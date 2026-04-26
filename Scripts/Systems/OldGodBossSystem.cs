using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using UsurperRemake.Utils;
using UsurperRemake.Data;
using UsurperRemake.UI;
using UsurperRemake.BBS;

namespace UsurperRemake.Systems
{
    /// <summary>
    /// Old God Boss System - Handles epic multi-phase boss encounters with the Old Gods
    /// Each boss has unique mechanics, dialogue, and can potentially be saved or allied with
    /// </summary>
    public class OldGodBossSystem
    {
        private static OldGodBossSystem? instance;
        public static OldGodBossSystem Instance => instance ??= new OldGodBossSystem();

        private Dictionary<OldGodType, OldGodBossData> bossData = new();
        private OldGodBossData? currentBoss;
        private bool bossDefeated;

        // Dungeon teammates passed from DungeonLocation for boss fights
        private List<Character>? dungeonTeammates;

        // Combat modifiers based on dialogue choices
        private CombatModifiers activeCombatModifiers = new();

        // Prevents concurrent boss encounters from corrupting shared state in MUD mode
        private readonly SemaphoreSlim _bossEncounterLock = new(1, 1);

        /// <summary>
        /// Combat modifiers applied based on dialogue choices before boss fight
        /// </summary>
        private class CombatModifiers
        {
            // Player bonuses
            public double DamageMultiplier { get; set; } = 1.0;
            public double DefenseMultiplier { get; set; } = 1.0;
            public int BonusDamage { get; set; } = 0;
            public int BonusDefense { get; set; } = 0;
            public double CriticalChance { get; set; } = 0.05; // 5% base
            public double CriticalMultiplier { get; set; } = 1.5;
            public bool HasRageBoost { get; set; } = false; // Extra attacks
            public bool HasInsight { get; set; } = false; // See boss patterns

            // Boss penalties/bonuses
            public double BossDamageMultiplier { get; set; } = 1.0;
            public double BossDefenseMultiplier { get; set; } = 1.0;
            public bool BossConfused { get; set; } = false; // May miss attacks
            public bool BossWeakened { get; set; } = false; // Reduced stats

            // Special effects
            public string ApproachType { get; set; } = "neutral"; // aggressive, diplomatic, cunning, humble
            public string? SpecialEffect { get; set; } = null;

            public void Reset()
            {
                DamageMultiplier = 1.0;
                DefenseMultiplier = 1.0;
                BonusDamage = 0;
                BonusDefense = 0;
                CriticalChance = 0.05;
                CriticalMultiplier = 1.5;
                HasRageBoost = false;
                HasInsight = false;
                BossDamageMultiplier = 1.0;
                BossDefenseMultiplier = 1.0;
                BossConfused = false;
                BossWeakened = false;
                ApproachType = "neutral";
                SpecialEffect = null;
            }
        }

        /// <summary>
        /// Check if the current boss was defeated
        /// </summary>
        public bool IsBossDefeated => bossDefeated;

        public event Action<OldGodType>? OnBossDefeated;
        public event Action<OldGodType>? OnBossSaved;

        public OldGodBossSystem()
        {
            LoadBossData();
        }

        /// <summary>
        /// Load boss data from OldGodsData
        /// </summary>
        private void LoadBossData()
        {
            var allBosses = OldGodsData.GetAllOldGods();
            foreach (var boss in allBosses)
            {
                bossData[boss.Type] = boss;
            }
            // GD.Print($"[BossSystem] Loaded {bossData.Count} Old God bosses");
        }

        /// <summary>
        /// Check if player can encounter a specific Old God
        /// </summary>
        public bool CanEncounterBoss(Character player, OldGodType type)
        {
            if (!bossData.TryGetValue(type, out var boss))
                return false;

            var story = StoryProgressionSystem.Instance;

            // Check if already dealt with (defeated, saved, allied, etc.)
            if (story.OldGodStates.TryGetValue(type, out var state))
            {
                // Awakened gods can be re-encountered if player has the required artifact
                // (they were spared via dialogue and player found the artifact to complete the save)
                if (state.Status == GodStatus.Awakened)
                {
                    // Only gods with save quests can be in Awakened state
                    // If a non-saveable god is somehow Awakened, treat as defeated to unblock progression
                    if (!state.CanBeSaved)
                    {
                        state.Status = GodStatus.Defeated;
                        return false;
                    }
                    var requiredArtifact = GetArtifactForSave(type);
                    if (requiredArtifact != null && ArtifactSystem.Instance.HasArtifact(requiredArtifact.Value))
                        return true; // Allow re-encounter to complete the save
                    return false; // No artifact yet, can't re-encounter
                }

                // Gods that have been fully resolved cannot be encountered again
                if (state.Status == GodStatus.Defeated ||
                    state.Status == GodStatus.Saved ||
                    state.Status == GodStatus.Allied ||
                    state.Status == GodStatus.Consumed)
                    return false;
            }

            // Check level requirement based on dungeon floor where god appears, not boss combat level
            // Gods appear on specific floors (25, 40, 55, 70, 85, 95, 100)
            // Player should be within 10 levels of the floor to encounter the boss
            int floorLevel = boss.DungeonFloor;
            if (player.Level < floorLevel - 10) // Allow 10 levels of leeway
                return false;

            // Check prerequisites
            return CheckPrerequisites(type);
        }

        /// <summary>
        /// Get the artifact required to save a specific god via the save quest chain.
        /// Returns null for gods that don't have a save-quest artifact.
        /// </summary>
        private ArtifactType? GetArtifactForSave(OldGodType type)
        {
            return type switch
            {
                OldGodType.Veloura => ArtifactType.SoulweaversLoom,
                OldGodType.Aurelion => ArtifactType.SunforgedBlade,
                _ => null
            };
        }

        /// <summary>
        /// Complete the save quest for an Awakened god. Called when the player returns
        /// to the god's floor with the required artifact after sparing them via dialogue.
        /// </summary>
        public async Task<BossEncounterResult> CompleteSaveQuest(
            Character player, OldGodType type, TerminalEmulator terminal)
        {
            if (!bossData.TryGetValue(type, out var boss))
                return new BossEncounterResult { Success = false, Outcome = BossOutcome.Fled, God = type };

            var story = StoryProgressionSystem.Instance;
            string godName = boss.Name;

            // The god remembers the player's promise
            terminal.WriteLine("");
            await Task.Delay(1000);
            terminal.SetColor("bright_magenta");
            terminal.WriteLine($"{godName} appears before you again.");
            await Task.Delay(1500);
            terminal.WriteLine("");
            terminal.SetColor("bright_cyan");
            terminal.WriteLine(Loc.Get("old_god.save_came_back"));
            await Task.Delay(1500);
            terminal.WriteLine(Loc.Get("old_god.save_brought_it"));
            await Task.Delay(1500);
            terminal.WriteLine("");

            terminal.SetColor("bright_white");
            terminal.WriteLine(Loc.Get("old_god.save_artifact_works"));
            await Task.Delay(2000);
            terminal.SetColor("bright_magenta");
            terminal.WriteLine(Loc.Get("old_god.save_corruption_peels"));
            await Task.Delay(1500);
            terminal.WriteLine(Loc.Get("old_god.save_something_clean"));
            await Task.Delay(1500);
            terminal.WriteLine("");

            terminal.SetColor("bright_cyan");
            terminal.WriteLine(Loc.Get("old_god.save_staggers", godName));
            await Task.Delay(1500);
            terminal.SetColor("bright_yellow");
            terminal.WriteLine(Loc.Get("old_god.save_i_remember"));
            await Task.Delay(2000);
            terminal.WriteLine("");

            terminal.SetColor("bright_white");
            terminal.WriteLine(Loc.Get("ui.press_enter"));
            await terminal.WaitForKey("");

            // Update god state to fully Saved
            story.UpdateGodState(type, GodStatus.Saved);
            story.SetStoryFlag($"{type.ToString().ToLower()}_saved", true);

            // Award experience and Chivalry — v0.57.12: paired movement
            long xpReward = boss.Level * 1000;
            player.Experience += xpReward;
            AlignmentSystem.Instance.ChangeAlignment(player, 100, isGood: true, "old_god.save");

            terminal.SetColor("bright_green");
            terminal.WriteLine(Loc.Get("old_god.save_xp_reward", xpReward));
            terminal.WriteLine(Loc.Get("old_god.save_chivalry"));
            terminal.WriteLine("");

            // Grant Ocean Philosophy fragment
            OceanPhilosophySystem.Instance.CollectFragment(WaveFragment.TheCorruption);

            return new BossEncounterResult
            {
                Success = true,
                Outcome = BossOutcome.Saved,
                God = type,
                ApproachType = "merciful"
            };
        }

