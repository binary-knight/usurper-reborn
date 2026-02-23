using System;
using System.Collections.Generic;
using UsurperRemake.Systems;

namespace UsurperRemake.Data
{
    /// <summary>
    /// Data definitions for the Seven Old Gods - the main antagonists/story drivers
    /// Each god has unique encounters, dialogue, and boss mechanics
    /// </summary>
    public static class OldGodsData
    {
        /// <summary>
        /// Get all Old God boss data
        /// </summary>
        public static List<OldGodBossData> GetAllOldGods()
        {
            return new List<OldGodBossData>
            {
                GetMaelketh(),
                GetVeloura(),
                GetThorgrim(),
                GetNoctura(),
                GetAurelion(),
                GetTerravok(),
                GetManwe()
            };
        }

        /// <summary>
        /// Get the full definition for an Old God boss encounter
        /// </summary>
        public static OldGodBossData GetGodBossData(OldGodType godType)
        {
            return godType switch
            {
                OldGodType.Maelketh => GetMaelketh(),
                OldGodType.Veloura => GetVeloura(),
                OldGodType.Thorgrim => GetThorgrim(),
                OldGodType.Noctura => GetNoctura(),
                OldGodType.Aurelion => GetAurelion(),
                OldGodType.Terravok => GetTerravok(),
                OldGodType.Manwe => GetManwe(),
                _ => throw new ArgumentException($"Unknown god type: {godType}")
            };
        }

        #region Individual God Definitions

        private static OldGodBossData GetMaelketh()
        {
            return new OldGodBossData
            {
                Type = OldGodType.Maelketh,
                Name = "Maelketh, The Broken Blade",
                Title = "God of War and Conquest",
                Level = 28,           // Slightly above floor 25 - requires preparation
                HP = 15000,           // God of War — a brutal, drawn-out battle
                MaxHP = 15000,
                Strength = 120,       // Hits hard - need good armor
                Defence = 80,
                Agility = 60,
                AttacksPerRound = 2,

                EncounterLocation = "Dungeon Floor 25",
                DungeonFloor = 25,

                Description = "Once the noble god of honorable combat, now a broken shell consumed by endless war.",

                IntroDialogue = new[]
                {
                    "The air grows thick with the stench of ancient blood.",
                    "A towering figure emerges from the shadows, armor rent and blade shattered.",
                    "",
                    "MAELKETH: \"Another challenger. Another corpse for my throne of bones.\"",
                    "",
                    "His eyes burn with an insane fire that has not dimmed in millennia.",
                    "",
                    "MAELKETH: \"Come then, mortal. Let us see if you can make me FEEL something again.\""
                },

                Phase1Threshold = 1.0f,
                Phase2Threshold = 0.5f,
                Phase3Threshold = 0.2f,

                Phase1Abilities = new[] { "Cleave", "War Cry", "Shield Bash" },
                Phase2Abilities = new[] { "Summon Soldiers", "Berserker Rage", "Whirlwind" },
                Phase3Abilities = new[] { "Final Stand", "Blood Sacrifice", "Endless War" },

                Phase2Dialogue = new[]
                {
                    "MAELKETH: \"You wound me. GOOD. Pain is the only thing that still makes sense!\"",
                    "",
                    "He raises his shattered blade, and spectral soldiers rise from the stones."
                },

                Phase3Dialogue = new[]
                {
                    "MAELKETH: \"I... remember now. Before the madness. Before the war.\"",
                    "",
                    "For a moment, clarity flickers in his eyes.",
                    "",
                    "MAELKETH: \"End it. Please. I am so... tired of fighting.\""
                },

                DefeatDialogue = new[]
                {
                    "Maelketh falls to his knees. His armor crumbles to rust around him.",
                    "",
                    "MAELKETH: \"At last... quiet. Thank you, mortal.\"",
                    "",
                    "He breaks apart into crimson light. Some of it gets into you.",
                    "Your hands shake. Your blood runs hot.",
                    "Somewhere deep down, you understand war now. Really understand it."
                },

                ArtifactDropped = ArtifactType.CreatorsEye,

                CanBeSaved = false,
                SaveRequirement = null,
                SaveDialogue = null,

                SpecialMechanics = new Dictionary<string, string>
                {
                    ["SummonSoldiers"] = "Summons 2 soldier minions every 3 rounds",
                    ["BerserkerRage"] = "Deals double damage but takes 25% more damage",
                    ["FinalStand"] = "At 20% HP, gains immunity for 2 turns and heals 10%"
                },

                LoreUnlocked = "The War God's Lament: Before the Sundering, Maelketh championed honor in combat. " +
                              "When the gods fell to corruption, he was the first to break. Now he knows only battle, " +
                              "fighting phantom enemies in an endless war that exists only in his shattered mind."
            };
        }

