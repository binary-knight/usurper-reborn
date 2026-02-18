using UsurperRemake.Utils;
using UsurperRemake.Systems;
using Godot;
using System;
using System.Collections.Generic;

/// <summary>
/// Character class based directly on Pascal UserRec structure from INIT.PAS
/// This maintains perfect compatibility with the original game data
/// </summary>
public class Character
{
    // Basic character info - from Pascal UserRec
    public string Name1 { get; set; } = "";        // bbs/real name
    public string Name2 { get; set; } = "";        // game alias (this is the main name used)
    public CharacterAI AI { get; set; }             // (C)omputer or (H)uman
    public CharacterRace Race { get; set; }         // races
    public int Age { get; set; }                    // age
    public long Gold { get; set; }                  // gold in hand
    public long HP { get; set; }                    // hitpoints
    public long Experience { get; set; }            // experience
    public int Level { get; set; } = 1;
    public long BankGold { get; set; }              // gold in bank
    public long Chivalry { get; set; }              // chivalry
    public long Darkness { get; set; }              // darkness
    public int Fights { get; set; }                 // dungeon fights
    public long Strength { get; set; }              // strength
    public long Defence { get; set; }               // defence
    public long Healing { get; set; }               // healing potions
    public long ManaPotions { get; set; }            // mana potions (bought at Magic Shop)
    public int MaxPotions => 20 + (Level - 1);      // max potions = 20 + (level - 1)
    public int MaxManaPotions => 20 + (Level - 1);   // max mana potions (scales with level like healing potions)
    public bool Allowed { get; set; }               // allowed to play
    public long MaxHP { get; set; }                 // max hitpoints
    public long LastOn { get; set; }                // laston, date
    public int AgePlus { get; set; }                // how soon before getting one year older
    public int DarkNr { get; set; }                 // dark deeds left
    public int ChivNr { get; set; }                 // good deeds left
    public int PFights { get; set; }                // player fights
    public bool King { get; set; }                  // king?
    public int Location { get; set; }               // offline location
    public virtual string CurrentLocation { get; set; } = ""; // current location as string (for display/AI)
    public string Team { get; set; } = "";          // team name
    public string TeamPW { get; set; } = "";        // team password
    public int TeamRec { get; set; }                // team record, days had town
    public int BGuard { get; set; }                 // type of guard
    public bool CTurf { get; set; }                 // is team in control of town
    public int GnollP { get; set; }                 // gnoll poison, temporary
    public int Mental { get; set; }                 // mental health
    public int Addict { get; set; }                 // drug addiction level (0-100)
    public int SteroidDays { get; set; }            // days remaining on steroids
    public int DrugEffectDays { get; set; }         // days remaining on drug effects
    public DrugType ActiveDrug { get; set; }        // currently active drug type
    public bool WellWish { get; set; }              // has visited wishing well
    public int Height { get; set; }                 // height
    public int Weight { get; set; }                 // weight
    public int Eyes { get; set; }                   // eye color
    public int Hair { get; set; }                   // hair color
    public int Skin { get; set; }                   // skin color
    public CharacterSex Sex { get; set; }           // sex, male=1 female=2
    public long Mana { get; set; }                  // mana, spellcasters only
    public long MaxMana { get; set; }               // maxmana
    public long Stamina { get; set; }               // stamina
    public long Agility { get; set; }               // agility
    public long Charisma { get; set; }              // charisma
    public long Dexterity { get; set; }             // dexterity
    public long Wisdom { get; set; }                // wisdom
    public long Intelligence { get; set; }          // intelligence
    public long Constitution { get; set; }          // constitution  
    public long WeapPow { get; set; }               // weapon power
    public long ArmPow { get; set; }                // armor power
    
    // Disease status
    public bool Blind { get; set; }                 // blind?
    public bool Plague { get; set; }                // plague?
    public bool Smallpox { get; set; }              // smallpox?
    public bool Measles { get; set; }               // measles?
    public bool Leprosy { get; set; }               // leprosy?
    public bool LoversBane { get; set; }            // STD from Love Street
    public int Mercy { get; set; }                  // mercy??
    
    // Inventory - array from Pascal
    public List<int> Item { get; set; } = new List<int>();             // inventory items (item IDs)
    public List<ObjType> ItemType { get; set; } = new List<ObjType>(); // type of items in inventory
    
    // Phrases used in different situations (6 phrases from Pascal)
    public List<string> Phrases { get; set; }       // phr array[1..6]
    /*
     * 1. what to say when being attacked
     * 2. what to say when you have defeated somebody
     * 3. what to say when you have been defeated
     * 4. what to say when you are begging for mercy
     * 5. what to say when you spare opponents life
     * 6. what to say when you don't spare opponents life
     */
    
    public bool DevMenuUsed { get; set; }            // permanently disables Steam achievements
    public bool AutoHeal { get; set; }              // autoheal in battle?
    public CombatSpeed CombatSpeed { get; set; } = CombatSpeed.Normal;  // combat text speed
    public bool SkipIntimateScenes { get; set; }    // skip detailed intimate scenes (fade to black)
    public bool ScreenReaderMode { get; set; }      // simplified text output for screen readers (accessibility)
    public ColorThemeType ColorTheme { get; set; } = ColorThemeType.Default;  // player-selected color theme
    public CharacterClass Class { get; set; }       // class
    public int Loyalty { get; set; }                // loyalty% (0-100)
    public int Haunt { get; set; }                  // how many demons haunt player
    public char Master { get; set; }                // level master player uses
    public int TFights { get; set; }                // team fights left
    public int Thiefs { get; set; }                 // thieveries left
    public int Brawls { get; set; }                 // brawls left
    public int Assa { get; set; }                   // assassinations left
    
    // Player description (4 lines from Pascal)
    public List<string> Description { get; set; }   // desc array[1..4]
    
    public int Poison { get; set; }                 // poison, adds to weapon
    
    // Spells (from Pascal: array[1..global_maxspells, 1..2] of boolean)
    public List<List<bool>> Spell { get; set; }     // spells [spell][known/mastered]

    // Learned combat abilities (non-caster classes)
    public HashSet<string> LearnedAbilities { get; set; } = new();

    /// <summary>
    /// Combat quickbar slots 1-9. Stores spell IDs ("spell:5") or ability IDs ("power_strike").
    /// Only equipped skills are usable in combat. Null = empty slot.
    /// </summary>
    public List<string?> Quickbar { get; set; } = new(new string?[9]);

    // Close combat skills (from Pascal: array[1..global_maxcombat] of int)
    public List<int> Skill { get; set; }            // close combat skills
    
    public int Trains { get; set; }                 // training sessions
    
    // Equipment slots (item pointers from Pascal)
    public int LHand { get; set; }                  // item in left hand
    public int RHand { get; set; }                  // item in right hand
    public int Head { get; set; }                   // head
    public int Body { get; set; }                   // body
    public int Arms { get; set; }                   // arms
    public int LFinger { get; set; }                // left finger
    public int RFinger { get; set; }                // right finger
    public int Legs { get; set; }                   // legs
    public int Feet { get; set; }                   // feet
    public int Waist { get; set; }                  // waist
    public int Neck { get; set; }                   // neck
    public int Neck2 { get; set; }                  // neck2
    public int Face { get; set; }                   // face
    public int Shield { get; set; }                 // shield
    public int Hands { get; set; }                  // hands
    public int ABody { get; set; }                  // around body
    
    public bool Immortal { get; set; }              // never deleted for inactivity
    public string BattleCry { get; set; } = "";     // battle cry
    public int BGuardNr { get; set; }               // number of doorguards

    // Difficulty mode (set at character creation)
    public DifficultyMode Difficulty { get; set; } = DifficultyMode.Normal;

    // Player statistics tracking
    public PlayerStatistics Statistics { get; set; } = new PlayerStatistics();

    // Achievement tracking
    public PlayerAchievements Achievements { get; set; } = new PlayerAchievements();

    // Hint system - tracks which contextual hints have been shown to this player
    public HashSet<string> HintsShown { get; set; } = new HashSet<string>();

    // Divine Wrath System - tracks when player angers their god by worshipping another
    public int DivineWrathLevel { get; set; } = 0;           // 0 = none, 1-3 = severity (higher = worse punishment)
    public string AngeredGodName { get; set; } = "";         // The god that was angered
    public string BetrayedForGodName { get; set; } = "";     // The god the player sacrificed to instead
    public bool DivineWrathPending { get; set; } = false;    // Has punishment triggered yet?
    public int DivineWrathTurnsRemaining { get; set; } = 0;  // Turns until wrath fades (if unpunished)

