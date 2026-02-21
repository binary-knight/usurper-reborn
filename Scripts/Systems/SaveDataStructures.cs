using System;
using System.Collections.Generic;

namespace UsurperRemake.Systems
{
    /// <summary>
    /// Main save game data structure
    /// </summary>
    public class SaveGameData
    {
        public int Version { get; set; }
        public DateTime SaveTime { get; set; }
        public DateTime LastDailyReset { get; set; }
        public int CurrentDay { get; set; }
        public DailyCycleMode DailyCycleMode { get; set; }
        public PlayerData Player { get; set; } = new();
        public List<NPCData> NPCs { get; set; } = new();
        public WorldStateData WorldState { get; set; } = new();
        public DailySettings Settings { get; set; } = new();

        // Story systems
        public StorySystemsData StorySystems { get; set; } = new();

        // Telemetry settings (opt-in)
        public TelemetryData? Telemetry { get; set; }
    }

    /// <summary>
    /// Data for all story/narrative systems
    /// </summary>
    public class StorySystemsData
    {
        // Ocean Philosophy
        public int AwakeningLevel { get; set; }
        public List<int> CollectedFragments { get; set; } = new();
        public List<int> ExperiencedMoments { get; set; } = new();

        // Seven Seals
        public List<int> CollectedSeals { get; set; } = new();

        // Companions (story characters like Lyris, Kael, Mira, Vex)
        public List<CompanionSaveInfo> Companions { get; set; } = new();
        public List<int> ActiveCompanionIds { get; set; } = new();
        public List<CompanionDeathInfo> FallenCompanions { get; set; } = new();

        // Dungeon party NPCs (spouses, team members, lovers added via party management)
        public List<string> DungeonPartyNPCIds { get; set; } = new();

        // Dungeon party player echoes (recruited via Team Corner)
        public List<string> DungeonPartyPlayerNames { get; set; } = new();

        // Grief state - full grief system data (supports multiple griefs)
        public int GriefStage { get; set; }  // Legacy field for backwards compatibility
        public int GriefDaysRemaining { get; set; }  // Legacy
        public string GriefCompanionName { get; set; } = "";  // Legacy
        public List<GriefStateSaveData> ActiveGriefs { get; set; } = new();  // Full grief states
        public List<GriefStateSaveData> ActiveNpcGriefs { get; set; } = new();  // NPC teammate grief
        public List<GriefMemorySaveData> GriefMemories { get; set; } = new();  // Memories of fallen

        // Story progression flags
        public Dictionary<string, bool> StoryFlags { get; set; } = new();
        public int CurrentCycle { get; set; } = 1;

        // Collected artifacts (ArtifactType as int)
        public List<int> CollectedArtifacts { get; set; } = new();

        // Old God defeat states - OldGodType (int) -> GodStatus (int)
        // Tracks which Old Gods have been defeated, saved, allied, etc.
        public Dictionary<int, int> OldGodStates { get; set; } = new();

        // God worship - player name -> god name
        public Dictionary<string, string> PlayerGods { get; set; } = new();

        // Family system - children
        public List<ChildData> Children { get; set; } = new();

        // Jungian Archetype tracking
        public ArchetypeTrackerData? ArchetypeTracker { get; set; }

        // Royal Court Political Systems
        public RoyalCourtSaveData? RoyalCourt { get; set; }

        // Relationship System - all character relationships
        public List<RelationshipSaveData> Relationships { get; set; } = new();

        // ===== NEW NARRATIVE SYSTEMS =====

        // Stranger/Noctura Encounter System
        public StrangerEncounterData? StrangerEncounters { get; set; }

        // Faction System
        public FactionSaveData? Factions { get; set; }

        // Town NPC Story System (memorable NPCs with arcs)
        public TownNPCStorySaveData? TownNPCStories { get; set; }

        // Dream System
        public DreamSaveData? Dreams { get; set; }

        // NPC Marriage System - tracks NPC-to-NPC marriages
        public List<NPCMarriageSaveData> NPCMarriages { get; set; } = new();

        // Affairs System - tracks player affairs with married NPCs
        public List<AffairSaveData> Affairs { get; set; } = new();

        // Social emergence - cultural memes (v0.42.0)
        public CulturalMemeSaveData? CulturalMemes { get; set; }
    }

    /// <summary>
    /// Save data for an NPC-to-NPC marriage
    /// </summary>
    public class NPCMarriageSaveData
    {
        public string Npc1Id { get; set; } = "";
        public string Npc2Id { get; set; } = "";
    }

    /// <summary>
    /// Save data for a player's affair with a married NPC
    /// </summary>
    public class AffairSaveData
    {
        public string MarriedNpcId { get; set; } = "";
        public string SeducerId { get; set; } = "";
        public int AffairProgress { get; set; }
        public int SecretMeetings { get; set; }
        public int SpouseSuspicion { get; set; }
        public bool IsActive { get; set; }
        public DateTime LastInteraction { get; set; }
    }


    public class CompanionSaveInfo
    {
        public int Id { get; set; }
        public bool IsRecruited { get; set; }
        public bool IsActive { get; set; }
        public bool IsDead { get; set; }
        public int LoyaltyLevel { get; set; }
        public int TrustLevel { get; set; }
        public int RomanceLevel { get; set; }
        public bool PersonalQuestStarted { get; set; }
        public bool PersonalQuestCompleted { get; set; }
        public int RecruitedDay { get; set; }

        // Level and experience
        public int Level { get; set; }
        public long Experience { get; set; }

        // Base stats (to preserve level-up gains)
        public int BaseStatsHP { get; set; }
        public int BaseStatsAttack { get; set; }
        public int BaseStatsDefense { get; set; }
        public int BaseStatsMagicPower { get; set; }
        public int BaseStatsSpeed { get; set; }
        public int BaseStatsHealingPower { get; set; }

        // Equipment (slot enum int -> equipment database ID)
        public Dictionary<int, int> EquippedItemsSave { get; set; } = new();

        // Disabled ability IDs
        public List<string> DisabledAbilities { get; set; } = new();
    }

    public class CompanionDeathInfo
    {
        public int CompanionId { get; set; }
        public int DeathType { get; set; }
        public string Circumstance { get; set; } = "";
        public string LastWords { get; set; } = "";
        public int DeathDay { get; set; }
    }

    /// <summary>
    /// Player data for save system
    /// </summary>
    public class PlayerData
    {
        // Unique player identifier (critical for romance/family systems)
        public string Id { get; set; } = "";

        // Basic info
        public string Name1 { get; set; } = "";
        public string Name2 { get; set; } = "";
        public string RealName { get; set; } = "";
        
