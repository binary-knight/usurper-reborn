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
            IsEcho = isEcho,
            // Restore base stats for RecalculateStats
            BaseStrength = playerData.BaseStrength > 0 ? playerData.BaseStrength : playerData.Strength,
            BaseDexterity = playerData.BaseDexterity > 0 ? playerData.BaseDexterity : playerData.Dexterity,
            BaseConstitution = playerData.BaseConstitution > 0 ? playerData.BaseConstitution : playerData.Constitution,
            BaseIntelligence = playerData.BaseIntelligence > 0 ? playerData.BaseIntelligence : playerData.Intelligence,
            BaseWisdom = playerData.BaseWisdom > 0 ? playerData.BaseWisdom : playerData.Wisdom,
            BaseCharisma = playerData.BaseCharisma > 0 ? playerData.BaseCharisma : playerData.Charisma,
            BaseMaxHP = playerData.BaseMaxHP > 0 ? playerData.BaseMaxHP : playerData.MaxHP,
            BaseMaxMana = playerData.BaseMaxMana > 0 ? playerData.BaseMaxMana : playerData.MaxMana,
            BaseDefence = playerData.BaseDefence > 0 ? playerData.BaseDefence : playerData.Defence,
            BaseStamina = playerData.BaseStamina > 0 ? playerData.BaseStamina : playerData.Stamina,
            BaseAgility = playerData.BaseAgility > 0 ? playerData.BaseAgility : playerData.Agility
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

        // Restore equipment so weapon requirements work (Magician needs Staff for spells, etc.)
        RestoreEquipment(character, playerData);

        return character;
    }

    /// <summary>
    /// Restore dynamic equipment and equipped items from save data.
    /// Always uses fresh IDs (RegisterDynamic) since loaded characters are temporary.
    /// </summary>
    private static void RestoreEquipment(Character character, PlayerData playerData)
    {
        var equipIdRemap = new Dictionary<int, int>();

        // Register dynamic equipment into the database with fresh IDs
        if (playerData.DynamicEquipment != null && playerData.DynamicEquipment.Count > 0)
        {
            foreach (var equipData in playerData.DynamicEquipment)
            {
                var equipment = new Equipment
                {
                    Name = equipData.Name,
                    Description = equipData.Description ?? "",
                    Slot = (EquipmentSlot)equipData.Slot,
                    WeaponPower = equipData.WeaponPower,
                    ArmorClass = equipData.ArmorClass,
                    ShieldBonus = equipData.ShieldBonus,
                    BlockChance = equipData.BlockChance,
                    StrengthBonus = equipData.StrengthBonus,
                    DexterityBonus = equipData.DexterityBonus,
                    ConstitutionBonus = equipData.ConstitutionBonus,
                    IntelligenceBonus = equipData.IntelligenceBonus,
                    WisdomBonus = equipData.WisdomBonus,
                    CharismaBonus = equipData.CharismaBonus,
                    MaxHPBonus = equipData.MaxHPBonus,
                    MaxManaBonus = equipData.MaxManaBonus,
                    DefenceBonus = equipData.DefenceBonus,
                    MinLevel = equipData.MinLevel,
                    Value = equipData.Value,
                    IsCursed = equipData.IsCursed,
                    Rarity = (EquipmentRarity)equipData.Rarity,
                    WeaponType = (WeaponType)equipData.WeaponType,
                    Handedness = (WeaponHandedness)equipData.Handedness,
                    ArmorType = (ArmorType)equipData.ArmorType,
                    StaminaBonus = equipData.StaminaBonus,
                    AgilityBonus = equipData.AgilityBonus,
                    CriticalChanceBonus = equipData.CriticalChanceBonus,
                    CriticalDamageBonus = equipData.CriticalDamageBonus,
                    MagicResistance = equipData.MagicResistance,
                    PoisonDamage = equipData.PoisonDamage,
                    LifeSteal = equipData.LifeSteal,
                    HasFireEnchant = equipData.HasFireEnchant,
                    HasFrostEnchant = equipData.HasFrostEnchant,
                    HasLightningEnchant = equipData.HasLightningEnchant,
                    HasPoisonEnchant = equipData.HasPoisonEnchant,
                    HasHolyEnchant = equipData.HasHolyEnchant,
                    HasShadowEnchant = equipData.HasShadowEnchant,
                    ManaSteal = equipData.ManaSteal,
                    ArmorPiercing = equipData.ArmorPiercing,
                    Thorns = equipData.Thorns,
                    HPRegen = equipData.HPRegen,
                    ManaRegen = equipData.ManaRegen,
                    WeightClass = (ArmorWeightClass)equipData.WeightClass,
                    StrengthRequired = equipData.StrengthRequired,
                    RequiresGood = equipData.RequiresGood,
                    RequiresEvil = equipData.RequiresEvil,
                    ClassRestrictions = equipData.ClassRestrictions?.Select(c => (CharacterClass)c).ToList() ?? new List<CharacterClass>(),
                    IsUnique = equipData.IsUnique,
                    HasBossSlayer = equipData.HasBossSlayer,
                    HasTitanResolve = equipData.HasTitanResolve
                };

                // Migration: fix legacy items with WeaponType=None
                if (equipment.Slot == EquipmentSlot.MainHand && equipment.WeaponType == WeaponType.None)
                {
                    equipment.WeaponType = ShopItemGenerator.InferWeaponType(equipment.Name);
                    equipment.Handedness = ShopItemGenerator.InferHandedness(equipment.WeaponType);
                }

                // Always use fresh IDs — loaded characters are temporary combat entities
                int newId = EquipmentDatabase.RegisterDynamic(equipment);
                if (newId != equipData.Id)
                {
                    equipIdRemap[equipData.Id] = newId;
                }
            }
        }

        // Restore equipped items dictionary with remapped IDs
        if (playerData.EquippedItems != null && playerData.EquippedItems.Count > 0)
        {
            character.EquippedItems = playerData.EquippedItems.ToDictionary(
                kvp => (EquipmentSlot)kvp.Key,
                kvp => equipIdRemap.TryGetValue(kvp.Value, out int newId) ? newId : kvp.Value
            );

            // Remove any orphaned references (equipment not found in database)
            var slotsToRemove = new List<EquipmentSlot>();
            foreach (var kvp in character.EquippedItems)
            {
                if (EquipmentDatabase.GetById(kvp.Value) == null)
                    slotsToRemove.Add(kvp.Key);
            }
            foreach (var slot in slotsToRemove)
                character.EquippedItems.Remove(slot);
        }

        // RecalculateStats applies equipment bonuses (weapon power, armor, stat bonuses)
        var savedHP = character.HP;
        var savedMana = character.Mana;
        character.RecalculateStats();
        character.HP = Math.Min(savedHP, character.MaxHP);
        character.Mana = Math.Min(savedMana, character.MaxMana);
    }
}
