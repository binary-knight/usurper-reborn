using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;

/// <summary>
/// Central equipment database containing all weapons, armor, shields, and accessories
/// Organized by slot type with full progression from basic to legendary items
/// </summary>
public static class EquipmentDatabase
{
    private static Dictionary<int, Equipment> _allEquipment = new();
    private static bool _initialized = false;

    // ID ranges for different equipment types
    private const int OneHandedWeaponStart = 1000;
    private const int TwoHandedWeaponStart = 2000;
    private const int ShieldStart = 3000;
    private const int HeadArmorStart = 4000;
    private const int BodyArmorStart = 5000;
    private const int ArmsArmorStart = 6000;
    private const int HandsArmorStart = 7000;
    private const int LegsArmorStart = 8000;
    private const int FeetArmorStart = 9000;
    private const int WaistArmorStart = 10000;
    private const int FaceArmorStart = 11000;
    private const int CloakStart = 12000;
    private const int NeckAccessoryStart = 13000;
    private const int RingStart = 14000;

    /// <summary>
    /// Initialize the equipment database - call once at game start
    /// </summary>
    public static void Initialize()
    {
        if (_initialized) return;

        _allEquipment.Clear();

        InitializeOneHandedWeapons();
        InitializeTwoHandedWeapons();
        InitializeShields();
        InitializeHeadArmor();
        InitializeBodyArmor();
        InitializeArmsArmor();
        InitializeHandsArmor();
        InitializeLegsArmor();
        InitializeFeetArmor();
        InitializeWaistArmor();
        InitializeFaceArmor();
        InitializeCloaks();
        InitializeNeckAccessories();
        InitializeRings();

        _initialized = true;
    }

    // Dynamic equipment starts at high ID to avoid conflicts
    private const int DynamicEquipmentStart = 100000;
    private static int _nextDynamicId = DynamicEquipmentStart;
    private static readonly object _lock = new object();

    #region Lookup Methods

    /// <summary>
    /// Get equipment by ID
    /// </summary>
    public static Equipment? GetById(int id)
    {
        EnsureInitialized();
        lock (_lock)
        {
            return _allEquipment.TryGetValue(id, out var equip) ? equip : null;
        }
    }

    /// <summary>
    /// Get equipment by name (case-insensitive search)
    /// </summary>
    public static Equipment? GetByName(string name)
    {
        if (string.IsNullOrEmpty(name)) return null;
        EnsureInitialized();
        lock (_lock)
        {
            return _allEquipment.Values.FirstOrDefault(e =>
                e.Name.Equals(name, StringComparison.OrdinalIgnoreCase));
        }
    }

    /// <summary>
    /// Register a dynamic equipment item (from inventory/crafting)
    /// Returns the assigned ID. Thread-safe for concurrent MUD sessions.
    /// </summary>
    public static int RegisterDynamic(Equipment equip)
    {
        EnsureInitialized();
        lock (_lock)
        {
            equip.Id = _nextDynamicId++;
            _allEquipment[equip.Id] = equip;
            return equip.Id;
        }
    }

    /// <summary>
    /// Register a dynamic equipment item with a specific ID (for loading saves).
    /// Thread-safe for concurrent MUD sessions.
    /// </summary>
    public static void RegisterDynamicWithId(Equipment equip, int id)
    {
        EnsureInitialized();
        lock (_lock)
        {
            equip.Id = id;
            _allEquipment[id] = equip;
            // Keep _nextDynamicId above any loaded ID to avoid conflicts
            if (id >= _nextDynamicId)
            {
                _nextDynamicId = id + 1;
            }
        }
    }

    /// <summary>
    /// Get all dynamically registered equipment (for saving)
    /// </summary>
    public static List<Equipment> GetDynamicEquipment()
    {
        EnsureInitialized();
        lock (_lock)
        {
            return _allEquipment.Values
                .Where(e => e.Id >= DynamicEquipmentStart)
                .ToList();
        }
    }

    /// <summary>
    /// Clear all dynamic equipment (for loading fresh save in single-player).
    /// WARNING: In MUD mode, this wipes ALL sessions' equipment since the database is static.
    /// Callers must gate this with a single-player check.
    /// </summary>
    public static void ClearDynamicEquipment()
    {
        EnsureInitialized();
        lock (_lock)
        {
            var dynamicIds = _allEquipment.Keys.Where(id => id >= DynamicEquipmentStart).ToList();
            foreach (var id in dynamicIds)
            {
                _allEquipment.Remove(id);
            }
            _nextDynamicId = DynamicEquipmentStart;
        }
    }

    /// <summary>
    /// Get all equipment for a specific slot (excludes dynamic/loot items by default)
    /// </summary>
    public static List<Equipment> GetBySlot(EquipmentSlot slot, bool includesDynamic = false)
    {
        EnsureInitialized();
        return _allEquipment.Values
            .Where(e => e.Slot == slot && (includesDynamic || e.Id < DynamicEquipmentStart))
            .OrderBy(e => e.Value)
            .ToList();
    }

    /// <summary>
    /// Get all weapons of a specific handedness (excludes dynamic/loot items by default)
    /// </summary>
    public static List<Equipment> GetWeaponsByHandedness(WeaponHandedness handedness, bool includeDynamic = false)
    {
        EnsureInitialized();
        return _allEquipment.Values
            .Where(e => e.Handedness == handedness && (includeDynamic || e.Id < DynamicEquipmentStart))
            .OrderBy(e => e.Value)
            .ToList();
    }

    /// <summary>
    /// Get all one-handed weapons
    /// </summary>
    public static List<Equipment> GetOneHandedWeapons() =>
        GetWeaponsByHandedness(WeaponHandedness.OneHanded);

    /// <summary>
    /// Get all two-handed weapons
    /// </summary>
    public static List<Equipment> GetTwoHandedWeapons() =>
        GetWeaponsByHandedness(WeaponHandedness.TwoHanded);

    /// <summary>
    /// Get all shields
    /// </summary>
    public static List<Equipment> GetShields()
    {
        EnsureInitialized();
        return _allEquipment.Values
            .Where(e => e.WeaponType == WeaponType.Shield)
            .OrderBy(e => e.Value)
            .ToList();
    }

    /// <summary>
    /// Get all armor for shop display (all slots)
    /// </summary>
    public static List<Equipment> GetAllArmor()
    {
        EnsureInitialized();
        return _allEquipment.Values
            .Where(e => e.Slot.IsArmorSlot())
            .OrderBy(e => e.Slot)
            .ThenBy(e => e.Value)
            .ToList();
    }

    /// <summary>
    /// Get all accessories (rings, amulets) - excludes dynamic/loot items
    /// </summary>
    public static List<Equipment> GetAccessories()
    {
        EnsureInitialized();
        return _allEquipment.Values
            .Where(e => e.Slot.IsAccessorySlot() && e.Id < DynamicEquipmentStart)
            .OrderBy(e => e.Value)
            .ToList();
    }

    /// <summary>
    /// Find best base equipment by power/AC within budget (excludes dynamic/loot items)
    /// </summary>
    public static Equipment? GetBestAffordable(EquipmentSlot slot, long maxGold)
    {
        EnsureInitialized();
        return _allEquipment.Values
            .Where(e => e.Slot == slot && e.Value <= maxGold && e.Id < DynamicEquipmentStart)
            .OrderByDescending(e => e.ArmorClass + e.WeaponPower + e.ShieldBonus)
            .FirstOrDefault();
    }

    /// <summary>
    /// Find equipment matching a power value (for save migration)
    /// </summary>
    public static Equipment? FindByPower(long power, EquipmentSlot slot)
    {
        EnsureInitialized();
        return _allEquipment.Values
            .Where(e => e.Slot == slot && e.Id < DynamicEquipmentStart)
            .OrderBy(e => Math.Abs((e.WeaponPower + e.ArmorClass + e.ShieldBonus) - power))
            .FirstOrDefault();
    }

    private static void EnsureInitialized()
    {
        if (!_initialized) Initialize();
    }

    private static void AddEquipment(Equipment equip)
    {
        _allEquipment[equip.Id] = equip;
    }

    #endregion

    #region One-Handed Weapons (55+ items - full level 1-100 progression)