        /// <summary>
        /// Check if prerequisites are met for encountering a god
        /// </summary>
        private bool CheckPrerequisites(OldGodType type)
        {
            var story = StoryProgressionSystem.Instance;

            // Count gods that have been resolved in any way (defeated, saved, allied, awakened, consumed)
            int resolvedGods = story.OldGodStates.Values.Count(s =>
                s.Status == GodStatus.Defeated ||
                s.Status == GodStatus.Saved ||
                s.Status == GodStatus.Allied ||
                s.Status == GodStatus.Awakened ||
                s.Status == GodStatus.Consumed);

            switch (type)
            {
                case OldGodType.Maelketh:
                    // First god, no prerequisites
                    return true;

                case OldGodType.Veloura:
                    // Must have resolved Maelketh
                    return resolvedGods >= 1;

                case OldGodType.Thorgrim:
                    // Must have resolved at least one god
                    return resolvedGods >= 1;

                case OldGodType.Noctura:
                    // Must have resolved at least two gods
                    return resolvedGods >= 2;

                case OldGodType.Aurelion:
                    // Must have resolved at least three gods
                    return resolvedGods >= 3;

                case OldGodType.Terravok:
                    // Must have resolved at least four gods
                    return resolvedGods >= 4;

                case OldGodType.Manwe:
                    // Must have resolved all six other gods
                    return resolvedGods >= 6;

                default:
                    return false;
            }
        }

        /// <summary>
        /// Start a boss encounter
        /// </summary>
        /// <param name="player">The player character</param>
        /// <param name="type">Which Old God to fight</param>
        /// <param name="terminal">Terminal for output</param>
        /// <param name="teammates">Optional list of dungeon teammates (NPCs traveling with player)</param>
        public async Task<BossEncounterResult> StartBossEncounter(
            Character player, OldGodType type, TerminalEmulator terminal, List<Character>? teammates = null)
        {
            if (!bossData.TryGetValue(type, out var boss))
            {
                return new BossEncounterResult { Success = false };
            }

            // Serialize boss encounters to prevent concurrent MUD players from corrupting
            // shared singleton state (currentBoss, activeCombatModifiers, etc.)
            await _bossEncounterLock.WaitAsync();
            try
            {
                currentBoss = boss;
                bossDefeated = false;
                dungeonTeammates = teammates;

                // v0.57.2 — mark the god as encountered the moment the player enters the room.
                // Covers cases where the player flees/disconnects before resolution (UpdateGodState
                // would otherwise not fire, leaving the Progress screen showing "????" forever).
                if (StoryProgressionSystem.Instance.OldGodStates.TryGetValue(type, out var encounterState))
                {
                    encounterState.HasBeenEncountered = true;
                }

                // Play introduction
                await PlayBossIntroduction(boss, player, terminal);

                // Companion-specific reactions before dialogue
                await PlayCompanionBossReaction(type, player, terminal);

                // Run dialogue
                var dialogueResult = await DialogueSystem.Instance.StartDialogue(
                    player, $"{type.ToString().ToLower()}_encounter", terminal);

                // Check if dialogue led to non-combat resolution
                var story = StoryProgressionSystem.Instance;
                if (story.HasStoryFlag($"{type.ToString().ToLower()}_ally"))
                {
                    story.UpdateGodState(type, GodStatus.Allied);
                    return new BossEncounterResult
                    {
                        Success = true,
                        Outcome = BossOutcome.Allied,
                        God = type,
                        ApproachType = "allied"
                    };
                }

                if (story.HasStoryFlag($"{type.ToString().ToLower()}_spared"))
                {
                    // Gods now give their artifact directly during the save dialogue
                    // Route through HandleBossSaved for proper state update + artifact grant
                    return await HandleBossSaved(player, boss, terminal);
                }

                // Combat time! Either the player chose a combat path, or said nothing
                if (!story.HasStoryFlag($"{type.ToString().ToLower()}_combat_start"))
                {
                    terminal.WriteLine("");
                    terminal.WriteLine(Loc.Get("old_god.say_nothing"), "gray");
                    terminal.WriteLine("");
                    terminal.WriteLine(Loc.Get("old_god.god_stares", boss.Name), boss.ThemeColor);
                    terminal.WriteLine("");
                    terminal.WriteLine($"\"{(type == OldGodType.Maelketh ? Loc.Get("old_god.maelketh_silence_rage") : Loc.Get("old_god.god_have_it_your_way"))}\"", boss.ThemeColor);
                    terminal.WriteLine("");
                    terminal.WriteLine(Loc.Get("old_god.god_attacks"), "bright_red");
                    await Task.Delay(2000);
                }

                var combatResult = await RunBossCombat(player, boss, terminal);
                return combatResult;
            }
            finally
            {
                _bossEncounterLock.Release();
            }
        }

        /// <summary>
        /// Play boss introduction sequence
        /// </summary>
        private async Task PlayBossIntroduction(OldGodBossData boss, Character player, TerminalEmulator terminal)
        {
            terminal.Clear();
            terminal.WriteLine("");

            // Build dramatic entrance
            terminal.WriteLine(Loc.Get("old_god.ground_trembles"), "red");
            await Task.Delay(800);

            terminal.WriteLine(Loc.Get("old_god.ancient_power_stirs"), "bright_red");
            await Task.Delay(800);

            terminal.WriteLine(Loc.Get("old_god.seal_shatters", boss.Name), "bright_magenta");
            await Task.Delay(1200);

            terminal.WriteLine("");
            if (!GameConfig.ScreenReaderMode)
            {
                terminal.WriteLine($"╔════════════════════════════════════════════════════════════════╗", boss.ThemeColor);
                terminal.WriteLine($"║                                                                ║", boss.ThemeColor);
                terminal.WriteLine($"║     {CenterText(boss.Name.ToUpper(), 58)}     ║", boss.ThemeColor);
                terminal.WriteLine($"║     {CenterText(boss.Title, 58)}     ║", boss.ThemeColor);
                terminal.WriteLine($"║                                                                ║", boss.ThemeColor);
                terminal.WriteLine($"╚════════════════════════════════════════════════════════════════╝", boss.ThemeColor);
            }
            else
            {
                terminal.WriteLine($"{boss.Name.ToUpper()} - {boss.Title}", boss.ThemeColor);
            }
            terminal.WriteLine("");

            // Show Old God art (skip for screen readers, BBS mode, and art-disabled)
            if (player is Player pp && !pp.ScreenReaderMode && !DoorMode.IsInDoorMode && !GameConfig.DisableCharacterMonsterArt)
            {
                var art = OldGodArtDatabase.GetArtForGod(boss.Type);
                if (art != null)
                {
                    await ANSIArt.DisplayArtAnimated(terminal, art, 80);
                    terminal.WriteLine("");
                }
            }

            await Task.Delay(2000);

            // Show boss stats
            terminal.WriteLine($"  {Loc.Get("ui.level")}: {boss.Level}", "gray");
            terminal.WriteLine($"  {Loc.Get("ui.stat_hp")}: {boss.HP:N0}", "red");

            // Warning for unenchanted weapons against gods with divine armor
            double divineArmor = GetDivineArmorReduction(boss.Type, player);
            if (divineArmor > 0)
            {
                terminal.WriteLine("");
                terminal.SetColor("bright_red");
                string armorName = boss.Type switch
                {
                    OldGodType.Aurelion => Loc.Get("old_god.armor_divine_shield"),
                    OldGodType.Terravok => Loc.Get("old_god.armor_stone_skin"),
                    OldGodType.Manwe => Loc.Get("old_god.armor_creators_ward"),
                    _ => Loc.Get("old_god.armor_divine")
                };
                terminal.WriteLine(Loc.Get("old_god.warning_protected", boss.Name, armorName));
                terminal.SetColor("red");
                terminal.WriteLine(Loc.Get("old_god.warning_less_damage", $"{divineArmor * 100:N0}"));
                terminal.SetColor("yellow");
                terminal.WriteLine(Loc.Get("old_god.warning_enchant"));
            }

            terminal.WriteLine("");

            await terminal.GetInputAsync($"  {Loc.Get("old_god.press_enter_face")}");
        }

