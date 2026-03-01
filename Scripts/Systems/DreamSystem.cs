using System;
using System.Collections.Generic;
using System.Linq;

namespace UsurperRemake.Systems
{
    /// <summary>
    /// Dream System - Handles dreams during rest, dungeon visions, and
    /// environmental narrative beats between major story events.
    ///
    /// Dreams become more vivid and prophetic as the player approaches
    /// the truth about their identity. Rest is a central narrative gateway —
    /// Old Gods communicate through dreams, companions reveal their depths,
    /// and story milestones echo in the sleeping mind.
    /// </summary>
    public class DreamSystem
    {
        private static DreamSystem? _fallbackInstance;
        public static DreamSystem Instance
        {
            get
            {
                var ctx = UsurperRemake.Server.SessionContext.Current;
                if (ctx != null) return ctx.Dreams;
                return _fallbackInstance ??= new DreamSystem();
            }
        }

        // Track experienced dreams
        public HashSet<string> ExperiencedDreams { get; private set; } = new();

        // Track seen dungeon visions (per floor to allow revisiting on different floors)
        public HashSet<string> SeenDungeonVisions { get; private set; } = new();

        // Track last dream to avoid repetition
        private string _lastDreamId = "";
        private int _restsSinceLastDream = 0;

        /// <summary>
        /// All possible dreams organized by player progress
        /// </summary>
        public static readonly List<NarrativeDreamData> Dreams = new()
        {
            // ====== EARLY GAME DREAMS (Levels 1-20) ======

            new NarrativeDreamData
            {
                Id = "dream_drowning",
                Title = "Drowning in Light",
                MinLevel = 1, MaxLevel = 15,
                MinAwakening = 0, MaxAwakening = 3,
                Priority = 10,
                Content = new[] {
                    "You dream of drowning.",
                    "Not in water though. In light. Bright, warm light everywhere.",
                    "For a second it doesnt feel like drowning. It feels like home.",
                    "Then you wake up gasping, grabbing at nothing."
                },
                PhilosophicalHint = "That light. You know that light from somewhere."
            },

            new NarrativeDreamData
            {
                Id = "dream_mirror",
                Title = "The Mirror",
                MinLevel = 5, MaxLevel = 25,
                MinAwakening = 0, MaxAwakening = 4,
                Priority = 10,
                Content = new[] {
                    "You stand before a mirror that shouldn't exist.",
                    "Your reflection wears a crown of stars.",
                    "It smiles sadly. 'Remember,' it says.",
                    "You wake with the word 'Manwe' on your lips, though you don't know why."
                },
                AwakeningGain = 1,
                PhilosophicalHint = "The face in the mirror was yours... wasn't it?"
            },

            new NarrativeDreamData
            {
                Id = "dream_voices",
                Title = "A Chorus of Voices",
                MinLevel = 1, MaxLevel = 30,
                MinAwakening = 0, MaxAwakening = 5,
                Priority = 5,
                Content = new[] {
                    "In the dream, you hear voices. Hundreds of them.",
                    "All calling your name. Except the name keeps changing.",
                    "Different name every time. None of them feel wrong.",
                    "You wake up not sure which one is yours."
                },
                PhilosophicalHint = "How many names have you had?"
            },

            new NarrativeDreamData
            {
                Id = "dream_first_blood",
                Title = "The Eyes",
                MinLevel = 1, MaxLevel = 10,
                MinKills = 1,
                Priority = 20,
                Content = new[] {
                    "You see the creature's eyes again. The first one you killed.",
                    "It was afraid. You can see that now, in the dream, clearer than you could in the moment.",
                    "'It's just the dungeon,' you tell yourself. 'It's what you do down there.'",
                    "The eyes don't close. They stay with you until morning."
                },
                PhilosophicalHint = "It was afraid of you."
            },

            new NarrativeDreamData
            {
                Id = "dream_tavern_memory",
                Title = "The Warm Room",
                MinLevel = 5, MaxLevel = 25,
                Priority = 5,
                Content = new[] {
                    "You dream of the Inn. Everyone is there. Laughing, talking, alive.",
                    "The fire is warm. Someone hands you a drink. Someone else tells a bad joke.",
                    "It feels more real than waking. More real than anything.",
                    "You don't want to leave. But morning comes anyway."
                },
                PhilosophicalHint = "The simple moments. Those are the ones that stay."
            },

            // ====== MID GAME DREAMS (Levels 20-50) ======

            new NarrativeDreamData
            {
                Id = "dream_maelketh_before",
                Title = "The Blade Before",
                MinLevel = 20, MaxLevel = 30,
                RequiredFloor = 20, MaxFloor = 24,
                Priority = 20, // High priority - foreshadowing
                Content = new[] {
                    "You dream of a warrior kneeling in a field of swords.",
                    "He looks exhausted. Like hes been fighting for centuries.",
                    "'I cant even remember why I started,' he says.",
                    "He looks up. His eyes are yours. 'Youre coming. I know you are.'"
                },
                PhilosophicalHint = "He looked tired. Really tired."
            },

            new NarrativeDreamData
            {
                Id = "dream_maelketh_after",
                Title = "The Broken Blade",
                MinLevel = 25, MaxLevel = 45,
                RequiresGodDefeated = OldGodType.Maelketh,
                Priority = 25,
                Content = new[] {
                    "Maelketh visits your dreams. Not as an enemy. As a question.",
                    "'Did I deserve peace? Or just an end?'",
                    "You have no answer. Neither does he.",
                    "He fades, still waiting, still wondering."
                },
                AwakeningGain = 1,
                PhilosophicalHint = "You dont have an answer either."
            },

            new NarrativeDreamData
            {
                Id = "dream_veloura_before",
                Title = "Withered Petals",
                MinLevel = 30, MaxLevel = 45,
                RequiredFloor = 35, MaxFloor = 39,
                Priority = 20,
                Content = new[] {
                    "You dream of a garden where roses bleed.",
                    "A woman kneels among them, hands stained red.",
                    "'They used to love me,' she says. 'Now they just take.'",
                    "She looks at you like youre supposed to fix it. You dont know how."
                },
                PhilosophicalHint = "She was begging. But not to you."
            },

            new NarrativeDreamData
            {
                Id = "dream_veloura_after_saved",
                Title = "The Garden Blooms",
                MinLevel = 40, MaxLevel = 65,
                RequiresGodSaved = OldGodType.Veloura,
                Priority = 25,
                Content = new[] {
                    "Veloura visits your dreams. The garden is different now.",
                    "A single rose blooms — white, untouched, impossibly perfect.",
                    "'You proved it still exists,' she says. 'Love. Real love. Not the kind that takes.'",
                    "'I'd forgotten what it felt like. Thank you for reminding me.'"
                },
                AwakeningGain = 1,
                PhilosophicalHint = "She smiled. A real smile. The first in ten thousand years."
            },

            new NarrativeDreamData
            {
                Id = "dream_veloura_after_defeated",
                Title = "Petals Fall",
                MinLevel = 40, MaxLevel = 65,
                RequiresGodDefeated = OldGodType.Veloura,
                Priority = 25,
                Content = new[] {
                    "You dream of the garden again. The roses are ash.",
                    "Veloura stands among them, fading. Not angry. Just tired.",
                    "'Was there another way?' she asks. Not accusing. Genuinely wondering.",
                    "You open your mouth to answer but she's already gone. The petals keep falling."
                },
                AwakeningGain = 1,
                PhilosophicalHint = "Was there another way? You'll never know."
            },

            new NarrativeDreamData
            {
                Id = "dream_ocean_first",
                Title = "The Ocean Speaks",
                MinLevel = 30, MaxLevel = 60,
                MinAwakening = 2, MaxAwakening = 5,
                Priority = 15,
                Content = new[] {
                    "You dream of standing at the edge of an ocean.",
                    "Something in the waves seems to recognize you.",
                    "'I dont know what I am,' you say to nobody.",
                    "The sound of the waves changes. Almost like laughing. 'You always say that.'"
                },
                AwakeningGain = 1,
                WaveFragment = WaveFragment.FirstSeparation,
                PhilosophicalHint = "The ocean knew you. How?"
            },

            new NarrativeDreamData
            {
                Id = "dream_old_road",
                Title = "The Endless Road",
                MinLevel = 25, MaxLevel = 50,
                Priority = 8,
                Content = new[] {
                    "You walk a road that never ends. The sun never moves.",
                    "Other travelers pass you going the opposite direction.",
                    "They all have your face. Different ages, different scars, same eyes.",
                    "None of them stop. None of them seem surprised."
                },
                PhilosophicalHint = "How many times have you walked this road?"
            },

            // ====== LATE GAME DREAMS (Levels 50-80) ======

            new NarrativeDreamData
            {
                Id = "dream_thorgrim_before",
                Title = "The Scales Tip",
                MinLevel = 45, MaxLevel = 60,
                RequiredFloor = 50, MaxFloor = 54,
                Priority = 20,
                Content = new[] {
                    "In the dream, you stand in a courtroom that goes on forever.",
                    "A judge sits on a bone throne. The scales in his hand are empty.",
                    "'ORDER,' he thunders. 'WITHOUT ORDER, CHAOS.'",
                    "You want to argue but you cant find the words."
                },
                PhilosophicalHint = "You talked back to a god. In a dream. Bold."
            },

            new NarrativeDreamData
            {
                Id = "dream_thorgrim_after",
                Title = "The Empty Court",
                MinLevel = 55, MaxLevel = 80,
                RequiresGodDefeated = OldGodType.Thorgrim,
                Priority = 25,
                Content = new[] {
                    "You dream of Thorgrim's courtroom. It's empty now.",
                    "The bone throne stands vacant. The scales lie on the floor, still empty.",
                    "His voice echoes from nowhere: 'Perhaps mercy was the law I forgot.'",
                    "The gavel rests on the bench. Nobody will pick it up again."
                },
                AwakeningGain = 1,
                PhilosophicalHint = "Justice without mercy isn't justice. He learned too late."
            },

            new NarrativeDreamData
            {
                Id = "dream_noctura_shadow",
                Title = "Shadows Speak",
                MinLevel = 50, MaxLevel = 75,
                RequiredFloor = 60, MaxFloor = 69,
                Priority = 20,
                Content = new[] {
                    "The dream is darkness. But darkness that thinks. That remembers.",
                    "'I've been watching you,' the shadow says. 'Since the beginning.'",
                    "'Why?' you ask.",
                    "'Because you're the only one who might understand. When the time comes.'"
                },
                PhilosophicalHint = "Something has been watching you. For a long time."
            },

            new NarrativeDreamData
            {
                Id = "dream_noctura_after_allied",
                Title = "The Shadow Smiles",
                MinLevel = 70, MaxLevel = 100,
                RequiresGodAllied = OldGodType.Noctura,
                Priority = 25,
                Content = new[] {
                    "Noctura visits your dreams. She doesn't hide this time.",
                    "'I chose you,' she says, 'because you'd understand. Most people fear the dark.'",
                    "'What's at the bottom?' you ask. 'What's waiting?'",
                    "She smiles. 'You already know. You've always known. You just haven't said it out loud yet.'"
                },
                AwakeningGain = 1,
                PhilosophicalHint = "She chose you. Long before you chose anything."
            },

            new NarrativeDreamData
            {
                Id = "dream_noctura_after_defeated",
                Title = "Quieter Shadows",
                MinLevel = 70, MaxLevel = 100,
                RequiresGodDefeated = OldGodType.Noctura,
                Priority = 25,
                Content = new[] {
                    "The shadows are quieter tonight. They used to whisper.",
                    "You dream of an empty room. Something should be here. Something important.",
                    "The darkness doesn't think anymore. It's just dark.",
                    "You didn't realize how much you'd miss being watched."
                },
                PhilosophicalHint = "Some absences are louder than presence."
            },

            new NarrativeDreamData
            {
                Id = "dream_creation",
                Title = "In the Beginning",
                MinLevel = 60, MaxLevel = 90,
                MinAwakening = 4, MaxAwakening = 7,
                Priority = 15,
                Content = new[] {
                    "You dream of being alone. Completely alone.",
                    "Not lonely. Theres nobody to miss. Just... nothing else exists.",
                    "And then you think: 'What if I made something?'",
                    "You wake up with the strangest feeling that you just remembered how the world began."
                },
                AwakeningGain = 2,
                WaveFragment = WaveFragment.Origin,
                PhilosophicalHint = "Being alone like that. You cant even imagine it."
            },

            new NarrativeDreamData
            {
                Id = "dream_deep_dungeon",
                Title = "The Heartbeat Below",
                MinLevel = 40, MaxLevel = 80,
                MinDeepestFloor = 50,
                Priority = 15,
                Content = new[] {
                    "You dream of the deep floors. Below everything you know.",
                    "The walls breathe down there. Something enormous sleeps beneath it all.",
                    "You press your hand against the stone and feel a heartbeat. Slow. Patient.",
                    "It's been beating for ten thousand years. Waiting for you to hear it."
                },
                AwakeningGain = 1,
                PhilosophicalHint = "The dungeon has a heartbeat. Whose?"
            },

            new NarrativeDreamData
            {
                Id = "dream_empty_throne",
                Title = "Seven Empty Chairs",
                MinLevel = 50, MaxLevel = 80,
                Priority = 8,
                Content = new[] {
                    "You dream of a throne room with no king.",
                    "Seven chairs circle the empty throne. Each one bears a name you almost recognize.",
                    "The last chair has YOUR name carved into it. The wood is ancient.",
                    "How can your name be carved into something older than you?"
                },
                PhilosophicalHint = "Your name was there before you were born."
            },

            // ====== ENDGAME DREAMS (Levels 80+) ======

            new NarrativeDreamData
            {
                Id = "dream_aurelion_fading",
                Title = "The Light Dims",
                MinLevel = 75, MaxLevel = 90,
                RequiredFloor = 80, MaxFloor = 84,
                Priority = 20,
                Content = new[] {
                    "You dream of a single candle in total darkness.",
                    "The flame keeps guttering. Almost going out.",
                    "'Dont forget,' something whispers. 'Please dont forget.'",
                    "You dont know what youre supposed to remember."
                },
                PhilosophicalHint = "It was begging you to remember. Desperate."
            },

            new NarrativeDreamData
            {
                Id = "dream_aurelion_after_saved",
                Title = "Light Carried",
                MinLevel = 85, MaxLevel = 100,
                RequiresGodSaved = OldGodType.Aurelion,
                Priority = 25,
                Content = new[] {
                    "Aurelion visits your dreams. His light is warm, not blinding.",
                    "'You took the burden,' he says. 'The weight of truth. Nobody asked you to.'",
                    "'Does it get easier?' you ask.",
                    "'No,' he says gently. 'But you get stronger. You already have.'"
                },
                AwakeningGain = 1,
                PhilosophicalHint = "Truth is heavy. But you're carrying it."
            },

            new NarrativeDreamData
            {
                Id = "dream_aurelion_after_defeated",
                Title = "The Last Candle",
                MinLevel = 85, MaxLevel = 100,
                RequiresGodDefeated = OldGodType.Aurelion,
                Priority = 25,
                Content = new[] {
                    "You dream of the candle again. This time it goes out.",
                    "In the sudden dark, you hear Aurelion's voice, thin as smoke.",
                    "'Every lie told in the world just got a little easier.'",
                    "The dark feels heavier now. Thicker. Like it's settling in permanently."
                },
                PhilosophicalHint = "The world got a little darker. You felt it."
            },

            new NarrativeDreamData
            {
                Id = "dream_terravok_deep",
                Title = "The Mountain Dreams",
                MinLevel = 85, MaxLevel = 100,
                RequiredFloor = 90, MaxFloor = 94,
                Priority = 20,
                Content = new[] {
                    "You dream of being a mountain. Heavy. Still. Ancient.",
                    "Youve been here for so long you forgot how to move.",
                    "Somewhere far away, you hear waves.",
                    "'When I wake up,' you think, 'everything changes.' You cant tell if thats good or bad."
                },
                PhilosophicalHint = "Mountains dont dream. But this one did."
            },

            new NarrativeDreamData
            {
                Id = "dream_manwe_waiting",
                Title = "The Weary Creator",
                MinLevel = 90, MaxLevel = 100,
                RequiredFloor = 95, MaxFloor = 99,
                MinAwakening = 5,
                Priority = 25, // Highest priority
                Content = new[] {
                    "You dream of a throne at the bottom of everything.",
                    "Someone sits there. Hes been waiting a very long time.",
                    "'Almost,' he says. 'Youre almost here.'",
                    "You open your mouth and the word that comes out is 'Father.' He just nods."
                },
                AwakeningGain = 2,
                WaveFragment = WaveFragment.TheTruth,
                PhilosophicalHint = "He called you Father. And Child. And Self."
            },

            // ====== COMPANION DEATH DREAMS ======

            new NarrativeDreamData
            {
                Id = "dream_grief_lyris",
                Title = "Starlight Fades",
                RequiresCompanionDeath = "Lyris",
                Priority = 30,
                Content = new[] {
                    "You dream of Lyris.",
                    "Shes standing somewhere bright. She looks peaceful.",
                    "'Its okay,' she says. 'I knew this would happen eventually.'",
                    "She smiles at you. Then the light takes her and shes gone."
                },
                AwakeningGain = 1,
                PhilosophicalHint = "She didnt seem sad. Thats the strange part."
            },

            new NarrativeDreamData
            {
                Id = "dream_grief_aldric",
                Title = "The Shield Rests",
                RequiresCompanionDeath = "Aldric",
                Priority = 30,
                Content = new[] {
                    "Aldric shows up in your dreams. Shield down for once.",
                    "'Shouldve ducked,' he says. Almost laughing.",
                    "Then hes serious. 'Finish this. For both of us.'",
                    "He salutes and fades out."
                },
                AwakeningGain = 1,
                PhilosophicalHint = "He saluted you. Even dead, still a soldier."
            },

            new NarrativeDreamData
            {
                Id = "dream_grief_mira",
                Title = "The Last Healing",
                RequiresCompanionDeath = "Mira",
                Priority = 30,
                Content = new[] {
                    "Mira visits your dreams. She looks tired but calm.",
                    "'I couldnt save myself,' she says. 'Funny, right?'",
                    "'But I saved you. Thats enough.'",
                    "She touches your forehead and a warmth spreads through you."
                },
                AwakeningGain = 2,
                PhilosophicalHint = "She was still trying to heal people. Even dead."
            },

            new NarrativeDreamData
            {
                Id = "dream_grief_vex",
                Title = "One Last Laugh",
                RequiresCompanionDeath = "Vex",
                Priority = 30,
                Content = new[] {
                    "Vex appears in your dreams, laughing.",
                    "'Don't look so glum! I was dying before we met, remember?'",
                    "'You gave me more adventure in one lifetime than most get in ten.'",
                    "'Besides...' he winks. 'The best jokes always have a punchline. Mine was LEGENDARY.'"
                },
                AwakeningGain = 1,
                PhilosophicalHint = "Typical Vex. Still cracking jokes."
            },

            // ====== COMPANION PRESENCE DREAMS (companion alive and recruited) ======

            new NarrativeDreamData
            {
                Id = "dream_with_lyris",
                Title = "Firelight",
                RequiresCompanionAlive = "Lyris",
                MinLevel = 15, MaxLevel = 100,
                MinAwakening = 1, MaxAwakening = 6,
                Priority = 15,
                Content = new[] {
                    "You dream of a campfire. Lyris sits across from you, watching the flames.",
                    "'I've seen this before,' she says quietly. 'You, sleeping. Me, watching.'",
                    "'But from the other side. Like I was the dreamer and you were the dream.'",
                    "She looks at you with those old, knowing eyes. 'You'll understand eventually.'"
                },
                AwakeningGain = 1,
                PhilosophicalHint = "She knows something. She's known for a long time."
            },

            new NarrativeDreamData
            {
                Id = "dream_with_aldric",
                Title = "The Watch",
                RequiresCompanionAlive = "Aldric",
                MinLevel = 10, MaxLevel = 100,
                Priority = 15,
                Content = new[] {
                    "You dream of waking in the night. Aldric is keeping watch.",
                    "He doesn't know you can see him. He's talking to someone who isn't there.",
                    "'I won't let it happen again,' he whispers. 'Not this time. Not to them.'",
                    "He straightens his shield and faces the dark. You pretend to sleep."
                },
                PhilosophicalHint = "He carries his dead with him. Every night."
            },

            new NarrativeDreamData
            {
                Id = "dream_with_mira",
                Title = "The Wound That Wasn't",
                RequiresCompanionAlive = "Mira",
                MinLevel = 20, MaxLevel = 100,
                Priority = 15,
                Content = new[] {
                    "Mira is healing a wound on your arm in the dream. There is no wound.",
                    "'Force of habit,' she says, not looking up.",
                    "She pauses. 'Can I ask you something? If you could save everyone — every single person — but it cost you everything you are... would you?'",
                    "She doesn't wait for an answer. She already knows what she'd choose."
                },
                PhilosophicalHint = "She already made her choice. Long before she asked you."
            },

            new NarrativeDreamData
            {
                Id = "dream_with_vex",
                Title = "One More Sunrise",
                RequiresCompanionAlive = "Vex",
                MinLevel = 25, MaxLevel = 100,
                Priority = 15,
                Content = new[] {
                    "You dream of a rooftop at dawn. Vex is sitting on the edge, legs dangling.",
                    "He's not joking for once. Not performing. Just watching the sky change color.",
                    "'You know I'm dying, right?' he says. 'I mean, everyone is. I just know roughly when.'",
                    "He turns to you. 'Don't be sad about it. I got more than I deserved. Way more.'"
                },
                PhilosophicalHint = "Behind every joke, he was saying goodbye."
            },

            // ====== STORY MILESTONE DREAMS ======

            new NarrativeDreamData
            {
                Id = "dream_crowned",
                Title = "The Weight of the Crown",
                RequiresIsKing = true,
                Priority = 20,
                Content = new[] {
                    "You dream of the crown. It's heavier than it should be.",
                    "In the dream, it grows roots — thin gold tendrils pushing into your skull.",
                    "Everyone in the throne room bows. But nobody looks at you.",
                    "You try to take it off. It doesn't come off."
                },
                PhilosophicalHint = "Power takes root. It doesn't let go easily."
            },

            new NarrativeDreamData
            {
                Id = "dream_high_darkness",
                Title = "The Dark Welcomes You",
                MinDarkness = 3000,
                Priority = 20,
                Content = new[] {
                    "The shadows in the dream don't flee from you. They reach for you.",
                    "They speak in a voice that sounds exactly like yours.",
                    "'We've been waiting,' they say. 'You're almost home.'",
                    "'Come further in. Come deeper. There's nothing to be afraid of. Not anymore.'"
                },
                PhilosophicalHint = "The darkness knows your name. It's been practicing."
            },

            new NarrativeDreamData
            {
                Id = "dream_high_chivalry",
                Title = "The Light Remembers",
                MinChivalry = 3000,
                Priority = 20,
                Content = new[] {
                    "A warm light reaches for you in the dream. Not sunlight. Something older.",
                    "It knows your name. All of your names. Every one you've ever had.",
                    "'You chose well,' it says. Not praising. Just acknowledging.",
                    "For a moment you feel connected to something vast and kind. Then you wake up."
                },
                AwakeningGain = 1,
                PhilosophicalHint = "The light remembered you. Even when you forgot yourself."
            },

            new NarrativeDreamData
            {
                Id = "dream_marriage",
                Title = "Every Lifetime",
                RequiresMarriage = true,
                Priority = 20,
                Content = new[] {
                    "You dream of your spouse. But their face keeps shifting.",
                    "Different features, different hair, different eyes. Different lifetimes.",
                    "The face changes but the feeling doesn't. The same warmth, every time.",
                    "'We keep finding each other,' they say. And you know it's true."
                },
                AwakeningGain = 1,
                PhilosophicalHint = "Some connections survive even forgetting."
            },

            new NarrativeDreamData
            {
                Id = "dream_all_seals",
                Title = "The Mirror of Truth",
                RequiresAllSeals = true,
                Priority = 25,
                MinAwakening = 4,
                Content = new[] {
                    "The seven seal fragments assemble themselves in the dream.",
                    "They form a mirror. Not glass. Something older. Something that shows what IS, not what appears.",
                    "You look into it. The face staring back is not yours.",
                    "It is the face of creation itself. And it has been crying for a very long time."
                },
                AwakeningGain = 2,
                PhilosophicalHint = "The mirror showed the truth. You weren't ready. Nobody ever is."
            },

            new NarrativeDreamData
            {
                Id = "dream_companion_quest",
                Title = "The Crossroads",
                RequiresAnyCompanionQuestDone = true,
                Priority = 18,
                Content = new[] {
                    "You dream of a crossroads. Your companion stands at the fork.",
                    "One road leads away from you. Safe. Easy. The life they could have had.",
                    "They look at it for a long time. Then they turn and walk toward you instead.",
                    "In the dream, you understand what that cost them. Everything."
                },
                AwakeningGain = 1,
                PhilosophicalHint = "They chose you. Knowing what it would cost."
            },

            // ====== CYCLE-SPECIFIC DREAMS (NG+) ======

            new NarrativeDreamData
            {
                Id = "dream_cycle_deja_vu",
                Title = "Haven't We Done This Before?",
                MinCycle = 2,
                Priority = 10,
                Content = new[] {
                    "The dream feels familiar. Way too familiar.",
                    "Youve been here before. Done all this before.",
                    "'How many times?' you mutter.",
                    "Nobody answers. But you get the feeling its been a lot."
                },
                PhilosophicalHint = "How many times have you done this?"
            },

            new NarrativeDreamData
            {
                Id = "dream_cycle_fragments",
                Title = "Fragments of Previous Lives",
                MinCycle = 3,
                MinAwakening = 3,
                Priority = 15,
                Content = new[] {
                    "You dream of all the other times.",
                    "Different faces every time. Same road though.",
                    "Someone always has to reach the end.",
                    "Someone always has to make the choice. This time its you. Again."
                },
                AwakeningGain = 2,
                PhilosophicalHint = "Different face every time. Same ending."
            }
        };