    private static void InitializeOneHandedWeapons()
    {
        int id = OneHandedWeaponStart;

        // ===========================================
        // DAGGERS (Rogue weapons - fast, crit bonus)
        // ===========================================
        // Early Game (Power 2-10)
        AddEquipment(new Equipment { Id = id++, Name = "Rusty Dagger", Slot = EquipmentSlot.MainHand, Handedness = WeaponHandedness.OneHanded, WeaponType = WeaponType.Dagger, WeaponPower = 2, Value = 15, Rarity = EquipmentRarity.Common });
        AddEquipment(new Equipment { Id = id++, Name = "Iron Dagger", Slot = EquipmentSlot.MainHand, Handedness = WeaponHandedness.OneHanded, WeaponType = WeaponType.Dagger, WeaponPower = 4, Value = 50, Rarity = EquipmentRarity.Common });
        AddEquipment(new Equipment { Id = id++, Name = "Steel Dagger", Slot = EquipmentSlot.MainHand, Handedness = WeaponHandedness.OneHanded, WeaponType = WeaponType.Dagger, WeaponPower = 6, Value = 150, Rarity = EquipmentRarity.Uncommon, CriticalChanceBonus = 5 });
        AddEquipment(new Equipment { Id = id++, Name = "Assassin's Blade", Slot = EquipmentSlot.MainHand, Handedness = WeaponHandedness.OneHanded, WeaponType = WeaponType.Dagger, WeaponPower = 10, Value = 800, Rarity = EquipmentRarity.Rare, CriticalChanceBonus = 10, CriticalDamageBonus = 25 });
        // Mid Game (Power 12-20)
        AddEquipment(new Equipment { Id = id++, Name = "Shadowfang", Slot = EquipmentSlot.MainHand, Handedness = WeaponHandedness.OneHanded, WeaponType = WeaponType.Dagger, WeaponPower = 15, Value = 5000, Rarity = EquipmentRarity.Epic, CriticalChanceBonus = 15, CriticalDamageBonus = 50, PoisonDamage = 5 });
        AddEquipment(new Equipment { Id = id++, Name = "Venomstrike", Slot = EquipmentSlot.MainHand, Handedness = WeaponHandedness.OneHanded, WeaponType = WeaponType.Dagger, WeaponPower = 18, Value = 12000, Rarity = EquipmentRarity.Epic, CriticalChanceBonus = 12, PoisonDamage = 15, DexterityBonus = 4 });
        // Late Game (Power 22-35)
        AddEquipment(new Equipment { Id = id++, Name = "Nightbane Kris", Slot = EquipmentSlot.MainHand, Handedness = WeaponHandedness.OneHanded, WeaponType = WeaponType.Dagger, WeaponPower = 22, Value = 35000, Rarity = EquipmentRarity.Epic, CriticalChanceBonus = 18, CriticalDamageBonus = 60, DexterityBonus = 5 });
        AddEquipment(new Equipment { Id = id++, Name = "Serpent's Fang", Slot = EquipmentSlot.MainHand, Handedness = WeaponHandedness.OneHanded, WeaponType = WeaponType.Dagger, WeaponPower = 26, Value = 75000, Rarity = EquipmentRarity.Legendary, CriticalChanceBonus = 20, PoisonDamage = 25, DexterityBonus = 6, RequiresEvil = true });
        AddEquipment(new Equipment { Id = id++, Name = "Dagger of the Silent", Slot = EquipmentSlot.MainHand, Handedness = WeaponHandedness.OneHanded, WeaponType = WeaponType.Dagger, WeaponPower = 30, Value = 150000, Rarity = EquipmentRarity.Legendary, CriticalChanceBonus = 22, CriticalDamageBonus = 75, DexterityBonus = 8, AgilityBonus = 6 });
        // Endgame (Power 35-45)
        AddEquipment(new Equipment { Id = id++, Name = "Deathedge", Slot = EquipmentSlot.MainHand, Handedness = WeaponHandedness.OneHanded, WeaponType = WeaponType.Dagger, WeaponPower = 35, Value = 350000, Rarity = EquipmentRarity.Legendary, CriticalChanceBonus = 25, CriticalDamageBonus = 100, DexterityBonus = 10, LifeSteal = 5, IsUnique = true });
        AddEquipment(new Equipment { Id = id++, Name = "Fang of the Void", Slot = EquipmentSlot.MainHand, Handedness = WeaponHandedness.OneHanded, WeaponType = WeaponType.Dagger, WeaponPower = 40, Value = 650000, Rarity = EquipmentRarity.Artifact, CriticalChanceBonus = 28, CriticalDamageBonus = 120, DexterityBonus = 12, AgilityBonus = 10, IsUnique = true });
        AddEquipment(new Equipment { Id = id++, Name = "Godslayer Stiletto", Slot = EquipmentSlot.MainHand, Handedness = WeaponHandedness.OneHanded, WeaponType = WeaponType.Dagger, WeaponPower = 45, Value = 1200000, Rarity = EquipmentRarity.Artifact, CriticalChanceBonus = 30, CriticalDamageBonus = 150, DexterityBonus = 15, StrengthBonus = 8, IsUnique = true });

        // ===========================================
        // SWORDS (Versatile, balanced)
        // ===========================================
        // Early Game (Power 3-13)
        AddEquipment(new Equipment { Id = id++, Name = "Rusty Sword", Slot = EquipmentSlot.MainHand, Handedness = WeaponHandedness.OneHanded, WeaponType = WeaponType.Sword, WeaponPower = 3, Value = 25, Rarity = EquipmentRarity.Common });
        AddEquipment(new Equipment { Id = id++, Name = "Short Sword", Slot = EquipmentSlot.MainHand, Handedness = WeaponHandedness.OneHanded, WeaponType = WeaponType.Sword, WeaponPower = 5, Value = 100, Rarity = EquipmentRarity.Common });
        AddEquipment(new Equipment { Id = id++, Name = "Long Sword", Slot = EquipmentSlot.MainHand, Handedness = WeaponHandedness.OneHanded, WeaponType = WeaponType.Sword, WeaponPower = 8, Value = 400, Rarity = EquipmentRarity.Common });
        AddEquipment(new Equipment { Id = id++, Name = "Broad Sword", Slot = EquipmentSlot.MainHand, Handedness = WeaponHandedness.OneHanded, WeaponType = WeaponType.Sword, WeaponPower = 10, Value = 800, Rarity = EquipmentRarity.Uncommon });
        AddEquipment(new Equipment { Id = id++, Name = "Bastard Sword", Slot = EquipmentSlot.MainHand, Handedness = WeaponHandedness.OneHanded, WeaponType = WeaponType.Sword, WeaponPower = 13, Value = 2000, Rarity = EquipmentRarity.Uncommon });
        // Mid Game (Power 16-25)
        AddEquipment(new Equipment { Id = id++, Name = "Knight's Blade", Slot = EquipmentSlot.MainHand, Handedness = WeaponHandedness.OneHanded, WeaponType = WeaponType.Sword, WeaponPower = 16, Value = 5000, Rarity = EquipmentRarity.Rare, StrengthBonus = 2 });
        AddEquipment(new Equipment { Id = id++, Name = "Paladin's Sword", Slot = EquipmentSlot.MainHand, Handedness = WeaponHandedness.OneHanded, WeaponType = WeaponType.Sword, WeaponPower = 20, Value = 15000, Rarity = EquipmentRarity.Epic, StrengthBonus = 3, RequiresGood = true });
        AddEquipment(new Equipment { Id = id++, Name = "Demon Blade", Slot = EquipmentSlot.MainHand, Handedness = WeaponHandedness.OneHanded, WeaponType = WeaponType.Sword, WeaponPower = 22, Value = 18000, Rarity = EquipmentRarity.Epic, StrengthBonus = 4, RequiresEvil = true, LifeSteal = 5 });
        AddEquipment(new Equipment { Id = id++, Name = "Flamebrand", Slot = EquipmentSlot.MainHand, Handedness = WeaponHandedness.OneHanded, WeaponType = WeaponType.Sword, WeaponPower = 24, Value = 28000, Rarity = EquipmentRarity.Epic, StrengthBonus = 4, CriticalDamageBonus = 25 });
        // Late Game (Power 28-40)
        AddEquipment(new Equipment { Id = id++, Name = "Excalibur", Slot = EquipmentSlot.MainHand, Handedness = WeaponHandedness.OneHanded, WeaponType = WeaponType.Sword, WeaponPower = 30, Value = 100000, Rarity = EquipmentRarity.Legendary, StrengthBonus = 5, CharismaBonus = 5, RequiresGood = true, IsUnique = true });
        AddEquipment(new Equipment { Id = id++, Name = "Frostmourne", Slot = EquipmentSlot.MainHand, Handedness = WeaponHandedness.OneHanded, WeaponType = WeaponType.Sword, WeaponPower = 32, Value = 120000, Rarity = EquipmentRarity.Legendary, StrengthBonus = 6, LifeSteal = 8, RequiresEvil = true, IsUnique = true });
        AddEquipment(new Equipment { Id = id++, Name = "Blade of the Phoenix", Slot = EquipmentSlot.MainHand, Handedness = WeaponHandedness.OneHanded, WeaponType = WeaponType.Sword, WeaponPower = 35, Value = 200000, Rarity = EquipmentRarity.Legendary, StrengthBonus = 6, MaxHPBonus = 40, RequiresGood = true });
        AddEquipment(new Equipment { Id = id++, Name = "Soulsplitter", Slot = EquipmentSlot.MainHand, Handedness = WeaponHandedness.OneHanded, WeaponType = WeaponType.Sword, WeaponPower = 38, Value = 320000, Rarity = EquipmentRarity.Legendary, StrengthBonus = 7, LifeSteal = 10, RequiresEvil = true });
        AddEquipment(new Equipment { Id = id++, Name = "Sword of the Titans", Slot = EquipmentSlot.MainHand, Handedness = WeaponHandedness.OneHanded, WeaponType = WeaponType.Sword, WeaponPower = 40, Value = 450000, Rarity = EquipmentRarity.Legendary, StrengthBonus = 8, MaxHPBonus = 50 });
        // Endgame (Power 42-55)
        AddEquipment(new Equipment { Id = id++, Name = "Dragonbane", Slot = EquipmentSlot.MainHand, Handedness = WeaponHandedness.OneHanded, WeaponType = WeaponType.Sword, WeaponPower = 43, Value = 600000, Rarity = EquipmentRarity.Artifact, StrengthBonus = 9, MagicResistance = 15 });
        AddEquipment(new Equipment { Id = id++, Name = "Warbringer", Slot = EquipmentSlot.MainHand, Handedness = WeaponHandedness.OneHanded, WeaponType = WeaponType.Sword, WeaponPower = 46, Value = 850000, Rarity = EquipmentRarity.Artifact, StrengthBonus = 10, CriticalChanceBonus = 10, CriticalDamageBonus = 40 });
        AddEquipment(new Equipment { Id = id++, Name = "Godsbane", Slot = EquipmentSlot.MainHand, Handedness = WeaponHandedness.OneHanded, WeaponType = WeaponType.Sword, WeaponPower = 50, Value = 1500000, Rarity = EquipmentRarity.Artifact, StrengthBonus = 12, MaxHPBonus = 80, CharismaBonus = 8, IsUnique = true });
        AddEquipment(new Equipment { Id = id++, Name = "Eternity's Edge", Slot = EquipmentSlot.MainHand, Handedness = WeaponHandedness.OneHanded, WeaponType = WeaponType.Sword, WeaponPower = 55, Value = 2500000, Rarity = EquipmentRarity.Artifact, StrengthBonus = 15, DexterityBonus = 10, MaxHPBonus = 100, IsUnique = true });

        // ===========================================
        // AXES (High damage)
        // ===========================================
        // Early-Mid Game (Power 6-18)
        AddEquipment(new Equipment { Id = id++, Name = "Hand Axe", Slot = EquipmentSlot.MainHand, Handedness = WeaponHandedness.OneHanded, WeaponType = WeaponType.Axe, WeaponPower = 6, Value = 75, Rarity = EquipmentRarity.Common });
        AddEquipment(new Equipment { Id = id++, Name = "War Axe", Slot = EquipmentSlot.MainHand, Handedness = WeaponHandedness.OneHanded, WeaponType = WeaponType.Axe, WeaponPower = 10, Value = 400, Rarity = EquipmentRarity.Common });
        AddEquipment(new Equipment { Id = id++, Name = "Battle Axe", Slot = EquipmentSlot.MainHand, Handedness = WeaponHandedness.OneHanded, WeaponType = WeaponType.Axe, WeaponPower = 14, Value = 1500, Rarity = EquipmentRarity.Uncommon });
        AddEquipment(new Equipment { Id = id++, Name = "Berserker Axe", Slot = EquipmentSlot.MainHand, Handedness = WeaponHandedness.OneHanded, WeaponType = WeaponType.Axe, WeaponPower = 18, Value = 6000, Rarity = EquipmentRarity.Rare, StrengthBonus = 3, CriticalDamageBonus = 20 });
        // Mid-Late Game (Power 22-35)
        AddEquipment(new Equipment { Id = id++, Name = "Executioner's Axe", Slot = EquipmentSlot.MainHand, Handedness = WeaponHandedness.OneHanded, WeaponType = WeaponType.Axe, WeaponPower = 25, Value = 25000, Rarity = EquipmentRarity.Epic, StrengthBonus = 5, CriticalDamageBonus = 50 });
        AddEquipment(new Equipment { Id = id++, Name = "Crimson Cleaver", Slot = EquipmentSlot.MainHand, Handedness = WeaponHandedness.OneHanded, WeaponType = WeaponType.Axe, WeaponPower = 28, Value = 55000, Rarity = EquipmentRarity.Epic, StrengthBonus = 6, LifeSteal = 6, RequiresEvil = true });
        AddEquipment(new Equipment { Id = id++, Name = "Warlord's Hatchet", Slot = EquipmentSlot.MainHand, Handedness = WeaponHandedness.OneHanded, WeaponType = WeaponType.Axe, WeaponPower = 32, Value = 110000, Rarity = EquipmentRarity.Legendary, StrengthBonus = 7, CriticalDamageBonus = 60 });
        AddEquipment(new Equipment { Id = id++, Name = "Skullsplitter", Slot = EquipmentSlot.MainHand, Handedness = WeaponHandedness.OneHanded, WeaponType = WeaponType.Axe, WeaponPower = 36, Value = 220000, Rarity = EquipmentRarity.Legendary, StrengthBonus = 8, CriticalChanceBonus = 12, CriticalDamageBonus = 70 });
        // Endgame (Power 40-50)
        AddEquipment(new Equipment { Id = id++, Name = "Gorehowl", Slot = EquipmentSlot.MainHand, Handedness = WeaponHandedness.OneHanded, WeaponType = WeaponType.Axe, WeaponPower = 42, Value = 500000, Rarity = EquipmentRarity.Artifact, StrengthBonus = 10, CriticalDamageBonus = 85, LifeSteal = 8, IsUnique = true });
        AddEquipment(new Equipment { Id = id++, Name = "Worldcleaver", Slot = EquipmentSlot.MainHand, Handedness = WeaponHandedness.OneHanded, WeaponType = WeaponType.Axe, WeaponPower = 48, Value = 1100000, Rarity = EquipmentRarity.Artifact, StrengthBonus = 13, MaxHPBonus = 60, CriticalDamageBonus = 100, IsUnique = true });

        // ===========================================
        // MACES (Armor piercing, Cleric weapons)
        // ===========================================
        // Early-Mid Game (Power 4-15)
        AddEquipment(new Equipment { Id = id++, Name = "Wooden Club", Slot = EquipmentSlot.MainHand, Handedness = WeaponHandedness.OneHanded, WeaponType = WeaponType.Mace, WeaponPower = 4, Value = 30, Rarity = EquipmentRarity.Common });
        AddEquipment(new Equipment { Id = id++, Name = "Iron Mace", Slot = EquipmentSlot.MainHand, Handedness = WeaponHandedness.OneHanded, WeaponType = WeaponType.Mace, WeaponPower = 7, Value = 200, Rarity = EquipmentRarity.Common });
        AddEquipment(new Equipment { Id = id++, Name = "Flanged Mace", Slot = EquipmentSlot.MainHand, Handedness = WeaponHandedness.OneHanded, WeaponType = WeaponType.Mace, WeaponPower = 11, Value = 800, Rarity = EquipmentRarity.Uncommon });
        AddEquipment(new Equipment { Id = id++, Name = "Holy Mace", Slot = EquipmentSlot.MainHand, Handedness = WeaponHandedness.OneHanded, WeaponType = WeaponType.Mace, WeaponPower = 15, Value = 4000, Rarity = EquipmentRarity.Rare, WisdomBonus = 3, RequiresGood = true });
        // Mid-Late Game (Power 18-32)
        AddEquipment(new Equipment { Id = id++, Name = "Skull Crusher", Slot = EquipmentSlot.MainHand, Handedness = WeaponHandedness.OneHanded, WeaponType = WeaponType.Mace, WeaponPower = 20, Value = 12000, Rarity = EquipmentRarity.Epic, StrengthBonus = 4 });
        AddEquipment(new Equipment { Id = id++, Name = "Morningstar", Slot = EquipmentSlot.MainHand, Handedness = WeaponHandedness.OneHanded, WeaponType = WeaponType.Mace, WeaponPower = 23, Value = 22000, Rarity = EquipmentRarity.Epic, StrengthBonus = 5, CriticalChanceBonus = 8 });
        AddEquipment(new Equipment { Id = id++, Name = "Sanctified Crusher", Slot = EquipmentSlot.MainHand, Handedness = WeaponHandedness.OneHanded, WeaponType = WeaponType.Mace, WeaponPower = 26, Value = 45000, Rarity = EquipmentRarity.Legendary, WisdomBonus = 6, MaxManaBonus = 40, RequiresGood = true });
        AddEquipment(new Equipment { Id = id++, Name = "Bonecrusher", Slot = EquipmentSlot.MainHand, Handedness = WeaponHandedness.OneHanded, WeaponType = WeaponType.Mace, WeaponPower = 30, Value = 95000, Rarity = EquipmentRarity.Legendary, StrengthBonus = 7, ConstitutionBonus = 5 });
        AddEquipment(new Equipment { Id = id++, Name = "Doomhammer", Slot = EquipmentSlot.MainHand, Handedness = WeaponHandedness.OneHanded, WeaponType = WeaponType.Mace, WeaponPower = 34, Value = 180000, Rarity = EquipmentRarity.Legendary, StrengthBonus = 8, MaxHPBonus = 40, IsUnique = true });
        // Endgame (Power 38-48)
        AddEquipment(new Equipment { Id = id++, Name = "Lightbringer", Slot = EquipmentSlot.MainHand, Handedness = WeaponHandedness.OneHanded, WeaponType = WeaponType.Mace, WeaponPower = 40, Value = 400000, Rarity = EquipmentRarity.Artifact, WisdomBonus = 10, MaxManaBonus = 80, MaxHPBonus = 50, RequiresGood = true, IsUnique = true });
        AddEquipment(new Equipment { Id = id++, Name = "Soulflayer", Slot = EquipmentSlot.MainHand, Handedness = WeaponHandedness.OneHanded, WeaponType = WeaponType.Mace, WeaponPower = 44, Value = 750000, Rarity = EquipmentRarity.Artifact, StrengthBonus = 11, LifeSteal = 12, RequiresEvil = true, IsUnique = true });
        AddEquipment(new Equipment { Id = id++, Name = "Hammer of the Gods", Slot = EquipmentSlot.MainHand, Handedness = WeaponHandedness.OneHanded, WeaponType = WeaponType.Mace, WeaponPower = 50, Value = 1400000, Rarity = EquipmentRarity.Artifact, StrengthBonus = 14, WisdomBonus = 10, MaxHPBonus = 80, IsUnique = true });

        // ===========================================
        // RAPIERS (Dex-based, fast, critical)
        // ===========================================
        // Early-Mid Game (Power 4-15)
        AddEquipment(new Equipment { Id = id++, Name = "Fencing Foil", Slot = EquipmentSlot.MainHand, Handedness = WeaponHandedness.OneHanded, WeaponType = WeaponType.Rapier, WeaponPower = 4, Value = 80, Rarity = EquipmentRarity.Common, DexterityBonus = 1 });
        AddEquipment(new Equipment { Id = id++, Name = "Rapier", Slot = EquipmentSlot.MainHand, Handedness = WeaponHandedness.OneHanded, WeaponType = WeaponType.Rapier, WeaponPower = 7, Value = 350, Rarity = EquipmentRarity.Common, DexterityBonus = 2 });
        AddEquipment(new Equipment { Id = id++, Name = "Duelist's Rapier", Slot = EquipmentSlot.MainHand, Handedness = WeaponHandedness.OneHanded, WeaponType = WeaponType.Rapier, WeaponPower = 11, Value = 1200, Rarity = EquipmentRarity.Uncommon, DexterityBonus = 3, CriticalChanceBonus = 5 });
        AddEquipment(new Equipment { Id = id++, Name = "Estoc", Slot = EquipmentSlot.MainHand, Handedness = WeaponHandedness.OneHanded, WeaponType = WeaponType.Rapier, WeaponPower = 15, Value = 5000, Rarity = EquipmentRarity.Rare, DexterityBonus = 4, CriticalChanceBonus = 10 });
        // Mid-Late Game (Power 18-32)
        AddEquipment(new Equipment { Id = id++, Name = "Dancer's Blade", Slot = EquipmentSlot.MainHand, Handedness = WeaponHandedness.OneHanded, WeaponType = WeaponType.Rapier, WeaponPower = 18, Value = 20000, Rarity = EquipmentRarity.Epic, DexterityBonus = 6, AgilityBonus = 5, CriticalChanceBonus = 15 });
        AddEquipment(new Equipment { Id = id++, Name = "Silverwind", Slot = EquipmentSlot.MainHand, Handedness = WeaponHandedness.OneHanded, WeaponType = WeaponType.Rapier, WeaponPower = 22, Value = 40000, Rarity = EquipmentRarity.Epic, DexterityBonus = 7, AgilityBonus = 6, CriticalChanceBonus = 18 });
        AddEquipment(new Equipment { Id = id++, Name = "Swiftblade", Slot = EquipmentSlot.MainHand, Handedness = WeaponHandedness.OneHanded, WeaponType = WeaponType.Rapier, WeaponPower = 26, Value = 75000, Rarity = EquipmentRarity.Legendary, DexterityBonus = 8, AgilityBonus = 8, CriticalChanceBonus = 20 });
        AddEquipment(new Equipment { Id = id++, Name = "Moonsilver Rapier", Slot = EquipmentSlot.MainHand, Handedness = WeaponHandedness.OneHanded, WeaponType = WeaponType.Rapier, WeaponPower = 30, Value = 140000, Rarity = EquipmentRarity.Legendary, DexterityBonus = 10, AgilityBonus = 10, CriticalChanceBonus = 22, MagicResistance = 10 });
        // Endgame (Power 35-48)
        AddEquipment(new Equipment { Id = id++, Name = "Whisper of Death", Slot = EquipmentSlot.MainHand, Handedness = WeaponHandedness.OneHanded, WeaponType = WeaponType.Rapier, WeaponPower = 36, Value = 300000, Rarity = EquipmentRarity.Legendary, DexterityBonus = 12, AgilityBonus = 12, CriticalChanceBonus = 25, CriticalDamageBonus = 50, IsUnique = true });
        AddEquipment(new Equipment { Id = id++, Name = "Phantom Edge", Slot = EquipmentSlot.MainHand, Handedness = WeaponHandedness.OneHanded, WeaponType = WeaponType.Rapier, WeaponPower = 42, Value = 620000, Rarity = EquipmentRarity.Artifact, DexterityBonus = 14, AgilityBonus = 14, CriticalChanceBonus = 28, CriticalDamageBonus = 70, IsUnique = true });
        AddEquipment(new Equipment { Id = id++, Name = "Blade of the Wind God", Slot = EquipmentSlot.MainHand, Handedness = WeaponHandedness.OneHanded, WeaponType = WeaponType.Rapier, WeaponPower = 48, Value = 1300000, Rarity = EquipmentRarity.Artifact, DexterityBonus = 18, AgilityBonus = 18, CriticalChanceBonus = 32, CriticalDamageBonus = 90, IsUnique = true });

        // ===========================================
        // SCIMITARS (Crit bonus, balanced)
        // ===========================================
        AddEquipment(new Equipment { Id = id++, Name = "Curved Blade", Slot = EquipmentSlot.MainHand, Handedness = WeaponHandedness.OneHanded, WeaponType = WeaponType.Scimitar, WeaponPower = 7, Value = 250, Rarity = EquipmentRarity.Common, CriticalChanceBonus = 3 });
        AddEquipment(new Equipment { Id = id++, Name = "Scimitar", Slot = EquipmentSlot.MainHand, Handedness = WeaponHandedness.OneHanded, WeaponType = WeaponType.Scimitar, WeaponPower = 12, Value = 1000, Rarity = EquipmentRarity.Uncommon, CriticalChanceBonus = 6, DexterityBonus = 2 });
        AddEquipment(new Equipment { Id = id++, Name = "Desert Wind", Slot = EquipmentSlot.MainHand, Handedness = WeaponHandedness.OneHanded, WeaponType = WeaponType.Scimitar, WeaponPower = 18, Value = 8000, Rarity = EquipmentRarity.Rare, CriticalChanceBonus = 10, DexterityBonus = 4, AgilityBonus = 3 });
        AddEquipment(new Equipment { Id = id++, Name = "Sandstorm Saber", Slot = EquipmentSlot.MainHand, Handedness = WeaponHandedness.OneHanded, WeaponType = WeaponType.Scimitar, WeaponPower = 25, Value = 35000, Rarity = EquipmentRarity.Epic, CriticalChanceBonus = 15, DexterityBonus = 6, AgilityBonus = 5 });
        AddEquipment(new Equipment { Id = id++, Name = "Crescent Moon Blade", Slot = EquipmentSlot.MainHand, Handedness = WeaponHandedness.OneHanded, WeaponType = WeaponType.Scimitar, WeaponPower = 33, Value = 130000, Rarity = EquipmentRarity.Legendary, CriticalChanceBonus = 20, DexterityBonus = 8, AgilityBonus = 8, StrengthBonus = 4 });
        AddEquipment(new Equipment { Id = id++, Name = "Sunfire Scimitar", Slot = EquipmentSlot.MainHand, Handedness = WeaponHandedness.OneHanded, WeaponType = WeaponType.Scimitar, WeaponPower = 42, Value = 550000, Rarity = EquipmentRarity.Artifact, CriticalChanceBonus = 25, DexterityBonus = 12, AgilityBonus = 10, StrengthBonus = 6, RequiresGood = true, IsUnique = true });

        // ===========================================
        // FLAILS (Ignores shield, unique mechanics)
        // ===========================================
        AddEquipment(new Equipment { Id = id++, Name = "Light Flail", Slot = EquipmentSlot.MainHand, Handedness = WeaponHandedness.OneHanded, WeaponType = WeaponType.Flail, WeaponPower = 8, Value = 300, Rarity = EquipmentRarity.Common });
        AddEquipment(new Equipment { Id = id++, Name = "Military Flail", Slot = EquipmentSlot.MainHand, Handedness = WeaponHandedness.OneHanded, WeaponType = WeaponType.Flail, WeaponPower = 14, Value = 1800, Rarity = EquipmentRarity.Uncommon, StrengthBonus = 2 });
        AddEquipment(new Equipment { Id = id++, Name = "Spiked Flail", Slot = EquipmentSlot.MainHand, Handedness = WeaponHandedness.OneHanded, WeaponType = WeaponType.Flail, WeaponPower = 20, Value = 9000, Rarity = EquipmentRarity.Rare, StrengthBonus = 4, CriticalDamageBonus = 15 });
        AddEquipment(new Equipment { Id = id++, Name = "Chaos Flail", Slot = EquipmentSlot.MainHand, Handedness = WeaponHandedness.OneHanded, WeaponType = WeaponType.Flail, WeaponPower = 28, Value = 60000, Rarity = EquipmentRarity.Epic, StrengthBonus = 6, CriticalDamageBonus = 30, RequiresEvil = true });
        AddEquipment(new Equipment { Id = id++, Name = "Doom Flail", Slot = EquipmentSlot.MainHand, Handedness = WeaponHandedness.OneHanded, WeaponType = WeaponType.Flail, WeaponPower = 38, Value = 250000, Rarity = EquipmentRarity.Legendary, StrengthBonus = 9, CriticalDamageBonus = 50, LifeSteal = 6 });
        AddEquipment(new Equipment { Id = id++, Name = "Apocalypse Flail", Slot = EquipmentSlot.MainHand, Handedness = WeaponHandedness.OneHanded, WeaponType = WeaponType.Flail, WeaponPower = 47, Value = 950000, Rarity = EquipmentRarity.Artifact, StrengthBonus = 13, CriticalDamageBonus = 75, LifeSteal = 10, MaxHPBonus = 50, IsUnique = true });

        // ===========================================
        // HAMMERS (Stun chance, armor breaking)
        // ===========================================
        AddEquipment(new Equipment { Id = id++, Name = "Smithing Hammer", Slot = EquipmentSlot.MainHand, Handedness = WeaponHandedness.OneHanded, WeaponType = WeaponType.Hammer, WeaponPower = 5, Value = 60, Rarity = EquipmentRarity.Common });
        AddEquipment(new Equipment { Id = id++, Name = "War Hammer", Slot = EquipmentSlot.MainHand, Handedness = WeaponHandedness.OneHanded, WeaponType = WeaponType.Hammer, WeaponPower = 12, Value = 900, Rarity = EquipmentRarity.Uncommon, StrengthBonus = 2 });
        AddEquipment(new Equipment { Id = id++, Name = "Thunderstrike", Slot = EquipmentSlot.MainHand, Handedness = WeaponHandedness.OneHanded, WeaponType = WeaponType.Hammer, WeaponPower = 19, Value = 7500, Rarity = EquipmentRarity.Rare, StrengthBonus = 4, CriticalChanceBonus = 5 });
        AddEquipment(new Equipment { Id = id++, Name = "Stormbreaker", Slot = EquipmentSlot.MainHand, Handedness = WeaponHandedness.OneHanded, WeaponType = WeaponType.Hammer, WeaponPower = 27, Value = 50000, Rarity = EquipmentRarity.Epic, StrengthBonus = 6, CriticalChanceBonus = 8, MagicResistance = 8 });
        AddEquipment(new Equipment { Id = id++, Name = "Mjolnir", Slot = EquipmentSlot.MainHand, Handedness = WeaponHandedness.OneHanded, WeaponType = WeaponType.Hammer, WeaponPower = 38, Value = 280000, Rarity = EquipmentRarity.Legendary, StrengthBonus = 10, CriticalChanceBonus = 12, MagicResistance = 15, RequiresGood = true, IsUnique = true });
        AddEquipment(new Equipment { Id = id++, Name = "Worldshaper", Slot = EquipmentSlot.MainHand, Handedness = WeaponHandedness.OneHanded, WeaponType = WeaponType.Hammer, WeaponPower = 46, Value = 900000, Rarity = EquipmentRarity.Artifact, StrengthBonus = 14, ConstitutionBonus = 10, MaxHPBonus = 100, MagicResistance = 20, IsUnique = true });
    }

    #endregion

    #region Two-Handed Weapons (45+ items - full level 1-100 progression)

