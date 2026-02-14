using System;
using System.Collections.Generic;
using System.Linq;
using Godot;

namespace UsurperRemake.Systems
{
    /// <summary>
    /// The Stranger Encounter System - Noctura (Old God of Shadows) appears in disguise
    /// throughout the game, teaching the player to accept death and rebirth.
    ///
    /// Core theme: "Through accepting The Stranger, who is the harbinger of death and rebirth,
    /// we accept the lesson she is teaching us."
    ///
    /// Player responses are tracked as Receptivity (-100 to +100). High receptivity unlocks
    /// peaceful alliance on Floor 70. Low receptivity means a harder fight.
    /// </summary>
    public class StrangerEncounterSystem
    {
        private static StrangerEncounterSystem? _fallbackInstance;
        public static StrangerEncounterSystem Instance
        {
            get
            {
                var ctx = UsurperRemake.Server.SessionContext.Current;
                if (ctx != null) return ctx.StrangerEncounters;
                return _fallbackInstance ??= new StrangerEncounterSystem();
            }
        }

        // Core encounter tracking
        public int EncountersHad { get; private set; } = 0;
        public HashSet<StrangerDisguise> EncounteredDisguises { get; private set; } = new();
        public List<StrangerEncounter> EncounterLog { get; private set; } = new();
        public bool PlayerSuspectsStranger { get; private set; } = false;
        public bool PlayerKnowsTruth { get; private set; } = false;

        // Receptivity: how open the player has been to Noctura's teachings (-100 to +100)
        public int Receptivity { get; private set; } = 0;

        // Response tracking: what the player chose at each encounter
        public Dictionary<int, StrangerResponseType> ResponseHistory { get; private set; } = new();

        // Scripted encounter tracking
        public HashSet<ScriptedEncounterType> CompletedScriptedEncounters { get; private set; } = new();
        public HashSet<ScriptedEncounterType> PendingScriptedEncounters { get; private set; } = new();

        // Non-repeating contextual dialogue tracking
        public HashSet<string> UsedDialogueIds { get; private set; } = new();

        // Recent game events for contextual dialogue selection
        public List<StrangerContextEvent> RecentGameEvents { get; private set; } = new();

        // Cooldown tracking
        private int _actionsSinceLastEncounter = 0;
        private const int MinActionsBetweenEncounters = 20;
        private readonly Random _random = new();

        /// <summary>
        /// All possible Stranger disguises with their contexts
        /// </summary>
        public static readonly Dictionary<StrangerDisguise, StrangerDisguiseData> Disguises = new()
        {
            [StrangerDisguise.HoodedTraveler] = new StrangerDisguiseData(
                "A Hooded Traveler",
                "A cloaked figure with eyes that seem to hold starlight",
                new[] { "MainStreet", "Inn", "DarkAlley" },
                1, 100
            ),
            [StrangerDisguise.OldBeggar] = new StrangerDisguiseData(
                "An Old Beggar",
                "A weathered crone with knowing eyes too sharp for her apparent age",
                new[] { "MainStreet", "Temple", "Church", "Healer" },
                1, 100
            ),
            [StrangerDisguise.TavernPatron] = new StrangerDisguiseData(
                "A Quiet Patron",
                "A shadowy figure in the corner, nursing a drink that never empties",
                new[] { "Inn", "DarkAlley" },
                5, 100
            ),
            [StrangerDisguise.MysteriousMerchant] = new StrangerDisguiseData(
                "A Mysterious Merchant",
                "A seller of curiosities with wares that seem to shift when you look away",
                new[] { "Marketplace", "MagicShop", "DarkAlley" },
                10, 100
            ),
            [StrangerDisguise.WoundedKnight] = new StrangerDisguiseData(
                "A Wounded Knight",
                "A figure in battered armor, but their wounds don't bleed quite right",
                new[] { "Healer", "Temple", "MainStreet" },
                15, 100
            ),
            [StrangerDisguise.DreamVisitor] = new StrangerDisguiseData(
                "A Figure in Dreams",
                "A shadow that speaks with a voice like distant thunder",
                new[] { "Inn", "Home", "Dormitory" },
                20, 100
            ),
            [StrangerDisguise.TempleSupplicant] = new StrangerDisguiseData(
                "A Temple Supplicant",
                "A worshipper who prays to no altar, yet the candles flicker at their presence",
                new[] { "Temple", "Church" },
                25, 100
            ),
            [StrangerDisguise.ShadowInMirror] = new StrangerDisguiseData(
                "A Shadow in the Mirror",
                "Your reflection blinks when you don't",
                new[] { "Home", "Inn", "Dungeon" },
                40, 100
            ),
            [StrangerDisguise.TrueForm] = new StrangerDisguiseData(
                "The Shadow Weaver",
                "Noctura reveals herself, darkness coiling around her like a living cloak",
                new[] { "DarkAlley", "Dungeon" },
                50, 100
            )
        };

        // ═══════════════════════════════════════════════════════════════════
        // SCRIPTED ENCOUNTERS - guaranteed, event-triggered, fire exactly once
        // ═══════════════════════════════════════════════════════════════════

