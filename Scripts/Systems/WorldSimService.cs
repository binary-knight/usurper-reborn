using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;

namespace UsurperRemake.Systems
{
    /// <summary>
    /// Headless 24/7 world simulator service.
    /// Runs NPC AI, dungeon exploration, leveling, shopping, social dynamics
    /// without any interactive terminal or player session.
    /// State is periodically persisted to the shared SQLite database.
    /// </summary>
    public class WorldSimService
    {
        private readonly SqlSaveBackend sqlBackend;
        private readonly int simIntervalSeconds;
        private readonly float npcXpMultiplier;
        private readonly int saveIntervalMinutes;

        private WorldSimulator? worldSimulator;
        private DateTime lastSaveTime = DateTime.MinValue;

        private readonly JsonSerializerOptions jsonOptions = new()
        {
            WriteIndented = false,
            PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
            IncludeFields = true
        };

        public WorldSimService(
            SqlSaveBackend backend,
            int simIntervalSeconds = 60,
            float npcXpMultiplier = 0.25f,
            int saveIntervalMinutes = 5)
        {
            this.sqlBackend = backend;
            this.simIntervalSeconds = simIntervalSeconds;
            this.npcXpMultiplier = npcXpMultiplier;
            this.saveIntervalMinutes = saveIntervalMinutes;
        }

        /// <summary>
        /// Run the world simulator in a loop until cancellation is requested.
        /// </summary>
        public async Task RunAsync(CancellationToken cancellationToken)
        {
            Console.Error.WriteLine($"[WORLDSIM] Starting persistent world simulator");
            Console.Error.WriteLine($"[WORLDSIM] Sim interval: {simIntervalSeconds}s");
            Console.Error.WriteLine($"[WORLDSIM] NPC XP multiplier: {npcXpMultiplier:F2}x");
            Console.Error.WriteLine($"[WORLDSIM] State save interval: {saveIntervalMinutes} minutes");

            // Phase 1: Initialize minimal systems
            InitializeSystems();

            // Phase 2: Load NPC state from database
            await LoadWorldState();

            // Phase 3: Set the NPC XP multiplier
            WorldSimulator.NpcXpMultiplier = npcXpMultiplier;

            // Phase 4: Run simulation loop
            lastSaveTime = DateTime.UtcNow;

            var aliveCount = NPCSpawnSystem.Instance.ActiveNPCs.Count(n => n.IsAlive && !n.IsDead);
            Console.Error.WriteLine($"[WORLDSIM] Simulation running. NPCs: {aliveCount} alive / {NPCSpawnSystem.Instance.ActiveNPCs.Count} total");

            try
            {
                int tickCount = 0;
                while (!cancellationToken.IsCancellationRequested)
                {
                    try
                    {
                        // Run one simulation tick
                        worldSimulator?.SimulateStep();
                        tickCount++;

                        // Log status every 10 ticks
                        if (tickCount % 10 == 0)
                        {
                            var alive = NPCSpawnSystem.Instance.ActiveNPCs.Count(n => n.IsAlive && !n.IsDead);
                            var dead = NPCSpawnSystem.Instance.ActiveNPCs.Count(n => !n.IsAlive || n.IsDead);
                            Console.Error.WriteLine($"[WORLDSIM] Tick {tickCount}: {alive} alive, {dead} dead NPCs");
                        }

                        // Check if it's time to persist state
                        if ((DateTime.UtcNow - lastSaveTime).TotalMinutes >= saveIntervalMinutes)
                        {
                            await SaveWorldState();
                            lastSaveTime = DateTime.UtcNow;
                        }
                    }
                    catch (Exception ex)
                    {
                        DebugLogger.Instance.LogError("WORLDSIM", $"Simulation step error: {ex.Message}\n{ex.StackTrace}");
                        Console.Error.WriteLine($"[WORLDSIM] Error in simulation step: {ex.Message}");
                    }

                    await Task.Delay(TimeSpan.FromSeconds(simIntervalSeconds), cancellationToken);
                }
            }
            catch (OperationCanceledException)
            {
                // Expected on shutdown
            }
            finally
            {
                // Graceful shutdown: save state one final time
                Console.Error.WriteLine("[WORLDSIM] Shutting down - saving final state...");
                await SaveWorldState();

                // Clear database callback
                NewsSystem.DatabaseCallback = null;

                Console.Error.WriteLine("[WORLDSIM] Final state saved. Goodbye.");
            }
        }

