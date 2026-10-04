using System;
using System.Threading.Tasks;
using UsurperRemake.Locations;
using UsurperRemake.UI;
using UsurperRemake.Utils;

namespace UsurperRemake.Systems
{
    /// <summary>
    /// Opening Sequence System - Handles the narrative hook for new players
    /// Triggers the mysterious stranger encounter and sets up the main story
    /// </summary>
    public class OpeningSequenceSystem
    {
        // v1.2.5: one instance per session. It holds no story state (that is in the session's story
        // flags, which are saved); only which character this session last entered a location as.
        private static OpeningSequenceSystem? _fallbackInstance;
        public static OpeningSequenceSystem Instance
        {
            get
            {
                var ctx = UsurperRemake.Server.SessionContext.Current;
                if (ctx != null) return ctx.OpeningSequence ??= new OpeningSequenceSystem();
                return _fallbackInstance ??= new OpeningSequenceSystem();
            }
        }

        /// <summary>The flag that marks the scene seen. It is set when the scene ends.</summary>
        internal const string MetFlag = "met_mysterious_stranger";
        internal const int MinLevel = 3;

        /// <summary>The roll against the trigger chance, in [0, 1). Tests replace it.</summary>
        internal Func<double> Roll { get; set; } = () => Random.Shared.NextDouble();

        // The character this session last entered a location as. The first entry after a login, a load
        // or a door re-entry (a new Character object) never shows the scene.
        private Character? _enteredAs;

        /// <summary>
        /// v1.2.5: called by BaseLocation.LocationLoop once per location entry, after the entry encounters
        /// and before the narrative encounters, outside combat and the location menu. Returns true when
        /// the scene was shown (the caller then shows no other entry scene).
        /// <paramref name="otherSceneShown"/>: an entry encounter already ran, so the scene waits.
        /// </summary>
        public async Task<bool> CheckOpeningSequenceTriggers(Character player, GameLocation location, TerminalEmulator terminal,
            bool otherSceneShown = false)
        {
            bool firstEntry = !ReferenceEquals(_enteredAs, player);
            _enteredAs = player;
            if (firstEntry || otherSceneShown || !IsEligible(player) || !CanTriggerHere(location))
                return false;
            if (Roll() >= GetTriggerChance(location, player))
                return false;

            await TriggerStrangerEncounter(player, terminal);
            return true;
        }

        /// <summary>
        /// Who may meet the Stranger's opening scene: a living character of level 3 or more who has not
        /// seen it, has had no encounter with Noctura's disguised stranger and does not know the truth
        /// about her (user decision 2026-10-04: the scene's "You don't know me. Not yet." must stay true).
        /// </summary>
        internal static bool IsEligible(Character player)
        {
            var story = StoryProgressionSystem.Instance;
            var stranger = StrangerEncounterSystem.Instance;
            return player.IsAlive
                && player.Level >= MinLevel
                && !story.HasStoryFlag(MetFlag)
                && stranger.EncountersHad == 0
                && !stranger.PlayerKnowsTruth
                && !story.HasFlag(StoryFlag.KnowsNocturaTruth);
        }

        /// <summary>
        /// Trigger chance based on location and player state
        /// </summary>
        internal static double GetTriggerChance(GameLocation location, Character player)
        {
            double baseChance = location switch
            {
                GameLocation.MainStreet => 0.15,
                GameLocation.TheInn => 0.12,
                GameLocation.DarkAlley => 0.25, // mysterious!
                _ => 0.05
            };

            // Level modifier - higher level = more likely
            baseChance += player.Level * 0.02;

            // Monster kills modifier - active player more likely
            baseChance += Math.Min(player.MKills * 0.005, 0.15);

            return Math.Min(baseChance, 0.5); // Cap at 50%
        }

        /// <summary>
        /// The locations the scene may happen in. The dungeon, homes and every other location are left
        /// out. (The Temple keeps its own loop and never reaches BaseLocation.LocationLoop.)
        /// </summary>
        internal static bool CanTriggerHere(GameLocation location) => location switch
        {
            GameLocation.MainStreet or GameLocation.TheInn or GameLocation.DarkAlley or GameLocation.AuctionHouse => true,
            _ => false
        };