        /// <summary>
        /// Apply combat modifiers based on dialogue choices
        /// </summary>
        private void ApplyDialogueModifiers(OldGodType godType, TerminalEmulator terminal)
        {
            activeCombatModifiers.Reset();
            var story = StoryProgressionSystem.Instance;
            var godName = godType.ToString().ToLower();

            // Check for god-specific dialogue flags and apply modifiers
            switch (godType)
            {
                case OldGodType.Maelketh:
                    ApplyMaelkethModifiers(story, terminal);
                    break;
                case OldGodType.Veloura:
                    ApplyVelouraModifiers(story, terminal);
                    break;
                case OldGodType.Thorgrim:
                    ApplyThorgrimModifiers(story, terminal);
                    break;
                case OldGodType.Noctura:
                    ApplyNocturaModifiers(story, terminal);
                    break;
                case OldGodType.Aurelion:
                    ApplyAurelionModifiers(story, terminal);
                    break;
                case OldGodType.Terravok:
                    ApplyTerravokModifiers(story, terminal);
                    break;
                case OldGodType.Manwe:
                    ApplyManweModifiers(story, terminal);
                    break;
            }
        }

        private void ApplyMaelkethModifiers(StoryProgressionSystem story, TerminalEmulator terminal)
        {
            // Option 1: "I am here to destroy you" - Aggressive approach = Rage boost
            if (story.HasStoryFlag("maelketh_combat_start") && !story.HasStoryFlag("maelketh_teaching"))
            {
                activeCombatModifiers.ApproachType = "aggressive";
                activeCombatModifiers.HasRageBoost = true;
                activeCombatModifiers.DamageMultiplier = 1.25; // 25% more damage
                activeCombatModifiers.DefenseMultiplier = 0.85; // 15% less defense (reckless)
                activeCombatModifiers.CriticalChance = 0.15; // 15% crit chance
                terminal.WriteLine($"  {Loc.Get("old_god.mod_fury_bright")}", "bright_red");
            }
            // Option 3: "Teach me" - Humble approach = Learn his patterns
            else if (story.HasStoryFlag("maelketh_teaching"))
            {
                activeCombatModifiers.ApproachType = "humble";
                activeCombatModifiers.HasInsight = true;
                activeCombatModifiers.DefenseMultiplier = 1.20; // 20% more defense
                activeCombatModifiers.BossDamageMultiplier = 0.85; // Boss does 15% less damage
                terminal.WriteLine($"  {Loc.Get("old_god.mod_maelketh_teachings")}", "cyan");
            }
            // Option 2: Peace path (fails but shows character) - No modifier, normal fight
        }

        /// <summary>
        /// Companion-specific dialogue before an Old God boss encounter.
        /// Fires after the boss introduction but before the player dialogue choices.
        /// </summary>
        private async Task PlayCompanionBossReaction(OldGodType type, Character player, TerminalEmulator terminal)
        {
            var companionSystem = CompanionSystem.Instance;
            if (companionSystem == null) return;

            // Mira reacts to Veloura — her former goddess
            if (type == OldGodType.Veloura && companionSystem.IsCompanionActive(CompanionId.Mira))
            {
                terminal.WriteLine("");
                terminal.WriteLine(Loc.Get("old_god.mira_steps_forward"), "white");
                terminal.WriteLine("");
                terminal.WriteLine(Loc.Get("old_god.mira_veloura"), "bright_cyan");
                await Task.Delay(2000);
                terminal.WriteLine("");
                terminal.WriteLine(Loc.Get("old_god.mira_prayed"), "bright_cyan");
                terminal.WriteLine(Loc.Get("old_god.mira_believed"), "bright_cyan");
                await Task.Delay(2500);
                terminal.WriteLine("");
                terminal.WriteLine(Loc.Get("old_god.mira_temple_burned"), "bright_cyan");
                terminal.WriteLine(Loc.Get("old_god.mira_sister_aldara"), "bright_cyan");
                terminal.WriteLine(Loc.Get("old_god.mira_corruption"), "bright_cyan");
                await Task.Delay(3000);
                terminal.WriteLine("");
                terminal.WriteLine(Loc.Get("old_god.mira_turns_to_you"), "white");
                terminal.WriteLine(Loc.Get("old_god.mira_follow_you"), "bright_cyan");
                terminal.WriteLine(Loc.Get("old_god.mira_not_always_this"), "bright_cyan");
                terminal.WriteLine("");
                await Task.Delay(2000);
            }
        }

        private void ApplyVelouraModifiers(StoryProgressionSystem story, TerminalEmulator terminal)
        {
            // Aggressive approach
            if (story.HasStoryFlag("veloura_combat_start") && !story.HasStoryFlag("veloura_empathy") && !story.HasStoryFlag("veloura_mercy_kill"))
            {
                activeCombatModifiers.ApproachType = "aggressive";
                activeCombatModifiers.DamageMultiplier = 1.15;
                activeCombatModifiers.BossDamageMultiplier = 1.10; // She fights harder
                terminal.WriteLine($"  {Loc.Get("old_god.mod_veloura_hostile")}", "red");
            }
            // Empathy shown
            else if (story.HasStoryFlag("veloura_empathy"))
            {
                activeCombatModifiers.ApproachType = "diplomatic";
                activeCombatModifiers.BossWeakened = true;
                activeCombatModifiers.BossDamageMultiplier = 0.80; // She's conflicted
                terminal.WriteLine($"  {Loc.Get("old_god.mod_veloura_empathy")}", "bright_magenta");
            }
            // Mercy kill - she accepts death
            else if (story.HasStoryFlag("veloura_mercy_kill"))
            {
                activeCombatModifiers.ApproachType = "merciful";
                activeCombatModifiers.BossWeakened = true;
                activeCombatModifiers.BossDefenseMultiplier = 0.70; // She doesn't fully resist
                terminal.WriteLine($"  {Loc.Get("old_god.mod_veloura_mercy")}", "bright_cyan");
            }

            // Mira's presence weakens Veloura's resolve — she recognizes her former priestess
            if (CompanionSystem.Instance?.IsCompanionActive(CompanionId.Mira) == true)
            {
                activeCombatModifiers.BossDamageMultiplier *= 0.90; // Veloura hesitates
                terminal.WriteLine("  Veloura's gaze falls on Mira. Something flickers in the corruption.", "bright_magenta");
                terminal.WriteLine("  She hesitates.", "bright_magenta");
            }
        }

        private void ApplyThorgrimModifiers(StoryProgressionSystem story, TerminalEmulator terminal)
        {
            // Defiant approach
            if (story.HasStoryFlag("thorgrim_combat_start") && !story.HasStoryFlag("thorgrim_honorable_combat") && !story.HasStoryFlag("thorgrim_broken_logic"))
            {
                activeCombatModifiers.ApproachType = "defiant";
                activeCombatModifiers.DamageMultiplier = 1.10;
                activeCombatModifiers.BossDamageMultiplier = 1.15; // He judges harshly
                terminal.WriteLine($"  {Loc.Get("old_god.mod_thorgrim_defiant")}", "yellow");
            }
            // Honorable combat via Right of Challenge
            else if (story.HasStoryFlag("thorgrim_honorable_combat"))
            {
                activeCombatModifiers.ApproachType = "honorable";
                activeCombatModifiers.DefenseMultiplier = 1.15;
                activeCombatModifiers.BonusDefense = 20;
                terminal.WriteLine($"  {Loc.Get("old_god.mod_thorgrim_honor")}", "gray");
            }
            // Broken his logic with paradox
            else if (story.HasStoryFlag("thorgrim_broken_logic"))
            {
                activeCombatModifiers.ApproachType = "cunning";
                activeCombatModifiers.BossConfused = true;
                activeCombatModifiers.BossDamageMultiplier = 0.75;
                activeCombatModifiers.BossDefenseMultiplier = 0.85;
                terminal.WriteLine($"  {Loc.Get("old_god.mod_thorgrim_broken")}", "bright_cyan");
            }
        }