        // Core stats
        public int Level { get; set; }
        public long Experience { get; set; }
        public long HP { get; set; }
        public long MaxHP { get; set; }
        public long Gold { get; set; }
        public long BankGold { get; set; }
        
        // Attributes
        public long Strength { get; set; }
        public long Defence { get; set; }
        public long Stamina { get; set; }
        public long Agility { get; set; }
        public long Charisma { get; set; }
        public long Dexterity { get; set; }
        public long Wisdom { get; set; }
        public long Intelligence { get; set; }
        public long Constitution { get; set; }
        public long Mana { get; set; }
        public long MaxMana { get; set; }

        // Equipment and items
        public long Healing { get; set; }  // CRITICAL: Healing potions count
        public long ManaPotions { get; set; }  // Mana potions (bought at Magic Shop)
        public long WeapPow { get; set; }  // CRITICAL: Weapon power
        public long ArmPow { get; set; }   // CRITICAL: Armor power
        
        // Character details
        public CharacterRace Race { get; set; }
        public CharacterClass Class { get; set; }
        public char Sex { get; set; }
        public int Age { get; set; }
        public DifficultyMode Difficulty { get; set; } = DifficultyMode.Normal;
        
        // Game state
        public string CurrentLocation { get; set; } = "";
        public int TurnCount { get; set; }  // World simulation turn counter (counts up from 0)
        public int TurnsRemaining { get; set; }  // Legacy: now just stores manual override
        public int DaysInPrison { get; set; }
        public bool CellDoorOpen { get; set; }  // Has been rescued
        public string RescuedBy { get; set; } = "";  // Name of rescuer
        public int PrisonEscapes { get; set; }  // Escape attempts remaining
        
        // Daily limits
        public int Fights { get; set; }
        public int PFights { get; set; }
        public int TFights { get; set; }
        public int Thiefs { get; set; }
        public int Brawls { get; set; }
        public int Assa { get; set; }
        public int DarkNr { get; set; }  // Dark deeds remaining today
        public int ChivNr { get; set; }  // Chivalry deeds remaining today
        
        // Items and equipment
        public int[] Items { get; set; } = new int[0];
        public int[] ItemTypes { get; set; } = new int[0];

        // NEW: Modern RPG Equipment System
        public Dictionary<int, int> EquippedItems { get; set; } = new(); // EquipmentSlot -> Equipment ID

        // Curse status for equipped items
        public bool WeaponCursed { get; set; }
        public bool ArmorCursed { get; set; }
        public bool ShieldCursed { get; set; }

        // Player inventory (dungeon loot, etc.)
        public List<InventoryItemData> Inventory { get; set; } = new();

        // Dynamic equipment (items equipped from inventory/dungeon loot that need to be restored)
        public List<DynamicEquipmentData> DynamicEquipment { get; set; } = new();

        // Base stats (without equipment bonuses)
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

        // Ruler status
        public bool King { get; set; }  // Is the player the current ruler?

        // Social/Team
        public string Team { get; set; } = "";
        public string TeamPassword { get; set; } = "";
        public bool IsTeamLeader { get; set; }
        public int TeamRec { get; set; }  // Team record, days had town
        public int BGuard { get; set; }   // Type of guard
        
        // Status
        public long Chivalry { get; set; }
        public long Darkness { get; set; }
        public int Mental { get; set; }
        public int Poison { get; set; }

        // Active status effects (StatusEffect -> remaining duration in rounds)
        public Dictionary<int, int> ActiveStatuses { get; set; } = new();
        public int GnollP { get; set; }  // Gnoll poison (temporary)
        public int Addict { get; set; }  // Drug addiction level
        public int SteroidDays { get; set; }  // Days remaining on steroids
        public int DrugEffectDays { get; set; }  // Days remaining on drug effects
        public int ActiveDrug { get; set; }  // Currently active drug type (DrugType enum)
        public int Mercy { get; set; }   // Mercy counter

        // Disease status
        public bool Blind { get; set; }
        public bool Plague { get; set; }
        public bool Smallpox { get; set; }
        public bool Measles { get; set; }
        public bool Leprosy { get; set; }
        public bool LoversBane { get; set; }  // STD from Love Street

        // Divine Wrath System - punishment for betraying your god
        public int DivineWrathLevel { get; set; }           // 0 = none, 1-3 = severity
        public string AngeredGodName { get; set; } = "";    // The god that was angered
        public string BetrayedForGodName { get; set; } = ""; // The god player sacrificed to instead
        public bool DivineWrathPending { get; set; }        // Has punishment triggered yet?
        public int DivineWrathTurnsRemaining { get; set; }  // Turns until wrath fades

        // Royal Loan (from the Crown)
        public long RoyalLoanAmount { get; set; }
        public int RoyalLoanDueDay { get; set; }
        public bool RoyalLoanBountyPosted { get; set; }

        // Noble Title (knighthood from king)
        public string? NobleTitle { get; set; }

        // Royal Mercenaries (hired bodyguards for king's dungeon party)
        public List<RoyalMercenarySaveData>? RoyalMercenaries { get; set; }

        // Blood Price / Murder Weight System (v0.42.0)
        public float MurderWeight { get; set; }
        public List<string> PermakillLog { get; set; } = new();
        public DateTime LastMurderWeightDecay { get; set; } = DateTime.MinValue;

        // Dev menu flag - permanently disables Steam achievements
        public bool DevMenuUsed { get; set; }

        // Character settings
        public bool AutoHeal { get; set; }  // Auto-heal in battle
        public CombatSpeed CombatSpeed { get; set; } = CombatSpeed.Normal;  // Combat text speed
        public bool SkipIntimateScenes { get; set; }  // Skip detailed intimate scenes (fade to black)
        public bool ScreenReaderMode { get; set; }  // Simplified text for screen readers (accessibility)
        public ColorThemeType ColorTheme { get; set; } = ColorThemeType.Default;  // Player-selected color theme
        public int Loyalty { get; set; }    // Loyalty percentage (0-100)
        public int Haunt { get; set; }      // How many demons haunt player
        public char Master { get; set; }    // Level master player uses
        public bool WellWish { get; set; }  // Has visited wishing well

        // Physical appearance
        public int Height { get; set; }
        public int Weight { get; set; }
        public int Eyes { get; set; }
        public int Hair { get; set; }
        public int Skin { get; set; }

        // Character flavor text
        public List<string> Phrases { get; set; } = new();      // Combat phrases (6 phrases)
        public List<string> Description { get; set; } = new();  // Character description (4 lines)
        