        public static readonly Dictionary<ScriptedEncounterType, ScriptedStrangerEncounter> ScriptedEncounters = new()
        {
            [ScriptedEncounterType.AfterFirstDeath] = new ScriptedStrangerEncounter
            {
                Type = ScriptedEncounterType.AfterFirstDeath,
                Disguise = StrangerDisguise.OldBeggar,
                Title = "THE RETURN",
                ValidLocations = new[] { "Temple", "Healer", "Inn", "MainStreet" },
                IntroNarration = new[]
                {
                    "A weathered crone sits near the entrance, watching you with eyes",
                    "too sharp for her apparent age. She saw you die. She knows."
                },
                Dialogue = new[]
                {
                    "\"Back from the dark, are we?\"",
                    "\"Most who cross that threshold don't return.\"",
                    "\"Tell me -- what did you see on the other side?\"",
                    "\"Was it cold? Or was it... warm?\""
                },
                ClosingNarration = new[]
                {
                    "The crone rises stiffly and shuffles away. But for a moment,",
                    "her shadow falls wrong -- too long, too dark, as if it belongs",
                    "to someone much larger."
                },
                Responses = new List<StrangerResponseOption>
                {
                    new() { Key = "1", Text = "It was terrifying. I never want to feel that again.",
                        ResponseType = StrangerResponseType.Engaged, ReceptivityChange = 8,
                        StrangerReply = new[] { "\"Fear of death is the first wall. Every wall can become a door.\"" } },
                    new() { Key = "2", Text = "There was... a moment of peace. Before the pain.",
                        ResponseType = StrangerResponseType.Reflective, ReceptivityChange = 12,
                        StrangerReply = new[] { "\"You felt it. That peace -- that is what waits beneath the fear.\"", "\"Remember it.\"" } },
                    new() { Key = "3", Text = "I don't remember anything. Just darkness.",
                        ResponseType = StrangerResponseType.Silent, ReceptivityChange = 0,
                        StrangerReply = new[] { "\"Perhaps. Or perhaps you chose to forget. The dying often do.\"" } },
                    new() { Key = "4", Text = "Death is for the weak. I won't make the same mistake.",
                        ResponseType = StrangerResponseType.Hostile, ReceptivityChange = -10,
                        StrangerReply = new[] { "*She smiles sadly.*", "\"The young always think death is a failure, not a teacher.\"" } },
                    new() { Key = "5", Text = "Who are you? Why do you care about my death?",
                        ResponseType = StrangerResponseType.Challenged, ReceptivityChange = 5,
                        StrangerReply = new[] { "\"I care about all deaths. And all returns.\"", "\"They are... my specialty.\"" } }
                }
            },

            [ScriptedEncounterType.AfterCompanionDeath] = new ScriptedStrangerEncounter
            {
                Type = ScriptedEncounterType.AfterCompanionDeath,
                Disguise = StrangerDisguise.WoundedKnight,
                Title = "THE RIVER AND THE SEA",
                ValidLocations = new[] { "Healer", "Temple", "MainStreet", "Inn" },
                IntroNarration = new[]
                {
                    "A figure in battered armor stands near where you lost them.",
                    "Their wounds don't bleed quite right. They look at you with",
                    "an expression of ancient, knowing sorrow."
                },
                Dialogue = new[]
                {
                    "\"I saw what happened. With your companion.\"",
                    "\"Death took them. You think it was cruel.\"",
                    "\"But tell me -- is the river cruel when it reaches the sea?\"",
                    "\"Or does the sea welcome every river home?\""
                },
                ClosingNarration = new[]
                {
                    "The knight inclines their head, a gesture of respect for your grief.",
                    "When you look again, they are gone -- only shadows where they stood."
                },
                Responses = new List<StrangerResponseOption>
                {
                    new() { Key = "1", Text = "They didn't deserve to die. This world is merciless.",
                        ResponseType = StrangerResponseType.Engaged, ReceptivityChange = 8,
                        StrangerReply = new[] { "\"Merciless? Or simply indifferent to our small ideas of fairness?\"", "\"The rain falls on saints and sinners alike.\"" } },
                    new() { Key = "2", Text = "I think... they're at peace now. I hope so.",
                        ResponseType = StrangerResponseType.Reflective, ReceptivityChange = 12,
                        StrangerReply = new[] { "*Her eyes soften.*", "\"They are. More than you know.\"", "\"Death is not the opposite of life. It is the rest between breaths.\"" } },
                    new() { Key = "3", Text = "I'll honor them by growing stronger. Their sacrifice means something.",
                        ResponseType = StrangerResponseType.Accepted, ReceptivityChange = 15,
                        StrangerReply = new[] { "\"Yes. That is the lesson.\"", "\"The fallen become the soil from which the living grow.\"", "\"They are not gone -- they are transformed.\"" } },
                    new() { Key = "4", Text = "I don't need your philosophy. I need them back.",
                        ResponseType = StrangerResponseType.Dismissed, ReceptivityChange = -5,
                        StrangerReply = new[] { "\"I know. Grief is its own kind of death.\"", "\"But you will survive it, as you survived your own death.\"" } },
                    new() { Key = "5", Text = "Get away from me.",
                        ResponseType = StrangerResponseType.Hostile, ReceptivityChange = -10,
                        StrangerReply = new[] { "*She doesn't flinch.*", "\"You cannot kill what I represent. But I understand the need to try.\"" } }
                }
            },

            [ScriptedEncounterType.AfterFirstOldGod] = new ScriptedStrangerEncounter
            {
                Type = ScriptedEncounterType.AfterFirstOldGod,
                Disguise = StrangerDisguise.TavernPatron,
                Title = "DEATH FEEDS LIFE",
                ValidLocations = new[] { "Inn", "DarkAlley", "MainStreet" },
                IntroNarration = new[]
                {
                    "A shadowy figure in the corner raises a glass to you.",
                    "Their drink never seems to empty. They've been watching."
                },
                Dialogue = new[]
                {
                    "\"You faced a god today.\"",
                    "\"Not many can say that and still draw breath.\"",
                    "\"But did you notice? When the god fell...\"",
                    "\"...part of them became part of you?\"",
                    "\"That is how it works. Death feeds life.\"",
                    "\"Destruction feeds creation.\"",
                    "\"The gods know this. They've lived it for ten thousand years.\""
                },
                ClosingNarration = new[]
                {
                    "The figure raises their glass one final time, toasting something",
                    "you cannot see. The shadows in the corner grow deeper, then empty."
                },
                Responses = new List<StrangerResponseOption>
                {
                    new() { Key = "1", Text = "I felt something change when I defeated them. What happened to me?",
                        ResponseType = StrangerResponseType.Engaged, ReceptivityChange = 8,
                        StrangerReply = new[] { "\"You absorbed a fragment of divinity.\"", "\"A drop returns to the ocean. A death becomes a birth.\"" } },
                    new() { Key = "2", Text = "The gods are prisoners, not enemies. Their deaths trouble me.",
                        ResponseType = StrangerResponseType.Reflective, ReceptivityChange = 12,
                        StrangerReply = new[] { "\"Good. Let it trouble you.\"", "\"The day death stops troubling you is the day you stop being worthy\"", "\"of the power it grants.\"" } },
                    new() { Key = "3", Text = "Every god I face teaches me something. Even in destruction.",
                        ResponseType = StrangerResponseType.Accepted, ReceptivityChange = 15,
                        StrangerReply = new[] { "*A genuine smile crosses her face.*", "\"Now you begin to understand my lesson.\"", "\"Destruction and creation are the same hand.\"" } },
                    new() { Key = "4", Text = "I did what I had to. Don't make it philosophical.",
                        ResponseType = StrangerResponseType.Dismissed, ReceptivityChange = -5,
                        StrangerReply = new[] { "\"Everything is philosophical, mortal.\"", "\"Every sword swing is an argument about power.\"", "\"Every mercy is a theorem about love.\"" } }
                }
            },

            [ScriptedEncounterType.TheMidgameLesson] = new ScriptedStrangerEncounter
            {
                Type = ScriptedEncounterType.TheMidgameLesson,
                Disguise = StrangerDisguise.DreamVisitor,
                Title = "THE COCOON",
                ValidLocations = new[] { "Inn", "Home" },
                IntroNarration = new[]
                {
                    "The dream comes between breaths of sleep. A figure stands at the edge",
                    "of a vast ocean. You know her now. You've seen her before, in many faces.",
                    "She turns to you."
                },
                Dialogue = new[]
                {
                    "\"You've grown strong. Strong enough to hear the truth.\"",
                    "\"Do you know what a caterpillar thinks when the cocoon closes?\"",
                    "\"It thinks it is dying. It IS dying.\"",
                    "\"The caterpillar ceases to exist. Dissolved. Gone.\"",
                    "\"But from that death, something with wings is born.\"",
                    "\"That is what I have been trying to teach you.\"",
                    "\"Death is not your enemy. It is the cocoon.\""
                },
                ClosingNarration = new[]
                {
                    "The ocean behind her roars, but gently -- like a lullaby.",
                    "You wake with the taste of salt on your lips and the feeling",
                    "that you've forgotten something important."
                },
                Responses = new List<StrangerResponseOption>
                {
                    new() { Key = "1", Text = "I've died. I've lost friends. I'm starting to understand.",
                        ResponseType = StrangerResponseType.Accepted, ReceptivityChange = 15,
                        StrangerReply = new[] { "\"Yes. The pain was the lesson. The loss was the teacher.\"", "\"And the teacher was always me.\"" } },
                    new() { Key = "2", Text = "You keep appearing. Who ARE you, really?",
                        ResponseType = StrangerResponseType.Challenged, ReceptivityChange = 5,
                        StrangerReply = new[] { "\"I am the one who waits at every threshold.\"", "\"The shadow between what was and what will be.\"", "\"When you are ready, I will tell you my true name.\"" } },
                    new() { Key = "3", Text = "Beautiful metaphor. But my friends are still dead.",
                        ResponseType = StrangerResponseType.Engaged, ReceptivityChange = 8,
                        StrangerReply = new[] { "\"Yes. And their deaths shaped you into someone who could hear this.\"", "\"Is that not a form of immortality --\"", "\"to live on in the transformation of those you loved?\"" } },
                    new() { Key = "4", Text = "I'm tired of your riddles, Stranger.",
                        ResponseType = StrangerResponseType.Dismissed, ReceptivityChange = -5,
                        StrangerReply = new[] { "*She gazes at the ocean.*", "\"The ocean is patient. It will wait.\"" } }
                }
            },

            [ScriptedEncounterType.TheRevelation] = new ScriptedStrangerEncounter
            {
                Type = ScriptedEncounterType.TheRevelation,
                Disguise = StrangerDisguise.TrueForm,
                Title = "THE SHADOW WEAVER",
                ValidLocations = new[] { "DarkAlley", "Dungeon", "MainStreet", "Temple" },
                IntroNarration = new[]
                {
                    "The shadows gather. Not randomly -- deliberately.",
                    "They coalesce into a form you have seen in fragments across",
                    "a dozen faces. The hooded traveler. The old beggar. The quiet patron.",
                    "All masks, now shed."
                },
                Dialogue = new[]
                {
                    "\"Enough pretending.\"",
                    "\"You know me. You've always known me.\"",
                    "\"I am Noctura. Goddess of Shadows. Of endings.\"",
                    "\"Of the dark between the stars.\"",
                    "",
                    "\"And I have been watching you since the day you woke\"",
                    "\"in that dormitory with no memory.\"",
                    "",
                    "\"Every disguise. Every encounter. Every lesson.\"",
                    "\"All leading to this moment.\"",
                    "",
                    "\"You are a fragment of Manwe, sent to walk among mortals.\"",
                    "\"You have died before. You will die again.\"",
                    "\"But now, finally, you might be ready to understand why.\""
                },
                ClosingNarration = new[]
                {
                    "The darkness recedes, leaving only the memory of starlight eyes",
                    "and a voice like silk and thunder. You know her name now.",
                    "And she knows yours -- your true name, from before the forgetting."
                },
                Responses = new List<StrangerResponseOption>
                {
                    new() { Key = "1", Text = "I've been listening. Every encounter taught me something.",
                        ResponseType = StrangerResponseType.Accepted, ReceptivityChange = 15,
                        StrangerReply = new[] {
                            "\"Then the cocoon is nearly open.\"",
                            "\"When we meet again on Floor 70,\"",
                            "\"it will not be as Stranger and mortal.\"",
                            "\"It will be as... what we truly are.\""
                        } },
                    new() { Key = "2", Text = "Noctura. The Goddess of Shadows. Why me?",
                        ResponseType = StrangerResponseType.Reflective, ReceptivityChange = 12,
                        StrangerReply = new[] {
                            "\"Because you are the wave that remembers it is water.\"",
                            "\"Manwe sends fragments of himself to learn mortality.\"",
                            "\"Most forget. You... you started to remember.\""
                        } },
                    new() { Key = "3", Text = "All those disguises... you were testing me?",
                        ResponseType = StrangerResponseType.Challenged, ReceptivityChange = 5,
                        StrangerReply = new[] {
                            "\"Not testing. Teaching.\"",
                            "\"Every death you witnessed, every loss you endured --\"",
                            "\"I was there, holding the mirror.\"",
                            "\"Showing you what death really is.\""
                        } },
                    new() { Key = "4", Text = "Stay away from me, goddess. I want nothing from the Old Gods.",
                        ResponseType = StrangerResponseType.Hostile, ReceptivityChange = -10,
                        StrangerReply = new[] {
                            "*She sighs.*",
                            "\"You cannot flee from shadow. It follows every light.\"",
                            "\"We will meet again on Floor 70.\"",
                            "\"And then you will choose.\""
                        } }
                }
            },

            [ScriptedEncounterType.PreFloor70] = new ScriptedStrangerEncounter
            {
                Type = ScriptedEncounterType.PreFloor70,
                Disguise = StrangerDisguise.ShadowInMirror,
                Title = "THE FINAL LESSON",
                ValidLocations = new[] { "Dungeon" },
                IntroNarration = new[]
                {
                    "Your reflection in a puddle of water shifts. It smiles when you don't.",
                    "It mouths words you cannot hear. Then, for an instant,",
                    "the reflection is not yours -- it is hers."
                },
                Dialogue = new[]
                {
                    "\"You are close now.\"",
                    "\"When we meet, you will have a choice.\"",
                    "\"You can fight me. Many have. All have suffered.\"",
                    "\"Or you can accept what I have taught you.\"",
                    "\"That death is not destruction. It is transformation.\"",
                    "\"That the shadow is not the enemy of light.\"",
                    "\"It is its other half.\""
                },
                ClosingNarration = new[]
                {
                    "The water stills. Your reflection is your own again.",
                    "But in the darkness ahead, something waits. Something familiar."
                },
                Responses = new List<StrangerResponseOption>
                {
                    new() { Key = "1", Text = "I accept your lesson, Noctura. I am ready.",
                        ResponseType = StrangerResponseType.Accepted, ReceptivityChange = 15,
                        StrangerReply = new[] { "\"Then when we meet, there will be no need for violence.\"", "\"Only understanding.\"" } },
                    new() { Key = "2", Text = "I hear your words. But I will decide when I face you.",
                        ResponseType = StrangerResponseType.Reflective, ReceptivityChange = 12,
                        StrangerReply = new[] { "\"Wise. The moment of truth is not in preparation but in action.\"" } },
                    new() { Key = "3", Text = "Whatever happens, I will face you without fear.",
                        ResponseType = StrangerResponseType.Challenged, ReceptivityChange = 5,
                        StrangerReply = new[] { "\"Without fear? That is the bravest thing a mortal can say\"", "\"to Death herself.\"" } }
                }
            }
        };