        /// <summary>
        /// Environmental visions triggered by dungeon exploration
        /// </summary>
        public static readonly List<DungeonVision> DungeonVisions = new()
        {
            new DungeonVision
            {
                Id = "vision_prayer_bones",
                FloorMin = 5, FloorMax = 15,
                Description = "A skeleton in prayer",
                Content = new[] {
                    "A skeleton kneels in the corner, hands clasped together. Still praying.",
                    "Whatever god they called to never answered.",
                    "Or maybe it did, and this was the answer."
                }
            },
            new DungeonVision
            {
                Id = "vision_wall_writing",
                FloorMin = 10, FloorMax = 25,
                Description = "Ancient writing on the wall",
                Content = new[] {
                    "Scratched into the stone, barely visible:",
                    "\"HE FORGOT WHAT HE WAS. WE DIDN'T.\"",
                    "Below it, in different handwriting: \"I was here before. Ill be here again.\""
                }
            },
            new DungeonVision
            {
                Id = "vision_counting_marks",
                FloorMin = 15, FloorMax = 30,
                Description = "Tally marks",
                Content = new[] {
                    "Tally marks are scratched into the stone. Hundreds of them.",
                    "Counting days? Kills? Steps? They go on and on, covering the whole wall.",
                    "Then they stop. The last mark is unfinished. Whoever was counting ran out of time."
                }
            },
            new DungeonVision
            {
                Id = "vision_singing_stone",
                FloorMin = 25, FloorMax = 40,
                Description = "A humming stone",
                Content = new[] {
                    "A stone embedded in the wall hums softly. A melody. Simple, old, familiar.",
                    "You know this tune from somewhere you've never been.",
                    "The stone hums it over and over, patient as the earth, waiting for someone to remember the words."
                }
            },
            new DungeonVision
            {
                Id = "vision_candles",
                FloorMin = 30, FloorMax = 50,
                Description = "Seven unlit candles",
                Content = new[] {
                    "Seven candles stand in a circle, cold and dark.",
                    "As you pass, they flicker to life for just a moment.",
                    "Seven flames. Seven gods. Seven pieces of something broken."
                }
            },
            new DungeonVision
            {
                Id = "vision_frozen_battle",
                FloorMin = 35, FloorMax = 55,
                Description = "Frozen combatants",
                Content = new[] {
                    "Two skeletons locked in combat. Swords raised mid-swing. Frozen in this moment for centuries.",
                    "They'll never know who won. Neither will you.",
                    "Maybe that's the point."
                }
            },
            new DungeonVision
            {
                Id = "vision_child_drawing",
                FloorMin = 40, FloorMax = 60,
                Description = "A child's drawing",
                Content = new[] {
                    "A crude drawing scratched into the wall — a stick figure holding a sun.",
                    "Below it, in a child's unsteady letters: \"WHEN I GROW UP I WANT TO GO HOME.\"",
                    "Something about those words hits harder than any monster ever has."
                },
                AwakeningGain = 1
            },
            new DungeonVision
            {
                Id = "vision_mirror_room",
                FloorMin = 50, FloorMax = 75,
                MinAwakening = 3,
                Description = "A room of mirrors",
                Content = new[] {
                    "The room is filled with mirrors, all angled differently.",
                    "In each one, you see a different face. All of them yours.",
                    "One mirror shows a face wearing a crown of stars.",
                    "It winks at you before the room goes dark."
                },
                AwakeningGain = 1
            },
            new DungeonVision
            {
                Id = "vision_dripping_time",
                FloorMin = 50, FloorMax = 70,
                Description = "Water dripping upward",
                Content = new[] {
                    "Water drips upward here. Against gravity, against reason.",
                    "Each drop carries a tiny reflection. Different moments in time.",
                    "In one, you see yourself entering this dungeon. In another, you see yourself leaving.",
                    "The faces are different."
                }
            },
            new DungeonVision
            {
                Id = "vision_empty_cage",
                FloorMin = 60, FloorMax = 80,
                Description = "An empty cage",
                Content = new[] {
                    "A cage stands here, large enough to hold a god. The bars are ancient iron, thick as trees.",
                    "The door hangs open. Claw marks score the inside — deep, desperate, patient.",
                    "Whatever was kept here, it got out. A long time ago.",
                    "Or maybe it was let out."
                }
            },
            new DungeonVision
            {
                Id = "vision_crying_statue",
                FloorMin = 70, FloorMax = 90,
                Description = "A weeping statue",
                Content = new[] {
                    "A statue of a robed figure kneels here, hands over its face.",
                    "Stone tears have worn channels down its cheeks.",
                    "\"I NEVER MEANT FOR THEM TO SUFFER,\" is carved at its base.",
                    "The statue looks... familiar."
                }
            },
            new DungeonVision
            {
                Id = "vision_forgotten_altar",
                FloorMin = 70, FloorMax = 85,
                Description = "A forgotten altar",
                Content = new[] {
                    "An altar to a god whose name has been chiseled away. Deliberately, thoroughly erased.",
                    "But the offerings are fresh. Flowers. Bread. A child's toy.",
                    "Someone still remembers. Someone still comes down here to pray to a name nobody speaks.",
                },
                AwakeningGain = 1
            },
            new DungeonVision
            {
                Id = "vision_ocean_sound",
                FloorMin = 85, FloorMax = 100,
                MinAwakening = 5,
                Description = "The sound of waves",
                Content = new[] {
                    "Impossible, this deep underground, but you hear it clearly.",
                    "Waves. The rhythm of an endless ocean.",
                    "For a moment, the dungeon walls shimmer like water.",
                    "You remember - no. You ALMOST remember."
                },
                AwakeningGain = 2,
                WaveFragment = WaveFragment.TheReturn
            },
            new DungeonVision
            {
                Id = "vision_final_door",
                FloorMin = 85, FloorMax = 95,
                Description = "A door with no handle",
                Content = new[] {
                    "A door stands in the wall. No handle. No hinges. No lock.",
                    "Written across it in letters that glow faintly:",
                    "\"YOU ALREADY KNOW WHAT'S ON THE OTHER SIDE.\"",
                    "You do. That's what scares you."
                },
                AwakeningGain = 1
            },
            new DungeonVision
            {
                Id = "vision_heartbeat",
                FloorMin = 90, FloorMax = 100,
                MinAwakening = 4,
                Description = "The heartbeat",
                Content = new[] {
                    "The walls pulse. The floor pulses. The air itself throbs with rhythm.",
                    "It's a heartbeat. Slow and vast and ancient.",
                    "You press your hand to the stone and feel it clearly. Thump. Thump. Thump.",
                    "It matches yours. It IS yours. The dungeon was never a place. It was always a body."
                },
                AwakeningGain = 2,
                WaveFragment = WaveFragment.ManwesChoice
            }
        };

