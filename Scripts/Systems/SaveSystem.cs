using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using UsurperRemake.Data;
using Godot;

namespace UsurperRemake.Systems
{
    /// <summary>
    /// Comprehensive save/load system for Usurper Reloaded
    /// Supports multiple daily cycle modes and complete world state persistence
    /// Supports BBS door mode with per-BBS save isolation
    /// </summary>
    public class SaveSystem
    {
        private static SaveSystem? instance;
        public static SaveSystem Instance => instance ??= new SaveSystem();

        /// <summary>
        /// Initialize the singleton with a specific backend before first use.
        /// Must be called before any code accesses Instance, otherwise the default
        /// FileSaveBackend is created. Used by online mode to inject SqlSaveBackend.
        /// </summary>
        public static void InitializeWithBackend(ISaveBackend backend)
        {
            if (instance != null)
            {
                DebugLogger.Instance.LogWarning("SAVE", "SaveSystem already initialized - replacing backend");
            }
            instance = new SaveSystem(backend);
        }

        private readonly ISaveBackend backend;

        /// <summary>
        /// Throttle autosaves in online/MUD mode to avoid serializing ~23 MB of JSON on every location change.
        /// Local/single-player mode is unthrottled (saves are fast to disk).
        /// </summary>
        private DateTime _lastAutoSaveTime = DateTime.MinValue;
        private const int AutoSaveIntervalSeconds = 60;

        /// <summary>
        /// The active save backend (FileSaveBackend for local, SqlSaveBackend for online)
        /// </summary>
        public ISaveBackend Backend => backend;

        /// <summary>
        /// Get the active save directory (delegates to backend)
        /// </summary>
        public string saveDirectory => backend.GetSaveDirectory();

        /// <summary>
        /// Public accessor for save directory (used by SysOp console)
        /// </summary>
        public string GetSaveDirectory() => backend.GetSaveDirectory();

        public SaveSystem() : this(new FileSaveBackend())
        {
        }

        public SaveSystem(ISaveBackend saveBackend)
        {
            backend = saveBackend;
        }
        
        /// <summary>
        /// Save complete game state including player, world, and NPCs
        /// </summary>
        public async Task<bool> SaveGame(string playerName, Character player)
        {
            try
            {
                // Create backup of existing save before overwriting
                backend.CreateBackup(playerName);

                // Log save event
                DebugLogger.Instance.LogSave(playerName, player.Level, player.HP, player.MaxHP, player.Gold);
                DebugLogger.Instance.LogDebug("SAVE", $"BaseMaxHP={player.BaseMaxHP}, BaseMaxMana={player.BaseMaxMana}");

                // In online/MUD mode, use per-session day state to avoid shared singleton cross-contamination
                var engine = GameEngine.Instance;
                bool useSessionDay = UsurperRemake.BBS.DoorMode.IsOnlineMode && engine != null;

                var saveData = new SaveGameData
                {
                    Version = GameConfig.SaveVersion,
                    SaveTime = DateTime.Now,
                    LastDailyReset = useSessionDay ? engine.SessionLastResetTime : DailySystemManager.Instance.LastResetTime,
                    CurrentDay = useSessionDay ? engine.SessionCurrentDay : DailySystemManager.Instance.CurrentDay,
                    DailyCycleMode = useSessionDay ? engine.SessionDailyCycleMode : DailySystemManager.Instance.CurrentMode,
                    Player = SerializePlayer(player),
                    NPCs = await SerializeNPCs(),
                    WorldState = SerializeWorldState(),
                    Settings = SerializeDailySettings(),
                    StorySystems = SerializeStorySystems(),
                    Telemetry = TelemetrySystem.Instance.Serialize()
                };

                var success = await backend.WriteGameData(playerName, saveData);

                if (success)
                {
                    DebugLogger.Instance.LogDebug("SAVE", $"Game saved successfully for '{playerName}'");
                }

                // Sync stats to Steam if available (blocked if dev menu was used)
                if (player is Player playerChar && playerChar.Statistics != null && !player.DevMenuUsed)
                {
                    SteamIntegration.SyncPlayerStats(playerChar.Statistics);
                }

                // In online mode, push NPC changes to shared world_state so the
                // world sim and dashboard can see team changes, combat results, etc.
                if (success && UsurperRemake.BBS.DoorMode.IsOnlineMode && OnlineStateManager.IsActive && OnlineStateManager.Instance != null)
                {
                    try
                    {
                        var sharedNpcData = OnlineStateManager.SerializeCurrentNPCs();
                        await OnlineStateManager.Instance.SaveSharedNPCs(sharedNpcData);
                        await OnlineStateManager.Instance.SaveRoyalCourtToWorldState();
                        await OnlineStateManager.Instance.SaveEconomyToWorldState();
                        DebugLogger.Instance.LogDebug("SAVE", "NPC, royal court, and economy synced to world_state");
                    }
                    catch (Exception syncEx)
                    {
                        DebugLogger.Instance.LogError("SAVE", $"World state sync failed (non-fatal): {syncEx.Message}");
                    }
                }

                return success;
            }
            catch (Exception ex)
            {
                DebugLogger.Instance.LogSystemError("SAVE", $"Failed to save game: {ex.Message}", ex.StackTrace);
                return false;
            }
        }
        
        /// <summary>
        /// Load complete game state
        /// </summary>
        public async Task<SaveGameData?> LoadGame(string playerName)
        {
            return await backend.ReadGameData(playerName);
        }
        
        /// <summary>
        /// Check if a save file exists for the player
        /// </summary>
        public bool SaveExists(string playerName)
        {
            return backend.GameDataExists(playerName);
        }

        /// <summary>
        /// Delete a save file
        /// </summary>
        public bool DeleteSave(string playerName)
        {
            return backend.DeleteGameData(playerName);
        }
        
        /// <summary>
        /// Get list of all save files
        /// </summary>
        public List<SaveInfo> GetAllSaves()
        {
            return backend.GetAllSaves();
        }

        /// <summary>
        /// Auto-save the current game state with rotation (keeps 5 most recent autosaves)
        /// </summary>
        public async Task<bool> AutoSave(Character player)
        {
            if (player == null) return false;

            // Throttle autosaves in online/MUD mode — the full save serializes ~5 MB of player data
            // plus ~18 MB of NPC data and writes to SQLite, taking several seconds.
            // Only save every 60 seconds instead of on every location redraw.
            if (UsurperRemake.BBS.DoorMode.IsOnlineMode)
            {
                var elapsed = (DateTime.UtcNow - _lastAutoSaveTime).TotalSeconds;
                if (elapsed < AutoSaveIntervalSeconds)
                    return true; // Pretend success, save will happen soon
            }

            var playerName = player.Name2 ?? player.Name1;

            // In online/MUD mode, use per-session day state to avoid shared singleton cross-contamination
            var engine2 = GameEngine.Instance;
            bool useSessionDay2 = UsurperRemake.BBS.DoorMode.IsOnlineMode && engine2 != null;

            // Serialize game state
            var saveData = new SaveGameData
            {
                Version = GameConfig.SaveVersion,
                SaveTime = DateTime.Now,
                LastDailyReset = useSessionDay2 ? engine2.SessionLastResetTime : DailySystemManager.Instance.LastResetTime,
                CurrentDay = useSessionDay2 ? engine2.SessionCurrentDay : DailySystemManager.Instance.CurrentDay,
                DailyCycleMode = useSessionDay2 ? engine2.SessionDailyCycleMode : DailySystemManager.Instance.CurrentMode,
                Player = SerializePlayer(player),
                NPCs = await SerializeNPCs(),
                WorldState = SerializeWorldState(),
                Settings = SerializeDailySettings(),
                StorySystems = SerializeStorySystems(),
                Telemetry = TelemetrySystem.Instance.Serialize()
            };

            var success = await backend.WriteAutoSave(playerName, saveData);

            // Sync stats to Steam if available (blocked if dev menu was used)
            if (success && player is Player playerChar && playerChar.Statistics != null && !player.DevMenuUsed)
            {
                SteamIntegration.SyncPlayerStats(playerChar.Statistics);
            }

            // In online mode, push NPC changes to shared world_state
            // Skip this — the WorldSimService already saves NPC state every 5 minutes
            // with dirty-checking. Player sessions don't need to duplicate this work.
            // The world sim is the authority for NPC state in online mode.

            if (success)
                _lastAutoSaveTime = DateTime.UtcNow;

            return success;
        }

        /// <summary>
        /// Get all saves for a specific player (including autosaves)
        /// </summary>
        public List<SaveInfo> GetPlayerSaves(string playerName)
        {
            return backend.GetPlayerSaves(playerName);
        }

        /// <summary>
        /// Get the most recent save for a player (autosave or manual)
        /// </summary>
        public SaveInfo? GetMostRecentSave(string playerName)
        {
            return backend.GetMostRecentSave(playerName);
        }

        /// <summary>
        /// Load a save by filename
        /// </summary>
        public async Task<SaveGameData?> LoadSaveByFileName(string fileName)
        {
            return await backend.ReadGameDataByFileName(fileName);
        }

        /// <summary>
        /// Get list of all unique player names that have saves
        /// </summary>
        public List<string> GetAllPlayerNames()
        {
            return backend.GetAllPlayerNames();
        }

        /// <summary>
        /// Create backup of existing save before overwriting
        /// </summary>
        public void CreateBackup(string playerName)
        {
            backend.CreateBackup(playerName);
        }

        /// <summary>
        /// Public accessor for story systems serialization.
        /// Used by OnlineStateManager to save shared world state.
        /// </summary>
        public StorySystemsData SerializeStorySystemsPublic()
        {
            return SerializeStorySystems();
        }

        /// <summary>
        /// Collect all dynamic equipment IDs (>= 100000) from both the player's equipped items
        /// and all companion equipped items. This ensures companion dynamic equipment definitions
        /// are saved alongside the player's, preventing equipment loss on reload.
        /// </summary>
        private HashSet<int> CollectAllDynamicEquipmentIds(Character player)
        {
            var ids = new HashSet<int>();

            // Player's own equipped items
            if (player.EquippedItems != null)
            {
                foreach (var id in player.EquippedItems.Values)
                {
                    if (id >= 100000) ids.Add(id);
                }
            }

            // Companion equipped items (Lyris, Aldric, Mira, Vex)
            try
            {
                var companionData = CompanionSystem.Instance.Serialize();
                if (companionData?.CompanionStates != null)
                {
                    foreach (var comp in companionData.CompanionStates)
                    {
                        if (comp.EquippedItemsSave != null)
                        {
                            foreach (var id in comp.EquippedItemsSave.Values)
                            {
                                if (id >= 100000) ids.Add(id);
                            }
                        }
                    }
                }
            }
            catch { /* CompanionSystem not initialized */ }

            return ids;
        }