        // ═══════════════════════════════════════════════════════════════════
        // CONTEXTUAL DIALOGUE POOL - non-repeating random encounters
        // ═══════════════════════════════════════════════════════════════════

        private static readonly List<ContextualStrangerDialogue> ContextualDialoguePool = new()
        {
            // ── EARLY GAME (encounters 1-4, levels 1-25): Death Omens ──

            new() { Id = "ctx_early_graveyard", MinEncounters = 1, MaxEncounters = 4, MinLevel = 1, MaxLevel = 30,
                Dialogue = new[] {
                    "\"The flowers in the graveyard bloom brightest. Did you know that?\"",
                    "\"The dead feed the living. The living become the dead.\"",
                    "\"It is not a tragedy. It is a circle.\""
                },
                Responses = new List<StrangerResponseOption> {
                    new() { Key = "1", Text = "That's a morbid way to look at flowers.", ResponseType = StrangerResponseType.Engaged, ReceptivityChange = 8,
                        StrangerReply = new[] { "\"Morbid? Or honest? The flower does not mourn the soil it grows from.\"" } },
                    new() { Key = "2", Text = "Death feeding life... there's a certain beauty to that.", ResponseType = StrangerResponseType.Reflective, ReceptivityChange = 12,
                        StrangerReply = new[] { "\"You see it. Most don't. Most see only the grave, never the bloom.\"" } },
                    new() { Key = "3", Text = "I'd rather not think about graveyards.", ResponseType = StrangerResponseType.Dismissed, ReceptivityChange = -5,
                        StrangerReply = new[] { "\"And yet the graveyard thinks about you. It is patient.\"" } },
                    new() { Key = "0", Text = "Say nothing and leave.", ResponseType = StrangerResponseType.Silent, ReceptivityChange = 0,
                        StrangerReply = new[] { "They watch you go with an expression that might be amusement, or might be sorrow." } }
                }
            },

            new() { Id = "ctx_early_seasons", MinEncounters = 1, MaxEncounters = 4, MinLevel = 1, MaxLevel = 30,
                Dialogue = new[] {
                    "\"Do you know why autumn is beautiful?\"",
                    "\"Because every leaf knows it is dying.\"",
                    "\"And instead of hiding, it burns brightest just before the end.\""
                },
                Responses = new List<StrangerResponseOption> {
                    new() { Key = "1", Text = "I never thought of it that way.", ResponseType = StrangerResponseType.Reflective, ReceptivityChange = 12,
                        StrangerReply = new[] { "\"Few do. They see the falling and call it loss. I see the burning and call it glory.\"" } },
                    new() { Key = "2", Text = "Are you always this philosophical?", ResponseType = StrangerResponseType.Challenged, ReceptivityChange = 5,
                        StrangerReply = new[] { "\"Only when I meet someone worth philosophizing at.\"" } },
                    new() { Key = "0", Text = "Say nothing and leave.", ResponseType = StrangerResponseType.Silent, ReceptivityChange = 0,
                        StrangerReply = new[] { "*A leaf drifts between you, though there are no trees nearby.*" } }
                }
            },

            new() { Id = "ctx_early_dormitory", MinEncounters = 1, MaxEncounters = 3, MinLevel = 1, MaxLevel = 20,
                Dialogue = new[] {
                    "\"The dormitory has seen many who wake without memories.\"",
                    "\"Each one was someone before. Each one died before they woke.\"",
                    "\"Does that trouble you? That the person you were... is gone?\""
                },
                Responses = new List<StrangerResponseOption> {
                    new() { Key = "1", Text = "I... hadn't thought about it like that. A death before waking.", ResponseType = StrangerResponseType.Reflective, ReceptivityChange = 12,
                        StrangerReply = new[] { "\"Your first death, and you don't even remember it.\"", "\"Perhaps that is a mercy.\"" } },
                    new() { Key = "2", Text = "I'm more concerned with who I am NOW.", ResponseType = StrangerResponseType.Challenged, ReceptivityChange = 5,
                        StrangerReply = new[] { "\"A practical answer. But 'now' is just the space between two deaths.\"" } },
                    new() { Key = "0", Text = "Say nothing and leave.", ResponseType = StrangerResponseType.Silent, ReceptivityChange = 0,
                        StrangerReply = new[] { "\"Think on it. When you're ready, the answer will find you.\"" } }
                }
            },

            new() { Id = "ctx_early_after_death", MinEncounters = 1, MaxEncounters = 5, MinLevel = 1, MaxLevel = 40,
                RequiredRecentEvent = StrangerContextEvent.PlayerDied,
                Dialogue = new[] {
                    "\"I heard you visited the other side recently.\"",
                    "\"How was it? Cold? Or did it feel like... remembering?\""
                },
                Responses = new List<StrangerResponseOption> {
                    new() { Key = "1", Text = "How do you know about that?", ResponseType = StrangerResponseType.Challenged, ReceptivityChange = 5,
                        StrangerReply = new[] { "\"I know about all deaths. It is my nature.\"" } },
                    new() { Key = "2", Text = "It felt... familiar. Like I'd been there before.", ResponseType = StrangerResponseType.Reflective, ReceptivityChange = 12,
                        StrangerReply = new[] { "\"Because you have. More times than you can imagine.\"" } },
                    new() { Key = "0", Text = "Say nothing and leave.", ResponseType = StrangerResponseType.Silent, ReceptivityChange = 0,
                        StrangerReply = new[] { "\"The dead keep their secrets. For now.\"" } }
                }
            },

            new() { Id = "ctx_early_candle", MinEncounters = 2, MaxEncounters = 5, MinLevel = 5, MaxLevel = 35,
                Dialogue = new[] {
                    "\"A candle does not fear the darkness. Did you know that?\"",
                    "\"It knows that when it goes out, it was not defeated.\"",
                    "\"It simply gave all its light. Every last drop.\""
                },
                Responses = new List<StrangerResponseOption> {
                    new() { Key = "1", Text = "And then another candle is lit from the first.", ResponseType = StrangerResponseType.Accepted, ReceptivityChange = 15,
                        StrangerReply = new[] { "*Her eyes widen with something like delight.*", "\"Yes! You understand! The flame passes on. Only the wax is lost.\"" } },
                    new() { Key = "2", Text = "Pretty words, but candles don't have feelings.", ResponseType = StrangerResponseType.Dismissed, ReceptivityChange = -5,
                        StrangerReply = new[] { "\"No. But you do. And one day, your light will pass to another.\"", "\"Will that be loss, or legacy?\"" } },
                    new() { Key = "0", Text = "Say nothing and leave.", ResponseType = StrangerResponseType.Silent, ReceptivityChange = 0,
                        StrangerReply = new[] { "A candle flame flickers nearby, though there is no wind." } }
                }
            },

            // ── MID GAME (encounters 5-7, levels 20-50): The Teacher ──

            new() { Id = "ctx_mid_wave", MinEncounters = 4, MaxEncounters = 8, MinLevel = 15, MaxLevel = 55,
                Dialogue = new[] {
                    "\"Have you ever watched a wave?\"",
                    "\"It rises, it crests, it crashes. And we mourn the wave.\"",
                    "\"But the water? The water is still there.\"",
                    "\"It was never the wave that mattered. It was always the ocean.\""
                },
                Responses = new List<StrangerResponseOption> {
                    new() { Key = "1", Text = "The wave doesn't know it's the ocean.", ResponseType = StrangerResponseType.Reflective, ReceptivityChange = 12,
                        StrangerReply = new[] { "\"No. And that is the great tragedy.\"", "\"But also the great adventure. To be a wave, believing you are separate,\"", "\"and then... to remember.\"" } },
                    new() { Key = "2", Text = "You're talking about more than waves, aren't you?", ResponseType = StrangerResponseType.Engaged, ReceptivityChange = 8,
                        StrangerReply = new[] { "\"I'm talking about everything. About you. About me. About death itself.\"", "\"The wave crashes. But the water never dies.\"" } },
                    new() { Key = "0", Text = "Say nothing and leave.", ResponseType = StrangerResponseType.Silent, ReceptivityChange = 0,
                        StrangerReply = new[] { "You walk away, but you swear you hear the sound of distant waves." } }
                }
            },

            new() { Id = "ctx_mid_after_god", MinEncounters = 4, MaxEncounters = 10, MinLevel = 20, MaxLevel = 70,
                RequiredRecentEvent = StrangerContextEvent.OldGodDefeated,
                Dialogue = new[] {
                    "\"Another god has fallen. Or risen. The distinction is thinner than you think.\"",
                    "\"When a god dies, the power doesn't vanish.\"",
                    "\"It transforms. Into rain. Into dreams. Into you.\""
                },
                Responses = new List<StrangerResponseOption> {
                    new() { Key = "1", Text = "Into me? What do you mean?", ResponseType = StrangerResponseType.Engaged, ReceptivityChange = 8,
                        StrangerReply = new[] { "\"Every god you face leaves a mark on your soul.\"", "\"Not a wound. A gift. Whether you wanted it or not.\"" } },
                    new() { Key = "2", Text = "I can feel their power. It frightens me sometimes.", ResponseType = StrangerResponseType.Reflective, ReceptivityChange = 12,
                        StrangerReply = new[] { "\"Good. Power without fear is power without wisdom.\"", "\"And wisdom is what separates a destroyer from a transformer.\"" } },
                    new() { Key = "0", Text = "Say nothing and leave.", ResponseType = StrangerResponseType.Silent, ReceptivityChange = 0,
                        StrangerReply = new[] { "\"The gods are watching you. Even the dead ones.\"" } }
                }
            },

            new() { Id = "ctx_mid_companion_grief", MinEncounters = 3, MaxEncounters = 10, MinLevel = 10, MaxLevel = 70,
                RequiredRecentEvent = StrangerContextEvent.CompanionDied,
                Dialogue = new[] {
                    "\"You carry a heaviness I recognize.\"",
                    "\"The weight of someone who wasn't ready to lose what they loved.\"",
                    "\"That weight is a gift. It means what you had was real.\""
                },
                Responses = new List<StrangerResponseOption> {
                    new() { Key = "1", Text = "A gift? It feels like a curse.", ResponseType = StrangerResponseType.Engaged, ReceptivityChange = 8,
                        StrangerReply = new[] { "\"Grief and love are the same emotion, seen from different sides.\"", "\"You cannot have one without accepting the other.\"" } },
                    new() { Key = "2", Text = "You're right. I wouldn't trade the memories to stop the pain.", ResponseType = StrangerResponseType.Accepted, ReceptivityChange = 15,
                        StrangerReply = new[] { "\"And there it is. The lesson death teaches best.\"", "\"That love is worth the price of loss.\"" } },
                    new() { Key = "0", Text = "Say nothing and leave.", ResponseType = StrangerResponseType.Silent, ReceptivityChange = 0,
                        StrangerReply = new[] { "She watches you go, carrying the weight. She has carried it too, for longer than you know." } }
                }
            },

            new() { Id = "ctx_mid_phoenix", MinEncounters = 4, MaxEncounters = 8, MinLevel = 20, MaxLevel = 50,
                Dialogue = new[] {
                    "\"In the old stories, the phoenix does not fear the flame.\"",
                    "\"It flies INTO it. Willingly. Joyfully.\"",
                    "\"Because it knows what waits on the other side of burning.\"",
                    "\"Not ash. Renewal.\""
                },
                Responses = new List<StrangerResponseOption> {
                    new() { Key = "1", Text = "I've seen renewal. After every battle, after every loss.", ResponseType = StrangerResponseType.Accepted, ReceptivityChange = 15,
                        StrangerReply = new[] { "\"You are learning faster than most.\"", "\"The flame is not the enemy. The refusal to burn is.\"" } },
                    new() { Key = "2", Text = "Easy to say when you're not the one burning.", ResponseType = StrangerResponseType.Challenged, ReceptivityChange = 5,
                        StrangerReply = new[] { "*She looks at you with ancient, weary eyes.*", "\"Child, I have burned more times than there are stars.\"" } },
                    new() { Key = "0", Text = "Say nothing and leave.", ResponseType = StrangerResponseType.Silent, ReceptivityChange = 0,
                        StrangerReply = new[] { "A warmth brushes past you. Like standing near a fire that gives light but cannot burn." } }
                }
            },

            new() { Id = "ctx_mid_sleep", MinEncounters = 4, MaxEncounters = 8, MinLevel = 20, MaxLevel = 55,
                Dialogue = new[] {
                    "\"What if death is just... sleep?\"",
                    "\"You close your eyes. The world dissolves.\"",
                    "\"And when you open them, you are somewhere new.\"",
                    "\"Different, but still yourself. Always yourself.\""
                },
                Responses = new List<StrangerResponseOption> {
                    new() { Key = "1", Text = "That's... oddly comforting.", ResponseType = StrangerResponseType.Reflective, ReceptivityChange = 12,
                        StrangerReply = new[] { "\"It was meant to be.\"", "\"Fear of death is fear of sleep. And no one fears sleep.\"" } },
                    new() { Key = "2", Text = "Sleep doesn't usually involve swords.", ResponseType = StrangerResponseType.Challenged, ReceptivityChange = 5,
                        StrangerReply = new[] { "*A flicker of amusement.*", "\"No. But the principle is the same.\"", "\"The letting go. The trust that something waits on the other side.\"" } },
                    new() { Key = "0", Text = "Say nothing and leave.", ResponseType = StrangerResponseType.Silent, ReceptivityChange = 0,
                        StrangerReply = new[] { "\"Sleep well, when you do. And don't fear the waking.\"" } }
                }
            },

            // ── LATE GAME (encounters 8+, levels 45+): The Bridge ──

            new() { Id = "ctx_late_cycle", MinEncounters = 7, MinLevel = 40, MinAwakening = 2,
                Dialogue = new[] {
                    "\"You're beginning to feel it, aren't you?\"",
                    "\"That ache at the edge of memory.\"",
                    "\"You've been here before. Done this before.\"",
                    "\"Death and rebirth. Death and rebirth. The wheel turns.\""
                },
                Responses = new List<StrangerResponseOption> {
                    new() { Key = "1", Text = "The wheel... is that what this is? A cycle?", ResponseType = StrangerResponseType.Engaged, ReceptivityChange = 8,
                        StrangerReply = new[] { "\"A cycle. A spiral. A dream dreaming itself awake.\"", "\"The question is not whether the wheel turns.\"", "\"It's whether you can step off it.\"" } },
                    new() { Key = "2", Text = "I feel like I've known you for lifetimes.", ResponseType = StrangerResponseType.Reflective, ReceptivityChange = 12,
                        StrangerReply = new[] { "\"You have. In every cycle, I find you. In every cycle, I teach.\"", "\"And in every cycle... you forget. Until now.\"" } },
                    new() { Key = "0", Text = "Say nothing and leave.", ResponseType = StrangerResponseType.Silent, ReceptivityChange = 0,
                        StrangerReply = new[] { "\"The wheel turns. But you're getting closer to the center.\"" } }
                }
            },

            new() { Id = "ctx_late_identity", MinEncounters = 7, MinLevel = 45, MinAwakening = 3,
                Dialogue = new[] {
                    "\"Have you noticed?\"",
                    "\"Every time you die, you come back the same.\"",
                    "\"Every time THEY die, they stay dead.\"",
                    "\"Why is that?\"",
                    "\"What makes you so... different?\""
                },
                Responses = new List<StrangerResponseOption> {
                    new() { Key = "1", Text = "I've wondered that. I'm afraid of the answer.", ResponseType = StrangerResponseType.Reflective, ReceptivityChange = 12,
                        StrangerReply = new[] { "\"Fear of the answer is close to the answer itself.\"", "\"You are not what you think you are.\"", "\"You are so much more.\"" } },
                    new() { Key = "2", Text = "Because I'm not mortal. Not really. Am I?", ResponseType = StrangerResponseType.Accepted, ReceptivityChange = 15,
                        StrangerReply = new[] { "*Her eyes glisten with something that might be tears.*", "\"You're remembering. Finally, you're remembering.\"" } },
                    new() { Key = "0", Text = "Say nothing and leave.", ResponseType = StrangerResponseType.Silent, ReceptivityChange = 0,
                        StrangerReply = new[] { "\"The answer is coming. Whether you want it or not.\"" } }
                }
            },

            new() { Id = "ctx_late_after_seal", MinEncounters = 5, MinLevel = 30,
                RequiredRecentEvent = StrangerContextEvent.SealCollected,
                Dialogue = new[] {
                    "\"Another seal. Another piece of the story.\"",
                    "\"Do you see the pattern yet?\"",
                    "\"Creation. Separation. Forgetting. Suffering.\"",
                    "\"And then... remembering. The hardest part.\""
                },
                Responses = new List<StrangerResponseOption> {
                    new() { Key = "1", Text = "The seals tell the story of the gods. And of us.", ResponseType = StrangerResponseType.Reflective, ReceptivityChange = 12,
                        StrangerReply = new[] { "\"Of ALL of us. Gods, mortals, shadows.\"", "\"We are all fragments of the same shattered mirror.\"" } },
                    new() { Key = "2", Text = "I'm still putting the pieces together.", ResponseType = StrangerResponseType.Engaged, ReceptivityChange = 8,
                        StrangerReply = new[] { "\"Keep going. When the last piece falls into place...\"", "\"...you will see the face in the mirror. Your true face.\"" } },
                    new() { Key = "0", Text = "Say nothing and leave.", ResponseType = StrangerResponseType.Silent, ReceptivityChange = 0,
                        StrangerReply = new[] { "\"The story writes itself. You need only keep reading.\"" } }
                }
            },

            new() { Id = "ctx_late_manwe", MinEncounters = 8, MinLevel = 50, MinAwakening = 4,
                Dialogue = new[] {
                    "\"Manwe is tired. Can you feel it?\"",
                    "\"He created the gods to have companions.\"",
                    "\"But he never understood them. Never truly.\"",
                    "\"So he became mortal. Again and again.\"",
                    "\"Living. Loving. Losing.\"",
                    "\"Learning what it means to die.\""
                },
                Responses = new List<StrangerResponseOption> {
                    new() { Key = "1", Text = "You're talking about me. I am Manwe's fragment.", ResponseType = StrangerResponseType.Accepted, ReceptivityChange = 15,
                        StrangerReply = new[] { "\"Yes. And every death you've endured, he endured with you.\"", "\"Every lesson I taught you, I taught him.\"", "\"Through you. Always through you.\"" } },
                    new() { Key = "2", Text = "Manwe... the Creator. Why would he choose suffering?", ResponseType = StrangerResponseType.Reflective, ReceptivityChange = 12,
                        StrangerReply = new[] { "\"Because understanding cannot come without experience.\"", "\"He needed to know what death FELT like.\"", "\"And so he died. A thousand times. Through you.\"" } },
                    new() { Key = "0", Text = "Say nothing and leave.", ResponseType = StrangerResponseType.Silent, ReceptivityChange = 0,
                        StrangerReply = new[] { "\"When you face him, remember this conversation.\"" } }
                }
            },

            // ── WISDOM/SUSPICION dialogues ──

            new() { Id = "ctx_suspects_identity", MinEncounters = 4, RequiresSuspicion = true,
                Dialogue = new[] {
                    "\"You've noticed, haven't you?\"",
                    "\"The same eyes in every face. The same shadow.\"",
                    "\"You're wondering if all the strangers are the same stranger.\""
                },
                Responses = new List<StrangerResponseOption> {
                    new() { Key = "1", Text = "Are you? Are you always the same person?", ResponseType = StrangerResponseType.Engaged, ReceptivityChange = 8,
                        StrangerReply = new[] { "\"Person? No. I am something older than persons.\"", "\"But yes. Every face you've seen was mine.\"", "\"Every lesson was the same lesson.\"" } },
                    new() { Key = "2", Text = "I know what you are. You're Noctura.", ResponseType = StrangerResponseType.Challenged, ReceptivityChange = 5,
                        StrangerReply = new[] { "\"Clever. But knowing my name changes nothing.\"", "\"The question is not who I am. It's what I'm teaching.\"" } },
                    new() { Key = "0", Text = "Say nothing and leave.", ResponseType = StrangerResponseType.Silent, ReceptivityChange = 0,
                        StrangerReply = new[] { "\"When you're ready to say my name, I'll be listening.\"" } }
                }
            }
        };