    /// <summary>
    /// Record divine wrath when the player betrays their god
    /// </summary>
    public void RecordDivineWrath(string playerGod, string betrayedForGod, int severity)
    {
        AngeredGodName = playerGod;
        BetrayedForGodName = betrayedForGod;
        DivineWrathLevel = Math.Min(3, DivineWrathLevel + severity);  // Stack up to level 3
        DivineWrathPending = true;
        DivineWrathTurnsRemaining = 50 + (severity * 20);  // Wrath lasts longer for more severe betrayals
    }

    /// <summary>
    /// Clear divine wrath after punishment has been dealt
    /// </summary>
    public void ClearDivineWrath()
    {
        DivineWrathLevel = 0;
        AngeredGodName = "";
        BetrayedForGodName = "";
        DivineWrathPending = false;
        DivineWrathTurnsRemaining = 0;
    }

    /// <summary>
    /// Reduce wrath over time if no punishment occurred
    /// </summary>
    public void TickDivineWrath()
    {
        if (DivineWrathPending && DivineWrathTurnsRemaining > 0)
        {
            DivineWrathTurnsRemaining--;
            if (DivineWrathTurnsRemaining <= 0)
            {
                // Wrath fades naturally over time (the god forgives... eventually)
                DivineWrathLevel = Math.Max(0, DivineWrathLevel - 1);
                if (DivineWrathLevel == 0)
                {
                    ClearDivineWrath();
                }
                else
                {
                    DivineWrathTurnsRemaining = 30;  // Reset timer for next level decay
                }
            }
        }
    }

    // Battle temporary flags
    public bool Casted { get; set; }                // used in battles
    public long Punch { get; set; }                 // player punch, temporary
    public long Absorb { get; set; }                // absorb punch, temporary
    public bool UsedItem { get; set; }              // has used item in battle
    public bool IsDefending { get; set; } = false;
    public bool IsRaging { get; set; } = false;        // Barbarian rage state
    public bool HasOceanMemory { get; set; } = false;  // Ocean's Memory spell - half mana cost
    public int SmiteChargesRemaining { get; set; } = 0; // Paladin daily smite uses left

    // Temporary combat bonuses from abilities
    public int TempAttackBonus { get; set; } = 0;
    public int TempAttackBonusDuration { get; set; } = 0;
    public int TempDefenseBonus { get; set; } = 0;
    public int TempDefenseBonusDuration { get; set; } = 0;
    public bool DodgeNextAttack { get; set; } = false;

    // Ability-applied combat state flags (combat-transient, not serialized)
    public bool HasBloodlust { get; set; } = false;      // Barbarian: heal on kill
    public bool HasStatusImmunity { get; set; } = false;  // Immune to debuffs
    public int StatusImmunityDuration { get; set; } = 0;

    // Companion system integration
    public bool IsCompanion { get; set; } = false;
    public UsurperRemake.Systems.CompanionId? CompanionId { get; set; } = null;

    // Player echo (loaded from DB for cooperative dungeons)
    public bool IsEcho { get; set; } = false;

    // Combat Stamina System - resource for special abilities
    // Formula: MaxCombatStamina = 50 + (Stamina stat * 2) + (Level * 3)
    public long CurrentCombatStamina { get; set; } = 100;
    public long MaxCombatStamina => 50 + (Stamina * 2) + (Level * 3);

    /// <summary>
    /// Initialize combat stamina to full at start of combat
    /// </summary>
    public void InitializeCombatStamina()
    {
        CurrentCombatStamina = MaxCombatStamina;
    }

    /// <summary>
    /// Regenerate stamina per combat round
    /// Base regen: 5 + (Stamina stat / 10)
    /// </summary>
    public int RegenerateCombatStamina()
    {
        int regen = 5 + (int)(Stamina / 10);
        long oldStamina = CurrentCombatStamina;
        CurrentCombatStamina = Math.Min(CurrentCombatStamina + regen, MaxCombatStamina);
        return (int)(CurrentCombatStamina - oldStamina);
    }

    /// <summary>
    /// Check if character has enough stamina for an ability
    /// </summary>
    public bool HasEnoughStamina(int cost)
    {
        return CurrentCombatStamina >= cost;
    }

    /// <summary>
    /// Spend stamina on an ability, returns true if successful
    /// </summary>
    public bool SpendStamina(int cost)
    {
        if (CurrentCombatStamina < cost) return false;
        CurrentCombatStamina -= cost;
        return true;
    }

    // Magical combat buffs
    public int MagicACBonus { get; set; } = 0;          // Flat AC bonus from spells like Shield/Prismatic Cage
    public int DamageAbsorptionPool { get; set; } = 0;  // Remaining damage Stoneskin can absorb

    // Cursed equipment flags
    public bool WeaponCursed { get; set; } = false;     // Weapon is cursed
    public bool ArmorCursed { get; set; } = false;      // Armor is cursed
    public bool ShieldCursed { get; set; } = false;     // Shield is cursed

    // NEW: Modern RPG Equipment System
    // Dictionary mapping each slot to equipment ID (0 = empty)
    public Dictionary<EquipmentSlot, int> EquippedItems { get; set; } = new();

    // Base stats (without equipment bonuses) - for recalculation
    public long BaseStrength { get; set; }
    public long BaseDexterity { get; set; }
    public long BaseConstitution { get; set; }
    public long BaseIntelligence { get; set; }
    public long BaseWisdom { get; set; }
    public long BaseCharisma { get; set; }
    public long BaseMaxHP { get; set; }
    public long BaseMaxMana { get; set; }
    public long BaseDefence { get; set; }
    public long BaseStamina { get; set; }
    public long BaseAgility { get; set; }

    // Training System - D&D style proficiency
    public int TrainingPoints { get; set; } = 0;
    public Dictionary<string, TrainingSystem.ProficiencyLevel> SkillProficiencies { get; set; } = new();
    public Dictionary<string, int> SkillTrainingProgress { get; set; } = new();

    // Gold-based Stat Training (v0.30.9) - separate from TrainingPoints system
    public Dictionary<string, int> StatTrainingCounts { get; set; } = new();

    // NPC Team Wage Tracking (v0.30.9) - tracks consecutive unpaid days per NPC
    public Dictionary<string, int> UnpaidWageDays { get; set; } = new();

    // Crafting Materials (v0.30.9) - rare lore-themed materials for high-tier enchantments and training
    public Dictionary<string, int> CraftingMaterials { get; set; } = new();

    public bool HasMaterial(string materialId, int count = 1)
    {
        return CraftingMaterials.TryGetValue(materialId, out int owned) && owned >= count;
    }

    public bool ConsumeMaterial(string materialId, int count = 1)
    {
        if (!HasMaterial(materialId, count)) return false;
        CraftingMaterials[materialId] -= count;
        if (CraftingMaterials[materialId] <= 0)
            CraftingMaterials.Remove(materialId);
        return true;
    }

    public void AddMaterial(string materialId, int count = 1)
    {
        if (!CraftingMaterials.ContainsKey(materialId))
            CraftingMaterials[materialId] = 0;
        CraftingMaterials[materialId] += count;
    }

    // Home Upgrade System - Gold sinks
    public int HomeLevel { get; set; } = 0;
    public int ChestLevel { get; set; } = 0;
    public int TrainingRoomLevel { get; set; } = 0;
    public int GardenLevel { get; set; } = 0;
    public bool HasTrophyRoom { get; set; } = false;
    public bool HasTeleportCircle { get; set; } = false;
    public bool HasLegendaryArmory { get; set; } = false;
    public bool HasVitalityFountain { get; set; } = false;
    public int PermanentDamageBonus { get; set; } = 0;
    public int PermanentDefenseBonus { get; set; } = 0;
    public long BonusMaxHP { get; set; } = 0;

    // Faction consumable properties (v0.40.2)
    public int PoisonCoatingCombats { get; set; } = 0;  // Combats remaining with poison coating (+20% dmg)
    public int SmokeBombs { get; set; } = 0;             // Guaranteed escape items (max 3)
    public int InnerSanctumLastDay { get; set; } = 0;    // Last day Inner Sanctum was used (legacy, single-player)

    // Real-world-date daily tracking (online mode — survives logout/login)
    public DateTime LastDailyResetBoundary { get; set; } = DateTime.MinValue;
    public DateTime LastPrayerRealDate { get; set; } = DateTime.MinValue;
    public DateTime LastInnerSanctumRealDate { get; set; } = DateTime.MinValue;
    public DateTime LastBindingOfSoulsRealDate { get; set; } = DateTime.MinValue;
    public int SethFightsToday { get; set; } = 0;
    public int ArmWrestlesToday { get; set; } = 0;
    public bool DivineFavorTriggeredThisCombat { get; set; } = false; // Transient: reset each combat