        public DreamSystem()
        {
            _fallbackInstance = this;
        }

        /// <summary>
        /// Get a dream for the player when they rest.
        /// Nightmares from the Blood Price take priority over normal dreams.
        /// Rest is a narrative gateway — dreams fire frequently.
        /// </summary>
        public NarrativeDreamData? GetDreamForRest(Character player, int currentFloor)
        {
            _restsSinceLastDream++;

            // Blood Price nightmares take priority — the dead don't wait their turn
            if (player.MurderWeight > 0)
            {
                var nightmare = GetNightmare(player);
                if (nightmare != null)
                {
                    _restsSinceLastDream = 0;
                    return nightmare;
                }
            }

            // Allow a dream every other rest (cooldown of 1 rest between dreams)
            if (_restsSinceLastDream < 1) return null;

            var awakening = OceanPhilosophySystem.Instance?.AwakeningLevel ?? 0;
            var cycle = StoryProgressionSystem.Instance?.CurrentCycle ?? 1;

            // Filter eligible dreams
            var eligible = Dreams
                .Where(d => !ExperiencedDreams.Contains(d.Id))
                .Where(d => d.MinLevel <= player.Level && d.MaxLevel >= player.Level)
                .Where(d => d.MinAwakening <= awakening && d.MaxAwakening >= awakening)
                .Where(d => d.MinCycle <= cycle)
                .Where(d => CheckDreamRequirements(d, currentFloor, player))
                .OrderByDescending(d => d.Priority)
                .ThenBy(_ => Guid.NewGuid()) // Randomize within priority
                .ToList();

            if (!eligible.Any()) return null;

            var dream = eligible.First();
            _lastDreamId = dream.Id;
            _restsSinceLastDream = 0;

            return dream;
        }

