using System;
using System.Collections.Generic;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Godot;
using UsurperRemake.BBS;

namespace UsurperRemake.Systems
{
    /// <summary>
    /// Manages shared world state and online player tracking for multiplayer mode.
    /// In online mode, world state (NPCs, King, Economy, Events) is stored in the
    /// shared world_state table instead of being embedded in each player's save.
    /// Also handles heartbeat, online player tracking, and daily reset coordination.
    /// </summary>
    public class OnlineStateManager
    {
        private static OnlineStateManager? instance;
        public static OnlineStateManager? Instance => instance;

        private readonly IOnlineSaveBackend backend;
        private readonly string username;

        private readonly JsonSerializerOptions jsonOptions;

        private System.Threading.Timer? heartbeatTimer;
        private System.Threading.Timer? messageCheckTimer;
        private System.Threading.Timer? staleCleanupTimer;
        private string currentLocation = "MainStreet";
        private bool isDisposed = false;
        private int cachedOnlinePlayerCount = 1; // Default to 1 (self)
        private long lastSeenMessageId = 0; // Track last processed message to avoid re-fetching broadcasts

        // World state keys in the database
        public const string KEY_NPCS = "npcs";
        public const string KEY_KING = "king";
        public const string KEY_WORLD_EVENTS = "world_events";
        public const string KEY_QUESTS = "quests";
        public const string KEY_MARKETPLACE = "marketplace";
        public const string KEY_STORY_SYSTEMS = "story_systems";
        public const string KEY_DAILY_STATE = "daily_state";
        public const string KEY_MARRIAGES = "marriages";

        /// <summary>
        /// True if online state management is active.
        /// </summary>
        public static bool IsActive => instance != null;

        /// <summary>
        /// Initialize the online state manager. Call once at startup when --online is active.
        /// </summary>
        public static OnlineStateManager Initialize(IOnlineSaveBackend backend, string username)
        {
            instance = new OnlineStateManager(backend, username);
            return instance;
        }

        private OnlineStateManager(IOnlineSaveBackend backend, string username)
        {
            this.backend = backend;
            this.username = username;

            jsonOptions = new JsonSerializerOptions
            {
                WriteIndented = false,
                PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
                IncludeFields = true
            };
        }

        // =====================================================================
        // Shared World State - Save/Load
        // =====================================================================

        /// <summary>
        /// Save NPC data to shared world state.
        /// Called after NPC changes that should be visible to all players.
        /// </summary>
        public async Task SaveSharedNPCs(List<NPCData> npcData)
        {
            try
            {
                var json = JsonSerializer.Serialize(npcData, jsonOptions);
                await backend.SaveWorldState(KEY_NPCS, json);
                DebugLogger.Instance.LogDebug("ONLINE", $"Saved {npcData.Count} NPCs to shared state");
            }
            catch (Exception ex)
            {
                DebugLogger.Instance.LogError("ONLINE", $"Failed to save shared NPCs: {ex.Message}");
            }
        }

        /// <summary>
        /// Load NPC data from shared world state.
        /// Returns null if no shared NPCs exist yet (first player initializes them).
        /// </summary>
        public async Task<List<NPCData>?> LoadSharedNPCs()
        {
            try
            {
                var json = await backend.LoadWorldState(KEY_NPCS);
                if (string.IsNullOrEmpty(json))
                    return null;

                return JsonSerializer.Deserialize<List<NPCData>>(json, jsonOptions);
            }
            catch (Exception ex)
            {
                DebugLogger.Instance.LogError("ONLINE", $"Failed to load shared NPCs: {ex.Message}");
                return null;
            }
        }

        /// <summary>
        /// Save world events to shared state.
        /// </summary>
        public async Task SaveSharedWorldEvents(List<WorldEventData> events)
        {
            try
            {
                var json = JsonSerializer.Serialize(events, jsonOptions);
                await backend.SaveWorldState(KEY_WORLD_EVENTS, json);
            }
            catch (Exception ex)
            {
                DebugLogger.Instance.LogError("ONLINE", $"Failed to save world events: {ex.Message}");
            }
        }

        /// <summary>
        /// Load world events from shared state.
        /// </summary>
        public async Task<List<WorldEventData>?> LoadSharedWorldEvents()
        {
            try
            {
                var json = await backend.LoadWorldState(KEY_WORLD_EVENTS);
                if (string.IsNullOrEmpty(json))
                    return null;

                return JsonSerializer.Deserialize<List<WorldEventData>>(json, jsonOptions);
            }
            catch (Exception ex)
            {
                DebugLogger.Instance.LogError("ONLINE", $"Failed to load world events: {ex.Message}");
                return null;
            }
        }