    // Dark Alley Overhaul (v0.41.0)
    public int GroggoShadowBlessingDex { get; set; } = 0;      // Active Groggo DEX buff (removed on rest)
    public int SteroidShopPurchases { get; set; } = 0;          // Lifetime steroid purchases (cap 3)
    public int AlchemistINTBoosts { get; set; } = 0;            // Lifetime alchemist INT boosts (cap 3)
    public int GamblingRoundsToday { get; set; } = 0;           // Daily gambling counter (max 10)
    public int PitFightsToday { get; set; } = 0;                // Daily pit fight counter (max 3)
    public long LoanAmount { get; set; } = 0;                   // Active loan balance (principal + interest)
    public int LoanDaysRemaining { get; set; } = 0;             // Days until enforcer attack
    public long LoanInterestAccrued { get; set; } = 0;          // Total interest accrued
    public int DarkAlleyReputation { get; set; } = 0;           // Underground reputation (0-1000)
    public Dictionary<int, int> DrugTolerance { get; set; } = new(); // DrugType(int) -> tolerance level
    public bool SafeHouseResting { get; set; } = false;        // Shadows members resting here are hidden from PvP

    // Weapon configuration detection
    public bool IsDualWielding =>
        EquippedItems.TryGetValue(EquipmentSlot.MainHand, out var mainId) && mainId > 0 &&
        EquippedItems.TryGetValue(EquipmentSlot.OffHand, out var offId) && offId > 0 &&
        EquipmentDatabase.GetById(mainId)?.Handedness == WeaponHandedness.OneHanded &&
        EquipmentDatabase.GetById(offId)?.Handedness == WeaponHandedness.OneHanded;

    public bool HasShieldEquipped =>
        EquippedItems.TryGetValue(EquipmentSlot.OffHand, out var offId) && offId > 0 &&
        EquipmentDatabase.GetById(offId)?.WeaponType == WeaponType.Shield ||
        EquipmentDatabase.GetById(offId)?.WeaponType == WeaponType.Buckler ||
        EquipmentDatabase.GetById(offId)?.WeaponType == WeaponType.TowerShield;

    public bool IsTwoHanding =>
        EquippedItems.TryGetValue(EquipmentSlot.MainHand, out var mainId) && mainId > 0 &&
        EquipmentDatabase.GetById(mainId)?.Handedness == WeaponHandedness.TwoHanded;

    /// <summary>
    /// Get the equipment in a specific slot
    /// </summary>
    public Equipment GetEquipment(EquipmentSlot slot)
    {
        if (EquippedItems.TryGetValue(slot, out var id) && id > 0)
            return EquipmentDatabase.GetById(id);
        return null;
    }

    /// <summary>
    /// Check if an item requires the player to choose which slot to equip it in.
    /// Returns true for one-handed weapons (can go in MainHand or OffHand for dual wielding).
    /// </summary>
    public static bool RequiresSlotSelection(Equipment item)
    {
        if (item == null) return false;
        // Only one-handed weapons can be equipped in either hand
        return item.Handedness == WeaponHandedness.OneHanded;
    }

    /// <summary>
    /// Equip an item to the appropriate slot (auto-determines slot)
    /// If there's an existing item in the slot, it will be moved to inventory
    /// </summary>
    public bool EquipItem(Equipment item, out string message)
    {
        return EquipItem(item, null, out message);
    }

    /// <summary>
    /// Equip an item to a specific slot (or auto-determine if targetSlot is null)
    /// If there's an existing item in the slot, it will be moved to inventory (or off-hand if applicable)
    /// For one-handed weapons, caller should prompt user and pass targetSlot explicitly.
    /// </summary>
    public bool EquipItem(Equipment item, EquipmentSlot? targetSlot, out string message)
    {
        message = "";

        if (item == null)
        {
            message = "No item to equip";
            return false;
        }

        // Check if character can equip this item
        if (!item.CanEquip(this, out string reason))
        {
            message = reason;
            return false;
        }

        // Handle two-handed weapons - must unequip BOTH main hand and off-hand
        if (item.Handedness == WeaponHandedness.TwoHanded)
        {
            // Unequip main hand first
            if (EquippedItems.TryGetValue(EquipmentSlot.MainHand, out var mainId) && mainId > 0)
            {
                var mainHandItem = UnequipSlot(EquipmentSlot.MainHand);
                if (mainHandItem != null)
                {
                    var legacyMainHand = ConvertEquipmentToItem(mainHandItem);
                    Inventory.Add(legacyMainHand);
                    message = $"Moved {mainHandItem.Name} to inventory. ";
                }
            }

            // Unequip off-hand
            if (EquippedItems.TryGetValue(EquipmentSlot.OffHand, out var offId) && offId > 0)
            {
                var offHandItem = UnequipSlot(EquipmentSlot.OffHand);
                if (offHandItem != null)
                {
                    var legacyOffHand = ConvertEquipmentToItem(offHandItem);
                    Inventory.Add(legacyOffHand);
                    message += $"Moved {offHandItem.Name} to inventory. ";
                }
            }
        }

        // Determine the correct slot for this item
        EquipmentSlot slot;

        if (targetSlot.HasValue)
        {
            // Use the explicitly specified slot
            slot = targetSlot.Value;

            // Validate the target slot is appropriate for this item
            if (item.Handedness == WeaponHandedness.OneHanded)
            {
                // One-handed weapons can go in either hand
                if (slot != EquipmentSlot.MainHand && slot != EquipmentSlot.OffHand)
                {
                    message = "One-handed weapons can only be equipped in MainHand or OffHand";
                    return false;
                }
            }
            else if (item.Handedness == WeaponHandedness.TwoHanded)
            {
                if (slot != EquipmentSlot.MainHand)
                {
                    message = "Two-handed weapons must be equipped in MainHand";
                    return false;
                }
            }
            else if (item.Handedness == WeaponHandedness.OffHandOnly)
            {
                if (slot != EquipmentSlot.OffHand)
                {
                    message = "Shields must be equipped in OffHand";
                    return false;
                }
            }
        }
        else
        {
            // Auto-determine slot based on item type
            slot = item.Slot;

            // For weapons, determine the correct slot
            if (item.Handedness == WeaponHandedness.OneHanded || item.Handedness == WeaponHandedness.TwoHanded)
                slot = EquipmentSlot.MainHand;
            else if (item.Handedness == WeaponHandedness.OffHandOnly)
                slot = EquipmentSlot.OffHand;

            // Smart ring slot selection: prefer empty slot over replacing
            if (slot == EquipmentSlot.LFinger || slot == EquipmentSlot.RFinger)
            {
                var leftRing = GetEquipment(EquipmentSlot.LFinger);
                var rightRing = GetEquipment(EquipmentSlot.RFinger);

                if (leftRing == null && rightRing == null)
                    slot = EquipmentSlot.LFinger; // Both empty, use left
                else if (leftRing == null)
                    slot = EquipmentSlot.LFinger; // Left empty, use it
                else if (rightRing == null)
                    slot = EquipmentSlot.RFinger; // Right empty, use it
                // else both full — keep original slot (caller should prompt)
            }
        }

        // Handle shields/off-hand - must unequip 2H weapon first if equipping off-hand
        if (slot == EquipmentSlot.OffHand && IsTwoHanding)
        {
            // Unequip the 2H weapon to allow off-hand equip
            var twoHandItem = UnequipSlot(EquipmentSlot.MainHand);
            if (twoHandItem != null)
            {
                var legacyTwoHand = ConvertEquipmentToItem(twoHandItem);
                Inventory.Add(legacyTwoHand);
                message += $"Moved {twoHandItem.Name} to inventory. ";
            }
        }

        // Check if we're equipping to main hand and should try to move existing item to off-hand
        if (slot == EquipmentSlot.MainHand && item.Handedness == WeaponHandedness.OneHanded)
        {
            var currentMainHand = GetEquipment(EquipmentSlot.MainHand);
            var currentOffHand = GetEquipment(EquipmentSlot.OffHand);

            // If main hand has a 1H weapon and off-hand is empty, move main hand to off-hand
            if (currentMainHand != null &&
                currentMainHand.Handedness == WeaponHandedness.OneHanded &&
                currentOffHand == null)
            {
                // Move main hand to off-hand (don't unequip, just reassign)
                EquippedItems[EquipmentSlot.OffHand] = currentMainHand.Id;
                EquippedItems.Remove(EquipmentSlot.MainHand);
                message += $"Moved {currentMainHand.Name} to off-hand. ";

                // Now equip the new item to main hand
                EquippedItems[slot] = item.Id;
                item.ApplyToCharacter(this);
                message += $"Equipped {item.Name} in main hand";
                return true;
            }
        }

        // Unequip current item in slot if any and move to inventory
        var oldEquipment = UnequipSlot(slot);
        if (oldEquipment != null)
        {
            // Convert Equipment to legacy Item and add to inventory
            var legacyItem = ConvertEquipmentToItem(oldEquipment);
            Inventory.Add(legacyItem);
            message += $"Moved {oldEquipment.Name} to inventory. ";
        }

        // Equip the new item
        EquippedItems[slot] = item.Id;

        // Apply stats
        item.ApplyToCharacter(this);

        string slotName = slot == EquipmentSlot.MainHand ? "main hand" : (slot == EquipmentSlot.OffHand ? "off-hand" : slot.ToString());
        message += $"Equipped {item.Name} in {slotName}";
        return true;
    }