        // Relationships
        public Dictionary<string, float> Relationships { get; set; } = new();

        // Romance Tracker Data
        public RomanceTrackerData RomanceData { get; set; } = new();

        // Quests
        public List<QuestData> ActiveQuests { get; set; } = new();
        
        // Achievements
        public Dictionary<string, bool> Achievements { get; set; } = new();

        // Learned combat abilities (non-caster classes)
        public List<string> LearnedAbilities { get; set; } = new();

        // Combat quickbar slots 1-9 (spell IDs like "spell:5" or ability IDs like "power_strike")
        public List<string?> Quickbar { get; set; } = new();

        // Training system
        public int Trains { get; set; }  // Training sessions available
        public int TrainingPoints { get; set; }
        public Dictionary<string, int> SkillProficiencies { get; set; } = new();  // Skill name -> proficiency level
        public Dictionary<string, int> SkillTrainingProgress { get; set; } = new();  // Skill name -> progress

        // Gold-based stat training (v0.30.9)
        public Dictionary<string, int> StatTrainingCounts { get; set; } = new();

        // NPC team wage tracking (v0.30.9)
        public Dictionary<string, int> UnpaidWageDays { get; set; } = new();

        // Crafting Materials (v0.30.9)
        public Dictionary<string, int> CraftingMaterials { get; set; } = new();

        // Spells array: [spellIndex][0=known, 1=mastered]
        public List<List<bool>> Spells { get; set; } = new();

        // Combat skills array
        public List<int> Skills { get; set; } = new();

        // Legacy equipment slots (for backwards compatibility)
        public int LHand { get; set; }
        public int RHand { get; set; }
        public int Head { get; set; }
        public int Body { get; set; }
        public int Arms { get; set; }
        public int LFinger { get; set; }
        public int RFinger { get; set; }
        public int Legs { get; set; }
        public int Feet { get; set; }
        public int Waist { get; set; }
        public int Neck { get; set; }
        public int Neck2 { get; set; }
        public int Face { get; set; }
        public int Shield { get; set; }
        public int Hands { get; set; }
        public int ABody { get; set; }

        // Combat flags
        public bool Immortal { get; set; }
        public string BattleCry { get; set; } = "";
        public int BGuardNr { get; set; }  // Number of door guards

        // Kill statistics
        public int MKills { get; set; }   // Monster kills
        public int MDefeats { get; set; } // Monster defeats
        public int PKills { get; set; }   // Player kills
        public int PDefeats { get; set; } // Player defeats

        // Timestamps
        public DateTime LastLogin { get; set; }
        public DateTime AccountCreated { get; set; }

        // Note: Gym removed - these fields kept for save compatibility but unused
        public DateTime LastStrengthTraining { get; set; }
        public DateTime LastDexterityTraining { get; set; }
        public DateTime LastTugOfWar { get; set; }
        public DateTime LastWrestling { get; set; }

        // Player statistics
        public PlayerStatistics? Statistics { get; set; }

        // Player achievements
        public PlayerAchievementsData? AchievementsData { get; set; }

        // Home Upgrade System (v0.44.0 overhaul)
        public int HomeLevel { get; set; } = 0;
        public int ChestLevel { get; set; } = 0;
        public int TrainingRoomLevel { get; set; } = 0;
        public int GardenLevel { get; set; } = 0;
        public int BedLevel { get; set; } = 0;
        public int HearthLevel { get; set; } = 0;
        public bool HasTrophyRoom { get; set; } = false;
        public bool HasTeleportCircle { get; set; } = false;
        public bool HasLegendaryArmory { get; set; } = false;
        public bool HasVitalityFountain { get; set; } = false;
        public bool HasStudy { get; set; } = false;
        public bool HasServants { get; set; } = false;
        public int PermanentDamageBonus { get; set; } = 0;
        public int PermanentDefenseBonus { get; set; } = 0;
        public long BonusMaxHP { get; set; } = 0;
        public int HomeRestsToday { get; set; } = 0;
        public int HerbsGatheredToday { get; set; } = 0;
        public int WellRestedCombats { get; set; } = 0;
        public float WellRestedBonus { get; set; } = 0f;
        public List<InventoryItemData>? ChestContents { get; set; }

        // Faction consumable properties (v0.40.2)
        public int PoisonCoatingCombats { get; set; }
        public int SmokeBombs { get; set; }
        public int InnerSanctumLastDay { get; set; }

        // Daily tracking (real-world-date based, online mode)
        public DateTime LastDailyResetBoundary { get; set; }
        public DateTime LastPrayerRealDate { get; set; }
        public DateTime LastInnerSanctumRealDate { get; set; }
        public DateTime LastBindingOfSoulsRealDate { get; set; }
        public int SethFightsToday { get; set; }
        public int ArmWrestlesToday { get; set; }
        public int RoyQuestsToday { get; set; }

        // Dark Alley Overhaul (v0.41.0)
        public int GroggoShadowBlessingDex { get; set; }
        public int SteroidShopPurchases { get; set; }
        public int AlchemistINTBoosts { get; set; }
        public int GamblingRoundsToday { get; set; }
        public int PitFightsToday { get; set; }
        public long LoanAmount { get; set; }
        public int LoanDaysRemaining { get; set; }
        public long LoanInterestAccrued { get; set; }
        public int DarkAlleyReputation { get; set; }
        public Dictionary<int, int>? DrugTolerance { get; set; }
        public bool SafeHouseResting { get; set; }

        // Recurring Duelist Rival
        public DuelistData? RecurringDuelist { get; set; }

        // Dungeon progression - tracks which boss/seal floors have been cleared
        public HashSet<int> ClearedSpecialFloors { get; set; } = new();

        // Dungeon floor persistence - tracks room state, respawn timers, etc.
        public Dictionary<int, DungeonFloorStateData> DungeonFloorStates { get; set; } = new();

        // Hint system - tracks which contextual hints have been shown to the player
        public HashSet<string> HintsShown { get; set; } = new();
    }

    /// <summary>
    /// Player achievements data for save system
    /// </summary>
    public class PlayerAchievementsData
    {
        public HashSet<string> UnlockedAchievements { get; set; } = new();
        public Dictionary<string, DateTime> UnlockDates { get; set; } = new();
    }

    /// <summary>
    /// Recurring duelist rival data for save system
    /// </summary>
    public class DuelistData
    {
        public string Name { get; set; } = "";
        public string Weapon { get; set; } = "Dueling Blade";
        public int Level { get; set; } = 1;
        public int TimesEncountered { get; set; } = 0;
        public int PlayerWins { get; set; } = 0;
        public int PlayerLosses { get; set; } = 0;
        public bool WasInsulted { get; set; } = false;
        public bool IsDead { get; set; } = false;
    }