        private void ApplyNocturaModifiers(StoryProgressionSystem story, TerminalEmulator terminal)
        {
            int receptivity = StrangerEncounterSystem.Instance.Receptivity;

            // If allied (shouldn't reach combat, but just in case)
            if (story.HasStoryFlag("noctura_ally"))
            {
                activeCombatModifiers.ApproachType = "allied";
                // No combat should occur
                return;
            }

            // Teaching fight: mid receptivity or last chance path
            if (story.HasStoryFlag("noctura_teaching_fight"))
            {
                activeCombatModifiers.ApproachType = "teaching";
                activeCombatModifiers.BossDamageMultiplier = 0.50; // 50% boss power
                activeCombatModifiers.BossDefenseMultiplier = 0.70;
                activeCombatModifiers.DamageMultiplier = 1.15;
                terminal.WriteLine($"  {Loc.Get("old_god.mod_noctura_teaching")}", "dark_magenta");
                terminal.WriteLine($"  {Loc.Get("old_god.mod_noctura_teaching_stats")}", "cyan");
                return;
            }

            // Enraged: negative receptivity, player rejected every teaching
            if (story.HasStoryFlag("noctura_enraged") || receptivity < 0)
            {
                activeCombatModifiers.ApproachType = "enraged";
                activeCombatModifiers.BossDamageMultiplier = 1.25;
                activeCombatModifiers.CriticalChance = 0.03; // Harder to land crits
                terminal.WriteLine($"  {Loc.Get("old_god.mod_noctura_enraged")}", "dark_magenta");
                terminal.WriteLine($"  {Loc.Get("old_god.mod_noctura_enraged_stats")}", "red");
                return;
            }

            // Reluctant fight: receptivity 25+ but took the fight path anyway
            if (receptivity >= 25)
            {
                activeCombatModifiers.ApproachType = "reluctant";
                activeCombatModifiers.BossDamageMultiplier = 0.70;
                activeCombatModifiers.BossDefenseMultiplier = 0.85;
                activeCombatModifiers.DamageMultiplier = 1.10;
                terminal.WriteLine($"  {Loc.Get("old_god.mod_noctura_reluctant")}", "dark_magenta");
                terminal.WriteLine($"  {Loc.Get("old_god.mod_noctura_reluctant_stats")}", "cyan");
                return;
            }

            // Standard fight: low receptivity (0-24), never engaged with teachings
            activeCombatModifiers.ApproachType = "aggressive";
            activeCombatModifiers.DamageMultiplier = 1.10;
            activeCombatModifiers.CriticalChance = 0.03; // Harder to land crits
            terminal.WriteLine($"  {Loc.Get("old_god.mod_noctura_standard")}", "dark_magenta");
        }

        /// <summary>
        /// Queue a Stranger scripted encounter after the first Old God resolution.
        /// </summary>
        private void QueueStrangerOldGodEncounter(StrangerContextEvent eventType)
        {
            StrangerEncounterSystem.Instance.RecordGameEvent(eventType);

            // Only queue AfterFirstOldGod on the very first Old God encounter
            if (!StrangerEncounterSystem.Instance.CompletedScriptedEncounters.Contains(ScriptedEncounterType.AfterFirstOldGod))
            {
                StrangerEncounterSystem.Instance.QueueScriptedEncounter(ScriptedEncounterType.AfterFirstOldGod);
            }
        }

        private void ApplyAurelionModifiers(StoryProgressionSystem story, TerminalEmulator terminal)
        {
            var godName = "aurelion";
            // Check for defiant approach
            if (story.HasStoryFlag($"{godName}_defiant"))
            {
                activeCombatModifiers.ApproachType = "defiant";
                activeCombatModifiers.DamageMultiplier = 1.20;
                activeCombatModifiers.DefenseMultiplier = 0.90;
                terminal.WriteLine($"  {Loc.Get("old_god.mod_aurelion_defiant")}", "bright_yellow");
            }
            // Humble approach
            else if (story.HasStoryFlag($"{godName}_humble"))
            {
                activeCombatModifiers.ApproachType = "humble";
                activeCombatModifiers.BossDamageMultiplier = 0.85;
                activeCombatModifiers.DefenseMultiplier = 1.15;
                terminal.WriteLine($"  {Loc.Get("old_god.mod_aurelion_humble")}", "bright_cyan");
            }
            // Default combat
            else if (story.HasStoryFlag($"{godName}_combat_start"))
            {
                activeCombatModifiers.ApproachType = "neutral";
                terminal.WriteLine($"  {Loc.Get("old_god.mod_aurelion_neutral")}", "bright_yellow");
            }
        }

        private void ApplyTerravokModifiers(StoryProgressionSystem story, TerminalEmulator terminal)
        {
            var godName = "terravok";
            // Aggressive/destructive approach
            if (story.HasStoryFlag($"{godName}_destructive"))
            {
                activeCombatModifiers.ApproachType = "aggressive";
                activeCombatModifiers.DamageMultiplier = 1.30;
                activeCombatModifiers.BossDamageMultiplier = 1.20;
                terminal.WriteLine($"  {Loc.Get("old_god.mod_terravok_destructive")}", "dark_green");
            }
            // Respect for nature
            else if (story.HasStoryFlag($"{godName}_respectful"))
            {
                activeCombatModifiers.ApproachType = "respectful";
                activeCombatModifiers.BossWeakened = true;
                activeCombatModifiers.BossDamageMultiplier = 0.90;
                terminal.WriteLine($"  {Loc.Get("old_god.mod_terravok_respectful")}", "green");
            }
            // Default
            else if (story.HasStoryFlag($"{godName}_combat_start"))
            {
                activeCombatModifiers.ApproachType = "neutral";
                terminal.WriteLine($"  {Loc.Get("old_god.mod_terravok_neutral")}", "dark_green");
            }
        }

        private void ApplyManweModifiers(StoryProgressionSystem story, TerminalEmulator terminal)
        {
            // Dialogue-based modifiers from the Manwe encounter
            if (story.HasStoryFlag("manwe_compassion"))
            {
                activeCombatModifiers.ApproachType = "compassionate";
                activeCombatModifiers.BossDamageMultiplier *= 0.80;
                activeCombatModifiers.BossDefenseMultiplier = 0.85;
                terminal.WriteLine($"  {Loc.Get("old_god.mod_manwe_compassion")}", "bright_cyan");
            }
            else if (story.HasStoryFlag("manwe_righteous"))
            {
                activeCombatModifiers.ApproachType = "righteous";
                activeCombatModifiers.DefenseMultiplier *= 1.15;
                activeCombatModifiers.BossDamageMultiplier *= 0.90;
                terminal.WriteLine($"  {Loc.Get("old_god.mod_manwe_righteous")}", "bright_green");
            }
            else
            {
                // Aggressive path — no special dialogue flag, just manwe_combat_start
                activeCombatModifiers.ApproachType = "aggressive";
                activeCombatModifiers.DamageMultiplier *= 1.25;
                activeCombatModifiers.CriticalChance = Math.Max(activeCombatModifiers.CriticalChance, 0.15);
                activeCombatModifiers.DefenseMultiplier *= 0.85;
                terminal.WriteLine($"  {Loc.Get("old_god.mod_fury_bright")}", "bright_red");
            }

            // Additive bonuses based on choices with previous gods
            int godsSaved = story.OldGodStates.Values.Count(s => s.Status == GodStatus.Saved);
            int godsAllied = story.OldGodStates.Values.Count(s => s.Status == GodStatus.Allied);
            int godsDestroyed = story.OldGodStates.Values.Count(s => s.Status == GodStatus.Defeated);

            if (godsAllied >= 2)
            {
                activeCombatModifiers.DamageMultiplier += 0.20;
                activeCombatModifiers.DefenseMultiplier += 0.20;
                terminal.WriteLine($"  {Loc.Get("old_god.mod_manwe_allies")}", "bright_magenta");
            }
            else if (godsSaved >= 3)
            {
                activeCombatModifiers.BossWeakened = true;
                activeCombatModifiers.BossDamageMultiplier *= 0.85;
                terminal.WriteLine($"  {Loc.Get("old_god.mod_manwe_saved_gods")}", "bright_cyan");
            }
            else if (godsDestroyed >= 5)
            {
                activeCombatModifiers.DamageMultiplier += 0.35;
                activeCombatModifiers.CriticalChance = Math.Max(activeCombatModifiers.CriticalChance, 0.20);
                terminal.WriteLine($"  {Loc.Get("old_god.mod_manwe_destroyed")}", "dark_red");
            }

            // Defiant to stranger bonus
            if (story.HasStoryFlag("defiant_to_stranger"))
            {
                activeCombatModifiers.BonusDamage += 50;
                terminal.WriteLine($"  {Loc.Get("old_god.mod_defiant_spirit")}", "bright_red");
            }

            // Willing hero bonus
            if (story.HasStoryFlag("willing_hero"))
            {
                activeCombatModifiers.BonusDefense += 30;
                terminal.WriteLine($"  {Loc.Get("old_god.mod_willing_hero")}", "bright_green");
            }
        }

