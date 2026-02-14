using System.Linq;
using UsurperRemake.Utils;
using Godot;
using System.Collections.Generic;

/// <summary>
/// Game configuration constants extracted directly from Pascal INIT.PAS
/// These values must match exactly with the original Usurper game
/// </summary>
public static partial class GameConfig
{
    // Version information
    public const string Version = "0.30.0";
    public const string VersionName = "Band of Brothers";

    // From Pascal global_maxXX constants
    public const int MaxPlayers = 400;           // global_maxplayers
    public const int MaxTeamMembers = 5;         // global_maxteammembers
    public const int MaxAllows = 15;             // global_maxallows
    public const int MaxNod = 5;                 // global_maxnod
    public const int MaxMon = 17;                // global_maxmon (active monsters)
    public const int MaxMSpells = 6;             // global_maxmspells
    public const int MaxItem = 15;               // global_maxitem
    public const int MaxHittas = 450;            // global_maxhittas (dungeon objects)
    public const int MaxSpells = 25;             // global_maxspells - Expanded from 12 to 25 spells per class
    public const int MaxCombat = 14;             // global_maxcombat
    public const int MaxClasses = 11;            // global_maxclasses
    public const int MaxRaces = 10;              // global_maxraces
    public const int MaxBarrelMasters = 15;      // global_maxbarrelmasters
    public const int MaxInput = 2000000000;      // global_maxinput
    public const int MaxMailLines = 15;          // global_maxmaillines
    public const int KingGuards = 5;             // global_KingGuards
    
    // Combat constants
    public const int CriticalHitChance = 5;      // 5% base critical hit chance
    public const float BackstabMultiplier = 3f;  // From MURDER.PAS
    public const float BerserkMultiplier = 2f;   // From FIGHT.PAS
    public const int MaxPoison = 100;            // maxpoison
    public const int MaxDarknessLevel = 5;            // maxdarkness
    public const int MaxDrugs = 100;             // maxdrugs
    
    // Game limits
    public const int MaxLevel = 200;             // maxlevel
    public const int TurnsPerDay = 325;          // turns_per_day
    public const int MaxChildren = 8;            // maxchildren
    public const int MaxKingEdicts = 5;          // max_kingedicts
    public const int MaxHeals = 15;              // max healing potions
    
    // Combat constants
    public const float CriticalHitMultiplier = 2.0f;  // Critical hit damage multiplier
    
    // Color constants for compatibility
    public const string HPColor = "`C";              // Bright red for HP display
    
    // Path constants
    public const string DataPath = "DATA/";          // Data directory path
    
    // Lock and timing constants
    public const int LockDelay = 50;                 // Lock delay in milliseconds
    
    // Daily reset constants
    public const int DefaultGymSessions = 3;         // Daily gym sessions
    public const int DefaultDrinksAtOrbs = 5;        // Daily drinks at orbs
    public const int DefaultIntimacyActs = 3;        // Daily intimacy acts
    public const int DefaultMaxWrestlings = 3;       // Daily wrestling matches
    public const int DefaultPickPocketAttempts = 3;  // Daily pickpocket attempts
    
    // Missing monster talks constant
    public static bool MonsterTalk = true;           // Whether monsters can speak

    // ============================================================
    // SysOp-Configurable Settings (BBS door mode administration)
    // These can be modified at runtime by SysOps via the admin console
    // ============================================================

    /// <summary>
    /// Message of the Day - displayed to players on login
    /// </summary>
    public static string MessageOfTheDay { get; set; } = "";

    /// <summary>
    /// Default daily turns for new characters (default: 325 to match TurnsPerDay)
    /// </summary>
    public static int DefaultDailyTurns { get; set; } = 325;

    /// <summary>
    /// XP multiplier for all combat rewards (1.0 = normal)
    /// </summary>
    public static float XPMultiplier { get; set; } = 1.0f;

    /// <summary>
    /// Gold multiplier for all rewards (1.0 = normal)
    /// </summary>
    public static float GoldMultiplier { get; set; } = 1.0f;

    /// <summary>
    /// Monster HP multiplier for difficulty adjustment (1.0 = normal)
    /// </summary>
    public static float MonsterHPMultiplier { get; set; } = 1.0f;

    /// <summary>
    /// Monster damage multiplier for difficulty adjustment (1.0 = normal)
    /// </summary>
    public static float MonsterDamageMultiplier { get; set; } = 1.0f;

    /// <summary>
    /// Maximum dungeon level (default: 100)
    /// </summary>
    public static int MaxDungeonLevel { get; set; } = 100;
    
    // Item limits
    public const int MaxItems = 325;             // maxitems
    public const int MaxArmor = 17;              // maxarmor
    public const int MaxWeapons = 35;            // maxweapons
    public const int MaxInventoryItems = 50;     // Maximum items in player inventory
    
    // Location constants
    public const int MaxMonsters = 65;           // maxmonsters
    public const int MaxGuards = 15;             // maxguards
    public const int MaxLevels = 25;             // maxlevels (dungeon)
    
    // Special NPC marker
    public const string NpcMark = "*";           // global_npcmark
    
    // File paths (from Pascal constants)
    public const string DataDir = "DATA/";       // global_datadir
    public const string DocsDir = "DOCS/";       // global_docsdir
    public const string NodeDir = "NODE/";       // global_nodedir
    public const string ScoreDir = "SCORES/";    // global_scoredir
    public const string TextDir = "TEXT/";       // global_textdir
    
    // Key files
    public const string UsurperCfg = "USURPER.CFG";
    public const string TextDataFile = "USUTEXT.DAT";
    public const string UserFile = DataDir + "USERS.DAT";
    public const string NpcFile = DataDir + "NPCS.DAT";
    public const string MonsterFile = DataDir + "MONSTER.DAT";
    public const string LevelFile = DataDir + "LEVELS.DAT";
    public const string MailFile = DataDir + "MAIL.DAT";
    public const string ArmorFile = DataDir + "ARMOR.DAT";
    public const string WeaponFile = DataDir + "WEAPON.DAT";
    public const string BankSafeFile = DataDir + "BANKSAFE.DAT";
    public const string WantedFile = DataDir + "WANTED.DAT";
    public const string GuardsFile = DataDir + "GUARDS.DAT";
    public const string DateFile = DataDir + "DATE.DAT";
    public const string FameFile = DataDir + "FAME.DAT";
    public const string MarketFile = DataDir + "PLMARKET.DAT";
    public const string ChestFile = DataDir + "CHEST.DAT";
    public const string GodsFile = DataDir + "GODS.DAT";
    public const string KingFile = DataDir + "KING.DAT";
    public const string RelationFile = DataDir + "RELATION.DAT";
    public const string ChildrenFile = DataDir + "CHILDREN.DAT";
    
    // Display constants
    public const int ScreenLines = 25;           // global_screenlines
    
    // Money/currency settings
    public const string MoneyType = "gold";      // default money type
    public const string MoneyType2 = "coin";     // singular form
    public const string MoneyType3 = "coins";    // plural form
    
    // Game version info
    public const string WebAddress = "http://www.usurper.info";
    public const string LevelRaiseText = "(you are eligible for a level raise!)";
    
    // Color constants (from Pascal)
    public const byte HpColor = 12;              // global_hpcol
    public const byte TalkColor = 13;            // global_talkcol
    public const byte TeamColor = 3;             // global_teamcol
    public const byte PlayerColor = 10;          // global_plycol
    public const byte GodColor = 10;             // global_godcol
    public const byte KingColor = 10;            // global_kingcol
    public const byte KidColor = 10;             // global_kidcol
    public const byte MonsterColor = 9;          // global_moncol
    public const byte ItemColor = 11;            // global_itemcol
    public const byte BashColor = 3;             // global_bashcol
    public const byte RelationColor = 6;         // global_relationcol
    
    // Online system constants
    public const int OnlineMaxWaits = 4500;      // global_online_maxwaits
    public const int OnlineMaxWaitsBigLoop = 50000; // global_online_maxwaits_bigloop
    public const string OnLocal = "Loc";         // global_onlocal
    
    // Special character constants
    public const char ReturnKey = '\r';          // #13
    public const char EscapeKey = '\x1b';        // #27
    public const char DeleteKey = '\b';          // #8
    public const char MaxInputKey = '>';         // MaxInput_key
    
    // Deleted player names
    public const string DelName1 = "EMPTY";      // global_delname1
    public const string DelName2 = "EMPTY";      // global_delname2
    
    // ANSI control character
    public const char AnsiControlChar = '`';     // acc
    