        public StrangerEncounterSystem()
        {
            _fallbackInstance = this;
        }

        // ═══════════════════════════════════════════════════════════════════
        // PUBLIC METHODS
        // ═══════════════════════════════════════════════════════════════════

        /// <summary>
        /// Increment action counter. Called from BaseLocation on every location visit.
        /// </summary>
        public void OnPlayerAction(string location, Character player)
        {
            _actionsSinceLastEncounter++;
        }

        /// <summary>
        /// Queue a scripted encounter to fire at the next eligible location visit.
        /// </summary>
        public void QueueScriptedEncounter(ScriptedEncounterType type)
        {
            if (CompletedScriptedEncounters.Contains(type)) return;
            if (PendingScriptedEncounters.Contains(type)) return;
            PendingScriptedEncounters.Add(type);
            GD.Print($"[Stranger] Queued scripted encounter: {type}");
        }

        /// <summary>
        /// Record a game event for contextual dialogue awareness.
        /// </summary>
        public void RecordGameEvent(StrangerContextEvent evt)
        {
            if (!RecentGameEvents.Contains(evt))
                RecentGameEvents.Add(evt);
        }

        /// <summary>
        /// Check for and return a pending scripted encounter at this location.
        /// Returns null if no scripted encounter is ready.
        /// </summary>
        public ScriptedStrangerEncounter? GetPendingScriptedEncounter(string location, Character player)
        {
            foreach (var type in PendingScriptedEncounters.ToList())
            {
                if (!ScriptedEncounters.TryGetValue(type, out var encounter)) continue;
                if (!encounter.ValidLocations.Contains(location)) continue;

                // Special checks for level-gated scripted encounters
                if (type == ScriptedEncounterType.TheMidgameLesson && (player.Level < 40 || EncountersHad < 3))
                    continue;
                if (type == ScriptedEncounterType.TheRevelation && (player.Level < 55 || EncountersHad < 5))
                    continue;

                return encounter;
            }
            return null;
        }

