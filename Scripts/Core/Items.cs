using System.Linq;
using UsurperRemake.Utils;
using Godot;
using System;
using System.Collections.Generic;

/// <summary>
/// Item system based on Pascal ORec structure from INIT.PAS
/// Maintains perfect compatibility with original Usurper item system
/// </summary>
public class Item
{
    // From Pascal ORec structure
    public string Name { get; set; } = "";              // objects name
    public ObjType Type { get; set; }                   // type of object (head, body, weapon, etc.)
    public long Value { get; set; }                     // object value (cost/price)
    public int HP { get; set; }                         // can object increase/decrease hps
    public int Stamina { get; set; }                    // ..stamina
    public int Agility { get; set; }                    // ..agility  
    public int Charisma { get; set; }                   // ..charisma
    public int Dexterity { get; set; }                  // ..dexterity
    public int Wisdom { get; set; }                     // ..wisdom
    public int Mana { get; set; }                       // ..mana
    public int Armor { get; set; }                      // ..can object increase armor value
    public int Attack { get; set; }                     // ..can object increase attack value
    public string Owned { get; set; } = "";             // owned by (character name)
    public bool OnlyOne { get; set; }                   // only one object of its kind?
    public Cures Cure { get; set; }                     // can the object heal?
    public bool Shop { get; set; }                      // is the object available in shoppe
    public bool Dungeon { get; set; }                   // can you find item in dungeons
    public bool Cursed { get; set; }                    // is the item cursed?
    public int MinLevel { get; set; }                   // min level to be found in dungeons
    public int MaxLevel { get; set; }                   // max level to be found in dungeons
    
    // Descriptions (from Pascal: array[1..5] of s70)
    public List<string> Description { get; set; }       // normal description
    public List<string> DetailedDescription { get; set; } // detailed description
    
    public int Strength { get; set; }                   // can object increase/decrease strength
    public int Defence { get; set; }                    // can object increase/decrease defence
    public int StrengthNeeded { get; set; }             // strength needed to wear object
    public bool RequiresGood { get; set; }              // character needs to be good to use
    public bool RequiresEvil { get; set; }              // character needs to be evil to use
    
    // Class restrictions (from Pascal: array[1..global_maxclasses] of boolean)
    public List<bool> ClassRestrictions { get; set; }   // which classes can use this item
    
    // Additional properties for enhanced functionality
    public int ItemID { get; set; }                     // Unique item identifier
    public DateTime CreatedDate { get; set; }           // When item was created
    public string Creator { get; set; } = "";           // Who created the item (for player-made items)
    public bool IsArtifact { get; set; } = false;       // Special unique artifacts
    public int Durability { get; set; } = 100;          // Item durability (0-100)
    public int MaxDurability { get; set; } = 100;       // Maximum durability
    
    // Magic properties
    public MagicEnhancement MagicProperties { get; set; } = new MagicEnhancement();
    public MagicItemType MagicType { get; set; } = MagicItemType.None;
    public bool IsIdentified { get; set; } = true; // Most items start identified
    public bool IsCursed { get; set; } = false;
    public bool OnlyForGood { get; set; } = false; // Good alignment required
    public bool OnlyForEvil { get; set; } = false; // Evil alignment required

    // Loot enchantment effects (v0.40.5) - tracks actual enchantment types from LootGenerator
    // Each entry is (SpecialEffect enum as int, value)
    public List<(int EffectType, int Value)> LootEffects { get; set; } = new();
    
    /// <summary>
    /// Constructor for creating items
    /// </summary>
    public Item()
    {
        Description = new List<string>(new string[5]);
        DetailedDescription = new List<string>(new string[5]);
        ClassRestrictions = new List<bool>(new bool[GameConfig.MaxClasses]);
        CreatedDate = DateTime.Now;
    }
    
    /// <summary>
    /// Check if a character can use this item
    /// </summary>
    public bool CanUse(Character character)
    {
        // Check strength requirement
        if (character.Strength < StrengthNeeded)
            return false;
            
        // Check alignment requirements
        if (RequiresGood && character.Chivalry <= 0)
            return false;
            
        if (RequiresEvil && character.Darkness <= 0)
            return false;
            
        // Check class restrictions
        var classIndex = (int)character.Class;
        if (classIndex < ClassRestrictions.Count && !ClassRestrictions[classIndex])
            return false;
            
        // Check level requirements (for dungeon items)
        if (MinLevel > 0 && character.Level < MinLevel)
            return false;
            
        return true;
    }
    
    /// <summary>
    /// Apply item effects to character when equipped
    /// </summary>
    public void ApplyEffects(Character character)
    {
        // Track if character was at full HP/Mana before applying bonuses
        bool wasAtFullHP = character.HP >= character.MaxHP;
        bool wasAtFullMana = character.Mana >= character.MaxMana;

        character.MaxHP += HP;
        character.MaxMana += Mana;

        // Only increase current HP/Mana if they were at full, or just cap to new max
        if (wasAtFullHP && HP > 0)
            character.HP = character.MaxHP;
        else
            character.HP = Math.Min(character.HP, character.MaxHP);

        if (wasAtFullMana && Mana > 0)
            character.Mana = character.MaxMana;
        else
            character.Mana = Math.Min(character.Mana, character.MaxMana);

        character.Stamina += Stamina;
        character.Agility += Agility;
        character.Charisma += Charisma;
        character.Dexterity += Dexterity;
        character.Wisdom += Wisdom;
        character.Strength += Strength;
        character.Defence += Defence;
        character.ArmPow += Armor;
        character.WeapPow += Attack;
    }
    