        /// <summary>
        /// Initialize only the systems needed for headless simulation.
        /// Skips: TerminalEmulator, LocationManager, UI systems, auth, player tracking.
        /// </summary>
        private void InitializeSystems()
        {
            Console.Error.WriteLine("[WORLDSIM] Initializing minimal systems...");

            // Initialize save system with SQL backend
            SaveSystem.InitializeWithBackend(sqlBackend);

            // Initialize static data systems
            EquipmentDatabase.Initialize();

            // Ensure NPC spawn system singleton exists
            _ = NPCSpawnSystem.Instance;

            // Ensure news system singleton exists (for NPC activity news)
            _ = NewsSystem.Instance;

            // Route NPC news to database for website activity feed ("The Living World")
            NewsSystem.DatabaseCallback = (message) =>
            {
                try
                {
                    _ = sqlBackend.AddNews(message, "npc", null);
                }
                catch { /* fail silently - don't crash sim for logging */ }
            };
            Console.Error.WriteLine("[WORLDSIM] NPC activity feed wired to database");

            // Create WorldSimulator but do NOT start its internal background loop.
            // We drive SimulateStep() directly in our controlled loop.
            worldSimulator = new WorldSimulator();
            worldSimulator.SetActive(true);

            Console.Error.WriteLine("[WORLDSIM] Minimal systems initialized");
            DebugLogger.Instance.LogInfo("WORLDSIM", "Minimal systems initialized for headless simulation");
        }

        /// <summary>
        /// Load NPC state from the shared world_state table.
        /// If no state exists, initialize fresh NPCs from templates.
        /// </summary>
        private async Task LoadWorldState()
        {
            try
            {
                var npcJson = await sqlBackend.LoadWorldState(OnlineStateManager.KEY_NPCS);
                if (!string.IsNullOrEmpty(npcJson))
                {
                    var npcData = JsonSerializer.Deserialize<List<NPCData>>(npcJson, jsonOptions);
                    if (npcData != null && npcData.Count > 0)
                    {
                        RestoreNPCsFromData(npcData);
                        Console.Error.WriteLine($"[WORLDSIM] Loaded {npcData.Count} NPCs from database");

                        // Process dead NPCs for respawn
                        worldSimulator?.ProcessDeadNPCsOnLoad();
                        return;
                    }
                }

                // No existing state -- initialize fresh NPCs from templates
                Console.Error.WriteLine("[WORLDSIM] No existing NPC state found. Initializing fresh NPCs...");
                await NPCSpawnSystem.Instance.InitializeClassicNPCs();
                Console.Error.WriteLine($"[WORLDSIM] Initialized {NPCSpawnSystem.Instance.ActiveNPCs.Count} fresh NPCs");
            }
            catch (Exception ex)
            {
                DebugLogger.Instance.LogError("WORLDSIM", $"Failed to load world state: {ex.Message}");
                Console.Error.WriteLine($"[WORLDSIM] Error loading state: {ex.Message}. Initializing fresh.");
                await NPCSpawnSystem.Instance.ForceReinitializeNPCs();
            }
        }