        /// <summary>
        /// Run the boss combat encounter - delegates to CombatEngine with BossCombatContext
        /// </summary>
        private async Task<BossEncounterResult> RunBossCombat(
            Character player, OldGodBossData boss, TerminalEmulator terminal)
        {
            // Apply combat modifiers based on dialogue choices
            ApplyDialogueModifiers(boss.Type, terminal);
            terminal.WriteLine("");
            await Task.Delay(1500);

            // Create a Monster from boss data
            var bossMonster = CreateBossMonster(boss);

            // Apply dialogue-based stat adjustments to the monster
            ApplyModifiersToMonster(bossMonster);

            // Apply player bonuses from dialogue
            ApplyModifiersToPlayer(player);

            // Set up boss context on combat engine
            var combatEngine = new CombatEngine(terminal);
            combatEngine.BossContext = new BossCombatContext
            {
                BossData = boss,
                GodType = boss.Type,
                AttacksPerRound = boss.AttacksPerRound,
                CanSave = boss.CanBeSaved && (GetArtifactForSave(boss.Type) == null || ArtifactSystem.Instance.HasArtifact(GetArtifactForSave(boss.Type)!.Value)),
                DamageMultiplier = activeCombatModifiers.DamageMultiplier,
                DefenseMultiplier = activeCombatModifiers.DefenseMultiplier,
                BonusDamage = activeCombatModifiers.BonusDamage,
                BonusDefense = activeCombatModifiers.BonusDefense,
                CriticalChance = activeCombatModifiers.CriticalChance,
                HasRageBoost = activeCombatModifiers.HasRageBoost,
                HasInsight = activeCombatModifiers.HasInsight,
                BossDamageMultiplier = activeCombatModifiers.BossDamageMultiplier,
                BossDefenseMultiplier = activeCombatModifiers.BossDefenseMultiplier,
                BossConfused = activeCombatModifiers.BossConfused,
                BossWeakened = activeCombatModifiers.BossWeakened,
            };

            // Configure boss-specific party balance mechanics (v0.52.1)
            ConfigureBossPartyMechanics(combatEngine.BossContext, boss.Type);

            // Set divine armor reduction on the combat context so CombatEngine applies it
            combatEngine.BossContext.DivineArmorReduction = GetDivineArmorReduction(boss.Type, player);

            // Set static flags for Manwe battle
            CombatEngine.IsManweBattle = boss.Type == OldGodType.Manwe;
            combatEngine.ResetManweBossFlags();

            // Run combat through the standard engine
            var combatResult = await combatEngine.PlayerVsMonsters(
                player, new List<Monster> { bossMonster }, dungeonTeammates);

            // Capture boss context state before clearing
            bool wasSaved = combatEngine.BossContext?.BossSaved ?? false;

            // Clean up
            CombatEngine.IsManweBattle = false;
            combatEngine.BossContext = null;
            ClearPlayerModifiers(player);

            // Convert CombatResult to BossEncounterResult
            return await ConvertToBossResult(combatResult, boss, wasSaved, terminal);
        }

        /// <summary>
        /// Create a Monster object from OldGodBossData for use with CombatEngine
        /// </summary>
        private Monster CreateBossMonster(OldGodBossData boss)
        {
            // v0.56.1 Divine Scaling: remaining Old Gods scale up with every artifact the
            // player has collected. Prevents trivialization once geared — feedback explicitly
            // said Veloura onward became easy with a tank+healer plus artifacts.
            int artifactCount = ArtifactSystem.Instance.GetCollectedCount();
            float hpScale = 1.0f + Math.Min(0.40f, artifactCount * GameConfig.OldGodDivineScalingHPPerArtifact);
            float dmgScale = 1.0f + Math.Min(0.20f, artifactCount * GameConfig.OldGodDivineScalingDamagePerArtifact);

            long scaledHP = (long)(boss.HP * hpScale);
            long scaledStrength = (long)(boss.Strength * dmgScale);

            // Split scaled Strength into Monster Strength + WeapPow for CombatEngine's damage formula
            long monsterStrength = scaledStrength / 2;
            long monsterWeapPow = scaledStrength / 2;

            // Split boss Defence into Monster Defence + ArmPow
            int monsterDefence = (int)(boss.Defence / 2);
            long monsterArmPow = boss.Defence / 2;

            var monster = new Monster
            {
                Name = boss.Name,
                Level = boss.Level,
                HP = scaledHP,
                MaxHP = scaledHP,
                Strength = monsterStrength,
                WeapPow = monsterWeapPow,
                Defence = monsterDefence,
                ArmPow = monsterArmPow,
                MagicRes = (int)(50 + boss.Wisdom / 10),
                MonsterColor = boss.ThemeColor,
                FamilyName = "OldGod",
                IsBoss = true,
                IsActive = true,
                CanSpeak = true,
                Phrase = boss.IntroDialogue.Length > 0 ? boss.IntroDialogue[0] : "",
                Experience = boss.Level * 2000,
                Gold = boss.Level * 500,
            };

            // Set special abilities from phase 1 abilities for display
            monster.SpecialAbilities = new List<string>(boss.Phase1Abilities);

            return monster;
        }

        /// <summary>
        /// Apply dialogue-based modifiers to the boss monster's stats
        /// </summary>
        private void ApplyModifiersToMonster(Monster monster)
        {
            if (activeCombatModifiers.BossWeakened)
            {
                monster.Strength = (long)(monster.Strength * 0.85);
                monster.WeapPow = (long)(monster.WeapPow * 0.85);
            }

            if (activeCombatModifiers.BossDefenseMultiplier != 1.0)
            {
                monster.Defence = (int)(monster.Defence * activeCombatModifiers.BossDefenseMultiplier);
                monster.ArmPow = (long)(monster.ArmPow * activeCombatModifiers.BossDefenseMultiplier);
            }

            if (activeCombatModifiers.BossConfused)
            {
                monster.IsConfused = true;
                monster.ConfusedDuration = 999;
            }
        }

        /// <summary>
        /// Apply dialogue-based bonuses to the player before boss combat
        /// </summary>
        private void ApplyModifiersToPlayer(Character player)
        {
            if (activeCombatModifiers.DamageMultiplier > 1.0)
            {
                int bonus = (int)((activeCombatModifiers.DamageMultiplier - 1.0) * (player.Strength + player.WeapPow));
                player.TempAttackBonus += bonus;
                player.TempAttackBonusDuration = 999;
            }

            if (activeCombatModifiers.DefenseMultiplier > 1.0)
            {
                int bonus = (int)((activeCombatModifiers.DefenseMultiplier - 1.0) * (player.Defence + player.ArmPow));
                player.TempDefenseBonus += bonus;
                player.TempDefenseBonusDuration = 999;
            }

            if (activeCombatModifiers.HasRageBoost)
            {
                player.HasBloodlust = true;
            }

            if (activeCombatModifiers.HasInsight)
            {
                player.DodgeNextAttack = true;
            }
        }

        /// <summary>
        /// Clear temporary player modifiers after boss combat
        /// </summary>
        private void ClearPlayerModifiers(Character player)
        {
            player.HasBloodlust = false;
            player.DodgeNextAttack = false;
            player.TempAttackBonus = 0;
            player.TempDefenseBonus = 0;
            player.TempAttackBonusDuration = 0;
            player.TempDefenseBonusDuration = 0;
        }

        /// <summary>
        /// Convert CombatResult from CombatEngine to BossEncounterResult
        /// </summary>
        private async Task<BossEncounterResult> ConvertToBossResult(
            CombatResult combatResult, OldGodBossData boss, bool wasSaved, TerminalEmulator terminal)
        {
            if (wasSaved)
            {
                return await HandleBossSaved(combatResult.Player, boss, terminal);
            }
            else if (combatResult.Outcome == CombatOutcome.Victory)
            {
                bossDefeated = true;
                return await HandleBossDefeated(combatResult.Player, boss, terminal);
            }
            else if (combatResult.Outcome == CombatOutcome.PlayerEscaped)
            {
                return new BossEncounterResult
                {
                    Success = false,
                    Outcome = BossOutcome.Fled,
                    God = boss.Type
                };
            }
            else // PlayerDied
            {
                return await HandlePlayerDefeated(combatResult.Player, boss, terminal);
            }
        }

        // NOTE: The following old boss combat methods have been removed as combat now routes
        // through CombatEngine.PlayerVsMonsters() with BossCombatContext:
        // - DisplayCombatStatus (boss version)
        // - CheckPhaseTransition
        // - SpawnSpectralSoldiers
        // - GetPlayerAction
        // - PlayerAttack
        // - PlayerSpecialAttack
        // - GetDivineArmorReduction
        // - PlayerHeal
        // - AttemptToSaveBoss
        // - BossTurn
        // These are now handled by CombatEngine's standard combat loop with boss-specific hooks.

