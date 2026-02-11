using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Godot;
using UsurperRemake.Utils;
using UsurperRemake.Data;

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
        private int currentPhase = 1;
        private long bossCurrentHP;
        private bool bossDefeated;
        private bool bossSaved;

        // Team combat support - includes both companions and NPC teammates from dungeon
        private List<BossFightTeammate> activeTeammates = new();
        private List<Character>? dungeonTeammates; // Passed from DungeonLocation

        // Minion support for boss phases (e.g., Maelketh's spectral soldiers)
        private List<BossMinion> activeMinions = new();
        private int roundsSinceLastSummon = 0;

        // Combat modifiers based on dialogue choices
        private CombatModifiers activeCombatModifiers = new();

        /// <summary>
        /// Represents any teammate fighting in the boss battle (companion or NPC)
        /// </summary>
        private class BossFightTeammate
        {
            public string Name { get; set; } = "";
            public long CurrentHP { get; set; }
            public long MaxHP { get; set; }
            public long Attack { get; set; }
            public long Defense { get; set; }
            public string Role { get; set; } = "Fighter"; // Tank, DPS, Healer, Hybrid, Fighter
            public string[] Abilities { get; set; } = Array.Empty<string>();
            public bool IsCompanion { get; set; } // True if from CompanionSystem, false if NPC
            public Companion? CompanionRef { get; set; } // Reference if this is a companion
            public Character? CharacterRef { get; set; } // Reference if this is an NPC
            public bool IsAlive => CurrentHP > 0;
        }

        /// <summary>
        /// Represents a minion summoned by the boss during combat
        /// </summary>
        private class BossMinion
        {
            public string Name { get; set; } = "Minion";
            public long CurrentHP { get; set; }
            public long MaxHP { get; set; }
            public long Attack { get; set; }
            public long Defense { get; set; }
            public bool IsAlive => CurrentHP > 0;
        }

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
        public event Action<OldGodType, int>? OnPhaseChange;

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
            terminal.WriteLine($"{godName} materializes before you once more...");
            await Task.Delay(1500);
            terminal.WriteLine("");
            terminal.SetColor("bright_cyan");
            terminal.WriteLine($"\"You... you actually returned. And you brought it.\"");
            await Task.Delay(1500);
            terminal.WriteLine($"\"The Soulweaver's Loom. I can feel its power reaching out to me.\"");
            await Task.Delay(1500);
            terminal.WriteLine("");

            terminal.SetColor("bright_white");
            terminal.WriteLine("The Loom begins to glow, threads of light weaving through the air...");
            await Task.Delay(2000);
            terminal.SetColor("bright_magenta");
            terminal.WriteLine("The corruption that has twisted this ancient being starts to unravel.");
            await Task.Delay(1500);
            terminal.WriteLine("Layer by layer, the curse falls away like old skin.");
            await Task.Delay(1500);
            terminal.WriteLine("");

            terminal.SetColor("bright_cyan");
            terminal.WriteLine($"\"{godName} gasps as memories of who they truly were flood back.\"");
            await Task.Delay(1500);
            terminal.SetColor("bright_yellow");
            terminal.WriteLine($"\"Thank you... I remember now. I remember what love truly means.\"");
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
            currentPhase = 1;
            bossCurrentHP = boss.HP;
            bossDefeated = false;
            bossSaved = false;
            dungeonTeammates = teammates; // Store for InitializeTeammates
            activeMinions.Clear(); // Clear any minions from previous fights
            roundsSinceLastSummon = 0;

            // GD.Print($"[BossSystem] Starting encounter with {boss.Name}");

            // Play introduction
            await PlayBossIntroduction(boss, terminal);

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

            // Combat time!
            if (story.HasStoryFlag($"{type.ToString().ToLower()}_combat_start"))
            {
                var combatResult = await RunBossCombat(player, boss, terminal);
                return combatResult;
            }

            // Dialogue ended without resolution - shouldn't happen normally
            return new BossEncounterResult { Success = false };
        }

        /// <summary>
        /// Play boss introduction sequence
        /// </summary>
        private async Task PlayBossIntroduction(OldGodBossData boss, TerminalEmulator terminal)
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

            await Task.Delay(2000);

            // Show boss stats
            terminal.WriteLine($"  Level: {boss.Level}", "gray");
            terminal.WriteLine($"  HP: {boss.HP:N0}", "red");
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
        /// Run the boss combat encounter
        /// </summary>
        private async Task<BossEncounterResult> RunBossCombat(
            Character player, OldGodBossData boss, TerminalEmulator terminal)
        {
            var story = StoryProgressionSystem.Instance;
            bool playerDead = false;

            // Apply combat modifiers based on dialogue choices
            ApplyDialogueModifiers(boss.Type, terminal);
            terminal.WriteLine("");
            await Task.Delay(1500);

            // Initialize teammates from companions
            InitializeTeammates();

            while (bossCurrentHP > 0 && !playerDead && !bossSaved)
            {
                terminal.Clear();

                // Display combat status (including teammates)
                DisplayCombatStatus(player, boss, terminal);

                // Check for phase transitions
                await CheckPhaseTransition(boss, terminal);

                // Player turn
                var action = await GetPlayerAction(player, boss, terminal);

                switch (action)
                {
                    case BossAction.Attack:
                        await PlayerAttack(player, boss, terminal);
                        break;

                    case BossAction.Special:
                        await PlayerSpecialAttack(player, boss, terminal);
                        break;

                    case BossAction.Heal:
                        await PlayerHeal(player, terminal);
                        break;

                    case BossAction.Save:
                        if (await AttemptToSaveBoss(player, boss, terminal))
                        {
                            bossSaved = true;
                            continue;
                        }
                        break;

                    case BossAction.Flee:
                        if (new Random().NextDouble() < 0.2) // 20% flee chance
                        {
                            terminal.WriteLine("You manage to escape!", "green");
                            await Task.Delay(1500);
                            return new BossEncounterResult
                            {
                                Success = false,
                                Outcome = BossOutcome.Fled,
                                God = boss.Type
                            };
                        }
                        terminal.WriteLine($"{boss.Name} blocks your escape!", "red");
                        await Task.Delay(1000);
                        break;
                }

                // Teammates attack the boss
                if (bossCurrentHP > 0 && !bossSaved)
                {
                    await TeammatesAttack(boss, terminal);
                }

                // Boss turn (if still alive)
                if (bossCurrentHP > 0 && !bossSaved)
                {
                    playerDead = await BossTurn(player, boss, terminal);
                }
            }

            // Determine outcome
            if (bossSaved)
            {
                return await HandleBossSaved(player, boss, terminal);
            }
            else if (bossCurrentHP <= 0)
            {
                bossDefeated = true;
                return await HandleBossDefeated(player, boss, terminal);
            }
            else
            {
                return await HandlePlayerDefeated(player, boss, terminal);
            }
        }

        /// <summary>
        /// Display combat status
        /// </summary>
        private void DisplayCombatStatus(Character player, OldGodBossData boss, TerminalEmulator terminal)
        {
            terminal.WriteLine("");
            terminal.WriteLine($"══════════════════════════════════════════════════════════════", boss.ThemeColor);
            terminal.WriteLine($"  {boss.Name} - Phase {currentPhase}", boss.ThemeColor);
            terminal.WriteLine($"══════════════════════════════════════════════════════════════", boss.ThemeColor);
            terminal.WriteLine("");

            // Boss HP bar
            var hpPercent = (double)bossCurrentHP / boss.HP;
            var hpBar = RenderHealthBar(hpPercent, 40);
            var hpColor = hpPercent > 0.5 ? "green" : hpPercent > 0.2 ? "yellow" : "red";
            terminal.WriteLine($"  Boss HP: [{hpBar}] {bossCurrentHP:N0}/{boss.HP:N0}", hpColor);

            // Minions HP bars (if any active)
            var aliveMinions = activeMinions.Where(m => m.IsAlive).ToList();
            if (aliveMinions.Count > 0)
            {
                terminal.WriteLine("");
                terminal.WriteLine($"  ═══ MINIONS ({aliveMinions.Count}) ═══", "dark_red");
                foreach (var minion in aliveMinions)
                {
                    var mPercent = (double)minion.CurrentHP / minion.MaxHP;
                    var mBar = RenderHealthBar(mPercent, 20);
                    var mColor = mPercent > 0.5 ? "red" : mPercent > 0.2 ? "dark_red" : "gray";
                    terminal.WriteLine($"  {minion.Name}: [{mBar}] {minion.CurrentHP}/{minion.MaxHP}", mColor);
                }
            }

            terminal.WriteLine("");

            // Player HP bar
            terminal.WriteLine($"  ═══ YOUR PARTY ═══", "bright_cyan");
            var playerPercent = (double)player.HP / player.MaxHP;
            var playerBar = RenderHealthBar(playerPercent, 30);
            var playerColor = playerPercent > 0.5 ? "green" : playerPercent > 0.2 ? "yellow" : "red";
            terminal.WriteLine($"  You:     [{playerBar}] {player.HP}/{player.MaxHP}", playerColor);

            // Teammate HP bars
            foreach (var teammate in activeTeammates)
            {
                var tmPercent = (double)teammate.CurrentHP / teammate.MaxHP;
                var tmBar = RenderHealthBar(tmPercent, 30);
                var tmColor = teammate.IsAlive ? (tmPercent > 0.5 ? "cyan" : tmPercent > 0.2 ? "yellow" : "red") : "dark_gray";
                var status = teammate.IsAlive ? $"{teammate.CurrentHP}/{teammate.MaxHP}" : "FALLEN";
                var roleTag = teammate.IsCompanion ? "" : $" ({teammate.Role})";
                terminal.WriteLine($"  {teammate.Name,-8} [{tmBar}] {status}{roleTag}", tmColor);
            }

            terminal.WriteLine("");
            if (player.MaxMana > 0)
            {
                terminal.WriteLine($"  Mana:    {player.Mana}/{player.MaxMana}", "cyan");
            }
            terminal.WriteLine($"  Potions: {player.Healing}", "green");
            terminal.WriteLine("");
        }

        /// <summary>
        /// Check and handle phase transitions
        /// </summary>
        private async Task CheckPhaseTransition(OldGodBossData boss, TerminalEmulator terminal)
        {
            var hpPercent = (double)bossCurrentHP / boss.HP;

            int newPhase = currentPhase;
            if (hpPercent <= 0.2 && currentPhase < 3)
                newPhase = 3;
            else if (hpPercent <= 0.5 && currentPhase < 2)
                newPhase = 2;

            if (newPhase != currentPhase)
            {
                currentPhase = newPhase;
                OnPhaseChange?.Invoke(boss.Type, currentPhase);

                terminal.WriteLine("");
                terminal.WriteLine($"═══ PHASE {currentPhase} ═══", "bright_red");
                terminal.WriteLine("");

                // Play phase dialogue
                var dialogue = currentPhase switch
                {
                    2 => boss.Phase2Dialogue,
                    3 => boss.Phase3Dialogue,
                    _ => Array.Empty<string>()
                };

                foreach (var line in dialogue)
                {
                    terminal.WriteLine($"  \"{line}\"", boss.ThemeColor);
                    await Task.Delay(200);
                }

                // Spawn minions for Maelketh phase 2
                if (boss.Type == OldGodType.Maelketh && currentPhase == 2)
                {
                    SpawnSpectralSoldiers(2, terminal);
                }

                terminal.WriteLine("");
                await Task.Delay(1500);
            }
        }

        /// <summary>
        /// Spawn spectral soldiers for Maelketh
        /// </summary>
        private void SpawnSpectralSoldiers(int count, TerminalEmulator terminal)
        {
            var player = GameEngine.Instance?.CurrentPlayer;
            int playerLevel = player?.Level ?? 25;

            for (int i = 0; i < count; i++)
            {
                var soldier = new BossMinion
                {
                    Name = $"Spectral Soldier",
                    MaxHP = 50 + playerLevel * 5,
                    CurrentHP = 50 + playerLevel * 5,
                    Attack = 10 + playerLevel * 2,
                    Defense = 5 + playerLevel
                };
                activeMinions.Add(soldier);
            }

            terminal.WriteLine("");
            terminal.WriteLine($"  {count} Spectral Soldiers materialize from the shadows!", "bright_red");
            roundsSinceLastSummon = 0;
        }

        /// <summary>
        /// Get player action choice
        /// </summary>
        private async Task<BossAction> GetPlayerAction(
            Character player, OldGodBossData boss, TerminalEmulator terminal)
        {
            terminal.WriteLine("  What do you do?", "cyan");
            terminal.WriteLine("");
            terminal.WriteLine("  [A] Attack", "white");
            terminal.WriteLine("  [S] Special Attack (costs mana)", player.Mana > 0 ? "bright_cyan" : "gray");

            if (boss.CanBeSaved && ArtifactSystem.Instance.HasArtifact(ArtifactType.SoulweaversLoom))
            {
                terminal.WriteLine("  [R] Attempt to Save (Soulweaver's Loom)", "bright_magenta");
            }

            terminal.WriteLine("  [H] Heal (use potion)", player.Healing > 0 ? "green" : "gray");
            terminal.WriteLine("  [F] Attempt to Flee", "yellow");
            terminal.WriteLine("");

            while (true)
            {
                var input = await terminal.GetInputAsync("  Choice: ");

                switch (input.ToUpper())
                {
                    case "A":
                        return BossAction.Attack;
                    case "S":
                        if (player.Mana > 0) return BossAction.Special;
                        terminal.WriteLine("  No mana!", "red");
                        break;
                    case "R":
                        if (boss.CanBeSaved && ArtifactSystem.Instance.HasArtifact(ArtifactType.SoulweaversLoom))
                            return BossAction.Save;
                        terminal.WriteLine("  Cannot save this god.", "red");
                        break;
                    case "H":
                        if (player.Healing > 0) return BossAction.Heal;
                        terminal.WriteLine("  No potions!", "red");
                        break;
                    case "F":
                        return BossAction.Flee;
                }
            }
        }

        /// <summary>
        /// Process player attack
        /// </summary>
        private async Task PlayerAttack(Character player, OldGodBossData boss, TerminalEmulator terminal)
        {
            var random = new Random();
            long baseDamage = player.Strength + player.WeapPow;
            long damage = baseDamage + random.Next((int)(baseDamage / 2));

            // Artifact bonuses
            damage += ArtifactSystem.Instance.GetTotalArtifactPower() / 10;

            // Apply dialogue-based combat modifiers
            damage = (long)(damage * activeCombatModifiers.DamageMultiplier);
            damage += activeCombatModifiers.BonusDamage;

            // Check for critical hit
            bool isCritical = random.NextDouble() < activeCombatModifiers.CriticalChance;
            if (isCritical)
            {
                damage = (long)(damage * activeCombatModifiers.CriticalMultiplier);
            }

            // Phase resistance (reduced by boss defense modifier)
            // Multiply by BossDefenseMultiplier: lower values = weaker defense = more damage taken
            double phaseResist = (1.0 - (currentPhase - 1) * 0.1) * activeCombatModifiers.BossDefenseMultiplier;
            damage = (long)(damage * phaseResist);

            // Rage boost: chance for extra attack
            bool extraAttack = activeCombatModifiers.HasRageBoost && random.NextDouble() < 0.3;

            if (isCritical)
            {
                terminal.WriteLine($"  CRITICAL HIT! You strike {boss.Name} with devastating force!", "bright_yellow");
            }
            else
            {
                terminal.WriteLine($"  You strike {boss.Name}!", "bright_yellow");
            }
            terminal.WriteLine($"  Dealt {damage:N0} damage!", "green");

            bossCurrentHP -= damage;

            // Extra attack from rage
            if (extraAttack && bossCurrentHP > 0)
            {
                await Task.Delay(400);
                long bonusDamage = (long)((baseDamage / 2) * activeCombatModifiers.DamageMultiplier);
                terminal.WriteLine($"  Your fury drives a follow-up strike!", "bright_red");
                terminal.WriteLine($"  Dealt {bonusDamage:N0} additional damage!", "bright_green");
                bossCurrentHP -= bonusDamage;
            }

            await Task.Delay(800);
        }

        /// <summary>
        /// Process player special attack
        /// </summary>
        private async Task PlayerSpecialAttack(Character player, OldGodBossData boss, TerminalEmulator terminal)
        {
            var random = new Random();
            int manaCost = 20 + player.Level;

            if (player.Mana < manaCost)
            {
                terminal.WriteLine("  Not enough mana!", "red");
                await Task.Delay(500);
                return;
            }

            player.Mana -= manaCost;

            long baseDamage = (player.Strength + player.WeapPow) * 2 + player.Wisdom;
            long damage = baseDamage + random.Next((int)Math.Min(baseDamage, int.MaxValue));

            // Apply dialogue-based combat modifiers
            damage = (long)(damage * activeCombatModifiers.DamageMultiplier);
            damage += activeCombatModifiers.BonusDamage;

            // Critical check for special attacks too
            bool isCritical = random.NextDouble() < (activeCombatModifiers.CriticalChance * 0.75); // Slightly lower crit on special
            if (isCritical)
            {
                damage = (long)(damage * activeCombatModifiers.CriticalMultiplier);
            }

            // Special attack ignores some phase resistance, further reduced by boss defense modifier
            // Multiply by BossDefenseMultiplier: lower values = weaker defense = more damage taken
            double phaseResist = (1.0 - (currentPhase - 1) * 0.05) * activeCombatModifiers.BossDefenseMultiplier;
            damage = (long)(damage * phaseResist);

            if (isCritical)
            {
                terminal.WriteLine($"  CRITICAL! You unleash a devastating special attack!", "bright_magenta");
            }
            else
            {
                terminal.WriteLine($"  You unleash a devastating special attack!", "bright_magenta");
            }
            terminal.WriteLine($"  Dealt {damage:N0} damage!", "bright_green");

            bossCurrentHP -= damage;

            await Task.Delay(1000);
        }

        /// <summary>
        /// Process player healing
        /// </summary>
        private async Task PlayerHeal(Character player, TerminalEmulator terminal)
        {
            if (player.Healing <= 0)
            {
                terminal.WriteLine("  No healing potions!", "red");
                return;
            }

            player.Healing--;
            long healAmount = player.MaxHP / 3;
            long oldHP = player.HP;
            player.HP = Math.Min(player.HP + healAmount, player.MaxHP);
            long actualHeal = player.HP - oldHP;

            // Track statistics
            player.Statistics.RecordPotionUsed(actualHeal);

            terminal.WriteLine($"  You drink a healing potion!", "green");
            terminal.WriteLine($"  Restored {actualHeal} HP!", "bright_green");

            await Task.Delay(800);
        }

        /// <summary>
        /// Attempt to save a god using the Soulweaver's Loom
        /// </summary>
        private async Task<bool> AttemptToSaveBoss(
            Character player, OldGodBossData boss, TerminalEmulator terminal)
        {
            if (!boss.CanBeSaved)
            {
                terminal.WriteLine($"  {boss.Name} cannot be saved.", "red");
                return false;
            }

            if (!ArtifactSystem.Instance.HasArtifact(ArtifactType.SoulweaversLoom))
            {
                terminal.WriteLine("  You need the Soulweaver's Loom!", "red");
                return false;
            }

            // Saving requires boss to be below 50% HP and player to have high alignment
            var hpPercent = (double)bossCurrentHP / boss.HP;
            if (hpPercent > 0.5)
            {
                terminal.WriteLine($"  {boss.Name} is too strong to save yet.", "yellow");
                terminal.WriteLine("  Weaken them first.", "gray");
                return false;
            }

            if (player.Chivalry - player.Darkness < 200)
            {
                terminal.WriteLine("  Your heart is too dark to use the Loom.", "dark_red");
                return false;
            }

            terminal.WriteLine("");
            terminal.WriteLine("  The Soulweaver's Loom glows with ancient power.", "bright_magenta");
            await Task.Delay(800);

            terminal.WriteLine($"  You reach out to {boss.Name}'s corrupted essence...", "white");
            await Task.Delay(1000);

            // Display save dialogue
            foreach (var line in boss.SaveDialogue)
            {
                terminal.WriteLine($"  \"{line}\"", "bright_cyan");
                await Task.Delay(300);
            }

            terminal.WriteLine("");
            terminal.WriteLine($"  {boss.Name} is cleansed of corruption!", "bright_green");

            return true;
        }

        /// <summary>
        /// Process boss turn - attacks player and teammates
        /// </summary>
        private async Task<bool> BossTurn(Character player, OldGodBossData boss, TerminalEmulator terminal)
        {
            var random = new Random();

            // Boss attacks multiple times based on AttacksPerRound
            int attackCount = boss.AttacksPerRound;

            terminal.WriteLine("");
            terminal.WriteLine($"  ═══ {boss.Name.ToUpper()}'S TURN ═══", boss.ThemeColor);

            for (int attack = 0; attack < attackCount; attack++)
            {
                if (player.HP <= 0) break;

                // If boss is confused (from broken logic), 25% chance to miss
                if (activeCombatModifiers.BossConfused && random.NextDouble() < 0.25)
                {
                    terminal.WriteLine("");
                    terminal.WriteLine($"  {boss.Name} hesitates, confused by internal contradictions!", "cyan");
                    await Task.Delay(500);
                    continue;
                }

                // Select ability based on phase
                var abilities = boss.Abilities.Where(a => a.Phase <= currentPhase).ToList();
                var ability = abilities[random.Next(abilities.Count)];

                terminal.WriteLine("");
                terminal.WriteLine($"  {boss.Name} uses {ability.Name}!", boss.ThemeColor);

                long damage = ability.BaseDamage + random.Next(ability.BaseDamage / 2);
                damage = (long)(damage * (1.0 + (currentPhase - 1) * 0.15)); // Phase scaling

                // Apply dialogue-based boss damage modifier
                damage = (long)(damage * activeCombatModifiers.BossDamageMultiplier);

                // If boss is weakened, reduce damage further
                if (activeCombatModifiers.BossWeakened)
                {
                    damage = (long)(damage * 0.90);
                }

                // Select target: player or a teammate
                // 60% chance to target player, 40% to target a random alive teammate
                var aliveTeammates = activeTeammates.Where(t => t.IsAlive).ToList();
                bool targetPlayer = aliveTeammates.Count == 0 || random.Next(100) < 60;

                if (targetPlayer)
                {
                    // Attack player - apply player's defense modifiers
                    long defense = player.Defence + player.ArmPow;
                    defense = (long)(defense * activeCombatModifiers.DefenseMultiplier);
                    defense += activeCombatModifiers.BonusDefense;

                    double defenseReduction = Math.Min(0.75, defense / (double)(defense + 200));
                    long actualDamage = (long)(damage * (1.0 - defenseReduction));
                    actualDamage = Math.Max(10, actualDamage);

                    // Insight: chance to dodge
                    if (activeCombatModifiers.HasInsight && random.NextDouble() < 0.15)
                    {
                        terminal.WriteLine($"  You read the attack and dodge!", "bright_cyan");
                        actualDamage = actualDamage / 3;
                    }

                    player.HP -= actualDamage;

                    terminal.WriteLine($"  You take {actualDamage} damage!", "red");
                }
                else
                {
                    // Attack a random teammate
                    var target = aliveTeammates[random.Next(aliveTeammates.Count)];

                    // Apply teammate defense as percentage reduction
                    double teammateDefenseReduction = Math.Min(0.50, target.Defense / (double)(target.Defense + 100));
                    long actualDamage = (long)(damage * (1.0 - teammateDefenseReduction));
                    actualDamage = Math.Max(15, actualDamage);

                    target.CurrentHP -= actualDamage;

                    // Also damage the underlying character if it's an NPC
                    if (!target.IsCompanion && target.CharacterRef != null)
                    {
                        target.CharacterRef.HP = Math.Max(0, target.CharacterRef.HP - actualDamage);
                    }

                    if (target.CurrentHP <= 0)
                    {
                        target.CurrentHP = 0;
                        terminal.WriteLine($"  {target.Name} takes {actualDamage} damage and falls!", "dark_red");
                        terminal.WriteLine($"  \"{target.Name}! NO!\"", "red");
                    }
                    else
                    {
                        terminal.WriteLine($"  {target.Name} takes {actualDamage} damage!", "yellow");
                    }
                }

                // Special effects
                if (ability.HasSpecialEffect)
                {
                    terminal.WriteLine($"  {ability.EffectDescription}", "dark_red");
                }

                await Task.Delay(500);
            }

            if (attackCount > 1)
            {
                terminal.WriteLine($"  ({attackCount} attacks this round)", "gray");
            }

            // Minion attacks
            var aliveMinionsForAttack = activeMinions.Where(m => m.IsAlive).ToList();
            if (aliveMinionsForAttack.Count > 0 && player.HP > 0)
            {
                terminal.WriteLine("");
                terminal.WriteLine($"  ═══ MINION ATTACKS ═══", "dark_red");

                foreach (var minion in aliveMinionsForAttack)
                {
                    if (player.HP <= 0) break;

                    // Minions have simpler attack logic
                    long minionDamage = minion.Attack + random.Next((int)minion.Attack / 2);

                    // Apply player defense
                    long defense = player.Defence + player.ArmPow;
                    double defReduction = Math.Min(0.6, defense / (double)(defense + 150));
                    minionDamage = (long)(minionDamage * (1.0 - defReduction));
                    minionDamage = Math.Max(5, minionDamage);

                    player.HP -= minionDamage;
                    terminal.WriteLine($"  {minion.Name} strikes you for {minionDamage} damage!", "red");
                    await Task.Delay(300);
                }
            }

            // Maelketh summons more soldiers every 3 rounds in phase 2+
            roundsSinceLastSummon++;
            if (boss.Type == OldGodType.Maelketh && currentPhase >= 2 && roundsSinceLastSummon >= 3)
            {
                // Summon 1-2 more soldiers
                int summonCount = 1 + random.Next(2);
                terminal.WriteLine("");
                SpawnSpectralSoldiers(summonCount, terminal);
                await Task.Delay(500);
            }

            await Task.Delay(500);

            return player.HP <= 0;
        }

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

            terminal.WriteLine($"  The corruption fades from {boss.Name}.", "white");
            terminal.WriteLine("  For the first time in millennia, they see clearly.", "white");
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
            terminal.WriteLine($"  {boss.Name}'s divine essence scatters to the winds.", "white");
            terminal.WriteLine("");

            // Update story state
            var story = StoryProgressionSystem.Instance;
            story.UpdateGodState(boss.Type, GodStatus.Defeated);
            story.SetStoryFlag($"{boss.Type.ToString().ToLower()}_destroyed", true);

            // Award artifact
            await ArtifactSystem.Instance.CollectArtifact(player, boss.ArtifactDropped, terminal);

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

            terminal.WriteLine($"  {boss.Name} stands over your broken form.", "red");
            terminal.WriteLine("");

            terminal.WriteLine($"  \"Pathetic mortal. Did you truly think", boss.ThemeColor);
            terminal.WriteLine($"   you could challenge a god?\"", boss.ThemeColor);
            terminal.WriteLine("");

            await Task.Delay(2000);

            // Player doesn't die permanently in boss fights - they're sent back
            terminal.WriteLine("  As darkness claims you, a voice speaks.", "gray");
            terminal.WriteLine("  \"Not yet. The wheel has not finished turning.\"", "bright_magenta");
            terminal.WriteLine("");
            terminal.WriteLine("  You awaken at the dungeon entrance, weakened but alive.", "white");

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
        /// Initialize teammates from both active companions AND dungeon teammates (NPCs)
        /// </summary>
        private void InitializeTeammates()
        {
            activeTeammates.Clear();

            // Add companions from CompanionSystem
            var companionSystem = CompanionSystem.Instance;
            if (companionSystem != null)
            {
                foreach (var companion in companionSystem.GetActiveCompanions())
                {
                    if (companion != null && !companion.IsDead)
                    {
                        // Scale companion HP based on their level and base stats
                        long scaledHP = companion.BaseStats.HP * (1 + companion.Level / 10);

                        // Determine role string from CombatRole
                        string role = companion.CombatRole switch
                        {
                            CombatRole.Tank => "Tank",
                            CombatRole.Damage => "DPS",
                            CombatRole.Healer => "Healer",
                            CombatRole.Hybrid => "Hybrid",
                            _ => "Fighter"
                        };

                        activeTeammates.Add(new BossFightTeammate
                        {
                            Name = companion.Name,
                            CurrentHP = scaledHP,
                            MaxHP = scaledHP,
                            Attack = companion.BaseStats.Attack + companion.Level * 2,
                            Defense = companion.BaseStats.Defense,
                            Role = role,
                            Abilities = companion.Abilities ?? Array.Empty<string>(),
                            IsCompanion = true,
                            CompanionRef = companion
                        });
                    }
                }
            }

            // Add NPC teammates from dungeon (spouses, team members, lovers, etc.)
            if (dungeonTeammates != null)
            {
                foreach (var npc in dungeonTeammates)
                {
                    if (npc != null && npc.IsAlive)
                    {
                        // Skip if this is a companion Character (already added above)
                        if (npc.IsCompanion) continue;

                        // Determine role based on class
                        string role = npc.Class switch
                        {
                            CharacterClass.Warrior or CharacterClass.Barbarian or CharacterClass.Paladin => "Tank",
                            CharacterClass.Assassin or CharacterClass.Ranger => "DPS",
                            CharacterClass.Cleric => "Healer",
                            CharacterClass.Magician or CharacterClass.Sage => "Hybrid",
                            _ => "Fighter"
                        };

                        // Generate abilities based on class
                        string[] abilities = npc.Class switch
                        {
                            CharacterClass.Warrior => new[] { "Cleave", "Shield Bash", "Battle Cry" },
                            CharacterClass.Barbarian => new[] { "Rage Strike", "Reckless Attack", "Intimidate" },
                            CharacterClass.Paladin => new[] { "Holy Strike", "Lay on Hands", "Divine Shield" },
                            CharacterClass.Assassin => new[] { "Backstab", "Poison Strike", "Shadow Step" },
                            CharacterClass.Ranger => new[] { "Precise Shot", "Twin Strike", "Hunter's Mark" },
                            CharacterClass.Cleric => new[] { "Divine Smite", "Heal", "Holy Light" },
                            CharacterClass.Magician => new[] { "Fireball", "Lightning Bolt", "Arcane Blast" },
                            CharacterClass.Sage => new[] { "Mind Blast", "Energy Drain", "Mystic Strike" },
                            CharacterClass.Bard => new[] { "Inspiring Song", "Cutting Words", "Vicious Mockery" },
                            CharacterClass.Alchemist => new[] { "Acid Flask", "Healing Bomb", "Explosive Mixture" },
                            CharacterClass.Jester => new[] { "Trick Attack", "Confuse", "Lucky Strike" },
                            _ => new[] { "Attack", "Power Strike", "Defend" }
                        };

                        activeTeammates.Add(new BossFightTeammate
                        {
                            Name = npc.DisplayName,
                            CurrentHP = npc.HP,
                            MaxHP = npc.MaxHP,
                            Attack = npc.Strength + npc.WeapPow,
                            Defense = npc.Defence + npc.ArmPow,
                            Role = role,
                            Abilities = abilities,
                            IsCompanion = false,
                            CharacterRef = npc
                        });
                    }
                }
            }
        }

        /// <summary>
        /// Process all teammates' attacks against the boss
        /// </summary>
        private async Task TeammatesAttack(OldGodBossData boss, TerminalEmulator terminal)
        {
            var aliveTeammates = activeTeammates.Where(t => t.IsAlive).ToList();
            if (aliveTeammates.Count == 0) return;

            terminal.WriteLine("");
            terminal.WriteLine($"  ═══ YOUR ALLIES ATTACK ═══", "bright_cyan");

            var random = new Random();

            foreach (var teammate in aliveTeammates)
            {
                if (bossCurrentHP <= 0) break;

                // Calculate teammate damage based on their role and stats
                long baseDamage = teammate.Attack;

                // Add role-specific bonuses
                switch (teammate.Role)
                {
                    case "DPS":
                        baseDamage = (long)(baseDamage * 1.5); // DPS does 50% more damage
                        break;
                    case "Hybrid":
                        baseDamage = (long)(baseDamage * 1.2); // Hybrids do 20% more
                        break;
                    case "Tank":
                        baseDamage = (long)(baseDamage * 0.8); // Tanks do less damage
                        break;
                    case "Healer":
                        baseDamage = (long)(baseDamage * 0.6); // Healers do least damage
                        break;
                    case "Fighter":
                    default:
                        // Standard damage for generic fighters
                        break;
                }

                // Add some variance
                long damage = baseDamage + random.Next(Math.Max(1, (int)(baseDamage / 3)));

                // Check if there are alive minions - 60% chance teammates focus minions instead of boss
                var aliveMinions = activeMinions.Where(m => m.IsAlive).ToList();
                bool attackMinion = aliveMinions.Count > 0 && random.Next(100) < 60;

                if (attackMinion)
                {
                    // Attack a random minion
                    var targetMinion = aliveMinions[random.Next(aliveMinions.Count)];
                    damage = Math.Max(10, damage); // Minions take more damage from teammates

                    targetMinion.CurrentHP -= damage;

                    string abilityName = GetTeammateAbilityName(teammate);
                    terminal.WriteLine($"  {teammate.Name} uses {abilityName}!", "cyan");

                    if (targetMinion.CurrentHP <= 0)
                    {
                        targetMinion.CurrentHP = 0;
                        terminal.WriteLine($"    Dealt {damage:N0} damage - {targetMinion.Name} destroyed!", "bright_green");
                    }
                    else
                    {
                        terminal.WriteLine($"    Dealt {damage:N0} damage to {targetMinion.Name}!", "bright_cyan");
                    }
                }
                else
                {
                    // Attack the boss
                    // Phase resistance affects teammates
                    damage = (long)(damage * (1.0 - (currentPhase - 1) * 0.05));
                    damage = Math.Max(5, damage);

                    bossCurrentHP -= damage;

                    // Pick an ability name
                    string abilityName = GetTeammateAbilityName(teammate);

                    terminal.WriteLine($"  {teammate.Name} uses {abilityName}!", "cyan");
                    terminal.WriteLine($"    Dealt {damage:N0} damage to {boss.Name}!", "bright_cyan");
                }

                await Task.Delay(400);
            }

            // Healers may heal the party
            foreach (var teammate in aliveTeammates.Where(t => t.Role == "Healer" || t.Role == "Hybrid"))
            {
                // 50% chance to heal
                if (random.Next(100) < 50)
                {
                    long healAmount = 20 + random.Next(30);
                    if (teammate.IsCompanion && teammate.CompanionRef != null)
                    {
                        healAmount = teammate.CompanionRef.BaseStats.HealingPower * 2 + random.Next(20);
                    }

                    terminal.WriteLine($"  {teammate.Name} channels healing energy!", "green");
                    terminal.WriteLine($"    The party is healed for {healAmount} HP!", "bright_green");

                    await Task.Delay(300);
                }
            }

            await Task.Delay(500);
        }

        /// <summary>
        /// Get a thematic ability name for a teammate based on their role and abilities
        /// </summary>
        private string GetTeammateAbilityName(BossFightTeammate teammate)
        {
            var random = new Random();

            if (teammate.Abilities != null && teammate.Abilities.Length > 0)
            {
                // Use one of their defined abilities (skip "Sacrifice")
                var usableAbilities = teammate.Abilities.Where(a => !a.Contains("Sacrifice")).ToArray();
                if (usableAbilities.Length > 0)
                {
                    return usableAbilities[random.Next(usableAbilities.Length)];
                }
            }

            // Fallback generic attacks by role
            return teammate.Role switch
            {
                "Tank" => "Shield Bash",
                "DPS" => "Vicious Strike",
                "Healer" => "Divine Smite",
                "Hybrid" => "Power Attack",
                "Fighter" => "Heavy Blow",
                _ => "Attack"
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

    public enum BossAction
    {
        Attack,
        Special,
        Heal,
        Save,
        Flee
    }

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