        /// <summary>
        /// Check if a random contextual encounter should trigger at this location.
        /// </summary>
        public bool ShouldTriggerRandomEncounter(string location, Character player)
        {
            // Don't trigger if a scripted encounter is pending
            if (PendingScriptedEncounters.Count > 0) return false;

            // Don't trigger too frequently
            if (_actionsSinceLastEncounter < MinActionsBetweenEncounters) return false;

            // Stop random encounters after revelation + PreFloor70 complete
            if (PlayerKnowsTruth && CompletedScriptedEncounters.Contains(ScriptedEncounterType.PreFloor70))
                return false;

            // Find valid disguises for this location
            var validDisguises = Disguises
                .Where(d => d.Value.ValidLocations.Contains(location) &&
                           player.Level >= d.Value.MinLevel)
                .ToList();
            if (!validDisguises.Any()) return false;

            // Base chance increases with player level
            float baseChance = 0.04f + (player.Level * 0.0015f);
            float cooldownFactor = Math.Min(1.0f, _actionsSinceLastEncounter / 40.0f);

            // Guaranteed minimum: boost if 0 encounters and level 5+
            if (EncountersHad == 0 && player.Level >= 5)
                baseChance *= 3.0f;

            // Recent game events boost chance
            if (RecentGameEvents.Count > 0)
                baseChance *= 1.5f;

            // After many encounters, reduce frequency
            if (EncountersHad > 8)
                baseChance *= 0.4f;

            return _random.NextDouble() < (baseChance * cooldownFactor);
        }