        /// <summary>
        /// Mark a dream as experienced
        /// </summary>
        public void ExperienceDream(string dreamId)
        {
            ExperiencedDreams.Add(dreamId);

            var dream = Dreams.FirstOrDefault(d => d.Id == dreamId);
            if (dream != null)
            {
                // Apply awakening gain
                if (dream.AwakeningGain > 0)
                {
                    OceanPhilosophySystem.Instance?.GainInsight(dream.AwakeningGain * 10);
                }

                // Grant wave fragment
                if (dream.WaveFragment.HasValue)
                {
                    OceanPhilosophySystem.Instance?.CollectFragment(dream.WaveFragment.Value);
                }
            }

        }

        /// <summary>
        /// Get a dungeon vision for the current floor (only shows each vision once per playthrough)
        /// </summary>
        public DungeonVision? GetDungeonVision(int floor, Character player)
        {
            var awakening = OceanPhilosophySystem.Instance?.AwakeningLevel ?? 0;

            // Filter to eligible visions that haven't been seen yet
            var eligible = DungeonVisions
                .Where(v => v.FloorMin <= floor && v.FloorMax >= floor)
                .Where(v => v.MinAwakening <= awakening)
                .Where(v => !SeenDungeonVisions.Contains(v.Id))  // Don't repeat seen visions
                .ToList();

            if (!eligible.Any()) return null;

            // 30% chance to trigger a vision when entering a new room
            if (Random.Shared.Next(0, 101) > 30) return null;

            var vision = eligible[Random.Shared.Next(0, eligible.Count)];

            // Mark as seen so it won't repeat
            SeenDungeonVisions.Add(vision.Id);

            return vision;
        }

