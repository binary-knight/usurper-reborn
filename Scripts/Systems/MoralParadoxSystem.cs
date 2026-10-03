using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using UsurperRemake.UI;
using UsurperRemake.Utils;

namespace UsurperRemake.Systems
{
    /// <summary>
    /// Moral Paradox System - Presents choices with no clear "good" answer
    /// Every choice has weight, every kindness has cost, every cruelty has reason
    /// The player IS the problem - their desire to "win" perpetuates the cycle
    /// </summary>
    public class MoralParadoxSystem
    {
        private static MoralParadoxSystem? _fallbackInstance;
        public static MoralParadoxSystem Instance
        {
            get
            {
                var ctx = UsurperRemake.Server.SessionContext.Current;
                if (ctx != null) return ctx.MoralParadox;
                return _fallbackInstance ??= new MoralParadoxSystem();
            }
        }

        // All moral paradoxes in the game
        private Dictionary<string, MoralParadox> paradoxes = new();

        // Player's choices
        private Dictionary<string, ParadoxChoice> madeChoices = new();

        // Events
        public event Action<string, ParadoxChoice>? OnParadoxResolved;

        // Track player's moral patterns
        public int UtilitarianChoices { get; private set; } = 0;  // Greater good
        public int DeontologicalChoices { get; private set; } = 0; // Rules/principles
        public int VirtueChoices { get; private set; } = 0;        // Character/compassion
        public int NihilistChoices { get; private set; } = 0;      // Rejection of meaning

        public MoralParadoxSystem()
        {
            InitializeParadoxes();
        }