        /// <summary>
        /// Save quest data to shared state.
        /// </summary>
        public async Task SaveSharedQuests(List<QuestData> quests)
        {
            try
            {
                var json = JsonSerializer.Serialize(quests, jsonOptions);
                await backend.SaveWorldState(KEY_QUESTS, json);
            }
            catch (Exception ex)
            {
                DebugLogger.Instance.LogError("ONLINE", $"Failed to save quests: {ex.Message}");
            }
        }

        /// <summary>
        /// Load quest data from shared state.
        /// </summary>
        public async Task<List<QuestData>?> LoadSharedQuests()
        {
            try
            {
                var json = await backend.LoadWorldState(KEY_QUESTS);
                if (string.IsNullOrEmpty(json))
                    return null;

                return JsonSerializer.Deserialize<List<QuestData>>(json, jsonOptions);
            }
            catch (Exception ex)
            {
                DebugLogger.Instance.LogError("ONLINE", $"Failed to load quests: {ex.Message}");
                return null;
            }
        }

        /// <summary>
        /// Save story systems to shared state (companions, seals, Old God states, etc.)
        /// </summary>
        public async Task SaveSharedStorySystems(StorySystemsData storyData)
        {
            try
            {
                var json = JsonSerializer.Serialize(storyData, jsonOptions);
                await backend.SaveWorldState(KEY_STORY_SYSTEMS, json);
            }
            catch (Exception ex)
            {
                DebugLogger.Instance.LogError("ONLINE", $"Failed to save story systems: {ex.Message}");
            }
        }

        /// <summary>
        /// Load story systems from shared state.
        /// </summary>
        public async Task<StorySystemsData?> LoadSharedStorySystems()
        {
            try
            {
                var json = await backend.LoadWorldState(KEY_STORY_SYSTEMS);
                if (string.IsNullOrEmpty(json))
                    return null;

                return JsonSerializer.Deserialize<StorySystemsData>(json, jsonOptions);
            }
            catch (Exception ex)
            {
                DebugLogger.Instance.LogError("ONLINE", $"Failed to load story systems: {ex.Message}");
                return null;
            }
        }

        /// <summary>
        /// Save all shared world state at once (called during game save).
        /// In online mode, this replaces the per-player world state serialization.
        /// </summary>
        public async Task SaveAllSharedState()
        {
            try
            {
                // NPCs
                var npcData = SerializeCurrentNPCs();
                if (npcData.Count > 0)
                    await SaveSharedNPCs(npcData);

                // World events
                var events = SerializeCurrentWorldEvents();
                await SaveSharedWorldEvents(events);

                // Quests
                var quests = SerializeCurrentQuests();
                await SaveSharedQuests(quests);

                // Story systems
                var story = SaveSystem.Instance.SerializeStorySystemsPublic();
                await SaveSharedStorySystems(story);

                DebugLogger.Instance.LogDebug("ONLINE", "All shared state saved");
            }
            catch (Exception ex)
            {
                DebugLogger.Instance.LogError("ONLINE", $"Failed to save all shared state: {ex.Message}");
            }
        }

        /// <summary>
        /// Load all shared world state (called during game load).
        /// Returns true if shared state was found and loaded.
        /// </summary>
        public async Task<bool> LoadAllSharedState()
        {
            try
            {
                bool foundState = false;

                // NPCs
                var npcs = await LoadSharedNPCs();
                if (npcs != null && npcs.Count > 0)
                {
                    DebugLogger.Instance.LogDebug("ONLINE", $"Loaded {npcs.Count} shared NPCs");
                    foundState = true;
                }

                // World events
                var events = await LoadSharedWorldEvents();
                if (events != null)
                {
                    DebugLogger.Instance.LogDebug("ONLINE", $"Loaded {events.Count} shared world events");
                    foundState = true;
                }

                // Quests
                var quests = await LoadSharedQuests();
                if (quests != null)
                {
                    DebugLogger.Instance.LogDebug("ONLINE", $"Loaded {quests.Count} shared quests");
                    foundState = true;
                }

                // Story systems
                var story = await LoadSharedStorySystems();
                if (story != null)
                {
                    DebugLogger.Instance.LogDebug("ONLINE", "Loaded shared story systems");
                    foundState = true;
                }

                return foundState;
            }
            catch (Exception ex)
            {
                DebugLogger.Instance.LogError("ONLINE", $"Failed to load shared state: {ex.Message}");
                return false;
            }
        }