    private static void InitializeTwoHandedWeapons()
    {
        int id = TwoHandedWeaponStart;

        // ===========================================
        // GREATSWORDS (High damage, balanced 2H)
        // ===========================================
        // Early Game (Power 8-22)
        AddEquipment(new Equipment { Id = id++, Name = "Wooden Greatsword", Slot = EquipmentSlot.MainHand, Handedness = WeaponHandedness.TwoHanded, WeaponType = WeaponType.Greatsword, WeaponPower = 8, Value = 100, Rarity = EquipmentRarity.Common });
        AddEquipment(new Equipment { Id = id++, Name = "Iron Greatsword", Slot = EquipmentSlot.MainHand, Handedness = WeaponHandedness.TwoHanded, WeaponType = WeaponType.Greatsword, WeaponPower = 12, Value = 500, Rarity = EquipmentRarity.Common });
        AddEquipment(new Equipment { Id = id++, Name = "Steel Greatsword", Slot = EquipmentSlot.MainHand, Handedness = WeaponHandedness.TwoHanded, WeaponType = WeaponType.Greatsword, WeaponPower = 16, Value = 1500, Rarity = EquipmentRarity.Uncommon });
        AddEquipment(new Equipment { Id = id++, Name = "Claymore", Slot = EquipmentSlot.MainHand, Handedness = WeaponHandedness.TwoHanded, WeaponType = WeaponType.Greatsword, WeaponPower = 22, Value = 5000, Rarity = EquipmentRarity.Rare, StrengthBonus = 3 });
        // Mid Game (Power 28-40)
        AddEquipment(new Equipment { Id = id++, Name = "Zweihander", Slot = EquipmentSlot.MainHand, Handedness = WeaponHandedness.TwoHanded, WeaponType = WeaponType.Greatsword, WeaponPower = 28, Value = 15000, Rarity = EquipmentRarity.Epic, StrengthBonus = 5 });
        AddEquipment(new Equipment { Id = id++, Name = "Bloodreaver", Slot = EquipmentSlot.MainHand, Handedness = WeaponHandedness.TwoHanded, WeaponType = WeaponType.Greatsword, WeaponPower = 34, Value = 40000, Rarity = EquipmentRarity.Epic, StrengthBonus = 6, LifeSteal = 8, RequiresEvil = true });
        AddEquipment(new Equipment { Id = id++, Name = "Dragonslayer", Slot = EquipmentSlot.MainHand, Handedness = WeaponHandedness.TwoHanded, WeaponType = WeaponType.Greatsword, WeaponPower = 40, Value = 150000, Rarity = EquipmentRarity.Legendary, StrengthBonus = 8, MaxHPBonus = 50, IsUnique = true });
        // Late Game (Power 46-58)
        AddEquipment(new Equipment { Id = id++, Name = "Flamberge", Slot = EquipmentSlot.MainHand, Handedness = WeaponHandedness.TwoHanded, WeaponType = WeaponType.Greatsword, WeaponPower = 46, Value = 250000, Rarity = EquipmentRarity.Legendary, StrengthBonus = 9, CriticalDamageBonus = 40 });
        AddEquipment(new Equipment { Id = id++, Name = "Armageddon Blade", Slot = EquipmentSlot.MainHand, Handedness = WeaponHandedness.TwoHanded, WeaponType = WeaponType.Greatsword, WeaponPower = 52, Value = 450000, Rarity = EquipmentRarity.Legendary, StrengthBonus = 10, MaxHPBonus = 70, CriticalChanceBonus = 10 });
        AddEquipment(new Equipment { Id = id++, Name = "Ragnarok", Slot = EquipmentSlot.MainHand, Handedness = WeaponHandedness.TwoHanded, WeaponType = WeaponType.Greatsword, WeaponPower = 58, Value = 700000, Rarity = EquipmentRarity.Artifact, StrengthBonus = 12, MaxHPBonus = 80, MagicResistance = 15, IsUnique = true });
        // Endgame (Power 65-80)
        AddEquipment(new Equipment { Id = id++, Name = "Worldender", Slot = EquipmentSlot.MainHand, Handedness = WeaponHandedness.TwoHanded, WeaponType = WeaponType.Greatsword, WeaponPower = 65, Value = 1200000, Rarity = EquipmentRarity.Artifact, StrengthBonus = 14, MaxHPBonus = 100, CriticalDamageBonus = 60, IsUnique = true });
        AddEquipment(new Equipment { Id = id++, Name = "Blade of Oblivion", Slot = EquipmentSlot.MainHand, Handedness = WeaponHandedness.TwoHanded, WeaponType = WeaponType.Greatsword, WeaponPower = 72, Value = 2200000, Rarity = EquipmentRarity.Artifact, StrengthBonus = 16, MaxHPBonus = 120, LifeSteal = 12, RequiresEvil = true, IsUnique = true });
        AddEquipment(new Equipment { Id = id++, Name = "Sword of Creation", Slot = EquipmentSlot.MainHand, Handedness = WeaponHandedness.TwoHanded, WeaponType = WeaponType.Greatsword, WeaponPower = 80, Value = 4000000, Rarity = EquipmentRarity.Artifact, StrengthBonus = 20, DexterityBonus = 12, MaxHPBonus = 150, CharismaBonus = 10, IsUnique = true });

        // ===========================================
        // GREAT AXES (Highest damage, crit focused)
        // ===========================================
        // Early Game (Power 10-26)
        AddEquipment(new Equipment { Id = id++, Name = "Woodcutter's Axe", Slot = EquipmentSlot.MainHand, Handedness = WeaponHandedness.TwoHanded, WeaponType = WeaponType.Greataxe, WeaponPower = 10, Value = 150, Rarity = EquipmentRarity.Common });
        AddEquipment(new Equipment { Id = id++, Name = "Greataxe", Slot = EquipmentSlot.MainHand, Handedness = WeaponHandedness.TwoHanded, WeaponType = WeaponType.Greataxe, WeaponPower = 18, Value = 2000, Rarity = EquipmentRarity.Uncommon, StrengthBonus = 2 });
        AddEquipment(new Equipment { Id = id++, Name = "Barbarian's Greataxe", Slot = EquipmentSlot.MainHand, Handedness = WeaponHandedness.TwoHanded, WeaponType = WeaponType.Greataxe, WeaponPower = 26, Value = 8000, Rarity = EquipmentRarity.Rare, StrengthBonus = 4, CriticalDamageBonus = 30 });
        // Mid Game (Power 35-50)
        AddEquipment(new Equipment { Id = id++, Name = "Demon Cleaver", Slot = EquipmentSlot.MainHand, Handedness = WeaponHandedness.TwoHanded, WeaponType = WeaponType.Greataxe, WeaponPower = 35, Value = 50000, Rarity = EquipmentRarity.Epic, StrengthBonus = 6, LifeSteal = 10, RequiresEvil = true });
        AddEquipment(new Equipment { Id = id++, Name = "Headsman's Pride", Slot = EquipmentSlot.MainHand, Handedness = WeaponHandedness.TwoHanded, WeaponType = WeaponType.Greataxe, WeaponPower = 42, Value = 120000, Rarity = EquipmentRarity.Legendary, StrengthBonus = 8, CriticalChanceBonus = 15, CriticalDamageBonus = 50 });
        AddEquipment(new Equipment { Id = id++, Name = "Annihilator", Slot = EquipmentSlot.MainHand, Handedness = WeaponHandedness.TwoHanded, WeaponType = WeaponType.Greataxe, WeaponPower = 50, Value = 280000, Rarity = EquipmentRarity.Legendary, StrengthBonus = 10, CriticalDamageBonus = 70 });
        // Late Game (Power 58-70)
        AddEquipment(new Equipment { Id = id++, Name = "Dread Cleaver", Slot = EquipmentSlot.MainHand, Handedness = WeaponHandedness.TwoHanded, WeaponType = WeaponType.Greataxe, WeaponPower = 58, Value = 550000, Rarity = EquipmentRarity.Artifact, StrengthBonus = 12, CriticalDamageBonus = 90, LifeSteal = 12 });
        AddEquipment(new Equipment { Id = id++, Name = "Carnage", Slot = EquipmentSlot.MainHand, Handedness = WeaponHandedness.TwoHanded, WeaponType = WeaponType.Greataxe, WeaponPower = 65, Value = 950000, Rarity = EquipmentRarity.Artifact, StrengthBonus = 14, CriticalChanceBonus = 18, CriticalDamageBonus = 110, RequiresEvil = true, IsUnique = true });
        AddEquipment(new Equipment { Id = id++, Name = "Titan's Cleaver", Slot = EquipmentSlot.MainHand, Handedness = WeaponHandedness.TwoHanded, WeaponType = WeaponType.Greataxe, WeaponPower = 72, Value = 1800000, Rarity = EquipmentRarity.Artifact, StrengthBonus = 16, ConstitutionBonus = 12, MaxHPBonus = 120, CriticalDamageBonus = 100, IsUnique = true });
        // Ultimate
        AddEquipment(new Equipment { Id = id++, Name = "Axe of the World Tree", Slot = EquipmentSlot.MainHand, Handedness = WeaponHandedness.TwoHanded, WeaponType = WeaponType.Greataxe, WeaponPower = 82, Value = 3500000, Rarity = EquipmentRarity.Artifact, StrengthBonus = 20, MaxHPBonus = 150, CriticalChanceBonus = 20, CriticalDamageBonus = 130, IsUnique = true });

        // ===========================================
        // STAVES (Magic weapons - Mana focused)
        // ===========================================
        // Early Game (Power 5-18)
        AddEquipment(new Equipment { Id = id++, Name = "Wooden Staff", Slot = EquipmentSlot.MainHand, Handedness = WeaponHandedness.TwoHanded, WeaponType = WeaponType.Staff, WeaponPower = 5, Value = 50, Rarity = EquipmentRarity.Common, MaxManaBonus = 10 });
        AddEquipment(new Equipment { Id = id++, Name = "Oak Staff", Slot = EquipmentSlot.MainHand, Handedness = WeaponHandedness.TwoHanded, WeaponType = WeaponType.Staff, WeaponPower = 8, Value = 300, Rarity = EquipmentRarity.Common, MaxManaBonus = 20, WisdomBonus = 1 });
        AddEquipment(new Equipment { Id = id++, Name = "Mage's Staff", Slot = EquipmentSlot.MainHand, Handedness = WeaponHandedness.TwoHanded, WeaponType = WeaponType.Staff, WeaponPower = 12, Value = 1000, Rarity = EquipmentRarity.Uncommon, MaxManaBonus = 40, WisdomBonus = 2, IntelligenceBonus = 2 });
        AddEquipment(new Equipment { Id = id++, Name = "Archmage Staff", Slot = EquipmentSlot.MainHand, Handedness = WeaponHandedness.TwoHanded, WeaponType = WeaponType.Staff, WeaponPower = 18, Value = 6000, Rarity = EquipmentRarity.Rare, MaxManaBonus = 80, WisdomBonus = 4, IntelligenceBonus = 4 });
        // Mid Game (Power 25-40)
        AddEquipment(new Equipment { Id = id++, Name = "Staff of Power", Slot = EquipmentSlot.MainHand, Handedness = WeaponHandedness.TwoHanded, WeaponType = WeaponType.Staff, WeaponPower = 25, Value = 30000, Rarity = EquipmentRarity.Epic, MaxManaBonus = 150, WisdomBonus = 6, IntelligenceBonus = 6, MagicResistance = 10 });
        AddEquipment(new Equipment { Id = id++, Name = "Staff of the Cosmos", Slot = EquipmentSlot.MainHand, Handedness = WeaponHandedness.TwoHanded, WeaponType = WeaponType.Staff, WeaponPower = 35, Value = 200000, Rarity = EquipmentRarity.Legendary, MaxManaBonus = 300, WisdomBonus = 10, IntelligenceBonus = 10, MagicResistance = 25, IsUnique = true });
        AddEquipment(new Equipment { Id = id++, Name = "Soulstaff", Slot = EquipmentSlot.MainHand, Handedness = WeaponHandedness.TwoHanded, WeaponType = WeaponType.Staff, WeaponPower = 40, Value = 320000, Rarity = EquipmentRarity.Legendary, MaxManaBonus = 400, WisdomBonus = 12, IntelligenceBonus = 12, LifeSteal = 6 });
        // Late Game (Power 48-60)
        AddEquipment(new Equipment { Id = id++, Name = "Void Staff", Slot = EquipmentSlot.MainHand, Handedness = WeaponHandedness.TwoHanded, WeaponType = WeaponType.Staff, WeaponPower = 48, Value = 550000, Rarity = EquipmentRarity.Artifact, MaxManaBonus = 500, WisdomBonus = 14, IntelligenceBonus = 14, MagicResistance = 30, RequiresEvil = true });
        AddEquipment(new Equipment { Id = id++, Name = "Staff of the Heavens", Slot = EquipmentSlot.MainHand, Handedness = WeaponHandedness.TwoHanded, WeaponType = WeaponType.Staff, WeaponPower = 55, Value = 900000, Rarity = EquipmentRarity.Artifact, MaxManaBonus = 600, WisdomBonus = 16, IntelligenceBonus = 16, MagicResistance = 35, MaxHPBonus = 60, RequiresGood = true, IsUnique = true });
        AddEquipment(new Equipment { Id = id++, Name = "Infinity Rod", Slot = EquipmentSlot.MainHand, Handedness = WeaponHandedness.TwoHanded, WeaponType = WeaponType.Staff, WeaponPower = 62, Value = 1500000, Rarity = EquipmentRarity.Artifact, MaxManaBonus = 750, WisdomBonus = 18, IntelligenceBonus = 18, MagicResistance = 40, IsUnique = true });
        // Ultimate
        AddEquipment(new Equipment { Id = id++, Name = "Staff of Eternity", Slot = EquipmentSlot.MainHand, Handedness = WeaponHandedness.TwoHanded, WeaponType = WeaponType.Staff, WeaponPower = 70, Value = 3000000, Rarity = EquipmentRarity.Artifact, MaxManaBonus = 1000, WisdomBonus = 22, IntelligenceBonus = 22, MagicResistance = 50, MaxHPBonus = 100, IsUnique = true });

        // ===========================================
        // POLEARMS (Reach, versatile)
        // ===========================================
        // Early Game (Power 9-20)
        AddEquipment(new Equipment { Id = id++, Name = "Spear", Slot = EquipmentSlot.MainHand, Handedness = WeaponHandedness.TwoHanded, WeaponType = WeaponType.Polearm, WeaponPower = 9, Value = 200, Rarity = EquipmentRarity.Common });
        AddEquipment(new Equipment { Id = id++, Name = "Halberd", Slot = EquipmentSlot.MainHand, Handedness = WeaponHandedness.TwoHanded, WeaponType = WeaponType.Polearm, WeaponPower = 15, Value = 1200, Rarity = EquipmentRarity.Uncommon, StrengthBonus = 1, DexterityBonus = 1 });
        AddEquipment(new Equipment { Id = id++, Name = "Glaive", Slot = EquipmentSlot.MainHand, Handedness = WeaponHandedness.TwoHanded, WeaponType = WeaponType.Polearm, WeaponPower = 20, Value = 4000, Rarity = EquipmentRarity.Rare, StrengthBonus = 2, DexterityBonus = 2 });
        // Mid Game (Power 28-45)
        AddEquipment(new Equipment { Id = id++, Name = "Bardiche", Slot = EquipmentSlot.MainHand, Handedness = WeaponHandedness.TwoHanded, WeaponType = WeaponType.Polearm, WeaponPower = 28, Value = 18000, Rarity = EquipmentRarity.Epic, StrengthBonus = 4, DexterityBonus = 3, CriticalChanceBonus = 8 });
        AddEquipment(new Equipment { Id = id++, Name = "Dragon Lance", Slot = EquipmentSlot.MainHand, Handedness = WeaponHandedness.TwoHanded, WeaponType = WeaponType.Polearm, WeaponPower = 36, Value = 80000, Rarity = EquipmentRarity.Epic, StrengthBonus = 5, DexterityBonus = 5, CriticalChanceBonus = 10 });
        AddEquipment(new Equipment { Id = id++, Name = "Voulge of Rending", Slot = EquipmentSlot.MainHand, Handedness = WeaponHandedness.TwoHanded, WeaponType = WeaponType.Polearm, WeaponPower = 45, Value = 180000, Rarity = EquipmentRarity.Legendary, StrengthBonus = 7, DexterityBonus = 6, CriticalDamageBonus = 40 });
        // Late Game (Power 54-68)
        AddEquipment(new Equipment { Id = id++, Name = "Gungnir", Slot = EquipmentSlot.MainHand, Handedness = WeaponHandedness.TwoHanded, WeaponType = WeaponType.Polearm, WeaponPower = 54, Value = 400000, Rarity = EquipmentRarity.Legendary, StrengthBonus = 10, DexterityBonus = 10, CriticalChanceBonus = 15, RequiresGood = true, IsUnique = true });
        AddEquipment(new Equipment { Id = id++, Name = "Impaler", Slot = EquipmentSlot.MainHand, Handedness = WeaponHandedness.TwoHanded, WeaponType = WeaponType.Polearm, WeaponPower = 60, Value = 700000, Rarity = EquipmentRarity.Artifact, StrengthBonus = 12, DexterityBonus = 10, CriticalChanceBonus = 18, LifeSteal = 8, RequiresEvil = true });
        AddEquipment(new Equipment { Id = id++, Name = "Longinus", Slot = EquipmentSlot.MainHand, Handedness = WeaponHandedness.TwoHanded, WeaponType = WeaponType.Polearm, WeaponPower = 68, Value = 1400000, Rarity = EquipmentRarity.Artifact, StrengthBonus = 14, DexterityBonus = 12, CriticalChanceBonus = 20, MagicResistance = 20, IsUnique = true });
        // Ultimate
        AddEquipment(new Equipment { Id = id++, Name = "Spear of Destiny", Slot = EquipmentSlot.MainHand, Handedness = WeaponHandedness.TwoHanded, WeaponType = WeaponType.Polearm, WeaponPower = 76, Value = 2800000, Rarity = EquipmentRarity.Artifact, StrengthBonus = 18, DexterityBonus = 15, CriticalChanceBonus = 25, MaxHPBonus = 100, CharismaBonus = 8, IsUnique = true });

        // ===========================================
        // MAULS (Heavy crushing weapons)
        // ===========================================
        AddEquipment(new Equipment { Id = id++, Name = "Sledgehammer", Slot = EquipmentSlot.MainHand, Handedness = WeaponHandedness.TwoHanded, WeaponType = WeaponType.Maul, WeaponPower = 14, Value = 600, Rarity = EquipmentRarity.Common, StrengthBonus = 1 });
        AddEquipment(new Equipment { Id = id++, Name = "War Maul", Slot = EquipmentSlot.MainHand, Handedness = WeaponHandedness.TwoHanded, WeaponType = WeaponType.Maul, WeaponPower = 22, Value = 3500, Rarity = EquipmentRarity.Uncommon, StrengthBonus = 3 });
        AddEquipment(new Equipment { Id = id++, Name = "Crusher", Slot = EquipmentSlot.MainHand, Handedness = WeaponHandedness.TwoHanded, WeaponType = WeaponType.Maul, WeaponPower = 32, Value = 25000, Rarity = EquipmentRarity.Rare, StrengthBonus = 5, ConstitutionBonus = 3 });
        AddEquipment(new Equipment { Id = id++, Name = "Earthshaker", Slot = EquipmentSlot.MainHand, Handedness = WeaponHandedness.TwoHanded, WeaponType = WeaponType.Maul, WeaponPower = 44, Value = 100000, Rarity = EquipmentRarity.Epic, StrengthBonus = 8, ConstitutionBonus = 5, MaxHPBonus = 40 });
        AddEquipment(new Equipment { Id = id++, Name = "Skullbreaker", Slot = EquipmentSlot.MainHand, Handedness = WeaponHandedness.TwoHanded, WeaponType = WeaponType.Maul, WeaponPower = 54, Value = 280000, Rarity = EquipmentRarity.Legendary, StrengthBonus = 10, ConstitutionBonus = 8, MaxHPBonus = 60, CriticalDamageBonus = 50 });
        AddEquipment(new Equipment { Id = id++, Name = "Demolisher", Slot = EquipmentSlot.MainHand, Handedness = WeaponHandedness.TwoHanded, WeaponType = WeaponType.Maul, WeaponPower = 64, Value = 650000, Rarity = EquipmentRarity.Artifact, StrengthBonus = 14, ConstitutionBonus = 10, MaxHPBonus = 100, CriticalDamageBonus = 70 });
        AddEquipment(new Equipment { Id = id++, Name = "Worldbreaker", Slot = EquipmentSlot.MainHand, Handedness = WeaponHandedness.TwoHanded, WeaponType = WeaponType.Maul, WeaponPower = 75, Value = 1600000, Rarity = EquipmentRarity.Artifact, StrengthBonus = 18, ConstitutionBonus = 14, MaxHPBonus = 150, CriticalDamageBonus = 100, IsUnique = true });
    }