        private static OldGodBossData GetVeloura()
        {
            return new OldGodBossData
            {
                Type = OldGodType.Veloura,
                Name = "Veloura, The Withered Heart",
                Title = "Goddess of Love and Passion",
                Level = 44,           // Above floor 40 - requires preparation
                HP = 25000,           // Goddess of Love — deceptively resilient
                MaxHP = 25000,
                Strength = 90,
                Defence = 85,
                Agility = 110,
                Charisma = 200,       // High charisma for charm effects
                AttacksPerRound = 2,

                EncounterLocation = "Dungeon Floor 40",
                DungeonFloor = 40,

                Description = "The goddess of love fades with each broken heart. She can still be saved.",

                IntroDialogue = new[]
                {
                    "The room fills with the scent of dying roses.",
                    "A figure of heartbreaking beauty sits upon a throne of wilted flowers.",
                    "",
                    "VELOURA: \"Another heart come to break? Or to be broken?\"",
                    "",
                    "Tears like liquid silver fall from her eyes.",
                    "",
                    "VELOURA: \"It matters not. Love always ends in pain. I have learned this truth\""
                },

                Phase1Threshold = 1.0f,
                Phase2Threshold = 0.5f,
                Phase3Threshold = 0.2f,

                Phase1Abilities = new[] { "Heartbreak", "Charm", "Thorn Embrace" },
                Phase2Abilities = new[] { "Jealous Rage", "Forgotten Lovers", "Passion's Fire" },
                Phase3Abilities = new[] { "Desperate Plea", "Final Kiss", "Love's Sacrifice" },

                Phase2Dialogue = new[]
                {
                    "VELOURA: \"You DARE fight the goddess of love?!\"",
                    "",
                    "Her beauty twists into something terrible.",
                    "",
                    "VELOURA: \"I will show you the pain of a billion broken hearts!\""
                },

                Phase3Dialogue = new[]
                {
                    "Veloura collapses, the rage fading from her eyes.",
                    "",
                    "VELOURA: \"Please... I don't want to hurt anymore.\"",
                    "",
                    "She looks at you with desperate hope.",
                    "",
                    "VELOURA: \"Is there... is there still love in the world? Real love?\""
                },

                DefeatDialogue = new[]
                {
                    "Veloura crumples. Pink mist rises off her like steam.",
                    "",
                    "VELOURA: \"So thats it then... love dies with me...\"",
                    "",
                    "The mist touches you and your chest aches.",
                    "Every heartbreak you ever felt, all at once.",
                    "Then its gone. You feel hollow inside, like something got scooped out."
                },

                ArtifactDropped = ArtifactType.SoulweaversLoom,

                CanBeSaved = true,
                SaveRequirement = "Chivalry >= 5000 AND completed a romance questline",
                SaveDialogue = new[]
                {
                    "> [SAVE] \"Love's not dead. I can prove it.\"",
                    "",
                    "You tell her about the people you care about.",
                    "Not poetry. Just the truth. The messy, imperfect truth.",
                    "",
                    "VELOURA: \"You actually mean it, dont you.\"",
                    "",
                    "She grabs your hand. Hers is freezing cold.",
                    "",
                    "VELOURA: \"Maybe... maybe it doesnt always have to hurt.\"",
                    "",
                    "Color creeps back into her face. She stands up."
                },

                SpecialMechanics = new Dictionary<string, string>
                {
                    ["Charm"] = "Prevents player from attacking for 2 rounds",
                    ["DesperatePlea"] = "At 20% HP, offers surrender—player can accept or refuse",
                    ["LoveSacrifice"] = "If saved, Veloura becomes a permanent ally"
                },

                LoreUnlocked = "The Heart's Lament: Veloura once blessed every union, every passion, every tender moment. " +
                              "But as mortals turned love into jealousy, possession, and cruelty, she began to wither. " +
                              "Now she barely exists, sustained only by the rare genuine love that still flickers in the world."
            };
        }

