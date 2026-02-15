using System.Collections.Generic;

namespace UsurperRemake.Data
{
    /// <summary>
    /// Classic Usurper NPCs with original-style names and personalities
    /// Based on the classic BBS door game NPCs
    ///
    /// LORE: These 60 NPCs populate the world of Dorashire. Most are ordinary
    /// adventurers seeking fortune in the dungeons, unaware of the divine drama
    /// unfolding around them. However, 10 "Story NPCs" have deeper connections
    /// to the Old Gods narrative:
    /// - The Stranger (Noctura in disguise)
    /// - Lysandra, Mordecai, Sylvana (romance options tied to ending paths)
    /// - Archpriest Aldwyn (knows about Aurelion)
    /// - Skarn the Bloodsworn (Maelketh's champion)
    /// - Whisperwind (Noctura's spy network)
    /// - Sera the Seeker (knows about the Seven Seals)
    /// - Sir Darius the Lost (fallen Aurelion paladin)
    /// - The Wavespeaker (knows the Ocean Philosophy truth)
    /// </summary>
    public static class ClassicNPCs
    {
        public static List<NPCTemplate> GetClassicNPCs()
        {
            return new List<NPCTemplate>
            {
                // ═══════════════════════════════════════════════════════════════════
                // Fighters & Warriors (10)
                // ═══════════════════════════════════════════════════════════════════
                new() { Name = "Grok the Destroyer", Class = CharacterClass.Warrior, Race = CharacterRace.Orc,
                    Personality = "Aggressive", Alignment = "Evil", StartLevel = 5,
                    Gender = GenderIdentity.Male, Orientation = SexualOrientation.Straight,
                    IntimateStyle = RomanceStyle.Dominant, RelationshipPref = RelationshipPreference.CasualOnly,
                    Passion = 0.9f, Sensuality = 0.7f },

                new() { Name = "Sir Galahad", Class = CharacterClass.Paladin, Race = CharacterRace.Human,
                    Personality = "Honorable", Alignment = "Good", StartLevel = 7,
                    Gender = GenderIdentity.Male, Orientation = SexualOrientation.Straight,
                    IntimateStyle = RomanceStyle.Vanilla, RelationshipPref = RelationshipPreference.Monogamous,
                    Romanticism = 0.9f, Passion = 0.6f },

                new() { Name = "Blackthorne", Class = CharacterClass.Warrior, Race = CharacterRace.Human,
                    Personality = "Ruthless", Alignment = "Evil", StartLevel = 6,
                    Gender = GenderIdentity.Male, Orientation = SexualOrientation.Straight,
                    IntimateStyle = RomanceStyle.Dominant, RelationshipPref = RelationshipPreference.OpenRelationship,
                    Passion = 0.8f, Adventurousness = 0.6f },

                new() { Name = "Ursula Ironheart", Class = CharacterClass.Warrior, Race = CharacterRace.Dwarf,
                    Personality = "Brave", Alignment = "Neutral", StartLevel = 5,
                    Gender = GenderIdentity.Female, Orientation = SexualOrientation.Straight,
                    IntimateStyle = RomanceStyle.Switch, RelationshipPref = RelationshipPreference.Monogamous,
                    Passion = 0.7f, Romanticism = 0.6f },

                new() { Name = "Ragnar Bloodaxe", Class = CharacterClass.Barbarian, Race = CharacterRace.Human,
                    Personality = "Fierce", Alignment = "Neutral", StartLevel = 6,
                    Gender = GenderIdentity.Male, Orientation = SexualOrientation.Straight,
                    IntimateStyle = RomanceStyle.Dominant, RelationshipPref = RelationshipPreference.Polyamorous,
                    Passion = 0.95f, Sensuality = 0.8f, Adventurousness = 0.7f },

                new() { Name = "Lady Morgana", Class = CharacterClass.Paladin, Race = CharacterRace.Human,
                    Personality = "Noble", Alignment = "Good", StartLevel = 7,
                    Gender = GenderIdentity.Female, Orientation = SexualOrientation.Straight,
                    IntimateStyle = RomanceStyle.Switch, RelationshipPref = RelationshipPreference.Monogamous,
                    Romanticism = 0.85f, Passion = 0.7f, Sensuality = 0.6f },

                new() { Name = "Grimbold Stonefist", Class = CharacterClass.Warrior, Race = CharacterRace.Dwarf,
                    Personality = "Stubborn", Alignment = "Good", StartLevel = 5,
                    Gender = GenderIdentity.Male, Orientation = SexualOrientation.Straight,
                    IntimateStyle = RomanceStyle.Vanilla, RelationshipPref = RelationshipPreference.Monogamous,
                    Romanticism = 0.5f, Passion = 0.6f },

                new() { Name = "Vex the Merciless", Class = CharacterClass.Warrior, Race = CharacterRace.Orc,
                    Personality = "Cruel", Alignment = "Evil", StartLevel = 6,
                    Gender = GenderIdentity.Female, Orientation = SexualOrientation.Straight,
                    IntimateStyle = RomanceStyle.Dominant, RelationshipPref = RelationshipPreference.OpenRelationship,
                    Passion = 0.85f, Adventurousness = 0.9f, Sensuality = 0.8f },

                new() { Name = "Thorn Blackblade", Class = CharacterClass.Assassin, Race = CharacterRace.Elf,
                    Personality = "Cunning", Alignment = "Evil", StartLevel = 8,
                    Gender = GenderIdentity.Male, Orientation = SexualOrientation.Gay,
                    IntimateStyle = RomanceStyle.Switch, RelationshipPref = RelationshipPreference.FriendsWithBenefits,
                    Passion = 0.7f, Sensuality = 0.75f, Adventurousness = 0.8f },

                new() { Name = "Borin Hammerhand", Class = CharacterClass.Warrior, Race = CharacterRace.Dwarf,
                    Personality = "Loyal", Alignment = "Good", StartLevel = 5,
                    Gender = GenderIdentity.Male, Orientation = SexualOrientation.Straight,
                    IntimateStyle = RomanceStyle.Vanilla, RelationshipPref = RelationshipPreference.Monogamous,
                    Romanticism = 0.7f, Passion = 0.6f },

                // ═══════════════════════════════════════════════════════════════════
                // Mages & Wizards (10)
                // ═══════════════════════════════════════════════════════════════════
                new() { Name = "Malachi the Dark", Class = CharacterClass.Magician, Race = CharacterRace.Human,
                    Personality = "Mysterious", Alignment = "Evil", StartLevel = 9,
                    Gender = GenderIdentity.Male, Orientation = SexualOrientation.Straight,
                    IntimateStyle = RomanceStyle.Dominant, RelationshipPref = RelationshipPreference.OpenRelationship,
                    Passion = 0.7f, Adventurousness = 0.85f, Sensuality = 0.75f },

                new() { Name = "Elara Moonwhisper", Class = CharacterClass.Magician, Race = CharacterRace.Elf,
                    Personality = "Wise", Alignment = "Good", StartLevel = 8,
                    Gender = GenderIdentity.Female, Orientation = SexualOrientation.Straight,
                    IntimateStyle = RomanceStyle.Switch, RelationshipPref = RelationshipPreference.Polyamorous,
                    Romanticism = 0.8f, Sensuality = 0.85f, Passion = 0.7f },

                new() { Name = "Zoltar the Arcane", Class = CharacterClass.Magician, Race = CharacterRace.Human,
                    Personality = "Arrogant", Alignment = "Neutral", StartLevel = 10,
                    Gender = GenderIdentity.Male, Orientation = SexualOrientation.Straight,
                    IntimateStyle = RomanceStyle.Dominant, RelationshipPref = RelationshipPreference.CasualOnly,
                    Passion = 0.5f, Sensuality = 0.4f },

                new() { Name = "Ravenna Shadowmoon", Class = CharacterClass.Magician, Race = CharacterRace.Elf,
                    Personality = "Scheming", Alignment = "Evil", StartLevel = 9,
                    Gender = GenderIdentity.Female, Orientation = SexualOrientation.Straight,
                    IntimateStyle = RomanceStyle.Dominant, RelationshipPref = RelationshipPreference.OpenRelationship,
                    Passion = 0.8f, Adventurousness = 0.9f, Sensuality = 0.85f },

                new() { Name = "Aldric Stormcaller", Class = CharacterClass.Magician, Race = CharacterRace.Human,
                    Personality = "Eccentric", Alignment = "Neutral", StartLevel = 8,
                    Gender = GenderIdentity.Male, Orientation = SexualOrientation.Straight,
                    IntimateStyle = RomanceStyle.Adventurous, RelationshipPref = RelationshipPreference.Polyamorous,
                    Adventurousness = 0.95f, Sensuality = 0.7f, Passion = 0.8f },

                new() { Name = "Seraphina Lightbringer", Class = CharacterClass.Cleric, Race = CharacterRace.Elf,
                    Personality = "Compassionate", Alignment = "Good", StartLevel = 7,
                    Gender = GenderIdentity.Female, Orientation = SexualOrientation.Straight,
                    IntimateStyle = RomanceStyle.Submissive, RelationshipPref = RelationshipPreference.Monogamous,
                    Romanticism = 0.95f, Sensuality = 0.7f, Passion = 0.75f },

                new() { Name = "Mord the Necromancer", Class = CharacterClass.Magician, Race = CharacterRace.Human,
                    Personality = "Sinister", Alignment = "Evil", StartLevel = 11,
                    Gender = GenderIdentity.Male, Orientation = SexualOrientation.Straight,
                    IntimateStyle = RomanceStyle.Dominant, RelationshipPref = RelationshipPreference.OpenRelationship,
                    Adventurousness = 0.9f, Passion = 0.6f },

                new() { Name = "Thalia Starweaver", Class = CharacterClass.Magician, Race = CharacterRace.Elf,
                    Personality = "Curious", Alignment = "Neutral", StartLevel = 8,
                    Gender = GenderIdentity.Female, Orientation = SexualOrientation.Straight,
                    IntimateStyle = RomanceStyle.Adventurous, RelationshipPref = RelationshipPreference.Polyamorous,
                    Adventurousness = 0.9f, Sensuality = 0.8f, Romanticism = 0.7f },

                new() { Name = "Caius Bloodstone", Class = CharacterClass.Magician, Race = CharacterRace.Human,
                    Personality = "Ambitious", Alignment = "Evil", StartLevel = 9,
                    Gender = GenderIdentity.Male, Orientation = SexualOrientation.Straight,
                    IntimateStyle = RomanceStyle.Dominant, RelationshipPref = RelationshipPreference.OpenRelationship,
                    Passion = 0.75f, Adventurousness = 0.7f },

                new() { Name = "Lysandra the Pure", Class = CharacterClass.Cleric, Race = CharacterRace.Human,
                    Personality = "Devout", Alignment = "Good", StartLevel = 7,
                    Gender = GenderIdentity.Female, Orientation = SexualOrientation.Straight,
                    IntimateStyle = RomanceStyle.Vanilla, RelationshipPref = RelationshipPreference.Monogamous,
                    Romanticism = 0.9f, Passion = 0.5f },

                // ═══════════════════════════════════════════════════════════════════
                // Thieves & Rogues (10)
                // ═══════════════════════════════════════════════════════════════════
                new() { Name = "Sly Fingers McGee", Class = CharacterClass.Assassin, Race = CharacterRace.Hobbit,
                    Personality = "Greedy", Alignment = "Neutral", StartLevel = 6,
                    Gender = GenderIdentity.Male, Orientation = SexualOrientation.Straight,
                    IntimateStyle = RomanceStyle.Switch, RelationshipPref = RelationshipPreference.FriendsWithBenefits,
                    Passion = 0.6f, Adventurousness = 0.7f },

                new() { Name = "Shadow", Class = CharacterClass.Assassin, Race = CharacterRace.Human,
                    Personality = "Silent", Alignment = "Evil", StartLevel = 8,
                    Gender = GenderIdentity.Male, Orientation = SexualOrientation.Straight,
                    IntimateStyle = RomanceStyle.Switch, RelationshipPref = RelationshipPreference.FriendsWithBenefits,
                    Passion = 0.7f, Sensuality = 0.8f },

                new() { Name = "Nimble Nick", Class = CharacterClass.Assassin, Race = CharacterRace.Hobbit,
                    Personality = "Cowardly", Alignment = "Neutral", StartLevel = 5,
                    Gender = GenderIdentity.Male, Orientation = SexualOrientation.Straight,
                    IntimateStyle = RomanceStyle.Submissive, RelationshipPref = RelationshipPreference.Monogamous,
                    Romanticism = 0.6f, Passion = 0.5f },

                new() { Name = "Viper", Class = CharacterClass.Assassin, Race = CharacterRace.Elf,
                    Personality = "Deadly", Alignment = "Evil", StartLevel = 9,
                    Gender = GenderIdentity.Female, Orientation = SexualOrientation.Straight,
                    IntimateStyle = RomanceStyle.Dominant, RelationshipPref = RelationshipPreference.CasualOnly,
                    Passion = 0.85f, Adventurousness = 0.8f, Sensuality = 0.9f },

                new() { Name = "Quicksilver Quinn", Class = CharacterClass.Assassin, Race = CharacterRace.Human,
                    Personality = "Charming", Alignment = "Neutral", StartLevel = 6,
                    Gender = GenderIdentity.Male, Orientation = SexualOrientation.Straight,
                    IntimateStyle = RomanceStyle.Switch, RelationshipPref = RelationshipPreference.Polyamorous,
                    Romanticism = 0.7f, Sensuality = 0.85f, Passion = 0.8f },

                new() { Name = "Dagger Dee", Class = CharacterClass.Assassin, Race = CharacterRace.Hobbit,
                    Personality = "Sneaky", Alignment = "Neutral", StartLevel = 5,
                    Gender = GenderIdentity.Female, Orientation = SexualOrientation.Lesbian,
                    IntimateStyle = RomanceStyle.Switch, RelationshipPref = RelationshipPreference.OpenRelationship,
                    Passion = 0.7f, Adventurousness = 0.75f },

                new() { Name = "Raven Nightblade", Class = CharacterClass.Assassin, Race = CharacterRace.Elf,
                    Personality = "Professional", Alignment = "Neutral", StartLevel = 8,
                    Gender = GenderIdentity.Female, Orientation = SexualOrientation.Straight,
                    IntimateStyle = RomanceStyle.Dominant, RelationshipPref = RelationshipPreference.FriendsWithBenefits,
                    Passion = 0.75f, Sensuality = 0.8f },

                new() { Name = "Pickpocket Pete", Class = CharacterClass.Assassin, Race = CharacterRace.Human,
                    Personality = "Nervous", Alignment = "Neutral", StartLevel = 4,
                    Gender = GenderIdentity.Male, Orientation = SexualOrientation.Straight,
                    IntimateStyle = RomanceStyle.Submissive, RelationshipPref = RelationshipPreference.Monogamous,
                    Romanticism = 0.8f, Passion = 0.5f },

                new() { Name = "Whisper", Class = CharacterClass.Assassin, Race = CharacterRace.Human,
                    Personality = "Cold", Alignment = "Evil", StartLevel = 10,
                    Gender = GenderIdentity.Female, Orientation = SexualOrientation.Straight,
                    IntimateStyle = RomanceStyle.Dominant, RelationshipPref = RelationshipPreference.OpenRelationship,
                    Passion = 0.7f, Adventurousness = 0.85f, Sensuality = 0.8f },

                new() { Name = "Slick Rick", Class = CharacterClass.Assassin, Race = CharacterRace.Hobbit,
                    Personality = "Lucky", Alignment = "Neutral", StartLevel = 6,
                    Gender = GenderIdentity.Male, Orientation = SexualOrientation.Straight,
                    IntimateStyle = RomanceStyle.Switch, RelationshipPref = RelationshipPreference.CasualOnly,
                    Passion = 0.7f, Sensuality = 0.6f },

                // ═══════════════════════════════════════════════════════════════════
                // Paladins & Clerics (8)
                // ═══════════════════════════════════════════════════════════════════
                new() { Name = "Brother Benedict", Class = CharacterClass.Cleric, Race = CharacterRace.Human,
                    Personality = "Pious", Alignment = "Good", StartLevel = 7,
                    Gender = GenderIdentity.Male, Orientation = SexualOrientation.Straight,
                    IntimateStyle = RomanceStyle.Vanilla, RelationshipPref = RelationshipPreference.Monogamous,
                    Romanticism = 0.85f, Passion = 0.5f },

                new() { Name = "Valeria the Righteous", Class = CharacterClass.Paladin, Race = CharacterRace.Human,
                    Personality = "Zealous", Alignment = "Good", StartLevel = 8,
                    Gender = GenderIdentity.Female, Orientation = SexualOrientation.Straight,
                    IntimateStyle = RomanceStyle.Switch, RelationshipPref = RelationshipPreference.Monogamous,
                    Romanticism = 0.8f, Passion = 0.7f },

                new() { Name = "Father Grimwald", Class = CharacterClass.Cleric, Race = CharacterRace.Dwarf,
                    Personality = "Stern", Alignment = "Good", StartLevel = 7,
                    Gender = GenderIdentity.Male, Orientation = SexualOrientation.Straight,
                    IntimateStyle = RomanceStyle.Dominant, RelationshipPref = RelationshipPreference.Monogamous,
                    Romanticism = 0.4f, Passion = 0.5f },

                new() { Name = "Sister Mercy", Class = CharacterClass.Cleric, Race = CharacterRace.Elf,
                    Personality = "Kind", Alignment = "Good", StartLevel = 6,
                    Gender = GenderIdentity.Female, Orientation = SexualOrientation.Straight,
                    IntimateStyle = RomanceStyle.Submissive, RelationshipPref = RelationshipPreference.Monogamous,
                    Romanticism = 0.9f, Sensuality = 0.7f, Passion = 0.65f },

                new() { Name = "Sir Cedric the Pure", Class = CharacterClass.Paladin, Race = CharacterRace.Human,
                    Personality = "Righteous", Alignment = "Good", StartLevel = 9,
                    Gender = GenderIdentity.Male, Orientation = SexualOrientation.Asexual,
                    IntimateStyle = RomanceStyle.Vanilla, RelationshipPref = RelationshipPreference.Monogamous,
                    Romanticism = 0.95f, Passion = 0.2f },

                new() { Name = "Evangeline Lighttouch", Class = CharacterClass.Cleric, Race = CharacterRace.Human,
                    Personality = "Gentle", Alignment = "Good", StartLevel = 6,
                    Gender = GenderIdentity.Female, Orientation = SexualOrientation.Straight,
                    IntimateStyle = RomanceStyle.Submissive, RelationshipPref = RelationshipPreference.Monogamous,
                    Romanticism = 0.9f, Sensuality = 0.75f, Passion = 0.7f },

                new() { Name = "Thorgrim Holyhammer", Class = CharacterClass.Paladin, Race = CharacterRace.Dwarf,
                    Personality = "Resolute", Alignment = "Good", StartLevel = 8,
                    Gender = GenderIdentity.Male, Orientation = SexualOrientation.Straight,
                    IntimateStyle = RomanceStyle.Dominant, RelationshipPref = RelationshipPreference.Monogamous,
                    Romanticism = 0.6f, Passion = 0.7f },

                new() { Name = "Mother Superior", Class = CharacterClass.Cleric, Race = CharacterRace.Human,
                    Personality = "Strict", Alignment = "Good", StartLevel = 10,
                    Gender = GenderIdentity.Female, Orientation = SexualOrientation.Straight,
                    IntimateStyle = RomanceStyle.Dominant, RelationshipPref = RelationshipPreference.Monogamous,
                    Romanticism = 0.5f, Passion = 0.4f },

                // ═══════════════════════════════════════════════════════════════════
                // Monks & Rangers (6)
                // ═══════════════════════════════════════════════════════════════════
                new() { Name = "Master Wu", Class = CharacterClass.Sage, Race = CharacterRace.Human,
                    Personality = "Serene", Alignment = "Neutral", StartLevel = 12,
                    Gender = GenderIdentity.Male, Orientation = SexualOrientation.Straight,
                    IntimateStyle = RomanceStyle.Switch, RelationshipPref = RelationshipPreference.Polyamorous,
                    Romanticism = 0.7f, Passion = 0.5f, Adventurousness = 0.6f },

                new() { Name = "Wildwood", Class = CharacterClass.Ranger, Race = CharacterRace.Elf,
                    Personality = "Solitary", Alignment = "Neutral", StartLevel = 7,
                    Gender = GenderIdentity.Male, Orientation = SexualOrientation.Straight,
                    IntimateStyle = RomanceStyle.Switch, RelationshipPref = RelationshipPreference.CasualOnly,
                    Passion = 0.6f, Sensuality = 0.7f },

                new() { Name = "Kai the Swift", Class = CharacterClass.Sage, Race = CharacterRace.Human,
                    Personality = "Disciplined", Alignment = "Good", StartLevel = 10,
                    Gender = GenderIdentity.Male, Orientation = SexualOrientation.Straight,
                    IntimateStyle = RomanceStyle.Switch, RelationshipPref = RelationshipPreference.Monogamous,
                    Romanticism = 0.75f, Passion = 0.65f },

                new() { Name = "Hawkeye", Class = CharacterClass.Ranger, Race = CharacterRace.Hobbit,
                    Personality = "Sharp", Alignment = "Neutral", StartLevel = 6,
                    Gender = GenderIdentity.Female, Orientation = SexualOrientation.Straight,
                    IntimateStyle = RomanceStyle.Switch, RelationshipPref = RelationshipPreference.OpenRelationship,
                    Passion = 0.7f, Adventurousness = 0.6f },

                new() { Name = "Zen Master Lotus", Class = CharacterClass.Sage, Race = CharacterRace.Elf,
                    Personality = "Peaceful", Alignment = "Good", StartLevel = 14,
                    Gender = GenderIdentity.Male, Orientation = SexualOrientation.Straight,
                    IntimateStyle = RomanceStyle.Switch, RelationshipPref = RelationshipPreference.Polyamorous,
                    Romanticism = 0.8f, Sensuality = 0.85f, Passion = 0.6f },

                new() { Name = "Thornwood", Class = CharacterClass.Ranger, Race = CharacterRace.Human,
                    Personality = "Gruff", Alignment = "Neutral", StartLevel = 7,
                    Gender = GenderIdentity.Male, Orientation = SexualOrientation.Straight,
                    IntimateStyle = RomanceStyle.Dominant, RelationshipPref = RelationshipPreference.CasualOnly,
                    Passion = 0.7f, Sensuality = 0.5f },

                // ═══════════════════════════════════════════════════════════════════
                // Mercenaries & Misc (6)
                // ═══════════════════════════════════════════════════════════════════
                new() { Name = "Scarface Sam", Class = CharacterClass.Warrior, Race = CharacterRace.Human,
                    Personality = "Brutal", Alignment = "Evil", StartLevel = 6,
                    Gender = GenderIdentity.Male, Orientation = SexualOrientation.Straight,
                    IntimateStyle = RomanceStyle.Dominant, RelationshipPref = RelationshipPreference.CasualOnly,
                    Passion = 0.85f, Adventurousness = 0.75f },

                new() { Name = "Gold Tooth Gary", Class = CharacterClass.Assassin, Race = CharacterRace.Human,
                    Personality = "Flashy", Alignment = "Neutral", StartLevel = 5,
                    Gender = GenderIdentity.Male, Orientation = SexualOrientation.Straight,
                    IntimateStyle = RomanceStyle.Switch, RelationshipPref = RelationshipPreference.Polyamorous,
                    Sensuality = 0.8f, Passion = 0.75f, Adventurousness = 0.7f },

                new() { Name = "Mad Dog Morgan", Class = CharacterClass.Barbarian, Race = CharacterRace.Human,
                    Personality = "Insane", Alignment = "Evil", StartLevel = 7,
                    Gender = GenderIdentity.Male, Orientation = SexualOrientation.Straight,
                    IntimateStyle = RomanceStyle.Adventurous, RelationshipPref = RelationshipPreference.OpenRelationship,
                    Passion = 0.95f, Adventurousness = 0.95f, Sensuality = 0.8f },

                new() { Name = "The Executioner", Class = CharacterClass.Warrior, Race = CharacterRace.Orc,
                    Personality = "Merciless", Alignment = "Evil", StartLevel = 9,
                    Gender = GenderIdentity.Male, Orientation = SexualOrientation.Straight,
                    IntimateStyle = RomanceStyle.Dominant, RelationshipPref = RelationshipPreference.CasualOnly,
                    Passion = 0.7f, Adventurousness = 0.6f },

                new() { Name = "Lucky Lou", Class = CharacterClass.Assassin, Race = CharacterRace.Hobbit,
                    Personality = "Optimistic", Alignment = "Neutral", StartLevel = 4,
                    Gender = GenderIdentity.Female, Orientation = SexualOrientation.Straight,
                    IntimateStyle = RomanceStyle.Switch, RelationshipPref = RelationshipPreference.OpenRelationship,
                    Romanticism = 0.7f, Passion = 0.65f, Sensuality = 0.7f },

                new() { Name = "Ironjaw", Class = CharacterClass.Warrior, Race = CharacterRace.Dwarf,
                    Personality = "Tough", Alignment = "Neutral", StartLevel = 8,
                    Gender = GenderIdentity.Male, Orientation = SexualOrientation.Straight,
                    IntimateStyle = RomanceStyle.Dominant, RelationshipPref = RelationshipPreference.FriendsWithBenefits,
                    Passion = 0.75f, Sensuality = 0.6f },

                // ═══════════════════════════════════════════════════════════════════
                // Story-Connected NPCs (10) - These NPCs have lore significance
                // ═══════════════════════════════════════════════════════════════════

                // The Stranger (Noctura's mortal disguise - key story character)
                new() { Name = "The Stranger", Class = CharacterClass.Sage, Race = CharacterRace.Human,
                    Personality = "Enigmatic", Alignment = "Neutral", StartLevel = 50,
                    Gender = GenderIdentity.Female, Orientation = SexualOrientation.Straight,
                    IntimateStyle = RomanceStyle.Switch, RelationshipPref = RelationshipPreference.OpenRelationship,
                    Romanticism = 0.5f, Passion = 0.7f, Adventurousness = 0.9f,
                    StoryRole = "TheStranger", LoreNote = "Noctura in disguise. Guides the player while hiding her divine nature." },

                // Lysandra - Light path romance option (mentioned in StoryProgressionSystem)
                new() { Name = "Lysandra Dawnwhisper", Class = CharacterClass.Paladin, Race = CharacterRace.Elf,
                    Personality = "Radiant", Alignment = "Good", StartLevel = 15,
                    Gender = GenderIdentity.Female, Orientation = SexualOrientation.Straight,
                    IntimateStyle = RomanceStyle.Switch, RelationshipPref = RelationshipPreference.Monogamous,
                    Romanticism = 0.95f, Passion = 0.7f, Sensuality = 0.6f,
                    StoryRole = "Lysandra", LoreNote = "Servant of Solarius seeking to restore Aurelion. Light path romance." },

                // Mordecai - Dark path romance option (mentioned in StoryProgressionSystem)
                new() { Name = "Mordecai Voidborne", Class = CharacterClass.Magician, Race = CharacterRace.Human,
                    Personality = "Brooding", Alignment = "Evil", StartLevel = 18,
                    Gender = GenderIdentity.Male, Orientation = SexualOrientation.Straight,
                    IntimateStyle = RomanceStyle.Dominant, RelationshipPref = RelationshipPreference.OpenRelationship,
                    Romanticism = 0.6f, Passion = 0.85f, Adventurousness = 0.8f, Sensuality = 0.75f,
                    StoryRole = "Mordecai", LoreNote = "Seeks to consume the Old Gods' power. Dark path romance." },

                // Sylvana - Neutral path romance (mentioned in StoryProgressionSystem)
                new() { Name = "Sylvana Riverwind", Class = CharacterClass.Ranger, Race = CharacterRace.Elf,
                    Personality = "Free-spirited", Alignment = "Neutral", StartLevel = 12,
                    Gender = GenderIdentity.Female, Orientation = SexualOrientation.Straight,
                    IntimateStyle = RomanceStyle.Adventurous, RelationshipPref = RelationshipPreference.Polyamorous,
                    Romanticism = 0.75f, Passion = 0.8f, Sensuality = 0.85f, Adventurousness = 0.9f,
                    StoryRole = "Sylvana", LoreNote = "Child of the forest goddess. Neutral path romance." },

                // A priest who knows about the Old Gods
                new() { Name = "Archpriest Aldwyn", Class = CharacterClass.Cleric, Race = CharacterRace.Human,
                    Personality = "Scholarly", Alignment = "Good", StartLevel = 25,
                    Gender = GenderIdentity.Male, Orientation = SexualOrientation.Straight,
                    IntimateStyle = RomanceStyle.Vanilla, RelationshipPref = RelationshipPreference.Monogamous,
                    Romanticism = 0.8f, Passion = 0.4f,
                    StoryRole = "HighPriest", LoreNote = "Knows the truth about Aurelion's fading. Can guide players to save him." },

                // A warrior who serves Maelketh knowingly
                new() { Name = "Skarn the Bloodsworn", Class = CharacterClass.Barbarian, Race = CharacterRace.Orc,
                    Personality = "Fanatical", Alignment = "Evil", StartLevel = 30,
                    Gender = GenderIdentity.Male, Orientation = SexualOrientation.Straight,
                    IntimateStyle = RomanceStyle.Dominant, RelationshipPref = RelationshipPreference.CasualOnly,
                    Passion = 0.95f, Adventurousness = 0.7f,
                    StoryRole = "MaelkethChampion", LoreNote = "Maelketh's mortal champion. Believes glory in battle leads to godhood." },

                // A thief connected to Noctura's network
                new() { Name = "Whisperwind", Class = CharacterClass.Assassin, Race = CharacterRace.Human,
                    Personality = "Secretive", Alignment = "Neutral", StartLevel = 20,
                    Gender = GenderIdentity.Male, Orientation = SexualOrientation.Straight,
                    IntimateStyle = RomanceStyle.Switch, RelationshipPref = RelationshipPreference.FriendsWithBenefits,
                    Passion = 0.65f, Sensuality = 0.8f, Adventurousness = 0.85f,
                    StoryRole = "ShadowAgent", LoreNote = "One of Noctura's mortal informants. Knows fragments of the truth." },

                // A scholar studying the Seven Seals
                new() { Name = "Sera the Seeker", Class = CharacterClass.Sage, Race = CharacterRace.Human,
                    Personality = "Obsessed", Alignment = "Neutral", StartLevel = 16,
                    Gender = GenderIdentity.Female, Orientation = SexualOrientation.Straight,
                    IntimateStyle = RomanceStyle.Submissive, RelationshipPref = RelationshipPreference.Monogamous,
                    Romanticism = 0.9f, Passion = 0.5f,
                    StoryRole = "SealScholar", LoreNote = "Has devoted her life to finding the Seven Seals. Knows their locations." },

                // A fallen paladin who once served Aurelion
                new() { Name = "Sir Darius the Lost", Class = CharacterClass.Paladin, Race = CharacterRace.Human,
                    Personality = "Tormented", Alignment = "Neutral", StartLevel = 35,
                    Gender = GenderIdentity.Male, Orientation = SexualOrientation.Straight,
                    IntimateStyle = RomanceStyle.Vanilla, RelationshipPref = RelationshipPreference.Undecided,
                    Romanticism = 0.6f, Passion = 0.5f,
                    StoryRole = "FallenPaladin", LoreNote = "Once Aurelion's champion. Lost faith when he saw the god fading." },

                // A mysterious oracle who speaks Ocean Philosophy truths
                new() { Name = "The Wavespeaker", Class = CharacterClass.Cleric, Race = CharacterRace.Elf,
                    Personality = "Serene", Alignment = "Good", StartLevel = 40,
                    Gender = GenderIdentity.Male, Orientation = SexualOrientation.Straight,
                    IntimateStyle = RomanceStyle.Switch, RelationshipPref = RelationshipPreference.Polyamorous,
                    Romanticism = 0.8f, Passion = 0.6f, Sensuality = 0.7f,
                    StoryRole = "OceanOracle", LoreNote = "Knows the Ocean Philosophy truth. Speaks in wave metaphors." }
            };
        }
    }

    public class NPCTemplate
    {
        public string Name { get; set; } = "";
        public CharacterClass Class { get; set; }
        public CharacterRace Race { get; set; }
        public string Personality { get; set; } = "";
        public string Alignment { get; set; } = "";
        public int StartLevel { get; set; } = 5;

        // Romance/Intimacy properties
        public GenderIdentity Gender { get; set; } = GenderIdentity.Male;
        public SexualOrientation Orientation { get; set; } = SexualOrientation.Straight;
        public RomanceStyle IntimateStyle { get; set; } = RomanceStyle.Switch;
        public RelationshipPreference RelationshipPref { get; set; } = RelationshipPreference.Undecided;

        // Romance personality modifiers (optional, will be randomly generated if not set)
        public float? Romanticism { get; set; }
        public float? Sensuality { get; set; }
        public float? Passion { get; set; }
        public float? Adventurousness { get; set; }

        // Story/Lore connection properties
        public string? StoryRole { get; set; }  // e.g., "TheStranger", "Lysandra", "Mordecai"
        public string? LoreNote { get; set; }   // Description of their role in the divine story
    }
}