        /// <summary>
        /// Initialize all moral paradoxes
        /// </summary>
        private void InitializeParadoxes()
        {
            // PARADOX 1: The Possessed Child
            paradoxes["possessed_child"] = new MoralParadox
            {
                Id = "possessed_child",
                TriggerFloor = 25,
                TriggerChapter = StoryChapter.RisingPower,
                Choices = new List<ParadoxOption>
                {
                    new ParadoxOption
                    {
                        Id = "kill_child",
                        MoralType = MoralType.Utilitarian,
                        ChivalryChange = -100,
                        DarknessChange = 200,
                        WisdomChange = 2,
                        StoryFlag = "killed_possessed_child"
                    },
                    new ParadoxOption
                    {
                        Id = "spare_child",
                        MoralType = MoralType.Deontological,
                        ChivalryChange = 100,
                        DarknessChange = 0,
                        WisdomChange = 0,
                        VillageDeaths = 100,
                        StoryFlag = "village_consumed"
                    },
                    new ParadoxOption
                    {
                        Id = "sacrifice_self",
                        MoralType = MoralType.Virtue,
                        ChivalryChange = 500,
                        DarknessChange = 300,
                        WisdomChange = 5,
                        HasDemonPassenger = true,
                        StoryFlag = "carries_demon"
                    }
                }
            };

            // PARADOX 2: Veloura's Cure - the cost of using the Soulweaver's Loom
            // This paradox triggers AFTER the player finds the Loom on floor 65 (from the save quest).
            // The Loom is already in hand - this is about what price must be paid to power it.
            // The actual save completion happens when the player returns to floor 40.
            paradoxes["velouras_cure"] = new MoralParadox
            {
                Id = "velouras_cure",
                TriggerFloor = 65,
                TriggerChapter = StoryChapter.FirstGod,
                RequiredArtifact = ArtifactType.SoulweaversLoom,
                Choices = new List<ParadoxOption>
                {
                    new ParadoxOption
                    {
                        Id = "sacrifice_lyris",
                        MoralType = MoralType.Utilitarian,
                        ChivalryChange = 0,
                        DarknessChange = 0,
                        WisdomChange = 5,
                        CompanionDeath = "Lyris",
                        StoryFlag = "lyris_sacrificed_for_veloura"
                    },
                    new ParadoxOption
                    {
                        Id = "refuse_sacrifice",
                        MoralType = MoralType.Virtue,
                        ChivalryChange = 100,
                        DarknessChange = 50,
                        WisdomChange = 2,
                        StoryFlag = "refused_lyris_sacrifice"
                    },
                    new ParadoxOption
                    {
                        Id = "offer_own_soul",
                        MoralType = MoralType.Virtue,
                        ChivalryChange = 200,
                        DarknessChange = 0,
                        WisdomChange = 3,
                        RevealsPlayerSecret = true,
                        StoryFlag = "soul_rejected_by_loom"
                    }
                }
            };

            // PARADOX 3: Free Terravok
            paradoxes["free_terravok"] = new MoralParadox
            {
                Id = "free_terravok",
                TriggerFloor = 80,
                TriggerChapter = StoryChapter.GodWar,
                Choices = new List<ParadoxOption>
                {
                    new ParadoxOption
                    {
                        Id = "wake_terravok",
                        MoralType = MoralType.Utilitarian,
                        ChivalryChange = -500,
                        DarknessChange = 500,
                        WisdomChange = 3,
                        MassDeaths = 10000,
                        GodAwakened = OldGodType.Terravok,
                        StoryFlag = "woke_terravok_early"
                    },
                    new ParadoxOption
                    {
                        Id = "let_sleep",
                        MoralType = MoralType.Deontological,
                        ChivalryChange = 100,
                        DarknessChange = 0,
                        WisdomChange = 1,
                        StoryFlag = "let_terravok_sleep"
                    },
                    new ParadoxOption
                    {
                        Id = "partial_wake",
                        MoralType = MoralType.Virtue,
                        ChivalryChange = 50,
                        DarknessChange = 0,
                        WisdomChange = 5,
                        StoryFlag = "spoke_to_terravok",
                        OceanPhilosophyBonus = true
                    }
                }
            };

            // PARADOX 4: Destroy Darkness
            paradoxes["destroy_darkness"] = new MoralParadox
            {
                Id = "destroy_darkness",
                TriggerFloor = 95,
                TriggerChapter = StoryChapter.Ascension,
                RequiredArtifact = ArtifactType.SunforgedBlade,
                Choices = new List<ParadoxOption>
                {
                    new ParadoxOption
                    {
                        Id = "purge_darkness",
                        MoralType = MoralType.Utilitarian,
                        ChivalryChange = 0,
                        DarknessChange = -10000, // Removes all darkness
                        WisdomChange = -10, // Wisdom requires understanding darkness
                        EndsWorld = true,
                        StoryFlag = "created_paradise"
                    },
                    new ParadoxOption
                    {
                        Id = "refuse_paradise",
                        MoralType = MoralType.Virtue,
                        ChivalryChange = 0,
                        DarknessChange = 0,
                        WisdomChange = 10,
                        StoryFlag = "refused_paradise",
                        OceanPhilosophyBonus = true
                    },
                    new ParadoxOption
                    {
                        Id = "take_darkness",
                        MoralType = MoralType.Virtue,
                        ChivalryChange = 1000, // Maxes out chivalry — "transmuted the world's pain into wisdom"
                        DarknessChange = 1000, // v0.57.12: was 5000 (stale against pre-cap 30000 constant). Max darkness is now 1000 — intent preserved (absorb the world's darkness until you max out).
                        WisdomChange = 20,
                        StoryFlag = "absorbed_world_darkness",
                        OceanPhilosophyBonus = true
                    }
                }
            };

            // PARADOX 5: The Final Choice (Player IS the problem)
            paradoxes["final_choice"] = new MoralParadox
            {
                Id = "final_choice",
                TriggerFloor = 100,
                TriggerChapter = StoryChapter.FinalConfrontation,
                Choices = new List<ParadoxOption>
                {
                    new ParadoxOption
                    {
                        Id = "claim_power",
                        MoralType = MoralType.Nihilist, // Ironically
                        ChivalryChange = 0,
                        DarknessChange = 1000,
                        WisdomChange = 0,
                        EndingType = EndingType.Usurper,
                        StoryFlag = "claimed_divine_power"
                    },
                    new ParadoxOption
                    {
                        Id = "refuse_power",
                        MoralType = MoralType.Deontological,
                        ChivalryChange = 500,
                        DarknessChange = -500,
                        WisdomChange = 20,
                        EndingType = EndingType.Defiant,
                        StoryFlag = "refused_divine_power",
                        OceanPhilosophyBonus = true
                    },
                    new ParadoxOption
                    {
                        Id = "remember_truth",
                        MoralType = MoralType.Virtue,
                        RequiresAllSeals = true,
                        ChivalryChange = 0,
                        DarknessChange = 0,
                        WisdomChange = 50,
                        EndingType = EndingType.TrueEnding,
                        StoryFlag = "remembered_truth",
                        OceanPhilosophyBonus = true
                    }
                }
            };

            // v1.2.5: each option knows its paradox, so its shown text is looked up by the two ids
            foreach (var paradox in paradoxes.Values)
                foreach (var option in paradox.Choices)
                    option.ParadoxId = paradox.Id;

            // GD.Print($"[MoralParadox] Initialized {paradoxes.Count} moral paradoxes");
        }