        /// <summary>
        /// Trigger the mysterious stranger encounter
        /// </summary>
        private async Task TriggerStrangerEncounter(Character player, TerminalEmulator terminal)
        {
            // Set up the atmosphere
            terminal.WriteLine("");
            if (!GameConfig.ScreenReaderMode)
                terminal.WriteLine("═══════════════════════════════════════════════════", "dark_magenta");
            terminal.WriteLine("");

            await Pacing.Wait(1000);

            terminal.WriteLine(Loc.Get("opening.air_grows_cold"), "gray");
            await Pacing.Wait(800);

            terminal.WriteLine(Loc.Get("opening.shadows_lengthen"), "dark_gray");
            await Pacing.Wait(800);

            terminal.WriteLine(Loc.Get("opening.time_pauses"), "white");
            await Pacing.Wait(1200);

            terminal.WriteLine("");
            terminal.WriteLine(Loc.Get("opening.not_alone"), "bright_magenta");
            terminal.WriteLine("");

            await Pacing.Wait(1500);

            // Run the dialogue
            var dialogue = DialogueSystem.Instance;
            await dialogue.StartDialogue(player, "mysterious_stranger_intro", terminal);

            // v1.2.5: the scene has ended (played through, or the player said nothing), so it is marked
            // seen now and never earlier: a run cut off by a disconnect plays again on a later entry, and
            // its rewards carry once flags, so they are never paid twice.
            StoryProgressionSystem.Instance.SetStoryFlag(MetFlag, true);
            StoryProgressionSystem.Instance.AdvanceChapter(StoryChapter.TheFirstSeal);

            await terminal.PressAnyKey();
        }

        /// <summary>
        /// Check for follow-up story hooks after the initial encounter
        /// </summary>
        private async Task<bool> CheckFollowUpHooks(Character player, string locationId, TerminalEmulator terminal)
        {
            var story = StoryProgressionSystem.Instance;

            // Check for dungeon hints at specific levels
            if (player.Level >= 10 && !story.HasStoryFlag("first_seal_hint"))
            {
                if (locationId.Equals("temple", StringComparison.OrdinalIgnoreCase))
                {
                    await ShowFirstSealHint(player, terminal);
                    return true;
                }
            }

            // Check for god awakening warnings
            if (player.Level >= 25 && !story.HasStoryFlag("maelketh_stirring_warning"))
            {
                if (locationId.Equals("tavern", StringComparison.OrdinalIgnoreCase) ||
                    locationId.Equals("inn", StringComparison.OrdinalIgnoreCase))
                {
                    await ShowGodStirringWarning(player, terminal, "Maelketh");
                    return true;
                }
            }

            return false;
        }

        /// <summary>
        /// Show hint about the first seal location
        /// </summary>
        private async Task ShowFirstSealHint(Character player, TerminalEmulator terminal)
        {
            terminal.WriteLine("");
            terminal.WriteLine(Loc.Get("opening.priest_approaches"), "cyan");
            terminal.WriteLine("");

            await Pacing.Wait(500);

            terminal.WriteLine(Loc.Get("opening.priest_mark"), "yellow");
            terminal.WriteLine(Loc.Get("opening.priest_visions"), "yellow");
            terminal.WriteLine("");

            await Pacing.Wait(800);

            terminal.WriteLine(Loc.Get("opening.priest_first_seal"), "white");
            terminal.WriteLine(Loc.Get("opening.priest_25th_level"), "white");
            terminal.WriteLine(Loc.Get("opening.priest_god_of_war"), "white");
            terminal.WriteLine("");

            await Pacing.Wait(800);

            terminal.WriteLine(Loc.Get("opening.priest_be_ready"), "cyan");
            terminal.WriteLine("");

            await Pacing.Wait(500);

            terminal.WriteLine(Loc.Get("opening.priest_fades"), "gray");
            terminal.WriteLine("");

            StoryProgressionSystem.Instance.SetStoryFlag("first_seal_hint", true);

            await terminal.PressAnyKey();
        }

