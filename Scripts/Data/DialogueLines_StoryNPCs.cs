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
            lines.Add(new() { Id = "grok_g1", Text = BuiltInText("grok_g1"), Category = "greeting", NpcName = "Grok the Destroyer", RelationshipTier = GameConfig.RelationFriendship });
            lines.Add(new() { Id = "grok_g2", Text = BuiltInText("grok_g2"), Category = "greeting", NpcName = "Grok the Destroyer", RelationshipTier = GameConfig.RelationTrust });
            lines.Add(new() { Id = "grok_g3", Text = BuiltInText("grok_g3"), Category = "greeting", NpcName = "Grok the Destroyer", RelationshipTier = GameConfig.RelationNormal });
            lines.Add(new() { Id = "grok_g4", Text = BuiltInText("grok_g4"), Category = "greeting", NpcName = "Grok the Destroyer", RelationshipTier = GameConfig.RelationAnger });
            lines.Add(new() { Id = "grok_g5", Text = BuiltInText("grok_g5"), Category = "greeting", NpcName = "Grok the Destroyer", RelationshipTier = GameConfig.RelationLove });
            lines.Add(new() { Id = "grok_g6", Text = BuiltInText("grok_g6"), Category = "greeting", NpcName = "Grok the Destroyer", Emotion = "joy" });
            lines.Add(new() { Id = "grok_g7", Text = BuiltInText("grok_g7"), Category = "greeting", NpcName = "Grok the Destroyer", Emotion = "anger" });

            // Small talk
            lines.Add(new() { Id = "grok_st1", Text = BuiltInText("grok_st1"), Category = "smalltalk", NpcName = "Grok the Destroyer" });
            lines.Add(new() { Id = "grok_st2", Text = BuiltInText("grok_st2"), Category = "smalltalk", NpcName = "Grok the Destroyer" });
            lines.Add(new() { Id = "grok_st3", Text = BuiltInText("grok_st3"), Category = "smalltalk", NpcName = "Grok the Destroyer" });
            lines.Add(new() { Id = "grok_st4", Text = BuiltInText("grok_st4"), Category = "smalltalk", NpcName = "Grok the Destroyer" });

            // Farewells
            lines.Add(new() { Id = "grok_fw1", Text = BuiltInText("grok_fw1"), Category = "farewell", NpcName = "Grok the Destroyer" });
            lines.Add(new() { Id = "grok_fw2", Text = BuiltInText("grok_fw2"), Category = "farewell", NpcName = "Grok the Destroyer", RelationshipTier = GameConfig.RelationFriendship });

            // ═══════════════════════════════════════════════════════════════
            // SIR GALAHAD - Honorable knight, noble and chivalrous
            // ═══════════════════════════════════════════════════════════════

            lines.Add(new() { Id = "galahad_g1", Text = BuiltInText("galahad_g1"), Category = "greeting", NpcName = "Sir Galahad", RelationshipTier = GameConfig.RelationFriendship });
            lines.Add(new() { Id = "galahad_g2", Text = BuiltInText("galahad_g2"), Category = "greeting", NpcName = "Sir Galahad", RelationshipTier = GameConfig.RelationTrust });
            lines.Add(new() { Id = "galahad_g3", Text = BuiltInText("galahad_g3"), Category = "greeting", NpcName = "Sir Galahad", RelationshipTier = GameConfig.RelationSuspicious });
            lines.Add(new() { Id = "galahad_g4", Text = BuiltInText("galahad_g4"), Category = "greeting", NpcName = "Sir Galahad", RelationshipTier = GameConfig.RelationAnger });

            lines.Add(new() { Id = "galahad_st1", Text = BuiltInText("galahad_st1"), Category = "smalltalk", NpcName = "Sir Galahad" });
            lines.Add(new() { Id = "galahad_st2", Text = BuiltInText("galahad_st2"), Category = "smalltalk", NpcName = "Sir Galahad" });
            lines.Add(new() { Id = "galahad_st3", Text = BuiltInText("galahad_st3"), Category = "smalltalk", NpcName = "Sir Galahad" });

            lines.Add(new() { Id = "galahad_fw1", Text = BuiltInText("galahad_fw1"), Category = "farewell", NpcName = "Sir Galahad" });

            // ═══════════════════════════════════════════════════════════════
            // LADY MORGANA - Noble aristocrat, dignified and commanding
            // ═══════════════════════════════════════════════════════════════

            lines.Add(new() { Id = "morgana_g1", Text = BuiltInText("morgana_g1"), Category = "greeting", NpcName = "Lady Morgana", RelationshipTier = GameConfig.RelationFriendship });
            lines.Add(new() { Id = "morgana_g2", Text = BuiltInText("morgana_g2"), Category = "greeting", NpcName = "Lady Morgana", RelationshipTier = GameConfig.RelationRespect });
            lines.Add(new() { Id = "morgana_g3", Text = BuiltInText("morgana_g3"), Category = "greeting", NpcName = "Lady Morgana", RelationshipTier = GameConfig.RelationAnger });

            lines.Add(new() { Id = "morgana_st1", Text = BuiltInText("morgana_st1"), Category = "smalltalk", NpcName = "Lady Morgana" });
            lines.Add(new() { Id = "morgana_st2", Text = BuiltInText("morgana_st2"), Category = "smalltalk", NpcName = "Lady Morgana" });

            lines.Add(new() { Id = "morgana_fw1", Text = BuiltInText("morgana_fw1"), Category = "farewell", NpcName = "Lady Morgana" });

            // ═══════════════════════════════════════════════════════════════
            // LYSANDRA THE PURE / LYSANDRA DAWNWHISPER - Devout healer
            // ═══════════════════════════════════════════════════════════════

            lines.Add(new() { Id = "lysandra_g1", Text = BuiltInText("lysandra_g1"), Category = "greeting", NpcName = "Lysandra the Pure", RelationshipTier = GameConfig.RelationFriendship });
            lines.Add(new() { Id = "lysandra_g2", Text = BuiltInText("lysandra_g2"), Category = "greeting", NpcName = "Lysandra the Pure", RelationshipTier = GameConfig.RelationNormal });
            lines.Add(new() { Id = "lysandra_g3", Text = BuiltInText("lysandra_g3"), Category = "greeting", NpcName = "Lysandra the Pure", Context = "low_hp" });

            lines.Add(new() { Id = "lysandradw_g1", Text = BuiltInText("lysandradw_g1"), Category = "greeting", NpcName = "Lysandra Dawnwhisper", RelationshipTier = GameConfig.RelationFriendship });
            lines.Add(new() { Id = "lysandradw_g2", Text = BuiltInText("lysandradw_g2"), Category = "greeting", NpcName = "Lysandra Dawnwhisper", RelationshipTier = GameConfig.RelationTrust });

            lines.Add(new() { Id = "lysandradw_st1", Text = BuiltInText("lysandradw_st1"), Category = "smalltalk", NpcName = "Lysandra Dawnwhisper" });
            lines.Add(new() { Id = "lysandradw_st2", Text = BuiltInText("lysandradw_st2"), Category = "smalltalk", NpcName = "Lysandra Dawnwhisper" });

            lines.Add(new() { Id = "lysandradw_fw1", Text = BuiltInText("lysandradw_fw1"), Category = "farewell", NpcName = "Lysandra Dawnwhisper" });

            // ═══════════════════════════════════════════════════════════════
            // MORDECAI VOIDBORNE - Brooding dark scholar
            // ═══════════════════════════════════════════════════════════════

            lines.Add(new() { Id = "mordecai_g1", Text = BuiltInText("mordecai_g1"), Category = "greeting", NpcName = "Mordecai Voidborne", RelationshipTier = GameConfig.RelationFriendship });
            lines.Add(new() { Id = "mordecai_g2", Text = BuiltInText("mordecai_g2"), Category = "greeting", NpcName = "Mordecai Voidborne", RelationshipTier = GameConfig.RelationTrust });
            lines.Add(new() { Id = "mordecai_g3", Text = BuiltInText("mordecai_g3"), Category = "greeting", NpcName = "Mordecai Voidborne", RelationshipTier = GameConfig.RelationNormal });

            lines.Add(new() { Id = "mordecai_st1", Text = BuiltInText("mordecai_st1"), Category = "smalltalk", NpcName = "Mordecai Voidborne" });
            lines.Add(new() { Id = "mordecai_st2", Text = BuiltInText("mordecai_st2"), Category = "smalltalk", NpcName = "Mordecai Voidborne" });
            lines.Add(new() { Id = "mordecai_st3", Text = BuiltInText("mordecai_st3"), Category = "smalltalk", NpcName = "Mordecai Voidborne" });

            lines.Add(new() { Id = "mordecai_fw1", Text = BuiltInText("mordecai_fw1"), Category = "farewell", NpcName = "Mordecai Voidborne" });

            // ═══════════════════════════════════════════════════════════════
            // SYLVANA RIVERWIND - Free-spirited nature lover
            // ═══════════════════════════════════════════════════════════════

            lines.Add(new() { Id = "sylvana_g1", Text = BuiltInText("sylvana_g1"), Category = "greeting", NpcName = "Sylvana Riverwind", RelationshipTier = GameConfig.RelationFriendship });
            lines.Add(new() { Id = "sylvana_g2", Text = BuiltInText("sylvana_g2"), Category = "greeting", NpcName = "Sylvana Riverwind", RelationshipTier = GameConfig.RelationNormal });
            lines.Add(new() { Id = "sylvana_g3", Text = BuiltInText("sylvana_g3"), Category = "greeting", NpcName = "Sylvana Riverwind", RelationshipTier = GameConfig.RelationLove });

            lines.Add(new() { Id = "sylvana_st1", Text = BuiltInText("sylvana_st1"), Category = "smalltalk", NpcName = "Sylvana Riverwind" });
            lines.Add(new() { Id = "sylvana_st2", Text = BuiltInText("sylvana_st2"), Category = "smalltalk", NpcName = "Sylvana Riverwind" });
            lines.Add(new() { Id = "sylvana_st3", Text = BuiltInText("sylvana_st3"), Category = "smalltalk", NpcName = "Sylvana Riverwind" });

            lines.Add(new() { Id = "sylvana_fw1", Text = BuiltInText("sylvana_fw1"), Category = "farewell", NpcName = "Sylvana Riverwind" });

            // ═══════════════════════════════════════════════════════════════
            // ARCHPRIEST ALDWYN - Scholarly religious leader
            // ═══════════════════════════════════════════════════════════════

            lines.Add(new() { Id = "aldwyn_g1", Text = BuiltInText("aldwyn_g1"), Category = "greeting", NpcName = "Archpriest Aldwyn", RelationshipTier = GameConfig.RelationFriendship });
            lines.Add(new() { Id = "aldwyn_g2", Text = BuiltInText("aldwyn_g2"), Category = "greeting", NpcName = "Archpriest Aldwyn", RelationshipTier = GameConfig.RelationNormal });

            lines.Add(new() { Id = "aldwyn_st1", Text = BuiltInText("aldwyn_st1"), Category = "smalltalk", NpcName = "Archpriest Aldwyn" });
            lines.Add(new() { Id = "aldwyn_st2", Text = BuiltInText("aldwyn_st2"), Category = "smalltalk", NpcName = "Archpriest Aldwyn" });
            lines.Add(new() { Id = "aldwyn_st3", Text = BuiltInText("aldwyn_st3"), Category = "smalltalk", NpcName = "Archpriest Aldwyn" });

            lines.Add(new() { Id = "aldwyn_fw1", Text = BuiltInText("aldwyn_fw1"), Category = "farewell", NpcName = "Archpriest Aldwyn" });

            // ═══════════════════════════════════════════════════════════════
            // SKARN THE BLOODSWORN - Fanatical warrior-zealot
            // ═══════════════════════════════════════════════════════════════

            lines.Add(new() { Id = "skarn_g1", Text = BuiltInText("skarn_g1"), Category = "greeting", NpcName = "Skarn the Bloodsworn", RelationshipTier = GameConfig.RelationFriendship });
            lines.Add(new() { Id = "skarn_g2", Text = BuiltInText("skarn_g2"), Category = "greeting", NpcName = "Skarn the Bloodsworn", RelationshipTier = GameConfig.RelationNormal });
            lines.Add(new() { Id = "skarn_g3", Text = BuiltInText("skarn_g3"), Category = "greeting", NpcName = "Skarn the Bloodsworn", RelationshipTier = GameConfig.RelationEnemy });

            lines.Add(new() { Id = "skarn_st1", Text = BuiltInText("skarn_st1"), Category = "smalltalk", NpcName = "Skarn the Bloodsworn" });
            lines.Add(new() { Id = "skarn_st2", Text = BuiltInText("skarn_st2"), Category = "smalltalk", NpcName = "Skarn the Bloodsworn" });

            lines.Add(new() { Id = "skarn_fw1", Text = BuiltInText("skarn_fw1"), Category = "farewell", NpcName = "Skarn the Bloodsworn" });

            // ═══════════════════════════════════════════════════════════════
            // WHISPERWIND - Secretive information broker
            // ═══════════════════════════════════════════════════════════════

            lines.Add(new() { Id = "whisper_g1", Text = BuiltInText("whisper_g1"), Category = "greeting", NpcName = "Whisperwind", RelationshipTier = GameConfig.RelationFriendship });
            lines.Add(new() { Id = "whisper_g2", Text = BuiltInText("whisper_g2"), Category = "greeting", NpcName = "Whisperwind", RelationshipTier = GameConfig.RelationTrust });
            lines.Add(new() { Id = "whisper_g3", Text = BuiltInText("whisper_g3"), Category = "greeting", NpcName = "Whisperwind", RelationshipTier = GameConfig.RelationNormal });

            lines.Add(new() { Id = "whisper_st1", Text = BuiltInText("whisper_st1"), Category = "smalltalk", NpcName = "Whisperwind" });
            lines.Add(new() { Id = "whisper_st2", Text = BuiltInText("whisper_st2"), Category = "smalltalk", NpcName = "Whisperwind" });
            lines.Add(new() { Id = "whisper_st3", Text = BuiltInText("whisper_st3"), Category = "smalltalk", NpcName = "Whisperwind" });

            lines.Add(new() { Id = "whisper_fw1", Text = BuiltInText("whisper_fw1"), Category = "farewell", NpcName = "Whisperwind" });

            // ═══════════════════════════════════════════════════════════════
            // SERA THE SEEKER - Obsessed dungeon researcher
            // ═══════════════════════════════════════════════════════════════

            lines.Add(new() { Id = "sera_g1", Text = BuiltInText("sera_g1"), Category = "greeting", NpcName = "Sera the Seeker", RelationshipTier = GameConfig.RelationFriendship });
            lines.Add(new() { Id = "sera_g2", Text = BuiltInText("sera_g2"), Category = "greeting", NpcName = "Sera the Seeker", RelationshipTier = GameConfig.RelationNormal });
            lines.Add(new() { Id = "sera_g3", Text = BuiltInText("sera_g3"), Category = "greeting", NpcName = "Sera the Seeker", Emotion = "joy" });

            lines.Add(new() { Id = "sera_st1", Text = BuiltInText("sera_st1"), Category = "smalltalk", NpcName = "Sera the Seeker" });
            lines.Add(new() { Id = "sera_st2", Text = BuiltInText("sera_st2"), Category = "smalltalk", NpcName = "Sera the Seeker" });
            lines.Add(new() { Id = "sera_st3", Text = BuiltInText("sera_st3"), Category = "smalltalk", NpcName = "Sera the Seeker" });

            lines.Add(new() { Id = "sera_fw1", Text = BuiltInText("sera_fw1"), Category = "farewell", NpcName = "Sera the Seeker" });

            // ═══════════════════════════════════════════════════════════════
            // SIR DARIUS THE LOST - Tormented fallen knight
            // ═══════════════════════════════════════════════════════════════

            lines.Add(new() { Id = "darius_g1", Text = BuiltInText("darius_g1"), Category = "greeting", NpcName = "Sir Darius the Lost", RelationshipTier = GameConfig.RelationFriendship });
            lines.Add(new() { Id = "darius_g2", Text = BuiltInText("darius_g2"), Category = "greeting", NpcName = "Sir Darius the Lost", RelationshipTier = GameConfig.RelationNormal });
            lines.Add(new() { Id = "darius_g3", Text = BuiltInText("darius_g3"), Category = "greeting", NpcName = "Sir Darius the Lost", Emotion = "sadness" });

            lines.Add(new() { Id = "darius_st1", Text = BuiltInText("darius_st1"), Category = "smalltalk", NpcName = "Sir Darius the Lost" });
            lines.Add(new() { Id = "darius_st2", Text = BuiltInText("darius_st2"), Category = "smalltalk", NpcName = "Sir Darius the Lost" });
            lines.Add(new() { Id = "darius_st3", Text = BuiltInText("darius_st3"), Category = "smalltalk", NpcName = "Sir Darius the Lost" });

            lines.Add(new() { Id = "darius_fw1", Text = BuiltInText("darius_fw1"), Category = "farewell", NpcName = "Sir Darius the Lost" });

            // ═══════════════════════════════════════════════════════════════
            // THE WAVESPEAKER - Serene ocean philosopher
            // ═══════════════════════════════════════════════════════════════

            lines.Add(new() { Id = "wave_g1", Text = BuiltInText("wave_g1"), Category = "greeting", NpcName = "The Wavespeaker", RelationshipTier = GameConfig.RelationFriendship });
            lines.Add(new() { Id = "wave_g2", Text = BuiltInText("wave_g2"), Category = "greeting", NpcName = "The Wavespeaker", RelationshipTier = GameConfig.RelationNormal });
            lines.Add(new() { Id = "wave_g3", Text = BuiltInText("wave_g3"), Category = "greeting", NpcName = "The Wavespeaker", RelationshipTier = GameConfig.RelationTrust });

            lines.Add(new() { Id = "wave_st1", Text = BuiltInText("wave_st1"), Category = "smalltalk", NpcName = "The Wavespeaker" });
            lines.Add(new() { Id = "wave_st2", Text = BuiltInText("wave_st2"), Category = "smalltalk", NpcName = "The Wavespeaker" });
            lines.Add(new() { Id = "wave_st3", Text = BuiltInText("wave_st3"), Category = "smalltalk", NpcName = "The Wavespeaker" });

            lines.Add(new() { Id = "wave_fw1", Text = BuiltInText("wave_fw1"), Category = "farewell", NpcName = "The Wavespeaker" });

            // ═══════════════════════════════════════════════════════════════
            // QUICKSILVER QUINN - Charming rogue
            // ═══════════════════════════════════════════════════════════════

            lines.Add(new() { Id = "quinn_g1", Text = BuiltInText("quinn_g1"), Category = "greeting", NpcName = "Quicksilver Quinn", RelationshipTier = GameConfig.RelationFriendship });
            lines.Add(new() { Id = "quinn_g2", Text = BuiltInText("quinn_g2"), Category = "greeting", NpcName = "Quicksilver Quinn", RelationshipTier = GameConfig.RelationTrust });

            lines.Add(new() { Id = "quinn_st1", Text = BuiltInText("quinn_st1"), Category = "smalltalk", NpcName = "Quicksilver Quinn" });
            lines.Add(new() { Id = "quinn_st2", Text = BuiltInText("quinn_st2"), Category = "smalltalk", NpcName = "Quicksilver Quinn" });

            lines.Add(new() { Id = "quinn_fw1", Text = BuiltInText("quinn_fw1"), Category = "farewell", NpcName = "Quicksilver Quinn" });

            // ═══════════════════════════════════════════════════════════════
            // MALACHI THE DARK - Mysterious dark mage
            // ═══════════════════════════════════════════════════════════════

            lines.Add(new() { Id = "malachi_g1", Text = BuiltInText("malachi_g1"), Category = "greeting", NpcName = "Malachi the Dark", RelationshipTier = GameConfig.RelationFriendship });
            lines.Add(new() { Id = "malachi_g2", Text = BuiltInText("malachi_g2"), Category = "greeting", NpcName = "Malachi the Dark", RelationshipTier = GameConfig.RelationNormal });

            lines.Add(new() { Id = "malachi_st1", Text = BuiltInText("malachi_st1"), Category = "smalltalk", NpcName = "Malachi the Dark" });
            lines.Add(new() { Id = "malachi_st2", Text = BuiltInText("malachi_st2"), Category = "smalltalk", NpcName = "Malachi the Dark" });

            lines.Add(new() { Id = "malachi_fw1", Text = BuiltInText("malachi_fw1"), Category = "farewell", NpcName = "Malachi the Dark" });

            // ═══════════════════════════════════════════════════════════════
            // ELARA MOONWHISPER - Wise mystic sage
            // ═══════════════════════════════════════════════════════════════

            lines.Add(new() { Id = "elara_g1", Text = BuiltInText("elara_g1"), Category = "greeting", NpcName = "Elara Moonwhisper", RelationshipTier = GameConfig.RelationFriendship });
            lines.Add(new() { Id = "elara_g2", Text = BuiltInText("elara_g2"), Category = "greeting", NpcName = "Elara Moonwhisper", RelationshipTier = GameConfig.RelationNormal });

            lines.Add(new() { Id = "elara_st1", Text = BuiltInText("elara_st1"), Category = "smalltalk", NpcName = "Elara Moonwhisper" });
            lines.Add(new() { Id = "elara_st2", Text = BuiltInText("elara_st2"), Category = "smalltalk", NpcName = "Elara Moonwhisper" });

            lines.Add(new() { Id = "elara_fw1", Text = BuiltInText("elara_fw1"), Category = "farewell", NpcName = "Elara Moonwhisper" });

            return lines;
        }
    }
}