    /// <summary>
    /// Convert Equipment to legacy Item for inventory storage
    /// </summary>
    private global::Item ConvertEquipmentToItem(Equipment equipment)
    {
        // Determine the item type based on handedness/weapon type first, then slot
        global::ObjType itemType;

        // Check if it's a weapon (has weapon power and is not a shield)
        if (equipment.Handedness == WeaponHandedness.OneHanded ||
            equipment.Handedness == WeaponHandedness.TwoHanded)
        {
            itemType = global::ObjType.Weapon;
        }
        else if (equipment.Handedness == WeaponHandedness.OffHandOnly ||
                 equipment.ShieldBonus > 0)
        {
            itemType = global::ObjType.Shield;
        }
        else
        {
            // Use slot to determine type for non-weapons
            itemType = equipment.Slot switch
            {
                EquipmentSlot.MainHand => global::ObjType.Weapon,
                EquipmentSlot.OffHand => global::ObjType.Shield,
                EquipmentSlot.Body => global::ObjType.Body,
                EquipmentSlot.Head => global::ObjType.Head,
                EquipmentSlot.Arms => global::ObjType.Arms,
                EquipmentSlot.Hands => global::ObjType.Hands,
                EquipmentSlot.Legs => global::ObjType.Legs,
                EquipmentSlot.Feet => global::ObjType.Feet,
                EquipmentSlot.LFinger => global::ObjType.Fingers,
                EquipmentSlot.RFinger => global::ObjType.Fingers,
                EquipmentSlot.Neck => global::ObjType.Neck,
                EquipmentSlot.Face => global::ObjType.Face,
                EquipmentSlot.Waist => global::ObjType.Waist,
                _ => global::ObjType.Abody
            };
        }

        var item = new global::Item
        {
            Name = equipment.Name,
            Type = itemType,
            Attack = equipment.WeaponPower,
            Armor = equipment.ArmorClass + equipment.ShieldBonus,
            Value = equipment.Value,
            Strength = equipment.StrengthBonus,
            Dexterity = equipment.DexterityBonus,
            Wisdom = equipment.WisdomBonus,
            HP = equipment.MaxHPBonus,
            Mana = equipment.MaxManaBonus,
            Defence = equipment.DefenceBonus,
            IsCursed = equipment.IsCursed,
            Cursed = equipment.IsCursed
        };
        return item;
    }

    /// <summary>
    /// Unequip item from a specific slot
    /// </summary>
    public Equipment UnequipSlot(EquipmentSlot slot)
    {
        if (!EquippedItems.TryGetValue(slot, out var id) || id == 0)
            return null;

        var item = EquipmentDatabase.GetById(id);
        if (item != null)
        {
            // Check if cursed
            if (item.IsCursed)
                return null; // Can't unequip cursed items

            // Remove stats
            item.RemoveFromCharacter(this);
        }

        EquippedItems[slot] = 0;
        return item;
    }

    /// <summary>
    /// Recalculate all stats from base values plus equipment bonuses
    /// Now applies stat-based bonuses from the StatEffectsSystem
    /// </summary>
    public void RecalculateStats()
    {
        // Start from base values
        Strength = BaseStrength;
        Dexterity = BaseDexterity;
        Constitution = BaseConstitution;
        Intelligence = BaseIntelligence;
        Wisdom = BaseWisdom;
        Charisma = BaseCharisma;
        MaxHP = BaseMaxHP;
        MaxMana = BaseMaxMana;
        Defence = BaseDefence;
        Stamina = BaseStamina;
        Agility = BaseAgility;
        WeapPow = 0;
        ArmPow = 0;

        // Add bonuses from all equipped items
        foreach (var kvp in EquippedItems)
        {
            if (kvp.Value <= 0) continue;
            var item = EquipmentDatabase.GetById(kvp.Value);
            item?.ApplyToCharacter(this);
        }

        // Apply stat-based bonuses AFTER equipment (stats may have been modified)
        // Constitution bonus to HP
        MaxHP += StatEffectsSystem.GetConstitutionHPBonus(Constitution, Level);

        // Intelligence and Wisdom bonus to Mana (for casters)
        if (BaseMaxMana > 0) // Only for classes with mana
        {
            MaxMana += StatEffectsSystem.GetIntelligenceManaBonus(Intelligence, Level);
            MaxMana += StatEffectsSystem.GetWisdomManaBonus(Wisdom);
        }

        // Agility bonus to Defense (evasion component)
        Defence += StatEffectsSystem.GetAgilityDefenseBonus(Agility);

        // Apply child bonuses (family provides stat boosts)
        UsurperRemake.Systems.FamilySystem.Instance?.ApplyChildBonuses(this);

        // Keep current HP/Mana within bounds
        var hpBefore = HP;
        HP = Math.Min(HP, MaxHP);
        // Log if HP was clamped (helps debug HP not saving correctly)
        if (HP != hpBefore)
        {
            UsurperRemake.Systems.DebugLogger.Instance.LogDebug("STATS", $"HP clamped: {hpBefore} -> {HP} (MaxHP={MaxHP}, BaseMaxHP={BaseMaxHP})");
        }
        Mana = Math.Min(Mana, MaxMana);
    }

    /// <summary>
    /// Initialize base stats from current values (call when creating character or loading old save)
    /// </summary>
    public void InitializeBaseStats()
    {
        BaseStrength = Strength;
        BaseDexterity = Dexterity;
        BaseConstitution = Constitution;
        BaseIntelligence = Intelligence;
        BaseWisdom = Wisdom;
        BaseCharisma = Charisma;
        BaseMaxHP = MaxHP;
        BaseMaxMana = MaxMana;
        BaseDefence = Defence;
        BaseStamina = Stamina;
        BaseAgility = Agility;
    }

    /// <summary>
    /// Get total equipment value (for sell price calculation)
    /// </summary>
    public long GetTotalEquipmentValue()
    {
        long total = 0;
        foreach (var kvp in EquippedItems)
        {
            if (kvp.Value <= 0) continue;
            var item = EquipmentDatabase.GetById(kvp.Value);
            if (item != null) total += item.Value;
        }
        return total;
    }

    /// <summary>
    /// Sum a specific special property across all equipped items.
    /// Used for enchant bonuses like crit chance, lifesteal, magic resist that
    /// aren't transferred to Character stats via ApplyToCharacter.
    /// </summary>
    private int SumEquipmentProperty(Func<Equipment, int> selector)
    {
        int total = 0;
        foreach (var kvp in EquippedItems)
        {
            if (kvp.Value <= 0) continue;
            var item = EquipmentDatabase.GetById(kvp.Value);
            if (item != null) total += selector(item);
        }
        return total;
    }

    public int GetEquipmentCritChanceBonus() => SumEquipmentProperty(e => e.CriticalChanceBonus);
    public int GetEquipmentCritDamageBonus() => SumEquipmentProperty(e => e.CriticalDamageBonus);
    public int GetEquipmentLifeSteal() => SumEquipmentProperty(e => e.LifeSteal);
    public int GetEquipmentMagicResistance() => SumEquipmentProperty(e => e.MagicResistance);
    public int GetEquipmentManaSteal() => SumEquipmentProperty(e => e.ManaSteal);
    public int GetEquipmentArmorPiercing() => SumEquipmentProperty(e => e.ArmorPiercing);
    public int GetEquipmentThorns() => SumEquipmentProperty(e => e.Thorns);
    public int GetEquipmentHPRegen() => SumEquipmentProperty(e => e.HPRegen);
    public int GetEquipmentManaRegen() => SumEquipmentProperty(e => e.ManaRegen);