        private static OldGodBossData GetThorgrim()
        {
            return new OldGodBossData
            {
                Type = OldGodType.Thorgrim,
                Name = "Thorgrim, The Hollow Judge",
                Title = "God of Law and Order",
                Level = 60,           // Above floor 55 - requires preparation
                HP = 40000,           // God of Law — immovable judge, endurance fight
                MaxHP = 40000,
                Strength = 140,
                Defence = 160,        // High defense - need armor penetration
                Agility = 50,
                AttacksPerRound = 2,

                EncounterLocation = "Dungeon Floor 55",
                DungeonFloor = 55,

                Description = "The god of justice became the god of tyranny. His scales weigh only power now.",

                IntroDialogue = new[]
                {
                    "The throne room trembles as reality bends around a figure of cold authority.",
                    "He wears robes of judicial black, but his scales are rusted and broken.",
                    "",
                    "THORGRIM: \"You stand accused of EXISTENCE, mortal.\"",
                    "",
                    "His voice echoes with the weight of unjust sentences.",
                    "",
                    "THORGRIM: \"The penalty is DEATH. As it always is. As it always was.\""
                },

                Phase1Threshold = 1.0f,
                Phase2Threshold = 0.5f,
                Phase3Threshold = 0.2f,

                Phase1Abilities = new[] { "Judgment", "Binding Chains", "Gavel Strike" },
                Phase2Abilities = new[] { "Martial Law", "Summon Executioners", "Absolute Order" },
                Phase3Abilities = new[] { "Final Verdict", "Eye for Eye", "Tyranny Unleashed" },

                Phase2Dialogue = new[]
                {
                    "THORGRIM: \"You challenge LAW? You challenge ORDER?\"",
                    "",
                    "Spectral executioners materialize around him.",
                    "",
                    "THORGRIM: \"Then you shall know the FULL WEIGHT of justice!\""
                },

                Phase3Dialogue = new[]
                {
                    "THORGRIM: \"Wait... this is not... I was meant to PROTECT them...\"",
                    "",
                    "The hollow light in his eyes flickers.",
                    "",
                    "THORGRIM: \"When did justice become... tyranny?\""
                },

                DefeatDialogue = new[]
                {
                    "Thorgrim cracks down the middle like a dropped statue.",
                    "",
                    "THORGRIM: \"Perhaps... mercy was the law I forgot...\"",
                    "",
                    "Cold iron certainty floods into your head.",
                    "Rules. Structures. The way things OUGHT to be.",
                    "Its heavy. Like carrying a library in your skull."
                },

                ArtifactDropped = ArtifactType.ScalesOfLaw,

                CanBeSaved = false,
                SaveRequirement = null,
                SaveDialogue = null,

                SpecialMechanics = new Dictionary<string, string>
                {
                    ["Judgment"] = "Deals damage based on player's Darkness score",
                    ["BindingChains"] = "Reduces player speed for 3 rounds",
                    ["SummonExecutioners"] = "Calls 3 executioner minions that deal execution damage"
                },

                LoreUnlocked = "The Judge's Fall: Thorgrim was once the fairest of gods, weighing every soul with perfect equity. " +
                              "But mortals sought to abuse his laws, twisting justice for profit and revenge. " +
                              "In despair, Thorgrim decided that if mortals could not be trusted with freedom, " +
                              "he would take it from them. Now he judges all guilty, and the sentence is always death."
            };
        }