        // =====================================================================
        // Daily Reset Coordination
        // =====================================================================

        /// <summary>
        /// Try to acquire the daily reset lock and process daily events.
        /// Only one player processes daily events; others wait briefly.
        /// Uses atomic update to prevent double-processing.
        /// </summary>
        public async Task<bool> TryProcessDailyReset(int currentDay)
        {
            try
            {
                bool acquired = await backend.TryAtomicUpdate(KEY_DAILY_STATE, currentJson =>
                {
                    if (string.IsNullOrEmpty(currentJson))
                        return JsonSerializer.Serialize(new DailyStateData { LastResetDay = currentDay, ProcessedBy = username });

                    var state = JsonSerializer.Deserialize<DailyStateData>(currentJson, jsonOptions);
                    if (state != null && state.LastResetDay >= currentDay)
                        return currentJson; // Already processed - return unchanged to signal "no update needed"

                    return JsonSerializer.Serialize(new DailyStateData { LastResetDay = currentDay, ProcessedBy = username });
                });

                if (acquired)
                {
                    // Check if we actually updated (compare days)
                    var stateJson = await backend.LoadWorldState(KEY_DAILY_STATE);
                    if (!string.IsNullOrEmpty(stateJson))
                    {
                        var state = JsonSerializer.Deserialize<DailyStateData>(stateJson, jsonOptions);
                        if (state?.ProcessedBy == username)
                        {
                            DebugLogger.Instance.LogInfo("ONLINE", $"Acquired daily reset lock for day {currentDay}");
                            return true;
                        }
                    }
                }

                DebugLogger.Instance.LogDebug("ONLINE", $"Daily reset for day {currentDay} already processed by another player");
                return false;
            }
            catch (Exception ex)
            {
                DebugLogger.Instance.LogError("ONLINE", $"Failed to coordinate daily reset: {ex.Message}");
                return false;
            }
        }

        // =====================================================================
        // Online Player Tracking (Phase 5)
        // =====================================================================

        /// <summary>
        /// Start online tracking: register as online, start heartbeat and message check timers.
        /// </summary>
        public async Task StartOnlineTracking(string displayName, string connectionType = "Unknown")
        {
            await backend.RegisterOnline(username, displayName, currentLocation, connectionType);
            await backend.UpdatePlayerSession(username, isLogin: true);

            // Initialize message watermark to current max ID so we don't replay old broadcasts
            try { lastSeenMessageId = await backend.GetMaxMessageId(); }
            catch { lastSeenMessageId = 0; }

            // Update cached player count on initial connect
            try { cachedOnlinePlayerCount = (await backend.GetOnlinePlayers()).Count; }
            catch { cachedOnlinePlayerCount = 1; }

            // Heartbeat every 30 seconds (also refreshes cached online player count)
            heartbeatTimer = new System.Threading.Timer(async _ =>
            {
                if (!isDisposed)
                {
                    try
                    {
                        await backend.UpdateHeartbeat(username, currentLocation);
                        cachedOnlinePlayerCount = (await backend.GetOnlinePlayers()).Count;
                    }
                    catch { /* Silently handle heartbeat failures */ }
                }
            }, null, TimeSpan.FromSeconds(30), TimeSpan.FromSeconds(30));

            // Check for messages every 5 seconds
            messageCheckTimer = new System.Threading.Timer(async _ =>
            {
                if (!isDisposed)
                {
                    try { await CheckForMessages(); }
                    catch { /* Silently handle message check failures */ }
                }
            }, null, TimeSpan.FromSeconds(5), TimeSpan.FromSeconds(5));

            // Cleanup stale online players every 60 seconds
            staleCleanupTimer = new System.Threading.Timer(async _ =>
            {
                if (!isDisposed)
                {
                    try { await backend.CleanupStaleOnlinePlayers(); }
                    catch { /* Silently handle cleanup failures */ }
                }
            }, null, TimeSpan.FromSeconds(60), TimeSpan.FromSeconds(60));

            DebugLogger.Instance.LogInfo("ONLINE", $"Online tracking started for '{displayName}'");
        }