        /// <summary>
        /// Persist current NPC state to the shared database.
        /// Uses the same serialization as OnlineStateManager.
        /// </summary>
        private async Task SaveWorldState()
        {
            try
            {
                var npcData = OnlineStateManager.SerializeCurrentNPCs();
                var json = JsonSerializer.Serialize(npcData, jsonOptions);
                await sqlBackend.SaveWorldState(OnlineStateManager.KEY_NPCS, json);

                var aliveCount = NPCSpawnSystem.Instance.ActiveNPCs.Count(n => n.IsAlive && !n.IsDead);
                Console.Error.WriteLine($"[WORLDSIM] State saved: {aliveCount} alive NPCs at {DateTime.UtcNow:HH:mm:ss}");
                DebugLogger.Instance.LogInfo("WORLDSIM", $"State persisted: {npcData.Count} NPCs ({aliveCount} alive)");

                // Prune old NPC activity entries (keep last 24 hours)
                await sqlBackend.PruneOldNews("npc", 24);
            }
            catch (Exception ex)
            {
                DebugLogger.Instance.LogError("WORLDSIM", $"Failed to save world state: {ex.Message}");
                Console.Error.WriteLine($"[WORLDSIM] Error saving state: {ex.Message}");
            }
        }

        /// <summary>
        /// Restore NPCs from saved data. Follows the same pattern as GameEngine.RestoreNPCs()
        /// but without requiring a GameEngine instance.
        /// </summary>
        private void RestoreNPCsFromData(List<NPCData> npcData)
        {
            // Clear existing NPCs
            NPCSpawnSystem.Instance.ClearAllNPCs();

            NPC? kingNpc = null;

            foreach (var data in npcData)
            {
                var npc = new NPC
                {
                    Id = data.Id,
                    ID = !string.IsNullOrEmpty(data.CharacterID) ? data.CharacterID : $"npc_{data.Name.ToLower().Replace(" ", "_")}",
                    Name1 = data.Name,
                    Name2 = data.Name,
                    Level = data.Level,
                    HP = data.HP,
                    MaxHP = data.MaxHP,
                    BaseMaxHP = data.BaseMaxHP > 0 ? data.BaseMaxHP : data.MaxHP,
                    BaseMaxMana = data.BaseMaxMana > 0 ? data.BaseMaxMana : data.MaxMana,
                    CurrentLocation = data.Location,
                    Experience = data.Experience,
                    Strength = data.Strength,
                    Defence = data.Defence,
                    Agility = data.Agility,
                    Dexterity = data.Dexterity,
                    Mana = data.Mana,
                    MaxMana = data.MaxMana,
                    WeapPow = data.WeapPow,
                    ArmPow = data.ArmPow,
                    BaseStrength = data.BaseStrength > 0 ? data.BaseStrength : data.Strength,
                    BaseDefence = data.BaseDefence > 0 ? data.BaseDefence : data.Defence,
                    BaseDexterity = data.BaseDexterity > 0 ? data.BaseDexterity : data.Dexterity,
                    BaseAgility = data.BaseAgility > 0 ? data.BaseAgility : data.Agility,
                    BaseStamina = data.BaseStamina > 0 ? data.BaseStamina : 50,
                    BaseConstitution = data.BaseConstitution > 0 ? data.BaseConstitution : 10 + data.Level * 2,
                    BaseIntelligence = data.BaseIntelligence > 0 ? data.BaseIntelligence : 10,
                    BaseWisdom = data.BaseWisdom > 0 ? data.BaseWisdom : 10,
                    BaseCharisma = data.BaseCharisma > 0 ? data.BaseCharisma : 10,
                    Class = data.Class,
                    Race = data.Race,
                    Sex = (CharacterSex)data.Sex,
                    Team = data.Team,
                    CTurf = data.IsTeamLeader,
                    IsDead = data.IsDead,
                    IsMarried = data.IsMarried,
                    Married = data.Married,
                    SpouseName = data.SpouseName ?? "",
                    MarriedTimes = data.MarriedTimes,
                    NPCFaction = data.NPCFaction >= 0 ? (Faction)data.NPCFaction : null,
                    Chivalry = data.Chivalry,
                    Darkness = data.Darkness,
                    Gold = data.Gold,
                    AI = CharacterAI.Computer
                };

                // Restore items
                if (data.Items != null && data.Items.Length > 0)
                {
                    npc.Item = new List<int>(data.Items);
                }

                // Restore market inventory
                if (data.MarketInventory != null && data.MarketInventory.Count > 0)
                {
                    if (npc.MarketInventory == null)
                        npc.MarketInventory = new List<global::Item>();

                    foreach (var itemData in data.MarketInventory)
                    {
                        var item = new global::Item
                        {
                            Name = itemData.ItemName,
                            Value = itemData.ItemValue,
                            Type = itemData.ItemType,
                            Attack = itemData.Attack,
                            Armor = itemData.Armor,
                            Strength = itemData.Strength,
                            Defence = itemData.Defence,
                            IsCursed = itemData.IsCursed
                        };
                        npc.MarketInventory.Add(item);
                    }
                }

                // Restore personality if available
                if (data.PersonalityProfile != null)
                {
                    npc.Personality = new PersonalityProfile
                    {
                        Aggression = data.PersonalityProfile.Aggression,
                        Loyalty = data.PersonalityProfile.Loyalty,
                        Intelligence = data.PersonalityProfile.Intelligence,
                        Greed = data.PersonalityProfile.Greed,
                        Sociability = data.PersonalityProfile.Compassion,
                        Courage = data.PersonalityProfile.Courage,
                        Trustworthiness = data.PersonalityProfile.Honesty,
                        Ambition = data.PersonalityProfile.Ambition,
                        Vengefulness = data.PersonalityProfile.Vengefulness,
                        Impulsiveness = data.PersonalityProfile.Impulsiveness,
                        Caution = data.PersonalityProfile.Caution,
                        Mysticism = data.PersonalityProfile.Mysticism,
                        Patience = data.PersonalityProfile.Patience,
                        Archetype = data.Archetype ?? "Balanced",
                        Gender = data.PersonalityProfile.Gender,
                        Orientation = data.PersonalityProfile.Orientation,
                        IntimateStyle = data.PersonalityProfile.IntimateStyle,
                        RelationshipPref = data.PersonalityProfile.RelationshipPref,
                        Romanticism = data.PersonalityProfile.Romanticism,
                        Sensuality = data.PersonalityProfile.Sensuality,
                        Jealousy = data.PersonalityProfile.Jealousy,
                        Commitment = data.PersonalityProfile.Commitment,
                        Adventurousness = data.PersonalityProfile.Adventurousness,
                        Exhibitionism = data.PersonalityProfile.Exhibitionism,
                        Voyeurism = data.PersonalityProfile.Voyeurism,
                        Flirtatiousness = data.PersonalityProfile.Flirtatiousness,
                        Passion = data.PersonalityProfile.Passion,
                        Tenderness = data.PersonalityProfile.Tenderness
                    };
                }
                npc.Archetype = data.Archetype ?? "citizen";

                // Initialize AI systems (uses restored personality if available)
                npc.EnsureSystemsInitialized();

                // Restore memories, goals, emotional state
                if (npc.Brain != null)
                {
                    if (data.Memories != null)
                    {
                        foreach (var memData in data.Memories)
                        {
                            if (Enum.TryParse<MemoryType>(memData.Type, out var memType))
                            {
                                var memory = new MemoryEvent
                                {
                                    Type = memType,
                                    Description = memData.Description,
                                    InvolvedCharacter = memData.InvolvedCharacter,
                                    Timestamp = memData.Timestamp,
                                    Importance = memData.Importance,
                                    EmotionalImpact = memData.EmotionalImpact
                                };
                                npc.Brain.Memory?.RecordEvent(memory);
                            }
                        }
                    }

                    if (data.CurrentGoals != null)
                    {
                        foreach (var goalData in data.CurrentGoals)
                        {
                            if (Enum.TryParse<global::GoalType>(goalData.Type, out var goalType))
                            {
                                var goal = new global::Goal(goalData.Name, goalType, goalData.Priority)
                                {
                                    Progress = goalData.Progress,
                                    IsActive = goalData.IsActive,
                                    TargetValue = goalData.TargetValue,
                                    CurrentValue = goalData.CurrentValue,
                                    CreatedTime = goalData.CreatedTime
                                };
                                npc.Brain.Goals?.AddGoal(goal);
                            }
                        }
                    }

                    if (data.EmotionalState != null)
                    {
                        if (data.EmotionalState.Happiness > 0)
                            npc.Brain.Emotions?.AddEmotion(EmotionType.Joy, data.EmotionalState.Happiness, 120);
                        if (data.EmotionalState.Anger > 0)
                            npc.Brain.Emotions?.AddEmotion(EmotionType.Anger, data.EmotionalState.Anger, 120);
                        if (data.EmotionalState.Fear > 0)
                            npc.Brain.Emotions?.AddEmotion(EmotionType.Fear, data.EmotionalState.Fear, 120);
                        if (data.EmotionalState.Trust > 0)
                            npc.Brain.Emotions?.AddEmotion(EmotionType.Gratitude, data.EmotionalState.Trust, 120);
                    }
                }

                // Fix XP for legacy data
                if (npc.Experience <= 0 && npc.Level > 1)
                {
                    npc.Experience = GetExperienceForLevel(npc.Level);
                }

                // Fix base stats if not set
                if (npc.BaseMaxHP <= 0)
                {
                    npc.BaseMaxHP = npc.MaxHP;
                    npc.BaseStrength = npc.Strength;
                    npc.BaseDefence = npc.Defence;
                    npc.BaseDexterity = npc.Dexterity;
                    npc.BaseAgility = npc.Agility;
                    npc.BaseStamina = npc.Stamina;
                    npc.BaseConstitution = npc.Constitution;
                    npc.BaseIntelligence = npc.Intelligence;
                    npc.BaseWisdom = npc.Wisdom;
                    npc.BaseCharisma = npc.Charisma;
                    npc.BaseMaxMana = npc.MaxMana;
                }

                // Restore dynamic equipment
                if (data.DynamicEquipment != null && data.DynamicEquipment.Count > 0)
                {
                    foreach (var equipData in data.DynamicEquipment)
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
                            ArmorType = (ArmorType)equipData.ArmorType
                        };

                        int newId = EquipmentDatabase.RegisterDynamic(equipment);

                        // Update EquippedItems to use the new dynamic ID
                        if (data.EquippedItems != null)
                        {
                            foreach (var slot in data.EquippedItems.Keys.ToList())
                            {
                                if (data.EquippedItems[slot] == equipData.Id)
                                    data.EquippedItems[slot] = newId;
                            }
                        }
                    }
                }