        private static OldGodBossData GetNoctura()
        {
            return new OldGodBossData
            {
                Type = OldGodType.Noctura,
                Name = "Noctura, The Shadow Weaver",
                Title = "Goddess of Shadow and Secrets",
                Level = 75,           // Above floor 70 - requires preparation
                HP = 50000,           // Goddess of Shadow — elusive but deadly
                MaxHP = 50000,
                Strength = 130,
                Defence = 100,
                Agility = 180,        // Very fast - hard to hit
                Charisma = 150,
                AttacksPerRound = 3,  // Multiple fast strikes

                EncounterLocation = "Dungeon Floor 70",
                DungeonFloor = 70,

                Description = "The mysterious orchestrator. She set these events in motion. Her true motives remain hidden.",

                IntroDialogue = new[]
                {
                    "The shadows coalesce into a familiar figure.",
                    "The Stranger. She was the Stranger all along.",
                    "",
                    "NOCTURA: \"Ah, so you finally see me for what I am.\"",
                    "",
                    "She smiles, and it is not unkind.",
                    "",
                    "NOCTURA: \"I have been watching you since you fell through the Veil.\"",
                    "NOCTURA: \"Guiding you. Testing you. Preparing you.\"",
                    "",
                    "\"The question is: are you ready for the truth?\""
                },

                Phase1Threshold = 1.0f,
                Phase2Threshold = 0.5f,
                Phase3Threshold = 0.2f,

                Phase1Abilities = new[] { "Shadow Step", "Veil of Darkness", "Whispered Lies" },
                Phase2Abilities = new[] { "Manifest Shadows", "Truth Unveiled", "Memory Theft" },
                Phase3Abilities = new[] { "Final Secret", "Shadow Merge", "The Offer" },

                Phase2Dialogue = new[]
                {
                    "NOCTURA: \"You're stronger than I expected. Good.\"",
                    "",
                    "Shadows swirl around her like living things.",
                    "",
                    "NOCTURA: \"But can you face the shadows within YOURSELF?\""
                },

                Phase3Dialogue = new[]
                {
                    "Noctura pauses, lowering her weapons.",
                    "",
                    "NOCTURA: \"Wait. I did not bring you here to kill you.\"",
                    "",
                    "She extends a hand.",
                    "",
                    "NOCTURA: \"I brought you here to offer you a CHOICE.\"",
                    "NOCTURA: \"Join me. Together we can reshape the divine order.\"",
                    "NOCTURA: \"Or refuse, and face me at my full power.\""
                },

                DefeatDialogue = new[]
                {
                    "Noctura melts into the shadows. You cant tell where she ends",
                    "and the darkness begins.",
                    "",
                    "NOCTURA: \"Interesting choice. We'll see if it was the right one.\"",
                    "",
                    "The shadows press in close and something changes in your eyes.",
                    "The dark doesnt look so dark anymore.",
                    "You can see things in it now. Things you wish you couldnt."
                },

                ArtifactDropped = ArtifactType.ShadowCrown,

                CanBeSaved = true, // Can be allied with
                SaveRequirement = "Accept her offer at Phase 3",
                SaveDialogue = new[]
                {
                    "> [ALLY] \"Together, then. What is your plan?\"",
                    "",
                    "Noctura's smile widens.",
                    "",
                    "NOCTURA: \"Finally, someone who understands.\"",
                    "",
                    "She reveals the truth: She orchestrated everything to find someone",
                    "capable of challenging the Creator himself.",
                    "",
                    "NOCTURA: \"Manwe has grown complacent. Cruel, even.\"",
                    "NOCTURA: \"The world needs new stewardship. Walk with me.\""
                },

                SpecialMechanics = new Dictionary<string, string>
                {
                    ["ShadowStep"] = "50% chance to dodge any attack",
                    ["TheOffer"] = "At 20% HP, offers alliance—changes entire ending path",
                    ["TruthUnveiled"] = "Reveals player's deepest secret (affects dialogue)"
                },

                LoreUnlocked = "The Weaver's Web: Noctura alone among the gods did not fall to corruption—she chose it. " +
                              "Or did she? The goddess of secrets keeps her true nature hidden even from herself. " +
                              "Some say she manipulates events to save the world. Others say she seeks only power. " +
                              "Perhaps both are true. Perhaps neither."
            };
        }