        /// <summary>
        /// v1.2.5: the key of a paradox's shown text (name, setup, reflection), built from its id. Two titles
        /// reuse the dungeon story keys that show the same title before the choice.
        /// </summary>
        internal static string ParadoxKey(string paradoxId, string part) => (paradoxId, part) switch
        {
            ("velouras_cure", "name") => "dungeon.story_soulweaver_price_title",
            ("destroy_darkness", "name") => "dungeon.story_purging_title",
            _ => $"moral.{paradoxId}.{part}"
        };

        /// <summary>v1.2.5: the key of an option's shown text (label, outcome), built from the paradox and option ids.</summary>
        internal static string OptionKey(string paradoxId, string optionId, string part) => $"moral.{paradoxId}.{optionId}.{part}";

        /// <summary>v1.2.5: the rows of a text kept under one key, in the reader's language.</summary>
        internal static string[] Rows(string key) => Loc.Get(key).Split('\n');

        /// <summary>
        /// Present a moral paradox to the player
        /// </summary>
        public async Task<ParadoxChoice?> PresentParadox(string paradoxId, Character player, TerminalEmulator terminal)
        {
            if (!paradoxes.TryGetValue(paradoxId, out var paradox))
            {
                // GD.Print($"[MoralParadox] Paradox not found: {paradoxId}");
                return null;
            }

            // Check if already resolved
            if (madeChoices.ContainsKey(paradoxId))
            {
                terminal.WriteLine(Loc.Get("moral.already_chosen"), "yellow");
                return madeChoices[paradoxId];
            }

            // Display setup
            await DisplayParadoxSetup(paradox, terminal);

            // Get player choice
            var choice = await GetPlayerChoice(paradox, player, terminal);
            if (choice == null) return null;

            // Display outcome
            await DisplayOutcome(choice, terminal);

            // Apply effects
            ApplyChoiceEffects(choice, player);

            // Display Ocean Philosophy reflection
            await DisplayPhilosophyReflection(paradox, terminal);

            // Record choice
            var paradoxChoice = new ParadoxChoice
            {
                ParadoxId = paradoxId,
                OptionId = choice.Id,
                MoralType = choice.MoralType,
                GameDay = StoryProgressionSystem.Instance.CurrentGameDay
            };
            madeChoices[paradoxId] = paradoxChoice;

            // Track moral patterns
            TrackMoralPattern(choice.MoralType);

            // Trigger event
            OnParadoxResolved?.Invoke(paradoxId, paradoxChoice);

            return paradoxChoice;
        }