        /// <summary>
        /// Reset seen dungeon visions (e.g., for New Game+)
        /// </summary>
        public void ResetDungeonVisions()
        {
            SeenDungeonVisions.Clear();
        }

        // ====== BLOOD PRICE NIGHTMARES ======
        // These override normal dreams when the player carries murder weight.
        // {VICTIM} is replaced at runtime with a real name from the PermakillLog.

        private static readonly List<(int tier, float triggerChance, NarrativeDreamData dream)> Nightmares = new()
        {
            // --- TIER 1: Weight 1-2 — unease, no mechanical penalty ---
            (1, 0.30f, new NarrativeDreamData
            {
                Id = "nightmare_shadows",
                Title = "Shadows in the Corner",
                Content = new[] {
                    "You sleep, but something watches.",
                    "Every time you turn, the shadows move wrong. Not with you. Against you.",
                    "There's a shape in the corner of the room. Standing still.",
                    "You tell yourself it isn't real. You almost believe it."
                },
                PhilosophicalHint = "The shadow didn't move when you did."
            }),
            (1, 0.30f, new NarrativeDreamData
            {
                Id = "nightmare_hands",
                Title = "Red Hands",
                Content = new[] {
                    "You look down at your hands in the dream.",
                    "They're clean. You scrub them anyway.",
                    "The water in the basin turns red. Your hands are still clean.",
                    "You keep scrubbing."
                },
                PhilosophicalHint = "Clean hands. Dirty water."
            }),

            // --- TIER 2: Weight 3-5 — uses {VICTIM}, rest penalty starts ---
            (2, 0.60f, new NarrativeDreamData
            {
                Id = "nightmare_face",
                Title = "A Face You Know",
                Content = new[] {
                    "The dream starts like any other. A road. A town. Nothing wrong.",
                    "Then you see {VICTIM} standing at the end of the street.",
                    "They don't speak. They just look at you.",
                    "You try to walk past but the street keeps stretching. They never get closer.",
                    "They never look away."
                },
                AwakeningGain = -1,
                PhilosophicalHint = "They were waiting for you."
            }),
            (2, 0.60f, new NarrativeDreamData
            {
                Id = "nightmare_chair",
                Title = "The Empty Chair",
                Content = new[] {
                    "You dream of the Inn. Everyone is there. Laughing, talking.",
                    "There's an empty chair at the table. Nobody sits in it.",
                    "You ask who it belongs to. Everyone stops talking.",
                    "'{VICTIM},' someone says. 'Don't you remember?'",
                    "You wake up before they finish the sentence."
                },
                AwakeningGain = -1,
                PhilosophicalHint = "The chair is always empty now."
            }),

            // --- TIER 3: Weight 6+ — vivid, repeatable, punishing ---
            (3, 0.85f, new NarrativeDreamData
            {
                Id = "nightmare_trial",
                Title = "The Trial",
                Content = new[] {
                    "You stand in a courtroom made of bone.",
                    "The jury is everyone you've ever known. None of them look at you.",
                    "{VICTIM} sits in the witness chair. They speak without moving their mouth.",
                    "'Tell them what you did,' they say. 'Tell them why.'",
                    "You open your mouth to answer and nothing comes out.",
                    "The verdict comes anyway."
                },
                AwakeningGain = -1,
                PhilosophicalHint = "There was no defense to offer."
            }),
            (3, 0.85f, new NarrativeDreamData
            {
                Id = "nightmare_echo",
                Title = "The Echo",
                Content = new[] {
                    "You hear your own voice in the dream, but it's coming from somewhere else.",
                    "It's saying the things you said before {VICTIM} died.",
                    "The words sound different now. Smaller. Uglier.",
                    "You try to stop listening but it's your own voice.",
                    "It doesn't stop."
                },
                AwakeningGain = -1,
                PhilosophicalHint = "You heard yourself clearly for the first time."
            }),
        };

