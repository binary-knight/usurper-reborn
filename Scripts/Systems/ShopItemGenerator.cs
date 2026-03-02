using System;
using System.Collections.Generic;
using System.Linq;

/// <summary>
/// Generates shop Equipment items from LootGenerator templates.
/// Shop items are slightly weaker than Common dungeon loot (85% power)
/// and priced to cost ~30-45 minutes of farming at the item's level.
/// Items use IDs 50000-99999 (below DynamicEquipmentStart of 100000).
/// </summary>
public static class ShopItemGenerator
{
    // ID ranges for shop-generated equipment
    private const int OneHandedWeaponStart = 50000;
    private const int TwoHandedWeaponStart = 52000;
    private const int ShieldStart = 54000;
    private const int HeadStart = 56000;
    private const int BodyStart = 58000;
    private const int ArmsStart = 60000;
    private const int HandsStart = 62000;
    private const int LegsStart = 64000;
    private const int FeetStart = 66000;
    private const int WaistStart = 68000;
    private const int FaceStart = 70000;
    private const int CloakStart = 72000;
    private const int NeckStart = 74000;
    private const int RingStart = 76000;
    private const int BowStart = 78000;

    public const int ShopGeneratedMin = 50000;
    public const int ShopGeneratedMax = 99999;

    private const int LevelInterval = 10;
    private const int MaxItemsPerTemplate = 4;

    public static bool IsShopGenerated(int id) => id >= ShopGeneratedMin && id <= ShopGeneratedMax;

    /// <summary>
    /// Generate all shop items from loot templates
    /// </summary>
    public static List<Equipment> GenerateAllShopItems()
    {
        var items = new List<Equipment>();

        var weaponTemplates = LootGenerator.GetWeaponTemplates();
        int twoHandedStart = LootGenerator.TwoHandedWeaponStartIndex;

        // One-handed weapons
        int id = OneHandedWeaponStart;
        for (int i = 0; i < twoHandedStart && i < weaponTemplates.Count; i++)
        {
            items.AddRange(GenerateWeaponItems(weaponTemplates[i], ref id,
                WeaponHandedness.OneHanded, EquipmentSlot.MainHand));
        }

        // Two-handed melee weapons (skip bows — they get their own ID range)
        id = TwoHandedWeaponStart;
        int bowId = BowStart;
        for (int i = twoHandedStart; i < weaponTemplates.Count; i++)
        {
            if (weaponTemplates[i].Name.Contains("Bow", StringComparison.OrdinalIgnoreCase))
            {
                items.AddRange(GenerateWeaponItems(weaponTemplates[i], ref bowId,
                    WeaponHandedness.TwoHanded, EquipmentSlot.MainHand));
            }
            else
            {
                items.AddRange(GenerateWeaponItems(weaponTemplates[i], ref id,
                    WeaponHandedness.TwoHanded, EquipmentSlot.MainHand));
            }
        }

        // Shields
        id = ShieldStart;
        foreach (var t in LootGenerator.GetShieldTemplates())
            items.AddRange(GenerateShieldItems(t, ref id));

        // Body armor
        id = BodyStart;
        foreach (var t in LootGenerator.GetBodyArmorTemplates())
            items.AddRange(GenerateArmorItems(t, ref id, EquipmentSlot.Body));

        // Head armor
        id = HeadStart;
        foreach (var t in LootGenerator.GetHeadArmorTemplates())
            items.AddRange(GenerateArmorItems(t, ref id, EquipmentSlot.Head));

        // Arms armor
        id = ArmsStart;
        foreach (var t in LootGenerator.GetArmsArmorTemplates())
            items.AddRange(GenerateArmorItems(t, ref id, EquipmentSlot.Arms));

        // Hands armor
        id = HandsStart;
        foreach (var t in LootGenerator.GetHandsArmorTemplates())
            items.AddRange(GenerateArmorItems(t, ref id, EquipmentSlot.Hands));

        // Legs armor
        id = LegsStart;
        foreach (var t in LootGenerator.GetLegsArmorTemplates())
            items.AddRange(GenerateArmorItems(t, ref id, EquipmentSlot.Legs));

        // Feet armor
        id = FeetStart;
        foreach (var t in LootGenerator.GetFeetArmorTemplates())
            items.AddRange(GenerateArmorItems(t, ref id, EquipmentSlot.Feet));

        // Waist armor
        id = WaistStart;
        foreach (var t in LootGenerator.GetWaistArmorTemplates())
            items.AddRange(GenerateArmorItems(t, ref id, EquipmentSlot.Waist));

        // Face armor
        id = FaceStart;
        foreach (var t in LootGenerator.GetFaceArmorTemplates())
            items.AddRange(GenerateArmorItems(t, ref id, EquipmentSlot.Face));

        // Cloaks
        id = CloakStart;
        foreach (var t in LootGenerator.GetCloakArmorTemplates())
            items.AddRange(GenerateArmorItems(t, ref id, EquipmentSlot.Cloak));

        // Necklaces
        id = NeckStart;
        foreach (var t in LootGenerator.GetNecklaceTemplates())
            items.AddRange(GenerateAccessoryItems(t, ref id, EquipmentSlot.Neck));

        // Rings
        id = RingStart;
        foreach (var t in LootGenerator.GetRingTemplates())
            items.AddRange(GenerateAccessoryItems(t, ref id, EquipmentSlot.LFinger));

        return items;
    }

