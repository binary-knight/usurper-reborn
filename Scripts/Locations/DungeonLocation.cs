using UsurperRemake.Utils;
using UsurperRemake.Systems;
using UsurperRemake.Data;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;

/// <summary>
/// Dungeon Location - Room-based exploration with atmosphere and tension
/// Features: Procedural floors, room navigation, feature interaction, combat, events
/// </summary>
public class DungeonLocation : BaseLocation
{
    private List<Character> teammates = new();
    private int currentDungeonLevel = 1;
    private int maxDungeonLevel = 100;
    private Random dungeonRandom = new Random();
    private DungeonTerrain currentTerrain = DungeonTerrain.Underground;

    // Legacy encounter chances (for old ExploreLevel fallback)
    private const float MonsterEncounterChance = 0.90f;
    private const float SpecialEventChance = 0.10f;

    // Current floor state
    private DungeonFloor currentFloor;
    private bool inRoomMode = false; // Are we exploring a room?

    // Player state tracking for tension
    private int consecutiveMonsterRooms = 0;
    private int roomsExploredThisFloor = 0;
    private bool hasRestThisFloor = false;

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
        currentDungeonLevel = GetMaxAccessibleFloor(player, currentDungeonLevel);

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
            hasRestThisFloor = false;

            // Track dungeon exploration statistics
            player.Statistics.RecordDungeonLevel(currentDungeonLevel);

            // Track archetype - Explorer for dungeon exploration
            UsurperRemake.Systems.ArchetypeTracker.Instance.RecordDungeonExploration(currentDungeonLevel);

            // Show dramatic dungeon entrance art
            term.ClearScreen();
            await UsurperRemake.UI.ANSIArt.DisplayArtAnimated(term, UsurperRemake.UI.ANSIArt.DungeonEntrance, 40);
            term.WriteLine("");
            term.SetColor("cyan");
            term.WriteLine($"  Floor {currentDungeonLevel} - {currentFloor.Theme}");
            term.SetColor("gray");
            term.WriteLine($"  {GetThemeDescription(currentFloor.Theme)}");

            // Show persistence status
            if (wasRestored && !didRespawn)
            {
                term.SetColor("bright_green");
                term.WriteLine("  [Continuing where you left off...]");
            }
            else if (didRespawn)
            {
                term.SetColor("yellow");
                term.WriteLine("  [The dungeon's dark magic has drawn new creatures from the depths...]");
            }