    // Game state flags (initialized as per Pascal)
    public static bool UBeta = false;            // global_ubeta
    public static bool UTest = false;            // global_utest
    public static bool Multi = false;            // global_multi
    public static bool UShare = true;            // global_ushare
    public static bool Ansi = false;             // global_ansi
    public static bool Registered = false;       // global_registered
    public static bool MaintRunning = false;     // global_maintrunning
    public static bool CarrierDropped = false;   // global_carrierdropped
    public static bool CheckCarrier = false;     // global_checkcarrier
    
    // Color values
    public static byte CForeground = 2;          // global_cfor
    public static byte CBackground = 0;          // global_cback
    
    // Dungeon level (affects XP calculation)
    public static int DungeonLevel = 3;          // global_dungeonlevel
    
    // Fake players
    public static byte FakePlayers = 0;          // global_fakeplayers
    
    // Supreme being equipment flags
    public static bool SupremeLantern = false;   // global_s_lantern
    public static bool SupremeSword = false;     // global_s_sword
    public static bool SupremeBStaff = false;    // global_s_bstaff
    public static bool SupremeWStaff = false;    // global_s_wstaff
    
    // God activity flag
    public static bool GodActive = false;        // Global_GodActive
    
    // Special game state flags
    public static bool PlayerInSteroids = false; // global_PlayerInSteroids
    public static bool PlayerInFight = false;    // global_PlayerInFight
    public static bool Begged = false;           // global_begged
    public static bool NoBeg = true;             // global_nobeg
    public static bool Escape = true;            // global_escape
    public static bool Killed = false;          // global_killed
    public static bool IceMap = false;           // global_icemap
    public static bool MonsterInit = false;      // global_monsterinit
    public static bool OneMin = false;           // global_onemin
    public static bool TwoMin = false;           // global_twomin
    
    // Maintenance text color
    public const int MaintTxtColor = 10;         // global_mainttxt
    
    // Auto probe location
    public static Places AutoProbe = Places.NoWhere; // global_auto_probe

    // Castle and Royal Court Constants
    public const int MaxRoyalGuards = 20;
    public const int MaxMoatGuards = 100;
    public const int MinLevelKing = 10;              // Minimum level to challenge for throne
    public const long DefaultRoyalTreasury = 50000;  // Starting royal treasury
    public const float DonationTaxRate = 0.1f;       // Tax rate on donations to royal purse
    
    // Royal Tax Alignment Types (Pascal: taxalignment)
    public enum TaxAlignment
    {
        All = 0,        // Everyone must pay
        Good = 1,       // Only good characters pay
        Evil = 2        // Only evil characters pay
    }
    
    // Royal Guard System
    public const long BaseGuardSalary = 1000;        // Base daily salary for guards
    public const int GuardRecruitmentCost = 5000;    // Cost to recruit a guard
    
    // Prison System (integrated with Castle)
    public const int MaxPrisonEscapeAttempts = 3;    // Daily escape attempts
    public const long PrisonBailMultiplier = 1000;   // Level * multiplier = bail cost
    
    // Royal Orphanage
    public const int MaxRoyalOrphans = 50;           // Maximum orphans in royal care
    public const long OrphanCareCost = 100;          // Daily cost per orphan
    
    // Court Magician
    public const long MagicSpellBaseCost = 500;      // Base cost for royal magic
    public const int MaxRoyalSpells = 10;            // Max spells available to king

    // Bank system constants
    public const int DefaultBankRobberyAttempts = 3;
    public const long MaxBankBalance = 2000000000L; // 2 billion gold limit
    public const int GuardSalaryPerLevel = 150;  // Increased from 50 to make guard job worthwhile at higher levels
                                                  // Level 100: 1000 + (100 * 150) = 16,000 gold/day (about 1.5 monster kills)
    
    // Bank guard requirements
    public const int MaxDarknessForGuard = 100;
    public const int MinLevelForGuard = 5;
    
    // Bank safe guard scaling
    public const long SafeGuardThreshold1 = 50000L;
    public const long SafeGuardThreshold2 = 100000L;
    public const long SafeGuardThreshold3 = 250000L;
    public const long SafeGuardThreshold4 = 500000L;
    public const long SafeGuardThreshold5 = 750000L;
    public const long SafeGuardThreshold6 = 1000000L;
    
    // Interest rates (for future implementation)
    public const float DailyInterestRate = 0.05f; // 5% daily interest
    
    // Money transfer limits
    public const long MaxMoneyTransfer = 1000000L;

    // Magic shop constants
    public const string DefaultMagicShopOwner = "Ravanella"; // Default gnome owner
    public const int DefaultIdentificationCost = 1500;
    public const int HealingPotionLevelMultiplier = 5; // Level × 5 gold per potion
    public const int MaxHealingPotions = 50; // Maximum potions player can carry
    
    // Magic item types sold in shop
    public const int MagicItemTypeNeck = 10;  // Amulets, necklaces
    public const int MagicItemTypeFingers = 5; // Rings
    public const int MagicItemTypeWaist = 9;   // Belts, girdles
    
    // Spell system constants (automatic by class/level)
    public const int MaxSpellLevel = 25; // Expanded from 12 to support all 25 spells per class
    public const int BaseSpellManaCost = 10; // Level 1 spells cost 10 mana
    public const int ManaPerSpellLevel = 10; // Each spell level adds 10 mana cost
    
    // Magic resistance and spell effects
    public const int BaseSpellResistance = 25;
    public const int MaxSpellDuration = 10; // Combat rounds

    // Temple/Church System Constants
    public const string DefaultTempleName = "Temple of the Ancient Ones";
    public const string DefaultTemplePriest = "Kenga The Faithful";
    public const string DefaultBishopName = "Bishop Aurelius";
    public const string DefaultPriestName = "Father Benedict";
    public const int MaxGods = 20;                     // Maximum gods in pantheon
    public const long SacrificeGoldBaseReturn = 10;    // Base power points per gold sacrificed
    public const int MaxBelieversPerGod = 1000;        // Maximum believers per deity
    public const long ResurrectionBaseCost = 5000;     // Base cost for resurrection
    
    // Alignment and Morality Constants  
    public const int MaxChivalry = 30000;              // Maximum chivalry points
    public const int MaxDarkness = 30000;              // Maximum darkness points
    public const int ChivalryGoodDeedCost = 1;         // Good deeds consumed per chivalrous act
    public const int DarknessEvilDeedCost = 1;         // Evil deeds consumed per dark act
    public const int AlignmentChangeThreshold = 100;   // Points needed to shift alignment
    
    // Marriage System (Temple-based)
    public const long MarriageCost = 1000;             // Cost for marriage ceremony
    public const long DivorceCost = 2000;              // Cost for divorce/annulment
    public const int MaxMarriageAttempts = 3;          // Daily marriage attempts
    public const int MinAgeForMarriage = 18;           // Minimum age to marry
    public const int MinDaysBeforeMarriage = 7;        // Minimum days of relationship before marriage (v0.26)
    public const int BaseProposalAcceptance = 50;      // Base 50% chance NPC accepts proposal (v0.26)
    public const int MaxDailyRelationshipGain = 2;     // Max relationship steps per day per NPC (v0.26)
    
    // Divine Services
    public const long BlessingCost = 500;              // Cost for divine blessing
    public const long HolyWaterCost = 200;             // Cost for holy water
    public const long ExorcismCost = 1500;             // Cost for exorcism
    public const int ResurrectionLevelPenalty = 1;     // Level loss upon resurrection
    
    // God System
    public enum GodPower
    {
        Fading = 0,      // Very weak god
        Weak = 1000,     // Weak god
        Average = 5000,  // Average god  
        Strong = 15000,  // Strong god
        Mighty = 50000,  // Mighty god
        Supreme = 100000 // Supreme god
    }
    
    // ═══════════════════════════════════════════════════════════════════
    // HEALER SYSTEM CONSTANTS
    // ═══════════════════════════════════════════════════════════════════
    
    // Healer Location
    public const string DefaultHealerName = "The Golden Bow, Healing Hut";
    public const string DefaultHealerManager = "Jadu The Fat";
    
    // Disease Healing Costs (Level-based multipliers from Pascal)
    public const int BlindnessCostMultiplier = 5000;   // Level * 5000
    public const int PlagueCostMultiplier = 6000;      // Level * 6000
    public const int SmallpoxCostMultiplier = 7000;    // Level * 7000
    public const int MeaslesCostMultiplier = 7500;     // Level * 7500
    public const int LeprosyCostMultiplier = 8500;     // Level * 8500
    
    // Cursed Item Removal
    public const int CursedItemRemovalMultiplier = 1000; // Level * 1000 per item
    
    // Disease Effects
    public const int DiseaseResistanceBase = 10;  // Base disease resistance
    public const int MaxDiseaseResistance = 90;   // Maximum disease resistance
    
    // Healing Delays (in milliseconds for animations)
    public const int HealingDelayShort = 800;
    public const int HealingDelayMedium = 1200;
    public const int HealingDelayLong = 2000;