        /// <summary>
        /// Select a nightmare based on the player's murder weight tier.
        /// Tier 3 nightmares can repeat. Tier 1-2 nightmares are one-time only.
        /// </summary>
        private NarrativeDreamData? GetNightmare(Character player)
        {
            if (player.MurderWeight <= 0) return null;

            int tier;
            if (player.MurderWeight >= 6) tier = 3;
            else if (player.MurderWeight >= 3) tier = 2;
            else tier = 1;

            var rng = new Random();

            // Get nightmares for this tier (and lower tiers)
            var candidates = Nightmares
                .Where(n => n.tier <= tier)
                .Where(n => n.tier == 3 || !ExperiencedDreams.Contains(n.dream.Id)) // Tier 3 can repeat
                .ToList();

            if (candidates.Count == 0) return null;

            // Pick a random candidate and check its trigger chance
            var pick = candidates[rng.Next(candidates.Count)];
            if (rng.NextDouble() > pick.triggerChance) return null;

            // Replace {VICTIM} placeholder with a real name from the kill log
            var dream = pick.dream;
            if (player.PermakillLog.Count > 0)
            {
                string victim = player.PermakillLog[rng.Next(player.PermakillLog.Count)];
                dream = new NarrativeDreamData
                {
                    Id = dream.Id,
                    Title = dream.Title,
                    Content = dream.Content.Select(line => line.Replace("{VICTIM}", victim)).ToArray(),
                    AwakeningGain = dream.AwakeningGain,
                    PhilosophicalHint = dream.PhilosophicalHint.Replace("{VICTIM}", victim),
                };
            }

            return dream;
        }

