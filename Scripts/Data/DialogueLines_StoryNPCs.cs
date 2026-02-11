using System.Collections.Generic;
using static UsurperRemake.Data.NPCDialogueDatabase;

namespace UsurperRemake.Data
{
    /// <summary>
    /// NPC-specific dialogue lines for story-important and iconic NPCs.
    /// These override generic personality-based lines when the NPC matches by name.
    /// </summary>
    public static class DialogueLines_StoryNPCs
    {
        public static List<DialogueLine> GetLines()
        {
            var lines = new List<DialogueLine>();

            // ═══════════════════════════════════════════════════════════════
            // GROK THE DESTROYER - Aggressive warrior, loves fighting
            // ═══════════════════════════════════════════════════════════════

            // Greetings
            lines.Add(new() { Id = "grok_g1", Text = "HA! {player_name}! Grok was just telling someone about the time we killed that thing in the dungeon. They didn't believe me. Come, tell them!", Category = "greeting", NpcName = "Grok the Destroyer", RelationshipTier = GameConfig.RelationFriendship });
            lines.Add(new() { Id = "grok_g2", Text = "*flexes* {player_name}! You come to fight? Grok hopes you come to fight. Everything is better with fighting.", Category = "greeting", NpcName = "Grok the Destroyer", RelationshipTier = GameConfig.RelationTrust });
            lines.Add(new() { Id = "grok_g3", Text = "Grok sees you, {player_name}. Grok has decided not to destroy you today. Be grateful.", Category = "greeting", NpcName = "Grok the Destroyer", RelationshipTier = GameConfig.RelationNormal });
            lines.Add(new() { Id = "grok_g4", Text = "*cracks knuckles* YOU. Grok remembers you. Grok's fists remember you too.", Category = "greeting", NpcName = "Grok the Destroyer", RelationshipTier = GameConfig.RelationAnger });
            lines.Add(new() { Id = "grok_g5", Text = "{player_name}! MY FRIEND! *bear hug* Grok missed you! ...Don't tell anyone Grok said that.", Category = "greeting", NpcName = "Grok the Destroyer", RelationshipTier = GameConfig.RelationLove });
            lines.Add(new() { Id = "grok_g6", Text = "*headbutts a wall for emphasis* GROK IS HAVING A GREAT DAY! The dungeon was full of things to destroy!", Category = "greeting", NpcName = "Grok the Destroyer", Emotion = "joy" });
            lines.Add(new() { Id = "grok_g7", Text = "Grok is... *punches table* ANGRY! Someone stole Grok's favorite seat at the tavern!", Category = "greeting", NpcName = "Grok the Destroyer", Emotion = "anger" });

            // Small talk
            lines.Add(new() { Id = "grok_st1", Text = "Grok went to floor twelve yesterday. Killed seventeen things. Would have been eighteen but one was already dead. Grok was disappointed.", Category = "smalltalk", NpcName = "Grok the Destroyer" });
            lines.Add(new() { Id = "grok_st2", Text = "People say Grok is just muscles. Grok also has bones. And teeth. Very sharp teeth.", Category = "smalltalk", NpcName = "Grok the Destroyer" });
            lines.Add(new() { Id = "grok_st3", Text = "The weapon shop tried to sell Grok a sword. Grok does not need sword. Grok IS the weapon.", Category = "smalltalk", NpcName = "Grok the Destroyer" });
            lines.Add(new() { Id = "grok_st4", Text = "You know what Grok's philosophy is? If it moves and shouldn't, hit it. If it doesn't move and should, hit it harder.", Category = "smalltalk", NpcName = "Grok the Destroyer" });

            // Farewells
            lines.Add(new() { Id = "grok_fw1", Text = "Grok goes now. There are things that need destroying. They don't know it yet, but they do.", Category = "farewell", NpcName = "Grok the Destroyer" });
            lines.Add(new() { Id = "grok_fw2", Text = "Try not to die before Grok sees you again. Grok would be... slightly annoyed.", Category = "farewell", NpcName = "Grok the Destroyer", RelationshipTier = GameConfig.RelationFriendship });

            // ═══════════════════════════════════════════════════════════════
            // SIR GALAHAD - Honorable knight, noble and chivalrous
            // ═══════════════════════════════════════════════════════════════

            lines.Add(new() { Id = "galahad_g1", Text = "Ah, {player_name}. It gladdens my heart to see a fellow warrior of honor in these troubled times.", Category = "greeting", NpcName = "Sir Galahad", RelationshipTier = GameConfig.RelationFriendship });
            lines.Add(new() { Id = "galahad_g2", Text = "Greetings, {player_name}. I trust your blade remains sharp and your conscience clear?", Category = "greeting", NpcName = "Sir Galahad", RelationshipTier = GameConfig.RelationTrust });
            lines.Add(new() { Id = "galahad_g3", Text = "I have heard disturbing things about you, {player_name}. I pray they are mere rumors.", Category = "greeting", NpcName = "Sir Galahad", RelationshipTier = GameConfig.RelationSuspicious });
            lines.Add(new() { Id = "galahad_g4", Text = "{player_name}. *hand on sword hilt* We have much to discuss about your conduct.", Category = "greeting", NpcName = "Sir Galahad", RelationshipTier = GameConfig.RelationAnger });

            lines.Add(new() { Id = "galahad_st1", Text = "I have been contemplating the nature of true courage. It is not the absence of fear, but acting rightly despite it.", Category = "smalltalk", NpcName = "Sir Galahad" });
            lines.Add(new() { Id = "galahad_st2", Text = "The dungeon grows darker with each passing day. I fear something ancient stirs in the deep.", Category = "smalltalk", NpcName = "Sir Galahad" });
            lines.Add(new() { Id = "galahad_st3", Text = "A knight's oath is sacred. I have seen too many break theirs for gold or glory. I shall not be among them.", Category = "smalltalk", NpcName = "Sir Galahad" });

            lines.Add(new() { Id = "galahad_fw1", Text = "Walk in the light, {player_name}. The darkness has enough servants.", Category = "farewell", NpcName = "Sir Galahad" });

            // ═══════════════════════════════════════════════════════════════
            // LADY MORGANA - Noble aristocrat, dignified and commanding
            // ═══════════════════════════════════════════════════════════════

            lines.Add(new() { Id = "morgana_g1", Text = "{player_name}. *inclines head regally* I was just considering who might be worthy of my time. You'll do.", Category = "greeting", NpcName = "Lady Morgana", RelationshipTier = GameConfig.RelationFriendship });
            lines.Add(new() { Id = "morgana_g2", Text = "Ah, the {player_class}. I've been watching your progress with... interest. You have potential, if you can refine it.", Category = "greeting", NpcName = "Lady Morgana", RelationshipTier = GameConfig.RelationRespect });
            lines.Add(new() { Id = "morgana_g3", Text = "You dare approach me after what you've done? Your audacity is almost admirable. Almost.", Category = "greeting", NpcName = "Lady Morgana", RelationshipTier = GameConfig.RelationAnger });

            lines.Add(new() { Id = "morgana_st1", Text = "Power is not taken, it is cultivated. Like a garden — with patience, precision, and the occasional pruning of dead weight.", Category = "smalltalk", NpcName = "Lady Morgana" });
            lines.Add(new() { Id = "morgana_st2", Text = "The court politics grow tiresome. Everyone schemes but none have the elegance to do it properly.", Category = "smalltalk", NpcName = "Lady Morgana" });

            lines.Add(new() { Id = "morgana_fw1", Text = "You may go. And {player_name}? Do try to be more interesting next time.", Category = "farewell", NpcName = "Lady Morgana" });

            // ═══════════════════════════════════════════════════════════════
            // LYSANDRA THE PURE / LYSANDRA DAWNWHISPER - Devout healer
            // ═══════════════════════════════════════════════════════════════

            lines.Add(new() { Id = "lysandra_g1", Text = "The light shines upon you today, {player_name}. I can see it in your eyes — you carry a great purpose.", Category = "greeting", NpcName = "Lysandra the Pure", RelationshipTier = GameConfig.RelationFriendship });
            lines.Add(new() { Id = "lysandra_g2", Text = "Welcome, traveler. The temple is open to all who seek healing — of body or spirit.", Category = "greeting", NpcName = "Lysandra the Pure", RelationshipTier = GameConfig.RelationNormal });
            lines.Add(new() { Id = "lysandra_g3", Text = "I sense a shadow upon you, {player_name}. Please... let me help. No one should carry such weight alone.", Category = "greeting", NpcName = "Lysandra the Pure", Context = "low_hp" });

            lines.Add(new() { Id = "lysandradw_g1", Text = "*the air shimmers with warmth as she smiles* {player_name}. Dawn breaks anew, and with it, hope.", Category = "greeting", NpcName = "Lysandra Dawnwhisper", RelationshipTier = GameConfig.RelationFriendship });
            lines.Add(new() { Id = "lysandradw_g2", Text = "I dreamed of your coming, {player_name}. The threads of fate draw us together for a reason.", Category = "greeting", NpcName = "Lysandra Dawnwhisper", RelationshipTier = GameConfig.RelationTrust });

            lines.Add(new() { Id = "lysandradw_st1", Text = "The Old Gods are not evil, not truly. They are... lost. Like children who have forgotten what it means to be loved.", Category = "smalltalk", NpcName = "Lysandra Dawnwhisper" });
            lines.Add(new() { Id = "lysandradw_st2", Text = "Have you listened to the dawn? Truly listened? There is a song in it. The same song the ocean sings.", Category = "smalltalk", NpcName = "Lysandra Dawnwhisper" });

            lines.Add(new() { Id = "lysandradw_fw1", Text = "May the dawn light your path, {player_name}. Even in the deepest dungeon, the sun remembers you.", Category = "farewell", NpcName = "Lysandra Dawnwhisper" });

            // ═══════════════════════════════════════════════════════════════
            // MORDECAI VOIDBORNE - Brooding dark scholar
            // ═══════════════════════════════════════════════════════════════

            lines.Add(new() { Id = "mordecai_g1", Text = "*looks up from ancient tome* {player_name}. You have an uncanny habit of appearing when the darkness grows thickest.", Category = "greeting", NpcName = "Mordecai Voidborne", RelationshipTier = GameConfig.RelationFriendship });
            lines.Add(new() { Id = "mordecai_g2", Text = "The void whispers your name, {player_name}. I choose to take that as a compliment on your behalf.", Category = "greeting", NpcName = "Mordecai Voidborne", RelationshipTier = GameConfig.RelationTrust });
            lines.Add(new() { Id = "mordecai_g3", Text = "Another visitor to disturb my studies. At least you're marginally more tolerable than the last one.", Category = "greeting", NpcName = "Mordecai Voidborne", RelationshipTier = GameConfig.RelationNormal });

            lines.Add(new() { Id = "mordecai_st1", Text = "I've been studying the patterns between the Old God floors. There's a mathematical harmony to the destruction. Beautiful, in a terrible way.", Category = "smalltalk", NpcName = "Mordecai Voidborne" });
            lines.Add(new() { Id = "mordecai_st2", Text = "People fear the dark. They should. But not because of what hides in it — because of what it reveals about ourselves.", Category = "smalltalk", NpcName = "Mordecai Voidborne" });
            lines.Add(new() { Id = "mordecai_st3", Text = "I read something disturbing last night. Then I read it again because it was also fascinating. That's usually how it goes.", Category = "smalltalk", NpcName = "Mordecai Voidborne" });

            lines.Add(new() { Id = "mordecai_fw1", Text = "Go then. The darkness and I have much to discuss. We always do.", Category = "farewell", NpcName = "Mordecai Voidborne" });

            // ═══════════════════════════════════════════════════════════════
            // SYLVANA RIVERWIND - Free-spirited nature lover
            // ═══════════════════════════════════════════════════════════════

            lines.Add(new() { Id = "sylvana_g1", Text = "*laughs with the wind* {player_name}! I just watched the most beautiful sunset from the rooftops. You should try it sometime!", Category = "greeting", NpcName = "Sylvana Riverwind", RelationshipTier = GameConfig.RelationFriendship });
            lines.Add(new() { Id = "sylvana_g2", Text = "Oh! Hello there! Sorry, I was listening to something... never mind. The birds around here tell the most interesting stories.", Category = "greeting", NpcName = "Sylvana Riverwind", RelationshipTier = GameConfig.RelationNormal });
            lines.Add(new() { Id = "sylvana_g3", Text = "{player_name}! *twirls* The river told me you'd come today. I know that sounds strange but the river is rarely wrong.", Category = "greeting", NpcName = "Sylvana Riverwind", RelationshipTier = GameConfig.RelationLove });

            lines.Add(new() { Id = "sylvana_st1", Text = "I tried to grow flowers in the dungeon once. They all died except one — a tiny blue thing that glowed. I left it there. It seemed happy.", Category = "smalltalk", NpcName = "Sylvana Riverwind" });
            lines.Add(new() { Id = "sylvana_st2", Text = "Why do people build walls? The best view is always from the other side of them.", Category = "smalltalk", NpcName = "Sylvana Riverwind" });
            lines.Add(new() { Id = "sylvana_st3", Text = "I collected seventeen different kinds of moss today. People think that's weird but moss is actually very underappreciated.", Category = "smalltalk", NpcName = "Sylvana Riverwind" });

            lines.Add(new() { Id = "sylvana_fw1", Text = "Off to wherever the wind takes me! Or possibly the tavern. The wind usually takes me to the tavern.", Category = "farewell", NpcName = "Sylvana Riverwind" });

            // ═══════════════════════════════════════════════════════════════
            // ARCHPRIEST ALDWYN - Scholarly religious leader
            // ═══════════════════════════════════════════════════════════════

            lines.Add(new() { Id = "aldwyn_g1", Text = "{player_name}. Come in, come in. I've been cross-referencing some ancient texts and I believe I've found something quite remarkable.", Category = "greeting", NpcName = "Archpriest Aldwyn", RelationshipTier = GameConfig.RelationFriendship });
            lines.Add(new() { Id = "aldwyn_g2", Text = "Ah, a visitor. Forgive the mess — the pursuit of knowledge generates rather a lot of parchment.", Category = "greeting", NpcName = "Archpriest Aldwyn", RelationshipTier = GameConfig.RelationNormal });

            lines.Add(new() { Id = "aldwyn_st1", Text = "The Old Gods were not always gods, you know. The texts suggest they were once mortal scholars who gazed too deeply into the truth of creation.", Category = "smalltalk", NpcName = "Archpriest Aldwyn" });
            lines.Add(new() { Id = "aldwyn_st2", Text = "I've catalogued three hundred and twelve interpretations of the Ocean Philosophy. At least four of them are probably correct.", Category = "smalltalk", NpcName = "Archpriest Aldwyn" });
            lines.Add(new() { Id = "aldwyn_st3", Text = "Faith without reason is superstition. Reason without faith is... well, it's still quite useful, honestly. But less interesting.", Category = "smalltalk", NpcName = "Archpriest Aldwyn" });

            lines.Add(new() { Id = "aldwyn_fw1", Text = "May wisdom guide your blade and mercy stay your hand. Now where did I put that footnote...", Category = "farewell", NpcName = "Archpriest Aldwyn" });

            // ═══════════════════════════════════════════════════════════════
            // SKARN THE BLOODSWORN - Fanatical warrior-zealot
            // ═══════════════════════════════════════════════════════════════

            lines.Add(new() { Id = "skarn_g1", Text = "The blood calls, {player_name}. Can you hear it? Every drop spilled in the dungeon sings a hymn of purpose.", Category = "greeting", NpcName = "Skarn the Bloodsworn", RelationshipTier = GameConfig.RelationFriendship });
            lines.Add(new() { Id = "skarn_g2", Text = "*traces a symbol in the air with a scarred finger* {player_name}. You walk with the scent of battle. Good. Good.", Category = "greeting", NpcName = "Skarn the Bloodsworn", RelationshipTier = GameConfig.RelationNormal });
            lines.Add(new() { Id = "skarn_g3", Text = "You! You who mocks the sacred oath! Skarn's blood boils at the sight of you!", Category = "greeting", NpcName = "Skarn the Bloodsworn", RelationshipTier = GameConfig.RelationEnemy });

            lines.Add(new() { Id = "skarn_st1", Text = "I carved my oath into my own flesh so I could never forget it. Some call that madness. I call it commitment.", Category = "smalltalk", NpcName = "Skarn the Bloodsworn" });
            lines.Add(new() { Id = "skarn_st2", Text = "The weak pray to the gods for mercy. The strong become the answer to those prayers. Through blood. Always through blood.", Category = "smalltalk", NpcName = "Skarn the Bloodsworn" });

            lines.Add(new() { Id = "skarn_fw1", Text = "The blood demands tribute. Skarn goes to deliver it. *strides toward the dungeon*", Category = "farewell", NpcName = "Skarn the Bloodsworn" });

            // ═══════════════════════════════════════════════════════════════
            // WHISPERWIND - Secretive information broker
            // ═══════════════════════════════════════════════════════════════

            lines.Add(new() { Id = "whisper_g1", Text = "*appears from nowhere* {player_name}. I know why you're here. I always know. The question is whether you can afford the answer.", Category = "greeting", NpcName = "Whisperwind", RelationshipTier = GameConfig.RelationFriendship });
            lines.Add(new() { Id = "whisper_g2", Text = "*leans against wall, watching* Interesting company you've been keeping, {player_name}. Very interesting.", Category = "greeting", NpcName = "Whisperwind", RelationshipTier = GameConfig.RelationTrust });
            lines.Add(new() { Id = "whisper_g3", Text = "*barely visible in the shadows* ...You shouldn't be able to see me. Hmm. You're more perceptive than most.", Category = "greeting", NpcName = "Whisperwind", RelationshipTier = GameConfig.RelationNormal });

            lines.Add(new() { Id = "whisper_st1", Text = "Secrets are currency, {player_name}. And this town is rich beyond measure — everyone has something to hide.", Category = "smalltalk", NpcName = "Whisperwind" });
            lines.Add(new() { Id = "whisper_st2", Text = "I heard something on the wind last night. Something about the deep floors. Something that made even me uncomfortable.", Category = "smalltalk", NpcName = "Whisperwind" });
            lines.Add(new() { Id = "whisper_st3", Text = "Three people tried to follow me yesterday. I let the first two think they succeeded. The third... well. Don't ask about the third.", Category = "smalltalk", NpcName = "Whisperwind" });

            lines.Add(new() { Id = "whisper_fw1", Text = "*steps backward into shadows* I was never here. Remember that.", Category = "farewell", NpcName = "Whisperwind" });

            // ═══════════════════════════════════════════════════════════════
            // SERA THE SEEKER - Obsessed dungeon researcher
            // ═══════════════════════════════════════════════════════════════

            lines.Add(new() { Id = "sera_g1", Text = "{player_name}! Perfect timing! I just mapped a section of floor thirty-seven that contradicts EVERYTHING I thought I knew about dungeon topology!", Category = "greeting", NpcName = "Sera the Seeker", RelationshipTier = GameConfig.RelationFriendship });
            lines.Add(new() { Id = "sera_g2", Text = "*surrounded by maps and notes* Oh! A person! Sorry, I've been down in the dungeon for... what day is it? Never mind, it doesn't matter. Look at THIS!", Category = "greeting", NpcName = "Sera the Seeker", RelationshipTier = GameConfig.RelationNormal });
            lines.Add(new() { Id = "sera_g3", Text = "{player_name}! *grabs your arm* Have you been past floor forty recently? I need someone to confirm what I found because if I'm right... if I'm RIGHT...", Category = "greeting", NpcName = "Sera the Seeker", Emotion = "joy" });

            lines.Add(new() { Id = "sera_st1", Text = "The dungeon rearranges itself, you know. Not randomly — there's a PATTERN. I've been tracking it for months. The rooms move when nobody's watching.", Category = "smalltalk", NpcName = "Sera the Seeker" });
            lines.Add(new() { Id = "sera_st2", Text = "I found writing on a wall on floor twenty-two. Ancient script. It said... well, it roughly translates to 'turn back.' Very unhelpful. I went deeper, obviously.", Category = "smalltalk", NpcName = "Sera the Seeker" });
            lines.Add(new() { Id = "sera_st3", Text = "Everyone asks me why I'm obsessed with the dungeon. It's not obsession. It's... okay, it might be obsession. But it's PRODUCTIVE obsession.", Category = "smalltalk", NpcName = "Sera the Seeker" });

            lines.Add(new() { Id = "sera_fw1", Text = "I need to get back down there. The dungeon won't map itself. Well, actually, it sort of does, but in the WRONG direction.", Category = "farewell", NpcName = "Sera the Seeker" });

            // ═══════════════════════════════════════════════════════════════
            // SIR DARIUS THE LOST - Tormented fallen knight
            // ═══════════════════════════════════════════════════════════════

            lines.Add(new() { Id = "darius_g1", Text = "*stares into the distance* {player_name}. Sometimes I wonder if we are all lost, and the only difference is whether we've noticed.", Category = "greeting", NpcName = "Sir Darius the Lost", RelationshipTier = GameConfig.RelationFriendship });
            lines.Add(new() { Id = "darius_g2", Text = "Another soul wandering these halls. At least you seem to know where you're going. That's more than I can say for myself.", Category = "greeting", NpcName = "Sir Darius the Lost", RelationshipTier = GameConfig.RelationNormal });
            lines.Add(new() { Id = "darius_g3", Text = "*winces at some inner pain* {player_name}. Forgive me. The ghosts are loud today.", Category = "greeting", NpcName = "Sir Darius the Lost", Emotion = "sadness" });

            lines.Add(new() { Id = "darius_st1", Text = "I was a knight of the realm once. Honors, titles, a family crest. All of it means nothing when you've seen what I've seen in the deep floors.", Category = "smalltalk", NpcName = "Sir Darius the Lost" });
            lines.Add(new() { Id = "darius_st2", Text = "They call me 'the Lost.' But I didn't lose my way. I found a truth I wasn't ready for. There's a difference.", Category = "smalltalk", NpcName = "Sir Darius the Lost" });
            lines.Add(new() { Id = "darius_st3", Text = "Do you hear them sometimes? The voices from below? No? ...Good. Keep it that way.", Category = "smalltalk", NpcName = "Sir Darius the Lost" });

            lines.Add(new() { Id = "darius_fw1", Text = "I'll keep wandering. It's what I do. Perhaps one day I'll find what I lost. Or perhaps I already have, and just don't recognize it.", Category = "farewell", NpcName = "Sir Darius the Lost" });

            // ═══════════════════════════════════════════════════════════════
            // THE WAVESPEAKER - Serene ocean philosopher
            // ═══════════════════════════════════════════════════════════════

            lines.Add(new() { Id = "wave_g1", Text = "*eyes like deep water* {player_name}. The tide brought you here. Do you feel it? The pull of something greater than yourself?", Category = "greeting", NpcName = "The Wavespeaker", RelationshipTier = GameConfig.RelationFriendship });
            lines.Add(new() { Id = "wave_g2", Text = "Be still. Listen. *closes eyes* The ocean is far from here, yet I hear it in everything. Even in you.", Category = "greeting", NpcName = "The Wavespeaker", RelationshipTier = GameConfig.RelationNormal });
            lines.Add(new() { Id = "wave_g3", Text = "Ah, {player_name}. Your soul ripples like a stone dropped in still water. You are changing. Can you feel it?", Category = "greeting", NpcName = "The Wavespeaker", RelationshipTier = GameConfig.RelationTrust });

            lines.Add(new() { Id = "wave_st1", Text = "The ocean does not fight the shore. It embraces it, again and again, patient beyond all understanding. We could learn from this.", Category = "smalltalk", NpcName = "The Wavespeaker" });
            lines.Add(new() { Id = "wave_st2", Text = "Every wave that breaks was once part of the deep. Every truth you discover was always inside you. The dungeon merely reveals what was already there.", Category = "smalltalk", NpcName = "The Wavespeaker" });
            lines.Add(new() { Id = "wave_st3", Text = "The Old Gods fear the ocean. Not because it is powerful, but because it is patient. It was here before them. It will be here after.", Category = "smalltalk", NpcName = "The Wavespeaker" });

            lines.Add(new() { Id = "wave_fw1", Text = "Go now. Flow where you must. The ocean asks nothing of its waves but that they return.", Category = "farewell", NpcName = "The Wavespeaker" });

            // ═══════════════════════════════════════════════════════════════
            // QUICKSILVER QUINN - Charming rogue
            // ═══════════════════════════════════════════════════════════════

            lines.Add(new() { Id = "quinn_g1", Text = "*flashes a dazzling smile* {player_name}! My absolute favorite person! At least for the next five minutes. After that, we'll see.", Category = "greeting", NpcName = "Quicksilver Quinn", RelationshipTier = GameConfig.RelationFriendship });
            lines.Add(new() { Id = "quinn_g2", Text = "Well, well, well. If it isn't the famous {player_name}. I'd curtsy but my back's still sore from that treasure chest incident.", Category = "greeting", NpcName = "Quicksilver Quinn", RelationshipTier = GameConfig.RelationTrust });

            lines.Add(new() { Id = "quinn_st1", Text = "I tried going legitimate once. Lasted three hours. Turns out honest work is even more exhausting than everyone says.", Category = "smalltalk", NpcName = "Quicksilver Quinn" });
            lines.Add(new() { Id = "quinn_st2", Text = "You know what the difference is between a thief and a merchant? Marketing. That's literally it.", Category = "smalltalk", NpcName = "Quicksilver Quinn" });

            lines.Add(new() { Id = "quinn_fw1", Text = "*winks* Time to go make some questionable decisions. Don't wait up!", Category = "farewell", NpcName = "Quicksilver Quinn" });

            // ═══════════════════════════════════════════════════════════════
            // MALACHI THE DARK - Mysterious dark mage
            // ═══════════════════════════════════════════════════════════════

            lines.Add(new() { Id = "malachi_g1", Text = "*shadows seem to deepen around him* {player_name}. I felt your presence before I saw you. The dark has a way of... noticing things.", Category = "greeting", NpcName = "Malachi the Dark", RelationshipTier = GameConfig.RelationFriendship });
            lines.Add(new() { Id = "malachi_g2", Text = "You approach a practitioner of the dark arts voluntarily. Either brave or foolish. In my experience, the distinction hardly matters.", Category = "greeting", NpcName = "Malachi the Dark", RelationshipTier = GameConfig.RelationNormal });

            lines.Add(new() { Id = "malachi_st1", Text = "Light and darkness are not opposites. They are the same force, viewed from different angles. Most are too afraid to look from mine.", Category = "smalltalk", NpcName = "Malachi the Dark" });
            lines.Add(new() { Id = "malachi_st2", Text = "The Necromancer, Mord, thinks we are kindred spirits. He is mistaken. I study the darkness. He lets it consume him. A crucial difference.", Category = "smalltalk", NpcName = "Malachi the Dark" });

            lines.Add(new() { Id = "malachi_fw1", Text = "The shadows have more to teach me. As do you, I suspect — in time.", Category = "farewell", NpcName = "Malachi the Dark" });

            // ═══════════════════════════════════════════════════════════════
            // ELARA MOONWHISPER - Wise mystic sage
            // ═══════════════════════════════════════════════════════════════

            lines.Add(new() { Id = "elara_g1", Text = "*moonlight seems to follow her gaze* {player_name}. The stars spoke of your coming. They speak of many things, if one has the patience to listen.", Category = "greeting", NpcName = "Elara Moonwhisper", RelationshipTier = GameConfig.RelationFriendship });
            lines.Add(new() { Id = "elara_g2", Text = "Welcome, seeker. Your questions are written on your face. Ask, and I will share what wisdom the moon has granted me.", Category = "greeting", NpcName = "Elara Moonwhisper", RelationshipTier = GameConfig.RelationNormal });

            lines.Add(new() { Id = "elara_st1", Text = "The moon sees all but judges nothing. I aspire to the same, though I confess... some days are harder than others.", Category = "smalltalk", NpcName = "Elara Moonwhisper" });
            lines.Add(new() { Id = "elara_st2", Text = "There is a place in the dungeon where time moves differently. I sat there for an hour and when I returned, three days had passed. Or was it the reverse? Fascinating either way.", Category = "smalltalk", NpcName = "Elara Moonwhisper" });

            lines.Add(new() { Id = "elara_fw1", Text = "The moon will watch over you, {player_name}. It watches over all of us, even when we cannot see it.", Category = "farewell", NpcName = "Elara Moonwhisper" });

            return lines;
        }
    }
}