    #endregion

    #region Shields (25+ items - full level 1-100 progression)

    private static void InitializeShields()
    {
        int id = ShieldStart;

        // ===========================================
        // BUCKLERS (Light shields - high block chance, low defense)
        // ===========================================
        AddEquipment(new Equipment { Id = id++, Name = "Wooden Buckler", Slot = EquipmentSlot.OffHand, Handedness = WeaponHandedness.OffHandOnly, WeaponType = WeaponType.Buckler, ShieldBonus = 2, BlockChance = 10, Value = 25, Rarity = EquipmentRarity.Common });
        AddEquipment(new Equipment { Id = id++, Name = "Iron Buckler", Slot = EquipmentSlot.OffHand, Handedness = WeaponHandedness.OffHandOnly, WeaponType = WeaponType.Buckler, ShieldBonus = 5, BlockChance = 15, Value = 200, Rarity = EquipmentRarity.Common });
        AddEquipment(new Equipment { Id = id++, Name = "Steel Buckler", Slot = EquipmentSlot.OffHand, Handedness = WeaponHandedness.OffHandOnly, WeaponType = WeaponType.Buckler, ShieldBonus = 8, BlockChance = 18, Value = 800, Rarity = EquipmentRarity.Uncommon, DexterityBonus = 2 });
        AddEquipment(new Equipment { Id = id++, Name = "Duelist's Buckler", Slot = EquipmentSlot.OffHand, Handedness = WeaponHandedness.OffHandOnly, WeaponType = WeaponType.Buckler, ShieldBonus = 12, BlockChance = 22, Value = 3500, Rarity = EquipmentRarity.Rare, DexterityBonus = 4, CriticalChanceBonus = 5 });
        AddEquipment(new Equipment { Id = id++, Name = "Elven Buckler", Slot = EquipmentSlot.OffHand, Handedness = WeaponHandedness.OffHandOnly, WeaponType = WeaponType.Buckler, ShieldBonus = 18, BlockChance = 28, Value = 25000, Rarity = EquipmentRarity.Epic, DexterityBonus = 6, AgilityBonus = 6 });
        AddEquipment(new Equipment { Id = id++, Name = "Phantom Buckler", Slot = EquipmentSlot.OffHand, Handedness = WeaponHandedness.OffHandOnly, WeaponType = WeaponType.Buckler, ShieldBonus = 25, BlockChance = 35, Value = 120000, Rarity = EquipmentRarity.Legendary, DexterityBonus = 10, AgilityBonus = 10, CriticalChanceBonus = 10 });

        // ===========================================
        // STANDARD SHIELDS (Balanced protection)
        // ===========================================
        AddEquipment(new Equipment { Id = id++, Name = "Leather Shield", Slot = EquipmentSlot.OffHand, Handedness = WeaponHandedness.OffHandOnly, WeaponType = WeaponType.Shield, ShieldBonus = 4, BlockChance = 12, Value = 75, Rarity = EquipmentRarity.Common });
        AddEquipment(new Equipment { Id = id++, Name = "Wooden Shield", Slot = EquipmentSlot.OffHand, Handedness = WeaponHandedness.OffHandOnly, WeaponType = WeaponType.Shield, ShieldBonus = 5, BlockChance = 15, Value = 150, Rarity = EquipmentRarity.Common });
        AddEquipment(new Equipment { Id = id++, Name = "Iron Shield", Slot = EquipmentSlot.OffHand, Handedness = WeaponHandedness.OffHandOnly, WeaponType = WeaponType.Shield, ShieldBonus = 7, BlockChance = 18, Value = 400, Rarity = EquipmentRarity.Common });
        AddEquipment(new Equipment { Id = id++, Name = "Steel Shield", Slot = EquipmentSlot.OffHand, Handedness = WeaponHandedness.OffHandOnly, WeaponType = WeaponType.Shield, ShieldBonus = 10, BlockChance = 20, Value = 1000, Rarity = EquipmentRarity.Uncommon });
        AddEquipment(new Equipment { Id = id++, Name = "Knight's Shield", Slot = EquipmentSlot.OffHand, Handedness = WeaponHandedness.OffHandOnly, WeaponType = WeaponType.Shield, ShieldBonus = 13, BlockChance = 22, Value = 2500, Rarity = EquipmentRarity.Uncommon, DefenceBonus = 2 });
        AddEquipment(new Equipment { Id = id++, Name = "Heater Shield", Slot = EquipmentSlot.OffHand, Handedness = WeaponHandedness.OffHandOnly, WeaponType = WeaponType.Shield, ShieldBonus = 16, BlockChance = 25, Value = 5000, Rarity = EquipmentRarity.Rare, DefenceBonus = 3 });
        AddEquipment(new Equipment { Id = id++, Name = "Paladin's Shield", Slot = EquipmentSlot.OffHand, Handedness = WeaponHandedness.OffHandOnly, WeaponType = WeaponType.Shield, ShieldBonus = 18, BlockChance = 25, Value = 15000, Rarity = EquipmentRarity.Epic, DefenceBonus = 4, MagicResistance = 10, RequiresGood = true });
        AddEquipment(new Equipment { Id = id++, Name = "Demon Shield", Slot = EquipmentSlot.OffHand, Handedness = WeaponHandedness.OffHandOnly, WeaponType = WeaponType.Shield, ShieldBonus = 20, BlockChance = 25, Value = 18000, Rarity = EquipmentRarity.Epic, DefenceBonus = 5, LifeSteal = 3, RequiresEvil = true });
        AddEquipment(new Equipment { Id = id++, Name = "Dragon Scale Shield", Slot = EquipmentSlot.OffHand, Handedness = WeaponHandedness.OffHandOnly, WeaponType = WeaponType.Shield, ShieldBonus = 25, BlockChance = 28, Value = 50000, Rarity = EquipmentRarity.Epic, DefenceBonus = 6, MagicResistance = 15 });
        AddEquipment(new Equipment { Id = id++, Name = "Aegis", Slot = EquipmentSlot.OffHand, Handedness = WeaponHandedness.OffHandOnly, WeaponType = WeaponType.Shield, ShieldBonus = 30, BlockChance = 35, Value = 120000, Rarity = EquipmentRarity.Legendary, DefenceBonus = 8, MagicResistance = 25, MaxHPBonus = 30, IsUnique = true });
        AddEquipment(new Equipment { Id = id++, Name = "Bulwark of Shadows", Slot = EquipmentSlot.OffHand, Handedness = WeaponHandedness.OffHandOnly, WeaponType = WeaponType.Shield, ShieldBonus = 28, BlockChance = 30, Value = 180000, Rarity = EquipmentRarity.Legendary, DefenceBonus = 8, LifeSteal = 8, RequiresEvil = true, IsUnique = true });
        AddEquipment(new Equipment { Id = id++, Name = "Wyrm Scale Aegis", Slot = EquipmentSlot.OffHand, Handedness = WeaponHandedness.OffHandOnly, WeaponType = WeaponType.Shield, ShieldBonus = 35, BlockChance = 38, Value = 280000, Rarity = EquipmentRarity.Legendary, DefenceBonus = 10, MagicResistance = 30, MaxHPBonus = 50 });
        AddEquipment(new Equipment { Id = id++, Name = "Celestial Barrier", Slot = EquipmentSlot.OffHand, Handedness = WeaponHandedness.OffHandOnly, WeaponType = WeaponType.Shield, ShieldBonus = 40, BlockChance = 42, Value = 450000, Rarity = EquipmentRarity.Artifact, DefenceBonus = 12, MagicResistance = 35, MaxHPBonus = 80, RequiresGood = true });
        AddEquipment(new Equipment { Id = id++, Name = "Voidguard", Slot = EquipmentSlot.OffHand, Handedness = WeaponHandedness.OffHandOnly, WeaponType = WeaponType.Shield, ShieldBonus = 42, BlockChance = 40, Value = 500000, Rarity = EquipmentRarity.Artifact, DefenceBonus = 14, LifeSteal = 12, MaxHPBonus = 60, RequiresEvil = true });
        AddEquipment(new Equipment { Id = id++, Name = "Shield of the Ancients", Slot = EquipmentSlot.OffHand, Handedness = WeaponHandedness.OffHandOnly, WeaponType = WeaponType.Shield, ShieldBonus = 50, BlockChance = 50, Value = 800000, Rarity = EquipmentRarity.Artifact, DefenceBonus = 15, MagicResistance = 40, MaxHPBonus = 100, IsUnique = true });

        // ===========================================
        // TOWER SHIELDS (Maximum protection, strength required)
        // ===========================================
        AddEquipment(new Equipment { Id = id++, Name = "Tower Shield", Slot = EquipmentSlot.OffHand, Handedness = WeaponHandedness.OffHandOnly, WeaponType = WeaponType.TowerShield, ShieldBonus = 22, BlockChance = 30, Value = 12000, Rarity = EquipmentRarity.Rare, DefenceBonus = 5, StrengthRequired = 15 });
        AddEquipment(new Equipment { Id = id++, Name = "Fortress Shield", Slot = EquipmentSlot.OffHand, Handedness = WeaponHandedness.OffHandOnly, WeaponType = WeaponType.TowerShield, ShieldBonus = 28, BlockChance = 35, Value = 45000, Rarity = EquipmentRarity.Epic, DefenceBonus = 8, MaxHPBonus = 30, StrengthRequired = 20 });
        AddEquipment(new Equipment { Id = id++, Name = "Wall of Faith", Slot = EquipmentSlot.OffHand, Handedness = WeaponHandedness.OffHandOnly, WeaponType = WeaponType.TowerShield, ShieldBonus = 35, BlockChance = 40, Value = 200000, Rarity = EquipmentRarity.Legendary, DefenceBonus = 10, MagicResistance = 30, RequiresGood = true, IsUnique = true });
        AddEquipment(new Equipment { Id = id++, Name = "Rampart of Damnation", Slot = EquipmentSlot.OffHand, Handedness = WeaponHandedness.OffHandOnly, WeaponType = WeaponType.TowerShield, ShieldBonus = 38, BlockChance = 42, Value = 350000, Rarity = EquipmentRarity.Legendary, DefenceBonus = 12, LifeSteal = 10, MaxHPBonus = 60, RequiresEvil = true });
        AddEquipment(new Equipment { Id = id++, Name = "Titan's Bulwark", Slot = EquipmentSlot.OffHand, Handedness = WeaponHandedness.OffHandOnly, WeaponType = WeaponType.TowerShield, ShieldBonus = 45, BlockChance = 48, Value = 600000, Rarity = EquipmentRarity.Artifact, DefenceBonus = 15, MaxHPBonus = 120, ConstitutionBonus = 10, StrengthRequired = 30 });
        AddEquipment(new Equipment { Id = id++, Name = "Wall of Eternity", Slot = EquipmentSlot.OffHand, Handedness = WeaponHandedness.OffHandOnly, WeaponType = WeaponType.TowerShield, ShieldBonus = 55, BlockChance = 55, Value = 1200000, Rarity = EquipmentRarity.Artifact, DefenceBonus = 20, MagicResistance = 45, MaxHPBonus = 150, ConstitutionBonus = 15, IsUnique = true });
    }

    #endregion

    #region Head Armor (35+ items - full level 1-100 progression)

    private static void InitializeHeadArmor()
    {
        int id = HeadArmorStart;

        // ===========================================
        // CLOTH/LEATHER (Light armor - for mages/rogues)
        // ===========================================
        AddEquipment(Equipment.CreateArmor(id++, "Cloth Cap", EquipmentSlot.Head, ArmorType.Cloth, 1, 15));
        AddEquipment(Equipment.CreateArmor(id++, "Leather Hood", EquipmentSlot.Head, ArmorType.Leather, 2, 50));
        AddEquipment(Equipment.CreateArmor(id++, "Leather Cap", EquipmentSlot.Head, ArmorType.Leather, 3, 100));
        AddEquipment(Equipment.CreateArmor(id++, "Studded Leather Cap", EquipmentSlot.Head, ArmorType.Leather, 4, 200));
        AddEquipment(Equipment.CreateArmor(id++, "Thief's Cowl", EquipmentSlot.Head, ArmorType.Leather, 5, 600, EquipmentRarity.Uncommon).WithDexterity(2).WithCritChance(3));
        AddEquipment(Equipment.CreateArmor(id++, "Mage's Hood", EquipmentSlot.Head, ArmorType.Cloth, 6, 8000, EquipmentRarity.Rare).WithMaxMana(30).WithIntelligence(3));
        AddEquipment(Equipment.CreateArmor(id++, "Shadow Cowl", EquipmentSlot.Head, ArmorType.Leather, 8, 15000, EquipmentRarity.Epic).WithDexterity(5).WithCritChance(8).RequiresEvilAlignment());
        AddEquipment(Equipment.CreateArmor(id++, "Archmage Crown", EquipmentSlot.Head, ArmorType.Magic, 10, 40000, EquipmentRarity.Epic).WithMaxMana(80).WithIntelligence(5).WithWisdom(5));
        AddEquipment(Equipment.CreateArmor(id++, "Nightstalker Hood", EquipmentSlot.Head, ArmorType.Magic, 12, 65000, EquipmentRarity.Legendary).WithDexterity(8).WithCritChance(12).WithAgility(5));
        AddEquipment(Equipment.CreateArmor(id++, "Crown of the Void Mage", EquipmentSlot.Head, ArmorType.Magic, 15, 180000, EquipmentRarity.Legendary).WithMaxMana(150).WithIntelligence(10).WithWisdom(8).RequiresEvilAlignment());
        AddEquipment(Equipment.CreateArmor(id++, "Crown of Celestial Light", EquipmentSlot.Head, ArmorType.Magic, 15, 200000, EquipmentRarity.Legendary).WithMaxMana(150).WithWisdom(12).WithMagicResist(20).RequiresGoodAlignment());

        // ===========================================
        // CHAIN/SCALE (Medium armor)
        // ===========================================
        AddEquipment(Equipment.CreateArmor(id++, "Chain Coif", EquipmentSlot.Head, ArmorType.Chain, 5, 400, EquipmentRarity.Uncommon));
        AddEquipment(Equipment.CreateArmor(id++, "Scale Coif", EquipmentSlot.Head, ArmorType.Scale, 7, 1000, EquipmentRarity.Uncommon));
        AddEquipment(Equipment.CreateArmor(id++, "Reinforced Coif", EquipmentSlot.Head, ArmorType.Chain, 9, 2500, EquipmentRarity.Rare));
        AddEquipment(Equipment.CreateArmor(id++, "Serpent Scale Hood", EquipmentSlot.Head, ArmorType.Scale, 13, 18000, EquipmentRarity.Epic).WithMagicResist(10).WithDexterity(3));

        // ===========================================
        // PLATE (Heavy armor - for warriors)
        // ===========================================
        AddEquipment(Equipment.CreateArmor(id++, "Iron Helm", EquipmentSlot.Head, ArmorType.Plate, 6, 750, EquipmentRarity.Uncommon));
        AddEquipment(Equipment.CreateArmor(id++, "Steel Helm", EquipmentSlot.Head, ArmorType.Plate, 8, 1500, EquipmentRarity.Uncommon));
        AddEquipment(Equipment.CreateArmor(id++, "Great Helm", EquipmentSlot.Head, ArmorType.Plate, 10, 3000, EquipmentRarity.Rare));
        AddEquipment(Equipment.CreateArmor(id++, "Knight's Helm", EquipmentSlot.Head, ArmorType.Plate, 12, 6000, EquipmentRarity.Rare));
        AddEquipment(Equipment.CreateArmor(id++, "Barbute", EquipmentSlot.Head, ArmorType.Plate, 14, 12000, EquipmentRarity.Rare));
        AddEquipment(Equipment.CreateArmor(id++, "Crusader's Helm", EquipmentSlot.Head, ArmorType.Plate, 16, 25000, EquipmentRarity.Epic).WithWisdom(2).RequiresGoodAlignment());
        AddEquipment(Equipment.CreateArmor(id++, "Demon Visage", EquipmentSlot.Head, ArmorType.Plate, 17, 28000, EquipmentRarity.Epic).WithStrength(3).RequiresEvilAlignment());
        AddEquipment(Equipment.CreateArmor(id++, "Dragon Helm", EquipmentSlot.Head, ArmorType.Plate, 20, 60000, EquipmentRarity.Epic).WithStrength(4).WithMagicResist(15));
        AddEquipment(Equipment.CreateArmor(id++, "Warlord's Helm", EquipmentSlot.Head, ArmorType.Plate, 22, 85000, EquipmentRarity.Epic).WithStrength(5).WithConstitution(3));
        AddEquipment(Equipment.CreateArmor(id++, "Crown of Valor", EquipmentSlot.Head, ArmorType.Plate, 24, 120000, EquipmentRarity.Legendary).WithStrength(5).WithCharisma(5).WithMaxHP(30));
        AddEquipment(Equipment.CreateArmor(id++, "Elder Dragon Helm", EquipmentSlot.Head, ArmorType.Plate, 26, 180000, EquipmentRarity.Legendary).WithStrength(6).WithMagicResist(25).WithMaxHP(40));
        AddEquipment(Equipment.CreateArmor(id++, "Crown of Shadows", EquipmentSlot.Head, ArmorType.Magic, 25, 200000, EquipmentRarity.Legendary).WithDexterity(8).WithCritChance(15).WithCritDamage(30).RequiresEvilAlignment());
        AddEquipment(Equipment.CreateArmor(id++, "Helm of the Conqueror", EquipmentSlot.Head, ArmorType.Plate, 28, 280000, EquipmentRarity.Legendary).WithStrength(8).WithConstitution(6).WithMaxHP(60));

        // ===========================================
        // ARTIFACT (Ultimate helms)
        // ===========================================
        AddEquipment(Equipment.CreateArmor(id++, "Divine Halo", EquipmentSlot.Head, ArmorType.Magic, 26, 220000, EquipmentRarity.Legendary).WithWisdom(10).WithMagicResist(30).RequiresGoodAlignment().AsUnique());
        AddEquipment(Equipment.CreateArmor(id++, "Helm of Annihilation", EquipmentSlot.Head, ArmorType.Artifact, 30, 400000, EquipmentRarity.Artifact).WithStrength(10).WithCritDamage(40).RequiresEvilAlignment());
        AddEquipment(Equipment.CreateArmor(id++, "Crown of the Ancients", EquipmentSlot.Head, ArmorType.Artifact, 32, 550000, EquipmentRarity.Artifact).WithStrength(8).WithIntelligence(8).WithWisdom(8).WithMaxHP(80).WithMaxMana(80).AsUnique());
        AddEquipment(Equipment.CreateArmor(id++, "Helm of the Titans", EquipmentSlot.Head, ArmorType.Artifact, 35, 750000, EquipmentRarity.Artifact).WithStrength(12).WithConstitution(12).WithMaxHP(120).AsUnique());
        AddEquipment(Equipment.CreateArmor(id++, "Crown of Omniscience", EquipmentSlot.Head, ArmorType.Artifact, 35, 900000, EquipmentRarity.Artifact).WithIntelligence(15).WithWisdom(15).WithMaxMana(200).WithMagicResist(40).AsUnique());
        AddEquipment(Equipment.CreateArmor(id++, "Crown of Eternity", EquipmentSlot.Head, ArmorType.Artifact, 40, 1500000, EquipmentRarity.Artifact).WithStrength(15).WithConstitution(15).WithWisdom(10).WithMaxHP(150).WithMaxMana(100).AsUnique());
    }