        /// <summary>
        /// Display the paradox setup
        /// </summary>
        private async Task DisplayParadoxSetup(MoralParadox paradox, TerminalEmulator terminal)
        {
            terminal.Clear();
            terminal.WriteLine("");
            UIHelper.WriteBoxHeader(terminal, Loc.Get("moral.header_choice"), "bright_yellow", 64);
            terminal.WriteLine("");
            terminal.WriteLine($"  {paradox.Name}", "bright_white");
            terminal.WriteLine("");
            if (!GameConfig.ScreenReaderMode)
                terminal.WriteLine("  ────────────────────────────────────────────", "dark_gray");
            terminal.WriteLine("");

            await Pacing.Wait(500);

            foreach (var line in paradox.Setup)
            {
                if (string.IsNullOrEmpty(line))
                {
                    terminal.WriteLine("");
                }
                else
                {
                    terminal.WriteLine($"  {line}", "white");
                }
                await Pacing.Wait(100);
            }

            terminal.WriteLine("");
            if (!GameConfig.ScreenReaderMode)
                terminal.WriteLine("  ────────────────────────────────────────────", "dark_gray");
            terminal.WriteLine("");

            await Pacing.Wait(500);
        }

        /// <summary>
        /// Get the player's choice
        /// </summary>
        private async Task<ParadoxOption?> GetPlayerChoice(MoralParadox paradox, Character player, TerminalEmulator terminal)
        {
            var availableChoices = new List<ParadoxOption>();
            int optionNum = 1;

            foreach (var option in paradox.Choices)
            {
                // Check requirements
                if (option.RequiresAllSeals)
                {
                    var seals = StoryProgressionSystem.Instance.CollectedSeals;
                    if (seals.Count < 7)
                    {
                        continue; // Skip unavailable option
                    }
                }

                availableChoices.Add(option);
                terminal.WriteLine($"  [{optionNum}] {option.Label}", "cyan");
                optionNum++;
            }

            terminal.WriteLine("");
            terminal.WriteLine($"  {Loc.Get("moral.no_going_back")}", "dark_red");
            terminal.WriteLine("");

            while (true)
            {
                var input = await terminal.GetInputAsync($"  {Loc.Get("ui.your_choice")}");
                if (int.TryParse(input, out int choice) && choice >= 1 && choice <= availableChoices.Count)
                {
                    return availableChoices[choice - 1];
                }
                terminal.WriteLine($"  {Loc.Get("moral.enter_valid_choice")}", "yellow");
            }
        }

        /// <summary>
        /// Display the outcome
        /// </summary>
        private async Task DisplayOutcome(ParadoxOption choice, TerminalEmulator terminal)
        {
            terminal.Clear();
            terminal.WriteLine("");
            UIHelper.WriteBoxHeader(terminal, Loc.Get("moral.header_consequences"), "dark_cyan", 64);
            terminal.WriteLine("");

            await Pacing.Wait(1000);

            foreach (var line in choice.Outcome)
            {
                if (string.IsNullOrEmpty(line))
                {
                    terminal.WriteLine("");
                }
                else
                {
                    terminal.WriteLine($"  {line}", "white");
                }
                await Pacing.Wait(200);
            }

            terminal.WriteLine("");
            await terminal.PressAnyKey();
        }

        /// <summary>
        /// Apply the effects of the choice
        /// </summary>
        internal void ApplyChoiceEffects(ParadoxOption choice, Character player)
        {
            // Alignment changes
            if (choice.ChivalryChange != 0)
            {
                player.Chivalry += choice.ChivalryChange;
            }
            if (choice.DarknessChange != 0)
            {
                player.Darkness += choice.DarknessChange;
            }

            // Wisdom changes
            if (choice.WisdomChange != 0)
            {
                player.GrantPermanentStat(StatKind.Wisdom, choice.WisdomChange); // 1.2.0: lasting, written to Base; a loss is floored at 1
            }

            // Set story flag
            if (!string.IsNullOrEmpty(choice.StoryFlag))
            {
                StoryProgressionSystem.Instance.SetStoryFlag(choice.StoryFlag, true);
            }

            // Handle special effects
            if (choice.HasDemonPassenger)
            {
                StoryProgressionSystem.Instance.SetStoryFlag("has_demon_passenger", true);
            }

            if (!string.IsNullOrEmpty(choice.CompanionDeath))
            {
                CompanionSystem.Instance.TriggerCompanionDeathByParadox(choice.CompanionDeath);
            }

            if (choice.GodSaved != null)
            {
                StoryProgressionSystem.Instance.UpdateGodState(choice.GodSaved.Value, GodStatus.Saved);
            }

            if (choice.GodAwakened != null)
            {
                StoryProgressionSystem.Instance.UpdateGodState(choice.GodAwakened.Value, GodStatus.Awakened);
            }

            // v1.1.12: the awakening moments these choices are
            var moment = MomentForChoice(choice.Id);
            if (moment.HasValue)
                OceanPhilosophySystem.Instance.ExperienceMoment(moment.Value);

            if (choice.OceanPhilosophyBonus)
            {
                OceanPhilosophySystem.Instance.GainInsight("paradox:" + choice.Id); // v1.1.12
            }

            if (choice.RevealsPlayerSecret)
            {
                StoryProgressionSystem.Instance.SetStoryFlag("loom_rejected_soul", true);
                AmnesiaSystem.Instance.RevealMajorMemory("fragment_of_manwe");
            }

            if (choice.EndingType != null)
            {
                StoryProgressionSystem.Instance.SetStoryFlag($"ending_type_{choice.EndingType}", true);
            }

            // GD.Print($"[MoralParadox] Applied effects for choice: {choice.Id}");
        }