        /// <summary>
        /// Handle boss being saved
        /// </summary>
        private async Task<BossEncounterResult> HandleBossSaved(
            Character player, OldGodBossData boss, TerminalEmulator terminal)
        {
            terminal.Clear();
            terminal.WriteLine("");
            UIHelper.WriteBoxHeader(terminal, Loc.Get("old_god.header_saved", boss.Name.ToUpper()), "bright_green", 63);
            terminal.WriteLine("");

            await Task.Delay(1500);

            terminal.WriteLine(Loc.Get("old_god.saved_darkness_lifts", boss.Name), "white");
            terminal.WriteLine($"  {Loc.Get("old_god.saved_seeing_world")}", "white");
            terminal.WriteLine("");

            foreach (var line in boss.SaveDialogue)
            {
                PrintDialogueLine(terminal, line,"bright_cyan");
                await Task.Delay(300);
            }

            terminal.WriteLine("");

            // Update story state
            var story = StoryProgressionSystem.Instance;
            story.UpdateGodState(boss.Type, GodStatus.Saved);
            story.SetStoryFlag($"{boss.Type.ToString().ToLower()}_saved", true);

            // Award experience
            long xpReward = boss.Level * 1000;
            player.Experience += xpReward;
            terminal.WriteLine(Loc.Get("old_god.saved_xp", $"{xpReward:N0}"), "cyan");

            // Award chivalry — v0.57.12: paired movement
            AlignmentSystem.Instance.ChangeAlignment(player, 100, isGood: true, "old_god.saved");
            terminal.WriteLine(Loc.Get("old_god.saved_chivalry"), "bright_green");

            // Saved gods give their artifact as a gift (instead of looting from their corpse)
            terminal.WriteLine("");
            terminal.WriteLine($"  {boss.Name} entrusts you with a sacred relic...", "bright_magenta");
            await ArtifactSystem.Instance.CollectArtifact(player, boss.ArtifactDropped, terminal);

            // Award thematic crafting materials (same as defeat)
            var thematicMaterial = GameConfig.CraftingMaterials.FirstOrDefault(
                m => m.ThematicGod == boss.Type.ToString());
            if (thematicMaterial != null)
            {
                player.AddMaterial(thematicMaterial.Id, 2);
                terminal.WriteLine("");
                terminal.SetColor(thematicMaterial.Color);
                terminal.WriteLine(Loc.Get("old_god.material_left_behind", boss.Name, thematicMaterial.Name, 2));
                terminal.SetColor("gray");
                terminal.WriteLine($"  \"{thematicMaterial.Description}\"");
            }
            if (boss.DungeonFloor >= 50)
            {
                player.AddMaterial("heart_of_the_ocean", 1);
                terminal.SetColor("cyan");
                terminal.WriteLine($"  {Loc.Get("old_god.heart_of_ocean")}");
            }

            OnBossSaved?.Invoke(boss.Type);

            // Queue Stranger encounter after first Old God
            QueueStrangerOldGodEncounter(StrangerContextEvent.OldGodSaved);

            await terminal.GetInputAsync($"  {Loc.Get("ui.press_enter")}");

            // Moment of silence — let the gravity of salvation settle
            await UsurperRemake.UI.UIHelper.MomentOfSilence(terminal, 4000);

            return new BossEncounterResult
            {
                Success = true,
                Outcome = BossOutcome.Saved,
                God = boss.Type,
                XPGained = xpReward,
                ApproachType = activeCombatModifiers.ApproachType
            };
        }

        /// <summary>
        /// Handle boss being defeated
        /// </summary>
        private async Task<BossEncounterResult> HandleBossDefeated(
            Character player, OldGodBossData boss, TerminalEmulator terminal)
        {
            terminal.Clear();
            terminal.WriteLine("");
            UIHelper.WriteBoxHeader(terminal, Loc.Get("old_god.header_defeated", boss.Name.ToUpper()), "bright_yellow", 63);
            terminal.WriteLine("");

            await Task.Delay(1500);

            foreach (var line in boss.DefeatDialogue)
            {
                PrintDialogueLine(terminal, line,boss.ThemeColor);
                await Task.Delay(1500); // Give players time to read each line
            }

            terminal.WriteLine("");
            await terminal.GetInputAsync($"  {Loc.Get("ui.press_enter")}");
            terminal.WriteLine("");
            terminal.WriteLine(Loc.Get("old_god.defeated_fades", boss.Name), "white");
            terminal.WriteLine("");

            // Update story state
            var story = StoryProgressionSystem.Instance;
            story.UpdateGodState(boss.Type, GodStatus.Defeated);
            story.SetStoryFlag($"{boss.Type.ToString().ToLower()}_destroyed", true);

            // Award artifact (Manwe drops none — Void Key auto-triggers when 6 artifacts collected)
            if (boss.Type != OldGodType.Manwe)
            {
                await ArtifactSystem.Instance.CollectArtifact(player, boss.ArtifactDropped, terminal);
            }

            // Award thematic crafting materials
            var thematicMaterial = GameConfig.CraftingMaterials.FirstOrDefault(
                m => m.ThematicGod == boss.Type.ToString());
            if (thematicMaterial != null)
            {
                player.AddMaterial(thematicMaterial.Id, 2);
                terminal.WriteLine("");
                terminal.SetColor(thematicMaterial.Color);
                terminal.WriteLine(Loc.Get("old_god.defeated_crystallizes", thematicMaterial.Name, 2));
                terminal.SetColor("gray");
                terminal.WriteLine($"  \"{thematicMaterial.Description}\"");
            }
            if (boss.DungeonFloor >= 50)
            {
                player.AddMaterial("heart_of_the_ocean", 1);
                terminal.SetColor("cyan");
                terminal.WriteLine($"  {Loc.Get("old_god.heart_of_ocean")}");
            }
            terminal.WriteLine("");

            // Award experience
            long xpReward = boss.Level * 2000;
            player.Experience += xpReward;
            terminal.WriteLine(Loc.Get("old_god.defeated_xp", $"{xpReward:N0}"), "cyan");

            // Award gold
            int goldReward = boss.Level * 500;
            player.Gold += goldReward;
            terminal.WriteLine(Loc.Get("old_god.defeated_gold", $"{goldReward:N0}"), "yellow");

            // Fame from defeating an Old God
            player.Fame += 50;

            OnBossDefeated?.Invoke(boss.Type);

            // Queue Stranger encounter after first Old God
            QueueStrangerOldGodEncounter(StrangerContextEvent.OldGodDefeated);

            await terminal.GetInputAsync($"  {Loc.Get("ui.press_enter")}");

            // Moment of silence — the weight of a god's fall
            await UsurperRemake.UI.UIHelper.MomentOfSilence(terminal, 4000);

            // === NOCTURA BETRAYAL — Post-Manwe surprise boss fight ===
            if (boss.Type == OldGodType.Manwe && StoryProgressionSystem.Instance.HasStoryFlag("noctura_ally"))
            {
                // v0.57.16: wrap the betrayal in a try/catch. Spudman report — the betrayal
                // combat NRE'd mid-fight, so HandleNocturaBetrayal never returned, the
                // post-betrayal flags never got set, and the caller never got back to fire
                // the post-Manwe ending sequence. The player ended up immortal-soft-locked:
                // can't NG+, can't retrigger the encounter, no ending flag for the failsafe.
                // Now: any exception here treats the betrayal as "Noctura escaped with power"
                // (the loss outcome) and continues normally so the ending sequence fires.
                bool betrayalResult = false;
                try
                {
                    betrayalResult = await HandleNocturaBetrayal(player, terminal, dungeonTeammates);
                }
                catch (Exception ex)
                {
                    DebugLogger.Instance.LogError("BETRAYAL", $"HandleNocturaBetrayal crashed: {ex.GetType().Name}: {ex.Message}\n{ex.StackTrace}");
                    terminal.WriteLine("");
                    terminal.SetColor("dark_magenta");
                    terminal.WriteLine("  As you reach for Noctura, the world fractures around you...");
                    terminal.WriteLine("  Her form dissolves into shadow, slipping through your fingers.");
                    terminal.WriteLine("  She has escaped — for now.");
                    terminal.WriteLine("");
                    await terminal.GetInputAsync($"  {Loc.Get("ui.press_enter")}");
                    betrayalResult = false; // treat as escape
                }

                // Set story flags regardless of outcome
                story.SetStoryFlag("noctura_betrayed", true);
                story.SetStoryFlag("noctura_ally", false); // Alliance is over

                // Record in MetaProgression (persists across NG+ cycles)
                try { MetaProgressionSystem.Instance.RecordNocturaBetrayal(betrayalResult); }
                catch (Exception ex) { DebugLogger.Instance.LogWarning("BETRAYAL", $"RecordNocturaBetrayal failed: {ex.Message}"); }

                if (betrayalResult)
                {
                    // Player defeated Noctura
                    story.SetStoryFlag("noctura_betrayal_defeated", true);
                    story.UpdateGodState(OldGodType.Noctura, GodStatus.Defeated);
                }
                else
                {
                    // Noctura escaped with power
                    story.SetStoryFlag("noctura_escaped_with_power", true);
                }
            }

            return new BossEncounterResult
            {
                Success = true,
                Outcome = BossOutcome.Defeated,
                God = boss.Type,
                XPGained = xpReward,
                GoldGained = goldReward,
                ApproachType = activeCombatModifiers.ApproachType
            };
        }