    private static List<Equipment> GenerateWeaponItems(
        (string Name, string[] Classes, int MinLevel, int MaxLevel, float BasePower) template,
        ref int nextId,
        WeaponHandedness handedness,
        EquipmentSlot slot)
    {
        var results = new List<Equipment>();
        var levels = GetShopLevels(template.MinLevel, template.MaxLevel);
        var weaponType = InferWeaponType(template.Name);

        foreach (int level in levels)
        {
            int power = CalculateShopPower(template.BasePower, level);
            long price = CalculateShopPrice(level, slot);

            var equip = new Equipment
            {
                Id = nextId++,
                Name = template.Name,
                Slot = slot,
                Handedness = handedness,
                WeaponType = weaponType,
                WeaponPower = power,
                Value = price,
                Rarity = GetRarityForLevel(level),
                MinLevel = level,
                ClassRestrictions = ParseClassRestrictions(template.Classes),
            };
            ApplyWeaponBonuses(equip, weaponType, level);
            results.Add(equip);
        }

        return results;
    }

    private static List<Equipment> GenerateShieldItems(
        (string Name, string[] Classes, int MinLevel, int MaxLevel, float BasePower) template,
        ref int nextId)
    {
        var results = new List<Equipment>();
        var levels = GetShopLevels(template.MinLevel, template.MaxLevel);
        var weaponType = InferShieldType(template.Name);

        foreach (int level in levels)
        {
            int power = CalculateShopPower(template.BasePower, level);
            long price = CalculateShopPrice(level, EquipmentSlot.OffHand);

            var equip = new Equipment
            {
                Id = nextId++,
                Name = template.Name,
                Slot = EquipmentSlot.OffHand,
                Handedness = WeaponHandedness.OffHandOnly,
                WeaponType = weaponType,
                ShieldBonus = power,
                BlockChance = Math.Min(5 + power / 3, 35),
                Value = price,
                Rarity = GetRarityForLevel(level),
                MinLevel = level,
                ClassRestrictions = ParseClassRestrictions(template.Classes),
            };
            ApplyShieldBonuses(equip, weaponType, level);
            results.Add(equip);
        }

        return results;
    }

    private static List<Equipment> GenerateArmorItems(
        (string Name, string[] Classes, int MinLevel, int MaxLevel, float BasePower) template,
        ref int nextId,
        EquipmentSlot slot)
    {
        var results = new List<Equipment>();
        var levels = GetShopLevels(template.MinLevel, template.MaxLevel);
        var armorType = InferArmorType(template.Name);

        foreach (int level in levels)
        {
            int power = CalculateShopPower(template.BasePower, level);
            long price = CalculateShopPrice(level, slot);

            var equip = new Equipment
            {
                Id = nextId++,
                Name = template.Name,
                Slot = slot,
                ArmorType = armorType,
                ArmorClass = power,
                Value = price,
                Rarity = GetRarityForLevel(level),
                MinLevel = level,
                ClassRestrictions = ParseClassRestrictions(template.Classes),
            };
            ApplyArmorBonuses(equip, armorType, level);
            results.Add(equip);
        }

        return results;
    }