    #endregion

    #region Body Armor (50 items - keeping existing progression)

    private static void InitializeBodyArmor()
    {
        int id = BodyArmorStart;

        // Basic armors (AC 0-5)
        AddEquipment(Equipment.CreateArmor(id++, "Bare Skin", EquipmentSlot.Body, ArmorType.Cloth, 0, 0));
        AddEquipment(Equipment.CreateArmor(id++, "Cloth Rags", EquipmentSlot.Body, ArmorType.Cloth, 1, 25));
        AddEquipment(Equipment.CreateArmor(id++, "Padded Cloth", EquipmentSlot.Body, ArmorType.Cloth, 2, 50));
        AddEquipment(Equipment.CreateArmor(id++, "Leather Tunic", EquipmentSlot.Body, ArmorType.Leather, 3, 100));
        AddEquipment(Equipment.CreateArmor(id++, "Hardened Leather", EquipmentSlot.Body, ArmorType.Leather, 4, 200));
        AddEquipment(Equipment.CreateArmor(id++, "Studded Leather", EquipmentSlot.Body, ArmorType.Leather, 5, 400));

        // Light mail (AC 6-10)
        AddEquipment(Equipment.CreateArmor(id++, "Ring Mail", EquipmentSlot.Body, ArmorType.Chain, 6, 750));
        AddEquipment(Equipment.CreateArmor(id++, "Scale Mail", EquipmentSlot.Body, ArmorType.Scale, 7, 1200));
        AddEquipment(Equipment.CreateArmor(id++, "Chain Shirt", EquipmentSlot.Body, ArmorType.Chain, 8, 2000, EquipmentRarity.Uncommon));
        AddEquipment(Equipment.CreateArmor(id++, "Chain Mail", EquipmentSlot.Body, ArmorType.Chain, 9, 3200, EquipmentRarity.Uncommon));
        AddEquipment(Equipment.CreateArmor(id++, "Reinforced Chain", EquipmentSlot.Body, ArmorType.Chain, 10, 5000, EquipmentRarity.Uncommon));

        // Medium armor (AC 11-15)
        AddEquipment(Equipment.CreateArmor(id++, "Splint Mail", EquipmentSlot.Body, ArmorType.Scale, 11, 7500, EquipmentRarity.Uncommon));
        AddEquipment(Equipment.CreateArmor(id++, "Banded Mail", EquipmentSlot.Body, ArmorType.Scale, 12, 11000, EquipmentRarity.Uncommon));
        AddEquipment(Equipment.CreateArmor(id++, "Bronze Plate", EquipmentSlot.Body, ArmorType.Plate, 13, 16000, EquipmentRarity.Rare));
        AddEquipment(Equipment.CreateArmor(id++, "Steel Plate", EquipmentSlot.Body, ArmorType.Plate, 14, 22000, EquipmentRarity.Rare));
        AddEquipment(Equipment.CreateArmor(id++, "Plate Mail", EquipmentSlot.Body, ArmorType.Plate, 15, 30000, EquipmentRarity.Rare));

        // Heavy armor (AC 16-20)
        AddEquipment(Equipment.CreateArmor(id++, "Field Plate", EquipmentSlot.Body, ArmorType.Plate, 16, 40000, EquipmentRarity.Rare));
        AddEquipment(Equipment.CreateArmor(id++, "Full Plate", EquipmentSlot.Body, ArmorType.Plate, 17, 52000, EquipmentRarity.Rare));
        AddEquipment(Equipment.CreateArmor(id++, "Master Plate", EquipmentSlot.Body, ArmorType.Plate, 18, 66000, EquipmentRarity.Epic));
        AddEquipment(Equipment.CreateArmor(id++, "Knight's Plate", EquipmentSlot.Body, ArmorType.Plate, 19, 82000, EquipmentRarity.Epic));
        AddEquipment(Equipment.CreateArmor(id++, "Royal Plate", EquipmentSlot.Body, ArmorType.Plate, 20, 100000, EquipmentRarity.Epic));

        // Enchanted armor (AC 21-25)
        AddEquipment(Equipment.CreateArmor(id++, "Plate of Valor", EquipmentSlot.Body, ArmorType.Magic, 21, 125000, EquipmentRarity.Epic).WithStrength(2));
        AddEquipment(Equipment.CreateArmor(id++, "Blessed Plate", EquipmentSlot.Body, ArmorType.Magic, 22, 155000, EquipmentRarity.Epic).WithWisdom(2).RequiresGoodAlignment());
        AddEquipment(Equipment.CreateArmor(id++, "Plate of Honor", EquipmentSlot.Body, ArmorType.Magic, 23, 190000, EquipmentRarity.Epic).WithCharisma(3));
        AddEquipment(Equipment.CreateArmor(id++, "Sacred Plate", EquipmentSlot.Body, ArmorType.Magic, 24, 230000, EquipmentRarity.Epic).WithMaxHP(30).RequiresGoodAlignment());
        AddEquipment(Equipment.CreateArmor(id++, "Holy Plate", EquipmentSlot.Body, ArmorType.Magic, 25, 275000, EquipmentRarity.Epic).WithMagicResist(15).RequiresGoodAlignment());

        // Dark armors (AC 26-30)
        AddEquipment(Equipment.CreateArmor(id++, "Shadow Plate", EquipmentSlot.Body, ArmorType.Magic, 26, 330000, EquipmentRarity.Epic).WithDexterity(3).RequiresEvilAlignment());
        AddEquipment(Equipment.CreateArmor(id++, "Demon Plate", EquipmentSlot.Body, ArmorType.Magic, 27, 390000, EquipmentRarity.Epic).WithStrength(4).RequiresEvilAlignment());
        AddEquipment(Equipment.CreateArmor(id++, "Cursed Plate", EquipmentSlot.Body, ArmorType.Magic, 28, 460000, EquipmentRarity.Epic).WithLifeSteal(5).RequiresEvilAlignment());
        AddEquipment(Equipment.CreateArmor(id++, "Infernal Plate", EquipmentSlot.Body, ArmorType.Magic, 29, 540000, EquipmentRarity.Legendary).WithStrength(5).WithLifeSteal(8).RequiresEvilAlignment());
        AddEquipment(Equipment.CreateArmor(id++, "Abyssal Armor", EquipmentSlot.Body, ArmorType.Magic, 30, 630000, EquipmentRarity.Legendary).WithStrength(6).WithMaxHP(50).RequiresEvilAlignment());

        // Dragon armors (AC 31-35)
        AddEquipment(Equipment.CreateArmor(id++, "Dragon Scale", EquipmentSlot.Body, ArmorType.Magic, 31, 730000, EquipmentRarity.Legendary).WithMagicResist(20));
        AddEquipment(Equipment.CreateArmor(id++, "Wyrm Scale", EquipmentSlot.Body, ArmorType.Magic, 32, 840000, EquipmentRarity.Legendary).WithMagicResist(22).WithMaxHP(40));
        AddEquipment(Equipment.CreateArmor(id++, "Ancient Dragon Hide", EquipmentSlot.Body, ArmorType.Magic, 33, 960000, EquipmentRarity.Legendary).WithMagicResist(25).WithStrength(5));
        AddEquipment(Equipment.CreateArmor(id++, "Great Wyrm Armor", EquipmentSlot.Body, ArmorType.Magic, 34, 1100000, EquipmentRarity.Legendary).WithMagicResist(28).WithMaxHP(60));
        AddEquipment(Equipment.CreateArmor(id++, "Elder Dragon Plate", EquipmentSlot.Body, ArmorType.Magic, 35, 1250000, EquipmentRarity.Legendary).WithMagicResist(30).WithStrength(6).WithMaxHP(80));

        // Celestial armors (AC 36-40)
        AddEquipment(Equipment.CreateArmor(id++, "Celestial Armor", EquipmentSlot.Body, ArmorType.Artifact, 36, 1420000, EquipmentRarity.Legendary).WithWisdom(5).WithMaxMana(50).RequiresGoodAlignment());
        AddEquipment(Equipment.CreateArmor(id++, "Heavenly Plate", EquipmentSlot.Body, ArmorType.Artifact, 37, 1600000, EquipmentRarity.Legendary).WithWisdom(6).WithMagicResist(30).RequiresGoodAlignment());
        AddEquipment(Equipment.CreateArmor(id++, "Angelic Armor", EquipmentSlot.Body, ArmorType.Artifact, 38, 1800000, EquipmentRarity.Legendary).WithWisdom(7).WithMaxHP(80).RequiresGoodAlignment());
        AddEquipment(Equipment.CreateArmor(id++, "Seraphim Plate", EquipmentSlot.Body, ArmorType.Artifact, 39, 2050000, EquipmentRarity.Artifact).WithWisdom(8).WithMaxMana(100).WithMagicResist(35).RequiresGoodAlignment());
        AddEquipment(Equipment.CreateArmor(id++, "Divine Protection", EquipmentSlot.Body, ArmorType.Artifact, 40, 2300000, EquipmentRarity.Artifact).WithWisdom(10).WithMaxHP(100).WithMagicResist(40).RequiresGoodAlignment().AsUnique());

        // Legendary armors (AC 41-45)
        AddEquipment(Equipment.CreateArmor(id++, "Titan Armor", EquipmentSlot.Body, ArmorType.Artifact, 41, 2600000, EquipmentRarity.Artifact).WithStrength(8).WithConstitution(8).WithMaxHP(100));
        AddEquipment(Equipment.CreateArmor(id++, "Godforged Plate", EquipmentSlot.Body, ArmorType.Artifact, 42, 2950000, EquipmentRarity.Artifact).WithStrength(10).WithMaxHP(120));
        AddEquipment(Equipment.CreateArmor(id++, "Eternal Guardian", EquipmentSlot.Body, ArmorType.Artifact, 43, 3350000, EquipmentRarity.Artifact).WithConstitution(10).WithMaxHP(150).WithMagicResist(40));
        AddEquipment(Equipment.CreateArmor(id++, "Mythril Armor", EquipmentSlot.Body, ArmorType.Artifact, 44, 3800000, EquipmentRarity.Artifact).WithDexterity(8).WithAgility(10).WithMaxHP(80));
        AddEquipment(Equipment.CreateArmor(id++, "Adamantine Plate", EquipmentSlot.Body, ArmorType.Artifact, 45, 4300000, EquipmentRarity.Artifact).WithStrength(12).WithMaxHP(150).WithDefence(10));

        // Ultimate armors (AC 46-50)
        AddEquipment(Equipment.CreateArmor(id++, "Supreme Protection", EquipmentSlot.Body, ArmorType.Artifact, 46, 4900000, EquipmentRarity.Artifact).WithStrength(10).WithConstitution(10).WithMaxHP(200));
        AddEquipment(Equipment.CreateArmor(id++, "Armor of the Ancients", EquipmentSlot.Body, ArmorType.Artifact, 47, 5600000, EquipmentRarity.Artifact).WithStrength(12).WithWisdom(12).WithMaxHP(200).WithMaxMana(100).AsUnique());
        AddEquipment(Equipment.CreateArmor(id++, "Immortal's Shell", EquipmentSlot.Body, ArmorType.Artifact, 48, 6400000, EquipmentRarity.Artifact).WithConstitution(15).WithMaxHP(300).WithMagicResist(50).AsUnique());
        AddEquipment(Equipment.CreateArmor(id++, "Armor of Eternity", EquipmentSlot.Body, ArmorType.Artifact, 49, 7300000, EquipmentRarity.Artifact).WithStrength(15).WithConstitution(15).WithMaxHP(350).AsUnique());
        AddEquipment(Equipment.CreateArmor(id++, "Ultimate Defense", EquipmentSlot.Body, ArmorType.Artifact, 50, 8500000, EquipmentRarity.Artifact).WithStrength(20).WithConstitution(20).WithMaxHP(500).WithMagicResist(50).WithDefence(20).AsUnique());
    }

    #endregion

    #region Arms Armor (35+ items - full level 1-100 progression)

    private static void InitializeArmsArmor()
    {
        int id = ArmsArmorStart;

        // ===========================================
        // CLOTH/LEATHER (Light armor)
        // ===========================================
        AddEquipment(Equipment.CreateArmor(id++, "Cloth Wraps", EquipmentSlot.Arms, ArmorType.Cloth, 1, 10));
        AddEquipment(Equipment.CreateArmor(id++, "Leather Bracers", EquipmentSlot.Arms, ArmorType.Leather, 2, 40));
        AddEquipment(Equipment.CreateArmor(id++, "Studded Bracers", EquipmentSlot.Arms, ArmorType.Leather, 3, 100));
        AddEquipment(Equipment.CreateArmor(id++, "Thief's Arm Guards", EquipmentSlot.Arms, ArmorType.Leather, 4, 350, EquipmentRarity.Uncommon).WithDexterity(2));
        AddEquipment(Equipment.CreateArmor(id++, "Mage's Sleeves", EquipmentSlot.Arms, ArmorType.Cloth, 4, 3000, EquipmentRarity.Rare).WithMaxMana(20).WithIntelligence(2));
        AddEquipment(Equipment.CreateArmor(id++, "Shadowweave Bracers", EquipmentSlot.Arms, ArmorType.Leather, 7, 8000, EquipmentRarity.Epic).WithDexterity(5).WithCritChance(5).RequiresEvilAlignment());
        AddEquipment(Equipment.CreateArmor(id++, "Archmage Sleeves", EquipmentSlot.Arms, ArmorType.Magic, 10, 45000, EquipmentRarity.Legendary).WithMaxMana(60).WithIntelligence(5).WithWisdom(3));
        AddEquipment(Equipment.CreateArmor(id++, "Nightblade Bracers", EquipmentSlot.Arms, ArmorType.Magic, 12, 80000, EquipmentRarity.Legendary).WithDexterity(8).WithCritChance(10).WithCritDamage(20));
        AddEquipment(Equipment.CreateArmor(id++, "Voidweave Sleeves", EquipmentSlot.Arms, ArmorType.Magic, 15, 180000, EquipmentRarity.Artifact).WithMaxMana(120).WithIntelligence(10).WithWisdom(8).RequiresEvilAlignment());

        // ===========================================
        // CHAIN/SCALE (Medium armor)
        // ===========================================
        AddEquipment(Equipment.CreateArmor(id++, "Chain Sleeves", EquipmentSlot.Arms, ArmorType.Chain, 4, 250));
        AddEquipment(Equipment.CreateArmor(id++, "Scale Sleeves", EquipmentSlot.Arms, ArmorType.Scale, 6, 700, EquipmentRarity.Uncommon));
        AddEquipment(Equipment.CreateArmor(id++, "Reinforced Chain Arms", EquipmentSlot.Arms, ArmorType.Chain, 8, 2000, EquipmentRarity.Rare).WithStrength(2));
        AddEquipment(Equipment.CreateArmor(id++, "Serpent Scale Arms", EquipmentSlot.Arms, ArmorType.Scale, 11, 12000, EquipmentRarity.Epic).WithMagicResist(8).WithDexterity(3));

        // ===========================================
        // PLATE (Heavy armor)
        // ===========================================
        AddEquipment(Equipment.CreateArmor(id++, "Iron Vambraces", EquipmentSlot.Arms, ArmorType.Plate, 5, 500, EquipmentRarity.Uncommon));
        AddEquipment(Equipment.CreateArmor(id++, "Steel Vambraces", EquipmentSlot.Arms, ArmorType.Plate, 6, 1000, EquipmentRarity.Uncommon));
        AddEquipment(Equipment.CreateArmor(id++, "Knight's Vambraces", EquipmentSlot.Arms, ArmorType.Plate, 8, 2500, EquipmentRarity.Rare));
        AddEquipment(Equipment.CreateArmor(id++, "Blessed Bracers", EquipmentSlot.Arms, ArmorType.Magic, 9, 5000, EquipmentRarity.Rare).WithStrength(2).RequiresGoodAlignment());
        AddEquipment(Equipment.CreateArmor(id++, "Dark Vambraces", EquipmentSlot.Arms, ArmorType.Magic, 9, 5000, EquipmentRarity.Rare).WithStrength(3).RequiresEvilAlignment());
        AddEquipment(Equipment.CreateArmor(id++, "Berserker Bracers", EquipmentSlot.Arms, ArmorType.Plate, 10, 8000, EquipmentRarity.Epic).WithStrength(4).WithCritDamage(15));
        AddEquipment(Equipment.CreateArmor(id++, "Dragon Scale Arms", EquipmentSlot.Arms, ArmorType.Magic, 12, 15000, EquipmentRarity.Epic).WithMagicResist(10));
        AddEquipment(Equipment.CreateArmor(id++, "Celestial Bracers", EquipmentSlot.Arms, ArmorType.Magic, 14, 30000, EquipmentRarity.Epic).WithWisdom(4).RequiresGoodAlignment());
        AddEquipment(Equipment.CreateArmor(id++, "Demon Arms", EquipmentSlot.Arms, ArmorType.Magic, 14, 32000, EquipmentRarity.Epic).WithStrength(5).WithLifeSteal(3).RequiresEvilAlignment());
        AddEquipment(Equipment.CreateArmor(id++, "Vambraces of Power", EquipmentSlot.Arms, ArmorType.Magic, 16, 50000, EquipmentRarity.Legendary).WithStrength(6).WithMaxHP(20));
        AddEquipment(Equipment.CreateArmor(id++, "Elder Dragon Arms", EquipmentSlot.Arms, ArmorType.Magic, 18, 75000, EquipmentRarity.Legendary).WithStrength(6).WithMagicResist(18).WithMaxHP(30));
        AddEquipment(Equipment.CreateArmor(id++, "Warlord's Vambraces", EquipmentSlot.Arms, ArmorType.Plate, 20, 95000, EquipmentRarity.Legendary).WithStrength(8).WithConstitution(5).WithMaxHP(40));
        AddEquipment(Equipment.CreateArmor(id++, "Crusader's Arms", EquipmentSlot.Arms, ArmorType.Magic, 20, 110000, EquipmentRarity.Legendary).WithStrength(6).WithWisdom(6).WithMagicResist(15).RequiresGoodAlignment());
        AddEquipment(Equipment.CreateArmor(id++, "Infernal Vambraces", EquipmentSlot.Arms, ArmorType.Magic, 22, 140000, EquipmentRarity.Legendary).WithStrength(8).WithLifeSteal(6).RequiresEvilAlignment());

        // ===========================================
        // ARTIFACT (Ultimate arms)
        // ===========================================
        AddEquipment(Equipment.CreateArmor(id++, "Arms of the Titan", EquipmentSlot.Arms, ArmorType.Artifact, 24, 180000, EquipmentRarity.Legendary).WithStrength(10).WithMaxHP(50));
        AddEquipment(Equipment.CreateArmor(id++, "Divine Vambraces", EquipmentSlot.Arms, ArmorType.Artifact, 26, 250000, EquipmentRarity.Artifact).WithStrength(8).WithWisdom(8).WithMagicResist(25).RequiresGoodAlignment());
        AddEquipment(Equipment.CreateArmor(id++, "Arms of Damnation", EquipmentSlot.Arms, ArmorType.Artifact, 26, 280000, EquipmentRarity.Artifact).WithStrength(12).WithLifeSteal(10).RequiresEvilAlignment());
        AddEquipment(Equipment.CreateArmor(id++, "Arms of Eternity", EquipmentSlot.Arms, ArmorType.Artifact, 28, 350000, EquipmentRarity.Artifact).WithStrength(12).WithConstitution(8).WithMaxHP(80));
        AddEquipment(Equipment.CreateArmor(id++, "Godforged Vambraces", EquipmentSlot.Arms, ArmorType.Artifact, 32, 500000, EquipmentRarity.Artifact).WithStrength(14).WithMaxHP(100).AsUnique());
        AddEquipment(Equipment.CreateArmor(id++, "Titan's Arms", EquipmentSlot.Arms, ArmorType.Artifact, 35, 750000, EquipmentRarity.Artifact).WithStrength(18).WithConstitution(12).WithMaxHP(120).AsUnique());
    }