    /// <summary>
    /// NPC data for save system
    /// </summary>
    public class NPCData
    {
        public string Id { get; set; } = "";
        public string CharacterID { get; set; } = "";  // The Character.ID property (name-based identifier used by RomanceTracker)
        public string Name { get; set; } = "";
        public string Archetype { get; set; } = "citizen";  // NPC archetype (citizen, thug, merchant, etc.)
        public int Level { get; set; }
        public long HP { get; set; }
        public long MaxHP { get; set; }
        public long BaseMaxHP { get; set; }  // Base HP before equipment bonuses
        public long BaseMaxMana { get; set; }  // Base Mana before equipment bonuses
        public string Location { get; set; } = "";
        public long Gold { get; set; }
        public long BankGold { get; set; }
        public int[] Items { get; set; } = new int[0];

        // Character stats
        public long Experience { get; set; }
        public long Strength { get; set; }
        public long Defence { get; set; }
        public long Agility { get; set; }
        public long Dexterity { get; set; }
        public long Mana { get; set; }
        public long MaxMana { get; set; }
        public long WeapPow { get; set; }
        public long ArmPow { get; set; }

        // Base stats (without equipment bonuses) - for RecalculateStats
        public long BaseStrength { get; set; }
        public long BaseDefence { get; set; }
        public long BaseDexterity { get; set; }
        public long BaseAgility { get; set; }
        public long BaseStamina { get; set; }
        public long BaseConstitution { get; set; }
        public long BaseIntelligence { get; set; }
        public long BaseWisdom { get; set; }
        public long BaseCharisma { get; set; }

        // Class and race
        public CharacterClass Class { get; set; }
        public CharacterRace Race { get; set; }
        public char Sex { get; set; }

        // Team and political status
        public string Team { get; set; } = "";
        public bool IsTeamLeader { get; set; }
        public bool IsKing { get; set; }

        // Death status - permanent death tracking
        public bool IsDead { get; set; }

        // Lifecycle - aging and natural death
        public int Age { get; set; }
        public DateTime BirthDate { get; set; } = DateTime.MinValue;
        public bool IsAgedDeath { get; set; }
        public bool IsPermaDead { get; set; }  // Permanent combat death (v0.42.0)
        public DateTime? PregnancyDueDate { get; set; }

        // Dialogue tracking - prevents repetition across sessions
        public List<string>? RecentDialogueIds { get; set; }

        // Social emergence (v0.42.0)
        public string EmergentRole { get; set; } = "";
        public int RoleStabilityTicks { get; set; }

        // Marriage status
        public bool IsMarried { get; set; }
        public bool Married { get; set; }
        public string SpouseName { get; set; } = "";
        public int MarriedTimes { get; set; }

        // Faction affiliation (nullable - -1 means no faction)
        public int NPCFaction { get; set; } = -1;

        // Alignment
        public long Chivalry { get; set; }
        public long Darkness { get; set; }

        // AI state
        public PersonalityData? PersonalityProfile { get; set; }
        public List<MemoryData> Memories { get; set; } = new();
        public List<GoalData> CurrentGoals { get; set; } = new();
        public EmotionalStateData? EmotionalState { get; set; }

        // Relationships
        public Dictionary<string, float> Relationships { get; set; } = new();

        // Enemy list (NPC IDs of rivals)
        public List<string> Enemies { get; set; } = new();

        // Marketplace inventory - items NPC has to sell
        public List<MarketItemData> MarketInventory { get; set; } = new();

        // Modern RPG Equipment System - equipped items on this NPC
        public Dictionary<int, int> EquippedItems { get; set; } = new(); // EquipmentSlot -> Equipment ID

        // Dynamic equipment that this NPC has equipped (dungeon loot, etc.)
        public List<DynamicEquipmentData> DynamicEquipment { get; set; } = new();
    }

    /// <summary>
    /// Marketplace item data for serialization
    /// </summary>
    public class MarketItemData
    {
        public string ItemName { get; set; } = "";
        public long ItemValue { get; set; }
        public global::ObjType ItemType { get; set; }
        public int Attack { get; set; }
        public int Armor { get; set; }
        public int Strength { get; set; }
        public int Defence { get; set; }
        public bool IsCursed { get; set; }
    }

    /// <summary>
    /// Player inventory item data for serialization (dungeon loot, etc.)
    /// </summary>
    public class InventoryItemData
    {
        public string Name { get; set; } = "";
        public long Value { get; set; }
        public global::ObjType Type { get; set; }
        public int Attack { get; set; }
        public int Armor { get; set; }
        public int Strength { get; set; }
        public int Dexterity { get; set; }
        public int Wisdom { get; set; }
        public int Defence { get; set; }
        public int HP { get; set; }
        public int Mana { get; set; }
        public int Charisma { get; set; }
        public int MinLevel { get; set; }
        public bool IsCursed { get; set; }
        // Cursed is now an alias for IsCursed for backwards compatibility
        public bool Cursed
        {
            get => IsCursed;
            set => IsCursed = value;
        }
        public bool IsIdentified { get; set; } = true;  // Defaults to true for backwards compat
        public bool Shop { get; set; }
        public bool Dungeon { get; set; }
        public List<string> Description { get; set; } = new();
    }

    /// <summary>
    /// Dynamic equipment data for serialization (equipped items from dungeon loot)
    /// </summary>
    public class DynamicEquipmentData
    {
        public int Id { get; set; }  // The dynamic ID assigned by EquipmentDatabase
        public string Name { get; set; } = "";
        public string Description { get; set; } = "";
        public int Slot { get; set; }  // EquipmentSlot enum as int
        public int WeaponPower { get; set; }
        public int ArmorClass { get; set; }
        public int ShieldBonus { get; set; }
        public int BlockChance { get; set; }
        public int StrengthBonus { get; set; }
        public int DexterityBonus { get; set; }
        public int ConstitutionBonus { get; set; }
        public int IntelligenceBonus { get; set; }
        public int WisdomBonus { get; set; }
        public int CharismaBonus { get; set; }
        public int MaxHPBonus { get; set; }
        public int MaxManaBonus { get; set; }
        public int DefenceBonus { get; set; }
        public int MinLevel { get; set; }
        public long Value { get; set; }
        public bool IsCursed { get; set; }
        public int Rarity { get; set; }  // EquipmentRarity enum as int
        public int WeaponType { get; set; }  // WeaponType enum as int
        public int Handedness { get; set; }  // WeaponHandedness enum as int
        public int ArmorType { get; set; }  // ArmorType enum as int