    // Temple system constants
    public const int DefaultTempleCharity = 50;  
    public const int DefaultTempleMarriage = 300;
    public const int DefaultTempleResurrection = 400;
    public const double ResurrectionCostMultiplier = 100.0;  // Level * this amount
    public const int MarriageBaseNeed = 1000;  
    public const int MarriageCharmaNeed = 5000;  
    
    // Prison system constants
    public const string DefaultPrisonName = "The Royal Prison";
    public const string DefaultPrisonCaption = "Ronald"; // Captain of the guard
    public const int DefaultPrisonEscapeAttempts = 1; // Daily escape attempts
    public const int PrisonEscapeSuccessRate = 50; // 50% chance of success
    public const int DefaultPrisonSentence = 1; // Default days imprisoned
    public const int MaxPrisonSentence = 30; // Maximum days a king can sentence
    
    // Prison breaking constants
    public const int PrisonBreakGuardCount = 4; // Guards that respond to prison break
    public const long PrisonBreakBounty = 5000; // Bounty for catching prison breakers
    public const int PrisonBreakPenalty = 2; // Extra days for getting caught breaking in
    
    // Prison messages and responses
    public const string PrisonDemandResponse1 = "Haha!";
    public const string PrisonDemandResponse2 = "Sure! Next year maybe! Haha!";
    public const string PrisonDemandResponse3 = "SHUT UP! OR WE WILL HURT YOU BAD!";
    public const string PrisonDemandResponse4 = "GIVE IT A REST IN THERE!";
    public const string PrisonDemandResponse5 = "Ho ho ho!";
    
    // Prison animation delays
    public const int PrisonCellOpenDelay = 1000; // ms to open cell door
    public const int PrisonEscapeDelay = 2000; // ms for escape attempt
    public const int PrisonGuardResponseDelay = 1500; // ms for guard response
    
    // Offline location constants (Pascal compatibility)
    public const int OfflineLocationDormitory = 0;
    public const int OfflineLocationInnRoom1 = 1;
    public const int OfflineLocationInnRoom2 = 2;  
    public const int OfflineLocationInnRoom3 = 3;
    public const int OfflineLocationInnRoom4 = 4;
    public const int OfflineLocationBeggarWall = 10;
    public const int OfflineLocationCastle = 30;
    public const int OfflineLocationPrison = 40;
    public const int OfflineLocationHome = 50;

    // Phase 12: Relationship System Constants
    // Relationship Types (from Pascal CMS.PAS constants)
    public const int RelationMarried = 10;
    public const int RelationLove = 20;
    public const int RelationPassion = 30;
    public const int RelationFriendship = 40;
    public const int RelationTrust = 50;
    public const int RelationRespect = 60;
    public const int RelationNone = 1;       // returned by social_relation function
    public const int RelationNormal = 70;    // default relation value
    public const int RelationSuspicious = 80;
    public const int RelationAnger = 90;
    public const int RelationEnemy = 100;
    public const int RelationHate = 110;
    
    // Love Corner Settings
    public const string DefaultLoveCornerName = "Lover's Corner";
    public const string DefaultGossipMongerName = "Elvira the Gossip Monger";
    public const int LoveCorner = 77;            // Location ID from Pascal INIT.PAS
    
    // Marriage and Relationship Costs
    public const long WeddingCostBase = 1000;
    public const long DivorceCostBase = 500;
    public const int MinimumAgeToMarry = 18;
    
    // Experience Multipliers for Romantic Actions (Pascal LOVERS.PAS)
    public const int KissExperienceMultiplier = 50;
    public const int DinnerExperienceMultiplier = 75;
    public const int HandHoldingExperienceMultiplier = 40;
    public const int IntimateExperienceMultiplier = 100;
    
    // Gift Shop Costs
    public const long RosesCost = 100;
    public const long ChocolatesCostBase = 200;
    public const long JewelryCostBase = 1000;
    public const long PoisonCostBase = 2000;
    
    // Child System Constants (from Pascal CHILDREN.PAS)
    public const int ChildLocationHome = 1;
    public const int ChildLocationOrphanage = 2;
    public const int ChildLocationKidnapped = 3;
    
    public const int ChildHealthNormal = 1;
    public const int ChildHealthPoisoned = 2;
    public const int ChildHealthCursed = 3;
    public const int ChildHealthDepressed = 4;
    
    public const int ChildAgeUpDays = 30;     // Days per age increment
    
    // Wedding Ceremony Messages (Pascal authentic)
    public static readonly string[] WeddingCeremonyMessages = 
    {
        "The priest says a few holy words and you are married!",
        "A beautiful ceremony filled with love and joy!",
        "The gods smile upon your union!",
        "Love conquers all! You are now wed!",
        "May your marriage be blessed with happiness!",
        "A match made in heaven!",
        "Together forever, through good times and bad!",
        "Your hearts beat as one!",
        "The kingdom celebrates your union!",
        "True love has prevailed!"
    };
    
    // Relationship Maintenance Settings
    public const int RelationshipMaintenanceInterval = 24; // hours
    public const int AutoDivorceChance = 20;  // 1 in 20 chance per day
    
    // Intimacy System
    public const int DefaultIntimacyActsPerDay = 3;
    public const int MaxIntimacyActsPerDay = 5;
    
    // Phase 13: God System Constants (from Pascal INITGODS.PAS, VARGODS.PAS, TEMPLE.PAS)
    // Supreme Creator
    public const string SupremeCreatorName = "Manwe";  // global_supreme_creator from INITGODS.PAS
    
    // Temple System
    public const int TempleLocationId = 47;          // onloc_temple from CMS.PAS
    public const int HeavenLocationId = 400;         // onloc_heaven from CMS.PAS
    public const int HeavenBossLocationId = 401;     // onloc_heaven_boss from CMS.PAS
    
    // God System Configuration
    public const int MaxGodRecords = 50;              // Maximum gods that can exist
    public const int DefaultGodDeedsLeft = 3;         // config.gods_deedsleft - daily deeds for gods
    public const int MaxGodLevel = 9;                 // Maximum god level
    public const int MinGodAge = 2;                   // Minimum god age (random(5) + 2)
    public const int MaxGodAge = 6;                   // Maximum god age (random(5) + 2)
    
    // God Level Experience Thresholds (from Pascal God_Level_Raise function)
    public const long GodLevel2Experience = 5000;     // Level 2: Minor Spirit
    public const long GodLevel3Experience = 15000;    // Level 3: Spirit
    public const long GodLevel4Experience = 50000;    // Level 4: Major Spirit
    public const long GodLevel5Experience = 70000;    // Level 5: Minor Deity
    public const long GodLevel6Experience = 90000;    // Level 6: Deity
    public const long GodLevel7Experience = 110000;   // Level 7: Major Deity
    public const long GodLevel8Experience = 550000;   // Level 8: DemiGod
    public const long GodLevel9Experience = 1000500;  // Level 9: God
    
    // God Titles (from Pascal God_Title function)
    public static readonly string[] GodTitles = 
    {
        "",                 // Index 0 - unused
        "Lesser Spirit",    // Level 1
        "Minor Spirit",     // Level 2
        "Spirit",           // Level 3
        "Major Spirit",     // Level 4
        "Minor Deity",      // Level 5
        "Deity",            // Level 6
        "Major Deity",      // Level 7
        "DemiGod",          // Level 8
        "God"               // Level 9
    };
    
    // Sacrifice Gold Return Tiers (from Pascal Sacrifice_Gold_Return function)
    public const long SacrificeGoldTier1Max = 20;           // Returns 1 power
    public const long SacrificeGoldTier2Max = 2000;         // Returns 2 power
    public const long SacrificeGoldTier3Max = 45000;        // Returns 3 power
    public const long SacrificeGoldTier4Max = 150000;       // Returns 4 power
    public const long SacrificeGoldTier5Max = 900000;       // Returns 5 power
    public const long SacrificeGoldTier6Max = 15000000;     // Returns 6 power
    public const long SacrificeGoldTier7Max = 110000000;    // Returns 7 power
    // Above 110000000 returns 8 power
    
    // Divine Intervention Settings
    public const int DivineInterventionCost = 1;            // Deeds cost per intervention
    public const int GodMaintenanceInterval = 24;           // Hours between god maintenance
    
    // Broadcast Messages (from Pascal CMS.PAS)
    public const string BroadcastGodDesecrated = "∩∩∩1";    // broadcast_GodDesecrated
    public const string BroadcastGodSacrificed = "∩∩∩2";    // broadcast_GodSacrificed
    public const string BroadcastNewGod = "*NEW GOD*";      // New god notification
    public const string BroadcastGodEnteredGame = "god_entered"; // God entered heaven
    