        private PlayerData SerializePlayer(Character player)
        {
            return new PlayerData
            {
                // Unique player identifier (critical for romance/family systems)
                Id = player.ID ?? player.Name2 ?? player.Name1 ?? Guid.NewGuid().ToString(),

                // Basic info
                Name1 = player.Name1,
                Name2 = player.Name2,
                RealName = (player as Player)?.RealName ?? player.Name1,
                
                // Core stats
                Level = player.Level,
                Experience = player.Experience,
                HP = player.HP,
                MaxHP = player.MaxHP,
                Gold = player.Gold,
                BankGold = player.BankGold,
                
                // Attributes
                Strength = player.Strength,
                Defence = player.Defence,
                Stamina = player.Stamina,
                Agility = player.Agility,
                Charisma = player.Charisma,
                Dexterity = player.Dexterity,
                Wisdom = player.Wisdom,
                Intelligence = player.Intelligence,
                Constitution = player.Constitution,
                Mana = player.Mana,
                MaxMana = player.MaxMana,

                // Equipment and items (CRITICAL FIXES)
                Healing = player.Healing,     // POTIONS
                ManaPotions = player.ManaPotions, // MANA POTIONS
                WeapPow = player.WeapPow,     // WEAPON POWER
                ArmPow = player.ArmPow,       // ARMOR POWER
                
                // Character details
                Race = player.Race,
                Class = player.Class,
                Sex = (char)((int)player.Sex),
                Age = player.Age,
                Difficulty = player.Difficulty,
                
                // Game state
                CurrentLocation = player.Location.ToString(),
                TurnCount = player.TurnCount,  // World simulation turn counter
                TurnsRemaining = player.TurnsRemaining,
                DaysInPrison = player.DaysInPrison,
                CellDoorOpen = player.CellDoorOpen,
                RescuedBy = player.RescuedBy ?? "",
                PrisonEscapes = player.PrisonEscapes,

                // Daily limits
                Fights = player.Fights,
                PFights = player.PFights,
                TFights = player.TFights,
                Thiefs = player.Thiefs,
                Brawls = player.Brawls,
                Assa = player.Assa,
                DarkNr = player.DarkNr,
                ChivNr = player.ChivNr,
                
                // Items and equipment
                Items = player.Item?.ToArray() ?? new int[0],
                ItemTypes = player.ItemType?.Select(t => (int)t).ToArray() ?? new int[0],

                // NEW: Modern RPG Equipment System
                EquippedItems = player.EquippedItems?.ToDictionary(
                    kvp => (int)kvp.Key,
                    kvp => kvp.Value
                ) ?? new Dictionary<int, int>(),

                // Curse status for equipped items
                WeaponCursed = player.WeaponCursed,
                ArmorCursed = player.ArmorCursed,
                ShieldCursed = player.ShieldCursed,

                // Player inventory (dungeon loot items)
                Inventory = player.Inventory?.Select(item => new InventoryItemData
                {
                    Name = item.Name,
                    Value = item.Value,
                    Type = item.Type,
                    Attack = item.Attack,
                    Armor = item.Armor,
                    Strength = item.Strength,
                    Dexterity = item.Dexterity,
                    Wisdom = item.Wisdom,
                    Defence = item.Defence,
                    HP = item.HP,
                    Mana = item.Mana,
                    Charisma = item.Charisma,
                    MinLevel = item.MinLevel,
                    IsCursed = item.IsCursed,
                    Cursed = item.Cursed,
                    IsIdentified = item.IsIdentified,
                    Shop = item.Shop,
                    Dungeon = item.Dungeon,
                    Description = item.Description?.ToList() ?? new List<string>()
                }).ToList() ?? new List<InventoryItemData>(),

                // Dynamic equipment (items equipped from dungeon loot that need definitions saved)
                // Must include dynamic IDs from BOTH player's equipped items AND companion equipped items,
                // otherwise companion dynamic equipment vanishes on reload (the ID is saved but the
                // Equipment object definition is lost).
                DynamicEquipment = CollectAllDynamicEquipmentIds(player)
                    .Select(id => EquipmentDatabase.GetById(id))
                    .Where(equip => equip != null)
                    .Select(equip => new DynamicEquipmentData
                {
                    Id = equip.Id,
                    Name = equip.Name,
                    Description = equip.Description ?? "",
                    Slot = (int)equip.Slot,
                    WeaponPower = equip.WeaponPower,
                    ArmorClass = equip.ArmorClass,
                    ShieldBonus = equip.ShieldBonus,
                    BlockChance = equip.BlockChance,
                    StrengthBonus = equip.StrengthBonus,
                    DexterityBonus = equip.DexterityBonus,
                    ConstitutionBonus = equip.ConstitutionBonus,
                    IntelligenceBonus = equip.IntelligenceBonus,
                    WisdomBonus = equip.WisdomBonus,
                    CharismaBonus = equip.CharismaBonus,
                    MaxHPBonus = equip.MaxHPBonus,
                    MaxManaBonus = equip.MaxManaBonus,
                    DefenceBonus = equip.DefenceBonus,
                    MinLevel = equip.MinLevel,
                    Value = equip.Value,
                    IsCursed = equip.IsCursed,
                    Rarity = (int)equip.Rarity,
                    WeaponType = (int)equip.WeaponType,
                    Handedness = (int)equip.Handedness,
                    ArmorType = (int)equip.ArmorType,
                    StaminaBonus = equip.StaminaBonus,
                    AgilityBonus = equip.AgilityBonus,
                    CriticalChanceBonus = equip.CriticalChanceBonus,
                    CriticalDamageBonus = equip.CriticalDamageBonus,
                    MagicResistance = equip.MagicResistance,
                    PoisonDamage = equip.PoisonDamage,
                    LifeSteal = equip.LifeSteal,
                    HasFireEnchant = equip.HasFireEnchant,
                    HasFrostEnchant = equip.HasFrostEnchant,
                    HasLightningEnchant = equip.HasLightningEnchant,
                    HasPoisonEnchant = equip.HasPoisonEnchant,
                    HasHolyEnchant = equip.HasHolyEnchant,
                    HasShadowEnchant = equip.HasShadowEnchant,
                    ManaSteal = equip.ManaSteal,
                    ArmorPiercing = equip.ArmorPiercing,
                    Thorns = equip.Thorns,
                    HPRegen = equip.HPRegen,
                    ManaRegen = equip.ManaRegen
                }).ToList(),

                // Base stats
                BaseStrength = player.BaseStrength,
                BaseDexterity = player.BaseDexterity,
                BaseConstitution = player.BaseConstitution,
                BaseIntelligence = player.BaseIntelligence,
                BaseWisdom = player.BaseWisdom,
                BaseCharisma = player.BaseCharisma,
                BaseMaxHP = player.BaseMaxHP,
                BaseMaxMana = player.BaseMaxMana,
                BaseDefence = player.BaseDefence,
                BaseStamina = player.BaseStamina,
                BaseAgility = player.BaseAgility,

                // Ruler status
                King = player.King,

                // Social/Team
                Team = player.Team,
                TeamPassword = player.TeamPW,
                IsTeamLeader = player.CTurf,
                TeamRec = player.TeamRec,
                BGuard = player.BGuard,

                // Status
                Chivalry = player.Chivalry,
                Darkness = player.Darkness,
                Mental = player.Mental,
                Poison = player.Poison,

                // Active status effects (convert enum keys to int)
                ActiveStatuses = player.ActiveStatuses?.ToDictionary(
                    kvp => (int)kvp.Key,
                    kvp => kvp.Value
                ) ?? new Dictionary<int, int>(),

                GnollP = player.GnollP,
                Addict = player.Addict,
                SteroidDays = player.SteroidDays,
                DrugEffectDays = player.DrugEffectDays,
                ActiveDrug = (int)player.ActiveDrug,
                Mercy = player.Mercy,

                // Disease status
                Blind = player.Blind,
                Plague = player.Plague,
                Smallpox = player.Smallpox,
                Measles = player.Measles,
                Leprosy = player.Leprosy,
                LoversBane = player.LoversBane,

                // Divine Wrath System
                DivineWrathLevel = player.DivineWrathLevel,
                AngeredGodName = player.AngeredGodName ?? "",
                BetrayedForGodName = player.BetrayedForGodName ?? "",
                DivineWrathPending = player.DivineWrathPending,
                DivineWrathTurnsRemaining = player.DivineWrathTurnsRemaining,

                // Royal Loan
                RoyalLoanAmount = player.RoyalLoanAmount,
                RoyalLoanDueDay = player.RoyalLoanDueDay,
                RoyalLoanBountyPosted = player.RoyalLoanBountyPosted,

                // Noble Title
                NobleTitle = player.NobleTitle,

                // Royal Mercenaries
                RoyalMercenaries = player.RoyalMercenaries?.Count > 0
                    ? player.RoyalMercenaries.Select(m => new RoyalMercenarySaveData
                    {
                        Name = m.Name,
                        Role = m.Role,
                        ClassId = (int)m.Class,
                        Sex = (int)m.Sex,
                        Level = m.Level,
                        HP = m.HP,
                        MaxHP = m.MaxHP,
                        Mana = m.Mana,
                        MaxMana = m.MaxMana,
                        Strength = m.Strength,
                        Defence = m.Defence,
                        WeapPow = m.WeapPow,
                        ArmPow = m.ArmPow,
                        Agility = m.Agility,
                        Dexterity = m.Dexterity,
                        Wisdom = m.Wisdom,
                        Intelligence = m.Intelligence,
                        Constitution = m.Constitution,
                        Healing = m.Healing
                    }).ToList()
                    : null,

                // Blood Price / Murder Weight System
                MurderWeight = player.MurderWeight,
                PermakillLog = player.PermakillLog ?? new(),
                LastMurderWeightDecay = player.LastMurderWeightDecay,

                // Combat statistics (kill/death counts)
                MKills = (int)player.MKills,
                MDefeats = (int)player.MDefeats,
                PKills = (int)player.PKills,
                PDefeats = (int)player.PDefeats,

                // Character settings
                DevMenuUsed = player.DevMenuUsed,
                AutoHeal = player.AutoHeal,
                CombatSpeed = player.CombatSpeed,
                SkipIntimateScenes = player.SkipIntimateScenes,
                ScreenReaderMode = player.ScreenReaderMode,
                ColorTheme = player.ColorTheme,
                Loyalty = player.Loyalty,
                Haunt = player.Haunt,
                Master = player.Master,
                WellWish = player.WellWish,

                // Physical appearance
                Height = player.Height,
                Weight = player.Weight,
                Eyes = player.Eyes,
                Hair = player.Hair,
                Skin = player.Skin,

                // Character flavor text
                Phrases = player.Phrases?.ToList() ?? new List<string>(),
                Description = player.Description?.ToList() ?? new List<string>(),
                
                // Relationships
                Relationships = SerializeRelationships(player),

                // Romance Tracker Data
                RomanceData = RomanceTracker.Instance.ToSaveData(),

                // Quests (only this player's claimed quests, not the entire database)
                ActiveQuests = SerializePlayerQuests(player),
                
                // Achievements (for Player type)
                Achievements = (player as Player)?.Achievements ?? new Dictionary<string, bool>(),

                // Learned combat abilities
                LearnedAbilities = player.LearnedAbilities?.ToList() ?? new List<string>(),

                // Combat quickbar
                Quickbar = player.Quickbar?.ToList() ?? new List<string?>(),

                // Training system
                Trains = player.Trains,
                TrainingPoints = player.TrainingPoints,
                SkillProficiencies = player.SkillProficiencies?.ToDictionary(
                    kvp => kvp.Key,
                    kvp => (int)kvp.Value) ?? new Dictionary<string, int>(),
                SkillTrainingProgress = player.SkillTrainingProgress ?? new Dictionary<string, int>(),

                // Gold-based stat training (v0.30.9)
                StatTrainingCounts = player.StatTrainingCounts ?? new Dictionary<string, int>(),
                UnpaidWageDays = player.UnpaidWageDays ?? new Dictionary<string, int>(),
                CraftingMaterials = player.CraftingMaterials ?? new Dictionary<string, int>(),

                // Spells and skills
                Spells = player.Spell ?? new List<List<bool>>(),
                Skills = player.Skill ?? new List<int>(),

                // Legacy equipment slots
                LHand = player.LHand,
                RHand = player.RHand,
                Head = player.Head,
                Body = player.Body,
                Arms = player.Arms,
                LFinger = player.LFinger,
                RFinger = player.RFinger,
                Legs = player.Legs,
                Feet = player.Feet,
                Waist = player.Waist,
                Neck = player.Neck,
                Neck2 = player.Neck2,
                Face = player.Face,
                Shield = player.Shield,
                Hands = player.Hands,
                ABody = player.ABody,

                // Combat flags
                Immortal = player.Immortal,
                BattleCry = player.BattleCry ?? "",
                BGuardNr = player.BGuardNr,

                // Timestamps
                LastLogin = (player as Player)?.LastLogin ?? DateTime.Now,
                AccountCreated = (player as Player)?.AccountCreated ?? DateTime.Now,

                // Gym cooldown timers
                LastStrengthTraining = player.LastStrengthTraining,
                LastDexterityTraining = player.LastDexterityTraining,
                LastTugOfWar = player.LastTugOfWar,
                LastWrestling = player.LastWrestling,

                // Player statistics - update session time before saving
                Statistics = UpdateAndGetStatistics(player),

                // Player achievements
                AchievementsData = SerializeAchievements(player),

                // Home Upgrade System (v0.44.0 overhaul)
                HomeLevel = player.HomeLevel,
                ChestLevel = player.ChestLevel,
                TrainingRoomLevel = player.TrainingRoomLevel,
                GardenLevel = player.GardenLevel,
                BedLevel = player.BedLevel,
                HearthLevel = player.HearthLevel,
                HasTrophyRoom = player.HasTrophyRoom,
                HasTeleportCircle = player.HasTeleportCircle,
                HasLegendaryArmory = player.HasLegendaryArmory,
                HasVitalityFountain = player.HasVitalityFountain,
                HasStudy = player.HasStudy,
                HasServants = player.HasServants,
                PermanentDamageBonus = player.PermanentDamageBonus,
                PermanentDefenseBonus = player.PermanentDefenseBonus,
                BonusMaxHP = player.BonusMaxHP,
                HomeRestsToday = player.HomeRestsToday,
                HerbsGatheredToday = player.HerbsGatheredToday,
                WellRestedCombats = player.WellRestedCombats,
                WellRestedBonus = player.WellRestedBonus,
                CycleExpMultiplier = player.CycleExpMultiplier,
                ChestContents = SerializeChestContents(player),

                // Faction consumable properties (v0.40.2)
                PoisonCoatingCombats = player.PoisonCoatingCombats,
                SmokeBombs = player.SmokeBombs,
                InnerSanctumLastDay = player.InnerSanctumLastDay,

                // Daily tracking (real-world-date based)
                LastDailyResetBoundary = player.LastDailyResetBoundary,
                LastPrayerRealDate = player.LastPrayerRealDate,
                LastInnerSanctumRealDate = player.LastInnerSanctumRealDate,
                LastBindingOfSoulsRealDate = player.LastBindingOfSoulsRealDate,
                SethFightsToday = player.SethFightsToday,
                ArmWrestlesToday = player.ArmWrestlesToday,
                RoyQuestsToday = player.RoyQuestsToday,

                // Dark Alley Overhaul (v0.41.0)
                GroggoShadowBlessingDex = player.GroggoShadowBlessingDex,
                SteroidShopPurchases = player.SteroidShopPurchases,
                AlchemistINTBoosts = player.AlchemistINTBoosts,
                GamblingRoundsToday = player.GamblingRoundsToday,
                PitFightsToday = player.PitFightsToday,
                LoanAmount = player.LoanAmount,
                LoanDaysRemaining = player.LoanDaysRemaining,
                LoanInterestAccrued = player.LoanInterestAccrued,
                DarkAlleyReputation = player.DarkAlleyReputation,
                DrugTolerance = player.DrugTolerance?.Count > 0 ? new Dictionary<int, int>(player.DrugTolerance) : null,
                SafeHouseResting = player.SafeHouseResting,

                // Recurring Duelist Rival
                RecurringDuelist = SerializeRecurringDuelist(player),

                // Dungeon progression
                ClearedSpecialFloors = player.ClearedSpecialFloors ?? new HashSet<int>(),

                // Dungeon floor persistence
                DungeonFloorStates = SerializeDungeonFloorStates(player),

                // Hint system - which hints have been shown
                HintsShown = player.HintsShown ?? new HashSet<string>()
            };
        }

