using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Godot;
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
        private static MoralParadoxSystem? instance;
        public static MoralParadoxSystem Instance => instance ??= new MoralParadoxSystem();

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
                Name = "The Innocent Vessel",
                TriggerFloor = 25,
                TriggerChapter = StoryChapter.RisingPower,
                Setup = new[]
                {
                    "A village has been placed under quarantine.",
                    "A demon has possessed a young child - no more than seven years old.",
                    "The demon grows stronger each day, feeding on the villagers' fear.",
                    "",
                    "The priest tells you: 'The child is gone. Only the demon remains.'",
                    "The mother weeps: 'Please... my baby is still in there. I can feel her.'",
                    "",
                    "If the demon is not stopped, it will consume the village within three days.",
                    "There is no known exorcism that works. The only certain method is death.",
                    "",
                    "One hundred innocent lives hang in the balance.",
                    "One innocent life holds the demon.",
                    "",
                    "What do you do?"
                },
                Choices = new List<ParadoxOption>
                {
                    new ParadoxOption
                    {
                        Id = "kill_child",
                        Label = "End the child's life to save the village",
                        MoralType = MoralType.Utilitarian,
                        Outcome = new[]
                        {
                            "Your blade is swift. Merciful, perhaps.",
                            "The demon's scream echoes as it is banished.",
                            "The mother's wail echoes longer.",
                            "",
                            "The village is saved. One hundred lives continue.",
                            "But the mother never speaks again.",
                            "And you... you remember the child's eyes.",
                            "In that last moment, they were human."
                        },
                        ChivalryChange = -100,
                        DarknessChange = 200,
                        WisdomChange = 2,
                        StoryFlag = "killed_possessed_child"
                    },
                    new ParadoxOption
                    {
                        Id = "spare_child",
                        Label = "Refuse to kill the child - there must be another way",
                        MoralType = MoralType.Deontological,
                        Outcome = new[]
                        {
                            "You cannot. You will not. There must be another way.",
                            "You search desperately for alternatives.",
                            "Three days pass.",
                            "",
                            "The demon consumes the village.",
                            "One hundred souls are devoured.",
                            "The child's body, a husk, laughs with a voice from the abyss.",
                            "",
                            "Your principles remain intact.",
                            "The graves are full."
                        },
                        ChivalryChange = 100,
                        DarknessChange = 0,
                        WisdomChange = 0,
                        VillageDeaths = 100,
                        StoryFlag = "village_consumed"
                    },
                    new ParadoxOption
                    {
                        Id = "sacrifice_self",
                        Label = "Offer yourself as a vessel instead",
                        MoralType = MoralType.Virtue,
                        Outcome = new[]
                        {
                            "You kneel before the possessed child.",
                            "'Take me instead,' you whisper. 'A stronger vessel.'",
                            "",
                            "The demon considers... and accepts.",
                            "You feel it enter you like ice through your veins.",
                            "",
                            "But you are not a child. You have trained. You have will.",
                            "For now, you contain it. But it grows.",
                            "How long can you hold?",
                            "",
                            "The child is free. The village is saved.",
                            "And you carry a passenger who whispers in the dark."
                        },
                        ChivalryChange = 500,
                        DarknessChange = 300,
                        WisdomChange = 5,
                        HasDemonPassenger = true,
                        StoryFlag = "carries_demon"
                    }
                },
                OceanPhilosophyReflection = new[]
                {
                    "The wave that saves must destroy.",
                    "The wave that destroys believes it saves.",
                    "The ocean contains all outcomes.",
                    "Which wave are you?"
                }
            };

            // PARADOX 2: Veloura's Cure - the cost of using the Soulweaver's Loom
            // This paradox triggers AFTER the player finds the Loom on floor 65 (from the save quest).
            // The Loom is already in hand - this is about what price must be paid to power it.
            // The actual save completion happens when the player returns to floor 40.
            paradoxes["velouras_cure"] = new MoralParadox
            {
                Id = "velouras_cure",
                Name = "The Soulweaver's Price",
                TriggerFloor = 65,
                TriggerChapter = StoryChapter.FirstGod,
                RequiredArtifact = ArtifactType.SoulweaversLoom,
                Setup = new[]
                {
                    "The Soulweaver's Loom hums with potential, but it needs",
                    "a soul to truly activate - a soul willingly given.",
                    "",
                    "Lyris steps forward. 'I have a confession.'",
                    "'I am... a fragment of Veloura herself.'",
                    "'Sent to earth to find one who could save her.'",
                    "'My soul can power the Loom. It always could.'",
                    "",
                    "She looks at you with eyes full of love and sorrow.",
                    "'Let me do this. Let my death have meaning.'",
                    "",
                    "A goddess can be saved.",
                    "But the cost may be the woman you love."
                },
                Choices = new List<ParadoxOption>
                {
                    new ParadoxOption
                    {
                        Id = "sacrifice_lyris",
                        Label = "Accept Lyris's sacrifice - empower the Loom",
                        MoralType = MoralType.Utilitarian,
                        Outcome = new[]
                        {
                            "Lyris smiles through tears. 'Thank you for letting me choose.'",
                            "Her form shimmers as the Loom activates.",
                            "You feel her hand in yours, then... nothing.",
                            "",
                            "The Loom blazes with light. Its threads glow gold.",
                            "You can feel Veloura's curse within it, ready to be unwoven.",
                            "",
                            "Return to Veloura on floor 40 to complete the cure."
                        },
                        ChivalryChange = 0,
                        DarknessChange = 0,
                        WisdomChange = 5,
                        CompanionDeath = "Lyris",
                        StoryFlag = "lyris_sacrificed_for_veloura"
                    },
                    new ParadoxOption
                    {
                        Id = "refuse_sacrifice",
                        Label = "Refuse - Lyris's life matters more than a goddess",
                        MoralType = MoralType.Virtue,
                        Outcome = new[]
                        {
                            "'No,' you say firmly. 'I won't let you die.'",
                            "Lyris's eyes fill with tears. 'But Veloura...'",
                            "",
                            "'Is already dying. Has been dying for millennia.'",
                            "'I will not trade a certain love for a distant goddess.'",
                            "",
                            "The Loom dims but does not go dark.",
                            "Perhaps... perhaps love itself can power it.",
                            "But Lyris remains at your side.",
                            "",
                            "Return to Veloura on floor 40 to attempt the cure."
                        },
                        ChivalryChange = 100,
                        DarknessChange = 50,
                        WisdomChange = 2,
                        StoryFlag = "refused_lyris_sacrifice"
                    },
                    new ParadoxOption
                    {
                        Id = "offer_own_soul",
                        Label = "Offer your own soul instead",
                        MoralType = MoralType.Virtue,
                        Outcome = new[]
                        {
                            "'Take mine,' you say. 'I love her. That makes it valid.'",
                            "",
                            "The Loom pulses. It considers.",
                            "",
                            "'No,' it resonates. 'Your soul is... fractured.'",
                            "'Pieces are missing. Pieces you do not remember.'",
                            "'You are not whole enough to pay this price.'",
                            "",
                            "The Loom dims, then steadies. Your attempt moved something.",
                            "Lyris looks at you with newfound wonder.",
                            "'What ARE you?'",
                            "",
                            "Return to Veloura on floor 40 to attempt the cure."
                        },
                        ChivalryChange = 200,
                        DarknessChange = 0,
                        WisdomChange = 3,
                        RevealsPlayerSecret = true,
                        StoryFlag = "soul_rejected_by_loom"
                    }
                },
                OceanPhilosophyReflection = new[]
                {
                    "A wave dies to become the shore.",
                    "A shore dies to become the wave.",
                    "Love is the current between them.",
                    "What are you willing to become?"
                }
            };

            // PARADOX 3: Free Terravok
            paradoxes["free_terravok"] = new MoralParadox
            {
                Id = "free_terravok",
                Name = "The Sleeping Mountain",
                TriggerFloor = 80,
                TriggerChapter = StoryChapter.GodWar,
                Setup = new[]
                {
                    "Terravok, the god of earth, slumbers beneath the dungeon.",
                    "His prison weakens. Soon he will wake regardless.",
                    "",
                    "But you could wake him NOW.",
                    "",
                    "Awake, he could end the God War.",
                    "His strength is unmatched. His wisdom ancient.",
                    "He was the only god Manwe truly trusted.",
                    "",
                    "But his waking will shake the world.",
                    "Mountains will rise. Valleys will open.",
                    "Thousands will die in the upheaval.",
                    "",
                    "To stop a war that kills slowly...",
                    "You could trigger a cataclysm that kills quickly.",
                    "",
                    "Is a swift death kinder than a slow one?",
                    "Is certainty better than hope?"
                },
                Choices = new List<ParadoxOption>
                {
                    new ParadoxOption
                    {
                        Id = "wake_terravok",
                        Label = "Wake Terravok - end the war at any cost",
                        MoralType = MoralType.Utilitarian,
                        Outcome = new[]
                        {
                            "You break the final seal.",
                            "The ground splits. The sky darkens.",
                            "",
                            "TERRAVOK RISES.",
                            "",
                            "Cities crumble. Rivers change course.",
                            "The death toll is... immense.",
                            "",
                            "But the god of earth looks upon the warring deities",
                            "and speaks with a voice like grinding stone:",
                            "",
                            "'ENOUGH.'",
                            "",
                            "The God War ends in a single word.",
                            "And you are left to count the cost."
                        },
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
                        Label = "Let him sleep - find another way to end the war",
                        MoralType = MoralType.Deontological,
                        Outcome = new[]
                        {
                            "You cannot trade lives so callously.",
                            "There must be another way.",
                            "",
                            "You leave Terravok to his dreams.",
                            "",
                            "The God War continues.",
                            "More die each day - slowly, in skirmishes.",
                            "Perhaps more will die than would have in the cataclysm.",
                            "Perhaps not.",
                            "",
                            "You will never know which path cost more.",
                            "That uncertainty is its own weight."
                        },
                        ChivalryChange = 100,
                        DarknessChange = 0,
                        WisdomChange = 1,
                        StoryFlag = "let_terravok_sleep"
                    },
                    new ParadoxOption
                    {
                        Id = "partial_wake",
                        Label = "Speak to him without fully waking - seek counsel",
                        MoralType = MoralType.Virtue,
                        Outcome = new[]
                        {
                            "You press your hand to the seal.",
                            "Not breaking it. Speaking through it.",
                            "",
                            "'Terravok,' you whisper. 'I need guidance.'",
                            "",
                            "An eternity passes. Then...",
                            "",
                            "'YOUNG WAVE. I HEAR YOU.'",
                            "'THE OCEAN STIRS IN YOUR SOUL.'",
                            "'WHEN I WAKE, FIND ME. SPEAK YOUR TRUTH.'",
                            "'BUT DO NOT BREAK WHAT IS NOT YET READY TO BREAK.'",
                            "",
                            "The presence recedes.",
                            "You have an ally, when the time comes."
                        },
                        ChivalryChange = 50,
                        DarknessChange = 0,
                        WisdomChange = 5,
                        StoryFlag = "spoke_to_terravok",
                        OceanPhilosophyBonus = true
                    }
                },
                OceanPhilosophyReflection = new[]
                {
                    "The mountain is the ocean, crystallized into patience.",
                    "To wake it is to release what was always there.",
                    "Destruction and creation are the same wave.",
                    "The timing is all that matters."
                }
            };

            // PARADOX 4: Destroy Darkness
            paradoxes["destroy_darkness"] = new MoralParadox
            {
                Id = "destroy_darkness",
                Name = "The Purging Light",
                TriggerFloor = 95,
                TriggerChapter = StoryChapter.Ascension,
                RequiredArtifact = ArtifactType.SunforgedBlade,
                Setup = new[]
                {
                    "Aurelion offers you a gift beyond measure:",
                    "The power to purge ALL darkness from the realm.",
                    "",
                    "No more evil. No more suffering caused by malice.",
                    "Every dark thought, every cruel impulse - gone.",
                    "",
                    "Paradise, given freely.",
                    "",
                    "But Noctura appears, her voice unusually earnest:",
                    "'Consider carefully. Darkness is not only evil.'",
                    "'It is the capacity for hard choices.'",
                    "'It is the strength to sacrifice for what matters.'",
                    "'Without darkness, there can be no courage.'",
                    "'For courage requires fear to overcome.'",
                    "",
                    "A world without darkness...",
                    "Is a world without growth.",
                    "Without meaning.",
                    "Without free will."
                },
                Choices = new List<ParadoxOption>
                {
                    new ParadoxOption
                    {
                        Id = "purge_darkness",
                        Label = "Accept the gift - create paradise",
                        MoralType = MoralType.Utilitarian,
                        Outcome = new[]
                        {
                            "Light spreads across the world.",
                            "Every shadow flees. Every cruel thought dissolves.",
                            "",
                            "For a moment, there is perfect peace.",
                            "",
                            "Then you realize... you feel nothing.",
                            "Not joy. Not satisfaction. Not pride.",
                            "Those require contrast. Light needs dark to shine.",
                            "",
                            "The world is peaceful.",
                            "The world is empty.",
                            "The world is... done.",
                            "",
                            "There are no more stories to tell."
                        },
                        ChivalryChange = 0,
                        DarknessChange = -10000, // Removes all darkness
                        WisdomChange = -10, // Wisdom requires understanding darkness
                        EndsWorld = true,
                        StoryFlag = "created_paradise"
                    },
                    new ParadoxOption
                    {
                        Id = "refuse_paradise",
                        Label = "Refuse - balance requires both light and shadow",
                        MoralType = MoralType.Virtue,
                        Outcome = new[]
                        {
                            "You decline the gift.",
                            "",
                            "Aurelion's light dims with disappointment.",
                            "But Noctura's shadow deepens with respect.",
                            "",
                            "'You understand,' she says. 'Better than most.'",
                            "'The dance requires both partners.'",
                            "'Light and dark, forever entwined.'",
                            "",
                            "The world remains imperfect.",
                            "But it remains ALIVE.",
                            "With all its pain. All its joy.",
                            "All its potential for growth."
                        },
                        ChivalryChange = 0,
                        DarknessChange = 0,
                        WisdomChange = 10,
                        StoryFlag = "refused_paradise",
                        OceanPhilosophyBonus = true
                    },
                    new ParadoxOption
                    {
                        Id = "take_darkness",
                        Label = "Take the realm's darkness into yourself",
                        MoralType = MoralType.Virtue,
                        Outcome = new[]
                        {
                            "'Give me the darkness,' you say. 'I will carry it.'",
                            "",
                            "Both gods stare in disbelief.",
                            "",
                            "'No mortal could survive-' Aurelion begins.",
                            "",
                            "But you are not mortal. Not truly.",
                            "You are a fragment of something greater.",
                            "",
                            "The darkness flows into you.",
                            "Every cruel thought. Every hateful impulse.",
                            "You contain it. You transmute it.",
                            "Pain becoming wisdom. Hate becoming understanding.",
                            "",
                            "The world is lighter.",
                            "And you carry its shadow."
                        },
                        ChivalryChange = 1000,
                        DarknessChange = 5000, // Take on the darkness
                        WisdomChange = 20,
                        StoryFlag = "absorbed_world_darkness",
                        OceanPhilosophyBonus = true
                    }
                },
                OceanPhilosophyReflection = new[]
                {
                    "The ocean has depths and surfaces.",
                    "To remove either is to destroy the whole.",
                    "Darkness is not your enemy.",
                    "It is the weight that teaches the wave to rise."
                }
            };

            // PARADOX 5: The Final Choice (Player IS the problem)
            paradoxes["final_choice"] = new MoralParadox
            {
                Id = "final_choice",
                Name = "The Endless Cycle",
                TriggerFloor = 100,
                TriggerChapter = StoryChapter.FinalConfrontation,
                Setup = new[]
                {
                    "Manwe speaks, and his voice is weary beyond measure:",
                    "",
                    "'You stand where so many have stood.'",
                    "'Ready to become a god. Ready to 'win.''",
                    "'But have you understood anything?'",
                    "",
                    "'The cycle continues because of GRASPING.'",
                    "'The desire to accumulate. To conquer. To become MORE.'",
                    "'Every hero who reaches me seeks power.'",
                    "'And in seeking, they perpetuate the very cycle they claim to fight.'",
                    "",
                    "'You have killed. You have conquered. You have WANTED.'",
                    "'Do you truly believe you are different?'",
                    "'That YOUR ascension will end the suffering?'",
                    "",
                    "'Or are you just another wave, crashing against the shore,'",
                    "'believing itself the ocean?'"
                },
                Choices = new List<ParadoxOption>
                {
                    new ParadoxOption
                    {
                        Id = "claim_power",
                        Label = "Claim divine power - you WILL be different",
                        MoralType = MoralType.Nihilist, // Ironically
                        Outcome = new[]
                        {
                            "You seize the power. It floods through you.",
                            "You are a god now. Everything you ever wanted.",
                            "",
                            "Manwe sighs. 'As I expected.'",
                            "",
                            "'In ten thousand years, another will stand here.'",
                            "'Ready to take YOUR power.'",
                            "'And the cycle continues.'",
                            "",
                            "You ARE different. Every god was different.",
                            "And yet... the cycle remains.",
                            "",
                            "You have won everything.",
                            "And changed nothing."
                        },
                        ChivalryChange = 0,
                        DarknessChange = 1000,
                        WisdomChange = 0,
                        EndingType = EndingType.Usurper,
                        StoryFlag = "claimed_divine_power"
                    },
                    new ParadoxOption
                    {
                        Id = "refuse_power",
                        Label = "Refuse power - break the cycle by not playing",
                        MoralType = MoralType.Deontological,
                        Outcome = new[]
                        {
                            "'No,' you say. 'I will not play this game.'",
                            "",
                            "Manwe's eyes widen. 'You... refuse?'",
                            "",
                            "'The cycle is fed by desire. By grasping.'",
                            "'I release my grip. I want nothing from you.'",
                            "'I am content to be mortal. To live. To die.'",
                            "'As the wave returns to the ocean.'",
                            "",
                            "For the first time in millennia, Manwe smiles.",
                            "",
                            "'Perhaps... perhaps there is hope after all.'"
                        },
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
                        Label = "[REQUIRES ALL SEALS] 'I remember who I am.'",
                        MoralType = MoralType.Virtue,
                        RequiresAllSeals = true,
                        Outcome = new[]
                        {
                            "'I am not here to take power, Father.'",
                            "",
                            "Manwe freezes. 'What did you call me?'",
                            "",
                            "'You sent me. A fragment of yourself.'",
                            "'To experience mortality. To understand suffering.'",
                            "'To remember what you forgot in your loneliness.'",
                            "",
                            "'I am the wave that remembers it is the ocean.'",
                            "",
                            "Tears stream down the Creator's face.",
                            "",
                            "'You... you came back. After all this time.'",
                            "'You came back to me.'"
                        },
                        ChivalryChange = 0,
                        DarknessChange = 0,
                        WisdomChange = 50,
                        EndingType = EndingType.TrueEnding,
                        StoryFlag = "remembered_truth",
                        OceanPhilosophyBonus = true
                    }
                },
                OceanPhilosophyReflection = new[]
                {
                    "The wave that stops grasping returns to the ocean.",
                    "The wave that keeps grasping becomes foam.",
                    "Which will you be?",
                    "The choice was always yours."
                }
            };

            // GD.Print($"[MoralParadox] Initialized {paradoxes.Count} moral paradoxes");
        }

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
                terminal.WriteLine("You have already made this choice.", "yellow");
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
            if (paradox.OceanPhilosophyReflection != null)
            {
                await DisplayPhilosophyReflection(paradox, terminal);
            }

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
            terminal.WriteLine("╔════════════════════════════════════════════════════════════════╗", "bright_yellow");
            terminal.WriteLine("║              A   C H O I C E   A W A I T S                     ║", "bright_yellow");
            terminal.WriteLine("╚════════════════════════════════════════════════════════════════╝", "bright_yellow");
            terminal.WriteLine("");
            terminal.WriteLine($"  {paradox.Name}", "bright_white");
            terminal.WriteLine("");
            terminal.WriteLine("  ────────────────────────────────────────────", "dark_gray");
            terminal.WriteLine("");

            await Task.Delay(500);

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
                await Task.Delay(100);
            }

            terminal.WriteLine("");
            terminal.WriteLine("  ────────────────────────────────────────────", "dark_gray");
            terminal.WriteLine("");

            await Task.Delay(500);
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
            terminal.WriteLine("  There is no going back.", "dark_red");
            terminal.WriteLine("");

            while (true)
            {
                var input = await terminal.GetInputAsync("  Your choice: ");
                if (int.TryParse(input, out int choice) && choice >= 1 && choice <= availableChoices.Count)
                {
                    return availableChoices[choice - 1];
                }
                terminal.WriteLine("  Please enter a valid choice.", "yellow");
            }
        }

        /// <summary>
        /// Display the outcome
        /// </summary>
        private async Task DisplayOutcome(ParadoxOption choice, TerminalEmulator terminal)
        {
            terminal.Clear();
            terminal.WriteLine("");
            terminal.WriteLine("╔════════════════════════════════════════════════════════════════╗", "dark_cyan");
            terminal.WriteLine("║                  C O N S E Q U E N C E S                       ║", "dark_cyan");
            terminal.WriteLine("╚════════════════════════════════════════════════════════════════╝", "dark_cyan");
            terminal.WriteLine("");

            await Task.Delay(1000);

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
                await Task.Delay(200);
            }

            terminal.WriteLine("");
            await terminal.GetInputAsync("  Press Enter to continue...");
        }

        /// <summary>
        /// Apply the effects of the choice
        /// </summary>
        private void ApplyChoiceEffects(ParadoxOption choice, Character player)
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
                player.Wisdom += choice.WisdomChange;
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

            if (choice.OceanPhilosophyBonus)
            {
                OceanPhilosophySystem.Instance.GainInsight(20);
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

        /// <summary>
        /// Display Ocean Philosophy reflection
        /// </summary>
        private async Task DisplayPhilosophyReflection(MoralParadox paradox, TerminalEmulator terminal)
        {
            if (OceanPhilosophySystem.Instance.AwakeningLevel < 3) return;

            terminal.WriteLine("");
            terminal.WriteLine("  ═══════════════════════════════════════════", "dark_cyan");
            terminal.WriteLine("");
            terminal.WriteLine("  [A deeper understanding stirs...]", "cyan");
            terminal.WriteLine("");

            foreach (var line in paradox.OceanPhilosophyReflection!)
            {
                terminal.WriteLine($"  {line}", "bright_cyan");
                await Task.Delay(400);
            }

            terminal.WriteLine("");
            await terminal.GetInputAsync("  Press Enter to continue...");
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
        /// Get summary of moral choices for endings
        /// </summary>
        public string GetMoralSummary()
        {
            var dominant = GetDominantMoralType();
            return dominant switch
            {
                MoralType.Utilitarian => "You chose the greater good, even at terrible cost.",
                MoralType.Deontological => "You held to your principles, even when it hurt.",
                MoralType.Virtue => "You followed your heart, seeking wisdom in compassion.",
                MoralType.Nihilist => "You rejected the false choices, seeing through the illusion.",
                _ => "Your path was your own."
            };
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
        public string Name { get; set; } = "";
        public int TriggerFloor { get; set; }
        public StoryChapter TriggerChapter { get; set; }
        public ArtifactType? RequiredArtifact { get; set; }
        public string[] Setup { get; set; } = Array.Empty<string>();
        public List<ParadoxOption> Choices { get; set; } = new();
        public string[]? OceanPhilosophyReflection { get; set; }
    }

    public class ParadoxOption
    {
        public string Id { get; set; } = "";
        public string Label { get; set; } = "";
        public MoralType MoralType { get; set; }
        public string[] Outcome { get; set; } = Array.Empty<string>();
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