        // Secondary stats (v0.40.1)
        public int StaminaBonus { get; set; }
        public int AgilityBonus { get; set; }

        // Special properties (v0.40.1)
        public int CriticalChanceBonus { get; set; }
        public int CriticalDamageBonus { get; set; }
        public int MagicResistance { get; set; }
        public int PoisonDamage { get; set; }
        public int LifeSteal { get; set; }

        // Elemental enchant flags (v0.30.9+)
        public bool HasFireEnchant { get; set; }
        public bool HasFrostEnchant { get; set; }
        public bool HasLightningEnchant { get; set; }
        public bool HasPoisonEnchant { get; set; }
        public bool HasHolyEnchant { get; set; }
        public bool HasShadowEnchant { get; set; }

        // Proc-based enchantments (v0.40.5)
        public int ManaSteal { get; set; }
        public int ArmorPiercing { get; set; }
        public int Thorns { get; set; }
        public int HPRegen { get; set; }
        public int ManaRegen { get; set; }
    }

    /// <summary>
    /// Marketplace listing data for persistence
    /// </summary>
    public class MarketListingData
    {
        public MarketItemData Item { get; set; } = new();
        public string Seller { get; set; } = "";
        public bool IsNPCSeller { get; set; }
        public string SellerNPCId { get; set; } = "";
        public long Price { get; set; }
        public DateTime Posted { get; set; }
    }

    /// <summary>
    /// World state data
    /// </summary>
    public class WorldStateData
    {
        // Economic state
        public int BankInterestRate { get; set; }
        public int TownPotValue { get; set; }

        // Political state
        public string? CurrentRuler { get; set; }

        // World events
        public List<WorldEventData> ActiveEvents { get; set; } = new();

        // Active quests
        public List<QuestData> ActiveQuests { get; set; } = new();

        // Shop inventories
        public Dictionary<string, ShopInventoryData> ShopInventories { get; set; } = new();

        // News and history
        public List<NewsEntryData> RecentNews { get; set; } = new();

        // God system state
        public Dictionary<string, GodStateData> GodStates { get; set; } = new();

        // Marketplace listings
        public List<MarketListingData> MarketplaceListings { get; set; } = new();
    }

    /// <summary>
    /// Daily cycle settings
    /// </summary>
    public class DailySettings
    {
        public DailyCycleMode Mode { get; set; }
        public DateTime LastResetTime { get; set; }
        public bool AutoSaveEnabled { get; set; }
        public TimeSpan AutoSaveInterval { get; set; }
    }

    /// <summary>
    /// Save file information for UI
    /// </summary>
    public class SaveInfo
    {
        public string PlayerName { get; set; } = "";
        public string ClassName { get; set; } = "";
        public DateTime SaveTime { get; set; }
        public int Level { get; set; }
        public int CurrentDay { get; set; }
        public int TurnsRemaining { get; set; }
        public string FileName { get; set; } = "";
        public bool IsAutosave { get; set; }
        public string SaveType { get; set; } = "Manual Save";
    }

    /// <summary>
    /// Quest data for save system - matches Quest class structure
    /// </summary>
    public class QuestData
    {
        public string Id { get; set; } = "";
        public string Title { get; set; } = "";
        public string Initiator { get; set; } = "";
        public string Comment { get; set; } = "";
        public QuestStatus Status { get; set; }
        public DateTime StartTime { get; set; }
        public int QuestType { get; set; }
        public int QuestTarget { get; set; }
        public int Difficulty { get; set; }
        public string Occupier { get; set; } = "";
        public int OccupiedDays { get; set; }
        public int DaysToComplete { get; set; }
        public int MinLevel { get; set; }
        public int MaxLevel { get; set; }
        public int Reward { get; set; }
        public int RewardType { get; set; }
        public int Penalty { get; set; }
        public int PenaltyType { get; set; }
        public string OfferedTo { get; set; } = "";
        public bool Forced { get; set; }
        public string TargetNPCName { get; set; } = "";
        public List<QuestObjectiveData> Objectives { get; set; } = new();
        public List<QuestMonsterData> Monsters { get; set; } = new();
    }

    /// <summary>
    /// Quest objective data for save system
    /// </summary>
    public class QuestObjectiveData
    {
        public string Id { get; set; } = "";
        public string Description { get; set; } = "";
        public int ObjectiveType { get; set; }
        public string TargetId { get; set; } = "";
        public string TargetName { get; set; } = "";
        public int RequiredProgress { get; set; }
        public int CurrentProgress { get; set; }
        public bool IsOptional { get; set; }
        public int BonusReward { get; set; }
    }

    /// <summary>
    /// Quest monster data for save system
    /// </summary>
    public class QuestMonsterData
    {
        public int MonsterType { get; set; }
        public int Count { get; set; }
        public string MonsterName { get; set; } = "";
    }

    /// <summary>
    /// Personality data for NPCs
    /// </summary>
    public class PersonalityData
    {
        // Core traits
        public float Aggression { get; set; }
        public float Loyalty { get; set; }
        public float Intelligence { get; set; }
        public float Greed { get; set; }
        public float Compassion { get; set; }
        public float Courage { get; set; }
        public float Honesty { get; set; }
        public float Ambition { get; set; }
        public float Vengefulness { get; set; }
        public float Impulsiveness { get; set; }
        public float Caution { get; set; }
        public float Mysticism { get; set; }
        public float Patience { get; set; }

        // Romance/Intimacy traits
        public GenderIdentity Gender { get; set; } = GenderIdentity.Male;
        public SexualOrientation Orientation { get; set; } = SexualOrientation.Bisexual;
        public RomanceStyle IntimateStyle { get; set; } = RomanceStyle.Switch;
        public RelationshipPreference RelationshipPref { get; set; } = RelationshipPreference.Undecided;
        public float Romanticism { get; set; }
        public float Sensuality { get; set; }
        public float Jealousy { get; set; }
        public float Commitment { get; set; }
        public float Adventurousness { get; set; }
        public float Exhibitionism { get; set; }
        public float Voyeurism { get; set; }
        public float Flirtatiousness { get; set; }
        public float Passion { get; set; }
        public float Tenderness { get; set; }
    }