    private static List<Equipment> GenerateAccessoryItems(
        (string Name, int MinLevel, int MaxLevel, float BasePower) template,
        ref int nextId,
        EquipmentSlot slot)
    {
        var results = new List<Equipment>();
        var levels = GetShopLevels(template.MinLevel, template.MaxLevel);

        foreach (int level in levels)
        {
            int power = CalculateShopPower(template.BasePower, level);
            long price = CalculateShopPrice(level, slot);

            var equip = new Equipment
            {
                Id = nextId++,
                Name = template.Name,
                Slot = slot,
                ArmorClass = power,
                Value = price,
                Rarity = GetRarityForLevel(level),
                MinLevel = level,
            };
            ApplyAccessoryBonuses(equip, template.Name, level);
            results.Add(equip);
        }

        return results;
    }

    #region Bonus Application

    /// <summary>
    /// Bonus scaling factor: 0 at level 1, ramps up to ~1.0 at level 100
    /// </summary>
    private static float BonusScale(int level) => Math.Max(0, (level - 5) / 95.0f);

    /// <summary>
    /// Apply stat bonuses to weapons based on weapon type and level
    /// </summary>
    private static void ApplyWeaponBonuses(Equipment equip, WeaponType type, int level)
    {
        float s = BonusScale(level);
        if (level < 10) return; // Very early items get no bonuses

        switch (type)
        {
            case WeaponType.Dagger:
                equip.CriticalChanceBonus = 2 + (int)(s * 18);       // 2-20%
                equip.DexterityBonus = (int)(s * 8);                  // 0-8
                if (level >= 40) equip.CriticalDamageBonus = (int)(s * 50); // 0-50%
                break;

            case WeaponType.Sword:
            case WeaponType.Greatsword:
                equip.StrengthBonus = 1 + (int)(s * 10);             // 1-11
                if (level >= 30) equip.CriticalDamageBonus = (int)(s * 30); // 0-30%
                if (level >= 60) equip.MaxHPBonus = (int)(s * 40);   // 0-40
                break;

            case WeaponType.Axe:
            case WeaponType.Greataxe:
                equip.StrengthBonus = 1 + (int)(s * 8);              // 1-9
                equip.CriticalDamageBonus = 5 + (int)(s * 40);       // 5-45%
                break;

            case WeaponType.Mace:
            case WeaponType.Hammer:
            case WeaponType.Maul:
                equip.StrengthBonus = 1 + (int)(s * 9);              // 1-10
                if (level >= 25) equip.ArmorPiercing = 3 + (int)(s * 12); // 3-15%
                break;

            case WeaponType.Fist:
                equip.DexterityBonus = 1 + (int)(s * 8);             // 1-9
                equip.AgilityBonus = 1 + (int)(s * 6);               // 1-7
                if (level >= 30) equip.CriticalChanceBonus = 3 + (int)(s * 15); // 3-18%
                break;

            case WeaponType.Staff:
                equip.IntelligenceBonus = 1 + (int)(s * 10);         // 1-11
                equip.WisdomBonus = (int)(s * 6);                    // 0-6
                if (level >= 20) equip.MaxManaBonus = 5 + (int)(s * 35); // 5-40
                break;

            case WeaponType.Rapier:
            case WeaponType.Scimitar:
                equip.DexterityBonus = 1 + (int)(s * 8);             // 1-9
                equip.AgilityBonus = (int)(s * 6);                   // 0-6
                equip.CriticalChanceBonus = 2 + (int)(s * 10);       // 2-12%
                break;

            case WeaponType.Flail:
                equip.StrengthBonus = 1 + (int)(s * 7);              // 1-8
                equip.DexterityBonus = (int)(s * 4);                 // 0-4
                break;

            case WeaponType.Polearm:
                equip.StrengthBonus = 1 + (int)(s * 8);              // 1-9
                equip.DefenceBonus = (int)(s * 5);                   // 0-5
                if (level >= 40) equip.MaxHPBonus = (int)(s * 25);   // 0-25
                break;

            case WeaponType.Bow:
                equip.DexterityBonus = 1 + (int)(s * 9);             // 1-10
                equip.AgilityBonus = (int)(s * 5);                   // 0-5
                equip.CriticalChanceBonus = 2 + (int)(s * 8);        // 2-10%
                break;

            case WeaponType.Instrument:
                equip.CharismaBonus = 2 + (int)(s * 12);             // 2-14
                equip.WisdomBonus = 1 + (int)(s * 8);                // 1-9
                if (level >= 30) equip.MaxManaBonus = (int)(s * 30);  // 0-30
                if (level >= 60) equip.MaxHPBonus = (int)(s * 25);    // 0-25
                break;

            default:
                // Monk weapons, bows, etc.
                equip.DexterityBonus = (int)(s * 7);                 // 0-7
                equip.AgilityBonus = (int)(s * 5);                   // 0-5
                break;
        }
    }