        private static OldGodBossData GetAurelion()
        {
            return new OldGodBossData
            {
                Type = OldGodType.Aurelion,
                Name = "Aurelion, The Fading Light",
                Title = "God of Light and Truth",
                Level = 90,           // Above floor 85 - requires preparation
                HP = 75000,           // God of Light — radiant divine armor, enchanted weapons essential
                MaxHP = 75000,
                Strength = 160,
                Defence = 120,
                Agility = 100,
                Wisdom = 220,         // High magic damage
                AttacksPerRound = 2,

                EncounterLocation = "Dungeon Floor 85",
                DungeonFloor = 85,

                Description = "The god of truth speaks only in whispers now. His light dims with every lie.",

                IntroDialogue = new[]
                {
                    "A faint glow flickers at the heart of the temple.",
                    "A figure of pure radiance, now barely visible, turns to face you.",
                    "",
                    "AURELION: \"You... can see me? Few can anymore.\"",
                    "",
                    "His voice is a whisper, barely audible.",
                    "",
                    "AURELION: \"The lies... so many lies in the world now.\"",
                    "AURELION: \"Each one dims my light. Each one brings the end closer.\""
                },

                Phase1Threshold = 1.0f,
                Phase2Threshold = 0.5f,
                Phase3Threshold = 0.2f,

                Phase1Abilities = new[] { "Purifying Light", "Truth Revealed", "Blinding Flash" },
                Phase2Abilities = new[] { "Solar Flare", "Divine Judgment", "Light's Embrace" },
                Phase3Abilities = new[] { "Last Light", "Sacrifice", "Final Truth" },

                Phase2Dialogue = new[]
                {
                    "AURELION: \"If you seek truth, you must earn it!\"",
                    "",
                    "His light flares brighter—painfully so.",
                    "",
                    "AURELION: \"Face the light that reveals all!\""
                },

                Phase3Dialogue = new[]
                {
                    "Aurelion's light gutters like a dying candle.",
                    "",
                    "AURELION: \"I am... nearly gone.\"",
                    "",
                    "He looks at you with ancient, weary eyes.",
                    "",
                    "AURELION: \"Will you let the light die? Or give it new purpose?\""
                },

                DefeatDialogue = new[]
                {
                    "Aurelion flickers once, twice, and goes out.",
                    "Like a candle in a draft.",
                    "",
                    "AURELION: \"Dark... at last...\"",
                    "",
                    "Something bright and painful lodges behind your eyes.",
                    "You blink and suddenly you can see everything too clearly.",
                    "Every lie. Every half-truth. Yours included."
                },

                ArtifactDropped = ArtifactType.SunforgedBlade,

                CanBeSaved = true,
                SaveRequirement = "Chivalry >= 3000 AND no lies told in dialogue",
                SaveDialogue = new[]
                {
                    "> [SAVE] \"Give it to me. The light. I'll carry it.\"",
                    "",
                    "Aurelion stares at you. For a long time.",
                    "",
                    "AURELION: \"You know what that means? You wont be able to lie. Ever.\"",
                    "",
                    "\"Yeah. I know.\"",
                    "",
                    "The light leaves him and settles into you, warm and heavy.",
                    "Aurelion slumps. He looks relieved.",
                    "",
                    "AURELION: \"Thank you. I was so tired of holding it alone.\""
                },

                SpecialMechanics = new Dictionary<string, string>
                {
                    ["TruthRevealed"] = "Detects if player has lied in any dialogue; bonus damage if true",
                    ["LightsEmbrace"] = "Heals player if they have high Chivalry, damages if high Darkness",
                    ["Sacrifice"] = "At 20% HP, can sacrifice himself to fully restore player"
                },

                LoreUnlocked = "The Light's Lament: Aurelion was truth itself—every honest word strengthened him, " +
                              "every lie weakened him. As mortals built societies on deception, the god of truth began to fade. " +
                              "Now he clings to existence by a thread, waiting for someone honest enough to either end his pain " +
                              "or give him new purpose."
            };
        }