        /// <summary>
        /// Check if dream requirements are met
        /// </summary>
        private bool CheckDreamRequirements(NarrativeDreamData dream, int currentFloor, Character player)
        {
            // Check floor requirements
            if (dream.RequiredFloor > 0 && currentFloor < dream.RequiredFloor) return false;
            if (dream.MaxFloor > 0 && currentFloor > dream.MaxFloor) return false;

            // Check god defeat requirements
            if (dream.RequiresGodDefeated.HasValue)
            {
                var godState = StoryProgressionSystem.Instance?.OldGodStates
                    .GetValueOrDefault(dream.RequiresGodDefeated.Value);
                if (godState?.Status != GodStatus.Defeated)
                    return false;
            }

            // Check god saved requirements
            if (dream.RequiresGodSaved.HasValue)
            {
                var godState = StoryProgressionSystem.Instance?.OldGodStates
                    .GetValueOrDefault(dream.RequiresGodSaved.Value);
                if (godState?.Status != GodStatus.Saved)
                    return false;
            }

            // Check god allied requirements
            if (dream.RequiresGodAllied.HasValue)
            {
                var godState = StoryProgressionSystem.Instance?.OldGodStates
                    .GetValueOrDefault(dream.RequiresGodAllied.Value);
                if (godState?.Status != GodStatus.Allied)
                    return false;
            }

            // Check companion death requirements
            if (!string.IsNullOrEmpty(dream.RequiresCompanionDeath))
            {
                var griefSystem = GriefSystem.Instance;
                var griefData = griefSystem?.Serialize();
                if (griefData?.ActiveGrief?.All(g => g.CompanionName != dream.RequiresCompanionDeath) ?? true)
                    return false;
            }

            // Check companion alive requirements
            if (!string.IsNullOrEmpty(dream.RequiresCompanionAlive))
            {
                var companion = CompanionSystem.Instance?.GetRecruitedCompanions()
                    .FirstOrDefault(c => c.Name == dream.RequiresCompanionAlive);
                if (companion == null)
                    return false;
            }

            // Check king requirement
            if (dream.RequiresIsKing && !(player.King))
                return false;

            // Check alignment requirements
            if (dream.MinDarkness > 0 && player.Darkness < dream.MinDarkness)
                return false;
            if (dream.MinChivalry > 0 && player.Chivalry < dream.MinChivalry)
                return false;

            // Check marriage requirement
            if (dream.RequiresMarriage && !player.IsMarried)
                return false;

            // Check minimum kills
            if (dream.MinKills > 0 && player.MKills < dream.MinKills)
                return false;

            // Check deepest dungeon floor reached
            if (dream.MinDeepestFloor > 0)
            {
                var stats = player.Statistics;
                if (stats == null || stats.DeepestDungeonLevel < dream.MinDeepestFloor)
                    return false;
            }

            // Check all seals collected
            if (dream.RequiresAllSeals)
            {
                var story = StoryProgressionSystem.Instance;
                if (story == null || story.CollectedSeals.Count < 7)
                    return false;
            }

            // Check any companion quest completed
            if (dream.RequiresAnyCompanionQuestDone)
            {
                var companions = CompanionSystem.Instance?.GetAllCompanions();
                if (companions == null || !companions.Any(c => c.PersonalQuestCompleted))
                    return false;
            }

            return true;
        }