    /// <summary>
    /// Apply stat bonuses to shields based on type and level
    /// </summary>
    private static void ApplyShieldBonuses(Equipment equip, WeaponType type, int level)
    {
        float s = BonusScale(level);
        if (level < 10) return;

        // All shields get some defence bonus
        equip.DefenceBonus = 1 + (int)(s * 6);                       // 1-7

        switch (type)
        {
            case WeaponType.Buckler:
                equip.DexterityBonus = (int)(s * 4);                 // 0-4
                equip.AgilityBonus = (int)(s * 3);                   // 0-3
                break;

            case WeaponType.TowerShield:
                equip.ConstitutionBonus = (int)(s * 5);              // 0-5
                equip.MaxHPBonus = (int)(s * 20);                    // 0-20
                if (level >= 40) equip.MagicResistance = (int)(s * 10); // 0-10%
                break;

            default: // Standard shield
                equip.ConstitutionBonus = (int)(s * 3);              // 0-3
                if (level >= 30) equip.MaxHPBonus = (int)(s * 10);   // 0-10
                break;
        }
    }

    /// <summary>
    /// Apply stat bonuses to armor based on armor type, slot, and level
    /// </summary>
    private static void ApplyArmorBonuses(Equipment equip, ArmorType type, int level)
    {
        float s = BonusScale(level);
        if (level < 10) return; // Very early armor gets no bonuses

        // Slot-specific bonus: body armor gets the most, minor slots get less
        float slotScale = equip.Slot switch
        {
            EquipmentSlot.Body => 1.0f,
            EquipmentSlot.Head or EquipmentSlot.Legs => 0.8f,
            EquipmentSlot.Arms or EquipmentSlot.Hands or EquipmentSlot.Feet => 0.6f,
            EquipmentSlot.Cloak => 0.7f,
            EquipmentSlot.Waist or EquipmentSlot.Face => 0.5f,
            _ => 0.5f,
        };

        switch (type)
        {
            case ArmorType.Plate:
                equip.ConstitutionBonus = Math.Max(1, (int)(s * 6 * slotScale));
                equip.MaxHPBonus = (int)(s * 35 * slotScale);
                if (level >= 30) equip.StrengthBonus = (int)(s * 4 * slotScale);
                break;

            case ArmorType.Scale:
                equip.ConstitutionBonus = Math.Max(1, (int)(s * 5 * slotScale));
                equip.MaxHPBonus = (int)(s * 25 * slotScale);
                if (level >= 30) equip.DefenceBonus = (int)(s * 5 * slotScale);
                break;

            case ArmorType.Chain:
                equip.DefenceBonus = Math.Max(1, (int)(s * 5 * slotScale));
                equip.ConstitutionBonus = (int)(s * 4 * slotScale);
                if (level >= 25) equip.MaxHPBonus = (int)(s * 15 * slotScale);
                break;

            case ArmorType.Leather:
                equip.DexterityBonus = Math.Max(1, (int)(s * 5 * slotScale));
                equip.AgilityBonus = (int)(s * 4 * slotScale);
                if (level >= 25) equip.CriticalChanceBonus = (int)(s * 5 * slotScale);
                break;

            case ArmorType.Cloth:
                equip.IntelligenceBonus = Math.Max(1, (int)(s * 5 * slotScale));
                equip.WisdomBonus = (int)(s * 4 * slotScale);
                equip.MaxManaBonus = (int)(s * 25 * slotScale);
                break;

            case ArmorType.Magic:
            case ArmorType.Artifact:
                equip.IntelligenceBonus = Math.Max(1, (int)(s * 4 * slotScale));
                equip.WisdomBonus = (int)(s * 4 * slotScale);
                equip.MaxManaBonus = (int)(s * 20 * slotScale);
                if (level >= 30) equip.MagicResistance = (int)(s * 10 * slotScale);
                if (level >= 50) equip.MaxHPBonus = (int)(s * 20 * slotScale);
                break;
        }
    }