    // Temple Menu Options (from Pascal TEMPLE.PAS)
    public const string TempleMenuWorship = "W";       // Worship a god
    public const string TempleMenuDesecrate = "D";     // Desecrate altar
    public const string TempleMenuAltars = "A";        // View altars
    public const string TempleMenuContribute = "C";    // Contribute/sacrifice
    public const string TempleMenuStatus = "S";        // Status
    public const string TempleMenuGodRanking = "G";    // God ranking
    public const string TempleMenuHolyNews = "H";      // Holy news
    public const string TempleMenuReturn = "R";        // Return
    
    // God World Menu Options (from Pascal GODWORLD.PAS)
    public const string GodWorldMenuImmortals = "I";       // List immortals
    public const string GodWorldMenuIntervention = "D";    // Divine intervention
    public const string GodWorldMenuVisitBoss = "V";       // Visit supreme creator
    public const string GodWorldMenuBelievers = "B";       // View believers
    public const string GodWorldMenuListMortals = "L";     // List mortals
    public const string GodWorldMenuMessage = "M";         // Send message
    public const string GodWorldMenuExamine = "E";         // Examine mortal
    public const string GodWorldMenuStatus = "S";          // God status
    public const string GodWorldMenuComment = "C";         // Comment to mortals
    public const string GodWorldMenuNews = "N";            // News
    public const string GodWorldMenuQuit = "Q";            // Quit heaven
    public const string GodWorldMenuFlock = "F";           // Flock inspection
    public const string GodWorldMenuSuicide = "*";         // God suicide
    public const string GodWorldMenuImmortalNews = "1";    // Immortal news
    
    // Divine Intervention Menu Options
    public const string DivineMortals = "M";               // Intervene with mortals
    public const string DivineChildren = "C";              // Intervene with children  
    public const string DivinePrisoners = "P";             // Intervene with prisoners
    public const string DivineHelp = "H";                  // Help menu
    public const string DivineReturn = "R";                // Return to main menu
    
    // God AI Types
    public const char GodAIHuman = 'H';                    // Human-controlled god
    public const char GodAIComputer = 'C';                 // Computer-controlled god
    
    // God Becoming Requirements
    public const int MinLevelToBecomeGod = 200;            // Must be max level to become god
    public const string GodBecomingLocation = "Rurforium"; // bossplace constant

    #region Character Creation System Constants

    // Starting values for new characters (Pascal USERHUNC.PAS)
    public const int DefaultStartingGold = 2000;  // startm variable
    public const int DefaultStartingExperience = 10;
    public const int DefaultStartingLevel = 1;
    public const int DefaultDungeonFights = 5;    // dngfights
    public const int DefaultPlayerFights = 3;     // plfights
    public const int DefaultStartingHealing = 20;  // starting potions (scales with level)
    public const int DefaultGoodDeeds = 3;        // chivnr
    public const int DefaultDarkDeeds = 3;        // darknr
    public const int DefaultLoyalty = 50;
    public const int DefaultMentalHealth = 100;
    public const int DefaultTournamentFights = 3; // tfights
    public const int DefaultThiefAttempts = 3;    // thiefs
    public const int DefaultBrawls = 3;
    public const int DefaultAssassinAttempts = 3; // assa

    // Character Class Starting Attributes (Pascal USERHUNC.PAS case statements)
    // Intelligence affects spell damage, mana pool, spell crit chance, XP bonus
    // Constitution affects bonus HP, poison/disease resistance
    public static readonly Dictionary<CharacterClass, CharacterAttributes> ClassStartingAttributes = new()
    {
        [CharacterClass.Alchemist] = new() { HP = 1, Strength = 2, Defence = 1, Stamina = 1, Agility = 2, Charisma = 4, Dexterity = 3, Wisdom = 5, Intelligence = 5, Constitution = 1, Mana = 0, MaxMana = 0 },
        [CharacterClass.Assassin] = new() { HP = 3, Strength = 4, Defence = 3, Stamina = 3, Agility = 4, Charisma = 2, Dexterity = 5, Wisdom = 2, Intelligence = 3, Constitution = 3, Mana = 0, MaxMana = 0 },
        [CharacterClass.Barbarian] = new() { HP = 5, Strength = 5, Defence = 4, Stamina = 5, Agility = 4, Charisma = 1, Dexterity = 2, Wisdom = 1, Intelligence = 1, Constitution = 5, Mana = 0, MaxMana = 0 },
        [CharacterClass.Bard] = new() { HP = 3, Strength = 4, Defence = 3, Stamina = 3, Agility = 3, Charisma = 4, Dexterity = 4, Wisdom = 3, Intelligence = 3, Constitution = 3, Mana = 0, MaxMana = 0 },
        [CharacterClass.Cleric] = new() { HP = 3, Strength = 3, Defence = 2, Stamina = 2, Agility = 2, Charisma = 4, Dexterity = 2, Wisdom = 4, Intelligence = 3, Constitution = 3, Mana = 20, MaxMana = 20 },
        [CharacterClass.Jester] = new() { HP = 2, Strength = 3, Defence = 2, Stamina = 2, Agility = 5, Charisma = 3, Dexterity = 5, Wisdom = 1, Intelligence = 2, Constitution = 2, Mana = 0, MaxMana = 0 },
        [CharacterClass.Magician] = new() { HP = 2, Strength = 1, Defence = 1, Stamina = 1, Agility = 2, Charisma = 5, Dexterity = 2, Wisdom = 4, Intelligence = 5, Constitution = 1, Mana = 40, MaxMana = 40 },
        [CharacterClass.Paladin] = new() { HP = 4, Strength = 4, Defence = 3, Stamina = 4, Agility = 2, Charisma = 2, Dexterity = 3, Wisdom = 3, Intelligence = 2, Constitution = 4, Mana = 0, MaxMana = 0 },
        [CharacterClass.Ranger] = new() { HP = 3, Strength = 3, Defence = 3, Stamina = 4, Agility = 3, Charisma = 2, Dexterity = 4, Wisdom = 3, Intelligence = 2, Constitution = 3, Mana = 0, MaxMana = 0 },
        [CharacterClass.Sage] = new() { HP = 2, Strength = 2, Defence = 2, Stamina = 2, Agility = 2, Charisma = 3, Dexterity = 3, Wisdom = 5, Intelligence = 5, Constitution = 2, Mana = 50, MaxMana = 50 },
        [CharacterClass.Warrior] = new() { HP = 4, Strength = 4, Defence = 4, Stamina = 4, Agility = 3, Charisma = 2, Dexterity = 2, Wisdom = 2, Intelligence = 2, Constitution = 4, Mana = 0, MaxMana = 0 }
    };

    // Character Race Bonuses (Pascal USERHUNC.PAS race case statements)
    public static readonly Dictionary<CharacterRace, RaceAttributes> RaceAttributes = new()
    {
        [CharacterRace.Human] = new() { HPBonus = 14, StrengthBonus = 4, DefenceBonus = 4, StaminaBonus = 4, MinAge = 18, MaxAge = 22, MinHeight = 180, MaxHeight = 219, MinWeight = 75, MaxWeight = 119, SkinColor = 10, HairColors = new[] { 1, 4, 5, 8 } },
        [CharacterRace.Hobbit] = new() { HPBonus = 12, StrengthBonus = 2, DefenceBonus = 3, StaminaBonus = 3, MinAge = 20, MaxAge = 34, MinHeight = 100, MaxHeight = 136, MinWeight = 40, MaxWeight = 79, SkinColor = 10, HairColors = new[] { 1, 4, 5, 8 } },
        [CharacterRace.Elf] = new() { HPBonus = 11, StrengthBonus = 3, DefenceBonus = 2, StaminaBonus = 3, MinAge = 20, MaxAge = 34, MinHeight = 160, MaxHeight = 184, MinWeight = 60, MaxWeight = 89, SkinColor = 10, HairColors = new[] { 1, 4, 5, 8 } },
        [CharacterRace.HalfElf] = new() { HPBonus = 13, StrengthBonus = 2, DefenceBonus = 3, StaminaBonus = 4, MinAge = 18, MaxAge = 25, MinHeight = 165, MaxHeight = 189, MinWeight = 70, MaxWeight = 94, SkinColor = 10, HairColors = new[] { 1, 4, 5, 8 } },
        [CharacterRace.Dwarf] = new() { HPBonus = 17, StrengthBonus = 5, DefenceBonus = 5, StaminaBonus = 4, MinAge = 25, MaxAge = 39, MinHeight = 160, MaxHeight = 179, MinWeight = 70, MaxWeight = 89, SkinColor = 7, HairColors = new[] { 1, 4, 5, 8 } },
        [CharacterRace.Troll] = new() { HPBonus = 16, StrengthBonus = 5, DefenceBonus = 5, StaminaBonus = 4, MinAge = 18, MaxAge = 29, MinHeight = 185, MaxHeight = 219, MinWeight = 85, MaxWeight = 114, SkinColor = 5, HairColors = new[] { 5, 4, 4, 5 } },
        [CharacterRace.Orc] = new() { HPBonus = 14, StrengthBonus = 3, DefenceBonus = 4, StaminaBonus = 3, MinAge = 18, MaxAge = 24, MinHeight = 170, MaxHeight = 189, MinWeight = 70, MaxWeight = 89, SkinColor = 5, HairColors = new[] { 5, 4, 4, 5 } },
        [CharacterRace.Gnome] = new() { HPBonus = 12, StrengthBonus = 2, DefenceBonus = 3, StaminaBonus = 3, MinAge = 18, MaxAge = 29, MinHeight = 160, MaxHeight = 189, MinWeight = 60, MaxWeight = 74, SkinColor = 3, HairColors = new[] { 3, 3, 4, 9 } },
        [CharacterRace.Gnoll] = new() { HPBonus = 13, StrengthBonus = 4, DefenceBonus = 3, StaminaBonus = 3, MinAge = 18, MaxAge = 27, MinHeight = 140, MaxHeight = 154, MinWeight = 50, MaxWeight = 64, SkinColor = 4, HairColors = new[] { 3, 3, 4, 9 } },
        [CharacterRace.Mutant] = new() { HPBonus = 14, StrengthBonus = 3, DefenceBonus = 3, StaminaBonus = 3, MinAge = 18, MaxAge = 32, MinHeight = 150, MaxHeight = 199, MinWeight = 50, MaxWeight = 99, SkinColor = 0, HairColors = new int[0] }  // Random for mutants - adaptive bonus
    };