    /// <summary>
    /// Memory data for NPCs
    /// </summary>
    public class MemoryData
    {
        public string Id { get; set; } = "";
        public string Type { get; set; } = "";  // String representation of MemoryType enum
        public string Description { get; set; } = "";
        public string InvolvedCharacter { get; set; } = "";
        public DateTime Timestamp { get; set; }
        public float Importance { get; set; }
        public float EmotionalImpact { get; set; }
        public Dictionary<string, object> Details { get; set; } = new();
    }

    /// <summary>
    /// Goal data for NPCs
    /// </summary>
    public class GoalData
    {
        public string Id { get; set; } = "";
        public string Name { get; set; } = "";
        public string Type { get; set; } = "";  // String representation of GoalType enum
        public float Priority { get; set; }
        public float Progress { get; set; }
        public bool IsActive { get; set; }
        public float TargetValue { get; set; }
        public float CurrentValue { get; set; }
        public DateTime CreatedTime { get; set; }
        public DateTime? CompletionTime { get; set; }
        public Dictionary<string, object> Parameters { get; set; } = new();
    }

    /// <summary>
    /// Emotional state data for NPCs
    /// </summary>
    public class EmotionalStateData
    {
        public float Happiness { get; set; }
        public float Anger { get; set; }
        public float Fear { get; set; }
        public float Trust { get; set; }
        // Extended emotions (v0.29.1)
        public float Confidence { get; set; }
        public float Sadness { get; set; }
        public float Greed { get; set; }
        public float Loneliness { get; set; }
        public float Envy { get; set; }
        public float Pride { get; set; }
        public float Hope { get; set; }
        public float Peace { get; set; }
    }

    /// <summary>
    /// World event data
    /// </summary>
    public class WorldEventData
    {
        public string Id { get; set; } = "";
        public string Type { get; set; } = "";
        public string Title { get; set; } = "";
        public string Description { get; set; } = "";
        public DateTime StartTime { get; set; }
        public DateTime? EndTime { get; set; }
        public Dictionary<string, object> Parameters { get; set; } = new();
        public List<string> AffectedLocations { get; set; } = new();
        public List<string> AffectedNPCs { get; set; } = new();
    }

    /// <summary>
    /// Shop inventory data
    /// </summary>
    public class ShopInventoryData
    {
        public string ShopId { get; set; } = "";
        public DateTime LastRestock { get; set; }
        public Dictionary<string, ShopItemData> Items { get; set; } = new();
        public float PriceModifier { get; set; } = 1.0f;
    }

    /// <summary>
    /// Shop item data
    /// </summary>
    public class ShopItemData
    {
        public string ItemId { get; set; } = "";
        public int Quantity { get; set; }
        public int Price { get; set; }
        public DateTime LastSold { get; set; }
    }

    /// <summary>
    /// News entry data
    /// </summary>
    public class NewsEntryData
    {
        public string Id { get; set; } = "";
        public string Category { get; set; } = "";
        public string Title { get; set; } = "";
        public string Content { get; set; } = "";
        public DateTime Timestamp { get; set; }
        public string Author { get; set; } = "";
        public List<string> Tags { get; set; } = new();
    }

    /// <summary>
    /// God state data
    /// </summary>
    public class GodStateData
    {
        public string GodId { get; set; } = "";
        public string Name { get; set; } = "";
        public long Power { get; set; }
        public int Followers { get; set; }
        public DateTime LastActivity { get; set; }
        public Dictionary<string, object> Attributes { get; set; } = new();
    }

    /// <summary>
    /// Daily cycle modes
    /// </summary>
    public enum DailyCycleMode
    {
        SessionBased,    // New day when turns depleted
        RealTime24Hour,  // Classic midnight reset
        Accelerated4Hour,
        Accelerated8Hour,
        Accelerated12Hour,
        Endless         // No turn limits
    }

    /// <summary>
    /// Quest status enumeration
    /// </summary>
    public enum QuestStatus
    {
        Active,
        Completed,
        Failed,
        Abandoned
    }

    /// <summary>
    /// Memory event types for NPCs
    /// </summary>
    public enum MemoryEventType
    {
        FirstMeeting,
        WasHelped,
        WasHarmed,
        WitnessedCombat,
        ReceivedGift,
        WasRobbed,
        SawDeath,
        SharedMeal,
        Conversation,
        Trade,
        Romance,
        Betrayal,
        Alliance,
        Rivalry
    }

    /// <summary>
    /// Goal types for NPCs
    /// </summary>
    public enum GoalType
    {
        Survival,
        Wealth,
        Power,
        Love,
        Revenge,
        Knowledge,
        Protection,
        Exploration,
        Social,
        Religious
    }

    /// <summary>
    /// Goal status enumeration
    /// </summary>
    public enum GoalStatus
    {
        Active,
        Completed,
        Failed,
        Paused,
        Abandoned
    }

    /// <summary>
    /// Romance tracker data for save system
    /// </summary>
    public class RomanceTrackerData
    {
        public List<LoverData> CurrentLovers { get; set; } = new();
        public List<SpouseData> Spouses { get; set; } = new();
        public List<string> FriendsWithBenefits { get; set; } = new();
        public List<string> Exes { get; set; } = new();
        public List<ExSpouseData> ExSpouses { get; set; } = new();  // Detailed ex-spouse records
        public List<IntimateEncounterData> EncounterHistory { get; set; } = new();
        public Dictionary<string, int> JealousyLevels { get; set; } = new();
        public Dictionary<string, int> AgreedStructures { get; set; } = new(); // RelationshipStructure enum as int
        public List<CuckoldArrangementData> CuckArrangements { get; set; } = new();
        public List<PolyNetworkData> PolyNetworks { get; set; } = new();
        public List<ConversationStateData> ConversationStates { get; set; } = new(); // Flirt progress per NPC
    }

    /// <summary>
    /// Lover data for save system
    /// </summary>
    public class LoverData
    {
        public string NPCId { get; set; } = "";
        public string NPCName { get; set; } = "";
        public int LoveLevel { get; set; }
        public bool IsExclusive { get; set; }
        public bool KnowsAboutOthers { get; set; }
        public List<string> MetamorsList { get; set; } = new();
        public DateTime RelationshipStart { get; set; }
        public DateTime? LastIntimateDate { get; set; }
    }

    /// <summary>
    /// Spouse data for save system
    /// </summary>
    public class SpouseData
    {
        public string NPCId { get; set; } = "";
        public string NPCName { get; set; } = "";
        public DateTime MarriedDate { get; set; }
        public int LoveLevel { get; set; }
        public bool AcceptsPolyamory { get; set; }
        public bool KnowsAboutOthers { get; set; }
        public int Children { get; set; }
        public DateTime? LastIntimateDate { get; set; }
    }