        private static OldGodBossData GetTerravok()
        {
            return new OldGodBossData
            {
                Type = OldGodType.Terravok,
                Name = "Terravok, The Sleeping Mountain",
                Title = "God of Earth and Endurance",
                Level = 98,           // Near floor 95 - penultimate challenge
                HP = 100000,          // God of Earth — massive stone skin, demands full preparation
                MaxHP = 100000,
                Strength = 200,       // Devastating slow hits
                Defence = 250,        // Incredibly tanky
                Agility = 25,
                AttacksPerRound = 1,  // Slow but each hit is brutal

                EncounterLocation = "Dungeon Floor 95",
                DungeonFloor = 95,

                Description = "The oldest god sleeps beneath the dungeon. He can be awakened... but should he be?",

                IntroDialogue = new[]
                {
                    "The dungeon walls ARE him. The stones, the foundations—all Terravok.",
                    "A face forms in the rock, ancient beyond imagining.",
                    "",
                    "TERRAVOK: \"WHO... DISTURBS... MY... REST...\"",
                    "",
                    "Each word takes an eternity. He has slept for eons.",
                    "",
                    "TERRAVOK: \"I... REMEMBER... NOTHING...\"",
                    "TERRAVOK: \"ONLY... WEIGHT... AND... TIME...\""
                },

                Phase1Threshold = 1.0f,
                Phase2Threshold = 0.5f,
                Phase3Threshold = 0.2f,

                Phase1Abilities = new[] { "Earthquake", "Stone Skin", "Mountain's Weight" },
                Phase2Abilities = new[] { "Entomb", "Magma Surge", "Geological Shift" },
                Phase3Abilities = new[] { "World Breaker", "Core Awakening", "The Long Sleep" },

                Phase2Dialogue = new[]
                {
                    "TERRAVOK: \"I... REMEMBER... PAIN...\"",
                    "",
                    "The dungeon shakes. Magma seeps through cracks.",
                    "",
                    "TERRAVOK: \"THEY... HURT... ME... THE OTHER GODS...\"",
                    "TERRAVOK: \"SO I... SLEPT... AND FORGOT...\""
                },

                Phase3Dialogue = new[]
                {
                    "TERRAVOK: \"I... REMEMBER... NOW...\"",
                    "",
                    "His voice gains strength—and sorrow.",
                    "",
                    "TERRAVOK: \"I WAS... FOUNDATION... PROTECTOR...\"",
                    "TERRAVOK: \"THEY USED ME... TO BUILD THEIR PRISONS...\"",
                    "",
                    "\"SHOULD I... WAKE FULLY? OR... SLEEP... FOREVER?\""
                },

                DefeatDialogue = new[]
                {
                    "Terravok crumbles. The whole dungeon groans like its about to",
                    "come down on your head. It holds. Barely.",
                    "",
                    "TERRAVOK: \"REST... AT LAST... REAL REST...\"",
                    "",
                    "Stone dust settles on your skin and sinks in.",
                    "Your bones feel heavier. Denser. Like they could hold up a mountain.",
                    "Maybe they can now."
                },

                ArtifactDropped = ArtifactType.Worldstone,

                CanBeSaved = true, // Can be awakened as an ally
                SaveRequirement = "Stamina >= 100 AND chose to wake him",
                SaveDialogue = new[]
                {
                    "> [AWAKEN] \"Wake fully, ancient one. The world needs its foundation.\"",
                    "",
                    "You channel your own endurance into the stone god.",
                    "",
                    "TERRAVOK: \"YOU... GIVE ME... YOUR STRENGTH?\"",
                    "",
                    "The dungeon stops shaking. The god RISES.",
                    "",
                    "TERRAVOK: \"THEN... I WILL... STAND... AGAIN.\"",
                    "TERRAVOK: \"THE FOUNDATION... WILL NOT... CRUMBLE.\"",
                    "",
                    "A mountain has become your ally."
                },

                SpecialMechanics = new Dictionary<string, string>
                {
                    ["StoneSkin"] = "Takes 50% reduced damage from physical attacks",
                    ["MountainsWeight"] = "Slows player by 50% for 3 rounds",
                    ["WorldBreaker"] = "Massive attack that hits everything; takes 2 rounds to charge"
                },

                LoreUnlocked = "The Foundation's Sorrow: Terravok was the first god, born when the world itself formed. " +
                              "He carried the weight of all existence without complaint. But the other gods grew jealous " +
                              "and bound him beneath the earth, using his body to build their dungeons and prisons. " +
                              "In despair, Terravok chose to sleep, to forget the betrayal. He has slumbered ever since."
            };
        }