    /// <summary>
    /// Get equipment summary for display
    /// </summary>
    public string GetEquipmentSummary()
    {
        var lines = new List<string>();

        void AddSlot(string label, EquipmentSlot slot)
        {
            var item = GetEquipment(slot);
            lines.Add($"{label}: {item?.Name ?? "None"}");
        }

        AddSlot("Main Hand", EquipmentSlot.MainHand);
        AddSlot("Off Hand", EquipmentSlot.OffHand);
        AddSlot("Head", EquipmentSlot.Head);
        AddSlot("Body", EquipmentSlot.Body);
        AddSlot("Arms", EquipmentSlot.Arms);
        AddSlot("Hands", EquipmentSlot.Hands);
        AddSlot("Legs", EquipmentSlot.Legs);
        AddSlot("Feet", EquipmentSlot.Feet);
        AddSlot("Waist", EquipmentSlot.Waist);
        AddSlot("Cloak", EquipmentSlot.Cloak);
        AddSlot("Neck", EquipmentSlot.Neck);
        AddSlot("Neck 2", EquipmentSlot.Neck2);
        AddSlot("Face", EquipmentSlot.Face);
        AddSlot("Left Ring", EquipmentSlot.LFinger);
        AddSlot("Right Ring", EquipmentSlot.RFinger);

        return string.Join("\n", lines);
    }

    // Kill statistics
    public long MKills { get; set; }                // monster kills
    public long MDefeats { get; set; }              // monster defeats
    public long PKills { get; set; }                // player kills
    public long PDefeats { get; set; }              // player defeats
    
    // New for version 0.08+
    public long Interest { get; set; }              // accumulated bank interest
    public long AliveBonus { get; set; }            // staying alive bonus
    public bool Expert { get; set; }                // expert menus ON/OFF
    public int MaxTime { get; set; }                // max minutes per session
    public byte Ear { get; set; }                   // internode message handling
    public char CastIn { get; set; }                // casting flag
    public int Weapon { get; set; }                 // OLD mode weapon
    public int Armor { get; set; }                  // OLD mode armor
    public int APow { get; set; }                   // OLD mode armor power
    public int WPow { get; set; }                   // OLD mode weapon power
    public byte DisRes { get; set; }                // disease resistance
    public bool AMember { get; set; }               // alchemist society member
    
    // Medals (from Pascal: array[1..20] of boolean)
    public List<bool> Medal { get; set; }           // medals earned
    
    public bool BankGuard { get; set; }             // bank guard?
    public long BankWage { get; set; }              // salary from bank
    public long Loan { get; set; }                  // outstanding bank loan
    public byte WeapHag { get; set; } = 3;          // weapon shop haggling attempts left
    public byte ArmHag { get; set; } = 3;           // armor shop haggling attempts left
    public int RecNr { get; set; }                  // file record number

    // New for version 0.14+
    public int Quests { get; set; }                 // completed missions/quests
    public bool Deleted { get; set; }               // is record deleted
    public string God { get; set; } = "";           // worshipped god name
    public long RoyQuests { get; set; }             // royal quests accomplished
    
    // New for version 0.17+
    public long RoyTaxPaid { get; set; }            // royal taxes paid
    public byte Wrestlings { get; set; }            // wrestling matches left
    public byte DrinksLeft { get; set; }            // drinks left today
    public byte DaysInPrison { get; set; }          // days left in prison
    public bool CellDoorOpen { get; set; }            // has someone unlocked the cell door (rescued)?
    public string RescuedBy { get; set; } = "";       // name of the rescuer

    // New for version 0.18+
    public byte UmanBearTries { get; set; }         // bear taming attempts
    public byte Massage { get; set; }               // massages today

    // Note: Gym removed - stat training doesn't fit single-player endless format
    // Legacy gym fields kept for save compatibility but unused
    public byte GymSessions { get; set; }           // UNUSED
    public byte GymOwner { get; set; }              // UNUSED
    public byte GymCard { get; set; }               // UNUSED
    public DateTime LastStrengthTraining { get; set; } = DateTime.MinValue;  // UNUSED
    public DateTime LastDexterityTraining { get; set; } = DateTime.MinValue; // UNUSED
    public DateTime LastTugOfWar { get; set; } = DateTime.MinValue;          // UNUSED
    public DateTime LastWrestling { get; set; } = DateTime.MinValue;         // UNUSED

    public int RoyQuestsToday { get; set; }         // royal quests today
    public byte KingVotePoll { get; set; }          // days since king vote
    public byte KingLastVote { get; set; }          // last vote value

    // Dungeon progression - tracks which boss/seal floors have been cleared
    public HashSet<int> ClearedSpecialFloors { get; set; } = new HashSet<int>();

    // Dungeon floor persistence - tracks room state per floor for respawn system
    public Dictionary<int, DungeonFloorState> DungeonFloorStates { get; set; } = new Dictionary<int, DungeonFloorState>();

    // Marriage and family
    public bool Married { get; set; }               // is married?
    public int Kids { get; set; }                   // number of children
    public int IntimacyActs { get; set; }           // intimacy acts left today
    public byte Pregnancy { get; set; }             // pregnancy days (0=not pregnant)
    public string FatherID { get; set; } = "";      // father's unique ID
    public string ID { get; set; } = "";            // unique player ID
    public bool TaxRelief { get; set; }             // free from tax
    
    public int MarriedTimes { get; set; }           // marriage counter
    public int BardSongsLeft { get; set; }          // bard songs left
    public byte PrisonEscapes { get; set; }         // escape attempts allowed
    public byte FileType { get; set; }              // file type (1=player, 2=npc)
    public int Resurrections { get; set; }          // resurrections left
    
    // New for version 0.20+
    public int PickPocketAttempts { get; set; }     // pick pocket attempts
    public int BankRobberyAttempts { get; set; }    // bank robbery attempts
    
    // Religious and Divine Properties (Pascal UserRec fields)
    public bool IsMarried { get; set; } = false;       // Marriage status
    public string SpouseName { get; set; } = "";       // Name of spouse
    public int MarriageAttempts { get; set; } = 0;     // Daily marriage attempts used
    public bool BannedFromChurch { get; set; } = false; // Banned from religious services
    public DateTime LastResurrection { get; set; } = DateTime.MinValue; // Last time resurrected
    public int ResurrectionsUsed { get; set; } = 0;    // Total resurrections used
    public int MaxResurrections { get; set; } = 3;     // Maximum resurrections allowed
    
    // Divine favor and religious standing  
    public int DivineBlessing { get; set; } = 0;       // Divine blessing duration (days)
    public bool HasHolyWater { get; set; } = false;    // Carrying holy water
    public DateTime LastConfession { get; set; } = DateTime.MinValue; // Last confession
    public int SacrificesMade { get; set; } = 0;       // Total sacrifices to gods
    
    // Church-related statistics
    public long ChurchDonations { get; set; } = 0;     // Total amount donated to church
    public int BlessingsReceived { get; set; } = 0;    // Number of blessings received
    public int HealingsReceived { get; set; } = 0;     // Number of healings received

    // Blood Price / Murder Weight System (v0.42.0)
    // Tracks the weight of the player's conscience from causing permanent NPC deaths
    public float MurderWeight { get; set; } = 0f;                              // Accumulated guilt (affects dreams, rest, endings)
    public List<string> PermakillLog { get; set; } = new();                    // Names of permanently killed NPCs (cap 20)
    public DateTime LastMurderWeightDecay { get; set; } = DateTime.MinValue;   // When weight last decayed naturally
    
    // Additional compatibility properties
    public int QuestsLeft { get; set; } = 5;
    public List<Quest> ActiveQuests { get; set; } = new();
    public int DrinkslLeft { get; set; } = 5;
    public long WeaponPower { get; set; }
    public long ArmorClass { get; set; }
    public int WantedLvl { get; set; } = 0;  // Wanted level for crime tracking
    
    // Missing inventory system
    public List<Item> Inventory { get; set; } = new();
    
    // Current values (convenience properties)
    public long CurrentHP 
    { 
        get => HP; 
        set => HP = value; 
    }
    
    public long CurrentMana 
    { 
        get => Mana; 
        set => Mana = value; 
    }
    
    // Additional properties for API compatibility
    // TurnCount - counts UP from 0, drives world simulation (single-player persistent system)
    public int TurnCount { get; set; } = 0;