    #endregion

    #region Hands Armor (35+ items - full level 1-100 progression)

    private static void InitializeHandsArmor()
    {
        int id = HandsArmorStart;

        // ===========================================
        // CLOTH/LEATHER (Light armor)
        // ===========================================
        AddEquipment(Equipment.CreateArmor(id++, "Cloth Gloves", EquipmentSlot.Hands, ArmorType.Cloth, 1, 10));
        AddEquipment(Equipment.CreateArmor(id++, "Leather Gloves", EquipmentSlot.Hands, ArmorType.Leather, 2, 35));
        AddEquipment(Equipment.CreateArmor(id++, "Studded Gloves", EquipmentSlot.Hands, ArmorType.Leather, 3, 80));
        AddEquipment(Equipment.CreateArmor(id++, "Thief's Gloves", EquipmentSlot.Hands, ArmorType.Leather, 4, 1500, EquipmentRarity.Rare).WithDexterity(3).WithCritChance(5));
        AddEquipment(Equipment.CreateArmor(id++, "Mage's Gloves", EquipmentSlot.Hands, ArmorType.Cloth, 3, 2500, EquipmentRarity.Rare).WithMaxMana(15).WithIntelligence(2));
        AddEquipment(Equipment.CreateArmor(id++, "Shadow Gloves", EquipmentSlot.Hands, ArmorType.Magic, 8, 15000, EquipmentRarity.Epic).WithDexterity(5).WithCritChance(10).RequiresEvilAlignment());
        AddEquipment(Equipment.CreateArmor(id++, "Archmage Gloves", EquipmentSlot.Hands, ArmorType.Magic, 10, 40000, EquipmentRarity.Legendary).WithMaxMana(50).WithIntelligence(5).WithWisdom(3));
        AddEquipment(Equipment.CreateArmor(id++, "Assassin's Touch", EquipmentSlot.Hands, ArmorType.Magic, 12, 75000, EquipmentRarity.Legendary).WithDexterity(8).WithCritChance(15).WithCritDamage(25));
        AddEquipment(Equipment.CreateArmor(id++, "Voidtouch Gloves", EquipmentSlot.Hands, ArmorType.Magic, 15, 160000, EquipmentRarity.Artifact).WithMaxMana(100).WithIntelligence(10).WithCritDamage(30).RequiresEvilAlignment());

        // ===========================================
        // CHAIN/SCALE (Medium armor)
        // ===========================================
        AddEquipment(Equipment.CreateArmor(id++, "Chain Gauntlets", EquipmentSlot.Hands, ArmorType.Chain, 4, 200));
        AddEquipment(Equipment.CreateArmor(id++, "Scale Gauntlets", EquipmentSlot.Hands, ArmorType.Scale, 6, 600, EquipmentRarity.Uncommon));
        AddEquipment(Equipment.CreateArmor(id++, "Reinforced Chain Gloves", EquipmentSlot.Hands, ArmorType.Chain, 8, 1800, EquipmentRarity.Rare).WithStrength(2));
        AddEquipment(Equipment.CreateArmor(id++, "Serpent Scale Gloves", EquipmentSlot.Hands, ArmorType.Scale, 11, 10000, EquipmentRarity.Epic).WithMagicResist(8).WithDexterity(3));

        // ===========================================
        // PLATE (Heavy armor)
        // ===========================================
        AddEquipment(Equipment.CreateArmor(id++, "Iron Gauntlets", EquipmentSlot.Hands, ArmorType.Plate, 5, 400, EquipmentRarity.Uncommon));
        AddEquipment(Equipment.CreateArmor(id++, "Steel Gauntlets", EquipmentSlot.Hands, ArmorType.Plate, 6, 800, EquipmentRarity.Uncommon));
        AddEquipment(Equipment.CreateArmor(id++, "Knight's Gauntlets", EquipmentSlot.Hands, ArmorType.Plate, 8, 2000, EquipmentRarity.Rare));
        AddEquipment(Equipment.CreateArmor(id++, "Blessed Gauntlets", EquipmentSlot.Hands, ArmorType.Magic, 9, 4000, EquipmentRarity.Rare).WithStrength(2).RequiresGoodAlignment());
        AddEquipment(Equipment.CreateArmor(id++, "Spiked Gauntlets", EquipmentSlot.Hands, ArmorType.Plate, 8, 5000, EquipmentRarity.Epic).WithCritDamage(10));
        AddEquipment(Equipment.CreateArmor(id++, "Dragon Gauntlets", EquipmentSlot.Hands, ArmorType.Magic, 12, 12000, EquipmentRarity.Epic).WithMagicResist(8).WithStrength(3));
        AddEquipment(Equipment.CreateArmor(id++, "Celestial Gauntlets", EquipmentSlot.Hands, ArmorType.Magic, 14, 25000, EquipmentRarity.Epic).WithWisdom(4).RequiresGoodAlignment());
        AddEquipment(Equipment.CreateArmor(id++, "Demon Gauntlets", EquipmentSlot.Hands, ArmorType.Magic, 14, 28000, EquipmentRarity.Epic).WithStrength(5).WithLifeSteal(2).RequiresEvilAlignment());
        AddEquipment(Equipment.CreateArmor(id++, "Gauntlets of Power", EquipmentSlot.Hands, ArmorType.Magic, 16, 50000, EquipmentRarity.Legendary).WithStrength(6));
        AddEquipment(Equipment.CreateArmor(id++, "Elder Dragon Gauntlets", EquipmentSlot.Hands, ArmorType.Magic, 18, 70000, EquipmentRarity.Legendary).WithStrength(6).WithMagicResist(15).WithMaxHP(25));
        AddEquipment(Equipment.CreateArmor(id++, "Warlord's Gauntlets", EquipmentSlot.Hands, ArmorType.Plate, 20, 90000, EquipmentRarity.Legendary).WithStrength(8).WithCritDamage(30));
        AddEquipment(Equipment.CreateArmor(id++, "Crusader's Gauntlets", EquipmentSlot.Hands, ArmorType.Magic, 20, 100000, EquipmentRarity.Legendary).WithStrength(5).WithWisdom(5).WithMagicResist(12).RequiresGoodAlignment());
        AddEquipment(Equipment.CreateArmor(id++, "Infernal Gauntlets", EquipmentSlot.Hands, ArmorType.Magic, 22, 130000, EquipmentRarity.Legendary).WithStrength(8).WithLifeSteal(5).RequiresEvilAlignment());

        // ===========================================
        // ARTIFACT (Ultimate hands)
        // ===========================================
        AddEquipment(Equipment.CreateArmor(id++, "Titan's Grasp", EquipmentSlot.Hands, ArmorType.Artifact, 24, 180000, EquipmentRarity.Artifact).WithStrength(10).WithMaxHP(40));
        AddEquipment(Equipment.CreateArmor(id++, "Divine Gauntlets", EquipmentSlot.Hands, ArmorType.Artifact, 26, 240000, EquipmentRarity.Artifact).WithStrength(8).WithWisdom(8).WithMagicResist(20).RequiresGoodAlignment());
        AddEquipment(Equipment.CreateArmor(id++, "Gauntlets of Damnation", EquipmentSlot.Hands, ArmorType.Artifact, 26, 260000, EquipmentRarity.Artifact).WithStrength(12).WithLifeSteal(8).RequiresEvilAlignment());
        AddEquipment(Equipment.CreateArmor(id++, "Godforged Gauntlets", EquipmentSlot.Hands, ArmorType.Artifact, 30, 400000, EquipmentRarity.Artifact).WithStrength(14).WithCritDamage(50).AsUnique());
        AddEquipment(Equipment.CreateArmor(id++, "Hands of Eternity", EquipmentSlot.Hands, ArmorType.Artifact, 35, 700000, EquipmentRarity.Artifact).WithStrength(18).WithDexterity(12).WithMaxHP(100).AsUnique());
    }

    #endregion

    #region Legs Armor (35+ items - full level 1-100 progression)

    private static void InitializeLegsArmor()
    {
        int id = LegsArmorStart;

        // ===========================================
        // CLOTH/LEATHER (Light armor)
        // ===========================================
        AddEquipment(Equipment.CreateArmor(id++, "Cloth Pants", EquipmentSlot.Legs, ArmorType.Cloth, 1, 12));
        AddEquipment(Equipment.CreateArmor(id++, "Leather Leggings", EquipmentSlot.Legs, ArmorType.Leather, 2, 45));
        AddEquipment(Equipment.CreateArmor(id++, "Studded Leggings", EquipmentSlot.Legs, ArmorType.Leather, 3, 100));
        AddEquipment(Equipment.CreateArmor(id++, "Thief's Leggings", EquipmentSlot.Legs, ArmorType.Leather, 4, 400, EquipmentRarity.Uncommon).WithDexterity(2).WithAgility(2));
        AddEquipment(Equipment.CreateArmor(id++, "Mage's Pants", EquipmentSlot.Legs, ArmorType.Cloth, 4, 3000, EquipmentRarity.Rare).WithMaxMana(20).WithIntelligence(2));
        AddEquipment(Equipment.CreateArmor(id++, "Shadow Leggings", EquipmentSlot.Legs, ArmorType.Magic, 8, 14000, EquipmentRarity.Epic).WithDexterity(5).WithAgility(5).RequiresEvilAlignment());
        AddEquipment(Equipment.CreateArmor(id++, "Archmage Leggings", EquipmentSlot.Legs, ArmorType.Magic, 10, 45000, EquipmentRarity.Legendary).WithMaxMana(60).WithIntelligence(5));
        AddEquipment(Equipment.CreateArmor(id++, "Greaves of Swiftness", EquipmentSlot.Legs, ArmorType.Magic, 12, 50000, EquipmentRarity.Legendary).WithDexterity(6).WithAgility(8));
        AddEquipment(Equipment.CreateArmor(id++, "Nightstalker Leggings", EquipmentSlot.Legs, ArmorType.Magic, 14, 85000, EquipmentRarity.Legendary).WithDexterity(10).WithAgility(10).WithCritChance(8));
        AddEquipment(Equipment.CreateArmor(id++, "Voidwalker Leggings", EquipmentSlot.Legs, ArmorType.Magic, 16, 170000, EquipmentRarity.Artifact).WithMaxMana(100).WithIntelligence(10).WithAgility(8).RequiresEvilAlignment());

        // ===========================================
        // CHAIN/SCALE (Medium armor)
        // ===========================================
        AddEquipment(Equipment.CreateArmor(id++, "Chain Leggings", EquipmentSlot.Legs, ArmorType.Chain, 4, 250));
        AddEquipment(Equipment.CreateArmor(id++, "Scale Leggings", EquipmentSlot.Legs, ArmorType.Scale, 6, 700, EquipmentRarity.Uncommon));
        AddEquipment(Equipment.CreateArmor(id++, "Reinforced Chain Legs", EquipmentSlot.Legs, ArmorType.Chain, 8, 2000, EquipmentRarity.Rare).WithConstitution(2));
        AddEquipment(Equipment.CreateArmor(id++, "Serpent Scale Legs", EquipmentSlot.Legs, ArmorType.Scale, 11, 12000, EquipmentRarity.Epic).WithMagicResist(8).WithAgility(3));

        // ===========================================
        // PLATE (Heavy armor)
        // ===========================================
        AddEquipment(Equipment.CreateArmor(id++, "Iron Greaves", EquipmentSlot.Legs, ArmorType.Plate, 5, 500, EquipmentRarity.Uncommon));
        AddEquipment(Equipment.CreateArmor(id++, "Steel Greaves", EquipmentSlot.Legs, ArmorType.Plate, 6, 1000, EquipmentRarity.Uncommon));
        AddEquipment(Equipment.CreateArmor(id++, "Knight's Greaves", EquipmentSlot.Legs, ArmorType.Plate, 8, 2500, EquipmentRarity.Rare));
        AddEquipment(Equipment.CreateArmor(id++, "Blessed Greaves", EquipmentSlot.Legs, ArmorType.Magic, 9, 5000, EquipmentRarity.Rare).WithAgility(2).RequiresGoodAlignment());
        AddEquipment(Equipment.CreateArmor(id++, "Berserker Greaves", EquipmentSlot.Legs, ArmorType.Plate, 10, 8000, EquipmentRarity.Epic).WithStrength(3).WithAgility(3));
        AddEquipment(Equipment.CreateArmor(id++, "Dragon Greaves", EquipmentSlot.Legs, ArmorType.Magic, 12, 15000, EquipmentRarity.Epic).WithMagicResist(10));
        AddEquipment(Equipment.CreateArmor(id++, "Celestial Greaves", EquipmentSlot.Legs, ArmorType.Magic, 14, 30000, EquipmentRarity.Epic).WithWisdom(4).RequiresGoodAlignment());
        AddEquipment(Equipment.CreateArmor(id++, "Demon Greaves", EquipmentSlot.Legs, ArmorType.Magic, 14, 32000, EquipmentRarity.Epic).WithStrength(5).RequiresEvilAlignment());
        AddEquipment(Equipment.CreateArmor(id++, "Elder Dragon Greaves", EquipmentSlot.Legs, ArmorType.Magic, 18, 70000, EquipmentRarity.Legendary).WithStrength(5).WithMagicResist(18).WithMaxHP(30));
        AddEquipment(Equipment.CreateArmor(id++, "Warlord's Greaves", EquipmentSlot.Legs, ArmorType.Plate, 20, 90000, EquipmentRarity.Legendary).WithStrength(8).WithConstitution(5).WithMaxHP(40));
        AddEquipment(Equipment.CreateArmor(id++, "Crusader's Greaves", EquipmentSlot.Legs, ArmorType.Magic, 20, 105000, EquipmentRarity.Legendary).WithStrength(5).WithWisdom(5).WithMagicResist(15).RequiresGoodAlignment());
        AddEquipment(Equipment.CreateArmor(id++, "Infernal Greaves", EquipmentSlot.Legs, ArmorType.Magic, 22, 135000, EquipmentRarity.Legendary).WithStrength(8).WithLifeSteal(5).RequiresEvilAlignment());

        // ===========================================
        // ARTIFACT (Ultimate legs)
        // ===========================================
        AddEquipment(Equipment.CreateArmor(id++, "Titan's Stride", EquipmentSlot.Legs, ArmorType.Artifact, 24, 175000, EquipmentRarity.Legendary).WithStrength(10).WithMaxHP(50));
        AddEquipment(Equipment.CreateArmor(id++, "Divine Greaves", EquipmentSlot.Legs, ArmorType.Artifact, 26, 245000, EquipmentRarity.Artifact).WithStrength(8).WithWisdom(8).WithMagicResist(25).RequiresGoodAlignment());
        AddEquipment(Equipment.CreateArmor(id++, "Greaves of Damnation", EquipmentSlot.Legs, ArmorType.Artifact, 26, 275000, EquipmentRarity.Artifact).WithStrength(12).WithLifeSteal(8).RequiresEvilAlignment());
        AddEquipment(Equipment.CreateArmor(id++, "Eternal Greaves", EquipmentSlot.Legs, ArmorType.Artifact, 28, 350000, EquipmentRarity.Artifact).WithStrength(12).WithConstitution(8).WithMaxHP(80));
        AddEquipment(Equipment.CreateArmor(id++, "Godforged Greaves", EquipmentSlot.Legs, ArmorType.Artifact, 32, 500000, EquipmentRarity.Artifact).WithStrength(14).WithMaxHP(100).AsUnique());
        AddEquipment(Equipment.CreateArmor(id++, "Legs of the Colossus", EquipmentSlot.Legs, ArmorType.Artifact, 35, 750000, EquipmentRarity.Artifact).WithStrength(18).WithConstitution(12).WithMaxHP(120).AsUnique());
    }

    #endregion

    #region Feet Armor (35+ items - full level 1-100 progression)