        private static OldGodBossData GetManwe()
        {
            return new OldGodBossData
            {
                Type = OldGodType.Manwe,
                Name = "Manwe, The Weary Creator",
                Title = "Supreme God of Creation and Balance",
                Level = 100,          // Final boss - the ultimate challenge
                HP = 150000,          // The Creator — the ultimate battle, requires everything you've got
                MaxHP = 150000,
                Strength = 220,
                Defence = 200,
                Agility = 180,
                Wisdom = 280,         // Master of all magic
                Charisma = 250,
                AttacksPerRound = 4,  // The Creator attacks with overwhelming force

                EncounterLocation = "Dungeon Floor 100 / Heaven",
                DungeonFloor = 100,

                Description = "The Supreme Creator. He waits at the end of all paths, weary of eternity.",

                IntroDialogue = new[]
                {
                    "At the heart of everything, a figure sits on a throne of stars.",
                    "He looks old. He looks young. He looks like everyone you ever met.",
                    "",
                    "MANWE: \"Ah. Took you long enough.\"",
                    "",
                    "His voice sounds like a thousand people talking at once.",
                    "",
                    "MANWE: \"I've been watching you, you know. Since the beginning.\"",
                    "MANWE: \"Every choice. Every fight. Every mistake.\"",
                    "",
                    "He stands up and the stars shake.",
                    "",
                    "MANWE: \"Lets find out what you're made of.\""
                },

                Phase1Threshold = 1.0f,
                Phase2Threshold = 0.5f,
                Phase3Threshold = 0.1f,

                Phase1Abilities = new[] { "Word of Creation", "Unmake", "Divine Judgment", "Time Stop", "Reality Warp" },
                Phase2Abilities = new[] { "Split Form", "Light Incarnate", "Shadow Incarnate", "The Question" },
                Phase3Abilities = new[] { "Final Word", "Creation's End", "The Offer" },

                Phase2Dialogue = new[]
                {
                    "Manwe splits into two beings—one of pure light, one of pure shadow.",
                    "",
                    "LIGHT MANWE: \"We are not your enemy.\"",
                    "SHADOW MANWE: \"We are the question you must answer.\"",
                    "",
                    "BOTH: \"WHAT WILL YOU DO WITH THE POWER OF CREATION?\""
                },

                Phase3Dialogue = new[]
                {
                    "The two forms merge. Manwe looks... tired.",
                    "",
                    "MANWE: \"Enough. I am weary of this game.\"",
                    "",
                    "He sets down his weapons.",
                    "",
                    "MANWE: \"I have watched mortals rise and fall for ten thousand years.\"",
                    "MANWE: \"I created the gods to guide you, but they grew cruel.\"",
                    "",
                    "\"I have waited for one who could either FIX what I broke...\"",
                    "\"...or END what I started.\"",
                    "",
                    "\"What say you, child of dust?\""
                },

                DefeatDialogue = new[]
                {
                    "Manwe staggers. Light and shadow bleed from the wound in his chest.",
                    "",
                    "MANWE: \"So. You chose to fight.\"",
                    "",
                    "He sinks to one knee. The dungeon trembles. Somewhere far above,",
                    "the sky cracks like glass.",
                    "",
                    "MANWE: \"Ten thousand years I waited for an answer.\"",
                    "MANWE: \"And you gave me one.\"",
                    "",
                    "He reaches out and presses his hand to your chest.",
                    "The weight of creation flows into you — every star, every stone,",
                    "every life that ever was or will be.",
                    "",
                    "MANWE: \"It is yours now. All of it. The choice of what comes next.\"",
                    "",
                    "His eyes close. The Weary Creator rests at last."
                },

                ArtifactDropped = ArtifactType.VoidKey,

                CanBeSaved = false, // Final choice, not saveable in traditional sense
                SaveRequirement = null,
                SaveDialogue = null,

                SpecialMechanics = new Dictionary<string, string>
                {
                    ["DivineJudgment"] = "Reflects damage if player has negative total alignment",
                    ["CreationsEnd"] = "Instant kill if player HP < 20%—can be blocked by artifacts",
                    ["SplitForm"] = "At 50% HP, becomes two enemies that must both be defeated",
                    ["TheOffer"] = "At 10% HP, combat ends and player makes final choice"
                },

                LoreUnlocked = "The Creator's Burden: Manwe was, is, and will be. He created everything—including the very " +
                              "concept of creation. But eternity is lonely, and perfection is boring. So he made mortals " +
                              "to surprise him, to grow, to change. He made gods to guide them. But it all went wrong. " +
                              "Now Manwe waits at the end of all paths, hoping someone will finally give him the answer " +
                              "he has sought for ten thousand years: Was creation worth it?"
            };
        }