        /// <summary>
        /// Serialize dungeon floor states for saving
        /// </summary>
        private Dictionary<int, DungeonFloorStateData> SerializeDungeonFloorStates(Character player)
        {
            var result = new Dictionary<int, DungeonFloorStateData>();

            if (player.DungeonFloorStates == null)
                return result;

            foreach (var kvp in player.DungeonFloorStates)
            {
                var state = kvp.Value;
                var data = new DungeonFloorStateData
                {
                    FloorLevel = state.FloorLevel,
                    LastClearedAt = state.LastClearedAt,
                    LastVisitedAt = state.LastVisitedAt,
                    EverCleared = state.EverCleared,
                    IsPermanentlyClear = state.IsPermanentlyClear,
                    BossDefeated = state.BossDefeated,
                    CurrentRoomId = state.CurrentRoomId,
                    Rooms = new List<DungeonRoomStateData>()
                };

                foreach (var roomKvp in state.RoomStates)
                {
                    var roomState = roomKvp.Value;
                    data.Rooms.Add(new DungeonRoomStateData
                    {
                        RoomId = roomState.RoomId,
                        IsExplored = roomState.IsExplored,
                        IsCleared = roomState.IsCleared,
                        TreasureLooted = roomState.TreasureLooted,
                        TrapTriggered = roomState.TrapTriggered,
                        EventCompleted = roomState.EventCompleted,
                        PuzzleSolved = roomState.PuzzleSolved,
                        RiddleAnswered = roomState.RiddleAnswered,
                        LoreCollected = roomState.LoreCollected,
                        InsightGranted = roomState.InsightGranted,
                        MemoryTriggered = roomState.MemoryTriggered,
                        SecretBossDefeated = roomState.SecretBossDefeated
                    });
                }

                result[kvp.Key] = data;
            }

            return result;
        }

        /// <summary>
        /// Serialize recurring duelist data for saving
        /// </summary>
        private DuelistData? SerializeRecurringDuelist(Character player)
        {
            string playerId = player.ID ?? player.Name;
            var duelistData = DungeonLocation.GetRecurringDuelist(playerId);
            if (duelistData.HasValue)
            {
                var duelist = duelistData.Value;
                return new DuelistData
                {
                    Name = duelist.Name,
                    Weapon = duelist.Weapon,
                    Level = duelist.Level,
                    TimesEncountered = duelist.TimesEncountered,
                    PlayerWins = duelist.PlayerWins,
                    PlayerLosses = duelist.PlayerLosses,
                    WasInsulted = duelist.WasInsulted,
                    IsDead = duelist.IsDead
                };
            }
            return null;
        }

        /// <summary>
        /// Serialize player achievements for saving
        /// </summary>
        private List<InventoryItemData>? SerializeChestContents(Character player)
        {
            var playerKey = (player is Player p ? p.RealName : player.Name2) ?? player.Name2;
            if (!UsurperRemake.Locations.HomeLocation.PlayerChests.TryGetValue(playerKey, out var chest) || chest.Count == 0)
                return null;

            return chest.Select(item => new InventoryItemData
            {
                Name = item.Name,
                Value = item.Value,
                Type = item.Type,
                Attack = item.Attack,
                Armor = item.Armor,
                Strength = item.Strength,
                Dexterity = item.Dexterity,
                Wisdom = item.Wisdom,
                Defence = item.Defence,
                HP = item.HP,
                Mana = item.Mana,
                Charisma = item.Charisma,
                MinLevel = item.MinLevel,
                IsCursed = item.IsCursed,
                IsIdentified = item.IsIdentified,
                Shop = item.Shop,
                Dungeon = item.Dungeon,
                Description = item.Description?.ToList() ?? new List<string>()
            }).ToList();
        }

        private PlayerAchievementsData SerializeAchievements(Character player)
        {
            return new PlayerAchievementsData
            {
                UnlockedAchievements = new HashSet<string>(player.Achievements.UnlockedAchievements),
                UnlockDates = new Dictionary<string, DateTime>(player.Achievements.UnlockDates)
            };
        }

        /// <summary>
        /// Update statistics session time and return for saving
        /// </summary>
        private PlayerStatistics UpdateAndGetStatistics(Character player)
        {
            player.Statistics.UpdateSessionTime();
            return player.Statistics;
        }
        