    // NPC Lifecycle Aging Rate
    // Controls how fast NPCs and their children age in the persistent world.
    // At 9.6 hours per game-year:
    //   - A child (18yr) grows up in ~7 real days
    //   - A Human (75yr) dies in ~30 real days
    //   - An Elf (200yr) dies in ~80 real days
    //   - A Gnoll (50yr) dies in ~20 real days
    public const double NpcLifecycleHoursPerYear = 9.6;

    // Race Maximum Lifespan (in game-years)
    // When an NPC reaches this age, they die permanently of old age - no respawn.
    public static readonly Dictionary<CharacterRace, int> RaceLifespan = new()
    {
        [CharacterRace.Human]   = 75,
        [CharacterRace.Hobbit]  = 90,
        [CharacterRace.Elf]     = 200,
        [CharacterRace.HalfElf] = 120,
        [CharacterRace.Dwarf]   = 150,
        [CharacterRace.Troll]   = 60,
        [CharacterRace.Orc]     = 55,
        [CharacterRace.Gnome]   = 130,
        [CharacterRace.Gnoll]   = 50,
        [CharacterRace.Mutant]  = 65,
    };

    // Race and Class Names (Pascal constants)
    public static readonly string[] RaceNames = {
        "Human", "Hobbit", "Elf", "Half-Elf", "Dwarf", "Troll", "Orc", "Gnome", "Gnoll", "Mutant"
    };

    public static readonly string[] ClassNames = {
        "Warrior", "Paladin", "Ranger", "Assassin", "Bard", "Jester", "Alchemist", "Magician", "Cleric", "Sage", "Barbarian"
    };

    // Race Descriptions for Character Creation
    public static readonly Dictionary<CharacterRace, string> RaceDescriptions = new()
    {
        [CharacterRace.Human] = "a humble Human",
        [CharacterRace.Hobbit] = "a loyal Hobbit", 
        [CharacterRace.Elf] = "a graceful Elf",
        [CharacterRace.HalfElf] = "an allround Half-Elf",
        [CharacterRace.Dwarf] = "a stubborn Dwarf",
        [CharacterRace.Troll] = "a stinking Troll",
        [CharacterRace.Orc] = "an ill-mannered Orc",
        [CharacterRace.Gnome] = "a willful Gnome",
        [CharacterRace.Gnoll] = "a puny Gnoll",
        [CharacterRace.Mutant] = "a weird Mutant"
    };

    // Physical Appearance Options (Pascal appearance system)
    public static readonly string[] EyeColors = {
        "", "Brown", "Blue", "Green", "Hazel", "Gray"  // 1-5 in Pascal
    };

    public static readonly string[] HairColors = {
        "", "Black", "Brown", "Red", "Blond", "Dark", "Light", "Auburn", "Golden", "Silver", "White"  // 1-10 in Pascal
    };

    public static readonly string[] SkinColors = {
        "", "Very Dark", "Dark", "Tanned", "Brownish", "Green", "Grayish", "Bronze", "Pale", "Fair", "Very Fair"  // 1-10 in Pascal
    };

    // Forbidden Character Names (Pascal validation)
    public static readonly string[] ForbiddenNames = {
        "SYSOP", "COMPUTER", "COMPUTER1", "COMPUTER2", "COMPUTER3", "COMPUTER4", "COMPUTER5"
    };

    // Character Creation Help Text
    public const string RaceHelpText = @"
Race determines your basic physical and mental characteristics:

Human     - Balanced in all areas. Can be any class.
Hobbit    - Small but agile. Good rangers, rogues, bards. Too small for heavy combat.
Elf       - Graceful and magical. Excellent mages and clerics. Dislike brute force.
Half-Elf  - Versatile like humans. Can be any class.
Dwarf     - Strong and tough. Great warriors. Distrust arcane magic.
Troll     - Massive brutes with natural regeneration. Warriors, barbarians, rangers only.
Orc       - Aggressive fighters. Warriors, assassins, rangers. Limited magic.
Gnome     - Small and clever. Great mages, alchemists. Poor heavy fighters.
Gnoll     - Pack hunters. Warriors, rangers, assassins. Limited intellect.
Mutant    - Chaotic and unpredictable. Can be any class.
";

    public const string ClassHelpText = @"
Class determines your profession and abilities:

=== MELEE FIGHTERS ===
Warrior   - Strong fighters, masters of weapons. Balanced and reliable.
Barbarian - Savage fighters with incredible strength. Requires brute force races.
Paladin   - Holy warriors of virtue. Restricted to honorable races.

=== HYBRID CLASSES ===
Ranger    - Woodsmen and trackers. Balanced fighters with survival skills.
Assassin  - Deadly killers, masters of stealth. Requires cunning and dexterity.
Bard      - Musicians and storytellers. Social skills and light combat.
Jester    - Entertainers and tricksters. Very agile and unpredictable.

=== MAGIC USERS ===
Magician  - Powerful spellcasters with low health. Requires high intellect.
Sage      - Scholars and wise magic users. Requires wisdom and study.
Cleric    - Healers and holy magic users. Requires devotion and wisdom.
Alchemist - Potion makers and researchers. Requires intellect and patience.
";

    // Invalid Race/Class Combinations (Pascal validation + expanded restrictions)
    // Based on racial attributes and common-sense fantasy archetypes
    public static readonly Dictionary<CharacterRace, CharacterClass[]> InvalidCombinations = new()
    {
        // Humans can be anything - jack of all trades
        // [CharacterRace.Human] = no restrictions

        // Hobbits: Small, not strong - can't be heavy melee classes
        [CharacterRace.Hobbit] = new[] { CharacterClass.Barbarian, CharacterClass.Paladin },

        // Elves: Graceful and magical - poor at brute force classes
        [CharacterRace.Elf] = new[] { CharacterClass.Barbarian },

        // Half-Elves: Versatile like humans - no restrictions
        // [CharacterRace.HalfElf] = no restrictions

        // Dwarves: Strong but stubborn, distrust magic - no pure casters
        [CharacterRace.Dwarf] = new[] { CharacterClass.Magician, CharacterClass.Sage },

        // Trolls: Massive brutes, too stupid for magic or finesse
        [CharacterRace.Troll] = new[] {
            CharacterClass.Paladin, CharacterClass.Magician, CharacterClass.Sage,
            CharacterClass.Cleric, CharacterClass.Alchemist, CharacterClass.Bard,
            CharacterClass.Assassin, CharacterClass.Jester
        },

        // Orcs: Aggressive fighters, limited magical ability
        [CharacterRace.Orc] = new[] {
            CharacterClass.Paladin, CharacterClass.Magician, CharacterClass.Sage,
            CharacterClass.Bard
        },

        // Gnomes: Small and clever, poor at heavy combat
        [CharacterRace.Gnome] = new[] { CharacterClass.Barbarian, CharacterClass.Paladin },

        // Gnolls: Pack hunters, limited intellect
        [CharacterRace.Gnoll] = new[] {
            CharacterClass.Paladin, CharacterClass.Magician, CharacterClass.Sage,
            CharacterClass.Cleric, CharacterClass.Alchemist
        },

        // Mutants: Unpredictable - can be anything (chaos incarnate)
        // [CharacterRace.Mutant] = no restrictions
    };