                // Restore equipped items
                if (data.EquippedItems != null && data.EquippedItems.Count > 0)
                {
                    foreach (var kvp in data.EquippedItems)
                    {
                        npc.EquippedItems[(EquipmentSlot)kvp.Key] = kvp.Value;
                    }
                }

                // Recalculate stats with equipment bonuses
                npc.RecalculateStats();

                // Sanity check HP
                long minHP = 20 + (npc.Level * 10);
                if (npc.MaxHP < minHP)
                {
                    npc.BaseMaxHP = minHP;
                    npc.MaxHP = minHP;
                    if (npc.HP < 0 || npc.HP > npc.MaxHP)
                        npc.HP = npc.IsDead ? 0 : npc.MaxHP;
                }

                NPCSpawnSystem.Instance.AddRestoredNPC(npc);

                if (data.IsKing)
                    kingNpc = npc;
            }

            // Restore king
            if (kingNpc != null)
            {
                global::CastleLocation.SetCurrentKing(kingNpc);
                Console.Error.WriteLine($"[WORLDSIM] Restored king: {kingNpc.Name}");
            }

            NPCSpawnSystem.Instance.MarkAsInitialized();

            DebugLogger.Instance.LogInfo("WORLDSIM",
                $"Restored {npcData.Count} NPCs, {npcData.Count(n => n.IsDead)} dead");
        }

        /// <summary>
        /// Calculate experience needed for a given level.
        /// Same formula as WorldSimulator.GetExperienceForLevel.
        /// </summary>
        private static long GetExperienceForLevel(int level)
        {
            if (level <= 1) return 0;
            long exp = 0;
            for (int i = 2; i <= level; i++)
            {
                exp += (long)(Math.Pow(i, 1.8) * 50);
            }
            return exp;
        }
    }
}
