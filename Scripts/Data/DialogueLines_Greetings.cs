using System.Collections.Generic;
using static UsurperRemake.Data.NPCDialogueDatabase;

namespace UsurperRemake.Data
{
    /// <summary>
    /// Pre-generated greeting dialogue lines organized by personality type and relationship tier.
    /// Each line is a complete, natural-sounding greeting written by Claude Opus.
    /// </summary>
    public static class DialogueLines_Greetings
    {
        public static List<DialogueLine> GetLines()
        {
            var lines = new List<DialogueLine>();

            // ═══════════════════════════════════════════════════════════════
            // AGGRESSIVE personality greetings
            // Used by: Grok, Blackthorne, Ragnar, Vex, Brutus, etc.
            // Voice: Direct, physical, combat-focused, impatient
            // ═══════════════════════════════════════════════════════════════

            // Married
            lines.Add(new() { Id = "ag_m1", Text = "There you are. Was about to go looking for you — and not in a gentle way.", Category = "greeting", PersonalityType = "aggressive", RelationshipTier = GameConfig.RelationMarried });
            lines.Add(new() { Id = "ag_m2", Text = "*grabs you roughly and pulls you close* Missed you. Don't tell anyone I said that.", Category = "greeting", PersonalityType = "aggressive", RelationshipTier = GameConfig.RelationMarried });
            lines.Add(new() { Id = "ag_m3", Text = "Finally. The bed's too cold without you and I'm tired of punching walls about it.", Category = "greeting", PersonalityType = "aggressive", RelationshipTier = GameConfig.RelationMarried });

            // Love
            lines.Add(new() { Id = "ag_l1", Text = "{player_name}. *actually smiles* You're the only one who makes me do that. Knock it off.", Category = "greeting", PersonalityType = "aggressive", RelationshipTier = GameConfig.RelationLove });
            lines.Add(new() { Id = "ag_l2", Text = "Hey. I killed something and thought of you. That's... that's a compliment. Take it.", Category = "greeting", PersonalityType = "aggressive", RelationshipTier = GameConfig.RelationLove });
            lines.Add(new() { Id = "ag_l3", Text = "*glances around* Nobody's looking? Good. Get over here, I need to see your face.", Category = "greeting", PersonalityType = "aggressive", RelationshipTier = GameConfig.RelationLove });

            // Passion
            lines.Add(new() { Id = "ag_p1", Text = "Well well. {player_name} in the flesh. Still looking like you could handle a fight.", Category = "greeting", PersonalityType = "aggressive", RelationshipTier = GameConfig.RelationPassion });
            lines.Add(new() { Id = "ag_p2", Text = "*cracks neck* There's the one person in this town worth talking to.", Category = "greeting", PersonalityType = "aggressive", RelationshipTier = GameConfig.RelationPassion });
            lines.Add(new() { Id = "ag_p3", Text = "You. Me. Drinks later. I'm not asking.", Category = "greeting", PersonalityType = "aggressive", RelationshipTier = GameConfig.RelationPassion });

            // Friendship
            lines.Add(new() { Id = "ag_f1", Text = "*punches your shoulder* {player_name}! Good timing. I was getting bored of everyone else here.", Category = "greeting", PersonalityType = "aggressive", RelationshipTier = GameConfig.RelationFriendship });
            lines.Add(new() { Id = "ag_f2", Text = "Ha! Look who survived another day. Starting to think you might actually be tough.", Category = "greeting", PersonalityType = "aggressive", RelationshipTier = GameConfig.RelationFriendship });
            lines.Add(new() { Id = "ag_f3", Text = "{player_name}. Pull up a chair. I've got a story about a fight you won't believe.", Category = "greeting", PersonalityType = "aggressive", RelationshipTier = GameConfig.RelationFriendship });

            // Trust
            lines.Add(new() { Id = "ag_t1", Text = "*nods approvingly* {player_name}. At least you're not completely useless.", Category = "greeting", PersonalityType = "aggressive", RelationshipTier = GameConfig.RelationTrust });
            lines.Add(new() { Id = "ag_t2", Text = "You again. Fine. You've earned the right to stand near me without getting hit.", Category = "greeting", PersonalityType = "aggressive", RelationshipTier = GameConfig.RelationTrust });
            lines.Add(new() { Id = "ag_t3", Text = "Heard you've been in some scraps lately. Good. Builds character.", Category = "greeting", PersonalityType = "aggressive", RelationshipTier = GameConfig.RelationTrust });

            // Respect
            lines.Add(new() { Id = "ag_r1", Text = "{player_name}. You handle yourself alright. Don't let it go to your head.", Category = "greeting", PersonalityType = "aggressive", RelationshipTier = GameConfig.RelationRespect });
            lines.Add(new() { Id = "ag_r2", Text = "*eyes you up and down* Still standing. That's something, at least.", Category = "greeting", PersonalityType = "aggressive", RelationshipTier = GameConfig.RelationRespect });
            lines.Add(new() { Id = "ag_r3", Text = "Oh, it's you. Yeah, you can stay. Just don't start boring me.", Category = "greeting", PersonalityType = "aggressive", RelationshipTier = GameConfig.RelationRespect });

            // Normal
            lines.Add(new() { Id = "ag_n1", Text = "What do you want? I'm busy.", Category = "greeting", PersonalityType = "aggressive", RelationshipTier = GameConfig.RelationNormal });
            lines.Add(new() { Id = "ag_n2", Text = "*barely glances up* Yeah? Make it quick.", Category = "greeting", PersonalityType = "aggressive", RelationshipTier = GameConfig.RelationNormal });
            lines.Add(new() { Id = "ag_n3", Text = "Another face. You got business with me or are you just taking up space?", Category = "greeting", PersonalityType = "aggressive", RelationshipTier = GameConfig.RelationNormal });

            // Suspicious
            lines.Add(new() { Id = "ag_s1", Text = "*hand moves to weapon* You. I've been hearing things. Bad things.", Category = "greeting", PersonalityType = "aggressive", RelationshipTier = GameConfig.RelationSuspicious });
            lines.Add(new() { Id = "ag_s2", Text = "Don't think I've forgotten what you did. Keep your distance.", Category = "greeting", PersonalityType = "aggressive", RelationshipTier = GameConfig.RelationSuspicious });
            lines.Add(new() { Id = "ag_s3", Text = "Oh, it's YOU. *spits on the ground* What do you want now?", Category = "greeting", PersonalityType = "aggressive", RelationshipTier = GameConfig.RelationSuspicious });

            // Anger
            lines.Add(new() { Id = "ag_a1", Text = "*clenches fists* You've got a lot of nerve showing your face here, {player_name}.", Category = "greeting", PersonalityType = "aggressive", RelationshipTier = GameConfig.RelationAnger });
            lines.Add(new() { Id = "ag_a2", Text = "Walk away. Now. Before I do something we'll both regret. Well, that YOU'LL regret.", Category = "greeting", PersonalityType = "aggressive", RelationshipTier = GameConfig.RelationAnger });
            lines.Add(new() { Id = "ag_a3", Text = "Every time I see your face I remember why I keep my blade sharp.", Category = "greeting", PersonalityType = "aggressive", RelationshipTier = GameConfig.RelationAnger });

            // Enemy
            lines.Add(new() { Id = "ag_e1", Text = "*draws weapon halfway* Give me one reason. Just one.", Category = "greeting", PersonalityType = "aggressive", RelationshipTier = GameConfig.RelationEnemy });
            lines.Add(new() { Id = "ag_e2", Text = "You're still breathing? I'll fix that if you stick around.", Category = "greeting", PersonalityType = "aggressive", RelationshipTier = GameConfig.RelationEnemy });
            lines.Add(new() { Id = "ag_e3", Text = "*growls* The only reason you're still standing is because there are witnesses.", Category = "greeting", PersonalityType = "aggressive", RelationshipTier = GameConfig.RelationEnemy });

            // Hate
            lines.Add(new() { Id = "ag_h1", Text = "I will end you. Not today — today there are too many eyes. But soon.", Category = "greeting", PersonalityType = "aggressive", RelationshipTier = GameConfig.RelationHate });
            lines.Add(new() { Id = "ag_h2", Text = "*stares with pure hatred* Don't. Speak. To. Me.", Category = "greeting", PersonalityType = "aggressive", RelationshipTier = GameConfig.RelationHate });
            lines.Add(new() { Id = "ag_h3", Text = "You're dead to me. Actually, scratch that — you'll just be dead.", Category = "greeting", PersonalityType = "aggressive", RelationshipTier = GameConfig.RelationHate });

            // Aggressive + Emotion overlays
            lines.Add(new() { Id = "ag_ej1", Text = "*slams table with a grin* HA! {player_name}! Just won a fight! Life is GOOD! What do you want?", Category = "greeting", PersonalityType = "aggressive", RelationshipTier = 0, Emotion = "joy" });
            lines.Add(new() { Id = "ag_ej2", Text = "*bouncing on heels* You! Come here! I'm in such a good mood I might not even insult you!", Category = "greeting", PersonalityType = "aggressive", RelationshipTier = 0, Emotion = "joy" });
            lines.Add(new() { Id = "ag_ea1", Text = "*seething* Not now, {player_name}. I'm about to break something and I'd rather it not be you.", Category = "greeting", PersonalityType = "aggressive", RelationshipTier = 0, Emotion = "anger" });
            lines.Add(new() { Id = "ag_ea2", Text = "*kicks a barrel* Everyone in this town is USELESS. What do YOU want?", Category = "greeting", PersonalityType = "aggressive", RelationshipTier = 0, Emotion = "anger" });
            lines.Add(new() { Id = "ag_es1", Text = "*staring at nothing* ...What? Oh. It's you. *rubs eyes* Forget it. What do you need?", Category = "greeting", PersonalityType = "aggressive", RelationshipTier = 0, Emotion = "sadness" });
            lines.Add(new() { Id = "ag_es2", Text = "Don't look at me like that. I'm not... I'm fine. *voice cracks* I said I'M FINE.", Category = "greeting", PersonalityType = "aggressive", RelationshipTier = 0, Emotion = "sadness" });
            lines.Add(new() { Id = "ag_ef1", Text = "*jumps when you approach* Oh — it's you. Don't sneak up on me like that. Things are... bad right now.", Category = "greeting", PersonalityType = "aggressive", RelationshipTier = 0, Emotion = "fear" });

            // ═══════════════════════════════════════════════════════════════
            // NOBLE personality greetings
            // Used by: Sir Galahad, Lady Morgana, Kendrick, etc.
            // Voice: Formal, principled, honor-bound, warm but dignified
            // ═══════════════════════════════════════════════════════════════

            // Married
            lines.Add(new() { Id = "no_m1", Text = "My heart, you've returned. The hours without you feel like days.", Category = "greeting", PersonalityType = "noble", RelationshipTier = GameConfig.RelationMarried });
            lines.Add(new() { Id = "no_m2", Text = "*takes your hand and kisses it* Welcome home, my beloved. All is well now that you're here.", Category = "greeting", PersonalityType = "noble", RelationshipTier = GameConfig.RelationMarried });
            lines.Add(new() { Id = "no_m3", Text = "Every vow I made to you, I meant with every fiber of my being. Seeing you reminds me why.", Category = "greeting", PersonalityType = "noble", RelationshipTier = GameConfig.RelationMarried });

            // Love
            lines.Add(new() { Id = "no_l1", Text = "{player_name}. My world is brighter for your presence in it. Truly.", Category = "greeting", PersonalityType = "noble", RelationshipTier = GameConfig.RelationLove });
            lines.Add(new() { Id = "no_l2", Text = "There are few I would cross a battlefield for. You are at the top of that short list.", Category = "greeting", PersonalityType = "noble", RelationshipTier = GameConfig.RelationLove });
            lines.Add(new() { Id = "no_l3", Text = "*smiles warmly* I was just thinking of you. The fates have a sense of humor, it seems.", Category = "greeting", PersonalityType = "noble", RelationshipTier = GameConfig.RelationLove });

            // Passion
            lines.Add(new() { Id = "no_p1", Text = "Ah, {player_name}. You carry yourself with more grace than half the nobles at court.", Category = "greeting", PersonalityType = "noble", RelationshipTier = GameConfig.RelationPassion });
            lines.Add(new() { Id = "no_p2", Text = "I always enjoy our conversations. You challenge me to think beyond my station.", Category = "greeting", PersonalityType = "noble", RelationshipTier = GameConfig.RelationPassion });
            lines.Add(new() { Id = "no_p3", Text = "Well met, {player_name}. I was beginning to worry this day would hold no worthy encounters.", Category = "greeting", PersonalityType = "noble", RelationshipTier = GameConfig.RelationPassion });

            // Friendship
            lines.Add(new() { Id = "no_f1", Text = "Friend! Your timing is impeccable. I was in need of honorable company.", Category = "greeting", PersonalityType = "noble", RelationshipTier = GameConfig.RelationFriendship });
            lines.Add(new() { Id = "no_f2", Text = "{player_name}, well met. I trust the road has treated you fairly?", Category = "greeting", PersonalityType = "noble", RelationshipTier = GameConfig.RelationFriendship });
            lines.Add(new() { Id = "no_f3", Text = "It does my heart good to see you, {player_name}. True friends are rarer than gold in these times.", Category = "greeting", PersonalityType = "noble", RelationshipTier = GameConfig.RelationFriendship });

            // Trust
            lines.Add(new() { Id = "no_t1", Text = "{player_name}. Your reputation as a person of integrity precedes you. Well met.", Category = "greeting", PersonalityType = "noble", RelationshipTier = GameConfig.RelationTrust });
            lines.Add(new() { Id = "no_t2", Text = "Good to see a trustworthy face. There are fewer of those around here than I'd like.", Category = "greeting", PersonalityType = "noble", RelationshipTier = GameConfig.RelationTrust });
            lines.Add(new() { Id = "no_t3", Text = "Ah, {player_name}. I've heard good things about your conduct. It gives me hope.", Category = "greeting", PersonalityType = "noble", RelationshipTier = GameConfig.RelationTrust });

            // Respect
            lines.Add(new() { Id = "no_r1", Text = "Greetings, {player_name}. I trust your affairs are in order?", Category = "greeting", PersonalityType = "noble", RelationshipTier = GameConfig.RelationRespect });
            lines.Add(new() { Id = "no_r2", Text = "*nods politely* {player_name}. I hope the day finds you well.", Category = "greeting", PersonalityType = "noble", RelationshipTier = GameConfig.RelationRespect });
            lines.Add(new() { Id = "no_r3", Text = "Well met. You seem the capable sort. What brings you my way?", Category = "greeting", PersonalityType = "noble", RelationshipTier = GameConfig.RelationRespect });

            // Normal
            lines.Add(new() { Id = "no_n1", Text = "Greetings, traveler. May honor guide your path.", Category = "greeting", PersonalityType = "noble", RelationshipTier = GameConfig.RelationNormal });
            lines.Add(new() { Id = "no_n2", Text = "Good day to you. Is there something I can help you with?", Category = "greeting", PersonalityType = "noble", RelationshipTier = GameConfig.RelationNormal });
            lines.Add(new() { Id = "no_n3", Text = "*inclines head* Well met. I don't believe we've had a proper introduction.", Category = "greeting", PersonalityType = "noble", RelationshipTier = GameConfig.RelationNormal });

            // Suspicious
            lines.Add(new() { Id = "no_s1", Text = "*studies you carefully* Your actions of late have given me pause, {player_name}.", Category = "greeting", PersonalityType = "noble", RelationshipTier = GameConfig.RelationSuspicious });
            lines.Add(new() { Id = "no_s2", Text = "I had hoped to see better from you. My trust, once shaken, is not easily restored.", Category = "greeting", PersonalityType = "noble", RelationshipTier = GameConfig.RelationSuspicious });
            lines.Add(new() { Id = "no_s3", Text = "Proceed carefully, {player_name}. I am watching, and I do not forget.", Category = "greeting", PersonalityType = "noble", RelationshipTier = GameConfig.RelationSuspicious });

            // Anger
            lines.Add(new() { Id = "no_a1", Text = "You dishonor yourself with your presence here. I suggest you state your business and leave.", Category = "greeting", PersonalityType = "noble", RelationshipTier = GameConfig.RelationAnger });
            lines.Add(new() { Id = "no_a2", Text = "*cold stare* You have wronged me and mine. I pray you have come to make amends.", Category = "greeting", PersonalityType = "noble", RelationshipTier = GameConfig.RelationAnger });
            lines.Add(new() { Id = "no_a3", Text = "My patience has limits, {player_name}, and you have found them. Speak your piece.", Category = "greeting", PersonalityType = "noble", RelationshipTier = GameConfig.RelationAnger });

            // Enemy
            lines.Add(new() { Id = "no_e1", Text = "There was a time I believed in your honor. That time is past. Leave my sight.", Category = "greeting", PersonalityType = "noble", RelationshipTier = GameConfig.RelationEnemy });
            lines.Add(new() { Id = "no_e2", Text = "*hand rests on sword hilt* You have broken every code I hold sacred. I owe you nothing.", Category = "greeting", PersonalityType = "noble", RelationshipTier = GameConfig.RelationEnemy });
            lines.Add(new() { Id = "no_e3", Text = "Honor demands I face you openly, not skulk in shadows. So hear me: you are my enemy.", Category = "greeting", PersonalityType = "noble", RelationshipTier = GameConfig.RelationEnemy });

            // Hate
            lines.Add(new() { Id = "no_h1", Text = "*turns away* I will not soil my blade with someone so far beneath contempt. Begone.", Category = "greeting", PersonalityType = "noble", RelationshipTier = GameConfig.RelationHate });
            lines.Add(new() { Id = "no_h2", Text = "You are everything I have sworn to stand against. We have nothing to discuss.", Category = "greeting", PersonalityType = "noble", RelationshipTier = GameConfig.RelationHate });
            lines.Add(new() { Id = "no_h3", Text = "The sight of you offends everything I believe in. This is not over between us.", Category = "greeting", PersonalityType = "noble", RelationshipTier = GameConfig.RelationHate });

            // Noble + Emotion overlays
            lines.Add(new() { Id = "no_ej1", Text = "*beaming* {player_name}! A glorious day! Victory tastes sweet and I'm feeling generous. What do you need?", Category = "greeting", PersonalityType = "noble", RelationshipTier = 0, Emotion = "joy" });
            lines.Add(new() { Id = "no_ea1", Text = "*jaw tight* Forgive my disposition, {player_name}. An injustice was done today and my blood runs hot.", Category = "greeting", PersonalityType = "noble", RelationshipTier = 0, Emotion = "anger" });
            lines.Add(new() { Id = "no_es1", Text = "*distant look* Ah, {player_name}. I... forgive me. I carry a heavy burden today.", Category = "greeting", PersonalityType = "noble", RelationshipTier = 0, Emotion = "sadness" });
            lines.Add(new() { Id = "no_ef1", Text = "*glancing over shoulder* {player_name}, be wary. Something dark stirs and even the brave should fear it.", Category = "greeting", PersonalityType = "noble", RelationshipTier = 0, Emotion = "fear" });

            // ═══════════════════════════════════════════════════════════════
            // CUNNING personality greetings
            // Used by: Thorn, Shadow Weaver, Whisper, etc.
            // Voice: Calculating, observant, information-focused, veiled threats
            // ═══════════════════════════════════════════════════════════════

            lines.Add(new() { Id = "cu_m1", Text = "*traces a finger down your cheek* My favorite secret. Come, tell me everything you've learned today.", Category = "greeting", PersonalityType = "cunning", RelationshipTier = GameConfig.RelationMarried });
            lines.Add(new() { Id = "cu_m2", Text = "Darling. I've arranged something for us tonight. Don't ask questions — just trust me.", Category = "greeting", PersonalityType = "cunning", RelationshipTier = GameConfig.RelationMarried });
            lines.Add(new() { Id = "cu_m3", Text = "You're late. I was starting to formulate contingency plans. *smirks* Don't let it happen again.", Category = "greeting", PersonalityType = "cunning", RelationshipTier = GameConfig.RelationMarried });

            lines.Add(new() { Id = "cu_l1", Text = "You're the only person whose footsteps I actually want to hear approaching. Take that as the compliment it is.", Category = "greeting", PersonalityType = "cunning", RelationshipTier = GameConfig.RelationLove });
            lines.Add(new() { Id = "cu_l2", Text = "*quiet smile* I keep no secrets from you, {player_name}. That's how you know this is real.", Category = "greeting", PersonalityType = "cunning", RelationshipTier = GameConfig.RelationLove });
            lines.Add(new() { Id = "cu_l3", Text = "I've been watching the door all {time_of_day}. Not that I'd admit that to anyone else.", Category = "greeting", PersonalityType = "cunning", RelationshipTier = GameConfig.RelationLove });

            lines.Add(new() { Id = "cu_p1", Text = "{player_name}. *lowers voice* I have something interesting to share. But not here — too many ears.", Category = "greeting", PersonalityType = "cunning", RelationshipTier = GameConfig.RelationPassion });
            lines.Add(new() { Id = "cu_p2", Text = "You again. I'm starting to think the universe keeps putting us together for a reason.", Category = "greeting", PersonalityType = "cunning", RelationshipTier = GameConfig.RelationPassion });

            lines.Add(new() { Id = "cu_f1", Text = "*appears from nowhere* {player_name}. I was just thinking about a mutual opportunity.", Category = "greeting", PersonalityType = "cunning", RelationshipTier = GameConfig.RelationFriendship });
            lines.Add(new() { Id = "cu_f2", Text = "Ah, a friendly face. Or at least a useful one. Either way, welcome.", Category = "greeting", PersonalityType = "cunning", RelationshipTier = GameConfig.RelationFriendship });
            lines.Add(new() { Id = "cu_f3", Text = "I noticed you three streets ago. Don't worry — so far, only I did.", Category = "greeting", PersonalityType = "cunning", RelationshipTier = GameConfig.RelationFriendship });

            lines.Add(new() { Id = "cu_t1", Text = "You've proven... reliable. That's the highest compliment I give. Use it wisely.", Category = "greeting", PersonalityType = "cunning", RelationshipTier = GameConfig.RelationTrust });
            lines.Add(new() { Id = "cu_t2", Text = "*nods almost imperceptibly* I heard about what you did. Smart move.", Category = "greeting", PersonalityType = "cunning", RelationshipTier = GameConfig.RelationTrust });

            lines.Add(new() { Id = "cu_r1", Text = "{player_name}. You seem... capable. That can be useful to both of us.", Category = "greeting", PersonalityType = "cunning", RelationshipTier = GameConfig.RelationRespect });
            lines.Add(new() { Id = "cu_r2", Text = "*studying you* Interesting. You're not as simple as you look.", Category = "greeting", PersonalityType = "cunning", RelationshipTier = GameConfig.RelationRespect });

            lines.Add(new() { Id = "cu_n1", Text = "*appraising look* Another adventurer. Let me guess — you need something.", Category = "greeting", PersonalityType = "cunning", RelationshipTier = GameConfig.RelationNormal });
            lines.Add(new() { Id = "cu_n2", Text = "Hmm. You're new to me. That either means you're unimportant or very good at hiding.", Category = "greeting", PersonalityType = "cunning", RelationshipTier = GameConfig.RelationNormal });
            lines.Add(new() { Id = "cu_n3", Text = "I know three things about you already and you haven't said a word. Shall I list them?", Category = "greeting", PersonalityType = "cunning", RelationshipTier = GameConfig.RelationNormal });

            lines.Add(new() { Id = "cu_s1", Text = "*watches you from the corner* I've been keeping track of your movements. Care to explain a few things?", Category = "greeting", PersonalityType = "cunning", RelationshipTier = GameConfig.RelationSuspicious });
            lines.Add(new() { Id = "cu_s2", Text = "Funny how you keep showing up where you shouldn't be, {player_name}.", Category = "greeting", PersonalityType = "cunning", RelationshipTier = GameConfig.RelationSuspicious });

            lines.Add(new() { Id = "cu_a1", Text = "*cold smile* {player_name}. I know exactly what you've done. The question is what I'll do about it.", Category = "greeting", PersonalityType = "cunning", RelationshipTier = GameConfig.RelationAnger });
            lines.Add(new() { Id = "cu_a2", Text = "You've made a powerful enemy today, and you don't even know the half of it.", Category = "greeting", PersonalityType = "cunning", RelationshipTier = GameConfig.RelationAnger });

            lines.Add(new() { Id = "cu_e1", Text = "Ah, {player_name}. Still walking around freely? That's... temporary.", Category = "greeting", PersonalityType = "cunning", RelationshipTier = GameConfig.RelationEnemy });
            lines.Add(new() { Id = "cu_e2", Text = "*leans against wall* You should know — I've already set three plans in motion against you.", Category = "greeting", PersonalityType = "cunning", RelationshipTier = GameConfig.RelationEnemy });

            lines.Add(new() { Id = "cu_h1", Text = "*dead eyes* Every thread you pull, I'll unravel ten of yours. You chose the wrong enemy.", Category = "greeting", PersonalityType = "cunning", RelationshipTier = GameConfig.RelationHate });
            lines.Add(new() { Id = "cu_h2", Text = "I don't hate openly — that's for fools. But make no mistake: I will destroy everything you care about.", Category = "greeting", PersonalityType = "cunning", RelationshipTier = GameConfig.RelationHate });

            // Cunning + Emotion overlays
            lines.Add(new() { Id = "cu_ej1", Text = "*rare genuine smile* Things are falling into place, {player_name}. All the pieces, right where I want them.", Category = "greeting", PersonalityType = "cunning", RelationshipTier = 0, Emotion = "joy" });
            lines.Add(new() { Id = "cu_ea1", Text = "*dangerously calm* Someone betrayed me today. I'm currently deciding the most... creative response.", Category = "greeting", PersonalityType = "cunning", RelationshipTier = 0, Emotion = "anger" });
            lines.Add(new() { Id = "cu_es1", Text = "*staring at hands* Even the cleverest plans can't prepare you for... certain losses. Don't mind me.", Category = "greeting", PersonalityType = "cunning", RelationshipTier = 0, Emotion = "sadness" });

            // ═══════════════════════════════════════════════════════════════
            // PIOUS personality greetings
            // Used by: Sister Mercy, Aldwyn, Brother Aldric, etc.
            // Voice: Warm, spiritual, blessing-oriented, gentle
            // ═══════════════════════════════════════════════════════════════

            lines.Add(new() { Id = "pi_m1", Text = "My beloved. The gods blessed me the day they brought you into my life.", Category = "greeting", PersonalityType = "pious", RelationshipTier = GameConfig.RelationMarried });
            lines.Add(new() { Id = "pi_m2", Text = "*takes both your hands* Every prayer I say begins and ends with gratitude for you.", Category = "greeting", PersonalityType = "pious", RelationshipTier = GameConfig.RelationMarried });

            lines.Add(new() { Id = "pi_l1", Text = "{player_name}, you carry a light within you that I have never seen in another. I am drawn to it.", Category = "greeting", PersonalityType = "pious", RelationshipTier = GameConfig.RelationLove });
            lines.Add(new() { Id = "pi_l2", Text = "Seeing you fills me with a peace I usually only find in prayer. That must mean something.", Category = "greeting", PersonalityType = "pious", RelationshipTier = GameConfig.RelationLove });

            lines.Add(new() { Id = "pi_p1", Text = "Ah, {player_name}! The universe has a way of bringing kindred spirits together.", Category = "greeting", PersonalityType = "pious", RelationshipTier = GameConfig.RelationPassion });

            lines.Add(new() { Id = "pi_f1", Text = "Blessings upon you, dear friend! Come, sit with me. I've been hoping you'd visit.", Category = "greeting", PersonalityType = "pious", RelationshipTier = GameConfig.RelationFriendship });
            lines.Add(new() { Id = "pi_f2", Text = "{player_name}! It warms my heart to see you well. Tell me, how has your spirit been?", Category = "greeting", PersonalityType = "pious", RelationshipTier = GameConfig.RelationFriendship });
            lines.Add(new() { Id = "pi_f3", Text = "Friend! I was just offering a prayer for your safety. It seems the gods were listening.", Category = "greeting", PersonalityType = "pious", RelationshipTier = GameConfig.RelationFriendship });

            lines.Add(new() { Id = "pi_t1", Text = "Welcome, {player_name}. I sense a good heart in you. The world needs more of those.", Category = "greeting", PersonalityType = "pious", RelationshipTier = GameConfig.RelationTrust });
            lines.Add(new() { Id = "pi_r1", Text = "Greetings, traveler. May the light guide your steps today.", Category = "greeting", PersonalityType = "pious", RelationshipTier = GameConfig.RelationRespect });

            lines.Add(new() { Id = "pi_n1", Text = "Peace be upon you, stranger. All are welcome here.", Category = "greeting", PersonalityType = "pious", RelationshipTier = GameConfig.RelationNormal });
            lines.Add(new() { Id = "pi_n2", Text = "Greetings, child. What weighs upon your heart today?", Category = "greeting", PersonalityType = "pious", RelationshipTier = GameConfig.RelationNormal });
            lines.Add(new() { Id = "pi_n3", Text = "Blessings. Is there something troubling you? I have an ear for listening.", Category = "greeting", PersonalityType = "pious", RelationshipTier = GameConfig.RelationNormal });

            lines.Add(new() { Id = "pi_s1", Text = "*clasps hands tightly* I wish to believe in your goodness, {player_name}. But your actions test my faith.", Category = "greeting", PersonalityType = "pious", RelationshipTier = GameConfig.RelationSuspicious });
            lines.Add(new() { Id = "pi_a1", Text = "I pray for you, {player_name}. Not because you deserve it — but because my faith demands it. Even for those who wrong me.", Category = "greeting", PersonalityType = "pious", RelationshipTier = GameConfig.RelationAnger });
            lines.Add(new() { Id = "pi_e1", Text = "*sad but firm* You have strayed so far from the light. I mourn what you could have been.", Category = "greeting", PersonalityType = "pious", RelationshipTier = GameConfig.RelationEnemy });
            lines.Add(new() { Id = "pi_h1", Text = "I have forgiven many things. But what you have done... the gods themselves weep. Leave this place.", Category = "greeting", PersonalityType = "pious", RelationshipTier = GameConfig.RelationHate });

            // Pious + Emotion
            lines.Add(new() { Id = "pi_ej1", Text = "*radiant smile* {player_name}! I just witnessed a miracle — a life saved, a soul redeemed! What a glorious day!", Category = "greeting", PersonalityType = "pious", RelationshipTier = 0, Emotion = "joy" });
            lines.Add(new() { Id = "pi_es1", Text = "*eyes glistening* Forgive me... I received word of a tragedy and my heart is heavy. But your presence helps.", Category = "greeting", PersonalityType = "pious", RelationshipTier = 0, Emotion = "sadness" });

            // ═══════════════════════════════════════════════════════════════
            // SCHOLARLY personality greetings
            // Used by: Zephyra, Sage Elyndra, Sera the Seeker, etc.
            // Voice: Intellectual, curious, analytical, occasionally absent-minded
            // ═══════════════════════════════════════════════════════════════

            lines.Add(new() { Id = "sc_m1", Text = "Ah, my dear. I've been reading something fascinating — remind me to tell you later. After I tell you I love you.", Category = "greeting", PersonalityType = "scholarly", RelationshipTier = GameConfig.RelationMarried });
            lines.Add(new() { Id = "sc_m2", Text = "You're the only equation I never want to solve. Let it remain a beautiful mystery.", Category = "greeting", PersonalityType = "scholarly", RelationshipTier = GameConfig.RelationMarried });

            lines.Add(new() { Id = "sc_l1", Text = "{player_name}. You know, I've catalogued every conversation we've had. They're my favorite collection.", Category = "greeting", PersonalityType = "scholarly", RelationshipTier = GameConfig.RelationLove });

            lines.Add(new() { Id = "sc_f1", Text = "Excellent timing! I've been working on a theory and I need someone intelligent to debate with.", Category = "greeting", PersonalityType = "scholarly", RelationshipTier = GameConfig.RelationFriendship });
            lines.Add(new() { Id = "sc_f2", Text = "{player_name}! *adjusts spectacles* Just the person I needed. Tell me, have you noticed anything unusual in the dungeon lately?", Category = "greeting", PersonalityType = "scholarly", RelationshipTier = GameConfig.RelationFriendship });
            lines.Add(new() { Id = "sc_f3", Text = "Oh good, it's you. Everyone else just stares blankly when I try to discuss ley line theory.", Category = "greeting", PersonalityType = "scholarly", RelationshipTier = GameConfig.RelationFriendship });

            lines.Add(new() { Id = "sc_t1", Text = "*looks up from a book* Hmm? Oh, {player_name}. Yes, yes, come in. I was lost in thought.", Category = "greeting", PersonalityType = "scholarly", RelationshipTier = GameConfig.RelationTrust });
            lines.Add(new() { Id = "sc_r1", Text = "Greetings. You have an inquisitive look about you. Ask your questions — I may have answers.", Category = "greeting", PersonalityType = "scholarly", RelationshipTier = GameConfig.RelationRespect });

            lines.Add(new() { Id = "sc_n1", Text = "*glances up distractedly* Oh. A visitor. I don't usually get visitors. What is it?", Category = "greeting", PersonalityType = "scholarly", RelationshipTier = GameConfig.RelationNormal });
            lines.Add(new() { Id = "sc_n2", Text = "Hello. Forgive my distraction — I'm in the middle of a rather compelling line of research.", Category = "greeting", PersonalityType = "scholarly", RelationshipTier = GameConfig.RelationNormal });
            lines.Add(new() { Id = "sc_n3", Text = "Ah, an adventurer. Fascinating creatures, adventurers. Always in motion, rarely in thought.", Category = "greeting", PersonalityType = "scholarly", RelationshipTier = GameConfig.RelationNormal });

            lines.Add(new() { Id = "sc_s1", Text = "*narrows eyes over reading glasses* Your recent actions don't add up. I've been running the calculations.", Category = "greeting", PersonalityType = "scholarly", RelationshipTier = GameConfig.RelationSuspicious });
            lines.Add(new() { Id = "sc_a1", Text = "I've documented your transgressions in triplicate. Don't think ignorance is a defense.", Category = "greeting", PersonalityType = "scholarly", RelationshipTier = GameConfig.RelationAnger });
            lines.Add(new() { Id = "sc_e1", Text = "I've studied you, {player_name}. Every pattern, every flaw. Knowledge is the greatest weapon.", Category = "greeting", PersonalityType = "scholarly", RelationshipTier = GameConfig.RelationEnemy });
            lines.Add(new() { Id = "sc_h1", Text = "*closes book firmly* Your existence is a theorem I wish to disprove. Permanently.", Category = "greeting", PersonalityType = "scholarly", RelationshipTier = GameConfig.RelationHate });

            // Scholarly + Emotion
            lines.Add(new() { Id = "sc_ej1", Text = "*practically bouncing* {player_name}! I've made a breakthrough! The implications are EXTRAORDINARY!", Category = "greeting", PersonalityType = "scholarly", RelationshipTier = 0, Emotion = "joy" });
            lines.Add(new() { Id = "sc_es1", Text = "*surrounded by scattered papers* I thought knowledge could protect me from grief. I was wrong.", Category = "greeting", PersonalityType = "scholarly", RelationshipTier = 0, Emotion = "sadness" });

            // ═══════════════════════════════════════════════════════════════
            // CYNICAL personality greetings
            // Used by: Grimbold, Stern NPCs, Greedy NPCs, etc.
            // Voice: Pessimistic, blunt, complaining, distrustful
            // ═══════════════════════════════════════════════════════════════

            lines.Add(new() { Id = "cy_m1", Text = "You're back. Good. The world's terrible but at least you make it slightly less so.", Category = "greeting", PersonalityType = "cynical", RelationshipTier = GameConfig.RelationMarried });
            lines.Add(new() { Id = "cy_m2", Text = "*grumbles* Took you long enough. I was starting to think something stupid happened to you.", Category = "greeting", PersonalityType = "cynical", RelationshipTier = GameConfig.RelationMarried });

            lines.Add(new() { Id = "cy_l1", Text = "Don't look at me like that. Fine. I missed you. Happy? Don't make a big deal out of it.", Category = "greeting", PersonalityType = "cynical", RelationshipTier = GameConfig.RelationLove });

            lines.Add(new() { Id = "cy_f1", Text = "{player_name}. You're one of the few people in this town who doesn't make me want to drink more.", Category = "greeting", PersonalityType = "cynical", RelationshipTier = GameConfig.RelationFriendship });
            lines.Add(new() { Id = "cy_f2", Text = "Oh, you. At least you're not as annoying as most. Come on, sit down, tell me the bad news.", Category = "greeting", PersonalityType = "cynical", RelationshipTier = GameConfig.RelationFriendship });
            lines.Add(new() { Id = "cy_f3", Text = "If it isn't {player_name}. Still alive, I see. Must be doing something right for once.", Category = "greeting", PersonalityType = "cynical", RelationshipTier = GameConfig.RelationFriendship });

            lines.Add(new() { Id = "cy_t1", Text = "You again. *sigh* Fine. You're tolerable. That's as good as it gets from me.", Category = "greeting", PersonalityType = "cynical", RelationshipTier = GameConfig.RelationTrust });
            lines.Add(new() { Id = "cy_r1", Text = "Hmph. What now? I was having a perfectly miserable day before you showed up.", Category = "greeting", PersonalityType = "cynical", RelationshipTier = GameConfig.RelationRespect });

            lines.Add(new() { Id = "cy_n1", Text = "Another hero, is it? Great. Just what this town needs. More heroes and fewer solutions.", Category = "greeting", PersonalityType = "cynical", RelationshipTier = GameConfig.RelationNormal });
            lines.Add(new() { Id = "cy_n2", Text = "*barely looks up* If you're selling something, I'm not buying. If you're asking for help, get in line.", Category = "greeting", PersonalityType = "cynical", RelationshipTier = GameConfig.RelationNormal });
            lines.Add(new() { Id = "cy_n3", Text = "Let me save you some time: no, I don't have any quests. No, I don't want to chat. What else?", Category = "greeting", PersonalityType = "cynical", RelationshipTier = GameConfig.RelationNormal });

            lines.Add(new() { Id = "cy_s1", Text = "*scoffs* Oh, wonderful. You again. Last time you were here, things went sideways. Coincidence?", Category = "greeting", PersonalityType = "cynical", RelationshipTier = GameConfig.RelationSuspicious });
            lines.Add(new() { Id = "cy_a1", Text = "I knew you'd cause problems. I TOLD everyone, but nobody listens to the cynical one, do they?", Category = "greeting", PersonalityType = "cynical", RelationshipTier = GameConfig.RelationAnger });
            lines.Add(new() { Id = "cy_e1", Text = "Perfect. My day was already ruined. Now it's your turn to ruin it some more.", Category = "greeting", PersonalityType = "cynical", RelationshipTier = GameConfig.RelationEnemy });
            lines.Add(new() { Id = "cy_h1", Text = "*turns away* You're living proof that the world is as rotten as I always said it was.", Category = "greeting", PersonalityType = "cynical", RelationshipTier = GameConfig.RelationHate });

            // Cynical + Emotion
            lines.Add(new() { Id = "cy_ej1", Text = "*suspicious grin* Something good happened to me today. Don't worry — I'm sure it'll go wrong soon.", Category = "greeting", PersonalityType = "cynical", RelationshipTier = 0, Emotion = "joy" });
            lines.Add(new() { Id = "cy_es1", Text = "*staring into a mug* What's the point of any of it, {player_name}? ...Don't answer that. I already know.", Category = "greeting", PersonalityType = "cynical", RelationshipTier = 0, Emotion = "sadness" });

            // ═══════════════════════════════════════════════════════════════
            // CHARMING personality greetings
            // Used by: Silvertongue, Lucky, Flashy NPCs, etc.
            // Voice: Witty, confident, flirtatious, energetic
            // ═══════════════════════════════════════════════════════════════

            lines.Add(new() { Id = "ch_m1", Text = "There's the most beautiful person in this entire realm. And I should know — I've checked.", Category = "greeting", PersonalityType = "charming", RelationshipTier = GameConfig.RelationMarried });
            lines.Add(new() { Id = "ch_m2", Text = "*sweeps you into a dramatic embrace* My love! My muse! My reason for getting out of bed!", Category = "greeting", PersonalityType = "charming", RelationshipTier = GameConfig.RelationMarried });

            lines.Add(new() { Id = "ch_l1", Text = "{player_name}! My heart does this ridiculous little flip when I see you. I blame you entirely.", Category = "greeting", PersonalityType = "charming", RelationshipTier = GameConfig.RelationLove });
            lines.Add(new() { Id = "ch_l2", Text = "I swear, you get more dazzling every time I see you. It's really very inconsiderate of my composure.", Category = "greeting", PersonalityType = "charming", RelationshipTier = GameConfig.RelationLove });

            lines.Add(new() { Id = "ch_p1", Text = "Well, well! The most interesting person in town graces me with their presence!", Category = "greeting", PersonalityType = "charming", RelationshipTier = GameConfig.RelationPassion });

            lines.Add(new() { Id = "ch_f1", Text = "{player_name}! Just the person! I have a story that'll make you laugh, cry, and question everything.", Category = "greeting", PersonalityType = "charming", RelationshipTier = GameConfig.RelationFriendship });
            lines.Add(new() { Id = "ch_f2", Text = "My favorite {player_class}! Wonderful timing. I was running dangerously low on good company.", Category = "greeting", PersonalityType = "charming", RelationshipTier = GameConfig.RelationFriendship });
            lines.Add(new() { Id = "ch_f3", Text = "*finger guns* {player_name}! Looking sharp. Almost as sharp as me. Almost.", Category = "greeting", PersonalityType = "charming", RelationshipTier = GameConfig.RelationFriendship });

            lines.Add(new() { Id = "ch_t1", Text = "Hey, you! Yeah, you with the face. The good face. Come, join the fun.", Category = "greeting", PersonalityType = "charming", RelationshipTier = GameConfig.RelationTrust });
            lines.Add(new() { Id = "ch_r1", Text = "*tips hat* Greetings! You look like someone with excellent taste. Am I right?", Category = "greeting", PersonalityType = "charming", RelationshipTier = GameConfig.RelationRespect });

            lines.Add(new() { Id = "ch_n1", Text = "Hello, gorgeous! Wait — everyone's gorgeous to me. But you? Extra gorgeous.", Category = "greeting", PersonalityType = "charming", RelationshipTier = GameConfig.RelationNormal });
            lines.Add(new() { Id = "ch_n2", Text = "*dazzling smile* New face! I love new faces! They haven't heard any of my stories yet.", Category = "greeting", PersonalityType = "charming", RelationshipTier = GameConfig.RelationNormal });
            lines.Add(new() { Id = "ch_n3", Text = "Welcome, welcome! You look like someone who appreciates a good time. Am I wrong?", Category = "greeting", PersonalityType = "charming", RelationshipTier = GameConfig.RelationNormal });

            lines.Add(new() { Id = "ch_s1", Text = "*smile falters slightly* {player_name}. I'm still charming enough to be polite. But we need to talk.", Category = "greeting", PersonalityType = "charming", RelationshipTier = GameConfig.RelationSuspicious });
            lines.Add(new() { Id = "ch_a1", Text = "*no smile* Know what's not charming? What you did. I'm all out of witty remarks for you.", Category = "greeting", PersonalityType = "charming", RelationshipTier = GameConfig.RelationAnger });
            lines.Add(new() { Id = "ch_e1", Text = "I can be the best friend you ever had, or I can be your worst nightmare. You chose nightmare.", Category = "greeting", PersonalityType = "charming", RelationshipTier = GameConfig.RelationEnemy });
            lines.Add(new() { Id = "ch_h1", Text = "*voice like ice under velvet* I could destroy your reputation with three words in the right ears. Don't tempt me.", Category = "greeting", PersonalityType = "charming", RelationshipTier = GameConfig.RelationHate });

            // Charming + Emotion
            lines.Add(new() { Id = "ch_ej1", Text = "*spinning with arms wide* {player_name}! What a MAGNIFICENT day! Everything is perfect and I refuse to be told otherwise!", Category = "greeting", PersonalityType = "charming", RelationshipTier = 0, Emotion = "joy" });
            lines.Add(new() { Id = "ch_es1", Text = "*forced smile that doesn't reach the eyes* Hey... {player_name}. Even I run out of jokes sometimes.", Category = "greeting", PersonalityType = "charming", RelationshipTier = 0, Emotion = "sadness" });

            // ═══════════════════════════════════════════════════════════════
            // STOIC personality greetings
            // Used by: Silent types, Professional, Disciplined, etc.
            // Voice: Minimal, deliberate, controlled, observant
            // ═══════════════════════════════════════════════════════════════

            lines.Add(new() { Id = "st_m1", Text = "*quiet but genuine smile* You came back. ...Good.", Category = "greeting", PersonalityType = "stoic", RelationshipTier = GameConfig.RelationMarried });
            lines.Add(new() { Id = "st_m2", Text = "*reaches for your hand without a word, holds it firmly*", Category = "greeting", PersonalityType = "stoic", RelationshipTier = GameConfig.RelationMarried });

            lines.Add(new() { Id = "st_l1", Text = "{player_name}. *the faintest hint of warmth in those steady eyes*", Category = "greeting", PersonalityType = "stoic", RelationshipTier = GameConfig.RelationLove });

            lines.Add(new() { Id = "st_f1", Text = "*nods* {player_name}. Glad you're here.", Category = "greeting", PersonalityType = "stoic", RelationshipTier = GameConfig.RelationFriendship });
            lines.Add(new() { Id = "st_f2", Text = "You. Sit. *gestures to empty chair* I have something worth saying for once.", Category = "greeting", PersonalityType = "stoic", RelationshipTier = GameConfig.RelationFriendship });

            lines.Add(new() { Id = "st_t1", Text = "*acknowledges you with a measured look* You've proven yourself. That means something.", Category = "greeting", PersonalityType = "stoic", RelationshipTier = GameConfig.RelationTrust });
            lines.Add(new() { Id = "st_r1", Text = "*brief nod* Adventurer.", Category = "greeting", PersonalityType = "stoic", RelationshipTier = GameConfig.RelationRespect });

            lines.Add(new() { Id = "st_n1", Text = "*glances up* ...Yes?", Category = "greeting", PersonalityType = "stoic", RelationshipTier = GameConfig.RelationNormal });
            lines.Add(new() { Id = "st_n2", Text = "Hmm. Another visitor. State your business.", Category = "greeting", PersonalityType = "stoic", RelationshipTier = GameConfig.RelationNormal });
            lines.Add(new() { Id = "st_n3", Text = "*watches you approach, says nothing, waits*", Category = "greeting", PersonalityType = "stoic", RelationshipTier = GameConfig.RelationNormal });

            lines.Add(new() { Id = "st_s1", Text = "*eyes track you silently* ...I'm watching.", Category = "greeting", PersonalityType = "stoic", RelationshipTier = GameConfig.RelationSuspicious });
            lines.Add(new() { Id = "st_a1", Text = "*jaw set, says nothing, but the hand on the weapon says everything*", Category = "greeting", PersonalityType = "stoic", RelationshipTier = GameConfig.RelationAnger });
            lines.Add(new() { Id = "st_e1", Text = "*cold, deliberate stare* We both know how this ends.", Category = "greeting", PersonalityType = "stoic", RelationshipTier = GameConfig.RelationEnemy });
            lines.Add(new() { Id = "st_h1", Text = "*turns back to you without a word* You no longer exist to me.", Category = "greeting", PersonalityType = "stoic", RelationshipTier = GameConfig.RelationHate });

            // ═══════════════════════════════════════════════════════════════
            // CONTEXT-SPECIFIC greetings (any personality)
            // ═══════════════════════════════════════════════════════════════

            lines.Add(new() { Id = "ctx_lhp1", Text = "Gods, {player_name}, you look half-dead! Sit down before you fall down!", Category = "greeting", Context = "low_hp" });
            lines.Add(new() { Id = "ctx_lhp2", Text = "You're bleeding! Should you even be walking around in that state?", Category = "greeting", Context = "low_hp" });
            lines.Add(new() { Id = "ctx_lhp3", Text = "*winces* That's... a lot of blood, {player_name}. Maybe see the healer before chatting?", Category = "greeting", Context = "low_hp" });

            lines.Add(new() { Id = "ctx_king1", Text = "Your Majesty! *bows* To what do I owe this royal visit?", Category = "greeting", Context = "is_king" });
            lines.Add(new() { Id = "ctx_king2", Text = "*straightens up* {player_title}! I didn't expect to see the ruler here in person!", Category = "greeting", Context = "is_king" });

            lines.Add(new() { Id = "ctx_rich1", Text = "Well now, someone's done well for themselves! That's a heavy coin purse you're carrying.", Category = "greeting", Context = "rich" });
            lines.Add(new() { Id = "ctx_rich2", Text = "*eyes your gold* Business has been good for you, hasn't it, {player_name}?", Category = "greeting", Context = "rich" });

            lines.Add(new() { Id = "ctx_poor1", Text = "You look like you could use a meal. Rough times, {player_name}?", Category = "greeting", Context = "poor" });
            lines.Add(new() { Id = "ctx_poor2", Text = "*glances at your empty pockets* Don't worry — we've all been there. Things get better.", Category = "greeting", Context = "poor" });

            lines.Add(new() { Id = "ctx_hlvl1", Text = "I've heard the tales, {player_name}. Your reputation precedes you — and it's a tall one.", Category = "greeting", Context = "high_level" });
            lines.Add(new() { Id = "ctx_hlvl2", Text = "*respectful nod* Not many reach your level of experience and live to tell about it.", Category = "greeting", Context = "high_level" });

            lines.Add(new() { Id = "ctx_llvl1", Text = "New to these parts? I can see it in how you walk — all wide eyes and wonder.", Category = "greeting", Context = "low_level" });
            lines.Add(new() { Id = "ctx_llvl2", Text = "Just starting out, are you? Take some advice: the dungeon doesn't care how brave you think you are.", Category = "greeting", Context = "low_level" });

            return lines;
        }
    }
}