    private static void InitializeFeetArmor()
    {
        int id = FeetArmorStart;

        // ===========================================
        // CLOTH/LEATHER (Light armor - speed/dex)
        // ===========================================
        AddEquipment(Equipment.CreateArmor(id++, "Sandals", EquipmentSlot.Feet, ArmorType.Cloth, 0, 5));
        AddEquipment(Equipment.CreateArmor(id++, "Cloth Shoes", EquipmentSlot.Feet, ArmorType.Cloth, 1, 15));
        AddEquipment(Equipment.CreateArmor(id++, "Leather Boots", EquipmentSlot.Feet, ArmorType.Leather, 2, 50));
        AddEquipment(Equipment.CreateArmor(id++, "Studded Boots", EquipmentSlot.Feet, ArmorType.Leather, 3, 120));
        AddEquipment(Equipment.CreateArmor(id++, "Thief's Boots", EquipmentSlot.Feet, ArmorType.Leather, 4, 500, EquipmentRarity.Uncommon).WithDexterity(2).WithAgility(3));
        AddEquipment(Equipment.CreateArmor(id++, "Boots of Speed", EquipmentSlot.Feet, ArmorType.Leather, 5, 4000, EquipmentRarity.Rare).WithAgility(5).WithDexterity(2));
        AddEquipment(Equipment.CreateArmor(id++, "Mage's Slippers", EquipmentSlot.Feet, ArmorType.Cloth, 3, 3500, EquipmentRarity.Rare).WithMaxMana(20).WithIntelligence(2));
        AddEquipment(Equipment.CreateArmor(id++, "Shadow Steps", EquipmentSlot.Feet, ArmorType.Magic, 8, 18000, EquipmentRarity.Epic).WithDexterity(6).WithAgility(8).WithCritChance(5).RequiresEvilAlignment());
        AddEquipment(Equipment.CreateArmor(id++, "Boots of the Wind", EquipmentSlot.Feet, ArmorType.Magic, 10, 50000, EquipmentRarity.Legendary).WithAgility(10).WithDexterity(5));
        AddEquipment(Equipment.CreateArmor(id++, "Archmage Slippers", EquipmentSlot.Feet, ArmorType.Magic, 10, 55000, EquipmentRarity.Legendary).WithMaxMana(60).WithIntelligence(5));
        AddEquipment(Equipment.CreateArmor(id++, "Windwalkers", EquipmentSlot.Feet, ArmorType.Magic, 14, 95000, EquipmentRarity.Legendary).WithDexterity(10).WithAgility(15).WithCritChance(8));
        AddEquipment(Equipment.CreateArmor(id++, "Voidstep Boots", EquipmentSlot.Feet, ArmorType.Magic, 16, 180000, EquipmentRarity.Artifact).WithMaxMana(100).WithIntelligence(10).WithAgility(12).RequiresEvilAlignment());

        // ===========================================
        // CHAIN/SCALE (Medium armor)
        // ===========================================
        AddEquipment(Equipment.CreateArmor(id++, "Chain Boots", EquipmentSlot.Feet, ArmorType.Chain, 4, 300));
        AddEquipment(Equipment.CreateArmor(id++, "Scale Boots", EquipmentSlot.Feet, ArmorType.Scale, 6, 800, EquipmentRarity.Uncommon));
        AddEquipment(Equipment.CreateArmor(id++, "Reinforced Chain Boots", EquipmentSlot.Feet, ArmorType.Chain, 8, 2200, EquipmentRarity.Rare).WithAgility(2));
        AddEquipment(Equipment.CreateArmor(id++, "Serpent Scale Boots", EquipmentSlot.Feet, ArmorType.Scale, 11, 11000, EquipmentRarity.Epic).WithMagicResist(8).WithAgility(4));

        // ===========================================
        // PLATE (Heavy armor)
        // ===========================================
        AddEquipment(Equipment.CreateArmor(id++, "Iron Boots", EquipmentSlot.Feet, ArmorType.Plate, 5, 600, EquipmentRarity.Uncommon));
        AddEquipment(Equipment.CreateArmor(id++, "Steel Boots", EquipmentSlot.Feet, ArmorType.Plate, 6, 1200, EquipmentRarity.Uncommon));
        AddEquipment(Equipment.CreateArmor(id++, "Knight's Boots", EquipmentSlot.Feet, ArmorType.Plate, 8, 3000, EquipmentRarity.Rare));
        AddEquipment(Equipment.CreateArmor(id++, "Blessed Boots", EquipmentSlot.Feet, ArmorType.Magic, 9, 5500, EquipmentRarity.Rare).WithAgility(3).RequiresGoodAlignment());
        AddEquipment(Equipment.CreateArmor(id++, "Dragon Boots", EquipmentSlot.Feet, ArmorType.Magic, 12, 15000, EquipmentRarity.Epic).WithMagicResist(10));
        AddEquipment(Equipment.CreateArmor(id++, "Celestial Boots", EquipmentSlot.Feet, ArmorType.Magic, 14, 35000, EquipmentRarity.Epic).WithWisdom(4).WithAgility(4).RequiresGoodAlignment());
        AddEquipment(Equipment.CreateArmor(id++, "Demon Treads", EquipmentSlot.Feet, ArmorType.Magic, 14, 38000, EquipmentRarity.Epic).WithStrength(5).WithAgility(4).RequiresEvilAlignment());
        AddEquipment(Equipment.CreateArmor(id++, "Elder Dragon Boots", EquipmentSlot.Feet, ArmorType.Magic, 18, 75000, EquipmentRarity.Legendary).WithStrength(5).WithMagicResist(18).WithAgility(5));
        AddEquipment(Equipment.CreateArmor(id++, "Warlord's Boots", EquipmentSlot.Feet, ArmorType.Plate, 20, 95000, EquipmentRarity.Legendary).WithStrength(8).WithConstitution(4).WithMaxHP(35));
        AddEquipment(Equipment.CreateArmor(id++, "Crusader's Boots", EquipmentSlot.Feet, ArmorType.Magic, 20, 110000, EquipmentRarity.Legendary).WithStrength(5).WithWisdom(5).WithMagicResist(12).RequiresGoodAlignment());
        AddEquipment(Equipment.CreateArmor(id++, "Infernal Boots", EquipmentSlot.Feet, ArmorType.Magic, 22, 140000, EquipmentRarity.Legendary).WithStrength(8).WithLifeSteal(4).RequiresEvilAlignment());

        // ===========================================
        // ARTIFACT (Ultimate boots)
        // ===========================================
        AddEquipment(Equipment.CreateArmor(id++, "Titan's Boots", EquipmentSlot.Feet, ArmorType.Artifact, 24, 185000, EquipmentRarity.Legendary).WithStrength(10).WithMaxHP(50));
        AddEquipment(Equipment.CreateArmor(id++, "Divine Boots", EquipmentSlot.Feet, ArmorType.Artifact, 26, 250000, EquipmentRarity.Artifact).WithStrength(8).WithWisdom(8).WithMagicResist(20).RequiresGoodAlignment());
        AddEquipment(Equipment.CreateArmor(id++, "Boots of Damnation", EquipmentSlot.Feet, ArmorType.Artifact, 26, 280000, EquipmentRarity.Artifact).WithStrength(12).WithLifeSteal(6).RequiresEvilAlignment());
        AddEquipment(Equipment.CreateArmor(id++, "Eternal Boots", EquipmentSlot.Feet, ArmorType.Artifact, 28, 360000, EquipmentRarity.Artifact).WithStrength(12).WithAgility(10).WithMaxHP(70));
        AddEquipment(Equipment.CreateArmor(id++, "Godforged Sabatons", EquipmentSlot.Feet, ArmorType.Artifact, 32, 520000, EquipmentRarity.Artifact).WithStrength(14).WithAgility(12).WithMaxHP(90).AsUnique());
        AddEquipment(Equipment.CreateArmor(id++, "Boots of the Gods", EquipmentSlot.Feet, ArmorType.Artifact, 35, 780000, EquipmentRarity.Artifact).WithStrength(18).WithDexterity(12).WithAgility(18).WithMaxHP(100).AsUnique());
    }

    #endregion

    #region Waist Armor (25+ items - full level 1-100 progression)

    private static void InitializeWaistArmor()
    {
        int id = WaistArmorStart;

        // ===========================================
        // CLOTH/LEATHER (Light armor)
        // ===========================================
        AddEquipment(Equipment.CreateArmor(id++, "Rope Belt", EquipmentSlot.Waist, ArmorType.Cloth, 0, 5));
        AddEquipment(Equipment.CreateArmor(id++, "Leather Belt", EquipmentSlot.Waist, ArmorType.Leather, 1, 30));
        AddEquipment(Equipment.CreateArmor(id++, "Studded Belt", EquipmentSlot.Waist, ArmorType.Leather, 2, 80));
        AddEquipment(Equipment.CreateArmor(id++, "Warrior's Belt", EquipmentSlot.Waist, ArmorType.Leather, 3, 500, EquipmentRarity.Uncommon).WithStrength(2));
        AddEquipment(Equipment.CreateArmor(id++, "Belt of Dexterity", EquipmentSlot.Waist, ArmorType.Leather, 3, 800, EquipmentRarity.Rare).WithDexterity(4));
        AddEquipment(Equipment.CreateArmor(id++, "Belt of Strength", EquipmentSlot.Waist, ArmorType.Leather, 4, 1200, EquipmentRarity.Rare).WithStrength(4));
        AddEquipment(Equipment.CreateArmor(id++, "Mage's Sash", EquipmentSlot.Waist, ArmorType.Cloth, 2, 1500, EquipmentRarity.Rare).WithMaxMana(30).WithIntelligence(2));
        AddEquipment(Equipment.CreateArmor(id++, "Thief's Belt", EquipmentSlot.Waist, ArmorType.Leather, 4, 3000, EquipmentRarity.Rare).WithDexterity(5).WithCritChance(5));
        AddEquipment(Equipment.CreateArmor(id++, "Assassin's Sash", EquipmentSlot.Waist, ArmorType.Magic, 6, 20000, EquipmentRarity.Epic).WithDexterity(8).WithCritChance(10).WithCritDamage(15));
        AddEquipment(Equipment.CreateArmor(id++, "Belt of Shadows", EquipmentSlot.Waist, ArmorType.Magic, 8, 45000, EquipmentRarity.Legendary).WithDexterity(10).WithCritChance(12).RequiresEvilAlignment());
        AddEquipment(Equipment.CreateArmor(id++, "Archmage Sash", EquipmentSlot.Waist, ArmorType.Magic, 8, 65000, EquipmentRarity.Legendary).WithMaxMana(100).WithIntelligence(8).WithWisdom(6));

        // ===========================================
        // CHAIN/PLATE (Heavy armor)
        // ===========================================
        AddEquipment(Equipment.CreateArmor(id++, "Chain Belt", EquipmentSlot.Waist, ArmorType.Chain, 3, 200));
        AddEquipment(Equipment.CreateArmor(id++, "Plate Belt", EquipmentSlot.Waist, ArmorType.Plate, 5, 1000, EquipmentRarity.Uncommon).WithStrength(2));
        AddEquipment(Equipment.CreateArmor(id++, "Girdle of Giants", EquipmentSlot.Waist, ArmorType.Magic, 6, 5000, EquipmentRarity.Epic).WithStrength(6).WithConstitution(3));
        AddEquipment(Equipment.CreateArmor(id++, "Belt of the Archmage", EquipmentSlot.Waist, ArmorType.Magic, 5, 8000, EquipmentRarity.Epic).WithMaxMana(60).WithIntelligence(4).WithWisdom(4));
        AddEquipment(Equipment.CreateArmor(id++, "Celestial Sash", EquipmentSlot.Waist, ArmorType.Magic, 8, 25000, EquipmentRarity.Legendary).WithWisdom(6).WithMaxHP(30).RequiresGoodAlignment());
        AddEquipment(Equipment.CreateArmor(id++, "Demon's Cinch", EquipmentSlot.Waist, ArmorType.Magic, 8, 30000, EquipmentRarity.Legendary).WithStrength(8).WithLifeSteal(4).RequiresEvilAlignment());
        AddEquipment(Equipment.CreateArmor(id++, "Dragon Scale Belt", EquipmentSlot.Waist, ArmorType.Magic, 10, 55000, EquipmentRarity.Legendary).WithStrength(6).WithMagicResist(15).WithMaxHP(40));
        AddEquipment(Equipment.CreateArmor(id++, "Warlord's Girdle", EquipmentSlot.Waist, ArmorType.Plate, 12, 85000, EquipmentRarity.Legendary).WithStrength(10).WithConstitution(6).WithMaxHP(60));

        // ===========================================
        // ARTIFACT (Ultimate belts)
        // ===========================================
        AddEquipment(Equipment.CreateArmor(id++, "Titan's Girdle", EquipmentSlot.Waist, ArmorType.Artifact, 14, 130000, EquipmentRarity.Legendary).WithStrength(12).WithMaxHP(80));
        AddEquipment(Equipment.CreateArmor(id++, "Divine Sash", EquipmentSlot.Waist, ArmorType.Artifact, 16, 200000, EquipmentRarity.Artifact).WithStrength(10).WithWisdom(10).WithMagicResist(20).RequiresGoodAlignment());
        AddEquipment(Equipment.CreateArmor(id++, "Belt of Damnation", EquipmentSlot.Waist, ArmorType.Artifact, 16, 220000, EquipmentRarity.Artifact).WithStrength(14).WithLifeSteal(8).RequiresEvilAlignment());
        AddEquipment(Equipment.CreateArmor(id++, "Girdle of Power", EquipmentSlot.Waist, ArmorType.Artifact, 18, 320000, EquipmentRarity.Artifact).WithStrength(15).WithConstitution(10).WithMaxHP(100).AsUnique());
        AddEquipment(Equipment.CreateArmor(id++, "Belt of Eternity", EquipmentSlot.Waist, ArmorType.Artifact, 22, 500000, EquipmentRarity.Artifact).WithStrength(18).WithWisdom(12).WithMaxHP(120).WithMaxMana(60).AsUnique());
    }

    #endregion

    #region Face Armor (25+ items - full level 1-100 progression)

    private static void InitializeFaceArmor()
    {
        int id = FaceArmorStart;

        // ===========================================
        // CLOTH/LEATHER (Light armor)
        // ===========================================
        AddEquipment(Equipment.CreateArmor(id++, "Cloth Mask", EquipmentSlot.Face, ArmorType.Cloth, 1, 20));
        AddEquipment(Equipment.CreateArmor(id++, "Leather Mask", EquipmentSlot.Face, ArmorType.Leather, 2, 60));
        AddEquipment(Equipment.CreateArmor(id++, "Thief's Mask", EquipmentSlot.Face, ArmorType.Leather, 3, 1000, EquipmentRarity.Rare).WithDexterity(3).WithCritChance(3));
        AddEquipment(Equipment.CreateArmor(id++, "Mage's Cowl", EquipmentSlot.Face, ArmorType.Cloth, 4, 3000, EquipmentRarity.Rare).WithMaxMana(25).WithIntelligence(3));
        AddEquipment(Equipment.CreateArmor(id++, "Shadow Mask", EquipmentSlot.Face, ArmorType.Magic, 6, 15000, EquipmentRarity.Epic).WithDexterity(5).WithCritChance(8).RequiresEvilAlignment());
        AddEquipment(Equipment.CreateArmor(id++, "Assassin's Veil", EquipmentSlot.Face, ArmorType.Magic, 8, 35000, EquipmentRarity.Legendary).WithDexterity(8).WithCritChance(12).WithCritDamage(20));
        AddEquipment(Equipment.CreateArmor(id++, "Archmage Cowl", EquipmentSlot.Face, ArmorType.Magic, 10, 60000, EquipmentRarity.Legendary).WithMaxMana(80).WithIntelligence(8).WithWisdom(6));
        AddEquipment(Equipment.CreateArmor(id++, "Nightstalker Mask", EquipmentSlot.Face, ArmorType.Magic, 12, 100000, EquipmentRarity.Legendary).WithDexterity(12).WithCritChance(15).WithCritDamage(30).RequiresEvilAlignment());

        // ===========================================
        // PLATE (Heavy armor)
        // ===========================================
        AddEquipment(Equipment.CreateArmor(id++, "Iron Visor", EquipmentSlot.Face, ArmorType.Plate, 3, 150));
        AddEquipment(Equipment.CreateArmor(id++, "Steel Visor", EquipmentSlot.Face, ArmorType.Plate, 4, 350, EquipmentRarity.Uncommon));
        AddEquipment(Equipment.CreateArmor(id++, "Battle Mask", EquipmentSlot.Face, ArmorType.Plate, 5, 700, EquipmentRarity.Uncommon).WithStrength(1));
        AddEquipment(Equipment.CreateArmor(id++, "Knight's Visor", EquipmentSlot.Face, ArmorType.Plate, 7, 2500, EquipmentRarity.Rare));
        AddEquipment(Equipment.CreateArmor(id++, "Dragon Mask", EquipmentSlot.Face, ArmorType.Magic, 10, 12000, EquipmentRarity.Epic).WithMagicResist(12).WithStrength(3));
        AddEquipment(Equipment.CreateArmor(id++, "Celestial Visage", EquipmentSlot.Face, ArmorType.Magic, 12, 30000, EquipmentRarity.Epic).WithWisdom(5).WithCharisma(5).RequiresGoodAlignment());
        AddEquipment(Equipment.CreateArmor(id++, "Demon's Visage", EquipmentSlot.Face, ArmorType.Magic, 12, 35000, EquipmentRarity.Epic).WithStrength(6).WithLifeSteal(3).RequiresEvilAlignment());
        AddEquipment(Equipment.CreateArmor(id++, "Mask of Terror", EquipmentSlot.Face, ArmorType.Magic, 14, 55000, EquipmentRarity.Legendary).WithStrength(6).WithCritDamage(20));
        AddEquipment(Equipment.CreateArmor(id++, "Elder Dragon Mask", EquipmentSlot.Face, ArmorType.Magic, 16, 80000, EquipmentRarity.Legendary).WithStrength(6).WithMagicResist(20).WithMaxHP(30));
        AddEquipment(Equipment.CreateArmor(id++, "Warlord's Visor", EquipmentSlot.Face, ArmorType.Plate, 18, 110000, EquipmentRarity.Legendary).WithStrength(10).WithConstitution(5).WithMaxHP(50));
        AddEquipment(Equipment.CreateArmor(id++, "Divine Visage", EquipmentSlot.Face, ArmorType.Magic, 18, 130000, EquipmentRarity.Legendary).WithWisdom(10).WithCharisma(8).WithMagicResist(15).RequiresGoodAlignment());
        AddEquipment(Equipment.CreateArmor(id++, "Infernal Mask", EquipmentSlot.Face, ArmorType.Magic, 18, 140000, EquipmentRarity.Legendary).WithStrength(10).WithLifeSteal(6).RequiresEvilAlignment());

        // ===========================================
        // ARTIFACT (Ultimate face armor)
        // ===========================================
        AddEquipment(Equipment.CreateArmor(id++, "Mask of the Ancients", EquipmentSlot.Face, ArmorType.Artifact, 22, 200000, EquipmentRarity.Artifact).WithIntelligence(10).WithWisdom(10).WithMaxMana(80).AsUnique());
        AddEquipment(Equipment.CreateArmor(id++, "Titan's Visage", EquipmentSlot.Face, ArmorType.Artifact, 24, 280000, EquipmentRarity.Artifact).WithStrength(14).WithConstitution(8).WithMaxHP(80));
        AddEquipment(Equipment.CreateArmor(id++, "Visage of Judgment", EquipmentSlot.Face, ArmorType.Artifact, 26, 380000, EquipmentRarity.Artifact).WithStrength(10).WithWisdom(12).WithMagicResist(25).RequiresGoodAlignment().AsUnique());
        AddEquipment(Equipment.CreateArmor(id++, "Mask of Oblivion", EquipmentSlot.Face, ArmorType.Artifact, 26, 400000, EquipmentRarity.Artifact).WithStrength(14).WithLifeSteal(10).WithCritDamage(40).RequiresEvilAlignment().AsUnique());
        AddEquipment(Equipment.CreateArmor(id++, "Visage of the Gods", EquipmentSlot.Face, ArmorType.Artifact, 30, 600000, EquipmentRarity.Artifact).WithStrength(15).WithWisdom(15).WithCharisma(15).WithMaxHP(100).AsUnique());
    }

    #endregion

    #region Cloaks (25+ items - full level 1-100 progression)