        /// <summary>
        /// Get a contextual random encounter for this location and player state.
        /// Returns null if no valid dialogue available.
        /// </summary>
        public StrangerEncounter? GetContextualEncounter(string location, Character player)
        {
            int awakening = OceanPhilosophySystem.Instance?.AwakeningLevel ?? 0;
            bool suspects = PlayerSuspectsStranger || player.Wisdom > 50 || EncountersHad >= 5;
            int encounterNum = EncountersHad + 1;

            // Find matching contextual dialogues
            var validDialogues = ContextualDialoguePool
                .Where(d => !UsedDialogueIds.Contains(d.Id) &&
                           encounterNum >= d.MinEncounters &&
                           encounterNum <= d.MaxEncounters &&
                           player.Level >= d.MinLevel &&
                           player.Level <= d.MaxLevel &&
                           awakening >= d.MinAwakening &&
                           (!d.RequiresSuspicion || suspects))
                .ToList();

            // Prioritize event-specific dialogues
            ContextualStrangerDialogue? chosen = null;
            if (RecentGameEvents.Count > 0)
            {
                var eventSpecific = validDialogues
                    .Where(d => d.RequiredRecentEvent.HasValue &&
                               RecentGameEvents.Contains(d.RequiredRecentEvent.Value))
                    .ToList();
                if (eventSpecific.Count > 0)
                    chosen = eventSpecific[_random.Next(eventSpecific.Count)];
            }

            // Fall back to general pool
            if (chosen == null)
            {
                var general = validDialogues.Where(d => !d.RequiredRecentEvent.HasValue).ToList();
                if (general.Count > 0)
                    chosen = general[_random.Next(general.Count)];
            }

            // Last resort: any valid dialogue
            if (chosen == null && validDialogues.Count > 0)
                chosen = validDialogues[_random.Next(validDialogues.Count)];

            if (chosen == null) return null;

            // Pick a disguise for the encounter
            var validDisguises = Disguises
                .Where(d => d.Value.ValidLocations.Contains(location) &&
                           player.Level >= d.Value.MinLevel)
                .OrderByDescending(d => EncounteredDisguises.Contains(d.Key) ? 0 : 1)
                .ThenBy(_ => _random.Next())
                .ToList();

            if (!validDisguises.Any()) return null;

            var disguise = validDisguises.First().Key;
            var disguiseData = Disguises[disguise];

            return new StrangerEncounter
            {
                Disguise = disguise,
                DisguiseData = disguiseData,
                Dialogue = string.Join("\n", chosen.Dialogue),
                Location = location,
                EncounterNumber = encounterNum,
                PlayerSuspects = suspects,
                ContextualDialogueId = chosen.Id,
                ResponseOptions = chosen.Responses
            };
        }