        /// <summary>v1.1.12: the awakening moment a paradox choice records, if any.</summary>
        internal static AwakeningMoment? MomentForChoice(string optionId) => optionId switch
        {
            "refuse_paradise" => AwakeningMoment.RejectedParadise,
            "take_darkness" => AwakeningMoment.AbsorbedDarkness,
            "refuse_power" => AwakeningMoment.LetGoOfPower, // final_choice is never presented; Manwe's Offer also records it
            _ => null
        };

        /// <summary>
        /// Display Ocean Philosophy reflection
        /// </summary>
        private async Task DisplayPhilosophyReflection(MoralParadox paradox, TerminalEmulator terminal)
        {
            if (OceanPhilosophySystem.Instance.AwakeningLevel < 3) return;

            terminal.WriteLine("");
            if (!GameConfig.ScreenReaderMode)
                terminal.WriteLine("  ═══════════════════════════════════════════", "dark_cyan");
            terminal.WriteLine("");
            terminal.WriteLine($"  {Loc.Get("moral.deeper_understanding")}", "cyan");
            terminal.WriteLine("");

            foreach (var line in paradox.OceanPhilosophyReflection)
            {
                terminal.WriteLine($"  {line}", "bright_cyan");
                await Pacing.Wait(400);
            }

            terminal.WriteLine("");
            await terminal.PressAnyKey();
        }

        /// <summary>
        /// Track moral patterns
        /// </summary>
        private void TrackMoralPattern(MoralType type)
        {
            switch (type)
            {
                case MoralType.Utilitarian:
                    UtilitarianChoices++;
                    break;
                case MoralType.Deontological:
                    DeontologicalChoices++;
                    break;
                case MoralType.Virtue:
                    VirtueChoices++;
                    break;
                case MoralType.Nihilist:
                    NihilistChoices++;
                    break;
            }
        }

        /// <summary>
        /// Get the player's dominant moral framework
        /// </summary>
        public MoralType GetDominantMoralType()
        {
            int max = Math.Max(Math.Max(UtilitarianChoices, DeontologicalChoices),
                              Math.Max(VirtueChoices, NihilistChoices));

            if (max == 0) return MoralType.Virtue;

            if (UtilitarianChoices == max) return MoralType.Utilitarian;
            if (DeontologicalChoices == max) return MoralType.Deontological;
            if (VirtueChoices == max) return MoralType.Virtue;
            return MoralType.Nihilist;
        }

        /// <summary>
        /// Check if a paradox is available at the current game state
        /// </summary>
        public bool IsParadoxAvailable(string paradoxId, Character player)
        {
            if (!paradoxes.TryGetValue(paradoxId, out var paradox))
                return false;

            if (madeChoices.ContainsKey(paradoxId))
                return false;

            var story = StoryProgressionSystem.Instance;

            // Check floor requirement
            // (Would need current floor from dungeon context)

            // Check chapter requirement
            if (story.CurrentChapter < paradox.TriggerChapter)
                return false;

            // Check artifact requirement
            if (paradox.RequiredArtifact != null &&
                !story.CollectedArtifacts.Contains(paradox.RequiredArtifact.Value))
                return false;

            return true;
        }