        /// <summary>
        /// Show warning about a god beginning to stir
        /// </summary>
        private async Task ShowGodStirringWarning(Character player, TerminalEmulator terminal, string godName)
        {
            terminal.WriteLine("");
            terminal.WriteLine(Loc.Get("opening.tremor"), "red");
            terminal.WriteLine(Loc.Get("opening.tankards_rattle"), "white");
            terminal.WriteLine("");

            await Pacing.Wait(800);

            terminal.WriteLine(Loc.Get("opening.veteran_turns"), "gray");
            terminal.WriteLine(Loc.Get("opening.veteran_feel_that"), "yellow");
            terminal.WriteLine(Loc.Get("opening.veteran_god_awakens", godName), "yellow");
            terminal.WriteLine("");

            await Pacing.Wait(1000);

            terminal.WriteLine(Loc.Get("opening.veteran_dangerous"), "white");
            terminal.WriteLine(Loc.Get("opening.veteran_do_it_soon"), "white");
            terminal.WriteLine("");

            StoryProgressionSystem.Instance.SetStoryFlag($"{godName.ToLower()}_stirring_warning", true);

            await terminal.PressAnyKey();
        }

        /// <summary>
        /// Check if the opening sequence is complete
        /// </summary>
        public bool IsOpeningComplete()
        {
            return StoryProgressionSystem.Instance.HasStoryFlag(MetFlag);
        }
    }

    /// <summary>
    /// Cycle/Prestige System - Handles New Game+ mechanics
    /// </summary>
    public class CycleSystem
    {
        private static CycleSystem? _fallbackInstance;
        public static CycleSystem Instance
        {
            get
            {
                var ctx = UsurperRemake.Server.SessionContext.Current;
                if (ctx != null) return ctx.Cycle;
                return _fallbackInstance ??= new CycleSystem();
            }
        }

        /// <summary>
        /// Start a new cycle (New Game+)
        /// </summary>
        public async Task StartNewCycle(Character player, EndingType endingAchieved, TerminalEmulator terminal)
        {
            var story = StoryProgressionSystem.Instance;
            var currentCycle = story.CurrentCycle;

            terminal.Clear();
            terminal.WriteLine("");
            UIHelper.WriteBoxHeader(terminal, Loc.Get("opening.eternal_cycle"), "bright_yellow", 67);
            terminal.WriteLine("");

            await Pacing.Wait(1000);

            terminal.WriteLine(Loc.Get("opening.world_fades"), "white");
            await Pacing.Wait(1500);

            terminal.WriteLine(Loc.Get("opening.familiar_voice"), "bright_magenta");
            terminal.WriteLine("");

            await Pacing.Wait(1000);

            terminal.WriteLine(Loc.Get("opening.cycle_never_ends"), "bright_magenta");
            terminal.WriteLine(Loc.Get("opening.wheel_turns"), "bright_magenta");
            terminal.WriteLine(Loc.Get("opening.you_remember"), "bright_magenta");
            terminal.WriteLine("");

            await Pacing.Wait(1500);

            // Calculate bonuses based on ending
            var bonuses = CalculateCycleBonuses(player, endingAchieved, currentCycle);

            terminal.WriteLine(Loc.Get("opening.entering_cycle", currentCycle + 1), "bright_cyan");
            terminal.WriteLine("");
            terminal.WriteLine(Loc.Get("opening.carry_forward"), "green");
            terminal.WriteLine(Loc.Get("opening.bonus_strength", bonuses.StrengthBonus), "white");
            terminal.WriteLine(Loc.Get("opening.bonus_defence", bonuses.DefenceBonus), "white");
            terminal.WriteLine(Loc.Get("opening.bonus_stamina", bonuses.StaminaBonus), "white");
            terminal.WriteLine(Loc.Get("opening.bonus_gold", bonuses.GoldBonus), "yellow");
            terminal.WriteLine(Loc.Get("opening.bonus_exp", bonuses.ExpMultiplier * 100 - 100), "cyan");
            terminal.WriteLine("");

            if (bonuses.KeepsArtifactKnowledge)
            {
                terminal.WriteLine($"  * {Loc.Get("opening.bonus_artifacts")}", "bright_magenta");
            }
            if (bonuses.StartWithKey)
            {
                terminal.WriteLine($"  * {Loc.Get("opening.bonus_key")}", "bright_magenta");
            }

            await terminal.PressAnyKey(Loc.Get("opening.press_enter_new_cycle"));

            // Reset story with cycle bonuses
            story.StartNewCycle(endingAchieved);

            // Apply bonuses to player
            ApplyCycleBonuses(player, bonuses);

        }

