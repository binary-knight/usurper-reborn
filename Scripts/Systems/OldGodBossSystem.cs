using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Godot;
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
            terminal.WriteLine($"\"You came back. You actually came back.\"");
            await Task.Delay(1500);
            terminal.WriteLine($"\"And you brought it. I can feel it from here.\"");
            await Task.Delay(1500);
            terminal.WriteLine("");

            terminal.SetColor("bright_white");
            terminal.WriteLine("The artifact does its work. You dont really understand how.");
            await Task.Delay(2000);
            terminal.SetColor("bright_magenta");
            terminal.WriteLine("The corruption peels away like dead bark off a tree.");
            await Task.Delay(1500);
            terminal.WriteLine("Underneath, something old and clean starts to show through.");
            await Task.Delay(1500);
            terminal.WriteLine("");

            terminal.SetColor("bright_cyan");
            terminal.WriteLine($"{godName} staggers, blinking, like someone waking from a bad dream.");
            await Task.Delay(1500);
            terminal.SetColor("bright_yellow");
            terminal.WriteLine($"\"I remember... gods, I remember everything. What I did... what I was...\"");
            await Task.Delay(2000);
            terminal.WriteLine("");

            terminal.SetColor("bright_white");
            terminal.WriteLine("Press Enter to continue...");
            await terminal.WaitForKey("");

            // Update god state to fully Saved
            story.UpdateGodState(type, GodStatus.Saved);
            story.SetStoryFlag($"{type.ToString().ToLower()}_saved", true);

            // Award experience and Chivalry
            long xpReward = boss.Level * 1000;
            player.Experience += xpReward;
            player.Chivalry += 100;

            terminal.SetColor("bright_green");
            terminal.WriteLine($"+{xpReward} XP for fulfilling your promise.");
            terminal.WriteLine("+100 Chivalry - You kept your word to a god.");
            terminal.WriteLine("");

            // Grant Ocean Philosophy fragment
            OceanPhilosophySystem.Instance.CollectFragment(WaveFragment.TheCorruption);

            return new BossEncounterResult
            {
                Success = true,
                Outcome = BossOutcome.Saved,
                God = type
            };
        }

        /// <summary>
        /// Check if prerequisites are met for encountering a god
        /// </summary>
        private bool CheckPrerequisites(OldGodType type)
        {
            var story = StoryProgressionSystem.Instance;

            switch (type)
            {
                case OldGodType.Maelketh:
                    // First god, no prerequisites
                    return true;

                case OldGodType.Veloura:
                    // Must have resolved Maelketh (defeated, saved, or allied)
                    if (story.OldGodStates.TryGetValue(OldGodType.Maelketh, out var maelkethState))
                    {
                        return maelkethState.Status == GodStatus.Defeated ||
                               maelkethState.Status == GodStatus.Saved ||
                               maelkethState.Status == GodStatus.Allied ||
                               maelkethState.Status == GodStatus.Awakened ||
                               maelkethState.Status == GodStatus.Consumed;
                    }
                    return false;

                case OldGodType.Thorgrim:
                    // Must have defeated at least one god
                    return story.OldGodStates.Values.Any(s => s.Status == GodStatus.Defeated);

                case OldGodType.Noctura:
                    // Must have defeated at least two gods
                    return story.OldGodStates.Values.Count(s => s.Status == GodStatus.Defeated) >= 2;

                case OldGodType.Aurelion:
                    // Must have defeated at least three gods
                    return story.OldGodStates.Values.Count(s => s.Status == GodStatus.Defeated) >= 3;

                case OldGodType.Terravok:
                    // Must have defeated at least four gods
                    return story.OldGodStates.Values.Count(s => s.Status == GodStatus.Defeated) >= 4;

                case OldGodType.Manwe:
                    // Must have all artifacts and faced all other gods
                    return story.CollectedArtifacts.Count >= 6 &&
                           story.HasStoryFlag("void_key_obtained");

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

            currentBoss = boss;
            bossDefeated = false;
            dungeonTeammates = teammates;

            // GD.Print($"[BossSystem] Starting encounter with {boss.Name}");

            // Play introduction
            await PlayBossIntroduction(boss, player, terminal);

            // Run dialogue
            var dialogueResult = await DialogueSystem.Instance.StartDialogue(
                player, $"{type.ToString().ToLower()}_encounter", terminal);

            // Check if dialogue led to non-combat resolution
            var story = StoryProgressionSystem.Instance;
            if (story.HasStoryFlag($"{type.ToString().ToLower()}_ally"))
            {
                // Allied with the god
                return new BossEncounterResult
                {
                    Success = true,
                    Outcome = BossOutcome.Allied,
                    God = type
                };
            }

            if (story.HasStoryFlag($"{type.ToString().ToLower()}_spared"))
            {
                // Spared the god - mark as Awakened (quest in progress, not fully saved yet)
                // Player must find the required artifact and return to complete the save
                story.UpdateGodState(type, GodStatus.Awakened);
                story.SetStoryFlag($"{type.ToString().ToLower()}_save_quest", true);

                return new BossEncounterResult
                {
                    Success = true,
                    Outcome = BossOutcome.Spared,
                    God = type
                };
            }

            // Combat time! Either the player chose a combat path, or said nothing
            // (an Old God won't just let you walk away in silence)
            if (!story.HasStoryFlag($"{type.ToString().ToLower()}_combat_start"))
            {
                // Player said nothing — the god forces combat
                terminal.WriteLine("");
                terminal.WriteLine($"You say nothing.", "gray");
                terminal.WriteLine("");
                terminal.WriteLine($"{boss.Name} stares at you.", boss.ThemeColor);
                terminal.WriteLine("");
                terminal.WriteLine($"\"{(type == OldGodType.Maelketh ? "You DARE stand there and say NOTHING to me?!" : "Fine. Have it your way.")}\"", boss.ThemeColor);
                terminal.WriteLine("");
                terminal.WriteLine("The god attacks!", "bright_red");
                await Task.Delay(2000);
            }

            var combatResult = await RunBossCombat(player, boss, terminal);
            return combatResult;
        }

        /// <summary>
        /// Play boss introduction sequence
        /// </summary>
        private async Task PlayBossIntroduction(OldGodBossData boss, Character player, TerminalEmulator terminal)
        {
            terminal.Clear();
            terminal.WriteLine("");

            // Build dramatic entrance
            terminal.WriteLine("The ground trembles.", "red");
            await Task.Delay(800);

            terminal.WriteLine("Ancient power stirs.", "bright_red");
            await Task.Delay(800);

            terminal.WriteLine($"The seal around {boss.Name} shatters.", "bright_magenta");
            await Task.Delay(1200);

            terminal.WriteLine("");
            terminal.WriteLine($"╔════════════════════════════════════════════════════════════════╗", boss.ThemeColor);
            terminal.WriteLine($"║                                                                ║", boss.ThemeColor);
            terminal.WriteLine($"║     {CenterText(boss.Name.ToUpper(), 58)}     ║", boss.ThemeColor);
            terminal.WriteLine($"║     {CenterText(boss.Title, 58)}     ║", boss.ThemeColor);
            terminal.WriteLine($"║                                                                ║", boss.ThemeColor);
            terminal.WriteLine($"╚════════════════════════════════════════════════════════════════╝", boss.ThemeColor);
            terminal.WriteLine("");

            // Show Old God art (skip for screen readers and BBS mode)
            if (player is Player pp && !pp.ScreenReaderMode && !DoorMode.IsInDoorMode)
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
            terminal.WriteLine($"  Level: {boss.Level}", "gray");
            terminal.WriteLine($"  HP: {boss.HP:N0}", "red");

            // Warning for unenchanted weapons against gods with divine armor
            double divineArmor = GetDivineArmorReduction(boss.Type, player);
            if (divineArmor > 0)
            {
                terminal.WriteLine("");
                terminal.SetColor("bright_red");
                string armorName = boss.Type switch
                {
                    OldGodType.Aurelion => "Divine Shield",
                    OldGodType.Terravok => "Stone Skin",
                    OldGodType.Manwe => "Creator's Ward",
                    _ => "Divine Armor"
                };
                terminal.WriteLine($"  WARNING: {boss.Name} is protected by {armorName}!");
                terminal.SetColor("red");
                terminal.WriteLine($"  Your unenchanted weapon will deal {divineArmor * 100:N0}% LESS damage.");
                terminal.SetColor("yellow");
                terminal.WriteLine("  Enchant your weapon at the Magic Shop to bypass this protection.");
            }

            terminal.WriteLine("");

            await terminal.GetInputAsync("  Press Enter to face the Old God...");
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
                terminal.WriteLine("  Your fury burns bright! (+25% damage, +15% crit, -15% defense)", "bright_red");
            }
            // Option 3: "Teach me" - Humble approach = Learn his patterns
            else if (story.HasStoryFlag("maelketh_teaching"))
            {
                activeCombatModifiers.ApproachType = "humble";
                activeCombatModifiers.HasInsight = true;
                activeCombatModifiers.DefenseMultiplier = 1.20; // 20% more defense
                activeCombatModifiers.BossDamageMultiplier = 0.85; // Boss does 15% less damage
                terminal.WriteLine("  Maelketh's teachings echo in your mind. (+20% defense, boss -15% damage)", "cyan");
            }
            // Option 2: Peace path (fails but shows character) - No modifier, normal fight
        }

        private void ApplyVelouraModifiers(StoryProgressionSystem story, TerminalEmulator terminal)
        {
            // Aggressive approach
            if (story.HasStoryFlag("veloura_combat_start") && !story.HasStoryFlag("veloura_empathy") && !story.HasStoryFlag("veloura_mercy_kill"))
            {
                activeCombatModifiers.ApproachType = "aggressive";
                activeCombatModifiers.DamageMultiplier = 1.15;
                activeCombatModifiers.BossDamageMultiplier = 1.10; // She fights harder
                terminal.WriteLine("  Your hostility fuels her passion. (+15% damage, but she hits 10% harder)", "red");
            }
            // Empathy shown
            else if (story.HasStoryFlag("veloura_empathy"))
            {
                activeCombatModifiers.ApproachType = "diplomatic";
                activeCombatModifiers.BossWeakened = true;
                activeCombatModifiers.BossDamageMultiplier = 0.80; // She's conflicted
                terminal.WriteLine("  Your empathy weakens her resolve. (Boss -20% damage)", "bright_magenta");
            }
            // Mercy kill - she accepts death
            else if (story.HasStoryFlag("veloura_mercy_kill"))
            {
                activeCombatModifiers.ApproachType = "merciful";
                activeCombatModifiers.BossWeakened = true;
                activeCombatModifiers.BossDefenseMultiplier = 0.70; // She doesn't fully resist
                terminal.WriteLine("  She accepts her fate with grace. (Boss -30% defense)", "bright_cyan");
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
                terminal.WriteLine("  Your defiance enrages the lawbringer! (+10% damage, but he hits 15% harder)", "yellow");
            }
            // Honorable combat via Right of Challenge
            else if (story.HasStoryFlag("thorgrim_honorable_combat"))
            {
                activeCombatModifiers.ApproachType = "honorable";
                activeCombatModifiers.DefenseMultiplier = 1.15;
                activeCombatModifiers.BonusDefense = 20;
                terminal.WriteLine("  The Right of Challenge grants you legal protection. (+15% defense, +20 armor)", "gray");
            }
            // Broken his logic with paradox
            else if (story.HasStoryFlag("thorgrim_broken_logic"))
            {
                activeCombatModifiers.ApproachType = "cunning";
                activeCombatModifiers.BossConfused = true;
                activeCombatModifiers.BossDamageMultiplier = 0.75;
                activeCombatModifiers.BossDefenseMultiplier = 0.85;
                terminal.WriteLine("  His broken logic makes him erratic! (Boss -25% damage, -15% defense)", "bright_cyan");
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
                terminal.WriteLine("  Noctura holds back. This is a lesson, not a death sentence.", "dark_magenta");
                terminal.WriteLine("  (-50% boss damage, -30% boss defense, +15% your damage)", "cyan");
                return;
            }

            // Enraged: negative receptivity, player rejected every teaching
            if (story.HasStoryFlag("noctura_enraged") || receptivity < 0)
            {
                activeCombatModifiers.ApproachType = "enraged";
                activeCombatModifiers.BossDamageMultiplier = 1.25;
                activeCombatModifiers.CriticalChance = 0.03; // Harder to land crits
                terminal.WriteLine("  Noctura is ENRAGED. The shadows boil with fury.", "dark_magenta");
                terminal.WriteLine("  (+25% boss damage, harder to crit)", "red");
                return;
            }

            // Reluctant fight: receptivity 25+ but took the fight path anyway
            if (receptivity >= 25)
            {
                activeCombatModifiers.ApproachType = "reluctant";
                activeCombatModifiers.BossDamageMultiplier = 0.70;
                activeCombatModifiers.BossDefenseMultiplier = 0.85;
                activeCombatModifiers.DamageMultiplier = 1.10;
                terminal.WriteLine("  Noctura fights reluctantly. She hoped you'd understand.", "dark_magenta");
                terminal.WriteLine("  (-30% boss damage, -15% boss defense, +10% your damage)", "cyan");
                return;
            }

            // Standard fight: low receptivity (0-24), never engaged with teachings
            activeCombatModifiers.ApproachType = "aggressive";
            activeCombatModifiers.DamageMultiplier = 1.10;
            activeCombatModifiers.CriticalChance = 0.03; // Harder to land crits
            terminal.WriteLine("  The shadows hide her movements. (+10% damage, but harder to crit)", "dark_magenta");
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
                terminal.WriteLine("  Your defiance against the light empowers your strikes! (+20% damage, -10% defense)", "bright_yellow");
            }
            // Humble approach
            else if (story.HasStoryFlag($"{godName}_humble"))
            {
                activeCombatModifiers.ApproachType = "humble";
                activeCombatModifiers.BossDamageMultiplier = 0.85;
                activeCombatModifiers.DefenseMultiplier = 1.15;
                terminal.WriteLine("  Your humility earns a measure of restraint. (-15% boss damage, +15% defense)", "bright_cyan");
            }
            // Default combat
            else if (story.HasStoryFlag($"{godName}_combat_start"))
            {
                activeCombatModifiers.ApproachType = "neutral";
                terminal.WriteLine("  The God of Light prepares to judge you.", "bright_yellow");
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
                terminal.WriteLine("  Your destructive intent awakens his full wrath! (+30% damage, but +20% boss damage)", "dark_green");
            }
            // Respect for nature
            else if (story.HasStoryFlag($"{godName}_respectful"))
            {
                activeCombatModifiers.ApproachType = "respectful";
                activeCombatModifiers.BossWeakened = true;
                activeCombatModifiers.BossDamageMultiplier = 0.90;
                terminal.WriteLine("  Your respect for nature tempers his rage. (-10% boss damage)", "green");
            }
            // Default
            else if (story.HasStoryFlag($"{godName}_combat_start"))
            {
                activeCombatModifiers.ApproachType = "neutral";
                terminal.WriteLine("  The God of Earth rises to crush you.", "dark_green");
            }
        }

        private void ApplyManweModifiers(StoryProgressionSystem story, TerminalEmulator terminal)
        {
            // Final boss - modifiers based on choices with previous gods
            int godsSaved = story.OldGodStates.Values.Count(s => s.Status == GodStatus.Saved);
            int godsAllied = story.OldGodStates.Values.Count(s => s.Status == GodStatus.Allied);
            int godsDestroyed = story.OldGodStates.Values.Count(s => s.Status == GodStatus.Defeated);

            if (godsAllied >= 2)
            {
                activeCombatModifiers.ApproachType = "allied";
                activeCombatModifiers.DamageMultiplier = 1.20;
                activeCombatModifiers.DefenseMultiplier = 1.20;
                terminal.WriteLine("  Your allied gods lend you their power! (+20% damage and defense)", "bright_magenta");
            }
            else if (godsSaved >= 3)
            {
                activeCombatModifiers.ApproachType = "savior";
                activeCombatModifiers.BossWeakened = true;
                activeCombatModifiers.BossDamageMultiplier = 0.85;
                terminal.WriteLine("  The saved gods weaken the Creator's hold. (-15% boss damage)", "bright_cyan");
            }
            else if (godsDestroyed >= 5)
            {
                activeCombatModifiers.ApproachType = "destroyer";
                activeCombatModifiers.DamageMultiplier = 1.35;
                activeCombatModifiers.DefenseMultiplier = 0.85;
                activeCombatModifiers.CriticalChance = 0.20;
                terminal.WriteLine("  Consumed divine power surges through you! (+35% damage, +20% crit, -15% defense)", "dark_red");
            }

            // Defiant to stranger bonus
            if (story.HasStoryFlag("defiant_to_stranger"))
            {
                activeCombatModifiers.BonusDamage += 50;
                terminal.WriteLine("  Your defiant spirit burns bright! (+50 bonus damage)", "bright_red");
            }

            // Willing hero bonus
            if (story.HasStoryFlag("willing_hero"))
            {
                activeCombatModifiers.BonusDefense += 30;
                terminal.WriteLine("  Your willing heart shields you! (+30 bonus defense)", "bright_green");
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
                CanSave = boss.CanBeSaved && ArtifactSystem.Instance.HasArtifact(ArtifactType.SoulweaversLoom),
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

            // Set static flag for Void Key artifact doubling
            CombatEngine.IsManweBattle = boss.Type == OldGodType.Manwe;

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
            // Split boss Strength into Monster Strength + WeapPow for CombatEngine's damage formula
            long monsterStrength = boss.Strength / 2;
            long monsterWeapPow = boss.Strength / 2;

            // Split boss Defence into Monster Defence + ArmPow
            int monsterDefence = (int)(boss.Defence / 2);
            long monsterArmPow = boss.Defence / 2;

            var monster = new Monster
            {
                Name = boss.Name,
                Level = boss.Level,
                HP = boss.HP,
                MaxHP = boss.HP,
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
            // Only clear the bonuses we added — TempAttackBonus/TempDefenseBonus
            // are reset at the start of each PlayerVsMonsters call anyway,
            // but clear HasBloodlust and DodgeNextAttack which persist
            player.HasBloodlust = false;
            player.DodgeNextAttack = false;
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
            terminal.WriteLine($"╔═══════════════════════════════════════════════════════════════╗", "bright_green");
            terminal.WriteLine($"║                  {boss.Name.ToUpper()} SAVED                          ║", "bright_green");
            terminal.WriteLine($"╚═══════════════════════════════════════════════════════════════╝", "bright_green");
            terminal.WriteLine("");

            await Task.Delay(1500);

            terminal.WriteLine($"  The darkness lifts from {boss.Name}.", "white");
            terminal.WriteLine("  They look around like they're seeing the world for the first time.", "white");
            terminal.WriteLine("");

            foreach (var line in boss.SaveDialogue)
            {
                terminal.WriteLine($"  \"{line}\"", "bright_cyan");
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
            terminal.WriteLine($"  (+{xpReward:N0} Experience for showing mercy)", "cyan");

            // Award chivalry
            player.Chivalry += 100;
            terminal.WriteLine($"  (+100 Chivalry)", "bright_green");

            // Award thematic crafting materials (same as defeat)
            var thematicMaterial = GameConfig.CraftingMaterials.FirstOrDefault(
                m => m.ThematicGod == boss.Type.ToString());
            if (thematicMaterial != null)
            {
                player.AddMaterial(thematicMaterial.Id, 2);
                terminal.WriteLine("");
                terminal.SetColor(thematicMaterial.Color);
                terminal.WriteLine($"  {boss.Name} leaves behind {thematicMaterial.Name} x2.");
                terminal.SetColor("gray");
                terminal.WriteLine($"  \"{thematicMaterial.Description}\"");
            }
            if (boss.DungeonFloor >= 50)
            {
                player.AddMaterial("heart_of_the_ocean", 1);
                terminal.SetColor("cyan");
                terminal.WriteLine("  A strange pearl drops to the ground -- a Heart of the Ocean!");
            }

            OnBossSaved?.Invoke(boss.Type);

            // Queue Stranger encounter after first Old God
            QueueStrangerOldGodEncounter(StrangerContextEvent.OldGodSaved);

            await terminal.GetInputAsync("  Press Enter to continue...");

            return new BossEncounterResult
            {
                Success = true,
                Outcome = BossOutcome.Saved,
                God = boss.Type,
                XPGained = xpReward
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
            terminal.WriteLine($"╔═══════════════════════════════════════════════════════════════╗", "bright_yellow");
            terminal.WriteLine($"║                {boss.Name.ToUpper()} DEFEATED                         ║", "bright_yellow");
            terminal.WriteLine($"╚═══════════════════════════════════════════════════════════════╝", "bright_yellow");
            terminal.WriteLine("");

            await Task.Delay(1500);

            foreach (var line in boss.DefeatDialogue)
            {
                terminal.WriteLine($"  \"{line}\"", boss.ThemeColor);
                await Task.Delay(1500); // Give players time to read each line
            }

            terminal.WriteLine("");
            await terminal.GetInputAsync("  Press Enter to continue...");
            terminal.WriteLine("");
            terminal.WriteLine($"  What's left of {boss.Name} breaks apart and fades.", "white");
            terminal.WriteLine("");

            // Update story state
            var story = StoryProgressionSystem.Instance;
            story.UpdateGodState(boss.Type, GodStatus.Defeated);
            story.SetStoryFlag($"{boss.Type.ToString().ToLower()}_destroyed", true);

            // Award artifact
            await ArtifactSystem.Instance.CollectArtifact(player, boss.ArtifactDropped, terminal);

            // Award thematic crafting materials
            var thematicMaterial = GameConfig.CraftingMaterials.FirstOrDefault(
                m => m.ThematicGod == boss.Type.ToString());
            if (thematicMaterial != null)
            {
                player.AddMaterial(thematicMaterial.Id, 2);
                terminal.WriteLine("");
                terminal.SetColor(thematicMaterial.Color);
                terminal.WriteLine($"  Something crystallizes out of the remains... {thematicMaterial.Name} x2!");
                terminal.SetColor("gray");
                terminal.WriteLine($"  \"{thematicMaterial.Description}\"");
            }
            if (boss.DungeonFloor >= 50)
            {
                player.AddMaterial("heart_of_the_ocean", 1);
                terminal.SetColor("cyan");
                terminal.WriteLine("  A strange pearl drops to the ground -- a Heart of the Ocean!");
            }
            terminal.WriteLine("");

            // Award experience
            long xpReward = boss.Level * 2000;
            player.Experience += xpReward;
            terminal.WriteLine($"  (+{xpReward:N0} Experience)", "cyan");

            // Award gold
            int goldReward = boss.Level * 500;
            player.Gold += goldReward;
            terminal.WriteLine($"  (+{goldReward:N0} Gold)", "yellow");

            OnBossDefeated?.Invoke(boss.Type);

            // Queue Stranger encounter after first Old God
            QueueStrangerOldGodEncounter(StrangerContextEvent.OldGodDefeated);

            await terminal.GetInputAsync("  Press Enter to continue...");

            return new BossEncounterResult
            {
                Success = true,
                Outcome = BossOutcome.Defeated,
                God = boss.Type,
                XPGained = xpReward,
                GoldGained = goldReward
            };
        }

        /// <summary>
        /// Handle player being defeated
        /// </summary>
        private async Task<BossEncounterResult> HandlePlayerDefeated(
            Character player, OldGodBossData boss, TerminalEmulator terminal)
        {
            terminal.Clear();
            terminal.WriteLine("");
            terminal.WriteLine("╔═══════════════════════════════════════════════════════════════╗", "dark_red");
            terminal.WriteLine("║                       D E F E A T                             ║", "dark_red");
            terminal.WriteLine("╚═══════════════════════════════════════════════════════════════╝", "dark_red");
            terminal.WriteLine("");

            await Task.Delay(1500);

            terminal.WriteLine($"  You hit the ground hard. {boss.Name} looms over you.", "red");
            terminal.WriteLine("");

            terminal.WriteLine($"  \"Not good enough, mortal. Not even close.\"", boss.ThemeColor);
            terminal.WriteLine("");

            await Task.Delay(2000);

            // Player doesn't die permanently in boss fights - they're sent back
            terminal.WriteLine("  Everything goes dark. But before you're gone completely,", "gray");
            terminal.WriteLine("  something pulls you back. Not yet.", "bright_magenta");
            terminal.WriteLine("");
            terminal.WriteLine("  You wake up at the dungeon entrance. Everything hurts.", "white");

            player.HP = player.MaxHP / 4;
            player.Experience = Math.Max(0, player.Experience - (boss.Level * 100));

            await terminal.GetInputAsync("  Press Enter to continue...");

            return new BossEncounterResult
            {
                Success = false,
                Outcome = BossOutcome.PlayerDefeated,
                God = boss.Type
            };
        }

        #region Helper Methods

        /// <summary>
        /// Calculate divine armor damage reduction for late-game Old Gods.
        /// Gods with divine armor resist unenchanted weapons.
        /// Having ANY enchantment on the main-hand weapon removes the penalty.
        /// </summary>
        private double GetDivineArmorReduction(OldGodType godType, Character player)
        {
            // Check if weapon has any enchantments
            var weapon = player.GetEquipment(EquipmentSlot.MainHand);
            if (weapon != null && weapon.GetEnchantmentCount() > 0)
                return 0; // Enchanted weapon — no penalty

            return godType switch
            {
                OldGodType.Aurelion => GameConfig.AurelionDivineShield,
                OldGodType.Terravok => GameConfig.TerravokStoneSkin,
                OldGodType.Manwe => GameConfig.ManweCreatorsWard,
                _ => 0
            };
        }

        private string CenterText(string text, int width)
        {
            if (text.Length >= width) return text;
            int padding = (width - text.Length) / 2;
            return text.PadLeft(padding + text.Length).PadRight(width);
        }

        private string RenderHealthBar(double percent, int width)
        {
            int filled = (int)(percent * width);
            return new string('█', filled) + new string('░', width - filled);
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
    }

    #endregion
}