    // Restriction reasons for player feedback
    public static readonly Dictionary<CharacterRace, string> RaceRestrictionReasons = new()
    {
        [CharacterRace.Hobbit] = "Hobbits are too small for heavy armor and brutal combat styles.",
        [CharacterRace.Elf] = "Elves find brute-force fighting distasteful and beneath them.",
        [CharacterRace.Dwarf] = "Dwarves distrust arcane magic, preferring steel to spells.",
        [CharacterRace.Troll] = "Trolls lack the intelligence and discipline for most classes.",
        [CharacterRace.Orc] = "Orcs are too aggressive and impatient for scholarly or holy pursuits.",
        [CharacterRace.Gnome] = "Gnomes are too small to wield heavy weapons effectively.",
        [CharacterRace.Gnoll] = "Gnolls lack the intellect for complex magic or holy devotion."
    };

    #endregion

    // ═══════════════════════════════════════════════════════════════
    // DAILY MAINTENANCE SYSTEM CONSTANTS (Pascal MAINT.PAS)
    // ═══════════════════════════════════════════════════════════════
    
    // Daily Player Processing (Pascal maintenance formulas)
    public const int AliveBonus = 350;                    // level * 350 per day alive
    public const long MaxAliveBonus = 1500000000;         // Maximum alive bonus allowed
    public const int DailyDungeonFights = 10;           // Daily dungeon fights reset
    public const int DailyPlayerFights = 3;             // Daily player fights reset
    public const int DefaultTeamFights = 2;               // Daily team fights reset
    public const int DailyThiefAttempts = 3;            // Daily thief attempts reset
    public const int DailyBrawls = 3;                   // Daily brawl attempts reset
    public const int DailyAssassinAttempts = 3;         // Daily assassin attempts reset
    public const int DefaultBardSongs = 5;                // Daily bard songs reset
    public const int AssassinThiefBonus = 2;              // Extra thief attempts for assassins
    
    // PvP Arena Configuration
    public const int MaxPvPAttacksPerDay = 5;           // Maximum PvP attacks per day
    public const int MinPvPLevel = 5;                   // Minimum level to enter the arena
    public const double PvPGoldStealPercent = 0.10;     // 10% of loser's gold on hand
    public const int PvPLevelRangeLimit = 20;           // Can't attack someone more than 20 levels different
    public const long PvPMinXPReward = 25;              // Minimum XP reward for PvP win
    public const long PvPMaxXPReward = 5000;            // Maximum XP reward for PvP win

    // Daily Limits and Resets (Pascal daily parameter resets)
    public const int DailyDarknessReset = 6;              // Daily darkness deeds reset
    public const int DailyChivalryReset = 6;              // Daily chivalry deeds reset
    public const int DailyMentalStabilityChance = 7;      // 1 in 7 chance for mental stability increase
    public const int MentalStabilityIncrease = 5;         // Max mental stability increase per day
    public const int MaxMentalStability = 100;            // Maximum mental stability
    
    // Healing Potion Maintenance (Pascal healing potion spoilage)
    public const float HealingSpoilageRate = 0.5f;        // 50% of overage spoils per day
    public const int MinHealingSpoilage = 2;               // Minimum spoilage threshold
    
    // Player Activity and Cleanup (Pascal inactivity system)
    public const int DefaultInactivityDays = 30;          // Days before deletion consideration
    public const int MinInactivityDays = 15;              // Minimum inactivity setting
    public const int MaxInactivityDays = 999;             // Maximum inactivity setting
    
    // Bank and Economic Maintenance (Pascal bank system)
    public const int DefaultBankInterest = 3;             // Default daily interest rate
    public const int MinBankInterest = 1;                 // Minimum interest rate
    public const int MaxBankInterest = 15;                // Maximum interest rate
    public const int DefaultTownPot = 5000;               // Default town pot value
    public const int MinTownPot = 100;                    // Minimum town pot
    public const int MaxTownPot = 500000000;              // Maximum town pot
    
    // Royal System Maintenance (Pascal king system daily resets)
    public const int DailyPrisonSentences = 4;            // King's daily prison sentences
    public const int DailyExecutions = 3;                 // King's daily executions
    public const int DefaultMaxNewQuests = 5;             // Max new royal quests per day
    public const int DefaultMarryActions = 3;             // Max royal marriage actions per day
    public const int DefaultWolfFeeding = 2;              // Max children to wolves per day
    public const int DefaultRoyalAdoptions = 3;           // Max royal adoptions per day
    
    // Mail System Constants (Pascal MAIL.PAS)
    public const int MaxMailRecords = 65500;              // Maximum mail database size
    public const int DefaultMaxMailDays = 30;             // Days before mail expires
    
    // Mail Request Types (Pascal mailrequest_ constants)
    public const byte MailRequestNothing = 0;
    public const byte MailRequestBirthday = 1;
    public const byte MailRequestChildBorn = 2;
    public const byte MailRequestChildDepressed = 3;
    public const byte MailRequestChildPoisoned = 4;
    public const byte MailRequestRoyalGuard = 5;
    public const byte MailRequestMarriage = 6;
    public const byte MailRequestNews = 7;
    public const byte MailRequestSystem = 8;
    
    // Birthday Gift Types (Pascal birthday system)
    public const int BirthdayExperienceGift = 1000;       // Experience gift amount
    public const int BirthdayLoveGift = 500;              // Love/charisma gift amount
    public const int BirthdayChildGift = 1;               // Adoption gift
    
    // Random Event Chances (Pascal random event system)
    public const float DailyEventChance = 0.15f;          // 15% chance for daily random event
    public const float WeeklyEventChance = 1.0f;          // 100% chance for weekly events
    public const float MonthlyEventChance = 1.0f;         // 100% chance for monthly events
    public const float FlavorTextChance = 0.7f;           // 70% chance for daily flavor text
    
    // Maintenance Configuration Indices (Pascal cfg file indices)
    public const int CFG_DUNGEON_FIGHTS = 6;              // Config index for dungeon fights
    public const int CFG_PLAYER_FIGHTS = 40;              // Config index for player fights  
    public const int CFG_BANK_INTEREST = 41;              // Config index for bank interest
    public const int CFG_INACTIVITY_DAYS = 7;             // Config index for inactivity days
    public const int CFG_TEAM_FIGHTS = 13;                // Config index for team fights
    public const int CFG_TOWN_POT = 89;                   // Config index for town pot value
    public const int CFG_RESURRECTION = 68;               // Config index for resurrection
    public const int CFG_MAX_TIME = 87;                   // Config index for max time
    
    // System Maintenance Flags (Pascal maintenance control)
    public const string MaintenanceFlagFile = "MAINT.FLG"; // Maintenance lock file
    public const string MaintenanceDateFile = "DATE.DAT";            // Date tracking file
    public const int MaintenanceLockDelay = 50;           // Delay between lock attempts
    public const int MaxMaintenanceLockTries = 150;       // Maximum lock attempts

    // ═══════════════════════════════════════════════════════════════
    // SAVE SYSTEM CONSTANTS
    // ═══════════════════════════════════════════════════════════════
    
    // Save file versioning
    public const int SaveVersion = 1;                     // Current save format version
    public const int MinSaveVersion = 1;                  // Minimum compatible save version
    
    // Auto-save settings
    public const int DefaultAutoSaveIntervalMinutes = 5;  // Default auto-save interval
    public const bool DefaultAutoSaveEnabled = true;      // Auto-save enabled by default
    
    // Save file limits
    public const int MaxSaveFiles = 100;                  // Maximum save files to keep
    public const int MaxBackupFiles = 5;                  // Maximum backup files per save
    public const long MaxSaveFileSize = 50 * 1024 * 1024; // 50MB max save file size
    
    // Daily cycle system defaults (enum defined in SaveDataStructures.cs)
    public const int DefaultDailyCycleModeInt = 5; // Endless = 5 (no turn limits)
    public const int SessionBasedTurns = TurnsPerDay;      // Full turns for session-based
    public const int AcceleratedTurnsDivisor = 6;         // Divide turns for accelerated modes
    public const int EndlessModeMinTurns = 50;             // Minimum turns before boost in endless mode
    public const int EndlessModeBoostAmount = 25;         // Turn boost amount in endless mode

    // ═══════════════════════════════════════════════════════════════
    // QUEST SYSTEM CONSTANTS (Pascal PLYQUEST.PAS & RQUESTS.PAS)
    // ═══════════════════════════════════════════════════════════════
    