    /// <summary>
    /// Remove item effects from character when unequipped
    /// </summary>
    public void RemoveEffects(Character character)
    {
        character.MaxHP -= HP;
        character.HP = Math.Min(character.HP, character.MaxHP);
        character.Stamina -= Stamina;
        character.Agility -= Agility;
        character.Charisma -= Charisma;
        character.Dexterity -= Dexterity;
        character.Wisdom -= Wisdom;
        character.MaxMana -= Mana;
        character.Mana = Math.Min(character.Mana, character.MaxMana);
        character.Strength -= Strength;
        character.Defence -= Defence;
        character.ArmPow -= Armor;
        character.WeapPow -= Attack;
    }
    
    /// <summary>
    /// Get item's display name with condition
    /// </summary>
    public string GetDisplayName()
    {
        var name = Name;
        
        if (Cursed)
            name += " (Cursed)";
            
        if (Durability < 100)
        {
            var condition = Durability switch
            {
                >= 80 => "Good",
                >= 60 => "Fair", 
                >= 40 => "Poor",
                >= 20 => "Bad",
                _ => "Broken"
            };
            name += $" [{condition}]";
        }
        
        if (IsArtifact)
            name += " *";
            
        return name;
    }
    
    /// <summary>
    /// Get full item description for examination
    /// </summary>
    public string GetFullDescription()
    {
        var desc = string.Join("\n", Description.Where(d => !string.IsNullOrEmpty(d)));
        
        if (!string.IsNullOrEmpty(desc))
        {
            desc += "\n\n";
        }
        
        // Add stat information
        var stats = new List<string>();
        
        if (Attack != 0) stats.Add($"Attack: {Attack:+#;-#;0}");
        if (Armor != 0) stats.Add($"Armor: {Armor:+#;-#;0}");
        if (Strength != 0) stats.Add($"Strength: {Strength:+#;-#;0}");
        if (Defence != 0) stats.Add($"Defence: {Defence:+#;-#;0}");
        if (HP != 0) stats.Add($"HP: {HP:+#;-#;0}");
        if (Mana != 0) stats.Add($"Mana: {Mana:+#;-#;0}");
        if (Stamina != 0) stats.Add($"Stamina: {Stamina:+#;-#;0}");
        if (Agility != 0) stats.Add($"Agility: {Agility:+#;-#;0}");
        if (Dexterity != 0) stats.Add($"Dexterity: {Dexterity:+#;-#;0}");
        if (Charisma != 0) stats.Add($"Charisma: {Charisma:+#;-#;0}");
        if (Wisdom != 0) stats.Add($"Wisdom: {Wisdom:+#;-#;0}");
        
        if (stats.Count > 0)
        {
            desc += string.Join(", ", stats) + "\n";
        }
        
        // Add requirements
        var requirements = new List<string>();
        if (StrengthNeeded > 0) requirements.Add($"Str: {StrengthNeeded}");
        if (RequiresGood) requirements.Add("Good alignment");
        if (RequiresEvil) requirements.Add("Evil alignment");
        
        if (requirements.Count > 0)
        {
            desc += "Requires: " + string.Join(", ", requirements) + "\n";
        }
        
        // Add special properties
        if (OnlyOne) desc += "Unique item\n";
        if (Cursed) desc += "This item is cursed!\n";
        if (Cure != Cures.Nothing) desc += $"Cures: {Cure}\n";
        
        return desc.Trim();
    }
    
    /// <summary>
    /// Damage the item (reduce durability)
    /// </summary>
    public void TakeDamage(int damage)
    {
        Durability = Math.Max(0, Durability - damage);
    }
    
    /// <summary>
    /// Repair the item
    /// </summary>
    public void Repair(int amount)
    {
        Durability = Math.Min(MaxDurability, Durability + amount);
    }
    
    /// <summary>
    /// Check if item is broken
    /// </summary>
    public bool IsBroken => Durability <= 0;

    // Compatibility aliases expected by newer location/shop code
    /// <summary>
    /// Newer modules use <c>IsShopItem</c>; map it to the legacy <c>Shop</c> flag.
    /// </summary>
    public bool IsShopItem
    {
        get => Shop;
        set => Shop = value;
    }

    /// <summary>
    /// Some shop calculations refer to <c>StrengthRequired</c> – this is equivalent to the
    /// original <c>StrengthNeeded</c> field (kept for Pascal compatibility).
    /// Using <see langword="long"/> keeps the same signature as the simplified <c>Item</c> shim.
    /// </summary>
    public long StrengthRequired
    {
        get => StrengthNeeded;
        set => StrengthNeeded = (int)value;
    }