    /// <summary>
    /// Apply bonuses to accessories (rings, necklaces) based on name and level
    /// </summary>
    private static void ApplyAccessoryBonuses(Equipment equip, string name, int level)
    {
        float s = BonusScale(level);
        if (level < 10) return;

        // Name-based bonuses for themed accessories
        if (name.Contains("Strength") || name.Contains("Might"))
        {
            equip.StrengthBonus = 1 + (int)(s * 8);
        }
        else if (name.Contains("Protection") || name.Contains("Warding") || name.Contains("Valor"))
        {
            equip.DefenceBonus = 1 + (int)(s * 6);
            equip.MaxHPBonus = (int)(s * 15);
        }
        else if (name.Contains("Wisdom") || name.Contains("Mage") || name.Contains("Archmage"))
        {
            equip.WisdomBonus = 1 + (int)(s * 6);
            equip.IntelligenceBonus = (int)(s * 4);
            equip.MaxManaBonus = (int)(s * 15);
        }
        else if (name.Contains("Power") || name.Contains("Heroes") || name.Contains("Dragon"))
        {
            equip.StrengthBonus = (int)(s * 5);
            equip.ConstitutionBonus = (int)(s * 4);
            equip.MaxHPBonus = (int)(s * 20);
        }
        else if (name.Contains("Vitality") || name.Contains("Health"))
        {
            equip.ConstitutionBonus = 1 + (int)(s * 6);
            equip.MaxHPBonus = 5 + (int)(s * 30);
        }
        else if (name.Contains("Luck") || name.Contains("Signet"))
        {
            equip.CriticalChanceBonus = 1 + (int)(s * 8);
            equip.DexterityBonus = (int)(s * 3);
        }
        else if (name.Contains("Fire"))
        {
            equip.StrengthBonus = (int)(s * 4);
            equip.CriticalDamageBonus = (int)(s * 20);
        }
        else if (name.Contains("Gods") || name.Contains("Planes"))
        {
            equip.IntelligenceBonus = (int)(s * 5);
            equip.WisdomBonus = (int)(s * 5);
            equip.MaxManaBonus = (int)(s * 25);
            equip.MagicResistance = (int)(s * 8);
        }
        else
        {
            // Generic accessories: small all-around bonuses
            equip.ConstitutionBonus = (int)(s * 2);
            equip.DefenceBonus = (int)(s * 2);
        }
    }

    #endregion

    /// <summary>
    /// Get the level points at which to generate shop items for a template
    /// </summary>
    private static List<int> GetShopLevels(int minLevel, int maxLevel)
    {
        var levels = new List<int>();
        int start = Math.Max(1, minLevel);

        // First item at the template's minimum level
        levels.Add(start);

        // Additional items every LevelInterval levels
        int next = ((start / LevelInterval) + 1) * LevelInterval;
        while (next <= maxLevel && levels.Count < MaxItemsPerTemplate)
        {
            levels.Add(next);
            next += LevelInterval;
        }

        // Ensure we have an item near the max level if there's room
        if (levels.Count < MaxItemsPerTemplate && maxLevel - levels[levels.Count - 1] >= LevelInterval / 2)
            levels.Add(maxLevel);

        return levels;
    }

    /// <summary>
    /// Calculate shop item power (85% of Common dungeon loot at same level)
    /// </summary>
    private static int CalculateShopPower(float basePower, int level)
    {
        float levelScale = 1.0f + (level / 80.0f);
        int power = (int)(basePower * levelScale * GameConfig.ShopPowerMultiplier);
        return Math.Max(1, power);
    }

    /// <summary>
    /// Calculate shop price based on level and slot (scaled to ~30-45 min farming)
    /// </summary>
    private static long CalculateShopPrice(int level, EquipmentSlot slot)
    {
        float slotMult = GetSlotPriceMultiplier(slot);
        long basePrice = (long)(Math.Pow(Math.Max(1, level), 1.5) * GameConfig.ShopPriceMultiplier);
        return Math.Max(10, (long)(basePrice * slotMult));
    }