    // Quest Database Limits (Pascal quest file handling)
    public const int MaxQuestsAllowed = 65000;             // Maximum quests in database
    public const int MaxQuestMonsters = 10;                // Maximum monsters per quest (global_maxmon)
    public const int MaxActiveQuests = 5;                  // Maximum active quests per player
    public const int MaxCompletedQuests = 3;               // Maximum quest completions per day
    public const int MaxQuestsPerDay = 3;                  // Maximum quests claimable per day
    
    // Quest Creation Limits (Pascal royal quest limits)
    public const int QuestMaxNewQuests = 5;              // Daily new quest limit for kings
    public const int MinQuestLevel = 1;                    // Minimum level for quest participation
    public const int MaxQuestLevel = 9999;                 // Maximum level for quest participation
    public const int DefaultQuestDays = 7;                 // Default days to complete quest
    public const int MinQuestDays = 1;                     // Minimum days to complete
    public const int MaxQuestDays = 30;                    // Maximum days to complete
    
    // Quest Difficulty Levels (Pascal difficulty system)
    public const byte QuestDifficultyEasy = 1;             // Easy quest difficulty
    public const byte QuestDifficultyMedium = 2;           // Medium quest difficulty  
    public const byte QuestDifficultyHard = 3;             // Hard quest difficulty
    public const byte QuestDifficultyExtreme = 4;          // Extreme quest difficulty
    
    // Quest Reward Levels (Pascal reward system)
    public const byte QuestRewardNone = 0;                 // No reward
    public const byte QuestRewardLow = 1;                  // Low reward level
    public const byte QuestRewardMedium = 2;               // Medium reward level
    public const byte QuestRewardHigh = 3;                 // High reward level
    
    // Quest Experience Rewards (Pascal reward calculations)
    public const int QuestExpLowMultiplier = 100;          // level * 100 (low exp)
    public const int QuestExpMediumMultiplier = 500;       // level * 500 (medium exp)
    public const int QuestExpHighMultiplier = 1000;        // level * 1000 (high exp)
    
    // Quest Gold Rewards (Pascal money rewards)
    public const int QuestGoldLowMultiplier = 1100;        // level * 1100 (low gold)
    public const int QuestGoldMediumMultiplier = 5100;     // level * 5100 (medium gold)
    public const int QuestGoldHighMultiplier = 11000;      // level * 11000 (high gold)
    
    // Quest Potion Rewards (Pascal healing potion rewards)
    public const int QuestPotionsLow = 50;                 // Low potion reward
    public const int QuestPotionsMedium = 100;             // Medium potion reward
    public const int QuestPotionsHigh = 200;               // High potion reward
    
    // Quest Darkness/Chivalry Rewards (Pascal alignment rewards)
    public const int QuestDarknessLow = 25;                // Low darkness reward
    public const int QuestDarknessMedium = 75;             // Medium darkness reward
    public const int QuestDarknessHigh = 110;              // High darkness reward
    public const int QuestChivalryLow = 25;                // Low chivalry reward
    public const int QuestChivalryMedium = 75;             // Medium chivalry reward
    public const int QuestChivalryHigh = 110;              // High chivalry reward
    
    // Quest Mail Types (Pascal mail integration)
    public const byte MailRequestQuestOffer = 9;           // Quest offer mail type
    public const byte MailRequestQuestComplete = 10;       // Quest completion mail type
    public const byte MailRequestQuestFailed = 11;         // Quest failure mail type
    
    // Quest Master Configuration (Pascal quest hall settings)
    public const string DefaultQuestMaster = "Pingon";     // Default quest master name
    public const string QuestHallLocation = "Quest Hall";  // Quest hall location name
    public const bool AllowKingToInitQuests = true;        // Allow kings to create quests
    public const bool ForceQuests = false;                 // Allow forcing quests on players
    
    // Quest Monster Generation (Pascal monster quest setup)
    public const int MinQuestMonsters = 1;                 // Minimum monsters in quest
    public const int MaxQuestMonstersPerType = 20;         // Maximum of single monster type
    public const int QuestMonsterLevelVariance = 3;        // Monster level variance for quests
    
    // Quest Failure Penalties (Pascal quest failure system)
    public const int QuestFailureDarknessLoss = 50;        // Darkness lost on quest failure
    public const int QuestFailureChivalryLoss = 50;        // Chivalry lost on quest failure
    public const int QuestFailureGoldLoss = 1000;          // Gold lost on quest failure
    public const int QuestFailureExpLoss = 500;            // Experience lost on quest failure

    // News System Constants (Phase 17)
    // From Pascal global_nwfile and GENNEWS.PAS
    public const string NewsAsciiFile = ScoreDir + "NEWS.ASC";         // global_nwfileasc
    public const string NewsAnsiFile = ScoreDir + "NEWS.ANS";          // global_nwfileans
    public const string YesterdayNewsAsciiFile = ScoreDir + "YNEWS.ASC"; // global_ynwfileasc
    public const string YesterdayNewsAnsiFile = ScoreDir + "YNEWS.ANS";  // global_ynwfileans
    
    // Specialized News Files (GENNEWS.PAS categories)
    public const string MonarchNewsAsciiFile = ScoreDir + "MONARCHS.ASC"; // global_MonarchsASCI
    public const string MonarchNewsAnsiFile = ScoreDir + "MONARCHS.ANS";  // global_MonarchsANSI
    public const string GodsNewsAsciiFile = ScoreDir + "GODS.ASC";        // global_GodsASCI
    public const string GodsNewsAnsiFile = ScoreDir + "GODS.ANS";         // global_GodsANSI
    public const string MarriageNewsAsciiFile = ScoreDir + "MARRHIST.ASC"; // global_MarrHistASCI
    public const string MarriageNewsAnsiFile = ScoreDir + "MARRHIST.ANS";  // global_MarrHistANSI
    public const string BirthNewsAsciiFile = ScoreDir + "BIRTHIST.ASC";   // global_ChildBirthHistASCI
    public const string BirthNewsAnsiFile = ScoreDir + "BIRTHIST.ANS";    // global_ChildBirthHistANSI
    
    // News System Settings
    public const int MaxNewsLines = 1000;          // Maximum lines per news file before rotation
    public const int MaxNewsAge = 7;               // Days to keep news before archiving
    public const int MaxDailyNewsEntries = 500;    // Maximum news entries per day
    public const int NewsLineLength = 120;         // Maximum characters per news line
    public const string NewsDateFormat = "MM/dd/yyyy HH:mm";  // Date format for news entries
    public const string NewsTimeFormat = "HH:mm";  // Time format for news entries
    
    // News Categories (Pascal newsy types)
    public enum NewsCategory
    {
        General = 0,        // General daily news (newsy function)
        Royal = 1,          // King/Queen announcements (generic_news royal)
        Marriage = 2,       // Marriage/divorce news (generic_news marriage)
        Birth = 3,          // Child birth announcements (generic_news birth)
        Holy = 4,           // God-related events (generic_news holy)
        System = 5          // System maintenance and events
    }
    
    // News Location Settings
    public const string DefaultNewsLocation = "Usurper Daily News";
    public const string NewsLocationGreeting = "Welcome to the Daily News!";
    public const string NewsLocationMenu = "Read";
    
    // News Menu Options
    public const string NewsMenuDaily = "D";           // Daily news
    public const string NewsMenuRoyal = "R";           // Royal announcements
    public const string NewsMenuMarriage = "M";        // Marriage/relationship news
    public const string NewsMenuBirth = "B";           // Birth announcements
    public const string NewsMenuHoly = "H";            // Holy/god news
    public const string NewsMenuYesterday = "Y";       // Yesterday's news
    public const string NewsMenuReturn = "Q";          // Return to main street
    
    // News File Headers
    public const string NewsHeaderDaily = "=== USURPER DAILY NEWS ===";
    public const string NewsHeaderRoyal = "=== ROYAL PROCLAMATIONS ===";
    public const string NewsHeaderMarriage = "=== MARRIAGE & RELATIONSHIP NEWS ===";
    public const string NewsHeaderBirth = "=== BIRTH ANNOUNCEMENTS ===";
    public const string NewsHeaderHoly = "=== HOLY NEWS & GOD EVENTS ===";
    public const string NewsHeaderYesterday = "=== YESTERDAY'S NEWS ===";
    
    // News Entry Prefixes (Pascal style)
    public const string NewsPrefixTime = "[{0}] ";     // Time prefix for entries
    public const string NewsPrefixDeath = "+ ";        // Death announcement
    public const string NewsPrefixBirth = "* ";        // Birth announcement
    public const string NewsPrefixMarriage = "<3 ";    // Marriage announcement
    public const string NewsPrefixDivorce = "X ";      // Divorce announcement
    public const string NewsPrefixRoyal = "# ";        // Royal announcement
    public const string NewsPrefixHoly = "! ";         // Holy event
    public const string NewsPrefixCombat = "x ";       // Combat event
    public const string NewsPrefixQuest = "> ";        // Quest event
    public const string NewsPrefixTeam = "^ ";         // Team/gang event
    public const string NewsPrefixPrison = "@ ";       // Prison event
    