    // Legacy properties for compatibility (no longer used for limiting gameplay)
    private int? _manualTurnsRemaining;
    public int TurnsRemaining
    {
        get => _manualTurnsRemaining ?? TurnCount; // Now returns turn count for save compatibility
        set => _manualTurnsRemaining = value;
    }
    public int PrisonsLeft { get; set; } = 0; // Prison sentences remaining
    public int ExecuteLeft { get; set; } = 0; // Execution attempts remaining
    public int MarryActions { get; set; } = 0; // Marriage actions remaining
    public int WolfFeed { get; set; } = 0; // Wolf feeding actions
    public int RoyalAdoptions { get; set; } = 0; // Royal adoption actions
    public int DaysInPower { get; set; } = 0; // Days as king/ruler
    public int Fame { get; set; } = 0; // Fame/reputation level
    public string? NobleTitle { get; set; } = null; // Noble title (Sir, Dame, Lord, Lady, etc.)
    public long RoyalLoanAmount { get; set; } = 0; // Outstanding loan from the king
    public int RoyalLoanDueDay { get; set; } = 0; // Day number when loan is due (0 = no loan)

    public DateTime LastLogin { get; set; }
    
    // Generic status effects (duration in rounds)
    public Dictionary<StatusEffect, int> ActiveStatuses { get; set; } = new();

    public bool HasStatus(StatusEffect s) => ActiveStatuses.ContainsKey(s);

    /// <summary>
    /// Check for status effect by string name (for spell effects like "evasion", "invisible")
    /// </summary>
    public bool HasStatusEffect(string effectName)
    {
        // Check if there's a matching StatusEffect enum
        if (Enum.TryParse<StatusEffect>(effectName, true, out var effect))
        {
            return HasStatus(effect);
        }

        // Check special string-based effects stored in combat buffs
        return effectName.ToLower() switch
        {
            "evasion" => HasStatus(StatusEffect.Blur) || HasStatus(StatusEffect.Haste),
            "invisible" => HasStatus(StatusEffect.Hidden), // Hidden acts like invisible
            "haste" => HasStatus(StatusEffect.Haste),
            _ => false
        };
    }

    public void ApplyStatus(StatusEffect status, int duration)
    {
        if (status == StatusEffect.None) return;
        ActiveStatuses[status] = duration;
    }

    /// <summary>
    /// Tick status durations and apply per-round effects (poison damage, etc.).
    /// Should be called once per combat round.
    /// Returns a list of status effect messages to display.
    /// </summary>
    public List<(string message, string color)> ProcessStatusEffects()
    {
        var messages = new List<(string message, string color)>();
        if (ActiveStatuses.Count == 0) return messages;

        var toRemove = new List<StatusEffect>();
        var rnd = new Random();

        foreach (var kvp in ActiveStatuses.ToList())
        {
            int dmg = 0;
            switch (kvp.Key)
            {
                case StatusEffect.Poisoned:
                    // Poison scales with level: 2-5 base + 1 per 10 levels
                    dmg = rnd.Next(2, 6) + (int)(Level / 10);
                    HP = Math.Max(0, HP - dmg);
                    messages.Add(($"{DisplayName} takes {dmg} poison damage!", "green"));
                    break;

                case StatusEffect.Bleeding:
                    dmg = rnd.Next(1, 7); // 1d6
                    HP = Math.Max(0, HP - dmg);
                    messages.Add(($"{DisplayName} bleeds for {dmg} damage!", "red"));
                    break;

                case StatusEffect.Burning:
                    dmg = rnd.Next(2, 9); // 2d4
                    HP = Math.Max(0, HP - dmg);
                    messages.Add(($"{DisplayName} burns for {dmg} fire damage!", "bright_red"));
                    break;

                case StatusEffect.Frozen:
                    dmg = rnd.Next(1, 4); // 1d3
                    HP = Math.Max(0, HP - dmg);
                    messages.Add(($"{DisplayName} takes {dmg} cold damage from the frost!", "bright_cyan"));
                    break;

                case StatusEffect.Cursed:
                    dmg = rnd.Next(1, 3); // 1d2
                    HP = Math.Max(0, HP - dmg);
                    messages.Add(($"{DisplayName} suffers {dmg} curse damage!", "magenta"));
                    break;

                case StatusEffect.Diseased:
                    HP = Math.Max(0, HP - 1);
                    messages.Add(($"{DisplayName} suffers from disease! (-1 HP)", "yellow"));
                    break;

                case StatusEffect.Regenerating:
                    var heal = rnd.Next(1, 7); // 1d6
                    HP = Math.Min(HP + heal, MaxHP);
                    messages.Add(($"{DisplayName} regenerates {heal} HP!", "bright_green"));
                    break;

                case StatusEffect.Reflecting:
                    // Handled during damage calculation, just remind
                    break;

                case StatusEffect.Lifesteal:
                    // Handled during damage calculation
                    break;
            }

            // Decrement duration (some effects like Stoneskin don't expire by time)
            if (kvp.Key != StatusEffect.Stoneskin && kvp.Key != StatusEffect.Shielded)
            {
                ActiveStatuses[kvp.Key] = kvp.Value - 1;
                if (ActiveStatuses[kvp.Key] <= 0)
                    toRemove.Add(kvp.Key);
            }
        }

        foreach (var s in toRemove)
        {
            ActiveStatuses.Remove(s);
            string effectName = s.GetShortName();

            switch (s)
            {
                case StatusEffect.Blessed:
                case StatusEffect.Defending:
                case StatusEffect.Protected:
                    MagicACBonus = 0;
                    messages.Add(($"{DisplayName}'s {effectName} effect fades.", "gray"));
                    break;
                case StatusEffect.Stoneskin:
                    DamageAbsorptionPool = 0;
                    messages.Add(($"{DisplayName}'s stoneskin crumbles away.", "gray"));
                    break;
                case StatusEffect.Raging:
                    IsRaging = false;
                    messages.Add(($"{DisplayName}'s rage subsides.", "gray"));
                    break;
                case StatusEffect.Haste:
                    messages.Add(($"{DisplayName} slows to normal speed.", "gray"));
                    break;
                case StatusEffect.Slow:
                    messages.Add(($"{DisplayName} can move normally again.", "gray"));
                    break;
                case StatusEffect.Stunned:
                case StatusEffect.Paralyzed:
                    messages.Add(($"{DisplayName} recovers and can act again!", "white"));
                    break;
                case StatusEffect.Silenced:
                    messages.Add(($"{DisplayName} can cast spells again.", "bright_cyan"));
                    break;
                case StatusEffect.Blinded:
                    messages.Add(($"{DisplayName}'s vision clears.", "white"));
                    break;
                case StatusEffect.Sleeping:
                    messages.Add(($"{DisplayName} wakes up!", "white"));
                    break;
                case StatusEffect.Poisoned:
                case StatusEffect.Bleeding:
                case StatusEffect.Burning:
                case StatusEffect.Frozen:
                case StatusEffect.Cursed:
                case StatusEffect.Diseased:
                    messages.Add(($"{DisplayName} is no longer {s.ToString().ToLower()}.", "gray"));
                    break;
                default:
                    messages.Add(($"{DisplayName}'s {effectName} wears off.", "gray"));
                    break;
            }
        }

        return messages;
    }

    /// <summary>
    /// Check if the character can take actions this turn
    /// </summary>
    public bool CanAct()
    {
        foreach (var status in ActiveStatuses.Keys)
        {
            if (status.PreventsAction())
                return false;
        }
        return true;
    }

    /// <summary>
    /// Check if the character can cast spells
    /// </summary>
    public bool CanCastSpells()
    {
        foreach (var status in ActiveStatuses.Keys)
        {
            if (status.PreventsSpellcasting())
                return false;
        }
        return true;
    }

    /// <summary>
    /// Get accuracy modifier from status effects and diseases
    /// </summary>
    public float GetAccuracyModifier()
    {
        float modifier = 1.0f;
        // Check both the disease flag (Blind) and the status effect (Blinded)
        if (Blind || HasStatus(StatusEffect.Blinded)) modifier *= 0.5f;
        if (HasStatus(StatusEffect.PowerStance)) modifier *= 0.75f;
        if (HasStatus(StatusEffect.Frozen)) modifier *= 0.75f;
        return modifier;
    }

    /// <summary>
    /// Get damage dealt modifier from status effects
    /// </summary>
    public float GetDamageDealtModifier()
    {
        float modifier = 1.0f;
        if (HasStatus(StatusEffect.Raging) || IsRaging) modifier *= 2.0f;
        if (HasStatus(StatusEffect.PowerStance)) modifier *= 1.5f;
        if (HasStatus(StatusEffect.Berserk)) modifier *= 1.5f;
        if (HasStatus(StatusEffect.Exhausted)) modifier *= 0.75f;
        if (HasStatus(StatusEffect.Empowered)) modifier *= 1.5f; // For spells
        if (HasStatus(StatusEffect.Hidden)) modifier *= 1.5f; // Stealth bonus
        return modifier;
    }