        /// <summary>
        /// Record an encounter with the player's response choice.
        /// </summary>
        public void RecordEncounterWithResponse(StrangerEncounter encounter, StrangerResponseType responseType, int receptivityChange)
        {
            EncountersHad++;
            _actionsSinceLastEncounter = 0;
            EncounteredDisguises.Add(encounter.Disguise);
            EncounterLog.Add(encounter);

            // Track response
            ResponseHistory[EncountersHad] = responseType;

            // Update receptivity (clamped to -100..+100)
            Receptivity = Math.Clamp(Receptivity + receptivityChange, -100, 100);

            // Mark contextual dialogue as used
            if (!string.IsNullOrEmpty(encounter.ContextualDialogueId))
                UsedDialogueIds.Add(encounter.ContextualDialogueId);

            // Clear recent events after encounter (she acknowledged them)
            RecentGameEvents.Clear();

            // Set story flag on first encounter
            if (EncountersHad == 1)
                StoryProgressionSystem.Instance?.SetFlag(StoryFlag.MetStranger);

            // After enough encounters, player starts to suspect
            if (EncountersHad >= 4 && !PlayerSuspectsStranger)
                PlayerSuspectsStranger = true;

            GD.Print($"[Stranger] Encounter #{EncountersHad} recorded. Response: {responseType}, Receptivity: {Receptivity}");
        }

        /// <summary>
        /// Complete a scripted encounter (remove from pending, add to completed).
        /// </summary>
        public void CompleteScriptedEncounter(ScriptedEncounterType type, StrangerResponseType responseType, int receptivityChange)
        {
            PendingScriptedEncounters.Remove(type);
            CompletedScriptedEncounters.Add(type);

            // Use the same recording logic
            var disguise = ScriptedEncounters.TryGetValue(type, out var enc) ? enc.Disguise : StrangerDisguise.HoodedTraveler;
            var encounter = new StrangerEncounter
            {
                Disguise = disguise,
                DisguiseData = Disguises[disguise],
                Dialogue = $"[Scripted: {type}]",
                Location = "Scripted",
                EncounterNumber = EncountersHad + 1,
                PlayerSuspects = PlayerSuspectsStranger
            };

            RecordEncounterWithResponse(encounter, responseType, receptivityChange);

            // Revelation sets PlayerKnowsTruth
            if (type == ScriptedEncounterType.TheRevelation && responseType != StrangerResponseType.Hostile)
            {
                RevealTruth();
            }
            // Even hostile response at Revelation sets suspicion
            if (type == ScriptedEncounterType.TheRevelation)
            {
                PlayerSuspectsStranger = true;
            }

            GD.Print($"[Stranger] Scripted encounter completed: {type}");
        }

        /// <summary>
        /// Reveal the Stranger's true identity
        /// </summary>
        public void RevealTruth()
        {
            PlayerKnowsTruth = true;
            StoryProgressionSystem.Instance?.SetFlag(StoryFlag.KnowsNocturaTruth);
            GD.Print("[Stranger] Player now knows Noctura's true identity");
        }

        // ═══════════════════════════════════════════════════════════════════
        // LEGACY COMPATIBILITY - kept for existing code that calls these
        // ═══════════════════════════════════════════════════════════════════

        /// <summary>
        /// Legacy: Check if encounter should trigger (now delegates to random check).
        /// </summary>
        public bool ShouldTriggerEncounter(string location, Character player)
        {
            return ShouldTriggerRandomEncounter(location, player);
        }

        /// <summary>
        /// Legacy: Get encounter (now delegates to contextual encounter).
        /// </summary>
        public StrangerEncounter? GetEncounter(string location, Character player)
        {
            return GetContextualEncounter(location, player);
        }

        /// <summary>
        /// Legacy: Record encounter without response (defaults to Silent).
        /// </summary>
        public void RecordEncounter(StrangerEncounter encounter)
        {
            RecordEncounterWithResponse(encounter, StrangerResponseType.Silent, 0);
        }

        /// <summary>
        /// Legacy: Get response options for contextual encounters.
        /// </summary>
        public List<(string key, string text, string response)> GetResponseOptions(StrangerEncounter encounter, Character player)
        {
            var options = new List<(string, string, string)>();

            if (encounter.ResponseOptions != null && encounter.ResponseOptions.Count > 0)
            {
                foreach (var opt in encounter.ResponseOptions)
                {
                    options.Add((opt.Key, opt.Text, string.Join(" ", opt.StrangerReply)));
                }
            }
            else
            {
                // Fallback for encounters without response options
                options.Add(("1", "Listen carefully",
                    "\"Good. Listening is the first step.\""));
                options.Add(("2", "Ask what they mean",
                    "\"Meaning is a luxury. Understanding comes later.\""));
                options.Add(("0", "Say nothing and leave",
                    "They watch you go with an expression that might be amusement, or might be sorrow."));
            }

            return options;
        }

        // ═══════════════════════════════════════════════════════════════════
        // SERIALIZATION
        // ═══════════════════════════════════════════════════════════════════