        /// <summary>
        /// Calculate bonuses for the new cycle
        /// </summary>
        private CycleBonuses CalculateCycleBonuses(Character player, EndingType ending, int cycle)
        {
            var bonuses = new CycleBonuses();

            // Base bonuses scale with cycle
            bonuses.StrengthBonus = CycleStatBonusPerCycle * cycle;
            bonuses.DefenceBonus = CycleStatBonusPerCycle * cycle;
            bonuses.StaminaBonus = CycleStatBonusPerCycle * cycle;
            bonuses.GoldBonus = 500 * cycle;
            bonuses.ExpMultiplier = 1.0f + (0.1f * cycle);

            // Ending-specific bonuses
            switch (ending)
            {
                case EndingType.Usurper:
                    // Dark path - more power, less luck
                    bonuses.StrengthBonus += 10;
                    bonuses.DarknessBonus = 100;
                    break;

                case EndingType.Savior:
                    // Light path - balanced bonuses
                    bonuses.ChivalryBonus = 100;
                    bonuses.KeepsArtifactKnowledge = true;
                    break;

                case EndingType.Defiant:
                    // Independent path - unique bonuses
                    bonuses.ExpMultiplier += 0.25f;
                    bonuses.StartWithKey = true;
                    break;

                case EndingType.TrueEnding:
                    // Perfect path - all bonuses
                    bonuses.StrengthBonus += 15;
                    bonuses.DefenceBonus += 15;
                    bonuses.StaminaBonus += 15;
                    bonuses.KeepsArtifactKnowledge = true;
                    bonuses.StartWithKey = true;
                    bonuses.ExpMultiplier += 0.5f;
                    break;
            }

            return bonuses;
        }

        /// <summary>
        /// Apply cycle bonuses to player
        /// </summary>
        private void ApplyCycleBonuses(Character player, CycleBonuses bonuses)
        {
            // 1.2.0: the Strength, Defence and Stamina bonus is granted to the new character only
            // (ApplyCycleBonusesToNewCharacter), as lasting grants; here it was wiped at once.
            player.Gold += bonuses.GoldBonus;
            player.Chivalry += bonuses.ChivalryBonus;
            player.Darkness += bonuses.DarknessBonus;

            // Store exp multiplier on character so CombatEngine can apply it
            player.CycleExpMultiplier = bonuses.ExpMultiplier;

            if (bonuses.StartWithKey)
            {
                StoryProgressionSystem.Instance.SetStoryFlag("has_ancient_key", true);
            }

            if (bonuses.KeepsArtifactKnowledge)
            {
                StoryProgressionSystem.Instance.SetStoryFlag("knows_artifact_locations", true);
            }
        }

        /// <summary>
        /// Apply cycle bonuses to a fresh NG+ character (called from CreateNewGame)
        /// </summary>
        public void ApplyCycleBonusesToNewCharacter(Character player, int cycle, EndingType lastEnding)
        {
            // cycle is already incremented (e.g., 2 for first NG+), use cycle-1 for bonus calculation
            var bonuses = CalculateCycleBonuses(player, lastEnding, cycle - 1);
            ApplyCycleBonuses(player, bonuses);
            // 1.2.0: lasting grants to the Base fields; before, the recalculation right after this call wiped them
            player.GrantPermanentStats(
                (StatKind.Strength, bonuses.StrengthBonus),
                (StatKind.Defence, bonuses.DefenceBonus),
                (StatKind.Stamina, bonuses.StaminaBonus));

            // Apply starting level bonus from MetaProgressionSystem
            int startingLevel = MetaProgressionSystem.Instance.GetStartingLevelBonus();
            if (startingLevel > 1)
            {
                for (int i = 1; i < startingLevel; i++)
                {
                    player.Level++;
                    LevelMasterLocation.ApplyClassStatIncreases(player);
                    player.TrainingPoints += TrainingSystem.CalculateTrainingPointsPerLevel(player);
                }
                // Set XP to match the new level so they don't instantly level again
                player.Experience = LevelMasterLocation.GetExperienceForLevel(startingLevel);
                player.RecalculateStats();
            }
        }

