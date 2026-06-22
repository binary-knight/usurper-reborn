using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Threading.Tasks;
using Microsoft.Data.Sqlite;
using UsurperRemake.Server;

namespace UsurperRemake.Systems
{
    /// <summary>
    /// Tolerant JSON converter that handles empty arrays [] for Dictionary types.
    /// This prevents deserialization crashes when an empty Dictionary was serialized as [].
    /// </summary>
    public class TolerantDictionaryConverter<TKey, TValue> : JsonConverter<Dictionary<TKey, TValue>> where TKey : notnull
    {
        public override Dictionary<TKey, TValue>? Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
        {
            if (reader.TokenType == JsonTokenType.StartArray)
            {
                // Skip the empty array
                while (reader.Read() && reader.TokenType != JsonTokenType.EndArray) { }
                return new Dictionary<TKey, TValue>();
            }
            // Normal dictionary deserialization
            return JsonSerializer.Deserialize<Dictionary<TKey, TValue>>(ref reader);
        }

        public override void Write(Utf8JsonWriter writer, Dictionary<TKey, TValue> value, JsonSerializerOptions options)
        {
            JsonSerializer.Serialize(writer, value);
        }
    }
    /// <summary>
    /// SQLite-based save backend for online multiplayer mode.
    /// All players share a single SQLite database on the server.
    /// Implements both ISaveBackend (core save/load) and IOnlineSaveBackend (online features).
    /// Uses WAL mode for concurrent read/write safety.
    /// </summary>
    public class SqlSaveBackend : IOnlineSaveBackend
    {
        // --- Alt Character Helpers ---
        public static string GetAltKey(string accountUsername) =>
            accountUsername.ToLower() + GameConfig.AltCharacterSuffix;
        public static string GetAccountUsername(string key) =>
            key.EndsWith(GameConfig.AltCharacterSuffix, StringComparison.OrdinalIgnoreCase)
                ? key[..^GameConfig.AltCharacterSuffix.Length] : key;
        public static bool IsAltCharacter(string key) =>
            key.EndsWith(GameConfig.AltCharacterSuffix, StringComparison.OrdinalIgnoreCase);

        private readonly string databasePath;
        private readonly string connectionString;
        private readonly JsonSerializerOptions jsonOptions;

        // v0.60.0 beta-launch Rage event: usernames erased during this server
        // uptime. Cross-checked by WriteGameData so a stray save coming from
        // anywhere (world sim, background timer, fire-and-forget) can't
        // re-INSERT the row that the rage cinematic just deleted. In-memory
        // only; the DB row deletion is the durable signal. Reset on restart,
        // which is fine because by the time the server restarts the row is
        // already gone and any in-flight saves died with the old process.
        private static readonly System.Collections.Concurrent.ConcurrentDictionary<string, byte> RageEventErasedUsernames = new();

        public SqlSaveBackend(string databasePath)
        {
            this.databasePath = databasePath;

            // Ensure directory exists
            var dir = Path.GetDirectoryName(databasePath);
            if (!string.IsNullOrEmpty(dir))
                Directory.CreateDirectory(dir);

            connectionString = $"Data Source={databasePath};Pooling=true";

            jsonOptions = new JsonSerializerOptions
            {
                WriteIndented = false, // compact for database storage
                PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
                IncludeFields = true,
                Converters =
                {
                    new TolerantDictionaryConverter<string, int>(),
                    new TolerantDictionaryConverter<string, long>(),
                }
            };

            InitializeDatabase();
            LoadServerConfigIntoGameConfig();
            PublishServerSettingsSchema();
        }

        /// <summary>
        /// v0.60.8: write the current ServerSettingsRegistry to the
        /// server_config_schema table so the web admin UI can fetch a
        /// canonical schema and render the right input control per setting.
        /// Re-runs on every server start so the schema stays in lockstep
        /// with the binary -- if a setting was added or removed the schema
        /// row reflects that immediately.
        /// </summary>
        private void PublishServerSettingsSchema()
        {
            try
            {
                var serializable = ServerSettingsRegistry.All.Select(d => new
                {
                    key = d.Key,
                    label = d.Label,
                    category = d.Category,
                    type = d.Type.ToString(),
                    defaultValue = d.DefaultValue,
                    minValue = d.MinValue,
                    maxValue = d.MaxValue,
                    maxLength = d.MaxLength,
                    description = d.Description,
                    changeImpact = d.ChangeImpact
                }).ToList();
                string json = System.Text.Json.JsonSerializer.Serialize(serializable);

                using var connection = new SqliteConnection(connectionString);
                connection.Open();
                using var cmd = connection.CreateCommand();
                cmd.CommandText = @"
                    INSERT INTO server_config_schema (id, schema_json, published_at)
                    VALUES (1, @j, datetime('now'))
                    ON CONFLICT(id) DO UPDATE SET schema_json = @j, published_at = datetime('now')";
                cmd.Parameters.AddWithValue("@j", json);
                cmd.ExecuteNonQuery();
                DebugLogger.Instance.LogInfo("SERVER_CONFIG", $"Published settings schema ({serializable.Count} settings).");
            }
            catch (Exception ex)
            {
                DebugLogger.Instance.LogError("SERVER_CONFIG", $"Schema publish failed: {ex.Message}");
            }
        }

        /// <summary>
        /// v0.60.8: drain pending setting changes queued by the web admin UI.
        /// Called by MudServer on a 1s timer. Each queued row UPSERT-applied
        /// the value to server_config (already done by the web process) and
        /// is now applied to the live GameConfig statics via the registry.
        /// Returns the number of rows applied.
        /// </summary>
        public int DrainServerConfigApplyQueue()
        {
            int applied = 0;
            try
            {
                using var connection = new SqliteConnection(connectionString);
                connection.Open();

                List<(long id, string key, string value)> rows = new();
                using (var sel = connection.CreateCommand())
                {
                    sel.CommandText = "SELECT id, key, value FROM server_config_apply_queue ORDER BY id";
                    using var rdr = sel.ExecuteReader();
                    while (rdr.Read())
                    {
                        rows.Add((rdr.GetInt64(0), rdr.GetString(1), rdr.GetString(2)));
                    }
                }
                if (rows.Count == 0) return 0;

                using var tx = connection.BeginTransaction();
                foreach (var (id, key, value) in rows)
                {
                    try
                    {
                        ApplyServerConfigToGameConfig(key, value);
                        applied++;
                        DebugLogger.Instance.LogInfo("SERVER_CONFIG",
                            $"Applied web change: {key} = {value}");
                    }
                    catch (Exception innerEx)
                    {
                        DebugLogger.Instance.LogError("SERVER_CONFIG",
                            $"Apply failed for {key}={value}: {innerEx.Message}");
                    }
                    using var del = connection.CreateCommand();
                    del.Transaction = tx;
                    del.CommandText = "DELETE FROM server_config_apply_queue WHERE id = @id";
                    del.Parameters.AddWithValue("@id", id);
                    del.ExecuteNonQuery();
                }
                tx.Commit();
            }
            catch (Exception ex)
            {
                DebugLogger.Instance.LogError("SERVER_CONFIG", $"Drain queue failed: {ex.Message}");
            }
            return applied;
        }

        /// <summary>
        /// v0.60.7: read every row from server_config and apply it to the
        /// matching GameConfig static field. Runs once after schema init so
        /// admin-set permadeath / resurrection settings survive restart and
        /// are already in effect by the time the first session connects.
        /// Unknown keys are ignored (forward-compat for keys removed in
        /// future releases).
        /// </summary>
        private void LoadServerConfigIntoGameConfig()
        {
            try
            {
                using var connection = new SqliteConnection(connectionString);
                connection.Open();
                using var cmd = connection.CreateCommand();
                cmd.CommandText = "SELECT key, value FROM server_config";
                using var reader = cmd.ExecuteReader();
                while (reader.Read())
                {
                    string key = reader.GetString(0);
                    string value = reader.GetString(1);
                    ApplyServerConfigToGameConfig(key, value);
                }
            }
            catch (Exception ex)
            {
                DebugLogger.Instance.LogWarning("SERVER_CONFIG",
                    $"Failed to load server_config at startup: {ex.Message}. Using GameConfig defaults.");
            }
        }

        /// <summary>
        /// Translate a key/value row into the matching GameConfig static.
        /// Routes through ServerSettingsRegistry so the key->field mapping
        /// lives in one place (the registry) and adding a new tunable doesn't
        /// require touching this file.
        /// </summary>
        private static void ApplyServerConfigToGameConfig(string key, string value)
        {
            ServerSettingsRegistry.ApplyConfigValue(key, value);
        }

        /// <summary>
        /// Read a single server_config value. Returns null if the key has
        /// never been set (caller should use the GameConfig default).
        /// </summary>
        public string? GetServerConfig(string key)
        {
            try
            {
                using var connection = new SqliteConnection(connectionString);
                connection.Open();
                using var cmd = connection.CreateCommand();
                cmd.CommandText = "SELECT value FROM server_config WHERE key = @k";
                cmd.Parameters.AddWithValue("@k", key);
                var result = cmd.ExecuteScalar();
                return result?.ToString();
            }
            catch (Exception ex)
            {
                DebugLogger.Instance.LogWarning("SERVER_CONFIG", $"GetServerConfig({key}) failed: {ex.Message}");
                return null;
            }
        }

        /// <summary>
        /// UPSERT a server_config key/value AND update the matching GameConfig
        /// static immediately so the new value takes effect on the next session
        /// without restart. Records the admin who made the change for audit.
        /// </summary>
        public void SetServerConfig(string key, string value, string? changedBy = null)
        {
            try
            {
                using var connection = new SqliteConnection(connectionString);
                connection.Open();
                using var cmd = connection.CreateCommand();
                cmd.CommandText = @"
                    INSERT INTO server_config (key, value, updated_at, updated_by)
                    VALUES (@k, @v, datetime('now'), @by)
                    ON CONFLICT(key) DO UPDATE SET value = @v, updated_at = datetime('now'), updated_by = @by";
                cmd.Parameters.AddWithValue("@k", key);
                cmd.Parameters.AddWithValue("@v", value);
                cmd.Parameters.AddWithValue("@by", (object?)changedBy ?? DBNull.Value);
                cmd.ExecuteNonQuery();
                ApplyServerConfigToGameConfig(key, value);
                DebugLogger.Instance.LogInfo("SERVER_CONFIG",
                    $"Set {key} = {value} (by {changedBy ?? "system"})");
            }
            catch (Exception ex)
            {
                DebugLogger.Instance.LogError("SERVER_CONFIG", $"SetServerConfig({key}={value}) failed: {ex.Message}");
            }
        }

        /// <summary>
        /// Create all tables if they don't exist. Called once on startup.
        /// </summary>
        private void InitializeDatabase()
        {
            using var connection = new SqliteConnection(connectionString);
            connection.Open();

            // Enable WAL mode for concurrent access safety
            using (var cmd = connection.CreateCommand())
            {
                cmd.CommandText = "PRAGMA journal_mode=WAL;";
                cmd.ExecuteNonQuery();
            }

            using (var cmd = connection.CreateCommand())
            {
                cmd.CommandText = @"
                    CREATE TABLE IF NOT EXISTS players (
                        username TEXT PRIMARY KEY,
                        display_name TEXT NOT NULL,
                        password_hash TEXT NOT NULL DEFAULT '',
                        player_data TEXT NOT NULL,
                        created_at TEXT DEFAULT (datetime('now')),
                        last_login TEXT,
                        last_logout TEXT,
                        total_playtime_minutes INTEGER DEFAULT 0,
                        is_banned INTEGER DEFAULT 0,
                        ban_reason TEXT,
                        last_login_ip TEXT,
                        created_ip TEXT
                    );

                    CREATE TABLE IF NOT EXISTS world_state (
                        key TEXT PRIMARY KEY,
                        value TEXT NOT NULL,
                        version INTEGER DEFAULT 1,
                        updated_at TEXT DEFAULT (datetime('now')),
                        updated_by TEXT
                    );

                    -- v0.60.5: hard-ban table. IP-based ban that drops connections at the
                    -- accept layer (before any auth) and refuses register/login from this
                    -- IP. Populated by BanPlayer when the target is currently online (their
                    -- IP gets captured), or by direct IP-only ban from the admin dashboard.
                    -- associated_username is the username this IP was banned alongside,
                    -- so UnbanPlayer can lift the IP ban when the account ban is lifted.
                    CREATE TABLE IF NOT EXISTS banned_ips (
                        ip_address TEXT PRIMARY KEY,
                        reason TEXT,
                        banned_at TEXT DEFAULT (datetime('now')),
                        banned_by TEXT,
                        associated_username TEXT
                    );
                    CREATE INDEX IF NOT EXISTS idx_banned_ips_username ON banned_ips(associated_username);

                    CREATE TABLE IF NOT EXISTS news (
                        id INTEGER PRIMARY KEY AUTOINCREMENT,
                        message TEXT NOT NULL,
                        category TEXT,
                        player_name TEXT,
                        created_at TEXT DEFAULT (datetime('now'))
                    );

                    CREATE TABLE IF NOT EXISTS online_players (
                        username TEXT PRIMARY KEY,
                        display_name TEXT,
                        location TEXT,
                        node_id TEXT,
                        connection_type TEXT DEFAULT 'Unknown',
                        ip_address TEXT DEFAULT '',
                        connected_at TEXT DEFAULT (datetime('now')),
                        last_heartbeat TEXT DEFAULT (datetime('now'))
                    );

                    CREATE TABLE IF NOT EXISTS messages (
                        id INTEGER PRIMARY KEY AUTOINCREMENT,
                        from_player TEXT NOT NULL,
                        to_player TEXT NOT NULL,
                        message_type TEXT NOT NULL,
                        message TEXT NOT NULL,
                        is_read INTEGER DEFAULT 0,
                        created_at TEXT DEFAULT (datetime('now'))
                    );

                    CREATE TABLE IF NOT EXISTS pvp_log (
                        id INTEGER PRIMARY KEY AUTOINCREMENT,
                        attacker TEXT NOT NULL,
                        defender TEXT NOT NULL,
                        attacker_level INTEGER NOT NULL,
                        defender_level INTEGER NOT NULL,
                        winner TEXT NOT NULL,
                        gold_stolen INTEGER DEFAULT 0,
                        xp_gained INTEGER DEFAULT 0,
                        attacker_hp_remaining INTEGER DEFAULT 0,
                        rounds INTEGER DEFAULT 0,
                        created_at TEXT DEFAULT (datetime('now'))
                    );

                    CREATE TABLE IF NOT EXISTS player_teams (
                        team_name TEXT PRIMARY KEY,
                        password_hash TEXT NOT NULL,
                        created_by TEXT NOT NULL,
                        created_at TEXT DEFAULT (datetime('now')),
                        member_count INTEGER DEFAULT 1,
                        controls_turf INTEGER DEFAULT 0
                    );

                    CREATE TABLE IF NOT EXISTS trade_offers (
                        id INTEGER PRIMARY KEY AUTOINCREMENT,
                        from_player TEXT NOT NULL,
                        to_player TEXT NOT NULL,
                        items_json TEXT DEFAULT '[]',
                        gold INTEGER DEFAULT 0,
                        status TEXT DEFAULT 'pending',
                        message TEXT DEFAULT '',
                        created_at TEXT DEFAULT (datetime('now')),
                        resolved_at TEXT
                    );

                    CREATE TABLE IF NOT EXISTS bounties (
                        id INTEGER PRIMARY KEY AUTOINCREMENT,
                        target_player TEXT NOT NULL,
                        placed_by TEXT NOT NULL,
                        amount INTEGER NOT NULL,
                        placed_at TEXT DEFAULT (datetime('now')),
                        claimed_by TEXT,
                        claimed_at TEXT,
                        status TEXT DEFAULT 'active'
                    );

                    CREATE TABLE IF NOT EXISTS auction_listings (
                        id INTEGER PRIMARY KEY AUTOINCREMENT,
                        seller TEXT NOT NULL,
                        item_name TEXT NOT NULL,
                        item_json TEXT NOT NULL,
                        price INTEGER NOT NULL,
                        listed_at TEXT DEFAULT (datetime('now')),
                        expires_at TEXT NOT NULL,
                        buyer TEXT,
                        status TEXT DEFAULT 'active'
                    );

                    CREATE TABLE IF NOT EXISTS team_wars (
                        id INTEGER PRIMARY KEY AUTOINCREMENT,
                        challenger_team TEXT NOT NULL,
                        defender_team TEXT NOT NULL,
                        status TEXT DEFAULT 'pending',
                        challenger_wins INTEGER DEFAULT 0,
                        defender_wins INTEGER DEFAULT 0,
                        gold_wagered INTEGER DEFAULT 0,
                        started_at TEXT DEFAULT (datetime('now')),
                        finished_at TEXT
                    );

                    CREATE TABLE IF NOT EXISTS world_bosses (
                        id INTEGER PRIMARY KEY AUTOINCREMENT,
                        boss_name TEXT NOT NULL,
                        boss_level INTEGER NOT NULL,
                        max_hp INTEGER NOT NULL,
                        current_hp INTEGER NOT NULL,
                        boss_data_json TEXT DEFAULT '{}',
                        started_at TEXT DEFAULT (datetime('now')),
                        expires_at TEXT NOT NULL,
                        status TEXT DEFAULT 'active'
                    );

                    CREATE TABLE IF NOT EXISTS world_boss_damage (
                        id INTEGER PRIMARY KEY AUTOINCREMENT,
                        boss_id INTEGER NOT NULL,
                        player_name TEXT NOT NULL,
                        damage_dealt INTEGER NOT NULL,
                        hits INTEGER DEFAULT 1,
                        last_hit_at TEXT DEFAULT (datetime('now')),
                        FOREIGN KEY (boss_id) REFERENCES world_bosses(id),
                        UNIQUE(boss_id, player_name)
                    );

                    CREATE TABLE IF NOT EXISTS castle_sieges (
                        id INTEGER PRIMARY KEY AUTOINCREMENT,
                        team_name TEXT NOT NULL,
                        guards_defeated INTEGER DEFAULT 0,
                        total_guards INTEGER NOT NULL,
                        result TEXT DEFAULT 'in_progress',
                        started_at TEXT DEFAULT (datetime('now')),
                        finished_at TEXT
                    );

                    CREATE TABLE IF NOT EXISTS team_upgrades (
                        team_name TEXT NOT NULL,
                        upgrade_type TEXT NOT NULL,
                        level INTEGER DEFAULT 1,
                        invested_gold INTEGER DEFAULT 0,
                        PRIMARY KEY (team_name, upgrade_type)
                    );

                    CREATE TABLE IF NOT EXISTS team_vault (
                        team_name TEXT PRIMARY KEY,
                        gold INTEGER DEFAULT 0
                    );

                    CREATE TABLE IF NOT EXISTS wizard_log (
                        id INTEGER PRIMARY KEY AUTOINCREMENT,
                        wizard_name TEXT NOT NULL,
                        action TEXT NOT NULL,
                        target TEXT,
                        details TEXT,
                        created_at TEXT DEFAULT (datetime('now'))
                    );

                    CREATE TABLE IF NOT EXISTS wizard_flags (
                        username TEXT PRIMARY KEY,
                        is_frozen INTEGER DEFAULT 0,
                        is_muted INTEGER DEFAULT 0,
                        frozen_by TEXT,
                        muted_by TEXT,
                        frozen_at TEXT,
                        muted_at TEXT
                    );

                    -- v0.60.7: server-wide configuration set by the admin console.
                    -- Survives restart. Loaded into GameConfig on backend init so
                    -- runtime checks read from in-memory statics. Admin writes
                    -- through SetServerConfig(key, value) which both UPSERTs the
                    -- row AND updates the matching GameConfig static.
                    CREATE TABLE IF NOT EXISTS server_config (
                        key TEXT PRIMARY KEY,
                        value TEXT NOT NULL,
                        updated_at TEXT DEFAULT (datetime('now')),
                        updated_by TEXT
                    );

                    -- v0.60.8: schema descriptor published by the running game
                    -- process on startup. Single-row JSON blob containing the
                    -- full ServerSettingsRegistry serialized as the form schema
                    -- the web admin UI renders against. Re-published on every
                    -- restart so the schema stays in sync with the binary.
                    CREATE TABLE IF NOT EXISTS server_config_schema (
                        id INTEGER PRIMARY KEY,
                        schema_json TEXT NOT NULL,
                        published_at TEXT DEFAULT (datetime('now'))
                    );

                    -- v0.60.8: apply-queue for live setting changes from the web
                    -- admin UI. The web process can write rows to server_config
                    -- but cannot reach the running game's in-memory GameConfig
                    -- statics. Every 1s the game drains this queue and routes
                    -- each row through ServerSettingsRegistry.ApplyConfigValue
                    -- so changes take effect without restart. Processed rows
                    -- are deleted; a row that fails to parse is logged and
                    -- discarded so a malformed value doesn't block the queue.
                    CREATE TABLE IF NOT EXISTS server_config_apply_queue (
                        id INTEGER PRIMARY KEY AUTOINCREMENT,
                        key TEXT NOT NULL,
                        value TEXT NOT NULL,
                        requested_at TEXT DEFAULT (datetime('now'))
                    );

                    CREATE TABLE IF NOT EXISTS sleeping_players (
                        username TEXT PRIMARY KEY,
                        sleep_location TEXT NOT NULL DEFAULT 'dormitory',
                        sleeping_since TEXT DEFAULT (datetime('now')),
                        is_dead INTEGER DEFAULT 0,
                        guards TEXT DEFAULT '[]',
                        inn_defense_boost INTEGER DEFAULT 0,
                        attack_log TEXT DEFAULT '[]'
                    );

                    -- Tier 1 grace window for accidental deletes (v0.57.22).
                    -- When a player deletes their character, the JSON blob is
                    -- archived here for 7 days. The same SSH account can call
                    -- /restore within that window to bring the character back.
                    -- After expires_at, a periodic cleanup drops the row.
                    CREATE TABLE IF NOT EXISTS deleted_characters (
                        id INTEGER PRIMARY KEY AUTOINCREMENT,
                        username TEXT NOT NULL,
                        display_name TEXT NOT NULL,
                        player_data TEXT NOT NULL,
                        deleted_at TEXT DEFAULT (datetime('now')),
                        expires_at TEXT NOT NULL
                    );
                    CREATE INDEX IF NOT EXISTS idx_deleted_characters_username
                        ON deleted_characters(LOWER(username));
                    CREATE INDEX IF NOT EXISTS idx_deleted_characters_expires
                        ON deleted_characters(expires_at);

                    CREATE TABLE IF NOT EXISTS combat_events (
                        id INTEGER PRIMARY KEY AUTOINCREMENT,
                        player_name TEXT NOT NULL,
                        player_level INTEGER NOT NULL,
                        player_class TEXT NOT NULL,
                        player_max_hp INTEGER NOT NULL,
                        player_str INTEGER NOT NULL,
                        player_dex INTEGER NOT NULL,
                        player_weap_pow INTEGER NOT NULL,
                        player_arm_pow INTEGER NOT NULL,
                        monster_name TEXT,
                        monster_level INTEGER,
                        monster_max_hp INTEGER,
                        monster_str INTEGER,
                        monster_def INTEGER,
                        is_boss INTEGER DEFAULT 0,
                        outcome TEXT NOT NULL,
                        rounds INTEGER DEFAULT 0,
                        damage_dealt INTEGER DEFAULT 0,
                        damage_taken INTEGER DEFAULT 0,
                        xp_gained INTEGER DEFAULT 0,
                        gold_gained INTEGER DEFAULT 0,
                        dungeon_floor INTEGER DEFAULT 0,
                        monster_count INTEGER DEFAULT 1,
                        has_teammates INTEGER DEFAULT 0,
                        created_at TEXT DEFAULT (datetime('now'))
                    );

                    CREATE INDEX IF NOT EXISTS idx_ce_player ON combat_events(player_name, created_at DESC);
                    CREATE INDEX IF NOT EXISTS idx_ce_outcome ON combat_events(outcome, created_at DESC);
                    CREATE INDEX IF NOT EXISTS idx_ce_class ON combat_events(player_class, outcome);

                    CREATE INDEX IF NOT EXISTS idx_news_created ON news(created_at DESC);
                    CREATE INDEX IF NOT EXISTS idx_messages_to ON messages(to_player, is_read);
                    CREATE INDEX IF NOT EXISTS idx_messages_to_type ON messages(to_player, message_type, is_read, created_at DESC);
                    CREATE INDEX IF NOT EXISTS idx_online_heartbeat ON online_players(last_heartbeat);
                    CREATE INDEX IF NOT EXISTS idx_pvp_attacker ON pvp_log(attacker, created_at);
                    CREATE INDEX IF NOT EXISTS idx_pvp_winner ON pvp_log(winner);
                    CREATE INDEX IF NOT EXISTS idx_trade_to ON trade_offers(to_player, status);
                    CREATE INDEX IF NOT EXISTS idx_trade_from ON trade_offers(from_player, status);
                    CREATE INDEX IF NOT EXISTS idx_teams_power ON player_teams(member_count DESC);
                    CREATE INDEX IF NOT EXISTS idx_bounties_target ON bounties(target_player, status);
                    CREATE INDEX IF NOT EXISTS idx_bounties_placer ON bounties(placed_by, status);
                    CREATE INDEX IF NOT EXISTS idx_auction_status ON auction_listings(status, expires_at);
                    CREATE INDEX IF NOT EXISTS idx_auction_seller ON auction_listings(seller, status);
                    CREATE INDEX IF NOT EXISTS idx_world_boss_damage ON world_boss_damage(boss_id, player_name);
                    CREATE INDEX IF NOT EXISTS idx_team_wars_teams ON team_wars(challenger_team, defender_team, status);
                    CREATE INDEX IF NOT EXISTS idx_wizard_log_created ON wizard_log(created_at DESC);
                    CREATE INDEX IF NOT EXISTS idx_wizard_log_wizard ON wizard_log(wizard_name, created_at DESC);

                    CREATE TABLE IF NOT EXISTS admin_commands (
                        id INTEGER PRIMARY KEY AUTOINCREMENT,
                        command TEXT NOT NULL,
                        target_username TEXT,
                        args TEXT,
                        status TEXT DEFAULT 'pending',
                        result TEXT,
                        created_at TEXT DEFAULT (datetime('now')),
                        executed_at TEXT,
                        created_by TEXT DEFAULT 'admin'
                    );

                    CREATE TABLE IF NOT EXISTS snoop_buffer (
                        id INTEGER PRIMARY KEY AUTOINCREMENT,
                        target_username TEXT NOT NULL,
                        line TEXT NOT NULL,
                        created_at TEXT DEFAULT (datetime('now'))
                    );
                    CREATE INDEX IF NOT EXISTS idx_snoop_target ON snoop_buffer(target_username, id);
                    CREATE INDEX IF NOT EXISTS idx_admin_cmd_status ON admin_commands(status, id);

                    -- v0.60.4: bot detection snapshot. Single-row table (id=1) holding
                    -- the latest BotDetectionSystem.Snapshot() output as JSON. Updated
                    -- periodically by the game process; read by the admin dashboard.
                    -- Table is empty when no players are actively in combat.
                    CREATE TABLE IF NOT EXISTS bot_detection_snapshot (
                        id INTEGER PRIMARY KEY CHECK (id = 1),
                        snapshot_at TEXT NOT NULL,
                        snapshot_json TEXT NOT NULL
                    );

                    -- v0.61.2 Phase 1 of the NPC AI project: every NPC action the
                    -- world sim takes is logged here so we can measure baseline
                    -- behavior (survival rates by class, gold accumulation, dungeon
                    -- success rates) BEFORE the AI subset lands. Once AI NPCs ship,
                    -- the is_ai_driven column lets us split the rollup and compare
                    -- AI vs heuristic cohorts on the same metrics. Pruned to last
                    -- 30 days on a periodic sweep to keep table size bounded.
                    CREATE TABLE IF NOT EXISTS npc_decision_log (
                        id INTEGER PRIMARY KEY AUTOINCREMENT,
                        npc_name TEXT NOT NULL,
                        npc_level INTEGER NOT NULL,
                        npc_class TEXT NOT NULL,
                        action TEXT NOT NULL,
                        location_before TEXT,
                        location_after TEXT,
                        outcome TEXT,
                        gold_delta INTEGER DEFAULT 0,
                        xp_delta INTEGER DEFAULT 0,
                        hp_before INTEGER DEFAULT 0,
                        hp_after INTEGER DEFAULT 0,
                        is_ai_driven INTEGER DEFAULT 0,
                        decision_source TEXT DEFAULT 'sim',
                        created_at TEXT DEFAULT (datetime('now'))
                    );
                    CREATE INDEX IF NOT EXISTS idx_npc_decision_log_npc ON npc_decision_log(npc_name, created_at DESC);
                    CREATE INDEX IF NOT EXISTS idx_npc_decision_log_action_outcome ON npc_decision_log(action, outcome);
                    CREATE INDEX IF NOT EXISTS idx_npc_decision_log_created ON npc_decision_log(created_at DESC);

                    -- v0.61.5: Items bequeathed to a player team leader when a team
                    -- NPC dies of old age. Queue model so the leader can be offline
                    -- when the death happens; items are delivered on next login.
                    -- Each row is one item from the NPC's equipped or inventory list,
                    -- serialized via the standard Item JSON format used in player saves.
                    CREATE TABLE IF NOT EXISTS pending_inheritance (
                        id INTEGER PRIMARY KEY AUTOINCREMENT,
                        player_username TEXT NOT NULL,
                        source_npc_name TEXT NOT NULL,
                        item_json TEXT NOT NULL,
                        gold_amount INTEGER DEFAULT 0,
                        created_at TEXT DEFAULT (datetime('now'))
                    );
                    CREATE INDEX IF NOT EXISTS idx_pending_inheritance_player ON pending_inheritance(player_username);

                    -- v0.65.0: player-to-player bank wire transfers. Queue model so the
                    -- recipient can be offline; the amount (already net of the bank fee)
                    -- is auto-deposited to their bank account on next login. The recipient's
                    -- OWN session applies the credit, so there is no cross-session save write.
                    CREATE TABLE IF NOT EXISTS pending_gold_transfers (
                        id INTEGER PRIMARY KEY AUTOINCREMENT,
                        recipient_username TEXT NOT NULL,
                        sender_display TEXT NOT NULL,
                        amount INTEGER NOT NULL,
                        note TEXT DEFAULT '',
                        created_at TEXT DEFAULT (datetime('now'))
                    );
                    CREATE INDEX IF NOT EXISTS idx_pending_gold_transfers_recipient ON pending_gold_transfers(recipient_username);

                    -- v0.64.0 Brain v2 Slice 10: LLM moment telemetry. One row
                    -- per LLM call attempt (success OR fallback). Powers the
                    -- balance dashboard's LLM stats card so the sysop can
                    -- watch token spend, success rate, and rendered output
                    -- without grepping the debug log. Pruned to last 30 days.
                    -- moment_type values: 'avenge' | 'death_epitaph' | 'personality_summary'
                    CREATE TABLE IF NOT EXISTS llm_usage (
                        id INTEGER PRIMARY KEY AUTOINCREMENT,
                        moment_type TEXT NOT NULL,
                        npc_name TEXT,
                        succeeded INTEGER NOT NULL DEFAULT 0,
                        prompt_tokens INTEGER DEFAULT 0,
                        completion_tokens INTEGER DEFAULT 0,
                        total_tokens INTEGER DEFAULT 0,
                        response_ms INTEGER DEFAULT 0,
                        rendered_text TEXT,
                        failure_reason TEXT,
                        created_at TEXT DEFAULT (datetime('now'))
                    );
                    CREATE INDEX IF NOT EXISTS idx_llm_usage_created ON llm_usage(created_at DESC);
                    CREATE INDEX IF NOT EXISTS idx_llm_usage_moment ON llm_usage(moment_type, created_at DESC);

                    -- v1.0 release prep (B1a): onboarding funnel telemetry.
                    -- One row per (username, milestone), written fire-and-forget
                    -- at five seams: account_created (RegisterPlayer),
                    -- character_created (first online save of a new character),
                    -- reached_town (first Main Street entry), first_kill
                    -- (MKills 0 -> 1), second_login (first login on a later
                    -- calendar day). UNIQUE(username, event) + INSERT OR IGNORE
                    -- makes every write idempotent, so hot paths can fire
                    -- without once-only bookkeeping. Diagnoses the new-account
                    -- bounce (87% of accounts played zero minutes as of Beta)
                    -- by showing exactly which step loses people, split by
                    -- connection type (Web drive-bys vs Steam buyers are
                    -- different populations).
                    CREATE TABLE IF NOT EXISTS onboarding_events (
                        id INTEGER PRIMARY KEY AUTOINCREMENT,
                        username TEXT NOT NULL,
                        event TEXT NOT NULL,
                        connection_type TEXT,
                        created_at TEXT DEFAULT (datetime('now')),
                        UNIQUE(username, event)
                    );
                    CREATE INDEX IF NOT EXISTS idx_onboarding_event ON onboarding_events(event, created_at DESC);
                ";
                cmd.ExecuteNonQuery();
            }

            // Migration: add connection_type column to existing online_players tables
            try
            {
                using var migCmd = connection.CreateCommand();
                migCmd.CommandText = "ALTER TABLE online_players ADD COLUMN connection_type TEXT DEFAULT 'Unknown';";
                migCmd.ExecuteNonQuery();
            }
            catch { /* Column already exists - expected */ }

            // Migration: add gold_collected column to auction_listings
            try
            {
                using var migCmd2 = connection.CreateCommand();
                migCmd2.CommandText = "ALTER TABLE auction_listings ADD COLUMN gold_collected INTEGER DEFAULT 0;";
                migCmd2.ExecuteNonQuery();
            }
            catch { /* Column already exists - expected */ }

            // Migration: add ip_address column to online_players
            try
            {
                using var migCmd = connection.CreateCommand();
                migCmd.CommandText = "ALTER TABLE online_players ADD COLUMN ip_address TEXT DEFAULT '';";
                migCmd.ExecuteNonQuery();
            }
            catch { /* Column already exists - expected */ }

            // v0.60.5: add last_login_ip column to players for IP-ban tracking
            try
            {
                using var migCmd = connection.CreateCommand();
                migCmd.CommandText = "ALTER TABLE players ADD COLUMN last_login_ip TEXT;";
                migCmd.ExecuteNonQuery();
            }
            catch { /* Column already exists - expected */ }

            // v0.60.5: add created_ip column for per-IP registration rate limiting
            try
            {
                using var migCmd = connection.CreateCommand();
                migCmd.CommandText = "ALTER TABLE players ADD COLUMN created_ip TEXT;";
                migCmd.ExecuteNonQuery();
            }
            catch { /* Column already exists - expected */ }

            // Migration: add wizard_level column to existing players table
            try
            {
                using var migCmd = connection.CreateCommand();
                migCmd.CommandText = "ALTER TABLE players ADD COLUMN wizard_level INTEGER DEFAULT 0;";
                migCmd.ExecuteNonQuery();
            }
            catch { /* Column already exists - expected */ }

            // Migration: add screen_reader and language columns (account-level preferences)
            try
            {
                using var migCmd = connection.CreateCommand();
                migCmd.CommandText = "ALTER TABLE players ADD COLUMN screen_reader INTEGER DEFAULT 0;";
                migCmd.ExecuteNonQuery();
            }
            catch { /* Column already exists - expected */ }
            try
            {
                using var migCmd = connection.CreateCommand();
                migCmd.CommandText = "ALTER TABLE players ADD COLUMN language TEXT DEFAULT 'en';";
                migCmd.ExecuteNonQuery();
            }
            catch { /* Column already exists - expected */ }

            // v0.63.2: add decision_source column to npc_decision_log so future
            // telemetry can distinguish world-sim writes ('sim') from external
            // killers (player murders, PvP, world events). Defaults to 'sim'
            // for existing rows since pre-v0.63.2 the only writer was the
            // world-sim dispatcher.
            try
            {
                using var migCmd = connection.CreateCommand();
                migCmd.CommandText = "ALTER TABLE npc_decision_log ADD COLUMN decision_source TEXT DEFAULT 'sim';";
                migCmd.ExecuteNonQuery();
            }
            catch { /* Column already exists - expected */ }

            DebugLogger.Instance.LogInfo("SQL", $"Database initialized at {databasePath}");
        }