        /// <summary>
        /// Handle the Noctura betrayal boss fight that triggers after defeating Manwe
        /// when the player previously allied with Noctura on floor 70.
        /// Returns true if player won, false if player lost.
        /// </summary>
        private async Task<bool> HandleNocturaBetrayal(Character player, TerminalEmulator terminal, List<Character>? teammates)
        {
            var betrayalData = OldGodsData.GetNocturaBetrayal();

            // Show betrayal intro
            terminal.Clear();
            terminal.WriteLine("");
            foreach (var line in betrayalData.IntroDialogue)
            {
                if (string.IsNullOrEmpty(line)) { terminal.WriteLine(""); continue; }
                terminal.SetColor(line.StartsWith("NOCTURA:") ? "dark_magenta" : "gray");
                terminal.WriteLine($"  {line}");
                await Task.Delay(1500);
            }

            await terminal.GetInputAsync($"\n  {Loc.Get("ui.press_enter")}");

            // Partial heal — Noctura wants a "fair" fight
            long healAmount = (player.MaxHP - player.HP) / 2;
            if (healAmount > 0)
            {
                player.HP += healAmount;
                terminal.SetColor("dark_magenta");
                terminal.WriteLine($"\n  Shadow energy courses through you, restoring {healAmount} HP...");
                terminal.SetColor("gray");
                terminal.WriteLine("  NOCTURA: \"I want you at your best. It's no fun otherwise.\"");
                await Task.Delay(2000);
            }

            // Create Noctura as a monster for combat using the same pattern as CreateBossMonster
            // v0.56.1: Apply Divine Scaling — Noctura is an Old God and should scale with artifacts too
            int nocturaArtifactCount = ArtifactSystem.Instance.GetCollectedCount();
            float nocturaHpScale = 1.0f + Math.Min(0.40f, nocturaArtifactCount * GameConfig.OldGodDivineScalingHPPerArtifact);
            float nocturaDmgScale = 1.0f + Math.Min(0.20f, nocturaArtifactCount * GameConfig.OldGodDivineScalingDamagePerArtifact);
            long nocturaScaledHP = (long)(betrayalData.HP * nocturaHpScale);
            long nocturaScaledStrength = (long)(betrayalData.Strength * nocturaDmgScale);

            long monsterStrength = nocturaScaledStrength / 2;
            long monsterWeapPow = nocturaScaledStrength / 2;
            int monsterDefence = (int)(betrayalData.Defence / 2);
            long monsterArmPow = betrayalData.Defence / 2;

            var noctura = new Monster
            {
                Name = betrayalData.Name,
                Level = betrayalData.Level,
                HP = nocturaScaledHP,
                MaxHP = nocturaScaledHP,
                Strength = monsterStrength,
                WeapPow = monsterWeapPow,
                Defence = monsterDefence,
                ArmPow = monsterArmPow,
                MagicRes = (int)(50 + betrayalData.Wisdom / 10),
                MonsterColor = betrayalData.ThemeColor,
                FamilyName = "OldGod",
                IsBoss = true,
                IsActive = true,
                CanSpeak = true,
                Phrase = betrayalData.IntroDialogue.Length > 0 ? betrayalData.IntroDialogue[0] : "",
                Experience = betrayalData.Level * 2000,
                Gold = betrayalData.Level * 500,
            };
            noctura.SpecialAbilities = new List<string>(betrayalData.Phase1Abilities);

            // Run combat
            terminal.Clear();
            UIHelper.WriteBoxHeader(terminal, "NOCTURA, THE SHADOW ASCENDANT", "dark_magenta", 63);
            terminal.WriteLine("");

            var combatEngine = new CombatEngine(terminal);
            // v0.57.2: Noctura betrayal is an Old God fight — set BossContext so the
            // 75% teammate damage cap, spell immunity handling, and divine armor
            // protections all apply (same as the main Old God encounters).
            // v0.57.17: BossData = betrayalData was missing — without it, the round-2
            // phase transition (when player burst-damages the boss below 50% HP) hit
            // `ctx.BossData.Phase2Dialogue` → NPE → propagated past the inner
            // try/catch and hit the outermost LoadSaveByFileName handler with "Error
            // loading save: Object reference not set...". The regular boss path at
            // ~line 825 sets BossData; the betrayal path was missing it. Same data
            // reused, same scaling/dialogue/phase logic now applies.
            combatEngine.BossContext = new BossCombatContext
            {
                BossData = betrayalData,
                GodType = OldGodType.Noctura,
                AttacksPerRound = 1,
                CanSave = false,
            };
            var result = await combatEngine.PlayerVsMonsters(player, new List<Monster> { noctura }, teammates);
            combatEngine.BossContext = null;

            if (result.Outcome == CombatOutcome.Victory)
            {
                // Show defeat dialogue
                terminal.Clear();
                terminal.WriteLine("");
                UIHelper.WriteBoxHeader(terminal, "THE SHADOW FALLS", "bright_magenta", 63);
                terminal.WriteLine("");

                foreach (var line in betrayalData.DefeatDialogue)
                {
                    if (string.IsNullOrEmpty(line)) { terminal.WriteLine(""); continue; }
                    terminal.SetColor(line.StartsWith("NOCTURA:") ? "dark_magenta" : "white");
                    terminal.WriteLine($"  {line}");
                    await Task.Delay(1500);
                }

                // Rewards
                long xpReward = 300000;
                int goldReward = 100000;
                player.Experience += xpReward;
                player.Gold += goldReward;
                player.Fame += 100;

                terminal.WriteLine("");
                terminal.SetColor("bright_cyan");
                terminal.WriteLine($"  Experience gained: {xpReward:N0}");
                terminal.SetColor("bright_yellow");
                terminal.WriteLine($"  Gold found: {goldReward:N0}");

                await terminal.GetInputAsync($"\n  {Loc.Get("ui.press_enter")}");
                return true;
            }
            else
            {
                // Player lost — Noctura escapes with power
                terminal.Clear();
                terminal.WriteLine("");

                foreach (var line in betrayalData.LossDialogue)
                {
                    if (string.IsNullOrEmpty(line)) { terminal.WriteLine(""); continue; }
                    terminal.SetColor(line.StartsWith("NOCTURA:") ? "dark_magenta" : "gray");
                    terminal.WriteLine($"  {line}");
                    await Task.Delay(1500);
                }

                player.HP = Math.Max(1, player.HP);

                await terminal.GetInputAsync($"\n  {Loc.Get("ui.press_enter")}");
                return false;
            }
        }

        /// <summary>
        /// Handle player being defeated
        /// </summary>
        private async Task<BossEncounterResult> HandlePlayerDefeated(
            Character player, OldGodBossData boss, TerminalEmulator terminal)
        {
            terminal.Clear();
            terminal.WriteLine("");
            UIHelper.WriteBoxHeader(terminal, Loc.Get("old_god.header_defeat"), "dark_red", 63);
            terminal.WriteLine("");

            await Task.Delay(1500);

            terminal.WriteLine(Loc.Get("old_god.defeat_hit_ground", boss.Name), "red");
            terminal.WriteLine("");

            terminal.WriteLine(Loc.Get("old_god.defeat_not_good_enough"), boss.ThemeColor);
            terminal.WriteLine("");

            await Task.Delay(2000);

            // Player doesn't die permanently in boss fights - they're sent back
            terminal.WriteLine($"  {Loc.Get("old_god.defeat_goes_dark")}", "gray");
            terminal.WriteLine($"  {Loc.Get("old_god.defeat_pulls_back")}", "bright_magenta");
            terminal.WriteLine("");
            terminal.WriteLine($"  {Loc.Get("old_god.defeat_wake_up")}", "white");

            player.HP = player.MaxHP / 4;
            player.Experience = Math.Max(0, player.Experience - (boss.Level * 100));

            await terminal.GetInputAsync($"  {Loc.Get("ui.press_enter")}");

            return new BossEncounterResult
            {
                Success = false,
                Outcome = BossOutcome.PlayerDefeated,
                God = boss.Type
            };
        }