        private async Task<List<NPCData>> SerializeNPCs()
        {
            var npcData = new List<NPCData>();

            // In online mode, NPC data is authoritative in world_state table —
            // skip serializing 60 NPCs into every player save (saves ~9MB per player)
            if (UsurperRemake.BBS.DoorMode.IsOnlineMode)
                return npcData;

            // Get all NPCs from NPCSpawnSystem
            var worldNPCs = GetWorldNPCs();

            // Get current king for reference
            var currentKing = global::CastleLocation.GetCurrentKing();

            foreach (var npc in worldNPCs)
            {
                npcData.Add(new NPCData
                {
                    Id = npc.Id ?? Guid.NewGuid().ToString(),
                    CharacterID = npc.ID ?? "",  // Save the Character.ID property (used by RomanceTracker)
                    Name = npc.Name2 ?? npc.Name1,
                    Archetype = npc.Archetype ?? "citizen",
                    Level = npc.Level,
                    HP = npc.HP,
                    MaxHP = npc.MaxHP,
                    BaseMaxHP = npc.BaseMaxHP > 0 ? npc.BaseMaxHP : npc.MaxHP,  // Fallback to MaxHP if BaseMaxHP not set
                    BaseMaxMana = npc.BaseMaxMana > 0 ? npc.BaseMaxMana : npc.MaxMana,  // Fallback to MaxMana if BaseMaxMana not set
                    Location = npc.CurrentLocation ?? npc.Location.ToString(),

                    // Character stats
                    Experience = npc.Experience,
                    Strength = npc.Strength,
                    Defence = npc.Defence,
                    Agility = npc.Agility,
                    Dexterity = npc.Dexterity,
                    Mana = npc.Mana,
                    MaxMana = npc.MaxMana,
                    WeapPow = npc.WeapPow,
                    ArmPow = npc.ArmPow,

                    // Base stats (without equipment bonuses)
                    BaseStrength = npc.BaseStrength > 0 ? npc.BaseStrength : npc.Strength,
                    BaseDefence = npc.BaseDefence > 0 ? npc.BaseDefence : npc.Defence,
                    BaseDexterity = npc.BaseDexterity > 0 ? npc.BaseDexterity : npc.Dexterity,
                    BaseAgility = npc.BaseAgility > 0 ? npc.BaseAgility : npc.Agility,
                    BaseStamina = npc.BaseStamina > 0 ? npc.BaseStamina : npc.Stamina,
                    BaseConstitution = npc.BaseConstitution > 0 ? npc.BaseConstitution : npc.Constitution,
                    BaseIntelligence = npc.BaseIntelligence > 0 ? npc.BaseIntelligence : npc.Intelligence,
                    BaseWisdom = npc.BaseWisdom > 0 ? npc.BaseWisdom : npc.Wisdom,
                    BaseCharisma = npc.BaseCharisma > 0 ? npc.BaseCharisma : npc.Charisma,

                    // Class and race
                    Class = npc.Class,
                    Race = npc.Race,
                    Sex = (char)npc.Sex,

                    // Team and political status - CRITICAL for persistence
                    Team = npc.Team ?? "",
                    IsTeamLeader = npc.CTurf,
                    IsKing = currentKing != null && currentKing.Name == npc.Name,

                    // Death status - permanent death tracking
                    IsDead = npc.IsDead,

                    // Lifecycle - aging and natural death
                    Age = npc.Age,
                    BirthDate = npc.BirthDate,
                    IsAgedDeath = npc.IsAgedDeath,
                    IsPermaDead = npc.IsPermaDead,
                    PregnancyDueDate = npc.PregnancyDueDate,

                    // Dialogue tracking
                    RecentDialogueIds = NPCDialogueDatabase.GetRecentlyUsedIds(npc.Name2 ?? npc.Name1 ?? ""),

                    // Social emergence
                    EmergentRole = npc.EmergentRole ?? "",
                    RoleStabilityTicks = npc.RoleStabilityTicks,

                    // Marriage status
                    IsMarried = npc.IsMarried,
                    Married = npc.Married,
                    SpouseName = npc.SpouseName ?? "",
                    MarriedTimes = npc.MarriedTimes,

                    // Faction affiliation
                    NPCFaction = npc.NPCFaction.HasValue ? (int)npc.NPCFaction.Value : -1,

                    // Alignment
                    Chivalry = npc.Chivalry,
                    Darkness = npc.Darkness,

                    // AI state
                    PersonalityProfile = SerializePersonality(npc.Brain?.Personality),
                    Memories = SerializeMemories(npc.Brain?.Memory),
                    CurrentGoals = SerializeGoals(npc.Brain?.Goals),
                    EmotionalState = SerializeEmotionalState(npc.Brain?.Emotions),

                    // Relationships
                    Relationships = SerializeNPCRelationships(npc),

                    // Enemies
                    Enemies = npc.Enemies?.ToList() ?? new List<string>(),

                    // Inventory
                    Gold = npc.Gold,
                    BankGold = npc.BankGold,
                    Items = npc.Item?.ToArray() ?? new int[0],

                    // Market inventory for NPC trading
                    MarketInventory = npc.MarketInventory?.Select(item => new MarketItemData
                    {
                        ItemName = item.Name,
                        ItemValue = item.Value,
                        ItemType = item.Type,
                        Attack = item.Attack,
                        Armor = item.Armor,
                        Strength = item.Strength,
                        Defence = item.Defence,
                        IsCursed = item.IsCursed
                    }).ToList() ?? new List<MarketItemData>(),

                    // Modern RPG Equipment System - save equipped items
                    EquippedItems = npc.EquippedItems?.ToDictionary(
                        kvp => (int)kvp.Key,
                        kvp => kvp.Value
                    ) ?? new Dictionary<int, int>(),

                    // Save dynamic equipment that this NPC has equipped
                    // DynamicEquipmentStart = 100000; base equipment (Waist=10000, Face=11000, etc.) must NOT be saved as dynamic
                    DynamicEquipment = npc.EquippedItems?
                        .Where(kvp => kvp.Value >= 100000) // Dynamic equipment IDs start at 100000
                        .Select(kvp => EquipmentDatabase.GetById(kvp.Value))
                        .Where(equip => equip != null)
                        .Select(equip => new DynamicEquipmentData
                        {
                            Id = equip!.Id,
                            Name = equip.Name,
                            Description = equip.Description ?? "",
                            Slot = (int)equip.Slot,
                            WeaponPower = equip.WeaponPower,
                            ArmorClass = equip.ArmorClass,
                            ShieldBonus = equip.ShieldBonus,
                            BlockChance = equip.BlockChance,
                            StrengthBonus = equip.StrengthBonus,
                            DexterityBonus = equip.DexterityBonus,
                            ConstitutionBonus = equip.ConstitutionBonus,
                            IntelligenceBonus = equip.IntelligenceBonus,
                            WisdomBonus = equip.WisdomBonus,
                            CharismaBonus = equip.CharismaBonus,
                            MaxHPBonus = equip.MaxHPBonus,
                            MaxManaBonus = equip.MaxManaBonus,
                            DefenceBonus = equip.DefenceBonus,
                            MinLevel = equip.MinLevel,
                            Value = equip.Value,
                            IsCursed = equip.IsCursed,
                            Rarity = (int)equip.Rarity,
                            WeaponType = (int)equip.WeaponType,
                            Handedness = (int)equip.Handedness,
                            ArmorType = (int)equip.ArmorType,
                            StaminaBonus = equip.StaminaBonus,
                            AgilityBonus = equip.AgilityBonus,
                            CriticalChanceBonus = equip.CriticalChanceBonus,
                            CriticalDamageBonus = equip.CriticalDamageBonus,
                            MagicResistance = equip.MagicResistance,
                            PoisonDamage = equip.PoisonDamage,
                            LifeSteal = equip.LifeSteal,
                            HasFireEnchant = equip.HasFireEnchant,
                            HasFrostEnchant = equip.HasFrostEnchant,
                            HasLightningEnchant = equip.HasLightningEnchant,
                            HasPoisonEnchant = equip.HasPoisonEnchant,
                            HasHolyEnchant = equip.HasHolyEnchant,
                            HasShadowEnchant = equip.HasShadowEnchant,
                            ManaSteal = equip.ManaSteal,
                            ArmorPiercing = equip.ArmorPiercing,
                            Thorns = equip.Thorns,
                            HPRegen = equip.HPRegen,
                            ManaRegen = equip.ManaRegen
                        }).ToList() ?? new List<DynamicEquipmentData>()
                });
            }

            await Task.CompletedTask;
            return npcData;
        }
        
        /// <summary>
        /// Helper method to get NPCs from the world
        /// </summary>
        private List<NPC> GetWorldNPCs()
        {
            // Get all active NPCs from NPCSpawnSystem
            return NPCSpawnSystem.Instance?.ActiveNPCs ?? new List<NPC>();
        }
        
        private WorldStateData SerializeWorldState()
        {
            return new WorldStateData
            {
                // Economic state
                BankInterestRate = GameConfig.DefaultBankInterest,
                TownPotValue = GameConfig.DefaultTownPot,

                // Political state
                CurrentRuler = GameEngine.Instance?.CurrentPlayer?.King == true ?
                              GameEngine.Instance.CurrentPlayer.Name2 : null,

                // World events
                ActiveEvents = SerializeActiveEvents(),

                // Active quests (only unclaimed board quests, not per-player claimed quests)
                ActiveQuests = SerializeWorldQuests(),

                // Shop inventories
                ShopInventories = SerializeShopInventories(),

                // News and history
                RecentNews = SerializeRecentNews(),

                // God system state
                GodStates = SerializeGodStates(),

                // Marketplace listings
                MarketplaceListings = MarketplaceSystem.Instance.ToSaveData()
            };
        }
        
        private DailySettings SerializeDailySettings()
        {
            return new DailySettings
            {
                Mode = DailySystemManager.Instance.CurrentMode,
                LastResetTime = DailySystemManager.Instance.LastResetTime,
                AutoSaveEnabled = true,
                AutoSaveInterval = TimeSpan.FromMinutes(5)
            };
        }
        
        // Helper methods for serialization
        private Dictionary<string, float> SerializeRelationships(Character player)
        {
            // This would integrate with the relationship system
            return new Dictionary<string, float>();
        }
        
        /// <summary>
        /// Serialize only quests belonging to a specific player (claimed by them).
        /// Used for PlayerData — each player only needs their own quests in their save.
        /// </summary>
        private List<QuestData> SerializePlayerQuests(Character? player)
        {
            if (player == null) return new List<QuestData>();

            var playerName = player.Name2 ?? player.Name1 ?? "";
            var allQuests = QuestSystem.GetAllQuests(includeCompleted: false);

            // Only serialize quests claimed by or offered to this player
            var playerQuests = allQuests.Where(q =>
                (!string.IsNullOrEmpty(q.Occupier) && string.Equals(q.Occupier, playerName, StringComparison.OrdinalIgnoreCase)) ||
                (!string.IsNullOrEmpty(q.OfferedTo) && string.Equals(q.OfferedTo, playerName, StringComparison.OrdinalIgnoreCase))
            ).ToList();

            var result = SerializeQuestList(playerQuests);
            GD.Print($"[SaveSystem] Serialized {result.Count} quests for player {playerName} (from {allQuests.Count} total)");
            return result;
        }

        /// <summary>
        /// Serialize unclaimed/board quests for WorldStateData.
        /// In single-player mode, this preserves the quest board between sessions.
        /// In online mode, quests are managed by the shared database so this is minimal.
        /// </summary>
        private List<QuestData> SerializeWorldQuests()
        {
            var allQuests = QuestSystem.GetAllQuests(includeCompleted: false);

            // Only serialize unclaimed board quests (no occupier) — not per-player claimed quests
            var worldQuests = allQuests.Where(q => string.IsNullOrEmpty(q.Occupier)).ToList();

            var result = SerializeQuestList(worldQuests);
            GD.Print($"[SaveSystem] Serialized {result.Count} world quests (from {allQuests.Count} total)");
            return result;
        }

        private List<QuestData> SerializeQuestList(List<Quest> quests)
        {
            var questDataList = new List<QuestData>();

            foreach (var quest in quests)
            {
                var questData = new QuestData
                {
                    Id = quest.Id,
                    Title = quest.Title,
                    Initiator = quest.Initiator,
                    Comment = quest.Comment,
                    Status = quest.Deleted ? QuestStatus.Completed :
                             string.IsNullOrEmpty(quest.Occupier) ? QuestStatus.Active : QuestStatus.Active,
                    StartTime = quest.Date,
                    QuestType = (int)quest.QuestType,
                    QuestTarget = (int)quest.QuestTarget,
                    Difficulty = quest.Difficulty,
                    Occupier = quest.Occupier,
                    OccupiedDays = quest.OccupiedDays,
                    DaysToComplete = quest.DaysToComplete,
                    MinLevel = quest.MinLevel,
                    MaxLevel = quest.MaxLevel,
                    Reward = quest.Reward,
                    RewardType = (int)quest.RewardType,
                    Penalty = quest.Penalty,
                    PenaltyType = (int)quest.PenaltyType,
                    OfferedTo = quest.OfferedTo,
                    Forced = quest.Forced,
                    TargetNPCName = quest.TargetNPCName ?? "",
                    Objectives = new List<QuestObjectiveData>(),
                    Monsters = new List<QuestMonsterData>()
                };

                // Serialize objectives
                foreach (var objective in quest.Objectives)
                {
                    questData.Objectives.Add(new QuestObjectiveData
                    {
                        Id = objective.Id,
                        Description = objective.Description,
                        ObjectiveType = (int)objective.ObjectiveType,
                        TargetId = objective.TargetId,
                        TargetName = objective.TargetName,
                        RequiredProgress = objective.RequiredProgress,
                        CurrentProgress = objective.CurrentProgress,
                        IsOptional = objective.IsOptional,
                        BonusReward = objective.BonusReward
                    });
                }

                // Serialize monsters
                foreach (var monster in quest.Monsters)
                {
                    questData.Monsters.Add(new QuestMonsterData
                    {
                        MonsterType = monster.MonsterType,
                        Count = monster.Count,
                        MonsterName = monster.MonsterName
                    });
                }

                questDataList.Add(questData);
            }

            return questDataList;
        }
        
        private PersonalityData? SerializePersonality(PersonalityProfile? profile)
        {
            if (profile == null) return null;

            return new PersonalityData
            {
                // Core traits
                Aggression = profile.Aggression,
                Loyalty = profile.Loyalty,
                Intelligence = profile.Intelligence,
                Greed = profile.Greed,
                Compassion = profile.Sociability, // Use Sociability as Compassion
                Courage = profile.Courage,
                Honesty = profile.Trustworthiness, // Use Trustworthiness as Honesty
                Ambition = profile.Ambition,
                Vengefulness = profile.Vengefulness,
                Impulsiveness = profile.Impulsiveness,
                Caution = profile.Caution,
                Mysticism = profile.Mysticism,
                Patience = profile.Patience,

                // Romance/Intimacy traits
                Gender = profile.Gender,
                Orientation = profile.Orientation,
                IntimateStyle = profile.IntimateStyle,
                RelationshipPref = profile.RelationshipPref,
                Romanticism = profile.Romanticism,
                Sensuality = profile.Sensuality,
                Jealousy = profile.Jealousy,
                Commitment = profile.Commitment,
                Adventurousness = profile.Adventurousness,
                Exhibitionism = profile.Exhibitionism,
                Voyeurism = profile.Voyeurism,
                Flirtatiousness = profile.Flirtatiousness,
                Passion = profile.Passion,
                Tenderness = profile.Tenderness
            };
        }
        