        /// <summary>
        /// Serialize for save
        /// </summary>
        public DreamSaveData Serialize()
        {
            return new DreamSaveData
            {
                ExperiencedDreams = ExperiencedDreams.ToList(),
                SeenDungeonVisions = SeenDungeonVisions.ToList(),
                RestsSinceLastDream = _restsSinceLastDream
            };
        }

        /// <summary>
        /// Deserialize from save
        /// </summary>
        public void Deserialize(DreamSaveData? data)
        {
            if (data == null) return;

            ExperiencedDreams = new HashSet<string>(data.ExperiencedDreams);
            SeenDungeonVisions = new HashSet<string>(data.SeenDungeonVisions ?? new List<string>());
            _restsSinceLastDream = data.RestsSinceLastDream;
        }

        /// <summary>
        /// Reset all state for a new game
        /// </summary>
        public void Reset()
        {
            ExperiencedDreams = new HashSet<string>();
            SeenDungeonVisions = new HashSet<string>();
            _lastDreamId = "";
            _restsSinceLastDream = 0;
        }
    }

    #region Data Classes

    public class NarrativeDreamData
    {
        public string Id { get; set; } = "";
        public string Title { get; set; } = "";
        public int MinLevel { get; set; } = 0;
        public int MaxLevel { get; set; } = 100;
        public int MinAwakening { get; set; } = 0;
        public int MaxAwakening { get; set; } = 7;
        public int MinCycle { get; set; } = 1;
        public int RequiredFloor { get; set; } = 0;
        public int MaxFloor { get; set; } = 0;
        public int Priority { get; set; } = 10;
        public string[] Content { get; set; } = Array.Empty<string>();
        public int AwakeningGain { get; set; } = 0;
        public WaveFragment? WaveFragment { get; set; }
        public OldGodType? RequiresGodDefeated { get; set; }
        public OldGodType? RequiresGodSaved { get; set; }
        public OldGodType? RequiresGodAllied { get; set; }
        public string? RequiresCompanionDeath { get; set; }
        public string? RequiresCompanionAlive { get; set; }
        public bool RequiresIsKing { get; set; } = false;
        public int MinDarkness { get; set; } = 0;
        public int MinChivalry { get; set; } = 0;
        public bool RequiresMarriage { get; set; } = false;
        public int MinKills { get; set; } = 0;
        public int MinDeepestFloor { get; set; } = 0;
        public bool RequiresAllSeals { get; set; } = false;
        public bool RequiresAnyCompanionQuestDone { get; set; } = false;
        public string PhilosophicalHint { get; set; } = "";
    }

    public class DungeonVision
    {
        public string Id { get; set; } = "";
        public int FloorMin { get; set; }
        public int FloorMax { get; set; }
        public int MinAwakening { get; set; } = 0;
        public string Description { get; set; } = "";
        public string[] Content { get; set; } = Array.Empty<string>();
        public int AwakeningGain { get; set; } = 0;
        public WaveFragment? WaveFragment { get; set; }
    }

    public class DreamSaveData
    {
        public List<string> ExperiencedDreams { get; set; } = new();
        public List<string> SeenDungeonVisions { get; set; } = new();
        public int RestsSinceLastDream { get; set; }
    }

    #endregion
}