        /// <summary>
        /// Update current location (called when player changes game location).
        /// </summary>
        public void UpdateLocation(string location)
        {
            currentLocation = location;
        }

        /// <summary>
        /// Get list of currently online players.
        /// </summary>
        public async Task<List<OnlinePlayerInfo>> GetOnlinePlayers()
        {
            return await backend.GetOnlinePlayers();
        }

        /// <summary>
        /// Get count of currently online players (async, queries DB).
        /// </summary>
        public async Task<int> GetOnlinePlayerCount()
        {
            var players = await backend.GetOnlinePlayers();
            return players.Count;
        }

        /// <summary>
        /// Get cached count of online players (synchronous, updated every 30s with heartbeat).
        /// Use this in non-async contexts like DisplayLocation().
        /// </summary>
        public int CachedOnlinePlayerCount => cachedOnlinePlayerCount;

        /// <summary>
        /// Get summary info for all players (for Hall of Fame leaderboard).
        /// </summary>
        public async Task<List<PlayerSummary>> GetAllPlayerSummaries()
        {
            return await backend.GetAllPlayerSummaries();
        }

        /// <summary>
        /// Send a message to another player.
        /// </summary>
        public async Task SendMessage(string toPlayer, string messageType, string message)
        {
            await backend.SendMessage(username, toPlayer, messageType, message);
        }

        /// <summary>
        /// Send a broadcast message to all players.
        /// </summary>
        public async Task BroadcastMessage(string messageType, string message)
        {
            await backend.SendMessage(username, "*", messageType, message);
        }

        /// <summary>
        /// Check for and process unread messages.
        /// </summary>
        private async Task CheckForMessages()
        {
            var messages = await backend.GetUnreadMessages(username, lastSeenMessageId);
            if (messages.Count > 0)
            {
                // Process each message based on type
                foreach (var msg in messages)
                {
                    ProcessIncomingMessage(msg);
                    if (msg.Id > lastSeenMessageId)
                        lastSeenMessageId = msg.Id;
                }
                // Mark direct messages as read (broadcast '*' messages are skipped by ID watermark)
                await backend.MarkMessagesRead(username);
            }
        }

        /// <summary>
        /// Process an incoming message based on its type.
        /// Forwards to OnlineChatSystem for display queuing.
        /// </summary>
        private void ProcessIncomingMessage(PlayerMessage msg)
        {
            // Forward to chat system for player-visible display
            if (OnlineChatSystem.IsActive)
            {
                OnlineChatSystem.Instance!.QueueIncomingMessage(msg.FromPlayer, msg.MessageType, msg.Message);
            }

            DebugLogger.Instance.LogDebug("ONLINE", $"Message ({msg.MessageType}) from {msg.FromPlayer}: {msg.Message}");
        }

        // =====================================================================
        // News
        // =====================================================================

        /// <summary>
        /// Add a news entry visible to all online players.
        /// </summary>
        public async Task AddNews(string message, string category)
        {
            await backend.AddNews(message, category, username);
        }

        /// <summary>
        /// Get recent news entries.
        /// </summary>
        public async Task<List<NewsEntry>> GetRecentNews(int count = 20)
        {
            return await backend.GetRecentNews(count);
        }

        // =====================================================================
        // Shutdown
        // =====================================================================

        /// <summary>
        /// Stop online tracking and clean up. Called on game exit.
        /// </summary>
        public async Task Shutdown()
        {
            isDisposed = true;

            heartbeatTimer?.Dispose();
            messageCheckTimer?.Dispose();
            staleCleanupTimer?.Dispose();

            try
            {
                await backend.UnregisterOnline(username);
                await backend.UpdatePlayerSession(username, isLogin: false);
                DebugLogger.Instance.LogInfo("ONLINE", "Online tracking stopped");
            }
            catch (Exception ex)
            {
                DebugLogger.Instance.LogError("ONLINE", $"Error during shutdown: {ex.Message}");
            }

            instance = null;
        }

        // =====================================================================
        // Serialization Helpers
        // =====================================================================