    // Daily Maintenance News Settings
    public const bool RotateNewsDaily = true;          // Rotate news files during maintenance
    public const bool ArchiveOldNews = true;           // Keep archived news files
    public const string NewsArchivePrefix = "ARCH_";   // Prefix for archived news files
    
    // News Color Codes (Pascal ANSI color integration)
    public const string NewsColorDefault = "`2";       // Green text (config.textcol1)
    public const string NewsColorHighlight = "`5";     // Magenta text (config.textcol2)
    public const string NewsColorTime = "`3";          // Yellow text for timestamps
    public const string NewsColorPlayer = "`A";        // Bright green for player names
    public const string NewsColorRoyal = "`E";         // Bright yellow for royal
    public const string NewsColorHoly = "`9";          // Bright blue for holy
    public const string NewsColorDeath = "`4";         // Red for death
    public const string NewsColorBirth = "`6";         // Cyan for birth
    
    // Additional missing color constants for game systems
    public const string DeathColor = "`4";             // Red for death messages
    public const string TextColor = "`2";              // Default green text
    public const string ExperienceColor = "`B";        // Bright cyan for experience
    public const string CombatColor = "`C";            // Bright red for combat
    public const string HealColor = "`A";              // Bright green for healing
    public const string TauntColor = "`D";             // Bright magenta for taunts
    public const string GoldColor = "`E";              // Bright yellow for gold
    public const string LocationColor = "`9";          // Bright blue for locations
    public const string EmptyColor = "`8";             // Gray for empty slots
    public const string EnemyColor = "`4";             // Red for enemies
    public const string WinnerColor = "`A";            // Bright green for winners
    public const string DamageColor = "`C";            // Bright red for damage
    public const string StatusColor = "`3";            // Yellow for status
    public const string SessionColor = "`B";           // Bright cyan for sessions
    public const string ControllerColor = "`5";        // Magenta for controllers
    public const string CardColor = "`6";              // Cyan for cards
    public const string FightColor = "`C";             // Bright red for fights

    // Location constants
    public const int NewsLocationId = 50;              // Location ID for news reading
    public const string NewsLocationName = "News Stand"; // Display name for location

    // Additional missing constants for system compatibility
    public const bool ClassicMode = false;       // Classic mode toggle
    public const int NPCBelievers = 50;          // NPC believers system

    // Terminal display constants
    public static readonly string DefaultColor = "white";
    public static readonly string HighlightColor = "yellow"; 
    public static readonly string ErrorColor = "red";
    public static readonly string SuccessColor = "green";
    public static readonly string WarningColor = "orange";
    public static readonly string InfoColor = "cyan";
    public static readonly string DescColor = "gray";  // Description text color
    
    // Player interaction constants
    public static readonly int MaxChatLength = 255;
    public static readonly int MaxNameLength = 30;
}

/// <summary>
/// Character class starting attributes structure
/// Based on Pascal USERHUNC.PAS class case statements
/// </summary>
public class CharacterAttributes
{
    public int HP { get; set; }
    public int Strength { get; set; }
    public int Defence { get; set; }
    public int Stamina { get; set; }
    public int Agility { get; set; }
    public int Charisma { get; set; }
    public int Dexterity { get; set; }
    public int Wisdom { get; set; }
    public int Intelligence { get; set; }
    public int Constitution { get; set; }
    public int Mana { get; set; }
    public int MaxMana { get; set; }
}

/// <summary>
/// Character race bonuses and physical appearance data
/// Based on Pascal USERHUNC.PAS race case statements
/// </summary>
public class RaceAttributes
{
    public int HPBonus { get; set; }
    public int StrengthBonus { get; set; }
    public int DefenceBonus { get; set; }
    public int StaminaBonus { get; set; }
    public int MinAge { get; set; }
    public int MaxAge { get; set; }
    public int MinHeight { get; set; }
    public int MaxHeight { get; set; }
    public int MinWeight { get; set; }
    public int MaxWeight { get; set; }
    public int SkinColor { get; set; }  // Fixed skin color for most races, 0 for random (mutants)
    public int[] HairColors { get; set; } = Array.Empty<int>(); // Possible hair colors for race
}

/// <summary>
/// Game locations for auto-probe system (from Pascal)
/// </summary>
public enum Places
{
    NoWhere,
    MainStreet,
    Slottet,        // Castle
    Inn,
    Dormy,          // Dormitory
    Prison,
    UmanCave,
    AtHome,
    WeaponShop = 15,
    MagicShop = 16,
    
    // Placeholder locations for future implementation
    ArmorShop = 20,
}

/// <summary>
/// Pascal location constants - exact match with CMS.PAS onloc_ constants
/// </summary>
public enum GameLocation
{
    NoWhere = 0,
    MainStreet = 1,      // onloc_mainstreet
    TheInn = 2,          // onloc_theinn  
    DarkAlley = 3,       // onloc_darkalley (outside the shady shops)
    Church = 4,          // onloc_church
    WeaponShop = 5,      // onloc_weaponshop
    Master = 6,          // onloc_master (level master)
    MagicShop = 7,       // onloc_magicshop
    Dungeons = 8,        // onloc_dungeons
    DeathMaze = 9,       // onloc_deathmaze
    MadMage = 17,        // onloc_madmage (groggo's shop)
    ArmorShop = 18,      // onloc_armorshop
    Bank = 19,           // onloc_bank
    ReportRoom = 20,     // onloc_reportroom
    Healer = 21,         // onloc_healer
    Marketplace = 22,    // onloc_marketplace
    FoodStore = 23,      // onloc_foodstore
    PlayerMarket = 24,   // onloc_plymarket
    Recruit = 25,        // onloc_recruit (hall of recruitment)
    Dormitory = 26,      // onloc_dormitory
    AnchorRoad = 27,     // onloc_anchorroad
    Orbs = 28,           // onloc_orbs (orbs bar)
    BobsBeer = 31,       // onloc_bobsbeer (Bob's Beer Hut)
    Alchemist = 32,      // onloc_alchemist
    Steroids = 33,       // onloc_steroids (Lizard's Training Center)
    Drugs = 34,          // onloc_drugs
    Darkness = 35,       // onloc_darkness
    Whores = 36,         // onloc_whores
    Gigolos = 38,        // onloc_gigolos
    OutsideInn = 39,     // onloc_outsideinn
    TeamCorner = 41,     // onloc_teamcorner
    Gym = 42,           // UNUSED - Gym removed (doesn't fit single-player endless format)
    LoveCorner = 77,    // love corner location same as constant above
    Temple = 47,         // onloc_temple (altar of the gods)
    BountyRoom = 44,     // onloc_bountyroom
    QuestHall = 75,      // onloc_questhall
    
    // Castle locations
    Castle = 70,         // onloc_castle (royal castle)
    RoyalMail = 71,      // onloc_royalmail
    CourtMage = 72,      // onloc_courtmage
    WarChamber = 73,     // onloc_warchamber
    QuestMaster = 74,    // onloc_questmaster
    RoyalOrphanage = 77, // onloc_royorphanag
    GuardOffice = 80,    // onloc_guardoffice
    OutsideCastle = 81,  // onloc_outcastle
    
    // Prison locations
    Prison = 90,         // onloc_prison
    Prisoner = 91,       // onloc_prisoner (in cell)
    PrisonerOpen = 92,   // onloc_prisonerop (cell door open)
    PrisonerExecution = 93, // onloc_prisonerex
    PrisonWalk = 94,     // onloc_prisonwalk (outside prison)
    PrisonBreak = 95,    // onloc_prisonbreak
    ChestLoot = 96,      // onloc_chestloot
    
    // Relationship locations
    LoveStreet = 200,    // onloc_lovestreet
    Home = 201,          // onloc_home
    Nursery = 202,       // onloc_nursery
    Kidnapper = 203,     // onloc_kidnapper
    GiftShop = 204,      // onloc_giftshop
    
    // Special locations
    IceCaves = 300,      // onloc_icecaves
    Heaven = 400,        // onloc_heaven
    HeavenBoss = 401,    // onloc_heaven_boss

    // BBS SysOp locations
    SysOpConsole = 500,  // SysOp administration console (BBS mode only)

    // PvP Arena (online mode only)
    Arena = 501,         // PvP combat arena

    Closed = 30000       // onloc_closed (for fake players)
}

/// <summary>
/// Combat speed settings - controls text delay during combat
/// </summary>
public enum CombatSpeed
{
    Normal = 0,   // Full delays (default, best for reading)
    Fast = 1,     // 50% delays
    Instant = 2   // No delays (0ms)
} 