        /// <summary>
        /// Check if a choice has been made for a paradox
        /// </summary>
        public bool HasMadeChoice(string paradoxId)
        {
            return madeChoices.ContainsKey(paradoxId);
        }

        /// <summary>
        /// Get the choice made for a paradox
        /// </summary>
        public ParadoxChoice? GetChoice(string paradoxId)
        {
            return madeChoices.TryGetValue(paradoxId, out var choice) ? choice : null;
        }

        /// <summary>
        /// Save state for serialization
        /// </summary>
        public Dictionary<string, object> SaveState()
        {
            return new Dictionary<string, object>
            {
                ["MadeChoices"] = madeChoices.ToDictionary(
                    k => k.Key,
                    v => new Dictionary<string, object>
                    {
                        ["OptionId"] = v.Value.OptionId,
                        ["MoralType"] = (int)v.Value.MoralType,
                        ["GameDay"] = v.Value.GameDay
                    }
                ),
                ["UtilitarianChoices"] = UtilitarianChoices,
                ["DeontologicalChoices"] = DeontologicalChoices,
                ["VirtueChoices"] = VirtueChoices,
                ["NihilistChoices"] = NihilistChoices
            };
        }
    }

    #region Moral Paradox Data Classes

    public enum MoralType
    {
        Utilitarian,    // Greatest good for greatest number
        Deontological,  // Rules and principles matter regardless of outcome
        Virtue,         // Character and compassion guide action
        Nihilist        // Rejection of imposed moral frameworks
    }

    public class MoralParadox
    {
        public string Id { get; set; } = "";
        /// <summary>v1.2.5: shown in the reader's language, from the key built from Id.</summary>
        public string Name => Loc.Get(MoralParadoxSystem.ParadoxKey(Id, "name"));
        public int TriggerFloor { get; set; }
        public StoryChapter TriggerChapter { get; set; }
        public ArtifactType? RequiredArtifact { get; set; }
        public string[] Setup => MoralParadoxSystem.Rows(MoralParadoxSystem.ParadoxKey(Id, "setup"));
        public List<ParadoxOption> Choices { get; set; } = new();
        public string[] OceanPhilosophyReflection => MoralParadoxSystem.Rows(MoralParadoxSystem.ParadoxKey(Id, "reflection"));
    }

    public class ParadoxOption
    {
        public string Id { get; set; } = "";
        /// <summary>v1.2.5: the paradox this option belongs to (stamped at start); with Id it keys the shown text.</summary>
        public string ParadoxId { get; set; } = "";
        public string Label => Loc.Get(MoralParadoxSystem.OptionKey(ParadoxId, Id, "label"));
        public MoralType MoralType { get; set; }
        public string[] Outcome => MoralParadoxSystem.Rows(MoralParadoxSystem.OptionKey(ParadoxId, Id, "outcome"));
        public long ChivalryChange { get; set; }
        public long DarknessChange { get; set; }
        public int WisdomChange { get; set; }
        public string? StoryFlag { get; set; }

        // Special effects
        public bool RequiresAllSeals { get; set; }
        public bool HasDemonPassenger { get; set; }
        public string? CompanionDeath { get; set; }
        public OldGodType? GodSaved { get; set; }
        public OldGodType? GodNotSaved { get; set; }
        public OldGodType? GodAwakened { get; set; }
        public int VillageDeaths { get; set; }
        public int MassDeaths { get; set; }
        public bool EndsWorld { get; set; }
        public bool RevealsPlayerSecret { get; set; }
        public bool OceanPhilosophyBonus { get; set; }
        public EndingType? EndingType { get; set; }
    }

    public class ParadoxChoice
    {
        public string ParadoxId { get; set; } = "";
        public string OptionId { get; set; } = "";
        public MoralType MoralType { get; set; }
        public int GameDay { get; set; }
    }

    #endregion
}