    private static float GetSlotPriceMultiplier(EquipmentSlot slot) => slot switch
    {
        EquipmentSlot.MainHand => 1.0f,
        EquipmentSlot.Body => 1.0f,
        EquipmentSlot.OffHand => 0.8f,
        EquipmentSlot.Head => 0.7f,
        EquipmentSlot.Legs => 0.65f,
        EquipmentSlot.Neck => 0.6f,
        EquipmentSlot.LFinger or EquipmentSlot.RFinger => 0.6f,
        EquipmentSlot.Arms or EquipmentSlot.Hands or EquipmentSlot.Feet => 0.5f,
        EquipmentSlot.Cloak => 0.45f,
        EquipmentSlot.Waist or EquipmentSlot.Face => 0.4f,
        _ => 0.5f,
    };

    private static EquipmentRarity GetRarityForLevel(int level) => level switch
    {
        <= 15 => EquipmentRarity.Common,
        <= 30 => EquipmentRarity.Uncommon,
        <= 50 => EquipmentRarity.Rare,
        <= 70 => EquipmentRarity.Epic,
        <= 85 => EquipmentRarity.Legendary,
        _ => EquipmentRarity.Artifact,
    };

    private static List<CharacterClass> ParseClassRestrictions(string[] classes)
    {
        if (classes.Length == 1 && classes[0] == "All")
            return new List<CharacterClass>();

        var restrictions = new List<CharacterClass>();
        foreach (var cls in classes)
        {
            if (Enum.TryParse<CharacterClass>(cls, out var cc))
                restrictions.Add(cc);
        }
        return restrictions;
    }

    /// <summary>
    /// Infer weapon type from item name. Used by both shop generation and dungeon loot conversion.
    /// </summary>
    public static WeaponType InferWeaponType(string name)
    {
        // Daggers (must be before Sword catch-all — "Assassin's Blade" contains "Blade")
        if (name.Contains("Dagger") || name.Contains("Stiletto") || name.Contains("Kris") ||
            name.Contains("Fang") || name.StartsWith("Assassin"))
            return WeaponType.Dagger;
        // Rapiers
        if (name.Contains("Rapier"))
            return WeaponType.Rapier;
        // Greatswords (two-handed swords — must be before Sword catch-all)
        if (name.Contains("Greatsword") || name.Contains("Claymore") || name.Contains("Zweihander") ||
            name.Contains("Flamberge") || name.Contains("Bloodreaver") || name.Contains("Dragonslayer"))
            return WeaponType.Greatsword;
        // Greataxes (two-handed axes — must be before one-handed Axe check)
        if (name.Contains("Greataxe") || name.Contains("Great Axe") || name.Contains("Annihilator") ||
            name.Contains("Headsman") || name.Contains("Demon Cleaver") || name.Contains("Woodcutter"))
            return WeaponType.Greataxe;
        // Axes (one-handed)
        if (name.Contains("Axe") || name.Contains("Cleaver") || name.Contains("Hatchet"))
            return WeaponType.Axe;
        // Polearms (two-handed)
        if (name.Contains("Spear") || name.Contains("Halberd") || name.Contains("Glaive") ||
            name.Contains("Lance") || name.Contains("Bardiche") || name.Contains("Voulge") ||
            name.Contains("Polearm"))
            return WeaponType.Polearm;
        // Mauls (two-handed heavy)
        if (name.Contains("Maul") || name.Contains("Sledge") || name.Contains("Crusher") ||
            name.Contains("Earthshaker") || name.Contains("Skullbreaker"))
            return WeaponType.Maul;
        // Staves
        if (name.Contains("Staff") || name.Contains("Quarterstaff") || name.Contains("Soulstaff") ||
            name.Contains("Void Staff"))
            return WeaponType.Staff;
        // Flails
        if (name.Contains("Flail"))
            return WeaponType.Flail;
        // Hammers/Maces
        if (name.Contains("Hammer") || name.Contains("Mace") || name.Contains("Club") ||
            name.Contains("Scepter"))
            return WeaponType.Mace;
        // Bows (must be before Sword catch-all; case-insensitive for "Longbow")
        if (name.Contains("Bow", StringComparison.OrdinalIgnoreCase))
            return WeaponType.Bow;
        // Scimitar
        if (name.Contains("Scimitar"))
            return WeaponType.Scimitar;
        // Musical instruments (Bard-only)
        if (name.Contains("Flute") || name.Contains("Lute") || name.Contains("Lyre") ||
            name.Contains("Drum") || name.Contains("Harp") || name.Contains("Horn") ||
            name.Contains("Instrument") || name.Contains("Opus"))
            return WeaponType.Instrument;
        // Swords (catch-all for blade weapons — MUST be last)
        if (name.Contains("Sword") || name.Contains("Blade") || name.Contains("Brand") ||
            name.Contains("Avenger") || name.Contains("Executioner"))
            return WeaponType.Sword;
        // Fist weapons
        if (name.Contains("Wraps") || name.Contains("Fist") || name.Contains("Nunchaku") ||
            name.Contains("Bo Staff"))
            return WeaponType.Fist;

        return WeaponType.Sword; // Default fallback
    }