    /// <summary>
    /// Quick shallow-copy helper used by shop/quest code to duplicate items.
    /// </summary>
    public Item Clone() => (Item) MemberwiseClone();
}

/// <summary>
/// Classic weapon from Pascal WeapRec structure
/// </summary>
public class ClassicWeapon
{
    public string Name { get; set; } = "";              // name of weapon
    public long Value { get; set; }                     // value
    public long Power { get; set; }                     // power
    
    public ClassicWeapon(string name, long value, long power)
    {
        Name = name;
        Value = value;
        Power = power;
    }
}

/// <summary>
/// Classic armor from Pascal ArmRec structure  
/// </summary>
public class ClassicArmor
{
    public string Name { get; set; } = "";              // name of armor
    public long Value { get; set; }                     // value
    public long Power { get; set; }                     // power
    
    public ClassicArmor(string name, long value, long power)
    {
        Name = name;
        Value = value; 
        Power = power;
    }
}

/// <summary>
/// Item manager for handling all game items
/// </summary>
public static class ItemManager
{
    private static Dictionary<int, Item> gameItems = new Dictionary<int, Item>();
    private static Dictionary<int, ClassicWeapon> classicWeapons = new Dictionary<int, ClassicWeapon>();
    private static Dictionary<int, ClassicArmor> classicArmor = new Dictionary<int, ClassicArmor>();
    
    /// <summary>
    /// Initialize items from Pascal data - based on Init_Items procedure
    /// </summary>
    public static void InitializeItems()
    {
        InitializeClassicWeapons();
        InitializeClassicArmor();
        InitializeSpecialItems();
    }
    
    /// <summary>
    /// Initialize classic weapons from Pascal weapon array
    /// </summary>
    private static void InitializeClassicWeapons()
    {
        // From Pascal INIT.PAS weapon initialization
        // weapon[x].name := 'Weapon Name'; weapon[x].value := cost; weapon[x].pow := damage;
        
        var weapons = new (string name, long value, long power)[]
        {
            ("Fists", 0, 1),
            ("Stick", 10, 2),
            ("Dagger", 25, 3),
            ("Club", 50, 4),
            ("Short Sword", 100, 5),
            ("Mace", 200, 6),
            ("Long Sword", 400, 7),
            ("Broad Sword", 800, 8),
            ("Battle Axe", 1500, 9),
            ("Two-Handed Sword", 3000, 10),
            ("War Hammer", 5000, 11),
            ("Halberd", 8000, 12),
            ("Bastard Sword", 12000, 13),
            ("Great Sword", 18000, 14),
            ("Executioner's Axe", 25000, 15),
            // ... continue for all 35 weapons from Pascal
        };
        
        for (int i = 0; i < weapons.Length; i++)
        {
            classicWeapons[i] = new ClassicWeapon(weapons[i].name, weapons[i].value, weapons[i].power);
        }
    }
    
    /// <summary>
    /// Initialize classic armor from Pascal armor array
    /// Extended to 50 armors for comprehensive progression
    /// </summary>
    private static void InitializeClassicArmor()
    {
        // Expanded armor list from basic to legendary (50 total)
        var armor = new (string name, long value, long power)[]
        {
            // Basic armors (AC 0-5)
            ("Skin", 0, 0),
            ("Cloth Rags", 25, 1),
            ("Padded Cloth", 50, 2),
            ("Leather Tunic", 100, 3),
            ("Hardened Leather", 200, 4),
            ("Studded Leather", 400, 5),

            // Light mail (AC 6-10)
            ("Ring Mail", 750, 6),
            ("Scale Mail", 1200, 7),
            ("Chain Shirt", 2000, 8),
            ("Chain Mail", 3200, 9),
            ("Reinforced Chain", 5000, 10),

            // Medium armor (AC 11-15)
            ("Splint Mail", 7500, 11),
            ("Banded Mail", 11000, 12),
            ("Bronze Plate", 16000, 13),
            ("Steel Plate", 22000, 14),
            ("Plate Mail", 30000, 15),

            // Heavy armor (AC 16-20)
            ("Field Plate", 40000, 16),
            ("Full Plate", 52000, 17),
            ("Master Plate", 66000, 18),
            ("Knight's Plate", 82000, 19),
            ("Royal Plate", 100000, 20),

            // Enchanted armor (AC 21-25)
            ("Plate of Valor", 125000, 21),
            ("Blessed Plate", 155000, 22),
            ("Plate of Honor", 190000, 23),
            ("Sacred Plate", 230000, 24),
            ("Holy Plate", 275000, 25),

            // Dark armors (AC 26-30)
            ("Shadow Plate", 330000, 26),
            ("Demon Plate", 390000, 27),
            ("Cursed Plate", 460000, 28),
            ("Infernal Plate", 540000, 29),
            ("Abyssal Armor", 630000, 30),

            // Dragon armors (AC 31-35)
            ("Dragon Scale", 730000, 31),
            ("Wyrm Scale", 840000, 32),
            ("Ancient Dragon Hide", 960000, 33),
            ("Great Wyrm Armor", 1100000, 34),
            ("Elder Dragon Plate", 1250000, 35),

            // Celestial armors (AC 36-40)
            ("Celestial Armor", 1420000, 36),
            ("Heavenly Plate", 1600000, 37),
            ("Angelic Armor", 1800000, 38),
            ("Seraphim Plate", 2050000, 39),
            ("Divine Protection", 2300000, 40),

            // Legendary armors (AC 41-45)
            ("Titan Armor", 2600000, 41),
            ("Godforged Plate", 2950000, 42),
            ("Eternal Guardian", 3350000, 43),
            ("Mythril Armor", 3800000, 44),
            ("Adamantine Plate", 4300000, 45),

            // Ultimate armors (AC 46-50)
            ("Supreme Protection", 4900000, 46),
            ("Armor of the Ancients", 5600000, 47),
            ("Immortal's Shell", 6400000, 48),
            ("Armor of Eternity", 7300000, 49),
            ("Ultimate Defense", 8500000, 50)
        };

        for (int i = 0; i < armor.Length; i++)
        {
            classicArmor[i + 1] = new ClassicArmor(armor[i].name, armor[i].value, armor[i].power);
        }
    }
    
