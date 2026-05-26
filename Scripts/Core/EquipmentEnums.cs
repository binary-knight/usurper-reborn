using System;

/// <summary>
/// Equipment slot types for the modern RPG equipment system
/// Supports full-body armor coverage plus weapon/shield configurations
/// </summary>
public enum EquipmentSlot
{
    None = 0,

    // Armor slots
    Head = 1,       // Helms, hats, crowns
    Body = 2,       // Chest armor, robes
    Arms = 3,       // Bracers, arm guards
    Hands = 4,      // Gloves, gauntlets
    Legs = 5,       // Leggings, greaves
    Feet = 6,       // Boots, shoes
    Waist = 7,      // Belts, sashes
    Neck = 8,       // Amulet slot 1
    Neck2 = 9,      // Amulet slot 2
    Face = 10,      // Masks, visors
    Cloak = 11,     // Cloaks, capes

    // Accessory slots
    LFinger = 12,   // Left ring
    RFinger = 13,   // Right ring

    // Weapon slots
    MainHand = 14,  // Primary weapon (right hand)
    OffHand = 15    // Secondary weapon, shield, or empty for 2H (left hand)
}

/// <summary>
/// Weapon handedness determines how weapons can be equipped
/// </summary>
public enum WeaponHandedness
{
    None = 0,           // Not a weapon (armor, accessory)
    OneHanded = 1,      // Can dual-wield or use with shield
    TwoHanded = 2,      // Requires both hands, no off-hand item
    OffHandOnly = 3     // Shields and off-hand only items
}

/// <summary>
/// Weapon type for categorization and special abilities
/// </summary>
public enum WeaponType
{
    None = 0,

    // One-handed melee
    Sword = 1,          // Balanced, versatile
    Axe = 2,            // High damage, slower
    Mace = 3,           // Armor piercing
    Dagger = 4,         // Fast, backstab bonus
    Rapier = 5,         // Dexterity-based
    Hammer = 6,         // Stun chance
    Flail = 7,          // Ignores shield
    Scimitar = 8,       // Crit bonus

    // Two-handed melee
    Greatsword = 10,    // High damage
    Greataxe = 11,      // Highest damage, slow
    Polearm = 12,       // Reach, first strike
    Staff = 13,         // Magic bonus
    Maul = 14,          // Armor crushing

    // Special
    Instrument = 17,    // Musical instruments (Bard-only, one-handed)

    // Ranged
    Bow = 15,           // Two-handed ranged, DEX-based

    // Shields
    Shield = 20,        // Block chance + AC
    Buckler = 21,       // Light, less AC, can still attack
    TowerShield = 22    // High AC, attack penalty
}

/// <summary>
/// Armor type for categorization
/// </summary>
public enum ArmorType
{
    None = 0,
    Cloth = 1,          // Mages, light
    Leather = 2,        // Rogues, light-medium
    Chain = 3,          // Medium armor
    Scale = 4,          // Medium-heavy
    Plate = 5,          // Heavy armor
    Magic = 6,          // Enchanted/special
    Artifact = 7        // Legendary items
}

/// <summary>
/// Armor weight class determines mobility, stamina, and fatigue trade-offs
/// </summary>
public enum ArmorWeightClass
{
    None = 0,       // Accessories, weapons, unarmored
    Light = 1,      // Cloth, Leather — casters, rogues
    Medium = 2,     // Chain, Scale — hybrids, rangers
    Heavy = 3       // Plate — tanks, warriors
}

/// <summary>
/// Equipment rarity affects stats and visuals
/// </summary>
public enum EquipmentRarity
{
    Common = 0,         // White - basic items
    Uncommon = 1,       // Green - slightly enhanced
    Rare = 2,           // Blue - good stats
    Epic = 3,           // Purple - excellent stats
    Legendary = 4,      // Orange - best non-unique
    Artifact = 5        // Gold - unique items
}

/// <summary>
/// Extension methods for equipment enums
/// </summary>
public static class EquipmentSlotExtensions
{
    /// <summary>
    /// Get the localized display name for an equipment slot. Routes through
    /// GameConfig.GetLocalizedSlotName (reads `equip.slot.{Slot}` loc keys, which
    /// cover every enum value) so all display sites -- shops, inventory, compare
    /// window -- show the slot name in the player's session language. Player
    /// report: armor shop showed "head: <localized empty>" with the slot name
    /// still in English.
    /// </summary>
    public static string GetDisplayName(this EquipmentSlot slot) =>
        GameConfig.GetLocalizedSlotName(slot);

    /// <summary>
    /// Check if slot is an armor slot
    /// </summary>
    public static bool IsArmorSlot(this EquipmentSlot slot) => slot switch
    {
        EquipmentSlot.Head or EquipmentSlot.Body or EquipmentSlot.Arms or
        EquipmentSlot.Hands or EquipmentSlot.Legs or EquipmentSlot.Feet or
        EquipmentSlot.Waist or EquipmentSlot.Face or EquipmentSlot.Cloak => true,
        _ => false
    };

    /// <summary>
    /// Check if slot is an accessory slot (rings, amulets)
    /// </summary>
    public static bool IsAccessorySlot(this EquipmentSlot slot) => slot switch
    {
        EquipmentSlot.Neck or EquipmentSlot.Neck2 or
        EquipmentSlot.LFinger or EquipmentSlot.RFinger or
        EquipmentSlot.Waist => true,
        _ => false
    };

    /// <summary>
    /// Check if slot is a weapon slot
    /// </summary>
    public static bool IsWeaponSlot(this EquipmentSlot slot) =>
        slot == EquipmentSlot.MainHand || slot == EquipmentSlot.OffHand;
}

public static class WeaponHandednessExtensions
{
    /// <summary>
    /// Get display name for weapon handedness
    /// </summary>
    public static string GetDisplayName(this WeaponHandedness handedness) => handedness switch
    {
        WeaponHandedness.OneHanded => "One-Handed",
        WeaponHandedness.TwoHanded => "Two-Handed",
        WeaponHandedness.OffHandOnly => "Off-Hand",
        _ => ""
    };
}

public static class ArmorTypeExtensions
{
    /// <summary>
    /// Map ArmorType to ArmorWeightClass
    /// </summary>
    public static ArmorWeightClass GetWeightClass(this ArmorType armorType) => armorType switch
    {
        ArmorType.Cloth => ArmorWeightClass.Light,
        ArmorType.Leather => ArmorWeightClass.Light,
        ArmorType.Chain => ArmorWeightClass.Medium,
        ArmorType.Scale => ArmorWeightClass.Medium,
        ArmorType.Plate => ArmorWeightClass.Heavy,
        ArmorType.Magic => ArmorWeightClass.Light,
        ArmorType.Artifact => ArmorWeightClass.Medium,
        _ => ArmorWeightClass.None
    };

    /// <summary>
    /// Get display color for weight class
    /// </summary>
    public static string GetWeightColor(this ArmorWeightClass weight) => weight switch
    {
        ArmorWeightClass.Light => "cyan",
        ArmorWeightClass.Medium => "yellow",
        ArmorWeightClass.Heavy => "red",
        _ => "white"
    };
}