        public StrangerEncounterData Serialize()
        {
            return new StrangerEncounterData
            {
                EncountersHad = EncountersHad,
                EncounteredDisguises = EncounteredDisguises.Cast<int>().ToList(),
                PlayerSuspectsStranger = PlayerSuspectsStranger,
                PlayerKnowsTruth = PlayerKnowsTruth,
                ActionsSinceLastEncounter = _actionsSinceLastEncounter,
                Receptivity = Receptivity,
                ResponseHistory = ResponseHistory.ToDictionary(kvp => kvp.Key, kvp => (int)kvp.Value),
                CompletedScriptedEncounters = CompletedScriptedEncounters.Cast<int>().ToList(),
                PendingScriptedEncounters = PendingScriptedEncounters.Cast<int>().ToList(),
                UsedDialogueIds = UsedDialogueIds.ToList(),
                RecentGameEvents = RecentGameEvents.Cast<int>().ToList()
            };
        }

        public void Deserialize(StrangerEncounterData? data)
        {
            if (data == null) return;

            EncountersHad = data.EncountersHad;
            EncounteredDisguises = new HashSet<StrangerDisguise>(
                data.EncounteredDisguises.Cast<StrangerDisguise>());
            PlayerSuspectsStranger = data.PlayerSuspectsStranger;
            PlayerKnowsTruth = data.PlayerKnowsTruth;
            _actionsSinceLastEncounter = data.ActionsSinceLastEncounter;

            // New fields with migration
            Receptivity = data.Receptivity;
            ResponseHistory = (data.ResponseHistory ?? new())
                .ToDictionary(kvp => kvp.Key, kvp => (StrangerResponseType)kvp.Value);
            CompletedScriptedEncounters = new HashSet<ScriptedEncounterType>(
                (data.CompletedScriptedEncounters ?? new()).Cast<ScriptedEncounterType>());
            PendingScriptedEncounters = new HashSet<ScriptedEncounterType>(
                (data.PendingScriptedEncounters ?? new()).Cast<ScriptedEncounterType>());
            UsedDialogueIds = new HashSet<string>(data.UsedDialogueIds ?? new());
            RecentGameEvents = (data.RecentGameEvents ?? new())
                .Select(e => (StrangerContextEvent)e).ToList();

            // MIGRATION: If old save had encounters but no receptivity data,
            // estimate receptivity based on encounter count
            if (EncountersHad > 0 && data.Receptivity == 0 &&
                (data.ResponseHistory == null || data.ResponseHistory.Count == 0))
            {
                Receptivity = EncountersHad * 5;
                if (PlayerKnowsTruth)
                    Receptivity = Math.Max(Receptivity, 40);

                GD.Print($"[Stranger] Migration: Estimated receptivity at {Receptivity} for {EncountersHad} encounters");
            }
        }

        public void Reset()
        {
            EncountersHad = 0;
            EncounteredDisguises = new HashSet<StrangerDisguise>();
            EncounterLog = new List<StrangerEncounter>();
            PlayerSuspectsStranger = false;
            PlayerKnowsTruth = false;
            _actionsSinceLastEncounter = 0;
            Receptivity = 0;
            ResponseHistory = new Dictionary<int, StrangerResponseType>();
            CompletedScriptedEncounters = new HashSet<ScriptedEncounterType>();
            PendingScriptedEncounters = new HashSet<ScriptedEncounterType>();
            UsedDialogueIds = new HashSet<string>();
            RecentGameEvents = new List<StrangerContextEvent>();
            GD.Print("[Stranger] System reset for new game");
        }
    }

    #region Data Classes

    public enum StrangerDisguise
    {
        HoodedTraveler,
        OldBeggar,
        TavernPatron,
        MysteriousMerchant,
        WoundedKnight,
        DreamVisitor,
        TempleSupplicant,
        ShadowInMirror,
        TrueForm
    }

    public enum StrangerResponseType
    {
        Accepted,       // Explicitly accepts teaching (+15 receptivity)
        Reflective,     // Thoughtful consideration (+12)
        Engaged,        // Asks questions, shows interest (+8)
        Challenged,     // Pushes back respectfully (+5)
        Silent,         // Says nothing (0)
        Dismissed,      // Brushes off (-5)
        Hostile         // Threatens (-10)
    }

    public enum ScriptedEncounterType
    {
        AfterFirstDeath,
        AfterCompanionDeath,
        AfterFirstOldGod,
        TheMidgameLesson,
        TheRevelation,
        PreFloor70
    }

    public enum StrangerContextEvent
    {
        PlayerDied,
        CompanionDied,
        OldGodDefeated,
        OldGodSaved,
        SealCollected,
        WaveFragmentCollected,
        GriefStageChanged
    }

    public class StrangerDisguiseData
    {
        public string Name { get; }
        public string Description { get; }
        public string[] ValidLocations { get; }
        public int MinLevel { get; }
        public int MaxLevel { get; }

        public StrangerDisguiseData(string name, string description, string[] locations, int minLevel, int maxLevel)
        {
            Name = name;
            Description = description;
            ValidLocations = locations;
            MinLevel = minLevel;
            MaxLevel = maxLevel;
        }
    }

    public class StrangerResponseOption
    {
        public string Key { get; set; } = "";
        public string Text { get; set; } = "";
        public string[] StrangerReply { get; set; } = Array.Empty<string>();
        public StrangerResponseType ResponseType { get; set; }
        public int ReceptivityChange { get; set; }
    }

    public class ScriptedStrangerEncounter
    {
        public ScriptedEncounterType Type { get; set; }
        public StrangerDisguise Disguise { get; set; }
        public string Title { get; set; } = "";
        public string[] ValidLocations { get; set; } = Array.Empty<string>();
        public string[] IntroNarration { get; set; } = Array.Empty<string>();
        public string[] Dialogue { get; set; } = Array.Empty<string>();
        public string[] ClosingNarration { get; set; } = Array.Empty<string>();
        public List<StrangerResponseOption> Responses { get; set; } = new();
    }

    public class ContextualStrangerDialogue
    {
        public string Id { get; set; } = "";
        public string[] Dialogue { get; set; } = Array.Empty<string>();
        public List<StrangerResponseOption> Responses { get; set; } = new();
        public int MinEncounters { get; set; }
        public int MaxEncounters { get; set; } = 99;
        public int MinLevel { get; set; }
        public int MaxLevel { get; set; } = 100;
        public StrangerContextEvent? RequiredRecentEvent { get; set; }
        public bool RequiresSuspicion { get; set; }
        public int MinAwakening { get; set; }
    }

    public class StrangerEncounter
    {
        public StrangerDisguise Disguise { get; set; }
        public StrangerDisguiseData DisguiseData { get; set; } = null!;
        public string Dialogue { get; set; } = "";
        public string Location { get; set; } = "";
        public int EncounterNumber { get; set; }
        public bool PlayerSuspects { get; set; }
        public DateTime Timestamp { get; set; } = DateTime.Now;
        // New fields for the overhaul
        public string? ContextualDialogueId { get; set; }
        public List<StrangerResponseOption>? ResponseOptions { get; set; }
    }

    public class StrangerEncounterData
    {
        // Original fields (backward compatible)
        public int EncountersHad { get; set; }
        public List<int> EncounteredDisguises { get; set; } = new();
        public bool PlayerSuspectsStranger { get; set; }
        public bool PlayerKnowsTruth { get; set; }
        public int ActionsSinceLastEncounter { get; set; }

        // New fields for storyline overhaul
        public int Receptivity { get; set; }
        public Dictionary<int, int> ResponseHistory { get; set; } = new();
        public List<int> CompletedScriptedEncounters { get; set; } = new();
        public List<int> PendingScriptedEncounters { get; set; } = new();
        public List<string> UsedDialogueIds { get; set; } = new();
        public List<int> RecentGameEvents { get; set; } = new();
    }

    #endregion
}