    /// <summary>
    /// Initialize special items and artifacts
    /// </summary>
    private static void InitializeSpecialItems()
    {
        // Supreme Being items (from Pascal global_s_* constants)
        CreateSupremeItem(1001, "Lantern of Eternal Light", ObjType.Weapon, 
            "A mystical lantern that never dims", true);
            
        CreateSupremeItem(1002, "Sword of Supreme Justice", ObjType.Weapon,
            "The ultimate weapon of righteousness", true);
            
        CreateSupremeItem(1003, "Staff of Black Magic", ObjType.Weapon,
            "A staff that channels dark powers", true);
            
        CreateSupremeItem(1004, "Staff of White Magic", ObjType.Weapon,
            "A staff blessed with holy power", true);
    }
    
    /// <summary>
    /// Create a supreme being item
    /// </summary>
    private static void CreateSupremeItem(int id, string name, ObjType type, string description, bool artifact)
    {
        var item = new Item
        {
            ItemID = id,
            Name = name,
            Type = type,
            Value = 999999,
            Attack = type == ObjType.Weapon ? 50 : 0,
            Armor = type != ObjType.Weapon ? 50 : 0,
            OnlyOne = true,
            IsArtifact = artifact,
            MinLevel = 100,
            RequiresGood = name.Contains("White") || name.Contains("Justice"),
            RequiresEvil = name.Contains("Black"),
            StrengthNeeded = 25
        };
        
        item.Description[0] = description;
        item.Description[1] = "This legendary item pulses with incredible power.";
        
        gameItems[id] = item;
    }
    
    /// <summary>
    /// Get item by ID
    /// </summary>
    public static Item? GetItem(int itemId)
    {
        return gameItems.ContainsKey(itemId) ? gameItems[itemId] : null;
    }
    
    /// <summary>
    /// Get classic weapon by index (Pascal compatible)
    /// </summary>
    public static ClassicWeapon? GetClassicWeapon(int index)
    {
        return classicWeapons.ContainsKey(index) ? classicWeapons[index] : null;
    }
    
    /// <summary>
    /// Get classic armor by index (Pascal compatible)
    /// </summary>
    public static ClassicArmor? GetClassicArmor(int index)
    {
        return classicArmor.ContainsKey(index) ? classicArmor[index] : null;
    }
    
    /// <summary>
    /// Get all items of a specific type
    /// </summary>
    public static List<Item> GetItemsByType(ObjType type)
    {
        return gameItems.Values.Where(item => item.Type == type).ToList();
    }
    
    /// <summary>
    /// Create a new item (for dynamic item creation)
    /// </summary>
    public static Item CreateItem(string name, ObjType type, long value)
    {
        var item = new Item
        {
            ItemID = gameItems.Count + 10000, // Avoid conflicts with static items
            Name = name,
            Type = type,
            Value = value,
            CreatedDate = DateTime.Now
        };
        
        gameItems[item.ItemID] = item;
        return item;
    }
    
    // Shop-related methods for weapon and armor shops
    
    /// <summary>
    /// Get weapon by ID (for weapon shop)
    /// </summary>
    public static ClassicWeapon? GetWeapon(int weaponId)
    {
        return classicWeapons.ContainsKey(weaponId) ? classicWeapons[weaponId] : null;
    }
    
    /// <summary>
    /// Get armor by ID (for armor shop)  
    /// </summary>
    public static ClassicArmor? GetArmor(int armorId)
    {
        return classicArmor.ContainsKey(armorId) ? classicArmor[armorId] : null;
    }
    
    /// <summary>
    /// Get all weapons available in shops
    /// </summary>
    public static List<ClassicWeapon> GetShopWeapons()
    {
        return classicWeapons.Values.OrderBy(w => w.Value).ToList();
    }
    
