using System;
using System.Collections.Generic;
using System.Linq;

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
                "A cloaked figure with strange, pale eyes",
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
                new[] { "AuctionHouse", "MagicShop", "DarkAlley" },
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
                        StrangerReply = new[] { "\"Good. Fear means you learned something.\"" } },
                    new() { Key = "2", Text = "There was... a moment of peace. Before the pain.",
                        ResponseType = StrangerResponseType.Reflective, ReceptivityChange = 12,
                        StrangerReply = new[] { "\"Hm. You felt the quiet part. Most people only get the screaming.\"", "\"Hold onto that.\"" } },
                    new() { Key = "3", Text = "I don't remember anything. Just darkness.",
                        ResponseType = StrangerResponseType.Silent, ReceptivityChange = 0,
                        StrangerReply = new[] { "\"Perhaps. Or perhaps you chose to forget. The dying often do.\"" } },
                    new() { Key = "4", Text = "Death is for the weak. I won't make the same mistake.",
                        ResponseType = StrangerResponseType.Hostile, ReceptivityChange = -10,
                        StrangerReply = new[] { "*She just looks at you.*", "\"Right. You got all the answers already, dont you.\"" } },
                    new() { Key = "5", Text = "Who are you? Why do you care about my death?",
                        ResponseType = StrangerResponseType.Challenged, ReceptivityChange = 5,
                        StrangerReply = new[] { "\"I care about all deaths. And all returns.\"", "\"They are... my specialty.\"" } }
                }
            },

            [ScriptedEncounterType.AfterCompanionDeath] = new ScriptedStrangerEncounter
            {
                Type = ScriptedEncounterType.AfterCompanionDeath,
                Disguise = StrangerDisguise.WoundedKnight,
                Title = "THE EMPTY CHAIR",
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
                    "\"Death took them. Thats... thats rough.\"",
                    "\"I wont pretend to make it better.\"",
                    "\"But I know a thing or two about losing people.\""
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
                        StrangerReply = new[] { "\"Yeah. It is. Doesnt care who deserves what.\"", "\"Never has.\"" } },
                    new() { Key = "2", Text = "I think... they're at peace now. I hope so.",
                        ResponseType = StrangerResponseType.Reflective, ReceptivityChange = 12,
                        StrangerReply = new[] { "*Her eyes soften.*", "\"They are. Trust me on that one.\"" } },
                    new() { Key = "3", Text = "I'll honor them by growing stronger. Their sacrifice means something.",
                        ResponseType = StrangerResponseType.Accepted, ReceptivityChange = 15,
                        StrangerReply = new[] { "\"Good. Thats a good way to carry it.\"", "\"They'd want that, I think.\"" } },
                    new() { Key = "4", Text = "I don't need your philosophy. I need them back.",
                        ResponseType = StrangerResponseType.Dismissed, ReceptivityChange = -5,
                        StrangerReply = new[] { "\"I know. Believe me, I know.\"", "\"You'll get through it. Eventually.\"" } },
                    new() { Key = "5", Text = "Get away from me.",
                        ResponseType = StrangerResponseType.Hostile, ReceptivityChange = -10,
                        StrangerReply = new[] { "*She doesnt move.*", "\"Fair enough. Ill leave you alone.\"" } }
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
                        StrangerReply = new[] { "\"You took a piece of them. Thats what happens when a god dies.\"", "\"Part of them ends up in whoever did the killing.\"" } },
                    new() { Key = "2", Text = "The gods are prisoners, not enemies. Their deaths trouble me.",
                        ResponseType = StrangerResponseType.Reflective, ReceptivityChange = 12,
                        StrangerReply = new[] { "\"Good. It should trouble you.\"", "\"Day it stops bothering you is the day you've got a problem.\"" } },
                    new() { Key = "3", Text = "Every god I face teaches me something. Even in destruction.",
                        ResponseType = StrangerResponseType.Accepted, ReceptivityChange = 15,
                        StrangerReply = new[] { "*Something like approval crosses her face.*", "\"Yeah. You're getting it.\"" } },
                    new() { Key = "4", Text = "I did what I had to. Don't make it philosophical.",
                        ResponseType = StrangerResponseType.Dismissed, ReceptivityChange = -5,
                        StrangerReply = new[] { "\"Everything is philosophical whether you like it or not.\"", "\"Even pretending its not is a kind of philosophy.\"" } }
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
                        StrangerReply = new[] { "\"Took you long enough.\"", "\"But yeah. You're getting there.\"" } },
                    new() { Key = "2", Text = "You keep appearing. Who ARE you, really?",
                        ResponseType = StrangerResponseType.Challenged, ReceptivityChange = 5,
                        StrangerReply = new[] { "\"You'll find out. When you're ready.\"", "\"And you're not ready yet.\"" } },
                    new() { Key = "3", Text = "Beautiful metaphor. But my friends are still dead.",
                        ResponseType = StrangerResponseType.Engaged, ReceptivityChange = 8,
                        StrangerReply = new[] { "\"Yeah. They are. And you're still here because of them.\"", "\"Make it count.\"" } },
                    new() { Key = "4", Text = "I'm tired of your riddles, Stranger.",
                        ResponseType = StrangerResponseType.Dismissed, ReceptivityChange = -5,
                        StrangerReply = new[] { "*She shrugs.*", "\"Suit yourself. Im not going anywhere.\"" } }
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
                    "The shadows pull back. Shes gone.",
                    "But you know her name now. And you wont forget it."
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
                            "\"Because Manwe sent pieces of himself out to live as mortals.\"",
                            "\"To learn what it felt like. Most of them forget everything.\"",
                            "\"You didnt. Thats why.\""
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
                            "*She shrugs.*",
                            "\"You can hate me all you want. Doesnt change anything.\"",
                            "\"Floor 70. Ill be there whether you like it or not.\""
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
                    "\"Almost there now.\"",
                    "\"When you find me, you'll have a choice.\"",
                    "\"You can fight me. Plenty have tried. It goes badly.\"",
                    "\"Or you can use what Ive been teaching you.\"",
                    "\"Up to you.\""
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
                        StrangerReply = new[] { "\"Good. Then maybe we can skip the fighting part.\"" } },
                    new() { Key = "2", Text = "I hear your words. But I will decide when I face you.",
                        ResponseType = StrangerResponseType.Reflective, ReceptivityChange = 12,
                        StrangerReply = new[] { "\"Fair. Talk is cheap anyway.\"" } },
                    new() { Key = "3", Text = "Whatever happens, I will face you without fear.",
                        ResponseType = StrangerResponseType.Challenged, ReceptivityChange = 5,
                        StrangerReply = new[] { "\"Huh. Brave or stupid. Guess we'll find out.\"" } }
                }
            }
        };

        // ═══════════════════════════════════════════════════════════════════
        // CONTEXTUAL DIALOGUE POOL - non-repeating random encounters
        // ═══════════════════════════════════════════════════════════════════

        private static readonly List<ContextualStrangerDialogue> ContextualDialoguePool = new()
        {
            // ── EARLY GAME (encounters 1-4, levels 1-25): Death Omens ──

            // v0.61.2 (player report: encounter dialogue not translated). The Dialogue
            // and StrangerReply string[] fields and Text field now contain localization
            // KEYS instead of literal English. The consumption sites (GenerateEncounter
            // line ~843 and GetResponseOptions line ~968) call Loc.Get on each element
            // before rendering, so dialogue translates in the current player's session
            // language. The literal English lives only in en.json under stranger.*
            // namespace; other languages inherit until translation pass runs.
            new() { Id = "ctx_early_graveyard", MinEncounters = 1, MaxEncounters = 4, MinLevel = 1, MaxLevel = 30,
                Dialogue = new[] {
                    "stranger.ctx_early_graveyard.dialogue.1",
                    "stranger.ctx_early_graveyard.dialogue.2",
                    "stranger.ctx_early_graveyard.dialogue.3"
                },
                Responses = new List<StrangerResponseOption> {
                    new() { Key = "1", Text = "stranger.ctx_early_graveyard.response.1.text", ResponseType = StrangerResponseType.Engaged, ReceptivityChange = 8,
                        StrangerReply = new[] { "stranger.ctx_early_graveyard.response.1.reply.1" } },
                    new() { Key = "2", Text = "stranger.ctx_early_graveyard.response.2.text", ResponseType = StrangerResponseType.Reflective, ReceptivityChange = 12,
                        StrangerReply = new[] { "stranger.ctx_early_graveyard.response.2.reply.1" } },
                    new() { Key = "3", Text = "stranger.ctx_early_graveyard.response.3.text", ResponseType = StrangerResponseType.Dismissed, ReceptivityChange = -5,
                        StrangerReply = new[] { "stranger.ctx_early_graveyard.response.3.reply.1" } },
                    new() { Key = "0", Text = "stranger.ctx_early_graveyard.response.0.text", ResponseType = StrangerResponseType.Silent, ReceptivityChange = 0,
                        StrangerReply = new[] { "stranger.ctx_early_graveyard.response.0.reply.1" } }
                }
            },

            new() { Id = "ctx_early_seasons", MinEncounters = 1, MaxEncounters = 4, MinLevel = 1, MaxLevel = 30,
                Dialogue = new[] {
                    "stranger.ctx_early_seasons.dialogue.1",
                    "stranger.ctx_early_seasons.dialogue.2",
                    "stranger.ctx_early_seasons.dialogue.3"
                },
                Responses = new List<StrangerResponseOption> {
                    new() { Key = "1", Text = "stranger.ctx_early_seasons.response.1.text", ResponseType = StrangerResponseType.Reflective, ReceptivityChange = 12,
                        StrangerReply = new[] { "stranger.ctx_early_seasons.response.1.reply.1" } },
                    new() { Key = "2", Text = "stranger.ctx_early_seasons.response.2.text", ResponseType = StrangerResponseType.Challenged, ReceptivityChange = 5,
                        StrangerReply = new[] { "stranger.ctx_early_seasons.response.2.reply.1" } },
                    new() { Key = "0", Text = "stranger.ctx_early_seasons.response.0.text", ResponseType = StrangerResponseType.Silent, ReceptivityChange = 0,
                        StrangerReply = new[] { "stranger.ctx_early_seasons.response.0.reply.1" } }
                }
            },

            new() { Id = "ctx_early_dormitory", MinEncounters = 1, MaxEncounters = 3, MinLevel = 1, MaxLevel = 20,
                Dialogue = new[] {
                    "stranger.ctx_early_dormitory.dialogue.1",
                    "stranger.ctx_early_dormitory.dialogue.2",
                    "stranger.ctx_early_dormitory.dialogue.3"
                },
                Responses = new List<StrangerResponseOption> {
                    new() { Key = "1", Text = "stranger.ctx_early_dormitory.response.1.text", ResponseType = StrangerResponseType.Reflective, ReceptivityChange = 12,
                        StrangerReply = new[] { "stranger.ctx_early_dormitory.response.1.reply.1", "stranger.ctx_early_dormitory.response.1.reply.2" } },
                    new() { Key = "2", Text = "stranger.ctx_early_dormitory.response.2.text", ResponseType = StrangerResponseType.Challenged, ReceptivityChange = 5,
                        StrangerReply = new[] { "stranger.ctx_early_dormitory.response.2.reply.1" } },
                    new() { Key = "0", Text = "stranger.ctx_early_dormitory.response.0.text", ResponseType = StrangerResponseType.Silent, ReceptivityChange = 0,
                        StrangerReply = new[] { "stranger.ctx_early_dormitory.response.0.reply.1" } }
                }
            },

            new() { Id = "ctx_early_after_death", MinEncounters = 1, MaxEncounters = 5, MinLevel = 1, MaxLevel = 40,
                RequiredRecentEvent = StrangerContextEvent.PlayerDied,
                Dialogue = new[] {
                    "stranger.ctx_early_after_death.dialogue.1",
                    "stranger.ctx_early_after_death.dialogue.2"
                },
                Responses = new List<StrangerResponseOption> {
                    new() { Key = "1", Text = "stranger.ctx_early_after_death.response.1.text", ResponseType = StrangerResponseType.Challenged, ReceptivityChange = 5,
                        StrangerReply = new[] { "stranger.ctx_early_after_death.response.1.reply.1" } },
                    new() { Key = "2", Text = "stranger.ctx_early_after_death.response.2.text", ResponseType = StrangerResponseType.Reflective, ReceptivityChange = 12,
                        StrangerReply = new[] { "stranger.ctx_early_after_death.response.2.reply.1" } },
                    new() { Key = "0", Text = "stranger.ctx_early_after_death.response.0.text", ResponseType = StrangerResponseType.Silent, ReceptivityChange = 0,
                        StrangerReply = new[] { "stranger.ctx_early_after_death.response.0.reply.1" } }
                }
            },

            new() { Id = "ctx_early_candle", MinEncounters = 2, MaxEncounters = 5, MinLevel = 5, MaxLevel = 35,
                Dialogue = new[] {
                    "stranger.ctx_early_candle.dialogue.1",
                    "stranger.ctx_early_candle.dialogue.2",
                    "stranger.ctx_early_candle.dialogue.3"
                },
                Responses = new List<StrangerResponseOption> {
                    new() { Key = "1", Text = "stranger.ctx_early_candle.response.1.text", ResponseType = StrangerResponseType.Accepted, ReceptivityChange = 15,
                        StrangerReply = new[] { "stranger.ctx_early_candle.response.1.reply.1", "stranger.ctx_early_candle.response.1.reply.2" } },
                    new() { Key = "2", Text = "stranger.ctx_early_candle.response.2.text", ResponseType = StrangerResponseType.Dismissed, ReceptivityChange = -5,
                        StrangerReply = new[] { "stranger.ctx_early_candle.response.2.reply.1", "stranger.ctx_early_candle.response.2.reply.2" } },
                    new() { Key = "0", Text = "stranger.ctx_early_candle.response.0.text", ResponseType = StrangerResponseType.Silent, ReceptivityChange = 0,
                        StrangerReply = new[] { "stranger.ctx_early_candle.response.0.reply.1" } }
                }
            },

            // ── MID GAME (encounters 5-7, levels 20-50): The Teacher ──

            new() { Id = "ctx_mid_wave", MinEncounters = 4, MaxEncounters = 8, MinLevel = 15, MaxLevel = 55,
                Dialogue = new[] {
                    "stranger.ctx_mid_wave.dialogue.1",
                    "stranger.ctx_mid_wave.dialogue.2",
                    "stranger.ctx_mid_wave.dialogue.3",
                    "stranger.ctx_mid_wave.dialogue.4"
                },
                Responses = new List<StrangerResponseOption> {
                    new() { Key = "1", Text = "stranger.ctx_mid_wave.response.1.text", ResponseType = StrangerResponseType.Reflective, ReceptivityChange = 12,
                        StrangerReply = new[] { "stranger.ctx_mid_wave.response.1.reply.1" } },
                    new() { Key = "2", Text = "stranger.ctx_mid_wave.response.2.text", ResponseType = StrangerResponseType.Engaged, ReceptivityChange = 8,
                        StrangerReply = new[] { "stranger.ctx_mid_wave.response.2.reply.1" } },
                    new() { Key = "0", Text = "stranger.ctx_mid_wave.response.0.text", ResponseType = StrangerResponseType.Silent, ReceptivityChange = 0,
                        StrangerReply = new[] { "stranger.ctx_mid_wave.response.0.reply.1" } }
                }
            },

            new() { Id = "ctx_mid_after_god", MinEncounters = 4, MaxEncounters = 10, MinLevel = 20, MaxLevel = 70,
                RequiredRecentEvent = StrangerContextEvent.OldGodDefeated,
                Dialogue = new[] {
                    "stranger.ctx_mid_after_god.dialogue.1",
                    "stranger.ctx_mid_after_god.dialogue.2",
                    "stranger.ctx_mid_after_god.dialogue.3"
                },
                Responses = new List<StrangerResponseOption> {
                    new() { Key = "1", Text = "stranger.ctx_mid_after_god.response.1.text", ResponseType = StrangerResponseType.Engaged, ReceptivityChange = 8,
                        StrangerReply = new[] { "stranger.ctx_mid_after_god.response.1.reply.1", "stranger.ctx_mid_after_god.response.1.reply.2" } },
                    new() { Key = "2", Text = "stranger.ctx_mid_after_god.response.2.text", ResponseType = StrangerResponseType.Reflective, ReceptivityChange = 12,
                        StrangerReply = new[] { "stranger.ctx_mid_after_god.response.2.reply.1" } },
                    new() { Key = "0", Text = "stranger.ctx_mid_after_god.response.0.text", ResponseType = StrangerResponseType.Silent, ReceptivityChange = 0,
                        StrangerReply = new[] { "stranger.ctx_mid_after_god.response.0.reply.1" } }
                }
            },

            new() { Id = "ctx_mid_companion_grief", MinEncounters = 3, MaxEncounters = 10, MinLevel = 10, MaxLevel = 70,
                RequiredRecentEvent = StrangerContextEvent.CompanionDied,
                Dialogue = new[] {
                    "stranger.ctx_mid_companion_grief.dialogue.1",
                    "stranger.ctx_mid_companion_grief.dialogue.2",
                    "stranger.ctx_mid_companion_grief.dialogue.3"
                },
                Responses = new List<StrangerResponseOption> {
                    new() { Key = "1", Text = "stranger.ctx_mid_companion_grief.response.1.text", ResponseType = StrangerResponseType.Engaged, ReceptivityChange = 8,
                        StrangerReply = new[] { "stranger.ctx_mid_companion_grief.response.1.reply.1" } },
                    new() { Key = "2", Text = "stranger.ctx_mid_companion_grief.response.2.text", ResponseType = StrangerResponseType.Accepted, ReceptivityChange = 15,
                        StrangerReply = new[] { "stranger.ctx_mid_companion_grief.response.2.reply.1" } },
                    new() { Key = "0", Text = "stranger.ctx_mid_companion_grief.response.0.text", ResponseType = StrangerResponseType.Silent, ReceptivityChange = 0,
                        StrangerReply = new[] { "stranger.ctx_mid_companion_grief.response.0.reply.1" } }
                }
            },

            new() { Id = "ctx_mid_phoenix", MinEncounters = 4, MaxEncounters = 8, MinLevel = 20, MaxLevel = 50,
                Dialogue = new[] {
                    "stranger.ctx_mid_phoenix.dialogue.1",
                    "stranger.ctx_mid_phoenix.dialogue.2",
                    "stranger.ctx_mid_phoenix.dialogue.3",
                    "stranger.ctx_mid_phoenix.dialogue.4"
                },
                Responses = new List<StrangerResponseOption> {
                    new() { Key = "1", Text = "stranger.ctx_mid_phoenix.response.1.text", ResponseType = StrangerResponseType.Accepted, ReceptivityChange = 15,
                        StrangerReply = new[] { "stranger.ctx_mid_phoenix.response.1.reply.1" } },
                    new() { Key = "2", Text = "stranger.ctx_mid_phoenix.response.2.text", ResponseType = StrangerResponseType.Challenged, ReceptivityChange = 5,
                        StrangerReply = new[] { "stranger.ctx_mid_phoenix.response.2.reply.1", "stranger.ctx_mid_phoenix.response.2.reply.2" } },
                    new() { Key = "0", Text = "stranger.ctx_mid_phoenix.response.0.text", ResponseType = StrangerResponseType.Silent, ReceptivityChange = 0,
                        StrangerReply = new[] { "stranger.ctx_mid_phoenix.response.0.reply.1" } }
                }
            },

            new() { Id = "ctx_mid_sleep", MinEncounters = 4, MaxEncounters = 8, MinLevel = 20, MaxLevel = 55,
                Dialogue = new[] {
                    "stranger.ctx_mid_sleep.dialogue.1",
                    "stranger.ctx_mid_sleep.dialogue.2",
                    "stranger.ctx_mid_sleep.dialogue.3",
                    "stranger.ctx_mid_sleep.dialogue.4"
                },
                Responses = new List<StrangerResponseOption> {
                    new() { Key = "1", Text = "stranger.ctx_mid_sleep.response.1.text", ResponseType = StrangerResponseType.Reflective, ReceptivityChange = 12,
                        StrangerReply = new[] { "stranger.ctx_mid_sleep.response.1.reply.1" } },
                    new() { Key = "2", Text = "stranger.ctx_mid_sleep.response.2.text", ResponseType = StrangerResponseType.Challenged, ReceptivityChange = 5,
                        StrangerReply = new[] { "stranger.ctx_mid_sleep.response.2.reply.1", "stranger.ctx_mid_sleep.response.2.reply.2", "stranger.ctx_mid_sleep.response.2.reply.3" } },
                    new() { Key = "0", Text = "stranger.ctx_mid_sleep.response.0.text", ResponseType = StrangerResponseType.Silent, ReceptivityChange = 0,
                        StrangerReply = new[] { "stranger.ctx_mid_sleep.response.0.reply.1" } }
                }
            },

            // ── LATE GAME (encounters 8+, levels 45+): The Bridge ──

            new() { Id = "ctx_late_cycle", MinEncounters = 7, MinLevel = 40, MinAwakening = 2,
                Dialogue = new[] {
                    "stranger.ctx_late_cycle.dialogue.1",
                    "stranger.ctx_late_cycle.dialogue.2",
                    "stranger.ctx_late_cycle.dialogue.3",
                    "stranger.ctx_late_cycle.dialogue.4"
                },
                Responses = new List<StrangerResponseOption> {
                    new() { Key = "1", Text = "stranger.ctx_late_cycle.response.1.text", ResponseType = StrangerResponseType.Engaged, ReceptivityChange = 8,
                        StrangerReply = new[] { "stranger.ctx_late_cycle.response.1.reply.1", "stranger.ctx_late_cycle.response.1.reply.2" } },
                    new() { Key = "2", Text = "stranger.ctx_late_cycle.response.2.text", ResponseType = StrangerResponseType.Reflective, ReceptivityChange = 12,
                        StrangerReply = new[] { "stranger.ctx_late_cycle.response.2.reply.1", "stranger.ctx_late_cycle.response.2.reply.2" } },
                    new() { Key = "0", Text = "stranger.ctx_late_cycle.response.0.text", ResponseType = StrangerResponseType.Silent, ReceptivityChange = 0,
                        StrangerReply = new[] { "stranger.ctx_late_cycle.response.0.reply.1" } }
                }
            },

            new() { Id = "ctx_late_identity", MinEncounters = 7, MinLevel = 45, MinAwakening = 3,
                Dialogue = new[] {
                    "stranger.ctx_late_identity.dialogue.1",
                    "stranger.ctx_late_identity.dialogue.2",
                    "stranger.ctx_late_identity.dialogue.3",
                    "stranger.ctx_late_identity.dialogue.4",
                    "stranger.ctx_late_identity.dialogue.5"
                },
                Responses = new List<StrangerResponseOption> {
                    new() { Key = "1", Text = "stranger.ctx_late_identity.response.1.text", ResponseType = StrangerResponseType.Reflective, ReceptivityChange = 12,
                        StrangerReply = new[] { "stranger.ctx_late_identity.response.1.reply.1", "stranger.ctx_late_identity.response.1.reply.2" } },
                    new() { Key = "2", Text = "stranger.ctx_late_identity.response.2.text", ResponseType = StrangerResponseType.Accepted, ReceptivityChange = 15,
                        StrangerReply = new[] { "stranger.ctx_late_identity.response.2.reply.1", "stranger.ctx_late_identity.response.2.reply.2" } },
                    new() { Key = "0", Text = "stranger.ctx_late_identity.response.0.text", ResponseType = StrangerResponseType.Silent, ReceptivityChange = 0,
                        StrangerReply = new[] { "stranger.ctx_late_identity.response.0.reply.1" } }
                }
            },

            new() { Id = "ctx_late_after_seal", MinEncounters = 5, MinLevel = 30,
                RequiredRecentEvent = StrangerContextEvent.SealCollected,
                Dialogue = new[] {
                    "stranger.ctx_late_after_seal.dialogue.1",
                    "stranger.ctx_late_after_seal.dialogue.2",
                    "stranger.ctx_late_after_seal.dialogue.3",
                    "stranger.ctx_late_after_seal.dialogue.4"
                },
                Responses = new List<StrangerResponseOption> {
                    new() { Key = "1", Text = "stranger.ctx_late_after_seal.response.1.text", ResponseType = StrangerResponseType.Reflective, ReceptivityChange = 12,
                        StrangerReply = new[] { "stranger.ctx_late_after_seal.response.1.reply.1", "stranger.ctx_late_after_seal.response.1.reply.2" } },
                    new() { Key = "2", Text = "stranger.ctx_late_after_seal.response.2.text", ResponseType = StrangerResponseType.Engaged, ReceptivityChange = 8,
                        StrangerReply = new[] { "stranger.ctx_late_after_seal.response.2.reply.1", "stranger.ctx_late_after_seal.response.2.reply.2" } },
                    new() { Key = "0", Text = "stranger.ctx_late_after_seal.response.0.text", ResponseType = StrangerResponseType.Silent, ReceptivityChange = 0,
                        StrangerReply = new[] { "stranger.ctx_late_after_seal.response.0.reply.1" } }
                }
            },

            new() { Id = "ctx_late_manwe", MinEncounters = 8, MinLevel = 50, MinAwakening = 4,
                Dialogue = new[] {
                    "stranger.ctx_late_manwe.dialogue.1",
                    "stranger.ctx_late_manwe.dialogue.2",
                    "stranger.ctx_late_manwe.dialogue.3",
                    "stranger.ctx_late_manwe.dialogue.4",
                    "stranger.ctx_late_manwe.dialogue.5"
                },
                Responses = new List<StrangerResponseOption> {
                    new() { Key = "1", Text = "stranger.ctx_late_manwe.response.1.text", ResponseType = StrangerResponseType.Accepted, ReceptivityChange = 15,
                        StrangerReply = new[] { "stranger.ctx_late_manwe.response.1.reply.1", "stranger.ctx_late_manwe.response.1.reply.2" } },
                    new() { Key = "2", Text = "stranger.ctx_late_manwe.response.2.text", ResponseType = StrangerResponseType.Reflective, ReceptivityChange = 12,
                        StrangerReply = new[] { "stranger.ctx_late_manwe.response.2.reply.1", "stranger.ctx_late_manwe.response.2.reply.2" } },
                    new() { Key = "0", Text = "stranger.ctx_late_manwe.response.0.text", ResponseType = StrangerResponseType.Silent, ReceptivityChange = 0,
                        StrangerReply = new[] { "stranger.ctx_late_manwe.response.0.reply.1" } }
                }
            },

            // ── WISDOM/SUSPICION dialogues ──

            new() { Id = "ctx_suspects_identity", MinEncounters = 4, RequiresSuspicion = true,
                Dialogue = new[] {
                    "stranger.ctx_suspects_identity.dialogue.1",
                    "stranger.ctx_suspects_identity.dialogue.2",
                    "stranger.ctx_suspects_identity.dialogue.3"
                },
                Responses = new List<StrangerResponseOption> {
                    new() { Key = "1", Text = "stranger.ctx_suspects_identity.response.1.text", ResponseType = StrangerResponseType.Engaged, ReceptivityChange = 8,
                        StrangerReply = new[] { "stranger.ctx_suspects_identity.response.1.reply.1", "stranger.ctx_suspects_identity.response.1.reply.2" } },
                    new() { Key = "2", Text = "stranger.ctx_suspects_identity.response.2.text", ResponseType = StrangerResponseType.Challenged, ReceptivityChange = 5,
                        StrangerReply = new[] { "stranger.ctx_suspects_identity.response.2.reply.1", "stranger.ctx_suspects_identity.response.2.reply.2" } },
                    new() { Key = "0", Text = "stranger.ctx_suspects_identity.response.0.text", ResponseType = StrangerResponseType.Silent, ReceptivityChange = 0,
                        StrangerReply = new[] { "stranger.ctx_suspects_identity.response.0.reply.1" } }
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
                // v0.61.2: chosen.Dialogue holds localization keys (e.g. "stranger.ctx_mid_wave.dialogue.1").
                // Resolve each key via Loc.Get at consumption time so the dialogue renders in the
                // current player's session language.
                Dialogue = string.Join("\n", chosen.Dialogue.Select(k => UsurperRemake.Systems.Loc.Get(k))),
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

        }

        /// <summary>
        /// Reveal the Stranger's true identity
        /// </summary>
        public void RevealTruth()
        {
            PlayerKnowsTruth = true;
            StoryProgressionSystem.Instance?.SetFlag(StoryFlag.KnowsNocturaTruth);
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
                    // v0.61.2: opt.Text and opt.StrangerReply elements hold localization
                    // keys (e.g. "stranger.ctx_mid_wave.response.2.text"). Resolve via
                    // Loc.Get at render time so dialogue renders in the player's session
                    // language. Pre-fix these were literal English strings.
                    string textLoc = UsurperRemake.Systems.Loc.Get(opt.Text);
                    string replyLoc = string.Join(" ", opt.StrangerReply.Select(k => UsurperRemake.Systems.Loc.Get(k)));
                    options.Add((opt.Key, textLoc, replyLoc));
                }
            }
            else
            {
                // Fallback for encounters without response options (legacy path).
                options.Add(("1", UsurperRemake.Systems.Loc.Get("stranger.fallback.option.1.text"),
                    UsurperRemake.Systems.Loc.Get("stranger.fallback.option.1.reply")));
                options.Add(("2", UsurperRemake.Systems.Loc.Get("stranger.fallback.option.2.text"),
                    UsurperRemake.Systems.Loc.Get("stranger.fallback.option.2.reply")));
                options.Add(("0", UsurperRemake.Systems.Loc.Get("stranger.fallback.option.0.text"),
                    UsurperRemake.Systems.Loc.Get("stranger.fallback.option.0.reply")));
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
                // v0.57.18: cap both at serialization time. UsedDialogueIds is an
                // unordered HashSet — when over the cap we keep an arbitrary N entries,
                // which is acceptable here (re-using an old line is preferable to OOM
                // and the dialogue pool is large enough that most are still novel).
                // RecentGameEvents is an ordered List, so we keep the last-N actually
                // appended via reverse-take-reverse. Normally cleared after each
                // encounter (line 873) but drifts if encounters are interrupted.
                UsedDialogueIds = UsedDialogueIds.Count > GameConfig.MaxSerializedStrangerDialogueIds
                    ? UsedDialogueIds.Take(GameConfig.MaxSerializedStrangerDialogueIds).ToList()
                    : UsedDialogueIds.ToList(),
                RecentGameEvents = RecentGameEvents.Count > GameConfig.MaxSerializedStrangerRecentEvents
                    ? RecentGameEvents
                        .Skip(RecentGameEvents.Count - GameConfig.MaxSerializedStrangerRecentEvents)
                        .Cast<int>()
                        .ToList()
                    : RecentGameEvents.Cast<int>().ToList()
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