        #region Helper Methods

        /// <summary>
        /// Configure boss-specific party balance mechanics per Old God.
        /// Earlier bosses (Maelketh, Veloura) have lighter mechanics to teach the player.
        /// Later bosses (Noctura+) have full mechanics requiring balanced parties.
        /// </summary>
        private void ConfigureBossPartyMechanics(BossCombatContext ctx, OldGodType godType)
        {
            // All bosses get potion cooldown (teaches reliance on healers)
            ctx.PotionCooldownRounds = GameConfig.ModBossPotionCooldownRounds;

            switch (godType)
            {
                case OldGodType.Maelketh: // Floor 25 — Tutorial boss: enrage only
                    ctx.EnrageRound = 40;
                    break;

                case OldGodType.Veloura: // Floor 40 — Introduces AoE (party damage spread)
                    ctx.EnrageRound = 35;
                    ctx.AoEFrequency = 4;
                    ctx.AoEDamage = 300;
                    ctx.AoEAbilityName = "Heartbreak Shatter";
                    break;

                case OldGodType.Thorgrim: // Floor 55 — Introduces channeling (needs interrupter)
                    ctx.EnrageRound = 35;
                    ctx.AoEFrequency = 4;
                    ctx.AoEDamage = 500;
                    ctx.AoEAbilityName = "Gavel of Judgment";
                    ctx.ChannelFrequency = 5;
                    ctx.ChannelAbilityName = "Final Verdict";
                    ctx.ChannelDamage = 1200;
                    break;

                case OldGodType.Noctura: // Floor 70 — Introduces corruption (needs healer cleanse)
                    ctx.EnrageRound = 30;
                    ctx.AoEFrequency = 3;
                    ctx.AoEDamage = 700;
                    ctx.AoEAbilityName = "Shadow Tempest";
                    ctx.ChannelFrequency = 4;
                    ctx.ChannelAbilityName = "Manifest Oblivion";
                    ctx.ChannelDamage = 1800;
                    ctx.CorruptionDamagePerStack = 35;
                    ctx.HasPhysicalImmunityPhase = true; // Phase 2: physical immunity
                    break;

                case OldGodType.Aurelion: // Floor 85 — Introduces doom (needs healer dispel)
                    ctx.EnrageRound = 28;
                    ctx.AoEFrequency = 3;
                    ctx.AoEDamage = 900;
                    ctx.AoEAbilityName = "Solar Cataclysm";
                    ctx.ChannelFrequency = 4;
                    ctx.ChannelAbilityName = "Purifying Annihilation";
                    ctx.ChannelDamage = 2500;
                    ctx.CorruptionDamagePerStack = 45;
                    ctx.DoomRounds = 3;
                    ctx.HasMagicalImmunityPhase = true; // Phase 2: magical immunity
                    break;

                case OldGodType.Terravok: // Floor 95 — Full mechanics, tighter timers
                    ctx.EnrageRound = 25;
                    ctx.AoEFrequency = 3;
                    ctx.AoEDamage = 1200;
                    ctx.AoEAbilityName = "World Breaker";
                    ctx.ChannelFrequency = 4;
                    ctx.ChannelAbilityName = "Tectonic Annihilation";
                    ctx.ChannelDamage = 3000;
                    ctx.CorruptionDamagePerStack = 55;
                    ctx.DoomRounds = 3;
                    ctx.HasPhysicalImmunityPhase = true; // Phase 2: physical immunity
                    break;

                case OldGodType.Manwe: // Floor 100 — Everything at maximum, tightest timers
                    ctx.EnrageRound = 25;
                    ctx.AoEFrequency = 2;
                    ctx.AoEDamage = 1500;
                    ctx.AoEAbilityName = "Creation's End";
                    ctx.ChannelFrequency = 3;
                    ctx.ChannelAbilityName = "Unmake Reality";
                    ctx.ChannelDamage = 4000;
                    ctx.CorruptionDamagePerStack = 70;
                    ctx.DoomRounds = 2; // Only 2 rounds! Must dispel fast
                    ctx.HasPhysicalImmunityPhase = true;
                    ctx.HasMagicalImmunityPhase = true; // Both immunities across phases
                    break;
            }
        }

        /// <summary>
        /// Calculate divine armor damage reduction for late-game Old Gods.
        /// Gods with divine armor resist unenchanted weapons.
        /// Artifact weapons fully bypass; enchanted weapons partially bypass.
        /// </summary>
        private double GetDivineArmorReduction(OldGodType godType, Character player)
        {
            double baseReduction = godType switch
            {
                OldGodType.Aurelion => GameConfig.AurelionDivineShield,
                OldGodType.Terravok => GameConfig.TerravokStoneSkin,
                OldGodType.Manwe => GameConfig.ManweCreatorsWard,
                _ => 0
            };
            if (baseReduction <= 0) return 0;

            // Check BOTH main hand and off-hand for divine armor bypass
            var mainHand = player.GetEquipment(EquipmentSlot.MainHand);
            var offHand = player.GetEquipment(EquipmentSlot.OffHand);

            // Check if either weapon is an artifact (full bypass)
            foreach (var weapon in new[] { mainHand, offHand })
            {
                if (weapon?.Name != null &&
                    (weapon.Name.Contains("Artifact") || weapon.Name.Contains("Godforged") ||
                     weapon.Name.Contains("Sunforged") || weapon.Name.Contains("Voidtouched")))
                {
                    return 0; // Full bypass
                }
            }

            // Check if either weapon has any enchantment (partial bypass)
            bool hasEnchantment = false;
            foreach (var weapon in new[] { mainHand, offHand })
            {
                if (weapon == null) continue;
                if (weapon.HasFireEnchant || weapon.HasFrostEnchant || weapon.HasLightningEnchant ||
                    weapon.HasPoisonEnchant || weapon.HasHolyEnchant || weapon.HasShadowEnchant ||
                    weapon.GetEnchantmentCount() > 0)
                {
                    hasEnchantment = true;
                    break;
                }
            }
            if (hasEnchantment)
                return baseReduction * (1.0 - GameConfig.BossDivineArmorEnchantedBypass);

            // No enchantments on either weapon — full divine armor penalty
            return baseReduction;
        }

        private string CenterText(string text, int width)
        {
            if (text.Length >= width) return text;
            int padding = (width - text.Length) / 2;
            return text.PadLeft(padding + text.Length).PadRight(width);
        }

        private string RenderHealthBar(double percent, int width)
        {
            if (GameConfig.ScreenReaderMode)
            {
                return $"{(int)(percent * 100)}%";
            }
            int filled = (int)(percent * width);
            return new string('█', filled) + new string('░', width - filled);
        }

        /// <summary>
        /// Print a single line of Old God dialogue with context-aware formatting.
        /// Lines starting with "GODNAME:" are spoken dialogue — printed as-is in the theme color.
        /// Lines starting with "> [" are player choice prompts — printed in bright_cyan.
        /// Empty lines pass through as blank lines.
        /// All other lines are narration/action — printed in italic *...* style in a softer color.
        /// </summary>
        public void PrintDialogueLine(TerminalEmulator term, string line, string themeColor)
        {
            if (string.IsNullOrEmpty(line))
            {
                term.WriteLine("");
                return;
            }

            // Spoken dialogue: "GODNAME: "..." " or "BOTH: ..." etc.
            bool isSpeech = System.Text.RegularExpressions.Regex.IsMatch(line, @"^[A-Z][A-Z ]+:");
            if (isSpeech)
            {
                term.SetColor(themeColor);
                term.WriteLine($"  {line}");
                return;
            }

            // Player choice prompt lines
            if (line.StartsWith("> ["))
            {
                term.SetColor("bright_cyan");
                term.WriteLine($"  {line}");
                return;
            }

            // Narration / action — wrap in *...* and use a slightly softer color
            term.SetColor("gray");
            term.WriteLine($"  *{line}*");
        }

        #endregion
    }

    #region Boss System Data Classes

    public enum BossOutcome
    {
        Defeated,
        Saved,
        Allied,
        Spared,
        PlayerDefeated,
        Fled
    }

    public class BossEncounterResult
    {
        public bool Success { get; set; }
        public BossOutcome Outcome { get; set; }
        public OldGodType God { get; set; }
        public long XPGained { get; set; }
        public int GoldGained { get; set; }
        public string ApproachType { get; set; } = "neutral";
    }

    #endregion
}