    /// <summary>
    /// Ex-spouse data for save system - preserves marriage history after divorce
    /// </summary>
    public class ExSpouseData
    {
        public string NPCId { get; set; } = "";
        public string NPCName { get; set; } = "";
        public DateTime MarriedDate { get; set; }
        public DateTime DivorceDate { get; set; }
        public int ChildrenTogether { get; set; }
        public string DivorceReason { get; set; } = "";
        public bool PlayerInitiated { get; set; }
    }

    /// <summary>
    /// Intimate encounter data for save system
    /// </summary>
    public class IntimateEncounterData
    {
        public DateTime Date { get; set; }
        public string Location { get; set; } = "";
        public List<string> PartnerIds { get; set; } = new();
        public List<string> PartnerNames { get; set; } = new();
        public int Type { get; set; } // EncounterType enum as int
        public int Mood { get; set; } // IntimacyMood enum as int
        public bool IsFirstTime { get; set; }
        public List<string> WatcherIds { get; set; } = new();
        public string Notes { get; set; } = "";
    }

    /// <summary>
    /// Cuckold arrangement data for save system
    /// </summary>
    public class CuckoldArrangementData
    {
        public string PrimaryPartnerId { get; set; } = "";
        public string PrimaryPartnerName { get; set; } = "";
        public string ThirdPartyId { get; set; } = "";
        public string ThirdPartyName { get; set; } = "";
        public bool PlayerIsWatching { get; set; }
        public DateTime ConsentedDate { get; set; }
        public int EncounterCount { get; set; }
    }

    /// <summary>
    /// Poly network data for save system
    /// </summary>
    public class PolyNetworkData
    {
        public string NetworkName { get; set; } = "";
        public List<string> MemberIds { get; set; } = new();
        public List<string> MemberNames { get; set; } = new();
        public DateTime EstablishedDate { get; set; }
    }

    /// <summary>
    /// Child data for save system
    /// </summary>
    public class ChildData
    {
        public string Name { get; set; } = "";
        public string Mother { get; set; } = "";
        public string Father { get; set; } = "";
        public string MotherID { get; set; } = "";
        public string FatherID { get; set; } = "";
        public string OriginalMother { get; set; } = "";
        public string OriginalFather { get; set; } = "";
        public int Sex { get; set; } // CharacterSex enum as int
        public int Age { get; set; }
        public DateTime BirthDate { get; set; }
        public bool Named { get; set; }
        public int Location { get; set; }
        public int Health { get; set; }
        public int Soul { get; set; }
        public bool MotherAccess { get; set; }
        public bool FatherAccess { get; set; }
        public bool Kidnapped { get; set; }
        public string KidnapperName { get; set; } = "";
        public long RansomDemanded { get; set; }
        public string CursedByGod { get; set; } = "";
        public int Royal { get; set; }
    }

    /// <summary>
    /// Conversation state data for save system (tracks flirt progress per NPC)
    /// </summary>
    public class ConversationStateData
    {
        public string NPCId { get; set; } = "";
        public int FlirtSuccessCount { get; set; }
        public bool LastFlirtWasPositive { get; set; }
        public int TotalConversations { get; set; }
        public int PersonalQuestionsAsked { get; set; }
        public bool HasConfessed { get; set; }
        public bool ConfessionAccepted { get; set; }
        public List<string> TopicsDiscussed { get; set; } = new();
        public DateTime LastConversationDate { get; set; }
    }

    /// <summary>
    /// Persistent dungeon floor state - tracks room exploration, clears, and respawn timing
    /// </summary>
    public class DungeonFloorStateData
    {
        public int FloorLevel { get; set; }
        public DateTime LastClearedAt { get; set; }          // When floor was fully cleared
        public DateTime LastVisitedAt { get; set; }          // When player last visited
        public bool EverCleared { get; set; } = false;       // Has this floor ever been fully cleared? (for first-clear bonus)
        public bool IsPermanentlyClear { get; set; } = false; // Boss/seal floors stay cleared forever
        public bool BossDefeated { get; set; } = false;      // True if the actual boss room boss was defeated
        public string CurrentRoomId { get; set; } = "";      // Where player left off
        public List<DungeonRoomStateData> Rooms { get; set; } = new();
    }

    /// <summary>
    /// Persistent room state within a dungeon floor
    /// </summary>
    public class DungeonRoomStateData
    {
        public string RoomId { get; set; } = "";
        public bool IsExplored { get; set; }
        public bool IsCleared { get; set; }
        public bool TreasureLooted { get; set; }
        public bool TrapTriggered { get; set; }
        public bool EventCompleted { get; set; }
        public bool PuzzleSolved { get; set; }
        public bool RiddleAnswered { get; set; }
        public bool LoreCollected { get; set; }
        public bool InsightGranted { get; set; }
        public bool MemoryTriggered { get; set; }
        public bool SecretBossDefeated { get; set; }
    }

    /// <summary>
    /// Save data for a single grief state (companion or NPC)
    /// </summary>
    public class GriefStateSaveData
    {
        public int CompanionId { get; set; }  // For companion grief (cast to CompanionId enum)
        public string? NpcId { get; set; }  // For NPC grief
        public string CompanionName { get; set; } = "";
        public int DeathType { get; set; }  // Cast to DeathType enum
        public int CurrentStage { get; set; }  // Cast to GriefStage enum
        public int StageStartDay { get; set; }
        public int GriefStartDay { get; set; }
        public int ResurrectionAttempts { get; set; }
        public bool IsComplete { get; set; }
    }

    /// <summary>
    /// Save data for grief memory
    /// </summary>
    public class GriefMemorySaveData
    {
        public int CompanionId { get; set; }  // For companion memories
        public string? NpcId { get; set; }  // For NPC memories
        public string CompanionName { get; set; } = "";
        public string MemoryText { get; set; } = "";
        public int CreatedDay { get; set; }
    }

    /// <summary>
    /// Save data for the entire royal court political system
    /// </summary>
    public class RoyalCourtSaveData
    {
        public string KingName { get; set; } = "";
        public long Treasury { get; set; }
        public long TaxRate { get; set; }
        public long TotalReign { get; set; }
        public int KingTaxPercent { get; set; } = 5;
        public int CityTaxPercent { get; set; } = 2;
        public string DesignatedHeir { get; set; } = "";
        public int KingAI { get; set; } = 1; // CharacterAI: 0=Human, 1=Computer
        public int KingSex { get; set; } = 0; // CharacterSex