    /// <summary>
    /// Get all armors available in shops
    /// </summary>
    public static List<ClassicArmor> GetShopArmors()
    {
        return classicArmor.Values.OrderBy(a => a.Value).ToList();
    }
    
    /// <summary>
    /// Get armors by equipment type (simplified for classic mode)
    /// </summary>
    public static List<ClassicArmor> GetArmorsByType(ObjType armorType)
    {
        // In classic mode, all armors are body armor
        return GetShopArmors();
    }
    
    /// <summary>
    /// Get best affordable weapon for player
    /// </summary>
    public static ClassicWeapon? GetBestAffordableWeapon(long maxGold, Character player)
    {
        return classicWeapons.Values
            .Where(w => w.Value <= maxGold)
            .OrderByDescending(w => w.Power)
            .ThenByDescending(w => w.Value)
            .FirstOrDefault();
    }
    
    /// <summary>
    /// Get best affordable armor for specific slot
    /// </summary>
    public static ClassicArmor? GetBestAffordableArmor(ObjType armorType, long maxGold, Character player)
    {
        return classicArmor.Values
            .Where(a => a.Value <= maxGold)
            .OrderByDescending(a => a.Power)
            .ThenByDescending(a => a.Value)
            .FirstOrDefault();
    }
}

/// <summary>
/// Magic item types for shop categorization
/// </summary>
public enum MagicItemType
{
    None = 0,
    Neck = 10,      // Amulets, necklaces
    Fingers = 5,    // Rings  
    Waist = 9       // Belts, girdles
}

/// <summary>
/// Disease cure types for magic items
/// </summary>
public enum CureType
{
    None = 0,
    All,        // Cures all diseases
    Blindness,  // Cures blindness
    Plague,     // Cures plague
    Smallpox,   // Cures smallpox
    Measles,    // Cures measles
    Leprosy     // Cures leprosy
}

/// <summary>
/// Magic enhancement properties for items
/// </summary>
public class MagicEnhancement
{
    public int Mana { get; set; }           // Mana bonus/penalty
    public int Wisdom { get; set; }         // Wisdom bonus/penalty
    public int Dexterity { get; set; }      // Dexterity bonus/penalty
    public int MagicResistance { get; set; } // Resistance to spells
    public CureType DiseaseImmunity { get; set; } // Disease protection
    public bool AntiMagic { get; set; }     // Blocks all magic
    public bool SpellReflection { get; set; } // Reflects spells back

    public MagicEnhancement()
    {
        DiseaseImmunity = CureType.None;
    }
}

/// <summary>
/// Modern equipment item with slot types, weapon handedness, and full stat bonuses
/// Used by the new equipment system for armor pieces, weapons, and accessories
/// </summary>
public class Equipment
{
    // Identity
    public int Id { get; set; }
    public string Name { get; set; } = "";
    public string Description { get; set; } = "";

    // Classification
    public EquipmentSlot Slot { get; set; } = EquipmentSlot.None;
    public WeaponHandedness Handedness { get; set; } = WeaponHandedness.None;
    public WeaponType WeaponType { get; set; } = WeaponType.None;
    public ArmorType ArmorType { get; set; } = ArmorType.None;
    public EquipmentRarity Rarity { get; set; } = EquipmentRarity.Common;

    // Economics
    public long Value { get; set; }         // Buy price
    public long SellValue => Value / 2;     // Sell price (50%)

    // Combat stats
    public int WeaponPower { get; set; }    // Damage bonus (for weapons)
    public int ArmorClass { get; set; }     // AC bonus (for armor)
    public int ShieldBonus { get; set; }    // Block AC bonus (for shields)
    public int BlockChance { get; set; }    // % chance to block (shields)

    // Primary stat bonuses
    public int StrengthBonus { get; set; }
    public int DexterityBonus { get; set; }
    public int ConstitutionBonus { get; set; }
    public int IntelligenceBonus { get; set; }
    public int WisdomBonus { get; set; }
    public int CharismaBonus { get; set; }

    // Secondary stat bonuses
    public int MaxHPBonus { get; set; }
    public int MaxManaBonus { get; set; }
    public int DefenceBonus { get; set; }   // Flat defence bonus
    public int StaminaBonus { get; set; }
    public int AgilityBonus { get; set; }

    // Special properties
    public int CriticalChanceBonus { get; set; }    // % bonus to crit
    public int CriticalDamageBonus { get; set; }    // % bonus to crit damage
    public int MagicResistance { get; set; }        // % magic damage reduction
    public int PoisonDamage { get; set; }           // Poison on hit
    public int LifeSteal { get; set; }              // % damage as healing

    // Restrictions
    public int MinLevel { get; set; } = 1;
    public int StrengthRequired { get; set; }
    public bool RequiresGood { get; set; }          // Good alignment only
    public bool RequiresEvil { get; set; }          // Evil alignment only
    public List<CharacterClass> ClassRestrictions { get; set; } = new();

    // Status
    public bool IsCursed { get; set; }
    public bool IsIdentified { get; set; } = true;
    public bool IsUnique { get; set; }              // Only one can exist