        private SqliteConnection OpenConnection()
        {
            var connection = new SqliteConnection(connectionString);
            connection.Open();
            return connection;
        }

        // =====================================================================
        // ISaveBackend Implementation (Core save/load)
        // =====================================================================

        public async Task<bool> WriteGameData(string playerName, SaveGameData data)
        {
            try
            {
                // v0.60.0 beta-launch Rage event guard. If the calling session has
                // been rage-killed, refuse the save outright. Without this, a
                // fire-and-forget save started before the cinematic could complete
                // AFTER the row deletion, re-INSERTing the row with an empty
                // password_hash. The user then sees "Incorrect password" on
                // re-login instead of the intended "Unknown username." Cross-check
                // is also done by deleted username -- if a save fires for a
                // username that was rage-erased earlier in this server uptime,
                // refuse (covers world-sim or background saves that don't have a
                // SessionContext).
                var ragedSession = UsurperRemake.Server.SessionContext.Current?.IsRageKilled == true;
                if (ragedSession || RageEventErasedUsernames.ContainsKey(playerName.ToLower()))
                {
                    DebugLogger.Instance.LogWarning("RAGE_EVENT",
                        $"Suppressing save for '{playerName}' (session rage-killed). Row will not be re-created.");
                    return false;
                }

                var json = JsonSerializer.Serialize(data, jsonOptions);
                var displayName = data.Player?.Name2 ?? data.Player?.Name1 ?? playerName;
                var normalizedUsername = playerName.ToLower();

                using var connection = OpenConnection();
                using var cmd = connection.CreateCommand();
                // Persist account-level preferences so they apply before character load
                int screenReaderFlag = data.Player?.ScreenReaderMode == true ? 1 : 0;
                string language = data.Player?.Language ?? "en";

                cmd.CommandText = @"
                    INSERT INTO players (username, display_name, player_data, last_login, screen_reader, language)
                    VALUES (@username, @displayName, @data, datetime('now'), @screenReader, @language)
                    ON CONFLICT(username) DO UPDATE SET
                        display_name = @displayName,
                        player_data = @data,
                        last_login = datetime('now'),
                        screen_reader = @screenReader,
                        language = @language;
                ";
                cmd.Parameters.AddWithValue("@username", normalizedUsername);
                cmd.Parameters.AddWithValue("@displayName", displayName);
                cmd.Parameters.AddWithValue("@data", json);
                cmd.Parameters.AddWithValue("@screenReader", screenReaderFlag);
                cmd.Parameters.AddWithValue("@language", language);

                try
                {
                    await cmd.ExecuteNonQueryAsync();
                }
                catch (Microsoft.Data.Sqlite.SqliteException ex) when (ex.SqliteErrorCode == 19)
                {
                    // UNIQUE constraint on display_name — another player already has this name.
                    // Save without updating display_name (keep existing display_name in DB).
                    cmd.CommandText = @"
                        UPDATE players SET
                            player_data = @data,
                            last_login = datetime('now'),
                            screen_reader = @screenReader,
                            language = @language
                        WHERE LOWER(username) = LOWER(@username);
                    ";
                    await cmd.ExecuteNonQueryAsync();
                    DebugLogger.Instance.LogWarning("SQL", $"Display name '{displayName}' conflicts with another player — saved data without updating display_name for '{playerName}'");
                }
                DebugLogger.Instance.LogDebug("SQL", $"Saved game data for '{playerName}'");
                return true;
            }
            catch (Exception ex)
            {
                DebugLogger.Instance.LogSystemError("SQL", $"Failed to write game data: {ex.Message}", ex.StackTrace);
                return false;
            }
        }

        public async Task<SaveGameData?> ReadGameData(string playerName)
        {
            try
            {
                using var connection = OpenConnection();
                using var cmd = connection.CreateCommand();
                // ORDER BY LENGTH DESC to prefer the record with actual save data over empty '{}' registration records
                cmd.CommandText = "SELECT player_data FROM players WHERE LOWER(username) = LOWER(@username) AND is_banned = 0 ORDER BY LENGTH(player_data) DESC LIMIT 1;";
                cmd.Parameters.AddWithValue("@username", playerName);

                var result = await cmd.ExecuteScalarAsync();
                if (result == null || result == DBNull.Value)
                {
                    DebugLogger.Instance.LogDebug("SQL", $"No save data found for '{playerName}'");
                    return null;
                }

                var json = (string)result;
                // Skip empty registration records
                if (json == "{}" || string.IsNullOrWhiteSpace(json))
                {
                    DebugLogger.Instance.LogDebug("SQL", $"Only empty registration record for '{playerName}'");
                    return null;
                }

                var saveData = JsonSerializer.Deserialize<SaveGameData>(json, jsonOptions);

                if (saveData == null)
                {
                    DebugLogger.Instance.LogError("SQL", $"Failed to deserialize save data for '{playerName}'");
                    return null;
                }

                if (saveData.Version < GameConfig.MinSaveVersion)
                {
                    DebugLogger.Instance.LogError("SQL", $"Save version {saveData.Version} too old (minimum: {GameConfig.MinSaveVersion})");
                    return null;
                }

                DebugLogger.Instance.LogDebug("SQL", $"Loaded game data for '{playerName}' (v{saveData.Version})");
                return saveData;
            }
            catch (Exception ex)
            {
                DebugLogger.Instance.LogSystemError("SQL", $"Failed to read game data: {ex.Message}", ex.StackTrace);
                return null;
            }
        }

        /// <summary>
        /// Lightweight lookup of a player's team name from their save JSON without deserializing the full save.
        /// </summary>
        public string GetPlayerTeamName(string username)
        {
            try
            {
                using var connection = OpenConnection();
                using var cmd = connection.CreateCommand();
                cmd.CommandText = @"
                    SELECT json_extract(player_data, '$.Player.Team')
                    FROM players
                    WHERE LOWER(username) = LOWER(@username) AND is_banned = 0
                    ORDER BY LENGTH(player_data) DESC LIMIT 1;";
                cmd.Parameters.AddWithValue("@username", username);
                var result = cmd.ExecuteScalar();
                return result as string ?? "";
            }
            catch
            {
                return "";
            }
        }

        public async Task<SaveGameData?> ReadGameDataByFileName(string fileName)
        {
            // In SQL mode, "fileName" is treated as a username
            return await ReadGameData(fileName);
        }

        public bool GameDataExists(string playerName)
        {
            try
            {
                using var connection = OpenConnection();
                using var cmd = connection.CreateCommand();
                // Only count records with actual save data (not empty '{}' registration records)
                cmd.CommandText = "SELECT COUNT(*) FROM players WHERE LOWER(username) = LOWER(@username) AND player_data != '{}' AND LENGTH(player_data) > 2;";
                cmd.Parameters.AddWithValue("@username", playerName);
                var count = (long)(cmd.ExecuteScalar() ?? 0);
                return count > 0;
            }
            catch (Exception ex)
            {
                DebugLogger.Instance.LogError("SQL", $"Failed to check game data exists: {ex.Message}");
                return false;
            }
        }

        public bool DeleteGameData(string playerName) => DeleteGameData(playerName, bypassArchive: false);

        /// <summary>
        /// v0.60.5: purge all per-player references from shared world-state tables
        /// at permadeath. Called by PermadeathHelper.ExecutePermadeath BEFORE the
        /// player_data clear so the player's identity is still resolvable for any
        /// joined queries in subscribed hooks. Does NOT touch the players row
        /// itself (that's DeleteGameData's job), the audit log (wizard_log), or
        /// the historical PvP log (pvp_log) -- those should survive permadeath.
        ///
        /// Player report (Rage): "lost all 4 lives, made a new char, came back
        /// in my guild still, actually still worshiping the same god." This
        /// addresses that class of leak: the player_data was cleared but the
        /// guild_members row, world_boss_damage row, bounties, etc. all still
        /// referenced the same username, so the new character inherited them.
        ///
        /// v0.65.0: displayName (Character.Name2) is also taken so the in-memory
        /// PermadeathPurgeHook can clear systems keyed by display name (god
        /// worship, relationships) rather than the account username -- those two
        /// differ for many players, and keying the purge by username alone let
        /// that state survive and re-bind to a same-name recreation. Falls back
        /// to username when displayName is null.
        /// </summary>
        public void PurgePlayerWorldState(string username, string? displayName = null)
        {
            if (string.IsNullOrWhiteSpace(username)) return;
            try
            {
                using var connection = OpenConnection();
                using var tx = connection.BeginTransaction();

                // Direct per-username tables. All use LOWER() comparison since
                // some tables stored mixed case in early alpha.
                ExecPurge(connection, tx, "guild_members",     "LOWER(username) = LOWER(@u)", username);
                ExecPurge(connection, tx, "online_players",    "LOWER(username) = LOWER(@u)", username);
                ExecPurge(connection, tx, "sleeping_players",  "LOWER(username) = LOWER(@u)", username);
                ExecPurge(connection, tx, "wizard_flags",      "LOWER(username) = LOWER(@u)", username);

                // Multi-column tables: the username can appear as sender/recipient,
                // attacker/defender, etc. Clear all of them.
                ExecPurge(connection, tx, "messages",          "LOWER(from_player) = LOWER(@u) OR LOWER(to_player) = LOWER(@u)", username);
                ExecPurge(connection, tx, "trade_offers",      "LOWER(from_player) = LOWER(@u) OR LOWER(to_player) = LOWER(@u)", username);
                ExecPurge(connection, tx, "bounties",          "LOWER(target_player) = LOWER(@u) OR LOWER(placed_by) = LOWER(@u) OR LOWER(claimed_by) = LOWER(@u)", username);
                ExecPurge(connection, tx, "auction_listings",  "LOWER(seller) = LOWER(@u) OR LOWER(buyer) = LOWER(@u)", username);
                ExecPurge(connection, tx, "world_boss_damage", "LOWER(player_name) = LOWER(@u)", username);

                // v0.65.0: pvp_log was deliberately excluded in v0.60.5 ("history
                // should survive permadeath"), but that conflated distinct
                // characters on the same account -- a permadied character's arena
                // wins re-joined to a same-account/same-name recreation on the
                // leaderboard (the winner column is the lowercase account name,
                // re-joined to the CURRENT display name). Purge by WINNER ONLY:
                // that zeroes the erased character's win count so a recreation
                // inherits nothing, while preserving rows where this character
                // was the LOSER -- those credit a win to a STILL-LIVING opponent
                // and must not be deleted out from under them.
                ExecPurge(connection, tx, "pvp_log", "LOWER(winner) = LOWER(@u)", username);

                tx.Commit();
                DebugLogger.Instance.LogInfo("PERMADEATH",
                    $"Purged shared world-state references for '{username}' (guild, bounties, trades, auctions, etc.)");

                // Fire the in-memory cleanup hook for systems that hold per-player
                // state outside SQLite (GodSystem, RelationshipSystem, etc.).
                // Caught broadly because any one hook throwing shouldn't block
                // the rest of the permadeath flow.
                try { PermadeathPurgeHook?.Invoke(username, displayName); }
                catch (Exception hookEx)
                {
                    DebugLogger.Instance.LogWarning("PERMADEATH",
                        $"PermadeathPurgeHook threw for '{username}': {hookEx.Message}");
                }
            }
            catch (Exception ex)
            {
                DebugLogger.Instance.LogError("PERMADEATH",
                    $"PurgePlayerWorldState failed for '{username}': {ex.Message}");
            }
        }

        private static void ExecPurge(SqliteConnection conn, SqliteTransaction tx, string table, string whereClause, string username)
        {
            try
            {
                using var cmd = conn.CreateCommand();
                cmd.Transaction = tx;
                cmd.CommandText = $"DELETE FROM {table} WHERE {whereClause};";
                cmd.Parameters.AddWithValue("@u", username);
                int rows = cmd.ExecuteNonQuery();
                if (rows > 0)
                    DebugLogger.Instance.LogInfo("PERMADEATH", $"  {table}: removed {rows} row(s) for '{username}'");
            }
            catch (Exception ex)
            {
                // Log and continue — one missing/changed table shouldn't abort the
                // whole purge transaction. Worst case: a row leaks; a second permadeath
                // for the same identity would have another shot at it.
                DebugLogger.Instance.LogWarning("PERMADEATH",
                    $"  {table}: purge failed for '{username}': {ex.Message}");
            }
        }

        /// <summary>
        /// v0.60.5: hook for in-memory per-player state cleanup. Subscribed by
        /// game-side systems at startup (GodSystem clears worship, etc.) so
        /// PurgePlayerWorldState can fan out without a hard reference. Static
        /// to mirror KickActiveSessionHook.
        /// </summary>
        // v0.65.0: second arg is the character display name (Name2) for hooks
        // that key per-player state by display name (god worship, relationships)
        // rather than the account username.
        public static Action<string, string?>? PermadeathPurgeHook { get; set; }

        /// <summary>
        /// Delete a player's character. Default behavior archives to deleted_characters
        /// for 7-day /restore window (v0.57.22 Tier 1). Pass bypassArchive=true to
        /// skip the archive entirely for irreversible deletes (v0.60.0 beta-launch
        /// Rage event uses this to make the divine erasure final).
        /// </summary>
        public bool DeleteGameData(string playerName, bool bypassArchive)
        {
            try
            {
                using var connection = OpenConnection();

                // v0.57.22 Tier 1: archive the player_data into deleted_characters
                // BEFORE clearing it, so the same SSH account can /restore within
                // 7 days. Skip the archive if player_data is already empty (no
                // point archiving '{}'). Best-effort: if archive fails, the
                // delete still proceeds. Player loses no MORE than they would
                // have lost in the pre-archive era. v0.60.0: the bypassArchive
                // path skips this block entirely for genuinely irreversible deletes.
                if (!bypassArchive)
                {
                    try
                    {
                        using var archiveCmd = connection.CreateCommand();
                        archiveCmd.CommandText = @"
                            INSERT INTO deleted_characters (username, display_name, player_data, expires_at)
                            SELECT username, display_name, player_data,
                                   datetime('now', '+7 days')
                              FROM players
                             WHERE LOWER(username) = LOWER(@username)
                               AND player_data IS NOT NULL
                               AND player_data != '{}'
                               AND length(player_data) > 4;";
                        archiveCmd.Parameters.AddWithValue("@username", playerName);
                        int archived = archiveCmd.ExecuteNonQuery();
                        if (archived > 0)
                        {
                            DebugLogger.Instance.LogInfo("SAVE",
                                $"Archived '{playerName}' to deleted_characters (7-day grace).");
                        }
                    }
                    catch (Exception archiveEx)
                    {
                        DebugLogger.Instance.LogWarning("SAVE",
                            $"deleted_characters archive failed for '{playerName}': {archiveEx.Message}. Proceeding with delete anyway.");
                    }
                }

                // Opportunistic cleanup of expired entries (one query per
                // delete keeps the table from growing unbounded; cheap because
                // of the expires_at index).
                try
                {
                    using var purgeCmd = connection.CreateCommand();
                    purgeCmd.CommandText = "DELETE FROM deleted_characters WHERE expires_at < datetime('now');";
                    purgeCmd.ExecuteNonQuery();
                }
                catch { /* best effort */ }

                using var cmd = connection.CreateCommand();
                // Clear player_data instead of deleting the row — preserves password_hash,
                // ban status, and other account-level fields. A row with '{}' player_data
                // is treated as "no save" by ReadGameData.
                cmd.CommandText = "UPDATE players SET player_data = '{}' WHERE LOWER(username) = LOWER(@username);";
                cmd.Parameters.AddWithValue("@username", playerName);
                var affected = cmd.ExecuteNonQuery();
                return affected > 0;
            }
            catch (Exception ex)
            {
                DebugLogger.Instance.LogError("SQL", $"Failed to delete game data: {ex.Message}");
                return false;
            }
        }

        /// <summary>
        /// v0.60.0 beta-launch Rage event: nuke the entire account row, not just
        /// the player_data column. After this runs, AuthenticatePlayer will return
        /// "Unknown username" because the row no longer exists. Used by the Rage
        /// god to make the erasure absolute -- character gone, account gone,
        /// password_hash gone. The player cannot log back in with their old
        /// credentials. They can re-register the same username, but it would be
        /// a brand new account with no connection to the old one.
        ///
        /// Also clears related session/state tables (online_players,
        /// sleeping_players, online_state) so the divine erasure is consistent
        /// across all server views.
        /// </summary>
        /// <summary>
        /// v0.60.0 beta: mark a username as erased in the in-memory blacklist
        /// without doing the actual DB delete. Used by PermadeathHelper.
        /// ExecutePermadeath which uses the soft-delete (DeleteGameData with
        /// archive=true) but still needs WriteGameData saves to be blocked
        /// during/after the cinematic so a fire-and-forget autosave or
        /// disconnect-save can't re-INSERT the player_data the soft-delete
        /// just cleared. Idempotent.
        /// </summary>
        public static void MarkUsernameErased(string username)
        {
            if (!string.IsNullOrEmpty(username))
                RageEventErasedUsernames[username.ToLower()] = 1;
        }

        /// <summary>
        /// v0.60.0 beta: clear the in-memory erasure mark. Called when a player
        /// consciously creates a NEW character on the same SSH account after
        /// being permadied -- otherwise WriteGameData would keep blocking
        /// their saves for the rest of the server uptime, and the new
        /// character's first save throws "Failed to save game!" (player
        /// report). Also called on session.IsRageKilled clear if that ever
        /// gets used.
        /// </summary>
        public static void ClearErasedMark(string username)
        {
            if (!string.IsNullOrEmpty(username))
                RageEventErasedUsernames.TryRemove(username.ToLower(), out _);
        }

        public bool DeleteAccountCompletely(string username)
        {
            // Mark the username in the process-wide blacklist FIRST. WriteGameData
            // checks this set and refuses to save for any matching username, so
            // a save in flight from another thread can't beat us to the row.
            RageEventErasedUsernames[username.ToLower()] = 1;

            try
            {
                using var connection = OpenConnection();

                // Lazy-create the rage_victims memorial table so we can ship this
                // without a schema-coordinated server restart. One row per erased
                // account, recording who Rage took and when.
                try
                {
                    using var schema = connection.CreateCommand();
                    schema.CommandText = @"
                        CREATE TABLE IF NOT EXISTS rage_victims (
                            id INTEGER PRIMARY KEY AUTOINCREMENT,
                            username TEXT NOT NULL,
                            display_name TEXT,
                            level INTEGER,
                            class_name TEXT,
                            erased_at TEXT DEFAULT (datetime('now'))
                        );";
                    schema.ExecuteNonQuery();
                }
                catch (Exception ex) { DebugLogger.Instance.LogWarning("SQL", $"rage_victims schema create failed: {ex.Message}"); }

                // Capture display_name / level / class BEFORE we drop the row,
                // so the memorial entry has more than just a lowercase username.
                string? capturedDisplayName = null;
                int? capturedLevel = null;
                string? capturedClass = null;
                try
                {
                    using var peek = connection.CreateCommand();
                    peek.CommandText = "SELECT display_name, player_data FROM players WHERE LOWER(username) = LOWER(@u);";
                    peek.Parameters.AddWithValue("@u", username);
                    using var rd = peek.ExecuteReader();
                    if (rd.Read())
                    {
                        capturedDisplayName = rd.IsDBNull(0) ? null : rd.GetString(0);
                        if (!rd.IsDBNull(1))
                        {
                            var json = rd.GetString(1);
                            if (json != "{}" && !string.IsNullOrWhiteSpace(json))
                            {
                                try
                                {
                                    var saveData = JsonSerializer.Deserialize<SaveGameData>(json, jsonOptions);
                                    if (saveData?.Player != null)
                                    {
                                        capturedLevel = saveData.Player.Level;
                                        capturedClass = saveData.Player.Class.ToString();
                                        if (string.IsNullOrEmpty(capturedDisplayName))
                                            capturedDisplayName = saveData.Player.Name2 ?? saveData.Player.Name1;
                                    }
                                }
                                catch { /* malformed save -- still record what we have */ }
                            }
                        }
                    }
                }
                catch (Exception ex) { DebugLogger.Instance.LogWarning("SQL", $"rage_victims peek failed for '{username}': {ex.Message}"); }

                // Insert the memorial row. Best-effort; a failure here doesn't
                // prevent the actual account deletion.
                try
                {
                    using var memorial = connection.CreateCommand();
                    memorial.CommandText = @"
                        INSERT INTO rage_victims (username, display_name, level, class_name)
                        VALUES (@u, @d, @l, @c);";
                    memorial.Parameters.AddWithValue("@u", username);
                    memorial.Parameters.AddWithValue("@d", (object?)capturedDisplayName ?? DBNull.Value);
                    memorial.Parameters.AddWithValue("@l", (object?)capturedLevel ?? DBNull.Value);
                    memorial.Parameters.AddWithValue("@c", (object?)capturedClass ?? DBNull.Value);
                    memorial.ExecuteNonQuery();
                }
                catch (Exception ex) { DebugLogger.Instance.LogWarning("SQL", $"rage_victims insert failed for '{username}': {ex.Message}"); }

                // Cleanup transient/session tables first (best-effort each).
                try
                {
                    using var c1 = connection.CreateCommand();
                    c1.CommandText = "DELETE FROM online_players WHERE LOWER(username) = LOWER(@u);";
                    c1.Parameters.AddWithValue("@u", username);
                    c1.ExecuteNonQuery();
                }
                catch (Exception ex) { DebugLogger.Instance.LogWarning("SQL", $"online_players cleanup failed for '{username}': {ex.Message}"); }

                try
                {
                    using var c2 = connection.CreateCommand();
                    c2.CommandText = "DELETE FROM sleeping_players WHERE LOWER(username) = LOWER(@u);";
                    c2.Parameters.AddWithValue("@u", username);
                    c2.ExecuteNonQuery();
                }
                catch (Exception ex) { DebugLogger.Instance.LogWarning("SQL", $"sleeping_players cleanup failed for '{username}': {ex.Message}"); }

                try
                {
                    using var c3 = connection.CreateCommand();
                    c3.CommandText = "DELETE FROM deleted_characters WHERE LOWER(username) = LOWER(@u);";
                    c3.Parameters.AddWithValue("@u", username);
                    c3.ExecuteNonQuery();
                }
                catch (Exception ex) { DebugLogger.Instance.LogWarning("SQL", $"deleted_characters cleanup failed for '{username}': {ex.Message}"); }

                // The kill: drop the account row itself.
                using var cmd = connection.CreateCommand();
                cmd.CommandText = "DELETE FROM players WHERE LOWER(username) = LOWER(@u);";
                cmd.Parameters.AddWithValue("@u", username);
                var affected = cmd.ExecuteNonQuery();

                DebugLogger.Instance.LogWarning("RAGE_EVENT",
                    $"Hard-deleted account '{username}' (display='{capturedDisplayName}', lv={capturedLevel}, class={capturedClass}). Memorialized in rage_victims.");

                return affected > 0;
            }
            catch (Exception ex)
            {
                DebugLogger.Instance.LogError("SQL", $"DeleteAccountCompletely failed for '{username}': {ex.Message}");
                return false;
            }
        }

        /// <summary>
        /// Look up the most recent archived character for an SSH account, if
        /// one exists within the 7-day grace window. Returns null if nothing
        /// to restore. Used by the /restore slash command.
        /// </summary>
        public DeletedCharacterInfo? GetMostRecentDeletedCharacter(string username)
        {
            try
            {
                using var connection = OpenConnection();
                using var cmd = connection.CreateCommand();
                cmd.CommandText = @"
                    SELECT id, display_name, player_data, deleted_at, expires_at
                      FROM deleted_characters
                     WHERE LOWER(username) = LOWER(@username)
                       AND expires_at >= datetime('now')
                     ORDER BY deleted_at DESC
                     LIMIT 1;";
                cmd.Parameters.AddWithValue("@username", username);
                using var reader = cmd.ExecuteReader();
                if (reader.Read())
                {
                    return new DeletedCharacterInfo
                    {
                        Id = reader.GetInt64(0),
                        DisplayName = reader.GetString(1),
                        PlayerData = reader.GetString(2),
                        DeletedAt = reader.GetString(3),
                        ExpiresAt = reader.GetString(4)
                    };
                }
                return null;
            }
            catch (Exception ex)
            {
                DebugLogger.Instance.LogError("SQL", $"GetMostRecentDeletedCharacter failed: {ex.Message}");
                return null;
            }
        }

        /// <summary>
        /// Restore the most-recent archived character back into the players table,
        /// then remove it from deleted_characters. Returns true on success.
        /// Will refuse to overwrite if the SSH account already has a non-empty
        /// player_data (player created a new character after deleting; we don't
        /// want to silently replace it).
        /// </summary>
        public bool RestoreFromDeleted(string username, out string failureReason)
        {
            failureReason = "";
            try
            {
                using var connection = OpenConnection();

                // Refuse if there's an active character on this account already.
                using (var checkCmd = connection.CreateCommand())
                {
                    checkCmd.CommandText = @"
                        SELECT player_data FROM players
                         WHERE LOWER(username) = LOWER(@username);";
                    checkCmd.Parameters.AddWithValue("@username", username);
                    var existing = checkCmd.ExecuteScalar() as string;
                    if (!string.IsNullOrEmpty(existing) && existing != "{}" && existing.Length > 4)
                    {
                        failureReason = "active_character_exists";
                        return false;
                    }
                }

                var info = GetMostRecentDeletedCharacter(username);
                if (info == null)
                {
                    failureReason = "no_archived_character";
                    return false;
                }

                using (var updateCmd = connection.CreateCommand())
                {
                    updateCmd.CommandText = @"
                        UPDATE players SET player_data = @data
                         WHERE LOWER(username) = LOWER(@username);";
                    updateCmd.Parameters.AddWithValue("@username", username);
                    updateCmd.Parameters.AddWithValue("@data", info.PlayerData);
                    int affected = updateCmd.ExecuteNonQuery();
                    if (affected == 0)
                    {
                        failureReason = "no_player_row";
                        return false;
                    }
                }

                // Remove the archive entry now that it's been claimed.
                using (var deleteCmd = connection.CreateCommand())
                {
                    deleteCmd.CommandText = "DELETE FROM deleted_characters WHERE id = @id;";
                    deleteCmd.Parameters.AddWithValue("@id", info.Id);
                    deleteCmd.ExecuteNonQuery();
                }

                DebugLogger.Instance.LogInfo("SAVE",
                    $"Restored '{username}' (display: '{info.DisplayName}') from deleted_characters archive.");
                return true;
            }
            catch (Exception ex)
            {
                failureReason = $"sql_error: {ex.Message}";
                DebugLogger.Instance.LogError("SQL", $"RestoreFromDeleted failed: {ex.Message}");
                return false;
            }
        }

        public class DeletedCharacterInfo
        {
            public long Id { get; set; }
            public string DisplayName { get; set; } = "";
            public string PlayerData { get; set; } = "";
            public string DeletedAt { get; set; } = "";
            public string ExpiresAt { get; set; } = "";
        }

        public List<SaveInfo> GetAllSaves()
        {
            var saves = new List<SaveInfo>();
            try
            {
                using var connection = OpenConnection();
                using var cmd = connection.CreateCommand();
                cmd.CommandText = "SELECT username, display_name, player_data, last_login FROM players WHERE is_banned = 0 ORDER BY last_login DESC;";

                using var reader = cmd.ExecuteReader();
                while (reader.Read())
                {
                    try
                    {
                        var json = reader.GetString(2);
                        // Skip empty registration records (player_data = '{}')
                        if (json == "{}" || string.IsNullOrWhiteSpace(json))
                            continue;
                        var saveData = JsonSerializer.Deserialize<SaveGameData>(json, jsonOptions);
                        if (saveData?.Player != null && saveData.Version >= GameConfig.MinSaveVersion)
                        {
                            saves.Add(new SaveInfo
                            {
                                PlayerName = saveData.Player.Name2 ?? saveData.Player.Name1,
                                SaveTime = saveData.SaveTime,
                                Level = saveData.Player.Level,
                                CurrentDay = saveData.CurrentDay,
                                TurnsRemaining = saveData.Player.TurnsRemaining,
                                FileName = reader.GetString(0), // username as "filename"
                                IsAutosave = false,
                                SaveType = "Online Save"
                            });
                        }
                    }
                    catch
                    {
                        // Skip invalid entries
                    }
                }
            }
            catch (Exception ex)
            {
                DebugLogger.Instance.LogError("SQL", $"Failed to get all saves: {ex.Message}");
            }
            return saves;
        }