    /// <summary>
    /// Get damage taken modifier from status effects
    /// </summary>
    public float GetDamageTakenModifier()
    {
        float modifier = 1.0f;
        if (HasStatus(StatusEffect.Defending)) modifier *= 0.5f;
        if (HasStatus(StatusEffect.Vulnerable)) modifier *= 1.25f;
        if (HasStatus(StatusEffect.Invulnerable)) modifier = 0f;
        return modifier;
    }

    /// <summary>
    /// Get number of attacks this round based on status effects
    /// </summary>
    public int GetAttackCountModifier(int baseAttacks)
    {
        int attacks = baseAttacks;
        if (HasStatus(StatusEffect.Haste)) attacks *= 2;
        if (HasStatus(StatusEffect.Slow)) attacks = Math.Max(1, attacks / 2);
        if (HasStatus(StatusEffect.Frozen)) attacks = Math.Max(1, attacks / 2);
        return attacks;
    }

    /// <summary>
    /// Remove a status effect
    /// </summary>
    public void RemoveStatus(StatusEffect effect)
    {
        if (ActiveStatuses.ContainsKey(effect))
        {
            ActiveStatuses.Remove(effect);

            // Clean up associated state
            switch (effect)
            {
                case StatusEffect.Raging:
                    IsRaging = false;
                    break;
                case StatusEffect.Stoneskin:
                    DamageAbsorptionPool = 0;
                    break;
                case StatusEffect.Blessed:
                case StatusEffect.Defending:
                case StatusEffect.Protected:
                    MagicACBonus = 0;
                    break;
            }
        }
    }

    /// <summary>
    /// Clear all status effects (e.g., after combat)
    /// </summary>
    public void ClearAllStatuses()
    {
        ActiveStatuses.Clear();
        IsRaging = false;
        IsDefending = false;
        HasOceanMemory = false;
        DamageAbsorptionPool = 0;
        MagicACBonus = 0;
        HasBloodlust = false;
        HasStatusImmunity = false;
        StatusImmunityDuration = 0;
    }

    /// <summary>
    /// Get a formatted string of active status effects for display
    /// </summary>
    public string GetStatusDisplayString()
    {
        if (ActiveStatuses.Count == 0) return "";

        var parts = new List<string>();
        foreach (var kvp in ActiveStatuses)
        {
            string shortName = kvp.Key.GetShortName();
            parts.Add($"{shortName}({kvp.Value})");
        }
        return string.Join(" ", parts);
    }
    
    // Constructor to initialize lists
    public Character()
    {
        // Initialize empty lists with capacity - don't pre-fill with default values
        // This prevents confusion between empty slots (Count check) and actual items
        Item = new List<int>(GameConfig.MaxItem);
        ItemType = new List<ObjType>(GameConfig.MaxItem);
        Phrases = new List<string>(6);
        Description = new List<string>(4);

        // Initialize spells array [maxspells][2] - spells need to track known/enabled state
        Spell = new List<List<bool>>();
        for (int i = 0; i < GameConfig.MaxSpells; i++)
        {
            Spell.Add(new List<bool> { false, false });
        }

        // Initialize combat skills with capacity
        Skill = new List<int>(GameConfig.MaxCombat);

        // Initialize medals - these need defaults since we check by index
        Medal = new List<bool>(new bool[20]);
    }
    
    // Helper properties for commonly used calculations
    public bool IsAlive => HP > 0;
    public bool IsPlayer => AI == CharacterAI.Human;
    public bool IsNPC => AI == CharacterAI.Computer;
    public string DisplayName => !string.IsNullOrEmpty(Name2) ? Name2 : Name1;

    // TurnsLeft - now just returns TurnCount for backward compatibility (no limits in single-player)
    public int TurnsLeft => TurnCount;
    
    // Combat-related properties
    public long WeaponValue => WeapPow;
    public long ArmorValue => ArmPow;
    public string WeaponName => GetEquippedItemName(RHand); // Right hand weapon
    public string ArmorName => GetEquippedItemName(Body);   // Body armor
    
    // Status properties
    public bool Poisoned => Poison > 0;
    public int PoisonLevel => Poison;
    public bool OnSteroids => SteroidDays > 0;
    public int DrugDays => DrugEffectDays;
    public bool OnDrugs => DrugEffectDays > 0 && ActiveDrug != DrugType.None;
    public bool IsAddicted => Addict >= 25; // Addiction threshold
    
    // Social properties
    public string TeamName => Team;
    public bool IsTeamLeader => CTurf;
    public int Children => Kids;
    
    /// <summary>
    /// Compatibility property that maps to CTurf for API consistency
    /// </summary>
    public bool ControlsTurf 
    { 
        get => CTurf; 
        set => CTurf = value; 
    }
    
    /// <summary>
    /// Compatibility property that maps to TeamPW for API consistency
    /// </summary>
    public string TeamPassword 
    { 
        get => TeamPW; 
        set => TeamPW = value; 
    }
    
    /// <summary>
    /// Compatibility property that maps to TeamRec for API consistency
    /// </summary>
    public int TeamRecord 
    { 
        get => TeamRec; 
        set => TeamRec = value; 
    }
    
    private string GetEquippedItemName(int itemId)
    {
        if (itemId == 0) return "None";
        // Look up equipment from game data
        var equipment = EquipmentDatabase.GetById(itemId);
        return equipment?.Name ?? $"Unknown Item #{itemId}";
    }
    
    // Pascal-compatible string access for names
    public string Name => Name2; // Main game name
    public string RealName => Name1; // BBS name
    public string KingName => King ? DisplayName : "";
    
    public DateTime Created { get; set; } = DateTime.Now;

    // Alias American spelling used by some systems
    public long Defense
    {
        get => Defence;
        set => Defence = value;
    }

    // Simplified thievery skill placeholder
    public long Thievery { get; set; }

    // Simple level-up event hook for UI/system code expecting it
    public event Action<Character>? OnLevelUp;

    public void RaiseLevel(int newLevel)
    {
        if (newLevel > Level)
        {
            Level = newLevel;
            OnLevelUp?.Invoke(this);
        }
    }

    /// <summary>
    /// Returns a CombatModifiers object describing bonuses and abilities granted by this character's class.
    /// The numbers largely mirror classic Usurper balance but are open to tuning.
    /// </summary>
    public CombatModifiers GetClassCombatModifiers()
    {
        return Class switch
        {
            CharacterClass.Warrior => new CombatModifiers { AttackBonus = Level / 5, ExtraAttacks = Level / 10 },
            CharacterClass.Assassin => new CombatModifiers { BackstabMultiplier = 3.0f, PoisonChance = 25 },
            CharacterClass.Barbarian => new CombatModifiers { DamageReduction = 2, RageAvailable = true },
            CharacterClass.Paladin => new CombatModifiers { SmiteCharges = 1 + Level / 10, AuraBonus = 2 },
            CharacterClass.Ranger => new CombatModifiers { RangedBonus = 4, Tracking = true },
            _ => new CombatModifiers()
        };
    }
}

/// <summary>
/// Character AI type from Pascal
/// </summary>
public enum CharacterAI
{
    Computer = 'C',
    Human = 'H',
    Civilian = 'N'
}

/// <summary>
/// Character sex from Pascal (1=male, 2=female)
/// </summary>
public enum CharacterSex
{
    Male = 1,
    Female = 2
}

/// <summary>
/// Character races from Pascal races enum
/// </summary>
public enum CharacterRace
{
    Human,      // change RATING.PAS and VARIOUS.PAS when changing # of races
    Hobbit,
    Elf,
    HalfElf,
    Dwarf,
    Troll,
    Orc,
    Gnome,
    Gnoll,
    Mutant
}

/// <summary>
/// Character classes from Pascal classes enum
/// </summary>
public enum CharacterClass
{
    Alchemist,  // change RATING.PAS and VARIOUS.PAS when changing # of classes
    Assassin,
    Barbarian,  // no special ability
    Bard,       // no special ability
    Cleric,
    Jester,     // no special ability
    Magician,
    Paladin,
    Ranger,     // no special ability
    Sage,
    Warrior     // no special ability
}

/// <summary>
/// Object types from Pascal ObjType enum
/// </summary>
public enum ObjType
{
    Head = 1,
    Body = 2,
    Arms = 3,
    Hands = 4,
    Fingers = 5,
    Legs = 6,
    Feet = 7,
    Waist = 8,
    Neck = 9,
    Face = 10,
    Shield = 11,
    Food = 12,
    Drink = 13,
    Weapon = 14,
    Abody = 15,  // around body
    Magic = 16,
    Potion = 17
}

/// <summary>
/// Disease types from Pascal Cures enum
/// </summary>
public enum Cures
{
    Nothing,
    All,
    Blindness,
    Plague,
    Smallpox,
    Measles,
    Leprosy
}