        private List<MemoryData> SerializeMemories(MemorySystem? memory)
        {
            if (memory == null) return new List<MemoryData>();

            return memory.AllMemories.Select(m => new MemoryData
            {
                Type = m.Type.ToString(),
                Description = m.Description,
                InvolvedCharacter = m.InvolvedCharacter ?? "",
                Importance = m.Importance,
                EmotionalImpact = m.EmotionalImpact,
                Timestamp = m.Timestamp
            }).ToList();
        }

        private List<GoalData> SerializeGoals(GoalSystem? goals)
        {
            if (goals == null) return new List<GoalData>();

            return goals.AllGoals.Select(g => new GoalData
            {
                Name = g.Name,
                Type = g.Type.ToString(),
                Priority = g.Priority,
                Progress = g.Progress,
                IsActive = g.IsActive,
                TargetValue = g.TargetValue,
                CurrentValue = g.CurrentValue
            }).ToList();
        }
        
        private EmotionalStateData? SerializeEmotionalState(EmotionalState? state)
        {
            if (state == null) return null;

            return new EmotionalStateData
            {
                Happiness = state.GetEmotionIntensity(EmotionType.Joy),
                Anger = state.GetEmotionIntensity(EmotionType.Anger),
                Fear = state.GetEmotionIntensity(EmotionType.Fear),
                Trust = state.GetEmotionIntensity(EmotionType.Gratitude),
                Confidence = state.GetEmotionIntensity(EmotionType.Confidence),
                Sadness = state.GetEmotionIntensity(EmotionType.Sadness),
                Greed = state.GetEmotionIntensity(EmotionType.Greed),
                Loneliness = state.GetEmotionIntensity(EmotionType.Loneliness),
                Envy = state.GetEmotionIntensity(EmotionType.Envy),
                Pride = state.GetEmotionIntensity(EmotionType.Pride),
                Hope = state.GetEmotionIntensity(EmotionType.Hope),
                Peace = state.GetEmotionIntensity(EmotionType.Peace)
            };
        }
        
        private Dictionary<string, float> SerializeNPCRelationships(NPC npc)
        {
            // Scale from internal -1..1 to dashboard-expected -100..100
            return npc.Brain?.Memory?.CharacterImpressions?.ToDictionary(
                kvp => kvp.Key, kvp => kvp.Value * 100f) ?? new Dictionary<string, float>();
        }
        
        private List<WorldEventData> SerializeActiveEvents()
        {
            var eventDataList = new List<WorldEventData>();
            var activeEvents = WorldEventSystem.Instance.GetActiveEvents();

            foreach (var evt in activeEvents)
            {
                var eventData = new WorldEventData
                {
                    Id = Guid.NewGuid().ToString(),
                    Type = evt.Type.ToString(),
                    Title = evt.Title,
                    Description = evt.Description,
                    StartTime = DateTime.Now.AddDays(-evt.StartDay),
                    EndTime = DateTime.Now.AddDays(evt.DaysRemaining),
                    Parameters = new Dictionary<string, object>
                    {
                        ["DaysRemaining"] = evt.DaysRemaining,
                        ["StartDay"] = evt.StartDay
                    }
                };

                // Add effect parameters
                foreach (var effect in evt.Effects)
                {
                    eventData.Parameters[$"Effect_{effect.Key}"] = effect.Value;
                }

                eventDataList.Add(eventData);
            }

            // Also save global modifier state
            if (eventDataList.Count > 0 || WorldEventSystem.Instance.PlaguActive ||
                WorldEventSystem.Instance.WarActive || WorldEventSystem.Instance.FestivalActive)
            {
                var stateData = new WorldEventData
                {
                    Id = "GLOBAL_STATE",
                    Type = "GlobalState",
                    Title = "World State",
                    Description = WorldEventSystem.Instance.CurrentKingDecree,
                    Parameters = new Dictionary<string, object>
                    {
                        ["PlaguActive"] = WorldEventSystem.Instance.PlaguActive,
                        ["WarActive"] = WorldEventSystem.Instance.WarActive,
                        ["FestivalActive"] = WorldEventSystem.Instance.FestivalActive,
                        ["GlobalPriceModifier"] = WorldEventSystem.Instance.GlobalPriceModifier,
                        ["GlobalXPModifier"] = WorldEventSystem.Instance.GlobalXPModifier,
                        ["GlobalGoldModifier"] = WorldEventSystem.Instance.GlobalGoldModifier,
                        ["GlobalStatModifier"] = WorldEventSystem.Instance.GlobalStatModifier
                    }
                };
                eventDataList.Add(stateData);
            }

            return eventDataList;
        }
        
        private Dictionary<string, ShopInventoryData> SerializeShopInventories()
        {
            // This would serialize shop inventories
            return new Dictionary<string, ShopInventoryData>();
        }
        
        private List<NewsEntryData> SerializeRecentNews()
        {
            // This would serialize recent news
            return new List<NewsEntryData>();
        }
        
        private Dictionary<string, GodStateData> SerializeGodStates()
        {
            // This would serialize god states
            return new Dictionary<string, GodStateData>();
        }
        
