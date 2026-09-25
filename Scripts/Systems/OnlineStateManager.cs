using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using UsurperRemake.BBS;
using UsurperRemake.Data;
using UsurperRemake.Utils;
using UsurperRemake.Server;

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
        private static OnlineStateManager? _fallbackInstance;

        /// <summary>
        /// Returns the per-session OnlineStateManager when in MUD mode (via SessionContext),
        /// or the static fallback instance for SSH-per-process mode.
        /// </summary>
        public static OnlineStateManager? Instance =>
            UsurperRemake.Server.SessionContext.Current?.OnlineState ?? _fallbackInstance;

        private readonly IOnlineSaveBackend backend;
        private string username;

        private readonly JsonSerializerOptions jsonOptions;

        private System.Threading.Timer? heartbeatTimer;
        private System.Threading.Timer? messageCheckTimer;
        private System.Threading.Timer? staleCleanupTimer;
        private string currentLocation = "MainStreet";
        private bool isDisposed = false;
        private int cachedOnlinePlayerCount = 1; // Default to 1 (self)
        private long lastSeenMessageId = 0; // Track last processed message to avoid re-fetching broadcasts
        private string? cachedDisplayName; // Last display name registered, used for Discord logout message

        /// <summary>
        /// Connection type saved at auth time, used by GameEngine.LoadSaveByFileName()
        /// to fire StartOnlineTracking after character load (not at the main menu).
        /// </summary>
        public string DeferredConnectionType { get; set; } = "Unknown";

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
        /// True if online state management is active (checks per-session first, then fallback).
        /// </summary>
        public static bool IsActive => Instance != null;

        /// <summary>
        /// Initialize the online state manager. In MUD mode, the instance is stored on
        /// SessionContext for per-session isolation. In SSH-per-process mode, stored as
        /// a static fallback.
        /// </summary>
        public static OnlineStateManager Initialize(IOnlineSaveBackend backend, string username)
        {
            var osm = new OnlineStateManager(backend, username);

            // If running inside a MUD session, store on the session context
            var ctx = UsurperRemake.Server.SessionContext.Current;
            if (ctx != null)
                ctx.OnlineState = osm;
            else
                _fallbackInstance = osm; // SSH-per-process mode

            return osm;
        }

        private OnlineStateManager(IOnlineSaveBackend backend, string username)
        {
            this.backend = backend;
            this.username = username;

            jsonOptions = new JsonSerializerOptions
            {
                WriteIndented = false,
                PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
                IncludeFields = true,
                NumberHandling = System.Text.Json.Serialization.JsonNumberHandling.AllowReadingFromString,
                Converters = { new TolerantEnumReadOnlyConverterFactory() }
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
                // v1.1.13: a process that is not the owner writes only under the version it loaded
                if (backend is SqlSaveBackend sql && !WorldEditLog.IsOwnerProcess(sql))
                {
                    await SaveSharedNPCsVersionedAsync(sql, npcData);
                    return;
                }
                var json = JsonSerializer.Serialize(npcData, jsonOptions);
                await backend.SaveWorldState(KEY_NPCS, json);
                DebugLogger.Instance.LogDebug("ONLINE", $"Saved {npcData.Count} NPCs to shared state");
            }
            catch (Exception ex)
            {
                DebugLogger.Instance.LogError("ONLINE", $"Failed to save shared NPCs: {ex.Message}");
            }
        }

        // v1.1.13: the stored npcs version this session's roster was loaded from or last written as, and a
        // hash per NPC of the roster as loaded, so the NPCs this session changed are known on a conflict.
        private long? _npcsVersion;
        private Dictionary<string, string>? _npcBaseline;
        // v1.1.13: kept apart from the baseline. Seen: every NPC in a roster this session loaded from the store or
        // wrote (an NPC never seen there is one this session created). Removed: tombstones, NPCs a stored roster
        // had and a later one no longer has, fed by every reload; they are never appended again. PendingMine:
        // NPCs this session changed that a reload laid over the stored roster and no write has stored yet.
        private readonly HashSet<string> _npcSeen = new();
        private readonly HashSet<string> _npcRemoved = new();
        private readonly HashSet<string> _npcPendingMine = new();

        internal long? NpcsVersion => _npcsVersion;

        private static string NpcKey(NPCData d) => !string.IsNullOrEmpty(d.Id) ? d.Id : "name:" + d.Name;

        private Dictionary<string, string> HashRoster(IEnumerable<NPCData> roster)
        {
            var map = new Dictionary<string, string>();
            using var sha = System.Security.Cryptography.SHA256.Create();
            foreach (var d in roster)
            {
                // the emotional state drifts with time on every read, so it is no sign of a change
                var node = JsonSerializer.SerializeToNode(d, jsonOptions) as System.Text.Json.Nodes.JsonObject;
                node?.Remove("emotionalState");
                map[NpcKey(d)] = Convert.ToHexString(sha.ComputeHash(System.Text.Encoding.UTF8.GetBytes(node?.ToJsonString() ?? "")));
            }
            return map;
        }

        /// <summary>v1.1.13: call after RestoreNPCs of the loaded shared roster: the NPCs as this session has them now.</summary>
        public void NoteNpcBaseline()
        {
            _npcBaseline = HashRoster(SerializeCurrentNPCs());
            _npcSeen.Clear();
            _npcSeen.UnionWith(_npcBaseline.Keys);   // v1.1.13: the post-login roster
            _npcRemoved.Clear();
            _npcPendingMine.Clear();
        }

        /// <summary>
        /// v1.1.13: the ordinary save of a process that is not the owner (a door session). The whole roster is
        /// written only under the version this session loaded, after the world edits are applied to it. On a
        /// conflict the stored roster is loaded, with this session's own changes laid over it: each NPC whose
        /// data differs from the roster as this session loaded it replaces the stored copy of that NPC, and an
        /// NPC this session created is appended (v1.1.13); an NPC it loaded that the stored roster no longer
        /// has is not brought back. The changes are found before any world edit is applied (v1.1.13). The edits are applied again and the write
        /// retried under the new version. Another process's changes to the other NPCs are kept; to an NPC both
        /// changed, this session's copy wins, as every door write did before. Returns true once written.
        /// </summary>
        internal async Task<bool> SaveSharedNPCsVersionedAsync(SqlSaveBackend sql, List<NPCData> npcData,
            Func<List<NPCData>, Task>? reloadRoster = null, Func<Task>? beforeWrite = null)
        {
            long generation = RosterGeneration;
            // v1.1.13: this session's own changes are found before any shared clean-up edit is applied, so an
            // NPC only an edit touched is never taken for one this session changed
            var mineKeys = SessionChangedKeys(npcData);
            var edits = sql.GetWorldEditsToApply(WorldEditLog.ReapplyHours);
            if (edits.Count > 0 && WorldEditLog.Apply(sql, edits) > 0) npcData = SerializeCurrentNPCs();   // applied, never marked here
            long version = _npcsVersion ?? (sql.GetWorldStateVersion(KEY_NPCS) == 0 ? 0 : -1);
            for (int attempt = 0; attempt < 5; attempt++)
            {
                if (version >= 0)
                {
                    if (beforeWrite != null) await beforeWrite();
                    if (await sql.SaveWorldStateIfVersion(KEY_NPCS, JsonSerializer.Serialize(npcData, jsonOptions), version))
                    {
                        NoteRosterWritten(npcData, version + 1);
                        NoteLiveRosterWritten(generation, version + 1);
                        DebugLogger.Instance.LogDebug("ONLINE", $"Saved {npcData.Count} NPCs to shared state (v{version + 1})");
                        return true;
                    }
                }
                version = sql.GetWorldStateVersion(KEY_NPCS);
                var storedJson = await sql.LoadWorldState(KEY_NPCS);
                var stored = string.IsNullOrEmpty(storedJson) ? null : JsonSerializer.Deserialize<List<NPCData>>(storedJson, jsonOptions);
                if (stored == null || stored.Count == 0) { version = 0; continue; }

                var mine = npcData.Where(d => mineKeys.Contains(NpcKey(d))).GroupBy(NpcKey).ToDictionary(g => g.Key, g => g.First());
                int kept = OverlaySessionChanges(stored, mine);
                if (reloadRoster != null) await reloadRoster(stored);
                else await GameEngine.Instance.RestoreNPCs(stored, version);
                generation = RosterGeneration;
                WorldEditLog.Apply(sql, edits);
                npcData = SerializeCurrentNPCs();
                // the NPCs laid over stay this session's own changes if this write conflicts too
                _npcBaseline = HashRoster(npcData);
                _npcPendingMine.UnionWith(mine.Keys);   // v1.1.13: kept as changed, never taken for created
                DebugLogger.Instance.LogInfo("ONLINE",
                    $"NPC roster changed by another process since this session loaded it (now v{version}): reloaded it, kept {kept} NPC(s) this session changed, retrying.");
            }
            _npcsVersion = version;
            DebugLogger.Instance.LogWarning("ONLINE", "Shared NPC save gave up: the npcs record kept changing. The reloaded roster is kept; the next save tries again.");
            return false;
        }

        /// <summary>v1.1.13: the keys of the NPCs this session changed or created since its roster was loaded or written.</summary>
        internal HashSet<string> SessionChangedKeys(List<NPCData> roster)
        {
            var keys = new HashSet<string>();
            if (_npcBaseline == null) return keys;
            foreach (var (key, hash) in HashRoster(roster))
                if (!_npcBaseline.TryGetValue(key, out var h) || h != hash || _npcPendingMine.Contains(key)) keys.Add(key);
            return keys;
        }

        /// <summary>
        /// v1.1.13: lay this session's changed NPCs over a stored roster. A stored NPC is replaced by this
        /// session's copy. An NPC this session created (not in the roster it loaded) is appended. An NPC it
        /// loaded that the stored roster no longer has was removed by another writer and stays removed.
        /// Returns how many were kept.
        /// </summary>
        internal int OverlaySessionChanges(List<NPCData> stored, Dictionary<string, NPCData> mine)
        {
            int kept = 0;
            var storedKeys = new HashSet<string>();
            for (int i = 0; i < stored.Count; i++)
            {
                storedKeys.Add(NpcKey(stored[i]));
                if (mine.TryGetValue(NpcKey(stored[i]), out var own)) { stored[i] = own; kept++; }
            }
            // v1.1.13: an NPC a roster this session saw had and this stored roster lacks was removed: a tombstone
            foreach (var key in _npcSeen)
                if (!storedKeys.Contains(key)) _npcRemoved.Add(key);
            foreach (var (key, own) in mine)
                if (!storedKeys.Contains(key) && _npcBaseline != null && CreatedThisSession(key)) { stored.Add(own); kept++; }
            _npcSeen.UnionWith(storedKeys);
            return kept;
        }

        /// <summary>v1.1.13: an NPC this session made: never in a stored roster it saw, and not removed since.</summary>
        private bool CreatedThisSession(string key) => !_npcSeen.Contains(key) && !_npcRemoved.Contains(key);

        /// <summary>v1.1.13: the stored roster now holds this session's roster at this version.</summary>
        internal void NoteRosterWritten(List<NPCData> written, long version)
        {
            _npcsVersion = version;
            _npcBaseline = HashRoster(written);
            _npcSeen.UnionWith(_npcBaseline.Keys);   // v1.1.13: stored now
            _npcPendingMine.Clear();
        }

        /// <summary>v1.1.13: after a purge reloaded the stored roster (and laid this session's changes over it) without writing.</summary>
        internal void NoteRosterReloaded(List<NPCData> stored, long version, IEnumerable<string> mineKeys)
        {
            _npcsVersion = version;
            _npcBaseline = HashRoster(stored);
            _npcPendingMine.UnionWith(mineKeys);   // v1.1.13: kept as changed, never taken for created
        }

        // v1.1.13: the live NPC roster is process-wide. RosterLock is held by every rebuild of it (RestoreNPCs,
        // the world sim's reload) and by a purge from its clean-up to its serialize, so a purge never writes a
        // half-rebuilt roster. The version is the stored npcs version the live roster was restored from or last
        // written as (null: unknown), and the generation counts the rebuilds.
        internal static readonly object RosterLock = new();
        private static long _rosterGeneration;
        private static long? _liveRosterVersion;

        internal static long RosterGeneration { get { lock (RosterLock) return _rosterGeneration; } }
        internal static long? LiveRosterVersion { get { lock (RosterLock) return _liveRosterVersion; } }

        /// <summary>v1.1.13: a rebuild replaced the live roster with the stored one of this version (null: not a stored roster).</summary>
        internal static void NoteRosterRestored(long? storedVersion)
        {
            lock (RosterLock)
            {
                _rosterGeneration++;
                _liveRosterVersion = storedVersion;
            }
        }

        /// <summary>v1.1.13: the live roster serialized at this generation is stored at this version (ignored if rebuilt since).</summary>
        internal static void NoteLiveRosterWritten(long generation, long version)
        {
            lock (RosterLock)
                if (_rosterGeneration == generation) _liveRosterVersion = version;
        }

        /// <summary>v1.1.13: the live roster and the generation it was serialized at, taken under the roster lock.</summary>
        internal static (List<NPCData> Data, long Generation) SnapshotLiveRoster()
        {
            lock (RosterLock) return (SerializeCurrentNPCs(), _rosterGeneration);
        }

        /// <summary>
        /// v1.1.13: no rebuild is under way and the live roster is at least half the size of the stored one it
        /// would be written over (as NPCSpawnSystem.IsCountPlausible judges against its high-water mark; the
        /// stored size is used here since that mark has a floor of 25 NPCs). Call under RosterLock.
        /// </summary>
        private static bool LiveRosterIsWhole(int storedCount)
        {
            var spawner = NPCSpawnSystem.Instance;
            if (spawner == null || spawner.IsRebuilding) return false;
            return (spawner.ActiveNPCs?.Count ?? 0) * 2 >= storedCount;
        }

        /// <summary>
        /// Load NPC data from shared world state.
        /// Returns null if no shared NPCs exist yet (first player initializes them).
        /// </summary>
        public async Task<List<NPCData>?> LoadSharedNPCs()
        {
            try
            {
                long version = (backend as SqlSaveBackend)?.GetWorldStateVersion(KEY_NPCS) ?? 0;   // v1.1.13: read before the value
                var json = await backend.LoadWorldState(KEY_NPCS);
                _npcsVersion = version;
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
        /// Load children from world_state (written by WorldSimService).
        /// Returns raw ChildData list for FamilySystem.DeserializeChildren().
        /// Player sessions should call this to get authoritative children instead of stale save data.
        /// </summary>
        public async Task<List<ChildData>?> LoadSharedChildren()
        {
            try
            {
                var json = await backend.LoadWorldState("children");
                if (string.IsNullOrEmpty(json))
                    return null;

                using var doc = System.Text.Json.JsonDocument.Parse(json);
                var root = doc.RootElement;

                if (!root.TryGetProperty("childrenRaw", out var rawElement))
                    return null;

                return JsonSerializer.Deserialize<List<ChildData>>(rawElement.GetRawText(), jsonOptions);
            }
            catch (Exception ex)
            {
                DebugLogger.Instance.LogError("ONLINE", $"Failed to load shared children: {ex.Message}");
                return null;
            }
        }

        /// <summary>
        /// Save children directly to world_state from a player session.
        /// Normally WorldSimService is the only writer, but player sessions need to
        /// flush births and renames to world_state immediately so the change survives
        /// a server restart that happens before WorldSim's next tick. This writes the
        /// same wrapper schema WorldSimService uses (childrenRaw + dashboard summary).
        /// Fire-and-forget callers can ignore the returned task.
        /// </summary>
        public async Task SaveSharedChildrenNow()
        {
            try
            {
                var familySystem = FamilySystem.Instance;
                var allChildren = familySystem.AllChildren.Where(c => !c.Deleted).ToList();

                var childrenData = allChildren.Select(c => new Dictionary<string, object>
                {
                    ["name"] = c.Name,
                    ["age"] = c.Age,
                    ["sex"] = c.Sex == CharacterSex.Male ? "Male" : "Female",
                    ["mother"] = c.Mother,
                    ["father"] = c.Father,
                    ["soul"] = c.Soul,
                    ["soulDesc"] = c.GetSoulDescription(),
                    ["health"] = c.Health,
                    ["royal"] = c.Royal,
                    ["kidnapped"] = c.Kidnapped,
                    ["birthDate"] = c.BirthDate.ToString("o"),
                    // v0.63.0 slice 4 (audit npc-M9): cover all four ChildLocation values.
                    // Pre-fix Kidnapped (=3) and Away (=4) both rendered as "Unknown".
                    ["location"] = c.Location == GameConfig.ChildLocationHome ? "Home" :
                                   c.Location == GameConfig.ChildLocationOrphanage ? "Orphanage" :
                                   c.Location == GameConfig.ChildLocationKidnapped ? "Kidnapped" :
                                   c.Location == GameConfig.ChildLocationAway ? "Away" : "Unknown"
                }).ToList();

                var childrenRaw = familySystem.SerializeChildren();

                var wrapper = new Dictionary<string, object>
                {
                    ["count"] = childrenData.Count,
                    ["children"] = childrenData,
                    ["childrenRaw"] = childrenRaw,
                    ["updatedAt"] = DateTime.UtcNow.ToString("o")
                };

                var json = JsonSerializer.Serialize(wrapper, jsonOptions);
                await backend.SaveWorldState("children", json);
            }
            catch (Exception ex)
            {
                DebugLogger.Instance.LogError("ONLINE", $"Failed to save children to world_state: {ex.Message}");
            }
        }

        /// <summary>
        /// Load NPC marriage registry from world_state (written by WorldSimService).
        /// Returns marriage and affair data for NPCMarriageRegistry restoration.
        /// Player sessions should call this to get authoritative marriages instead of stale save data.
        /// </summary>
        public async Task<(List<NPCMarriageData>? marriages, List<AffairState>? affairs)> LoadSharedMarriages()
        {
            try
            {
                var json = await backend.LoadWorldState(KEY_MARRIAGES);
                if (string.IsNullOrEmpty(json))
                    return (null, null);

                using var doc = System.Text.Json.JsonDocument.Parse(json);
                var root = doc.RootElement;

                List<NPCMarriageData>? marriages = null;
                List<AffairState>? affairs = null;

                if (root.TryGetProperty("marriages", out var marriagesEl))
                    marriages = JsonSerializer.Deserialize<List<NPCMarriageData>>(marriagesEl.GetRawText(), jsonOptions);

                if (root.TryGetProperty("affairs", out var affairsEl))
                    affairs = JsonSerializer.Deserialize<List<AffairState>>(affairsEl.GetRawText(), jsonOptions);

                return (marriages, affairs);
            }
            catch (Exception ex)
            {
                DebugLogger.Instance.LogError("ONLINE", $"Failed to load shared marriages: {ex.Message}");
                return (null, null);
            }
        }

        /// <summary>
        /// v0.65.0: push the current shared quest list to world_state immediately,
        /// mirroring SaveSharedChildrenNow. Used by permadeath after purging a
        /// dead character's quests from questDatabase so the removal is durable
        /// in world_state["quests"] right away, rather than waiting for (and
        /// racing) the next world-sim save cycle.
        /// </summary>
        public async Task SaveSharedQuestsNow()
        {
            try { await SaveSharedQuests(SerializeCurrentQuests()); }
            catch (Exception ex)
            {
                DebugLogger.Instance.LogError("ONLINE", $"SaveSharedQuestsNow failed: {ex.Message}");
            }
        }

        /// <summary>
        /// v1.1.11: remove the matching quests from the shared record itself, leaving every other quest as
        /// stored. A delete in a fresh process has not loaded the shared quests, so pushing its own list
        /// (SaveSharedQuestsNow) would overwrite other players' quests (review). Returns the number removed.
        /// </summary>
        public Task<int> RemoveSharedQuestsAsync(Func<QuestData, bool> matches) => RemoveSharedQuestsAsync(matches, null);

        /// <summary>
        /// v1.1.11: the edit is written under the record's version, re-read and re-applied if another
        /// process wrote first, so a concurrent removal or addition is kept. beforeWrite is a test hook.
        /// </summary>
        internal async Task<int> RemoveSharedQuestsAsync(Func<QuestData, bool> matches, Func<Task>? beforeWrite)
        {
            try
            {
                if (backend is not SqlSaveBackend sql)
                {
                    var shared = await LoadSharedQuests();
                    if (shared == null) return 0;
                    int removed = shared.RemoveAll(q => matches(q));
                    if (removed > 0) await SaveSharedQuests(shared);
                    return removed;
                }
                for (int attempt = 0; attempt < 5; attempt++)
                {
                    long version = sql.GetWorldStateVersion(KEY_QUESTS);   // read before the value, so any later write is a conflict
                    var shared = await LoadSharedQuests();
                    if (shared == null) return 0;
                    int removed = shared.RemoveAll(q => matches(q));
                    if (removed == 0) return 0;
                    if (beforeWrite != null) await beforeWrite();
                    if (await sql.SaveWorldStateIfVersion(KEY_QUESTS, JsonSerializer.Serialize(shared, jsonOptions), version)) return removed;
                }
                DebugLogger.Instance.LogWarning("ONLINE", "RemoveSharedQuestsAsync gave up: the quest record kept changing.");
                return 0;
            }
            catch (Exception ex)
            {
                DebugLogger.Instance.LogError("ONLINE", $"RemoveSharedQuestsAsync failed: {ex.Message}");
                return 0;
            }
        }

        /// <summary>v1.1.13: the options SaveSharedNPCs writes with, for the static persist below.</summary>
        private static readonly JsonSerializerOptions PersistJsonOptions = new()
        {
            WriteIndented = false,
            PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
            IncludeFields = true,
            NumberHandling = System.Text.Json.Serialization.JsonNumberHandling.AllowReadingFromString,
            Converters = { new TolerantEnumReadOnlyConverterFactory() }
        };

        /// <summary>
        /// v1.1.13: apply cleanUp to the live NPC roster and write it to world_state at once, under the
        /// version this process's roster was loaded at or last written as, never unconditionally and never
        /// under a newer version read now. The clean-up and the serialize hold RosterLock, which every rebuild
        /// holds too, and run only on a whole roster (waiting up to rosterWaitMs; after that the world edit
        /// log carries the clean-up). If another writer got in first, the stored roster is loaded into the
        /// game with a door session's own changes laid over it, the clean-up re-applied to it and the write
        /// retried (as RemoveSharedQuestsAsync does). The marriages record then loses every
        /// marriage of endedMarriages (the NPC ids the clean-up divorced) the same way. The record is edited,
        /// not replaced by this process's registry, since only the world sim's process loads the registry.
        /// Returns what the first clean-up changed. beforeWrite is a test hook; onWritten runs once the stored
        /// roster holds the clean-up (v1.1.13: the owner marks its world edit applied there).
        /// </summary>
        public static async Task<int> PersistNpcWorldNow(SqlSaveBackend sql, Func<int> cleanUp, ISet<string> endedMarriages,
            Func<Task>? beforeWrite = null, Action? onWritten = null, int rosterWaitMs = 10000)
        {
            // v1.1.13: a door session's own unsaved NPC changes are laid over a reloaded roster, as its save does
            var session = WorldEditLog.IsOwnerProcess(sql) ? null : Instance;
            HashSet<string> mineKeys = new();
            List<NPCData> pre = new();
            int changed;
            List<NPCData> data;
            long generation;
            long version;
            // v1.1.13: the clean-up and the serialize run under the roster lock that every rebuild holds, and
            // only on a whole roster, so a login's rebuild on another task is never written half done
            int storedCount = sql.GetWorldStateArrayLength(KEY_NPCS);
            var waitUntil = DateTime.UtcNow.AddMilliseconds(rosterWaitMs);
            while (true)
            {
                int left = Math.Max(0, (int)(waitUntil - DateTime.UtcNow).TotalMilliseconds);
                if (!Monitor.TryEnter(RosterLock, left))
                {
                    DebugLogger.Instance.LogWarning("ONLINE", "PersistNpcWorldNow: the NPC roster stayed locked by a rebuild; not written now, the world edit log carries the clean-up.");
                    return 0;
                }
                try
                {
                    if (LiveRosterIsWhole(storedCount))
                    {
                        pre = SerializeCurrentNPCs();
                        mineKeys = session?.SessionChangedKeys(pre) ?? mineKeys;   // before the clean-up (it is not a session change)
                        changed = cleanUp();
                        if (changed == 0) return 0;
                        data = SerializeCurrentNPCs();
                        generation = _rosterGeneration;
                        // v1.1.13: written only over the version this roster was loaded at or last written as
                        version = _liveRosterVersion ?? -1;
                        break;
                    }
                }
                finally { Monitor.Exit(RosterLock); }
                if (DateTime.UtcNow >= waitUntil)
                {
                    DebugLogger.Instance.LogWarning("ONLINE", "PersistNpcWorldNow: the NPC roster is being rebuilt or is partial; not written now, the world edit log carries the clean-up.");
                    return 0;
                }
                await Task.Delay(50);
            }
            if (version < 0 && sql.GetWorldStateVersion(KEY_NPCS) == 0) version = 0;
            var mine = pre.Where(d => mineKeys.Contains(NpcKey(d))).GroupBy(NpcKey).ToDictionary(g => g.Key, g => g.First());
            try
            {
                bool saved = false;
                for (int attempt = 0; attempt < 5 && !saved; attempt++)
                {
                    if (version >= 0)
                    {
                        if (beforeWrite != null) await beforeWrite();
                        if (await sql.SaveWorldStateIfVersion(KEY_NPCS, JsonSerializer.Serialize(data, PersistJsonOptions), version))
                        {
                            saved = true;
                            NoteLiveRosterWritten(generation, version + 1);
                            session?.NoteRosterWritten(data, version + 1);
                            break;
                        }
                    }
                    // another writer got in first: its roster, this session's own changes over it, the clean-up again
                    version = sql.GetWorldStateVersion(KEY_NPCS);
                    var storedJson = await sql.LoadWorldState(KEY_NPCS);
                    var stored = string.IsNullOrEmpty(storedJson) ? null : JsonSerializer.Deserialize<List<NPCData>>(storedJson, PersistJsonOptions);
                    if (stored == null || stored.Count == 0) break;
                    var loaded = JsonSerializer.Deserialize<List<NPCData>>(storedJson!, PersistJsonOptions)!;
                    if (session != null) session.OverlaySessionChanges(stored, mine);
                    await GameEngine.Instance.RestoreNPCs(stored, version);
                    session?.NoteRosterReloaded(loaded, version, mine.Keys);
                    int again;
                    lock (RosterLock)
                    {
                        again = cleanUp();
                        data = SerializeCurrentNPCs();
                        generation = _rosterGeneration;
                        version = _liveRosterVersion ?? -1;   // a login may have restored another version meanwhile
                    }
                    if (again == 0 && mine.Count == 0) { saved = true; break; }   // the stored roster is already clean
                }
                if (!saved)
                    DebugLogger.Instance.LogWarning("ONLINE", "PersistNpcWorldNow gave up: the npcs record kept changing.");
                if (endedMarriages.Count > 0)
                    await RemoveStoredMarriagesAsync(sql, endedMarriages);
                if (saved) onWritten?.Invoke();
            }
            catch (Exception ex)
            {
                DebugLogger.Instance.LogError("ONLINE", $"PersistNpcWorldNow failed: {ex.Message}");
            }
            return changed;
        }

        /// <summary>
        /// v1.1.13: remove every marriage naming one of the NPC ids from world_state["marriages"], under its
        /// version, re-reading and re-applying on a conflict. Everything else in the record is kept as stored.
        /// </summary>
        internal static async Task<int> RemoveStoredMarriagesAsync(SqlSaveBackend sql, ICollection<string> npcIds, Func<Task>? beforeWrite = null)
        {
            for (int attempt = 0; attempt < 5; attempt++)
            {
                long version = sql.GetWorldStateVersion(KEY_MARRIAGES);
                var json = await sql.LoadWorldState(KEY_MARRIAGES);
                if (string.IsNullOrEmpty(json)) return 0;
                if (System.Text.Json.Nodes.JsonNode.Parse(json) is not System.Text.Json.Nodes.JsonObject root
                    || root["marriages"] is not System.Text.Json.Nodes.JsonArray list) return 0;
                int removed = 0;
                for (int i = list.Count - 1; i >= 0; i--)
                {
                    string a = IdOf(list[i], "npc1Id"), b = IdOf(list[i], "npc2Id");
                    if (npcIds.Contains(a) || npcIds.Contains(b)) { list.RemoveAt(i); removed++; }
                }
                if (removed == 0) return 0;
                if (beforeWrite != null) await beforeWrite();
                if (await sql.SaveWorldStateIfVersion(KEY_MARRIAGES, root.ToJsonString(), version)) return removed;
            }
            DebugLogger.Instance.LogWarning("ONLINE", "RemoveStoredMarriagesAsync gave up: the marriages record kept changing.");
            return 0;
        }

        private static string IdOf(System.Text.Json.Nodes.JsonNode? node, string key)
        {
            try { return node?[key]?.GetValue<string>() ?? ""; } catch { return ""; }
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
        /// Save royal court state to world_state.
        /// Called by player sessions when they change king state (throne challenge,
        /// tax policy change, treasury deposit/withdraw, etc.)
        /// The world_state 'royal_court' key is the single source of truth -
        /// the world sim reads this and maintains it between player sessions.
        /// </summary>
        public async Task SaveRoyalCourtToWorldState(bool throneVacated = false)
        {
            try
            {
                if (backend is not SqlSaveBackend sql) return;
                var king = global::CastleLocation.GetCurrentKing();
                // v1.1.11: an unmarked empty court never replaces a marked vacancy the world sim has yet to act on
                if (king == null && KeepsStoredVacancy(await ReadRoyalCourtFromWorldState(), throneVacated)) return;

                // v1.1.13: written only over the stored court this process's court was loaded from. A court never
                // loaded here may write only where none is stored yet. On a conflict the stored court is loaded and
                // this copy is not written over it: every change to a court is its own guarded write
                // (ApplyCourtChangeAsync), so the stored court already holds them.
                long? loadedAt = RoyalCourtVersion;
                if (loadedAt == null && sql.GetWorldStateVersion("royal_court") == 0) loadedAt = 0;
                if (loadedAt != null && await SaveRoyalCourtIfVersionAsync(loadedAt.Value, throneVacated))
                {
                    DebugLogger.Instance.LogDebug("ONLINE", $"Royal court saved to world_state: {king?.Name ?? "(vacant)"}");
                    return;
                }
                DebugLogger.Instance.LogInfo("ONLINE", "Royal court save skipped: the stored court changed since it was loaded. Reloading it.");
                await LoadRoyalCourtFromWorldState();
            }
            catch (Exception ex)
            {
                DebugLogger.Instance.LogError("ONLINE", $"Failed to save royal court to world_state: {ex.Message}");
            }
        }

        /// <summary>v1.1.11: the stored form of an empty court; a reign that just ended with no successor is marked, so the loaders clear their king.</summary>
        internal static string EmptyRoyalCourtJson(bool throneVacated)   // v1.1.13: internal for the world sim
        {
            // v1.1.11: the history goes too, with the reign the abdication just recorded
            var emptyData = new RoyalCourtSaveData { KingName = "", Treasury = 0, KingAI = 1, ThroneVacant = throneVacated,
                                                     MonarchHistory = global::CastleLocation.MonarchHistorySaveData() };
            return System.Text.Json.JsonSerializer.Serialize(emptyData,
                new System.Text.Json.JsonSerializerOptions { PropertyNamingPolicy = System.Text.Json.JsonNamingPolicy.CamelCase });
        }

        /// <summary>v1.1.11: the stored form of the court of this king (the body of SaveRoyalCourtToWorldState).</summary>
        private static string RoyalCourtJson(King king) => JsonSerializer.Serialize(CourtData(king), CourtJsonOptions);

        /// <summary>v1.1.13: the stored form of this king's court, as a record a court change edits (both processes' saves).</summary>
        internal static RoyalCourtSaveData CourtData(King king) => CourtData(king, null);

        /// <summary>v1.1.13: with this monarch history (null: the in-memory one).</summary>
        internal static RoyalCourtSaveData CourtData(King king, List<MonarchRecordSaveData>? history)
        {
            var data = new RoyalCourtSaveData
            {
                KingName = king.Name,
                Treasury = king.Treasury,
                TaxRate = king.TaxRate,
                TotalReign = king.TotalReign,
                KingTaxPercent = king.KingTaxPercent,
                CityTaxPercent = king.CityTaxPercent,
                DesignatedHeir = king.DesignatedHeir ?? "",
                KingAI = (int)king.AI,
                KingSex = (int)king.Sex,
                CoronationDate = king.CoronationDate.ToString("o"),
                TaxAlignment = (int)king.TaxAlignment,
                MonarchHistory = history ?? global::CastleLocation.GetMonarchHistory()?.Select(m => new MonarchRecordSaveData
                {
                    Name = m.Name,
                    Title = m.Title,
                    DaysReigned = m.DaysReigned,
                    CoronationDate = m.CoronationDate.ToString("o"),
                    EndReason = m.EndReason
                }).ToList() ?? new List<MonarchRecordSaveData>(),
                CourtMembers = king.CourtMembers?.Select(m => new CourtMemberSaveData
                {
                    Name = m.Name,
                    Faction = (int)m.Faction,
                    Influence = m.Influence,
                    LoyaltyToKing = m.LoyaltyToKing,
                    Role = m.Role,
                    IsPlotting = m.IsPlotting
                }).ToList() ?? new List<CourtMemberSaveData>(),
                Heirs = king.Heirs?.Select(h => new RoyalHeirSaveData
                {
                    Name = h.Name,
                    Age = h.Age,
                    ClaimStrength = h.ClaimStrength,
                    ParentName = h.ParentName,
                    Sex = (int)h.Sex,
                    IsDesignated = h.IsDesignated
                }).ToList() ?? new List<RoyalHeirSaveData>(),
                Spouse = king.Spouse != null ? new RoyalSpouseSaveData
                {
                    Name = king.Spouse.Name,
                    Sex = (int)king.Spouse.Sex,
                    OriginalFaction = (int)king.Spouse.OriginalFaction,
                    Dowry = king.Spouse.Dowry,
                    Happiness = king.Spouse.Happiness
                } : null,
                ActivePlots = king.ActivePlots?.Select(p => new CourtIntrigueSaveData
                {
                    PlotType = p.PlotType,
                    Conspirators = p.Conspirators,
                    Target = p.Target,
                    Progress = p.Progress,
                    IsDiscovered = p.IsDiscovered
                }).ToList() ?? new List<CourtIntrigueSaveData>(),
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

                // Phase 2: previously unserialized fields
                Prisoners = king.Prisoners?.Select(kvp => new PrisonRecordSaveData
                {
                    CharacterName = kvp.Value.CharacterName,
                    Crime = kvp.Value.Crime,
                    Sentence = kvp.Value.Sentence,
                    DaysServed = kvp.Value.DaysServed,
                    ImprisonmentDate = kvp.Value.ImprisonmentDate.ToString("o"),
                    BailAmount = kvp.Value.BailAmount
                }).ToList() ?? new List<PrisonRecordSaveData>(),
                Orphans = king.Orphans?.Select(OrphanData).ToList() ?? new List<RoyalOrphanSaveData>(),
                MagicBudget = king.MagicBudget,
                EstablishmentStatus = king.EstablishmentStatus ?? new Dictionary<string, bool>(),
                LastProclamation = king.LastProclamation ?? "",
                LastProclamationDate = king.LastProclamationDate != DateTime.MinValue
                    ? king.LastProclamationDate.ToString("o") : ""
            };
            return data;
        }

        /// <summary>v1.1.13: an orphan as the stored court holds it.</summary>
        internal static RoyalOrphanSaveData OrphanData(RoyalOrphan o) => new()
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
        };

        // v1.1.13: the options every court read and write uses (the ones jsonOptions has)
        private static readonly JsonSerializerOptions CourtJsonOptions = PersistJsonOptions;

        // v1.1.13: the stored royal_court version the in-memory court was loaded from or last written as. The
        // king is a process-wide static, so this is too: the world sim and every session share one court. The
        // in-memory court and this version change together, under this lock.
        private static readonly object RoyalCourtVersionLock = new();
        private static long? _royalCourtVersion;

        internal static long? RoyalCourtVersion { get { lock (RoyalCourtVersionLock) return _royalCourtVersion; } }

        internal static void NoteRoyalCourtVersion(long? version) { lock (RoyalCourtVersionLock) _royalCourtVersion = version; }

        /// <summary>v1.1.13: the in-memory court as it would be stored, and the version it was loaded at or last written as, taken together.</summary>
        internal static (string Json, long? Version) SnapshotCourt(bool throneVacated)
        {
            lock (RoyalCourtVersionLock)
            {
                var king = global::CastleLocation.GetCurrentKing();
                return (king == null ? EmptyRoyalCourtJson(throneVacated) : RoyalCourtJson(king), _royalCourtVersion);
            }
        }

        /// <summary>v1.1.13: a whole-court save written at version + 1 from a snapshot taken at version.</summary>
        internal static void NoteCourtWritten(long version)
        {
            lock (RoyalCourtVersionLock)
                if (_royalCourtVersion == null || _royalCourtVersion == version) _royalCourtVersion = version + 1;
        }

        // v1.1.13: court changes through one store run one at a time; the in-memory court only ever moves to a
        // newer written version, so changes through two stores of one process never put an older copy back
        private static readonly SemaphoreSlim LocalCourtGate = new(1, 1);
        private static readonly System.Runtime.CompilerServices.ConditionalWeakTable<SqlSaveBackend, SemaphoreSlim> CourtGates = new();
        private static SemaphoreSlim CourtGateFor(SqlSaveBackend? sql) => sql == null ? LocalCourtGate : CourtGates.GetValue(sql, _ => new SemaphoreSlim(1, 1));

        /// <summary>v1.1.13: the shared court this session writes to (null: none; the in-memory court is the only one).</summary>
        internal SqlSaveBackend? CourtStore => backend as SqlSaveBackend;

        /// <summary>
        /// v1.1.13: every change to an existing court goes through here. The stored court is read with its
        /// version, change is applied to that fresh copy (the cost and the benefit together), and the copy is
        /// written under that version; on a conflict the court is read again and change applied again. Once
        /// the write lands the in-memory court is the written copy, at the written version. change returns
        /// false to refuse (no king, a short treasury, a record gone): nothing is written and the in-memory
        /// court is the stored one. The caller applies the player's side only on true. Without SQL the
        /// in-memory court is the only one and change is applied to it the same way. beforeWrite is a test hook.
        /// </summary>
        internal static async Task<bool> ApplyCourtChangeAsync(SqlSaveBackend? sql, Func<RoyalCourtSaveData, bool> change, Func<Task>? beforeWrite = null)
        {
            var gate = CourtGateFor(sql);
            await gate.WaitAsync();
            try
            {
                if (sql == null)
                {
                    RoyalCourtSaveData local;
                    lock (RoyalCourtVersionLock)
                    {
                        var king = global::CastleLocation.GetCurrentKing();
                        if (king == null) return false;
                        local = CourtData(king);
                    }
                    if (!change(local)) return false;
                    lock (RoyalCourtVersionLock) ApplyCourtToKing(local, exact: true);
                    return true;
                }
                for (int attempt = 0; attempt < 5; attempt++)
                {
                    long version = sql.GetWorldStateVersion("royal_court");   // read before the value, so a later write is a conflict
                    var json = await sql.LoadWorldState("royal_court");
                    RoyalCourtSaveData? court;
                    if (string.IsNullOrEmpty(json))
                    {
                        // no court stored yet: this process's court is the first one
                        lock (RoyalCourtVersionLock)
                        {
                            var king = global::CastleLocation.GetCurrentKing();
                            court = version == 0 && king != null ? CourtData(king) : null;
                        }
                        if (court == null) return false;
                    }
                    else court = JsonSerializer.Deserialize<RoyalCourtSaveData>(json, CourtJsonOptions);
                    if (court == null || string.IsNullOrEmpty(court.KingName) || court.ThroneVacant || !change(court))
                    {
                        // refused: the in-memory court becomes the stored one, as read
                        if (!string.IsNullOrEmpty(json)) ApplyLoadedCourt(JsonSerializer.Deserialize<RoyalCourtSaveData>(json, CourtJsonOptions), version);
                        return false;
                    }
                    if (beforeWrite != null) await beforeWrite();
                    if (await sql.SaveWorldStateIfVersion("royal_court", JsonSerializer.Serialize(court, CourtJsonOptions), version))
                    {
                        lock (RoyalCourtVersionLock)
                        {
                            if (_royalCourtVersion == null || _royalCourtVersion <= version)
                            {
                                ApplyCourtToKing(court, exact: true);
                                global::CastleLocation.RoyalCourtLoadedFromShared = true;
                                _royalCourtVersion = version + 1;
                            }
                        }
                        return true;
                    }
                }
                DebugLogger.Instance.LogWarning("ONLINE", "Court change gave up: the royal court kept changing. Nothing was changed.");
                long v = sql.GetWorldStateVersion("royal_court");
                var latest = await sql.LoadWorldState("royal_court");
                if (!string.IsNullOrEmpty(latest)) ApplyLoadedCourt(JsonSerializer.Deserialize<RoyalCourtSaveData>(latest, CourtJsonOptions), v);
                return false;
            }
            catch (Exception ex)
            {
                DebugLogger.Instance.LogError("ONLINE", $"Court change failed: {ex.Message}");
                return false;
            }
            finally { gate.Release(); }
        }

        /// <summary>v1.1.13: a court change against this session's shared court (see the static form).</summary>
        internal Task<bool> TryApplyCourtChangeAsync(Func<RoyalCourtSaveData, bool> change, Func<Task>? beforeWrite = null) =>
            ApplyCourtChangeAsync(CourtStore, change, beforeWrite);

        /// <summary>
        /// v1.1.13: the same guarded court change, for game logic written against King (the world sim's court
        /// politics). change runs on a King made from the stored court, never the in-memory one; the court is
        /// written only when change returns true and the court it leaves differs from the one read.
        /// </summary>
        internal static Task<bool> ApplyKingChangeAsync(SqlSaveBackend? sql, Func<King, bool> change, Func<Task>? beforeWrite = null) =>
            ApplyCourtChangeAsync(sql, court =>
            {
                var working = KingFromCourt(court);
                string before = JsonSerializer.Serialize(CourtData(working, court.MonarchHistory), CourtJsonOptions);
                if (!change(working)) return false;
                var after = CourtData(working, court.MonarchHistory);
                if (JsonSerializer.Serialize(after, CourtJsonOptions) == before) return false;
                foreach (var prop in typeof(RoyalCourtSaveData).GetProperties())
                    if (prop.CanRead && prop.CanWrite) prop.SetValue(court, prop.GetValue(after));
                return true;
            }, beforeWrite);

        /// <summary>
        /// v1.1.13: a change of monarch as one versioned write. The stored court is read with its version (null:
        /// none stored) and crown builds the new court from it, or returns null to refuse (the throne changed
        /// hands first; see ReigningName). The new court is written under the version read and retried on a
        /// conflict; once written the in-memory court is the written copy with the new king. On a refusal the
        /// in-memory court becomes the stored one (unless reloadOnRefusal is false). The caller applies the
        /// coronation's other effects (NPC and player flags, news) only on true. beforeWrite is a test hook.
        /// </summary>
        internal static async Task<bool> CrownAsync(SqlSaveBackend? sql,
            Func<RoyalCourtSaveData?, RoyalCourtSaveData?> crown, bool reloadOnRefusal = true, Func<Task>? beforeWrite = null)
        {
            var gate = CourtGateFor(sql);
            await gate.WaitAsync();
            try
            {
                if (sql == null)
                {
                    RoyalCourtSaveData? local;
                    lock (RoyalCourtVersionLock)
                    {
                        var king = global::CastleLocation.GetCurrentKing();
                        local = king == null ? null : CourtData(king);
                        if (local != null && !king!.IsActive) local.ThroneVacant = true;   // a reign ended here
                    }
                    var crownedLocal = crown(local);
                    if (crownedLocal == null || string.IsNullOrEmpty(crownedLocal.KingName)) return false;
                    crownedLocal.ThroneVacant = false;
                    lock (RoyalCourtVersionLock) ApplyCrownedCourt(crownedLocal);
                    return true;
                }
                for (int attempt = 0; attempt < 5; attempt++)
                {
                    long version = sql.GetWorldStateVersion("royal_court");   // read before the value, so a later write is a conflict
                    var json = await sql.LoadWorldState("royal_court");
                    RoyalCourtSaveData? stored;
                    if (string.IsNullOrEmpty(json))
                    {
                        // no court stored yet: this process's court is the first one
                        lock (RoyalCourtVersionLock)
                        {
                            var king = global::CastleLocation.GetCurrentKing();
                            stored = version == 0 && king != null && king.IsActive ? CourtData(king) : null;
                        }
                    }
                    else stored = JsonSerializer.Deserialize<RoyalCourtSaveData>(json, CourtJsonOptions);
                    var crowned = crown(stored);
                    if (crowned == null || string.IsNullOrEmpty(crowned.KingName))
                    {
                        if (reloadOnRefusal && !string.IsNullOrEmpty(json))
                            ApplyLoadedCourt(JsonSerializer.Deserialize<RoyalCourtSaveData>(json, CourtJsonOptions), version);
                        return false;
                    }
                    crowned.ThroneVacant = false;
                    if (beforeWrite != null) await beforeWrite();
                    if (await sql.SaveWorldStateIfVersion("royal_court", JsonSerializer.Serialize(crowned, CourtJsonOptions), version))
                    {
                        lock (RoyalCourtVersionLock)
                        {
                            if (_royalCourtVersion == null || _royalCourtVersion <= version)
                            {
                                ApplyCrownedCourt(crowned);
                                global::CastleLocation.RoyalCourtLoadedFromShared = true;
                                _royalCourtVersion = version + 1;
                            }
                        }
                        return true;
                    }
                }
                DebugLogger.Instance.LogWarning("ONLINE", "Coronation gave up: the royal court kept changing. Nothing was changed.");
                if (reloadOnRefusal)
                {
                    long v = sql.GetWorldStateVersion("royal_court");
                    var latest = await sql.LoadWorldState("royal_court");
                    if (!string.IsNullOrEmpty(latest)) ApplyLoadedCourt(JsonSerializer.Deserialize<RoyalCourtSaveData>(latest, CourtJsonOptions), v);
                }
                return false;
            }
            catch (Exception ex)
            {
                DebugLogger.Instance.LogError("ONLINE", $"Coronation failed: {ex.Message}");
                return false;
            }
            finally { gate.Release(); }
        }

        /// <summary>v1.1.13: the monarch a court record names as reigning (null: none, or a vacancy).</summary>
        internal static string? ReigningName(RoyalCourtSaveData? court) =>
            court == null || court.ThroneVacant || string.IsNullOrEmpty(court.KingName) ? null : court.KingName;

        /// <summary>v1.1.13: a crowned court becomes the in-memory court, as a new King. Call under RoyalCourtVersionLock.</summary>
        private static void ApplyCrownedCourt(RoyalCourtSaveData court)
        {
            var king = new King
            {
                Name = court.KingName,
                AI = (CharacterAI)court.KingAI,
                Sex = (CharacterSex)court.KingSex,
                IsActive = true
            };
            ApplyCourtTo(king, court, exact: true, history: true);
            global::CastleLocation.SetKing(king);
        }

        /// <summary>v1.1.13: the world sim's shared court when no session's is at hand (set by the running world sim).</summary>
        internal static SqlSaveBackend? SimCourtStore;

        /// <summary>v1.1.13: the shared court a change goes to: this session's, else the world sim's when online, else none.</summary>
        internal static SqlSaveBackend? CourtStoreFor(OnlineStateManager? osm) =>
            osm?.CourtStore ?? (DoorMode.IsOnlineMode ? SimCourtStore : null);

        /// <summary>v1.1.11: the stored court and the version it was read at (the version read first, so a later write is a conflict).</summary>
        internal async Task<(RoyalCourtSaveData? Court, long Version)> ReadRoyalCourtWithVersionAsync()
        {
            long version = (backend as SqlSaveBackend)?.GetWorldStateVersion("royal_court") ?? 0;
            return (await ReadRoyalCourtFromWorldState(), version);
        }

        /// <summary>
        /// v1.1.11: this process's court, written only if the stored court is still at the version read.
        /// False on a conflict, so the caller re-reads and decides again. Without SQL it is a plain save.
        /// </summary>
        internal async Task<bool> SaveRoyalCourtIfVersionAsync(long version, bool throneVacated)
        {
            try
            {
                if (backend is not SqlSaveBackend sql) return false;   // v1.1.13: no unversioned fallback
                var (json, _) = SnapshotCourt(throneVacated);
                if (!await sql.SaveWorldStateIfVersion("royal_court", json, version)) return false;
                NoteCourtWritten(version);   // v1.1.13: the in-memory court is now the stored one
                return true;
            }
            catch (Exception ex)
            {
                DebugLogger.Instance.LogError("ONLINE", $"Failed to save royal court under its version: {ex.Message}");
                return false;
            }
        }

        /// <summary>
        /// Save economy/city control data to world_state for the dashboard.
        /// Called from player sessions (game server) which have access to the player,
        /// so the dashboard can show the player as city control leader when applicable.
        /// </summary>
        public async Task SaveEconomyToWorldState()
        {
            try
            {
                var king = CastleLocation.GetCurrentKing();
                var cityInfo = CityControlSystem.Instance.GetCityControlInfo();
                var leader = CityControlSystem.Instance.GetCityControlLeader();

                var economyData = new Dictionary<string, object?>
                {
                    ["kingName"] = king?.Name ?? "None",
                    ["kingIsActive"] = king?.IsActive ?? false,
                    ["treasury"] = king?.Treasury ?? 0,
                    ["taxRate"] = king?.TaxRate ?? 0,
                    ["kingTaxPercent"] = king?.KingTaxPercent ?? 0,
                    ["cityTaxPercent"] = king?.CityTaxPercent ?? 0,
                    ["dailyTaxRevenue"] = king?.DailyTaxRevenue ?? 0,
                    ["dailyCityTaxRevenue"] = king?.DailyCityTaxRevenue ?? 0,
                    ["dailyIncome"] = king?.CalculateDailyIncome() ?? 0,
                    ["dailyExpenses"] = king?.CalculateDailyExpenses() ?? 0,
                    ["cityControlTeam"] = cityInfo.TeamName,
                    ["cityControlMembers"] = cityInfo.MemberCount,
                    ["cityControlPower"] = cityInfo.TotalPower,
                    ["cityControlLeader"] = leader.Name,
                    ["cityControlLeaderBank"] = leader.BankGold,
                    ["cityControlLeaderIsPlayer"] = leader.IsPlayer,
                    ["updatedAt"] = DateTime.UtcNow.ToString("o")
                };

                var json = JsonSerializer.Serialize(economyData, jsonOptions);
                await backend.SaveWorldState("economy", json);
            }
            catch (Exception ex)
            {
                DebugLogger.Instance.LogError("ONLINE", $"Failed to save economy to world_state: {ex.Message}");
            }
        }

        /// <summary>v1.1.11: an empty-court save that is not itself a vacancy leaves a stored vacancy in place.</summary>
        internal static bool KeepsStoredVacancy(RoyalCourtSaveData? stored, bool throneVacated) =>
            !throneVacated && stored?.ThroneVacant == true;

        /// <summary>v1.1.11: the shared royal_court as stored, applied to nothing (null when absent or unreadable).</summary>
        public async Task<RoyalCourtSaveData?> ReadRoyalCourtFromWorldState()
        {
            try
            {
                var json = await backend.LoadWorldState("royal_court");
                return string.IsNullOrEmpty(json) ? null : JsonSerializer.Deserialize<RoyalCourtSaveData>(json, jsonOptions);
            }
            catch (Exception ex)
            {
                DebugLogger.Instance.LogError("ONLINE", $"Failed to read royal court: {ex.Message}");
                return null;
            }
        }

        /// <summary>
        /// Load royal court state from world_state and apply to current king.
        /// Called by player sessions on login to get the authoritative king state
        /// from the world (maintained by the world sim).
        /// </summary>
        public async Task LoadRoyalCourtFromWorldState()
        {
            try
            {
                long version = (backend as SqlSaveBackend)?.GetWorldStateVersion("royal_court") ?? 0;   // v1.1.13: read before the value
                var json = await backend.LoadWorldState("royal_court");
                if (string.IsNullOrEmpty(json)) return;
                ApplyLoadedCourt(JsonSerializer.Deserialize<RoyalCourtSaveData>(json, jsonOptions), version);
            }
            catch (Exception ex)
            {
                DebugLogger.Instance.LogError("ONLINE", $"Failed to load royal court from world_state: {ex.Message}");
            }
        }

        /// <summary>
        /// v1.1.13: a court read from world_state at this version becomes the in-memory court, with the version
        /// noted in the same step (both loaders). True when it was a marked vacancy that cleared the king.
        /// </summary>
        internal static bool ApplyLoadedCourt(RoyalCourtSaveData? royalCourt, long version)
        {
            if (royalCourt == null) return false;
            lock (RoyalCourtVersionLock)
            {
                global::CastleLocation.RoyalCourtLoadedFromShared = true;   // v1.1.11
                _royalCourtVersion = version;
                if (global::CastleLocation.ApplySharedThroneVacancy(royalCourt)) return true;   // v1.1.11
                if (string.IsNullOrEmpty(royalCourt.KingName)) return false;
                ApplyCourtToKing(royalCourt, exact: false);
                return false;
            }
        }

        /// <summary>
        /// v1.1.13: the stored form of a court applied to the in-memory king (a new King when the name differs).
        /// exact: every record is replaced, empty lists too, so the in-memory court is the written copy; a load
        /// (not exact) keeps the in-memory lists an older record left empty, as before. Fields the record does
        /// not hold (recruitment and joining dates, the day's tax takings) are kept by name. Call under
        /// RoyalCourtVersionLock.
        /// </summary>
        private static void ApplyCourtToKing(RoyalCourtSaveData royalCourt, bool exact)
        {
            var king = global::CastleLocation.GetCurrentKing();
            if (king == null || king.Name != royalCourt.KingName)
            {
                // Create king directly from saved data - don't use SetCurrentKing
                // which creates a fresh King with default treasury/empty guards
                king = new King
                {
                    Name = royalCourt.KingName,
                    AI = (CharacterAI)royalCourt.KingAI,
                    Sex = (CharacterSex)royalCourt.KingSex,
                    IsActive = true
                };
                global::CastleLocation.SetKing(king);
            }
            ApplyCourtTo(king, royalCourt, exact, history: true);
        }

        /// <summary>v1.1.13: a King made from a court record, apart from the in-memory court (a court change's working copy).</summary>
        internal static King KingFromCourt(RoyalCourtSaveData royalCourt)
        {
            var king = new King
            {
                Name = royalCourt.KingName,
                AI = (CharacterAI)royalCourt.KingAI,
                Sex = (CharacterSex)royalCourt.KingSex,
                IsActive = true
            };
            ApplyCourtTo(king, royalCourt, exact: true, history: false);
            return king;
        }

        private static void ApplyCourtTo(King king, RoyalCourtSaveData royalCourt, bool exact, bool history)
        {
            king.Treasury = royalCourt.Treasury;
            king.TaxRate = royalCourt.TaxRate;
            king.TotalReign = royalCourt.TotalReign;
            king.KingTaxPercent = royalCourt.KingTaxPercent > 0 ? royalCourt.KingTaxPercent : 5;
            king.CityTaxPercent = royalCourt.CityTaxPercent > 0 ? royalCourt.CityTaxPercent : 2;
            king.DesignatedHeir = royalCourt.DesignatedHeir;

            // Restore coronation date and tax alignment
            if (!string.IsNullOrEmpty(royalCourt.CoronationDate))
            {
                if (DateTime.TryParse(royalCourt.CoronationDate, null, System.Globalization.DateTimeStyles.RoundtripKind, out var coronation))
                    king.CoronationDate = coronation;
            }
            king.TaxAlignment = (GameConfig.TaxAlignment)royalCourt.TaxAlignment;

            // Restore monarch history
            if (history && royalCourt.MonarchHistory != null && royalCourt.MonarchHistory.Count > 0)
            {
                var monarchs = royalCourt.MonarchHistory.Select(m => new MonarchRecord
                {
                    Name = m.Name,
                    Title = m.Title,
                    DaysReigned = m.DaysReigned,
                    CoronationDate = DateTime.TryParse(m.CoronationDate, null, System.Globalization.DateTimeStyles.RoundtripKind, out var cd) ? cd : DateTime.Now,
                    EndReason = m.EndReason
                }).ToList();
                global::CastleLocation.SetMonarchHistory(monarchs);
            }

            if (royalCourt.CourtMembers != null)
            {
                var joined = (king.CourtMembers ?? new List<CourtMember>()).GroupBy(m => m.Name).ToDictionary(g => g.Key, g => g.First().JoinedCourt);
                king.CourtMembers = royalCourt.CourtMembers.Select(m => new CourtMember
                {
                    Name = m.Name,
                    Faction = (CourtFaction)m.Faction,
                    Influence = m.Influence,
                    LoyaltyToKing = m.LoyaltyToKing,
                    Role = m.Role,
                    IsPlotting = m.IsPlotting,
                    JoinedCourt = joined.TryGetValue(m.Name, out var j) ? j : DateTime.Now
                }).ToList();
            }

            if (royalCourt.Heirs != null)
            {
                var born = (king.Heirs ?? new List<RoyalHeir>()).GroupBy(h => h.Name).ToDictionary(g => g.Key, g => g.First().BirthDate);
                king.Heirs = royalCourt.Heirs.Select(h => new RoyalHeir
                {
                    Name = h.Name,
                    Age = h.Age,
                    ClaimStrength = h.ClaimStrength,
                    ParentName = h.ParentName,
                    Sex = (CharacterSex)h.Sex,
                    IsDesignated = h.IsDesignated,
                    BirthDate = born.TryGetValue(h.Name, out var b) ? b : DateTime.Now
                }).ToList();
            }

            if (royalCourt.Spouse != null)
            {
                var married = king.Spouse?.Name == royalCourt.Spouse.Name ? king.Spouse.MarriageDate : DateTime.Now;
                king.Spouse = new RoyalSpouse
                {
                    Name = royalCourt.Spouse.Name,
                    Sex = (CharacterSex)royalCourt.Spouse.Sex,
                    OriginalFaction = (CourtFaction)royalCourt.Spouse.OriginalFaction,
                    Dowry = royalCourt.Spouse.Dowry,
                    Happiness = royalCourt.Spouse.Happiness,
                    MarriageDate = married
                };
            }
            else
            {
                king.Spouse = null; // Ensure old spouse doesn't carry over
            }

            if (royalCourt.ActivePlots != null)
            {
                king.ActivePlots = royalCourt.ActivePlots.Select(p => new CourtIntrigue
                {
                    PlotType = p.PlotType,
                    Conspirators = p.Conspirators ?? new List<string>(),
                    Target = p.Target,
                    Progress = p.Progress,
                    IsDiscovered = p.IsDiscovered
                }).ToList();
            }

            // Restore guards
            if (royalCourt.Guards != null && (exact || royalCourt.Guards.Count > 0))
            {
                var recruited = (king.Guards ?? new List<RoyalGuard>()).GroupBy(g => g.Name).ToDictionary(g => g.Key, g => g.First().RecruitmentDate);
                king.Guards = royalCourt.Guards.Select(g => new RoyalGuard
                {
                    Name = g.Name,
                    AI = (CharacterAI)g.AI,
                    Sex = (CharacterSex)g.Sex,
                    DailySalary = g.DailySalary,
                    Loyalty = g.Loyalty,
                    IsActive = g.IsActive,
                    RecruitmentDate = recruited.TryGetValue(g.Name, out var r) ? r : DateTime.Now
                }).ToList();
            }

            // Restore monster guards
            if (royalCourt.MonsterGuards != null && (exact || royalCourt.MonsterGuards.Count > 0))
            {
                var acquired = (king.MonsterGuards ?? new List<MonsterGuard>()).GroupBy(m => m.Name).ToDictionary(g => g.Key, g => g.First().AcquiredDate);
                king.MonsterGuards = royalCourt.MonsterGuards.Select(m => new MonsterGuard
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
                    DailyFeedingCost = m.DailyFeedingCost,
                    AcquiredDate = acquired.TryGetValue(m.Name, out var a) ? a : DateTime.Now
                }).ToList();
            }

            // Phase 2 - restore previously unserialized fields
            if (royalCourt.Prisoners != null && (exact || royalCourt.Prisoners.Count > 0))
            {
                king.Prisoners = royalCourt.Prisoners.GroupBy(p => p.CharacterName).Select(g => g.Last()).ToDictionary(
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

            if (royalCourt.Orphans != null && (exact || royalCourt.Orphans.Count > 0))
            {
                king.Orphans = royalCourt.Orphans.Select(o => new RoyalOrphan
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

            king.MagicBudget = royalCourt.MagicBudget;

            // an empty list is an older record's, never a court with no establishments: the defaults stay
            if (royalCourt.EstablishmentStatus != null && royalCourt.EstablishmentStatus.Count > 0)
                king.EstablishmentStatus = new Dictionary<string, bool>(royalCourt.EstablishmentStatus);

            if (exact || !string.IsNullOrEmpty(royalCourt.LastProclamation))
                king.LastProclamation = royalCourt.LastProclamation ?? "";

            if (!string.IsNullOrEmpty(royalCourt.LastProclamationDate) &&
                DateTime.TryParse(royalCourt.LastProclamationDate, out var procDate))
                king.LastProclamationDate = procDate;
            else if (exact)
                king.LastProclamationDate = DateTime.MinValue;
        }

        /// <summary>
        /// Load settlement state from world_state (authoritative source).
        /// Called by player sessions on login so they get current settlement data
        /// instead of stale data from their player save.
        /// </summary>
        public async Task LoadSettlementFromWorldState()
        {
            try
            {
                var json = await backend.LoadWorldState("settlement");
                if (string.IsNullOrEmpty(json)) return;

                var saveData = JsonSerializer.Deserialize<SettlementSaveData>(json, jsonOptions);
                if (saveData != null)
                {
                    SettlementSystem.Instance.RestoreFromSaveData(saveData);
                    DebugLogger.Instance.LogInfo("ONLINE", $"Settlement overridden from world_state: {SettlementSystem.Instance.GetSettlerCount()} settlers");
                }
            }
            catch (Exception ex)
            {
                DebugLogger.Instance.LogError("ONLINE", $"Failed to load settlement from world_state: {ex.Message}");
            }
        }

        /// <summary>
        /// Immediately persist current settlement state to world_state.
        /// Called after player actions (contributions, votes) to prevent data loss
        /// if the server restarts before the next world sim save cycle.
        /// </summary>
        public async Task SaveSettlementToWorldState()
        {
            try
            {
                var saveData = SettlementSystem.Instance.ToSaveData();
                var json = JsonSerializer.Serialize(saveData, jsonOptions);
                await backend.SaveWorldState("settlement", json);
            }
            catch (Exception ex)
            {
                DebugLogger.Instance.LogError("ONLINE", $"Failed to persist settlement to world_state: {ex.Message}");
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
            var ipAddress = SessionContext.Current?.RemoteIP ?? "";
            await backend.RegisterOnline(username, displayName, currentLocation, connectionType, ipAddress);
            await backend.UpdatePlayerSession(username, isLogin: true, ipAddress: ipAddress);

            // v0.57.13: announce arrival to Discord gossip channel (no-op if bridge disabled)
            cachedDisplayName = displayName;
            try { DiscordBridge.QueueSystemEvent($"{displayName} has entered the world."); } catch { }

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
                        bool updated = await backend.UpdateHeartbeat(username, currentLocation);
                        if (!updated)
                        {
                            // Row was deleted by stale cleanup or never inserted — re-register
                            DebugLogger.Instance.LogWarning("HEARTBEAT", $"Re-registering '{displayName}' — heartbeat row was missing");
                            await backend.RegisterOnline(username, displayName, currentLocation, DeferredConnectionType);
                        }
                        cachedOnlinePlayerCount = (await backend.GetOnlinePlayers()).Count;
                    }
                    catch (Exception ex)
                    {
                        DebugLogger.Instance.LogError("HEARTBEAT", $"Heartbeat failed for '{username}': {ex.Message}");
                    }
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
        /// Update display name in online_players table (called when character loads with custom Name2).
        /// </summary>
        public async Task UpdateDisplayName(string displayName)
        {
            try
            {
                await backend.UpdateOnlineDisplayName(username, displayName);
            }
            catch (Exception ex)
            {
                DebugLogger.Instance.LogError("ONLINE", $"Failed to update display name: {ex.Message}");
            }
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
        /// <summary>
        /// Switch online identity (e.g., when player switches from main to alt character).
        /// Unregisters old key and registers new key in online_players.
        /// </summary>
        public async Task SwitchIdentity(string newKey, string displayName, string connectionType)
        {
            var oldKey = username;
            try { await backend.UnregisterOnline(oldKey); } catch { }
            username = newKey;
            await backend.RegisterOnline(newKey, displayName, currentLocation, connectionType);
            await backend.UpdatePlayerSession(newKey, isLogin: true);
            DebugLogger.Instance.LogInfo("ONLINE", $"Switched identity from '{oldKey}' to '{newKey}'");
        }

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

                // v0.57.13: announce departure to Discord gossip channel (no-op if bridge disabled)
                var departName = cachedDisplayName ?? username;
                try { DiscordBridge.QueueSystemEvent($"{departName} has left the world."); } catch { }

                DebugLogger.Instance.LogInfo("ONLINE", $"Online tracking stopped for '{username}'");
            }
            catch (Exception ex)
            {
                DebugLogger.Instance.LogError("ONLINE", $"Error during shutdown for '{username}': {ex.Message}");
            }

            // Clear the correct reference
            var ctx = UsurperRemake.Server.SessionContext.Current;
            if (ctx != null && ctx.OnlineState == this)
                ctx.OnlineState = null;
            else if (_fallbackInstance == this)
                _fallbackInstance = null;
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
                    // v0.63.2 Fix B: persist intrinsic gear power so it
                    // survives the RecalculateStats reset on next load.
                    BaseWeapPow = npc.BaseWeapPow > 0 ? npc.BaseWeapPow : npc.WeapPow,
                    BaseArmPow = npc.BaseArmPow > 0 ? npc.BaseArmPow : npc.ArmPow,
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
                    // v0.61.3: NPC potion counts. Mirrors the single-player path
                    // in SaveSystem.SerializeNPCs. Pre-fix these were never written
                    // to world_state, so any potion consumption / player gift was
                    // wiped on the next world-sim reload.
                    HealingPotions = (int)npc.Healing,
                    ManaPotions = (int)npc.ManaPotions,
                    IsMarried = npc.IsMarried,
                    Married = npc.Married,
                    SpouseName = npc.SpouseName ?? "",
                    MarriedTimes = npc.MarriedTimes,
                    NPCFaction = npc.NPCFaction.HasValue ? (int)npc.NPCFaction.Value : -1,
                    Chivalry = npc.Chivalry,
                    Darkness = npc.Darkness,
                    Gold = npc.Gold,
                    BankGold = npc.BankGold,
                    Items = npc.Item?.ToArray() ?? new int[0],
                    EquippedItems = npc.EquippedItems?.ToDictionary(
                        kvp => (int)kvp.Key, kvp => kvp.Value) ?? new Dictionary<int, int>(),

                    // Save dynamic equipment that this NPC has equipped (dungeon loot, etc.)
                    // DynamicEquipmentStart = 100000; base/shop equipment must NOT be saved as dynamic
                    DynamicEquipment = npc.EquippedItems?
                        .Where(kvp => kvp.Value >= 100000)
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
                        Family = equip.Family ?? "",
                        IsIdentified = equip.IsIdentified, // v1.1.1: defaulted true on reload
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
                            ManaRegen = equip.ManaRegen,
                            WeightClass = (int)equip.WeightClass,
                            StrengthRequired = equip.StrengthRequired,
                            RequiresGood = equip.RequiresGood,
                            RequiresEvil = equip.RequiresEvil,
                            ClassRestrictions = equip.ClassRestrictions?.Select(c => (int)c).ToList(),
                            IsUnique = equip.IsUnique,
                            HasBossSlayer = equip.HasBossSlayer,
                            HasTitanResolve = equip.HasTitanResolve
                        }).ToList() ?? new List<DynamicEquipmentData>(),

                    // AI state - for dashboard analytics
                    PersonalityProfile = SerializePersonalityStatic(npc.Brain?.Personality),
                    Memories = SerializeMemoriesStatic(npc.Brain?.Memory),
                    MemoryTimesKept = true,   // v1.1.13
                    CurrentGoals = SerializeGoalsStatic(npc.Brain?.Goals),
                    EmotionalState = SerializeEmotionalStateForDashboard(npc),
                    // Scale from internal -1..1 to dashboard-expected -100..100
                    // Cap at 20 most significant relationships to limit serialization size
                    Relationships = npc.Brain?.Memory?.CharacterImpressions?
                        .OrderByDescending(kvp => Math.Abs(kvp.Value))
                        .Take(20)
                        .ToDictionary(kvp => kvp.Key, kvp => kvp.Value * 100f)
                        ?? new Dictionary<string, float>(),

                    // Enemies. v0.65.5: cap for parity with SaveSystem.SerializeNPCs (was uncapped
                    // here, and world_state.npcs is rewritten frequently, so an NPC with a long-lived
                    // grudge list bloated every save). TakeLast keeps the most recent grudges.
                    Enemies = (npc.Enemies ?? new List<string>())
                        .TakeLast(GameConfig.MaxSerializedEnemiesPerNpc).ToList(),

                    // Divine worship
                    WorshippedGod = npc.WorshippedGod ?? "",

                    // Dialogue tracking
                    RecentDialogueIds = NPCDialogueDatabase.GetRecentlyUsedIds(npc.Name2 ?? npc.Name1 ?? ""),

                    // Social emergence
                    EmergentRole = npc.EmergentRole ?? "",
                    RoleStabilityTicks = npc.RoleStabilityTicks,

                    // Skill proficiency
                    SkillProficiencies = npc.SkillProficiencies?.ToDictionary(
                        kvp => kvp.Key, kvp => (int)kvp.Value) ?? new Dictionary<string, int>(),
                    SkillTrainingProgress = npc.SkillTrainingProgress ?? new Dictionary<string, int>(),

                    // Market inventory for NPC trading. v0.65.5: capped for parity with SaveSystem.
                    MarketInventory = (npc.MarketInventory ?? new List<Item>())
                        .Take(GameConfig.MaxSerializedNPCInventory)
                        .Select(MarketItemData.FromItem).ToList(),

                    // v0.57.4: personal bag — items the player transferred to this
                    // NPC via combat [T] / Home / Team Corner / dungeon viewer.
                    // Previously dropped on world-sim reload in MUD mode (the MUD
                    // server persists shared NPC state to world_state SQLite; no
                    // Inventory field = runtime items lost every tick).
                    // issue #112: full-fidelity converter (carries rarity + all stats) so NPC-bag items round-trip in world_state.
                    // v0.65.5: capped to the most recent N for parity with SaveSystem.
                    Inventory = (npc.Inventory ?? new List<Item>()).Where(i => i != null)
                        .TakeLast(GameConfig.MaxSerializedNPCInventory)
                        .Select(InventoryItemData.FromItem).ToList(),

                    // Lifecycle
                    Age = npc.Age,
                    BirthDate = npc.BirthDate,
                    IsAgedDeath = npc.IsAgedDeath,
                    IsPermaDead = npc.IsPermaDead,
                    DeathDate = npc.DeathDate,
                    PregnancyDueDate = npc.PregnancyDueDate,
                    PregnancyFatherName = npc.PregnancyFatherName,

                    // Lineage (v0.63.0 -- relationship completion slice 1).
                    MotherName = npc.MotherName ?? "",
                    FatherName = npc.FatherName ?? "",
                    MotherID = npc.MotherID ?? "",
                    FatherID = npc.FatherID ?? "",
                    OriginalMotherName = npc.OriginalMotherName ?? "",
                    OriginalFatherName = npc.OriginalFatherName ?? "",
                    SoulAtGraduation = npc.SoulAtGraduation,
                    WasRaisedByPlayer = npc.WasRaisedByPlayer,

                    // v0.64.0 Brain v2 Slice 1 cohort flag.
                    IsAIDriven = npc.IsAIDriven,

                    // Hostility, social graph, and gang affiliation
                    IsHostile = npc.IsHostile,
                    KnownCharacters = npc.KnownCharacters?.ToList() ?? new List<string>(),
                    GangId = npc.GangId ?? "",

                    // Class specialization
                    Specialization = npc.Specialization
                });
            }

            return npcData;
        }

        // --- Static serialization helpers for dashboard analytics ---

        private static PersonalityData? SerializePersonalityStatic(PersonalityProfile? profile)
        {
            if (profile == null) return null;
            return new PersonalityData
            {
                Aggression = profile.Aggression,
                Loyalty = profile.Loyalty,
                Intelligence = profile.Intelligence,
                Greed = profile.Greed,
                Compassion = profile.Sociability,
                Courage = profile.Courage,
                Honesty = profile.Trustworthiness,
                Ambition = profile.Ambition,
                Vengefulness = profile.Vengefulness,
                Impulsiveness = profile.Impulsiveness,
                Caution = profile.Caution,
                Mysticism = profile.Mysticism,
                Patience = profile.Patience,
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

        private static List<MemoryData> SerializeMemoriesStatic(MemorySystem? memory)
        {
            if (memory == null) return new List<MemoryData>();
            // Limit to last 10 memories to control JSON blob size
            return memory.AllMemories
                .OrderByDescending(m => m.Timestamp)
                .Take(10)
                .Select(m => new MemoryData
                {
                    Type = m.Type.ToString(),
                    Description = m.Description,
                    InvolvedCharacter = m.InvolvedCharacter ?? "",
                    Importance = m.Importance,
                    EmotionalImpact = m.EmotionalImpact,
                    Timestamp = m.Timestamp
                }).ToList();
        }

        private static List<GoalData> SerializeGoalsStatic(GoalSystem? goals)
        {
            if (goals == null) return new List<GoalData>();
            // Only serialize active goals, capped at 30, to prevent unbounded growth
            return goals.AllGoals
                .Where(g => g.IsActive && !g.IsCompleted)
                .OrderByDescending(g => g.GetEffectivePriority())
                .Take(30)
                .Select(g => new GoalData
                {
                    Name = g.Name,
                    Type = g.Type.ToString(),
                    Priority = g.Priority,
                    Progress = g.Progress,
                    IsActive = g.IsActive,
                    TargetValue = g.TargetValue,
                    CurrentValue = g.CurrentValue,
                    TargetCharacter = g.TargetCharacter ?? "" // v0.64.1: load-bearing for Slices 13/14b/19/20 + revenge completion
                }).ToList();
        }

        /// <summary>
        /// Serialize emotional state for the dashboard. Merges transient emotions
        /// (from NPC.EmotionalState and Brain.Emotions) with personality-derived
        /// baselines so the dashboard always shows meaningful values.
        /// </summary>
        private static EmotionalStateData? SerializeEmotionalStateForDashboard(NPC npc)
        {
            var personality = npc.Brain?.Personality;
            if (personality == null) return null;

            // Get transient emotions from both possible sources (they're separate instances)
            var es1 = npc.EmotionalState;
            var es2 = npc.Brain?.Emotions;

            float GetTransient(EmotionType type)
            {
                float v1 = es1?.GetEmotionIntensity(type) ?? 0f;
                float v2 = es2?.GetEmotionIntensity(type) ?? 0f;
                return Math.Max(v1, v2);
            }

            // Personality-derived emotional dispositions (full 0.0 - 1.0 range).
            // These represent who the NPC fundamentally IS emotionally.
            float happiness = personality.Sociability * 0.30f + (1f - personality.Aggression) * 0.25f
                            + personality.Patience * 0.20f + (1f - personality.Vengefulness) * 0.15f
                            + personality.Trustworthiness * 0.10f;
            float anger = personality.Aggression * 0.35f + personality.Vengefulness * 0.25f
                        + personality.Impulsiveness * 0.20f + (1f - personality.Patience) * 0.20f;
            float fear = (1f - personality.Courage) * 0.35f + personality.Caution * 0.30f
                       + (1f - personality.Intelligence) * 0.15f + personality.Mysticism * 0.10f
                       + (1f - personality.Ambition) * 0.10f;
            float confidence = personality.Courage * 0.35f + personality.Ambition * 0.25f
                             + personality.Intelligence * 0.20f + (1f - personality.Caution) * 0.20f;
            float sadness = (1f - personality.Sociability) * 0.30f + (1f - personality.Ambition) * 0.25f
                          + (1f - personality.Courage) * 0.20f + personality.Patience * 0.10f;
            float greed = personality.Greed * 0.50f + personality.Ambition * 0.20f
                        + (1f - personality.Trustworthiness) * 0.15f + personality.Impulsiveness * 0.15f;
            float trust = personality.Trustworthiness * 0.30f + personality.Loyalty * 0.25f
                        + personality.Sociability * 0.20f + (1f - personality.Aggression) * 0.15f
                        + personality.Patience * 0.10f;
            float loneliness = (1f - personality.Sociability) * 0.35f + (1f - personality.Loyalty) * 0.25f
                             + (1f - personality.Trustworthiness) * 0.20f + personality.Caution * 0.10f;
            float envy = personality.Greed * 0.25f + personality.Ambition * 0.25f
                       + personality.Vengefulness * 0.20f + (1f - personality.Patience) * 0.15f;
            float pride = personality.Ambition * 0.30f + personality.Courage * 0.20f
                        + (1f - personality.Trustworthiness) * 0.15f + personality.Intelligence * 0.15f;
            float hope = personality.Ambition * 0.30f + personality.Courage * 0.25f
                       + personality.Patience * 0.20f + personality.Intelligence * 0.15f;
            float peace = personality.Patience * 0.30f + personality.Mysticism * 0.25f
                        + (1f - personality.Aggression) * 0.25f + (1f - personality.Impulsiveness) * 0.20f;

            // Antagonistic suppression: contradictory emotions dampen each other.
            // A fearful, angry NPC shouldn't also be maximally happy.
            happiness *= (1f - anger * 0.3f) * (1f - fear * 0.2f) * (1f - sadness * 0.3f);
            confidence *= (1f - fear * 0.4f) * (1f - sadness * 0.2f);
            peace *= (1f - anger * 0.4f) * (1f - fear * 0.3f) * (1f - greed * 0.2f);
            hope *= (1f - sadness * 0.3f) * (1f - fear * 0.2f);
            trust *= (1f - anger * 0.2f) * (1f - fear * 0.15f);
            sadness *= (1f - happiness * 0.3f) * (1f - confidence * 0.2f);
            loneliness *= (1f - trust * 0.3f) * (1f - happiness * 0.2f);

            // Transient emotions from recent events add a small modulation (±15% max)
            float tJoy = GetTransient(EmotionType.Joy);
            float tAnger = GetTransient(EmotionType.Anger);
            float tFear = GetTransient(EmotionType.Fear);
            float tConfidence = GetTransient(EmotionType.Confidence);
            float tSadness = GetTransient(EmotionType.Sadness);
            float tGreed = GetTransient(EmotionType.Greed);
            float tGratitude = GetTransient(EmotionType.Gratitude);
            float tLoneliness = GetTransient(EmotionType.Loneliness);
            float tHope = GetTransient(EmotionType.Hope);
            float tPeace = GetTransient(EmotionType.Peace);

            const float transientWeight = 0.15f;
            happiness = Math.Clamp(happiness + tJoy * transientWeight, 0f, 1f);
            anger = Math.Clamp(anger + tAnger * transientWeight, 0f, 1f);
            fear = Math.Clamp(fear + tFear * transientWeight, 0f, 1f);
            confidence = Math.Clamp(confidence + tConfidence * transientWeight, 0f, 1f);
            sadness = Math.Clamp(sadness + tSadness * transientWeight, 0f, 1f);
            greed = Math.Clamp(greed + tGreed * transientWeight, 0f, 1f);
            trust = Math.Clamp(trust + tGratitude * transientWeight, 0f, 1f);
            loneliness = Math.Clamp(loneliness + tLoneliness * transientWeight, 0f, 1f);
            hope = Math.Clamp(hope + tHope * transientWeight, 0f, 1f);
            peace = Math.Clamp(peace + tPeace * transientWeight, 0f, 1f);

            return new EmotionalStateData
            {
                Happiness = happiness,
                Anger = anger,
                Fear = fear,
                Trust = trust,
                Confidence = confidence,
                Sadness = sadness,
                Greed = greed,
                Loneliness = loneliness,
                Envy = Math.Clamp(envy, 0f, 1f),
                Pride = Math.Clamp(pride, 0f, 1f),
                Hope = hope,
                Peace = peace
            };
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
                    TitleKey = quest.TitleKey,
                    TitleArgs = new List<string>(quest.TitleArgs),
                    CommentKey = quest.CommentKey,
                    CommentArgs = new List<string>(quest.CommentArgs),
                    InitiatorKey = quest.InitiatorKey,
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
                    Monsters = new List<QuestMonsterData>(),
                    // v0.64.1 audit fix: the SP writer copies TargetNPCName but this
                    // online writer didn't -- so the world_state copy of DefeatNPC
                    // bounties (king bounties + Slice 20 NPC-issued quests) couldn't
                    // track their target across a server restart.
                    TargetNPCName = quest.TargetNPCName ?? "",
                    IsPlayerBounty = quest.IsPlayerBounty,
                    BountyGold = quest.BountyGold,   // v1.1.11
                    // v0.62.x Phase 4 (Mercenary board): faction-issued freelance contract fields.
                    IsMercContract = quest.IsMercContract,
                    IssuingFaction = quest.IssuingFaction.HasValue ? (int)quest.IssuingFaction.Value : -1,
                    MercContractTier = quest.MercContractTier
                };

                foreach (var objective in quest.Objectives)
                {
                    questData.Objectives.Add(new QuestObjectiveData
                    {
                        Id = objective.Id,
                        Description = objective.Description,
                        DescriptionKey = objective.DescriptionKey,
                        DescriptionArgs = objective.DescriptionArgs != null
                            ? new List<string>(objective.DescriptionArgs)
                            : new List<string>(),
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