        // New in Phase 1 — coronation, tax alignment, monarch history
        public string CoronationDate { get; set; } = "";  // ISO 8601 string
        public int TaxAlignment { get; set; } = 0;        // GameConfig.TaxAlignment enum
        public List<MonarchRecordSaveData> MonarchHistory { get; set; } = new();

        public List<CourtMemberSaveData> CourtMembers { get; set; } = new();
        public List<RoyalHeirSaveData> Heirs { get; set; } = new();
        public RoyalSpouseSaveData? Spouse { get; set; }
        public List<CourtIntrigueSaveData> ActivePlots { get; set; } = new();
        public List<RoyalGuardSaveData> Guards { get; set; } = new();
        public List<MonsterGuardSaveData> MonsterGuards { get; set; } = new();

        // Phase 2 — previously unserialized King fields
        public List<PrisonRecordSaveData> Prisoners { get; set; } = new();
        public List<RoyalOrphanSaveData> Orphans { get; set; } = new();
        public long MagicBudget { get; set; } = 10000;
        public Dictionary<string, bool> EstablishmentStatus { get; set; } = new();
        public string LastProclamation { get; set; } = "";
        public string LastProclamationDate { get; set; } = "";  // ISO 8601
    }

    /// <summary>
    /// Save data for historical monarch record
    /// </summary>
    public class MonarchRecordSaveData
    {
        public string Name { get; set; } = "";
        public string Title { get; set; } = "";
        public int DaysReigned { get; set; }
        public string CoronationDate { get; set; } = ""; // ISO 8601
        public string EndReason { get; set; } = "";
    }

    /// <summary>
    /// Save data for a prison record
    /// </summary>
    public class PrisonRecordSaveData
    {
        public string CharacterName { get; set; } = "";
        public string Crime { get; set; } = "";
        public int Sentence { get; set; }
        public int DaysServed { get; set; }
        public string ImprisonmentDate { get; set; } = "";  // ISO 8601
        public long BailAmount { get; set; }
    }

    /// <summary>
    /// Save data for a royal orphan
    /// </summary>
    public class RoyalOrphanSaveData
    {
        public string Name { get; set; } = "";
        public int Age { get; set; }
        public int Sex { get; set; }
        public string ArrivalDate { get; set; } = "";  // ISO 8601
        public string BackgroundStory { get; set; } = "";
        public int Happiness { get; set; }

        // New fields (defaults are backwards-compatible with old saves)
        public string? MotherName { get; set; }
        public string? FatherName { get; set; }
        public string? MotherID { get; set; }
        public string? FatherID { get; set; }
        public int Race { get; set; } = 0;             // CharacterRace as int
        public string BirthDate { get; set; } = "";    // ISO 8601
        public int Soul { get; set; } = 0;
        public bool IsRealOrphan { get; set; } = false;
    }

    /// <summary>
    /// Save data for a royal NPC guard
    /// </summary>
    public class RoyalGuardSaveData
    {
        public string Name { get; set; } = "";
        public int AI { get; set; }
        public int Sex { get; set; }
        public long DailySalary { get; set; }
        public int Loyalty { get; set; } = 100;
        public bool IsActive { get; set; } = true;
    }

    /// <summary>
    /// Save data for a moat monster guard
    /// </summary>
    public class MonsterGuardSaveData
    {
        public string Name { get; set; } = "";
        public int Level { get; set; }
        public long HP { get; set; }
        public long MaxHP { get; set; }
        public long Strength { get; set; }
        public long Defence { get; set; }
        public long WeapPow { get; set; }
        public long ArmPow { get; set; }
        public string MonsterType { get; set; } = "";
        public long PurchaseCost { get; set; }
        public long DailyFeedingCost { get; set; }
    }

    /// <summary>
    /// Save data for a court member
    /// </summary>
    public class CourtMemberSaveData
    {
        public string Name { get; set; } = "";
        public int Faction { get; set; }
        public int Influence { get; set; }
        public int LoyaltyToKing { get; set; }
        public string Role { get; set; } = "";
        public bool IsPlotting { get; set; }
    }

    /// <summary>
    /// Save data for a royal heir
    /// </summary>
    public class RoyalHeirSaveData
    {
        public string Name { get; set; } = "";
        public int Age { get; set; }
        public int ClaimStrength { get; set; }
        public string ParentName { get; set; } = "";
        public int Sex { get; set; }
        public bool IsDesignated { get; set; }
    }

    /// <summary>
    /// Save data for royal spouse
    /// </summary>
    public class RoyalSpouseSaveData
    {
        public string Name { get; set; } = "";
        public int Sex { get; set; }
        public int OriginalFaction { get; set; }
        public long Dowry { get; set; }
        public int Happiness { get; set; }
    }

    /// <summary>
    /// Save data for court intrigue/plot
    /// </summary>
    public class CourtIntrigueSaveData
    {
        public string PlotType { get; set; } = "";
        public List<string> Conspirators { get; set; } = new();
        public string Target { get; set; } = "";
        public int Progress { get; set; }
        public bool IsDiscovered { get; set; }
    }

    /// <summary>
    /// Save data for royal mercenaries (hired bodyguards for king's dungeon party)
    /// </summary>
    public class RoyalMercenarySaveData
    {
        public string Name { get; set; } = "";
        public string Role { get; set; } = "";
        public int ClassId { get; set; }
        public int Sex { get; set; }
        public int Level { get; set; }
        public long HP { get; set; }
        public long MaxHP { get; set; }
        public long Mana { get; set; }
        public long MaxMana { get; set; }
        public long Strength { get; set; }
        public long Defence { get; set; }
        public long WeapPow { get; set; }
        public long ArmPow { get; set; }
        public long Agility { get; set; }
        public long Dexterity { get; set; }
        public long Wisdom { get; set; }
        public long Intelligence { get; set; }
        public long Constitution { get; set; }
        public long Healing { get; set; }
    }

    /// <summary>
    /// Save data for character relationships (from RelationshipSystem)
    /// </summary>
    public class RelationshipSaveData
    {
        public string Name1 { get; set; } = "";  // First character name
        public string Name2 { get; set; } = "";  // Second character name
        public string IdTag1 { get; set; } = ""; // First character unique ID
        public string IdTag2 { get; set; } = ""; // Second character unique ID
        public int Relation1 { get; set; }       // Character 1's relation to Character 2
        public int Relation2 { get; set; }       // Character 2's relation to Character 1
        public int MarriedDays { get; set; }     // Days married (0 if not married)
        public bool Deleted { get; set; }
        public DateTime LastUpdated { get; set; }
        public int CreatedOnGameDay { get; set; } // In-game day when relationship started (v0.26)
    }

} 