        public List<SaveInfo> GetPlayerSaves(string playerName)
        {
            // In online mode, there's exactly one save per player (no autosaves/manual distinction)
            var saves = new List<SaveInfo>();
            try
            {
                using var connection = OpenConnection();
                using var cmd = connection.CreateCommand();
                // ORDER BY LENGTH DESC to prefer actual save data over empty '{}' registration records
                cmd.CommandText = "SELECT player_data FROM players WHERE LOWER(username) = LOWER(@username) AND is_banned = 0 ORDER BY LENGTH(player_data) DESC LIMIT 1;";
                cmd.Parameters.AddWithValue("@username", playerName);

                var result = cmd.ExecuteScalar();
                if (result != null && result != DBNull.Value)
                {
                    var json = (string)result;
                    // Skip empty registration records (player_data = '{}')
                    if (json != "{}" && !string.IsNullOrWhiteSpace(json))
                    {
                        var saveData = JsonSerializer.Deserialize<SaveGameData>(json, jsonOptions);
                        if (saveData?.Player != null && saveData.Version >= GameConfig.MinSaveVersion)
                        {
                            saves.Add(new SaveInfo
                            {
                                PlayerName = saveData.Player.Name2 ?? saveData.Player.Name1,
                                ClassName = saveData.Player.Class.ToString(),
                                SaveTime = saveData.SaveTime,
                                Level = saveData.Player.Level,
                                CurrentDay = saveData.CurrentDay,
                                TurnsRemaining = saveData.Player.TurnsRemaining,
                                FileName = playerName,
                                IsAutosave = false,
                                SaveType = "Online Save"
                            });
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                DebugLogger.Instance.LogError("SQL", $"Failed to get player saves: {ex.Message}");
            }
            return saves;
        }

        public SaveInfo? GetMostRecentSave(string playerName)
        {
            var saves = GetPlayerSaves(playerName);
            return saves.FirstOrDefault();
        }

        public List<string> GetAllPlayerNames()
        {
            var names = new List<string>();
            try
            {
                using var connection = OpenConnection();
                using var cmd = connection.CreateCommand();
                cmd.CommandText = "SELECT display_name FROM players WHERE is_banned = 0 AND username NOT LIKE 'emergency_%' ORDER BY display_name;";

                using var reader = cmd.ExecuteReader();
                while (reader.Read())
                {
                    var name = reader.GetString(0);
                    if (!string.IsNullOrWhiteSpace(name))
                        names.Add(name);
                }
            }
            catch (Exception ex)
            {
                DebugLogger.Instance.LogError("SQL", $"Failed to get player names: {ex.Message}");
            }
            return names;
        }

        /// <summary>
        /// Check if a display name is already taken by another account.
        /// Returns true if the name is taken by someone other than excludeUsername.
        /// </summary>
        public bool IsDisplayNameTaken(string displayName, string excludeUsername)
        {
            try
            {
                using var connection = OpenConnection();
                using var cmd = connection.CreateCommand();
                cmd.CommandText = @"
                    SELECT COUNT(*) FROM players
                    WHERE LOWER(display_name) = LOWER(@displayName)
                    AND LOWER(username) != LOWER(@excludeUsername)
                    AND username NOT LIKE 'emergency_%';";
                cmd.Parameters.AddWithValue("@displayName", displayName);
                cmd.Parameters.AddWithValue("@excludeUsername", excludeUsername);
                var count = Convert.ToInt64(cmd.ExecuteScalar());
                return count > 0;
            }
            catch (Exception ex)
            {
                DebugLogger.Instance.LogError("SQL", $"Failed to check display name: {ex.Message}");
                return false;
            }
        }

        public async Task<bool> WriteAutoSave(string playerName, SaveGameData data)
        {
            // In online mode, autosave just overwrites the main save (no rotation needed)
            return await WriteGameData(playerName, data);
        }

        public void CreateBackup(string playerName)
        {
            // In online mode, backups are handled at the database level (daily SQLite backup to S3)
            // No per-save backup needed
            DebugLogger.Instance.LogDebug("SQL", $"Backup requested for '{playerName}' (handled by database-level backup)");
        }

        public string GetSaveDirectory()
        {
            return Path.GetDirectoryName(databasePath) ?? databasePath;
        }

        // =====================================================================
        // IOnlineSaveBackend Implementation (Online features)
        // =====================================================================

        // --- World State ---

        /// <summary>
        /// v0.65.0 (1.0-prep SR): version-guarded world_state write (CAS).
        /// Writes only if the row's version still equals expectedVersion --
        /// i.e. nobody else wrote since the caller last reconciled with the
        /// stored state. Returns false on conflict so the caller can skip
        /// this cycle and reload-merge on the next one instead of clobbering
        /// the concurrent write (the v0.61.2 stale-snapshot race class:
        /// world-sim's serialize window vs a player session's
        /// SaveAllSharedState). expectedVersion 0 = "key should not exist
        /// yet" (first-ever write).
        /// </summary>
        public async Task<bool> SaveWorldStateIfVersion(string key, string jsonValue, long expectedVersion)
        {
            try
            {
                using var connection = OpenConnection();
                using var transaction = connection.BeginTransaction();

                long currentVersion = -1; // -1 = row absent
                using (var readCmd = connection.CreateCommand())
                {
                    readCmd.Transaction = transaction;
                    readCmd.CommandText = "SELECT version FROM world_state WHERE key = @key;";
                    readCmd.Parameters.AddWithValue("@key", key);
                    var result = await readCmd.ExecuteScalarAsync();
                    if (result != null && result != DBNull.Value)
                        currentVersion = Convert.ToInt64(result);
                }

                if (currentVersion == -1)
                {
                    if (expectedVersion != 0) { transaction.Rollback(); return false; }
                    using var insertCmd = connection.CreateCommand();
                    insertCmd.Transaction = transaction;
                    insertCmd.CommandText = @"
                        INSERT INTO world_state (key, value, version, updated_at)
                        VALUES (@key, @value, 1, datetime('now'));";
                    insertCmd.Parameters.AddWithValue("@key", key);
                    insertCmd.Parameters.AddWithValue("@value", jsonValue);
                    await insertCmd.ExecuteNonQueryAsync();
                }
                else
                {
                    if (currentVersion != expectedVersion) { transaction.Rollback(); return false; }
                    using var updateCmd = connection.CreateCommand();
                    updateCmd.Transaction = transaction;
                    updateCmd.CommandText = @"
                        UPDATE world_state SET value = @value, version = version + 1, updated_at = datetime('now')
                        WHERE key = @key AND version = @expected;";
                    updateCmd.Parameters.AddWithValue("@key", key);
                    updateCmd.Parameters.AddWithValue("@value", jsonValue);
                    updateCmd.Parameters.AddWithValue("@expected", expectedVersion);
                    int rows = await updateCmd.ExecuteNonQueryAsync();
                    if (rows == 0) { transaction.Rollback(); return false; }
                }

                transaction.Commit();
                return true;
            }
            catch (Exception ex)
            {
                DebugLogger.Instance.LogError("SQL", $"SaveWorldStateIfVersion('{key}') failed: {ex.Message}");
                return false;
            }
        }

        public async Task SaveWorldState(string key, string jsonValue)
        {
            try
            {
                using var connection = OpenConnection();
                using var cmd = connection.CreateCommand();
                cmd.CommandText = @"
                    INSERT INTO world_state (key, value, version, updated_at)
                    VALUES (@key, @value, 1, datetime('now'))
                    ON CONFLICT(key) DO UPDATE SET
                        value = @value,
                        version = version + 1,
                        updated_at = datetime('now');
                ";
                cmd.Parameters.AddWithValue("@key", key);
                cmd.Parameters.AddWithValue("@value", jsonValue);
                await cmd.ExecuteNonQueryAsync();
            }
            catch (Exception ex)
            {
                DebugLogger.Instance.LogError("SQL", $"Failed to save world state '{key}': {ex.Message}");
            }
        }

        public async Task<string?> LoadWorldState(string key)
        {
            try
            {
                using var connection = OpenConnection();
                using var cmd = connection.CreateCommand();
                cmd.CommandText = "SELECT value FROM world_state WHERE key = @key;";
                cmd.Parameters.AddWithValue("@key", key);
                var result = await cmd.ExecuteScalarAsync();
                return result as string;
            }
            catch (Exception ex)
            {
                DebugLogger.Instance.LogError("SQL", $"Failed to load world state '{key}': {ex.Message}");
                return null;
            }
        }

        /// <summary>
        /// Get the current version number of a world_state key.
        /// Used to detect when another process (game server) has modified the data.
        /// Returns 0 if the key doesn't exist.
        /// </summary>
        public long GetWorldStateVersion(string key)
        {
            try
            {
                using var connection = OpenConnection();
                using var cmd = connection.CreateCommand();
                cmd.CommandText = "SELECT version FROM world_state WHERE key = @key;";
                cmd.Parameters.AddWithValue("@key", key);
                var result = cmd.ExecuteScalar();
                return result != null ? Convert.ToInt64(result) : 0;
            }
            catch (Exception ex)
            {
                DebugLogger.Instance.LogError("SQL", $"Failed to get world state version for '{key}': {ex.Message}");
                return 0;
            }
        }

        // GetLatestRoyalCourtJson() removed - world_state 'royal_court' key is now the
        // authoritative source (maintained by world sim + player sessions), not player saves.

        public async Task<bool> TryAtomicUpdate(string key, Func<string, string> transform)
        {
            try
            {
                using var connection = OpenConnection();
                using var transaction = connection.BeginTransaction();

                // Read current value and version
                string? currentValue = null;
                long currentVersion = 0;

                using (var readCmd = connection.CreateCommand())
                {
                    readCmd.Transaction = transaction;
                    readCmd.CommandText = "SELECT value, version FROM world_state WHERE key = @key;";
                    readCmd.Parameters.AddWithValue("@key", key);

                    using var reader = await readCmd.ExecuteReaderAsync();
                    if (await reader.ReadAsync())
                    {
                        currentValue = reader.GetString(0);
                        currentVersion = reader.GetInt64(1);
                    }
                }

                if (currentValue == null)
                {
                    // Key doesn't exist - create it
                    var newValue = transform("");
                    using var insertCmd = connection.CreateCommand();
                    insertCmd.Transaction = transaction;
                    insertCmd.CommandText = @"
                        INSERT INTO world_state (key, value, version, updated_at)
                        VALUES (@key, @value, 1, datetime('now'));
                    ";
                    insertCmd.Parameters.AddWithValue("@key", key);
                    insertCmd.Parameters.AddWithValue("@value", newValue);
                    await insertCmd.ExecuteNonQueryAsync();
                }
                else
                {
                    // Transform and write with optimistic locking
                    var newValue = transform(currentValue);
                    using var updateCmd = connection.CreateCommand();
                    updateCmd.Transaction = transaction;
                    updateCmd.CommandText = @"
                        UPDATE world_state SET value = @value, version = @newVersion, updated_at = datetime('now')
                        WHERE key = @key AND version = @oldVersion;
                    ";
                    updateCmd.Parameters.AddWithValue("@key", key);
                    updateCmd.Parameters.AddWithValue("@value", newValue);
                    updateCmd.Parameters.AddWithValue("@newVersion", currentVersion + 1);
                    updateCmd.Parameters.AddWithValue("@oldVersion", currentVersion);

                    var affected = await updateCmd.ExecuteNonQueryAsync();
                    if (affected == 0)
                    {
                        // Another process modified the value - rollback
                        transaction.Rollback();
                        return false;
                    }
                }

                transaction.Commit();
                return true;
            }
            catch (Exception ex)
            {
                DebugLogger.Instance.LogError("SQL", $"Atomic update failed for '{key}': {ex.Message}");
                return false;
            }
        }

        // --- World Sim Lock ---
        // Database-level leader election for embedded world simulator.
        // Only one process runs the worldsim at a time. Uses a heartbeat
        // in the world_state table to detect stale locks.

        private const string WORLDSIM_LOCK_KEY = "worldsim_lock";
        private const int WORLDSIM_LOCK_STALE_SECONDS = 90;

        /// <summary>
        /// Try to acquire the world sim lock. Returns true if this process is now the world sim host.
        /// Lock is granted if: no lock exists, lock is stale (heartbeat > 90s old), or we already own it.
        /// Uses an atomic transaction to prevent race conditions between concurrent door sessions.
        /// </summary>
        public bool TryAcquireWorldSimLock(string ownerId)
        {
            try
            {
                using var connection = OpenConnection();
                using var transaction = connection.BeginTransaction();

                string? currentValue = null;
                using (var readCmd = connection.CreateCommand())
                {
                    readCmd.Transaction = transaction;
                    readCmd.CommandText = "SELECT value FROM world_state WHERE key = @key;";
                    readCmd.Parameters.AddWithValue("@key", WORLDSIM_LOCK_KEY);
                    currentValue = readCmd.ExecuteScalar() as string;
                }

                bool canAcquire = true;

                if (!string.IsNullOrEmpty(currentValue))
                {
                    try
                    {
                        using var doc = System.Text.Json.JsonDocument.Parse(currentValue);
                        var root = doc.RootElement;

                        // Check if we already own it
                        if (root.TryGetProperty("owner", out var ownerEl) &&
                            ownerEl.GetString() == ownerId)
                        {
                            canAcquire = true; // Re-acquire our own lock
                        }
                        // Check if heartbeat is stale
                        else if (root.TryGetProperty("heartbeat", out var hbEl))
                        {
                            var heartbeat = DateTime.Parse(hbEl.GetString()!, System.Globalization.CultureInfo.InvariantCulture,
                                System.Globalization.DateTimeStyles.RoundtripKind);
                            var age = (DateTime.UtcNow - heartbeat).TotalSeconds;
                            canAcquire = age > WORLDSIM_LOCK_STALE_SECONDS;

                            if (!canAcquire)
                            {
                                var existingOwner = root.TryGetProperty("owner", out var eo) ? eo.GetString() : "unknown";
                                DebugLogger.Instance.LogDebug("SQL", $"WorldSim lock held by '{existingOwner}' (age: {age:F0}s)");
                            }
                        }
                    }
                    catch
                    {
                        canAcquire = true; // Corrupt lock data — take over
                    }
                }

                if (canAcquire)
                {
                    var lockJson = System.Text.Json.JsonSerializer.Serialize(new
                    {
                        owner = ownerId,
                        heartbeat = DateTime.UtcNow.ToString("o"),
                        pid = Environment.ProcessId,
                        acquired = DateTime.UtcNow.ToString("o")
                    });

                    using var writeCmd = connection.CreateCommand();
                    writeCmd.Transaction = transaction;
                    writeCmd.CommandText = @"
                        INSERT INTO world_state (key, value, version, updated_at)
                        VALUES (@key, @value, 1, datetime('now'))
                        ON CONFLICT(key) DO UPDATE SET
                            value = @value,
                            version = version + 1,
                            updated_at = datetime('now');
                    ";
                    writeCmd.Parameters.AddWithValue("@key", WORLDSIM_LOCK_KEY);
                    writeCmd.Parameters.AddWithValue("@value", lockJson);
                    writeCmd.ExecuteNonQuery();
                }

                transaction.Commit();
                return canAcquire;
            }
            catch (Exception ex)
            {
                DebugLogger.Instance.LogError("SQL", $"Failed to acquire worldsim lock: {ex.Message}");
                return false;
            }
        }

        /// <summary>
        /// Update the world sim heartbeat. Called after each simulation tick.
        /// Other processes check this to determine if the lock is stale.
        /// </summary>
        public void UpdateWorldSimHeartbeat(string ownerId)
        {
            try
            {
                var lockJson = System.Text.Json.JsonSerializer.Serialize(new
                {
                    owner = ownerId,
                    heartbeat = DateTime.UtcNow.ToString("o"),
                    pid = Environment.ProcessId,
                    acquired = DateTime.UtcNow.ToString("o")
                });

                using var connection = OpenConnection();
                using var cmd = connection.CreateCommand();
                cmd.CommandText = @"
                    UPDATE world_state SET value = @value, updated_at = datetime('now')
                    WHERE key = @key;
                ";
                cmd.Parameters.AddWithValue("@key", WORLDSIM_LOCK_KEY);
                cmd.Parameters.AddWithValue("@value", lockJson);
                cmd.ExecuteNonQuery();
            }
            catch (Exception ex)
            {
                DebugLogger.Instance.LogError("SQL", $"Failed to update worldsim heartbeat: {ex.Message}");
            }
        }

        /// <summary>
        /// Release the world sim lock. Called on graceful shutdown.
        /// Only releases if we own the lock (prevents stealing another process's lock).
        /// </summary>
        public void ReleaseWorldSimLock(string ownerId)
        {
            try
            {
                using var connection = OpenConnection();
                using var transaction = connection.BeginTransaction();

                // Verify we own the lock before deleting (atomic with transaction)
                using var readCmd = connection.CreateCommand();
                readCmd.Transaction = transaction;
                readCmd.CommandText = "SELECT value FROM world_state WHERE key = @key;";
                readCmd.Parameters.AddWithValue("@key", WORLDSIM_LOCK_KEY);
                var currentValue = readCmd.ExecuteScalar() as string;

                if (!string.IsNullOrEmpty(currentValue))
                {
                    try
                    {
                        using var doc = System.Text.Json.JsonDocument.Parse(currentValue);
                        if (doc.RootElement.TryGetProperty("owner", out var ownerEl) &&
                            ownerEl.GetString() != ownerId)
                        {
                            transaction.Rollback();
                            return; // Not our lock
                        }
                    }
                    catch { /* corrupt data, safe to delete */ }
                }

                using var deleteCmd = connection.CreateCommand();
                deleteCmd.Transaction = transaction;
                deleteCmd.CommandText = "DELETE FROM world_state WHERE key = @key;";
                deleteCmd.Parameters.AddWithValue("@key", WORLDSIM_LOCK_KEY);
                deleteCmd.ExecuteNonQuery();

                transaction.Commit();
                DebugLogger.Instance.LogInfo("SQL", $"WorldSim lock released by '{ownerId}'");
            }
            catch (Exception ex)
            {
                DebugLogger.Instance.LogError("SQL", $"Failed to release worldsim lock: {ex.Message}");
            }
        }

        /// <summary>
        /// Check if the world sim lock is currently held and active (not stale).
        /// Used to determine if another process is already running the world sim.
        /// </summary>
        public bool IsWorldSimLockActive()
        {
            try
            {
                using var connection = OpenConnection();
                using var cmd = connection.CreateCommand();
                cmd.CommandText = "SELECT value FROM world_state WHERE key = @key;";
                cmd.Parameters.AddWithValue("@key", WORLDSIM_LOCK_KEY);
                var value = cmd.ExecuteScalar() as string;

                if (string.IsNullOrEmpty(value)) return false;

                using var doc = System.Text.Json.JsonDocument.Parse(value);
                if (doc.RootElement.TryGetProperty("heartbeat", out var hbEl))
                {
                    var heartbeat = DateTime.Parse(hbEl.GetString()!, System.Globalization.CultureInfo.InvariantCulture,
                        System.Globalization.DateTimeStyles.RoundtripKind);
                    return (DateTime.UtcNow - heartbeat).TotalSeconds <= WORLDSIM_LOCK_STALE_SECONDS;
                }

                return false;
            }
            catch
            {
                return false;
            }
        }

        // --- News ---

        public async Task AddNews(string message, string category, string playerName)
        {
            try
            {
                using var connection = OpenConnection();
                using var cmd = connection.CreateCommand();
                cmd.CommandText = @"
                    INSERT INTO news (message, category, player_name) VALUES (@message, @category, @playerName);
                ";
                cmd.Parameters.AddWithValue("@message", message);
                cmd.Parameters.AddWithValue("@category", category);
                cmd.Parameters.AddWithValue("@playerName", (object?)playerName ?? DBNull.Value);
                await cmd.ExecuteNonQueryAsync();
            }
            catch (ObjectDisposedException)
            {
                // Expected during session teardown — fire-and-forget news posts may outlive the session
                DebugLogger.Instance.LogDebug("SQL", "AddNews skipped — connection disposed (session ended)");
            }
            catch (Exception ex)
            {
                DebugLogger.Instance.LogError("SQL", $"Failed to add news: {ex.Message}");
            }
        }

        // --- Combat Events (Balance Dashboard) ---

        public async Task LogCombatEvent(
            string playerName, int playerLevel, string playerClass,
            long playerMaxHP, long playerSTR, long playerDEX, long playerWeapPow, long playerArmPow,
            string? monsterName, int monsterLevel, long monsterMaxHP, long monsterSTR, long monsterDEF,
            bool isBoss, string outcome, int rounds,
            long damageDealt, long damageTaken, long xpGained, long goldGained,
            int dungeonFloor, int monsterCount, bool hasTeammates)
        {
            try
            {
                using var connection = OpenConnection();
                using var cmd = connection.CreateCommand();
                cmd.CommandText = @"
                    INSERT INTO combat_events (
                        player_name, player_level, player_class,
                        player_max_hp, player_str, player_dex, player_weap_pow, player_arm_pow,
                        monster_name, monster_level, monster_max_hp, monster_str, monster_def,
                        is_boss, outcome, rounds, damage_dealt, damage_taken,
                        xp_gained, gold_gained, dungeon_floor, monster_count, has_teammates
                    ) VALUES (
                        @pName, @pLevel, @pClass,
                        @pMaxHP, @pSTR, @pDEX, @pWeapPow, @pArmPow,
                        @mName, @mLevel, @mMaxHP, @mSTR, @mDEF,
                        @isBoss, @outcome, @rounds, @dmgDealt, @dmgTaken,
                        @xpGained, @goldGained, @floor, @mCount, @hasTeam
                    );
                ";
                cmd.Parameters.AddWithValue("@pName", playerName);
                cmd.Parameters.AddWithValue("@pLevel", playerLevel);
                cmd.Parameters.AddWithValue("@pClass", playerClass);
                cmd.Parameters.AddWithValue("@pMaxHP", playerMaxHP);
                cmd.Parameters.AddWithValue("@pSTR", playerSTR);
                cmd.Parameters.AddWithValue("@pDEX", playerDEX);
                cmd.Parameters.AddWithValue("@pWeapPow", playerWeapPow);
                cmd.Parameters.AddWithValue("@pArmPow", playerArmPow);
                cmd.Parameters.AddWithValue("@mName", (object?)monsterName ?? DBNull.Value);
                cmd.Parameters.AddWithValue("@mLevel", monsterLevel);
                cmd.Parameters.AddWithValue("@mMaxHP", monsterMaxHP);
                cmd.Parameters.AddWithValue("@mSTR", monsterSTR);
                cmd.Parameters.AddWithValue("@mDEF", monsterDEF);
                cmd.Parameters.AddWithValue("@isBoss", isBoss ? 1 : 0);
                cmd.Parameters.AddWithValue("@outcome", outcome);
                cmd.Parameters.AddWithValue("@rounds", rounds);
                cmd.Parameters.AddWithValue("@dmgDealt", damageDealt);
                cmd.Parameters.AddWithValue("@dmgTaken", damageTaken);
                cmd.Parameters.AddWithValue("@xpGained", xpGained);
                cmd.Parameters.AddWithValue("@goldGained", goldGained);
                cmd.Parameters.AddWithValue("@floor", dungeonFloor);
                cmd.Parameters.AddWithValue("@mCount", monsterCount);
                cmd.Parameters.AddWithValue("@hasTeam", hasTeammates ? 1 : 0);
                await cmd.ExecuteNonQueryAsync();
            }
            catch (Exception ex)
            {
                DebugLogger.Instance.LogError("SQL", $"Failed to log combat event: {ex.Message}");
            }
        }

        /// <summary>
        /// v0.61.2 Phase 1 of NPC AI project: log every world-sim NPC decision so
        /// we can measure baseline behavior (survival rates by class, gold delta
        /// per action, dungeon outcome distribution) BEFORE the AI subset lands.
        /// Once AI NPCs ship, the isAiDriven flag lets us split rollups and
        /// compare AI vs heuristic cohorts on the same metrics. Fire-and-forget
        /// from the call site so a write failure doesn't break world sim.
        /// </summary>
        public void LogNPCDecision(
            string npcName, int npcLevel, string npcClass,
            string action, string? locationBefore, string? locationAfter,
            string? outcome, long goldDelta, long xpDelta,
            long hpBefore, long hpAfter, bool isAiDriven,
            string decisionSource = "sim")
        {
            try
            {
                using var connection = OpenConnection();
                using var cmd = connection.CreateCommand();
                cmd.CommandText = @"
                    INSERT INTO npc_decision_log (
                        npc_name, npc_level, npc_class,
                        action, location_before, location_after, outcome,
                        gold_delta, xp_delta, hp_before, hp_after, is_ai_driven,
                        decision_source
                    ) VALUES (
                        @name, @level, @class,
                        @action, @locBefore, @locAfter, @outcome,
                        @goldDelta, @xpDelta, @hpBefore, @hpAfter, @aiDriven,
                        @source
                    );
                ";
                cmd.Parameters.AddWithValue("@name", npcName ?? "");
                cmd.Parameters.AddWithValue("@level", npcLevel);
                cmd.Parameters.AddWithValue("@class", npcClass ?? "");
                cmd.Parameters.AddWithValue("@action", action ?? "");
                cmd.Parameters.AddWithValue("@locBefore", (object?)locationBefore ?? DBNull.Value);
                cmd.Parameters.AddWithValue("@locAfter", (object?)locationAfter ?? DBNull.Value);
                cmd.Parameters.AddWithValue("@outcome", (object?)outcome ?? DBNull.Value);
                cmd.Parameters.AddWithValue("@goldDelta", goldDelta);
                cmd.Parameters.AddWithValue("@xpDelta", xpDelta);
                cmd.Parameters.AddWithValue("@hpBefore", hpBefore);
                cmd.Parameters.AddWithValue("@hpAfter", hpAfter);
                cmd.Parameters.AddWithValue("@aiDriven", isAiDriven ? 1 : 0);
                cmd.Parameters.AddWithValue("@source", decisionSource ?? "sim");
                cmd.ExecuteNonQuery();
            }
            catch (Exception ex)
            {
                DebugLogger.Instance.LogError("SQL", $"Failed to log NPC decision: {ex.Message}");
            }
        }

        /// <summary>
        /// v0.64.0 Brain v2 Slice 10: persist one LLM call attempt to llm_usage
        /// for the balance dashboard's LLM stats card. Captures both successes
        /// (with token counts + rendered text + latency) and fallbacks (with
        /// failure_reason). Fire-and-forget from LLMMoments call sites so a
        /// write failure doesn't break the moment generator.
        /// </summary>
        public void RecordLLMUsage(
            string momentType,
            string? npcName,
            bool succeeded,
            int promptTokens,
            int completionTokens,
            int totalTokens,
            int responseMs,
            string? renderedText,
            string? failureReason)
        {
            try
            {
                using var connection = OpenConnection();
                using var cmd = connection.CreateCommand();
                cmd.CommandText = @"
                    INSERT INTO llm_usage (
                        moment_type, npc_name, succeeded,
                        prompt_tokens, completion_tokens, total_tokens,
                        response_ms, rendered_text, failure_reason
                    ) VALUES (
                        @moment, @npc, @ok,
                        @ptok, @ctok, @ttok,
                        @ms, @text, @reason
                    );
                ";
                cmd.Parameters.AddWithValue("@moment", momentType ?? "unknown");
                cmd.Parameters.AddWithValue("@npc", (object?)npcName ?? DBNull.Value);
                cmd.Parameters.AddWithValue("@ok", succeeded ? 1 : 0);
                cmd.Parameters.AddWithValue("@ptok", promptTokens);
                cmd.Parameters.AddWithValue("@ctok", completionTokens);
                cmd.Parameters.AddWithValue("@ttok", totalTokens);
                cmd.Parameters.AddWithValue("@ms", responseMs);
                cmd.Parameters.AddWithValue("@text", (object?)renderedText ?? DBNull.Value);
                cmd.Parameters.AddWithValue("@reason", (object?)failureReason ?? DBNull.Value);
                cmd.ExecuteNonQuery();
            }
            catch (Exception ex)
            {
                DebugLogger.Instance.LogError("SQL", $"Failed to log LLM usage: {ex.Message}");
            }
        }

        /// <summary>
        /// Prune npc_decision_log rows older than the cutoff so the table stays
        /// bounded. Called from the daily maintenance pass.
        /// </summary>
        public async Task PruneOldNPCDecisionLog(int daysToKeep = 30)
        {
            try
            {
                using var connection = OpenConnection();
                using var cmd = connection.CreateCommand();
                cmd.CommandText = @"DELETE FROM npc_decision_log WHERE created_at < datetime('now', @cutoff);";
                cmd.Parameters.AddWithValue("@cutoff", $"-{daysToKeep} days");
                await cmd.ExecuteNonQueryAsync();
            }
            catch (Exception ex)
            {
                DebugLogger.Instance.LogError("SQL", $"Failed to prune npc_decision_log: {ex.Message}");
            }
        }

        /// <summary>
        /// v1.0 release prep (B5): prune llm_usage rows older than the cutoff.
        /// The table's own creation comment claimed "Pruned to last 30 days"
        /// since v0.64.0 but no prune ever existed -- with Slice 11 dialogue
        /// flavor writing one row per LLM attempt per (NPC, cache key), the
        /// production DB grew without bound. Called from the same maintenance
        /// pass as PruneOldNPCDecisionLog.
        /// </summary>
        public async Task PruneOldLLMUsage(int daysToKeep = 30)
        {
            try
            {
                using var connection = OpenConnection();
                using var cmd = connection.CreateCommand();
                cmd.CommandText = @"DELETE FROM llm_usage WHERE created_at < datetime('now', @cutoff);";
                cmd.Parameters.AddWithValue("@cutoff", $"-{daysToKeep} days");
                await cmd.ExecuteNonQueryAsync();
            }
            catch (Exception ex)
            {
                DebugLogger.Instance.LogError("SQL", $"Failed to prune llm_usage: {ex.Message}");
            }
        }

        /// <summary>
        /// v0.65.0 (1.0-prep SR): total LLM tokens recorded today (UTC) --
        /// used by LLMBudget.RehydrateFromBackend so a server restart no
        /// longer resets the daily cap. Synchronous single SUM on an indexed
        /// column; called once at startup.
        /// </summary>
        public long GetLLMTokensUsedTodayUtc()
        {
            try
            {
                using var connection = OpenConnection();
                using var cmd = connection.CreateCommand();
                cmd.CommandText = @"SELECT COALESCE(SUM(total_tokens), 0) FROM llm_usage WHERE created_at >= date('now');";
                return Convert.ToInt64(cmd.ExecuteScalar());
            }
            catch (Exception ex)
            {
                DebugLogger.Instance.LogError("SQL", $"GetLLMTokensUsedTodayUtc failed: {ex.Message}");
                return 0;
            }
        }

        public async Task PruneOldNews(string category, int hoursToKeep = 24)
        {
            try
            {
                using var connection = OpenConnection();
                using var cmd = connection.CreateCommand();
                cmd.CommandText = @"
                    DELETE FROM news WHERE category = @category AND created_at < datetime('now', @cutoff);
                ";
                cmd.Parameters.AddWithValue("@category", category);
                cmd.Parameters.AddWithValue("@cutoff", $"-{hoursToKeep} hours");
                await cmd.ExecuteNonQueryAsync();
            }
            catch (Exception ex)
            {
                DebugLogger.Instance.LogError("SQL", $"Failed to prune old news: {ex.Message}");
            }
        }

        /// <summary>
        /// Prune all news categories by age, then enforce per-category row caps.
        /// NPC news is capped separately from player news so high-volume NPC events
        /// don't push out player events.
        /// </summary>
        public async Task PruneAllNews(int hoursToKeep = 48, int maxNpcNews = 500, int maxPlayerNews = 200)
        {
            try
            {
                using var connection = OpenConnection();

                // 1. Delete all entries older than the time cutoff (across all categories)
                using (var cmd = connection.CreateCommand())
                {
                    cmd.CommandText = @"
                        DELETE FROM news WHERE created_at < datetime('now', @cutoff);
                    ";
                    cmd.Parameters.AddWithValue("@cutoff", $"-{hoursToKeep} hours");
                    await cmd.ExecuteNonQueryAsync();
                }

                // 2. Enforce per-category caps so NPC spam doesn't evict player news
                // Cap NPC news
                using (var cmd = connection.CreateCommand())
                {
                    cmd.CommandText = @"
                        DELETE FROM news WHERE category = 'npc' AND id NOT IN (
                            SELECT id FROM news WHERE category = 'npc' ORDER BY created_at DESC LIMIT @maxNpc
                        );
                    ";
                    cmd.Parameters.AddWithValue("@maxNpc", maxNpcNews);
                    await cmd.ExecuteNonQueryAsync();
                }

                // Cap player news (quest, combat, etc.)
                using (var cmd = connection.CreateCommand())
                {
                    cmd.CommandText = @"
                        DELETE FROM news WHERE category != 'npc' AND id NOT IN (
                            SELECT id FROM news WHERE category != 'npc' ORDER BY created_at DESC LIMIT @maxPlayer
                        );
                    ";
                    cmd.Parameters.AddWithValue("@maxPlayer", maxPlayerNews);
                    await cmd.ExecuteNonQueryAsync();
                }
            }
            catch (Exception ex)
            {
                DebugLogger.Instance.LogError("SQL", $"Failed to prune all news: {ex.Message}");
            }
        }

        /// <summary>Prune combat_events older than the specified number of days, keeping at most maxRows.</summary>
        public async Task PruneCombatEvents(int daysToKeep = 7, int maxRows = 1000)
        {
            try
            {
                using var conn = new SqliteConnection(connectionString);
                await conn.OpenAsync();

                // Delete old events by age
                using (var cmd = conn.CreateCommand())
                {
                    cmd.CommandText = "DELETE FROM combat_events WHERE created_at < datetime('now', '-' || @days || ' days');";
                    cmd.Parameters.AddWithValue("@days", daysToKeep);
                    await cmd.ExecuteNonQueryAsync();
                }

                // Cap total rows (keep newest)
                using (var cmd = conn.CreateCommand())
                {
                    cmd.CommandText = @"
                        DELETE FROM combat_events WHERE id NOT IN (
                            SELECT id FROM combat_events ORDER BY created_at DESC LIMIT @maxRows
                        );";
                    cmd.Parameters.AddWithValue("@maxRows", maxRows);
                    await cmd.ExecuteNonQueryAsync();
                }
            }
            catch (Exception ex)
            {
                DebugLogger.Instance.LogError("SQL", $"Failed to prune combat events: {ex.Message}");
            }
        }

        /// <summary>Remove orphaned rows from tables that reference deleted players.</summary>
        public async Task PruneOrphanedPlayerData()
        {
            try
            {
                using var conn = new SqliteConnection(connectionString);
                await conn.OpenAsync();

                // sleeping_players, online_players, combat_events keyed on username/player_name
                string[] orphanQueries = new[]
                {
                    "DELETE FROM sleeping_players WHERE username NOT IN (SELECT username FROM players)",
                    "DELETE FROM online_players WHERE username NOT IN (SELECT username FROM players)",
                    "DELETE FROM combat_events WHERE player_name NOT IN (SELECT username FROM players) AND player_name NOT IN (SELECT display_name FROM players)",
                    // v0.65.0 (save-state-review F1): reap pending wire transfers + inheritance
                    // whose recipient permadied/was deleted before delivery. The gold was already
                    // a sink (sender paid, recipient gone); this just stops dead rows accumulating.
                    // Both columns are stored lowercase, matching players.username (also lowercase).
                    "DELETE FROM pending_gold_transfers WHERE recipient_username NOT IN (SELECT username FROM players)",
                    "DELETE FROM pending_inheritance WHERE player_username NOT IN (SELECT username FROM players)",
                };

                foreach (var sql in orphanQueries)
                {
                    using var cmd = conn.CreateCommand();
                    cmd.CommandText = sql;
                    await cmd.ExecuteNonQueryAsync();
                }
            }
            catch (Exception ex)
            {
                DebugLogger.Instance.LogError("SQL", $"Failed to prune orphaned player data: {ex.Message}");
            }
        }

        public async Task<List<NewsEntry>> GetRecentNews(int count = 20)
        {
            var entries = new List<NewsEntry>();
            try
            {
                using var connection = OpenConnection();
                using var cmd = connection.CreateCommand();
                cmd.CommandText = "SELECT id, message, category, player_name, created_at FROM news ORDER BY created_at DESC LIMIT @count;";
                cmd.Parameters.AddWithValue("@count", count);

                using var reader = await cmd.ExecuteReaderAsync();
                while (await reader.ReadAsync())
                {
                    entries.Add(new NewsEntry
                    {
                        Id = reader.GetInt32(0),
                        Message = reader.GetString(1),
                        Category = reader.IsDBNull(2) ? "" : reader.GetString(2),
                        PlayerName = reader.IsDBNull(3) ? "" : reader.GetString(3),
                        CreatedAt = DateTime.Parse(reader.GetString(4))
                    });
                }
            }
            catch (Exception ex)
            {
                DebugLogger.Instance.LogError("SQL", $"Failed to get recent news: {ex.Message}");
            }
            return entries;
        }

        // --- Online Player Tracking ---

        /// <summary>
        /// Check if a player is currently online (has a recent heartbeat).
        /// Used to prevent duplicate logins on the same character.
        /// </summary>
        public async Task<bool> IsPlayerOnline(string username)
        {
            try
            {
                using var connection = OpenConnection();
                using var cmd = connection.CreateCommand();
                cmd.CommandText = @"
                    SELECT COUNT(*) FROM online_players
                    WHERE LOWER(username) = LOWER(@username)
                      AND last_heartbeat >= datetime('now', '-300 seconds');
                ";
                cmd.Parameters.AddWithValue("@username", username);
                var result = await cmd.ExecuteScalarAsync();
                return Convert.ToInt64(result) > 0;
            }
            catch (Exception ex)
            {
                DebugLogger.Instance.LogError("SQL", $"Failed to check if player is online: {ex.Message}");
                return false; // Fail open - don't block login on DB errors
            }
        }

        public async Task RegisterOnline(string username, string displayName, string location, string connectionType = "Unknown", string ipAddress = "")
        {
            try
            {
                using var connection = OpenConnection();

                // Remove any case-variant entries first (PK is case-sensitive but usernames should be case-insensitive)
                using var delCmd = connection.CreateCommand();
                delCmd.CommandText = "DELETE FROM online_players WHERE LOWER(username) = LOWER(@username);";
                delCmd.Parameters.AddWithValue("@username", username);
                await delCmd.ExecuteNonQueryAsync();

                using var cmd = connection.CreateCommand();
                cmd.CommandText = @"
                    INSERT INTO online_players (username, display_name, location, node_id, connection_type, ip_address, connected_at, last_heartbeat)
                    VALUES (@username, @displayName, @location, @nodeId, @connectionType, @ipAddress, datetime('now'), datetime('now'));
                ";
                cmd.Parameters.AddWithValue("@username", username);
                cmd.Parameters.AddWithValue("@displayName", displayName);
                cmd.Parameters.AddWithValue("@location", location);
                cmd.Parameters.AddWithValue("@nodeId", Environment.ProcessId.ToString());
                cmd.Parameters.AddWithValue("@connectionType", connectionType);
                cmd.Parameters.AddWithValue("@ipAddress", ipAddress);
                await cmd.ExecuteNonQueryAsync();
            }
            catch (Exception ex)
            {
                DebugLogger.Instance.LogError("SQL", $"Failed to register online: {ex.Message}");
            }
        }

        public async Task UpdateOnlineDisplayName(string username, string displayName)
        {
            try
            {
                using var connection = OpenConnection();
                using var cmd = connection.CreateCommand();
                cmd.CommandText = @"
                    UPDATE online_players SET display_name = @displayName
                    WHERE LOWER(username) = LOWER(@username);
                ";
                cmd.Parameters.AddWithValue("@username", username);
                cmd.Parameters.AddWithValue("@displayName", displayName);
                await cmd.ExecuteNonQueryAsync();
            }
            catch (Exception ex)
            {
                DebugLogger.Instance.LogError("SQL", $"Failed to update online display name: {ex.Message}");
            }
        }

        public async Task<bool> UpdateHeartbeat(string username, string location)
        {
            try
            {
                using var connection = OpenConnection();
                using var cmd = connection.CreateCommand();
                cmd.CommandText = @"
                    UPDATE online_players SET last_heartbeat = datetime('now'), location = @location
                    WHERE LOWER(username) = LOWER(@username);
                ";
                cmd.Parameters.AddWithValue("@username", username);
                cmd.Parameters.AddWithValue("@location", location);
                var rowsAffected = await cmd.ExecuteNonQueryAsync();
                if (rowsAffected == 0)
                {
                    DebugLogger.Instance.LogWarning("SQL", $"Heartbeat update affected 0 rows for '{username}' — row may have been deleted by stale cleanup");
                    return false;
                }
                return true;
            }
            catch (Exception ex)
            {
                DebugLogger.Instance.LogError("SQL", $"Failed to update heartbeat: {ex.Message}");
                return false;
            }
        }

        public async Task UnregisterOnline(string username)
        {
            try
            {
                using var connection = OpenConnection();
                using var cmd = connection.CreateCommand();
                cmd.CommandText = "DELETE FROM online_players WHERE LOWER(username) = LOWER(@username);";
                cmd.Parameters.AddWithValue("@username", username);
                await cmd.ExecuteNonQueryAsync();
            }
            catch (Exception ex)
            {
                DebugLogger.Instance.LogError("SQL", $"Failed to unregister online: {ex.Message}");
            }
        }

        public async Task<List<OnlinePlayerInfo>> GetOnlinePlayers()
        {
            var players = new List<OnlinePlayerInfo>();
            try
            {
                using var connection = OpenConnection();
                using var cmd = connection.CreateCommand();
                // Only return players with heartbeat in last 120 seconds
                cmd.CommandText = @"
                    SELECT username, display_name, location, connected_at, last_heartbeat, connection_type
                    FROM online_players
                    WHERE last_heartbeat >= datetime('now', '-300 seconds')
                    ORDER BY display_name;
                ";

                using var reader = await cmd.ExecuteReaderAsync();
                while (await reader.ReadAsync())
                {
                    players.Add(new OnlinePlayerInfo
                    {
                        Username = reader.GetString(0),
                        DisplayName = reader.IsDBNull(1) ? reader.GetString(0) : reader.GetString(1),
                        Location = reader.IsDBNull(2) ? "Unknown" : reader.GetString(2),
                        ConnectedAt = DateTime.Parse(reader.GetString(3)),
                        LastHeartbeat = DateTime.Parse(reader.GetString(4)),
                        ConnectionType = reader.IsDBNull(5) ? "Unknown" : reader.GetString(5)
                    });
                }
            }
            catch (Exception ex)
            {
                DebugLogger.Instance.LogError("SQL", $"Failed to get online players: {ex.Message}");
            }
            return players;
        }

        public async Task CleanupStaleOnlinePlayers()
        {
            try
            {
                using var connection = OpenConnection();
                using var cmd = connection.CreateCommand();
                cmd.CommandText = "DELETE FROM online_players WHERE last_heartbeat < datetime('now', '-300 seconds');";
                var removed = await cmd.ExecuteNonQueryAsync();
                if (removed > 0)
                {
                    DebugLogger.Instance.LogInfo("SQL", $"Cleaned up {removed} stale online player entries");
                }
            }
            catch (Exception ex)
            {
                DebugLogger.Instance.LogError("SQL", $"Failed to cleanup stale players: {ex.Message}");
            }
        }

        // --- Messaging ---

        public async Task SendMessage(string from, string to, string messageType, string message)
        {
            try
            {
                using var connection = OpenConnection();
                using var cmd = connection.CreateCommand();
                cmd.CommandText = @"
                    INSERT INTO messages (from_player, to_player, message_type, message)
                    VALUES (@from, @to, @type, @message);
                ";
                cmd.Parameters.AddWithValue("@from", from);
                cmd.Parameters.AddWithValue("@to", to);
                cmd.Parameters.AddWithValue("@type", messageType);
                cmd.Parameters.AddWithValue("@message", message);
                await cmd.ExecuteNonQueryAsync();
            }
            catch (Exception ex)
            {
                DebugLogger.Instance.LogError("SQL", $"Failed to send message: {ex.Message}");
            }
        }

        public async Task<List<PlayerMessage>> GetUnreadMessages(string username, long afterMessageId = 0)
        {
            var messages = new List<PlayerMessage>();
            try
            {
                using var connection = OpenConnection();
                using var cmd = connection.CreateCommand();
                // Use ID watermark to avoid re-fetching broadcast messages
                // Direct messages (to_player = username) use is_read flag
                // Broadcast messages (to_player = '*') use ID watermark and exclude self-sent
                cmd.CommandText = @"
                    SELECT id, from_player, to_player, message_type, message, created_at
                    FROM messages
                    WHERE (((LOWER(to_player) = LOWER(@username) OR to_player IN (SELECT display_name FROM players WHERE LOWER(username) = LOWER(@username))) AND is_read = 0)
                           OR (to_player = '*' AND id > @afterId AND LOWER(from_player) != LOWER(@username)))
                    ORDER BY created_at ASC;
                ";
                cmd.Parameters.AddWithValue("@username", username);
                cmd.Parameters.AddWithValue("@afterId", afterMessageId);

                using var reader = await cmd.ExecuteReaderAsync();
                while (await reader.ReadAsync())
                {
                    messages.Add(new PlayerMessage
                    {
                        Id = reader.GetInt32(0),
                        FromPlayer = reader.GetString(1),
                        ToPlayer = reader.GetString(2),
                        MessageType = reader.GetString(3),
                        Message = reader.GetString(4),
                        CreatedAt = DateTime.Parse(reader.GetString(5))
                    });
                }
            }
            catch (Exception ex)
            {
                DebugLogger.Instance.LogError("SQL", $"Failed to get unread messages: {ex.Message}");
            }
            return messages;
        }

        public async Task MarkMessagesRead(string username)
        {
            try
            {
                using var connection = OpenConnection();
                using var cmd = connection.CreateCommand();
                // Must match the same messages as GetUnreadMessages — both username AND display_name,
                // otherwise messages sent to display_name are fetched but never marked read (infinite loop)
                cmd.CommandText = @"UPDATE messages SET is_read = 1
                    WHERE (LOWER(to_player) = LOWER(@username)
                           OR to_player IN (SELECT display_name FROM players WHERE LOWER(username) = LOWER(@username)))
                    AND is_read = 0;";
                cmd.Parameters.AddWithValue("@username", username);
                await cmd.ExecuteNonQueryAsync();
            }
            catch (Exception ex)
            {
                DebugLogger.Instance.LogError("SQL", $"Failed to mark messages read: {ex.Message}");
            }
        }

        public async Task<long> GetMaxMessageId()
        {
            try
            {
                using var connection = OpenConnection();
                using var cmd = connection.CreateCommand();
                cmd.CommandText = "SELECT COALESCE(MAX(id), 0) FROM messages;";
                var result = await cmd.ExecuteScalarAsync();
                return Convert.ToInt64(result);
            }
            catch (Exception ex)
            {
                DebugLogger.Instance.LogError("SQL", $"Failed to get max message ID: {ex.Message}");
                return 0;
            }
        }

        // --- Leaderboard ---

        public async Task<List<PlayerSummary>> GetAllPlayerSummaries()
        {
            var summaries = new List<PlayerSummary>();
            try
            {
                using var connection = OpenConnection();
                using var cmd = connection.CreateCommand();
                // Extract level, class, experience, and display name from saved JSON
                // Skip banned players and empty saves
                cmd.CommandText = @"
                    SELECT
                        p.username,
                        p.display_name,
                        json_extract(p.player_data, '$.player.level') as level,
                        json_extract(p.player_data, '$.player.class') as class_id,
                        json_extract(p.player_data, '$.player.experience') as xp,
                        CASE WHEN op.username IS NOT NULL THEN 1 ELSE 0 END as is_online,
                        json_extract(p.player_data, '$.player.nobleTitle') as noble_title,
                        COALESCE(json_extract(p.player_data, '$.player.arenaChampionTier'), 0) as arena_tier
                    FROM players p
                    LEFT JOIN online_players op ON LOWER(p.username) = LOWER(op.username)
                        AND op.last_heartbeat >= datetime('now', '-300 seconds')
                    WHERE p.is_banned = 0
                        AND p.player_data != '{}'
                        AND LENGTH(p.player_data) > 2
                        AND json_extract(p.player_data, '$.player.level') IS NOT NULL
                        AND p.username NOT LIKE 'emergency_%'
                        AND COALESCE(json_extract(p.player_data, '$.player.isImmortal'), 0) != 1
                        AND COALESCE(json_extract(p.player_data, '$.player.hp'), 0) > 0;
                ";

                using var reader = await cmd.ExecuteReaderAsync();
                while (await reader.ReadAsync())
                {
                    summaries.Add(new PlayerSummary
                    {
                        Username = reader.GetString(0),
                        DisplayName = reader.GetString(1),
                        Level = reader.IsDBNull(2) ? 1 : Convert.ToInt32(reader.GetValue(2)),
                        ClassId = reader.IsDBNull(3) ? 0 : Convert.ToInt32(reader.GetValue(3)),
                        Experience = reader.IsDBNull(4) ? 0 : Convert.ToInt64(reader.GetValue(4)),
                        IsOnline = reader.GetInt32(5) == 1,
                        NobleTitle = reader.IsDBNull(6) ? null : reader.GetString(6),
                        ArenaChampionTier = reader.IsDBNull(7) ? 0 : Convert.ToInt32(reader.GetValue(7))
                    });
                }
            }
            catch (Exception ex)
            {
                DebugLogger.Instance.LogError("SQL", $"Failed to get player summaries: {ex.Message}");
            }
            return summaries;
        }

        /// <summary>
        /// Get a player's rank among all players, ordered by level descending.
        /// Returns 1-based rank, or 1 if query fails.
        /// </summary>
        public int GetPlayerRank(string username)
        {
            try
            {
                using var connection = OpenConnection();
                using var cmd = connection.CreateCommand();
                cmd.CommandText = @"
                    SELECT COUNT(*) + 1 FROM players
                    WHERE is_banned = 0
                        AND player_data != '{}'
                        AND LENGTH(player_data) > 2
                        AND json_extract(player_data, '$.player.level') IS NOT NULL
                        AND username NOT LIKE 'emergency_%'
                        AND json_extract(player_data, '$.player.level') > (
                            SELECT COALESCE(json_extract(player_data, '$.player.level'), 0)
                            FROM players WHERE LOWER(username) = LOWER(@username)
                        );
                ";
                cmd.Parameters.AddWithValue("@username", username);
                var result = cmd.ExecuteScalar();
                return result != null ? Convert.ToInt32(result) : 1;
            }
            catch
            {
                return 1;
            }
        }

        /// <summary>
        /// Find the closest player by level from ALL players in the database (not just online).
        /// Used for rival assignment in weekly rankings.
        /// </summary>
        public (string? displayName, int level) GetClosestPlayerByLevel(string excludeUsername, int playerLevel)
        {
            try
            {
                using var connection = OpenConnection();
                using var cmd = connection.CreateCommand();
                cmd.CommandText = @"
                    SELECT
                        display_name,
                        json_extract(player_data, '$.player.level') as level
                    FROM players
                    WHERE LOWER(username) != LOWER(@excludeUser)
                        AND is_banned = 0
                        AND player_data != '{}'
                        AND LENGTH(player_data) > 2
                        AND json_extract(player_data, '$.player.level') IS NOT NULL
                        AND username NOT LIKE 'emergency_%'
                    ORDER BY ABS(json_extract(player_data, '$.player.level') - @playerLevel)
                    LIMIT 1;
                ";
                cmd.Parameters.AddWithValue("@excludeUser", excludeUsername);
                cmd.Parameters.AddWithValue("@playerLevel", playerLevel);

                using var reader = cmd.ExecuteReader();
                if (reader.Read())
                {
                    string displayName = reader.GetString(0);
                    int level = reader.IsDBNull(1) ? 1 : Convert.ToInt32(reader.GetValue(1));
                    return (displayName, level);
                }
            }
            catch (Exception ex)
            {
                DebugLogger.Instance.LogError("SQL", $"Failed to get closest player by level: {ex.Message}");
            }
            return (null, 0);
        }

        // --- Divine System (God-Mortal Interactions) ---

        public async Task<List<ImmortalPlayerInfo>> GetImmortalPlayers()
        {
            var immortals = new List<ImmortalPlayerInfo>();
            try
            {
                using var connection = OpenConnection();
                using var cmd = connection.CreateCommand();
                cmd.CommandText = @"
                    SELECT
                        p.display_name,
                        p.username,
                        json_extract(p.player_data, '$.player.divineName') as divine_name,
                        json_extract(p.player_data, '$.player.godLevel') as god_level,
                        json_extract(p.player_data, '$.player.godExperience') as god_exp,
                        json_extract(p.player_data, '$.player.godAlignment') as god_align,
                        CASE WHEN op.username IS NOT NULL THEN 1 ELSE 0 END as is_online,
                        json_extract(p.player_data, '$.player.divineBoonConfig') as boon_config
                    FROM players p
                    LEFT JOIN online_players op ON LOWER(p.username) = LOWER(op.username)
                        AND op.last_heartbeat >= datetime('now', '-300 seconds')
                    WHERE p.is_banned = 0
                        AND p.player_data != '{}' AND LENGTH(p.player_data) > 2
                        AND json_extract(p.player_data, '$.player.isImmortal') = 1
                        AND json_extract(p.player_data, '$.player.divineName') IS NOT NULL
                        AND p.username NOT LIKE 'emergency_%';
                ";
                using var reader = await cmd.ExecuteReaderAsync();
                while (await reader.ReadAsync())
                {
                    immortals.Add(new ImmortalPlayerInfo
                    {
                        MortalName = reader.GetString(0),
                        Username = reader.IsDBNull(1) ? "" : reader.GetString(1),
                        DivineName = reader.IsDBNull(2) ? "" : reader.GetString(2),
                        GodLevel = reader.IsDBNull(3) ? 1 : Convert.ToInt32(reader.GetValue(3)),
                        GodExperience = reader.IsDBNull(4) ? 0 : Convert.ToInt64(reader.GetValue(4)),
                        GodAlignment = reader.IsDBNull(5) ? "" : reader.GetString(5),
                        IsOnline = reader.GetInt32(6) == 1,
                        DivineBoonConfig = reader.IsDBNull(7) ? "" : reader.GetString(7)
                    });
                }
            }
            catch (Exception ex)
            {
                DebugLogger.Instance.LogError("SQL", $"Failed to get immortal players: {ex.Message}");
            }
            return immortals;
        }

        public async Task<List<MortalPlayerInfo>> GetMortalPlayers(int limit = 30)
        {
            var mortals = new List<MortalPlayerInfo>();
            try
            {
                using var connection = OpenConnection();
                using var cmd = connection.CreateCommand();
                cmd.CommandText = @"
                    SELECT
                        p.display_name,
                        p.username,
                        json_extract(p.player_data, '$.player.level') as level,
                        json_extract(p.player_data, '$.player.class') as class_id,
                        json_extract(p.player_data, '$.player.worshippedGod') as worshipped_god,
                        json_extract(p.player_data, '$.player.divineBlessingCombats') as blessing_combats,
                        json_extract(p.player_data, '$.player.hp') as hp,
                        json_extract(p.player_data, '$.player.maxHP') as max_hp,
                        CASE WHEN op.username IS NOT NULL THEN 1 ELSE 0 END as is_online
                    FROM players p
                    LEFT JOIN online_players op ON LOWER(p.username) = LOWER(op.username)
                        AND op.last_heartbeat >= datetime('now', '-300 seconds')
                    WHERE p.is_banned = 0
                        AND p.player_data != '{}' AND LENGTH(p.player_data) > 2
                        AND (json_extract(p.player_data, '$.player.isImmortal') IS NULL
                             OR json_extract(p.player_data, '$.player.isImmortal') = 0)
                        AND json_extract(p.player_data, '$.player.level') IS NOT NULL
                        AND p.username NOT LIKE 'emergency_%'
                    ORDER BY COALESCE(json_extract(p.player_data, '$.player.level'), 0) DESC
                    LIMIT @limit;
                ";
                cmd.Parameters.AddWithValue("@limit", limit);
                using var reader = await cmd.ExecuteReaderAsync();
                while (await reader.ReadAsync())
                {
                    mortals.Add(new MortalPlayerInfo
                    {
                        DisplayName = reader.GetString(0),
                        Username = reader.IsDBNull(1) ? "" : reader.GetString(1),
                        Level = reader.IsDBNull(2) ? 1 : Convert.ToInt32(reader.GetValue(2)),
                        ClassId = reader.IsDBNull(3) ? 0 : Convert.ToInt32(reader.GetValue(3)),
                        WorshippedGod = reader.IsDBNull(4) ? "" : reader.GetString(4),
                        BlessingCombats = reader.IsDBNull(5) ? 0 : Convert.ToInt32(reader.GetValue(5)),
                        HP = reader.IsDBNull(6) ? 0 : Convert.ToInt64(reader.GetValue(6)),
                        MaxHP = reader.IsDBNull(7) ? 0 : Convert.ToInt64(reader.GetValue(7)),
                        IsOnline = reader.GetInt32(8) == 1
                    });
                }
            }
            catch (Exception ex)
            {
                DebugLogger.Instance.LogError("SQL", $"Failed to get mortal players: {ex.Message}");
            }
            return mortals;
        }

        public async Task<int> CountPlayerBelievers(string divineName)
        {
            try
            {
                using var connection = OpenConnection();
                using var cmd = connection.CreateCommand();
                cmd.CommandText = @"
                    SELECT COUNT(*) FROM players
                    WHERE json_extract(player_data, '$.player.worshippedGod') = @divineName
                    AND (json_extract(player_data, '$.player.isImmortal') IS NULL
                         OR json_extract(player_data, '$.player.isImmortal') = 0)
                    AND player_data != '{}' AND LENGTH(player_data) > 2
                    AND is_banned = 0 AND username NOT LIKE 'emergency_%';
                ";
                cmd.Parameters.AddWithValue("@divineName", divineName);
                var result = await Task.Run(() => cmd.ExecuteScalar());
                return Convert.ToInt32(result);
            }
            catch { return 0; }
        }

        public async Task ApplyDivineBlessing(string username, int combats, float bonus)
        {
            try
            {
                using var connection = OpenConnection();
                using var cmd = connection.CreateCommand();
                cmd.CommandText = @"
                    UPDATE players SET player_data = json_set(player_data,
                        '$.player.divineBlessingCombats', @combats,
                        '$.player.divineBlessingBonus', @bonus)
                    WHERE LOWER(username) = LOWER(@username)
                    AND player_data != '{}' AND LENGTH(player_data) > 2;
                ";
                cmd.Parameters.AddWithValue("@username", username);
                cmd.Parameters.AddWithValue("@combats", combats);
                cmd.Parameters.AddWithValue("@bonus", (double)bonus);
                await Task.Run(() => cmd.ExecuteNonQuery());
            }
            catch (Exception ex)
            {
                DebugLogger.Instance.LogError("SQL", $"Failed to apply divine blessing to {username}: {ex.Message}");
            }
        }

        public async Task ApplyDivineSmite(string username, float damagePercent)
        {
            try
            {
                using var connection = OpenConnection();
                using var cmd = connection.CreateCommand();
                cmd.CommandText = @"
                    UPDATE players SET player_data = json_set(player_data,
                        '$.player.hp',
                        MAX(1, CAST(json_extract(player_data, '$.player.hp') AS INTEGER)
                            - MAX(1, CAST(CAST(json_extract(player_data, '$.player.maxHP') AS INTEGER) * @pct AS INTEGER))))
                    WHERE LOWER(username) = LOWER(@username)
                    AND player_data != '{}' AND LENGTH(player_data) > 2;
                ";
                cmd.Parameters.AddWithValue("@username", username);
                cmd.Parameters.AddWithValue("@pct", (double)damagePercent);
                await Task.Run(() => cmd.ExecuteNonQuery());
            }
            catch (Exception ex)
            {
                DebugLogger.Instance.LogError("SQL", $"Failed to apply divine smite to {username}: {ex.Message}");
            }
        }

        public async Task SetPlayerWorshippedGod(string username, string divineName)
        {
            try
            {
                using var connection = OpenConnection();
                using var cmd = connection.CreateCommand();
                cmd.CommandText = @"
                    UPDATE players SET player_data = json_set(player_data,
                        '$.player.worshippedGod', @god)
                    WHERE LOWER(username) = LOWER(@username)
                    AND player_data != '{}' AND LENGTH(player_data) > 2;
                ";
                cmd.Parameters.AddWithValue("@username", username);
                cmd.Parameters.AddWithValue("@god", divineName);
                await Task.Run(() => cmd.ExecuteNonQuery());
            }
            catch (Exception ex)
            {
                DebugLogger.Instance.LogError("SQL", $"Failed to set worshipped god for {username}: {ex.Message}");
            }
        }

        public async Task AddGodExperience(string divineName, long amount)
        {
            try
            {
                using var connection = OpenConnection();
                using var cmd = connection.CreateCommand();
                cmd.CommandText = @"
                    UPDATE players SET player_data = json_set(player_data,
                        '$.player.godExperience',
                        CAST(json_extract(player_data, '$.player.godExperience') AS INTEGER) + @amount)
                    WHERE json_extract(player_data, '$.player.divineName') = @divineName
                    AND json_extract(player_data, '$.player.isImmortal') = 1
                    AND player_data != '{}' AND LENGTH(player_data) > 2;
                ";
                cmd.Parameters.AddWithValue("@divineName", divineName);
                cmd.Parameters.AddWithValue("@amount", amount);
                await Task.Run(() => cmd.ExecuteNonQuery());
            }
            catch (Exception ex)
            {
                DebugLogger.Instance.LogError("SQL", $"Failed to add god experience for {divineName}: {ex.Message}");
            }
        }

        public async Task<string> GetGodBoonConfig(string divineName)
        {
            try
            {
                using var connection = OpenConnection();
                using var cmd = connection.CreateCommand();
                cmd.CommandText = @"
                    SELECT json_extract(player_data, '$.player.divineBoonConfig')
                    FROM players
                    WHERE json_extract(player_data, '$.player.divineName') = @divineName
                    AND json_extract(player_data, '$.player.isImmortal') = 1
                    AND player_data != '{}' AND LENGTH(player_data) > 2
                    AND username NOT LIKE 'emergency_%'
                    LIMIT 1;
                ";
                cmd.Parameters.AddWithValue("@divineName", divineName);
                var result = await Task.Run(() => cmd.ExecuteScalar());
                return result != null && result != DBNull.Value ? result.ToString() ?? "" : "";
            }
            catch (Exception ex)
            {
                DebugLogger.Instance.LogError("SQL", $"Failed to get boon config for {divineName}: {ex.Message}");
                return "";
            }
        }

        public async Task SetGodBoonConfig(string username, string boonConfig)
        {
            try
            {
                using var connection = OpenConnection();
                using var cmd = connection.CreateCommand();
                cmd.CommandText = @"
                    UPDATE players SET player_data = json_set(player_data,
                        '$.player.divineBoonConfig', @config)
                    WHERE LOWER(username) = LOWER(@username)
                    AND player_data != '{}' AND LENGTH(player_data) > 2;
                ";
                cmd.Parameters.AddWithValue("@username", username);
                cmd.Parameters.AddWithValue("@config", boonConfig ?? "");
                await Task.Run(() => cmd.ExecuteNonQuery());
            }
            catch (Exception ex)
            {
                DebugLogger.Instance.LogError("SQL", $"Failed to set boon config for {username}: {ex.Message}");
            }
        }

        // --- Player Management ---

        public async Task<bool> IsPlayerBanned(string username)
        {
            try
            {
                using var connection = OpenConnection();
                using var cmd = connection.CreateCommand();
                cmd.CommandText = "SELECT is_banned FROM players WHERE LOWER(username) = LOWER(@username);";
                cmd.Parameters.AddWithValue("@username", username);
                var result = await cmd.ExecuteScalarAsync();
                return result != null && (long)result == 1;
            }
            catch (Exception ex)
            {
                DebugLogger.Instance.LogError("SQL", $"Failed to check ban status: {ex.Message}");
                return false;
            }
        }

        public async Task BanPlayer(string username, string reason)
        {
            try
            {
                // v0.60.5: capture the player's IP BEFORE removing them from
                // online_players, so the IP-ban can be applied. Falls back to
                // the persisted last_login_ip if they're not currently online.
                string? ipToBan = GetCurrentOnlineIpForPlayer(username) ?? GetLastLoginIpForPlayer(username);

                using var connection = OpenConnection();
                using var cmd = connection.CreateCommand();
                cmd.CommandText = @"
                    UPDATE players SET is_banned = 1, ban_reason = @reason WHERE LOWER(username) = LOWER(@username);
                ";
                cmd.Parameters.AddWithValue("@username", username);
                cmd.Parameters.AddWithValue("@reason", reason);
                await cmd.ExecuteNonQueryAsync();

                // Also remove from online players
                using var removeCmd = connection.CreateCommand();
                removeCmd.CommandText = "DELETE FROM online_players WHERE LOWER(username) = LOWER(@username);";
                removeCmd.Parameters.AddWithValue("@username", username);
                await removeCmd.ExecuteNonQueryAsync();

                // v0.60.5: full ban — also ban the IP and kick any active session.
                // Loopback addresses are excluded (would lock out the local game-engine
                // process testing, plus admins running ssh-proxy on the same host).
                if (!string.IsNullOrWhiteSpace(ipToBan)
                    && ipToBan != "127.0.0.1" && ipToBan != "::1" && ipToBan != "localhost")
                {
                    BanIp(ipToBan, $"Account ban cascade: {reason}", "BanPlayer", username);
                }

                // Kick the active session if the target is currently connected.
                // Static hook — wired by MudServer at startup so the SqlSaveBackend
                // doesn't need a hard reference to the server class.
                try { KickActiveSessionHook?.Invoke(username, $"Banned: {reason}"); }
                catch (Exception kex) { DebugLogger.Instance.LogWarning("BAN", $"Kick hook threw for '{username}': {kex.Message}"); }

                DebugLogger.Instance.LogInfo("BAN", $"Player '{username}' banned: {reason} (IP: {ipToBan ?? "unknown"})");
            }
            catch (Exception ex)
            {
                DebugLogger.Instance.LogError("SQL", $"Failed to ban player: {ex.Message}");
            }
        }

        /// <summary>
        /// v0.60.5: hook set by MudServer at startup so BanPlayer can drop the
        /// target's TCP session immediately. Signature: (username, reason).
        /// Static so it's available even if SqlSaveBackend is constructed before
        /// the MudServer (admin console / sysop setup paths).
        /// </summary>
        public static Action<string, string>? KickActiveSessionHook { get; set; }

        // =====================================================================
        // Admin Methods
        // =====================================================================

        public async Task UnbanPlayer(string username)
        {
            try
            {
                using var connection = OpenConnection();
                using var cmd = connection.CreateCommand();
                cmd.CommandText = "UPDATE players SET is_banned = 0, ban_reason = NULL WHERE LOWER(username) = LOWER(@username);";
                cmd.Parameters.AddWithValue("@username", username);
                await cmd.ExecuteNonQueryAsync();

                // v0.60.5: also lift any IP bans associated with this account
                using var ipCmd = connection.CreateCommand();
                ipCmd.CommandText = "DELETE FROM banned_ips WHERE LOWER(associated_username) = LOWER(@username);";
                ipCmd.Parameters.AddWithValue("@username", username);
                int ipRows = await ipCmd.ExecuteNonQueryAsync();
                if (ipRows > 0)
                    DebugLogger.Instance.LogInfo("BAN", $"Unban '{username}' also lifted {ipRows} associated IP ban(s).");
                DebugLogger.Instance.LogInfo("SQL", $"Player '{username}' unbanned by admin");
            }
            catch (Exception ex)
            {
                DebugLogger.Instance.LogError("SQL", $"Failed to unban player: {ex.Message}");
            }
        }

        // v0.60.5: IP-ban methods --------------------------------------------------

        /// <summary>
        /// Add an IP to the ban list. Synchronous because it's called from the
        /// session-accept hot path where async would risk a race with other
        /// connections. Idempotent: re-banning an already-banned IP just refreshes
        /// the reason/timestamp.
        /// </summary>
        public void BanIp(string ipAddress, string? reason, string? bannedBy, string? associatedUsername)
        {
            if (string.IsNullOrWhiteSpace(ipAddress)) return;
            try
            {
                using var connection = OpenConnection();
                using var cmd = connection.CreateCommand();
                cmd.CommandText = @"
                    INSERT INTO banned_ips (ip_address, reason, banned_at, banned_by, associated_username)
                    VALUES (@ip, @reason, datetime('now'), @by, @user)
                    ON CONFLICT(ip_address) DO UPDATE SET
                        reason = excluded.reason,
                        banned_at = excluded.banned_at,
                        banned_by = excluded.banned_by,
                        associated_username = excluded.associated_username;";
                cmd.Parameters.AddWithValue("@ip", ipAddress);
                cmd.Parameters.AddWithValue("@reason", (object?)reason ?? DBNull.Value);
                cmd.Parameters.AddWithValue("@by", (object?)bannedBy ?? DBNull.Value);
                cmd.Parameters.AddWithValue("@user", (object?)associatedUsername ?? DBNull.Value);
                cmd.ExecuteNonQuery();
                DebugLogger.Instance.LogInfo("BAN", $"IP banned: {ipAddress} (reason: {reason ?? "none"}, by: {bannedBy ?? "system"}, account: {associatedUsername ?? "none"})");
            }
            catch (Exception ex)
            {
                DebugLogger.Instance.LogError("BAN", $"Failed to ban IP {ipAddress}: {ex.Message}");
            }
        }

        public void UnbanIp(string ipAddress)
        {
            if (string.IsNullOrWhiteSpace(ipAddress)) return;
            try
            {
                using var connection = OpenConnection();
                using var cmd = connection.CreateCommand();
                cmd.CommandText = "DELETE FROM banned_ips WHERE ip_address = @ip;";
                cmd.Parameters.AddWithValue("@ip", ipAddress);
                cmd.ExecuteNonQuery();
                DebugLogger.Instance.LogInfo("BAN", $"IP unbanned: {ipAddress}");
            }
            catch (Exception ex)
            {
                DebugLogger.Instance.LogError("BAN", $"Failed to unban IP {ipAddress}: {ex.Message}");
            }
        }

        /// <summary>
        /// Cheap synchronous lookup. Returns the ban reason if banned, null otherwise.
        /// Empty/null IP always returns null (we never ban "no IP").
        /// v0.60.5: also checks CIDR ranges. If the IP matches an exact ban OR
        /// falls within a banned CIDR (e.g. "1.2.3.0/24"), returns the reason.
        /// CIDR scan is bounded by the count of CIDR rows (ones with "/" in the
        /// ip_address column), which is expected to be small (handful at most).
        /// </summary>
        public string? GetIpBanReason(string? ipAddress)
        {
            if (string.IsNullOrWhiteSpace(ipAddress)) return null;
            try
            {
                using var connection = OpenConnection();

                // Fast path: exact match.
                using (var exactCmd = connection.CreateCommand())
                {
                    exactCmd.CommandText = "SELECT COALESCE(reason, '') FROM banned_ips WHERE ip_address = @ip LIMIT 1;";
                    exactCmd.Parameters.AddWithValue("@ip", ipAddress);
                    var exact = exactCmd.ExecuteScalar();
                    if (exact != null) return exact.ToString() ?? "";
                }

                // CIDR scan: walk only rows that look like CIDR notation.
                using var cidrCmd = connection.CreateCommand();
                cidrCmd.CommandText = "SELECT ip_address, COALESCE(reason, '') FROM banned_ips WHERE ip_address LIKE '%/%';";
                using var reader = cidrCmd.ExecuteReader();
                while (reader.Read())
                {
                    var cidr = reader.GetString(0);
                    if (CidrContains(cidr, ipAddress))
                        return reader.GetString(1);
                }
                return null;
            }
            catch { return null; }
        }

        public bool IsIpBanned(string? ipAddress) => GetIpBanReason(ipAddress) != null;

        /// <summary>
        /// v0.60.5: returns true if <paramref name="ipAddress"/> falls within the
        /// CIDR range <paramref name="cidr"/> (e.g. "1.2.3.0/24"). Supports IPv4
        /// and IPv6. Returns false on any parse error rather than throwing — a
        /// malformed CIDR row in the DB shouldn't crash the connection-accept path.
        /// </summary>
        internal static bool CidrContains(string cidr, string ipAddress)
        {
            if (string.IsNullOrWhiteSpace(cidr) || string.IsNullOrWhiteSpace(ipAddress)) return false;
            var slash = cidr.IndexOf('/');
            if (slash < 0) return false;
            var networkPart = cidr.Substring(0, slash);
            var maskPart = cidr.Substring(slash + 1);
            if (!System.Net.IPAddress.TryParse(networkPart, out var network)) return false;
            if (!System.Net.IPAddress.TryParse(ipAddress, out var addr)) return false;
            if (network.AddressFamily != addr.AddressFamily) return false;
            if (!int.TryParse(maskPart, out var maskBits)) return false;

            var netBytes = network.GetAddressBytes();
            var ipBytes = addr.GetAddressBytes();
            if (netBytes.Length != ipBytes.Length) return false;
            int maxBits = netBytes.Length * 8;
            if (maskBits < 0 || maskBits > maxBits) return false;

            int fullBytes = maskBits / 8;
            int remBits = maskBits % 8;
            for (int i = 0; i < fullBytes; i++)
                if (netBytes[i] != ipBytes[i]) return false;
            if (remBits > 0 && fullBytes < netBytes.Length)
            {
                int mask = (0xFF << (8 - remBits)) & 0xFF;
                if ((netBytes[fullBytes] & mask) != (ipBytes[fullBytes] & mask)) return false;
            }
            return true;
        }

        /// <summary>
        /// Look up the last-known login IP for a player. Used by BanPlayer to
        /// also IP-ban the player at the moment of account ban. Returns null if
        /// no IP is recorded (player never logged in since the column was added).
        /// </summary>
        public string? GetLastLoginIpForPlayer(string username)
        {
            if (string.IsNullOrWhiteSpace(username)) return null;
            try
            {
                using var connection = OpenConnection();
                using var cmd = connection.CreateCommand();
                cmd.CommandText = "SELECT last_login_ip FROM players WHERE LOWER(username) = LOWER(@u) AND last_login_ip IS NOT NULL AND last_login_ip != '' LIMIT 1;";
                cmd.Parameters.AddWithValue("@u", username);
                var result = cmd.ExecuteScalar();
                return result == null || result == DBNull.Value ? null : result.ToString();
            }
            catch { return null; }
        }

        /// <summary>
        /// Look up the IP a currently-online player is connected from. Returns
        /// null if they're not currently online or no IP is recorded.
        /// </summary>
        public string? GetCurrentOnlineIpForPlayer(string username)
        {
            if (string.IsNullOrWhiteSpace(username)) return null;
            try
            {
                using var connection = OpenConnection();
                using var cmd = connection.CreateCommand();
                cmd.CommandText = "SELECT ip_address FROM online_players WHERE LOWER(username) = LOWER(@u) AND ip_address IS NOT NULL AND ip_address != '' LIMIT 1;";
                cmd.Parameters.AddWithValue("@u", username);
                var result = cmd.ExecuteScalar();
                return result == null || result == DBNull.Value ? null : result.ToString();
            }
            catch { return null; }
        }

        public List<(string ip, string? reason, string? bannedBy, string? associatedUsername, string? bannedAt)> GetBannedIps()
        {
            var result = new List<(string, string?, string?, string?, string?)>();
            try
            {
                using var connection = OpenConnection();
                using var cmd = connection.CreateCommand();
                cmd.CommandText = "SELECT ip_address, reason, banned_by, associated_username, banned_at FROM banned_ips ORDER BY banned_at DESC;";
                using var reader = cmd.ExecuteReader();
                while (reader.Read())
                {
                    result.Add((
                        reader.GetString(0),
                        reader.IsDBNull(1) ? null : reader.GetString(1),
                        reader.IsDBNull(2) ? null : reader.GetString(2),
                        reader.IsDBNull(3) ? null : reader.GetString(3),
                        reader.IsDBNull(4) ? null : reader.GetString(4)
                    ));
                }
            }
            catch (Exception ex) { DebugLogger.Instance.LogError("BAN", $"Failed to list banned IPs: {ex.Message}"); }
            return result;
        }

        // -------------------------------------------------------------------------

        public async Task<List<(string username, string displayName, string? banReason)>> GetBannedPlayers()
        {
            var banned = new List<(string, string, string?)>();
            try
            {
                using var connection = OpenConnection();
                using var cmd = connection.CreateCommand();
                cmd.CommandText = "SELECT username, display_name, ban_reason FROM players WHERE is_banned = 1 ORDER BY display_name;";
                using var reader = await cmd.ExecuteReaderAsync();
                while (await reader.ReadAsync())
                {
                    banned.Add((
                        reader.GetString(0),
                        reader.GetString(1),
                        reader.IsDBNull(2) ? null : reader.GetString(2)
                    ));
                }
            }
            catch (Exception ex)
            {
                DebugLogger.Instance.LogError("SQL", $"Failed to get banned players: {ex.Message}");
            }
            return banned;
        }

        public async Task<List<AdminPlayerInfo>> GetAllPlayersDetailed()
        {
            var players = new List<AdminPlayerInfo>();
            try
            {
                using var connection = OpenConnection();
                using var cmd = connection.CreateCommand();
                cmd.CommandText = @"
                    SELECT
                        p.username, p.display_name, p.is_banned, p.ban_reason,
                        p.last_login, p.created_at, p.total_playtime_minutes,
                        json_extract(p.player_data, '$.player.level') as level,
                        json_extract(p.player_data, '$.player.class') as class_id,
                        json_extract(p.player_data, '$.player.gold') as gold,
                        json_extract(p.player_data, '$.player.experience') as xp,
                        CASE WHEN op.username IS NOT NULL THEN 1 ELSE 0 END as is_online
                    FROM players p
                    LEFT JOIN online_players op ON LOWER(p.username) = LOWER(op.username)
                        AND op.last_heartbeat >= datetime('now', '-300 seconds')
                    ORDER BY p.display_name;
                ";
                using var reader = await cmd.ExecuteReaderAsync();
                while (await reader.ReadAsync())
                {
                    players.Add(new AdminPlayerInfo
                    {
                        Username = reader.GetString(0),
                        DisplayName = reader.GetString(1),
                        IsBanned = reader.GetInt32(2) != 0,
                        BanReason = reader.IsDBNull(3) ? null : reader.GetString(3),
                        LastLogin = reader.IsDBNull(4) ? null : reader.GetString(4),
                        CreatedAt = reader.IsDBNull(5) ? null : reader.GetString(5),
                        TotalPlaytimeMinutes = reader.IsDBNull(6) ? 0 : reader.GetInt32(6),
                        Level = reader.IsDBNull(7) ? 0 : Convert.ToInt32(reader.GetValue(7)),
                        ClassId = reader.IsDBNull(8) ? 0 : Convert.ToInt32(reader.GetValue(8)),
                        Gold = reader.IsDBNull(9) ? 0 : Convert.ToInt64(reader.GetValue(9)),
                        Experience = reader.IsDBNull(10) ? 0 : Convert.ToInt64(reader.GetValue(10)),
                        IsOnline = reader.GetInt32(11) != 0
                    });
                }
            }
            catch (Exception ex)
            {
                DebugLogger.Instance.LogError("SQL", $"Failed to get detailed player list: {ex.Message}");
            }
            return players;
        }

        public async Task ClearAllNews()
        {
            try
            {
                using var connection = OpenConnection();
                using var cmd = connection.CreateCommand();
                cmd.CommandText = "DELETE FROM news;";
                var affected = await cmd.ExecuteNonQueryAsync();
                DebugLogger.Instance.LogInfo("SQL", $"News table cleared by admin ({affected} entries)");
            }
            catch (Exception ex)
            {
                DebugLogger.Instance.LogError("SQL", $"Failed to clear news: {ex.Message}");
            }
        }

        public async Task FullGameReset()
        {
            try
            {
                using var connection = OpenConnection();
                using var transaction = connection.BeginTransaction();

                // Clear all player save data (preserve accounts/passwords)
                using (var cmd = connection.CreateCommand())
                {
                    cmd.Transaction = transaction;
                    cmd.CommandText = "UPDATE players SET player_data = '{}', is_banned = 0, ban_reason = NULL;";
                    await cmd.ExecuteNonQueryAsync();
                }

                // Clear all game data tables in a single batch
                using (var cmd = connection.CreateCommand())
                {
                    cmd.Transaction = transaction;
                    cmd.CommandText = @"
                        DELETE FROM world_state;
                        DELETE FROM news;
                        DELETE FROM messages;
                        DELETE FROM online_players;
                        DELETE FROM pvp_log;
                        DELETE FROM player_teams;
                        DELETE FROM team_vault;
                        DELETE FROM team_upgrades;
                        DELETE FROM team_wars;
                        DELETE FROM trade_offers;
                        DELETE FROM bounties;
                        DELETE FROM auction_listings;
                        DELETE FROM world_bosses;
                        DELETE FROM world_boss_damage;
                        DELETE FROM castle_sieges;
                        DELETE FROM wizard_flags;
                        DELETE FROM sleeping_players;
                    ";
                    await cmd.ExecuteNonQueryAsync();
                }

                transaction.Commit();
                DebugLogger.Instance.LogWarning("SQL", "Full game reset performed by admin (all 18 tables wiped)");
            }
            catch (Exception ex)
            {
                DebugLogger.Instance.LogError("SQL", $"Failed to perform full game reset: {ex.Message}");
                throw;
            }
        }

        /// <summary>
        /// Get comprehensive game statistics for the SysOp console
        /// </summary>
        public async Task<SysOpGameStats> GetGameStatistics()
        {
            var stats = new SysOpGameStats();
            try
            {
                using var connection = OpenConnection();

                // Player stats + aggregate economy/combat in one query
                using (var cmd = connection.CreateCommand())
                {
                    cmd.CommandText = @"
                        SELECT
                            COUNT(*) as total_players,
                            SUM(CASE WHEN p.is_banned = 1 THEN 1 ELSE 0 END) as banned_count,
                            SUM(CASE WHEN op.username IS NOT NULL THEN 1 ELSE 0 END) as online_count,
                            SUM(CASE WHEN p.player_data != '{}' AND p.player_data IS NOT NULL THEN 1 ELSE 0 END) as active_count,
                            MAX(COALESCE(json_extract(p.player_data, '$.player.level'), 0)) as max_level,
                            AVG(CASE WHEN json_extract(p.player_data, '$.player.level') > 0
                                THEN json_extract(p.player_data, '$.player.level') END) as avg_level,
                            SUM(COALESCE(json_extract(p.player_data, '$.player.gold'), 0)) as total_gold,
                            SUM(COALESCE(json_extract(p.player_data, '$.player.bankGold'), 0)) as total_bank_gold,
                            SUM(COALESCE(json_extract(p.player_data, '$.player.statistics.totalMonstersKilled'), 0)) as total_monsters_killed,
                            SUM(COALESCE(json_extract(p.player_data, '$.player.statistics.totalBossesKilled'), 0)) as total_bosses_killed,
                            SUM(COALESCE(json_extract(p.player_data, '$.player.statistics.totalPlayerKills'), 0)) as total_pvp_kills,
                            SUM(COALESCE(json_extract(p.player_data, '$.player.statistics.totalMonsterDeaths'), 0)) as total_pve_deaths,
                            SUM(COALESCE(json_extract(p.player_data, '$.player.statistics.totalDamageDealt'), 0)) as total_damage_dealt,
                            SUM(COALESCE(json_extract(p.player_data, '$.player.statistics.totalGoldEarned'), 0)) as total_gold_earned,
                            SUM(COALESCE(json_extract(p.player_data, '$.player.statistics.totalGoldSpent'), 0)) as total_gold_spent,
                            SUM(COALESCE(json_extract(p.player_data, '$.player.statistics.totalItemsBought'), 0)) as total_items_bought,
                            SUM(COALESCE(json_extract(p.player_data, '$.player.statistics.totalItemsSold'), 0)) as total_items_sold,
                            MAX(COALESCE(json_extract(p.player_data, '$.player.statistics.deepestDungeonLevel'), 0)) as deepest_dungeon,
                            SUM(p.total_playtime_minutes) as total_playtime
                        FROM players p
                        LEFT JOIN online_players op ON LOWER(p.username) = LOWER(op.username)
                            AND op.last_heartbeat >= datetime('now', '-300 seconds');
                    ";
                    using var reader = await cmd.ExecuteReaderAsync();
                    if (await reader.ReadAsync())
                    {
                        stats.TotalPlayers = reader.IsDBNull(0) ? 0 : reader.GetInt32(0);
                        stats.BannedPlayers = reader.IsDBNull(1) ? 0 : Convert.ToInt32(reader.GetValue(1));
                        stats.OnlinePlayers = reader.IsDBNull(2) ? 0 : Convert.ToInt32(reader.GetValue(2));
                        stats.ActivePlayers = reader.IsDBNull(3) ? 0 : Convert.ToInt32(reader.GetValue(3));
                        stats.HighestLevel = reader.IsDBNull(4) ? 0 : Convert.ToInt32(reader.GetValue(4));
                        stats.AverageLevel = reader.IsDBNull(5) ? 0 : Convert.ToDouble(reader.GetValue(5));
                        stats.TotalGoldOnHand = reader.IsDBNull(6) ? 0 : Convert.ToInt64(reader.GetValue(6));
                        stats.TotalBankGold = reader.IsDBNull(7) ? 0 : Convert.ToInt64(reader.GetValue(7));
                        stats.TotalMonstersKilled = reader.IsDBNull(8) ? 0 : Convert.ToInt64(reader.GetValue(8));
                        stats.TotalBossesKilled = reader.IsDBNull(9) ? 0 : Convert.ToInt64(reader.GetValue(9));
                        stats.TotalPvPKills = reader.IsDBNull(10) ? 0 : Convert.ToInt64(reader.GetValue(10));
                        stats.TotalPvEDeaths = reader.IsDBNull(11) ? 0 : Convert.ToInt64(reader.GetValue(11));
                        stats.TotalDamageDealt = reader.IsDBNull(12) ? 0 : Convert.ToInt64(reader.GetValue(12));
                        stats.TotalGoldEarned = reader.IsDBNull(13) ? 0 : Convert.ToInt64(reader.GetValue(13));
                        stats.TotalGoldSpent = reader.IsDBNull(14) ? 0 : Convert.ToInt64(reader.GetValue(14));
                        stats.TotalItemsBought = reader.IsDBNull(15) ? 0 : Convert.ToInt64(reader.GetValue(15));
                        stats.TotalItemsSold = reader.IsDBNull(16) ? 0 : Convert.ToInt64(reader.GetValue(16));
                        stats.DeepestDungeon = reader.IsDBNull(17) ? 0 : Convert.ToInt32(reader.GetValue(17));
                        stats.TotalPlaytimeMinutes = reader.IsDBNull(18) ? 0 : Convert.ToInt64(reader.GetValue(18));
                    }
                }

                // Top player by level
                using (var cmd = connection.CreateCommand())
                {
                    cmd.CommandText = @"
                        SELECT p.display_name,
                               json_extract(p.player_data, '$.player.level') as level,
                               json_extract(p.player_data, '$.player.class') as class_id
                        FROM players p
                        WHERE p.player_data != '{}' AND p.player_data IS NOT NULL
                        ORDER BY COALESCE(json_extract(p.player_data, '$.player.level'), 0) DESC
                        LIMIT 1;
                    ";
                    using var reader = await cmd.ExecuteReaderAsync();
                    if (await reader.ReadAsync())
                    {
                        stats.TopPlayerName = reader.IsDBNull(0) ? "" : reader.GetString(0);
                        stats.TopPlayerLevel = reader.IsDBNull(1) ? 0 : Convert.ToInt32(reader.GetValue(1));
                        stats.TopPlayerClassId = reader.IsDBNull(2) ? 0 : Convert.ToInt32(reader.GetValue(2));
                    }
                }

                // Most popular class
                using (var cmd = connection.CreateCommand())
                {
                    cmd.CommandText = @"
                        SELECT json_extract(p.player_data, '$.player.class') as class_id, COUNT(*) as cnt
                        FROM players p
                        WHERE p.player_data != '{}' AND p.player_data IS NOT NULL
                          AND json_extract(p.player_data, '$.player.class') IS NOT NULL
                        GROUP BY class_id ORDER BY cnt DESC LIMIT 1;
                    ";
                    using var reader = await cmd.ExecuteReaderAsync();
                    if (await reader.ReadAsync())
                    {
                        stats.MostPopularClassId = reader.IsDBNull(0) ? -1 : Convert.ToInt32(reader.GetValue(0));
                        stats.MostPopularClassCount = reader.IsDBNull(1) ? 0 : reader.GetInt32(1);
                    }
                }

                // Table counts for server health
                using (var cmd = connection.CreateCommand())
                {
                    cmd.CommandText = @"
                        SELECT
                            (SELECT COUNT(*) FROM news) as news_count,
                            (SELECT COUNT(*) FROM messages) as msg_count,
                            (SELECT COUNT(*) FROM pvp_log) as pvp_count,
                            (SELECT COUNT(*) FROM player_teams) as team_count,
                            (SELECT COUNT(*) FROM bounties WHERE status = 'active') as bounty_count,
                            (SELECT COUNT(*) FROM auction_listings WHERE status = 'active') as auction_count;
                    ";
                    using var reader = await cmd.ExecuteReaderAsync();
                    if (await reader.ReadAsync())
                    {
                        stats.NewsEntries = reader.IsDBNull(0) ? 0 : reader.GetInt32(0);
                        stats.TotalMessages = reader.IsDBNull(1) ? 0 : reader.GetInt32(1);
                        stats.TotalPvPFights = reader.IsDBNull(2) ? 0 : reader.GetInt32(2);
                        stats.ActiveTeams = reader.IsDBNull(3) ? 0 : reader.GetInt32(3);
                        stats.ActiveBounties = reader.IsDBNull(4) ? 0 : reader.GetInt32(4);
                        stats.ActiveAuctions = reader.IsDBNull(5) ? 0 : reader.GetInt32(5);
                    }
                }

                // Newest player
                using (var cmd = connection.CreateCommand())
                {
                    cmd.CommandText = @"
                        SELECT display_name, created_at FROM players
                        ORDER BY created_at DESC LIMIT 1;
                    ";
                    using var reader = await cmd.ExecuteReaderAsync();
                    if (await reader.ReadAsync())
                    {
                        stats.NewestPlayerName = reader.IsDBNull(0) ? "" : reader.GetString(0);
                        stats.NewestPlayerDate = reader.IsDBNull(1) ? null : reader.GetString(1);
                    }
                }

                // Database file size
                try
                {
                    var dbPath = connectionString.Replace("Data Source=", "").Trim();
                    if (File.Exists(dbPath))
                    {
                        stats.DatabaseSizeBytes = new FileInfo(dbPath).Length;
                    }
                }
                catch { /* ignore */ }
            }
            catch (Exception ex)
            {
                DebugLogger.Instance.LogError("SQL", $"Failed to get game statistics: {ex.Message}");
            }
            return stats;
        }

        public async Task UpdatePlayerSession(string username, bool isLogin, string? ipAddress = null)
        {
            try
            {
                using var connection = OpenConnection();
                using var cmd = connection.CreateCommand();

                if (isLogin)
                {
                    // v0.60.5: persist last_login_ip on every login so BanPlayer
                    // can find the IP to ban even if the player isn't currently
                    // online (offline-ban scenario from the admin dashboard).
                    if (!string.IsNullOrWhiteSpace(ipAddress))
                    {
                        cmd.CommandText = "UPDATE players SET last_login = datetime('now'), last_login_ip = @ip WHERE LOWER(username) = LOWER(@username);";
                        cmd.Parameters.AddWithValue("@ip", ipAddress);
                    }
                    else
                    {
                        cmd.CommandText = "UPDATE players SET last_login = datetime('now') WHERE LOWER(username) = LOWER(@username);";
                    }
                }
                else
                {
                    // On logout, update last_logout and accumulate playtime
                    cmd.CommandText = @"
                        UPDATE players SET
                            last_logout = datetime('now'),
                            total_playtime_minutes = total_playtime_minutes +
                                CAST((julianday('now') - julianday(COALESCE(last_login, datetime('now')))) * 1440 AS INTEGER)
                        WHERE LOWER(username) = LOWER(@username);
                    ";
                }

                cmd.Parameters.AddWithValue("@username", username);
                await cmd.ExecuteNonQueryAsync();
            }
            catch (Exception ex)
            {
                DebugLogger.Instance.LogError("SQL", $"Failed to update player session: {ex.Message}");
            }
        }

        // =====================================================================
        // Player Authentication
        // =====================================================================

        /// <summary>
        /// Hash a password using PBKDF2 with a random salt.
        /// Returns "salt:hash" as a base64 string pair.
        /// </summary>
        private static string HashPassword(string password)
        {
            byte[] salt = new byte[16];
            using (var rng = System.Security.Cryptography.RandomNumberGenerator.Create())
            {
                rng.GetBytes(salt);
            }

            using var pbkdf2 = new System.Security.Cryptography.Rfc2898DeriveBytes(
                password, salt, iterations: 100000, System.Security.Cryptography.HashAlgorithmName.SHA256);
            byte[] hash = pbkdf2.GetBytes(32);

            return Convert.ToBase64String(salt) + ":" + Convert.ToBase64String(hash);
        }

        /// <summary>
        /// Verify a password against a stored "salt:hash" string.
        /// </summary>
        private static bool VerifyPassword(string password, string storedHash)
        {
            if (string.IsNullOrEmpty(storedHash)) return false;

            var parts = storedHash.Split(':');
            if (parts.Length != 2) return false;

            byte[] salt = Convert.FromBase64String(parts[0]);
            byte[] expectedHash = Convert.FromBase64String(parts[1]);

            using var pbkdf2 = new System.Security.Cryptography.Rfc2898DeriveBytes(
                password, salt, iterations: 100000, System.Security.Cryptography.HashAlgorithmName.SHA256);
            byte[] actualHash = pbkdf2.GetBytes(32);

            // Constant-time comparison to prevent timing attacks
            return System.Security.Cryptography.CryptographicOperations.FixedTimeEquals(actualHash, expectedHash);
        }

        /// <summary>
        /// Register a new player account. Returns true if successful, false if username taken.
        /// </summary>
        /// <summary>
        /// v1.0 release prep (B1a): record an onboarding-funnel milestone.
        /// Idempotent (UNIQUE(username, event) + INSERT OR IGNORE), so callers
        /// can fire from hot paths without once-only bookkeeping. Best-effort:
        /// telemetry failure never breaks gameplay.
        /// </summary>
        public void RecordOnboardingEvent(string username, string eventName, string? connectionType = null)
        {
            try
            {
                if (string.IsNullOrWhiteSpace(username) || string.IsNullOrWhiteSpace(eventName)) return;
                using var connection = OpenConnection();
                using var cmd = connection.CreateCommand();
                cmd.CommandText = @"
                    INSERT OR IGNORE INTO onboarding_events (username, event, connection_type)
                    VALUES (@username, @event, @ctype);";
                cmd.Parameters.AddWithValue("@username", username.ToLowerInvariant());
                cmd.Parameters.AddWithValue("@event", eventName);
                cmd.Parameters.AddWithValue("@ctype", (object?)connectionType ?? DBNull.Value);
                cmd.ExecuteNonQuery();
            }
            catch (Exception ex)
            {
                DebugLogger.Instance.LogDebug("FUNNEL", $"RecordOnboardingEvent failed: {ex.Message}");
            }
        }

        public async Task<(bool success, string message)> RegisterPlayer(string username, string password, string? ipAddress = null)
        {
            // v0.60.5: full ban means no new accounts from this IP either. Same
            // defense-in-depth pattern as AuthenticatePlayer.
            if (!string.IsNullOrWhiteSpace(ipAddress) && IsIpBanned(ipAddress))
            {
                return (false, "Registration refused: this address is banned from the server.");
            }

            try
            {
                if (string.IsNullOrWhiteSpace(username) || username.Length < 2 || username.Length > 20)
                    return (false, "Username must be 2-20 characters.");

                if (password.Length < 4)
                    return (false, "Password must be at least 4 characters.");

                // Block reserved alt character suffix
                if (username.Contains(GameConfig.AltCharacterSuffix, StringComparison.OrdinalIgnoreCase))
                    return (false, "Username contains reserved characters.");

                // Check for valid characters (alphanumeric, spaces, hyphens, underscores)
                foreach (char c in username)
                {
                    if (!char.IsLetterOrDigit(c) && c != ' ' && c != '-' && c != '_')
                        return (false, "Username can only contain letters, numbers, spaces, hyphens, and underscores.");
                }

                using var connection = OpenConnection();

                // Check if username already exists
                using (var checkCmd = connection.CreateCommand())
                {
                    checkCmd.CommandText = "SELECT COUNT(*) FROM players WHERE LOWER(username) = LOWER(@username);";
                    checkCmd.Parameters.AddWithValue("@username", username);
                    var count = Convert.ToInt64(await checkCmd.ExecuteScalarAsync());
                    if (count > 0)
                        return (false, "That username is already taken.");
                }

                // v0.60.5: per-IP registration rate limit. Loopback skipped so
                // local testing isn't blocked. Mismatched casing (LOWER not
                // applied to created_ip) is fine -- IP strings are stored as-is.
                if (!string.IsNullOrWhiteSpace(ipAddress)
                    && ipAddress != "127.0.0.1" && ipAddress != "::1" && ipAddress != "localhost")
                {
                    using var rateCmd = connection.CreateCommand();
                    rateCmd.CommandText = @"
                        SELECT COUNT(*) FROM players
                        WHERE created_ip = @ip
                        AND created_at > datetime('now', '-24 hours')";
                    rateCmd.Parameters.AddWithValue("@ip", ipAddress);
                    var recentCount = Convert.ToInt64(await rateCmd.ExecuteScalarAsync());
                    if (recentCount >= GameConfig.MaxRegistrationsPerIpPer24h)
                    {
                        DebugLogger.Instance.LogWarning("BAN",
                            $"Registration rate-limited for IP {ipAddress}: {recentCount} accounts in last 24h (cap {GameConfig.MaxRegistrationsPerIpPer24h})");
                        return (false, "Too many accounts registered from this address recently. Try again tomorrow.");
                    }
                }

                // Check if banned
                using (var banCmd = connection.CreateCommand())
                {
                    banCmd.CommandText = "SELECT COUNT(*) FROM banned_names WHERE LOWER(name) = LOWER(@username);";
                    banCmd.Parameters.AddWithValue("@username", username);
                    try
                    {
                        var count = Convert.ToInt64(await banCmd.ExecuteScalarAsync());
                        if (count > 0)
                            return (false, "That username is not available.");
                    }
                    catch { /* banned_names table may not exist yet, that's fine */ }
                }

                // Insert the new player with hashed password and empty player data.
                // v0.60.5: also persist created_ip so the rate limiter can count
                // future registrations from this address.
                string passwordHash = HashPassword(password);
                using var insertCmd = connection.CreateCommand();
                insertCmd.CommandText = @"
                    INSERT INTO players (username, display_name, password_hash, player_data, created_at, created_ip)
                    VALUES (@username, @display_name, @password_hash, '{}', datetime('now'), @created_ip);
                ";
                insertCmd.Parameters.AddWithValue("@username", username.ToLower());
                insertCmd.Parameters.AddWithValue("@display_name", username);
                insertCmd.Parameters.AddWithValue("@password_hash", passwordHash);
                insertCmd.Parameters.AddWithValue("@created_ip", (object?)ipAddress ?? DBNull.Value);
                await insertCmd.ExecuteNonQueryAsync();

                DebugLogger.Instance.LogInfo("SQL", $"New player registered: '{username}'");

                // v1.0 release prep (B1a): funnel milestone 1 of 5.
                RecordOnboardingEvent(username,
                    "account_created",
                    UsurperRemake.Server.SessionContext.Current?.ConnectionType);

                return (true, "Account created successfully!");
            }
            catch (Exception ex)
            {
                DebugLogger.Instance.LogError("SQL", $"Failed to register player: {ex.Message}");
                return (false, "Registration failed. Please try again.");
            }
        }

        /// <summary>
        /// Auto-provision a player account for trusted auth (no password).
        /// Used by --auto-provision flag for BBS passthrough connections where
        /// the BBS already handles user authentication.
        /// </summary>
        public async Task<(bool success, string message)> AutoProvisionPlayer(string username)
        {
            try
            {
                if (string.IsNullOrWhiteSpace(username) || username.Length < 2 || username.Length > 20)
                    return (false, "Username must be 2-20 characters.");

                // Block reserved alt character suffix
                if (username.Contains(GameConfig.AltCharacterSuffix, StringComparison.OrdinalIgnoreCase))
                    return (false, "Username contains reserved characters.");

                // Check for valid characters (alphanumeric, spaces, hyphens, underscores)
                foreach (char c in username)
                {
                    if (!char.IsLetterOrDigit(c) && c != ' ' && c != '-' && c != '_')
                        return (false, "Username can only contain letters, numbers, spaces, hyphens, and underscores.");
                }

                using var connection = OpenConnection();

                // Check if username already exists
                using (var checkCmd = connection.CreateCommand())
                {
                    checkCmd.CommandText = "SELECT COUNT(*) FROM players WHERE LOWER(username) = LOWER(@username);";
                    checkCmd.Parameters.AddWithValue("@username", username);
                    var count = Convert.ToInt64(await checkCmd.ExecuteScalarAsync());
                    if (count > 0)
                        return (true, "Account already exists."); // Not an error — just means no provisioning needed
                }

                // Check if banned
                using (var banCmd = connection.CreateCommand())
                {
                    banCmd.CommandText = "SELECT COUNT(*) FROM banned_names WHERE LOWER(name) = LOWER(@username);";
                    banCmd.Parameters.AddWithValue("@username", username);
                    try
                    {
                        var count = Convert.ToInt64(await banCmd.ExecuteScalarAsync());
                        if (count > 0)
                            return (false, "That username is not available.");
                    }
                    catch { /* banned_names table may not exist yet, that's fine */ }
                }

                // Insert with empty password_hash (trusted auth only — no password needed)
                using var insertCmd = connection.CreateCommand();
                insertCmd.CommandText = @"
                    INSERT INTO players (username, display_name, password_hash, player_data, created_at)
                    VALUES (@username, @display_name, '', '{}', datetime('now'));
                ";
                insertCmd.Parameters.AddWithValue("@username", username.ToLower());
                insertCmd.Parameters.AddWithValue("@display_name", username);
                await insertCmd.ExecuteNonQueryAsync();

                DebugLogger.Instance.LogInfo("SQL", $"Auto-provisioned account: '{username}'");
                return (true, "Account auto-provisioned.");
            }
            catch (Exception ex)
            {
                DebugLogger.Instance.LogError("SQL", $"Failed to auto-provision player: {ex.Message}");
                return (false, "Account creation failed. Please try again.");
            }
        }

        /// <summary>
        /// Authenticate a player. Returns (success, displayName, message).
        /// </summary>
        public async Task<(bool success, string displayName, string message, bool screenReader, string language)> AuthenticatePlayer(string username, string password, string? ipAddress = null)
        {
            // v0.60.5: defense-in-depth IP check. The MudServer accept-time check
            // should already drop banned-IP connections before they reach this
            // method, but adding it here protects any future code path that
            // calls AuthenticatePlayer directly without going through the accept
            // gate (e.g., a new SSH gateway, an HTTP login endpoint).
            if (!string.IsNullOrWhiteSpace(ipAddress) && IsIpBanned(ipAddress))
            {
                return (false, "", "Login refused: this address is banned from the server.", false, "en");
            }

            try
            {
                using var connection = OpenConnection();
                using var cmd = connection.CreateCommand();
                cmd.CommandText = "SELECT display_name, password_hash, is_banned, ban_reason, COALESCE(screen_reader, 0), COALESCE(language, 'en') FROM players WHERE LOWER(username) = LOWER(@username);";
                cmd.Parameters.AddWithValue("@username", username);

                using var reader = await cmd.ExecuteReaderAsync();
                if (!await reader.ReadAsync())
                    return (false, "", "Unknown username. Type 'R' to register a new account.", false, "en");

                string displayName = reader.GetString(0);
                string storedHash = reader.GetString(1);
                bool isBanned = reader.GetInt32(2) != 0;
                string? banReason = reader.IsDBNull(3) ? null : reader.GetString(3);
                bool screenReader = reader.GetInt32(4) != 0;
                string language = reader.IsDBNull(5) ? "en" : reader.GetString(5);

                if (isBanned)
                {
                    string msg = "Your account has been banned.";
                    if (!string.IsNullOrEmpty(banReason))
                        msg += $" Reason: {banReason}";
                    return (false, "", msg, false, "en");
                }

                if (!VerifyPassword(password, storedHash))
                    return (false, "", "Incorrect password.", false, "en");

                return (true, displayName, "Login successful!", screenReader, language);
            }
            catch (Exception ex)
            {
                DebugLogger.Instance.LogError("SQL", $"Failed to authenticate player: {ex.Message}");
                return (false, "", "Authentication failed. Please try again.", false, "en");
            }
        }

        /// <summary>
        /// Change a player's password. Returns true if successful.
        /// </summary>
        public async Task<(bool success, string message)> ChangePassword(string username, string oldPassword, string newPassword)
        {
            try
            {
                // Verify old password first
                var (authenticated, _, _, _, _) = await AuthenticatePlayer(username, oldPassword);
                if (!authenticated)
                    return (false, "Current password is incorrect.");

                if (newPassword.Length < 4)
                    return (false, "New password must be at least 4 characters.");

                string newHash = HashPassword(newPassword);
                using var connection = OpenConnection();
                using var cmd = connection.CreateCommand();
                cmd.CommandText = "UPDATE players SET password_hash = @hash WHERE LOWER(username) = LOWER(@username);";
                cmd.Parameters.AddWithValue("@hash", newHash);
                cmd.Parameters.AddWithValue("@username", username);
                await cmd.ExecuteNonQueryAsync();

                DebugLogger.Instance.LogInfo("SQL", $"Password changed for: '{username}'");
                return (true, "Password changed successfully!");
            }
            catch (Exception ex)
            {
                DebugLogger.Instance.LogError("SQL", $"Failed to change password: {ex.Message}");
                return (false, "Password change failed. Please try again.");
            }
        }

        /// <summary>
        /// Admin force-reset a player's password (no old password required).
        /// </summary>
        public (bool success, string message) AdminResetPassword(string username, string newPassword)
        {
            try
            {
                if (newPassword.Length < 4)
                    return (false, "New password must be at least 4 characters.");

                // Verify user exists
                using var connection = OpenConnection();
                using var checkCmd = connection.CreateCommand();
                checkCmd.CommandText = "SELECT COUNT(*) FROM players WHERE LOWER(username) = LOWER(@username);";
                checkCmd.Parameters.AddWithValue("@username", username);
                var count = Convert.ToInt64(checkCmd.ExecuteScalar());
                if (count == 0)
                    return (false, $"Player '{username}' not found.");

                string newHash = HashPassword(newPassword);
                using var cmd = connection.CreateCommand();
                cmd.CommandText = "UPDATE players SET password_hash = @hash WHERE LOWER(username) = LOWER(@username);";
                cmd.Parameters.AddWithValue("@hash", newHash);
                cmd.Parameters.AddWithValue("@username", username);
                cmd.ExecuteNonQuery();

                DebugLogger.Instance.LogInfo("SQL", $"Admin reset password for: '{username}'");
                return (true, $"Password reset for '{username}'.");
            }
            catch (Exception ex)
            {
                DebugLogger.Instance.LogError("SQL", $"Failed to reset password: {ex.Message}");
                return (false, "Password reset failed.");
            }
        }

        // =====================================================================
        // PvP Combat Log
        // =====================================================================

        /// <summary>
        /// Record a PvP combat result in the log.
        /// </summary>
        public async Task LogPvPCombat(string attacker, string defender,
            int attackerLevel, int defenderLevel, string winner,
            long goldStolen, long xpGained, long attackerHpRemaining, int rounds)
        {
            try
            {
                using var connection = OpenConnection();
                using var cmd = connection.CreateCommand();
                cmd.CommandText = @"
                    INSERT INTO pvp_log (attacker, defender, attacker_level, defender_level,
                                         winner, gold_stolen, xp_gained, attacker_hp_remaining, rounds)
                    VALUES (@attacker, @defender, @attackerLevel, @defenderLevel,
                            @winner, @goldStolen, @xpGained, @hpRemaining, @rounds);
                ";
                cmd.Parameters.AddWithValue("@attacker", attacker.ToLower());
                cmd.Parameters.AddWithValue("@defender", defender.ToLower());
                cmd.Parameters.AddWithValue("@attackerLevel", attackerLevel);
                cmd.Parameters.AddWithValue("@defenderLevel", defenderLevel);
                cmd.Parameters.AddWithValue("@winner", winner.ToLower());
                cmd.Parameters.AddWithValue("@goldStolen", goldStolen);
                cmd.Parameters.AddWithValue("@xpGained", xpGained);
                cmd.Parameters.AddWithValue("@hpRemaining", attackerHpRemaining);
                cmd.Parameters.AddWithValue("@rounds", rounds);
                await Task.Run(() => cmd.ExecuteNonQuery());
            }
            catch (Exception ex)
            {
                DebugLogger.Instance.LogError("SQL", $"Failed to log PvP combat: {ex.Message}");
            }
        }

        /// <summary>
        /// Get the number of PvP attacks a player has made today.
        /// </summary>
        public int GetPvPAttacksToday(string attacker)
        {
            try
            {
                using var connection = OpenConnection();
                using var cmd = connection.CreateCommand();
                cmd.CommandText = @"
                    SELECT COUNT(*) FROM pvp_log
                    WHERE attacker = LOWER(@attacker)
                    AND created_at >= date('now');
                ";
                cmd.Parameters.AddWithValue("@attacker", attacker);
                return Convert.ToInt32(cmd.ExecuteScalar() ?? 0);
            }
            catch (Exception ex)
            {
                DebugLogger.Instance.LogError("SQL", $"Failed to get PvP attacks today: {ex.Message}");
                return 0;
            }
        }

        /// <summary>
        /// Check if a player has already attacked a specific defender today.
        /// </summary>
        public bool HasAttackedPlayerToday(string attacker, string defender)
        {
            try
            {
                using var connection = OpenConnection();
                using var cmd = connection.CreateCommand();
                cmd.CommandText = @"
                    SELECT COUNT(*) FROM pvp_log
                    WHERE attacker = LOWER(@attacker)
                    AND defender = LOWER(@defender)
                    AND created_at >= date('now');
                ";
                cmd.Parameters.AddWithValue("@attacker", attacker);
                cmd.Parameters.AddWithValue("@defender", defender);
                return Convert.ToInt32(cmd.ExecuteScalar() ?? 0) > 0;
            }
            catch (Exception ex)
            {
                DebugLogger.Instance.LogError("SQL", $"Failed to check PvP attack: {ex.Message}");
                return true; // Fail safe: prevent attack
            }
        }

        /// <summary>
        /// v0.60.0 alpha balance review: defender shield. Returns true if the
        /// defender has lost any PvP since their most recent login AND that
        /// loss happened today. Two-layer semantic: shield drops on next login
        /// (player has agency to clear it) OR at daily reset (catches the
        /// case where the defender is offline for a long time). Stops the
        /// spam-attack-the-same-victim pattern that turned vazren (19 attacks,
        /// 3.16M gold lost) and shornthesheep (13 attacks, 2.76M gold lost)
        /// into farming targets.
        /// </summary>
        public bool IsDefenderShielded(string defender)
        {
            try
            {
                using var connection = OpenConnection();
                using var cmd = connection.CreateCommand();
                cmd.CommandText = @"
                    SELECT 1 FROM pvp_log
                    WHERE LOWER(defender) = LOWER(@defender)
                      AND created_at >= date('now')
                      AND created_at > COALESCE(
                          (SELECT last_login FROM players WHERE LOWER(username) = LOWER(@defender)),
                          '1900-01-01'
                      )
                    LIMIT 1;
                ";
                cmd.Parameters.AddWithValue("@defender", defender);
                var result = cmd.ExecuteScalar();
                return result != null && result != DBNull.Value;
            }
            catch (Exception ex)
            {
                DebugLogger.Instance.LogError("SQL", $"Failed to check defender shield: {ex.Message}");
                return false;
            }
        }

        /// <summary>
        /// Get the PvP leaderboard -- top players ranked by win count.
        /// </summary>
        public async Task<List<PvPLeaderboardEntry>> GetPvPLeaderboard(int limit = 20)
        {
            var entries = new List<PvPLeaderboardEntry>();
            try
            {
                using var connection = OpenConnection();
                using var cmd = connection.CreateCommand();
                cmd.CommandText = @"
                    SELECT
                        p1.winner,
                        COUNT(*) as wins,
                        (SELECT COUNT(*) FROM pvp_log p2
                         WHERE (p2.attacker = p1.winner AND p2.winner != p1.winner)
                            OR (p2.defender = p1.winner AND p2.winner != p1.winner)) as losses,
                        COALESCE(SUM(p1.gold_stolen), 0) as total_gold_stolen,
                        (SELECT p.display_name FROM players p
                         WHERE LOWER(p.username) = p1.winner) as display_name,
                        (SELECT json_extract(p.player_data, '$.player.level')
                         FROM players p WHERE LOWER(p.username) = p1.winner) as level,
                        (SELECT json_extract(p.player_data, '$.player.class')
                         FROM players p WHERE LOWER(p.username) = p1.winner) as class_id
                    FROM pvp_log p1
                    GROUP BY p1.winner
                    ORDER BY wins DESC, total_gold_stolen DESC
                    LIMIT @limit;
                ";
                cmd.Parameters.AddWithValue("@limit", limit);

                using var reader = await Task.Run(() => cmd.ExecuteReader());
                int rank = 0;
                while (reader.Read())
                {
                    rank++;
                    entries.Add(new PvPLeaderboardEntry
                    {
                        Rank = rank,
                        Username = reader.GetString(0),
                        Wins = reader.GetInt32(1),
                        Losses = reader.GetInt32(2),
                        TotalGoldStolen = reader.GetInt64(3),
                        DisplayName = reader.IsDBNull(4) ? reader.GetString(0) : reader.GetString(4),
                        Level = reader.IsDBNull(5) ? 1 : Convert.ToInt32(reader.GetValue(5)),
                        ClassId = reader.IsDBNull(6) ? 0 : Convert.ToInt32(reader.GetValue(6))
                    });
                }
            }
            catch (Exception ex)
            {
                DebugLogger.Instance.LogError("SQL", $"Failed to get PvP leaderboard: {ex.Message}");
            }
            return entries;
        }

        /// <summary>
        /// Get recent PvP fights for the arena history display.
        /// </summary>
        public async Task<List<PvPLogEntry>> GetRecentPvPFights(int limit = 10)
        {
            var entries = new List<PvPLogEntry>();
            try
            {
                using var connection = OpenConnection();
                using var cmd = connection.CreateCommand();
                cmd.CommandText = @"
                    SELECT
                        (SELECT p.display_name FROM players p
                         WHERE LOWER(p.username) = pvp.attacker) as attacker_name,
                        (SELECT p.display_name FROM players p
                         WHERE LOWER(p.username) = pvp.defender) as defender_name,
                        pvp.winner,
                        pvp.gold_stolen,
                        pvp.attacker_level,
                        pvp.defender_level,
                        pvp.created_at
                    FROM pvp_log pvp
                    ORDER BY pvp.created_at DESC
                    LIMIT @limit;
                ";
                cmd.Parameters.AddWithValue("@limit", limit);

                using var reader = await Task.Run(() => cmd.ExecuteReader());
                while (reader.Read())
                {
                    entries.Add(new PvPLogEntry
                    {
                        AttackerName = reader.IsDBNull(0) ? "Unknown" : reader.GetString(0),
                        DefenderName = reader.IsDBNull(1) ? "Unknown" : reader.GetString(1),
                        WinnerUsername = reader.GetString(2),
                        GoldStolen = reader.GetInt64(3),
                        AttackerLevel = reader.GetInt32(4),
                        DefenderLevel = reader.GetInt32(5),
                        CreatedAt = DateTime.Parse(reader.GetString(6))
                    });
                }
            }
            catch (Exception ex)
            {
                DebugLogger.Instance.LogError("SQL", $"Failed to get recent PvP fights: {ex.Message}");
            }
            return entries;
        }

        /// <summary>
        /// Deduct gold from a player's save data atomically.
        /// Uses json_set to update without loading the full save blob.
        /// </summary>
        public async Task DeductGoldFromPlayer(string username, long goldAmount)
        {
            try
            {
                using var connection = OpenConnection();
                using var cmd = connection.CreateCommand();
                cmd.CommandText = @"
                    UPDATE players
                    SET player_data = json_set(
                        player_data,
                        '$.player.gold',
                        MAX(0, CAST(json_extract(player_data, '$.player.gold') AS INTEGER) - @goldAmount)
                    )
                    WHERE (LOWER(username) = LOWER(@username) OR LOWER(display_name) = LOWER(@username))
                    AND player_data != '{}' AND LENGTH(player_data) > 2;
                ";
                cmd.Parameters.AddWithValue("@username", username);
                cmd.Parameters.AddWithValue("@goldAmount", goldAmount);
                await Task.Run(() => cmd.ExecuteNonQuery());
            }
            catch (Exception ex)
            {
                DebugLogger.Instance.LogError("SQL", $"Failed to deduct gold from {username}: {ex.Message}");
            }
        }

        /// <summary>
        /// Atomically deduct a percentage of a player's gold.
        /// </summary>
        public async Task DeductGoldByPercentage(string username, int percent)
        {
            try
            {
                using var connection = OpenConnection();
                using var cmd = connection.CreateCommand();
                cmd.CommandText = @"
                    UPDATE players
                    SET player_data = json_set(
                        player_data,
                        '$.player.gold',
                        MAX(0, CAST(json_extract(player_data, '$.player.gold') AS INTEGER) -
                            CAST(json_extract(player_data, '$.player.gold') AS INTEGER) * @percent / 100)
                    )
                    WHERE (LOWER(username) = LOWER(@username) OR LOWER(display_name) = LOWER(@username))
                    AND player_data != '{}' AND LENGTH(player_data) > 2;
                ";
                cmd.Parameters.AddWithValue("@username", username);
                cmd.Parameters.AddWithValue("@percent", percent);
                await Task.Run(() => cmd.ExecuteNonQuery());
            }
            catch (Exception ex)
            {
                DebugLogger.Instance.LogError("SQL", $"Failed to deduct {percent}% gold from {username}: {ex.Message}");
            }
        }

        /// <summary>
        /// Atomically add gold to a player's save data.
        /// Used for PvP rewards when a defender wins.
        /// </summary>
        public async Task AddGoldToPlayer(string username, long goldAmount)
        {
            try
            {
                using var connection = OpenConnection();
                using var cmd = connection.CreateCommand();
                cmd.CommandText = @"
                    UPDATE players
                    SET player_data = json_set(
                        player_data,
                        '$.player.gold',
                        CAST(json_extract(player_data, '$.player.gold') AS INTEGER) + @goldAmount
                    )
                    WHERE (LOWER(username) = LOWER(@username) OR LOWER(display_name) = LOWER(@username))
                    AND player_data != '{}' AND LENGTH(player_data) > 2;
                ";
                cmd.Parameters.AddWithValue("@username", username);
                cmd.Parameters.AddWithValue("@goldAmount", goldAmount);
                await Task.Run(() => cmd.ExecuteNonQuery());
            }
            catch (Exception ex)
            {
                DebugLogger.Instance.LogError("SQL", $"Failed to add gold to {username}: {ex.Message}");
            }
        }

        /// <summary>
        /// Appends items (as InventoryItemData JSON array) to a player's inventory in their save data.
        /// Used to return trade items to offline players on decline/expiry/cancel.
        /// </summary>
        public async Task AddItemsToPlayerSave(string username, string itemsJson)
        {
            if (string.IsNullOrEmpty(itemsJson) || itemsJson == "[]") return;
            try
            {
                using var connection = OpenConnection();

                // Read current inventory JSON
                using var readCmd = connection.CreateCommand();
                readCmd.CommandText = @"
                    SELECT json_extract(player_data, '$.player.inventory')
                    FROM players
                    WHERE (LOWER(username) = LOWER(@username) OR LOWER(display_name) = LOWER(@username))
                    AND player_data != '{}' AND LENGTH(player_data) > 2;
                ";
                readCmd.Parameters.AddWithValue("@username", username);
                var currentJson = await Task.Run(() => readCmd.ExecuteScalar() as string);

                // Merge arrays in C#
                var options = new System.Text.Json.JsonSerializerOptions { PropertyNamingPolicy = System.Text.Json.JsonNamingPolicy.CamelCase };
                var existingItems = new List<System.Text.Json.JsonElement>();
                if (!string.IsNullOrEmpty(currentJson) && currentJson != "null")
                {
                    var arr = System.Text.Json.JsonSerializer.Deserialize<List<System.Text.Json.JsonElement>>(currentJson);
                    if (arr != null) existingItems = arr;
                }
                var newItems = System.Text.Json.JsonSerializer.Deserialize<List<System.Text.Json.JsonElement>>(itemsJson);
                if (newItems != null) existingItems.AddRange(newItems);

                string mergedJson = System.Text.Json.JsonSerializer.Serialize(existingItems);

                // Write merged inventory back
                using var writeCmd = connection.CreateCommand();
                writeCmd.CommandText = @"
                    UPDATE players
                    SET player_data = json_set(player_data, '$.player.inventory', json(@mergedInventory))
                    WHERE (LOWER(username) = LOWER(@username) OR LOWER(display_name) = LOWER(@username))
                    AND player_data != '{}' AND LENGTH(player_data) > 2;
                ";
                writeCmd.Parameters.AddWithValue("@username", username);
                writeCmd.Parameters.AddWithValue("@mergedInventory", mergedJson);
                await Task.Run(() => writeCmd.ExecuteNonQuery());
            }
            catch (Exception ex)
            {
                DebugLogger.Instance.LogError("SQL", $"Failed to add items to {username}'s save: {ex.Message}");
            }
        }

        public async Task AddXPToPlayer(string username, long xpAmount)
        {
            try
            {
                using var connection = OpenConnection();
                using var cmd = connection.CreateCommand();
                cmd.CommandText = @"
                    UPDATE players
                    SET player_data = json_set(
                        player_data,
                        '$.player.experience',
                        CAST(json_extract(player_data, '$.player.experience') AS INTEGER) + @xp
                    )
                    WHERE LOWER(username) = LOWER(@username)
                    AND player_data != '{}' AND LENGTH(player_data) > 2;
                ";
                cmd.Parameters.AddWithValue("@username", username);
                cmd.Parameters.AddWithValue("@xp", xpAmount);
                await Task.Run(() => cmd.ExecuteNonQuery());
            }
            catch (Exception ex)
            {
                DebugLogger.Instance.LogError("SQL", $"Failed to add XP to {username}: {ex.Message}");
            }
        }

        /// <summary>
        /// Atomically set DaysInPrison on a player's save data.
        /// Used when the king imprisons another player via Royal Orders.
        /// </summary>
        public async Task ImprisonPlayer(string username, int days)
        {
            try
            {
                using var connection = OpenConnection();
                using var cmd = connection.CreateCommand();
                cmd.CommandText = @"
                    UPDATE players
                    SET player_data = json_set(
                        player_data,
                        '$.player.daysInPrison',
                        @days
                    )
                    WHERE (LOWER(username) = LOWER(@username) OR LOWER(display_name) = LOWER(@username))
                    AND player_data != '{}' AND LENGTH(player_data) > 2;
                ";
                cmd.Parameters.AddWithValue("@username", username);
                cmd.Parameters.AddWithValue("@days", days);
                await Task.Run(() => cmd.ExecuteNonQuery());
            }
            catch (Exception ex)
            {
                DebugLogger.Instance.LogError("SQL", $"Failed to imprison player {username}: {ex.Message}");
            }
        }

        // =====================================================================
        // "While You Were Gone" Queries
        // =====================================================================

        /// <summary>
        /// Get the player's last logout timestamp for "While you were gone" summary.
        /// </summary>
        public async Task<DateTime?> GetLastLogoutTime(string username)
        {
            try
            {
                using var connection = OpenConnection();
                using var cmd = connection.CreateCommand();
                cmd.CommandText = "SELECT last_logout FROM players WHERE LOWER(username) = LOWER(@username);";
                cmd.Parameters.AddWithValue("@username", username);

                var result = await Task.Run(() => cmd.ExecuteScalar());
                if (result != null && result != DBNull.Value)
                {
                    return DateTime.Parse(result.ToString()!);
                }
            }
            catch (Exception ex)
            {
                DebugLogger.Instance.LogError("SQL", $"Failed to get last logout time for {username}: {ex.Message}");
            }
            return null;
        }

        /// <summary>
        /// Get news entries since a given timestamp for "While you were gone" summary.
        /// </summary>
        public async Task<List<NewsEntry>> GetNewsSince(DateTime sinceTime, int limit = 15)
        {
            var entries = new List<NewsEntry>();
            try
            {
                using var connection = OpenConnection();
                using var cmd = connection.CreateCommand();
                cmd.CommandText = @"
                    SELECT id, message, category, player_name, created_at
                    FROM news
                    WHERE created_at > @since
                    ORDER BY created_at DESC
                    LIMIT @limit;
                ";
                cmd.Parameters.AddWithValue("@since", sinceTime.ToString("yyyy-MM-dd HH:mm:ss"));
                cmd.Parameters.AddWithValue("@limit", limit);

                using var reader = await Task.Run(() => cmd.ExecuteReader());
                while (reader.Read())
                {
                    entries.Add(new NewsEntry
                    {
                        Id = reader.GetInt32(0),
                        Message = reader.GetString(1),
                        Category = reader.IsDBNull(2) ? "" : reader.GetString(2),
                        PlayerName = reader.IsDBNull(3) ? "" : reader.GetString(3),
                        CreatedAt = DateTime.Parse(reader.GetString(4))
                    });
                }
            }
            catch (Exception ex)
            {
                DebugLogger.Instance.LogError("SQL", $"Failed to get news since {sinceTime}: {ex.Message}");
            }
            return entries;
        }

        /// <summary>
        /// Get PvP attacks where the player was the defender since a given timestamp.
        /// </summary>
        public async Task<List<PvPLogEntry>> GetPvPAttacksAgainst(string username, DateTime sinceTime)
        {
            var entries = new List<PvPLogEntry>();
            try
            {
                using var connection = OpenConnection();
                using var cmd = connection.CreateCommand();
                cmd.CommandText = @"
                    SELECT
                        (SELECT p.display_name FROM players p
                         WHERE LOWER(p.username) = pvp.attacker) as attacker_name,
                        (SELECT p.display_name FROM players p
                         WHERE LOWER(p.username) = pvp.defender) as defender_name,
                        pvp.winner,
                        pvp.gold_stolen,
                        pvp.attacker_level,
                        pvp.defender_level,
                        pvp.created_at
                    FROM pvp_log pvp
                    WHERE LOWER(pvp.defender) = LOWER(@username)
                      AND pvp.created_at > @since
                    ORDER BY pvp.created_at DESC;
                ";
                cmd.Parameters.AddWithValue("@username", username);
                cmd.Parameters.AddWithValue("@since", sinceTime.ToString("yyyy-MM-dd HH:mm:ss"));

                using var reader = await Task.Run(() => cmd.ExecuteReader());
                while (reader.Read())
                {
                    entries.Add(new PvPLogEntry
                    {
                        AttackerName = reader.IsDBNull(0) ? "Unknown" : reader.GetString(0),
                        DefenderName = reader.IsDBNull(1) ? "Unknown" : reader.GetString(1),
                        WinnerUsername = reader.GetString(2),
                        GoldStolen = reader.GetInt64(3),
                        AttackerLevel = reader.GetInt32(4),
                        DefenderLevel = reader.GetInt32(5),
                        CreatedAt = DateTime.Parse(reader.GetString(6))
                    });
                }
            }
            catch (Exception ex)
            {
                DebugLogger.Instance.LogError("SQL", $"Failed to get PvP attacks against {username}: {ex.Message}");
            }
            return entries;
        }

    // ========== Player Teams ==========

    public async Task<bool> CreatePlayerTeam(string teamName, string passwordHash, string createdBy)
    {
        try
        {
            using var connection = OpenConnection();
            using var cmd = connection.CreateCommand();
            cmd.CommandText = "INSERT INTO player_teams (team_name, password_hash, created_by) VALUES (@name, @hash, @creator);";
            cmd.Parameters.AddWithValue("@name", teamName);
            cmd.Parameters.AddWithValue("@hash", passwordHash);
            cmd.Parameters.AddWithValue("@creator", createdBy.ToLower());
            await Task.Run(() => cmd.ExecuteNonQuery());
            return true;
        }
        catch (Exception ex)
        {
            DebugLogger.Instance.LogError("SQL", $"Failed to create player team '{teamName}': {ex.Message}");
            return false;
        }
    }

    /// <summary>
    /// v0.61.5: Look up the player who created a team (the team leader). Returns
    /// the leader's username (lowercase) or null if the team doesn't exist.
    /// Used by the NPC-old-age-death inheritance flow to find who should receive
    /// the deceased teammate's belongings.
    /// </summary>
    public async Task<string?> GetTeamLeaderUsername(string teamName)
    {
        try
        {
            using var connection = OpenConnection();
            using var cmd = connection.CreateCommand();
            cmd.CommandText = "SELECT created_by FROM player_teams WHERE team_name = @name LIMIT 1;";
            cmd.Parameters.AddWithValue("@name", teamName);
            var result = await Task.Run(() => cmd.ExecuteScalar());
            return result?.ToString();
        }
        catch (Exception ex)
        {
            DebugLogger.Instance.LogError("SQL", $"Failed to look up team leader for '{teamName}': {ex.Message}");
            return null;
        }
    }

    /// <summary>
    /// v0.61.5: Queue an item for delivery to a player on their next login.
    /// Used when a team NPC dies of old age — their belongings go to the team
    /// leader. Each call queues one item (or a gold amount when itemJson is null).
    /// </summary>
    public bool QueueInheritance(string playerUsername, string sourceNpcName, string? itemJson, long goldAmount = 0)
    {
        try
        {
            using var connection = OpenConnection();
            using var cmd = connection.CreateCommand();
            cmd.CommandText = @"
                INSERT INTO pending_inheritance (player_username, source_npc_name, item_json, gold_amount)
                VALUES (@user, @npc, @json, @gold);";
            cmd.Parameters.AddWithValue("@user", playerUsername.ToLower());
            cmd.Parameters.AddWithValue("@npc", sourceNpcName);
            cmd.Parameters.AddWithValue("@json", itemJson ?? "");
            cmd.Parameters.AddWithValue("@gold", goldAmount);
            cmd.ExecuteNonQuery();
            return true;
        }
        catch (Exception ex)
        {
            DebugLogger.Instance.LogError("SQL", $"Failed to queue inheritance for '{playerUsername}' from '{sourceNpcName}': {ex.Message}");
            return false;
        }
    }

    /// <summary>
    /// v0.61.5: Fetch and consume all pending inheritance rows for a player.
    /// Returns one entry per row (id, sourceNpc, itemJson, gold) so the caller
    /// can present a summary and append items to the player's inventory.
    /// The rows are NOT deleted by this call — the caller invokes
    /// ClearInheritance(ids) once delivery to the in-memory Character succeeds,
    /// so partial-failure scenarios (process crash mid-delivery) don't lose items.
    /// </summary>
    public List<(long Id, string SourceNpc, string ItemJson, long Gold)> GetPendingInheritance(string playerUsername)
    {
        var results = new List<(long, string, string, long)>();
        try
        {
            using var connection = OpenConnection();
            using var cmd = connection.CreateCommand();
            cmd.CommandText = @"
                SELECT id, source_npc_name, item_json, gold_amount
                FROM pending_inheritance
                WHERE player_username = @user
                ORDER BY created_at ASC;";
            cmd.Parameters.AddWithValue("@user", playerUsername.ToLower());
            using var reader = cmd.ExecuteReader();
            while (reader.Read())
            {
                results.Add((
                    reader.GetInt64(0),
                    reader.IsDBNull(1) ? "" : reader.GetString(1),
                    reader.IsDBNull(2) ? "" : reader.GetString(2),
                    reader.IsDBNull(3) ? 0L : reader.GetInt64(3)
                ));
            }
        }
        catch (Exception ex)
        {
            DebugLogger.Instance.LogError("SQL", $"Failed to fetch pending inheritance for '{playerUsername}': {ex.Message}");
        }
        return results;
    }

    /// <summary>
    /// v0.61.5: Delete pending_inheritance rows by ID after their items have
    /// been successfully delivered into the in-memory Character. Atomic at the
    /// row level — if process crashes mid-delete, the remaining rows fire again
    /// next login (idempotent because each row is one specific item).
    /// </summary>
    public void ClearInheritance(IEnumerable<long> ids)
    {
        try
        {
            using var connection = OpenConnection();
            foreach (var id in ids)
            {
                using var cmd = connection.CreateCommand();
                cmd.CommandText = "DELETE FROM pending_inheritance WHERE id = @id;";
                cmd.Parameters.AddWithValue("@id", id);
                cmd.ExecuteNonQuery();
            }
        }
        catch (Exception ex)
        {
            DebugLogger.Instance.LogError("SQL", $"Failed to clear inheritance: {ex.Message}");
        }
    }

    /// <summary>
    /// v0.65.0: queue a player-to-player bank wire. The amount is ALREADY net of the
    /// bank fee (the sender's bank was debited the gross; the fee is a gold sink that is
    /// never queued). Delivered to the recipient's bank on their next login. Username is
    /// stored lowercase so it matches the lowercased lookup in GetPendingGoldTransfers
    /// regardless of session casing.
    /// </summary>
    public bool QueueGoldTransfer(string recipientUsername, string senderDisplay, long amount, string note = "")
    {
        try
        {
            using var connection = OpenConnection();
            using var cmd = connection.CreateCommand();
            cmd.CommandText = @"
                INSERT INTO pending_gold_transfers (recipient_username, sender_display, amount, note)
                VALUES (@user, @sender, @amount, @note);";
            cmd.Parameters.AddWithValue("@user", recipientUsername.ToLower());
            cmd.Parameters.AddWithValue("@sender", senderDisplay ?? "");
            cmd.Parameters.AddWithValue("@amount", amount);
            cmd.Parameters.AddWithValue("@note", note ?? "");
            cmd.ExecuteNonQuery();
            return true;
        }
        catch (Exception ex)
        {
            DebugLogger.Instance.LogError("SQL", $"Failed to queue gold transfer for '{recipientUsername}': {ex.Message}");
            return false;
        }
    }

    /// <summary>
    /// v0.65.0: fetch all pending wire transfers for a recipient. Rows are NOT deleted
    /// here — the caller credits the in-memory Character then calls ClearGoldTransfers(ids),
    /// matching the inheritance pattern (a crash before clear re-delivers next login; a
    /// crash after clear but before the credit is persisted loses the gold to the sink
    /// rather than duplicating it).
    /// </summary>
    public List<(long Id, string Sender, long Amount, string Note)> GetPendingGoldTransfers(string recipientUsername)
    {
        var results = new List<(long, string, long, string)>();
        try
        {
            using var connection = OpenConnection();
            using var cmd = connection.CreateCommand();
            cmd.CommandText = @"
                SELECT id, sender_display, amount, note
                FROM pending_gold_transfers
                WHERE recipient_username = @user
                ORDER BY created_at ASC;";
            cmd.Parameters.AddWithValue("@user", recipientUsername.ToLower());
            using var reader = cmd.ExecuteReader();
            while (reader.Read())
            {
                results.Add((
                    reader.GetInt64(0),
                    reader.IsDBNull(1) ? "" : reader.GetString(1),
                    reader.IsDBNull(2) ? 0L : reader.GetInt64(2),
                    reader.IsDBNull(3) ? "" : reader.GetString(3)
                ));
            }
        }
        catch (Exception ex)
        {
            DebugLogger.Instance.LogError("SQL", $"Failed to fetch pending gold transfers for '{recipientUsername}': {ex.Message}");
        }
        return results;
    }

    /// <summary>
    /// v0.65.0: delete delivered wire-transfer rows by ID after their gold has been
    /// credited to the in-memory Character.
    /// </summary>
    public void ClearGoldTransfers(IEnumerable<long> ids)
    {
        try
        {
            using var connection = OpenConnection();
            foreach (var id in ids)
            {
                using var cmd = connection.CreateCommand();
                cmd.CommandText = "DELETE FROM pending_gold_transfers WHERE id = @id;";
                cmd.Parameters.AddWithValue("@id", id);
                cmd.ExecuteNonQuery();
            }
        }
        catch (Exception ex)
        {
            DebugLogger.Instance.LogError("SQL", $"Failed to clear gold transfers: {ex.Message}");
        }
    }

    public async Task<(bool exists, bool passwordCorrect)> VerifyPlayerTeam(string teamName, string password)
    {
        try
        {
            using var connection = OpenConnection();
            using var cmd = connection.CreateCommand();
            cmd.CommandText = "SELECT password_hash FROM player_teams WHERE team_name = @name;";
            cmd.Parameters.AddWithValue("@name", teamName);
            var result = await Task.Run(() => cmd.ExecuteScalar());
            if (result == null) return (false, false);
            var storedHash = result.ToString() ?? "";
            return (true, VerifyPassword(password, storedHash));
        }
        catch (Exception ex)
        {
            DebugLogger.Instance.LogError("SQL", $"Failed to verify player team '{teamName}': {ex.Message}");
            return (false, false);
        }
    }

    public async Task<List<PlayerTeamInfo>> GetPlayerTeams()
    {
        var teams = new List<PlayerTeamInfo>();
        try
        {
            using var connection = OpenConnection();
            using var cmd = connection.CreateCommand();
            cmd.CommandText = "SELECT team_name, created_by, member_count, controls_turf, created_at FROM player_teams ORDER BY member_count DESC;";
            using var reader = await Task.Run(() => cmd.ExecuteReader());
            while (reader.Read())
            {
                teams.Add(new PlayerTeamInfo
                {
                    TeamName = reader.GetString(0),
                    CreatedBy = reader.GetString(1),
                    MemberCount = reader.GetInt32(2),
                    ControlsTurf = reader.GetInt32(3) != 0,
                    CreatedAt = DateTime.TryParse(reader.GetString(4), out var dt) ? dt : DateTime.Now
                });
            }
        }
        catch (Exception ex)
        {
            DebugLogger.Instance.LogError("SQL", $"Failed to get player teams: {ex.Message}");
        }
        return teams;
    }

    public async Task<List<PlayerSummary>> GetPlayerTeamMembers(string teamName, string? excludeUsername = null)
    {
        var members = new List<PlayerSummary>();
        try
        {
            using var connection = OpenConnection();
            using var cmd = connection.CreateCommand();
            cmd.CommandText = @"
                SELECT p.display_name,
                       CAST(json_extract(p.player_data, '$.player.level') AS INTEGER) as level,
                       CAST(json_extract(p.player_data, '$.player.class') AS INTEGER) as class_id,
                       CAST(json_extract(p.player_data, '$.player.experience') AS INTEGER) as xp,
                       p.last_login,
                       CASE WHEN op.username IS NOT NULL THEN 1 ELSE 0 END as is_online
                FROM players p
                LEFT JOIN online_players op ON LOWER(p.username) = LOWER(op.username)
                    AND op.last_heartbeat > datetime('now', '-120 seconds')
                WHERE json_extract(p.player_data, '$.player.team') = @teamName
                AND p.player_data != '{}' AND LENGTH(p.player_data) > 2
                AND p.is_banned = 0
                AND p.username NOT LIKE 'emergency_%'
                ORDER BY level DESC;
            ";
            cmd.Parameters.AddWithValue("@teamName", teamName);
            using var reader = await Task.Run(() => cmd.ExecuteReader());
            while (reader.Read())
            {
                var name = reader.GetString(0);
                if (excludeUsername != null && name.Equals(excludeUsername, StringComparison.OrdinalIgnoreCase))
                    continue;
                members.Add(new PlayerSummary
                {
                    DisplayName = name,
                    Level = reader.IsDBNull(1) ? 1 : reader.GetInt32(1),
                    ClassId = reader.IsDBNull(2) ? 0 : reader.GetInt32(2),
                    Experience = reader.IsDBNull(3) ? 0 : reader.GetInt64(3),
                    IsOnline = reader.GetInt32(5) != 0
                });
            }
        }
        catch (Exception ex)
        {
            DebugLogger.Instance.LogError("SQL", $"Failed to get player team members for '{teamName}': {ex.Message}");
        }
        return members;
    }

    public async Task DeletePlayerTeam(string teamName)
    {
        try
        {
            using var connection = OpenConnection();
            using var cmd = connection.CreateCommand();
            cmd.CommandText = "DELETE FROM player_teams WHERE team_name = @name;";
            cmd.Parameters.AddWithValue("@name", teamName);
            await Task.Run(() => cmd.ExecuteNonQuery());
        }
        catch (Exception ex)
        {
            DebugLogger.Instance.LogError("SQL", $"Failed to delete player team '{teamName}': {ex.Message}");
        }
    }

    public async Task UpdatePlayerTeamMemberCount(string teamName)
    {
        try
        {
            using var connection = OpenConnection();
            using var cmd = connection.CreateCommand();
            cmd.CommandText = @"
                UPDATE player_teams SET member_count = (
                    SELECT COUNT(*) FROM players
                    WHERE json_extract(player_data, '$.player.team') = @teamName
                    AND player_data != '{}' AND LENGTH(player_data) > 2
                    AND is_banned = 0 AND username NOT LIKE 'emergency_%'
                ) WHERE team_name = @teamName;
            ";
            cmd.Parameters.AddWithValue("@teamName", teamName);
            await Task.Run(() => cmd.ExecuteNonQuery());
        }
        catch (Exception ex)
        {
            DebugLogger.Instance.LogError("SQL", $"Failed to update team member count for '{teamName}': {ex.Message}");
        }
    }

    public bool IsTeamNameTaken(string teamName)
    {
        try
        {
            using var connection = OpenConnection();
            using var cmd = connection.CreateCommand();
            cmd.CommandText = "SELECT COUNT(*) FROM player_teams WHERE team_name = @name;";
            cmd.Parameters.AddWithValue("@name", teamName);
            var count = Convert.ToInt32(cmd.ExecuteScalar());
            return count > 0;
        }
        catch (Exception ex)
        {
            DebugLogger.Instance.LogError("SQL", $"Failed to check team name '{teamName}': {ex.Message}");
            return false;
        }
    }

    public static string HashTeamPassword(string password)
    {
        return HashPassword(password);
    }

    // ========== Offline Mail ==========

    public bool PlayerExists(string nameOrDisplay)
    {
        try
        {
            using var connection = OpenConnection();
            using var cmd = connection.CreateCommand();
            cmd.CommandText = "SELECT COUNT(*) FROM players WHERE (LOWER(username) = LOWER(@name) OR LOWER(display_name) = LOWER(@name)) AND is_banned = 0;";
            cmd.Parameters.AddWithValue("@name", nameOrDisplay);
            return Convert.ToInt32(cmd.ExecuteScalar()) > 0;
        }
        catch { return false; }
    }

    /// <summary>
    /// Get account-level preferences (screen reader, language) for a player.
    /// </summary>
    public (bool screenReader, string language) GetAccountPreferences(string username)
    {
        try
        {
            using var connection = OpenConnection();
            using var cmd = connection.CreateCommand();
            cmd.CommandText = "SELECT COALESCE(screen_reader, 0), COALESCE(language, 'en') FROM players WHERE LOWER(username) = LOWER(@username);";
            cmd.Parameters.AddWithValue("@username", username);
            using var reader = cmd.ExecuteReader();
            if (reader.Read())
                return (reader.GetInt32(0) != 0, reader.IsDBNull(1) ? "en" : reader.GetString(1));
        }
        catch { }
        return (false, "en");
    }

    /// <summary>
    /// Resolves a player name (username or display name) to their lowercase display name.
    /// Returns null if the player doesn't exist.
    /// </summary>
    public string? ResolvePlayerDisplayName(string nameOrDisplay)
    {
        try
        {
            using var connection = OpenConnection();
            using var cmd = connection.CreateCommand();
            cmd.CommandText = "SELECT display_name FROM players WHERE (LOWER(username) = LOWER(@name) OR LOWER(display_name) = LOWER(@name)) AND is_banned = 0 AND username NOT LIKE 'emergency_%' LIMIT 1;";
            cmd.Parameters.AddWithValue("@name", nameOrDisplay);
            var result = cmd.ExecuteScalar();
            return result?.ToString();
        }
        catch { return null; }
    }

    /// <summary>
    /// v0.64.1: resolve a name-or-display-name to the canonical USERNAME
    /// (the messages table's to_player key). Twin of ResolvePlayerDisplayName.
    /// Used by the spouse-death notification to route in-game mail to the
    /// widowed player. Returns null if no matching player.
    /// </summary>
    public string? ResolvePlayerUsername(string nameOrDisplay)
    {
        try
        {
            using var connection = OpenConnection();
            using var cmd = connection.CreateCommand();
            cmd.CommandText = "SELECT username FROM players WHERE (LOWER(username) = LOWER(@name) OR LOWER(display_name) = LOWER(@name)) AND is_banned = 0 AND username NOT LIKE 'emergency_%' LIMIT 1;";
            cmd.Parameters.AddWithValue("@name", nameOrDisplay);
            var result = cmd.ExecuteScalar();
            return result?.ToString();
        }
        catch { return null; }
    }

    public async Task<List<PlayerMessage>> GetMailInbox(string username, int limit = 20, int offset = 0)
    {
        var messages = new List<PlayerMessage>();
        try
        {
            using var connection = OpenConnection();
            using var cmd = connection.CreateCommand();
            cmd.CommandText = @"
                SELECT id, from_player, to_player, message_type, message, is_read, created_at
                FROM messages
                WHERE LOWER(to_player) = LOWER(@username)
                AND to_player != '*'
                ORDER BY created_at DESC
                LIMIT @limit OFFSET @offset;
            ";
            cmd.Parameters.AddWithValue("@username", username);
            cmd.Parameters.AddWithValue("@limit", limit);
            cmd.Parameters.AddWithValue("@offset", offset);
            using var reader = await Task.Run(() => cmd.ExecuteReader());
            while (reader.Read())
            {
                messages.Add(new PlayerMessage
                {
                    Id = reader.GetInt32(0),
                    FromPlayer = reader.GetString(1),
                    ToPlayer = reader.GetString(2),
                    MessageType = reader.GetString(3),
                    Message = reader.GetString(4),
                    IsRead = reader.GetInt32(5) != 0,
                    CreatedAt = DateTime.TryParse(reader.GetString(6), out var dt) ? dt : DateTime.Now
                });
            }
        }
        catch (Exception ex)
        {
            DebugLogger.Instance.LogError("SQL", $"Failed to get mail inbox for {username}: {ex.Message}");
        }
        return messages;
    }

    public int GetUnreadMailCount(string username)
    {
        try
        {
            using var connection = OpenConnection();
            using var cmd = connection.CreateCommand();
            cmd.CommandText = @"
                SELECT COUNT(*) FROM messages
                WHERE LOWER(to_player) = LOWER(@username)
                AND to_player != '*' AND is_read = 0;
            ";
            cmd.Parameters.AddWithValue("@username", username);
            return Convert.ToInt32(cmd.ExecuteScalar());
        }
        catch { return 0; }
    }

    public async Task DeleteMessage(long messageId, string username)
    {
        try
        {
            using var connection = OpenConnection();
            using var cmd = connection.CreateCommand();
            cmd.CommandText = "DELETE FROM messages WHERE id = @id AND LOWER(to_player) = LOWER(@username);";
            cmd.Parameters.AddWithValue("@id", messageId);
            cmd.Parameters.AddWithValue("@username", username);
            await Task.Run(() => cmd.ExecuteNonQuery());
        }
        catch (Exception ex)
        {
            DebugLogger.Instance.LogError("SQL", $"Failed to delete message {messageId}: {ex.Message}");
        }
    }

    public int GetMailsSentToday(string username)
    {
        try
        {
            using var connection = OpenConnection();
            using var cmd = connection.CreateCommand();
            cmd.CommandText = @"
                SELECT COUNT(*) FROM messages
                WHERE LOWER(from_player) = LOWER(@username)
                AND message_type = 'mail'
                AND created_at >= date('now');
            ";
            cmd.Parameters.AddWithValue("@username", username);
            return Convert.ToInt32(cmd.ExecuteScalar());
        }
        catch { return 0; }
    }

    // ========== Player Trading ==========

    public async Task<long> CreateTradeOffer(string fromPlayer, string toPlayer, string itemsJson, long gold, string message)
    {
        try
        {
            using var connection = OpenConnection();
            using var cmd = connection.CreateCommand();
            cmd.CommandText = @"
                INSERT INTO trade_offers (from_player, to_player, items_json, gold, message)
                VALUES (@from, @to, @items, @gold, @msg);
                SELECT last_insert_rowid();
            ";
            cmd.Parameters.AddWithValue("@from", fromPlayer.ToLower());
            cmd.Parameters.AddWithValue("@to", toPlayer.ToLower());
            cmd.Parameters.AddWithValue("@items", itemsJson);
            cmd.Parameters.AddWithValue("@gold", gold);
            cmd.Parameters.AddWithValue("@msg", message);
            var result = await Task.Run(() => cmd.ExecuteScalar());
            return Convert.ToInt64(result);
        }
        catch (Exception ex)
        {
            DebugLogger.Instance.LogError("SQL", $"Failed to create trade offer: {ex.Message}");
            return -1;
        }
    }

    public async Task<List<TradeOffer>> GetPendingTradeOffers(string username)
    {
        var offers = new List<TradeOffer>();
        try
        {
            using var connection = OpenConnection();
            using var cmd = connection.CreateCommand();
            cmd.CommandText = @"
                SELECT t.id, t.from_player, t.to_player, t.items_json, t.gold, t.status, t.message, t.created_at,
                       COALESCE(p.display_name, t.from_player) as from_display
                FROM trade_offers t
                LEFT JOIN players p ON LOWER(p.username) = t.from_player
                WHERE t.to_player = LOWER(@username) AND t.status = 'pending'
                ORDER BY t.created_at DESC;
            ";
            cmd.Parameters.AddWithValue("@username", username);
            using var reader = await Task.Run(() => cmd.ExecuteReader());
            while (reader.Read())
            {
                offers.Add(new TradeOffer
                {
                    Id = reader.GetInt64(0),
                    FromPlayer = reader.GetString(1),
                    ToPlayer = reader.GetString(2),
                    ItemsJson = reader.GetString(3),
                    Gold = reader.GetInt64(4),
                    Status = reader.GetString(5),
                    Message = reader.GetString(6),
                    CreatedAt = DateTime.TryParse(reader.GetString(7), out var dt) ? dt : DateTime.Now,
                    FromDisplayName = reader.GetString(8)
                });
            }
        }
        catch (Exception ex)
        {
            DebugLogger.Instance.LogError("SQL", $"Failed to get pending trade offers for {username}: {ex.Message}");
        }
        return offers;
    }

    public async Task<List<TradeOffer>> GetSentTradeOffers(string username)
    {
        var offers = new List<TradeOffer>();
        try
        {
            using var connection = OpenConnection();
            using var cmd = connection.CreateCommand();
            cmd.CommandText = @"
                SELECT t.id, t.from_player, t.to_player, t.items_json, t.gold, t.status, t.message, t.created_at,
                       COALESCE(p.display_name, t.to_player) as to_display
                FROM trade_offers t
                LEFT JOIN players p ON LOWER(p.username) = t.to_player
                WHERE t.from_player = LOWER(@username) AND t.status = 'pending'
                ORDER BY t.created_at DESC;
            ";
            cmd.Parameters.AddWithValue("@username", username);
            using var reader = await Task.Run(() => cmd.ExecuteReader());
            while (reader.Read())
            {
                offers.Add(new TradeOffer
                {
                    Id = reader.GetInt64(0),
                    FromPlayer = reader.GetString(1),
                    ToPlayer = reader.GetString(2),
                    ItemsJson = reader.GetString(3),
                    Gold = reader.GetInt64(4),
                    Status = reader.GetString(5),
                    Message = reader.GetString(6),
                    CreatedAt = DateTime.TryParse(reader.GetString(7), out var dt) ? dt : DateTime.Now,
                    ToDisplayName = reader.GetString(8)
                });
            }
        }
        catch (Exception ex)
        {
            DebugLogger.Instance.LogError("SQL", $"Failed to get sent trade offers for {username}: {ex.Message}");
        }
        return offers;
    }

    public async Task<TradeOffer?> GetTradeOffer(long offerId)
    {
        try
        {
            using var connection = OpenConnection();
            using var cmd = connection.CreateCommand();
            cmd.CommandText = "SELECT id, from_player, to_player, items_json, gold, status, message, created_at FROM trade_offers WHERE id = @id;";
            cmd.Parameters.AddWithValue("@id", offerId);
            using var reader = await Task.Run(() => cmd.ExecuteReader());
            if (reader.Read())
            {
                return new TradeOffer
                {
                    Id = reader.GetInt64(0),
                    FromPlayer = reader.GetString(1),
                    ToPlayer = reader.GetString(2),
                    ItemsJson = reader.GetString(3),
                    Gold = reader.GetInt64(4),
                    Status = reader.GetString(5),
                    Message = reader.GetString(6),
                    CreatedAt = DateTime.TryParse(reader.GetString(7), out var dt) ? dt : DateTime.Now
                };
            }
        }
        catch (Exception ex)
        {
            DebugLogger.Instance.LogError("SQL", $"Failed to get trade offer {offerId}: {ex.Message}");
        }
        return null;
    }

    /// <summary>
    /// Atomically resolve a pending trade offer to the given terminal status.
    /// Returns true if THIS call won the race (offer was 'pending' and is now @status).
    /// Returns false if the offer was already resolved by someone else (concurrent
    /// accept/cancel/decline/expire) — caller MUST then skip its gold/item movement
    /// to avoid duplication.
    ///
    /// Player report (gold-dupe exploit): "if one player sends a trade with money,
    /// then cancels the trade at the same time the other player accepts the trade,
    /// the money is returned and the receiving player receives the money duplicating
    /// it." The previous implementation blindly wrote the new status without
    /// checking the current state, so two concurrent calls both succeeded and both
    /// proceeded to move gold. The `WHERE status = 'pending'` clause now ensures
    /// at most one caller wins; the loser's UPDATE affects 0 rows and we return
    /// false. Same protection covers the expiry-vs-accept race.
    /// </summary>
    public async Task<bool> UpdateTradeOfferStatus(long offerId, string status)
    {
        try
        {
            using var connection = OpenConnection();
            using var cmd = connection.CreateCommand();
            cmd.CommandText = "UPDATE trade_offers SET status = @status, resolved_at = datetime('now') WHERE id = @id AND status = 'pending';";
            cmd.Parameters.AddWithValue("@id", offerId);
            cmd.Parameters.AddWithValue("@status", status);
            int rowsAffected = await Task.Run(() => cmd.ExecuteNonQuery());
            return rowsAffected == 1;
        }
        catch (Exception ex)
        {
            DebugLogger.Instance.LogError("SQL", $"Failed to update trade offer {offerId}: {ex.Message}");
            return false;
        }
    }

    public int GetPendingTradeOfferCount(string username)
    {
        try
        {
            using var connection = OpenConnection();
            using var cmd = connection.CreateCommand();
            cmd.CommandText = "SELECT COUNT(*) FROM trade_offers WHERE to_player = LOWER(@username) AND status = 'pending';";
            cmd.Parameters.AddWithValue("@username", username);
            return Convert.ToInt32(cmd.ExecuteScalar());
        }
        catch { return 0; }
    }

    public int GetSentTradeOfferCount(string username)
    {
        try
        {
            using var connection = OpenConnection();
            using var cmd = connection.CreateCommand();
            cmd.CommandText = "SELECT COUNT(*) FROM trade_offers WHERE from_player = LOWER(@username) AND status = 'pending';";
            cmd.Parameters.AddWithValue("@username", username);
            return Convert.ToInt32(cmd.ExecuteScalar());
        }
        catch { return 0; }
    }

    public async Task ExpireOldTradeOffers()
    {
        try
        {
            using var connection = OpenConnection();
            // Get expired offers to return gold and items
            using var selectCmd = connection.CreateCommand();
            selectCmd.CommandText = @"
                SELECT id, from_player, gold, items_json FROM trade_offers
                WHERE status = 'pending' AND created_at < datetime('now', '-7 days');
            ";
            var expiredOffers = new List<(long id, string fromPlayer, long gold, string itemsJson)>();
            using (var reader = await Task.Run(() => selectCmd.ExecuteReader()))
            {
                while (reader.Read())
                {
                    expiredOffers.Add((reader.GetInt64(0), reader.GetString(1), reader.GetInt64(2),
                        reader.IsDBNull(3) ? "[]" : reader.GetString(3)));
                }
            }

            // Mark expired and return gold + items. Use the atomic compare-and-set
            // form so we don't refund gold on an offer that a player just accepted /
            // cancelled in the same tick (gold-dupe vector — see UpdateTradeOfferStatus
            // doc comment).
            foreach (var (id, fromPlayer, gold, itemsJson) in expiredOffers)
            {
                bool resolved = await UpdateTradeOfferStatus(id, "expired");
                if (!resolved) continue; // Someone else resolved it first; their handler did the gold/item work.
                if (gold > 0) await AddGoldToPlayer(fromPlayer, gold);
                if (!string.IsNullOrEmpty(itemsJson) && itemsJson != "[]")
                    await AddItemsToPlayerSave(fromPlayer, itemsJson);
                bool hasItems = !string.IsNullOrEmpty(itemsJson) && itemsJson != "[]";
                string returnMsg = hasItems
                    ? $"Your trade package expired. {gold:N0} gold and items returned."
                    : $"Your trade package expired and {gold:N0} gold was returned.";
                await SendMessage("System", fromPlayer, "trade", returnMsg);
            }
        }
        catch (Exception ex)
        {
            DebugLogger.Instance.LogError("SQL", $"Failed to expire old trade offers: {ex.Message}");
        }
    }

    // ═══════════════════════════════════════════════════════════════════════════
    // Bounties
    // ═══════════════════════════════════════════════════════════════════════════

    public async Task PlaceBounty(string placedBy, string targetPlayer, long amount)
    {
        try
        {
            using var connection = OpenConnection();
            using var cmd = connection.CreateCommand();
            cmd.CommandText = @"INSERT INTO bounties (target_player, placed_by, amount)
                                VALUES (LOWER(@target), LOWER(@placer), @amount);";
            cmd.Parameters.AddWithValue("@target", targetPlayer);
            cmd.Parameters.AddWithValue("@placer", placedBy);
            cmd.Parameters.AddWithValue("@amount", amount);
            await cmd.ExecuteNonQueryAsync();
        }
        catch (Exception ex) { DebugLogger.Instance.LogError("SQL", $"Failed to place bounty: {ex.Message}"); }
    }

    public async Task<List<BountyInfo>> GetActiveBounties(int limit = 20)
    {
        var bounties = new List<BountyInfo>();
        try
        {
            using var connection = OpenConnection();
            using var cmd = connection.CreateCommand();
            cmd.CommandText = @"SELECT id, target_player, placed_by, amount, placed_at
                                FROM bounties WHERE status = 'active'
                                ORDER BY amount DESC LIMIT @limit;";
            cmd.Parameters.AddWithValue("@limit", limit);
            using var reader = await cmd.ExecuteReaderAsync();
            while (await reader.ReadAsync())
            {
                bounties.Add(new BountyInfo
                {
                    Id = reader.GetInt32(0),
                    TargetPlayer = reader.GetString(1),
                    PlacedBy = reader.GetString(2),
                    Amount = reader.GetInt64(3),
                    PlacedAt = DateTime.TryParse(reader.GetString(4), out var dt) ? dt : DateTime.Now
                });
            }
        }
        catch (Exception ex) { DebugLogger.Instance.LogError("SQL", $"Failed to get bounties: {ex.Message}"); }
        return bounties;
    }

    public async Task<List<BountyInfo>> GetBountiesOnPlayer(string targetPlayer)
    {
        var bounties = new List<BountyInfo>();
        try
        {
            using var connection = OpenConnection();
            using var cmd = connection.CreateCommand();
            cmd.CommandText = @"SELECT id, target_player, placed_by, amount, placed_at
                                FROM bounties WHERE LOWER(target_player) = LOWER(@target) AND status = 'active'
                                ORDER BY amount DESC;";
            cmd.Parameters.AddWithValue("@target", targetPlayer);
            using var reader = await cmd.ExecuteReaderAsync();
            while (await reader.ReadAsync())
            {
                bounties.Add(new BountyInfo
                {
                    Id = reader.GetInt32(0),
                    TargetPlayer = reader.GetString(1),
                    PlacedBy = reader.GetString(2),
                    Amount = reader.GetInt64(3),
                    PlacedAt = DateTime.TryParse(reader.GetString(4), out var dt) ? dt : DateTime.Now
                });
            }
        }
        catch (Exception ex) { DebugLogger.Instance.LogError("SQL", $"Failed to get bounties on player: {ex.Message}"); }
        return bounties;
    }

    public async Task<long> ClaimBounties(string targetPlayer, string claimedBy)
    {
        long totalClaimed = 0;
        try
        {
            using var connection = OpenConnection();
            using var transaction = connection.BeginTransaction();

            // Sum active bounties
            using (var sumCmd = connection.CreateCommand())
            {
                sumCmd.Transaction = transaction;
                sumCmd.CommandText = @"SELECT COALESCE(SUM(amount), 0) FROM bounties
                                      WHERE LOWER(target_player) = LOWER(@target) AND status = 'active';";
                sumCmd.Parameters.AddWithValue("@target", targetPlayer);
                totalClaimed = (long)(sumCmd.ExecuteScalar() ?? 0L);
            }

            if (totalClaimed > 0)
            {
                // Mark all claimed
                using var updateCmd = connection.CreateCommand();
                updateCmd.Transaction = transaction;
                updateCmd.CommandText = @"UPDATE bounties SET status = 'claimed', claimed_by = LOWER(@claimer),
                                         claimed_at = datetime('now')
                                         WHERE LOWER(target_player) = LOWER(@target) AND status = 'active';";
                updateCmd.Parameters.AddWithValue("@target", targetPlayer);
                updateCmd.Parameters.AddWithValue("@claimer", claimedBy);
                await updateCmd.ExecuteNonQueryAsync();
            }

            transaction.Commit();
        }
        catch (Exception ex) { DebugLogger.Instance.LogError("SQL", $"Failed to claim bounties: {ex.Message}"); }
        return totalClaimed;
    }

    public int GetActiveBountyCount(string placedBy)
    {
        try
        {
            using var connection = OpenConnection();
            using var cmd = connection.CreateCommand();
            cmd.CommandText = @"SELECT COUNT(*) FROM bounties WHERE LOWER(placed_by) = LOWER(@placer) AND status = 'active';";
            cmd.Parameters.AddWithValue("@placer", placedBy);
            return Convert.ToInt32(cmd.ExecuteScalar());
        }
        catch { return 0; }
    }

    // ═══════════════════════════════════════════════════════════════════════════
    // Auction House
    // ═══════════════════════════════════════════════════════════════════════════

    public async Task<int> CreateAuctionListing(string seller, string itemName, string itemJson, long price, int hoursToExpire = 48)
    {
        try
        {
            using var connection = OpenConnection();
            using var cmd = connection.CreateCommand();
            cmd.CommandText = @"INSERT INTO auction_listings (seller, item_name, item_json, price, expires_at)
                                VALUES (LOWER(@seller), @itemName, @itemJson, @price, datetime('now', '+' || @hours || ' hours'))
                                RETURNING id;";
            cmd.Parameters.AddWithValue("@seller", seller);
            cmd.Parameters.AddWithValue("@itemName", itemName);
            cmd.Parameters.AddWithValue("@itemJson", itemJson);
            cmd.Parameters.AddWithValue("@price", price);
            cmd.Parameters.AddWithValue("@hours", hoursToExpire);
            var result = await cmd.ExecuteScalarAsync();
            return Convert.ToInt32(result);
        }
        catch (Exception ex) { DebugLogger.Instance.LogError("SQL", $"Failed to create auction: {ex.Message}"); return -1; }
    }

    public async Task<List<AuctionListing>> GetActiveAuctionListings(int limit = 50)
    {
        var listings = new List<AuctionListing>();
        try
        {
            using var connection = OpenConnection();
            using var cmd = connection.CreateCommand();
            cmd.CommandText = @"SELECT id, seller, item_name, item_json, price, listed_at, expires_at
                                FROM auction_listings WHERE status = 'active' AND expires_at > datetime('now')
                                ORDER BY listed_at DESC LIMIT @limit;";
            cmd.Parameters.AddWithValue("@limit", limit);
            using var reader = await cmd.ExecuteReaderAsync();
            while (await reader.ReadAsync())
            {
                listings.Add(new AuctionListing
                {
                    Id = reader.GetInt32(0),
                    Seller = reader.GetString(1),
                    ItemName = reader.GetString(2),
                    ItemJson = reader.GetString(3),
                    Price = reader.GetInt64(4),
                    ListedAt = DateTime.TryParse(reader.GetString(5), out var lt) ? lt : DateTime.Now,
                    ExpiresAt = DateTime.TryParse(reader.GetString(6), out var et) ? et : DateTime.Now,
                    Status = "active"
                });
            }
        }
        catch (Exception ex) { DebugLogger.Instance.LogError("SQL", $"Failed to get auction listings: {ex.Message}"); }
        return listings;
    }

    public async Task<AuctionListing?> GetAuctionListing(int listingId)
    {
        try
        {
            using var connection = OpenConnection();
            using var cmd = connection.CreateCommand();
            cmd.CommandText = @"SELECT id, seller, item_name, item_json, price, listed_at, expires_at, status
                                FROM auction_listings WHERE id = @id;";
            cmd.Parameters.AddWithValue("@id", listingId);
            using var reader = await cmd.ExecuteReaderAsync();
            if (await reader.ReadAsync())
            {
                return new AuctionListing
                {
                    Id = reader.GetInt32(0),
                    Seller = reader.GetString(1),
                    ItemName = reader.GetString(2),
                    ItemJson = reader.GetString(3),
                    Price = reader.GetInt64(4),
                    ListedAt = DateTime.TryParse(reader.GetString(5), out var lt) ? lt : DateTime.Now,
                    ExpiresAt = DateTime.TryParse(reader.GetString(6), out var et) ? et : DateTime.Now,
                    Status = reader.GetString(7)
                };
            }
        }
        catch (Exception ex) { DebugLogger.Instance.LogError("SQL", $"Failed to get auction listing: {ex.Message}"); }
        return null;
    }

    public async Task<bool> BuyAuctionListing(int listingId, string buyer)
    {
        try
        {
            using var connection = OpenConnection();
            using var cmd = connection.CreateCommand();
            cmd.CommandText = @"UPDATE auction_listings SET status = 'sold', buyer = LOWER(@buyer)
                                WHERE id = @id AND status = 'active' AND expires_at > datetime('now');";
            cmd.Parameters.AddWithValue("@id", listingId);
            cmd.Parameters.AddWithValue("@buyer", buyer);
            var affected = await cmd.ExecuteNonQueryAsync();
            return affected > 0;
        }
        catch (Exception ex) { DebugLogger.Instance.LogError("SQL", $"Failed to buy auction: {ex.Message}"); return false; }
    }

    /// <summary>
    /// v0.57.1 — un-sell a listing when the buyer's side of the transaction fails after
    /// BuyAuctionListing already marked it sold (e.g., corrupt item JSON). Returns the listing
    /// to active status so the buyer can try again / another buyer can purchase / the seller
    /// eventually collects on expiry.
    /// </summary>
    public async Task<bool> RefundAuctionListing(int listingId)
    {
        try
        {
            using var connection = OpenConnection();
            using var cmd = connection.CreateCommand();
            cmd.CommandText = @"UPDATE auction_listings SET status = 'active', buyer = NULL
                                WHERE id = @id AND status = 'sold';";
            cmd.Parameters.AddWithValue("@id", listingId);
            var affected = await cmd.ExecuteNonQueryAsync();
            return affected > 0;
        }
        catch (Exception ex) { DebugLogger.Instance.LogError("SQL", $"Failed to refund auction: {ex.Message}"); return false; }
    }

    public async Task<bool> CancelAuctionListing(int listingId, string seller)
    {
        try
        {
            using var connection = OpenConnection();
            using var cmd = connection.CreateCommand();
            cmd.CommandText = @"UPDATE auction_listings SET status = 'cancelled'
                                WHERE id = @id AND LOWER(seller) = LOWER(@seller) AND status = 'active';";
            cmd.Parameters.AddWithValue("@id", listingId);
            cmd.Parameters.AddWithValue("@seller", seller);
            var affected = await cmd.ExecuteNonQueryAsync();
            return affected > 0;
        }
        catch (Exception ex) { DebugLogger.Instance.LogError("SQL", $"Failed to cancel auction: {ex.Message}"); return false; }
    }

    public async Task<List<AuctionListing>> GetMyAuctionListings(string seller)
    {
        var listings = new List<AuctionListing>();
        try
        {
            using var connection = OpenConnection();
            using var cmd = connection.CreateCommand();
            cmd.CommandText = @"SELECT id, seller, item_name, item_json, price, listed_at, expires_at, status, COALESCE(gold_collected, 0)
                                FROM auction_listings WHERE LOWER(seller) = LOWER(@seller)
                                AND NOT (status = 'sold' AND COALESCE(gold_collected, 0) = 1)
                                AND status NOT IN ('collected', 'cancelled')
                                ORDER BY listed_at DESC LIMIT 20;";
            cmd.Parameters.AddWithValue("@seller", seller);
            using var reader = await cmd.ExecuteReaderAsync();
            while (await reader.ReadAsync())
            {
                listings.Add(new AuctionListing
                {
                    Id = reader.GetInt32(0),
                    Seller = reader.GetString(1),
                    ItemName = reader.GetString(2),
                    ItemJson = reader.GetString(3),
                    Price = reader.GetInt64(4),
                    ListedAt = DateTime.TryParse(reader.GetString(5), out var lt) ? lt : DateTime.Now,
                    ExpiresAt = DateTime.TryParse(reader.GetString(6), out var et) ? et : DateTime.Now,
                    Status = reader.GetString(7),
                    GoldCollected = reader.GetInt32(8) != 0
                });
            }
        }
        catch (Exception ex) { DebugLogger.Instance.LogError("SQL", $"Failed to get my auctions: {ex.Message}"); }
        return listings;
    }

    public async Task<bool> CollectExpiredAuctionListing(int listingId, string seller)
    {
        try
        {
            using var connection = OpenConnection();
            using var cmd = connection.CreateCommand();
            cmd.CommandText = @"UPDATE auction_listings SET status = 'collected'
                                WHERE id = @id AND LOWER(seller) = LOWER(@seller) AND status = 'expired';";
            cmd.Parameters.AddWithValue("@id", listingId);
            cmd.Parameters.AddWithValue("@seller", seller);
            var affected = await cmd.ExecuteNonQueryAsync();
            return affected > 0;
        }
        catch (Exception ex) { DebugLogger.Instance.LogError("SQL", $"Failed to collect expired auction: {ex.Message}"); return false; }
    }

    public async Task CleanupExpiredAuctions()
    {
        try
        {
            using var connection = OpenConnection();
            using var cmd = connection.CreateCommand();
            cmd.CommandText = @"UPDATE auction_listings SET status = 'expired'
                                WHERE status = 'active' AND expires_at <= datetime('now');";
            await cmd.ExecuteNonQueryAsync();
        }
        catch (Exception ex) { DebugLogger.Instance.LogError("SQL", $"Failed to cleanup auctions: {ex.Message}"); }
    }

    public async Task<bool> CollectAuctionGold(int listingId, string seller)
    {
        try
        {
            using var connection = OpenConnection();
            using var cmd = connection.CreateCommand();
            cmd.CommandText = @"UPDATE auction_listings SET gold_collected = 1
                                WHERE id = @id AND LOWER(seller) = LOWER(@seller) AND status = 'sold' AND COALESCE(gold_collected, 0) = 0;";
            cmd.Parameters.AddWithValue("@id", listingId);
            cmd.Parameters.AddWithValue("@seller", seller);
            var affected = await cmd.ExecuteNonQueryAsync();
            return affected > 0;
        }
        catch (Exception ex) { DebugLogger.Instance.LogError("SQL", $"Failed to collect auction gold: {ex.Message}"); return false; }
    }

    // ═══════════════════════════════════════════════════════════════════════════
    // Team Wars
    // ═══════════════════════════════════════════════════════════════════════════

    public async Task<int> CreateTeamWar(string challengerTeam, string defenderTeam, long goldWagered)
    {
        try
        {
            using var connection = OpenConnection();
            using var cmd = connection.CreateCommand();
            cmd.CommandText = @"INSERT INTO team_wars (challenger_team, defender_team, status, gold_wagered)
                                VALUES (@challenger, @defender, 'active', @gold) RETURNING id;";
            cmd.Parameters.AddWithValue("@challenger", challengerTeam);
            cmd.Parameters.AddWithValue("@defender", defenderTeam);
            cmd.Parameters.AddWithValue("@gold", goldWagered);
            var result = await cmd.ExecuteScalarAsync();
            return Convert.ToInt32(result);
        }
        catch (Exception ex) { DebugLogger.Instance.LogError("SQL", $"Failed to create team war: {ex.Message}"); return -1; }
    }

    public async Task UpdateTeamWarScore(int warId, bool challengerWon)
    {
        try
        {
            using var connection = OpenConnection();
            using var cmd = connection.CreateCommand();
            cmd.CommandText = challengerWon
                ? "UPDATE team_wars SET challenger_wins = challenger_wins + 1 WHERE id = @id;"
                : "UPDATE team_wars SET defender_wins = defender_wins + 1 WHERE id = @id;";
            cmd.Parameters.AddWithValue("@id", warId);
            await cmd.ExecuteNonQueryAsync();
        }
        catch (Exception ex) { DebugLogger.Instance.LogError("SQL", $"Failed to update war score: {ex.Message}"); }
    }

    public async Task CompleteTeamWar(int warId, string result)
    {
        try
        {
            using var connection = OpenConnection();
            using var cmd = connection.CreateCommand();
            cmd.CommandText = @"UPDATE team_wars SET status = @result, finished_at = datetime('now') WHERE id = @id;";
            cmd.Parameters.AddWithValue("@id", warId);
            cmd.Parameters.AddWithValue("@result", result);
            await cmd.ExecuteNonQueryAsync();
        }
        catch (Exception ex) { DebugLogger.Instance.LogError("SQL", $"Failed to complete team war: {ex.Message}"); }
    }

    public async Task<List<TeamWarInfo>> GetTeamWarHistory(string teamName, int limit = 10)
    {
        var wars = new List<TeamWarInfo>();
        try
        {
            using var connection = OpenConnection();
            using var cmd = connection.CreateCommand();
            cmd.CommandText = @"SELECT id, challenger_team, defender_team, status, challenger_wins, defender_wins,
                                       gold_wagered, started_at, finished_at
                                FROM team_wars
                                WHERE (challenger_team = @team OR defender_team = @team)
                                ORDER BY started_at DESC LIMIT @limit;";
            cmd.Parameters.AddWithValue("@team", teamName);
            cmd.Parameters.AddWithValue("@limit", limit);
            using var reader = await cmd.ExecuteReaderAsync();
            while (await reader.ReadAsync())
            {
                wars.Add(new TeamWarInfo
                {
                    Id = reader.GetInt32(0),
                    ChallengerTeam = reader.GetString(1),
                    DefenderTeam = reader.GetString(2),
                    Status = reader.GetString(3),
                    ChallengerWins = reader.GetInt32(4),
                    DefenderWins = reader.GetInt32(5),
                    GoldWagered = reader.GetInt64(6),
                    StartedAt = DateTime.TryParse(reader.GetString(7), out var st) ? st : DateTime.Now,
                    FinishedAt = reader.IsDBNull(8) ? null : DateTime.TryParse(reader.GetString(8), out var ft) ? ft : null
                });
            }
        }
        catch (Exception ex) { DebugLogger.Instance.LogError("SQL", $"Failed to get war history: {ex.Message}"); }
        return wars;
    }

    public bool HasActiveTeamWar(string teamName)
    {
        try
        {
            using var connection = OpenConnection();
            using var cmd = connection.CreateCommand();
            cmd.CommandText = @"SELECT COUNT(*) FROM team_wars
                                WHERE (challenger_team = @team OR defender_team = @team) AND status = 'active';";
            cmd.Parameters.AddWithValue("@team", teamName);
            return Convert.ToInt32(cmd.ExecuteScalar()) > 0;
        }
        catch { return false; }
    }

    // ═══════════════════════════════════════════════════════════════════════════
    // World Bosses
    // ═══════════════════════════════════════════════════════════════════════════

    public async Task<int> SpawnWorldBoss(string bossName, int bossLevel, long maxHp, int hoursToExpire = 24, string bossDataJson = "{}")
    {
        try
        {
            using var connection = OpenConnection();
            using var cmd = connection.CreateCommand();
            cmd.CommandText = @"INSERT INTO world_bosses (boss_name, boss_level, max_hp, current_hp, boss_data_json, expires_at)
                                VALUES (@name, @level, @hp, @hp, @json, datetime('now', '+' || @hours || ' hours'))
                                RETURNING id;";
            cmd.Parameters.AddWithValue("@name", bossName);
            cmd.Parameters.AddWithValue("@level", bossLevel);
            cmd.Parameters.AddWithValue("@hp", maxHp);
            cmd.Parameters.AddWithValue("@json", bossDataJson);
            cmd.Parameters.AddWithValue("@hours", hoursToExpire);
            var result = await cmd.ExecuteScalarAsync();
            return Convert.ToInt32(result);
        }
        catch (Exception ex) { DebugLogger.Instance.LogError("SQL", $"Failed to spawn world boss: {ex.Message}"); return -1; }
    }

    public async Task<WorldBossInfo?> GetActiveWorldBoss()
    {
        try
        {
            using var connection = OpenConnection();
            using var cmd = connection.CreateCommand();
            cmd.CommandText = @"SELECT id, boss_name, boss_level, max_hp, current_hp, started_at, expires_at, boss_data_json
                                FROM world_bosses WHERE status = 'active' AND expires_at > datetime('now')
                                ORDER BY started_at DESC LIMIT 1;";
            using var reader = await cmd.ExecuteReaderAsync();
            if (await reader.ReadAsync())
            {
                return new WorldBossInfo
                {
                    Id = reader.GetInt32(0),
                    BossName = reader.GetString(1),
                    BossLevel = reader.GetInt32(2),
                    MaxHP = reader.GetInt64(3),
                    CurrentHP = reader.GetInt64(4),
                    StartedAt = DateTime.TryParse(reader.GetString(5), out var st) ? st : DateTime.Now,
                    ExpiresAt = DateTime.TryParse(reader.GetString(6), out var et) ? et : DateTime.Now,
                    BossDataJson = reader.IsDBNull(7) ? "{}" : reader.GetString(7)
                };
            }
        }
        catch (Exception ex) { DebugLogger.Instance.LogError("SQL", $"Failed to get world boss: {ex.Message}"); }
        return null;
    }

    /// <summary>
    /// Apply a player's damage to the shared world-boss HP pool and return the remaining HP
    /// along with a flag indicating whether this specific call delivered the killing blow.
    /// The killing-blow flag is set atomically via a conditional status flip so that when two
    /// concurrent players bring HP to zero in the same round, only one is credited with the
    /// kill. The other sees remainingHp == 0 but wasKillingBlow == false. Fixes v0.57.9 report
    /// where two players both received "killing blow" broadcasts for the same boss.
    /// </summary>
    public async Task<(long remainingHp, bool wasKillingBlow)> RecordWorldBossDamage(int bossId, string playerName, long damage)
    {
        long remainingHp = 0;
        bool wasKillingBlow = false;
        try
        {
            using var connection = OpenConnection();
            using var transaction = connection.BeginTransaction();

            // Update boss HP
            using (var updateCmd = connection.CreateCommand())
            {
                updateCmd.Transaction = transaction;
                updateCmd.CommandText = @"UPDATE world_bosses SET current_hp = MAX(0, current_hp - @damage)
                                         WHERE id = @id AND status = 'active';";
                updateCmd.Parameters.AddWithValue("@id", bossId);
                updateCmd.Parameters.AddWithValue("@damage", damage);
                await updateCmd.ExecuteNonQueryAsync();
            }

            // Upsert player damage
            using (var dmgCmd = connection.CreateCommand())
            {
                dmgCmd.Transaction = transaction;
                dmgCmd.CommandText = @"INSERT INTO world_boss_damage (boss_id, player_name, damage_dealt, hits)
                                      VALUES (@bossId, LOWER(@player), @damage, 1)
                                      ON CONFLICT(boss_id, player_name) DO UPDATE SET
                                          damage_dealt = damage_dealt + @damage,
                                          hits = hits + 1,
                                          last_hit_at = datetime('now');";
                dmgCmd.Parameters.AddWithValue("@bossId", bossId);
                dmgCmd.Parameters.AddWithValue("@player", playerName);
                dmgCmd.Parameters.AddWithValue("@damage", damage);
                await dmgCmd.ExecuteNonQueryAsync();
            }

            // Get remaining HP
            using (var hpCmd = connection.CreateCommand())
            {
                hpCmd.Transaction = transaction;
                hpCmd.CommandText = "SELECT current_hp FROM world_bosses WHERE id = @id;";
                hpCmd.Parameters.AddWithValue("@id", bossId);
                remainingHp = Convert.ToInt64(hpCmd.ExecuteScalar() ?? 0);
            }

            // If HP hit zero, try to atomically claim the kill by flipping status active → defeated.
            // Only ONE caller whose transaction wins this race will get affected_rows == 1; every
            // other caller who sees remainingHp == 0 will get 0 rows (status is already 'defeated').
            // That row-count is the authoritative "I killed it" signal — remainingHp alone isn't.
            if (remainingHp <= 0)
            {
                using var defeatCmd = connection.CreateCommand();
                defeatCmd.Transaction = transaction;
                defeatCmd.CommandText = @"UPDATE world_bosses SET status = 'defeated'
                                          WHERE id = @id AND status = 'active';";
                defeatCmd.Parameters.AddWithValue("@id", bossId);
                int rowsAffected = await defeatCmd.ExecuteNonQueryAsync();
                wasKillingBlow = rowsAffected == 1;
            }

            transaction.Commit();
        }
        catch (Exception ex) { DebugLogger.Instance.LogError("SQL", $"Failed to record world boss damage: {ex.Message}"); }
        return (remainingHp, wasKillingBlow);
    }

    public async Task<List<WorldBossDamageEntry>> GetWorldBossDamageLeaderboard(int bossId, int limit = 20)
    {
        var entries = new List<WorldBossDamageEntry>();
        try
        {
            using var connection = OpenConnection();
            using var cmd = connection.CreateCommand();
            cmd.CommandText = @"SELECT player_name, damage_dealt, hits FROM world_boss_damage
                                WHERE boss_id = @id ORDER BY damage_dealt DESC LIMIT @limit;";
            cmd.Parameters.AddWithValue("@id", bossId);
            cmd.Parameters.AddWithValue("@limit", limit);
            using var reader = await cmd.ExecuteReaderAsync();
            while (await reader.ReadAsync())
            {
                entries.Add(new WorldBossDamageEntry
                {
                    PlayerName = reader.GetString(0),
                    DamageDealt = reader.GetInt64(1),
                    Hits = reader.GetInt32(2)
                });
            }
        }
        catch (Exception ex) { DebugLogger.Instance.LogError("SQL", $"Failed to get boss damage leaderboard: {ex.Message}"); }
        return entries;
    }

    public async Task ExpireWorldBosses()
    {
        try
        {
            using var connection = OpenConnection();
            using var cmd = connection.CreateCommand();
            cmd.CommandText = @"UPDATE world_bosses SET status = 'expired'
                                WHERE status = 'active' AND expires_at <= datetime('now');";
            await cmd.ExecuteNonQueryAsync();
        }
        catch (Exception ex) { DebugLogger.Instance.LogError("SQL", $"Failed to expire world bosses: {ex.Message}"); }
    }

    public DateTime? GetLastWorldBossEndTime()
    {
        try
        {
            using var connection = OpenConnection();
            using var cmd = connection.CreateCommand();
            // Get the most recent defeated or expired boss end time
            // For defeated: use started_at + 1 hour (approximate defeat time is within the window)
            // For expired: use expires_at
            // Simpler: just get started_at of the most recent boss of any status
            cmd.CommandText = @"SELECT started_at FROM world_bosses
                                WHERE status IN ('defeated', 'expired')
                                ORDER BY started_at DESC LIMIT 1;";
            var result = cmd.ExecuteScalar();
            if (result != null && DateTime.TryParse(result.ToString(), out var lastStart))
                return lastStart;
        }
        catch (Exception ex) { DebugLogger.Instance.LogError("SQL", $"Failed to get last world boss time: {ex.Message}"); }
        return null;
    }

    public async Task UpdateWorldBossData(int bossId, string bossDataJson)
    {
        try
        {
            using var connection = OpenConnection();
            using var cmd = connection.CreateCommand();
            cmd.CommandText = @"UPDATE world_bosses SET boss_data_json = @json WHERE id = @id;";
            cmd.Parameters.AddWithValue("@id", bossId);
            cmd.Parameters.AddWithValue("@json", bossDataJson);
            await cmd.ExecuteNonQueryAsync();
        }
        catch (Exception ex) { DebugLogger.Instance.LogError("SQL", $"Failed to update world boss data: {ex.Message}"); }
    }

    public int GetOnlinePlayerCount()
    {
        try
        {
            using var connection = OpenConnection();
            using var cmd = connection.CreateCommand();
            cmd.CommandText = @"SELECT COUNT(*) FROM online_players WHERE last_heartbeat > datetime('now', '-2 minutes');";
            return Convert.ToInt32(cmd.ExecuteScalar() ?? 0);
        }
        catch { return 0; }
    }

    /// <summary>
    /// v0.60.4: write the latest BotDetectionSystem snapshot JSON to the
    /// single-row bot_detection_snapshot table. UPSERT keyed on id=1.
    /// Called from BotDetectionSystem.WriteSnapshotToDb on the periodic
    /// timer in MudServer; admin dashboard polls /api/admin/bot-stats which
    /// reads back from this table.
    /// </summary>
    public void UpsertBotDetectionSnapshot(string snapshotJson)
    {
        try
        {
            using var connection = OpenConnection();
            using var cmd = connection.CreateCommand();
            cmd.CommandText = @"
                INSERT INTO bot_detection_snapshot (id, snapshot_at, snapshot_json)
                VALUES (1, datetime('now'), @json)
                ON CONFLICT(id) DO UPDATE SET
                    snapshot_at = excluded.snapshot_at,
                    snapshot_json = excluded.snapshot_json;";
            cmd.Parameters.AddWithValue("@json", snapshotJson);
            cmd.ExecuteNonQuery();
        }
        catch (Exception ex)
        {
            DebugLogger.Instance.LogError("SQL", $"Failed to upsert bot detection snapshot: {ex.Message}");
        }
    }

    public int GetAverageOnlineLevel()
    {
        try
        {
            using var connection = OpenConnection();
            using var cmd = connection.CreateCommand();
            cmd.CommandText = @"SELECT AVG(level) FROM online_players WHERE last_heartbeat > datetime('now', '-2 minutes') AND level > 0;";
            var result = cmd.ExecuteScalar();
            return result == DBNull.Value || result == null ? 20 : Convert.ToInt32(result);
        }
        catch { return 20; }
    }

    // ═══════════════════════════════════════════════════════════════════════════
    // Castle Sieges
    // ═══════════════════════════════════════════════════════════════════════════

    public async Task<int> StartCastleSiege(string teamName, int totalGuards)
    {
        try
        {
            using var connection = OpenConnection();
            using var cmd = connection.CreateCommand();
            cmd.CommandText = @"INSERT INTO castle_sieges (team_name, total_guards)
                                VALUES (@team, @guards) RETURNING id;";
            cmd.Parameters.AddWithValue("@team", teamName);
            cmd.Parameters.AddWithValue("@guards", totalGuards);
            var result = await cmd.ExecuteScalarAsync();
            return Convert.ToInt32(result);
        }
        catch (Exception ex) { DebugLogger.Instance.LogError("SQL", $"Failed to start siege: {ex.Message}"); return -1; }
    }

    public async Task UpdateSiegeProgress(int siegeId, int guardsDefeated)
    {
        try
        {
            using var connection = OpenConnection();
            using var cmd = connection.CreateCommand();
            cmd.CommandText = @"UPDATE castle_sieges SET guards_defeated = @defeated WHERE id = @id;";
            cmd.Parameters.AddWithValue("@id", siegeId);
            cmd.Parameters.AddWithValue("@defeated", guardsDefeated);
            await cmd.ExecuteNonQueryAsync();
        }
        catch (Exception ex) { DebugLogger.Instance.LogError("SQL", $"Failed to update siege: {ex.Message}"); }
    }

    public async Task CompleteSiege(int siegeId, string result)
    {
        try
        {
            using var connection = OpenConnection();
            using var cmd = connection.CreateCommand();
            cmd.CommandText = @"UPDATE castle_sieges SET result = @result, finished_at = datetime('now') WHERE id = @id;";
            cmd.Parameters.AddWithValue("@id", siegeId);
            cmd.Parameters.AddWithValue("@result", result);
            await cmd.ExecuteNonQueryAsync();
        }
        catch (Exception ex) { DebugLogger.Instance.LogError("SQL", $"Failed to complete siege: {ex.Message}"); }
    }

    public bool CanTeamSiege(string teamName)
    {
        try
        {
            using var connection = OpenConnection();
            using var cmd = connection.CreateCommand();
            // 24h cooldown between sieges
            cmd.CommandText = @"SELECT COUNT(*) FROM castle_sieges
                                WHERE team_name = @team AND started_at > datetime('now', '-24 hours');";
            cmd.Parameters.AddWithValue("@team", teamName);
            return Convert.ToInt32(cmd.ExecuteScalar()) == 0;
        }
        catch { return false; }
    }

    public async Task<List<CastleSiegeInfo>> GetSiegeHistory(int limit = 10)
    {
        var sieges = new List<CastleSiegeInfo>();
        try
        {
            using var connection = OpenConnection();
            using var cmd = connection.CreateCommand();
            cmd.CommandText = @"SELECT id, team_name, guards_defeated, total_guards, result, started_at
                                FROM castle_sieges ORDER BY started_at DESC LIMIT @limit;";
            cmd.Parameters.AddWithValue("@limit", limit);
            using var reader = await cmd.ExecuteReaderAsync();
            while (await reader.ReadAsync())
            {
                sieges.Add(new CastleSiegeInfo
                {
                    Id = reader.GetInt32(0),
                    TeamName = reader.GetString(1),
                    GuardsDefeated = reader.GetInt32(2),
                    TotalGuards = reader.GetInt32(3),
                    Result = reader.GetString(4),
                    StartedAt = DateTime.TryParse(reader.GetString(5), out var st) ? st : DateTime.Now
                });
            }
        }
        catch (Exception ex) { DebugLogger.Instance.LogError("SQL", $"Failed to get siege history: {ex.Message}"); }
        return sieges;
    }

    // ═══════════════════════════════════════════════════════════════════════════
    // Team Headquarters / Upgrades / Vault
    // ═══════════════════════════════════════════════════════════════════════════

    public async Task<List<TeamUpgradeInfo>> GetTeamUpgrades(string teamName)
    {
        var upgrades = new List<TeamUpgradeInfo>();
        try
        {
            using var connection = OpenConnection();
            using var cmd = connection.CreateCommand();
            cmd.CommandText = @"SELECT upgrade_type, level, invested_gold FROM team_upgrades
                                WHERE team_name = @team ORDER BY upgrade_type;";
            cmd.Parameters.AddWithValue("@team", teamName);
            using var reader = await cmd.ExecuteReaderAsync();
            while (await reader.ReadAsync())
            {
                upgrades.Add(new TeamUpgradeInfo
                {
                    UpgradeType = reader.GetString(0),
                    Level = reader.GetInt32(1),
                    InvestedGold = reader.GetInt64(2)
                });
            }
        }
        catch (Exception ex) { DebugLogger.Instance.LogError("SQL", $"Failed to get team upgrades: {ex.Message}"); }
        return upgrades;
    }

    public async Task<bool> UpgradeTeamFacility(string teamName, string upgradeType, long cost)
    {
        try
        {
            using var connection = OpenConnection();
            using var cmd = connection.CreateCommand();
            cmd.CommandText = @"INSERT INTO team_upgrades (team_name, upgrade_type, level, invested_gold)
                                VALUES (@team, @type, 1, @cost)
                                ON CONFLICT(team_name, upgrade_type) DO UPDATE SET
                                    level = level + 1,
                                    invested_gold = invested_gold + @cost;";
            cmd.Parameters.AddWithValue("@team", teamName);
            cmd.Parameters.AddWithValue("@type", upgradeType);
            cmd.Parameters.AddWithValue("@cost", cost);
            await cmd.ExecuteNonQueryAsync();
            return true;
        }
        catch (Exception ex) { DebugLogger.Instance.LogError("SQL", $"Failed to upgrade facility: {ex.Message}"); return false; }
    }

    public async Task<long> GetTeamVaultGold(string teamName)
    {
        try
        {
            using var connection = OpenConnection();
            using var cmd = connection.CreateCommand();
            cmd.CommandText = @"SELECT gold FROM team_vault WHERE team_name = @team;";
            cmd.Parameters.AddWithValue("@team", teamName);
            var result = cmd.ExecuteScalar();
            return result != null ? Convert.ToInt64(result) : 0;
        }
        catch { return 0; }
    }

    public async Task<bool> DepositToTeamVault(string teamName, long amount)
    {
        try
        {
            using var connection = OpenConnection();
            using var cmd = connection.CreateCommand();
            cmd.CommandText = @"INSERT INTO team_vault (team_name, gold) VALUES (@team, @amount)
                                ON CONFLICT(team_name) DO UPDATE SET gold = gold + @amount;";
            cmd.Parameters.AddWithValue("@team", teamName);
            cmd.Parameters.AddWithValue("@amount", amount);
            await cmd.ExecuteNonQueryAsync();
            return true;
        }
        catch (Exception ex) { DebugLogger.Instance.LogError("SQL", $"Failed to deposit to vault: {ex.Message}"); return false; }
    }

    public async Task<bool> WithdrawFromTeamVault(string teamName, long amount)
    {
        try
        {
            using var connection = OpenConnection();
            using var cmd = connection.CreateCommand();
            cmd.CommandText = @"UPDATE team_vault SET gold = gold - @amount
                                WHERE team_name = @team AND gold >= @amount;";
            cmd.Parameters.AddWithValue("@team", teamName);
            cmd.Parameters.AddWithValue("@amount", amount);
            var affected = await cmd.ExecuteNonQueryAsync();
            return affected > 0;
        }
        catch (Exception ex) { DebugLogger.Instance.LogError("SQL", $"Failed to withdraw from vault: {ex.Message}"); return false; }
    }

    public int GetTeamUpgradeLevel(string teamName, string upgradeType)
    {
        try
        {
            using var connection = OpenConnection();
            using var cmd = connection.CreateCommand();
            cmd.CommandText = @"SELECT level FROM team_upgrades WHERE team_name = @team AND upgrade_type = @type;";
            cmd.Parameters.AddWithValue("@team", teamName);
            cmd.Parameters.AddWithValue("@type", upgradeType);
            var result = cmd.ExecuteScalar();
            return result != null ? Convert.ToInt32(result) : 0;
        }
        catch { return 0; }
    }
        // =====================================================================
        // Wizard System
        // =====================================================================

        /// <summary>Get the wizard level for a player. Returns Mortal if not found.</summary>
        public async Task<WizardLevel> GetWizardLevel(string username)
        {
            try
            {
                using var connection = OpenConnection();
                using var cmd = connection.CreateCommand();
                cmd.CommandText = "SELECT wizard_level FROM players WHERE LOWER(username) = LOWER(@username)";
                cmd.Parameters.AddWithValue("@username", username);
                var result = await cmd.ExecuteScalarAsync();
                if (result != null && result != DBNull.Value)
                    return (WizardLevel)Convert.ToInt32(result);
                return WizardLevel.Mortal;
            }
            catch (Exception ex)
            {
                DebugLogger.Instance.LogError("SQL", $"GetWizardLevel failed for '{username}': {ex.Message}");
                return WizardLevel.Mortal;
            }
        }

        /// <summary>Set the wizard level for a player.</summary>
        public async Task SetWizardLevel(string username, WizardLevel level)
        {
            try
            {
                using var connection = OpenConnection();
                using var cmd = connection.CreateCommand();
                cmd.CommandText = "UPDATE players SET wizard_level = @level WHERE LOWER(username) = LOWER(@username)";
                cmd.Parameters.AddWithValue("@level", (int)level);
                cmd.Parameters.AddWithValue("@username", username);
                await cmd.ExecuteNonQueryAsync();
            }
            catch (Exception ex)
            {
                DebugLogger.Instance.LogError("SQL", $"SetWizardLevel failed for '{username}': {ex.Message}");
            }
        }

        /// <summary>Get freeze/mute flags for a player.</summary>
        public async Task<(bool isFrozen, bool isMuted)> GetWizardFlags(string username)
        {
            try
            {
                using var connection = OpenConnection();
                using var cmd = connection.CreateCommand();
                cmd.CommandText = "SELECT is_frozen, is_muted FROM wizard_flags WHERE LOWER(username) = LOWER(@username)";
                cmd.Parameters.AddWithValue("@username", username);
                using var reader = await cmd.ExecuteReaderAsync();
                if (await reader.ReadAsync())
                {
                    return (reader.GetInt32(0) != 0, reader.GetInt32(1) != 0);
                }
                return (false, false);
            }
            catch (Exception ex)
            {
                DebugLogger.Instance.LogError("SQL", $"GetWizardFlags failed for '{username}': {ex.Message}");
                return (false, false);
            }
        }

        /// <summary>Set frozen status for a player.</summary>
        public async Task SetFrozen(string username, bool frozen, string? frozenBy = null)
        {
            try
            {
                using var connection = OpenConnection();
                using var cmd = connection.CreateCommand();
                cmd.CommandText = @"
                    INSERT INTO wizard_flags (username, is_frozen, frozen_by, frozen_at)
                    VALUES (LOWER(@username), @frozen, @frozenBy, datetime('now'))
                    ON CONFLICT(username) DO UPDATE SET
                        is_frozen = @frozen,
                        frozen_by = CASE WHEN @frozen = 1 THEN @frozenBy ELSE NULL END,
                        frozen_at = CASE WHEN @frozen = 1 THEN datetime('now') ELSE NULL END";
                cmd.Parameters.AddWithValue("@username", username);
                cmd.Parameters.AddWithValue("@frozen", frozen ? 1 : 0);
                cmd.Parameters.AddWithValue("@frozenBy", (object?)frozenBy ?? DBNull.Value);
                await cmd.ExecuteNonQueryAsync();
            }
            catch (Exception ex)
            {
                DebugLogger.Instance.LogError("SQL", $"SetFrozen failed for '{username}': {ex.Message}");
            }
        }

        /// <summary>Set muted status for a player.</summary>
        public async Task SetMuted(string username, bool muted, string? mutedBy = null)
        {
            try
            {
                using var connection = OpenConnection();
                using var cmd = connection.CreateCommand();
                cmd.CommandText = @"
                    INSERT INTO wizard_flags (username, is_muted, muted_by, muted_at)
                    VALUES (LOWER(@username), @muted, @mutedBy, datetime('now'))
                    ON CONFLICT(username) DO UPDATE SET
                        is_muted = @muted,
                        muted_by = CASE WHEN @muted = 1 THEN @mutedBy ELSE NULL END,
                        muted_at = CASE WHEN @muted = 1 THEN datetime('now') ELSE NULL END";
                cmd.Parameters.AddWithValue("@username", username);
                cmd.Parameters.AddWithValue("@muted", muted ? 1 : 0);
                cmd.Parameters.AddWithValue("@mutedBy", (object?)mutedBy ?? DBNull.Value);
                await cmd.ExecuteNonQueryAsync();
            }
            catch (Exception ex)
            {
                DebugLogger.Instance.LogError("SQL", $"SetMuted failed for '{username}': {ex.Message}");
            }
        }

        /// <summary>Log a wizard action to the audit trail.</summary>
        public void LogWizardAction(string wizardName, string action, string? target = null, string? details = null)
        {
            try
            {
                using var connection = OpenConnection();
                using var cmd = connection.CreateCommand();
                cmd.CommandText = @"
                    INSERT INTO wizard_log (wizard_name, action, target, details)
                    VALUES (@wizard, @action, @target, @details)";
                cmd.Parameters.AddWithValue("@wizard", wizardName);
                cmd.Parameters.AddWithValue("@action", action);
                cmd.Parameters.AddWithValue("@target", (object?)target ?? DBNull.Value);
                cmd.Parameters.AddWithValue("@details", (object?)details ?? DBNull.Value);
                cmd.ExecuteNonQuery();
            }
            catch (Exception ex)
            {
                DebugLogger.Instance.LogError("SQL", $"LogWizardAction failed: {ex.Message}");
            }
        }

        /// <summary>Get recent wizard log entries.</summary>
        public async Task<List<WizardLogEntry>> GetRecentWizardLog(int count = 50)
        {
            var entries = new List<WizardLogEntry>();
            try
            {
                using var connection = OpenConnection();
                using var cmd = connection.CreateCommand();
                cmd.CommandText = @"
                    SELECT wizard_name, action, target, details, created_at
                    FROM wizard_log ORDER BY created_at DESC LIMIT @count";
                cmd.Parameters.AddWithValue("@count", count);
                using var reader = await cmd.ExecuteReaderAsync();
                while (await reader.ReadAsync())
                {
                    entries.Add(new WizardLogEntry
                    {
                        WizardName = reader.GetString(0),
                        Action = reader.GetString(1),
                        Target = reader.IsDBNull(2) ? null : reader.GetString(2),
                        Details = reader.IsDBNull(3) ? null : reader.GetString(3),
                        CreatedAt = reader.GetString(4)
                    });
                }
            }
            catch (Exception ex)
            {
                DebugLogger.Instance.LogError("SQL", $"GetRecentWizardLog failed: {ex.Message}");
            }
            return entries;
        }

        // =====================================================================
        // Admin Command Queue (Web Dashboard → MUD Server IPC)
        // =====================================================================

        /// <summary>Get all pending admin commands from the web dashboard.</summary>
        public List<AdminCommand> GetPendingAdminCommands()
        {
            var commands = new List<AdminCommand>();
            try
            {
                using var connection = OpenConnection();
                using var cmd = connection.CreateCommand();
                cmd.CommandText = @"
                    SELECT id, command, target_username, args
                    FROM admin_commands WHERE status = 'pending'
                    ORDER BY id LIMIT 20";
                using var reader = cmd.ExecuteReader();
                while (reader.Read())
                {
                    commands.Add(new AdminCommand
                    {
                        Id = reader.GetInt32(0),
                        Command = reader.GetString(1),
                        TargetUsername = reader.IsDBNull(2) ? null : reader.GetString(2),
                        Args = reader.IsDBNull(3) ? null : reader.GetString(3)
                    });
                }
            }
            catch (ObjectDisposedException)
            {
                DebugLogger.Instance.LogDebug("SQL", "GetPendingAdminCommands skipped — connection disposed");
            }
            catch (Exception ex)
            {
                DebugLogger.Instance.LogError("SQL", $"GetPendingAdminCommands failed: {ex.Message}");
            }
            return commands;
        }

        /// <summary>Mark an admin command as successfully executed.</summary>
        public void MarkAdminCommandExecuted(int id, string result)
        {
            try
            {
                using var connection = OpenConnection();
                using var cmd = connection.CreateCommand();
                cmd.CommandText = @"
                    UPDATE admin_commands SET status = 'executed', result = @result,
                    executed_at = datetime('now') WHERE id = @id";
                cmd.Parameters.AddWithValue("@id", id);
                cmd.Parameters.AddWithValue("@result", result);
                cmd.ExecuteNonQuery();
            }
            catch (Exception ex)
            {
                DebugLogger.Instance.LogError("SQL", $"MarkAdminCommandExecuted failed: {ex.Message}");
            }
        }

        /// <summary>Mark an admin command as failed.</summary>
        public void MarkAdminCommandFailed(int id, string error)
        {
            try
            {
                using var connection = OpenConnection();
                using var cmd = connection.CreateCommand();
                cmd.CommandText = @"
                    UPDATE admin_commands SET status = 'failed', result = @error,
                    executed_at = datetime('now') WHERE id = @id";
                cmd.Parameters.AddWithValue("@id", id);
                cmd.Parameters.AddWithValue("@error", error);
                cmd.ExecuteNonQuery();
            }
            catch (Exception ex)
            {
                DebugLogger.Instance.LogError("SQL", $"MarkAdminCommandFailed failed: {ex.Message}");
            }
        }

        /// <summary>Write a line of snooped terminal output to the buffer.</summary>
        public void WriteSnoopLine(string targetUsername, string line)
        {
            try
            {
                using var connection = OpenConnection();
                using var cmd = connection.CreateCommand();
                cmd.CommandText = @"
                    INSERT INTO snoop_buffer (target_username, line)
                    VALUES (LOWER(@username), @line)";
                cmd.Parameters.AddWithValue("@username", targetUsername);
                cmd.Parameters.AddWithValue("@line", line);
                cmd.ExecuteNonQuery();
            }
            catch { /* Snoop buffer writes are best-effort */ }
        }

        /// <summary>Prune old snoop buffer entries (older than 5 minutes).</summary>
        public void PruneSnoopBuffer()
        {
            try
            {
                using var connection = OpenConnection();
                using var cmd = connection.CreateCommand();
                cmd.CommandText = "DELETE FROM snoop_buffer WHERE created_at < datetime('now', '-5 minutes')";
                cmd.ExecuteNonQuery();
            }
            catch { /* Best-effort cleanup */ }
        }

        /// <summary>Expire admin commands older than 60 seconds that are still pending.</summary>
        public void ExpireStaleAdminCommands()
        {
            try
            {
                using var connection = OpenConnection();
                using var cmd = connection.CreateCommand();
                cmd.CommandText = @"
                    UPDATE admin_commands SET status = 'expired', result = 'Command expired (not picked up within 60s)',
                    executed_at = datetime('now')
                    WHERE status = 'pending' AND created_at < datetime('now', '-60 seconds')";
                cmd.ExecuteNonQuery();
            }
            catch { /* Best-effort cleanup */ }
        }

        // =====================================================================
        // Sleeping Player Vulnerability System
        // =====================================================================

        public async Task RegisterSleepingPlayer(string username, string location, string guardsJson, int innBoost)
        {
            try
            {
                using var connection = OpenConnection();
                using var cmd = connection.CreateCommand();
                cmd.CommandText = @"
                    INSERT OR REPLACE INTO sleeping_players (username, sleep_location, sleeping_since, is_dead, guards, inn_defense_boost, attack_log)
                    VALUES (LOWER(@username), @location, datetime('now'), 0, @guards, @innBoost, '[]');
                ";
                cmd.Parameters.AddWithValue("@username", username);
                cmd.Parameters.AddWithValue("@location", location);
                cmd.Parameters.AddWithValue("@guards", guardsJson);
                cmd.Parameters.AddWithValue("@innBoost", innBoost);
                await Task.Run(() => cmd.ExecuteNonQuery());
            }
            catch (Exception ex)
            {
                DebugLogger.Instance.LogError("SQL", $"Failed to register sleeping player {username}: {ex.Message}");
            }
        }

        public async Task UnregisterSleepingPlayer(string username)
        {
            try
            {
                using var connection = OpenConnection();
                using var cmd = connection.CreateCommand();
                cmd.CommandText = "DELETE FROM sleeping_players WHERE LOWER(username) = LOWER(@username);";
                cmd.Parameters.AddWithValue("@username", username);
                await Task.Run(() => cmd.ExecuteNonQuery());
            }
            catch (Exception ex)
            {
                DebugLogger.Instance.LogError("SQL", $"Failed to unregister sleeping player {username}: {ex.Message}");
            }
        }

        public async Task<List<SleepingPlayerInfo>> GetSleepingPlayers()
        {
            var sleepers = new List<SleepingPlayerInfo>();
            try
            {
                using var connection = OpenConnection();
                using var cmd = connection.CreateCommand();
                cmd.CommandText = "SELECT username, sleep_location, sleeping_since, is_dead, guards, inn_defense_boost, attack_log FROM sleeping_players WHERE is_dead = 0;";
                using var reader = await Task.Run(() => cmd.ExecuteReader());
                while (reader.Read())
                {
                    sleepers.Add(new SleepingPlayerInfo
                    {
                        Username = reader.GetString(0),
                        SleepLocation = reader.GetString(1),
                        SleepingSince = reader.IsDBNull(2) ? null : reader.GetString(2),
                        IsDead = reader.GetInt32(3) != 0,
                        GuardsJson = reader.IsDBNull(4) ? "[]" : reader.GetString(4),
                        InnDefenseBoost = reader.GetInt32(5) != 0,
                        AttackLogJson = reader.IsDBNull(6) ? "[]" : reader.GetString(6)
                    });
                }
            }
            catch (Exception ex)
            {
                DebugLogger.Instance.LogError("SQL", $"Failed to get sleeping players: {ex.Message}");
            }
            return sleepers;
        }

        public async Task<SleepingPlayerInfo?> GetSleepingPlayerInfo(string username)
        {
            try
            {
                using var connection = OpenConnection();
                using var cmd = connection.CreateCommand();
                cmd.CommandText = "SELECT username, sleep_location, sleeping_since, is_dead, guards, inn_defense_boost, attack_log FROM sleeping_players WHERE LOWER(username) = LOWER(@username);";
                cmd.Parameters.AddWithValue("@username", username);
                using var reader = await Task.Run(() => cmd.ExecuteReader());
                if (reader.Read())
                {
                    return new SleepingPlayerInfo
                    {
                        Username = reader.GetString(0),
                        SleepLocation = reader.GetString(1),
                        SleepingSince = reader.IsDBNull(2) ? null : reader.GetString(2),
                        IsDead = reader.GetInt32(3) != 0,
                        GuardsJson = reader.IsDBNull(4) ? "[]" : reader.GetString(4),
                        InnDefenseBoost = reader.GetInt32(5) != 0,
                        AttackLogJson = reader.IsDBNull(6) ? "[]" : reader.GetString(6)
                    };
                }
            }
            catch (Exception ex)
            {
                DebugLogger.Instance.LogError("SQL", $"Failed to get sleeping player info for {username}: {ex.Message}");
            }
            return null;
        }

        public async Task MarkSleepingPlayerDead(string username)
        {
            try
            {
                using var connection = OpenConnection();
                using var cmd = connection.CreateCommand();
                cmd.CommandText = "UPDATE sleeping_players SET is_dead = 1 WHERE LOWER(username) = LOWER(@username);";
                cmd.Parameters.AddWithValue("@username", username);
                await Task.Run(() => cmd.ExecuteNonQuery());
            }
            catch (Exception ex)
            {
                DebugLogger.Instance.LogError("SQL", $"Failed to mark sleeping player dead {username}: {ex.Message}");
            }
        }

        public async Task AppendSleepAttackLog(string username, string jsonEntry)
        {
            try
            {
                using var connection = OpenConnection();
                using var cmd = connection.CreateCommand();
                cmd.CommandText = @"
                    UPDATE sleeping_players
                    SET attack_log = json_insert(attack_log, '$[#]', json(@entry))
                    WHERE LOWER(username) = LOWER(@username);
                ";
                cmd.Parameters.AddWithValue("@username", username);
                cmd.Parameters.AddWithValue("@entry", jsonEntry);
                await Task.Run(() => cmd.ExecuteNonQuery());
            }
            catch (Exception ex)
            {
                DebugLogger.Instance.LogError("SQL", $"Failed to append sleep attack log for {username}: {ex.Message}");
            }
        }

        public async Task UpdateSleeperGuards(string username, string guardsJson)
        {
            try
            {
                using var connection = OpenConnection();
                using var cmd = connection.CreateCommand();
                cmd.CommandText = "UPDATE sleeping_players SET guards = @guards WHERE LOWER(username) = LOWER(@username);";
                cmd.Parameters.AddWithValue("@username", username);
                cmd.Parameters.AddWithValue("@guards", guardsJson);
                await Task.Run(() => cmd.ExecuteNonQuery());
            }
            catch (Exception ex)
            {
                DebugLogger.Instance.LogError("SQL", $"Failed to update sleeper guards for {username}: {ex.Message}");
            }
        }

    } // end class SqlSaveBackend

    public class SleepingPlayerInfo
    {
        public string Username { get; set; } = "";
        public string SleepLocation { get; set; } = "dormitory";
        public string? SleepingSince { get; set; }
        public bool IsDead { get; set; }
        public string GuardsJson { get; set; } = "[]";
        public bool InnDefenseBoost { get; set; }
        public string AttackLogJson { get; set; } = "[]";
    }

    public class WizardLogEntry
    {
        public string WizardName { get; set; } = "";
        public string Action { get; set; } = "";
        public string? Target { get; set; }
        public string? Details { get; set; }
        public string CreatedAt { get; set; } = "";
    }

    public class AdminPlayerInfo
    {
        public string Username { get; set; } = "";
        public string DisplayName { get; set; } = "";
        public bool IsBanned { get; set; }
        public string? BanReason { get; set; }
        public string? LastLogin { get; set; }
        public string? CreatedAt { get; set; }
        public int TotalPlaytimeMinutes { get; set; }
        public int Level { get; set; }
        public int ClassId { get; set; }
        public long Gold { get; set; }
        public long Experience { get; set; }
        public bool IsOnline { get; set; }
    }

    public class SysOpGameStats
    {
        // Player counts
        public int TotalPlayers { get; set; }
        public int ActivePlayers { get; set; }
        public int OnlinePlayers { get; set; }
        public int BannedPlayers { get; set; }

        // Player stats
        public int HighestLevel { get; set; }
        public double AverageLevel { get; set; }
        public string TopPlayerName { get; set; } = "";
        public int TopPlayerLevel { get; set; }
        public int TopPlayerClassId { get; set; }
        public int MostPopularClassId { get; set; } = -1;
        public int MostPopularClassCount { get; set; }
        public string NewestPlayerName { get; set; } = "";
        public string? NewestPlayerDate { get; set; }

        // Economy
        public long TotalGoldOnHand { get; set; }
        public long TotalBankGold { get; set; }
        public long TotalGoldEarned { get; set; }
        public long TotalGoldSpent { get; set; }
        public long TotalItemsBought { get; set; }
        public long TotalItemsSold { get; set; }

        // Combat
        public long TotalMonstersKilled { get; set; }
        public long TotalBossesKilled { get; set; }
        public long TotalPvPKills { get; set; }
        public long TotalPvEDeaths { get; set; }
        public long TotalDamageDealt { get; set; }
        public int TotalPvPFights { get; set; }
        public int DeepestDungeon { get; set; }

        // World
        public int ActiveTeams { get; set; }
        public int ActiveBounties { get; set; }
        public int ActiveAuctions { get; set; }

        // Server
        public int NewsEntries { get; set; }
        public int TotalMessages { get; set; }
        public long TotalPlaytimeMinutes { get; set; }
        public long DatabaseSizeBytes { get; set; }
    }

    /// <summary>Represents a pending admin command from the web dashboard.</summary>
    public class AdminCommand
    {
        public int Id { get; set; }
        public string Command { get; set; } = "";
        public string? TargetUsername { get; set; }
        public string? Args { get; set; }
    }
}