        public static List<NPCData> SerializeCurrentNPCs()
        {
            var npcData = new List<NPCData>();
            var worldNPCs = NPCSpawnSystem.Instance?.ActiveNPCs ?? new List<NPC>();
            var currentKing = global::CastleLocation.GetCurrentKing();

            foreach (var npc in worldNPCs)
            {
                npcData.Add(new NPCData
                {
                    Id = npc.Id ?? Guid.NewGuid().ToString(),
                    CharacterID = npc.ID ?? "",
                    Name = npc.Name2 ?? npc.Name1,
                    Archetype = npc.Archetype ?? "citizen",
                    Level = npc.Level,
                    HP = npc.HP,
                    MaxHP = npc.MaxHP,
                    BaseMaxHP = npc.BaseMaxHP > 0 ? npc.BaseMaxHP : npc.MaxHP,
                    BaseMaxMana = npc.BaseMaxMana > 0 ? npc.BaseMaxMana : npc.MaxMana,
                    Location = npc.CurrentLocation ?? npc.Location.ToString(),
                    Experience = npc.Experience,
                    Strength = npc.Strength,
                    Defence = npc.Defence,
                    Agility = npc.Agility,
                    Dexterity = npc.Dexterity,
                    Mana = npc.Mana,
                    MaxMana = npc.MaxMana,
                    WeapPow = npc.WeapPow,
                    ArmPow = npc.ArmPow,
                    BaseStrength = npc.BaseStrength > 0 ? npc.BaseStrength : npc.Strength,
                    BaseDefence = npc.BaseDefence > 0 ? npc.BaseDefence : npc.Defence,
                    BaseDexterity = npc.BaseDexterity > 0 ? npc.BaseDexterity : npc.Dexterity,
                    BaseAgility = npc.BaseAgility > 0 ? npc.BaseAgility : npc.Agility,
                    BaseStamina = npc.BaseStamina > 0 ? npc.BaseStamina : npc.Stamina,
                    BaseConstitution = npc.BaseConstitution > 0 ? npc.BaseConstitution : npc.Constitution,
                    BaseIntelligence = npc.BaseIntelligence > 0 ? npc.BaseIntelligence : npc.Intelligence,
                    BaseWisdom = npc.BaseWisdom > 0 ? npc.BaseWisdom : npc.Wisdom,
                    BaseCharisma = npc.BaseCharisma > 0 ? npc.BaseCharisma : npc.Charisma,
                    Class = npc.Class,
                    Race = npc.Race,
                    Sex = (char)npc.Sex,
                    Team = npc.Team ?? "",
                    IsTeamLeader = npc.CTurf,
                    IsKing = currentKing != null && currentKing.Name == npc.Name,
                    IsDead = npc.IsDead,
                    IsMarried = npc.IsMarried,
                    Married = npc.Married,
                    SpouseName = npc.SpouseName ?? "",
                    MarriedTimes = npc.MarriedTimes,
                    NPCFaction = npc.NPCFaction.HasValue ? (int)npc.NPCFaction.Value : -1,
                    Chivalry = npc.Chivalry,
                    Darkness = npc.Darkness,
                    Gold = npc.Gold,
                    Items = npc.Item?.ToArray() ?? new int[0],
                    EquippedItems = npc.EquippedItems?.ToDictionary(
                        kvp => (int)kvp.Key, kvp => kvp.Value) ?? new Dictionary<int, int>()
                });
            }

            return npcData;
        }

        private List<WorldEventData> SerializeCurrentWorldEvents()
        {
            var eventDataList = new List<WorldEventData>();
            var activeEvents = WorldEventSystem.Instance.GetActiveEvents();

            foreach (var evt in activeEvents)
            {
                eventDataList.Add(new WorldEventData
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
                });
            }

            return eventDataList;
        }

        private List<QuestData> SerializeCurrentQuests()
        {
            var questDataList = new List<QuestData>();
            var allQuests = QuestSystem.GetAllQuests(includeCompleted: false);

            foreach (var quest in allQuests)
            {
                var questData = new QuestData
                {
                    Id = quest.Id,
                    Title = quest.Title,
                    Initiator = quest.Initiator,
                    Comment = quest.Comment,
                    Status = quest.Deleted ? QuestStatus.Completed : QuestStatus.Active,
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
                    Objectives = new List<QuestObjectiveData>(),
                    Monsters = new List<QuestMonsterData>()
                };

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
    }

    /// <summary>
    /// Tracks daily reset coordination state in the world_state table.
    /// </summary>
    public class DailyStateData
    {
        public int LastResetDay { get; set; }
        public string ProcessedBy { get; set; } = "";
        public DateTime ProcessedAt { get; set; } = DateTime.UtcNow;
    }
}