        /// <summary>
        /// 1.2.0 one-time login backfill of the NG+ cycle bonus, which no character received before
        /// (it was wiped by the recalculation at NG+ start). For a character loaded from an older save
        /// (CycleStatBonusApplied false) this grants the per-cycle part, +5 Strength, Defence and
        /// Stamina for each completed cycle, from the saved cycle number; the ending-specific extra is
        /// not derivable and is not granted. An immortal's cycle was advanced once more at ascension
        /// without a new start, so that step is not counted. Cycle 1 grants nothing. Sets the flag, so
        /// it runs once per character. Returns the amount granted per stat.
        /// </summary>
        public static int BackfillCycleStatBonus(Character player, int savedCycle)
        {
            if (player == null || player.CycleStatBonusApplied) return 0;
            int completedCycles = Math.Max(0, savedCycle - 1 - (player.IsImmortal ? 1 : 0));
            int amount = CycleStatBonusPerCycle * completedCycles;
            if (amount > 0)
            {
                player.GrantPermanentStats(
                    (StatKind.Strength, amount),
                    (StatKind.Defence, amount),
                    (StatKind.Stamina, amount));
            }
            player.CycleStatBonusApplied = true;
            return amount;
        }

        /// <summary>The per-cycle Strength, Defence and Stamina bonus (CalculateCycleBonuses).</summary>
        public const int CycleStatBonusPerCycle = 5;

        /// <summary>
        /// Get a list of current cycle bonuses for display purposes
        /// </summary>
        public List<string> GetCurrentCycleBonuses()
        {
            var bonuses = new List<string>();
            var story = StoryProgressionSystem.Instance;
            int cycle = story.CurrentCycle;

            if (cycle <= 1)
                return bonuses; // No bonuses on first cycle

            // Calculate base bonuses
            int strBonus = 5 * (cycle - 1);
            int defBonus = 5 * (cycle - 1);
            int staBonus = 5 * (cycle - 1);
            int goldBonus = 500 * (cycle - 1);
            float expMult = 1.0f + (0.1f * (cycle - 1));

            bonuses.Add(Loc.Get("opening.cycle_str_bonus", strBonus));
            bonuses.Add(Loc.Get("opening.cycle_def_bonus", defBonus));
            bonuses.Add(Loc.Get("opening.cycle_sta_bonus", staBonus));
            bonuses.Add(Loc.Get("opening.cycle_gold_bonus", goldBonus));
            bonuses.Add(Loc.Get("opening.cycle_exp_bonus", $"{(expMult - 1) * 100:0}"));

            // Starting level bonus from meta-progression
            int startingLevel = MetaProgressionSystem.Instance.GetStartingLevelBonus();
            if (startingLevel > 1)
            {
                bonuses.Add(Loc.Get("opening.cycle_level_bonus", startingLevel));
            }

            // Check for special bonuses from endings
            if (story.HasStoryFlag("keeps_artifact_knowledge") || story.HasStoryFlag("knows_artifact_locations"))
            {
                bonuses.Add(Loc.Get("opening.cycle_artifacts_revealed"));
            }
            if (story.HasStoryFlag("has_ancient_key"))
            {
                bonuses.Add(Loc.Get("opening.cycle_ancient_key"));
            }
            if (story.CompletedEndings.Contains(EndingType.TrueEnding))
            {
                bonuses.Add(Loc.Get("opening.cycle_ocean_remembers"));
            }

            return bonuses;
        }

        /// <summary>
        /// Check if player qualifies for true ending
        /// </summary>
        public bool QualifiesForTrueEnding(Character player)
        {
            var story = StoryProgressionSystem.Instance;

            // Must have completed at least 3 cycles (cycles 3, 4, 5... qualify)
            if (story.CurrentCycle < 3) return false;

            // Must have saved at least 2 gods
            int savedGods = 0;
            if (story.HasStoryFlag("veloura_saved")) savedGods++;
            if (story.HasStoryFlag("aurelion_saved")) savedGods++;
            if (story.HasStoryFlag("terravok_awakened")) savedGods++;
            if (savedGods < 2) return false;

            // Must have allied with Noctura
            if (!story.HasStoryFlag("noctura_ally")) return false;

            // Must have collected all Seven Seals
            if (story.CollectedSeals.Count < 7) return false;

            // Must have balanced alignment
            var alignment = player.Chivalry - player.Darkness;
            if (Math.Abs(alignment) > 200) return false;

            return true;
        }
    }
}