        /// <summary>
        /// Serialize all story systems state
        /// Note: Uses reflection to safely access properties that may or may not exist
        /// </summary>
        private StorySystemsData SerializeStorySystems()
        {
            var data = new StorySystemsData();

            // Ocean Philosophy - save awakening level and collected fragments
            try
            {
                var ocean = OceanPhilosophySystem.Instance;
                data.AwakeningLevel = ocean.AwakeningLevel;
                data.CollectedFragments = ocean.CollectedFragments.Select(f => (int)f).ToList();
                data.ExperiencedMoments = ocean.ExperiencedMoments.Select(m => (int)m).ToList();
            }
            catch { /* System not initialized */ }

            // Grief System - save full grief state (multiple griefs, memories)
            try
            {
                var grief = GriefSystem.Instance;
                data.GriefStage = (int)grief.CurrentStage;  // Legacy field for backwards compatibility

                // Serialize full grief data
                var griefData = grief.Serialize();

                // Convert companion grief states
                data.ActiveGriefs = griefData.ActiveGrief?.Select(g => new GriefStateSaveData
                {
                    CompanionId = (int)g.CompanionId,
                    NpcId = g.NpcId,
                    CompanionName = g.CompanionName,
                    DeathType = (int)g.DeathType,
                    CurrentStage = (int)g.CurrentStage,
                    StageStartDay = g.StageStartDay,
                    GriefStartDay = g.GriefStartDay,
                    ResurrectionAttempts = g.ResurrectionAttempts,
                    IsComplete = g.IsComplete
                }).ToList() ?? new List<GriefStateSaveData>();

                // Convert memories
                data.GriefMemories = griefData.Memories?.Select(m => new GriefMemorySaveData
                {
                    CompanionId = (int)m.CompanionId,
                    NpcId = m.NpcId,
                    CompanionName = m.CompanionName,
                    MemoryText = m.MemoryText,
                    CreatedDay = m.CreatedDay
                }).ToList() ?? new List<GriefMemorySaveData>();

                if (data.ActiveGriefs.Count > 0 || data.GriefMemories.Count > 0)
                {
                    GD.Print($"[SaveSystem] Saving grief data: {data.ActiveGriefs.Count} active griefs, {data.GriefMemories.Count} memories");
                }
            }
            catch { /* System not initialized */ }

            // Story Progression - save cycle count, seals, story flags, and Old God states
            try
            {
                var story = StoryProgressionSystem.Instance;
                data.CurrentCycle = story.CurrentCycle;
                data.CollectedSeals = story.CollectedSeals.Select(s => (int)s).ToList();
                data.CollectedArtifacts = story.CollectedArtifacts.Select(a => (int)a).ToList();
                data.StoryFlags = story.ExportStringFlags();
                data.CompletedEndings = story.CompletedEndings.Select(e => (int)e).ToList();

                // Save Old God defeat states (critical for permanent boss defeats)
                data.OldGodStates = story.OldGodStates.ToDictionary(
                    kvp => (int)kvp.Key,
                    kvp => (int)kvp.Value.Status
                );
                if (data.OldGodStates.Count > 0)
                {
                    GD.Print($"[SaveSystem] Saving {data.OldGodStates.Count} Old God states");
                }
            }
            catch { /* System not initialized */ }

            // God System - save player worship data
            try
            {
                var godSystem = UsurperRemake.GodSystemSingleton.Instance;
                var godData = godSystem.ToDictionary();
                if (godData.ContainsKey("PlayerGods") && godData["PlayerGods"] is Dictionary<string, string> playerGodDict)
                {
                    data.PlayerGods = new Dictionary<string, string>(playerGodDict);
                }
            }
            catch { /* System not initialized */ }

            // Companion System - save companion states
            try
            {
                var companionData = CompanionSystem.Instance.Serialize();

                // Convert CompanionSaveData to CompanionSaveInfo for storage
                data.Companions = companionData.CompanionStates.Select(c => new CompanionSaveInfo
                {
                    Id = (int)c.Id,
                    IsRecruited = c.IsRecruited,
                    IsActive = c.IsActive,
                    IsDead = c.IsDead,
                    LoyaltyLevel = c.LoyaltyLevel,
                    TrustLevel = c.TrustLevel,
                    RomanceLevel = c.RomanceLevel,
                    PersonalQuestStarted = c.PersonalQuestStarted,
                    PersonalQuestCompleted = c.PersonalQuestCompleted,
                    RecruitedDay = c.RecruitedDay,
                    // Level and experience
                    Level = c.Level,
                    Experience = c.Experience,
                    // Base stats (preserves level-up gains)
                    BaseStatsHP = c.BaseStatsHP,
                    BaseStatsAttack = c.BaseStatsAttack,
                    BaseStatsDefense = c.BaseStatsDefense,
                    BaseStatsMagicPower = c.BaseStatsMagicPower,
                    BaseStatsSpeed = c.BaseStatsSpeed,
                    BaseStatsHealingPower = c.BaseStatsHealingPower,
                    EquippedItemsSave = c.EquippedItemsSave,
                    DisabledAbilities = c.DisabledAbilities
                }).ToList();

                data.ActiveCompanionIds = companionData.ActiveCompanions.Select(c => (int)c).ToList();
                GD.Print($"[SaveSystem] Saving {data.ActiveCompanionIds.Count} active companions: [{string.Join(", ", data.ActiveCompanionIds)}]");

                data.FallenCompanions = companionData.FallenCompanions.Select(d => new CompanionDeathInfo
                {
                    CompanionId = (int)d.CompanionId,
                    DeathType = (int)d.Type,
                    Circumstance = d.Circumstance,
                    LastWords = d.LastWords,
                    DeathDay = d.DeathDay
                }).ToList();
            }
            catch { /* Companion system not initialized */ }

            // Dungeon Party NPCs - save NPC teammates (spouses, team members, lovers)
            try
            {
                data.DungeonPartyNPCIds = GameEngine.Instance?.DungeonPartyNPCIds?.ToList() ?? new List<string>();
                data.DungeonPartyPlayerNames = GameEngine.Instance?.DungeonPartyPlayerNames?.ToList() ?? new List<string>();
                if (data.DungeonPartyNPCIds.Count > 0)
                {
                    GD.Print($"[SaveSystem] Saving {data.DungeonPartyNPCIds.Count} dungeon party NPCs: [{string.Join(", ", data.DungeonPartyNPCIds)}]");
                }
            }
            catch { /* GameEngine not initialized */ }

            // Family System - save children
            try
            {
                data.Children = FamilySystem.Instance.SerializeChildren();
                if (data.Children.Count > 0)
                {
                    GD.Print($"[SaveSystem] Saved {data.Children.Count} children");
                }
            }
            catch (Exception ex)
            {
                GD.PrintErr($"[SaveSystem] Failed to save children: {ex.Message}");
            }

            // Archetype Tracker - save Jungian archetype scores
            try
            {
                data.ArchetypeTracker = ArchetypeTracker.Instance.Serialize();
                GD.Print($"[SaveSystem] Saved archetype tracker data");
            }
            catch (Exception ex)
            {
                GD.PrintErr($"[SaveSystem] Failed to save archetype tracker: {ex.Message}");
            }

            // Royal Court Political Systems - save court members, heirs, spouse, plots
            try
            {
                var king = global::CastleLocation.GetCurrentKing();
                if (king != null && king.IsActive)
                {
                    data.RoyalCourt = new RoyalCourtSaveData
                    {
                        KingName = king.Name,
                        Treasury = king.Treasury,
                        TaxRate = king.TaxRate,
                        TotalReign = king.TotalReign,
                        KingTaxPercent = king.KingTaxPercent,
                        CityTaxPercent = king.CityTaxPercent,
                        CoronationDate = king.CoronationDate.ToString("o"),
                        TaxAlignment = (int)king.TaxAlignment,
                        MonarchHistory = global::CastleLocation.GetMonarchHistory()?.Select(m => new MonarchRecordSaveData
                        {
                            Name = m.Name,
                            Title = m.Title,
                            DaysReigned = m.DaysReigned,
                            CoronationDate = m.CoronationDate.ToString("o"),
                            EndReason = m.EndReason
                        }).ToList() ?? new List<MonarchRecordSaveData>(),

                        // Court members
                        CourtMembers = king.CourtMembers.Select(m => new CourtMemberSaveData
                        {
                            Name = m.Name,
                            Faction = (int)m.Faction,
                            Influence = m.Influence,
                            LoyaltyToKing = m.LoyaltyToKing,
                            Role = m.Role,
                            IsPlotting = m.IsPlotting
                        }).ToList(),

                        // Heirs
                        Heirs = king.Heirs.Select(h => new RoyalHeirSaveData
                        {
                            Name = h.Name,
                            Age = h.Age,
                            ClaimStrength = h.ClaimStrength,
                            ParentName = h.ParentName,
                            Sex = (int)h.Sex,
                            IsDesignated = h.IsDesignated
                        }).ToList(),

                        // Spouse
                        Spouse = king.Spouse != null ? new RoyalSpouseSaveData
                        {
                            Name = king.Spouse.Name,
                            Sex = (int)king.Spouse.Sex,
                            OriginalFaction = (int)king.Spouse.OriginalFaction,
                            Dowry = king.Spouse.Dowry,
                            Happiness = king.Spouse.Happiness
                        } : null,

                        // Active plots
                        ActivePlots = king.ActivePlots.Select(p => new CourtIntrigueSaveData
                        {
                            PlotType = p.PlotType,
                            Conspirators = p.Conspirators,
                            Target = p.Target,
                            Progress = p.Progress,
                            IsDiscovered = p.IsDiscovered
                        }).ToList(),

                        DesignatedHeir = king.DesignatedHeir ?? "",
                        KingAI = (int)king.AI,
                        KingSex = (int)king.Sex,

                        // Guards and monsters
                        Guards = king.Guards?.Select(g => new RoyalGuardSaveData
                        {
                            Name = g.Name,
                            AI = (int)g.AI,
                            Sex = (int)g.Sex,
                            DailySalary = g.DailySalary,
                            Loyalty = g.Loyalty,
                            IsActive = g.IsActive
                        }).ToList() ?? new List<RoyalGuardSaveData>(),

                        MonsterGuards = king.MonsterGuards?.Select(m => new MonsterGuardSaveData
                        {
                            Name = m.Name,
                            Level = m.Level,
                            HP = m.HP,
                            MaxHP = m.MaxHP,
                            Strength = m.Strength,
                            Defence = m.Defence,
                            WeapPow = m.WeapPow,
                            ArmPow = m.ArmPow,
                            MonsterType = m.MonsterType,
                            PurchaseCost = m.PurchaseCost,
                            DailyFeedingCost = m.DailyFeedingCost
                        }).ToList() ?? new List<MonsterGuardSaveData>(),

                        // Phase 2 — previously unserialized fields
                        Prisoners = king.Prisoners?.Select(kvp => new PrisonRecordSaveData
                        {
                            CharacterName = kvp.Value.CharacterName,
                            Crime = kvp.Value.Crime,
                            Sentence = kvp.Value.Sentence,
                            DaysServed = kvp.Value.DaysServed,
                            ImprisonmentDate = kvp.Value.ImprisonmentDate.ToString("o"),
                            BailAmount = kvp.Value.BailAmount
                        }).ToList() ?? new List<PrisonRecordSaveData>(),

                        Orphans = king.Orphans?.Select(o => new RoyalOrphanSaveData
                        {
                            Name = o.Name,
                            Age = o.Age,
                            Sex = (int)o.Sex,
                            ArrivalDate = o.ArrivalDate.ToString("o"),
                            BackgroundStory = o.BackgroundStory,
                            Happiness = o.Happiness,
                            MotherName = o.MotherName,
                            FatherName = o.FatherName,
                            MotherID = o.MotherID,
                            FatherID = o.FatherID,
                            Race = (int)o.Race,
                            BirthDate = o.BirthDate.ToString("o"),
                            Soul = o.Soul,
                            IsRealOrphan = o.IsRealOrphan
                        }).ToList() ?? new List<RoyalOrphanSaveData>(),

                        MagicBudget = king.MagicBudget,
                        EstablishmentStatus = king.EstablishmentStatus ?? new Dictionary<string, bool>(),
                        LastProclamation = king.LastProclamation ?? "",
                        LastProclamationDate = king.LastProclamationDate != DateTime.MinValue
                            ? king.LastProclamationDate.ToString("o") : ""
                    };
                    GD.Print($"[SaveSystem] Saved royal court: {king.CourtMembers.Count} members, {king.Heirs.Count} heirs, {king.Guards.Count} guards, {king.MonsterGuards.Count} monsters");
                }
            }
            catch (Exception ex)
            {
                GD.PrintErr($"[SaveSystem] Failed to save royal court: {ex.Message}");
            }

            // Relationship System - save all character relationships
            try
            {
                data.Relationships = RelationshipSystem.ExportAllRelationships();
                if (data.Relationships.Count > 0)
                {
                    GD.Print($"[SaveSystem] Saved {data.Relationships.Count} relationships");
                }
            }
            catch (Exception ex)
            {
                GD.PrintErr($"[SaveSystem] Failed to save relationships: {ex.Message}");
            }

            // ===== NEW NARRATIVE SYSTEMS =====

            // Stranger/Noctura Encounter System
            try
            {
                data.StrangerEncounters = StrangerEncounterSystem.Instance.Serialize();
                if (data.StrangerEncounters.EncountersHad > 0)
                {
                    GD.Print($"[SaveSystem] Saved {data.StrangerEncounters.EncountersHad} stranger encounters");
                }
            }
            catch (Exception ex)
            {
                GD.PrintErr($"[SaveSystem] Failed to save stranger encounters: {ex.Message}");
            }

            // Faction System
            try
            {
                data.Factions = FactionSystem.Instance.Serialize();
                if (data.Factions.PlayerFaction >= 0)
                {
                    GD.Print($"[SaveSystem] Saved faction data: faction {data.Factions.PlayerFaction}, rank {data.Factions.FactionRank}");
                }
            }
            catch (Exception ex)
            {
                GD.PrintErr($"[SaveSystem] Failed to save faction system: {ex.Message}");
            }

            // Town NPC Story System
            try
            {
                data.TownNPCStories = TownNPCStorySystem.Instance.Serialize();
                var activeStories = data.TownNPCStories.NPCStates.Count(s => s.CurrentStage > 0);
                if (activeStories > 0)
                {
                    GD.Print($"[SaveSystem] Saved {activeStories} active town NPC stories");
                }
            }
            catch (Exception ex)
            {
                GD.PrintErr($"[SaveSystem] Failed to save town NPC stories: {ex.Message}");
            }

            // Dream System
            try
            {
                data.Dreams = DreamSystem.Instance.Serialize();
                if (data.Dreams.ExperiencedDreams.Count > 0)
                {
                    GD.Print($"[SaveSystem] Saved {data.Dreams.ExperiencedDreams.Count} experienced dreams");
                }
            }
            catch (Exception ex)
            {
                GD.PrintErr($"[SaveSystem] Failed to save dream system: {ex.Message}");
            }

            // NPC Marriage Registry
            try
            {
                var marriages = NPCMarriageRegistry.Instance.GetAllMarriages();
                data.NPCMarriages = marriages.Select(m => new NPCMarriageSaveData
                {
                    Npc1Id = m.Npc1Id,
                    Npc2Id = m.Npc2Id
                }).ToList();

                var affairs = NPCMarriageRegistry.Instance.GetAllAffairs();
                data.Affairs = affairs.Select(a => new AffairSaveData
                {
                    MarriedNpcId = a.MarriedNpcId,
                    SeducerId = a.SeducerId,
                    AffairProgress = a.AffairProgress,
                    SecretMeetings = a.SecretMeetings,
                    SpouseSuspicion = a.SpouseSuspicion,
                    IsActive = a.IsActive,
                    LastInteraction = a.LastInteraction
                }).ToList();

                if (data.NPCMarriages.Count > 0 || data.Affairs.Count > 0)
                {
                    GD.Print($"[SaveSystem] Saved {data.NPCMarriages.Count} NPC marriages, {data.Affairs.Count} affairs");
                }
            }
            catch (Exception ex)
            {
                GD.PrintErr($"[SaveSystem] Failed to save NPC marriages/affairs: {ex.Message}");
            }

            // Cultural Meme System (Social Emergence)
            try
            {
                data.CulturalMemes = CulturalMemeSystem.Instance?.ExportSaveData();
                if (data.CulturalMemes != null)
                {
                    GD.Print($"[SaveSystem] Saved cultural meme system data");
                }
            }
            catch (Exception ex)
            {
                GD.PrintErr($"[SaveSystem] Failed to save cultural meme system: {ex.Message}");
            }

            return data;
        }

