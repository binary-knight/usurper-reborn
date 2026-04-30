using UsurperRemake.Utils;
using UsurperRemake.Systems;
using UsurperRemake.Data;
using UsurperRemake.Locations;
using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using System.Linq;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.IO;

/// <summary>
/// Main game engine based on Pascal USURPER.PAS
/// Handles the core game loop, initialization, and game state management
/// Now includes comprehensive save/load system and flexible daily cycles
/// </summary>
public partial class GameEngine
{
    private static readonly Lazy<GameEngine> lazyInstance = new Lazy<GameEngine>(() => new GameEngine());
    private static GameEngine? _fallbackInstance;
    private TerminalEmulator terminal;
    private Character? currentPlayer;

    /// <summary>
    /// Temporary remap of dynamic equipment IDs from save → fresh IDs (MUD mode only).
    /// Used during RestorePlayer to avoid ID collisions between players in shared EquipmentDatabase.
    /// Also applied during companion equipment restore in CompanionSystem.
    /// </summary>
    internal Dictionary<int, int>? DynamicEquipIdRemap { get; set; }

    /// <summary>
    /// Pending notifications to show the player (team events, important world events, etc.)
    /// In MUD mode, stored per-session in SessionContext. In single-player, uses static queue.
    /// </summary>
    public static Queue<string> PendingNotifications
    {
        get
        {
            var ctx = UsurperRemake.Server.SessionContext.Current;
            if (ctx != null) return ctx.PendingNotifications;
            return _staticPendingNotifications;
        }
    }
    private static readonly Queue<string> _staticPendingNotifications = new();
    private bool _splashScreenShown = false;
    private string? _sleepLocationOnLogin;

    /// <summary>
    /// Add a notification to be shown to the player
    /// </summary>
    public static void AddNotification(string message)
    {
        if (!string.IsNullOrWhiteSpace(message))
        {
            var queue = PendingNotifications;
            // Cap notifications to prevent unbounded growth
            while (queue.Count >= 100)
                queue.Dequeue();
            queue.Enqueue(message);
        }
    }

    /// <summary>
    /// Flag indicating an intentional exit (quit from menu) vs unexpected termination
    /// In MUD mode, stored per-session in SessionContext.
    /// </summary>
    public static bool IsIntentionalExit
    {
        get
        {
            var ctx = UsurperRemake.Server.SessionContext.Current;
            if (ctx != null) return ctx.IsIntentionalExit;
            return _staticIsIntentionalExit;
        }
        private set
        {
            var ctx = UsurperRemake.Server.SessionContext.Current;
            if (ctx != null) ctx.IsIntentionalExit = value;
            else _staticIsIntentionalExit = value;
        }
    }
    private static bool _staticIsIntentionalExit = false;

    /// <summary>
    /// True if the player actively selected a language on the main menu this session.
    /// Used to prevent LoadSaveByFileName from overwriting their choice with the saved default.
    /// Uses AsyncLocal so each MUD session has its own flag.
    /// </summary>
    private static readonly System.Threading.AsyncLocal<bool> _languageSetThisSession = new();

    /// <summary>
    /// Mark the current session as an intentional exit (prevents warning on shutdown)
    /// </summary>
    public static void MarkIntentionalExit()
    {
        IsIntentionalExit = true;
    }

    /// <summary>
    /// Flag indicating the player accepted NG+ and needs a fresh character
    /// </summary>
    public bool PendingNewGamePlus { get; set; }

    /// <summary>
    /// Flag indicating the player ascended to immortality after an ending
    /// </summary>
    public bool PendingImmortalAscension { get; set; }

    /// <summary>
    /// Flag indicating the player died in Nightmare mode (permadeath) — save deleted, exit to menu
    /// </summary>
    public bool IsPermadeath { get; set; }

    /// <summary>
    /// Thread-safe singleton accessor. In MUD mode, returns the per-session engine.
    /// In single-player/BBS, returns the static fallback instance.
    /// </summary>
    public static GameEngine Instance
    {
        get
        {
            // MUD mode: per-session engine
            var ctx = UsurperRemake.Server.SessionContext.Current;
            if (ctx?.Engine != null) return ctx.Engine;
            // If a specific instance was set explicitly, use it
            if (_fallbackInstance != null) return _fallbackInstance;
            // Otherwise use lazy initialization for thread safety
            return lazyInstance.Value;
        }
    }

    /// <summary>
    /// Set the instance explicitly
    /// </summary>
    public static void SetInstance(GameEngine engine)
    {
        var ctx = UsurperRemake.Server.SessionContext.Current;
        if (ctx != null) ctx.Engine = engine;
        else _fallbackInstance = engine;
    }
    
    // Missing properties for compilation
    public Character? CurrentPlayer
    {
        get => currentPlayer;
        set => currentPlayer = value;
    }

    public static string DataPath => GameConfig.DataPath;

    // Dungeon party NPC IDs (spouses, team members, lovers) - persisted across saves
    private List<string> dungeonPartyNPCIds = new();

    /// <summary>
    /// Get the list of NPC IDs currently in the dungeon party
    /// </summary>
    public List<string> DungeonPartyNPCIds => dungeonPartyNPCIds;

    /// <summary>
    /// Set dungeon party NPC IDs (called from DungeonLocation when party changes)
    /// </summary>
    public void SetDungeonPartyNPCs(IEnumerable<string> npcIds)
    {
        dungeonPartyNPCIds = npcIds.ToList();
    }

    /// <summary>
    /// Clear dungeon party NPCs (called when leaving dungeon or on death)
    /// </summary>
    public void ClearDungeonParty()
    {
        dungeonPartyNPCIds.Clear();
        dungeonPartyPlayerNames.Clear();
    }

    // Player echo teammates for cooperative dungeons (online mode)
    private List<string> dungeonPartyPlayerNames = new();
    public List<string> DungeonPartyPlayerNames => dungeonPartyPlayerNames;
    public void SetDungeonPartyPlayers(IEnumerable<string> names) => dungeonPartyPlayerNames = names.ToList();
    public void ClearDungeonPartyPlayers() => dungeonPartyPlayerNames.Clear();

    /// <summary>
    /// Auto-populate quickbar for characters migrating from pre-quickbar saves.
    /// Fills slots 1-9 with known spells (casters) or learned abilities (martial).
    /// </summary>
    public static void AutoPopulateQuickbar(Character player)
    {
        player.Quickbar = new List<string?>(new string?[9]);
        int slot = 0;

        bool isSpellcaster = ClassAbilitySystem.IsSpellcaster(player.Class);
        bool hasSpells = SpellSystem.HasSpells(player);

        if (isSpellcaster)
        {
            // Pure caster: fill with known spells ordered by level
            var knownSpells = SpellSystem.GetAvailableSpells(player);
            foreach (var spell in knownSpells.OrderBy(s => s.Level))
            {
                if (slot >= 9) break;
                player.Quickbar[slot] = $"spell:{spell.Level}";
                slot++;
            }
        }
        else
        {
            // Fill with learned abilities ordered by level requirement
            var abilities = ClassAbilitySystem.GetAvailableAbilities(player);
            foreach (var ability in abilities.OrderBy(a => a.LevelRequired))
            {
                if (slot >= 9) break;
                player.Quickbar[slot] = ability.Id;
                slot++;
            }

            // Hybrid (prestige): also add known spells in remaining slots
            if (hasSpells)
            {
                var knownSpells = SpellSystem.GetAvailableSpells(player);
                foreach (var spell in knownSpells.OrderBy(s => s.Level))
                {
                    if (slot >= 9) break;
                    player.Quickbar[slot] = $"spell:{spell.Level}";
                    slot++;
                }
            }
        }
    }

    /// <summary>
    /// Add newly available spells/abilities to empty quickbar slots.
    /// Called after level-up so new skills are immediately usable in combat.
    /// Does NOT overwrite existing slot assignments.
    /// </summary>
    public static void QuickbarAddNewSkills(Character player)
    {
        if (player.Quickbar == null || player.Quickbar.Count < 9)
            player.Quickbar = new List<string?>(new string?[9]);

        // Collect what's already on the quickbar
        var equipped = new HashSet<string>(player.Quickbar.Where(s => s != null)!);

        bool isSpellcaster = ClassAbilitySystem.IsSpellcaster(player.Class);
        bool hasSpells = SpellSystem.HasSpells(player);

        if (isSpellcaster)
        {
            var knownSpells = SpellSystem.GetAvailableSpells(player);
            foreach (var spell in knownSpells.OrderBy(s => s.Level))
            {
                string id = $"spell:{spell.Level}";
                if (equipped.Contains(id)) continue;
                int emptySlot = player.Quickbar.IndexOf(null);
                if (emptySlot < 0) break; // No empty slots
                player.Quickbar[emptySlot] = id;
                equipped.Add(id);
            }
        }
        else
        {
            var abilities = ClassAbilitySystem.GetAvailableAbilities(player);
            foreach (var ability in abilities.OrderBy(a => a.LevelRequired))
            {
                if (equipped.Contains(ability.Id)) continue;
                int emptySlot = player.Quickbar.IndexOf(null);
                if (emptySlot < 0) break; // No empty slots
                player.Quickbar[emptySlot] = ability.Id;
                equipped.Add(ability.Id);
            }

            // Hybrid (prestige): also add new spells to remaining slots
            if (hasSpells)
            {
                var knownSpells = SpellSystem.GetAvailableSpells(player);
                foreach (var spell in knownSpells.OrderBy(s => s.Level))
                {
                    string id = $"spell:{spell.Level}";
                    if (equipped.Contains(id)) continue;
                    int emptySlot = player.Quickbar.IndexOf(null);
                    if (emptySlot < 0) break;
                    player.Quickbar[emptySlot] = id;
                    equipped.Add(id);
                }
            }
        }
    }

    // Terminal access for systems
    public TerminalEmulator Terminal => terminal;
    
    // Core game components
    private GameState gameState;
    private List<NPC> worldNPCs;
    private List<Monster> worldMonsters;
    private LocationManager locationManager;
    private DailySystemManager dailyManager;
    private CombatEngine combatEngine;
    private WorldSimulator worldSimulator;
    
    // Online system
    private List<OnlinePlayer> onlinePlayers;
    
    // Per-session daily state (avoids shared singleton cross-contamination in MUD mode)
    public int SessionCurrentDay { get; set; } = 1;
    public DateTime SessionLastResetTime { get; set; } = DateTime.Now;
    public DailyCycleMode SessionDailyCycleMode { get; set; } = DailyCycleMode.Endless;

    // Auto-save timer
    private DateTime lastPeriodicCheck;
    
    // Stub classes for compilation
    private class UsurperConfig
    {
        // Pascal compatible config structure
    }
    
    private class ScoreManager
    {
        // Score and ranking management
    }

    /// <summary>
    /// Console entry point for running the full game
    /// </summary>
    public static async Task RunConsoleAsync()
    {
        var engine = Instance;

        // Check if we're in BBS door mode or online mode (both have pre-set player names)
        if (UsurperRemake.BBS.DoorMode.IsInDoorMode || UsurperRemake.BBS.DoorMode.IsOnlineMode)
        {
            await engine.RunBBSDoorMode();
        }
        else
        {
            await engine.RunMainGameLoop();
        }
    }

    /// <summary>
    /// Main game loop for console mode
    /// </summary>
    private async Task RunMainGameLoop()
    {
        InitializeGame();

        // Start version check in background while splash screen shows
        var versionCheckTask = UsurperRemake.Systems.VersionChecker.Instance.CheckForUpdatesAsync();

        // Show splash screen (the colorful USURPER REBORN title)
        await UsurperRemake.UI.SplashScreen.Show(terminal);

        // Wait for version check to complete (should be done by now)
        await versionCheckTask;

        // Show update notification if new version available
        if (UsurperRemake.Systems.VersionChecker.Instance.NewVersionAvailable)
        {
            await UsurperRemake.Systems.VersionChecker.Instance.DisplayUpdateNotification(terminal);
            var shouldExit = await UsurperRemake.Systems.VersionChecker.Instance.PromptAndInstallUpdate(terminal);
            if (shouldExit)
            {
                // Exit the game so the updater can replace files
                Environment.Exit(0);
            }
        }

        // Go directly to main menu (skip the redundant title screen)
        await MainMenu();
    }

    /// <summary>
    /// BBS Door mode - automatically loads or creates character based on drop file
    /// </summary>
    private async Task RunBBSDoorMode()
    {
        InitializeGame();

        // In MUD mode, use the immutable account username from SessionContext.
        // GetPlayerName() can return alt keys or corrupted values after character switching.
        // In BBS door mode, fall back to GetPlayerName() (reads from drop file).
        var ctx0 = UsurperRemake.Server.SessionContext.Current;
        var playerName = (ctx0 != null && !string.IsNullOrEmpty(ctx0.Username))
            ? ctx0.Username
            : UsurperRemake.BBS.DoorMode.GetPlayerName();
        UsurperRemake.BBS.DoorMode.Log($"BBS Door mode: Looking for save for '{playerName}'");

        // Show the title screen (once per session)
        if (!_splashScreenShown)
        {
            await UsurperRemake.UI.SplashScreen.Show(terminal);
            _splashScreenShown = true;
        }

        terminal.ClearScreen();

        // Show MOTD if set
        if (!string.IsNullOrEmpty(GameConfig.MessageOfTheDay))
        {
            terminal.SetColor("bright_yellow");
            terminal.WriteLine($"  {GameConfig.MessageOfTheDay}");
            terminal.WriteLine("");
        }

        ShowAlphaBanner();

        // Check if this account has existing characters (main + alt)
        var accountName = playerName;
        var altKey = SqlSaveBackend.GetAltKey(accountName);
        var mainSave = SaveSystem.Instance.GetMostRecentSave(accountName);
        var altSave = SaveSystem.Instance.GetMostRecentSave(altKey);

        // Show welcome with display name from save (if available), otherwise capitalize account name
        var welcomeName = mainSave?.PlayerName ?? altSave?.PlayerName
            ?? (char.ToUpper(playerName[0]) + playerName.Substring(1));
        terminal.SetColor("white");
        terminal.WriteLine(Loc.Get("engine.welcome", welcomeName));
        terminal.WriteLine("");
        bool isSysOp = UsurperRemake.BBS.DoorMode.IsSysOp;
        bool isOnlineAdmin = UsurperRemake.Server.SessionContext.IsActive
            ? (UsurperRemake.Server.SessionContext.Current?.WizardLevel ?? UsurperRemake.Server.WizardLevel.Mortal) >= UsurperRemake.Server.WizardLevel.God
            : UsurperRemake.BBS.DoorMode.IsOnlineMode &&
                (string.Equals(UsurperRemake.BBS.DoorMode.OnlineUsername, "rage", StringComparison.OrdinalIgnoreCase) ||
                 string.Equals(UsurperRemake.BBS.DoorMode.OnlineUsername, "fastfinge", StringComparison.OrdinalIgnoreCase));

        // Peek at main save to check immortal/alt slot status
        bool mainIsImmortal = false;
        bool hasAltSlot = false;
        if (mainSave != null)
        {
            try
            {
                var mainData = await SaveSystem.Instance.LoadSaveByFileName(accountName);
                mainIsImmortal = mainData?.Player?.IsImmortal == true;
                hasAltSlot = mainData?.Player?.HasEarnedAltSlot == true;
            }
            catch (Exception ex) { DebugLogger.Instance.LogError("ENGINE", $"[ShowCharacterSlots] Failed to peek alt slot data: {ex.Message}"); }
        }

        // Show character slots
        if (mainSave != null)
        {
            terminal.SetColor("green");
            string mainTag = mainIsImmortal ? Loc.Get("engine.immortal_tag") : "";
            terminal.WriteLine(Loc.Get("engine.char_slot", "1", mainSave.PlayerName, mainSave.Level, mainSave.ClassName, mainTag));
            terminal.SetColor("gray");
            if (mainSave.SaveTime.Year >= 2020)
                terminal.WriteLine(Loc.Get("engine.last_played", mainSave.SaveTime.ToString("yyyy-MM-dd HH:mm")));
        }
        if (altSave != null)
        {
            terminal.SetColor("green");
            terminal.WriteLine(Loc.Get("engine.char_slot", "2", altSave.PlayerName, altSave.Level, altSave.ClassName, ""));
            terminal.SetColor("gray");
            if (altSave.SaveTime.Year >= 2020)
                terminal.WriteLine(Loc.Get("engine.last_played", altSave.SaveTime.ToString("yyyy-MM-dd HH:mm")));
        }
        if (mainSave != null || altSave != null)
            terminal.WriteLine("");

        // Show alt creation option if eligible (has alt slot but no alt character yet)
        bool canCreateAlt = (mainIsImmortal || hasAltSlot) && altSave == null && UsurperRemake.BBS.DoorMode.IsOnlineMode;

        // Compact BBS menu (fits 24-line terminals) vs full menu for MUD/local
        bool compactMenu = UsurperRemake.BBS.DoorMode.IsInDoorMode;
        bool showOnline = !UsurperRemake.BBS.DoorMode.IsMudServerMode && !UsurperRemake.BBS.DoorMode.IsMudRelayMode
            && !GameConfig.DisableOnlinePlay;

        if (compactMenu)
        {
            // ── Compact BBS menu: all options in a dense layout ──
            if (mainSave != null)
                WriteMenuKey("1", Loc.Get("engine.menu_play", mainSave.PlayerName));
            if (altSave != null)
                WriteMenuKey("2", Loc.Get("engine.menu_play", altSave.PlayerName));
            if (mainSave == null)
                WriteMenuKey("N", Loc.Get("engine.menu_new_char"));
            else
                WriteMenuKey("N", Loc.Get("engine.menu_new_overwrite"));
            if (canCreateAlt)
                WriteMenuKey("M", Loc.Get("engine.menu_create_alt"));
            if (altSave != null)
                WriteMenuKey("D", Loc.Get("engine.menu_delete_alt"));
            if (showOnline)
                WriteMenuKey("O", Loc.Get("engine.menu_online"));
            terminal.WriteLine("");
            WriteMenuKey("I", Loc.Get("engine.menu_story"));
            WriteMenuKey("H", Loc.Get("engine.menu_history"));
            WriteMenuKey("B", Loc.Get("engine.menu_bbs_list"));
            WriteMenuKey("C", Loc.Get("engine.menu_credits"));
            WriteMenuKey("A", GameConfig.ScreenReaderMode ? Loc.Get("engine.menu_sr_on") : Loc.Get("engine.menu_sr_off"));
            WriteMenuKey("Z", GameConfig.CompactMode ? Loc.Get("engine.menu_compact_on") : Loc.Get("engine.menu_compact_off"));
            if (UsurperRemake.Server.SessionContext.IsActive)
                WriteMenuKey("S", Loc.Get("engine.menu_spectate"));
            if (UsurperRemake.BBS.DoorMode.IsOnlineMode && !UsurperRemake.BBS.DoorMode.IsInDoorMode)
                WriteMenuKey("P", Loc.Get("engine.menu_password"));
            if (isSysOp || isOnlineAdmin)
                WriteMenuKey("%", isSysOp ? Loc.Get("engine.menu_sysop") : Loc.Get("engine.menu_admin"));
#if !STEAM_BUILD
            WriteMenuKey("@", Loc.Get("engine.menu_support"));
#endif
            WriteMenuKey("Q", Loc.Get("engine.menu_quit"));
            terminal.WriteLine("");
        }
        else
        {
            // ── Full menu for MUD/local/Steam ──
            terminal.SetColor("darkgray");
            terminal.WriteLine(GameConfig.ScreenReaderMode ? Loc.Get("engine.section_play") : "  ── PLAY ─────────────────────────────────────────────────────────────────");
            if (mainSave != null)
                WriteMenuKey("1", Loc.Get("engine.menu_play", mainSave.PlayerName));
            if (altSave != null)
                WriteMenuKey("2", Loc.Get("engine.menu_play", altSave.PlayerName));
            if (canCreateAlt)
                WriteMenuKey("M", Loc.Get("engine.menu_create_mortal_alt"));
            if (altSave != null)
                WriteMenuKey("D", Loc.Get("engine.menu_delete_alt_full"));
            if (mainSave == null)
                WriteMenuKey("N", Loc.Get("engine.menu_create_new"));
            else
                WriteMenuKey("N", Loc.Get("engine.menu_new_overwrite_full"));
            if (showOnline)
                WriteMenuKey("O", Loc.Get("engine.menu_online_full"));

            terminal.WriteLine("");

            terminal.SetColor("darkgray");
            terminal.WriteLine(GameConfig.ScreenReaderMode ? Loc.Get("engine.section_info") : "  ── INFO ─────────────────────────────────────────────────────────────────");
            WriteMenuKey("I", Loc.Get("engine.menu_story_full"));
            WriteMenuKey("H", Loc.Get("engine.menu_history_full"));
            WriteMenuKey("B", Loc.Get("engine.menu_bbs_full"));
            WriteMenuKey("C", Loc.Get("engine.menu_credits"));
#if !STEAM_BUILD
            WriteMenuKey("@", Loc.Get("engine.menu_support_full"));
#endif

            terminal.WriteLine("");

            terminal.SetColor("darkgray");
            terminal.WriteLine(GameConfig.ScreenReaderMode ? Loc.Get("engine.section_accessibility") : "  ── ACCESSIBILITY ────────────────────────────────────────────────────────");
            terminal.SetColor("darkgray");
            terminal.Write("  [");
            terminal.SetColor("bright_yellow");
            terminal.Write("A");
            terminal.SetColor("darkgray");
            terminal.Write("] ");
            if (GameConfig.ScreenReaderMode)
            {
                terminal.SetColor("bright_green");
                terminal.WriteLine(Loc.Get("engine.menu_sr_on"));
            }
            else
            {
                terminal.SetColor("white");
                terminal.WriteLine(Loc.Get("engine.menu_sr_off"));
            }
            terminal.SetColor("darkgray");
            terminal.Write("  [");
            terminal.SetColor("bright_yellow");
            terminal.Write("Z");
            terminal.SetColor("darkgray");
            terminal.Write("] ");
            if (GameConfig.CompactMode)
            {
                terminal.SetColor("bright_green");
                terminal.WriteLine(Loc.Get("engine.main_compact_on_visual"));
            }
            else
            {
                terminal.SetColor("white");
                terminal.WriteLine(Loc.Get("engine.main_compact_off_visual"));
            }
            if (UsurperRemake.Server.SessionContext.IsActive)
                WriteMenuKey("S", Loc.Get("engine.menu_spectate_full"));
            WriteMenuKey("G", Loc.Get("engine.main_language_visual", UsurperRemake.Systems.Loc.GetLanguageName(GameConfig.Language)));

            terminal.WriteLine("");

            if (UsurperRemake.BBS.DoorMode.IsOnlineMode && !UsurperRemake.BBS.DoorMode.IsInDoorMode)
                WriteMenuKey("P", Loc.Get("engine.menu_change_password"));
            if (isSysOp || isOnlineAdmin)
                WriteMenuKey("%", isSysOp ? Loc.Get("engine.menu_sysop_console") : Loc.Get("engine.menu_admin_console"));
            terminal.SetColor("darkgray");
            terminal.Write("  [");
            terminal.SetColor("red");
            terminal.Write("Q");
            terminal.SetColor("darkgray");
            terminal.Write("] ");
            terminal.SetColor("gray");
            terminal.WriteLine(Loc.Get("engine.menu_quit"));
            terminal.WriteLine("");
        }

        var choice = await terminal.GetInput(Loc.Get("ui.your_choice"));

        switch (choice.ToUpper())
        {
            case "1":
                if (mainSave != null)
                {
                    // Ensure identity is set to main account
                    UsurperRemake.BBS.DoorMode.SetOnlineUsername(accountName);
                    var ctx1 = UsurperRemake.Server.SessionContext.Current;
                    if (ctx1 != null) ctx1.CharacterKey = accountName;
                    await LoadSaveByFileName(mainSave.FileName);
                }
                else
                {
                    await CreateNewGame(accountName);
                }
                break;

            case "L":
                // Legacy shortcut — load main character (or alt if only alt exists)
                if (mainSave != null)
                {
                    UsurperRemake.BBS.DoorMode.SetOnlineUsername(accountName);
                    var ctxL = UsurperRemake.Server.SessionContext.Current;
                    if (ctxL != null) ctxL.CharacterKey = accountName;
                    await LoadSaveByFileName(mainSave.FileName);
                }
                else if (altSave != null)
                {
                    await SwitchToAltCharacter(altKey, altSave.PlayerName);
                    await LoadSaveByFileName(altKey);
                }
                break;

            case "2":
                if (altSave != null)
                {
                    await SwitchToAltCharacter(altKey, altSave.PlayerName);
                    await LoadSaveByFileName(altKey);
                }
                else if (mainSave != null)
                {
                    // Default to loading main
                    await LoadSaveByFileName(mainSave.FileName);
                }
                break;

            case "M":
                if (canCreateAlt)
                {
                    await SwitchToAltCharacter(altKey);
                    await CreateNewGame(altKey);
                }
                else
                {
                    terminal.WriteLine(Loc.Get("engine.immortal_required"), "red");
                    await Task.Delay(2000);
                    await RunBBSDoorMode();
                    return;
                }
                break;

            case "N":
                if (mainSave != null)
                {
                    terminal.SetColor("bright_red");
                    terminal.WriteLine("");
                    terminal.WriteLine(Loc.Get("engine.delete_main_warning"));
                    if (altSave != null)
                        terminal.WriteLine(Loc.Get("engine.delete_alt_unaffected"));
                    var confirm = await terminal.GetInput(Loc.Get("engine.delete_confirm_prompt"));
                    if (confirm == "DELETE")
                    {
                        // Delete main character save only (not alt)
                        var saves = SaveSystem.Instance.GetPlayerSaves(accountName);
                        foreach (var save in saves)
                        {
                            SaveSystem.Instance.DeleteSave(Path.GetFileNameWithoutExtension(save.FileName));
                        }
                        // Ensure identity is main account
                        UsurperRemake.BBS.DoorMode.SetOnlineUsername(accountName);
                        var ctxN = UsurperRemake.Server.SessionContext.Current;
                        if (ctxN != null) ctxN.CharacterKey = accountName;
                        await CreateNewGame(accountName);
                    }
                    else
                    {
                        terminal.WriteLine(Loc.Get("engine.delete_cancelled"), "yellow");
                        await Task.Delay(2000);
                        await RunBBSDoorMode();
                        return;
                    }
                }
                else
                {
                    await CreateNewGame(accountName);
                }
                break;

            case "C":
                await ShowCredits();
                await RunBBSDoorMode();
                return;

            case "I":
                await ShowInstructions();
                await RunBBSDoorMode();
                return;

            case "H":
                await UsurperHistorySystem.Instance.ShowHistory(terminal);
                await RunBBSDoorMode();
                return;

            case "B":
                await ShowBBSList();
                await RunBBSDoorMode();
                return;

            case "P":
                if (UsurperRemake.BBS.DoorMode.IsOnlineMode)
                {
                    await ChangePasswordScreen();
                    await RunBBSDoorMode();
                    return;
                }
                break;

            case "%":
                if (isSysOp || (isOnlineAdmin && UsurperRemake.BBS.DoorMode.IsInDoorMode))
                {
                    await ShowSysOpConsole();
                    await RunBBSDoorMode();
                    return;
                }
                else if (isOnlineAdmin)
                {
                    await ShowOnlineAdminConsole();
                    await RunBBSDoorMode();
                    return;
                }
                break;

            case "A":
                GameConfig.ScreenReaderMode = !GameConfig.ScreenReaderMode;
                // Only persist to sysop_config in single-player/BBS mode (server-wide setting)
                // In MUD mode, SR preference is per-player (stored in their save data)
                if (!UsurperRemake.BBS.DoorMode.IsOnlineMode)
                    UsurperRemake.Systems.SysOpConfigSystem.Instance.SaveConfig();
                terminal.WriteLine("");
                if (GameConfig.ScreenReaderMode)
                {
                    terminal.WriteLine(Loc.Get("engine.sr_enabled"), "bright_green");
                    terminal.WriteLine(Loc.Get("engine.sr_enabled_desc"), "white");
                }
                else
                {
                    terminal.WriteLine(Loc.Get("engine.sr_disabled"), "white");
                    terminal.WriteLine(Loc.Get("engine.sr_disabled_desc"), "white");
                }
                await Task.Delay(1500);
                await RunBBSDoorMode();
                return;

            case "Z":
                GameConfig.CompactMode = !GameConfig.CompactMode;
                terminal.WriteLine("");
                if (GameConfig.CompactMode)
                {
                    terminal.WriteLine(Loc.Get("engine.compact_enabled"), "bright_green");
                    terminal.WriteLine(Loc.Get("engine.compact_enabled_desc"), "white");
                }
                else
                {
                    terminal.WriteLine(Loc.Get("engine.compact_disabled"), "white");
                    terminal.WriteLine(Loc.Get("engine.compact_disabled_desc"), "white");
                }
                await Task.Delay(1500);
                await RunBBSDoorMode();
                return;

            case "O":
                if (!UsurperRemake.BBS.DoorMode.IsMudServerMode && !UsurperRemake.BBS.DoorMode.IsMudRelayMode
                    && !GameConfig.DisableOnlinePlay)
                {
                    var onlinePlay = new UsurperRemake.Systems.OnlinePlaySystem(terminal);
                    await onlinePlay.StartOnlinePlay();
                    await RunBBSDoorMode();
                    return;
                }
                break;

            case "S":
                if (UsurperRemake.Server.SessionContext.IsActive)
                {
                    await RunSpectatorModeUI();
                    await RunBBSDoorMode();
                    return;
                }
                break;

            case "G":
                terminal.WriteLine("");
                terminal.WriteLine(Loc.Get("prefs.select_language"), "bright_yellow");
                terminal.WriteLine("");
                var doorLangs = UsurperRemake.Systems.Loc.AvailableLanguages;
                for (int li = 0; li < doorLangs.Length; li++)
                {
                    var marker = doorLangs[li].Code == (GameConfig.Language ?? "en") ? " *" : "";
                    terminal.WriteLine($"  {li + 1}. {doorLangs[li].Name}{marker}");
                }
                terminal.WriteLine("");
                var doorLangChoice = await terminal.GetInput(Loc.Get("ui.your_choice"));
                if (int.TryParse(doorLangChoice.Trim(), out int doorLangIdx) && doorLangIdx >= 1 && doorLangIdx <= doorLangs.Length)
                {
                    var selectedLang = doorLangs[doorLangIdx - 1].Code;
                    GameConfig.Language = selectedLang;
                    _languageSetThisSession.Value = true;
                    terminal.WriteLine(Loc.Get("prefs.language_set", UsurperRemake.Systems.Loc.GetLanguageName(selectedLang)), "green");
                    await Task.Delay(800);
                }
                await RunBBSDoorMode();
                return;

            case "Q":
                IsIntentionalExit = true;
                terminal.WriteLine(Loc.Get("engine.goodbye"), "cyan");
                await Task.Delay(1000);
                break;

#if !STEAM_BUILD
            case "@":
                await ShowSupportPage();
                await RunBBSDoorMode();
                return;
#endif

            case "D":
                if (altSave != null)
                {
                    terminal.SetColor("bright_red");
                    terminal.WriteLine("");
                    terminal.WriteLine(Loc.Get("engine.delete_alt_warning", altSave.PlayerName));
                    var confirmDel = await terminal.GetInput(Loc.Get("engine.delete_alt_confirm"));
                    if (confirmDel == "DELETE")
                    {
                        SaveSystem.Instance.DeleteSave(altKey);
                        // Also clean up sleeping_players entry for the alt
                        if (SaveSystem.Instance.Backend is SqlSaveBackend sqlDel)
                        {
                            try { await sqlDel.UnregisterSleepingPlayer(altKey); } catch (Exception ex) { DebugLogger.Instance.LogError("ENGINE", $"[DeleteAltCharacter] Failed to unregister sleeping player '{altKey}': {ex.Message}"); }
                        }
                        terminal.WriteLine(Loc.Get("engine.delete_alt_done", altSave.PlayerName), "yellow");
                        await Task.Delay(2000);
                    }
                    else
                    {
                        terminal.WriteLine(Loc.Get("engine.delete_alt_cancelled"), "gray");
                        await Task.Delay(1500);
                    }
                    await RunBBSDoorMode();
                    return;
                }
                break;

            default:
                // Default: load first available character
                if (mainSave != null)
                {
                    await LoadSaveByFileName(mainSave.FileName);
                }
                else if (altSave != null)
                {
                    await SwitchToAltCharacter(altKey, altSave.PlayerName);
                    await LoadSaveByFileName(altKey);
                }
                else
                {
                    await CreateNewGame(accountName);
                }
                break;
        }

        // Handle immortal ascension — player became a god, enter the Pantheon
        if (PendingImmortalAscension)
        {
            PendingImmortalAscension = false;
            // Route to Pantheon for this session; future logins route via IsImmortal check in LoadSaveByFileName
            if (currentPlayer != null)
            {
                await locationManager.EnterLocation(GameLocation.Pantheon, currentPlayer);
            }
        }

        // Handle NG+ restart in BBS/online mode — after any LoadSaveByFileName path
        if (PendingNewGamePlus)
        {
            PendingNewGamePlus = false;
            // Preserve player preferences before deleting old save
            bool preserveScreenReader = currentPlayer?.ScreenReaderMode ?? GameConfig.ScreenReaderMode;
            var preserveOrientation = currentPlayer?.Orientation ?? SexualOrientation.Straight;
            // Use the active character key (could be main or alt)
            var activeKey = UsurperRemake.BBS.DoorMode.GetPlayerName()?.ToLowerInvariant() ?? accountName;
            var ngpSaves = SaveSystem.Instance.GetPlayerSaves(activeKey);
            foreach (var save in ngpSaves)
                SaveSystem.Instance.DeleteSave(Path.GetFileNameWithoutExtension(save.FileName));
            await CreateNewGame(activeKey);
            // Restore preferences that CreateNewGame defaults from CLI flags
            if (currentPlayer != null)
            {
                currentPlayer.ScreenReaderMode = preserveScreenReader;
                currentPlayer.Orientation = preserveOrientation;
            }
        }
    }

    public void _Ready()
    {

        // Initialize core systems
        InitializeGame();

        // Handle async operations properly since _Ready() can't be async
        // Wrap in try-catch to prevent silent exception swallowing
        _ = Task.Run(async () =>
        {
            try
            {
                await ShowTitleScreen();
                await MainMenu();
            }
            catch (Exception ex)
            {
                UsurperRemake.Systems.DebugLogger.Instance.LogError("CRASH", $"Fatal error in main loop:\n{ex}");
                UsurperRemake.Systems.DebugLogger.Instance.Flush();
                throw; // Re-throw to crash properly rather than hang silently
            }
        });
    }
    
    /// <summary>
    /// Initialize game systems - based on Init_Usurper procedure from Pascal
    /// </summary>
    private void InitializeGame()
    {
        // Ensure we have a working terminal instance
        if (terminal == null)
        {
            terminal = TerminalEmulator.Instance ?? new TerminalEmulator();
        }

        ReadStartCfgValues();

        // Load SysOp configuration (for BBS door mode settings persistence)
        UsurperRemake.Systems.SysOpConfigSystem.Instance.LoadConfig();
        // If --screen-reader CLI flag was passed, persist the preference for future launches
        if (GameConfig.ScreenReaderMode)
            UsurperRemake.Systems.SysOpConfigSystem.Instance.SaveConfig();

        // Create the LocationManager early so that it becomes the singleton before NPCs are loaded
        if (locationManager == null)
        {
            locationManager = new LocationManager(terminal);
        }

        InitializeItems();      // From INIT.PAS Init_Items
        InitializeMonsters();   // From INIT.PAS Init_Monsters
        InitializeNPCs();       // From INIT.PAS Init_NPCs (needs LocationManager ready)
        InitializeLevels();     // From INIT.PAS Init_Levels
        InitializeGuards();     // From INIT.PAS Init_Guards
        
        gameState = new GameState
        {
            GameDate = 1,
            LastDayRun = 0,
            PlayersOnline = 0,
            MaintenanceRunning = false
        };
        
        // Initialize remaining core systems (LocationManager already created)
        dailyManager = DailySystemManager.Instance;
        combatEngine = new CombatEngine();

        // World simulator – start background AI processing
        // In MUD/online mode, only the first session should create and start the simulator.
        // Subsequent sessions reuse the existing shared instance to avoid N background loops
        // all processing the same NPC list (which causes N× respawn messages).
        if (WorldSimulator.Instance != null && WorldSimulator.Instance.IsRunning)
        {
            worldSimulator = WorldSimulator.Instance;
        }
        else
        {
            worldSimulator = new WorldSimulator();
            worldSimulator.StartSimulation(worldNPCs ?? new List<NPC>());
        }

        // Initialize collections
        worldMonsters = new List<Monster>();
        onlinePlayers = new List<OnlinePlayer>();

        // Initialize achievement and statistics systems
        AchievementSystem.Initialize();
        QuestSystem.EnsureQuestsExist();

        // Initialize periodic check timer
        lastPeriodicCheck = DateTime.Now;

    }
    
    /// <summary>
    /// Periodic update for game systems (called regularly during gameplay)
    /// </summary>
    public async Task PeriodicUpdate()
    {
        var now = DateTime.Now;

        // Only run periodic checks every 30 seconds
        if (now - lastPeriodicCheck < TimeSpan.FromSeconds(30))
            return;

        lastPeriodicCheck = now;

        // Check for daily reset
        await dailyManager.CheckDailyReset();

        // World simulation is driven by WorldSimulator.StartSimulation() background loop.
        // Do NOT call SimulateStep() here — it causes double-ticking and in MUD mode
        // each session would double the sim rate.

        // Process NPC behaviors and maintenance
        await RunNPCMaintenanceCycle();
    }

    /// <summary>
    /// Run NPC maintenance cycle - handles NPC movement, activities, and world events
    /// </summary>
    private async Task RunNPCMaintenanceCycle()
    {
        var npcs = NPCSpawnSystem.Instance.ActiveNPCs;
        if (npcs == null || npcs.Count == 0) return;

        var random = Random.Shared;

        // Process each living NPC
        foreach (var npc in npcs.Where(n => n.IsAlive).ToList())
        {
            // 20% chance to move to a different location
            if (random.Next(5) == 0)
            {
                MoveNPCToRandomLocation(npc, random);
            }

            // Process NPC activities (shopping, healing, etc.)
            await ProcessNPCActivity(npc, random);

            // Small chance for NPC to generate news
            if (random.Next(20) == 0)
            {
                GenerateNPCNews(npc, random);
            }
        }

        // Process NPC leveling (rare)
        if (random.Next(10) == 0)
        {
            ProcessNPCLeveling(npcs, random);
        }
    }

    /// <summary>
    /// Move an NPC to a random location in town
    /// </summary>
    private void MoveNPCToRandomLocation(NPC npc, Random random)
    {
        var locations = new[]
        {
            "Main Street", "Auction House", "Inn", "Temple", "Church",
            "Weapon Shop", "Armor Shop", "Magic Shop", "Castle",
            "Bank", "Healer", "Dark Alley"
        };

        var newLocation = locations[random.Next(locations.Length)];

        // Don't log every move - too spammy
        npc.CurrentLocation = newLocation;
    }

    /// <summary>
    /// Process NPC activity based on their current situation
    /// </summary>
    private async Task ProcessNPCActivity(NPC npc, Random random)
    {
        // Heal if injured
        if (npc.HP < npc.MaxHP && random.Next(3) == 0)
        {
            long healAmount = Math.Min(npc.MaxHP / 10, npc.MaxHP - npc.HP);
            npc.HP += (int)healAmount;
        }

        // Restore mana
        if (npc.Mana < npc.MaxMana && random.Next(2) == 0)
        {
            long manaAmount = Math.Min(npc.MaxMana / 5, npc.MaxMana - npc.Mana);
            npc.Mana += (int)manaAmount;
        }

        // Shopping (if at shop and has gold)
        if (npc.Gold > 500 && random.Next(10) == 0)
        {
            // Buy equipment upgrade
            if (npc.CurrentLocation == "Weapon Shop")
            {
                int cost = random.Next(100, 500);
                if (npc.Gold >= cost)
                {
                    npc.Gold -= cost;
                    npc.WeapPow += random.Next(1, 5);
                }
            }
            else if (npc.CurrentLocation == "Armor Shop")
            {
                int cost = random.Next(100, 400);
                if (npc.Gold >= cost)
                {
                    npc.Gold -= cost;
                    npc.ArmPow += random.Next(1, 4);
                }
            }
        }

        await Task.CompletedTask;
    }

    /// <summary>
    /// Generate news about NPC activities
    /// </summary>
    private void GenerateNPCNews(NPC npc, Random random)
    {
        var newsSystem = NewsSystem.Instance;
        if (newsSystem == null) return;

        var newsItems = new List<string>();

        // Alignment-based news
        if (npc.Darkness > npc.Chivalry + 200)
        {
            newsItems.Add($"{npc.Name2} was seen lurking in the shadows");
            newsItems.Add($"{npc.Name2} threatened a merchant");
            newsItems.Add($"Guards are watching {npc.Name2} closely");
        }
        else if (npc.Chivalry > npc.Darkness + 200)
        {
            newsItems.Add($"{npc.Name2} helped a lost child find their parents");
            newsItems.Add($"{npc.Name2} donated gold to the temple");
            newsItems.Add($"{npc.Name2} protected a merchant from thieves");
        }
        else
        {
            newsItems.Add($"{npc.Name2} was seen at the {npc.CurrentLocation}");
            newsItems.Add($"{npc.Name2} is looking for adventure partners");
        }

        // Class-based news
        switch (npc.Class)
        {
            case CharacterClass.Warrior:
            case CharacterClass.Barbarian:
                newsItems.Add($"{npc.Name2} challenged someone to a duel");
                break;
            case CharacterClass.Magician:
            case CharacterClass.Sage:
                newsItems.Add($"{npc.Name2} was seen studying ancient tomes");
                break;
            case CharacterClass.Assassin:
                newsItems.Add($"Rumors swirl about {npc.Name2}'s latest target");
                break;
        }

        if (newsItems.Count > 0)
        {
            var headline = newsItems[random.Next(newsItems.Count)];
            newsSystem.Newsy(true, headline);
        }
    }

    /// <summary>
    /// Process NPC leveling based on their activities
    /// </summary>
    private void ProcessNPCLeveling(List<NPC> npcs, Random random)
    {
        // Pick a random NPC to level up
        var eligibleNPCs = npcs.Where(n => n.IsAlive && n.Level < 50).ToList();
        if (eligibleNPCs.Count == 0) return;

        var luckyNPC = eligibleNPCs[random.Next(eligibleNPCs.Count)];

        // Level up!
        luckyNPC.Level++;
        luckyNPC.Experience += luckyNPC.Level * 1000;

        // Boost stats using proper class-based increases (same as players)
        LevelMasterLocation.ApplyClassStatIncreases(luckyNPC);
        luckyNPC.RecalculateStats();
        luckyNPC.HP = luckyNPC.MaxHP;
        luckyNPC.WeapPow += random.Next(1, 3);
        luckyNPC.ArmPow += random.Next(1, 2);

        // Generate news about the level up
        var newsSystem = NewsSystem.Instance;
        if (newsSystem != null)
        {
            newsSystem.Newsy(true, $"{luckyNPC.Name2} has reached level {luckyNPC.Level}!");
        }
    }
    
    /// <summary>
    /// Show title screen - displays USURPER.ANS from Pascal
    /// </summary>
    private async Task ShowTitleScreen()
    {
        terminal.ClearScreen();
        terminal.ShowANSIArt("USURPER");
        terminal.SetColor("bright_yellow");
        terminal.WriteLine(Loc.Get("engine.title"));
        terminal.SetColor("gray");
        terminal.WriteLine(Loc.Get("engine.title_version", GameConfig.Version, GameConfig.VersionName));
        terminal.WriteLine("");
        terminal.SetColor("gray");
        terminal.WriteLine(Loc.Get("engine.title_original"));
        terminal.WriteLine(Loc.Get("engine.title_reborn"));
        terminal.WriteLine("");
        terminal.WriteLine(Loc.Get("ui.press_enter"));
        await terminal.WaitForKey();
    }
    
    private void ShowAlphaBanner()
    {
        bool compact = UsurperRemake.BBS.DoorMode.IsInDoorMode ||
            UsurperRemake.Server.SessionContext.Current?.ConnectionType == "BBS";

        if (compact)
        {
            terminal.SetColor("bright_yellow");
            terminal.WriteLine(Loc.Get("engine.alpha_compact"));
            return;
        }

        terminal.WriteLine("");
        if (GameConfig.ScreenReaderMode)
        {
            terminal.SetColor("bright_yellow");
            terminal.WriteLine(Loc.Get("engine.alpha_sr"));
            terminal.SetColor("white");
            terminal.WriteLine(Loc.Get("engine.alpha_wipe"));
            terminal.WriteLine(Loc.Get("engine.alpha_report"));
            terminal.SetColor("bright_cyan");
            terminal.WriteLine($"  {GameConfig.DiscordInvite}");
        }
        else
        {
            terminal.SetColor("bright_red");
            terminal.WriteLine("  ╔══════════════════════════════════════════════════════════════════════════╗");
            terminal.Write("  ║ ");
            terminal.SetColor("bright_yellow");
            terminal.Write(Loc.Get("engine.alpha_box_title"));
            terminal.SetColor("bright_red");
            terminal.WriteLine("  ║");
            terminal.Write("  ║ ");
            terminal.SetColor("white");
            terminal.Write(Loc.Get("engine.alpha_box_wipe"));
            terminal.SetColor("bright_red");
            terminal.WriteLine(" ║");
            terminal.Write("  ║ ");
            terminal.SetColor("white");
            terminal.Write(Loc.Get("engine.alpha_box_report"));
            terminal.SetColor("bright_red");
            terminal.WriteLine(" ║");
            terminal.Write("  ║ ");
            terminal.SetColor("bright_cyan");
            terminal.Write(GameConfig.DiscordInvite);
            terminal.SetColor("bright_red");
            terminal.WriteLine("                                                    ║");
            terminal.WriteLine("  ╚══════════════════════════════════════════════════════════════════════════╝");
        }
        terminal.WriteLine("");
    }

    /// <summary>
    /// Main menu - based on Town_Menu procedure from Pascal
    /// </summary>
    private async Task MainMenu()
    {
        bool done = false;

        while (!done)
        {
            // NG+ auto-restart: player accepted New Game+, skip menu and create fresh character
            if (PendingNewGamePlus)
            {
                PendingNewGamePlus = false;
                await CreateNewGame("");
                continue;
            }

            // Phase 3: Electron mode emits structured menu data so JS renders
            // splash + clickable menu buttons. Click sends key+\n via stdin
            // which lands at the same GetInput call below (Pattern B).
            // Emit fires unconditionally inside the helper (no-op when not in
            // Electron mode); text body below is gated on !ElectronMode.
            EmitMainMenuToElectron();

            if (!GameConfig.ElectronMode)
            {
            terminal.ClearScreen();

            // Title header
            if (GameConfig.ScreenReaderMode)
            {
                terminal.SetColor("bright_yellow");
                terminal.WriteLine(Loc.Get("engine.main_title_sr"));
            }
            else
            {
                terminal.SetColor("cyan");
                terminal.WriteLine("╔══════════════════════════════════════════════════════════════════════════════╗");
                terminal.SetColor("cyan");
                terminal.Write("║");
                terminal.SetColor("bright_yellow");
                { string t = Loc.Get("engine.main_title_box"); string s = Loc.Get("engine.main_subtitle_box"); string full = t + s; int pad = 78 - full.Length; int l = pad / 2; int r = pad - l; terminal.Write(new string(' ', l) + t); terminal.SetColor("white"); terminal.Write(s + new string(' ', r)); }
                terminal.SetColor("cyan");
                terminal.WriteLine("║");
                terminal.SetColor("cyan");
                terminal.WriteLine("╚══════════════════════════════════════════════════════════════════════════════╝");
            }
            terminal.SetColor("darkgray");
            terminal.WriteLine($"  v{GameConfig.Version} \"{GameConfig.VersionName}\"");
            ShowAlphaBanner();

            if (GameConfig.ScreenReaderMode)
            {
                // Screen reader friendly menu - plain text, no multi-color brackets
                terminal.WriteLine(Loc.Get("engine.section_play"), "darkgray");
                terminal.WriteLine($"  S. {Loc.Get("engine.main_single_player")}", "bright_green");
                if (!UsurperRemake.BBS.DoorMode.IsMudServerMode && !UsurperRemake.BBS.DoorMode.IsMudRelayMode
                    && !GameConfig.DisableOnlinePlay)
                    terminal.WriteLine(Loc.Get("engine.main_online_sr"), "bright_yellow");
                // v0.57.3: Game Editor is a local single-player tool for modding/save editing.
                // Hidden for BBS door sysops (they have their own admin tools), the MUD server
                // process itself, and MUD relay sessions (editing saves while connected to the
                // server makes no sense — the server owns the live state).
                if (!UsurperRemake.BBS.DoorMode.IsInDoorMode
                    && !UsurperRemake.BBS.DoorMode.IsMudServerMode
                    && !UsurperRemake.BBS.DoorMode.IsMudRelayMode)
                    terminal.WriteLine($"  G. {Loc.Get("engine.main_editor")}", "bright_magenta");
                terminal.WriteLine("");

                terminal.WriteLine(Loc.Get("engine.section_info"), "darkgray");
                terminal.WriteLine(Loc.Get("engine.main_story_sr"), "white");
                terminal.WriteLine(Loc.Get("engine.main_history_sr"), "white");
                terminal.WriteLine(Loc.Get("engine.main_bbs_sr"), "white");
                terminal.WriteLine(Loc.Get("engine.main_credits_sr"), "white");
#if !STEAM_BUILD
                terminal.WriteLine(Loc.Get("engine.main_support_sr"), "bright_yellow");
#endif
                terminal.WriteLine("");

                terminal.WriteLine(Loc.Get("engine.section_accessibility"), "darkgray");
                terminal.WriteLine(Loc.Get("engine.main_sr_on"), "bright_green");
                terminal.WriteLine(GameConfig.CompactMode
                    ? Loc.Get("engine.main_compact_on_sr")
                    : Loc.Get("engine.main_compact_off_sr"), GameConfig.CompactMode ? "bright_green" : "white");
                if (BaseLocation.IsRunningInWezTerm())
                    terminal.WriteLine(Loc.Get("engine.main_font_sr", BaseLocation.ReadCurrentFont()), "white");
                terminal.WriteLine($"  L. {Loc.Get("engine.main_language_sr", UsurperRemake.Systems.Loc.GetLanguageName(GameConfig.Language ?? "en"))}", "white");
                terminal.WriteLine("");

                terminal.WriteLine(Loc.Get("engine.main_quit_sr"), "gray");

                if (UsurperRemake.BBS.DoorMode.IsInDoorMode && UsurperRemake.BBS.DoorMode.IsSysOp)
                    terminal.WriteLine(Loc.Get("engine.main_sysop_sr"), "yellow");
            }
            else
            {
                // Visual menu with colored brackets
                // PLAY section
                terminal.SetColor("darkgray");
                { string t = Loc.Get("engine.section_play_visual"); terminal.WriteLine(t + new string('─', 78 - t.Length)); }
                terminal.SetColor("darkgray");
                terminal.Write("  [");
                terminal.SetColor("bright_cyan");
                terminal.Write("S");
                terminal.SetColor("darkgray");
                terminal.Write("] ");
                terminal.SetColor("bright_green");
                terminal.WriteLine(Loc.Get("engine.main_single_player"));

                // Online Multiplayer - hidden when already on the server, or when SysOp disabled it
                if (!UsurperRemake.BBS.DoorMode.IsMudServerMode && !UsurperRemake.BBS.DoorMode.IsMudRelayMode
                    && !GameConfig.DisableOnlinePlay)
                {
                    terminal.SetColor("darkgray");
                    terminal.Write("  [");
                    terminal.SetColor("bright_yellow");
                    terminal.Write("O");
                    terminal.SetColor("darkgray");
                    terminal.Write("] ");
                    terminal.SetColor("bright_yellow");
                    terminal.WriteLine(Loc.Get("engine.menu_online_full"));
                }

                // v0.57.3: Game Editor — only shown in local single-player / Steam contexts.
                // Never shown inside a BBS door session (sysops already have their own tools)
                // or when this process IS the MUD server / a relay client to one (editing a
                // local save while connected to a shared server would just confuse things).
                if (!UsurperRemake.BBS.DoorMode.IsInDoorMode
                    && !UsurperRemake.BBS.DoorMode.IsMudServerMode
                    && !UsurperRemake.BBS.DoorMode.IsMudRelayMode)
                {
                    terminal.SetColor("darkgray");
                    terminal.Write("  [");
                    terminal.SetColor("bright_magenta");
                    terminal.Write("G");
                    terminal.SetColor("darkgray");
                    terminal.Write("] ");
                    terminal.SetColor("bright_magenta");
                    terminal.WriteLine(Loc.Get("engine.menu_editor_full"));
                }

                terminal.WriteLine("");

                // INFO section
                terminal.SetColor("darkgray");
                { string t = Loc.Get("engine.section_info_visual"); terminal.WriteLine(t + new string('─', 78 - t.Length)); }
                terminal.SetColor("darkgray");
                terminal.Write("  [");
                terminal.SetColor("bright_cyan");
                terminal.Write("I");
                terminal.SetColor("darkgray");
                terminal.Write("] ");
                terminal.SetColor("white");
                terminal.WriteLine(Loc.Get("engine.menu_story_full"));

                terminal.SetColor("darkgray");
                terminal.Write("  [");
                terminal.SetColor("bright_cyan");
                terminal.Write("H");
                terminal.SetColor("darkgray");
                terminal.Write("] ");
                terminal.SetColor("white");
                terminal.WriteLine(Loc.Get("engine.menu_history_full"));

                terminal.SetColor("darkgray");
                terminal.Write("  [");
                terminal.SetColor("bright_cyan");
                terminal.Write("B");
                terminal.SetColor("darkgray");
                terminal.Write("] ");
                terminal.SetColor("white");
                terminal.WriteLine(Loc.Get("engine.menu_bbs_full"));

                terminal.SetColor("darkgray");
                terminal.Write("  [");
                terminal.SetColor("bright_cyan");
                terminal.Write("C");
                terminal.SetColor("darkgray");
                terminal.Write("] ");
                terminal.SetColor("gray");
                terminal.WriteLine(Loc.Get("engine.menu_credits"));


#if !STEAM_BUILD
                terminal.SetColor("darkgray");
                terminal.Write("  [");
                terminal.SetColor("bright_yellow");
                terminal.Write("@");
                terminal.SetColor("darkgray");
                terminal.Write("] ");
                terminal.SetColor("bright_yellow");
                terminal.WriteLine(Loc.Get("engine.menu_support_full"));
#endif

                terminal.WriteLine("");

                // Accessibility section
                terminal.SetColor("darkgray");
                { string t = Loc.Get("engine.section_accessibility_visual"); terminal.WriteLine(t + new string('─', 78 - t.Length)); }
                terminal.SetColor("darkgray");
                terminal.Write("  [");
                terminal.SetColor("bright_cyan");
                terminal.Write("A");
                terminal.SetColor("darkgray");
                terminal.Write("] ");
                terminal.SetColor("white");
                terminal.WriteLine(Loc.Get("engine.main_sr_off_visual"));

                terminal.SetColor("darkgray");
                terminal.Write("  [");
                terminal.SetColor("bright_cyan");
                terminal.Write("Z");
                terminal.SetColor("darkgray");
                terminal.Write("] ");
                if (GameConfig.CompactMode)
                {
                    terminal.SetColor("bright_green");
                    terminal.WriteLine(Loc.Get("engine.main_compact_on_visual"));
                }
                else
                {
                    terminal.SetColor("white");
                    terminal.WriteLine(Loc.Get("engine.main_compact_off_visual"));
                }

                terminal.SetColor("darkgray");
                terminal.Write("  [");
                terminal.SetColor("bright_cyan");
                terminal.Write("L");
                terminal.SetColor("darkgray");
                terminal.Write("] ");
                terminal.SetColor("white");
                terminal.WriteLine(Loc.Get("engine.main_language_visual", UsurperRemake.Systems.Loc.GetLanguageName(GameConfig.Language ?? "en")));

                if (BaseLocation.IsRunningInWezTerm())
                {
                    terminal.SetColor("darkgray");
                    terminal.Write("  [");
                    terminal.SetColor("bright_cyan");
                    terminal.Write("F");
                    terminal.SetColor("darkgray");
                    terminal.Write("] ");
                    terminal.SetColor("white");
                    terminal.WriteLine(Loc.Get("engine.main_font_visual", BaseLocation.ReadCurrentFont()));
                }

                terminal.WriteLine("");
                terminal.SetColor("darkgray");
                terminal.Write("  [");
                terminal.SetColor("red");
                terminal.Write("Q");
                terminal.SetColor("darkgray");
                terminal.Write("] ");
                terminal.SetColor("gray");
                terminal.WriteLine(Loc.Get("engine.menu_quit"));

                // SysOp option - only visible in BBS door mode for SysOps
                if (UsurperRemake.BBS.DoorMode.IsInDoorMode && UsurperRemake.BBS.DoorMode.IsSysOp)
                {
                    terminal.WriteLine("");
                    terminal.SetColor("darkgray");
                    terminal.Write("  [");
                    terminal.SetColor("bright_yellow");
                    terminal.Write("%");
                    terminal.SetColor("darkgray");
                    terminal.Write("] ");
                    terminal.SetColor("yellow");
                    terminal.WriteLine(Loc.Get("engine.menu_sysop_console"));
                }
            }

            terminal.WriteLine("");
            terminal.SetColor("bright_white");
            } // end if (!GameConfig.ElectronMode) — Electron mode skipped the entire text body above
            var choice = await terminal.GetInput(GameConfig.ElectronMode ? "" : Loc.Get("ui.your_choice"));

            switch (choice.ToUpper())
            {
                case "S":
                case "E": // Legacy fallback
                    await EnterGame();
                    break;
                case "G":
                    // v0.57.3: Launch the standalone game editor without requiring the user to
                    // re-launch with --editor. Locked behind the same single-player/Steam
                    // eligibility as the menu item itself so accidentally hitting G during a
                    // BBS door session or on the live MUD server just does nothing.
                    if (!UsurperRemake.BBS.DoorMode.IsInDoorMode
                        && !UsurperRemake.BBS.DoorMode.IsMudServerMode
                        && !UsurperRemake.BBS.DoorMode.IsMudRelayMode)
                    {
                        // Save and restore console state so the editor's plain Console.ReadLine/
                        // Write calls don't leave the terminal in a weird color / cursor state
                        // when we return to the animated menu.
                        var prevForeground = Console.ForegroundColor;
                        terminal.ClearScreen();
                        try
                        {
                            await UsurperRemake.Editor.EditorMain.RunAsync(System.Array.Empty<string>());
                        }
                        catch (System.Exception ex)
                        {
                            terminal.WriteLine($"Editor error: {ex.Message}", "red");
                            await System.Threading.Tasks.Task.Delay(2000);
                        }
                        finally
                        {
                            Console.ForegroundColor = prevForeground;
                        }
                    }
                    break;
                case "I":
                    await ShowInstructions();
                    break;
                case "H":
                    await UsurperHistorySystem.Instance.ShowHistory(terminal);
                    break;
                case "B":
                    await ShowBBSList();
                    break;
                case "C":
                    await ShowCredits();
                    break;
                case "A":
                    GameConfig.ScreenReaderMode = !GameConfig.ScreenReaderMode;
                    UsurperRemake.Systems.SysOpConfigSystem.Instance.SaveConfig();
                    terminal.WriteLine("");
                    if (GameConfig.ScreenReaderMode)
                    {
                        terminal.WriteLine(Loc.Get("engine.sr_enabled"), "bright_green");
                        terminal.WriteLine(Loc.Get("engine.sr_enabled_desc"), "white");
                    }
                    else
                    {
                        terminal.WriteLine(Loc.Get("engine.sr_disabled"), "white");
                        terminal.WriteLine(Loc.Get("engine.sr_disabled_desc"), "white");
                    }
                    await Task.Delay(1500);
                    break;
                case "Z":
                    GameConfig.CompactMode = !GameConfig.CompactMode;
                    terminal.WriteLine("");
                    if (GameConfig.CompactMode)
                    {
                        terminal.WriteLine(Loc.Get("engine.compact_enabled"), "bright_green");
                        terminal.WriteLine(Loc.Get("engine.compact_enabled_desc"), "white");
                    }
                    else
                    {
                        terminal.WriteLine(Loc.Get("engine.compact_disabled"), "white");
                        terminal.WriteLine(Loc.Get("engine.compact_disabled_desc"), "white");
                    }
                    await Task.Delay(1500);
                    break;
                case "F":
                    if (BaseLocation.IsRunningInWezTerm())
                    {
                        var fonts = new[] { "JetBrains Mono", "Cascadia Code", "Fira Code", "Iosevka", "Hack", "Kelmscott Mono" };
                        var currentFont = BaseLocation.ReadCurrentFont();
                        int idx = Array.IndexOf(fonts, currentFont);
                        int next = (idx + 1) % fonts.Length;
                        BaseLocation.WriteTerminalFont(fonts[next]);
                        terminal.WriteLine("");
                        terminal.WriteLine(Loc.Get("engine.font_set", fonts[next]), "green");
                        terminal.WriteLine(Loc.Get("engine.font_update"), "white");
                        await Task.Delay(800);
                    }
                    break;
                case "L":
                    terminal.WriteLine("");
                    terminal.WriteLine(Loc.Get("prefs.select_language"), "bright_yellow");
                    terminal.WriteLine("");
                    var mainLangs = UsurperRemake.Systems.Loc.AvailableLanguages;
                    for (int li = 0; li < mainLangs.Length; li++)
                    {
                        var marker = mainLangs[li].Code == (GameConfig.Language ?? "en") ? " *" : "";
                        terminal.WriteLine($"  {li + 1}. {mainLangs[li].Name}{marker}");
                    }
                    terminal.WriteLine("");
                    var mainLangChoice = await terminal.GetInput(Loc.Get("ui.your_choice"));
                    if (int.TryParse(mainLangChoice.Trim(), out int mainLangIdx) && mainLangIdx >= 1 && mainLangIdx <= mainLangs.Length)
                    {
                        var selectedLang = mainLangs[mainLangIdx - 1].Code;
                        GameConfig.Language = selectedLang;
                        _languageSetThisSession.Value = true;
                        terminal.WriteLine(Loc.Get("prefs.language_set", UsurperRemake.Systems.Loc.GetLanguageName(selectedLang)), "green");
                        await Task.Delay(800);
                    }
                    break;
                case "Q":
                    IsIntentionalExit = true;
                    done = true;
                    break;
                case "O":
                    if (!UsurperRemake.BBS.DoorMode.IsMudServerMode && !UsurperRemake.BBS.DoorMode.IsMudRelayMode
                        && !GameConfig.DisableOnlinePlay)
                    {
                        var onlinePlay = new UsurperRemake.Systems.OnlinePlaySystem(terminal);
                        await onlinePlay.StartOnlinePlay();
                    }
                    break;
                case "%":
                    if (UsurperRemake.BBS.DoorMode.IsInDoorMode && UsurperRemake.BBS.DoorMode.IsSysOp)
                    {
                        await ShowSysOpConsole();
                    }
                    break;
#if !STEAM_BUILD
                case "@":
                    await ShowSupportPage();
                    break;
#endif
            }
        }
    }

    /// <summary>
    /// Phase 3: emit the main menu state for the Electron graphical client.
    /// JS renders the splash + clickable menu buttons. Click sends key+\n
    /// via stdin which lands at MainMenu's GetInput call. No-op when not in
    /// Electron mode. Item-visibility logic mirrors the text-rendering
    /// branches above so graphical and text menus always agree.
    /// </summary>
    private void EmitMainMenuToElectron()
    {
        if (!GameConfig.ElectronMode) return;

        var items = new List<ElectronBridge.MenuItemData>
        {
            new() { Key = "S", Label = Loc.Get("engine.main_single_player"), Category = "play" }
        };
        if (!UsurperRemake.BBS.DoorMode.IsMudServerMode
            && !UsurperRemake.BBS.DoorMode.IsMudRelayMode
            && !GameConfig.DisableOnlinePlay)
            items.Add(new() { Key = "O", Label = "Online Multiplayer", Category = "play" });
        if (!UsurperRemake.BBS.DoorMode.IsInDoorMode
            && !UsurperRemake.BBS.DoorMode.IsMudServerMode
            && !UsurperRemake.BBS.DoorMode.IsMudRelayMode)
            items.Add(new() { Key = "G", Label = Loc.Get("engine.main_editor"), Category = "tools" });
        items.Add(new() { Key = "I", Label = "Story", Category = "info" });
        items.Add(new() { Key = "H", Label = "History", Category = "info" });
        items.Add(new() { Key = "B", Label = "BBS List", Category = "info" });
        items.Add(new() { Key = "C", Label = "Credits", Category = "info" });
        items.Add(new() { Key = "L", Label = $"Language: {UsurperRemake.Systems.Loc.GetLanguageName(GameConfig.Language ?? "en")}", Category = "settings" });
        items.Add(new() { Key = "Z", Label = GameConfig.CompactMode ? "Compact Mode: ON" : "Compact Mode: OFF", Category = "settings" });
        items.Add(new() { Key = "A", Label = GameConfig.ScreenReaderMode ? "Screen Reader: ON" : "Screen Reader: OFF", Category = "accessibility" });
        items.Add(new() { Key = "Q", Label = "Quit", Category = "exit" });
        if (UsurperRemake.BBS.DoorMode.IsInDoorMode && UsurperRemake.BBS.DoorMode.IsSysOp)
            items.Add(new() { Key = "%", Label = "SysOp Console", Category = "tools" });

        ElectronBridge.EmitMainMenu(
            title: Loc.Get("engine.main_title_box").Trim(),
            subtitle: Loc.Get("engine.main_subtitle_box").Trim(),
            version: $"v{GameConfig.Version} \"{GameConfig.VersionName}\"",
            items: items);
    }

    /// <summary>
    /// Phase 3: emit the save selection state for the Electron graphical client.
    /// JS renders save slots as cards with character info, last-played, and
    /// [RECOVERY] / [EMERGENCY SAVE] tags styled distinctly. Click sends the
    /// numeric slot key (or N/B) via stdin which lands at EnterGame's GetInput.
    /// No-op when not in Electron mode.
    /// </summary>
    private void EmitSaveListToElectron(List<string> playerNames)
    {
        if (!GameConfig.ElectronMode) return;

        var slots = new List<ElectronBridge.SaveSlotData>();
        for (int i = 0; i < playerNames.Count; i++)
        {
            var name = playerNames[i];
            var save = SaveSystem.Instance.GetMostRecentSave(name);
            if (save == null) continue;
            slots.Add(new ElectronBridge.SaveSlotData
            {
                SlotKey = (i + 1).ToString(),
                CharacterName = save.PlayerName,
                Level = save.Level,
                ClassName = save.ClassName ?? "",
                RaceName = "", // SaveInfo doesn't carry race; loading the save would, but cheap listing skips it
                IsImmortal = false, // same — flag derives from full save load
                LastPlayed = save.SaveTime.Year >= 2020 ? save.SaveTime.ToString("o") : null,
                IsRecovered = save.IsRecovered,
                IsEmergency = save.IsEmergency,
                IsAlt = false,
            });
        }

        var actions = new List<ElectronBridge.MenuItemData>
        {
            new() { Key = "N", Label = Loc.Get("engine.new_character"), Category = "create" },
            new() { Key = "B", Label = Loc.Get("engine.back_to_menu"), Category = "navigate" },
        };

        ElectronBridge.EmitSaveList(
            accountName: UsurperRemake.Server.SessionContext.Current?.Username ?? "",
            slots: slots,
            actions: actions);
    }

    /// <summary>
    /// SysOp Administration Console - accessible from main menu before any save is loaded.
    /// This allows SysOps to manage the game without affecting player state.
    /// </summary>
    private async Task ShowSysOpConsole()
    {
        var sysopConsole = new SysOpConsoleManager(terminal);
        await sysopConsole.Run();
    }

    /// <summary>
    /// Online Admin Console - accessible from character selection when authenticated as admin.
    /// Manages players, settings, and world state via SqlSaveBackend.
    /// </summary>
    private async Task ShowOnlineAdminConsole()
    {
        var sqlBackend = SaveSystem.Instance.Backend as SqlSaveBackend;
        if (sqlBackend == null)
        {
            terminal.SetColor("red");
            terminal.WriteLine(Loc.Get("engine.admin_requires_online"));
            await terminal.GetInputAsync(Loc.Get("ui.press_enter"));
            return;
        }
        var adminConsole = new OnlineAdminConsole(terminal, sqlBackend);
        await adminConsole.Run();
    }

    /// <summary>
    /// Change password screen - accessible from character selection in online mode.
    /// Requires current password for verification before allowing change.
    /// </summary>
    private async Task ChangePasswordScreen()
    {
        var sqlBackend = SaveSystem.Instance.Backend as SqlSaveBackend;
        if (sqlBackend == null)
        {
            terminal.SetColor("red");
            terminal.WriteLine(Loc.Get("engine.password_requires_online"));
            await terminal.GetInputAsync(Loc.Get("ui.press_enter"));
            return;
        }

        terminal.ClearScreen();
        terminal.SetColor("bright_cyan");
        terminal.WriteLine(GameConfig.ScreenReaderMode ? Loc.Get("engine.change_password_title") : Loc.Get("engine.change_password_title_visual"));
        terminal.WriteLine("");

        var username = UsurperRemake.BBS.DoorMode.OnlineUsername;
        if (string.IsNullOrEmpty(username))
        {
            terminal.SetColor("red");
            terminal.WriteLine(Loc.Get("engine.no_username"));
            await terminal.GetInputAsync(Loc.Get("ui.press_enter"));
            return;
        }

        terminal.SetColor("white");
        terminal.WriteLine(Loc.Get("engine.account_label", username));
        terminal.WriteLine("");

        var currentPassword = await terminal.GetMaskedInput(Loc.Get("engine.current_password_prompt"));
        if (string.IsNullOrWhiteSpace(currentPassword))
        {
            terminal.SetColor("yellow");
            terminal.WriteLine(Loc.Get("ui.cancelled"));
            await Task.Delay(1000);
            return;
        }

        var newPassword = await terminal.GetMaskedInput(Loc.Get("engine.new_password_prompt"));
        if (string.IsNullOrWhiteSpace(newPassword) || newPassword.Length < 4)
        {
            terminal.SetColor("red");
            terminal.WriteLine(Loc.Get("engine.password_min_length"));
            await terminal.GetInputAsync(Loc.Get("ui.press_enter"));
            return;
        }

        var confirmPassword = await terminal.GetMaskedInput(Loc.Get("engine.confirm_password_prompt"));
        if (newPassword != confirmPassword)
        {
            terminal.SetColor("red");
            terminal.WriteLine(Loc.Get("engine.passwords_no_match"));
            await terminal.GetInputAsync(Loc.Get("ui.press_enter"));
            return;
        }

        var (success, message) = await sqlBackend.ChangePassword(username, currentPassword, newPassword);
        terminal.SetColor(success ? "bright_green" : "red");
        terminal.WriteLine(message);
        await terminal.GetInputAsync(Loc.Get("ui.press_enter"));
    }

    // ═══════════════════════════════════════════════════════════════════
    // SPECTATOR MODE
    // ═══════════════════════════════════════════════════════════════════

    /// <summary>
    /// Spectator mode UI: select a player to watch, request consent, enter spectator loop.
    /// Called from the main menu before loading a character.
    /// </summary>
    private async Task RunSpectatorModeUI()
    {
        var server = UsurperRemake.Server.MudServer.Instance;
        if (server == null) return;

        var ctx = UsurperRemake.Server.SessionContext.Current;
        if (ctx == null) return;

        var myUsername = ctx.Username;
        var mySession = server.ActiveSessions.TryGetValue(myUsername.ToLowerInvariant(), out var s) ? s : null;
        if (mySession == null) return;

        terminal.ClearScreen();
        if (GameConfig.ScreenReaderMode)
        {
            terminal.WriteLine(Loc.Get("engine.spectator_title"), "bright_white");
        }
        else
        {
            terminal.SetColor("bright_magenta");
            terminal.WriteLine("╔══════════════════════════════════════════════════════════════════════════════╗");
            terminal.Write("║");
            terminal.SetColor("bright_white");
            { string t = Loc.Get("engine.spectator_title"); int l = (78 - t.Length) / 2; int r = 78 - t.Length - l; terminal.Write(new string(' ', l) + t + new string(' ', r)); }
            terminal.SetColor("bright_magenta");
            terminal.WriteLine("║");
            terminal.WriteLine("╚══════════════════════════════════════════════════════════════════════════════╝");
        }
        terminal.WriteLine("");

        // Get list of in-game players (exclude self, invisible wizards, other spectators)
        var candidates = server.ActiveSessions.Values
            .Where(p => p.IsInGame && !p.IsSpectating
                        && !p.Username.Equals(myUsername, StringComparison.OrdinalIgnoreCase)
                        && !p.IsWizInvisible)
            .OrderBy(p => p.Username)
            .ToList();

        if (candidates.Count == 0)
        {
            terminal.SetColor("gray");
            terminal.WriteLine(Loc.Get("engine.no_players_to_watch"));
            terminal.WriteLine("");
            await terminal.PressAnyKey();
            return;
        }

        terminal.SetColor("bright_cyan");
        terminal.WriteLine(Loc.Get("engine.players_to_watch"));
        terminal.WriteLine("");

        for (int i = 0; i < candidates.Count; i++)
        {
            var p = candidates[i];
            var spectatorCount = p.Spectators.Count;
            var watching = spectatorCount > 0 ? Loc.Get("engine.watching_count", spectatorCount) : "";
            if (GameConfig.ScreenReaderMode)
            {
                terminal.SetColor("white");
                terminal.WriteLine($"  {i + 1}. {p.Username} ({p.ConnectionType}){watching}");
            }
            else
            {
                terminal.SetColor("bright_yellow");
                terminal.Write($"  [{i + 1}] ");
                terminal.SetColor("white");
                terminal.WriteLine($"{p.Username} [{p.ConnectionType}]{watching}");
            }
        }

        terminal.WriteLine("");
        terminal.SetColor("gray");
        terminal.WriteLine(Loc.Get("engine.spectator_consent"));
        terminal.WriteLine("");

        var input = await terminal.GetInput(Loc.Get("engine.spectator_select"));
        if (string.IsNullOrWhiteSpace(input) || input.Trim().ToUpper() == "Q")
            return;

        if (!int.TryParse(input.Trim(), out int selection) || selection < 1 || selection > candidates.Count)
        {
            terminal.SetColor("red");
            terminal.WriteLine(Loc.Get("engine.spectator_invalid"));
            await Task.Delay(1500);
            return;
        }

        var target = candidates[selection - 1];

        // Check if target already has a pending request
        if (target.PendingSpectateRequest != null && !target.PendingSpectateRequest.IsExpired)
        {
            terminal.SetColor("yellow");
            terminal.WriteLine(Loc.Get("engine.spectator_pending"));
            await Task.Delay(2000);
            return;
        }

        // Send the spectate request
        var request = new UsurperRemake.Server.SpectateRequest { Requester = mySession };
        target.PendingSpectateRequest = request;

        // Phase 8: emit graphical spectate request to the target's Electron
        // client. Dormant until Phase 9 wires online-Electron.
        if (GameConfig.ElectronMode)
        {
            ElectronBridge.EmitSpectateRequest(myUsername, timeoutSeconds: 60);
        }

        // Notify the target
        target.EnqueueMessage(
            $"\u001b[1;35m  * {myUsername} wants to watch your session (Spectator Mode).\u001b[0m");
        target.EnqueueMessage(
            $"\u001b[1;35m  * Type /accept to allow or /deny to refuse.\u001b[0m");

        terminal.SetColor("bright_cyan");
        terminal.WriteLine(Loc.Get("engine.spectator_sent", target.Username));
        terminal.WriteLine(Loc.Get("engine.spectator_waiting"));

        // Wait for response with timeout
        bool accepted;
        try
        {
            var responseTask = request.Response.Task;
            var timeoutTask = Task.Delay(TimeSpan.FromSeconds(60));
            var completedTask = await Task.WhenAny(responseTask, timeoutTask);

            if (completedTask == responseTask)
            {
                accepted = responseTask.Result;
            }
            else
            {
                accepted = false;
                request.Response.TrySetResult(false);
                target.PendingSpectateRequest = null;
            }
        }
        catch (Exception ex)
        {
            DebugLogger.Instance.LogError("ENGINE", $"[RunSpectatorModeUI] Spectate request handling failed: {ex.Message}");
            accepted = false;
            request.Response.TrySetResult(false);
            target.PendingSpectateRequest = null;
        }

        if (!accepted)
        {
            terminal.SetColor("yellow");
            terminal.WriteLine(Loc.Get("engine.spectator_denied"));
            await Task.Delay(2000);
            return;
        }

        // Accepted — enter spectator mode
        terminal.SetColor("bright_green");
        terminal.WriteLine(Loc.Get("engine.spectator_accepted", target.Username));
        terminal.WriteLine("");
        await Task.Delay(1000);

        await RunSpectatorLoop(mySession, target);
    }

    /// <summary>
    /// The spectator viewing loop. Registers as a spectator on the target's terminal,
    /// then waits in a simple input loop until the spectator types Q or the target disconnects.
    /// </summary>
    private async Task RunSpectatorLoop(
        UsurperRemake.Server.PlayerSession mySession,
        UsurperRemake.Server.PlayerSession targetSession)
    {
        // Register as spectator
        targetSession.Spectators.Add(mySession);
        mySession.SpectatingSession = targetSession;
        mySession.IsSpectating = true;

        // Update online location to show spectating status
        var ctx = UsurperRemake.Server.SessionContext.Current;
        ctx?.OnlineState?.UpdateLocation($"Spectating {targetSession.Username}");

        // Add our terminal to the target's terminal for output forwarding
        if (targetSession.Context?.Terminal != null)
        {
            targetSession.Context.Terminal.AddSpectatorStream(terminal);
        }

        // Disable message pump so forwarded output isn't interleaved with prompt redraws
        var savedMessageSource = terminal.MessageSource;
        terminal.MessageSource = null;

        // Notify the target
        targetSession.EnqueueMessage(
            $"\u001b[1;33m  * {mySession.Username} is now watching your session.\u001b[0m");

        // Show spectator header
        terminal.ClearScreen();
        if (!GameConfig.ScreenReaderMode)
        {
            terminal.SetColor("bright_magenta");
            terminal.WriteLine("  ══════════════════════════════════════════════════════════════════════════");
        }
        terminal.SetColor("bright_white");
        terminal.WriteLine(Loc.Get("engine.spectating_label", targetSession.Username));
        if (!GameConfig.ScreenReaderMode)
        {
            terminal.SetColor("bright_magenta");
            terminal.WriteLine("  ══════════════════════════════════════════════════════════════════════════");
        }
        terminal.SetColor("gray");
        terminal.WriteLine(Loc.Get("engine.spectator_quit_hint"));
        terminal.WriteLine("");

        // Spectator input loop
        try
        {
            while (true)
            {
                var input = await terminal.GetInput("");

                if (!string.IsNullOrEmpty(input) && input.Trim().ToUpper() == "Q")
                    break;

                // Check if target is still connected
                if (targetSession.Context == null ||
                    !UsurperRemake.Server.MudServer.Instance!.ActiveSessions.ContainsKey(
                        targetSession.Username.ToLowerInvariant()))
                {
                    terminal.WriteLine("");
                    terminal.SetColor("yellow");
                    terminal.WriteLine(Loc.Get("engine.spectator_disconnected"));
                    break;
                }

                // Keep the session alive (update activity time)
                mySession.LastActivityTime = DateTime.UtcNow;
            }
        }
        catch (Exception ex)
        {
            // Spectator disconnected or error
            DebugLogger.Instance.LogError("ENGINE", $"[RunSpectatorLoop] Spectator session error: {ex.Message}");
        }
        finally
        {
            // Cleanup
            targetSession.Spectators.Remove(mySession);
            targetSession.Context?.Terminal?.RemoveSpectatorStream(terminal);
            mySession.SpectatingSession = null;
            mySession.IsSpectating = false;
            terminal.MessageSource = savedMessageSource;

            // Notify target (if still connected)
            try
            {
                if (UsurperRemake.Server.MudServer.Instance?.ActiveSessions.ContainsKey(
                    targetSession.Username.ToLowerInvariant()) == true)
                {
                    targetSession.EnqueueMessage(
                        $"\u001b[1;33m  * {mySession.Username} stopped watching your session.\u001b[0m");
                }
            }
            catch (Exception ex) { DebugLogger.Instance.LogError("ENGINE", $"[RunSpectatorLoop] Failed to notify spectate target on disconnect: {ex.Message}"); }
        }

        terminal.SetColor("cyan");
        terminal.WriteLine("");
        terminal.WriteLine(Loc.Get("engine.spectator_ended"));
        await Task.Delay(1500);
    }

    /// <summary>
    /// Enter the game with modern save/load UI
    /// </summary>
    private async Task EnterGame()
    {
        while (true)
        {
            // Get all unique player names. Done unconditionally — used by both
            // render paths and the no-saves fast-path.
            var playerNames = SaveSystem.Instance.GetAllPlayerNames();

            if (playerNames.Count == 0)
            {
                // No saves exist - go directly to character creation. In
                // Electron mode the char-create screen takes over fully; in
                // text mode we briefly explain what's happening first.
                if (!GameConfig.ElectronMode)
                {
                    terminal.ClearScreen();
                    terminal.SetColor("yellow");
                    terminal.WriteLine(Loc.Get("engine.no_saves_found"));
                    terminal.WriteLine("");
                    terminal.SetColor("white");
                    terminal.WriteLine(Loc.Get("engine.create_character_prompt"));
                    terminal.WriteLine("");
                }
                await CreateNewGame("");
                return;
            }

            // Phase 3: Electron mode emits the save list as structured data
            // for the JS-side save selection screen. Text body below is gated
            // on !ElectronMode so the same GetInput call serves both modes.
            EmitSaveListToElectron(playerNames);

            if (!GameConfig.ElectronMode)
            {
            terminal.ClearScreen();
            if (GameConfig.ScreenReaderMode)
            {
                terminal.SetColor("bright_yellow");
                terminal.WriteLine(Loc.Get("engine.save_management_title"));
            }
            else
            {
                terminal.SetColor("bright_cyan");
                terminal.WriteLine("╔══════════════════════════════════════════════════════════════════════════════╗");
                terminal.Write("║");
                terminal.SetColor("bright_yellow");
                { string t = Loc.Get("engine.save_management_title"); int l = (78 - t.Length) / 2, r = 78 - t.Length - l; terminal.Write(new string(' ', l) + t + new string(' ', r)); }
                terminal.SetColor("bright_cyan");
                terminal.WriteLine("║");
                terminal.WriteLine("╚══════════════════════════════════════════════════════════════════════════════╝");
            }
            terminal.WriteLine("");

            // Show existing save slots
            terminal.SetColor("bright_green");
            terminal.WriteLine(Loc.Get("engine.existing_saves"));
            terminal.WriteLine("");

            for (int i = 0; i < playerNames.Count; i++)
            {
                var playerName = playerNames[i];
                var mostRecentSave = SaveSystem.Instance.GetMostRecentSave(playerName);

                if (mostRecentSave != null)
                {
                    // v0.57.18: surface IsRecovered / IsEmergency status so the player
                    // knows the slot will route to the recovery menu rather than load
                    // normally. Saves will be flagged when the listing fell back to
                    // filename-only metadata (the JSON deserialize threw — likely a
                    // bloated save) or when the only file on disk is an emergency dump.
                    string statusTag = mostRecentSave.IsEmergency ? " [EMERGENCY SAVE]"
                        : mostRecentSave.IsRecovered ? " [RECOVERY]"
                        : "";
                    string statusColor = mostRecentSave.IsEmergency ? "bright_red"
                        : mostRecentSave.IsRecovered ? "yellow"
                        : "";

                    if (GameConfig.ScreenReaderMode)
                    {
                        terminal.SetColor("white");
                        string saveTimeStr = mostRecentSave.SaveTime.Year >= 2020 ? mostRecentSave.SaveTime.ToString("yyyy-MM-dd HH:mm:ss") : "";
                        terminal.WriteLine(Loc.Get("engine.save_slot_sr_display", i + 1, mostRecentSave.PlayerName, mostRecentSave.ClassName, mostRecentSave.Level, mostRecentSave.SaveType, saveTimeStr) + statusTag);
                    }
                    else
                    {
                        terminal.SetColor("darkgray");
                        terminal.Write($"[");
                        terminal.SetColor("bright_cyan");
                        terminal.Write($"{i + 1}");
                        terminal.SetColor("darkgray");
                        terminal.Write("] ");
                        terminal.SetColor("white");
                        terminal.Write($"{mostRecentSave.PlayerName}");
                        if (!string.IsNullOrEmpty(statusTag))
                        {
                            terminal.SetColor(statusColor);
                            terminal.Write(statusTag);
                        }
                        else
                        {
                            terminal.SetColor("cyan");
                            terminal.Write($" ({mostRecentSave.ClassName})");
                        }
                        terminal.SetColor("gray");
                        terminal.Write(Loc.Get("engine.save_slot_level", mostRecentSave.Level));
                        terminal.SetColor("darkgray");
                        terminal.Write(" | ");
                        terminal.SetColor(mostRecentSave.IsAutosave ? "yellow" : "green");
                        terminal.Write(mostRecentSave.SaveType);
                        terminal.SetColor("darkgray");
                        terminal.Write(" | ");
                        terminal.SetColor("gray");
                        terminal.WriteLine(mostRecentSave.SaveTime.ToString("yyyy-MM-dd HH:mm:ss"));
                    }
                }
            }

            terminal.WriteLine("");
            if (GameConfig.ScreenReaderMode)
            {
                terminal.WriteLine($"N. {Loc.Get("engine.new_character")}", "green");
                terminal.WriteLine($"B. {Loc.Get("engine.back_to_menu")}", "red");
            }
            else
            {
                terminal.SetColor("darkgray");
                terminal.Write("[");
                terminal.SetColor("bright_yellow");
                terminal.Write("N");
                terminal.SetColor("darkgray");
                terminal.Write("] ");
                terminal.SetColor("green");
                terminal.WriteLine(Loc.Get("engine.new_character"));

                terminal.SetColor("darkgray");
                terminal.Write("[");
                terminal.SetColor("bright_yellow");
                terminal.Write("B");
                terminal.SetColor("darkgray");
                terminal.Write("] ");
                terminal.SetColor("red");
                terminal.WriteLine(Loc.Get("engine.back_to_menu"));
            }

            terminal.WriteLine("");
            terminal.SetColor("bright_white");
            } // end if (!GameConfig.ElectronMode) — text body skipped in Electron mode
            var choice = await terminal.GetInput(GameConfig.ElectronMode ? "" : Loc.Get("engine.select_save_prompt"));

            // Handle numeric selection
            if (int.TryParse(choice, out int slotNumber) && slotNumber > 0 && slotNumber <= playerNames.Count)
            {
                var selectedPlayer = playerNames[slotNumber - 1];
                await ShowSaveSlotMenu(selectedPlayer);
                return;
            }

            // Handle letter commands
            switch (choice.ToUpper())
            {
                case "N":
                    var newName = await terminal.GetInput(Loc.Get("engine.enter_name_prompt"));
                    if (!string.IsNullOrWhiteSpace(newName))
                    {
                        // Refresh player names list in case characters were deleted
                        var currentPlayerNames = SaveSystem.Instance.GetAllPlayerNames();

                        // Case-insensitive check to prevent file system conflicts
                        var nameExists = currentPlayerNames.Any(n =>
                            string.Equals(n, newName, StringComparison.OrdinalIgnoreCase));

                        if (nameExists)
                        {
                            terminal.WriteLine(Loc.Get("engine.name_exists"), "red");
                            await Task.Delay(2000);
                        }
                        else
                        {
                            await CreateNewGame(newName);
                            return;
                        }
                    }
                    break;

                case "B":
                    return;

                default:
                    terminal.WriteLine(Loc.Get("engine.invalid_choice"), "red");
                    await Task.Delay(1500);
                    break;
            }
        }
    }

    /// <summary>
    /// Show save slot menu for a specific player
    /// </summary>
    private async Task ShowSaveSlotMenu(string playerName)
    {
        while (true)
        {
            terminal.ClearScreen();
            if (GameConfig.ScreenReaderMode)
            {
                terminal.SetColor("bright_yellow");
                terminal.WriteLine(Loc.Get("engine.save_slots_for", playerName));
            }
            else
            {
                terminal.SetColor("bright_cyan");
                terminal.WriteLine($"╔══════════════════════════════════════════════════════════════════════════════╗");
                terminal.Write("║");
                terminal.SetColor("bright_yellow");
                { string label = Loc.Get("engine.save_slots_for", playerName); int l = (78 - label.Length) / 2, r = 78 - label.Length - l; terminal.Write(new string(' ', l) + label + new string(' ', r)); }
                terminal.SetColor("bright_cyan");
                terminal.WriteLine("║");
                terminal.WriteLine($"╚══════════════════════════════════════════════════════════════════════════════╝");
            }
            terminal.WriteLine("");

            var saves = SaveSystem.Instance.GetPlayerSaves(playerName);

            if (saves.Count == 0)
            {
                terminal.WriteLine(Loc.Get("engine.no_saves_for_char"), "red");
                await Task.Delay(2000);
                return;
            }

            // Display all saves (autosaves and manual saves)
            terminal.SetColor("bright_green");
            terminal.WriteLine(Loc.Get("engine.available_saves"));
            terminal.WriteLine("");

            for (int i = 0; i < saves.Count && i < 10; i++) // Show up to 10 saves
            {
                var save = saves[i];
                terminal.SetColor("darkgray");
                terminal.Write($"[");
                terminal.SetColor("bright_cyan");
                terminal.Write($"{i + 1}");
                terminal.SetColor("darkgray");
                terminal.Write("] ");

                // v0.57.18: choose color/label based on slot type so the player sees
                // which entries are recovery candidates vs normal saves at a glance.
                string typeColor = save.IsEmergency ? "bright_red"
                    : save.IsRecovered ? "yellow"
                    : save.IsAutosave ? "yellow"
                    : "bright_green";
                terminal.SetColor(typeColor);
                terminal.Write($"{save.SaveType.PadRight(12)}");

                terminal.SetColor("gray");
                if (save.IsRecovered)
                {
                    terminal.Write("(unparsed — will open recovery menu)");
                }
                else
                {
                    terminal.Write(Loc.Get("engine.save_detail", save.CurrentDay, save.Level, save.TurnsRemaining));
                }

                terminal.SetColor("darkgray");
                terminal.Write(" | ");

                terminal.SetColor("cyan");
                terminal.WriteLine(save.SaveTime.ToString("yyyy-MM-dd HH:mm:ss"));
            }

            terminal.WriteLine("");
            terminal.SetColor("darkgray");
            terminal.Write("[");
            terminal.SetColor("bright_yellow");
            terminal.Write("D");
            terminal.SetColor("darkgray");
            terminal.Write("] ");
            terminal.SetColor("red");
            terminal.WriteLine(Loc.Get("engine.delete_all_saves"));

            terminal.SetColor("darkgray");
            terminal.Write("[");
            terminal.SetColor("bright_yellow");
            terminal.Write("B");
            terminal.SetColor("darkgray");
            terminal.Write("] ");
            terminal.SetColor("red");
            terminal.WriteLine(Loc.Get("engine.back"));

            terminal.WriteLine("");
            terminal.SetColor("bright_white");
            var choice = await terminal.GetInput(Loc.Get("engine.select_save_load"));

            // Handle numeric selection
            if (int.TryParse(choice, out int saveNumber) && saveNumber > 0 && saveNumber <= saves.Count)
            {
                var selectedSave = saves[saveNumber - 1];
                // v0.57.18: if the listing already detected this slot is unparseable
                // (IsRecovered = true), route straight to the recovery menu instead
                // of doing a redundant load attempt that we know will fail. Saves a
                // step for the player and shows the recovery options immediately.
                if (selectedSave.IsRecovered)
                {
                    // v0.57.20: include "Not enough memory" / "too large" tokens in
                    // the synthetic reason for non-emergency recovery slots so
                    // ShowLoadFailureWithRecovery's bloat-detector matches and
                    // surfaces the [R] auto-repair option. Pre-fix, this short-circuit
                    // path never showed [R] because the reason text didn't contain
                    // any of the OOM-detection keywords. Slots get tagged
                    // IsRecovered when GetAllSaves' deserialize throws — almost
                    // always OOM on a bloated save — so it's correct to assume
                    // bloat here.
                    string reason = selectedSave.IsEmergency
                        ? "This is an emergency save (Ctrl+C dump). The regular save for this character was lost or never written."
                        : "Save file failed to parse during listing — the save is likely bloated or too large. Not enough memory to load it normally. Recovery options below.";
                    await ShowLoadFailureWithRecovery(selectedSave.FileName, reason);
                    return;
                }
                await LoadSaveByFileName(selectedSave.FileName);
                return;
            }

            // Handle letter commands
            switch (choice.ToUpper())
            {
                case "D":
                    terminal.WriteLine("");
                    var confirm = await terminal.GetInput(Loc.Get("engine.delete_saves_confirm", playerName));
                    if (confirm == "DELETE")
                    {
                        // Delete all saves for this player
                        foreach (var save in saves)
                        {
                            var filePath = System.IO.Path.Combine(
                                System.IO.Path.Combine(GetUserDataPath(), "saves"),
                                save.FileName);
                            try
                            {
                                System.IO.File.Delete(filePath);
                            }
                            catch (Exception ex)
            {
                DebugLogger.Instance.Log(DebugLogger.LogLevel.Debug, "ENGINE", $"Swallowed exception: {ex.Message}");
            }
                        }
                        terminal.WriteLine(Loc.Get("engine.all_saves_deleted"), "green");
                        await Task.Delay(1500);
                        return;
                    }
                    break;

                case "B":
                    return;

                default:
                    terminal.WriteLine(Loc.Get("engine.invalid_choice"), "red");
                    await Task.Delay(1500);
                    break;
            }
        }
    }

    /// <summary>
    /// Load game by filename
    /// </summary>
    private async Task LoadSaveByFileName(string fileName)
    {
        try
        {
            // v0.60.0 beta-launch Rage event: short-circuit BEFORE any save
            // restore happens. The cinematic plays for ~25 seconds, and any
            // restore step that fires a fire-and-forget save would race with
            // the deletion. By short-circuiting at the top of the method we
            // guarantee no save path can run during this session.
            if (UsurperRemake.Systems.RageEventSystem.IsActive && UsurperRemake.BBS.DoorMode.IsOnlineMode)
            {
                var rageUsername = UsurperRemake.Server.SessionContext.Current?.Username ?? fileName;
                await UsurperRemake.Systems.RageEventSystem.RunRageEventAsync(null!, terminal, rageUsername);
                IsIntentionalExit = true;
                return;
            }

            terminal.WriteLine(Loc.Get("engine.loading_save"), "yellow");

            // v0.57.14: use the error-reporting load path so we can show the player
            // what actually went wrong (missing file, disk error, malformed JSON,
            // OOM on a bloated save) instead of a generic "corrupted" line. The old
            // path silently returned null on any exception, which made it look like
            // the save had vanished even when the file was intact on disk.
            var (saveData, loadError) = await SaveSystem.Instance.LoadSaveByFileNameWithError(fileName);
            if (saveData == null)
            {
                await ShowLoadFailureWithRecovery(fileName, loadError ?? "Unknown error — save file could not be parsed.");
                return;
            }

            // Validate save data
            if (saveData.Player == null)
            {
                // v0.57.14: no player data — treat like a failed load and show recovery
                await ShowLoadFailureWithRecovery(fileName, "Save file parsed successfully but contains no player data. The save may have been written incompletely.");
                return;
            }

            terminal.WriteLine(Loc.Get("engine.restoring_player", saveData.Player.Name2 ?? saveData.Player.Name1), "green");
            await Task.Delay(500);

            // Log save data before restore
            DebugLogger.Instance.LogDebug("LOAD", $"Save file data - HP={saveData.Player.HP}/{saveData.Player.MaxHP}, BaseMaxHP={saveData.Player.BaseMaxHP}");

            // Restore player from save data
            currentPlayer = RestorePlayerFromSaveData(saveData.Player);

            if (currentPlayer == null)
            {
                DebugLogger.Instance.LogError("LOAD", "Failed to restore player data");
                terminal.WriteLine(Loc.Get("engine.restore_failed"), "red");
                await Task.Delay(3000);
                return;
            }

            // Gold audit snapshot on login
            DebugLogger.Instance.LogInfo("GOLD_LOGIN", $"{currentPlayer.Name} Lv{currentPlayer.Level} LOGIN gold={currentPlayer.Gold:N0} bank={currentPlayer.BankGold:N0} totalEarned={currentPlayer.Statistics?.TotalGoldEarned ?? 0:N0} totalWealth={currentPlayer.Gold + currentPlayer.BankGold:N0}");

            // Process daily login streak rewards
            await ProcessLoginStreak(currentPlayer);

            // Show weekly rank change on login
            if (currentPlayer.WeeklyRank > 0 && currentPlayer.PreviousWeeklyRank > 0)
            {
                int change = currentPlayer.PreviousWeeklyRank - currentPlayer.WeeklyRank; // positive = moved up
                if (change != 0)
                {
                    terminal.WriteLine("");
                    if (change > 0)
                    {
                        terminal.SetColor("bright_green");
                        terminal.WriteLine($"  You moved up {change} rank{(change != 1 ? "s" : "")}! Now #{currentPlayer.WeeklyRank}");
                    }
                    else
                    {
                        terminal.SetColor("yellow");
                        terminal.WriteLine($"  You dropped {Math.Abs(change)} rank{(Math.Abs(change) != 1 ? "s" : "")}. Now #{currentPlayer.WeeklyRank}");
                    }

                    // v0.57.10 (player report — "Every single time I have logged
                    // in recently, it says you have moved up 28 ranks and are
                    // now in 14th place. It always says the same place."):
                    // the display fired whenever `WeeklyRank != PreviousWeeklyRank`,
                    // but the only code that touches `PreviousWeeklyRank` is the
                    // Monday daily-reset in `DailySystemManager.UpdateWeeklyRankings`.
                    // Between Mondays the delta stayed constant, so every login
                    // re-printed the same message. Consume the delta after
                    // showing it — next week's Monday update will re-snapshot
                    // the current rank into `PreviousWeeklyRank` before
                    // recomputing, so the week-over-week comparison stays
                    // intact.
                    currentPlayer.PreviousWeeklyRank = currentPlayer.WeeklyRank;
                    _ = SaveCurrentGame();
                }
                if (!string.IsNullOrEmpty(currentPlayer.RivalName))
                {
                    terminal.SetColor("cyan");
                    terminal.WriteLine($"  Your rival {currentPlayer.RivalName} is Level {currentPlayer.RivalLevel}.");
                }
                terminal.WriteLine("");
            }

            // Load daily system state
            if (dailyManager != null)
            {
                dailyManager.LoadFromSaveData(saveData);
            }

            // Store per-session daily state (safe from shared singleton cross-contamination in MUD mode)
            SessionCurrentDay = saveData.CurrentDay;
            SessionLastResetTime = saveData.LastDailyReset;
            SessionDailyCycleMode = saveData.DailyCycleMode;

            // Restore world state
            await RestoreWorldState(saveData.WorldState);

            // Restore NPCs - from player's save as baseline
            await RestoreNPCs(saveData.NPCs);

            // In online mode, override NPCs and royal court with world_state (authoritative source).
            // The world sim maintains shared state 24/7 - player saves may be stale.
            if (UsurperRemake.BBS.DoorMode.IsOnlineMode && OnlineStateManager.Instance != null)
            {
                // Load current NPCs from world_state (teams, levels, equipment, etc.)
                var sharedNpcs = await OnlineStateManager.Instance.LoadSharedNPCs();
                if (sharedNpcs != null && sharedNpcs.Count > 0)
                {
                    await RestoreNPCs(sharedNpcs);
                    DebugLogger.Instance.LogInfo("ONLINE", $"NPCs overridden from world_state: {sharedNpcs.Count} NPCs loaded");
                }

                // Load settlement from world_state (authoritative source, not stale player save)
                await OnlineStateManager.Instance.LoadSettlementFromWorldState();

                // v0.57.10 (Coosh report — "When I challenge the turf, I win it.
                // When I relog though it appears Watchers has taken back over"):
                // self-heal stale turf flags on relog. Three cases to resolve:
                //
                //   (a) Player save says CTurf=true AND world_state NPCs have
                //       CTurf=true on a DIFFERENT team (real NPC team, not the
                //       player's). The "different team with CTurf" is more
                //       recent — either (i) the pre-v0.57.10 stale-Watchers
                //       snapshot, which we'd want to clear, or (ii) another
                //       online player's team legitimately took turf while this
                //       player was offline, in which case the player save is
                //       now stale and CTurf should come OFF the player. We
                //       can't distinguish (i) from (ii) here, but the
                //       conservative choice is to trust world_state — clearing
                //       player.CTurf is always safer than leaving two
                //       controllers in disagreement. If (i) was the truth and
                //       we cleared wrongly, the player just re-wins the turf
                //       from the stale Watchers flag, which is at worst a
                //       minor annoyance compared to the current silent-reverts
                //       bug.
                //
                //   (b) Player save says CTurf=true AND no other team has it
                //       in world_state (or only the player's own team does).
                //       Player is the rightful controller — rebase NPC flags
                //       onto the player's team NPCs (fixes the pre-v0.57.10
                //       saves that never wrote their win to world_state).
                //
                //   (c) Player save says CTurf=false. No action — world_state
                //       is authoritative, player will read the true state from
                //       it naturally.
                if (currentPlayer.CTurf && !string.IsNullOrEmpty(currentPlayer.Team))
                {
                    var rivalController = NPCSpawnSystem.Instance.ActiveNPCs
                        .FirstOrDefault(n => n.CTurf
                                          && !string.IsNullOrEmpty(n.Team)
                                          && n.Team != currentPlayer.Team);
                    if (rivalController != null)
                    {
                        // Case (a): clear the stale player flag. world_state wins.
                        currentPlayer.CTurf = false;
                        DebugLogger.Instance.LogInfo("TURF", $"Login heal: player {currentPlayer.DisplayName} save said CTurf=true on team '{currentPlayer.Team}' but world_state has team '{rivalController.Team}' holding turf. Cleared player.CTurf.");

                        // v0.57.10 (C1): surface the loss to the player on login.
                        // Without this, a player who held turf and comes back to
                        // find a rival in charge only sees the change by noticing
                        // the `/town` command stopped working. Give them a clear
                        // one-line notice so the state change isn't invisible.
                        terminal.WriteLine("");
                        terminal.SetColor("bright_red");
                        terminal.WriteLine($"  {Loc.Get("engine.turf_lost_offline", rivalController.Team ?? "another team")}");
                        terminal.SetColor("gray");
                        terminal.WriteLine($"  {Loc.Get("engine.turf_lost_offline_hint")}");
                        terminal.WriteLine("");
                    }
                    else
                    {
                        // Case (b): stamp / strip flags to match the player.
                        int stripped = 0, stamped = 0;
                        foreach (var npc in NPCSpawnSystem.Instance.ActiveNPCs)
                        {
                            if (npc.Team == currentPlayer.Team)
                            {
                                if (!npc.CTurf) { npc.CTurf = true; stamped++; }
                            }
                            else if (npc.CTurf)
                            {
                                npc.CTurf = false; stripped++;
                            }
                        }
                        if (stripped > 0 || stamped > 0)
                        {
                            DebugLogger.Instance.LogInfo("TURF", $"Login heal: player {currentPlayer.DisplayName} has CTurf on team '{currentPlayer.Team}'. Stamped {stamped} teammate NPCs, stripped {stripped} stale NPC flags. Persisting to world_state.");
                            try { await OnlineStateManager.Instance.SaveAllSharedState(); }
                            catch (Exception ex) { DebugLogger.Instance.LogError("TURF", $"Login heal save failed: {ex.Message}"); }
                        }
                    }
                }
            }

            // NOTE: Player quests are already merged in RestorePlayerFromSaveData via MergePlayerQuests.
            // A second merge here would create duplicate Quest objects, orphaning the ones in player.ActiveQuests.

            // Check for quests targeting permadead NPCs — auto-complete with rewards
            if (currentPlayer.ActiveQuests.Count > 0 && NPCSpawnSystem.Instance?.ActiveNPCs.Count > 0)
            {
                await CleanupDeadNPCQuests(currentPlayer);
            }

            // Restore story systems (companions, children, seals, etc.)
            // In online mode, only restore this player's god entry — other players' stale
            // snapshots would overwrite their current worship choices in the shared GodSystem.
            string? godRestoreFilter = (UsurperRemake.BBS.DoorMode.IsOnlineMode && currentPlayer != null)
                ? currentPlayer.Name2 : null;
            SaveSystem.Instance.RestoreStorySystems(saveData.StorySystems, godRestoreFilter);

            // Migration: sync RelationshipSystem with RomanceTracker for saves affected by
            // the bidirectional key bug (pre-v0.42.4). If RomanceTracker says Lover/Spouse/FWB
            // but RelationshipSystem is stuck at Normal (70), force it to Love (20).
            if (currentPlayer != null)
            {
                try
                {
                    foreach (var npc in NPCSpawnSystem.Instance.ActiveNPCs)
                    {
                        var romanceType = RomanceTracker.Instance.GetRelationType(npc.ID);
                        if (romanceType == RomanceRelationType.None || romanceType == RomanceRelationType.Ex)
                            continue;

                        int currentRel = RelationshipSystem.GetRelationshipStatus(currentPlayer, npc);
                        int targetRel = romanceType == RomanceRelationType.Spouse ? GameConfig.RelationMarried :
                                        romanceType == RomanceRelationType.Lover ? GameConfig.RelationLove :
                                        GameConfig.RelationPassion; // FWB

                        if (currentRel > targetRel)
                        {
                            // Relationship is worse than it should be — force it to match romance status
                            int stepsNeeded = 0;
                            int test = currentRel;
                            while (test > targetRel && stepsNeeded < 20)
                            {
                                test = test switch
                                {
                                    >= 100 => 90,
                                    >= 90 => 80,
                                    >= 80 => 70,
                                    >= 70 => 60,
                                    >= 60 => 50,
                                    >= 50 => 40,
                                    >= 40 => 30,
                                    >= 30 => 20,
                                    >= 20 => 10,
                                    _ => test
                                };
                                stepsNeeded++;
                            }
                            RelationshipSystem.UpdateRelationship(currentPlayer, npc, 1, stepsNeeded, false, true);
                            DebugLogger.Instance.LogInfo("MIGRATION", $"Fixed relationship with {npc.Name}: {currentRel} -> {targetRel} (romance: {romanceType})");
                        }
                    }
                }
                catch (Exception ex)
                {
                    DebugLogger.Instance.LogWarning("MIGRATION", $"Relationship sync failed: {ex.Message}");
                }
            }

            // In online mode, override royal court, children, and marriages with world_state
            // (authoritative source). RestoreStorySystems loaded stale data from the player's
            // save — the world sim maintains these 24/7 while the player is offline.
            if (UsurperRemake.BBS.DoorMode.IsOnlineMode && OnlineStateManager.Instance != null)
            {
                await OnlineStateManager.Instance.LoadRoyalCourtFromWorldState();
                await LoadSharedChildrenAndMarriages();
            }

            terminal.WriteLine(Loc.Get("engine.save_loaded"), "bright_green");
            await Task.Delay(1000);

            // Update online display name to character's Name2 (custom display name)
            if (OnlineStateManager.IsActive && currentPlayer != null)
            {
                var displayName = currentPlayer.Name2 ?? currentPlayer.Name1;
                if (!string.IsNullOrEmpty(displayName))
                {
                    await OnlineStateManager.Instance!.UpdateDisplayName(displayName);

                    // Update RoomRegistry session so "Also here" shows display name, not account name
                    var ctx = UsurperRemake.Server.SessionContext.Current;
                    var srv = UsurperRemake.Server.MudServer.Instance;
                    if (ctx != null && srv != null && srv.ActiveSessions.TryGetValue(ctx.Username?.ToLowerInvariant() ?? "", out var sess))
                    {
                        sess.ActiveCharacterName = displayName;
                    }
                }
            }

            // Online mode: validate player's team still exists
            if (UsurperRemake.BBS.DoorMode.IsOnlineMode && currentPlayer != null && !string.IsNullOrEmpty(currentPlayer.Team))
            {
                if (SaveSystem.Instance?.Backend is SqlSaveBackend teamBackend)
                {
                    // Check if team exists in player_teams or NPC teams
                    bool teamExists = teamBackend.IsTeamNameTaken(currentPlayer.Team) ||
                        NPCSpawnSystem.Instance.ActiveNPCs.Any(n => n.Team == currentPlayer.Team);
                    if (!teamExists)
                    {
                        terminal.SetColor("yellow");
                        terminal.WriteLine(Loc.Get("engine.team_no_longer_exists", currentPlayer.Team));
                        WorldSimulator.UnregisterPlayerTeam(currentPlayer.Team);
                        currentPlayer.Team = "";
                        currentPlayer.TeamPW = "";
                        currentPlayer.CTurf = false;
                        await Task.Delay(2000);
                    }
                }
            }

            // Online mode: sync player King and CTurf flags with authoritative world state.
            // Player's save may have stale flags if the world changed while they were offline.
            if (UsurperRemake.BBS.DoorMode.IsOnlineMode && currentPlayer != null)
            {
                SyncPlayerFlagsFromWorldState(currentPlayer);
            }

            // Check if player was sleeping and handle offline attacks
            _sleepLocationOnLogin = null;
            if (UsurperRemake.BBS.DoorMode.IsOnlineMode && SaveSystem.Instance?.Backend is SqlSaveBackend sqlBackend)
            {
                _sleepLocationOnLogin = await HandleSleepReport(sqlBackend);
                await ShowWhileYouWereGone(sqlBackend);
            }

            // Ensure quests are regenerated with corrected data
            QuestSystem.EnsureQuestsExist(currentPlayer?.Level ?? 10);

            // Mark session as in-game so broadcasts (gossip, shouts) are delivered
            var mudServer = UsurperRemake.Server.MudServer.Instance;
            var sessionKey = UsurperRemake.BBS.DoorMode.GetPlayerName()?.ToLowerInvariant() ?? "";
            UsurperRemake.Server.PlayerSession? playerSession = null;
            if (mudServer != null && sessionKey.Length > 0)
                mudServer.ActiveSessions.TryGetValue(sessionKey, out playerSession);
            if (playerSession != null)
                playerSession.IsInGame = true;

            // NOW register online presence and broadcast login — deferred from auth time
            // so players sitting at the main menu don't appear as "in the game"
            if (OnlineStateManager.IsActive)
            {
                var displayName = currentPlayer?.Name2 ?? currentPlayer?.Name1 ?? UsurperRemake.BBS.DoorMode.GetPlayerName() ?? "Unknown";
                var connType = OnlineStateManager.Instance!.DeferredConnectionType;
                await OnlineStateManager.Instance!.StartOnlineTracking(displayName, connType);

                // Notify WizNet of wizard login
                if (playerSession != null && playerSession.WizardLevel >= UsurperRemake.Server.WizardLevel.Builder)
                    UsurperRemake.Server.WizNet.SystemNotify($"{UsurperRemake.Server.WizardConstants.GetTitle(playerSession.WizardLevel)} {displayName} has connected.");

                // Global login announcement (suppress for invisible wizards)
                if (mudServer != null && playerSession != null && !playerSession.IsWizInvisible)
                {
                    mudServer.BroadcastToAll(
                        $"\u001b[1;33m  {displayName} has entered the realm. [{connType}]\u001b[0m",
                        excludeUsername: sessionKey);
                }
            }

            // Online mode: check if a daily reset was missed while offline (7 PM ET boundary crossed)
            if (UsurperRemake.BBS.DoorMode.IsOnlineMode && dailyManager != null)
            {
                await dailyManager.CheckDailyReset();
            }

            // Manwe worship cleanup: Manwe (Supreme Creator / final boss) is not a valid
            // worship target — players could select it at the temple due to missing filter.
            if (currentPlayer != null)
            {
                var godSystem = UsurperRemake.GodSystemSingleton.Instance;
                var elderGod = godSystem?.GetPlayerGod(currentPlayer.Name2);
                if (elderGod == GameConfig.SupremeCreatorName)
                {
                    DebugLogger.Instance.LogWarning("WORSHIP", $"Cleaned up invalid Manwe worship for {currentPlayer.Name2}");
                    godSystem?.SetPlayerGod(currentPlayer.Name2, "");
                    elderGod = "";
                }

                // Dual-worship cleanup: player can't worship both an elder god and a player-god
                if (!string.IsNullOrEmpty(currentPlayer.WorshippedGod) && !string.IsNullOrEmpty(elderGod))
                {
                    // Elder god takes priority — clear the player-god worship
                    DebugLogger.Instance.LogWarning("WORSHIP", $"Dual worship detected for {currentPlayer.Name2}: elder god '{elderGod}' + immortal '{currentPlayer.WorshippedGod}'. Clearing immortal.");
                    currentPlayer.WorshippedGod = "";
                }
            }

            // Marriage cleanup: verify spouse NPC still exists and is alive
            if (currentPlayer != null && currentPlayer.IsMarried && !string.IsNullOrEmpty(currentPlayer.SpouseName))
            {
                var spouseNPC = NPCSpawnSystem.Instance?.GetNPCByName(currentPlayer.SpouseName);
                if (spouseNPC == null || spouseNPC.IsDead || spouseNPC.IsPermaDead)
                {
                    DebugLogger.Instance.LogWarning("MARRIAGE", $"Spouse '{currentPlayer.SpouseName}' not found or dead — clearing marriage for {currentPlayer.Name2}");
                    currentPlayer.IsMarried = false;
                    currentPlayer.SpouseName = "";
                }
            }

            // Online mode: cache divine boon effects for mortals who worship a player-god
            if (UsurperRemake.BBS.DoorMode.IsOnlineMode && currentPlayer != null
                && !string.IsNullOrEmpty(currentPlayer.WorshippedGod)
                && SaveSystem.Instance?.Backend is SqlSaveBackend boonBackend)
            {
                try
                {
                    string boonConfig = await boonBackend.GetGodBoonConfig(currentPlayer.WorshippedGod);
                    if (!string.IsNullOrEmpty(boonConfig))
                    {
                        currentPlayer.CachedBoonEffects = DivineBoonRegistry.CalculateEffects(boonConfig);
                        DebugLogger.Instance.LogInfo("BOONS", $"Cached boon effects for {currentPlayer.Name2} from god {currentPlayer.WorshippedGod}: {boonConfig}");
                    }
                }
                catch (Exception ex)
                {
                    DebugLogger.Instance.LogWarning("BOONS", $"Failed to cache boon effects: {ex.Message}");
                }
            }

            // Failsafe: if player beat Manwe but the ending sequence didn't complete (whether
            // they disconnected mid-sequence OR something downstream crashed before the ending
            // could fire), re-trigger the ending sequence on login.
            // v0.57.16: dropped the `hasEndingFlag` precondition. Spudman report — the
            // post-Manwe Noctura betrayal NRE'd before any ending flag got set, leaving the
            // player permanently stuck (Manwe resolved, no immortality, no ending recorded,
            // no NG+ option, no way to retrigger). Now Manwe-resolved + not-immortal +
            // sequence-not-completed is enough to fire DetermineEnding on login.
            if (currentPlayer != null && !currentPlayer.IsImmortal)
            {
                var storySystem = StoryProgressionSystem.Instance;
                bool manweResolved = storySystem.OldGodStates.TryGetValue(OldGodType.Manwe, out var manweState) &&
                    (manweState.Status == GodStatus.Defeated || manweState.Status == GodStatus.Allied ||
                     manweState.Status == GodStatus.Saved || manweState.Status == GodStatus.Consumed);
                bool sequenceCompleted = storySystem.HasStoryFlag("ending_sequence_completed");

                if (manweResolved && !sequenceCompleted)
                {
                    terminal.SetColor("bright_yellow");
                    terminal.WriteLine("");
                    terminal.WriteLine(Loc.Get("engine.session_incomplete"));
                    terminal.WriteLine(Loc.Get("engine.session_resuming"));
                    terminal.WriteLine("");
                    await Task.Delay(2000);

                    var endingType = EndingsSystem.Instance.DetermineEnding(currentPlayer);
                    await EndingsSystem.Instance.TriggerEnding(currentPlayer, endingType, terminal);

                    if (PendingNewGamePlus)
                    {
                        PendingNewGamePlus = false;
                        var activeKey = UsurperRemake.BBS.DoorMode.GetPlayerName()?.ToLowerInvariant() ?? fileName;
                        var ngpSaves = SaveSystem.Instance.GetPlayerSaves(activeKey);
                        foreach (var save in ngpSaves)
                            SaveSystem.Instance.DeleteSave(System.IO.Path.GetFileNameWithoutExtension(save.FileName));
                        await CreateNewGame(activeKey);
                        return;
                    }
                    if (PendingImmortalAscension)
                    {
                        PendingImmortalAscension = false;
                        await locationManager.EnterLocation(GameLocation.Pantheon, currentPlayer);
                        return;
                    }
                }
            }

            // Route immortals to Pantheon; sleeping players wake where they slept; others go to Main Street
            GameLocation startLocation;
            if (currentPlayer!.IsImmortal)
            {
                startLocation = GameLocation.Pantheon;
            }
            else if (_sleepLocationOnLogin != null)
            {
                startLocation = _sleepLocationOnLogin switch
                {
                    "inn" => GameLocation.TheInn,
                    "home" => GameLocation.Home,
                    "castle" => GameLocation.Castle,
                    _ => GameLocation.MainStreet
                };
            }
            else
            {
                startLocation = GameLocation.MainStreet;
            }
            // If player was saved dead (HP <= 0), they closed during the death screen — re-present Veil
            if (currentPlayer!.HP <= 0)
            {
                terminal.SetColor("red");
                terminal.WriteLine("  You awaken at the threshold between life and death...");
                terminal.WriteLine("");
                // Apply Accept Your Fate penalties since they tried to dodge death
                int levelsLost = Math.Min(5, currentPlayer.Level - 1);
                if (levelsLost > 0)
                {
                    for (int i = 0; i < levelsLost; i++)
                    {
                        LevelMasterLocation.ReverseClassStatIncrease(currentPlayer);
                        currentPlayer.Level--;
                    }
                    currentPlayer.Experience = LevelMasterLocation.GetExperienceForLevel(currentPlayer.Level);
                    terminal.WriteLine($"  You lose {levelsLost} levels. (Now level {currentPlayer.Level})", "red");
                }
                long goldLost = (long)(currentPlayer.Gold * 0.75);
                currentPlayer.Gold -= goldLost;
                terminal.WriteLine($"  You lose {goldLost:N0} gold.", "red");
                currentPlayer.RecalculateStats();
                currentPlayer.HP = Math.Max(1, (int)(currentPlayer.MaxHP * 0.1));
                terminal.WriteLine("  The void releases you, diminished.", "gray");
                terminal.WriteLine("");
                await terminal.PressAnyKey();
                startLocation = GameLocation.MainStreet;
                _ = SaveCurrentGame();
            }

            // v0.60.0 Rage hook moved to top-of-method to prevent save races
            // during the cinematic. See start of method.

            await locationManager.EnterLocation(startLocation, currentPlayer);
        }
        catch (Exception ex)
        {
            terminal.WriteLine(Loc.Get("engine.error_loading", ex.Message), "red");
            UsurperRemake.Systems.DebugLogger.Instance.LogError("CRASH", $"Failed to load save {fileName}:\n{ex}");
            UsurperRemake.Systems.DebugLogger.Instance.Flush();
            await Task.Delay(3000);
        }
    }

    /// <summary>
    /// v0.57.14: Show a detailed load-failure report and offer recovery options.
    /// Displays the specific error (from FileSaveBackend.ReadGameDataByFileNameWithError),
    /// the full path to the save file, and any recovery files that exist alongside it
    /// (<name>_backup.json, emergency_autosave.json, timestamped autosaves). The player
    /// can choose to load the backup, start a new character (single-player wipes the
    /// broken save when they do), or return to the main menu and manually investigate.
    /// Goal: never make a player think their saves silently disappeared.
    /// </summary>
    private async Task ShowLoadFailureWithRecovery(string fileName, string errorMessage)
    {
        terminal.WriteLine("");
        terminal.WriteLine("========================================================================", "red");
        terminal.WriteLine("  SAVE LOAD FAILED", "bright_red");
        terminal.WriteLine("========================================================================", "red");
        terminal.WriteLine("");
        terminal.WriteLine("The game could not load your save. Your save file is still on disk —", "yellow");
        terminal.WriteLine("it was NOT deleted. Details below so you can recover it.", "yellow");
        terminal.WriteLine("");

        terminal.WriteLine("Error:", "bright_white");
        terminal.WriteLine($"  {errorMessage}", "red");
        terminal.WriteLine("");

        // Discover recovery files
        string saveDir = "";
        try { saveDir = SaveSystem.Instance.GetSaveDirectory(); } catch { }

        string baseName = System.IO.Path.GetFileNameWithoutExtension(fileName);
        var recoveryOptions = new List<(string label, string path)>();

        if (!string.IsNullOrEmpty(saveDir) && System.IO.Directory.Exists(saveDir))
        {
            terminal.WriteLine("Save folder:", "bright_white");
            terminal.WriteLine($"  {saveDir}", "cyan");
            terminal.WriteLine("");

            // Primary backup
            string backupPath = System.IO.Path.Combine(saveDir, $"{baseName}_backup.json");
            if (System.IO.File.Exists(backupPath))
            {
                var info = new System.IO.FileInfo(backupPath);
                recoveryOptions.Add(($"Backup from {info.LastWriteTime:yyyy-MM-dd HH:mm}", backupPath));
            }

            // Timestamped autosaves (most recent first)
            try
            {
                var autosaves = System.IO.Directory.GetFiles(saveDir, $"{baseName}_autosave_*.json")
                    .Select(p => new System.IO.FileInfo(p))
                    .OrderByDescending(fi => fi.LastWriteTime)
                    .Take(3)
                    .ToList();
                foreach (var fi in autosaves)
                {
                    recoveryOptions.Add(($"Autosave from {fi.LastWriteTime:yyyy-MM-dd HH:mm}", fi.FullName));
                }
            }
            catch { }

            // Emergency autosave (not tied to this character name, but worth mentioning)
            string emergencyPath = System.IO.Path.Combine(saveDir, "emergency_autosave.json");
            if (System.IO.File.Exists(emergencyPath))
            {
                var info = new System.IO.FileInfo(emergencyPath);
                recoveryOptions.Add(($"Emergency autosave from {info.LastWriteTime:yyyy-MM-dd HH:mm}", emergencyPath));
            }
        }

        // v0.57.19: detect OOM-class errors. When the failure is "save too big to
        // fit in memory" rather than corrupted-JSON, offer the automatic repair
        // path. The repair clips bloated arrays in-place via JsonDocument so the
        // file becomes loadable. ALL recovery files are typically also bloated
        // for affected players, so offering repair on the primary AND each
        // recovery slot is the only way out.
        //
        // v0.57.20: also detect bloat by file size. Some load failure paths pass
        // through generic error strings that don't contain OOM keywords (e.g. the
        // [RECOVERY]-tagged short-circuit, or load failures that trigger before
        // the deserialize step). If the primary save OR ANY recovery candidate is
        // over the bloat threshold (~10 MB — well under the 5 MB SAVE_AUDIT
        // warning ceiling but a realistic upper bound for healthy saves), assume
        // bloat and offer [R] regardless of the error text.
        bool isBloatError = errorMessage != null && (
            errorMessage.Contains("Not enough memory", StringComparison.OrdinalIgnoreCase) ||
            errorMessage.Contains("too large", StringComparison.OrdinalIgnoreCase) ||
            errorMessage.Contains("more data than", StringComparison.OrdinalIgnoreCase) ||
            errorMessage.Contains("bloated", StringComparison.OrdinalIgnoreCase));

        // File-size fallback: scan primary + all recovery candidates. Anything
        // over BloatDetectionBytes is suspicious. Cheap (FileInfo.Length is an
        // OS-level metadata read, no file content access).
        if (!isBloatError)
        {
            try
            {
                long bloatThreshold = 10L * 1024 * 1024; // 10 MB
                string primaryPath = System.IO.Path.Combine(saveDir ?? "", fileName);
                if (System.IO.File.Exists(primaryPath))
                {
                    var pi = new System.IO.FileInfo(primaryPath);
                    if (pi.Length > bloatThreshold) isBloatError = true;
                }
                if (!isBloatError)
                {
                    foreach (var (_, p) in recoveryOptions)
                    {
                        try
                        {
                            var ri = new System.IO.FileInfo(p);
                            if (ri.Length > bloatThreshold) { isBloatError = true; break; }
                        }
                        catch { }
                    }
                }
            }
            catch { }
        }

        if (recoveryOptions.Count > 0)
        {
            terminal.WriteLine("Recovery files found:", "bright_white");
            for (int i = 0; i < recoveryOptions.Count; i++)
            {
                terminal.WriteLine($"  [{i + 1}] {recoveryOptions[i].label}", "bright_cyan");
                terminal.WriteLine($"      {recoveryOptions[i].path}", "dark_gray");
            }
            terminal.WriteLine("");
            terminal.WriteLine("Options:", "bright_white");
            terminal.WriteLine("  [1-" + recoveryOptions.Count + "] Try to load a recovery file", "yellow");
            if (isBloatError)
            {
                terminal.WriteLine("  [R]    Auto-repair the bloated save file (recommended)", "bright_green");
            }
            terminal.WriteLine("  [N]    Start a NEW character (will overwrite the broken save)", "yellow");
            terminal.WriteLine("  [Q]    Return to main menu without loading", "yellow");
        }
        else
        {
            terminal.WriteLine("No backup or autosave files were found alongside the save.", "yellow");
            terminal.WriteLine("");
            terminal.WriteLine("Manual recovery steps:", "bright_white");
            terminal.WriteLine("  1. Open the save folder above in a file browser", "gray");
            terminal.WriteLine("  2. Open the save file in a text editor (e.g. Notepad, VS Code)", "gray");
            terminal.WriteLine("  3. The JSON at the top of the file contains your character data", "gray");
            terminal.WriteLine("");
            terminal.WriteLine("Options:", "bright_white");
            if (isBloatError)
            {
                terminal.WriteLine("  [R]    Auto-repair the bloated save file (recommended)", "bright_green");
            }
            terminal.WriteLine("  [N]    Start a NEW character (will overwrite the broken save)", "yellow");
            terminal.WriteLine("  [Q]    Return to main menu without loading", "yellow");
        }
        terminal.WriteLine("");

        // Phase 3: emit the recovery menu state for the Electron client. The
        // graphical view shows the error + clickable recovery file buttons +
        // the optional [R] auto-repair button (only when isBloatError) +
        // [N] / [Q] actions. Click sends the key via stdin which lands at
        // the same GetInput call below.
        if (GameConfig.ElectronMode)
        {
            var files = new List<ElectronBridge.RecoveryFileData>();
            for (int i = 0; i < recoveryOptions.Count; i++)
            {
                long size = 0;
                try { size = new System.IO.FileInfo(recoveryOptions[i].path).Length; } catch { }
                files.Add(new ElectronBridge.RecoveryFileData
                {
                    Key = (i + 1).ToString(),
                    Label = recoveryOptions[i].label,
                    Path = recoveryOptions[i].path,
                    SizeBytes = size
                });
            }
            ElectronBridge.EmitRecoveryMenu(
                errorMessage: errorMessage ?? "",
                saveFolderPath: saveDir ?? "",
                files: files,
                offerAutoRepair: isBloatError);
        }

        var choice = (await terminal.GetInput("Your choice: ")).Trim().ToUpperInvariant();

        if (choice == "Q" || string.IsNullOrEmpty(choice))
        {
            return;
        }

        if (choice == "R" && isBloatError)
        {
            await RunSaveRepair(fileName, errorMessage);
            return;
        }

        if (choice == "N")
        {
            terminal.WriteLine("");
            terminal.WriteLine("Starting new character...", "yellow");
            await Task.Delay(800);
            await CreateNewGame(fileName);
            return;
        }

        if (int.TryParse(choice, out int recoveryIndex) &&
            recoveryIndex >= 1 && recoveryIndex <= recoveryOptions.Count)
        {
            var (label, path) = recoveryOptions[recoveryIndex - 1];
            terminal.WriteLine("");
            terminal.WriteLine($"Attempting to load {label}...", "yellow");
            await Task.Delay(800);

            // Use the filename (not full path) — SaveSystem loads from SaveDirectory.
            string recoveryFileName = System.IO.Path.GetFileName(path);
            var (recoveryData, recoveryError) = await SaveSystem.Instance.LoadSaveByFileNameWithError(recoveryFileName);

            if (recoveryData?.Player == null)
            {
                terminal.WriteLine("");
                terminal.WriteLine("That recovery file also failed to load.", "red");
                terminal.WriteLine($"  {recoveryError ?? "Missing player data"}", "red");
                terminal.WriteLine("");
                await terminal.PressAnyKey();
                // Re-enter the recovery menu so the player can pick a different file.
                // v0.57.20: pass the FRESH recoveryError, not the stale original
                // errorMessage. Pre-fix, if the primary failed for a non-OOM reason
                // but a recovery file OOMed, the re-entered menu still showed the
                // primary's error and never offered [R] auto-repair.
                await ShowLoadFailureWithRecovery(fileName, recoveryError ?? errorMessage);
                return;
            }

            // Recovery file parsed — copy it over the primary and reload through the
            // normal path so all the post-load restoration runs.
            try
            {
                string primaryPath = System.IO.Path.Combine(saveDir, fileName);
                System.IO.File.Copy(path, primaryPath, overwrite: true);
                terminal.WriteLine("Recovery file restored as primary save.", "bright_green");
                terminal.WriteLine("");
                await Task.Delay(1000);
                await LoadSaveByFileName(fileName);
            }
            catch (Exception ex)
            {
                terminal.WriteLine($"Could not copy recovery file: {ex.Message}", "red");
                await Task.Delay(3000);
            }
            return;
        }

        // Unknown input — return to menu
    }

    /// <summary>
    /// v0.57.19: emergency repair flow for the "save too bloated to load" case.
    /// Tries the primary file first, then walks the same recovery chain as
    /// ShowLoadFailureWithRecovery (backup, autosaves, emergency). On any
    /// successful repair+load the character is restored. The repair is
    /// in-place — repaired files are written back to their original paths so
    /// future loads get the trimmed version.
    /// </summary>
    private async Task RunSaveRepair(string fileName, string originalError)
    {
        terminal.WriteLine("");
        terminal.WriteLine("========================================================================", "bright_green");
        terminal.WriteLine("  AUTOMATIC SAVE REPAIR", "bright_green");
        terminal.WriteLine("========================================================================", "bright_green");
        terminal.WriteLine("");
        terminal.WriteLine("This will read the bloated save and write a trimmed version", "yellow");
        terminal.WriteLine("with oversized lists clipped down to safe limits. Your character,", "yellow");
        terminal.WriteLine("inventory, level, gold, and quests are preserved. Some NPC history", "yellow");
        terminal.WriteLine("(old memories, prisoners, encounters) will be discarded.", "yellow");
        terminal.WriteLine("");

        string saveDir;
        try { saveDir = SaveSystem.Instance.GetSaveDirectory(); }
        catch (Exception ex)
        {
            terminal.WriteLine($"Could not access save folder: {ex.Message}", "red");
            await terminal.PressAnyKey();
            return;
        }

        // Build the same candidate list as ShowLoadFailureWithRecovery: primary
        // first, then backup, then most-recent 3 autosaves, then emergency files
        // for this character. Try each in order until one repairs+loads.
        var candidates = new List<(string label, string path)>();

        string primaryPath = System.IO.Path.Combine(saveDir, fileName);
        if (System.IO.File.Exists(primaryPath))
            candidates.Add(("Primary save", primaryPath));

        string baseName = System.IO.Path.GetFileNameWithoutExtension(fileName);

        string backupPath = System.IO.Path.Combine(saveDir, $"{baseName}_backup.json");
        if (System.IO.File.Exists(backupPath))
            candidates.Add(("Backup", backupPath));

        try
        {
            var autosaves = System.IO.Directory.GetFiles(saveDir, $"{baseName}_autosave_*.json")
                .Select(p => new System.IO.FileInfo(p))
                .OrderByDescending(fi => fi.LastWriteTime)
                .Take(3);
            foreach (var fi in autosaves)
                candidates.Add(($"Autosave {fi.LastWriteTime:yyyy-MM-dd HH:mm}", fi.FullName));
        }
        catch { }

        try
        {
            var sanitized = string.Join("_", baseName.Split(System.IO.Path.GetInvalidFileNameChars()));
            var emergencies = System.IO.Directory.GetFiles(saveDir, $"emergency_{sanitized}_*.json")
                .Select(p => new System.IO.FileInfo(p))
                .OrderByDescending(fi => fi.LastWriteTime);
            foreach (var fi in emergencies)
                candidates.Add(($"Emergency {fi.LastWriteTime:yyyy-MM-dd HH:mm}", fi.FullName));
        }
        catch { }

        // Legacy single-file emergency fallback.
        string legacyEmergency = System.IO.Path.Combine(saveDir, "emergency_autosave.json");
        if (System.IO.File.Exists(legacyEmergency))
            candidates.Add(("Legacy emergency", legacyEmergency));

        if (candidates.Count == 0)
        {
            terminal.WriteLine("No save files found to repair.", "red");
            await terminal.PressAnyKey();
            return;
        }

        // The repair writes via Utf8JsonWriter — pick up the indented setting from
        // the file backend so repaired files match the format of normal saves.
        var writeOpts = new System.Text.Json.JsonSerializerOptions { WriteIndented = true };

        foreach (var (label, path) in candidates)
        {
            terminal.WriteLine($"Repairing: {label}...", "yellow");
            terminal.WriteLine($"  {path}", "dark_gray");

            UsurperRemake.Systems.SaveFileRepair.RepairResult result;
            try
            {
                result = UsurperRemake.Systems.SaveFileRepair.RepairInPlace(path, writeOpts);
            }
            catch (Exception ex)
            {
                terminal.WriteLine($"  Repair threw: {ex.GetType().Name}: {ex.Message}", "red");
                continue;
            }

            if (!result.Success)
            {
                terminal.WriteLine($"  Repair failed: {result.ErrorMessage}", "red");
                continue;
            }

            long origMB = result.OriginalSizeBytes / (1024 * 1024);
            long newMB = result.RepairedSizeBytes / (1024 * 1024);
            string sizeChange = origMB > 0
                ? $"{origMB} MB -> {newMB} MB"
                : $"{result.OriginalSizeBytes / 1024} KB -> {result.RepairedSizeBytes / 1024} KB";
            terminal.WriteLine($"  Repair complete: {sizeChange}, {result.TrimmedFields.Count} field(s) trimmed.", "bright_green");

            // If the repaired file isn't already the primary, copy it over.
            if (!string.Equals(path, primaryPath, StringComparison.OrdinalIgnoreCase))
            {
                try
                {
                    System.IO.File.Copy(path, primaryPath, overwrite: true);
                    terminal.WriteLine($"  Restored {label} as primary save.", "bright_green");
                }
                catch (Exception ex)
                {
                    terminal.WriteLine($"  Could not copy repaired file to primary: {ex.Message}", "red");
                    continue;
                }
            }

            terminal.WriteLine("");
            terminal.WriteLine("Attempting to load the repaired save...", "yellow");
            await Task.Delay(800);

            // Verify it loads. LoadSaveByFileName runs the full restoration pipeline.
            // If the repaired file STILL fails (unrecognized bloat surface, malformed
            // JSON in a non-bloat field, etc.), control returns to the load failure
            // handler which will re-show this menu.
            var (verifyData, verifyError) = await SaveSystem.Instance.LoadSaveByFileNameWithError(fileName);
            if (verifyData?.Player != null)
            {
                terminal.WriteLine("Repair succeeded — loading character now.", "bright_green");
                terminal.WriteLine("");
                await Task.Delay(1000);
                await LoadSaveByFileName(fileName);
                return;
            }

            terminal.WriteLine($"  Repair completed but load still failed: {verifyError}", "red");
            terminal.WriteLine("  Trying next candidate...", "yellow");
            terminal.WriteLine("");
        }

        terminal.WriteLine("");
        terminal.WriteLine("All repair attempts failed. The save may have damage beyond bloat", "red");
        terminal.WriteLine("(corrupt JSON, version mismatch, etc.). Please report this with your", "red");
        terminal.WriteLine("save folder so we can investigate.", "red");
        terminal.WriteLine("");
        await terminal.PressAnyKey();
    }

    /// <summary>
    /// Check active quests for NPC targets that are permadead. Auto-complete with rewards
    /// so the player isn't stuck with an impossible quest.
    /// </summary>
    private async Task CleanupDeadNPCQuests(Character player)
    {
        var questsToRemove = new List<Quest>();

        foreach (var quest in player.ActiveQuests)
        {
            // Check quest-level TargetNPCName
            string targetName = quest.TargetNPCName;

            // Also check objective-level targets for TalkToNPC / DefeatNPC
            if (string.IsNullOrEmpty(targetName))
            {
                var npcObjective = quest.Objectives.FirstOrDefault(o =>
                    (o.ObjectiveType == QuestObjectiveType.TalkToNPC ||
                     o.ObjectiveType == QuestObjectiveType.DefeatNPC) &&
                    !string.IsNullOrEmpty(o.TargetId));
                if (npcObjective != null)
                    targetName = npcObjective.TargetId;
            }

            if (string.IsNullOrEmpty(targetName))
                continue;

            // Look up the NPC including dead ones
            var npc = NPCSpawnSystem.Instance.GetNPCByName(targetName, includeDead: true);
            if (npc != null && npc.IsDead)
            {
                questsToRemove.Add(quest);
            }
        }

        if (questsToRemove.Count == 0)
            return;

        terminal.WriteLine("");
        foreach (var quest in questsToRemove)
        {
            string targetDisplay = !string.IsNullOrEmpty(quest.TargetNPCName)
                ? quest.TargetNPCName
                : quest.Objectives.FirstOrDefault(o =>
                    !string.IsNullOrEmpty(o.TargetName))?.TargetName ?? "Unknown";

            // Give the reward
            var rewardAmount = quest.CalculateReward(player.Level);
            if (rewardAmount <= 0) rewardAmount = player.Level * 100;

            player.Gold += rewardAmount;
            player.Statistics?.RecordQuestGoldReward(rewardAmount);
            player.RoyQuests++;
            player.Fame += 5;

            // Clean up the quest
            quest.Deleted = true;
            quest.Occupier = "";
            player.ActiveQuests.Remove(quest);

            terminal.WriteLine($"  Quest Update: {targetDisplay} has perished.", "yellow");
            terminal.WriteLine($"  \"{quest.Title ?? "Quest"}\" auto-completed. Reward: {rewardAmount:N0} gold.", "bright_green");
            terminal.WriteLine("");

            DebugLogger.Instance.LogInfo("QUEST", $"Auto-completed quest '{quest.Title}' for {player.DisplayName} — target NPC '{targetDisplay}' is permadead. Reward: {rewardAmount:N0}g");
        }

        await terminal.PressAnyKey();
    }

    /// <summary>
    /// Process daily login streak — increment or reset streak, display reward screen, apply rewards.
    /// Called once per calendar day during LoadSaveByFileName, after all player data is restored.
    /// </summary>
    private async Task ProcessLoginStreak(Character player)
    {
        string today = DateTime.UtcNow.ToString("yyyy-MM-dd");
        string yesterday = DateTime.UtcNow.AddDays(-1).ToString("yyyy-MM-dd");

        if (player.LastLoginDate == today)
            return; // Already logged in today

        if (player.LastLoginDate == yesterday)
        {
            // Consecutive day — increment streak
            player.LoginStreak++;
        }
        else if (string.IsNullOrEmpty(player.LastLoginDate))
        {
            // First ever login
            player.LoginStreak = 1;
        }
        else
        {
            // Missed a day — reset streak, show message
            if (player.LoginStreak > 1)
            {
                terminal.WriteLine("");
                terminal.SetColor("yellow");
                terminal.WriteLine($"  Your {player.LoginStreak}-day login streak has ended!");
                terminal.WriteLine("");
            }
            player.LoginStreak = 1;
        }

        player.LastLoginDate = today;
        if (player.LoginStreak > player.LongestLoginStreak)
            player.LongestLoginStreak = player.LoginStreak;

        // Calculate and apply rewards
        long goldReward = 0;
        int potionsReward = 0;
        int herbsReward = 0;
        string specialMessage = "";
        string achievementId = "";

        int streak = player.LoginStreak;

        if (streak >= 100)
        {
            goldReward = 1000;
            specialMessage = $"Day {streak} streak! Here's your daily loyalty bonus.";
        }
        else if (streak == 90)
        {
            goldReward = 50000;
            achievementId = "legendary_devotion";
            specialMessage = "90 days! Your legendary devotion is unmatched!";
        }
        else if (streak == 60)
        {
            goldReward = 25000;
            specialMessage = "60 days! Your dedication is truly remarkable!";
        }
        else if (streak == 30)
        {
            goldReward = 10000;
            achievementId = "devoted_champion";
            specialMessage = "30 days! You are a Devoted Champion!";
        }
        else if (streak == 14)
        {
            goldReward = 5000;
            herbsReward = 5;
            specialMessage = "Two weeks straight! Here's a rare herb collection!";
        }
        else if (streak == 7)
        {
            goldReward = 2500;
            achievementId = "dedicated_adventurer";
            specialMessage = "One full week! You're a Dedicated Adventurer!";
        }
        else if (streak == 3)
        {
            goldReward = 1000;
            potionsReward = 3;
            specialMessage = "Three days running! Have some supplies!";
        }
        else if (streak == 1)
        {
            goldReward = 500;
            specialMessage = "Welcome back! Start a new streak!";
        }
        else if (streak == 2)
        {
            goldReward = 500;
            specialMessage = "Two days in a row! Keep it going!";
        }
        else
        {
            // Days between milestones
            goldReward = 500;
        }

        // Display the streak reward
        terminal.WriteLine("");
        if (!GameConfig.ScreenReaderMode)
        {
            terminal.WriteLine("╔══════════════════════════════════════════╗", "bright_yellow");
            terminal.WriteLine($"║         {Loc.Get("engine.daily_login_streak")}               ║", "bright_yellow");
            terminal.WriteLine("╚══════════════════════════════════════════╝", "bright_yellow");
        }
        else
        {
            terminal.WriteLine($"--- {Loc.Get("engine.daily_login_streak")} ---", "bright_yellow");
        }
        terminal.WriteLine("");
        terminal.SetColor("bright_green");
        terminal.WriteLine($"  {Loc.Get("engine.day_streak", streak, player.LongestLoginStreak)}");

        if (!string.IsNullOrEmpty(specialMessage))
        {
            terminal.SetColor("cyan");
            terminal.WriteLine($"  {specialMessage}");
        }

        terminal.WriteLine("");

        // Apply gold
        if (goldReward > 0)
        {
            player.Gold += goldReward;
            terminal.SetColor("bright_yellow");
            terminal.WriteLine($"  +{goldReward:N0} {Loc.Get("ui.gold")}", "bright_yellow");
        }

        // Apply potions
        if (potionsReward > 0)
        {
            player.Healing = Math.Min(player.Healing + potionsReward, 99);
            terminal.WriteLine($"  +{potionsReward} {Loc.Get("engine.healing_potions")}", "bright_green");
        }

        // Apply herbs (one of each type)
        if (herbsReward > 0)
        {
            player.HerbHealing = Math.Min(player.HerbHealing + herbsReward, 99);
            player.HerbIronbark = Math.Min(player.HerbIronbark + herbsReward, 99);
            player.HerbFirebloom = Math.Min(player.HerbFirebloom + herbsReward, 99);
            player.HerbSwiftthistle = Math.Min(player.HerbSwiftthistle + herbsReward, 99);
            player.HerbStarbloom = Math.Min(player.HerbStarbloom + herbsReward, 99);
            terminal.WriteLine($"  +{herbsReward} {Loc.Get("engine.of_each_herb_type")}", "green");
        }

        terminal.WriteLine("");

        // Next milestone hint
        int nextMilestone = streak < 3 ? 3 : streak < 7 ? 7 : streak < 14 ? 14 : streak < 30 ? 30 : streak < 60 ? 60 : streak < 90 ? 90 : 0;
        if (nextMilestone > 0)
        {
            int daysToNext = nextMilestone - streak;
            terminal.SetColor("gray");
            terminal.WriteLine($"  {Loc.Get("engine.next_milestone", nextMilestone, daysToNext)}");
        }
        terminal.WriteLine("");

        await terminal.PressAnyKey();

        // Unlock achievements
        if (!string.IsNullOrEmpty(achievementId))
        {
            AchievementSystem.TryUnlock(player, achievementId);
        }
    }

    /// <summary>
    /// Sync player's King and CTurf flags with the authoritative world state.
    /// Called on login after NPCs and royal court are loaded from world_state.
    /// The player's save may have stale flags if the world changed while they were offline.
    /// </summary>
    private void SyncPlayerFlagsFromWorldState(Character player)
    {
        try
        {
            // Sync King flag: derive from current king identity
            var currentKing = CastleLocation.GetCurrentKing();
            bool shouldBeKing = currentKing != null && currentKing.IsActive &&
                (currentKing.Name == player.DisplayName || currentKing.Name == player.Name2);
            if (player.King != shouldBeKing)
            {
                player.King = shouldBeKing;
                DebugLogger.Instance.LogInfo("ONLINE", $"Synced player King={shouldBeKing} (throne belongs to {currentKing?.Name ?? "nobody"})");
            }

            // Sync CTurf flag: derive from whether this player's team controls the city
            if (!string.IsNullOrEmpty(player.Team))
            {
                var npcs = NPCSpawnSystem.Instance?.ActiveNPCs;
                bool teamHasTurf = npcs != null &&
                    npcs.Any(n => n.Team == player.Team && n.CTurf && n.IsAlive && !n.IsDead);
                if (player.CTurf != teamHasTurf)
                {
                    player.CTurf = teamHasTurf;
                    DebugLogger.Instance.LogInfo("ONLINE", $"Synced player CTurf={teamHasTurf} (team '{player.Team}')");
                }
            }
            else if (player.CTurf)
            {
                // No team but has CTurf — impossible, clear it
                player.CTurf = false;
                DebugLogger.Instance.LogInfo("ONLINE", "Synced player CTurf=false (no team)");
            }
        }
        catch (Exception ex)
        {
            DebugLogger.Instance.LogWarning("ONLINE", $"Failed to sync player flags: {ex.Message}");
        }
    }

    /// <summary>
    /// Load authoritative children and marriage data from world_state.
    /// Called after RestoreStorySystems to override stale player-save data with
    /// the world sim's current state. This ensures players see children born
    /// and marriages formed while they were offline.
    /// </summary>
    private async Task LoadSharedChildrenAndMarriages()
    {
        try
        {
            // Load children from world_state (maintained by WorldSimService)
            var sharedChildren = await OnlineStateManager.Instance!.LoadSharedChildren();
            if (sharedChildren != null)
            {
                // Empty list is valid — means all children aged out or were converted to NPCs
                FamilySystem.Instance.DeserializeChildren(sharedChildren);
                DebugLogger.Instance.LogInfo("ONLINE", $"Children overridden from world_state: {sharedChildren.Count} children");
            }

            // NPC marriages are managed by the world sim (WorldSimService loads them once on startup
            // and persists them to world_state). Individual player sessions must NOT call
            // RestoreMarriages/RestoreAffairs, as that clears the global shared registry and
            // causes cross-player contamination when multiple sessions load concurrently.
            DebugLogger.Instance.LogInfo("ONLINE", "Skipping marriage registry restore — world sim is authority");

            // Load world events from world_state so player sees active plagues, festivals, etc.
            var sharedEvents = await OnlineStateManager.Instance.LoadSharedWorldEvents();
            if (sharedEvents != null)
            {
                // Empty list is valid — means no active world events
                WorldEventSystem.Instance.RestoreFromSaveData(sharedEvents, 0);
                DebugLogger.Instance.LogInfo("ONLINE", $"World events loaded from world_state: {WorldEventSystem.Instance.GetActiveEvents().Count} active events");
            }
        }
        catch (Exception ex)
        {
            DebugLogger.Instance.LogError("ONLINE", $"Failed to load shared children/marriages/events: {ex.Message}");
        }
    }

    /// <summary>
    /// Check if the player was registered as sleeping and show attack report.
    /// If the player was killed while sleeping, apply death state (reduced HP, no mana).
    /// Gold/item/XP losses are already applied by WorldSimulator — we just display the report.
    /// </summary>
    private async Task<string?> HandleSleepReport(SqlSaveBackend backend)
    {
        try
        {
            var username = UsurperRemake.BBS.DoorMode.OnlineUsername;
            if (string.IsNullOrEmpty(username)) return null;

            var sleepInfo = await backend.GetSleepingPlayerInfo(username);
            if (sleepInfo == null) return null;

            // Parse attack log
            var attackLog = new List<JsonNode>();
            try
            {
                var parsed = JsonNode.Parse(sleepInfo.AttackLogJson);
                if (parsed is JsonArray arr)
                {
                    foreach (var entry in arr)
                        if (entry != null) attackLog.Add(entry);
                }
            }
            catch (Exception ex) { DebugLogger.Instance.LogError("ENGINE", $"[ProcessSleepReport] Failed to parse sleep attack log JSON: {ex.Message}"); }

            // Show report if anything happened
            if (attackLog.Count > 0 || sleepInfo.IsDead)
            {
                terminal.WriteLine("");
                terminal.SetColor("bright_cyan");
                terminal.WriteLine("\u2554\u2550\u2550\u2550\u2550\u2550\u2550\u2550\u2550\u2550\u2550\u2550\u2550\u2550\u2550\u2550\u2550\u2550\u2550\u2550\u2550\u2550\u2550\u2550\u2550\u2550\u2550\u2550\u2550\u2550\u2550\u2550\u2550\u2550\u2550\u2550\u2550\u2550\u2550\u2550\u2550\u2550\u2550\u2550\u2550\u2550\u2550\u2550\u2550\u2550\u2550\u2550\u2550\u2550\u2550\u2550\u2550\u2550\u2550\u2550\u2550\u2550\u2550\u2550\u2550\u2550\u2550\u2550\u2550\u2550\u2550\u2550\u2550\u2550\u2550\u2550\u2550\u2557");
                terminal.SetColor("bright_red");
                string locationLabel = sleepInfo.SleepLocation switch
                {
                    "inn" => Loc.Get("engine.sleep_inn"),
                    "home" => Loc.Get("engine.sleep_home"),
                    "castle" => Loc.Get("engine.sleep_castle"),
                    _ => Loc.Get("engine.sleep_dormitory")
                };
                { string sleepTitle = Loc.Get("engine.sleep_report_title", locationLabel); int sl = (76 - sleepTitle.Length) / 2; int sr = 76 - sleepTitle.Length - sl; terminal.WriteLine($"\u2551{new string(' ', sl)}{sleepTitle}{new string(' ', sr)}\u2551"); }
                terminal.SetColor("bright_cyan");
                terminal.WriteLine("\u2560\u2550\u2550\u2550\u2550\u2550\u2550\u2550\u2550\u2550\u2550\u2550\u2550\u2550\u2550\u2550\u2550\u2550\u2550\u2550\u2550\u2550\u2550\u2550\u2550\u2550\u2550\u2550\u2550\u2550\u2550\u2550\u2550\u2550\u2550\u2550\u2550\u2550\u2550\u2550\u2550\u2550\u2550\u2550\u2550\u2550\u2550\u2550\u2550\u2550\u2550\u2550\u2550\u2550\u2550\u2550\u2550\u2550\u2550\u2550\u2550\u2550\u2550\u2550\u2550\u2550\u2550\u2550\u2550\u2550\u2550\u2550\u2550\u2550\u2550\u2550\u2550\u2563");

                foreach (var entry in attackLog)
                {
                    string attacker = entry["attacker"]?.GetValue<string>() ?? "Unknown";
                    string result = entry["result"]?.GetValue<string>() ?? "unknown";
                    long goldStolen = 0;
                    try { goldStolen = entry["gold_stolen"]?.GetValue<long>() ?? 0; } catch (Exception ex) { DebugLogger.Instance.LogError("ENGINE", $"[ProcessSleepReport] Failed to parse gold_stolen: {ex.Message}"); }
                    string? itemStolen = entry["item_stolen"]?.GetValue<string>();
                    long xpLost = 0;
                    try { xpLost = entry["xp_lost"]?.GetValue<long>() ?? 0; } catch (Exception ex) { DebugLogger.Instance.LogError("ENGINE", $"[ProcessSleepReport] Failed to parse xp_lost: {ex.Message}"); }

                    terminal.WriteLine("");

                    // Show guard fights if present
                    var guardFights = entry["guard_fights"];
                    if (guardFights is JsonArray guardArr)
                    {
                        foreach (var gf in guardArr)
                        {
                            if (gf == null) continue;
                            string guardName = gf["guard"]?.GetValue<string>() ?? "Guard";
                            string guardResult = gf["result"]?.GetValue<string>() ?? "unknown";

                            if (guardResult == "guard_won")
                            {
                                terminal.SetColor("bright_green");
                                terminal.WriteLine(Loc.Get("engine.guard_fought_off", guardName, attacker));
                            }
                            else
                            {
                                terminal.SetColor("red");
                                terminal.WriteLine(Loc.Get("engine.guard_defeated", guardName, attacker));
                            }
                        }
                    }

                    // Show main result
                    if (result == "attacker_won")
                    {
                        terminal.SetColor("bright_red");
                        terminal.WriteLine(Loc.Get("engine.attacker_won", attacker));
                        if (goldStolen > 0)
                        {
                            terminal.SetColor("red");
                            terminal.WriteLine(Loc.Get("engine.gold_stolen", goldStolen.ToString("N0")));
                        }
                        if (!string.IsNullOrEmpty(itemStolen))
                        {
                            terminal.SetColor("red");
                            terminal.WriteLine(Loc.Get("engine.item_stolen", itemStolen));
                        }
                        if (xpLost > 0)
                        {
                            terminal.SetColor("red");
                            terminal.WriteLine(Loc.Get("engine.xp_lost", xpLost.ToString("N0")));
                        }
                    }
                    else if (result == "defender_won")
                    {
                        terminal.SetColor("bright_green");
                        terminal.WriteLine(Loc.Get("engine.defender_won", attacker));
                    }
                    else if (result == "guards_repelled")
                    {
                        terminal.SetColor("bright_green");
                        terminal.WriteLine(Loc.Get("engine.guards_repelled", attacker));
                    }
                }

                // Death state
                if (sleepInfo.IsDead)
                {
                    terminal.WriteLine("");
                    terminal.SetColor("bright_red");
                    terminal.WriteLine("  ================================================");
                    terminal.WriteLine(Loc.Get("engine.killed_sleeping"));
                    terminal.WriteLine(Loc.Get("engine.wake_temple"));
                    terminal.WriteLine("  ================================================");

                    // Apply death state — respawn with reduced HP
                    if (currentPlayer != null)
                    {
                        currentPlayer.HP = Math.Max(1, currentPlayer.MaxHP / 4);
                        currentPlayer.Mana = 0;
                    }
                }
                else
                {
                    terminal.WriteLine("");
                    terminal.SetColor("bright_green");
                    terminal.WriteLine(Loc.Get("engine.survived_night"));
                }

                terminal.WriteLine("");
                terminal.SetColor("bright_cyan");
                terminal.WriteLine("\u255a\u2550\u2550\u2550\u2550\u2550\u2550\u2550\u2550\u2550\u2550\u2550\u2550\u2550\u2550\u2550\u2550\u2550\u2550\u2550\u2550\u2550\u2550\u2550\u2550\u2550\u2550\u2550\u2550\u2550\u2550\u2550\u2550\u2550\u2550\u2550\u2550\u2550\u2550\u2550\u2550\u2550\u2550\u2550\u2550\u2550\u2550\u2550\u2550\u2550\u2550\u2550\u2550\u2550\u2550\u2550\u2550\u2550\u2550\u2550\u2550\u2550\u2550\u2550\u2550\u2550\u2550\u2550\u2550\u2550\u2550\u2550\u2550\u2550\u2550\u2550\u2550\u255d");
                terminal.WriteLine("");
                await terminal.PressAnyKey();
            }

            // Unregister from sleeping_players (whether attacked or not)
            await backend.UnregisterSleepingPlayer(username);
            return sleepInfo.SleepLocation;
        }
        catch (Exception ex)
        {
            DebugLogger.Instance.LogError("SLEEP", $"Failed to process sleep report: {ex.Message}");
            return null;
        }
    }

    /// <summary>
    /// Show a "While you were gone" summary for online players on login.
    /// Displays PvP attacks, unread messages, and world news since last logout.
    /// </summary>
    private async Task ShowWhileYouWereGone(SqlSaveBackend backend)
    {
        try
        {
            var username = UsurperRemake.BBS.DoorMode.OnlineUsername;
            if (string.IsNullOrEmpty(username)) return;

            // Get the player's last logout time
            var lastLogout = await backend.GetLastLogoutTime(username);
            if (lastLogout == null) return; // First login ever, skip

            // Query all data sources
            var pvpAttacks = await backend.GetPvPAttacksAgainst(username, lastLogout.Value);
            var news = await backend.GetNewsSince(lastLogout.Value, 15);
            var allMessages = await backend.GetUnreadMessages(username);

            // Filter to direct messages only (not broadcasts)
            var directMessages = allMessages
                .Where(m => !string.Equals(m.ToPlayer, "*", StringComparison.OrdinalIgnoreCase))
                .Take(5)
                .ToList();

            // Get unread mail and pending trade counts
            int unreadMail = backend.GetUnreadMailCount(username);
            int pendingTrades = backend.GetPendingTradeOfferCount(username);

            // If nothing happened, skip silently
            if (pvpAttacks.Count == 0 && directMessages.Count == 0 && news.Count == 0
                && unreadMail == 0 && pendingTrades == 0)
                return;

            // Phase 8: emit graphical "While You Were Gone" overlay. Text path
            // below still renders for terminal users / SR mode.
            if (GameConfig.ElectronMode)
            {
                var feed = new ElectronBridge.NewsFeedData
                {
                    CharacterName = username,
                    LastSeen = lastLogout.Value.ToString("o"),
                    UnreadMailCount = unreadMail,
                    PendingTradeCount = pendingTrades,
                    UnreadMessages = directMessages.Count
                };

                if (pvpAttacks.Count > 0)
                {
                    var section = new ElectronBridge.NewsFeedSection { Title = Loc.Get("engine.arena_attacks"), Icon = "sword" };
                    foreach (var attack in pvpAttacks)
                    {
                        bool defenderWon = attack.WinnerUsername.Equals(username, StringComparison.OrdinalIgnoreCase)
                            || attack.WinnerUsername.Equals(attack.DefenderName, StringComparison.OrdinalIgnoreCase);
                        section.Items.Add(new ElectronBridge.NewsFeedItem
                        {
                            Text = defenderWon
                                ? $"You defeated {attack.AttackerName} in self-defense"
                                : $"{attack.AttackerName} defeated you",
                            Type = "pvp_attack",
                            GoldDelta = defenderWon ? attack.GoldStolen : -attack.GoldStolen,
                            IsGood = defenderWon,
                            IsBad = !defenderWon
                        });
                    }
                    feed.Sections.Add(section);
                }

                if (news.Count > 0)
                {
                    var section = new ElectronBridge.NewsFeedSection { Title = "World News", Icon = "scroll" };
                    foreach (var n in news)
                    {
                        section.Items.Add(new ElectronBridge.NewsFeedItem
                        {
                            Text = n.Message ?? "",
                            Type = n.Category ?? "world_event",
                            Timestamp = n.CreatedAt.ToString("o")
                        });
                    }
                    feed.Sections.Add(section);
                }

                if (directMessages.Count > 0)
                {
                    var section = new ElectronBridge.NewsFeedSection { Title = "Messages", Icon = "letter" };
                    foreach (var msg in directMessages)
                    {
                        string msgText = msg.Message.Length > 80 ? msg.Message.Substring(0, 77) + "..." : msg.Message;
                        section.Items.Add(new ElectronBridge.NewsFeedItem
                        {
                            Text = $"[{msg.FromPlayer}]: {msgText}",
                            Type = "system",
                            Timestamp = msg.CreatedAt.ToString("o")
                        });
                    }
                    feed.Sections.Add(section);
                }

                ElectronBridge.EmitNewsFeed(feed);
            }

            terminal.WriteLine("");
            if (GameConfig.ScreenReaderMode)
            {
                terminal.SetColor("bright_yellow");
                terminal.WriteLine(Loc.Get("engine.while_you_were_gone"));
            }
            else
            {
                string wyweTitle = Loc.Get("engine.while_you_were_gone");
                const int wyweInner = 78;
                int wyweLeft  = (wyweInner - wyweTitle.Length) / 2;
                int wyweRight = wyweInner - wyweTitle.Length - wyweLeft;

                terminal.SetColor("bright_cyan");
                terminal.WriteLine("╔══════════════════════════════════════════════════════════════════════════════╗");
                terminal.Write("║");
                terminal.SetColor("bright_yellow");
                terminal.Write(new string(' ', wyweLeft) + wyweTitle + new string(' ', wyweRight));
                terminal.SetColor("bright_cyan");
                terminal.WriteLine("║");
                terminal.WriteLine("╠══════════════════════════════════════════════════════════════════════════════╣");
            }

            // --- PvP Attacks Section ---
            if (pvpAttacks.Count > 0)
            {
                terminal.WriteLine("");
                terminal.SetColor("bright_yellow");
                terminal.WriteLine(Loc.Get("engine.arena_attacks"));
                foreach (var attack in pvpAttacks)
                {
                    bool defenderWon = attack.WinnerUsername.Equals(username, StringComparison.OrdinalIgnoreCase)
                        || attack.WinnerUsername.Equals(attack.DefenderName, StringComparison.OrdinalIgnoreCase);

                    if (defenderWon)
                    {
                        terminal.SetColor("bright_green");
                        string goldMsg = attack.GoldStolen > 0 ? Loc.Get("engine.pvp_you_gained_gold", attack.GoldStolen.ToString("N0")) : "";
                        terminal.WriteLine(Loc.Get("engine.pvp_shadow_won", attack.AttackerName, goldMsg));
                    }
                    else
                    {
                        terminal.SetColor("bright_red");
                        string goldMsg = attack.GoldStolen > 0 ? Loc.Get("engine.pvp_they_stole_gold", attack.GoldStolen.ToString("N0")) : "";
                        terminal.WriteLine(Loc.Get("engine.pvp_attacker_won", attack.AttackerName, goldMsg));
                    }
                }
            }

            // --- Messages Section ---
            if (directMessages.Count > 0)
            {
                terminal.WriteLine("");
                terminal.SetColor("bright_yellow");
                terminal.WriteLine(Loc.Get("engine.messages_section", unreadMail));
                foreach (var msg in directMessages)
                {
                    terminal.SetColor("cyan");
                    string msgText = msg.Message.Length > 60 ? msg.Message.Substring(0, 57) + "..." : msg.Message;
                    terminal.WriteLine($"  [{msg.FromPlayer}]: {msgText}");
                }
                if (unreadMail > directMessages.Count)
                {
                    terminal.SetColor("gray");
                    terminal.WriteLine(Loc.Get("engine.read_mailbox"));
                }
            }
            else if (unreadMail > 0)
            {
                terminal.WriteLine("");
                terminal.SetColor("bright_yellow");
                terminal.WriteLine(Loc.Get("engine.mail_section", unreadMail));
                terminal.SetColor("gray");
                terminal.WriteLine(Loc.Get("engine.read_mail"));
            }

            // --- Trade Packages Section ---
            if (pendingTrades > 0)
            {
                terminal.WriteLine("");
                terminal.SetColor("bright_yellow");
                terminal.WriteLine(Loc.Get("engine.trade_section", pendingTrades));
                terminal.SetColor("cyan");
                terminal.WriteLine(Loc.Get("engine.trade_waiting", pendingTrades, pendingTrades != 1 ? "s" : ""));
                terminal.SetColor("gray");
                terminal.WriteLine(Loc.Get("engine.trade_hint"));
            }

            // --- World News Section ---
            if (news.Count > 0)
            {
                terminal.WriteLine("");
                terminal.SetColor("bright_yellow");
                terminal.WriteLine(Loc.Get("engine.world_news"));
                int newsShown = 0;
                foreach (var entry in news)
                {
                    if (newsShown >= 10) break;

                    string color = entry.Category?.ToLower() switch
                    {
                        "combat" => "red",
                        "pvp" => "bright_yellow",
                        "politics" => "bright_cyan",
                        "romance" => "magenta",
                        "economy" => "yellow",
                        "quest" => "green",
                        _ => "white"
                    };
                    terminal.SetColor(color);
                    string newsText = entry.Message.Length > 72 ? entry.Message.Substring(0, 69) + "..." : entry.Message;
                    terminal.WriteLine($"  * {newsText}");
                    newsShown++;
                }
            }

            terminal.WriteLine("");
            if (!GameConfig.ScreenReaderMode)
            {
                terminal.SetColor("bright_cyan");
                terminal.WriteLine("╚══════════════════════════════════════════════════════════════════════════════╝");
            }
            terminal.WriteLine("");

            // Mark direct messages as read so they don't re-appear via chat polling
            if (directMessages.Count > 0)
                await backend.MarkMessagesRead(username);

            await terminal.PressAnyKey();
        }
        catch (Exception ex)
        {
            // Fail silently - this is a cosmetic feature that should never block login
            DebugLogger.Instance.LogError("WYWEG", $"Failed to show 'While you were gone': {ex.Message}");
        }
    }

    /// <summary>
    /// Get user data path (cross-platform)
    /// </summary>
    private string GetUserDataPath()
    {
        var appName = "UsurperReloaded";

        if (System.Environment.OSVersion.Platform == PlatformID.Win32NT)
        {
            return System.IO.Path.Combine(System.Environment.GetFolderPath(System.Environment.SpecialFolder.ApplicationData), appName);
        }
        else if (System.Environment.OSVersion.Platform == PlatformID.Unix)
        {
            var home = System.Environment.GetEnvironmentVariable("HOME");
            return System.IO.Path.Combine(home ?? "/tmp", ".local", "share", appName);
        }
        else
        {
            var home = System.Environment.GetEnvironmentVariable("HOME");
            return System.IO.Path.Combine(home ?? "/tmp", "Library", "Application Support", appName);
        }
    }
    
    /// <summary>
    /// Load existing game from save file
    /// </summary>
    private async Task LoadExistingGame(string playerName)
    {
        terminal.WriteLine(Loc.Get("save.loading"), "yellow");

        // Clear dungeon party before loading to prevent state leak between saves
        ClearDungeonParty();

        var saveData = await SaveSystem.Instance.LoadGame(playerName);
        if (saveData == null)
        {
            terminal.WriteLine(Loc.Get("engine.load_game_failed"), "red");
            await Task.Delay(2000);
            return;
        }

        // Restore player from save data
        currentPlayer = RestorePlayerFromSaveData(saveData.Player);

        // Load daily system state
        dailyManager.LoadFromSaveData(saveData);
        
        // Restore world state
        await RestoreWorldState(saveData.WorldState);
        
        // Restore NPCs - from player's save as baseline
        await RestoreNPCs(saveData.NPCs);

        // In online mode, override NPCs and royal court with world_state (authoritative source).
        // The world sim maintains shared state 24/7 - player saves may be stale.
        if (UsurperRemake.BBS.DoorMode.IsOnlineMode && OnlineStateManager.Instance != null)
        {
            // Load current NPCs from world_state (teams, levels, equipment, etc.)
            var sharedNpcs = await OnlineStateManager.Instance.LoadSharedNPCs();
            if (sharedNpcs != null && sharedNpcs.Count > 0)
            {
                await RestoreNPCs(sharedNpcs);
                DebugLogger.Instance.LogInfo("ONLINE", $"NPCs overridden from world_state: {sharedNpcs.Count} NPCs loaded");
            }

            // Load settlement from world_state (authoritative source, not stale player save)
            await OnlineStateManager.Instance.LoadSettlementFromWorldState();
        }

        // Restore story systems (companions, children, seals, etc.)
        // In online mode, only restore this player's god entry — other players' stale
        // snapshots would overwrite their current worship choices in the shared GodSystem.
        string? godFilter = (UsurperRemake.BBS.DoorMode.IsOnlineMode && currentPlayer != null)
            ? currentPlayer.Name2 : null;
        SaveSystem.Instance.RestoreStorySystems(saveData.StorySystems, godFilter);

        // In online mode, override royal court, children, and marriages with world_state
        // (authoritative source). RestoreStorySystems loaded stale data from the player's
        // save — the world sim maintains these 24/7 while the player is offline.
        if (UsurperRemake.BBS.DoorMode.IsOnlineMode && OnlineStateManager.Instance != null)
        {
            await OnlineStateManager.Instance.LoadRoyalCourtFromWorldState();
            await LoadSharedChildrenAndMarriages();
        }

        // Online mode: sync player King and CTurf flags with authoritative world state.
        if (UsurperRemake.BBS.DoorMode.IsOnlineMode && currentPlayer != null)
        {
            SyncPlayerFlagsFromWorldState(currentPlayer);
        }


        terminal.WriteLine(Loc.Get("engine.game_loaded", saveData.CurrentDay, saveData.Player.TurnsRemaining), "green");
        await Task.Delay(1500);

        // World sim catch-up: fast-forward the world based on time away (single-player only)
        await RunWorldSimCatchUp(saveData.SaveTime);

        // Check if daily reset is needed after loading
        await dailyManager.CheckDailyReset();

        // Enter the game world
        await EnterGameWorld();
    }
    
    /// <summary>
    /// Create new game
    /// </summary>
    private async Task CreateNewGame(string playerName)
    {
        // v0.60.0 beta-launch event: refuse to create a character during the Rage
        // window. The god of intolerance does not care that the mortal is brand
        // new -- he sees the intent to play, scoffs, and erases the attempt.
        // Online mode only.
        if (UsurperRemake.Systems.RageEventSystem.IsActive && UsurperRemake.BBS.DoorMode.IsOnlineMode)
        {
            var rageUsername = UsurperRemake.Server.SessionContext.Current?.Username ?? playerName;
            // Pass null player -- there's nothing to delete yet, but the cinematic
            // still plays and the kicker disconnects the session afterward.
            await UsurperRemake.Systems.RageEventSystem.RunRageEventAsync(null!, terminal, rageUsername);
            IsIntentionalExit = true;
            return;
        }

        // Reset per-player session systems for new game
        var storyDbg = StoryProgressionSystem.Instance;
        bool isNgPlus = storyDbg.CurrentCycle > 1;
        Console.Error.WriteLine($"[NG+] CreateNewGame: cycle={storyDbg.CurrentCycle}, isNgPlus={isNgPlus}, endings=[{string.Join(",", storyDbg.CompletedEndings)}]");

        // Capture the previous-life character's display name BEFORE we wipe currentPlayer.
        // We need it for the NG+ child-disown step below in online mode — children are
        // matched to parents by name, and FamilySystem isn't reset in online mode (world
        // data is shared across players), so without this step the new NG+ character
        // walks into Home and sees their previous-cycle kids waiting at the table.
        string? previousLifeName = isNgPlus ? currentPlayer?.Name : null;

        UsurperRemake.Systems.RomanceTracker.Instance.Reset();
        UsurperRemake.Systems.CompanionSystem.Instance?.ResetAllCompanions();
        if (isNgPlus)
        {
            // NG+ — StartNewCycle already reset story state while preserving cycle/endings/seals.
            // Don't call FullReset which would wipe cycle counter and completed endings.
            Console.Error.WriteLine($"[NG+] Preserving cycle data (skipping FullReset)");

            // God worship does NOT carry across cycles. The fresh Character has WorshippedGod=""
            // by default, but GodSystem.playerGods is a separate in-memory dict keyed by player
            // name that survives the cycle (the old save was overwritten, not deleted, so the
            // DeleteSave cleanup never ran). Without this clear, the player's old god still
            // counts them as a believer and the temple / pantheon / /health screens report
            // residual worship from the prior cycle.
            try
            {
                UsurperRemake.GodSystemSingleton.Instance?.SetPlayerGod(playerName, "");
            }
            catch (Exception ex) { DebugLogger.Instance.Log(DebugLogger.LogLevel.Debug, "NG+", $"GodSystem clear failed: {ex.Message}"); }
        }
        else
        {
            Console.Error.WriteLine($"[NG+] NOT NG+ — calling FullReset");
            UsurperRemake.Systems.StoryProgressionSystem.Instance.FullReset();
        }
        UsurperRemake.Systems.ArchetypeTracker.Instance.Reset();
        UsurperRemake.Systems.FactionSystem.Instance.Reset();

        // Reset narrative systems for new game
        UsurperRemake.Systems.StrangerEncounterSystem.Instance.Reset();
        UsurperRemake.Systems.TownNPCStorySystem.Instance.Reset();
        UsurperRemake.Systems.DreamSystem.Instance.Reset();
        UsurperRemake.Systems.OceanPhilosophySystem.Instance.Reset();
        UsurperRemake.Systems.GriefSystem.Instance.Reset();

        // In online mode, world-level systems (NPCs, children, marriages) are shared
        // across all players and managed by the WorldSimService. Don't reset them
        // when a single player creates a new character.
        if (!UsurperRemake.BBS.DoorMode.IsOnlineMode)
        {
            UsurperRemake.Systems.FamilySystem.Instance.Reset();
            UsurperRemake.Systems.NPCSpawnSystem.Instance.ResetNPCs();
            WorldInitializerSystem.Instance.ResetWorld();
            NPCMarriageRegistry.Instance.Reset();
            NPCDialogueDatabase.ClearAllTracking();
            RelationshipSystem.Instance.Reset();
        }
        else
        {
            // Online mode: only clear this player's relationships, preserve NPC-to-NPC
            RelationshipSystem.Instance.ResetPlayerRelationships(playerName);

            // Online NG+ child disown: in single-player mode FamilySystem.Reset() above
            // wipes _children entirely, but in online mode children are world data and
            // can't be globally reset. Without this step the new NG+ character would
            // still be parented to all the previous cycle's kids by name match — see
            // v0.57.17 release notes for the player report that surfaced this.
            // Children parented by anyone else (NPC partners, other players) are
            // untouched. Both-parents-empty minor children move to the orphanage so
            // the existing orphan pipeline picks them up.
            if (isNgPlus && !string.IsNullOrEmpty(previousLifeName))
            {
                int disowned = UsurperRemake.Systems.FamilySystem.Instance.DisownChildrenOf(previousLifeName);
                if (disowned > 0)
                {
                    DebugLogger.Instance.LogInfo("NG+", $"Online NG+ disowned {disowned} children of {previousLifeName}");
                }
            }
        }

        // Initialize per-session daily state for new character
        SessionCurrentDay = 1;
        SessionLastResetTime = DateTime.Now;
        SessionDailyCycleMode = DailyCycleMode.Endless;

        // Clear dungeon party from previous saves
        ClearDungeonParty();

        // Create new player using character creation system
        var newCharacter = await CreateNewPlayer(playerName);
        if (newCharacter == null)
        {
            return; // Player cancelled creation
        }

        currentPlayer = (Character)newCharacter;

        // Apply SysOp's default color theme to new characters
        currentPlayer.ColorTheme = GameConfig.DefaultColorTheme;
        ColorTheme.Current = GameConfig.DefaultColorTheme;

        // Inherit pre-login screen reader toggle into new character
        currentPlayer.ScreenReaderMode = GameConfig.ScreenReaderMode;

        // Inherit pre-login compact mode and language into new character
        currentPlayer.CompactMode = GameConfig.CompactMode;
        currentPlayer.DisableCharacterMonsterArt = GameConfig.DisableCharacterMonsterArt;
        currentPlayer.Language = GameConfig.Language;

        // Auto-populate quickbar with starting spells/abilities
        AutoPopulateQuickbar(currentPlayer);

        // Apply NG+ cycle bonuses to the fresh character
        if (isNgPlus)
        {
            var story = StoryProgressionSystem.Instance;
            var lastEnding = story.CompletedEndings.Count > 0
                ? story.CompletedEndings.Last()
                : EndingType.Defiant; // fallback
            CycleSystem.Instance.ApplyCycleBonusesToNewCharacter(currentPlayer, story.CurrentCycle, lastEnding);
            currentPlayer.RecalculateStats();
        }

        // Save the new game using the character's actual name (Name1)
        // This is important because playerName may be empty if coming from no-saves path
        string savePlayerName = !string.IsNullOrEmpty(currentPlayer.Name1) ? currentPlayer.Name1 : currentPlayer.Name2;
        var success = await SaveSystem.Instance.SaveGame(savePlayerName, currentPlayer);
        if (success)
        {
            terminal.WriteLine(Loc.Get("engine.new_game_saved"), "green");
        }
        else
        {
            terminal.WriteLine(Loc.Get("engine.save_warning"), "red");
        }

        await Task.Delay(1500);

        // Online news: announce new adventurer and update display name
        if (UsurperRemake.Systems.OnlineStateManager.IsActive)
        {
            // NG+ in online mode: persist the disowned-children change (and other shared
            // story state mutated during NG+ setup) to world_state immediately. Otherwise
            // a quick logout / world-sim restart before the next periodic snapshot would
            // reload the previous-cycle children with parents intact.
            if (isNgPlus)
            {
                _ = UsurperRemake.Systems.OnlineStateManager.Instance!.SaveAllSharedState();
            }

            var displayName = currentPlayer.Name2 ?? currentPlayer.Name1;
            var className = currentPlayer.ClassName;
            _ = UsurperRemake.Systems.OnlineStateManager.Instance!.AddNews(
                $"A new adventurer arrives! {displayName} the {className} begins their journey.", "quest");
            // Update online_players display_name from BBS username to character name
            await OnlineStateManager.Instance!.UpdateDisplayName(displayName);

            // Update RoomRegistry session so "Also here" shows display name, not account name
            var ctx = UsurperRemake.Server.SessionContext.Current;
            var srv = UsurperRemake.Server.MudServer.Instance;
            if (ctx != null && srv != null && srv.ActiveSessions.TryGetValue(ctx.Username?.ToLowerInvariant() ?? "", out var sess))
            {
                sess.ActiveCharacterName = displayName;
            }
        }

        // Play the opening story sequence
        // This establishes the mystery, the goal, and hooks the player
        var openingSystem = OpeningStorySystem.Instance;
        if (StoryProgressionSystem.Instance.CurrentCycle > 1)
        {
            // NG+ has a different opening that acknowledges the cycle
            await openingSystem.PlayNewGamePlusOpening(currentPlayer, terminal);
        }
        else
        {
            // First playthrough - full opening sequence
            await openingSystem.PlayOpeningSequence(currentPlayer, terminal);
        }

        // Show Getting Started summary for brand new characters
        if (currentPlayer.Statistics.TotalMonstersKilled == 0)
        {
            HintSystem.Instance.TryShowHint(HintSystem.HINT_GETTING_STARTED, terminal, currentPlayer.HintsShown);
            await terminal.PressAnyKey();
        }

        // Captain Aldric's Mission — opening guided quest for new characters
        if (!isNgPlus && currentPlayer.Statistics.TotalMonstersKilled == 0)
        {
            terminal.ClearScreen();
            terminal.WriteLine("");
            terminal.SetColor("bright_yellow");
            terminal.WriteLine($"  {Loc.Get("aldric_quest.soldier_appears")}");
            terminal.WriteLine("");
            terminal.SetColor("white");
            terminal.WriteLine($"  {Loc.Get("aldric_quest.intro")}");
            terminal.WriteLine("");
            terminal.SetColor("gray");
            terminal.WriteLine($"  {Loc.Get("aldric_quest.studies_you")}");
            terminal.WriteLine("");
            terminal.SetColor("white");
            terminal.WriteLine($"  {Loc.Get("aldric_quest.lost_contact")}");
            terminal.WriteLine($"  {Loc.Get("aldric_quest.need_someone")}");
            terminal.WriteLine($"  {Loc.Get("aldric_quest.just_enter")}");
            terminal.WriteLine($"  {Loc.Get("aldric_quest.for_treasure")}");
            terminal.WriteLine("");
            terminal.SetColor("bright_cyan");
            terminal.WriteLine($"  {Loc.Get("aldric_quest.hands_scroll")}");
            terminal.WriteLine("");
            terminal.SetColor("yellow");
            terminal.WriteLine($"  {Loc.Get("aldric_quest.quest_received")}");
            terminal.SetColor("gray");
            terminal.WriteLine($"    {Loc.Get("aldric_quest.obj_enter")}");
            terminal.WriteLine($"    {Loc.Get("aldric_quest.obj_defeat")}");
            terminal.WriteLine($"    {Loc.Get("aldric_quest.obj_treasure")}");
            terminal.WriteLine($"    {Loc.Get("aldric_quest.obj_report")}");
            terminal.WriteLine("");
            terminal.SetColor("white");
            terminal.WriteLine($"  {Loc.Get("aldric_quest.good_luck")}");

            terminal.WriteLine("");
            currentPlayer.HintsShown.Add("aldric_quest_active");
            await terminal.PressAnyKey();
        }

        // Register online presence and broadcast login now that character is created
        if (OnlineStateManager.IsActive)
        {
            var ngDisplayName = currentPlayer.Name2 ?? currentPlayer.Name1 ?? UsurperRemake.BBS.DoorMode.GetPlayerName() ?? "Unknown";
            var ngConnType = OnlineStateManager.Instance!.DeferredConnectionType;
            await OnlineStateManager.Instance!.StartOnlineTracking(ngDisplayName, ngConnType);

            // Mark session as in-game
            var ngMudServer = UsurperRemake.Server.MudServer.Instance;
            var ngSessionKey = UsurperRemake.BBS.DoorMode.GetPlayerName()?.ToLowerInvariant() ?? "";
            UsurperRemake.Server.PlayerSession? ngPlayerSession = null;
            if (ngMudServer != null && ngSessionKey.Length > 0)
                ngMudServer.ActiveSessions.TryGetValue(ngSessionKey, out ngPlayerSession);
            if (ngPlayerSession != null)
                ngPlayerSession.IsInGame = true;

            // Global login announcement
            if (ngMudServer != null && ngPlayerSession != null && !ngPlayerSession.IsWizInvisible)
            {
                ngMudServer.BroadcastToAll(
                    $"\u001b[1;33m  {ngDisplayName} has entered the realm. [{ngConnType}]\u001b[0m",
                    excludeUsername: ngSessionKey);

                // New player welcome broadcast (not NG+ rerolls)
                if (!isNgPlus)
                {
                    ngMudServer.BroadcastToAll(
                        $"\u001b[1;36m  *** {ngDisplayName} is a new adventurer! Type /gos to welcome them! ***\u001b[0m",
                        excludeUsername: ngSessionKey);
                }
            }
        }

        // Enter the game world
        await EnterGameWorld();
    }

    /// <summary>
    /// Enter the main game world
    /// </summary>
    private async Task EnterGameWorld()
    {
        if (currentPlayer == null) return;

        // Log game start
        bool isNewGame = currentPlayer.Statistics?.TotalSessionsPlayed <= 1;
        DebugLogger.Instance.LogGameStart(currentPlayer.Name, isNewGame);

        // Set notable player name for distant world news references
        WorldEventSystem.Instance.NotablePlayerName ??= currentPlayer.Name;

        // Initialize NPCs only if they haven't been initialized yet
        // The NPCSpawnSystem has a guard flag to prevent duplicate spawning
        if (NPCSpawnSystem.Instance.ActiveNPCs.Count == 0)
        {
            await NPCSpawnSystem.Instance.InitializeClassicNPCs();

            // Initialize the world with simulated history (100 days of activity)
            // This creates teams, establishes a King, city control, guards, etc.
            if (!WorldInitializerSystem.Instance.IsWorldInitialized)
            {
                terminal.WriteLine(Loc.Get("engine.world_stirs"), "cyan");
                await WorldInitializerSystem.Instance.InitializeWorld(100);
                terminal.WriteLine(Loc.Get("engine.history_written"), "bright_green");
            }
        }

        // Ensure quests exist (also regenerates stale starter quests with corrected data)
        QuestSystem.EnsureQuestsExist();

        // Check if player is allowed to play
        if (!currentPlayer.Allowed)
        {
            terminal.WriteLine(Loc.Get("engine.not_allowed"), "red");
            await Task.Delay(2000);
            return;
        }

        // Check daily limits (but not in endless mode)
        if (dailyManager.CurrentMode != DailyCycleMode.Endless && !await CheckDailyLimits())
        {
            terminal.WriteLine(Loc.Get("engine.turns_used", currentPlayer.TurnsRemaining), "red");
            await Task.Delay(2000);
            return;
        }

        // Check if player is dead - handle death and continue playing
        if (!currentPlayer.IsAlive)
        {
            await HandleDeath();
            // After death handling, player is resurrected - continue to game
            // (HandleDeath sets HP > 0 and saves)
        }

        // Enter main game using location system
        // Immortals always go to the Pantheon; sleeping players wake where they slept; others use saved location or MainStreet
        GameLocation startLocation;
        if (currentPlayer.IsImmortal)
        {
            startLocation = GameLocation.Pantheon;
        }
        else if (_sleepLocationOnLogin != null)
        {
            startLocation = _sleepLocationOnLogin switch
            {
                "inn" => GameLocation.TheInn,
                "home" => GameLocation.Home,
                "castle" => GameLocation.Castle,
                _ => GameLocation.MainStreet  // dormitory, street, unknown
            };
        }
        else
        {
            startLocation = currentPlayer.Location > 0
                ? (GameLocation)currentPlayer.Location
                : GameLocation.MainStreet;
        }
        // Override start location if player is in prison
        if (currentPlayer.DaysInPrison > 0)
            startLocation = GameLocation.Prison;

        await locationManager.EnterLocation(startLocation, currentPlayer);
    }
    
    /// <summary>
    /// Restore player from save data
    /// </summary>
    private Character RestorePlayerFromSaveData(PlayerData playerData)
    {
        Character player = new Player
        {
            // Unique player identifier (critical for romance/family systems)
            ID = !string.IsNullOrEmpty(playerData.Id) ? playerData.Id : (playerData.Name2 ?? playerData.Name1 ?? Guid.NewGuid().ToString()),

            Name1 = playerData.Name1 ?? "",
            Name2 = playerData.Name2 ?? "",
            RealName = !string.IsNullOrEmpty(playerData.RealName) ? playerData.RealName : (playerData.Name1 ?? ""),
            Level = playerData.Level,
            Experience = playerData.Experience,
            HP = playerData.HP,
            MaxHP = playerData.MaxHP,
            Gold = playerData.Gold,
            BankGold = playerData.BankGold,
            BankGuard = playerData.BankGuard,
            BankWage = playerData.BankWage,
            Loan = playerData.BankLoan,
            Interest = playerData.BankInterest,
            BankRobberyAttempts = playerData.BankRobberyAttempts,
            TempleResurrectionsUsed = playerData.TempleResurrectionsUsed,

            // Resurrection & Church
            Resurrections = playerData.Resurrections,
            ResurrectionsUsed = playerData.ResurrectionsUsed,
            MaxResurrections = playerData.MaxResurrections > 0 ? playerData.MaxResurrections : 3,
            BannedFromChurch = playerData.BannedFromChurch,
            BlessingsReceived = playerData.BlessingsReceived,
            ChurchDonations = playerData.ChurchDonations,
            SacrificesMade = playerData.SacrificesMade,
            HealingsReceived = playerData.HealingsReceived,
            HasHolyWater = playerData.HasHolyWater,
            BardSongsLeft = playerData.BardSongsLeft,

            // Kingdom & Crime
            DaysInPower = playerData.DaysInPower,
            Thievery = playerData.Thievery,
            WantedLvl = playerData.WantedLvl,
            TaxRelief = playerData.TaxRelief,
            RoyTaxPaid = playerData.RoyTaxPaid,
            KingVotePoll = (byte)playerData.KingVotePoll,
            KingLastVote = (byte)playerData.KingLastVote,
            ThroneChallengedToday = playerData.ThroneChallengedToday,
            TotalExecutions = playerData.TotalExecutions,
            ExecutionsToday = playerData.ExecutionsToday,
            PlayerImprisonedToday = playerData.PlayerImprisonedToday,
            NPCsImprisonedToday = playerData.NPCsImprisonedToday,

            // Family & Misc
            Kids = playerData.Kids,
            DisRes = (byte)playerData.DisRes,
            AgePlus = playerData.AgePlus,

            // Attributes
            Strength = playerData.Strength,
            Defence = playerData.Defence,
            Stamina = playerData.Stamina,
            Agility = playerData.Agility,
            Charisma = playerData.Charisma,
            Dexterity = playerData.Dexterity,
            Wisdom = playerData.Wisdom,
            Intelligence = playerData.Intelligence,
            Constitution = playerData.Constitution,
            Mana = playerData.Mana,
            MaxMana = playerData.MaxMana,

            // Equipment and items (CRITICAL FIXES)
            Healing = playerData.Healing,     // POTIONS
            ManaPotions = playerData.ManaPotions, // MANA POTIONS
            Antidotes = playerData.Antidotes,     // ANTIDOTES
            WeapPow = playerData.WeapPow,     // WEAPON POWER
            ArmPow = playerData.ArmPow,       // ARMOR POWER

            // Character details
            Race = playerData.Race,
            Class = playerData.Class,
            Sex = (CharacterSex)playerData.Sex,
            Orientation = (SexualOrientation)playerData.Orientation,
            Age = playerData.Age,
            Difficulty = playerData.Difficulty,
            
            // Game state
            TurnCount = playerData.TurnCount,  // World simulation turn counter
            TurnsRemaining = playerData.TurnsRemaining,
            GameTimeMinutes = playerData.GameTimeMinutes > 0 ? playerData.GameTimeMinutes : GameConfig.NewGameStartHour * 60,
            DaysInPrison = (byte)playerData.DaysInPrison,
            IsMurderConvict = playerData.IsMurderConvict,
            CellDoorOpen = playerData.CellDoorOpen,
            RescuedBy = playerData.RescuedBy ?? "",
            PrisonEscapes = (byte)playerData.PrisonEscapes,

            // Daily limits
            Fights = playerData.Fights,
            PFights = playerData.PFights,
            TFights = playerData.TFights,
            Thiefs = playerData.Thiefs,
            Brawls = playerData.Brawls,
            Assa = playerData.Assa,
            DarkNr = playerData.DarkNr > 0 ? playerData.DarkNr : GameConfig.DefaultDarkDeeds,
            ChivNr = playerData.ChivNr > 0 ? playerData.ChivNr : GameConfig.DefaultGoodDeeds,
            
            // Status — v0.57.12: retroactive paired-movement heal for pre-v0.57.12 saves that exceeded AlignmentCap
            // through bypass mutation sites. HealOverflow reduces the opposite scale by excess/2 and clamps.
            Chivalry = AlignmentSystem.HealOverflowChivalry(playerData.Chivalry, playerData.Darkness),
            Darkness = AlignmentSystem.HealOverflowDarkness(playerData.Chivalry, playerData.Darkness),
            Fame = playerData.Fame,
            Mental = playerData.Mental,
            Poison = playerData.Poison,
            PoisonTurns = playerData.PoisonTurns,

            // Active status effects (convert int keys back to StatusEffect enum)
            ActiveStatuses = playerData.ActiveStatuses?.ToDictionary(
                kvp => (StatusEffect)kvp.Key,
                kvp => kvp.Value
            ) ?? new Dictionary<StatusEffect, int>(),

            GnollP = playerData.GnollP,
            Addict = playerData.Addict,
            SteroidDays = playerData.SteroidDays,
            DrugEffectDays = playerData.DrugEffectDays,
            ActiveDrug = (DrugType)playerData.ActiveDrug,
            Mercy = playerData.Mercy,

            // Disease status
            Blind = playerData.Blind,
            Plague = playerData.Plague,
            Smallpox = playerData.Smallpox,
            Measles = playerData.Measles,
            Leprosy = playerData.Leprosy,
            LoversBane = playerData.LoversBane,

            // Divine Wrath System
            DivineWrathLevel = playerData.DivineWrathLevel,
            AngeredGodName = playerData.AngeredGodName ?? "",
            BetrayedForGodName = playerData.BetrayedForGodName ?? "",
            DivineWrathPending = playerData.DivineWrathPending,
            DivineWrathTurnsRemaining = playerData.DivineWrathTurnsRemaining,

            // Royal Loan
            RoyalLoanAmount = playerData.RoyalLoanAmount,
            RoyalLoanDueDay = playerData.RoyalLoanDueDay,
            RoyalLoanBountyPosted = playerData.RoyalLoanBountyPosted,

            // Noble Title
            NobleTitle = playerData.NobleTitle,
            // Migration: pre-0.54.6 saves stored IsKnighted as a computed property.
            // If player has Sir/Dame title but IsKnighted is false, they were knighted before the flag existed.
            IsKnighted = playerData.IsKnighted || playerData.NobleTitle == "Sir" || playerData.NobleTitle == "Dame",

            // Royal Mercenaries
            RoyalMercenaries = playerData.RoyalMercenaries?.Select(m => new RoyalMercenary
            {
                Name = m.Name,
                Role = m.Role,
                Class = (CharacterClass)m.ClassId,
                Sex = (CharacterSex)m.Sex,
                Level = m.Level,
                HP = m.HP,
                MaxHP = m.MaxHP,
                Mana = m.Mana,
                MaxMana = m.MaxMana,
                Strength = m.Strength,
                Defence = m.Defence,
                WeapPow = m.WeapPow,
                ArmPow = m.ArmPow,
                Agility = m.Agility,
                Dexterity = m.Dexterity,
                Wisdom = m.Wisdom,
                Intelligence = m.Intelligence,
                Constitution = m.Constitution,
                Healing = m.Healing
            }).ToList() ?? new(),

            // Blood Price / Murder Weight System
            MurderWeight = playerData.MurderWeight,
            PermakillLog = playerData.PermakillLog ?? new(),
            LastMurderWeightDecay = playerData.LastMurderWeightDecay,

            // Immortal Ascension System
            HasEarnedAltSlot = playerData.HasEarnedAltSlot,
            IsImmortal = playerData.IsImmortal,
            DivineName = playerData.DivineName ?? "",
            GodLevel = playerData.GodLevel,
            GodExperience = playerData.GodExperience,
            DeedsLeft = playerData.DeedsLeft,
            GodAlignment = playerData.GodAlignment ?? "",
            AscensionDate = playerData.AscensionDate,
            WorshippedGod = playerData.WorshippedGod ?? "",
            DivineBlessingCombats = playerData.DivineBlessingCombats,
            DivineBlessingBonus = playerData.DivineBlessingBonus,
            DivineBoonConfig = playerData.DivineBoonConfig ?? "",
            MudTitle = playerData.MudTitle ?? "",

            // Combat statistics (kill/death counts)
            MKills = playerData.MKills,
            MDefeats = playerData.MDefeats,
            PKills = playerData.PKills,
            PDefeats = playerData.PDefeats,

            // Character settings
            DevMenuUsed = playerData.DevMenuUsed,
            AutoHeal = playerData.AutoHeal,
            CombatSpeed = playerData.CombatSpeed,
            SkipIntimateScenes = playerData.SkipIntimateScenes,
            ScreenReaderMode = playerData.ScreenReaderMode,
            CompactMode = playerData.CompactMode,
            DisableCharacterMonsterArt = playerData.DisableCharacterMonsterArt,
            Language = playerData.Language ?? "en",
            ColorTheme = playerData.ColorTheme,
            AutoLevelUp = playerData.AutoLevelUp,
            AutoEquipDisabled = playerData.AutoEquipDisabled,
            DateFormatPreference = playerData.DateFormatPreference,
            AutoRedistributeXP = playerData.AutoRedistributeXP,
            TeamXPPercent = playerData.TeamXPPercent ?? TeamXPConfig.DefaultTeamXPPercent.ToArray(),
            TeamXPIsExplicit = playerData.TeamXPIsExplicit,
            Loyalty = playerData.Loyalty,
            Haunt = playerData.Haunt,
            Master = playerData.Master,
            WellWish = playerData.WellWish,

            // Physical appearance
            Height = playerData.Height,
            Weight = playerData.Weight,
            Eyes = playerData.Eyes,
            Hair = playerData.Hair,
            Skin = playerData.Skin,

            // Ruler status
            King = playerData.King,

            // Social/Team
            Team = playerData.Team,
            TeamPW = playerData.TeamPassword,
            CTurf = playerData.IsTeamLeader,
            TeamRec = playerData.TeamRec,
            BGuard = playerData.BGuard,
            CityTaxEarnedThisWeek = playerData.CityTaxEarnedThisWeek,
            CityTaxEarnedLifetime = playerData.CityTaxEarnedLifetime,

            Allowed = true // Always allow loaded players
        };

        // Restore character flavor text
        if (playerData.Phrases?.Count > 0)
        {
            player.Phrases = playerData.Phrases;
        }

        if (playerData.Description?.Count > 0)
        {
            player.Description = playerData.Description;
        }
        
        // Restore items (ensure lists are always initialized)
        player.Item = playerData.Items?.Length > 0
            ? playerData.Items.ToList()
            : new List<int>();

        player.ItemType = playerData.ItemTypes?.Length > 0
            ? playerData.ItemTypes.Select(i => (ObjType)i).ToList()
            : new List<ObjType>();

        // Restore dynamic equipment FIRST (before EquippedItems, so IDs are registered)
        // In single-player, clear all dynamic equipment to avoid stale entries from previous saves.
        // In MUD/online mode, DO NOT clear — EquipmentDatabase is static and shared across all
        // player sessions. Clearing it wipes other players' equipment, causing items to disappear
        // or show wrong items in wrong slots (e.g., another player's longbow in your ring slot).
        if (!UsurperRemake.BBS.DoorMode.IsOnlineMode)
        {
            EquipmentDatabase.ClearDynamicEquipment();
        }

        // In MUD mode, we must remap dynamic equipment IDs to fresh unique IDs.
        // Multiple players can have overlapping saved IDs (e.g., both have ID 100000),
        // and RegisterDynamicWithId would overwrite one player's equipment with another's.
        // This caused equipment to vanish or have wrong stats on save/reload.
        var equipIdRemap = new Dictionary<int, int>();
        DynamicEquipIdRemap = equipIdRemap;

        if (playerData.DynamicEquipment != null && playerData.DynamicEquipment.Count > 0)
        {
            foreach (var equipData in playerData.DynamicEquipment)
            {
                var equipment = new Equipment
                {
                    Name = equipData.Name,
                    Description = equipData.Description ?? "",
                    Slot = (EquipmentSlot)equipData.Slot,
                    WeaponPower = equipData.WeaponPower,
                    ArmorClass = equipData.ArmorClass,
                    ShieldBonus = equipData.ShieldBonus,
                    BlockChance = equipData.BlockChance,
                    StrengthBonus = equipData.StrengthBonus,
                    DexterityBonus = equipData.DexterityBonus,
                    ConstitutionBonus = equipData.ConstitutionBonus,
                    IntelligenceBonus = equipData.IntelligenceBonus,
                    WisdomBonus = equipData.WisdomBonus,
                    CharismaBonus = equipData.CharismaBonus,
                    MaxHPBonus = equipData.MaxHPBonus,
                    MaxManaBonus = equipData.MaxManaBonus,
                    DefenceBonus = equipData.DefenceBonus,
                    MinLevel = equipData.MinLevel,
                    Value = equipData.Value,
                    IsCursed = equipData.IsCursed,
                    Rarity = (EquipmentRarity)equipData.Rarity,
                    WeaponType = (WeaponType)equipData.WeaponType,
                    Handedness = (WeaponHandedness)equipData.Handedness,
                    ArmorType = (ArmorType)equipData.ArmorType,
                    StaminaBonus = equipData.StaminaBonus,
                    AgilityBonus = equipData.AgilityBonus,
                    CriticalChanceBonus = equipData.CriticalChanceBonus,
                    CriticalDamageBonus = equipData.CriticalDamageBonus,
                    MagicResistance = equipData.MagicResistance,
                    PoisonDamage = equipData.PoisonDamage,
                    LifeSteal = equipData.LifeSteal,
                    HasFireEnchant = equipData.HasFireEnchant,
                    HasFrostEnchant = equipData.HasFrostEnchant,
                    HasLightningEnchant = equipData.HasLightningEnchant,
                    HasPoisonEnchant = equipData.HasPoisonEnchant,
                    HasHolyEnchant = equipData.HasHolyEnchant,
                    HasShadowEnchant = equipData.HasShadowEnchant,
                    ManaSteal = equipData.ManaSteal,
                    ArmorPiercing = equipData.ArmorPiercing,
                    Thorns = equipData.Thorns,
                    HPRegen = equipData.HPRegen,
                    ManaRegen = equipData.ManaRegen,
                    WeightClass = (ArmorWeightClass)equipData.WeightClass,
                    StrengthRequired = equipData.StrengthRequired,
                    RequiresGood = equipData.RequiresGood,
                    RequiresEvil = equipData.RequiresEvil,
                    ClassRestrictions = equipData.ClassRestrictions?.Select(c => (CharacterClass)c).ToList() ?? new List<CharacterClass>(),
                    IsUnique = equipData.IsUnique,
                    HasBossSlayer = equipData.HasBossSlayer,
                    HasTitanResolve = equipData.HasTitanResolve,
                    IsIdentified = equipData.IsIdentified
                };

                // Migration: loot items may have wrong WeaponType — either None (legacy) or
                // Sword (multi-monster equip path defaulted to Sword instead of inferring).
                // Re-infer from name on load so weapon requirements work correctly.
                // v0.57.13: skip shields — they live in OffHand with ShieldBonus/BlockChance set,
                // and InferWeaponType has no shield keywords so it would rewrite them to Sword +
                // OneHanded, letting IsDualWielding fire an off-hand strike with the shield.
                // v0.60.0: name-based detection added as a third signal alongside
                // ShieldBonus / BlockChance / WeaponType. Catches shields that
                // were mangled by a pre-v0.53.13 load and saved with all stat-
                // side signals stripped: the new name match rescues them on
                // load instead of leaving them perpetually mis-typed as Sword.
                bool isShield = equipment.ShieldBonus > 0 || equipment.BlockChance > 0
                    || equipment.WeaponType == WeaponType.Shield
                    || equipment.WeaponType == WeaponType.Buckler
                    || equipment.WeaponType == WeaponType.TowerShield
                    || (equipment.Slot == EquipmentSlot.OffHand
                        && ShopItemGenerator.LooksLikeShieldByName(equipment.Name));
                if ((equipment.Slot == EquipmentSlot.MainHand || equipment.Slot == EquipmentSlot.OffHand) && !isShield)
                {
                    var inferred = ShopItemGenerator.InferWeaponType(equipment.Name);
                    if (equipment.WeaponType != inferred)
                    {
                        equipment.WeaponType = inferred;
                        equipment.Handedness = ShopItemGenerator.InferHandedness(inferred);
                    }
                }
                else if (isShield && equipment.Slot == EquipmentSlot.OffHand)
                {
                    // Heal legacy shields that were mangled by a prior load
                    if (equipment.WeaponType != WeaponType.Shield
                        && equipment.WeaponType != WeaponType.Buckler
                        && equipment.WeaponType != WeaponType.TowerShield)
                    {
                        equipment.WeaponType = ShopItemGenerator.InferShieldType(equipment.Name);
                    }
                    equipment.Handedness = WeaponHandedness.OffHandOnly;
                }

                if (UsurperRemake.BBS.DoorMode.IsOnlineMode)
                {
                    // MUD mode: assign fresh unique ID to avoid collisions with other players
                    int newId = EquipmentDatabase.RegisterDynamic(equipment);
                    if (newId != equipData.Id)
                    {
                        equipIdRemap[equipData.Id] = newId;
                    }
                }
                else
                {
                    // Single-player: use original IDs (no collision risk)
                    EquipmentDatabase.RegisterDynamicWithId(equipment, equipData.Id);
                }
            }
        }

        // Restore equipment system — remap IDs if needed (MUD mode collision avoidance)
        if (playerData.EquippedItems != null && playerData.EquippedItems.Count > 0)
        {
            player.EquippedItems = playerData.EquippedItems.ToDictionary(
                kvp => (EquipmentSlot)kvp.Key,
                kvp => equipIdRemap.TryGetValue(kvp.Value, out int newId) ? newId : kvp.Value
            );
        }

        // Safety check: if any equipped item can't be found in the database, remove it
        // (this catches corrupted saves where equipment definitions were lost)
        var slotsToUnequip = new List<EquipmentSlot>();
        foreach (var kvp in player.EquippedItems)
        {
            var equip = EquipmentDatabase.GetById(kvp.Value);
            if (equip == null)
            {
                slotsToUnequip.Add(kvp.Key);
                DebugLogger.Instance?.LogWarning("EQUIP", $"Removing orphaned equipment reference from {kvp.Key} (ID {kvp.Value} not found in database)");
            }
        }
        foreach (var slot in slotsToUnequip)
        {
            player.EquippedItems.Remove(slot);
        }

        // Restore curse status for equipped items
        player.WeaponCursed = playerData.WeaponCursed;
        player.ArmorCursed = playerData.ArmorCursed;
        player.ShieldCursed = playerData.ShieldCursed;

        // Restore player inventory (dungeon loot items)
        if (playerData.Inventory != null && playerData.Inventory.Count > 0)
        {
            player.Inventory = playerData.Inventory.Select(itemData =>
            {
                var item = new Item
                {
                    Name = itemData.Name,
                    Value = itemData.Value,
                    Type = itemData.Type,
                    Attack = itemData.Attack,
                    Armor = itemData.Armor,
                    Strength = itemData.Strength,
                    Dexterity = itemData.Dexterity,
                    Wisdom = itemData.Wisdom,
                    Defence = itemData.Defence,
                    BlockChance = itemData.BlockChance,
                    ShieldBonus = itemData.ShieldBonus,
                    HP = itemData.HP,
                    Mana = itemData.Mana,
                    Charisma = itemData.Charisma,
                    Agility = itemData.Agility,
                    Stamina = itemData.Stamina,
                    MinLevel = itemData.MinLevel,
                    IsCursed = itemData.IsCursed,
                    Cursed = itemData.Cursed,
                    IsIdentified = itemData.IsIdentified,
                    Shop = itemData.Shop,
                    Dungeon = itemData.Dungeon,
                    Description = itemData.Description?.ToList() ?? new List<string>()
                };
                if (itemData.LootEffects != null)
                    item.LootEffects = itemData.LootEffects.Select(e => (e.EffectType, e.Value)).ToList();
                return item;
            }).ToList();
        }

        // Restore base stats
        player.BaseStrength = playerData.BaseStrength > 0 ? playerData.BaseStrength : playerData.Strength;
        player.BaseDexterity = playerData.BaseDexterity > 0 ? playerData.BaseDexterity : playerData.Dexterity;
        player.BaseConstitution = playerData.BaseConstitution > 0 ? playerData.BaseConstitution : playerData.Constitution;
        player.BaseIntelligence = playerData.BaseIntelligence > 0 ? playerData.BaseIntelligence : playerData.Intelligence;
        player.BaseWisdom = playerData.BaseWisdom > 0 ? playerData.BaseWisdom : playerData.Wisdom;
        player.BaseCharisma = playerData.BaseCharisma > 0 ? playerData.BaseCharisma : playerData.Charisma;
        player.BaseMaxHP = playerData.BaseMaxHP > 0 ? playerData.BaseMaxHP : playerData.MaxHP;
        player.BaseMaxMana = playerData.BaseMaxMana > 0 ? playerData.BaseMaxMana : playerData.MaxMana;
        player.BaseDefence = playerData.BaseDefence > 0 ? playerData.BaseDefence : playerData.Defence;
        player.BaseStamina = playerData.BaseStamina > 0 ? playerData.BaseStamina : playerData.Stamina;
        player.BaseAgility = playerData.BaseAgility > 0 ? playerData.BaseAgility : playerData.Agility;

        // If this is an old save without equipment data, initialize from WeapPow/ArmPow
        if ((playerData.EquippedItems == null || playerData.EquippedItems.Count == 0)
            && (playerData.WeapPow > 0 || playerData.ArmPow > 0))
        {
            // Migration: Find best matching equipment based on WeapPow/ArmPow
            MigrateOldEquipmentToNew(player, playerData.WeapPow, playerData.ArmPow);
        }

        // Parse location
        if (int.TryParse(playerData.CurrentLocation, out var locationId))
        {
            player.Location = locationId;
        }

        // Restore romance tracker data
        if (playerData.RomanceData != null)
        {
            UsurperRemake.Systems.RomanceTracker.Instance.LoadFromSaveData(playerData.RomanceData);
        }

        // Restore learned combat abilities
        if (playerData.LearnedAbilities?.Count > 0)
        {
            player.LearnedAbilities = new HashSet<string>(playerData.LearnedAbilities);
        }

        // Restore combat quickbar (with migration for existing saves)
        if (playerData.Quickbar != null && playerData.Quickbar.Any(s => s != null))
        {
            player.Quickbar = playerData.Quickbar.ToList();
            while (player.Quickbar.Count < 9) player.Quickbar.Add(null);
        }
        else
        {
            AutoPopulateQuickbar(player);
        }

        // Restore training system
        player.Trains = playerData.Trains;
        player.TrainingPoints = playerData.TrainingPoints;
        if (playerData.SkillProficiencies?.Count > 0)
        {
            player.SkillProficiencies = playerData.SkillProficiencies.ToDictionary(
                kvp => kvp.Key,
                kvp => (TrainingSystem.ProficiencyLevel)kvp.Value);
        }
        if (playerData.SkillTrainingProgress?.Count > 0)
        {
            player.SkillTrainingProgress = new Dictionary<string, int>(playerData.SkillTrainingProgress);
        }

        // v0.57.14: restore per-channel chat mutes
        if (playerData.MutedChannels != null && playerData.MutedChannels.Count > 0)
        {
            player.MutedChannels = new HashSet<string>(playerData.MutedChannels, StringComparer.OrdinalIgnoreCase);
        }

        // Restore gold-based stat training (v0.30.9)
        if (playerData.StatTrainingCounts?.Count > 0)
            player.StatTrainingCounts = new Dictionary<string, int>(playerData.StatTrainingCounts);
        if (playerData.UnpaidWageDays?.Count > 0)
            player.UnpaidWageDays = new Dictionary<string, int>(playerData.UnpaidWageDays);
        if (playerData.CraftingMaterials?.Count > 0)
            player.CraftingMaterials = new Dictionary<string, int>(playerData.CraftingMaterials);

        // Restore spells and skills (ensure lists are never null)
        if (playerData.Spells?.Count > 0)
        {
            player.Spell = playerData.Spells;
        }
        else if (player.Spell == null)
        {
            player.Spell = new List<List<bool>>();
        }
        if (playerData.Skills?.Count > 0)
        {
            player.Skill = playerData.Skills;
        }
        else if (player.Skill == null)
        {
            player.Skill = new List<int>();
        }

        // Restore legacy equipment slots
        player.LHand = playerData.LHand;
        player.RHand = playerData.RHand;
        player.Head = playerData.Head;
        player.Body = playerData.Body;
        player.Arms = playerData.Arms;
        player.LFinger = playerData.LFinger;
        player.RFinger = playerData.RFinger;
        player.Legs = playerData.Legs;
        player.Feet = playerData.Feet;
        player.Waist = playerData.Waist;
        player.Neck = playerData.Neck;
        player.Neck2 = playerData.Neck2;
        player.Face = playerData.Face;
        player.Shield = playerData.Shield;
        player.Hands = playerData.Hands;
        player.ABody = playerData.ABody;

        // Restore combat flags
        player.Immortal = playerData.Immortal;
        player.BattleCry = playerData.BattleCry ?? "";
        player.BGuardNr = playerData.BGuardNr;

        // Restore gym cooldown timers
        player.LastStrengthTraining = playerData.LastStrengthTraining;
        player.LastDexterityTraining = playerData.LastDexterityTraining;
        player.LastTugOfWar = playerData.LastTugOfWar;
        player.LastWrestling = playerData.LastWrestling;

        // Set the global difficulty mode based on the loaded player
        // Defense-in-depth: online mode is always Normal difficulty regardless of saved data
        // (some legacy characters were created before the online gate was added)
        if (UsurperRemake.BBS.DoorMode.IsOnlineMode && player.Difficulty != DifficultyMode.Normal)
        {
            DebugLogger.Instance.LogWarning("ONLINE", $"Player {player.Name2 ?? player.Name1} had non-Normal difficulty ({player.Difficulty}) in online mode — forcing Normal");
            player.Difficulty = DifficultyMode.Normal;
        }
        DifficultySystem.CurrentDifficulty = player.Difficulty;

        // Load player statistics (or initialize if not present)
        if (playerData.Statistics != null)
        {
            player.Statistics = playerData.Statistics;
            player.Statistics.TrackNewSession();
        }
        else
        {
            player.Statistics = new PlayerStatistics();
            player.Statistics.TrackNewSession();
        }
        StatisticsManager.Current = player.Statistics;

        // Load player achievements (or initialize if not present)
        if (playerData.AchievementsData != null)
        {
            player.Achievements.UnlockedAchievements = new HashSet<string>(playerData.AchievementsData.UnlockedAchievements);
            player.Achievements.UnlockDates = new Dictionary<string, DateTime>(playerData.AchievementsData.UnlockDates);
        }
        else
        {
            player.Achievements = new PlayerAchievements();
        }

        // Initialize achievement system
        AchievementSystem.Initialize();

        // Restore Home Upgrade System (v0.44.0 overhaul)
        player.HomeLevel = playerData.HomeLevel;
        player.ChestLevel = playerData.ChestLevel;
        player.TrainingRoomLevel = playerData.TrainingRoomLevel;
        player.GardenLevel = playerData.GardenLevel;
        player.BedLevel = playerData.BedLevel;
        player.HearthLevel = playerData.HearthLevel;
        player.HasTrophyRoom = playerData.HasTrophyRoom;
        player.HasTeleportCircle = playerData.HasTeleportCircle;
        player.HasLegendaryArmory = playerData.HasLegendaryArmory;
        player.HasVitalityFountain = playerData.HasVitalityFountain;
        player.HasStudy = playerData.HasStudy;
        player.HasServants = playerData.HasServants;
        player.HasReinforcedDoor = playerData.HasReinforcedDoor;
        player.PermanentDamageBonus = playerData.PermanentDamageBonus;
        player.PermanentDefenseBonus = playerData.PermanentDefenseBonus;
        player.BonusMaxHP = playerData.BonusMaxHP;
        player.BonusWeapPow = playerData.BonusWeapPow;
        player.BonusArmPow = playerData.BonusArmPow;
        player.HomeRestsToday = playerData.HomeRestsToday;
        player.HerbsGatheredToday = playerData.HerbsGatheredToday;
        player.WellRestedCombats = playerData.WellRestedCombats;
        player.WellRestedBonus = playerData.WellRestedBonus;
        player.Fatigue = playerData.Fatigue;
        player.LoversBlissCombats = playerData.LoversBlissCombats;
        player.LoversBlissBonus = playerData.LoversBlissBonus;
        player.CycleExpMultiplier = playerData.CycleExpMultiplier;

        // Restore herb pouch inventory (v0.48.5)
        player.HerbHealing = playerData.HerbHealing;
        player.HerbIronbark = playerData.HerbIronbark;
        player.HerbFirebloom = playerData.HerbFirebloom;
        player.HerbSwiftthistle = playerData.HerbSwiftthistle;
        player.HerbStarbloom = playerData.HerbStarbloom;
        player.HerbBuffType = playerData.HerbBuffType;
        player.HerbBuffCombats = playerData.HerbBuffCombats;
        player.HerbBuffValue = playerData.HerbBuffValue;
        player.HerbExtraAttacks = playerData.HerbExtraAttacks;

        // Restore God Slayer buff (post-Old God power fantasy)
        player.GodSlayerCombats = playerData.GodSlayerCombats;
        player.GodSlayerDamageBonus = playerData.GodSlayerDamageBonus;
        player.GodSlayerDefenseBonus = playerData.GodSlayerDefenseBonus;

        // Restore song buff properties (Music Shop performances)
        player.SongBuffType = playerData.SongBuffType;
        player.SongBuffCombats = playerData.SongBuffCombats;
        player.SongBuffValue = playerData.SongBuffValue;
        player.SongBuffValue2 = playerData.SongBuffValue2;
        player.HeardLoreSongs = playerData.HeardLoreSongs != null ? new HashSet<int>(playerData.HeardLoreSongs) : new HashSet<int>();

        // Restore food buff properties (Inn meals)
        player.FoodBuffType = playerData.FoodBuffType;
        player.FoodBuffCombats = playerData.FoodBuffCombats;
        player.FoodBuffValue = playerData.FoodBuffValue;
        player.MealsToday = playerData.MealsToday;
        player.InnDuelsToday = playerData.InnDuelsToday;
        player.TavernStrangerTalkedToday = playerData.TavernStrangerTalkedToday;

        // Dungeon Settlements & Wilderness (v0.49.4)
        player.VisitedSettlements = playerData.VisitedSettlements != null ? new HashSet<string>(playerData.VisitedSettlements) : new HashSet<string>();
        player.SettlementLoreRead = playerData.SettlementLoreRead != null ? new HashSet<string>(playerData.SettlementLoreRead) : new HashSet<string>();
        player.WildernessExplorationsToday = playerData.WildernessExplorationsToday;
        player.WildernessRevisitsToday = playerData.WildernessRevisitsToday;
        player.WildernessDiscoveries = playerData.WildernessDiscoveries != null ? new HashSet<string>(playerData.WildernessDiscoveries) : new HashSet<string>();

        // Dark Pact & Evil Deed tracking (v0.49.4)
        player.DarkPactCombats = playerData.DarkPactCombats;
        player.DarkPactDamageBonus = playerData.DarkPactDamageBonus;
        player.HasShatteredSealFragment = playerData.HasShatteredSealFragment;
        player.HasTouchedTheVoid = playerData.HasTouchedTheVoid;

        // NPC Settlement buffs (v0.49.5)
        player.SettlementBuffType = playerData.SettlementBuffType;
        player.SettlementBuffCombats = playerData.SettlementBuffCombats;
        player.SettlementBuffValue = playerData.SettlementBuffValue;
        player.SettlementGoldClaimedToday = playerData.SettlementGoldClaimedToday;
        player.SettlementHerbClaimedToday = playerData.SettlementHerbClaimedToday;
        player.SettlementShrineUsedToday = playerData.SettlementShrineUsedToday;
        player.SettlementCircleUsedToday = playerData.SettlementCircleUsedToday;
        player.SettlementWorkshopUsedToday = playerData.SettlementWorkshopUsedToday;
        player.WorkshopBuffCombats = playerData.WorkshopBuffCombats;

        // Restore chest contents
        var playerKey = (player is Player pp ? pp.RealName : player.Name2) ?? player.Name2;
        if (playerData.ChestContents != null && playerData.ChestContents.Count > 0)
        {
            var chestItems = playerData.ChestContents.Select(data =>
            {
                var chestItem = new global::Item
                {
                    Name = data.Name,
                    Value = data.Value,
                    Type = data.Type,
                    Attack = data.Attack,
                    Armor = data.Armor,
                    Strength = data.Strength,
                    Dexterity = data.Dexterity,
                    Wisdom = data.Wisdom,
                    Defence = data.Defence,
                    BlockChance = data.BlockChance,
                    ShieldBonus = data.ShieldBonus,
                    HP = data.HP,
                    Mana = data.Mana,
                    Charisma = data.Charisma,
                    Agility = data.Agility,
                    Stamina = data.Stamina,
                    MinLevel = data.MinLevel,
                    IsCursed = data.IsCursed,
                    IsIdentified = data.IsIdentified,
                    Shop = data.Shop,
                    Dungeon = data.Dungeon,
                    Description = data.Description?.ToList() ?? new List<string>()
                };
                if (data.LootEffects != null)
                    chestItem.LootEffects = data.LootEffects.Select(e => (e.EffectType, e.Value)).ToList();
                return chestItem;
            }).ToList();
            UsurperRemake.Locations.HomeLocation.PlayerChests[playerKey] = chestItems;
        }
        else
        {
            UsurperRemake.Locations.HomeLocation.PlayerChests[playerKey] = new List<global::Item>();
        }

        // Faction consumable properties (v0.40.2)
        player.PoisonCoatingCombats = playerData.PoisonCoatingCombats;
        player.ActivePoisonType = (PoisonType)playerData.ActivePoisonType;
        player.PoisonVials = playerData.PoisonVials;
        player.SmokeBombs = playerData.SmokeBombs;
        player.InnerSanctumLastDay = playerData.InnerSanctumLastDay;

        // Daily tracking (real-world-date based)
        player.LastDailyResetBoundary = playerData.LastDailyResetBoundary;
        player.LastPrayerRealDate = playerData.LastPrayerRealDate;
        player.LastInnerSanctumRealDate = playerData.LastInnerSanctumRealDate;
        player.LastBindingOfSoulsRealDate = playerData.LastBindingOfSoulsRealDate;
        player.SethFightsToday = playerData.SethFightsToday;
        player.ArmWrestlesToday = playerData.ArmWrestlesToday;
        player.SethDefeatsTotal = playerData.SethDefeatsTotal;
        player.RoyQuestsToday = playerData.RoyQuestsToday;
        player.Quests = playerData.Quests;
        player.RoyQuests = playerData.RoyQuests;

        // Dark Alley Overhaul (v0.41.0)
        player.GroggoShadowBlessingDex = playerData.GroggoShadowBlessingDex;
        player.SteroidShopPurchases = playerData.SteroidShopPurchases;
        player.AlchemistINTBoosts = playerData.AlchemistINTBoosts;
        player.GamblingRoundsToday = playerData.GamblingRoundsToday;
        player.PitFightsToday = playerData.PitFightsToday;
        player.DesecrationsToday = playerData.DesecrationsToday;
        player.ConfessionsToday = playerData.ConfessionsToday;
        player.MurdersToday = playerData.MurdersToday;
        player.TeamWarsToday = playerData.TeamWarsToday;
        player.DrinkingGamesToday = playerData.DrinkingGamesToday;
        player.LastPartnerBondingUtc = playerData.LastPartnerBondingUtc;
        player.LoanAmount = playerData.LoanAmount;
        player.LoanDaysRemaining = playerData.LoanDaysRemaining;
        player.LoanInterestAccrued = playerData.LoanInterestAccrued;
        player.DarkAlleyReputation = playerData.DarkAlleyReputation;
        player.DrugTolerance = playerData.DrugTolerance ?? new Dictionary<int, int>();
        // SafeHouseResting is cleared on login — player is no longer hiding
        player.SafeHouseResting = false;

        // Restore daily counters (v0.53.12)
        player.DrinksLeft = (byte)playerData.DrinksLeft;
        player.PrisonsLeft = playerData.PrisonsLeft;
        player.ExecuteLeft = playerData.ExecuteLeft;
        player.QuestsLeft = playerData.QuestsLeft;
        player.PrisonActivitiesToday = playerData.PrisonActivitiesToday;

        // Restore Daily Login Streak (v0.52.0)
        player.LoginStreak = playerData.LoginStreak;
        player.LongestLoginStreak = playerData.LongestLoginStreak;
        player.LastLoginDate = playerData.LastLoginDate ?? "";

        // Restore Blood Moon Event (v0.52.0)
        player.BloodMoonDay = playerData.BloodMoonDay;
        player.IsBloodMoon = playerData.IsBloodMoon;

        // Restore Weekly Power Rankings (v0.52.0)
        player.WeeklyRank = playerData.WeeklyRank;
        player.PreviousWeeklyRank = playerData.PreviousWeeklyRank;
        player.RivalName = playerData.RivalName ?? "";
        player.RivalLevel = playerData.RivalLevel;

        // Restore Recurring Duelist Rival
        if (playerData.RecurringDuelist != null)
        {
            string playerId = player.ID ?? player.Name;
            DungeonLocation.RestoreRecurringDuelist(playerId,
                playerData.RecurringDuelist.Name,
                playerData.RecurringDuelist.Weapon,
                playerData.RecurringDuelist.Level,
                playerData.RecurringDuelist.TimesEncountered,
                playerData.RecurringDuelist.PlayerWins,
                playerData.RecurringDuelist.PlayerLosses,
                playerData.RecurringDuelist.WasInsulted,
                playerData.RecurringDuelist.IsDead);
        }

        // Restore dungeon progression (cleared boss/seal floors)
        if (playerData.ClearedSpecialFloors != null)
        {
            player.ClearedSpecialFloors = playerData.ClearedSpecialFloors;
        }

        // Restore dungeon floor states from save data (room exploration, cleared rooms, etc.)
        // The original boss-room-showing-cleared bug has been properly fixed via the BossDefeated flag,
        // so we no longer need to nuke all floor state on every load.
        if (playerData.DungeonFloorStates != null && playerData.DungeonFloorStates.Count > 0)
        {
            player.DungeonFloorStates = RestoreDungeonFloorStates(playerData.DungeonFloorStates);
            DebugLogger.Instance?.LogDebug("LOAD", $"Restored dungeon floor states for {playerData.DungeonFloorStates.Count} floors");
        }
        else
        {
            player.DungeonFloorStates = new Dictionary<int, UsurperRemake.Systems.DungeonFloorState>();
            DebugLogger.Instance?.LogDebug("LOAD", "No dungeon floor states in save data - starting fresh");
        }

        // Restore player's active quests directly from save data into the quest database.
        // This runs BEFORE RestoreWorldState, so we merge player quests into the database here.
        // RestoreWorldState will then add unclaimed board quests. Both paths are needed because
        // PlayerData.ActiveQuests has claimed quests and WorldState.ActiveQuests has unclaimed ones.
        if (playerData.ActiveQuests != null && playerData.ActiveQuests.Count > 0)
        {
            player.ActiveQuests.Clear();
            var playerName = player.Name2 ?? player.Name1 ?? "";
            QuestSystem.MergePlayerQuests(playerName, playerData.ActiveQuests);
            var dbQuests = QuestSystem.GetPlayerQuests(playerName);
            foreach (var q in dbQuests)
            {
                if (!player.ActiveQuests.Contains(q))
                    player.ActiveQuests.Add(q);
            }
            DebugLogger.Instance.LogDebug("LOAD", $"Restored {player.ActiveQuests.Count} active quests for {playerName}");
        }

        // Restore hint system (which hints have been shown to this player)
        if (playerData.HintsShown != null)
        {
            player.HintsShown = playerData.HintsShown;
        }

        // CRITICAL: Recalculate stats to apply equipment bonuses from loaded items
        // This ensures WeapPow, ArmPow, and all stat bonuses are correctly applied
        //
        // BUG FIX: We must preserve HP/Mana before RecalculateStats because:
        // 1. RecalculateStats sets MaxHP = BaseMaxHP first (which is lower than final MaxHP)
        // 2. Equipment's ApplyToCharacter sees HP > MaxHP and clamps it down
        // 3. Then constitution/equipment bonuses raise MaxHP back up
        // 4. But HP is already clamped to the intermediate lower value
        //
        // Solution: Save HP/Mana, recalculate, then restore and clamp to final MaxHP
        var savedHP = player.HP;
        var savedMana = player.Mana;

        player.RecalculateStats();

        // Restore the saved HP/Mana, clamped to the newly calculated MaxHP/MaxMana
        player.HP = Math.Min(savedHP, player.MaxHP);
        player.Mana = Math.Min(savedMana, player.MaxMana);

        // Log successful restore
        DebugLogger.Instance.LogLoad(player.Name, player.Level, player.HP, player.MaxHP, player.Gold);
        DebugLogger.Instance.LogDebug("LOAD", $"Stats: STR={player.Strength} DEF={player.Defence} WeapPow={player.WeapPow} ArmPow={player.ArmPow}");

        // Register player's team so WorldSimulator protects it from NPC AI dissolution
        // This is critical in MUD mode where GameEngine.Instance is null on the world sim thread
        if (!string.IsNullOrEmpty(player.Team))
        {
            WorldSimulator.RegisterPlayerTeam(player.Team);

            // Cache team HQ upgrade levels for combat bonuses (online mode only)
            if (UsurperRemake.BBS.DoorMode.IsOnlineMode)
            {
                var hqBackend = SaveSystem.Instance.Backend as SqlSaveBackend;
                if (hqBackend != null)
                {
                    player.HQArmoryLevel = hqBackend.GetTeamUpgradeLevel(player.Team, "armory");
                    player.HQBarracksLevel = hqBackend.GetTeamUpgradeLevel(player.Team, "barracks");
                    player.HQTrainingLevel = hqBackend.GetTeamUpgradeLevel(player.Team, "training");
                    player.HQInfirmaryLevel = hqBackend.GetTeamUpgradeLevel(player.Team, "infirmary");
                }
            }
        }

        // Apply player's color theme preference
        ColorTheme.Current = player.ColorTheme;

        // Sync screen reader mode from player save to global
        GameConfig.ScreenReaderMode = player.ScreenReaderMode;

        // Sync to PlayerSession for group broadcast sanitization
        var ctx = UsurperRemake.Server.SessionContext.Current;
        if (ctx != null)
        {
            var session = UsurperRemake.Server.MudServer.Instance?.ActiveSessions
                .TryGetValue(ctx.Username.ToLowerInvariant(), out var s) == true ? s : null;
            if (session != null)
                session.ScreenReaderMode = player.ScreenReaderMode;
        }

        // Sync compact mode and language from player save to global
        GameConfig.CompactMode = player.CompactMode;
        GameConfig.DisableCharacterMonsterArt = player.DisableCharacterMonsterArt;
        // If the player actively chose a language on the main menu this session, keep it
        // and update their character to match. Otherwise restore from save.
        if (_languageSetThisSession.Value)
        {
            player.Language = GameConfig.Language;
        }
        else
        {
            GameConfig.Language = player.Language ?? "en";
            GameConfig.DateFormat = player.DateFormatPreference;
        }

        return player;
    }

    /// <summary>
    /// Restore dungeon floor states from save data
    /// </summary>
    private Dictionary<int, UsurperRemake.Systems.DungeonFloorState> RestoreDungeonFloorStates(
        Dictionary<int, DungeonFloorStateData> savedStates)
    {
        var result = new Dictionary<int, UsurperRemake.Systems.DungeonFloorState>();

        foreach (var kvp in savedStates)
        {
            var saved = kvp.Value;
            var state = new UsurperRemake.Systems.DungeonFloorState
            {
                FloorLevel = saved.FloorLevel,
                LastClearedAt = saved.LastClearedAt,
                LastVisitedAt = saved.LastVisitedAt,
                EverCleared = saved.EverCleared,
                IsPermanentlyClear = saved.IsPermanentlyClear,
                BossDefeated = saved.BossDefeated,
                CompletionBonusAwarded = saved.CompletionBonusAwarded,
                CurrentRoomId = saved.CurrentRoomId,
                RoomStates = new Dictionary<string, UsurperRemake.Systems.DungeonRoomState>()
            };

            foreach (var roomData in saved.Rooms)
            {
                state.RoomStates[roomData.RoomId] = new UsurperRemake.Systems.DungeonRoomState
                {
                    RoomId = roomData.RoomId,
                    IsExplored = roomData.IsExplored,
                    IsCleared = roomData.IsCleared,
                    TreasureLooted = roomData.TreasureLooted,
                    TrapTriggered = roomData.TrapTriggered,
                    EventCompleted = roomData.EventCompleted,
                    PuzzleSolved = roomData.PuzzleSolved,
                    RiddleAnswered = roomData.RiddleAnswered,
                    LoreCollected = roomData.LoreCollected,
                    InsightGranted = roomData.InsightGranted,
                    MemoryTriggered = roomData.MemoryTriggered,
                    SecretBossDefeated = roomData.SecretBossDefeated
                };
            }

            result[kvp.Key] = state;
        }

        return result;
    }

    /// <summary>
    /// Migrate old WeapPow/ArmPow to new equipment system for old saves
    /// </summary>
    private void MigrateOldEquipmentToNew(Character player, long weapPow, long armPow)
    {
        // Find best matching weapon for WeapPow
        if (weapPow > 0)
        {
            var weapons = EquipmentDatabase.GetWeaponsByHandedness(WeaponHandedness.OneHanded);
            var bestWeapon = weapons
                .Where(w => w.WeaponPower <= weapPow)
                .OrderByDescending(w => w.WeaponPower)
                .FirstOrDefault();

            if (bestWeapon != null)
            {
                player.EquippedItems[EquipmentSlot.MainHand] = bestWeapon.Id;
            }
        }

        // Find best matching armor for ArmPow
        if (armPow > 0)
        {
            var armors = EquipmentDatabase.GetBySlot(EquipmentSlot.Body);
            var bestArmor = armors
                .Where(a => a.ArmorClass <= armPow)
                .OrderByDescending(a => a.ArmorClass)
                .FirstOrDefault();

            if (bestArmor != null)
            {
                player.EquippedItems[EquipmentSlot.Body] = bestArmor.Id;
            }
        }

        // Initialize base stats
        player.InitializeBaseStats();
    }
    
    /// <summary>
    /// Restore world state from save data
    /// </summary>
    private async Task RestoreWorldState(WorldStateData worldState)
    {
        if (worldState == null)
        {
            // GD.Print("[GameEngine] No world state to restore");
            return;
        }

        // Restore economic state
        // This would integrate with bank and economy systems

        // Restore political state
        if (!string.IsNullOrEmpty(worldState.CurrentRuler))
        {
            // Set current ruler if applicable
        }

        // Restore active world events from save data
        var currentDay = dailyManager?.CurrentDay ?? 1;
        WorldEventSystem.Instance.RestoreFromSaveData(worldState.ActiveEvents, currentDay);

        // Restore unclaimed board quests from world state.
        // Player quests are already loaded via MergePlayerQuests in RestorePlayerFromSaveData,
        // so we merge world quests additively instead of using RestoreFromSaveData (which
        // calls questDatabase.Clear() and would wipe the player's quests).
        if (worldState.ActiveQuests != null && worldState.ActiveQuests.Count > 0)
        {
            if (UsurperRemake.BBS.DoorMode.IsOnlineMode)
            {
                // Online: world quests managed by world sim, skip
            }
            else
            {
                // Single-player: add unclaimed board quests without clearing player quests
                QuestSystem.MergeWorldQuests(worldState.ActiveQuests);
            }
        }

        // Restore marketplace listings from save data
        if (worldState.MarketplaceListings != null && worldState.MarketplaceListings.Count > 0)
        {
            UsurperRemake.Systems.MarketplaceSystem.Instance.LoadFromSaveData(worldState.MarketplaceListings);
            // GD.Print($"[GameEngine] Restored {worldState.MarketplaceListings.Count} marketplace listings");
        }

        // Restore NPC settlement state (single-player only — online mode loads from world_state)
        if (worldState.Settlement != null && !UsurperRemake.BBS.DoorMode.IsOnlineMode)
        {
            UsurperRemake.Systems.SettlementSystem.Instance.RestoreFromSaveData(worldState.Settlement);
        }

        // GD.Print($"[GameEngine] World state restored: {worldState.ActiveEvents?.Count ?? 0} active events, {worldState.ActiveQuests?.Count ?? 0} quests");
        await Task.CompletedTask;
    }
    
    /// <summary>
    /// Restore NPCs from save data
    /// </summary>
    /// <summary>
    /// Cap a legacy-migrated NPC age at 60% of the race's max lifespan to prevent instant aging deaths.
    /// </summary>
    private static int CapLegacyAge(CharacterRace race, int randomAge)
    {
        int maxLifespan = GameConfig.RaceLifespan.TryGetValue(race, out var lifespan) ? lifespan : 75;
        int cap = (int)(maxLifespan * 0.6);
        return Math.Min(randomAge, cap);
    }

    private async Task RestoreNPCs(List<NPCData> npcData)
    {
        if (npcData == null || npcData.Count == 0)
        {
            // Expected in online mode — player saves don't store NPCs (world sim manages them via world_state)
            DebugLogger.Instance.LogDebug("NPC", "No NPC data in player save (normal for online mode)");
            return;
        }

        // Clear existing NPCs before restoring
        NPCSpawnSystem.Instance.ClearAllNPCs();

        NPC? kingNpc = null;

        foreach (var data in npcData)
        {
            // v0.57.18 (third-round audit): defense-in-depth around per-NPC restore.
            // Pre-fix, a single bad NPC entry (corrupt enum, malformed dictionary,
            // unexpected null) would throw and abort the entire restore, leaving
            // NPCSpawnSystem partially populated and the world half-built. Same shape
            // of bug as the original FileSaveBackend silent-drop. Now: log + skip
            // bad entries, keep going. The world loses one NPC instead of all of them.
            try
            {
            // Create NPC from save data
            var npc = new NPC
            {
                Id = data.Id,
                ID = !string.IsNullOrEmpty(data.CharacterID) ? data.CharacterID : $"npc_{data.Name.ToLower().Replace(" ", "_")}",  // Restore Character.ID (or generate if missing)
                Name1 = data.Name,
                Name2 = data.Name,
                Level = data.Level,
                HP = data.HP,
                MaxHP = data.MaxHP,
                BaseMaxHP = data.BaseMaxHP > 0 ? data.BaseMaxHP : data.MaxHP,  // Fallback to MaxHP if not saved
                BaseMaxMana = data.BaseMaxMana > 0 ? data.BaseMaxMana : data.MaxMana,  // Fallback to MaxMana if not saved
                CurrentLocation = data.Location,

                // Stats
                Experience = data.Experience,
                Strength = data.Strength,
                Defence = data.Defence,
                Agility = data.Agility,
                Dexterity = data.Dexterity,
                Mana = data.Mana,
                MaxMana = data.MaxMana,
                WeapPow = data.WeapPow,
                ArmPow = data.ArmPow,

                // Base stats - CRITICAL for RecalculateStats to work correctly
                // Fallback to current stats if base stats not saved (legacy save compatibility)
                BaseStrength = data.BaseStrength > 0 ? data.BaseStrength : data.Strength,
                BaseDefence = data.BaseDefence > 0 ? data.BaseDefence : data.Defence,
                BaseDexterity = data.BaseDexterity > 0 ? data.BaseDexterity : data.Dexterity,
                BaseAgility = data.BaseAgility > 0 ? data.BaseAgility : data.Agility,
                BaseStamina = data.BaseStamina > 0 ? data.BaseStamina : 50,  // Default stamina
                BaseConstitution = data.BaseConstitution > 0 ? data.BaseConstitution : 10 + data.Level * 2,
                BaseIntelligence = data.BaseIntelligence > 0 ? data.BaseIntelligence : 10,
                BaseWisdom = data.BaseWisdom > 0 ? data.BaseWisdom : 10,
                BaseCharisma = data.BaseCharisma > 0 ? data.BaseCharisma : 10,

                // Class and race
                Class = data.Class,
                Race = data.Race,
                Sex = (CharacterSex)data.Sex,

                // Team and political status
                Team = data.Team,
                CTurf = data.IsTeamLeader,

                // Death status - permanent death tracking
                IsDead = data.IsDead,

                // Lifecycle - aging and natural death
                // Legacy age migration: cap at 60% of race's max lifespan to prevent instant aging deaths
                Age = data.Age > 0 ? data.Age : CapLegacyAge(data.Race, Random.Shared.Next(18, 50)),
                BirthDate = data.BirthDate > DateTime.MinValue
                    ? data.BirthDate
                    : DateTime.Now.AddHours(-(data.Age > 0 ? data.Age : CapLegacyAge(data.Race, Random.Shared.Next(18, 50))) * GameConfig.NpcLifecycleHoursPerYear),
                IsAgedDeath = data.IsAgedDeath,
                IsPermaDead = data.IsPermaDead,
                PregnancyDueDate = data.PregnancyDueDate,
                PregnancyFatherName = data.PregnancyFatherName,

                // Marriage status
                IsMarried = data.IsMarried,
                Married = data.Married,
                SpouseName = data.SpouseName ?? "",
                MarriedTimes = data.MarriedTimes,

                // Faction affiliation
                NPCFaction = data.NPCFaction >= 0 ? (UsurperRemake.Systems.Faction)data.NPCFaction : null,

                // Divine worship
                WorshippedGod = data.WorshippedGod ?? "",

                // Alignment — v0.57.12: same overflow heal applied to NPCs (WorldSimulator also had unbounded
                // direct-mutation sites that could push NPC alignment past the cap).
                Chivalry = AlignmentSystem.HealOverflowChivalry(data.Chivalry, data.Darkness),
                Darkness = AlignmentSystem.HealOverflowDarkness(data.Chivalry, data.Darkness),

                // Inventory
                Gold = data.Gold,
                BankGold = data.BankGold,
                AI = CharacterAI.Computer
            };

            // Restore items
            if (data.Items != null && data.Items.Length > 0)
            {
                npc.Item = data.Items.ToList();
            }

            // Restore market inventory for NPC trading
            if (data.MarketInventory != null && data.MarketInventory.Count > 0)
            {
                // Ensure MarketInventory is initialized
                if (npc.MarketInventory == null)
                {
                    npc.MarketInventory = new List<Item>();
                }
                foreach (var itemData in data.MarketInventory)
                {
                    var item = new global::Item
                    {
                        Name = itemData.ItemName,
                        Value = itemData.ItemValue,
                        Type = itemData.ItemType,
                        Attack = itemData.Attack,
                        Armor = itemData.Armor,
                        Strength = itemData.Strength,
                        Defence = itemData.Defence,
                        IsCursed = itemData.IsCursed
                    };
                    npc.MarketInventory.Add(item);
                }
            }

            // v0.57.4: restore the NPC's personal bag (transferred via combat [T] /
            // Home / Team Corner / dungeon viewer). Separate from MarketInventory
            // (shop stock). Pre-fix, NPC teammate bags evaporated every save.
            if (data.Inventory != null && data.Inventory.Count > 0)
            {
                // Pair with the save-side NPC_BAG log so the full round-trip is
                // visible. If items vanish between save and load, this is where
                // it'll show: either the restore doesn't fire (field missing) or
                // the count differs.
                var bagSummary = string.Join(", ", data.Inventory.Take(3).Select(i => i?.Name ?? "?"));
                if (data.Inventory.Count > 3) bagSummary += $", +{data.Inventory.Count - 3} more";
                DebugLogger.Instance.LogInfo("NPC_BAG",
                    $"RestoreNPCs RESTORED {data.Name} bag ({data.Inventory.Count}): {bagSummary}");

                npc.Inventory ??= new List<Item>();
                foreach (var itemData in data.Inventory)
                {
                    var item = new global::Item
                    {
                        Name = itemData.Name,
                        Value = itemData.Value,
                        Type = itemData.Type,
                        Attack = itemData.Attack,
                        Armor = itemData.Armor,
                        Strength = itemData.Strength,
                        Dexterity = itemData.Dexterity,
                        Wisdom = itemData.Wisdom,
                        Defence = itemData.Defence,
                        BlockChance = itemData.BlockChance,
                        ShieldBonus = itemData.ShieldBonus,
                        HP = itemData.HP,
                        Mana = itemData.Mana,
                        Charisma = itemData.Charisma,
                        Agility = itemData.Agility,
                        Stamina = itemData.Stamina,
                        MinLevel = itemData.MinLevel,
                        IsCursed = itemData.IsCursed,
                        Cursed = itemData.Cursed,
                        IsIdentified = itemData.IsIdentified,
                        Shop = itemData.Shop,
                        Dungeon = itemData.Dungeon,
                        Description = itemData.Description?.ToList() ?? new List<string>(),
                    };
                    if (itemData.LootEffects != null && itemData.LootEffects.Count > 0)
                        item.LootEffects = itemData.LootEffects.Select(e => (e.EffectType, e.Value)).ToList();
                    npc.Inventory.Add(item);
                }
            }

            // Restore personality profile if available, then initialize AI systems
            if (data.PersonalityProfile != null)
            {
                // Reconstruct PersonalityProfile from saved PersonalityData
                npc.Personality = new PersonalityProfile
                {
                    // Core traits
                    Aggression = data.PersonalityProfile.Aggression,
                    Loyalty = data.PersonalityProfile.Loyalty,
                    Intelligence = data.PersonalityProfile.Intelligence,
                    Greed = data.PersonalityProfile.Greed,
                    Sociability = data.PersonalityProfile.Compassion, // Compassion maps to Sociability
                    Courage = data.PersonalityProfile.Courage,
                    Trustworthiness = data.PersonalityProfile.Honesty, // Honesty maps to Trustworthiness
                    Ambition = data.PersonalityProfile.Ambition,
                    Vengefulness = data.PersonalityProfile.Vengefulness,
                    Impulsiveness = data.PersonalityProfile.Impulsiveness,
                    Caution = data.PersonalityProfile.Caution,
                    Mysticism = data.PersonalityProfile.Mysticism,
                    Patience = data.PersonalityProfile.Patience,
                    Archetype = data.Archetype ?? "Balanced",

                    // Romance/Intimacy traits
                    Gender = data.PersonalityProfile.Gender,
                    Orientation = data.PersonalityProfile.Orientation,
                    IntimateStyle = data.PersonalityProfile.IntimateStyle,
                    RelationshipPref = data.PersonalityProfile.RelationshipPref,
                    Romanticism = data.PersonalityProfile.Romanticism,
                    Sensuality = data.PersonalityProfile.Sensuality,
                    Jealousy = data.PersonalityProfile.Jealousy,
                    Commitment = data.PersonalityProfile.Commitment,
                    Adventurousness = data.PersonalityProfile.Adventurousness,
                    Exhibitionism = data.PersonalityProfile.Exhibitionism,
                    Voyeurism = data.PersonalityProfile.Voyeurism,
                    Flirtatiousness = data.PersonalityProfile.Flirtatiousness,
                    Passion = data.PersonalityProfile.Passion,
                    Tenderness = data.PersonalityProfile.Tenderness
                };
                npc.Archetype = data.Archetype ?? "citizen";
            }
            else
            {
                npc.Archetype = data.Archetype ?? "citizen";
            }

            // Initialize AI systems now that name and archetype are set
            // This will use the restored personality if available, or generate one if not
            npc.EnsureSystemsInitialized();

            // Restore NPC memories, goals, and emotional state from saved data
            // This must happen AFTER EnsureSystemsInitialized creates the Brain
            if (npc.Brain != null)
            {
                // Restore memories
                if (data.Memories != null && data.Memories.Count > 0)
                {
                    foreach (var memData in data.Memories)
                    {
                        if (Enum.TryParse<MemoryType>(memData.Type, out var memType))
                        {
                            var memory = new MemoryEvent
                            {
                                Type = memType,
                                Description = memData.Description,
                                InvolvedCharacter = memData.InvolvedCharacter,
                                Timestamp = memData.Timestamp,
                                Importance = memData.Importance,
                                EmotionalImpact = memData.EmotionalImpact
                            };
                            npc.Brain.Memory?.RecordEvent(memory);
                        }
                    }
                    // GD.Print($"[GameEngine] Restored {data.Memories.Count} memories for {npc.Name}");
                }

                // Restore goals
                if (data.CurrentGoals != null && data.CurrentGoals.Count > 0)
                {
                    foreach (var goalData in data.CurrentGoals)
                    {
                        if (Enum.TryParse<GoalType>(goalData.Type, out var goalType))
                        {
                            var goal = new Goal(goalData.Name, goalType, goalData.Priority)
                            {
                                Progress = goalData.Progress,
                                IsActive = goalData.IsActive,
                                TargetValue = goalData.TargetValue,
                                CurrentValue = goalData.CurrentValue,
                                CreatedTime = goalData.CreatedTime
                            };
                            npc.Brain.Goals?.AddGoal(goal);
                        }
                    }
                    // GD.Print($"[GameEngine] Restored {data.CurrentGoals.Count} goals for {npc.Name}");
                }

                // Restore emotional state
                if (data.EmotionalState != null)
                {
                    if (data.EmotionalState.Happiness > 0)
                        npc.Brain.Emotions?.AddEmotion(EmotionType.Joy, data.EmotionalState.Happiness, 120);
                    if (data.EmotionalState.Anger > 0)
                        npc.Brain.Emotions?.AddEmotion(EmotionType.Anger, data.EmotionalState.Anger, 120);
                    if (data.EmotionalState.Fear > 0)
                        npc.Brain.Emotions?.AddEmotion(EmotionType.Fear, data.EmotionalState.Fear, 120);
                    if (data.EmotionalState.Trust > 0)
                        npc.Brain.Emotions?.AddEmotion(EmotionType.Gratitude, data.EmotionalState.Trust, 120);
                    if (data.EmotionalState.Confidence > 0)
                        npc.Brain.Emotions?.AddEmotion(EmotionType.Confidence, data.EmotionalState.Confidence, 120);
                    if (data.EmotionalState.Sadness > 0)
                        npc.Brain.Emotions?.AddEmotion(EmotionType.Sadness, data.EmotionalState.Sadness, 120);
                    if (data.EmotionalState.Greed > 0)
                        npc.Brain.Emotions?.AddEmotion(EmotionType.Greed, data.EmotionalState.Greed, 120);
                    if (data.EmotionalState.Loneliness > 0)
                        npc.Brain.Emotions?.AddEmotion(EmotionType.Loneliness, data.EmotionalState.Loneliness, 120);
                    if (data.EmotionalState.Envy > 0)
                        npc.Brain.Emotions?.AddEmotion(EmotionType.Envy, data.EmotionalState.Envy, 120);
                    if (data.EmotionalState.Pride > 0)
                        npc.Brain.Emotions?.AddEmotion(EmotionType.Pride, data.EmotionalState.Pride, 120);
                    if (data.EmotionalState.Hope > 0)
                        npc.Brain.Emotions?.AddEmotion(EmotionType.Hope, data.EmotionalState.Hope, 120);
                    if (data.EmotionalState.Peace > 0)
                        npc.Brain.Emotions?.AddEmotion(EmotionType.Peace, data.EmotionalState.Peace, 120);
                }
            }

            // Restore enemy list
            if (data.Enemies != null && data.Enemies.Count > 0)
            {
                npc.Enemies = new List<string>(data.Enemies);
            }

            // Fix Experience if it's too low for the NPC's level — legacy saves may not have
            // tracked NPC XP, or GenerateNPCStats may have used template.StartLevel instead of actual Level
            long expectedMinXP = GetExperienceForNPCLevel(npc.Level);
            if (npc.Experience < expectedMinXP && npc.Level > 1)
            {
                npc.Experience = expectedMinXP;
            }

            // Initialize base stats if they're not set (legacy save compatibility)
            // This ensures RecalculateStats() works correctly after level-ups
            if (npc.BaseMaxHP <= 0)
            {
                npc.BaseMaxHP = npc.MaxHP;
                npc.BaseStrength = npc.Strength;
                npc.BaseDefence = npc.Defence;
                npc.BaseDexterity = npc.Dexterity;
                npc.BaseAgility = npc.Agility;
                npc.BaseStamina = npc.Stamina;
                npc.BaseConstitution = npc.Constitution;
                npc.BaseIntelligence = npc.Intelligence;
                npc.BaseWisdom = npc.Wisdom;
                npc.BaseCharisma = npc.Charisma;
                npc.BaseMaxMana = npc.MaxMana;
            }

            // Restore recently-used dialogue IDs to prevent repetition across sessions
            if (data.RecentDialogueIds != null && data.RecentDialogueIds.Count > 0)
            {
                npc.RecentDialogueIds = new List<string>(data.RecentDialogueIds);
                NPCDialogueDatabase.RestoreRecentlyUsedIds(npc.Name2 ?? npc.Name1 ?? "", data.RecentDialogueIds);
            }

            // Restore social emergence role
            npc.EmergentRole = data.EmergentRole ?? "";
            npc.RoleStabilityTicks = data.RoleStabilityTicks;

            // Restore skill proficiency
            if (data.SkillProficiencies?.Count > 0)
            {
                npc.SkillProficiencies = data.SkillProficiencies.ToDictionary(
                    kvp => kvp.Key, kvp => (TrainingSystem.ProficiencyLevel)kvp.Value);
            }
            if (data.SkillTrainingProgress?.Count > 0)
            {
                npc.SkillTrainingProgress = new Dictionary<string, int>(data.SkillTrainingProgress);
            }

            // Restore hostility, social graph, and gang affiliation
            npc.IsHostile = data.IsHostile;
            if (data.KnownCharacters?.Count > 0)
            {
                npc.KnownCharacters = new List<string>(data.KnownCharacters);
            }
            npc.GangId = data.GangId ?? "";
            npc.Specialization = data.Specialization;

            // Migrate: Assign faction to NPCs that don't have one (legacy save compatibility)
            if (!npc.NPCFaction.HasValue)
            {
                npc.NPCFaction = DetermineFactionForNPC(npc);
                if (npc.NPCFaction.HasValue)
                {
                }
            }

            // Restore dynamic equipment FIRST (before EquippedItems, so IDs are registered)
            if (data.DynamicEquipment != null && data.DynamicEquipment.Count > 0)
            {
                foreach (var equipData in data.DynamicEquipment)
                {
                    var equipment = new Equipment
                    {
                        Name = equipData.Name,
                        Description = equipData.Description ?? "",
                        Slot = (EquipmentSlot)equipData.Slot,
                        WeaponPower = equipData.WeaponPower,
                        ArmorClass = equipData.ArmorClass,
                        ShieldBonus = equipData.ShieldBonus,
                        BlockChance = equipData.BlockChance,
                        StrengthBonus = equipData.StrengthBonus,
                        DexterityBonus = equipData.DexterityBonus,
                        ConstitutionBonus = equipData.ConstitutionBonus,
                        IntelligenceBonus = equipData.IntelligenceBonus,
                        WisdomBonus = equipData.WisdomBonus,
                        CharismaBonus = equipData.CharismaBonus,
                        MaxHPBonus = equipData.MaxHPBonus,
                        MaxManaBonus = equipData.MaxManaBonus,
                        DefenceBonus = equipData.DefenceBonus,
                        MinLevel = equipData.MinLevel,
                        Value = equipData.Value,
                        IsCursed = equipData.IsCursed,
                        Rarity = (EquipmentRarity)equipData.Rarity,
                        WeaponType = (WeaponType)equipData.WeaponType,
                        Handedness = (WeaponHandedness)equipData.Handedness,
                        ArmorType = (ArmorType)equipData.ArmorType,
                        StaminaBonus = equipData.StaminaBonus,
                        AgilityBonus = equipData.AgilityBonus,
                        CriticalChanceBonus = equipData.CriticalChanceBonus,
                        CriticalDamageBonus = equipData.CriticalDamageBonus,
                        MagicResistance = equipData.MagicResistance,
                        PoisonDamage = equipData.PoisonDamage,
                        LifeSteal = equipData.LifeSteal,
                        HasFireEnchant = equipData.HasFireEnchant,
                        HasFrostEnchant = equipData.HasFrostEnchant,
                        HasLightningEnchant = equipData.HasLightningEnchant,
                        HasPoisonEnchant = equipData.HasPoisonEnchant,
                        HasHolyEnchant = equipData.HasHolyEnchant,
                        HasShadowEnchant = equipData.HasShadowEnchant,
                        ManaSteal = equipData.ManaSteal,
                        ArmorPiercing = equipData.ArmorPiercing,
                        Thorns = equipData.Thorns,
                        HPRegen = equipData.HPRegen,
                        ManaRegen = equipData.ManaRegen,
                        WeightClass = (ArmorWeightClass)equipData.WeightClass,
                        StrengthRequired = equipData.StrengthRequired,
                        RequiresGood = equipData.RequiresGood,
                        RequiresEvil = equipData.RequiresEvil,
                        ClassRestrictions = equipData.ClassRestrictions?.Select(c => (CharacterClass)c).ToList() ?? new List<CharacterClass>(),
                        IsUnique = equipData.IsUnique,
                        HasBossSlayer = equipData.HasBossSlayer,
                        HasTitanResolve = equipData.HasTitanResolve,
                        IsIdentified = equipData.IsIdentified
                    };

                    // Migration: re-infer weapon type from name (fixes multi-monster equip
                    // path that defaulted to Sword instead of inferring). v0.57.13: skip shields —
                    // InferWeaponType has no shield keywords and would rewrite them to Sword +
                    // OneHanded, letting IsDualWielding fire an off-hand strike with the shield.
                    // v0.60.0: name-based shield detection added as a third
                    // signal so pre-v0.53.13 mangled shields self-heal on load
                    // even when stat signals (ShieldBonus / BlockChance) were
                    // stripped by an earlier bad migration.
                    bool isShieldNpc = equipment.ShieldBonus > 0 || equipment.BlockChance > 0
                        || equipment.WeaponType == WeaponType.Shield
                        || equipment.WeaponType == WeaponType.Buckler
                        || equipment.WeaponType == WeaponType.TowerShield
                        || (equipment.Slot == EquipmentSlot.OffHand
                            && ShopItemGenerator.LooksLikeShieldByName(equipment.Name));
                    if ((equipment.Slot == EquipmentSlot.MainHand || equipment.Slot == EquipmentSlot.OffHand) && !isShieldNpc)
                    {
                        var inferred = ShopItemGenerator.InferWeaponType(equipment.Name);
                        if (equipment.WeaponType != inferred)
                        {
                            equipment.WeaponType = inferred;
                            equipment.Handedness = ShopItemGenerator.InferHandedness(inferred);
                        }
                    }
                    else if (isShieldNpc && equipment.Slot == EquipmentSlot.OffHand)
                    {
                        if (equipment.WeaponType != WeaponType.Shield
                            && equipment.WeaponType != WeaponType.Buckler
                            && equipment.WeaponType != WeaponType.TowerShield)
                        {
                            equipment.WeaponType = ShopItemGenerator.InferShieldType(equipment.Name);
                        }
                        equipment.Handedness = WeaponHandedness.OffHandOnly;
                    }

                    // Register and get the new ID (may differ from saved ID)
                    int newId = EquipmentDatabase.RegisterDynamic(equipment);

                    // Update the EquippedItems dictionary to use the new ID
                    if (data.EquippedItems != null)
                    {
                        foreach (var slot in data.EquippedItems.Keys.ToList())
                        {
                            if (data.EquippedItems[slot] == equipData.Id)
                            {
                                data.EquippedItems[slot] = newId;
                            }
                        }
                    }
                }
            }

            // Restore equipped items
            if (data.EquippedItems != null && data.EquippedItems.Count > 0)
            {
                foreach (var kvp in data.EquippedItems)
                {
                    var slot = (EquipmentSlot)kvp.Key;
                    var equipmentId = kvp.Value;
                    npc.EquippedItems[slot] = equipmentId;
                }
            }

            // CRITICAL: Validate and fix base stats before RecalculateStats
            // Old saves or corrupted NPCs may have 0 base stats, which causes
            // RecalculateStats to zero out all stats (STR: 0, DEF: -18 issues)
            ValidateAndFixNPCBaseStats(npc);

            // Now recalculate stats with valid base stats
            npc.RecalculateStats();

            // Sanity check: ensure NPC has valid HP (fix corrupted saves)
            long minHP = 20 + (npc.Level * 10);
            if (npc.MaxHP < minHP)
            {
                UsurperRemake.Systems.DebugLogger.Instance.LogWarning("NPC", $"Fixing corrupted NPC {npc.Name}: MaxHP={npc.MaxHP}, BaseMaxHP={npc.BaseMaxHP}, resetting to {minHP}");
                npc.BaseMaxHP = minHP;
                npc.MaxHP = minHP;
                if (npc.HP < 0 || npc.HP > npc.MaxHP)
                {
                    npc.HP = npc.IsDead ? 0 : npc.MaxHP;
                }
            }

            // Add to spawn system
            NPCSpawnSystem.Instance.AddRestoredNPC(npc);

            // Track who was king
            if (data.IsKing)
            {
                kingNpc = npc;
            }
            }
            catch (Exception ex)
            {
                DebugLogger.Instance.LogWarning("NPC",
                    $"RestoreNPCs: skipping NPC '{data?.Name ?? "?"}' due to restore error: {ex.Message}");
            }
        }

        // Restore the king if there was one
        if (kingNpc != null)
        {
            global::CastleLocation.SetCurrentKing(kingNpc);
        }

        // Mark NPCs as initialized so they don't get re-created
        NPCSpawnSystem.Instance.MarkAsInitialized();

        // Process dead NPCs for respawn - this queues them with a faster timer
        // since they've been dead since the last save
        var deadCount = npcData.Count(n => n.IsDead);
        UsurperRemake.Systems.DebugLogger.Instance.LogInfo("NPC", $"Restoring {npcData.Count} NPCs, {deadCount} are dead");

        if (worldSimulator != null)
        {
            worldSimulator.ProcessDeadNPCsOnLoad();
        }
        else
        {
            UsurperRemake.Systems.DebugLogger.Instance.LogWarning("NPC", "worldSimulator is null - cannot process dead NPCs!");
        }

        await Task.CompletedTask;
    }

    /// <summary>
    /// Validate and fix NPC base stats if they are invalid (0 or negative).
    /// This is critical for saves from older versions where base stats weren't saved,
    /// or for corrupted NPCs. Without valid base stats, RecalculateStats() will
    /// zero out all stats causing STR: 0, DEF: -18 type issues.
    /// </summary>
    private void ValidateAndFixNPCBaseStats(NPC npc)
    {
        bool needsFix = false;
        int level = npc.Level > 0 ? npc.Level : 1;

        // Check if base stats are invalid (0 or negative)
        if (npc.BaseStrength <= 0)
        {
            // Calculate reasonable base strength for level and class
            npc.BaseStrength = 10 + (level * 5);
            if (npc.Class == CharacterClass.Warrior || npc.Class == CharacterClass.Barbarian)
                npc.BaseStrength += level * 2;
            needsFix = true;
        }

        if (npc.BaseDefence <= 0)
        {
            npc.BaseDefence = 10 + (level * 3);
            needsFix = true;
        }

        if (npc.BaseAgility <= 0)
        {
            npc.BaseAgility = 10 + (level * 3);
            needsFix = true;
        }

        if (npc.BaseDexterity <= 0)
        {
            npc.BaseDexterity = 10 + (level * 2);
            if (npc.Class == CharacterClass.Assassin)
                npc.BaseDexterity += level * 3;
            needsFix = true;
        }

        if (npc.BaseStamina <= 0)
        {
            npc.BaseStamina = 10 + (level * 4);
            needsFix = true;
        }

        if (npc.BaseConstitution <= 0)
        {
            npc.BaseConstitution = 10 + (level * 2);
            needsFix = true;
        }

        if (npc.BaseIntelligence <= 0)
        {
            npc.BaseIntelligence = 10 + (level * 2);
            if (npc.Class == CharacterClass.Magician)
                npc.BaseIntelligence += level * 3;
            needsFix = true;
        }

        if (npc.BaseWisdom <= 0)
        {
            npc.BaseWisdom = 10 + (level * 2);
            if (npc.Class == CharacterClass.Cleric || npc.Class == CharacterClass.Paladin)
                npc.BaseWisdom += level * 2;
            needsFix = true;
        }

        if (npc.BaseCharisma <= 0)
        {
            npc.BaseCharisma = 10;
            needsFix = true;
        }

        if (npc.BaseMaxHP <= 0)
        {
            // Calculate based on class
            npc.BaseMaxHP = npc.Class switch
            {
                CharacterClass.Warrior or CharacterClass.Barbarian => 100 + (level * 50),
                CharacterClass.Magician => 50 + (level * 25),
                CharacterClass.Cleric or CharacterClass.Paladin => 80 + (level * 40),
                CharacterClass.Assassin => 70 + (level * 35),
                CharacterClass.Sage => 90 + (level * 45),
                _ => 80 + (level * 40)
            };
            needsFix = true;
        }

        if (npc.BaseMaxMana <= 0 && (npc.Class == CharacterClass.Magician ||
            npc.Class == CharacterClass.Cleric || npc.Class == CharacterClass.Paladin ||
            npc.Class == CharacterClass.Sage))
        {
            npc.BaseMaxMana = npc.Class switch
            {
                CharacterClass.Magician => 50 + (level * 30),
                CharacterClass.Cleric or CharacterClass.Paladin => 40 + (level * 20),
                CharacterClass.Sage => 30 + (level * 15),
                _ => 0
            };
            needsFix = true;
        }

        if (needsFix)
        {
            UsurperRemake.Systems.DebugLogger.Instance.LogWarning("NPC",
                $"Fixed corrupted base stats for {npc.Name} (Level {level} {npc.ClassName}): " +
                $"STR={npc.BaseStrength}, DEF={npc.BaseDefence}, AGI={npc.BaseAgility}");
        }
    }

    /// <summary>
    /// XP formula matching the player's curve (level^2.0 * 50)
    /// Used to initialize NPC XP when loading legacy saves
    /// </summary>
    private static long GetExperienceForNPCLevel(int level)
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
    /// Determine faction for an NPC based on their class and alignment (for legacy save migration)
    /// Uses same logic as NPCSpawnSystem.DetermineFactionForNPC but works with NPC object
    /// </summary>
    private static UsurperRemake.Systems.Faction? DetermineFactionForNPC(NPC npc)
    {
        var random = Random.Shared;

        // Clerics are strongly associated with The Faith
        if (npc.Class == CharacterClass.Cleric)
        {
            // Only evil clerics wouldn't be Faith
            if (npc.Darkness <= npc.Chivalry)
                return UsurperRemake.Systems.Faction.TheFaith;
        }

        // Assassins are associated with The Shadows
        if (npc.Class == CharacterClass.Assassin)
        {
            if (random.Next(100) < 80) // 80% chance
                return UsurperRemake.Systems.Faction.TheShadows;
        }

        // Warriors and Paladins may be Crown members
        if (npc.Class == CharacterClass.Warrior || npc.Class == CharacterClass.Paladin)
        {
            // Good-aligned warriors often serve the Crown
            if (npc.Chivalry > npc.Darkness && random.Next(100) < 60)
                return UsurperRemake.Systems.Faction.TheCrown;
        }

        // Evil-aligned characters may be Shadows
        if (npc.Darkness > npc.Chivalry + 200)
        {
            if (random.Next(100) < 50) // 50% chance
                return UsurperRemake.Systems.Faction.TheShadows;
        }

        // Most NPCs remain unaffiliated
        return null;
    }

    /// <summary>
    /// Save current game state
    /// </summary>
    public async Task SaveCurrentGame()
    {
        if (currentPlayer == null) return;

        // In online mode, use the session's character key (handles alt characters correctly)
        var playerName = UsurperRemake.BBS.DoorMode.IsOnlineMode
            ? (UsurperRemake.BBS.DoorMode.GetPlayerName()?.ToLowerInvariant() ?? currentPlayer.Name2 ?? currentPlayer.Name1)
            : (currentPlayer.Name2 ?? currentPlayer.Name1);
        terminal.WriteLine(Loc.Get("save.saving"), "yellow");
        
        var success = await SaveSystem.Instance.SaveGame(playerName, currentPlayer);
        
        if (success)
        {
            terminal.WriteLine(Loc.Get("save.saved"), "green");
        }
        else
        {
            terminal.WriteLine(Loc.Get("engine.save_failed"), "red");
        }

        await Task.Delay(1000);
    }

    /// <summary>
    /// Create new player using comprehensive character creation system
    /// Based on Pascal USERHUNC.PAS implementation
    /// </summary>
    private async Task<Character> CreateNewPlayer(string playerName)
    {
        try
        {
            // Use the CharacterCreationSystem for full Pascal-compatible creation
            var creationSystem = new CharacterCreationSystem(terminal);
            var newCharacter = await creationSystem.CreateNewCharacter(playerName);
            
            if (newCharacter == null)
            {
                // Character creation was aborted
                terminal.WriteLine("");
                terminal.WriteLine(Loc.Get("engine.creation_cancelled"), "yellow");
                terminal.WriteLine(Loc.Get("engine.must_create"), "white");

                var retry = await terminal.GetInputAsync(Loc.Get("engine.retry_prompt"));
                if (retry.ToUpper() == "Y")
                {
                    return await CreateNewPlayer(playerName); // Retry
                }
                
                return null; // User chose not to retry
            }
            
            // Character creation successful - message already displayed by CharacterCreationSystem
            return newCharacter;
        }
        catch (OperationCanceledException)
        {
            terminal.WriteLine(Loc.Get("engine.creation_aborted"), "red");
            return null;
        }
        catch (Exception ex)
        {
            terminal.WriteLine(Loc.Get("engine.creation_error", ex.Message), "red");
            UsurperRemake.Systems.DebugLogger.Instance.LogError("CRASH", $"Character creation error:\n{ex}");

            terminal.WriteLine(Loc.Get("engine.please_try_again"), "yellow");
            var retry = await terminal.GetInputAsync(Loc.Get("engine.retry_prompt"));
            if (retry.ToUpper() == "Y")
            {
                return await CreateNewPlayer(playerName); // Retry
            }
            
            return null;
        }
    }
    
    /// <summary>
    /// Get current location description for compatibility
    /// </summary>
    public string GetCurrentLocationDescription()
    {
        return locationManager?.GetCurrentLocationDescription() ?? "Unknown location";
    }
    
    /// <summary>
    /// Update status line with player info
    /// </summary>
    private void UpdateStatusLine()
    {
        var statusText = $"[{currentPlayer.DisplayName}] " +
                        $"{Loc.Get("ui.level")}: {currentPlayer.Level} " +
                        $"{Loc.Get("combat.bar_hp")}: {currentPlayer.HP}/{currentPlayer.MaxHP} " +
                        $"{Loc.Get("ui.gold")}: {currentPlayer.Gold} " +
                        Loc.Get("engine.turns_label", currentPlayer.TurnsLeft);
        
        terminal.SetStatusLine(statusText);
    }
    
    /// <summary>
    /// Get NPCs in a specific location (for location system compatibility)
    /// </summary>
    public List<NPC> GetNPCsInLocation(GameLocation locationId)
    {
        return locationManager?.GetNPCsInLocation(locationId) ?? new List<NPC>();
    }
    
    /// <summary>
    /// Add NPC to a specific location
    /// </summary>
    public void AddNPCToLocation(GameLocation locationId, NPC npc)
    {
        locationManager?.AddNPCToLocation(locationId, npc);
    }
    
    /// <summary>
    /// Get current player for location system
    /// </summary>
    public Character GetCurrentPlayer()
    {
        return currentPlayer;
    }
    
    /// <summary>
    /// Check daily limits - based on CHECK_ALLOWED from Pascal
    /// </summary>
    private async Task<bool> CheckDailyLimits()
    {
        // Check if it's a new day
        if (dailyManager.IsNewDay())
        {
            await dailyManager.CheckDailyReset();
        }
        
        return currentPlayer.TurnsRemaining > 0;
    }
    
    /// <summary>
    /// Handle player death - based on death handling from Pascal
    /// Player respawns at the Inn with penalties instead of being deleted
    /// </summary>
    private async Task HandleDeath()
    {
        terminal.ClearScreen();
        terminal.ShowANSIArt("DEATH");
        terminal.SetColor("bright_red");
        if (GameConfig.ScreenReaderMode)
        {
            terminal.WriteLine(Loc.Get("engine.you_have_died"));
        }
        else
        {
            terminal.WriteLine("═══════════════════════════════════════════════════════════════");
            terminal.WriteLine($"                        {Loc.Get("engine.you_have_died")}                          ");
            terminal.WriteLine("═══════════════════════════════════════════════════════════════");
        }
        terminal.WriteLine("");

        terminal.SetColor("gray");
        terminal.WriteLine(Loc.Get("engine.death_vision"));
        terminal.WriteLine("");
        await Task.Delay(2000);

        // Check if player has resurrections (from items/temple)
        if (currentPlayer.Resurrections > 0)
        {
            terminal.SetColor("yellow");
            terminal.WriteLine(Loc.Get("engine.resurrections_available", currentPlayer.Resurrections));
            terminal.WriteLine("");
            var resurrect = await terminal.GetInput(Loc.Get("engine.use_resurrection_prompt"));

            if (resurrect.ToUpper().StartsWith("Y"))
            {
                currentPlayer.Resurrections--;
                currentPlayer.Statistics.RecordResurrection();
                currentPlayer.HP = currentPlayer.MaxHP;
                terminal.SetColor("bright_green");
                terminal.WriteLine("");
                terminal.WriteLine(Loc.Get("engine.divine_light"));
                terminal.WriteLine(Loc.Get("engine.resurrected_no_penalty"));
                await Task.Delay(2500);

                // Return to the Inn
                currentPlayer.Location = (int)GameLocation.TheInn;
                await SaveSystem.Instance.AutoSave(currentPlayer);
                return;
            }
        }

        // Apply death penalties
        terminal.SetColor("red");
        terminal.WriteLine(Loc.Get("engine.death_penalties"));
        if (!GameConfig.ScreenReaderMode)
            terminal.WriteLine("─────────────────────────");

        // Calculate penalties
        long expLoss = currentPlayer.Experience / 10;  // Lose 10% experience
        long goldLoss = currentPlayer.Gold / 4;        // Lose 25% gold on hand

        // Apply penalties
        currentPlayer.Experience = Math.Max(0, currentPlayer.Experience - expLoss);
        currentPlayer.Gold = Math.Max(0, currentPlayer.Gold - goldLoss);

        // Track death count
        currentPlayer.MDefeats++;

        terminal.SetColor("yellow");
        if (expLoss > 0)
            terminal.WriteLine(Loc.Get("engine.lost_exp", expLoss.ToString("N0")));
        if (goldLoss > 0)
            terminal.WriteLine(Loc.Get("engine.lost_gold", goldLoss.ToString("N0")));
        terminal.WriteLine(Loc.Get("engine.monster_defeats", currentPlayer.MDefeats));
        terminal.WriteLine("");

        // Resurrect player at the Inn with half HP
        currentPlayer.HP = Math.Max(1, currentPlayer.MaxHP / 2);
        currentPlayer.Location = (int)GameLocation.TheInn;

        // Clear any negative status effects
        currentPlayer.Poison = 0;
        currentPlayer.PoisonTurns = 0;

        terminal.SetColor("cyan");
        terminal.WriteLine(Loc.Get("engine.wake_at_inn"));
        terminal.WriteLine(Loc.Get("engine.wounds_healed", currentPlayer.HP, currentPlayer.MaxHP));
        terminal.WriteLine("");

        // Online news: player death
        if (UsurperRemake.Systems.OnlineStateManager.IsActive)
        {
            var deathName = currentPlayer.Name2 ?? currentPlayer.Name1;
            _ = UsurperRemake.Systems.OnlineStateManager.Instance!.AddNews(
                $"{deathName} has fallen in battle and was carried back to the Inn.", "combat");
        }

        terminal.SetColor("gray");
        terminal.WriteLine(Loc.Get("engine.innkeeper_quote"));
        terminal.WriteLine("");

        await terminal.PressAnyKey();

        // Save the resurrected character
        await SaveSystem.Instance.AutoSave(currentPlayer);

        // Continue playing - don't mark as deleted!
        terminal.SetColor("green");
        terminal.WriteLine(Loc.Get("engine.adventure_continues"));
        await Task.Delay(1500);
    }
    
    /// <summary>
    /// Handle prison - based on prison handling from Pascal
    /// </summary>
    /// <summary>
    /// Run world sim catch-up after loading a single-player save.
    /// Fast-forwards the world simulation based on how long the player was away,
    /// up to CatchUpMaxDays (7 days). Shows a progress bar and narrative summary.
    /// </summary>
    private async Task RunWorldSimCatchUp(DateTime saveTime)
    {
        // Only for single-player — online mode has 24/7 world sim
        if (UsurperRemake.BBS.DoorMode.IsOnlineMode) return;
        if (worldSimulator == null) return;

        // Guard against old saves that don't have SaveTime (defaults to DateTime.MinValue)
        // Without this, upgrading to v0.51.0 would trigger a full 7-day catch-up for every player
        if (saveTime == default || saveTime.Year < 2020) return;

        var absence = DateTime.Now - saveTime;
        if (absence.TotalMinutes < GameConfig.CatchUpMinAbsenceMinutes) return;

        // Cap at max days
        double absenceDays = Math.Min(absence.TotalDays, GameConfig.CatchUpMaxDays);
        int totalTicks = (int)(absenceDays * GameConfig.CatchUpTicksPerDay);
        totalTicks = Math.Min(totalTicks, GameConfig.CatchUpMaxTicks);

        if (totalTicks <= 0) return;

        // Show catch-up header
        terminal.WriteLine("", "white");
        if (!GameConfig.ScreenReaderMode)
            terminal.WriteLine("═══════════════════════════════════════", "bright_cyan");

        string timeDesc;
        if (absenceDays >= 1.0)
            timeDesc = $"{absenceDays:F1} days";
        else if (absence.TotalHours >= 1.0)
            timeDesc = $"{absence.TotalHours:F1} hours";
        else
            timeDesc = $"{(int)absence.TotalMinutes} minutes";

        terminal.WriteLine($"  While you were away ({timeDesc})...", "bright_yellow");
        terminal.WriteLine($"  The world continued without you.", "cyan");
        if (!GameConfig.ScreenReaderMode)
            terminal.WriteLine("═══════════════════════════════════════", "bright_cyan");
        terminal.WriteLine("", "white");

        // Set up catch-up buffer to collect news events
        // Cap at 10k entries to prevent unbounded memory growth
        const int maxBufferSize = 10000;
        var catchUpEvents = new List<string>();
        NewsSystem.Instance.SetCatchUpBuffer(catchUpEvents, maxBufferSize);

        // Stop background sim and wait for it to fully stop.
        // StopSimulation() sets isRunning = false; the background loop checks this
        // between SimulateStep() calls. We wait up to 5 seconds for the loop to exit.
        worldSimulator.StopSimulation();
        for (int wait = 0; wait < 50; wait++)
        {
            await Task.Delay(100);
            // Background loop exits when isRunning is false between iterations
            // 5 seconds is more than enough for one 30-second SimulateStep cycle
        }

        try
        {
            worldSimulator.IsCatchUpMode = true;
            worldSimulator.SetActive(true); // Mark as running so SimulateStep() works

            int ticksPerDay = GameConfig.CatchUpTicksPerDay;
            int progressInterval = GameConfig.CatchUpProgressInterval;
            bool isBBS = UsurperRemake.BBS.DoorMode.IsInDoorMode;

            for (int tick = 0; tick <= totalTicks; tick++)
            {
                try
                {
                    worldSimulator.SimulateStep();
                }
                catch (Exception ex)
                {
                    DebugLogger.Instance.LogError("CATCHUP", $"Sim step {tick} error: {ex.Message}");
                }

                // Daily reset every CatchUpTicksPerDay ticks
                if (tick > 0 && tick % ticksPerDay == 0)
                {
                    dailyManager.RunCatchUpDailyReset();
                }

                // Progress bar update
                if (tick % progressInterval == 0 || tick == totalTicks)
                {
                    int pct = (int)((tick + 1) * 100L / (totalTicks + 1));
                    if (GameConfig.ScreenReaderMode || isBBS)
                    {
                        // BBS and screen reader: use WriteLine (no \r support)
                        if (tick % (progressInterval * 5) == 0 || tick == totalTicks)
                            terminal.WriteLine($"  Simulating... {pct}%", "gray");
                    }
                    else
                    {
                        int barWidth = 30;
                        int filled = pct * barWidth / 100;
                        string bar = new string('█', filled) + new string('░', barWidth - filled);
                        terminal.Write($"\r  [{bar}] {pct}% ", "bright_cyan");
                    }
                }
            }

            // Finish progress bar
            if (!GameConfig.ScreenReaderMode && !isBBS)
                terminal.WriteLine("", "white");
        }
        finally
        {
            // Always clean up catch-up state, even on exception
            worldSimulator.IsCatchUpMode = false;
            worldSimulator.SetActive(false);
            NewsSystem.Instance.ClearCatchUpBuffer();
        }

        // Show narrative summary
        await ShowCatchUpSummary(catchUpEvents);

        // Restart normal background simulation
        worldSimulator.StartSimulation();
    }

    /// <summary>
    /// Display a categorized narrative summary of what happened during catch-up.
    /// </summary>
    private async Task ShowCatchUpSummary(List<string> events)
    {
        if (events.Count == 0)
        {
            terminal.WriteLine("  The realm was quiet in your absence.", "gray");
            terminal.WriteLine("", "white");
            await terminal.PressAnyKey();
            return;
        }

        // Categorize events using case-insensitive matching
        var deaths = new List<string>();
        var births = new List<string>();
        var political = new List<string>();
        var social = new List<string>();
        var settlement = new List<string>();
        var worldEvents = new List<string>();

        foreach (var evt in events)
        {
            // Strip timestamp prefix if present (e.g., "[14:32] ")
            string clean = evt;
            if (clean.StartsWith("[") && clean.Length > 7 && clean[6] == ']')
                clean = clean.Substring(8).Trim();

            // Skip internal markers (day separators, etc.)
            if (clean.Contains("═══") || clean.IndexOf("New Day", StringComparison.OrdinalIgnoreCase) >= 0 || string.IsNullOrWhiteSpace(clean))
                continue;

            if (clean.IndexOf("slain", StringComparison.OrdinalIgnoreCase) >= 0 ||
                clean.IndexOf("passed away", StringComparison.OrdinalIgnoreCase) >= 0 ||
                clean.IndexOf("soul moves on", StringComparison.OrdinalIgnoreCase) >= 0)
                deaths.Add(clean);
            else if (clean.IndexOf("proud parents", StringComparison.OrdinalIgnoreCase) >= 0 ||
                     clean.IndexOf("come of age", StringComparison.OrdinalIgnoreCase) >= 0 ||
                     clean.IndexOf("born", StringComparison.OrdinalIgnoreCase) >= 0)
                births.Add(clean);
            else if (clean.IndexOf("king", StringComparison.OrdinalIgnoreCase) >= 0 ||
                     clean.IndexOf("proclaims", StringComparison.OrdinalIgnoreCase) >= 0 ||
                     clean.IndexOf("throne", StringComparison.OrdinalIgnoreCase) >= 0 ||
                     clean.IndexOf("treasury", StringComparison.OrdinalIgnoreCase) >= 0)
                political.Add(clean);
            else if (clean.IndexOf("married", StringComparison.OrdinalIgnoreCase) >= 0 ||
                     clean.IndexOf("divorced", StringComparison.OrdinalIgnoreCase) >= 0 ||
                     clean.IndexOf("affair", StringComparison.OrdinalIgnoreCase) >= 0 ||
                     clean.IndexOf("birthday", StringComparison.OrdinalIgnoreCase) >= 0)
                social.Add(clean);
            else if (clean.IndexOf("settlement", StringComparison.OrdinalIgnoreCase) >= 0 ||
                     clean.IndexOf("outskirts", StringComparison.OrdinalIgnoreCase) >= 0 ||
                     clean.IndexOf("constructed", StringComparison.OrdinalIgnoreCase) >= 0 ||
                     clean.IndexOf("building", StringComparison.OrdinalIgnoreCase) >= 0)
                settlement.Add(clean);
            else if (clean.IndexOf("level", StringComparison.OrdinalIgnoreCase) >= 0 ||
                     clean.IndexOf("quest", StringComparison.OrdinalIgnoreCase) >= 0 ||
                     clean.IndexOf("team", StringComparison.OrdinalIgnoreCase) >= 0)
                worldEvents.Add(clean);
            else
                worldEvents.Add(clean); // Default bucket
        }

        int maxPerCat = GameConfig.CatchUpMaxEventsPerCategory;

        terminal.WriteLine("", "white");
        if (!GameConfig.ScreenReaderMode)
            terminal.WriteLine($"─── {Loc.Get("engine.news_from_realm")} ───", "bright_yellow");
        else
            terminal.WriteLine($"--- {Loc.Get("engine.news_from_realm")} ---", "bright_yellow");
        terminal.WriteLine("", "white");

        ShowCatchUpCategory(Loc.Get("engine.cat_deaths"), deaths, maxPerCat, "red");
        ShowCatchUpCategory(Loc.Get("engine.cat_births"), births, maxPerCat, "bright_green");
        ShowCatchUpCategory(Loc.Get("engine.cat_royal"), political, maxPerCat, "bright_yellow");
        ShowCatchUpCategory(Loc.Get("engine.cat_love"), social, maxPerCat, "bright_magenta");
        ShowCatchUpCategory(Loc.Get("engine.cat_outskirts"), settlement, maxPerCat, "cyan");
        ShowCatchUpCategory(Loc.Get("engine.cat_world_events"), worldEvents, maxPerCat, "white");

        terminal.WriteLine($"  {Loc.Get("engine.total_events", events.Count)}", "gray");
        terminal.WriteLine("", "white");

        await terminal.PressAnyKey();
    }

    /// <summary>
    /// Display a single category of catch-up events.
    /// </summary>
    private void ShowCatchUpCategory(string title, List<string> events, int max, string color)
    {
        if (events.Count == 0) return;

        terminal.WriteLine($"  {title}:", color);

        // Show the last N events (most recent)
        var toShow = events.Count > max ? events.Skip(events.Count - max).ToList() : events;
        foreach (var evt in toShow)
        {
            // Strip emoji/symbol prefixes for cleaner display
            string display = evt;
            while (display.Length > 0 && !char.IsLetterOrDigit(display[0]) && display[0] != '[')
                display = display.Substring(1);
            if (string.IsNullOrWhiteSpace(display)) display = evt;
            terminal.WriteLine($"    - {display}", "gray");
        }

        if (events.Count > max)
            terminal.WriteLine($"    ...and {events.Count - max} more", "dark_gray");

        terminal.WriteLine("", "white");
    }

    /// <summary>
    /// Quit game and save
    /// </summary>
    private async Task QuitGame()
    {
        terminal.WriteLine(Loc.Get("save.saving"), "yellow");

        if (currentPlayer != null)
        {
            // Log game exit
            DebugLogger.Instance.LogGameExit(currentPlayer.Name, "QuitGame");
        }

        // Ensure save completes before exiting
        if (currentPlayer != null)
        {
            try
            {
                string playerName = currentPlayer.Name2 ?? currentPlayer.Name1;
                var success = await SaveSystem.Instance.SaveGame(playerName, currentPlayer);
                if (success)
                {
                    terminal.WriteLine(Loc.Get("save.saved"), "bright_green");
                }
                else
                {
                    terminal.WriteLine(Loc.Get("engine.save_not_completed"), "yellow");
                }
            }
            catch (Exception ex)
            {
                terminal.WriteLine(Loc.Get("engine.save_error", ex.Message), "red");
            }
        }

        // Stop background simulation threads
        worldSimulator?.StopSimulation();

        // Clean up online presence BEFORE exit so session doesn't stay stale
        if (OnlineStateManager.IsActive)
        {
            try
            {
                OnlineStateManager.Instance!.Shutdown().GetAwaiter().GetResult();
            }
            catch (Exception ex) { DebugLogger.Instance.LogError("ENGINE", $"[QuitGame] OnlineStateManager shutdown failed: {ex.Message}"); }
        }
        if (OnlineChatSystem.IsActive)
        {
            try { OnlineChatSystem.Instance!.Shutdown(); } catch (Exception ex) { DebugLogger.Instance.LogError("ENGINE", $"[QuitGame] OnlineChatSystem shutdown failed: {ex.Message}"); }
        }

        terminal.WriteLine(Loc.Get("engine.goodbye"), "green");
        await Task.Delay(1000);

        // Mark as intentional exit so bootstrap doesn't show warning
        IsIntentionalExit = true;

        Environment.Exit(0);
    }
    
    // Helper methods
    private string GetTimeOfDay()
    {
        var hour = DateTime.Now.Hour;
        return hour switch
        {
            >= 6 and < 12 => "morning",
            >= 12 and < 18 => "afternoon",
            >= 18 and < 22 => "evening",
            _ => "night"
        };
    }
    
    private string GetWeather()
    {
        var weather = new[] { "clear", "cloudy", "misty", "cool", "warm", "breezy" };
        return weather[Random.Shared.Next(0, weather.Length)];
    }
    
    /// <summary>
    /// Navigate to a specific location using the location manager
    /// </summary>
    public async Task<bool> NavigateToLocation(GameLocation destination)
    {
        return await locationManager.NavigateTo(destination, currentPlayer);
    }
    
    // Placeholder methods for game actions
    private async Task ShowInstructions() => await ShowStoryIntroduction();
    private async Task ListPlayers() => await ShowInfoScreen("Player List", "Player list will be here...");
    private async Task ShowTeams() => await ShowInfoScreen("Teams", "Team information will be here...");
    private async Task ShowGameSettings() => await ShowInfoScreen("Game Settings", "Game settings will be here...");
    private async Task ShowStatus() => await ShowInfoScreen("Status", $"Player: {currentPlayer?.DisplayName}\nLevel: {currentPlayer?.Level}\nHP: {currentPlayer?.HP}/{currentPlayer?.MaxHP}");

#if !STEAM_BUILD
    private async Task ShowSupportPage()
    {
        terminal.ClearScreen();
        if (GameConfig.ScreenReaderMode)
        {
            terminal.SetColor("bright_white");
            terminal.WriteLine(Loc.Get("engine.support_title"));
        }
        else
        {
            terminal.SetColor("bright_yellow");
            terminal.WriteLine("╔══════════════════════════════════════════════════════════════════════════════╗");
            terminal.Write("║");
            terminal.SetColor("bright_white");
            { string t = Loc.Get("engine.support_title"); int l = (78 - t.Length) / 2; int r = 78 - t.Length - l; terminal.Write(new string(' ', l) + t + new string(' ', r)); }
            terminal.SetColor("bright_yellow");
            terminal.WriteLine("║");
            terminal.WriteLine("╚══════════════════════════════════════════════════════════════════════════════╝");
        }
        terminal.WriteLine("");

        terminal.SetColor("white");
        terminal.WriteLine(Loc.Get("engine.support_desc_1"));
        terminal.WriteLine(Loc.Get("engine.support_desc_2"));
        terminal.WriteLine(Loc.Get("engine.support_desc_3"));
        terminal.WriteLine("");

        terminal.SetColor("bright_cyan");
        terminal.WriteLine(GameConfig.ScreenReaderMode ? Loc.Get("engine.support_how_sr") : Loc.Get("engine.support_how_visual"));
        terminal.WriteLine("");

        terminal.SetColor("bright_yellow");
        terminal.Write(Loc.Get("engine.support_sponsor"));
        terminal.SetColor("white");
        terminal.WriteLine(Loc.Get("engine.support_sponsor_desc"));
        terminal.WriteLine(Loc.Get("engine.support_sponsor_desc2"));
        terminal.SetColor("bright_green");
        terminal.WriteLine("  https://github.com/sponsors/binary-knight");
        terminal.WriteLine("");

        terminal.SetColor("bright_yellow");
        terminal.Write(Loc.Get("engine.support_star"));
        terminal.SetColor("white");
        terminal.WriteLine(Loc.Get("engine.support_star_desc"));
        terminal.WriteLine(Loc.Get("engine.support_star_desc2"));
        terminal.WriteLine(Loc.Get("engine.support_star_desc3"));
        terminal.SetColor("bright_green");
        terminal.WriteLine("  https://github.com/binary-knight/usurper-reborn");
        terminal.WriteLine("");

        terminal.SetColor("bright_yellow");
        terminal.Write(Loc.Get("engine.support_steam"));
        terminal.SetColor("white");
        terminal.WriteLine(Loc.Get("engine.support_steam_desc"));
        terminal.WriteLine(Loc.Get("engine.support_steam_desc2"));
        terminal.WriteLine("");

        if (!GameConfig.ScreenReaderMode)
        {
            terminal.SetColor("bright_cyan");
            terminal.WriteLine("  ─────────────────────────");
            terminal.WriteLine("");
        }

        terminal.SetColor("gray");
        terminal.WriteLine(Loc.Get("engine.support_thanks_1"));
        terminal.WriteLine(Loc.Get("engine.support_thanks_2"));
        terminal.WriteLine("");

        terminal.SetColor("white");
        await terminal.PressAnyKey();
    }
#endif

    /// <summary>
    /// Display the credits screen
    /// </summary>
    private async Task ShowBBSList()
    {
        terminal.ClearScreen();

        if (GameConfig.ScreenReaderMode)
        {
            terminal.WriteLine(Loc.Get("engine.bbs_list_title"), "bright_white");
        }
        else
        {
            terminal.SetColor("bright_cyan");
            terminal.WriteLine("+=============================================================================+");
            terminal.SetColor("bright_white");
            { string t = Loc.Get("engine.bbs_list_title"); int l = (78 - t.Length) / 2; int r = 78 - t.Length - l; terminal.WriteLine("|" + new string(' ', l) + t + new string(' ', r) + "|"); }
            terminal.SetColor("bright_cyan");
            terminal.WriteLine("+=============================================================================+");
        }
        terminal.WriteLine("");

        terminal.SetColor("white");
        terminal.WriteLine(Loc.Get("engine.bbs_list_desc_1"));
        terminal.WriteLine(Loc.Get("engine.bbs_list_desc_2"));
        terminal.WriteLine("");

        // Column headers
        terminal.SetColor("bright_yellow");
        terminal.WriteLine(Loc.Get("engine.bbs_list_header"));
        if (!GameConfig.ScreenReaderMode)
        {
            terminal.SetColor("darkgray");
            terminal.WriteLine("  ---------------------------------------------------------------------------");
        }

        // --- BBS Entries ---

        terminal.SetColor("bright_white");
        terminal.Write("  Shurato's Heavenly Sphere      ");
        terminal.SetColor("gray");
        terminal.Write("EleBBS      ");
        terminal.SetColor("bright_green");
        terminal.WriteLine("shsbbs.net");

        terminal.SetColor("bright_white");
        terminal.Write("  The X-BIT BBS                  ");
        terminal.SetColor("gray");
        terminal.Write("Synchronet  ");
        terminal.SetColor("bright_green");
        terminal.WriteLine("x-bit.org:23 / ssh -p 22222");

        terminal.SetColor("bright_white");
        terminal.Write("  The UNIX-BIT BBS               ");
        terminal.SetColor("gray");
        terminal.Write("Synchronet  ");
        terminal.SetColor("bright_green");
        terminal.WriteLine("x-bit.org:1336 / ssh -p 1337");

        terminal.SetColor("bright_white");
        terminal.Write("  Lunatics Unleashed             ");
        terminal.SetColor("gray");
        terminal.Write("Mystic      ");
        terminal.SetColor("bright_green");
        terminal.WriteLine("lunaticsunleashed.ddns.net:2333");

        terminal.SetColor("bright_white");
        terminal.Write("  A-Net Online                   ");
        terminal.SetColor("gray");
        terminal.Write("Synchronet  ");
        terminal.SetColor("bright_green");
        terminal.WriteLine("bbs.a-net.online:1337 / ssh -p 1338");

        terminal.SetColor("bright_white");
        terminal.Write("  Nite Eyes BBS                  ");
        terminal.SetColor("gray");
        terminal.Write("Mystic      ");
        terminal.SetColor("bright_green");
        terminal.WriteLine("bbs.lizardmaster.com");

        // --- End BBS Entries ---

        terminal.WriteLine("");
        if (!GameConfig.ScreenReaderMode)
        {
            terminal.SetColor("darkgray");
            terminal.WriteLine("  ---------------------------------------------------------------------------");
        }
        terminal.WriteLine("");

        terminal.SetColor("bright_magenta");
        terminal.WriteLine(Loc.Get("engine.bbs_sysop_question"));
        terminal.SetColor("gray");
        terminal.WriteLine(Loc.Get("engine.bbs_get_listed"));
        terminal.WriteLine("");
        terminal.SetColor("bright_cyan");
        terminal.WriteLine("    https://github.com/binary-knight/usurper-reborn");
        terminal.WriteLine("");
        terminal.SetColor("gray");
        terminal.WriteLine(Loc.Get("engine.bbs_include_info"));
        terminal.WriteLine("");

        terminal.SetColor("yellow");
        terminal.WriteLine($"                         {Loc.Get("engine.press_enter_return")}");
        await terminal.WaitForKey();
    }

    /// <summary>Helper to display a menu key option in the character selection screen.</summary>
    private void WriteMenuKey(string key, string label)
    {
        if (GameConfig.ScreenReaderMode)
        {
            terminal.WriteLine($"  {key}. {label}", "bright_white");
            return;
        }
        terminal.SetColor("darkgray");
        terminal.Write("  [");
        terminal.SetColor("bright_yellow");
        terminal.Write(key);
        terminal.SetColor("darkgray");
        terminal.Write("] ");
        terminal.SetColor("bright_white");
        terminal.WriteLine(label);
    }

    /// <summary>Switch session identity to an alt character key for save/load routing.</summary>
    /// <param name="altKey">The alt character DB key (e.g. "rage__alt")</param>
    /// <param name="displayName">Optional display name for online presence (from save info). Falls back to altKey.</param>
    private async Task SwitchToAltCharacter(string altKey, string? displayName = null)
    {
        UsurperRemake.BBS.DoorMode.SetOnlineUsername(altKey);
        var ctx = UsurperRemake.Server.SessionContext.Current;
        if (ctx != null) ctx.CharacterKey = altKey;

        // Switch online presence tracking to the new identity
        var osm = OnlineStateManager.Instance;
        var onlineName = !string.IsNullOrEmpty(displayName) ? displayName : altKey;
        if (osm != null)
        {
            var connType = ctx?.ConnectionType ?? "Unknown";
            // Use the character's display name (not the raw __alt key) for online presence
            await osm.SwitchIdentity(altKey, onlineName, connType);
        }

        // Update the PlayerSession's active character name so RoomRegistry
        // shows the alt's name (not the account name) in "Also here" and broadcasts
        var server = UsurperRemake.Server.MudServer.Instance;
        if (server != null && server.ActiveSessions.TryGetValue(ctx?.Username?.ToLowerInvariant() ?? "", out var session))
        {
            session.ActiveCharacterName = onlineName;
        }
    }

    private async Task ShowCredits()
    {
        terminal.ClearScreen();

        if (GameConfig.ScreenReaderMode)
        {
            terminal.WriteLine(Loc.Get("engine.credits_title"), "bright_yellow");
        }
        else
        {
            terminal.SetColor("bright_yellow");
            terminal.WriteLine("+=============================================================================+");
            { string t = Loc.Get("engine.credits_title"); int l = (78 - t.Length) / 2; int r = 78 - t.Length - l; terminal.WriteLine("|" + new string(' ', l) + t + new string(' ', r) + "|"); }
            terminal.WriteLine("+=============================================================================+");
        }
        terminal.WriteLine("");

        terminal.SetColor("bright_cyan");
        terminal.WriteLine($"                         {Loc.Get("engine.credits_subtitle")}");
        terminal.WriteLine($"                    {Loc.Get("engine.credits_tagline")}");
        terminal.WriteLine("");

        terminal.SetColor("bright_white");
        terminal.WriteLine(Loc.Get("engine.credits_original_game"));
        terminal.SetColor("white");
        terminal.WriteLine(Loc.Get("engine.credits_original_name"));
        terminal.WriteLine("");

        terminal.SetColor("bright_yellow");
        terminal.WriteLine(Loc.Get("engine.credits_original_creators"));
        terminal.SetColor("cyan");
        terminal.WriteLine(Loc.Get("engine.credits_jakob"));
        terminal.WriteLine(Loc.Get("engine.credits_rick"));
        terminal.WriteLine(Loc.Get("engine.credits_daniel"));
        terminal.WriteLine("");

        terminal.SetColor("bright_green");
        terminal.WriteLine(Loc.Get("engine.credits_reborn"));
        terminal.SetColor("cyan");
        terminal.WriteLine(Loc.Get("engine.credits_jason"));
        terminal.WriteLine("");
        terminal.SetColor("gray");
        terminal.WriteLine(Loc.Get("engine.credits_built_with"));
        terminal.WriteLine("");

        terminal.SetColor("bright_red");
        terminal.WriteLine(Loc.Get("engine.credits_ansi_art"));
        terminal.SetColor("cyan");
        terminal.WriteLine(Loc.Get("engine.credits_xbit"));
        terminal.WriteLine(Loc.Get("engine.credits_sudden_death"));
        terminal.WriteLine(Loc.Get("engine.credits_cozmo"));
        terminal.WriteLine("");

        terminal.SetColor("bright_white");
        terminal.WriteLine(Loc.Get("engine.credits_contributors"));
        terminal.SetColor("cyan");
        terminal.WriteLine(Loc.Get("engine.credits_fastfinge"));
        terminal.WriteLine(Loc.Get("engine.credits_maxsond"));
        terminal.WriteLine(Loc.Get("engine.credits_djlunacy"));
        terminal.WriteLine(Loc.Get("engine.credits_xykier"));
        terminal.WriteLine(Loc.Get("engine.credits_lowleveljavacoder"));
        terminal.WriteLine("");

        terminal.SetColor("bright_magenta");
        terminal.WriteLine(Loc.Get("engine.credits_special_thanks"));
        terminal.SetColor("gray");
        terminal.WriteLine(Loc.Get("engine.credits_bbs_community"));
        terminal.WriteLine(Loc.Get("engine.credits_sysops"));
        terminal.WriteLine(Loc.Get("engine.credits_players"));
        terminal.WriteLine(Loc.Get("engine.credits_wiki"));
        terminal.WriteLine("");

        terminal.SetColor("bright_white");
        terminal.WriteLine(Loc.Get("engine.credits_license"));
        terminal.SetColor("gray");
        terminal.WriteLine(Loc.Get("engine.credits_license_1"));
        terminal.WriteLine(Loc.Get("engine.credits_license_2"));
        terminal.WriteLine("");

        terminal.SetColor("bright_cyan");
        terminal.WriteLine(Loc.Get("engine.credits_community"));
        terminal.SetColor("gray");
        terminal.WriteLine(Loc.Get("engine.credits_discord"));
        terminal.WriteLine("");

        terminal.SetColor("yellow");
        terminal.WriteLine("");
        terminal.WriteLine($"                         {Loc.Get("engine.press_enter_return")}");
        await terminal.WaitForKey();
    }

    /// <summary>
    /// Display the story introduction and lore
    /// </summary>
    private async Task ShowStoryIntroduction()
    {
        terminal.ClearScreen();

        // Page 1: The Golden Age
        if (GameConfig.ScreenReaderMode)
        {
            terminal.WriteLine(Loc.Get("engine.story_title"), "bright_yellow");
        }
        else
        {
            terminal.SetColor("bright_yellow");
            terminal.WriteLine("╔══════════════════════════════════════════════════════════════════════════════╗");
            { string t = Loc.Get("engine.story_title"); int l = (78 - t.Length) / 2; int r = 78 - t.Length - l; terminal.WriteLine("║" + new string(' ', l) + t + new string(' ', r) + "║"); }
            terminal.WriteLine("╚══════════════════════════════════════════════════════════════════════════════╝");
        }
        terminal.WriteLine("");

        terminal.SetColor("bright_cyan");
        terminal.WriteLine($"                           {Loc.Get("engine.story_golden_age")}");
        terminal.WriteLine("");
        terminal.SetColor("white");
        terminal.WriteLine(Loc.Get("engine.story_golden_1"));
        terminal.WriteLine(Loc.Get("engine.story_golden_2"));
        terminal.WriteLine(Loc.Get("engine.story_golden_3"));
        terminal.WriteLine(Loc.Get("engine.story_golden_4"));
        terminal.WriteLine(Loc.Get("engine.story_golden_5"));
        terminal.WriteLine("");
        terminal.SetColor("gray");
        terminal.WriteLine(Loc.Get("engine.story_golden_6"));
        terminal.WriteLine("");

        terminal.SetColor("yellow");
        terminal.WriteLine($"                              {Loc.Get("engine.press_enter")}");
        await terminal.WaitForKey();

        // Page 2: The Sundering
        terminal.ClearScreen();
        if (GameConfig.ScreenReaderMode)
        {
            terminal.SetColor("bright_yellow");
            terminal.WriteLine(Loc.Get("engine.story_title"));
        }
        else
        {
            terminal.SetColor("bright_yellow");
            terminal.WriteLine("╔══════════════════════════════════════════════════════════════════════════════╗");
            { string t = Loc.Get("engine.story_title"); int l = (78 - t.Length) / 2; int r = 78 - t.Length - l; terminal.WriteLine("║" + new string(' ', l) + t + new string(' ', r) + "║"); }
            terminal.WriteLine("╚══════════════════════════════════════════════════════════════════════════════╝");
        }
        terminal.WriteLine("");

        terminal.SetColor("bright_red");
        terminal.WriteLine($"                            {Loc.Get("engine.story_sundering")}");
        terminal.WriteLine("");
        terminal.SetColor("white");
        terminal.WriteLine(Loc.Get("engine.story_sundering_1"));
        terminal.WriteLine(Loc.Get("engine.story_sundering_2"));
        terminal.WriteLine(Loc.Get("engine.story_sundering_3"));
        terminal.WriteLine(Loc.Get("engine.story_sundering_4"));
        terminal.WriteLine("");
        terminal.SetColor("red");
        terminal.WriteLine(Loc.Get("engine.story_sundering_5"));
        terminal.WriteLine("");
        terminal.SetColor("white");
        terminal.WriteLine(Loc.Get("engine.story_sundering_6"));
        terminal.WriteLine(Loc.Get("engine.story_sundering_7"));
        terminal.WriteLine(Loc.Get("engine.story_sundering_8"));
        terminal.WriteLine("");

        terminal.SetColor("yellow");
        terminal.WriteLine($"                              {Loc.Get("engine.press_enter")}");
        await terminal.WaitForKey();

        // Page 3: The Age of Avarice
        terminal.ClearScreen();
        if (GameConfig.ScreenReaderMode)
        {
            terminal.SetColor("bright_yellow");
            terminal.WriteLine(Loc.Get("engine.story_title"));
        }
        else
        {
            terminal.SetColor("bright_yellow");
            terminal.WriteLine("╔══════════════════════════════════════════════════════════════════════════════╗");
            { string t = Loc.Get("engine.story_title"); int l = (78 - t.Length) / 2; int r = 78 - t.Length - l; terminal.WriteLine("║" + new string(' ', l) + t + new string(' ', r) + "║"); }
            terminal.WriteLine("╚══════════════════════════════════════════════════════════════════════════════╝");
        }
        terminal.WriteLine("");

        terminal.SetColor("bright_magenta");
        terminal.WriteLine($"                          {Loc.Get("engine.story_avarice")}");
        terminal.WriteLine("");
        terminal.SetColor("white");
        terminal.WriteLine(Loc.Get("engine.story_avarice_1"));
        terminal.WriteLine(Loc.Get("engine.story_avarice_2"));
        terminal.WriteLine(Loc.Get("engine.story_avarice_3"));
        terminal.WriteLine("");
        terminal.WriteLine(Loc.Get("engine.story_avarice_4"));
        terminal.WriteLine("");
        terminal.SetColor("bright_yellow");
        terminal.WriteLine(Loc.Get("engine.story_avarice_5"));
        terminal.WriteLine(Loc.Get("engine.story_avarice_6"));
        terminal.WriteLine(Loc.Get("engine.story_avarice_7"));
        terminal.WriteLine("");
        terminal.SetColor("gray");
        terminal.WriteLine(Loc.Get("engine.story_avarice_8"));
        terminal.WriteLine(Loc.Get("engine.story_avarice_9"));
        terminal.WriteLine(Loc.Get("engine.story_avarice_10"));
        terminal.WriteLine("");

        terminal.SetColor("yellow");
        terminal.WriteLine($"                              {Loc.Get("engine.press_enter")}");
        await terminal.WaitForKey();

        // Page 4: Your Story Begins
        terminal.ClearScreen();
        if (GameConfig.ScreenReaderMode)
        {
            terminal.SetColor("bright_yellow");
            terminal.WriteLine(Loc.Get("engine.story_title"));
        }
        else
        {
            terminal.SetColor("bright_yellow");
            terminal.WriteLine("╔══════════════════════════════════════════════════════════════════════════════╗");
            { string t = Loc.Get("engine.story_title"); int l = (78 - t.Length) / 2; int r = 78 - t.Length - l; terminal.WriteLine("║" + new string(' ', l) + t + new string(' ', r) + "║"); }
            terminal.WriteLine("╚══════════════════════════════════════════════════════════════════════════════╝");
        }
        terminal.WriteLine("");

        terminal.SetColor("bright_green");
        terminal.WriteLine($"                          {Loc.Get("engine.story_begins")}");
        terminal.WriteLine("");
        terminal.SetColor("white");
        terminal.WriteLine(Loc.Get("engine.story_begins_1"));
        terminal.WriteLine(Loc.Get("engine.story_begins_2"));
        terminal.WriteLine(Loc.Get("engine.story_begins_3"));
        terminal.WriteLine(Loc.Get("engine.story_begins_4"));
        terminal.WriteLine("");
        terminal.SetColor("cyan");
        terminal.WriteLine(Loc.Get("engine.story_begins_5"));
        terminal.WriteLine(Loc.Get("engine.story_begins_6"));
        terminal.WriteLine(Loc.Get("engine.story_begins_7"));
        terminal.WriteLine("");
        terminal.SetColor("bright_magenta");
        terminal.WriteLine(Loc.Get("engine.story_begins_8"));
        terminal.WriteLine(Loc.Get("engine.story_begins_9"));
        terminal.WriteLine(Loc.Get("engine.story_begins_10"));
        terminal.WriteLine("");
        terminal.SetColor("bright_white");
        terminal.WriteLine(Loc.Get("engine.story_begins_11"));
        terminal.WriteLine("");

        terminal.SetColor("yellow");
        terminal.WriteLine($"                         {Loc.Get("engine.press_enter_return")}");
        await terminal.WaitForKey();
    }
    
    private async Task ShowInfoScreen(string title, string content)
    {
        terminal.ClearScreen();
        terminal.SetColor("bright_cyan");
        terminal.WriteLine(title);
        if (!GameConfig.ScreenReaderMode)
        {
            terminal.SetColor("cyan");
            terminal.WriteLine(new string('═', title.Length));
        }
        terminal.WriteLine("");
        terminal.SetColor("white");
        terminal.WriteLine(content);
        terminal.WriteLine("");
        terminal.WriteLine(Loc.Get("ui.press_enter"));
        await terminal.WaitForKey();
    }

    // Placeholder initialization methods
    private void ReadStartCfgValues() { /* Load config from file */ }
    private void InitializeItems()
    {
        // Initialize items using ItemManager
        ItemManager.InitializeItems();
    }
    private void InitializeMonsters() { /* Load monsters from data */ }
    private void InitializeNPCs()
    {
        try
        {
            if (worldNPCs == null)
                worldNPCs = new List<NPC>();

            var dataPath = Path.Combine(DataPath, "npcs.json");
            if (!File.Exists(dataPath))
            {
                DebugLogger.Instance?.LogDebug("INIT", $"NPC data file not found at {dataPath}. Using hard-coded specials only.");
                return;
            }

            var json = File.ReadAllText(dataPath);
            using var doc = JsonDocument.Parse(json);

            // Flatten all category arrays (tavern_npcs, guard_npcs, random_npcs, etc.)
            var root = doc.RootElement;
            foreach (var prop in root.EnumerateObject())
            {
                if (prop.Value.ValueKind != JsonValueKind.Array) continue;

                foreach (var npcElem in prop.Value.EnumerateArray())
                {
                    try
                    {
                        var name = npcElem.GetProperty("name").GetString() ?? "Unknown";
                        var archetype = npcElem.GetProperty("archetype").GetString() ?? "citizen";
                        var classStr = npcElem.GetProperty("class").GetString() ?? "warrior";
                        var level = npcElem.GetProperty("level").GetInt32();

                        if (!Enum.TryParse<CharacterClass>(classStr, true, out var charClass))
                        {
                            charClass = CharacterClass.Warrior;
                        }

                        var npc = new NPC(name, archetype, charClass, level);

                        // Gold override if provided
                        if (npcElem.TryGetProperty("gold", out var goldProp) && goldProp.TryGetInt64(out long gold))
                        {
                            npc.Gold = gold;
                        }

                        // Starting location mapping
                        string startLoc = npcElem.GetProperty("startingLocation").GetString() ?? "main_street";
                        var locId = MapStringToLocation(startLoc);
                        npc.UpdateLocation(startLoc); // keep textual for AI compatibility

                        worldNPCs.Add(npc);

                        // Add to LocationManager so they show up to the player
                        LocationManager.Instance.AddNPCToLocation(locId, npc);
                    }
                    catch (Exception exNpc)
                    {
                    }
                }
            }

        }
        catch (Exception ex)
            {
                DebugLogger.Instance.Log(DebugLogger.LogLevel.Debug, "ENGINE", $"Swallowed exception: {ex.Message}");
            }
    }
    
    /// <summary>
    /// Map simple string location names from JSON to GameLocation enum.
    /// </summary>
    private static GameLocation MapStringToLocation(string loc)
    {
        return loc.ToLower() switch
        {
            "tavern" or "inn" => GameLocation.TheInn,
            "market" or "marketplace" or "auction house" or "auctionhouse" => GameLocation.AuctionHouse,
            "town_square" or "main_street" => GameLocation.MainStreet,
            "castle" => GameLocation.Castle,
            "temple" or "church" => GameLocation.Temple,
            "dungeon" or "dungeons" => GameLocation.Dungeons,
            "bank" => GameLocation.Bank,
            "dark_alley" or "alley" => GameLocation.DarkAlley,
            _ => GameLocation.MainStreet
        };
    }
    
    private void InitializeLevels() { /* Load level data */ }
    private void InitializeGuards() { /* Load guard data */ }
    
    // Character creation helpers (now handled by CharacterCreationSystem)
    // These methods are kept for backwards compatibility but are no longer used
    private Task<CharacterSex> SelectSex() => Task.FromResult(CharacterSex.Male);
    private Task<CharacterRace> SelectRace() => Task.FromResult(CharacterRace.Human);
    private Task<CharacterClass> SelectClass() => Task.FromResult(CharacterClass.Warrior);
    private void ApplyRacialBonuses(Character character) { }
    private void ApplyClassBonuses(Character character) { }
    private void SetInitialEquipment(Character character) { }
    private Task ShowCharacterSummary(Character character) => Task.CompletedTask;

}

/// <summary>
/// Menu option for terminal menus
/// </summary>
public class MenuOption
{
    public string Key { get; set; } = "";
    public string Text { get; set; } = "";
    public Func<Task> Action { get; set; } = () => Task.CompletedTask;
}

/// <summary>
/// Game state tracking
/// </summary>
public class GameState
{
    public int GameDate { get; set; }
    public int LastDayRun { get; set; }
    public int PlayersOnline { get; set; }
    public bool MaintenanceRunning { get; set; }
}

/// <summary>
/// Online player tracking
/// </summary>
public class OnlinePlayer
{
    public string Name { get; set; } = "";
    public string Node { get; set; } = "";
    public DateTime Arrived { get; set; }
    public string Location { get; set; } = "";
    public bool IsNPC { get; set; }
}

/// <summary>
/// Config record based on Pascal ConfigRecord
/// </summary>
public class ConfigRecord
{
    public bool MarkNPCs { get; set; } = true;
    public int LevelDiff { get; set; } = 5;
    public bool FastPlay { get; set; } = false;
    public string Anchor { get; set; } = "Anchor road";
    public bool SimulNode { get; set; } = false;
    public bool AutoMaint { get; set; } = true;
    // Add more config fields as needed
}

/// <summary>
/// King record based on Pascal KingRec
/// </summary>
public class KingRecord
{
    public string Name { get; set; } = "";
    public CharacterAI AI { get; set; } = CharacterAI.Computer;
    public CharacterSex Sex { get; set; } = CharacterSex.Male;
    public long DaysInPower { get; set; } = 0;
    public byte Tax { get; set; } = 10;
    public long Treasury { get; set; } = 50000;
    // Add more king fields as needed
} 