    // Elemental enchant flags (v0.30.9+)
    public bool HasFireEnchant { get; set; }
    public bool HasFrostEnchant { get; set; }
    public bool HasLightningEnchant { get; set; }  // Chance to stun
    public bool HasPoisonEnchant { get; set; }     // Poison DoT
    public bool HasHolyEnchant { get; set; }       // Bonus vs undead
    public bool HasShadowEnchant { get; set; }     // Bonus vs living

    // Proc-based enchantments (v0.40.5) - from loot effects
    public int ManaSteal { get; set; }             // % mana restore on hit
    public int ArmorPiercing { get; set; }         // % armor ignore
    public int Thorns { get; set; }                // % damage reflected to attacker
    public int HPRegen { get; set; }               // HP restored per combat round
    public int ManaRegen { get; set; }             // Mana restored per combat round

    /// <summary>
    /// Create a new equipment item
    /// </summary>
    public Equipment() { }

    /// <summary>
    /// Create equipment with basic properties
    /// </summary>
    public static Equipment Create(int id, string name, EquipmentSlot slot, long value)
    {
        return new Equipment
        {
            Id = id,
            Name = name,
            Slot = slot,
            Value = value
        };
    }

    /// <summary>
    /// Create a weapon
    /// </summary>
    public static Equipment CreateWeapon(int id, string name, WeaponHandedness handedness,
        WeaponType weaponType, int power, long value, EquipmentRarity rarity = EquipmentRarity.Common)
    {
        return new Equipment
        {
            Id = id,
            Name = name,
            Slot = handedness == WeaponHandedness.OffHandOnly ? EquipmentSlot.OffHand : EquipmentSlot.MainHand,
            Handedness = handedness,
            WeaponType = weaponType,
            WeaponPower = power,
            Value = value,
            Rarity = rarity,
            MinLevel = GetMinLevelForRarity(rarity)
        };
    }

    /// <summary>
    /// Create a shield
    /// </summary>
    public static Equipment CreateShield(int id, string name, int shieldBonus, int blockChance,
        long value, EquipmentRarity rarity = EquipmentRarity.Common)
    {
        return new Equipment
        {
            Id = id,
            Name = name,
            Slot = EquipmentSlot.OffHand,
            Handedness = WeaponHandedness.OffHandOnly,
            WeaponType = WeaponType.Shield,
            ShieldBonus = shieldBonus,
            BlockChance = blockChance,
            Value = value,
            Rarity = rarity,
            MinLevel = GetMinLevelForRarity(rarity)
        };
    }

    /// <summary>
    /// Create armor for a specific slot
    /// </summary>
    public static Equipment CreateArmor(int id, string name, EquipmentSlot slot,
        ArmorType armorType, int ac, long value, EquipmentRarity rarity = EquipmentRarity.Common)
    {
        return new Equipment
        {
            Id = id,
            Name = name,
            Slot = slot,
            ArmorType = armorType,
            ArmorClass = ac,
            Value = value,
            Rarity = rarity,
            MinLevel = GetMinLevelForRarity(rarity)
        };
    }

    /// <summary>
    /// Create an accessory (ring, amulet)
    /// </summary>
    public static Equipment CreateAccessory(int id, string name, EquipmentSlot slot,
        long value, EquipmentRarity rarity = EquipmentRarity.Common)
    {
        return new Equipment
        {
            Id = id,
            Name = name,
            Slot = slot,
            Value = value,
            Rarity = rarity,
            MinLevel = GetMinLevelForRarity(rarity)
        };
    }

    /// <summary>
    /// Get minimum level requirement based on equipment rarity.
    /// Epic items require level 45, Legendary items require level 65.
    /// </summary>
    private static int GetMinLevelForRarity(EquipmentRarity rarity)
    {
        return rarity switch
        {
            EquipmentRarity.Epic => 45,
            EquipmentRarity.Legendary => 65,
            _ => 1  // Common, Uncommon, Rare have no level requirement
        };
    }

    /// <summary>
    /// Calculate a minimum level requirement based on item power.
    /// Prevents low-level players from equipping absurdly powerful gear
    /// regardless of how they acquired it (auction house, trading, etc.)
    /// Formula: MinLevel = max(1, power / 10) where power = max(WeaponPower, ArmorClass)
    /// </summary>
    public static int CalculateMinLevelFromPower(Equipment equip)
    {
        int power = Math.Max(equip.WeaponPower, equip.ArmorClass);
        if (power <= 15) return 1; // Starter gear has no restriction
        return Math.Min(100, Math.Max(1, power / 10));
    }

    /// <summary>
    /// Ensure MinLevel is at least as high as the power-based floor.
    /// Call this after creating Equipment to prevent overpowered gear at low levels.
    /// </summary>
    public void EnforceMinLevelFromPower()
    {
        int powerMinLevel = CalculateMinLevelFromPower(this);
        MinLevel = Math.Max(MinLevel, powerMinLevel);
    }