        /// <summary>
        /// Restore story systems state from save data
        /// Note: Restoration is best-effort - systems may not support all restore operations
        /// </summary>
        public void RestoreStorySystems(StorySystemsData? data)
        {
            if (data == null) return;

            // Ocean Philosophy - restore fragments one by one
            try
            {
                var ocean = OceanPhilosophySystem.Instance;
                foreach (var fragmentInt in data.CollectedFragments)
                {
                    var fragment = (WaveFragment)fragmentInt;
                    if (!ocean.CollectedFragments.Contains(fragment))
                    {
                        ocean.CollectFragment(fragment);
                    }
                }
                foreach (var momentInt in data.ExperiencedMoments)
                {
                    var moment = (AwakeningMoment)momentInt;
                    if (!ocean.ExperiencedMoments.Contains(moment))
                    {
                        ocean.ExperienceMoment(moment);
                    }
                }
            }
            catch { /* System not available */ }

            // Story Progression - restore seals, story flags, and Old God states
            try
            {
                var story = StoryProgressionSystem.Instance;

                // Restore collected seals
                foreach (var sealInt in data.CollectedSeals)
                {
                    var seal = (UsurperRemake.Systems.SealType)sealInt;
                    if (!story.CollectedSeals.Contains(seal))
                    {
                        story.CollectSeal(seal);
                    }
                }

                // Restore collected artifacts
                if (data.CollectedArtifacts != null)
                {
                    foreach (var artifactInt in data.CollectedArtifacts)
                    {
                        var artifact = (ArtifactType)artifactInt;
                        if (!story.CollectedArtifacts.Contains(artifact))
                        {
                            story.CollectArtifact(artifact);
                        }
                    }
                }

                // Restore story flags
                story.ImportStringFlags(data.StoryFlags);

                // Restore cycle count
                story.CurrentCycle = data.CurrentCycle;

                // Restore completed endings
                if (data.CompletedEndings != null)
                {
                    foreach (var e in data.CompletedEndings)
                        story.AddCompletedEnding((EndingType)e);
                }

                // Restore Old God defeat states (critical for permanent boss defeats)
                if (data.OldGodStates != null && data.OldGodStates.Count > 0)
                {
                    foreach (var kvp in data.OldGodStates)
                    {
                        var godType = (OldGodType)kvp.Key;
                        var godStatus = (GodStatus)kvp.Value;

                        // Only update if the saved state is "resolved" (defeated, saved, allied, etc.)
                        // This prevents overwriting the initial state with default values
                        if (story.OldGodStates.TryGetValue(godType, out var existingState))
                        {
                            existingState.Status = godStatus;
                            GD.Print($"[SaveSystem] Restored {godType} state: {godStatus}");
                        }
                    }
                    GD.Print($"[SaveSystem] Restored {data.OldGodStates.Count} Old God states");
                }

                // MIGRATION: Sync OldGodStates from story flags for saves created before OldGodStates was saved
                // This ensures backward compatibility with old saves
                MigrateOldGodStatesFromStoryFlags(story, data.StoryFlags);
            }
            catch { /* System not available */ }

            // God System - restore player worship data
            try
            {
                var godSystem = UsurperRemake.GodSystemSingleton.Instance;
                foreach (var kvp in data.PlayerGods)
                {
                    godSystem.SetPlayerGod(kvp.Key, kvp.Value);
                }
            }
            catch { /* System not available */ }

            // Companion System - restore companion states
            try
            {
                // Restore if we have companion states OR active companion IDs
                bool hasCompanionData = (data.Companions != null && data.Companions.Count > 0);
                bool hasActiveCompanions = (data.ActiveCompanionIds != null && data.ActiveCompanionIds.Count > 0);

                if (hasCompanionData || hasActiveCompanions)
                {
                    // Convert CompanionSaveInfo back to CompanionSystemData format
                    var companionSystemData = new CompanionSystemData
                    {
                        CompanionStates = data.Companions?.Select(c => new CompanionSaveData
                        {
                            Id = (CompanionId)c.Id,
                            IsRecruited = c.IsRecruited,
                            IsActive = c.IsActive,
                            IsDead = c.IsDead,
                            LoyaltyLevel = c.LoyaltyLevel,
                            TrustLevel = c.TrustLevel,
                            RomanceLevel = c.RomanceLevel,
                            PersonalQuestStarted = c.PersonalQuestStarted,
                            PersonalQuestCompleted = c.PersonalQuestCompleted,
                            RecruitedDay = c.RecruitedDay,
                            // Level and experience
                            Level = c.Level,
                            Experience = c.Experience,
                            // Base stats (preserves level-up gains)
                            BaseStatsHP = c.BaseStatsHP,
                            BaseStatsAttack = c.BaseStatsAttack,
                            BaseStatsDefense = c.BaseStatsDefense,
                            BaseStatsMagicPower = c.BaseStatsMagicPower,
                            BaseStatsSpeed = c.BaseStatsSpeed,
                            BaseStatsHealingPower = c.BaseStatsHealingPower,
                            EquippedItemsSave = c.EquippedItemsSave ?? new Dictionary<int, int>(),
                            DisabledAbilities = c.DisabledAbilities ?? new List<string>()
                        }).ToList() ?? new List<CompanionSaveData>(),

                        ActiveCompanions = data.ActiveCompanionIds?.Select(id => (CompanionId)id).ToList() ?? new List<CompanionId>(),

                        FallenCompanions = data.FallenCompanions?.Select(d => new CompanionDeath
                        {
                            CompanionId = (CompanionId)d.CompanionId,
                            Type = (DeathType)d.DeathType,
                            Circumstance = d.Circumstance,
                            LastWords = d.LastWords,
                            DeathDay = d.DeathDay
                        }).ToList() ?? new List<CompanionDeath>()
                    };

                    CompanionSystem.Instance.Deserialize(companionSystemData);
                    GD.Print($"[SaveSystem] Restored {companionSystemData.ActiveCompanions.Count} active companions");
                }
            }
            catch { /* Companion system not available */ }

            // Dungeon Party NPCs - restore NPC teammates
            try
            {
                if (data.DungeonPartyNPCIds != null && data.DungeonPartyNPCIds.Count > 0)
                {
                    GameEngine.Instance?.SetDungeonPartyNPCs(data.DungeonPartyNPCIds);
                    GD.Print($"[SaveSystem] Restored {data.DungeonPartyNPCIds.Count} dungeon party NPCs");
                }
                if (data.DungeonPartyPlayerNames != null && data.DungeonPartyPlayerNames.Count > 0)
                {
                    GameEngine.Instance?.SetDungeonPartyPlayers(data.DungeonPartyPlayerNames);
                    GD.Print($"[SaveSystem] Restored {data.DungeonPartyPlayerNames.Count} dungeon party player echoes");
                }
            }
            catch { /* GameEngine not available */ }

            // Family System - restore children
            try
            {
                if (data.Children != null && data.Children.Count > 0)
                {
                    FamilySystem.Instance.DeserializeChildren(data.Children);
                    GD.Print($"[SaveSystem] Restored {data.Children.Count} children from save");
                }
            }
            catch (Exception ex)
            {
                GD.PrintErr($"[SaveSystem] Failed to restore children: {ex.Message}");
            }

            // Archetype Tracker - restore Jungian archetype scores
            try
            {
                if (data.ArchetypeTracker != null)
                {
                    ArchetypeTracker.Instance.Deserialize(data.ArchetypeTracker);
                    GD.Print($"[SaveSystem] Restored archetype tracker data");
                }
            }
            catch (Exception ex)
            {
                GD.PrintErr($"[SaveSystem] Failed to restore archetype tracker: {ex.Message}");
            }

            // Royal Court Political Systems - restore court members, heirs, spouse, plots
            try
            {
                if (data.RoyalCourt != null)
                {
                    var king = global::CastleLocation.GetCurrentKing();
                    if (king != null)
                    {
                        // Restore treasury and tax settings (previously missing!)
                        king.Treasury = data.RoyalCourt.Treasury;
                        king.TaxRate = data.RoyalCourt.TaxRate;
                        king.TotalReign = data.RoyalCourt.TotalReign;
                        king.KingTaxPercent = data.RoyalCourt.KingTaxPercent > 0 ? data.RoyalCourt.KingTaxPercent : 5;
                        king.CityTaxPercent = data.RoyalCourt.CityTaxPercent > 0 ? data.RoyalCourt.CityTaxPercent : 2;

                        // Restore coronation date and tax alignment
                        if (!string.IsNullOrEmpty(data.RoyalCourt.CoronationDate))
                        {
                            if (DateTime.TryParse(data.RoyalCourt.CoronationDate, null, System.Globalization.DateTimeStyles.RoundtripKind, out var coronation))
                                king.CoronationDate = coronation;
                        }
                        king.TaxAlignment = (GameConfig.TaxAlignment)data.RoyalCourt.TaxAlignment;

                        // Restore king AI and Sex (SetCurrentKing hardcodes AI=Computer)
                        king.AI = (CharacterAI)data.RoyalCourt.KingAI;
                        king.Sex = (CharacterSex)data.RoyalCourt.KingSex;

                        // Restore monarch history
                        if (data.RoyalCourt.MonarchHistory != null && data.RoyalCourt.MonarchHistory.Count > 0)
                        {
                            var history = data.RoyalCourt.MonarchHistory.Select(m => new MonarchRecord
                            {
                                Name = m.Name,
                                Title = m.Title,
                                DaysReigned = m.DaysReigned,
                                CoronationDate = DateTime.TryParse(m.CoronationDate, null, System.Globalization.DateTimeStyles.RoundtripKind, out var cd) ? cd : DateTime.Now,
                                EndReason = m.EndReason
                            }).ToList();
                            global::CastleLocation.SetMonarchHistory(history);
                        }

                        // Restore court members
                        king.CourtMembers = data.RoyalCourt.CourtMembers?.Select(m => new CourtMember
                        {
                            Name = m.Name,
                            Faction = (CourtFaction)m.Faction,
                            Influence = m.Influence,
                            LoyaltyToKing = m.LoyaltyToKing,
                            Role = m.Role,
                            IsPlotting = m.IsPlotting
                        }).ToList() ?? new List<CourtMember>();

                        // Restore heirs
                        king.Heirs = data.RoyalCourt.Heirs?.Select(h => new RoyalHeir
                        {
                            Name = h.Name,
                            Age = h.Age,
                            ClaimStrength = h.ClaimStrength,
                            ParentName = h.ParentName,
                            Sex = (CharacterSex)h.Sex,
                            IsDesignated = h.IsDesignated
                        }).ToList() ?? new List<RoyalHeir>();

                        // Restore spouse
                        if (data.RoyalCourt.Spouse != null)
                        {
                            king.Spouse = new RoyalSpouse
                            {
                                Name = data.RoyalCourt.Spouse.Name,
                                Sex = (CharacterSex)data.RoyalCourt.Spouse.Sex,
                                OriginalFaction = (CourtFaction)data.RoyalCourt.Spouse.OriginalFaction,
                                Dowry = data.RoyalCourt.Spouse.Dowry,
                                Happiness = data.RoyalCourt.Spouse.Happiness
                            };
                        }
                        else
                        {
                            king.Spouse = null; // Ensure old spouse doesn't carry over
                        }

                        // Restore active plots
                        king.ActivePlots = data.RoyalCourt.ActivePlots?.Select(p => new CourtIntrigue
                        {
                            PlotType = p.PlotType,
                            Conspirators = p.Conspirators ?? new List<string>(),
                            Target = p.Target,
                            Progress = p.Progress,
                            IsDiscovered = p.IsDiscovered
                        }).ToList() ?? new List<CourtIntrigue>();

                        king.DesignatedHeir = data.RoyalCourt.DesignatedHeir;

                        // Restore guards
                        if (data.RoyalCourt.Guards != null && data.RoyalCourt.Guards.Count > 0)
                        {
                            king.Guards = data.RoyalCourt.Guards.Select(g => new RoyalGuard
                            {
                                Name = g.Name,
                                AI = (CharacterAI)g.AI,
                                Sex = (CharacterSex)g.Sex,
                                DailySalary = g.DailySalary,
                                Loyalty = g.Loyalty,
                                IsActive = g.IsActive
                            }).ToList();
                        }

                        // Restore monster guards
                        if (data.RoyalCourt.MonsterGuards != null && data.RoyalCourt.MonsterGuards.Count > 0)
                        {
                            king.MonsterGuards = data.RoyalCourt.MonsterGuards.Select(m => new MonsterGuard
                            {
                                Name = m.Name,
                                Level = m.Level,
                                HP = m.HP,
                                MaxHP = m.MaxHP,
                                Strength = m.Strength,
                                Defence = m.Defence,
                                WeapPow = m.WeapPow,
                                ArmPow = m.ArmPow,
                                MonsterType = m.MonsterType,
                                PurchaseCost = m.PurchaseCost,
                                DailyFeedingCost = m.DailyFeedingCost
                            }).ToList();
                        }

                        // Phase 2 — restore previously unserialized fields
                        if (data.RoyalCourt.Prisoners != null && data.RoyalCourt.Prisoners.Count > 0)
                        {
                            king.Prisoners = data.RoyalCourt.Prisoners.ToDictionary(
                                p => p.CharacterName,
                                p => new PrisonRecord
                                {
                                    CharacterName = p.CharacterName,
                                    Crime = p.Crime,
                                    Sentence = p.Sentence,
                                    DaysServed = p.DaysServed,
                                    ImprisonmentDate = DateTime.TryParse(p.ImprisonmentDate, out var impDate) ? impDate : DateTime.Now,
                                    BailAmount = p.BailAmount
                                });
                        }

                        if (data.RoyalCourt.Orphans != null && data.RoyalCourt.Orphans.Count > 0)
                        {
                            king.Orphans = data.RoyalCourt.Orphans.Select(o => new RoyalOrphan
                            {
                                Name = o.Name,
                                Age = o.Age,
                                Sex = (CharacterSex)o.Sex,
                                ArrivalDate = DateTime.TryParse(o.ArrivalDate, out var arrDate) ? arrDate : DateTime.Now,
                                BackgroundStory = o.BackgroundStory,
                                Happiness = o.Happiness,
                                MotherName = o.MotherName,
                                FatherName = o.FatherName,
                                MotherID = o.MotherID,
                                FatherID = o.FatherID,
                                Race = (CharacterRace)o.Race,
                                BirthDate = DateTime.TryParse(o.BirthDate, out var bd) ? bd : DateTime.Now,
                                Soul = o.Soul,
                                IsRealOrphan = o.IsRealOrphan
                            }).ToList();
                        }

                        king.MagicBudget = data.RoyalCourt.MagicBudget;

                        if (data.RoyalCourt.EstablishmentStatus != null && data.RoyalCourt.EstablishmentStatus.Count > 0)
                        {
                            king.EstablishmentStatus = new Dictionary<string, bool>(data.RoyalCourt.EstablishmentStatus);
                        }

                        if (!string.IsNullOrEmpty(data.RoyalCourt.LastProclamation))
                        {
                            king.LastProclamation = data.RoyalCourt.LastProclamation;
                        }

                        if (!string.IsNullOrEmpty(data.RoyalCourt.LastProclamationDate) &&
                            DateTime.TryParse(data.RoyalCourt.LastProclamationDate, out var procDate))
                        {
                            king.LastProclamationDate = procDate;
                        }

                        GD.Print($"[SaveSystem] Restored royal court: {king.CourtMembers.Count} members, {king.Heirs.Count} heirs, {king.Guards.Count} guards, {king.MonsterGuards.Count} monsters, {king.Prisoners.Count} prisoners, {king.Orphans.Count} orphans");
                    }
                }
            }
            catch (Exception ex)
            {
                GD.PrintErr($"[SaveSystem] Failed to restore royal court: {ex.Message}");
            }

            // Grief System - restore full grief states and memories
            try
            {
                bool hasGriefData = (data.ActiveGriefs != null && data.ActiveGriefs.Count > 0) ||
                                    (data.GriefMemories != null && data.GriefMemories.Count > 0);

                if (hasGriefData)
                {
                    // Convert save data back to GriefSystemData
                    var griefSystemData = new GriefSystemData
                    {
                        ActiveGrief = data.ActiveGriefs?.Select(g => new GriefState
                        {
                            CompanionId = (CompanionId)g.CompanionId,
                            NpcId = g.NpcId,
                            CompanionName = g.CompanionName,
                            DeathType = (DeathType)g.DeathType,
                            CurrentStage = (GriefStage)g.CurrentStage,
                            StageStartDay = g.StageStartDay,
                            GriefStartDay = g.GriefStartDay,
                            ResurrectionAttempts = g.ResurrectionAttempts,
                            IsComplete = g.IsComplete
                        }).ToList() ?? new List<GriefState>(),

                        Memories = data.GriefMemories?.Select(m => new CompanionMemory
                        {
                            CompanionId = (CompanionId)m.CompanionId,
                            NpcId = m.NpcId,
                            CompanionName = m.CompanionName,
                            MemoryText = m.MemoryText,
                            CreatedDay = m.CreatedDay
                        }).ToList() ?? new List<CompanionMemory>()
                    };

                    GriefSystem.Instance.Deserialize(griefSystemData);
                    GD.Print($"[SaveSystem] Restored grief data: {griefSystemData.ActiveGrief.Count} active griefs, {griefSystemData.Memories.Count} memories");
                }
            }
            catch (Exception ex)
            {
                GD.PrintErr($"[SaveSystem] Failed to restore grief system: {ex.Message}");
            }

            // Relationship System - restore all character relationships
            try
            {
                if (data.Relationships != null && data.Relationships.Count > 0)
                {
                    RelationshipSystem.ImportAllRelationships(data.Relationships);
                    GD.Print($"[SaveSystem] Restored {data.Relationships.Count} relationships");
                }
            }
            catch (Exception ex)
            {
                GD.PrintErr($"[SaveSystem] Failed to restore relationships: {ex.Message}");
            }

            // ===== NEW NARRATIVE SYSTEMS =====

            // Stranger/Noctura Encounter System
            try
            {
                if (data.StrangerEncounters != null)
                {
                    StrangerEncounterSystem.Instance.Deserialize(data.StrangerEncounters);
                    GD.Print($"[SaveSystem] Restored {data.StrangerEncounters.EncountersHad} stranger encounters");
                }
            }
            catch (Exception ex)
            {
                GD.PrintErr($"[SaveSystem] Failed to restore stranger encounters: {ex.Message}");
            }

            // Faction System
            try
            {
                if (data.Factions != null)
                {
                    FactionSystem.Instance.Deserialize(data.Factions);
                    if (data.Factions.PlayerFaction >= 0)
                    {
                        GD.Print($"[SaveSystem] Restored faction: {data.Factions.PlayerFaction}, rank {data.Factions.FactionRank}");
                    }
                }
            }
            catch (Exception ex)
            {
                GD.PrintErr($"[SaveSystem] Failed to restore faction system: {ex.Message}");
            }

            // Town NPC Story System
            try
            {
                if (data.TownNPCStories != null)
                {
                    TownNPCStorySystem.Instance.Deserialize(data.TownNPCStories);
                    var activeStories = data.TownNPCStories.NPCStates.Count(s => s.CurrentStage > 0);
                    GD.Print($"[SaveSystem] Restored {activeStories} active town NPC stories");
                }
            }
            catch (Exception ex)
            {
                GD.PrintErr($"[SaveSystem] Failed to restore town NPC stories: {ex.Message}");
            }

            // Dream System
            try
            {
                if (data.Dreams != null)
                {
                    DreamSystem.Instance.Deserialize(data.Dreams);
                    GD.Print($"[SaveSystem] Restored {data.Dreams.ExperiencedDreams.Count} experienced dreams");
                }
            }
            catch (Exception ex)
            {
                GD.PrintErr($"[SaveSystem] Failed to restore dream system: {ex.Message}");
            }

            // NPC Marriage Registry
            try
            {
                // Restore marriages
                if (data.NPCMarriages != null && data.NPCMarriages.Count > 0)
                {
                    var marriageData = data.NPCMarriages.Select(m => new NPCMarriageData
                    {
                        Npc1Id = m.Npc1Id,
                        Npc2Id = m.Npc2Id
                    }).ToList();
                    NPCMarriageRegistry.Instance.RestoreMarriages(marriageData);
                    GD.Print($"[SaveSystem] Restored {data.NPCMarriages.Count} NPC marriages");
                }

                // Restore affairs
                if (data.Affairs != null && data.Affairs.Count > 0)
                {
                    var affairData = data.Affairs.Select(a => new AffairState
                    {
                        MarriedNpcId = a.MarriedNpcId,
                        SeducerId = a.SeducerId,
                        AffairProgress = a.AffairProgress,
                        SecretMeetings = a.SecretMeetings,
                        SpouseSuspicion = a.SpouseSuspicion,
                        IsActive = a.IsActive,
                        LastInteraction = a.LastInteraction
                    }).ToList();
                    NPCMarriageRegistry.Instance.RestoreAffairs(affairData);
                    GD.Print($"[SaveSystem] Restored {data.Affairs.Count} affairs");
                }
            }
            catch (Exception ex)
            {
                GD.PrintErr($"[SaveSystem] Failed to restore NPC marriages/affairs: {ex.Message}");
            }

            // Cultural Meme System (Social Emergence)
            try
            {
                CulturalMemeSystem.Instance?.RestoreFromSaveData(data?.CulturalMemes);
                if (data?.CulturalMemes != null)
                {
                    GD.Print($"[SaveSystem] Restored cultural meme system data");
                }
            }
            catch (Exception ex)
            {
                GD.PrintErr($"[SaveSystem] Failed to restore cultural meme system: {ex.Message}");
            }
        }