        #endregion
    }

    /// <summary>
    /// Full data structure for an Old God boss encounter
    /// </summary>
    public class OldGodBossData
    {
        public OldGodType Type { get; set; }
        public string Name { get; set; } = "";
        public string Title { get; set; } = "";
        public string ThemeColor { get; set; } = "white";

        // Combat stats
        public int Level { get; set; }
        public long HP { get; set; }
        public long MaxHP { get; set; }
        public long Strength { get; set; }
        public long Defence { get; set; }
        public long Agility { get; set; }
        public long Wisdom { get; set; }
        public long Charisma { get; set; }
        public int AttacksPerRound { get; set; }

        // Location
        public string EncounterLocation { get; set; } = "";
        public int DungeonFloor { get; set; }
        public string Description { get; set; } = "";

        // Dialogue
        public string[] IntroDialogue { get; set; } = Array.Empty<string>();
        public string[] Phase2Dialogue { get; set; } = Array.Empty<string>();
        public string[] Phase3Dialogue { get; set; } = Array.Empty<string>();
        public string[] DefeatDialogue { get; set; } = Array.Empty<string>();

        // Phase thresholds (percentage of HP)
        public float Phase1Threshold { get; set; }
        public float Phase2Threshold { get; set; }
        public float Phase3Threshold { get; set; }

        // Abilities per phase
        public string[] Phase1Abilities { get; set; } = Array.Empty<string>();
        public string[] Phase2Abilities { get; set; } = Array.Empty<string>();
        public string[] Phase3Abilities { get; set; } = Array.Empty<string>();

        // Boss abilities for combat (computed from phase abilities)
        public List<BossAbility> Abilities => GetAllAbilities();

        private List<BossAbility> GetAllAbilities()
        {
            var abilities = new List<BossAbility>();
            // Boss abilities should deal significant damage - use full Strength as base
            // Phase 1: 100% Strength, Phase 2: 150% Strength, Phase 3: 200% Strength
            foreach (var name in Phase1Abilities)
                abilities.Add(new BossAbility { Name = name, Phase = 1, BaseDamage = (int)Strength });
            foreach (var name in Phase2Abilities)
                abilities.Add(new BossAbility { Name = name, Phase = 2, BaseDamage = (int)(Strength * 1.5) });
            foreach (var name in Phase3Abilities)
                abilities.Add(new BossAbility { Name = name, Phase = 3, BaseDamage = (int)(Strength * 2) });
            if (abilities.Count == 0)
                abilities.Add(new BossAbility { Name = "Divine Strike", Phase = 1, BaseDamage = (int)Strength });
            return abilities;
        }

        // Rewards
        public ArtifactType ArtifactDropped { get; set; }

        // Save mechanics
        public bool CanBeSaved { get; set; }
        public string? SaveRequirement { get; set; }
        public string[]? SaveDialogue { get; set; }

        // Special mechanics
        public Dictionary<string, string> SpecialMechanics { get; set; } = new();

        // Lore
        public string LoreUnlocked { get; set; } = "";
    }

    /// <summary>
    /// Boss ability data
    /// </summary>
    public class BossAbility
    {
        public string Name { get; set; } = "";
        public int Phase { get; set; } = 1;
        public int BaseDamage { get; set; } = 50;
        public bool HasSpecialEffect { get; set; }
        public string EffectDescription { get; set; } = "";
    }
}