    /// <summary>
    /// Apply equipment bonuses to a character
    /// </summary>
    public void ApplyToCharacter(Character character)
    {
        if (character == null) return;

        // Combat stats
        character.WeapPow += WeaponPower;
        character.ArmPow += ArmorClass + ShieldBonus;

        // Primary stats
        character.Strength += StrengthBonus;
        character.Dexterity += DexterityBonus;
        character.Constitution += ConstitutionBonus;
        character.Intelligence += IntelligenceBonus;
        character.Wisdom += WisdomBonus;
        character.Charisma += CharismaBonus;

        // Secondary stats
        character.MaxHP += MaxHPBonus;
        character.MaxMana += MaxManaBonus;
        character.Defence += DefenceBonus;
        character.Stamina += StaminaBonus;
        character.Agility += AgilityBonus;

        // Keep HP/Mana within bounds
        character.HP = Math.Min(character.HP, character.MaxHP);
        character.Mana = Math.Min(character.Mana, character.MaxMana);
    }

    /// <summary>
    /// Remove equipment bonuses from a character
    /// </summary>
    public void RemoveFromCharacter(Character character)
    {
        if (character == null) return;

        // Combat stats (guard against underflow)
        character.WeapPow = Math.Max(0, character.WeapPow - WeaponPower);
        character.ArmPow = Math.Max(0, character.ArmPow - ArmorClass - ShieldBonus);

        // Primary stats (guard against underflow - stats shouldn't go below 1)
        character.Strength = Math.Max(1, character.Strength - StrengthBonus);
        character.Dexterity = Math.Max(1, character.Dexterity - DexterityBonus);
        character.Constitution = Math.Max(1, character.Constitution - ConstitutionBonus);
        character.Intelligence = Math.Max(1, character.Intelligence - IntelligenceBonus);
        character.Wisdom = Math.Max(1, character.Wisdom - WisdomBonus);
        character.Charisma = Math.Max(1, character.Charisma - CharismaBonus);

        // Secondary stats (guard against underflow)
        character.MaxHP = Math.Max(1, character.MaxHP - MaxHPBonus);
        character.MaxMana = Math.Max(0, character.MaxMana - MaxManaBonus);
        character.Defence = Math.Max(0, character.Defence - DefenceBonus);
        character.Stamina = Math.Max(0, character.Stamina - StaminaBonus);
        character.Agility = Math.Max(0, character.Agility - AgilityBonus);

        // Keep HP/Mana within bounds
        character.HP = Math.Min(character.HP, character.MaxHP);
        character.Mana = Math.Min(character.Mana, character.MaxMana);
    }

    /// <summary>
    /// Check if character meets requirements to equip this item
    /// </summary>
    public bool CanEquip(Character character, out string reason)
    {
        reason = "";

        if (character.Level < MinLevel)
        {
            reason = $"Requires level {MinLevel}";
            return false;
        }

        if (character.Strength < StrengthRequired)
        {
            reason = $"Requires {StrengthRequired} Strength";
            return false;
        }

        if (RequiresGood && character.Chivalry <= character.Darkness)
        {
            reason = "Requires good alignment";
            return false;
        }

        if (RequiresEvil && character.Darkness <= character.Chivalry)
        {
            reason = "Requires evil alignment";
            return false;
        }

        if (ClassRestrictions.Count > 0 && !ClassRestrictions.Contains(character.Class))
        {
            reason = $"Cannot be used by {character.Class}";
            return false;
        }

        return true;
    }

    /// <summary>
    /// Get display string with stats
    /// </summary>
    public string GetDisplayString()
    {
        var stats = new List<string>();

        if (WeaponPower > 0) stats.Add($"Pow: {WeaponPower}");
        if (ArmorClass > 0) stats.Add($"AC: {ArmorClass}");
        if (ShieldBonus > 0) stats.Add($"Block: +{ShieldBonus}");

        if (StrengthBonus != 0) stats.Add($"Str: {StrengthBonus:+#;-#;0}");
        if (DexterityBonus != 0) stats.Add($"Dex: {DexterityBonus:+#;-#;0}");
        if (WisdomBonus != 0) stats.Add($"Wis: {WisdomBonus:+#;-#;0}");
        if (MaxHPBonus != 0) stats.Add($"HP: {MaxHPBonus:+#;-#;0}");
        if (MaxManaBonus != 0) stats.Add($"Mana: {MaxManaBonus:+#;-#;0}");

        return stats.Count > 0 ? string.Join(", ", stats) : "No bonuses";
    }

    /// <summary>
    /// Get color for rarity display
    /// </summary>
    public string GetRarityColor() => Rarity switch
    {
        EquipmentRarity.Common => "white",
        EquipmentRarity.Uncommon => "green",
        EquipmentRarity.Rare => "cyan",
        EquipmentRarity.Epic => "magenta",
        EquipmentRarity.Legendary => "yellow",
        EquipmentRarity.Artifact => "bright_yellow",
        _ => "white"
    };