        /// <summary>
        /// MIGRATION: Sync OldGodStates from story flags for saves created before OldGodStates was saved.
        /// This ensures backward compatibility - if story flags indicate a god was defeated but
        /// OldGodStates doesn't reflect that, we sync them up.
        /// </summary>
        private void MigrateOldGodStatesFromStoryFlags(StoryProgressionSystem story, Dictionary<string, bool> storyFlags)
        {
            if (storyFlags == null) return;

            var godMappings = new Dictionary<string, OldGodType>
            {
                { "maelketh", OldGodType.Maelketh },
                { "veloura", OldGodType.Veloura },
                { "thorgrim", OldGodType.Thorgrim },
                { "noctura", OldGodType.Noctura },
                { "aurelion", OldGodType.Aurelion },
                { "terravok", OldGodType.Terravok },
                { "manwe", OldGodType.Manwe }
            };

            int migrationCount = 0;

            foreach (var mapping in godMappings)
            {
                string godName = mapping.Key;
                OldGodType godType = mapping.Value;

                // Check for various story flags that indicate god status
                bool wasDestroyed = storyFlags.TryGetValue($"{godName}_destroyed", out bool destroyed) && destroyed;
                bool wasDefeated = storyFlags.TryGetValue($"{godName}_defeated", out bool defeated) && defeated;
                bool wasSaved = storyFlags.TryGetValue($"{godName}_saved", out bool saved) && saved;
                bool wasAllied = storyFlags.TryGetValue($"{godName}_ally", out bool allied) && allied;
                bool wasAwakened = storyFlags.TryGetValue($"{godName}_awakened", out bool awakened) && awakened;

                if (story.OldGodStates.TryGetValue(godType, out var godState))
                {
                    // Only migrate if the current state is NOT already resolved
                    bool isAlreadyResolved = godState.Status == GodStatus.Defeated ||
                                              godState.Status == GodStatus.Saved ||
                                              godState.Status == GodStatus.Allied ||
                                              godState.Status == GodStatus.Awakened ||
                                              godState.Status == GodStatus.Consumed;

                    if (!isAlreadyResolved)
                    {
                        // Apply migration based on story flags
                        if (wasDestroyed || wasDefeated)
                        {
                            godState.Status = GodStatus.Defeated;
                            GD.Print($"[SaveSystem] MIGRATION: Set {godType} to Defeated (from story flags)");
                            migrationCount++;
                        }
                        else if (wasSaved)
                        {
                            godState.Status = GodStatus.Saved;
                            GD.Print($"[SaveSystem] MIGRATION: Set {godType} to Saved (from story flags)");
                            migrationCount++;
                        }
                        else if (wasAllied)
                        {
                            godState.Status = GodStatus.Allied;
                            GD.Print($"[SaveSystem] MIGRATION: Set {godType} to Allied (from story flags)");
                            migrationCount++;
                        }
                        else if (wasAwakened)
                        {
                            godState.Status = GodStatus.Awakened;
                            GD.Print($"[SaveSystem] MIGRATION: Set {godType} to Awakened (from story flags)");
                            migrationCount++;
                        }
                    }
                }
            }

            if (migrationCount > 0)
            {
                GD.Print($"[SaveSystem] Migration complete: Updated {migrationCount} Old God states from story flags");
            }
        }
    }
} 