    /// <summary>
    /// Infer weapon handedness from weapon type.
    /// </summary>
    public static WeaponHandedness InferHandedness(WeaponType type) => type switch
    {
        WeaponType.Greatsword => WeaponHandedness.TwoHanded,
        WeaponType.Greataxe => WeaponHandedness.TwoHanded,
        WeaponType.Staff => WeaponHandedness.TwoHanded,
        WeaponType.Polearm => WeaponHandedness.TwoHanded,
        WeaponType.Maul => WeaponHandedness.TwoHanded,
        WeaponType.Bow => WeaponHandedness.TwoHanded,
        _ => WeaponHandedness.OneHanded,
    };

    private static WeaponType InferShieldType(string name)
    {
        if (name.Contains("Buckler"))
            return WeaponType.Buckler;
        if (name.Contains("Tower") || name.Contains("Fortress") || name.Contains("Wall") ||
            name.Contains("Bulwark"))
            return WeaponType.TowerShield;
        return WeaponType.Shield;
    }

    private static ArmorType InferArmorType(string name)
    {
        // Plate: heavy metal armor
        if (name.Contains("Plate") || name.Contains("Sabatons") || name.Contains("Greaves") ||
            name.Contains("Gauntlets") || name.Contains("Faceplate") || name.Contains("Steel") ||
            name.Contains("Iron") || name.Contains("Battle") || name.Contains("War ") ||
            name.Contains("Spiked"))
            return ArmorType.Plate;
        // Chain: medium metal armor
        if (name.Contains("Chain") || name.Contains("Mail") || name.Contains("Coif") ||
            name.Contains("Chainweave"))
            return ArmorType.Chain;
        // Scale: medium-heavy armor
        if (name.Contains("Scale") || name.Contains("Splint") || name.Contains("Banded") ||
            name.Contains("Dragonscale") || name.Contains("Dragonhide"))
            return ArmorType.Scale;
        // Leather: light-medium armor
        if (name.Contains("Leather") || name.Contains("Studded") || name.Contains("Hide") ||
            name.Contains("Scout") || name.Contains("Shadow") || name.Contains("Ranger") ||
            name.Contains("Thief") || name.Contains("Barbarian") || name.Contains("Death"))
            return ArmorType.Leather;
        // Cloth: light armor, caster gear, cloaks, belts
        if (name.Contains("Cloth") || name.Contains("Robe") || name.Contains("Silk") ||
            name.Contains("Vestment") || name.Contains("Gi") || name.Contains("Sash") ||
            name.Contains("Wizard") || name.Contains("Arcane") || name.Contains("Mystic") ||
            name.Contains("Archmage") || name.Contains("Mantle") || name.Contains("Shroud") ||
            name.Contains("Veil") || name.Contains("Headband") || name.Contains("Slippers") ||
            name.Contains("Sandals") || name.Contains("Handwraps") || name.Contains("Trousers") ||
            name.Contains("Cloak") || name.Contains("Cape") || name.Contains("Belt") ||
            name.Contains("Girdle") || name.Contains("Mask") || name.Contains("Rope") ||
            name.Contains("Tattered") || name.Contains("Traveler") || name.Contains("Elven") ||
            name.Contains("Gi"))
            return ArmorType.Cloth;
        // Magic: enchanted, divine, legendary materials
        if (name.Contains("Mithril") || name.Contains("Adamantine") || name.Contains("Holy") ||
            name.Contains("Divine") || name.Contains("Sacred") || name.Contains("Blessed") ||
            name.Contains("Enchanted") || name.Contains("Titan") || name.Contains("Dragon") ||
            name.Contains("Crown"))
            return ArmorType.Magic;

        return ArmorType.Leather; // Default fallback
    }
}