/// <summary>
/// Drug types available in the game - affects stats temporarily
/// </summary>
public enum DrugType
{
    None = 0,

    // Strength enhancers
    Steroids = 1,           // +Str, +Damage, risk of addiction
    BerserkerRage = 2,      // +Str, +Attack, -Defense, short duration

    // Speed enhancers
    Haste = 10,             // +Agi, +Attacks, -HP drain
    QuickSilver = 11,       // +Dex, +Crit chance

    // Magic enhancers
    ManaBoost = 20,         // +Mana, +Spell power
    ThirdEye = 21,          // +Wis, +Magic resist

    // Defensive
    Ironhide = 30,          // +Con, +Defense, -Agi
    Stoneskin = 31,         // +Armor, -Speed

    // Risky/Addictive
    DarkEssence = 40,       // +All stats briefly, high addiction, crashes hard
    DemonBlood = 41         // +Damage, +Darkness, very addictive
}

/// <summary>
/// Drug system helper - calculates drug effects and manages addiction
/// </summary>
public static class DrugSystem
{
    private static Random _random = new();

    /// <summary>
    /// Apply a drug to a character
    /// </summary>
    public static (bool success, string message) UseDrug(Character character, DrugType drug, int potency = 1)
    {
        if (character.OnDrugs && character.ActiveDrug != DrugType.None)
        {
            // Overdose risk when stacking drugs (v0.41.0)
            if (_random.NextDouble() < GameConfig.DrugOverdoseChance)
            {
                long hpLoss = (long)(character.MaxHP * GameConfig.DrugOverdoseHPLoss);
                character.HP = Math.Max(1, character.HP - hpLoss);
                character.Addict = Math.Min(100, (int)(character.Addict * GameConfig.DrugOverdoseAddictionMultiplier) + 10);
                return (false, $"OVERDOSE! The substances react violently! You lose {hpLoss} HP and your addiction worsens!");
            }
            // No overdose — replace current drug
            character.ActiveDrug = DrugType.None;
            character.DrugEffectDays = 0;
        }

        character.ActiveDrug = drug;

        // Duration based on drug type and potency
        character.DrugEffectDays = drug switch
        {
            DrugType.Steroids => 3 + potency,
            DrugType.BerserkerRage => 1,
            DrugType.Haste => 2 + potency,
            DrugType.QuickSilver => 2 + potency,
            DrugType.ManaBoost => 3 + potency,
            DrugType.ThirdEye => 3 + potency,
            DrugType.Ironhide => 2 + potency,
            DrugType.Stoneskin => 2 + potency,
            DrugType.DarkEssence => 1,
            DrugType.DemonBlood => 2,
            _ => 1
        };

        // Drug tolerance — reduces duration with repeated use (v0.41.0)
        int drugKey = (int)drug;
        if (character.DrugTolerance == null)
            character.DrugTolerance = new Dictionary<int, int>();
        if (!character.DrugTolerance.ContainsKey(drugKey))
            character.DrugTolerance[drugKey] = 0;
        character.DrugTolerance[drugKey]++;
        int tolerancePenalty = Math.Min(character.DrugEffectDays - 1, character.DrugTolerance[drugKey] - 1);
        character.DrugEffectDays = Math.Max(1, character.DrugEffectDays - tolerancePenalty);

        // Steroids use separate tracking
        if (drug == DrugType.Steroids)
        {
            character.SteroidDays = character.DrugEffectDays;
        }

        // Addiction risk
        int addictionRisk = GetAddictionRisk(drug);
        if (_random.Next(100) < addictionRisk)
        {
            character.Addict = Math.Min(100, character.Addict + _random.Next(5, 15));
        }

        return (true, $"You take the {drug}. You feel its effects coursing through you!");
    }

    /// <summary>
    /// Get stat bonuses from active drug
    /// </summary>
    public static DrugEffects GetDrugEffects(Character character)
    {
        if (!character.OnDrugs) return new DrugEffects();

        return character.ActiveDrug switch
        {
            DrugType.Steroids => new DrugEffects { StrengthBonus = 20, DamageBonus = 15 },
            DrugType.BerserkerRage => new DrugEffects { StrengthBonus = 30, AttackBonus = 25, DefensePenalty = 20 },
            DrugType.Haste => new DrugEffects { AgilityBonus = 25, ExtraAttacks = 1, HPDrain = 5 },
            DrugType.QuickSilver => new DrugEffects { DexterityBonus = 20, CritBonus = 15 },
            DrugType.ManaBoost => new DrugEffects { ManaBonus = 50, SpellPowerBonus = 20 },
            DrugType.ThirdEye => new DrugEffects { WisdomBonus = 15, MagicResistBonus = 25 },
            DrugType.Ironhide => new DrugEffects { ConstitutionBonus = 25, DefenseBonus = 20, AgilityPenalty = 10 },
            DrugType.Stoneskin => new DrugEffects { ArmorBonus = 30, SpeedPenalty = 15 },
            DrugType.DarkEssence => new DrugEffects { StrengthBonus = 15, AgilityBonus = 15, DexterityBonus = 15, ManaBonus = 25 },
            DrugType.DemonBlood => new DrugEffects { DamageBonus = 25, DarknessBonus = 10 },
            _ => new DrugEffects()
        };
    }

    /// <summary>
    /// Process daily drug effects (withdrawal, duration reduction)
    /// </summary>
    public static string ProcessDailyDrugEffects(Character character)
    {
        var messages = new List<string>();

        // Reduce drug duration
        if (character.DrugEffectDays > 0)
        {
            character.DrugEffectDays--;
            if (character.DrugEffectDays == 0)
            {
                // Check drug type BEFORE clearing it for crash effects
                var expiringDrug = character.ActiveDrug;
                messages.Add($"The effects of {expiringDrug} have worn off.");

                // Crash effects for some drugs
                if (expiringDrug == DrugType.DarkEssence)
                {
                    character.HP = Math.Max(1, character.HP - character.MaxHP / 4);
                    messages.Add("You crash hard from the Dark Essence. Your body aches.");
                }

                character.ActiveDrug = DrugType.None;
            }
        }

        // Reduce steroid duration
        if (character.SteroidDays > 0)
        {
            character.SteroidDays--;
        }

        // Withdrawal effects for addicts
        if (character.IsAddicted && !character.OnDrugs)
        {
            int withdrawalSeverity = character.Addict / 25; // 1-4 severity

            // Stat penalties during withdrawal
            character.Strength = Math.Max(1, character.Strength - withdrawalSeverity);
            character.Agility = Math.Max(1, character.Agility - withdrawalSeverity);

            if (withdrawalSeverity >= 2)
            {
                messages.Add("Your hands shake... you crave your next fix.");
            }
            if (withdrawalSeverity >= 3)
            {
                messages.Add("The withdrawal is agonizing. Your body screams for drugs.");
            }

            // Slow addiction recovery if clean
            if (_random.Next(100) < 20)
            {
                character.Addict = Math.Max(0, character.Addict - 1);
            }
        }

        return string.Join(" ", messages);
    }

    /// <summary>
    /// Get addiction risk percentage for a drug
    /// </summary>
    private static int GetAddictionRisk(DrugType drug)
    {
        return drug switch
        {
            DrugType.Steroids => 15,
            DrugType.BerserkerRage => 10,
            DrugType.Haste => 5,
            DrugType.QuickSilver => 5,
            DrugType.ManaBoost => 3,
            DrugType.ThirdEye => 3,
            DrugType.Ironhide => 5,
            DrugType.Stoneskin => 3,
            DrugType.DarkEssence => 40,
            DrugType.DemonBlood => 50,
            _ => 0
        };
    }
}

/// <summary>
/// Stat effects from drugs
/// </summary>
public class DrugEffects
{
    public int StrengthBonus { get; set; }
    public int AgilityBonus { get; set; }
    public int DexterityBonus { get; set; }
    public int ConstitutionBonus { get; set; }
    public int WisdomBonus { get; set; }
    public int DamageBonus { get; set; }
    public int AttackBonus { get; set; }
    public int DefenseBonus { get; set; }
    public int ArmorBonus { get; set; }
    public int ManaBonus { get; set; }
    public int SpellPowerBonus { get; set; }
    public int CritBonus { get; set; }
    public int MagicResistBonus { get; set; }
    public int ExtraAttacks { get; set; }

    // Penalties
    public int DefensePenalty { get; set; }
    public int AgilityPenalty { get; set; }
    public int SpeedPenalty { get; set; }
    public int HPDrain { get; set; }

    // Special
    public int DarknessBonus { get; set; }
} 
