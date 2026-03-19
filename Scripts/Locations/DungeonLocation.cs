using UsurperRemake.Utils;
using UsurperRemake.Systems;
using UsurperRemake.Locations;
using UsurperRemake.Data;
using UsurperRemake.BBS;
using UsurperRemake.Server;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Channels;
using System.Threading.Tasks;

/// <summary>
/// Dungeon Location - Room-based exploration with atmosphere and tension
/// Features: Procedural floors, room navigation, feature interaction, combat, events
/// </summary>
public class DungeonLocation : BaseLocation
{
    internal List<Character> teammates = new();
    private int currentDungeonLevel = 1;
    private int maxDungeonLevel = 100;
    private Random dungeonRandom = new Random();
    private DungeonTerrain currentTerrain = DungeonTerrain.Underground;

    // Legacy encounter chances (for old ExploreLevel fallback)
    private const float MonsterEncounterChance = 0.90f;
    private const float SpecialEventChance = 0.10f;

    // Current floor state
    private DungeonFloor currentFloor = null!;
    private bool inRoomMode = false; // Are we exploring a room?

    // Player state tracking for tension
    private int consecutiveMonsterRooms = 0;
    private int roomsExploredThisFloor = 0;
    private bool hasCampedThisFloor = false;

    // Dungeon handles poison ticks on room movement, not on every menu input.
    // This prevents invalid keys and guide navigation from double-ticking poison.
    protected override bool SuppressBasePoisonTick => true;

    /// <summary>
    /// Invalidate the cached floor so it regenerates with current language on next entry.
    /// </summary>
    public void InvalidateFloorCache()
    {
        currentFloor = null!;
    }

    // One-time tutorial flag stored in player.HintsShown
    private const string DUNGEON_TUTORIAL_FLAG = "dungeon_tutorial_v1";

    public DungeonLocation() : base(
        GameLocation.Dungeons,
        "The Dungeons",
        "You stand before the entrance to the ancient dungeons. Dark passages lead deep into the earth."
    ) { }

    protected override void SetupLocation()
    {
        base.SetupLocation();
        // Note: currentDungeonLevel is initialized in EnterLocation() when we have the actual player
    }

    public override async Task EnterLocation(Character player, TerminalEmulator term)
    {
        // Set base class fields so WriteSectionHeader/WriteDivider etc. work
        currentPlayer = player;
        terminal = term;

        // GROUP FOLLOWER CHECK: If this player is in a group but not the leader,
        // and the leader is already in the dungeon, enter as a follower instead.
        var group = GroupSystem.Instance?.GetGroupFor(player?.Name2 ?? "");
        if (group == null)
        {
            // Also check by username from session context
            var ctx = SessionContext.Current;
            if (ctx != null)
                group = GroupSystem.Instance?.GetGroupFor(ctx.Username);
        }
        if (group != null && !group.IsLeader(player?.Name2 ?? "") && !group.IsLeader(SessionContext.Current?.Username ?? ""))
        {
            if (group.IsInDungeon)
            {
                await EnterAsGroupFollower(player!, term, group);
                // After leaving the group dungeon, redirect to Main Street
                // (bare return would unwind the entire call stack and disconnect)
                throw new LocationExitException(GameLocation.MainStreet);
            }
            else
            {
                term.SetColor("yellow");
                term.WriteLine(Loc.Get("dungeon.group_leader_not_entered"));
                term.WriteLine(Loc.Get("dungeon.wait_for_leader"));
                await term.PressAnyKey();
                throw new LocationExitException(GameLocation.MainStreet);
            }
        }

        // CRITICAL: Clear teammates list at the start to prevent leaking from other saves
        // The list will be rebuilt by AddCompanionsToParty() and RestoreNPCTeammates()
        teammates.Clear();

        // Initialize dungeon level based on the actual player entering
        var playerLevel = player?.Level ?? 1;
        currentDungeonLevel = Math.Max(1, playerLevel);

        if (currentDungeonLevel > maxDungeonLevel)
            currentDungeonLevel = maxDungeonLevel;

        // Check if player is trying to skip an uncleared boss/seal floor
        // They can't enter floors beyond an uncleared special floor
        currentDungeonLevel = GetMaxAccessibleFloor(player!, currentDungeonLevel);
        player!.CurrentLocation = $"Dungeon Floor {currentDungeonLevel}";

        // Generate or restore floor based on persistence state
        // CRITICAL: Also regenerate if we have a cached floor but no saved state for it
        // This handles the case where DungeonFloorStates was reset on game load but
        // currentFloor instance still exists from the previous session
        bool hasNoSavedState = !player.DungeonFloorStates.ContainsKey(currentDungeonLevel);
        bool isNewFloor = currentFloor == null || currentFloor.Level != currentDungeonLevel || hasNoSavedState;
        if (isNewFloor)
        {
            // Check if we have saved state for this floor
            var floorResult = GenerateOrRestoreFloor(player, currentDungeonLevel);
            currentFloor = floorResult.Floor;
            bool wasRestored = floorResult.WasRestored;
            bool didRespawn = floorResult.DidRespawn;

            roomsExploredThisFloor = wasRestored ? currentFloor.Rooms.Count(r => r.IsExplored) : 0;
            hasCampedThisFloor = false;

            // Track dungeon exploration statistics
            player.Statistics.RecordDungeonLevel(currentDungeonLevel);

            // Track archetype - Explorer for dungeon exploration
            UsurperRemake.Systems.ArchetypeTracker.Instance.RecordDungeonExploration(currentDungeonLevel);

            // Show dramatic dungeon entrance art
            term.ClearScreen();
            if (GameConfig.ScreenReaderMode)
            {
                term.WriteLine("");
                term.SetColor("bright_red");
                term.WriteLine(Loc.Get("dungeon.sr_title"));
                term.SetColor("red");
                term.WriteLine(Loc.Get("dungeon.sr_subtitle"));
                term.WriteLine("");
            }
            else
            {
                await UsurperRemake.UI.ANSIArt.DisplayArtAnimated(term, UsurperRemake.UI.ANSIArt.DungeonEntrance, 40);
                term.WriteLine("");
            }
            term.SetColor("cyan");
            term.WriteLine($"  {Loc.Get("dungeon.floor", currentDungeonLevel)} - {GetThemeShortName(currentFloor.Theme)}");
            term.SetColor("gray");
            term.WriteLine($"  {GetThemeDescription(currentFloor.Theme)}");

            // Show persistence status
            if (wasRestored && !didRespawn)
            {
                term.SetColor("bright_green");
                term.WriteLine(Loc.Get("dungeon.continuing"));
            }
            else if (didRespawn)
            {
                term.SetColor("yellow");
                term.WriteLine(Loc.Get("dungeon.respawned"));
                BroadcastDungeonRespawn();
            }

            // Remind player of active god save quests
            var saveQuestStory = StoryProgressionSystem.Instance;
            if (saveQuestStory != null)
            {
                foreach (var god in saveQuestStory.OldGodStates)
                {
                    if (god.Value.Status != GodStatus.Awakened) continue;
                    string godName = god.Key.ToString();
                    // Check the correct artifact for each god's save quest
                    var requiredArtifact = god.Key switch
                    {
                        OldGodType.Veloura => ArtifactType.SoulweaversLoom,
                        OldGodType.Aurelion => ArtifactType.SunforgedBlade,
                        _ => (ArtifactType?)null
                    };
                    bool hasArtifact = requiredArtifact != null && ArtifactSystem.Instance.HasArtifact(requiredArtifact.Value);
                    term.SetColor("bright_magenta");
                    if (hasArtifact)
                        term.WriteLine(Loc.Get("dungeon.god_awaits_return", godName));
                    else
                        term.WriteLine(Loc.Get("dungeon.god_awaits_rescue", godName));
                }
            }

            term.WriteLine("");
            term.SetColor("darkgray");
            term.Write(Loc.Get("dungeon.press_enter_continue"));
            await term.ReadKeyAsync();
        }

        // Refresh bounty board quests based on player level
        QuestSystem.RefreshBountyBoard(player?.Level ?? 1, player?.Statistics?.DeepestDungeonLevel ?? 0);

        // Update quest progress for reaching this floor (dungeon entry)
        QuestSystem.OnDungeonFloorReached(player!, currentDungeonLevel);

        // Check for story events at milestone floors
        await CheckFloorStoryEvents(player!, term);

        // Add active companions to the teammates list
        await AddCompanionsToParty(player, term);

        // Restore NPC teammates (spouses, team members, lovers) from saved state
        await RestoreNPCTeammates(term);

        // Restore player echoes ONLY if not in a real player group
        // (real group members join live via EnterAsGroupFollower, not as AI echoes)
        var myGroup = GroupSystem.Instance?.GetGroupFor(SessionContext.Current?.Username ?? "");
        if (myGroup == null)
        {
            await RestorePlayerTeammates(term);
        }

        // Add royal mercenaries (hired bodyguards for king players)
        AddRoyalMercenariesToParty(player, term);

        // Defensive deduplication after all team restoration methods
        DeduplicateTeammates();

        // Check for dungeon entry fees for overleveled teammates
        // Player can always enter - unaffordable allies simply stay behind
        await CheckAndPayEntryFees(player, term);

        // Setup group dungeon mode: notify followers, mark group in-dungeon
        await SetupGroupDungeon(player, term);

        // Show contextual hint for new players on their first dungeon entry
        HintSystem.Instance.TryShowHint(HintSystem.HINT_FIRST_DUNGEON, term, player.HintsShown);

        // Show guided tutorial for first-time dungeon visitors on floor 1
        if (currentDungeonLevel == 1 && !player.HintsShown.Contains(DUNGEON_TUTORIAL_FLAG))
        {
            await RunDungeonTutorial(player, term);
        }

        // Captain Aldric's Mission — dungeon entry objective
        if (player.HintsShown.Contains("aldric_quest_active") && !player.HintsShown.Contains("quest_scout_enter_dungeon"))
        {
            player.HintsShown.Add("quest_scout_enter_dungeon");
            term.WriteLine("");
            term.SetColor("bright_green");
            term.WriteLine($"  {Loc.Get("aldric_quest.dungeon_entered")}");
            term.SetColor("yellow");
            term.WriteLine($"  {Loc.Get("aldric_quest.dungeon_updated")}");
            term.SetColor("white");
            term.WriteLine("");
        }

        // Floor 5 Guardian - one-time mini-boss for new players (v0.52.0)
        if (currentDungeonLevel == 5 && !player.HintsShown.Contains("floor5_guardian_defeated"))
        {
            await ShowFloor5Guardian(player, term);
            // If the player died, don't continue into the dungeon
            if (player.HP <= 0) return;
        }

        // Show NG+ world modifiers on dungeon entry (v0.52.0)
        int ngCycle = StoryProgressionSystem.Instance?.CurrentCycle ?? 1;
        if (ngCycle >= 2)
        {
            term.SetColor("bright_magenta");
            term.WriteLine("");
            term.WriteLine("  Active NG+ Modifiers:");
            if (ngCycle >= 2) term.WriteLine("    Empowered Monsters (+20% stats, +50% gold)");
            if (ngCycle >= 3) term.WriteLine("    Ancient Magic (+30% stats)");
            if (ngCycle >= 4) term.WriteLine("    The Convergence (+50% stats, +100% gold)");
            term.SetColor("white");
            term.WriteLine("");
        }

        // Mark NPC teammates as engaged so the world sim won't kill them
        foreach (var mate in teammates)
        {
            if (mate is NPC npcMate) npcMate.IsInConversation = true;
        }

        try
        {
            // Call base to enter the location loop
            await base.EnterLocation(player, term);
        }
        finally
        {
            // Release NPC teammates when leaving the dungeon
            foreach (var mate in teammates)
            {
                if (mate is NPC npcMate) npcMate.IsInConversation = false;
            }

            // Clean up group dungeon state when leader leaves
            CleanupGroupDungeonOnLeaderExit(player);
        }
    }

    /// <summary>
    /// Guided dungeon tutorial for new players — one-time, skippable.
    /// Teaches floor navigation, rooms, combat, potions, events, features, and rest.
    /// </summary>
    private async Task RunDungeonTutorial(Character player, TerminalEmulator term)
    {
        bool isSR = GameConfig.ScreenReaderMode;
        bool isBBS = IsBBSSession;

        // ── Ask if they want the tutorial ─────────────────────────────────────
        term.ClearScreen();
        if (isSR)
        {
            term.WriteLine("=== DUNGEON TUTORIAL ===", "bright_yellow");
            term.WriteLine("Welcome to the Dungeons!", "white");
        }
        else
        {
            term.WriteLine("╔══════════════════════════════════════════════════════════╗", "bright_yellow");
            term.WriteLine("║              WELCOME TO THE DUNGEONS                    ║", "bright_yellow");
            term.WriteLine("╚══════════════════════════════════════════════════════════╝", "bright_yellow");
        }
        term.WriteLine("");
        term.WriteLine("This looks like your first time venturing underground.", "white");
        term.WriteLine("Would you like a quick guided tour? It covers everything:", "gray");
        term.WriteLine("  navigation, combat, potions, events, and more.", "gray");
        term.WriteLine("");
        term.WriteLine("Skip it if you already know the ropes.", "darkgray");
        term.WriteLine("");

        string ans = await term.GetInput(isSR ? "Take the tutorial? (Y/N): " : "[Y] Take the Tutorial   [N] Skip it > ");
        ans = ans.Trim().ToUpperInvariant();

        // Mark as seen regardless of choice so we never ask again
        player.HintsShown.Add(DUNGEON_TUTORIAL_FLAG);

        if (ans != "Y")
        {
            term.WriteLine("Alright — good luck down there!", "gray");
            await Task.Delay(1200);
            return;
        }

        // Helper to show a tutorial page and wait for the player to continue
        async Task Page(string title, string titleColor, List<(string text, string color)> lines)
        {
            term.ClearScreen();
            if (isSR)
            {
                term.WriteLine($"=== {title.ToUpper()} ===", titleColor);
            }
            else
            {
                string bar = new string('─', Math.Min(title.Length + 4, 60));
                term.WriteLine($"┌─ {title} ─" + new string('─', Math.Max(0, 56 - title.Length)) + "┐", titleColor);
            }
            term.WriteLine("");
            foreach (var (text, color) in lines)
                term.WriteLine(text, color);
            term.WriteLine("");
            await term.PressAnyKey();
        }

        // ── Screen 1: The Floor Map ────────────────────────────────────────────
        await Page("1 of 8 — The Floor Map", "cyan", new()
        {
            ("The dungeon is made up of connected ROOMS spread across a floor.", "white"),
            ("Press [M] from any room to see the map. Read it like this:", "gray"),
            ("", "white"),
            (isBBS ? "  @  You are here" : "  @  You are here (bright yellow)", "bright_yellow"),
            (isBBS ? "  ?  Unexplored room (you haven't been there yet)" : "  ?  Unexplored — you haven't been there yet", "darkgray"),
            (isBBS ? "  #  Cleared room (no threats left)" : "  #  Cleared — no threats remain", "green"),
            (isBBS ? "  X  Room with monsters" : "  █  Room with monsters (danger!)", "red"),
            (isBBS ? "  >  Stairs down to next floor" : "  >  Stairs down to the next floor", "blue"),
            (isBBS ? "  B  Boss room" : "  B  Boss room — powerful enemy within", "bright_red"),
            ("", "white"),
            ("To move between rooms, use the direction keys:", "gray"),
            ("  N = North   S = South   E = East   W = West", "white"),
            ("  (or enter a room number shown on the map)", "darkgray"),
        });

        // ── Screen 2: Entering a Room ──────────────────────────────────────────
        await Page("2 of 8 — Entering Rooms", "cyan", new()
        {
            ("When you enter a room, you'll see what it contains:", "white"),
            ("", "white"),
            ("  MONSTERS  — Enemies waiting to fight you", "red"),
            ("  TREASURE  — Loot to collect after the monsters are dead", "bright_yellow"),
            ("  EVENT     — A special encounter or mystery to investigate", "magenta"),
            ("  FEATURE   — An object to examine: shrine, lever, puzzle", "cyan"),
            ("  STAIRS    — A passage down to the next floor", "blue"),
            ("  SAFE ROOM — No enemies here; great for resting", "green"),
            ("", "white"),
            ("The room menu shows the available actions. You can only loot treasure", "gray"),
            ("or descend the stairs AFTER all monsters in the room are dead.", "gray"),
        });

        // ── Screen 3: Combat ──────────────────────────────────────────────────
        await Page("3 of 8 — Combat", "cyan", new()
        {
            ("Press [F] to Fight the monsters in a room. In combat:", "white"),
            ("", "white"),
            ("  [A] Attack       — Standard hit. Reliable.", "bright_green"),
            ("  [D] Defend       — Skip attacking; reduce damage you take this round.", "bright_green"),
            ("  [P] Power Attack — Hit harder, but you take more damage too.", "bright_green"),
            ("  [S] Precise Strike — Slower but lands more reliably on tough enemies.", "bright_green"),
            ("  [T] Taunt        — Draw enemy attention; protects fragile allies.", "bright_green"),
            ("  [E] Disarm       — Try to knock the enemy's weapon free.", "bright_green"),
            ("  [C] Cast Spell   — Use magic (spellcasters only).", "bright_green"),
            ("  [1-9] Abilities  — Your class special moves.", "bright_green"),
            ("  [R] Flee         — Attempt to escape. May cost HP.", "yellow"),
            ("", "white"),
            ("Tip: check /health often. Dead is bad. Retreat is smart.", "darkgray"),
        });

        // ── Screen 4: Potions & Healing ───────────────────────────────────────
        await Page("4 of 8 — Potions & Healing", "cyan", new()
        {
            ("Healing potions restore HP. Mana potions restore MP.", "white"),
            ("", "white"),
            ("In combat, press [U] to use a potion during your turn.", "bright_green"),
            ("Outside combat, press [P] from the room menu anytime.", "bright_green"),
            ("", "white"),
            ("Where to get potions:", "gray"),
            ("  • The Healer in town sells healing potions (expensive but reliable).", "white"),
            ("  • Treasure chests in dungeon rooms sometimes contain potions.", "white"),
            ("  • Monster drops can include potions.", "white"),
            ("  • If you have Herbs, press [J] to use one (buffs vary by herb type).", "white"),
            ("", "white"),
            ("You carry potions automatically. Check your count with [=] (Status).", "darkgray"),
        });

        // ── Screen 5: Healing Teammates ───────────────────────────────────────
        await Page("5 of 8 — Healing Teammates", "cyan", new()
        {
            ("If you have companions or NPC allies in your party, they fight with you.", "white"),
            ("You can heal them mid-combat with:", "gray"),
            ("", "white"),
            ("  [H] Aid Ally — Give a potion or herbs to a wounded teammate.", "bright_green"),
            ("", "white"),
            ("A well-timed heal can save a companion from death.", "gray"),
            ("Companions who die permanently are GONE — permadeath is real.", "red"),
            ("", "white"),
            ("Recruit companions at the Inn (Aldric) and other town locations.", "darkgray"),
            ("They level up with you and gain their own equipment.", "darkgray"),
        });

        // ── Screen 6: Events & Investigate ───────────────────────────────────
        await Page("6 of 8 — Events & Investigate", "cyan", new()
        {
            ("Some rooms have an EVENT marked on them.", "white"),
            ("Press [V] to Investigate the event.", "bright_green"),
            ("", "white"),
            ("Events can be:", "gray"),
            ("  • A traveller or wandering merchant", "white"),
            ("  • A wounded survivor who needs help", "white"),
            ("  • A mysterious altar or arcane node", "white"),
            ("  • A hidden cache of treasure", "white"),
            ("  • A trap disguised as an opportunity", "white"),
            ("", "white"),
            ("Events often present choices with consequences.", "gray"),
            ("Your alignment and stats influence outcomes.", "darkgray"),
        });

        // ── Screen 7: Examine Features ────────────────────────────────────────
        await Page("7 of 8 — Examine Features", "cyan", new()
        {
            ("Some rooms contain FEATURES — permanent objects to interact with.", "white"),
            ("Press [X] to Examine them.", "bright_green"),
            ("", "white"),
            ("Common features include:", "gray"),
            ("  • Shrines — Pray for a blessing (or risk a curse).", "white"),
            ("  • Levers — Pull them in the right sequence to unlock secrets.", "white"),
            ("  • Puzzles — Riddles and challenges for XP and loot rewards.", "white"),
            ("  • Ancient Inscriptions — Lore about the Old Gods.", "white"),
            ("  • Sealed Doors — Require specific items or quests to open.", "white"),
            ("", "white"),
            ("Features are one-time interactions per floor visit.", "darkgray"),
        });

        // ── Screen 8: Rest, Return & Tips ─────────────────────────────────────
        await Page("8 of 8 — Rest, Return & Tips", "cyan", new()
        {
            ("When you're tired or wounded:", "white"),
            ("", "white"),
            ("  [R] Camp — Rest in a cleared room. Restores HP/MP. Once per floor.", "bright_green"),
            ("  [Q] Quit — Leave the dungeon and return to the surface.", "bright_green"),
            ("  [B] Back — Return to the floor map from a room.", "bright_green"),
            ("  [M] Map  — View the full floor map at any time.", "bright_green"),
            ("", "white"),
            ("A few important tips:", "gray"),
            ("  • Floors within ±10 levels of yours are accessible.", "white"),
            ("  • Floor 25, 40, 55, 70, 85, 95, 100 have Old God bosses.", "white"),
            ("    You cannot leave these floors until you fight them.", "white"),
            ("  • Floors 15, 30, 45, 60, 80, 99 hold ancient Seals to collect.", "white"),
            ("  • Use /health anytime to see your full status and active buffs.", "white"),
            ("  • Use /gear to inspect your equipped items and their bonuses.", "white"),
            ("", "white"),
            ("That's everything. Now go find glory — or a gruesome death!", "bright_yellow"),
        });

        term.WriteLine("Tutorial complete. The dungeon awaits!", "bright_green");
        await Task.Delay(1500);
    }

    /// <summary>
    /// Floor 5 Dungeon Guardian — a one-time mini-boss encounter for new players.
    /// Gives an early boss-fight milestone before the deeper dungeon challenges.
    /// If the player flees, the guardian will appear again on next visit.
    /// </summary>
    private async Task ShowFloor5Guardian(Character player, TerminalEmulator term)
    {
        term.ClearScreen();
        term.WriteLine("");
        term.SetColor("bright_red");
        term.WriteLine("  A massive figure blocks the passage ahead!");
        term.WriteLine("");
        term.SetColor("yellow");
        term.WriteLine("  The Dungeon Guardian rises from the shadows,");
        term.WriteLine("  an ancient sentinel that tests all who seek");
        term.WriteLine("  to venture deeper into the dungeon.");
        term.WriteLine("");
        term.SetColor("gray");
        term.WriteLine("  \"None shall pass without proving their worth!\"");
        term.WriteLine("");
        await term.PressAnyKey();

        // Generate the guardian as a mini-boss scaled for floor 5
        var guardian = MonsterGenerator.GenerateMonster(5, isBoss: false, isMiniBoss: true);
        guardian.Name = "Dungeon Guardian";
        guardian.MonsterColor = "bright_red";
        guardian.CanSpeak = true;
        guardian.Phrase = "You are not yet worthy!";

        // Run full combat using the same pattern as room combat
        var combatEngine = new CombatEngine(terminal);
        var combatResult = await combatEngine.PlayerVsMonsters(player, new List<Monster> { guardian }, teammates);

        if (combatResult.Outcome == CombatOutcome.Victory)
        {
            // Mark as defeated so the guardian won't appear again
            player.HintsShown.Add("floor5_guardian_defeated");

            term.WriteLine("");
            term.SetColor("bright_green");
            term.WriteLine("  The Dungeon Guardian falls!");
            term.WriteLine("");
            term.SetColor("cyan");
            term.WriteLine("  \"You have proven worthy, adventurer.\"");
            term.WriteLine("  \"The deeper floors await...\"");
            term.WriteLine("");

            // Bonus gold reward
            long bonusGold = 1000;
            player.Gold += bonusGold;
            player.Statistics.RecordGoldChange(player.Gold);
            term.SetColor("bright_yellow");
            term.WriteLine($"  The Guardian drops a pouch of {bonusGold:N0} gold!");
            term.WriteLine("");

            await term.PressAnyKey();

            // Unlock achievement
            AchievementSystem.TryUnlock(player, "guardian_slayer");
        }
        else if (combatResult.ShouldReturnToTemple)
        {
            // Player died and was resurrected — exit dungeon
            term.SetColor("yellow");
            term.WriteLine(Loc.Get("dungeon.awaken_temple"));
            await Task.Delay(2000);
            return;
        }
        // If fled, they'll encounter the guardian again next time (no flag set)
    }

    /// <summary>
    /// Check and trigger story events when entering key dungeon floors
    /// This ensures the player experiences the main narrative at appropriate levels
    /// </summary>
    private async Task CheckFloorStoryEvents(Character player, TerminalEmulator term)
    {
        // Check for PreFloor70 Stranger encounter on floors 60-69
        if (currentDungeonLevel >= 60 && currentDungeonLevel <= 69)
        {
            var strangerSystem = UsurperRemake.Systems.StrangerEncounterSystem.Instance;
            // Only queue if Noctura encounter is pending (floor 70 not yet cleared)
            var story70 = StoryProgressionSystem.Instance;
            bool nocturaNotYetFaced = !story70.HasStoryFlag("noctura_combat_start") &&
                                       !story70.HasStoryFlag("noctura_ally");
            if (nocturaNotYetFaced &&
                !strangerSystem.CompletedScriptedEncounters.Contains(UsurperRemake.Systems.ScriptedEncounterType.PreFloor70) &&
                strangerSystem.EncountersHad >= 3)
            {
                strangerSystem.QueueScriptedEncounter(UsurperRemake.Systems.ScriptedEncounterType.PreFloor70);
            }

            // Check if a scripted encounter is ready right now (dungeon location)
            var scriptedEncounter = strangerSystem.GetPendingScriptedEncounter("Dungeon", player);
            if (scriptedEncounter != null)
            {
                await DisplayScriptedStrangerEncounterInDungeon(scriptedEncounter, player, term);
            }
        }

        // Also queue TheMidgameLesson and TheRevelation based on player level
        {
            var strangerSystem = UsurperRemake.Systems.StrangerEncounterSystem.Instance;
            if (player.Level >= 40 && strangerSystem.EncountersHad >= 3 &&
                !strangerSystem.CompletedScriptedEncounters.Contains(UsurperRemake.Systems.ScriptedEncounterType.TheMidgameLesson))
            {
                strangerSystem.QueueScriptedEncounter(UsurperRemake.Systems.ScriptedEncounterType.TheMidgameLesson);
            }

            int resolvedGods = StoryProgressionSystem.Instance?.OldGodStates?.Count(g => g.Value.Status != GodStatus.Unknown) ?? 0;
            if (player.Level >= 55 && strangerSystem.EncountersHad >= 5 && resolvedGods >= 2 &&
                !strangerSystem.CompletedScriptedEncounters.Contains(UsurperRemake.Systems.ScriptedEncounterType.TheRevelation))
            {
                strangerSystem.QueueScriptedEncounter(UsurperRemake.Systems.ScriptedEncounterType.TheRevelation);
            }
        }

        // Check if there's a Seal on this floor that the player hasn't collected
        var sealSystem = SevenSealsSystem.Instance;
        var sealType = sealSystem.GetSealForFloor(currentDungeonLevel);

        // Debug: Show seal status for seal floors
        int[] sealFloors = { 15, 30, 45, 60, 80, 99 };
        var story = StoryProgressionSystem.Instance;
        if (sealFloors.Contains(currentDungeonLevel))
        {
            // Count seal-appropriate rooms on this floor
            int sealRoomCount = currentFloor.Rooms.Count(r =>
                r.Type == RoomType.Shrine ||
                r.Type == RoomType.LoreLibrary ||
                r.Type == RoomType.SecretVault ||
                r.Type == RoomType.MeditationChamber);

            // Show seal info to player on seal floors (if seal not yet collected)
            if (sealType.HasValue)
            {
                term.SetColor("bright_yellow");
                term.WriteLine(Loc.Get("dungeon.seal_on_floor"));
                term.WriteLine(Loc.Get("dungeon.seal_rooms_available", sealRoomCount));
                term.SetColor("white");
            }
            else
            {
                term.SetColor("gray");
                term.WriteLine(Loc.Get("dungeon.seal_already_collected"));
                term.SetColor("white");
            }
        }

        if (sealType.HasValue)
        {
            // Mark that this floor has an uncollected seal - will be found during exploration
            currentFloor.HasUncollectedSeal = true;
            currentFloor.SealType = sealType.Value;
        }

        // Trigger story events at milestone floors (first time only)
        string floorVisitedFlag = $"dungeon_floor_{currentDungeonLevel}_visited";

        if (!story!.HasStoryFlag(floorVisitedFlag))
        {
            story.SetStoryFlag(floorVisitedFlag, true);

            // Story milestone events
            await TriggerFloorStoryEvent(player, term);
        }

        // Quest-dependent events that must fire on every entry (not just first visit)
        // because the quest flag may not exist yet when the floor is first visited
        await CheckQuestDependentFloorEvents(player, term);
    }

    /// <summary>
    /// Floor events that depend on quest state and must re-check on every entry,
    /// not just the first visit. Handles cases where a player visits a floor before
    /// having the required quest flag, then returns later with it active.
    /// </summary>
    private async Task CheckQuestDependentFloorEvents(Character player, TerminalEmulator term)
    {
        switch (currentDungeonLevel)
        {
            case 85:
                // Aurelion save quest return - triggers when entering floor 85 with the Sunforged Blade
                {
                    var story85 = StoryProgressionSystem.Instance;
                    if (story85.OldGodStates.TryGetValue(OldGodType.Aurelion, out var aurelionState) &&
                        aurelionState.Status == GodStatus.Awakened &&
                        ArtifactSystem.Instance.HasArtifact(ArtifactType.SunforgedBlade))
                    {
                        term.WriteLine("");
                        term.SetColor("bright_yellow");
                        term.WriteLine(Loc.Get("dungeon.sunforged_corridor"));
                        await Task.Delay(1500);
                        term.SetColor("bright_cyan");
                        term.WriteLine(Loc.Get("dungeon.sunforged_humming"));
                        await Task.Delay(1500);
                        term.WriteLine("");

                        var saveResult = await OldGodBossSystem.Instance.CompleteSaveQuest(player, OldGodType.Aurelion, term);
                        await HandleGodEncounterResult(saveResult, player, term);

                        if (saveResult.Outcome == BossOutcome.Saved && currentFloor != null)
                        {
                            currentFloor.BossDefeated = true;
                        }
                    }
                }
                break;

            case 90:
                // Sunforged Blade discovery - triggered by Aurelion's save quest
                if (StoryProgressionSystem.Instance.HasStoryFlag("aurelion_save_quest") &&
                    !ArtifactSystem.Instance.HasArtifact(ArtifactType.SunforgedBlade))
                {
                    await ShowStoryMoment(term, Loc.Get("dungeon.sunforged_title"),
                        new[] {
                            Loc.Get("dungeon.sunforged_1"),
                            Loc.Get("dungeon.sunforged_2"),
                            Loc.Get("dungeon.sunforged_3"),
                            "",
                            Loc.Get("dungeon.sunforged_4"),
                            Loc.Get("dungeon.sunforged_5"),
                        }, "bright_yellow");

                    // Grant the artifact
                    await ArtifactSystem.Instance.CollectArtifact(player, ArtifactType.SunforgedBlade, term);

                    term.WriteLine("");
                    term.SetColor("bright_yellow");
                    term.WriteLine(Loc.Get("dungeon.sunforged_warm"));
                    term.WriteLine(Loc.Get("dungeon.sunforged_return"), "yellow");
                }
                break;

            case 95:
                // Moral Paradox: Destroy Darkness (requires Sunforged Blade)
                if (MoralParadoxSystem.Instance.IsParadoxAvailable("destroy_darkness", player))
                {
                    await MoralParadoxSystem.Instance.PresentParadox("destroy_darkness", player, term);
                }
                break;
        }
    }

    /// <summary>
    /// Trigger narrative events at key dungeon floors
    /// </summary>
    private async Task TriggerFloorStoryEvent(Character player, TerminalEmulator term)
    {
        switch (currentDungeonLevel)
        {
            case 10:
                await ShowStoryMoment(term, Loc.Get("dungeon.story_depths_title"),
                    new[] {
                        Loc.Get("dungeon.story_depths_1"),
                        Loc.Get("dungeon.story_depths_2"),
                        "",
                        Loc.Get("dungeon.story_depths_3"),
                        Loc.Get("dungeon.story_depths_4"),
                        "",
                        Loc.Get("dungeon.story_depths_5")
                    }, "cyan");
                break;

            case 15:
                await ShowStoryMoment(term, Loc.Get("dungeon.story_battlefield_title"),
                    new[] {
                        Loc.Get("dungeon.story_battlefield_1"),
                        Loc.Get("dungeon.story_battlefield_2"),
                        "",
                        Loc.Get("dungeon.story_battlefield_3"),
                        Loc.Get("dungeon.story_battlefield_4"),
                        "",
                        Loc.Get("dungeon.story_battlefield_5"),
                        "",
                        Loc.Get("dungeon.story_battlefield_6"),
                        Loc.Get("dungeon.story_battlefield_7")
                    }, "dark_red");
                StoryProgressionSystem.Instance.SetStoryFlag("knows_about_seals", true);
                break;

            case 25:
                await ShowStoryMoment(term, Loc.Get("dungeon.story_whispers_title"),
                    new[] {
                        Loc.Get("dungeon.story_whispers_1"),
                        Loc.Get("dungeon.story_whispers_2"),
                        "",
                        Loc.Get("dungeon.story_whispers_3"),
                        Loc.Get("dungeon.story_whispers_4"),
                        "",
                        Loc.Get("dungeon.story_whispers_5"),
                        Loc.Get("dungeon.story_whispers_6")
                    }, "bright_cyan");
                StoryProgressionSystem.Instance.SetStoryFlag("heard_whispers", true);
                // Moral Paradox: The Possessed Child
                if (MoralParadoxSystem.Instance.IsParadoxAvailable("possessed_child", player))
                {
                    await MoralParadoxSystem.Instance.PresentParadox("possessed_child", player, term);
                }
                break;

            case 30:
                await ShowStoryMoment(term, Loc.Get("dungeon.story_shrine_title"),
                    new[] {
                        Loc.Get("dungeon.story_shrine_1"),
                        Loc.Get("dungeon.story_shrine_2"),
                        "",
                        Loc.Get("dungeon.story_shrine_3"),
                        Loc.Get("dungeon.story_shrine_4"),
                        Loc.Get("dungeon.story_shrine_5"),
                        "",
                        Loc.Get("dungeon.story_shrine_6"),
                        "",
                        Loc.Get("dungeon.story_shrine_7")
                    }, "dark_magenta");
                break;

            case 40:
                // Veloura save quest return - triggers when entering floor 40 with the Loom
                {
                    var story40 = StoryProgressionSystem.Instance;
                    if (story40.OldGodStates.TryGetValue(OldGodType.Veloura, out var velouraState) &&
                        velouraState.Status == GodStatus.Awakened &&
                        ArtifactSystem.Instance.HasArtifact(ArtifactType.SoulweaversLoom))
                    {
                        term.WriteLine("");
                        term.SetColor("bright_magenta");
                        term.WriteLine(Loc.Get("dungeon.veloura_warmth"));
                        await Task.Delay(1500);
                        term.SetColor("bright_cyan");
                        term.WriteLine(Loc.Get("dungeon.veloura_senses"));
                        await Task.Delay(1500);
                        term.WriteLine("");

                        var saveResult = await OldGodBossSystem.Instance.CompleteSaveQuest(player, OldGodType.Veloura, term);
                        await HandleGodEncounterResult(saveResult, player, term);

                        if (saveResult.Outcome == BossOutcome.Saved && currentFloor != null)
                        {
                            currentFloor.BossDefeated = true;
                        }
                    }
                }
                break;

            case 45:
                await ShowStoryMoment(term, Loc.Get("dungeon.story_prison_title"),
                    new[] {
                        Loc.Get("dungeon.story_prison_1"),
                        Loc.Get("dungeon.story_prison_2"),
                        "",
                        Loc.Get("dungeon.story_prison_3"),
                        Loc.Get("dungeon.story_prison_4"),
                        "",
                        Loc.Get("dungeon.story_prison_5"),
                        Loc.Get("dungeon.story_prison_6"),
                        "",
                        Loc.Get("dungeon.story_prison_7")
                    }, "gray");
                StoryProgressionSystem.Instance.AdvanceChapter(StoryChapter.TheWhispers);
                break;

            case 60:
                await ShowStoryMoment(term, Loc.Get("dungeon.story_oracle_title"),
                    new[] {
                        Loc.Get("dungeon.story_oracle_1"),
                        Loc.Get("dungeon.story_oracle_2"),
                        "",
                        Loc.Get("dungeon.story_oracle_3"),
                        Loc.Get("dungeon.story_oracle_4"),
                        Loc.Get("dungeon.story_oracle_5"),
                        "",
                        Loc.Get("dungeon.story_oracle_6"),
                        Loc.Get("dungeon.story_oracle_7"),
                        "",
                        Loc.Get("dungeon.story_oracle_8")
                    }, "bright_cyan");
                StoryProgressionSystem.Instance.SetStoryFlag("knows_prophecy", true);

                // MAELKETH ENCOUNTER - First Old God boss
                if (OldGodBossSystem.Instance.CanEncounterBoss(player, OldGodType.Maelketh))
                {
                    term.WriteLine("");
                    term.WriteLine(Loc.Get("dungeon.ground_trembles"), "bright_red");
                    await Task.Delay(2000);

                    term.WriteLine(Loc.Get("dungeon.face_maelketh"), "yellow");
                    var response = await term.GetInput("> ");
                    if (response.Trim().ToUpper().StartsWith("Y"))
                    {
                        var result = await OldGodBossSystem.Instance.StartBossEncounter(player, OldGodType.Maelketh, term, teammates);
                        await HandleGodEncounterResult(result, player, term);
                    }
                    else
                    {
                        term.WriteLine(Loc.Get("dungeon.god_retreats"), "gray");
                    }
                }
                break;

            case 65:
                // Soulweaver's Loom discovery - triggered by Veloura's save quest
                if (StoryProgressionSystem.Instance.HasStoryFlag("veloura_save_quest") &&
                    !ArtifactSystem.Instance.HasArtifact(ArtifactType.SoulweaversLoom))
                {
                    await ShowStoryMoment(term, Loc.Get("dungeon.story_soulweaver_title"),
                        new[] {
                            Loc.Get("dungeon.story_soulweaver_1"),
                            Loc.Get("dungeon.story_soulweaver_2"),
                            Loc.Get("dungeon.story_soulweaver_3"),
                            "",
                            Loc.Get("dungeon.story_soulweaver_4"),
                            Loc.Get("dungeon.story_soulweaver_5"),
                        }, "bright_magenta");

                    // Grant the artifact
                    await ArtifactSystem.Instance.CollectArtifact(player, ArtifactType.SoulweaversLoom, term);

                    term.WriteLine("");
                    term.SetColor("bright_yellow");
                    term.WriteLine(Loc.Get("dungeon.remember_veloura"));
                    term.WriteLine(Loc.Get("dungeon.return_veloura_loom"), "yellow");
                }

                // Moral Paradox: Veloura's Cure (deeper choice about the Loom's cost)
                if (MoralParadoxSystem.Instance.IsParadoxAvailable("velouras_cure", player))
                {
                    term.WriteLine("");
                    await ShowStoryMoment(term, Loc.Get("dungeon.story_soulweaver_price_title"),
                        new[] {
                            Loc.Get("dungeon.story_soulweaver_price_1"),
                            Loc.Get("dungeon.story_soulweaver_price_2"),
                        }, "bright_magenta");
                    await MoralParadoxSystem.Instance.PresentParadox("velouras_cure", player, term);
                }
                break;

            case 75:
                await ShowStoryMoment(term, Loc.Get("dungeon.story_echoes_title"),
                    new[] {
                        Loc.Get("dungeon.story_echoes_1"),
                        Loc.Get("dungeon.story_echoes_2"),
                        "",
                        Loc.Get("dungeon.story_echoes_3"),
                        Loc.Get("dungeon.story_echoes_4"),
                        "",
                        Loc.Get("dungeon.story_echoes_5"),
                        Loc.Get("dungeon.story_echoes_6"),
                        Loc.Get("dungeon.story_echoes_7"),
                        "",
                        Loc.Get("dungeon.story_echoes_8"),
                        Loc.Get("dungeon.story_echoes_9")
                    }, "bright_magenta");
                AmnesiaSystem.Instance?.RecoverMemory(MemoryFragment.TheDecision);
                break;

            case 80:
                await ShowStoryMoment(term, Loc.Get("dungeon.story_mourning_title"),
                    new[] {
                        Loc.Get("dungeon.story_mourning_1"),
                        Loc.Get("dungeon.story_mourning_2"),
                        "",
                        Loc.Get("dungeon.story_mourning_3"),
                        Loc.Get("dungeon.story_mourning_4"),
                        "",
                        Loc.Get("dungeon.story_mourning_5"),
                        Loc.Get("dungeon.story_mourning_6"),
                        "",
                        Loc.Get("dungeon.story_mourning_7"),
                        "",
                        Loc.Get("dungeon.story_mourning_8")
                    }, "bright_blue");
                // Moral Paradox: Free Terravok (alternative to combat)
                if (MoralParadoxSystem.Instance.IsParadoxAvailable("free_terravok", player))
                {
                    await MoralParadoxSystem.Instance.PresentParadox("free_terravok", player, term);
                }
                break;

            // Cases 85 and 90 moved to CheckQuestDependentFloorEvents() — they depend on
            // quest state that may not exist on first visit (aurelion_save_quest flag)

            case 95:
                // Moral Paradox story text (first-visit only); actual paradox check is in CheckQuestDependentFloorEvents
                if (MoralParadoxSystem.Instance.IsParadoxAvailable("destroy_darkness", player))
                {
                    await ShowStoryMoment(term, Loc.Get("dungeon.story_purging_title"),
                        new[] {
                            Loc.Get("dungeon.story_purging_1"),
                            Loc.Get("dungeon.story_purging_2"),
                            "",
                            Loc.Get("dungeon.story_purging_3"),
                            Loc.Get("dungeon.story_purging_4")
                        }, "bright_yellow");
                    await MoralParadoxSystem.Instance.PresentParadox("destroy_darkness", player, term);
                }
                break;

            case 99:
                await ShowStoryMoment(term, Loc.Get("dungeon.story_threshold_title"),
                    new[] {
                        Loc.Get("dungeon.story_threshold_1"),
                        Loc.Get("dungeon.story_threshold_2"),
                        "",
                        Loc.Get("dungeon.story_threshold_3"),
                        Loc.Get("dungeon.story_threshold_4"),
                        "",
                        Loc.Get("dungeon.story_threshold_5"),
                        Loc.Get("dungeon.story_threshold_6"),
                        "",
                        Loc.Get("dungeon.story_threshold_7"),
                        Loc.Get("dungeon.story_threshold_8")
                    }, "white");
                break;

            case 100:
                await ShowStoryMoment(term, Loc.Get("dungeon.story_end_title"),
                    new[] {
                        Loc.Get("dungeon.story_end_1"),
                        "",
                        Loc.Get("dungeon.story_end_2"),
                        Loc.Get("dungeon.story_end_3"),
                        Loc.Get("dungeon.story_end_4"),
                        Loc.Get("dungeon.story_end_5"),
                        "",
                        Loc.Get("dungeon.story_end_6"),
                        Loc.Get("dungeon.story_end_7"),
                        Loc.Get("dungeon.story_end_8"),
                        Loc.Get("dungeon.story_end_9"),
                        "",
                        Loc.Get("dungeon.story_end_10"),
                        Loc.Get("dungeon.story_end_11"),
                        "",
                        Loc.Get("dungeon.story_end_12")
                    }, "bright_yellow");
                StoryProgressionSystem.Instance.AdvanceChapter(StoryChapter.FinalConfrontation);
                break;
        }
    }

    /// <summary>
    /// Add active companions to the party's teammates list for combat
    /// </summary>
    private async Task AddCompanionsToParty(Character player, TerminalEmulator term)
    {
        var companionSystem = UsurperRemake.Systems.CompanionSystem.Instance;
        var companionCharacters = companionSystem.GetCompanionsAsCharacters();

        if (companionCharacters.Count == 0)
            return;

        // Remove any existing companion entries (in case of re-entry)
        teammates.RemoveAll(t => t.IsCompanion);

        // Add companions to teammates list (with defensive CompanionId dedup)
        foreach (var companion in companionCharacters)
        {
            if (companion.IsAlive && !teammates.Any(t => t.CompanionId == companion.CompanionId))
            {
                teammates.Add(companion);
            }
        }

        // Show companion status if any are present
        if (companionCharacters.Count > 0)
        {
            term.WriteLine("");
            WriteSectionHeader(Loc.Get("dungeon.section_companions"), "bright_cyan");
            foreach (var companion in companionCharacters)
            {
                var companionData = companionSystem.GetCompanion(companion.CompanionId!.Value);
                term.SetColor("white");
                term.Write($"  {companion.DisplayName}");
                term.SetColor("gray");
                term.Write($" ({companionData?.CombatRole}) ");
                term.SetColor(companion.HP > companion.MaxHP / 2 ? "green" : "yellow");
                term.WriteLine($"{Loc.Get("combat.bar_hp")}: {companion.HP}/{companion.MaxHP}");
            }
            term.WriteLine("");
            await Task.Delay(1500);
        }
    }

    /// <summary>
    /// Restore NPC teammates (spouses, team members, lovers) from saved state.
    /// Respects party cap of 4 - companions are added first and take priority.
    /// </summary>
    private async Task RestoreNPCTeammates(TerminalEmulator term)
    {
        var savedNPCIds = GameEngine.Instance?.DungeonPartyNPCIds;
        if (savedNPCIds == null || savedNPCIds.Count == 0)
            return;

        var npcSystem = UsurperRemake.Systems.NPCSpawnSystem.Instance;
        if (npcSystem == null)
            return;

        int restoredCount = 0;
        int skippedCount = 0;
        const int maxPartySize = 4;

        foreach (var npcId in savedNPCIds)
        {
            // Check party cap before adding
            if (teammates.Count >= maxPartySize)
            {
                skippedCount++;
                continue;
            }

            var npc = npcSystem.ActiveNPCs?.FirstOrDefault(n => n.ID == npcId && n.IsAlive);
            if (npc != null && !teammates.Any(t => t is NPC existingNpc && existingNpc.ID == npcId))
            {
                LevelMasterLocation.EnsureClassStatsForLevel(npc); // Retroactively fix legacy stat gaps
                teammates.Add(npc);
                npc.UpdateLocation("Dungeon");
                restoredCount++;
            }
        }

        if (restoredCount > 0)
        {
            term.WriteLine("");
            WriteSectionHeader(Loc.Get("dungeon.party_restored"), "bright_cyan");
            term.SetColor("green");
            term.WriteLine(Loc.Get("dungeon.allies_rejoin", restoredCount));
            term.WriteLine("");
            await Task.Delay(1500);
        }

        // Notify if some allies couldn't join due to party cap
        if (skippedCount > 0)
        {
            term.SetColor("yellow");
            term.WriteLine(Loc.Get("dungeon.allies_skipped", skippedCount, maxPartySize));
            term.WriteLine(Loc.Get("dungeon.use_party_management"));
            term.WriteLine("");
            await Task.Delay(1500);
        }
    }

    /// <summary>
    /// Restore player echoes from database for cooperative dungeon runs.
    /// Player allies are loaded as AI-controlled Characters with IsEcho = true.
    /// </summary>
    private async Task RestorePlayerTeammates(TerminalEmulator term)
    {
        if (!UsurperRemake.BBS.DoorMode.IsOnlineMode) return;

        var playerNames = GameEngine.Instance?.DungeonPartyPlayerNames;
        if (playerNames == null || playerNames.Count == 0) return;

        var backend = SaveSystem.Instance.Backend as SqlSaveBackend;
        if (backend == null) return;

        const int maxPartySize = 4;
        int restoredCount = 0;

        foreach (var name in playerNames)
        {
            if (teammates.Count >= maxPartySize) break;

            // Skip if already in party
            if (teammates.Any(t => t.DisplayName.Equals(name, StringComparison.OrdinalIgnoreCase)))
                continue;

            try
            {
                // Load player's save data from database
                var saveData = await backend.ReadGameData(name.ToLower());
                if (saveData?.Player == null) continue;

                // Verify they're still on the same team
                if (currentPlayer != null && !string.IsNullOrEmpty(currentPlayer.Team))
                {
                    if (saveData.Player.Team != currentPlayer.Team)
                    {
                        term.SetColor("yellow");
                        term.WriteLine(Loc.Get("dungeon.not_on_team", name));
                        continue;
                    }
                }

                // Create echo character
                var ally = PlayerCharacterLoader.CreateFromSaveData(saveData.Player, name, isEcho: true);
                teammates.Add(ally);
                restoredCount++;

                term.SetColor("bright_cyan");
                term.WriteLine(Loc.Get("dungeon.echo_materializes", name));
            }
            catch (Exception ex)
            {
                DebugLogger.Instance.LogError("DUNGEON", $"Failed to load player echo '{name}': {ex.Message}");
            }
        }

        if (restoredCount > 0)
        {
            term.SetColor("gray");
            term.WriteLine(Loc.Get("dungeon.echoes_info"));
            term.WriteLine("");
            await Task.Delay(1500);
        }
    }

    /// <summary>
    /// Add royal mercenaries (hired bodyguards) to dungeon party for king players.
    /// </summary>
    private void AddRoyalMercenariesToParty(Character player, TerminalEmulator term)
    {
        if (!player.King || player.RoyalMercenaries == null || player.RoyalMercenaries.Count == 0) return;

        const int maxPartySize = 4;
        int addedCount = 0;

        foreach (var merc in player.RoyalMercenaries)
        {
            if (teammates.Count >= maxPartySize) break;
            if (merc.HP <= 0) continue; // Dead mercenaries don't join

            // Skip if already in party (shouldn't happen since teammates is cleared on entry)
            if (teammates.Any(t => t.IsMercenary && t.MercenaryName == merc.Name)) continue;

            var character = new Character
            {
                Name2 = merc.Name,
                Class = merc.Class,
                Sex = merc.Sex,
                Level = merc.Level,
                HP = merc.HP,
                MaxHP = merc.MaxHP,
                Mana = merc.Mana,
                MaxMana = merc.MaxMana,
                Strength = merc.Strength,
                Defence = merc.Defence,
                WeapPow = merc.WeapPow,
                ArmPow = merc.ArmPow,
                Agility = merc.Agility,
                Dexterity = merc.Dexterity,
                Wisdom = merc.Wisdom,
                Intelligence = merc.Intelligence,
                Constitution = merc.Constitution,
                Healing = merc.Healing,
                AI = CharacterAI.Computer,
                IsMercenary = true,
                MercenaryName = merc.Name,
                // Set Base* fields so RecalculateStats won't zero them
                BaseMaxHP = merc.MaxHP,
                BaseMaxMana = merc.MaxMana,
                BaseStrength = merc.Strength,
                BaseDefence = merc.Defence,
                BaseAgility = merc.Agility,
                BaseDexterity = merc.Dexterity,
                BaseWisdom = merc.Wisdom,
                BaseIntelligence = merc.Intelligence,
                BaseConstitution = merc.Constitution,
                Stamina = 5 + merc.Level * 2
            };

            teammates.Add(character);
            addedCount++;
        }

        if (addedCount > 0)
        {
            term.SetColor("bright_cyan");
            term.WriteLine(Loc.Get("dungeon.bodyguards_join", addedCount));
            foreach (var t in teammates.Where(t => t.IsMercenary))
            {
                term.SetColor("white");
                term.Write($"    {t.DisplayName}");
                term.SetColor("gray");
                term.WriteLine(Loc.Get("dungeon.merc_level_class", t.Level, t.Class));
            }
            term.WriteLine("");
        }
    }

    /// <summary>
    /// Sync current NPC teammates to GameEngine for persistence
    /// </summary>
    private void SyncNPCTeammatesToGameEngine()
    {
        var npcIds = teammates
            .OfType<NPC>()
            .Select(n => n.ID)
            .ToList();
        GameEngine.Instance?.SetDungeonPartyNPCs(npcIds);
    }

    /// <summary>
    /// Remove duplicate entries from the teammates list.
    /// Uses NPC ID for NPCs, CompanionId for companions, and DisplayName for others.
    /// </summary>
    private void DeduplicateTeammates()
    {
        var seen = new HashSet<string>();
        int removed = 0;

        for (int i = teammates.Count - 1; i >= 0; i--)
        {
            var t = teammates[i];
            string key;

            if (t is NPC npc)
                key = $"npc:{npc.ID}";
            else if (t.IsCompanion && t.CompanionId.HasValue)
                key = $"comp:{t.CompanionId.Value}";
            else
                key = $"name:{t.DisplayName}";

            if (!seen.Add(key))
            {
                teammates.RemoveAt(i);
                removed++;
            }
        }

        if (removed > 0)
        {
            DebugLogger.Instance.Log(DebugLogger.LogLevel.Warning, "DUNGEON",
                $"Removed {removed} duplicate teammate(s) from party list");
        }
    }

    /// <summary>
    /// Check for dungeon entry fees for overleveled teammates
    /// Displays fee breakdown and asks player to confirm payment
    /// Returns true if entry is allowed (no fees, or player paid)
    /// </summary>
    private async Task<bool> CheckAndPayEntryFees(Character player, TerminalEmulator term)
    {
        var balanceSystem = UsurperRemake.Systems.TeamBalanceSystem.Instance;
        long totalFee = balanceSystem.CalculateTotalEntryFees(player, teammates);

        // No fees needed
        if (totalFee == 0)
        {
            // Still show XP penalty info if applicable
            float xpMult = balanceSystem.CalculateXPMultiplier(player, teammates);
            if (xpMult < 1.0f)
            {
                await balanceSystem.DisplayFeeInfo(term, player, teammates);
            }
            return true;
        }

        // Display fee information
        await balanceSystem.DisplayFeeInfo(term, player, teammates);

        // Check if player can afford all fees
        if (player.Gold < totalFee)
        {
            term.SetColor("yellow");
            term.WriteLine(Loc.Get("dungeon.need_gold_fee", totalFee.ToString("N0"), player.Gold.ToString("N0")));
            term.WriteLine("");

            // Remove teammates player can't afford, starting with most expensive
            var breakdown = balanceSystem.GetFeeBreakdown(player, teammates).OrderByDescending(b => b.fee).ToList();
            long remainingGold = player.Gold;
            var affordableTeammates = new List<NPC>();
            var unaffordableTeammates = new List<(NPC npc, long fee)>();

            foreach (var (npc, fee, _) in breakdown)
            {
                if (fee == 0)
                {
                    // Free teammates always come
                    affordableTeammates.Add(npc);
                }
                else if (remainingGold >= fee)
                {
                    // Can afford this one
                    affordableTeammates.Add(npc);
                    remainingGold -= fee;
                }
                else
                {
                    // Can't afford
                    unaffordableTeammates.Add((npc, fee));
                }
            }

            if (unaffordableTeammates.Count > 0)
            {
                term.SetColor("gray");
                term.WriteLine(Loc.Get("dungeon.allies_too_expensive"));
                foreach (var (npc, fee) in unaffordableTeammates)
                {
                    term.WriteLine($"  {npc.Name}: {fee:N0} gold", "darkgray");
                }
                term.WriteLine("");
            }

            // Calculate what player CAN afford
            long affordableFee = player.Gold - remainingGold;

            if (affordableFee > 0 && affordableTeammates.Any(t => breakdown.Any(b => b.npc == t && b.fee > 0)))
            {
                term.SetColor("cyan");
                var payChoice = await term.GetInput(Loc.Get("dungeon.pay_affordable", affordableFee.ToString("N0")));

                if (payChoice.ToUpper().StartsWith("Y"))
                {
                    player.Gold -= affordableFee;
                    term.SetColor("green");
                    term.WriteLine(Loc.Get("dungeon.paid_gold", affordableFee.ToString("N0")));
                }
                else
                {
                    // Don't pay - remove all paid allies
                    foreach (var (npc, fee, _) in breakdown.Where(b => b.fee > 0))
                    {
                        if (affordableTeammates.Contains(npc))
                        {
                            affordableTeammates.Remove(npc);
                            unaffordableTeammates.Add((npc, fee));
                        }
                    }
                }
            }

            // Update teammates list - keep only affordable ones
            teammates.Clear();
            foreach (var npc in affordableTeammates)
            {
                teammates.Add(npc);
            }

            if (unaffordableTeammates.Count > 0)
            {
                term.SetColor("gray");
                term.WriteLine(Loc.Get("dungeon.staying_behind"));
                foreach (var (npc, _) in unaffordableTeammates)
                {
                    term.WriteLine(Loc.Get("dungeon.waits_at_entrance", npc.Name), "darkgray");
                }
            }

            SyncNPCTeammatesToGameEngine();
            await Task.Delay(1500);
            return true; // Allow entry with whoever player can afford
        }

        // Player can afford all fees - ask for confirmation
        term.SetColor("cyan");
        var confirm = await term.GetInput(Loc.Get("dungeon.pay_all_allies", totalFee.ToString("N0")));

        if (confirm.ToUpper().StartsWith("Y"))
        {
            player.Gold -= totalFee;
            term.SetColor("green");
            term.WriteLine(Loc.Get("dungeon.paid_allies_prepare", totalFee.ToString("N0")));
            term.SetColor("gray");
            term.WriteLine(Loc.Get("dungeon.remaining_gold", player.Gold.ToString("N0")));
            await Task.Delay(1000);
            return true;
        }
        else
        {
            term.SetColor("gray");
            term.WriteLine(Loc.Get("dungeon.decline_pay"));

            // Remove overleveled NPCs from party
            var breakdown = balanceSystem.GetFeeBreakdown(player, teammates);
            foreach (var (npc, fee, _) in breakdown.Where(b => b.fee > 0))
            {
                teammates.Remove(npc);
                term.WriteLine(Loc.Get("dungeon.stays_behind", npc.Name), "darkgray");
            }

            SyncNPCTeammatesToGameEngine();
            await Task.Delay(1000);
            return true; // Still allow entry, just without the expensive teammates
        }
    }

    /// <summary>
    /// Display a story moment with dramatic formatting
    /// </summary>
    private async Task ShowStoryMoment(TerminalEmulator term, string title, string[] lines, string color)
    {
        term.ClearScreen();
        term.WriteLine("");
        term.SetColor(color);
        if (GameConfig.ScreenReaderMode)
        {
            term.WriteLine(title);
        }
        else
        {
            term.WriteLine($"╔{'═'.ToString().PadRight(58, '═')}╗");
            term.WriteLine($"║  {title.PadRight(55)} ║");
            term.WriteLine($"╚{'═'.ToString().PadRight(58, '═')}╝");
        }
        term.WriteLine("");

        await Task.Delay(1000);

        foreach (var line in lines)
        {
            if (string.IsNullOrEmpty(line))
            {
                term.WriteLine("");
            }
            else
            {
                term.SetColor("white");
                term.WriteLine($"  {line}");
            }
            await Task.Delay(200);
        }

        term.WriteLine("");
        term.SetColor("gray");
        await term.GetInputAsync(Loc.Get("dungeon.press_enter_continue"));
    }

    /// <summary>
    /// Display a scripted Stranger encounter in the dungeon (PreFloor70 or others).
    /// Similar to BaseLocation's version but adapted for dungeon context.
    /// </summary>
    private async Task DisplayScriptedStrangerEncounterInDungeon(
        UsurperRemake.Systems.ScriptedStrangerEncounter encounter, Character player, TerminalEmulator term)
    {
        term.ClearScreen();
        WriteBoxHeader(encounter.Title, "dark_magenta");
        term.WriteLine("");

        // Intro narration
        foreach (var line in encounter.IntroNarration)
        {
            term.SetColor("gray");
            term.WriteLine($"  {line}");
            await Task.Delay(1200);
        }
        term.WriteLine("");
        await Task.Delay(500);

        // Disguise
        var disguiseData = UsurperRemake.Systems.StrangerEncounterSystem.Disguises.GetValueOrDefault(encounter.Disguise);
        if (disguiseData != null)
        {
            term.SetColor("white");
            term.WriteLine($"  {disguiseData.Name}");
            term.SetColor("darkgray");
            term.WriteLine($"  {disguiseData.Description}");
            term.WriteLine("");
            await Task.Delay(800);
        }

        // Dialogue
        foreach (var line in encounter.Dialogue)
        {
            if (string.IsNullOrEmpty(line))
            {
                term.WriteLine("");
                await Task.Delay(400);
                continue;
            }
            term.SetColor("bright_magenta");
            term.WriteLine($"  {line}");
            await Task.Delay(1000);
        }
        term.WriteLine("");
        await Task.Delay(500);

        // Response choices
        var responseType = UsurperRemake.Systems.StrangerResponseType.Silent;
        int receptivityChange = 0;

        if (encounter.Responses.Count > 0)
        {
            term.SetColor("cyan");
            term.WriteLine(Loc.Get("dungeon.how_respond"));
            term.WriteLine("");

            foreach (var opt in encounter.Responses)
            {
                term.SetColor("darkgray");
                term.Write("    [");
                term.SetColor("bright_yellow");
                term.Write($"{opt.Key}");
                term.SetColor("darkgray");
                term.Write("] ");
                term.SetColor("white");
                term.WriteLine(opt.Text);
            }

            term.WriteLine("");
            var choice = await term.GetInput(Loc.Get("dungeon.your_response"));

            var selectedOpt = encounter.Responses
                .FirstOrDefault(o => o.Key.Equals(choice?.Trim(), StringComparison.OrdinalIgnoreCase));

            if (selectedOpt != null)
            {
                responseType = selectedOpt.ResponseType;
                receptivityChange = selectedOpt.ReceptivityChange;

                term.WriteLine("");
                foreach (var replyLine in selectedOpt.StrangerReply)
                {
                    term.SetColor("magenta");
                    term.WriteLine($"  {replyLine}");
                    await Task.Delay(1000);
                }
                term.WriteLine("");
                await Task.Delay(500);
            }
        }

        // Closing narration
        if (encounter.ClosingNarration.Length > 0)
        {
            term.WriteLine("");
            foreach (var line in encounter.ClosingNarration)
            {
                term.SetColor("gray");
                term.WriteLine($"  {line}");
                await Task.Delay(1200);
            }
            term.WriteLine("");
        }

        // Record completion
        UsurperRemake.Systems.StrangerEncounterSystem.Instance.CompleteScriptedEncounter(
            encounter.Type, responseType, receptivityChange);

        await term.PressAnyKey();
    }

    /// <summary>
    /// Handle the result of an Old God boss encounter
    /// </summary>
    private async Task HandleGodEncounterResult(BossEncounterResult result, Character player, TerminalEmulator term)
    {
        if (result == null || !result.Success) return;

        switch (result.Outcome)
        {
            case BossOutcome.Defeated:
                term.WriteLine("");
                term.WriteLine(Loc.Get("dungeon.god_fallen"), "bright_yellow");

                // Grant artifact based on god defeated
                var artifactType = GetArtifactForGod(result.God);
                if (artifactType.HasValue)
                {
                    term.WriteLine(Loc.Get("dungeon.obtained_artifact", artifactType.Value), "bright_magenta");
                    StoryProgressionSystem.Instance.CollectedArtifacts.Add(artifactType.Value);
                }

                // XP and gold reward
                if (result.XPGained > 0)
                {
                    player.Experience += result.XPGained;
                    term.WriteLine(Loc.Get("dungeon.xp_gained", result.XPGained), "green");
                }
                if (result.GoldGained > 0)
                {
                    player.Gold += result.GoldGained;
                    term.WriteLine(Loc.Get("dungeon.gold_gained", result.GoldGained), "yellow");
                }

                // Chivalry impact
                player.Darkness += 100;
                term.WriteLine(Loc.Get("dungeon.darkness_deepens"), "red");

                // Check achievements
                AchievementSystem.CheckAchievements(player);
                await AchievementSystem.ShowPendingNotifications(term, player);

                // Online news: Old God defeated
                if (UsurperRemake.Systems.OnlineStateManager.IsActive)
                {
                    var godDisplayName = player.Name2 ?? player.Name1;
                    _ = UsurperRemake.Systems.OnlineStateManager.Instance!.AddNews(
                        $"{godDisplayName} has slain the Old God {result.God} on floor {currentDungeonLevel}!", "combat");
                }
                break;

            case BossOutcome.Saved:
                term.WriteLine("");
                term.WriteLine(Loc.Get("dungeon.god_corruption_lifts"), "bright_cyan");
                term.WriteLine(Loc.Get("dungeon.divine_gratitude"), "white");

                player.Chivalry += 150;
                term.WriteLine(Loc.Get("dungeon.chivalry_grows"), "bright_green");

                // Ocean Philosophy moment
                OceanPhilosophySystem.Instance.CollectFragment(WaveFragment.TheCycle);

                // Online news: Old God saved
                if (UsurperRemake.Systems.OnlineStateManager.IsActive)
                {
                    var saviorName = player.Name2 ?? player.Name1;
                    _ = UsurperRemake.Systems.OnlineStateManager.Instance!.AddNews(
                        $"{saviorName} has saved the Old God {result.God} from corruption!", "quest");
                }
                break;

            case BossOutcome.Allied:
                term.WriteLine("");
                term.WriteLine(Loc.Get("dungeon.god_alliance"), "bright_magenta");
                term.WriteLine(Loc.Get("dungeon.forged_alliance"), "white");

                player.Chivalry += 50;
                player.Wisdom += 2;
                break;

            case BossOutcome.Spared:
                term.WriteLine("");
                term.WriteLine(Loc.Get("dungeon.god_spared_hope"), "bright_cyan");
                term.WriteLine(Loc.Get("dungeon.god_spared_lingers"), "white");
                term.WriteLine("");
                term.SetColor("bright_magenta");
                term.WriteLine(Loc.Get("dungeon.promised_loom"));
                term.WriteLine(Loc.Get("dungeon.artifact_deeper"), "yellow");
                term.WriteLine("");

                player.Chivalry += 75;
                term.WriteLine(Loc.Get("dungeon.chivalry_mercy"), "bright_green");
                term.SetColor("bright_yellow");
                term.WriteLine("");
                term.WriteLine(Loc.Get("dungeon.quest_started_loom"));

                // Online news
                if (UsurperRemake.Systems.OnlineStateManager.IsActive)
                {
                    var sparerName = player.Name2 ?? player.Name1;
                    _ = UsurperRemake.Systems.OnlineStateManager.Instance!.AddNews(
                        $"{sparerName} has shown mercy to the Old God {result.God} and vowed to find a cure!", "quest");
                }
                break;

            case BossOutcome.Fled:
                term.WriteLine("");
                term.WriteLine(Loc.Get("dungeon.god_fled_retreat"), "gray");
                term.WriteLine(Loc.Get("dungeon.god_fled_wait"), "dark_gray");
                break;
        }

        await Task.Delay(3000);

        // Auto-return to town with reaction scene for resolved encounters (not Manwe — has own ending)
        if (result.God != OldGodType.Manwe &&
            result.Outcome != BossOutcome.Fled &&
            result.Outcome != BossOutcome.PlayerDefeated)
        {
            // Grant God Slayer buff — temporary divine power surge (v0.49.3)
            player.GodSlayerCombats = GameConfig.GodSlayerBuffDuration;
            player.GodSlayerDamageBonus = GameConfig.GodSlayerDamageBonus;
            player.GodSlayerDefenseBonus = GameConfig.GodSlayerDefenseBonus;
            term.WriteLine("");
            term.SetColor("bright_yellow");
            term.WriteLine(Loc.Get("dungeon.god_slayer_buff", (int)(GameConfig.GodSlayerDamageBonus * 100), (int)(GameConfig.GodSlayerDefenseBonus * 100), GameConfig.GodSlayerBuffDuration));
            await Task.Delay(2000);

            await ShowTownReactionScene(result, player, term);
            throw new LocationExitException(GameLocation.MainStreet);
        }
    }

    /// <summary>
    /// Cinematic scene when the player emerges from the dungeon after an Old God encounter.
    /// Townsfolk react based on the god encountered, the outcome, and the player's approach.
    /// </summary>
    private async Task ShowTownReactionScene(BossEncounterResult result, Character player, TerminalEmulator term)
    {
        var godData = OldGodsData.GetGodBossData(result.God);
        var playerName = player.Name2 ?? player.Name1;

        term.Clear();
        term.WriteLine("");

        // Beat 1: Emergence from the dungeon
        term.WriteLine(Loc.Get("dungeon.emerge_steps"), "white");
        await Task.Delay(1500);
        term.WriteLine(Loc.Get("dungeon.emerge_sunlight"), "bright_yellow");
        await Task.Delay(1500);
        term.WriteLine("");

        term.WriteLine(Loc.Get("dungeon.word_spread"), "gray");
        await Task.Delay(1200);

        string outcomeWord = result.Outcome switch
        {
            BossOutcome.Defeated => Loc.Get("dungeon.outcome_slain"),
            BossOutcome.Saved => Loc.Get("dungeon.outcome_saved"),
            BossOutcome.Allied => Loc.Get("dungeon.outcome_allied"),
            BossOutcome.Spared => Loc.Get("dungeon.outcome_mercy"),
            _ => Loc.Get("dungeon.outcome_faced")
        };
        term.WriteLine(Loc.Get("dungeon.they_know", outcomeWord, godData.Name), "gray");
        await Task.Delay(2000);
        term.WriteLine("");

        // Beat 2: Crowd reactions
        var reactions = GetTownReactionLines(result.God, result.Outcome, result.ApproachType, godData);
        foreach (var (line, color) in reactions)
        {
            term.WriteLine($"  {line}", color);
            await Task.Delay(1800);
        }

        term.WriteLine("");
        await Task.Delay(1000);

        // Beat 3: Closing reflection
        string closing = result.Outcome switch
        {
            BossOutcome.Defeated => Loc.Get("dungeon.closing_defeated"),
            BossOutcome.Saved => Loc.Get("dungeon.closing_saved"),
            BossOutcome.Allied => Loc.Get("dungeon.closing_allied"),
            BossOutcome.Spared => Loc.Get("dungeon.closing_spared"),
            _ => Loc.Get("dungeon.closing_default")
        };
        term.WriteLine($"  {closing}", "white");
        await Task.Delay(2000);

        // Next god breadcrumb — hint at what lies deeper (v0.49.3)
        var nextGod = GetNextUnencounteredGod(result.God);
        if (nextGod != null)
        {
            var nextGodData = OldGodsData.GetGodBossData(nextGod.Value);
            term.WriteLine("");
            await Task.Delay(1500);
            term.SetColor("dark_cyan");
            term.WriteLine(Loc.Get("dungeon.breadcrumb_old_woman"));
            await Task.Delay(1500);
            term.SetColor("gray");
            term.WriteLine(Loc.Get("dungeon.breadcrumb_next_god", nextGodData.Name));
            await Task.Delay(2000);
        }

        term.WriteLine("");
        await term.PressAnyKey();
    }

    /// <summary>
    /// Get the next Old God in floor order that hasn't been encountered yet.
    /// Returns null if all gods have been dealt with (or only Manwe remains).
    /// </summary>
    private static OldGodType? GetNextUnencounteredGod(OldGodType justEncountered)
    {
        // Gods in floor order (excluding Manwe who has his own ending sequence)
        OldGodType[] godOrder = {
            OldGodType.Maelketh,  // Floor 25
            OldGodType.Veloura,   // Floor 40
            OldGodType.Thorgrim,  // Floor 55
            OldGodType.Noctura,   // Floor 70
            OldGodType.Aurelion,  // Floor 85
            OldGodType.Terravok   // Floor 95
        };

        var story = StoryProgressionSystem.Instance;
        bool passedCurrent = false;

        foreach (var god in godOrder)
        {
            if (god == justEncountered)
            {
                passedCurrent = true;
                continue;
            }
            if (!passedCurrent) continue;

            // Check if this god hasn't been encountered yet
            if (story.OldGodStates.TryGetValue(god, out var state))
            {
                if (state.Status == GodStatus.Unknown)
                    return god;
            }
            else
            {
                return god; // No state entry means never encountered
            }
        }

        return null;
    }

    /// <summary>
    /// Get crowd reaction lines based on god, outcome, and dialogue approach.
    /// Returns tuples of (text, color).
    /// </summary>
    private List<(string text, string color)> GetTownReactionLines(
        OldGodType god, BossOutcome outcome, string approach, OldGodBossData godData)
    {
        var lines = new List<(string, string)>();

        switch (god)
        {
            case OldGodType.Maelketh:
                if (outcome == BossOutcome.Defeated)
                {
                    if (approach == "aggressive" || approach == "enraged")
                    {
                        lines.Add((Loc.Get("dungeon.react_maelketh_def_agg_1"), "gray"));
                        lines.Add((Loc.Get("dungeon.react_maelketh_def_agg_2"), godData.ThemeColor));
                        lines.Add((Loc.Get("dungeon.react_maelketh_def_agg_3"), "gray"));
                    }
                    else if (approach == "humble" || approach == "teaching")
                    {
                        lines.Add((Loc.Get("dungeon.react_maelketh_def_hum_1"), "gray"));
                        lines.Add((Loc.Get("dungeon.react_maelketh_def_hum_2"), "white"));
                        lines.Add((Loc.Get("dungeon.react_maelketh_def_hum_3"), "bright_yellow"));
                    }
                    else
                    {
                        lines.Add((Loc.Get("dungeon.react_maelketh_def_def_1"), "gray"));
                        lines.Add((Loc.Get("dungeon.react_maelketh_def_def_2"), "white"));
                    }
                }
                else if (outcome == BossOutcome.Saved)
                {
                    lines.Add((Loc.Get("dungeon.react_maelketh_sav_1"), "gray"));
                    lines.Add((Loc.Get("dungeon.react_maelketh_sav_2"), "bright_green"));
                    lines.Add((Loc.Get("dungeon.react_maelketh_sav_3"), "bright_cyan"));
                }
                else // Allied/Spared
                {
                    lines.Add((Loc.Get("dungeon.react_maelketh_other_1"), "gray"));
                    lines.Add((Loc.Get("dungeon.react_maelketh_other_2"), "white"));
                }
                break;

            case OldGodType.Veloura:
                if (outcome == BossOutcome.Defeated)
                {
                    if (approach == "aggressive")
                    {
                        lines.Add((Loc.Get("dungeon.react_veloura_def_agg_1"), "gray"));
                        lines.Add((Loc.Get("dungeon.react_veloura_def_agg_2"), godData.ThemeColor));
                        lines.Add((Loc.Get("dungeon.react_veloura_def_agg_3"), "gray"));
                    }
                    else if (approach == "merciful" || approach == "diplomatic" || approach == "reluctant")
                    {
                        lines.Add((Loc.Get("dungeon.react_veloura_def_merc_1"), "gray"));
                        lines.Add((Loc.Get("dungeon.react_veloura_def_merc_2"), "white"));
                    }
                    else
                    {
                        lines.Add((Loc.Get("dungeon.react_veloura_def_def_1"), "gray"));
                        lines.Add((Loc.Get("dungeon.react_veloura_def_def_2"), "white"));
                    }
                }
                else if (outcome == BossOutcome.Saved)
                {
                    lines.Add((Loc.Get("dungeon.react_veloura_sav_1"), "bright_yellow"));
                    lines.Add((Loc.Get("dungeon.react_veloura_sav_2"), "bright_magenta"));
                    lines.Add((Loc.Get("dungeon.react_veloura_sav_3"), "bright_cyan"));
                }
                else
                {
                    lines.Add((Loc.Get("dungeon.react_veloura_other_1"), "gray"));
                    lines.Add((Loc.Get("dungeon.react_veloura_other_2"), "white"));
                }
                break;

            case OldGodType.Thorgrim:
                if (outcome == BossOutcome.Defeated)
                {
                    if (approach == "defiant")
                    {
                        lines.Add((Loc.Get("dungeon.react_thorgrim_def_defy_1"), "gray"));
                        lines.Add((Loc.Get("dungeon.react_thorgrim_def_defy_2"), godData.ThemeColor));
                        lines.Add((Loc.Get("dungeon.react_thorgrim_def_defy_3"), "white"));
                    }
                    else if (approach == "honorable" || approach == "cunning")
                    {
                        lines.Add((Loc.Get("dungeon.react_thorgrim_def_hon_1"), "gray"));
                        lines.Add((Loc.Get("dungeon.react_thorgrim_def_hon_2"), "bright_yellow"));
                    }
                    else
                    {
                        lines.Add((Loc.Get("dungeon.react_thorgrim_def_def_1"), "gray"));
                        lines.Add((Loc.Get("dungeon.react_thorgrim_def_def_2"), "white"));
                    }
                }
                else if (outcome == BossOutcome.Saved)
                {
                    lines.Add((Loc.Get("dungeon.react_thorgrim_sav_1"), "gray"));
                    lines.Add((Loc.Get("dungeon.react_thorgrim_sav_2"), "bright_cyan"));
                    lines.Add((Loc.Get("dungeon.react_thorgrim_sav_3"), "bright_green"));
                }
                else
                {
                    lines.Add((Loc.Get("dungeon.react_thorgrim_other_1"), "gray"));
                    lines.Add((Loc.Get("dungeon.react_thorgrim_other_2"), "white"));
                }
                break;

            case OldGodType.Noctura:
                if (outcome == BossOutcome.Allied)
                {
                    lines.Add((Loc.Get("dungeon.react_noctura_ally_1"), "gray"));
                    lines.Add((Loc.Get("dungeon.react_noctura_ally_2"), "dark_gray"));
                    lines.Add((Loc.Get("dungeon.react_noctura_ally_3"), godData.ThemeColor));
                }
                else if (outcome == BossOutcome.Defeated)
                {
                    if (approach == "aggressive")
                    {
                        lines.Add((Loc.Get("dungeon.react_noctura_def_agg_1"), "bright_yellow"));
                        lines.Add((Loc.Get("dungeon.react_noctura_def_agg_2"), "gray"));
                        lines.Add((Loc.Get("dungeon.react_noctura_def_agg_3"), "dark_gray"));
                    }
                    else
                    {
                        lines.Add((Loc.Get("dungeon.react_noctura_def_def_1"), "gray"));
                        lines.Add((Loc.Get("dungeon.react_noctura_def_def_2"), "white"));
                    }
                }
                else if (outcome == BossOutcome.Saved)
                {
                    lines.Add((Loc.Get("dungeon.react_noctura_sav_1"), "gray"));
                    lines.Add((Loc.Get("dungeon.react_noctura_sav_2"), "white"));
                    lines.Add((Loc.Get("dungeon.react_noctura_sav_3"), godData.ThemeColor));
                }
                else
                {
                    lines.Add((Loc.Get("dungeon.react_noctura_other_1"), "gray"));
                    lines.Add((Loc.Get("dungeon.react_noctura_other_2"), "white"));
                }
                break;

            case OldGodType.Aurelion:
                if (outcome == BossOutcome.Defeated)
                {
                    if (approach == "compassionate" || approach == "righteous")
                    {
                        lines.Add((Loc.Get("dungeon.react_aurelion_def_comp_1"), "gray"));
                        lines.Add((Loc.Get("dungeon.react_aurelion_def_comp_2"), "bright_yellow"));
                        lines.Add((Loc.Get("dungeon.react_aurelion_def_comp_3"), "white"));
                    }
                    else
                    {
                        lines.Add((Loc.Get("dungeon.react_aurelion_def_def_1"), "gray"));
                        lines.Add((Loc.Get("dungeon.react_aurelion_def_def_2"), godData.ThemeColor));
                        lines.Add((Loc.Get("dungeon.react_aurelion_def_def_3"), "dark_gray"));
                    }
                }
                else if (outcome == BossOutcome.Saved)
                {
                    lines.Add((Loc.Get("dungeon.react_aurelion_sav_1"), "bright_yellow"));
                    lines.Add((Loc.Get("dungeon.react_aurelion_sav_2"), "white"));
                    lines.Add((Loc.Get("dungeon.react_aurelion_sav_3"), "bright_yellow"));
                    lines.Add((Loc.Get("dungeon.react_aurelion_sav_4"), "bright_cyan"));
                }
                else
                {
                    lines.Add((Loc.Get("dungeon.react_aurelion_other_1"), "bright_yellow"));
                    lines.Add((Loc.Get("dungeon.react_aurelion_other_2"), "gray"));
                }
                break;

            case OldGodType.Terravok:
                if (outcome == BossOutcome.Defeated)
                {
                    if (approach == "respectful")
                    {
                        lines.Add((Loc.Get("dungeon.react_terravok_def_resp_1"), "gray"));
                        lines.Add((Loc.Get("dungeon.react_terravok_def_resp_2"), "white"));
                    }
                    else
                    {
                        lines.Add((Loc.Get("dungeon.react_terravok_def_def_1"), "gray"));
                        lines.Add((Loc.Get("dungeon.react_terravok_def_def_2"), godData.ThemeColor));
                        lines.Add((Loc.Get("dungeon.react_terravok_def_def_3"), "white"));
                    }
                }
                else if (outcome == BossOutcome.Saved)
                {
                    lines.Add((Loc.Get("dungeon.react_terravok_sav_1"), "gray"));
                    lines.Add((Loc.Get("dungeon.react_terravok_sav_2"), "bright_green"));
                    lines.Add((Loc.Get("dungeon.react_terravok_sav_3"), "bright_cyan"));
                }
                else
                {
                    lines.Add((Loc.Get("dungeon.react_terravok_other_1"), "gray"));
                    lines.Add((Loc.Get("dungeon.react_terravok_other_2"), "white"));
                }
                break;

            default:
                lines.Add((Loc.Get("dungeon.react_default_1"), "gray"));
                lines.Add((Loc.Get("dungeon.react_default_2"), "white"));
                break;
        }

        return lines;
    }

    /// <summary>
    /// Get the artifact dropped by a specific Old God
    /// </summary>
    private ArtifactType? GetArtifactForGod(OldGodType god)
    {
        return god switch
        {
            OldGodType.Maelketh => ArtifactType.CreatorsEye,
            OldGodType.Veloura => ArtifactType.SoulweaversLoom,
            OldGodType.Thorgrim => ArtifactType.ScalesOfLaw,
            OldGodType.Noctura => ArtifactType.ShadowCrown,
            OldGodType.Aurelion => ArtifactType.SunforgedBlade,
            OldGodType.Terravok => ArtifactType.Worldstone,
            OldGodType.Manwe => null, // Manwe is the creator, no artifact
            _ => null
        };
    }

    protected override string GetMudPromptName() => $"Dungeon Fl.{currentDungeonLevel}";

    protected override string[]? GetAmbientMessages() => new[]
    {
        Loc.Get("dungeon.ambient_drips"),
        Loc.Get("dungeon.ambient_torch"),
        Loc.Get("dungeon.ambient_skitters"),
        Loc.Get("dungeon.ambient_draft"),
        Loc.Get("dungeon.ambient_groan"),
        Loc.Get("dungeon.ambient_clang"),
        Loc.Get("dungeon.ambient_shadows"),
    };

    protected override void DisplayLocation()
    {
        terminal.ClearScreen();

        // Get current room
        var room = currentFloor?.GetCurrentRoom();

        if (room != null && inRoomMode)
        {
            DisplayRoomView(room);
        }
        else
        {
            DisplayFloorOverview();
        }
    }

    /// <summary>
    /// Display when player is in a specific room - the main exploration view
    /// </summary>
    private void DisplayRoomView(DungeonRoom room)
    {
        if (IsBBSSession) { DisplayRoomViewBBS(room); return; }

        var player = GetCurrentPlayer();

        // Room header with theme color
        terminal.SetColor(GetThemeColor(currentFloor.Theme));
        if (GameConfig.ScreenReaderMode)
        {
            terminal.WriteLine(room.Name);
        }
        else
        {
            terminal.WriteLine($"╔{new string('═', 55)}╗");
            terminal.WriteLine($"║  {room.Name.PadRight(52)} ║");
            terminal.WriteLine($"╚{new string('═', 55)}╝");
        }

        // Show danger indicators
        ShowDangerIndicators(room);

        terminal.WriteLine("");

        // Blood Moon atmosphere (v0.52.0)
        if (currentPlayer != null && currentPlayer.IsBloodMoon)
        {
            terminal.SetColor("red");
            terminal.WriteLine($"  {Loc.Get("dungeon.blood_moon_atmosphere")}");
            terminal.WriteLine("");
        }

        // Room description
        terminal.SetColor("white");
        terminal.WriteLine(room.Description);
        terminal.WriteLine("");

        // Atmospheric text (builds tension)
        terminal.SetColor("gray");
        terminal.WriteLine(room.AtmosphereText);
        terminal.WriteLine("");

        // Mystery breadcrumbs — early floors hint at something deeper (v0.49.6)
        if (currentDungeonLevel <= 5 && Random.Shared.Next(100) < 20)
        {
            var breadcrumbs = new[]
            {
                Loc.Get("dungeon.breadcrumb_1"),
                Loc.Get("dungeon.breadcrumb_2"),
                Loc.Get("dungeon.breadcrumb_3"),
                Loc.Get("dungeon.breadcrumb_4"),
                Loc.Get("dungeon.breadcrumb_5"),
                Loc.Get("dungeon.breadcrumb_6"),
                Loc.Get("dungeon.breadcrumb_7"),
                Loc.Get("dungeon.breadcrumb_8"),
            };
            terminal.SetColor("dark_magenta");
            terminal.WriteLine($"  {breadcrumbs[Random.Shared.Next(breadcrumbs.Length)]}");
            terminal.SetColor("white");
            terminal.WriteLine("");
        }

        // Show what's in the room
        ShowRoomContents(room);

        // Show exits
        ShowExits(room);

        // Show room actions
        ShowRoomActions(room);

        // Quick status bar
        ShowQuickStatus(player);

        // Show level eligibility notification
        ShowLevelEligibilityMessage();
    }

    /// <summary>
    /// BBS compact room view - fits within 25 rows on an 80x25 terminal
    /// </summary>
    private void DisplayRoomViewBBS(DungeonRoom room)
    {
        var player = GetCurrentPlayer();

        // Line 1: Header with floor + room name
        string dangerIndicator = GameConfig.ScreenReaderMode
            ? Loc.Get("dungeon.danger_of", room.DangerRating, 3)
            : new string('*', room.DangerRating) + new string('.', 3 - room.DangerRating);
        string roomStatus = room.IsCleared ? Loc.Get("dungeon.tag_cleared") : room.HasMonsters ? Loc.Get("dungeon.tag_danger") : "";
        string bossTag = room.IsBossRoom ? " " + Loc.Get("dungeon.tag_boss") : "";
        ShowBBSHeader($"{Loc.Get("dungeon.floor", currentDungeonLevel)} - {room.Name} {dangerIndicator}{bossTag} {roomStatus}");

        // Line 2: Theme
        terminal.SetColor(GetThemeColor(currentFloor.Theme));
        terminal.Write($" {GetThemeShortName(currentFloor.Theme)}");
        terminal.SetColor("gray");
        terminal.WriteLine($" | {room.Description}");

        // Blood Moon atmosphere (v0.52.0)
        if (player != null && player.IsBloodMoon)
        {
            terminal.SetColor("red");
            terminal.WriteLine($"  {Loc.Get("dungeon.blood_moon_atmosphere_bbs")}");
        }

        // Lines 3-6: Room contents (compact, 1 line each)
        if (room.HasMonsters && !room.IsCleared)
        {
            terminal.SetColor("red");
            terminal.WriteLine(room.IsBossRoom
                ? Loc.Get("dungeon.bbs_boss_presence")
                : Loc.Get("dungeon.bbs_hostile_creatures"));
        }
        if (room.HasTreasure && !room.TreasureLooted)
        {
            terminal.SetColor("yellow");
            terminal.WriteLine(Loc.Get("dungeon.bbs_treasure_glints"));
        }
        if (room.HasEvent && !room.EventCompleted)
        {
            terminal.SetColor("cyan");
            terminal.WriteLine($" >> {GetEventHint(room.EventType).TrimStart('>', ' ').TrimEnd('<', '>', ' ')} <<");
        }
        if (room.HasStairsDown)
        {
            terminal.SetColor("blue");
            terminal.WriteLine(Loc.Get("dungeon.bbs_stairs_down"));
        }

        // Features (1 line)
        var uninteractedFeatures = room.Features.Where(f => !f.IsInteracted).ToList();
        if (uninteractedFeatures.Any())
        {
            terminal.SetColor("cyan");
            terminal.Write(Loc.Get("dungeon.bbs_notice"));
            terminal.SetColor("white");
            terminal.WriteLine(string.Join(", ", uninteractedFeatures.Select(f => f.Name)));
        }

        // Exits (1 line)
        terminal.SetColor("white");
        terminal.Write(Loc.Get("dungeon.bbs_exits"));
        foreach (var exit in room.Exits)
        {
            var targetRoom = currentFloor.GetRoom(exit.Value.TargetRoomId);
            var dirKey = GetDirectionKey(exit.Key);
            string status;
            if (targetRoom == null)
                status = "";
            else if (targetRoom.IsCleared)
                status = GameConfig.ScreenReaderMode && IsDirectionFullyCleared(exit.Value.TargetRoomId, room.Id) ? "(all clear)" : "(clr)";
            else if (targetRoom.IsExplored)
                status = "(exp)";
            else
                status = "(?)";
            terminal.SetColor("darkgray");
            terminal.Write(" [");
            terminal.SetColor("bright_cyan");
            terminal.Write(dirKey);
            terminal.SetColor("darkgray");
            terminal.Write("]");
            terminal.SetColor("gray");
            terminal.Write(status);
        }
        terminal.WriteLine("");

        // Party info (1 line)
        if (teammates.Count > 0)
        {
            terminal.SetColor("cyan");
            terminal.Write(Loc.Get("dungeon.bbs_party_count", 1 + teammates.Count));
            terminal.SetColor("white");
            terminal.WriteLine(string.Join(", ", teammates.Select(t => t.Name2)));
        }

        terminal.WriteLine("");

        // Actions - context-sensitive rows
        var row1 = new List<(string key, string color, string label)>();
        if (room.HasMonsters && !room.IsCleared)
            row1.Add(("F", "bright_yellow", Loc.Get("dungeon.bbs_fight")));
        if (room.HasTreasure && !room.TreasureLooted && (room.IsCleared || !room.HasMonsters))
            row1.Add(("T", "bright_yellow", Loc.Get("dungeon.bbs_treasure")));
        if (room.HasEvent && !room.EventCompleted)
            row1.Add(("V", "bright_yellow", Loc.Get("dungeon.bbs_investigate")));
        if (uninteractedFeatures.Any())
            row1.Add(("X", "bright_yellow", Loc.Get("dungeon.bbs_examine")));
        if (room.HasStairsDown && (room.IsCleared || !room.HasMonsters))
            row1.Add(("D", "bright_yellow", Loc.Get("dungeon.bbs_descend")));
        if ((room.IsCleared || !room.HasMonsters) && !hasCampedThisFloor)
            row1.Add(("R", "bright_yellow", Loc.Get("dungeon.bbs_camp")));

        if (row1.Count > 0)
            ShowBBSMenuRow(row1.ToArray());

        ShowBBSMenuRow(
            ("M", "bright_yellow", Loc.Get("dungeon.bbs_map_label")),
            ("G", "bright_yellow", Loc.Get("dungeon.bbs_guide_label")),
            ("I", "bright_yellow", Loc.Get("dungeon.bbs_inv")),
            ("P", "bright_yellow", Loc.Get("dungeon.bbs_potions_label")),
            ("=", "bright_yellow", Loc.Get("dungeon.bbs_status_label")),
            ("Q", "bright_yellow", Loc.Get("dungeon.bbs_leave"))
        );

        // Status line
        terminal.SetColor("darkgray");
        terminal.Write($" {Loc.Get("combat.bar_hp")}:");
        float hpPct = player.MaxHP > 0 ? (float)player.HP / player.MaxHP : 0;
        terminal.SetColor(hpPct > 0.5f ? "bright_green" : hpPct > 0.25f ? "yellow" : "bright_red");
        terminal.Write($"{player.HP}/{player.MaxHP}");
        terminal.SetColor("gray");
        terminal.Write($" {Loc.Get("dungeon.bbs_potions")}:");
        terminal.SetColor("green");
        terminal.Write($"{player.Healing}/{player.MaxPotions}");
        terminal.SetColor("gray");
        terminal.Write($" {Loc.Get("dungeon.bbs_gold")}:");
        terminal.SetColor("yellow");
        terminal.Write($"{player.Gold:N0}");
        if (player.MaxMana > 0)
        {
            terminal.SetColor("gray");
            terminal.Write($" {Loc.Get("dungeon.bbs_mana")}:");
            terminal.SetColor("blue");
            terminal.Write($"{player.Mana}/{player.MaxMana}");
        }
        terminal.SetColor("gray");
        terminal.Write($" {Loc.Get("dungeon.bbs_lv")}:");
        terminal.SetColor("cyan");
        terminal.WriteLine($"{player.Level}");

        // Training points reminder only shown on Main Street (where Level Master is accessible)
    }

    /// <summary>
    /// BBS compact floor overview - fits within 25 rows on an 80x25 terminal
    /// </summary>
    private void DisplayFloorOverviewBBS()
    {
        var player = GetCurrentPlayer();

        // Line 1: Header
        ShowBBSHeader($"{Loc.Get("dungeon.floor", currentDungeonLevel).ToUpper()} - {GetThemeShortName(currentFloor.Theme)}");

        // Line 2: Floor stats
        int explored = currentFloor.Rooms.Count(r => r.IsExplored);
        int cleared = currentFloor.Rooms.Count(r => r.IsCleared);
        terminal.SetColor("white");
        terminal.Write($" {Loc.Get("dungeon.rooms")}:{currentFloor.Rooms.Count}");
        terminal.SetColor("gray");
        terminal.Write($" {Loc.Get("dungeon.explored_count")}:");
        terminal.SetColor(explored == currentFloor.Rooms.Count ? "bright_green" : "yellow");
        terminal.Write($"{explored}/{currentFloor.Rooms.Count}");
        terminal.SetColor("gray");
        terminal.Write($" {Loc.Get("dungeon.cleared_count")}:");
        terminal.SetColor(cleared == currentFloor.Rooms.Count ? "bright_green" : "yellow");
        terminal.Write($"{cleared}/{currentFloor.Rooms.Count}");
        terminal.SetColor("gray");
        terminal.Write($" {Loc.Get("dungeon.danger_level")}:");
        terminal.SetColor(currentFloor.DangerLevel >= 7 ? "red" : currentFloor.DangerLevel >= 4 ? "yellow" : "green");
        terminal.WriteLine($"{currentFloor.DangerLevel}/10");

        // Line 3: Party
        terminal.SetColor("cyan");
        terminal.Write(Loc.Get("dungeon.your_party", 1 + teammates.Count));
        if (teammates.Count > 0)
        {
            terminal.SetColor("white");
            terminal.Write($" ({string.Join(", ", teammates.Select(t => t.Name2))})");
        }
        terminal.WriteLine("");

        // Line 4: Seal hint (if applicable)
        if (currentFloor.HasUncollectedSeal && !currentFloor.SealCollected)
        {
            terminal.SetColor("bright_yellow");
            terminal.WriteLine(Loc.Get("dungeon.seal_hint"));
        }

        // Line 5: Floor guidance hint (reuse the existing logic in compact form)
        ShowFloorGuidanceBBS(currentDungeonLevel);

        // Line 6: Level eligibility
        if (player != null && player.Level < GameConfig.MaxLevel)
        {
            long experienceNeeded = GetExperienceForLevel(player.Level + 1);
            if (player.Experience >= experienceNeeded)
            {
                terminal.SetColor("bright_green");
                terminal.WriteLine(Loc.Get("dungeon.level_raise"));
            }
        }

        terminal.WriteLine("");

        // Menu rows
        ShowBBSMenuRow(
            ("E", "bright_yellow", Loc.Get("dungeon.bbs_enter")),
            ("J", "bright_yellow", Loc.Get("dungeon.bbs_journal")),
            ("T", "bright_yellow", Loc.Get("dungeon.bbs_party")),
            ("S", "bright_yellow", Loc.Get("dungeon.bbs_status_label"))
        );
        ShowBBSMenuRow(
            ("L", "bright_yellow", Loc.Get("dungeon.bbs_level_change")),
            ("R", "bright_yellow", Loc.Get("dungeon.bbs_return"))
        );

        // Footer status
        ShowBBSFooter();
    }

    /// <summary>
    /// Compact floor guidance for BBS mode (1 line max)
    /// </summary>
    private void ShowFloorGuidanceBBS(int floor)
    {
        var story = StoryProgressionSystem.Instance;
        string? hint = null;
        string color = "gray";

        // Seal and boss floors
        if (floor == 15 && !story.CollectedSeals.Contains(SealType.FirstWar))
        { hint = Loc.Get("dungeon.guide_seal_2"); color = "bright_cyan"; }
        else if (floor == 25 && !story.HasStoryFlag("maelketh_encountered"))
        { hint = Loc.Get("dungeon.guide_maelketh"); color = "bright_red"; }
        else if (floor == 30 && !story.CollectedSeals.Contains(SealType.Corruption))
        { hint = Loc.Get("dungeon.guide_seal_3"); color = "bright_cyan"; }
        else if (floor == 40 && !story.HasStoryFlag("veloura_encountered"))
        { hint = Loc.Get("dungeon.guide_veloura"); color = "bright_magenta"; }
        else if (floor == 45 && !story.CollectedSeals.Contains(SealType.Imprisonment))
        { hint = Loc.Get("dungeon.guide_seal_4"); color = "bright_cyan"; }
        else if (floor == 55 && !story.HasStoryFlag("thorgrim_encountered"))
        { hint = Loc.Get("dungeon.guide_thorgrim"); color = "bright_cyan"; }
        else if (floor == 60 && !story.CollectedSeals.Contains(SealType.Prophecy))
        { hint = Loc.Get("dungeon.guide_seal_5"); color = "bright_cyan"; }
        else if (floor == 70 && !story.HasStoryFlag("noctura_encountered"))
        { hint = Loc.Get("dungeon.guide_noctura"); color = "bright_magenta"; }
        else if (floor == 80 && !story.CollectedSeals.Contains(SealType.Regret))
        { hint = Loc.Get("dungeon.guide_seal_6"); color = "bright_cyan"; }
        else if (floor == 85 && !story.HasStoryFlag("aurelion_encountered"))
        { hint = Loc.Get("dungeon.guide_aurelion"); color = "bright_yellow"; }
        else if (floor == 95 && !story.HasStoryFlag("terravok_encountered"))
        { hint = Loc.Get("dungeon.guide_terravok"); color = "bright_yellow"; }
        else if (floor == 99 && !story.CollectedSeals.Contains(SealType.Truth))
        { hint = Loc.Get("dungeon.guide_seal_final"); color = "bright_cyan"; }
        else if (floor == 100)
        { hint = Loc.Get("dungeon.guide_manwe"); color = "bright_white"; }

        if (hint != null)
        {
            terminal.SetColor(color);
            terminal.WriteLine($" {hint}");
        }
    }

    private void ShowDangerIndicators(DungeonRoom room)
    {
        terminal.SetColor("darkgray");
        terminal.Write($"{Loc.Get("dungeon.bbs_level")} {currentDungeonLevel} | ");

        // Show floor theme
        terminal.SetColor(GetThemeColor(currentFloor.Theme));
        terminal.Write($"{GetThemeShortName(currentFloor.Theme)} | ");

        // Danger rating
        terminal.SetColor(room.DangerRating >= 3 ? "red" : room.DangerRating >= 2 ? "yellow" : "green");
        if (GameConfig.ScreenReaderMode)
        {
            terminal.Write(Loc.Get("dungeon.danger_of", room.DangerRating, 3));
        }
        else
        {
            terminal.Write($"{Loc.Get("dungeon.bbs_danger")}: ");
            for (int i = 0; i < room.DangerRating; i++) terminal.Write("*");
            for (int i = room.DangerRating; i < 3; i++) terminal.Write(".");
        }

        // Room status
        if (room.IsCleared)
        {
            terminal.SetColor("green");
            terminal.Write($" {Loc.Get("dungeon.tag_cleared")}");
        }
        else if (room.HasMonsters)
        {
            terminal.SetColor("red");
            terminal.Write($" {Loc.Get("dungeon.tag_danger")}");
        }

        if (room.IsBossRoom)
        {
            terminal.SetColor("bright_red");
            terminal.Write($" {Loc.Get("dungeon.tag_boss")}");
        }

        terminal.WriteLine("");
    }

    private void ShowRoomContents(DungeonRoom room)
    {
        bool hasAnything = false;

        // Monsters present (not yet cleared)
        if (room.HasMonsters && !room.IsCleared)
        {
            terminal.SetColor("red");
            if (room.IsBossRoom)
            {
                terminal.WriteLine(Loc.Get("dungeon.room_boss_presence"));
            }
            else
            {
                var monsterHints = new[]
                {
                    Loc.Get("dungeon.monster_hint_1"),
                    Loc.Get("dungeon.monster_hint_2"),
                    Loc.Get("dungeon.monster_hint_3"),
                    Loc.Get("dungeon.monster_hint_4")
                };
                terminal.WriteLine(monsterHints[dungeonRandom.Next(monsterHints.Length)]);
            }
            hasAnything = true;
        }

        // Treasure
        if (room.HasTreasure && !room.TreasureLooted)
        {
            terminal.SetColor("yellow");
            terminal.WriteLine(Loc.Get("dungeon.room_treasure_glints"));
            hasAnything = true;
        }

        // Trap detection hint — class-specific detection chance
        if (room.HasTrap && !room.TrapTriggered)
        {
            double detectChance = currentPlayer.Class switch
            {
                CharacterClass.Assassin => 0.65,  // Trained to spot traps
                CharacterClass.Ranger => 0.50,    // Wilderness instincts
                CharacterClass.Jester => 0.40,    // Street-smart awareness
                CharacterClass.Bard => 0.35,      // Perceptive
                _ => 0.25                          // General awareness
            };
            if (dungeonRandom.NextDouble() < detectChance)
            {
                terminal.SetColor("magenta");
                if (currentPlayer.Class == CharacterClass.Assassin)
                    terminal.WriteLine(Loc.Get("dungeon.trap_detect_assassin"));
                else if (currentPlayer.Class == CharacterClass.Ranger)
                    terminal.WriteLine(Loc.Get("dungeon.trap_detect_ranger"));
                else
                    terminal.WriteLine(Loc.Get("dungeon.trap_detect_general"));
                hasAnything = true;
            }
        }

        // Event
        if (room.HasEvent && !room.EventCompleted)
        {
            terminal.SetColor("cyan");
            terminal.WriteLine(GetEventHint(room.EventType));
            hasAnything = true;
        }

        // Stairs
        if (room.HasStairsDown)
        {
            terminal.SetColor("blue");
            terminal.WriteLine(Loc.Get("dungeon.room_stairs_down"));
            hasAnything = true;
        }

        // Features to examine
        if (room.Features.Any(f => !f.IsInteracted))
        {
            terminal.SetColor("cyan");
            terminal.WriteLine("");
            terminal.WriteLine(Loc.Get("dungeon.you_notice"));
            foreach (var feature in room.Features.Where(f => !f.IsInteracted))
            {
                terminal.Write("  - ");
                terminal.SetColor("white");
                terminal.WriteLine(feature.Name);
                terminal.SetColor("cyan");
            }
            hasAnything = true;
        }

        if (hasAnything)
            terminal.WriteLine("");
    }

    private string GetEventHint(DungeonEventType eventType)
    {
        return eventType switch
        {
            DungeonEventType.TreasureChest => Loc.Get("dungeon.event_hint_chest"),
            DungeonEventType.Merchant => Loc.Get("dungeon.event_hint_merchant"),
            DungeonEventType.Shrine => Loc.Get("dungeon.event_hint_shrine"),
            DungeonEventType.NPCEncounter => Loc.Get("dungeon.event_hint_npc"),
            DungeonEventType.Puzzle => Loc.Get("dungeon.event_hint_puzzle"),
            DungeonEventType.RestSpot => Loc.Get("dungeon.event_hint_rest"),
            DungeonEventType.MysteryEvent => Loc.Get("dungeon.event_hint_mystery"),
            DungeonEventType.Settlement => Loc.Get("dungeon.event_hint_settlement"),
            _ => Loc.Get("dungeon.event_hint_default")
        };
    }

    private void ShowExits(DungeonRoom room)
    {
        terminal.SetColor("white");
        terminal.WriteLine(Loc.Get("dungeon.exits"));

        foreach (var exit in room.Exits)
        {
            var targetRoom = currentFloor.GetRoom(exit.Value.TargetRoomId);
            var dirKey = GetDirectionKey(exit.Key);

            if (IsScreenReader)
            {
                string status = "";
                if (targetRoom != null)
                {
                    if (targetRoom.IsCleared)
                        status = IsDirectionFullyCleared(exit.Value.TargetRoomId, room.Id) ? $" ({Loc.Get("dungeon.fully_cleared")})" : $" ({Loc.Get("dungeon.cleared")})";
                    else if (targetRoom.IsExplored)
                        status = $" ({Loc.Get("dungeon.explored")})";
                    else
                        status = $" ({Loc.Get("dungeon.unknown")})";
                }
                terminal.WriteLine($"{dirKey}. {exit.Value.Description}{status}");
            }
            else
            {
                terminal.SetColor("darkgray");
                terminal.Write("  [");
                terminal.SetColor("bright_cyan");
                terminal.Write(dirKey);
                terminal.SetColor("darkgray");
                terminal.Write("] ");

                terminal.SetColor("gray");
                terminal.Write(exit.Value.Description);

                // Show target room status
                if (targetRoom != null)
                {
                    if (targetRoom.IsCleared)
                    {
                        terminal.SetColor("green");
                        if (GameConfig.ScreenReaderMode && IsDirectionFullyCleared(exit.Value.TargetRoomId, room.Id))
                            terminal.Write($" ({Loc.Get("dungeon.fully_cleared")})");
                        else
                            terminal.Write($" ({Loc.Get("dungeon.cleared")})");
                    }
                    else if (targetRoom.IsExplored)
                    {
                        terminal.SetColor("yellow");
                        terminal.Write($" ({Loc.Get("dungeon.explored")})");
                    }
                    else
                    {
                        terminal.SetColor("darkgray");
                        terminal.Write($" ({Loc.Get("dungeon.unknown")})");
                    }
                }

                terminal.WriteLine("");
            }
        }
        terminal.WriteLine("");
    }

    private void ShowRoomActions(DungeonRoom room)
    {
        terminal.SetColor("white");
        terminal.WriteLine(Loc.Get("dungeon.actions"));

        if (IsScreenReader)
        {
            // Screen reader: plain text menu
            if (room.HasMonsters && !room.IsCleared)
                WriteSRMenuOption("F", Loc.Get("dungeon.fight"));
            if (room.HasTreasure && !room.TreasureLooted && (room.IsCleared || !room.HasMonsters))
                WriteSRMenuOption("T", Loc.Get("dungeon.collect_treasure"));
            if (room.HasEvent && !room.EventCompleted)
                WriteSRMenuOption("V", Loc.Get("dungeon.investigate"));
            if (room.Features.Any(f => !f.IsInteracted))
                WriteSRMenuOption("X", Loc.Get("dungeon.examine"));
            if (room.HasStairsDown && (room.IsCleared || !room.HasMonsters))
                WriteSRMenuOption("D", Loc.Get("dungeon.descend_stairs"));
            if ((room.IsCleared || !room.HasMonsters) && !hasCampedThisFloor)
                WriteSRMenuOption("R", Loc.Get("dungeon.make_camp"));
            WriteSRMenuOption("M", Loc.Get("dungeon.map"));
            WriteSRMenuOption("G", Loc.Get("dungeon.guide"));
            WriteSRMenuOption("I", Loc.Get("dungeon.inventory"));
            WriteSRMenuOption("P", Loc.Get("dungeon.potions"));
            if (currentPlayer.TotalHerbCount > 0)
                WriteSRMenuOption("J", Loc.Get("dungeon.herbs", currentPlayer.TotalHerbCount.ToString()));
            if (teammates.Count > 0)
                WriteSRMenuOption("Y", "Party");
            WriteSRMenuOption("=", Loc.Get("dungeon.status"));
            WriteSRMenuOption("Q", Loc.Get("dungeon.leave_dungeon"));
            terminal.WriteLine("");
            return;
        }

        // Fight monsters
        if (room.HasMonsters && !room.IsCleared)
        {
            terminal.SetColor("darkgray");
            terminal.Write("  [");
            terminal.SetColor("bright_yellow");
            terminal.Write("F");
            terminal.SetColor("darkgray");
            terminal.Write("] ");
            terminal.SetColor("white");
            terminal.WriteLine(Loc.Get("dungeon.fight"));
        }

        // Search for treasure (available if room is cleared OR has no monsters)
        if (room.HasTreasure && !room.TreasureLooted && (room.IsCleared || !room.HasMonsters))
        {
            terminal.SetColor("darkgray");
            terminal.Write("  [");
            terminal.SetColor("bright_yellow");
            terminal.Write("T");
            terminal.SetColor("darkgray");
            terminal.Write("] ");
            terminal.SetColor("white");
            terminal.WriteLine(Loc.Get("dungeon.collect_treasure"));
        }

        // Interact with event
        if (room.HasEvent && !room.EventCompleted)
        {
            terminal.SetColor("darkgray");
            terminal.Write("  [");
            terminal.SetColor("bright_yellow");
            terminal.Write("V");
            terminal.SetColor("darkgray");
            terminal.Write("] ");
            terminal.SetColor("white");
            terminal.WriteLine(Loc.Get("dungeon.investigate"));
        }

        // Examine features
        if (room.Features.Any(f => !f.IsInteracted))
        {
            terminal.SetColor("darkgray");
            terminal.Write("  [");
            terminal.SetColor("bright_yellow");
            terminal.Write("X");
            terminal.SetColor("darkgray");
            terminal.Write("] ");
            terminal.SetColor("white");
            terminal.WriteLine(Loc.Get("dungeon.examine"));
        }

        // Use stairs (available if room is cleared OR has no monsters)
        if (room.HasStairsDown && (room.IsCleared || !room.HasMonsters))
        {
            terminal.SetColor("darkgray");
            terminal.Write("  [");
            terminal.SetColor("bright_yellow");
            terminal.Write("D");
            terminal.SetColor("darkgray");
            terminal.Write("] ");
            terminal.SetColor("white");
            terminal.WriteLine(Loc.Get("dungeon.descend_stairs"));
        }

        // Camp (if safe - room cleared or no monsters)
        if ((room.IsCleared || !room.HasMonsters) && !hasCampedThisFloor)
        {
            terminal.SetColor("darkgray");
            terminal.Write("  [");
            terminal.SetColor("bright_yellow");
            terminal.Write("R");
            terminal.SetColor("darkgray");
            terminal.Write("] ");
            terminal.SetColor("white");
            terminal.WriteLine(Loc.Get("dungeon.make_camp"));
        }

        // General options
        terminal.SetColor("darkgray");
        terminal.Write("  [");
        terminal.SetColor("bright_yellow");
        terminal.Write("M");
        terminal.SetColor("darkgray");
        terminal.Write("] ");
        terminal.SetColor("white");
        terminal.Write($"{Loc.Get("dungeon.map")}  ");

        terminal.SetColor("darkgray");
        terminal.Write("[");
        terminal.SetColor("bright_yellow");
        terminal.Write("G");
        terminal.SetColor("darkgray");
        terminal.Write("] ");
        terminal.SetColor("white");
        terminal.Write($"{Loc.Get("dungeon.guide")}  ");

        terminal.SetColor("darkgray");
        terminal.Write("[");
        terminal.SetColor("bright_yellow");
        terminal.Write("I");
        terminal.SetColor("darkgray");
        terminal.Write("] ");
        terminal.SetColor("white");
        terminal.Write($"{Loc.Get("dungeon.inventory")}  ");

        terminal.SetColor("darkgray");
        terminal.Write("[");
        terminal.SetColor("bright_yellow");
        terminal.Write("P");
        terminal.SetColor("darkgray");
        terminal.Write("] ");
        terminal.SetColor("white");
        terminal.Write($"{Loc.Get("dungeon.potions")}  ");

        if (currentPlayer.TotalHerbCount > 0)
        {
            terminal.SetColor("darkgray");
            terminal.Write("[");
            terminal.SetColor("bright_yellow");
            terminal.Write("J");
            terminal.SetColor("darkgray");
            terminal.Write("] ");
            terminal.SetColor("bright_green");
            terminal.Write($"{Loc.Get("dungeon.herbs", currentPlayer.TotalHerbCount.ToString())}  ");
        }

        if (teammates.Count > 0)
        {
            terminal.SetColor("darkgray");
            terminal.Write("[");
            terminal.SetColor("bright_yellow");
            terminal.Write("Y");
            terminal.SetColor("darkgray");
            terminal.Write("] ");
            terminal.SetColor("white");
            terminal.Write("Party  ");
        }

        terminal.SetColor("darkgray");
        terminal.Write("[");
        terminal.SetColor("bright_yellow");
        terminal.Write("=");
        terminal.SetColor("darkgray");
        terminal.Write("] ");
        terminal.SetColor("white");
        terminal.Write($"{Loc.Get("dungeon.status")}  ");

        terminal.SetColor("darkgray");
        terminal.Write("[");
        terminal.SetColor("bright_yellow");
        terminal.Write("Q");
        terminal.SetColor("darkgray");
        terminal.Write("] ");
        terminal.SetColor("white");
        terminal.WriteLine(Loc.Get("dungeon.leave_dungeon"));

        terminal.WriteLine("");
    }

    private void ShowQuickStatus(Character player)
    {
        if (!GameConfig.ScreenReaderMode)
        {
            terminal.SetColor("darkgray");
            terminal.Write(new string('─', 57));
            terminal.WriteLine("");
        }

        // Health bar
        terminal.SetColor("white");
        terminal.Write($"{Loc.Get("status.hp")}: ");
        DrawBar(player.HP, player.MaxHP, 20, "red", "darkgray");
        terminal.Write($" {player.HP}/{player.MaxHP}");

        terminal.Write("  ");

        // Potions
        terminal.SetColor("green");
        terminal.Write($"{Loc.Get("status.potions")}: {player.Healing}/{player.MaxPotions}");

        terminal.Write("  ");

        // Gold
        terminal.SetColor("yellow");
        terminal.Write($"{Loc.Get("status.gold_label")}: {player.Gold:N0}");

        // Fatigue indicator (only when Tired or Exhausted)
        var (fatigueLabel, fatigueColor) = player.GetFatigueTier();
        if (!string.IsNullOrEmpty(fatigueLabel) && fatigueLabel != "Well-Rested")
        {
            terminal.Write("  ");
            terminal.SetColor(fatigueColor);
            terminal.Write(fatigueLabel);
        }

        terminal.WriteLine("");
    }

    private void DrawBar(long current, long max, int width, string fillColor, string emptyColor)
    {
        if (GameConfig.ScreenReaderMode) return;

        int filled = max > 0 ? (int)((current * width) / max) : 0;
        filled = Math.Max(0, Math.Min(width, filled));

        terminal.Write("[");
        terminal.SetColor(fillColor);
        terminal.Write(new string('█', filled));
        terminal.SetColor(emptyColor);
        terminal.Write(new string('░', width - filled));
        terminal.SetColor("white");
        terminal.Write("]");
    }

    private string GetDirectionKey(Direction dir)
    {
        return dir switch
        {
            Direction.North => "N",
            Direction.South => "S",
            Direction.East => "E",
            Direction.West => "W",
            _ => "?"
        };
    }

    private static string GetThemeShortName(DungeonTheme theme)
    {
        return theme switch
        {
            DungeonTheme.Catacombs => Loc.Get("dungeon.theme_short.catacombs"),
            DungeonTheme.Sewers => Loc.Get("dungeon.theme_short.sewers"),
            DungeonTheme.Caverns => Loc.Get("dungeon.theme_short.caverns"),
            DungeonTheme.AncientRuins => Loc.Get("dungeon.theme_short.ancient_ruins"),
            DungeonTheme.DemonLair => Loc.Get("dungeon.theme_short.demon_lair"),
            DungeonTheme.FrozenDepths => Loc.Get("dungeon.theme_short.frozen_depths"),
            DungeonTheme.VolcanicPit => Loc.Get("dungeon.theme_short.volcanic_pit"),
            DungeonTheme.AbyssalVoid => Loc.Get("dungeon.theme_short.abyssal_void"),
            _ => theme.ToString()
        };
    }

    private string GetThemeDescription(DungeonTheme theme)
    {
        return theme switch
        {
            DungeonTheme.Catacombs => Loc.Get("dungeon.theme_catacombs"),
            DungeonTheme.Sewers => Loc.Get("dungeon.theme_sewers"),
            DungeonTheme.Caverns => Loc.Get("dungeon.theme_caverns"),
            DungeonTheme.AncientRuins => Loc.Get("dungeon.theme_ruins"),
            DungeonTheme.DemonLair => Loc.Get("dungeon.theme_demon"),
            DungeonTheme.FrozenDepths => Loc.Get("dungeon.theme_frozen"),
            DungeonTheme.VolcanicPit => Loc.Get("dungeon.theme_volcanic"),
            DungeonTheme.AbyssalVoid => Loc.Get("dungeon.theme_abyssal"),
            _ => Loc.Get("dungeon.theme_unknown")
        };
    }

    private string GetThemeColor(DungeonTheme theme)
    {
        return theme switch
        {
            DungeonTheme.Catacombs => "gray",
            DungeonTheme.Sewers => "green",
            DungeonTheme.Caverns => "cyan",
            DungeonTheme.AncientRuins => "yellow",
            DungeonTheme.DemonLair => "red",
            DungeonTheme.FrozenDepths => "bright_cyan",
            DungeonTheme.VolcanicPit => "bright_red",
            DungeonTheme.AbyssalVoid => "magenta",
            _ => "white"
        };
    }

    private static string GetAnsiThemeColor(DungeonTheme theme) => theme switch
    {
        DungeonTheme.Catacombs => "\u001b[37m",
        DungeonTheme.Sewers => "\u001b[32m",
        DungeonTheme.Caverns => "\u001b[36m",
        DungeonTheme.AncientRuins => "\u001b[33m",
        DungeonTheme.DemonLair => "\u001b[31m",
        DungeonTheme.FrozenDepths => "\u001b[96m",
        DungeonTheme.VolcanicPit => "\u001b[91m",
        DungeonTheme.AbyssalVoid => "\u001b[35m",
        _ => "\u001b[37m"
    };

    /// <summary>
    /// Display floor overview before entering
    /// </summary>
    private void DisplayFloorOverview()
    {
        if (IsBBSSession) { DisplayFloorOverviewBBS(); return; }

        if (IsScreenReader) { DisplayFloorOverviewSR(); return; }

        ShowBreadcrumb();

        // Header - standardized format
        WriteBoxHeader($"{Loc.Get("dungeon.dungeon_level", currentDungeonLevel)}", "bright_cyan", 77);
        terminal.WriteLine("");
        terminal.SetColor(GetThemeColor(currentFloor.Theme));
        terminal.WriteLine($"{Loc.Get("dungeon.theme")}: {GetThemeShortName(currentFloor.Theme)}");
        terminal.WriteLine("");

        // Floor stats
        terminal.SetColor("white");
        terminal.WriteLine($"{Loc.Get("dungeon.rooms")}: {currentFloor.Rooms.Count}");
        terminal.WriteLine($"{Loc.Get("dungeon.danger_level")}: {currentFloor.DangerLevel}/10");

        int explored = currentFloor.Rooms.Count(r => r.IsExplored);
        int cleared = currentFloor.Rooms.Count(r => r.IsCleared);
        terminal.WriteLine($"{Loc.Get("dungeon.explored_count")}: {explored}/{currentFloor.Rooms.Count}");
        terminal.WriteLine($"{Loc.Get("dungeon.cleared_count")}: {cleared}/{currentFloor.Rooms.Count}");
        terminal.WriteLine("");

        // Floor flavor
        terminal.SetColor("gray");
        terminal.WriteLine(GetFloorFlavorText(currentFloor.Theme));
        terminal.WriteLine("");

        // Team info
        terminal.SetColor("cyan");
        terminal.WriteLine(Loc.Get("dungeon.your_party", 1 + teammates.Count));
        terminal.WriteLine("");

        // Show seal hint if this floor has an uncollected seal
        if (currentFloor.HasUncollectedSeal && !currentFloor.SealCollected)
        {
            terminal.SetColor("bright_yellow");
            terminal.WriteLine(Loc.Get("dungeon.seal_hint"));
            terminal.WriteLine("");
        }

        // Show level eligibility notification
        ShowLevelEligibilityMessage();

        // Show floor-specific guidance
        ShowFloorGuidance(currentDungeonLevel);

        // Options - standardized format
        terminal.SetColor("cyan");
        terminal.WriteLine(Loc.Get("dungeon.actions"));
        terminal.WriteLine("");

        terminal.SetColor("darkgray");
        terminal.Write(" [");
        terminal.SetColor("bright_yellow");
        terminal.Write("E");
        terminal.SetColor("darkgray");
        terminal.Write("]");
        terminal.SetColor("white");
        terminal.Write(Loc.Get("dungeon.enter_dungeon_label"));

        terminal.SetColor("darkgray");
        terminal.Write("[");
        terminal.SetColor("bright_yellow");
        terminal.Write("J");
        terminal.SetColor("darkgray");
        terminal.Write("]");
        terminal.SetColor("white");
        terminal.WriteLine(Loc.Get("dungeon.journal_label"));

        terminal.SetColor("darkgray");
        terminal.Write(" [");
        terminal.SetColor("bright_yellow");
        terminal.Write("T");
        terminal.SetColor("darkgray");
        terminal.Write("]");
        terminal.SetColor("white");
        terminal.Write(Loc.Get("dungeon.party_mgmt_label"));

        terminal.SetColor("darkgray");
        terminal.Write("[");
        terminal.SetColor("bright_yellow");
        terminal.Write("S");
        terminal.SetColor("darkgray");
        terminal.Write("]");
        terminal.SetColor("white");
        terminal.WriteLine(Loc.Get("dungeon.status_label"));

        terminal.SetColor("darkgray");
        terminal.Write(" [");
        terminal.SetColor("bright_yellow");
        terminal.Write("L");
        terminal.SetColor("darkgray");
        terminal.Write("]");
        terminal.SetColor("white");
        terminal.Write(Loc.Get("dungeon.level_change_label"));

        terminal.SetColor("darkgray");
        terminal.Write("[");
        terminal.SetColor("bright_yellow");
        terminal.Write("R");
        terminal.SetColor("darkgray");
        terminal.Write("]");
        terminal.SetColor("white");
        terminal.WriteLine(Loc.Get("dungeon.return_town_label"));
        terminal.WriteLine("");
    }

    /// <summary>
    /// Screen reader friendly floor overview
    /// </summary>
    private void DisplayFloorOverviewSR()
    {
        terminal.WriteLine(Loc.Get("dungeon.dungeon_level", currentDungeonLevel), "bright_cyan");
        terminal.WriteLine("");

        terminal.WriteLine($"{Loc.Get("dungeon.theme")}: {currentFloor.Theme}", GetThemeColor(currentFloor.Theme));
        terminal.WriteLine("");

        int explored = currentFloor.Rooms.Count(r => r.IsExplored);
        int cleared = currentFloor.Rooms.Count(r => r.IsCleared);
        terminal.SetColor("white");
        terminal.WriteLine($"{Loc.Get("dungeon.rooms")}: {currentFloor.Rooms.Count}, {Loc.Get("dungeon.explored_count")}: {explored}, {Loc.Get("dungeon.cleared_count")}: {cleared}");
        terminal.WriteLine($"{Loc.Get("dungeon.danger_level")}: {currentFloor.DangerLevel}/10");
        terminal.WriteLine("");

        terminal.WriteLine(GetFloorFlavorText(currentFloor.Theme), "gray");
        terminal.WriteLine("");

        terminal.WriteLine(Loc.Get("dungeon.your_party", 1 + teammates.Count), "cyan");
        terminal.WriteLine("");

        if (currentFloor.HasUncollectedSeal && !currentFloor.SealCollected)
        {
            terminal.WriteLine(Loc.Get("dungeon.seal_hint"), "bright_yellow");
            terminal.WriteLine("");
        }

        ShowLevelEligibilityMessage();
        ShowFloorGuidance(currentDungeonLevel);

        terminal.WriteLine(Loc.Get("dungeon.actions"), "cyan");
        terminal.WriteLine($"  E. {Loc.Get("dungeon.enter_dungeon")}", "white");
        terminal.WriteLine($"  J. {Loc.Get("dungeon.journal")}", "white");
        terminal.WriteLine($"  T. {Loc.Get("dungeon.party_mgmt")}", "white");
        terminal.WriteLine($"  S. {Loc.Get("menu.action.status")}", "white");
        terminal.WriteLine($"  L. {Loc.Get("dungeon.level_change")}", "white");
        terminal.WriteLine($"  R. {Loc.Get("dungeon.return_town")}", "white");
        terminal.WriteLine("");
    }

    private string GetFloorFlavorText(DungeonTheme theme)
    {
        return theme switch
        {
            DungeonTheme.Catacombs => Loc.Get("dungeon.flavor_catacombs"),
            DungeonTheme.Sewers => Loc.Get("dungeon.flavor_sewers"),
            DungeonTheme.Caverns => Loc.Get("dungeon.flavor_caverns"),
            DungeonTheme.AncientRuins => Loc.Get("dungeon.flavor_ruins"),
            DungeonTheme.DemonLair => Loc.Get("dungeon.flavor_demon"),
            DungeonTheme.FrozenDepths => Loc.Get("dungeon.flavor_frozen"),
            DungeonTheme.VolcanicPit => Loc.Get("dungeon.flavor_volcanic"),
            DungeonTheme.AbyssalVoid => Loc.Get("dungeon.flavor_abyssal"),
            _ => Loc.Get("dungeon.flavor_default")
        };
    }

    /// <summary>
    /// Show floor-specific guidance to help players understand what's coming
    /// </summary>
    private void ShowFloorGuidance(int floor)
    {
        var story = StoryProgressionSystem.Instance;
        string? hint = null;
        string color = "gray";

        // Special floor hints - Seal floors match SevenSealsSystem.cs
        // Seal 1 (Creation) = Temple (floor 0), Seal 2 (FirstWar) = 15, Seal 3 (Corruption) = 30
        // Seal 4 (Imprisonment) = 45, Seal 5 (Prophecy) = 60, Seal 6 (Regret) = 80, Seal 7 (Truth) = 99
        if (floor == 15 && !story.CollectedSeals.Contains(SealType.FirstWar))
        {
            hint = Loc.Get("dungeon.hint_seal_2");
            color = "bright_cyan";
        }
        else if (floor == 25)
        {
            // Only show paradox hint if it's actually available
            var player = GetCurrentPlayer();
            if (player != null && MoralParadoxSystem.Instance.IsParadoxAvailable("possessed_child", player))
            {
                hint = Loc.Get("dungeon.hint_paradox_child");
                color = "bright_magenta";
            }
            else if (!story.HasStoryFlag("maelketh_encountered"))
            {
                hint = Loc.Get("dungeon.hint_maelketh");
                color = "bright_red";
            }
        }
        else if (floor == 30 && !story.CollectedSeals.Contains(SealType.Corruption))
        {
            hint = Loc.Get("dungeon.hint_seal_3");
            color = "bright_cyan";
        }
        else if (floor == 40 && !story.HasStoryFlag("veloura_encountered"))
        {
            hint = Loc.Get("dungeon.hint_veloura");
            color = "bright_magenta";
        }
        else if (floor == 45 && !story.CollectedSeals.Contains(SealType.Imprisonment))
        {
            hint = Loc.Get("dungeon.hint_seal_4");
            color = "bright_cyan";
        }
        else if (floor == 55 && !story.HasStoryFlag("thorgrim_encountered"))
        {
            hint = Loc.Get("dungeon.hint_thorgrim");
            color = "bright_cyan";
        }
        else if (floor == 60)
        {
            if (!story.CollectedSeals.Contains(SealType.Prophecy))
            {
                hint = Loc.Get("dungeon.hint_seal_5");
                color = "bright_cyan";
            }
        }
        else if (floor == 65)
        {
            var player65 = GetCurrentPlayer();
            if (player65 != null)
            {
                // Check if the Soulweaver's Loom can be found here (save quest active, Loom not yet collected)
                if (StoryProgressionSystem.Instance.HasStoryFlag("veloura_save_quest") &&
                    !ArtifactSystem.Instance.HasArtifact(ArtifactType.SoulweaversLoom))
                {
                    hint = Loc.Get("dungeon.hint_loom_discovery");
                    color = "bright_magenta";
                }
                else if (MoralParadoxSystem.Instance.IsParadoxAvailable("velouras_cure", player65))
                {
                    hint = Loc.Get("dungeon.hint_soulweaver_price");
                    color = "bright_magenta";
                }
            }
        }
        else if (floor == 70 && !story.HasStoryFlag("noctura_encountered"))
        {
            hint = Loc.Get("dungeon.hint_noctura");
            color = "bright_magenta";
        }
        else if (floor == 75)
        {
            hint = Loc.Get("dungeon.hint_memory");
            color = "bright_blue";
        }
        else if (floor == 80 && !story.CollectedSeals.Contains(SealType.Regret))
        {
            hint = Loc.Get("dungeon.hint_seal_6");
            color = "bright_cyan";
        }
        else if (floor == 85 && !story.HasStoryFlag("aurelion_encountered"))
        {
            hint = Loc.Get("dungeon.hint_aurelion");
            color = "bright_yellow";
        }
        else if (floor == 90)
        {
            var player90 = GetCurrentPlayer();
            if (player90 != null &&
                StoryProgressionSystem.Instance.HasStoryFlag("aurelion_save_quest") &&
                !ArtifactSystem.Instance.HasArtifact(ArtifactType.SunforgedBlade))
            {
                hint = "You sense something blazing with ancient light nearby...";
                color = "bright_yellow";
            }
        }
        else if (floor == 95)
        {
            var player95 = GetCurrentPlayer();
            if (player95 != null && MoralParadoxSystem.Instance.IsParadoxAvailable("destroy_darkness", player95))
            {
                hint = Loc.Get("dungeon.hint_destroy_darkness");
                color = "bright_magenta";
            }
            else if (!story.HasStoryFlag("terravok_encountered"))
            {
                hint = Loc.Get("dungeon.hint_terravok");
                color = "bright_yellow";
            }
        }
        else if (floor == 99 && !story.CollectedSeals.Contains(SealType.Truth))
        {
            hint = Loc.Get("dungeon.hint_seal_final");
            color = "bright_cyan";
        }
        else if (floor == 100)
        {
            hint = Loc.Get("dungeon.hint_manwe");
            color = "bright_white";
        }
        else if (floor >= 50 && floor < 60)
        {
            hint = Loc.Get("dungeon.hint_something_stirs");
            color = "yellow";
        }
        else if (floor >= 70 && floor < 80)
        {
            hint = Loc.Get("dungeon.hint_ancient_power");
            color = "yellow";
        }

        if (hint != null)
        {
            terminal.SetColor(color);
            terminal.WriteLine(hint);
            terminal.WriteLine("");
        }
    }

    /// <summary>
    /// Shows a message if the player is eligible for a level raise
    /// </summary>
    private void ShowLevelEligibilityMessage()
    {
        if (currentPlayer == null || currentPlayer.Level >= GameConfig.MaxLevel)
            return;

        long experienceNeeded = GetExperienceForLevel(currentPlayer.Level + 1);

        if (currentPlayer.Experience >= experienceNeeded)
        {
            terminal.WriteLine("");
            WriteBoxHeader(Loc.Get("dungeon.level_raise"), "bright_yellow");
            terminal.WriteLine("");
        }
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

    protected override string GetBreadcrumbPath()
    {
        var room = currentFloor?.GetCurrentRoom();
        if (room != null && inRoomMode)
        {
            return Loc.Get("dungeon.breadcrumb_room", currentDungeonLevel, room.Name);
        }
        return Loc.Get("dungeon.breadcrumb_floor", currentDungeonLevel);
    }

    /// <summary>
    /// Override to save floor state before leaving dungeon
    /// </summary>
    protected override async Task NavigateToLocation(GameLocation destination)
    {
        // Save current floor state before leaving
        var player = GetCurrentPlayer();
        if (player != null)
        {
            SaveFloorState(player);
        }

        await base.NavigateToLocation(destination);
    }

    protected override async Task<bool> ProcessChoice(string choice)
    {
        // Handle global quick commands first
        var (handled, shouldExit) = await TryProcessGlobalCommand(choice);
        if (handled) return shouldExit;

        if (string.IsNullOrWhiteSpace(choice))
            return false;

        var upperChoice = choice.ToUpper().Trim();

        // Different handling based on whether we're in room mode or floor overview
        if (inRoomMode)
        {
            return await ProcessRoomChoice(upperChoice);
        }
        else
        {
            return await ProcessOverviewChoice(upperChoice);
        }
    }

    /// <summary>
    /// Process input when viewing floor overview
    /// </summary>
    private async Task<bool> ProcessOverviewChoice(string choice)
    {
        switch (choice)
        {
            case "E":
                // Enter the dungeon - go to first room
                inRoomMode = true;
                currentFloor.CurrentRoomId = currentFloor.EntranceRoomId;
                var entranceRoom = currentFloor.GetCurrentRoom();
                if (entranceRoom != null)
                {
                    entranceRoom.IsExplored = true;
                    roomsExploredThisFloor++;
                    // Auto-clear rooms without monsters
                    if (!entranceRoom.HasMonsters)
                    {
                        entranceRoom.IsCleared = true;
                    }
                }
                terminal.WriteLine(Loc.Get("dungeon.entering_dungeon"), "gray");
                await Task.Delay(1500);

                // Rare encounter check on dungeon entry
                var player = GetCurrentPlayer();
                if (player != null)
                {
                    await RareEncounters.TryRareEncounter(
                        terminal,
                        player,
                        currentFloor.Theme,
                        currentDungeonLevel
                    );
                }
                RequestRedisplay();
                return false;

            case "J":
                await ShowStoryJournal();
                return false;

            case "T":
                await ManageTeam();
                return false;

            case "S":
            case "=":
                await ShowStatus();
                return false;

            case "L":
                await ChangeDungeonLevel();
                return false;

            case "R":
            case "Q":
                // Players can always leave to town - they may need to gear up, get companions, etc.
                // Floor locking only prevents ascending to PREVIOUS floors within the dungeon
                // Confirm exit to prevent accidental dungeon departure (e.g. pressing Q through submenus)
                terminal.SetColor("yellow");
                terminal.Write(Loc.Get("dungeon.confirm_leave"));
                terminal.SetColor("white");
                string exitConfirm = (await terminal.GetInput("")).Trim().ToUpper();
                if (exitConfirm == "Y" || exitConfirm == "YES")
                {
                    await NavigateToLocation(GameLocation.MainStreet);
                    return true;
                }
                return false;

            default:
                terminal.WriteLine(Loc.Get("dungeon.invalid_choice"), "red");
                await Task.Delay(1000);
                return false;
        }
    }

    /// <summary>
    /// Show the Story Journal - helps players understand their current objectives
    /// </summary>
    private async Task ShowStoryJournal()
    {
        var player = GetCurrentPlayer();
        var story = StoryProgressionSystem.Instance;
        var seals = SevenSealsSystem.Instance;

        terminal.ClearScreen();
        WriteBoxHeader(Loc.Get("dungeon.story_journal"), "bright_cyan", 60);
        terminal.WriteLine("");

        // Current chapter
        terminal.SetColor("white");
        terminal.Write(Loc.Get("dungeon.current_chapter"));
        terminal.SetColor("yellow");
        terminal.WriteLine(GetChapterName(story.CurrentChapter));
        terminal.WriteLine("");

        // The main objective
        WriteSectionHeader(Loc.Get("dungeon.section_quest"), "bright_white");
        terminal.SetColor("white");
        terminal.WriteLine(GetCurrentObjective(story, player!));
        terminal.WriteLine("");

        // Seals progress
        WriteSectionHeader(Loc.Get("dungeon.seals_header", story.CollectedSeals.Count), "bright_yellow");
        terminal.SetColor("gray");

        foreach (var seal in seals.GetAllSeals())
        {
            if (story.CollectedSeals.Contains(seal.Type))
            {
                terminal.SetColor("green");
                if (IsScreenReader)
                    terminal.WriteLine(Loc.Get("dungeon.seal_collected_sr", seal.Name, seal.Title));
                else
                    terminal.WriteLine($"  [X] {seal.Name} - {seal.Title}");
            }
            else
            {
                terminal.SetColor("gray");
                string locationText = seal.DungeonFloor == 0 ? Loc.Get("dungeon.seal_hidden_town") : Loc.Get("dungeon.seal_hidden_dungeon");
                if (IsScreenReader)
                    terminal.WriteLine(Loc.Get("dungeon.seal_not_collected_sr", seal.Name, locationText));
                else
                    terminal.WriteLine($"  [ ] {seal.Name} - {locationText}");
                terminal.SetColor("dark_cyan");
                terminal.WriteLine($"      {seal.LocationHint}");
            }
        }
        terminal.WriteLine("");

        // What you know so far
        WriteSectionHeader(Loc.Get("dungeon.section_learned"), "bright_magenta");
        terminal.SetColor("white");
        ShowKnownLore(story);
        terminal.WriteLine("");

        // Ocean Philosophy / Awakening progress
        ShowOceanProgress();
        terminal.WriteLine("");

        // Old God status
        ShowOldGodStatus(story);
        terminal.WriteLine("");

        // Town NPC stories progress
        ShowTownStoriesProgress();
        terminal.WriteLine("");

        // Companion status
        ShowCompanionStatus();
        terminal.WriteLine("");

        // Next steps
        WriteSectionHeader(Loc.Get("dungeon.section_next_steps"), "bright_green");
        terminal.SetColor("white");
        ShowSuggestedSteps(story, player, seals);
        terminal.WriteLine("");

        terminal.SetColor("gray");
        await terminal.GetInputAsync(Loc.Get("ui.press_enter"));
    }

    private string GetChapterName(StoryChapter chapter)
    {
        return chapter switch
        {
            StoryChapter.Awakening => Loc.Get("dungeon.chapter_awakening"),
            StoryChapter.FirstBlood => Loc.Get("dungeon.chapter_first_blood"),
            StoryChapter.TheStranger => Loc.Get("dungeon.chapter_stranger"),
            StoryChapter.FactionChoice => Loc.Get("dungeon.chapter_faction"),
            StoryChapter.RisingPower => Loc.Get("dungeon.chapter_rising"),
            StoryChapter.TheWhispers => Loc.Get("dungeon.chapter_whispers"),
            StoryChapter.FirstGod => Loc.Get("dungeon.chapter_first_god"),
            StoryChapter.GodWar => Loc.Get("dungeon.chapter_god_war"),
            StoryChapter.TheChoice => Loc.Get("dungeon.chapter_choice"),
            StoryChapter.Ascension => Loc.Get("dungeon.chapter_ascension"),
            StoryChapter.FinalConfrontation => Loc.Get("dungeon.chapter_final"),
            StoryChapter.Epilogue => Loc.Get("dungeon.chapter_epilogue"),
            _ => Loc.Get("dungeon.chapter_unknown")
        };
    }

    private string GetCurrentObjective(StoryProgressionSystem story, Character player)
    {
        var level = player?.Level ?? 1;

        if (level < 10)
            return Loc.Get("dungeon.objective_explore");
        if (story.CollectedSeals.Count == 0)
            return Loc.Get("dungeon.objective_find_seals");
        if (story.CollectedSeals.Count < 4)
            return Loc.Get("dungeon.objective_more_seals");
        if (story.CollectedSeals.Count < 7)
            return Loc.Get("dungeon.objective_deeper");
        if (!story.HasStoryFlag("manwe_defeated"))
            return Loc.Get("dungeon.objective_face_manwe");
        return Loc.Get("dungeon.objective_ending");
    }

    private void ShowKnownLore(StoryProgressionSystem story)
    {
        var lorePoints = new List<string>();

        if (story.HasStoryFlag("knows_about_seals"))
            lorePoints.Add(Loc.Get("dungeon.lore_seals"));
        if (story.HasStoryFlag("heard_whispers"))
            lorePoints.Add(Loc.Get("dungeon.lore_wave"));
        if (story.HasStoryFlag("knows_prophecy"))
            lorePoints.Add(Loc.Get("dungeon.lore_prophecy"));
        if (story.CollectedSeals.Count >= 3)
            lorePoints.Add(Loc.Get("dungeon.lore_corruption"));
        if (story.CollectedSeals.Count >= 5)
            lorePoints.Add(Loc.Get("dungeon.lore_imprisonment"));
        if (story.CollectedSeals.Count >= 7)
            lorePoints.Add(Loc.Get("dungeon.lore_ocean"));
        if (story.HasStoryFlag("all_seals_collected"))
            lorePoints.Add(Loc.Get("dungeon.lore_all_seals"));

        if (lorePoints.Count == 0)
        {
            terminal.WriteLine(Loc.Get("dungeon.lore_none_1"));
            terminal.WriteLine(Loc.Get("dungeon.lore_none_2"));
        }
        else
        {
            foreach (var point in lorePoints)
            {
                terminal.WriteLine($"  {point}");
            }
        }
    }

    private void ShowSuggestedSteps(StoryProgressionSystem story, Character player, SevenSealsSystem seals)
    {
        var level = player?.Level ?? 1;
        var steps = new List<string>();

        // Suggest next seal to find
        var nextSeal = seals.GetAllSeals()
            .Where(s => !story.CollectedSeals.Contains(s.Type))
            .OrderBy(s => s.DungeonFloor)
            .FirstOrDefault();

        if (nextSeal != null)
        {
            if (nextSeal.DungeonFloor == 0)
            {
                steps.Add(Loc.Get("dungeon.step_seal_town", nextSeal.Name));
            }
            else
            {
                steps.Add(Loc.Get("dungeon.step_seal_dungeon", nextSeal.Name));
            }
        }

        // Level suggestions
        if (level < 15)
            steps.Add(Loc.Get("dungeon.step_grow"));
        else if (level < 50)
            steps.Add(Loc.Get("dungeon.step_level"));
        else if (level < 100)
            steps.Add(Loc.Get("dungeon.step_descend"));

        // Story suggestions
        if (!story.HasStoryFlag("met_stranger"))
            steps.Add(Loc.Get("dungeon.step_stranger"));
        if (story.CollectedSeals.Count < 7 && level >= 50)
            steps.Add(Loc.Get("dungeon.step_all_seals"));

        if (steps.Count == 0)
        {
            steps.Add(Loc.Get("dungeon.step_ready"));
        }

        foreach (var step in steps.Take(4))
        {
            terminal.WriteLine($"  {step}");
        }
    }

    private void ShowOceanProgress()
    {
        var ocean = OceanPhilosophySystem.Instance;

        WriteSectionHeader(Loc.Get("dungeon.ocean_header", ocean.AwakeningLevel), "bright_blue");

        // Show awakening level description
        terminal.SetColor("cyan");
        string awakeningDesc = ocean.AwakeningLevel switch
        {
            0 => Loc.Get("dungeon.awakening_0"),
            1 => Loc.Get("dungeon.awakening_1"),
            2 => Loc.Get("dungeon.awakening_2"),
            3 => Loc.Get("dungeon.awakening_3"),
            4 => Loc.Get("dungeon.awakening_4"),
            5 => Loc.Get("dungeon.awakening_5"),
            6 => Loc.Get("dungeon.awakening_6"),
            7 => Loc.Get("dungeon.awakening_7"),
            _ => Loc.Get("dungeon.awakening_unknown")
        };
        terminal.WriteLine($"  {awakeningDesc}");

        // Show wave fragments collected
        int fragmentCount = ocean.CollectedFragments.Count;
        int totalFragments = 10; // Total Wave Fragments
        terminal.SetColor("gray");
        terminal.WriteLine(Loc.Get("dungeon.wave_fragments", fragmentCount, totalFragments));

        if (fragmentCount > 0)
        {
            terminal.SetColor("dark_cyan");
            foreach (var fragment in ocean.CollectedFragments)
            {
                if (OceanPhilosophySystem.FragmentData.TryGetValue(fragment, out var data))
                {
                    terminal.WriteLine($"    - {data.Title}");
                }
            }
        }
    }

    private void ShowOldGodStatus(StoryProgressionSystem story)
    {
        WriteSectionHeader(Loc.Get("dungeon.section_old_gods"), "bright_red");

        // Define Old Gods and their floors
        var gods = new (OldGodType type, string name, int floor)[]
        {
            (OldGodType.Maelketh, Loc.Get("dungeon.god_name_maelketh"), 25),
            (OldGodType.Veloura, Loc.Get("dungeon.god_name_veloura"), 40),
            (OldGodType.Thorgrim, Loc.Get("dungeon.god_name_thorgrim"), 55),
            (OldGodType.Noctura, Loc.Get("dungeon.god_name_noctura"), 70),
            (OldGodType.Aurelion, Loc.Get("dungeon.god_name_aurelion"), 85),
            (OldGodType.Terravok, Loc.Get("dungeon.god_name_terravok"), 95),
            (OldGodType.Manwe, Loc.Get("dungeon.god_name_manwe"), 100)
        };

        foreach (var (godType, godName, floor) in gods)
        {
            if (story.OldGodStates.TryGetValue(godType, out var state) &&
                state.Status != GodStatus.Unknown)
            {
                string statusText = state.Status switch
                {
                    GodStatus.Imprisoned => Loc.Get("dungeon.god_status_imprisoned"),
                    GodStatus.Dormant => Loc.Get("dungeon.god_status_dormant"),
                    GodStatus.Dying => Loc.Get("dungeon.god_status_dying"),
                    GodStatus.Corrupted => Loc.Get("dungeon.god_status_corrupted"),
                    GodStatus.Neutral => Loc.Get("dungeon.god_status_neutral"),
                    GodStatus.Awakened => Loc.Get("dungeon.god_status_awakened"),
                    GodStatus.Hostile => Loc.Get("dungeon.god_status_hostile"),
                    GodStatus.Allied => Loc.Get("dungeon.god_status_allied"),
                    GodStatus.Saved => Loc.Get("dungeon.god_status_saved"),
                    GodStatus.Defeated => Loc.Get("dungeon.god_status_defeated"),
                    GodStatus.Consumed => Loc.Get("dungeon.god_status_consumed"),
                    _ => Loc.Get("dungeon.god_status_unknown")
                };

                string color = state.Status switch
                {
                    GodStatus.Allied or GodStatus.Saved => "green",
                    GodStatus.Defeated or GodStatus.Consumed => "red",
                    _ => "yellow"
                };

                terminal.SetColor(color);
                terminal.WriteLine(Loc.Get("dungeon.god_floor_status", floor, godName, statusText));
            }
            else
            {
                terminal.SetColor("gray");
                terminal.WriteLine(Loc.Get("dungeon.god_unknown_stirs"));
            }
        }
    }

    private void ShowTownStoriesProgress()
    {
        var storySystem = TownNPCStorySystem.Instance;

        WriteSectionHeader(Loc.Get("dungeon.section_stories"), "bright_yellow");

        // Define memorable NPCs and their story counts
        var npcInfo = new (string id, string name, int totalStages)[]
        {
            ("Pip", "Pip the Urchin", 6),
            ("Marcus", "Marcus the Guard", 5),
            ("Elena", "Elena the Barmaid", 5),
            ("Ezra", "Old Ezra the Sage", 5),
            ("Greta", "Greta the Healer", 6),
            ("Bartholomew", "Bartholomew the Scholar", 5)
        };

        bool anyProgress = false;
        foreach (var (id, name, totalStages) in npcInfo)
        {
            if (storySystem.NPCStates.TryGetValue(id, out var state))
            {
                int currentStage = state.CurrentStage;
                bool isComplete = state.IsCompleted;
                bool isFailed = currentStage == -1;

                if (currentStage > 0 || isComplete || isFailed)
                {
                    anyProgress = true;
                    if (isComplete)
                    {
                        terminal.SetColor("green");
                        if (IsScreenReader)
                            terminal.WriteLine(Loc.Get("dungeon.story_complete_sr", name));
                        else
                            terminal.WriteLine(Loc.Get("dungeon.story_complete", name));
                    }
                    else if (isFailed)
                    {
                        terminal.SetColor("red");
                        if (IsScreenReader)
                            terminal.WriteLine(Loc.Get("dungeon.story_failed_sr", name));
                        else
                            terminal.WriteLine(Loc.Get("dungeon.story_failed", name));
                    }
                    else
                    {
                        terminal.SetColor("yellow");
                        if (IsScreenReader)
                            terminal.WriteLine(Loc.Get("dungeon.story_in_progress_sr", name, currentStage, totalStages));
                        else
                            terminal.WriteLine(Loc.Get("dungeon.story_in_progress", name, currentStage, totalStages));
                    }
                }
            }
        }

        if (!anyProgress)
        {
            terminal.SetColor("gray");
            terminal.WriteLine(Loc.Get("dungeon.no_story_progress"));
            terminal.WriteLine(Loc.Get("dungeon.explore_town_hint"));
        }
    }

    private void ShowCompanionStatus()
    {
        var companions = CompanionSystem.Instance;

        WriteSectionHeader(Loc.Get("dungeon.section_companions_journal"), "bright_white");

        var recruited = companions.GetRecruitedCompanions().ToList();
        var active = companions.GetActiveCompanions().ToList();
        var fallen = companions.GetFallenCompanions().ToList();

        if (recruited.Count == 0 && fallen.Count == 0)
        {
            terminal.SetColor("gray");
            terminal.WriteLine(Loc.Get("dungeon.no_companions"));
            terminal.WriteLine(Loc.Get("dungeon.visit_inn_hint"));
            return;
        }

        // Show active companions
        if (active.Count > 0)
        {
            terminal.SetColor("bright_green");
            terminal.WriteLine(Loc.Get("dungeon.active_party"));
            foreach (var companion in active)
            {
                int hp = companions.GetCompanionHP(companion.Id);
                int maxHP = companions.GetCompanionMaxHP(companion);
                terminal.SetColor("green");
                terminal.WriteLine(Loc.Get("dungeon.companion_hp", companion.Name, hp, maxHP));
            }
        }

        // Show inactive recruited companions
        var inactive = companions.GetInactiveCompanions().ToList();
        if (inactive.Count > 0)
        {
            terminal.SetColor("yellow");
            terminal.WriteLine(Loc.Get("dungeon.at_the_inn"));
            foreach (var companion in inactive)
            {
                terminal.WriteLine($"    {companion.Name}");
            }
        }

        // Show fallen companions
        if (fallen.Count > 0)
        {
            terminal.SetColor("red");
            terminal.WriteLine(Loc.Get("dungeon.fallen_companions"));
            foreach (var (companion, death) in fallen)
            {
                terminal.WriteLine($"    {companion.Name} - {death.Circumstance}");
            }
        }
    }

    /// <summary>
    /// Process input when exploring a room
    /// </summary>
    private async Task<bool> ProcessRoomChoice(string choice)
    {
        var room = currentFloor.GetCurrentRoom();
        if (room == null) return false;

        // Check for directional movement
        var direction = choice switch
        {
            "N" => Direction.North,
            "S" => Direction.South,
            "E" => Direction.East,
            "W" => Direction.West,
            _ => (Direction?)null
        };

        if (direction.HasValue && room.Exits.ContainsKey(direction.Value))
        {
            await MoveToRoom(room.Exits[direction.Value].TargetRoomId);
            // Poison ticks on room movement (base loop poison is suppressed for dungeon)
            if (currentPlayer?.Poison > 0)
                await ApplyPoisonDamage();
            return false;
        }

        // Action-based commands
        switch (choice)
        {
            case "F":
                if (room.HasMonsters && !room.IsCleared)
                {
                    await FightRoomMonsters(room);
                }
                return false;

            case "T":
                if (room.HasTreasure && !room.TreasureLooted && (room.IsCleared || !room.HasMonsters))
                {
                    await CollectTreasure(room);
                }
                return false;

            case "V":
                if (room.HasEvent && !room.EventCompleted)
                {
                    await HandleRoomEvent(room);
                    RequestRedisplay();
                }
                return false;

            case "X":
                if (room.Features.Any(f => !f.IsInteracted))
                {
                    await ExamineFeatures(room);
                    RequestRedisplay();
                }
                return false;

            case "D":
                if (room.HasStairsDown && (room.IsCleared || !room.HasMonsters))
                {
                    await DescendStairs();
                }
                return false;

            case "R":
                if ((room.IsCleared || !room.HasMonsters) && !hasCampedThisFloor)
                {
                    await RestInRoom();
                    RequestRedisplay();
                }
                return false;

            case "M":
                await ShowDungeonMap();
                return false;

            case "G":
                await ShowDungeonNavigator();
                return false;

            case "I":
                await ShowInventory();
                return false;

            case "P":
                await UsePotions();
                RequestRedisplay();
                return false;

            case "J":
                await HomeLocation.UseHerbMenu(currentPlayer, terminal);
                RequestRedisplay();
                return false;

            case "Y":
                if (teammates.Count > 0)
                {
                    await ManagePartyInDungeon();
                    RequestRedisplay();
                }
                return false;

            case "=":
                await ShowStatus();
                return false;

            case "Q":
                // Leave dungeon
                inRoomMode = false;
                RequestRedisplay();
                return false;

            default:
                terminal.WriteLine(Loc.Get("dungeon.invalid_choice"), "red");
                await Task.Delay(1000);
                return false;
        }
    }

    /// <summary>
    /// Move to another room
    /// </summary>
    private async Task MoveToRoom(string targetRoomId)
    {
        var targetRoom = currentFloor.GetRoom(targetRoomId);
        if (targetRoom == null) return;

        // Moving transition
        // In screen reader mode, skip the ClearScreen here — the transition text flows
        // naturally in the buffer, and DisplayLocation's single ClearScreen (separator)
        // will cleanly introduce the room view. Double separators confuse screen readers.
        if (!GameConfig.ScreenReaderMode)
            terminal.ClearScreen();
        terminal.SetColor("gray");
        terminal.WriteLine(Loc.Get("dungeon.move_passage"));
        await Task.Delay(800);

        // Check for trap on entering unexplored room
        if (!targetRoom.IsExplored && targetRoom.HasTrap && !targetRoom.TrapTriggered)
        {
            await TriggerTrap(targetRoom);
        }

        // Check for backtrack into fully-cleared branch before updating current room
        string? previousRoomId = currentFloor.CurrentRoomId;

        // Update current room
        currentFloor.CurrentRoomId = targetRoomId;

        // Auto-clear rooms without monsters on entry (even if already explored from
        // knowledge events or floor respawn — IsCleared resets on respawn but IsExplored persists)
        if (!targetRoom.IsCleared && !targetRoom.HasMonsters)
        {
            targetRoom.IsCleared = true;
        }

        // Companion snarky comment when backtracking into a fully-cleared area
        if (targetRoom.IsExplored && targetRoom.IsCleared && previousRoomId != null
            && IsDirectionFullyCleared(targetRoomId, previousRoomId))
        {
            await TryCompanionBacktrackComment();
        }

        // Always push room view to followers (they need to see every room the leader enters)
        PushRoomToFollowers(targetRoom);

        if (!targetRoom.IsExplored)
        {
            targetRoom.IsExplored = true;
            roomsExploredThisFloor++;

            // Advance game time for room exploration (single-player)
            if (!UsurperRemake.BBS.DoorMode.IsOnlineMode)
            {
                var timePlayer = GetCurrentPlayer();
                if (timePlayer != null)
                {
                    DailySystemManager.Instance.AdvanceGameTime(timePlayer, GameConfig.MinutesPerDungeonRoom);
                    // Dungeon room fatigue scaled by armor weight
                    float dungeonArmorFatigueMult = timePlayer.GetArmorWeightTier() switch
                    {
                        ArmorWeightClass.Light => GameConfig.LightArmorFatigueMult,
                        ArmorWeightClass.Medium => GameConfig.MediumArmorFatigueMult,
                        _ => GameConfig.HeavyArmorFatigueMult
                    };
                    int dungeonFatigueCost = Math.Max(1, (int)(GameConfig.FatigueCostDungeonRoom * dungeonArmorFatigueMult));
                    timePlayer.Fatigue = Math.Min(100, timePlayer.Fatigue + dungeonFatigueCost);
                }
            }

            // Room discovery message
            terminal.SetColor(GetThemeColor(currentFloor.Theme));
            terminal.WriteLine(Loc.Get("dungeon.you_enter_room", targetRoom.Name));
            await Task.Delay(500);

            // Check for seal discovery on this floor
            var player = GetCurrentPlayer();
            if (player != null && await TryDiscoverSeal(player, targetRoom))
            {
                // Seal was found - give player time to process
                await Task.Delay(500);
            }

            // Auto-trigger riddles and puzzles when entering special rooms for the first time
            if (targetRoom.HasEvent && !targetRoom.EventCompleted)
            {
                // Riddle Gates and Puzzle Rooms require solving to proceed
                if (targetRoom.Type == RoomType.RiddleGate || targetRoom.Type == RoomType.PuzzleRoom)
                {
                    await HandleRoomEvent(targetRoom);
                }
            }

            // Rare encounter check on first visit to a room
            if (player != null)
            {
                bool hadEncounter = await RareEncounters.TryRareEncounter(
                    terminal,
                    player,
                    currentFloor.Theme,
                    currentDungeonLevel
                );

                if (hadEncounter)
                {
                    // Give a brief pause after rare encounter before showing room
                    await Task.Delay(500);
                }

                // Check for dungeon visions (narrative environmental beats)
                var vision = DreamSystem.Instance.GetDungeonVision(currentDungeonLevel, player);
                if (vision != null)
                {
                    await DisplayDungeonVision(vision);
                }

                // Check for companion personal quest encounters
                await CheckCompanionQuestEncounters(player, targetRoom);
            }
            else
            {
                // Already-explored room: still announce the room name so the player
                // knows where they are (especially important for screen reader users)
                terminal.SetColor("gray");
                terminal.WriteLine(Loc.Get("dungeon.you_return_room", targetRoom.Name));
            }
        }

        // Check for Old God save quest return visit (god is Awakened, player has artifact)
        // This triggers on room entry so the player doesn't need to "fight" a cleared boss room
        if (targetRoom.IsBossRoom)
        {
            var player2 = GetCurrentPlayer();
            if (player2 != null)
            {
                OldGodType? returnGodType = currentDungeonLevel switch
                {
                    25 => OldGodType.Maelketh,
                    40 => OldGodType.Veloura,
                    55 => OldGodType.Thorgrim,
                    70 => OldGodType.Noctura,
                    85 => OldGodType.Aurelion,
                    95 => OldGodType.Terravok,
                    100 => OldGodType.Manwe,
                    _ => null
                };

                if (returnGodType != null)
                {
                    var returnStory = StoryProgressionSystem.Instance;
                    if (returnStory.OldGodStates.TryGetValue(returnGodType.Value, out var returnGodState) &&
                        returnGodState.Status == GodStatus.Awakened &&
                        OldGodBossSystem.Instance.CanEncounterBoss(player2, returnGodType.Value))
                    {
                        var saveResult = await OldGodBossSystem.Instance.CompleteSaveQuest(player2, returnGodType.Value, terminal);
                        await HandleGodEncounterResult(saveResult, player2, terminal);

                        if (saveResult.Outcome == BossOutcome.Saved)
                        {
                            targetRoom.IsCleared = true;
                            currentFloor.BossDefeated = true;
                        }

                        await terminal.PressAnyKey();
                    }
                }
            }
        }

        // If room has monsters and player enters, auto-engage (ambush chance)
        if (targetRoom.HasMonsters && !targetRoom.IsCleared)
        {
            consecutiveMonsterRooms++;

            if (dungeonRandom.NextDouble() < 0.3 && !targetRoom.IsBossRoom)
            {
                terminal.SetColor("red");
                terminal.WriteLine(Loc.Get("dungeon.ambush"));
                await Task.Delay(1000);
                await FightRoomMonsters(targetRoom, isAmbush: true);
            }
        }
        else
        {
            consecutiveMonsterRooms = 0;
        }

        // MUD streaming mode: entering a new room is a content change — show the room view
        // on the next loop iteration instead of just re-showing the prompt
        RequestRedisplay();
    }

    /// <summary>
    /// Display a dungeon vision (environmental narrative beat)
    /// </summary>
    private async Task DisplayDungeonVision(DungeonVision vision)
    {
        terminal.WriteLine("");
        terminal.SetColor("dark_magenta");
        terminal.WriteLine($"=== {vision.Description} ===");
        terminal.WriteLine("");

        terminal.SetColor("magenta");
        foreach (var line in vision.Content)
        {
            terminal.WriteLine($"  {line}");
            await Task.Delay(1200);
        }
        terminal.WriteLine("");

        // Apply awakening gain if any
        if (vision.AwakeningGain > 0)
        {
            OceanPhilosophySystem.Instance.GainInsight(vision.AwakeningGain * 10);
            terminal.SetColor("cyan");
            terminal.WriteLine(Loc.Get("dungeon.vision_memory_stirs"));
        }

        // Grant wave fragment if any
        if (vision.WaveFragment.HasValue)
        {
            OceanPhilosophySystem.Instance.CollectFragment(vision.WaveFragment.Value);
            terminal.SetColor("bright_cyan");
            terminal.WriteLine(Loc.Get("dungeon.vision_fragment_settles"));
        }

        await terminal.PressAnyKey();
    }

    /// <summary>
    /// Check for crafting material drops after dungeon combat victory.
    /// Materials only drop from monsters within specific floor ranges.
    /// </summary>
    private async Task CheckForMaterialDrop(Character player, bool isBoss, bool isMiniBoss)
    {
        var eligibleMaterials = GameConfig.GetMaterialsForFloor(currentDungeonLevel);
        if (eligibleMaterials.Count == 0) return;

        double dropChance;
        int dropCount = 1;

        if (isBoss)
        {
            dropChance = 1.0;
            dropCount = dungeonRandom.Next(GameConfig.MaterialDropCountBossMin, GameConfig.MaterialDropCountBossMax + 1);
        }
        else if (isMiniBoss)
        {
            dropChance = GameConfig.MaterialDropChanceMiniBoss;
        }
        else
        {
            dropChance = GameConfig.MaterialDropChanceRegular;
        }

        if (dungeonRandom.NextDouble() < dropChance)
        {
            var material = eligibleMaterials[dungeonRandom.Next(eligibleMaterials.Count)];
            player.AddMaterial(material.Id, dropCount);
            await DisplayMaterialDrop(material, dropCount);
        }
    }

    /// <summary>
    /// Display a dramatic material drop notification
    /// </summary>
    private async Task DisplayMaterialDrop(GameConfig.CraftingMaterialDef material, int count)
    {
        terminal.WriteLine("");
        WriteThickDivider(42);
        terminal.SetColor(material.Color);
        terminal.WriteLine(Loc.Get("dungeon.rare_material_found"));
        WriteThickDivider(42);
        terminal.SetColor(material.Color);
        terminal.WriteLine($"    {material.Name}" + (count > 1 ? $" x{count}" : ""));
        terminal.SetColor("gray");
        terminal.WriteLine($"    \"{material.Description}\"");
        WriteThickDivider(42);
        terminal.WriteLine("");
        await Task.Delay(1500);
    }

    /// <summary>
    /// Check if player evades a trap based on agility
    /// Returns true if trap is evaded, false if it hits
    /// </summary>
    private bool TryEvadeTrap(Character player, int trapDifficulty = 50)
    {
        // Base evasion chance: Agility / 3, capped at 75%
        // Higher agility = better chance to dodge
        // trapDifficulty modifies the roll (higher = harder to evade)
        int evasionChance = (int)Math.Min(75, player.Agility / 3);

        // Dungeon level makes traps harder to evade
        evasionChance -= currentDungeonLevel / 5;

        // Assassins get strong trap evasion: +15 base + Dexterity scaling
        if (player.Class == CharacterClass.Assassin)
            evasionChance += 15 + (int)(player.Dexterity / 8);

        // Rangers get moderate trap evasion: +10 base + Dexterity scaling
        else if (player.Class == CharacterClass.Ranger)
            evasionChance += 10 + (int)(player.Dexterity / 12);

        // Jester/Bard get minor bonus from nimbleness
        else if (player.Class == CharacterClass.Jester || player.Class == CharacterClass.Bard)
            evasionChance += 5;

        // Minimum 5% chance to evade, cap at 85%
        evasionChance = Math.Clamp(evasionChance, 5, 85);

        int roll = dungeonRandom.Next(100);
        return roll < evasionChance;
    }

    /// <summary>
    /// Trigger a trap when entering a room
    /// </summary>
    private async Task TriggerTrap(DungeonRoom room)
    {
        room.TrapTriggered = true;
        var player = GetCurrentPlayer();

        terminal.SetColor("red");
        terminal.WriteLine(Loc.Get("dungeon.trap_triggered"));
        BroadcastDungeonEvent("\u001b[1;31m  *** TRAP! ***\u001b[0m");
        await Task.Delay(500);

        // Check for evasion based on agility
        if (TryEvadeTrap(player!))
        {
            terminal.SetColor("green");
            terminal.WriteLine(Loc.Get("dungeon.trap_evade_reflexes"));
            terminal.WriteLine(Loc.Get("dungeon.trap_evade_agility", player.Agility));
            BroadcastDungeonEvent($"\u001b[32m  {player!.Name2}'s quick reflexes avoid the trap!\u001b[0m");
            await Task.Delay(1500);
            return;
        }

        terminal.WriteLine(Loc.Get("dungeon.trap_no_react"), "yellow");
        await Task.Delay(300);

        var trapType = dungeonRandom.Next(6);
        switch (trapType)
        {
            case 0:
                var pitDmg = currentDungeonLevel * 3 + dungeonRandom.Next(10);
                player.HP = Math.Max(1, player.HP - pitDmg);
                terminal.WriteLine(Loc.Get("dungeon.trap_pit", pitDmg));
                BroadcastDungeonEvent($"\u001b[31m  The floor gives way! {player!.Name2} falls into a pit for {pitDmg} damage!\u001b[0m");
                break;

            case 1:
                var dartDmg = currentDungeonLevel * 2 + dungeonRandom.Next(8);
                player.HP = Math.Max(1, player.HP - dartDmg);
                player.Poison = Math.Max(player.Poison, 1);
                player.PoisonTurns = Math.Max(player.PoisonTurns, 5 + currentDungeonLevel / 5);
                terminal.WriteLine(Loc.Get("dungeon.trap_darts", dartDmg));
                BroadcastDungeonEvent($"\u001b[31m  Poison darts! {player!.Name2} takes {dartDmg} damage and is poisoned!\u001b[0m");
                break;

            case 2:
                var fireDmg = currentDungeonLevel * 4 + dungeonRandom.Next(12);
                player.HP = Math.Max(1, player.HP - fireDmg);
                terminal.WriteLine(Loc.Get("dungeon.trap_fire", fireDmg));
                BroadcastDungeonEvent($"\u001b[31m  A gout of flame! {player!.Name2} takes {fireDmg} fire damage!\u001b[0m");
                break;

            case 3:
                var goldLost = player.Gold / 10;
                player.Gold -= goldLost;
                terminal.WriteLine(Loc.Get("dungeon.trap_acid", goldLost));
                BroadcastDungeonEvent($"\u001b[33m  Acid sprays the party! {player!.Name2} loses {goldLost} gold!\u001b[0m");
                break;

            case 4:
                // Wisdom check: high wisdom can fully resist the curse
                int resistChance = (int)Math.Min(60, player.Wisdom / 2);
                if (player.Class == CharacterClass.Cleric || player.Class == CharacterClass.Sage)
                    resistChance += 15;
                if (dungeonRandom.Next(100) < resistChance)
                {
                    terminal.SetColor("bright_cyan");
                    terminal.WriteLine(Loc.Get("dungeon.trap_curse_resist", player.Wisdom));
                    BroadcastDungeonEvent($"\u001b[36m  A dark curse washes over {player!.Name2}, but their wisdom shields them!\u001b[0m");
                }
                else
                {
                    // XP drain: capped to 5% of current XP or level*25, whichever is lower
                    long baseExpLost = currentDungeonLevel * 50;
                    long maxDrain = Math.Max(50, (long)(player.Experience * 0.05));
                    long levelCap = (long)player.Level * 25;
                    var expLost = (long)Math.Min(baseExpLost, Math.Min(maxDrain, levelCap));
                    // Constitution reduces the drain by up to 40%
                    float conReduction = Math.Min(0.4f, player.Constitution / 100f);
                    expLost = (long)(expLost * (1f - conReduction));
                    expLost = Math.Max(10, expLost);
                    player.Experience = Math.Max(0, player.Experience - expLost);
                    terminal.WriteLine(Loc.Get("dungeon.trap_curse_drain", expLost));
                    BroadcastDungeonEvent($"\u001b[35m  A dark curse washes over the room! {player!.Name2} loses {expLost} experience!\u001b[0m");
                }
                break;

            case 5:
                terminal.SetColor("green");
                terminal.WriteLine(Loc.Get("dungeon.trap_broken"));
                long bonusGold = currentDungeonLevel * 20;
                player.Gold += bonusGold;
                terminal.WriteLine(Loc.Get("dungeon.trap_salvage", bonusGold));
                BroadcastDungeonEvent($"\u001b[32m  The trap mechanism is broken! {player!.Name2} salvages {bonusGold} gold.\u001b[0m");
                break;
        }

        await Task.Delay(2000);
    }

    /// <summary>
    /// Fight the monsters in a room
    /// </summary>
    private async Task FightRoomMonsters(DungeonRoom room, bool isAmbush = false)
    {
        var player = GetCurrentPlayer();

        if (!GameConfig.ScreenReaderMode)
            terminal.ClearScreen();
        terminal.SetColor("red");

        if (room.IsBossRoom)
        {
            WriteBoxHeader(Loc.Get("dungeon.boss_encounter"), "red", 51);
            terminal.WriteLine("");
            terminal.WriteLine(room.Description);

            // Check for Old God boss encounters on specific floors
            bool hadOldGodEncounter = await TryOldGodBossEncounter(player!, room);
            if (hadOldGodEncounter)
            {
                return; // Old God encounter handled the room
            }

            // If this is an Old God floor but the boss can't be encountered
            // (already defeated/saved/allied), mark room as cleared without combat
            OldGodType? godType = currentDungeonLevel switch
            {
                25 => OldGodType.Maelketh,
                40 => OldGodType.Veloura,
                55 => OldGodType.Thorgrim,
                70 => OldGodType.Noctura,
                85 => OldGodType.Aurelion,
                95 => OldGodType.Terravok,
                100 => OldGodType.Manwe,
                _ => null
            };

            if (godType != null)
            {
                // Check if the Old God was actually resolved (defeated, saved, allied, etc.)
                var story = StoryProgressionSystem.Instance;
                bool godResolved = story.OldGodStates.TryGetValue(godType.Value, out var state) &&
                    (state.Status == GodStatus.Defeated ||
                     state.Status == GodStatus.Saved ||
                     state.Status == GodStatus.Allied ||
                     state.Status == GodStatus.Consumed);

                if (godResolved)
                {
                    // Old God was already dealt with - room is empty
                    room.IsCleared = true;
                    currentFloor.BossDefeated = true;
                    terminal.SetColor("gray");
                    terminal.WriteLine(Loc.Get("dungeon.chamber_empty"));
                    await Task.Delay(1500);
                    return;
                }
                else
                {
                    // God exists but prerequisites not met - hint that something ancient lurks here
                    terminal.SetColor("dark_magenta");
                    terminal.WriteLine(Loc.Get("dungeon.ancient_presence_sealed"));
                    terminal.WriteLine(Loc.Get("dungeon.prove_yourself_hint"), "gray");
                    terminal.WriteLine("");
                    await Task.Delay(2000);
                }
                // Fall through to generate normal boss monsters as placeholder
            }
        }
        else
        {
            WriteSectionHeader(Loc.Get("dungeon.section_combat"));
        }

        terminal.WriteLine("");
        await Task.Delay(1000);

        // Straggler encounters: chance of weaker monsters from upper floors (v0.49.3)
        int effectiveMonsterLevel = currentDungeonLevel;
        if (currentDungeonLevel >= GameConfig.StragglerMinFloor && !room.IsBossRoom
            && dungeonRandom.NextDouble() < GameConfig.StragglerEncounterChance)
        {
            effectiveMonsterLevel = Math.Max(1, currentDungeonLevel - dungeonRandom.Next(GameConfig.StragglerLevelReductionMin, GameConfig.StragglerLevelReductionMax));
            terminal.SetColor("gray");
            terminal.WriteLine(Loc.Get("dungeon.straggler_appears"));
            terminal.WriteLine("");
        }

        // Generate monsters appropriate for this room
        var monsters = MonsterGenerator.GenerateMonsterGroup(effectiveMonsterLevel, dungeonRandom);

        // Make boss room monsters tougher (HP only — STR boost removed to prevent
        // double-dipping with Monster.GetAttackPower()'s 1.3x IsBoss multiplier)
        if (room.IsBossRoom)
        {
            foreach (var m in monsters)
            {
                m.HP = (long)(m.HP * 1.5);
                m.MaxHP = m.HP;  // Keep MaxHP in sync with boosted HP
            }
            // Ensure there's a boss
            if (!monsters.Any(m => m.IsBoss))
            {
                monsters[0].IsBoss = true;
                monsters[0].Name = GetBossName(currentFloor.Theme);
                monsters[0].Phrase = GetBossPhrase(currentFloor.Theme);
            }
        }

        // Display what we're fighting - handle mixed encounters properly
        if (monsters.Count == 1)
        {
            var monster = monsters[0];
            terminal.SetColor(monster.MonsterColor);
            terminal.WriteLine(Loc.Get("dungeon.monster_attacks", monster.Name));
        }
        else
        {
            // Group monsters by name to handle mixed encounters
            var monsterGroups = monsters.GroupBy(m => m.Name)
                .Select(g => new { Name = g.Key, Count = g.Count(), Color = g.First().MonsterColor })
                .ToList();

            terminal.SetColor("yellow");
            if (monsterGroups.Count == 1)
            {
                // All same type
                var group = monsterGroups[0];
                string plural = group.Count > 1 ? GetPluralName(group.Name) : group.Name;
                terminal.WriteLine(Loc.Get("dungeon.face_monsters", group.Count, plural));
            }
            else
            {
                // Mixed encounter
                terminal.Write(Loc.Get("dungeon.you_face_prefix"));
                for (int i = 0; i < monsterGroups.Count; i++)
                {
                    var group = monsterGroups[i];
                    string plural = group.Count > 1 ? GetPluralName(group.Name) : group.Name;

                    if (i > 0 && i == monsterGroups.Count - 1)
                        terminal.Write(Loc.Get("dungeon.and_separator"));
                    else if (i > 0)
                        terminal.Write(", ");

                    terminal.Write($"{group.Count} {plural}");
                }
                terminal.WriteLine("!");
            }
        }

        terminal.WriteLine("");

        // Broadcast combat encounter to group followers
        {
            var monsterSummary = string.Join(", ", monsters.GroupBy(m => m.Name)
                .Select(g => g.Count() > 1 ? $"{g.Count()} {GetPluralName(g.Key)}" : g.Key));
            BroadcastDungeonEvent(room.IsBossRoom
                ? $"\u001b[1;31m  *** BOSS ENCOUNTER: {monsterSummary} ***\u001b[0m"
                : $"\u001b[1;33m  Combat! The group faces {monsterSummary}!\u001b[0m");
        }

        await Task.Delay(1500);

        // Check for divine punishment before combat
        var (punishmentApplied, damageModifier, defenseModifier) = await CheckDivinePunishment(player!);

        // Apply temporary combat penalties from divine wrath
        int originalTempAttackBonus = player.TempAttackBonus;
        int originalTempDefenseBonus = player.TempDefenseBonus;
        if (punishmentApplied)
        {
            // Convert percentage modifier to stat penalty (rough approximation)
            player.TempAttackBonus -= Math.Abs(damageModifier) * 2;
            player.TempDefenseBonus -= Math.Abs(defenseModifier) * 2;
        }

        // Combat
        var combatEngine = new CombatEngine(terminal);
        var combatResult = await combatEngine.PlayerVsMonsters(player, monsters, teammates, offerMonkEncounter: true, isAmbush: isAmbush);

        // Restore original temp bonuses after combat
        if (punishmentApplied)
        {
            player.TempAttackBonus = originalTempAttackBonus;
            player.TempDefenseBonus = originalTempDefenseBonus;
        }

        // Check if player should return to temple after resurrection
        if (combatResult.ShouldReturnToTemple)
        {
            terminal.SetColor("yellow");
            terminal.WriteLine(Loc.Get("dungeon.awaken_temple"));
            await Task.Delay(2000);
            await NavigateToLocation(GameLocation.Temple);
            return;
        }

        // Check if player survived AND won (fleeing doesn't clear the room)
        if (player.HP > 0 && combatResult.Outcome == CombatOutcome.Victory)
        {
            room.IsCleared = true;
            currentFloor.MonstersKilled += monsters.Count;

            terminal.SetColor("green");
            terminal.WriteLine(Loc.Get("dungeon.room_cleared"));
            await TryCompanionNavigationComment(room);

            if (room.IsBossRoom)
            {
                currentFloor.BossDefeated = true;

                // Also set the persistent flag in floor state
                if (player.DungeonFloorStates.TryGetValue(currentDungeonLevel, out var floorState))
                {
                    floorState.BossDefeated = true;
                }

                terminal.SetColor("bright_yellow");
                terminal.WriteLine(Loc.Get("dungeon.boss_defeated"));
                terminal.WriteLine("");

                // Bonus rewards for boss
                long bossGold = currentDungeonLevel * 500 + dungeonRandom.Next(1000);
                long bossExp = currentDungeonLevel * 300;

                var (bossXPShare, bossGoldShare) = AwardDungeonReward(bossExp, bossGold, "Boss Defeated");

                if (teammates.Count > 0)
                {
                    terminal.WriteLine(Loc.Get("dungeon.boss_bonus_share", bossGold, bossExp, bossGoldShare, bossXPShare));
                    BroadcastDungeonEvent($"\u001b[1;33m  *** BOSS DEFEATED! ***\u001b[0m");
                }
                else
                {
                    terminal.WriteLine(Loc.Get("dungeon.boss_bonus", bossGold, bossExp));
                }

                // Artifact drop chance for specific floor bosses
                await CheckArtifactDrop(player, currentDungeonLevel);
            }

            // Check for rare crafting material drops
            bool hadBoss = room.IsBossRoom;
            bool hadMiniBoss = monsters.Any(m => m.IsMiniBoss);
            await CheckForMaterialDrop(player, hadBoss, hadMiniBoss);

            await Task.Delay(2000);
        }

        RequestRedisplay();
        await terminal.PressAnyKey();
    }

    private static readonly Dictionary<DungeonTheme, string[]> BossNamePool = new()
    {
        { DungeonTheme.Catacombs, new[] { "Bone Lord", "Crypt Warden", "Skull Revenant", "Tomb Sentinel", "Ossuary King" } },
        { DungeonTheme.Sewers, new[] { "Sludge Abomination", "Bile Crawler", "Plague Broodmother", "Sewer Hydra", "Filth Colossus" } },
        { DungeonTheme.Caverns, new[] { "Crystal Guardian", "Stone Colossus", "Deep Wurm", "Stalactite Horror", "Cave Drake" } },
        { DungeonTheme.AncientRuins, new[] { "Awakened Golem", "Ruined Sentinel", "Arcane Construct", "Timeworn Pharaoh", "Relic Guardian" } },
        { DungeonTheme.DemonLair, new[] { "Pit Fiend", "Infernal Tyrant", "Doom Bringer", "Hellfire Warden", "Abyssal Overlord" } },
        { DungeonTheme.FrozenDepths, new[] { "Frost Wyrm", "Glacial Titan", "Permafrost Revenant", "Blizzard Elemental", "Ice Lich" } },
        { DungeonTheme.VolcanicPit, new[] { "Magma Elemental", "Volcanic Behemoth", "Molten Serpent", "Cinder Lord", "Lava Golem" } },
        { DungeonTheme.AbyssalVoid, new[] { "Void Horror", "Entropy Weaver", "Null Devourer", "Shadow Sovereign", "Abyssal Maw" } },
    };

    private string GetBossName(DungeonTheme theme)
    {
        if (BossNamePool.TryGetValue(theme, out var names))
        {
            // Use floor level as seed so the same floor always has the same boss name
            int index = currentDungeonLevel % names.Length;
            return names[index];
        }
        return "Dungeon Boss";
    }

    private static string GetBossPhrase(DungeonTheme theme)
    {
        return theme switch
        {
            DungeonTheme.Catacombs => Loc.Get("dungeon.boss_phrase_catacombs"),
            DungeonTheme.Sewers => Loc.Get("dungeon.boss_phrase_sewers"),
            DungeonTheme.Caverns => Loc.Get("dungeon.boss_phrase_caverns"),
            DungeonTheme.AncientRuins => Loc.Get("dungeon.boss_phrase_ruins"),
            DungeonTheme.DemonLair => Loc.Get("dungeon.boss_phrase_demon"),
            DungeonTheme.FrozenDepths => Loc.Get("dungeon.boss_phrase_frozen"),
            DungeonTheme.VolcanicPit => Loc.Get("dungeon.boss_phrase_volcanic"),
            DungeonTheme.AbyssalVoid => Loc.Get("dungeon.boss_phrase_abyssal"),
            _ => Loc.Get("dungeon.boss_phrase_default")
        };
    }

    /// <summary>
    /// Check for and handle Old God boss encounters on specific floors
    /// Returns true if an Old God encounter was triggered (regardless of outcome)
    /// </summary>
    private async Task<bool> TryOldGodBossEncounter(Character player, DungeonRoom room)
    {
        OldGodType? godType = currentDungeonLevel switch
        {
            25 => OldGodType.Maelketh,   // The Broken Blade - God of War
            40 => OldGodType.Veloura,    // The Fading Heart - Goddess of Love (saveable)
            55 => OldGodType.Thorgrim,   // The Unjust Judge - God of Law
            70 => OldGodType.Noctura,    // The Shadow Queen - Goddess of Shadows (ally-able)
            85 => OldGodType.Aurelion,   // The Dimming Light - God of Light (saveable)
            95 => OldGodType.Terravok,   // The Worldbreaker - God of Earth (awakenable)
            100 => OldGodType.Manwe,     // The Creator - Final Boss
            _ => null
        };

        if (godType == null)
            return false;

        // Check if this is a save quest return visit (god is Awakened and player has artifact)
        var story = StoryProgressionSystem.Instance;
        if (story.OldGodStates.TryGetValue(godType.Value, out var godState) &&
            godState.Status == GodStatus.Awakened)
        {
            // Player has the artifact - complete the save quest
            if (OldGodBossSystem.Instance.CanEncounterBoss(player, godType.Value))
            {
                var saveResult = await OldGodBossSystem.Instance.CompleteSaveQuest(player, godType.Value, terminal);
                await HandleGodEncounterResult(saveResult, player, terminal);

                if (saveResult.Outcome == BossOutcome.Saved)
                {
                    room.IsCleared = true;
                    currentFloor.BossDefeated = true;
                }

                await terminal.PressAnyKey();
                return true;
            }
            // No artifact yet - don't trigger encounter, player needs to find it first
            return false;
        }

        if (!OldGodBossSystem.Instance.CanEncounterBoss(player, godType.Value))
            return false;

        // Display Old God encounter intro based on which god
        terminal.WriteLine("");
        await Task.Delay(1000);

        switch (godType.Value)
        {
            case OldGodType.Maelketh:
                terminal.SetColor("bright_red");
                terminal.WriteLine(Loc.Get("dungeon.god_intro_maelketh_1"));
                await Task.Delay(1500);
                terminal.WriteLine(Loc.Get("dungeon.god_intro_maelketh_2"), "bright_red");
                terminal.WriteLine("");
                terminal.WriteLine(Loc.Get("dungeon.god_intro_maelketh_3"), "yellow");
                terminal.WriteLine(Loc.Get("dungeon.god_intro_maelketh_4"), "red");
                break;

            case OldGodType.Veloura:
                terminal.SetColor("bright_magenta");
                terminal.WriteLine(Loc.Get("dungeon.god_intro_veloura_1"));
                await Task.Delay(1500);
                terminal.WriteLine(Loc.Get("dungeon.god_intro_veloura_2"), "bright_magenta");
                terminal.WriteLine("");
                terminal.WriteLine(Loc.Get("dungeon.god_intro_veloura_3"), "magenta");
                terminal.WriteLine(Loc.Get("dungeon.god_intro_veloura_4"), "bright_magenta");
                terminal.WriteLine(Loc.Get("dungeon.god_intro_veloura_5"), "magenta");
                break;

            case OldGodType.Thorgrim:
                terminal.SetColor("white");
                terminal.WriteLine(Loc.Get("dungeon.god_intro_thorgrim_1"));
                await Task.Delay(1500);
                terminal.WriteLine(Loc.Get("dungeon.god_intro_thorgrim_2"), "white");
                terminal.WriteLine("");
                terminal.WriteLine(Loc.Get("dungeon.god_intro_thorgrim_3"), "gray");
                terminal.WriteLine(Loc.Get("dungeon.god_intro_thorgrim_4"), "white");
                terminal.WriteLine(Loc.Get("dungeon.god_intro_thorgrim_5"), "gray");
                break;

            case OldGodType.Noctura:
                terminal.SetColor("bright_cyan");
                terminal.WriteLine(Loc.Get("dungeon.god_intro_noctura_1"));
                await Task.Delay(1500);
                terminal.WriteLine(Loc.Get("dungeon.god_intro_noctura_2"), "bright_cyan");
                terminal.WriteLine("");
                terminal.WriteLine(Loc.Get("dungeon.god_intro_noctura_3"), "cyan");
                terminal.WriteLine(Loc.Get("dungeon.god_intro_noctura_4"), "bright_cyan");
                terminal.WriteLine(Loc.Get("dungeon.god_intro_noctura_5"), "cyan");
                break;

            case OldGodType.Aurelion:
                terminal.SetColor("bright_yellow");
                terminal.WriteLine(Loc.Get("dungeon.god_intro_aurelion_1"));
                await Task.Delay(1500);
                terminal.WriteLine(Loc.Get("dungeon.god_intro_aurelion_2"), "bright_yellow");
                terminal.WriteLine("");
                terminal.WriteLine(Loc.Get("dungeon.god_intro_aurelion_3"), "yellow");
                terminal.WriteLine(Loc.Get("dungeon.god_intro_aurelion_4"), "bright_yellow");
                terminal.WriteLine(Loc.Get("dungeon.god_intro_aurelion_5"), "yellow");
                break;

            case OldGodType.Terravok:
                terminal.SetColor("bright_yellow");
                terminal.WriteLine(Loc.Get("dungeon.god_intro_terravok_1"));
                await Task.Delay(1500);
                terminal.WriteLine(Loc.Get("dungeon.god_intro_terravok_2"), "bright_yellow");
                terminal.WriteLine("");
                terminal.WriteLine(Loc.Get("dungeon.god_intro_terravok_3"), "yellow");
                terminal.WriteLine(Loc.Get("dungeon.god_intro_terravok_4"), "yellow");
                break;

            case OldGodType.Manwe:
                terminal.SetColor("bright_white");
                terminal.WriteLine(Loc.Get("dungeon.god_intro_manwe_1"));
                await Task.Delay(2000);
                terminal.WriteLine(Loc.Get("dungeon.god_intro_manwe_2"), "bright_white");
                terminal.WriteLine("");
                terminal.SetColor("white");
                terminal.WriteLine(Loc.Get("dungeon.god_intro_manwe_3"));
                terminal.SetColor("bright_cyan");
                terminal.WriteLine(Loc.Get("dungeon.god_intro_manwe_4"));
                terminal.WriteLine(Loc.Get("dungeon.god_intro_manwe_5"));
                terminal.WriteLine(Loc.Get("dungeon.god_intro_manwe_6"));
                break;
        }

        terminal.WriteLine("");
        await Task.Delay(1500);

        string godName = godType.Value switch
        {
            OldGodType.Maelketh => Loc.Get("dungeon.god_title_maelketh"),
            OldGodType.Veloura => Loc.Get("dungeon.god_title_veloura"),
            OldGodType.Thorgrim => Loc.Get("dungeon.god_title_thorgrim"),
            OldGodType.Noctura => Loc.Get("dungeon.god_title_noctura"),
            OldGodType.Aurelion => Loc.Get("dungeon.god_title_aurelion"),
            OldGodType.Terravok => Loc.Get("dungeon.god_title_terravok"),
            OldGodType.Manwe => Loc.Get("dungeon.god_title_manwe"),
            _ => Loc.Get("dungeon.god_title_default")
        };

        terminal.SetColor("bright_yellow");
        terminal.WriteLine(Loc.Get("dungeon.face_god_prompt", godName));
        var response = await terminal.GetInput("> ");

        if (response.Trim().ToUpper().StartsWith("Y"))
        {
            var result = await OldGodBossSystem.Instance.StartBossEncounter(player, godType.Value, terminal, teammates);
            await HandleGodEncounterResult(result, player, terminal);

            // Mark room as cleared if defeated or alternate outcome achieved
            if (result.Outcome != BossOutcome.Fled && result.Outcome != BossOutcome.PlayerDefeated)
            {
                room.IsCleared = true;
                currentFloor.BossDefeated = true;
            }

            // Special handling for Manwe - trigger the full ending sequence (cinematic ending, credits, NG+ offer)
            if (godType.Value == OldGodType.Manwe && result.Outcome != BossOutcome.Fled)
            {
                var endingType = EndingsSystem.Instance.DetermineEnding(player);
                await EndingsSystem.Instance.TriggerEnding(player, endingType, terminal);
                if (GameEngine.Instance.PendingNewGamePlus || GameEngine.Instance.PendingImmortalAscension)
                    throw new LocationExitException(GameLocation.NoWhere);
                else
                    await NavigateToLocation(GameLocation.MainStreet);
            }

            RequestRedisplay();
            await terminal.PressAnyKey();
            return true;
        }
        else
        {
            terminal.SetColor("gray");
            string retreatMessage = godType.Value switch
            {
                OldGodType.Maelketh => Loc.Get("dungeon.god_retreat_maelketh"),
                OldGodType.Terravok => Loc.Get("dungeon.god_retreat_terravok"),
                OldGodType.Manwe => Loc.Get("dungeon.god_retreat_manwe"),
                _ => Loc.Get("dungeon.god_retreat_default")
            };
            terminal.WriteLine(retreatMessage);
            terminal.WriteLine(Loc.Get("dungeon.boss_room_unconquered"), "yellow");
            await terminal.PressAnyKey();
            return true; // Still return true - we don't want regular monsters
        }
    }

    /// <summary>
    /// Collect treasure from a cleared room
    /// </summary>
    private async Task CollectTreasure(DungeonRoom room)
    {
        var player = GetCurrentPlayer();
        room.TreasureLooted = true;
        currentFloor.TreasuresFound++;

        terminal.ClearScreen();

        // Display treasure art
        if (GameConfig.ScreenReaderMode)
        {
            terminal.WriteLine("");
            terminal.SetColor("bright_yellow");
            terminal.WriteLine(Loc.Get("dungeon.treasure_found_sr"));
            terminal.WriteLine("");
        }
        else
        {
            await UsurperRemake.UI.ANSIArt.DisplayArtAnimated(terminal, UsurperRemake.UI.ANSIArt.Treasure, 30);
            terminal.WriteLine("");
        }

        // Scale rewards with level
        long goldFound = currentDungeonLevel * 100 + dungeonRandom.Next(currentDungeonLevel * 200);
        long expFound = currentDungeonLevel * 50 + dungeonRandom.Next(100);

        var (actualXP, actualGold) = AwardDungeonReward(expFound, goldFound, "Treasure");

        if (teammates.Count > 0)
        {
            terminal.WriteLine(Loc.Get("dungeon.treasure_party_finds", goldFound, expFound));
            terminal.WriteLine(Loc.Get("dungeon.treasure_your_share", actualGold, actualXP));
            BroadcastDungeonEvent($"\u001b[93m  The party found treasure!\u001b[0m");
        }
        else
        {
            terminal.WriteLine(Loc.Get("dungeon.treasure_gold_found", goldFound));
            terminal.WriteLine(Loc.Get("dungeon.treasure_exp_found", expFound));
        }

        // Chance for bonus items
        if (dungeonRandom.NextDouble() < 0.3)
        {
            int potions = dungeonRandom.Next(1, 3);
            player.Healing = Math.Min(player.MaxPotions, player.Healing + potions);
            terminal.SetColor("green");
            terminal.WriteLine(Loc.Get("dungeon.treasure_potions", potions));
        }

        // Captain Aldric's Mission — treasure objective
        if (player.HintsShown.Contains("aldric_quest_active") && !player.HintsShown.Contains("quest_scout_find_treasure"))
        {
            player.HintsShown.Add("quest_scout_find_treasure");
            terminal.WriteLine("");
            terminal.SetColor("bright_cyan");
            terminal.WriteLine($"  {Loc.Get("aldric_quest.treasure_found")}");
            terminal.SetColor("yellow");
            terminal.WriteLine($"  {Loc.Get("aldric_quest.treasure_updated")}");

            // Check if all dungeon objectives are done
            if (player.HintsShown.Contains("quest_scout_enter_dungeon") && player.HintsShown.Contains("quest_scout_kill_monster"))
            {
                terminal.SetColor("bright_green");
                terminal.WriteLine($"  {Loc.Get("aldric_quest.all_complete")}");
            }
            terminal.SetColor("white");
        }

        // Auto-save after finding treasure
        await SaveSystem.Instance.AutoSave(player);

        await Task.Delay(2500);
        await terminal.PressAnyKey();
    }

    /// <summary>
    /// Handle room-specific events
    /// </summary>
    private async Task HandleRoomEvent(DungeonRoom room)
    {
        room.EventCompleted = true;

        // Broadcast room event type to followers
        string? eventDesc = room.EventType switch
        {
            DungeonEventType.TreasureChest => Loc.Get("dungeon.event_desc_treasure"),
            DungeonEventType.Merchant => Loc.Get("dungeon.event_desc_merchant"),
            DungeonEventType.Shrine => Loc.Get("dungeon.event_desc_shrine"),
            DungeonEventType.NPCEncounter => Loc.Get("dungeon.event_desc_npc"),
            DungeonEventType.Puzzle => Loc.Get("dungeon.event_desc_puzzle"),
            DungeonEventType.RestSpot => Loc.Get("dungeon.event_desc_rest"),
            DungeonEventType.MysteryEvent => Loc.Get("dungeon.event_desc_mystery"),
            DungeonEventType.Riddle => Loc.Get("dungeon.event_desc_riddle"),
            DungeonEventType.LoreDiscovery => Loc.Get("dungeon.event_desc_lore"),
            DungeonEventType.MemoryFlash => Loc.Get("dungeon.event_desc_memory"),
            DungeonEventType.SecretBoss => Loc.Get("dungeon.event_desc_secret_boss"),
            _ => Loc.Get("dungeon.event_desc_default")
        };
        var player = GetCurrentPlayer();
        BroadcastDungeonEvent($"\u001b[96m  {player?.Name2} {eventDesc}...\u001b[0m");

        switch (room.EventType)
        {
            case DungeonEventType.TreasureChest:
                await TreasureChestEncounter();
                break;
            case DungeonEventType.Merchant:
                await MerchantEncounter();
                break;
            case DungeonEventType.Shrine:
                await MysteriousShrine();
                break;
            case DungeonEventType.NPCEncounter:
                await NPCEncounter();
                break;
            case DungeonEventType.Puzzle:
                await PuzzleEncounter();
                break;
            case DungeonEventType.RestSpot:
                await RestSpotEncounter();
                break;
            case DungeonEventType.MysteryEvent:
                await MysteryEventEncounter();
                break;
            case DungeonEventType.Riddle:
                await RiddleGateEncounter();
                break;
            case DungeonEventType.LoreDiscovery:
                await LoreLibraryEncounter();
                break;
            case DungeonEventType.MemoryFlash:
                await MemoryFragmentEncounter();
                break;
            case DungeonEventType.SecretBoss:
                await SecretBossEncounter();
                break;
            case DungeonEventType.Settlement:
                await SettlementEncounter();
                break;
            default:
                await RandomDungeonEvent();
                break;
        }
    }

    /// <summary>
    /// Examine room features
    /// </summary>
    private async Task ExamineFeatures(DungeonRoom room)
    {
        var unexamined = room.Features.Where(f => !f.IsInteracted).ToList();
        if (unexamined.Count == 0) return;

        terminal.ClearScreen();
        terminal.SetColor("cyan");
        terminal.WriteLine(Loc.Get("dungeon.examine_what"));
        terminal.WriteLine("");

        for (int i = 0; i < unexamined.Count; i++)
        {
            terminal.SetColor("white");
            terminal.Write($"[{i + 1}] ");
            terminal.SetColor("yellow");
            terminal.Write(unexamined[i].Name);
            terminal.SetColor("gray");
            terminal.WriteLine($" ({unexamined[i].Interaction})");
        }

        terminal.WriteLine("");
        terminal.SetColor("darkgray");
        terminal.Write("[");
        terminal.SetColor("bright_yellow");
        terminal.Write("0");
        terminal.SetColor("darkgray");
        terminal.Write("] ");
        terminal.SetColor("white");
        terminal.WriteLine(Loc.Get("ui.cancel"));
        terminal.WriteLine("");

        var input = await terminal.GetInput(Loc.Get("ui.choice"));

        if (int.TryParse(input, out int idx) && idx >= 1 && idx <= unexamined.Count)
        {
            await InteractWithFeature(unexamined[idx - 1]);
        }
    }

    /// <summary>
    /// Interact with a specific feature using the enhanced FeatureInteractionSystem
    /// </summary>
    private async Task InteractWithFeature(RoomFeature feature)
    {
        feature.IsInteracted = true;
        var player = GetCurrentPlayer();

        // Map terrain to theme for the feature system
        var theme = currentTerrain switch
        {
            DungeonTerrain.Underground => DungeonTheme.Catacombs,
            DungeonTerrain.Mountains => DungeonTheme.FrozenDepths,
            DungeonTerrain.Desert => DungeonTheme.AncientRuins,
            DungeonTerrain.Forest => DungeonTheme.Caverns,
            DungeonTerrain.Caves => DungeonTheme.Sewers,
            _ => DungeonTheme.Catacombs
        };

        // Use the new comprehensive feature interaction system
        var outcome = await FeatureInteractionSystem.Instance.InteractWithFeature(
            feature,
            player!,
            currentDungeonLevel,
            theme,
            terminal
        );

        // Post-hoc reward split: FeatureInteractionSystem already awarded full amount to leader.
        // Reduce leader to their share and distribute to teammates.
        if (teammates.Count > 0 && (outcome.GoldGained > 0 || outcome.ExperienceGained > 0))
        {
            int totalMembers = 1 + teammates.Count;
            long goldPerMember = outcome.GoldGained > 0 ? outcome.GoldGained / totalMembers : 0;

            // Split gold: reduce leader to their share, give rest to teammates
            if (outcome.GoldGained > 0)
            {
                player!.Gold -= (outcome.GoldGained - goldPerMember);
                foreach (var t in teammates.Where(t => t != null && t.IsAlive))
                    t.Gold += goldPerMember;
            }

            // Split XP and send personalized reward notifications
            int highestLevel = Math.Max(player!.Level, teammates.Max(t => t.Level));
            foreach (var t in teammates.Where(t => t != null && t.IsAlive))
            {
                if (t.IsGroupedPlayer)
                {
                    long xpShare = 0;
                    if (outcome.ExperienceGained > 0)
                    {
                        float mult = GroupSystem.GetGroupXPMultiplier(t.Level, highestLevel);
                        xpShare = (long)(outcome.ExperienceGained * mult);
                        t.Experience += xpShare;
                    }

                    // Notify grouped player of their share (gold, XP, or both)
                    var parts = new System.Collections.Generic.List<string>();
                    if (goldPerMember > 0) parts.Add($"+{goldPerMember:N0} gold");
                    if (xpShare > 0) parts.Add($"+{xpShare:N0} XP");
                    if (parts.Count > 0)
                    {
                        var session = GroupSystem.GetSession(t.GroupPlayerUsername ?? "");
                        session?.EnqueueMessage(
                            $"\u001b[1;32m  ═══ {feature.Name} (Your Share) ═══\u001b[0m\n" +
                            $"\u001b[33m  {string.Join("  ", parts)}\u001b[0m");
                    }
                }
                else if (!t.IsCompanion && !t.IsEcho)
                {
                    if (outcome.ExperienceGained > 0)
                        t.Experience += (long)(outcome.ExperienceGained * 0.75);
                }
            }

            // Broadcast interaction narrative with outcome details
            var featureSb = new System.Text.StringBuilder();
            featureSb.AppendLine($"\u001b[96m  {player?.Name2} examines {feature.Name}...\u001b[0m");
            if (outcome.LoreDiscovered)
                featureSb.AppendLine("\u001b[35m  Ancient lore discovered!\u001b[0m");
            if (outcome.DamageTaken > 0)
                featureSb.AppendLine($"\u001b[31m  It was dangerous! {player?.Name2} took {outcome.DamageTaken} damage.\u001b[0m");
            if (outcome.OceanInsightGained)
                featureSb.AppendLine("\u001b[36m  A moment of spiritual insight...\u001b[0m");
            BroadcastDungeonEvent(featureSb.ToString());
        }
        else if (teammates.Count > 0)
        {
            // No rewards, but still broadcast what happened
            var featureSb = new System.Text.StringBuilder();
            featureSb.AppendLine($"\u001b[96m  {player?.Name2} examines {feature.Name}...\u001b[0m");
            if (outcome.LoreDiscovered)
                featureSb.AppendLine("\u001b[35m  Ancient lore discovered!\u001b[0m");
            if (outcome.DamageTaken > 0)
                featureSb.AppendLine($"\u001b[31m  It was dangerous! {player?.Name2} took {outcome.DamageTaken} damage.\u001b[0m");
            if (outcome.OceanInsightGained)
                featureSb.AppendLine("\u001b[36m  A moment of spiritual insight...\u001b[0m");
            BroadcastDungeonEvent(featureSb.ToString());
        }
    }

    // Floors that require full clear before leaving
    private static readonly int[] SealFloors = { 15, 30, 45, 60, 80, 99 };
    private static readonly int[] SecretBossFloors = { 25, 50, 75, 99 };

    // Old God boss floors
    private static readonly int[] OldGodFloors = { 25, 40, 55, 70, 85, 95, 100 };

    // Combined list of all special floors for easy lookup (includes Old God floors)
    private static readonly int[] AllSpecialFloors = { 15, 25, 30, 40, 45, 50, 55, 60, 70, 75, 80, 85, 95, 99, 100 };

    // Rate-limit dungeon respawn broadcasts to one per 30 minutes
    private static DateTime _lastDungeonRespawnBroadcast = DateTime.MinValue;
    private static readonly object _respawnBroadcastLock = new();
    private static readonly string[] DungeonRespawnMessages = {
        "The dungeon trembles as dark magic pulls new horrors from the void...",
        "A cold wind howls through the dungeon depths. Something stirs below.",
        "The Old Gods' power seeps through ancient stone — the dungeon renews itself.",
        "Torches flicker and shadows shift. The dungeon has drawn new creatures to its halls.",
        "The earth groans as the dungeon's hunger calls forth fresh terrors from the deep.",
        "Whispers echo through forgotten corridors. The dead do not rest for long down here.",
        "A pulse of ancient energy ripples through the dungeon. New dangers await the bold.",
        "The dungeon breathes. Creatures crawl from cracks in reality, filling empty halls.",
        "Bones rattle and dust swirls — the dungeon's eternal cycle begins anew.",
        "The stones remember. The dungeon rebuilds its armies from shadow and spite.",
        "Something ancient and hungry has restocked the dungeon's larder with fresh nightmares.",
        "The seal-wards flare briefly. The dungeon's curse replenishes what adventurers have slain.",
    };

    /// <summary>
    /// Broadcast a random dungeon respawn flavor message to all players (rate-limited)
    /// </summary>
    private static void BroadcastDungeonRespawn()
    {
        lock (_respawnBroadcastLock)
        {
            if ((DateTime.Now - _lastDungeonRespawnBroadcast).TotalMinutes < 30)
                return;
            _lastDungeonRespawnBroadcast = DateTime.Now;
        }
        var msg = DungeonRespawnMessages[new Random().Next(DungeonRespawnMessages.Length)];
        try { NewsSystem.Instance?.Newsy(msg); } catch { }
    }

    /// <summary>
    /// Check if a floor has an Old God boss encounter
    /// </summary>
    private static bool IsOldGodFloor(int floorLevel)
    {
        return OldGodFloors.Contains(floorLevel);
    }

    /// <summary>
    /// Get the Old God type for a specific floor level
    /// </summary>
    private static OldGodType? GetOldGodForFloor(int floorLevel)
    {
        return floorLevel switch
        {
            25 => OldGodType.Maelketh,
            40 => OldGodType.Veloura,
            55 => OldGodType.Thorgrim,
            70 => OldGodType.Noctura,
            85 => OldGodType.Aurelion,
            95 => OldGodType.Terravok,
            100 => OldGodType.Manwe,
            _ => null
        };
    }

    /// <summary>
    /// Result of floor generation/restoration
    /// </summary>
    private struct FloorGenerationResult
    {
        public DungeonFloor Floor;
        public bool WasRestored;  // True if floor was restored from save
        public bool DidRespawn;   // True if monsters respawned (24h passed)
    }

    /// <summary>
    /// Generate a new floor or restore from saved state with respawn logic
    /// - If no saved state: generate fresh floor
    /// - If saved state exists and <24h: restore exactly as saved
    /// - If saved state exists and >24h: restore but respawn monsters (keep treasure looted)
    /// - Boss/seal floors: never respawn once cleared
    /// </summary>
    private FloorGenerationResult GenerateOrRestoreFloor(Character player, int floorLevel)
    {
        // Check if we have saved state for this floor
        if (player.DungeonFloorStates.TryGetValue(floorLevel, out var savedState))
        {
            // Generate fresh floor structure (layout is deterministic per level)
            var floor = DungeonGenerator.GenerateFloor(floorLevel);

            bool shouldRespawn = savedState.ShouldRespawn();

            // Restore room states
            foreach (var room in floor.Rooms)
            {
                if (savedState.RoomStates.TryGetValue(room.Id, out var roomState))
                {
                    room.IsExplored = roomState.IsExplored;

                    // Monster clear status respawns after 24h (unless permanent)
                    if (shouldRespawn && !savedState.IsPermanentlyClear)
                    {
                        // Monsters respawn - room is no longer cleared
                        room.IsCleared = false;
                    }
                    else
                    {
                        room.IsCleared = roomState.IsCleared;
                    }

                    // These are permanent - never respawn
                    room.TreasureLooted = roomState.TreasureLooted;
                    room.TrapTriggered = roomState.TrapTriggered;
                    room.EventCompleted = roomState.EventCompleted;
                    room.PuzzleSolved = roomState.PuzzleSolved;
                    room.RiddleAnswered = roomState.RiddleAnswered;
                    room.LoreCollected = roomState.LoreCollected;
                    room.InsightGranted = roomState.InsightGranted;
                    room.MemoryTriggered = roomState.MemoryTriggered;
                    room.SecretBossDefeated = roomState.SecretBossDefeated;
                }

                // CRITICAL: Boss rooms should NEVER be marked cleared unless the actual boss
                // was defeated. This prevents bugs where save corruption, non-deterministic
                // generation, or defeating non-boss-room monsters with IsBoss=true incorrectly
                // marks the boss room as cleared.
                if (room.IsBossRoom)
                {
                    if (IsOldGodFloor(floorLevel))
                    {
                        // Old God floors: Check if the god was actually resolved
                        var godType = GetOldGodForFloor(floorLevel);
                        if (godType != null)
                        {
                            var story = StoryProgressionSystem.Instance;
                            bool godResolved = story.OldGodStates.TryGetValue(godType.Value, out var state) &&
                                (state.Status == GodStatus.Defeated ||
                                 state.Status == GodStatus.Saved ||
                                 state.Status == GodStatus.Allied ||
                                 state.Status == GodStatus.Awakened ||
                                 state.Status == GodStatus.Consumed);

                            if (!godResolved)
                            {
                                // Force boss room to be uncleared if Old God wasn't resolved
                                room.IsCleared = false;
                            }
                        }
                    }
                    else
                    {
                        // Non-Old-God floors: Boss room should ONLY be cleared if we have proof
                        // that the actual boss room boss was defeated (BossDefeated flag).
                        // This prevents any false clears from mini-bosses or save corruption.
                        if (!savedState.BossDefeated)
                        {
                            // Boss was never actually defeated, force room to be uncleared
                            room.IsCleared = false;
                        }
                    }
                }
            }

            // Restore current room position
            if (!string.IsNullOrEmpty(savedState.CurrentRoomId))
            {
                floor.CurrentRoomId = savedState.CurrentRoomId;
            }

            // Update visit time
            savedState.LastVisitedAt = DateTime.Now;

            return new FloorGenerationResult
            {
                Floor = floor,
                WasRestored = true,
                DidRespawn = shouldRespawn && !savedState.IsPermanentlyClear
            };
        }

        // No saved state - generate fresh floor
        var newFloor = DungeonGenerator.GenerateFloor(floorLevel);

        // Create initial floor state
        bool isSpecialFloor = SealFloors.Contains(floorLevel) || SecretBossFloors.Contains(floorLevel);
        player.DungeonFloorStates[floorLevel] = new DungeonFloorState
        {
            FloorLevel = floorLevel,
            LastVisitedAt = DateTime.Now,
            IsPermanentlyClear = false, // Will be set true when cleared if special floor
            RoomStates = new Dictionary<string, DungeonRoomState>()
        };

        return new FloorGenerationResult
        {
            Floor = newFloor,
            WasRestored = false,
            DidRespawn = false
        };
    }

    /// <summary>
    /// Save current floor state to player's persistent data
    /// Called when leaving dungeon or changing floors
    /// </summary>
    private void SaveFloorState(Character player)
    {
        if (currentFloor == null || player == null) return;

        var floorLevel = currentDungeonLevel;

        // Get or create floor state
        if (!player.DungeonFloorStates.TryGetValue(floorLevel, out var floorState))
        {
            floorState = new DungeonFloorState { FloorLevel = floorLevel };
            player.DungeonFloorStates[floorLevel] = floorState;
        }

        floorState.LastVisitedAt = DateTime.Now;
        floorState.CurrentRoomId = currentFloor.CurrentRoomId;

        // Check if floor is now fully cleared
        bool isNowCleared = IsFloorCleared();
        if (isNowCleared && !floorState.EverCleared)
        {
            floorState.EverCleared = true;
            floorState.LastClearedAt = DateTime.Now;

            // Special floors stay permanently cleared
            if (SealFloors.Contains(floorLevel) || SecretBossFloors.Contains(floorLevel))
            {
                floorState.IsPermanentlyClear = true;
            }

            // Notify quest system that floor is cleared
            var questPlayer = GetCurrentPlayer();
            if (questPlayer != null)
            {
                QuestSystem.OnDungeonFloorCleared(questPlayer, floorLevel);
            }
        }

        // Save room states
        floorState.RoomStates.Clear();
        foreach (var room in currentFloor.Rooms)
        {
            floorState.RoomStates[room.Id] = new DungeonRoomState
            {
                RoomId = room.Id,
                IsExplored = room.IsExplored,
                IsCleared = room.IsCleared,
                TreasureLooted = room.TreasureLooted,
                TrapTriggered = room.TrapTriggered,
                EventCompleted = room.EventCompleted,
                PuzzleSolved = room.PuzzleSolved,
                RiddleAnswered = room.RiddleAnswered,
                LoreCollected = room.LoreCollected,
                InsightGranted = room.InsightGranted,
                MemoryTriggered = room.MemoryTriggered,
                SecretBossDefeated = room.SecretBossDefeated
            };
        }
    }

    /// <summary>
    /// Get the maximum floor the player can access based on Old God floor gates.
    /// Old God floors are hard gates - players MUST defeat the god before going deeper.
    /// Seal floors are NOT hard gates - players can skip seals (affects endings only).
    /// </summary>
    private int GetMaxAccessibleFloor(Character player, int requestedFloor)
    {
        if (player == null)
            return requestedFloor;

        // Only Old God floors are hard progression gates
        foreach (int godFloor in OldGodFloors.OrderBy(f => f))
        {
            // If player wants to go past this Old God floor
            if (requestedFloor > godFloor)
            {
                // Check if the Old God on this floor has been defeated/resolved
                var godType = GetOldGodForFloor(godFloor);
                bool isResolved = false;

                if (godType != null)
                {
                    var story = StoryProgressionSystem.Instance;
                    if (story.OldGodStates.TryGetValue(godType.Value, out var state))
                    {
                        isResolved = state.Status == GodStatus.Defeated ||
                                     state.Status == GodStatus.Saved ||
                                     state.Status == GodStatus.Allied ||
                                     state.Status == GodStatus.Awakened ||
                                     state.Status == GodStatus.Consumed;
                    }
                }

                // Also check ClearedSpecialFloors as a backup (for saves before Old God tracking)
                if (!isResolved)
                {
                    isResolved = player.ClearedSpecialFloors.Contains(godFloor);
                }

                // If Old God not defeated, cap access at this floor (player can reach the floor but not pass it)
                if (!isResolved)
                {
                    return Math.Min(requestedFloor, godFloor);
                }
            }
        }

        return requestedFloor;
    }

    /// <summary>
    /// Check if current floor requires clearing before the player can descend deeper.
    /// Old God floors are hard gates - players MUST defeat the god before progressing.
    /// Seal floors are soft gates - players can skip seals (affects endings only).
    /// </summary>
    private bool RequiresFloorClear()
    {
        return OldGodFloors.Contains(currentDungeonLevel);
    }

    /// <summary>
    /// Migration helper: Sync collected seals with ClearedSpecialFloors
    /// This fixes saves where seals were collected before the floor tracking fix
    /// </summary>
    private void SyncCollectedSealsWithClearedFloors(Character player)
    {
        var story = StoryProgressionSystem.Instance;
        var sealSystem = SevenSealsSystem.Instance;

        // Check each collected seal and ensure its floor is in ClearedSpecialFloors
        foreach (var sealType in story.CollectedSeals)
        {
            var sealData = sealSystem.GetSeal(sealType);
            if (sealData != null && sealData.DungeonFloor > 0)
            {
                if (!player.ClearedSpecialFloors.Contains(sealData.DungeonFloor))
                {
                    player.ClearedSpecialFloors.Add(sealData.DungeonFloor);
                }
            }
        }
    }

    /// <summary>
    /// Check if current floor is fully cleared
    /// For Old God floors: check if the god has been defeated/resolved
    /// For Seal floors: check if seal has been collected
    /// For other special floors: check if all monster rooms are cleared
    /// </summary>
    private bool IsFloorCleared()
    {
        if (currentFloor == null) return true;

        // Old God boss floors - check if the god has been defeated/resolved
        if (IsOldGodFloor(currentDungeonLevel))
        {
            var godType = GetOldGodForFloor(currentDungeonLevel);
            if (godType != null)
            {
                var story = StoryProgressionSystem.Instance;
                if (story.OldGodStates.TryGetValue(godType.Value, out var state))
                {
                    // God is resolved if defeated, saved, allied, awakened, or consumed
                    return state.Status == GodStatus.Defeated ||
                           state.Status == GodStatus.Saved ||
                           state.Status == GodStatus.Allied ||
                           state.Status == GodStatus.Awakened ||
                           state.Status == GodStatus.Consumed;
                }
                // God not yet encountered - floor not cleared
                return false;
            }
        }

        // Seal floors - check if the seal has been collected
        if (SealFloors.Contains(currentDungeonLevel))
        {
            var player = GetCurrentPlayer();
            if (player != null)
            {
                // Check if player has collected this seal
                return player.ClearedSpecialFloors.Contains(currentDungeonLevel);
            }
        }

        // Default: all monster rooms must be cleared
        return currentFloor.Rooms.All(r => !r.HasMonsters || r.IsCleared);
    }

    /// <summary>
    /// Get description of what remains to clear on the floor
    /// </summary>
    private string GetRemainingClearInfo()
    {
        if (currentFloor == null) return "";

        // Old God floors - show god status
        if (IsOldGodFloor(currentDungeonLevel))
        {
            var godType = GetOldGodForFloor(currentDungeonLevel);
            if (godType != null)
            {
                return $"The {godType.Value} awaits in the boss chamber";
            }
        }

        // Seal floors
        if (SealFloors.Contains(currentDungeonLevel))
        {
            return "The ancient seal must be claimed";
        }

        // Default: show monster room count
        int remaining = currentFloor.Rooms.Count(r => r.HasMonsters && !r.IsCleared);
        int total = currentFloor.Rooms.Count(r => r.HasMonsters);
        return $"{remaining} of {total} monster rooms remain uncleared";
    }

    /// <summary>
    /// Award bonus XP and gold for fully clearing a floor
    /// Only awards bonus on FIRST clear - respawned floors don't give bonus again
    /// </summary>
    private async Task AwardFloorCompletionBonus(Character player, int floorLevel)
    {
        // Check if the completion bonus has already been paid out for this floor
        // Note: EverCleared can be set by seal collection before the bonus is awarded,
        // so we use a separate CompletionBonusAwarded flag to track payout.
        if (player.DungeonFloorStates.TryGetValue(floorLevel, out var floorState) && floorState.CompletionBonusAwarded)
        {
            terminal.WriteLine("");
            terminal.WriteLine(Loc.Get("dungeon.floor_recleared"), "gray");
            terminal.WriteLine(Loc.Get("dungeon.first_clear_only"), "darkgray");
            terminal.WriteLine("");
            await Task.Delay(1500);
            return;
        }

        // Base bonus scales with floor level
        int baseXP = (int)(50 * Math.Pow(floorLevel, 1.2));
        int baseGold = (int)(25 * Math.Pow(floorLevel, 1.15));

        // Boss/seal floors give 3x bonus
        bool isSpecialFloor = SealFloors.Contains(floorLevel) || SecretBossFloors.Contains(floorLevel);
        float multiplier = isSpecialFloor ? 3.0f : 1.0f;

        int xpBonus = (int)(baseXP * multiplier);
        int goldBonus = (int)(baseGold * multiplier);

        // Award the bonus
        var (clearXP, clearGold) = AwardDungeonReward(xpBonus, goldBonus, "Floor Cleared");

        // Display the bonus
        terminal.WriteLine("");
        if (isSpecialFloor)
        {
            WriteBoxHeader(Loc.Get("dungeon.floor_conquered"), "bright_yellow", 38);
            terminal.WriteLine(Loc.Get("dungeon.proven_sacred_floor"), "bright_magenta");
        }
        else
        {
            WriteSectionHeader(Loc.Get("dungeon.section_floor_cleared"), "bright_green");
            terminal.WriteLine(Loc.Get("dungeon.vanquished_all_foes"), "green");
        }
        if (teammates.Count > 0)
        {
            terminal.WriteLine(Loc.Get("dungeon.bonus_xp_share", $"{xpBonus:N0}", $"{clearXP:N0}"), "bright_cyan");
            terminal.WriteLine(Loc.Get("dungeon.bonus_gold_share", $"{goldBonus:N0}", $"{clearGold:N0}"), "bright_yellow");
            BroadcastDungeonEvent($"\u001b[1;33m  ═══ FLOOR CLEARED ═══\u001b[0m");
        }
        else
        {
            terminal.WriteLine(Loc.Get("dungeon.bonus_xp", $"{xpBonus:N0}"), "bright_cyan");
            terminal.WriteLine(Loc.Get("dungeon.bonus_gold", $"{goldBonus:N0}"), "bright_yellow");
        }
        terminal.WriteLine("");

        // Mark special floors as cleared in persistent player data
        if (isSpecialFloor)
        {
            player.ClearedSpecialFloors.Add(floorLevel);
        }

        // Mark the completion bonus as paid so we don't award it again on re-clears
        if (!player.DungeonFloorStates.TryGetValue(floorLevel, out var bonusFloorState))
        {
            bonusFloorState = new DungeonFloorState { FloorLevel = floorLevel };
            player.DungeonFloorStates[floorLevel] = bonusFloorState;
        }
        bonusFloorState.CompletionBonusAwarded = true;

        await Task.Delay(2500);
    }

    /// <summary>
    /// Descend to the next floor
    /// </summary>
    private async Task DescendStairs()
    {
        var player = GetCurrentPlayer();
        var playerLevel = player?.Level ?? 1;
        int maxAccessible = Math.Min(maxDungeonLevel, playerLevel + 10);

        // Old God floors are hard gates - must defeat the god before descending
        if (RequiresFloorClear() && !IsFloorCleared())
        {
            terminal.WriteLine("", "red");
            terminal.WriteLine(Loc.Get("dungeon.presence_blocks_descent"), "bright_red");
            terminal.WriteLine(Loc.Get("dungeon.defeat_old_god_first"), "yellow");
            terminal.WriteLine($"({GetRemainingClearInfo()})", "gray");
            terminal.WriteLine(Loc.Get("dungeon.may_ascend"), "cyan");
            await Task.Delay(2500);
            return;
        }

        // Check level restriction (player level + 10)
        if (currentDungeonLevel >= maxAccessible)
        {
            terminal.WriteLine(Loc.Get("dungeon.cannot_venture_deeper", maxAccessible), "yellow");
            terminal.WriteLine(Loc.Get("dungeon.level_up_hint"), "gray");
            await Task.Delay(2000);
            return;
        }

        if (currentDungeonLevel >= maxDungeonLevel)
        {
            terminal.WriteLine(Loc.Get("dungeon.deepest_level"), "red");
            terminal.WriteLine(Loc.Get("dungeon.nowhere_descend"), "yellow");
            await Task.Delay(2000);
            return;
        }

        // Award floor completion bonus if fully cleared (optional for non-boss floors)
        if (IsFloorCleared() && player != null)
        {
            await AwardFloorCompletionBonus(player, currentDungeonLevel);
        }

        // Save current floor state before leaving
        if (player != null)
        {
            SaveFloorState(player);
        }

        // Dungeon descent broadcast removed — too spammy with multiple players online.

        // Screen reader mode: skip ClearScreen to avoid double separators.
        // Transition text flows naturally; DisplayLocation adds one clean separator.
        if (!GameConfig.ScreenReaderMode)
            terminal.ClearScreen();
        terminal.SetColor("blue");
        terminal.WriteLine(Loc.Get("dungeon.descend_stairs"));
        terminal.WriteLine(Loc.Get("dungeon.darkness_deeper"));
        terminal.WriteLine(Loc.Get("dungeon.air_colder"));
        await Task.Delay(2000);

        // Generate or restore the next floor
        int nextLevel = currentDungeonLevel + 1;
        var floorResult = GenerateOrRestoreFloor(player, nextLevel);
        currentFloor = floorResult.Floor;
        currentDungeonLevel = nextLevel;
        if (player != null) player.CurrentLocation = $"Dungeon Floor {currentDungeonLevel}";
        roomsExploredThisFloor = floorResult.WasRestored ? currentFloor.Rooms.Count(r => r.IsExplored) : 0;
        hasCampedThisFloor = false;
        consecutiveMonsterRooms = 0;

        // Update quest progress for reaching this floor
        if (player != null)
        {
            QuestSystem.OnDungeonFloorReached(player, currentDungeonLevel);
        }

        // Start in entrance room (or restored position)
        if (!floorResult.WasRestored || string.IsNullOrEmpty(currentFloor.CurrentRoomId))
        {
            currentFloor.CurrentRoomId = currentFloor.EntranceRoomId;
            var entranceRoom = currentFloor.GetCurrentRoom();
            if (entranceRoom != null)
            {
                entranceRoom.IsExplored = true;
                roomsExploredThisFloor++;
                // Auto-clear rooms without monsters
                if (!entranceRoom.HasMonsters)
                {
                    entranceRoom.IsCleared = true;
                }
            }
        }

        terminal.SetColor(GetThemeColor(currentFloor.Theme));
        terminal.WriteLine("");
        terminal.WriteLine(Loc.Get("dungeon.arrive_at_level", currentDungeonLevel));
        terminal.WriteLine(Loc.Get("dungeon.theme_display", currentFloor.Theme));
        BroadcastDungeonEvent($"\u001b[34m  The group descends to Floor {currentDungeonLevel} ({currentFloor.Theme}).\u001b[0m");

        // Keep group state in sync
        var descGroup = GroupSystem.Instance?.GetGroupFor(SessionContext.Current?.Username ?? "");
        if (descGroup != null) descGroup.CurrentFloor = currentDungeonLevel;

        // Show followers the entrance room on the new floor
        var newFloorRoom = currentFloor.GetRoom(currentFloor.CurrentRoomId);
        if (newFloorRoom != null) PushRoomToFollowers(newFloorRoom);

        // Show restoration status
        if (floorResult.WasRestored && !floorResult.DidRespawn)
        {
            terminal.SetColor("bright_green");
            terminal.WriteLine(Loc.Get("dungeon.continuing_where_left_off"));
        }
        else if (floorResult.DidRespawn)
        {
            terminal.SetColor("yellow");
            terminal.WriteLine(Loc.Get("dungeon.new_creatures_emerged"));
            BroadcastDungeonRespawn();
        }

        terminal.WriteLine("");
        terminal.SetColor("gray");
        terminal.WriteLine(GetFloorFlavorText(currentFloor.Theme));

        // Check for story events (seals, narrative moments) on this new floor
        await CheckFloorStoryEvents(player, terminal);

        await Task.Delay(2500);
        RequestRedisplay();
    }

    /// <summary>
    /// Rest in a cleared room (once per floor)
    /// </summary>
    private async Task RestInRoom()
    {
        var player = GetCurrentPlayer();

        if (!GameConfig.ScreenReaderMode)
            terminal.ClearScreen();
        terminal.SetColor("green");
        terminal.WriteLine(Loc.Get("dungeon.make_camp"));
        terminal.WriteLine("");
        await Task.Delay(1500);

        // Blood Price rest penalty — dark memories reduce rest effectiveness
        float restEfficiency = 1.0f;
        if (player.MurderWeight >= 6f) restEfficiency = 0.50f;
        else if (player.MurderWeight >= 3f) restEfficiency = 0.75f;

        // Heal 25% of max HP (reduced by murder weight)
        long healAmount = (long)(player.MaxHP / 4 * restEfficiency);
        player.HP = Math.Min(player.MaxHP, player.HP + healAmount);

        // Recover 25% of max Mana
        long manaAmount = (long)(player.MaxMana / 4 * restEfficiency);
        player.Mana = Math.Min(player.MaxMana, player.Mana + manaAmount);

        // Recover 25% of max Combat Stamina
        long staminaAmount = (long)(player.MaxCombatStamina / 4 * restEfficiency);
        player.CurrentCombatStamina = Math.Min(player.MaxCombatStamina, player.CurrentCombatStamina + staminaAmount);

        terminal.WriteLine(Loc.Get("dungeon.recover_hp", healAmount));
        if (manaAmount > 0)
            terminal.WriteLine(Loc.Get("dungeon.recover_mana", manaAmount));
        if (staminaAmount > 0)
            terminal.WriteLine(Loc.Get("dungeon.recover_stamina", staminaAmount));

        // Heal grouped followers (+25% HP/MP/ST, no blood price penalty)
        if (teammates.Count > 0)
        {
            List<Character> teamSnap;
            lock (teammates) { teamSnap = new List<Character>(teammates); }

            foreach (var mate in teamSnap)
            {
                if (mate == null || !mate.IsAlive) continue;

                long mateHeal = mate.MaxHP / 4;
                long mateMana = mate.MaxMana / 4;
                long mateSta = mate.MaxCombatStamina / 4;

                mate.HP = Math.Min(mate.MaxHP, mate.HP + mateHeal);
                mate.Mana = Math.Min(mate.MaxMana, mate.Mana + mateMana);
                mate.CurrentCombatStamina = Math.Min(mate.MaxCombatStamina, mate.CurrentCombatStamina + mateSta);

                if (mate.IsGroupedPlayer)
                {
                    var session = GroupSystem.GetSession(mate.GroupPlayerUsername ?? "");
                    if (session != null)
                    {
                        var sb = new System.Text.StringBuilder();
                        sb.AppendLine("\u001b[32m  The group makes camp in a defensible position...\u001b[0m");
                        sb.AppendLine($"\u001b[32m  You recover {mateHeal:N0} hit points.\u001b[0m");
                        if (mateMana > 0)
                            sb.AppendLine($"\u001b[32m  You recover {mateMana:N0} mana.\u001b[0m");
                        if (mateSta > 0)
                            sb.AppendLine($"\u001b[32m  You recover {mateSta:N0} stamina.\u001b[0m");
                        sb.Append($"\u001b[36m  HP: {mate.HP}/{mate.MaxHP}  MP: {mate.Mana}/{mate.MaxMana}  ST: {mate.CurrentCombatStamina}/{mate.MaxCombatStamina}\u001b[0m");
                        session.EnqueueMessage(sb.ToString());
                    }
                }
            }
        }

        if (restEfficiency < 1.0f)
        {
            terminal.SetColor("dark_red");
            terminal.WriteLine(Loc.Get("dungeon.troubled_rest"));
        }

        terminal.WriteLine("");
        terminal.SetColor("cyan");
        terminal.WriteLine($"{Loc.Get("combat.bar_hp")}: {player.HP}/{player.MaxHP}  {Loc.Get("combat.bar_mp")}: {player.Mana}/{player.MaxMana}  {Loc.Get("combat.bar_st")}: {player.CurrentCombatStamina}/{player.MaxCombatStamina}");

        hasCampedThisFloor = true;

        // Advance game time for camping (single-player only)
        if (!UsurperRemake.BBS.DoorMode.IsOnlineMode)
        {
            DailySystemManager.Instance.AdvanceGameTime(player, 120); // 2 hours of rest

            // Reduce fatigue from dungeon camping
            int oldFatigue = player.Fatigue;
            player.Fatigue = Math.Max(0, player.Fatigue - GameConfig.FatigueReductionDungeonRest);
            if (oldFatigue > 0 && player.Fatigue < oldFatigue)
                terminal.WriteLine(Loc.Get("dungeon.fatigue_reduced", oldFatigue - player.Fatigue), "bright_green");
        }

        // Check for nightmares in the dungeon
        var dream = UsurperRemake.Systems.DreamSystem.Instance.GetDreamForRest(player, currentDungeonLevel);
        if (dream != null)
        {
            terminal.WriteLine("");
            terminal.SetColor("dark_magenta");
            terminal.WriteLine(Loc.Get("dungeon.dream_takes_shape"));
            terminal.WriteLine("");
            await Task.Delay(1500);

            terminal.SetColor("bright_magenta");
            terminal.WriteLine($"=== {dream.Title} ===");
            terminal.WriteLine("");

            terminal.SetColor("magenta");
            foreach (var line in dream.Content)
            {
                terminal.WriteLine($"  {line}");
                await Task.Delay(1200);
            }

            if (!string.IsNullOrEmpty(dream.PhilosophicalHint))
            {
                terminal.WriteLine("");
                terminal.SetColor("dark_cyan");
                terminal.WriteLine($"  ({dream.PhilosophicalHint})");
            }

            terminal.WriteLine("");
            UsurperRemake.Systems.DreamSystem.Instance.ExperienceDream(dream.Id);
        }
        else
        {
            terminal.WriteLine("");
            terminal.SetColor("gray");
            terminal.WriteLine(Loc.Get("dungeon.break_camp"));
        }

        await Task.Delay(2500);
        await terminal.PressAnyKey();
    }

    /// <summary>
    /// Change dungeon level from overview
    /// </summary>
    private async Task ChangeDungeonLevel()
    {
        var playerLevel = GetCurrentPlayer()?.Level ?? 1;

        // Players can always ascend (go up) to any floor, but can only descend to playerLevel + 10
        int maxAccessible = Math.Min(maxDungeonLevel, playerLevel + 10);

        terminal.WriteLine("");
        terminal.WriteLine(Loc.Get("dungeon.current_level", currentDungeonLevel), "white");
        terminal.WriteLine(Loc.Get("dungeon.your_level", playerLevel), "cyan");
        terminal.WriteLine(Loc.Get("dungeon.deepest_accessible", maxAccessible), "yellow");
        terminal.WriteLine("");

        var input = await terminal.GetInput(Loc.Get("dungeon.enter_target_level"));

        int targetLevel = currentDungeonLevel;

        if (input.StartsWith("+") && int.TryParse(input.Substring(1), out int plus))
        {
            targetLevel = currentDungeonLevel + plus;
        }
        else if (input.StartsWith("-") && int.TryParse(input.Substring(1), out int minus))
        {
            targetLevel = currentDungeonLevel - minus;
        }
        else if (int.TryParse(input, out int absolute))
        {
            targetLevel = absolute;
        }

        // Players can always go up (min floor 1), but can't descend past playerLevel + 10
        targetLevel = Math.Max(1, Math.Min(maxAccessible, targetLevel));

        // Cap at the first undefeated Old God floor between current position and target
        int requestedTarget = targetLevel;
        if (targetLevel > currentDungeonLevel)
        {
            var player2 = GetCurrentPlayer();
            if (player2 != null)
            {
                targetLevel = GetMaxAccessibleFloor(player2, targetLevel);
            }
        }

        // If GetMaxAccessibleFloor capped the target (Old God gate), explain why
        if (targetLevel < requestedTarget && targetLevel <= currentDungeonLevel)
        {
            // Find which Old God floor is blocking
            var blockingFloor = OldGodFloors.OrderBy(f => f).FirstOrDefault(f => f >= currentDungeonLevel && f <= requestedTarget);
            var blockingGod = blockingFloor > 0 ? GetOldGodForFloor(blockingFloor) : null;
            var godName = blockingGod != null ? blockingGod.Value.ToString() : "an Old God";

            terminal.WriteLine("", "red");
            terminal.WriteLine(Loc.Get("dungeon.presence_blocks"), "bright_red");
            terminal.WriteLine(Loc.Get("dungeon.must_defeat_god_floor", godName, blockingFloor), "yellow");
            terminal.WriteLine(Loc.Get("dungeon.may_still_ascend"), "cyan");
            await Task.Delay(2500);
            return;
        }

        // Check if trying to DESCEND past an Old God floor that hasn't been cleared
        // Players CAN ascend (retreat to prepare) but CANNOT descend (skip the god) until defeated
        if (targetLevel > currentDungeonLevel && RequiresFloorClear() && !IsFloorCleared())
        {
            terminal.WriteLine("", "red");
            terminal.WriteLine(Loc.Get("dungeon.presence_blocks"), "bright_red");
            terminal.WriteLine(Loc.Get("dungeon.must_defeat_old_god_this_floor"), "yellow");
            terminal.WriteLine($"({GetRemainingClearInfo()})", "gray");
            terminal.WriteLine(Loc.Get("dungeon.may_still_ascend"), "cyan");
            await Task.Delay(2500);
            return;
        }

        if (targetLevel != currentDungeonLevel)
        {
            var player = GetCurrentPlayer();

            // Save current floor state before leaving
            if (player != null)
            {
                SaveFloorState(player);
            }

            // Generate or restore the target floor
            int previousLevel = currentDungeonLevel;
            var floorResult = GenerateOrRestoreFloor(player, targetLevel);
            currentFloor = floorResult.Floor;
            currentDungeonLevel = targetLevel;
            if (player != null) player.CurrentLocation = $"Dungeon Floor {currentDungeonLevel}";
            roomsExploredThisFloor = floorResult.WasRestored ? currentFloor.Rooms.Count(r => r.IsExplored) : 0;
            hasCampedThisFloor = false;
            consecutiveMonsterRooms = 0;

            // Log floor change
            UsurperRemake.Systems.DebugLogger.Instance.LogDungeonFloorChange(player?.Name ?? "Player", previousLevel, currentDungeonLevel);

            // Update quest progress for reaching this floor
            if (player != null)
            {
                QuestSystem.OnDungeonFloorReached(player, currentDungeonLevel);
            }

            terminal.WriteLine(Loc.Get("dungeon.level_set_to", currentDungeonLevel), "green");

            // Show restoration status
            if (floorResult.WasRestored && !floorResult.DidRespawn)
            {
                terminal.WriteLine(Loc.Get("dungeon.continuing_where_left_off"), "bright_green");
            }
            else if (floorResult.DidRespawn)
            {
                terminal.WriteLine(Loc.Get("dungeon.new_creatures_emerged"), "yellow");
                BroadcastDungeonRespawn();
            }

            // Check for story events (seals, narrative moments) on this new floor
            if (player != null)
            {
                await CheckFloorStoryEvents(player, terminal);
            }
        }
        else
        {
            terminal.WriteLine(Loc.Get("dungeon.no_level_change"), "gray");
        }

        await Task.Delay(1500);
        RequestRedisplay();
    }

    /// <summary>
    /// Main exploration mechanic - Pascal encounter system
    /// </summary>
    private async Task ExploreLevel()
    {
        var currentPlayer = GetCurrentPlayer();

        // No turn/fight limits in the new persistent system - explore freely!

        if (!GameConfig.ScreenReaderMode)
            terminal.ClearScreen();
        WriteSectionHeader(Loc.Get("dungeon.section_exploring"), "yellow");
        terminal.WriteLine("");

        // Atmospheric exploration text
        await ShowExplorationText();

        // Determine encounter type: 90% monsters, 10% special events
        var encounterRoll = dungeonRandom.NextDouble();

        if (encounterRoll < MonsterEncounterChance)
        {
            await MonsterEncounter();
        }
        else
        {
            await SpecialEventEncounter();
        }

        await Task.Delay(1000);
        await terminal.PressAnyKey();
    }
    
    /// <summary>
    /// Monster encounter - Pascal DUNGEVC.PAS mechanics
    /// </summary>
    private async Task MonsterEncounter()
    {
        terminal.SetColor("red");
        terminal.WriteLine(GameConfig.ScreenReaderMode ? Loc.Get("dungeon.monster_encounter_sr") : Loc.Get("dungeon.monster_encounter_visual"));
        terminal.WriteLine("");

        // Use new MonsterGenerator to create level-appropriate monsters
        var monsters = MonsterGenerator.GenerateMonsterGroup(currentDungeonLevel, dungeonRandom);

        var combatEngine = new CombatEngine(terminal);

        // Display encounter message with color
        if (monsters.Count == 1)
        {
            var monster = monsters[0];
            if (monster.IsBoss)
            {
                terminal.SetColor("bright_red");
                terminal.WriteLine(GameConfig.ScreenReaderMode
                    ? Loc.Get("dungeon.boss_blocks_path_sr", $"[{monster.MonsterColor}]{monster.Name}[/]")
                    : Loc.Get("dungeon.boss_blocks_path_visual", $"[{monster.MonsterColor}]{monster.Name}[/]"));
            }
            else
            {
                terminal.SetColor(monster.MonsterColor);
                terminal.WriteLine(Loc.Get("dungeon.monster_appears", $"[{monster.MonsterColor}]{monster.Name}[/]"));
            }
        }
        else
        {
            // Group monsters by name to handle mixed encounters properly
            var monsterGroups = monsters.GroupBy(m => m.Name)
                .Select(g => new { Name = g.Key, Count = g.Count(), Color = g.First().MonsterColor })
                .ToList();

            terminal.SetColor("yellow");
            if (monsterGroups.Count == 1)
            {
                // All monsters are the same type
                var group = monsterGroups[0];
                string plural = group.Count > 1 ? GetPluralName(group.Name) : group.Name;
                if (monsters[0].FamilyName != "")
                {
                    terminal.Write(Loc.Get("dungeon.encounter_group_family", $"[{group.Color}]{group.Count} {plural}[/]", monsters[0].FamilyName));
                }
                else
                {
                    terminal.Write(Loc.Get("dungeon.encounter_group", $"[{group.Color}]{group.Count} {plural}[/]"));
                }
            }
            else
            {
                // Mixed encounter - show all monster types
                terminal.Write(Loc.Get("dungeon.you_encounter"));
                for (int i = 0; i < monsterGroups.Count; i++)
                {
                    var group = monsterGroups[i];
                    string plural = group.Count > 1 ? GetPluralName(group.Name) : group.Name;

                    if (i > 0 && i == monsterGroups.Count - 1)
                        terminal.Write(Loc.Get("dungeon.and_separator"));
                    else if (i > 0)
                        terminal.Write(", ");

                    terminal.Write($"[{group.Color}]{group.Count} {plural}[/]");
                }
                terminal.Write("!");
            }
            terminal.WriteLine("");
        }

        // Show difficulty assessment
        var currentPlayer = GetCurrentPlayer();
        ShowDifficultyAssessment(monsters, currentPlayer);

        terminal.WriteLine("");
        await Task.Delay(2000);

        // Check for divine punishment before combat
        var (punishmentApplied, damageModifier, defenseModifier) = await CheckDivinePunishment(currentPlayer);

        // Apply temporary combat penalties from divine wrath
        int originalTempAttackBonus = currentPlayer.TempAttackBonus;
        int originalTempDefenseBonus = currentPlayer.TempDefenseBonus;
        if (punishmentApplied)
        {
            currentPlayer.TempAttackBonus -= Math.Abs(damageModifier) * 2;
            currentPlayer.TempDefenseBonus -= Math.Abs(defenseModifier) * 2;
        }

        // Use new PlayerVsMonsters method - ALL monsters fight at once!
        // Monk will appear after ALL monsters are defeated
        var combatResult = await combatEngine.PlayerVsMonsters(currentPlayer, monsters, teammates, offerMonkEncounter: true);

        // Restore original temp bonuses after combat
        if (punishmentApplied)
        {
            currentPlayer.TempAttackBonus = originalTempAttackBonus;
            currentPlayer.TempDefenseBonus = originalTempDefenseBonus;
        }

        // Check if player should return to temple after resurrection
        if (combatResult.ShouldReturnToTemple)
        {
            terminal.SetColor("yellow");
            terminal.WriteLine(Loc.Get("dungeon.awaken_temple"));
            await Task.Delay(2000);
            await NavigateToLocation(GameLocation.Temple);
            return;
        }
    }

    /// <summary>
    /// Show difficulty assessment before combat
    /// </summary>
    private void ShowDifficultyAssessment(List<Monster> monsters, Character player)
    {
        // Calculate total monster threat
        long totalMonsterHP = monsters.Sum(m => m.HP);
        long totalMonsterStr = monsters.Sum(m => m.Strength);
        int avgMonsterLevel = (int)monsters.Average(m => m.Level);

        // Calculate player power
        long playerPower = player.Strength + player.WeapPow + (player.Level * 5);
        long monsterPower = totalMonsterStr + avgMonsterLevel * 5;

        // Estimate difficulty
        float powerRatio = monsterPower > 0 ? (float)playerPower / monsterPower : 2f;
        float hpRatio = player.MaxHP > 0 ? (float)totalMonsterHP / player.MaxHP : 1f;

        string difficulty;
        string diffColor;
        string xpHint;

        // Calculate estimated XP
        long estXP = monsters.Sum(m => (long)(Math.Pow(m.Level, 1.5) * 15));
        estXP = DifficultySystem.ApplyExperienceMultiplier(estXP);

        if (powerRatio > 2.0f && hpRatio < 0.5f)
        {
            difficulty = Loc.Get("dungeon.difficulty_trivial");
            diffColor = "darkgray";
            xpHint = Loc.Get("dungeon.xp_hint_not_worth", estXP);
        }
        else if (powerRatio > 1.5f && hpRatio < 1.0f)
        {
            difficulty = Loc.Get("dungeon.difficulty_easy");
            diffColor = "bright_green";
            xpHint = $"~{estXP} XP";
        }
        else if (powerRatio > 1.0f && hpRatio < 1.5f)
        {
            difficulty = Loc.Get("dungeon.difficulty_fair");
            diffColor = "green";
            xpHint = $"~{estXP} XP";
        }
        else if (powerRatio > 0.7f && hpRatio < 2.5f)
        {
            difficulty = Loc.Get("dungeon.difficulty_challenging");
            diffColor = "yellow";
            xpHint = Loc.Get("dungeon.xp_hint_bring_potions", estXP);
        }
        else if (powerRatio > 0.5f)
        {
            difficulty = Loc.Get("dungeon.difficulty_dangerous");
            diffColor = "bright_yellow";
            xpHint = Loc.Get("dungeon.xp_hint_high_risk", estXP);
        }
        else
        {
            difficulty = Loc.Get("dungeon.difficulty_deadly");
            diffColor = "bright_red";
            xpHint = Loc.Get("dungeon.xp_hint_flee", estXP);
        }

        terminal.WriteLine("");
        terminal.SetColor("gray");
        terminal.Write(Loc.Get("dungeon.threat_label"));
        terminal.SetColor(diffColor);
        terminal.Write(difficulty);
        terminal.SetColor("gray");
        terminal.WriteLine($"  |  {xpHint}");
    }
    
    /// <summary>
    /// Magic scroll encounter - Pascal scroll mechanics
    /// </summary>
    private async Task HandleMagicScroll()
    {
        terminal.WriteLine(Loc.Get("dungeon.scroll_found"));
        terminal.WriteLine("");

        var scrollType = dungeonRandom.Next(3);
        var currentPlayer = GetCurrentPlayer();

        switch (scrollType)
        {
            case 0: // Blessing scroll
                terminal.SetColor("bright_white");
                terminal.WriteLine(Loc.Get("dungeon.scroll_blessing_words"));
                terminal.WriteLine(Loc.Get("dungeon.scroll_blessing_hint"));
                break;

            case 1: // Undead summon scroll
                terminal.SetColor("red");
                terminal.WriteLine(Loc.Get("dungeon.scroll_undead_words"));
                terminal.WriteLine(Loc.Get("dungeon.scroll_undead_hint"));
                break;

            case 2: // Secret cave scroll
                terminal.SetColor("cyan");
                terminal.WriteLine(Loc.Get("dungeon.scroll_secret_words"));
                terminal.WriteLine(Loc.Get("dungeon.scroll_secret_hint"));
                break;
        }

        terminal.WriteLine("");
        var recite = await terminal.GetInput(Loc.Get("dungeon.recite_scroll_prompt"));
        
        if (recite.ToUpper() == "Y")
        {
            await ExecuteScrollMagic(scrollType, currentPlayer);
        }
        else
        {
            terminal.WriteLine(Loc.Get("dungeon.scroll_stored"), "gray");
        }
    }
    
    /// <summary>
    /// Execute scroll magic effects
    /// </summary>
    private async Task ExecuteScrollMagic(int scrollType, Character player)
    {
        terminal.WriteLine("");
        terminal.WriteLine(Loc.Get("dungeon.scroll_words_resonate"), "bright_white");
        await Task.Delay(2000);

        switch (scrollType)
        {
            case 0: // Blessing
                {
                    long chivalryGain = dungeonRandom.Next(500) + 50;
                    player.Chivalry += chivalryGain;

                    terminal.SetColor("bright_yellow");
                    terminal.WriteLine(Loc.Get("dungeon.scroll_divine_light"));
                    terminal.WriteLine(Loc.Get("dungeon.scroll_chivalry_gain", chivalryGain));
                }
                break;

            case 1: // Undead summon (triggers combat)
                {
                    terminal.SetColor("red");
                    terminal.WriteLine(Loc.Get("dungeon.scroll_ground_trembles"));
                    await Task.Delay(2000);

                    // Create undead monster
                    var undead = CreateUndeadMonster();
                    terminal.WriteLine(Loc.Get("dungeon.scroll_summoned_undead", undead.Name));
                    
                    // Fight the undead
                    var combatEngine = new CombatEngine(terminal);
                    var combatResult = await combatEngine.PlayerVsMonster(player, undead, teammates);

                    // Check if player should return to temple after resurrection
                    if (combatResult.ShouldReturnToTemple)
                    {
                        terminal.SetColor("yellow");
                        terminal.WriteLine(Loc.Get("dungeon.awaken_temple"));
                        await Task.Delay(2000);
                        await NavigateToLocation(GameLocation.Temple);
                        return;
                    }
                }
                break;
                
            case 2: // Secret opportunity
                {
                    terminal.SetColor("bright_cyan");
                    terminal.WriteLine(Loc.Get("dungeon.scroll_hidden_passage"));

                    // Track secret found for achievements
                    player.Statistics.RecordSecretFound();

                    long bonusGold = currentDungeonLevel * 2000;
                    player.Gold += bonusGold;

                    terminal.WriteLine(Loc.Get("dungeon.scroll_secret_gold", bonusGold));
                }
                break;
        }
    }
    
    /// <summary>
    /// Special event encounters - Based on Pascal DUNGEVC.PAS and DUNGEV2.PAS
    /// Includes positive, negative, and neutral events for variety
    /// </summary>
    private async Task SpecialEventEncounter()
    {
        // 16 different event types (12 original + 4 new mid-game events)
        var eventType = dungeonRandom.Next(16);

        switch (eventType)
        {
            case 0:
                await TreasureChestEncounter();
                break;
            case 1:
                await PotionCacheEncounter();
                break;
            case 2:
                await MerchantEncounter();
                break;
            case 3:
                await WitchDoctorEncounter();
                break;
            case 4:
                await BeggarEncounter();
                break;
            case 5:
                await StrangersEncounter();
                break;
            case 6:
                await HarassedWomanEncounter();
                break;
            case 7:
                await WoundedManEncounter();
                break;
            case 8:
                await MysteriousShrine();
                break;
            case 9:
                await TrapEncounter();
                break;
            case 10:
                await AncientScrollEncounter();
                break;
            case 11:
                await GamblingGhostEncounter();
                break;
            case 12:
                await FallenAdventurerEncounter();
                break;
            case 13:
                await EchoingVoicesEncounter();
                break;
            case 14:
                await MysteriousPortalEncounter();
                break;
            case 15:
                await ChallengingDuelistEncounter();
                break;
        }
    }

    /// <summary>
    /// Fallen adventurer encounter - discover a deceased adventurer with their journal
    /// </summary>
    private async Task FallenAdventurerEncounter()
    {
        terminal.SetColor("gray");
        terminal.WriteLine(Loc.Get("dungeon.fallen_adventurer_header"));
        terminal.WriteLine("");

        var currentPlayer = GetCurrentPlayer();
        var adventurerClasses = new[] { "warrior", "mage", "cleric", "rogue", "paladin" };
        var adventurerClass = adventurerClasses[dungeonRandom.Next(adventurerClasses.Length)];

        terminal.SetColor("white");
        terminal.WriteLine(Loc.Get("dungeon.fallen_adventurer_remains", adventurerClass));
        terminal.WriteLine(Loc.Get("dungeon.fallen_adventurer_journal"));
        terminal.WriteLine("");

        // Generate lore entries based on dungeon level
        string[] journalEntries;
        if (currentDungeonLevel < 30)
        {
            journalEntries = new[]
            {
                "\"Day 12: The creatures here grow stronger. I've heard whispers of something ancient below...\"",
                "\"I've discovered that certain monsters fear fire. Must remember this.\"",
                "\"The merchants in town warned me about these depths. They were right to be afraid.\"",
                "\"If anyone finds this: Defend often. Healing is precious. Don't fight tired.\""
            };
        }
        else if (currentDungeonLevel < 60)
        {
            journalEntries = new[]
            {
                "\"The Old Gods stir in the depths. I've felt their presence... watching.\"",
                "\"Power Attacks work well against the armored beasts here.\"",
                "\"Found a seal fragment. The temple above spoke of seven such seals...\"",
                "\"The dungeon seems to respond to those who show both mercy and might.\""
            };
        }
        else
        {
            journalEntries = new[]
            {
                "\"I've seen Manwe's throne. None should sit upon it lightly.\"",
                "\"The artifacts hidden here... they're keys to something greater.\"",
                "\"To any who read this: The true ending requires more than strength.\"",
                "\"I almost reached the bottom. Almost. Beware the god of the deep.\""
            };
        }

        terminal.SetColor("cyan");
        terminal.WriteLine(journalEntries[dungeonRandom.Next(journalEntries.Length)]);
        terminal.WriteLine("");

        // Chance to find supplies
        var choice = await terminal.GetInput(Loc.Get("dungeon.fallen_adventurer_choice"));

        if (choice.ToUpper() == "S")
        {
            int roll = dungeonRandom.Next(100);
            if (roll < 40)
            {
                // Gold scaled to level - roughly 1-2 monster kills worth
                long goldFound = currentDungeonLevel * 30 + dungeonRandom.Next(currentDungeonLevel * 20);
                currentPlayer.Gold += goldFound;
                terminal.SetColor("green");
                terminal.WriteLine(Loc.Get("dungeon.find_gold_coins", goldFound));
            }
            else if (roll < 70)
            {
                int potions = dungeonRandom.Next(1, 4);
                currentPlayer.Healing = Math.Min(currentPlayer.Healing + potions, currentPlayer.MaxPotions);
                terminal.SetColor("magenta");
                terminal.WriteLine(Loc.Get("dungeon.find_healing_potions", potions));
            }
            else if (roll < 90)
            {
                // XP scaled to roughly 1 monster kill
                long xp = (long)(Math.Pow(currentDungeonLevel, 1.5) * 15);
                currentPlayer.Experience += xp;
                terminal.SetColor("cyan");
                terminal.WriteLine(Loc.Get("dungeon.notes_teach_something", xp));
            }
            else
            {
                terminal.SetColor("red");
                terminal.WriteLine(Loc.Get("dungeon.corpse_animates"));
                await Task.Delay(1000);

                var undead = Monster.CreateMonster(
                    currentDungeonLevel, $"Undead {adventurerClass.Substring(0,1).ToUpper() + adventurerClass.Substring(1)}",
                    currentDungeonLevel * 12, currentDungeonLevel * 3, 0,
                    "You will join me...", false, false, "Rusty Blade", "Tattered Armor",
                    false, false, currentDungeonLevel * 4, currentDungeonLevel * 2, currentDungeonLevel * 2
                );
                undead.Level = currentDungeonLevel;

                var combatEngine = new CombatEngine(terminal);
                var combatResult = await combatEngine.PlayerVsMonster(currentPlayer, undead, teammates);

                // Check if player should return to temple after resurrection
                if (combatResult.ShouldReturnToTemple)
                {
                    terminal.SetColor("yellow");
                    terminal.WriteLine(Loc.Get("dungeon.awaken_temple"));
                    await Task.Delay(2000);
                    await NavigateToLocation(GameLocation.Temple);
                    return;
                }
            }
        }
        else
        {
            terminal.SetColor("white");
            terminal.WriteLine(Loc.Get("dungeon.pray_for_fallen"));
            currentPlayer.Chivalry += 2;
            terminal.SetColor("green");
            terminal.WriteLine(Loc.Get("dungeon.chivalry_increases_slightly"));
        }

        await Task.Delay(1500);
    }

    /// <summary>
    /// Echoing voices encounter - hear whispers that reveal hints
    /// </summary>
    private async Task EchoingVoicesEncounter()
    {
        terminal.SetColor("magenta");
        terminal.WriteLine(Loc.Get("dungeon.echoing_voices_header"));
        terminal.WriteLine("");

        terminal.SetColor("white");
        terminal.WriteLine(Loc.Get("dungeon.whispers_echo"));
        terminal.WriteLine("");

        await Task.Delay(1500);

        // Different whispers based on floor and player state
        var currentPlayer = GetCurrentPlayer();
        string[] whispers;

        if (currentPlayer.HP < currentPlayer.MaxHP / 3)
        {
            whispers = new[]
            {
                "\"Rest... you need rest...\"",
                "\"The Inn above offers safety...\"",
                "\"Death awaits the weary...\""
            };
        }
        else if (currentDungeonLevel >= 80)
        {
            whispers = new[]
            {
                "\"Manwe watches from his throne...\"",
                "\"The seven seals... break them all...\"",
                "\"Will you usurp... or save...?\""
            };
        }
        else
        {
            whispers = new[]
            {
                "\"Deeper... the truth lies deeper...\"",
                "\"The Old Gods remember...\"",
                "\"Not all treasures are gold...\"",
                "\"Your companions may hold secrets...\"",
                "\"Power attacks break armor... precision finds weakness...\""
            };
        }

        terminal.SetColor("dark_gray");
        terminal.WriteLine(whispers[dungeonRandom.Next(whispers.Length)]);
        terminal.WriteLine("");

        // Small XP for experiencing the mystery
        long xpGain = currentDungeonLevel * 50;
        currentPlayer.Experience += xpGain;
        terminal.SetColor("cyan");
        terminal.WriteLine(Loc.Get("dungeon.encounter_wiser", xpGain));

        await Task.Delay(2000);
    }

    /// <summary>
    /// Mysterious portal encounter - quick travel or danger
    /// </summary>
    private async Task MysteriousPortalEncounter()
    {
        terminal.SetColor("blue");
        terminal.WriteLine(Loc.Get("dungeon.portal_header"));
        terminal.WriteLine("");

        terminal.SetColor("white");
        terminal.WriteLine(Loc.Get("dungeon.portal_description_1"));
        terminal.WriteLine(Loc.Get("dungeon.portal_description_2"));
        terminal.WriteLine("");

        var choice = await terminal.GetInput(Loc.Get("dungeon.portal_choice"));
        var currentPlayer = GetCurrentPlayer();

        if (choice.ToUpper() == "E")
        {
            int roll = dungeonRandom.Next(100);
            if (roll < 50)
            {
                // Teleport deeper (5-10 floors)
                int floorsDown = dungeonRandom.Next(5, 11);
                int newFloor = Math.Min(currentDungeonLevel + floorsDown, 100);
                terminal.SetColor("cyan");
                terminal.WriteLine(Loc.Get("dungeon.portal_whisks_deeper"));
                terminal.WriteLine(Loc.Get("dungeon.portal_emerge_floor", newFloor));
                currentDungeonLevel = newFloor;
                if (currentPlayer != null) currentPlayer.CurrentLocation = $"Dungeon Floor {currentDungeonLevel}";
            }
            else if (roll < 75)
            {
                // Teleport back up (safe)
                int floorsUp = dungeonRandom.Next(3, 8);
                int newFloor = Math.Max(currentDungeonLevel - floorsUp, 1);
                terminal.SetColor("yellow");
                terminal.WriteLine(Loc.Get("dungeon.portal_carries_upward"));
                terminal.WriteLine(Loc.Get("dungeon.portal_emerge_floor", newFloor));
                currentDungeonLevel = newFloor;
                if (currentPlayer != null) currentPlayer.CurrentLocation = $"Dungeon Floor {currentDungeonLevel}";
            }
            else if (roll < 90)
            {
                // Treasure dimension - rare event, give more generous gold (5-8 monster kills worth)
                terminal.SetColor("green");
                terminal.WriteLine(Loc.Get("dungeon.portal_treasure_dimension"));
                long goldFound = (long)(Math.Pow(currentDungeonLevel, 1.5) * 60) + dungeonRandom.Next(currentDungeonLevel * 30);
                currentPlayer.Gold += goldFound;
                terminal.WriteLine(Loc.Get("dungeon.portal_grab_gold", goldFound));
            }
            else
            {
                // Hostile dimension - fight
                terminal.SetColor("red");
                terminal.WriteLine(Loc.Get("dungeon.portal_hostile_dimension"));
                await Task.Delay(1000);

                var guardian = Monster.CreateMonster(
                    currentDungeonLevel + 5, "Portal Guardian",
                    currentDungeonLevel * 18, currentDungeonLevel * 5, 0,
                    "You should not be here!", false, false, "Void Blade", "Dimensional Armor",
                    true, false, currentDungeonLevel * 6, currentDungeonLevel * 4, currentDungeonLevel * 3
                );
                guardian.Level = currentDungeonLevel + 5;
                guardian.IsMiniBoss = true;  // Portal guardians are elite encounters, not floor bosses

                var combatEngine = new CombatEngine(terminal);
                var result = await combatEngine.PlayerVsMonster(currentPlayer, guardian, teammates);

                // Check if player should return to temple after resurrection
                if (result.ShouldReturnToTemple)
                {
                    terminal.SetColor("yellow");
                    terminal.WriteLine(Loc.Get("dungeon.awaken_temple"));
                    await Task.Delay(2000);
                    await NavigateToLocation(GameLocation.Temple);
                    return;
                }

                if (result.Victory)
                {
                    // Boss-level rewards - roughly 3x normal monster
                    terminal.SetColor("green");
                    terminal.WriteLine(Loc.Get("dungeon.portal_guardian_crystal"));
                    long bonusGold = (long)(Math.Pow(currentDungeonLevel, 1.5) * 36);
                    long bonusXp = (long)(Math.Pow(currentDungeonLevel, 1.5) * 45);
                    currentPlayer.Gold += bonusGold;
                    currentPlayer.Experience += bonusXp;
                }
            }
        }
        else if (choice.ToUpper() == "S")
        {
            terminal.SetColor("cyan");
            terminal.WriteLine(Loc.Get("dungeon.portal_study"));
            // Small XP for studying - about half a monster kill
            long xpGain = (long)(Math.Pow(currentDungeonLevel, 1.5) * 8);
            currentPlayer.Experience += xpGain;
            terminal.WriteLine(Loc.Get("dungeon.portal_learn_magic", xpGain));
        }
        else
        {
            terminal.SetColor("gray");
            terminal.WriteLine(Loc.Get("dungeon.portal_avoid"));
        }

        await Task.Delay(2000);
    }

    /// <summary>
    /// Challenging duelist encounter - recurring named rivals with memory
    /// </summary>
    private async Task ChallengingDuelistEncounter()
    {
        var currentPlayer = GetCurrentPlayer();

        // Get or create a recurring duelist for this player
        var duelist = GetOrCreateRecurringDuelist(currentPlayer);

        terminal.SetColor("yellow");
        terminal.WriteLine(Loc.Get("dungeon.duelist_header"));
        terminal.WriteLine("");

        terminal.SetColor("white");
        terminal.WriteLine(Loc.Get("dungeon.duelist_emerges", duelist.Name));
        terminal.WriteLine("");

        // Different dialogue based on encounter history
        if (duelist.TimesEncountered == 0)
        {
            // First meeting
            terminal.SetColor("cyan");
            terminal.WriteLine(Loc.Get("dungeon.duelist_first_1", currentPlayer.Name));
            terminal.WriteLine(Loc.Get("dungeon.duelist_first_2", duelist.Name));
            terminal.WriteLine(Loc.Get("dungeon.duelist_first_3"));
        }
        else if (duelist.PlayerWins > duelist.PlayerLosses)
        {
            // Player has been winning
            terminal.SetColor("yellow");
            terminal.WriteLine(Loc.Get("dungeon.duelist_winning_1", currentPlayer.Name));
            terminal.WriteLine(Loc.Get("dungeon.duelist_winning_2", duelist.PlayerWins, duelist.PlayerLosses));
            terminal.WriteLine(Loc.Get("dungeon.duelist_winning_3"));
        }
        else if (duelist.PlayerLosses > duelist.PlayerWins)
        {
            // Duelist has been winning
            terminal.SetColor("red");
            terminal.WriteLine(Loc.Get("dungeon.duelist_losing_1", currentPlayer.Name));
            terminal.WriteLine(Loc.Get("dungeon.duelist_losing_2", duelist.PlayerLosses));
            terminal.WriteLine(Loc.Get("dungeon.duelist_losing_3"));
        }
        else
        {
            // Evenly matched
            terminal.SetColor("magenta");
            terminal.WriteLine(Loc.Get("dungeon.duelist_even_1", currentPlayer.Name));
            terminal.WriteLine(Loc.Get("dungeon.duelist_even_2", duelist.PlayerWins));
            terminal.WriteLine(Loc.Get("dungeon.duelist_even_3"));
        }

        // Show duelist info for returning encounters
        if (duelist.TimesEncountered > 0)
        {
            terminal.WriteLine("");
            terminal.SetColor("gray");
            terminal.WriteLine(Loc.Get("dungeon.duelist_record", duelist.TimesEncountered, duelist.PlayerWins, duelist.PlayerLosses));
            terminal.WriteLine(Loc.Get("dungeon.duelist_strength", duelist.Name, duelist.Level));
        }

        terminal.WriteLine("");
        var choice = await terminal.GetInput(Loc.Get("dungeon.duelist_choice"));

        if (choice.ToUpper() == "A")
        {
            terminal.SetColor("cyan");
            terminal.WriteLine(Loc.Get("dungeon.duelist_accept", duelist.Name));
            terminal.WriteLine(Loc.Get("dungeon.duelist_steel_clashes"));
            await Task.Delay(1500);

            // Duelist scales with player but gets stronger each encounter
            int duelistLevel = Math.Max(currentPlayer.Level, duelist.Level);
            float strengthMod = 0.8f + (duelist.TimesEncountered * 0.05f); // Gets harder each time
            strengthMod = Math.Min(strengthMod, 1.2f); // Cap at 120%

            var duelistMonster = Monster.CreateMonster(
                duelistLevel, duelist.Name,
                (long)(currentPlayer.MaxHP * strengthMod), (long)(currentPlayer.Strength * strengthMod), 0,
                duelist.GetBattleCry(), false, false, duelist.Weapon, "Duelist's Garb",
                false, false, (long)(currentPlayer.Dexterity * strengthMod), (long)(currentPlayer.Wisdom * 0.8), 0
            );
            duelistMonster.Level = duelistLevel;
            duelistMonster.IsProperName = true;
            duelistMonster.CanSpeak = true;

            var combatEngine = new CombatEngine(terminal);
            var result = await combatEngine.PlayerVsMonster(currentPlayer, duelistMonster, null);

            // Check if player should return to temple after resurrection
            if (result.ShouldReturnToTemple)
            {
                terminal.SetColor("yellow");
                terminal.WriteLine(Loc.Get("dungeon.awaken_temple"));
                await Task.Delay(2000);
                await NavigateToLocation(GameLocation.Temple);
                return;
            }

            duelist.TimesEncountered++;
            duelist.Level = Math.Max(duelist.Level, currentPlayer.Level); // Duelist keeps up

            if (result.Victory)
            {
                duelist.PlayerWins++;
                terminal.SetColor("green");

                if (duelist.PlayerWins == 1)
                {
                    terminal.WriteLine(Loc.Get("dungeon.duelist_win1_1", duelist.Name));
                    terminal.WriteLine(Loc.Get("dungeon.duelist_win1_2"));
                }
                else if (duelist.PlayerWins == 3)
                {
                    terminal.WriteLine(Loc.Get("dungeon.duelist_win3_1", duelist.Name));
                    terminal.WriteLine(Loc.Get("dungeon.duelist_win3_2", currentPlayer.Name));
                    terminal.WriteLine(Loc.Get("dungeon.duelist_win3_3"));
                }
                else if (duelist.PlayerWins >= 5)
                {
                    terminal.WriteLine(Loc.Get("dungeon.duelist_win5_1", duelist.Name));
                    terminal.WriteLine(Loc.Get("dungeon.duelist_win5_2"));
                }
                else
                {
                    terminal.WriteLine(Loc.Get("dungeon.duelist_win_default_1", duelist.Name));
                    terminal.WriteLine(Loc.Get("dungeon.duelist_win_default_2"));
                }

                // Rewards scale with rivalry intensity - roughly 2-4 monster kills based on rivalry
                int rivalryBonus = 1 + duelist.TimesEncountered / 3;
                long goldReward = (long)(Math.Pow(currentDungeonLevel, 1.5) * 24 * rivalryBonus);
                long xpReward = (long)(Math.Pow(currentDungeonLevel, 1.5) * 15 * (1 + rivalryBonus * 0.5));
                currentPlayer.Gold += goldReward;
                currentPlayer.Experience += xpReward;
                currentPlayer.Chivalry += 5;

                terminal.WriteLine(Loc.Get("dungeon.duelist_rewards", goldReward, xpReward));
            }
            else if (currentPlayer.IsAlive)
            {
                duelist.PlayerLosses++;
                terminal.SetColor("yellow");

                if (duelist.PlayerLosses == 1)
                {
                    terminal.WriteLine(Loc.Get("dungeon.duelist_loss1_1", duelist.Name));
                    terminal.WriteLine(Loc.Get("dungeon.duelist_loss1_2"));
                }
                else if (duelist.PlayerLosses >= 3)
                {
                    terminal.WriteLine(Loc.Get("dungeon.duelist_loss3_1", duelist.Name));
                    terminal.WriteLine(Loc.Get("dungeon.duelist_loss3_2"));
                }
                else
                {
                    terminal.WriteLine(Loc.Get("dungeon.duelist_loss_default_1", duelist.Name));
                    terminal.WriteLine(Loc.Get("dungeon.duelist_loss_default_2"));
                }
            }

            // Save duelist progress
            SaveDuelistProgress(currentPlayer, duelist);
        }
        else if (choice.ToUpper() == "D")
        {
            terminal.SetColor("white");
            terminal.WriteLine(Loc.Get("dungeon.duelist_decline"));

            if (duelist.TimesEncountered > 0)
            {
                terminal.WriteLine(Loc.Get("dungeon.duelist_decline_known_1", duelist.Name));
                terminal.WriteLine(Loc.Get("dungeon.duelist_decline_known_2"));
            }
            else
            {
                terminal.WriteLine(Loc.Get("dungeon.duelist_decline_new_1", duelist.Name));
                terminal.WriteLine(Loc.Get("dungeon.duelist_decline_new_2"));
            }
            currentPlayer.Chivalry += 1;
        }
        else if (choice.ToUpper() == "I")
        {
            terminal.SetColor("red");
            terminal.WriteLine(Loc.Get("dungeon.duelist_insult"));

            if (duelist.TimesEncountered > 0 && duelist.PlayerWins > 0)
            {
                terminal.WriteLine(Loc.Get("dungeon.duelist_insult_known_1", duelist.Name));
                terminal.WriteLine(Loc.Get("dungeon.duelist_insult_known_2"));
                terminal.WriteLine(Loc.Get("dungeon.duelist_insult_known_3"));
            }
            else
            {
                terminal.WriteLine(Loc.Get("dungeon.duelist_insult_new_1", duelist.Name));
                terminal.WriteLine(Loc.Get("dungeon.duelist_insult_new_2"));
            }
            await Task.Delay(1000);

            currentPlayer.Darkness += 3;
            duelist.WasInsulted = true;

            // Enraged duelist is much stronger
            int rageLevel = currentPlayer.Level + 3 + (duelist.TimesEncountered / 2);
            var angryDuelist = Monster.CreateMonster(
                rageLevel, duelist.Name + " (Enraged)",
                (long)(currentPlayer.MaxHP * 1.3), (long)(currentPlayer.Strength * 1.3), 0,
                "DIE!", false, false, duelist.Weapon, "Duelist's Garb",
                false, false, currentPlayer.Dexterity, currentPlayer.Wisdom, 0
            );
            angryDuelist.Level = rageLevel;
            angryDuelist.IsProperName = true;
            angryDuelist.CanSpeak = true;

            var combatEngine = new CombatEngine(terminal);
            var result = await combatEngine.PlayerVsMonster(currentPlayer, angryDuelist, teammates);

            // Check if player should return to temple after resurrection
            if (result.ShouldReturnToTemple)
            {
                terminal.SetColor("yellow");
                terminal.WriteLine(Loc.Get("dungeon.awaken_temple"));
                await Task.Delay(2000);
                await NavigateToLocation(GameLocation.Temple);
                return;
            }

            duelist.TimesEncountered++;
            if (result.Victory)
            {
                duelist.PlayerWins++;
                terminal.SetColor("yellow");
                terminal.WriteLine(Loc.Get("dungeon.duelist_slain", duelist.Name));
                terminal.WriteLine(Loc.Get("dungeon.duelist_spirit_haunts"));
                currentPlayer.Darkness += 5;

                // Kill them permanently - they won't return
                duelist.IsDead = true;
            }
            else if (currentPlayer.IsAlive)
            {
                duelist.PlayerLosses++;
            }

            SaveDuelistProgress(currentPlayer, duelist);
        }
        else
        {
            terminal.SetColor("gray");
            terminal.WriteLine(Loc.Get("dungeon.duelist_walk_away", duelist.Name));
            if (duelist.TimesEncountered > 0)
            {
                terminal.WriteLine(Loc.Get("dungeon.duelist_coward"));
            }
        }

        await Task.Delay(1500);
    }

    /// <summary>
    /// Recurring duelist data - stored per player
    /// </summary>
    private class RecurringDuelist
    {
        public string Name { get; set; } = "";
        public string Weapon { get; set; } = "Dueling Blade";
        public int Level { get; set; } = 1;
        public int TimesEncountered { get; set; } = 0;
        public int PlayerWins { get; set; } = 0;
        public int PlayerLosses { get; set; } = 0;
        public bool WasInsulted { get; set; } = false;
        public bool IsDead { get; set; } = false;

        public string GetBattleCry()
        {
            if (WasInsulted) return "You will pay for your insults!";
            if (PlayerWins > PlayerLosses + 2) return "This time will be different!";
            if (PlayerLosses > PlayerWins + 2) return "You cannot hope to defeat me!";
            return "An honorable fight!";
        }
    }

    // Static storage for recurring duelists per player
    private static Dictionary<string, RecurringDuelist> _playerDuelists = new();
    private const int MaxPlayerDuelists = 200;

    private RecurringDuelist GetOrCreateRecurringDuelist(Character player)
    {
        string playerId = player.ID ?? player.Name;

        if (_playerDuelists.TryGetValue(playerId, out var existing))
        {
            if (!existing.IsDead)
                return existing;
            // If dead, create a new rival
        }

        // Create a new recurring duelist for this player
        var duelistTemplates = new[]
        {
            ("Sir Varen the Unyielding", "Longsword of Honor"),
            ("Lady Seraphina Dawnblade", "Rapier of the Sun"),
            ("Grimjaw the Ironclad", "War Axe"),
            ("The Masked Challenger", "Shadow Blade"),
            ("Kira Shadowstep", "Twin Daggers"),
            ("Marcus Steelwind", "Greatsword"),
            ("Yuki the Swift", "Katana"),
            ("Bartholomew the Bold", "Mace of Valor")
        };

        var template = duelistTemplates[dungeonRandom.Next(duelistTemplates.Length)];

        var newDuelist = new RecurringDuelist
        {
            Name = template.Item1,
            Weapon = template.Item2,
            Level = Math.Max(1, player.Level - 2)
        };

        // Evict oldest entries if over cap
        if (_playerDuelists.Count >= MaxPlayerDuelists)
        {
            var firstKey = _playerDuelists.Keys.First();
            _playerDuelists.Remove(firstKey);
        }
        _playerDuelists[playerId] = newDuelist;
        return newDuelist;
    }

    private void SaveDuelistProgress(Character player, RecurringDuelist duelist)
    {
        string playerId = player.ID ?? player.Name;
        _playerDuelists[playerId] = duelist;
    }

    /// <summary>
    /// Get recurring duelist data for save system (public accessor)
    /// </summary>
    public static (string Name, string Weapon, int Level, int TimesEncountered, int PlayerWins, int PlayerLosses, bool WasInsulted, bool IsDead)? GetRecurringDuelist(string playerId)
    {
        if (_playerDuelists.TryGetValue(playerId, out var duelist))
        {
            return (duelist.Name, duelist.Weapon, duelist.Level, duelist.TimesEncountered,
                    duelist.PlayerWins, duelist.PlayerLosses, duelist.WasInsulted, duelist.IsDead);
        }
        return null;
    }

    /// <summary>
    /// Restore recurring duelist data from save system
    /// </summary>
    public static void RestoreRecurringDuelist(string playerId, string name, string weapon, int level,
                                                int timesEncountered, int playerWins, int playerLosses,
                                                bool wasInsulted, bool isDead)
    {
        _playerDuelists[playerId] = new RecurringDuelist
        {
            Name = name,
            Weapon = weapon,
            Level = level,
            TimesEncountered = timesEncountered,
            PlayerWins = playerWins,
            PlayerLosses = playerLosses,
            WasInsulted = wasInsulted,
            IsDead = isDead
        };
    }

    /// <summary>
    /// Treasure chest encounter - Classic Pascal treasure mechanics
    /// </summary>
    private async Task TreasureChestEncounter()
    {
        terminal.SetColor("yellow");
        terminal.WriteLine(Loc.Get("dungeon.treasure_chest_header"));
        terminal.WriteLine("");

        terminal.WriteLine(Loc.Get("dungeon.treasure_chest_discover"), "cyan");
        terminal.WriteLine("");

        var choice = await terminal.GetInput(Loc.Get("dungeon.treasure_chest_choice"));

        var currentPlayer = GetCurrentPlayer();

        if (choice.ToUpper() == "O")
        {
            // Track chest opened for achievements
            currentPlayer.Statistics.RecordChestOpened();

            // 70% good, 20% trap, 10% mimic
            var chestRoll = dungeonRandom.Next(10);

            if (chestRoll < 7)
            {
                // Good treasure!
                // XP scaled to be roughly equivalent to 1-2 monster kills at this level
                // Monster XP at level L = L^1.5 * 15, so chest gives about 1.5x that
                long goldFound = currentDungeonLevel * 150 + dungeonRandom.Next(currentDungeonLevel * 100);
                long expGained = (long)(Math.Pow(currentDungeonLevel, 1.5) * 20) + dungeonRandom.Next(currentDungeonLevel * 5);

                terminal.SetColor("green");
                terminal.WriteLine(Loc.Get("dungeon.chest_opens_treasure"));

                var (chestXP, chestGold) = AwardDungeonReward(expGained, goldFound, "Treasure Chest");

                if (teammates.Count > 0)
                {
                    terminal.WriteLine(Loc.Get("dungeon.chest_party_finds", goldFound, expGained));
                    terminal.WriteLine(Loc.Get("dungeon.chest_your_share", $"{chestGold:N0}", $"{chestXP:N0}"));
                    BroadcastDungeonEvent($"\u001b[93m  The party opens a treasure chest!\u001b[0m");
                }
                else
                {
                    terminal.WriteLine(Loc.Get("dungeon.chest_find_gold", goldFound));
                    terminal.WriteLine(Loc.Get("dungeon.chest_gain_exp", expGained));
                }

                // Crafting material chance from treasure chests
                var eligibleMaterials = GameConfig.GetMaterialsForFloor(currentDungeonLevel);
                if (eligibleMaterials.Count > 0 && dungeonRandom.NextDouble() < GameConfig.MaterialDropChanceTreasure)
                {
                    var material = eligibleMaterials[dungeonRandom.Next(eligibleMaterials.Count)];
                    currentPlayer.AddMaterial(material.Id, 1);
                    terminal.WriteLine("");
                    terminal.SetColor(material.Color);
                    terminal.WriteLine(Loc.Get("dungeon.chest_discover_material", material.Name));
                    terminal.SetColor("gray");
                    terminal.WriteLine($"\"{material.Description}\"");
                }
            }
            else if (chestRoll < 9)
            {
                // Trap!
                terminal.SetColor("red");
                terminal.WriteLine(Loc.Get("dungeon.chest_trap"));
                BroadcastDungeonEvent("\u001b[91m  The chest was trapped!\u001b[0m");

                // Check for evasion based on agility
                if (TryEvadeTrap(currentPlayer))
                {
                    terminal.SetColor("green");
                    terminal.WriteLine(Loc.Get("dungeon.chest_leap_back"));
                    terminal.WriteLine(Loc.Get("dungeon.chest_reflexes_saved", currentPlayer.Agility));
                }
                else
                {
                    terminal.WriteLine(Loc.Get("dungeon.chest_no_react"), "yellow");
                    var trapType = dungeonRandom.Next(3);
                    switch (trapType)
                    {
                        case 0:
                            var poisonDmg = currentDungeonLevel * 5;
                            currentPlayer.HP = Math.Max(1, currentPlayer.HP - poisonDmg);
                            terminal.WriteLine(Loc.Get("dungeon.chest_poison_gas", poisonDmg));
                            currentPlayer.Poison = Math.Max(currentPlayer.Poison, 1);
                            currentPlayer.PoisonTurns = Math.Max(currentPlayer.PoisonTurns, 5 + currentDungeonLevel / 5);
                            terminal.WriteLine(Loc.Get("dungeon.chest_poisoned"), "magenta");
                            break;
                        case 1:
                            var spikeDmg = currentDungeonLevel * 8;
                            currentPlayer.HP = Math.Max(1, currentPlayer.HP - spikeDmg);
                            terminal.WriteLine(Loc.Get("dungeon.chest_spikes", spikeDmg));
                            break;
                        case 2:
                            var goldLost = currentPlayer.Gold / 10;
                            currentPlayer.Gold -= goldLost;
                            terminal.WriteLine(Loc.Get("dungeon.chest_acid", goldLost));
                            break;
                    }
                }
            }
            else
            {
                // Mimic! (triggers combat)
                terminal.SetColor("bright_red");
                terminal.WriteLine(Loc.Get("dungeon.chest_mimic"));
                BroadcastDungeonEvent("\u001b[91m  The chest was a MIMIC!\u001b[0m");
                await Task.Delay(1500);

                // Use MonsterGenerator stats so mimics scale like other mini-bosses
                int mimicLevel = currentDungeonLevel;
                long mimicBaseHP = (long)((50 * mimicLevel) + Math.Pow(mimicLevel, 1.2) * 15);
                long mimicHP = (long)(mimicBaseHP * 1.5f); // mini-boss multiplier
                long mimicStr = (long)(((2 * mimicLevel) + Math.Pow(mimicLevel, 1.05) * 1.5) * 1.5f);
                long mimicDef = (long)(((mimicLevel) + Math.Pow(mimicLevel, 1.02) * 0.5) * 1.5f);
                long mimicPunch = (long)(((mimicLevel) + Math.Pow(mimicLevel, 1.02) * 0.5) * 1.5f);
                long mimicWeapPow = (long)(((1.5 * mimicLevel) + Math.Pow(mimicLevel, 1.05) * 1) * 1.5f);
                long mimicArmPow = (long)(((0.5 * mimicLevel) + Math.Pow(mimicLevel, 1.02) * 0.3) * 1.5f * 0.7f);
                var mimic = Monster.CreateMonster(
                    mimicLevel, "Mimic",
                    mimicHP, mimicStr, mimicDef,
                    "Fooled you!", false, false, "Teeth", "Wooden Shell",
                    false, false, mimicPunch, mimicArmPow, mimicWeapPow
                );
                mimic.IsMiniBoss = true;  // Mimics are elite encounters, not floor bosses
                mimic.Level = mimicLevel;

                var combatEngine = new CombatEngine(terminal);
                var combatResult = await combatEngine.PlayerVsMonster(currentPlayer, mimic, teammates);

                // Check if player should return to temple after resurrection
                if (combatResult.ShouldReturnToTemple)
                {
                    terminal.SetColor("yellow");
                    terminal.WriteLine(Loc.Get("dungeon.awaken_temple"));
                    await Task.Delay(2000);
                    await NavigateToLocation(GameLocation.Temple);
                    return;
                }
            }
        }
        else
        {
            terminal.WriteLine(Loc.Get("dungeon.chest_leave_alone"), "gray");
        }

        await Task.Delay(2000);
    }

    /// <summary>
    /// Strangers encounter - Band of rogues/orcs (from Pascal DUNGEV2.PAS)
    /// </summary>
    private async Task StrangersEncounter()
    {
        terminal.SetColor("red");
        terminal.WriteLine(Loc.Get("dungeon.strangers_header"));
        terminal.WriteLine("");

        var currentPlayer = GetCurrentPlayer();
        var groupType = dungeonRandom.Next(4);
        string groupName;
        string[] memberTypes;

        switch (groupType)
        {
            case 0:
                groupName = Loc.Get("dungeon.strangers_orcs");
                memberTypes = new[] { "Orc", "Half-Orc", "Orc Raider" };
                terminal.WriteLine(Loc.Get("dungeon.strangers_orcs_desc"), "gray");
                terminal.WriteLine(Loc.Get("dungeon.strangers_orcs_arms"), "gray");
                break;
            case 1:
                groupName = Loc.Get("dungeon.strangers_trolls");
                memberTypes = new[] { "Troll", "Half-Troll", "Lumber-Troll" };
                terminal.WriteLine(Loc.Get("dungeon.strangers_trolls_desc"), "green");
                terminal.WriteLine(Loc.Get("dungeon.strangers_trolls_arms"), "gray");
                break;
            case 2:
                groupName = Loc.Get("dungeon.strangers_rogues");
                memberTypes = new[] { "Rogue", "Thief", "Pirate" };
                terminal.WriteLine(Loc.Get("dungeon.strangers_rogues_desc"), "cyan");
                terminal.WriteLine(Loc.Get("dungeon.strangers_rogues_arms"), "gray");
                break;
            default:
                groupName = Loc.Get("dungeon.strangers_dwarves");
                memberTypes = new[] { "Dwarf", "Dwarf Warrior", "Dwarf Scout" };
                terminal.WriteLine(Loc.Get("dungeon.strangers_dwarves_desc"), "yellow");
                terminal.WriteLine(Loc.Get("dungeon.strangers_dwarves_arms"), "gray");
                break;
        }

        terminal.WriteLine("");
        terminal.WriteLine(Loc.Get("dungeon.strangers_demand_gold"), "white");
        terminal.WriteLine("");

        var choice = await terminal.GetInput(Loc.Get("dungeon.strangers_choice"));

        if (choice.ToUpper() == "F")
        {
            terminal.WriteLine(Loc.Get("dungeon.strangers_draw_weapon"), "yellow");
            await Task.Delay(1500);

            // Create the group
            int groupSize = dungeonRandom.Next(3, 6);
            var monsters = new List<Monster>();

            for (int i = 0; i < groupSize; i++)
            {
                var name = memberTypes[dungeonRandom.Next(memberTypes.Length)];
                if (i == 0) name = name + " Leader";

                var monster = Monster.CreateMonster(
                    currentDungeonLevel, name,
                    currentDungeonLevel * (i == 0 ? 8 : 4),
                    currentDungeonLevel * 2, 0,
                    "Attack!", false, false, "Weapon", "Armor",
                    false, false, currentDungeonLevel * 2, currentDungeonLevel, currentDungeonLevel * 2
                );
                monster.Level = currentDungeonLevel;
                if (i == 0) monster.IsMiniBoss = true;  // Group leaders are elites, not floor bosses
                monsters.Add(monster);
            }

            var combatEngine = new CombatEngine(terminal);
            var combatResult = await combatEngine.PlayerVsMonsters(currentPlayer, monsters, teammates, offerMonkEncounter: false);

            // Check if player should return to temple after resurrection
            if (combatResult.ShouldReturnToTemple)
            {
                terminal.SetColor("yellow");
                terminal.WriteLine(Loc.Get("dungeon.awaken_temple"));
                await Task.Delay(2000);
                await NavigateToLocation(GameLocation.Temple);
                return;
            }
        }
        else if (choice.ToUpper() == "P")
        {
            long bribe = currentDungeonLevel * 500 + dungeonRandom.Next(1000);
            if (currentPlayer.Gold >= bribe)
            {
                currentPlayer.Gold -= bribe;
                terminal.WriteLine(Loc.Get("dungeon.strangers_pay_bribe", bribe), "yellow");
                terminal.WriteLine(Loc.Get("dungeon.strangers_leave_peace", groupName), "gray");
            }
            else
            {
                terminal.WriteLine(Loc.Get("ui.not_enough_gold"), "red");
                terminal.WriteLine(Loc.Get("dungeon.strangers_attack_anyway"), "red");
                await Task.Delay(1500);
                // Trigger simplified combat
                var monster = Monster.CreateMonster(
                    currentDungeonLevel, $"{groupName.Substring(0, 1).ToUpper()}{groupName.Substring(1)} Leader",
                    currentDungeonLevel * 10, currentDungeonLevel * 3, 0,
                    "No gold means death!", false, false, "Weapon", "Armor",
                    false, false, currentDungeonLevel * 3, currentDungeonLevel * 2, currentDungeonLevel * 2
                );
                var combatEngine = new CombatEngine(terminal);
                var combatResult = await combatEngine.PlayerVsMonster(currentPlayer, monster, teammates);

                // Check if player should return to temple after resurrection
                if (combatResult.ShouldReturnToTemple)
                {
                    terminal.SetColor("yellow");
                    terminal.WriteLine(Loc.Get("dungeon.awaken_temple"));
                    await Task.Delay(2000);
                    await NavigateToLocation(GameLocation.Temple);
                    return;
                }
            }
        }
        else
        {
            // Escape attempt - 60% chance
            if (dungeonRandom.NextDouble() < 0.6)
            {
                terminal.WriteLine(Loc.Get("dungeon.strangers_escape_success"), "green");
            }
            else
            {
                terminal.WriteLine(Loc.Get("dungeon.strangers_escape_fail"), "red");
                long stolen = currentPlayer.Gold / 5;
                currentPlayer.Gold -= stolen;
                terminal.WriteLine(Loc.Get("dungeon.strangers_steal_gold", stolen), "red");
            }
        }

        await Task.Delay(2000);
    }

    /// <summary>
    /// Harassed woman encounter - Moral choice event (from Pascal DUNGEVC.PAS)
    /// </summary>
    private async Task HarassedWomanEncounter()
    {
        terminal.SetColor("magenta");
        terminal.WriteLine(GameConfig.ScreenReaderMode ? Loc.Get("dungeon.damsel_header_sr") : Loc.Get("dungeon.damsel_header_visual"));
        terminal.WriteLine("");

        terminal.WriteLine(Loc.Get("dungeon.damsel_screams"), "white");
        terminal.WriteLine(Loc.Get("dungeon.damsel_harassed"), "gray");
        terminal.WriteLine("");

        var choice = await terminal.GetInput(Loc.Get("dungeon.damsel_choice"));

        var currentPlayer = GetCurrentPlayer();

        if (choice.ToUpper() == "H")
        {
            terminal.WriteLine(Loc.Get("dungeon.damsel_rush_defense"), "green");
            terminal.WriteLine(Loc.Get("dungeon.damsel_unhand_her"), "yellow");
            await Task.Delay(1500);

            // Fight ruffians
            var monsters = new List<Monster>();
            int count = dungeonRandom.Next(2, 4);
            for (int i = 0; i < count; i++)
            {
                var name = i == 0 ? "Ruffian Leader" : "Ruffian";
                var monster = Monster.CreateMonster(
                    currentDungeonLevel, name,
                    currentDungeonLevel * (i == 0 ? 6 : 3), currentDungeonLevel * 2, 0,
                    "Mind your own business!", false, false, "Knife", "Rags",
                    false, false, currentDungeonLevel * 2, currentDungeonLevel, currentDungeonLevel
                );
                monster.Level = currentDungeonLevel;
                monsters.Add(monster);
            }

            var combatEngine = new CombatEngine(terminal);
            var combatResult = await combatEngine.PlayerVsMonsters(currentPlayer, monsters, teammates, offerMonkEncounter: false);

            // Check if player should return to temple after resurrection
            if (combatResult.ShouldReturnToTemple)
            {
                terminal.SetColor("yellow");
                terminal.WriteLine(Loc.Get("dungeon.awaken_temple"));
                await Task.Delay(2000);
                await NavigateToLocation(GameLocation.Temple);
                return;
            }

            if (currentPlayer.HP > 0)
            {
                terminal.WriteLine("");
                terminal.WriteLine(Loc.Get("dungeon.damsel_thanks"), "cyan");
                long reward = currentDungeonLevel * 300 + dungeonRandom.Next(500);
                long chivGain = dungeonRandom.Next(50) + 30;

                terminal.WriteLine(Loc.Get("dungeon.damsel_reward", reward), "yellow");
                terminal.WriteLine(Loc.Get("dungeon.chivalry_increase", chivGain), "white");

                currentPlayer.Gold += reward;
                currentPlayer.Chivalry += chivGain;
            }
        }
        else if (choice.ToUpper() == "J")
        {
            terminal.SetColor("red");
            terminal.WriteLine(Loc.Get("dungeon.damsel_join_ruffians"));
            terminal.WriteLine(Loc.Get("dungeon.damsel_shameful"));

            long stolen = dungeonRandom.Next(200) + 50;
            long darkGain = dungeonRandom.Next(75) + 50;

            currentPlayer.Gold += stolen;
            currentPlayer.Darkness += darkGain;

            terminal.WriteLine(Loc.Get("dungeon.damsel_steal", stolen), "yellow");
            terminal.WriteLine(Loc.Get("dungeon.darkness_increase", darkGain), "magenta");
        }
        else
        {
            terminal.WriteLine(Loc.Get("dungeon.damsel_ignore_1"), "gray");
            terminal.WriteLine(Loc.Get("dungeon.damsel_ignore_2"), "gray");
        }

        await Task.Delay(2500);
    }

    /// <summary>
    /// Wounded man encounter - Healing quest (from Pascal DUNGEVC.PAS)
    /// </summary>
    private async Task WoundedManEncounter()
    {
        terminal.SetColor("cyan");
        terminal.WriteLine(GameConfig.ScreenReaderMode ? Loc.Get("dungeon.wounded_header_sr") : Loc.Get("dungeon.wounded_header_visual"));
        terminal.WriteLine("");

        terminal.WriteLine(Loc.Get("dungeon.wounded_find"), "white");
        terminal.WriteLine(Loc.Get("dungeon.wounded_bleeding"), "gray");
        terminal.WriteLine(Loc.Get("dungeon.wounded_begs"), "yellow");
        terminal.WriteLine("");

        var choice = await terminal.GetInput(Loc.Get("dungeon.wounded_choice"));

        var currentPlayer = GetCurrentPlayer();

        if (choice.ToUpper() == "H")
        {
            if (currentPlayer.Healing > 0)
            {
                currentPlayer.Healing--;
                terminal.WriteLine(Loc.Get("dungeon.wounded_heal_potion"), "green");
                terminal.WriteLine(Loc.Get("dungeon.wounded_recovers"), "white");

                long reward = currentDungeonLevel * 500 + dungeonRandom.Next(1000);
                long chivGain = dungeonRandom.Next(40) + 20;

                terminal.WriteLine(Loc.Get("dungeon.wounded_reward", reward), "yellow");
                terminal.WriteLine(Loc.Get("dungeon.chivalry_increase", chivGain), "white");

                currentPlayer.Gold += reward;
                currentPlayer.Chivalry += chivGain;
            }
            else
            {
                terminal.WriteLine(Loc.Get("ui.no_healing_potions_spare"), "red");
                terminal.WriteLine(Loc.Get("dungeon.wounded_bandage"), "gray");

                if (dungeonRandom.NextDouble() < 0.5)
                {
                    terminal.WriteLine(Loc.Get("dungeon.wounded_helps"), "green");
                    currentPlayer.Chivalry += 10;
                }
                else
                {
                    terminal.WriteLine(Loc.Get("dungeon.wounded_dies"), "red");
                }
            }
        }
        else if (choice.ToUpper() == "R")
        {
            terminal.SetColor("red");
            terminal.WriteLine(Loc.Get("dungeon.wounded_rob"));

            long stolen = dungeonRandom.Next(500) + 100;
            long darkGain = dungeonRandom.Next(80) + 60;

            terminal.WriteLine(Loc.Get("dungeon.wounded_rob_gold", stolen), "yellow");
            terminal.WriteLine(Loc.Get("dungeon.darkness_increase", darkGain), "magenta");

            currentPlayer.Gold += stolen;
            currentPlayer.Darkness += darkGain;

            terminal.WriteLine(Loc.Get("dungeon.wounded_dies_cursing"), "gray");
        }
        else
        {
            terminal.WriteLine(Loc.Get("dungeon.wounded_leave_1"), "gray");
            terminal.WriteLine(Loc.Get("dungeon.wounded_leave_2"), "gray");
        }

        await Task.Delay(2500);
    }

    /// <summary>
    /// Mysterious shrine - Random buff or debuff
    /// Also handles Lyris companion recruitment on floor 15
    /// </summary>
    private async Task MysteriousShrine()
    {
        var currentPlayer = GetCurrentPlayer();

        // Check for Lyris companion encounter on floor 15
        if (currentDungeonLevel == 15 && await TryLyrisRecruitment(currentPlayer))
        {
            return; // Lyris encounter handled
        }

        terminal.SetColor("bright_cyan");
        terminal.WriteLine(GameConfig.ScreenReaderMode ? Loc.Get("dungeon.shrine_header_sr") : Loc.Get("dungeon.shrine_header_visual"));
        terminal.WriteLine("");

        terminal.WriteLine(Loc.Get("dungeon.shrine_discover"), "white");
        terminal.WriteLine(Loc.Get("dungeon.shrine_offerings"), "gray");
        terminal.WriteLine("");

        var choice = await terminal.GetInput(Loc.Get("dungeon.shrine_choice"));

        if (choice.ToUpper() == "P")
        {
            terminal.WriteLine(Loc.Get("dungeon.shrine_kneel"), "cyan");
            await Task.Delay(1500);

            // Random blessing or curse
            var outcome = dungeonRandom.Next(6);
            switch (outcome)
            {
                case 0:
                    terminal.WriteLine(Loc.Get("dungeon.shrine_divine_light"), "bright_yellow");
                    currentPlayer.HP = currentPlayer.MaxHP;
                    terminal.WriteLine(Loc.Get("dungeon.shrine_fully_healed"), "green");
                    BroadcastDungeonEvent($"\u001b[32m  {currentPlayer.Name2} prays at a shrine and is fully healed!\u001b[0m");
                    break;
                case 1:
                    var strBonus = dungeonRandom.Next(5) + 1;
                    currentPlayer.Strength += strBonus;
                    terminal.WriteLine(Loc.Get("dungeon.shrine_stronger", strBonus), "green");
                    BroadcastDungeonEvent($"\u001b[32m  {currentPlayer.Name2} prays at a shrine and gains +{strBonus} Strength!\u001b[0m");
                    break;
                case 2:
                    var expBonus = 50 + currentDungeonLevel * 15;
                    currentPlayer.Experience += expBonus;
                    terminal.WriteLine(Loc.Get("dungeon.shrine_wisdom", expBonus), "yellow");
                    ShareEventRewardsWithGroup(currentPlayer, 0, expBonus, "Mysterious Shrine");
                    BroadcastDungeonEvent($"\u001b[33m  {currentPlayer.Name2} prays at a shrine and gains +{expBonus} EXP!\u001b[0m");
                    break;
                case 3:
                    terminal.WriteLine(Loc.Get("dungeon.shrine_silent"), "gray");
                    terminal.WriteLine(Loc.Get("dungeon.shrine_nothing"), "gray");
                    BroadcastDungeonEvent($"\u001b[90m  {currentPlayer.Name2} prays at a shrine... nothing happens.\u001b[0m");
                    break;
                case 4:
                    var hpLoss = currentPlayer.HP / 4;
                    currentPlayer.HP = Math.Max(1, currentPlayer.HP - hpLoss);
                    terminal.WriteLine(Loc.Get("dungeon.shrine_drains"), "red");
                    terminal.WriteLine(Loc.Get("dungeon.shrine_lose_hp", hpLoss), "red");
                    BroadcastDungeonEvent($"\u001b[31m  {currentPlayer.Name2} prays at a shrine and loses {hpLoss} HP!\u001b[0m");
                    break;
                case 5:
                    var goldLoss = currentPlayer.Gold / 5;
                    currentPlayer.Gold -= goldLoss;
                    terminal.WriteLine(Loc.Get("dungeon.shrine_gold_dissolves"), "red");
                    terminal.WriteLine(Loc.Get("dungeon.shrine_lose_gold", goldLoss), "red");
                    BroadcastDungeonEvent($"\u001b[31m  {currentPlayer.Name2} prays at a shrine and loses {goldLoss} gold!\u001b[0m");
                    break;
            }
        }
        else if (choice.ToUpper() == "D")
        {
            terminal.SetColor("red");
            terminal.WriteLine(Loc.Get("dungeon.shrine_desecrate"));

            long stolen = currentDungeonLevel * 200 + dungeonRandom.Next(500);
            long darkGain = dungeonRandom.Next(50) + 30;

            terminal.WriteLine(Loc.Get("dungeon.shrine_desecrate_gold", stolen), "yellow");
            terminal.WriteLine(Loc.Get("dungeon.darkness_increase", darkGain), "magenta");

            currentPlayer.Gold += stolen;
            currentPlayer.Darkness += darkGain;
            ShareEventRewardsWithGroup(currentPlayer, stolen, 0, "Desecrated Shrine");

            // Chance of angering spirits
            if (dungeonRandom.NextDouble() < 0.3)
            {
                terminal.WriteLine("");
                terminal.WriteLine(Loc.Get("dungeon.shrine_angry_spirit"), "bright_red");
                await Task.Delay(1500);

                var spirit = Monster.CreateMonster(
                    currentDungeonLevel + 5, "Vengeful Spirit",
                    currentDungeonLevel * 12, currentDungeonLevel * 4, 0,
                    "You will pay for your sacrilege!", false, false, "Spectral Claws", "Ethereal Form",
                    false, false, currentDungeonLevel * 4, currentDungeonLevel * 3, currentDungeonLevel * 3
                );
                spirit.IsMiniBoss = true;  // Vengeful spirits are elite encounters, not floor bosses

                var combatEngine = new CombatEngine(terminal);
                var combatResult = await combatEngine.PlayerVsMonster(currentPlayer, spirit, teammates);

                // Check if player should return to temple after resurrection
                if (combatResult.ShouldReturnToTemple)
                {
                    terminal.SetColor("yellow");
                    terminal.WriteLine(Loc.Get("dungeon.awaken_temple"));
                    await Task.Delay(2000);
                    await NavigateToLocation(GameLocation.Temple);
                    return;
                }
            }
        }
        else
        {
            terminal.WriteLine(Loc.Get("dungeon.shrine_leave"), "gray");
        }

        await Task.Delay(2000);
    }

    /// <summary>
    /// Try to recruit Lyris at a floor 15 shrine
    /// Returns true if the encounter was triggered (regardless of recruitment outcome)
    /// </summary>
    private async Task<bool> TryLyrisRecruitment(Character player)
    {
        var companionSystem = UsurperRemake.Systems.CompanionSystem.Instance;
        var lyris = companionSystem.GetCompanion(UsurperRemake.Systems.CompanionId.Lyris);

        // Check if Lyris can be recruited
        if (lyris == null || lyris.IsRecruited || lyris.IsDead || player.Level < lyris.RecruitLevel)
        {
            return false;
        }

        // Check if we've already encountered her on this playthrough (story flag)
        var story = StoryProgressionSystem.Instance;
        if (story.HasStoryFlag("lyris_shrine_encounter_complete"))
        {
            return false;
        }

        // Show the encounter
        terminal.ClearScreen();
        WriteBoxHeader(Loc.Get("dungeon.forgotten_shrine"), "bright_magenta", 66);
        terminal.WriteLine("");
        await Task.Delay(1000);

        terminal.SetColor("white");
        terminal.WriteLine(Loc.Get("quest.lyris_shrine.intro_1"));
        terminal.WriteLine(Loc.Get("quest.lyris_shrine.intro_2"));
        terminal.WriteLine("");
        await Task.Delay(1500);

        terminal.SetColor("gray");
        terminal.WriteLine(Loc.Get("quest.lyris_shrine.woman_1"));
        terminal.WriteLine(Loc.Get("quest.lyris_shrine.woman_2"));
        terminal.WriteLine(Loc.Get("quest.lyris_shrine.woman_3"));
        terminal.WriteLine("");
        await Task.Delay(1500);

        terminal.SetColor("bright_cyan");
        terminal.WriteLine($"\"{lyris.DialogueHints[0]}\"");
        terminal.WriteLine("");
        await Task.Delay(2000);

        terminal.SetColor("white");
        terminal.WriteLine(Loc.Get("quest.lyris_shrine.studies"));
        terminal.WriteLine("");
        terminal.SetColor("cyan");
        terminal.WriteLine($"\"{lyris.DialogueHints[1]}\"");
        terminal.WriteLine("");
        await Task.Delay(2000);

        // Show her details
        terminal.SetColor("yellow");
        terminal.WriteLine(Loc.Get("quest.lyris_shrine.this_is", lyris.Name, lyris.Title));
        terminal.WriteLine(Loc.Get("quest.lyris_shrine.role", lyris.CombatRole));
        terminal.WriteLine(Loc.Get("quest.lyris_shrine.abilities", string.Join(", ", lyris.Abilities)));
        terminal.WriteLine("");

        terminal.SetColor("gray");
        terminal.WriteLine(lyris.BackstoryBrief);
        terminal.WriteLine("");
        await Task.Delay(1500);

        terminal.SetColor("bright_yellow");
        if (IsScreenReader)
        {
            WriteSRMenuOption("R", Loc.Get("dungeon.ask_join"));
            WriteSRMenuOption("T", Loc.Get("dungeon.talk_more"));
            WriteSRMenuOption("L", Loc.Get("dungeon.leave_prayers"));
        }
        else
        {
            terminal.WriteLine($"[R] {Loc.Get("quest.lyris_shrine.option_join")}");
            terminal.WriteLine($"[T] {Loc.Get("quest.lyris_shrine.option_talk")}");
            terminal.WriteLine($"[L] {Loc.Get("quest.lyris_shrine.option_leave")}");
        }
        terminal.WriteLine("");

        var choice = await GetChoice();

        switch (choice.ToUpper())
        {
            case "R":
                bool success = await TryRecruitCompanionInDungeon(
                    UsurperRemake.Systems.CompanionId.Lyris, player);
                if (success)
                {
                    terminal.SetColor("bright_green");
                    terminal.WriteLine("");
                    terminal.WriteLine(Loc.Get("quest.lyris_shrine.rises", lyris.Name));
                    terminal.WriteLine(Loc.Get("quest.lyris_shrine.rises_quote"));
                    terminal.WriteLine("");
                    terminal.SetColor("yellow");
                    terminal.WriteLine(Loc.Get("quest.lyris_shrine.warning"));

                    // Generate news
                    NewsSystem.Instance.Newsy(false, $"{player.Name2} found {lyris.Name} at a forgotten shrine in the dungeon.");
                }
                break;

            case "T":
                terminal.WriteLine("");
                terminal.SetColor("cyan");
                terminal.WriteLine(Loc.Get("quest.lyris_shrine.speaks", lyris.Name));
                terminal.WriteLine("");
                terminal.SetColor("white");
                terminal.WriteLine(lyris.Description);
                terminal.WriteLine("");
                if (!string.IsNullOrEmpty(lyris.PersonalQuestDescription))
                {
                    terminal.SetColor("bright_magenta");
                    terminal.WriteLine(Loc.Get("quest.lyris_shrine.personal_quest", lyris.PersonalQuestName));
                    terminal.WriteLine($"\"{lyris.PersonalQuestDescription}\"");
                    terminal.WriteLine("");
                }
                terminal.SetColor("cyan");
                terminal.WriteLine($"\"{lyris.DialogueHints[2]}\"");
                terminal.WriteLine("");

                var followUp = await terminal.GetInput(Loc.Get("quest.lyris_shrine.ask_join_yn"));
                if (followUp.ToUpper() == "Y")
                {
                    await TryRecruitCompanionInDungeon(
                        UsurperRemake.Systems.CompanionId.Lyris, player);
                }
                break;

            default:
                terminal.SetColor("gray");
                terminal.WriteLine("");
                terminal.WriteLine(Loc.Get("quest.lyris_shrine.nod_leave", lyris.Name));
                terminal.WriteLine(Loc.Get("quest.lyris_shrine.meet_again"));
                break;
        }

        // Mark encounter as complete
        story.SetStoryFlag("lyris_shrine_encounter_complete", true);
        await terminal.PressAnyKey();
        return true;
    }

    /// <summary>
    /// Trap encounter - Various dungeon hazards
    /// </summary>
    private async Task TrapEncounter()
    {
        terminal.SetColor("red");
        terminal.WriteLine(GameConfig.ScreenReaderMode ? Loc.Get("dungeon.trap_header_sr") : Loc.Get("dungeon.trap_header_visual"));
        terminal.WriteLine("");

        var currentPlayer = GetCurrentPlayer();

        // Check for evasion based on agility
        if (TryEvadeTrap(currentPlayer))
        {
            terminal.SetColor("green");
            terminal.WriteLine(Loc.Get("dungeon.trap_reflexes_save"));
            terminal.WriteLine(Loc.Get("dungeon.trap_dodge_entirely", currentPlayer.Agility));
            await Task.Delay(1500);
            return;
        }

        terminal.WriteLine(Loc.Get("dungeon.trap_no_react"), "yellow");
        await Task.Delay(300);

        var trapType = dungeonRandom.Next(5);

        switch (trapType)
        {
            case 0:
                terminal.WriteLine(Loc.Get("dungeon.trap_floor_gives_way"), "white");
                var fallDmg = currentDungeonLevel * 3 + dungeonRandom.Next(10);
                currentPlayer.HP = Math.Max(1, currentPlayer.HP - fallDmg);
                terminal.WriteLine(Loc.Get("dungeon.trap_fall_damage", fallDmg), "red");
                break;
            case 1:
                terminal.WriteLine(Loc.Get("dungeon.trap_poison_darts"), "white");
                var dartDmg = currentDungeonLevel * 2 + dungeonRandom.Next(8);
                currentPlayer.HP = Math.Max(1, currentPlayer.HP - dartDmg);
                currentPlayer.Poison = Math.Max(currentPlayer.Poison, 1);
                currentPlayer.PoisonTurns = Math.Max(currentPlayer.PoisonTurns, 5 + currentDungeonLevel / 5);
                terminal.WriteLine(Loc.Get("dungeon.trap_dart_damage_poisoned", dartDmg), "magenta");
                break;
            case 2:
                terminal.WriteLine(Loc.Get("dungeon.trap_rune_explodes"), "bright_magenta");
                var runeDmg = currentDungeonLevel * 5 + dungeonRandom.Next(15);
                currentPlayer.HP = Math.Max(1, currentPlayer.HP - runeDmg);
                terminal.WriteLine(Loc.Get("dungeon.trap_magic_damage", runeDmg), "red");
                break;
            case 3:
                terminal.WriteLine(Loc.Get("dungeon.trap_net_falls"), "white");
                terminal.WriteLine(Loc.Get("dungeon.trap_struggle_free"), "gray");
                // Could implement time/turn penalty here
                break;
            case 4:
                terminal.WriteLine(Loc.Get("dungeon.trap_tripwire"), "white");
                terminal.WriteLine(Loc.Get("dungeon.trap_broken_nothing"), "green");
                terminal.WriteLine(Loc.Get("dungeon.trap_find_gold"), "yellow");
                currentPlayer.Gold += currentDungeonLevel * 50;
                break;
        }

        await Task.Delay(2500);
    }

    /// <summary>
    /// Ancient scroll encounter - Magic scroll discovery
    /// </summary>
    private async Task AncientScrollEncounter()
    {
        terminal.SetColor("bright_cyan");
        terminal.WriteLine(Loc.Get("dungeon.ancient_scroll_header"));
        terminal.WriteLine("");

        terminal.WriteLine(Loc.Get("dungeon.ancient_scroll_discover"), "white");
        terminal.WriteLine(Loc.Get("dungeon.ancient_scroll_symbols"), "gray");

        await HandleMagicScroll();
    }

    /// <summary>
    /// Gambling ghost encounter - Risk/reward minigame
    /// </summary>
    private async Task GamblingGhostEncounter()
    {
        terminal.SetColor("bright_white");
        terminal.WriteLine(Loc.Get("dungeon.ghost_header"));
        terminal.WriteLine("");

        terminal.WriteLine(Loc.Get("dungeon.ghost_appears"), "cyan");
        terminal.WriteLine(Loc.Get("dungeon.ghost_greeting"), "yellow");
        terminal.WriteLine(Loc.Get("dungeon.ghost_dice"), "gray");
        terminal.WriteLine("");

        var currentPlayer = GetCurrentPlayer();
        long minBet = 100;
        long maxBet = currentPlayer.Gold / 2;

        if (currentPlayer.Gold < minBet)
        {
            terminal.WriteLine(Loc.Get("dungeon.ghost_no_gold"), "yellow");
            terminal.WriteLine(Loc.Get("dungeon.ghost_fades_disappointed"), "gray");
            await Task.Delay(2000);
            return;
        }

        terminal.WriteLine(Loc.Get("dungeon.ghost_place_bet", minBet, maxBet), "yellow");
        var betStr = await terminal.GetInput(Loc.Get("dungeon.ghost_bet_prompt"));

        if (!long.TryParse(betStr, out long bet) || bet < minBet || bet > maxBet)
        {
            terminal.WriteLine(Loc.Get("dungeon.ghost_coward"), "yellow");
            terminal.WriteLine(Loc.Get("dungeon.ghost_fades"), "gray");
            await Task.Delay(2000);
            return;
        }

        terminal.WriteLine(Loc.Get("dungeon.ghost_you_bet", bet), "white");
        terminal.WriteLine(Loc.Get("dungeon.ghost_rolls"), "gray");
        await Task.Delay(1500);

        var ghostRoll = dungeonRandom.Next(1, 7) + dungeonRandom.Next(1, 7);
        terminal.WriteLine(Loc.Get("dungeon.ghost_roll_result", ghostRoll), "cyan");

        terminal.WriteLine(Loc.Get("dungeon.ghost_your_turn"), "gray");
        await Task.Delay(1000);

        var playerRoll = dungeonRandom.Next(1, 7) + dungeonRandom.Next(1, 7);
        terminal.WriteLine(Loc.Get("dungeon.ghost_player_roll", playerRoll), "yellow");

        if (playerRoll > ghostRoll)
        {
            terminal.SetColor("green");
            terminal.WriteLine(Loc.Get("dungeon.ghost_you_win"));
            terminal.WriteLine(Loc.Get("dungeon.ghost_pays", bet), "yellow");
            currentPlayer.Gold += bet;
        }
        else if (playerRoll < ghostRoll)
        {
            terminal.SetColor("red");
            terminal.WriteLine(Loc.Get("dungeon.ghost_you_lose"));
            terminal.WriteLine(Loc.Get("dungeon.ghost_cackles"), "yellow");
            currentPlayer.Gold -= bet;
        }
        else
        {
            terminal.SetColor("yellow");
            terminal.WriteLine(Loc.Get("dungeon.ghost_tie"));
            terminal.WriteLine(Loc.Get("dungeon.ghost_tie_msg"), "yellow");
        }

        terminal.WriteLine(Loc.Get("dungeon.ghost_fades_shadows"), "gray");
        await Task.Delay(2500);
    }

    /// <summary>
    /// Potion cache encounter - find random potions
    /// </summary>
    private async Task PotionCacheEncounter()
    {
        terminal.SetColor("bright_green");
        terminal.WriteLine(GameConfig.ScreenReaderMode ? Loc.Get("dungeon.potion_cache_header_sr") : $"✚ {Loc.Get("dungeon.potion_cache_header")} ✚");
        terminal.WriteLine("");

        var currentPlayer = GetCurrentPlayer();

        // Random potion messages
        string[] messages = new[]
        {
            Loc.Get("dungeon.potion_cache_msg_1"),
            Loc.Get("dungeon.potion_cache_msg_2"),
            Loc.Get("dungeon.potion_cache_msg_3"),
            Loc.Get("dungeon.potion_cache_msg_4"),
            Loc.Get("dungeon.potion_cache_msg_5")
        };

        terminal.WriteLine(messages[dungeonRandom.Next(messages.Length)], "cyan");
        terminal.WriteLine("");

        // Give 1-5 potions, but don't exceed max
        int potionsFound = dungeonRandom.Next(1, 6);
        int currentPotions = (int)currentPlayer.Healing;
        int maxPotions = currentPlayer.MaxPotions;
        int roomAvailable = maxPotions - currentPotions;

        if (roomAvailable <= 0)
        {
            terminal.WriteLine(Loc.Get("dungeon.potion_cache_max"), "yellow");
            terminal.WriteLine(Loc.Get("dungeon.potion_cache_leave"), "gray");
        }
        else
        {
            int actualGained = Math.Min(potionsFound, roomAvailable);
            currentPlayer.Healing += actualGained;

            terminal.SetColor("green");
            terminal.WriteLine(Loc.Get("dungeon.potion_cache_collect", actualGained));
            terminal.WriteLine(Loc.Get("dungeon.potion_cache_count", currentPlayer.Healing, currentPlayer.MaxPotions), "cyan");

            if (actualGained < potionsFound)
            {
                terminal.WriteLine(Loc.Get("dungeon.potion_cache_left_behind", potionsFound - actualGained), "gray");
            }
        }

        await Task.Delay(2500);
    }
    
    /// <summary>
    /// Merchant encounter - Pascal DUNGEV2.PAS
    /// </summary>
    private async Task MerchantEncounter()
    {
        var player = GetCurrentPlayer();

        terminal.ClearScreen();
        WriteBoxHeader(Loc.Get("dungeon.merchant"), "green", 55);
        terminal.WriteLine("");
        terminal.SetColor("white");
        terminal.WriteLine(Loc.Get("dungeon.merchant_appears"));
        terminal.WriteLine(Loc.Get("dungeon.merchant_greeting_text"));
        terminal.WriteLine("");

        string choice;
        while (true)
        {
            choice = (await terminal.GetInput(Loc.Get("dungeon.merchant_trade_or_attack"))).ToUpper();
            if (choice == "T" || choice == "A" || choice == "L" || choice == "")
                break;
        }

        if (choice == "T")
        {
            await MerchantTradeMenu(player);
        }
        else if (choice == "A")
        {
            terminal.SetColor("red");
            terminal.WriteLine(Loc.Get("dungeon.merchant_rob"));
            terminal.WriteLine("");
            await Task.Delay(1000);

            // Create merchant monster for combat
            var merchant = CreateMerchantMonster();
            var combatEngine = new CombatEngine(terminal);
            var result = await combatEngine.PlayerVsMonster(player, merchant, teammates);

            // Check if player should return to temple after resurrection
            if (result.ShouldReturnToTemple)
            {
                terminal.SetColor("yellow");
                terminal.WriteLine(Loc.Get("dungeon.awaken_temple"));
                await Task.Delay(2000);
                await NavigateToLocation(GameLocation.Temple);
                return;
            }

            if (result.Outcome == CombatOutcome.Victory)
            {
                // Loot the merchant — split among group if in a party
                long totalLoot = currentDungeonLevel * 100 + dungeonRandom.Next(200);
                var aliveGroupMembers = teammates.Where(t => t != null && t.IsAlive && t.IsGroupedPlayer).ToList();

                if (aliveGroupMembers.Count > 0)
                {
                    int totalMembers = 1 + aliveGroupMembers.Count;
                    long goldPerMember = totalLoot / totalMembers;

                    player.Gold += goldPerMember;
                    player.Healing = Math.Min(player.MaxPotions, player.Healing + 3);
                    terminal.SetColor("yellow");
                    terminal.WriteLine(Loc.Get("dungeon.merchant_loot_split", goldPerMember, totalMembers));

                    foreach (var mate in aliveGroupMembers)
                    {
                        mate.Gold += goldPerMember;
                        mate.Healing = Math.Min(mate.MaxPotions, mate.Healing + 3);
                        mate.Darkness += 10;
                        mate.Statistics?.RecordGoldChange(mate.Gold);

                        if (mate.RemoteTerminal != null)
                        {
                            mate.RemoteTerminal.SetColor("yellow");
                            mate.RemoteTerminal.WriteLine(Loc.Get("dungeon.merchant_loot", goldPerMember));
                            mate.RemoteTerminal.SetColor("red");
                            mate.RemoteTerminal.WriteLine(Loc.Get("dungeon.merchant_darkness"));
                        }
                    }
                }
                else
                {
                    player.Gold += totalLoot;
                    player.Healing = Math.Min(player.MaxPotions, player.Healing + 3);
                    terminal.SetColor("yellow");
                    terminal.WriteLine(Loc.Get("dungeon.merchant_loot", totalLoot));
                }
            }

            // Evil deed (leader always gets darkness — group members handled above)
            player.Darkness += 10;
            terminal.SetColor("red");
            terminal.WriteLine(Loc.Get("dungeon.merchant_darkness"));
        }
        else // L or empty — leave peacefully
        {
            terminal.SetColor("gray");
            terminal.WriteLine(Loc.Get("dungeon.merchant_goodbye"));
        }

        await terminal.PressAnyKey();
    }

    private async Task MerchantTradeMenu(Character player)
    {
        int potionPrice = 40 + (currentDungeonLevel * 5);
        int megaPotionPrice = currentDungeonLevel * 100;
        int antidotePrice = 75;

        // Generate rare items based on dungeon level
        var rareItems = GenerateMerchantRareItems(currentDungeonLevel);

        bool trading = true;
        while (trading)
        {
            terminal.ClearScreen();
            WriteBoxHeader(Loc.Get("dungeon.merchant_wares"), "green", 55);
            terminal.WriteLine("");

            terminal.SetColor("yellow");
            terminal.WriteLine($"{Loc.Get("ui.gold")}: {player.Gold:N0}");
            terminal.WriteLine($"{Loc.Get("status.potions")}: {player.Healing}/{player.MaxPotions}");
            terminal.SetColor("white");
            terminal.WriteLine($"{Loc.Get("ui.weapon_power")}: {player.WeapPow}  |  {Loc.Get("combat.status_armor_power")}: {player.ArmPow}");
            terminal.WriteLine("");

            WriteSectionHeader(Loc.Get("dungeon.section_supplies"), "white");
            terminal.WriteLine("");

            terminal.SetColor("darkgray");
            terminal.Write("  [");
            terminal.SetColor("green");
            terminal.Write("1");
            terminal.SetColor("darkgray");
            terminal.Write("] ");
            terminal.SetColor("white");
            terminal.WriteLine(Loc.Get("dungeon.merchant_healing_potion", potionPrice));

            terminal.SetColor("darkgray");
            terminal.Write("  [");
            terminal.SetColor("bright_green");
            terminal.Write("2");
            terminal.SetColor("darkgray");
            terminal.Write("] ");
            terminal.SetColor("white");
            terminal.WriteLine(Loc.Get("dungeon.merchant_mega_potion", megaPotionPrice));

            terminal.SetColor("darkgray");
            terminal.Write("  [");
            terminal.SetColor("cyan");
            terminal.Write("3");
            terminal.SetColor("darkgray");
            terminal.Write("] ");
            terminal.SetColor("white");
            terminal.WriteLine(Loc.Get("dungeon.merchant_antidote", antidotePrice, player.Antidotes, player.MaxAntidotes));

            terminal.SetColor("darkgray");
            terminal.Write("  [");
            terminal.SetColor("yellow");
            terminal.Write("4");
            terminal.SetColor("darkgray");
            terminal.Write("] ");
            terminal.SetColor("white");
            int potionsToMax = (int)Math.Max(0, player.MaxPotions - player.Healing);
            terminal.WriteLine(potionsToMax > 0
                ? Loc.Get("dungeon.merchant_buy_max", potionPrice * potionsToMax)
                : Loc.Get("dungeon.merchant_buy_max_full"));

            terminal.WriteLine("");
            WriteSectionHeader(Loc.Get("dungeon.section_rare_items"), "bright_magenta");
            terminal.WriteLine("");

            for (int i = 0; i < rareItems.Count; i++)
            {
                var item = rareItems[i];
                terminal.SetColor("darkgray");
                terminal.Write("  [");
                terminal.SetColor("bright_magenta");
                terminal.Write($"{(char)('A' + i)}");
                terminal.SetColor("darkgray");
                terminal.Write("] ");
                terminal.SetColor(item.Sold ? "darkgray" : "bright_yellow");
                if (item.Sold)
                {
                    terminal.WriteLine($"{item.Name} - SOLD");
                }
                else
                {
                    terminal.WriteLine($"{item.Name} ({item.Price:N0}g) - {item.Description}");
                }
            }

            terminal.WriteLine("");
            terminal.SetColor("darkgray");
            terminal.Write("  [");
            terminal.SetColor("red");
            terminal.Write("L");
            terminal.SetColor("darkgray");
            terminal.Write("] ");
            terminal.SetColor("white");
            terminal.WriteLine(Loc.Get("dungeon.merchant_leave_shop"));

            terminal.WriteLine("");
            terminal.SetColor("cyan");
            terminal.Write(Loc.Get("ui.choice"));
            terminal.SetColor("white");

            var choice = (await terminal.GetInput("")).Trim().ToUpper();

            switch (choice)
            {
                case "1":
                    if (player.Healing >= player.MaxPotions)
                    {
                        terminal.SetColor("yellow");
                        terminal.WriteLine(Loc.Get("dungeon.merchant_cant_carry_potions"));
                    }
                    else if (player.Gold >= potionPrice)
                    {
                        player.Gold -= potionPrice;
                        player.Healing++;
                        terminal.SetColor("green");
                        terminal.WriteLine(Loc.Get("dungeon.merchant_purchased_potion", player.Healing, player.MaxPotions));
                    }
                    else
                    {
                        terminal.SetColor("red");
                        terminal.WriteLine(Loc.Get("ui.not_enough_gold_friend"));
                    }
                    await Task.Delay(1500);
                    break;

                case "2":
                    if (player.Gold >= megaPotionPrice)
                    {
                        player.Gold -= megaPotionPrice;
                        player.HP = player.MaxHP;
                        terminal.SetColor("bright_green");
                        terminal.WriteLine(Loc.Get("dungeon.merchant_mega_full_heal"));
                        terminal.WriteLine($"{Loc.Get("combat.bar_hp")}: {player.HP}/{player.MaxHP}");
                    }
                    else
                    {
                        terminal.SetColor("red");
                        terminal.WriteLine(Loc.Get("ui.not_enough_gold_friend"));
                    }
                    await Task.Delay(1500);
                    break;

                case "3":
                    if (player.Antidotes >= player.MaxAntidotes)
                    {
                        terminal.SetColor("yellow");
                        terminal.WriteLine(Loc.Get("dungeon.merchant_cant_carry_antidotes"));
                    }
                    else if (player.Gold >= antidotePrice)
                    {
                        player.Gold -= antidotePrice;
                        if (player.Poison > 0)
                        {
                            // If poisoned, use immediately
                            player.Poison = 0;
                            player.PoisonTurns = 0;
                            terminal.SetColor("green");
                            terminal.WriteLine(Loc.Get("dungeon.merchant_antidote_cure"));
                        }
                        else
                        {
                            // Store for later use
                            player.Antidotes++;
                            terminal.SetColor("green");
                            terminal.WriteLine(Loc.Get("dungeon.merchant_purchased_antidote", player.Antidotes, player.MaxAntidotes));
                        }
                    }
                    else
                    {
                        terminal.SetColor("red");
                        terminal.WriteLine(Loc.Get("ui.not_enough_gold_friend"));
                    }
                    await Task.Delay(1500);
                    break;

                case "4":
                    int potionsNeeded = Math.Max(0, player.MaxPotions - (int)player.Healing);
                    if (potionsNeeded <= 0)
                    {
                        terminal.SetColor("yellow");
                        terminal.WriteLine(Loc.Get("dungeon.merchant_full_potions"));
                    }
                    else
                    {
                        long totalCost = potionsNeeded * potionPrice;
                        if (player.Gold >= totalCost)
                        {
                            player.Gold -= totalCost;
                            player.Healing = player.MaxPotions;
                            terminal.SetColor("green");
                            terminal.WriteLine(Loc.Get("dungeon.merchant_purchased_potions", potionsNeeded, totalCost));
                            terminal.WriteLine(Loc.Get("dungeon.merchant_potions_count", player.Healing, player.MaxPotions));
                        }
                        else
                        {
                            int canAfford = Math.Min((int)(player.Gold / potionPrice), potionsNeeded);
                            if (canAfford > 0)
                            {
                                player.Gold -= canAfford * potionPrice;
                                player.Healing += canAfford;
                                terminal.SetColor("yellow");
                                terminal.WriteLine(Loc.Get("dungeon.merchant_afford_partial", canAfford));
                                terminal.WriteLine(Loc.Get("dungeon.merchant_potions_count", player.Healing, player.MaxPotions));
                            }
                            else
                            {
                                terminal.SetColor("red");
                                terminal.WriteLine(Loc.Get("ui.not_enough_gold_friend"));
                            }
                        }
                    }
                    await Task.Delay(1500);
                    break;

                case "A":
                case "B":
                case "C":
                case "D":
                    int itemIndex = choice[0] - 'A';
                    if (itemIndex >= 0 && itemIndex < rareItems.Count)
                    {
                        await PurchaseRareItem(player, rareItems[itemIndex]);
                    }
                    break;

                case "L":
                case "":
                    trading = false;
                    terminal.SetColor("gray");
                    terminal.WriteLine(Loc.Get("dungeon.merchant_safe_travels"));
                    await Task.Delay(1000);
                    break;

                default:
                    terminal.SetColor("red");
                    terminal.WriteLine(Loc.Get("dungeon.merchant_invalid"));
                    await Task.Delay(1000);
                    break;
            }
        }
    }

    private class MerchantRareItem
    {
        public string Name { get; set; } = "";
        public string Description { get; set; } = "";
        public long Price { get; set; }
        public string Type { get; set; } = ""; // weapon, armor, accessory
        public bool Sold { get; set; } = false;
        public Item? LootItem { get; set; }
    }

    private List<MerchantRareItem> GenerateMerchantRareItems(int level)
    {
        var items = new List<MerchantRareItem>();
        var player = GetCurrentPlayer();
        var playerClass = player.Class;

        // Generate 4 real loot items at guaranteed Uncommon+ rarity
        // Merchant has curated goods — better than random drops but at a premium price
        int attempts = 0;
        int maxAttempts = 50;

        // Item 1: Weapon
        while (attempts++ < maxAttempts)
        {
            var item = LootGenerator.GenerateWeapon(level, playerClass);
            if (item != null && item.Attack > 0)
            {
                item.IsIdentified = true; // Merchant items are always identified
                long merchantPrice = Math.Max(500, (long)(item.Value * 1.5));
                items.Add(new MerchantRareItem
                {
                    Name = item.Name,
                    Description = $"Atk +{item.Attack}" + FormatItemBonuses(item),
                    Price = merchantPrice,
                    Type = "weapon",
                    LootItem = item
                });
                break;
            }
        }

        // Item 2: Armor (body/head/legs/etc.)
        attempts = 0;
        while (attempts++ < maxAttempts)
        {
            var item = LootGenerator.GenerateArmor(level, playerClass);
            if (item != null && item.Armor > 0)
            {
                item.IsIdentified = true; // Merchant items are always identified
                long merchantPrice = Math.Max(500, (long)(item.Value * 1.5));
                items.Add(new MerchantRareItem
                {
                    Name = item.Name,
                    Description = $"AC +{item.Armor}" + FormatItemBonuses(item),
                    Price = merchantPrice,
                    Type = "armor",
                    LootItem = item
                });
                break;
            }
        }

        // Item 3: Another weapon or armor (variety)
        attempts = 0;
        while (attempts++ < maxAttempts)
        {
            var item = LootGenerator.GenerateDungeonLoot(level, playerClass);
            if (item != null && (item.Attack > 0 || item.Armor > 0))
            {
                // Avoid duplicates
                if (items.Any(i => i.Name == item.Name)) continue;

                item.IsIdentified = true; // Merchant items are always identified
                long merchantPrice = Math.Max(500, (long)(item.Value * 1.5));
                string stats = item.Type == ObjType.Weapon
                    ? $"Atk +{item.Attack}" + FormatItemBonuses(item)
                    : $"AC +{item.Armor}" + FormatItemBonuses(item);
                items.Add(new MerchantRareItem
                {
                    Name = item.Name,
                    Description = stats,
                    Price = merchantPrice,
                    Type = item.Type == ObjType.Weapon ? "weapon" : "armor",
                    LootItem = item
                });
                break;
            }
        }

        // Item 4: Ring or Necklace
        attempts = 0;
        while (attempts++ < maxAttempts)
        {
            Item item;
            if (dungeonRandom.NextDouble() < 0.5)
                item = LootGenerator.GenerateRing(level);
            else
                item = LootGenerator.GenerateNecklace(level);

            if (item != null)
            {
                item.IsIdentified = true; // Merchant items are always identified
                long merchantPrice = Math.Max(300, (long)(item.Value * 1.5));
                string stats = FormatAccessoryStats(item);
                items.Add(new MerchantRareItem
                {
                    Name = item.Name,
                    Description = stats,
                    Price = merchantPrice,
                    Type = "accessory",
                    LootItem = item
                });
                break;
            }
        }

        return items;
    }

    private static string FormatItemBonuses(Item item)
    {
        var bonuses = new List<string>();
        if (item.Strength != 0) bonuses.Add($"STR {item.Strength:+#;-#;0}");
        if (item.Defence != 0) bonuses.Add($"DEF {item.Defence:+#;-#;0}");
        if (item.Dexterity != 0) bonuses.Add($"DEX {item.Dexterity:+#;-#;0}");
        if (item.Wisdom != 0) bonuses.Add($"WIS {item.Wisdom:+#;-#;0}");
        if (item.Agility != 0) bonuses.Add($"AGI {item.Agility:+#;-#;0}");
        if (item.Charisma != 0) bonuses.Add($"CHA {item.Charisma:+#;-#;0}");
        // CON and INT are stored in LootEffects, not as direct Item properties
        int conFromEffects = item.LootEffects?.Where(e => e.EffectType == (int)LootGenerator.SpecialEffect.Constitution).Sum(e => e.Value) ?? 0;
        int intFromEffects = item.LootEffects?.Where(e => e.EffectType == (int)LootGenerator.SpecialEffect.Intelligence).Sum(e => e.Value) ?? 0;
        if (conFromEffects != 0) bonuses.Add($"CON {conFromEffects:+#;-#;0}");
        if (intFromEffects != 0) bonuses.Add($"INT {intFromEffects:+#;-#;0}");
        if (item.HP != 0) bonuses.Add($"HP {item.HP:+#;-#;0}");
        if (item.Mana != 0) bonuses.Add($"Mana {item.Mana:+#;-#;0}");
        if (item.Stamina != 0) bonuses.Add($"STA {item.Stamina:+#;-#;0}");
        if (bonuses.Count > 0) return ", " + string.Join(", ", bonuses);
        return "";
    }

    private static string FormatAccessoryStats(Item item)
    {
        var stats = new List<string>();
        if (item.Attack > 0) stats.Add($"Atk +{item.Attack}");
        if (item.Armor > 0) stats.Add($"AC +{item.Armor}");
        if (item.Strength != 0) stats.Add($"STR {item.Strength:+#;-#;0}");
        if (item.Defence != 0) stats.Add($"DEF {item.Defence:+#;-#;0}");
        if (item.Dexterity != 0) stats.Add($"DEX {item.Dexterity:+#;-#;0}");
        if (item.Wisdom != 0) stats.Add($"WIS {item.Wisdom:+#;-#;0}");
        if (item.Agility != 0) stats.Add($"AGI {item.Agility:+#;-#;0}");
        if (item.Charisma != 0) stats.Add($"CHA {item.Charisma:+#;-#;0}");
        // CON and INT are stored in LootEffects, not as direct Item properties
        int conFromEffects = item.LootEffects?.Where(e => e.EffectType == (int)LootGenerator.SpecialEffect.Constitution).Sum(e => e.Value) ?? 0;
        int intFromEffects = item.LootEffects?.Where(e => e.EffectType == (int)LootGenerator.SpecialEffect.Intelligence).Sum(e => e.Value) ?? 0;
        if (conFromEffects != 0) stats.Add($"CON {conFromEffects:+#;-#;0}");
        if (intFromEffects != 0) stats.Add($"INT {intFromEffects:+#;-#;0}");
        if (item.HP != 0) stats.Add($"HP {item.HP:+#;-#;0}");
        if (item.Mana != 0) stats.Add($"Mana {item.Mana:+#;-#;0}");
        if (item.Stamina != 0) stats.Add($"STA {item.Stamina:+#;-#;0}");
        return stats.Count > 0 ? string.Join(", ", stats) : "Accessory";
    }

    private async Task PurchaseRareItem(Character player, MerchantRareItem item)
    {
        if (item.Sold)
        {
            terminal.SetColor("gray");
            terminal.WriteLine(Loc.Get("dungeon.merchant_item_sold"));
            await Task.Delay(1500);
            return;
        }

        if (player.Gold < item.Price)
        {
            terminal.SetColor("red");
            terminal.WriteLine(Loc.Get("dungeon.merchant_need_gold", item.Price, item.Name));
            terminal.WriteLine(Loc.Get("dungeon.merchant_come_back"));
            await Task.Delay(2000);
            return;
        }

        // Show item comparison if it's a weapon or armor
        if (item.LootItem != null)
        {
            terminal.SetColor("white");
            terminal.WriteLine("");
            terminal.WriteLine($"  {item.Name}");
            terminal.SetColor("cyan");
            terminal.WriteLine($"  {item.Description}");

            if (item.LootItem.Type == ObjType.Weapon)
            {
                terminal.SetColor("gray");
                terminal.WriteLine($"  {Loc.Get("dungeon.merchant_current_weapon", player.WeapPow)}");
            }
            else if (item.LootItem.Armor > 0)
            {
                terminal.SetColor("gray");
                terminal.WriteLine($"  {Loc.Get("dungeon.merchant_current_armor", player.ArmPow)}");
            }
            terminal.WriteLine("");
        }

        terminal.SetColor("cyan");
        terminal.WriteLine(Loc.Get("dungeon.merchant_purchase_confirm", item.Name, item.Price));
        var confirm = (await terminal.GetInput("")).Trim().ToUpper();

        if (confirm == "Y")
        {
            player.Gold -= item.Price;
            item.Sold = true;
            player.Statistics?.RecordPurchase(item.Price);
            player.Statistics?.RecordGoldSpent(item.Price);
            player.Statistics?.RecordGoldChange(player.Gold);

            if (item.LootItem != null)
            {
                player.Inventory.Add(item.LootItem);
            }

            terminal.SetColor("bright_yellow");
            terminal.WriteLine("");
            WriteThickDivider(39);
            terminal.SetColor("bright_yellow");
            terminal.WriteLine($"  ACQUIRED: {item.Name.ToUpper()}");
            WriteThickDivider(39);
            terminal.SetColor("green");
            terminal.WriteLine($"{item.Description}");
            terminal.SetColor("white");
            terminal.WriteLine(Loc.Get("dungeon.merchant_added_inventory"));
            terminal.WriteLine("");
            terminal.SetColor("gray");
            terminal.WriteLine(Loc.Get("dungeon.merchant_fine_choice"));
        }
        else
        {
            terminal.SetColor("gray");
            terminal.WriteLine(Loc.Get("dungeon.merchant_another_time"));
        }

        await Task.Delay(2000);
    }
    
    /// <summary>
    /// Witch Doctor encounter - Pascal DUNGEV2.PAS
    /// </summary>
    private async Task WitchDoctorEncounter()
    {
        terminal.SetColor("magenta");
        terminal.WriteLine(Loc.Get("dungeon.witch_doctor_header"));
        terminal.WriteLine("");

        var currentPlayer = GetCurrentPlayer();
        long cost = currentPlayer.Level * 12500;

        if (currentPlayer.Gold >= cost)
        {
            terminal.WriteLine(Loc.Get("dungeon.witch_doctor_meet"));
            terminal.WriteLine(Loc.Get("dungeon.witch_doctor_demand", cost));
            terminal.WriteLine("");

            var choice = await terminal.GetInput(Loc.Get("dungeon.witch_doctor_prompt"));

            if (choice.ToUpper() == "P")
            {
                currentPlayer.Gold -= cost;
                terminal.WriteLine(Loc.Get("dungeon.witch_doctor_pay"), "yellow");
                terminal.WriteLine(Loc.Get("dungeon.witch_doctor_vanish"));
            }
            else
            {
                // 50% chance to escape
                if (dungeonRandom.Next(2) == 0)
                {
                    terminal.WriteLine(Loc.Get("dungeon.witch_doctor_flee"), "green");
                }
                else
                {
                    terminal.WriteLine(Loc.Get("dungeon.witch_doctor_cursed"), "red");

                    // Random curse effect
                    var curseType = dungeonRandom.Next(3);
                    switch (curseType)
                    {
                        case 0:
                            var expLoss = currentPlayer.Level * 1500;
                            currentPlayer.Experience = Math.Max(0, currentPlayer.Experience - expLoss);
                            terminal.WriteLine(Loc.Get("dungeon.witch_doctor_lose_exp", expLoss));
                            break;
                        case 1:
                            var fightLoss = dungeonRandom.Next(5) + 1;
                            currentPlayer.Fights = Math.Max(0, currentPlayer.Fights - fightLoss);
                            terminal.WriteLine(Loc.Get("dungeon.witch_doctor_lose_fights", fightLoss));
                            break;
                        case 2:
                            var pfightLoss = dungeonRandom.Next(3) + 1;
                            currentPlayer.PFights = Math.Max(0, currentPlayer.PFights - pfightLoss);
                            terminal.WriteLine(Loc.Get("dungeon.witch_doctor_lose_pfights", pfightLoss));
                            break;
                    }
                }
            }
        }
        else
        {
            terminal.WriteLine(Loc.Get("dungeon.witch_doctor_no_gold"), "gray");
        }
        
        await Task.Delay(3000);
    }
    
    /// <summary>
    /// Create dungeon monster based on level and terrain
    /// </summary>
    private Monster CreateDungeonMonster(bool isLeader = false)
    {
        var monsterNames = GetMonsterNamesForTerrain(currentTerrain);
        var weaponArmor = GetWeaponArmorForTerrain(currentTerrain);
        
        var name = monsterNames[dungeonRandom.Next(monsterNames.Length)];
        var weapon = weaponArmor.weapons[dungeonRandom.Next(weaponArmor.weapons.Length)];
        var armor = weaponArmor.armor[dungeonRandom.Next(weaponArmor.armor.Length)];
        
        if (isLeader)
        {
            name = GetLeaderName(name);
        }
        
        // Smooth scaling factors – tuned for balanced difficulty curve
        float scaleFactor = 1f + (currentDungeonLevel / 20f); // every 20 levels → +100 %

        // Regular monsters are weaker, bosses are tougher (like the original game)
        float monsterMultiplier = isLeader ? 1.8f : 0.6f; // Regular monsters are 60% strength, bosses are 180%

        long hp = (long)(currentDungeonLevel * 4 * scaleFactor * monsterMultiplier); // survivability

        int strength = (int)(currentDungeonLevel * 1.5f * scaleFactor * monsterMultiplier); // base damage
        int punch    = (int)(currentDungeonLevel * 1.2f * scaleFactor * monsterMultiplier); // natural attacks
        int weapPow  = (int)(currentDungeonLevel * 0.9f * scaleFactor * monsterMultiplier); // weapon bonus
        int armPow   = (int)(currentDungeonLevel * 0.9f * scaleFactor * monsterMultiplier); // defense bonus

        var monster = Monster.CreateMonster(
            nr: currentDungeonLevel,
            name: name,
            hps: hp,
            strength: strength,
            defence: 0,
            phrase: GetMonsterPhrase(currentTerrain),
            grabweap: dungeonRandom.NextDouble() < 0.3,
            grabarm: false,
            weapon: weapon,
            armor: armor,
            poisoned: false,
            disease: false,
            punch: punch,
            armpow: armPow,
            weappow: weapPow
        );
        
        if (isLeader)
        {
            monster.IsMiniBoss = true;  // Terrain encounter leaders are elites, not floor bosses
        }
        
        // Store level for other systems (initiative scaling etc.)
        monster.Level = currentDungeonLevel;
        
        return monster;
    }
    
    // Helper methods for monster creation
    private string[] GetMonsterNamesForTerrain(DungeonTerrain terrain)
    {
        return terrain switch
        {
            DungeonTerrain.Underground => new[] { "Orc", "Half-Orc", "Goblin", "Troll", "Skeleton" },
            DungeonTerrain.Mountains => new[] { "Mountain Bandit", "Hill Giant", "Stone Golem", "Dwarf Warrior" },
            DungeonTerrain.Desert => new[] { "Robber Knight", "Robber Squire", "Desert Nomad", "Sand Troll" },
            DungeonTerrain.Forest => new[] { "Tree Hunter", "Green Threat", "Forest Bandit", "Wild Beast" },
            DungeonTerrain.Caves => new[] { "Cave Troll", "Underground Drake", "Deep Dweller", "Rock Monster" },
            _ => new[] { "Monster", "Creature", "Beast", "Fiend" }
        };
    }
    
    private (string[] weapons, string[] armor) GetWeaponArmorForTerrain(DungeonTerrain terrain)
    {
        return terrain switch
        {
            DungeonTerrain.Underground => (
                new[] { "Sword", "Spear", "Axe", "Club" },
                new[] { "Leather", "Chain-mail", "Cloth" }
            ),
            DungeonTerrain.Mountains => (
                new[] { "War Hammer", "Battle Axe", "Mace" },
                new[] { "Chain-mail", "Scale Mail", "Plate" }
            ),
            DungeonTerrain.Desert => (
                new[] { "Lance", "Scimitar", "Javelin" },
                new[] { "Chain-Mail", "Leather", "Robes" }
            ),
            DungeonTerrain.Forest => (
                new[] { "Silver Dagger", "Sling", "Sharp Stick", "Bow" },
                new[] { "Cloth", "Leather", "Bark Armor" }
            ),
            _ => (
                new[] { "Rusty Sword", "Broken Spear", "Old Club" },
                new[] { "Torn Clothes", "Rags", "Nothing" }
            )
        };
    }
    
    private string GetLeaderName(string baseName)
    {
        return baseName + " Leader";
    }

    /// <summary>
    /// Get the plural form of a monster name for display purposes.
    /// Handles common English pluralization rules.
    /// </summary>
    private string GetPluralName(string name)
    {
        if (string.IsNullOrEmpty(name))
            return name;

        // Handle special cases
        var lowerName = name.ToLower();

        // Irregular plurals
        if (lowerName.EndsWith("wolf")) return name.Substring(0, name.Length - 1) + "ves";
        if (lowerName.EndsWith("thief")) return name.Substring(0, name.Length - 1) + "ves";
        if (lowerName.EndsWith("elf")) return name.Substring(0, name.Length - 1) + "ves";
        if (lowerName == "dwarf") return name + "s"; // Dwarfs or Dwarves both acceptable
        if (lowerName.EndsWith("man")) return name.Substring(0, name.Length - 2) + "en";

        // Words ending in s, x, z, ch, sh - add "es"
        if (lowerName.EndsWith("s") || lowerName.EndsWith("x") || lowerName.EndsWith("z") ||
            lowerName.EndsWith("ch") || lowerName.EndsWith("sh"))
            return name + "es";

        // Words ending in consonant + y - change y to ies
        if (lowerName.EndsWith("y") && lowerName.Length > 1)
        {
            char beforeY = lowerName[lowerName.Length - 2];
            if (!"aeiou".Contains(beforeY))
                return name.Substring(0, name.Length - 1) + "ies";
        }

        // Default: just add s
        return name + "s";
    }
    
    private string GetMonsterPhrase(DungeonTerrain terrain)
    {
        var phrases = terrain switch
        {
            DungeonTerrain.Underground => new[] { "Trespasser!", "Attack!", "Kill them!", "No mercy!" },
            DungeonTerrain.Mountains => new[] { "Give yourself up!", "Take no prisoners!", "For the clan!" },
            DungeonTerrain.Desert => new[] { "No prisoners!", "Your gold or your life!", "Die, infidel!" },
            DungeonTerrain.Forest => new[] { "Wrong way, lads!", "Protect the trees!", "Nature's revenge!" },
            _ => new[] { "Grrargh!", "Attack!", "Die!", "No escape!" }
        };
        
        return phrases[dungeonRandom.Next(phrases.Length)];
    }
    
    // Additional helper methods
    private async Task ShowExplorationText()
    {
        var explorationTexts = new[]
        {
            Loc.Get("dungeon.explore_1"),
            Loc.Get("dungeon.explore_2"),
            Loc.Get("dungeon.explore_3"),
            Loc.Get("dungeon.explore_4"),
            Loc.Get("dungeon.explore_5")
        };

        terminal.WriteLine(explorationTexts[dungeonRandom.Next(explorationTexts.Length)], "gray");
        await Task.Delay(2000);
    }
    
    private string GetTerrainDescription(DungeonTerrain terrain)
    {
        return terrain switch
        {
            DungeonTerrain.Underground => Loc.Get("dungeon.terrain_underground"),
            DungeonTerrain.Mountains => Loc.Get("dungeon.terrain_mountains"),
            DungeonTerrain.Desert => Loc.Get("dungeon.terrain_desert"),
            DungeonTerrain.Forest => Loc.Get("dungeon.terrain_forest"),
            DungeonTerrain.Caves => Loc.Get("dungeon.terrain_caves"),
            _ => Loc.Get("dungeon.terrain_unknown")
        };
    }
    
    // Placeholder methods for features to implement
    private async Task DescendDeeper()
    {
        var player = GetCurrentPlayer();
        var playerLevel = player?.Level ?? 1;
        int maxAccessible = Math.Min(maxDungeonLevel, playerLevel + 10);

        // Old God floors are hard gates - must defeat the god before descending
        if (RequiresFloorClear() && !IsFloorCleared())
        {
            terminal.WriteLine("", "red");
            terminal.WriteLine(Loc.Get("dungeon.presence_blocks"), "bright_red");
            terminal.WriteLine(Loc.Get("dungeon.must_defeat_old_god"), "yellow");
            terminal.WriteLine($"({GetRemainingClearInfo()})", "gray");
            await Task.Delay(2500);
            return;
        }

        // Check if player can descend (limited to player level + 10)
        if (currentDungeonLevel >= maxAccessible)
        {
            terminal.WriteLine(Loc.Get("dungeon.cant_venture_deeper", maxAccessible), "yellow");
            terminal.WriteLine(Loc.Get("dungeon.level_up_access"), "gray");
        }
        else if (currentDungeonLevel < maxDungeonLevel)
        {
            int nextLevel = currentDungeonLevel + 1;
            var floorResult = GenerateOrRestoreFloor(player, nextLevel);
            currentFloor = floorResult.Floor;
            currentDungeonLevel = nextLevel;
            if (player != null) player.CurrentLocation = $"Dungeon Floor {currentDungeonLevel}";
            terminal.WriteLine(Loc.Get("dungeon.descend_to", currentDungeonLevel), "yellow");

            // Update quest progress for reaching this floor
            if (player != null)
            {
                QuestSystem.OnDungeonFloorReached(player, currentDungeonLevel);
            }
        }
        else
        {
            terminal.WriteLine(Loc.Get("dungeon.deepest_level"), "red");
        }
        await Task.Delay(1500);
        RequestRedisplay();
    }

    private async Task AscendToSurface()
    {
        // No level restrictions for ascending
        if (currentDungeonLevel > 1)
        {
            currentDungeonLevel--;
            terminal.WriteLine(Loc.Get("dungeon.ascend_to", currentDungeonLevel), "green");

            // Update quest progress for reaching this floor
            var player = GetCurrentPlayer();
            if (player != null)
            {
                QuestSystem.OnDungeonFloorReached(player, currentDungeonLevel);
            }
        }
        else
        {
            await NavigateToLocation(GameLocation.MainStreet);
        }
        await Task.Delay(1500);
        RequestRedisplay();
    }

    private async Task ManageTeam()
    {
        var player = GetCurrentPlayer();

        // Defensive deduplication — remove any duplicate entries that slipped in
        DeduplicateTeammates();

        terminal.ClearScreen();
        WriteBoxHeader(Loc.Get("dungeon.party_management"), "bright_cyan", 51);
        terminal.WriteLine("");

        // Check if player has any potential party members (team, spouse, or companions)
        bool hasTeam = !string.IsNullOrEmpty(player.Team);
        bool hasSpouse = UsurperRemake.Systems.RomanceTracker.Instance?.IsMarried == true;
        var companionSystem = UsurperRemake.Systems.CompanionSystem.Instance;
        bool hasAnyCompanions = companionSystem?.GetRecruitedCompanions()?.Any() == true;

        if (!hasTeam && !hasSpouse && !hasAnyCompanions && teammates.Count == 0)
        {
            terminal.SetColor("yellow");
            terminal.WriteLine(Loc.Get("dungeon.no_one_to_bring"));
            terminal.WriteLine(Loc.Get("dungeon.get_team"));
            terminal.WriteLine("");
            await terminal.PressAnyKey();
            return;
        }

        terminal.SetColor("white");
        if (hasTeam)
        {
            terminal.WriteLine(Loc.Get("dungeon.team_label", player.Team));
            terminal.WriteLine(Loc.Get("dungeon.team_turf", (player.CTurf ? Loc.Get("ui.yes") : Loc.Get("ui.no"))));
        }
        else
        {
            terminal.WriteLine(Loc.Get("dungeon.no_team_loved_ones"));
        }
        terminal.WriteLine("");

        // Show current dungeon party
        terminal.SetColor("cyan");
        terminal.WriteLine(Loc.Get("dungeon.current_dungeon_party"));
        terminal.WriteLine($"  1. {player.DisplayName} (You) - Level {player.Level} {player.ClassName}");
        for (int i = 0; i < teammates.Count; i++)
        {
            var tm = teammates[i];
            string status = tm.IsAlive ? $"{Loc.Get("combat.bar_hp")}: {tm.HP}/{tm.MaxHP}" : Loc.Get("combat.injured");
            string tag = tm.IsGroupedPlayer ? " [Player]" : "";
            if (tm.IsGroupedPlayer) terminal.SetColor("bright_green");
            terminal.WriteLine($"  {i + 2}. {tm.DisplayName}{tag} - Level {tm.Level} {tm.ClassName} - {status}");
            if (tm.IsGroupedPlayer) terminal.SetColor("cyan");
        }
        terminal.WriteLine("");

        // Get available NPC teammates from same team (only if player has a team)
        var npcTeammates = new List<NPC>();
        if (!string.IsNullOrEmpty(player.Team))
        {
            npcTeammates = UsurperRemake.Systems.NPCSpawnSystem.Instance.ActiveNPCs
                .Where(n => n.Team == player.Team && n.IsAlive && !teammates.Contains(n))
                .ToList();
        }

        // Add spouse as potential teammate (if married) - spouse can always join
        // Dead NPCs cannot join the party
        NPC? spouseNpc = null;
        var romance = UsurperRemake.Systems.RomanceTracker.Instance;
        if (romance?.IsMarried == true)
        {
            var spouse = romance.PrimarySpouse;
            if (spouse != null)
            {
                spouseNpc = UsurperRemake.Systems.NPCSpawnSystem.Instance?.ActiveNPCs?
                    .FirstOrDefault(n => n.ID == spouse.NPCId && n.IsAlive && !n.IsDead && !teammates.Contains(n) && !npcTeammates.Contains(n));
                if (spouseNpc != null)
                {
                    npcTeammates.Insert(0, spouseNpc); // Spouse first in list
                }
            }
        }

        // Add lovers as potential party members too
        // Dead NPCs cannot join the party
        if (romance != null)
        {
            foreach (var lover in romance.CurrentLovers)
            {
                var loverNpc = UsurperRemake.Systems.NPCSpawnSystem.Instance?.ActiveNPCs?
                    .FirstOrDefault(n => n.ID == lover.NPCId && n.IsAlive && !n.IsDead && !teammates.Contains(n) && !npcTeammates.Contains(n));
                if (loverNpc != null)
                {
                    npcTeammates.Add(loverNpc);
                }
            }
        }

        // Get inactive companions (recruited but not currently in party)
        var inactiveCompanions = companionSystem?.GetInactiveCompanions()?.ToList() ?? new List<UsurperRemake.Systems.Companion>();

        // Show available companions section
        if (inactiveCompanions.Count > 0)
        {
            terminal.SetColor("bright_cyan");
            terminal.WriteLine(Loc.Get("dungeon.available_companions"));
            for (int i = 0; i < inactiveCompanions.Count; i++)
            {
                var comp = inactiveCompanions[i];
                terminal.SetColor("cyan");
                terminal.WriteLine($"  [C{i + 1}] {comp.Name} ({comp.CombatRole}) - Level {comp.Level}");
            }
            terminal.WriteLine("");
        }

        if (npcTeammates.Count > 0)
        {
            terminal.SetColor("green");
            terminal.WriteLine(Loc.Get("dungeon.available_allies"));
            var balanceSystem = UsurperRemake.Systems.TeamBalanceSystem.Instance;
            for (int i = 0; i < npcTeammates.Count; i++)
            {
                var npc = npcTeammates[i];
                bool isSpouse = spouseNpc != null && npc.ID == spouseNpc.ID;
                bool isLover = romance?.CurrentLovers?.Any(l => l.NPCId == npc.ID) == true;
                long fee = balanceSystem.CalculateEntryFee(player, npc);
                string feeStr = fee > 0 ? $" [Fee: {fee:N0}g]" : "";

                if (isSpouse)
                {
                    terminal.SetColor("bright_magenta");
                    terminal.Write($"  [{i + 1}] <3 {npc.DisplayName} (Spouse) - Level {npc.Level} {npc.ClassName} - HP: {npc.HP}/{npc.MaxHP}");
                    if (fee > 0) { terminal.SetColor("yellow"); terminal.Write(feeStr); }
                    terminal.WriteLine("");
                    terminal.SetColor("green");
                }
                else if (isLover)
                {
                    terminal.SetColor("magenta");
                    terminal.Write($"  [{i + 1}] <3 {npc.DisplayName} (Lover) - Level {npc.Level} {npc.ClassName} - HP: {npc.HP}/{npc.MaxHP}");
                    if (fee > 0) { terminal.SetColor("yellow"); terminal.Write(feeStr); }
                    terminal.WriteLine("");
                    terminal.SetColor("green");
                }
                else
                {
                    terminal.Write($"  [{i + 1}] {npc.DisplayName} - Level {npc.Level} {npc.ClassName} - HP: {npc.HP}/{npc.MaxHP}");
                    if (fee > 0) { terminal.SetColor("yellow"); terminal.Write(feeStr); }
                    terminal.WriteLine("");
                    terminal.SetColor("green");
                }
            }
            terminal.WriteLine("");
        }
        else if (teammates.Count == 0)
        {
            terminal.SetColor("gray");
            terminal.WriteLine(Loc.Get("dungeon.no_allies_available"));
            terminal.WriteLine("");
        }

        // Show options
        terminal.SetColor("white");
        terminal.WriteLine(Loc.Get("dungeon.options"));
        if (inactiveCompanions.Count > 0 && teammates.Count < 4)
        {
            terminal.Write("  [");
            terminal.SetColor("bright_yellow");
            terminal.Write("C1");
            terminal.SetColor("white");
            terminal.Write("-");
            terminal.SetColor("bright_yellow");
            terminal.Write("C" + inactiveCompanions.Count);
            terminal.SetColor("white");
            terminal.WriteLine(Loc.Get("dungeon.add_companion_party"));
        }
        if (npcTeammates.Count > 0 && teammates.Count < 4) // Max 4 teammates + player = 5
        {
            terminal.Write("  [");
            terminal.SetColor("bright_yellow");
            terminal.Write("A");
            terminal.SetColor("white");
            terminal.WriteLine(Loc.Get("dungeon.add_ally"));
        }
        if (teammates.Count > 0)
        {
            terminal.Write("  [");
            terminal.SetColor("bright_yellow");
            terminal.Write("R");
            terminal.SetColor("white");
            terminal.WriteLine(Loc.Get("dungeon.remove_ally"));
        }
        // XP distribution percentages (only count non-grouped, non-echo teammates)
        int xpEligibleCount = teammates.Count(t => t != null && !t.IsGroupedPlayer && !t.IsEcho);
        int totalPct = player.TeamXPPercent.Take(1 + xpEligibleCount).Sum();
        terminal.Write("  [");
        terminal.SetColor("bright_yellow");
        terminal.Write("X");
        terminal.SetColor("white");
        terminal.WriteLine(Loc.Get("dungeon.xp_distribution_pct", totalPct));
        terminal.Write("  [");
        terminal.SetColor("bright_yellow");
        terminal.Write("B");
        terminal.SetColor("white");
        terminal.WriteLine(Loc.Get("dungeon.back_dungeon_menu"));
        terminal.WriteLine("");

        var choice = await terminal.GetInput(Loc.Get("ui.choice"));
        choice = choice.ToUpper().Trim();

        // Handle companion add (C1, C2, etc.)
        if (choice.StartsWith("C") && choice.Length >= 2)
        {
            if (int.TryParse(choice.Substring(1), out int compIndex) && compIndex >= 1 && compIndex <= inactiveCompanions.Count)
            {
                if (teammates.Count >= 4)
                {
                    terminal.WriteLine(Loc.Get("dungeon.party_full_max4"), "yellow");
                    await Task.Delay(1500);
                }
                else
                {
                    await AddCompanionToParty(inactiveCompanions[compIndex - 1]);
                }
            }
            else
            {
                terminal.WriteLine(Loc.Get("dungeon.invalid_companion"), "red");
                await Task.Delay(1500);
            }
            return;
        }

        switch (choice)
        {
            case "A":
                if (npcTeammates.Count > 0 && teammates.Count < 4)
                {
                    await AddTeammateToParty(npcTeammates);
                }
                else if (teammates.Count >= 4)
                {
                    terminal.WriteLine(Loc.Get("dungeon.party_full_max4"), "yellow");
                    await Task.Delay(1500);
                }
                else
                {
                    terminal.WriteLine(Loc.Get("dungeon.no_team_members"), "gray");
                    await Task.Delay(1500);
                }
                break;

            case "R":
                if (teammates.Count > 0)
                {
                    await RemoveTeammateFromParty();
                }
                else
                {
                    terminal.WriteLine(Loc.Get("dungeon.no_teammates_remove"), "gray");
                    await Task.Delay(1500);
                }
                break;

            case "X":
                await ShowXPDistributionMenu();
                return;

            case "B":
            default:
                break;
        }
    }

    /// <summary>
    /// Add an inactive companion back to the active party
    /// </summary>
    private async Task AddCompanionToParty(UsurperRemake.Systems.Companion companion)
    {
        var companionSystem = UsurperRemake.Systems.CompanionSystem.Instance;

        // Activate the companion
        if (companionSystem.ActivateCompanion(companion.Id))
        {
            // Get the companion as a Character and add to teammates
            var companionCharacters = companionSystem.GetCompanionsAsCharacters();
            var compChar = companionCharacters.FirstOrDefault(c => c.CompanionId == companion.Id);

            if (compChar != null && !teammates.Any(t => t.CompanionId == companion.Id))
            {
                teammates.Add(compChar);
            }

            terminal.SetColor("bright_green");
            terminal.WriteLine(Loc.Get("dungeon.companion_rejoins", companion.Name));
            terminal.SetColor("gray");
            terminal.WriteLine(Loc.Get("dungeon.companion_role", companion.CombatRole));
        }
        else
        {
            terminal.SetColor("red");
            terminal.WriteLine(Loc.Get("dungeon.could_not_add", companion.Name));
        }

        await Task.Delay(1500);
    }

    /// <summary>
    /// In-dungeon party management: view party stats and manage equipment between fights.
    /// </summary>
    private async Task ManagePartyInDungeon()
    {
        while (true)
        {
            terminal.ClearScreen();
            WriteBoxHeader("PARTY MANAGEMENT", "bright_cyan", 57);
            terminal.WriteLine("");

            // List party members with key stats
            for (int i = 0; i < teammates.Count; i++)
            {
                var tm = teammates[i];
                bool isCompanion = !tm.IsGroupedPlayer && !tm.IsEcho;
                string hpColor = tm.HP < tm.MaxHP * 0.3 ? "red" : tm.HP < tm.MaxHP * 0.7 ? "yellow" : "bright_green";

                terminal.SetColor("bright_yellow");
                terminal.Write($"  {i + 1}. ");
                terminal.SetColor("white");
                terminal.Write($"{tm.DisplayName} ");
                terminal.SetColor("gray");
                terminal.Write($"Lv{tm.Level} {tm.ClassName}  ");
                terminal.SetColor(hpColor);
                terminal.Write($"HP:{tm.HP}/{tm.MaxHP}");
                if (tm.IsManaClass)
                {
                    terminal.SetColor("cyan");
                    terminal.Write($"  MP:{tm.Mana}/{tm.MaxMana}");
                }
                terminal.WriteLine("");

                // Show equipped weapon and body armor on one line
                var mainHand = tm.GetEquipment(EquipmentSlot.MainHand);
                var body = tm.GetEquipment(EquipmentSlot.Body);
                terminal.SetColor("darkgray");
                terminal.Write("     ");
                if (mainHand != null)
                {
                    terminal.Write($"Wpn: ");
                    terminal.SetColor("gray");
                    terminal.Write($"{(mainHand.IsIdentified ? mainHand.Name : "???")}");
                    terminal.SetColor("darkgray");
                }
                if (body != null)
                {
                    terminal.Write($"  Armor: ");
                    terminal.SetColor("gray");
                    terminal.Write($"{(body.IsIdentified ? body.Name : "???")}");
                }
                terminal.WriteLine("");
            }

            terminal.WriteLine("");
            terminal.SetColor("cyan");
            terminal.WriteLine("  [#] View/equip member  [Q] Back");
            terminal.WriteLine("");
            terminal.SetColor("cyan");
            terminal.Write(Loc.Get("ui.choice"));
            terminal.SetColor("white");

            var choice = (await terminal.ReadLineAsync()).Trim().ToUpper();

            if (choice == "Q" || string.IsNullOrEmpty(choice))
                return;

            if (int.TryParse(choice, out int idx) && idx >= 1 && idx <= teammates.Count)
            {
                var selectedMember = teammates[idx - 1];

                // Don't allow managing grouped player equipment or echo characters
                if (selectedMember.IsGroupedPlayer)
                {
                    terminal.SetColor("yellow");
                    terminal.WriteLine("  Other players manage their own equipment.");
                    await Task.Delay(1500);
                    continue;
                }
                if (selectedMember.IsEcho)
                {
                    terminal.SetColor("yellow");
                    terminal.WriteLine("  Echo characters cannot be managed.");
                    await Task.Delay(1500);
                    continue;
                }

                await ManagePartyMemberEquipment(selectedMember);
            }
        }
    }

    /// <summary>
    /// Equipment management screen for a single party member in the dungeon.
    /// Uses the shared slot-based equip flow from BaseLocation.
    /// </summary>
    private async Task ManagePartyMemberEquipment(Character target)
    {
        while (true)
        {
            terminal.ClearScreen();
            WriteBoxHeader($"EQUIPMENT: {target.DisplayName.ToUpper()}", "bright_cyan", 57);
            terminal.WriteLine("");

            // Show target's stats
            terminal.SetColor("white");
            terminal.WriteLine($"  Level: {target.Level}  Class: {target.ClassName}  Race: {target.Race}");
            string hpColor = target.HP < target.MaxHP * 0.3 ? "red" : target.HP < target.MaxHP * 0.7 ? "yellow" : "bright_green";
            terminal.SetColor(hpColor);
            terminal.Write($"  HP: {target.HP}/{target.MaxHP}");
            if (target.IsManaClass)
            {
                terminal.SetColor("cyan");
                terminal.Write($"  MP: {target.Mana}/{target.MaxMana}");
            }
            terminal.WriteLine("");
            terminal.SetColor("white");
            terminal.WriteLine($"  STR: {target.Strength}  DEX: {target.Dexterity}  AGI: {target.Agility}  CON: {target.Constitution}");
            terminal.WriteLine($"  INT: {target.Intelligence}  WIS: {target.Wisdom}  CHA: {target.Charisma}  DEF: {target.Defence}");
            terminal.WriteLine("");

            // Show current equipment with stats
            terminal.SetColor("bright_yellow");
            terminal.WriteLine("  Equipment:");
            terminal.SetColor("white");

            DisplayEquipmentSlotWithStats(target, EquipmentSlot.MainHand, "Main Hand");
            DisplayEquipmentSlotWithStats(target, EquipmentSlot.OffHand, "Off Hand");
            DisplayEquipmentSlotWithStats(target, EquipmentSlot.Head, "Head");
            DisplayEquipmentSlotWithStats(target, EquipmentSlot.Body, "Body");
            DisplayEquipmentSlotWithStats(target, EquipmentSlot.Arms, "Arms");
            DisplayEquipmentSlotWithStats(target, EquipmentSlot.Hands, "Hands");
            DisplayEquipmentSlotWithStats(target, EquipmentSlot.Legs, "Legs");
            DisplayEquipmentSlotWithStats(target, EquipmentSlot.Feet, "Feet");
            DisplayEquipmentSlotWithStats(target, EquipmentSlot.Waist, "Belt");
            DisplayEquipmentSlotWithStats(target, EquipmentSlot.Face, "Face");
            DisplayEquipmentSlotWithStats(target, EquipmentSlot.Cloak, "Cloak");
            DisplayEquipmentSlotWithStats(target, EquipmentSlot.Neck, "Neck");
            DisplayEquipmentSlotWithStats(target, EquipmentSlot.LFinger, "Left Ring");
            DisplayEquipmentSlotWithStats(target, EquipmentSlot.RFinger, "Right Ring");
            terminal.WriteLine("");

            // Options
            terminal.SetColor("cyan");
            terminal.WriteLine("  [E] Equip item  [U] Unequip item  [Q] Back");
            terminal.WriteLine("");
            terminal.SetColor("cyan");
            terminal.Write(Loc.Get("ui.choice"));
            terminal.SetColor("white");

            var choice = (await terminal.ReadLineAsync()).Trim().ToUpper();

            switch (choice)
            {
                case "E":
                    await DungeonEquipItemToMember(target);
                    break;
                case "U":
                    await DungeonUnequipItemFromMember(target);
                    break;
                case "Q":
                case "":
                    return;
            }
        }
    }

    /// <summary>
    /// Slot-based equip flow for dungeon party members.
    /// </summary>
    private async Task DungeonEquipItemToMember(Character target)
    {
        terminal.ClearScreen();
        WriteBoxHeader($"EQUIP TO {target.DisplayName.ToUpper()}", "bright_cyan", 57);
        terminal.WriteLine("");

        // Step 1: Pick a slot
        var selectedSlot = await PromptForEquipmentSlot(target);
        if (selectedSlot == null) return;

        // Step 2: Get items that match this slot
        var equipmentItems = GetItemsForSlot(selectedSlot.Value);

        if (equipmentItems.Count == 0)
        {
            terminal.WriteLine("");
            terminal.SetColor("yellow");
            terminal.WriteLine("  No items available for this slot.");
            await Task.Delay(2000);
            return;
        }

        // Step 3: Show current item in slot
        terminal.WriteLine("");
        var currentItem = target.GetEquipment(selectedSlot.Value);
        terminal.SetColor("white");
        terminal.Write($"  Current: ");
        if (currentItem != null)
        {
            terminal.SetColor(currentItem.IsIdentified ? currentItem.GetRarityColor() : "magenta");
            terminal.Write(currentItem.IsIdentified ? currentItem.Name : "Unidentified");
            if (currentItem.IsIdentified) WriteEquipmentStatSummary(currentItem);
            terminal.WriteLine("");
        }
        else
        {
            terminal.SetColor("darkgray");
            terminal.WriteLine("Empty");
        }
        terminal.WriteLine("");

        // Step 4: Display matching items with full stats
        terminal.SetColor("white");
        terminal.WriteLine("  Available items:");
        terminal.WriteLine("");
        DisplayEquipmentItemList(equipmentItems, target);

        terminal.WriteLine("");
        terminal.SetColor("cyan");
        terminal.Write("  Item # (Q to cancel): ");
        terminal.SetColor("white");

        var input = await terminal.ReadLineAsync();
        if (!int.TryParse(input, out int itemIdx) || itemIdx < 1 || itemIdx > equipmentItems.Count)
        {
            terminal.SetColor("gray");
            terminal.WriteLine(Loc.Get("ui.cancelled"));
            await Task.Delay(1000);
            return;
        }

        var (selectedItem, wasEquipped, sourceSlot) = equipmentItems[itemIdx - 1];

        // Block unidentified items
        if (!selectedItem.IsIdentified)
        {
            terminal.SetColor("yellow");
            terminal.WriteLine("  Must identify the item first.");
            await Task.Delay(2000);
            return;
        }

        // Check if target can equip
        if (!selectedItem.CanEquip(target, out string equipReason))
        {
            terminal.SetColor("red");
            terminal.WriteLine($"  {target.DisplayName} cannot use this: {equipReason}");
            await Task.Delay(2000);
            return;
        }

        EquipmentSlot? targetSlot = selectedSlot.Value;

        // Remove from player
        if (wasEquipped && sourceSlot.HasValue)
        {
            currentPlayer.UnequipSlot(sourceSlot.Value);
            currentPlayer.RecalculateStats();
        }
        else
        {
            var invItem = currentPlayer.Inventory.FirstOrDefault(i => i.Name == selectedItem.Name);
            if (invItem != null)
                currentPlayer.Inventory.Remove(invItem);
        }

        // Track displaced items
        var targetInventoryBefore = target.Inventory.Count;

        var result = target.EquipItem(selectedItem, targetSlot, out string message);
        target.RecalculateStats();

        if (result)
        {
            // Move displaced items to player inventory
            if (target.Inventory.Count > targetInventoryBefore)
            {
                var displacedItems = target.Inventory.Skip(targetInventoryBefore).ToList();
                foreach (var displaced in displacedItems)
                {
                    target.Inventory.Remove(displaced);
                    currentPlayer.Inventory.Add(displaced);
                }
            }

            terminal.WriteLine("");
            terminal.SetColor("bright_green");
            terminal.WriteLine($"  {target.DisplayName} equipped {selectedItem.Name}.");
            if (!string.IsNullOrEmpty(message))
            {
                terminal.SetColor("yellow");
                terminal.WriteLine($"  {message}");
            }

            // Sync equipment and save — prevents item loss on disconnect
            if (target.IsCompanion)
                CompanionSystem.Instance?.SyncCompanionEquipment(target);
            _ = GameEngine.Instance.SaveCurrentGame();
            if (UsurperRemake.BBS.DoorMode.IsOnlineMode)
            {
                _ = Task.Run(async () =>
                {
                    try { await OnlineStateManager.Instance.SaveAllSharedState(); }
                    catch { /* best-effort */ }
                });
            }
        }
        else
        {
            // Failed - return item
            var legacyItem = currentPlayer.ConvertEquipmentToLegacyItem(selectedItem);
            currentPlayer.Inventory.Add(legacyItem);
            terminal.SetColor("red");
            terminal.WriteLine($"  Failed: {message}");
        }

        await Task.Delay(2000);
    }

    /// <summary>
    /// Unequip an item from a dungeon party member.
    /// </summary>
    private async Task DungeonUnequipItemFromMember(Character target)
    {
        terminal.ClearScreen();
        WriteBoxHeader($"UNEQUIP FROM {target.DisplayName.ToUpper()}", "bright_cyan", 57);
        terminal.WriteLine("");

        var equippedSlots = new List<(EquipmentSlot slot, Equipment item)>();
        foreach (EquipmentSlot slot in Enum.GetValues(typeof(EquipmentSlot)))
        {
            if (slot == EquipmentSlot.None) continue;
            var item = target.GetEquipment(slot);
            if (item != null)
                equippedSlots.Add((slot, item));
        }

        if (equippedSlots.Count == 0)
        {
            terminal.SetColor("yellow");
            terminal.WriteLine($"  {target.DisplayName} has no equipment.");
            await Task.Delay(2000);
            return;
        }

        for (int i = 0; i < equippedSlots.Count; i++)
        {
            var (slot, item) = equippedSlots[i];
            terminal.SetColor("bright_yellow");
            terminal.Write($"  {i + 1}. ");
            terminal.SetColor("gray");
            terminal.Write($"{slot.GetDisplayName(),-12}: ");
            terminal.SetColor(item.GetRarityColor());
            terminal.Write(item.Name);
            WriteEquipmentStatSummary(item);
            terminal.WriteLine("");
        }

        terminal.WriteLine("");
        terminal.SetColor("cyan");
        terminal.Write("  Unequip # (Q to cancel): ");
        terminal.SetColor("white");

        var input = await terminal.ReadLineAsync();
        if (!int.TryParse(input, out int idx) || idx < 1 || idx > equippedSlots.Count)
        {
            terminal.SetColor("gray");
            terminal.WriteLine(Loc.Get("ui.cancelled"));
            await Task.Delay(1000);
            return;
        }

        var (selectedSlot, selectedItem) = equippedSlots[idx - 1];

        // Unequip and give to player
        target.UnequipSlot(selectedSlot);
        target.RecalculateStats();

        var legacyItem = currentPlayer.ConvertEquipmentToLegacyItem(selectedItem);
        currentPlayer.Inventory.Add(legacyItem);

        // Sync equipment and save — prevents item loss on disconnect
        if (target.IsCompanion)
            CompanionSystem.Instance?.SyncCompanionEquipment(target);
        _ = GameEngine.Instance.SaveCurrentGame();
        if (UsurperRemake.BBS.DoorMode.IsOnlineMode)
        {
            _ = Task.Run(async () =>
            {
                try { await OnlineStateManager.Instance.SaveAllSharedState(); }
                catch { /* best-effort */ }
            });
        }

        terminal.WriteLine("");
        terminal.SetColor("bright_green");
        terminal.WriteLine($"  Took {selectedItem.Name} from {target.DisplayName}.");
        await Task.Delay(1500);
    }

    private async Task AddTeammateToParty(List<NPC> available)
    {
        var player = GetCurrentPlayer();
        terminal.WriteLine("");
        terminal.SetColor("white");
        var input = await terminal.GetInput(Loc.Get("dungeon.enter_team_number", available.Count));

        if (int.TryParse(input, out int index) && index >= 1 && index <= available.Count)
        {
            var npc = available[index - 1];

            // Check for dungeon entry fee for overleveled NPCs
            var balanceSystem = UsurperRemake.Systems.TeamBalanceSystem.Instance;
            long fee = balanceSystem.CalculateEntryFee(player, npc);

            if (fee > 0)
            {
                // Show fee info
                int levelGap = npc.Level - player.Level;
                terminal.WriteLine("");
                terminal.SetColor("yellow");
                terminal.WriteLine(Loc.Get("dungeon.npc_higher_level", npc.DisplayName, levelGap));
                terminal.WriteLine(Loc.Get("dungeon.npc_demand_fee", fee));
                terminal.SetColor("gray");
                terminal.WriteLine(Loc.Get("dungeon.your_gold", player.Gold));

                if (player.Gold < fee)
                {
                    terminal.SetColor("red");
                    terminal.WriteLine(Loc.Get("dungeon.cannot_afford_fee"));
                    await Task.Delay(2000);
                    return;
                }

                terminal.WriteLine("");
                terminal.SetColor("cyan");
                var confirm = await terminal.GetInput(Loc.Get("dungeon.pay_fee_confirm", fee));
                if (!confirm.ToUpper().StartsWith("Y"))
                {
                    terminal.SetColor("gray");
                    terminal.WriteLine(Loc.Get("dungeon.npc_shrugs", npc.DisplayName));
                    await Task.Delay(1500);
                    return;
                }

                // Deduct fee
                player.Gold -= fee;
                terminal.SetColor("green");
                terminal.WriteLine(Loc.Get("dungeon.paid_fee", fee));
            }

            // Defensive duplicate check by NPC ID before adding
            if (!teammates.Any(t => t is NPC existingNpc && existingNpc.ID == npc.ID))
            {
                teammates.Add(npc);
            }

            // Move NPC to dungeon
            npc.UpdateLocation("Dungeon");

            // Sync to GameEngine for persistence
            SyncNPCTeammatesToGameEngine();

            terminal.SetColor("green");
            terminal.WriteLine(Loc.Get("dungeon.npc_joins", npc.DisplayName));
            terminal.WriteLine(Loc.Get("dungeon.fight_alongside"));

            // Show XP penalty warning if applicable
            float xpMult = balanceSystem.CalculateXPMultiplier(player, teammates);
            if (xpMult < 1.0f)
            {
                terminal.SetColor("yellow");
                terminal.WriteLine(Loc.Get("dungeon.xp_penalty_warning", (int)(xpMult * 100)));
            }
            else
            {
                // 15% team XP/gold bonus for having teammates
                terminal.SetColor("cyan");
                terminal.WriteLine(Loc.Get("dungeon.team_bonus"));
            }
        }
        else
        {
            terminal.WriteLine(Loc.Get("ui.invalid_selection"), "red");
        }
        await Task.Delay(2000);
    }

    private async Task RemoveTeammateFromParty()
    {
        terminal.WriteLine("");
        // Show who can be removed
        terminal.SetColor("cyan");
        for (int i = 0; i < teammates.Count; i++)
        {
            var tm = teammates[i];
            string tag = tm.IsGroupedPlayer ? " [Player]" : "";
            terminal.WriteLine($"  {i + 2}. {tm.DisplayName}{tag} - Level {tm.Level} {tm.ClassName}");
        }
        terminal.SetColor("white");
        var input = await terminal.GetInput(Loc.Get("dungeon.enter_party_remove", teammates.Count + 1));

        // Convert from party number (2-based) to teammates index (0-based)
        // Party #2 = teammates[0], Party #3 = teammates[1], etc.
        if (int.TryParse(input, out int partyNumber) && partyNumber >= 2 && partyNumber <= teammates.Count + 1)
        {
            int index = partyNumber - 2;
            var member = teammates[index];

            // Grouped players cannot be removed via party management — they must /leave
            if (member.IsGroupedPlayer)
            {
                terminal.SetColor("yellow");
                terminal.WriteLine(Loc.Get("dungeon.grouped_player_leave", member.DisplayName));
                await Task.Delay(2000);
                return;
            }

            teammates.RemoveAt(index);

            // Handle companion removal - put them on standby, not "return to town"
            if (member.IsCompanion && member.CompanionId.HasValue)
            {
                var companionSystem = UsurperRemake.Systems.CompanionSystem.Instance;
                companionSystem.DeactivateCompanion(member.CompanionId.Value);

                terminal.SetColor("yellow");
                terminal.WriteLine(Loc.Get("dungeon.companion_steps_back", member.DisplayName));
                terminal.SetColor("gray");
                terminal.WriteLine(Loc.Get("dungeon.companion_available"));
            }
            else if (member is NPC npc)
            {
                // Move NPC back to town
                npc.UpdateLocation("Main Street");
                terminal.SetColor("yellow");
                terminal.WriteLine(Loc.Get("dungeon.npc_returns_town", member.DisplayName));
            }
            else
            {
                terminal.SetColor("yellow");
                terminal.WriteLine(Loc.Get("dungeon.member_leaves_party", member.DisplayName));
            }

            // Sync to GameEngine for persistence
            SyncNPCTeammatesToGameEngine();
        }
        else
        {
            terminal.WriteLine(Loc.Get("ui.invalid_selection"), "red");
        }
        await Task.Delay(1500);
    }

    private async Task ShowXPDistributionMenu()
    {
        var player = GetCurrentPlayer();

        while (true)
        {
            // Build list of XP-eligible teammates (skip grouped players and echoes — they have their own XP)
            var xpTeammates = teammates.Where(t => t != null && !t.IsGroupedPlayer && !t.IsEcho).ToList();

            terminal.ClearScreen();
            WriteSectionHeader(Loc.Get("dungeon.section_xp_dist"), "bright_cyan");
            terminal.WriteLine("");

            // Show grouped players note if any
            int groupedCount = teammates.Count(t => t != null && t.IsGroupedPlayer);
            if (groupedCount > 0)
            {
                terminal.SetColor("gray");
                terminal.WriteLine($"  {Loc.Get("dungeon.grouped_independent", groupedCount)}");
                terminal.WriteLine("");
            }

            // Show current party with percentages
            terminal.SetColor("white");
            terminal.WriteLine(Loc.Get("dungeon.current_party_xp"));

            // Slot 0 — Player
            terminal.SetColor("bright_white");
            string youLabel = $"  [0] You ({player.ClassName} Lv.{player.Level})";
            terminal.Write(youLabel.PadRight(40));
            terminal.SetColor("bright_green");
            terminal.WriteLine($": {player.TeamXPPercent[0],3}%");

            // Slots 1-4 — XP-eligible teammates only
            for (int i = 0; i < 4; i++)
            {
                int slotIndex = i + 1;
                int pct = slotIndex < player.TeamXPPercent.Length ? player.TeamXPPercent[slotIndex] : 0;

                if (i < xpTeammates.Count)
                {
                    var tm = xpTeammates[i];
                    string tmLabel = tm.IsCompanion
                        ? $"  [{slotIndex}] {tm.DisplayName} (Companion Lv.{tm.Level})"
                        : $"  [{slotIndex}] {tm.DisplayName} (Lv.{tm.Level} {tm.ClassName})";
                    terminal.SetColor("cyan");
                    terminal.Write(tmLabel.PadRight(40));
                    terminal.SetColor("bright_green");
                    terminal.WriteLine($": {pct,3}%");
                }
                else
                {
                    terminal.SetColor("gray");
                    string emptyLabel = $"  [{slotIndex}] (empty)";
                    terminal.Write(emptyLabel.PadRight(40));
                    terminal.WriteLine($": {pct,3}%");
                }
            }

            // Total across occupied slots
            int occupiedSlots = 1 + xpTeammates.Count;
            int total = player.TeamXPPercent.Take(Math.Min(occupiedSlots, player.TeamXPPercent.Length)).Sum();
            terminal.SetColor("white");
            terminal.Write("".PadRight(40));
            string totalColor = total > 100 ? "red" : total == 100 ? "bright_green" : "yellow";
            terminal.SetColor(totalColor);
            terminal.WriteLine($"  Total: {total}%");

            if (total < 100)
            {
                terminal.SetColor("yellow");
                terminal.WriteLine($"  {Loc.Get("dungeon.xp_unallocated", 100 - total)}");
            }

            terminal.WriteLine("");
            terminal.SetColor("white");
            terminal.WriteLine(Loc.Get("dungeon.xp_adjust_prompt"));
            terminal.WriteLine("");

            var input = (await terminal.GetInput(Loc.Get("ui.choice"))).Trim().ToUpper();

            if (input == "B" || string.IsNullOrEmpty(input))
            {
                break;
            }
            else if (input == "E")
            {
                // Even split across occupied slots
                int evenShare = 100 / occupiedSlots;
                int remainder = 100 - (evenShare * occupiedSlots);
                for (int s = 0; s < player.TeamXPPercent.Length; s++)
                {
                    if (s < occupiedSlots)
                        player.TeamXPPercent[s] = evenShare + (s == 0 ? remainder : 0);
                    else
                        player.TeamXPPercent[s] = 0;
                }
                terminal.SetColor("green");
                terminal.WriteLine(Loc.Get("dungeon.xp_even_split", evenShare + remainder, evenShare));
                await GameEngine.Instance.SaveCurrentGame();
                await Task.Delay(1500);
            }
            else if (int.TryParse(input, out int slot) && slot >= 0 && slot < TeamXPConfig.MaxTeamSlots)
            {
                // Check if slot is occupied (slot 0 = player, 1-4 = xpTeammates)
                if (slot > xpTeammates.Count)
                {
                    terminal.SetColor("red");
                    terminal.WriteLine(Loc.Get("dungeon.xp_slot_empty"));
                    await Task.Delay(1500);
                    continue;
                }

                string slotName = slot == 0 ? "You" : (slot <= xpTeammates.Count ? xpTeammates[slot - 1].DisplayName : "(empty)");

                // Calculate how much room is left (excluding this slot's current value)
                int currentSlotPct = player.TeamXPPercent[slot];
                int otherSlotsTotal = player.TeamXPPercent.Take(Math.Min(occupiedSlots, player.TeamXPPercent.Length)).Sum() - currentSlotPct;
                int maxAllowed = 100 - otherSlotsTotal;

                terminal.SetColor("cyan");
                var pctInput = await terminal.GetInput(Loc.Get("dungeon.xp_set_prompt", slotName, maxAllowed, currentSlotPct));

                if (int.TryParse(pctInput, out int newPct) && newPct >= 0)
                {
                    // Player slot must always get at least 10% XP
                    if (slot == 0 && newPct < 10)
                    {
                        terminal.SetColor("red");
                        terminal.WriteLine(Loc.Get("dungeon.xp_min_self"));
                        await Task.Delay(2000);
                    }
                    else if (newPct > maxAllowed)
                    {
                        terminal.SetColor("red");
                        terminal.WriteLine(Loc.Get("dungeon.xp_total_exceeded", otherSlotsTotal + newPct));
                        await Task.Delay(2000);
                    }
                    else
                    {
                        player.TeamXPPercent[slot] = newPct;
                        terminal.SetColor("green");
                        terminal.WriteLine(Loc.Get("dungeon.xp_set_to", slotName, newPct));
                        await GameEngine.Instance.SaveCurrentGame();
                        await Task.Delay(1000);
                    }
                }
                else
                {
                    terminal.SetColor("red");
                    terminal.WriteLine(Loc.Get("dungeon.invalid_percentage"));
                    await Task.Delay(1500);
                }
            }
            else
            {
                terminal.SetColor("red");
                terminal.WriteLine(Loc.Get("ui.invalid_choice"));
                await Task.Delay(1000);
            }
        }
    }

    /// <summary>
    /// Attempt to recruit a companion in the dungeon. Handles party full scenario.
    /// </summary>
    /// <param name="companionId">The companion to recruit</param>
    /// <param name="player">The player character</param>
    /// <returns>True if recruitment was successful</returns>
    private async Task<bool> TryRecruitCompanionInDungeon(UsurperRemake.Systems.CompanionId companionId, Character player)
    {
        var companionSystem = UsurperRemake.Systems.CompanionSystem.Instance;
        var companion = companionSystem.GetCompanion(companionId);

        if (companion == null || companion.IsRecruited || companion.IsDead)
            return false;

        // Count non-companion teammates (NPCs, spouse, etc.)
        int nonCompanionCount = teammates.Count(t => !t.IsCompanion);

        // Check if adding this companion would exceed the party cap
        // Max 4 teammates total. Companions are special but still count toward limit.
        if (teammates.Count >= 4)
        {
            terminal.WriteLine("");
            terminal.SetColor("yellow");
            terminal.WriteLine(Loc.Get("dungeon.party_full_allies"));
            terminal.WriteLine("");
            terminal.SetColor("white");
            terminal.WriteLine(Loc.Get("dungeon.make_room", companion.Name));
            terminal.WriteLine("");

            // Show current party members
            terminal.SetColor("cyan");
            terminal.WriteLine(Loc.Get("dungeon.current_party_members"));
            for (int i = 0; i < teammates.Count; i++)
            {
                var tm = teammates[i];
                string type = tm.IsCompanion ? "[Companion]" : "[Ally]";
                terminal.WriteLine($"  {i + 1}. {tm.DisplayName} - Level {tm.Level} {type}");
            }
            terminal.WriteLine("");

            terminal.SetColor("bright_yellow");
            if (IsScreenReader)
            {
                WriteSRMenuOption("R", Loc.Get("dungeon.remove_member"));
                WriteSRMenuOption("C", Loc.Get("dungeon.cancel_recruit"));
            }
            else
            {
                terminal.WriteLine(Loc.Get("dungeon.remove_make_room"));
                terminal.WriteLine(Loc.Get("dungeon.cancel_recruitment"));
            }
            terminal.WriteLine("");

            var removeChoice = await GetChoice();

            if (removeChoice.ToUpper() == "R")
            {
                terminal.WriteLine("");
                terminal.SetColor("white");
                terminal.WriteLine(Loc.Get("dungeon.who_should_leave"));
                terminal.WriteLine(Loc.Get("dungeon.companions_readd"));
                terminal.WriteLine("");

                var removeInput = await terminal.GetInput(Loc.Get("dungeon.enter_remove_number", teammates.Count));

                if (int.TryParse(removeInput, out int removeIndex) && removeIndex >= 1 && removeIndex <= teammates.Count)
                {
                    var memberToRemove = teammates[removeIndex - 1];
                    teammates.RemoveAt(removeIndex - 1);

                    // Handle removal based on type
                    if (memberToRemove is NPC npc)
                    {
                        npc.UpdateLocation("Main Street");
                    }

                    // If it was a companion, just deactivate them (they're still recruited)
                    if (memberToRemove.IsCompanion && memberToRemove.CompanionId.HasValue)
                    {
                        companionSystem.DeactivateCompanion(memberToRemove.CompanionId.Value);
                    }

                    SyncNPCTeammatesToGameEngine();

                    terminal.SetColor("yellow");
                    terminal.WriteLine(Loc.Get("dungeon.member_leaves_party", memberToRemove.DisplayName));
                    await Task.Delay(1000);
                }
                else
                {
                    terminal.WriteLine(Loc.Get("dungeon.invalid_cancelled"), "red");
                    await Task.Delay(1500);
                    return false;
                }
            }
            else
            {
                terminal.SetColor("gray");
                terminal.WriteLine(Loc.Get("dungeon.recruitment_cancelled"));
                await Task.Delay(1000);
                return false;
            }
        }

        // Now recruit the companion
        bool success = await companionSystem.RecruitCompanion(companionId, player, terminal);

        if (success)
        {
            // Add the companion to the dungeon party teammates list
            var companionCharacters = companionSystem.GetCompanionsAsCharacters();
            var newCompanionChar = companionCharacters.FirstOrDefault(c => c.CompanionId == companionId);

            if (newCompanionChar != null && !teammates.Any(t => t.CompanionId == companionId))
            {
                teammates.Add(newCompanionChar);
            }
        }

        return success;
    }

    private async Task ShowDungeonStatus()
    {
        await ShowStatus();
    }
    
    private async Task UsePotions()
    {
        var player = GetCurrentPlayer();

        // Get all party members: NPC teammates + Companions
        var allPartyMembers = GetAllPartyMembers();

        while (true)
        {
            // Refresh party members each loop (in case HP changed)
            allPartyMembers = GetAllPartyMembers();

            terminal.ClearScreen();
            WriteBoxHeader(Loc.Get("dungeon.potions_menu"), "cyan", 55);
            terminal.WriteLine("");

            // Show player status
            WriteSectionHeader(Loc.Get("dungeon.section_your_status"), "bright_white");
            terminal.SetColor("white");
            terminal.Write($"{Loc.Get("combat.bar_hp")}: ");
            DrawBar(player.HP, player.MaxHP, 25, "red", "darkgray");
            terminal.WriteLine($" {player.HP}/{player.MaxHP}");

            terminal.SetColor("yellow");
            terminal.WriteLine($"{Loc.Get("ui.healing_potions")}: {player.Healing}/{player.MaxPotions}");
            terminal.WriteLine($"{Loc.Get("ui.gold")}: {player.Gold:N0}");
            terminal.WriteLine("");

            // Show teammate status if we have party members
            if (allPartyMembers.Count > 0)
            {
                WriteSectionHeader(Loc.Get("dungeon.section_team_status"), "bright_cyan");
                foreach (var member in allPartyMembers)
                {
                    int hpPercent = member.MaxHP > 0 ? (int)(100 * member.HP / member.MaxHP) : 100;
                    string hpColor = hpPercent < 25 ? "red" : hpPercent < 50 ? "yellow" : hpPercent < 100 ? "bright_green" : "green";
                    terminal.SetColor(hpColor);
                    terminal.Write($"  {member.DisplayName,-18} ");
                    DrawBar(member.HP, member.MaxHP, 15, hpColor, "darkgray");
                    string status = hpPercent >= 100 ? " (Full)" : "";
                    terminal.WriteLine($" {member.HP}/{member.MaxHP}{status}");
                }
                terminal.WriteLine("");
            }

            // Calculate heal amount (potions heal 25% of max HP)
            long healAmount = player.MaxHP / 4;

            terminal.SetColor("white");
            terminal.WriteLine(Loc.Get("dungeon.options"));
            terminal.WriteLine("");

            // Calculate costs for display
            int costPerPotion = 50 + (player.Level * 10);

            if (IsScreenReader)
            {
                // Screen reader: plain text menu
                if (player.Healing > 0)
                    WriteSRMenuOption("U", Loc.Get("dungeon.use_potion_self", healAmount));
                else
                    WriteSRMenuOption("U", Loc.Get("dungeon.no_potions"));
                WriteSRMenuOption("B", Loc.Get("dungeon.buy_potions_monk", costPerPotion, Math.Max(75, player.Level * 3)));
                if (player.Healing > 0 && player.HP < player.MaxHP)
                    WriteSRMenuOption("H", Loc.Get("dungeon.heal_full"));
                if (allPartyMembers.Count > 0 && player.Healing > 0)
                    WriteSRMenuOption("T", Loc.Get("dungeon.heal_teammate"));
                bool anyTeammateInjured = allPartyMembers.Any(c => c.HP < c.MaxHP);
                if (allPartyMembers.Count > 0 && player.Healing > 0 && (player.HP < player.MaxHP || anyTeammateInjured))
                    WriteSRMenuOption("A", Loc.Get("dungeon.heal_all"));
                if (player.Antidotes > 0 && player.Poison > 0)
                    WriteSRMenuOption("D", Loc.Get("dungeon.use_antidote", player.Antidotes));
                else if (player.Antidotes > 0)
                    WriteSRMenuOption("D", Loc.Get("dungeon.use_antidote_not_poisoned", player.Antidotes));
                terminal.WriteLine("");
                WriteSRMenuOption("Q", Loc.Get("dungeon.return_dungeon"));
            }
            else
            {
                // Use potion option
                if (player.Healing > 0)
                {
                    terminal.SetColor("darkgray");
                    terminal.Write("  [");
                    terminal.SetColor("bright_yellow");
                    terminal.Write("U");
                    terminal.SetColor("darkgray");
                    terminal.Write("] ");
                    terminal.SetColor("white");
                    terminal.WriteLine(Loc.Get("dungeon.use_potion_self", healAmount));
                }
                else
                {
                    terminal.SetColor("darkgray");
                    terminal.WriteLine($"  [U] {Loc.Get("dungeon.no_potions_potion_menu")}");
                }

                // Buy potions option
                terminal.SetColor("darkgray");
                terminal.Write("  [");
                terminal.SetColor("bright_yellow");
                terminal.Write("B");
                terminal.SetColor("darkgray");
                terminal.Write("] ");
                terminal.SetColor("white");
                terminal.WriteLine(Loc.Get("dungeon.buy_potions_monk", costPerPotion, Math.Max(75, player.Level * 3)));

                // Quick heal - use potions until full
                if (player.Healing > 0 && player.HP < player.MaxHP)
                {
                    terminal.SetColor("darkgray");
                    terminal.Write("  [");
                    terminal.SetColor("bright_yellow");
                    terminal.Write("H");
                    terminal.SetColor("darkgray");
                    terminal.Write("] ");
                    terminal.SetColor("white");
                    terminal.WriteLine(Loc.Get("dungeon.heal_full"));
                }

                // Heal teammate option
                if (allPartyMembers.Count > 0 && player.Healing > 0)
                {
                    terminal.SetColor("darkgray");
                    terminal.Write("  [");
                    terminal.SetColor("bright_yellow");
                    terminal.Write("T");
                    terminal.SetColor("darkgray");
                    terminal.Write("] ");
                    terminal.SetColor("white");
                    terminal.WriteLine(Loc.Get("dungeon.heal_teammate"));
                }

                // Heal entire party option
                bool anyTeammateInjured = allPartyMembers.Any(c => c.HP < c.MaxHP);
                if (allPartyMembers.Count > 0 && player.Healing > 0 && (player.HP < player.MaxHP || anyTeammateInjured))
                {
                    terminal.SetColor("darkgray");
                    terminal.Write("  [");
                    terminal.SetColor("bright_yellow");
                    terminal.Write("A");
                    terminal.SetColor("darkgray");
                    terminal.Write("] ");
                    terminal.SetColor("white");
                    terminal.WriteLine(Loc.Get("dungeon.heal_all"));
                }

                // Antidote option
                if (player.Antidotes > 0 && player.Poison > 0)
                {
                    terminal.SetColor("darkgray");
                    terminal.Write("  [");
                    terminal.SetColor("bright_green");
                    terminal.Write("D");
                    terminal.SetColor("darkgray");
                    terminal.Write("] ");
                    terminal.SetColor("white");
                    terminal.WriteLine(Loc.Get("dungeon.use_antidote", player.Antidotes));
                }
                else if (player.Antidotes > 0)
                {
                    terminal.SetColor("darkgray");
                    terminal.WriteLine($"  [D] {Loc.Get("dungeon.use_antidote_not_poisoned", player.Antidotes)}");
                }

                terminal.WriteLine("");
                terminal.SetColor("darkgray");
                terminal.Write("  [");
                terminal.SetColor("bright_yellow");
                terminal.Write("Q");
                terminal.SetColor("darkgray");
                terminal.Write("] ");
                terminal.SetColor("white");
                terminal.WriteLine(Loc.Get("dungeon.return_dungeon"));
            }

            terminal.WriteLine("");
            terminal.SetColor("cyan");
            terminal.Write(Loc.Get("ui.choice"));
            terminal.SetColor("white");

            string choice = (await terminal.GetInput("")).Trim().ToUpper();

            switch (choice)
            {
                case "U":
                    if (player.Healing > 0)
                    {
                        await UseHealingPotion(player);
                    }
                    else
                    {
                        terminal.SetColor("red");
                        terminal.WriteLine(Loc.Get("dungeon.no_healing_potions"));
                        await Task.Delay(1500);
                    }
                    break;

                case "B":
                    await BuyPotionsFromMonk(player);
                    break;

                case "H":
                    if (player.Healing > 0 && player.HP < player.MaxHP)
                    {
                        await HealToFull(player);
                    }
                    break;

                case "T":
                    if (allPartyMembers.Count > 0 && player.Healing > 0)
                    {
                        await HealTeammate(player, allPartyMembers);
                    }
                    break;

                case "A":
                    if (allPartyMembers.Count > 0 && player.Healing > 0)
                    {
                        await HealEntireParty(player, allPartyMembers);
                    }
                    break;

                case "D":
                    if (player.Antidotes > 0 && player.Poison > 0)
                    {
                        player.Antidotes--;
                        player.Poison = 0;
                        player.PoisonTurns = 0;
                        terminal.SetColor("bright_green");
                        terminal.WriteLine(Loc.Get("dungeon.antidote_cure_poison"));
                        await Task.Delay(1500);
                    }
                    else if (player.Antidotes > 0)
                    {
                        terminal.SetColor("yellow");
                        terminal.WriteLine(Loc.Get("dungeon.not_poisoned"));
                        await Task.Delay(1000);
                    }
                    else
                    {
                        terminal.SetColor("red");
                        terminal.WriteLine(Loc.Get("dungeon.no_antidotes"));
                        await Task.Delay(1000);
                    }
                    break;

                case "Q":
                case "":
                    return;

                default:
                    terminal.SetColor("red");
                    terminal.WriteLine(Loc.Get("ui.invalid_choice"));
                    await Task.Delay(1000);
                    break;
            }
        }
    }

    /// <summary>
    /// Get all party members (NPC teammates + Companions) as Characters
    /// </summary>
    private List<Character> GetAllPartyMembers()
    {
        var result = new List<Character>();

        // Add NPC teammates (includes spouses, team members, etc.)
        foreach (var teammate in teammates)
        {
            if (teammate != null && teammate.IsAlive)
            {
                result.Add(teammate);
            }
        }

        // Add companions
        var companionSystem = UsurperRemake.Systems.CompanionSystem.Instance;
        var companions = companionSystem.GetCompanionsAsCharacters();
        foreach (var companion in companions)
        {
            // Avoid duplicates (if somehow an NPC is also tracked as a companion)
            if (!result.Any(r => r.DisplayName == companion.DisplayName))
            {
                result.Add(companion);
            }
        }

        return result;
    }

    /// <summary>
    /// Heal a specific teammate using the player's potions
    /// </summary>
    private async Task HealTeammate(Character player, List<Character> companions)
    {
        terminal.WriteLine("");
        terminal.SetColor("bright_cyan");
        terminal.WriteLine(Loc.Get("dungeon.select_teammate_heal"));
        terminal.WriteLine("");

        for (int i = 0; i < companions.Count; i++)
        {
            var companion = companions[i];
            int hpPercent = companion.MaxHP > 0 ? (int)(100 * companion.HP / companion.MaxHP) : 100;
            string hpColor = hpPercent < 25 ? "red" : hpPercent < 50 ? "yellow" : hpPercent < 100 ? "bright_green" : "green";
            terminal.SetColor(hpColor);
            string status = hpPercent >= 100 ? " (Full)" : "";
            terminal.WriteLine($"  [{i + 1}] {companion.DisplayName} - HP: {companion.HP}/{companion.MaxHP} ({hpPercent}%){status}");
        }
        terminal.SetColor("darkgray");
        terminal.Write("  [");
        terminal.SetColor("bright_yellow");
        terminal.Write("0");
        terminal.SetColor("darkgray");
        terminal.Write("] ");
        terminal.SetColor("gray");
        terminal.WriteLine(Loc.Get("dungeon.cancel"));
        terminal.WriteLine("");

        terminal.SetColor("white");
        var input = await terminal.GetInput(Loc.Get("dungeon.choose"));

        if (!int.TryParse(input, out int targetChoice) || targetChoice == 0)
        {
            return;
        }

        if (targetChoice < 1 || targetChoice > companions.Count)
        {
            terminal.WriteLine(Loc.Get("dungeon.merchant_invalid"), "red");
            await Task.Delay(1000);
            return;
        }

        var target = companions[targetChoice - 1];

        if (target.HP >= target.MaxHP)
        {
            terminal.WriteLine(Loc.Get("dungeon.already_full_health_name", target.DisplayName), "yellow");
            await Task.Delay(1500);
            return;
        }

        // Calculate potions needed
        long missingHP = target.MaxHP - target.HP;
        int healPerPotion = 30 + player.Level * 5 + 20;
        int potionsNeeded = (int)Math.Ceiling((double)missingHP / healPerPotion);
        potionsNeeded = Math.Min(potionsNeeded, (int)player.Healing);

        terminal.WriteLine("");
        terminal.SetColor("bright_cyan");
        terminal.WriteLine(Loc.Get("dungeon.missing_hp", target.DisplayName, missingHP));
        terminal.WriteLine(Loc.Get("dungeon.each_potion_heals", healPerPotion));
        terminal.WriteLine("");
        terminal.SetColor("darkgray");
        terminal.Write("[");
        terminal.SetColor("bright_yellow");
        terminal.Write("1");
        terminal.SetColor("darkgray");
        terminal.Write("] ");
        terminal.SetColor("white");
        terminal.WriteLine(Loc.Get("dungeon.use_1_potion"));
        if (potionsNeeded > 1)
        {
            terminal.SetColor("darkgray");
            terminal.Write("[");
            terminal.SetColor("bright_yellow");
            terminal.Write("F");
            terminal.SetColor("darkgray");
            terminal.Write("] ");
            terminal.SetColor("white");
            terminal.WriteLine(Loc.Get("dungeon.fully_heal_potions", potionsNeeded));
        }
        terminal.SetColor("darkgray");
        terminal.Write("[");
        terminal.SetColor("bright_yellow");
        terminal.Write("0");
        terminal.SetColor("darkgray");
        terminal.Write("] ");
        terminal.SetColor("gray");
        terminal.WriteLine(Loc.Get("dungeon.cancel"));
        terminal.WriteLine("");

        terminal.SetColor("white");
        terminal.Write(Loc.Get("ui.choice"));
        var potionChoice = (await terminal.GetInput("")).Trim().ToUpper();

        if (string.IsNullOrEmpty(potionChoice) || potionChoice == "0")
        {
            return;
        }

        int potionsToUse = 1;
        if (potionChoice == "F" && potionsNeeded > 1)
        {
            potionsToUse = potionsNeeded;
        }
        else if (potionChoice != "1")
        {
            terminal.WriteLine(Loc.Get("ui.invalid_choice"), "red");
            await Task.Delay(1000);
            return;
        }

        // Apply healing
        long oldHP = target.HP;
        for (int i = 0; i < potionsToUse && target.HP < target.MaxHP; i++)
        {
            player.Healing--;
            int healAmount = 30 + player.Level * 5 + dungeonRandom.Next(10, 31);
            target.HP = Math.Min(target.MaxHP, target.HP + healAmount);
        }
        long totalHeal = target.HP - oldHP;

        // Track statistics
        player.Statistics.RecordPotionUsed(totalHeal);

        terminal.WriteLine("");
        terminal.SetColor("bright_green");
        if (potionsToUse == 1)
        {
            terminal.WriteLine(Loc.Get("dungeon.give_potion_one", target.DisplayName));
        }
        else
        {
            terminal.WriteLine(Loc.Get("dungeon.give_potions_many", potionsToUse, target.DisplayName));
        }
        terminal.WriteLine(Loc.Get("dungeon.recovers_hp", target.DisplayName, totalHeal));

        if (target.HP >= target.MaxHP)
        {
            terminal.WriteLine(Loc.Get("dungeon.fully_healed_name", target.DisplayName), "bright_green");
        }

        // Sync companion HP
        if (target.IsCompanion && target.CompanionId.HasValue)
        {
            UsurperRemake.Systems.CompanionSystem.Instance.SyncCompanionHP(target);
        }

        await Task.Delay(2000);
    }

    /// <summary>
    /// Heal the entire party to full using player's potions
    /// </summary>
    private async Task HealEntireParty(Character player, List<Character> companions)
    {
        int healPerPotion = 30 + player.Level * 5 + 20;
        int totalPotionsUsed = 0;
        long totalHealing = 0;

        // Calculate total potions needed
        long playerMissing = player.MaxHP - player.HP;
        int playerPotionsNeeded = playerMissing > 0 ? (int)Math.Ceiling((double)playerMissing / healPerPotion) : 0;

        int teammatesPotionsNeeded = 0;
        foreach (var companion in companions)
        {
            long missing = companion.MaxHP - companion.HP;
            if (missing > 0)
            {
                teammatesPotionsNeeded += (int)Math.Ceiling((double)missing / healPerPotion);
            }
        }

        int totalPotionsNeeded = playerPotionsNeeded + teammatesPotionsNeeded;

        if (totalPotionsNeeded == 0)
        {
            terminal.WriteLine(Loc.Get("dungeon.everyone_full_health"), "yellow");
            await Task.Delay(1500);
            return;
        }

        int potionsAvailable = (int)player.Healing;
        int potionsToUse = Math.Min(totalPotionsNeeded, potionsAvailable);

        terminal.WriteLine("");
        terminal.SetColor("bright_cyan");
        terminal.WriteLine(Loc.Get("dungeon.heal_party_requires", totalPotionsNeeded));
        terminal.WriteLine(Loc.Get("dungeon.you_have_potions", potionsAvailable));
        terminal.WriteLine("");

        if (potionsAvailable < totalPotionsNeeded)
        {
            terminal.SetColor("yellow");
            terminal.WriteLine(Loc.Get("dungeon.not_enough_potions_warning", potionsAvailable));
            terminal.WriteLine("");
        }

        terminal.SetColor("darkgray");
        terminal.Write("[");
        terminal.SetColor("bright_yellow");
        terminal.Write("Y");
        terminal.SetColor("darkgray");
        terminal.Write("] ");
        terminal.SetColor("white");
        terminal.WriteLine(Loc.Get("dungeon.yes_heal_party", potionsToUse));
        terminal.SetColor("darkgray");
        terminal.Write("[");
        terminal.SetColor("bright_yellow");
        terminal.Write("N");
        terminal.SetColor("darkgray");
        terminal.Write("] ");
        terminal.SetColor("gray");
        terminal.WriteLine(Loc.Get("dungeon.cancel"));
        terminal.WriteLine("");

        terminal.SetColor("white");
        terminal.Write(Loc.Get("ui.choice"));
        var choice = (await terminal.GetInput("")).Trim().ToUpper();

        if (choice != "Y")
        {
            return;
        }

        terminal.WriteLine("");
        terminal.SetColor("bright_green");
        terminal.WriteLine(Loc.Get("dungeon.distributing_potions"));
        terminal.WriteLine("");

        // Heal player first
        if (player.HP < player.MaxHP && player.Healing > 0)
        {
            long oldHP = player.HP;
            while (player.HP < player.MaxHP && player.Healing > 0)
            {
                player.Healing--;
                totalPotionsUsed++;
                int healAmount = 30 + player.Level * 5 + dungeonRandom.Next(10, 31);
                player.HP = Math.Min(player.MaxHP, player.HP + healAmount);
            }
            long healed = player.HP - oldHP;
            totalHealing += healed;
            terminal.SetColor("green");
            terminal.WriteLine($"  {Loc.Get("dungeon.you_recover_hp", healed)}");
        }

        // Heal companions
        foreach (var companion in companions)
        {
            if (companion.HP < companion.MaxHP && player.Healing > 0)
            {
                long oldHP = companion.HP;
                while (companion.HP < companion.MaxHP && player.Healing > 0)
                {
                    player.Healing--;
                    totalPotionsUsed++;
                    int healAmount = 30 + player.Level * 5 + dungeonRandom.Next(10, 31);
                    companion.HP = Math.Min(companion.MaxHP, companion.HP + healAmount);
                }
                long healed = companion.HP - oldHP;
                totalHealing += healed;
                terminal.SetColor("green");
                terminal.WriteLine($"  {Loc.Get("dungeon.teammate_recovers", companion.DisplayName, healed)}");

                // Sync companion HP
                if (companion.IsCompanion && companion.CompanionId.HasValue)
                {
                    UsurperRemake.Systems.CompanionSystem.Instance.SyncCompanionHP(companion);
                }
            }
        }

        terminal.WriteLine("");
        terminal.SetColor("bright_green");
        terminal.WriteLine(Loc.Get("dungeon.used_potions_total", totalPotionsUsed, totalHealing));
        terminal.SetColor("gray");
        terminal.WriteLine(Loc.Get("dungeon.potions_remaining", player.Healing, player.MaxPotions));

        // Track statistics for total healing done
        if (totalPotionsUsed > 0)
        {
            // Record each potion used with average healing per potion
            for (int i = 0; i < totalPotionsUsed; i++)
            {
                player.Statistics.RecordPotionUsed(totalHealing / totalPotionsUsed);
            }
        }

        await Task.Delay(2500);
    }

    private async Task UseHealingPotion(Character player)
    {
        if (player.Healing <= 0)
        {
            terminal.SetColor("red");
            terminal.WriteLine(Loc.Get("dungeon.no_healing_potions"));
            await Task.Delay(1500);
            return;
        }

        if (player.HP >= player.MaxHP)
        {
            terminal.SetColor("yellow");
            terminal.WriteLine(Loc.Get("dungeon.already_full_health"));
            await Task.Delay(1500);
            return;
        }

        // Use one potion
        player.Healing--;
        long healAmount = player.MaxHP / 4;
        long oldHP = player.HP;
        player.HP = Math.Min(player.MaxHP, player.HP + healAmount);
        long actualHeal = player.HP - oldHP;

        // Track statistics
        player.Statistics.RecordPotionUsed(actualHeal);

        terminal.SetColor("bright_green");
        terminal.WriteLine("");
        terminal.WriteLine("*glug glug glug*");
        terminal.WriteLine(Loc.Get("dungeon.drink_potion_recover", actualHeal));
        terminal.Write($"{Loc.Get("combat.bar_hp")}: ");
        DrawBar(player.HP, player.MaxHP, 25, "red", "darkgray");
        terminal.WriteLine($" {player.HP}/{player.MaxHP}");
        terminal.SetColor("gray");
        terminal.WriteLine($"{Loc.Get("ui.potions_remaining")}: {player.Healing}/{player.MaxPotions}");

        await Task.Delay(2000);
    }

    private async Task HealToFull(Character player)
    {
        if (player.Healing <= 0)
        {
            terminal.SetColor("red");
            terminal.WriteLine(Loc.Get("dungeon.no_healing_potions"));
            await Task.Delay(1500);
            return;
        }

        if (player.HP >= player.MaxHP)
        {
            terminal.SetColor("yellow");
            terminal.WriteLine(Loc.Get("dungeon.already_full_health"));
            await Task.Delay(1500);
            return;
        }

        long healAmount = player.MaxHP / 4;
        int potionsNeeded = (int)Math.Ceiling((double)(player.MaxHP - player.HP) / healAmount);
        int potionsToUse = Math.Min(potionsNeeded, (int)player.Healing);

        terminal.SetColor("cyan");
        terminal.WriteLine(Loc.Get("dungeon.will_use_potions", potionsToUse));
        string confirm = (await terminal.GetInput("")).Trim().ToUpper();

        if (confirm != "Y")
        {
            terminal.SetColor("gray");
            terminal.WriteLine(Loc.Get("ui.cancelled"));
            await Task.Delay(1000);
            return;
        }

        long oldHP = player.HP;
        int actualPotionsUsed = 0;
        for (int i = 0; i < potionsToUse; i++)
        {
            player.Healing--;
            actualPotionsUsed++;
            player.HP = Math.Min(player.MaxHP, player.HP + healAmount);
            if (player.HP >= player.MaxHP) break;
        }
        long actualHeal = player.HP - oldHP;

        // Track statistics
        player.Statistics.RecordPotionUsed(actualHeal);

        terminal.SetColor("bright_green");
        terminal.WriteLine("");
        terminal.WriteLine("*glug glug glug* *glug glug*");
        terminal.WriteLine(Loc.Get("dungeon.drink_potions_recover", actualPotionsUsed, actualHeal));
        terminal.Write($"{Loc.Get("combat.bar_hp")}: ");
        DrawBar(player.HP, player.MaxHP, 25, "red", "darkgray");
        terminal.WriteLine($" {player.HP}/{player.MaxHP}");
        terminal.SetColor("gray");
        terminal.WriteLine($"{Loc.Get("ui.potions_remaining")}: {player.Healing}/{player.MaxPotions}");

        await Task.Delay(2000);
    }

    private async Task BuyPotionsFromMonk(Character player)
    {
        bool canBuyHealing = player.Healing < player.MaxPotions;
        bool canBuyMana = player.ManaPotions < player.MaxManaPotions; // v0.52.5: non-casters can buy for teammates

        terminal.ClearScreen();
        terminal.SetColor("cyan");
        terminal.WriteLine("");
        terminal.WriteLine(Loc.Get("dungeon.monk_appears"));
        terminal.WriteLine("");
        terminal.SetColor("white");
        terminal.WriteLine(Loc.Get("dungeon.monk_greeting"));
        terminal.WriteLine("");

        // Calculate costs
        int healCost = 50 + (player.Level * 10);
        int manaCost = Math.Max(75, player.Level * 3);

        terminal.SetColor("yellow");
        terminal.WriteLine(Loc.Get("dungeon.monk_your_gold", player.Gold));
        terminal.WriteLine("");

        if (canBuyHealing)
        {
            terminal.SetColor("darkgray");
            terminal.Write("[");
            terminal.SetColor("bright_yellow");
            terminal.Write("H");
            terminal.SetColor("darkgray");
            terminal.Write("]");
            terminal.SetColor("green");
            terminal.WriteLine($"ealing Potions - {healCost}g each ({player.Healing}/{player.MaxPotions})");
        }
        else
        {
            terminal.SetColor("darkgray");
            terminal.WriteLine($"[H]ealing Potions - FULL ({player.Healing}/{player.MaxPotions})");
        }

        if (canBuyMana)
        {
            terminal.SetColor("darkgray");
            terminal.Write("[");
            terminal.SetColor("bright_yellow");
            terminal.Write("M");
            terminal.SetColor("darkgray");
            terminal.Write("]");
            terminal.SetColor("blue");
            terminal.WriteLine($"ana Potions - {manaCost}g each ({player.ManaPotions}/{player.MaxManaPotions})");
        }
        else if (SpellSystem.HasSpells(player))
        {
            terminal.SetColor("darkgray");
            terminal.WriteLine($"[M]ana Potions - FULL ({player.ManaPotions}/{player.MaxManaPotions})");
        }

        terminal.SetColor("darkgray");
        terminal.Write("[");
        terminal.SetColor("bright_yellow");
        terminal.Write("C");
        terminal.SetColor("darkgray");
        terminal.Write("]");
        terminal.SetColor("gray");
        terminal.WriteLine("ancel");
        terminal.WriteLine("");

        terminal.SetColor("cyan");
        terminal.Write(Loc.Get("ui.choice"));
        terminal.SetColor("white");
        var choice = (await terminal.GetInput("")).Trim().ToUpper();

        if (choice == "H" && canBuyHealing)
        {
            await MonkBuyPotionTypeInDungeon(player, "healing", healCost,
                (int)player.Healing, player.MaxPotions,
                bought => { player.Healing += bought; });
        }
        else if (choice == "M" && canBuyMana)
        {
            await MonkBuyPotionTypeInDungeon(player, "mana", manaCost,
                (int)player.ManaPotions, player.MaxManaPotions,
                bought => { player.ManaPotions += bought; });
        }
        else
        {
            terminal.SetColor("gray");
            terminal.WriteLine(Loc.Get("dungeon.monk_another_time"));
            await Task.Delay(1500);
            return;
        }

        terminal.WriteLine("");
        terminal.SetColor("gray");
        terminal.WriteLine(Loc.Get("dungeon.monk_fades"));
        await Task.Delay(2000);
    }

    private async Task MonkBuyPotionTypeInDungeon(Character player, string potionType, int costPerPotion,
        int currentCount, int maxCount, Action<int> applyPurchase)
    {
        int roomForPotions = maxCount - currentCount;
        int maxAffordable = (int)(player.Gold / costPerPotion);
        int maxCanBuy = Math.Min(roomForPotions, maxAffordable);

        if (maxCanBuy <= 0)
        {
            if (roomForPotions <= 0)
            {
                terminal.SetColor("yellow");
                terminal.WriteLine(Loc.Get("dungeon.monk_max_potions", potionType));
            }
            else
            {
                terminal.SetColor("red");
                terminal.WriteLine(Loc.Get("dungeon.monk_no_gold"));
            }
            await terminal.PressAnyKey();
            return;
        }

        terminal.SetColor("cyan");
        terminal.WriteLine(Loc.Get("dungeon.monk_how_many", potionType, maxCanBuy));
        terminal.Write("> ");
        terminal.SetColor("white");

        var amountInput = await terminal.GetInput("");

        if (!int.TryParse(amountInput.Trim(), out int amount) || amount < 1)
        {
            terminal.SetColor("gray");
            terminal.WriteLine(Loc.Get("dungeon.monk_cancel"));
            await Task.Delay(1500);
            return;
        }

        if (amount > maxCanBuy)
        {
            terminal.SetColor("yellow");
            terminal.WriteLine(Loc.Get("dungeon.monk_only_provide", maxCanBuy));
            amount = maxCanBuy;
        }

        long totalCost = amount * costPerPotion;
        player.Gold -= totalCost;
        applyPurchase(amount);
        player.Statistics?.RecordPurchase(totalCost);
        player.Statistics?.RecordGoldSpent(totalCost);

        string color = potionType == "mana" ? "blue" : "bright_green";
        terminal.WriteLine("");
        terminal.SetColor(color);
        terminal.WriteLine(Loc.Get("dungeon.monk_purchased", amount, potionType, totalCost));
        terminal.SetColor("yellow");
        terminal.WriteLine(Loc.Get("dungeon.monk_gold_remaining", player.Gold));
    }
    
    private async Task ShowDungeonMap()
    {
        if (currentFloor == null)
        {
            terminal.WriteLine(Loc.Get("dungeon.no_floor_map"), "gray");
            await Task.Delay(1500);
            return;
        }

        if (GameConfig.ScreenReaderMode)
        {
            await ShowDungeonMapScreenReader();
            return;
        }

        terminal.ClearScreen();

        // Build spatial map from room connections
        var roomPositions = BuildRoomPositionMap();

        if (roomPositions.Count == 0)
        {
            terminal.WriteLine(Loc.Get("dungeon.map_unavailable"), "gray");
            await terminal.PressAnyKey();
            return;
        }

        // Find bounds
        int minX = roomPositions.Values.Min(p => p.x);
        int maxX = roomPositions.Values.Max(p => p.x);
        int minY = roomPositions.Values.Min(p => p.y);
        int maxY = roomPositions.Values.Max(p => p.y);

        // Create position lookup
        var posToRoom = new Dictionary<(int x, int y), DungeonRoom>();
        foreach (var kvp in roomPositions)
        {
            var room = currentFloor.Rooms.FirstOrDefault(r => r.Id == kvp.Key);
            if (room != null)
                posToRoom[kvp.Value] = room;
        }

        // Map dimensions for the grid (each room = 4 chars wide, 2 rows tall)
        int mapWidth = (maxX - minX + 1) * 4 + 1;

        // Header line
        string themeColor = GetThemeColor(currentFloor.Theme);
        terminal.SetColor(themeColor);
        int explored = currentFloor.Rooms.Count(r => r.IsExplored);
        int cleared = currentFloor.Rooms.Count(r => r.IsCleared);
        int total = currentFloor.Rooms.Count;
        terminal.WriteLine($" DUNGEON MAP ── Level {currentDungeonLevel} ({currentFloor.Theme})  [{explored}/{total} explored, {cleared}/{total} cleared]");
        terminal.SetColor("darkgray");
        terminal.WriteLine(new string('─', 78));

        // Render compact roguelike map
        // Each room: 1 char symbol, connections: ─ (horizontal), │ (vertical)
        // Layout: 4 chars per room column (room + padding), 2 rows per room row (room + vertical connector)
        int legendRow = 0;
        string[] legend = {
            "",
            "  Legend:",
            "",
            "  \u001b[93m@\u001b[0m You",      // placeholder, rendered manually
            "  \u001b[92m#\u001b[0m Cleared",
            "  \u001b[91m█\u001b[0m Monsters",
            "  \u001b[94m>\u001b[0m Stairs",
            "  \u001b[91mB\u001b[0m Boss",
            "  \u001b[96m·\u001b[0m Safe",
            "  \u001b[90m?\u001b[0m Unknown",
            "",
        };

        for (int y = minY; y <= maxY; y++)
        {
            // === Room row ===
            // Left padding for centering (map area is left ~50 chars, legend on right)
            terminal.Write("  ");
            for (int x = minX; x <= maxX; x++)
            {
                if (posToRoom.TryGetValue((x, y), out var room))
                {
                    // West connector — only draw if target room is at adjacent grid position
                    bool hasWest = room.Exits.TryGetValue(Direction.West, out var westExit)
                        && roomPositions.TryGetValue(westExit.TargetRoomId, out var westPos)
                        && westPos == (x - 1, y);
                    if (hasWest && room.IsExplored)
                    {
                        terminal.SetColor("darkgray");
                        terminal.Write("───");
                    }
                    else if (x > minX)
                    {
                        terminal.Write("   ");
                    }
                    else
                    {
                        terminal.Write("  ");
                    }

                    // Room symbol (1 char, colored)
                    char sym = GetRoomMapChar(room);
                    string color = GetRoomMapColor(room);
                    terminal.SetColor(color);
                    terminal.Write(sym.ToString());
                }
                else
                {
                    // Empty cell
                    terminal.Write(x > minX ? "    " : "   ");
                }
            }

            // Right-side legend
            if (legendRow < legend.Length)
                RenderLegendEntry(legendRow, 50);
            legendRow++;
            terminal.WriteLine("");

            // === Vertical connector row ===
            if (y < maxY)
            {
                terminal.Write("  ");
                for (int x = minX; x <= maxX; x++)
                {
                    if (posToRoom.TryGetValue((x, y), out var room))
                    {
                        // South connector — only draw if target room is at grid position below
                        bool hasSouth = room.Exits.TryGetValue(Direction.South, out var southExit)
                            && roomPositions.TryGetValue(southExit.TargetRoomId, out var southPos)
                            && southPos == (x, y + 1);
                        if (hasSouth && room.IsExplored)
                        {
                            terminal.SetColor("darkgray");
                            if (x == minX)
                                terminal.Write("  │");
                            else
                                terminal.Write("   │");
                        }
                        else
                        {
                            terminal.Write(x == minX ? "   " : "    ");
                        }
                    }
                    else
                    {
                        terminal.Write(x == minX ? "   " : "    ");
                    }
                }

                if (legendRow < legend.Length)
                    RenderLegendEntry(legendRow, 50);
                legendRow++;
                terminal.WriteLine("");
            }
        }

        // Print remaining legend entries if map was small
        while (legendRow < legend.Length)
        {
            terminal.Write(new string(' ', 50));
            RenderLegendEntry(legendRow, 0);
            legendRow++;
            terminal.WriteLine("");
        }

        terminal.SetColor("darkgray");
        terminal.WriteLine(new string('─', 78));

        // Current room info + boss status on one line
        var currentRoom = currentFloor.GetCurrentRoom();
        terminal.SetColor("white");
        terminal.Write(" Location: ");
        terminal.SetColor("yellow");
        terminal.Write(currentRoom?.Name ?? "Unknown");
        if (currentFloor.BossDefeated)
        {
            terminal.SetColor("bright_green");
            terminal.Write(GameConfig.ScreenReaderMode ? "  * BOSS DEFEATED" : "  ★ BOSS DEFEATED");
        }
        terminal.WriteLine("");

        terminal.WriteLine("");
        terminal.SetColor("gray");
        terminal.WriteLine(" Press Enter to continue...");
        await terminal.GetInput("");
    }

    private async Task ShowDungeonMapScreenReader()
    {
        await ShowDungeonNavigator();
    }

    private async Task ShowDungeonNavigator()
    {
        if (currentFloor == null)
        {
            terminal.WriteLine(Loc.Get("dungeon.no_floor_data"), "gray");
            await Task.Delay(1500);
            return;
        }

        terminal.ClearScreen();

        int explored = currentFloor!.Rooms.Count(r => r.IsExplored);
        int cleared = currentFloor.Rooms.Count(r => r.IsCleared);
        int total = currentFloor.Rooms.Count;

        var current = currentFloor.GetCurrentRoom();
        terminal.WriteLine(Loc.Get("dungeon.guide_title", currentDungeonLevel, currentFloor.Theme), "bright_yellow");
        if (current != null)
        {
            terminal.Write(Loc.Get("dungeon.you_are_in", current.Name), "white");
            terminal.WriteLine($" ({GetRoomStatusText(current)})", "gray");
            var exitDirs = current.Exits.Keys.OrderBy(d => d).Select(d => d.ToString());
            terminal.WriteLine(Loc.Get("dungeon.guide_exits", string.Join(", ", exitDirs)), "gray");
        }
        terminal.WriteLine($"{explored}/{total} explored, {cleared}/{total} cleared.", "gray");
        if (currentFloor.BossDefeated)
            terminal.WriteLine(Loc.Get("dungeon.boss_defeated"), "bright_green");
        terminal.WriteLine("");

        // Build BFS parent map from current room through explored rooms
        var parentMap = new Dictionary<string, (string parentId, Direction direction)>();
        var bfsQueue = new Queue<string>();
        var bfsVisited = new HashSet<string>();
        string startId = current?.Id ?? "";
        if (!string.IsNullOrEmpty(startId))
        {
            bfsVisited.Add(startId);
            bfsQueue.Enqueue(startId);
        }
        while (bfsQueue.Count > 0)
        {
            var roomId = bfsQueue.Dequeue();
            var room = currentFloor.Rooms.FirstOrDefault(r => r.Id == roomId);
            if (room == null) continue;
            foreach (var kvp in room.Exits)
            {
                if (bfsVisited.Contains(kvp.Value.TargetRoomId)) continue;
                var target = currentFloor.Rooms.FirstOrDefault(r => r.Id == kvp.Value.TargetRoomId);
                if (target == null) continue;
                bfsVisited.Add(kvp.Value.TargetRoomId);
                parentMap[kvp.Value.TargetRoomId] = (roomId, kvp.Key);
                // Only continue BFS through explored rooms — unexplored rooms are reachable but not waypoints
                if (target.IsExplored)
                    bfsQueue.Enqueue(kvp.Value.TargetRoomId);
            }
        }

        // Find targets
        string? stairsRoomId = null;
        string? bossRoomId = null;
        string? nearestUnexploredId = null;
        string? nearestUnclearedId = null;

        // BFS order guarantees nearest first, so iterate parentMap keys in insertion order
        foreach (var roomId in bfsVisited)
        {
            if (roomId == startId) continue;
            var room = currentFloor.Rooms.FirstOrDefault(r => r.Id == roomId);
            if (room == null) continue;

            if (stairsRoomId == null && room.HasStairsDown && room.IsExplored)
                stairsRoomId = roomId;
            if (bossRoomId == null && room.IsBossRoom && !room.IsCleared && room.IsExplored)
                bossRoomId = roomId;
            if (nearestUnexploredId == null && !room.IsExplored)
                nearestUnexploredId = roomId;
            if (nearestUnclearedId == null && room.IsExplored && !room.IsCleared && room.HasMonsters)
                nearestUnclearedId = roomId;
        }

        // Menu
        var options = new List<(string key, string label, string? targetId)>();
        if (nearestUnexploredId != null)
            options.Add(("U", "Nearest unexplored room", nearestUnexploredId));
        if (nearestUnclearedId != null)
            options.Add(("C", "Nearest uncleared room", nearestUnclearedId));
        if (stairsRoomId != null)
            options.Add(("S", "Stairs down", stairsRoomId));
        if (bossRoomId != null)
            options.Add(("B", "Boss room", bossRoomId));

        if (options.Count == 0)
        {
            terminal.WriteLine(Loc.Get("dungeon.no_known_destinations"), "gray");
            terminal.WriteLine("");
            await terminal.PressAnyKey();
            return;
        }

        terminal.WriteLine(Loc.Get("dungeon.directions_to"), "white");
        foreach (var opt in options)
        {
            var path = BuildDirectionPath(opt.targetId!, startId, parentMap);
            var room = currentFloor.Rooms.FirstOrDefault(r => r.Id == opt.targetId);
            string roomName = room?.Name ?? "unknown";
            string dist = path != null ? $"{path.Count} room{(path.Count == 1 ? "" : "s")}" : "?";
            terminal.Write($"  [{opt.key}] {opt.label}", "bright_cyan");
            terminal.WriteLine($" - {roomName}, {dist} away", "gray");
        }
        terminal.WriteLine("  [Enter] Return to dungeon", "gray");
        terminal.WriteLine("");

        string choice = (await terminal.GetInput("Navigate to: ")).Trim().ToUpperInvariant();

        var selected = options.FirstOrDefault(o => o.key == choice);
        if (selected.targetId != null)
        {
            var roomIdPath = BuildRoomIdPath(selected.targetId, startId, parentMap);
            if (roomIdPath != null && roomIdPath.Count > 0)
            {
                long startHP = currentPlayer.HP;
                foreach (var roomId in roomIdPath)
                {
                    await MoveToRoom(roomId);
                    // MoveToRoom handles traps, events, combat — stop if anything happened
                    if (currentPlayer.HP <= 0) break;
                    if (currentPlayer.HP < startHP) break;
                    // Apply poison tick for each room traversed
                    if (currentPlayer.Poison > 0)
                    {
                        await ApplyPoisonDamage();
                        if (currentPlayer.HP <= 0) break;
                    }
                    var rm = currentFloor.GetCurrentRoom();
                    if (rm != null && rm.HasMonsters && !rm.IsCleared) break;
                }
                RequestRedisplay();
            }
        }
    }

    private List<Direction>? BuildDirectionPath(string targetId, string startId,
        Dictionary<string, (string parentId, Direction direction)> parentMap)
    {
        if (!parentMap.ContainsKey(targetId)) return null;

        var path = new List<Direction>();
        string current = targetId;
        while (current != startId && parentMap.TryGetValue(current, out var parent))
        {
            path.Add(parent.direction);
            current = parent.parentId;
        }
        path.Reverse();
        return path;
    }

    private List<string>? BuildRoomIdPath(string targetId, string startId,
        Dictionary<string, (string parentId, Direction direction)> parentMap)
    {
        if (!parentMap.ContainsKey(targetId)) return null;

        var path = new List<string>();
        string current = targetId;
        while (current != startId && parentMap.TryGetValue(current, out var parent))
        {
            path.Add(current);
            current = parent.parentId;
        }
        if (current != startId) return null;
        path.Reverse();
        return path;
    }

    private string GetRoomStatusText(DungeonRoom room)
    {
        if (room.IsBossRoom && !room.IsCleared)
            return Loc.Get("dungeon.status_boss_uncleared");
        if (room.IsBossRoom && room.IsCleared)
            return Loc.Get("dungeon.status_boss_cleared");
        if (room.HasMonsters && !room.IsCleared)
            return Loc.Get("dungeon.status_monsters");
        if (room.HasStairsDown)
            return room.IsCleared ? Loc.Get("dungeon.status_stairs_cleared") : Loc.Get("dungeon.status_stairs");
        if (room.IsSafeRoom)
            return Loc.Get("dungeon.status_safe");
        if (room.IsCleared)
            return Loc.Get("dungeon.status_cleared");
        return Loc.Get("dungeon.status_explored");
    }

    /// <summary>
    /// BFS from a room to check if all reachable explored rooms in that direction are cleared.
    /// Used in screen reader mode to mark exits as "fully cleared" so players know they can skip them.
    /// </summary>
    private bool IsDirectionFullyCleared(string startRoomId, string comingFromRoomId)
    {
        if (currentFloor == null) return false;
        var visited = new HashSet<string> { comingFromRoomId }; // don't path back
        var queue = new Queue<string>();
        visited.Add(startRoomId);
        queue.Enqueue(startRoomId);

        while (queue.Count > 0)
        {
            var roomId = queue.Dequeue();
            var room = currentFloor.Rooms.FirstOrDefault(r => r.Id == roomId);
            if (room == null) continue;

            if (!room.IsExplored) return false; // unexplored room reachable — not fully cleared
            if (!room.IsCleared) return false;   // uncleared room — not fully cleared

            foreach (var exit in room.Exits.Values)
            {
                if (!visited.Contains(exit.TargetRoomId))
                {
                    visited.Add(exit.TargetRoomId);
                    queue.Enqueue(exit.TargetRoomId);
                }
            }
        }
        return true;
    }

    private static readonly Random _companionCommentRng = new();

    /// <summary>
    /// After clearing a room, a companion may suggest which direction to go next.
    /// ~40% chance to fire, picks a random active companion with personality-appropriate lines.
    /// </summary>
    private async Task TryCompanionNavigationComment(DungeonRoom clearedRoom)
    {
        if (teammates.Count == 0 || currentFloor == null) return;
        if (_companionCommentRng.Next(100) >= 40) return; // 40% chance

        // Find an uncleared or unexplored exit to suggest
        Direction? suggestDir = null;
        foreach (var exit in clearedRoom.Exits)
        {
            var target = currentFloor.GetRoom(exit.Value.TargetRoomId);
            if (target == null) continue;
            if (!target.IsExplored) { suggestDir = exit.Key; break; }
            if (!target.IsCleared) { suggestDir = exit.Key; break; }
        }
        if (suggestDir == null) return; // all exits cleared/explored, nothing to suggest

        string dir = suggestDir.Value.ToString().ToLower();

        // Pick a companion to speak
        var speaker = teammates[_companionCommentRng.Next(teammates.Count)];
        string name = speaker.DisplayName ?? speaker.Name2 ?? "Companion";

        // Personality-based lines
        string[] lines;
        var companionId = speaker.CompanionId;
        if (companionId == CompanionId.Vex)
        {
            lines = new[] {
                $"Obviously we should go {dir} from here.",
                $"So are we going {dir} or are we just standing around?",
                $"I vote {dir}. Not that anyone asked.",
                $"Last one to go {dir} is buying drinks at the Inn.",
            };
        }
        else if (companionId == CompanionId.Lyris)
        {
            lines = new[] {
                $"I sense something to the {dir}...",
                $"The path {dir} calls to us.",
                $"To the {dir}, I think. The stars agree.",
            };
        }
        else if (companionId == CompanionId.Aldric)
        {
            lines = new[] {
                $"We should push {dir}. Stay sharp.",
                $"Form up. We move {dir}.",
                $"There's more to the {dir}. Let's keep moving.",
            };
        }
        else if (companionId == CompanionId.Mira)
        {
            lines = new[] {
                $"There may be more who need help to the {dir}.",
                $"I think we should try going {dir}.",
                $"The {dir} passage... I have a feeling about it.",
            };
        }
        else
        {
            // Generic NPC teammates
            lines = new[] {
                $"Let's try going {dir}.",
                $"I say we head {dir} from here.",
                $"Looks like there's more to the {dir}.",
            };
        }

        string line = lines[_companionCommentRng.Next(lines.Length)];
        terminal.SetColor("cyan");
        terminal.WriteLine($"{name}: \"{line}\"");
        await Task.Delay(800);
    }

    /// <summary>
    /// When the player backtracks into a fully-cleared branch, a companion may comment.
    /// ~30% chance to fire.
    /// </summary>
    private async Task TryCompanionBacktrackComment()
    {
        if (teammates.Count == 0) return;
        if (_companionCommentRng.Next(100) >= 30) return; // 30% chance

        var speaker = teammates[_companionCommentRng.Next(teammates.Count)];
        string name = speaker.DisplayName ?? speaker.Name2 ?? "Companion";

        string[] lines;
        var companionId = speaker.CompanionId;
        if (companionId == CompanionId.Vex)
        {
            lines = new[] {
                "Are you lost or something? There's nothing left this way.",
                "We already cleared this area. Did you forget already?",
                "I know I'm dying, but I'd rather not waste what time I have backtracking.",
                "Oh good, my favorite activity. Walking through empty rooms.",
            };
        }
        else if (companionId == CompanionId.Lyris)
        {
            lines = new[] {
                "We've been this way before. The path ahead lies elsewhere.",
                "This area is quiet now. We should seek what remains unseen.",
                "The echoes here are fading. Our purpose lies in another direction.",
            };
        }
        else if (companionId == CompanionId.Aldric)
        {
            lines = new[] {
                "We've secured this area already. Nothing left to fight here.",
                "This ground is cleared. We should press forward, not backward.",
                "I cleared this corridor myself. Trust me, it's empty.",
            };
        }
        else if (companionId == CompanionId.Mira)
        {
            lines = new[] {
                "I don't think there's anything left for us here...",
                "This area feels... peaceful now. We should look elsewhere.",
                "We've already helped everyone we could here.",
            };
        }
        else
        {
            lines = new[] {
                "We've already been through here. It's all cleared.",
                "Nothing left this way. Maybe we should turn around.",
                "This area's been cleaned out already.",
            };
        }

        string line = lines[_companionCommentRng.Next(lines.Length)];
        terminal.SetColor("cyan");
        terminal.WriteLine($"{name}: \"{line}\"");
        await Task.Delay(1000);
    }

    /// <summary>
    /// Get single-character map symbol for a room (roguelike style)
    /// </summary>
    private char GetRoomMapChar(DungeonRoom room)
    {
        bool isCurrentRoom = room.Id == currentFloor?.CurrentRoomId;

        if (isCurrentRoom)
            return '@';
        if (!room.IsExplored)
            return '?';
        if (room.IsBossRoom && !room.IsCleared)
            return 'B';
        if (room.HasStairsDown)
            return '>';
        if (room.IsCleared)
            return '#';
        if (room.HasMonsters)
            return '\u2588'; // █ solid block
        if (room.IsSafeRoom)
            return '~';
        return '\u00B7'; // · middle dot (explored, safe)
    }

    /// <summary>
    /// Render a legend entry at the right side of the map
    /// </summary>
    private void RenderLegendEntry(int index, int padTo)
    {
        // Legend entries rendered manually with terminal colors
        switch (index)
        {
            case 1:
                terminal.SetColor("white");
                terminal.Write("  Legend:");
                break;
            case 3:
                terminal.SetColor("bright_yellow");
                terminal.Write("  @ ");
                terminal.SetColor("gray");
                terminal.Write(Loc.Get("dungeon.map_legend_you"));
                break;
            case 4:
                terminal.SetColor("green");
                terminal.Write("  # ");
                terminal.SetColor("gray");
                terminal.Write(Loc.Get("dungeon.map_legend_cleared"));
                break;
            case 5:
                terminal.SetColor("red");
                terminal.Write("  \u2588 ");
                terminal.SetColor("gray");
                terminal.Write(Loc.Get("dungeon.map_legend_monsters"));
                break;
            case 6:
                terminal.SetColor("blue");
                terminal.Write("  > ");
                terminal.SetColor("gray");
                terminal.Write(Loc.Get("dungeon.map_legend_stairs"));
                break;
            case 7:
                terminal.SetColor("bright_red");
                terminal.Write("  B ");
                terminal.SetColor("gray");
                terminal.Write(Loc.Get("dungeon.map_legend_boss"));
                break;
            case 8:
                terminal.SetColor("cyan");
                terminal.Write("  \u00B7 ");
                terminal.SetColor("gray");
                terminal.Write(Loc.Get("dungeon.map_legend_safe"));
                break;
            case 9:
                terminal.SetColor("darkgray");
                terminal.Write("  ? ");
                terminal.SetColor("gray");
                terminal.Write(Loc.Get("dungeon.map_legend_unknown"));
                break;
        }
    }

    /// <summary>
    /// Build a spatial position map centered on the current room.
    /// Uses local BFS (max 3 hops) so directions always match exits.
    /// Collisions are skipped — rooms that don't fit simply aren't shown.
    /// </summary>
    private Dictionary<string, (int x, int y)> BuildRoomPositionMap()
    {
        var positions = new Dictionary<string, (int x, int y)>();

        if (currentFloor == null || currentFloor.Rooms.Count == 0)
            return positions;

        // Start from entrance at origin so the map is stable regardless of player position
        var startId = currentFloor.EntranceRoomId;
        if (string.IsNullOrEmpty(startId) && currentFloor.Rooms.Count > 0)
            startId = currentFloor.Rooms[0].Id;

        var occupiedPositions = new HashSet<(int x, int y)>();
        positions[startId] = (0, 0);
        occupiedPositions.Add((0, 0));
        var queue = new Queue<(string roomId, int x, int y)>();
        queue.Enqueue((startId, 0, 0));

        // BFS with no depth limit — show all explored rooms on the map.
        // Rooms are connected with spatially consistent directions, so grid
        // positions won't conflict. The occupiedPositions check is a safety net.
        while (queue.Count > 0)
        {
            var (roomId, x, y) = queue.Dequeue();

            var room = currentFloor.Rooms.FirstOrDefault(r => r.Id == roomId);
            if (room == null) continue;

            foreach (var exit in room.Exits)
            {
                var targetId = exit.Value.TargetRoomId;
                if (positions.ContainsKey(targetId)) continue;

                int newX = x, newY = y;
                switch (exit.Key)
                {
                    case Direction.North: newY--; break;
                    case Direction.South: newY++; break;
                    case Direction.East: newX++; break;
                    case Direction.West: newX--; break;
                }

                // Skip if position already taken (safety net for legacy saves)
                if (occupiedPositions.Contains((newX, newY)))
                    continue;

                positions[targetId] = (newX, newY);
                occupiedPositions.Add((newX, newY));
                queue.Enqueue((targetId, newX, newY));
            }
        }

        return positions;
    }

    /// <summary>
    /// Get the map symbol for a room (3 chars)
    /// </summary>
    private string GetRoomMapSymbol(DungeonRoom room)
    {
        bool isCurrentRoom = room.Id == currentFloor?.CurrentRoomId;

        if (isCurrentRoom)
            return "[@]";
        if (!room.IsExplored)
            return "[?]";
        if (room.IsBossRoom)
            return "[B]";
        if (room.HasStairsDown)
            return "[>]";
        if (room.IsCleared)
            return "[#]";
        if (room.HasMonsters)
            return "[!]";
        return "[.]";
    }

    /// <summary>
    /// Render a map line with proper colors for each room symbol
    /// </summary>
    private void RenderColoredMapLine(string line, Dictionary<(int x, int y), DungeonRoom> posToRoom, int minX, int maxX, int y)
    {
        int charIndex = 0;
        for (int x = minX; x <= maxX; x++)
        {
            if (posToRoom.TryGetValue((x, y), out var room))
            {
                // West corridor (1 char)
                terminal.SetColor("darkgray");
                terminal.Write(line.Substring(charIndex, 1));
                charIndex++;

                // Room symbol (3 chars) with color
                string color = GetRoomMapColor(room);
                terminal.SetColor(color);
                terminal.Write(line.Substring(charIndex, 3));
                charIndex += 3;

                // East corridor (1 char)
                terminal.SetColor("darkgray");
                terminal.Write(line.Substring(charIndex, 1));
                charIndex++;
            }
            else
            {
                // Empty space (5 chars)
                terminal.SetColor("darkgray");
                terminal.Write("     ");
                charIndex += 5;
            }
        }
        terminal.WriteLine("");
    }

    /// <summary>
    /// Get the color for a room's map symbol
    /// </summary>
    private string GetRoomMapColor(DungeonRoom room)
    {
        bool isCurrentRoom = room.Id == currentFloor?.CurrentRoomId;

        if (isCurrentRoom)
            return "bright_yellow";
        if (!room.IsExplored)
            return "darkgray";
        if (room.IsBossRoom)
            return "bright_red";
        if (room.HasStairsDown)
            return "blue";
        if (room.IsCleared)
            return "green";
        if (room.HasMonsters)
            return "red";
        return "cyan";
    }

    /// <summary>
    /// NPC encounter in dungeon
    /// </summary>
    private async Task NPCEncounter()
    {
        terminal.ClearScreen();
        terminal.SetColor("cyan");
        terminal.WriteLine("*** DUNGEON ENCOUNTER ***");
        terminal.WriteLine("");

        var player = GetCurrentPlayer();
        var npcType = dungeonRandom.Next(5);

        switch (npcType)
        {
            case 0: // Pixie encounter
                terminal.SetColor("bright_magenta");
                terminal.WriteLine(Loc.Get("dungeon.pixie_appears"));
                terminal.SetColor("magenta");
                terminal.WriteLine(Loc.Get("dungeon.pixie_giggles"));
                terminal.WriteLine("");

                terminal.SetColor("darkgray");
                terminal.Write("[");
                terminal.SetColor("bright_yellow");
                terminal.Write("C");
                terminal.SetColor("darkgray");
                terminal.Write("] ");
                terminal.SetColor("white");
                terminal.WriteLine(Loc.Get("dungeon.pixie_catch"));
                terminal.SetColor("darkgray");
                terminal.Write("[");
                terminal.SetColor("bright_yellow");
                terminal.Write("L");
                terminal.SetColor("darkgray");
                terminal.Write("] ");
                terminal.SetColor("white");
                terminal.WriteLine(Loc.Get("dungeon.pixie_leave"));

                var pixieChoice = await terminal.GetInput(Loc.Get("ui.choice"));
                if (pixieChoice.ToUpper() == "C")
                {
                    // DEX-based catch chance: 35% base + 1% per DEX, capped at 85%
                    int catchChance = Math.Min(85, 35 + (int)player.Dexterity);
                    bool caught = dungeonRandom.Next(100) < catchChance;

                    terminal.WriteLine("");
                    if (caught)
                    {
                        terminal.SetColor("bright_magenta");
                        terminal.WriteLine(Loc.Get("dungeon.pixie_caught"));
                        terminal.WriteLine("");
                        terminal.SetColor("magenta");
                        terminal.WriteLine(Loc.Get("dungeon.pixie_eyes"));
                        terminal.SetColor("bright_magenta");
                        terminal.WriteLine(Loc.Get("dungeon.pixie_kiss_quote"));
                        terminal.WriteLine("");

                        // Kiss - full HP/Mana restore
                        player.HP = player.MaxHP;
                        player.Mana = player.MaxMana;
                        terminal.SetColor("bright_green");
                        terminal.WriteLine(Loc.Get("dungeon.pixie_restored"));

                        // Blessing - combat buff
                        if (player.WellRestedCombats <= 0)
                        {
                            player.WellRestedCombats = 5;
                            player.WellRestedBonus = 0.15f;
                            terminal.SetColor("cyan");
                            terminal.WriteLine(Loc.Get("dungeon.pixie_blessing"));
                        }
                        else
                        {
                            player.WellRestedCombats += 3;
                            terminal.SetColor("cyan");
                            terminal.WriteLine(Loc.Get("dungeon.pixie_blessing_stronger"));
                        }

                        // Gift - level-scaled gold
                        long pixieGold = currentDungeonLevel * 100 + dungeonRandom.Next(200);
                        player.Gold += pixieGold;
                        terminal.SetColor("yellow");
                        terminal.WriteLine(Loc.Get("dungeon.pixie_gold", pixieGold));

                        terminal.SetColor("magenta");
                        terminal.WriteLine("");
                        terminal.WriteLine(Loc.Get("dungeon.pixie_vanish"));
                        ShareEventRewardsWithGroup(player, pixieGold, 0, "Pixie Gift");
                        BroadcastDungeonEvent($"\u001b[35m  {player.Name2} catches a pixie and receives a magical blessing!\u001b[0m");
                    }
                    else
                    {
                        terminal.SetColor("bright_red");
                        terminal.WriteLine(Loc.Get("dungeon.pixie_dodge"));
                        terminal.SetColor("magenta");
                        terminal.WriteLine("");
                        terminal.WriteLine(Loc.Get("dungeon.pixie_clumsy"));
                        terminal.WriteLine("");

                        // Curse - poison + gold stolen
                        terminal.SetColor("dark_magenta");
                        terminal.WriteLine(Loc.Get("dungeon.pixie_dust"));

                        player.Poison = Math.Max(player.Poison, 1);
                        player.PoisonTurns = Math.Max(player.PoisonTurns, 5 + currentDungeonLevel / 5);
                        terminal.SetColor("green");
                        terminal.WriteLine(Loc.Get("dungeon.pixie_poisoned"));

                        long stolenGold = Math.Min(player.Gold, currentDungeonLevel * 50 + dungeonRandom.Next(100));
                        if (stolenGold > 0)
                        {
                            player.Gold -= stolenGold;
                            terminal.SetColor("red");
                            terminal.WriteLine(Loc.Get("dungeon.pixie_steals", stolenGold));
                        }

                        terminal.SetColor("magenta");
                        terminal.WriteLine("");
                        terminal.WriteLine(Loc.Get("dungeon.pixie_cackles"));
                        BroadcastDungeonEvent($"\u001b[31m  {player.Name2} angers a pixie and is cursed!\u001b[0m");
                    }
                }
                else
                {
                    terminal.SetColor("magenta");
                    terminal.WriteLine(Loc.Get("dungeon.pixie_watch"));
                    terminal.SetColor("gray");
                    terminal.WriteLine(Loc.Get("dungeon.pixie_alone"));
                }
                break;

            case 1: // Wounded adventurer
                terminal.WriteLine(Loc.Get("dungeon.wounded_adventurer"), "red");
                terminal.WriteLine(Loc.Get("dungeon.wounded_adventurer_map"), "yellow");
                terminal.WriteLine("");

                // Mark more rooms as explored
                foreach (var room in currentFloor.Rooms.Take(currentFloor.Rooms.Count / 2))
                {
                    room.IsExplored = true;
                    if (!room.HasMonsters)
                        room.IsCleared = true;
                }
                terminal.WriteLine(Loc.Get("dungeon.gain_knowledge"), "green");
                BroadcastDungeonEvent($"\u001b[32m  {player.Name2} receives a map from a wounded adventurer — dungeon layout revealed!\u001b[0m");
                break;

            case 2: // Rival adventurer
                terminal.WriteLine(Loc.Get("dungeon.rival_blocks"), "red");
                terminal.WriteLine(Loc.Get("dungeon.rival_mine"), "yellow");
                terminal.WriteLine("");

                var rivalChoice = await terminal.GetInput(Loc.Get("dungeon.rival_fight_prompt"));
                if (rivalChoice.ToUpper() == "F")
                {
                    int rivalLevel = Math.Max(1, currentDungeonLevel);
                    var rival = Monster.CreateMonster(
                        rivalLevel, "Rival Adventurer",
                        80 + rivalLevel * 38,              // HP: comparable to real NPCs (80 + level*38)
                        10 + rivalLevel * 3,               // STR: scales with level
                        5 + rivalLevel * 2,                // DEF: real armor value
                        "Die!", false, false, "Steel Sword", "Chain Mail",
                        false, false,
                        5 + rivalLevel * 3,                // WeapPow
                        3 + rivalLevel * 2,                // ArmPow (left)
                        3 + rivalLevel * 2                 // ArmPow (right)
                    );
                    rival.Level = rivalLevel;

                    var combatEngine = new CombatEngine(terminal);
                    var combatResult = await combatEngine.PlayerVsMonster(player, rival, teammates);

                    // Check if player should return to temple after resurrection
                    if (combatResult.ShouldReturnToTemple)
                    {
                        terminal.SetColor("yellow");
                        terminal.WriteLine(Loc.Get("dungeon.awaken_temple"));
                        await Task.Delay(2000);
                        await NavigateToLocation(GameLocation.Temple);
                        return;
                    }
                }
                else if (rivalChoice.ToUpper() == "N")
                {
                    long bribe = currentDungeonLevel * 200;
                    if (player.Gold >= bribe)
                    {
                        player.Gold -= bribe;
                        terminal.WriteLine(Loc.Get("dungeon.rival_bribe", bribe), "yellow");
                    }
                    else
                    {
                        terminal.WriteLine(Loc.Get("dungeon.rival_no_gold"), "red");
                    }
                }
                break;

            case 3: // Lost explorer
                terminal.WriteLine(Loc.Get("dungeon.lost_explorer"), "white");
                terminal.WriteLine(Loc.Get("dungeon.lost_explorer_thanks"), "yellow");
                terminal.WriteLine("");

                terminal.SetColor("darkgray");
                terminal.Write("[");
                terminal.SetColor("bright_yellow");
                terminal.Write("G");
                terminal.SetColor("darkgray");
                terminal.Write("] ");
                terminal.SetColor("white");
                terminal.WriteLine(Loc.Get("dungeon.guide_to_safety"));
                terminal.SetColor("darkgray");
                terminal.Write("[");
                terminal.SetColor("bright_yellow");
                terminal.Write("R");
                terminal.SetColor("darkgray");
                terminal.Write("] ");
                terminal.SetColor("white");
                terminal.WriteLine(Loc.Get("dungeon.rob_and_leave"));
                terminal.SetColor("darkgray");
                terminal.Write("[");
                terminal.SetColor("bright_yellow");
                terminal.Write("L");
                terminal.SetColor("darkgray");
                terminal.Write("] ");
                terminal.SetColor("white");
                terminal.WriteLine(Loc.Get("dungeon.leave_them"));

                var explorerChoice = await terminal.GetInput(Loc.Get("ui.choice"));
                long reward;
                if (explorerChoice.ToUpper() == "R")
                {
                    reward = currentDungeonLevel * 200;
                    terminal.WriteLine("");
                    terminal.WriteLine(Loc.Get("dungeon.rob_explorer"), "red");
                    terminal.WriteLine(Loc.Get("dungeon.rob_explorer_cry"), "yellow");
                    player.Gold += reward;
                    player.Darkness += 25;
                    terminal.WriteLine(Loc.Get("dungeon.rob_explorer_gold", reward), "red");
                    terminal.WriteLine(Loc.Get("dungeon.darkness_increases"), "dark_magenta");
                    ShareEventRewardsWithGroup(player, reward, 0, "Robbed Explorer");
                    BroadcastDungeonEvent($"\u001b[31m  {player.Name2} robs a lost explorer for {reward} gold!\u001b[0m");
                }
                else if (explorerChoice.ToUpper() != "L")
                {
                    // Default / G = guide to safety
                    terminal.WriteLine("");
                    terminal.WriteLine(Loc.Get("dungeon.guide_explorer"), "green");
                    reward = currentDungeonLevel * 150;
                    player.Gold += reward;
                    player.Chivalry += 20;
                    terminal.WriteLine(Loc.Get("dungeon.explorer_reward", reward), "yellow");
                    terminal.WriteLine(Loc.Get("dungeon.chivalry_increases"), "white");
                    ShareEventRewardsWithGroup(player, reward, 0, "Lost Explorer Rescue");
                    BroadcastDungeonEvent($"\u001b[33m  {player.Name2} rescues a lost explorer and receives {reward} gold!\u001b[0m");
                }
                else
                {
                    terminal.WriteLine("");
                    terminal.WriteLine(Loc.Get("dungeon.leave_explorer"), "gray");
                    terminal.WriteLine(Loc.Get("dungeon.explorer_plea"), "yellow");
                    terminal.WriteLine(Loc.Get("dungeon.explorer_fades"), "gray");
                }
                break;

            case 4: // Mysterious stranger
                terminal.WriteLine(Loc.Get("dungeon.cloaked_figure"), "magenta");
                terminal.WriteLine(Loc.Get("dungeon.cloaked_figure_fate"), "yellow");
                terminal.WriteLine("");

                if (dungeonRandom.NextDouble() < 0.5)
                {
                    terminal.WriteLine(Loc.Get("dungeon.cloaked_blessing"), "green");
                    player.HP = Math.Min(player.MaxHP, player.HP + player.MaxHP / 2);
                    terminal.WriteLine(Loc.Get("dungeon.cloaked_revitalized"));
                }
                else
                {
                    terminal.WriteLine(Loc.Get("dungeon.cloaked_beware"), "red");
                    terminal.WriteLine(Loc.Get("dungeon.cloaked_vanish"));
                }
                break;
        }

        await Task.Delay(2000);
        await terminal.PressAnyKey();
    }

    /// <summary>
    /// Puzzle encounter
    /// </summary>
    private async Task PuzzleEncounter()
    {
        terminal.ClearScreen();
        var player = GetCurrentPlayer();

        // 50% chance for riddle, 50% for puzzle
        bool useRiddle = dungeonRandom.Next(100) < 50;

        if (useRiddle)
        {
            // Use the full RiddleDatabase
            await RiddleEncounter(player);
        }
        else
        {
            // Use the full PuzzleSystem
            await FullPuzzleEncounter(player);
        }
    }

    private async Task RiddleEncounter(Character player)
    {
        terminal.SetColor("cyan");
        terminal.WriteLine(Loc.Get("dungeon.riddle_header"));
        terminal.WriteLine("");

        // Get a riddle appropriate for this dungeon level
        int difficulty = Math.Min(5, 1 + (currentDungeonLevel / 20));
        var riddle = RiddleDatabase.Instance.GetRandomRiddle(difficulty, currentFloor?.Theme);

        // Present the riddle using the full system
        var result = await RiddleDatabase.Instance.PresentRiddle(riddle, player, terminal);

        if (result.Solved)
        {
            terminal.SetColor("green");
            terminal.WriteLine("");
            terminal.WriteLine(Loc.Get("dungeon.mechanism_unlocks"));

            // Rewards scale with difficulty and level
            // XP equivalent to 2-4 monster kills based on difficulty
            long goldReward = currentDungeonLevel * 50 + difficulty * currentDungeonLevel * 20;
            long expReward = (long)(Math.Pow(currentDungeonLevel, 1.5) * 15 * (1 + difficulty * 0.5));
            player.Gold += goldReward;
            player.Experience += expReward;
            terminal.WriteLine(Loc.Get("dungeon.puzzle_gold_xp", goldReward, expReward));
            ShareEventRewardsWithGroup(player, goldReward, expReward, "Riddle Solved");

            // Chance for Ocean Philosophy fragment on high difficulty riddles
            if (difficulty >= 3 && dungeonRandom.Next(100) < 30)
            {
                terminal.WriteLine("");
                terminal.SetColor("bright_cyan");
                terminal.WriteLine(Loc.Get("dungeon.riddle_truth"));
                // Grant a random uncollected wave fragment
                var fragments = Enum.GetValues<WaveFragment>()
                    .Where(f => !OceanPhilosophySystem.Instance.CollectedFragments.Contains(f))
                    .ToList();
                if (fragments.Count > 0)
                {
                    var fragment = fragments[dungeonRandom.Next(fragments.Count)];
                    OceanPhilosophySystem.Instance.CollectFragment(fragment);
                    terminal.WriteLine(Loc.Get("dungeon.ocean_wisdom"), "bright_magenta");
                }
            }
        }
        else
        {
            terminal.SetColor("red");
            terminal.WriteLine("");
            terminal.WriteLine(Loc.Get("dungeon.gate_sealed"));

            // Take damage on failure
            int damage = riddle.FailureDamage * currentDungeonLevel / 5;
            // Settlement Prison trap resistance
            if (player.HasSettlementBuff && player.SettlementBuffType == (int)UsurperRemake.Systems.SettlementBuffType.TrapResist)
            {
                damage = (int)(damage * (1f - player.SettlementBuffValue));
                player.SettlementBuffCombats--;
                if (player.SettlementBuffCombats <= 0)
                {
                    player.SettlementBuffType = 0;
                    player.SettlementBuffValue = 0f;
                }
            }
            if (damage > 0)
            {
                player.HP = Math.Max(1, player.HP - damage);
                terminal.WriteLine(Loc.Get("dungeon.trap_activates", damage));
            }
        }

        await terminal.PressAnyKey();
    }

    private async Task FullPuzzleEncounter(Character player)
    {
        terminal.SetColor("cyan");
        terminal.WriteLine(Loc.Get("dungeon.puzzle_header"));
        terminal.WriteLine("");

        // Get puzzle type and difficulty based on floor level
        int difficulty = Math.Min(5, 1 + (currentDungeonLevel / 15));
        var puzzleType = PuzzleSystem.Instance.GetRandomPuzzleType(currentDungeonLevel);
        var theme = currentFloor?.Theme ?? DungeonTheme.Catacombs;

        var puzzle = PuzzleSystem.Instance.GeneratePuzzle(puzzleType, difficulty, theme);

        // Display puzzle description
        terminal.WriteLine(puzzle.Description, "white");
        terminal.WriteLine("");

        // Show hints/clues if available
        if (puzzle.Hints.Count > 0)
        {
            terminal.SetColor("cyan");
            foreach (var hint in puzzle.Hints)
            {
                // Don't add bullet points - the hint formatting is already handled
                terminal.WriteLine(hint);
            }
            terminal.WriteLine("");
        }

        bool solved = false;
        int attempts = puzzle.AttemptsRemaining;

        while (attempts > 0 && !solved)
        {
            terminal.SetColor("yellow");
            terminal.WriteLine(Loc.Get("dungeon.attempts_remaining", attempts));
            terminal.WriteLine("");

            // Handle different puzzle types
            switch (puzzle.Type)
            {
                case PuzzleType.LeverSequence:
                    solved = await HandleLeverPuzzle(puzzle, player);
                    break;
                case PuzzleType.SymbolAlignment:
                    solved = await HandleSymbolPuzzle(puzzle, player);
                    break;
                case PuzzleType.NumberGrid:
                    solved = await HandleNumberPuzzle(puzzle, player);
                    break;
                case PuzzleType.MemoryMatch:
                    solved = await HandleMemoryPuzzle(puzzle, player);
                    break;
                default:
                    // Fallback to simple choice for other types
                    solved = await HandleSimplePuzzle(puzzle, player);
                    break;
            }

            if (!solved)
            {
                attempts--;
                if (attempts > 0)
                {
                    terminal.SetColor("red");
                    terminal.WriteLine(Loc.Get("dungeon.not_quite_right"));
                    terminal.WriteLine("");
                }
            }
        }

        if (solved)
        {
            terminal.SetColor("green");
            terminal.WriteLine("");
            terminal.WriteLine(Loc.Get("dungeon.puzzle_solved_header"));

            // Rewards scaled to dungeon level - roughly 2-3 monster kills worth
            long goldReward = currentDungeonLevel * 30 + difficulty * currentDungeonLevel * 15;
            long expReward = (long)(Math.Pow(currentDungeonLevel, 1.5) * 15 * (1 + difficulty * 0.3));
            player.Gold += goldReward;
            player.Experience += expReward;
            terminal.WriteLine(Loc.Get("dungeon.puzzle_gain_gold_xp", goldReward, expReward));
            ShareEventRewardsWithGroup(player, goldReward, expReward, "Puzzle Solved");

            PuzzleSystem.Instance.MarkPuzzleSolved(currentDungeonLevel, puzzle.Title);
        }
        else
        {
            terminal.SetColor("red");
            terminal.WriteLine("");
            terminal.WriteLine(Loc.Get("dungeon.puzzle_failed"));

            int damage = (int)(player.MaxHP * (puzzle.FailureDamagePercent / 100.0));
            // Settlement Prison trap resistance
            if (player.HasSettlementBuff && player.SettlementBuffType == (int)UsurperRemake.Systems.SettlementBuffType.TrapResist)
            {
                damage = (int)(damage * (1f - player.SettlementBuffValue));
                player.SettlementBuffCombats--;
                if (player.SettlementBuffCombats <= 0)
                {
                    player.SettlementBuffType = 0;
                    player.SettlementBuffValue = 0f;
                }
            }
            if (damage > 0)
            {
                player.HP = Math.Max(1, player.HP - damage);
                terminal.WriteLine(Loc.Get("dungeon.trap_springs", damage));
            }
        }

        await terminal.PressAnyKey();
    }

    private async Task<bool> HandleLeverPuzzle(PuzzleInstance puzzle, Character player)
    {
        int leverCount = puzzle.Solution.Count;
        terminal.WriteLine(Loc.Get("dungeon.lever_puzzle", leverCount), "white");

        var input = await terminal.GetInput("> ");
        var parts = input.Split(',', ' ').Select(s => s.Trim()).Where(s => !string.IsNullOrEmpty(s)).ToList();

        if (parts.Count != leverCount) return false;

        for (int i = 0; i < leverCount; i++)
        {
            if (!int.TryParse(parts[i], out int lever)) return false;
            if (lever.ToString() != puzzle.Solution[i]) return false;
        }

        return true;
    }

    private async Task<bool> HandleSymbolPuzzle(PuzzleInstance puzzle, Character player)
    {
        terminal.WriteLine(Loc.Get("dungeon.symbol_puzzle", string.Join(", ", puzzle.AvailableChoices)), "white");
        terminal.WriteLine(Loc.Get("dungeon.symbol_enter", puzzle.Solution.Count), "white");

        var input = await terminal.GetInput("> ");
        var parts = input.Split(',', ' ').Select(s => s.Trim().ToLower()).Where(s => !string.IsNullOrEmpty(s)).ToList();

        if (parts.Count != puzzle.Solution.Count) return false;

        for (int i = 0; i < parts.Count; i++)
        {
            if (parts[i] != puzzle.Solution[i].ToLower()) return false;
        }

        return true;
    }

    private async Task<bool> HandleNumberPuzzle(PuzzleInstance puzzle, Character player)
    {
        terminal.WriteLine(Loc.Get("dungeon.number_target", puzzle.TargetNumber), "white");
        terminal.WriteLine(Loc.Get("dungeon.number_available", string.Join(", ", puzzle.AvailableNumbers)), "white");
        terminal.WriteLine(Loc.Get("dungeon.number_enter"), "white");

        var input = await terminal.GetInput("> ");
        var parts = input.Split(',', ' ', '+').Select(s => s.Trim()).Where(s => !string.IsNullOrEmpty(s)).ToList();

        int sum = 0;
        foreach (var part in parts)
        {
            if (int.TryParse(part, out int num))
                sum += num;
        }

        return sum == puzzle.TargetNumber;
    }

    private async Task<bool> HandleMemoryPuzzle(PuzzleInstance puzzle, Character player)
    {
        // Show the sequence briefly
        terminal.WriteLine(Loc.Get("dungeon.memorize"), "yellow");
        terminal.WriteLine("");
        terminal.SetColor("bright_cyan");
        terminal.WriteLine("  " + string.Join(" - ", puzzle.Solution));

        // Scale display time: 2 seconds + 0.5s per symbol (harder puzzles have more symbols)
        int displayMs = 2000 + (puzzle.Solution.Count * 500);
        await Task.Delay(displayMs);

        // Clear the sequence and ask about a random position
        terminal.ClearScreen();
        terminal.SetColor("cyan");
        terminal.WriteLine(Loc.Get("dungeon.memory_puzzle_header"));
        terminal.WriteLine("");

        // Ask 2 questions about specific positions to prevent lucky guesses
        int questionsNeeded = Math.Min(2, puzzle.Solution.Count);
        var askedPositions = new HashSet<int>();
        var random = new Random();

        for (int q = 0; q < questionsNeeded; q++)
        {
            int pos;
            do { pos = random.Next(puzzle.Solution.Count); } while (askedPositions.Contains(pos));
            askedPositions.Add(pos);

            string ordinal = (pos + 1) switch
            {
                1 => "1st", 2 => "2nd", 3 => "3rd", _ => $"{pos + 1}th"
            };

            terminal.WriteLine($"What was the {ordinal} symbol in the sequence?", "white");
            var input = await terminal.GetInput("> ");

            if (string.IsNullOrWhiteSpace(input) ||
                !input.Trim().Equals(puzzle.Solution[pos], StringComparison.OrdinalIgnoreCase))
            {
                return false;
            }

            if (q < questionsNeeded - 1)
                terminal.WriteLine("Correct!", "bright_green");
        }

        return true;
    }

    private async Task<bool> HandleSimplePuzzle(PuzzleInstance puzzle, Character player)
    {
        // Generic handler for other puzzle types - use Intelligence check
        terminal.WriteLine(Loc.Get("dungeon.careful_thought"), "white");
        terminal.WriteLine("");
        terminal.SetColor("darkgray");
        terminal.Write("[");
        terminal.SetColor("bright_yellow");
        terminal.Write("1");
        terminal.SetColor("darkgray");
        terminal.Write("] ");
        terminal.SetColor("white");
        terminal.WriteLine(Loc.Get("dungeon.examine_carefully"));
        terminal.SetColor("darkgray");
        terminal.Write("[");
        terminal.SetColor("bright_yellow");
        terminal.Write("2");
        terminal.SetColor("darkgray");
        terminal.Write("] ");
        terminal.SetColor("white");
        terminal.WriteLine(Loc.Get("dungeon.try_random"));
        terminal.SetColor("darkgray");
        terminal.Write("[");
        terminal.SetColor("bright_yellow");
        terminal.Write("3");
        terminal.SetColor("darkgray");
        terminal.Write("] ");
        terminal.SetColor("white");
        terminal.WriteLine(Loc.Get("dungeon.give_up"));

        var choice = await terminal.GetInput("> ");

        if (choice == "1")
        {
            // Intelligence-based success chance
            int intBonus = (int)((player.Intelligence - 10) / 2); // Simple INT modifier
            int baseChance = 40 + intBonus;
            return dungeonRandom.Next(100) < baseChance;
        }
        else if (choice == "2")
        {
            // Low random chance
            return dungeonRandom.Next(100) < 20;
        }

        return false;
    }

    // ═══════════════════════════════════════════════════════════════
    // DUNGEON SETTLEMENTS — safe outpost hubs at theme boundaries
    // ═══════════════════════════════════════════════════════════════

    /// <summary>
    /// Settlement encounter — safe outpost with NPC, healing, trading, lore
    /// </summary>
    private async Task SettlementEncounter()
    {
        var player = GetCurrentPlayer();
        var settlement = DungeonSettlementData.GetSettlement(currentDungeonLevel);
        if (settlement == null) { await RandomDungeonEvent(); return; }

        // Track first visit
        bool firstVisit = !player.VisitedSettlements.Contains(settlement.Id);
        if (firstVisit) player.VisitedSettlements.Add(settlement.Id);

        // Broadcast to group
        BroadcastDungeonEvent($"\u001b[33m  The party arrives at {settlement.Name}.\u001b[0m");

        bool stayInSettlement = true;
        while (stayInSettlement)
        {
            terminal.ClearScreen();
            terminal.SetColor(settlement.ThemeColor);
            if (GameConfig.ScreenReaderMode)
            {
                terminal.WriteLine(settlement.Name);
            }
            else
            {
                terminal.WriteLine($"╔{'═'.ToString().PadRight(settlement.Name.Length + 4, '═')}╗");
                terminal.WriteLine($"║  {settlement.Name}  ║");
                terminal.WriteLine($"╚{'═'.ToString().PadRight(settlement.Name.Length + 4, '═')}╝");
            }
            terminal.WriteLine("");

            // Description
            terminal.SetColor("white");
            foreach (var line in settlement.Description.Split('\n'))
                terminal.WriteLine(line);
            terminal.WriteLine("");

            // NPC greeting
            terminal.SetColor("bright_cyan");
            terminal.Write($"{settlement.NPCName}");
            terminal.SetColor("gray");
            terminal.WriteLine($" ({settlement.NPCTitle})");
            terminal.SetColor("white");
            string greeting = firstVisit ? settlement.FirstGreeting : settlement.ReturnGreeting;
            foreach (var line in greeting.Split('\n'))
                terminal.WriteLine(line);
            firstVisit = false; // Only show first greeting once per visit
            terminal.WriteLine("");

            // Menu
            terminal.SetColor("bright_yellow");
            if (IsScreenReader)
            {
                if (settlement.HasHealing)
                {
                    long healCost = CalculateSettlementHealCost(player, settlement);
                    WriteSRMenuOption("H", $"Heal - Restore HP & Mana ({healCost}g)");
                }
                if (settlement.HasTrading)
                    WriteSRMenuOption("T", Loc.Get("dungeon.trade_supplies"));
                if (settlement.HasLore)
                {
                    int totalLore = settlement.LoreFragments.Length;
                    int readLore = 0;
                    foreach (var frag in settlement.LoreFragments)
                    {
                        string loreKey = $"{settlement.Id}_{System.Array.IndexOf(settlement.LoreFragments, frag)}";
                        if (player.SettlementLoreRead.Contains(loreKey)) readLore++;
                    }
                    WriteSRMenuOption("L", $"Lore - Ask about the depths ({readLore}/{totalLore} heard)");
                }
                terminal.SetColor("gray");
                WriteSRMenuOption("R", Loc.Get("dungeon.return"));
            }
            else
            {
                if (settlement.HasHealing)
                {
                    long healCost = CalculateSettlementHealCost(player, settlement);
                    terminal.WriteLine($"[H] Heal - Restore HP & Mana ({healCost}g)");
                }
                if (settlement.HasTrading)
                    terminal.WriteLine("[T] Trade - Buy supplies");
                if (settlement.HasLore)
                {
                    int totalLore = settlement.LoreFragments.Length;
                    int readLore = 0;
                    foreach (var frag in settlement.LoreFragments)
                    {
                        string loreKey = $"{settlement.Id}_{System.Array.IndexOf(settlement.LoreFragments, frag)}";
                        if (player.SettlementLoreRead.Contains(loreKey)) readLore++;
                    }
                    terminal.WriteLine($"[L] Lore - Ask about the depths ({readLore}/{totalLore} heard)");
                }
                terminal.SetColor("gray");
                terminal.WriteLine("[R] Return to dungeon");
            }
            terminal.WriteLine("");

            ShowStatusLine();

            var choice = await GetChoice();

            switch (choice.ToUpper())
            {
                case "H":
                    if (settlement.HasHealing)
                        await SettlementHeal(player, settlement);
                    break;
                case "T":
                    if (settlement.HasTrading)
                        await SettlementTrade(player, settlement);
                    break;
                case "L":
                    if (settlement.HasLore)
                        await SettlementLore(player, settlement);
                    break;
                case "R":
                case "0":
                case "Q":
                    stayInSettlement = false;
                    terminal.SetColor("gray");
                    terminal.WriteLine($"\"{(settlement.Id == "last_hearth" ? "Stay alive out there." : "Safe travels, friend.")}\"", "cyan");
                    await Task.Delay(1500);
                    break;
            }
        }
    }

    private long CalculateSettlementHealCost(Character player, DungeonSettlement settlement)
    {
        // Cheaper than town healer, scaled to floor level
        long baseCost = 10 + (currentDungeonLevel * 5);
        return (long)(baseCost * settlement.HealCostMultiplier);
    }

    private async Task SettlementHeal(Character player, DungeonSettlement settlement)
    {
        long cost = CalculateSettlementHealCost(player, settlement);

        if (player.Gold < cost)
        {
            terminal.WriteLine($"\"{(settlement.Id == "rat_king_market" ? "No gold, no service. That's the rule down here." : "I'm sorry, you don't have enough gold.")}\"", "yellow");
            await Task.Delay(2000);
            return;
        }

        if (player.HP >= player.MaxHP && player.Mana >= player.MaxMana)
        {
            terminal.WriteLine(Loc.Get("dungeon.settlement_no_heal_needed"), "cyan");
            await Task.Delay(2000);
            return;
        }

        player.Gold -= cost;

        long hpHealed = (long)((player.MaxHP - player.HP) * settlement.HealEffectiveness);
        long manaHealed = (long)((player.MaxMana - player.Mana) * settlement.HealEffectiveness);

        player.HP = Math.Min(player.MaxHP, player.HP + hpHealed);
        player.Mana = Math.Min(player.MaxMana, player.Mana + manaHealed);

        // Cure poison at settlements
        if (player.Poison > 0)
        {
            player.Poison = 0;
            player.PoisonTurns = 0;
            terminal.SetColor("green");
            terminal.WriteLine(Loc.Get("dungeon.settlement_poison_cured"));
        }

        terminal.SetColor("green");
        if (hpHealed > 0) terminal.WriteLine(Loc.Get("dungeon.settlement_hp_restored", hpHealed));
        if (manaHealed > 0) terminal.WriteLine(Loc.Get("dungeon.settlement_mana_restored", manaHealed));
        terminal.SetColor("gray");
        terminal.WriteLine($"(-{cost} gold)");

        // Broadcast to group
        BroadcastDungeonEvent($"\u001b[32m  {player.Name} was healed at {settlement.Name}.\u001b[0m");

        await Task.Delay(2000);
    }

    private async Task SettlementTrade(Character player, DungeonSettlement settlement)
    {
        bool shopping = true;
        while (shopping)
        {
            terminal.ClearScreen();
            terminal.SetColor(settlement.ThemeColor);
            terminal.WriteLine($"{settlement.NPCName}'s Wares");
            if (!GameConfig.ScreenReaderMode)
            {
                terminal.SetColor("gray");
                terminal.WriteLine(new string('─', 40));
            }
            terminal.WriteLine("");

            // Generate trade items based on floor level
            var items = GetSettlementTradeItems(settlement);

            for (int i = 0; i < items.Count; i++)
            {
                terminal.SetColor("white");
                terminal.WriteLine($"  [{i + 1}] {items[i].name,-25} {items[i].cost:N0}g");
            }

            terminal.WriteLine("");
            terminal.SetColor("bright_yellow");
            terminal.WriteLine(Loc.Get("dungeon.settlement_your_gold", player.Gold.ToString("N0")));
            terminal.SetColor("gray");
            if (IsScreenReader)
                WriteSRMenuOption("0", Loc.Get("ui.done_shopping"));
            else
                terminal.WriteLine("[0] Done shopping");
            terminal.WriteLine("");

            var choice = await terminal.GetInput("Buy: ");

            if (choice == "0" || choice.ToUpper() == "R" || choice.ToUpper() == "Q")
            {
                shopping = false;
                continue;
            }

            if (int.TryParse(choice, out int idx) && idx >= 1 && idx <= items.Count)
            {
                var item = items[idx - 1];
                if (player.Gold < item.cost)
                {
                    terminal.WriteLine(Loc.Get("ui.not_enough_gold"), "red");
                    await Task.Delay(1500);
                }
                else
                {
                    player.Gold -= item.cost;
                    ApplySettlementPurchase(player, item.id);
                    terminal.SetColor("green");
                    terminal.WriteLine(Loc.Get("dungeon.settlement_purchased", item.name));
                    terminal.SetColor("gray");
                    terminal.WriteLine($"(-{item.cost} gold)");
                    await Task.Delay(1500);
                }
            }
        }
    }

    private List<(string id, string name, long cost)> GetSettlementTradeItems(DungeonSettlement settlement)
    {
        var items = new List<(string id, string name, long cost)>();
        long priceScale = 1 + (currentDungeonLevel / 10);

        foreach (var itemName in settlement.TradeItems)
        {
            switch (itemName)
            {
                case "Healing Potion":
                    items.Add(("heal_potion", "Healing Potion", 25 * priceScale));
                    break;
                case "Mana Potion":
                    items.Add(("mana_potion", "Mana Potion", 30 * priceScale));
                    break;
                case "Antidote":
                    items.Add(("antidote", "Antidote", 20 * priceScale));
                    break;
                case "Healing Herb":
                    items.Add(("healing_herb", "Healing Herb", 40 * priceScale));
                    break;
                case "Starbloom Essence":
                    items.Add(("starbloom", "Starbloom Essence", 80 * priceScale));
                    break;
                case "Firebloom Petal":
                    items.Add(("firebloom", "Firebloom Petal", 60 * priceScale));
                    break;
                case "Torch":
                    items.Add(("torch", "Enchanted Torch", 15 * priceScale));
                    break;
                case "Lockpick":
                    items.Add(("lockpick", "Lockpick Set", 50 * priceScale));
                    break;
                case "Smoke Bomb":
                    items.Add(("smoke_bomb", "Smoke Bomb (flee aid)", 35 * priceScale));
                    break;
            }
        }

        return items;
    }

    private void ApplySettlementPurchase(Character player, string itemId)
    {
        switch (itemId)
        {
            case "heal_potion":
                player.Healing = Math.Min(player.Healing + 1, player.MaxPotions);
                break;
            case "mana_potion":
                player.ManaPotions = Math.Min(player.ManaPotions + 1, player.MaxManaPotions);
                break;
            case "antidote":
                player.Antidotes = Math.Min(player.Antidotes + 1, player.MaxAntidotes);
                break;
            case "healing_herb":
                if (player.HerbHealing < GameConfig.HerbMaxCarry[(int)HerbType.HealingHerb])
                    player.HerbHealing++;
                break;
            case "starbloom":
                if (player.HerbStarbloom < GameConfig.HerbMaxCarry[(int)HerbType.StarbloomEssence])
                    player.HerbStarbloom++;
                break;
            case "firebloom":
                if (player.HerbFirebloom < GameConfig.HerbMaxCarry[(int)HerbType.FirebloomPetal])
                    player.HerbFirebloom++;
                break;
            case "torch":
                // Torch: small temporary buff — not implemented as item, just heal 10 HP as flavor
                player.HP = Math.Min(player.MaxHP, player.HP + 10);
                break;
            case "lockpick":
                // Lockpick: +5 Dexterity temporarily (until next combat)
                player.Dexterity += 2;
                break;
            case "smoke_bomb":
                // Smoke bomb: small agility boost
                player.Agility += 2;
                break;
        }
    }

    private async Task SettlementLore(Character player, DungeonSettlement settlement)
    {
        // Find the next unread lore fragment
        string? unreadKey = null;
        string? unreadText = null;

        for (int i = 0; i < settlement.LoreFragments.Length; i++)
        {
            string key = $"{settlement.Id}_{i}";
            if (!player.SettlementLoreRead.Contains(key))
            {
                unreadKey = key;
                unreadText = settlement.LoreFragments[i];
                break;
            }
        }

        if (unreadText == null)
        {
            terminal.SetColor("gray");
            terminal.WriteLine($"\"{settlement.NPCName} leans back thoughtfully.\"", "cyan");
            terminal.WriteLine("\"I've told you everything I know about these depths.", "white");
            terminal.WriteLine(" You'll have to discover the rest yourself.\"", "white");
            await terminal.PressAnyKey();
            return;
        }

        // Mark as read
        player.SettlementLoreRead.Add(unreadKey!);

        terminal.ClearScreen();
        terminal.SetColor("bright_cyan");
        terminal.WriteLine($"{settlement.NPCName} speaks...");
        if (!GameConfig.ScreenReaderMode)
        {
            terminal.SetColor("gray");
            terminal.WriteLine(new string('─', 40));
        }
        terminal.WriteLine("");

        terminal.SetColor("white");
        foreach (var line in unreadText.Split('\n'))
            terminal.WriteLine(line);

        terminal.WriteLine("");
        terminal.SetColor("gray");
        terminal.WriteLine($"(Lore fragment discovered)");

        // Broadcast to group
        BroadcastDungeonEvent($"\u001b[36m  {settlement.NPCName} shares knowledge of the depths.\u001b[0m");

        await terminal.PressAnyKey();
    }

    /// <summary>
    /// Rest spot encounter - now triggers dream sequences via AmnesiaSystem
    /// </summary>
    private async Task RestSpotEncounter()
    {
        terminal.ClearScreen();
        terminal.SetColor("green");
        terminal.WriteLine("*** SAFE HAVEN ***");
        terminal.WriteLine("");

        var player = GetCurrentPlayer();

        terminal.WriteLine(Loc.Get("dungeon.sanctuary_discover"), "white");
        terminal.WriteLine(Loc.Get("dungeon.sanctuary_calm"), "gray");
        terminal.WriteLine("");

        if (!hasCampedThisFloor)
        {
            terminal.WriteLine(Loc.Get("dungeon.sanctuary_camp"), "green");

            // Sanctuary provides better recovery - 33% of max stats
            long healAmount = player.MaxHP / 3;
            player.HP = Math.Min(player.MaxHP, player.HP + healAmount);
            terminal.WriteLine(Loc.Get("dungeon.sanctuary_hp", healAmount));

            long manaAmount = player.MaxMana / 3;
            player.Mana = Math.Min(player.MaxMana, player.Mana + manaAmount);
            if (manaAmount > 0)
                terminal.WriteLine(Loc.Get("dungeon.sanctuary_mana", manaAmount));

            long staminaAmount = player.MaxCombatStamina / 3;
            player.CurrentCombatStamina = Math.Min(player.MaxCombatStamina, player.CurrentCombatStamina + staminaAmount);
            if (staminaAmount > 0)
                terminal.WriteLine(Loc.Get("dungeon.sanctuary_stamina", staminaAmount));

            // Cure poison
            if (player.Poison > 0)
            {
                player.Poison = 0;
                player.PoisonTurns = 0;
                terminal.WriteLine(Loc.Get("dungeon.sanctuary_cure_poison"), "cyan");
            }

            hasCampedThisFloor = true;

            // Reduce fatigue from dungeon rest (single-player only)
            if (!UsurperRemake.BBS.DoorMode.IsOnlineMode)
            {
                int oldFatigue = player.Fatigue;
                player.Fatigue = Math.Max(0, player.Fatigue - GameConfig.FatigueReductionDungeonRest);
                if (oldFatigue > 0 && player.Fatigue < oldFatigue)
                    terminal.WriteLine(Loc.Get("dungeon.fatigue_reduced", oldFatigue - player.Fatigue), "bright_green");
            }

            // Share rest healing with grouped players
            if (teammates != null)
            {
                foreach (var mate in teammates.Where(t => t != null && t.IsAlive && t.IsGroupedPlayer))
                {
                    long mateHeal = mate.MaxHP / 3;
                    mate.HP = Math.Min(mate.MaxHP, mate.HP + mateHeal);
                    long mateMana = mate.MaxMana / 3;
                    mate.Mana = Math.Min(mate.MaxMana, mate.Mana + mateMana);
                    long mateSta = mate.MaxCombatStamina / 3;
                    mate.CurrentCombatStamina = Math.Min(mate.MaxCombatStamina, mate.CurrentCombatStamina + mateSta);
                    if (mate.Poison > 0) { mate.Poison = 0; mate.PoisonTurns = 0; }

                    var session = GroupSystem.GetSession(mate.GroupPlayerUsername ?? "");
                    session?.EnqueueMessage(
                        $"\u001b[32m  ═══ Safe Haven Camp ═══\u001b[0m\n" +
                        $"\u001b[32m  +{mateHeal} HP" +
                        (mateMana > 0 ? $"  +{mateMana} MP" : "") +
                        (mateSta > 0 ? $"  +{mateSta} STA" : "") + "\u001b[0m");
                }
            }
            BroadcastDungeonEvent($"\u001b[32m  The party rests in a safe haven and recovers their strength.\u001b[0m");

            // Trigger dream sequences through the Amnesia System
            // Dreams reveal the player's forgotten past as a fragment of Manwe
            await Task.Delay(1500);
            terminal.WriteLine("");
            terminal.SetColor("gray");
            terminal.WriteLine(Loc.Get("dungeon.sanctuary_close_eyes"));
            await Task.Delay(1000);

            await AmnesiaSystem.Instance.OnPlayerRest(terminal, player);

            // In single-player, dungeon rest can advance the day if it's nighttime
            if (!UsurperRemake.BBS.DoorMode.IsOnlineMode && DailySystemManager.CanRestForNight(player))
            {
                terminal.WriteLine("");
                terminal.SetColor("gray");
                terminal.WriteLine(Loc.Get("dungeon.sanctuary_sleep_deep"));
                await Task.Delay(1500);
                await DailySystemManager.Instance.RestAndAdvanceToMorning(player);
                terminal.SetColor("yellow");
                terminal.WriteLine(Loc.Get("dungeon.sanctuary_new_day", DailySystemManager.Instance.CurrentDay));
            }
        }
        else
        {
            terminal.WriteLine(Loc.Get("dungeon.sanctuary_already_rested"), "gray");
            terminal.WriteLine(Loc.Get("dungeon.sanctuary_no_benefit"));
        }

        await Task.Delay(1500);
        await terminal.PressAnyKey();
    }

    /// <summary>
    /// Mystery event encounter
    /// </summary>
    private async Task MysteryEventEncounter()
    {
        terminal.ClearScreen();
        terminal.SetColor("magenta");
        terminal.WriteLine(Loc.Get("dungeon.mystery_header"));
        terminal.WriteLine("");

        var player = GetCurrentPlayer();
        var mysteryType = dungeonRandom.Next(5);

        switch (mysteryType)
        {
            case 0: // Vision
                terminal.WriteLine(Loc.Get("dungeon.vision_overtakes"), "cyan");
                await Task.Delay(1500);
                terminal.WriteLine(Loc.Get("dungeon.vision_layout"), "yellow");
                foreach (var room in currentFloor.Rooms)
                {
                    room.IsExplored = true;
                    if (!room.HasMonsters)
                        room.IsCleared = true;
                }
                terminal.WriteLine(Loc.Get("dungeon.rooms_revealed"), "green");
                BroadcastDungeonEvent($"\u001b[36m  A vision reveals the entire floor layout!\u001b[0m");
                break;

            case 1: // Time warp
                terminal.WriteLine(Loc.Get("dungeon.reality_warps"), "red");
                await Task.Delay(1000);
                terminal.SetColor("green");
                terminal.WriteLine(Loc.Get("dungeon.feel_stronger"));
                // XP equivalent to about 1.5 monster kills
                long timeWarpXp = (long)(Math.Pow(currentDungeonLevel, 1.5) * 22);
                player.Experience += timeWarpXp;
                terminal.WriteLine(Loc.Get("dungeon.mystery_xp_plus", timeWarpXp));
                ShareEventRewardsWithGroup(player, 0, timeWarpXp, "Time Warp");
                BroadcastDungeonEvent($"\u001b[32m  Reality warps! {player.Name2} gains +{timeWarpXp} XP!\u001b[0m");
                break;

            case 2: // Ghostly message
                terminal.WriteLine(Loc.Get("dungeon.ghost_appears_room"), "white");
                terminal.WriteLine(Loc.Get("dungeon.ghost_seek_chamber"), "yellow");
                terminal.WriteLine(Loc.Get("dungeon.ghost_find_seek"), "yellow");
                await Task.Delay(1500);
                terminal.WriteLine(Loc.Get("dungeon.ghost_points"), "gray");
                break;

            case 3: // Random teleport
                terminal.WriteLine(Loc.Get("dungeon.portal_opens"), "bright_magenta");
                await Task.Delay(1000);
                var randomRoom = currentFloor.Rooms[dungeonRandom.Next(currentFloor.Rooms.Count)];
                currentFloor.CurrentRoomId = randomRoom.Id;
                randomRoom.IsExplored = true;
                // Auto-clear rooms without monsters
                if (!randomRoom.HasMonsters)
                {
                    randomRoom.IsCleared = true;
                }
                terminal.WriteLine(Loc.Get("dungeon.transported_to", randomRoom.Name), "yellow");
                break;

            case 4: // Treasure rain
                terminal.WriteLine(Loc.Get("dungeon.gold_rains"), "yellow");
                long goldRain = currentDungeonLevel * 100 + dungeonRandom.Next(500);
                player.Gold += goldRain;
                terminal.WriteLine(Loc.Get("dungeon.gather_gold", goldRain));
                ShareEventRewardsWithGroup(player, goldRain, 0, "Treasure Rain");
                BroadcastDungeonEvent($"\u001b[33m  Gold coins rain from the ceiling! {player.Name2} gathers {goldRain} gold!\u001b[0m");
                break;
        }

        await Task.Delay(2500);
        await terminal.PressAnyKey();
    }

    /// <summary>
    /// Random fallback dungeon event
    /// </summary>
    private async Task RandomDungeonEvent()
    {
        // Pick a random existing event
        var eventType = dungeonRandom.Next(6);
        switch (eventType)
        {
            case 0: await TreasureChestEncounter(); break;
            case 1: await PotionCacheEncounter(); break;
            case 2: await MysteriousShrine(); break;
            case 3: await GamblingGhostEncounter(); break;
            case 4: await BeggarEncounter(); break;
            case 5: await WoundedManEncounter(); break;
        }
    }

    // ═══════════════════════════════════════════════════════════════════════
    // OCEAN PHILOSOPHY ROOM ENCOUNTERS
    // These rooms reveal the Wave/Ocean truth through gameplay
    // ═══════════════════════════════════════════════════════════════════════

    /// <summary>
    /// Lore Library encounter - find Wave Fragments and Ocean Philosophy
    /// </summary>
    private async Task LoreLibraryEncounter()
    {
        terminal.ClearScreen();
        terminal.SetColor("dark_cyan");
        if (!GameConfig.ScreenReaderMode)
            terminal.WriteLine("━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━");
        terminal.WriteLine("              " + Loc.Get("dungeon.lore_library_header"));
        if (!GameConfig.ScreenReaderMode)
            terminal.WriteLine("━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━");
        terminal.WriteLine("");

        var player = GetCurrentPlayer();
        var ocean = OceanPhilosophySystem.Instance;

        terminal.SetColor("white");
        terminal.WriteLine(Loc.Get("dungeon.lore_library_1"));
        terminal.WriteLine(Loc.Get("dungeon.lore_library_2"));
        terminal.WriteLine("");
        await Task.Delay(1500);

        // Determine which fragment to reveal based on awakening level
        var availableFragments = OceanPhilosophySystem.FragmentData
            .Where(f => !ocean.CollectedFragments.Contains(f.Key))
            .Where(f => f.Value.RequiredAwakening <= ocean.AwakeningLevel + 2)
            .ToList();

        if (availableFragments.Count > 0)
        {
            var fragment = availableFragments[dungeonRandom.Next(availableFragments.Count)];
            var fragmentData = fragment.Value;

            terminal.SetColor("bright_cyan");
            terminal.WriteLine(Loc.Get("dungeon.lore_library_tome"));
            terminal.WriteLine("");
            await Task.Delay(1000);

            terminal.SetColor("yellow");
            terminal.WriteLine($"  \"{fragmentData.Title}\"");
            terminal.WriteLine("");
            terminal.SetColor("cyan");

            // Display the lore text with dramatic pacing
            var words = fragmentData.Text.Split(' ');
            string currentLine = "  ";
            foreach (var word in words)
            {
                if (currentLine.Length + word.Length > 60)
                {
                    terminal.WriteLine(currentLine);
                    currentLine = "  " + word + " ";
                }
                else
                {
                    currentLine += word + " ";
                }
            }
            if (currentLine.Trim().Length > 0)
                terminal.WriteLine(currentLine);

            terminal.WriteLine("");
            await Task.Delay(2000);

            // Collect the fragment
            ocean.CollectFragment(fragment.Key);

            terminal.SetColor("bright_magenta");
            terminal.WriteLine(Loc.Get("dungeon.lore_library_burns"));
            terminal.WriteLine(Loc.Get("dungeon.wave_fragment_collected", fragmentData.Title));

            // Grant awakening progress
            if (fragmentData.RequiredAwakening >= 5)
            {
                terminal.WriteLine("");
                terminal.SetColor("magenta");
                terminal.WriteLine(Loc.Get("dungeon.lore_library_stirs"));
            }
        }
        else
        {
            // All fragments collected - give ambient wisdom instead
            terminal.SetColor("gray");
            terminal.WriteLine(Loc.Get("dungeon.lore_library_nothing"));
            terminal.WriteLine(Loc.Get("dungeon.lore_library_known"));

            if (ocean.AwakeningLevel >= 5)
            {
                terminal.WriteLine("");
                terminal.SetColor("bright_cyan");
                terminal.WriteLine(Loc.Get("dungeon.lore_library_always_did"));
            }
        }

        terminal.WriteLine("");
        await terminal.PressAnyKey();
    }

    /// <summary>
    /// Memory Fragment encounter - trigger Amnesia System memory recovery
    /// </summary>
    private async Task MemoryFragmentEncounter()
    {
        terminal.ClearScreen();
        terminal.SetColor("magenta");
        if (!GameConfig.ScreenReaderMode)
            terminal.WriteLine("━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━");
        terminal.WriteLine("              " + Loc.Get("dungeon.memory_chamber_header"));
        if (!GameConfig.ScreenReaderMode)
            terminal.WriteLine("━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━");
        terminal.WriteLine("");

        var player = GetCurrentPlayer();
        var amnesia = AmnesiaSystem.Instance;

        terminal.SetColor("white");
        terminal.WriteLine(Loc.Get("dungeon.memory_mirrors_1"));
        terminal.WriteLine(Loc.Get("dungeon.memory_mirrors_2"));
        terminal.WriteLine("");
        await Task.Delay(2000);

        // Check for floor-based memory triggers (only if player is valid)
        if (player != null)
        {
            if (currentDungeonLevel >= 10 && currentDungeonLevel < 25)
            {
                amnesia.CheckMemoryTrigger(TriggerType.DungeonFloor10, player);
            }
            else if (currentDungeonLevel >= 25 && currentDungeonLevel < 50)
            {
                amnesia.CheckMemoryTrigger(TriggerType.DungeonFloor25, player);
            }
            else if (currentDungeonLevel >= 50 && currentDungeonLevel < 75)
            {
                amnesia.CheckMemoryTrigger(TriggerType.DungeonFloor50, player);
            }
            else if (currentDungeonLevel >= 75)
            {
                amnesia.CheckMemoryTrigger(TriggerType.DungeonFloor75, player);
            }
        }

        // Display a recovered memory if available
        var newMemory = AmnesiaSystem.MemoryData
            .Where(m => amnesia.RecoveredMemories.Contains(m.Key))
            .OrderByDescending(m => m.Value.RequiredLevel)
            .FirstOrDefault();

        if (newMemory.Value != null)
        {
            terminal.SetColor("bright_magenta");
            terminal.WriteLine(Loc.Get("dungeon.memory_surfaces", newMemory.Value.Title));
            terminal.WriteLine("");

            terminal.SetColor("magenta");
            foreach (var line in newMemory.Value.Lines)
            {
                terminal.WriteLine($"  {line}");
                await Task.Delay(1200);
            }

            terminal.WriteLine("");
            terminal.SetColor("gray");
            terminal.WriteLine(Loc.Get("dungeon.memory_fades"));
        }
        else
        {
            terminal.SetColor("gray");
            terminal.WriteLine(Loc.Get("dungeon.memory_fragments"));
            terminal.WriteLine(Loc.Get("dungeon.memory_not_yet"));
        }

        terminal.WriteLine("");
        await terminal.PressAnyKey();
    }

    /// <summary>
    /// Riddle Gate encounter - use RiddleDatabase for puzzle challenge
    /// </summary>
    private async Task RiddleGateEncounter()
    {
        terminal.ClearScreen();
        terminal.SetColor("yellow");
        if (!GameConfig.ScreenReaderMode)
            terminal.WriteLine("━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━");
        terminal.WriteLine("              " + Loc.Get("dungeon.riddle_gate_header"));
        if (!GameConfig.ScreenReaderMode)
            terminal.WriteLine("━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━");
        terminal.WriteLine("");

        var player = GetCurrentPlayer();
        var ocean = OceanPhilosophySystem.Instance;

        terminal.SetColor("white");
        terminal.WriteLine(Loc.Get("dungeon.stone_door"));
        terminal.WriteLine(Loc.Get("dungeon.stone_face"));
        terminal.WriteLine("");
        await Task.Delay(1000);

        terminal.SetColor("bright_yellow");
        terminal.WriteLine(Loc.Get("dungeon.stone_riddle"));
        terminal.WriteLine("");
        await Task.Delay(500);

        // Get appropriate riddle based on level
        int difficulty = Math.Min(5, currentDungeonLevel / 20 + 1);

        // Use Ocean Philosophy riddle at high awakening levels
        Riddle riddle;
        if (ocean.AwakeningLevel >= 4 && dungeonRandom.Next(100) < 40)
        {
            riddle = RiddleDatabase.Instance.GetOceanPhilosophyRiddle();
        }
        else
        {
            riddle = RiddleDatabase.Instance.GetRandomRiddle(difficulty);
        }

        // Present the riddle
        var result = await RiddleDatabase.Instance.PresentRiddle(riddle, player, terminal);

        terminal.ClearScreen();
        if (result.Solved)
        {
            terminal.SetColor("green");
            if (!GameConfig.ScreenReaderMode)
                terminal.WriteLine("━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━");
            terminal.WriteLine("              " + Loc.Get("dungeon.riddle_solved_header"));
            if (!GameConfig.ScreenReaderMode)
                terminal.WriteLine("━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━");
            terminal.WriteLine("");
            terminal.WriteLine(Loc.Get("dungeon.stone_correct"));
            terminal.WriteLine(Loc.Get("dungeon.stone_open"));
            terminal.WriteLine("");

            // Reward based on riddle difficulty
            // XP equivalent to 2-4 monster kills based on difficulty
            long xpReward = (long)(Math.Pow(currentDungeonLevel, 1.5) * 15 * (1 + riddle.Difficulty * 0.5));
            player.Experience += xpReward;
            terminal.WriteLine(Loc.Get("dungeon.riddle_xp", xpReward), "cyan");
            ShareEventRewardsWithGroup(player, 0, xpReward, "Riddle Gate Solved");

            // Ocean philosophy riddles grant awakening insight
            if (riddle.IsOceanPhilosophy)
            {
                ocean.GainInsight(20);
                terminal.WriteLine(Loc.Get("dungeon.riddle_deeper"), "magenta");
            }
        }
        else
        {
            terminal.SetColor("red");
            if (!GameConfig.ScreenReaderMode)
                terminal.WriteLine("━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━");
            terminal.WriteLine("              " + Loc.Get("dungeon.riddle_failed_header"));
            if (!GameConfig.ScreenReaderMode)
                terminal.WriteLine("━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━");
            terminal.WriteLine("");
            terminal.WriteLine(Loc.Get("dungeon.riddle_foolish_mortal"));
            terminal.WriteLine("");

            // Trigger combat with a guardian
            terminal.SetColor("bright_red");
            terminal.WriteLine(Loc.Get("dungeon.gate_guardian"));
            await Task.Delay(1500);

            // Create a riddle guardian monster and fight
            var guardian = CreateRiddleGuardian();
            var combatEngine = new CombatEngine(terminal);
            var combatResult = await combatEngine.PlayerVsMonster(player, guardian, teammates);

            // Check if player should return to temple after resurrection
            if (combatResult.ShouldReturnToTemple)
            {
                terminal.SetColor("yellow");
                terminal.WriteLine(Loc.Get("dungeon.awaken_temple"));
                await Task.Delay(2000);
                await NavigateToLocation(GameLocation.Temple);
                return;
            }
        }

        terminal.WriteLine("");
        await terminal.PressAnyKey();
    }

    /// <summary>
    /// Create a riddle guardian monster
    /// </summary>
    private Monster CreateRiddleGuardian()
    {
        int level = currentDungeonLevel + 5;
        return Monster.CreateMonster(
            level,
            "Riddle Guardian",
            level * 50,
            level * 8,
            level * 4,
            "Your ignorance shall be your doom!",
            false,
            true, // Can cast spells
            "Stone Fist",
            "Ancient Stone",
            false, // canHurt
            false, // isUndead
            level * 30,  // armpow
            level * 20,  // weappow
            level * 5    // gold
        );
    }

    /// <summary>
    /// Secret Boss encounter - epic hidden bosses with deep lore
    /// </summary>
    private async Task SecretBossEncounter()
    {
        var player = GetCurrentPlayer();
        var bossMgr = SecretBossManager.Instance;

        // Check if there's a secret boss for this floor
        var bossType = bossMgr.GetBossForFloor(currentDungeonLevel);

        if (bossType == null)
        {
            // No boss for this floor - give atmospheric message instead
            terminal.ClearScreen();
            terminal.SetColor("dark_magenta");
            if (!GameConfig.ScreenReaderMode)
                terminal.WriteLine("━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━");
            terminal.WriteLine("              " + Loc.Get("dungeon.hidden_chamber_header"));
            if (!GameConfig.ScreenReaderMode)
                terminal.WriteLine("━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━");
            terminal.WriteLine("");

            // Track secret found for achievements (finding a hidden chamber counts as a secret)
            player.Statistics.RecordSecretFound();

            terminal.SetColor("gray");
            terminal.WriteLine(Loc.Get("dungeon.sense_power"));
            terminal.WriteLine(Loc.Get("dungeon.moved_on"));
            terminal.WriteLine("");
            terminal.WriteLine(Loc.Get("dungeon.perhaps_deeper"));
            await terminal.PressAnyKey();
            return;
        }

        // Finding a secret boss chamber counts as discovering a secret
        player.Statistics.RecordSecretFound();

        // Encounter the secret boss (displays intro and dialogue)
        var encounterResult = await bossMgr.EncounterBoss(bossType.Value, player, terminal);

        if (!encounterResult.Encountered)
            return;

        // Create the boss monster for actual combat
        var bossMonster = bossMgr.CreateBossMonster(bossType.Value, player.Level);

        // Engage in combat with the secret boss
        var combatEngine = new CombatEngine(terminal);
        var combatResult = await combatEngine.PlayerVsMonster(player, bossMonster, teammates);

        // Check if player should return to temple after resurrection
        if (combatResult.ShouldReturnToTemple)
        {
            terminal.SetColor("yellow");
            terminal.WriteLine(Loc.Get("dungeon.awaken_temple"));
            await Task.Delay(2000);
            await NavigateToLocation(GameLocation.Temple);
            return;
        }

        // Check if player won (player is still alive and boss is dead)
        if (player.HP > 0 && bossMonster.HP <= 0)
        {
            // Player won - handle victory through the SecretBossManager
            await bossMgr.HandleVictory(bossType.Value, player, terminal);

            // Additional memory trigger for secret boss defeat
            var bossData = bossMgr.GetBoss(bossType.Value);
            if (bossData?.TriggersMemoryFlash == true)
            {
                await Task.Delay(1000);
                terminal.ClearScreen();
                terminal.SetColor("bright_magenta");
                terminal.WriteLine(Loc.Get("dungeon.battle_breaks_open"));
                await Task.Delay(1500);
                AmnesiaSystem.Instance.CheckMemoryTrigger(TriggerType.SecretBossDefeated, player);
            }
        }
    }

    private async Task QuitToDungeon()
    {
        await NavigateToLocation(GameLocation.MainStreet);
    }
    
    private async Task ShowDungeonHelp()
    {
        terminal.ClearScreen();
        terminal.SetColor("bright_cyan");
        terminal.WriteLine(Loc.Get("dungeon.help_title"));
        terminal.WriteLine("============");
        terminal.WriteLine("");
        terminal.SetColor("white");
        terminal.WriteLine(Loc.Get("dungeon.help_explore"));
        terminal.WriteLine(Loc.Get("dungeon.help_descend"));
        terminal.WriteLine(Loc.Get("dungeon.help_ascend"));
        terminal.WriteLine(Loc.Get("dungeon.help_team"));
        terminal.WriteLine(Loc.Get("dungeon.help_status"));
        terminal.WriteLine(Loc.Get("dungeon.help_potions"));
        terminal.WriteLine(Loc.Get("dungeon.help_map"));
        terminal.WriteLine(Loc.Get("dungeon.help_guide"));
        terminal.WriteLine(Loc.Get("dungeon.help_quit"));
        terminal.WriteLine("");
        await terminal.PressAnyKey();
    }
    
    /// <summary>
    /// Increase dungeon level directly (limited to player level +10).
    /// </summary>
    private async Task IncreaseDifficulty()
    {
        var playerLevel = GetCurrentPlayer()?.Level ?? 1;
        int maxAccessible = Math.Min(maxDungeonLevel, playerLevel + 10);

        // Jump 10 floors deeper, but capped at player level + 10
        int targetLevel = Math.Min(currentDungeonLevel + 10, maxAccessible);

        if (targetLevel == currentDungeonLevel)
        {
            if (currentDungeonLevel >= maxAccessible)
            {
                terminal.WriteLine(Loc.Get("dungeon.cant_venture_deeper", maxAccessible), "yellow");
                terminal.WriteLine(Loc.Get("dungeon.level_up_access"), "gray");
            }
            else
            {
                terminal.WriteLine(Loc.Get("dungeon.deepest_level"), "yellow");
            }
        }
        else
        {
            currentDungeonLevel = targetLevel;
            if (currentPlayer != null) currentPlayer.CurrentLocation = $"Dungeon Floor {currentDungeonLevel}";
            terminal.WriteLine(Loc.Get("dungeon.steel_nerves", currentDungeonLevel), "magenta");

            // Update quest progress for reaching this floor
            if (currentPlayer != null)
            {
                QuestSystem.OnDungeonFloorReached(currentPlayer, currentDungeonLevel);
            }
        }

        await Task.Delay(1500);
    }
    
    // Additional encounter methods
    private async Task BeggarEncounter()
    {
        var currentPlayer = GetCurrentPlayer();

        terminal.SetColor("cyan");
        terminal.WriteLine(Loc.Get("dungeon.beggar_header"));
        terminal.WriteLine("");
        terminal.WriteLine(Loc.Get("dungeon.beggar_appears"));
        terminal.WriteLine(Loc.Get("dungeon.beggar_plea"));
        terminal.WriteLine("");

        terminal.SetColor("white");
        terminal.WriteLine(Loc.Get("dungeon.beggar_give_label"));
        terminal.WriteLine(Loc.Get("dungeon.beggar_rob_label"));
        terminal.WriteLine(Loc.Get("dungeon.beggar_ignore_label"));
        terminal.WriteLine("");

        var choice = (await GetChoice()).ToUpper().Trim();

        if (choice == "G")
        {
            int giveAmount = Math.Max(10, currentPlayer.Level * 2);
            if (currentPlayer.Gold >= giveAmount)
            {
                currentPlayer.Gold -= giveAmount;
                currentPlayer.Chivalry += 5;
                terminal.WriteLine("");
                terminal.WriteLine(Loc.Get("dungeon.beggar_give", giveAmount), "green");
                terminal.WriteLine(Loc.Get("dungeon.beggar_bless"), "green");
                terminal.WriteLine(Loc.Get("dungeon.beggar_chivalry"), "bright_green");

                // Small chance the beggar rewards your kindness
                if (dungeonRandom.Next(100) < 15)
                {
                    terminal.WriteLine("");
                    terminal.WriteLine(Loc.Get("dungeon.beggar_bonus"), "yellow");
                    int bonusGold = giveAmount * 3 + dungeonRandom.Next(50, 150);
                    currentPlayer.Gold += bonusGold;
                    terminal.WriteLine(Loc.Get("dungeon.beggar_bonus_gold", bonusGold), "bright_yellow");
                    terminal.WriteLine(Loc.Get("dungeon.beggar_test"), "cyan");
                }
            }
            else
            {
                terminal.WriteLine("");
                terminal.WriteLine(Loc.Get("ui.not_enough_gold_spare"), "red");
            }
        }
        else if (choice == "R")
        {
            int stolenGold = 5 + dungeonRandom.Next(currentPlayer.Level, currentPlayer.Level * 3);
            currentPlayer.Gold += stolenGold;
            currentPlayer.Darkness += 10;
            terminal.WriteLine("");
            terminal.WriteLine(Loc.Get("dungeon.beggar_rob"), "red");
            terminal.WriteLine(Loc.Get("dungeon.beggar_rob_gold", stolenGold), "yellow");
            terminal.WriteLine(Loc.Get("dungeon.beggar_darkness"), "bright_red");
            currentPlayer.Statistics?.RecordGoldChange(currentPlayer.Gold);

            // Chance the beggar fights back
            if (dungeonRandom.Next(100) < 20)
            {
                terminal.WriteLine("");
                terminal.WriteLine(Loc.Get("dungeon.beggar_fights_back"), "bright_red");
                int damage = Math.Max(5, currentPlayer.Level + dungeonRandom.Next(5, 15));
                currentPlayer.HP -= damage;
                terminal.WriteLine(Loc.Get("dungeon.beggar_damage", damage), "red");
                if (currentPlayer.HP <= 0) currentPlayer.HP = 1; // Don't kill from this
            }
        }
        else
        {
            terminal.WriteLine("");
            terminal.WriteLine(Loc.Get("dungeon.beggar_ignore"), "gray");
            terminal.WriteLine(Loc.Get("dungeon.beggar_eyes"), "gray");
        }

        await terminal.PressAnyKey();
    }
    
    private Monster CreateMerchantMonster()
    {
        int level = Math.Max(1, currentDungeonLevel);
        long hp = 40 + level * 15;
        long str = 8 + level * 2;
        long def = 5 + level;
        long punch = 3 + level;
        long armPow = 1 + level / 2;
        long weapPow = 2 + level;
        return Monster.CreateMonster(level, "Traveling Merchant", hp, str, def,
            "You'll regret this!", false, false, "Merchant's Blade", "Leather Armor",
            false, false, punch, armPow, weapPow);
    }
    
    private Monster CreateUndeadMonster()
    {
        var names = new[] { "Undead", "Zombie", "Skeleton Warrior" };
        var name = names[dungeonRandom.Next(names.Length)];

        return Monster.CreateMonster(currentDungeonLevel, name,
            currentDungeonLevel * 5, currentDungeonLevel * 2, 0,
            "...", false, false, "Rusty Sword", "Tattered Armor",
            false, false, currentDungeonLevel * 70, 0, currentDungeonLevel * 2);
    }

    /// <summary>
    /// Try to discover a Seal when exploring a room on a floor that has one
    /// Seals are found in special rooms (shrines, libraries, secret vaults) or boss rooms
    /// Guaranteed discovery after exploring 75%+ of the floor to prevent frustration
    /// </summary>
    private async Task<bool> TryDiscoverSeal(Character player, DungeonRoom room)
    {
        // Check if this floor has an uncollected seal
        if (!currentFloor.HasUncollectedSeal || currentFloor.SealCollected || !currentFloor.SealType.HasValue)
            return false;

        // Calculate exploration progress
        float explorationProgress = (float)currentFloor.Rooms.Count(r => r.IsExplored) / currentFloor.Rooms.Count;

        // GUARANTEED discovery after 75% exploration - player has earned it
        bool guaranteedDiscovery = explorationProgress >= 0.75;

        // Seals are found in thematically appropriate rooms
        bool isSealRoom = room.Type == RoomType.Shrine ||
                          room.Type == RoomType.LoreLibrary ||
                          room.Type == RoomType.SecretVault ||
                          room.Type == RoomType.MeditationChamber ||
                          room.IsBossRoom ||
                          (room.Type == RoomType.Chamber && dungeonRandom.NextDouble() < 0.20) ||
                          (room.HasEvent && room.EventType == DungeonEventType.Shrine);

        // Higher chance in cleared rooms and special rooms
        if (!isSealRoom && !guaranteedDiscovery)
        {
            // Scaling chance based on exploration: 15% at 50%, 25% at 60%, etc.
            if (explorationProgress < 0.5)
                return false;
            double scaledChance = 0.15 + (explorationProgress - 0.5) * 0.4; // 15% to 35% based on exploration
            if (dungeonRandom.NextDouble() > scaledChance)
                return false;
        }

        // Found the seal!
        currentFloor.SealCollected = true;
        currentFloor.SealRoomId = room.Id;

        // Dramatic discovery sequence
        terminal.ClearScreen();
        terminal.SetColor("bright_yellow");
        terminal.WriteLine("");
        WriteThickDivider(62);
        terminal.WriteLine("");
        terminal.WriteLine(Loc.Get("dungeon.seal_ancient_power"));
        terminal.WriteLine("");
        WriteThickDivider(62);
        terminal.WriteLine("");
        await Task.Delay(2000);

        terminal.SetColor("white");
        terminal.WriteLine(Loc.Get("dungeon.seal_stone_tablet"));
        terminal.WriteLine(Loc.Get("dungeon.seal_divine_energy"));
        terminal.WriteLine("");
        terminal.WriteLine(Loc.Get("dungeon.seal_seven_seals"));
        terminal.WriteLine("");
        await Task.Delay(1500);

        terminal.SetColor("gray");
        await terminal.GetInputAsync(Loc.Get("dungeon.seal_press_enter"));

        // Collect the seal using the SevenSealsSystem
        var sealSystem = SevenSealsSystem.Instance;
        await sealSystem.CollectSeal(player, currentFloor.SealType.Value, terminal);

        // Online news: seal discovered
        if (UsurperRemake.Systems.OnlineStateManager.IsActive)
        {
            var sealFinderName = player.Name2 ?? player.Name1;
            var sealCount = StoryProgressionSystem.Instance.CollectedSeals.Count;
            _ = UsurperRemake.Systems.OnlineStateManager.Instance!.AddNews(
                $"{sealFinderName} has discovered an ancient Seal! ({sealCount}/7 collected)", "quest");
        }

        // Mark this seal floor as cleared so player can progress to deeper floors
        // This is required because IsFloorCleared() and GetMaxAccessibleFloor() check ClearedSpecialFloors
        player.ClearedSpecialFloors.Add(currentDungeonLevel);

        // Also mark the floor as cleared in the persistence system
        if (player.DungeonFloorStates.TryGetValue(currentDungeonLevel, out var floorState))
        {
            floorState.EverCleared = true;
            floorState.IsPermanentlyClear = true;
            floorState.LastClearedAt = DateTime.Now;
        }

        return true;
    }

    /// <summary>
    /// Check if boss defeat drops an artifact based on floor level
    /// NOTE: This is a legacy function. All artifacts now drop from Old God encounters
    /// which are handled by OldGodBossSystem.HandleBossDefeated() using the boss's
    /// ArtifactDropped property from OldGodsData.cs. Old God floors (25, 40, 55, 70, 85, 95, 100)
    /// return early from TryOldGodBossEncounter, so this function only runs for non-Old-God bosses.
    /// </summary>
    private async Task CheckArtifactDrop(Character player, int floorLevel)
    {
        // All artifacts drop from Old Gods and are handled by OldGodBossSystem
        // Old God floors: 25=Maelketh, 40=Veloura, 55=Thorgrim, 70=Noctura, 85=Aurelion, 95=Terravok, 100=Manwe
        // Non-Old-God secret boss floors (50, 75, 99) don't have unique artifacts
        var artifactFloors = new Dictionary<int, UsurperRemake.Systems.ArtifactType>();

        if (!artifactFloors.TryGetValue(floorLevel, out var artifactType))
            return;

        // Check if already collected
        if (UsurperRemake.Systems.ArtifactSystem.Instance.HasArtifact(artifactType))
            return;

        // Collect the artifact!
        terminal.WriteLine("");
        terminal.SetColor("bright_magenta");
        WriteThickDivider(62);
        terminal.WriteLine(Loc.Get("dungeon.artifact_header"));
        WriteThickDivider(62);
        terminal.WriteLine("");

        await UsurperRemake.Systems.ArtifactSystem.Instance.CollectArtifact(player, artifactType, terminal);

        terminal.WriteLine("");
        await terminal.PressAnyKey();
    }

    #region Companion Personal Quest Encounters

    /// <summary>
    /// Check for companion personal quest encounters based on dungeon conditions
    /// </summary>
    private async Task CheckCompanionQuestEncounters(Character player, DungeonRoom room)
    {
        var companionSystem = UsurperRemake.Systems.CompanionSystem.Instance;
        var story = UsurperRemake.Systems.StoryProgressionSystem.Instance;

        // Check each companion's quest conditions
        foreach (var companion in companionSystem.GetActiveCompanions())
        {
            if (companion == null || !companion.PersonalQuestStarted || companion.PersonalQuestCompleted)
                continue;

            bool triggered = companion.Id switch
            {
                UsurperRemake.Systems.CompanionId.Lyris => await CheckLyrisQuestEncounter(player, companion, room, story),
                UsurperRemake.Systems.CompanionId.Aldric => await CheckAldricQuestEncounter(player, companion, room, story),
                UsurperRemake.Systems.CompanionId.Mira => await CheckMiraQuestEncounter(player, companion, room, story),
                UsurperRemake.Systems.CompanionId.Vex => await CheckVexQuestEncounter(player, companion, room, story),
                UsurperRemake.Systems.CompanionId.Melodia => await CheckMelodiaQuestEncounter(player, companion, room, story),
                _ => false
            };

            if (triggered)
            {
                await Task.Delay(500);
                break; // Only one quest encounter per room
            }
        }
    }

    /// <summary>
    /// Lyris Quest: "The Light That Was" - Find Aurelion's artifact
    /// Triggers on floor 85 (Aurelion's domain)
    /// </summary>
    private async Task<bool> CheckLyrisQuestEncounter(Character player, UsurperRemake.Systems.Companion lyris,
        DungeonRoom room, UsurperRemake.Systems.StoryProgressionSystem story)
    {
        // Lyris's quest triggers near Aurelion's floor (85)
        if (currentDungeonLevel < 80 || currentDungeonLevel > 90)
            return false;

        // Only trigger once
        if (story.HasStoryFlag("lyris_quest_artifact_found"))
            return false;

        // 15% chance per room on correct floors
        if (dungeonRandom.NextDouble() > 0.15)
            return false;

        // Trigger the quest event
        terminal.ClearScreen();
        terminal.SetColor("bright_magenta");
        WriteThickDivider();
        terminal.WriteLine($"                    {Loc.Get("quest.lyris_light.title")}                                        ");
        WriteThickDivider();
        terminal.WriteLine("");

        await Task.Delay(1000);

        terminal.SetColor("white");
        terminal.WriteLine(Loc.Get("quest.lyris_light.stops"));
        terminal.WriteLine("");
        await Task.Delay(1000);

        terminal.SetColor("cyan");
        terminal.WriteLine(Loc.Get("quest.lyris_light.feels_it"));
        terminal.WriteLine("");
        await Task.Delay(1000);

        terminal.SetColor("white");
        terminal.WriteLine(Loc.Get("quest.lyris_light.wall_1"));
        terminal.WriteLine(Loc.Get("quest.lyris_light.wall_2"));
        terminal.WriteLine("");
        await Task.Delay(1500);

        terminal.SetColor("bright_yellow");
        terminal.WriteLine(Loc.Get("quest.lyris_light.chamber_1"));
        terminal.WriteLine(Loc.Get("quest.lyris_light.chamber_2"));
        terminal.WriteLine("");
        await Task.Delay(1500);

        terminal.SetColor("cyan");
        terminal.WriteLine(Loc.Get("quest.lyris_light.heart_1"));
        terminal.WriteLine(Loc.Get("quest.lyris_light.heart_2"));
        terminal.WriteLine("");
        await Task.Delay(1500);

        terminal.SetColor("white");
        terminal.WriteLine(Loc.Get("quest.lyris_light.hesitates"));
        terminal.WriteLine("");

        terminal.SetColor("darkgray");
        terminal.Write("[");
        terminal.SetColor("bright_yellow");
        terminal.Write("1");
        terminal.SetColor("darkgray");
        terminal.Write("] ");
        terminal.SetColor("yellow");
        terminal.WriteLine(Loc.Get("quest.lyris_light.option_1"));
        terminal.SetColor("darkgray");
        terminal.Write("[");
        terminal.SetColor("bright_yellow");
        terminal.Write("2");
        terminal.SetColor("darkgray");
        terminal.Write("] ");
        terminal.SetColor("yellow");
        terminal.WriteLine(Loc.Get("quest.lyris_light.option_2"));
        terminal.SetColor("darkgray");
        terminal.Write("[");
        terminal.SetColor("bright_yellow");
        terminal.Write("3");
        terminal.SetColor("darkgray");
        terminal.Write("] ");
        terminal.SetColor("yellow");
        terminal.WriteLine(Loc.Get("quest.lyris_light.option_3"));
        terminal.WriteLine("");

        var choice = await terminal.GetInput(Loc.Get("quest.lyris_light.what_do"));

        switch (choice)
        {
            case "1":
                terminal.SetColor("white");
                terminal.WriteLine("");
                terminal.WriteLine(Loc.Get("quest.lyris_light.c1_encourage"));
                terminal.WriteLine("");
                await Task.Delay(1000);

                terminal.SetColor("bright_magenta");
                terminal.WriteLine(Loc.Get("quest.lyris_light.c1_lifts"));
                terminal.WriteLine(Loc.Get("quest.lyris_light.c1_once_was"));
                terminal.WriteLine("");
                await Task.Delay(1500);

                terminal.SetColor("cyan");
                terminal.WriteLine(Loc.Get("quest.lyris_light.c1_feel_1"));
                terminal.WriteLine(Loc.Get("quest.lyris_light.c1_feel_2"));
                terminal.WriteLine(Loc.Get("quest.lyris_light.c1_feel_3"));
                terminal.WriteLine("");

                UsurperRemake.Systems.CompanionSystem.Instance.ModifyLoyalty(
                    UsurperRemake.Systems.CompanionId.Lyris, 20, "Trusted her with Aurelion's Heart");
                break;

            case "2":
                terminal.SetColor("white");
                terminal.WriteLine("");
                terminal.WriteLine(Loc.Get("quest.lyris_light.c2_careful"));
                terminal.WriteLine("");
                await Task.Delay(1000);

                terminal.SetColor("cyan");
                terminal.WriteLine(Loc.Get("quest.lyris_light.c2_right"));
                terminal.WriteLine(Loc.Get("quest.lyris_light.c2_worth"));
                terminal.WriteLine("");
                await Task.Delay(1000);

                terminal.SetColor("bright_magenta");
                terminal.WriteLine(Loc.Get("quest.lyris_light.c2_lifts"));
                terminal.WriteLine(Loc.Get("quest.lyris_light.c2_warmth"));
                terminal.WriteLine("");

                UsurperRemake.Systems.CompanionSystem.Instance.ModifyLoyalty(
                    UsurperRemake.Systems.CompanionId.Lyris, 10, "Showed concern for her safety");
                break;

            case "3":
                terminal.SetColor("white");
                terminal.WriteLine("");
                terminal.WriteLine(Loc.Get("quest.lyris_light.c3_reach"));
                terminal.WriteLine("");
                await Task.Delay(1000);

                terminal.SetColor("red");
                terminal.WriteLine(Loc.Get("quest.lyris_light.c3_pain"));
                var orbDmg = player.MaxHP / 4;
                player.HP = Math.Max(1, player.HP - orbDmg);
                terminal.WriteLine(Loc.Get("quest.lyris_light.c3_damage", orbDmg));
                terminal.WriteLine("");
                await Task.Delay(1000);

                terminal.SetColor("cyan");
                terminal.WriteLine(Loc.Get("quest.lyris_light.c3_responds_1"));
                terminal.WriteLine(Loc.Get("quest.lyris_light.c3_responds_2"));
                terminal.WriteLine("");

                UsurperRemake.Systems.CompanionSystem.Instance.ModifyLoyalty(
                    UsurperRemake.Systems.CompanionId.Lyris, -5, "Tried to take her artifact");
                break;
        }

        await Task.Delay(1500);

        terminal.SetColor("bright_green");
        WriteThickDivider();
        terminal.WriteLine($"           {Loc.Get("quest.lyris_light.complete_title")}                                 ");
        WriteThickDivider();
        terminal.WriteLine("");

        story.SetStoryFlag("lyris_quest_artifact_found", true);
        UsurperRemake.Systems.CompanionSystem.Instance.CompletePersonalQuest(
            UsurperRemake.Systems.CompanionId.Lyris, true);

        // Bonus: Lyris gains power
        lyris.BaseStats.MagicPower += 25;
        lyris.BaseStats.HealingPower += 15;
        terminal.WriteLine(Loc.Get("quest.lyris_light.bonus"), "bright_cyan");

        await terminal.PressAnyKey();
        return true;
    }

    /// <summary>
    /// Aldric Quest: "Ghosts of the Guard" - Confront the demon that killed his unit
    /// Triggers on floor 55-65 (demonic territory)
    /// </summary>
    private async Task<bool> CheckAldricQuestEncounter(Character player, UsurperRemake.Systems.Companion aldric,
        DungeonRoom room, UsurperRemake.Systems.StoryProgressionSystem story)
    {
        // Aldric's quest triggers in demonic territory
        if (currentDungeonLevel < 55 || currentDungeonLevel > 65)
            return false;

        if (story.HasStoryFlag("aldric_quest_demon_confronted"))
            return false;

        // 15% chance per room
        if (dungeonRandom.NextDouble() > 0.15)
            return false;

        terminal.ClearScreen();
        terminal.SetColor("dark_red");
        WriteThickDivider();
        terminal.WriteLine($"                    {Loc.Get("quest.aldric_ghosts.title")}                                       ");
        WriteThickDivider();
        terminal.WriteLine("");

        await Task.Delay(1000);

        terminal.SetColor("white");
        terminal.WriteLine(Loc.Get("quest.aldric_ghosts.freezes"));
        terminal.WriteLine("");
        await Task.Delay(1000);

        terminal.SetColor("bright_yellow");
        terminal.WriteLine(Loc.Get("quest.aldric_ghosts.smell_1"));
        terminal.WriteLine(Loc.Get("quest.aldric_ghosts.smell_2"));
        terminal.WriteLine("");
        await Task.Delay(1500);

        terminal.SetColor("white");
        terminal.WriteLine(Loc.Get("quest.aldric_ghosts.emerges_1"));
        terminal.WriteLine(Loc.Get("quest.aldric_ghosts.emerges_2"));
        terminal.WriteLine("");
        await Task.Delay(1500);

        terminal.SetColor("red");
        terminal.WriteLine(Loc.Get("quest.aldric_ghosts.malachar_speaks"));
        terminal.WriteLine(Loc.Get("quest.aldric_ghosts.malachar_1"));
        terminal.WriteLine(Loc.Get("quest.aldric_ghosts.malachar_2"));
        terminal.WriteLine("");
        await Task.Delay(1500);

        terminal.SetColor("white");
        terminal.WriteLine(Loc.Get("quest.aldric_ghosts.trembles"));
        terminal.WriteLine("");

        terminal.SetColor("darkgray");
        terminal.Write("[");
        terminal.SetColor("bright_yellow");
        terminal.Write("1");
        terminal.SetColor("darkgray");
        terminal.Write("] ");
        terminal.SetColor("yellow");
        terminal.WriteLine(Loc.Get("quest.aldric_ghosts.option_1"));
        terminal.SetColor("darkgray");
        terminal.Write("[");
        terminal.SetColor("bright_yellow");
        terminal.Write("2");
        terminal.SetColor("darkgray");
        terminal.Write("] ");
        terminal.SetColor("yellow");
        terminal.WriteLine(Loc.Get("quest.aldric_ghosts.option_2"));
        terminal.SetColor("darkgray");
        terminal.Write("[");
        terminal.SetColor("bright_yellow");
        terminal.Write("3");
        terminal.SetColor("darkgray");
        terminal.Write("] ");
        terminal.SetColor("yellow");
        terminal.WriteLine(Loc.Get("quest.aldric_ghosts.option_3"));
        terminal.WriteLine("");

        var choice = await terminal.GetInput(Loc.Get("quest.aldric_ghosts.what_say"));

        switch (choice)
        {
            case "1":
                terminal.SetColor("bright_cyan");
                terminal.WriteLine("");
                terminal.WriteLine(Loc.Get("quest.aldric_ghosts.c1_together"));
                terminal.WriteLine(Loc.Get("quest.aldric_ghosts.c1_wont_fail"));
                terminal.WriteLine("");
                await Task.Delay(1000);

                await FightMalachar(player, aldric, true);

                UsurperRemake.Systems.CompanionSystem.Instance.ModifyLoyalty(
                    UsurperRemake.Systems.CompanionId.Aldric, 25, "Fought beside him against his demon");
                break;

            case "2":
                terminal.SetColor("white");
                terminal.WriteLine("");
                terminal.WriteLine(Loc.Get("quest.aldric_ghosts.c2_understand"));
                terminal.WriteLine(Loc.Get("quest.aldric_ghosts.c2_thanks"));
                terminal.WriteLine("");
                await Task.Delay(1000);

                await FightMalachar(player, aldric, false);

                UsurperRemake.Systems.CompanionSystem.Instance.ModifyLoyalty(
                    UsurperRemake.Systems.CompanionId.Aldric, 15, "Respected his need to face his demon alone");
                break;

            case "3":
                terminal.SetColor("white");
                terminal.WriteLine("");
                terminal.WriteLine(Loc.Get("quest.aldric_ghosts.c3_no"));
                terminal.WriteLine(Loc.Get("quest.aldric_ghosts.c3_ends"));
                terminal.WriteLine("");
                await Task.Delay(1000);

                await FightMalachar(player, aldric, true);

                UsurperRemake.Systems.CompanionSystem.Instance.ModifyLoyalty(
                    UsurperRemake.Systems.CompanionId.Aldric, -10, "Suggested retreating from his demon");
                break;
        }

        return true;
    }

    /// <summary>
    /// Boss fight with Malachar for Aldric's quest
    /// </summary>
    private async Task FightMalachar(Character player, UsurperRemake.Systems.Companion aldric, bool playerJoins)
    {
        terminal.ClearScreen();
        terminal.SetColor("red");
        WriteThickDivider();
        terminal.WriteLine($"                    {Loc.Get("quest.aldric_ghosts.boss_title")}                                 ");
        WriteThickDivider();
        terminal.WriteLine("");

        // Create Malachar as a boss monster
        var malachar = new Monster
        {
            Name = "Malachar the Slayer",
            Level = 60,
            HP = 8000,
            MaxHP = 8000,
            Strength = 180,
            Defence = 120,
            MonsterColor = "dark_red"
        };

        // Aldric gets a proper HP pool for this scripted fight (not his tiny BaseStats.HP)
        int aldricMaxHP = 2000 + (player.Level * 50);
        int aldricHP = aldricMaxHP;

        terminal.SetColor("red");
        terminal.WriteLine(Loc.Get("quest.aldric_ghosts.malachar_hp", malachar.HP, malachar.MaxHP));
        terminal.WriteLine("");
        await Task.Delay(1000);

        // Simplified boss fight
        int rounds = 0;
        while (malachar.HP > 0 && player.HP > 0 && aldricHP > 0 && rounds < 15)
        {
            rounds++;

            // Player attacks if joined
            if (playerJoins)
            {
                long playerDmg = player.Strength + player.WeapPow + dungeonRandom.Next(50);
                malachar.HP -= (int)playerDmg;
                terminal.WriteLine(Loc.Get("quest.aldric_ghosts.player_strike", playerDmg), "bright_cyan");
            }

            // Aldric attacks with determination — hits harder when fighting alone
            int aldricAtkBonus = playerJoins ? 0 : 150;
            int aldricDmg = aldric.BaseStats.Attack * 2 + aldricAtkBonus + dungeonRandom.Next(100);
            malachar.HP -= aldricDmg;
            terminal.WriteLine(Loc.Get("quest.aldric_ghosts.aldric_fury", aldricDmg), "bright_yellow");

            if (malachar.HP <= 0) break;

            // Malachar attacks Aldric — less damage when player is drawing aggro
            int demonBaseDmg = playerJoins ? 50 : 120;
            int demonDmg = demonBaseDmg + dungeonRandom.Next(80);
            aldricHP -= demonDmg;
            terminal.WriteLine(Loc.Get("quest.aldric_ghosts.malachar_claws", demonDmg), "red");

            terminal.WriteLine("");
            terminal.WriteLine(Loc.Get("quest.aldric_ghosts.malachar_hp", Math.Max(0, malachar.HP), malachar.MaxHP), "red");
            terminal.WriteLine(Loc.Get("quest.aldric_ghosts.aldric_hp", Math.Max(0, aldricHP)), "yellow");
            await Task.Delay(800);
            terminal.WriteLine("");
        }

        if (malachar.HP <= 0)
        {
            terminal.SetColor("bright_green");
            terminal.WriteLine("");
            terminal.WriteLine(Loc.Get("quest.aldric_ghosts.falls"));
            terminal.WriteLine("");
            await Task.Delay(1500);

            terminal.SetColor("white");
            terminal.WriteLine(Loc.Get("quest.aldric_ghosts.tears"));
            terminal.WriteLine("");
            await Task.Delay(1000);

            terminal.SetColor("bright_yellow");
            terminal.WriteLine(Loc.Get("quest.aldric_ghosts.done_1"));
            terminal.WriteLine(Loc.Get("quest.aldric_ghosts.done_2"));
            terminal.WriteLine("");
            await Task.Delay(1500);

            terminal.SetColor("cyan");
            terminal.WriteLine(Loc.Get("quest.aldric_ghosts.peace_1"));
            terminal.WriteLine(Loc.Get("quest.aldric_ghosts.peace_2"));
            terminal.WriteLine("");

            terminal.SetColor("bright_green");
            WriteThickDivider();
            terminal.WriteLine($"           {Loc.Get("quest.aldric_ghosts.complete_title")}                               ");
            WriteThickDivider();
            terminal.WriteLine("");

            var story = UsurperRemake.Systems.StoryProgressionSystem.Instance;
            story.SetStoryFlag("aldric_quest_demon_confronted", true);
            UsurperRemake.Systems.CompanionSystem.Instance.CompletePersonalQuest(
                UsurperRemake.Systems.CompanionId.Aldric, true);

            // Bonus: Aldric's guilt is lifted, gaining stats
            aldric.BaseStats.Defense += 30;
            aldric.BaseStats.HP += 200;
            terminal.WriteLine(Loc.Get("quest.aldric_ghosts.bonus"), "bright_cyan");

            // XP reward
            player.Experience += 25000;
            terminal.WriteLine(Loc.Get("quest.aldric_ghosts.xp_reward", "25,000"), "bright_green");
        }

        await terminal.PressAnyKey();
    }

    /// <summary>
    /// Mira Quest: "The Meaning of Mercy" - Help her find purpose in healing
    /// Triggers on floor 40-50 (where suffering is greatest)
    /// </summary>
    private async Task<bool> CheckMiraQuestEncounter(Character player, UsurperRemake.Systems.Companion mira,
        DungeonRoom room, UsurperRemake.Systems.StoryProgressionSystem story)
    {
        if (currentDungeonLevel < 40 || currentDungeonLevel > 50)
            return false;

        if (story.HasStoryFlag("mira_quest_choice_made"))
            return false;

        if (dungeonRandom.NextDouble() > 0.15)
            return false;

        terminal.ClearScreen();
        terminal.SetColor("bright_green");
        WriteThickDivider();
        terminal.WriteLine($"                    {Loc.Get("quest.mira_mercy.title")}                                      ");
        WriteThickDivider();
        terminal.WriteLine("");

        await Task.Delay(1000);

        terminal.SetColor("white");
        terminal.WriteLine(Loc.Get("quest.mira_mercy.scene_1"));
        terminal.WriteLine(Loc.Get("quest.mira_mercy.scene_2"));
        terminal.WriteLine(Loc.Get("quest.mira_mercy.scene_3"));
        terminal.WriteLine("");
        await Task.Delay(1500);

        terminal.SetColor("cyan");
        terminal.WriteLine(Loc.Get("quest.mira_mercy.kneels_1"));
        terminal.WriteLine(Loc.Get("quest.mira_mercy.kneels_2"));
        terminal.WriteLine("");
        await Task.Delay(1000);

        terminal.SetColor("white");
        terminal.WriteLine(Loc.Get("quest.mira_mercy.save_1"));
        terminal.WriteLine(Loc.Get("quest.mira_mercy.save_2"));
        terminal.WriteLine(Loc.Get("quest.mira_mercy.save_3"));
        terminal.WriteLine("");
        await Task.Delay(1500);

        terminal.SetColor("gray");
        terminal.WriteLine(Loc.Get("quest.mira_mercy.mother"));
        terminal.WriteLine(Loc.Get("quest.mira_mercy.youngman"));
        terminal.WriteLine("");

        terminal.SetColor("darkgray");
        terminal.Write("[");
        terminal.SetColor("bright_yellow");
        terminal.Write("1");
        terminal.SetColor("darkgray");
        terminal.Write("] ");
        terminal.SetColor("yellow");
        terminal.WriteLine(Loc.Get("quest.mira_mercy.option_1"));
        terminal.SetColor("darkgray");
        terminal.Write("[");
        terminal.SetColor("bright_yellow");
        terminal.Write("2");
        terminal.SetColor("darkgray");
        terminal.Write("] ");
        terminal.SetColor("yellow");
        terminal.WriteLine(Loc.Get("quest.mira_mercy.option_2"));
        terminal.SetColor("darkgray");
        terminal.Write("[");
        terminal.SetColor("bright_yellow");
        terminal.Write("3");
        terminal.SetColor("darkgray");
        terminal.Write("] ");
        terminal.SetColor("yellow");
        terminal.WriteLine(Loc.Get("quest.mira_mercy.option_3"));
        terminal.WriteLine("");

        var choice = await terminal.GetInput(Loc.Get("quest.mira_mercy.what_say"));

        switch (choice)
        {
            case "1":
                terminal.SetColor("bright_green");
                terminal.WriteLine("");
                terminal.WriteLine(Loc.Get("quest.mira_mercy.c1_nods"));
                terminal.WriteLine(Loc.Get("quest.mira_mercy.c1_gasps"));
                terminal.WriteLine("");
                await Task.Delay(1500);

                terminal.SetColor("white");
                terminal.WriteLine(Loc.Get("quest.mira_mercy.c1_sobs"));
                terminal.WriteLine(Loc.Get("quest.mira_mercy.c1_broken"));
                terminal.WriteLine("");
                await Task.Delay(1000);

                terminal.SetColor("cyan");
                terminal.WriteLine(Loc.Get("quest.mira_mercy.c1_saved"));
                terminal.WriteLine(Loc.Get("quest.mira_mercy.c1_hates"));
                story.SetStoryFlag("mira_chose_life", true);
                break;

            case "2":
                terminal.SetColor("white");
                terminal.WriteLine("");
                terminal.WriteLine(Loc.Get("quest.mira_mercy.c2_fades"));
                terminal.WriteLine("");
                await Task.Delay(1000);

                terminal.SetColor("cyan");
                terminal.WriteLine(Loc.Get("quest.mira_mercy.c2_here"));
                terminal.WriteLine("");
                await Task.Delay(1000);

                terminal.SetColor("white");
                terminal.WriteLine(Loc.Get("quest.mira_mercy.c2_smiles"));
                terminal.WriteLine(Loc.Get("quest.mira_mercy.c2_wails"));
                terminal.WriteLine("");
                await Task.Delay(1500);

                terminal.SetColor("cyan");
                terminal.WriteLine(Loc.Get("quest.mira_mercy.c2_kindest_1"));
                terminal.WriteLine(Loc.Get("quest.mira_mercy.c2_kindest_2"));
                story.SetStoryFlag("mira_chose_peace", true);
                break;

            case "3":
                terminal.SetColor("white");
                terminal.WriteLine("");
                terminal.WriteLine(Loc.Get("quest.mira_mercy.c3_looks"));
                terminal.WriteLine(Loc.Get("quest.mira_mercy.c3_noone"));
                terminal.WriteLine("");
                await Task.Delay(1500);

                terminal.SetColor("cyan");
                terminal.WriteLine(Loc.Get("quest.mira_mercy.c3_healer_1"));
                terminal.WriteLine(Loc.Get("quest.mira_mercy.c3_healer_2"));
                terminal.WriteLine("");
                await Task.Delay(1000);

                terminal.SetColor("bright_green");
                terminal.WriteLine(Loc.Get("quest.mira_mercy.c3_heals"));
                terminal.WriteLine(Loc.Get("quest.mira_mercy.c3_slips"));
                terminal.WriteLine("");
                story.SetStoryFlag("mira_chose_middle", true);
                break;
        }

        await Task.Delay(1500);

        terminal.SetColor("bright_green");
        WriteThickDivider();
        terminal.WriteLine($"           {Loc.Get("quest.mira_mercy.complete_title")}                               ");
        WriteThickDivider();
        terminal.WriteLine("");

        story.SetStoryFlag("mira_quest_choice_made", true);
        UsurperRemake.Systems.CompanionSystem.Instance.CompletePersonalQuest(
            UsurperRemake.Systems.CompanionId.Mira, true);

        terminal.SetColor("cyan");
        terminal.WriteLine(Loc.Get("quest.mira_mercy.thanks_1"));
        terminal.WriteLine(Loc.Get("quest.mira_mercy.thanks_2"));
        terminal.WriteLine("");

        // Bonus: Mira gains wisdom
        mira.BaseStats.HealingPower += 40;
        mira.BaseStats.MagicPower += 20;
        terminal.WriteLine(Loc.Get("quest.mira_mercy.bonus"), "bright_cyan");

        UsurperRemake.Systems.CompanionSystem.Instance.ModifyLoyalty(
            UsurperRemake.Systems.CompanionId.Mira, 20, "Helped her find meaning");

        await terminal.PressAnyKey();
        return true;
    }

    /// <summary>
    /// Vex Quest: "One More Sunrise" - Help him complete his bucket list
    /// Triggers progressively as he nears death
    /// </summary>
    private async Task<bool> CheckVexQuestEncounter(Character player, UsurperRemake.Systems.Companion vex,
        DungeonRoom room, UsurperRemake.Systems.StoryProgressionSystem story)
    {
        // Vex's quest can trigger anywhere, but depends on how close he is to death
        int daysWithVex = UsurperRemake.Systems.StoryProgressionSystem.Instance.CurrentGameDay - vex.RecruitedDay;

        // Only trigger if he's been with player for at least 10 days
        if (daysWithVex < 10)
            return false;

        // Check which bucket list items are done
        int itemsDone = 0;
        if (story.HasStoryFlag("vex_bucket_treasure")) itemsDone++;
        if (story.HasStoryFlag("vex_bucket_joke")) itemsDone++;
        if (story.HasStoryFlag("vex_bucket_truth")) itemsDone++;

        // All items complete = quest done
        if (itemsDone >= 3)
            return false;

        // 10% chance per room
        if (dungeonRandom.NextDouble() > 0.10)
            return false;

        terminal.ClearScreen();
        terminal.SetColor("bright_yellow");
        WriteThickDivider();
        terminal.WriteLine($"                    {Loc.Get("quest.vex_sunrise.title")}                                          ");
        WriteThickDivider();
        terminal.WriteLine("");

        await Task.Delay(1000);

        // Determine which event to trigger
        if (!story.HasStoryFlag("vex_bucket_treasure"))
        {
            await VexBucketTreasure(player, vex, story);
        }
        else if (!story.HasStoryFlag("vex_bucket_joke"))
        {
            await VexBucketJoke(player, vex, story);
        }
        else if (!story.HasStoryFlag("vex_bucket_truth"))
        {
            await VexBucketTruth(player, vex, story);
        }

        // Check if quest is now complete
        itemsDone = 0;
        if (story.HasStoryFlag("vex_bucket_treasure")) itemsDone++;
        if (story.HasStoryFlag("vex_bucket_joke")) itemsDone++;
        if (story.HasStoryFlag("vex_bucket_truth")) itemsDone++;

        if (itemsDone >= 3)
        {
            terminal.SetColor("bright_green");
            WriteThickDivider();
            terminal.WriteLine($"           {Loc.Get("quest.vex_sunrise.complete_title")}                                   ");
            WriteThickDivider();
            terminal.WriteLine("");

            UsurperRemake.Systems.CompanionSystem.Instance.CompletePersonalQuest(
                UsurperRemake.Systems.CompanionId.Vex, true);

            terminal.SetColor("cyan");
            terminal.WriteLine(Loc.Get("quest.vex_sunrise.complete_1"));
            terminal.WriteLine(Loc.Get("quest.vex_sunrise.complete_2"));
            terminal.WriteLine(Loc.Get("quest.vex_sunrise.complete_3"));
            terminal.WriteLine("");
        }

        await terminal.PressAnyKey();
        return true;
    }

    private async Task VexBucketTreasure(Character player, UsurperRemake.Systems.Companion vex,
        UsurperRemake.Systems.StoryProgressionSystem story)
    {
        terminal.SetColor("white");
        terminal.WriteLine(Loc.Get("quest.vex_sunrise.t_stops"));
        terminal.WriteLine("");
        await Task.Delay(1000);

        terminal.SetColor("yellow");
        terminal.WriteLine(Loc.Get("quest.vex_sunrise.t_wanted_1"));
        terminal.WriteLine(Loc.Get("quest.vex_sunrise.t_wanted_2"));
        terminal.WriteLine("");
        await Task.Delay(1000);

        terminal.SetColor("white");
        terminal.WriteLine(Loc.Get("quest.vex_sunrise.t_points_1"));
        terminal.WriteLine(Loc.Get("quest.vex_sunrise.t_points_2"));
        terminal.WriteLine("");

        terminal.SetColor("darkgray");
        terminal.Write("[");
        terminal.SetColor("bright_yellow");
        terminal.Write("1");
        terminal.SetColor("darkgray");
        terminal.Write("] ");
        terminal.SetColor("yellow");
        terminal.WriteLine(Loc.Get("quest.vex_sunrise.t_option_1"));
        terminal.SetColor("darkgray");
        terminal.Write("[");
        terminal.SetColor("bright_yellow");
        terminal.Write("2");
        terminal.SetColor("darkgray");
        terminal.Write("] ");
        terminal.SetColor("yellow");
        terminal.WriteLine(Loc.Get("quest.vex_sunrise.t_option_2"));
        terminal.WriteLine("");

        var choice = await terminal.GetInput(Loc.Get("ui.choice"));

        terminal.SetColor("bright_yellow");
        terminal.WriteLine("");
        terminal.WriteLine(Loc.Get("quest.vex_sunrise.t_opens"));
        terminal.WriteLine("");

        long gold = 50000 + dungeonRandom.Next(25000);
        player.Gold += gold;
        terminal.WriteLine(Loc.Get("quest.vex_sunrise.t_found_gold", gold), "bright_green");
        terminal.WriteLine("");

        if (choice == "1")
        {
            terminal.SetColor("cyan");
            terminal.WriteLine(Loc.Get("quest.vex_sunrise.t_together_1"));
            terminal.WriteLine(Loc.Get("quest.vex_sunrise.t_together_2"));
            UsurperRemake.Systems.CompanionSystem.Instance.ModifyLoyalty(
                UsurperRemake.Systems.CompanionId.Vex, 15, "Shared treasure discovery");
        }
        else
        {
            terminal.SetColor("cyan");
            terminal.WriteLine(Loc.Get("quest.vex_sunrise.t_alone_1"));
            terminal.WriteLine(Loc.Get("quest.vex_sunrise.t_alone_2"));
            UsurperRemake.Systems.CompanionSystem.Instance.ModifyLoyalty(
                UsurperRemake.Systems.CompanionId.Vex, 10, "Let him have his moment");
        }

        story.SetStoryFlag("vex_bucket_treasure", true);
        terminal.SetColor("gray");
        terminal.WriteLine("");
        terminal.WriteLine(Loc.Get("quest.vex_sunrise.t_bucket"));
    }

    private async Task VexBucketJoke(Character player, UsurperRemake.Systems.Companion vex,
        UsurperRemake.Systems.StoryProgressionSystem story)
    {
        terminal.SetColor("white");
        terminal.WriteLine(Loc.Get("quest.vex_sunrise.j_encounter"));
        terminal.WriteLine(Loc.Get("quest.vex_sunrise.j_steps"));
        terminal.WriteLine("");
        await Task.Delay(1000);

        terminal.SetColor("yellow");
        terminal.WriteLine(Loc.Get("quest.vex_sunrise.j_joke_1"));
        terminal.WriteLine("");
        await Task.Delay(1000);

        terminal.SetColor("red");
        terminal.WriteLine(Loc.Get("quest.vex_sunrise.j_confused"));
        terminal.WriteLine(Loc.Get("quest.vex_sunrise.j_what"));
        terminal.WriteLine("");
        await Task.Delay(1000);

        terminal.SetColor("yellow");
        terminal.WriteLine(Loc.Get("quest.vex_sunrise.j_punchline_1"));
        terminal.WriteLine(Loc.Get("quest.vex_sunrise.j_punchline_2"));
        terminal.WriteLine("");
        await Task.Delay(1500);

        terminal.SetColor("white");
        terminal.WriteLine(Loc.Get("quest.vex_sunrise.j_stare"));
        terminal.WriteLine(Loc.Get("quest.vex_sunrise.j_laughing"));
        terminal.WriteLine("");
        await Task.Delay(1000);

        terminal.SetColor("red");
        terminal.WriteLine(Loc.Get("quest.vex_sunrise.j_terrible"));
        terminal.WriteLine(Loc.Get("quest.vex_sunrise.j_getout"));
        terminal.WriteLine("");

        terminal.SetColor("cyan");
        terminal.WriteLine(Loc.Get("quest.vex_sunrise.j_beaming"));
        terminal.WriteLine(Loc.Get("quest.vex_sunrise.j_triumph_1"));
        terminal.WriteLine(Loc.Get("quest.vex_sunrise.j_triumph_2"));
        terminal.WriteLine("");

        story.SetStoryFlag("vex_bucket_joke", true);
        terminal.SetColor("gray");
        terminal.WriteLine(Loc.Get("quest.vex_sunrise.j_bucket"));

        UsurperRemake.Systems.CompanionSystem.Instance.ModifyLoyalty(
            UsurperRemake.Systems.CompanionId.Vex, 10, "Witnessed his triumph");
    }

    private async Task VexBucketTruth(Character player, UsurperRemake.Systems.Companion vex,
        UsurperRemake.Systems.StoryProgressionSystem story)
    {
        terminal.SetColor("white");
        terminal.WriteLine(Loc.Get("quest.vex_sunrise.truth_quiet"));
        terminal.WriteLine("");
        await Task.Delay(1000);

        terminal.SetColor("yellow");
        terminal.WriteLine(Loc.Get("quest.vex_sunrise.truth_tell_1"));
        terminal.WriteLine(Loc.Get("quest.vex_sunrise.truth_tell_2"));
        terminal.WriteLine("");

        terminal.SetColor("darkgray");
        terminal.Write("[");
        terminal.SetColor("bright_yellow");
        terminal.Write("1");
        terminal.SetColor("darkgray");
        terminal.Write("] ");
        terminal.SetColor("yellow");
        terminal.WriteLine(Loc.Get("quest.vex_sunrise.truth_option_1"));
        terminal.SetColor("darkgray");
        terminal.Write("[");
        terminal.SetColor("bright_yellow");
        terminal.Write("2");
        terminal.SetColor("darkgray");
        terminal.Write("] ");
        terminal.SetColor("yellow");
        terminal.WriteLine(Loc.Get("quest.vex_sunrise.truth_option_2"));
        terminal.WriteLine("");

        var choice = await terminal.GetInput(Loc.Get("quest.vex_sunrise.truth_prompt"));

        terminal.SetColor("white");
        terminal.WriteLine("");
        if (choice == "1")
        {
            terminal.WriteLine(Loc.Get("quest.vex_sunrise.truth_sit"));
        }
        else
        {
            terminal.WriteLine(Loc.Get("quest.vex_sunrise.truth_need"));
        }
        terminal.WriteLine("");
        await Task.Delay(1000);

        terminal.SetColor("cyan");
        terminal.WriteLine(Loc.Get("quest.vex_sunrise.truth_act_1"));
        terminal.WriteLine(Loc.Get("quest.vex_sunrise.truth_act_2"));
        terminal.WriteLine("");
        await Task.Delay(1500);

        terminal.SetColor("white");
        terminal.WriteLine(Loc.Get("quest.vex_sunrise.truth_cracks"));
        terminal.WriteLine("");

        terminal.SetColor("cyan");
        terminal.WriteLine(Loc.Get("quest.vex_sunrise.truth_terrified"));
        terminal.WriteLine(Loc.Get("quest.vex_sunrise.truth_want_1"));
        terminal.WriteLine(Loc.Get("quest.vex_sunrise.truth_want_2"));
        terminal.WriteLine("");
        await Task.Delay(2000);

        terminal.SetColor("white");
        terminal.WriteLine(Loc.Get("quest.vex_sunrise.truth_laughs"));
        terminal.WriteLine("");

        terminal.SetColor("cyan");
        terminal.WriteLine(Loc.Get("quest.vex_sunrise.truth_jokes_1"));
        terminal.WriteLine(Loc.Get("quest.vex_sunrise.truth_jokes_2"));
        terminal.WriteLine("");
        await Task.Delay(1500);

        if (choice == "1")
        {
            terminal.SetColor("white");
            terminal.WriteLine(Loc.Get("quest.vex_sunrise.truth_shoulder"));
            UsurperRemake.Systems.CompanionSystem.Instance.ModifyLoyalty(
                UsurperRemake.Systems.CompanionId.Vex, 20, "Listened to his truth");
        }
        else
        {
            terminal.SetColor("white");
            terminal.WriteLine(Loc.Get("quest.vex_sunrise.truth_wipes"));
            terminal.WriteLine(Loc.Get("quest.vex_sunrise.truth_alone"));
            UsurperRemake.Systems.CompanionSystem.Instance.ModifyLoyalty(
                UsurperRemake.Systems.CompanionId.Vex, 15, "Respected his truth");
        }

        story.SetStoryFlag("vex_bucket_truth", true);
        terminal.SetColor("gray");
        terminal.WriteLine("");
        terminal.WriteLine(Loc.Get("quest.vex_sunrise.truth_bucket"));
    }

    /// <summary>
    /// Melodia Quest: "The Lost Opus" - Recover a legendary musical score
    /// Triggers on floors 50-60
    /// </summary>
    private async Task<bool> CheckMelodiaQuestEncounter(Character player, UsurperRemake.Systems.Companion melodia,
        DungeonRoom room, UsurperRemake.Systems.StoryProgressionSystem story)
    {
        // Melodia's quest triggers on floors 50-60
        if (currentDungeonLevel < 50 || currentDungeonLevel > 60)
            return false;

        // Only trigger once
        if (story.HasStoryFlag("melodia_quest_opus_found"))
            return false;

        // 15% chance per room on correct floors
        if (dungeonRandom.NextDouble() > 0.15)
            return false;

        // Trigger the quest event
        terminal.ClearScreen();
        terminal.SetColor("bright_magenta");
        WriteThickDivider();
        terminal.WriteLine($"                       {Loc.Get("quest.melodia_opus.title")}                                        ");
        WriteThickDivider();
        terminal.WriteLine("");

        await Task.Delay(1000);

        terminal.SetColor("white");
        terminal.WriteLine(Loc.Get("quest.melodia_opus.melody_1"));
        terminal.WriteLine(Loc.Get("quest.melodia_opus.melody_2"));
        terminal.WriteLine("");
        await Task.Delay(1500);

        terminal.SetColor("bright_cyan");
        terminal.WriteLine(Loc.Get("quest.melodia_opus.hear_1"));
        terminal.WriteLine(Loc.Get("quest.melodia_opus.hear_2"));
        terminal.WriteLine("");
        await Task.Delay(1500);

        terminal.SetColor("white");
        terminal.WriteLine(Loc.Get("quest.melodia_opus.follows_1"));
        terminal.WriteLine(Loc.Get("quest.melodia_opus.follows_2"));
        terminal.WriteLine(Loc.Get("quest.melodia_opus.follows_3"));
        terminal.WriteLine("");
        await Task.Delay(1500);

        terminal.SetColor("bright_yellow");
        terminal.WriteLine(Loc.Get("quest.melodia_opus.chamber_1"));
        terminal.WriteLine(Loc.Get("quest.melodia_opus.chamber_2"));
        terminal.WriteLine(Loc.Get("quest.melodia_opus.chamber_3"));
        terminal.WriteLine("");
        await Task.Delay(1500);

        terminal.SetColor("bright_cyan");
        terminal.WriteLine(Loc.Get("quest.melodia_opus.breathes_1"));
        terminal.WriteLine(Loc.Get("quest.melodia_opus.breathes_2"));
        terminal.WriteLine(Loc.Get("quest.melodia_opus.breathes_3"));
        terminal.WriteLine("");
        await Task.Delay(1500);

        terminal.SetColor("white");
        terminal.WriteLine(Loc.Get("quest.melodia_opus.reaches_1"));
        terminal.WriteLine(Loc.Get("quest.melodia_opus.reaches_2"));
        terminal.WriteLine(Loc.Get("quest.melodia_opus.reaches_3"));
        terminal.WriteLine("");

        terminal.SetColor("darkgray");
        terminal.Write("[");
        terminal.SetColor("bright_yellow");
        terminal.Write("1");
        terminal.SetColor("darkgray");
        terminal.Write("] ");
        terminal.SetColor("yellow");
        terminal.WriteLine(Loc.Get("quest.melodia_opus.option_1"));
        terminal.SetColor("darkgray");
        terminal.Write("[");
        terminal.SetColor("bright_yellow");
        terminal.Write("2");
        terminal.SetColor("darkgray");
        terminal.Write("] ");
        terminal.SetColor("yellow");
        terminal.WriteLine(Loc.Get("quest.melodia_opus.option_2"));
        terminal.SetColor("darkgray");
        terminal.Write("[");
        terminal.SetColor("bright_yellow");
        terminal.Write("3");
        terminal.SetColor("darkgray");
        terminal.Write("] ");
        terminal.SetColor("yellow");
        terminal.WriteLine(Loc.Get("quest.melodia_opus.option_3"));
        terminal.WriteLine("");

        var choice = await terminal.GetInput(Loc.Get("quest.melodia_opus.what_do"));

        switch (choice)
        {
            case "1":
                terminal.SetColor("white");
                terminal.WriteLine("");
                terminal.WriteLine(Loc.Get("quest.melodia_opus.c1_take"));
                terminal.WriteLine("");
                await Task.Delay(1000);

                terminal.SetColor("bright_magenta");
                terminal.WriteLine(Loc.Get("quest.melodia_opus.c1_lifts_1"));
                terminal.WriteLine(Loc.Get("quest.melodia_opus.c1_lifts_2"));
                terminal.WriteLine(Loc.Get("quest.melodia_opus.c1_lifts_3"));
                terminal.WriteLine("");
                await Task.Delay(1500);

                terminal.SetColor("bright_cyan");
                terminal.WriteLine(Loc.Get("quest.melodia_opus.c1_hear_1"));
                terminal.WriteLine(Loc.Get("quest.melodia_opus.c1_hear_2"));
                terminal.WriteLine(Loc.Get("quest.melodia_opus.c1_hear_3"));
                terminal.WriteLine("");

                UsurperRemake.Systems.CompanionSystem.Instance.ModifyLoyalty(
                    UsurperRemake.Systems.CompanionId.Melodia, 20, "Trusted her with the Lost Opus");
                break;

            case "2":
                terminal.SetColor("white");
                terminal.WriteLine("");
                terminal.WriteLine(Loc.Get("quest.melodia_opus.c2_work"));
                terminal.WriteLine("");
                await Task.Delay(1000);

                terminal.SetColor("bright_cyan");
                terminal.WriteLine(Loc.Get("quest.melodia_opus.c2_brilliant_1"));
                terminal.WriteLine(Loc.Get("quest.melodia_opus.c2_brilliant_2"));
                terminal.WriteLine(Loc.Get("quest.melodia_opus.c2_brilliant_3"));
                terminal.WriteLine("");
                await Task.Delay(1500);

                terminal.SetColor("bright_magenta");
                terminal.WriteLine(Loc.Get("quest.melodia_opus.c2_plays_1"));
                terminal.WriteLine(Loc.Get("quest.melodia_opus.c2_plays_2"));
                terminal.WriteLine(Loc.Get("quest.melodia_opus.c2_plays_3"));
                terminal.WriteLine("");
                await Task.Delay(1500);

                terminal.SetColor("bright_cyan");
                terminal.WriteLine(Loc.Get("quest.melodia_opus.c2_saved_1"));
                terminal.WriteLine(Loc.Get("quest.melodia_opus.c2_saved_2"));
                terminal.WriteLine("");

                UsurperRemake.Systems.CompanionSystem.Instance.ModifyLoyalty(
                    UsurperRemake.Systems.CompanionId.Melodia, 25, "Helped transcribe the Lost Opus together");
                break;

            case "3":
            default:
                terminal.SetColor("white");
                terminal.WriteLine("");
                terminal.WriteLine(Loc.Get("quest.melodia_opus.c3_reach_1"));
                terminal.WriteLine(Loc.Get("quest.melodia_opus.c3_reach_2"));
                terminal.WriteLine("");
                await Task.Delay(1000);

                terminal.SetColor("red");
                var opusDmg = player.MaxHP / 5;
                player.HP = Math.Max(1, player.HP - opusDmg);
                terminal.WriteLine(Loc.Get("quest.melodia_opus.c3_damage", opusDmg));
                terminal.WriteLine("");
                await Task.Delay(1000);

                terminal.SetColor("bright_cyan");
                terminal.WriteLine(Loc.Get("quest.melodia_opus.c3_takes_1"));
                terminal.WriteLine(Loc.Get("quest.melodia_opus.c3_takes_2"));
                terminal.WriteLine(Loc.Get("quest.melodia_opus.c3_takes_3"));
                terminal.WriteLine("");

                UsurperRemake.Systems.CompanionSystem.Instance.ModifyLoyalty(
                    UsurperRemake.Systems.CompanionId.Melodia, -5, "Tried to take the Opus");
                break;
        }

        await Task.Delay(1500);

        terminal.SetColor("bright_green");
        WriteThickDivider();
        terminal.WriteLine($"              {Loc.Get("quest.melodia_opus.complete_title")}                                  ");
        WriteThickDivider();
        terminal.WriteLine("");

        story.SetStoryFlag("melodia_quest_opus_found", true);
        UsurperRemake.Systems.CompanionSystem.Instance.CompletePersonalQuest(
            UsurperRemake.Systems.CompanionId.Melodia, true);

        // Bonus: Melodia gains power from understanding the world's true song
        melodia.BaseStats.MagicPower += 30;
        melodia.BaseStats.HealingPower += 20;
        terminal.WriteLine(Loc.Get("quest.melodia_opus.bonus_1"), "bright_cyan");
        terminal.WriteLine(Loc.Get("quest.melodia_opus.bonus_2"), "bright_cyan");

        await terminal.PressAnyKey();
        return true;
    }

    #endregion

    #region Divine Punishment System

    /// <summary>
    /// Check if divine punishment should trigger and apply effects before combat
    /// Returns true if punishment was applied, along with combat modifiers
    /// </summary>
    private async Task<(bool applied, int damageModifier, int defenseModifier)> CheckDivinePunishment(Character player)
    {
        if (!player.DivineWrathPending || player.DivineWrathLevel <= 0)
        {
            return (false, 0, 0);
        }

        // Chance to trigger based on wrath level: 20%/40%/60% per combat
        int triggerChance = player.DivineWrathLevel * 20;
        if (dungeonRandom.Next(100) >= triggerChance)
        {
            // No punishment this time, but tick the wrath timer
            player.TickDivineWrath();
            return (false, 0, 0);
        }

        // Divine punishment triggers!
        terminal.WriteLine("");
        WriteBoxHeader(Loc.Get("dungeon.divine_wrath"), "bright_red", 64);
        terminal.WriteLine("");
        await Task.Delay(1000);

        // Choose punishment based on wrath level
        int damageModifier = 0;
        int defenseModifier = 0;

        switch (player.DivineWrathLevel)
        {
            case 1: // Minor punishment - stat debuff
                await ApplyMinorDivinePunishment(player);
                damageModifier = -10; // 10% less damage dealt
                defenseModifier = -10; // 10% less defense
                break;

            case 2: // Moderate punishment - HP damage + debuff
                await ApplyModerateDivinePunishment(player);
                damageModifier = -20;
                defenseModifier = -20;
                break;

            case 3: // Severe punishment - Major damage + severe debuffs
                await ApplySevereDivinePunishment(player);
                damageModifier = -30;
                defenseModifier = -30;
                break;
        }

        terminal.WriteLine("");
        terminal.SetColor("gray");
        terminal.WriteLine($"(Combat penalties: {damageModifier}% damage, {defenseModifier}% defense)");
        terminal.WriteLine("");
        await Task.Delay(2000);

        // Clear the wrath after punishment (or reduce level for severe cases)
        if (player.DivineWrathLevel >= 3)
        {
            player.DivineWrathLevel = 1; // Severe punishment reduces but doesn't fully clear
            player.DivineWrathTurnsRemaining = 30;
        }
        else
        {
            player.ClearDivineWrath();
        }

        return (true, damageModifier, defenseModifier);
    }

    private async Task ApplyMinorDivinePunishment(Character player)
    {
        string godName = player.AngeredGodName;
        string betrayedFor = player.BetrayedForGodName;

        terminal.SetColor("red");
        terminal.WriteLine($"A cold presence fills the air...");
        await Task.Delay(1000);

        terminal.SetColor("bright_magenta");
        terminal.WriteLine($"\"{player.Name2}... You dare worship at another's altar?\"");
        await Task.Delay(1500);

        terminal.SetColor("red");
        var punishments = new[]
        {
            $"The voice of {godName} echoes in your mind, sapping your strength!",
            $"{godName}'s displeasure manifests as a chilling weakness in your limbs!",
            $"You feel {godName}'s watchful gaze - judging, disappointed, angry!"
        };
        terminal.WriteLine(punishments[dungeonRandom.Next(punishments.Length)]);
        await Task.Delay(1500);

        terminal.SetColor("yellow");
        terminal.WriteLine("Your attacks will be weakened in the coming battle.");
    }

    private async Task ApplyModerateDivinePunishment(Character player)
    {
        string godName = player.AngeredGodName;
        string betrayedFor = player.BetrayedForGodName;

        terminal.SetColor("bright_red");
        terminal.WriteLine($"The dungeon trembles! Divine fury descends!");
        await Task.Delay(1000);

        terminal.SetColor("bright_magenta");
        terminal.WriteLine($"\"FAITHLESS ONE! You gave to {betrayedFor} what was MINE!\"");
        await Task.Delay(1500);

        // Deal HP damage
        long damage = Math.Max(1, player.HP / 4); // 25% current HP
        player.HP = Math.Max(1, player.HP - damage);

        terminal.SetColor("red");
        terminal.WriteLine($"Divine lightning strikes you for {damage} damage!");
        terminal.WriteLine($"{Loc.Get("combat.bar_hp")}: {player.HP}/{player.MaxHP}");
        await Task.Delay(1500);

        terminal.SetColor("bright_magenta");
        terminal.WriteLine($"\"Let this pain remind you of your broken vows!\"");
        await Task.Delay(1000);

        terminal.SetColor("yellow");
        terminal.WriteLine("Your strength and defense are significantly reduced!");
    }

    private async Task ApplySevereDivinePunishment(Character player)
    {
        string godName = player.AngeredGodName;
        string betrayedFor = player.BetrayedForGodName;

        terminal.SetColor("bright_red");
        WriteThickDivider(66, "bright_red");
        terminal.SetColor("bright_red");
        terminal.WriteLine("              THE HEAVENS THEMSELVES CRY OUT IN RAGE!");
        WriteThickDivider(66, "bright_red");
        await Task.Delay(1500);

        terminal.SetColor("bright_magenta");
        terminal.WriteLine($"\"WRETCHED TRAITOR! {betrayedFor.ToUpper()} CANNOT PROTECT YOU FROM MY WRATH!\"");
        await Task.Delay(1500);

        // Severe HP damage
        long damage = Math.Max(1, player.HP / 2); // 50% current HP
        player.HP = Math.Max(1, player.HP - damage);

        terminal.SetColor("red");
        terminal.WriteLine($"Divine fire consumes you for {damage} damage!");
        terminal.WriteLine($"{Loc.Get("combat.bar_hp")}: {player.HP}/{player.MaxHP}");
        await Task.Delay(1000);

        // Mana drain
        if (player.Mana > 0)
        {
            long manaDrain = player.Mana / 2;
            player.Mana = Math.Max(0, player.Mana - manaDrain);
            terminal.WriteLine($"Your magical essence is torn away! -{manaDrain} Mana!");
        }
        await Task.Delay(1000);

        // Random disease or curse
        if (dungeonRandom.Next(100) < 50)
        {
            var diseases = new[] { "Blind", "Plague", "Measles" };
            string disease = diseases[dungeonRandom.Next(diseases.Length)];
            switch (disease)
            {
                case "Blind":
                    player.Blind = true;
                    terminal.WriteLine("Divine light sears your eyes - you are BLINDED!");
                    break;
                case "Plague":
                    player.Plague = true;
                    terminal.WriteLine("Pestilence courses through your veins - you have the PLAGUE!");
                    break;
                case "Measles":
                    player.Measles = true;
                    terminal.WriteLine("Your skin erupts in painful sores - MEASLES!");
                    break;
            }
            await Task.Delay(1000);
        }

        terminal.SetColor("bright_magenta");
        terminal.WriteLine($"\"Remember this agony, {player.Name2}. My patience is NOT infinite.\"");
        await Task.Delay(1500);

        terminal.SetColor("red");
        terminal.WriteLine("You are severely weakened. Survival is not guaranteed...");
    }

    #endregion

    #region Group Dungeon System

    /// <summary>
    /// Broadcast a dungeon event message to all group followers (not the leader).
    /// Leader sees the event via their own terminal; followers get it via EnqueueMessage.
    /// </summary>
    private void BroadcastDungeonEvent(string message)
    {
        var ctx = SessionContext.Current;
        if (ctx == null) return;
        var group = GroupSystem.Instance?.GetGroupFor(ctx.Username);
        if (group == null || !group.IsLeader(ctx.Username)) return;
        GroupSystem.Instance!.BroadcastToAllGroupSessions(group, message, excludeUsername: ctx.Username);
    }

    /// <summary>
    /// Share gold and/or XP from a dungeon event with all grouped players.
    /// The leader has already received the full amount — this gives each grouped player their share
    /// and reduces the leader's gold to their fair share. XP uses level-gap multiplier.
    /// </summary>
    private void ShareEventRewardsWithGroup(Character leader, long goldAmount, long xpAmount, string source)
    {
        if (teammates == null || teammates.Count == 0) return;
        if (goldAmount <= 0 && xpAmount <= 0) return;

        var groupedPlayers = teammates.Where(t => t != null && t.IsAlive && t.IsGroupedPlayer).ToList();
        if (groupedPlayers.Count == 0) return;

        int totalMembers = 1 + groupedPlayers.Count;

        // Split gold
        long goldPerMember = goldAmount > 0 ? goldAmount / totalMembers : 0;
        if (goldAmount > 0)
        {
            leader.Gold -= (goldAmount - goldPerMember); // Reduce leader to their share
        }

        // Calculate XP for each grouped player and notify
        int highestLevel = Math.Max(leader.Level, groupedPlayers.Max(t => t.Level));
        foreach (var mate in groupedPlayers)
        {
            if (goldPerMember > 0)
            {
                mate.Gold += goldPerMember;
                mate.Statistics?.RecordGoldChange(mate.Gold);
            }

            long xpShare = 0;
            if (xpAmount > 0)
            {
                float mult = GroupSystem.GetGroupXPMultiplier(mate.Level, highestLevel);
                xpShare = (long)(xpAmount * mult);
                mate.Experience += xpShare;
            }

            // Notify grouped player of their share
            var parts = new System.Collections.Generic.List<string>();
            if (goldPerMember > 0) parts.Add($"+{goldPerMember:N0} gold");
            if (xpShare > 0) parts.Add($"+{xpShare:N0} XP");
            if (parts.Count > 0)
            {
                var session = GroupSystem.GetSession(mate.GroupPlayerUsername ?? "");
                session?.EnqueueMessage(
                    $"\u001b[1;32m  ═══ {source} (Your Share) ═══\u001b[0m\n" +
                    $"\u001b[33m  {string.Join("  ", parts)}\u001b[0m");
            }
        }

        // Notify leader about the split
        if (goldAmount > 0)
        {
            terminal.SetColor("gray");
            terminal.WriteLine($"(Gold split {totalMembers} ways: {goldPerMember} each)");
        }
    }

    /// <summary>
    /// Push a full room view to each group follower's terminal, clearing their screen first.
    /// Each follower gets a personalized display with their own HP/gold status bar.
    /// Called from the leader's thread when the leader moves to a new room or descends floors.
    /// </summary>
    private void PushRoomToFollowers(DungeonRoom room)
    {
        var ctx = SessionContext.Current;
        if (ctx == null) return;
        var group = GroupSystem.Instance?.GetGroupFor(ctx.Username);
        if (group == null || !group.IsLeader(ctx.Username)) return;

        // Build the shared room portion (same for all followers)
        string roomAnsi = BuildRoomAnsi(room, ctx.Username);

        // Snapshot teammates for thread safety
        List<Character> teamSnap;
        lock (teammates) { teamSnap = new List<Character>(teammates); }

        foreach (var mate in teamSnap)
        {
            if (mate == null || !mate.IsGroupedPlayer) continue;
            PushRoomToSingleFollower(mate, roomAnsi, ctx.Username);
        }
    }

    /// <summary>
    /// Push a room view to a single follower's terminal. Clear screen + room + personal status.
    /// </summary>
    private static void PushRoomToSingleFollower(Character follower, string roomAnsi, string leaderName)
    {
        var followerTerm = follower.RemoteTerminal;
        if (followerTerm == null) return;

        var sb = new System.Text.StringBuilder();
        sb.Append("\x1b[2J\x1b[H"); // Clear screen + cursor home

        // Room content (shared)
        sb.Append(roomAnsi);

        // Personal status bar
        sb.AppendLine($"\u001b[90m  {new string('─', 55)}\u001b[0m");
        int hpPct = follower.MaxHP > 0 ? (int)(follower.HP * 20 / follower.MaxHP) : 0;
        hpPct = Math.Clamp(hpPct, 0, 20);
        string hpBar = new string('#', hpPct) + new string('.', 20 - hpPct);
        sb.AppendLine($"\u001b[37m  HP: \u001b[31m[{hpBar}]\u001b[37m {follower.HP}/{follower.MaxHP}  \u001b[33mGold: {follower.Gold:N0}  \u001b[32mPotions: {follower.Healing}/{follower.MaxPotions}\u001b[0m");
        sb.AppendLine($"\u001b[90m  Following \u001b[97m{leaderName}\u001b[90m | [I]nv [P]ot [=]Stats [Q]Leave | /party /help /say\u001b[0m");
        sb.AppendLine();

        followerTerm.WriteRawAnsi(sb.ToString());
    }

    /// <summary>
    /// Build the ANSI room description string shared by all followers.
    /// </summary>
    private string BuildRoomAnsi(DungeonRoom room, string leaderName)
    {
        var sb = new System.Text.StringBuilder();
        string tc = GetAnsiThemeColor(currentFloor.Theme);

        // Room header
        sb.AppendLine($"{tc}  ╔{new string('═', 55)}╗\u001b[0m");
        sb.AppendLine($"{tc}  ║  {room.Name,-52} ║\u001b[0m");
        sb.AppendLine($"{tc}  ╚{new string('═', 55)}╝\u001b[0m");

        // Floor/theme/danger/status line
        string stars = new string('*', room.DangerRating) + new string('.', 3 - room.DangerRating);
        string dc = room.DangerRating >= 3 ? "\u001b[91m" : room.DangerRating >= 2 ? "\u001b[93m" : "\u001b[92m";
        string status = room.IsBossRoom ? " \u001b[91m[BOSS]\u001b[0m"
            : room.IsCleared ? " \u001b[92m[CLEARED]\u001b[0m"
            : room.HasMonsters ? " \u001b[91m[DANGER]\u001b[0m" : "";
        sb.AppendLine($"\u001b[90m  Floor {currentDungeonLevel} | {tc}{currentFloor.Theme}\u001b[90m | {dc}{stars}\u001b[0m{status}");
        sb.AppendLine();

        // Description + atmosphere
        if (!string.IsNullOrEmpty(room.Description))
            sb.AppendLine($"\u001b[37m  {room.Description}\u001b[0m");
        if (!string.IsNullOrEmpty(room.AtmosphereText))
            sb.AppendLine($"\u001b[90m  {room.AtmosphereText}\u001b[0m");
        sb.AppendLine();

        // Room contents
        if (room.HasMonsters && !room.IsCleared)
            sb.AppendLine(room.IsBossRoom
                ? "\u001b[91m  >> A powerful presence dominates this room! <<\u001b[0m"
                : "\u001b[91m  >> Hostile creatures lurk in the shadows <<\u001b[0m");
        if (room.HasTreasure && !room.TreasureLooted)
            sb.AppendLine("\u001b[93m  >> Something valuable glints in the darkness <<\u001b[0m");
        if (room.HasEvent && !room.EventCompleted)
            sb.AppendLine($"\u001b[96m  >> {GetEventHint(room.EventType).Replace(">>", "").Replace("<<", "").Trim()} <<\u001b[0m");
        if (room.HasStairsDown)
            sb.AppendLine("\u001b[94m  >> Stairs lead down to a deeper level <<\u001b[0m");

        // Unexamined features
        var unexamined = room.Features?.Where(f => !f.IsInteracted).ToList();
        if (unexamined != null && unexamined.Count > 0)
        {
            sb.AppendLine("\u001b[96m  You notice:\u001b[0m");
            foreach (var f in unexamined)
                sb.AppendLine($"\u001b[37m    - {f.Name}\u001b[0m");
        }

        // Exits
        if (room.Exits.Count > 0)
        {
            sb.Append("\u001b[37m  Exits:");
            foreach (var exit in room.Exits)
            {
                var target = currentFloor.GetRoom(exit.Value.TargetRoomId);
                string dirKey = GetDirectionKey(exit.Key);
                string st = target == null ? "" : target.IsCleared ? " clr" : target.IsExplored ? " exp" : " ?";
                sb.Append($" \u001b[90m[\u001b[96m{dirKey}\u001b[90m{st}\u001b[90m]");
            }
            sb.AppendLine("\u001b[0m");
        }

        return sb.ToString();
    }

    /// <summary>
    /// Split dungeon rewards among all party members (leader + teammates including NPCs).
    /// Gold: divided evenly by party size.
    /// XP for grouped players: full XP with level gap penalty.
    /// XP for NPC teammates: 75% of total XP (matches AwardTeammateExperience rate).
    /// Companions are skipped (handled by CompanionSystem).
    /// Returns (leaderXP, leaderGold).
    /// </summary>
    private (long leaderXP, long leaderGold) SplitPartyRewards(long totalXP, long totalGold, string source)
    {
        var player = GetCurrentPlayer();
        if (player == null) return (totalXP, totalGold);

        // Snapshot teammates for thread safety
        List<Character> teamSnap;
        lock (teammates) { teamSnap = new List<Character>(teammates); }

        int totalMembers = 1 + teamSnap.Count;
        if (totalMembers <= 1) return (totalXP, totalGold);

        long goldPerMember = totalGold / totalMembers;

        // Find highest level for gap penalty
        int highestLevel = player.Level;
        foreach (var t in teamSnap)
            if (t.Level > highestLevel) highestLevel = t.Level;

        // Award each teammate
        foreach (var teammate in teamSnap)
        {
            if (teammate == null || !teammate.IsAlive) continue;

            // Gold share for everyone
            teammate.Gold += goldPerMember;

            if (teammate.IsGroupedPlayer)
            {
                // Real players: full XP with level gap penalty
                float groupXPMult = GroupSystem.GetGroupXPMultiplier(teammate.Level, highestLevel);
                long memberXP = (long)(totalXP * groupXPMult);
                teammate.Experience += memberXP;
                teammate.Statistics.RecordGoldChange(teammate.Gold);

                var session = GroupSystem.GetSession(teammate.GroupPlayerUsername ?? "");
                if (session != null)
                {
                    string penalty = groupXPMult < 1.0f ? $" ({(int)(groupXPMult * 100)}%)" : "";
                    session.EnqueueMessage(
                        $"\u001b[1;32m  ═══ {source} (Your Share) ═══\u001b[0m\n" +
                        $"\u001b[33m  Gold: +{goldPerMember:N0}  XP: +{memberXP:N0}{penalty}\u001b[0m");
                }
            }
            else if (!teammate.IsCompanion && !teammate.IsEcho)
            {
                // NPC teammates (spouses, mercenaries): 75% XP
                long npcXP = (long)(totalXP * 0.75);
                teammate.Experience += npcXP;
            }
        }

        // Leader's share
        float leaderMult = GroupSystem.GetGroupXPMultiplier(player.Level, highestLevel);
        long leaderXP = (long)(totalXP * leaderMult);

        return (leaderXP, goldPerMember);
    }

    /// <summary>
    /// Award XP and gold to the leader, splitting among party if in a group.
    /// Returns actual (xp, gold) awarded to the leader.
    /// </summary>
    private (long xp, long gold) AwardDungeonReward(long xp, long gold, string source)
    {
        var player = GetCurrentPlayer();
        if (player == null) return (xp, gold);

        // Only split rewards when there are actual grouped human players
        // NPC teammates (mercenaries, spouse) don't reduce the leader's non-combat rewards
        bool hasGroupedPlayers;
        lock (teammates) { hasGroupedPlayers = teammates.Any(t => t.IsGroupedPlayer); }
        if (hasGroupedPlayers)
        {
            var (leaderXP, leaderGold) = SplitPartyRewards(xp, gold, source);
            player.Gold += leaderGold;
            player.Experience += leaderXP;
            return (leaderXP, leaderGold);
        }
        else
        {
            player.Gold += gold;
            player.Experience += xp;
            return (xp, gold);
        }
    }

    /// <summary>
    /// Called by the leader's EnterLocation() to mark the group as in-dungeon and notify followers.
    /// </summary>
    private async Task SetupGroupDungeon(Character player, TerminalEmulator term)
    {
        var ctx = SessionContext.Current;
        if (ctx == null) return;

        var group = GroupSystem.Instance?.GetGroupFor(ctx.Username);
        if (group == null || !group.IsLeader(ctx.Username)) return;

        group.IsInDungeon = true;
        group.CurrentFloor = currentDungeonLevel;

        // Notify group followers that leader has entered the dungeon
        GroupSystem.Instance!.NotifyGroup(group,
            $"\u001b[1;33m  * Your group leader {ctx.Username} has entered the dungeon (Floor {currentDungeonLevel})!\u001b[0m" +
            $"\n\u001b[1;33m  * Go to the Dungeons to join them!\u001b[0m",
            excludeUsername: ctx.Username);

        // Show leader how many slots are available for NPCs
        List<string> members;
        lock (group.MemberUsernames)
        {
            members = new List<string>(group.MemberUsernames);
        }

        int groupedPlayerCount = members.Count - 1; // exclude leader
        int npcSlots = 4 - groupedPlayerCount; // 4 teammate cap

        // Cap existing NPC teammates if group members take up slots
        if (groupedPlayerCount > 0 && teammates.Count > npcSlots)
        {
            var excess = teammates.Count - npcSlots;
            var removed = teammates.GetRange(teammates.Count - excess, excess);
            teammates.RemoveRange(teammates.Count - excess, excess);
            if (removed.Count > 0)
            {
                term.SetColor("yellow");
                foreach (var r in removed)
                    term.WriteLine($"  {r.Name2} stays behind to make room for group members.");
            }
        }
    }

    /// <summary>
    /// Enter the dungeon as a group follower. Attaches to the leader's session,
    /// adds this player's Character to the leader's teammates, sets up output forwarding,
    /// and enters a simple follower loop.
    /// </summary>
    private async Task EnterAsGroupFollower(Character player, TerminalEmulator term, DungeonGroup group)
    {
        var ctx = SessionContext.Current;
        if (ctx == null) return;

        var mySession = GroupSystem.GetSession(ctx.Username);
        if (mySession == null) return;

        // Find the leader's session and dungeon location
        var leaderSession = GroupSystem.GetSession(group.LeaderUsername);
        if (leaderSession?.Context?.LocationManager == null)
        {
            term.SetColor("red");
            term.WriteLine("  Could not connect to your group leader's dungeon session.");
            await term.PressAnyKey();
            return;
        }

        var leaderDungeon = leaderSession.Context.LocationManager.GetLocation(GameLocation.Dungeons) as DungeonLocation;
        if (leaderDungeon == null)
        {
            term.SetColor("red");
            term.WriteLine("  Your group leader is not in the dungeon.");
            await term.PressAnyKey();
            return;
        }

        // Check if leader's party is full (4 teammates max)
        bool partyFull;
        lock (leaderDungeon.teammates)
        {
            partyFull = leaderDungeon.teammates.Count >= 4;
        }
        if (partyFull)
        {
            term.SetColor("yellow");
            term.WriteLine("  Your group leader's party is full (4 allies maximum).");
            await term.PressAnyKey();
            return;
        }

        // Set up this player's Character for group combat
        player.RemoteTerminal = term;
        player.GroupPlayerUsername = ctx.Username;
        player.CombatInputChannel = System.Threading.Channels.Channel.CreateBounded<string>(1);

        // Add to leader's teammates list
        lock (leaderDungeon.teammates)
        {
            leaderDungeon.teammates.Add(player);
        }

        // Mark this session as a group follower
        mySession.IsGroupFollower = true;
        mySession.GroupLeaderSession = leaderSession;

        // Update online location
        ctx.OnlineState?.UpdateLocation($"Dungeon (Group: {group.LeaderUsername})");

        // Notify leader and group
        leaderSession.EnqueueMessage(
            $"\u001b[1;32m  * {ctx.Username} has joined your dungeon group!\u001b[0m");
        GroupSystem.Instance?.NotifyGroup(group,
            $"\u001b[1;32m  * {ctx.Username} has entered the dungeon with the group.\u001b[0m",
            excludeUsername: ctx.Username);

        // Show the current room the leader is in (so follower immediately feels "in" the dungeon)
        var leaderRoom = leaderDungeon.currentFloor?.GetCurrentRoom();
        if (leaderRoom != null)
        {
            string roomAnsi = leaderDungeon.BuildRoomAnsi(leaderRoom, group.LeaderUsername);
            PushRoomToSingleFollower(player, roomAnsi, group.LeaderUsername);
        }
        else
        {
            // Fallback if room not available
            term.ClearScreen();
            term.SetColor("gray");
            term.WriteLine($"  You join {group.LeaderUsername}'s group in the dungeon...");
            term.WriteLine("");
        }

        try
        {
            await GroupFollowerLoop(player, term, group, mySession, leaderSession, leaderDungeon);
        }
        finally
        {
            // Clean up: remove from leader's party
            CleanupGroupFollower(player, term, mySession, leaderSession, leaderDungeon);
        }
    }

    /// <summary>
    /// Follower loop — active input loop (MUD model).
    /// The follower reads from their own terminal. During combat, input is forwarded
    /// to the CombatInputChannel which the combat engine reads. Between combats,
    /// the follower sees dungeon events via EnqueueMessage broadcasts and can use
    /// slash commands or Q to leave.
    /// </summary>
    private async Task GroupFollowerLoop(
        Character player, TerminalEmulator term, DungeonGroup group,
        PlayerSession mySession, PlayerSession leaderSession,
        DungeonLocation leaderDungeon)
    {
        try
        {
            while (mySession.IsGroupFollower && leaderSession.Context != null)
            {
                // Active read from follower's own terminal — message pump is active,
                // so EnqueueMessage broadcasts appear at the prompt
                string? input;
                try
                {
                    input = await term.GetInput("");
                }
                catch
                {
                    break; // disconnect or stream error
                }

                if (input == null) break; // disconnect

                var trimmed = input.Trim();

                // Q = leave group dungeon
                if (trimmed.Equals("Q", StringComparison.OrdinalIgnoreCase))
                    break;

                // If combat engine is waiting for this player's input, forward to channel
                if (player.IsAwaitingCombatInput && player.CombatInputChannel != null)
                {
                    try
                    {
                        await player.CombatInputChannel.Writer.WriteAsync(trimmed);
                    }
                    catch { /* channel closed */ }
                    continue;
                }

                // Slash commands — both chat and game commands
                if (trimmed.StartsWith("/"))
                {
                    bool handled = await ProcessFollowerSlashCommand(trimmed, player, term);
                    if (!handled)
                        await MudChatSystem.TryProcessCommand(trimmed, term);
                    continue;
                }

                // I = Inventory (with equip/unequip)
                if (trimmed.Equals("I", StringComparison.OrdinalIgnoreCase))
                {
                    await ShowFollowerInventory(player, term);
                    RePushRoomToFollower(player, leaderDungeon, group);
                    continue;
                }

                // P = Use potion (healing or mana)
                if (trimmed.Equals("P", StringComparison.OrdinalIgnoreCase))
                {
                    await UseFollowerPotion(player, term);
                    RePushRoomToFollower(player, leaderDungeon, group);
                    continue;
                }

                // = = Character status
                if (trimmed == "=")
                {
                    await ShowFollowerStatus(player, term);
                    RePushRoomToFollower(player, leaderDungeon, group);
                    continue;
                }

                // Invalid input feedback (between combats)
                if (trimmed.Length > 0)
                {
                    term.SetColor("gray");
                    term.WriteLine($"  Unknown command '{trimmed}'. Use I/P/=/Q or /help");
                }
            }
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine($"[MUD] [{mySession.Username}] GroupFollowerLoop error: {ex.Message}");
        }
    }

    /// <summary>
    /// Re-push the current room view to a single follower (after they dismiss an overlay like inventory/status).
    /// </summary>
    private static void RePushRoomToFollower(Character follower, DungeonLocation leaderDungeon, DungeonGroup group)
    {
        var room = leaderDungeon.currentFloor?.GetCurrentRoom();
        if (room != null)
        {
            string roomAnsi = leaderDungeon.BuildRoomAnsi(room, group.LeaderUsername);
            PushRoomToSingleFollower(follower, roomAnsi, group.LeaderUsername);
        }
    }

    /// <summary>
    /// Process game-specific slash commands for a group follower.
    /// Returns true if the command was handled, false to fall through to MudChatSystem.
    /// </summary>
    private static async Task<bool> ProcessFollowerSlashCommand(string input, Character player, TerminalEmulator term)
    {
        var command = input.Substring(1).ToLower().Trim();
        switch (command)
        {
            case "":
            case "?":
            case "help":
            case "commands":
                if (GameConfig.ScreenReaderMode)
                    term.WriteLine("FOLLOWER COMMANDS", "bright_cyan");
                else
                    term.WriteLine("═══ FOLLOWER COMMANDS ═══", "bright_cyan");
                term.SetColor("white");
                term.WriteLine("  Between combats:");
                term.SetColor("yellow");
                term.Write("    I");
                term.SetColor("gray");
                term.WriteLine("  - Inventory (equip/unequip)");
                term.SetColor("yellow");
                term.Write("    P");
                term.SetColor("gray");
                term.WriteLine("  - Use potion (HP or Mana)");
                term.SetColor("yellow");
                term.Write("    =");
                term.SetColor("gray");
                term.WriteLine("  - Character status");
                term.SetColor("yellow");
                term.Write("    Q");
                term.SetColor("gray");
                term.WriteLine("  - Leave dungeon");
                term.SetColor("white");
                term.WriteLine("  In combat:");
                term.SetColor("yellow");
                term.Write("    A");
                term.SetColor("gray");
                term.WriteLine("  - Attack (A2=target #2)");
                term.SetColor("yellow");
                term.Write("    C");
                term.SetColor("gray");
                term.WriteLine("  - Cast best spell");
                term.SetColor("yellow");
                term.Write("    D");
                term.SetColor("gray");
                term.WriteLine("  - Defend");
                term.SetColor("yellow");
                term.Write("    I");
                term.SetColor("gray");
                term.WriteLine("  - Use potion");
                term.SetColor("yellow");
                term.Write("    H");
                term.SetColor("gray");
                term.WriteLine("  - Heal ally");
                term.SetColor("yellow");
                term.Write("    R");
                term.SetColor("gray");
                term.WriteLine("  - Retreat");
                term.SetColor("yellow");
                term.Write("    1-9");
                term.SetColor("gray");
                term.WriteLine(" - Quickbar (spells/abilities)");
                term.SetColor("white");
                term.WriteLine("  Slash commands:");
                term.SetColor("gray");
                term.WriteLine("    /party  /stats  /health  /gold  /quests");
                term.WriteLine("    /say  /tell  /who  /help");
                term.WriteLine("");
                return true;

            case "s":
            case "st":
            case "stats":
            case "status":
                term.SetColor("bright_cyan");
                term.WriteLine($"  {player.DisplayName} — Level {player.Level} {player.Race} {player.ClassName}");
                term.SetColor("white");
                term.WriteLine($"  HP: {player.HP}/{player.MaxHP}  Mana: {player.Mana}/{player.MaxMana}  Sta: {player.CurrentCombatStamina}/{player.Stamina}");
                term.SetColor("gray");
                term.WriteLine($"  STR:{player.Strength} DEF:{player.Defense} DEX:{player.Dexterity} AGI:{player.Agility}");
                term.WriteLine($"  CON:{player.Constitution} INT:{player.Intelligence} WIS:{player.Wisdom} CHA:{player.Charisma}");
                term.WriteLine($"  XP: {player.Experience:N0}  Gold: {player.Gold:N0}");
                return true;

            case "h":
            case "hp":
            case "health":
                int hpPct = player.MaxHP > 0 ? (int)(player.HP * 100 / player.MaxHP) : 0;
                int mpPct = player.MaxMana > 0 ? (int)(player.Mana * 100 / player.MaxMana) : 0;
                string hpColor = hpPct > 50 ? "green" : hpPct > 25 ? "yellow" : "red";
                term.SetColor(hpColor);
                term.WriteLine($"  HP: {player.HP}/{player.MaxHP} ({hpPct}%)");
                term.SetColor("blue");
                term.WriteLine($"  MP: {player.Mana}/{player.MaxMana} ({mpPct}%)");
                term.SetColor("yellow");
                term.WriteLine($"  Potions: HP={player.Healing}  MP={player.ManaPotions}");
                return true;

            case "g":
            case "gold":
                term.SetColor("yellow");
                term.WriteLine($"  Gold on hand: {player.Gold:N0}");
                term.SetColor("gray");
                term.WriteLine($"  Bank balance: {player.BankGold:N0}");
                return true;

            case "p":
            case "party":
            case "group":
            {
                var partyGroup = GroupSystem.Instance?.GetGroupFor(player.GroupPlayerUsername ?? player.DisplayName);
                if (partyGroup == null)
                {
                    term.SetColor("gray");
                    term.WriteLine("  You are not in a group.");
                    return true;
                }
                if (GameConfig.ScreenReaderMode)
                    term.WriteLine("GROUP STATUS", "bright_cyan");
                else
                    term.WriteLine("═══ GROUP STATUS ═══", "bright_cyan");
                // Show all members (snapshot list to avoid lock issues)
                List<string> memberSnapshot;
                lock (partyGroup.MemberUsernames)
                    memberSnapshot = new List<string>(partyGroup.MemberUsernames);
                foreach (var memberName in memberSnapshot)
                {
                    bool isLeader = string.Equals(memberName, partyGroup.LeaderUsername, StringComparison.OrdinalIgnoreCase);
                    bool isMe = string.Equals(memberName, player.GroupPlayerUsername ?? player.DisplayName, StringComparison.OrdinalIgnoreCase);
                    var memberSession = GroupSystem.GetSession(memberName);
                    var memberPlayer = memberSession?.Context?.Engine?.CurrentPlayer;
                    if (memberPlayer != null)
                    {
                        int mhpPct = memberPlayer.MaxHP > 0 ? (int)(memberPlayer.HP * 100 / memberPlayer.MaxHP) : 0;
                        string mhpColor = mhpPct > 50 ? "32" : mhpPct > 25 ? "33" : "31";
                        string prefix = isLeader
                            ? (GameConfig.ScreenReaderMode ? "*" : "\u001b[1;33m★\u001b[0m")
                            : (GameConfig.ScreenReaderMode ? "-" : "·");
                        string suffix = isMe ? " \u001b[36m(you)\u001b[0m" : "";
                        term.WriteLine($"  {prefix} \u001b[{mhpColor}m{memberPlayer.DisplayName}\u001b[0m Lv{memberPlayer.Level} {memberPlayer.ClassName} — HP:{memberPlayer.HP}/{memberPlayer.MaxHP} ({mhpPct}%) MP:{memberPlayer.Mana}/{memberPlayer.MaxMana}{suffix}");
                    }
                }
                return true;
            }

            case "q":
            case "quest":
            case "quests":
                var activeQuests = QuestSystem.GetActiveQuestsForPlayer(player.DisplayName);
                if (activeQuests == null || activeQuests.Count == 0)
                {
                    term.SetColor("gray");
                    term.WriteLine("  No active quests.");
                }
                else
                {
                    term.SetColor("bright_cyan");
                    term.WriteLine($"  Active Quests ({activeQuests.Count}):");
                    foreach (var quest in activeQuests.Take(5))
                    {
                        term.SetColor("yellow");
                        term.Write($"    - {quest.Title}");
                        term.SetColor("gray");
                        term.WriteLine($" ({quest.GetDifficultyString()})");
                    }
                    if (activeQuests.Count > 5)
                    {
                        term.SetColor("gray");
                        term.WriteLine($"    ... and {activeQuests.Count - 5} more");
                    }
                }
                return true;

            default:
                return false; // Not a game command, let MudChatSystem try
        }
    }

    /// <summary>
    /// Show a follower's equipped items and backpack on their terminal.
    /// </summary>
    private static async Task ShowFollowerInventory(Character player, TerminalEmulator term)
    {
        while (true)
        {
            term.ClearScreen();
            if (GameConfig.ScreenReaderMode)
                term.WriteLine("INVENTORY", "bright_cyan");
            else
                term.WriteLine("═══ INVENTORY ═══", "bright_cyan");
            term.WriteLine("");

            // Equipped items
            term.SetColor("white");
            term.WriteLine("  Equipped:");
            var slotNames = new (EquipmentSlot slot, string name)[]
            {
                (EquipmentSlot.MainHand, "Weapon"),
                (EquipmentSlot.OffHand, "Off-Hand"),
                (EquipmentSlot.Head, "Head"),
                (EquipmentSlot.Body, "Body"),
                (EquipmentSlot.Arms, "Arms"),
                (EquipmentSlot.Hands, "Hands"),
                (EquipmentSlot.Legs, "Legs"),
                (EquipmentSlot.Feet, "Feet"),
                (EquipmentSlot.Cloak, "Cloak"),
                (EquipmentSlot.Waist, "Waist"),
                (EquipmentSlot.Neck, "Neck"),
                (EquipmentSlot.LFinger, "L.Ring"),
                (EquipmentSlot.RFinger, "R.Ring"),
            };

            var equippedList = new List<(EquipmentSlot slot, string name, Equipment equip)>();
            foreach (var (slot, name) in slotNames)
            {
                if (player.EquippedItems.TryGetValue(slot, out var id) && id > 0)
                {
                    var equip = EquipmentDatabase.GetById(id);
                    if (equip != null)
                        equippedList.Add((slot, name, equip));
                }
            }

            if (equippedList.Count == 0)
            {
                term.SetColor("gray");
                term.WriteLine("    (nothing equipped)");
            }
            else
            {
                foreach (var (slot, name, equip) in equippedList)
                {
                    term.SetColor("gray");
                    term.Write($"    {name,-10}");
                    term.SetColor("yellow");
                    term.WriteLine($"{equip.Name}");
                }
            }

            // Backpack
            term.WriteLine("");
            term.SetColor("white");
            term.WriteLine("  Backpack:");
            if (player.Inventory.Count == 0)
            {
                term.SetColor("gray");
                term.WriteLine("    (empty)");
            }
            else
            {
                for (int i = 0; i < player.Inventory.Count; i++)
                {
                    term.SetColor("gray");
                    term.Write($"    {i + 1}. ");
                    term.SetColor("cyan");
                    term.WriteLine(player.Inventory[i].Name);
                }
            }

            term.WriteLine("");
            term.SetColor("yellow");
            term.WriteLine($"  Gold: {player.Gold:N0}    HP Potions: {player.Healing}  MP Potions: {player.ManaPotions}");

            // Show equip/unequip options
            bool hasBackpackItems = player.Inventory.Count > 0;
            bool hasEquippedItems = equippedList.Count > 0;

            if (hasBackpackItems || hasEquippedItems)
            {
                term.WriteLine("");
                term.SetColor("white");
                if (hasBackpackItems)
                    term.Write("  [#] Equip item");
                if (hasEquippedItems)
                {
                    if (hasBackpackItems) term.Write("  ");
                    term.Write("[U] Unequip");
                }
                term.WriteLine("");
            }

            term.SetColor("gray");
            term.WriteLine("  [Enter] Close");
            term.Write("  > ");
            var choice = await term.GetInput("");
            var trimChoice = choice.Trim();

            if (string.IsNullOrEmpty(trimChoice))
                break;

            // Equip from backpack by number
            if (int.TryParse(trimChoice, out int itemNum) && itemNum >= 1 && itemNum <= player.Inventory.Count)
            {
                var item = player.Inventory[itemNum - 1];
                if (!item.CanUse(player))
                {
                    term.SetColor("red");
                    term.WriteLine($"  You cannot equip {item.Name}.");
                    await Task.Delay(1500);
                    continue;
                }

                // Look up as Equipment in the database
                var knownEquip = EquipmentDatabase.GetByName(item.Name);
                if (knownEquip != null)
                {
                    // Use Character.EquipItem which handles slot detection, unequip old, etc.
                    if (player.EquipItem(knownEquip, out string equipMsg))
                    {
                        player.Inventory.RemoveAt(itemNum - 1);
                        term.SetColor("bright_green");
                        term.WriteLine($"  Equipped {knownEquip.Name}!");
                        if (!string.IsNullOrEmpty(equipMsg))
                        {
                            term.SetColor("gray");
                            term.WriteLine($"  {equipMsg}");
                        }
                    }
                    else
                    {
                        term.SetColor("red");
                        term.WriteLine($"  Cannot equip: {equipMsg}");
                    }
                }
                else
                {
                    term.SetColor("red");
                    term.WriteLine($"  {item.Name} cannot be equipped.");
                }
                await Task.Delay(1500);
                continue;
            }

            // Unequip
            if (trimChoice.Equals("U", StringComparison.OrdinalIgnoreCase) && hasEquippedItems)
            {
                term.SetColor("white");
                term.WriteLine("  Choose slot to unequip:");
                for (int i = 0; i < equippedList.Count; i++)
                {
                    term.SetColor("gray");
                    term.Write($"    {i + 1}. ");
                    term.SetColor("yellow");
                    term.Write($"{equippedList[i].name}: ");
                    term.SetColor("cyan");
                    term.WriteLine(equippedList[i].equip.Name);
                }
                term.SetColor("gray");
                term.Write("  Unequip #: ");
                var unequipChoice = await term.GetInput("");
                if (int.TryParse(unequipChoice.Trim(), out int slotNum) &&
                    slotNum >= 1 && slotNum <= equippedList.Count)
                {
                    var (uSlot, _, uEquip) = equippedList[slotNum - 1];
                    var unequipped = player.UnequipSlot(uSlot);
                    if (unequipped != null)
                    {
                        var legacyItem = player.ConvertEquipmentToLegacyItem(unequipped);
                        player.Inventory.Add(legacyItem);
                        player.RecalculateStats();

                        term.SetColor("bright_yellow");
                        term.WriteLine($"  Unequipped {unequipped.Name} to backpack.");
                    }
                    else
                    {
                        term.SetColor("red");
                        term.WriteLine("  Cannot unequip (item may be cursed).");
                    }
                    await Task.Delay(1000);
                }
                continue;
            }
        }
    }

    /// <summary>
    /// Use a healing or mana potion for a group follower.
    /// If player has both types, prompts for choice; otherwise auto-uses the available type.
    /// </summary>
    private static async Task UseFollowerPotion(Character player, TerminalEmulator term)
    {
        term.WriteLine("");
        bool hasHealPots = player.Healing > 0 && player.HP < player.MaxHP;
        bool hasManaPots = player.ManaPotions > 0 && player.Mana < player.MaxMana;

        if (!hasHealPots && !hasManaPots)
        {
            if (player.Healing <= 0 && player.ManaPotions <= 0)
            {
                term.SetColor("red");
                term.WriteLine("  You have no potions.");
            }
            else
            {
                term.SetColor("green");
                term.WriteLine("  HP and Mana are already full.");
            }
            await Task.Delay(1500);
            return;
        }

        bool useMana = false;
        if (hasHealPots && hasManaPots)
        {
            term.SetColor("white");
            term.WriteLine($"  [H] Healing potion ({player.Healing} left)  [M] Mana potion ({player.ManaPotions} left)");
            term.SetColor("gray");
            term.Write("  Choose: ");
            var choice = await term.GetInput("");
            useMana = choice.Trim().Equals("M", StringComparison.OrdinalIgnoreCase);
        }
        else if (hasManaPots)
        {
            useMana = true;
        }

        if (useMana)
        {
            long oldMana = player.Mana;
            int manaAmount = 20 + player.Level * 3 + Random.Shared.Next(5, 21);
            player.Mana = Math.Min(player.MaxMana, player.Mana + manaAmount);
            long actualMana = player.Mana - oldMana;
            player.ManaPotions--;

            term.SetColor("bright_blue");
            term.WriteLine($"  You drink a mana potion and recover {actualMana:N0} MP!");
            term.SetColor("cyan");
            term.WriteLine($"  Mana: {player.Mana}/{player.MaxMana}    Mana Potions: {player.ManaPotions}");
        }
        else
        {
            long oldHP = player.HP;
            int healAmount = 30 + player.Level * 5 + Random.Shared.Next(10, 31);
            player.HP = Math.Min(player.MaxHP, player.HP + healAmount);
            long actualHeal = player.HP - oldHP;
            player.Healing--;
            player.Statistics.RecordPotionUsed(actualHeal);

            term.SetColor("bright_green");
            term.WriteLine($"  You drink a healing potion and recover {actualHeal:N0} HP!");
            term.SetColor("cyan");
            term.WriteLine($"  HP: {player.HP}/{player.MaxHP}    Potions: {player.Healing}/{player.MaxPotions}");
        }
        await Task.Delay(1500);
    }

    /// <summary>
    /// Show a follower's character status on their terminal.
    /// </summary>
    private static async Task ShowFollowerStatus(Character player, TerminalEmulator term)
    {
        term.ClearScreen();
        if (GameConfig.ScreenReaderMode)
            term.WriteLine("CHARACTER STATUS", "bright_cyan");
        else
            term.WriteLine("═══ CHARACTER STATUS ═══", "bright_cyan");
        term.WriteLine("");

        term.SetColor("white");
        term.WriteLine($"  {player.DisplayName}");
        term.SetColor("gray");
        term.WriteLine($"  Level {player.Level} {player.Race} {player.ClassName}");
        term.WriteLine("");

        // HP bar
        int hpPct = player.MaxHP > 0 ? (int)(player.HP * 20 / player.MaxHP) : 0;
        hpPct = Math.Clamp(hpPct, 0, 20);
        string hpBar = new string('#', hpPct) + new string('.', 20 - hpPct);
        term.SetColor("white");
        term.Write("  HP:   ");
        term.SetColor("red");
        term.Write($"[{hpBar}]");
        term.SetColor("white");
        term.WriteLine($" {player.HP}/{player.MaxHP}");

        // MP bar
        int mpPct = player.MaxMana > 0 ? (int)(player.Mana * 20 / player.MaxMana) : 0;
        mpPct = Math.Clamp(mpPct, 0, 20);
        string mpBar = new string('#', mpPct) + new string('.', 20 - mpPct);
        term.Write("  Mana: ");
        term.SetColor("blue");
        term.Write($"[{mpBar}]");
        term.SetColor("white");
        term.WriteLine($" {player.Mana}/{player.MaxMana}");

        // Stamina bar
        int stPct = player.MaxCombatStamina > 0 ? (int)(player.CurrentCombatStamina * 20 / player.MaxCombatStamina) : 0;
        stPct = Math.Clamp(stPct, 0, 20);
        string stBar = new string('#', stPct) + new string('.', 20 - stPct);
        term.Write("  Stam: ");
        term.SetColor("green");
        term.Write($"[{stBar}]");
        term.SetColor("white");
        term.WriteLine($" {player.CurrentCombatStamina}/{player.MaxCombatStamina}");

        term.WriteLine("");
        term.SetColor("gray");
        term.WriteLine($"  STR: {player.Strength,-5} DEF: {player.Defence,-5} DEX: {player.Dexterity,-5} AGI: {player.Agility}");
        term.WriteLine($"  CON: {player.Constitution,-5} INT: {player.Intelligence,-5} WIS: {player.Wisdom,-5} CHA: {player.Charisma}");
        term.WriteLine("");
        term.SetColor("yellow");
        term.WriteLine($"  Gold: {player.Gold:N0}    XP: {player.Experience:N0}");
        term.SetColor("green");
        term.WriteLine($"  Potions: {player.Healing}/{player.MaxPotions}");
        term.WriteLine("");
        await term.PressAnyKey();
    }

    /// <summary>
    /// Clean up when a group follower exits the dungeon (voluntarily, disconnect, or group disband).
    /// </summary>
    private void CleanupGroupFollower(
        Character player, TerminalEmulator term,
        PlayerSession mySession, PlayerSession leaderSession,
        DungeonLocation? leaderDungeon)
    {
        // Remove from leader's teammates
        if (leaderDungeon != null)
        {
            lock (leaderDungeon.teammates)
            {
                leaderDungeon.teammates.Remove(player);
            }
        }

        // Clear group follower state
        player.RemoteTerminal = null;
        player.GroupPlayerUsername = null;
        player.CombatInputChannel = null;
        player.IsAwaitingCombatInput = false;
        mySession.IsGroupFollower = false;
        mySession.GroupLeaderSession = null;

        // Notify leader (follower left the dungeon, not necessarily the group)
        leaderSession.EnqueueMessage(
            $"\u001b[1;33m  * {mySession.Username} has left the dungeon.\u001b[0m");
    }

    /// <summary>
    /// Clean up group state when the leader exits the dungeon.
    /// Returns followers to town but keeps the group intact for regrouping.
    /// </summary>
    private void CleanupGroupDungeonOnLeaderExit(Character player)
    {
        var ctx = SessionContext.Current;
        if (ctx == null) return;

        var group = GroupSystem.Instance?.GetGroupFor(ctx.Username);
        if (group == null || !group.IsLeader(ctx.Username)) return;

        if (group.IsInDungeon)
        {
            // Snapshot grouped followers before removing from teammates
            List<Character> groupedFollowers;
            lock (teammates)
            {
                groupedFollowers = teammates.Where(t => t.IsGroupedPlayer).ToList();
                teammates.RemoveAll(t => t.IsGroupedPlayer);
            }

            group.IsInDungeon = false;

            // Signal each follower to exit the dungeon (breaks their GroupFollowerLoop)
            // but do NOT disband the group — they stay grouped for regrouping later
            foreach (var follower in groupedFollowers)
            {
                var followerSession = GroupSystem.GetSession(follower.GroupPlayerUsername ?? "");
                if (followerSession != null)
                {
                    followerSession.IsGroupFollower = false; // breaks the while loop
                    followerSession.EnqueueMessage(
                        "\u001b[1;33m  * The leader has left the dungeon. Returning to town...\u001b[0m");
                }
            }

            // Notify group (including non-dungeon members)
            GroupSystem.Instance?.NotifyGroup(group,
                $"\u001b[1;33m  * {ctx.Username} has left the dungeon. The dungeon run is over.\u001b[0m",
                excludeUsername: ctx.Username);
        }
    }

    #endregion
}

/// <summary>
/// Dungeon terrain types affecting encounters
/// </summary>
public enum DungeonTerrain
{
    Underground,
    Mountains, 
    Desert,
    Forest,
    Caves
} 