            term.WriteLine("");
            term.SetColor("darkgray");
            term.Write("  Press Enter to continue...");
            await term.ReadKeyAsync();
        }

        // Refresh bounty board quests based on player level
        QuestSystem.RefreshBountyBoard(player?.Level ?? 1);

        // Check for story events at milestone floors
        await CheckFloorStoryEvents(player, term);

        // Add active companions to the teammates list
        await AddCompanionsToParty(player, term);

        // Restore NPC teammates (spouses, team members, lovers) from saved state
        await RestoreNPCTeammates(term);

        // Restore player echoes (cooperative dungeon allies) from saved state
        await RestorePlayerTeammates(term);

        // Check for dungeon entry fees for overleveled teammates
        // Player can always enter - unaffordable allies simply stay behind
        await CheckAndPayEntryFees(player, term);

        // Show contextual hint for new players on their first dungeon entry
        HintSystem.Instance.TryShowHint(HintSystem.HINT_FIRST_DUNGEON, term, player.HintsShown);

        // Call base to enter the location loop
        await base.EnterLocation(player, term);
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
                term.WriteLine($"[!] This floor contains a Seal of Truth. Explore carefully.");
                term.WriteLine($"    Seal-discovery rooms available: {sealRoomCount}");
                term.SetColor("white");
            }
            else
            {
                term.SetColor("gray");
                term.WriteLine($"[i] The seal on this floor has already been collected.");
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

        if (!story.HasStoryFlag(floorVisitedFlag))
        {
            story.SetStoryFlag(floorVisitedFlag, true);

            // Story milestone events
            await TriggerFloorStoryEvent(player, term);
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
                await ShowStoryMoment(term, "The Depths Begin",
                    new[] {
                        "As you descend to level 10, the air grows heavier.",
                        "The walls here are older, carved by hands that predate memory.",
                        "",
                        "You notice strange symbols repeated throughout - seven interlocking circles.",
                        "A pattern that tugs at something deep within you...",
                        "",
                        "Somewhere ahead, answers await."
                    }, "cyan");
                break;

            case 15:
                await ShowStoryMoment(term, "Ancient Battlefield",
                    new[] {
                        "The stones here are stained with something old.",
                        "Not rust. Not mortal blood. Something... golden.",
                        "",
                        "Weapons of impossible design litter the ground.",
                        "Too large for human hands. Too heavy for mortal arms.",
                        "",
                        "Something terrible happened here, long before history began.",
                        "",
                        "A SEAL OF TRUTH lies hidden on this floor.",
                        "Seek a shrine, library, or sacred chamber to find it."
                    }, "dark_red");
                StoryProgressionSystem.Instance.SetStoryFlag("knows_about_seals", true);
                break;

            case 25:
                await ShowStoryMoment(term, "The Whispers Begin",
                    new[] {
                        "You hear it now - a voice at the edge of perception.",
                        "Not speaking to you. Speaking AS you.",
                        "",
                        "\"Why do you descend, wave?\"",
                        "\"What do you seek in the deep?\"",
                        "",
                        "The voice knows your true name.",
                        "The name you have forgotten."
                    }, "bright_cyan");
                StoryProgressionSystem.Instance.SetStoryFlag("heard_whispers", true);
                // Moral Paradox: The Possessed Child
                if (MoralParadoxSystem.Instance.IsParadoxAvailable("possessed_child", player))
                {
                    await MoralParadoxSystem.Instance.PresentParadox("possessed_child", player, term);
                }
                break;

            case 30:
                await ShowStoryMoment(term, "Corrupted Shrine",
                    new[] {
                        "Seven statues stand in a circle, faces worn by time.",
                        "But something is wrong with them.",
                        "",
                        "The stone itself seems... sick. Twisted.",
                        "As if the sculptures were changed after they were made.",
                        "Beautiful forms warped into something grotesque.",
                        "",
                        "What force could corrupt stone itself?",
                        "",
                        "A SEAL OF TRUTH awaits those who seek answers here."
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
                        term.WriteLine("You feel a familiar warmth as you step onto this floor...");
                        await Task.Delay(1500);
                        term.SetColor("bright_cyan");
                        term.WriteLine("Veloura senses your return - and what you carry.");
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
                await ShowStoryMoment(term, "Prison of Ages",
                    new[] {
                        "Empty cells stretch into darkness.",
                        "Not prison cells for mortals - these are vast. Cathedral-sized.",
                        "",
                        "The bars are made of something that isn't metal.",
                        "Something that hums with power even now.",
                        "",
                        "Whatever was kept here was immense. And angry.",
                        "Claw marks scar the walls, deeper than any blade could cut.",
                        "",
                        "A SEAL OF TRUTH holds the answer to who was imprisoned here."
                    }, "gray");
                StoryProgressionSystem.Instance.AdvanceChapter(StoryChapter.TheWhispers);
                break;

            case 60:
                await ShowStoryMoment(term, "Oracle's Tomb",
                    new[] {
                        "A skeleton sits upon a throne of bone.",
                        "In death, she still clutches a crystal orb.",
                        "",
                        "The Oracle. The last mortal to speak with the gods.",
                        "She saw the future before she died.",
                        "And what she saw made her weep.",
                        "",
                        "Words are carved into the stone at her feet:",
                        "\"The Seal of Fate reveals what I could not speak.\"",
                        "",
                        "A SEAL OF TRUTH awaits. It holds her final prophecy."
                    }, "bright_cyan");
                StoryProgressionSystem.Instance.SetStoryFlag("knows_prophecy", true);

                // MAELKETH ENCOUNTER - First Old God boss
                if (OldGodBossSystem.Instance.CanEncounterBoss(player, OldGodType.Maelketh))
                {
                    term.WriteLine("");
                    term.WriteLine("The ground trembles. An ancient presence stirs...", "bright_red");
                    await Task.Delay(2000);

                    term.WriteLine("Do you wish to face Maelketh, the Broken Blade? (Y/N)", "yellow");
                    var response = await term.GetInput("> ");
                    if (response.Trim().ToUpper().StartsWith("Y"))
                    {
                        var result = await OldGodBossSystem.Instance.StartBossEncounter(player, OldGodType.Maelketh, term, teammates);
                        await HandleGodEncounterResult(result, player, term);
                    }
                    else
                    {
                        term.WriteLine("You sense the god retreating back into slumber... for now.", "gray");
                    }
                }
                break;

            case 65:
                // Soulweaver's Loom discovery - triggered by Veloura's save quest
                if (StoryProgressionSystem.Instance.HasStoryFlag("veloura_save_quest") &&
                    !ArtifactSystem.Instance.HasArtifact(ArtifactType.SoulweaversLoom))
                {
                    await ShowStoryMoment(term, "The Soulweaver's Chamber",
                        new[] {
                            "Deep within this forgotten chamber, something ancient calls to you...",
                            "A device of impossible delicacy sits upon a stone pedestal.",
                            "Threads of light drift from it like spider silk in a breeze.",
                            "",
                            "This is the Soulweaver's Loom - the artifact that can undo divine corruption.",
                            "The very thing you promised to find.",
                        }, "bright_magenta");

                    // Grant the artifact
                    await ArtifactSystem.Instance.CollectArtifact(player, ArtifactType.SoulweaversLoom, term);

                    term.WriteLine("");
                    term.SetColor("bright_yellow");
                    term.WriteLine("  You remember your promise to Veloura on floor 40.");
                    term.WriteLine("  Return to her with the Loom to complete the save.", "yellow");
                }

                // Moral Paradox: Veloura's Cure (deeper choice about the Loom's cost)
                if (MoralParadoxSystem.Instance.IsParadoxAvailable("velouras_cure", player))
                {
                    term.WriteLine("");
                    await ShowStoryMoment(term, "The Soulweaver's Price",
                        new[] {
                            "The Soulweaver's Loom pulses with power in your hands.",
                            "And before you stands someone you never expected to see again...",
                        }, "bright_magenta");
                    await MoralParadoxSystem.Instance.PresentParadox("velouras_cure", player, term);
                }
                break;

            case 75:
                await ShowStoryMoment(term, "Chamber of Echoes",
                    new[] {
                        "You see yourself in the walls.",
                        "Not a reflection - a memory.",
                        "",
                        "You have been here before.",
                        "A thousand times. A million.",
                        "",
                        "The cycle repeats.",
                        "Manwe sends a fragment of himself to experience mortality.",
                        "To learn. To grow. To remember what it means to be small.",
                        "",
                        "And at the end, the fragment returns...",
                        "Unless this time, it doesn't."
                    }, "bright_magenta");
                AmnesiaSystem.Instance?.RecoverMemory(MemoryFragment.TheDecision);
                break;

            case 80:
                await ShowStoryMoment(term, "Chamber of Mourning",
                    new[] {
                        "The walls here are made of crystal.",
                        "Blue-grey. Cold. And wet.",
                        "",
                        "Not water. Something thicker. Saltier.",
                        "As if these crystals formed from tears.",
                        "",
                        "Oceans of tears, shed over millennia.",
                        "Frozen in stone. A monument to sorrow.",
                        "",
                        "Whose grief could fill a mountain?",
                        "",
                        "The SEAL OF TRUTH on this floor holds the answer."
                    }, "bright_blue");
                // Moral Paradox: Free Terravok (alternative to combat)
                if (MoralParadoxSystem.Instance.IsParadoxAvailable("free_terravok", player))
                {
                    await MoralParadoxSystem.Instance.PresentParadox("free_terravok", player, term);
                }
                // TERRAVOK ENCOUNTER - God of Earth
                else if (OldGodBossSystem.Instance.CanEncounterBoss(player, OldGodType.Terravok))
                {
                    term.WriteLine("");
                    term.WriteLine("The mountain itself seems to breathe. Stone shifts like flesh.", "yellow");
                    await Task.Delay(2000);

                    term.WriteLine("Terravok, the Worldbreaker, senses your presence.", "bright_yellow");
                    term.WriteLine("Do you wish to wake the Sleeping Mountain? (Y/N)", "yellow");
                    var response = await term.GetInput("> ");
                    if (response.Trim().ToUpper().StartsWith("Y"))
                    {
                        var result = await OldGodBossSystem.Instance.StartBossEncounter(player, OldGodType.Terravok, term, teammates);
                        await HandleGodEncounterResult(result, player, term);
                    }
                    else
                    {
                        term.WriteLine("The mountain settles. Terravok slumbers on.", "gray");
                    }
                }
                break;

            case 95:
                // Moral Paradox: Destroy Darkness (requires Sunforged Blade)
                if (MoralParadoxSystem.Instance.IsParadoxAvailable("destroy_darkness", player))
                {
                    await ShowStoryMoment(term, "The Purging Light",
                        new[] {
                            "Aurelion stands before you, radiant with divine light.",
                            "The Sunforged Blade burns bright in your hands.",
                            "",
                            "'You have earned this moment,' the God of Light speaks.",
                            "'I offer you the power to end all darkness forever.'"
                        }, "bright_yellow");
                    await MoralParadoxSystem.Instance.PresentParadox("destroy_darkness", player, term);
                }
                break;

            case 99:
                await ShowStoryMoment(term, "The Threshold",
                    new[] {
                        "You stand at the edge of understanding.",
                        "One floor above, Manwe waits.",
                        "",
                        "The Creator. Your creator.",
                        "Or perhaps... yourself?",
                        "",
                        "You are the wave that remembered it was the ocean.",
                        "And now the ocean must answer for what it has done.",
                        "",
                        "Collect the final seal if you have not.",
                        "Then face the truth."
                    }, "white");
                break;

            case 100:
                await ShowStoryMoment(term, "The End of All Things",
                    new[] {
                        "This is it.",
                        "",
                        "Beyond this threshold, Manwe dreams.",
                        "The Creator who made everything.",
                        "The god who broke his own children.",
                        "The ocean that forgot it was water.",
                        "",
                        "You have three choices:",
                        "  - DESTROY him and take his power (Usurper)",
                        "  - SAVE him and restore the gods (Savior)",
                        "  - DEFY him and forge your own path (Defiant)",
                        "",
                        "Or, if you collected all Seven Seals...",
                        "Perhaps something else is possible.",
                        "",
                        "Choose wisely. This is the only choice that matters."
                    }, "bright_yellow");
                StoryProgressionSystem.Instance.AdvanceChapter(StoryChapter.FinalConfrontation);

                // MANWE ENCOUNTER - The Creator, Final Boss
                if (OldGodBossSystem.Instance.CanEncounterBoss(player, OldGodType.Manwe))
                {
                    term.WriteLine("");
                    term.WriteLine("The dream trembles. The Dreamer stirs.", "white");
                    await Task.Delay(2000);

                    term.WriteLine("Manwe, the Creator of All, awaits your judgment.", "bright_white");
                    term.WriteLine("");
                    term.WriteLine("This is the final confrontation. Are you ready? (Y/N)", "bright_yellow");
                    var response = await term.GetInput("> ");
                    if (response.Trim().ToUpper().StartsWith("Y"))
                    {
                        var result = await OldGodBossSystem.Instance.StartBossEncounter(player, OldGodType.Manwe, term, teammates);
                        await HandleGodEncounterResult(result, player, term);

                        // After Manwe, trigger ending determination
                        if (result.Outcome != BossOutcome.Fled)
                        {
                            var endingType = EndingsSystem.Instance.DetermineEnding(player);
                            await ShowEnding(endingType, player, term);
                        }
                    }
                    else
                    {
                        term.WriteLine("You hesitate at the threshold. The Creator can wait.", "gray");
                        term.WriteLine("But not forever...", "dark_gray");
                    }
                }
                // Fallback: Moral Paradox if boss not available
                else if (MoralParadoxSystem.Instance.IsParadoxAvailable("final_choice", player))
                {
                    var choice = await MoralParadoxSystem.Instance.PresentParadox("final_choice", player, term);

                    // Trigger the actual ending based on the choice made
                    if (choice != null)
                    {
                        EndingType endingType;
                        if (choice.OptionId == "claim_power")
                            endingType = EndingType.Usurper;
                        else if (choice.OptionId == "refuse_power")
                            endingType = EndingType.Defiant;
                        else if (choice.OptionId == "remember_truth")
                            endingType = EndingType.TrueEnding;
                        else
                            endingType = EndingsSystem.Instance.DetermineEnding(player);

                        await ShowEnding(endingType, player, term);
                    }
                }
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

        // Add companions to teammates list
        foreach (var companion in companionCharacters)
        {
            if (companion.IsAlive)
            {
                teammates.Add(companion);
            }
        }

        // Show companion status if any are present
        if (companionCharacters.Count > 0)
        {
            term.WriteLine("");
            term.SetColor("bright_cyan");
            term.WriteLine("═══ YOUR COMPANIONS ═══");
            foreach (var companion in companionCharacters)
            {
                var companionData = companionSystem.GetCompanion(companion.CompanionId!.Value);
                term.SetColor("white");
                term.Write($"  {companion.DisplayName}");
                term.SetColor("gray");
                term.Write($" ({companionData?.CombatRole}) ");
                term.SetColor(companion.HP > companion.MaxHP / 2 ? "green" : "yellow");
                term.WriteLine($"HP: {companion.HP}/{companion.MaxHP}");
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
                teammates.Add(npc);
                npc.UpdateLocation("Dungeon");
                restoredCount++;
            }
        }

        if (restoredCount > 0)
        {
            term.WriteLine("");
            term.SetColor("bright_cyan");
            term.WriteLine($"═══ PARTY RESTORED ═══");
            term.SetColor("green");
            term.WriteLine($"{restoredCount} ally/allies rejoin your dungeon party from your last session.");
            term.WriteLine("");
            await Task.Delay(1500);
        }

        // Notify if some allies couldn't join due to party cap
        if (skippedCount > 0)
        {
            term.SetColor("yellow");
            term.WriteLine($"{skippedCount} ally/allies couldn't rejoin - party is full (max {maxPartySize}).");
            term.WriteLine("Use Party Management to adjust your party composition.");
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
                        term.WriteLine($"  {name} is no longer on your team.");
                        continue;
                    }
                }

                // Create echo character
                var ally = PlayerCharacterLoader.CreateFromSaveData(saveData.Player, name, isEcho: true);
                teammates.Add(ally);
                restoredCount++;

                term.SetColor("bright_cyan");
                term.WriteLine($"  {name}'s echo materializes beside you!");
            }
            catch (Exception ex)
            {
                DebugLogger.Instance.LogError("DUNGEON", $"Failed to load player echo '{name}': {ex.Message}");
            }
        }

        if (restoredCount > 0)
        {
            term.SetColor("gray");
            term.WriteLine("  Player echoes fight as AI allies with their real stats.");
            term.WriteLine("");
            await Task.Delay(1500);
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
            term.WriteLine($"You need {totalFee:N0} gold but only have {player.Gold:N0}!");
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
                term.WriteLine("These allies demand more gold than you have:");
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
                var payChoice = await term.GetInput($"Pay {affordableFee:N0} gold for allies you can afford? (Y/N): ");

                if (payChoice.ToUpper().StartsWith("Y"))
                {
                    player.Gold -= affordableFee;
                    term.SetColor("green");
                    term.WriteLine($"Paid {affordableFee:N0} gold.");
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
                term.WriteLine("Staying behind:");
                foreach (var (npc, _) in unaffordableTeammates)
                {
                    term.WriteLine($"  {npc.Name} waits at the entrance.", "darkgray");
                }
            }

            SyncNPCTeammatesToGameEngine();
            await Task.Delay(1500);
            return true; // Allow entry with whoever player can afford
        }

        // Player can afford all fees - ask for confirmation
        term.SetColor("cyan");
        var confirm = await term.GetInput($"Pay {totalFee:N0} gold to bring your allies? (Y/N): ");

        if (confirm.ToUpper().StartsWith("Y"))
        {
            player.Gold -= totalFee;
            term.SetColor("green");
            term.WriteLine($"Paid {totalFee:N0} gold. Your allies prepare for the dungeon.");
            term.SetColor("gray");
            term.WriteLine($"Remaining gold: {player.Gold:N0}");
            await Task.Delay(1000);
            return true;
        }
        else
        {
            term.SetColor("gray");
            term.WriteLine("You decide not to pay. Your allies won't join you this time.");

            // Remove overleveled NPCs from party
            var breakdown = balanceSystem.GetFeeBreakdown(player, teammates);
            foreach (var (npc, fee, _) in breakdown.Where(b => b.fee > 0))
            {
                teammates.Remove(npc);
                term.WriteLine($"  {npc.Name} stays behind.", "darkgray");
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
        term.WriteLine($"╔{'═'.ToString().PadRight(58, '═')}╗");
        term.WriteLine($"║  {title.PadRight(55)} ║");
        term.WriteLine($"╚{'═'.ToString().PadRight(58, '═')}╝");
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
        await term.GetInputAsync("  Press Enter to continue...");
    }

    /// <summary>
    /// Display a scripted Stranger encounter in the dungeon (PreFloor70 or others).
    /// Similar to BaseLocation's version but adapted for dungeon context.
    /// </summary>
    private async Task DisplayScriptedStrangerEncounterInDungeon(
        UsurperRemake.Systems.ScriptedStrangerEncounter encounter, Character player, TerminalEmulator term)
    {
        term.ClearScreen();
        term.SetColor("dark_magenta");
        term.WriteLine("");
        term.WriteLine("╔══════════════════════════════════════════════════════════════════════════════╗");

        string titlePadded = encounter.Title.PadLeft((78 + encounter.Title.Length) / 2).PadRight(78);
        term.Write("║");
        term.SetColor("bright_magenta");
        term.Write(titlePadded);
        term.SetColor("dark_magenta");
        term.WriteLine("║");
        term.WriteLine("╚══════════════════════════════════════════════════════════════════════════════╝");
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
            term.WriteLine("  How do you respond?");
            term.WriteLine("");

            foreach (var opt in encounter.Responses)
            {
                term.SetColor("yellow");
                term.Write($"    [{opt.Key}] ");
                term.SetColor("white");
                term.WriteLine(opt.Text);
            }

            term.WriteLine("");
            var choice = await term.GetInput("Your response: ");

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
                term.WriteLine("The Old God has fallen. Their power flows into you.", "bright_yellow");

                // Grant artifact based on god defeated
                var artifactType = GetArtifactForGod(result.God);
                if (artifactType.HasValue)
                {
                    term.WriteLine($"You obtained: {artifactType.Value}!", "bright_magenta");
                    StoryProgressionSystem.Instance.CollectedArtifacts.Add(artifactType.Value);
                }

                // XP and gold reward
                if (result.XPGained > 0)
                {
                    player.Experience += result.XPGained;
                    term.WriteLine($"Experience gained: {result.XPGained}", "green");
                }
                if (result.GoldGained > 0)
                {
                    player.Gold += result.GoldGained;
                    term.WriteLine($"Gold gained: {result.GoldGained}", "yellow");
                }

                // Chivalry impact
                player.Darkness += 100;
                term.WriteLine("Your darkness deepens. You are becoming the Usurper.", "red");

                // Check achievements
                AchievementSystem.CheckAchievements(player);
                await AchievementSystem.ShowPendingNotifications(term);

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
                term.WriteLine("The Old God's corruption lifts. They remember who they were.", "bright_cyan");
                term.WriteLine("A fragment of divine gratitude fills your heart.", "white");

                player.Chivalry += 150;
                term.WriteLine("Your chivalry grows. You are becoming the Savior.", "bright_green");

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
                term.WriteLine("The Old God sees something in you. An understanding.", "bright_magenta");
                term.WriteLine("You have forged an alliance beyond mortal comprehension.", "white");

                player.Chivalry += 50;
                player.Wisdom += 2;
                break;

            case BossOutcome.Spared:
                term.WriteLine("");
                term.WriteLine("The Old God's eyes widen with something long forgotten - hope.", "bright_cyan");
                term.WriteLine("They fade from this place, but their presence lingers in your heart.", "white");
                term.WriteLine("");
                term.SetColor("bright_magenta");
                term.WriteLine("You have promised to find the Soulweaver's Loom and return.");
                term.WriteLine("The artifact lies somewhere deeper in the dungeon...", "yellow");
                term.WriteLine("");

                player.Chivalry += 75;
                term.WriteLine("+75 Chivalry - Your mercy speaks louder than any blade.", "bright_green");
                term.SetColor("bright_yellow");
                term.WriteLine("");
                term.WriteLine("  [QUEST STARTED] Find the Soulweaver's Loom and return to save this god.");

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
                term.WriteLine("You retreat from the divine presence.", "gray");
                term.WriteLine("The Old God watches you go. They can wait.", "dark_gray");
                break;
        }

        await Task.Delay(3000);
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

    /// <summary>
    /// Display the ending sequence based on ending type
    /// </summary>
    private async Task ShowEnding(EndingType ending, Character player, TerminalEmulator term)
    {
        term.ClearScreen();
        term.WriteLine("");

        string color = ending switch
        {
            EndingType.Usurper => "bright_red",
            EndingType.Savior => "bright_green",
            EndingType.Defiant => "bright_yellow",
            EndingType.TrueEnding => "bright_cyan",
            EndingType.Secret => "bright_magenta",
            _ => "white"
        };

        string title = ending switch
        {
            EndingType.Usurper => "THE USURPER",
            EndingType.Savior => "THE SAVIOR",
            EndingType.Defiant => "THE DEFIANT",
            EndingType.TrueEnding => "THE AWAKENED",
            EndingType.Secret => "DISSOLUTION",
            _ => "THE END"
        };

        term.SetColor(color);
        term.WriteLine("╔══════════════════════════════════════════════════════════════════╗");
        term.WriteLine($"║                         {title.PadRight(35)}  ║");
        term.WriteLine("╚══════════════════════════════════════════════════════════════════╝");
        term.WriteLine("");

        await Task.Delay(2000);

        // Show ending text based on type
        switch (ending)
        {
            case EndingType.Usurper:
                await ShowUsurperEnding(term);
                break;
            case EndingType.Savior:
                await ShowSaviorEnding(term);
                break;
            case EndingType.Defiant:
                await ShowDefiantEnding(term);
                break;
            case EndingType.TrueEnding:
                await ShowTrueEnding(term);
                break;
            case EndingType.Secret:
                await ShowSecretEnding(term);
                break;
        }

        // Mark the ending
        StoryProgressionSystem.Instance.SetStoryFlag($"completed_{ending.ToString().ToLower()}", true);

        term.WriteLine("");
        term.SetColor("gray");
        term.WriteLine("╔══════════════════════════════════════════════════════════════════╗");
        term.WriteLine("║                    CONGRATULATIONS                               ║");
        term.WriteLine("║                                                                  ║");
        term.WriteLine("║        You have completed Usurper Reborn.                       ║");
        term.WriteLine("║                                                                  ║");
        term.WriteLine("║        Your journey is recorded in the annals of time.          ║");
        term.WriteLine("╚══════════════════════════════════════════════════════════════════╝");
        term.WriteLine("");

        await term.GetInputAsync("Press Enter to continue...");
    }

    private async Task ShowUsurperEnding(TerminalEmulator term)
    {
        string[] lines = {
            "You stand where Manwe once stood.",
            "The power of creation flows through you.",
            "",
            "The Old Gods bow to your will.",
            "The world trembles at your command.",
            "",
            "You have become what you were sent to destroy.",
            "The Usurper. The new Creator.",
            "",
            "But power, you discover, is a cold companion.",
            "And eternity stretches before you, empty and vast.",
            "",
            "Was it worth it?",
            "Only you can answer that now."
        };

        foreach (var line in lines)
        {
            term.SetColor(string.IsNullOrEmpty(line) ? "white" : "red");
            term.WriteLine($"  {line}");
            await Task.Delay(300);
        }
    }

    private async Task ShowSaviorEnding(TerminalEmulator term)
    {
        string[] lines = {
            "The corruption lifts from the world.",
            "The Old Gods remember who they were.",
            "",
            "Maelketh becomes the god of honorable combat once more.",
            "Veloura's love heals instead of destroying.",
            "Thorgrim judges with wisdom instead of tyranny.",
            "",
            "And you... you return to mortality.",
            "But not alone.",
            "",
            "Songs are sung of the one who saved the gods.",
            "Children speak your name with wonder.",
            "",
            "When you finally rest, paradise awaits.",
            "The gods remember their Savior."
        };

        foreach (var line in lines)
        {
            term.SetColor(string.IsNullOrEmpty(line) ? "white" : "bright_green");
            term.WriteLine($"  {line}");
            await Task.Delay(300);
        }
    }

    private async Task ShowDefiantEnding(TerminalEmulator term)
    {
        string[] lines = {
            "You reject them all.",
            "Manwe. The Old Gods. The cycles. The power.",
            "",
            "\"I am not your pawn,\" you declare.",
            "\"I am not your child. I am not your successor.\"",
            "",
            "\"I am myself. Nothing more. Nothing less.\"",
            "",
            "And with that, you walk away.",
            "Into the sunrise. Into the unknown.",
            "",
            "Behind you, the gods become mortal.",
            "Equals now, instead of masters.",
            "",
            "Freedom, you realize, is the greatest power of all."
        };

        foreach (var line in lines)
        {
            term.SetColor(string.IsNullOrEmpty(line) ? "white" : "bright_yellow");
            term.WriteLine($"  {line}");
            await Task.Delay(300);
        }
    }

    private async Task ShowTrueEnding(TerminalEmulator term)
    {
        string[] lines = {
            "You remember now.",
            "Not just who you were. What you are.",
            "",
            "You are the wave that remembered it was the ocean.",
            "You are the dream that woke within the Dreamer.",
            "",
            "The Seven Seals unlock the final truth:",
            "You and Manwe are one. Always were.",
            "",
            "Not god, not mortal, but something new.",
            "Something that can bridge the gap.",
            "",
            "The cycle doesn't end. It transcends.",
            "And you carry the best of both within you.",
            "",
            "This is not an ending.",
            "It is a new beginning."
        };

        foreach (var line in lines)
        {
            term.SetColor(string.IsNullOrEmpty(line) ? "white" : "bright_cyan");
            term.WriteLine($"  {line}");
            await Task.Delay(300);
        }
    }

    private async Task ShowSecretEnding(TerminalEmulator term)
    {
        string[] lines = {
            "Three cycles. Three lifetimes.",
            "Each time, a little more remembered.",
            "Each time, the boundaries grew thinner.",
            "",
            "And now...",
            "",
            "The wave dissolves.",
            "The ocean remembers.",
            "",
            "You are Manwe.",
            "You always were.",
            "",
            "And as the dream ends, you wake.",
            "Not as a fragment, but as the whole.",
            "",
            "The cycle is complete.",
            "The Dreamer opens their eyes.",
            "",
            "Thank you for playing."
        };

        foreach (var line in lines)
        {
            term.SetColor(string.IsNullOrEmpty(line) ? "white" : "bright_magenta");
            term.WriteLine($"  {line}");
            await Task.Delay(400);
        }
    }

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
        var player = GetCurrentPlayer();

        // Room header with theme color
        terminal.SetColor(GetThemeColor(currentFloor.Theme));
        terminal.WriteLine($"╔{new string('═', 55)}╗");
        terminal.WriteLine($"║  {room.Name.PadRight(52)} ║");
        terminal.WriteLine($"╚{new string('═', 55)}╝");

        // Show danger indicators
        ShowDangerIndicators(room);

        terminal.WriteLine("");

        // Room description
        terminal.SetColor("white");
        terminal.WriteLine(room.Description);
        terminal.WriteLine("");

        // Atmospheric text (builds tension)
        terminal.SetColor("gray");
        terminal.WriteLine(room.AtmosphereText);
        terminal.WriteLine("");

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

    private void ShowDangerIndicators(DungeonRoom room)
    {
        terminal.SetColor("darkgray");
        terminal.Write($"Level {currentDungeonLevel} | ");

        // Show floor theme
        terminal.SetColor(GetThemeColor(currentFloor.Theme));
        terminal.Write($"{currentFloor.Theme} | ");

        // Danger rating
        terminal.SetColor(room.DangerRating >= 3 ? "red" : room.DangerRating >= 2 ? "yellow" : "green");
        terminal.Write($"Danger: ");
        for (int i = 0; i < room.DangerRating; i++) terminal.Write("*");
        for (int i = room.DangerRating; i < 3; i++) terminal.Write(".");

        // Room status
        if (room.IsCleared)
        {
            terminal.SetColor("green");
            terminal.Write(" [CLEARED]");
        }
        else if (room.HasMonsters)
        {
            terminal.SetColor("red");
            terminal.Write(" [DANGER]");
        }

        if (room.IsBossRoom)
        {
            terminal.SetColor("bright_red");
            terminal.Write(" [BOSS]");
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
                terminal.WriteLine(">> A powerful presence dominates this room! <<");
            }
            else
            {
                var monsterHints = new[]
                {
                    "Shadows move at the edge of your torchlight...",
                    "You hear hostile sounds from the darkness...",
                    "Something is watching you from the shadows...",
                    "The air feels thick with menace..."
                };
                terminal.WriteLine(monsterHints[dungeonRandom.Next(monsterHints.Length)]);
            }
            hasAnything = true;
        }

        // Treasure
        if (room.HasTreasure && !room.TreasureLooted)
        {
            terminal.SetColor("yellow");
            terminal.WriteLine(">> Something valuable glints in the darkness! <<");
            hasAnything = true;
        }

        // Trap (hidden hint)
        if (room.HasTrap && !room.TrapTriggered && dungeonRandom.NextDouble() < 0.3)
        {
            terminal.SetColor("magenta");
            terminal.WriteLine(">> You sense hidden danger... <<");
            hasAnything = true;
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
            terminal.WriteLine(">> Stairs lead down to a deeper level <<");
            hasAnything = true;
        }

        // Features to examine
        if (room.Features.Any(f => !f.IsInteracted))
        {
            terminal.SetColor("cyan");
            terminal.WriteLine("");
            terminal.WriteLine("You notice:");
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
            DungeonEventType.TreasureChest => ">> An old chest sits in the corner <<",
            DungeonEventType.Merchant => ">> You see a figure by a small campfire <<",
            DungeonEventType.Shrine => ">> A strange altar radiates energy <<",
            DungeonEventType.NPCEncounter => ">> Someone else is here <<",
            DungeonEventType.Puzzle => ">> Strange mechanisms cover one wall <<",
            DungeonEventType.RestSpot => ">> This area seems relatively safe <<",
            DungeonEventType.MysteryEvent => ">> Something unusual catches your eye <<",
            _ => ">> Something interesting is here <<"
        };
    }

    private void ShowExits(DungeonRoom room)
    {
        terminal.SetColor("white");
        terminal.WriteLine("Exits:");

        foreach (var exit in room.Exits)
        {
            var targetRoom = currentFloor.GetRoom(exit.Value.TargetRoomId);
            var dirKey = GetDirectionKey(exit.Key);

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
                    terminal.Write(" (cleared)");
                }
                else if (targetRoom.IsExplored)
                {
                    terminal.SetColor("yellow");
                    terminal.Write(" (explored)");
                }
                else
                {
                    terminal.SetColor("darkgray");
                    terminal.Write(" (unknown)");
                }
            }

            terminal.WriteLine("");
        }
        terminal.WriteLine("");
    }

    private void ShowRoomActions(DungeonRoom room)
    {
        terminal.SetColor("white");
        terminal.WriteLine("Actions:");

        // Fight monsters
        if (room.HasMonsters && !room.IsCleared)
        {
            terminal.SetColor("darkgray");
            terminal.Write("  [");
            terminal.SetColor("red");
            terminal.Write("F");
            terminal.SetColor("darkgray");
            terminal.Write("] ");
            terminal.SetColor("white");
            terminal.WriteLine("Fight the monsters");
        }

        // Search for treasure (available if room is cleared OR has no monsters)
        if (room.HasTreasure && !room.TreasureLooted && (room.IsCleared || !room.HasMonsters))
        {
            terminal.SetColor("darkgray");
            terminal.Write("  [");
            terminal.SetColor("yellow");
            terminal.Write("T");
            terminal.SetColor("darkgray");
            terminal.Write("] ");
            terminal.SetColor("white");
            terminal.WriteLine("Collect treasure");
        }

        // Interact with event
        if (room.HasEvent && !room.EventCompleted)
        {
            terminal.SetColor("darkgray");
            terminal.Write("  [");
            terminal.SetColor("cyan");
            terminal.Write("V");
            terminal.SetColor("darkgray");
            terminal.Write("] ");
            terminal.SetColor("white");
            terminal.WriteLine("Investigate the event");
        }

        // Examine features
        if (room.Features.Any(f => !f.IsInteracted))
        {
            terminal.SetColor("darkgray");
            terminal.Write("  [");
            terminal.SetColor("magenta");
            terminal.Write("X");
            terminal.SetColor("darkgray");
            terminal.Write("] ");
            terminal.SetColor("white");
            terminal.WriteLine("Examine features");
        }

        // Use stairs (available if room is cleared OR has no monsters)
        if (room.HasStairsDown && (room.IsCleared || !room.HasMonsters))
        {
            terminal.SetColor("darkgray");
            terminal.Write("  [");
            terminal.SetColor("blue");
            terminal.Write("D");
            terminal.SetColor("darkgray");
            terminal.Write("] ");
            terminal.SetColor("white");
            terminal.WriteLine("Descend stairs");
        }

        // Rest (if safe - room cleared or no monsters)
        if ((room.IsCleared || !room.HasMonsters) && !hasRestThisFloor)
        {
            terminal.SetColor("darkgray");
            terminal.Write("  [");
            terminal.SetColor("green");
            terminal.Write("R");
            terminal.SetColor("darkgray");
            terminal.Write("] ");
            terminal.SetColor("white");
            terminal.WriteLine("Rest and recover");
        }

        // General options
        terminal.SetColor("darkgray");
        terminal.Write("  [");
        terminal.SetColor("cyan");
        terminal.Write("M");
        terminal.SetColor("darkgray");
        terminal.Write("] ");
        terminal.SetColor("white");
        terminal.Write("Map  ");

        terminal.SetColor("darkgray");
        terminal.Write("[");
        terminal.SetColor("cyan");
        terminal.Write("I");
        terminal.SetColor("darkgray");
        terminal.Write("] ");
        terminal.SetColor("white");
        terminal.Write("Inventory  ");

        terminal.SetColor("darkgray");
        terminal.Write("[");
        terminal.SetColor("cyan");
        terminal.Write("P");
        terminal.SetColor("darkgray");
        terminal.Write("] ");
        terminal.SetColor("white");
        terminal.Write("Potions  ");

        terminal.SetColor("darkgray");
        terminal.Write("[");
        terminal.SetColor("cyan");
        terminal.Write("=");
        terminal.SetColor("darkgray");
        terminal.Write("] ");
        terminal.SetColor("white");
        terminal.Write("Status  ");

        terminal.SetColor("darkgray");
        terminal.Write("[");
        terminal.SetColor("red");
        terminal.Write("Q");
        terminal.SetColor("darkgray");
        terminal.Write("] ");
        terminal.SetColor("white");
        terminal.WriteLine("Leave dungeon");

        terminal.WriteLine("");
    }

    private void ShowQuickStatus(Character player)
    {
        terminal.SetColor("darkgray");
        terminal.Write(new string('─', 57));
        terminal.WriteLine("");

        // Health bar
        terminal.SetColor("white");
        terminal.Write("HP: ");
        DrawBar(player.HP, player.MaxHP, 20, "red", "darkgray");
        terminal.Write($" {player.HP}/{player.MaxHP}");

        terminal.Write("  ");

        // Potions
        terminal.SetColor("green");
        terminal.Write($"Potions: {player.Healing}/{player.MaxPotions}");

        terminal.Write("  ");

        // Gold
        terminal.SetColor("yellow");
        terminal.Write($"Gold: {player.Gold:N0}");

        terminal.WriteLine("");
    }

    private void DrawBar(long current, long max, int width, string fillColor, string emptyColor)
    {
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

    private string GetThemeDescription(DungeonTheme theme)
    {
        return theme switch
        {
            DungeonTheme.Catacombs => "Ancient burial chambers filled with restless dead",
            DungeonTheme.Sewers => "Fetid tunnels crawling with vermin and worse",
            DungeonTheme.Caverns => "Natural caves carved by underground rivers",
            DungeonTheme.AncientRuins => "Crumbling remnants of a forgotten civilization",
            DungeonTheme.DemonLair => "Hellish corridors reeking of brimstone",
            DungeonTheme.FrozenDepths => "Ice-encrusted halls where cold itself hunts",
            DungeonTheme.VolcanicPit => "Molten rivers and scorching heat await",
            DungeonTheme.AbyssalVoid => "Reality itself warps in these cursed depths",
            _ => "Dark passages wind into the unknown"
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

    /// <summary>
    /// Display floor overview before entering
    /// </summary>
    private void DisplayFloorOverview()
    {
        ShowBreadcrumb();

        // Header - standardized format
        terminal.SetColor("bright_cyan");
        terminal.WriteLine("╔═════════════════════════════════════════════════════════════════════════════╗");
        terminal.SetColor("bright_yellow");
        terminal.WriteLine($"║                         DUNGEON LEVEL {currentDungeonLevel.ToString().PadLeft(3)}                                  ║");
        terminal.SetColor("bright_cyan");
        terminal.WriteLine("╚═════════════════════════════════════════════════════════════════════════════╝");
        terminal.WriteLine("");
        terminal.SetColor(GetThemeColor(currentFloor.Theme));
        terminal.WriteLine($"Theme: {currentFloor.Theme}");
        terminal.WriteLine("");

        // Floor stats
        terminal.SetColor("white");
        terminal.WriteLine($"Rooms: {currentFloor.Rooms.Count}");
        terminal.WriteLine($"Danger Level: {currentFloor.DangerLevel}/10");

        int explored = currentFloor.Rooms.Count(r => r.IsExplored);
        int cleared = currentFloor.Rooms.Count(r => r.IsCleared);
        terminal.WriteLine($"Explored: {explored}/{currentFloor.Rooms.Count}");
        terminal.WriteLine($"Cleared: {cleared}/{currentFloor.Rooms.Count}");
        terminal.WriteLine("");

        // Floor flavor
        terminal.SetColor("gray");
        terminal.WriteLine(GetFloorFlavorText(currentFloor.Theme));
        terminal.WriteLine("");

        // Team info
        terminal.SetColor("cyan");
        terminal.WriteLine($"Your Party: {1 + teammates.Count} member{(teammates.Count > 0 ? "s" : "")}");
        terminal.WriteLine("");

        // Show seal hint if this floor has an uncollected seal
        if (currentFloor.HasUncollectedSeal && !currentFloor.SealCollected)
        {
            terminal.SetColor("bright_yellow");
            terminal.WriteLine(">> A Seal of the Old Gods is hidden on this floor! <<");
            terminal.WriteLine("");
        }

        // Show level eligibility notification
        ShowLevelEligibilityMessage();

        // Show floor-specific guidance
        ShowFloorGuidance(currentDungeonLevel);

        // Options - standardized format
        terminal.SetColor("cyan");
        terminal.WriteLine("Actions:");
        terminal.WriteLine("");

        terminal.SetColor("darkgray");
        terminal.Write(" [");
        terminal.SetColor("bright_green");
        terminal.Write("E");
        terminal.SetColor("darkgray");
        terminal.Write("]");
        terminal.SetColor("white");
        terminal.Write("nter the dungeon      ");

        terminal.SetColor("darkgray");
        terminal.Write("[");
        terminal.SetColor("bright_cyan");
        terminal.Write("J");
        terminal.SetColor("darkgray");
        terminal.Write("]");
        terminal.SetColor("white");
        terminal.WriteLine("ournal - Quest progress");

        terminal.SetColor("darkgray");
        terminal.Write(" [");
        terminal.SetColor("bright_yellow");
        terminal.Write("T");
        terminal.SetColor("darkgray");
        terminal.Write("]");
        terminal.SetColor("white");
        terminal.Write("Party management      ");

        terminal.SetColor("darkgray");
        terminal.Write("[");
        terminal.SetColor("bright_cyan");
        terminal.Write("S");
        terminal.SetColor("darkgray");
        terminal.Write("]");
        terminal.SetColor("white");
        terminal.WriteLine("tatus");

        terminal.SetColor("darkgray");
        terminal.Write(" [");
        terminal.SetColor("bright_yellow");
        terminal.Write("L");
        terminal.SetColor("darkgray");
        terminal.Write("]");
        terminal.SetColor("white");
        terminal.Write("evel change (+/- 10)  ");

        terminal.SetColor("darkgray");
        terminal.Write("[");
        terminal.SetColor("bright_red");
        terminal.Write("Q");
        terminal.SetColor("darkgray");
        terminal.Write("]");
        terminal.SetColor("white");
        terminal.WriteLine("uit to town");
        terminal.WriteLine("");
    }

    private string GetFloorFlavorText(DungeonTheme theme)
    {
        return theme switch
        {
            DungeonTheme.Catacombs => "Ancient burial grounds stretch before you. The dead do not rest easy here.",
            DungeonTheme.Sewers => "The stench is overwhelming. Things lurk in the fetid waters below.",
            DungeonTheme.Caverns => "Natural caves twist into darkness. Bioluminescent life casts eerie shadows.",
            DungeonTheme.AncientRuins => "The ruins of a forgotten civilization. Magic still lingers in these stones.",
            DungeonTheme.DemonLair => "Hell has bled into this place. Tortured screams echo endlessly.",
            DungeonTheme.FrozenDepths => "Impossible cold. Your breath freezes. Things are preserved in the ice.",
            DungeonTheme.VolcanicPit => "Rivers of magma light the way. The heat is almost unbearable.",
            DungeonTheme.AbyssalVoid => "Reality breaks down here. What lurks beyond sanity itself?",
            _ => "Darkness awaits."
        };
    }

    /// <summary>
    /// Show floor-specific guidance to help players understand what's coming
    /// </summary>
    private void ShowFloorGuidance(int floor)
    {
        var story = StoryProgressionSystem.Instance;
        string hint = null;
        string color = "gray";

        // Special floor hints - Seal floors match SevenSealsSystem.cs
        // Seal 1 (Creation) = Temple (floor 0), Seal 2 (FirstWar) = 15, Seal 3 (Corruption) = 30
        // Seal 4 (Imprisonment) = 45, Seal 5 (Prophecy) = 60, Seal 6 (Regret) = 80, Seal 7 (Truth) = 99
        if (floor == 15 && !story.CollectedSeals.Contains(SealType.FirstWar))
        {
            hint = "LORE: The Second Seal awaits - it tells of the first war between gods.";
            color = "bright_cyan";
        }
        else if (floor == 25)
        {
            // Only show paradox hint if it's actually available
            var player = GetCurrentPlayer();
            if (player != null && MoralParadoxSystem.Instance.IsParadoxAvailable("possessed_child", player))
            {
                hint = "EVENT: The Possessed Child paradox may appear here. Choose wisely.";
                color = "bright_magenta";
            }
            else if (!story.HasStoryFlag("maelketh_encountered"))
            {
                hint = "BOSS: Maelketh, God of War, awaits in the depths of this floor!";
                color = "bright_red";
            }
        }
        else if (floor == 30 && !story.CollectedSeals.Contains(SealType.Corruption))
        {
            hint = "LORE: The Third Seal reveals how Manwe corrupted his own children.";
            color = "bright_cyan";
        }
        else if (floor == 45 && !story.CollectedSeals.Contains(SealType.Imprisonment))
        {
            hint = "LORE: The Fourth Seal tells of the eternal chains that bind the gods.";
            color = "bright_cyan";
        }
        else if (floor == 60)
        {
            if (!story.HasStoryFlag("maelketh_encountered"))
            {
                hint = "BOSS: Maelketh, God of War, can be challenged on this floor!";
                color = "bright_red";
            }
            else if (!story.CollectedSeals.Contains(SealType.Prophecy))
            {
                hint = "LORE: The Fifth Seal contains a prophecy about your coming.";
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
                    hint = "EVENT: The Soulweaver's Loom awaits discovery in this chamber.";
                    color = "bright_magenta";
                }
                else if (MoralParadoxSystem.Instance.IsParadoxAvailable("velouras_cure", player65))
                {
                    hint = "EVENT: The Soulweaver's Price - a moral choice awaits.";
                    color = "bright_magenta";
                }
            }
        }
        else if (floor == 75)
        {
            hint = "MEMORY: Your forgotten past will surface here. Pay attention to dreams.";
            color = "bright_blue";
        }
        else if (floor == 80)
        {
            if (!story.HasStoryFlag("terravok_encountered"))
            {
                hint = "BOSS: Terravok, God of Earth, slumbers here. Will you wake him?";
                color = "bright_yellow";
            }
            else if (!story.CollectedSeals.Contains(SealType.Regret))
            {
                hint = "LORE: The Sixth Seal shows Manwe's regret - his tears crystallized.";
                color = "bright_cyan";
            }
        }
        else if (floor == 95)
        {
            hint = "EVENT: The Destroy Darkness paradox awaits those with the Sunforged Blade.";
            color = "bright_magenta";
        }
        else if (floor == 99 && !story.CollectedSeals.Contains(SealType.Truth))
        {
            hint = "LORE: The Final Seal awaits - the truth of the Ocean Philosophy.";
            color = "bright_cyan";
        }
        else if (floor == 100)
        {
            hint = "FINALE: Manwe awaits. Your choices will determine the ending.";
            color = "bright_white";
        }
        else if (floor >= 50 && floor < 60)
        {
            hint = "TIP: Something powerful stirs in the depths below...";
            color = "yellow";
        }
        else if (floor >= 70 && floor < 80)
        {
            hint = "TIP: Ancient power awaits those who dare descend further...";
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
            terminal.SetColor("bright_yellow");
            terminal.WriteLine("╔══════════════════════════════════════════════════════════════════════════════╗");
            terminal.SetColor("bright_green");
            terminal.WriteLine("║     * You are eligible for a level raise! Visit your Master to advance! *    ║");
            terminal.SetColor("bright_yellow");
            terminal.WriteLine("╚══════════════════════════════════════════════════════════════════════════════╝");
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
            exp += (long)(Math.Pow(i, 1.8) * 50);
        }
        return exp;
    }

    protected override string GetBreadcrumbPath()
    {
        var room = currentFloor?.GetCurrentRoom();
        if (room != null && inRoomMode)
        {
            return $"Dungeons > Level {currentDungeonLevel} > {room.Name}";
        }
        return $"Main Street > Dungeons > Level {currentDungeonLevel}";
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
                terminal.WriteLine("You enter the dungeon...", "gray");
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

            case "Q":
                // Players can always leave to town - they may need to gear up, get companions, etc.
                // Floor locking only prevents ascending to PREVIOUS floors within the dungeon
                await NavigateToLocation(GameLocation.MainStreet);
                return true;

            default:
                terminal.WriteLine("Invalid choice.", "red");
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
        terminal.SetColor("bright_cyan");
        terminal.WriteLine("╔════════════════════════════════════════════════════════════╗");
        terminal.WriteLine("║                    S T O R Y   J O U R N A L               ║");
        terminal.WriteLine("╚════════════════════════════════════════════════════════════╝");
        terminal.WriteLine("");

        // Current chapter
        terminal.SetColor("white");
        terminal.Write("Current Chapter: ");
        terminal.SetColor("yellow");
        terminal.WriteLine(GetChapterName(story.CurrentChapter));
        terminal.WriteLine("");

        // The main objective
        terminal.SetColor("bright_white");
        terminal.WriteLine("═══ YOUR QUEST ═══");
        terminal.SetColor("white");
        terminal.WriteLine(GetCurrentObjective(story, player));
        terminal.WriteLine("");

        // Seals progress
        terminal.SetColor("bright_yellow");
        terminal.WriteLine($"═══ SEALS OF THE OLD GODS ({story.CollectedSeals.Count}/7) ═══");
        terminal.SetColor("gray");

        foreach (var seal in seals.GetAllSeals())
        {
            if (story.CollectedSeals.Contains(seal.Type))
            {
                terminal.SetColor("green");
                terminal.WriteLine($"  [X] {seal.Name} - {seal.Title}");
            }
            else
            {
                terminal.SetColor("gray");
                string locationText = seal.DungeonFloor == 0 ? "Hidden somewhere in town" : "Hidden in the dungeon depths";
                terminal.WriteLine($"  [ ] {seal.Name} - {locationText}");
                terminal.SetColor("dark_cyan");
                terminal.WriteLine($"      {seal.LocationHint}");
            }
        }
        terminal.WriteLine("");

        // What you know so far
        terminal.SetColor("bright_magenta");
        terminal.WriteLine("═══ WHAT YOU'VE LEARNED ═══");
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
        terminal.SetColor("bright_green");
        terminal.WriteLine("═══ SUGGESTED NEXT STEPS ═══");
        terminal.SetColor("white");
        ShowSuggestedSteps(story, player, seals);
        terminal.WriteLine("");

        terminal.SetColor("gray");
        await terminal.GetInputAsync("Press Enter to continue...");
    }

    private string GetChapterName(StoryChapter chapter)
    {
        return chapter switch
        {
            StoryChapter.Awakening => "The Awakening - A stranger in Dorashire",
            StoryChapter.FirstBlood => "First Blood - Learning to fight",
            StoryChapter.TheStranger => "The Stranger - Meeting the mysterious guide",
            StoryChapter.FactionChoice => "Faction Choice - Choosing your allegiance",
            StoryChapter.RisingPower => "Rising Power - Building your strength",
            StoryChapter.TheWhispers => "The Whispers - The gods begin to stir",
            StoryChapter.FirstGod => "First God - Confronting divine power",
            StoryChapter.GodWar => "God War - The Old Gods awaken",
            StoryChapter.TheChoice => "The Choice - Deciding your fate",
            StoryChapter.Ascension => "Ascension - The final preparations",
            StoryChapter.FinalConfrontation => "Final Confrontation - Face the Creator",
            StoryChapter.Epilogue => "Epilogue - The aftermath",
            _ => "Unknown Chapter"
        };
    }

    private string GetCurrentObjective(StoryProgressionSystem story, Character player)
    {
        var level = player?.Level ?? 1;

        if (level < 10)
            return "Explore the dungeons and grow stronger. Something awaits in the depths...";
        if (story.CollectedSeals.Count == 0)
            return "Find the Seals of the Old Gods hidden throughout the dungeon. They hold the truth.";
        if (story.CollectedSeals.Count < 4)
            return "Continue collecting Seals. Each one reveals more of the divine history.";
        if (story.CollectedSeals.Count < 7)
            return "Descend deeper. The remaining Seals await discovery in the abyss.";
        if (!story.HasStoryFlag("manwe_defeated"))
            return "All Seals collected. Face Manwe, the Creator. Choose your ending.";
        return "The story ends where it began. Seek your ending.";
    }

    private void ShowKnownLore(StoryProgressionSystem story)
    {
        var lorePoints = new List<string>();

        if (story.HasStoryFlag("knows_about_seals"))
            lorePoints.Add("- Seven Seals contain the history of the Old Gods");
        if (story.HasStoryFlag("heard_whispers"))
            lorePoints.Add("- A voice calls you 'wave' - as if you are part of something larger");
        if (story.HasStoryFlag("knows_prophecy"))
            lorePoints.Add("- The prophecy speaks of 'one from beyond the veil' who will decide the gods' fate");
        if (story.CollectedSeals.Count >= 3)
            lorePoints.Add("- Manwe corrupted his own children - the Old Gods - to stop their war");
        if (story.CollectedSeals.Count >= 5)
            lorePoints.Add("- The gods have been imprisoned for ten thousand years, slowly going mad");
        if (story.CollectedSeals.Count >= 7)
            lorePoints.Add("- The 'Ocean Philosophy': You are not a wave fighting the ocean - you ARE the ocean");
        if (story.HasStoryFlag("all_seals_collected"))
            lorePoints.Add("- ALL SEALS COLLECTED - The true ending is now possible!");

        if (lorePoints.Count == 0)
        {
            terminal.WriteLine("  You have not yet discovered the deeper truths...");
            terminal.WriteLine("  Explore the dungeons. Find the Seals. Remember who you are.");
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
                steps.Add($"- Find the {nextSeal.Name} (hidden somewhere in town)");
            }
            else
            {
                steps.Add($"- Seek the {nextSeal.Name} in the dungeon depths");
            }
        }

        // Level suggestions
        if (level < 15)
            steps.Add("- Grow stronger to delve deeper into the dungeon");
        else if (level < 50)
            steps.Add("- Continue leveling to access deeper dungeon floors");
        else if (level < 100)
            steps.Add("- Descend deeper. Ancient powers await in the abyss.");

        // Story suggestions
        if (!story.HasStoryFlag("met_stranger"))
            steps.Add("- Look for 'The Stranger' - a mysterious NPC who knows more than they let on");
        if (story.CollectedSeals.Count < 7 && level >= 50)
            steps.Add("- Collect all 7 Seals to unlock the true ending");

        if (steps.Count == 0)
        {
            steps.Add("- You are ready. Face the Creator in the deepest depths.");
        }

        foreach (var step in steps.Take(4))
        {
            terminal.WriteLine($"  {step}");
        }
    }

    private void ShowOceanProgress()
    {
        var ocean = OceanPhilosophySystem.Instance;

        terminal.SetColor("bright_blue");
        terminal.WriteLine($"═══ THE OCEAN'S WHISPER (Awakening: {ocean.AwakeningLevel}/7) ═══");

        // Show awakening level description
        terminal.SetColor("cyan");
        string awakeningDesc = ocean.AwakeningLevel switch
        {
            0 => "The world seems solid and separate. You know only the physical.",
            1 => "In quiet moments, the boundaries feel thin...",
            2 => "What is a wave but the ocean in motion?",
            3 => "When water meets water, which loses its identity?",
            4 => "Some souls are older than the bodies they wear.",
            5 => "Child of the deep waters, you are beginning to remember.",
            6 => "The boundaries blur. Self and other merge at the edges.",
            7 => "The wave remembers it is water. Welcome back, Dreamer.",
            _ => "Unknown state"
        };
        terminal.WriteLine($"  {awakeningDesc}");

        // Show wave fragments collected
        int fragmentCount = ocean.CollectedFragments.Count;
        int totalFragments = 10; // Total Wave Fragments
        terminal.SetColor("gray");
        terminal.WriteLine($"  Wave Fragments collected: {fragmentCount}/{totalFragments}");

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
        terminal.SetColor("bright_red");
        terminal.WriteLine("═══ THE OLD GODS ═══");

        // Define Old Gods and their floors
        var gods = new (OldGodType type, string name, int floor)[]
        {
            (OldGodType.Maelketh, "Maelketh, God of Chaos", 25),
            (OldGodType.Veloura, "Veloura, Goddess of Love", 40),
            (OldGodType.Thorgrim, "Thorgrim, God of Law", 55),
            (OldGodType.Noctura, "Noctura, Goddess of Shadow", 70),
            (OldGodType.Aurelion, "Aurelion, God of Light", 85),
            (OldGodType.Terravok, "Terravok, God of Earth", 95),
            (OldGodType.Manwe, "Manwe, the Creator", 100)
        };

        foreach (var (godType, godName, floor) in gods)
        {
            if (story.OldGodStates.TryGetValue(godType, out var state) &&
                state.Status != GodStatus.Unknown)
            {
                string statusText = state.Status switch
                {
                    GodStatus.Imprisoned => "Imprisoned",
                    GodStatus.Dormant => "Dormant",
                    GodStatus.Dying => "Dying",
                    GodStatus.Corrupted => "Corrupted",
                    GodStatus.Neutral => "Neutral",
                    GodStatus.Awakened => "Awakened",
                    GodStatus.Hostile => "Hostile",
                    GodStatus.Allied => "Allied",
                    GodStatus.Saved => "Saved",
                    GodStatus.Defeated => "Defeated",
                    GodStatus.Consumed => "Consumed",
                    _ => "Unknown"
                };

                string color = state.Status switch
                {
                    GodStatus.Allied or GodStatus.Saved => "green",
                    GodStatus.Defeated or GodStatus.Consumed => "red",
                    _ => "yellow"
                };

                terminal.SetColor(color);
                terminal.WriteLine($"  Floor {floor}: {godName} - {statusText}");
            }
            else
            {
                terminal.SetColor("gray");
                terminal.WriteLine($"  ???: Something stirs in the depths...");
            }
        }
    }

    private void ShowTownStoriesProgress()
    {
        var storySystem = TownNPCStorySystem.Instance;

        terminal.SetColor("bright_yellow");
        terminal.WriteLine("═══ TOWN STORIES ═══");

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
                        terminal.WriteLine($"  [✓] {name} - Story Complete");
                    }
                    else if (isFailed)
                    {
                        terminal.SetColor("red");
                        terminal.WriteLine($"  [X] {name} - Story Failed");
                    }
                    else
                    {
                        terminal.SetColor("yellow");
                        terminal.WriteLine($"  [~] {name} - Chapter {currentStage}/{totalStages}");
                    }
                }
            }
        }

        if (!anyProgress)
        {
            terminal.SetColor("gray");
            terminal.WriteLine("  You haven't made progress on any town stories yet.");
            terminal.WriteLine("  Explore town and talk to memorable NPCs to begin their tales.");
        }
    }

    private void ShowCompanionStatus()
    {
        var companions = CompanionSystem.Instance;

        terminal.SetColor("bright_white");
        terminal.WriteLine("═══ COMPANIONS ═══");

        var recruited = companions.GetRecruitedCompanions().ToList();
        var active = companions.GetActiveCompanions().ToList();
        var fallen = companions.GetFallenCompanions().ToList();

        if (recruited.Count == 0 && fallen.Count == 0)
        {
            terminal.SetColor("gray");
            terminal.WriteLine("  No companions recruited yet.");
            terminal.WriteLine("  Visit the Inn to meet potential allies.");
            return;
        }

        // Show active companions
        if (active.Count > 0)
        {
            terminal.SetColor("bright_green");
            terminal.WriteLine("  Active Party:");
            foreach (var companion in active)
            {
                int hp = companions.GetCompanionHP(companion.Id);
                terminal.SetColor("green");
                terminal.WriteLine($"    {companion.Name} - HP: {hp}/{companion.BaseStats.HP}");
            }
        }

        // Show inactive recruited companions
        var inactive = companions.GetInactiveCompanions().ToList();
        if (inactive.Count > 0)
        {
            terminal.SetColor("yellow");
            terminal.WriteLine("  At the Inn:");
            foreach (var companion in inactive)
            {
                terminal.WriteLine($"    {companion.Name}");
            }
        }

        // Show fallen companions
        if (fallen.Count > 0)
        {
            terminal.SetColor("red");
            terminal.WriteLine("  Fallen:");
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
                }
                return false;

            case "X":
                if (room.Features.Any(f => !f.IsInteracted))
                {
                    await ExamineFeatures(room);
                }
                return false;

            case "D":
                if (room.HasStairsDown && (room.IsCleared || !room.HasMonsters))
                {
                    await DescendStairs();
                }
                return false;

            case "R":
                if ((room.IsCleared || !room.HasMonsters) && !hasRestThisFloor)
                {
                    await RestInRoom();
                }
                return false;

            case "M":
                await ShowDungeonMap();
                return false;

            case "I":
                await ShowInventory();
                return false;

            case "P":
                await UsePotions();
                return false;

            case "=":
                await ShowStatus();
                return false;

            case "Q":
                // Leave dungeon
                inRoomMode = false;
                return false;

            default:
                terminal.WriteLine("Invalid choice. Use direction keys (N/S/E/W) or action keys.", "red");
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
        terminal.ClearScreen();
        terminal.SetColor("gray");
        terminal.WriteLine("You move through the passage...");
        await Task.Delay(800);

        // Check for trap on entering unexplored room
        if (!targetRoom.IsExplored && targetRoom.HasTrap && !targetRoom.TrapTriggered)
        {
            await TriggerTrap(targetRoom);
        }

        // Update current room
        currentFloor.CurrentRoomId = targetRoomId;

        if (!targetRoom.IsExplored)
        {
            targetRoom.IsExplored = true;
            roomsExploredThisFloor++;

            // Auto-clear rooms without monsters
            if (!targetRoom.HasMonsters)
            {
                targetRoom.IsCleared = true;
            }

            // Room discovery message
            terminal.SetColor(GetThemeColor(currentFloor.Theme));
            terminal.WriteLine($"You enter: {targetRoom.Name}");
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
                terminal.WriteLine("AMBUSH! The monsters attack!");
                await Task.Delay(1000);
                await FightRoomMonsters(targetRoom);
            }
        }
        else
        {
            consecutiveMonsterRooms = 0;
        }
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
            terminal.WriteLine("  (Something stirs in your memory...)");
        }

        // Grant wave fragment if any
        if (vision.WaveFragment.HasValue)
        {
            OceanPhilosophySystem.Instance.CollectFragment(vision.WaveFragment.Value);
            terminal.SetColor("bright_cyan");
            terminal.WriteLine("  (A fragment of truth settles into your consciousness...)");
        }

        await terminal.PressAnyKey();
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

        // Assassins get bonus trap evasion
        if (player.Class == CharacterClass.Assassin)
            evasionChance += 15;

        // Minimum 5% chance to evade
        evasionChance = Math.Max(5, evasionChance);

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
        terminal.WriteLine("*** TRAP! ***");
        await Task.Delay(500);

        // Check for evasion based on agility
        if (TryEvadeTrap(player))
        {
            terminal.SetColor("green");
            terminal.WriteLine("Your quick reflexes save you!");
            terminal.WriteLine($"You nimbly avoid the trap! (Agility: {player.Agility})");
            await Task.Delay(1500);
            return;
        }

        terminal.WriteLine("You couldn't react in time!", "yellow");
        await Task.Delay(300);

        var trapType = dungeonRandom.Next(6);
        switch (trapType)
        {
            case 0:
                var pitDmg = currentDungeonLevel * 3 + dungeonRandom.Next(10);
                player.HP -= pitDmg;
                terminal.WriteLine($"The floor gives way! You fall into a pit for {pitDmg} damage!");
                break;

            case 1:
                var dartDmg = currentDungeonLevel * 2 + dungeonRandom.Next(8);
                player.HP -= dartDmg;
                player.Poison = Math.Max(player.Poison, 1);
                terminal.WriteLine($"Poison darts! You take {dartDmg} damage and are poisoned!");
                break;

            case 2:
                var fireDmg = currentDungeonLevel * 4 + dungeonRandom.Next(12);
                player.HP -= fireDmg;
                terminal.WriteLine($"A gout of flame! You take {fireDmg} fire damage!");
                break;

            case 3:
                var goldLost = player.Gold / 10;
                player.Gold -= goldLost;
                terminal.WriteLine($"Acid sprays your belongings! You lose {goldLost} gold!");
                break;

            case 4:
                var expLost = currentDungeonLevel * 50;
                player.Experience = Math.Max(0, player.Experience - expLost);
                terminal.WriteLine($"A curse drains you! You lose {expLost} experience!");
                break;

            case 5:
                terminal.SetColor("green");
                terminal.WriteLine("The trap mechanism is broken. Nothing happens!");
                long bonusGold = currentDungeonLevel * 20;
                player.Gold += bonusGold;
                terminal.WriteLine($"You salvage {bonusGold} gold from the trap parts.");
                break;
        }

        await Task.Delay(2000);
    }

    /// <summary>
    /// Fight the monsters in a room
    /// </summary>
    private async Task FightRoomMonsters(DungeonRoom room)
    {
        var player = GetCurrentPlayer();

        terminal.ClearScreen();
        terminal.SetColor("red");

        if (room.IsBossRoom)
        {
            terminal.WriteLine("╔═══════════════════════════════════════════════════╗");
            terminal.WriteLine("║              *** BOSS ENCOUNTER ***               ║");
            terminal.WriteLine("╚═══════════════════════════════════════════════════╝");
            terminal.WriteLine("");
            terminal.WriteLine(room.Description);

            // Check for Old God boss encounters on specific floors
            bool hadOldGodEncounter = await TryOldGodBossEncounter(player, room);
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
                     state.Status == GodStatus.Awakened ||
                     state.Status == GodStatus.Consumed);

                if (godResolved)
                {
                    // Old God was already dealt with - room is empty
                    room.IsCleared = true;
                    currentFloor.BossDefeated = true;
                    terminal.SetColor("gray");
                    terminal.WriteLine("The chamber is empty. The ancient presence has already been dealt with.");
                    await Task.Delay(1500);
                    return;
                }
                else
                {
                    // God exists but prerequisites not met - hint that something ancient lurks here
                    terminal.SetColor("dark_magenta");
                    terminal.WriteLine("You sense an ancient presence sealed away in this chamber...");
                    terminal.WriteLine("Perhaps you must prove yourself elsewhere before it reveals itself.", "gray");
                    terminal.WriteLine("");
                    await Task.Delay(2000);
                }
                // Fall through to generate normal boss monsters as placeholder
            }
        }
        else
        {
            terminal.WriteLine("═══ COMBAT! ═══");
        }

        terminal.WriteLine("");
        await Task.Delay(1000);

        // Generate monsters appropriate for this room
        var monsters = MonsterGenerator.GenerateMonsterGroup(currentDungeonLevel, dungeonRandom);

        // Make boss room monsters tougher
        if (room.IsBossRoom)
        {
            foreach (var m in monsters)
            {
                m.HP = (long)(m.HP * 1.5);
                m.MaxHP = m.HP;  // Keep MaxHP in sync with boosted HP
                m.Strength = (int)(m.Strength * 1.3);
            }
            // Ensure there's a boss
            if (!monsters.Any(m => m.IsBoss))
            {
                monsters[0].IsBoss = true;
                monsters[0].Name = GetBossName(currentFloor.Theme);
            }
        }

        // Display what we're fighting - handle mixed encounters properly
        if (monsters.Count == 1)
        {
            var monster = monsters[0];
            terminal.SetColor(monster.MonsterColor);
            terminal.WriteLine($"A {monster.Name} attacks!");
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
                terminal.WriteLine($"You face {group.Count} {plural}!");
            }
            else
            {
                // Mixed encounter
                terminal.Write("You face ");
                for (int i = 0; i < monsterGroups.Count; i++)
                {
                    var group = monsterGroups[i];
                    string plural = group.Count > 1 ? GetPluralName(group.Name) : group.Name;

                    if (i > 0 && i == monsterGroups.Count - 1)
                        terminal.Write(" and ");
                    else if (i > 0)
                        terminal.Write(", ");

                    terminal.Write($"{group.Count} {plural}");
                }
                terminal.WriteLine("!");
            }
        }

        terminal.WriteLine("");
        await Task.Delay(1500);

        // Check for divine punishment before combat
        var (punishmentApplied, damageModifier, defenseModifier) = await CheckDivinePunishment(player);

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
        var combatResult = await combatEngine.PlayerVsMonsters(player, monsters, teammates, offerMonkEncounter: true);

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
            terminal.WriteLine("You awaken at the Temple of Light...");
            await Task.Delay(2000);
            await NavigateToLocation(GameLocation.Temple);
            return;
        }

        // Check if player survived
        if (player.HP > 0)
        {
            room.IsCleared = true;
            currentFloor.MonstersKilled += monsters.Count;

            terminal.SetColor("green");
            terminal.WriteLine("The room is cleared!");

            if (room.IsBossRoom)
            {
                currentFloor.BossDefeated = true;

                // Also set the persistent flag in floor state
                if (player.DungeonFloorStates.TryGetValue(currentDungeonLevel, out var floorState))
                {
                    floorState.BossDefeated = true;
                }

                terminal.SetColor("bright_yellow");
                terminal.WriteLine("*** BOSS DEFEATED! ***");
                terminal.WriteLine("");

                // Bonus rewards for boss
                long bossGold = currentDungeonLevel * 500 + dungeonRandom.Next(1000);
                long bossExp = currentDungeonLevel * 300;
                player.Gold += bossGold;
                player.Experience += bossExp;

                terminal.WriteLine($"Bonus: {bossGold} gold, {bossExp} experience!");

                // Artifact drop chance for specific floor bosses
                await CheckArtifactDrop(player, currentDungeonLevel);
            }

            await Task.Delay(2000);
        }

        await terminal.PressAnyKey();
    }

    private string GetBossName(DungeonTheme theme)
    {
        return theme switch
        {
            DungeonTheme.Catacombs => "Bone Lord",
            DungeonTheme.Sewers => "Sludge Abomination",
            DungeonTheme.Caverns => "Crystal Guardian",
            DungeonTheme.AncientRuins => "Awakened Golem",
            DungeonTheme.DemonLair => "Pit Fiend",
            DungeonTheme.FrozenDepths => "Frost Wyrm",
            DungeonTheme.VolcanicPit => "Magma Elemental",
            DungeonTheme.AbyssalVoid => "Void Horror",
            _ => "Dungeon Boss"
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
                terminal.WriteLine("The air grows thick with the scent of ancient battlefields...");
                await Task.Delay(1500);
                terminal.WriteLine("MAELKETH, THE BROKEN BLADE, rises before you!", "bright_red");
                terminal.WriteLine("");
                terminal.WriteLine("The God of War speaks:", "yellow");
                terminal.WriteLine("\"Another mortal seeking glory? I have broken ten thousand like you.\"", "red");
                break;

            case OldGodType.Veloura:
                terminal.SetColor("bright_magenta");
                terminal.WriteLine("The scent of dying roses fills the air...");
                await Task.Delay(1500);
                terminal.WriteLine("VELOURA, THE FADING HEART, appears before you!", "bright_magenta");
                terminal.WriteLine("");
                terminal.WriteLine("The Goddess of Love weeps:", "magenta");
                terminal.WriteLine("\"Another heart come to break? Or to be broken?\"", "bright_magenta");
                terminal.WriteLine("\"It matters not. Love always ends in pain.\"", "magenta");
                break;

            case OldGodType.Thorgrim:
                terminal.SetColor("white");
                terminal.WriteLine("The weight of judgment presses upon your soul...");
                await Task.Delay(1500);
                terminal.WriteLine("THORGRIM, THE UNJUST JUDGE, descends!", "white");
                terminal.WriteLine("");
                terminal.WriteLine("The God of Law pronounces:", "gray");
                terminal.WriteLine("\"You are guilty. All are guilty. That is the only truth.\"", "white");
                terminal.WriteLine("\"The sentence is death. It was always death.\"", "gray");
                break;

            case OldGodType.Noctura:
                terminal.SetColor("bright_cyan");
                terminal.WriteLine("Shadows coalesce, forming shapes that watch...");
                await Task.Delay(1500);
                terminal.WriteLine("NOCTURA, THE SHADOW QUEEN, emerges from darkness!", "bright_cyan");
                terminal.WriteLine("");
                terminal.WriteLine("The Goddess of Shadows whispers:", "cyan");
                terminal.WriteLine("\"Interesting. You see me. Most cannot.\"", "bright_cyan");
                terminal.WriteLine("\"I wonder... are you enemy or opportunity?\"", "cyan");
                break;

            case OldGodType.Aurelion:
                terminal.SetColor("bright_yellow");
                terminal.WriteLine("A faint light flickers in the darkness, struggling to persist...");
                await Task.Delay(1500);
                terminal.WriteLine("AURELION, THE DIMMING LIGHT, manifests weakly!", "bright_yellow");
                terminal.WriteLine("");
                terminal.WriteLine("The God of Light speaks faintly:", "yellow");
                terminal.WriteLine("\"You come seeking light, but I have so little left to give.\"", "bright_yellow");
                terminal.WriteLine("\"The darkness grows stronger. Even gods can fade.\"", "yellow");
                break;

            case OldGodType.Terravok:
                terminal.SetColor("bright_yellow");
                terminal.WriteLine("The mountain itself seems to breathe. Stone shifts like flesh...");
                await Task.Delay(1500);
                terminal.WriteLine("TERRAVOK, THE WORLDBREAKER, awakens!", "bright_yellow");
                terminal.WriteLine("");
                terminal.WriteLine("The God of Earth rumbles:", "yellow");
                terminal.WriteLine("\"You dare disturb my slumber? I will return you to the stone.\"", "yellow");
                break;

            case OldGodType.Manwe:
                terminal.SetColor("bright_white");
                terminal.WriteLine("Reality itself trembles. The dream knows it is being watched...");
                await Task.Delay(2000);
                terminal.WriteLine("MANWE, THE CREATOR OF ALL, manifests!", "bright_white");
                terminal.WriteLine("");
                terminal.SetColor("white");
                terminal.WriteLine("The Creator speaks in a voice that is all voices:");
                terminal.SetColor("bright_cyan");
                terminal.WriteLine("\"You have come at last, my wayward child.\"");
                terminal.WriteLine("\"I have watched you from the beginning.\"");
                terminal.WriteLine("\"I AM the beginning. And perhaps... the end.\"");
                break;
        }

        terminal.WriteLine("");
        await Task.Delay(1500);

        string godName = godType.Value switch
        {
            OldGodType.Maelketh => "Maelketh, the Broken Blade",
            OldGodType.Veloura => "Veloura, the Fading Heart",
            OldGodType.Thorgrim => "Thorgrim, the Unjust Judge",
            OldGodType.Noctura => "Noctura, the Shadow Queen",
            OldGodType.Aurelion => "Aurelion, the Dimming Light",
            OldGodType.Terravok => "Terravok, the Worldbreaker",
            OldGodType.Manwe => "Manwe, the Creator",
            _ => "the Old God"
        };

        terminal.SetColor("bright_yellow");
        terminal.WriteLine($"Do you wish to face {godName}? (Y/N)");
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

            // Special handling for Manwe - trigger ending
            if (godType.Value == OldGodType.Manwe && result.Outcome != BossOutcome.Fled)
            {
                var endingType = EndingsSystem.Instance.DetermineEnding(player);
                await ShowEnding(endingType, player, terminal);
            }

            await terminal.PressAnyKey();
            return true;
        }
        else
        {
            terminal.SetColor("gray");
            string retreatMessage = godType.Value switch
            {
                OldGodType.Maelketh => "You sense the god retreating back into slumber... for now.",
                OldGodType.Terravok => "The mountain settles. Terravok slumbers on.",
                OldGodType.Manwe => "The Creator's presence fades, but you feel his gaze upon you still...",
                _ => "The god withdraws... for now."
            };
            terminal.WriteLine(retreatMessage);
            terminal.WriteLine("The boss room remains unconquered.", "yellow");
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
        await UsurperRemake.UI.ANSIArt.DisplayArtAnimated(terminal, UsurperRemake.UI.ANSIArt.Treasure, 30);
        terminal.WriteLine("");

        // Scale rewards with level
        long goldFound = currentDungeonLevel * 100 + dungeonRandom.Next(currentDungeonLevel * 200);
        long expFound = currentDungeonLevel * 50 + dungeonRandom.Next(100);

        player.Gold += goldFound;
        player.Experience += expFound;

        terminal.WriteLine($"You find {goldFound} gold pieces!");
        terminal.WriteLine($"You gain {expFound} experience!");

        // Chance for bonus items
        if (dungeonRandom.NextDouble() < 0.3)
        {
            int potions = dungeonRandom.Next(1, 3);
            player.Healing = Math.Min(player.MaxPotions, player.Healing + potions);
            terminal.SetColor("green");
            terminal.WriteLine($"You also find {potions} healing potion{(potions > 1 ? "s" : "")}!");
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
        terminal.WriteLine("What do you want to examine?");
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
        terminal.SetColor("white");
        terminal.WriteLine("[0] Cancel");
        terminal.WriteLine("");

        var input = await terminal.GetInput("Choice: ");

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
        await FeatureInteractionSystem.Instance.InteractWithFeature(
            feature,
            player,
            currentDungeonLevel,
            theme,
            terminal
        );
    }

    // Floors that require full clear before leaving
    private static readonly int[] SealFloors = { 15, 30, 45, 60, 80, 99 };
    private static readonly int[] SecretBossFloors = { 25, 50, 75, 99 };

    // Old God boss floors
    private static readonly int[] OldGodFloors = { 25, 40, 55, 70, 85, 95, 100 };

    // Combined list of all special floors for easy lookup (includes Old God floors)
    private static readonly int[] AllSpecialFloors = { 15, 25, 30, 40, 45, 50, 55, 60, 70, 75, 80, 85, 95, 99, 100 };

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
                    Godot.GD.Print($"[DungeonLocation] Synced seal floor {sealData.DungeonFloor} to ClearedSpecialFloors");
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
        // Check if this is the first time clearing this floor
        bool isFirstClear = true;
        if (player.DungeonFloorStates.TryGetValue(floorLevel, out var floorState))
        {
            isFirstClear = !floorState.EverCleared;
        }

        // Only award bonus on first clear
        if (!isFirstClear)
        {
            terminal.WriteLine("");
            terminal.WriteLine("You have re-cleared this floor.", "gray");
            terminal.WriteLine("(Completion bonus only awarded on first clear)", "darkgray");
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
        player.Experience += xpBonus;
        player.Gold += goldBonus;

        // Display the bonus
        terminal.WriteLine("");
        if (isSpecialFloor)
        {
            terminal.WriteLine("╔══════════════════════════════════════╗", "bright_yellow");
            terminal.WriteLine("║      FLOOR CONQUERED!                ║", "bright_yellow");
            terminal.WriteLine("╚══════════════════════════════════════╝", "bright_yellow");
            terminal.WriteLine($"You have proven your worth on this sacred floor!", "bright_magenta");
        }
        else
        {
            terminal.WriteLine("═══ FLOOR CLEARED ═══", "bright_green");
            terminal.WriteLine("You have vanquished all foes on this level!", "green");
        }
        terminal.WriteLine($"  Bonus XP: +{xpBonus:N0}", "bright_cyan");
        terminal.WriteLine($"  Bonus Gold: +{goldBonus:N0}", "bright_yellow");
        terminal.WriteLine("");

        // Track telemetry
        TelemetrySystem.Instance.TrackDungeonEvent(
            "floor_cleared", player.Level, floorLevel,
            details: isSpecialFloor ? "boss_floor" : "normal",
            xpGained: xpBonus, goldChange: goldBonus
        );

        // Mark special floors as cleared in persistent player data
        if (isSpecialFloor)
        {
            player.ClearedSpecialFloors.Add(floorLevel);
        }

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
            terminal.WriteLine("A powerful presence blocks your descent.", "bright_red");
            terminal.WriteLine("You must defeat the Old God on this floor before descending deeper.", "yellow");
            terminal.WriteLine($"({GetRemainingClearInfo()})", "gray");
            terminal.WriteLine("You may still ascend to prepare.", "cyan");
            await Task.Delay(2500);
            return;
        }

        // Check level restriction (player level + 10)
        if (currentDungeonLevel >= maxAccessible)
        {
            terminal.WriteLine($"You cannot venture deeper than level {maxAccessible} at your current strength.", "yellow");
            terminal.WriteLine("Level up to access deeper floors.", "gray");
            await Task.Delay(2000);
            return;
        }

        if (currentDungeonLevel >= maxDungeonLevel)
        {
            terminal.WriteLine("You have reached the deepest level of the dungeon.", "red");
            terminal.WriteLine("There is nowhere left to descend.", "yellow");
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

        // MUD mode: broadcast dungeon descent
        UsurperRemake.Server.RoomRegistry.BroadcastAction($"{player.DisplayName} descends deeper into the dungeon.");

        terminal.ClearScreen();
        terminal.SetColor("blue");
        terminal.WriteLine("You descend the ancient stairs...");
        terminal.WriteLine("The darkness grows deeper.");
        terminal.WriteLine("The air grows colder.");
        await Task.Delay(2000);

        // Generate or restore the next floor
        int nextLevel = currentDungeonLevel + 1;
        var floorResult = GenerateOrRestoreFloor(player, nextLevel);
        currentFloor = floorResult.Floor;
        currentDungeonLevel = nextLevel;
        roomsExploredThisFloor = floorResult.WasRestored ? currentFloor.Rooms.Count(r => r.IsExplored) : 0;
        hasRestThisFloor = false;
        consecutiveMonsterRooms = 0;

        // Track dungeon floor telemetry
        if (player != null)
        {
            TelemetrySystem.Instance.TrackDungeonEvent(
                "enter_floor", player.Level, currentDungeonLevel
            );
        }

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
        terminal.WriteLine($"You arrive at Level {currentDungeonLevel}");
        terminal.WriteLine($"Theme: {currentFloor.Theme}");

        // Show restoration status
        if (floorResult.WasRestored && !floorResult.DidRespawn)
        {
            terminal.SetColor("bright_green");
            terminal.WriteLine("[Continuing where you left off...]");
        }
        else if (floorResult.DidRespawn)
        {
            terminal.SetColor("yellow");
            terminal.WriteLine("[New creatures have emerged from the depths...]");
        }

        terminal.WriteLine("");
        terminal.SetColor("gray");
        terminal.WriteLine(GetFloorFlavorText(currentFloor.Theme));

        // Check for story events (seals, narrative moments) on this new floor
        await CheckFloorStoryEvents(player, terminal);

        await Task.Delay(2500);
    }

    /// <summary>
    /// Rest in a cleared room (once per floor)
    /// </summary>
    private async Task RestInRoom()
    {
        var player = GetCurrentPlayer();

        terminal.ClearScreen();
        terminal.SetColor("green");
        terminal.WriteLine("You find a defensible corner and rest...");
        terminal.WriteLine("");

        await Task.Delay(1500);

        // Heal 25% of max HP
        long healAmount = player.MaxHP / 4;
        player.HP = Math.Min(player.MaxHP, player.HP + healAmount);

        // Recover 25% of max Mana
        long manaAmount = player.MaxMana / 4;
        player.Mana = Math.Min(player.MaxMana, player.Mana + manaAmount);

        // Recover 25% of max Combat Stamina
        long staminaAmount = player.MaxCombatStamina / 4;
        player.CurrentCombatStamina = Math.Min(player.MaxCombatStamina, player.CurrentCombatStamina + staminaAmount);

        terminal.WriteLine($"You recover {healAmount} hit points.");
        if (manaAmount > 0)
            terminal.WriteLine($"You recover {manaAmount} mana.");
        if (staminaAmount > 0)
            terminal.WriteLine($"You recover {staminaAmount} stamina.");
        terminal.WriteLine("");
        terminal.SetColor("cyan");
        terminal.WriteLine($"HP: {player.HP}/{player.MaxHP}  MP: {player.Mana}/{player.MaxMana}  ST: {player.CurrentCombatStamina}/{player.MaxCombatStamina}");

        hasRestThisFloor = true;

        terminal.WriteLine("");
        terminal.SetColor("gray");
        terminal.WriteLine("You feel rested, but dare not linger too long.");

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
        terminal.WriteLine($"Current level: {currentDungeonLevel}", "white");
        terminal.WriteLine($"Your level: {playerLevel}", "cyan");
        terminal.WriteLine($"Deepest accessible: {maxAccessible} (your level + 10)", "yellow");
        terminal.WriteLine("");

        var input = await terminal.GetInput("Enter target level (or +/- for relative): ");

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
            terminal.WriteLine("A powerful presence blocks your descent.", "bright_red");
            terminal.WriteLine($"You must defeat {godName} on floor {blockingFloor} before descending deeper.", "yellow");
            terminal.WriteLine("You may still ascend to prepare.", "cyan");
            await Task.Delay(2500);
            return;
        }

        // Check if trying to DESCEND past an Old God floor that hasn't been cleared
        // Players CAN ascend (retreat to prepare) but CANNOT descend (skip the god) until defeated
        if (targetLevel > currentDungeonLevel && RequiresFloorClear() && !IsFloorCleared())
        {
            terminal.WriteLine("", "red");
            terminal.WriteLine("A powerful presence blocks your descent.", "bright_red");
            terminal.WriteLine("You must defeat the Old God on this floor before descending deeper.", "yellow");
            terminal.WriteLine($"({GetRemainingClearInfo()})", "gray");
            terminal.WriteLine("You may still ascend to prepare.", "cyan");
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
            roomsExploredThisFloor = floorResult.WasRestored ? currentFloor.Rooms.Count(r => r.IsExplored) : 0;
            hasRestThisFloor = false;
            consecutiveMonsterRooms = 0;

            // Log floor change
            UsurperRemake.Systems.DebugLogger.Instance.LogDungeonFloorChange(player?.Name ?? "Player", previousLevel, currentDungeonLevel);

            terminal.WriteLine($"Dungeon level set to {currentDungeonLevel}.", "green");

            // Show restoration status
            if (floorResult.WasRestored && !floorResult.DidRespawn)
            {
                terminal.WriteLine("[Continuing where you left off...]", "bright_green");
            }
            else if (floorResult.DidRespawn)
            {
                terminal.WriteLine("[New creatures have emerged from the depths...]", "yellow");
            }

            // Check for story events (seals, narrative moments) on this new floor
            if (player != null)
            {
                await CheckFloorStoryEvents(player, terminal);
            }
        }
        else
        {
            terminal.WriteLine("No change to dungeon level.", "gray");
        }

        await Task.Delay(1500);
    }
    
    /// <summary>
    /// Main exploration mechanic - Pascal encounter system
    /// </summary>
    private async Task ExploreLevel()
    {
        var currentPlayer = GetCurrentPlayer();

        // No turn/fight limits in the new persistent system - explore freely!

        terminal.ClearScreen();
        terminal.SetColor("yellow");
        terminal.WriteLine("═══ EXPLORING ═══");
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
        terminal.WriteLine("▼ MONSTER ENCOUNTER ▼");
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
                terminal.WriteLine($"⚠ A powerful [{monster.MonsterColor}]{monster.Name}[/] blocks your path! ⚠");
            }
            else
            {
                terminal.SetColor(monster.MonsterColor);
                terminal.WriteLine($"A [{monster.MonsterColor}]{monster.Name}[/] appears!");
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
                terminal.Write($"You encounter [{group.Color}]{group.Count} {plural}[/]");
                if (monsters[0].FamilyName != "")
                {
                    terminal.Write($" from the {monsters[0].FamilyName} family!");
                }
                else
                {
                    terminal.Write("!");
                }
            }
            else
            {
                // Mixed encounter - show all monster types
                terminal.Write("You encounter ");
                for (int i = 0; i < monsterGroups.Count; i++)
                {
                    var group = monsterGroups[i];
                    string plural = group.Count > 1 ? GetPluralName(group.Name) : group.Name;

                    if (i > 0 && i == monsterGroups.Count - 1)
                        terminal.Write(" and ");
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
            terminal.WriteLine("You awaken at the Temple of Light...");
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
            difficulty = "Trivial";
            diffColor = "darkgray";
            xpHint = $"~{estXP} XP (not worth your time)";
        }
        else if (powerRatio > 1.5f && hpRatio < 1.0f)
        {
            difficulty = "Easy";
            diffColor = "bright_green";
            xpHint = $"~{estXP} XP";
        }
        else if (powerRatio > 1.0f && hpRatio < 1.5f)
        {
            difficulty = "Fair";
            diffColor = "green";
            xpHint = $"~{estXP} XP";
        }
        else if (powerRatio > 0.7f && hpRatio < 2.5f)
        {
            difficulty = "Challenging";
            diffColor = "yellow";
            xpHint = $"~{estXP} XP (bring potions)";
        }
        else if (powerRatio > 0.5f)
        {
            difficulty = "Dangerous";
            diffColor = "bright_yellow";
            xpHint = $"~{estXP} XP (high risk)";
        }
        else
        {
            difficulty = "DEADLY";
            diffColor = "bright_red";
            xpHint = $"~{estXP} XP (flee recommended!)";
        }

        terminal.WriteLine("");
        terminal.SetColor("gray");
        terminal.Write("  Threat: ");
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
        terminal.WriteLine("You have found a scroll! It reads:");
        terminal.WriteLine("");
        
        var scrollType = dungeonRandom.Next(3);
        var currentPlayer = GetCurrentPlayer();
        
        switch (scrollType)
        {
            case 0: // Blessing scroll
                terminal.SetColor("bright_white");
                terminal.WriteLine("Utter: 'XAVARANTHE JHUSULMAX VASWIUN'");
                terminal.WriteLine("And you will receive a blessing.");
                break;
                
            case 1: // Undead summon scroll  
                terminal.SetColor("red");
                terminal.WriteLine("Utter: 'ZASHNIVANTHE ULIPMAN NO SEE'");
                terminal.WriteLine("And you will see ancient power rise again.");
                break;
                
            case 2: // Secret cave scroll
                terminal.SetColor("cyan");
                terminal.WriteLine("Utter: 'RANTVANTHI SHGELUUIM VARTHMIOPLXH'");
                terminal.WriteLine("And you will be given opportunities.");
                break;
        }
        
        terminal.WriteLine("");
        var recite = await terminal.GetInput("Recite the scroll? (Y/N): ");
        
        if (recite.ToUpper() == "Y")
        {
            await ExecuteScrollMagic(scrollType, currentPlayer);
        }
        else
        {
            terminal.WriteLine("You carefully store the scroll for later.", "gray");
        }
    }
    
    /// <summary>
    /// Execute scroll magic effects
    /// </summary>
    private async Task ExecuteScrollMagic(int scrollType, Character player)
    {
        terminal.WriteLine("");
        terminal.WriteLine("The ancient words resonate with power...", "bright_white");
        await Task.Delay(2000);
        
        switch (scrollType)
        {
            case 0: // Blessing
                {
                    long chivalryGain = dungeonRandom.Next(500) + 50;
                    player.Chivalry += chivalryGain;
                    
                    terminal.SetColor("bright_yellow");
                    terminal.WriteLine("Divine light surrounds you!");
                    terminal.WriteLine($"Your chivalry increases by {chivalryGain}!");
                }
                break;
                
            case 1: // Undead summon (triggers combat)
                {
                    terminal.SetColor("red");
                    terminal.WriteLine("The ground trembles as ancient evil awakens!");
                    await Task.Delay(2000);
                    
                    // Create undead monster
                    var undead = CreateUndeadMonster();
                    terminal.WriteLine($"You have summoned a {undead.Name}!");
                    
                    // Fight the undead
                    var combatEngine = new CombatEngine(terminal);
                    var combatResult = await combatEngine.PlayerVsMonster(player, undead, teammates);

                    // Check if player should return to temple after resurrection
                    if (combatResult.ShouldReturnToTemple)
                    {
                        terminal.SetColor("yellow");
                        terminal.WriteLine("You awaken at the Temple of Light...");
                        await Task.Delay(2000);
                        await NavigateToLocation(GameLocation.Temple);
                        return;
                    }
                }
                break;
                
            case 2: // Secret opportunity
                {
                    terminal.SetColor("bright_cyan");
                    terminal.WriteLine("A hidden passage opens before you!");

                    // Track secret found for achievements
                    player.Statistics.RecordSecretFound();

                    long bonusGold = currentDungeonLevel * 2000;
                    player.Gold += bonusGold;

                    terminal.WriteLine($"You discover {bonusGold} gold in the secret chamber!");
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
        terminal.WriteLine("=== FALLEN ADVENTURER ===");
        terminal.WriteLine("");

        var currentPlayer = GetCurrentPlayer();
        var adventurerClasses = new[] { "warrior", "mage", "cleric", "rogue", "paladin" };
        var adventurerClass = adventurerClasses[dungeonRandom.Next(adventurerClasses.Length)];

        terminal.SetColor("white");
        terminal.WriteLine($"You come upon the remains of a fallen {adventurerClass}.");
        terminal.WriteLine("Their weathered journal lies open beside them.");
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
        var choice = await terminal.GetInput("(S)earch their belongings or (R)espect the dead? ");

        if (choice.ToUpper() == "S")
        {
            int roll = dungeonRandom.Next(100);
            if (roll < 40)
            {
                // Gold scaled to level - roughly 1-2 monster kills worth
                long goldFound = currentDungeonLevel * 30 + dungeonRandom.Next(currentDungeonLevel * 20);
                currentPlayer.Gold += goldFound;
                terminal.SetColor("green");
                terminal.WriteLine($"You find {goldFound} gold coins.");
            }
            else if (roll < 70)
            {
                int potions = dungeonRandom.Next(1, 4);
                currentPlayer.Healing = Math.Min(currentPlayer.Healing + potions, currentPlayer.MaxPotions);
                terminal.SetColor("magenta");
                terminal.WriteLine($"You find {potions} healing potions.");
            }
            else if (roll < 90)
            {
                // XP scaled to roughly 1 monster kill
                long xp = (long)(Math.Pow(currentDungeonLevel, 1.5) * 15);
                currentPlayer.Experience += xp;
                terminal.SetColor("cyan");
                terminal.WriteLine($"Reading their notes teaches you something! (+{xp} XP)");
            }
            else
            {
                terminal.SetColor("red");
                terminal.WriteLine("The corpse animates! Undead guardian!");
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
                    terminal.WriteLine("You awaken at the Temple of Light...");
                    await Task.Delay(2000);
                    await NavigateToLocation(GameLocation.Temple);
                    return;
                }
            }
        }
        else
        {
            terminal.SetColor("white");
            terminal.WriteLine("You say a quiet prayer for the fallen adventurer.");
            currentPlayer.Chivalry += 2;
            terminal.SetColor("green");
            terminal.WriteLine("Your chivalry increases slightly.");
        }

        await Task.Delay(1500);
    }

    /// <summary>
    /// Echoing voices encounter - hear whispers that reveal hints
    /// </summary>
    private async Task EchoingVoicesEncounter()
    {
        terminal.SetColor("magenta");
        terminal.WriteLine("=== ECHOING VOICES ===");
        terminal.WriteLine("");

        terminal.SetColor("white");
        terminal.WriteLine("Strange whispers echo through the corridors...");
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
        terminal.WriteLine($"The encounter leaves you wiser. (+{xpGain} XP)");

        await Task.Delay(2000);
    }

    /// <summary>
    /// Mysterious portal encounter - quick travel or danger
    /// </summary>
    private async Task MysteriousPortalEncounter()
    {
        terminal.SetColor("blue");
        terminal.WriteLine("=== MYSTERIOUS PORTAL ===");
        terminal.WriteLine("");

        terminal.SetColor("white");
        terminal.WriteLine("A shimmering portal hovers before you, crackling with energy.");
        terminal.WriteLine("It pulses with an otherworldly light.");
        terminal.WriteLine("");

        var choice = await terminal.GetInput("(E)nter the portal, (S)tudy it, or (I)gnore it? ");
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
                terminal.WriteLine($"The portal whisks you deeper into the dungeon!");
                terminal.WriteLine($"You emerge on floor {newFloor}!");
                currentDungeonLevel = newFloor;
            }
            else if (roll < 75)
            {
                // Teleport back up (safe)
                int floorsUp = dungeonRandom.Next(3, 8);
                int newFloor = Math.Max(currentDungeonLevel - floorsUp, 1);
                terminal.SetColor("yellow");
                terminal.WriteLine($"The portal carries you upward!");
                terminal.WriteLine($"You emerge on floor {newFloor}.");
                currentDungeonLevel = newFloor;
            }
            else if (roll < 90)
            {
                // Treasure dimension - rare event, give more generous gold (5-8 monster kills worth)
                terminal.SetColor("green");
                terminal.WriteLine("You find yourself in a treasure dimension!");
                long goldFound = (long)(Math.Pow(currentDungeonLevel, 1.5) * 60) + dungeonRandom.Next(currentDungeonLevel * 30);
                currentPlayer.Gold += goldFound;
                terminal.WriteLine($"You grab {goldFound} gold before being pulled back!");
            }
            else
            {
                // Hostile dimension - fight
                terminal.SetColor("red");
                terminal.WriteLine("The portal leads to a hostile dimension!");
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
                    terminal.WriteLine("You awaken at the Temple of Light...");
                    await Task.Delay(2000);
                    await NavigateToLocation(GameLocation.Temple);
                    return;
                }

                if (result.Victory)
                {
                    // Boss-level rewards - roughly 3x normal monster
                    terminal.SetColor("green");
                    terminal.WriteLine("The guardian drops a rare crystal!");
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
            terminal.WriteLine("You study the portal's magical patterns...");
            // Small XP for studying - about half a monster kill
            long xpGain = (long)(Math.Pow(currentDungeonLevel, 1.5) * 8);
            currentPlayer.Experience += xpGain;
            terminal.WriteLine($"You learn something about dimensional magic. (+{xpGain} XP)");
        }
        else
        {
            terminal.SetColor("gray");
            terminal.WriteLine("You wisely avoid the unstable portal. It fades away.");
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
        terminal.WriteLine("=== THE DUELIST ===");
        terminal.WriteLine("");

        terminal.SetColor("white");
        terminal.WriteLine($"A warrior emerges from the shadows: {duelist.Name}");
        terminal.WriteLine("");

        // Different dialogue based on encounter history
        if (duelist.TimesEncountered == 0)
        {
            // First meeting
            terminal.SetColor("cyan");
            terminal.WriteLine($"\"{currentPlayer.Name}! I have heard tales of your exploits.\"");
            terminal.WriteLine("\"I am " + duelist.Name + ", and I seek worthy opponents.\"");
            terminal.WriteLine("\"Face me in honorable combat, and prove your worth!\"");
        }
        else if (duelist.PlayerWins > duelist.PlayerLosses)
        {
            // Player has been winning
            terminal.SetColor("yellow");
            terminal.WriteLine($"\"{currentPlayer.Name}! We meet again.\"");
            terminal.WriteLine($"\"Our record stands at {duelist.PlayerWins} to {duelist.PlayerLosses} in your favor.\"");
            terminal.WriteLine("\"I have trained relentlessly since our last bout. Today, I will prevail!\"");
        }
        else if (duelist.PlayerLosses > duelist.PlayerWins)
        {
            // Duelist has been winning
            terminal.SetColor("red");
            terminal.WriteLine($"\"Ah, {currentPlayer.Name}. Come for another lesson?\"");
            terminal.WriteLine($"\"I've bested you {duelist.PlayerLosses} times now.\"");
            terminal.WriteLine("\"Perhaps this time you'll put up a real fight.\"");
        }
        else
        {
            // Evenly matched
            terminal.SetColor("magenta");
            terminal.WriteLine($"\"{currentPlayer.Name}! My worthy rival!\"");
            terminal.WriteLine($"\"We are evenly matched at {duelist.PlayerWins} victories each.\"");
            terminal.WriteLine("\"Today we settle who is truly superior!\"");
        }

        // Show duelist info for returning encounters
        if (duelist.TimesEncountered > 0)
        {
            terminal.WriteLine("");
            terminal.SetColor("gray");
            terminal.WriteLine($"  Encounters: {duelist.TimesEncountered} | Your Wins: {duelist.PlayerWins} | Your Losses: {duelist.PlayerLosses}");
            terminal.WriteLine($"  {duelist.Name}'s current strength: Level {duelist.Level}");
        }

        terminal.WriteLine("");
        var choice = await terminal.GetInput("(A)ccept the challenge, (D)ecline politely, or (I)nsult them? ");

        if (choice.ToUpper() == "A")
        {
            terminal.SetColor("cyan");
            terminal.WriteLine($"You accept {duelist.Name}'s challenge!");
            terminal.WriteLine("Steel clashes against steel!");
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

            var combatEngine = new CombatEngine(terminal);
            var result = await combatEngine.PlayerVsMonster(currentPlayer, duelistMonster, null);

            // Check if player should return to temple after resurrection
            if (result.ShouldReturnToTemple)
            {
                terminal.SetColor("yellow");
                terminal.WriteLine("You awaken at the Temple of Light...");
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
                    terminal.WriteLine($"{duelist.Name} kneels before you!");
                    terminal.WriteLine("\"A worthy foe indeed! I shall remember this battle.\"");
                }
                else if (duelist.PlayerWins == 3)
                {
                    terminal.WriteLine($"{duelist.Name} laughs despite the loss!");
                    terminal.WriteLine("\"Three times now! You have earned my respect, {currentPlayer.Name}.\"");
                    terminal.WriteLine("\"But I will never stop seeking to surpass you!\"");
                }
                else if (duelist.PlayerWins >= 5)
                {
                    terminal.WriteLine($"{duelist.Name} bows deeply.");
                    terminal.WriteLine("\"You are truly a master. I am honored to call you my rival.\"");
                }
                else
                {
                    terminal.WriteLine($"{duelist.Name} bows in defeat.");
                    terminal.WriteLine("\"Well fought! Until we meet again...\"");
                }

                // Rewards scale with rivalry intensity - roughly 2-4 monster kills based on rivalry
                int rivalryBonus = 1 + duelist.TimesEncountered / 3;
                long goldReward = (long)(Math.Pow(currentDungeonLevel, 1.5) * 24 * rivalryBonus);
                long xpReward = (long)(Math.Pow(currentDungeonLevel, 1.5) * 15 * (1 + rivalryBonus * 0.5));
                currentPlayer.Gold += goldReward;
                currentPlayer.Experience += xpReward;
                currentPlayer.Chivalry += 5;

                terminal.WriteLine($"+{goldReward} gold, +{xpReward} XP, +5 Chivalry");
            }
            else if (currentPlayer.IsAlive)
            {
                duelist.PlayerLosses++;
                terminal.SetColor("yellow");

                if (duelist.PlayerLosses == 1)
                {
                    terminal.WriteLine($"{duelist.Name} spares your life!");
                    terminal.WriteLine("\"Train harder. I expect more next time.\"");
                }
                else if (duelist.PlayerLosses >= 3)
                {
                    terminal.WriteLine($"{duelist.Name} sighs with disappointment.");
                    terminal.WriteLine("\"I had hoped for better. Grow stronger, then face me again.\"");
                }
                else
                {
                    terminal.WriteLine($"{duelist.Name} sheaths their blade.");
                    terminal.WriteLine("\"We will meet again when you are ready.\"");
                }
            }

            // Save duelist progress
            SaveDuelistProgress(currentPlayer, duelist);
        }
        else if (choice.ToUpper() == "D")
        {
            terminal.SetColor("white");
            terminal.WriteLine("You politely decline the challenge.");

            if (duelist.TimesEncountered > 0)
            {
                terminal.WriteLine($"{duelist.Name} looks disappointed but understanding.");
                terminal.WriteLine("\"Very well. But do not avoid me forever.\"");
            }
            else
            {
                terminal.WriteLine($"{duelist.Name} nods respectfully.");
                terminal.WriteLine("\"A wise warrior knows when to fight. We shall meet again.\"");
            }
            currentPlayer.Chivalry += 1;
        }
        else if (choice.ToUpper() == "I")
        {
            terminal.SetColor("red");
            terminal.WriteLine("You hurl insults at the duelist!");

            if (duelist.TimesEncountered > 0 && duelist.PlayerWins > 0)
            {
                terminal.WriteLine($"{duelist.Name}'s eyes flash with fury!");
                terminal.WriteLine("\"After all our battles, you dishonor me like this?!\"");
                terminal.WriteLine("\"I will END you!\"");
            }
            else
            {
                terminal.WriteLine($"{duelist.Name}'s face contorts with rage!");
                terminal.WriteLine("\"You DARE mock me?! You will pay for that!\"");
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

            var combatEngine = new CombatEngine(terminal);
            var result = await combatEngine.PlayerVsMonster(currentPlayer, angryDuelist, teammates);

            // Check if player should return to temple after resurrection
            if (result.ShouldReturnToTemple)
            {
                terminal.SetColor("yellow");
                terminal.WriteLine("You awaken at the Temple of Light...");
                await Task.Delay(2000);
                await NavigateToLocation(GameLocation.Temple);
                return;
            }

            duelist.TimesEncountered++;
            if (result.Victory)
            {
                duelist.PlayerWins++;
                terminal.SetColor("yellow");
                terminal.WriteLine($"You have slain {duelist.Name} in dishonorable combat.");
                terminal.WriteLine("Their spirit will haunt you...");
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
            terminal.WriteLine($"{duelist.Name} watches you walk away.");
            if (duelist.TimesEncountered > 0)
            {
                terminal.WriteLine("\"Running from me now? Coward!\"");
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
        terminal.WriteLine("* TREASURE CHEST *");
        terminal.WriteLine("");

        terminal.WriteLine("You discover an ancient chest hidden in the shadows!", "cyan");
        terminal.WriteLine("");

        var choice = await terminal.GetInput("(O)pen the chest or (L)eave it alone? ");

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
                terminal.WriteLine("The chest opens to reveal glittering treasure!");
                terminal.WriteLine($"You find {goldFound} gold pieces!");
                terminal.WriteLine($"You gain {expGained} experience!");

                currentPlayer.Gold += goldFound;
                currentPlayer.Experience += expGained;
            }
            else if (chestRoll < 9)
            {
                // Trap!
                terminal.SetColor("red");
                terminal.WriteLine("CLICK! It's a trap!");

                // Check for evasion based on agility
                if (TryEvadeTrap(currentPlayer))
                {
                    terminal.SetColor("green");
                    terminal.WriteLine("You leap back just in time!");
                    terminal.WriteLine($"Your reflexes saved you! (Agility: {currentPlayer.Agility})");
                }
                else
                {
                    terminal.WriteLine("You couldn't react in time!", "yellow");
                    var trapType = dungeonRandom.Next(3);
                    switch (trapType)
                    {
                        case 0:
                            var poisonDmg = currentDungeonLevel * 5;
                            currentPlayer.HP -= poisonDmg;
                            terminal.WriteLine($"Poison gas! You take {poisonDmg} damage!");
                            currentPlayer.Poison = Math.Max(currentPlayer.Poison, 1);
                            terminal.WriteLine("You have been poisoned!", "magenta");
                            break;
                        case 1:
                            var spikeDmg = currentDungeonLevel * 8;
                            currentPlayer.HP -= spikeDmg;
                            terminal.WriteLine($"Spikes shoot out! You take {spikeDmg} damage!");
                            break;
                        case 2:
                            var goldLost = currentPlayer.Gold / 10;
                            currentPlayer.Gold -= goldLost;
                            terminal.WriteLine($"Acid sprays your coin pouch! You lose {goldLost} gold!");
                            break;
                    }
                }
            }
            else
            {
                // Mimic! (triggers combat)
                terminal.SetColor("bright_red");
                terminal.WriteLine("The chest MOVES! It's a MIMIC!");
                await Task.Delay(1500);

                var mimic = Monster.CreateMonster(
                    currentDungeonLevel, "Mimic",
                    currentDungeonLevel * 15, currentDungeonLevel * 4, 0,
                    "Fooled you!", false, false, "Teeth", "Wooden Shell",
                    false, false, currentDungeonLevel * 5, currentDungeonLevel * 3, currentDungeonLevel * 3
                );
                mimic.IsMiniBoss = true;  // Mimics are elite encounters, not floor bosses
                mimic.Level = currentDungeonLevel;

                var combatEngine = new CombatEngine(terminal);
                var combatResult = await combatEngine.PlayerVsMonster(currentPlayer, mimic, teammates);

                // Check if player should return to temple after resurrection
                if (combatResult.ShouldReturnToTemple)
                {
                    terminal.SetColor("yellow");
                    terminal.WriteLine("You awaken at the Temple of Light...");
                    await Task.Delay(2000);
                    await NavigateToLocation(GameLocation.Temple);
                    return;
                }
            }
        }
        else
        {
            terminal.WriteLine("You wisely leave the chest alone and continue on.", "gray");
        }

        await Task.Delay(2000);
    }

    /// <summary>
    /// Strangers encounter - Band of rogues/orcs (from Pascal DUNGEV2.PAS)
    /// </summary>
    private async Task StrangersEncounter()
    {
        terminal.SetColor("red");
        terminal.WriteLine("=== STRANGERS APPROACH ===");
        terminal.WriteLine("");

        var currentPlayer = GetCurrentPlayer();
        var groupType = dungeonRandom.Next(4);
        string groupName;
        string[] memberTypes;

        switch (groupType)
        {
            case 0:
                groupName = "orcs";
                memberTypes = new[] { "Orc", "Half-Orc", "Orc Raider" };
                terminal.WriteLine("A group of orcs emerges from the shadows!", "gray");
                terminal.WriteLine("They are poorly armed with sticks and clubs.", "gray");
                break;
            case 1:
                groupName = "trolls";
                memberTypes = new[] { "Troll", "Half-Troll", "Lumber-Troll" };
                terminal.WriteLine("A band of trolls blocks your path!", "green");
                terminal.WriteLine("They carry clubs and spears.", "gray");
                break;
            case 2:
                groupName = "rogues";
                memberTypes = new[] { "Rogue", "Thief", "Pirate" };
                terminal.WriteLine("A gang of rogues surrounds you!", "cyan");
                terminal.WriteLine("They brandish knives and rapiers.", "gray");
                break;
            default:
                groupName = "dwarves";
                memberTypes = new[] { "Dwarf", "Dwarf Warrior", "Dwarf Scout" };
                terminal.WriteLine("A group of armed dwarves approaches!", "yellow");
                terminal.WriteLine("They carry swords and axes.", "gray");
                break;
        }

        terminal.WriteLine("");
        terminal.WriteLine("Their leader demands your gold!", "white");
        terminal.WriteLine("");

        var choice = await terminal.GetInput("(F)ight them, (P)ay them off, or try to (E)scape? ");

        if (choice.ToUpper() == "F")
        {
            terminal.WriteLine("You draw your weapon and prepare for battle!", "yellow");
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
                terminal.WriteLine("You awaken at the Temple of Light...");
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
                terminal.WriteLine($"You reluctantly hand over {bribe} gold.", "yellow");
                terminal.WriteLine($"The {groupName} leave you in peace.", "gray");
            }
            else
            {
                terminal.WriteLine("You don't have enough gold!", "red");
                terminal.WriteLine("They attack anyway!", "red");
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
                    terminal.WriteLine("You awaken at the Temple of Light...");
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
                terminal.WriteLine("You manage to slip away into the shadows!", "green");
            }
            else
            {
                terminal.WriteLine("They catch you trying to escape!", "red");
                long stolen = currentPlayer.Gold / 5;
                currentPlayer.Gold -= stolen;
                terminal.WriteLine($"They beat you and steal {stolen} gold!", "red");
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
        terminal.WriteLine("♀ DAMSEL IN DISTRESS ♀");
        terminal.WriteLine("");

        terminal.WriteLine("You hear screams echoing through the corridor!", "white");
        terminal.WriteLine("A woman is being harassed by a band of ruffians.", "gray");
        terminal.WriteLine("");

        var choice = await terminal.GetInput("(H)elp her, (I)gnore the situation, or (J)oin the ruffians? ");

        var currentPlayer = GetCurrentPlayer();

        if (choice.ToUpper() == "H")
        {
            terminal.WriteLine("You rush to her defense!", "green");
            terminal.WriteLine("\"Unhand her, villains!\"", "yellow");
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
                terminal.WriteLine("You awaken at the Temple of Light...");
                await Task.Delay(2000);
                await NavigateToLocation(GameLocation.Temple);
                return;
            }

            if (currentPlayer.HP > 0)
            {
                terminal.WriteLine("");
                terminal.WriteLine("The woman thanks you profusely!", "cyan");
                long reward = currentDungeonLevel * 300 + dungeonRandom.Next(500);
                long chivGain = dungeonRandom.Next(50) + 30;

                terminal.WriteLine($"She rewards you with {reward} gold!", "yellow");
                terminal.WriteLine($"Your chivalry increases by {chivGain}!", "white");

                currentPlayer.Gold += reward;
                currentPlayer.Chivalry += chivGain;
            }
        }
        else if (choice.ToUpper() == "J")
        {
            terminal.SetColor("red");
            terminal.WriteLine("You join the ruffians in their villainy!");
            terminal.WriteLine("This is a shameful act!");

            long stolen = dungeonRandom.Next(200) + 50;
            long darkGain = dungeonRandom.Next(75) + 50;

            currentPlayer.Gold += stolen;
            currentPlayer.Darkness += darkGain;

            terminal.WriteLine($"You steal {stolen} gold from the woman.", "yellow");
            terminal.WriteLine($"Your darkness increases by {darkGain}!", "magenta");
        }
        else
        {
            terminal.WriteLine("You turn away and pretend not to notice.", "gray");
            terminal.WriteLine("Her cries fade as you continue your journey...", "gray");
        }

        await Task.Delay(2500);
    }

    /// <summary>
    /// Wounded man encounter - Healing quest (from Pascal DUNGEVC.PAS)
    /// </summary>
    private async Task WoundedManEncounter()
    {
        terminal.SetColor("cyan");
        terminal.WriteLine("✚ WOUNDED STRANGER ✚");
        terminal.WriteLine("");

        terminal.WriteLine("You find a wounded man lying against the wall.", "white");
        terminal.WriteLine("He is bleeding heavily and begs for help.", "gray");
        terminal.WriteLine("\"Please... I need healing... I can pay...\"", "yellow");
        terminal.WriteLine("");

        var choice = await terminal.GetInput("(H)elp him, (R)ob him, or (L)eave him? ");

        var currentPlayer = GetCurrentPlayer();

        if (choice.ToUpper() == "H")
        {
            if (currentPlayer.Healing > 0)
            {
                currentPlayer.Healing--;
                terminal.WriteLine("You use a healing potion on the wounded stranger.", "green");
                terminal.WriteLine("He recovers enough to stand.", "white");

                long reward = currentDungeonLevel * 500 + dungeonRandom.Next(1000);
                long chivGain = dungeonRandom.Next(40) + 20;

                terminal.WriteLine($"\"Thank you, hero! Take this reward: {reward} gold!\"", "yellow");
                terminal.WriteLine($"Your chivalry increases by {chivGain}!", "white");

                currentPlayer.Gold += reward;
                currentPlayer.Chivalry += chivGain;
            }
            else
            {
                terminal.WriteLine("You have no healing potions to spare!", "red");
                terminal.WriteLine("You try to bandage his wounds with cloth...", "gray");

                if (dungeonRandom.NextDouble() < 0.5)
                {
                    terminal.WriteLine("It seems to help a little.", "green");
                    currentPlayer.Chivalry += 10;
                }
                else
                {
                    terminal.WriteLine("Unfortunately, he dies from his wounds.", "red");
                }
            }
        }
        else if (choice.ToUpper() == "R")
        {
            terminal.SetColor("red");
            terminal.WriteLine("You search the dying man's belongings...");

            long stolen = dungeonRandom.Next(500) + 100;
            long darkGain = dungeonRandom.Next(80) + 60;

            terminal.WriteLine($"You find {stolen} gold in his purse.", "yellow");
            terminal.WriteLine($"Your darkness increases by {darkGain}!", "magenta");

            currentPlayer.Gold += stolen;
            currentPlayer.Darkness += darkGain;

            terminal.WriteLine("He dies cursing your name...", "gray");
        }
        else
        {
            terminal.WriteLine("You step over the dying man and continue on.", "gray");
            terminal.WriteLine("His moans fade behind you...", "gray");
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
        terminal.WriteLine("✦ MYSTERIOUS SHRINE ✦");
        terminal.WriteLine("");

        terminal.WriteLine("You discover an ancient shrine glowing with strange light.", "white");
        terminal.WriteLine("Offerings of gold and bones surround a stone altar.", "gray");
        terminal.WriteLine("");

        var choice = await terminal.GetInput("(P)ray at the shrine, (D)esecrate it, or (L)eave? ");

        if (choice.ToUpper() == "P")
        {
            terminal.WriteLine("You kneel before the ancient shrine...", "cyan");
            await Task.Delay(1500);

            // Random blessing or curse
            var outcome = dungeonRandom.Next(6);
            switch (outcome)
            {
                case 0:
                    terminal.WriteLine("Divine light fills you!", "bright_yellow");
                    currentPlayer.HP = currentPlayer.MaxHP;
                    terminal.WriteLine("You are fully healed!", "green");
                    break;
                case 1:
                    var strBonus = dungeonRandom.Next(5) + 1;
                    currentPlayer.Strength += strBonus;
                    terminal.WriteLine($"You feel stronger! +{strBonus} Strength!", "green");
                    break;
                case 2:
                    var expBonus = currentDungeonLevel * 500;
                    currentPlayer.Experience += expBonus;
                    terminal.WriteLine($"Ancient wisdom flows into you! +{expBonus} EXP!", "yellow");
                    break;
                case 3:
                    terminal.WriteLine("The shrine is silent...", "gray");
                    terminal.WriteLine("Nothing happens.", "gray");
                    break;
                case 4:
                    var hpLoss = currentPlayer.HP / 4;
                    currentPlayer.HP -= hpLoss;
                    terminal.WriteLine("The shrine drains your life force!", "red");
                    terminal.WriteLine($"You lose {hpLoss} HP!", "red");
                    break;
                case 5:
                    var goldLoss = currentPlayer.Gold / 5;
                    currentPlayer.Gold -= goldLoss;
                    terminal.WriteLine("Your gold dissolves into the altar!", "red");
                    terminal.WriteLine($"You lose {goldLoss} gold!", "red");
                    break;
            }
        }
        else if (choice.ToUpper() == "D")
        {
            terminal.SetColor("red");
            terminal.WriteLine("You smash the shrine and steal the offerings!");

            long stolen = currentDungeonLevel * 200 + dungeonRandom.Next(500);
            long darkGain = dungeonRandom.Next(50) + 30;

            terminal.WriteLine($"You find {stolen} gold among the offerings!", "yellow");
            terminal.WriteLine($"Your darkness increases by {darkGain}!", "magenta");

            currentPlayer.Gold += stolen;
            currentPlayer.Darkness += darkGain;

            // Chance of angering spirits
            if (dungeonRandom.NextDouble() < 0.3)
            {
                terminal.WriteLine("");
                terminal.WriteLine("An angry spirit emerges from the ruined shrine!", "bright_red");
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
                    terminal.WriteLine("You awaken at the Temple of Light...");
                    await Task.Delay(2000);
                    await NavigateToLocation(GameLocation.Temple);
                    return;
                }
            }
        }
        else
        {
            terminal.WriteLine("You wisely leave the mysterious shrine alone.", "gray");
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
        terminal.SetColor("bright_magenta");
        terminal.WriteLine("╔══════════════════════════════════════════════════════════════════╗");
        terminal.WriteLine("║              THE FORGOTTEN SHRINE                                 ║");
        terminal.WriteLine("╚══════════════════════════════════════════════════════════════════╝");
        terminal.WriteLine("");
        await Task.Delay(1000);

        terminal.SetColor("white");
        terminal.WriteLine("Unlike the other shrines in this dungeon, this one feels different.");
        terminal.WriteLine("Older. Sadder. The air hums with faded divinity.");
        terminal.WriteLine("");
        await Task.Delay(1500);

        terminal.SetColor("gray");
        terminal.WriteLine("Before the altar kneels a woman.");
        terminal.WriteLine("Silver-streaked hair cascades past her shoulders.");
        terminal.WriteLine("Her eyes, when she turns to look at you, hold ancient sorrow.");
        terminal.WriteLine("");
        await Task.Delay(1500);

        terminal.SetColor("bright_cyan");
        terminal.WriteLine($"\"{lyris.DialogueHints[0]}\"");
        terminal.WriteLine("");
        await Task.Delay(2000);

        terminal.SetColor("white");
        terminal.WriteLine("She rises slowly, studying you with unnerving intensity.");
        terminal.WriteLine("");
        terminal.SetColor("cyan");
        terminal.WriteLine($"\"{lyris.DialogueHints[1]}\"");
        terminal.WriteLine("");
        await Task.Delay(2000);

        // Show her details
        terminal.SetColor("yellow");
        terminal.WriteLine($"This is {lyris.Name}, {lyris.Title}.");
        terminal.WriteLine($"Role: {lyris.CombatRole}");
        terminal.WriteLine($"Abilities: {string.Join(", ", lyris.Abilities)}");
        terminal.WriteLine("");

        terminal.SetColor("gray");
        terminal.WriteLine(lyris.BackstoryBrief);
        terminal.WriteLine("");
        await Task.Delay(1500);

        terminal.SetColor("bright_yellow");
        terminal.WriteLine("[R] Ask her to join you");
        terminal.WriteLine("[T] Talk more");
        terminal.WriteLine("[L] Leave her to her prayers");
        terminal.WriteLine("");

        var choice = await terminal.GetInput("Your choice: ");

        switch (choice.ToUpper())
        {
            case "R":
                bool success = await TryRecruitCompanionInDungeon(
                    UsurperRemake.Systems.CompanionId.Lyris, player);
                if (success)
                {
                    terminal.SetColor("bright_green");
                    terminal.WriteLine("");
                    terminal.WriteLine($"{lyris.Name} rises from the altar.");
                    terminal.WriteLine("\"Perhaps... this is what I was waiting for.\"");
                    terminal.WriteLine("");
                    terminal.SetColor("yellow");
                    terminal.WriteLine("WARNING: Companions can die permanently. Guard her well.");

                    // Generate news
                    NewsSystem.Instance.Newsy(false, $"{player.Name2} found {lyris.Name} at a forgotten shrine in the dungeon.");
                }
                break;

            case "T":
                terminal.WriteLine("");
                terminal.SetColor("cyan");
                terminal.WriteLine($"{lyris.Name} speaks of her past...");
                terminal.WriteLine("");
                terminal.SetColor("white");
                terminal.WriteLine(lyris.Description);
                terminal.WriteLine("");
                if (!string.IsNullOrEmpty(lyris.PersonalQuestDescription))
                {
                    terminal.SetColor("bright_magenta");
                    terminal.WriteLine($"Personal Quest: {lyris.PersonalQuestName}");
                    terminal.WriteLine($"\"{lyris.PersonalQuestDescription}\"");
                    terminal.WriteLine("");
                }
                terminal.SetColor("cyan");
                terminal.WriteLine($"\"{lyris.DialogueHints[2]}\"");
                terminal.WriteLine("");

                var followUp = await terminal.GetInput("Ask her to join you? (Y/N): ");
                if (followUp.ToUpper() == "Y")
                {
                    await TryRecruitCompanionInDungeon(
                        UsurperRemake.Systems.CompanionId.Lyris, player);
                }
                break;

            default:
                terminal.SetColor("gray");
                terminal.WriteLine("");
                terminal.WriteLine($"You nod to {lyris.Name} and continue on your way.");
                terminal.WriteLine("\"We will meet again,\" she whispers as you leave.");
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
        terminal.WriteLine("⚠ TRAP! ⚠");
        terminal.WriteLine("");

        var currentPlayer = GetCurrentPlayer();

        // Check for evasion based on agility
        if (TryEvadeTrap(currentPlayer))
        {
            terminal.SetColor("green");
            terminal.WriteLine("Your quick reflexes save you!");
            terminal.WriteLine($"You dodge the trap entirely! (Agility: {currentPlayer.Agility})");
            await Task.Delay(1500);
            return;
        }

        terminal.WriteLine("You couldn't react in time!", "yellow");
        await Task.Delay(300);

        var trapType = dungeonRandom.Next(5);

        switch (trapType)
        {
            case 0:
                terminal.WriteLine("The floor gives way beneath you!", "white");
                var fallDmg = currentDungeonLevel * 3 + dungeonRandom.Next(10);
                currentPlayer.HP -= fallDmg;
                terminal.WriteLine($"You fall into a pit and take {fallDmg} damage!", "red");
                break;
            case 1:
                terminal.WriteLine("Poison darts shoot from the walls!", "white");
                var dartDmg = currentDungeonLevel * 2 + dungeonRandom.Next(8);
                currentPlayer.HP -= dartDmg;
                currentPlayer.Poison = Math.Max(currentPlayer.Poison, 1);
                terminal.WriteLine($"You take {dartDmg} damage and are poisoned!", "magenta");
                break;
            case 2:
                terminal.WriteLine("A magical rune explodes beneath your feet!", "bright_magenta");
                var runeDmg = currentDungeonLevel * 5 + dungeonRandom.Next(15);
                currentPlayer.HP -= runeDmg;
                terminal.WriteLine($"You take {runeDmg} magical damage!", "red");
                break;
            case 3:
                terminal.WriteLine("A net falls from above, trapping you!", "white");
                terminal.WriteLine("You struggle free, but lose time...", "gray");
                // Could implement time/turn penalty here
                break;
            case 4:
                terminal.WriteLine("You trigger a tripwire!", "white");
                terminal.WriteLine("But nothing happens... the trap is broken.", "green");
                terminal.WriteLine("You find some gold hidden near the mechanism.", "yellow");
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
        terminal.WriteLine("📜 ANCIENT SCROLL 📜");
        terminal.WriteLine("");

        terminal.WriteLine("You discover an ancient scroll tucked into a wall crevice.", "white");
        terminal.WriteLine("Strange symbols glow faintly on the parchment.", "gray");

        await HandleMagicScroll();
    }

    /// <summary>
    /// Gambling ghost encounter - Risk/reward minigame
    /// </summary>
    private async Task GamblingGhostEncounter()
    {
        terminal.SetColor("bright_white");
        terminal.WriteLine("=== GAMBLING GHOST ===");
        terminal.WriteLine("");

        terminal.WriteLine("A spectral figure materializes before you!", "cyan");
        terminal.WriteLine("\"Greetings, mortal! Care for a game of chance?\"", "yellow");
        terminal.WriteLine("The ghost produces a pair of ethereal dice.", "gray");
        terminal.WriteLine("");

        var currentPlayer = GetCurrentPlayer();
        long minBet = 100;
        long maxBet = currentPlayer.Gold / 2;

        if (currentPlayer.Gold < minBet)
        {
            terminal.WriteLine("\"Bah! You have no gold worth gambling for!\"", "yellow");
            terminal.WriteLine("The ghost fades away in disappointment.", "gray");
            await Task.Delay(2000);
            return;
        }

        terminal.WriteLine($"\"Place your bet! (Minimum {minBet}, Maximum {maxBet})\"", "yellow");
        var betStr = await terminal.GetInput("Your bet (or 0 to decline): ");

        if (!long.TryParse(betStr, out long bet) || bet < minBet || bet > maxBet)
        {
            terminal.WriteLine("\"Coward! Perhaps next time...\"", "yellow");
            terminal.WriteLine("The ghost fades away.", "gray");
            await Task.Delay(2000);
            return;
        }

        terminal.WriteLine($"You bet {bet} gold!", "white");
        terminal.WriteLine("The ghost rolls the dice...", "gray");
        await Task.Delay(1500);

        var ghostRoll = dungeonRandom.Next(1, 7) + dungeonRandom.Next(1, 7);
        terminal.WriteLine($"Ghost rolls: {ghostRoll}", "cyan");

        terminal.WriteLine("Your turn to roll...", "gray");
        await Task.Delay(1000);

        var playerRoll = dungeonRandom.Next(1, 7) + dungeonRandom.Next(1, 7);
        terminal.WriteLine($"You roll: {playerRoll}", "yellow");

        if (playerRoll > ghostRoll)
        {
            terminal.SetColor("green");
            terminal.WriteLine("YOU WIN!");
            terminal.WriteLine($"The ghost begrudgingly pays you {bet} gold!", "yellow");
            currentPlayer.Gold += bet;
        }
        else if (playerRoll < ghostRoll)
        {
            terminal.SetColor("red");
            terminal.WriteLine("YOU LOSE!");
            terminal.WriteLine($"The ghost cackles as your gold vanishes!", "yellow");
            currentPlayer.Gold -= bet;
        }
        else
        {
            terminal.SetColor("yellow");
            terminal.WriteLine("TIE!");
            terminal.WriteLine("\"Interesting... keep your gold, mortal. Until next time!\"", "yellow");
        }

        terminal.WriteLine("The ghost fades into the shadows...", "gray");
        await Task.Delay(2500);
    }

    /// <summary>
    /// Potion cache encounter - find random potions
    /// </summary>
    private async Task PotionCacheEncounter()
    {
        terminal.SetColor("bright_green");
        terminal.WriteLine("✚ POTION CACHE ✚");
        terminal.WriteLine("");

        var currentPlayer = GetCurrentPlayer();

        // Random potion messages
        string[] messages = new[]
        {
            "You discover an abandoned healer's satchel!",
            "A fallen adventurer's pack contains healing supplies!",
            "You find a monk's abandoned cache of potions!",
            "A hidden alcove reveals a stash of healing elixirs!",
            "The corpse of a cleric clutches a bag of potions!"
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
            terminal.WriteLine("You already have the maximum number of potions!", "yellow");
            terminal.WriteLine("You leave the potions for another adventurer.", "gray");
        }
        else
        {
            int actualGained = Math.Min(potionsFound, roomAvailable);
            currentPlayer.Healing += actualGained;

            terminal.SetColor("green");
            terminal.WriteLine($"You collect {actualGained} healing potion{(actualGained > 1 ? "s" : "")}!");
            terminal.WriteLine($"Potions: {currentPlayer.Healing}/{currentPlayer.MaxPotions}", "cyan");

            if (actualGained < potionsFound)
            {
                terminal.WriteLine($"(You had to leave {potionsFound - actualGained} potion{(potionsFound - actualGained > 1 ? "s" : "")} behind - at maximum capacity)", "gray");
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
        terminal.SetColor("green");
        terminal.WriteLine("╔═══════════════════════════════════════════════════════╗");
        terminal.WriteLine("║            ♦ TRAVELING MERCHANT ♦                     ║");
        terminal.WriteLine("╚═══════════════════════════════════════════════════════╝");
        terminal.WriteLine("");
        terminal.SetColor("white");
        terminal.WriteLine("A traveling merchant appears from the shadows!");
        terminal.WriteLine("\"Greetings, brave adventurer! Care to trade?\"");
        terminal.WriteLine("");

        var choice = await terminal.GetInput("(T)rade with merchant or (A)ttack for goods? ");

        if (choice.ToUpper() == "T")
        {
            await MerchantTradeMenu(player);
        }
        else if (choice.ToUpper() == "A")
        {
            terminal.SetColor("red");
            terminal.WriteLine("You decide to rob the poor merchant!");
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
                terminal.WriteLine("You awaken at the Temple of Light...");
                await Task.Delay(2000);
                await NavigateToLocation(GameLocation.Temple);
                return;
            }

            if (result.Outcome == CombatOutcome.Victory)
            {
                // Loot the merchant
                long loot = currentDungeonLevel * 100 + dungeonRandom.Next(200);
                player.Gold += loot;
                player.Healing = Math.Min(player.MaxPotions, player.Healing + 3);
                terminal.SetColor("yellow");
                terminal.WriteLine($"You loot {loot} gold and 3 healing potions from the merchant!");
            }

            // Evil deed
            player.Darkness += 10;
            terminal.SetColor("red");
            terminal.WriteLine("+10 Darkness for attacking an innocent merchant!");
        }
        else
        {
            terminal.SetColor("gray");
            terminal.WriteLine("The merchant waves goodbye and vanishes into the shadows.");
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
            terminal.SetColor("green");
            terminal.WriteLine("╔═══════════════════════════════════════════════════════╗");
            terminal.WriteLine("║            MERCHANT'S WARES                           ║");
            terminal.WriteLine("╚═══════════════════════════════════════════════════════╝");
            terminal.WriteLine("");

            terminal.SetColor("yellow");
            terminal.WriteLine($"Your Gold: {player.Gold:N0}");
            terminal.WriteLine($"Your Potions: {player.Healing}/{player.MaxPotions}");
            terminal.SetColor("white");
            terminal.WriteLine($"Weapon Power: {player.WeapPow}  |  Armor Power: {player.ArmPow}");
            terminal.WriteLine("");

            terminal.SetColor("white");
            terminal.WriteLine("═══ SUPPLIES ═══");
            terminal.WriteLine("");

            terminal.SetColor("darkgray");
            terminal.Write("  [");
            terminal.SetColor("green");
            terminal.Write("1");
            terminal.SetColor("darkgray");
            terminal.Write("] ");
            terminal.SetColor("white");
            terminal.WriteLine($"Healing Potion ({potionPrice}g)");

            terminal.SetColor("darkgray");
            terminal.Write("  [");
            terminal.SetColor("bright_green");
            terminal.Write("2");
            terminal.SetColor("darkgray");
            terminal.Write("] ");
            terminal.SetColor("white");
            terminal.WriteLine($"Mega Potion ({megaPotionPrice}g) - Full heal!");

            terminal.SetColor("darkgray");
            terminal.Write("  [");
            terminal.SetColor("cyan");
            terminal.Write("3");
            terminal.SetColor("darkgray");
            terminal.Write("] ");
            terminal.SetColor("white");
            terminal.WriteLine($"Antidote ({antidotePrice}g) - Cures poison");

            terminal.SetColor("darkgray");
            terminal.Write("  [");
            terminal.SetColor("yellow");
            terminal.Write("4");
            terminal.SetColor("darkgray");
            terminal.Write("] ");
            terminal.SetColor("white");
            terminal.WriteLine($"Buy Max Potions ({potionPrice * (player.MaxPotions - player.Healing)}g)");

            terminal.WriteLine("");
            terminal.SetColor("bright_magenta");
            terminal.WriteLine("═══ RARE ITEMS (Dungeon Exclusive!) ═══");
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
            terminal.WriteLine("Leave shop");

            terminal.WriteLine("");
            terminal.SetColor("cyan");
            terminal.Write("Choice: ");
            terminal.SetColor("white");

            var choice = (await terminal.GetInput("")).Trim().ToUpper();

            switch (choice)
            {
                case "1":
                    if (player.Healing >= player.MaxPotions)
                    {
                        terminal.SetColor("yellow");
                        terminal.WriteLine("\"You can't carry any more potions, friend!\"");
                    }
                    else if (player.Gold >= potionPrice)
                    {
                        player.Gold -= potionPrice;
                        player.Healing++;
                        terminal.SetColor("green");
                        terminal.WriteLine($"Purchased 1 healing potion! ({player.Healing}/{player.MaxPotions})");
                    }
                    else
                    {
                        terminal.SetColor("red");
                        terminal.WriteLine("\"Not enough gold, friend.\"");
                    }
                    await Task.Delay(1500);
                    break;

                case "2":
                    if (player.Gold >= megaPotionPrice)
                    {
                        player.Gold -= megaPotionPrice;
                        player.HP = player.MaxHP;
                        terminal.SetColor("bright_green");
                        terminal.WriteLine("You drink the mega potion - FULL HEALTH RESTORED!");
                        terminal.WriteLine($"HP: {player.HP}/{player.MaxHP}");
                    }
                    else
                    {
                        terminal.SetColor("red");
                        terminal.WriteLine("\"Not enough gold, friend.\"");
                    }
                    await Task.Delay(1500);
                    break;

                case "3":
                    if (player.Poison <= 0)
                    {
                        terminal.SetColor("yellow");
                        terminal.WriteLine("\"You're not poisoned! Save your gold.\"");
                    }
                    else if (player.Gold >= antidotePrice)
                    {
                        player.Gold -= antidotePrice;
                        player.Poison = 0;
                        terminal.SetColor("green");
                        terminal.WriteLine("The poison drains from your body!");
                    }
                    else
                    {
                        terminal.SetColor("red");
                        terminal.WriteLine("\"Not enough gold, friend.\"");
                    }
                    await Task.Delay(1500);
                    break;

                case "4":
                    int potionsNeeded = player.MaxPotions - (int)player.Healing;
                    if (potionsNeeded <= 0)
                    {
                        terminal.SetColor("yellow");
                        terminal.WriteLine("\"You're already full on potions!\"");
                    }
                    else
                    {
                        long totalCost = potionsNeeded * potionPrice;
                        if (player.Gold >= totalCost)
                        {
                            player.Gold -= totalCost;
                            player.Healing = player.MaxPotions;
                            terminal.SetColor("green");
                            terminal.WriteLine($"Purchased {potionsNeeded} potions for {totalCost}g!");
                            terminal.WriteLine($"Potions: {player.Healing}/{player.MaxPotions}");
                        }
                        else
                        {
                            int canAfford = (int)(player.Gold / potionPrice);
                            if (canAfford > 0)
                            {
                                player.Gold -= canAfford * potionPrice;
                                player.Healing += canAfford;
                                terminal.SetColor("yellow");
                                terminal.WriteLine($"Could only afford {canAfford} potions.");
                                terminal.WriteLine($"Potions: {player.Healing}/{player.MaxPotions}");
                            }
                            else
                            {
                                terminal.SetColor("red");
                                terminal.WriteLine("\"Not enough gold, friend.\"");
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
                    terminal.WriteLine("\"Safe travels, adventurer!\"");
                    await Task.Delay(1000);
                    break;

                default:
                    terminal.SetColor("red");
                    terminal.WriteLine("Invalid choice.");
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
        public string Type { get; set; } = ""; // weapon, armor, ring, amulet, special
        public int Power { get; set; }
        public bool Sold { get; set; } = false;
        public Func<Character, Task>? EffectAsync { get; set; }
    }

    private List<MerchantRareItem> GenerateMerchantRareItems(int level)
    {
        var items = new List<MerchantRareItem>();
        int basePower = level + 5;
        long basePrice = level * 500;

        // Weapon options
        var weapons = new[]
        {
            ("Shadow Blade", "Strikes from darkness", basePower + 3),
            ("Flame Tongue", "Burns with eternal fire", basePower + 4),
            ("Frost Brand", "Chills to the bone", basePower + 4),
            ("Thunderclap", "Echoes with lightning", basePower + 5),
            ("Venom Fang", "Drips with poison", basePower + 3),
            ("Soul Reaver", "Drains life force", basePower + 6),
            ("Demon Slayer", "Bane of evil", basePower + 5),
            ("Dragon Tooth", "From an ancient wyrm", basePower + 7),
        };

        // Armor options
        var armors = new[]
        {
            ("Shadowmail", "Blends with darkness", basePower + 2),
            ("Dragonscale Vest", "Resistant to fire", basePower + 4),
            ("Mithril Chain", "Light as a feather", basePower + 3),
            ("Void Armor", "Absorbs magic", basePower + 5),
            ("Phoenix Plate", "Regenerates slowly", basePower + 4),
            ("Titan's Guard", "Immense protection", basePower + 6),
        };

        // Ring options
        var rings = new[]
        {
            ("Ring of Might", "+5 Strength", 5),
            ("Ring of Vitality", "+50 Max HP", 50),
            ("Ring of the Thief", "+5 Dexterity", 5),
            ("Ring of Wisdom", "+5 Intelligence", 5),
            ("Ring of Fortune", "+10% Gold Find", 10),
            ("Ring of Protection", "+3 Defense", 3),
        };

        // Amulet/Special options
        var specials = new[]
        {
            ("Amulet of Life", "+100 Max HP", 100),
            ("Charm of Speed", "+1 Attack per round", 1),
            ("Talisman of Power", "+10 All Stats", 10),
            ("Lucky Coin", "Extra gold from enemies", 0),
        };

        // Pick random items for this merchant
        var weaponChoice = weapons[dungeonRandom.Next(weapons.Length)];
        var weaponPower = weaponChoice.Item3;
        var weaponName = weaponChoice.Item1;
        var weaponDesc = weaponChoice.Item2;
        items.Add(new MerchantRareItem
        {
            Name = weaponName,
            Description = $"+{weaponPower} Weapon Power - {weaponDesc}",
            Price = basePrice + (weaponPower * 100),
            Type = "weapon",
            Power = weaponPower,
            EffectAsync = async (p) => {
                // Create actual equipment and equip it
                var weapon = Equipment.CreateWeapon(
                    id: 9000 + dungeonRandom.Next(1000),
                    name: weaponName,
                    handedness: WeaponHandedness.OneHanded,
                    weaponType: WeaponType.Sword,
                    power: weaponPower,
                    value: basePrice + (weaponPower * 100),
                    rarity: EquipmentRarity.Rare
                );
                weapon.Description = weaponDesc;
                EquipmentDatabase.RegisterDynamic(weapon);

                // For one-handed weapons, ask which slot to use
                EquipmentSlot? targetSlot = null;
                if (Character.RequiresSlotSelection(weapon))
                {
                    targetSlot = await PromptForWeaponSlotDungeon(p);
                }

                if (p.EquipItem(weapon, targetSlot, out string msg))
                {
                    terminal?.WriteLine(msg, "green");
                }
                else
                {
                    // Fallback: add power directly if equip fails
                    p.WeapPow += weaponPower;
                    terminal?.WriteLine($"Weapon power increased by {weaponPower}!", "yellow");
                }
            }
        });

        var armorChoice = armors[dungeonRandom.Next(armors.Length)];
        var armorPower = armorChoice.Item3;
        var armorName = armorChoice.Item1;
        var armorDesc = armorChoice.Item2;
        items.Add(new MerchantRareItem
        {
            Name = armorName,
            Description = $"+{armorPower} Armor Power - {armorDesc}",
            Price = basePrice + (armorPower * 80),
            Type = "armor",
            Power = armorPower,
            EffectAsync = async (p) => {
                // Create actual equipment and equip it
                var armor = Equipment.CreateArmor(
                    id: 9000 + dungeonRandom.Next(1000),
                    name: armorName,
                    slot: EquipmentSlot.Body,
                    armorType: ArmorType.Chain,
                    ac: armorPower,
                    value: basePrice + (armorPower * 80),
                    rarity: EquipmentRarity.Rare
                );
                armor.Description = armorDesc;
                EquipmentDatabase.RegisterDynamic(armor);
                if (p.EquipItem(armor, out string msg))
                {
                    terminal?.WriteLine(msg, "green");
                }
                else
                {
                    // Fallback: add power directly if equip fails
                    p.ArmPow += armorPower;
                    terminal?.WriteLine($"Armor power increased by {armorPower}!", "yellow");
                }
                await Task.CompletedTask; // Make method async
            }
        });

        var ringChoice = rings[dungeonRandom.Next(rings.Length)];
        var ringName = ringChoice.Item1;
        var ringDesc = ringChoice.Item2;
        var ringPower = ringChoice.Item3;
        var ringPrice = basePrice / 2 + (ringPower * 50);
        items.Add(new MerchantRareItem
        {
            Name = ringName,
            Description = ringDesc,
            Price = ringPrice,
            Type = "ring",
            Power = ringPower,
            EffectAsync = async (p) => {
                // Create actual ring equipment
                var ring = Equipment.CreateAccessory(
                    id: 9000 + dungeonRandom.Next(1000),
                    name: ringName,
                    slot: EquipmentSlot.LFinger,
                    value: ringPrice,
                    rarity: EquipmentRarity.Rare
                );
                ring.Description = ringDesc;

                // Apply appropriate bonuses based on ring type
                switch (ringName)
                {
                    case "Ring of Might":
                        ring = ring.WithStrength(5);
                        break;
                    case "Ring of Vitality":
                        ring = ring.WithMaxHP(50);
                        break;
                    case "Ring of the Thief":
                        ring = ring.WithDexterity(5);
                        break;
                    case "Ring of Wisdom":
                        ring = ring.WithWisdom(5);
                        break;
                    case "Ring of Fortune":
                        // Passive gold find effect - just equip it
                        break;
                    case "Ring of Protection":
                        ring = ring.WithDefence(3);
                        break;
                }

                EquipmentDatabase.RegisterDynamic(ring);
                if (p.EquipItem(ring, out string msg))
                {
                    terminal?.WriteLine(msg, "green");
                }
                else
                {
                    // Try RFinger slot if LFinger is occupied
                    ring.Slot = EquipmentSlot.RFinger;
                    if (p.EquipItem(ring, out string msg2))
                    {
                        terminal?.WriteLine(msg2, "green");
                    }
                    else
                    {
                        // Fallback: apply stats directly
                        switch (ringName)
                        {
                            case "Ring of Might": p.Strength += 5; break;
                            case "Ring of Vitality": p.MaxHP += 50; p.HP += 50; break;
                            case "Ring of the Thief": p.Dexterity += 5; break;
                            case "Ring of Wisdom": p.Intelligence += 5; break;
                            case "Ring of Protection": p.ArmPow += 3; break;
                        }
                        terminal?.WriteLine($"Ring power applied!", "yellow");
                    }
                }
                await Task.CompletedTask; // Make method async
            }
        });

        var specialChoice = specials[dungeonRandom.Next(specials.Length)];
        var specialName = specialChoice.Item1;
        var specialDesc = specialChoice.Item2;
        var specialPower = specialChoice.Item3;
        var specialPrice = basePrice + 1000;
        items.Add(new MerchantRareItem
        {
            Name = specialName,
            Description = specialDesc,
            Price = specialPrice,
            Type = "special",
            Power = specialPower,
            EffectAsync = async (p) => {
                // Create actual amulet/accessory equipment
                var amulet = Equipment.CreateAccessory(
                    id: 9000 + dungeonRandom.Next(1000),
                    name: specialName,
                    slot: EquipmentSlot.Neck,
                    value: specialPrice,
                    rarity: EquipmentRarity.Epic
                );
                amulet.Description = specialDesc;

                // Apply appropriate bonuses based on item type
                switch (specialName)
                {
                    case "Amulet of Life":
                        amulet = amulet.WithMaxHP(100);
                        break;
                    case "Charm of Speed":
                        amulet = amulet.WithDexterity(10).WithAgility(10);
                        break;
                    case "Talisman of Power":
                        amulet = amulet.WithStrength(10).WithIntelligence(10).WithWisdom(10)
                            .WithDexterity(10).WithConstitution(10).WithCharisma(10);
                        break;
                    case "Lucky Coin":
                        // Passive gold find effect - just equip it
                        break;
                }

                EquipmentDatabase.RegisterDynamic(amulet);
                if (p.EquipItem(amulet, out string msg))
                {
                    terminal?.WriteLine(msg, "green");
                }
                else
                {
                    // Fallback: apply stats directly
                    switch (specialName)
                    {
                        case "Amulet of Life": p.MaxHP += 100; p.HP += 100; break;
                        case "Charm of Speed": p.Dexterity += 10; p.Agility += 10; break;
                        case "Talisman of Power":
                            p.Strength += 10; p.Intelligence += 10; p.Wisdom += 10;
                            p.Dexterity += 10; p.Constitution += 10; p.Charisma += 10;
                            break;
                    }
                    terminal?.WriteLine($"Amulet power applied!", "yellow");
                }
                await Task.CompletedTask; // Make method async
            }
        });

        return items;
    }

    /// <summary>
    /// Prompt player to choose which hand to equip a one-handed weapon in (dungeon version)
    /// </summary>
    private async Task<EquipmentSlot?> PromptForWeaponSlotDungeon(Character player)
    {
        terminal.WriteLine("");
        terminal.SetColor("cyan");
        terminal.WriteLine("This is a one-handed weapon. Where would you like to equip it?");
        terminal.WriteLine("");

        // Show current equipment in both slots
        var mainHandItem = player.GetEquipment(EquipmentSlot.MainHand);
        var offHandItem = player.GetEquipment(EquipmentSlot.OffHand);

        terminal.SetColor("white");
        terminal.Write("  (M) Main Hand: ");
        if (mainHandItem != null)
        {
            terminal.SetColor("yellow");
            terminal.WriteLine(mainHandItem.Name);
        }
        else
        {
            terminal.SetColor("gray");
            terminal.WriteLine("Empty");
        }

        terminal.SetColor("white");
        terminal.Write("  (O) Off-Hand:  ");
        if (offHandItem != null)
        {
            terminal.SetColor("yellow");
            terminal.WriteLine(offHandItem.Name);
        }
        else
        {
            terminal.SetColor("gray");
            terminal.WriteLine("Empty");
        }

        terminal.WriteLine("");

        terminal.Write("Your choice: ");
        var slotChoice = await terminal.GetInput("");

        return slotChoice.ToUpper() switch
        {
            "M" => EquipmentSlot.MainHand,
            "O" => EquipmentSlot.OffHand,
            _ => EquipmentSlot.MainHand // Default to main hand
        };
    }

    private async Task PurchaseRareItem(Character player, MerchantRareItem item)
    {
        if (item.Sold)
        {
            terminal.SetColor("gray");
            terminal.WriteLine("\"That item has already been sold, friend.\"");
            await Task.Delay(1500);
            return;
        }

        if (player.Gold < item.Price)
        {
            terminal.SetColor("red");
            terminal.WriteLine($"\"You need {item.Price:N0} gold for the {item.Name}.\"");
            terminal.WriteLine($"\"Come back when you have more coin!\"");
            await Task.Delay(2000);
            return;
        }

        terminal.SetColor("cyan");
        terminal.WriteLine($"Purchase {item.Name} for {item.Price:N0} gold? (Y/N)");
        var confirm = (await terminal.GetInput("")).Trim().ToUpper();

        if (confirm == "Y")
        {
            player.Gold -= item.Price;
            item.Sold = true;
            if (item.EffectAsync != null)
                await item.EffectAsync(player);

            terminal.SetColor("bright_yellow");
            terminal.WriteLine("");
            terminal.WriteLine("═══════════════════════════════════════");
            terminal.WriteLine($"  * ACQUIRED: {item.Name.ToUpper()} *");
            terminal.WriteLine("═══════════════════════════════════════");
            terminal.SetColor("green");
            terminal.WriteLine($"{item.Description}");
            terminal.WriteLine("");
            terminal.SetColor("gray");
            terminal.WriteLine("\"A fine choice! Use it well.\"");
        }
        else
        {
            terminal.SetColor("gray");
            terminal.WriteLine("\"Perhaps another time.\"");
        }

        await Task.Delay(2000);
    }
    
    /// <summary>
    /// Witch Doctor encounter - Pascal DUNGEV2.PAS
    /// </summary>
    private async Task WitchDoctorEncounter()
    {
        terminal.SetColor("magenta");
        terminal.WriteLine("=== WITCH DOCTOR ENCOUNTER ===");
        terminal.WriteLine("");
        
        var currentPlayer = GetCurrentPlayer();
        long cost = currentPlayer.Level * 12500;
        
        if (currentPlayer.Gold >= cost)
        {
            terminal.WriteLine("You meet the evil Witch-Doctor Mbluta!");
            terminal.WriteLine($"He demands {cost} gold or he will curse you!");
            terminal.WriteLine("");
            
            var choice = await terminal.GetInput("(P)ay the witch doctor or (R)un away? ");
            
            if (choice.ToUpper() == "P")
            {
                currentPlayer.Gold -= cost;
                terminal.WriteLine("You reluctantly pay the witch doctor.", "yellow");
                terminal.WriteLine("He vanishes into the darkness...");
            }
            else
            {
                // 50% chance to escape
                if (dungeonRandom.Next(2) == 0)
                {
                    terminal.WriteLine("You manage to flee the evil witch doctor!", "green");
                }
                else
                {
                    terminal.WriteLine("You fail to escape and are cursed!", "red");
                    
                    // Random curse effect
                    var curseType = dungeonRandom.Next(3);
                    switch (curseType)
                    {
                        case 0:
                            var expLoss = currentPlayer.Level * 1500;
                            currentPlayer.Experience = Math.Max(0, currentPlayer.Experience - expLoss);
                            terminal.WriteLine($"You lose {expLoss} experience points!");
                            break;
                        case 1:
                            var fightLoss = dungeonRandom.Next(5) + 1;
                            currentPlayer.Fights = Math.Max(0, currentPlayer.Fights - fightLoss);
                            terminal.WriteLine($"You lose {fightLoss} dungeon fights!");
                            break;
                        case 2:
                            var pfightLoss = dungeonRandom.Next(3) + 1;
                            currentPlayer.PFights = Math.Max(0, currentPlayer.PFights - pfightLoss);
                            terminal.WriteLine($"You lose {pfightLoss} player fights!");
                            break;
                    }
                }
            }
        }
        else
        {
            terminal.WriteLine("A witch doctor appears but sees you have no gold and leaves.", "gray");
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
        if (lowerName == "wolf") return name.Substring(0, name.Length - 4) + "olves";
        if (lowerName.EndsWith("wolf")) return name.Substring(0, name.Length - 4) + "olves";
        if (lowerName == "thief") return name.Substring(0, name.Length - 4) + "ieves";
        if (lowerName.EndsWith("thief")) return name.Substring(0, name.Length - 4) + "ieves";
        if (lowerName == "elf") return name.Substring(0, name.Length - 3) + "lves";
        if (lowerName.EndsWith("elf")) return name.Substring(0, name.Length - 3) + "lves";
        if (lowerName == "dwarf") return name + "s"; // Dwarfs or Dwarves both acceptable
        if (lowerName == "man") return name.Substring(0, name.Length - 3) + "en";
        if (lowerName.EndsWith("man")) return name.Substring(0, name.Length - 3) + "en";

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
            "You cautiously advance through the shadowy corridors...",
            "Your footsteps echo in the ancient stone passages...",
            "Flickering torchlight reveals mysterious doorways ahead...",
            "The air grows colder as you venture deeper into the dungeon...",
            "Strange sounds echo from the darkness beyond..."
        };
        
        terminal.WriteLine(explorationTexts[dungeonRandom.Next(explorationTexts.Length)], "gray");
        await Task.Delay(2000);
    }
    
    private string GetTerrainDescription(DungeonTerrain terrain)
    {
        return terrain switch
        {
            DungeonTerrain.Underground => "Ancient Underground Tunnels",
            DungeonTerrain.Mountains => "Rocky Mountain Passages",
            DungeonTerrain.Desert => "Desert Ruins and Tombs",
            DungeonTerrain.Forest => "Overgrown Forest Caves",
            DungeonTerrain.Caves => "Deep Natural Caverns",
            _ => "Unknown Territory"
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
            terminal.WriteLine("A powerful presence blocks your descent.", "bright_red");
            terminal.WriteLine("You must defeat the Old God on this floor before descending deeper.", "yellow");
            terminal.WriteLine($"({GetRemainingClearInfo()})", "gray");
            await Task.Delay(2500);
            return;
        }

        // Check if player can descend (limited to player level + 10)
        if (currentDungeonLevel >= maxAccessible)
        {
            terminal.WriteLine($"You cannot venture deeper than level {maxAccessible} at your current strength.", "yellow");
            terminal.WriteLine("Level up to access deeper floors.", "gray");
        }
        else if (currentDungeonLevel < maxDungeonLevel)
        {
            int nextLevel = currentDungeonLevel + 1;
            var floorResult = GenerateOrRestoreFloor(player, nextLevel);
            currentFloor = floorResult.Floor;
            currentDungeonLevel = nextLevel;
            terminal.WriteLine($"You descend to dungeon level {currentDungeonLevel}.", "yellow");

            // Update quest progress for reaching this floor
            if (player != null)
            {
                QuestSystem.OnDungeonFloorReached(player, currentDungeonLevel);
            }
        }
        else
        {
            terminal.WriteLine("You have reached the deepest level of the dungeon.", "red");
        }
        await Task.Delay(1500);
    }

    private async Task AscendToSurface()
    {
        // No level restrictions for ascending
        if (currentDungeonLevel > 1)
        {
            currentDungeonLevel--;
            terminal.WriteLine($"You ascend to dungeon level {currentDungeonLevel}.", "green");
        }
        else
        {
            await NavigateToLocation(GameLocation.MainStreet);
        }
        await Task.Delay(1500);
    }
    
    private async Task ManageTeam()
    {
        var player = GetCurrentPlayer();

        terminal.ClearScreen();
        terminal.SetColor("bright_cyan");
        terminal.WriteLine("╔═══════════════════════════════════════════════════╗");
        terminal.WriteLine("║              PARTY MANAGEMENT                     ║");
        terminal.WriteLine("╚═══════════════════════════════════════════════════╝");
        terminal.WriteLine("");

        // Check if player has any potential party members (team, spouse, or companions)
        bool hasTeam = !string.IsNullOrEmpty(player.Team);
        bool hasSpouse = UsurperRemake.Systems.RomanceTracker.Instance?.IsMarried == true;
        var companionSystem = UsurperRemake.Systems.CompanionSystem.Instance;
        bool hasAnyCompanions = companionSystem?.GetRecruitedCompanions()?.Any() == true;

        if (!hasTeam && !hasSpouse && !hasAnyCompanions && teammates.Count == 0)
        {
            terminal.SetColor("yellow");
            terminal.WriteLine("You have no one to bring with you.");
            terminal.WriteLine("Get married, recruit companions, or join a team!");
            terminal.WriteLine("");
            await terminal.PressAnyKey();
            return;
        }

        terminal.SetColor("white");
        if (hasTeam)
        {
            terminal.WriteLine($"Team: {player.Team}");
            terminal.WriteLine($"Team controls turf: {(player.CTurf ? "Yes" : "No")}");
        }
        else
        {
            terminal.WriteLine("You're not in a team, but your loved ones can join you.");
        }
        terminal.WriteLine("");

        // Show current dungeon party
        terminal.SetColor("cyan");
        terminal.WriteLine("Current Dungeon Party:");
        terminal.WriteLine($"  1. {player.DisplayName} (You) - Level {player.Level} {player.Class}");
        for (int i = 0; i < teammates.Count; i++)
        {
            var tm = teammates[i];
            string status = tm.IsAlive ? $"HP: {tm.HP}/{tm.MaxHP}" : "[INJURED]";
            terminal.WriteLine($"  {i + 2}. {tm.DisplayName} - Level {tm.Level} {tm.Class} - {status}");
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
            terminal.WriteLine("Available Companions (on standby):");
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
            terminal.WriteLine("Available Allies (not in dungeon party):");
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
                    terminal.Write($"  [{i + 1}] <3 {npc.DisplayName} (Spouse) - Level {npc.Level} {npc.Class} - HP: {npc.HP}/{npc.MaxHP}");
                    if (fee > 0) { terminal.SetColor("yellow"); terminal.Write(feeStr); }
                    terminal.WriteLine("");
                    terminal.SetColor("green");
                }
                else if (isLover)
                {
                    terminal.SetColor("magenta");
                    terminal.Write($"  [{i + 1}] <3 {npc.DisplayName} (Lover) - Level {npc.Level} {npc.Class} - HP: {npc.HP}/{npc.MaxHP}");
                    if (fee > 0) { terminal.SetColor("yellow"); terminal.Write(feeStr); }
                    terminal.WriteLine("");
                    terminal.SetColor("green");
                }
                else
                {
                    terminal.Write($"  [{i + 1}] {npc.DisplayName} - Level {npc.Level} {npc.Class} - HP: {npc.HP}/{npc.MaxHP}");
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
            terminal.WriteLine("No allies available to join your dungeon party.");
            terminal.WriteLine("");
        }

        // Show options
        terminal.SetColor("white");
        terminal.WriteLine("Options:");
        if (inactiveCompanions.Count > 0 && teammates.Count < 4)
        {
            terminal.WriteLine("  [C1-C" + inactiveCompanions.Count + "] Add companion to party");
        }
        if (npcTeammates.Count > 0 && teammates.Count < 4) // Max 4 teammates + player = 5
        {
            terminal.WriteLine("  [A]dd ally to dungeon party");
        }
        if (teammates.Count > 0)
        {
            terminal.WriteLine("  [R]emove ally from party");
        }
        terminal.WriteLine("  [B]ack to dungeon menu");
        terminal.WriteLine("");

        var choice = await terminal.GetInput("Choice: ");
        choice = choice.ToUpper().Trim();

        // Handle companion add (C1, C2, etc.)
        if (choice.StartsWith("C") && choice.Length >= 2)
        {
            if (int.TryParse(choice.Substring(1), out int compIndex) && compIndex >= 1 && compIndex <= inactiveCompanions.Count)
            {
                if (teammates.Count >= 4)
                {
                    terminal.WriteLine("Your dungeon party is full (max 4 teammates)!", "yellow");
                    await Task.Delay(1500);
                }
                else
                {
                    await AddCompanionToParty(inactiveCompanions[compIndex - 1]);
                }
            }
            else
            {
                terminal.WriteLine("Invalid companion selection.", "red");
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
                    terminal.WriteLine("Your dungeon party is full (max 4 teammates)!", "yellow");
                    await Task.Delay(1500);
                }
                else
                {
                    terminal.WriteLine("No team members available to add.", "gray");
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
                    terminal.WriteLine("No teammates to remove.", "gray");
                    await Task.Delay(1500);
                }
                break;

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
            terminal.WriteLine($"{companion.Name} rejoins your party!");
            terminal.SetColor("gray");
            terminal.WriteLine($"Role: {companion.CombatRole}");
        }
        else
        {
            terminal.SetColor("red");
            terminal.WriteLine($"Could not add {companion.Name} to the party.");
        }

        await Task.Delay(1500);
    }

    private async Task AddTeammateToParty(List<NPC> available)
    {
        var player = GetCurrentPlayer();
        terminal.WriteLine("");
        terminal.SetColor("white");
        terminal.Write("Enter number of team member to add (1-");
        terminal.Write($"{available.Count}");
        terminal.Write("): ");
        var input = await terminal.GetInput("");

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
                terminal.WriteLine($"{npc.DisplayName} is {levelGap} levels higher than you.");
                terminal.WriteLine($"They demand {fee:N0} gold to join you in the dungeon.");
                terminal.SetColor("gray");
                terminal.WriteLine($"Your gold: {player.Gold:N0}");

                if (player.Gold < fee)
                {
                    terminal.SetColor("red");
                    terminal.WriteLine("You cannot afford this fee!");
                    await Task.Delay(2000);
                    return;
                }

                terminal.WriteLine("");
                terminal.SetColor("cyan");
                var confirm = await terminal.GetInput($"Pay {fee:N0} gold? (Y/N): ");
                if (!confirm.ToUpper().StartsWith("Y"))
                {
                    terminal.SetColor("gray");
                    terminal.WriteLine($"{npc.DisplayName} shrugs and stays behind.");
                    await Task.Delay(1500);
                    return;
                }

                // Deduct fee
                player.Gold -= fee;
                terminal.SetColor("green");
                terminal.WriteLine($"Paid {fee:N0} gold.");
            }

            teammates.Add(npc);

            // Move NPC to dungeon
            npc.UpdateLocation("Dungeon");

            // Sync to GameEngine for persistence
            SyncNPCTeammatesToGameEngine();

            terminal.SetColor("green");
            terminal.WriteLine($"{npc.DisplayName} joins your dungeon party!");
            terminal.WriteLine("They will fight alongside you against monsters.");

            // Show XP penalty warning if applicable
            float xpMult = balanceSystem.CalculateXPMultiplier(player, teammates);
            if (xpMult < 1.0f)
            {
                terminal.SetColor("yellow");
                terminal.WriteLine($"Warning: XP penalty active ({(int)(xpMult * 100)}% rate due to high-level ally)");
            }
            else
            {
                // 15% team XP/gold bonus for having teammates
                terminal.SetColor("cyan");
                terminal.WriteLine("Team bonus: +15% XP and gold from battles!");
            }
        }
        else
        {
            terminal.WriteLine("Invalid selection.", "red");
        }
        await Task.Delay(2000);
    }

    private async Task RemoveTeammateFromParty()
    {
        terminal.WriteLine("");
        terminal.SetColor("white");
        // Party list shows player as #1, so teammates are #2 onwards
        // Ask for 2-N to match the displayed party numbers
        terminal.Write("Enter party number to remove (2-");
        terminal.Write($"{teammates.Count + 1}");
        terminal.Write("): ");
        var input = await terminal.GetInput("");

        // Convert from party number (2-based) to teammates index (0-based)
        // Party #2 = teammates[0], Party #3 = teammates[1], etc.
        if (int.TryParse(input, out int partyNumber) && partyNumber >= 2 && partyNumber <= teammates.Count + 1)
        {
            int index = partyNumber - 2;
            var member = teammates[index];
            teammates.RemoveAt(index);

            // Handle companion removal - put them on standby, not "return to town"
            if (member.IsCompanion && member.CompanionId.HasValue)
            {
                var companionSystem = UsurperRemake.Systems.CompanionSystem.Instance;
                companionSystem.DeactivateCompanion(member.CompanionId.Value);

                terminal.SetColor("yellow");
                terminal.WriteLine($"{member.DisplayName} steps back from the front lines.");
                terminal.SetColor("gray");
                terminal.WriteLine("(They remain available - use Party Management to bring them back)");
            }
            else if (member is NPC npc)
            {
                // Move NPC back to town
                npc.UpdateLocation("Main Street");
                terminal.SetColor("yellow");
                terminal.WriteLine($"{member.DisplayName} leaves the dungeon party and returns to town.");
            }
            else
            {
                terminal.SetColor("yellow");
                terminal.WriteLine($"{member.DisplayName} leaves the dungeon party.");
            }

            // Sync to GameEngine for persistence
            SyncNPCTeammatesToGameEngine();
        }
        else
        {
            terminal.WriteLine("Invalid selection.", "red");
        }
        await Task.Delay(1500);
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
            terminal.WriteLine("Your party is full (max 4 allies)!");
            terminal.WriteLine("");
            terminal.SetColor("white");
            terminal.WriteLine($"{companion.Name} would like to join you, but you need to make room first.");
            terminal.WriteLine("");

            // Show current party members
            terminal.SetColor("cyan");
            terminal.WriteLine("Current party members:");
            for (int i = 0; i < teammates.Count; i++)
            {
                var tm = teammates[i];
                string type = tm.IsCompanion ? "[Companion]" : "[Ally]";
                terminal.WriteLine($"  {i + 1}. {tm.DisplayName} - Level {tm.Level} {type}");
            }
            terminal.WriteLine("");

            terminal.SetColor("bright_yellow");
            terminal.WriteLine("[R] Remove someone to make room");
            terminal.WriteLine("[C] Cancel recruitment");
            terminal.WriteLine("");

            var removeChoice = await terminal.GetInput("Your choice: ");

            if (removeChoice.ToUpper() == "R")
            {
                terminal.WriteLine("");
                terminal.SetColor("white");
                terminal.WriteLine("Who should leave the party?");
                terminal.WriteLine("(Companions can be re-added anytime from Party Management)");
                terminal.WriteLine("");

                var removeInput = await terminal.GetInput($"Enter number (1-{teammates.Count}): ");

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
                    terminal.WriteLine($"{memberToRemove.DisplayName} leaves the party.");
                    await Task.Delay(1000);
                }
                else
                {
                    terminal.WriteLine("Invalid selection. Recruitment cancelled.", "red");
                    await Task.Delay(1500);
                    return false;
                }
            }
            else
            {
                terminal.SetColor("gray");
                terminal.WriteLine("Recruitment cancelled.");
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
            terminal.SetColor("cyan");
            terminal.WriteLine("╔═══════════════════════════════════════════════════════╗");
            terminal.WriteLine("║                    POTIONS MENU                       ║");
            terminal.WriteLine("╚═══════════════════════════════════════════════════════╝");
            terminal.WriteLine("");

            // Show player status
            terminal.SetColor("bright_white");
            terminal.WriteLine("═══ YOUR STATUS ═══");
            terminal.SetColor("white");
            terminal.Write("HP: ");
            DrawBar(player.HP, player.MaxHP, 25, "red", "darkgray");
            terminal.WriteLine($" {player.HP}/{player.MaxHP}");

            terminal.SetColor("yellow");
            terminal.WriteLine($"Healing Potions: {player.Healing}/{player.MaxPotions}");
            terminal.WriteLine($"Gold: {player.Gold:N0}");
            terminal.WriteLine("");

            // Show teammate status if we have party members
            if (allPartyMembers.Count > 0)
            {
                terminal.SetColor("bright_cyan");
                terminal.WriteLine("═══ TEAM STATUS ═══");
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
            terminal.WriteLine("Options:");
            terminal.WriteLine("");

            // Use potion option
            if (player.Healing > 0)
            {
                terminal.SetColor("darkgray");
                terminal.Write("  [");
                terminal.SetColor("green");
                terminal.Write("U");
                terminal.SetColor("darkgray");
                terminal.Write("] ");
                terminal.SetColor("white");
                terminal.WriteLine($"Use Healing Potion on yourself (heals ~{healAmount} HP)");
            }
            else
            {
                terminal.SetColor("darkgray");
                terminal.WriteLine("  [U] Use Healing Potion - NO POTIONS!");
            }

            // Buy potions option
            int costPerPotion = 50 + (player.Level * 10);
            terminal.SetColor("darkgray");
            terminal.Write("  [");
            terminal.SetColor("yellow");
            terminal.Write("B");
            terminal.SetColor("darkgray");
            terminal.Write("] ");
            terminal.SetColor("white");
            terminal.WriteLine($"Buy Potions from Monk ({costPerPotion}g each)");

            // Quick heal - use potions until full
            if (player.Healing > 0 && player.HP < player.MaxHP)
            {
                terminal.SetColor("darkgray");
                terminal.Write("  [");
                terminal.SetColor("bright_green");
                terminal.Write("H");
                terminal.SetColor("darkgray");
                terminal.Write("] ");
                terminal.SetColor("white");
                terminal.WriteLine("Heal yourself to Full (use multiple potions)");
            }

            // Heal teammate option
            if (allPartyMembers.Count > 0 && player.Healing > 0)
            {
                terminal.SetColor("darkgray");
                terminal.Write("  [");
                terminal.SetColor("bright_cyan");
                terminal.Write("T");
                terminal.SetColor("darkgray");
                terminal.Write("] ");
                terminal.SetColor("white");
                terminal.WriteLine("Heal a Teammate");
            }

            // Heal entire party option
            bool anyTeammateInjured = allPartyMembers.Any(c => c.HP < c.MaxHP);
            if (allPartyMembers.Count > 0 && player.Healing > 0 && (player.HP < player.MaxHP || anyTeammateInjured))
            {
                terminal.SetColor("darkgray");
                terminal.Write("  [");
                terminal.SetColor("bright_magenta");
                terminal.Write("A");
                terminal.SetColor("darkgray");
                terminal.Write("] ");
                terminal.SetColor("white");
                terminal.WriteLine("Heal ALL Party Members to Full");
            }

            terminal.WriteLine("");
            terminal.SetColor("darkgray");
            terminal.Write("  [");
            terminal.SetColor("red");
            terminal.Write("Q");
            terminal.SetColor("darkgray");
            terminal.Write("] ");
            terminal.SetColor("white");
            terminal.WriteLine("Return to Dungeon");

            terminal.WriteLine("");
            terminal.SetColor("cyan");
            terminal.Write("Choice: ");
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
                        terminal.WriteLine("You don't have any healing potions!");
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

                case "Q":
                case "":
                    return;

                default:
                    terminal.SetColor("red");
                    terminal.WriteLine("Invalid choice.");
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
        terminal.WriteLine("Select teammate to heal:");
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
        terminal.SetColor("gray");
        terminal.WriteLine("  [0] Cancel");
        terminal.WriteLine("");

        terminal.SetColor("white");
        terminal.Write("Choose: ");
        var input = await terminal.GetInput("");

        if (!int.TryParse(input, out int targetChoice) || targetChoice == 0)
        {
            return;
        }

        if (targetChoice < 1 || targetChoice > companions.Count)
        {
            terminal.WriteLine("Invalid choice.", "red");
            await Task.Delay(1000);
            return;
        }

        var target = companions[targetChoice - 1];

        if (target.HP >= target.MaxHP)
        {
            terminal.WriteLine($"{target.DisplayName} is already at full health!", "yellow");
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
        terminal.WriteLine($"{target.DisplayName} is missing {missingHP} HP.");
        terminal.WriteLine($"Each potion heals approximately {healPerPotion} HP.");
        terminal.WriteLine("");
        terminal.SetColor("white");
        terminal.WriteLine($"[1] Use 1 potion");
        if (potionsNeeded > 1)
        {
            terminal.WriteLine($"[F] Fully heal (uses up to {potionsNeeded} potions)");
        }
        terminal.SetColor("gray");
        terminal.WriteLine("[0] Cancel");
        terminal.WriteLine("");

        terminal.SetColor("white");
        terminal.Write("Choice: ");
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
            terminal.WriteLine("Invalid choice.", "red");
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
            terminal.WriteLine($"You give a healing potion to {target.DisplayName}!");
        }
        else
        {
            terminal.WriteLine($"You give {potionsToUse} healing potions to {target.DisplayName}!");
        }
        terminal.WriteLine($"{target.DisplayName} recovers {totalHeal} HP!");

        if (target.HP >= target.MaxHP)
        {
            terminal.WriteLine($"{target.DisplayName} is fully healed!", "bright_green");
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
            terminal.WriteLine("Everyone is already at full health!", "yellow");
            await Task.Delay(1500);
            return;
        }

        int potionsAvailable = (int)player.Healing;
        int potionsToUse = Math.Min(totalPotionsNeeded, potionsAvailable);

        terminal.WriteLine("");
        terminal.SetColor("bright_cyan");
        terminal.WriteLine($"Healing entire party requires approximately {totalPotionsNeeded} potions.");
        terminal.WriteLine($"You have {potionsAvailable} potions.");
        terminal.WriteLine("");

        if (potionsAvailable < totalPotionsNeeded)
        {
            terminal.SetColor("yellow");
            terminal.WriteLine($"Warning: Not enough potions for full heal. Will use all {potionsAvailable}.");
            terminal.WriteLine("");
        }

        terminal.SetColor("white");
        terminal.WriteLine($"[Y] Yes, heal the party (uses {potionsToUse} potions)");
        terminal.SetColor("gray");
        terminal.WriteLine("[N] Cancel");
        terminal.WriteLine("");

        terminal.SetColor("white");
        terminal.Write("Choice: ");
        var choice = (await terminal.GetInput("")).Trim().ToUpper();

        if (choice != "Y")
        {
            return;
        }

        terminal.WriteLine("");
        terminal.SetColor("bright_green");
        terminal.WriteLine("Distributing potions to the party...");
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
            terminal.WriteLine($"  You recover {healed} HP!");
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
                terminal.WriteLine($"  {companion.DisplayName} recovers {healed} HP!");

                // Sync companion HP
                if (companion.IsCompanion && companion.CompanionId.HasValue)
                {
                    UsurperRemake.Systems.CompanionSystem.Instance.SyncCompanionHP(companion);
                }
            }
        }

        terminal.WriteLine("");
        terminal.SetColor("bright_green");
        terminal.WriteLine($"Used {totalPotionsUsed} potions. Total HP restored: {totalHealing}");
        terminal.SetColor("gray");
        terminal.WriteLine($"Potions remaining: {player.Healing}/{player.MaxPotions}");

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
            terminal.WriteLine("You don't have any healing potions!");
            await Task.Delay(1500);
            return;
        }

        if (player.HP >= player.MaxHP)
        {
            terminal.SetColor("yellow");
            terminal.WriteLine("You're already at full health!");
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
        terminal.WriteLine($"You drink a healing potion and recover {actualHeal} HP!");
        terminal.Write("HP: ");
        DrawBar(player.HP, player.MaxHP, 25, "red", "darkgray");
        terminal.WriteLine($" {player.HP}/{player.MaxHP}");
        terminal.SetColor("gray");
        terminal.WriteLine($"Potions remaining: {player.Healing}/{player.MaxPotions}");

        await Task.Delay(2000);
    }

    private async Task HealToFull(Character player)
    {
        if (player.Healing <= 0)
        {
            terminal.SetColor("red");
            terminal.WriteLine("You don't have any healing potions!");
            await Task.Delay(1500);
            return;
        }

        if (player.HP >= player.MaxHP)
        {
            terminal.SetColor("yellow");
            terminal.WriteLine("You're already at full health!");
            await Task.Delay(1500);
            return;
        }

        long healAmount = player.MaxHP / 4;
        int potionsNeeded = (int)Math.Ceiling((double)(player.MaxHP - player.HP) / healAmount);
        int potionsToUse = Math.Min(potionsNeeded, (int)player.Healing);

        terminal.SetColor("cyan");
        terminal.WriteLine($"This will use {potionsToUse} potion(s). Continue? (Y/N)");
        string confirm = (await terminal.GetInput("")).Trim().ToUpper();

        if (confirm != "Y")
        {
            terminal.SetColor("gray");
            terminal.WriteLine("Cancelled.");
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
        terminal.WriteLine($"You drink {actualPotionsUsed} healing potion(s) and recover {actualHeal} HP!");
        terminal.Write("HP: ");
        DrawBar(player.HP, player.MaxHP, 25, "red", "darkgray");
        terminal.WriteLine($" {player.HP}/{player.MaxHP}");
        terminal.SetColor("gray");
        terminal.WriteLine($"Potions remaining: {player.Healing}/{player.MaxPotions}");

        await Task.Delay(2000);
    }

    private async Task BuyPotionsFromMonk(Character player)
    {
        terminal.ClearScreen();
        terminal.SetColor("cyan");
        terminal.WriteLine("");
        terminal.WriteLine("A wandering monk materializes from the shadows...");
        terminal.WriteLine("");
        terminal.SetColor("white");
        terminal.WriteLine("\"Greetings, traveler. I sense you need healing supplies.\"");
        terminal.WriteLine($"\"You carry {player.Healing} of {player.MaxPotions} potions.\"");
        terminal.WriteLine("");

        // Calculate cost per potion (scales with level)
        int costPerPotion = 50 + (player.Level * 10);

        terminal.SetColor("yellow");
        terminal.WriteLine($"Price: {costPerPotion} gold per potion");
        terminal.WriteLine($"Your gold: {player.Gold:N0}");
        terminal.WriteLine("");

        // Calculate max potions player can buy
        int roomForPotions = player.MaxPotions - (int)player.Healing;
        int maxAffordable = (int)(player.Gold / costPerPotion);
        int maxCanBuy = Math.Min(roomForPotions, maxAffordable);

        if (maxCanBuy <= 0)
        {
            if (roomForPotions <= 0)
            {
                terminal.SetColor("yellow");
                terminal.WriteLine("\"You already carry all the potions you can hold!\"");
            }
            else
            {
                terminal.SetColor("red");
                terminal.WriteLine("\"I'm afraid you lack the gold, my friend.\"");
            }
            await terminal.PressAnyKey();
            return;
        }

        terminal.SetColor("cyan");
        terminal.WriteLine($"How many potions would you like? (Max: {maxCanBuy}, 0 to cancel)");
        terminal.Write("> ");
        terminal.SetColor("white");

        var amountInput = await terminal.GetInput("");

        if (!int.TryParse(amountInput.Trim(), out int amount) || amount < 1)
        {
            terminal.SetColor("gray");
            terminal.WriteLine("\"Perhaps another time, then.\"");
            await Task.Delay(1500);
            return;
        }

        if (amount > maxCanBuy)
        {
            terminal.SetColor("yellow");
            terminal.WriteLine($"\"I can only provide you with {maxCanBuy} potions.\"");
            amount = maxCanBuy;
        }

        // Complete the purchase
        long totalCost = amount * costPerPotion;
        player.Gold -= totalCost;
        player.Healing += amount;

        terminal.WriteLine("");
        terminal.SetColor("bright_green");
        terminal.WriteLine($"You purchase {amount} healing potion{(amount > 1 ? "s" : "")} for {totalCost:N0} gold.");
        terminal.SetColor("cyan");
        terminal.WriteLine($"Potions: {player.Healing}/{player.MaxPotions}");
        terminal.SetColor("yellow");
        terminal.WriteLine($"Gold remaining: {player.Gold:N0}");

        terminal.WriteLine("");
        terminal.SetColor("gray");
        terminal.WriteLine("The monk bows and fades back into the shadows...");
        await Task.Delay(2000);
    }
    
    private async Task ShowDungeonMap()
    {
        if (currentFloor == null)
        {
            terminal.WriteLine("No floor to map.", "gray");
            await Task.Delay(1500);
            return;
        }

        terminal.ClearScreen();
        terminal.SetColor(GetThemeColor(currentFloor.Theme));
        terminal.WriteLine($"╔═══════════════════════════════════════════════════════╗");
        terminal.WriteLine($"║  DUNGEON MAP - Level {currentDungeonLevel} ({currentFloor.Theme})".PadRight(56) + "║");
        terminal.WriteLine($"╚═══════════════════════════════════════════════════════╝");
        terminal.WriteLine("");

        // Build spatial map from room connections
        var roomPositions = BuildRoomPositionMap();

        if (roomPositions.Count == 0)
        {
            terminal.WriteLine("Map data unavailable.", "gray");
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

        // Render map (each room is 5 chars wide, 3 chars tall)
        // Row format: corridor row, then room row, then corridor row
        for (int y = minY; y <= maxY; y++)
        {
            // Top corridor row (vertical connections from above)
            var topLine = new System.Text.StringBuilder();
            for (int x = minX; x <= maxX; x++)
            {
                if (posToRoom.TryGetValue((x, y), out var room))
                {
                    // Check if room has north exit and it's explored
                    bool hasNorth = room.Exits.ContainsKey(Direction.North);
                    bool northExplored = hasNorth && room.IsExplored;

                    if (northExplored)
                    {
                        topLine.Append("  |  ");
                    }
                    else
                    {
                        topLine.Append("     ");
                    }
                }
                else
                {
                    topLine.Append("     ");
                }
            }
            terminal.SetColor("darkgray");
            terminal.WriteLine(topLine.ToString());

            // Room row
            var roomLine = new System.Text.StringBuilder();
            for (int x = minX; x <= maxX; x++)
            {
                // West corridor
                if (posToRoom.TryGetValue((x, y), out var room))
                {
                    bool hasWest = room.Exits.ContainsKey(Direction.West);
                    bool westExplored = hasWest && room.IsExplored;

                    if (westExplored)
                    {
                        terminal.SetColor("darkgray");
                        roomLine.Append("-");
                    }
                    else
                    {
                        roomLine.Append(" ");
                    }

                    // Room symbol
                    string roomSymbol = GetRoomMapSymbol(room);
                    roomLine.Append(roomSymbol);

                    // East corridor
                    bool hasEast = room.Exits.ContainsKey(Direction.East);
                    bool eastExplored = hasEast && room.IsExplored;

                    if (eastExplored)
                    {
                        roomLine.Append("-");
                    }
                    else
                    {
                        roomLine.Append(" ");
                    }
                }
                else
                {
                    roomLine.Append("     ");
                }
            }

            // Render room line with colors
            RenderColoredMapLine(roomLine.ToString(), posToRoom, minX, maxX, y);

            // Bottom corridor row (vertical connections below)
            var bottomLine = new System.Text.StringBuilder();
            for (int x = minX; x <= maxX; x++)
            {
                if (posToRoom.TryGetValue((x, y), out var room))
                {
                    bool hasSouth = room.Exits.ContainsKey(Direction.South);
                    bool southExplored = hasSouth && room.IsExplored;

                    if (southExplored)
                    {
                        bottomLine.Append("  |  ");
                    }
                    else
                    {
                        bottomLine.Append("     ");
                    }
                }
                else
                {
                    bottomLine.Append("     ");
                }
            }
            terminal.SetColor("darkgray");
            terminal.WriteLine(bottomLine.ToString());
        }

        terminal.WriteLine("");
        terminal.SetColor("gray");
        terminal.Write("Legend: ");
        terminal.SetColor("bright_yellow");
        terminal.Write("[@]");
        terminal.SetColor("gray");
        terminal.Write("=You ");
        terminal.SetColor("bright_red");
        terminal.Write("[B]");
        terminal.SetColor("gray");
        terminal.Write("=Boss ");
        terminal.SetColor("blue");
        terminal.Write("[>]");
        terminal.SetColor("gray");
        terminal.Write("=Stairs ");
        terminal.SetColor("green");
        terminal.Write("[#]");
        terminal.SetColor("gray");
        terminal.Write("=Cleared ");
        terminal.SetColor("red");
        terminal.Write("[!]");
        terminal.SetColor("gray");
        terminal.WriteLine("=Danger");
        terminal.Write("        ");
        terminal.SetColor("cyan");
        terminal.Write("[.]");
        terminal.SetColor("gray");
        terminal.Write("=Safe ");
        terminal.SetColor("darkgray");
        terminal.Write("[?]");
        terminal.SetColor("gray");
        terminal.Write("=Unknown  ");
        terminal.SetColor("darkgray");
        terminal.WriteLine("- | = Passages");
        terminal.WriteLine("");

        // Floor stats
        int explored = currentFloor.Rooms.Count(r => r.IsExplored);
        int cleared = currentFloor.Rooms.Count(r => r.IsCleared);
        terminal.SetColor("white");
        terminal.WriteLine($"Explored: {explored}/{currentFloor.Rooms.Count}  Cleared: {cleared}/{currentFloor.Rooms.Count}");

        // Current room info
        var currentRoom = currentFloor.GetCurrentRoom();
        if (currentRoom != null)
        {
            terminal.SetColor("yellow");
            terminal.WriteLine($"Location: {currentRoom.Name}");
        }

        if (currentFloor.BossDefeated)
        {
            terminal.SetColor("green");
            terminal.WriteLine("BOSS DEFEATED!");
        }

        terminal.WriteLine("");
        terminal.SetColor("gray");
        terminal.WriteLine("Press Enter to continue...");
        await terminal.GetInput("");
    }

    /// <summary>
    /// Build a spatial position map by traversing room connections via BFS
    /// </summary>
    private Dictionary<string, (int x, int y)> BuildRoomPositionMap()
    {
        var positions = new Dictionary<string, (int x, int y)>();

        if (currentFloor == null || currentFloor.Rooms.Count == 0)
            return positions;

        var visited = new HashSet<string>();
        var queue = new Queue<(string roomId, int x, int y)>();

        // Start from entrance room at origin
        var entranceId = currentFloor.EntranceRoomId;
        if (string.IsNullOrEmpty(entranceId) && currentFloor.Rooms.Count > 0)
            entranceId = currentFloor.Rooms[0].Id;

        queue.Enqueue((entranceId, 0, 0));
        visited.Add(entranceId);
        positions[entranceId] = (0, 0);

        // BFS to assign positions based on exit directions
        while (queue.Count > 0)
        {
            var (roomId, x, y) = queue.Dequeue();
            var room = currentFloor.Rooms.FirstOrDefault(r => r.Id == roomId);

            if (room == null) continue;

            foreach (var exit in room.Exits)
            {
                var targetId = exit.Value.TargetRoomId;
                if (visited.Contains(targetId)) continue;

                // Calculate new position based on direction
                int newX = x, newY = y;
                switch (exit.Key)
                {
                    case Direction.North: newY--; break;
                    case Direction.South: newY++; break;
                    case Direction.East: newX++; break;
                    case Direction.West: newX--; break;
                }

                // Check for collision - if position taken, try to find nearby spot
                var targetPos = (newX, newY);
                if (positions.ContainsValue(targetPos))
                {
                    // Find nearest free position
                    targetPos = FindNearestFreePosition(positions, newX, newY);
                }

                visited.Add(targetId);
                positions[targetId] = targetPos;
                queue.Enqueue((targetId, targetPos.Item1, targetPos.Item2));
            }
        }

        return positions;
    }

    /// <summary>
    /// Find nearest free position when there's a collision
    /// </summary>
    private (int x, int y) FindNearestFreePosition(Dictionary<string, (int x, int y)> positions, int targetX, int targetY)
    {
        // Spiral outward to find free spot
        for (int radius = 1; radius < 10; radius++)
        {
            for (int dx = -radius; dx <= radius; dx++)
            {
                for (int dy = -radius; dy <= radius; dy++)
                {
                    if (Math.Abs(dx) == radius || Math.Abs(dy) == radius)
                    {
                        var pos = (targetX + dx, targetY + dy);
                        if (!positions.ContainsValue(pos))
                            return pos;
                    }
                }
            }
        }
        return (targetX, targetY); // Fallback
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
        terminal.WriteLine("*** ANOTHER ADVENTURER ***");
        terminal.WriteLine("");

        var player = GetCurrentPlayer();
        var npcType = dungeonRandom.Next(5);

        switch (npcType)
        {
            case 0: // Friendly trader
                terminal.WriteLine("A fellow adventurer hails you!", "white");
                terminal.WriteLine("\"Greetings, friend! I have supplies for sale.\"", "yellow");
                terminal.WriteLine("");

                terminal.WriteLine("[B] Buy healing potions (500 gold each)");
                terminal.WriteLine("[I] Trade information (100 gold)");
                terminal.WriteLine("[L] Leave");

                var tradeChoice = await terminal.GetInput("Choice: ");
                if (tradeChoice.ToUpper() == "B")
                {
                    if (player.Gold >= 500)
                    {
                        player.Gold -= 500;
                        player.Healing = Math.Min(player.MaxPotions, player.Healing + 1);
                        terminal.WriteLine("You purchase a healing potion.", "green");
                    }
                    else
                    {
                        terminal.WriteLine("\"You don't have enough gold, friend.\"", "yellow");
                    }
                }
                else if (tradeChoice.ToUpper() == "I")
                {
                    if (player.Gold >= 100)
                    {
                        player.Gold -= 100;
                        terminal.SetColor("cyan");
                        terminal.WriteLine("");
                        terminal.WriteLine("The adventurer leans in close...");
                        terminal.WriteLine("");
                        terminal.WriteLine("\"The boss room is always at the far end of the dungeon.\"");
                        terminal.WriteLine("\"Watch for traps near treasure rooms - thieves love 'em.\"");
                        terminal.WriteLine("\"Resting recovers health, but you can only rest once per floor.\"");
                        terminal.WriteLine("\"And between you and me... the deeper you go, the richer the rewards.\"");
                        terminal.WriteLine("");
                        terminal.SetColor("gray");
                        terminal.WriteLine($"(-100 gold)");
                    }
                    else
                    {
                        terminal.WriteLine("\"Information costs gold, friend. Come back when you have some.\"", "yellow");
                    }
                }
                else if (tradeChoice.ToUpper() != "L")
                {
                    terminal.WriteLine("The adventurer shrugs as you walk away.", "gray");
                }
                break;

            case 1: // Wounded adventurer
                terminal.WriteLine("A wounded adventurer lies against the wall!", "red");
                terminal.WriteLine("\"Please... take my map... avenge me...\"", "yellow");
                terminal.WriteLine("");

                // Mark more rooms as explored
                foreach (var room in currentFloor.Rooms.Take(currentFloor.Rooms.Count / 2))
                {
                    room.IsExplored = true;
                }
                terminal.WriteLine("You gain knowledge of the dungeon layout!", "green");
                break;

            case 2: // Rival adventurer
                terminal.WriteLine("A rival adventurer blocks your path!", "red");
                terminal.WriteLine("\"This treasure is MINE! Get out!\"", "yellow");
                terminal.WriteLine("");

                var rivalChoice = await terminal.GetInput("(F)ight, (N)egotiate, or (L)eave? ");
                if (rivalChoice.ToUpper() == "F")
                {
                    var rival = Monster.CreateMonster(
                        currentDungeonLevel, "Rival Adventurer",
                        currentDungeonLevel * 10, currentDungeonLevel * 3, 0,
                        "Die!", false, false, "Steel Sword", "Chain Mail",
                        false, false, currentDungeonLevel * 3, currentDungeonLevel * 2, currentDungeonLevel * 2
                    );
                    rival.Level = currentDungeonLevel;

                    var combatEngine = new CombatEngine(terminal);
                    var combatResult = await combatEngine.PlayerVsMonster(player, rival, teammates);

                    // Check if player should return to temple after resurrection
                    if (combatResult.ShouldReturnToTemple)
                    {
                        terminal.SetColor("yellow");
                        terminal.WriteLine("You awaken at the Temple of Light...");
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
                        terminal.WriteLine($"You pay {bribe} gold to pass.", "yellow");
                    }
                    else
                    {
                        terminal.WriteLine("\"No gold? Then fight!\"", "red");
                    }
                }
                break;

            case 3: // Lost explorer
                terminal.WriteLine("A lost explorer stumbles towards you!", "white");
                terminal.WriteLine("\"Oh thank the gods! I've been lost for days!\"", "yellow");
                terminal.WriteLine("");
                terminal.WriteLine("You guide them to safety.", "green");
                long reward = currentDungeonLevel * 150;
                player.Gold += reward;
                player.Chivalry += 20;
                terminal.WriteLine($"They reward you with {reward} gold!", "yellow");
                terminal.WriteLine("Your chivalry increases!", "white");
                break;

            case 4: // Mysterious stranger
                terminal.WriteLine("A cloaked figure emerges from the shadows...", "magenta");
                terminal.WriteLine("\"Fate has brought us together...\"", "yellow");
                terminal.WriteLine("");

                if (dungeonRandom.NextDouble() < 0.5)
                {
                    terminal.WriteLine("They offer you a blessing!", "green");
                    player.HP = Math.Min(player.MaxHP, player.HP + player.MaxHP / 2);
                    terminal.WriteLine("You feel revitalized!");
                }
                else
                {
                    terminal.WriteLine("\"Beware the darkness ahead...\"", "red");
                    terminal.WriteLine("They vanish as mysteriously as they appeared.");
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
        terminal.WriteLine("*** RIDDLE GATE ***");
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
            terminal.WriteLine("The ancient mechanism unlocks!");

            // Rewards scale with difficulty and level
            // XP equivalent to 2-4 monster kills based on difficulty
            long goldReward = currentDungeonLevel * 50 + difficulty * currentDungeonLevel * 20;
            long expReward = (long)(Math.Pow(currentDungeonLevel, 1.5) * 15 * (1 + difficulty * 0.5));
            player.Gold += goldReward;
            player.Experience += expReward;
            terminal.WriteLine($"You receive {goldReward} gold and {expReward} experience!");

            // Chance for Ocean Philosophy fragment on high difficulty riddles
            if (difficulty >= 3 && dungeonRandom.Next(100) < 30)
            {
                terminal.WriteLine("");
                terminal.SetColor("bright_cyan");
                terminal.WriteLine("As you solve the riddle, a deeper truth resonates within you...");
                // Grant a random uncollected wave fragment
                var fragments = Enum.GetValues<WaveFragment>()
                    .Where(f => !OceanPhilosophySystem.Instance.CollectedFragments.Contains(f))
                    .ToList();
                if (fragments.Count > 0)
                {
                    var fragment = fragments[dungeonRandom.Next(fragments.Count)];
                    OceanPhilosophySystem.Instance.CollectFragment(fragment);
                    terminal.WriteLine("You've gained insight into the Ocean's wisdom...", "bright_magenta");
                }
            }
        }
        else
        {
            terminal.SetColor("red");
            terminal.WriteLine("");
            terminal.WriteLine("The gate remains sealed.");

            // Take damage on failure
            int damage = riddle.FailureDamage * currentDungeonLevel / 5;
            if (damage > 0)
            {
                player.HP -= damage;
                terminal.WriteLine($"A trap activates! You take {damage} damage!");
            }
        }

        await terminal.PressAnyKey();
    }

    private async Task FullPuzzleEncounter(Character player)
    {
        terminal.SetColor("cyan");
        terminal.WriteLine("*** ANCIENT PUZZLE ***");
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
            terminal.WriteLine($"Attempts remaining: {attempts}");
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
                    terminal.WriteLine("That's not quite right...");
                    terminal.WriteLine("");
                }
            }
        }

        if (solved)
        {
            terminal.SetColor("green");
            terminal.WriteLine("");
            terminal.WriteLine("*** PUZZLE SOLVED! ***");

            // Rewards scaled to dungeon level - roughly 2-3 monster kills worth
            long goldReward = currentDungeonLevel * 30 + difficulty * currentDungeonLevel * 15;
            long expReward = (long)(Math.Pow(currentDungeonLevel, 1.5) * 15 * (1 + difficulty * 0.3));
            player.Gold += goldReward;
            player.Experience += expReward;
            terminal.WriteLine($"You gain {goldReward} gold and {expReward} experience!");

            PuzzleSystem.Instance.MarkPuzzleSolved(currentDungeonLevel, puzzle.Title);
        }
        else
        {
            terminal.SetColor("red");
            terminal.WriteLine("");
            terminal.WriteLine("The puzzle resets. You failed to solve it.");

            int damage = (int)(player.MaxHP * (puzzle.FailureDamagePercent / 100.0));
            if (damage > 0)
            {
                player.HP -= damage;
                terminal.WriteLine($"A trap springs! You take {damage} damage!");
            }
        }

        await terminal.PressAnyKey();
    }

    private async Task<bool> HandleLeverPuzzle(PuzzleInstance puzzle, Character player)
    {
        int leverCount = puzzle.Solution.Count;
        terminal.WriteLine($"There are {leverCount} levers. Enter the sequence (e.g., 1,2,3):", "white");

        var input = await terminal.GetInput("> ");
        var parts = input.Split(',', ' ').Select(s => s.Trim()).Where(s => !string.IsNullOrEmpty(s)).ToList();

        if (parts.Count != leverCount) return false;

        for (int i = 0; i < leverCount; i++)
        {
            if (!int.TryParse(parts[i], out int lever)) return false;
            if ((lever - 1).ToString() != puzzle.Solution[i]) return false;
        }

        return true;
    }

    private async Task<bool> HandleSymbolPuzzle(PuzzleInstance puzzle, Character player)
    {
        terminal.WriteLine("Available symbols: " + string.Join(", ", puzzle.AvailableChoices), "white");
        terminal.WriteLine($"Enter {puzzle.Solution.Count} symbols separated by commas:", "white");

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
        terminal.WriteLine($"Target sum: {puzzle.TargetNumber}", "white");
        terminal.WriteLine("Available numbers: " + string.Join(", ", puzzle.AvailableNumbers), "white");
        terminal.WriteLine("Enter numbers that sum to the target:", "white");

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
        terminal.WriteLine("Memorize this sequence:", "yellow");
        terminal.WriteLine("");
        terminal.SetColor("bright_cyan");
        terminal.WriteLine("  " + string.Join(" - ", puzzle.Solution));
        await Task.Delay(3000); // Show for 3 seconds

        // Clear the sequence
        terminal.ClearScreen();
        terminal.SetColor("cyan");
        terminal.WriteLine("*** MEMORY PUZZLE ***");
        terminal.WriteLine("");
        terminal.WriteLine("Enter the sequence you saw:", "white");

        var input = await terminal.GetInput("> ");
        var parts = input.Split(',', ' ', '-').Select(s => s.Trim().ToLower()).Where(s => !string.IsNullOrEmpty(s)).ToList();

        if (parts.Count != puzzle.Solution.Count) return false;

        for (int i = 0; i < parts.Count; i++)
        {
            if (parts[i] != puzzle.Solution[i].ToLower()) return false;
        }

        return true;
    }

    private async Task<bool> HandleSimplePuzzle(PuzzleInstance puzzle, Character player)
    {
        // Generic handler for other puzzle types - use Intelligence check
        terminal.WriteLine("This puzzle requires careful thought...", "white");
        terminal.WriteLine("");
        terminal.WriteLine("[1] Examine carefully and deduce the answer", "white");
        terminal.WriteLine("[2] Try a random approach", "white");
        terminal.WriteLine("[3] Give up", "white");

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

        terminal.WriteLine("You discover a hidden sanctuary!", "white");
        terminal.WriteLine("The air here is calm, protected by ancient magic.", "gray");
        terminal.WriteLine("");

        if (!hasRestThisFloor)
        {
            terminal.WriteLine("You rest and recover your strength.", "green");

            // Sanctuary provides better recovery - 33% of max stats
            long healAmount = player.MaxHP / 3;
            player.HP = Math.Min(player.MaxHP, player.HP + healAmount);
            terminal.WriteLine($"You recover {healAmount} HP!");

            long manaAmount = player.MaxMana / 3;
            player.Mana = Math.Min(player.MaxMana, player.Mana + manaAmount);
            if (manaAmount > 0)
                terminal.WriteLine($"You recover {manaAmount} mana!");

            long staminaAmount = player.MaxCombatStamina / 3;
            player.CurrentCombatStamina = Math.Min(player.MaxCombatStamina, player.CurrentCombatStamina + staminaAmount);
            if (staminaAmount > 0)
                terminal.WriteLine($"You recover {staminaAmount} stamina!");

            // Cure poison
            if (player.Poison > 0)
            {
                player.Poison = 0;
                terminal.WriteLine("The sanctuary's magic cures your poison!", "cyan");
            }

            hasRestThisFloor = true;

            // Trigger dream sequences through the Amnesia System
            // Dreams reveal the player's forgotten past as a fragment of Manwe
            await Task.Delay(1500);
            terminal.WriteLine("");
            terminal.SetColor("gray");
            terminal.WriteLine("You close your eyes...");
            await Task.Delay(1000);

            await AmnesiaSystem.Instance.OnPlayerRest(terminal, player);
        }
        else
        {
            terminal.WriteLine("You've already rested on this floor.", "gray");
            terminal.WriteLine("The sanctuary offers no additional benefit.");
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
        terminal.WriteLine("*** MYSTERIOUS OCCURRENCE ***");
        terminal.WriteLine("");

        var player = GetCurrentPlayer();
        var mysteryType = dungeonRandom.Next(5);

        switch (mysteryType)
        {
            case 0: // Vision
                terminal.WriteLine("A strange vision overtakes you...", "cyan");
                await Task.Delay(1500);
                terminal.WriteLine("You see the layout of this floor!", "yellow");
                foreach (var room in currentFloor.Rooms)
                {
                    room.IsExplored = true;
                }
                terminal.WriteLine("All rooms are now revealed on your map!", "green");
                break;

            case 1: // Time warp
                terminal.WriteLine("Reality warps around you!", "red");
                await Task.Delay(1000);
                terminal.SetColor("green");
                terminal.WriteLine("When it clears, you feel younger, stronger!");
                // XP equivalent to about 1.5 monster kills
                long timeWarpXp = (long)(Math.Pow(currentDungeonLevel, 1.5) * 22);
                player.Experience += timeWarpXp;
                terminal.WriteLine($"+{timeWarpXp} experience!");
                break;

            case 2: // Ghostly message
                terminal.WriteLine("A ghostly figure appears!", "white");
                terminal.WriteLine("\"Seek the chamber of bones...\"", "yellow");
                terminal.WriteLine("\"There you will find what you seek...\"", "yellow");
                await Task.Delay(1500);
                terminal.WriteLine("The ghost points towards a direction and fades.", "gray");
                break;

            case 3: // Random teleport
                terminal.WriteLine("A magical portal suddenly opens beneath you!", "bright_magenta");
                await Task.Delay(1000);
                var randomRoom = currentFloor.Rooms[dungeonRandom.Next(currentFloor.Rooms.Count)];
                currentFloor.CurrentRoomId = randomRoom.Id;
                randomRoom.IsExplored = true;
                // Auto-clear rooms without monsters
                if (!randomRoom.HasMonsters)
                {
                    randomRoom.IsCleared = true;
                }
                terminal.WriteLine($"You are transported to: {randomRoom.Name}!", "yellow");
                break;

            case 4: // Treasure rain
                terminal.WriteLine("Gold coins rain from the ceiling!", "yellow");
                long goldRain = currentDungeonLevel * 100 + dungeonRandom.Next(500);
                player.Gold += goldRain;
                terminal.WriteLine($"You gather {goldRain} gold!");
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
        terminal.WriteLine("━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━");
        terminal.WriteLine("              ANCIENT LORE LIBRARY");
        terminal.WriteLine("━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━");
        terminal.WriteLine("");

        var player = GetCurrentPlayer();
        var ocean = OceanPhilosophySystem.Instance;

        terminal.SetColor("white");
        terminal.WriteLine("Dust motes dance in pale light from an unknown source.");
        terminal.WriteLine("Ancient tomes line walls that stretch beyond sight.");
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
            terminal.WriteLine("A tome floats down from the shelves, opening before you...");
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
            terminal.WriteLine("The words burn into your memory...");
            terminal.WriteLine($"(Wave Fragment collected: {fragmentData.Title})");

            // Grant awakening progress
            if (fragmentData.RequiredAwakening >= 5)
            {
                terminal.WriteLine("");
                terminal.SetColor("magenta");
                terminal.WriteLine("Something stirs in the depths of your consciousness...");
            }
        }
        else
        {
            // All fragments collected - give ambient wisdom instead
            terminal.SetColor("gray");
            terminal.WriteLine("You browse the ancient texts, but find nothing new.");
            terminal.WriteLine("The knowledge here already lives within you.");

            if (ocean.AwakeningLevel >= 5)
            {
                terminal.WriteLine("");
                terminal.SetColor("bright_cyan");
                terminal.WriteLine("...or perhaps it always did.");
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
        terminal.WriteLine("━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━");
        terminal.WriteLine("              MEMORY CHAMBER");
        terminal.WriteLine("━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━");
        terminal.WriteLine("");

        var player = GetCurrentPlayer();
        var amnesia = AmnesiaSystem.Instance;

        terminal.SetColor("white");
        terminal.WriteLine("The walls here are mirrors, but they don't show your reflection.");
        terminal.WriteLine("They show... someone else. Someone familiar.");
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
            terminal.WriteLine($"A memory surfaces: \"{newMemory.Value.Title}\"");
            terminal.WriteLine("");

            terminal.SetColor("magenta");
            foreach (var line in newMemory.Value.Lines)
            {
                terminal.WriteLine($"  {line}");
                await Task.Delay(1200);
            }

            terminal.WriteLine("");
            terminal.SetColor("gray");
            terminal.WriteLine("The vision fades, but the feeling remains.");
        }
        else
        {
            terminal.SetColor("gray");
            terminal.WriteLine("The mirrors show fragments of a life not quite your own.");
            terminal.WriteLine("You sense there is more to remember, but not here. Not yet.");
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
        terminal.WriteLine("━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━");
        terminal.WriteLine("              THE RIDDLE GATE");
        terminal.WriteLine("━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━");
        terminal.WriteLine("");

        var player = GetCurrentPlayer();
        var ocean = OceanPhilosophySystem.Instance;

        terminal.SetColor("white");
        terminal.WriteLine("A massive stone door blocks your path.");
        terminal.WriteLine("Carved into its surface: a face, ancient and knowing.");
        terminal.WriteLine("");
        await Task.Delay(1000);

        terminal.SetColor("bright_yellow");
        terminal.WriteLine("The stone lips move: 'Answer my riddle to pass.'");
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
            terminal.WriteLine("━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━");
            terminal.WriteLine("              RIDDLE SOLVED");
            terminal.WriteLine("━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━");
            terminal.WriteLine("");
            terminal.WriteLine("The stone face smiles. 'Wisdom opens all doors.'");
            terminal.WriteLine("The gate rumbles open.");
            terminal.WriteLine("");

            // Reward based on riddle difficulty
            // XP equivalent to 2-4 monster kills based on difficulty
            long xpReward = (long)(Math.Pow(currentDungeonLevel, 1.5) * 15 * (1 + riddle.Difficulty * 0.5));
            player.Experience += xpReward;
            terminal.WriteLine($"You gain {xpReward} experience!", "cyan");

            // Ocean philosophy riddles grant awakening insight
            if (riddle.IsOceanPhilosophy)
            {
                ocean.GainInsight(20);
                terminal.WriteLine("The riddle's deeper meaning resonates within you...", "magenta");
            }
        }
        else
        {
            terminal.SetColor("red");
            terminal.WriteLine("━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━");
            terminal.WriteLine("              RIDDLE FAILED");
            terminal.WriteLine("━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━");
            terminal.WriteLine("");
            terminal.WriteLine("'Foolish mortal. There is a price for failure.'");
            terminal.WriteLine("");

            // Trigger combat with a guardian
            terminal.SetColor("bright_red");
            terminal.WriteLine("The gate guardian manifests to punish your ignorance!");
            await Task.Delay(1500);

            // Create a riddle guardian monster and fight
            var guardian = CreateRiddleGuardian();
            var combatEngine = new CombatEngine(terminal);
            var combatResult = await combatEngine.PlayerVsMonster(player, guardian, teammates);

            // Check if player should return to temple after resurrection
            if (combatResult.ShouldReturnToTemple)
            {
                terminal.SetColor("yellow");
                terminal.WriteLine("You awaken at the Temple of Light...");
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
            terminal.WriteLine("━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━");
            terminal.WriteLine("              HIDDEN CHAMBER");
            terminal.WriteLine("━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━");
            terminal.WriteLine("");

            // Track secret found for achievements (finding a hidden chamber counts as a secret)
            player.Statistics.RecordSecretFound();

            terminal.SetColor("gray");
            terminal.WriteLine("You sense great power once resided here.");
            terminal.WriteLine("But whatever dwelt in this place... has moved on.");
            terminal.WriteLine("");
            terminal.WriteLine("Perhaps deeper in the dungeon, you will find what you seek.");
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
            terminal.WriteLine("You awaken at the Temple of Light...");
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
                terminal.WriteLine("As the battle ends, something breaks open in your mind...");
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
        terminal.WriteLine("Dungeon Help");
        terminal.WriteLine("============");
        terminal.WriteLine("");
        terminal.SetColor("white");
        terminal.WriteLine("E - Explore the current level");
        terminal.WriteLine("D - Descend to a deeper, more dangerous level");
        terminal.WriteLine("A - Ascend to a safer level or return to town");
        terminal.WriteLine("T - Manage your team members");
        terminal.WriteLine("S - View your character status");
        terminal.WriteLine("P - Buy potions from the wandering monk");
        terminal.WriteLine("M - View the dungeon map");
        terminal.WriteLine("Q - Quit and return to town");
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
                terminal.WriteLine($"You cannot venture deeper than level {maxAccessible} at your current strength.", "yellow");
                terminal.WriteLine("Level up to access deeper floors.", "gray");
            }
            else
            {
                terminal.WriteLine("You have reached the deepest level of the dungeon.", "yellow");
            }
        }
        else
        {
            currentDungeonLevel = targetLevel;
            terminal.WriteLine($"You steel your nerves. The dungeon now feels like level {currentDungeonLevel}!", "magenta");
        }

        await Task.Delay(1500);
    }
    
    // Additional encounter methods
    private async Task BeggarEncounter()
    {
        terminal.SetColor("cyan");
        terminal.WriteLine("☂ BEGGAR ENCOUNTER ☂");
        terminal.WriteLine("");
        terminal.WriteLine("A poor beggar approaches you with outstretched hands.");
        terminal.WriteLine("'Please, kind sir/madam, spare some gold for a poor soul?'");
        terminal.WriteLine("");
        
        var choice = await terminal.GetInput("(G)ive gold to beggar or (I)gnore them? ");
        
        if (choice.ToUpper() == "G")
        {
            var currentPlayer = GetCurrentPlayer();
            if (currentPlayer.Gold >= 10)
            {
                currentPlayer.Gold -= 10;
                currentPlayer.Chivalry += 5;
                terminal.WriteLine("The beggar thanks you profusely for your kindness!", "green");
                terminal.WriteLine("Your chivalry increases!");
            }
            else
            {
                terminal.WriteLine("You don't have enough gold to spare.", "red");
            }
        }
        else
        {
            terminal.WriteLine("You ignore the beggar and continue on your way.", "gray");
        }
        
        await Task.Delay(2000);
    }
    
    private Monster CreateMerchantMonster()
    {
        return Monster.CreateMonster(1, "Frightened Merchant", 30, 10, 0,
            "Help me!", false, false, "Walking Stick", "Robes", 
            false, false, 5, 1, 3);
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
        terminal.WriteLine("══════════════════════════════════════════════════════════════");
        terminal.WriteLine("");
        terminal.WriteLine("       As you explore the room, an ancient power stirs...");
        terminal.WriteLine("");
        terminal.WriteLine("══════════════════════════════════════════════════════════════");
        terminal.WriteLine("");
        await Task.Delay(2000);

        terminal.SetColor("white");
        terminal.WriteLine("  Hidden beneath the dust of ages, you find a stone tablet.");
        terminal.WriteLine("  It pulses with divine energy, warm to the touch.");
        terminal.WriteLine("");
        terminal.WriteLine("  This is one of the Seven Seals - the truth of the Old Gods.");
        terminal.WriteLine("");
        await Task.Delay(1500);

        terminal.SetColor("gray");
        await terminal.GetInputAsync("  Press Enter to continue...");

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
        terminal.WriteLine("══════════════════════════════════════════════════════════════");
        terminal.WriteLine("      A DIVINE ARTIFACT PULSES WITH POWER!");
        terminal.WriteLine("══════════════════════════════════════════════════════════════");
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
        terminal.WriteLine("══════════════════════════════════════════════════════════════════════════════");
        terminal.WriteLine("                    THE LIGHT THAT WAS                                        ");
        terminal.WriteLine("══════════════════════════════════════════════════════════════════════════════");
        terminal.WriteLine("");

        await Task.Delay(1000);

        terminal.SetColor("white");
        terminal.WriteLine("Lyris suddenly stops, her eyes widening.");
        terminal.WriteLine("");
        await Task.Delay(1000);

        terminal.SetColor("cyan");
        terminal.WriteLine("\"I feel it,\" she whispers. \"The artifact... it's close.\"");
        terminal.WriteLine("");
        await Task.Delay(1000);

        terminal.SetColor("white");
        terminal.WriteLine("She moves to a seemingly unremarkable section of wall,");
        terminal.WriteLine("pressing her palm against the stone. Ancient symbols flare to life.");
        terminal.WriteLine("");
        await Task.Delay(1500);

        terminal.SetColor("bright_yellow");
        terminal.WriteLine("A hidden chamber opens, revealing a pedestal.");
        terminal.WriteLine("Upon it rests a crystalline orb, pulsing with fading golden light.");
        terminal.WriteLine("");
        await Task.Delay(1500);

        terminal.SetColor("cyan");
        terminal.WriteLine("\"Aurelion's Heart,\" Lyris breathes. \"The last fragment of his true self.\"");
        terminal.WriteLine("\"Before the corruption. Before Manwe twisted everything.\"");
        terminal.WriteLine("");
        await Task.Delay(1500);

        terminal.SetColor("white");
        terminal.WriteLine("She reaches for it, then hesitates, looking at you.");
        terminal.WriteLine("");

        terminal.SetColor("yellow");
        terminal.WriteLine("[1] Encourage her to take it");
        terminal.WriteLine("[2] Warn her it might be dangerous");
        terminal.WriteLine("[3] Take it yourself");
        terminal.WriteLine("");

        var choice = await terminal.GetInput("What do you do? ");

        switch (choice)
        {
            case "1":
                terminal.SetColor("white");
                terminal.WriteLine("");
                terminal.WriteLine("You nod encouragingly. \"This is what you've been searching for.\"");
                terminal.WriteLine("");
                await Task.Delay(1000);

                terminal.SetColor("bright_magenta");
                terminal.WriteLine("Lyris gently lifts the orb. Light floods the chamber.");
                terminal.WriteLine("For a moment, you see her as she once was - radiant, divine.");
                terminal.WriteLine("");
                await Task.Delay(1500);

                terminal.SetColor("cyan");
                terminal.WriteLine("\"I... I can feel him. The god I once served.\"");
                terminal.WriteLine("\"He's still in there, buried under Manwe's corruption.\"");
                terminal.WriteLine("\"With this... maybe there's hope.\"");
                terminal.WriteLine("");

                UsurperRemake.Systems.CompanionSystem.Instance.ModifyLoyalty(
                    UsurperRemake.Systems.CompanionId.Lyris, 20, "Trusted her with Aurelion's Heart");
                break;

            case "2":
                terminal.SetColor("white");
                terminal.WriteLine("");
                terminal.WriteLine("\"Be careful,\" you warn. \"Divine artifacts can be treacherous.\"");
                terminal.WriteLine("");
                await Task.Delay(1000);

                terminal.SetColor("cyan");
                terminal.WriteLine("\"You're right to be cautious,\" she says softly.");
                terminal.WriteLine("\"But this... this is worth any risk.\"");
                terminal.WriteLine("");
                await Task.Delay(1000);

                terminal.SetColor("bright_magenta");
                terminal.WriteLine("She carefully lifts the orb, bracing for pain that never comes.");
                terminal.WriteLine("Instead, warmth spreads through the chamber.");
                terminal.WriteLine("");

                UsurperRemake.Systems.CompanionSystem.Instance.ModifyLoyalty(
                    UsurperRemake.Systems.CompanionId.Lyris, 10, "Showed concern for her safety");
                break;

            case "3":
                terminal.SetColor("white");
                terminal.WriteLine("");
                terminal.WriteLine("You step forward to take the orb yourself.");
                terminal.WriteLine("");
                await Task.Delay(1000);

                terminal.SetColor("red");
                terminal.WriteLine("The moment your fingers touch it, searing pain shoots through you!");
                player.HP -= player.MaxHP / 4;
                terminal.WriteLine($"You take {player.MaxHP / 4} damage!");
                terminal.WriteLine("");
                await Task.Delay(1000);

                terminal.SetColor("cyan");
                terminal.WriteLine("\"It only responds to those who once served the light,\"");
                terminal.WriteLine("Lyris explains, gently taking the orb from your burned hands.");
                terminal.WriteLine("");

                UsurperRemake.Systems.CompanionSystem.Instance.ModifyLoyalty(
                    UsurperRemake.Systems.CompanionId.Lyris, -5, "Tried to take her artifact");
                break;
        }

        await Task.Delay(1500);

        terminal.SetColor("bright_green");
        terminal.WriteLine("══════════════════════════════════════════════════════════════════════════════");
        terminal.WriteLine("           QUEST COMPLETE: THE LIGHT THAT WAS                                 ");
        terminal.WriteLine("══════════════════════════════════════════════════════════════════════════════");
        terminal.WriteLine("");

        story.SetStoryFlag("lyris_quest_artifact_found", true);
        UsurperRemake.Systems.CompanionSystem.Instance.CompletePersonalQuest(
            UsurperRemake.Systems.CompanionId.Lyris, true);

        // Bonus: Lyris gains power
        lyris.BaseStats.MagicPower += 25;
        lyris.BaseStats.HealingPower += 15;
        terminal.WriteLine("Lyris has gained new power from connecting with Aurelion's essence!", "bright_cyan");

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
        terminal.WriteLine("══════════════════════════════════════════════════════════════════════════════");
        terminal.WriteLine("                    GHOSTS OF THE GUARD                                       ");
        terminal.WriteLine("══════════════════════════════════════════════════════════════════════════════");
        terminal.WriteLine("");

        await Task.Delay(1000);

        terminal.SetColor("white");
        terminal.WriteLine("Aldric freezes mid-step, his face going pale.");
        terminal.WriteLine("");
        await Task.Delay(1000);

        terminal.SetColor("bright_yellow");
        terminal.WriteLine("\"That smell,\" he growls. \"Brimstone and blood. I know it.\"");
        terminal.WriteLine("\"Malachar. The demon that slaughtered my men.\"");
        terminal.WriteLine("");
        await Task.Delay(1500);

        terminal.SetColor("white");
        terminal.WriteLine("From the shadows ahead, a massive figure emerges.");
        terminal.WriteLine("Horned, wreathed in flame, its eyes burning with malevolent intelligence.");
        terminal.WriteLine("");
        await Task.Delay(1500);

        terminal.SetColor("red");
        terminal.WriteLine("MALACHAR THE SLAYER speaks:");
        terminal.WriteLine("\"The last of the King's Guard. I wondered when you'd find me.\"");
        terminal.WriteLine("\"Your men died screaming your name. Did you know that?\"");
        terminal.WriteLine("");
        await Task.Delay(1500);

        terminal.SetColor("white");
        terminal.WriteLine("Aldric's hands tremble on his shield.");
        terminal.WriteLine("");

        terminal.SetColor("yellow");
        terminal.WriteLine("[1] \"Aldric, we fight together. You're not alone this time.\"");
        terminal.WriteLine("[2] \"This is your battle. I'll support you from behind.\"");
        terminal.WriteLine("[3] \"We should retreat and prepare properly.\"");
        terminal.WriteLine("");

        var choice = await terminal.GetInput("What do you say? ");

        switch (choice)
        {
            case "1":
                terminal.SetColor("bright_cyan");
                terminal.WriteLine("");
                terminal.WriteLine("\"Together,\" Aldric nods, his fear hardening into resolve.");
                terminal.WriteLine("\"This time, I won't fail.\"");
                terminal.WriteLine("");
                await Task.Delay(1000);

                await FightMalachar(player, aldric, true);

                UsurperRemake.Systems.CompanionSystem.Instance.ModifyLoyalty(
                    UsurperRemake.Systems.CompanionId.Aldric, 25, "Fought beside him against his demon");
                break;

            case "2":
                terminal.SetColor("white");
                terminal.WriteLine("");
                terminal.WriteLine("\"I understand,\" Aldric says quietly. \"This is my burden.\"");
                terminal.WriteLine("\"But... thank you for being here.\"");
                terminal.WriteLine("");
                await Task.Delay(1000);

                await FightMalachar(player, aldric, false);

                UsurperRemake.Systems.CompanionSystem.Instance.ModifyLoyalty(
                    UsurperRemake.Systems.CompanionId.Aldric, 15, "Respected his need to face his demon alone");
                break;

            case "3":
                terminal.SetColor("white");
                terminal.WriteLine("");
                terminal.WriteLine("\"No,\" Aldric says firmly. \"I've run from this for too long.\"");
                terminal.WriteLine("\"Today it ends. With or without you.\"");
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
        terminal.WriteLine("══════════════════════════════════════════════════════════════════════════════");
        terminal.WriteLine("                    BOSS: MALACHAR THE SLAYER                                 ");
        terminal.WriteLine("══════════════════════════════════════════════════════════════════════════════");
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

        terminal.SetColor("red");
        terminal.WriteLine($"Malachar HP: {malachar.HP}/{malachar.MaxHP}");
        terminal.WriteLine("");
        await Task.Delay(1000);

        // Simplified boss fight
        int rounds = 0;
        while (malachar.HP > 0 && player.HP > 0 && aldric.BaseStats.HP > 0 && rounds < 15)
        {
            rounds++;

            // Player attacks if joined
            if (playerJoins)
            {
                long playerDmg = player.Strength + player.WeapPow + dungeonRandom.Next(50);
                malachar.HP -= (int)playerDmg;
                terminal.WriteLine($"You strike Malachar for {playerDmg} damage!", "bright_cyan");
            }

            // Aldric attacks with determination
            int aldricDmg = aldric.BaseStats.Attack * 2 + dungeonRandom.Next(100);
            malachar.HP -= aldricDmg;
            terminal.WriteLine($"Aldric unleashes his fury for {aldricDmg} damage!", "bright_yellow");

            if (malachar.HP <= 0) break;

            // Malachar attacks Aldric (his target)
            int demonDmg = 50 + dungeonRandom.Next(80);
            aldric.BaseStats.HP -= demonDmg;
            terminal.WriteLine($"Malachar claws Aldric for {demonDmg} damage!", "red");

            terminal.WriteLine("");
            terminal.WriteLine($"Malachar HP: {Math.Max(0, malachar.HP)}/{malachar.MaxHP}", "red");
            terminal.WriteLine($"Aldric HP: {Math.Max(0, aldric.BaseStats.HP)}", "yellow");
            await Task.Delay(800);
            terminal.WriteLine("");
        }

        // Restore Aldric's HP (he can't die from this scripted fight)
        aldric.BaseStats.HP = Math.Max(100, aldric.BaseStats.HP);

        if (malachar.HP <= 0)
        {
            terminal.SetColor("bright_green");
            terminal.WriteLine("");
            terminal.WriteLine("Malachar falls, his flames sputtering out.");
            terminal.WriteLine("");
            await Task.Delay(1500);

            terminal.SetColor("white");
            terminal.WriteLine("Aldric stands over the demon's corpse, tears streaming down his face.");
            terminal.WriteLine("");
            await Task.Delay(1000);

            terminal.SetColor("bright_yellow");
            terminal.WriteLine("\"It's done,\" he whispers. \"Sergeant Bors. Private Kell. Captain Maren.\"");
            terminal.WriteLine("\"All of them. They can rest now. I... I did it.\"");
            terminal.WriteLine("");
            await Task.Delay(1500);

            terminal.SetColor("cyan");
            terminal.WriteLine("He turns to you, and for the first time, you see peace in his eyes.");
            terminal.WriteLine("\"Thank you. For standing with me. For not letting me face this alone.\"");
            terminal.WriteLine("");

            terminal.SetColor("bright_green");
            terminal.WriteLine("══════════════════════════════════════════════════════════════════════════════");
            terminal.WriteLine("           QUEST COMPLETE: GHOSTS OF THE GUARD                               ");
            terminal.WriteLine("══════════════════════════════════════════════════════════════════════════════");
            terminal.WriteLine("");

            var story = UsurperRemake.Systems.StoryProgressionSystem.Instance;
            story.SetStoryFlag("aldric_quest_demon_confronted", true);
            UsurperRemake.Systems.CompanionSystem.Instance.CompletePersonalQuest(
                UsurperRemake.Systems.CompanionId.Aldric, true);

            // Bonus: Aldric's guilt is lifted, gaining stats
            aldric.BaseStats.Defense += 30;
            aldric.BaseStats.HP += 200;
            terminal.WriteLine("Aldric's burden is lifted. He fights with renewed purpose!", "bright_cyan");

            // XP reward
            player.Experience += 25000;
            terminal.WriteLine($"You gained 25,000 experience!", "bright_green");
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
        terminal.WriteLine("══════════════════════════════════════════════════════════════════════════════");
        terminal.WriteLine("                    THE MEANING OF MERCY                                      ");
        terminal.WriteLine("══════════════════════════════════════════════════════════════════════════════");
        terminal.WriteLine("");

        await Task.Delay(1000);

        terminal.SetColor("white");
        terminal.WriteLine("You come upon a gruesome scene.");
        terminal.WriteLine("A young adventurer lies dying, wounds too severe to survive.");
        terminal.WriteLine("Beside him, an older woman - his mother, by the look - weeps.");
        terminal.WriteLine("");
        await Task.Delay(1500);

        terminal.SetColor("cyan");
        terminal.WriteLine("Mira kneels beside them, her hands already glowing with healing light.");
        terminal.WriteLine("But she stops, her face twisted with doubt.");
        terminal.WriteLine("");
        await Task.Delay(1000);

        terminal.SetColor("white");
        terminal.WriteLine("\"I can save him,\" she whispers. \"But he'll never walk again.\"");
        terminal.WriteLine("\"He'll live, but as a shadow of who he was.\"");
        terminal.WriteLine("\"Is that mercy? Or cruelty?\"");
        terminal.WriteLine("");
        await Task.Delay(1500);

        terminal.SetColor("gray");
        terminal.WriteLine("The mother looks at you desperately. \"Please, any life is better than none!\"");
        terminal.WriteLine("The young man's eyes find yours. He shakes his head slightly.");
        terminal.WriteLine("");

        terminal.SetColor("yellow");
        terminal.WriteLine("[1] \"Heal him, Mira. Life is precious, no matter the cost.\"");
        terminal.WriteLine("[2] \"Let him go peacefully. Some pain should not be prolonged.\"");
        terminal.WriteLine("[3] \"This is your choice, Mira. Not mine.\"");
        terminal.WriteLine("");

        var choice = await terminal.GetInput("What do you say? ");

        switch (choice)
        {
            case "1":
                terminal.SetColor("bright_green");
                terminal.WriteLine("");
                terminal.WriteLine("Mira nods slowly, and her hands blaze with light.");
                terminal.WriteLine("The young man gasps, color returning to his cheeks.");
                terminal.WriteLine("");
                await Task.Delay(1500);

                terminal.SetColor("white");
                terminal.WriteLine("The mother sobs with relief, clutching her son.");
                terminal.WriteLine("But the young man's eyes... there's something broken there.");
                terminal.WriteLine("");
                await Task.Delay(1000);

                terminal.SetColor("cyan");
                terminal.WriteLine("\"I saved a life,\" Mira says quietly. \"That has to mean something.\"");
                terminal.WriteLine("\"Even if... even if he hates me for it someday.\"");
                story.SetStoryFlag("mira_chose_life", true);
                break;

            case "2":
                terminal.SetColor("white");
                terminal.WriteLine("");
                terminal.WriteLine("Mira's light fades. She takes the young man's hand instead.");
                terminal.WriteLine("");
                await Task.Delay(1000);

                terminal.SetColor("cyan");
                terminal.WriteLine("\"I'm here,\" she whispers. \"You don't have to be afraid.\"");
                terminal.WriteLine("");
                await Task.Delay(1000);

                terminal.SetColor("white");
                terminal.WriteLine("He smiles - genuinely smiles - and closes his eyes.");
                terminal.WriteLine("The mother's wails echo through the dungeon.");
                terminal.WriteLine("");
                await Task.Delay(1500);

                terminal.SetColor("cyan");
                terminal.WriteLine("\"Sometimes,\" Mira says, tears streaming, \"the kindest thing\"");
                terminal.WriteLine("\"is to hold their hand at the end.\"");
                story.SetStoryFlag("mira_chose_peace", true);
                break;

            case "3":
                terminal.SetColor("white");
                terminal.WriteLine("");
                terminal.WriteLine("Mira looks at you, then back at the dying man.");
                terminal.WriteLine("For a long moment, no one moves.");
                terminal.WriteLine("");
                await Task.Delay(1500);

                terminal.SetColor("cyan");
                terminal.WriteLine("Finally, she speaks. \"I became a healer to help people.\"");
                terminal.WriteLine("\"But I forgot that helping isn't just about bodies.\"");
                terminal.WriteLine("");
                await Task.Delay(1000);

                terminal.SetColor("bright_green");
                terminal.WriteLine("She heals his pain, but not his wounds.");
                terminal.WriteLine("He slips away peacefully, free of suffering.");
                terminal.WriteLine("");
                story.SetStoryFlag("mira_chose_middle", true);
                break;
        }

        await Task.Delay(1500);

        terminal.SetColor("bright_green");
        terminal.WriteLine("══════════════════════════════════════════════════════════════════════════════");
        terminal.WriteLine("           QUEST COMPLETE: THE MEANING OF MERCY                               ");
        terminal.WriteLine("══════════════════════════════════════════════════════════════════════════════");
        terminal.WriteLine("");

        story.SetStoryFlag("mira_quest_choice_made", true);
        UsurperRemake.Systems.CompanionSystem.Instance.CompletePersonalQuest(
            UsurperRemake.Systems.CompanionId.Mira, true);

        terminal.SetColor("cyan");
        terminal.WriteLine("\"Thank you,\" Mira says. \"For being here. For helping me understand.\"");
        terminal.WriteLine("\"Healing isn't about fixing everything. It's about being present.\"");
        terminal.WriteLine("");

        // Bonus: Mira gains wisdom
        mira.BaseStats.HealingPower += 40;
        mira.BaseStats.MagicPower += 20;
        terminal.WriteLine("Mira's understanding deepens. Her healing grows stronger!", "bright_cyan");

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
        terminal.WriteLine("══════════════════════════════════════════════════════════════════════════════");
        terminal.WriteLine("                    ONE MORE SUNRISE                                          ");
        terminal.WriteLine("══════════════════════════════════════════════════════════════════════════════");
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
            terminal.WriteLine("══════════════════════════════════════════════════════════════════════════════");
            terminal.WriteLine("           QUEST COMPLETE: ONE MORE SUNRISE                                   ");
            terminal.WriteLine("══════════════════════════════════════════════════════════════════════════════");
            terminal.WriteLine("");

            UsurperRemake.Systems.CompanionSystem.Instance.CompletePersonalQuest(
                UsurperRemake.Systems.CompanionId.Vex, true);

            terminal.SetColor("cyan");
            terminal.WriteLine("Vex grins at you, his eyes misty.");
            terminal.WriteLine("\"I did it. Everything I wanted to do before... you know.\"");
            terminal.WriteLine("\"Thank you. For making these last days mean something.\"");
            terminal.WriteLine("");
        }

        await terminal.PressAnyKey();
        return true;
    }

    private async Task VexBucketTreasure(Character player, UsurperRemake.Systems.Companion vex,
        UsurperRemake.Systems.StoryProgressionSystem story)
    {
        terminal.SetColor("white");
        terminal.WriteLine("Vex suddenly stops, a mischievous grin spreading across his face.");
        terminal.WriteLine("");
        await Task.Delay(1000);

        terminal.SetColor("yellow");
        terminal.WriteLine("\"You know what I always wanted to do?\" he asks.");
        terminal.WriteLine("\"Find a legendary treasure. The kind they write songs about.\"");
        terminal.WriteLine("");
        await Task.Delay(1000);

        terminal.SetColor("white");
        terminal.WriteLine("He points to a hidden alcove you would have missed.");
        terminal.WriteLine("Inside: a chest covered in ancient runes.");
        terminal.WriteLine("");

        terminal.SetColor("yellow");
        terminal.WriteLine("[1] Help him open it together");
        terminal.WriteLine("[2] Let him have this moment alone");
        terminal.WriteLine("");

        var choice = await terminal.GetInput("Choice: ");

        terminal.SetColor("bright_yellow");
        terminal.WriteLine("");
        terminal.WriteLine("The chest opens to reveal genuine treasure - gold, gems, artifacts!");
        terminal.WriteLine("");

        long gold = 50000 + dungeonRandom.Next(25000);
        player.Gold += gold;
        terminal.WriteLine($"You found {gold} gold!", "bright_green");
        terminal.WriteLine("");

        if (choice == "1")
        {
            terminal.SetColor("cyan");
            terminal.WriteLine("\"Legendary treasure, found together,\" Vex laughs.");
            terminal.WriteLine("\"That's even better than the songs.\"");
            UsurperRemake.Systems.CompanionSystem.Instance.ModifyLoyalty(
                UsurperRemake.Systems.CompanionId.Vex, 15, "Shared treasure discovery");
        }
        else
        {
            terminal.SetColor("cyan");
            terminal.WriteLine("\"My name in the history books,\" Vex murmurs.");
            terminal.WriteLine("\"Even if just as a footnote. That's something.\"");
            UsurperRemake.Systems.CompanionSystem.Instance.ModifyLoyalty(
                UsurperRemake.Systems.CompanionId.Vex, 10, "Let him have his moment");
        }

        story.SetStoryFlag("vex_bucket_treasure", true);
        terminal.SetColor("gray");
        terminal.WriteLine("");
        terminal.WriteLine("[Bucket List: Find Legendary Treasure - COMPLETE]");
    }

    private async Task VexBucketJoke(Character player, UsurperRemake.Systems.Companion vex,
        UsurperRemake.Systems.StoryProgressionSystem story)
    {
        terminal.SetColor("white");
        terminal.WriteLine("You encounter a patrol of demon guards.");
        terminal.WriteLine("Before you can draw your weapon, Vex steps forward.");
        terminal.WriteLine("");
        await Task.Delay(1000);

        terminal.SetColor("yellow");
        terminal.WriteLine("\"Hey! Why did the demon cross the road?\"");
        terminal.WriteLine("");
        await Task.Delay(1000);

        terminal.SetColor("red");
        terminal.WriteLine("The demons look at each other, confused.");
        terminal.WriteLine("\"What?\" one snarls.");
        terminal.WriteLine("");
        await Task.Delay(1000);

        terminal.SetColor("yellow");
        terminal.WriteLine("\"Because he was DYING to get to the other side!\"");
        terminal.WriteLine("Vex spreads his arms. \"Get it? DYING?\"");
        terminal.WriteLine("");
        await Task.Delay(1500);

        terminal.SetColor("white");
        terminal.WriteLine("The demons stare. One snorts. Then another.");
        terminal.WriteLine("Suddenly, they're all laughing.");
        terminal.WriteLine("");
        await Task.Delay(1000);

        terminal.SetColor("red");
        terminal.WriteLine("\"That's terrible,\" the lead demon wheezes.");
        terminal.WriteLine("\"Get out of here before I change my mind.\"");
        terminal.WriteLine("");

        terminal.SetColor("cyan");
        terminal.WriteLine("As you hurry past, Vex is beaming.");
        terminal.WriteLine("\"Made a demon laugh. MADE A DEMON LAUGH!\"");
        terminal.WriteLine("\"Cross that off the list!\"");
        terminal.WriteLine("");

        story.SetStoryFlag("vex_bucket_joke", true);
        terminal.SetColor("gray");
        terminal.WriteLine("[Bucket List: Make a Demon Laugh - COMPLETE]");

        UsurperRemake.Systems.CompanionSystem.Instance.ModifyLoyalty(
            UsurperRemake.Systems.CompanionId.Vex, 10, "Witnessed his triumph");
    }

    private async Task VexBucketTruth(Character player, UsurperRemake.Systems.Companion vex,
        UsurperRemake.Systems.StoryProgressionSystem story)
    {
        terminal.SetColor("white");
        terminal.WriteLine("During a rest, Vex grows uncharacteristically quiet.");
        terminal.WriteLine("");
        await Task.Delay(1000);

        terminal.SetColor("yellow");
        terminal.WriteLine("\"Can I tell you something?\" he asks softly.");
        terminal.WriteLine("\"Something I've never told anyone?\"");
        terminal.WriteLine("");

        terminal.SetColor("yellow");
        terminal.WriteLine("[1] \"Of course. I'm listening.\"");
        terminal.WriteLine("[2] \"You don't have to tell me anything.\"");
        terminal.WriteLine("");

        var choice = await terminal.GetInput("Response: ");

        terminal.SetColor("white");
        terminal.WriteLine("");
        if (choice == "1")
        {
            terminal.WriteLine("You sit beside him, waiting.");
        }
        else
        {
            terminal.WriteLine("\"No,\" he says. \"I need to. While I still can.\"");
        }
        terminal.WriteLine("");
        await Task.Delay(1000);

        terminal.SetColor("cyan");
        terminal.WriteLine("\"I act like I don't care about dying,\" he begins.");
        terminal.WriteLine("\"All the jokes. The bravado. The 'life's too short' nonsense.\"");
        terminal.WriteLine("");
        await Task.Delay(1500);

        terminal.SetColor("white");
        terminal.WriteLine("His voice cracks.");
        terminal.WriteLine("");

        terminal.SetColor("cyan");
        terminal.WriteLine("\"I'm terrified. Every single day.\"");
        terminal.WriteLine("\"I don't want to go. I want to see what happens next.\"");
        terminal.WriteLine("\"I want to fall in love. Grow old. Have regrets.\"");
        terminal.WriteLine("");
        await Task.Delay(2000);

        terminal.SetColor("white");
        terminal.WriteLine("He laughs, but there's no humor in it.");
        terminal.WriteLine("");

        terminal.SetColor("cyan");
        terminal.WriteLine("\"But I don't get that. So I make jokes instead.\"");
        terminal.WriteLine("\"Because if I'm laughing, I can pretend I'm not screaming.\"");
        terminal.WriteLine("");
        await Task.Delay(1500);

        if (choice == "1")
        {
            terminal.SetColor("white");
            terminal.WriteLine("You put a hand on his shoulder. No words needed.");
            UsurperRemake.Systems.CompanionSystem.Instance.ModifyLoyalty(
                UsurperRemake.Systems.CompanionId.Vex, 20, "Listened to his truth");
        }
        else
        {
            terminal.SetColor("white");
            terminal.WriteLine("He wipes his eyes and forces a smile.");
            terminal.WriteLine("\"Thanks for not making me say it alone.\"");
            UsurperRemake.Systems.CompanionSystem.Instance.ModifyLoyalty(
                UsurperRemake.Systems.CompanionId.Vex, 15, "Respected his truth");
        }

        story.SetStoryFlag("vex_bucket_truth", true);
        terminal.SetColor("gray");
        terminal.WriteLine("");
        terminal.WriteLine("[Bucket List: Tell Someone the Truth - COMPLETE]");
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
        terminal.SetColor("bright_red");
        terminal.WriteLine("╔════════════════════════════════════════════════════════════════╗");
        terminal.WriteLine("║                    *** DIVINE WRATH ***                        ║");
        terminal.WriteLine("╚════════════════════════════════════════════════════════════════╝");
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
        terminal.WriteLine($"HP: {player.HP}/{player.MaxHP}");
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
        terminal.WriteLine("██████████████████████████████████████████████████████████████████");
        terminal.WriteLine("              THE HEAVENS THEMSELVES CRY OUT IN RAGE!");
        terminal.WriteLine("██████████████████████████████████████████████████████████████████");
        await Task.Delay(1500);

        terminal.SetColor("bright_magenta");
        terminal.WriteLine($"\"WRETCHED TRAITOR! {betrayedFor.ToUpper()} CANNOT PROTECT YOU FROM MY WRATH!\"");
        await Task.Delay(1500);

        // Severe HP damage
        long damage = Math.Max(1, player.HP / 2); // 50% current HP
        player.HP = Math.Max(1, player.HP - damage);

        terminal.SetColor("red");
        terminal.WriteLine($"Divine fire consumes you for {damage} damage!");
        terminal.WriteLine($"HP: {player.HP}/{player.MaxHP}");
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