    private static void InitializeCloaks()
    {
        int id = CloakStart;

        // ===========================================
        // BASIC CLOAKS (Early game)
        // ===========================================
        AddEquipment(Equipment.CreateArmor(id++, "Torn Cloak", EquipmentSlot.Cloak, ArmorType.Cloth, 1, 15));
        AddEquipment(Equipment.CreateArmor(id++, "Wool Cloak", EquipmentSlot.Cloak, ArmorType.Cloth, 2, 40));
        AddEquipment(Equipment.CreateArmor(id++, "Leather Cloak", EquipmentSlot.Cloak, ArmorType.Leather, 3, 100));
        AddEquipment(Equipment.CreateArmor(id++, "Silk Cloak", EquipmentSlot.Cloak, ArmorType.Cloth, 4, 300, EquipmentRarity.Uncommon).WithCharisma(2));
        AddEquipment(Equipment.CreateArmor(id++, "Traveler's Cloak", EquipmentSlot.Cloak, ArmorType.Leather, 5, 600, EquipmentRarity.Uncommon).WithStamina(10));

        // ===========================================
        // MAGE CLOAKS
        // ===========================================
        AddEquipment(Equipment.CreateArmor(id++, "Mage's Robe", EquipmentSlot.Cloak, ArmorType.Cloth, 4, 1500, EquipmentRarity.Rare).WithMaxMana(30).WithIntelligence(3));
        AddEquipment(Equipment.CreateArmor(id++, "Arcane Cloak", EquipmentSlot.Cloak, ArmorType.Magic, 7, 8000, EquipmentRarity.Epic).WithMaxMana(60).WithIntelligence(5));
        AddEquipment(Equipment.CreateArmor(id++, "Archmage Robes", EquipmentSlot.Cloak, ArmorType.Magic, 10, 50000, EquipmentRarity.Legendary).WithMaxMana(100).WithIntelligence(8).WithWisdom(5));
        AddEquipment(Equipment.CreateArmor(id++, "Robes of the Grand Sorcerer", EquipmentSlot.Cloak, ArmorType.Magic, 14, 120000, EquipmentRarity.Legendary).WithMaxMana(150).WithIntelligence(12).WithWisdom(8).WithMagicResist(15));

        // ===========================================
        // ROGUE CLOAKS
        // ===========================================
        AddEquipment(Equipment.CreateArmor(id++, "Cloak of Shadows", EquipmentSlot.Cloak, ArmorType.Magic, 5, 5000, EquipmentRarity.Epic).WithDexterity(4).WithCritChance(5));
        AddEquipment(Equipment.CreateArmor(id++, "Nightfall Cloak", EquipmentSlot.Cloak, ArmorType.Magic, 8, 22000, EquipmentRarity.Epic).WithDexterity(6).WithAgility(6).WithCritChance(8).RequiresEvilAlignment());
        AddEquipment(Equipment.CreateArmor(id++, "Cloak of Invisibility", EquipmentSlot.Cloak, ArmorType.Magic, 10, 80000, EquipmentRarity.Legendary).WithDexterity(10).WithAgility(10).WithCritChance(15).AsUnique());
        AddEquipment(Equipment.CreateArmor(id++, "Phantom Shroud", EquipmentSlot.Cloak, ArmorType.Magic, 14, 140000, EquipmentRarity.Legendary).WithDexterity(14).WithAgility(12).WithCritChance(18).WithCritDamage(25));

        // ===========================================
        // WARRIOR CLOAKS
        // ===========================================
        AddEquipment(Equipment.CreateArmor(id++, "Cloak of Protection", EquipmentSlot.Cloak, ArmorType.Magic, 6, 3000, EquipmentRarity.Rare).WithDefence(3).WithMagicResist(5));
        AddEquipment(Equipment.CreateArmor(id++, "Dragon Scale Cloak", EquipmentSlot.Cloak, ArmorType.Magic, 10, 15000, EquipmentRarity.Epic).WithMagicResist(15));
        AddEquipment(Equipment.CreateArmor(id++, "Warlord's Cape", EquipmentSlot.Cloak, ArmorType.Magic, 14, 55000, EquipmentRarity.Legendary).WithStrength(8).WithMaxHP(40).WithMagicResist(12));
        AddEquipment(Equipment.CreateArmor(id++, "Elder Dragon Cape", EquipmentSlot.Cloak, ArmorType.Magic, 18, 100000, EquipmentRarity.Legendary).WithStrength(6).WithMagicResist(25).WithMaxHP(60));

        // ===========================================
        // ALIGNMENT CLOAKS
        // ===========================================
        AddEquipment(Equipment.CreateArmor(id++, "Celestial Mantle", EquipmentSlot.Cloak, ArmorType.Magic, 12, 35000, EquipmentRarity.Legendary).WithWisdom(5).WithMagicResist(15).RequiresGoodAlignment());
        AddEquipment(Equipment.CreateArmor(id++, "Cloak of the Demon", EquipmentSlot.Cloak, ArmorType.Magic, 12, 40000, EquipmentRarity.Legendary).WithStrength(5).WithLifeSteal(5).RequiresEvilAlignment());
        AddEquipment(Equipment.CreateArmor(id++, "Seraphim Wings", EquipmentSlot.Cloak, ArmorType.Magic, 16, 95000, EquipmentRarity.Legendary).WithWisdom(10).WithMagicResist(25).WithMaxHP(40).RequiresGoodAlignment());
        AddEquipment(Equipment.CreateArmor(id++, "Infernal Shroud", EquipmentSlot.Cloak, ArmorType.Magic, 16, 105000, EquipmentRarity.Legendary).WithStrength(10).WithLifeSteal(8).WithCritDamage(20).RequiresEvilAlignment());

        // ===========================================
        // ARTIFACT CLOAKS (Ultimate)
        // ===========================================
        AddEquipment(Equipment.CreateArmor(id++, "Divine Mantle", EquipmentSlot.Cloak, ArmorType.Artifact, 20, 180000, EquipmentRarity.Artifact).WithWisdom(12).WithMagicResist(35).WithMaxHP(70).RequiresGoodAlignment().AsUnique());
        AddEquipment(Equipment.CreateArmor(id++, "Cloak of Damnation", EquipmentSlot.Cloak, ArmorType.Artifact, 20, 200000, EquipmentRarity.Artifact).WithStrength(14).WithLifeSteal(12).WithMaxHP(60).RequiresEvilAlignment().AsUnique());
        AddEquipment(Equipment.CreateArmor(id++, "Cloak of the Cosmos", EquipmentSlot.Cloak, ArmorType.Artifact, 22, 350000, EquipmentRarity.Artifact).WithIntelligence(14).WithWisdom(14).WithMaxMana(200).WithMagicResist(40).AsUnique());
        AddEquipment(Equipment.CreateArmor(id++, "Titan's Mantle", EquipmentSlot.Cloak, ArmorType.Artifact, 24, 450000, EquipmentRarity.Artifact).WithStrength(16).WithConstitution(12).WithMaxHP(120).WithMagicResist(30));
        AddEquipment(Equipment.CreateArmor(id++, "Cloak of Eternity", EquipmentSlot.Cloak, ArmorType.Artifact, 28, 700000, EquipmentRarity.Artifact).WithStrength(12).WithIntelligence(12).WithWisdom(12).WithMaxHP(100).WithMaxMana(100).WithMagicResist(45).AsUnique());
    }

    #endregion

    #region Neck Accessories (35+ items - full level 1-100 progression)

    private static void InitializeNeckAccessories()
    {
        int id = NeckAccessoryStart;

        // BASIC NECKLACES (Early game - levels 1-10)
        AddEquipment(Equipment.CreateAccessory(id++, "Wooden Pendant", EquipmentSlot.Neck, 30).WithMaxHP(5));
        AddEquipment(Equipment.CreateAccessory(id++, "Bone Necklace", EquipmentSlot.Neck, 50).WithStrength(1));
        AddEquipment(Equipment.CreateAccessory(id++, "Copper Necklace", EquipmentSlot.Neck, 80).WithCharisma(1));
        AddEquipment(Equipment.CreateAccessory(id++, "Shell Pendant", EquipmentSlot.Neck, 120).WithMagicResist(2));
        AddEquipment(Equipment.CreateAccessory(id++, "Stone Amulet", EquipmentSlot.Neck, 180).WithDefence(1).WithMaxHP(8));

        // SILVER/BRONZE TIER (Levels 10-25)
        AddEquipment(Equipment.CreateAccessory(id++, "Bronze Medallion", EquipmentSlot.Neck, 250, EquipmentRarity.Uncommon).WithStrength(2));
        AddEquipment(Equipment.CreateAccessory(id++, "Silver Amulet", EquipmentSlot.Neck, 400, EquipmentRarity.Uncommon).WithWisdom(2).WithMaxMana(15));
        AddEquipment(Equipment.CreateAccessory(id++, "Jade Pendant", EquipmentSlot.Neck, 600, EquipmentRarity.Uncommon).WithAgility(2).WithDexterity(1));
        AddEquipment(Equipment.CreateAccessory(id++, "Gold Amulet", EquipmentSlot.Neck, 900, EquipmentRarity.Uncommon).WithCharisma(3).WithMaxHP(15));
        AddEquipment(Equipment.CreateAccessory(id++, "Onyx Necklace", EquipmentSlot.Neck, 1200, EquipmentRarity.Uncommon).WithStrength(3).WithDefence(1));

        // ENCHANTED TIER (Levels 25-45)
        AddEquipment(Equipment.CreateAccessory(id++, "Amulet of Strength", EquipmentSlot.Neck, 2000, EquipmentRarity.Rare).WithStrength(4));
        AddEquipment(Equipment.CreateAccessory(id++, "Amulet of Wisdom", EquipmentSlot.Neck, 2000, EquipmentRarity.Rare).WithWisdom(4).WithMaxMana(30));
        AddEquipment(Equipment.CreateAccessory(id++, "Pendant of Protection", EquipmentSlot.Neck, 2500, EquipmentRarity.Rare).WithDefence(3).WithMagicResist(8));
        AddEquipment(Equipment.CreateAccessory(id++, "Assassin's Choker", EquipmentSlot.Neck, 3000, EquipmentRarity.Rare).WithDexterity(4).WithCritChance(3));
        AddEquipment(Equipment.CreateAccessory(id++, "Holy Symbol", EquipmentSlot.Neck, 4000, EquipmentRarity.Rare).WithWisdom(5).WithMaxMana(40).RequiresGoodAlignment());
        AddEquipment(Equipment.CreateAccessory(id++, "Dark Medallion", EquipmentSlot.Neck, 4500, EquipmentRarity.Rare).WithStrength(5).WithLifeSteal(2).RequiresEvilAlignment());
        AddEquipment(Equipment.CreateAccessory(id++, "Berserker's Torc", EquipmentSlot.Neck, 5500, EquipmentRarity.Rare).WithStrength(6).WithMaxHP(25));

        // EPIC TIER (Levels 45-65)
        AddEquipment(Equipment.CreateAccessory(id++, "Crystal Necklace", EquipmentSlot.Neck, 8000, EquipmentRarity.Epic).WithMaxMana(60).WithIntelligence(4));
        AddEquipment(Equipment.CreateAccessory(id++, "Bloodstone Amulet", EquipmentSlot.Neck, 12000, EquipmentRarity.Epic).WithStrength(6).WithLifeSteal(3));
        AddEquipment(Equipment.CreateAccessory(id++, "Amulet of the Dragon", EquipmentSlot.Neck, 16000, EquipmentRarity.Epic).WithMagicResist(18).WithMaxHP(40));
        AddEquipment(Equipment.CreateAccessory(id++, "Necklace of Vitality", EquipmentSlot.Neck, 22000, EquipmentRarity.Epic).WithConstitution(6).WithMaxHP(60));
        AddEquipment(Equipment.CreateAccessory(id++, "Scarab of the Sands", EquipmentSlot.Neck, 30000, EquipmentRarity.Epic).WithAgility(6).WithDexterity(4).WithMagicResist(12));
        AddEquipment(Equipment.CreateAccessory(id++, "Amulet of the Archmage", EquipmentSlot.Neck, 40000, EquipmentRarity.Epic).WithMaxMana(100).WithIntelligence(7).WithWisdom(5));

        // LEGENDARY TIER (Levels 65-85)
        AddEquipment(Equipment.CreateAccessory(id++, "Celestial Pendant", EquipmentSlot.Neck, 60000, EquipmentRarity.Legendary).WithWisdom(9).WithMagicResist(22).RequiresGoodAlignment());
        AddEquipment(Equipment.CreateAccessory(id++, "Amulet of Shadows", EquipmentSlot.Neck, 70000, EquipmentRarity.Legendary).WithDexterity(9).WithCritChance(12).RequiresEvilAlignment());
        AddEquipment(Equipment.CreateAccessory(id++, "Titan's Pendant", EquipmentSlot.Neck, 90000, EquipmentRarity.Legendary).WithStrength(10).WithMaxHP(70));
        AddEquipment(Equipment.CreateAccessory(id++, "Phoenix Amulet", EquipmentSlot.Neck, 120000, EquipmentRarity.Legendary).WithConstitution(8).WithMaxHP(80).WithMagicResist(18));
        AddEquipment(Equipment.CreateAccessory(id++, "Serpent's Eye Pendant", EquipmentSlot.Neck, 160000, EquipmentRarity.Legendary).WithDexterity(10).WithAgility(8).WithCritDamage(15));
        AddEquipment(Equipment.CreateAccessory(id++, "Amulet of Power", EquipmentSlot.Neck, 220000, EquipmentRarity.Legendary).WithStrength(10).WithIntelligence(8).WithWisdom(8).AsUnique());

        // ARTIFACT TIER (Levels 85-100 - Ultimate accessories)
        AddEquipment(Equipment.CreateAccessory(id++, "Divine Amulet", EquipmentSlot.Neck, 350000, EquipmentRarity.Artifact).WithWisdom(14).WithMagicResist(35).WithMaxHP(100).RequiresGoodAlignment().AsUnique());
        AddEquipment(Equipment.CreateAccessory(id++, "Amulet of the Void", EquipmentSlot.Neck, 400000, EquipmentRarity.Artifact).WithStrength(14).WithLifeSteal(12).WithMagicResist(30).RequiresEvilAlignment().AsUnique());
        AddEquipment(Equipment.CreateAccessory(id++, "Stormcaller's Torc", EquipmentSlot.Neck, 500000, EquipmentRarity.Artifact).WithIntelligence(14).WithMaxMana(150).WithMagicResist(35).AsUnique());
        AddEquipment(Equipment.CreateAccessory(id++, "Heart of the Mountain", EquipmentSlot.Neck, 650000, EquipmentRarity.Artifact).WithStrength(12).WithConstitution(14).WithMaxHP(150).WithDefence(8).AsUnique());
        AddEquipment(Equipment.CreateAccessory(id++, "Amulet of Eternity", EquipmentSlot.Neck, 850000, EquipmentRarity.Artifact).WithStrength(12).WithConstitution(12).WithWisdom(12).WithMaxHP(120).WithMaxMana(120).AsUnique());
    }

    #endregion

    #region Rings (30+ items - full level 1-100 progression)

    private static void InitializeRings()
    {
        int id = RingStart;

        // BASIC RINGS (Early game - levels 1-10)
        AddEquipment(Equipment.CreateAccessory(id++, "Copper Ring", EquipmentSlot.LFinger, 25));
        AddEquipment(Equipment.CreateAccessory(id++, "Iron Band", EquipmentSlot.LFinger, 50).WithStrength(1));
        AddEquipment(Equipment.CreateAccessory(id++, "Bronze Ring", EquipmentSlot.LFinger, 80).WithMaxHP(5));
        AddEquipment(Equipment.CreateAccessory(id++, "Silver Ring", EquipmentSlot.LFinger, 120, EquipmentRarity.Uncommon).WithCharisma(1));
        AddEquipment(Equipment.CreateAccessory(id++, "Bone Ring", EquipmentSlot.LFinger, 180).WithMagicResist(2));

        // SILVER/BRONZE TIER (Levels 10-25)
        AddEquipment(Equipment.CreateAccessory(id++, "Gold Ring", EquipmentSlot.LFinger, 300, EquipmentRarity.Uncommon).WithCharisma(2));
        AddEquipment(Equipment.CreateAccessory(id++, "Jade Ring", EquipmentSlot.LFinger, 500, EquipmentRarity.Uncommon).WithAgility(2));
        AddEquipment(Equipment.CreateAccessory(id++, "Ring of Dexterity", EquipmentSlot.LFinger, 800, EquipmentRarity.Uncommon).WithDexterity(3));
        AddEquipment(Equipment.CreateAccessory(id++, "Ring of Strength", EquipmentSlot.LFinger, 800, EquipmentRarity.Uncommon).WithStrength(3));
        AddEquipment(Equipment.CreateAccessory(id++, "Mana Ring", EquipmentSlot.LFinger, 1200, EquipmentRarity.Uncommon).WithMaxMana(25));
        AddEquipment(Equipment.CreateAccessory(id++, "Onyx Ring", EquipmentSlot.LFinger, 1600, EquipmentRarity.Uncommon).WithStrength(3).WithDefence(1));

        // ENCHANTED TIER (Levels 25-45)
        AddEquipment(Equipment.CreateAccessory(id++, "Ring of Protection", EquipmentSlot.LFinger, 2500, EquipmentRarity.Rare).WithDefence(3).WithMagicResist(6));
        AddEquipment(Equipment.CreateAccessory(id++, "Ring of Vitality", EquipmentSlot.LFinger, 3000, EquipmentRarity.Rare).WithMaxHP(30).WithConstitution(3));
        AddEquipment(Equipment.CreateAccessory(id++, "Thief's Band", EquipmentSlot.LFinger, 3500, EquipmentRarity.Rare).WithDexterity(4).WithCritChance(3));
        AddEquipment(Equipment.CreateAccessory(id++, "Sage's Ring", EquipmentSlot.LFinger, 4000, EquipmentRarity.Rare).WithIntelligence(4).WithWisdom(4));
        AddEquipment(Equipment.CreateAccessory(id++, "Blessed Band", EquipmentSlot.LFinger, 5000, EquipmentRarity.Rare).WithWisdom(5).WithMaxMana(35).RequiresGoodAlignment());
        AddEquipment(Equipment.CreateAccessory(id++, "Cursed Ring", EquipmentSlot.LFinger, 5500, EquipmentRarity.Rare).WithStrength(5).WithLifeSteal(2).RequiresEvilAlignment());
        AddEquipment(Equipment.CreateAccessory(id++, "Berserker's Ring", EquipmentSlot.LFinger, 6500, EquipmentRarity.Rare).WithStrength(6).WithCritDamage(5));

        // EPIC TIER (Levels 45-65)
        AddEquipment(Equipment.CreateAccessory(id++, "Ring of Shadows", EquipmentSlot.LFinger, 9000, EquipmentRarity.Epic).WithDexterity(6).WithCritChance(6));
        AddEquipment(Equipment.CreateAccessory(id++, "Warrior's Band", EquipmentSlot.LFinger, 12000, EquipmentRarity.Epic).WithStrength(6).WithMaxHP(35));
        AddEquipment(Equipment.CreateAccessory(id++, "Ring of the Archmage", EquipmentSlot.LFinger, 16000, EquipmentRarity.Epic).WithMaxMana(60).WithIntelligence(6));
        AddEquipment(Equipment.CreateAccessory(id++, "Bloodstone Ring", EquipmentSlot.LFinger, 22000, EquipmentRarity.Epic).WithStrength(7).WithLifeSteal(4));
        AddEquipment(Equipment.CreateAccessory(id++, "Ring of the Elements", EquipmentSlot.LFinger, 30000, EquipmentRarity.Epic).WithMagicResist(20).WithMaxMana(50));
        AddEquipment(Equipment.CreateAccessory(id++, "Champion's Signet", EquipmentSlot.LFinger, 40000, EquipmentRarity.Epic).WithStrength(7).WithDexterity(5).WithMaxHP(45));

        // LEGENDARY TIER (Levels 65-85)
        AddEquipment(Equipment.CreateAccessory(id++, "Master's Ring", EquipmentSlot.LFinger, 55000, EquipmentRarity.Legendary).WithStrength(7).WithDexterity(7).WithWisdom(7));
        AddEquipment(Equipment.CreateAccessory(id++, "Ring of Power", EquipmentSlot.LFinger, 75000, EquipmentRarity.Legendary).WithStrength(9).WithMaxHP(55));
        AddEquipment(Equipment.CreateAccessory(id++, "Ring of the Dragon", EquipmentSlot.LFinger, 100000, EquipmentRarity.Legendary).WithMagicResist(22).WithMaxHP(65));
        AddEquipment(Equipment.CreateAccessory(id++, "Celestial Band", EquipmentSlot.LFinger, 130000, EquipmentRarity.Legendary).WithWisdom(10).WithMagicResist(20).RequiresGoodAlignment());
        AddEquipment(Equipment.CreateAccessory(id++, "Ring of Darkness", EquipmentSlot.LFinger, 140000, EquipmentRarity.Legendary).WithStrength(10).WithLifeSteal(7).RequiresEvilAlignment());
        AddEquipment(Equipment.CreateAccessory(id++, "Phoenix Ring", EquipmentSlot.LFinger, 180000, EquipmentRarity.Legendary).WithConstitution(10).WithMaxHP(80).WithMagicResist(15));
        AddEquipment(Equipment.CreateAccessory(id++, "Serpent's Coil", EquipmentSlot.LFinger, 240000, EquipmentRarity.Legendary).WithDexterity(12).WithAgility(8).WithCritChance(12));

        // ARTIFACT TIER (Levels 85-100 - Ultimate rings)
        AddEquipment(Equipment.CreateAccessory(id++, "Titan's Band", EquipmentSlot.LFinger, 350000, EquipmentRarity.Artifact).WithStrength(12).WithConstitution(10).WithMaxHP(100).AsUnique());
        AddEquipment(Equipment.CreateAccessory(id++, "Ring of the Arcane", EquipmentSlot.LFinger, 420000, EquipmentRarity.Artifact).WithIntelligence(14).WithMaxMana(150).WithMagicResist(30).AsUnique());
        AddEquipment(Equipment.CreateAccessory(id++, "Shadowlord's Signet", EquipmentSlot.LFinger, 500000, EquipmentRarity.Artifact).WithDexterity(14).WithCritChance(18).WithCritDamage(25).RequiresEvilAlignment().AsUnique());
        AddEquipment(Equipment.CreateAccessory(id++, "Divine Seal", EquipmentSlot.LFinger, 520000, EquipmentRarity.Artifact).WithWisdom(14).WithMaxMana(120).WithMagicResist(35).RequiresGoodAlignment().AsUnique());
        AddEquipment(Equipment.CreateAccessory(id++, "Ring of the Gods", EquipmentSlot.LFinger, 700000, EquipmentRarity.Artifact).WithStrength(10).WithDexterity(10).WithWisdom(10).WithIntelligence(10).AsUnique());
        AddEquipment(Equipment.CreateAccessory(id++, "Ring of Eternity", EquipmentSlot.LFinger, 900000, EquipmentRarity.Artifact).WithStrength(14).WithConstitution(14).WithMaxHP(120).WithMaxMana(80).WithLifeSteal(8).AsUnique());
    }

    #endregion
}