    /// <summary>
    /// Create a deep copy of this equipment item (for enchanting system)
    /// </summary>
    public Equipment Clone()
    {
        return new Equipment
        {
            // Identity (Id intentionally NOT copied - will be assigned by RegisterDynamic)
            Name = this.Name,
            Description = this.Description,
            // Classification
            Slot = this.Slot,
            Handedness = this.Handedness,
            WeaponType = this.WeaponType,
            ArmorType = this.ArmorType,
            Rarity = this.Rarity,
            // Economics
            Value = this.Value,
            // Combat stats
            WeaponPower = this.WeaponPower,
            ArmorClass = this.ArmorClass,
            ShieldBonus = this.ShieldBonus,
            BlockChance = this.BlockChance,
            // Primary stat bonuses
            StrengthBonus = this.StrengthBonus,
            DexterityBonus = this.DexterityBonus,
            ConstitutionBonus = this.ConstitutionBonus,
            IntelligenceBonus = this.IntelligenceBonus,
            WisdomBonus = this.WisdomBonus,
            CharismaBonus = this.CharismaBonus,
            // Secondary stat bonuses
            MaxHPBonus = this.MaxHPBonus,
            MaxManaBonus = this.MaxManaBonus,
            DefenceBonus = this.DefenceBonus,
            StaminaBonus = this.StaminaBonus,
            AgilityBonus = this.AgilityBonus,
            // Special properties
            CriticalChanceBonus = this.CriticalChanceBonus,
            CriticalDamageBonus = this.CriticalDamageBonus,
            MagicResistance = this.MagicResistance,
            PoisonDamage = this.PoisonDamage,
            LifeSteal = this.LifeSteal,
            // Restrictions
            MinLevel = this.MinLevel,
            StrengthRequired = this.StrengthRequired,
            RequiresGood = this.RequiresGood,
            RequiresEvil = this.RequiresEvil,
            ClassRestrictions = new List<CharacterClass>(this.ClassRestrictions),
            // Status
            IsCursed = this.IsCursed,
            IsIdentified = this.IsIdentified,
            IsUnique = false, // Enchanted copies are never unique
        };
    }

    /// <summary>
    /// Get the number of enchantments applied to this item.
    /// Tracked via [E:N] marker in the Description field.
    /// </summary>
    public int GetEnchantmentCount()
    {
        if (string.IsNullOrEmpty(Description)) return 0;
        var match = System.Text.RegularExpressions.Regex.Match(Description, @"\[E:(\d+)\]");
        return match.Success ? int.Parse(match.Groups[1].Value) : 0;
    }

    /// <summary>
    /// Increment the enchantment counter in the Description field.
    /// </summary>
    public void IncrementEnchantmentCount()
    {
        int current = GetEnchantmentCount();
        string marker = $"[E:{current + 1}]";
        if (current > 0)
            Description = System.Text.RegularExpressions.Regex.Replace(Description, @"\[E:\d+\]", marker);
        else
            Description = string.IsNullOrEmpty(Description) ? marker : Description + " " + marker;
    }

    #region Fluent Setters (for builder pattern)

    // Primary stat bonuses
    public Equipment WithStrength(int bonus) { StrengthBonus = bonus; return this; }
    public Equipment WithDexterity(int bonus) { DexterityBonus = bonus; return this; }
    public Equipment WithConstitution(int bonus) { ConstitutionBonus = bonus; return this; }
    public Equipment WithIntelligence(int bonus) { IntelligenceBonus = bonus; return this; }
    public Equipment WithWisdom(int bonus) { WisdomBonus = bonus; return this; }
    public Equipment WithCharisma(int bonus) { CharismaBonus = bonus; return this; }

    // Secondary stat bonuses
    public Equipment WithMaxHP(int bonus) { MaxHPBonus = bonus; return this; }
    public Equipment WithMaxMana(int bonus) { MaxManaBonus = bonus; return this; }
    public Equipment WithDefence(int bonus) { DefenceBonus = bonus; return this; }
    public Equipment WithStamina(int bonus) { StaminaBonus = bonus; return this; }
    public Equipment WithAgility(int bonus) { AgilityBonus = bonus; return this; }

    // Special properties
    public Equipment WithCritChance(int bonus) { CriticalChanceBonus = bonus; return this; }
    public Equipment WithCritDamage(int bonus) { CriticalDamageBonus = bonus; return this; }
    public Equipment WithMagicResist(int percent) { MagicResistance = percent; return this; }
    public Equipment WithPoison(int damage) { PoisonDamage = damage; return this; }
    public Equipment WithLifeSteal(int percent) { LifeSteal = percent; return this; }

    // Requirements
    public Equipment RequiresLevel(int level) { MinLevel = level; return this; }
    public Equipment RequiresStr(int str) { StrengthRequired = str; return this; }
    public Equipment RequiresGoodAlignment() { RequiresGood = true; return this; }
    public Equipment RequiresEvilAlignment() { RequiresEvil = true; return this; }
    public Equipment ForClasses(params CharacterClass[] classes) { ClassRestrictions.AddRange(classes); return this; }

    // Status flags
    public Equipment AsCursed() { IsCursed = true; return this; }
    public Equipment AsUnique() { IsUnique = true; return this; }
    public Equipment Unidentified() { IsIdentified = false; return this; }
    public Equipment WithDescription(string desc) { Description = desc; return this; }

    #endregion
} 
