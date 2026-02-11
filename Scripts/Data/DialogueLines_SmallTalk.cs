using System.Collections.Generic;
using static UsurperRemake.Data.NPCDialogueDatabase;

namespace UsurperRemake.Data
{
    /// <summary>
    /// Pre-generated small talk dialogue lines organized by personality type.
    /// Each personality has distinct topics and speech patterns.
    /// </summary>
    public static class DialogueLines_SmallTalk
    {
        public static List<DialogueLine> GetLines()
        {
            var lines = new List<DialogueLine>();

            // ═══════════════════════════════════════════════════════════════
            // AGGRESSIVE - Combat stories, boasts, challenges
            // ═══════════════════════════════════════════════════════════════

            lines.Add(new() { Id = "st_ag1", Text = "You ever fight something so big you couldn't see the top of it? I have. Twice. Won both times.", Category = "smalltalk", PersonalityType = "aggressive" });
            lines.Add(new() { Id = "st_ag2", Text = "The monsters in the dungeon are getting stronger. Good. I was getting bored.", Category = "smalltalk", PersonalityType = "aggressive" });
            lines.Add(new() { Id = "st_ag3", Text = "Someone challenged me to a drinking contest last night. Then a fistfight. I won the fistfight.", Category = "smalltalk", PersonalityType = "aggressive" });
            lines.Add(new() { Id = "st_ag4", Text = "You know what I respect? Someone who hits first and apologizes never.", Category = "smalltalk", PersonalityType = "aggressive" });
            lines.Add(new() { Id = "st_ag5", Text = "I don't understand people who talk about peace. Peace is just the boring part between fights.", Category = "smalltalk", PersonalityType = "aggressive" });
            lines.Add(new() { Id = "st_ag6", Text = "My weapon needs sharpening again. That's a good sign — means I'm using it.", Category = "smalltalk", PersonalityType = "aggressive" });
            lines.Add(new() { Id = "st_ag7", Text = "Heard there's something nasty on the deeper floors. I'm going to find it and punch it.", Category = "smalltalk", PersonalityType = "aggressive" });
            lines.Add(new() { Id = "st_ag8", Text = "People keep asking me to 'use my words.' My words are 'get out of the way.'", Category = "smalltalk", PersonalityType = "aggressive" });
            lines.Add(new() { Id = "st_ag9", Text = "You train every day? You should. The dungeon doesn't care about your excuses.", Category = "smalltalk", PersonalityType = "aggressive" });
            lines.Add(new() { Id = "st_ag10", Text = "Best meal I ever had was roasted lizard on floor twenty-something. Earned every bite of it.", Category = "smalltalk", PersonalityType = "aggressive" });

            // ═══════════════════════════════════════════════════════════════
            // NOBLE - Honor, duty, politics, chivalry
            // ═══════════════════════════════════════════════════════════════

            lines.Add(new() { Id = "st_no1", Text = "The state of the kingdom concerns me. We need leaders who lead by example, not by decree.", Category = "smalltalk", PersonalityType = "noble" });
            lines.Add(new() { Id = "st_no2", Text = "I believe every warrior should have a code. Without one, we're no better than the monsters we fight.", Category = "smalltalk", PersonalityType = "noble" });
            lines.Add(new() { Id = "st_no3", Text = "Have you noticed how the common folk look at us adventurers? With hope. We mustn't squander that.", Category = "smalltalk", PersonalityType = "noble" });
            lines.Add(new() { Id = "st_no4", Text = "A true victory isn't one where you merely survive. It's one where you can look at yourself afterward.", Category = "smalltalk", PersonalityType = "noble" });
            lines.Add(new() { Id = "st_no5", Text = "My father told me: 'The measure of a person is what they do when nobody's watching.' I think of that often.", Category = "smalltalk", PersonalityType = "noble" });
            lines.Add(new() { Id = "st_no6", Text = "The throne needs someone worthy. Someone who serves the people, not the other way around.", Category = "smalltalk", PersonalityType = "noble" });
            lines.Add(new() { Id = "st_no7", Text = "I trained with a knight who could defeat anyone in single combat but couldn't lead three men into a tavern. Skill isn't everything.", Category = "smalltalk", PersonalityType = "noble" });
            lines.Add(new() { Id = "st_no8", Text = "These dark times won't last forever. They never do. But we must be the ones to end them.", Category = "smalltalk", PersonalityType = "noble" });
            lines.Add(new() { Id = "st_no9", Text = "I've been thinking about what legacy I'll leave. Not gold or glory — but whether people were better for knowing me.", Category = "smalltalk", PersonalityType = "noble" });
            lines.Add(new() { Id = "st_no10", Text = "The dungeon tests our courage, but the town tests our character. Both matter.", Category = "smalltalk", PersonalityType = "noble" });

            // ═══════════════════════════════════════════════════════════════
            // CUNNING - Rumors, schemes, observations
            // ═══════════════════════════════════════════════════════════════

            lines.Add(new() { Id = "st_cu1", Text = "Interesting thing about this town — everyone has at least two secrets. I know most of them.", Category = "smalltalk", PersonalityType = "cunning" });
            lines.Add(new() { Id = "st_cu2", Text = "The merchant on the corner has been pricing his potions 20% higher since Tuesday. Nobody's noticed. Except me.", Category = "smalltalk", PersonalityType = "cunning" });
            lines.Add(new() { Id = "st_cu3", Text = "You ever watch the guards? Their patrol pattern has a gap every third rotation. Fascinating.", Category = "smalltalk", PersonalityType = "cunning" });
            lines.Add(new() { Id = "st_cu4", Text = "Information is the real currency, {player_name}. Gold comes and goes, but a good secret? That appreciates in value.", Category = "smalltalk", PersonalityType = "cunning" });
            lines.Add(new() { Id = "st_cu5", Text = "Someone in the castle is feeding information to someone in the dark alley. I haven't figured out who yet. Yet.", Category = "smalltalk", PersonalityType = "cunning" });
            lines.Add(new() { Id = "st_cu6", Text = "The dungeon isn't random, you know. There are patterns. Connections. Someone — or something — designed it.", Category = "smalltalk", PersonalityType = "cunning" });
            lines.Add(new() { Id = "st_cu7", Text = "Trust is a vulnerability disguised as a virtue. I prefer... professional agreements.", Category = "smalltalk", PersonalityType = "cunning" });
            lines.Add(new() { Id = "st_cu8", Text = "Three new faces in town this week. One's definitely running from something. The other two? Running towards something worse.", Category = "smalltalk", PersonalityType = "cunning" });
            lines.Add(new() { Id = "st_cu9", Text = "The best plans have contingencies for the contingencies. I'm currently on plan G for the week.", Category = "smalltalk", PersonalityType = "cunning" });
            lines.Add(new() { Id = "st_cu10", Text = "I overheard something last night that would change everything. But I'm not sure who to trust with it.", Category = "smalltalk", PersonalityType = "cunning" });

            // ═══════════════════════════════════════════════════════════════
            // PIOUS - Faith, blessings, morality, philosophy
            // ═══════════════════════════════════════════════════════════════

            lines.Add(new() { Id = "st_pi1", Text = "Have you taken a moment to be grateful today? Even in darkness, there are blessings to count.", Category = "smalltalk", PersonalityType = "pious" });
            lines.Add(new() { Id = "st_pi2", Text = "I was praying this morning and felt something... a presence. Warm. Like being held by the universe itself.", Category = "smalltalk", PersonalityType = "pious" });
            lines.Add(new() { Id = "st_pi3", Text = "Every soul has value, {player_name}. Even the monsters we fight were created for a purpose we don't understand.", Category = "smalltalk", PersonalityType = "pious" });
            lines.Add(new() { Id = "st_pi4", Text = "The temple could use more visitors. People forget that the spirit needs tending as much as the body.", Category = "smalltalk", PersonalityType = "pious" });
            lines.Add(new() { Id = "st_pi5", Text = "I've been counseling a young adventurer who lost a friend in the dungeon. Grief is sacred work.", Category = "smalltalk", PersonalityType = "pious" });
            lines.Add(new() { Id = "st_pi6", Text = "Forgiveness isn't weakness — it's the strongest thing a person can do. I truly believe that.", Category = "smalltalk", PersonalityType = "pious" });
            lines.Add(new() { Id = "st_pi7", Text = "The stars were beautiful last night. I spent an hour just watching them. Makes our troubles feel so small.", Category = "smalltalk", PersonalityType = "pious" });
            lines.Add(new() { Id = "st_pi8", Text = "I worry about the darkness in this world. Not the kind in the dungeon — the kind in people's hearts.", Category = "smalltalk", PersonalityType = "pious" });
            lines.Add(new() { Id = "st_pi9", Text = "A child asked me yesterday why bad things happen to good people. I still don't have a perfect answer.", Category = "smalltalk", PersonalityType = "pious" });
            lines.Add(new() { Id = "st_pi10", Text = "Life is a pilgrimage, not a destination. Every step teaches us something, if we're willing to learn.", Category = "smalltalk", PersonalityType = "pious" });

            // ═══════════════════════════════════════════════════════════════
            // SCHOLARLY - Knowledge, research, theory, discovery
            // ═══════════════════════════════════════════════════════════════

            lines.Add(new() { Id = "st_sc1", Text = "I've been studying the magical resonance patterns in the dungeon. The deeper floors vibrate at a completely different frequency.", Category = "smalltalk", PersonalityType = "scholarly" });
            lines.Add(new() { Id = "st_sc2", Text = "Did you know there are seventeen distinct species of dungeon fungus? Twelve of them will kill you. Three will cure diseases.", Category = "smalltalk", PersonalityType = "scholarly" });
            lines.Add(new() { Id = "st_sc3", Text = "I found a text suggesting the Old Gods aren't gods at all — they're something else entirely. The implications are staggering.", Category = "smalltalk", PersonalityType = "scholarly" });
            lines.Add(new() { Id = "st_sc4", Text = "The metallurgy in ancient weapons fascinates me. Modern smiths can't replicate it. The knowledge was lost... or hidden.", Category = "smalltalk", PersonalityType = "scholarly" });
            lines.Add(new() { Id = "st_sc5", Text = "I've been mapping the dungeon layout mathematically. There's an algorithm to the floor generation. It's beautiful, in its way.", Category = "smalltalk", PersonalityType = "scholarly" });
            lines.Add(new() { Id = "st_sc6", Text = "Most people see a monster and think 'kill it.' I see a monster and think 'what evolutionary pressure created THAT?'", Category = "smalltalk", PersonalityType = "scholarly" });
            lines.Add(new() { Id = "st_sc7", Text = "I stayed up all night reading. Found a reference to a spell that shouldn't be possible according to current magical theory.", Category = "smalltalk", PersonalityType = "scholarly" });
            lines.Add(new() { Id = "st_sc8", Text = "The problem with most adventurers is they don't take notes. You can't learn from an experience you can't remember accurately.", Category = "smalltalk", PersonalityType = "scholarly" });
            lines.Add(new() { Id = "st_sc9", Text = "I have a hypothesis about the seals in the dungeon. They're not locks — they're... stitches. Holding something together.", Category = "smalltalk", PersonalityType = "scholarly" });
            lines.Add(new() { Id = "st_sc10", Text = "There's a fascinating correlation between the phases of the moon and monster spawning in the lower floors. Care to hear more?", Category = "smalltalk", PersonalityType = "scholarly" });

            // ═══════════════════════════════════════════════════════════════
            // CYNICAL - Complaints, pessimism, dark humor
            // ═══════════════════════════════════════════════════════════════

            lines.Add(new() { Id = "st_cy1", Text = "You know what's funny? Nothing. Nothing is funny. I looked.", Category = "smalltalk", PersonalityType = "cynical" });
            lines.Add(new() { Id = "st_cy2", Text = "They raised the price of ale again. The one good thing about this town, and they ruined it.", Category = "smalltalk", PersonalityType = "cynical" });
            lines.Add(new() { Id = "st_cy3", Text = "Another 'hero' came through town yesterday. Lasted about six hours before the dungeon sent them back in pieces.", Category = "smalltalk", PersonalityType = "cynical" });
            lines.Add(new() { Id = "st_cy4", Text = "Someone told me to 'look on the bright side.' I did. It was on fire.", Category = "smalltalk", PersonalityType = "cynical" });
            lines.Add(new() { Id = "st_cy5", Text = "I've lived here long enough to know: if something seems too good to be true, it's usually also on fire.", Category = "smalltalk", PersonalityType = "cynical" });
            lines.Add(new() { Id = "st_cy6", Text = "The economy is terrible, the monsters are everywhere, and my back hurts. Just another day in paradise.", Category = "smalltalk", PersonalityType = "cynical" });
            lines.Add(new() { Id = "st_cy7", Text = "You ever notice how the people with the most money give the least advice worth hearing?", Category = "smalltalk", PersonalityType = "cynical" });
            lines.Add(new() { Id = "st_cy8", Text = "I used to be an optimist. Then I woke up. Now I'm just a realist with a drinking problem.", Category = "smalltalk", PersonalityType = "cynical" });
            lines.Add(new() { Id = "st_cy9", Text = "The king's new policy? Don't get me started. Actually, do. I haven't had a good rant in hours.", Category = "smalltalk", PersonalityType = "cynical" });
            lines.Add(new() { Id = "st_cy10", Text = "Every morning I wake up and think, 'At least it can't get worse.' And every morning, it does.", Category = "smalltalk", PersonalityType = "cynical" });

            // ═══════════════════════════════════════════════════════════════
            // CHARMING - Stories, flirting, humor, adventure
            // ═══════════════════════════════════════════════════════════════

            lines.Add(new() { Id = "st_ch1", Text = "So there I was, surrounded by twelve goblins, wearing nothing but my boots and a smile. Want to hear how it ended?", Category = "smalltalk", PersonalityType = "charming" });
            lines.Add(new() { Id = "st_ch2", Text = "I met the most fascinating person at the inn last night. Actually, I meet the most fascinating person everywhere I go. It's me.", Category = "smalltalk", PersonalityType = "charming" });
            lines.Add(new() { Id = "st_ch3", Text = "Life's too short for bad wine, boring stories, and ugly weapons. I'm currently holding none of the above.", Category = "smalltalk", PersonalityType = "charming" });
            lines.Add(new() { Id = "st_ch4", Text = "Have you tried the new tavern special? Three sips and you'll swear you can speak to trees. Five sips and the trees talk back.", Category = "smalltalk", PersonalityType = "charming" });
            lines.Add(new() { Id = "st_ch5", Text = "I have a gift for making friends. Also enemies. Same skill, different application.", Category = "smalltalk", PersonalityType = "charming" });
            lines.Add(new() { Id = "st_ch6", Text = "Someone once told me I was 'irresponsibly charming.' I took that as a challenge to be even more so.", Category = "smalltalk", PersonalityType = "charming" });
            lines.Add(new() { Id = "st_ch7", Text = "The secret to a good adventure isn't the destination — it's the stories you can tell afterward at the bar.", Category = "smalltalk", PersonalityType = "charming" });
            lines.Add(new() { Id = "st_ch8", Text = "I tried being serious once. Lasted about forty-five seconds. Personal record.", Category = "smalltalk", PersonalityType = "charming" });
            lines.Add(new() { Id = "st_ch9", Text = "People ask why I'm always smiling. Because the alternative involves too much paperwork.", Category = "smalltalk", PersonalityType = "charming" });
            lines.Add(new() { Id = "st_ch10", Text = "You know what this town needs? A festival. Music, dancing, maybe a tournament. I'd organize it, but I'm allergic to responsibility.", Category = "smalltalk", PersonalityType = "charming" });

            // ═══════════════════════════════════════════════════════════════
            // STOIC - Observations, silence, minimal but meaningful
            // ═══════════════════════════════════════════════════════════════

            lines.Add(new() { Id = "st_st1", Text = "...The wind changed direction this morning. Means a storm. Or trouble. Usually both.", Category = "smalltalk", PersonalityType = "stoic" });
            lines.Add(new() { Id = "st_st2", Text = "I sharpened my blade today. Cleaned my armor. Checked my provisions. That's enough for one day.", Category = "smalltalk", PersonalityType = "stoic" });
            lines.Add(new() { Id = "st_st3", Text = "*long pause* ...The town is quieter than yesterday. I don't like quiet.", Category = "smalltalk", PersonalityType = "stoic" });
            lines.Add(new() { Id = "st_st4", Text = "Words are wasted on most things. A look says enough. A sword says the rest.", Category = "smalltalk", PersonalityType = "stoic" });
            lines.Add(new() { Id = "st_st5", Text = "I watched the sunrise this morning. Didn't think about anything. It was... enough.", Category = "smalltalk", PersonalityType = "stoic" });
            lines.Add(new() { Id = "st_st6", Text = "Noticed three new cracks in the south wall. Someone should fix those before something comes through.", Category = "smalltalk", PersonalityType = "stoic" });
            lines.Add(new() { Id = "st_st7", Text = "A warrior asked me for advice. I said: 'survive today.' They wanted more. There isn't more.", Category = "smalltalk", PersonalityType = "stoic" });
            lines.Add(new() { Id = "st_st8", Text = "*stares into middle distance* ...I was just thinking. Never mind. It's not important.", Category = "smalltalk", PersonalityType = "stoic" });
            lines.Add(new() { Id = "st_st9", Text = "Some people fill silence with words. I fill words with silence. Makes them count for more.", Category = "smalltalk", PersonalityType = "stoic" });
            lines.Add(new() { Id = "st_st10", Text = "The dungeon changes people. I've watched it happen. The ones who survive aren't always the strongest — just the most patient.", Category = "smalltalk", PersonalityType = "stoic" });

            return lines;
        }
    }
}
