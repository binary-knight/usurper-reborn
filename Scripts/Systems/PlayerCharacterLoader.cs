using UsurperRemake.Systems;

/// <summary>
/// Shared utility for loading player characters from saved data.
/// Used by: ArenaLocation (PvP), DungeonLocation (coop), WorldSimulator (team wars).
/// </summary>
public static class PlayerCharacterLoader
{
    /// <summary>
    /// Create a combat-ready Character from saved PlayerData.
    /// The character starts at full HP and is AI-controlled.
    /// </summary>
    public static Character CreateFromSaveData(PlayerData playerData, string displayName, bool isEcho = false)
    {
        var character = new Character
        {
            Name1 = playerData.Name1,
            Name2 = playerData.Name2 ?? displayName,
            Level = playerData.Level,
            HP = playerData.MaxHP,
            MaxHP = playerData.MaxHP,
            Mana = playerData.MaxMana,
            MaxMana = playerData.MaxMana,
            Strength = playerData.Strength,
            Defence = playerData.Defence,
            Stamina = playerData.Stamina,
            Agility = playerData.Agility,
            Charisma = playerData.Charisma,
            Dexterity = playerData.Dexterity,
            Wisdom = playerData.Wisdom,
            Intelligence = playerData.Intelligence,
            Constitution = playerData.Constitution,
            WeapPow = playerData.WeapPow,
            ArmPow = playerData.ArmPow,
            Healing = playerData.Healing,
            ManaPotions = playerData.ManaPotions,
            Race = playerData.Race,
            Class = playerData.Class,
            Sex = playerData.Sex == 'F' ? CharacterSex.Female : CharacterSex.Male,
            Gold = playerData.Gold,
            Poison = playerData.Poison,
            AI = CharacterAI.Computer,
            IsEcho = isEcho
        };

        // Restore spells so AI can cast them
        if (playerData.Spells != null && playerData.Spells.Count > 0)
        {
            character.Spell = new List<List<bool>>(playerData.Spells);
        }

        // Restore abilities so AI can use them
        if (playerData.LearnedAbilities != null && playerData.LearnedAbilities.Count > 0)
        {
            character.LearnedAbilities = new HashSet<string>(playerData.LearnedAbilities);
        }

        return character;
    }
}
