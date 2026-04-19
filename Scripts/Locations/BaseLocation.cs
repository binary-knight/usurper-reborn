using UsurperRemake.Utils;
using UsurperRemake.Systems;
using UsurperRemake.Locations;
using UsurperRemake.Data;
using UsurperRemake.UI;
using UsurperRemake.BBS;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;

/// <summary>
/// Base location class for all game locations
/// Based on Pascal location system from ONLINE.PAS
/// </summary>
public abstract class BaseLocation
{
    public GameLocation LocationId { get; protected set; }
    public string Name { get; protected set; } = "";
    public string Description { get; protected set; } = "";
    public List<GameLocation> PossibleExits { get; protected set; } = new();
    public List<NPC> LocationNPCs { get; protected set; } = new();
    public List<string> LocationActions { get; protected set; } = new();

    // Pascal compatibility
    public bool RefreshRequired { get; set; } = true;

    protected TerminalEmulator terminal = null!;
    protected Character currentPlayer = null!;

    // NPC approach tracking - prevents spam from same NPC
    private static readonly Dictionary<string, int> _lastApproachedTurn = new();
    private const int MinTurnsBetweenApproaches = 10;

    /// <summary>When true, the next loop iteration skips DisplayLocation() redraw. Used for chat commands.</summary>
    protected bool _skipNextRedraw;

    /// <summary>MUD streaming mode: true after the location banner has been shown once on entry.
    /// Prevents full-screen redraws on every loop iteration so output flows like a real MUD.</summary>
    private bool _locationEntryDisplayed = false;

    /// <summary>
    /// Signal that DisplayLocation() should run on the next loop iteration.
    /// Use in MUD streaming mode when content changes substantially (e.g. dungeon room navigation,
    /// floor changes, or returning from a sub-menu that changed the view).
    /// </summary>
    protected void RequestRedisplay() => _locationEntryDisplayed = false;

    // Companion death trigger tracking — once per game day
    private static int _lastCompanionDeathCheckDay = -1;

    // World boss notification tracking — static so it persists across location changes
    private static string? _lastNotifiedBossName = null;
    private static DateTime _lastBossNotifyTime = DateTime.MinValue;

    // Ambient message state (MUD mode only)
    private DateTime _lastAmbientTime = DateTime.MinValue;
    private int _ambientIndex = 0;
    private static readonly Random _ambientRng = new();

    // Co-presence cache: other online players at this location (MUD mode only, 15s TTL)
    private List<UsurperRemake.Systems.OnlinePlayerInfo> _coPresenceCache = new();
    private DateTime _coPresenceCacheTime = DateTime.MinValue;

    /// <summary>
    /// True when this session should use compact BBS menus (80x24 terminal).
    /// Covers both single-player BBS door mode and MUD server BBS connections.
    /// </summary>
    protected static bool IsBBSSession
    {
        get
        {
            if (GameConfig.CompactMode) return true;
            if (DoorMode.IsInDoorMode) return true;
            var ctx = UsurperRemake.Server.SessionContext.Current;
            return ctx?.ConnectionType == "BBS";
        }
    }

    public BaseLocation(GameLocation locationId, string name, string description)
    {
        LocationId = locationId;
        Name = name;
        Description = description;
        SetupLocation();
    }

    /// <summary>
    /// Returns the current player. Shadows the global LegacyUI.GetCurrentPlayer() nullable version
    /// since we always have a valid player when inside a location.
    /// </summary>
    protected Character GetCurrentPlayer() => currentPlayer ?? GameEngine.Instance.CurrentPlayer;

    /// <summary>
    /// Localized Loc.Get("ui.your_choice") prompt. Use instead of terminal.GetInput(Loc.Get("ui.your_choice")).
    /// </summary>
    protected static string GetOrientationLabel(SexualOrientation orientation) => orientation switch
    {
        SexualOrientation.Straight => Loc.Get("base.orientation_straight"),
        SexualOrientation.Gay => Loc.Get("base.orientation_gay"),
        SexualOrientation.Bisexual => Loc.Get("base.orientation_bisexual"),
        SexualOrientation.Asexual => Loc.Get("base.orientation_asexual"),
        _ => Loc.Get("base.orientation_straight")
    };

    protected async Task<string> GetChoice()
    {
        return await terminal.GetInput(Loc.Get("ui.your_choice"));
    }

    /// <summary>
    /// Setup location-specific data (exits, NPCs, actions)
    /// </summary>
    protected virtual void SetupLocation()
    {
        // Override in derived classes
    }

    /// <summary>
    /// Enter the location - main entry point
    /// </summary>
    public virtual async Task EnterLocation(Character player, TerminalEmulator term)
    {
        currentPlayer = player;
        terminal = term;

        // Log location entry
        UsurperRemake.Systems.DebugLogger.Instance.LogInfo("LOCATION", $"Entered {Name} (ID: {LocationId})");

        // Update player location
        player.Location = (int)LocationId;
        player.CurrentLocation = Name;

        // Clear Safe House protection when player moves to any location
        if (player.SafeHouseResting)
            player.SafeHouseResting = false;

        // Update online presence with current location
        if (UsurperRemake.Systems.OnlineStateManager.IsActive)
            UsurperRemake.Systems.OnlineStateManager.Instance!.UpdateLocation(Name);

        // Track location visit statistics
        player.Statistics?.RecordLocationVisit(Name);

        // Sync player King/CTurf state with world state (catches background sim changes)
        SyncPlayerWorldState(player);

        // Immortal players are locked to the Pantheon (v0.46.0)
        if (player.IsImmortal && LocationId != GameLocation.Pantheon)
        {
            terminal.SetColor("bright_yellow");
            terminal.WriteLine("");
            terminal.WriteLine($"  {Loc.Get("base.immortal_locked")}");
            terminal.WriteLine("");
            await terminal.PressAnyKey();
            throw new LocationExitException(GameLocation.Pantheon);
        }

        // Check if this establishment has been closed by royal decree
        if (IsClosedByRoyalDecree())
        {
            var king = CastleLocation.GetCurrentKing();
            terminal.SetColor("red");
            terminal.WriteLine("");
            terminal.WriteLine($"  {Loc.Get("base.establishment_closed")}");
            terminal.SetColor("gray");
            if (king != null)
                terminal.WriteLine($"  {Loc.Get("base.establishment_closed_by", king.GetTitle(), king.Name)}");
            terminal.WriteLine("");
            await terminal.PressAnyKey();
            throw new LocationExitException(GameLocation.MainStreet);
        }

        // MUD mode: show other players at this location
        if (UsurperRemake.Server.SessionContext.IsActive && UsurperRemake.Server.RoomRegistry.Instance != null)
        {
            var otherPlayers = UsurperRemake.Server.RoomRegistry.Instance.GetPlayerNamesAt(LocationId, player.DisplayName);
            if (otherPlayers.Count > 0)
            {
                term.SetColor("cyan");
                term.WriteLine($"  {Loc.Get("base.also_here")}: {string.Join(", ", otherPlayers)}");
                term.SetColor("white");
            }
        }

        // Check for achievements on location entry (catches non-combat achievements)
        AchievementSystem.CheckAchievements(player);
        await AchievementSystem.ShowPendingNotifications(term, player);

        // Show any pending game notifications (team events, etc.)
        await ShowPendingGameNotifications(term);

        // Player reputation whisper effect (v0.42.0 - Social Emergence)
        await CheckReputationWhispers(player, term);

        // Ensure NPCs are initialized (safety check)
        if (NPCSpawnSystem.Instance.ActiveNPCs.Count == 0)
        {
            await NPCSpawnSystem.Instance.InitializeClassicNPCs();
        }

        // Check for guard defense alert - player may be a royal guard who needs to defend!
        await CheckGuardDefenseAlert();

        // Main location loop
        await LocationLoop();
    }

    /// <summary>
    /// Show any pending game notifications (team events, important world events, etc.)
    /// </summary>
    private async Task ShowPendingGameNotifications(TerminalEmulator term)
    {
        if (GameEngine.PendingNotifications.Count == 0) return;

        var notifications = new List<string>();
        while (GameEngine.PendingNotifications.Count > 0)
        {
            notifications.Add(GameEngine.PendingNotifications.Dequeue());
        }

        term.WriteLine("");
        WriteBoxHeader(Loc.Get("base.important_news"), "bright_yellow");
        term.WriteLine("");

        foreach (var notification in notifications)
        {
            term.SetColor("white");
            term.WriteLine($"  {notification}");
        }

        term.WriteLine("");

        await term.PressAnyKey();
    }

    /// <summary>
    /// Sync player King and CTurf flags with actual world state.
    /// Background simulation can change the king or city control without updating the player directly.
    /// </summary>
    /// <summary>
    /// Map location types to king establishment keys for royal decree closures
    /// </summary>
    private static readonly Dictionary<Type, string> EstablishmentTypeMap = new()
    {
        { typeof(InnLocation), "Inn" },
        { typeof(WeaponShopLocation), "WeaponShop" },
        { typeof(ArmorShopLocation), "ArmorShop" },
        { typeof(BankLocation), "Bank" },
        { typeof(MagicShopLocation), "MagicShop" },
        { typeof(HealerLocation), "Healer" },
        { typeof(UsurperRemake.Locations.MarketplaceLocation), "AuctionHouse" },
        { typeof(UsurperRemake.Locations.ChurchLocation), "Church" },
    };

    /// <summary>
    /// Check if the king has closed this establishment by royal decree
    /// </summary>
    private bool IsClosedByRoyalDecree()
    {
        if (!EstablishmentTypeMap.TryGetValue(GetType(), out var estKey))
            return false; // Not a closeable establishment

        var king = CastleLocation.GetCurrentKing();
        if (king == null) return false;

        if (king.EstablishmentStatus.TryGetValue(estKey, out bool isOpen))
            return !isOpen;

        return false; // Not in dictionary = default open
    }

    private void SyncPlayerWorldState(Character player)
    {
        try
        {
            // Sync King flag: if player thinks they're king but they're not
            if (player.King)
            {
                var currentKing = CastleLocation.GetCurrentKing();
                if (currentKing == null || !currentKing.IsActive ||
                    (currentKing.Name != player.DisplayName && currentKing.Name != player.Name2))
                {
                    player.King = false;
                    player.RoyalMercenaries?.Clear(); // Dismiss bodyguards on dethronement
                    player.RecalculateStats(); // Remove Royal Authority HP bonus
                    string newRuler = currentKing?.Name ?? "nobody";

                    // Notify the player they lost the throne
                    var term = GameEngine.Instance?.Terminal;
                    if (term != null)
                    {
                        term.SetColor("bright_red");
                        term.WriteLine("");
                        if (!GameConfig.ScreenReaderMode)
                            term.WriteLine("═══════════════════════════════════════════════════════════");
                        term.WriteLine($"  {Loc.Get("base.deposed")}");
                        if (currentKing != null && currentKing.IsActive)
                            term.WriteLine($"  {Loc.Get("base.throne_belongs_to", currentKing.Name)}");
                        else
                            term.WriteLine($"  {Loc.Get("base.throne_vacant")}");
                        if (!GameConfig.ScreenReaderMode)
                            term.WriteLine("═══════════════════════════════════════════════════════════");
                        term.WriteLine("");
                        term.SetColor("white");
                    }
                }
            }

            // Sync CTurf flag: derive from whether NPC teammates actually hold turf
            if (!string.IsNullOrEmpty(player.Team))
            {
                var npcs = NPCSpawnSystem.Instance?.ActiveNPCs;
                bool teamHasTurf = npcs != null &&
                    npcs.Any(n => n.Team == player.Team && n.CTurf && n.IsAlive && !n.IsDead);
                if (player.CTurf != teamHasTurf)
                {
                    player.CTurf = teamHasTurf;
                }
            }
            else if (player.CTurf)
            {
                // Player has CTurf but no team — can't control city without a team
                player.CTurf = false;
            }
        }
        catch (Exception ex)
        {
            // Non-critical sync — don't break location entry
            DebugLogger.Instance.LogError("LOCATION", $"[SyncPlayerWorldState] World state sync failed: {ex.Message}");
        }
    }

    /// <summary>
    /// Show subtle reputation whisper when player enters a location with NPCs who've heard about them (v0.42.0)
    /// </summary>
    private Task CheckReputationWhispers(Character player, TerminalEmulator term)
    {
        try
        {
            var playerName = player?.Name2 ?? player?.DisplayName ?? "";
            if (string.IsNullOrEmpty(playerName)) return Task.CompletedTask;

            // Find NPCs at this location who've heard about the player through gossip
            var npcsHere = NPCSpawnSystem.Instance.ActiveNPCs
                .Where(n => n.IsAlive && !n.IsDead && n.CurrentLocation == Name)
                .ToList();

            int gossipAwareCount = 0;
            float averageImpression = 0f;

            foreach (var npc in npcsHere)
            {
                var impression = npc.Brain?.Memory?.GetCharacterImpression(playerName) ?? 0f;
                if (Math.Abs(impression) > GameConfig.ReputationThresholdForReaction)
                {
                    // Check if they know about the player through gossip
                    var memories = npc.Brain?.Memory?.GetMemoriesAboutCharacter(playerName);
                    if (memories != null && memories.Any(m => m.Type == MemoryType.HeardGossip))
                    {
                        gossipAwareCount++;
                        averageImpression += impression;
                    }
                }
            }

            if (gossipAwareCount < 2) return Task.CompletedTask; // Need at least 2 NPCs whispering

            averageImpression /= gossipAwareCount;

            // Show the whisper message
            term.SetColor("gray");
            if (averageImpression < -0.3f)
                term.WriteLine($"  {Loc.Get("base.whisper_uneasy")}");
            else if (averageImpression > 0.3f)
                term.WriteLine($"  {Loc.Get("base.whisper_approving")}");
            else
                term.WriteLine($"  {Loc.Get("base.whisper_neutral")}");
            term.SetColor("white");
        }
        catch (Exception ex)
        {
            // Non-critical — don't let reputation whispers break location entry
            DebugLogger.Instance.LogError("LOCATION", $"[CheckReputationWhispers] Reputation whisper failed: {ex.Message}");
        }
        return Task.CompletedTask;
    }

    /// <summary>
    /// Check if player is a royal guard and the throne is under attack
    /// </summary>
    protected virtual async Task CheckGuardDefenseAlert()
    {
        try
        {
            var king = CastleLocation.GetCurrentKing();
            if (king?.ActiveDefenseEvent == null) return;
            if (king.ActiveDefenseEvent.PlayerNotified) return;

            // Check if current player is a royal guard
            var playerGuard = king.Guards.FirstOrDefault(g =>
                g.Name.Equals(currentPlayer.DisplayName, StringComparison.OrdinalIgnoreCase) ||
                g.Name.Equals(currentPlayer.Name2, StringComparison.OrdinalIgnoreCase));

            if (playerGuard == null) return;

            // Player is a guard - notify them!
            king.ActiveDefenseEvent.PlayerNotified = true;

            terminal.ClearScreen();
            terminal.WriteLine("");
            WriteBoxHeader(Loc.Get("base.castle_under_attack"), "bright_red");
            terminal.WriteLine("");

            terminal.SetColor("yellow");
            terminal.WriteLine(Loc.Get("base.guard_messenger"));
            terminal.WriteLine("");
            terminal.SetColor("white");
            terminal.WriteLine($"{king.ActiveDefenseEvent.ChallengerName} ({Loc.Get("base.guard_level")} {king.ActiveDefenseEvent.ChallengerLevel})");
            terminal.WriteLine(Loc.Get("base.guard_challenging", king.GetTitle(), king.Name));
            terminal.WriteLine("");

            terminal.SetColor("cyan");
            terminal.WriteLine(Loc.Get("base.guard_honor_bound"));
            terminal.WriteLine(Loc.Get("base.guard_rush_question"));
            terminal.WriteLine("");

            terminal.SetColor("yellow");
            terminal.Write(Loc.Get("base.guard_rush_prompt"));
            terminal.SetColor("white");

            string response = await terminal.ReadLineAsync();

            if (response?.ToUpper() == "Y")
            {
                king.ActiveDefenseEvent.PlayerResponded = true;
                terminal.SetColor("bright_green");
                terminal.WriteLine("");
                terminal.WriteLine(Loc.Get("base.guard_rush_castle"));
                terminal.WriteLine(Loc.Get("base.guard_loyalty_unquestioned"));
                await terminal.PressAnyKey();

                // Transport player to castle for defense
                // The player will participate in the defense when they arrive
                await GameEngine.Instance.NavigateToLocation(GameLocation.Castle);
            }
            else
            {
                // Player refused - severe loyalty penalty
                playerGuard.Loyalty = Math.Max(0, playerGuard.Loyalty - 25);

                terminal.SetColor("red");
                terminal.WriteLine("");
                terminal.WriteLine(Loc.Get("base.guard_turn_away"));
                terminal.WriteLine(Loc.Get("base.guard_crown_remembers"));
                terminal.WriteLine(Loc.Get("base.guard_loyalty_dropped", playerGuard.Loyalty));

                if (playerGuard.Loyalty <= 20)
                {
                    terminal.SetColor("bright_red");
                    terminal.WriteLine("");
                    terminal.WriteLine(Loc.Get("base.guard_stripped"));
                    king.Guards.Remove(playerGuard);
                    NewsSystem.Instance?.Newsy(true, Loc.Get("base.news_guard_dismissed", playerGuard.Name));
                }

                await terminal.PressAnyKey();
            }
        }
        catch (Exception ex)
        {
            // King system not available - ignore
            DebugLogger.Instance.LogError("LOCATION", $"[CheckGuardDefenseAlert] King system guard check failed: {ex.Message}");
        }
    }

    /// <summary>
    /// Main location loop - handles display and user input
    /// </summary>
    protected virtual async Task LocationLoop()
    {
        bool exitLocation = false;

        // Check for encounters when first entering location
        if (ShouldCheckForEncounters())
        {
            // Priority: consequence encounters (grudges, jealous spouses, throne challengers)
            var consequenceResult = await StreetEncounterSystem.Instance
                .CheckForConsequenceEncounter(currentPlayer, LocationId, terminal);

            if (consequenceResult.EncounterOccurred)
            {
                if (!currentPlayer.IsAlive)
                    return;
            }
            else
            {
                // Normal random encounter (only if no consequence encounter fired)
                var encounterResult = await StreetEncounterSystem.Instance.CheckForEncounter(
                    currentPlayer, LocationId, terminal);

                if (encounterResult.EncounterOccurred)
                {
                    if (!currentPlayer.IsAlive)
                        return;
                }
            }
        }

        // Check for narrative encounters (Stranger, Town NPCs)
        await CheckNarrativeEncounters();

        // Check for NPC petitions (world-state-driven encounters)
        if (currentPlayer.IsAlive && NPCPetitionSystem.Instance != null)
            await NPCPetitionSystem.Instance.CheckForPetition(currentPlayer, LocationId, terminal);

        // Reset on every location entry so the banner always shows once on arrival
        _locationEntryDisplayed = false;

        while (!exitLocation && currentPlayer.IsAlive) // No turn limit - continuous gameplay
        {
            // Nightmare permadeath — save deleted, exit immediately
            if (GameEngine.Instance.IsPermadeath)
                return;

            // Royal arrest — if guards seized you mid-game, redirect to prison
            if (currentPlayer.DaysInPrison > 0 && !(this is PrisonLocation) && !(this is PrisonWalkLocation))
            {
                terminal.SetColor("bright_red");
                terminal.WriteLine("");
                terminal.WriteLine($"  {Loc.Get("base.royal_guards_surround")}");
                terminal.WriteLine($"  {Loc.Get("base.under_arrest")}");
                terminal.WriteLine("");
                await terminal.PressAnyKey();
                await NavigateToLocation(GameLocation.Prison);
                return;
            }

            // Auto-level-up check — catches ALL XP sources (combat, quests, seals, events, etc.)
            if (currentPlayer != null && currentPlayer.AI == CharacterAI.Human && currentPlayer.AutoLevelUp)
            {
                int levelsGained = LevelMasterLocation.CheckAutoLevelUp(currentPlayer);
                if (levelsGained > 0)
                {
                    terminal.WriteLine("");
                    terminal.SetColor("bright_green");
                    terminal.WriteLine($"  {Loc.Get("base.level_up", currentPlayer.Level)}");
                    terminal.SetColor("yellow");
                    if (currentPlayer.TrainingPoints > 0)
                        terminal.WriteLine($"  {Loc.Get("base.training_points_hint")}");
                    terminal.SetColor("white");
                    terminal.WriteLine("");

                    // Show Level Master hint on first level-up
                    HintSystem.Instance.TryShowHint(HintSystem.HINT_LEVEL_MASTER, terminal, currentPlayer.HintsShown);

                    await terminal.PressAnyKey();
                }
            }

            // Show deferred daily reset banner at a clean display boundary
            // (instead of mid-shop or mid-interaction where PeriodicUpdate fires)
            if (DailySystemManager.Instance.PendingDailyResetDisplay)
            {
                DailySystemManager.Instance.PendingDailyResetDisplay = false;
                await DailySystemManager.Instance.DisplayDailyResetMessage();
            }

            // Companion death triggers (once per game day)
            if (currentPlayer != null && CompanionSystem.Instance != null)
            {
                int gameDay = GameEngine.Instance?.SessionCurrentDay ?? 0;
                if (gameDay > _lastCompanionDeathCheckDay)
                {
                    _lastCompanionDeathCheckDay = gameDay;
                    var deathCheck = CompanionSystem.Instance.CheckDeathTriggers(currentPlayer);
                    if (deathCheck?.TriggeredCompanion != null && deathCheck.TriggerType.HasValue)
                    {
                        await CompanionSystem.Instance.KillCompanion(
                            deathCheck.TriggeredCompanion.Value,
                            deathCheck.TriggerType.Value,
                            deathCheck.TriggerReason ?? "Unknown",
                            terminal);
                    }
                }
            }

            // In MUD streaming mode: show the full location display only on the first iteration
            // (or after `look`/`l` resets the flag). Subsequent iterations just flow output
            // continuously without wiping the scroll buffer — real MUD behaviour.
            bool showDisplay = !_skipNextRedraw &&
                (!UsurperRemake.BBS.DoorMode.IsMudServerMode || !_locationEntryDisplayed);
            _skipNextRedraw = false;

            // Refresh co-presence player cache every 15s (MUD mode only)
            if (UsurperRemake.BBS.DoorMode.IsMudServerMode &&
                UsurperRemake.Systems.OnlineStateManager.IsActive &&
                (DateTime.Now - _coPresenceCacheTime).TotalSeconds >= 15)
            {
                var allPlayers = await UsurperRemake.Systems.OnlineStateManager.Instance!.GetOnlinePlayers();
                _coPresenceCache = allPlayers
                    .Where(p => p.Location == Name && p.DisplayName != (currentPlayer?.Name2 ?? ""))
                    .ToList();
                _coPresenceCacheTime = DateTime.Now;
            }

            if (showDisplay)
            {
                // Autosave BEFORE displaying location (save stable state)
                // This ensures we don't save during quit/exit actions
                if (currentPlayer != null)
                {
                    await SaveSystem.Instance.AutoSave(currentPlayer);
                }

                // Display location
                DisplayLocation();
                _locationEntryDisplayed = true;

                // Immersion text — overheard NPC dialogue and world-state flavor
                ShowImmersionText();

                // Co-presence: show other online players at this location (MUD mode only)
                if (UsurperRemake.BBS.DoorMode.IsMudServerMode && _coPresenceCache.Count > 0)
                {
                    terminal.SetColor("cyan");
                    terminal.Write(Loc.Get("base.also_here") + ": ");
                    terminal.SetColor("bright_cyan");
                    terminal.WriteLine(string.Join(", ", _coPresenceCache.Select(p => p.DisplayName)));
                    terminal.WriteLine("");
                }
            }

            // Always drain pending messages — in streaming mode these must flow every
            // iteration, not only when the screen redraws
            if (OnlineChatSystem.IsActive)
            {
                OnlineChatSystem.Instance!.DisplayPendingMessages(terminal);
            }

            // MUD mode: drain incoming room/system messages (arrival/departure, chat, etc.)
            if (UsurperRemake.Server.SessionContext.IsActive)
            {
                var ctx = UsurperRemake.Server.SessionContext.Current;
                var session = ctx != null ? UsurperRemake.Server.MudServer.Instance?.ActiveSessions
                    .GetValueOrDefault(ctx.Username.ToLowerInvariant()) : null;
                if (session != null)
                {
                    while (session.IncomingMessages.TryDequeue(out var msg))
                    {
                        terminal.WriteLine(msg);
                    }
                }
            }

            // Show persistent broadcast banner if active (MUD mode)
            var broadcast = UsurperRemake.Server.MudServer.ActiveBroadcast;
            if (!string.IsNullOrEmpty(broadcast))
            {
                terminal.WriteLine("");
                terminal.WriteLine($"*** {Loc.Get("base.system_message")}: {broadcast} ***", "bright_red");
            }

            // World boss active notification (online mode)
            // Shows once when a new boss spawns, then reminds every 5 minutes
            if (UsurperRemake.BBS.DoorMode.IsOnlineMode && currentPlayer.Level >= 10)
            {
                var activeBossName = WorldBossSystem.Instance.ActiveBossName;
                if (!string.IsNullOrEmpty(activeBossName))
                {
                    bool isNewBoss = _lastNotifiedBossName != activeBossName;
                    bool reminderDue = (DateTime.Now - _lastBossNotifyTime).TotalMinutes >= 5;
                    if (isNewBoss || reminderDue)
                    {
                        _lastNotifiedBossName = activeBossName;
                        _lastBossNotifyTime = DateTime.Now;
                        terminal.WriteLine("");
                        terminal.SetColor("bright_red");
                        terminal.WriteLine($"  *** {Loc.Get("base.world_boss_rampaging", activeBossName)} ***");
                    }
                }
                else
                {
                    // Boss died or despawned — reset so next boss triggers immediately
                    _lastNotifiedBossName = null;
                }
            }

            // Ambient messages (MUD mode only) — fire every 30-60s, flowing inline
            if (UsurperRemake.BBS.DoorMode.IsMudServerMode)
            {
                var ambientPool = GetAmbientMessages();
                if (ambientPool != null && ambientPool.Length > 0)
                {
                    if ((DateTime.Now - _lastAmbientTime).TotalSeconds >= 30 + _ambientRng.Next(31))
                    {
                        terminal.WriteLine(ambientPool[_ambientIndex % ambientPool.Length], "gray");
                        _ambientIndex++;
                        _lastAmbientTime = DateTime.Now;
                    }
                }
            }

            // Get user choice
            var choice = await GetUserChoice();

            // Process choice
            exitLocation = await ProcessChoice(choice);

            // Increment turn count and advance game time
            if (currentPlayer != null && !string.IsNullOrWhiteSpace(choice))
            {
                currentPlayer.TurnCount++;

                // Apply poison damage each turn
                // Dungeon suppresses this — it handles poison ticks on room movement instead,
                // so invalid keys and guide navigation don't double-tick poison
                if (!SuppressBasePoisonTick)
                    await ApplyPoisonDamage();

                // 5% chance per turn: an NPC with strong opinions approaches the player
                if (!exitLocation && currentPlayer.IsAlive && _npcRandom.Next(100) < 5)
                {
                    await TryNPCApproach();
                }

                // Advance game time and run world sim on hour boundaries (single-player)
                // Online mode keeps the old turn-based trigger
                if (!UsurperRemake.BBS.DoorMode.IsOnlineMode)
                {
                    int hoursCrossed = DailySystemManager.Instance.AdvanceGameTime(
                        currentPlayer, GameConfig.MinutesPerAction);
                    for (int i = 0; i < hoursCrossed; i++)
                    {
                        await RunWorldSimulationTick();
                    }

                    // Show atmospheric time transition message if period changed
                    bool inDungeon = this is DungeonLocation;
                    var transition = DailySystemManager.Instance.CheckTimeTransition(currentPlayer, inDungeon);
                    if (transition != null)
                    {
                        terminal.WriteLine("");
                        terminal.SetColor(DailySystemManager.GetTimePeriodColor(currentPlayer));
                        terminal.WriteLine(transition);
                    }
                }
                else
                {
                    // Online mode: world simulation every 5 turns (legacy behavior)
                    if (currentPlayer.TurnCount % 5 == 0)
                    {
                        await RunWorldSimulationTick();
                    }
                }
            }
        }
    }

    /// <summary>
    /// When true, the base location loop skips its per-input poison tick.
    /// Dungeon overrides this because it handles poison on room movement instead.
    /// </summary>
    protected virtual bool SuppressBasePoisonTick => false;

    /// <summary>
    /// Check if this location should have random encounters
    /// </summary>
    protected virtual bool ShouldCheckForEncounters()
    {
        // Most locations have encounters; override in safe locations
        return LocationId switch
        {
            GameLocation.Home => false,           // Safe zone
            GameLocation.Bank => false,           // Guards present, very safe
            GameLocation.Church => false,         // Sacred ground
            GameLocation.Temple => false,         // Sacred ground
            GameLocation.Dungeons => false,       // Has own encounter system
            GameLocation.Prison => false,         // Special handling
            GameLocation.Master => false,         // Level master's sanctum
            _ => true                             // Other locations have encounters
        };
    }

    /// <summary>
    /// Check for narrative encounters (Stranger and Town NPC stories)
    /// </summary>
    protected virtual async Task CheckNarrativeEncounters()
    {
        if (currentPlayer == null || terminal == null) return;

        var locationName = LocationId.ToString();

        // Track player actions for Stranger encounter system
        StrangerEncounterSystem.Instance.OnPlayerAction(locationName, currentPlayer);

        // Queue level-gated scripted encounters if conditions are met
        var strangerSys = StrangerEncounterSystem.Instance;
        if (currentPlayer.Level >= 40 && strangerSys.EncountersHad >= 3 &&
            !strangerSys.CompletedScriptedEncounters.Contains(ScriptedEncounterType.TheMidgameLesson))
        {
            strangerSys.QueueScriptedEncounter(ScriptedEncounterType.TheMidgameLesson);
        }

        int resolvedGods = StoryProgressionSystem.Instance?.OldGodStates?
            .Count(g => g.Value.Status != GodStatus.Unknown) ?? 0;
        if (currentPlayer.Level >= 55 && strangerSys.EncountersHad >= 5 && resolvedGods >= 2 &&
            !strangerSys.CompletedScriptedEncounters.Contains(ScriptedEncounterType.TheRevelation))
        {
            strangerSys.QueueScriptedEncounter(ScriptedEncounterType.TheRevelation);
        }

        // Check for SCRIPTED Stranger encounters first (guaranteed, event-triggered)
        var scriptedEncounter = StrangerEncounterSystem.Instance.GetPendingScriptedEncounter(locationName, currentPlayer);
        if (scriptedEncounter != null)
        {
            await DisplayScriptedStrangerEncounter(scriptedEncounter);
            return; // One encounter per visit
        }

        // Check for random contextual Stranger encounters
        if (StrangerEncounterSystem.Instance.ShouldTriggerRandomEncounter(locationName, currentPlayer))
        {
            var encounter = StrangerEncounterSystem.Instance.GetContextualEncounter(locationName, currentPlayer);
            if (encounter != null)
            {
                await DisplayStrangerEncounter(encounter);
                return; // One encounter per visit
            }
        }

        // Check for memorable NPC encounters (Town NPC stories)
        var npcEncounter = TownNPCStorySystem.Instance.GetAvailableNPCEncounter(locationName, currentPlayer);
        if (npcEncounter != null)
        {
            var npcKey = TownNPCStorySystem.MemorableNPCs.FirstOrDefault(kvp => kvp.Value == npcEncounter).Key;
            var stage = TownNPCStorySystem.Instance.GetNextStage(npcKey, currentPlayer);
            if (stage != null)
            {
                await DisplayTownNPCEncounter(npcEncounter, stage, npcKey);
            }
        }
    }

    /// <summary>
    /// Display a contextual random Stranger (Noctura) encounter with response tracking
    /// </summary>
    private async Task DisplayStrangerEncounter(StrangerEncounter encounter)
    {
        terminal.ClearScreen();
        WriteBoxHeader(Loc.Get("base.mysterious_encounter"), "dark_magenta");
        terminal.WriteLine("");

        terminal.SetColor("white");
        terminal.WriteLine($"  {encounter.DisguiseData.Name}");
        terminal.SetColor("gray");
        terminal.WriteLine($"  {encounter.DisguiseData.Description}");
        terminal.WriteLine("");

        await Task.Delay(1500);

        // Display dialogue lines with pacing
        var dialogueLines = encounter.Dialogue.Split('\n');
        foreach (var line in dialogueLines)
        {
            terminal.SetColor("bright_magenta");
            terminal.WriteLine($"  {line}");
            await Task.Delay(800);
        }
        terminal.WriteLine("");

        await Task.Delay(500);

        // Display response options from the encounter's ResponseOptions
        var responseType = StrangerResponseType.Silent;
        int receptivityChange = 0;

        if (encounter.ResponseOptions != null && encounter.ResponseOptions.Count > 0)
        {
            terminal.SetColor("cyan");
            terminal.WriteLine(Loc.Get("base.how_respond"));
            terminal.WriteLine("");

            foreach (var opt in encounter.ResponseOptions)
            {
                if (IsScreenReader)
                {
                    WriteSRMenuOption(opt.Key, opt.Text);
                }
                else
                {
                    terminal.SetColor("white");
                    terminal.Write("    [");
                    terminal.SetColor("bright_yellow");
                    terminal.Write(opt.Key);
                    terminal.SetColor("white");
                    terminal.Write("] ");
                    terminal.SetColor("white");
                    terminal.WriteLine(opt.Text);
                }
            }

            terminal.WriteLine("");
            var choice = await terminal.GetInput(Loc.Get("base.your_response"));

            var selectedOpt = encounter.ResponseOptions
                .FirstOrDefault(o => o.Key.Equals(choice?.Trim(), StringComparison.OrdinalIgnoreCase));

            if (selectedOpt != null)
            {
                responseType = selectedOpt.ResponseType;
                receptivityChange = selectedOpt.ReceptivityChange;

                terminal.WriteLine("");
                foreach (var replyLine in selectedOpt.StrangerReply)
                {
                    terminal.SetColor("magenta");
                    terminal.WriteLine($"  {replyLine}");
                    await Task.Delay(800);
                }
                terminal.WriteLine("");
            }
        }
        else
        {
            // Fallback for encounters without structured response options
            var options = StrangerEncounterSystem.Instance.GetResponseOptions(encounter, currentPlayer);

            terminal.SetColor("cyan");
            terminal.WriteLine(Loc.Get("base.how_respond"));
            terminal.WriteLine("");

            foreach (var (key, text, _) in options)
            {
                terminal.SetColor("white");
                terminal.Write("    [");
                terminal.SetColor("bright_yellow");
                terminal.Write(key);
                terminal.SetColor("white");
                terminal.Write("] ");
                terminal.SetColor("white");
                terminal.WriteLine(text);
            }

            terminal.WriteLine("");
            var choice = await terminal.GetInput(Loc.Get("base.your_response"));

            var selectedOption = options.FirstOrDefault(o => o.key.Equals(choice, StringComparison.OrdinalIgnoreCase));
            if (selectedOption.response != null)
            {
                terminal.WriteLine("");
                terminal.SetColor("magenta");
                terminal.WriteLine($"  {selectedOption.response}");
                terminal.WriteLine("");
            }
        }

        // Record the encounter with response tracking
        StrangerEncounterSystem.Instance.RecordEncounterWithResponse(encounter, responseType, receptivityChange);

        await terminal.PressAnyKey();
    }

    /// <summary>
    /// Display a scripted Stranger encounter (guaranteed story beats with full narration)
    /// </summary>
    private async Task DisplayScriptedStrangerEncounter(ScriptedStrangerEncounter encounter)
    {
        terminal.ClearScreen();
        WriteBoxHeader(encounter.Title, "dark_magenta");
        terminal.WriteLine("");

        // Intro narration (atmospheric, gray)
        foreach (var line in encounter.IntroNarration)
        {
            terminal.SetColor("gray");
            terminal.WriteLine($"  {line}");
            await Task.Delay(1200);
        }
        terminal.WriteLine("");
        await Task.Delay(500);

        // Disguise name
        var disguiseData = StrangerEncounterSystem.Disguises.GetValueOrDefault(encounter.Disguise);
        if (disguiseData != null)
        {
            terminal.SetColor("white");
            terminal.WriteLine($"  {disguiseData.Name}");
            terminal.SetColor("darkgray");
            terminal.WriteLine($"  {disguiseData.Description}");
            terminal.WriteLine("");
            await Task.Delay(800);
        }

        // Main dialogue (bright magenta, spoken lines)
        foreach (var line in encounter.Dialogue)
        {
            if (string.IsNullOrEmpty(line))
            {
                terminal.WriteLine("");
                await Task.Delay(400);
                continue;
            }
            terminal.SetColor("bright_magenta");
            terminal.WriteLine($"  {line}");
            await Task.Delay(1000);
        }
        terminal.WriteLine("");
        await Task.Delay(500);

        // Response choices
        var responseType = StrangerResponseType.Silent;
        int receptivityChange = 0;

        if (encounter.Responses.Count > 0)
        {
            terminal.SetColor("cyan");
            terminal.WriteLine(Loc.Get("base.how_respond"));
            terminal.WriteLine("");

            foreach (var opt in encounter.Responses)
            {
                terminal.SetColor("white");
                terminal.Write("    [");
                terminal.SetColor("bright_yellow");
                terminal.Write(opt.Key);
                terminal.SetColor("white");
                terminal.Write("] ");
                terminal.SetColor("white");
                terminal.WriteLine(opt.Text);
            }

            terminal.WriteLine("");
            var choice = await terminal.GetInput(Loc.Get("base.your_response"));

            var selectedOpt = encounter.Responses
                .FirstOrDefault(o => o.Key.Equals(choice?.Trim(), StringComparison.OrdinalIgnoreCase));

            if (selectedOpt != null)
            {
                responseType = selectedOpt.ResponseType;
                receptivityChange = selectedOpt.ReceptivityChange;

                terminal.WriteLine("");
                foreach (var replyLine in selectedOpt.StrangerReply)
                {
                    terminal.SetColor("magenta");
                    terminal.WriteLine($"  {replyLine}");
                    await Task.Delay(1000);
                }
                terminal.WriteLine("");
                await Task.Delay(500);
            }
        }

        // Closing narration
        if (encounter.ClosingNarration.Length > 0)
        {
            terminal.WriteLine("");
            foreach (var line in encounter.ClosingNarration)
            {
                terminal.SetColor("gray");
                terminal.WriteLine($"  {line}");
                await Task.Delay(1200);
            }
            terminal.WriteLine("");
        }

        // Record completion
        StrangerEncounterSystem.Instance.CompleteScriptedEncounter(
            encounter.Type, responseType, receptivityChange);

        await terminal.PressAnyKey();
    }

    /// <summary>
    /// Display a memorable Town NPC encounter
    /// </summary>
    private async Task DisplayTownNPCEncounter(MemorableNPCData npc, NPCStoryStage stage, string npcKey)
    {
        terminal.ClearScreen();
        string localName = TownNPCStorySystem.GetLocalizedName(npcKey, npc);
        string localTitle = TownNPCStorySystem.GetLocalizedTitle(npcKey, npc);
        WriteBoxHeader($"{localName.ToUpper()} - {localTitle.ToUpper()}", "bright_cyan");
        terminal.WriteLine("");

        terminal.SetColor("gray");
        terminal.WriteLine($"  {TownNPCStorySystem.GetLocalizedDescription(npcKey, npc)}");
        terminal.WriteLine("");

        await Task.Delay(1500);

        // Display dialogue
        terminal.SetColor("white");
        foreach (var line in TownNPCStorySystem.GetLocalizedDialogue(npcKey, stage))
        {
            terminal.WriteLine($"  {line}");
            await Task.Delay(1500);
        }
        terminal.WriteLine("");

        string? choiceMade = null;

        // Handle choice if present
        if (stage.Choice != null)
        {
            terminal.SetColor("yellow");
            terminal.WriteLine($"  {TownNPCStorySystem.GetLocalizedChoicePrompt(npcKey, stage)}");
            terminal.WriteLine("");

            foreach (var option in stage.Choice.Options)
            {
                terminal.SetColor("white");
                terminal.Write("    [");
                terminal.SetColor("bright_yellow");
                terminal.Write(option.Key);
                terminal.SetColor("white");
                terminal.Write("] ");
                terminal.SetColor("white");
                terminal.WriteLine(TownNPCStorySystem.GetLocalizedChoiceText(npcKey, stage, option));
            }
            terminal.WriteLine("");

            var input = await GetChoice();
            var selected = stage.Choice.Options.FirstOrDefault(o =>
                o.Key.Equals(input, StringComparison.OrdinalIgnoreCase));

            if (selected != null)
            {
                choiceMade = selected.Key;

                // Apply choice effects
                if (selected.GoldCost > 0 && currentPlayer.Gold >= selected.GoldCost)
                {
                    currentPlayer.Gold -= selected.GoldCost;
                    terminal.WriteLine(Loc.Get("base.you_paid_gold", selected.GoldCost), "yellow");
                }
                if (selected.Chivalry > 0)
                {
                    currentPlayer.Chivalry += selected.Chivalry;
                    terminal.WriteLine(Loc.Get("base.plus_chivalry", selected.Chivalry), "bright_green");
                }
                if (selected.Darkness > 0)
                {
                    currentPlayer.Darkness += selected.Darkness;
                    terminal.WriteLine(Loc.Get("base.plus_darkness", selected.Darkness), "dark_red");
                }
            }
        }

        // Apply rewards if present
        if (stage.Reward != null)
        {
            terminal.WriteLine("");
            terminal.SetColor("bright_green");

            if (stage.Reward.ChivalryBonus > 0)
            {
                currentPlayer.Chivalry += stage.Reward.ChivalryBonus;
                terminal.WriteLine(Loc.Get("base.reward_chivalry", stage.Reward.ChivalryBonus));
            }
            if (stage.Reward.Wisdom > 0)
            {
                currentPlayer.Wisdom += stage.Reward.Wisdom;
                terminal.WriteLine(Loc.Get("base.reward_wisdom", stage.Reward.Wisdom));
            }
            if (stage.Reward.Dexterity > 0)
            {
                currentPlayer.Dexterity += stage.Reward.Dexterity;
                terminal.WriteLine(Loc.Get("base.reward_dexterity", stage.Reward.Dexterity));
            }
            if (stage.Reward.WaveFragment.HasValue)
            {
                OceanPhilosophySystem.Instance.CollectFragment(stage.Reward.WaveFragment.Value);
                terminal.WriteLine(Loc.Get("base.fragment_truth"));
            }
            if (stage.Reward.AwakeningMoment.HasValue)
            {
                OceanPhilosophySystem.Instance.ExperienceMoment(stage.Reward.AwakeningMoment.Value);
                terminal.WriteLine(Loc.Get("base.something_shifts"));
            }
        }

        // Apply awakening gain
        if (stage.AwakeningGain > 0)
        {
            OceanPhilosophySystem.Instance.GainInsight(stage.AwakeningGain * 10);
            terminal.SetColor("magenta");
            terminal.WriteLine(Loc.Get("base.deeper_understanding"));
        }

        // Apply gold loss if any
        if (stage.GoldLost > 0)
        {
            var actualLoss = Math.Min(stage.GoldLost, currentPlayer.Gold);
            currentPlayer.Gold -= actualLoss;
            terminal.SetColor("red");
            terminal.WriteLine(Loc.Get("base.minus_gold", actualLoss));
        }

        // Complete the stage
        TownNPCStorySystem.Instance.CompleteStage(npcKey, stage.StageId, choiceMade);

        terminal.WriteLine("");
        await terminal.PressAnyKey();
    }

    /// <summary>
    /// Run a tick of world simulation (NPCs act, world events, etc.)
    /// </summary>
    private async Task RunWorldSimulationTick()
    {
        // Run game engine's periodic update for world simulation
        var gameEngine = GameEngine.Instance;
        if (gameEngine != null)
        {
            await gameEngine.PeriodicUpdate();
        }

        // Check for alignment-based random events (5% chance per tick)
        if (currentPlayer != null && terminal != null)
        {
            await AlignmentSystem.Instance.CheckAlignmentEvent(currentPlayer, terminal);
        }
    }

    /// <summary>
    /// Apply poison damage each turn if player is poisoned
    /// </summary>
    protected async Task ApplyPoisonDamage()
    {
        if (currentPlayer == null || currentPlayer.Poison <= 0)
            return;

        // Migration: old saves have Poison > 0 but PoisonTurns == 0
        // Give them a reasonable duration based on poison intensity
        if (currentPlayer.PoisonTurns <= 0)
            currentPlayer.PoisonTurns = Math.Max(5, currentPlayer.Poison * 2);

        // Poison damage scales with poison level and player level
        // Base damage: 2-5 HP per turn, plus level scaling, plus poison intensity
        var random = Random.Shared;
        int baseDamage = 2 + random.Next(4);  // 2-5 base damage
        int levelScaling = currentPlayer.Level / 10;  // +1 per 10 levels
        int poisonBonus = currentPlayer.Poison / 5;  // +1 per 5 poison intensity
        int totalDamage = baseDamage + levelScaling + poisonBonus;

        // Cap damage at 10% of max HP to prevent instant deaths
        int maxDamage = (int)Math.Max(3, currentPlayer.MaxHP / 10);
        totalDamage = Math.Min(totalDamage, maxDamage);

        // Apply damage
        currentPlayer.HP -= totalDamage;

        // Tick down poison duration
        currentPlayer.PoisonTurns--;

        // Show poison damage message with remaining turns
        terminal.SetColor("magenta");
        if (currentPlayer.PoisonTurns > 0)
            terminal.WriteLine(Loc.Get("base.poison_damage_turns", totalDamage, currentPlayer.PoisonTurns));
        else
            terminal.WriteLine(Loc.Get("base.poison_damage", totalDamage));

        // Check if player died from poison
        if (currentPlayer.HP <= 0)
        {
            currentPlayer.HP = 0;
            terminal.SetColor("red");
            terminal.WriteLine(Loc.Get("base.poison_death"));
            await Task.Delay(1500);
        }
        else if (currentPlayer.PoisonTurns <= 0)
        {
            // Poison has expired
            currentPlayer.Poison = 0;
            terminal.SetColor("green");
            terminal.WriteLine(Loc.Get("base.poison_cleared"));
            await Task.Delay(800);
        }
        else
        {
            await Task.Delay(500);
        }
    }

    /// <summary>
    /// Display the location screen
    /// </summary>
    protected virtual void DisplayLocation()
    {
        terminal.ClearScreen();

        // Breadcrumb navigation
        ShowBreadcrumb();

        // Location header (with time-of-day for single-player, non-dungeon locations)
        terminal.SetColor("bright_yellow");
        if (!UsurperRemake.BBS.DoorMode.IsOnlineMode && currentPlayer != null
            && LocationId != GameLocation.Dungeons)
        {
            var timePeriod = DailySystemManager.GetTimePeriodString(currentPlayer);
            var timeColor = DailySystemManager.GetTimePeriodColor(currentPlayer);
            terminal.Write(Name);
            terminal.SetColor("gray");
            terminal.Write(" — ");
            terminal.SetColor(timeColor);
            terminal.Write(timePeriod);

            // Append fatigue tier label when Tired or Exhausted
            var (fatigueLabel, fatigueColor) = currentPlayer.GetFatigueTier();
            int headerLen = Name.Length + 3 + timePeriod.Length;
            if (!string.IsNullOrEmpty(fatigueLabel) && currentPlayer.Fatigue >= GameConfig.FatigueTiredThreshold)
            {
                terminal.SetColor("gray");
                terminal.Write(" (");
                terminal.SetColor(fatigueColor);
                terminal.Write(fatigueLabel);
                terminal.SetColor("gray");
                terminal.Write(")");
                headerLen += 3 + fatigueLabel.Length; // " (" + label + ")"
            }
            terminal.WriteLine("");

            if (!IsScreenReader)
            {
                terminal.SetColor("yellow");
                terminal.WriteLine(new string('═', headerLen));
            }
        }
        else
        {
            // Show fatigue in dungeon header (single-player only)
            if (!UsurperRemake.BBS.DoorMode.IsOnlineMode && currentPlayer != null
                && currentPlayer.Fatigue >= GameConfig.FatigueTiredThreshold)
            {
                var (fatigueLabel, fatigueColor) = currentPlayer.GetFatigueTier();
                terminal.Write(Name);
                terminal.SetColor("gray");
                terminal.Write(" (");
                terminal.SetColor(fatigueColor);
                terminal.Write(fatigueLabel);
                terminal.SetColor("gray");
                terminal.WriteLine(")");
                if (!IsScreenReader)
                {
                    terminal.SetColor("yellow");
                    terminal.WriteLine(new string('═', Name.Length + 3 + fatigueLabel.Length));
                }
            }
            else
            {
                terminal.WriteLine(Name);
                if (!IsScreenReader)
                {
                    terminal.SetColor("yellow");
                    terminal.WriteLine(new string('═', Name.Length));
                }
            }
        }

        // Blood Moon indicator (v0.52.0)
        if (currentPlayer != null && currentPlayer.IsBloodMoon)
        {
            terminal.SetColor("bright_red");
            if (GameConfig.ScreenReaderMode)
                terminal.WriteLine($"  {Loc.Get("base.blood_moon_sr")}");
            else
                terminal.WriteLine($"  {Loc.Get("base.blood_moon_visual")}");
        }

        terminal.WriteLine("");

        // Location description
        terminal.SetColor("white");
        terminal.WriteLine(Description);
        terminal.WriteLine("");

        // Show NPCs in location
        ShowNPCsInLocation();

        // Show available actions
        ShowLocationActions();

        // Show exits
        ShowExits();

        // Status line
        ShowStatusLine();
    }

    /// <summary>
    /// Show immersion text after location display — overheard NPC dialogue and world-state flavor.
    /// </summary>
    protected virtual void ShowImmersionText()
    {
        if (currentPlayer == null || terminal == null) return;

        // Dungeon has its own atmosphere system
        if (LocationId == GameLocation.Dungeons) return;

        // World-state flavor (always show if available — these are rare by nature)
        var worldFlavor = GetWorldStateFlavor();
        if (worldFlavor != null)
        {
            terminal.SetColor("gray");
            terminal.WriteLine($"  {worldFlavor}");
        }

        // NPC overheard dialogue (20% chance)
        if (Random.Shared.Next(100) < 20)
        {
            var overheard = GenerateOverheardDialogue();
            if (overheard != null)
            {
                terminal.SetColor("dark_cyan");
                terminal.WriteLine($"  {overheard}");
                terminal.SetColor("white");
            }
        }
    }

    /// <summary>
    /// Generate an overheard dialogue snippet between two NPCs at this location.
    /// Returns null if fewer than 2 NPCs are present.
    /// </summary>
    protected virtual string? GenerateOverheardDialogue()
    {
        var npcsHere = GetLiveNPCsAtLocation();
        if (npcsHere.Count < 2) return null;

        // Pick two random NPCs
        var shuffled = npcsHere.OrderBy(_ => Random.Shared.Next()).Take(2).ToList();
        var npc1 = shuffled[0];
        var npc2 = shuffled[1];

        // Location-specific dialogue templates
        var templates = LocationId switch
        {
            GameLocation.TheInn or GameLocation.BobsBeer or GameLocation.Orbs => new[]
            {
                Loc.Get("base.overhear_inn_1", npc1.Name, npc2.Name),
                Loc.Get("base.overhear_inn_2", npc1.Name, npc2.Name),
                Loc.Get("base.overhear_inn_3", npc2.Name, npc1.Name),
                Loc.Get("base.overhear_inn_4", npc1.Name, npc2.Name),
                Loc.Get("base.overhear_inn_5", npc1.Name, npc2.Name),
            },
            GameLocation.MainStreet => new[]
            {
                Loc.Get("base.overhear_street_1", npc1.Name, npc2.Name),
                Loc.Get("base.overhear_street_2", npc1.Name, npc2.Name),
                Loc.Get("base.overhear_street_3", npc1.Name, npc2.Name),
                Loc.Get("base.overhear_street_4", npc1.Name, npc2.Name),
                Loc.Get("base.overhear_street_5", npc2.Name, npc1.Name),
            },
            GameLocation.Temple or GameLocation.Church => new[]
            {
                Loc.Get("base.overhear_temple_1", npc1.Name, npc2.Name),
                Loc.Get("base.overhear_temple_2", npc1.Name, npc2.Name),
                Loc.Get("base.overhear_temple_3", npc2.Name, npc1.Name),
                Loc.Get("base.overhear_temple_4", npc1.Name, npc2.Name),
            },
            GameLocation.Healer => new[]
            {
                Loc.Get("base.overhear_healer_1", npc1.Name, npc2.Name),
                Loc.Get("base.overhear_healer_2", npc1.Name, npc2.Name),
                Loc.Get("base.overhear_healer_3", npc2.Name, npc1.Name),
            },
            _ => new[]
            {
                Loc.Get("base.overhear_default_1", npc1.Name, npc2.Name),
                Loc.Get("base.overhear_default_2", npc1.Name, npc2.Name),
                Loc.Get("base.overhear_default_3", npc1.Name, npc2.Name),
                Loc.Get("base.overhear_default_4", npc2.Name, npc1.Name),
            }
        };

        return templates[Random.Shared.Next(templates.Length)];
    }

    /// <summary>
    /// Return a single world-state flavor line based on current game state, or null.
    /// Only returns a line for notable conditions — most of the time returns null.
    /// </summary>
    protected virtual string? GetWorldStateFlavor()
    {
        // Blood Moon is already shown in the main display — skip it here
        // World boss active
        if (UsurperRemake.BBS.DoorMode.IsOnlineMode)
        {
            var bossName = WorldBossSystem.Instance?.ActiveBossName;
            if (!string.IsNullOrEmpty(bossName))
                return Loc.Get("base.flavor_world_boss", bossName);
        }

        // Recent coup — new king (within 3 real-time days of coronation)
        var king = CastleLocation.GetCurrentKing();
        if (king != null && king.IsActive)
        {
            if ((DateTime.Now - king.CoronationDate).TotalDays <= 3)
                return Loc.Get("base.flavor_new_ruler", king.Name);
        }

        // Time of day flavor (single-player only, non-dungeon)
        if (!UsurperRemake.BBS.DoorMode.IsOnlineMode && currentPlayer != null)
        {
            int hour = currentPlayer.GameTimeMinutes / 60;
            if (hour >= 0 && hour < 5)
                return Loc.Get("base.flavor_late_night");
            if (hour >= 5 && hour < 7)
                return Loc.Get("base.flavor_dawn");
            if (hour >= 20 && hour < 22)
                return Loc.Get("base.flavor_evening");
            if (hour >= 22)
                return Loc.Get("base.flavor_night");
        }

        return null;
    }

    /// <summary>
    /// Map GameLocation enum to NPC location strings
    /// </summary>
    protected virtual string GetNPCLocationString()
    {
        return LocationId switch
        {
            GameLocation.MainStreet => "Main Street",
            GameLocation.TheInn => "Inn",
            GameLocation.Church => "Church",
            GameLocation.Temple => "Temple",
            GameLocation.WeaponShop => "Weapon Shop",
            GameLocation.ArmorShop => "Armor Shop",
            GameLocation.MagicShop => "Magic Shop",
            GameLocation.AuctionHouse => "Auction House",
            GameLocation.Steroids => "Level Master",
            GameLocation.DarkAlley => "Dark Alley",
            GameLocation.Castle => "Castle",
            GameLocation.LoveStreet => "Love Street",
            GameLocation.LoveCorner => "Love Street",
            GameLocation.Home => "Home",
            GameLocation.Orbs => "Inn",
            GameLocation.BobsBeer => "Inn",
            GameLocation.Bank => "Bank",
            GameLocation.Healer => "Healer",
            GameLocation.Dungeons => "Dungeon",
            _ => Name
        };
    }

    /// <summary>
    /// Get NPCs currently at this location from NPCSpawnSystem
    /// </summary>
    protected virtual List<NPC> GetLiveNPCsAtLocation()
    {
        var locationString = GetNPCLocationString();
        var allNPCs = NPCSpawnSystem.Instance.ActiveNPCs ?? new List<NPC>();

        return allNPCs
            .Where(npc => npc.IsAlive && !npc.IsDead &&
                   npc.CurrentLocation?.Equals(locationString, StringComparison.OrdinalIgnoreCase) == true)
            .ToList();
    }

    private static Random _npcRandom = Random.Shared;

    /// <summary>
    /// Get a random shout/action for an NPC based on their personality
    /// </summary>
    protected virtual string GetNPCShout(NPC npc)
    {
        var name = npc.Name2;
        var shouts = new List<string>();

        // Personality-based shouts
        if (npc.Darkness > npc.Chivalry)
        {
            // Evil NPCs
            shouts.AddRange(new[] {
                Loc.Get("base.shout_evil_glare", name),
                Loc.Get("base.shout_evil_curse", name),
                Loc.Get("base.shout_evil_gold", name),
                Loc.Get("base.shout_evil_spit", name),
                Loc.Get("base.shout_evil_dagger", name),
                Loc.Get("base.shout_evil_laugh", name),
                Loc.Get("base.shout_evil_sneer", name),
            });
        }
        else if (npc.Chivalry > 500)
        {
            // Good NPCs
            shouts.AddRange(new[] {
                Loc.Get("base.shout_good_nod", name),
                Loc.Get("base.shout_good_wave", name),
                Loc.Get("base.shout_good_news", name),
                Loc.Get("base.shout_good_rumor", name),
                Loc.Get("base.shout_good_sword", name),
                Loc.Get("base.shout_good_hum", name),
                Loc.Get("base.shout_good_smile", name),
            });
        }
        else
        {
            // Neutral NPCs
            shouts.AddRange(new[] {
                Loc.Get("base.shout_neutral_business", name),
                Loc.Get("base.shout_neutral_thought", name),
                Loc.Get("base.shout_neutral_merchandise", name),
                Loc.Get("base.shout_neutral_chat", name),
                Loc.Get("base.shout_neutral_stretch", name),
                Loc.Get("base.shout_neutral_gold", name),
                Loc.Get("base.shout_neutral_yawn", name),
            });
        }

        // Class-based shouts
        switch (npc.Class)
        {
            case CharacterClass.Warrior:
            case CharacterClass.Barbarian:
                shouts.Add(Loc.Get("base.shout_class_flex", name));
                shouts.Add(Loc.Get("base.shout_class_polish", name));
                break;
            case CharacterClass.Magician:
            case CharacterClass.Sage:
                shouts.Add(Loc.Get("base.shout_class_tome", name));
                shouts.Add(Loc.Get("base.shout_class_arcane", name));
                break;
            case CharacterClass.Cleric:
            case CharacterClass.Paladin:
                shouts.Add(Loc.Get("base.shout_class_blessing", name));
                shouts.Add(Loc.Get("base.shout_class_pray", name));
                break;
            case CharacterClass.Assassin:
                shouts.Add(Loc.Get("base.shout_class_shadows", name));
                shouts.Add(Loc.Get("base.shout_class_blade", name));
                break;
        }

        return shouts[_npcRandom.Next(shouts.Count)];
    }

    /// <summary>
    /// Get alignment display string
    /// </summary>
    protected virtual string GetAlignmentDisplay(NPC npc)
    {
        if (npc.Darkness > npc.Chivalry + 300) return $"({Loc.Get("base.align_evil")})";
        if (npc.Chivalry > npc.Darkness + 300) return $"({Loc.Get("base.align_good")})";
        return $"({Loc.Get("base.align_neutral")})";
    }

    /// <summary>
    /// Get relationship display information (color, text, symbol) based on relationship level
    /// Relationship levels: Married=10, Love=20, Passion=30, Friendship=40, Trust=50,
    /// Respect=60, Normal=70, Suspicious=80, Anger=90, Enemy=100, Hate=110
    /// </summary>
    protected virtual (string color, string text, string symbol) GetRelationshipDisplayInfo(int relationLevel)
    {
        return relationLevel switch
        {
            <= GameConfig.RelationMarried => ("bright_red", Loc.Get("base.rel_married"), "<3"),     // 10 - Married (red with heart)
            <= GameConfig.RelationLove => ("bright_magenta", Loc.Get("base.rel_in_love"), "<3"),    // 20 - Love
            <= GameConfig.RelationPassion => ("magenta", Loc.Get("base.rel_passionate"), ""),        // 30 - Passion
            <= GameConfig.RelationFriendship => ("bright_cyan", Loc.Get("base.rel_friends"), ""),   // 40 - Friendship
            <= GameConfig.RelationTrust => ("cyan", Loc.Get("base.rel_trusted"), ""),               // 50 - Trust
            <= GameConfig.RelationRespect => ("bright_green", Loc.Get("base.rel_respected"), ""),   // 60 - Respect
            <= GameConfig.RelationNormal => ("gray", Loc.Get("base.rel_neutral"), ""),              // 70 - Normal/Neutral
            <= GameConfig.RelationSuspicious => ("yellow", Loc.Get("base.rel_wary"), ""),           // 80 - Suspicious
            <= GameConfig.RelationAnger => ("bright_yellow", Loc.Get("base.rel_hostile"), ""),      // 90 - Anger
            <= GameConfig.RelationEnemy => ("red", Loc.Get("base.rel_enemy"), ""),                  // 100 - Enemy
            _ => ("dark_red", Loc.Get("base.rel_hated"), "")                                        // 110+ - Hate
        };
    }

    /// <summary>
    /// Show NPCs in this location with contextual activity flavor text.
    /// Shows up to 3 NPCs with activity descriptions that reflect what they're doing.
    /// Only shows NPCs the player has met (has memory of) unless they're static location NPCs.
    /// </summary>
    protected virtual void ShowNPCsInLocation()
    {
        // Get live NPCs from the spawn system
        var liveNPCs = GetLiveNPCsAtLocation();

        // Also include any static LocationNPCs (special NPCs like shopkeepers)
        var allNPCs = new List<NPC>(LocationNPCs);
        foreach (var npc in liveNPCs)
        {
            if (!allNPCs.Any(n => n.Name2 == npc.Name2))
                allNPCs.Add(npc);
        }

        // Filter to alive NPCs at this location
        var visibleNPCs = allNPCs.Where(npc => npc.IsAlive && !npc.IsDead).ToList();

        if (visibleNPCs.Count > 0)
        {
            terminal.SetColor("gray");
            terminal.WriteLine(Loc.Get("base.you_notice"));

            foreach (var npc in visibleNPCs.Take(3))
            {
                // Color based on alignment
                if (npc.Darkness > npc.Chivalry + 200)
                    terminal.SetColor("red");
                else if (npc.Chivalry > npc.Darkness + 200)
                    terminal.SetColor("bright_green");
                else
                    terminal.SetColor("cyan");

                // Always use location-contextual flavor text.
                // CurrentActivity is set by WorldSimulator based on what the NPC *did* (e.g. visited the Inn),
                // but the NPC may have since moved to a different location, making the old activity text wrong
                // (e.g. "having a drink at the bar" while standing in the Church).
                var activity = GetLocationContextActivity(npc);

                terminal.WriteLine($"  {activity}");
            }

            // Show count of other NPCs not displayed
            var otherCount = allNPCs.Count(n => n.IsAlive) - visibleNPCs.Take(3).Count();
            if (otherCount > 0)
            {
                terminal.SetColor("gray");
                terminal.WriteLine($"  {Loc.Get("base.others_going_about", otherCount)}");
            }

            terminal.WriteLine("");
        }
    }

    /// <summary>
    /// Get a location-contextual activity string for an NPC whose CurrentActivity isn't set.
    /// Returns flavor text appropriate to where the NPC currently is.
    /// </summary>
    protected virtual string GetLocationContextActivity(NPC npc)
    {
        var name = npc.Name2;
        var location = LocationId;
        return location switch
        {
            GameLocation.TheInn or GameLocation.BobsBeer => _npcRandom.Next(3) switch
            {
                0 => Loc.Get("base.activity_inn_drink", name),
                1 => Loc.Get("base.activity_inn_chat", name),
                _ => Loc.Get("base.activity_inn_corner", name)
            },
            GameLocation.Church => _npcRandom.Next(3) switch
            {
                0 => Loc.Get("base.activity_church_pray", name),
                1 => Loc.Get("base.activity_church_candle", name),
                _ => Loc.Get("base.activity_church_priest", name)
            },
            GameLocation.WeaponShop => _npcRandom.Next(3) switch
            {
                0 => Loc.Get("base.activity_weapon_blade", name),
                1 => Loc.Get("base.activity_weapon_mace", name),
                _ => Loc.Get("base.activity_weapon_haggle", name)
            },
            GameLocation.ArmorShop => _npcRandom.Next(3) switch
            {
                0 => Loc.Get("base.activity_armor_gauntlets", name),
                1 => Loc.Get("base.activity_armor_shield", name),
                _ => Loc.Get("base.activity_armor_chainmail", name)
            },
            GameLocation.MagicShop => _npcRandom.Next(3) switch
            {
                0 => Loc.Get("base.activity_magic_scroll", name),
                1 => Loc.Get("base.activity_magic_crystal", name),
                _ => Loc.Get("base.activity_magic_potion", name)
            },
            GameLocation.AuctionHouse => _npcRandom.Next(3) switch
            {
                0 => Loc.Get("base.activity_auction_bid", name),
                1 => Loc.Get("base.activity_auction_browse", name),
                _ => Loc.Get("base.activity_auction_appraise", name)
            },
            GameLocation.Healer => _npcRandom.Next(2) switch
            {
                0 => Loc.Get("base.activity_healer_potions", name),
                _ => Loc.Get("base.activity_healer_waiting", name)
            },
            GameLocation.MainStreet => _npcRandom.Next(4) switch
            {
                0 => Loc.Get("base.activity_street_stroll", name),
                1 => Loc.Get("base.activity_street_lean", name),
                2 => Loc.Get("base.activity_street_talk", name),
                _ => Loc.Get("base.activity_street_business", name)
            },
            GameLocation.DarkAlley => _npcRandom.Next(3) switch
            {
                0 => Loc.Get("base.activity_alley_lurk", name),
                1 => Loc.Get("base.activity_alley_whisper", name),
                _ => Loc.Get("base.activity_alley_watch", name)
            },
            GameLocation.Castle => _npcRandom.Next(3) switch
            {
                0 => Loc.Get("base.activity_castle_court", name),
                1 => Loc.Get("base.activity_castle_guard", name),
                _ => Loc.Get("base.activity_castle_courtier", name)
            },
            _ => GetNPCShout(npc) // Fallback to the old system
        };
    }

    /// <summary>
    /// Show a mood-aware shopkeeper greeting line. Looks up the NPC by name and displays
    /// their mood prefix based on emotional state and impression of the player.
    /// Falls back to a generic greeting if the NPC isn't found.
    /// </summary>
    protected void ShowShopkeeperMood(string shopkeeperName, string fallbackGreeting)
    {
        var npc = NPCSpawnSystem.Instance?.GetNPCByName(shopkeeperName);
        if (npc != null && currentPlayer != null)
        {
            var moodText = npc.GetMoodPrefix(currentPlayer);
            terminal.SetColor("gray");
            terminal.WriteLine(moodText);
        }
        else
        {
            terminal.SetColor("white");
            terminal.WriteLine(fallbackGreeting);
        }
    }

    /// <summary>
    /// Show location-specific actions
    /// </summary>
    protected virtual void ShowLocationActions()
    {
        if (LocationActions.Count > 0)
        {
            terminal.SetColor("white");
            terminal.WriteLine(Loc.Get("base.available_actions"));

            for (int i = 0; i < LocationActions.Count; i++)
            {
                terminal.WriteLine($"  {i + 1}. {LocationActions[i]}");
            }
            terminal.WriteLine("");
        }
    }

    /// <summary>
    /// Show available exits (Pascal-compatible)
    /// </summary>
    protected virtual void ShowExits()
    {
        if (PossibleExits.Count > 0)
        {
            terminal.SetColor("yellow");
            terminal.WriteLine(Loc.Get("base.exits"));

            foreach (var exit in PossibleExits)
            {
                var exitName = GetLocationName(exit);
                var exitKey = GetLocationKey(exit);
                terminal.WriteLine($"  ({exitKey}) {exitName}");
            }
            terminal.WriteLine("");
        }
    }

    /// <summary>
    /// Show breadcrumb navigation at top of screen
    /// </summary>
    protected virtual void ShowBreadcrumb()
    {
        terminal.SetColor("gray");
        terminal.Write(Loc.Get("base.location_label") + " ");
        terminal.SetColor("bright_cyan");

        // Build breadcrumb path based on current location
        string breadcrumb = GetBreadcrumbPath();
        terminal.WriteLine(breadcrumb);
        terminal.WriteLine("");
    }

    /// <summary>
    /// Get breadcrumb path for current location
    /// </summary>
    protected virtual string GetBreadcrumbPath()
    {
        // Default: just show location name
        // Subclasses can override for more complex paths (e.g., "Main Street > Dungeons > Level 3")
        switch (LocationId)
        {
            case GameLocation.MainStreet:
                return Loc.Get("base.bc_main_street");
            case GameLocation.Home:
                return Loc.Get("base.bc_home");
            case GameLocation.AnchorRoad:
                return Loc.Get("base.bc_anchor_road");
            case GameLocation.WeaponShop:
                return Loc.Get("base.bc_weapon_shop");
            case GameLocation.ArmorShop:
                return Loc.Get("base.bc_armor_shop");
            case GameLocation.MagicShop:
                return Loc.Get("base.bc_magic_shop");
            case GameLocation.TheInn:
                return Loc.Get("base.bc_the_inn");
            case GameLocation.DarkAlley:
                return Loc.Get("base.bc_dark_alley");
            case GameLocation.Church:
                return Loc.Get("base.bc_church");
            case GameLocation.Bank:
                return Loc.Get("base.bc_bank");
            case GameLocation.Castle:
                return Loc.Get("base.bc_castle");
            case GameLocation.Prison:
                return Loc.Get("base.bc_prison");
            default:
                return Name ?? Loc.Get("base.bc_unknown");
        }
    }

    /// <summary>
    /// Show status line at bottom
    /// </summary>
    protected virtual void ShowStatusLine()
    {
        if (IsScreenReader)
        {
            // Screen reader: plain labeled text, one stat per line
            string resource = currentPlayer.IsManaClass
                ? $"{Loc.Get("status.mana")}: {currentPlayer.Mana}/{currentPlayer.MaxMana}"
                : $"{Loc.Get("status.stamina")}: {currentPlayer.CurrentCombatStamina}/{currentPlayer.MaxCombatStamina}";
            string xpInfo = "";
            if (currentPlayer.Level < GameConfig.MaxLevel)
            {
                long currentXP = currentPlayer.Experience;
                long nextLevelXP = GetExperienceForLevel(currentPlayer.Level + 1);
                long prevLevelXP = GetExperienceForLevel(currentPlayer.Level);
                long xpIntoLevel = currentXP - prevLevelXP;
                long xpNeeded = nextLevelXP - prevLevelXP;
                int xpPercent = xpNeeded > 0 ? (int)((xpIntoLevel * 100) / xpNeeded) : 0;
                xpPercent = Math.Clamp(xpPercent, 0, 100);
                xpInfo = $", {Loc.Get("status.xp_to_next", xpPercent)}";
            }
            terminal.SetColor("white");
            terminal.WriteLine($"{Loc.Get("status.hp")}: {currentPlayer.HP}/{currentPlayer.MaxHP}, {Loc.Get("status.gold_label")}: {currentPlayer.Gold:N0}, {resource}, {Loc.Get("ui.level")} {currentPlayer.Level}{xpInfo}");
            terminal.WriteLine("");
        }
        else
        {
            // HP with urgency coloring
            terminal.SetColor("gray");
            terminal.Write($"{Loc.Get("status.hp")}: ");
            float hpPercent = currentPlayer.MaxHP > 0 ? (float)currentPlayer.HP / currentPlayer.MaxHP : 0;
            string hpColor = hpPercent > 0.5f ? "bright_green" : hpPercent > 0.25f ? "yellow" : "bright_red";
            terminal.SetColor(hpColor);
            terminal.Write($"{currentPlayer.HP}");
            terminal.SetColor("gray");
            terminal.Write("/");
            terminal.SetColor(hpColor);
            terminal.Write($"{currentPlayer.MaxHP}");

            terminal.SetColor("gray");
            terminal.Write($" | {Loc.Get("status.gold_label")}: ");
            terminal.SetColor("yellow");
            terminal.Write($"{currentPlayer.Gold:N0}");

            if (currentPlayer.IsManaClass)
            {
                terminal.SetColor("gray");
                terminal.Write($" | {Loc.Get("status.mp")}: ");
                terminal.SetColor("blue");
                terminal.Write($"{currentPlayer.Mana}");
                terminal.SetColor("gray");
                terminal.Write("/");
                terminal.SetColor("blue");
                terminal.Write($"{currentPlayer.MaxMana}");
            }
            else
            {
                terminal.SetColor("gray");
                terminal.Write($" | {Loc.Get("status.sta")}: ");
                terminal.SetColor("yellow");
                terminal.Write($"{currentPlayer.CurrentCombatStamina}");
                terminal.SetColor("gray");
                terminal.Write("/");
                terminal.SetColor("yellow");
                terminal.Write($"{currentPlayer.MaxCombatStamina}");
            }

            terminal.SetColor("gray");
            terminal.Write($" | {Loc.Get("ui.level")} ");
            terminal.SetColor("cyan");
            terminal.Write($"{currentPlayer.Level}");

            // XP progress to next level
            if (currentPlayer.Level < GameConfig.MaxLevel)
            {
                long currentXP = currentPlayer.Experience;
                long nextLevelXP = GetExperienceForLevel(currentPlayer.Level + 1);
                long prevLevelXP = GetExperienceForLevel(currentPlayer.Level);
                long xpIntoLevel = currentXP - prevLevelXP;
                long xpNeeded = nextLevelXP - prevLevelXP;
                int xpPercent = xpNeeded > 0 ? (int)((xpIntoLevel * 100) / xpNeeded) : 0;
                xpPercent = Math.Clamp(xpPercent, 0, 100);

                terminal.SetColor("gray");
                terminal.Write(" (");
                terminal.SetColor(xpPercent >= 90 ? "bright_green" : "white");
                terminal.Write($"{xpPercent}%");
                terminal.SetColor("gray");
                terminal.Write(")");
            }

            terminal.WriteLine("");
            terminal.WriteLine("");
        } // end else (non-SR status line)

        // Quick command bar
        ShowQuickCommandBar();
    }

    /// <summary>
    /// Experience required to have the specified level (cumulative)
    /// </summary>
    private static long GetExperienceForLevel(int level)
    {
        if (level <= 1) return 0;
        long exp = 0;
        for (int i = 2; i <= level; i++)
        {
            exp += (long)(Math.Pow(i, 2.0) * 50);
        }
        return exp;
    }

    /// <summary>
    /// Show quick command bar with common keyboard shortcuts
    /// </summary>
    protected virtual void ShowQuickCommandBar()
    {
        if (IsScreenReader)
        {
            // Screen reader: plain text list without decorative brackets or divider
            terminal.SetColor("white");
            terminal.Write($"{Loc.Get("ui.quick_commands")}: % {Loc.Get("menu.action.status")}, ");
            if (LocationId != GameLocation.MainStreet)
                terminal.Write($"R {Loc.Get("ui.return")}, ");
            terminal.Write($"* {Loc.Get("menu.action.inventory")}, ? {Loc.Get("menu.action.help")}, ");
            var srNpcsHere = GetLiveNPCsAtLocation();
            if (srNpcsHere.Count > 0)
                terminal.Write($"0 {Loc.Get("ui.talk")} ({srNpcsHere.Count}), ");
            terminal.Write($"~ {Loc.Get("menu.action.preferences")}, / {Loc.Get("ui.commands")}, ! {Loc.Get("menu.action.report_bug")}");
            terminal.WriteLine("");
            terminal.WriteLine("");
            return;
        }

        terminal.SetColor("darkgray");
        terminal.Write("─────────────────────────────────────────────────────────────────────────────");
        terminal.WriteLine("");

        terminal.SetColor("gray");
        terminal.Write($"{Loc.Get("ui.quick_commands")}: ");

        terminal.SetColor("darkgray");
        terminal.Write("[");
        terminal.SetColor("bright_yellow");
        terminal.Write("%");
        terminal.SetColor("darkgray");
        terminal.Write("]");
        terminal.SetColor("white");
        terminal.Write(Loc.Get("base.qc_status_suffix") + "  ");

        if (LocationId != GameLocation.MainStreet)
        {
            terminal.SetColor("darkgray");
            terminal.Write("[");
            terminal.SetColor("bright_yellow");
            terminal.Write("R");
            terminal.SetColor("darkgray");
            terminal.Write("]");
            terminal.SetColor("white");
            terminal.Write(Loc.Get("base.qc_return_suffix") + "  ");
        }

        terminal.SetColor("darkgray");
        terminal.Write("[");
        terminal.SetColor("bright_yellow");
        terminal.Write("*");
        terminal.SetColor("darkgray");
        terminal.Write("]");
        terminal.SetColor("white");
        terminal.Write(Loc.Get("base.qc_inventory") + "  ");

        terminal.SetColor("darkgray");
        terminal.Write("[");
        terminal.SetColor("bright_yellow");
        terminal.Write("?");
        terminal.SetColor("darkgray");
        terminal.Write("]");
        terminal.SetColor("white");
        terminal.Write(Loc.Get("base.qc_help") + "  ");

        // Show Talk option if NPCs are present
        var npcsHere = GetLiveNPCsAtLocation();
        if (npcsHere.Count > 0)
        {
            terminal.SetColor("darkgray");
            terminal.Write("[");
            terminal.SetColor("bright_yellow");
            terminal.Write("0");
            terminal.SetColor("darkgray");
            terminal.Write("]");
            terminal.SetColor("white");
            terminal.Write($" {Loc.Get("base.qc_talk")} ({npcsHere.Count})  ");
        }

        // Show Preferences option
        terminal.SetColor("darkgray");
        terminal.Write("[");
        terminal.SetColor("bright_yellow");
        terminal.Write("~");
        terminal.SetColor("darkgray");
        terminal.Write("]");
        terminal.SetColor("white");
        terminal.Write(Loc.Get("base.qc_prefs") + "  ");

        // Show slash commands hint
        terminal.SetColor("darkgray");
        terminal.Write("[");
        terminal.SetColor("bright_yellow");
        terminal.Write("/");
        terminal.SetColor("darkgray");
        terminal.Write("]");
        terminal.SetColor("white");
        terminal.Write(Loc.Get("base.qc_cmds") + "  ");

        // Show bug report hint
        terminal.SetColor("darkgray");
        terminal.Write("[");
        terminal.SetColor("bright_yellow");
        terminal.Write("!");
        terminal.SetColor("darkgray");
        terminal.Write("]");
        terminal.SetColor("white");
        terminal.Write(Loc.Get("base.qc_bug"));

        terminal.WriteLine("");
        terminal.WriteLine("");
    }

    // ═══════════════════════════════════════════════════════════════════
    // BBS 80x25 compact display helpers
    // Used by location-specific DisplayLocationBBS() methods
    // ═══════════════════════════════════════════════════════════════════

    /// <summary>
    /// BBS: 1-line header with title centered in a decorative line
    /// </summary>
    protected void ShowBBSHeader(string title)
    {
        if (IsScreenReader)
        {
            terminal.SetColor("bright_white");
            terminal.WriteLine(title);
            return;
        }
        int padLen = Math.Max(0, (76 - title.Length) / 2);
        string padL = new string('═', padLen);
        string padR = new string('═', 76 - title.Length - padLen);
        terminal.SetColor("bright_blue");
        terminal.Write("╔" + padL + " ");
        terminal.SetColor("bright_white");
        terminal.Write(title);
        terminal.SetColor("bright_blue");
        terminal.WriteLine(" " + padR + "╗");
    }

    /// <summary>
    /// BBS: 1-line NPC summary (up to 2 names + "and N others")
    /// </summary>
    protected void ShowBBSNPCs()
    {
        var liveNPCs = GetLiveNPCsAtLocation();
        if (liveNPCs.Count > 0)
        {
            terminal.SetColor("gray");
            terminal.Write($" {Loc.Get("base.you_notice")}: ");
            terminal.SetColor("cyan");
            var names = liveNPCs.Take(2).Select(n => n.Name2).ToList();
            terminal.Write(string.Join(", ", names));
            if (liveNPCs.Count > 2)
            {
                terminal.SetColor("gray");
                terminal.Write($", +{liveNPCs.Count - 2} {Loc.Get("base.more")}");
            }
            terminal.WriteLine("");
        }
    }

    /// <summary>
    /// BBS: 1-line compact status (HP/Gold/Mana/Level with XP%)
    /// </summary>
    protected void ShowBBSStatusLine()
    {
        terminal.SetColor("gray");
        terminal.Write($" {Loc.Get("status.hp")}:");
        float hpPct = currentPlayer.MaxHP > 0 ? (float)currentPlayer.HP / currentPlayer.MaxHP : 0;
        terminal.SetColor(hpPct > 0.5f ? "bright_green" : hpPct > 0.25f ? "yellow" : "bright_red");
        terminal.Write($"{currentPlayer.HP}/{currentPlayer.MaxHP}");
        terminal.SetColor("gray");
        terminal.Write($" {Loc.Get("status.gold_label")}:");
        terminal.SetColor("yellow");
        terminal.Write($"{currentPlayer.Gold:N0}");
        if (currentPlayer.IsManaClass)
        {
            terminal.SetColor("gray");
            terminal.Write($" {Loc.Get("status.mp")}:");
            terminal.SetColor("blue");
            terminal.Write($"{currentPlayer.Mana}/{currentPlayer.MaxMana}");
        }
        else
        {
            terminal.SetColor("gray");
            terminal.Write($" {Loc.Get("status.sta")}:");
            terminal.SetColor("yellow");
            terminal.Write($"{currentPlayer.CurrentCombatStamina}/{currentPlayer.MaxCombatStamina}");
        }
        terminal.SetColor("gray");
        terminal.Write($" {Loc.Get("base.lv_label")}:");
        terminal.SetColor("cyan");
        terminal.Write($"{currentPlayer.Level}");
        if (currentPlayer.Level < GameConfig.MaxLevel)
        {
            long curXP = currentPlayer.Experience;
            long nextXP = GetExperienceForLevel(currentPlayer.Level + 1);
            long prevXP = GetExperienceForLevel(currentPlayer.Level);
            long xpInto = curXP - prevXP;
            long xpNeed = nextXP - prevXP;
            int pct = xpNeed > 0 ? (int)((xpInto * 100) / xpNeed) : 0;
            terminal.SetColor("gray");
            terminal.Write($"({Math.Clamp(pct, 0, 100)}%)");
        }
        terminal.WriteLine("");
    }

    /// <summary>
    /// BBS: 1-line compact quick command bar
    /// </summary>
    protected void ShowBBSQuickCommands()
    {
        var npcsHere = GetLiveNPCsAtLocation();
        terminal.SetColor("darkgray");
        terminal.Write(" ["); terminal.SetColor("bright_yellow"); terminal.Write("%"); terminal.SetColor("darkgray"); terminal.Write("]");
        terminal.SetColor("white"); terminal.Write(Loc.Get("base.qc_status_suffix") + " ");
        terminal.SetColor("darkgray"); terminal.Write("["); terminal.SetColor("bright_yellow"); terminal.Write("*"); terminal.SetColor("darkgray"); terminal.Write("]");
        terminal.SetColor("white"); terminal.Write(Loc.Get("base.qc_inv") + " ");
        terminal.SetColor("darkgray"); terminal.Write("["); terminal.SetColor("bright_yellow"); terminal.Write("?"); terminal.SetColor("darkgray"); terminal.Write("]");
        terminal.SetColor("white"); terminal.Write(Loc.Get("base.qc_help") + " ");
        if (npcsHere.Count > 0)
        {
            terminal.SetColor("darkgray"); terminal.Write("["); terminal.SetColor("bright_yellow"); terminal.Write("0"); terminal.SetColor("darkgray"); terminal.Write("]");
            terminal.SetColor("white"); terminal.Write($" {Loc.Get("base.qc_talk")} ({npcsHere.Count}) ");
        }
        terminal.SetColor("darkgray"); terminal.Write("["); terminal.SetColor("bright_yellow"); terminal.Write("~"); terminal.SetColor("darkgray"); terminal.Write("]");
        terminal.SetColor("white"); terminal.Write(Loc.Get("base.qc_prefs") + " ");
        terminal.WriteLine("");
    }

    /// <summary>
    /// BBS: Render a row of menu items. Each tuple is (key, keyColor, label).
    /// </summary>
    protected void ShowBBSMenuRow(params (string key, string color, string label)[] items)
    {
        terminal.Write(" ");
        foreach (var (key, color, label) in items)
        {
            terminal.SetColor("darkgray"); terminal.Write("[");
            terminal.SetColor(color); terminal.Write(key);
            terminal.SetColor("darkgray"); terminal.Write("]");
            terminal.SetColor("white"); terminal.Write(label + " ");
        }
        terminal.WriteLine("");
    }

    /// <summary>
    /// BBS: Full compact display wrapper - header, description, NPCs, then caller adds menu, then status+commands.
    /// Intended to be used as: ShowBBSHeader → description → ShowBBSNPCs → menu → ShowBBSFooter
    /// </summary>
    protected void ShowBBSFooter()
    {
        terminal.WriteLine("");
        ShowBBSStatusLine();
        ShowBBSQuickCommands();
    }

    /// <summary>
    /// Get user choice
    /// </summary>
    protected virtual async Task<string> GetUserChoice()
    {
        if (UsurperRemake.BBS.DoorMode.IsMudServerMode)
        {
            var player = GetCurrentPlayer();
            if (player != null)
            {
                double hpPct = player.MaxHP > 0 ? (double)player.HP / player.MaxHP : 1.0;
                string hpColor = hpPct < 0.25 ? "red" : hpPct < 0.50 ? "yellow" : "bright_green";
                terminal.Write("[", "white");
                terminal.Write($"{player.HP}hp", hpColor);
                if (player.IsManaClass)
                    terminal.Write($" {player.Mana}mp", "cyan");
                else
                    terminal.Write($" {player.CurrentCombatStamina}st", "yellow");
                terminal.Write("] ", "white");
            }
            var promptName = GetMudPromptName();
            terminal.Write($"{promptName}", "bright_white");
            terminal.Write(" | ", "darkgray");
            terminal.Write("look", "bright_yellow");
            terminal.Write($" {Loc.Get("base.mud_to_redraw")}", "darkgray");
            terminal.Write(" > ", "bright_white");
            return await terminal.GetInput("");
        }
        terminal.SetColor("bright_white");
        return await GetChoice();
    }

    /// <summary>
    /// Short location name shown in the MUD streaming prompt, e.g. "Inn > ".
    /// Override in subclasses for a better name than the default class-name stripping.
    /// </summary>
    protected virtual string GetMudPromptName()
    {
        return GetType().Name.Replace("Location", "");
    }

    /// <summary>
    /// Flavor lines printed occasionally in MUD streaming mode to make the world feel alive.
    /// Return null (default) to suppress ambient messages for a location.
    /// </summary>
    protected virtual string[]? GetAmbientMessages() => null;

    /// <summary>
    /// Try to process global quick commands (* for inventory, ? for help, etc.)
    /// Returns (handled, shouldExit) - if handled is true, the command was processed
    /// </summary>
    protected async Task<(bool handled, bool shouldExit)> TryProcessGlobalCommand(string choice)
    {
        if (string.IsNullOrWhiteSpace(choice))
            return (false, false);

        var upperChoice = choice.ToUpper().Trim();

        // MUD streaming mode: `look` reprints the location banner (MUD convention).
        // Single-letter `l` is intentionally excluded to avoid conflicting with location
        // menu keys (e.g. [L]evel Raise at Level Master, [L]eave, etc.).
        if (UsurperRemake.BBS.DoorMode.IsMudServerMode && upperChoice == "LOOK")
        {
            _locationEntryDisplayed = false;
            _skipNextRedraw = false;
            return (true, false);
        }

        // Handle slash commands (works from any location)
        if (choice.StartsWith("/"))
        {
            // MUD mode: route chat through in-memory system (instant delivery)
            if (UsurperRemake.Server.SessionContext.IsActive)
            {
                var handled = await UsurperRemake.Server.MudChatSystem.TryProcessCommand(choice.Trim(), terminal);
                if (handled)
                {
                    var cmd = choice.Trim().Split(' ')[0].TrimStart('/').ToLowerInvariant();
                    if (IsInfoDisplayCommand(cmd))
                    {
                        // Info commands produce multi-line output — pause before redraw
                        await terminal.PressAnyKey();
                    }
                    else
                    {
                        // Chat commands: skip menu redraw so conversation stays visible
                        _skipNextRedraw = true;
                    }
                    return (true, false);
                }
            }
            // Legacy online mode: route through SQLite-polled chat system
            else if (OnlineChatSystem.IsActive)
            {
                var handled = await OnlineChatSystem.Instance!.TryProcessCommand(choice.Trim(), terminal);
                if (handled)
                {
                    var cmd = choice.Trim().Split(' ')[0].TrimStart('/').ToLowerInvariant();
                    if (IsInfoDisplayCommand(cmd))
                        await terminal.PressAnyKey();
                    return (true, false);
                }
            }

            return await ProcessSlashCommand(choice.Substring(1).ToLower().Trim());
        }

        switch (upperChoice)
        {
            case "%":
                await ShowStatus();
                return (true, false);
            case "*":
                await ShowInventory();
                return (true, false);
            case "~":
            case "PREFS":
            case "PREFERENCES":
                await ShowPreferencesMenu();
                return (true, false);
            case "0":
            case "TALK":
                if (LocationId == GameLocation.Dungeons)
                    return (false, false); // No NPCs to talk to in the dungeon
                await TalkToNPC();
                return (true, false);
            case "?":
            case "HELP":
                await ShowQuickCommandsHelp();
                return (true, false);
            case "!":
                await BugReportSystem.ReportBug(terminal, currentPlayer);
                return (true, false);
            default:
                return (false, false);
        }
    }

    /// <summary>
    /// Process slash commands like /stats, /quests, /time, etc.
    /// </summary>
    protected async Task<(bool handled, bool shouldExit)> ProcessSlashCommand(string command)
    {
        switch (command)
        {
            case "":
            case "?":
            case "help":
            case "commands":
                await ShowQuickCommandsHelp();
                return (true, false);

            case "s":
            case "st":
            case "stats":
            case "status":
                await ShowStatus();
                return (true, false);

            case "i":
            case "inv":
            case "inventory":
                await ShowInventory();
                return (true, false);

            case "q":
            case "quest":
            case "quests":
                await ShowActiveQuests();
                return (true, false);

            case "g":
            case "gold":
                await ShowGoldStatus();
                return (true, false);

            case "h":
            case "hp":
            case "health":
                await ShowHealthStatus();
                return (true, false);

            case "gear":
            case "eq":
            case "equipment":
                await ShowGearWithTeamSelection();
                return (true, false);

            case "p":
            case "pref":
            case "prefs":
            case "preferences":
                await ShowPreferencesMenu();
                return (true, false);

            case "pot":
            case "potion":
                await UseQuickPotion();
                return (true, false);

            case "j":
            case "herb":
            case "herbs":
                await HomeLocation.UseHerbMenu(currentPlayer, terminal);
                return (true, false);

            case "antidote":
                if (currentPlayer.Antidotes > 0 && currentPlayer.Poison > 0)
                {
                    currentPlayer.Antidotes--;
                    currentPlayer.Poison = 0;
                    currentPlayer.PoisonTurns = 0;
                    terminal.SetColor("bright_green");
                    terminal.WriteLine(Loc.Get("base.antidote_used"));
                    terminal.SetColor("gray");
                    terminal.WriteLine(Loc.Get("base.antidotes_remaining", currentPlayer.Antidotes, currentPlayer.MaxAntidotes));
                    await Task.Delay(1500);
                }
                else if (currentPlayer.Antidotes > 0)
                {
                    terminal.SetColor("yellow");
                    terminal.WriteLine(Loc.Get("base.not_poisoned"));
                    await Task.Delay(1000);
                }
                else
                {
                    terminal.SetColor("red");
                    terminal.WriteLine(Loc.Get("base.no_antidotes"));
                    await Task.Delay(1000);
                }
                return (true, false);

            case "bug":
            case "report":
            case "bugreport":
                await BugReportSystem.ReportBug(terminal, currentPlayer);
                return (true, false);

            case "mail":
            case "mailbox":
                if (UsurperRemake.BBS.DoorMode.IsOnlineMode)
                    await ShowMailbox();
                else
                {
                    terminal.SetColor("yellow");
                    terminal.WriteLine($"  {Loc.Get("base.online_only_mail")}");
                    await Task.Delay(1500);
                }
                return (true, false);

            case "trade":
            case "trades":
            case "package":
            case "packages":
                if (UsurperRemake.BBS.DoorMode.IsOnlineMode)
                    await ShowTradeMenu();
                else
                {
                    terminal.SetColor("yellow");
                    terminal.WriteLine($"  {Loc.Get("base.online_only_trade")}");
                    await Task.Delay(1500);
                }
                return (true, false);

            case "bounty":
            case "bounties":
                if (UsurperRemake.BBS.DoorMode.IsOnlineMode)
                    await ShowBountyMenu();
                else
                {
                    terminal.SetColor("yellow");
                    terminal.WriteLine($"  {Loc.Get("base.online_only_bounties")}");
                    await Task.Delay(1500);
                }
                return (true, false);

            case "auction":
            case "ah":
            case "market":
                if (UsurperRemake.BBS.DoorMode.IsOnlineMode)
                    await ShowAuctionMenu();
                else
                {
                    terminal.SetColor("yellow");
                    terminal.WriteLine($"  {Loc.Get("base.online_only_auction")}");
                    await Task.Delay(1500);
                }
                return (true, false);

            case "m":
            case "mat":
            case "mats":
            case "materials":
                await ShowMaterials();
                return (true, false);

            case "boss":
            case "worldboss":
                if (UsurperRemake.BBS.DoorMode.IsOnlineMode)
                    await WorldBossSystem.Instance.ShowWorldBossUI(currentPlayer, terminal);
                else
                {
                    terminal.SetColor("yellow");
                    terminal.WriteLine($"  {Loc.Get("base.online_only_boss")}");
                    await Task.Delay(1500);
                }
                return (true, false);

            case "t":
            case "time":
                ShowGameTime();
                await terminal.PressAnyKey();
                return (true, false);

            case "compact":
            case "mobile":
                currentPlayer.CompactMode = !currentPlayer.CompactMode;
                GameConfig.CompactMode = currentPlayer.CompactMode;
                terminal.WriteLine(currentPlayer.CompactMode
                    ? $"  {Loc.Get("base.compact_enabled")}"
                    : $"  {Loc.Get("base.compact_disabled")}", "green");
                await GameEngine.Instance.SaveCurrentGame();
                await Task.Delay(1000);
                return (true, false);

            default:
                terminal.WriteLine("");
                terminal.SetColor("red");
                terminal.WriteLine($"  {Loc.Get("base.unknown_command", command)}");
                terminal.SetColor("gray");
                terminal.WriteLine($"  {Loc.Get("base.type_help")}");
                terminal.WriteLine("");
                await terminal.PressAnyKey();
                return (true, false);
        }
    }

    /// <summary>
    /// Show quick commands help
    /// </summary>
    protected async Task ShowQuickCommandsHelp()
    {
        if (IsScreenReader)
        {
            await ShowQuickCommandsHelpSR();
            return;
        }

        // Helper: write colored content then pad to 78 visible chars + closing ║
        void WriteBoxLine(Action writeContent, int contentChars)
        {
            terminal.SetColor("bright_cyan");
            terminal.Write("║");
            writeContent();
            int pad = 78 - contentChars;
            if (pad > 0) terminal.Write(new string(' ', pad));
            terminal.SetColor("bright_cyan");
            terminal.WriteLine("║");
        }

        terminal.WriteLine("");
        terminal.SetColor("bright_cyan");
        terminal.WriteLine("╔══════════════════════════════════════════════════════════════════════════════╗");
        var helpTitle = Loc.Get("base.quick_commands");
        int helpTitlePad = (78 - helpTitle.Length) / 2;
        WriteBoxLine(() => { terminal.SetColor("white"); terminal.Write(new string(' ', helpTitlePad) + helpTitle); }, helpTitlePad + helpTitle.Length);
        terminal.SetColor("bright_cyan");
        terminal.WriteLine("╠══════════════════════════════════════════════════════════════════════════════╣");
        var helpSubtitle = "  " + Loc.Get("base.help_commands_work");
        WriteBoxLine(() => { terminal.SetColor("white"); terminal.Write(helpSubtitle); }, helpSubtitle.Length);
        WriteBoxLine(() => { }, 0);

        // Slash commands with aliases
        void WriteCmdAlias(string cmd, string alias, string desc)
        {
            WriteBoxLine(() =>
            {
                terminal.Write(" ");
                terminal.SetColor("cyan");
                terminal.Write(cmd.PadRight(10));
                terminal.SetColor("gray");
                terminal.Write(" or ");
                terminal.SetColor("cyan");
                terminal.Write(alias.PadRight(4));
                terminal.SetColor("white");
                terminal.Write($" {desc}");
            }, 10 + 4 + 4 + 1 + desc.Length + 1);
        }

        // Slash commands without aliases
        void WriteCmd(string cmd, string desc)
        {
            WriteBoxLine(() =>
            {
                terminal.Write(" ");
                terminal.SetColor("cyan");
                terminal.Write(cmd.PadRight(18));
                terminal.SetColor("white");
                terminal.Write($" {desc}");
            }, 18 + 1 + desc.Length + 1);
        }

        WriteCmdAlias("/stats", "%", Loc.Get("base.help_stats"));
        WriteCmdAlias("/inventory", "*", Loc.Get("base.help_inventory"));
        WriteCmdAlias("/quests", "/q", Loc.Get("base.help_quests"));
        WriteCmdAlias("/gold", "/g", Loc.Get("base.help_gold"));
        WriteCmdAlias("/health", "/hp", Loc.Get("base.help_health"));
        WriteCmdAlias("/gear", "/eq", Loc.Get("base.help_gear"));
        WriteCmdAlias("/potion", "/pot", Loc.Get("base.help_potion"));
        WriteCmdAlias("/herb", "/j", Loc.Get("base.help_herb"));
        WriteCmdAlias("/materials", "/mat", Loc.Get("base.help_materials"));
        WriteCmdAlias("/time", "/t", Loc.Get("base.help_time"));
        WriteCmdAlias("/prefs", "/p", Loc.Get("base.help_prefs"));
        WriteCmd("/mail", Loc.Get("base.help_mail"));
        WriteCmd("/trade", Loc.Get("base.help_trade"));
        WriteCmd("/auction", Loc.Get("base.help_auction"));
        WriteCmd("/boss", Loc.Get("base.help_boss"));
        WriteCmd("/compact", Loc.Get("base.help_compact"));
        WriteCmd("/bug", Loc.Get("base.help_bug"));

        WriteBoxLine(() => { }, 0);
        var quickKeysLabel = "  " + Loc.Get("base.help_quick_keys");
        WriteBoxLine(() => { terminal.SetColor("white"); terminal.Write(quickKeysLabel); }, quickKeysLabel.Length);

        void WriteQuickKey(string key, string desc)
        {
            WriteBoxLine(() =>
            {
                terminal.Write(" ");
                terminal.SetColor("bright_yellow");
                terminal.Write(key.PadRight(2));
                terminal.SetColor("white");
                terminal.Write($" {desc}");
            }, 2 + 1 + desc.Length + 1);
        }

        WriteQuickKey("*", Loc.Get("base.help_key_inventory"));
        WriteQuickKey("~", Loc.Get("base.help_key_prefs"));
        WriteQuickKey("%", Loc.Get("base.help_key_status"));
        WriteQuickKey("?", Loc.Get("base.help_key_help"));
        WriteQuickKey("!", Loc.Get("base.help_key_bug"));

        // Online/MUD chat commands
        if (UsurperRemake.Server.SessionContext.IsActive || OnlineChatSystem.IsActive)
        {
            WriteBoxLine(() => { }, 0);
            var onlineCmdsLabel = "  " + Loc.Get("base.help_online_commands");
            WriteBoxLine(() => { terminal.SetColor("white"); terminal.Write(onlineCmdsLabel); }, onlineCmdsLabel.Length);

            void WriteOnlineCmd(string cmd, string desc)
            {
                WriteBoxLine(() =>
                {
                    terminal.Write(" ");
                    terminal.SetColor("bright_green");
                    terminal.Write(cmd.PadRight(20));
                    terminal.SetColor("white");
                    terminal.Write($" {desc}");
                }, 20 + 1 + desc.Length + 1);
            }

            WriteOnlineCmd("/say <msg>", Loc.Get("base.help_say"));
            WriteOnlineCmd("/shout <msg>", Loc.Get("base.help_shout"));
            WriteOnlineCmd("/tell <name> <msg>", Loc.Get("base.help_tell"));
            WriteOnlineCmd("/emote <action>", Loc.Get("base.help_emote"));
            WriteOnlineCmd("/who", Loc.Get("base.help_who"));
            WriteOnlineCmd("/gossip <msg>", Loc.Get("base.help_gossip"));
            WriteOnlineCmd("/guild", Loc.Get("base.help_guild"));
            WriteOnlineCmd("/gcreate <name>", Loc.Get("base.help_gcreate"));
            WriteOnlineCmd("/ginvite <player>", Loc.Get("base.help_ginvite"));
            WriteOnlineCmd("/gleave", Loc.Get("base.help_gleave"));
            WriteOnlineCmd("/gkick <player>", Loc.Get("base.help_gkick"));
            WriteOnlineCmd("/gc <msg>", Loc.Get("base.help_gc"));
            WriteOnlineCmd("/gbank", Loc.Get("base.help_gbank"));
            WriteOnlineCmd("/gdeposit", Loc.Get("base.help_gdeposit"));
            WriteOnlineCmd("/gwithdraw <#>", Loc.Get("base.help_gwithdraw"));
            WriteOnlineCmd("/grank <p> <rank>", Loc.Get("base.help_grank"));
            WriteOnlineCmd("/gtransfer <player>", Loc.Get("base.help_gtransfer"));
            WriteOnlineCmd("/ginfo <guild>", Loc.Get("base.help_ginfo"));

            WriteBoxLine(() => { }, 0);
            var groupCmdsLabel = "  " + Loc.Get("base.help_group_commands");
            WriteBoxLine(() => { terminal.SetColor("white"); terminal.Write(groupCmdsLabel); }, groupCmdsLabel.Length);

            WriteOnlineCmd("/group <player>", Loc.Get("base.help_group"));
            WriteOnlineCmd("/leave", Loc.Get("base.help_leave"));
            WriteOnlineCmd("/disband", Loc.Get("base.help_disband"));
            WriteOnlineCmd("/party", Loc.Get("base.help_party"));
            WriteOnlineCmd("/accept", Loc.Get("base.help_accept"));
            WriteOnlineCmd("/deny", Loc.Get("base.help_deny"));
        }

        terminal.SetColor("bright_cyan");
        terminal.WriteLine("╚══════════════════════════════════════════════════════════════════════════════╝");
        terminal.WriteLine("");

        await terminal.PressAnyKey();
    }

    /// <summary>
    private async Task ShowQuickCommandsHelpSR()
    {
        WriteSectionHeader(Loc.Get("base.quick_commands"), "white");
        terminal.WriteLine("");
        terminal.SetColor("white");
        terminal.WriteLine(Loc.Get("base.help_commands_work"));
        terminal.WriteLine("");
        terminal.WriteLine($"/stats or % {Loc.Get("base.help_stats")}");
        terminal.WriteLine($"/inventory or * {Loc.Get("base.help_inventory")}");
        terminal.WriteLine($"/quests or /q {Loc.Get("base.help_quests")}");
        terminal.WriteLine($"/gold or /g {Loc.Get("base.help_gold")}");
        terminal.WriteLine($"/health or /hp {Loc.Get("base.help_health")}");
        terminal.WriteLine($"/gear or /eq {Loc.Get("base.help_gear")}");
        terminal.WriteLine($"/potion or /pot {Loc.Get("base.help_potion")}");
        terminal.WriteLine($"/herb or /j {Loc.Get("base.help_herb")}");
        terminal.WriteLine($"/materials or /mat {Loc.Get("base.help_materials")}");
        terminal.WriteLine($"/time or /t {Loc.Get("base.help_time")}");
        terminal.WriteLine($"/prefs or /p {Loc.Get("base.help_prefs")}");
        terminal.WriteLine($"/mail {Loc.Get("base.help_mail")}");
        terminal.WriteLine($"/trade {Loc.Get("base.help_trade")}");
        terminal.WriteLine($"/auction {Loc.Get("base.help_auction")}");
        terminal.WriteLine($"/boss {Loc.Get("base.help_boss")}");
        terminal.WriteLine($"/compact {Loc.Get("base.help_compact")}");
        terminal.WriteLine($"/bug {Loc.Get("base.help_bug")}");
        terminal.WriteLine("");
        terminal.WriteLine(Loc.Get("base.help_quick_keys"));
        terminal.WriteLine($"* {Loc.Get("base.help_key_inventory")}");
        terminal.WriteLine($"~ {Loc.Get("base.help_key_prefs")}");
        terminal.WriteLine($"% {Loc.Get("base.help_key_status")}");
        terminal.WriteLine($"? {Loc.Get("base.help_key_help")}");
        terminal.WriteLine($"! {Loc.Get("base.help_key_bug")}");

        if (UsurperRemake.Server.SessionContext.IsActive || OnlineChatSystem.IsActive)
        {
            terminal.WriteLine("");
            terminal.WriteLine(Loc.Get("base.help_online_commands"));
            terminal.WriteLine($"/say <msg> {Loc.Get("base.help_say")}");
            terminal.WriteLine($"/shout <msg> {Loc.Get("base.help_shout")}");
            terminal.WriteLine($"/tell <name> <msg> {Loc.Get("base.help_tell")}");
            terminal.WriteLine($"/emote <action> {Loc.Get("base.help_emote")}");
            terminal.WriteLine($"/who {Loc.Get("base.help_who")}");
            terminal.WriteLine($"/gossip <msg> {Loc.Get("base.help_gossip")}");
            terminal.WriteLine($"/guild - {Loc.Get("base.help_guild")}");
            terminal.WriteLine($"/gcreate <name> - {Loc.Get("base.help_gcreate")}");
            terminal.WriteLine($"/ginvite <player> - {Loc.Get("base.help_ginvite")}");
            terminal.WriteLine($"/gleave - {Loc.Get("base.help_gleave")}");
            terminal.WriteLine($"/gkick <player> - {Loc.Get("base.help_gkick")}");
            terminal.WriteLine($"/gc <msg> - {Loc.Get("base.help_gc")}");
            terminal.WriteLine($"/gbank - {Loc.Get("base.help_gbank")}");
            terminal.WriteLine($"/gdeposit - {Loc.Get("base.help_gdeposit")}");
            terminal.WriteLine($"/gwithdraw <#> - {Loc.Get("base.help_gwithdraw")}");
            terminal.WriteLine($"/grank <p> <rank> - {Loc.Get("base.help_grank")}");
            terminal.WriteLine($"/gtransfer <player> - {Loc.Get("base.help_gtransfer")}");
            terminal.WriteLine($"/ginfo <guild> - {Loc.Get("base.help_ginfo")}");
            terminal.WriteLine("");
            terminal.WriteLine(Loc.Get("base.help_group_commands"));
            terminal.WriteLine($"/group <player> - {Loc.Get("base.help_group")}");
            terminal.WriteLine($"/leave - {Loc.Get("base.help_leave")}");
            terminal.WriteLine($"/disband - {Loc.Get("base.help_disband")}");
            terminal.WriteLine($"/party - {Loc.Get("base.help_party")}");
            terminal.WriteLine($"/accept - {Loc.Get("base.help_accept")}");
            terminal.WriteLine($"/deny - {Loc.Get("base.help_deny")}");
        }

        terminal.WriteLine("");
        await terminal.PressAnyKey();
    }

    /// Show active quests summary
    /// </summary>
    protected virtual async Task ShowActiveQuests()
    {
        terminal.WriteLine("");
        WriteBoxHeader(Loc.Get("base.active_quests"), "bright_magenta");

        var playerName = currentPlayer?.Name2 ?? currentPlayer?.DisplayName ?? "";
        var activeQuests = QuestSystem.GetActiveQuestsForPlayer(playerName);

        if (activeQuests == null || activeQuests.Count == 0)
        {
            terminal.SetColor("gray");
            terminal.WriteLine($"  {Loc.Get("base.no_active_quests")}");
        }
        else
        {
            foreach (var quest in activeQuests.Take(8))
            {
                terminal.SetColor("bright_yellow");
                terminal.WriteLine($"  {quest.Title ?? Loc.Get("base.unknown_quest")}");
                terminal.SetColor("gray");
                terminal.WriteLine($"    {Loc.Get("base.quest_type")}: {quest.GetTargetDescription()}  |  {quest.GetDifficultyString()}  |  {Loc.Get("base.quest_days_left", quest.DaysRemaining)}");

                // Show objectives with progress
                if (quest.Objectives != null && quest.Objectives.Count > 0)
                {
                    foreach (var obj in quest.Objectives)
                    {
                        bool done = obj.IsComplete;
                        string check = done ? "X" : " ";
                        string progress = obj.RequiredProgress > 1
                            ? $" ({obj.CurrentProgress}/{obj.RequiredProgress})"
                            : "";
                        terminal.SetColor(done ? "green" : "white");
                        terminal.WriteLine($"    [{check}] {obj.Description}{progress}");
                    }
                }
                else
                {
                    // Quests without structured objectives — show target info
                    if (!string.IsNullOrEmpty(quest.TargetNPCName))
                    {
                        terminal.SetColor("white");
                        terminal.WriteLine($"    {Loc.Get("base.quest_target")}: {quest.TargetNPCName}");
                    }
                }

                // Show reward
                if (quest.BountyGold > 0)
                {
                    terminal.SetColor("yellow");
                    terminal.WriteLine($"    {Loc.Get("base.quest_reward")}: {quest.BountyGold:N0} {Loc.Get("ui.gold")}");
                }
                else
                {
                    long reward = quest.CalculateReward(currentPlayer?.Level ?? 1);
                    if (reward > 0)
                    {
                        terminal.SetColor("yellow");
                        terminal.WriteLine($"    {Loc.Get("base.quest_reward")}: {reward:N0} {quest.RewardType.ToString().ToLower()}");
                    }
                }
                terminal.WriteLine("");
            }

            if (activeQuests.Count > 8)
            {
                terminal.SetColor("gray");
                terminal.WriteLine($"  {Loc.Get("base.more_quests", activeQuests.Count - 8)}");
            }
        }

        terminal.WriteLine("");
        await terminal.PressAnyKey();
    }

    /// <summary>
    /// Show crafting materials collection
    /// </summary>
    protected async Task ShowMaterials()
    {
        terminal.ClearScreen();
        WriteBoxHeader(Loc.Get("base.crafting_materials"), "bright_magenta");
        terminal.WriteLine("");

        bool hasAny = false;
        foreach (var matDef in GameConfig.CraftingMaterials)
        {
            int count = 0;
            currentPlayer?.CraftingMaterials?.TryGetValue(matDef.Id, out count);

            if (count > 0)
            {
                hasAny = true;
                terminal.SetColor(matDef.Color);
                terminal.Write($"  {matDef.Name}");
                terminal.SetColor("white");
                terminal.Write($" x{count}");
                terminal.SetColor("gray");
                terminal.WriteLine($"  — {matDef.Description}");
                terminal.SetColor("darkgray");
                terminal.WriteLine($"    {Loc.Get("base.mat_found_floors", matDef.FloorMin, matDef.FloorMax)}");
                terminal.WriteLine("");
            }
        }

        if (!hasAny)
        {
            terminal.SetColor("gray");
            terminal.WriteLine($"  {Loc.Get("base.no_materials")}");
            terminal.WriteLine("");
            terminal.SetColor("darkgray");
            terminal.WriteLine($"  {Loc.Get("base.materials_hint1")}");
            terminal.WriteLine($"  {Loc.Get("base.materials_hint2")}");
            terminal.WriteLine($"  {Loc.Get("base.materials_hint3")}");
        }

        terminal.WriteLine("");
        await terminal.PressAnyKey();
    }

    /// <summary>
    /// Show gold status
    /// </summary>
    protected async Task ShowGoldStatus()
    {
        terminal.WriteLine("");
        terminal.SetColor("bright_yellow");
        terminal.Write($"  {Loc.Get("base.gold_on_hand")}: ");
        terminal.SetColor("white");
        terminal.WriteLine($"{currentPlayer?.Gold:N0}");

        var bankBalance = currentPlayer?.BankGold ?? 0;
        terminal.SetColor("bright_cyan");
        terminal.Write($"  {Loc.Get("base.bank_balance")}: ");
        terminal.SetColor("white");
        terminal.WriteLine($"{bankBalance:N0}");

        terminal.SetColor("gray");
        terminal.Write($"  {Loc.Get("base.total_wealth")}: ");
        terminal.SetColor("bright_green");
        terminal.WriteLine($"{(currentPlayer?.Gold ?? 0) + bankBalance:N0}");
        terminal.WriteLine("");
        await terminal.PressAnyKey();
    }

    /// <summary>
    /// Show current game time (single-player: game clock, online: real time)
    /// </summary>
    protected void ShowGameTime()
    {
        terminal.WriteLine("");
        if (!UsurperRemake.BBS.DoorMode.IsOnlineMode && currentPlayer != null)
        {
            // Single-player: show game clock
            var timeStr = DailySystemManager.GetTimeString(currentPlayer);
            var period = DailySystemManager.GetTimePeriodString(currentPlayer);
            var color = DailySystemManager.GetTimePeriodColor(currentPlayer);
            terminal.SetColor(color);
            terminal.WriteLine($"  {Loc.Get("base.time_label")}: {timeStr} ({period})");

            // Show rest availability
            terminal.SetColor("gray");
            if (DailySystemManager.CanRestForNight(currentPlayer))
                terminal.WriteLine($"  {Loc.Get("base.can_rest")}");
            else
                terminal.WriteLine($"  {Loc.Get("base.rest_available_after", GameConfig.RestAvailableHour)}");
        }
        else
        {
            // Online: show real time (server time)
            var now = DateTime.Now;
            terminal.SetColor("white");
            terminal.WriteLine($"  {Loc.Get("base.server_time")}: {now:h:mm tt}");
        }
        terminal.WriteLine("");
    }

    /// <summary>
    /// Show health status
    /// </summary>
    protected async Task ShowHealthStatus()
    {
        terminal.WriteLine("");
        int hpPercent = currentPlayer?.MaxHP > 0 ? (int)(100.0 * currentPlayer.CurrentHP / currentPlayer.MaxHP) : 0;

        terminal.SetColor("bright_red");
        terminal.Write($"  {Loc.Get("status.hp")}: ");
        terminal.SetColor(hpPercent > 50 ? "bright_green" : hpPercent > 25 ? "yellow" : "red");
        terminal.WriteLine($"{currentPlayer?.CurrentHP}/{currentPlayer?.MaxHP} ({hpPercent}%)");

        if (currentPlayer?.IsManaClass == true)
        {
            int mpPercent = currentPlayer.MaxMana > 0 ? (int)(100.0 * currentPlayer.CurrentMana / currentPlayer.MaxMana) : 0;
            terminal.SetColor("bright_blue");
            terminal.Write($"  {Loc.Get("status.mp")}: ");
            terminal.SetColor(mpPercent > 50 ? "bright_cyan" : mpPercent > 25 ? "cyan" : "gray");
            terminal.WriteLine($"{currentPlayer.CurrentMana}/{currentPlayer.MaxMana} ({mpPercent}%)");
        }
        else if (currentPlayer != null)
        {
            int stPercent = currentPlayer.MaxCombatStamina > 0 ? (int)(100.0 * currentPlayer.CurrentCombatStamina / currentPlayer.MaxCombatStamina) : 0;
            terminal.SetColor("bright_yellow");
            terminal.Write($"  {Loc.Get("status.stamina")}: ");
            terminal.SetColor(stPercent > 50 ? "bright_yellow" : stPercent > 25 ? "yellow" : "gray");
            terminal.WriteLine($"{currentPlayer.CurrentCombatStamina}/{currentPlayer.MaxCombatStamina} ({stPercent}%)");
        }

        // Fatigue display (single-player only)
        if (!UsurperRemake.BBS.DoorMode.IsOnlineMode && currentPlayer != null)
        {
            var (fatigueLabel, fatigueColor) = currentPlayer.GetFatigueTier();
            terminal.SetColor("white");
            terminal.Write($"  {Loc.Get("base.fatigue_label")}: ");
            if (!string.IsNullOrEmpty(fatigueLabel))
            {
                terminal.SetColor(fatigueColor);
                terminal.Write($"{fatigueLabel} ");
            }
            terminal.SetColor("gray");
            terminal.Write($"({currentPlayer.Fatigue}/100)");
            // Show penalty description
            if (currentPlayer.Fatigue >= GameConfig.FatigueExhaustedThreshold)
                terminal.WriteLine($" — {Loc.Get("base.fatigue_exhausted_penalty")}");
            else if (currentPlayer.Fatigue >= GameConfig.FatigueTiredThreshold)
                terminal.WriteLine($" — {Loc.Get("base.fatigue_tired_penalty")}");
            else
                terminal.WriteLine("");
        }

        // Fame display
        if (currentPlayer != null)
        {
            terminal.SetColor("white");
            terminal.Write($"  {Loc.Get("base.fame_label")}: ");
            string fameLabel = currentPlayer.Fame switch
            {
                >= 200 => Loc.Get("base.fame_legendary"),
                >= 100 => Loc.Get("base.fame_renowned"),
                >= 50 => Loc.Get("base.fame_well_known"),
                >= 20 => Loc.Get("base.fame_notable"),
                >= 1 => Loc.Get("base.fame_unknown"),
                _ => Loc.Get("base.fame_nobody")
            };
            string fameColor = currentPlayer.Fame switch
            {
                >= 200 => "bright_yellow",
                >= 100 => "bright_cyan",
                >= 50 => "cyan",
                >= 20 => "green",
                _ => "gray"
            };
            terminal.SetColor(fameColor);
            terminal.WriteLine($"{fameLabel} ({currentPlayer.Fame})");
        }

        // Weekly Power Rankings display
        if (currentPlayer != null && !string.IsNullOrEmpty(currentPlayer.RivalName))
        {
            terminal.SetColor("cyan");
            terminal.WriteLine($"  {Loc.Get("base.rival_label")}: {currentPlayer.RivalName} ({Loc.Get("base.lv_label")} {currentPlayer.RivalLevel})");
        }
        if (currentPlayer != null && currentPlayer.WeeklyRank > 0)
        {
            terminal.SetColor("cyan");
            terminal.WriteLine($"  {Loc.Get("base.weekly_rank")}: #{currentPlayer.WeeklyRank}");
        }

        terminal.WriteLine("");
        await terminal.PressAnyKey();
    }

    /// <summary>
    /// Use a healing potion outside of combat via /potion quick command
    /// </summary>
    protected async Task UseQuickPotion()
    {
        terminal.WriteLine("");
        if (currentPlayer == null)
        {
            terminal.WriteLine($"  {Loc.Get("base.no_active_character")}", "gray");
            await terminal.PressAnyKey();
            return;
        }

        if (currentPlayer.HP >= currentPlayer.MaxHP)
        {
            terminal.SetColor("cyan");
            terminal.WriteLine($"  {Loc.Get("base.already_full_health")}");
            terminal.WriteLine("");
            await terminal.PressAnyKey();
            return;
        }

        if (currentPlayer.Healing <= 0)
        {
            terminal.SetColor("yellow");
            terminal.WriteLine($"  {Loc.Get("base.no_healing_potions")}");
            terminal.SetColor("gray");
            terminal.WriteLine($"  {Loc.Get("base.visit_healer")}");
            terminal.WriteLine("");
            await terminal.PressAnyKey();
            return;
        }

        // Use one potion
        long healAmount = 30 + currentPlayer.Level * 5 + Random.Shared.Next(10, 30);
        healAmount = Math.Min(healAmount, currentPlayer.MaxHP - currentPlayer.HP);
        currentPlayer.HP += healAmount;
        currentPlayer.Healing--;
        currentPlayer.Statistics?.RecordPotionUsed(healAmount);

        terminal.SetColor("bright_green");
        terminal.WriteLine($"  {Loc.Get("base.potion_healed", healAmount)}");
        terminal.SetColor("cyan");
        terminal.WriteLine($"  {Loc.Get("status.hp")}: {currentPlayer.HP}/{currentPlayer.MaxHP}  |  {Loc.Get("base.potions_remaining")}: {currentPlayer.Healing}/{currentPlayer.MaxPotions}");
        terminal.WriteLine("");
        await terminal.PressAnyKey();
    }

    /// <summary>
    /// Process user choice - returns true if should exit location
    /// </summary>
    protected virtual async Task<bool> ProcessChoice(string choice)
    {
        if (string.IsNullOrWhiteSpace(choice))
            return false;

        var upperChoice = choice.ToUpper().Trim();

        // Check for exits first
        foreach (var exit in PossibleExits)
        {
            if (upperChoice == GetLocationKey(exit))
            {
                await NavigateToLocation(exit);
                return true;
            }
        }

        // Check for numbered actions
        if (int.TryParse(upperChoice, out int actionIndex))
        {
            if (actionIndex > 0 && actionIndex <= LocationActions.Count)
            {
                await ExecuteLocationAction(actionIndex - 1);
                return false;
            }
        }

        // Check for special commands
        switch (upperChoice)
        {
            case "%":
                await ShowStatus();
                break;
            case "*":
                await ShowInventory();
                break;
            case "?":
                // Help/menu already shown
                break;
            case "Q":
                if (LocationId != GameLocation.MainStreet)
                {
                    await NavigateToLocation(GameLocation.MainStreet);
                    return true;
                }
                break;
            case "0":
            case "TALK":
                await TalkToNPC();
                break;
            case "~":
            case "PREFS":
            case "PREFERENCES":
                await ShowPreferencesMenu();
                break;
            default:
                terminal.SetColor("red");
                terminal.WriteLine(Loc.Get("base.invalid_choice", choice));
                terminal.SetColor("gray");
                terminal.Write($"{Loc.Get("base.try_hint")}: [");
                terminal.SetColor("bright_yellow");
                terminal.Write("%");
                terminal.SetColor("gray");
                terminal.Write("]");
                terminal.Write(Loc.Get("base.qc_status_suffix"));
                terminal.Write(", [");
                terminal.SetColor("bright_yellow");
                terminal.Write("*");
                terminal.SetColor("gray");
                terminal.Write("] ");
                terminal.Write(Loc.Get("base.qc_inventory"));

                if (LocationId != GameLocation.MainStreet)
                {
                    terminal.Write(", [");
                    terminal.SetColor("bright_yellow");
                    terminal.Write("R");
                    terminal.SetColor("gray");
                    terminal.Write("]");
                    terminal.Write(Loc.Get("base.qc_return_suffix"));
                }

                terminal.Write($", {Loc.Get("base.or")} [");
                terminal.SetColor("bright_yellow");
                terminal.Write("?");
                terminal.SetColor("gray");
                terminal.WriteLine($"] {Loc.Get("base.for_help")}");
                await Task.Delay(2000);
                break;
        }

        return false;
    }

    /// <summary>
    /// Execute a location-specific action
    /// </summary>
    protected virtual async Task ExecuteLocationAction(int actionIndex)
    {
        // Override in derived classes
        terminal.WriteLine(Loc.Get("base.nothing_happens"), "gray");
        await Task.Delay(1000);
    }

    /// <summary>
    /// Navigate to another location
    /// </summary>
    protected virtual async Task NavigateToLocation(GameLocation destination)
    {
        terminal.WriteLine(Loc.Get("base.heading_to", GetLocationName(destination)), "yellow");
        await Task.Delay(500);

        // Throw exception to signal location change
        throw new LocationExitException(destination);
    }

    /// <summary>
    /// Show the inventory screen for managing equipment
    /// </summary>
    protected virtual async Task ShowInventory()
    {
        var inventorySystem = new InventorySystem(terminal, currentPlayer);
        await inventorySystem.ShowInventory();
    }

    /// <summary>
    /// Show quick preferences menu (accessible from any location via ~)
    /// </summary>
    protected virtual async Task ShowPreferencesMenu()
    {
        bool exitPrefs = false;

        while (!exitPrefs)
        {
            terminal.ClearScreen();

            if (currentPlayer.ScreenReaderMode)
            {
                // Screen reader friendly: plain text, no box-drawing, no color switching
                terminal.WriteLine(Loc.Get("prefs.title"));
                terminal.WriteLine("");

                string speedDesc = currentPlayer.CombatSpeed switch
                {
                    CombatSpeed.Instant => Loc.Get("prefs.combat_speed.instant"),
                    CombatSpeed.Fast => Loc.Get("prefs.combat_speed.fast"),
                    _ => Loc.Get("prefs.combat_speed.normal")
                };

                terminal.WriteLine(Loc.Get("prefs.current_settings"));
                terminal.WriteLine($"  {Loc.Get("prefs.combat_speed")}: {speedDesc}");
                terminal.WriteLine($"  {Loc.Get("prefs.auto_heal")}: {(currentPlayer.AutoHeal ? Loc.Get("prefs.enabled") : Loc.Get("prefs.disabled"))}");
                terminal.WriteLine($"  {Loc.Get("prefs.skip_intimate")}: {(currentPlayer.SkipIntimateScenes ? Loc.Get("prefs.enabled") : Loc.Get("prefs.disabled"))}");
                terminal.WriteLine($"  {Loc.Get("prefs.screen_reader")}: {Loc.Get("prefs.enabled")}");
                terminal.WriteLine($"  {Loc.Get("prefs.color_theme")}: {ColorTheme.GetThemeName(currentPlayer.ColorTheme)}");
                terminal.WriteLine($"  {Loc.Get("prefs.auto_level")}: {(currentPlayer.AutoLevelUp ? Loc.Get("prefs.enabled") : Loc.Get("prefs.disabled"))}");
                terminal.WriteLine($"  {Loc.Get("prefs.compact_mode")}: {(currentPlayer.CompactMode ? Loc.Get("prefs.enabled") : Loc.Get("prefs.disabled"))}");
                terminal.WriteLine($"  {Loc.Get("prefs.auto_equip")}: {(currentPlayer.AutoEquipDisabled ? Loc.Get("prefs.disabled") : Loc.Get("prefs.enabled"))}");
                terminal.WriteLine("");

                string srDateFormat = currentPlayer.DateFormatPreference switch { 1 => "DD/MM/YYYY", 2 => "YYYY-MM-DD", _ => "MM/DD/YYYY" };

                terminal.WriteLine($"{Loc.Get("prefs.options")}");
                terminal.WriteLine(Loc.Get("base.prefs_gameplay"));
                terminal.WriteLine($"  1. {Loc.Get("prefs.toggle", Loc.Get("prefs.combat_speed"))}");
                terminal.WriteLine($"  2. {Loc.Get("prefs.toggle", Loc.Get("prefs.auto_heal"))}");
                terminal.WriteLine($"  8. {Loc.Get("prefs.toggle", Loc.Get("prefs.auto_level"))}");
                terminal.WriteLine($"  A. {Loc.Get("prefs.toggle", Loc.Get("prefs.auto_equip"))}");
                terminal.WriteLine(Loc.Get("base.prefs_display"));
                terminal.WriteLine($"  6. {Loc.Get("prefs.color_theme")}");
                terminal.WriteLine($"  9. {Loc.Get("prefs.toggle", Loc.Get("prefs.compact_mode"))}");
                terminal.WriteLine($"  D. {Loc.Get("base.prefs_date_format")} ({srDateFormat})");
                if (IsRunningInWezTerm())
                    terminal.WriteLine($"  7. {Loc.Get("prefs.terminal_font")}");
                terminal.WriteLine(Loc.Get("base.prefs_accessibility"));
                terminal.WriteLine($"  4. {Loc.Get("prefs.toggle", Loc.Get("prefs.screen_reader"))}");
                terminal.WriteLine($"  B. {Loc.Get("prefs.language")} ({UsurperRemake.Systems.Loc.GetLanguageName(currentPlayer.Language)})");
                terminal.WriteLine(Loc.Get("base.prefs_character"));
                terminal.WriteLine($"  3. {Loc.Get("prefs.toggle", Loc.Get("prefs.skip_intimate"))}");
                terminal.WriteLine($"  O. {Loc.Get("prefs.orientation")}");
                terminal.WriteLine($"  T. {Loc.Get("base.prefs_title")}");
                terminal.WriteLine($"0. {Loc.Get("prefs.back")}");
                terminal.WriteLine("");
            }
            else
            {
                // Standard visual menu — organized into categories
                WriteBoxHeader(Loc.Get("prefs.title"), "bright_yellow");
                terminal.WriteLine("");

                // Helper to write a menu option line
                void WriteMenuOption(string key, string label)
                {
                    terminal.Write("[");
                    terminal.SetColor("bright_yellow");
                    terminal.Write(key);
                    terminal.SetColor("white");
                    terminal.WriteLine($"] {label}");
                }

                string onOff(bool v) => v ? Loc.Get("prefs.on") : Loc.Get("prefs.off");
                string speedDesc = currentPlayer.CombatSpeed switch
                {
                    CombatSpeed.Instant => Loc.Get("prefs.combat_speed.instant"),
                    CombatSpeed.Fast => Loc.Get("prefs.combat_speed.fast"),
                    _ => Loc.Get("prefs.combat_speed.normal")
                };
                string dateFormatName = currentPlayer.DateFormatPreference switch
                {
                    1 => "DD/MM/YYYY",
                    2 => "YYYY-MM-DD",
                    _ => "MM/DD/YYYY"
                };

                // -- GAMEPLAY --
                terminal.SetColor("bright_cyan");
                terminal.WriteLine($"  {Loc.Get("base.prefs_gameplay_header")}");
                terminal.SetColor("white");
                WriteMenuOption("1", $"{Loc.Get("prefs.combat_speed")}: {speedDesc}");
                WriteMenuOption("2", $"{Loc.Get("prefs.auto_heal")}: {onOff(currentPlayer.AutoHeal)}");
                WriteMenuOption("8", $"{Loc.Get("prefs.auto_level")}: {onOff(currentPlayer.AutoLevelUp)}");
                WriteMenuOption("A", $"{Loc.Get("prefs.auto_equip")}: {onOff(!currentPlayer.AutoEquipDisabled)}");
                terminal.WriteLine("");

                // -- DISPLAY --
                terminal.SetColor("bright_cyan");
                terminal.WriteLine($"  {Loc.Get("base.prefs_display_header")}");
                terminal.SetColor("white");
                WriteMenuOption("6", $"{Loc.Get("prefs.color_theme")}: {ColorTheme.GetThemeName(currentPlayer.ColorTheme)}");
                WriteMenuOption("9", $"{Loc.Get("prefs.compact_mode")}: {onOff(currentPlayer.CompactMode)}");
                WriteMenuOption("D", $"{Loc.Get("base.prefs_date_format")}: {dateFormatName}");
                if (IsRunningInWezTerm())
                    WriteMenuOption("7", $"{Loc.Get("prefs.terminal_font")}: {ReadCurrentFont()}");
                terminal.WriteLine("");

                // -- ACCESSIBILITY --
                terminal.SetColor("bright_cyan");
                terminal.WriteLine($"  {Loc.Get("base.prefs_accessibility_header")}");
                terminal.SetColor("white");
                WriteMenuOption("4", $"{Loc.Get("prefs.screen_reader")}: {onOff(currentPlayer.ScreenReaderMode)}");
                WriteMenuOption("B", $"{Loc.Get("prefs.language")}: {UsurperRemake.Systems.Loc.GetLanguageName(currentPlayer.Language)}");
                terminal.WriteLine("");

                // -- CHARACTER --
                terminal.SetColor("bright_cyan");
                terminal.WriteLine($"  {Loc.Get("base.prefs_character_header")}");
                terminal.SetColor("white");
                WriteMenuOption("3", $"{Loc.Get("prefs.skip_intimate")}: {(currentPlayer.SkipIntimateScenes ? Loc.Get("prefs.skip_intimate.on") : Loc.Get("prefs.skip_intimate.off"))}");
                WriteMenuOption("O", $"{Loc.Get("prefs.orientation")}: {GetOrientationLabel(currentPlayer.Orientation)}");
                WriteMenuOption("T", $"{Loc.Get("base.prefs_title")}: {currentPlayer.NobleTitle ?? Loc.Get("ui.none")}");
                terminal.WriteLine("");

                WriteMenuOption("0", Loc.Get("prefs.back"));
                terminal.WriteLine("");
            }

            var choice = await terminal.GetInput(Loc.Get("ui.choice"));

            switch (choice.Trim().ToUpperInvariant())
            {
                case "1":
                    // Cycle through combat speeds
                    currentPlayer.CombatSpeed = currentPlayer.CombatSpeed switch
                    {
                        CombatSpeed.Normal => CombatSpeed.Fast,
                        CombatSpeed.Fast => CombatSpeed.Instant,
                        _ => CombatSpeed.Normal
                    };
                    string newSpeed = currentPlayer.CombatSpeed switch
                    {
                        CombatSpeed.Instant => Loc.Get("prefs.combat_speed.instant"),
                        CombatSpeed.Fast => Loc.Get("prefs.combat_speed.fast"),
                        _ => Loc.Get("prefs.combat_speed.normal")
                    };
                    terminal.WriteLine(Loc.Get("base.combat_speed_set", newSpeed), "green");
                    await GameEngine.Instance.SaveCurrentGame();
                    await Task.Delay(800);
                    break;

                case "2":
                    currentPlayer.AutoHeal = !currentPlayer.AutoHeal;
                    terminal.WriteLine(Loc.Get("base.pref_auto_heal_toggled", currentPlayer.AutoHeal ? Loc.Get("prefs.enabled") : Loc.Get("prefs.disabled")), "green");
                    await GameEngine.Instance.SaveCurrentGame();
                    await Task.Delay(800);
                    break;

                case "3":
                    currentPlayer.SkipIntimateScenes = !currentPlayer.SkipIntimateScenes;
                    if (currentPlayer.SkipIntimateScenes)
                    {
                        terminal.WriteLine(Loc.Get("base.pref_intimate_fade"), "green");
                    }
                    else
                    {
                        terminal.WriteLine(Loc.Get("base.pref_intimate_full"), "green");
                    }
                    await GameEngine.Instance.SaveCurrentGame();
                    await Task.Delay(1000);
                    break;

                case "4":
                    currentPlayer.ScreenReaderMode = !currentPlayer.ScreenReaderMode;
                    GameConfig.ScreenReaderMode = currentPlayer.ScreenReaderMode;
                    if (currentPlayer.ScreenReaderMode)
                    {
                        terminal.WriteLine(Loc.Get("base.pref_sr_enabled"), "green");
                        terminal.WriteLine(Loc.Get("base.pref_sr_enabled_desc"), "white");
                    }
                    else
                    {
                        terminal.WriteLine(Loc.Get("base.pref_sr_disabled"), "green");
                        terminal.WriteLine(Loc.Get("base.pref_sr_disabled_desc"), "white");
                    }
                    await GameEngine.Instance.SaveCurrentGame();
                    await Task.Delay(1200);
                    break;

                case "6":
                    // Cycle color theme
                    var nextTheme = ColorTheme.NextTheme(currentPlayer.ColorTheme);
                    currentPlayer.ColorTheme = nextTheme;
                    ColorTheme.Current = nextTheme;
                    // Force screen clear even in MUD mode so new theme colors are visible immediately
                    terminal.WriteRawAnsi("\x1b[2J\x1b[H");
                    terminal.WriteLine(Loc.Get("base.pref_theme_set", ColorTheme.GetThemeName(nextTheme)), "green");
                    terminal.WriteLine($"  {ColorTheme.GetThemeDescription(nextTheme)}", "white");
                    await GameEngine.Instance.SaveCurrentGame();
                    await Task.Delay(800);
                    break;

                case "7":
                    // Cycle terminal font (only when running inside WezTerm)
                    if (IsRunningInWezTerm())
                    {
                        var fonts = new[] { "JetBrains Mono", "Cascadia Code", "Fira Code", "Iosevka", "Hack", "Kelmscott Mono" };
                        var currentFont = ReadCurrentFont();
                        int idx = Array.IndexOf(fonts, currentFont);
                        int next = (idx + 1) % fonts.Length;
                        WriteTerminalFont(fonts[next]);
                        terminal.WriteLine(Loc.Get("base.pref_font_set", fonts[next]), "green");
                        terminal.WriteLine(Loc.Get("base.pref_font_update"), "white");
                        await Task.Delay(800);
                    }
                    break;

                case "8":
                    currentPlayer.AutoLevelUp = !currentPlayer.AutoLevelUp;
                    if (currentPlayer.AutoLevelUp)
                    {
                        terminal.WriteLine(Loc.Get("base.pref_autolevel_enabled"), "green");
                        terminal.WriteLine(Loc.Get("base.pref_autolevel_enabled_desc"), "white");
                    }
                    else
                    {
                        terminal.WriteLine(Loc.Get("base.pref_autolevel_disabled"), "green");
                        terminal.WriteLine(Loc.Get("base.pref_autolevel_disabled_desc"), "white");
                    }
                    await GameEngine.Instance.SaveCurrentGame();
                    await Task.Delay(1000);
                    break;

                case "9":
                    currentPlayer.CompactMode = !currentPlayer.CompactMode;
                    GameConfig.CompactMode = currentPlayer.CompactMode;
                    if (currentPlayer.CompactMode)
                    {
                        terminal.WriteLine(Loc.Get("base.pref_compact_enabled"), "green");
                        terminal.WriteLine(Loc.Get("base.pref_compact_enabled_desc"), "white");
                        terminal.WriteLine(Loc.Get("base.pref_compact_enabled_keys"), "white");
                    }
                    else
                    {
                        terminal.WriteLine(Loc.Get("base.pref_compact_disabled"), "green");
                        terminal.WriteLine(Loc.Get("base.pref_compact_disabled_desc"), "white");
                    }
                    await GameEngine.Instance.SaveCurrentGame();
                    await Task.Delay(1000);
                    break;

                case "A":
                    currentPlayer.AutoEquipDisabled = !currentPlayer.AutoEquipDisabled;
                    if (currentPlayer.AutoEquipDisabled)
                    {
                        terminal.WriteLine(Loc.Get("base.pref_autoequip_disabled"), "green");
                        terminal.WriteLine(Loc.Get("base.pref_autoequip_disabled_desc"), "white");
                    }
                    else
                    {
                        terminal.WriteLine(Loc.Get("base.pref_autoequip_enabled"), "green");
                        terminal.WriteLine(Loc.Get("base.pref_autoequip_enabled_desc"), "white");
                    }
                    await GameEngine.Instance.SaveCurrentGame();
                    await Task.Delay(1000);
                    break;

                case "D":
                    // Cycle date format: MM/DD → DD/MM → YYYY-MM-DD → MM/DD
                    currentPlayer.DateFormatPreference = (currentPlayer.DateFormatPreference + 1) % 3;
                    GameConfig.DateFormat = currentPlayer.DateFormatPreference;
                    string newDateFormat = currentPlayer.DateFormatPreference switch
                    {
                        1 => "DD/MM/YYYY",
                        2 => "YYYY-MM-DD",
                        _ => "MM/DD/YYYY"
                    };
                    terminal.WriteLine(Loc.Get("base.date_format_set", newDateFormat), "green");
                    terminal.WriteLine($"  {Loc.Get("base.date_format_example")}: {GameConfig.FormatDate(DateTime.Now, currentPlayer.DateFormatPreference)}", "gray");
                    await GameEngine.Instance.SaveCurrentGame();
                    await Task.Delay(800);
                    break;

                case "B":
                    terminal.WriteLine("");
                    terminal.WriteLine(Loc.Get("prefs.select_language"), "bright_yellow");
                    terminal.WriteLine("");
                    var langs = UsurperRemake.Systems.Loc.AvailableLanguages;
                    for (int li = 0; li < langs.Length; li++)
                    {
                        var marker = langs[li].Code == currentPlayer.Language ? " *" : "";
                        terminal.WriteLine($"  {li + 1}. {langs[li].Name}{marker}");
                    }
                    terminal.WriteLine("");
                    var langChoice = await terminal.GetInput(Loc.Get("ui.your_choice"));
                    if (int.TryParse(langChoice.Trim(), out int langIdx) && langIdx >= 1 && langIdx <= langs.Length)
                    {
                        var selectedLang = langs[langIdx - 1].Code;
                        currentPlayer.Language = selectedLang;
                        GameConfig.Language = selectedLang;
                        terminal.WriteLine(Loc.Get("prefs.language_set", UsurperRemake.Systems.Loc.GetLanguageName(selectedLang)), "green");
                        // Invalidate cached dungeon floor so rooms regenerate in new language
                        var dungeonLoc = LocationManager.Instance?.GetLocation(GameLocation.Dungeons) as DungeonLocation;
                        dungeonLoc?.InvalidateFloorCache();
                        await GameEngine.Instance.SaveCurrentGame();
                        await Task.Delay(800);
                    }
                    break;

                case "T":
                    terminal.WriteLine("");
                    terminal.SetColor("bright_yellow");
                    terminal.WriteLine($"  {Loc.Get("base.select_title")}");
                    terminal.WriteLine("");

                    // Gather all available titles
                    var availableTitles = new List<string>();

                    // King/Queen title (while on the throne)
                    if (currentPlayer.King)
                        availableTitles.Add(currentPlayer.Sex == CharacterSex.Female ? "Queen" : "King");

                    // Knight title
                    if (currentPlayer.IsKnighted)
                        availableTitles.Add(currentPlayer.Sex == CharacterSex.Female ? "Dame" : "Sir");

                    // MetaProgression titles (earned across NG+ cycles)
                    var metaTitles = MetaProgressionSystem.Instance.UnlockedTitles;
                    foreach (var mt in metaTitles)
                        if (!availableTitles.Contains(mt)) availableTitles.Add(mt);

                    if (availableTitles.Count == 0)
                    {
                        terminal.SetColor("gray");
                        terminal.WriteLine($"  {Loc.Get("base.no_titles")}");
                        terminal.WriteLine($"  {Loc.Get("base.titles_hint")}");
                        await Task.Delay(2000);
                    }
                    else
                    {
                        terminal.SetColor("white");
                        terminal.WriteLine($"  0. ({Loc.Get("ui.none")}) — {Loc.Get("base.remove_title")}");
                        for (int ti = 0; ti < availableTitles.Count; ti++)
                        {
                            string marker = availableTitles[ti] == currentPlayer.NobleTitle ? " *" : "";
                            terminal.WriteLine($"  {ti + 1}. {availableTitles[ti]}{marker}");
                        }
                        terminal.WriteLine("");
                        var titleChoice = await terminal.GetInput($"  {Loc.Get("base.select_prompt")}");
                        if (titleChoice.Trim() == "0")
                        {
                            currentPlayer.NobleTitle = null;
                            terminal.SetColor("green");
                            terminal.WriteLine($"  {Loc.Get("base.title_removed")}");
                            await GameEngine.Instance.SaveCurrentGame();
                            await Task.Delay(1000);
                        }
                        else if (int.TryParse(titleChoice.Trim(), out int titleIdx) && titleIdx >= 1 && titleIdx <= availableTitles.Count)
                        {
                            currentPlayer.NobleTitle = availableTitles[titleIdx - 1];
                            terminal.SetColor("green");
                            terminal.WriteLine($"  {Loc.Get("base.title_set", currentPlayer.NobleTitle, currentPlayer.DisplayName)}");
                            await GameEngine.Instance.SaveCurrentGame();
                            await Task.Delay(1000);
                        }
                    }
                    break;

                case "O":
                    terminal.WriteLine("");
                    terminal.SetColor("bright_yellow");
                    terminal.WriteLine($"  {Loc.Get("creation.orientation")}");
                    terminal.WriteLine("");
                    terminal.SetColor("white");
                    terminal.WriteLine($"  {Loc.Get("creation.orientation_1")}");
                    terminal.WriteLine($"  {Loc.Get("creation.orientation_2")}");
                    terminal.WriteLine($"  {Loc.Get("creation.orientation_3")}");
                    terminal.WriteLine($"  {Loc.Get("creation.orientation_4")}");
                    terminal.WriteLine("");
                    var oriChoice = await terminal.GetInput(Loc.Get("ui.your_choice"));
                    switch (oriChoice.Trim())
                    {
                        case "1":
                            currentPlayer.Orientation = SexualOrientation.Straight;
                            break;
                        case "2":
                            currentPlayer.Orientation = SexualOrientation.Gay;
                            break;
                        case "3":
                            currentPlayer.Orientation = SexualOrientation.Bisexual;
                            break;
                        case "4":
                            currentPlayer.Orientation = SexualOrientation.Asexual;
                            break;
                    }
                    terminal.SetColor("green");
                    terminal.WriteLine($"  {Loc.Get("base.orientation_set", GetOrientationLabel(currentPlayer.Orientation))}");
                    await GameEngine.Instance.SaveCurrentGame();
                    await Task.Delay(1000);
                    break;

                case "0":
                case "":
                    exitPrefs = true;
                    // Force location redraw so theme/compact/language changes are visible immediately
                    _locationEntryDisplayed = false;
                    // MUD mode: clear the screen so stale preferences menu doesn't linger
                    if (UsurperRemake.BBS.DoorMode.IsMudServerMode)
                        terminal.WriteRawAnsi("\x1b[2J\x1b[H");
                    break;

                default:
                    terminal.WriteLine(Loc.Get("base.invalid_choice_simple"), "red");
                    await Task.Delay(500);
                    break;
            }
        }
    }

    // ═══════════════════════════════════════════════════════════════════════════
    // Screen Reader Accessibility Helpers
    // ═══════════════════════════════════════════════════════════════════════════

    /// <summary>
    /// Whether the current player has screen reader mode enabled.
    /// </summary>
    protected bool IsScreenReader => currentPlayer?.ScreenReaderMode == true;

    /// <summary>
    /// v0.57.2 — Party Inventory Viewer (Phase 1 of the "mule" pattern).
    /// Shows a list of party members (companions, spouse/lover, team NPCs) and lets the player
    /// view what's in each one's inventory and take items back to their own inventory. Phase 1 is
    /// view + take only; "give to companion" comes later.
    ///
    /// Called from TeamCorner, Home, Inn, and the Dungeon party-management menu. Caller supplies
    /// the appropriate party list (e.g. Home supplies spouse+lover+companions, Dungeon supplies
    /// current teammates). Empty-party case is handled — method just shows a message and returns.
    /// </summary>
    protected async Task ShowPartyInventoryViewer(List<Character> partyMembers)
    {
        if (partyMembers == null) partyMembers = new List<Character>();

        // Filter out grouped players (they manage their own inventory) and echoes (read-only snapshots)
        partyMembers = partyMembers
            .Where(m => m != null && !m.IsGroupedPlayer && !m.IsEcho)
            .ToList();

        if (partyMembers.Count == 0)
        {
            terminal.SetColor("gray");
            terminal.WriteLine(Loc.Get("party_inv.no_members"));
            await terminal.PressAnyKey();
            return;
        }

        while (true)
        {
            terminal.ClearScreen();
            WriteBoxHeader(Loc.Get("party_inv.header"), "bright_cyan", 60);
            terminal.WriteLine("");
            terminal.SetColor("gray");
            terminal.WriteLine(Loc.Get("party_inv.subtitle"));
            terminal.WriteLine("");

            for (int i = 0; i < partyMembers.Count; i++)
            {
                var m = partyMembers[i];
                int invCount = m.Inventory?.Count ?? 0;

                terminal.SetColor("bright_yellow");
                terminal.Write($"  {i + 1}. ");
                terminal.SetColor("white");
                terminal.Write($"{m.DisplayName,-24}");
                terminal.SetColor("gray");
                terminal.Write($" {Loc.Get("ui.level")} {m.Level,-3} ");

                if (invCount == 0)
                {
                    terminal.SetColor("dark_gray");
                    terminal.WriteLine(Loc.Get("party_inv.empty_count"));
                }
                else
                {
                    terminal.SetColor(invCount > 10 ? "bright_green" : "cyan");
                    terminal.WriteLine(Loc.Get("party_inv.item_count", invCount));
                }
            }

            terminal.WriteLine("");
            terminal.SetColor("cyan");
            terminal.WriteLine(Loc.Get("party_inv.select_member"));
            terminal.WriteLine("");
            terminal.Write(Loc.Get("ui.your_choice"));
            terminal.SetColor("white");

            var input = (await terminal.ReadLineAsync()).Trim().ToUpper();
            if (input == "Q" || string.IsNullOrEmpty(input)) return;

            if (!int.TryParse(input, out int idx) || idx < 1 || idx > partyMembers.Count)
            {
                terminal.SetColor("yellow");
                terminal.WriteLine(Loc.Get("ui.invalid_choice"));
                await Task.Delay(900);
                continue;
            }

            await ShowSinglePartyMemberInventory(partyMembers[idx - 1]);
        }
    }

    /// <summary>
    /// Render one NPC's inventory list with take-back option. Items taken back are subject to
    /// the player's normal inventory cap — if the player is full, the take is refused.
    /// </summary>
    private async Task ShowSinglePartyMemberInventory(Character member)
    {
        while (true)
        {
            terminal.ClearScreen();
            WriteBoxHeader(Loc.Get("party_inv.member_header", member.DisplayName), "bright_cyan", 60);
            terminal.WriteLine("");

            var inv = member.Inventory ?? new List<Item>();
            if (inv.Count == 0)
            {
                terminal.SetColor("gray");
                terminal.WriteLine(Loc.Get("party_inv.empty_inventory", member.DisplayName));
                terminal.WriteLine("");
                await terminal.PressAnyKey();
                return;
            }

            for (int i = 0; i < inv.Count; i++)
            {
                var item = inv[i];
                string display = item.IsIdentified
                    ? item.Name
                    : LootGenerator.GetUnidentifiedName(item);

                terminal.SetColor("bright_yellow");
                terminal.Write($"  {i + 1,2}. ");
                terminal.SetColor(item.IsCursed ? "bright_red" : "white");
                terminal.Write($"{display,-34}");
                terminal.SetColor("gray");
                if (item.Value > 0)
                    terminal.Write($" {item.Value,8:N0}g");
                if (item.IsCursed)
                {
                    terminal.SetColor("bright_red");
                    terminal.Write(" [" + Loc.Get("ui.cursed") + "]");
                }
                terminal.WriteLine("");
            }

            terminal.WriteLine("");
            terminal.SetColor("cyan");
            terminal.WriteLine(Loc.Get("party_inv.take_prompt"));
            terminal.WriteLine("");
            terminal.Write(Loc.Get("ui.your_choice"));
            terminal.SetColor("white");

            var input = (await terminal.ReadLineAsync()).Trim().ToUpper();
            if (input == "Q" || string.IsNullOrEmpty(input)) return;

            if (!int.TryParse(input, out int idx) || idx < 1 || idx > inv.Count)
            {
                terminal.SetColor("yellow");
                terminal.WriteLine(Loc.Get("ui.invalid_choice"));
                await Task.Delay(900);
                continue;
            }

            var chosenItem = inv[idx - 1];

            if (currentPlayer.IsInventoryFull)
            {
                terminal.SetColor("red");
                terminal.WriteLine(Loc.Get("party_inv.player_inventory_full"));
                await Task.Delay(1500);
                continue;
            }

            // Transfer: remove from NPC, add to player.
            member.Inventory!.Remove(chosenItem);
            currentPlayer.Inventory.Add(chosenItem);

            // v0.57.4: forensic log for take-back — complement to combat [T]'s
            // transfer log, so round-trips like "gave the item to companion,
            // later took it back" are fully traceable through logs.
            string takeBackCategory = member is NPC ? "NPC_BAG" : "COMPANION_BAG";
            DebugLogger.Instance.LogInfo(takeBackCategory,
                $"PartyInv TAKE-BACK ← {member.DisplayName}: \"{chosenItem.Name}\" (bag remaining: {member.Inventory.Count})");

            terminal.SetColor("bright_green");
            string takenName = chosenItem.IsIdentified
                ? chosenItem.Name
                : LootGenerator.GetUnidentifiedName(chosenItem);
            terminal.WriteLine(Loc.Get("party_inv.taken", takenName, member.DisplayName));

            // Persist the change — NPC inventories live on the canonical NPC (world_state in online mode)
            // so take-back needs to flush both save paths, mirroring the equip/unequip patterns.
            CombatEngine.SyncNPCTeammateToActiveNPCs(member);
            SaveSystem.Instance.ResetAutoSaveThrottle();
            await SaveSystem.Instance.AutoSave(currentPlayer);
            if (DoorMode.IsOnlineMode && OnlineStateManager.Instance != null)
            {
                try { await OnlineStateManager.Instance.SaveAllSharedState(); }
                catch (Exception ex) { DebugLogger.Instance.LogError("PARTYINV", $"SaveAllSharedState failed after take-back: {ex.Message}"); }
            }

            await Task.Delay(1200);
        }
    }

    /// <summary>
    /// Write a centered box header (╔═══╗ / ║ TITLE ║ / ╚═══╝).
    /// In screen reader mode, outputs plain text title only.
    /// </summary>
    protected void WriteBoxHeader(string title, string color = "bright_cyan", int width = 78)
    {
        if (IsScreenReader)
        {
            terminal.WriteLine(title);
            return;
        }
        terminal.SetColor(color);
        terminal.WriteLine($"╔{new string('═', width)}╗");
        int l = (width - title.Length) / 2;
        int r = width - title.Length - l;
        terminal.WriteLine($"║{new string(' ', l)}{title}{new string(' ', r)}║");
        terminal.WriteLine($"╚{new string('═', width)}╝");
    }

    /// <summary>
    /// Write a section header like "═══ Title ═══".
    /// In screen reader mode, outputs plain text title only.
    /// </summary>
    protected void WriteSectionHeader(string title, string color = "white")
    {
        if (IsScreenReader)
        {
            terminal.WriteLine(title);
            return;
        }
        terminal.SetColor(color);
        terminal.WriteLine($"═══ {title} ═══");
    }

    /// <summary>
    /// Write a thin divider line (───). In screen reader mode, outputs nothing.
    /// </summary>
    protected void WriteDivider(int width = 78, string color = "darkgray")
    {
        if (!IsScreenReader)
        {
            terminal.SetColor(color);
            terminal.WriteLine(new string('─', width));
        }
    }

    /// <summary>
    /// Write a thick divider line (═══). In screen reader mode, outputs nothing.
    /// </summary>
    protected void WriteThickDivider(int width = 78, string color = "darkgray")
    {
        if (!IsScreenReader)
        {
            terminal.SetColor(color);
            terminal.WriteLine(new string('═', width));
        }
    }

    /// <summary>
    /// Write a single menu option. In screen reader mode uses "K. Label" format.
    /// In normal mode uses color-switched [K] Label format.
    /// </summary>
    protected void WriteSRMenuOption(string key, string label, bool available = true, string keyColor = "bright_yellow")
    {
        if (IsScreenReader)
        {
            terminal.WriteLine($"{key}. {label}");
            return;
        }
        string textColor = available ? "white" : "dark_gray";
        string actualKeyColor = available ? keyColor : "dark_gray";
        terminal.SetColor("darkgray");
        terminal.Write("[");
        terminal.SetColor(actualKeyColor);
        terminal.Write(key);
        terminal.SetColor("darkgray");
        terminal.Write("] ");
        terminal.SetColor(textColor);
        terminal.WriteLine(label);
    }

    /// <summary>
    /// Check if we're running inside WezTerm (which supports font switching).
    /// </summary>
    internal static bool IsRunningInWezTerm()
    {
        var termProgram = Environment.GetEnvironmentVariable("TERM_PROGRAM");
        return string.Equals(termProgram, "WezTerm", StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>
    /// Read the current terminal font preference from font-choice.txt.
    /// Returns "JetBrains Mono" if no preference file exists.
    /// </summary>
    internal static string ReadCurrentFont()
    {
        try
        {
            var path = Path.Combine(AppContext.BaseDirectory, "font-choice.txt");
            if (File.Exists(path))
            {
                var line = File.ReadAllText(path).Trim();
                if (!string.IsNullOrEmpty(line)) return line;
            }
        }
        catch (Exception ex) { DebugLogger.Instance.LogError("LOCATION", $"[ReadCurrentFont] Failed to read font-choice.txt: {ex.Message}"); }
        return "JetBrains Mono";
    }

    /// <summary>
    /// Write the terminal font preference to font-choice.txt.
    /// WezTerm auto-reloads its config and picks up the change.
    /// </summary>
    internal static void WriteTerminalFont(string fontName)
    {
        try
        {
            var path = Path.Combine(AppContext.BaseDirectory, "font-choice.txt");
            File.WriteAllText(path, fontName);
        }
        catch (Exception ex) { DebugLogger.Instance.LogError("LOCATION", $"[WriteTerminalFont] Failed to write font-choice.txt: {ex.Message}"); }
    }

    /// <summary>
    /// Talk to an NPC at the current location
    /// </summary>
    protected virtual async Task TalkToNPC()
    {
        var npcsHere = GetLiveNPCsAtLocation();

        // Also include any static LocationNPCs, but exclude special NPCs that have
        // their own dedicated interaction paths (e.g., Seth Able has [F] Challenge)
        var allNPCs = new List<NPC>(LocationNPCs.Where(n => !n.IsSpecialNPC));
        foreach (var npc in npcsHere)
        {
            if (!npc.IsSpecialNPC && !allNPCs.Any(n => n.Name2 == npc.Name2))
                allNPCs.Add(npc);
        }

        if (allNPCs.Count == 0)
        {
            terminal.SetColor("gray");
            terminal.WriteLine(Loc.Get("base.no_one_to_talk"));
            await Task.Delay(1500);
            return;
        }

        terminal.ClearScreen();
        WriteBoxHeader(Loc.Get("base.people_nearby"), "bright_cyan");
        terminal.WriteLine("");

        terminal.SetColor("yellow");
        terminal.WriteLine($"  {Loc.Get("base.who_talk_to")}");
        terminal.WriteLine("");

        // List NPCs with numbers
        for (int i = 0; i < allNPCs.Count; i++)
        {
            var npc = allNPCs[i];

            // Get relationship status with player
            int relationLevel = RelationshipSystem.GetRelationshipStatus(currentPlayer, npc);
            var (relationColor, relationText, relationSymbol) = GetRelationshipDisplayInfo(relationLevel);

            terminal.SetColor("white");
            terminal.Write("  [");
            terminal.SetColor("bright_yellow");
            terminal.Write($"{i + 1}");
            terminal.SetColor("white");
            terminal.Write("] ");

            // Name color based on relationship rather than alignment
            terminal.SetColor(relationColor);
            terminal.Write($"{npc.Name2}");

            // Show class/level
            terminal.SetColor("gray");
            terminal.Write($" - {Loc.Get("base.guard_level")} {npc.Level} {npc.ClassName}");

            // Show relationship status in brackets with color
            terminal.Write(" [");
            terminal.SetColor(relationColor);
            terminal.Write(relationText);
            if (!string.IsNullOrEmpty(relationSymbol))
            {
                terminal.SetColor("bright_red");
                terminal.Write($" {relationSymbol}");
            }
            terminal.SetColor("gray");
            terminal.WriteLine("]");
        }

        terminal.WriteLine("");
        terminal.SetColor("white");
        terminal.Write("  [");
        terminal.SetColor("bright_yellow");
        terminal.Write("0");
        terminal.SetColor("white");
        terminal.WriteLine($"] {Loc.Get("base.never_mind")}");
        terminal.WriteLine("");

        string choice = await terminal.GetInput(Loc.Get("base.talk_to_who"));

        if (int.TryParse(choice, out int targetIndex) && targetIndex >= 1 && targetIndex <= allNPCs.Count)
        {
            var npc = allNPCs[targetIndex - 1];
            await InteractWithNPC(npc);
        }
        else if (choice != "0")
        {
            terminal.SetColor("gray");
            terminal.WriteLine(Loc.Get("base.decide_not_talk"));
            await Task.Delay(1000);
        }
    }

    /// <summary>
    /// Have a conversation with an NPC
    /// </summary>
    protected virtual async Task InteractWithNPC(NPC npc)
    {
        npc.IsInConversation = true; // Protect from world sim during interaction
        try
        {
            bool stayInConversation = true;
            bool isFirstGreeting = true;

            while (stayInConversation)
            {
                terminal.ClearScreen();
                WriteBoxHeader(Loc.Get("base.talking_to", npc.Name2), "bright_cyan");
                terminal.WriteLine("");

                // Show NPC portrait (skip for screen readers)
                if (!currentPlayer.ScreenReaderMode)
                {
                    var portrait = PortraitGenerator.GeneratePortrait(npc);
                    ANSIArt.DisplayArt(terminal, portrait);
                    terminal.WriteLine("");
                }

                // Show NPC info
                terminal.SetColor("gray");
                string sexDisplay = npc.Sex == CharacterSex.Female ? Loc.Get("base.female") : Loc.Get("base.male");
                terminal.WriteLine($"  {Loc.Get("base.guard_level")} {npc.Level} {npc.Race} {sexDisplay} {npc.ClassName}");
                terminal.WriteLine($"  {GetAlignmentDisplay(npc)}");
                terminal.WriteLine("");

                // Get NPC's greeting (only on first interaction)
                if (isFirstGreeting)
                {
                    // Update talk-to-NPC quest objectives
                    QuestSystem.OnNPCTalkedTo(currentPlayer, npc.Name);
                    QuestSystem.OnNPCTalkedTo(currentPlayer, npc.Name2);

                    string greeting = npc.GetGreeting(currentPlayer);
                    terminal.SetColor("yellow");
                    terminal.WriteLine($"  {Loc.Get("base.npc_says", npc.Name2)}");
                    terminal.SetColor("white");
                    terminal.WriteLine($"  \"{greeting}\"");
                    terminal.WriteLine("");
                    isFirstGreeting = false;
                }

                // Show interaction options
                terminal.SetColor("cyan");
                terminal.WriteLine($"  {Loc.Get("base.what_do_you_do")}");
                terminal.WriteLine("");

                terminal.SetColor("darkgray");
                terminal.Write("  [");
                terminal.SetColor("bright_yellow");
                terminal.Write("1");
                terminal.SetColor("darkgray");
                terminal.Write("]");
                terminal.SetColor("white");
                terminal.WriteLine($" {Loc.Get("base.chat_with_them")}");

                terminal.SetColor("darkgray");
                terminal.Write("  [");
                terminal.SetColor("bright_yellow");
                terminal.Write("2");
                terminal.SetColor("darkgray");
                terminal.Write("]");
                terminal.SetColor("white");
                terminal.WriteLine($" {Loc.Get("base.ask_rumors")}");

                terminal.SetColor("darkgray");
                terminal.Write("  [");
                terminal.SetColor("bright_yellow");
                terminal.Write("3");
                terminal.SetColor("darkgray");
                terminal.Write("]");
                terminal.SetColor("white");
                terminal.WriteLine($" {Loc.Get("base.ask_dungeons")}");

                // Only show challenge option if they're a fighter type
                if (npc.Level > 0 && npc.IsAlive)
                {
                    terminal.SetColor("darkgray");
                    terminal.Write("  [");
                    terminal.SetColor("bright_yellow");
                    terminal.Write("4");
                    terminal.SetColor("darkgray");
                    terminal.Write("]");
                    terminal.SetColor("white");
                    terminal.WriteLine($" {Loc.Get("base.challenge_duel")}");
                }

                // Full conversation option (visual novel style)
                terminal.SetColor("darkgray");
                terminal.Write("  [");
                terminal.SetColor("bright_yellow");
                terminal.Write("5");
                terminal.SetColor("darkgray");
                terminal.Write("]");
                terminal.SetColor("bright_magenta");
                terminal.WriteLine($" {Loc.Get("base.deep_conversation")}");

                // Attack option (murder/assassination)
                if (npc.Level > 0 && npc.IsAlive && !npc.IsStoryNPC && !npc.King)
                {
                    terminal.SetColor("darkgray");
                    terminal.Write("  [");
                    terminal.SetColor("bright_yellow");
                    terminal.Write("6");
                    terminal.SetColor("darkgray");
                    terminal.Write("]");
                    terminal.SetColor("dark_red");
                    terminal.WriteLine($" {Loc.Get("base.attack_npc")}");
                }

                terminal.WriteLine("");
                terminal.SetColor("white");
                terminal.Write("  [");
                terminal.SetColor("bright_yellow");
                terminal.Write("0");
                terminal.SetColor("white");
                terminal.WriteLine($"] {Loc.Get("base.walk_away")}");

                // Debug option
                terminal.SetColor("dark_gray");
                terminal.Write("  [");
                terminal.SetColor("bright_yellow");
                terminal.Write("9");
                terminal.SetColor("dark_gray");
                terminal.WriteLine($"] {Loc.Get("base.debug_personality")}");
                terminal.WriteLine("");

                string action = await GetChoice();

                switch (action)
                {
                    case "1":
                        await ChatWithNPC(npc);
                        break;
                    case "2":
                        await AskForRumors(npc);
                        break;
                    case "3":
                        await AskAboutDungeons(npc);
                        break;
                    case "4":
                        if (npc.Level > 0 && npc.IsAlive)
                        {
                            await ChallengeNPC(npc);
                            stayInConversation = false; // Exit after combat
                        }
                        break;
                    case "5":
                        // Full visual novel style conversation
                        await UsurperRemake.Systems.VisualNovelDialogueSystem.Instance.StartConversation(currentPlayer, npc, terminal);
                        break;
                    case "6":
                        if (npc.Level > 0 && npc.IsAlive && !npc.IsStoryNPC && !npc.King)
                        {
                            await AttackNPC(npc);
                            stayInConversation = false;
                        }
                        break;
                    case "9":
                        await ShowNPCDebugTraits(npc);
                        break;
                    case "0":
                    default:
                        // Show NPC's farewell using dynamic dialogue system
                        string farewell = npc.GetFarewell((currentPlayer as Player)!);
                        terminal.SetColor("yellow");
                        terminal.WriteLine($"  {Loc.Get("base.npc_says", npc.Name2)}");
                        terminal.SetColor("white");
                        terminal.WriteLine($"  \"{farewell}\"");
                        terminal.WriteLine("");
                        terminal.SetColor("gray");
                        terminal.WriteLine($"  {Loc.Get("base.nod_walk_away")}");
                        await Task.Delay(1500);
                        stayInConversation = false;
                        break;
                }
            }
        }
        finally { npc.IsInConversation = false; }
    }

    /// <summary>
    /// Have a casual chat with an NPC
    /// Uses the Dynamic NPC Dialogue System for personality-driven conversation
    /// </summary>
    private async Task ChatWithNPC(NPC npc)
    {
        npc.IsInConversation = true; // Protect from world sim while chatting
        try
        {
            terminal.WriteLine("");

            // Use the dynamic dialogue system for small talk
            var player = (currentPlayer as Player)!;
            string smallTalk = npc.GetSmallTalk(player);

            terminal.SetColor("yellow");
            terminal.WriteLine($"  {Loc.Get("base.npc_says", npc.Name2)}");
            terminal.SetColor("white");
            terminal.WriteLine($"  \"{smallTalk}\"");
            await Task.Delay(800);

            // Sometimes add a second line of dialogue for variety
            if (Random.Shared.NextDouble() < 0.5)
            {
                await Task.Delay(600);
                string moreTalk = npc.GetSmallTalk(player);
                if (moreTalk != smallTalk) // Avoid repetition
                {
                    terminal.WriteLine($"  \"{moreTalk}\"");
                    await Task.Delay(600);
                }
            }

            // Small relationship boost for friendly chat
            RelationshipSystem.UpdateRelationship(currentPlayer, npc, 1, 1, false, false);

            terminal.WriteLine("");
            await terminal.PressAnyKey();
        }
        finally { npc.IsInConversation = false; }
    }

    /// <summary>
    /// Generate contextual chat lines based on NPC personality
    /// </summary>
    private string[] GenerateNPCChat(NPC npc)
    {
        var random = Random.Shared;
        var chatOptions = new List<string[]>();

        // Class-specific chat
        switch (npc.Class)
        {
            case CharacterClass.Warrior:
                chatOptions.Add(new[] { Loc.Get("base.chat_warrior_1a"), Loc.Get("base.chat_warrior_1b") });
                chatOptions.Add(new[] { Loc.Get("base.chat_warrior_2a"), Loc.Get("base.chat_warrior_2b") });
                break;
            case CharacterClass.Magician:
                chatOptions.Add(new[] { Loc.Get("base.chat_magician_1a"), Loc.Get("base.chat_magician_1b") });
                chatOptions.Add(new[] { Loc.Get("base.chat_magician_2a"), Loc.Get("base.chat_magician_2b") });
                break;
            case CharacterClass.Cleric:
                chatOptions.Add(new[] { Loc.Get("base.chat_cleric_1a"), Loc.Get("base.chat_cleric_1b") });
                chatOptions.Add(new[] { Loc.Get("base.chat_cleric_2a"), Loc.Get("base.chat_cleric_2b") });
                break;
            case CharacterClass.Assassin:
                chatOptions.Add(new[] { Loc.Get("base.chat_assassin_1a"), Loc.Get("base.chat_assassin_1b") });
                chatOptions.Add(new[] { Loc.Get("base.chat_assassin_2a"), Loc.Get("base.chat_assassin_2b") });
                break;
            default:
                chatOptions.Add(new[] { Loc.Get("base.chat_default_1a"), Loc.Get("base.chat_default_1b") });
                chatOptions.Add(new[] { Loc.Get("base.chat_default_2a"), Loc.Get("base.chat_default_2b") });
                break;
        }

        // Alignment-specific additions
        if (npc.Darkness > npc.Chivalry + 500)
        {
            chatOptions.Add(new[] { Loc.Get("base.chat_evil_1"), Loc.Get("base.chat_evil_2"), Loc.Get("base.chat_evil_3") });
        }
        else if (npc.Chivalry > npc.Darkness + 500)
        {
            chatOptions.Add(new[] { Loc.Get("base.chat_good_1"), Loc.Get("base.chat_good_2") });
        }

        return chatOptions[random.Next(chatOptions.Count)];
    }

    /// <summary>
    /// Ask NPC for rumors
    /// </summary>
    private async Task AskForRumors(NPC npc)
    {
        terminal.WriteLine("");
        terminal.SetColor("cyan");
        terminal.WriteLine($"  {Loc.Get("base.ask_rumors_to", npc.Name2)}");
        terminal.WriteLine("");

        var random = Random.Shared;
        var rumors = GetRumors();
        var selectedRumor = rumors[random.Next(rumors.Length)];

        terminal.SetColor("yellow");
        terminal.WriteLine($"  {Loc.Get("base.npc_whispers", npc.Name2)}");
        terminal.SetColor("white");
        terminal.WriteLine($"  \"{selectedRumor}\"");

        terminal.WriteLine("");
        await terminal.PressAnyKey();
    }

    /// <summary>
    /// Get list of rumors NPCs can share
    /// </summary>
    private string[] GetRumors()
    {
        return new[]
        {
            Loc.Get("base.rumor_seals"),
            Loc.Get("base.rumor_old_gods"),
            Loc.Get("base.rumor_stranger"),
            Loc.Get("base.rumor_king"),
            Loc.Get("base.rumor_creature"),
            Loc.Get("base.rumor_lower_levels"),
            Loc.Get("base.rumor_dark_alley"),
            Loc.Get("base.rumor_temple"),
            Loc.Get("base.rumor_castle"),
            Loc.Get("base.rumor_wave"),
            Loc.Get("base.rumor_manwe"),
            Loc.Get("base.rumor_healers"),
            Loc.Get("base.rumor_team"),
            Loc.Get("base.rumor_veloura"),
            Loc.Get("base.rumor_npc_items")
        };
    }

    /// <summary>
    /// Ask NPC about dungeons
    /// </summary>
    private async Task AskAboutDungeons(NPC npc)
    {
        terminal.WriteLine("");
        terminal.SetColor("cyan");
        terminal.WriteLine($"  {Loc.Get("base.ask_dungeons_to", npc.Name2)}");
        terminal.WriteLine("");

        // Give advice based on NPC's level/experience
        terminal.SetColor("yellow");
        terminal.WriteLine($"  {Loc.Get("base.npc_says", npc.Name2)}");
        terminal.SetColor("white");

        if (npc.Level > currentPlayer.Level + 10)
        {
            terminal.WriteLine($"  \"{Loc.Get("base.dungeon_not_ready")}\"");
            terminal.WriteLine($"  \"{Loc.Get("base.dungeon_get_stronger", currentPlayer.Level)}\"");
        }
        else if (npc.Level > currentPlayer.Level)
        {
            terminal.WriteLine($"  \"{Loc.Get("base.dungeon_upper_ok")}\"");
            terminal.WriteLine($"  \"{Loc.Get("base.dungeon_watch_floor", Math.Min(npc.Level, 10))}\"");
        }
        else
        {
            terminal.WriteLine($"  \"{Loc.Get("base.dungeon_you_experienced")}\"");
            terminal.WriteLine($"  \"{Loc.Get("base.dungeon_good_luck")}\"");
        }

        terminal.WriteLine("");
        await terminal.PressAnyKey();
    }

    /// <summary>
    /// DEBUG: Show NPC personality and relationship traits
    /// </summary>
    private async Task ShowNPCDebugTraits(NPC npc)
    {
        terminal.ClearScreen();
        WriteBoxHeader($"DEBUG: {npc.Name2}", "bright_magenta");
        terminal.WriteLine("");

        var profile = npc.Personality;
        if (profile == null)
        {
            terminal.SetColor("red");
            terminal.WriteLine($"  {Loc.Get("base.no_personality")}");
            await terminal.PressAnyKey();
            return;
        }

        // Basic identity
        terminal.SetColor("bright_cyan");
        terminal.WriteLine($"  === {Loc.Get("base.debug_identity")} ===");
        terminal.SetColor("white");
        terminal.WriteLine($"  {Loc.Get("base.debug_gender")}    {profile.Gender}");
        terminal.WriteLine($"  {Loc.Get("base.debug_orientation")} {profile.Orientation}");
        terminal.WriteLine("");

        // Faction affiliation
        terminal.SetColor("bright_yellow");
        terminal.WriteLine($"  === {Loc.Get("base.debug_faction")} ===");
        terminal.SetColor("white");
        terminal.Write($"  {Loc.Get("base.debug_faction_label")}            ");
        if (npc.NPCFaction.HasValue)
        {
            var factionColor = npc.NPCFaction.Value switch
            {
                UsurperRemake.Systems.Faction.TheCrown => "bright_yellow",
                UsurperRemake.Systems.Faction.TheFaith => "bright_cyan",
                UsurperRemake.Systems.Faction.TheShadows => "bright_magenta",
                _ => "white"
            };
            terminal.SetColor(factionColor);
            terminal.WriteLine(npc.NPCFaction.Value.ToString());
        }
        else
        {
            terminal.SetColor("gray");
            terminal.WriteLine(Loc.Get("base.none_independent"));
        }
        terminal.WriteLine("");

        // Relationship preferences
        terminal.SetColor("bright_yellow");
        terminal.WriteLine($"  === {Loc.Get("base.debug_rel_style")} ===");
        terminal.SetColor("white");
        terminal.WriteLine($"  {Loc.Get("base.debug_rel_pref")}  {profile.RelationshipPref}");
        terminal.WriteLine($"  {Loc.Get("base.debug_intimate")}     {profile.IntimateStyle}");
        terminal.WriteLine("");

        // Romance traits
        terminal.SetColor("bright_green");
        terminal.WriteLine($"  === {Loc.Get("base.debug_romance_traits")} ===");
        terminal.SetColor("white");

        // Color code based on value - using inline method
        PrintTraitLine("Romanticism:", profile.Romanticism, "(romantic vs practical)");
        PrintTraitLine("Sensuality:", profile.Sensuality, "(physical desire)");
        PrintTraitLine("Passion:", profile.Passion, "(intensity)");
        PrintTraitLine("Flirtatiousness:", profile.Flirtatiousness, "(likely to flirt)");
        PrintTraitLine("Commitment:", profile.Commitment, "(marriage-minded)");
        PrintTraitLine("Tenderness:", profile.Tenderness, "(gentle vs rough)");
        PrintTraitLine("Jealousy:", profile.Jealousy, "(possessiveness)");
        terminal.WriteLine("");

        // Polyamory assessment
        terminal.SetColor("bright_magenta");
        terminal.WriteLine($"  === {Loc.Get("base.debug_polyamory")} ===");

        bool openToPolyamory = profile.RelationshipPref == RelationshipPreference.OpenRelationship ||
                               profile.RelationshipPref == RelationshipPreference.Polyamorous;
        bool lowJealousy = profile.Jealousy < 0.4f;
        bool lowCommitment = profile.Commitment < 0.5f;

        terminal.SetColor("white");
        terminal.Write($"  {Loc.Get("base.debug_open_poly")} ");
        if (openToPolyamory && lowJealousy)
        {
            terminal.SetColor("bright_green");
            terminal.WriteLine(Loc.Get("base.poly_very_likely"));
        }
        else if (openToPolyamory || (lowJealousy && lowCommitment))
        {
            terminal.SetColor("yellow");
            terminal.WriteLine(Loc.Get("base.poly_possible"));
        }
        else
        {
            terminal.SetColor("red");
            terminal.WriteLine(Loc.Get("base.poly_unlikely"));
        }

        terminal.WriteLine("");
        terminal.SetColor("dark_gray");
        terminal.WriteLine($"  {Loc.Get("base.debug_poly_factors")}");
        terminal.WriteLine("");

        await terminal.PressAnyKey();
        await InteractWithNPC(npc); // Return to interaction menu
    }

    /// <summary>
    /// Helper to print a trait line with color coding based on value
    /// </summary>
    private void PrintTraitLine(string name, float value, string description)
    {
        string color = value >= 0.7f ? "bright_green" : value >= 0.4f ? "yellow" : "gray";
        terminal.SetColor(color);
        terminal.Write($"  {name,-18} ");
        terminal.SetColor("white");
        terminal.Write($"{value:F2}");
        terminal.SetColor("dark_gray");
        terminal.WriteLine($"  {description}");
    }
    /// <summary>
    /// Challenge NPC to a duel
    /// </summary>
    private async Task ChallengeNPC(NPC npc)
    {
        terminal.WriteLine("");
        terminal.SetColor("bright_red");
        terminal.WriteLine(Loc.Get("base.duel_challenge", npc.Name2));
        terminal.WriteLine("");

        // Check if NPC accepts
        bool accepts = ShouldNPCAcceptDuel(npc);

        if (!accepts)
        {
            terminal.SetColor("yellow");
            terminal.WriteLine(Loc.Get("base.npc_says_label", npc.Name2));
            terminal.SetColor("white");

            if (npc.Level > currentPlayer.Level + 5)
            {
                terminal.WriteLine(Loc.Get("base.duel_decline_strong"));
            }
            else if (npc.Level < currentPlayer.Level - 5)
            {
                terminal.WriteLine(Loc.Get("base.duel_decline_weak"));
            }
            else
            {
                terminal.WriteLine(Loc.Get("base.duel_decline_busy"));
            }

            await Task.Delay(2000);
            return;
        }

        terminal.SetColor("yellow");
        terminal.WriteLine(Loc.Get("base.duel_accepted", npc.Name2));
        terminal.WriteLine("");

        terminal.SetColor("white");
        terminal.WriteLine(Loc.Get("base.duel_honorably"));
        terminal.WriteLine("");

        await Task.Delay(1500);

        // Initiate combat through StreetEncounterSystem
        var result = await StreetEncounterSystem.Instance.AttackCharacter(currentPlayer, npc, terminal);

        if (result.Victory)
        {
            terminal.SetColor("bright_green");
            terminal.WriteLine("\n  " + Loc.Get("base.duel_victory", npc.Name2));
            currentPlayer.PKills++;

            // Small reputation boost for honorable duel
            currentPlayer.Chivalry += 5;
        }
        else
        {
            terminal.SetColor("red");
            terminal.WriteLine("\n  " + Loc.Get("base.duel_defeat", npc.Name2));
            currentPlayer.PDefeats++;
        }

        await Task.Delay(2000);
    }

    /// <summary>
    /// Attack an NPC — murder/assassination attempt. No acceptance check.
    /// On victory: NPC is permanently killed (until respawn), player steals gold.
    /// </summary>
    private async Task AttackNPC(NPC npc)
    {
        terminal.WriteLine("");

        // Check if player's teammates include this NPC (same team name)
        if (!string.IsNullOrEmpty(currentPlayer.Team) &&
            !string.IsNullOrEmpty(npc.Team) &&
            currentPlayer.Team.Equals(npc.Team, StringComparison.OrdinalIgnoreCase))
        {
            terminal.SetColor("yellow");
            terminal.WriteLine(Loc.Get("base.attack_teammate"));
            await Task.Delay(1500);
            return;
        }

        // Check if this NPC is a bounty target — bounty kills skip murder consequences
        string npcName = npc.Name2 ?? npc.Name ?? "";
        var bountyInitiator = QuestSystem.GetActiveBountyInitiator(currentPlayer.Name2, npcName);
        bool isBountyTarget = bountyInitiator != null;

        // === MURDER DAILY CAP (non-bounty kills only, v0.57.6) ===
        // Block the interaction up front so the player doesn't waste time
        // reading the full murder-consequences prompt, then get denied. Cap
        // only applies to non-bounty kills — sanctioned bounty contracts
        // aren't "murder" for the purposes of this limit.
        if (!isBountyTarget && currentPlayer.MurdersToday >= GameConfig.MaxMurdersPerDay)
        {
            terminal.SetColor("bright_red");
            terminal.WriteLine("");
            terminal.WriteLine($"  {Loc.Get("base.murder_daily_cap_reached", GameConfig.MaxMurdersPerDay)}");
            terminal.SetColor("gray");
            terminal.WriteLine($"  {Loc.Get("base.murder_daily_cap_hint")}");
            terminal.WriteLine("");
            await Task.Delay(2000);
            return;
        }

        // === MURDER WARNING (non-bounty kills only) ===
        if (!isBountyTarget)
        {
            terminal.SetColor("bright_red");
            terminal.WriteLine("");
            terminal.WriteLine("  ══════════════════════════════════════════");
            terminal.WriteLine($"              {Loc.Get("base.murder_capital_offense")}");
            terminal.WriteLine("  ══════════════════════════════════════════");
            terminal.SetColor("red");
            terminal.WriteLine("");
            terminal.WriteLine($"  {Loc.Get("base.murder_serious_crime")}");
            terminal.WriteLine($"  {Loc.Get("base.murder_crown_respond")}");
            terminal.WriteLine("");
            terminal.SetColor("bright_yellow");
            terminal.WriteLine($"  {Loc.Get("base.murder_arrested")}");
            terminal.WriteLine($"  {Loc.Get("base.murder_executed_chance")}");
            terminal.WriteLine($"  {Loc.Get("base.murder_prison_2_days")}");
            terminal.SetColor("red");
            terminal.WriteLine("");
            terminal.WriteLine($"  {Loc.Get("base.murder_cannot_undo")}");
            terminal.WriteLine("  ══════════════════════════════════════════");
            terminal.WriteLine("");
            terminal.SetColor("white");
            terminal.Write($"  {Loc.Get("base.murder_confirm_yn")} ");
            var murderConfirm = await terminal.GetInput("");
            if (murderConfirm.Trim().ToUpper() != "Y")
            {
                terminal.SetColor("green");
                terminal.WriteLine($"  {Loc.Get("base.murder_walk_away")}");
                await Task.Delay(1500);
                return;
            }

            // Second confirmation for severity
            terminal.SetColor("bright_red");
            terminal.Write($"  {Loc.Get("base.murder_type_confirm")} ");
            var finalConfirm = await terminal.GetInput("");
            if (finalConfirm.Trim().ToUpper() != "MURDER")
            {
                terminal.SetColor("green");
                terminal.WriteLine($"  {Loc.Get("base.murder_step_back")}");
                await Task.Delay(1500);
                return;
            }
        }

        // Warn if NPC is much higher level
        if (npc.Level > currentPlayer.Level + 10)
        {
            terminal.SetColor("bright_red");
            terminal.WriteLine(Loc.Get("base.attack_dangerous", npc.Name2, npc.Level));
            terminal.Write(Loc.Get("base.attack_confirm"));
            var confirm = await terminal.GetInput("");
            if (confirm.Trim().ToUpper() != "Y")
            {
                terminal.SetColor("gray");
                terminal.WriteLine(Loc.Get("base.attack_reconsider"));
                await Task.Delay(1000);
                return;
            }
        }

        terminal.SetColor("dark_red");
        terminal.WriteLine(Loc.Get("base.attack_lunge", npc.Name2));
        terminal.SetColor("red");
        terminal.WriteLine(Loc.Get("base.attack_treacherous"));
        terminal.WriteLine("");
        await Task.Delay(1500);

        // Initiate murder combat through StreetEncounterSystem
        var result = await StreetEncounterSystem.Instance.MurderNPC(currentPlayer, npc, terminal, LocationId);

        if (result.Victory)
        {
            terminal.SetColor("dark_red");
            terminal.WriteLine("\n  " + Loc.Get("base.attack_killed", npc.Name2));

            if (result.GoldGained > 0)
            {
                terminal.SetColor("yellow");
                terminal.WriteLine(Loc.Get("base.attack_looted", result.GoldGained));
            }

            currentPlayer.PKills++;
            // v0.57.0 paired alignment movement — murder gains darkness AND burns chivalry
            UsurperRemake.Systems.AlignmentSystem.Instance.ChangeAlignment(currentPlayer, GameConfig.MurderDarknessGain, isGood: false, reason: "murder");

            terminal.SetColor("gray");
            terminal.WriteLine(Loc.Get("base.attack_darkness", GameConfig.MurderDarknessGain));

            // === MURDER CONSEQUENCES (non-bounty kills only) ===
            // Darkness is already applied above (line 4803) — the PR #82 merge
            // added a second ChangeAlignment call here which double-counted the
            // gain (e.g. MurderDarknessGain=250 → +500 per murder). Removed.
            if (!result.WasBountyKill)
            {
                // v0.57.6: increment daily cap counter. Placed BEFORE
                // ApplyMurderConsequences because that method can throw
                // "CHARACTER_EXECUTED" to drop the session, which would
                // skip any post-call work. Counter should reflect the
                // murder regardless of what the crown does next.
                currentPlayer.MurdersToday++;
                await ApplyMurderConsequences(currentPlayer, npc);
            }
        }
        else
        {
            terminal.SetColor("red");
            terminal.WriteLine("\n  " + Loc.Get("base.attack_overpowered", npc.Name2));
            terminal.SetColor("gray");
            terminal.WriteLine(Loc.Get("base.attack_remember"));
            currentPlayer.PDefeats++;
            // v0.57.0 — failed murder attempt also goes through paired movement (intent matters).
            UsurperRemake.Systems.AlignmentSystem.Instance.ChangeAlignment(currentPlayer, 10, isGood: false, reason: "attempted murder");

            // Even failed murder attempts have consequences
            if (!isBountyTarget)
            {
                terminal.SetColor("bright_red");
                terminal.WriteLine($"\n  {Loc.Get("base.murder_guards_arrest")}");
                if (currentPlayer is Player p)
                {
                    p.DaysInPrison = 1; // 1 day for attempted murder
                    terminal.SetColor("yellow");
                    terminal.WriteLine($"  {Loc.Get("base.murder_prison_1_day")}");
                }
            }
        }

        await Task.Delay(2000);
    }
    /// <summary>
    /// Apply severe consequences for non-bounty NPC murder:
    /// 50% chance of execution or prison (2 days).
    /// Called from BaseLocation.AttackNPC and MagicShopLocation.CastDeathSpell.
    /// </summary>
    internal async Task ApplyMurderConsequences(Character player, NPC victim)
    {
        await Task.Delay(1500);

        terminal.SetColor("bright_red");
        terminal.WriteLine("");
        terminal.WriteLine("  ══════════════════════════════════════════");
        terminal.WriteLine($"         {Loc.Get("base.crowns_justice")}");
        terminal.WriteLine("  ══════════════════════════════════════════");
        terminal.WriteLine("");
        terminal.SetColor("white");
        terminal.WriteLine($"  {Loc.Get("base.guards_surround")}");
        terminal.SetColor("red");
        terminal.WriteLine($"  \"{Loc.Get("base.arrest_for_murder", victim.Name2 ?? victim.Name)}\"");
        terminal.WriteLine("");
        await Task.Delay(2000);

        terminal.SetColor("red");
        terminal.WriteLine(Loc.Get("street_encounter.guard.halt"));
        terminal.SetColor("magenta");
        terminal.WriteLine(Loc.Get("street_encounter.guard.looking_for_you", player.Darkness));
        terminal.WriteLine("");

        terminal.Write("  [", "white");
        terminal.Write("S", "bright_yellow");
        terminal.Write($"]{Loc.Get("street_encounter.guard.opt_surrender")}  [", "white");
        terminal.Write("F", "bright_yellow");
        terminal.Write($"]{Loc.Get("street_encounter.hostile.opt_fight")}", "white");

        // v0.57.8: re-prompt on invalid keystroke. Original code treated any
        // non-S input as "F" which silently threw the player into the guards
        // fight on a misclick.
        string choice;
        while (true)
        {
            choice = (await terminal.GetKeyInput()).ToUpperInvariant();
            if (choice == "S" || choice == "F") break;
        }

        bool captured;

        if (choice == "S")
        {
            terminal.SetColor("yellow");
            terminal.WriteLine(Loc.Get("street_encounter.guard.arrested"));
            terminal.WriteLine("");

            captured = true;
        }
        else
        {
            var guards = new Monster[5];
            int level = currentPlayer.Level + 5;
            for (int i = 0; i < 5; i++)
            {
                long guardHP = (long)(100 * level + Math.Pow(level, 1.4) * 25);
                var guard = new Monster
                {
                    Name = "Murder Guard",
                    Level = level,
                    HP = guardHP,
                    MaxHP = guardHP,
                    Strength = 18 + level * 3,
                    Defence = 15 + level * 2,
                    WeapPow = 15 + level,
                    ArmPow = 10 + level * 2 / 3,
                    WeaponName = "Halberd",
                    ArmorName = "Half-Plate"
                };
                guards[i] = guard;
            }

            var combatEngine = new CombatEngine(terminal);
            var result = await combatEngine.PlayerVsMonsters(player, guards.ToList());

            if (result.Victory)
            {
                AlignmentSystem.Instance.ChangeAlignment(player, 100, isGood: false, reason: "murder");
                terminal.SetColor("red");
                terminal.WriteLine(Loc.Get("street_encounter.guard.darkness_guards"));

                captured = false;
            }
            else if (player.IsAlive)
            {
                // Lost the fight but survived — arrested
                terminal.SetColor("yellow");
                terminal.WriteLine("");
                terminal.WriteLine(Loc.Get("street_encounter.guard.overpowered"));

                captured = true;
            }
            else
            {
                // Lost the fight and died
                captured = false; // Died, so resurrect at temple
            }
        }

        // 50% execution, 50% prison
        bool isExecuted = Random.Shared.Next(100) < 50;

        if (isExecuted && captured)
        {
            terminal.SetColor("bright_red");
            terminal.WriteLine("  ══════════════════════════════════════════");
            terminal.WriteLine($"              {Loc.Get("base.death_sentence")}");
            terminal.WriteLine("  ══════════════════════════════════════════");
            terminal.WriteLine("");
            terminal.SetColor("red");
            terminal.WriteLine($"  {Loc.Get("base.magistrate_verdict")}");
            terminal.WriteLine("");
            terminal.SetColor("bright_red");
            terminal.WriteLine($"  \"{Loc.Get("base.sentenced_to_death_1")}");
            terminal.WriteLine($"   {Loc.Get("base.sentenced_to_death_2")}\"");
            terminal.WriteLine("");
            terminal.SetColor("dark_red");
            terminal.WriteLine($"  {Loc.Get("base.executioner_axe")}");
            terminal.WriteLine("");
            await Task.Delay(3000);



            // Broadcast the execution
            if (DoorMode.IsOnlineMode)
            {
                NewsSystem.Instance?.Newsy(
                    $"⚖ {player.Name2} was executed by the Crown for the murder of {victim.Name2 ?? victim.Name}. Justice is served.");
            }

            // Kill the character
            if (player is Player p)
            {
                if (DoorMode.IsOnlineMode)
                {
                    // Kill Player
                    try
                    {
                        p.Die();
                        DebugLogger.Instance?.Log(DebugLogger.LogLevel.Info, "MURDER", $"Executed player {p.Name2}");
                    }
                    catch (Exception ex)
                    {
                        DebugLogger.Instance?.Log(DebugLogger.LogLevel.Error, "MURDER", $"Failed to Kill executed player: {ex.Message}");
                    }
                }
                else
                {
                    // Kill Player
                    p.Die();
                }

                // v0.57.8: now that PR #82 changed execution from "delete save"
                // to "p.Die()", the character survives and should go through
                // the normal death / resurrection flow. No more force-quit in
                // single-player (kicking the user out of the process is wrong
                // UX when the character isn't actually gone). Online mode
                // still throws to drop the session — the server routes that
                // through the standard death handler.
                terminal.SetColor("gray");
                terminal.WriteLine($"  {Loc.Get("base.press_key_exit")}");
                await terminal.PressAnyKey();

                if (DoorMode.IsOnlineMode)
                    throw new Exception("CHARACTER_EXECUTED");
                // Single-player: fall through — HP=0 from Die() will trigger
                // the normal resurrection flow at the Temple / death screen.
            }
        }
        else if (captured)
        {
            // Prison
            terminal.SetColor("bright_yellow");
            terminal.WriteLine("  ══════════════════════════════════════════");
            terminal.WriteLine($"              {Loc.Get("base.life_spared")}");
            terminal.WriteLine("  ══════════════════════════════════════════");
            terminal.WriteLine("");
            terminal.SetColor("white");
            terminal.WriteLine($"  {Loc.Get("base.magistrate_mercy")}");
            terminal.SetColor("yellow");
            terminal.WriteLine("");
            terminal.WriteLine($"  \"{Loc.Get("base.sentenced_prison_1")}");
            terminal.WriteLine($"   {Loc.Get("base.sentenced_prison_2")}\"");
            terminal.WriteLine("");
            terminal.SetColor("red");
            terminal.WriteLine($"  \"{Loc.Get("base.possessions_confiscated")}\"");
            terminal.WriteLine("");
            await Task.Delay(2000);

            if (player is Player p)
            {
                // Take all gold
                long goldTaken = p.Gold / 2;
                p.Gold = p.Gold / 2;

                // Prison for 2 real days — maximum security, no escape
                p.DaysInPrison = 2;
                p.IsMurderConvict = true;
                p.PrisonEscapes = 0;
                p.CellDoorOpen = false;

                p.RecalculateStats();

                terminal.SetColor("gray");
                terminal.WriteLine(Loc.Get("base.gold_confiscated", goldTaken));
                terminal.WriteLine($"  {Loc.Get("base.equipment_confiscated")}");
                terminal.WriteLine($"  {Loc.Get("base.bank_seized")}");
                terminal.WriteLine("");
                terminal.SetColor("bright_red");
                terminal.WriteLine($"  {Loc.Get("base.dragged_to_prison")}");
                terminal.WriteLine($"  {Loc.Get("base.no_escape")}");
                terminal.WriteLine("");
            }

            // Broadcast
            if (DoorMode.IsOnlineMode)
            {
                NewsSystem.Instance?.Newsy(
                    $"⚖ {player.Name2} was imprisoned for 2 days for the murder of {victim.Name2 ?? victim.Name}.");
            }

            terminal.SetColor("gray");
            terminal.WriteLine($"  {Loc.Get("base.press_key_continue")}");
            await terminal.PressAnyKey();
        }
    }

    /// <summary>
    /// Determine if NPC should accept a duel challenge
    /// </summary>
    private bool ShouldNPCAcceptDuel(NPC npc)
    {
        var random = Random.Shared;

        // Level difference affects acceptance
        int levelDiff = npc.Level - currentPlayer.Level;

        // Very high level NPCs don't bother with weak players
        if (levelDiff > 10) return random.Next(100) < 10;  // 10% chance

        // Very low level NPCs are scared
        if (levelDiff < -10) return random.Next(100) < 20; // 20% chance

        // Similar level - personality matters
        if (npc.Darkness > npc.Chivalry)
        {
            return random.Next(100) < 70; // Evil NPCs like fights
        }
        else if (npc.Chivalry > npc.Darkness + 500)
        {
            return random.Next(100) < 40; // Honorable NPCs prefer peace
        }

        return random.Next(100) < 50; // 50-50 otherwise
    }

    /// <summary>
    /// Show player status - Comprehensive character information display
    /// </summary>
    protected virtual async Task ShowStatus()
    {
        // Electron graphical client — emit full character sheet
        if (GameConfig.ElectronMode)
        {
            var p = currentPlayer;
            bool isMana = p is Player pp && pp.IsManaClass;
            ElectronBridge.Emit("character_status", new
            {
                name = p.DisplayName,
                className = p.ClassName,
                race = p.Race.ToString(),
                sex = p.Sex,
                level = p.Level,
                experience = p.Experience,
                hp = p.HP,
                maxHp = p.MaxHP,
                mana = isMana ? p.Mana : 0,
                maxMana = isMana ? p.MaxMana : 0,
                stamina = isMana ? 0 : p.Stamina,
                maxStamina = isMana ? 0 : p.BaseStamina,
                str = p.Strength,
                dex = p.Dexterity,
                agi = p.Agility,
                con = p.Constitution,
                intel = p.Intelligence,
                wis = p.Wisdom,
                cha = p.Charisma,
                def = p.Defence,
                gold = p.Gold,
                potions = p is Player pl ? pl.Healing : 0,
                maxPotions = p is Player pl2 ? pl2.MaxPotions : 0,
                isManaClass = isMana,
                isKnighted = p.IsKnighted,
                alignment = "Neutral",
            });

            // Skip text rendering in Electron mode
            ElectronBridge.EmitPressAnyKey();
            await terminal.PressAnyKey();
            return;
        }

        terminal.ClearScreen();

        // Header
        WriteBoxHeader(Loc.Get("base.character_status"), "bright_cyan");
        terminal.WriteLine("");

        // Basic Info
        WriteSectionHeader(Loc.Get("base.basic_information"), "yellow");
        terminal.SetColor("white");
        terminal.Write(Loc.Get("base.stat_name") + " ");
        terminal.SetColor("bright_white");
        terminal.WriteLine(currentPlayer.DisplayName);

        terminal.SetColor("white");
        terminal.Write(Loc.Get("base.stat_class") + " ");
        terminal.SetColor("bright_green");
        terminal.Write($"{currentPlayer.ClassName}");
        terminal.SetColor("white");
        terminal.Write("  |  " + Loc.Get("base.stat_race") + " ");
        terminal.SetColor("bright_green");
        terminal.Write($"{currentPlayer.Race}");
        terminal.SetColor("white");
        terminal.Write("  |  " + Loc.Get("base.stat_sex") + " ");
        terminal.SetColor("bright_green");
        terminal.WriteLine($"{(currentPlayer.Sex == CharacterSex.Male ? Loc.Get("base.male") : Loc.Get("base.female"))}");

        terminal.SetColor("white");
        terminal.Write(Loc.Get("base.stat_age") + " ");
        terminal.SetColor("cyan");
        terminal.Write($"{currentPlayer.Age}");
        terminal.SetColor("white");
        terminal.Write("  |  " + Loc.Get("base.stat_height") + " ");
        terminal.SetColor("cyan");
        terminal.Write($"{currentPlayer.Height}cm");
        terminal.SetColor("white");
        terminal.Write("  |  " + Loc.Get("base.stat_weight") + " ");
        terminal.SetColor("cyan");
        terminal.WriteLine($"{currentPlayer.Weight}kg");

        // Royal Authority buff display
        if (currentPlayer.King)
        {
            terminal.SetColor("bright_yellow");
            terminal.WriteLine(Loc.Get("base.stat_royal_authority"));
        }
        terminal.WriteLine("");

        // Level & Experience
        WriteSectionHeader(Loc.Get("base.level_experience"), "yellow");
        terminal.SetColor("white");
        terminal.Write(Loc.Get("base.stat_current_level") + " ");
        terminal.SetColor("bright_yellow");
        terminal.WriteLine($"{currentPlayer.Level}");

        terminal.SetColor("white");
        terminal.Write($"{Loc.Get("ui.experience")}: ");
        terminal.SetColor("bright_cyan");
        terminal.WriteLine($"{currentPlayer.Experience:N0}");

        // Calculate XP needed for next level
        long nextLevelXP = GetExperienceForLevel(currentPlayer.Level + 1);
        long xpNeeded = nextLevelXP - currentPlayer.Experience;

        terminal.SetColor("white");
        terminal.Write(Loc.Get("base.stat_xp_next") + " ");
        terminal.SetColor("bright_magenta");
        terminal.Write($"{xpNeeded:N0}");
        terminal.SetColor("gray");
        terminal.WriteLine(Loc.Get("base.stat_xp_need_total", nextLevelXP));
        terminal.WriteLine("");

        // Combat Stats
        WriteSectionHeader(Loc.Get("base.combat_statistics"), "yellow");
        terminal.SetColor("white");
        terminal.Write($"{Loc.Get("combat.bar_hp")}: ");
        terminal.SetColor("bright_red");
        terminal.Write($"{currentPlayer.HP}");
        terminal.SetColor("white");
        terminal.Write("/");
        terminal.SetColor("red");
        terminal.WriteLine($"{currentPlayer.MaxHP}");

        if (currentPlayer.MaxMana > 0)
        {
            terminal.SetColor("white");
            terminal.Write($"{Loc.Get("ui.mana_label")}: ");
            terminal.SetColor("bright_blue");
            terminal.Write($"{currentPlayer.Mana}");
            terminal.SetColor("white");
            terminal.Write("/");
            terminal.SetColor("blue");
            terminal.WriteLine($"{currentPlayer.MaxMana}");
        }

        terminal.SetColor("white");
        terminal.Write($"{Loc.Get("ui.stat_strength")}: ");
        terminal.SetColor("bright_green");
        terminal.Write($"{currentPlayer.Strength}");
        terminal.SetColor("white");
        terminal.Write($"  |  {Loc.Get("ui.stat_defense")}: ");
        terminal.SetColor("bright_green");
        terminal.Write($"{currentPlayer.Defence}");
        terminal.SetColor("white");
        terminal.Write($"  |  {Loc.Get("ui.stat_agility")}: ");
        terminal.SetColor("bright_green");
        terminal.WriteLine($"{currentPlayer.Agility}");

        terminal.SetColor("white");
        terminal.Write($"{Loc.Get("ui.stat_dexterity")}: ");
        terminal.SetColor("cyan");
        terminal.Write($"{currentPlayer.Dexterity}");
        terminal.SetColor("white");
        terminal.Write($"  |  {Loc.Get("ui.stat_stamina")}: ");
        terminal.SetColor("cyan");
        terminal.Write($"{currentPlayer.Stamina}");
        terminal.SetColor("white");
        terminal.Write($"  |  {Loc.Get("ui.stat_wisdom")}: ");
        terminal.SetColor("cyan");
        terminal.WriteLine($"{currentPlayer.Wisdom}");

        terminal.SetColor("white");
        terminal.Write($"{Loc.Get("ui.stat_intelligence")}: ");
        terminal.SetColor("cyan");
        terminal.Write($"{currentPlayer.Intelligence}");
        terminal.SetColor("white");
        terminal.Write($"  |  {Loc.Get("ui.stat_charisma")}: ");
        terminal.SetColor("cyan");
        terminal.Write($"{currentPlayer.Charisma}");
        terminal.SetColor("white");
        terminal.Write($"  |  {Loc.Get("ui.stat_constitution")}: ");
        terminal.SetColor("cyan");
        terminal.WriteLine($"{currentPlayer.Constitution}");
        terminal.WriteLine("");

        // Pagination - Page 1 break
        terminal.SetColor("gray");
        terminal.Write(Loc.Get("ui.press_enter"));
        await terminal.GetInput("");
        terminal.WriteLine("");

        // Equipment - Full Slot Display
        WriteSectionHeader(Loc.Get("base.equipment"), "yellow");

        // Combat style indicator
        terminal.SetColor("white");
        terminal.Write(Loc.Get("base.stat_combat_style") + " ");
        if (currentPlayer.IsTwoHanding)
        {
            terminal.SetColor("bright_red");
            terminal.WriteLine(Loc.Get("base.style_two_handed"));
        }
        else if (currentPlayer.IsDualWielding)
        {
            terminal.SetColor("bright_yellow");
            terminal.WriteLine(Loc.Get("base.style_dual_wield"));
        }
        else if (currentPlayer.HasShieldEquipped)
        {
            terminal.SetColor("bright_cyan");
            terminal.WriteLine(Loc.Get("base.style_sword_board"));
        }
        else
        {
            terminal.SetColor("gray");
            terminal.WriteLine(Loc.Get("base.style_one_handed"));
        }
        terminal.WriteLine("");

        // Weapons
        terminal.SetColor("bright_red");
        terminal.Write(Loc.Get("base.slot_main_hand") + " ");
        DisplayEquipmentSlot(EquipmentSlot.MainHand);
        terminal.SetColor("bright_red");
        terminal.Write(Loc.Get("base.slot_off_hand") + " ");
        DisplayEquipmentSlot(EquipmentSlot.OffHand);
        terminal.WriteLine("");

        // Armor slots (in two columns)
        terminal.SetColor("bright_cyan");
        terminal.Write(Loc.Get("base.slot_head") + " ");
        DisplayEquipmentSlot(EquipmentSlot.Head);
        terminal.SetColor("bright_cyan");
        terminal.Write(Loc.Get("base.slot_body") + " ");
        DisplayEquipmentSlot(EquipmentSlot.Body);
        terminal.SetColor("bright_cyan");
        terminal.Write(Loc.Get("base.slot_arms") + " ");
        DisplayEquipmentSlot(EquipmentSlot.Arms);
        terminal.SetColor("bright_cyan");
        terminal.Write(Loc.Get("base.slot_hands") + " ");
        DisplayEquipmentSlot(EquipmentSlot.Hands);
        terminal.SetColor("bright_cyan");
        terminal.Write(Loc.Get("base.slot_legs") + " ");
        DisplayEquipmentSlot(EquipmentSlot.Legs);
        terminal.SetColor("bright_cyan");
        terminal.Write(Loc.Get("base.slot_feet") + " ");
        DisplayEquipmentSlot(EquipmentSlot.Feet);
        terminal.SetColor("bright_cyan");
        terminal.Write(Loc.Get("base.slot_waist") + " ");
        DisplayEquipmentSlot(EquipmentSlot.Waist);
        terminal.SetColor("bright_cyan");
        terminal.Write(Loc.Get("base.slot_face") + " ");
        DisplayEquipmentSlot(EquipmentSlot.Face);
        terminal.SetColor("bright_cyan");
        terminal.Write(Loc.Get("base.slot_cloak") + " ");
        DisplayEquipmentSlot(EquipmentSlot.Cloak);
        terminal.WriteLine("");

        // Accessories
        terminal.SetColor("bright_magenta");
        terminal.Write(Loc.Get("base.slot_neck") + " ");
        DisplayEquipmentSlot(EquipmentSlot.Neck);
        terminal.SetColor("bright_magenta");
        terminal.Write(Loc.Get("base.slot_left_ring") + " ");
        DisplayEquipmentSlot(EquipmentSlot.LFinger);
        terminal.SetColor("bright_magenta");
        terminal.Write(Loc.Get("base.slot_right_ring") + " ");
        DisplayEquipmentSlot(EquipmentSlot.RFinger);
        terminal.WriteLine("");

        // Equipment totals
        DisplayEquipmentTotals();
        terminal.WriteLine("");

        // Show active buffs if any
        if (currentPlayer.MagicACBonus > 0 || currentPlayer.DamageAbsorptionPool > 0 ||
            currentPlayer.IsRaging || currentPlayer.SmiteChargesRemaining > 0)
        {
            terminal.SetColor("bright_magenta");
            terminal.WriteLine(Loc.Get("base.stat_active_effects"));

            if (currentPlayer.MagicACBonus > 0)
            {
                terminal.SetColor("magenta");
                terminal.WriteLine(Loc.Get("base.effect_magic_ac", currentPlayer.MagicACBonus));
            }
            if (currentPlayer.DamageAbsorptionPool > 0)
            {
                terminal.SetColor("magenta");
                terminal.WriteLine(Loc.Get("base.effect_stoneskin", currentPlayer.DamageAbsorptionPool));
            }
            if (currentPlayer.IsRaging)
            {
                terminal.SetColor("bright_red");
                terminal.WriteLine(Loc.Get("base.effect_raging"));
            }
            if (currentPlayer.SmiteChargesRemaining > 0)
            {
                terminal.SetColor("yellow");
                terminal.WriteLine(Loc.Get("base.effect_smite", currentPlayer.SmiteChargesRemaining));
            }
            terminal.WriteLine("");
        }

        // Show temporary combat buffs (well-rested, god slayer, song, herbs)
        bool hasAnyBuff = currentPlayer.IsKnighted || currentPlayer.WellRestedCombats > 0 || currentPlayer.HasGodSlayerBuff
            || currentPlayer.HasDarkPactBuff || currentPlayer.HasSettlementBuff
            || currentPlayer.HasActiveSongBuff || currentPlayer.HasActiveHerbBuff
            || currentPlayer.LoversBlissCombats > 0 || currentPlayer.DivineBlessingCombats > 0
            || currentPlayer.Class == CharacterClass.Alchemist
            || currentPlayer.Class == CharacterClass.Paladin
            || currentPlayer.Class == CharacterClass.Magician
            || currentPlayer.Class == CharacterClass.Jester
            || currentPlayer.Class == CharacterClass.Cleric
            || currentPlayer.Class == CharacterClass.Tidesworn
            || currentPlayer.Class == CharacterClass.Wavecaller
            || currentPlayer.Class == CharacterClass.Cyclebreaker
            || currentPlayer.Class == CharacterClass.Abysswarden
            || currentPlayer.Class == CharacterClass.Voidreaver;
        if (hasAnyBuff)
        {
            terminal.SetColor("bright_cyan");
            terminal.WriteLine(Loc.Get("base.stat_active_buffs"));

            // Blood Price debuffs (murder weight consequences)
            if (currentPlayer.MurderWeight >= GameConfig.MurderWeightTier3Threshold)
            {
                terminal.SetColor("dark_red");
                terminal.WriteLine($"  - Blood Price (Mass Murderer): -{(int)(GameConfig.MurderWeightTier3CombatPenalty * 100)}% damage, +{(int)(GameConfig.MurderWeightTier3ShopMarkup * 100)}% shop prices, +{(int)(GameConfig.MurderWeightTier3HealPenalty * 100)}% healer costs");
                terminal.WriteLine($"    Murder Weight: {currentPlayer.MurderWeight:F1} — Confess at the Church to reduce.");
            }
            else if (currentPlayer.MurderWeight >= GameConfig.MurderWeightTier2Threshold)
            {
                terminal.SetColor("red");
                terminal.WriteLine($"  - Blood Price (Notorious Killer): -{(int)(GameConfig.MurderWeightTier2CombatPenalty * 100)}% damage, +{(int)(GameConfig.MurderWeightTier2ShopMarkup * 100)}% shop prices");
                terminal.WriteLine($"    Murder Weight: {currentPlayer.MurderWeight:F1} — Confess at the Church to reduce.");
            }
            else if (currentPlayer.MurderWeight >= GameConfig.MurderWeightShopMarkupThreshold)
            {
                terminal.SetColor("yellow");
                terminal.WriteLine($"  - Blood Price (Known Killer): +{(int)(GameConfig.MurderWeightShopMarkupPercent * 100)}% shop prices");
                terminal.WriteLine($"    Murder Weight: {currentPlayer.MurderWeight:F1} — Confess at the Church to reduce.");
            }

            if (currentPlayer.IsKnighted)
            {
                terminal.SetColor("bright_yellow");
                terminal.WriteLine($"  - {currentPlayer.NobleTitle}'s Honor: +{(int)(GameConfig.KnightDamageBonus * 100)}% damage, +{(int)(GameConfig.KnightDefenseBonus * 100)}% defense (permanent)");
            }
            if (currentPlayer.Class == CharacterClass.Alchemist)
            {
                terminal.SetColor("bright_cyan");
                terminal.WriteLine(Loc.Get("base.buff_potion_mastery", (int)(GameConfig.AlchemistPotionMasteryBonus * 100)));
            }
            if (currentPlayer.Class == CharacterClass.Magician)
            {
                terminal.SetColor("bright_cyan");
                terminal.WriteLine(Loc.Get("base.buff_arcane_mastery", (int)((GameConfig.MagicianArcaneSpellBonus - 1.0f) * 100)));
            }
            if (currentPlayer.Class == CharacterClass.Bard)
            {
                terminal.SetColor("bright_yellow");
                terminal.WriteLine($"  - Bardic Inspiration: {GameConfig.BardInspirationChance}% chance per ability to inspire a teammate (+{GameConfig.BardInspirationAttackBonus} ATK)");
            }
            if (currentPlayer.Class == CharacterClass.Jester)
            {
                terminal.SetColor("bright_magenta");
                terminal.WriteLine(Loc.Get("base.buff_tricksters_luck", GameConfig.JesterTrickstersLuckChance));
            }
            if (currentPlayer.Class == CharacterClass.Assassin)
            {
                terminal.SetColor("bright_red");
                terminal.WriteLine($"  - Lethal Precision: +{(int)(GameConfig.AssassinLethalPrecisionCritBonus * 100)}% crit damage with dagger, +{(int)(GameConfig.AssassinLethalPrecisionPoisonBonus * 100)}% damage vs poisoned targets");
            }
            if (currentPlayer.Class == CharacterClass.MysticShaman)
            {
                terminal.SetColor("bright_yellow");
                terminal.WriteLine($"  - Elemental Mastery: +{(int)(GameConfig.ShamanElementalMastery * 100)}% elemental damage per INT point");
                terminal.WriteLine($"  - Totem Duration: {GameConfig.ShamanTotemBaseDuration} rounds | Enchant Duration: {GameConfig.ShamanEnchantDuration} rounds");
                if (currentPlayer.ShamanEnchantRounds > 0)
                {
                    string enchantName = currentPlayer.ShamanEnchantType switch { 1 => "Flametongue", 2 => "Frostbrand", 3 => "Rockbiter", 4 => "Stormstrike", _ => "Unknown" };
                    terminal.WriteLine($"  - Active Enchant: {enchantName} ({currentPlayer.ShamanEnchantRounds} rounds remaining)");
                }
            }
            if (currentPlayer.Class == CharacterClass.Paladin)
            {
                terminal.SetColor("bright_white");
                terminal.WriteLine($"  - Divine Resolve: +{(int)(GameConfig.PaladinDivineResolveDamageBonus * 100)}% damage vs undead/demons, {(int)(GameConfig.PaladinDivineResolveStatusResist * 100)}% status resist");
            }
            if (currentPlayer.Class == CharacterClass.Cleric)
            {
                terminal.SetColor("bright_cyan");
                terminal.WriteLine($"  - Divine Grace: +{(int)(GameConfig.ClericDivineGraceBonus * 100)}% healing from abilities and spells");
            }
            if (currentPlayer.Class == CharacterClass.Tidesworn)
            {
                terminal.SetColor("bright_cyan");
                terminal.WriteLine($"  - Ocean's Blessing: +{(int)(GameConfig.TideswornOceansBlessingBonus * 100)}% healing from abilities and spells");
                terminal.WriteLine($"  - Ocean's Resilience: Regen {(int)(GameConfig.TideswornOceansResiliencePercent * 100)}% max HP/round (+{(int)(GameConfig.TideswornOceansResilienceBelowHalfBonus * 100)}% below 50% HP)");
            }
            if (currentPlayer.Class == CharacterClass.Wavecaller)
            {
                terminal.SetColor("bright_magenta");
                terminal.WriteLine($"  - Harmonic Resonance: +{(int)(GameConfig.WavecallerHarmonicResonanceBonus * 100)}% healing from abilities and spells");
                terminal.WriteLine($"  - Damage Reflection: {(int)(GameConfig.WavecallerReflectionPercent * 100)}% damage reflected when Harmonic Shield or Empathic Link active");
            }
            if (currentPlayer.Class == CharacterClass.Cyclebreaker)
            {
                terminal.SetColor("bright_magenta");
                terminal.WriteLine($"  - Probability Manipulation: {(int)(GameConfig.CyclebreakerDebuffResistChance * 100)}% chance to resist incoming debuffs");
                int cycle = StoryProgressionSystem.Instance?.CurrentCycle ?? 1;
                float xpBonus = Math.Min(GameConfig.CyclebreakerCycleXPBonusCap, (cycle - 1) * GameConfig.CyclebreakerCycleXPBonus);
                if (xpBonus > 0)
                    terminal.WriteLine($"  - Cycle Memory: +{(int)(xpBonus * 100)}% XP from combat (Cycle {cycle})");
                else
                    terminal.WriteLine($"  - Cycle Memory: +5% XP per NG+ cycle (inactive in Cycle 1)");
            }
            if (currentPlayer.Class == CharacterClass.Abysswarden)
            {
                terminal.SetColor("dark_red");
                terminal.WriteLine($"  - Abyssal Siphon: {(int)(GameConfig.AbysswardenAbyssalSiphonPercent * 100)}% lifesteal on all attacks");
                terminal.WriteLine($"  - Prison Warden's Resilience: Enemies deal {(int)(GameConfig.AbysswardenPrisonWardResist * 100)}% less damage");
                terminal.WriteLine($"  - Corruption Harvest: Heal {(int)(GameConfig.AbysswardenCorruptionHealPercent * 100)}% max HP on killing a poisoned enemy");
            }
            if (currentPlayer.Class == CharacterClass.Voidreaver)
            {
                terminal.SetColor("dark_red");
                terminal.WriteLine($"  - Void Hunger: Heal {(int)(GameConfig.VoidreaverVoidHungerPercent * 100)}% max HP on every kill");
                terminal.WriteLine($"  - Pain Threshold: +{(int)(GameConfig.VoidreaverPainThresholdBonus * 100)}% ability damage when below 50% HP");
                terminal.WriteLine($"  - Soul Eater: Restore {(int)(GameConfig.VoidreaverSoulEaterManaPercent * 100)}% max mana on killing blow");
            }
            if (currentPlayer.HasGodSlayerBuff)
            {
                terminal.SetColor("bright_yellow");
                terminal.WriteLine($"  - God Slayer: +{(int)(currentPlayer.GodSlayerDamageBonus * 100)}% dmg, +{(int)(currentPlayer.GodSlayerDefenseBonus * 100)}% def ({currentPlayer.GodSlayerCombats} combats)");
            }
            if (currentPlayer.HasDarkPactBuff)
            {
                terminal.SetColor("dark_red");
                terminal.WriteLine($"  - Dark Pact: +{(int)(currentPlayer.DarkPactDamageBonus * 100)}% dmg ({currentPlayer.DarkPactCombats} combats)");
            }
            if (currentPlayer.HasSettlementBuff)
            {
                string buffName = ((UsurperRemake.Systems.SettlementBuffType)currentPlayer.SettlementBuffType) switch
                {
                    UsurperRemake.Systems.SettlementBuffType.XPBonus => "Settlement (XP)",
                    UsurperRemake.Systems.SettlementBuffType.DefenseBonus => "Settlement (Def)",
                    UsurperRemake.Systems.SettlementBuffType.DamageBonus => "Arena (Dmg)",
                    UsurperRemake.Systems.SettlementBuffType.GoldBonus => "Thieves' Den (Gold)",
                    UsurperRemake.Systems.SettlementBuffType.TrapResist => "Prison (Trap Resist)",
                    UsurperRemake.Systems.SettlementBuffType.LibraryXP => "Library (XP)",
                    _ => "Settlement"
                };
                terminal.SetColor("bright_green");
                terminal.WriteLine($"  - {buffName}: +{(int)(currentPlayer.SettlementBuffValue * 100)}% ({currentPlayer.SettlementBuffCombats} combats)");
            }
            if (currentPlayer.WellRestedCombats > 0)
            {
                terminal.SetColor("green");
                terminal.WriteLine($"  - Well-Rested: +{(int)(currentPlayer.WellRestedBonus * 100)}% dmg/def ({currentPlayer.WellRestedCombats} combats)");
            }
            if (currentPlayer.HasActiveSongBuff)
            {
                string songName = currentPlayer.SongBuffType switch { 1 => "War March", 2 => "Lullaby of Iron", 3 => "Fortune's Tune", 4 => "Battle Hymn", _ => "Song" };
                terminal.SetColor("magenta");
                terminal.WriteLine($"  - {songName} ({currentPlayer.SongBuffCombats} combats)");
            }
            if (currentPlayer.HasActiveHerbBuff)
            {
                string herbName = ((HerbType)currentPlayer.HerbBuffType).ToString();
                terminal.SetColor("green");
                terminal.WriteLine($"  - {herbName} ({currentPlayer.HerbBuffCombats} combats)");
            }
            if (currentPlayer.HasActiveFoodBuff)
            {
                string foodName = currentPlayer.FoodBuffType switch
                {
                    1 => "Dragon Steak (+10% dmg)",
                    2 => "Honey Bread (+10% def)",
                    3 => "Iron Rations (+15% max HP)",
                    4 => "Mushroom Soup (+15% spell dmg)",
                    5 => "Food Poisoning (-5% stats)",
                    _ => "Food"
                };
                string foodColor = currentPlayer.FoodBuffType == 5 ? "dark_red" : "bright_yellow";
                terminal.SetColor(foodColor);
                terminal.WriteLine($"  - {foodName} ({currentPlayer.FoodBuffCombats} combats)");
            }
            if (currentPlayer.LoversBlissCombats > 0)
            {
                terminal.SetColor("bright_magenta");
                terminal.WriteLine($"  - Lover's Bliss ({currentPlayer.LoversBlissCombats} combats)");
            }
            if (currentPlayer.DivineBlessingCombats > 0)
            {
                terminal.SetColor("bright_cyan");
                terminal.WriteLine($"  - Divine Blessing ({currentPlayer.DivineBlessingCombats} combats)");
            }
            // Team HQ upgrade bonuses
            if (currentPlayer.HQArmoryLevel > 0 || currentPlayer.HQBarracksLevel > 0 ||
                currentPlayer.HQTrainingLevel > 0 || currentPlayer.HQInfirmaryLevel > 0)
            {
                terminal.SetColor("bright_yellow");
                if (currentPlayer.HQArmoryLevel > 0)
                    terminal.WriteLine($"  - Team Armory Lv{currentPlayer.HQArmoryLevel}: +{currentPlayer.HQArmoryLevel * 5}% attack");
                if (currentPlayer.HQBarracksLevel > 0)
                    terminal.WriteLine($"  - Team Barracks Lv{currentPlayer.HQBarracksLevel}: +{currentPlayer.HQBarracksLevel * 5}% defense");
                if (currentPlayer.HQTrainingLevel > 0)
                    terminal.WriteLine($"  - Team Training Lv{currentPlayer.HQTrainingLevel}: +{currentPlayer.HQTrainingLevel * 5}% XP");
                if (currentPlayer.HQInfirmaryLevel > 0)
                    terminal.WriteLine($"  - Team Infirmary Lv{currentPlayer.HQInfirmaryLevel}: +{currentPlayer.HQInfirmaryLevel * 10}% potion healing");
            }
            // Session XP diminishing returns indicator (online mode only)
            long sessionThreshold = GameConfig.GetSessionXPThreshold(currentPlayer.Level);
            if (UsurperRemake.BBS.DoorMode.IsOnlineMode && currentPlayer.SessionXPEarned > sessionThreshold)
            {
                long overThreshold = currentPlayer.SessionXPEarned - sessionThreshold;
                double diminishFactor = Math.Max(GameConfig.SessionXPDiminishFloor, 1.0 - (overThreshold / 1000.0) * GameConfig.SessionXPDiminishRate);
                int pct = (int)(diminishFactor * 100);
                terminal.SetColor("dark_yellow");
                terminal.WriteLine($"  - Session Fatigue: XP at {pct}% (earned {currentPlayer.SessionXPEarned:N0} this session)");
            }
            terminal.WriteLine("");
        }

        // Awakening status (v0.49.6)
        var ocean = OceanPhilosophySystem.Instance;
        if (ocean != null)
        {
            var awakeningLevel = ocean.AwakeningLevel;
            var awakeningLabel = awakeningLevel switch
            {
                0 => Loc.Get("base.awakening_dormant"),
                1 => Loc.Get("base.awakening_stirring"),
                2 => Loc.Get("base.awakening_aware"),
                3 => Loc.Get("base.awakening_seeking"),
                4 => Loc.Get("base.awakening_illuminated"),
                5 => Loc.Get("base.awakening_transcendent"),
                6 => Loc.Get("base.awakening_enlightened"),
                7 => Loc.Get("base.awakening_awakened"),
                _ => Loc.Get("base.awakening_dormant")
            };
            terminal.SetColor("dark_magenta");
            terminal.WriteLine(Loc.Get("base.stat_awakening", awakeningLabel, awakeningLevel));
            terminal.SetColor("white");
            terminal.WriteLine("");
        }

        // NG+ World Modifiers (v0.52.0)
        int ngCycle = StoryProgressionSystem.Instance?.CurrentCycle ?? 1;
        if (ngCycle >= 2)
        {
            terminal.SetColor("bright_magenta");
            terminal.WriteLine($"  {Loc.Get("base.ngplus_cycle")}: {ngCycle}");
            if (ngCycle >= 2) terminal.WriteLine($"    - {Loc.Get("base.ngplus_empowered")}");
            if (ngCycle >= 3) terminal.WriteLine($"    - {Loc.Get("base.ngplus_ancient")}");
            if (ngCycle >= 4) terminal.WriteLine($"    - {Loc.Get("base.ngplus_convergence")}");
            terminal.SetColor("white");
            terminal.WriteLine("");
        }

        // Wealth
        WriteSectionHeader(Loc.Get("base.wealth"), "yellow");
        terminal.SetColor("white");
        terminal.Write(Loc.Get("base.stat_gold_hand") + " ");
        terminal.SetColor("bright_yellow");
        terminal.WriteLine($"{currentPlayer.Gold:N0}");

        terminal.SetColor("white");
        terminal.Write(Loc.Get("base.stat_gold_bank") + " ");
        terminal.SetColor("yellow");
        terminal.WriteLine($"{currentPlayer.BankGold:N0}");

        terminal.SetColor("white");
        terminal.Write(Loc.Get("base.stat_total_wealth") + " ");
        terminal.SetColor("bright_yellow");
        terminal.WriteLine($"{(currentPlayer.Gold + currentPlayer.BankGold):N0}");
        terminal.WriteLine("");

        // Pagination - Page 2 break
        terminal.SetColor("gray");
        terminal.Write(Loc.Get("ui.press_enter"));
        await terminal.GetInput("");
        terminal.WriteLine("");

        // Relationships
        WriteSectionHeader(Loc.Get("base.relationships"), "yellow");
        terminal.SetColor("white");
        terminal.Write(Loc.Get("base.stat_marital") + " ");

        // Check both Character properties AND RomanceTracker for marriage status
        var romanceTracker = UsurperRemake.Systems.RomanceTracker.Instance;
        bool isMarried = currentPlayer.Married || currentPlayer.IsMarried || (romanceTracker?.IsMarried == true);

        if (isMarried)
        {
            terminal.SetColor("bright_magenta");
            terminal.Write(Loc.Get("base.stat_married"));

            // Get spouse name from RomanceTracker first, fall back to Character property
            string spouseName = "";
            if (romanceTracker?.IsMarried == true)
            {
                var spouse = romanceTracker.PrimarySpouse;
                if (spouse != null)
                {
                    var npc = UsurperRemake.Systems.NPCSpawnSystem.Instance?.ActiveNPCs?
                        .FirstOrDefault(n => n.ID == spouse.NPCId);
                    spouseName = npc?.Name ?? spouse.NPCName;
                }
            }
            if (string.IsNullOrEmpty(spouseName))
            {
                spouseName = currentPlayer.SpouseName;
            }

            if (!string.IsNullOrEmpty(spouseName))
            {
                terminal.SetColor("white");
                terminal.Write(" " + Loc.Get("base.stat_married_to") + " ");
                terminal.SetColor("magenta");
                terminal.Write(spouseName);
            }
            terminal.WriteLine("");

            // Show all spouses if polygamous
            if (romanceTracker != null && romanceTracker.Spouses.Count > 1)
            {
                terminal.SetColor("gray");
                terminal.WriteLine(Loc.Get("base.stat_spouses_total", romanceTracker.Spouses.Count));
            }

            // Get children count from both systems
            int childCount = currentPlayer.Kids;
            var familyChildren = UsurperRemake.Systems.FamilySystem.Instance?.GetChildrenOf(currentPlayer);
            if (familyChildren != null && familyChildren.Count > childCount)
            {
                childCount = familyChildren.Count;
            }

            terminal.SetColor("white");
            terminal.Write(Loc.Get("base.stat_children") + " ");
            terminal.SetColor("cyan");
            terminal.WriteLine($"{childCount}");

            if (currentPlayer.Pregnancy > 0)
            {
                terminal.SetColor("white");
                terminal.Write(Loc.Get("base.stat_pregnancy") + " ");
                terminal.SetColor("bright_cyan");
                terminal.WriteLine(Loc.Get("base.stat_days", currentPlayer.Pregnancy));
            }
        }
        else if (romanceTracker?.CurrentLovers?.Count > 0)
        {
            terminal.SetColor("magenta");
            terminal.WriteLine(Loc.Get("base.stat_in_relationship", romanceTracker.CurrentLovers.Count));
        }
        else
        {
            terminal.SetColor("gray");
            terminal.WriteLine(Loc.Get("base.stat_single"));
        }

        terminal.SetColor("white");
        terminal.Write(Loc.Get("base.stat_team") + " ");
        if (!string.IsNullOrEmpty(currentPlayer.Team))
        {
            terminal.SetColor("bright_green");
            terminal.WriteLine(currentPlayer.Team);
        }
        else
        {
            terminal.SetColor("gray");
            terminal.WriteLine(Loc.Get("ui.none"));
        }
        terminal.WriteLine("");

        // Alignment & Reputation
        WriteSectionHeader(Loc.Get("base.alignment_reputation"), "yellow");

        // Get alignment info from AlignmentSystem
        var (alignText, alignColor) = AlignmentSystem.Instance.GetAlignmentDisplay(currentPlayer);

        terminal.SetColor("white");
        terminal.Write($"{Loc.Get("ui.alignment")}: ");
        terminal.SetColor(alignColor);
        terminal.WriteLine(alignText);

        terminal.SetColor("white");
        terminal.Write(Loc.Get("base.stat_chivalry") + " ");
        terminal.SetColor("bright_green");
        terminal.Write($"{currentPlayer.Chivalry}/1000");
        terminal.SetColor("white");
        terminal.Write("  |  " + Loc.Get("base.stat_darkness") + " ");
        terminal.SetColor("red");
        terminal.WriteLine($"{currentPlayer.Darkness}/1000");

        // Show alignment bar
        if (!IsScreenReader)
        {
            terminal.SetColor("gray");
            terminal.Write("  " + Loc.Get("base.stat_holy") + " ");
            terminal.SetColor("bright_green");
            int chivBars = (int)Math.Min(10, currentPlayer.Chivalry / 100);
            int darkBars = (int)Math.Min(10, currentPlayer.Darkness / 100);
            terminal.Write(new string('█', chivBars));
            terminal.SetColor("darkgray");
            terminal.Write(new string('░', 10 - chivBars));
            terminal.Write(" | ");
            terminal.SetColor("red");
            terminal.Write(new string('█', darkBars));
            terminal.SetColor("darkgray");
            terminal.Write(new string('░', 10 - darkBars));
            terminal.WriteLine(" " + Loc.Get("base.stat_evil"));
        }

        // Show alignment abilities
        var abilities = AlignmentSystem.Instance.GetAlignmentAbilities(currentPlayer);
        if (abilities.Count > 0)
        {
            terminal.SetColor("cyan");
            terminal.WriteLine(Loc.Get("base.stat_align_abilities"));
            terminal.SetColor("white");
            foreach (var ability in abilities)
            {
                terminal.WriteLine($"    - {ability}");
            }
        }
        terminal.WriteLine("");

        terminal.SetColor("white");
        terminal.Write(Loc.Get("base.stat_loyalty") + " ");
        terminal.SetColor("cyan");
        terminal.Write($"{currentPlayer.Loyalty}%");
        terminal.SetColor("white");
        terminal.Write("  |  " + Loc.Get("base.stat_mental") + " ");
        terminal.SetColor(currentPlayer.Mental >= 50 ? "green" : "red");
        terminal.WriteLine($"{currentPlayer.Mental}");

        if (currentPlayer.King)
        {
            terminal.SetColor("bright_yellow");
            terminal.WriteLine(Loc.Get("base.stat_monarch"));
        }
        terminal.WriteLine("");

        // Faction
        WriteSectionHeader(Loc.Get("base.faction"), "yellow");
        var factionSystem = UsurperRemake.Systems.FactionSystem.Instance;
        if (factionSystem.PlayerFaction != null)
        {
            var faction = factionSystem.PlayerFaction.Value;
            var factionData = UsurperRemake.Systems.FactionSystem.Factions[faction];

            terminal.SetColor("white");
            terminal.Write(Loc.Get("base.stat_allegiance") + " ");
            terminal.SetColor(GetFactionColor(faction));
            terminal.WriteLine(factionData.Name);

            terminal.SetColor("white");
            terminal.Write(Loc.Get("base.stat_rank") + " ");
            terminal.SetColor("bright_cyan");
            terminal.Write($"{factionSystem.FactionRank}");
            terminal.SetColor("gray");
            terminal.Write(" (");
            terminal.SetColor("cyan");
            terminal.Write(factionSystem.GetCurrentRankTitle());
            terminal.SetColor("gray");
            terminal.WriteLine(")");

            // Show active bonuses
            terminal.SetColor("bright_green");
            terminal.WriteLine(Loc.Get("base.stat_active_bonuses"));
            terminal.SetColor("green");
            switch (faction)
            {
                case UsurperRemake.Systems.Faction.TheCrown:
                    terminal.WriteLine(Loc.Get("base.faction_crown_bonus"));
                    break;
                case UsurperRemake.Systems.Faction.TheFaith:
                    terminal.WriteLine(Loc.Get("base.faction_faith_bonus"));
                    break;
                case UsurperRemake.Systems.Faction.TheShadows:
                    terminal.WriteLine(Loc.Get("base.faction_shadows_bonus"));
                    break;
            }
        }
        else
        {
            terminal.SetColor("gray");
            terminal.WriteLine(Loc.Get("base.stat_no_faction"));
            terminal.SetColor("darkgray");
            terminal.WriteLine(Loc.Get("base.stat_faction_hint"));
        }

        // Show standing with all factions
        terminal.WriteLine("");
        terminal.SetColor("white");
        terminal.WriteLine(Loc.Get("base.stat_faction_standing"));
        foreach (var faction in new[] { UsurperRemake.Systems.Faction.TheCrown,
                                         UsurperRemake.Systems.Faction.TheFaith,
                                         UsurperRemake.Systems.Faction.TheShadows })
        {
            var standing = factionSystem.FactionStanding[faction];
            var factionData = UsurperRemake.Systems.FactionSystem.Factions[faction];

            terminal.SetColor("gray");
            terminal.Write("  ");
            terminal.SetColor(GetFactionColor(faction));
            terminal.Write($"{factionData.Name,-15}");
            terminal.SetColor("white");
            terminal.Write(": ");

            // Color based on standing
            if (standing >= 100)
                terminal.SetColor("bright_green");
            else if (standing >= 50)
                terminal.SetColor("green");
            else if (standing >= 0)
                terminal.SetColor("gray");
            else if (standing >= -50)
                terminal.SetColor("yellow");
            else
                terminal.SetColor("red");

            terminal.Write($"{standing,4}");

            // Standing descriptor
            terminal.SetColor("darkgray");
            string standingDesc = standing switch
            {
                >= 200 => " (" + Loc.Get("base.standing_revered") + ")",
                >= 100 => " (" + Loc.Get("base.standing_honored") + ")",
                >= 50 => " (" + Loc.Get("base.standing_friendly") + ")",
                >= 0 => " (" + Loc.Get("base.standing_neutral") + ")",
                >= -50 => " (" + Loc.Get("base.standing_unfriendly") + ")",
                >= -100 => " (" + Loc.Get("base.standing_hostile") + ")",
                _ => " (" + Loc.Get("base.standing_hated") + ")"
            };
            terminal.WriteLine(standingDesc);
        }
        terminal.WriteLine("");

        // Pagination - Page 3 break
        terminal.SetColor("gray");
        terminal.Write(Loc.Get("ui.press_enter"));
        await terminal.GetInput("");
        terminal.WriteLine("");

        // Battle Record
        WriteSectionHeader(Loc.Get("base.battle_record"), "yellow");
        terminal.SetColor("white");
        terminal.Write(Loc.Get("base.stat_monster_kills") + " ");
        terminal.SetColor("bright_green");
        terminal.Write($"{currentPlayer.MKills}");
        terminal.SetColor("white");
        terminal.Write("  |  " + Loc.Get("base.stat_monster_defeats") + " ");
        terminal.SetColor("red");
        terminal.WriteLine($"{currentPlayer.MDefeats}");

        terminal.SetColor("white");
        terminal.Write(Loc.Get("base.stat_player_kills") + " ");
        terminal.SetColor("bright_yellow");
        terminal.Write($"{currentPlayer.PKills}");
        terminal.SetColor("white");
        terminal.Write("  |  " + Loc.Get("base.stat_player_defeats") + " ");
        terminal.SetColor("red");
        terminal.WriteLine($"{currentPlayer.PDefeats}");

        // Calculate win rate
        long totalMonsterBattles = currentPlayer.MKills + currentPlayer.MDefeats;
        long totalPlayerBattles = currentPlayer.PKills + currentPlayer.PDefeats;

        if (totalMonsterBattles > 0)
        {
            double monsterWinRate = (double)currentPlayer.MKills / totalMonsterBattles * 100;
            terminal.SetColor("white");
            terminal.Write(Loc.Get("base.stat_monster_winrate") + " ");
            terminal.SetColor("cyan");
            terminal.WriteLine($"{monsterWinRate:F1}%");
        }

        if (totalPlayerBattles > 0)
        {
            double playerWinRate = (double)currentPlayer.PKills / totalPlayerBattles * 100;
            terminal.SetColor("white");
            terminal.Write(Loc.Get("base.stat_pvp_winrate") + " ");
            terminal.SetColor("cyan");
            terminal.WriteLine($"{playerWinRate:F1}%");
        }
        terminal.WriteLine("");

        // Dungeon Progress
        WriteSectionHeader(Loc.Get("base.dungeon_progress"), "yellow");
        terminal.SetColor("white");
        terminal.Write(Loc.Get("base.stat_deepest_floor") + " ");
        int deepestFloor = currentPlayer.Statistics?.DeepestDungeonLevel ?? 1;
        if (currentPlayer is Player playerForDungeon && playerForDungeon.DungeonLevel > deepestFloor)
            deepestFloor = playerForDungeon.DungeonLevel;
        terminal.SetColor("bright_magenta");
        terminal.WriteLine($"{deepestFloor} / 100");

        // Show Old Gods defeated
        var storySystem = UsurperRemake.Systems.StoryProgressionSystem.Instance;
        if (storySystem != null)
        {
            int godsDefeated = storySystem.OldGodStates.Count(g => g.Value.Status == UsurperRemake.Systems.GodStatus.Defeated);
            int godsAllied = storySystem.OldGodStates.Count(g => g.Value.Status == UsurperRemake.Systems.GodStatus.Allied);
            int godsSaved = storySystem.OldGodStates.Count(g => g.Value.Status == UsurperRemake.Systems.GodStatus.Saved);
            var godsAwakened = storySystem.OldGodStates
                .Where(g => g.Value.Status == UsurperRemake.Systems.GodStatus.Awakened)
                .ToList();

            terminal.SetColor("white");
            terminal.Write(Loc.Get("base.stat_old_gods") + " ");
            bool hasAny = false;
            if (godsDefeated > 0)
            {
                terminal.SetColor("bright_red");
                terminal.Write(Loc.Get("base.gods_defeated", godsDefeated));
                hasAny = true;
            }
            if (godsAllied > 0)
            {
                if (hasAny) terminal.Write(", ");
                terminal.SetColor("bright_green");
                terminal.Write(Loc.Get("base.gods_allied", godsAllied));
                hasAny = true;
            }
            if (godsSaved > 0)
            {
                if (hasAny) terminal.Write(", ");
                terminal.SetColor("bright_cyan");
                terminal.Write(Loc.Get("base.gods_saved", godsSaved));
                hasAny = true;
            }
            if (godsAwakened.Count > 0)
            {
                if (hasAny) terminal.Write(", ");
                terminal.SetColor("bright_magenta");
                terminal.Write(Loc.Get("base.gods_awaiting", godsAwakened.Count));
                hasAny = true;
            }
            if (!hasAny)
            {
                terminal.SetColor("gray");
                terminal.Write(Loc.Get("base.gods_none"));
            }
            terminal.WriteLine("");

            // Show active save quests with hints
            foreach (var god in godsAwakened)
            {
                string godName = god.Key.ToString();
                bool hasLoom = UsurperRemake.Systems.ArtifactSystem.Instance.HasArtifact(UsurperRemake.Systems.ArtifactType.SoulweaversLoom);
                terminal.SetColor("bright_magenta");
                if (hasLoom)
                    terminal.WriteLine(Loc.Get("base.god_have_artifact", godName));
                else
                    terminal.WriteLine(Loc.Get("base.god_seek_artifact", godName));
            }

            // Show seals collected
            int sealsCollected = storySystem.CollectedSeals.Count;
            terminal.SetColor("white");
            terminal.Write(Loc.Get("base.stat_seals") + " ");
            terminal.SetColor(sealsCollected > 0 ? "bright_yellow" : "gray");
            terminal.WriteLine(Loc.Get("base.seals_collected", sealsCollected));
        }
        terminal.WriteLine("");

        // God Worship & Divine Wrath
        WriteSectionHeader(Loc.Get("base.divine_status"), "yellow");
        terminal.SetColor("white");
        terminal.Write(Loc.Get("base.stat_worshipped_god") + " ");
        string worshippedGod = UsurperRemake.GodSystemSingleton.Instance?.GetPlayerGod(currentPlayer.Name2) ?? "";
        // Also check player-created (immortal) god worship
        if (string.IsNullOrEmpty(worshippedGod) && !string.IsNullOrEmpty(currentPlayer.WorshippedGod))
            worshippedGod = currentPlayer.WorshippedGod;
        if (!string.IsNullOrEmpty(worshippedGod))
        {
            // Get god alignment indicator from the GodSystem (Darkness > Goodness = Evil)
            var godInfo = UsurperRemake.GodSystemSingleton.Instance?.GetGod(worshippedGod);
            bool isEvilGod = godInfo != null && godInfo.Darkness > godInfo.Goodness;
            terminal.SetColor(isEvilGod ? "red" : "bright_cyan");
            terminal.WriteLine(worshippedGod);
        }
        else
        {
            terminal.SetColor("gray");
            terminal.WriteLine(Loc.Get("base.stat_agnostic"));
        }

        // Show Divine Wrath status if active
        if (currentPlayer.DivineWrathPending)
        {
            terminal.SetColor("bright_red");
            terminal.WriteLine("");
            terminal.WriteLine(Loc.Get("base.wrath_active"));
            terminal.SetColor("red");
            terminal.WriteLine(Loc.Get("base.wrath_angered", currentPlayer.AngeredGodName));
            terminal.WriteLine(Loc.Get("base.wrath_by_worshipping", currentPlayer.BetrayedForGodName));
            terminal.SetColor("yellow");
            string severity = currentPlayer.DivineWrathLevel switch
            {
                1 => Loc.Get("base.wrath_minor"),
                2 => Loc.Get("base.wrath_moderate"),
                3 => Loc.Get("base.wrath_severe"),
                _ => Loc.Get("base.wrath_unknown")
            };
            terminal.WriteLine(Loc.Get("base.wrath_severity", severity));
            terminal.SetColor("gray");
            terminal.WriteLine(Loc.Get("base.wrath_punishment_hint"));
        }
        terminal.WriteLine("");

        // Artifacts (if any collected)
        var artifactSystem = UsurperRemake.Systems.ArtifactSystem.Instance;
        if (artifactSystem != null)
        {
            var artifactAbilities = artifactSystem.GetActiveArtifactAbilities();
            if (artifactAbilities.Count > 0)
            {
                WriteSectionHeader(Loc.Get("base.artifacts"), "yellow");
                foreach (var ability in artifactAbilities)
                {
                    terminal.SetColor("bright_yellow");
                    terminal.WriteLine($"  {ability}");
                }
                terminal.WriteLine("");
            }
        }

        // Diseases & Afflictions
        if (currentPlayer.Blind || currentPlayer.Plague || currentPlayer.Smallpox ||
            currentPlayer.Measles || currentPlayer.Leprosy || currentPlayer.Poison > 0 ||
            currentPlayer.Addict > 0 || currentPlayer.Haunt > 0)
        {
            WriteSectionHeader(Loc.Get("base.afflictions"), "bright_red");

            if (currentPlayer.Blind)
            {
                terminal.SetColor("red");
                terminal.WriteLine("  - " + Loc.Get("base.affliction_blind"));
            }
            if (currentPlayer.Plague)
            {
                terminal.SetColor("red");
                terminal.WriteLine("  - " + Loc.Get("base.affliction_plague"));
            }
            if (currentPlayer.Smallpox)
            {
                terminal.SetColor("red");
                terminal.WriteLine("  - " + Loc.Get("base.affliction_smallpox"));
            }
            if (currentPlayer.Measles)
            {
                terminal.SetColor("red");
                terminal.WriteLine("  - " + Loc.Get("base.affliction_measles"));
            }
            if (currentPlayer.Leprosy)
            {
                terminal.SetColor("red");
                terminal.WriteLine("  - " + Loc.Get("base.affliction_leprosy"));
            }
            if (currentPlayer.Poison > 0)
            {
                terminal.SetColor("red");
                terminal.WriteLine(Loc.Get("base.affliction_poisoned", currentPlayer.Poison));
            }
            if (currentPlayer.Addict > 0)
            {
                terminal.SetColor("red");
                terminal.WriteLine(Loc.Get("base.affliction_addicted", currentPlayer.Addict));
            }
            if (currentPlayer.Haunt > 0)
            {
                terminal.SetColor("red");
                terminal.WriteLine(Loc.Get("base.affliction_haunted", currentPlayer.Haunt));
            }
            terminal.WriteLine("");
        }

        // Footer
        if (!IsScreenReader)
        {
            terminal.SetColor("gray");
            terminal.WriteLine("────────────────────────────────────────────────────────────────────────────────");
        }

        terminal.WriteLine("");
        await terminal.PressAnyKey();
    }

    /// <summary>
    /// Get the display color for a faction
    /// </summary>
    private static string GetFactionColor(UsurperRemake.Systems.Faction faction)
    {
        return faction switch
        {
            UsurperRemake.Systems.Faction.TheCrown => "bright_yellow",
            UsurperRemake.Systems.Faction.TheFaith => "bright_cyan",
            UsurperRemake.Systems.Faction.TheShadows => "bright_magenta",
            _ => "white"
        };
    }

    /// <summary>
    /// Show gear with optional team member selection
    /// </summary>
    protected async Task ShowGearWithTeamSelection()
    {
        if (currentPlayer == null) return;

        // Build list of available targets
        var targets = new List<(string label, Character character)>();
        targets.Add(($"{currentPlayer.DisplayName} ({Loc.Get("base.label_you")})", currentPlayer));

        // NPC teammates
        var npcTeammates = NPCSpawnSystem.Instance?.ActiveNPCs?
            .Where(n => !string.IsNullOrEmpty(n.Team) && n.Team == currentPlayer.Team && !n.IsDead && n.IsAlive)
            .ToList() ?? new List<NPC>();
        foreach (var npc in npcTeammates)
            targets.Add(($"{npc.DisplayName} ({Loc.Get("base.label_teammate")})", npc));

        // Companions
        var companions = CompanionSystem.Instance?.GetCompanionsAsCharacters() ?? new List<Character>();
        foreach (var comp in companions)
            targets.Add(($"{comp.DisplayName} ({Loc.Get("base.label_companion")})", comp));

        // Spouse
        if (!string.IsNullOrEmpty(currentPlayer.SpouseName))
        {
            var spouseNpc = NPCSpawnSystem.Instance?.GetNPCByName(currentPlayer.SpouseName);
            if (spouseNpc != null && !spouseNpc.IsDead)
                targets.Add(($"{spouseNpc.DisplayName} ({Loc.Get("base.label_spouse")})", spouseNpc));
        }

        // If only the player, show directly
        if (targets.Count == 1)
        {
            ShowDetailedGear();
            await terminal.PressAnyKey();
            return;
        }

        // Show selection
        terminal.WriteLine("");
        terminal.SetColor("bright_yellow");
        terminal.WriteLine(Loc.Get("base.gear_inspect_who"));
        terminal.WriteLine("");

        for (int i = 0; i < targets.Count; i++)
        {
            terminal.SetColor("white");
            terminal.Write($"  {i + 1}. ");
            terminal.SetColor("cyan");
            terminal.WriteLine(targets[i].label);
        }

        terminal.WriteLine("");
        string input = await terminal.GetInput(Loc.Get("base.gear_selection_prompt"));

        Character selected;
        if (string.IsNullOrWhiteSpace(input))
        {
            selected = currentPlayer;
        }
        else if (int.TryParse(input.Trim(), out int choice) && choice >= 1 && choice <= targets.Count)
        {
            selected = targets[choice - 1].character;
        }
        else
        {
            selected = currentPlayer;
        }

        ShowDetailedGear(selected);
        await terminal.PressAnyKey();
    }

    /// <summary>
    /// Show detailed gear breakdown with all stats for every equipped item
    /// </summary>
    protected void ShowDetailedGear(Character? target = null)
    {
        var player = target ?? currentPlayer;
        if (player == null) return;

        terminal.WriteLine("");
        UIHelper.WriteBoxHeader(terminal, $"Equipment — {player.DisplayName}", "bright_yellow", 76);
        terminal.WriteLine("");

        var slots = new (EquipmentSlot slot, string label)[]
        {
            (EquipmentSlot.MainHand, Loc.Get("base.slot_main_hand")),
            (EquipmentSlot.OffHand, Loc.Get("base.slot_off_hand")),
            (EquipmentSlot.Head, Loc.Get("base.slot_head")),
            (EquipmentSlot.Body, Loc.Get("base.slot_body")),
            (EquipmentSlot.Arms, Loc.Get("base.slot_arms")),
            (EquipmentSlot.Hands, Loc.Get("base.slot_hands")),
            (EquipmentSlot.Legs, Loc.Get("base.slot_legs")),
            (EquipmentSlot.Feet, Loc.Get("base.slot_feet")),
            (EquipmentSlot.Waist, Loc.Get("base.slot_waist")),
            (EquipmentSlot.Face, Loc.Get("base.slot_face")),
            (EquipmentSlot.Cloak, Loc.Get("base.slot_cloak")),
            (EquipmentSlot.Neck, Loc.Get("base.slot_neck")),
            (EquipmentSlot.LFinger, Loc.Get("base.slot_left_ring")),
            (EquipmentSlot.RFinger, Loc.Get("base.slot_right_ring")),
        };

        int totalWP = 0, totalAC = 0, totalStr = 0, totalDex = 0, totalCon = 0;
        int totalInt = 0, totalWis = 0, totalCha = 0, totalAgi = 0, totalDef = 0;
        int totalHP = 0, totalMP = 0, totalSta = 0;
        int itemCount = 0;

        foreach (var (slot, label) in slots)
        {
            var item = player.GetEquipment(slot);
            terminal.SetColor("white");
            terminal.Write($"  {label,-11}");

            if (item == null)
            {
                // Two-handed weapon check for off-hand
                if (slot == EquipmentSlot.OffHand)
                {
                    var mainHand = player.GetEquipment(EquipmentSlot.MainHand);
                    if (mainHand?.Handedness == WeaponHandedness.TwoHanded)
                    {
                        terminal.SetColor("darkgray");
                        terminal.WriteLine(Loc.Get("base.using_2h_weapon"));
                        continue;
                    }
                }
                terminal.SetColor("darkgray");
                terminal.WriteLine(Loc.Get("ui.empty"));
                continue;
            }

            itemCount++;

            // Item name with rarity color
            terminal.SetColor(GetEquipmentRarityColor(item.Rarity));
            terminal.WriteLine(item.IsIdentified ? item.Name : Loc.Get("base.unidentified"));

            // Accumulate totals
            totalWP += item.WeaponPower;
            totalAC += item.ArmorClass + item.ShieldBonus;
            totalStr += item.StrengthBonus;
            totalDex += item.DexterityBonus;
            totalCon += item.ConstitutionBonus;
            totalInt += item.IntelligenceBonus;
            totalWis += item.WisdomBonus;
            totalCha += item.CharismaBonus;
            totalAgi += item.AgilityBonus;
            totalDef += item.DefenceBonus;
            totalHP += item.MaxHPBonus;
            totalMP += item.MaxManaBonus;
            totalSta += item.StaminaBonus;

            if (!item.IsIdentified) continue;

            // Line 1: Combat stats
            var combatStats = new List<string>();
            if (item.WeaponPower > 0) combatStats.Add($"{Loc.Get("ui.stat_wp")}:{item.WeaponPower}");
            if (item.ArmorClass > 0) combatStats.Add($"{Loc.Get("ui.stat_ac")}:{item.ArmorClass}");
            if (item.ShieldBonus > 0) combatStats.Add($"{Loc.Get("ui.stat_block")}:{item.ShieldBonus}");
            if (item.DefenceBonus > 0) combatStats.Add($"{Loc.Get("ui.stat_def")}:{item.DefenceBonus:+#;-#;0}");

            // Primary stats
            if (item.StrengthBonus != 0) combatStats.Add($"{Loc.Get("ui.stat_str")}:{item.StrengthBonus:+#;-#;0}");
            if (item.DexterityBonus != 0) combatStats.Add($"{Loc.Get("ui.stat_dex")}:{item.DexterityBonus:+#;-#;0}");
            if (item.ConstitutionBonus != 0) combatStats.Add($"{Loc.Get("ui.stat_con")}:{item.ConstitutionBonus:+#;-#;0}");
            if (item.IntelligenceBonus != 0) combatStats.Add($"{Loc.Get("ui.stat_int")}:{item.IntelligenceBonus:+#;-#;0}");
            if (item.WisdomBonus != 0) combatStats.Add($"{Loc.Get("ui.stat_wis")}:{item.WisdomBonus:+#;-#;0}");
            if (item.CharismaBonus != 0) combatStats.Add($"{Loc.Get("ui.stat_cha")}:{item.CharismaBonus:+#;-#;0}");
            if (item.AgilityBonus != 0) combatStats.Add($"{Loc.Get("ui.stat_agi")}:{item.AgilityBonus:+#;-#;0}");
            if (item.MaxHPBonus != 0) combatStats.Add($"{Loc.Get("ui.stat_hp")}:{item.MaxHPBonus:+#;-#;0}");
            if (item.MaxManaBonus != 0) combatStats.Add($"{Loc.Get("ui.stat_mp")}:{item.MaxManaBonus:+#;-#;0}");
            if (item.StaminaBonus != 0) combatStats.Add($"{Loc.Get("ui.stat_sta")}:{item.StaminaBonus:+#;-#;0}");

            if (combatStats.Count > 0)
            {
                terminal.SetColor("gray");
                terminal.WriteLine($"             {string.Join(", ", combatStats)}");
            }

            // Line 2: Special properties (enchantments, procs, etc.)
            var specials = new List<string>();
            if (item.CriticalChanceBonus > 0) specials.Add($"Crit +{item.CriticalChanceBonus}%");
            if (item.CriticalDamageBonus > 0) specials.Add($"CritDmg +{item.CriticalDamageBonus}%");
            if (item.LifeSteal > 0) specials.Add($"Lifesteal {item.LifeSteal}%");
            if (item.ManaSteal > 0) specials.Add($"Manasteal {item.ManaSteal}%");
            if (item.ArmorPiercing > 0) specials.Add($"ArmorPen {item.ArmorPiercing}%");
            if (item.Thorns > 0) specials.Add($"Thorns {item.Thorns}%");
            if (item.HPRegen > 0) specials.Add($"HPRegen {item.HPRegen}/rd");
            if (item.ManaRegen > 0) specials.Add($"MPRegen {item.ManaRegen}/rd");
            if (item.PoisonDamage > 0) specials.Add($"Poison {item.PoisonDamage}");
            if (item.MagicResistance > 0) specials.Add($"MagRes {item.MagicResistance}%");
            if (item.HasFireEnchant) specials.Add("Fire");
            if (item.HasFrostEnchant) specials.Add("Frost");
            if (item.HasLightningEnchant) specials.Add("Lightning");
            if (item.HasPoisonEnchant) specials.Add("Poison");
            if (item.HasHolyEnchant) specials.Add("Holy");
            if (item.HasShadowEnchant) specials.Add("Shadow");
            if (item.IsCursed) specials.Add("CURSED");

            if (specials.Count > 0)
            {
                terminal.SetColor("bright_magenta");
                terminal.WriteLine($"             {string.Join(", ", specials)}");
            }
        }

        // Totals
        terminal.WriteLine("");
        terminal.SetColor("bright_yellow");
        terminal.WriteLine($"  {Loc.Get("base.gear_totals", itemCount)}:");
        terminal.SetColor("white");

        // Combat totals
        terminal.Write("    ");
        if (totalWP > 0) { terminal.SetColor("bright_red"); terminal.Write($"WP:{totalWP}  "); }
        if (totalAC > 0) { terminal.SetColor("bright_cyan"); terminal.Write($"{Loc.Get("ui.stat_ac")}:{totalAC}  "); }
        if (totalDef > 0) { terminal.SetColor("bright_cyan"); terminal.Write($"{Loc.Get("ui.stat_def")}:+{totalDef}  "); }
        terminal.WriteLine("");

        // Stat totals
        var statLine = new List<string>();
        if (totalStr != 0) statLine.Add($"{Loc.Get("ui.stat_str")}:{totalStr:+#;-#;0}");
        if (totalDex != 0) statLine.Add($"{Loc.Get("ui.stat_dex")}:{totalDex:+#;-#;0}");
        if (totalCon != 0) statLine.Add($"{Loc.Get("ui.stat_con")}:{totalCon:+#;-#;0}");
        if (totalInt != 0) statLine.Add($"{Loc.Get("ui.stat_int")}:{totalInt:+#;-#;0}");
        if (totalWis != 0) statLine.Add($"{Loc.Get("ui.stat_wis")}:{totalWis:+#;-#;0}");
        if (totalCha != 0) statLine.Add($"{Loc.Get("ui.stat_cha")}:{totalCha:+#;-#;0}");
        if (totalAgi != 0) statLine.Add($"{Loc.Get("ui.stat_agi")}:{totalAgi:+#;-#;0}");
        if (totalHP != 0) statLine.Add($"{Loc.Get("ui.stat_hp")}:{totalHP:+#;-#;0}");
        if (totalMP != 0) statLine.Add($"{Loc.Get("ui.stat_mp")}:{totalMP:+#;-#;0}");
        if (totalSta != 0) statLine.Add($"{Loc.Get("ui.stat_sta")}:{totalSta:+#;-#;0}");

        if (statLine.Count > 0)
        {
            terminal.SetColor("green");
            terminal.WriteLine($"    {string.Join(", ", statLine)}");
        }

        terminal.WriteLine("");
    }

    /// <summary>
    /// Display a single equipment slot for the status screen
    /// </summary>
    private void DisplayEquipmentSlot(EquipmentSlot slot)
    {
        var item = currentPlayer.GetEquipment(slot);

        if (item != null)
        {
            // Color based on rarity
            terminal.SetColor(GetEquipmentRarityColor(item.Rarity));
            terminal.Write(item.Name);

            // Show key stats
            var stats = GetEquipmentStatSummary(item);
            if (!string.IsNullOrEmpty(stats))
            {
                terminal.SetColor("gray");
                terminal.Write($" ({stats})");
            }
            terminal.WriteLine("");
        }
        else
        {
            // Check if off-hand is empty because of a two-handed weapon
            if (slot == EquipmentSlot.OffHand)
            {
                var mainHand = currentPlayer.GetEquipment(EquipmentSlot.MainHand);
                if (mainHand?.Handedness == WeaponHandedness.TwoHanded)
                {
                    terminal.SetColor("darkgray");
                    terminal.WriteLine(Loc.Get("base.using_2h_weapon"));
                    return;
                }
            }
            terminal.SetColor("darkgray");
            terminal.WriteLine(Loc.Get("ui.empty"));
        }
    }

    /// <summary>
    /// Get color based on equipment rarity
    /// </summary>
    private static string GetEquipmentRarityColor(EquipmentRarity rarity)
    {
        return rarity switch
        {
            EquipmentRarity.Common => "white",
            EquipmentRarity.Uncommon => "green",
            EquipmentRarity.Rare => "blue",
            EquipmentRarity.Epic => "magenta",
            EquipmentRarity.Legendary => "yellow",
            EquipmentRarity.Artifact => "bright_red",
            _ => "white"
        };
    }

    /// <summary>
    /// Get a short summary of equipment stats
    /// </summary>
    private static string GetEquipmentStatSummary(Equipment item)
    {
        var stats = new List<string>();

        if (item.WeaponPower > 0) stats.Add($"{Loc.Get("ui.stat_wp")}:{item.WeaponPower}");
        if (item.ArmorClass > 0) stats.Add($"{Loc.Get("ui.stat_ac")}:{item.ArmorClass}");
        if (item.ShieldBonus > 0) stats.Add($"{Loc.Get("ui.stat_block")}:{item.ShieldBonus}");
        if (item.DefenceBonus != 0) stats.Add($"{Loc.Get("ui.stat_def")}:{item.DefenceBonus:+#;-#;0}");
        if (item.StrengthBonus != 0) stats.Add($"{Loc.Get("ui.stat_str")}:{item.StrengthBonus:+#;-#;0}");
        if (item.DexterityBonus != 0) stats.Add($"{Loc.Get("ui.stat_dex")}:{item.DexterityBonus:+#;-#;0}");
        if (item.AgilityBonus != 0) stats.Add($"Agi:{item.AgilityBonus:+#;-#;0}");
        if (item.ConstitutionBonus != 0) stats.Add($"{Loc.Get("ui.stat_con")}:{item.ConstitutionBonus:+#;-#;0}");
        if (item.IntelligenceBonus != 0) stats.Add($"{Loc.Get("ui.stat_int")}:{item.IntelligenceBonus:+#;-#;0}");
        if (item.WisdomBonus != 0) stats.Add($"Wis:{item.WisdomBonus:+#;-#;0}");
        if (item.CharismaBonus != 0) stats.Add($"Cha:{item.CharismaBonus:+#;-#;0}");
        if (item.MaxHPBonus != 0) stats.Add($"{Loc.Get("ui.stat_hp")}:{item.MaxHPBonus:+#;-#;0}");
        if (item.MaxManaBonus != 0) stats.Add($"{Loc.Get("ui.stat_mp")}:{item.MaxManaBonus:+#;-#;0}");
        if (item.StaminaBonus != 0) stats.Add($"Sta:{item.StaminaBonus:+#;-#;0}");

        // Limit to 4 stats for concise display
        return string.Join(", ", stats.Take(4));
    }

    /// <summary>
    /// Display total equipment bonuses
    /// </summary>
    private void DisplayEquipmentTotals()
    {
        int totalWeapPow = 0, totalArmPow = 0;
        int totalStr = 0, totalDex = 0, totalAgi = 0, totalCon = 0, totalInt = 0, totalWis = 0, totalCha = 0;
        int totalMaxHP = 0, totalMaxMana = 0, totalDef = 0, totalSta = 0;

        foreach (var slot in Enum.GetValues<EquipmentSlot>())
        {
            var item = currentPlayer.GetEquipment(slot);
            if (item != null)
            {
                totalWeapPow += item.WeaponPower;
                totalArmPow += item.ArmorClass + item.ShieldBonus;
                totalStr += item.StrengthBonus;
                totalDex += item.DexterityBonus;
                totalAgi += item.AgilityBonus;
                totalCon += item.ConstitutionBonus;
                totalInt += item.IntelligenceBonus;
                totalWis += item.WisdomBonus;
                totalCha += item.CharismaBonus;
                totalMaxHP += item.MaxHPBonus;
                totalMaxMana += item.MaxManaBonus;
                totalDef += item.DefenceBonus;
                totalSta += item.StaminaBonus;
            }
        }

        terminal.SetColor("yellow");
        terminal.WriteLine(Loc.Get("base.equipment_totals"));
        terminal.SetColor("white");
        terminal.Write("  " + Loc.Get("base.weapon_power") + " ");
        terminal.SetColor("bright_red");
        terminal.Write($"{totalWeapPow}");
        terminal.SetColor("white");
        terminal.Write("  |  " + Loc.Get("base.armor_class") + " ");
        terminal.SetColor("bright_cyan");
        terminal.WriteLine($"{totalArmPow}");

        // Only show stat bonuses if there are any
        bool hasStatBonuses = totalStr != 0 || totalDex != 0 || totalAgi != 0 || totalCon != 0 ||
                              totalInt != 0 || totalWis != 0 || totalCha != 0 ||
                              totalMaxHP != 0 || totalMaxMana != 0 || totalDef != 0 || totalSta != 0;
        if (hasStatBonuses)
        {
            terminal.SetColor("white");
            terminal.Write("  " + Loc.Get("base.bonuses") + " ");
            if (totalStr != 0) { terminal.SetColor("green"); terminal.Write($"Str {totalStr:+#;-#;0}  "); }
            if (totalDex != 0) { terminal.SetColor("green"); terminal.Write($"Dex {totalDex:+#;-#;0}  "); }
            if (totalAgi != 0) { terminal.SetColor("green"); terminal.Write($"Agi {totalAgi:+#;-#;0}  "); }
            if (totalCon != 0) { terminal.SetColor("green"); terminal.Write($"Con {totalCon:+#;-#;0}  "); }
            if (totalInt != 0) { terminal.SetColor("cyan"); terminal.Write($"Int {totalInt:+#;-#;0}  "); }
            if (totalWis != 0) { terminal.SetColor("cyan"); terminal.Write($"Wis {totalWis:+#;-#;0}  "); }
            if (totalCha != 0) { terminal.SetColor("cyan"); terminal.Write($"Cha {totalCha:+#;-#;0}  "); }
            if (totalMaxHP != 0) { terminal.SetColor("red"); terminal.Write($"MaxHP {totalMaxHP:+#;-#;0}  "); }
            if (totalMaxMana != 0) { terminal.SetColor("blue"); terminal.Write($"MaxMP {totalMaxMana:+#;-#;0}  "); }
            if (totalDef != 0) { terminal.SetColor("bright_cyan"); terminal.Write($"Def {totalDef:+#;-#;0}  "); }
            if (totalSta != 0) { terminal.SetColor("yellow"); terminal.Write($"Sta {totalSta:+#;-#;0}  "); }
            terminal.WriteLine("");
        }
    }

    /// <summary>
    /// Get location name for display
    /// </summary>
    public static string GetLocationName(GameLocation location)
    {
        return location switch
        {
            GameLocation.MainStreet => "Main Street",
            GameLocation.TheInn => "The Inn",
            GameLocation.DarkAlley => "Dark Alley",
            GameLocation.Church => "Church",
            GameLocation.WeaponShop => "Weapon Shop",
            GameLocation.ArmorShop => "Armor Shop",
            GameLocation.Bank => "Bank",
            GameLocation.AuctionHouse => "Auction House",
            GameLocation.Dungeons => "Dungeons",
            GameLocation.Castle => "Royal Castle",
            GameLocation.Dormitory => "Dormitory",
            GameLocation.AnchorRoad => "Anchor Road",
            GameLocation.Temple => "Temple",
            GameLocation.BobsBeer => "Bob's Beer",
            GameLocation.Healer => "Healer",
            GameLocation.MagicShop => "Magic Shop",
            GameLocation.Master => "Level Master",
            _ => location.ToString()
        };
    }

    /// <summary>
    /// Get location key for navigation
    /// </summary>
    public static string GetLocationKey(GameLocation location)
    {
        return location switch
        {
            GameLocation.MainStreet => "M",
            GameLocation.TheInn => "I",
            GameLocation.DarkAlley => "D",
            GameLocation.Church => "C",
            GameLocation.WeaponShop => "W",
            GameLocation.ArmorShop => "A",
            GameLocation.Bank => "B",
            GameLocation.AuctionHouse => "K",
            GameLocation.Dungeons => "U",
            GameLocation.Castle => "S",
            GameLocation.Dormitory => "O",
            GameLocation.AnchorRoad => "R",
            GameLocation.Temple => "T",
            GameLocation.BobsBeer => "H",
            GameLocation.Healer => "E",
            GameLocation.MagicShop => "G",
            GameLocation.Master => "L",
            _ => "?"
        };
    }

    /// <summary>
    /// Add NPC to this location
    /// </summary>
    public virtual void AddNPC(NPC npc)
    {
        if (!LocationNPCs.Contains(npc))
        {
            LocationNPCs.Add(npc);
            // Don't override CurrentLocation here — it was already set correctly
            // by NPC.UpdateLocation() before this method is called.
            // Using LocationId.ToString().ToLower() produced broken names like "theinn".
        }
    }

    /// <summary>
    /// Remove NPC from this location
    /// </summary>
    public virtual void RemoveNPC(NPC npc)
    {
        LocationNPCs.Remove(npc);
    }

    /// <summary>
    /// Get location description for online system (Pascal compatible)
    /// </summary>
    public virtual string GetLocationDescription()
    {
        return LocationId switch
        {
            GameLocation.MainStreet => "Main street",
            GameLocation.TheInn => "Inn",
            GameLocation.DarkAlley => "outside the Shady Shops",
            GameLocation.Church => "Church",
            GameLocation.Dungeons => "Dungeons",
            GameLocation.WeaponShop => "Weapon shop",
            GameLocation.Master => "level master",
            GameLocation.MagicShop => "Magic shop",
            GameLocation.ArmorShop => "Armor shop",
            GameLocation.Bank => "Bank",
            GameLocation.Healer => "Healer",
            GameLocation.AuctionHouse => "Auction House",
            GameLocation.Dormitory => "Dormitory",
            GameLocation.AnchorRoad => "Anchor road",
            GameLocation.BobsBeer => "Bobs Beer",
            GameLocation.Castle => "Royal Castle",
            GameLocation.Prison => "Royal Prison",
            GameLocation.Temple => "Holy Temple",
            _ => Name
        };
    }

    // Convenience constructor for legacy classes that only provide name and skip description
    protected BaseLocation(GameLocation locationId, string name) : this(locationId, name, "")
    {
    }

    // Legacy constructor where parameters were (string name, GameLocation id)
    protected BaseLocation(string name, GameLocation locationId) : this(locationId, name, "")
    {
    }

    // Legacy constructor that passed only a name (defaults to NoWhere)
    protected BaseLocation(string name) : this(GameLocation.NoWhere, name, "")
    {
    }

    // Some pre-refactor code refers to LocationName instead of Name
    public string LocationName
    {
        get => Name;
        set => Name = value;
    }

    // ShortDescription used by some legacy locations
    public string ShortDescription { get; set; } = string.Empty;

    // Pascal fields expected by Prison/Temple legacy code
    public string LocationDescription { get; set; } = string.Empty;
    public HashSet<CharacterClass> AllowedClasses { get; set; } = new();
    public int LevelRequirement { get; set; } = 1;

    // Legacy single-parameter Enter wrapper
    public virtual async Task Enter(Character player)
    {
        await EnterLocation(player, TerminalEmulator.Instance ?? new TerminalEmulator());
    }

    // Legacy OnEnter hook – alias of DisplayLocation for now
    public virtual void OnEnter(Character player)
    {
        // For now simply display location header
        DisplayLocation();
    }

    // Allow derived locations to add menu options without maintaining their own list
    protected List<(string Key, string Text)> LegacyMenuOptions { get; } = new();

    public void AddMenuOption(string key, string text)
    {
        LegacyMenuOptions.Add((key, text));
    }

    // Stub for ShowLocationMenu used by some locations
    protected virtual void ShowLocationMenu()
    {
        // Basic menu display if terminal available
        if (terminal == null || LegacyMenuOptions.Count == 0) return;
        terminal.Clear();
        terminal.WriteLine($"{LocationName} Menu:");
        foreach (var (Key, Text) in LegacyMenuOptions)
        {
            terminal.WriteLine($"({Key}) {Text}");
        }
    }

    // Expose CurrentPlayer as Player for legacy code while still maintaining Character
    public Player? CurrentPlayer { get; protected set; }

    // Legacy exit helper used by some derived locations
    protected virtual async Task Exit(Player player)
    {
        // Simply break out by returning
        await Task.CompletedTask;
    }

    // Parameterless constructor retained for serialization or manual instantiation
    protected BaseLocation()
    {
        LocationId = GameLocation.NoWhere;
        Name = string.Empty;
        Description = string.Empty;
    }

    // Legacy helper referenced by some shop locations
    protected void ExitLocation()
    {
        // simply break – actual navigation handled by LocationManager
    }

    /// <summary>
    /// An NPC with strong feelings about the player may approach them.
    /// Positive impression → friendly interaction (gift, info, compliment).
    /// Negative impression → confrontation (threat, warning).
    /// Tracked per-NPC to prevent spam (minimum 10 turns between approaches from same NPC).
    /// </summary>
    protected virtual async Task TryNPCApproach()
    {
        if (currentPlayer == null || terminal == null) return;

        var npcsHere = GetLiveNPCsAtLocation();
        if (npcsHere.Count == 0) return;

        var playerName = currentPlayer.Name2 ?? "";
        var turnCount = currentPlayer.TurnCount;

        // Find NPCs with strong impressions (|impression| > 0.5)
        // Prioritize story NPCs and companions
        var candidates = npcsHere
            .Where(npc => npc.IsAlive && !npc.IsDead && npc.Memory != null)
            .Select(npc => new
            {
                NPC = npc,
                Impression = npc.Memory.GetCharacterImpression(playerName),
                IsStory = npc.IsStoryNPC
            })
            .Where(c => Math.Abs(c.Impression) > 0.5f)
            .OrderByDescending(c => c.IsStory)           // Story NPCs first
            .ThenByDescending(c => Math.Abs(c.Impression)) // Strongest feelings first
            .ToList();

        if (candidates.Count == 0) return;

        // Check cooldown for each candidate
        foreach (var candidate in candidates)
        {
            var npcKey = candidate.NPC.Name2;
            if (_lastApproachedTurn.TryGetValue(npcKey, out var lastTurn))
            {
                if (turnCount - lastTurn < MinTurnsBetweenApproaches)
                    continue; // Too soon
            }

            // This NPC approaches!
            _lastApproachedTurn[npcKey] = turnCount;

            terminal.WriteLine("");
            if (!IsScreenReader)
            {
                terminal.SetColor("bright_yellow");
                terminal.WriteLine("─────────────────────────────────────────");
            }

            if (candidate.Impression > 0.5f)
            {
                await ShowFriendlyApproach(candidate.NPC);
            }
            else
            {
                await ShowHostileApproach(candidate.NPC);
            }

            if (!IsScreenReader)
            {
                terminal.SetColor("bright_yellow");
                terminal.WriteLine("─────────────────────────────────────────");
            }
            terminal.WriteLine("");
            await terminal.PressAnyKey();
            return; // Only one approach per turn
        }
    }

    private async Task ShowFriendlyApproach(NPC npc)
    {
        var random = _npcRandom;
        var approachType = random.Next(4);

        switch (approachType)
        {
            case 0: // Gift
                terminal.SetColor("bright_green");
                terminal.WriteLine(Loc.Get("base.npc_approaches", npc.Name2));
                terminal.SetColor("white");
                terminal.WriteLine(Loc.Get("base.npc_gift_dialogue"));

                // Small gold gift based on NPC level
                var giftGold = (int)(npc.Level * (5 + random.Next(10)));
                currentPlayer.Gold += giftGold;
                terminal.SetColor("bright_yellow");
                terminal.WriteLine(Loc.Get("base.received_gold", giftGold));
                break;

            case 1: // Information
                terminal.SetColor("bright_cyan");
                terminal.WriteLine(Loc.Get("base.npc_catches_eye", npc.Name2));
                terminal.SetColor("white");
                var tips = new[]
                {
                    Loc.Get("base.tip_weapons"),
                    Loc.Get("base.tip_healer"),
                    Loc.Get("base.tip_training"),
                    Loc.Get("base.tip_temple"),
                    Loc.Get("base.tip_marketplace"),
                };
                terminal.WriteLine(tips[random.Next(tips.Length)]);
                break;

            case 2: // Compliment
                terminal.SetColor("bright_green");
                terminal.WriteLine(Loc.Get("base.npc_smiles", npc.Name2));
                terminal.SetColor("white");
                var compliments = new[]
                {
                    Loc.Get("base.compliment1"),
                    Loc.Get("base.compliment2"),
                    Loc.Get("base.compliment3"),
                    Loc.Get("base.compliment4"),
                };
                terminal.WriteLine(compliments[random.Next(compliments.Length)]);
                break;

            default: // Healing potion gift
                terminal.SetColor("bright_green");
                terminal.WriteLine(Loc.Get("base.npc_presses", npc.Name2));
                terminal.SetColor("white");
                terminal.WriteLine(Loc.Get("base.npc_heal_dialogue"));

                var healAmount = Math.Min(currentPlayer.MaxHP / 5, currentPlayer.MaxHP - currentPlayer.HP);
                if (healAmount > 0)
                {
                    currentPlayer.HP += healAmount;
                    terminal.SetColor("bright_green");
                    terminal.WriteLine(Loc.Get("base.restored_hp", healAmount));
                }
                else
                {
                    terminal.SetColor("gray");
                    terminal.WriteLine(Loc.Get("base.already_healthy"));
                }
                break;
        }

        await Task.CompletedTask;
    }

    private async Task ShowHostileApproach(NPC npc)
    {
        var random = _npcRandom;
        var approachType = random.Next(3);

        switch (approachType)
        {
            case 0: // Threat
                terminal.SetColor("red");
                terminal.WriteLine(Loc.Get("base.npc_blocks_path", npc.Name2));
                terminal.SetColor("white");
                var threats = new[]
                {
                    Loc.Get("base.threat1"),
                    Loc.Get("base.threat2"),
                    Loc.Get("base.threat3"),
                };
                terminal.WriteLine(threats[random.Next(threats.Length)]);
                break;

            case 1: // Warning
                terminal.SetColor("yellow");
                terminal.WriteLine(Loc.Get("base.npc_catches_arm", npc.Name2));
                terminal.SetColor("white");
                terminal.WriteLine(Loc.Get("base.warning_talking"));
                break;

            default: // Cold shoulder with intimidation
                terminal.SetColor("red");
                terminal.WriteLine(Loc.Get("base.npc_stares_down", npc.Name2));
                terminal.SetColor("white");
                terminal.WriteLine(Loc.Get("base.hand_on_pommel"));
                terminal.SetColor("gray");
                terminal.WriteLine(Loc.Get("base.not_welcome"));
                break;
        }

        await Task.CompletedTask;
    }

    // ========== Online Mail System ==========

    /// <summary>
    /// Show the player's mailbox with interactive options.
    /// </summary>
    protected async Task ShowMailbox()
    {
        var backend = SaveSystem.Instance.Backend as SqlSaveBackend;
        if (backend == null) return;

        string username = currentPlayer.DisplayName.ToLower();
        int page = 0;
        const int pageSize = 10;

        while (true)
        {
            terminal.ClearScreen();
            WriteBoxHeader(Loc.Get("base.your_mailbox"), "bright_cyan");
            terminal.WriteLine("");

            int unread = backend.GetUnreadMailCount(username);
            var inbox = await backend.GetMailInbox(username, pageSize, page * pageSize);

            terminal.SetColor("white");
            terminal.WriteLine(Loc.Get("base.mail_unread", unread));
            if (!IsScreenReader)
            {
                terminal.SetColor("darkgray");
                terminal.WriteLine(new string('─', 70));
            }

            if (inbox.Count == 0 && page == 0)
            {
                terminal.SetColor("yellow");
                terminal.WriteLine(Loc.Get("base.mailbox_empty"));
            }
            else
            {
                terminal.SetColor("white");
                terminal.WriteLine($"{"#",-4} {"From",-16} {"Date",-12} {"Message",-36}");
                if (!IsScreenReader)
                {
                    terminal.SetColor("darkgray");
                    terminal.WriteLine(new string('─', 70));
                }

                for (int i = 0; i < inbox.Count; i++)
                {
                    var msg = inbox[i];
                    string unreadMark = msg.IsRead ? " " : "*";
                    string dateStr = GameConfig.FormatShortDate(msg.CreatedAt, currentPlayer.DateFormatPreference);
                    string msgPreview = msg.Message.Length > 35 ? msg.Message.Substring(0, 32) + "..." : msg.Message;

                    terminal.SetColor(msg.IsRead ? "gray" : "white");
                    terminal.WriteLine($"{unreadMark}{i + 1,-3} {msg.FromPlayer,-16} {dateStr,-12} {msgPreview,-36}");
                }
            }

            terminal.WriteLine("");
            terminal.SetColor("white");
            terminal.Write("[");
            terminal.SetColor("bright_yellow");
            terminal.Write("R");
            terminal.SetColor("white");
            terminal.Write($"]{Loc.Get("base.mail_read_label")}  [");
            terminal.SetColor("bright_yellow");
            terminal.Write("S");
            terminal.SetColor("white");
            terminal.Write($"]{Loc.Get("base.mail_send_label")}  [");
            terminal.SetColor("bright_yellow");
            terminal.Write("D");
            terminal.SetColor("white");
            terminal.Write(Loc.Get("base.mail_delete_menu"));
            terminal.SetColor("bright_yellow");
            terminal.Write("N");
            terminal.SetColor("white");
            terminal.Write(Loc.Get("base.mail_next_menu"));
            terminal.SetColor("bright_yellow");
            terminal.Write("P");
            terminal.SetColor("white");
            terminal.Write(Loc.Get("base.mail_prev_menu"));
            terminal.SetColor("bright_yellow");
            terminal.Write("Q");
            terminal.SetColor("white");
            terminal.WriteLine($"]{Loc.Get("base.mail_quit_label")}");
            terminal.Write("> ");
            terminal.SetColor("white");
            string input = (await terminal.ReadLineAsync()).Trim();

            if (string.IsNullOrEmpty(input) || input.ToUpper() == "Q")
                break;

            string cmd = input.ToUpper();

            if (cmd == "N")
            {
                if (inbox.Count >= pageSize) page++;
            }
            else if (cmd == "P")
            {
                if (page > 0) page--;
            }
            else if (cmd == "S")
            {
                await SendMail(backend, username);
            }
            else if (cmd.StartsWith("R") && cmd.Length > 1 && int.TryParse(cmd.Substring(1).Trim(), out int readIdx))
            {
                if (readIdx >= 1 && readIdx <= inbox.Count)
                    await ReadMail(backend, inbox[readIdx - 1]);
            }
            else if (cmd.StartsWith("D") && cmd.Length > 1 && int.TryParse(cmd.Substring(1).Trim(), out int delIdx))
            {
                if (delIdx >= 1 && delIdx <= inbox.Count)
                {
                    await backend.DeleteMessage(inbox[delIdx - 1].Id, username);
                    terminal.SetColor("bright_green");
                    terminal.WriteLine(Loc.Get("base.mail_deleted"));
                    await Task.Delay(1000);
                }
            }
            else if (int.TryParse(cmd, out int directRead) && directRead >= 1 && directRead <= inbox.Count)
            {
                await ReadMail(backend, inbox[directRead - 1]);
            }
        }
    }

    private async Task ReadMail(SqlSaveBackend backend, PlayerMessage msg)
    {
        terminal.ClearScreen();
        if (IsScreenReader)
        {
            terminal.SetColor("bright_cyan");
            terminal.WriteLine(Loc.Get("base.mail_message_from", msg.FromPlayer.ToUpper()));
        }
        else
        {
            terminal.SetColor("bright_cyan");
            terminal.WriteLine(Loc.Get("base.mail_message_from_box", msg.FromPlayer.ToUpper()));
        }
        terminal.WriteLine("");
        terminal.SetColor("gray");
        terminal.WriteLine(Loc.Get("base.mail_date", GameConfig.FormatDate(msg.CreatedAt, currentPlayer.DateFormatPreference, includeTime: true)));
        terminal.SetColor("white");
        terminal.WriteLine(Loc.Get("base.mail_type", msg.MessageType));
        terminal.WriteLine("");
        terminal.SetColor("white");
        terminal.WriteLine(msg.Message);
        terminal.WriteLine("");

        // Mark as read (using existing MarkMessagesRead won't work for a single message,
        // but the message has been seen)
        terminal.SetColor("darkgray");
        terminal.WriteLine(Loc.Get("ui.press_enter"));
        await terminal.ReadKeyAsync();
    }

    private async Task SendMail(SqlSaveBackend backend, string senderUsername)
    {
        // Spam protection
        int sentToday = backend.GetMailsSentToday(senderUsername);
        if (sentToday >= 20)
        {
            terminal.SetColor("red");
            terminal.WriteLine(Loc.Get("base.mail_daily_limit"));
            await Task.Delay(2000);
            return;
        }

        terminal.WriteLine("");
        terminal.SetColor("cyan");
        terminal.Write(Loc.Get("base.mail_send_to"));
        terminal.SetColor("white");
        string recipient = await terminal.ReadLineAsync();

        if (string.IsNullOrWhiteSpace(recipient)) return;

        // Resolve recipient (handles username or display name input)
        string? resolvedMailName = backend.ResolvePlayerDisplayName(recipient);
        if (resolvedMailName == null)
        {
            terminal.SetColor("red");
            terminal.WriteLine(Loc.Get("base.mail_player_not_found", recipient));
            await Task.Delay(2000);
            return;
        }
        recipient = resolvedMailName;

        terminal.SetColor("cyan");
        terminal.Write(Loc.Get("base.mail_message_prompt"));
        terminal.SetColor("white");
        string message = await terminal.ReadLineAsync();

        if (string.IsNullOrWhiteSpace(message)) return;
        if (message.Length > 200)
            message = message.Substring(0, 200);

        await backend.SendMessage(currentPlayer.DisplayName, recipient, "mail", message);

        // Real-time notification in MUD mode
        UsurperRemake.Server.MudServer.Instance?.SendToPlayer(recipient,
            $"\u001b[35m  [Mail] {currentPlayer.DisplayName}: {message}\u001b[0m");

        terminal.SetColor("bright_green");
        terminal.WriteLine(Loc.Get("base.mail_sent", recipient));
        await Task.Delay(1500);
    }

    // ========== Player Trading System ==========

    /// <summary>
    /// Show the player's trade packages menu.
    /// </summary>
    protected async Task ShowTradeMenu()
    {
        var backend = SaveSystem.Instance.Backend as SqlSaveBackend;
        if (backend == null) return;

        string username = currentPlayer.DisplayName.ToLower();

        while (true)
        {
            terminal.ClearScreen();
            WriteBoxHeader(Loc.Get("base.trade_packages"), "bright_cyan");
            terminal.WriteLine("");

            // Get incoming and sent offers
            var incoming = await backend.GetPendingTradeOffers(username);
            var sent = await backend.GetSentTradeOffers(username);

            // Show incoming
            terminal.SetColor("bright_yellow");
            terminal.WriteLine(Loc.Get("base.trade_incoming", incoming.Count));
            if (!IsScreenReader)
            {
                terminal.SetColor("darkgray");
                terminal.WriteLine(new string('─', 65));
            }

            if (incoming.Count == 0)
            {
                terminal.SetColor("gray");
                terminal.WriteLine(Loc.Get("base.trade_no_pending"));
            }
            else
            {
                for (int i = 0; i < incoming.Count; i++)
                {
                    var offer = incoming[i];
                    string itemDesc = ParseTradeItems(offer.ItemsJson);
                    string goldStr = offer.Gold > 0 ? $"{offer.Gold:N0}g" : "";
                    string details = !string.IsNullOrEmpty(itemDesc) && !string.IsNullOrEmpty(goldStr)
                        ? $"{itemDesc} + {goldStr}" : $"{itemDesc}{goldStr}";
                    if (string.IsNullOrEmpty(details)) details = "(empty)";

                    terminal.SetColor("white");
                    terminal.Write($"  {i + 1}. ");
                    terminal.SetColor("bright_green");
                    terminal.Write($"[{Loc.Get("base.new_label")}] ");
                    terminal.SetColor("white");
                    terminal.Write($"{Loc.Get("base.from_label")} {offer.FromDisplayName}: {details}");
                    if (!string.IsNullOrEmpty(offer.Message))
                    {
                        terminal.SetColor("gray");
                        terminal.Write($"  \"{offer.Message}\"");
                    }
                    terminal.WriteLine("");
                }
            }

            terminal.WriteLine("");

            // Show sent
            terminal.SetColor("bright_yellow");
            terminal.WriteLine(Loc.Get("base.trade_sent", sent.Count));
            if (!IsScreenReader)
            {
                terminal.SetColor("darkgray");
                terminal.WriteLine(new string('─', 65));
            }

            if (sent.Count == 0)
            {
                terminal.SetColor("gray");
                terminal.WriteLine(Loc.Get("base.trade_no_outgoing"));
            }
            else
            {
                int offset = incoming.Count;
                for (int i = 0; i < sent.Count; i++)
                {
                    var offer = sent[i];
                    string itemDesc = ParseTradeItems(offer.ItemsJson);
                    string goldStr = offer.Gold > 0 ? $"{offer.Gold:N0}g" : "";
                    string details = !string.IsNullOrEmpty(itemDesc) && !string.IsNullOrEmpty(goldStr)
                        ? $"{itemDesc} + {goldStr}" : $"{itemDesc}{goldStr}";
                    if (string.IsNullOrEmpty(details)) details = "(empty)";

                    terminal.SetColor("gray");
                    terminal.WriteLine($"  {offset + i + 1}. To {offer.ToDisplayName}: {details}  (pending)");
                }
            }

            terminal.WriteLine("");
            if (!IsScreenReader)
            {
                terminal.SetColor("darkgray");
                terminal.WriteLine(new string('─', 65));
            }
            terminal.SetColor("cyan");
            terminal.WriteLine(Loc.Get("base.trade_commands"));
            terminal.WriteLine("");
            terminal.SetColor("white");
            terminal.Write("  [");
            terminal.SetColor("bright_yellow");
            terminal.Write("A#");
            terminal.SetColor("white");
            terminal.Write(Loc.Get("base.trade_accept"));
            terminal.Write("[");
            terminal.SetColor("bright_yellow");
            terminal.Write("D#");
            terminal.SetColor("white");
            terminal.WriteLine(Loc.Get("base.trade_decline"));
            terminal.Write("  [");
            terminal.SetColor("bright_yellow");
            terminal.Write("S");
            terminal.SetColor("white");
            terminal.Write(Loc.Get("base.trade_send_new"));
            terminal.Write("[");
            terminal.SetColor("bright_yellow");
            terminal.Write("C#");
            terminal.SetColor("white");
            terminal.WriteLine(Loc.Get("base.trade_cancel_sent"));
            terminal.Write("  [");
            terminal.SetColor("bright_yellow");
            terminal.Write("Q");
            terminal.SetColor("white");
            terminal.WriteLine(Loc.Get("base.trade_quit"));
            terminal.WriteLine("");
            terminal.SetColor("gray");
            if (incoming.Count > 0)
                terminal.WriteLine(Loc.Get("base.trade_example"));
            terminal.Write("> ");
            terminal.SetColor("white");
            string input = (await terminal.ReadLineAsync()).Trim();

            if (string.IsNullOrEmpty(input) || input.ToUpper() == "Q")
                break;

            string cmd = input.ToUpper();

            if (cmd == "S")
            {
                await SendTradePackage(backend, username);
            }
            else if (cmd.StartsWith("A") && cmd.Length > 1 && int.TryParse(cmd.Substring(1).Trim(), out int acceptIdx))
            {
                if (acceptIdx >= 1 && acceptIdx <= incoming.Count)
                    await AcceptTradeOffer(backend, incoming[acceptIdx - 1]);
            }
            else if (cmd.StartsWith("D") && cmd.Length > 1 && int.TryParse(cmd.Substring(1).Trim(), out int declineIdx))
            {
                if (declineIdx >= 1 && declineIdx <= incoming.Count)
                    await DeclineTradeOffer(backend, incoming[declineIdx - 1]);
            }
            else if (cmd.StartsWith("C") && cmd.Length > 1 && int.TryParse(cmd.Substring(1).Trim(), out int cancelIdx))
            {
                int sentIdx = cancelIdx - incoming.Count;
                if (sentIdx >= 1 && sentIdx <= sent.Count)
                    await CancelTradeOffer(backend, sent[sentIdx - 1]);
            }
        }
    }

    private string ParseTradeItems(string itemsJson)
    {
        try
        {
            if (string.IsNullOrEmpty(itemsJson) || itemsJson == "[]") return "";
            var items = System.Text.Json.JsonSerializer.Deserialize<List<Dictionary<string, object>>>(itemsJson);
            if (items == null || items.Count == 0) return "";
            var names = items.Select(i => i.ContainsKey("name") ? i["name"]?.ToString() ?? "?" : "?");
            return string.Join(", ", names);
        }
        catch (Exception ex) { DebugLogger.Instance.LogError("LOCATION", $"[ParseTradeItems] Failed to parse trade item JSON: {ex.Message}"); return "items"; }
    }

    private async Task AcceptTradeOffer(SqlSaveBackend backend, TradeOffer offer)
    {
        // Add gold to player
        if (offer.Gold > 0)
        {
            currentPlayer.Gold += offer.Gold;
        }

        // Add items to player inventory
        if (!string.IsNullOrEmpty(offer.ItemsJson) && offer.ItemsJson != "[]")
        {
            try
            {
                var items = System.Text.Json.JsonSerializer.Deserialize<List<UsurperRemake.Systems.InventoryItemData>>(
                    offer.ItemsJson, new System.Text.Json.JsonSerializerOptions { PropertyNamingPolicy = System.Text.Json.JsonNamingPolicy.CamelCase });
                if (items != null)
                {
                    foreach (var itemData in items)
                    {
                        currentPlayer.Inventory.Add(new Item
                        {
                            Name = itemData.Name ?? "Unknown",
                            Type = itemData.Type,
                            Value = itemData.Value,
                            Attack = itemData.Attack,
                            Armor = itemData.Armor,
                            HP = itemData.HP,
                            Mana = itemData.Mana,
                            Strength = itemData.Strength,
                            Defence = itemData.Defence,
                            Dexterity = itemData.Dexterity
                        });
                    }
                }
            }
            catch (Exception ex)
            {
                DebugLogger.Instance.LogError("TRADE", $"Failed to parse trade items: {ex.Message}");
            }
        }

        await backend.UpdateTradeOfferStatus(offer.Id, "accepted");
        await backend.SendMessage("System", offer.FromPlayer, "trade",
            $"{currentPlayer.DisplayName} accepted your package!");
        UsurperRemake.Server.MudServer.Instance?.SendToPlayer(offer.FromPlayer,
            $"\u001b[92m  {currentPlayer.DisplayName} accepted your package!\u001b[0m");

        terminal.SetColor("bright_green");
        if (offer.Gold > 0)
            terminal.WriteLine(Loc.Get("base.trade_received_gold", $"{offer.Gold:N0}"));
        terminal.WriteLine(Loc.Get("base.trade_accepted"));
        await Task.Delay(1500);
    }

    private async Task DeclineTradeOffer(SqlSaveBackend backend, TradeOffer offer)
    {
        await backend.UpdateTradeOfferStatus(offer.Id, "declined");

        // Return gold to sender
        if (offer.Gold > 0)
        {
            await backend.AddGoldToPlayer(offer.FromPlayer, offer.Gold);
        }

        // Return items to sender's inventory in their save data
        if (!string.IsNullOrEmpty(offer.ItemsJson) && offer.ItemsJson != "[]")
        {
            await backend.AddItemsToPlayerSave(offer.FromPlayer, offer.ItemsJson);
        }

        bool hasItems = !string.IsNullOrEmpty(offer.ItemsJson) && offer.ItemsJson != "[]";
        string returnMsg = hasItems
            ? $"{currentPlayer.DisplayName} declined your package. Items and gold returned."
            : $"{currentPlayer.DisplayName} declined your package. Gold returned.";

        await backend.SendMessage("System", offer.FromPlayer, "trade", returnMsg);
        UsurperRemake.Server.MudServer.Instance?.SendToPlayer(offer.FromPlayer,
            $"\u001b[93m  {returnMsg}\u001b[0m");

        terminal.SetColor("yellow");
        terminal.WriteLine(Loc.Get("base.trade_declined"));
        await Task.Delay(1500);
    }

    private async Task CancelTradeOffer(SqlSaveBackend backend, TradeOffer offer)
    {
        await backend.UpdateTradeOfferStatus(offer.Id, "cancelled");

        // Return gold to sender (self)
        if (offer.Gold > 0)
        {
            currentPlayer.Gold += offer.Gold;
        }

        // Return items to sender (self) — parse from stored JSON
        if (!string.IsNullOrEmpty(offer.ItemsJson) && offer.ItemsJson != "[]")
        {
            try
            {
                var items = System.Text.Json.JsonSerializer.Deserialize<List<UsurperRemake.Systems.InventoryItemData>>(
                    offer.ItemsJson, new System.Text.Json.JsonSerializerOptions { PropertyNamingPolicy = System.Text.Json.JsonNamingPolicy.CamelCase });
                if (items != null)
                {
                    foreach (var itemData in items)
                    {
                        currentPlayer.Inventory.Add(new Item
                        {
                            Name = itemData.Name ?? "Unknown",
                            Type = itemData.Type,
                            Value = itemData.Value,
                            Attack = itemData.Attack,
                            Armor = itemData.Armor,
                            HP = itemData.HP,
                            Mana = itemData.Mana,
                            Strength = itemData.Strength,
                            Defence = itemData.Defence,
                            Dexterity = itemData.Dexterity
                        });
                    }
                    terminal.SetColor("bright_green");
                    terminal.WriteLine($"  {items.Count} item(s) returned to your inventory.");
                }
            }
            catch (Exception ex)
            {
                DebugLogger.Instance.LogError("TRADE", $"Failed to return cancelled trade items: {ex.Message}");
            }
        }

        terminal.SetColor("yellow");
        terminal.WriteLine(Loc.Get("base.trade_cancelled"));
        await Task.Delay(1500);
    }

    private async Task SendTradePackage(SqlSaveBackend backend, string senderUsername)
    {
        // Check max outgoing
        int sentCount = backend.GetSentTradeOfferCount(senderUsername);
        if (sentCount >= 10)
        {
            terminal.SetColor("red");
            terminal.WriteLine(Loc.Get("base.trade_too_many"));
            await Task.Delay(2000);
            return;
        }

        terminal.WriteLine("");
        terminal.SetColor("cyan");
        terminal.Write(Loc.Get("base.send_to_prompt"));
        terminal.SetColor("white");
        string recipient = await terminal.ReadLineAsync();

        if (string.IsNullOrWhiteSpace(recipient)) return;

        // Resolve recipient display name (handles username or display name input)
        string? resolvedName = backend.ResolvePlayerDisplayName(recipient);
        if (resolvedName == null)
        {
            terminal.SetColor("red");
            terminal.WriteLine(Loc.Get("base.player_not_found", recipient));
            await Task.Delay(2000);
            return;
        }
        recipient = resolvedName;

        // Block self-trading
        if (recipient.Equals(currentPlayer.DisplayName, StringComparison.OrdinalIgnoreCase))
        {
            terminal.SetColor("red");
            terminal.WriteLine(Loc.Get("base.trade_no_self"));
            await Task.Delay(1500);
            return;
        }

        // Select items from inventory
        var selectedItems = new List<Item>();
        terminal.SetColor("cyan");
        terminal.WriteLine(Loc.Get("base.trade_select_items"));

        if (currentPlayer.Inventory.Count > 0)
        {
            for (int round = 0; round < 5; round++)
            {
                terminal.WriteLine("");
                terminal.SetColor("white");
                terminal.WriteLine(Loc.Get("base.trade_your_inventory"));
                var available = currentPlayer.Inventory.Where(i => !selectedItems.Contains(i)).ToList();
                for (int i = 0; i < available.Count; i++)
                {
                    terminal.WriteLine($"  {i + 1}. {available[i].Name} (value: {available[i].Value:N0}g)");
                }

                if (selectedItems.Count > 0)
                {
                    terminal.SetColor("bright_green");
                    terminal.WriteLine($"Selected: {string.Join(", ", selectedItems.Select(i => i.Name))}");
                }

                terminal.SetColor("cyan");
                terminal.Write(Loc.Get("base.trade_add_item"));
                terminal.SetColor("white");
                string itemInput = await terminal.ReadLineAsync();

                if (!int.TryParse(itemInput, out int itemIdx) || itemIdx == 0) break;
                if (itemIdx >= 1 && itemIdx <= available.Count)
                {
                    selectedItems.Add(available[itemIdx - 1]);
                    terminal.SetColor("bright_green");
                    terminal.WriteLine(Loc.Get("base.trade_added", available[itemIdx - 1].Name));
                }
            }
        }

        // Enter gold amount
        terminal.SetColor("cyan");
        terminal.Write(Loc.Get("base.trade_gold_prompt", currentPlayer.Gold.ToString("N0")));
        terminal.SetColor("white");
        string goldInput = await terminal.ReadLineAsync();
        long goldAmount = 0;
        if (long.TryParse(goldInput, out long parsed) && parsed > 0)
        {
            goldAmount = Math.Min(parsed, currentPlayer.Gold);
        }

        // Must send something
        if (selectedItems.Count == 0 && goldAmount == 0)
        {
            terminal.SetColor("yellow");
            terminal.WriteLine(Loc.Get("base.trade_empty"));
            await Task.Delay(1500);
            return;
        }

        // Optional note
        terminal.SetColor("cyan");
        terminal.Write(Loc.Get("base.trade_note"));
        terminal.SetColor("white");
        string note = await terminal.ReadLineAsync();
        if (note?.Length > 100) note = note.Substring(0, 100);

        // Confirm
        terminal.WriteLine("");
        terminal.SetColor("yellow");
        terminal.Write(Loc.Get("base.trade_send_prefix"));
        if (selectedItems.Count > 0)
            terminal.Write(Loc.Get("base.trade_items_count", selectedItems.Count));
        if (goldAmount > 0)
            terminal.Write(Loc.Get("base.trade_gold_amount", goldAmount.ToString("N0")));
        terminal.Write(Loc.Get("base.trade_to_confirm", recipient));
        terminal.SetColor("white");
        string confirm = await terminal.ReadLineAsync();

        if (confirm?.ToUpper().StartsWith("Y") != true)
        {
            terminal.SetColor("gray");
            terminal.WriteLine(Loc.Get("ui.cancelled"));
            await Task.Delay(1000);
            return;
        }

        // Remove items from inventory
        foreach (var item in selectedItems)
        {
            currentPlayer.Inventory.Remove(item);
        }

        // Deduct gold
        if (goldAmount > 0)
        {
            currentPlayer.Gold -= goldAmount;
        }

        // Serialize items
        string itemsJson = "[]";
        if (selectedItems.Count > 0)
        {
            var itemDataList = selectedItems.Select(i => new UsurperRemake.Systems.InventoryItemData
            {
                Name = i.Name,
                Type = i.Type,
                Value = i.Value,
                Attack = i.Attack,
                Armor = i.Armor,
                HP = i.HP,
                Mana = i.Mana,
                Strength = i.Strength,
                Defence = i.Defence,
                Dexterity = i.Dexterity
            }).ToList();
            itemsJson = System.Text.Json.JsonSerializer.Serialize(itemDataList,
                new System.Text.Json.JsonSerializerOptions { PropertyNamingPolicy = System.Text.Json.JsonNamingPolicy.CamelCase });
        }

        await backend.CreateTradeOffer(senderUsername, recipient.ToLower(), itemsJson, goldAmount, note ?? "");
        await backend.SendMessage(currentPlayer.DisplayName, recipient, "trade",
            $"{currentPlayer.DisplayName} sent you a package! Type /trade to view.");

        // Real-time notification in MUD mode
        UsurperRemake.Server.MudServer.Instance?.SendToPlayer(recipient,
            $"\u001b[93m  {currentPlayer.DisplayName} sent you a package! Type /trade to view.\u001b[0m");

        terminal.SetColor("bright_green");
        terminal.WriteLine(Loc.Get("base.trade_package_sent", recipient));
        await Task.Delay(1500);
    }

    // ========== Player Bounty System ==========

    protected async Task ShowBountyMenu()
    {
        var backend = SaveSystem.Instance.Backend as SqlSaveBackend;
        if (backend == null) return;

        while (true)
        {
            terminal.ClearScreen();
            WriteBoxHeader(Loc.Get("base.bounty_board"), "bright_red");
            terminal.WriteLine("");

            var bounties = await backend.GetActiveBounties(20);
            if (bounties.Count == 0)
            {
                terminal.SetColor("gray");
                terminal.WriteLine(Loc.Get("base.bounty_empty"));
            }
            else
            {
                terminal.SetColor("darkgray");
                terminal.WriteLine($"  {"#",-4} {"Target",-20} {"Bounty",-15} {"Posted By",-20}");
                if (!IsScreenReader)
                    terminal.WriteLine("  " + new string('─', 60));

                for (int i = 0; i < bounties.Count; i++)
                {
                    var b = bounties[i];
                    terminal.SetColor("bright_yellow");
                    terminal.Write($"  {i + 1,-4} ");
                    terminal.SetColor("white");
                    terminal.Write($"{b.TargetPlayer,-20} ");
                    terminal.SetColor("bright_green");
                    terminal.Write($"{b.Amount:N0} gold     ");
                    terminal.SetColor("gray");
                    terminal.WriteLine($"{b.PlacedBy,-20}");
                }
            }

            terminal.WriteLine("");
            terminal.SetColor("white");
            terminal.Write("  [");
            terminal.SetColor("bright_yellow");
            terminal.Write("P");
            terminal.SetColor("white");
            terminal.Write(Loc.Get("base.bounty_place_menu"));
            terminal.SetColor("bright_yellow");
            terminal.Write("M");
            terminal.SetColor("white");
            terminal.Write(Loc.Get("base.bounty_my_menu"));
            terminal.SetColor("bright_yellow");
            terminal.Write("Q");
            terminal.SetColor("white");
            terminal.WriteLine(Loc.Get("base.bounty_back_menu"));
            terminal.SetColor("white");
            terminal.Write(Loc.Get("base.bounty_choice"));
            string input = (await terminal.ReadLineAsync())?.Trim().ToUpper() ?? "";

            if (input == "Q" || input == "") break;

            if (input == "P")
            {
                await PlaceBounty(backend);
            }
            else if (input == "M")
            {
                await ShowMyBounties(backend);
            }
        }
    }

    private async Task PlaceBounty(SqlSaveBackend backend)
    {
        string username = currentPlayer.DisplayName.ToLower();

        // Check bounty limit (max 3 active per player)
        int activeCount = backend.GetActiveBountyCount(username);
        if (activeCount >= 3)
        {
            terminal.SetColor("red");
            terminal.WriteLine(Loc.Get("base.bounty_max"));
            await Task.Delay(2000);
            return;
        }

        terminal.SetColor("white");
        terminal.Write(Loc.Get("base.bounty_target_prompt"));
        string target = (await terminal.ReadLineAsync())?.Trim() ?? "";
        if (string.IsNullOrEmpty(target)) return;

        if (target.ToLower() == username)
        {
            terminal.SetColor("red");
            terminal.WriteLine(Loc.Get("base.bounty_no_self"));
            await Task.Delay(1500);
            return;
        }

        // Verify target exists
        var allPlayers = await backend.GetAllPlayerSummaries();
        var targetPlayer = allPlayers.FirstOrDefault(p => p.DisplayName.Equals(target, StringComparison.OrdinalIgnoreCase));
        if (targetPlayer == null)
        {
            terminal.SetColor("red");
            terminal.WriteLine(Loc.Get("base.bounty_not_found"));
            await Task.Delay(1500);
            return;
        }

        long minBounty = 500;
        terminal.SetColor("white");
        terminal.Write(Loc.Get("base.bounty_amount_prompt", minBounty.ToString("N0"), currentPlayer.Gold.ToString("N0")));
        string amountStr = (await terminal.ReadLineAsync())?.Trim() ?? "";
        if (!long.TryParse(amountStr, out long amount) || amount < minBounty)
        {
            terminal.SetColor("red");
            terminal.WriteLine(Loc.Get("base.bounty_min_amount", minBounty.ToString("N0")));
            await Task.Delay(1500);
            return;
        }

        if (amount > currentPlayer.Gold)
        {
            terminal.SetColor("red");
            terminal.WriteLine(Loc.Get("base.bounty_not_enough"));
            await Task.Delay(1500);
            return;
        }

        currentPlayer.Gold -= amount;
        await backend.PlaceBounty(username, target.ToLower(), amount);
        await backend.SendMessage(currentPlayer.DisplayName, target, "bounty",
            $"A bounty of {amount:N0} gold has been placed on your head!");
        UsurperRemake.Server.MudServer.Instance?.SendToPlayer(target,
            $"\u001b[91m  A bounty of {amount:N0} gold has been placed on your head!\u001b[0m");

        if (UsurperRemake.Systems.OnlineStateManager.IsActive)
            _ = UsurperRemake.Systems.OnlineStateManager.Instance!.AddNews($"{currentPlayer.DisplayName} placed a {amount:N0}g bounty on {targetPlayer.DisplayName}!", "bounty");

        terminal.SetColor("bright_green");
        terminal.WriteLine(Loc.Get("base.bounty_placed", amount.ToString("N0"), targetPlayer.DisplayName));
        await Task.Delay(2000);
    }

    private async Task ShowMyBounties(SqlSaveBackend backend)
    {
        string username = currentPlayer.DisplayName.ToLower();
        var myBounties = await backend.GetActiveBounties(50);
        var placed = myBounties.Where(b => b.PlacedBy.Equals(username, StringComparison.OrdinalIgnoreCase)).ToList();
        var onMe = await backend.GetBountiesOnPlayer(username);

        terminal.ClearScreen();
        terminal.SetColor("bright_yellow");
        terminal.WriteLine(Loc.Get("base.bounty_placed_header"));
        if (placed.Count == 0)
        {
            terminal.SetColor("gray");
            terminal.WriteLine(Loc.Get("base.bounty_none"));
        }
        else
        {
            foreach (var b in placed)
            {
                terminal.SetColor("white");
                terminal.WriteLine(Loc.Get("base.bounty_target_entry", b.TargetPlayer, b.Amount.ToString("N0")));
            }
        }

        terminal.SetColor("bright_red");
        terminal.WriteLine(Loc.Get("base.bounty_on_you_header"));
        if (onMe.Count == 0)
        {
            terminal.SetColor("gray");
            terminal.WriteLine(Loc.Get("base.bounty_clean"));
        }
        else
        {
            long total = onMe.Sum(b => b.Amount);
            terminal.SetColor("red");
            terminal.WriteLine(Loc.Get("base.bounty_total", onMe.Count, total.ToString("N0")));
            foreach (var b in onMe)
            {
                terminal.SetColor("white");
                terminal.WriteLine(Loc.Get("base.bounty_entry", b.Amount.ToString("N0"), b.PlacedBy));
            }
        }

        await terminal.PressAnyKey();
    }

    // ========== Auction House System ==========

    protected async Task ShowAuctionMenu()
    {
        var backend = SaveSystem.Instance.Backend as SqlSaveBackend;
        if (backend == null) return;

        // Clean up expired listings on entry
        await backend.CleanupExpiredAuctions();

        while (true)
        {
            terminal.ClearScreen();

            // Header
            WriteBoxHeader(Loc.Get("base.auction_house"), "bright_cyan");
            terminal.WriteLine("");

            // Get listing count for atmospheric text
            var listings = await backend.GetActiveAuctionListings(50);
            int totalListings = listings.Count;

            // Atmospheric description
            terminal.SetColor("white");
            terminal.Write(Loc.Get("base.auction_hall_desc"));
            terminal.WriteLine(Loc.Get("base.auction_hall_desc2"));
            terminal.Write(Loc.Get("base.auction_hall_desc3"));
            if (totalListings > 10)
            {
                terminal.SetColor("gray");
                terminal.WriteLine(Loc.Get("base.auction_busy"));
            }
            else if (totalListings > 0)
            {
                terminal.SetColor("gray");
                terminal.WriteLine(Loc.Get("base.auction_moderate"));
            }
            else
            {
                terminal.SetColor("gray");
                terminal.WriteLine(Loc.Get("base.auction_quiet"));
            }
            terminal.WriteLine("");

            // Auctioneer flavor
            terminal.SetColor("yellow");
            terminal.Write(Loc.Get("base.auction_grimjaw"));
            terminal.SetColor("gray");
            terminal.Write(Loc.Get("base.auction_grimjaw_desc"));
            if (totalListings == 0)
            {
                terminal.SetColor("white");
                terminal.WriteLine(Loc.Get("base.auction_yawns"));
                terminal.SetColor("yellow");
                terminal.WriteLine(Loc.Get("base.auction_slow_day"));
            }
            else if (totalListings < 5)
            {
                terminal.SetColor("white");
                terminal.WriteLine("");
                terminal.SetColor("yellow");
                terminal.WriteLine(Loc.Get("base.auction_few_things"));
            }
            else
            {
                terminal.SetColor("white");
                terminal.WriteLine("");
                terminal.SetColor("yellow");
                terminal.WriteLine(Loc.Get("base.auction_plenty"));
            }
            terminal.WriteLine("");

            // Listing summary
            if (totalListings > 0)
            {
                long totalValue = 0;
                foreach (var l in listings) totalValue += l.Price;
                terminal.SetColor("cyan");
                terminal.Write(Loc.Get("base.auction_listings"));
                terminal.SetColor("white");
                terminal.Write($"{totalListings}");
                terminal.SetColor("gray");
                terminal.Write(Loc.Get("base.auction_total_value"));
                terminal.SetColor("bright_yellow");
                terminal.WriteLine($"{totalValue:N0} {GameConfig.MoneyType}");
                terminal.WriteLine("");
            }

            // Show NPCs present at the Auction House
            var npcsHere = (NPCSpawnSystem.Instance.ActiveNPCs ?? new List<NPC>())
                .Where(npc => npc.IsAlive && !npc.IsDead &&
                       npc.CurrentLocation?.Equals("Auction House", StringComparison.OrdinalIgnoreCase) == true)
                .ToList();

            if (npcsHere.Count > 0)
            {
                terminal.SetColor("gray");
                terminal.Write(Loc.Get("base.auction_people"));
                for (int i = 0; i < npcsHere.Count && i < 8; i++)
                {
                    if (i > 0) terminal.Write(", ");
                    terminal.SetColor("cyan");
                    terminal.Write(npcsHere[i].Name2);
                }
                if (npcsHere.Count > 8)
                {
                    terminal.SetColor("gray");
                    terminal.Write(Loc.Get("base.auction_and_others", npcsHere.Count - 8));
                }
                terminal.SetColor("gray");
                terminal.WriteLine("");
                terminal.WriteLine("");
            }

            // Menu
            terminal.SetColor("cyan");
            terminal.WriteLine(Loc.Get("base.auction_what_do"));
            terminal.WriteLine("");

            // Row 1
            terminal.SetColor("darkgray");
            terminal.Write(" [");
            terminal.SetColor("bright_yellow");
            terminal.Write("B");
            terminal.SetColor("darkgray");
            terminal.Write("]");
            terminal.SetColor("white");
            terminal.Write(Loc.Get("base.auction_browse"));

            terminal.SetColor("darkgray");
            terminal.Write("[");
            terminal.SetColor("bright_yellow");
            terminal.Write("S");
            terminal.SetColor("darkgray");
            terminal.Write("]");
            terminal.SetColor("white");
            terminal.Write(Loc.Get("base.auction_sell"));

            terminal.SetColor("darkgray");
            terminal.Write("[");
            terminal.SetColor("bright_yellow");
            terminal.Write("M");
            terminal.SetColor("darkgray");
            terminal.Write("]");
            terminal.SetColor("white");
            terminal.WriteLine(Loc.Get("base.auction_my_listings"));

            // Row 2
            terminal.SetColor("darkgray");
            terminal.Write(" [");
            terminal.SetColor("bright_yellow");
            terminal.Write("R");
            terminal.SetColor("darkgray");
            terminal.Write("]");
            terminal.SetColor("white");
            terminal.Write(Loc.Get("base.auction_return"));

            if (npcsHere.Count > 0)
            {
                terminal.SetColor("darkgray");
                terminal.Write("[");
                terminal.SetColor("bright_yellow");
                terminal.Write("0");
                terminal.SetColor("darkgray");
                terminal.Write("]");
                terminal.SetColor("white");
                terminal.Write(Loc.Get("base.auction_talk", npcsHere.Count));
            }
            terminal.WriteLine("");
            terminal.WriteLine("");

            // Status line
            ShowStatusLine();

            terminal.SetColor("bright_white");
            string input = await GetChoice();
            input = input?.Trim().ToUpper() ?? "";

            if (input == "R" || input == "Q" || input == "") break;

            switch (input)
            {
                case "B": await BrowseAuctions(backend); break;
                case "S": await SellOnAuction(backend); break;
                case "M": await ShowMyAuctions(backend); break;
                case "0":
                    await TalkToNPCAtLocation("Auction House");
                    break;
                default:
                    // Try global commands (inventory, help, etc.)
                    var (handled, shouldExit) = await TryProcessGlobalCommand(input);
                    if (shouldExit) return;
                    break;
            }
        }
    }

    /// <summary>
    /// Talk to NPCs at a specific location string (for inline sub-menus like the online Auction House)
    /// </summary>
    private async Task TalkToNPCAtLocation(string locationString)
    {
        var npcsHere = (NPCSpawnSystem.Instance.ActiveNPCs ?? new List<NPC>())
            .Where(npc => npc.IsAlive && !npc.IsDead &&
                   npc.CurrentLocation?.Equals(locationString, StringComparison.OrdinalIgnoreCase) == true)
            .ToList();

        if (npcsHere.Count == 0)
        {
            terminal.SetColor("gray");
            terminal.WriteLine(Loc.Get("base.no_one_to_talk"));
            await Task.Delay(1500);
            return;
        }

        terminal.ClearScreen();
        WriteBoxHeader(Loc.Get("base.people_nearby"), "bright_cyan");
        terminal.WriteLine("");

        terminal.SetColor("yellow");
        terminal.WriteLine($"  {Loc.Get("base.who_talk_to")}");
        terminal.WriteLine("");

        for (int i = 0; i < npcsHere.Count; i++)
        {
            var npc = npcsHere[i];
            int relationLevel = RelationshipSystem.GetRelationshipStatus(currentPlayer, npc);
            var (relationColor, relationText, relationSymbol) = GetRelationshipDisplayInfo(relationLevel);

            terminal.SetColor("white");
            terminal.Write("  [");
            terminal.SetColor("bright_yellow");
            terminal.Write($"{i + 1}");
            terminal.SetColor("white");
            terminal.Write("] ");
            terminal.SetColor(relationColor);
            terminal.Write($"{npc.Name2}");
            terminal.SetColor("gray");
            terminal.Write($" - Level {npc.Level} {npc.ClassName}");
            terminal.Write(" [");
            terminal.SetColor(relationColor);
            terminal.Write(relationText);
            if (!string.IsNullOrEmpty(relationSymbol))
            {
                terminal.Write($" {relationSymbol}");
            }
            terminal.SetColor("gray");
            terminal.WriteLine("]");
        }

        terminal.SetColor("white");
        terminal.Write("\n  [");
        terminal.SetColor("bright_yellow");
        terminal.Write("0");
        terminal.SetColor("white");
        terminal.WriteLine($"] {Loc.Get("base.cancel")}");
        terminal.SetColor("white");
        string choice = await terminal.GetInput($"\n  {Loc.Get("base.talk_to_prompt")} ");
        if (!int.TryParse(choice, out int idx) || idx < 1 || idx > npcsHere.Count) return;

        await InteractWithNPC(npcsHere[idx - 1]);
    }

    private async Task BrowseAuctions(SqlSaveBackend backend)
    {
        var listings = await backend.GetActiveAuctionListings(50);
        if (listings.Count == 0)
        {
            terminal.SetColor("gray");
            terminal.WriteLine(Loc.Get("base.auction_no_items"));
            await Task.Delay(2000);
            return;
        }

        // Deserialize all items upfront for stats display
        var items = new Item?[listings.Count];
        for (int i = 0; i < listings.Count; i++)
        {
            try { items[i] = System.Text.Json.JsonSerializer.Deserialize<Item>(listings[i].ItemJson); }
            catch (Exception ex) { DebugLogger.Instance.LogError("LOCATION", $"[BrowseAuctions] Failed to deserialize auction item: {ex.Message}"); items[i] = null; }
        }

        terminal.ClearScreen();
        terminal.SetColor("bright_cyan");
        if (IsScreenReader)
            terminal.WriteLine(Loc.Get("base.auction_listings_header"));
        else
            terminal.WriteLine(Loc.Get("base.auction_listings_header_box"));
        terminal.SetColor("darkgray");
        string priceHeader = "Price".PadLeft(10);
        terminal.WriteLine($"  {"#",-4} {"Item",-24} {"Stats",-16} {priceHeader}   {"Seller",-14} {"Expires"}");
        if (!IsScreenReader)
            terminal.WriteLine("  " + new string('─', 74));

        string username = currentPlayer.DisplayName.ToLower();
        for (int i = 0; i < listings.Count; i++)
        {
            var l = listings[i];
            var item = items[i];
            bool isMine = l.Seller.Equals(username, StringComparison.OrdinalIgnoreCase);
            var timeLeft = l.ExpiresAt - DateTime.UtcNow;
            string expires = timeLeft.TotalHours > 1 ? $"{timeLeft.TotalHours:F0}h" : $"{timeLeft.TotalMinutes:F0}m";

            // Build compact stats string from item data
            string stats = GetItemStatsCompact(item);

            terminal.SetColor("bright_yellow");
            terminal.Write($"  {i + 1,-4} ");
            terminal.SetColor("white");
            terminal.Write($"{Truncate(l.ItemName, 23),-24} ");
            terminal.SetColor("cyan");
            terminal.Write($"{stats,-16} ");
            terminal.SetColor("bright_green");
            terminal.Write($"{l.Price:N0}g".PadLeft(10));
            terminal.Write("   ");
            terminal.SetColor(isMine ? "cyan" : "gray");
            terminal.Write($"{Truncate(l.Seller, 13),-14} ");
            terminal.SetColor("darkgray");
            terminal.WriteLine(expires);
        }

        terminal.SetColor("darkgray");
        terminal.WriteLine(Loc.Get("base.auction_inspect"));
        terminal.SetColor("white");
        terminal.Write(Loc.Get("base.auction_choice"));
        string input = (await terminal.ReadLineAsync())?.Trim() ?? "";
        if (!int.TryParse(input, out int choice) || choice < 1 || choice > listings.Count) return;

        var listing = listings[choice - 1];
        var selectedItem = items[choice - 1];

        // Show full item details before purchase
        await ShowAuctionItemDetails(listing, selectedItem, username, backend);
    }

    private static string Truncate(string s, int maxLen)
    {
        if (s.Length <= maxLen) return s;
        return s.Substring(0, maxLen - 1) + "…";
    }

    private static string GetItemStatsCompact(Item? item)
    {
        if (item == null) return "???";

        var parts = new List<string>();
        if (item.Attack > 0) parts.Add($"A:{item.Attack}");
        if (item.Armor > 0) parts.Add($"D:{item.Armor}");
        if (item.HP > 0) parts.Add($"HP:{item.HP}");
        if (item.Strength > 0) parts.Add($"S:{item.Strength}");
        if (item.Defence > 0) parts.Add($"Df:{item.Defence}");
        if (item.Mana > 0) parts.Add($"M:{item.Mana}");

        if (parts.Count == 0)
        {
            // Consumable/misc items
            string typeName = GetItemTypeName(item.Type);
            return typeName;
        }

        return string.Join(" ", parts);
    }

    private static string GetItemTypeName(ObjType type)
    {
        return type switch
        {
            ObjType.Weapon => "Weapon",
            ObjType.Head => "Helm",
            ObjType.Body => "Armor",
            ObjType.Arms => "Arms",
            ObjType.Hands => "Gloves",
            ObjType.Fingers => "Ring",
            ObjType.Legs => "Legs",
            ObjType.Feet => "Boots",
            ObjType.Waist => "Belt",
            ObjType.Neck => "Necklace",
            ObjType.Face => "Face",
            ObjType.Shield => "Shield",
            ObjType.Abody => "Cloak",
            ObjType.Food => "Food",
            ObjType.Drink => "Drink",
            ObjType.Magic => "Magic",
            ObjType.Potion => "Potion",
            _ => "Item"
        };
    }

    private async Task ShowAuctionItemDetails(AuctionListing listing, Item? item, string username, SqlSaveBackend backend)
    {
        terminal.ClearScreen();
        terminal.SetColor("bright_cyan");
        if (IsScreenReader)
            terminal.WriteLine(Loc.Get("base.auction_item_header"));
        else
            terminal.WriteLine(Loc.Get("base.auction_item_header_box"));
        terminal.SetColor("white");
        terminal.WriteLine($"\n  {listing.ItemName}");

        if (item != null)
        {
            terminal.SetColor("darkgray");
            terminal.WriteLine(Loc.Get("base.auction_type", GetItemTypeName(item.Type), item.Value.ToString("N0")));

            // Stat bonuses
            terminal.SetColor("bright_cyan");
            terminal.WriteLine("");
            var statLines = new List<(string label, int value)>();
            if (item.Attack != 0) statLines.Add(("Attack", item.Attack));
            if (item.Armor != 0) statLines.Add(("Armor", item.Armor));
            if (item.HP != 0) statLines.Add(("HP", item.HP));
            if (item.Strength != 0) statLines.Add(("Strength", item.Strength));
            if (item.Defence != 0) statLines.Add(("Defence", item.Defence));
            if (item.Stamina != 0) statLines.Add(("Stamina", item.Stamina));
            if (item.Agility != 0) statLines.Add(("Agility", item.Agility));
            if (item.Dexterity != 0) statLines.Add(("Dexterity", item.Dexterity));
            if (item.Wisdom != 0) statLines.Add(("Wisdom", item.Wisdom));
            if (item.Charisma != 0) statLines.Add(("Charisma", item.Charisma));
            if (item.Mana != 0) statLines.Add(("Mana", item.Mana));

            if (statLines.Count > 0)
            {
                foreach (var (label, value) in statLines)
                {
                    terminal.SetColor(value > 0 ? "bright_green" : "red");
                    terminal.WriteLine($"  {label,-12} {(value > 0 ? "+" : "")}{value}");
                }
            }
            else
            {
                terminal.SetColor("gray");
                terminal.WriteLine(Loc.Get("base.auction_no_stats"));
            }

            // Requirements and flags
            terminal.SetColor("darkgray");
            terminal.WriteLine("");
            if (item.MinLevel > 0)
                terminal.WriteLine(Loc.Get("base.auction_requires_level", item.MinLevel));
            if (item.StrengthNeeded > 0)
                terminal.WriteLine(Loc.Get("base.auction_requires_str", item.StrengthNeeded));
            if (item.RequiresGood || item.OnlyForGood)
            {
                terminal.SetColor("bright_yellow");
                terminal.WriteLine(Loc.Get("base.auction_req_good"));
            }
            if (item.RequiresEvil || item.OnlyForEvil)
            {
                terminal.SetColor("bright_red");
                terminal.WriteLine(Loc.Get("base.auction_req_evil"));
            }
            if (item.Cursed || item.IsCursed)
            {
                terminal.SetColor("red");
                terminal.WriteLine(Loc.Get("base.auction_cursed"));
            }
        }
        else
        {
            terminal.SetColor("gray");
            terminal.WriteLine(Loc.Get("base.auction_unavailable"));
        }

        // Sale info
        terminal.SetColor("darkgray");
        terminal.WriteLine("");
        var timeLeft = listing.ExpiresAt - DateTime.UtcNow;
        string expires = timeLeft.TotalHours > 1 ? $"{timeLeft.TotalHours:F0} hours" : $"{timeLeft.TotalMinutes:F0} minutes";
        terminal.WriteLine($"  Seller: {listing.Seller}    Expires in: {expires}");

        terminal.SetColor("bright_green");
        terminal.WriteLine($"\n  Price: {listing.Price:N0} gold");
        terminal.SetColor("darkgray");
        terminal.WriteLine($"  Your gold: {currentPlayer.Gold:N0}");

        // Purchase flow
        bool isMine = listing.Seller.Equals(username, StringComparison.OrdinalIgnoreCase);
        if (isMine)
        {
            terminal.SetColor("cyan");
            terminal.WriteLine(Loc.Get("base.auction_your_listing"));
            terminal.Write(Loc.Get("base.auction_press_enter"));
            await terminal.ReadLineAsync();
            return;
        }

        // Check level requirement (both stored MinLevel and power-based floor)
        if (item != null)
        {
            int powerLevel = Math.Max(item.Attack, item.Armor);
            int requiredLevel = item.MinLevel;
            if (powerLevel > 15)
                requiredLevel = Math.Max(requiredLevel, Math.Min(100, powerLevel / 10));
            if (currentPlayer.Level < requiredLevel)
            {
                terminal.SetColor("red");
                terminal.WriteLine(Loc.Get("base.auction_req_level", requiredLevel, currentPlayer.Level));
                terminal.Write(Loc.Get("base.auction_press_enter"));
                await terminal.ReadLineAsync();
                return;
            }
        }

        if (currentPlayer.Gold < listing.Price)
        {
            terminal.SetColor("red");
            terminal.WriteLine(Loc.Get("base.auction_need_more", (listing.Price - currentPlayer.Gold).ToString("N0")));
            terminal.Write(Loc.Get("base.auction_press_enter"));
            await terminal.ReadLineAsync();
            return;
        }

        terminal.SetColor("yellow");
        terminal.Write(Loc.Get("base.auction_buy_confirm", listing.Price.ToString("N0")));
        string confirm = (await terminal.ReadLineAsync())?.Trim().ToUpper() ?? "";
        if (confirm != "Y") return;

        bool success = await backend.BuyAuctionListing(listing.Id, username);
        if (!success)
        {
            terminal.SetColor("red");
            terminal.WriteLine(Loc.Get("base.auction_already_sold"));
            await Task.Delay(1500);
            return;
        }

        // v0.57.1 — deserialize FIRST, before deducting gold or marking the buyer's side of the
        // transaction complete. If the JSON is corrupt, refund by unmarking the auction and
        // returning early, so the buyer doesn't pay for an item they never receive.
        Item? purchasedItem = null;
        try
        {
            purchasedItem = System.Text.Json.JsonSerializer.Deserialize<Item>(listing.ItemJson);
        }
        catch (Exception ex)
        {
            DebugLogger.Instance.LogError("LOCATION", $"[ShowAuctionItemDetails] Auction item JSON corrupt: {ex.Message}");
        }

        if (purchasedItem == null)
        {
            // Refund path — un-sell the listing so the item isn't orphaned, buyer keeps gold.
            await backend.RefundAuctionListing(listing.Id);
            terminal.SetColor("yellow");
            terminal.WriteLine(Loc.Get("base.auction_purchased_error"));
            await Task.Delay(1500);
            return;
        }

        // Deduct gold from buyer (seller collects from My Listings)
        currentPlayer.Gold -= listing.Price;
        currentPlayer.Inventory.Add(purchasedItem);
        terminal.SetColor("bright_green");
        terminal.WriteLine(Loc.Get("base.auction_purchased", listing.ItemName, listing.Price.ToString("N0")));

        // Force-save so the freshly-added item persists even if the buyer disconnects immediately.
        try
        {
            SaveSystem.Instance.ResetAutoSaveThrottle();
            await SaveSystem.Instance.AutoSave(currentPlayer);
        }
        catch (Exception ex) { DebugLogger.Instance.LogError("LOCATION", $"[ShowAuctionItemDetails] Post-purchase save failed: {ex.Message}"); }

        await backend.SendMessage("Auction House", listing.Seller, "auction",
            $"Your {listing.ItemName} sold for {listing.Price:N0} gold! Visit the Auction House to collect.");
        UsurperRemake.Server.MudServer.Instance?.SendToPlayer(listing.Seller,
            $"\u001b[93m  [Auction] Your {listing.ItemName} sold for {listing.Price:N0} gold! Visit the Auction House to collect.\u001b[0m");

        await Task.Delay(2000);
    }

    private static (long fee, int basePct, int taxPct) CalculateAuctionFee(long price, int durationHours)
    {
        int basePct = durationHours switch
        {
            12 => 5,
            24 => 4,
            48 => 3,
            72 => 2,
            _ => 3
        };
        var king = CastleLocation.GetCurrentKing();
        int taxPct = king?.KingTaxPercent ?? 0;
        long fee = Math.Max(1, (price * (basePct + taxPct)) / 100);
        return (fee, basePct, taxPct);
    }

    private static readonly (int hours, string label)[] AuctionDurations =
    {
        (12, "12 hours"),
        (24, "24 hours"),
        (48, "48 hours"),
        (72, "72 hours")
    };

    private async Task SellOnAuction(SqlSaveBackend backend)
    {
        if (currentPlayer.Inventory.Count == 0)
        {
            terminal.SetColor("gray");
            terminal.WriteLine(Loc.Get("base.auction_no_items_sell"));
            await Task.Delay(1500);
            return;
        }

        // Check listing limit (max 5 active)
        var myListings = await backend.GetMyAuctionListings(currentPlayer.DisplayName.ToLower());
        int activeCount = myListings.Count(l => l.Status == "active");
        if (activeCount >= 5)
        {
            terminal.SetColor("red");
            terminal.WriteLine(Loc.Get("base.auction_max_listings"));
            await Task.Delay(2000);
            return;
        }

        terminal.ClearScreen();
        terminal.SetColor("bright_cyan");
        if (IsScreenReader)
            terminal.WriteLine(Loc.Get("base.auction_inv_header"));
        else
            terminal.WriteLine(Loc.Get("base.auction_inv_header_box"));
        for (int i = 0; i < currentPlayer.Inventory.Count; i++)
        {
            terminal.SetColor("bright_yellow");
            terminal.Write($"  {i + 1,-4} ");
            terminal.SetColor("white");
            terminal.WriteLine(currentPlayer.Inventory[i].Name);
        }

        terminal.SetColor("white");
        terminal.Write(Loc.Get("base.auction_item_num"));
        string input = (await terminal.ReadLineAsync())?.Trim() ?? "";
        if (!int.TryParse(input, out int choice) || choice < 1 || choice > currentPlayer.Inventory.Count) return;

        var item = currentPlayer.Inventory[choice - 1];

        terminal.Write(Loc.Get("base.auction_asking_price"));
        string priceStr = (await terminal.ReadLineAsync())?.Trim() ?? "";
        if (!long.TryParse(priceStr, out long price) || price < 1)
        {
            terminal.SetColor("red");
            terminal.WriteLine(Loc.Get("base.auction_invalid_price"));
            await Task.Delay(1500);
            return;
        }

        // Duration selection with fee display
        terminal.SetColor("bright_cyan");
        terminal.WriteLine(Loc.Get("base.auction_duration"));
        for (int i = 0; i < AuctionDurations.Length; i++)
        {
            var (hours, label) = AuctionDurations[i];
            var (fee, basePct, taxPct) = CalculateAuctionFee(price, hours);
            terminal.SetColor("white");
            terminal.Write("  [");
            terminal.SetColor("bright_yellow");
            terminal.Write($"{i + 1}");
            terminal.SetColor("white");
            terminal.Write($"] {label,-12}");
            terminal.SetColor("gray");
            terminal.Write($"  {Loc.Get("base.fee_label")} ");
            terminal.SetColor("white");
            terminal.Write($"{fee:N0}g");
            terminal.SetColor("darkgray");
            terminal.WriteLine($"  ({basePct}% base + {taxPct}% tax)");
        }

        terminal.SetColor("white");
        terminal.Write(Loc.Get("base.auction_duration_prompt"));
        string durInput = (await terminal.ReadLineAsync())?.Trim() ?? "";
        if (!int.TryParse(durInput, out int durChoice) || durChoice < 1 || durChoice > AuctionDurations.Length) return;

        int chosenHours = AuctionDurations[durChoice - 1].hours;
        string chosenLabel = AuctionDurations[durChoice - 1].label;
        var (listingFee, _, _) = CalculateAuctionFee(price, chosenHours);

        // Check player can afford the fee
        if (currentPlayer.Gold < listingFee)
        {
            terminal.SetColor("red");
            terminal.WriteLine(Loc.Get("base.auction_need_fee", listingFee.ToString("N0"), currentPlayer.Gold.ToString("N0")));
            await Task.Delay(2000);
            return;
        }

        // Confirm
        terminal.SetColor("yellow");
        terminal.Write(Loc.Get("base.auction_list_confirm", item.Name, price.ToString("N0"), chosenLabel, listingFee.ToString("N0")));
        string confirm = (await terminal.ReadLineAsync())?.Trim().ToUpper() ?? "";
        if (confirm != "Y") return;

        string itemJson = System.Text.Json.JsonSerializer.Serialize(item);
        int id = await backend.CreateAuctionListing(currentPlayer.DisplayName.ToLower(), item.Name, itemJson, price, chosenHours);
        if (id > 0)
        {
            currentPlayer.Inventory.RemoveAt(choice - 1);

            // Deduct listing fee and route through tax system
            currentPlayer.Gold -= listingFee;
            CityControlSystem.Instance.ProcessSaleTax(listingFee);

            terminal.SetColor("bright_green");
            terminal.WriteLine(Loc.Get("base.auction_listed", item.Name, price.ToString("N0"), listingFee.ToString("N0"), chosenLabel));

            // Global announcement
            UsurperRemake.Server.MudServer.Instance?.BroadcastToAll(
                $"\u001b[93m  [Auction] {currentPlayer.DisplayName} just listed {item.Name} for {price:N0} gold! ({chosenLabel})\u001b[0m",
                excludeUsername: currentPlayer.DisplayName);
        }
        else
        {
            terminal.SetColor("red");
            terminal.WriteLine(Loc.Get("base.auction_failed"));
        }
        await Task.Delay(2000);
    }

    private async Task ShowMyAuctions(SqlSaveBackend backend)
    {
        var listings = await backend.GetMyAuctionListings(currentPlayer.DisplayName.ToLower());
        terminal.ClearScreen();
        terminal.SetColor("bright_cyan");
        if (IsScreenReader)
            terminal.WriteLine(Loc.Get("base.auction_my_header"));
        else
            terminal.WriteLine(Loc.Get("base.auction_my_header_box"));

        if (listings.Count == 0)
        {
            terminal.SetColor("gray");
            terminal.WriteLine(Loc.Get("base.auction_no_listings"));
            await terminal.PressAnyKey();
            return;
        }

        // Calculate uncollected gold and expired items
        long uncollectedGold = 0;
        var soldUncollected = new List<int>(); // indices of sold+uncollected listings
        var expiredUncollected = new List<int>(); // indices of expired listings
        for (int i = 0; i < listings.Count; i++)
        {
            var l = listings[i];
            if (l.Status == "sold" && !l.GoldCollected)
            {
                uncollectedGold += l.Price;
                soldUncollected.Add(i);
            }
            else if (l.Status == "expired")
            {
                expiredUncollected.Add(i);
            }
        }

        for (int i = 0; i < listings.Count; i++)
        {
            var l = listings[i];
            string statusText = l.Status.ToUpper();
            string statusColor = l.Status switch
            {
                "active" => "bright_green",
                "sold" => l.GoldCollected ? "gray" : "bright_yellow",
                "expired" => "bright_red",
                "collected" => "gray",
                "cancelled" => "gray",
                _ => "white"
            };

            if (l.Status == "sold" && !l.GoldCollected)
                statusText = Loc.Get("base.auction_sold_uncollected");
            else if (l.Status == "sold" && l.GoldCollected)
                statusText = Loc.Get("base.auction_sold_collected");
            else if (l.Status == "expired")
                statusText = Loc.Get("base.auction_expired_collect");
            else if (l.Status == "collected")
                statusText = Loc.Get("base.auction_expired_collected");

            terminal.SetColor("bright_yellow");
            terminal.Write($"  {i + 1,-4} ");
            terminal.SetColor("white");
            terminal.Write($"{Truncate(l.ItemName, 23),-24} ");
            terminal.SetColor("bright_green");
            terminal.Write($"{l.Price:N0}g".PadLeft(10));
            terminal.Write("  ");
            terminal.SetColor(statusColor);
            terminal.WriteLine($"[{statusText}]");
        }

        // Show uncollected gold summary
        if (uncollectedGold > 0)
        {
            terminal.SetColor("bright_yellow");
            terminal.WriteLine(Loc.Get("base.auction_gold_awaiting", uncollectedGold.ToString("N0"), soldUncollected.Count, soldUncollected.Count != 1 ? "s" : ""));
        }

        // Show expired items summary
        if (expiredUncollected.Count > 0)
        {
            terminal.SetColor("red");
            terminal.WriteLine(Loc.Get("base.auction_expired_count", expiredUncollected.Count, expiredUncollected.Count != 1 ? "s" : ""));
        }

        terminal.SetColor("white");
        terminal.WriteLine("");
        terminal.Write("  ");
        if (uncollectedGold > 0)
        {
            terminal.Write("[");
            terminal.SetColor("bright_yellow");
            terminal.Write("C");
            terminal.SetColor("white");
            terminal.Write(Loc.Get("base.auction_collect_gold"));
        }
        if (expiredUncollected.Count > 0)
        {
            terminal.Write("[");
            terminal.SetColor("bright_yellow");
            terminal.Write("E");
            terminal.SetColor("white");
            terminal.Write(Loc.Get("base.auction_collect_expired"));
        }
        terminal.Write("[");
        terminal.SetColor("bright_yellow");
        terminal.Write("#");
        terminal.SetColor("white");
        terminal.Write(Loc.Get("base.auction_cancel_collect"));
        terminal.WriteLine("");

        terminal.Write(Loc.Get("base.auction_my_choice"));
        string input = (await terminal.ReadLineAsync())?.Trim().ToUpper() ?? "";
        if (string.IsNullOrEmpty(input)) return;

        // Collect all gold
        if (input == "C" && uncollectedGold > 0)
        {
            long totalCollected = 0;
            foreach (int idx in soldUncollected)
            {
                var l = listings[idx];
                bool collected = await backend.CollectAuctionGold(l.Id, currentPlayer.DisplayName.ToLower());
                if (collected)
                    totalCollected += l.Price;
            }
            if (totalCollected > 0)
            {
                currentPlayer.Gold += totalCollected;
                terminal.SetColor("bright_green");
                terminal.WriteLine(Loc.Get("base.auction_gold_collected", totalCollected.ToString("N0")));
            }
            else
            {
                terminal.SetColor("gray");
                terminal.WriteLine(Loc.Get("base.auction_no_gold"));
            }
            await Task.Delay(2000);
            return;
        }

        // Collect all expired items
        if (input == "E" && expiredUncollected.Count > 0)
        {
            int collected = 0;
            foreach (int idx in expiredUncollected)
            {
                var l = listings[idx];
                bool ok = await backend.CollectExpiredAuctionListing(l.Id, currentPlayer.DisplayName.ToLower());
                if (ok)
                {
                    try
                    {
                        var item = System.Text.Json.JsonSerializer.Deserialize<Item>(l.ItemJson);
                        if (item != null) { currentPlayer.Inventory.Add(item); collected++; }
                    }
                    catch (Exception ex) { DebugLogger.Instance.LogError("LOCATION", $"[ShowMyAuctions] Failed to deserialize expired auction item: {ex.Message}"); }
                }
            }
            if (collected > 0)
            {
                terminal.SetColor("bright_green");
                terminal.WriteLine(Loc.Get("base.auction_items_collected", collected));
            }
            else
            {
                terminal.SetColor("gray");
                terminal.WriteLine(Loc.Get("base.auction_no_expired"));
            }
            await Task.Delay(2000);
            return;
        }

        // Select a listing by number
        if (!int.TryParse(input, out int choice) || choice < 1 || choice > listings.Count) return;

        var listing = listings[choice - 1];

        // Collect expired item by number
        if (listing.Status == "expired")
        {
            bool ok = await backend.CollectExpiredAuctionListing(listing.Id, currentPlayer.DisplayName.ToLower());
            if (ok)
            {
                try
                {
                    var item = System.Text.Json.JsonSerializer.Deserialize<Item>(listing.ItemJson);
                    if (item != null) currentPlayer.Inventory.Add(item);
                }
                catch (Exception ex) { DebugLogger.Instance.LogError("LOCATION", $"[ShowMyAuctions] Failed to deserialize collected auction item: {ex.Message}"); }
                terminal.SetColor("bright_green");
                terminal.WriteLine(Loc.Get("base.auction_collected_back", listing.ItemName));
            }
            else
            {
                terminal.SetColor("red");
                terminal.WriteLine(Loc.Get("base.auction_collect_failed"));
            }
            await Task.Delay(1500);
            return;
        }

        if (listing.Status != "active")
        {
            terminal.SetColor("red");
            terminal.WriteLine(Loc.Get("base.auction_only_active"));
            await Task.Delay(1500);
            return;
        }

        bool cancelled = await backend.CancelAuctionListing(listing.Id, currentPlayer.DisplayName.ToLower());
        if (cancelled)
        {
            // Return item to inventory
            try
            {
                var item = System.Text.Json.JsonSerializer.Deserialize<Item>(listing.ItemJson);
                if (item != null) currentPlayer.Inventory.Add(item);
            }
            catch (Exception ex) { DebugLogger.Instance.LogError("LOCATION", $"[ShowMyAuctions] Failed to deserialize cancelled auction item: {ex.Message}"); }

            terminal.SetColor("bright_green");
            terminal.WriteLine(Loc.Get("base.auction_listing_cancelled"));
        }
        await Task.Delay(1500);
    }

    /// <summary>
    /// Returns true for slash commands that produce multi-line output and need
    /// a "press any key" pause before the location menu redraws.
    /// Chat/action commands (say, tell, emote, etc.) return false.
    /// </summary>
    private static bool IsInfoDisplayCommand(string cmd)
    {
        return cmd switch
        {
            "who" or "w" => true,
            "stat" => true,
            "wizhelp" => true,
            "wizwho" => true,
            "where" => true,
            "wizlog" => true,
            "holylight" => true,
            "help" => true,
            _ => false
        };
    }

    /// <summary>
    /// Write compact stat summary for an equipment item (shared by all equip screens).
    /// Example: [WP:45 STR:+3 DEX:+2 CRIT:5%]
    /// </summary>
    protected void WriteEquipmentStatSummary(Equipment item)
    {
        var stats = new List<string>();
        if (item.WeaponPower > 0) stats.Add($"{Loc.Get("ui.stat_wp")}:{item.WeaponPower}");
        if (item.ArmorClass > 0) stats.Add($"{Loc.Get("ui.stat_ac")}:{item.ArmorClass}");
        if (item.ShieldBonus > 0) stats.Add($"{Loc.Get("ui.stat_block")}:{item.ShieldBonus}");
        if (item.DefenceBonus != 0) stats.Add($"{Loc.Get("ui.stat_def")}:{item.DefenceBonus:+#;-#}");
        if (item.StrengthBonus != 0) stats.Add($"{Loc.Get("ui.stat_str")}:{item.StrengthBonus:+#;-#}");
        if (item.DexterityBonus != 0) stats.Add($"{Loc.Get("ui.stat_dex")}:{item.DexterityBonus:+#;-#}");
        if (item.AgilityBonus != 0) stats.Add($"{Loc.Get("ui.stat_agi")}:{item.AgilityBonus:+#;-#}");
        if (item.ConstitutionBonus != 0) stats.Add($"{Loc.Get("ui.stat_con")}:{item.ConstitutionBonus:+#;-#}");
        if (item.IntelligenceBonus != 0) stats.Add($"{Loc.Get("ui.stat_int")}:{item.IntelligenceBonus:+#;-#}");
        if (item.WisdomBonus != 0) stats.Add($"{Loc.Get("ui.stat_wis")}:{item.WisdomBonus:+#;-#}");
        if (item.CharismaBonus != 0) stats.Add($"{Loc.Get("ui.stat_cha")}:{item.CharismaBonus:+#;-#}");
        if (item.MaxHPBonus != 0) stats.Add($"{Loc.Get("ui.stat_hp")}:{item.MaxHPBonus:+#;-#}");
        if (item.MaxManaBonus != 0) stats.Add($"{Loc.Get("ui.stat_mp")}:{item.MaxManaBonus:+#;-#}");
        if (item.CriticalChanceBonus > 0) stats.Add($"{Loc.Get("ui.stat_crit")}:{item.CriticalChanceBonus}%");
        if (item.LifeSteal > 0) stats.Add($"{Loc.Get("ui.stat_leech")}:{item.LifeSteal}%");
        if (item.MagicResistance > 0) stats.Add($"{Loc.Get("ui.stat_mr")}:{item.MagicResistance}%");
        if (item.PoisonDamage > 0) stats.Add($"{Loc.Get("ui.stat_psn")}:{item.PoisonDamage}");

        if (stats.Count > 0)
        {
            terminal.SetColor("darkgray");
            terminal.Write($" [{string.Join(" ", stats)}]");
        }
    }

    /// <summary>
    /// Display an equipment slot with its current item and stat summary (shared by equip screens).
    /// </summary>
    protected void DisplayEquipmentSlotWithStats(Character target, EquipmentSlot slot, string label)
    {
        var item = target.GetEquipment(slot);
        terminal.SetColor("gray");
        terminal.Write($"  {label,-12}: ");
        if (item != null)
        {
            if (!item.IsIdentified)
            {
                terminal.SetColor("magenta");
                terminal.WriteLine($"Unidentified {slot.GetDisplayName()}");
            }
            else
            {
                terminal.SetColor(item.GetRarityColor());
                terminal.Write(item.Name);
                WriteEquipmentStatSummary(item);
                terminal.WriteLine("");
            }
        }
        else
        {
            // Check if off-hand is empty because of a two-handed weapon
            if (slot == EquipmentSlot.OffHand)
            {
                var mainHand = target.GetEquipment(EquipmentSlot.MainHand);
                if (mainHand?.Handedness == WeaponHandedness.TwoHanded)
                {
                    terminal.SetColor("darkgray");
                    terminal.WriteLine(Loc.Get("base.equip_using_2h"));
                    return;
                }
            }
            terminal.SetColor("darkgray");
            terminal.WriteLine(Loc.Get("base.equip_empty"));
        }
    }

    /// <summary>
    /// Slot picker for equipment management. Returns selected slot, or null if cancelled.
    /// Shows current equipment in each slot for context.
    /// </summary>
    protected async Task<EquipmentSlot?> PromptForEquipmentSlot(Character target)
    {
        var slots = new (EquipmentSlot slot, string label)[]
        {
            (EquipmentSlot.MainHand, "Main Hand"),
            (EquipmentSlot.OffHand, "Off Hand"),
            (EquipmentSlot.Head, "Head"),
            (EquipmentSlot.Body, "Body"),
            (EquipmentSlot.Arms, "Arms"),
            (EquipmentSlot.Hands, "Hands"),
            (EquipmentSlot.Legs, "Legs"),
            (EquipmentSlot.Feet, "Feet"),
            (EquipmentSlot.Waist, "Waist"),
            (EquipmentSlot.Face, "Face"),
            (EquipmentSlot.Cloak, "Cloak"),
            (EquipmentSlot.Neck, "Neck"),
            (EquipmentSlot.LFinger, "Left Ring"),
            (EquipmentSlot.RFinger, "Right Ring"),
        };

        terminal.SetColor("bright_yellow");
        terminal.WriteLine($"  {Loc.Get("base.choose_slot")}");
        terminal.WriteLine("");

        // Two-column layout: 1-7 left, 8-14 right
        for (int row = 0; row < 7; row++)
        {
            // Left column
            int li = row;
            var (lSlot, lLabel) = slots[li];
            var lItem = target.GetEquipment(lSlot);
            terminal.SetColor("bright_yellow");
            terminal.Write($"  {li + 1,2}. ");
            terminal.SetColor("white");
            terminal.Write($"{lLabel,-12}");
            if (lItem != null)
            {
                terminal.SetColor("gray");
                terminal.Write($"{(lItem.IsIdentified ? lItem.Name : "???"),-20}");
            }
            else
            {
                terminal.SetColor("darkgray");
                terminal.Write($"{"---",-20}");
            }

            // Right column
            int ri = row + 7;
            var (rSlot, rLabel) = slots[ri];
            var rItem = target.GetEquipment(rSlot);
            terminal.SetColor("bright_yellow");
            terminal.Write($" {ri + 1,2}. ");
            terminal.SetColor("white");
            terminal.Write($"{rLabel,-12}");
            if (rItem != null)
            {
                terminal.SetColor("gray");
                terminal.Write(rItem.IsIdentified ? rItem.Name : "???");
            }
            else
            {
                terminal.SetColor("darkgray");
                terminal.Write("---");
            }
            terminal.WriteLine("");
        }

        terminal.WriteLine("");
        terminal.SetColor("cyan");
        terminal.Write($"  {Loc.Get("base.slot_prompt")} ");
        terminal.SetColor("white");

        var input = (await terminal.ReadLineAsync()).Trim().ToUpper();
        if (input == "Q" || string.IsNullOrEmpty(input))
            return null;

        if (int.TryParse(input, out int slotIdx) && slotIdx >= 1 && slotIdx <= 14)
            return slots[slotIdx - 1].slot;

        return null;
    }

    /// <summary>
    /// Show items from player inventory/equipment that match a specific slot, with full stats.
    /// Used by slot-based equip flow. Returns list of matching items.
    /// </summary>
    protected List<(Equipment item, bool isEquipped, EquipmentSlot? fromSlot)> GetItemsForSlot(
        EquipmentSlot targetSlot)
    {
        var items = new List<(Equipment item, bool isEquipped, EquipmentSlot? fromSlot)>();

        // Add matching items from player's inventory
        foreach (var invItem in currentPlayer.Inventory)
        {
            var equipment = ConvertInventoryItemToEquipment(invItem);
            if (equipment == null) continue;

            if (ItemMatchesSlot(equipment, targetSlot))
                items.Add((equipment, false, null));
        }

        // Add matching items from player's equipped items
        foreach (EquipmentSlot slot in Enum.GetValues(typeof(EquipmentSlot)))
        {
            if (slot == EquipmentSlot.None) continue;
            var equipped = currentPlayer.GetEquipment(slot);
            if (equipped == null) continue;

            if (ItemMatchesSlot(equipped, targetSlot))
                items.Add((equipped, true, slot));
        }

        return items;
    }

    /// <summary>
    /// Check if an equipment item can go in the specified slot.
    /// Handles weapons (MainHand/OffHand), rings (LFinger/RFinger), and exact slot matches.
    /// </summary>
    private static bool ItemMatchesSlot(Equipment item, EquipmentSlot targetSlot)
    {
        // Weapons can go in MainHand; one-handed weapons can also go in OffHand
        if (targetSlot == EquipmentSlot.MainHand)
            return item.Slot == EquipmentSlot.MainHand;

        if (targetSlot == EquipmentSlot.OffHand)
        {
            // Shields always go to off-hand
            if (item.Slot == EquipmentSlot.OffHand) return true;
            // One-handed weapons can go to off-hand (dual wield)
            if (item.Slot == EquipmentSlot.MainHand && item.Handedness == WeaponHandedness.OneHanded)
                return true;
            return false;
        }

        // Rings can go in either finger slot
        if (targetSlot == EquipmentSlot.LFinger || targetSlot == EquipmentSlot.RFinger)
            return item.Slot == EquipmentSlot.LFinger || item.Slot == EquipmentSlot.RFinger;

        // Exact match for all other slots
        return item.Slot == targetSlot;
    }

    /// <summary>
    /// Display a list of equipment items with full stat summaries and numbering.
    /// Returns the displayed items for selection. Handles unidentified items.
    /// </summary>
    protected void DisplayEquipmentItemList(
        List<(Equipment item, bool isEquipped, EquipmentSlot? fromSlot)> items,
        Character target)
    {
        for (int i = 0; i < items.Count; i++)
        {
            var (item, isEquipped, fromSlot) = items[i];
            terminal.SetColor("bright_yellow");
            terminal.Write($"  {i + 1}. ");

            if (!item.IsIdentified)
            {
                terminal.SetColor("magenta");
                terminal.Write($"Unidentified {item.Slot.GetDisplayName()} ");
            }
            else
            {
                terminal.SetColor(item.GetRarityColor());
                terminal.Write(item.Name);
                WriteEquipmentStatSummary(item);
            }

            // Show if currently equipped by player
            if (isEquipped)
            {
                terminal.SetColor("cyan");
                terminal.Write($" (your {fromSlot?.GetDisplayName()})");
            }

            // Check if target can use it
            if (item.IsIdentified && !item.CanEquip(target, out string reason))
            {
                terminal.SetColor("red");
                terminal.Write($" [{reason}]");
            }

            terminal.WriteLine("");
        }
    }

    /// <summary>
    /// Convert a legacy Item to an Equipment object for equipping to teammates/companions/spouses.
    /// Returns null if the item is not equippable (potions, food, etc.).
    /// </summary>
    protected Equipment? ConvertInventoryItemToEquipment(Item invItem)
    {
        // Skip non-equippable item types
        if (invItem.Type == ObjType.Food || invItem.Type == ObjType.Drink ||
            invItem.Type == ObjType.Potion)
            return null;

        // Skip magic items that aren't equippable (rings, necklaces, belts are OK)
        if (invItem.Type == ObjType.Magic)
        {
            int magicType = (int)invItem.MagicType;
            if (magicType != 5 && magicType != 10 && magicType != 9) // Fingers, Neck, Waist
                return null;
        }

        // Determine slot from ObjType
        var slot = invItem.Type switch
        {
            ObjType.Weapon => EquipmentSlot.MainHand,
            ObjType.Shield => EquipmentSlot.OffHand,
            ObjType.Body => EquipmentSlot.Body,
            ObjType.Head => EquipmentSlot.Head,
            ObjType.Arms => EquipmentSlot.Arms,
            ObjType.Hands => EquipmentSlot.Hands,
            ObjType.Legs => EquipmentSlot.Legs,
            ObjType.Feet => EquipmentSlot.Feet,
            ObjType.Waist => EquipmentSlot.Waist,
            ObjType.Neck => EquipmentSlot.Neck,
            ObjType.Face => EquipmentSlot.Face,
            ObjType.Fingers => EquipmentSlot.LFinger,
            ObjType.Abody => EquipmentSlot.Cloak,
            ObjType.Magic => (int)invItem.MagicType switch
            {
                5 => EquipmentSlot.LFinger,
                10 => EquipmentSlot.Neck,
                9 => EquipmentSlot.Waist,
                _ => EquipmentSlot.None
            },
            _ => EquipmentSlot.None
        };

        if (slot == EquipmentSlot.None)
            return null; // Unknown item type — can't convert to equipment

        // Determine handedness
        WeaponHandedness handedness = WeaponHandedness.None;
        if (invItem.Type == ObjType.Weapon)
        {
            var knownEquip = EquipmentDatabase.GetByName(invItem.Name);
            if (knownEquip != null)
                handedness = knownEquip.Handedness;
            else
            {
                string nameLower = invItem.Name.ToLower();
                if (nameLower.Contains("two-hand") || nameLower.Contains("2h") ||
                    nameLower.Contains("greatsword") || nameLower.Contains("greataxe") ||
                    nameLower.Contains("halberd") || nameLower.Contains("pike") ||
                    nameLower.Contains("longbow") || nameLower.Contains("crossbow") ||
                    nameLower.Contains("staff") || nameLower.Contains("quarterstaff") ||
                    nameLower.Contains("maul") || nameLower.Contains("spear") ||
                    nameLower.Contains("glaive") || nameLower.Contains("bardiche") ||
                    nameLower.Contains("lance") || nameLower.Contains("voulge"))
                    handedness = WeaponHandedness.TwoHanded;
                else
                    handedness = WeaponHandedness.OneHanded;
            }
        }
        else if (invItem.Type == ObjType.Shield)
            handedness = WeaponHandedness.OffHandOnly;

        // Infer weight class for armor pieces
        var weightClass = ArmorWeightClass.None;
        if (slot.IsArmorSlot() && invItem.Type != ObjType.Weapon && invItem.Type != ObjType.Shield)
            weightClass = ShopItemGenerator.InferArmorWeightClass(invItem.Name);

        // Infer weapon type for weapons (needed for ability weapon requirements)
        var weaponType = WeaponType.None;
        if (invItem.Type == ObjType.Weapon)
        {
            var knownEquip = EquipmentDatabase.GetByName(invItem.Name);
            weaponType = knownEquip?.WeaponType ?? ShopItemGenerator.InferWeaponType(invItem.Name);
        }
        else if (invItem.Type == ObjType.Shield)
        {
            weaponType = ShopItemGenerator.InferShieldType(invItem.Name);
        }

        var equipment = new Equipment
        {
            Name = invItem.Name,
            Slot = slot,
            Handedness = handedness,
            WeaponType = weaponType,
            WeaponPower = invItem.Attack,
            ArmorClass = invItem.Type == ObjType.Shield ? 0 : invItem.Armor,
            WeightClass = weightClass,
            ShieldBonus = invItem.Type == ObjType.Shield ? invItem.ShieldBonus : 0,
            BlockChance = invItem.Type == ObjType.Shield ? invItem.BlockChance : 0,
            DefenceBonus = invItem.Defence,
            StrengthBonus = invItem.Strength,
            DexterityBonus = invItem.Dexterity,
            AgilityBonus = invItem.Agility,
            StaminaBonus = invItem.Stamina,
            WisdomBonus = invItem.Wisdom,
            CharismaBonus = invItem.Charisma,
            MaxHPBonus = invItem.HP,
            MaxManaBonus = invItem.Mana,
            Value = invItem.Value,
            IsCursed = invItem.IsCursed,
            IsIdentified = invItem.IsIdentified,
            MinLevel = invItem.MinLevel,
            Rarity = EquipmentRarity.Common
        };

        // Transfer all LootEffects (enchantments, stat bonuses, special properties)
        if (invItem.LootEffects != null)
        {
            foreach (var (effectType, value) in invItem.LootEffects)
            {
                var effect = (LootGenerator.SpecialEffect)effectType;
                switch (effect)
                {
                    case LootGenerator.SpecialEffect.FireDamage: equipment.HasFireEnchant = true; break;
                    case LootGenerator.SpecialEffect.IceDamage: equipment.HasFrostEnchant = true; break;
                    case LootGenerator.SpecialEffect.LightningDamage: equipment.HasLightningEnchant = true; break;
                    case LootGenerator.SpecialEffect.PoisonDamage:
                        equipment.HasPoisonEnchant = true;
                        equipment.PoisonDamage = Math.Max(equipment.PoisonDamage, value);
                        break;
                    case LootGenerator.SpecialEffect.HolyDamage: equipment.HasHolyEnchant = true; break;
                    case LootGenerator.SpecialEffect.ShadowDamage: equipment.HasShadowEnchant = true; break;
                    case LootGenerator.SpecialEffect.LifeSteal: equipment.LifeSteal = Math.Max(equipment.LifeSteal, Math.Max(5, value / 2)); break;
                    case LootGenerator.SpecialEffect.ManaSteal: equipment.ManaSteal = Math.Max(equipment.ManaSteal, Math.Max(5, value / 2)); break;
                    case LootGenerator.SpecialEffect.CriticalStrike: equipment.CriticalChanceBonus = Math.Max(equipment.CriticalChanceBonus, value); break;
                    case LootGenerator.SpecialEffect.CriticalDamage: equipment.CriticalDamageBonus = Math.Max(equipment.CriticalDamageBonus, value); break;
                    case LootGenerator.SpecialEffect.ArmorPiercing: equipment.ArmorPiercing = Math.Max(equipment.ArmorPiercing, value); break;
                    case LootGenerator.SpecialEffect.Thorns: equipment.Thorns = Math.Max(equipment.Thorns, value); break;
                    case LootGenerator.SpecialEffect.Regeneration: equipment.HPRegen = Math.Max(equipment.HPRegen, value); break;
                    case LootGenerator.SpecialEffect.ManaRegen: equipment.ManaRegen = Math.Max(equipment.ManaRegen, value); break;
                    case LootGenerator.SpecialEffect.MagicResist: equipment.MagicResistance = Math.Max(equipment.MagicResistance, value); break;
                    case LootGenerator.SpecialEffect.Constitution: equipment.ConstitutionBonus += value; break;
                    case LootGenerator.SpecialEffect.Intelligence: equipment.IntelligenceBonus += value; break;
                    case LootGenerator.SpecialEffect.AllStats:
                        equipment.ConstitutionBonus += value;
                        equipment.IntelligenceBonus += value;
                        equipment.CharismaBonus += value;
                        break;
                    case LootGenerator.SpecialEffect.BossSlayer: equipment.HasBossSlayer = true; break;
                    case LootGenerator.SpecialEffect.TitanResolve: equipment.HasTitanResolve = true; break;
                }
            }
        }

        EquipmentDatabase.RegisterDynamic(equipment);
        return equipment;
    }

    /// <summary>
    /// Filtered sell flow - lets players sell backpack items matching filters:
    /// by level gap, by Common rarity only, or by max gold value.
    /// Skips cursed and unidentified items automatically.
    /// </summary>
    protected async Task<bool> FilteredSellFromBackpack(ObjType[] validTypes, float fenceModifier)
    {
        if (currentPlayer.Inventory == null || currentPlayer.Inventory.Count == 0)
        {
            terminal.SetColor("gray");
            terminal.WriteLine(Loc.Get("shop.filter_none_found"));
            await terminal.WaitForKey();
            return false;
        }

        terminal.ClearScreen();
        WriteSectionHeader(Loc.Get("shop.filtered_sell_header"), "bright_yellow");
        terminal.WriteLine("");

        terminal.SetColor("white");
        terminal.WriteLine(Loc.Get("shop.filter_menu_prompt"));
        terminal.WriteLine("");

        int defaultLevelGap = 5;
        terminal.SetColor("bright_yellow");
        terminal.WriteLine($"  {Loc.Get("shop.filter_level_option", defaultLevelGap)}");
        terminal.WriteLine($"  {Loc.Get("shop.filter_value_option")}");
        terminal.WriteLine($"  {Loc.Get("shop.filter_rarity_option")}");
        terminal.SetColor("gray");
        terminal.WriteLine($"  {Loc.Get("shop.filter_cancel")}");
        terminal.WriteLine("");

        terminal.SetColor("cyan");
        terminal.Write(Loc.Get("ui.choice"));
        terminal.SetColor("white");
        var filterChoice = (await terminal.GetInput("")).Trim().ToUpper();

        List<Item> filtered;

        switch (filterChoice)
        {
            case "L":
                {
                    // Level-based filter
                    terminal.WriteLine("");
                    terminal.SetColor("white");
                    terminal.Write(Loc.Get("shop.filter_enter_level_gap", defaultLevelGap));
                    var gapInput = (await terminal.GetInput("")).Trim();
                    int levelGap = int.TryParse(gapInput, out int g) && g > 0 ? g : defaultLevelGap;
                    int maxItemLevel = Math.Max(1, currentPlayer.Level - levelGap);

                    filtered = currentPlayer.Inventory
                        .Where(i => i.IsIdentified && !i.IsCursed &&
                               validTypes.Contains(i.Type) &&
                               i.MinLevel <= maxItemLevel && i.MinLevel > 0)
                        .ToList();

                    if (filtered.Count == 0)
                    {
                        terminal.SetColor("gray");
                        terminal.WriteLine("");
                        terminal.WriteLine(Loc.Get("shop.filter_none_found"));
                        terminal.WriteLine(Loc.Get("shop.filter_level_label", maxItemLevel));
                        await terminal.WaitForKey();
                        return false;
                    }

                    terminal.SetColor("gray");
                    terminal.WriteLine(Loc.Get("shop.filter_level_label", maxItemLevel));
                    break;
                }

            case "V":
                {
                    // Value-based filter
                    terminal.WriteLine("");
                    terminal.SetColor("white");
                    terminal.Write(Loc.Get("shop.filter_enter_max_value"));
                    var valInput = (await terminal.GetInput("")).Trim();
                    if (!long.TryParse(valInput, out long maxValue) || maxValue <= 0)
                    {
                        terminal.SetColor("gray");
                        terminal.WriteLine(Loc.Get("ui.cancelled"));
                        await terminal.WaitForKey();
                        return false;
                    }

                    filtered = currentPlayer.Inventory
                        .Where(i => i.IsIdentified && !i.IsCursed &&
                               validTypes.Contains(i.Type) &&
                               i.Value < maxValue)
                        .ToList();

                    if (filtered.Count == 0)
                    {
                        terminal.SetColor("gray");
                        terminal.WriteLine("");
                        terminal.WriteLine(Loc.Get("shop.filter_none_found"));
                        await terminal.WaitForKey();
                        return false;
                    }
                    break;
                }

            case "C":
                {
                    // Common rarity only - items with no special enchantments or loot effects
                    filtered = currentPlayer.Inventory
                        .Where(i => i.IsIdentified && !i.IsCursed &&
                               validTypes.Contains(i.Type) &&
                               (i.LootEffects == null || i.LootEffects.Count == 0) &&
                               !i.IsArtifact)
                        .ToList();

                    if (filtered.Count == 0)
                    {
                        terminal.SetColor("gray");
                        terminal.WriteLine("");
                        terminal.WriteLine(Loc.Get("shop.filter_none_found"));
                        await terminal.WaitForKey();
                        return false;
                    }
                    break;
                }

            default:
                return false;
        }

        // Show preview
        long totalGold = filtered.Sum(i => (long)((i.Value / 2) * fenceModifier));
        terminal.WriteLine("");
        terminal.SetColor("white");
        terminal.WriteLine(Loc.Get("shop.filter_preview", filtered.Count, totalGold.ToString("N0")));
        terminal.WriteLine("");

        // List items being sold
        for (int i = 0; i < Math.Min(filtered.Count, 20); i++)
        {
            terminal.SetColor("gray");
            terminal.Write("  ");
            terminal.SetColor("white");
            terminal.Write(filtered[i].Name);
            terminal.SetColor("darkgray");
            long itemPrice = (long)((filtered[i].Value / 2) * fenceModifier);
            terminal.WriteLine($" - {itemPrice:N0}g");
        }
        if (filtered.Count > 20)
        {
            terminal.SetColor("darkgray");
            terminal.WriteLine($"  ... and {filtered.Count - 20} more");
        }

        terminal.WriteLine("");
        terminal.SetColor("yellow");
        terminal.Write(Loc.Get("shop.filter_confirm", filtered.Count, totalGold.ToString("N0")));
        var confirm = (await terminal.GetInput("")).Trim().ToUpper();

        if (confirm == "Y")
        {
            foreach (var item in filtered)
                currentPlayer.Inventory.Remove(item);
            currentPlayer.Gold += totalGold;
            currentPlayer.Statistics.RecordSale(totalGold);
            DebugLogger.Instance.LogInfo("GOLD", $"FILTERED SELL: {currentPlayer.DisplayName} sold {filtered.Count} items for {totalGold:N0}g (gold now {currentPlayer.Gold:N0})");
            currentPlayer.RecalculateStats();

            terminal.SetColor("bright_green");
            terminal.WriteLine("");
            terminal.WriteLine(Loc.Get("shop.filter_sold", filtered.Count, totalGold.ToString("N0")));

            await SaveSystem.Instance.AutoSave(currentPlayer);
            await terminal.WaitForKey();
            return true;
        }

        return false;
    }
}
