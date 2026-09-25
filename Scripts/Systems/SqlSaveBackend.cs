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
    public partial class SqlSaveBackend : IOnlineSaveBackend
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

        /// <summary>v1.1.11: the database file, for a system that opens its own connection (guild succession).</summary>
        public string DatabasePath => databasePath;

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
            catch (ObjectDisposedException)
            {
                // The SQLite connection was disposed mid-drain (server shutting down, or the
                // connection was closed under us). Benign shutdown race -- don't page the
                // server-monitor Discord for it.
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
        /// <summary>
        /// v1.1.8: the indexes that keep the website's statistics page off the player blob.
        ///
        /// player_data averages 84 KB, and the stats page asks for five to seven fields per row
        /// across about seven full passes. On the live server with 410 players one rebuild stalled
        /// the whole process for up to 83 seconds; the web service is single threaded and rebuilds
        /// on a timer, so the news feed, the API and the browser terminal froze for most of every
        /// two minutes whether or not anyone was visiting. A profile of that stall put 62 percent
        /// of it in SQLite's jsonTranslateTextToBlob, which is what re-translating each row's JSON
        /// per json_extract call would look like; the same SQL in another SQLite build is fast, and
        /// that difference is recorded as unexplained rather than guessed at. With these
        /// indexes the same aggregate answers from the index alone in 0.3 ms, measured through the
        /// web service's own SQLite. The worst stall fell from 83.3 s to 7.4 s and the service went
        /// from burning 68 percent of a core continuously to under 4 percent.
        ///
        /// These are the one definition of each. The migration rebuilds any index whose stored
        /// definition differs, so a server carrying an older or hand-made version converges on
        /// exactly what a fresh database gets.
        /// </summary>
        private static readonly (string Name, string Ddl)[] PlayerIndexDefinitions =
        {
            ("idx_players_stats_cover", "CREATE INDEX idx_players_stats_cover ON players(is_banned, username, json_extract(player_data,'$.player.level'), json_extract(player_data,'$.player.gold'), json_extract(player_data,'$.player.bankGold'), json_extract(player_data,'$.player.statistics.totalMonstersKilled'), json_extract(player_data,'$.player.statistics.deepestDungeonLevel'), json_extract(player_data,'$.player.class'))"),
            ("idx_players_immortal", "CREATE INDEX idx_players_immortal ON players(json_extract(player_data,'$.player.isImmortal'))"),
            ("idx_players_murder_weight", "CREATE INDEX idx_players_murder_weight ON players(json_extract(player_data,'$.player.murderWeight'))"),
            ("idx_players_worshipped_god", "CREATE INDEX idx_players_worshipped_god ON players(json_extract(player_data,'$.player.worshippedGod'))"),
            ("idx_players_level", "CREATE INDEX idx_players_level ON players(json_extract(player_data,'$.player.level') DESC)"),
            ("idx_players_class", "CREATE INDEX idx_players_class ON players(json_extract(player_data,'$.player.class'))"),
            ("idx_players_xp", "CREATE INDEX idx_players_xp ON players(json_extract(player_data,'$.player.experience') DESC)"),
        };

        private static string NormalizeDdl(string? sql) =>
            System.Text.RegularExpressions.Regex.Replace(sql ?? "", @"\s+", " ").Replace("IF NOT EXISTS ", "").Trim();

        /// <summary>
        /// Create each player index, and rebuild any whose stored definition does not match the one
        /// above, so that an upgraded server and a fresh one carry byte-identical definitions,
        /// whether the difference is the older double-quoted JSON path or only the layout of a
        /// definition someone typed by hand.
        ///
        /// On the spelling: earlier releases wrote these paths in double quotes, and this one uses
        /// single quotes. That is for consistency, not correction. Measured on the live database,
        /// both SQLite builds in play choose a double-quoted index from a single-quoted query:
        /// the game's 3.41.2 and the website's 3.49.2. The rebuild exists so that every server
        /// ends up with one definition, not because the old one failed.
        /// Failures are logged and skipped rather than blocking startup.
        /// </summary>
        private void EnsurePlayerIndexes(SqliteConnection connection)
        {
            foreach (var (name, ddl) in PlayerIndexDefinitions)
            {
                try
                {
                    string? existing;
                    using (var read = connection.CreateCommand())
                    {
                        read.CommandText = "SELECT sql FROM sqlite_master WHERE type='index' AND name=@n;";
                        read.Parameters.AddWithValue("@n", name);
                        existing = read.ExecuteScalar() as string;
                    }
                    if (existing != null && NormalizeDdl(existing) == NormalizeDdl(ddl)) continue;
                    if (existing != null)
                    {
                        using var drop = connection.CreateCommand();
                        drop.CommandText = $"DROP INDEX IF EXISTS \"{name}\";";
                        drop.ExecuteNonQuery();
                        DebugLogger.Instance.LogInfo("SQL", $"Rebuilding player index {name}: its definition differed from this release");
                    }
                    using var create = connection.CreateCommand();
                    create.CommandText = ddl + ";";
                    create.ExecuteNonQuery();
                }
                catch (Exception ex)
                {
                    DebugLogger.Instance.LogWarning("SQL", $"Player index {name} not ensured: {ex.Message}");
                }
            }
        }

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
                        controls_turf INTEGER DEFAULT 0,
                        last_join_at TEXT
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

                    -- v0.65.8 (R5) Fallen Legacy: durable memorial + heirloom for
                    -- involuntary permadeaths. Rows are never pruned (the memorial
                    -- IS the point); at ~3 deaths/week this grows ~150 rows/year.
                    CREATE TABLE IF NOT EXISTS fallen_legacy (
                        id INTEGER PRIMARY KEY AUTOINCREMENT,
                        username TEXT NOT NULL,
                        display_name TEXT NOT NULL,
                        level INTEGER NOT NULL,
                        class_name TEXT NOT NULL DEFAULT '',
                        killer TEXT NOT NULL DEFAULT '',
                        heirloom_gold INTEGER NOT NULL DEFAULT 0,
                        claimed INTEGER NOT NULL DEFAULT 0,
                        died_at TEXT DEFAULT (datetime('now'))
                    );
                    CREATE INDEX IF NOT EXISTS idx_fallen_legacy_username
                        ON fallen_legacy(LOWER(username), claimed);
                    CREATE INDEX IF NOT EXISTS idx_fallen_legacy_died
                        ON fallen_legacy(died_at DESC);

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

                    -- The players indexes are ensured by EnsurePlayerIndexes below, which also
                    -- rebuilds any that an older release defined differently.

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

                    -- v1.1.13: a web delete made while the MUD was down queues its world purge here;
                    -- the MUD runs it once its world is loaded. mud_heartbeat is the admin poller's beat.
                    CREATE TABLE IF NOT EXISTS pending_purges (
                        id INTEGER PRIMARY KEY AUTOINCREMENT,
                        username TEXT NOT NULL,
                        name2 TEXT,
                        display_name TEXT,
                        deleted_at TEXT DEFAULT (datetime('now')),
                        created_by TEXT DEFAULT 'admin-web',
                        player_id TEXT,
                        untimed INTEGER
                    );
                    CREATE TABLE IF NOT EXISTS mud_heartbeat (
                        id INTEGER PRIMARY KEY CHECK (id = 1),
                        beat_at TEXT NOT NULL
                    );

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

                    -- v1.1.11: one row per bounty paid, so a bounty is paid once across processes.
                    -- v1.1.11: never pruned (a stale save can bring an old bounty back); one small row per bounty paid.
                    CREATE TABLE IF NOT EXISTS bounty_claims (
                        quest_id TEXT PRIMARY KEY,
                        claimed_by TEXT,
                        claimed_at TEXT DEFAULT (datetime('now'))
                    );

                    -- v1.1.13: idempotent edits of the shared world records (a deleted character's grudges,
                    -- marriages, throne) that the owner process re-applies after stale writes. See WorldEditLog.
                    CREATE TABLE IF NOT EXISTS world_edits (id INTEGER PRIMARY KEY AUTOINCREMENT, kind TEXT NOT NULL, payload TEXT NOT NULL, created_at TEXT DEFAULT (datetime('now')), created_by TEXT, applied_at TEXT, applied_by TEXT);
                    CREATE INDEX IF NOT EXISTS idx_world_edits_created ON world_edits(created_at);
                ";
                cmd.ExecuteNonQuery();
            }

            EnsurePlayerIndexes(connection);

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

            // v1.1.11: when a player last joined the team; the empty-team cleanup leaves a team alone for a
            // while after a join, in every process (a join and the cleanup can run in different processes)
            try
            {
                using var migCmd = connection.CreateCommand();
                migCmd.CommandText = "ALTER TABLE player_teams ADD COLUMN last_join_at TEXT;";
                migCmd.ExecuteNonQuery();
            }
            catch { /* Column already exists - expected */ }

            // v1.1.13: a queued purge's character ID, and whether another player used the name at the delete
            foreach (var column in new[] { "player_id TEXT", "untimed INTEGER" })
            {
                try
                {
                    using var migCmd = connection.CreateCommand();
                    migCmd.CommandText = $"ALTER TABLE pending_purges ADD COLUMN {column};";
                    migCmd.ExecuteNonQuery();
                }
                catch { /* Column already exists - expected */ }
            }

            // v1.1.12: who paid a team war's wager, so a war left active by a lost session can be refunded
            try
            {
                using var migCmd = connection.CreateCommand();
                migCmd.CommandText = "ALTER TABLE team_wars ADD COLUMN challenger_key TEXT;";
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

            // v1.1.11: the one-time bounty claim table, also on a database made by an older release
            try
            {
                using var migCmd = connection.CreateCommand();
                migCmd.CommandText = "CREATE TABLE IF NOT EXISTS bounty_claims (quest_id TEXT PRIMARY KEY, claimed_by TEXT, claimed_at TEXT DEFAULT (datetime('now')));";
                migCmd.ExecuteNonQuery();
            }
            catch (Exception ex) { DebugLogger.Instance.LogWarning("SQL", $"bounty_claims not ensured: {ex.Message}"); }

            // v1.1.13: the world edits log, also on a database made by an older release
            try
            {
                using var migCmd = connection.CreateCommand();
                migCmd.CommandText = "CREATE TABLE IF NOT EXISTS world_edits (id INTEGER PRIMARY KEY AUTOINCREMENT, kind TEXT NOT NULL, payload TEXT NOT NULL, created_at TEXT DEFAULT (datetime('now')), created_by TEXT, applied_at TEXT, applied_by TEXT); CREATE INDEX IF NOT EXISTS idx_world_edits_created ON world_edits(created_at);";
                migCmd.ExecuteNonQuery();
            }
            catch (Exception ex) { DebugLogger.Instance.LogWarning("SQL", $"world_edits not ensured: {ex.Message}"); }

            MigrateWorldBossTables(connection); // v1.1.4

            DebugLogger.Instance.LogInfo("SQL", $"Database initialized at {databasePath}");
        }

        private SqliteConnection OpenConnection()
        {
            var connection = new SqliteConnection(connectionString);
            connection.Open();
            // v1.1.12: SQLite LOWER() folds ASCII only; ulower folds case as C# does (the team name guards)
            connection.CreateFunction("ulower", (string? s) => s?.ToLowerInvariant());
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
                // v0.65.1: compose the DB display_name with the optional family surname
                // (chosen at marriage) so the leaderboard / character-select show the
                // married name, matching the in-game Character.DisplayName. Name2 stays
                // the identity key; this column is cosmetic.
                var baseDisplayName = data.Player?.Name2 ?? data.Player?.Name1 ?? playerName;
                var displayName = string.IsNullOrEmpty(data.Player?.FamilySurname)
                    ? baseDisplayName
                    : $"{baseDisplayName} {data.Player!.FamilySurname}";
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

        /// <summary>
        /// v1.1.12: the save's Name2 for one key, banned accounts included (ReadGameData skips them), for the
        /// admin deletes: a married display name is not the name children and quests record. Null if none.
        /// </summary>
        /// <summary>v1.1.13: the character ID in the account's save (null when none), read before the row is emptied.</summary>
        public string? GetStoredCharacterId(string username)
        {
            try
            {
                using var connection = OpenConnection();
                using var cmd = connection.CreateCommand();
                cmd.CommandText = "SELECT CASE WHEN json_valid(player_data) THEN json_extract(player_data, '$.player.id') END FROM players " +
                                  "WHERE LOWER(username) = LOWER(@u) ORDER BY LENGTH(player_data) DESC LIMIT 1;";
                cmd.Parameters.AddWithValue("@u", username);
                return cmd.ExecuteScalar() is string s && !string.IsNullOrWhiteSpace(s) ? s : null;
            }
            catch (Exception ex)
            {
                DebugLogger.Instance.LogWarning("SQL", $"GetStoredCharacterId('{username}') failed: {ex.Message}");
                return null;
            }
        }

        public string? GetStoredName2(string username)
        {
            try
            {
                using var connection = OpenConnection();
                using var cmd = connection.CreateCommand();
                cmd.CommandText = "SELECT CASE WHEN json_valid(player_data) THEN json_extract(player_data, '$.player.name2') END FROM players " +
                                  "WHERE LOWER(username) = LOWER(@u) ORDER BY LENGTH(player_data) DESC LIMIT 1;";
                cmd.Parameters.AddWithValue("@u", username);
                return cmd.ExecuteScalar() is string s && !string.IsNullOrWhiteSpace(s) ? s : null;
            }
            catch (Exception ex)
            {
                DebugLogger.Instance.LogWarning("SQL", $"GetStoredName2('{username}') failed: {ex.Message}");
                return null;
            }
        }

        public async Task<SaveGameData?> ReadGameData(string playerName)
        {
            try
            {
                using var connection = OpenConnection();
                using var cmd = connection.CreateCommand();
                // ORDER BY LENGTH DESC to prefer the record with actual save data over empty '{}' registration records
                cmd.CommandText = "SELECT player_data FROM players WHERE LOWER(username) = LOWER(@username) AND is_banned = 0 ORDER BY (username = LOWER(@username)) DESC, LENGTH(player_data) DESC LIMIT 1;";
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
                    -- v1.1.12: the save's keys are camelCase; '$.Player.Team' never matched, so the sleeper's
                    -- team was always empty and their NPC teammates could attack them
                    SELECT CASE WHEN json_valid(player_data) THEN json_extract(player_data, '$.player.team') END
                    FROM players
                    WHERE LOWER(username) = LOWER(@username) AND is_banned = 0
                    ORDER BY (username = LOWER(@username)) DESC, LENGTH(player_data) DESC LIMIT 1;";
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
                // v1.1.12: mail to the key is kept when another character goes by that name (account "bob" playing
                // "Alice" beside a character "Bob"), as the alias clause below does
                // v1.1.13: the same for mail from the key, which may be another character's sent mail
                ExecPurge(connection, tx, "messages",          "(LOWER(from_player) = LOWER(@u) " +
                    "AND NOT EXISTS (SELECT 1 FROM players p WHERE LOWER(p.username) != LOWER(@u) AND (LOWER(p.display_name) = LOWER(messages.from_player) " +
                    "OR LOWER(CASE WHEN json_valid(p.player_data) THEN json_extract(p.player_data, '$.player.name2') END) = LOWER(messages.from_player)))) " +
                    "OR (LOWER(to_player) = LOWER(@u) " +
                    "AND NOT EXISTS (SELECT 1 FROM players p WHERE LOWER(p.username) != LOWER(@u) AND (LOWER(p.display_name) = LOWER(messages.to_player) " +
                    "OR LOWER(CASE WHEN json_valid(p.player_data) THEN json_extract(p.player_data, '$.player.name2') END) = LOWER(messages.to_player))))", username);
                ExecPurge(connection, tx, "trade_offers",      "LOWER(from_player) = LOWER(@u) OR LOWER(to_player) = LOWER(@u)", username);
                ExecPurge(connection, tx, "bounties",          "LOWER(target_player) = LOWER(@u) OR LOWER(placed_by) = LOWER(@u) OR LOWER(claimed_by) = LOWER(@u)", username);
                // v1.1.11: only the character's own listings, under each name it may have listed as (the key,
                // the display name, the stored married display name). The buyer clause is gone: a sold row the
                // seller has not collected holds that seller's gold, and the buyer already has the item. A
                // seller name another player or an NPC may carry now is left alone, alias by alias.
                string? storedDisplayName = null;
                using (var dn = connection.CreateCommand())
                {
                    dn.Transaction = tx;
                    dn.CommandText = "SELECT display_name FROM players WHERE LOWER(username) = LOWER(@u) AND display_name IS NOT NULL LIMIT 1;";
                    dn.Parameters.AddWithValue("@u", username);
                    storedDisplayName = dn.ExecuteScalar() as string;
                }
                foreach (var alias in AuctionSellerAliases(username, displayName, storedDisplayName))
                {
                    if (CouldBeNpcName(alias)) continue;
                    ExecPurge(connection, tx, "auction_listings",
                        $"LOWER(seller) = LOWER(@d) AND {SellerNotOtherPlayer}", username, alias);
                }
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

                // v1.1.11: queued deliveries keyed by the character key. A new character on the same
                // key used to collect the deleted one's inheritance, bank wires and boss rewards.
                ExecPurge(connection, tx, "pending_inheritance",    "LOWER(player_username) = LOWER(@u)", username);
                ExecPurge(connection, tx, "pending_gold_transfers", "LOWER(recipient_username) = LOWER(@u)", username);
                ExecPurge(connection, tx, "world_boss_rewards",     "LOWER(player_name) = LOWER(@u) AND COALESCE(delivered, 0) = 0", username);
                // v1.1.12: an unfinished war's refund would otherwise be queued later under this reused key
                using (var wars = connection.CreateCommand())
                {
                    wars.Transaction = tx;
                    wars.CommandText = "UPDATE team_wars SET challenger_key = NULL WHERE LOWER(challenger_key) = LOWER(@u) AND status = 'active';";
                    wars.Parameters.AddWithValue("@u", username);
                    wars.ExecuteNonQuery();
                }

                // v1.1.11: mail and auctions also key on the display name (mail to Name2, auction sellers
                // are DisplayName.ToLower(), the married surname form comes from players.display_name).
                // Only mail TO the character; a name that is another account's key is left alone.
                if (!string.IsNullOrWhiteSpace(displayName))
                {
                    const string ownNames = "(SELECT LOWER(display_name) FROM players WHERE LOWER(username) = LOWER(@u) AND display_name IS NOT NULL)";
                    ExecPurge(connection, tx, "messages",
                        $"to_player != '*' AND (LOWER(to_player) = LOWER(@d) OR LOWER(to_player) IN {ownNames}) " +
                        "AND NOT EXISTS (SELECT 1 FROM players p WHERE LOWER(p.username) = LOWER(messages.to_player) AND LOWER(p.username) != LOWER(@u)) " +
                        // v1.1.12: nor a name another character goes by (a married "Bob Smith" beside a player "Bob Smith")
                        "AND NOT EXISTS (SELECT 1 FROM players p WHERE LOWER(p.username) != LOWER(@u) AND (LOWER(p.display_name) = LOWER(messages.to_player) " +
                        "OR LOWER(CASE WHEN json_valid(p.player_data) THEN json_extract(p.player_data, '$.player.name2') END) = LOWER(messages.to_player)))",
                        username, displayName);
                }

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

        // v1.1.11: an auction seller name that another player's row carries now, as its display name or
        // its save's Name2, is that player's listing, never the deleted character's.
        private const string SellerNotOtherPlayer =
            "NOT EXISTS (SELECT 1 FROM players p WHERE LOWER(p.username) != LOWER(@u) AND (" +
            "LOWER(p.display_name) = LOWER(auction_listings.seller) OR " +
            "LOWER(CASE WHEN json_valid(p.player_data) THEN json_extract(p.player_data, '$.player.name2') END) = LOWER(auction_listings.seller)))";

        /// <summary>
        /// v1.1.11: every seller name the delete purge removes listings under: the key, the display name
        /// given, and the display name stored on the players row (the married form). Each is checked
        /// against the NPC guard by the caller; one list, so the guard and the DELETE cannot differ.
        /// </summary>
        internal static List<string> AuctionSellerAliases(string username, string? displayName, string? storedDisplayName) =>
            new[] { username, displayName, storedDisplayName }
                .Where(n => !string.IsNullOrWhiteSpace(n))
                .Select(n => n!)
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToList();

        // v1.1.11: a name an NPC may carry (one does, or the roster cannot rule it out)
        private static bool CouldBeNpcName(string name)
        {
            var spawner = NPCSpawnSystem.Instance;
            return spawner == null || !spawner.IsRosterTrustworthy || QuestSystem.IsNPCName(name);
        }

        /// <summary>
        /// v1.1.11: claims a bounty for payout, once across every process on this database. True only for the
        /// first claim of the quest id; false when another process (or an earlier claim) already took it, or
        /// the claim could not be written.
        /// </summary>
        public bool TryClaimBounty(string questId, string claimer) => TryClaimBountyOrFail(questId, claimer) == true;

        /// <summary>v1.1.11: as TryClaimBounty, but null when the claim could not be written, so the bounty stays open.</summary>
        public bool? TryClaimBountyOrFail(string questId, string claimer)
        {
            if (string.IsNullOrWhiteSpace(questId)) return false;
            try
            {
                using var connection = OpenConnection();
                using var cmd = connection.CreateCommand();
                cmd.CommandText = "INSERT OR IGNORE INTO bounty_claims (quest_id, claimed_by) VALUES (@q, @c);";
                cmd.Parameters.AddWithValue("@q", questId);
                cmd.Parameters.AddWithValue("@c", claimer ?? "");
                return cmd.ExecuteNonQuery() == 1;
            }
            catch (Exception ex)
            {
                DebugLogger.Instance.LogError("SQL", $"TryClaimBounty failed for '{questId}': {ex.Message}");
                return null;
            }
        }

        private static void ExecPurge(SqliteConnection conn, SqliteTransaction tx, string table, string whereClause, string username, string? displayName = null)
        {
            try
            {
                using var cmd = conn.CreateCommand();
                cmd.Transaction = tx;
                cmd.CommandText = $"DELETE FROM {table} WHERE {whereClause};";
                cmd.Parameters.AddWithValue("@u", username);
                if (displayName != null) cmd.Parameters.AddWithValue("@d", displayName);   // v1.1.11: display-name key
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

                using var restoreTx = connection.BeginTransaction(); // v1.1.1: restore and archive removal apply together
                using (var updateCmd = connection.CreateCommand())
                {
                    updateCmd.Transaction = restoreTx;
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
                    deleteCmd.Transaction = restoreTx;
                    deleteCmd.CommandText = "DELETE FROM deleted_characters WHERE id = @id;";
                    deleteCmd.Parameters.AddWithValue("@id", info.Id);
                    deleteCmd.ExecuteNonQuery();
                }
                restoreTx.Commit();

                // v1.1.1: permadeath marked the username erased so a late autosave could not
                // resurrect it. A restored character kept that mark, so every save it made
                // afterwards was silently refused for the rest of the server's uptime.
                ClearErasedMark(username);

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
                cmd.CommandText = "SELECT player_data FROM players WHERE LOWER(username) = LOWER(@username) AND is_banned = 0 ORDER BY (username = LOWER(@username)) DESC, LENGTH(player_data) DESC LIMIT 1;";
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

        public async Task SaveWorldState(string key, string jsonValue) => await TrySaveWorldState(key, jsonValue);

        /// <summary>v1.1.14: SaveWorldState that says whether the write landed (false: it failed and was logged).</summary>
        public async Task<bool> TrySaveWorldState(string key, string jsonValue) => await SaveWorldStateReturningVersion(key, jsonValue) != null;

        /// <summary>
        /// v1.1.14: SaveWorldState that returns the version the row now has, read in the same statement (RETURNING),
        /// so a writer learns the version of its own write, never a later writer's. Null when the write failed.
        /// </summary>
        public async Task<long?> SaveWorldStateReturningVersion(string key, string jsonValue)
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
                        updated_at = datetime('now')
                    RETURNING version;
                ";
                cmd.Parameters.AddWithValue("@key", key);
                cmd.Parameters.AddWithValue("@value", jsonValue);
                return Convert.ToInt64(await cmd.ExecuteScalarAsync());
            }
            catch (Exception ex)
            {
                DebugLogger.Instance.LogError("SQL", $"Failed to save world state '{key}': {ex.Message}");
                return null;
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
        /// <summary>v1.1.13: the number of entries in a world_state JSON array (0 when absent or not an array).</summary>
        public int GetWorldStateArrayLength(string key)
        {
            try
            {
                using var connection = OpenConnection();
                using var cmd = connection.CreateCommand();
                cmd.CommandText = "SELECT CASE WHEN json_valid(value) AND json_type(value) = 'array' THEN json_array_length(value) ELSE 0 END FROM world_state WHERE key = @key;";
                cmd.Parameters.AddWithValue("@key", key);
                var result = cmd.ExecuteScalar();
                return result != null && result != DBNull.Value ? Convert.ToInt32(result) : 0;
            }
            catch (Exception ex)
            {
                DebugLogger.Instance.LogWarning("SQL", $"GetWorldStateArrayLength('{key}') failed: {ex.Message}");
                return 0;
            }
        }

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

        // --- v1.1.13: World edits ---
        // Idempotent edits of the shared world records, appended by the process that makes them and
        // re-applied by the owner process (WorldEditLog). Times are SQLite datetime('now'), UTC.

        /// <summary>v1.1.13: append an edit; returns its id, or 0 when the write failed.</summary>
        public long AppendWorldEdit(string kind, string payloadJson, string createdBy)
        {
            try
            {
                using var connection = OpenConnection();
                using var cmd = connection.CreateCommand();
                cmd.CommandText = "INSERT INTO world_edits (kind, payload, created_by) VALUES (@k, @p, @b); SELECT last_insert_rowid();";
                cmd.Parameters.AddWithValue("@k", kind);
                cmd.Parameters.AddWithValue("@p", payloadJson);
                cmd.Parameters.AddWithValue("@b", createdBy ?? "");
                return Convert.ToInt64(cmd.ExecuteScalar());
            }
            catch (Exception ex)
            {
                DebugLogger.Instance.LogError("SQL", $"AppendWorldEdit('{kind}') failed: {ex.Message}");
                return 0;
            }
        }

        /// <summary>
        /// v1.1.13: the edits the owner applies: every edit not yet applied, whatever its age, and every
        /// edit made in the last reapplyHours (applied or not), oldest first.
        /// </summary>
        public List<WorldEdit> GetWorldEditsToApply(int reapplyHours = 24) =>
            QueryWorldEdits("applied_at IS NULL OR created_at >= datetime('now', @h)", $"-{reapplyHours} hours");

        /// <summary>v1.1.13: edits never applied that are older than hours (for the warning line).</summary>
        public List<WorldEdit> GetUnappliedWorldEditsOlderThan(int hours) =>
            QueryWorldEdits("applied_at IS NULL AND created_at < datetime('now', @h)", $"-{hours} hours");

        private List<WorldEdit> QueryWorldEdits(string where, string span)
        {
            var list = new List<WorldEdit>();
            try
            {
                using var connection = OpenConnection();
                using var cmd = connection.CreateCommand();
                cmd.CommandText = $"SELECT id, kind, payload, created_at, created_by, applied_at, applied_by FROM world_edits WHERE {where} ORDER BY id;";
                cmd.Parameters.AddWithValue("@h", span);
                using var r = cmd.ExecuteReader();
                while (r.Read())
                    list.Add(new WorldEdit
                    {
                        Id = r.GetInt64(0), Kind = r.GetString(1), Payload = r.GetString(2),
                        CreatedAt = r.IsDBNull(3) ? "" : r.GetString(3), CreatedBy = r.IsDBNull(4) ? "" : r.GetString(4),
                        AppliedAt = r.IsDBNull(5) ? null : r.GetString(5), AppliedBy = r.IsDBNull(6) ? null : r.GetString(6)
                    });
            }
            catch (Exception ex) { DebugLogger.Instance.LogError("SQL", $"World edits read failed: {ex.Message}"); }
            return list;
        }

        /// <summary>v1.1.13: mark edits applied; an edit already marked keeps its first mark. Returns the rows marked.</summary>
        public int MarkWorldEditsApplied(IEnumerable<long> ids, string appliedBy)
        {
            int marked = 0;
            try
            {
                using var connection = OpenConnection();
                foreach (var id in ids)
                {
                    using var cmd = connection.CreateCommand();
                    cmd.CommandText = "UPDATE world_edits SET applied_at = datetime('now'), applied_by = @b WHERE id = @id AND applied_at IS NULL;";
                    cmd.Parameters.AddWithValue("@id", id);
                    cmd.Parameters.AddWithValue("@b", appliedBy ?? "");
                    marked += cmd.ExecuteNonQuery();
                }
            }
            catch (Exception ex) { DebugLogger.Instance.LogError("SQL", $"MarkWorldEditsApplied failed: {ex.Message}"); }
            return marked;
        }

        /// <summary>v1.1.13: delete edits applied more than days ago. An edit never applied is never deleted here.</summary>
        public int PruneAppliedWorldEdits(int days = 7)
        {
            try
            {
                using var connection = OpenConnection();
                using var cmd = connection.CreateCommand();
                cmd.CommandText = "DELETE FROM world_edits WHERE applied_at IS NOT NULL AND applied_at < datetime('now', @d);";
                cmd.Parameters.AddWithValue("@d", $"-{days} days");
                return cmd.ExecuteNonQuery();
            }
            catch (Exception ex)
            {
                DebugLogger.Instance.LogError("SQL", $"PruneAppliedWorldEdits failed: {ex.Message}");
                return 0;
            }
        }

        /// <summary>
        /// v1.1.13: a character that came after the edit uses one of the names: a player row with a save
        /// under the name (display name or Name2) created after the edit, saved after it, or on the deleted
        /// character's own account (a same-account recreation keeps the account's created_at).
        /// A failed read counts as a later character, so nothing untimed is re-applied on a doubt.
        /// </summary>
        public bool LaterCharacterUsesName(IEnumerable<string> names, string editCreatedAt, string? characterKey)
        {
            try
            {
                using var connection = OpenConnection();
                foreach (var name in names)
                {
                    if (string.IsNullOrWhiteSpace(name)) continue;
                    using var cmd = connection.CreateCommand();
                    cmd.CommandText = "SELECT EXISTS (SELECT 1 FROM players WHERE player_data IS NOT NULL AND length(player_data) > 4 " +
                        "AND (LOWER(display_name) = LOWER(@n) OR " +
                        "LOWER(CASE WHEN json_valid(player_data) THEN json_extract(player_data, '$.player.name2') END) = LOWER(@n)) " +
                        "AND (created_at > @t OR last_login > @t OR LOWER(username) = LOWER(@k)));";
                    cmd.Parameters.AddWithValue("@n", name);
                    cmd.Parameters.AddWithValue("@t", editCreatedAt ?? "");
                    cmd.Parameters.AddWithValue("@k", characterKey ?? "");
                    if (Convert.ToInt64(cmd.ExecuteScalar()) != 0) return true;
                }
                return false;
            }
            catch (Exception ex)
            {
                DebugLogger.Instance.LogWarning("SQL", $"LaterCharacterUsesName failed: {ex.Message}");
                return true;
            }
        }

        /// <summary>v1.1.13: the owner id in the world sim lock, or null when none is held.</summary>
        public string? WorldSimLockOwner()
        {
            try
            {
                using var connection = OpenConnection();
                using var cmd = connection.CreateCommand();
                cmd.CommandText = "SELECT value FROM world_state WHERE key = @key;";
                cmd.Parameters.AddWithValue("@key", WORLDSIM_LOCK_KEY);
                if (cmd.ExecuteScalar() is not string json || string.IsNullOrEmpty(json)) return null;
                using var doc = System.Text.Json.JsonDocument.Parse(json);
                return doc.RootElement.TryGetProperty("owner", out var o) ? o.GetString() : null;
            }
            catch { return null; }
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
        /// v1.1.14: a compare-and-swap, the same test as TryAcquireWorldSimLock in one transaction: the beat is
        /// written only when the lock is free, stale, or already this owner's. It overwrote the lock
        /// unconditionally, so two world sims beating on one database passed it back and forth and
        /// IsOwnerProcess flipped between them. Returns false when another process holds the lock.
        /// </summary>
        public bool UpdateWorldSimHeartbeat(string ownerId) => TryAcquireWorldSimLock(ownerId);

        /// <summary>
        /// v1.1.14: the lock taken whoever holds it, for the processes that own the shared records by design
        /// (the MUD server and the standalone world sim) at their start. A door's embedded world sim that held
        /// it then fails its next heartbeat and stops claiming it.
        /// </summary>
        public void TakeOverWorldSimLock(string ownerId)
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
                    INSERT INTO world_state (key, value, version, updated_at)
                    VALUES (@key, @value, 1, datetime('now'))
                    ON CONFLICT(key) DO UPDATE SET
                        value = @value,
                        version = version + 1,
                        updated_at = datetime('now');
                ";
                cmd.Parameters.AddWithValue("@key", WORLDSIM_LOCK_KEY);
                cmd.Parameters.AddWithValue("@value", lockJson);
                cmd.ExecuteNonQuery();
            }
            catch (Exception ex)
            {
                DebugLogger.Instance.LogError("SQL", $"Failed to take over the worldsim lock: {ex.Message}");
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

        /// <summary>
        /// Prune combat_events. v0.65.3: deaths are rare (~0.3% of combats) but the most important
        /// row for balance analysis, and the old prune (7 days / 1000 rows, row cap applied to ALL
        /// outcomes) let the flood of victories evict deaths almost immediately -- the live table
        /// showed 3 deaths when saves recorded 63. Fix: the row cap now applies ONLY to non-death
        /// outcomes (victory/fled), so deaths are never crowded out; deaths are kept for a long
        /// window (deathDaysToKeep) and non-deaths for a shorter one. Net table size stays tiny.
        /// </summary>
        public async Task PruneCombatEvents(int daysToKeep = 14, int maxRows = 5000, int deathDaysToKeep = 90)
        {
            try
            {
                using var conn = new SqliteConnection(connectionString);
                await conn.OpenAsync();

                // Age out non-death events on the short window.
                using (var cmd = conn.CreateCommand())
                {
                    cmd.CommandText = "DELETE FROM combat_events WHERE outcome != 'death' AND created_at < datetime('now', '-' || @days || ' days');";
                    cmd.Parameters.AddWithValue("@days", daysToKeep);
                    await cmd.ExecuteNonQueryAsync();
                }

                // Age out deaths only on the much longer window (they're rare + high-value).
                using (var cmd = conn.CreateCommand())
                {
                    cmd.CommandText = "DELETE FROM combat_events WHERE outcome = 'death' AND created_at < datetime('now', '-' || @days || ' days');";
                    cmd.Parameters.AddWithValue("@days", deathDaysToKeep);
                    await cmd.ExecuteNonQueryAsync();
                }

                // Row cap applies ONLY to non-death outcomes so a flood of victories can never
                // evict the rare death rows (the bug that made the table lie about deaths).
                using (var cmd = conn.CreateCommand())
                {
                    cmd.CommandText = @"
                        DELETE FROM combat_events WHERE outcome != 'death' AND id NOT IN (
                            SELECT id FROM combat_events WHERE outcome != 'death' ORDER BY created_at DESC LIMIT @maxRows
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

                // v0.65.1 (B1a fix): account_created fires inside RegisterPlayer, before
                // PlayerSession builds SessionContext.Current, so its connection_type writes
                // NULL -- the Web-vs-Steam funnel split was blank for every cohort. Every
                // LATER milestone (character_created/reached_town/...) carries the real type,
                // so backfill the still-blank account_created row from the first such event.
                // Same session for a new account, so this is the correct attribution.
                if (!string.IsNullOrWhiteSpace(connectionType) && eventName != "account_created")
                {
                    using var fill = connection.CreateCommand();
                    fill.CommandText = @"
                        UPDATE onboarding_events SET connection_type = @ctype
                        WHERE username = @username AND event = 'account_created'
                          AND (connection_type IS NULL OR connection_type = '');";
                    fill.Parameters.AddWithValue("@username", username.ToLowerInvariant());
                    fill.Parameters.AddWithValue("@ctype", connectionType);
                    fill.ExecuteNonQuery();
                }
            }
            catch (Exception ex)
            {
                DebugLogger.Instance.LogDebug("FUNNEL", $"RecordOnboardingEvent failed: {ex.Message}");
            }
        }

        /// <summary>
        /// v0.65.8 (R5) Fallen Legacy: record an involuntary permadeath so the
        /// name endures in the Hall of the Fallen and the account's next
        /// character can claim the heirloom. Called from PermadeathHelper
        /// BEFORE DeleteGameData (the players row is about to vanish).
        /// </summary>
        public void RecordFallenLegacy(string username, string displayName, int level, string className, string killer, long heirloomGold)
        {
            try
            {
                if (string.IsNullOrWhiteSpace(username) || string.IsNullOrWhiteSpace(displayName)) return;
                using var connection = OpenConnection();
                using var cmd = connection.CreateCommand();
                cmd.CommandText = @"
                    INSERT INTO fallen_legacy (username, display_name, level, class_name, killer, heirloom_gold)
                    VALUES (@username, @display, @level, @class, @killer, @gold);";
                cmd.Parameters.AddWithValue("@username", username.ToLowerInvariant());
                cmd.Parameters.AddWithValue("@display", displayName);
                cmd.Parameters.AddWithValue("@level", level);
                cmd.Parameters.AddWithValue("@class", className ?? "");
                cmd.Parameters.AddWithValue("@killer", killer ?? "");
                cmd.Parameters.AddWithValue("@gold", heirloomGold);
                cmd.ExecuteNonQuery();
                DebugLogger.Instance.LogInfo("DEATH_CAP",
                    $"Fallen legacy recorded: '{displayName}' Lv.{level} {className} (heirloom {heirloomGold}g) for account '{username}'.");
            }
            catch (Exception ex)
            {
                DebugLogger.Instance.LogWarning("DEATH_CAP", $"RecordFallenLegacy failed: {ex.Message}");
            }
        }

        /// <summary>
        /// v0.65.8 (R5): claim the most recent unclaimed heirloom for this
        /// account. Atomic (UPDATE ... WHERE claimed = 0 wins the race) so a
        /// double-call can't double-grant. Returns null when nothing to claim.
        /// </summary>
        public (string displayName, int level, string className, long heirloomGold)? ClaimFallenLegacy(string username)
        {
            try
            {
                if (string.IsNullOrWhiteSpace(username)) return null;
                using var connection = OpenConnection();
                using var cmd = connection.CreateCommand();
                cmd.CommandText = @"
                    SELECT id, display_name, level, class_name, heirloom_gold
                    FROM fallen_legacy
                    WHERE username = @username AND claimed = 0
                    ORDER BY id DESC LIMIT 1;";
                cmd.Parameters.AddWithValue("@username", username.ToLowerInvariant());
                using var reader = cmd.ExecuteReader();
                if (!reader.Read()) return null;
                long id = reader.GetInt64(0);
                string display = reader.GetString(1);
                int level = reader.GetInt32(2);
                string className = reader.GetString(3);
                long gold = reader.GetInt64(4);
                reader.Close();

                using var claim = connection.CreateCommand();
                claim.CommandText = "UPDATE fallen_legacy SET claimed = 1 WHERE id = @id AND claimed = 0;";
                claim.Parameters.AddWithValue("@id", id);
                if (claim.ExecuteNonQuery() == 0) return null; // lost the race

                // Older unclaimed rows (multiple deaths before a re-roll) are
                // folded closed too -- only the most recent legacy pays out.
                using var fold = connection.CreateCommand();
                fold.CommandText = "UPDATE fallen_legacy SET claimed = 1 WHERE username = @username AND claimed = 0;";
                fold.Parameters.AddWithValue("@username", username.ToLowerInvariant());
                fold.ExecuteNonQuery();

                return (display, level, className, gold);
            }
            catch (Exception ex)
            {
                DebugLogger.Instance.LogWarning("DEATH_CAP", $"ClaimFallenLegacy failed: {ex.Message}");
                return null;
            }
        }

        /// <summary>
        /// v0.65.8 (R5): newest-first memorial rows for the Hall of the Fallen.
        /// </summary>
        public List<(string displayName, int level, string className, string killer, string diedAt)> GetFallenMemorials(int limit = 15)
        {
            var rows = new List<(string, int, string, string, string)>();
            try
            {
                using var connection = OpenConnection();
                using var cmd = connection.CreateCommand();
                cmd.CommandText = @"
                    SELECT display_name, level, class_name, killer, died_at
                    FROM fallen_legacy
                    ORDER BY id DESC LIMIT @limit;";
                cmd.Parameters.AddWithValue("@limit", Math.Clamp(limit, 1, 100));
                using var reader = cmd.ExecuteReader();
                while (reader.Read())
                {
                    string diedAtRaw = reader.IsDBNull(4) ? "" : reader.GetString(4);
                    // Stored as SQLite datetime('now') "yyyy-MM-dd HH:mm:ss"; the
                    // memorial only needs the date part.
                    string diedAt = diedAtRaw.Length >= 10 ? diedAtRaw.Substring(0, 10) : diedAtRaw;
                    rows.Add((reader.GetString(0), reader.GetInt32(1), reader.GetString(2), reader.GetString(3), diedAt));
                }
            }
            catch (Exception ex)
            {
                DebugLogger.Instance.LogWarning("DEATH_CAP", $"GetFallenMemorials failed: {ex.Message}");
            }
            return rows;
        }

        public async Task<(bool success, string message)> RegisterPlayer(string username, string password, string? ipAddress = null)
        {
            // v1.1.1: the relay and desktop clients send AUTH:user:password:type; a colon in the
            // password split that line wrong and locked the account out of every relay login.
            if (password != null && password.Contains(':'))
                return (false, "Password cannot contain ':'.");
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

                // (reserved-name gate is applied with the other name rules below)

                // Block reserved alt character suffix
                if (username.Contains(GameConfig.AltCharacterSuffix, StringComparison.OrdinalIgnoreCase))
                    return (false, "Username contains reserved characters.");

                // Block the Implementor account (security audit F1, v0.65.14)
                if (IsReservedUsername(username))
                    return (false, "That username is reserved.");

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
                // A UNIQUE-constraint failure means the chosen name (or its display name) is
                // already taken. That is a normal user-facing outcome, not a server error: give
                // the player a clear message and log it quietly so it does not page the
                // server-monitor Discord (previously it logged at Error and showed a useless
                // "try again").
                if (ex.Message.Contains("UNIQUE constraint failed"))
                {
                    DebugLogger.Instance.LogInfo("SQL", $"Registration rejected (name already taken): {ex.Message}");
                    return (false, "That name is already taken. Please choose a different name.");
                }
                DebugLogger.Instance.LogError("SQL", $"Failed to register player: {ex.Message}");
                return (false, "Registration failed. Please try again.");
            }
        }

        /// <summary>
        /// Auto-provision a player account for trusted auth (no password).
        /// Used by --auto-provision flag for BBS passthrough connections where
        /// the BBS already handles user authentication.
        /// </summary>
        /// <summary>
        /// Usernames no self-service path may claim. Currently the Implementor
        /// account: PlayerSession auto-promotes it to the top wizard tier on
        /// every login and Implementor can never be demoted, so registering it
        /// would hand a stranger the server (security audit F1, v0.65.14).
        /// The operator provisions it out of band (direct DB insert, or by
        /// pointing USURPER_IMPLEMENTOR at an account they already control).
        /// Case-insensitive: usernames are lowercased at INSERT, and the
        /// auto-promotion compares case-insensitively.
        /// </summary>
        public static bool IsReservedUsername(string username)
        {
            if (string.IsNullOrWhiteSpace(username)) return false;
            return username.Trim().Equals(
                UsurperRemake.Server.WizardConstants.ImplementorUsername,
                StringComparison.OrdinalIgnoreCase);
        }

        public async Task<(bool success, string message)> AutoProvisionPlayer(string username)
        {
            try
            {
                if (string.IsNullOrWhiteSpace(username) || username.Length < 2 || username.Length > 20)
                    return (false, "Username must be 2-20 characters.");

                // Block reserved alt character suffix
                if (username.Contains(GameConfig.AltCharacterSuffix, StringComparison.OrdinalIgnoreCase))
                    return (false, "Username contains reserved characters.");

                // Block the Implementor account (security audit F1, v0.65.14)
                if (IsReservedUsername(username))
                    return (false, "That username is reserved.");

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
        /// <summary>
        /// v0.65.12 (loc audit): persist the language chosen at the login gate
        /// so a fresh registration's first session runs in the player's language
        /// (the column otherwise only updates from the first character save).
        /// </summary>
        public void SetAccountLanguage(string username, string language)
        {
            try
            {
                if (string.IsNullOrWhiteSpace(username) || string.IsNullOrWhiteSpace(language)) return;
                using var connection = OpenConnection();
                using var cmd = connection.CreateCommand();
                cmd.CommandText = "UPDATE players SET language = @lang WHERE LOWER(username) = LOWER(@username);";
                cmd.Parameters.AddWithValue("@lang", language);
                cmd.Parameters.AddWithValue("@username", username);
                cmd.ExecuteNonQuery();
            }
            catch (Exception ex)
            {
                DebugLogger.Instance.LogWarning("AUTH", $"SetAccountLanguage failed: {ex.Message}");
            }
        }

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
                if (newPassword != null && newPassword.Contains(':'))
                    return (false, "Password cannot contain ':'."); // v1.1.1: the relay/desktop AUTH line splits on ':'
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
                if (newPassword.Contains(':'))
                    return (false, "Password cannot contain ':'."); // v1.1.1: the relay/desktop AUTH line splits on ':'
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
        /// v1.1.7: take up to an amount from a player's saved gold and report what was actually
        /// taken, in one transaction. The arena credits the winner with this figure, so a depleted
        /// balance, a missing row, or a failed write cannot mint gold. Zero on any failure.
        /// </summary>
        public async Task<long> TakeGoldFromPlayer(string username, long goldAmount)
        {
            if (goldAmount <= 0) return 0;
            try
            {
                return await Task.Run(() =>
                {
                    using var connection = OpenConnection();
                    using var transaction = connection.BeginTransaction();
                    const string who = "(LOWER(username) = LOWER(@username) OR LOWER(display_name) = LOWER(@username)) AND player_data != '{}' AND LENGTH(player_data) > 2";
                    long held;
                    using (var read = connection.CreateCommand())
                    {
                        read.Transaction = transaction;
                        read.CommandText = $"SELECT CAST(json_extract(player_data, '$.player.gold') AS INTEGER) FROM players WHERE {who} LIMIT 1;";
                        read.Parameters.AddWithValue("@username", username);
                        var v = read.ExecuteScalar();
                        if (v == null || v is DBNull) { transaction.Rollback(); return 0L; }
                        held = Math.Max(0, Convert.ToInt64(v));
                    }
                    long taken = Math.Min(held, goldAmount);
                    if (taken <= 0) { transaction.Rollback(); return 0L; }
                    using (var write = connection.CreateCommand())
                    {
                        write.Transaction = transaction;
                        write.CommandText = $"UPDATE players SET player_data = json_set(player_data, '$.player.gold', @left) WHERE {who};";
                        write.Parameters.AddWithValue("@username", username);
                        write.Parameters.AddWithValue("@left", held - taken);
                        if (write.ExecuteNonQuery() == 0) { transaction.Rollback(); return 0L; }
                    }
                    transaction.Commit();
                    return taken;
                });
            }
            catch (Exception ex)
            {
                DebugLogger.Instance.LogError("SQL", $"Failed to take gold from {username}: {ex.Message}");
                return 0;
            }
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

    /// <summary>
    /// v1.1.12: one guarded INSERT, so of two sessions creating the same name only one gets the row; a name
    /// that differs only in case counts as taken (the protection list ignores case). The join stamp gives
    /// the founder's save time to land before the empty-team cleanup looks at it. False when not created.
    /// </summary>
    public async Task<bool> CreatePlayerTeam(string teamName, string passwordHash, string createdBy)
    {
        try
        {
            return await Task.Run(() =>
            {
                using var connection = OpenConnection();
                using var tx = connection.BeginTransaction();
                using var cmd = connection.CreateCommand();
                cmd.Transaction = tx;
                cmd.CommandText = @"INSERT INTO player_teams (team_name, password_hash, created_by, last_join_at)
                    SELECT @name, @hash, @creator, datetime('now')
                    WHERE NOT EXISTS (SELECT 1 FROM player_teams WHERE ulower(team_name) = ulower(@name));";
                cmd.Parameters.AddWithValue("@name", teamName);
                cmd.Parameters.AddWithValue("@hash", passwordHash);
                cmd.Parameters.AddWithValue("@creator", createdBy.ToLower());
                if (cmd.ExecuteNonQuery() != 1) return false;
                // v1.1.12: a new team starts bare; before this release a team dissolved by its last member
                // left its upgrades and vault under the name for the next team of that name
                foreach (var table in new[] { "team_upgrades", "team_vault" })
                {
                    using var clear = connection.CreateCommand();
                    clear.Transaction = tx;
                    clear.CommandText = $"DELETE FROM {table} WHERE ulower(team_name) = ulower(@name);";
                    clear.Parameters.AddWithValue("@name", teamName);
                    clear.ExecuteNonQuery();
                }
                tx.Commit();
                return true;
            });
        }
        catch (Exception ex)
        {
            DebugLogger.Instance.LogError("SQL", $"Failed to create player team '{teamName}': {ex.Message}");
            return false;
        }
    }

    /// <summary>v1.1.12: whether a team has a player_teams row (an NPC-founded team has none); null on a DB error.</summary>
    public bool? HasPlayerTeamRow(string teamName)
    {
        try
        {
            using var connection = OpenConnection();
            using var cmd = connection.CreateCommand();
            cmd.CommandText = "SELECT COUNT(*) FROM player_teams WHERE team_name = @name;";
            cmd.Parameters.AddWithValue("@name", teamName);
            return Convert.ToInt64(cmd.ExecuteScalar()) > 0;
        }
        catch (Exception ex)
        {
            DebugLogger.Instance.LogError("SQL", $"Failed to look up team '{teamName}': {ex.Message}");
            return null;
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
    /// v1.1.10: a team whose leader key (created_by) matches no character. Teams founded before
    /// v1.1.10 recorded the founder's display name, lowercased; a dying NPC member's bequest is queued
    /// under it, never delivered, and deleted by the orphan sweep. The saves hold no record of who
    /// founded a team, so the admin console maps these one at a time (OnlineAdminConsole.FixTeamLeaders).
    /// </summary>
    public class TeamWithUnknownLeader
    {
        public string TeamName { get; set; } = "";
        public string OldKey { get; set; } = "";
        public List<PlayerSummary> Members { get; set; } = new();
        public int QueuedBequests { get; set; }
        /// <summary>Other teams with the same old key: what waits under it cannot be attributed to either.</summary>
        public List<string> SharedWith { get; set; } = new();
    }

    /// <summary>v1.1.10: every team whose leader key matches no character, with its current members.</summary>
    public async Task<List<TeamWithUnknownLeader>> GetTeamsWithUnknownLeader()
    {
        var teams = new List<TeamWithUnknownLeader>();
        try
        {
            using (var connection = OpenConnection())
            using (var cmd = connection.CreateCommand())
            {
                cmd.CommandText = @"
                    SELECT t.team_name, t.created_by,
                           (SELECT COUNT(*) FROM pending_inheritance pi WHERE pi.player_username = t.created_by)
                    FROM player_teams t
                    WHERE t.created_by NOT IN (SELECT username FROM players)
                    ORDER BY t.team_name;";
                using var reader = cmd.ExecuteReader();
                while (reader.Read())
                    teams.Add(new TeamWithUnknownLeader { TeamName = reader.GetString(0), OldKey = reader.GetString(1), QueuedBequests = reader.GetInt32(2) });
            }
            foreach (var team in teams)
            {
                team.Members = await GetPlayerTeamMembers(team.TeamName);
                team.SharedWith = teams.Where(t => t.OldKey == team.OldKey && t.TeamName != team.TeamName).Select(t => t.TeamName).ToList();
            }
        }
        catch (Exception ex)
        {
            DebugLogger.Instance.LogError("SQL", $"Failed to list teams with an unknown leader: {ex.Message}");
        }
        return teams;
    }

    /// <summary>
    /// v1.1.10: sets a team's leader key to a character's save key, as confirmed by an admin. Only if
    /// the team still has oldKey (nothing changed it meanwhile) and newKey is a character. Bequests
    /// already queued under oldKey follow it; if another team still has oldKey they could be that
    /// team's, so they are set aside for the sweep instead. newKey must be a current member of the team,
    /// and oldKey must still match no character. True when the team was updated.
    /// </summary>
    public bool SetTeamLeaderKey(string teamName, string oldKey, string newKey) => SetTeamLeaderKey(teamName, oldKey, newKey, out _, out _);

    /// <summary>As above; also says how many waiting bequests moved or were set aside, and which.</summary>
    public bool SetTeamLeaderKey(string teamName, string oldKey, string newKey, out int bequests, out bool keyShared)
    {
        bequests = 0; keyShared = false;
        try
        {
            using var connection = OpenConnection();
            using var tx = connection.BeginTransaction();
            using (var check = connection.CreateCommand())
            {
                check.Transaction = tx;
                // a current member of the team only: a stranger who takes a founder's old name is not the leader
                check.CommandText = "SELECT COUNT(*) FROM players WHERE username = @new AND (CASE WHEN json_valid(player_data) THEN json_extract(player_data, '$.player.team') END) = @team;";
                check.Parameters.AddWithValue("@new", newKey);
                check.Parameters.AddWithValue("@team", teamName);
                if (Convert.ToInt32(check.ExecuteScalar()) != 1) return false;
            }
            using (var stillUnknown = connection.CreateCommand())
            {
                // a character registered under the old key since the screen was opened owns what waits under it
                stillUnknown.Transaction = tx;
                stillUnknown.CommandText = "SELECT COUNT(*) FROM players WHERE username = @old;";
                stillUnknown.Parameters.AddWithValue("@old", oldKey);
                if (Convert.ToInt32(stillUnknown.ExecuteScalar()) != 0) return false;
            }
            using (var team = connection.CreateCommand())
            {
                team.Transaction = tx;
                team.CommandText = "UPDATE player_teams SET created_by = @new WHERE team_name = @team AND created_by = @old;";
                team.Parameters.AddWithValue("@new", newKey);
                team.Parameters.AddWithValue("@team", teamName);
                team.Parameters.AddWithValue("@old", oldKey);
                if (team.ExecuteNonQuery() != 1) return false;
            }
            using (var queued = connection.CreateCommand())
            {
                queued.Transaction = tx;
                // Another team still has the old key: what waits under it could be either team's, so it is
                // set aside under a key no character has, where the sweep removes it as it always did, and
                // a later fix of the other team cannot take it (Codex review). Otherwise it follows the team.
                queued.CommandText = @"
                    UPDATE pending_inheritance
                    SET player_username = CASE WHEN EXISTS (SELECT 1 FROM player_teams WHERE created_by = @old)
                                               THEN '#shared-key:' || @old ELSE @new END
                    WHERE player_username = @old;";
                queued.Parameters.AddWithValue("@new", newKey);
                queued.Parameters.AddWithValue("@old", oldKey);
                bequests = queued.ExecuteNonQuery();
            }
            using (var shared = connection.CreateCommand())
            {
                shared.Transaction = tx;
                shared.CommandText = "SELECT COUNT(*) FROM player_teams WHERE created_by = @old;";
                shared.Parameters.AddWithValue("@old", oldKey);
                keyShared = Convert.ToInt32(shared.ExecuteScalar()) > 0;
            }
            tx.Commit();
            DebugLogger.Instance.LogInfo("SQL", $"Team '{teamName}' leader key set from '{oldKey}' to '{newKey}' by an admin");
            return true;
        }
        catch (Exception ex)
        {
            DebugLogger.Instance.LogError("SQL", $"Failed to set the leader key of team '{teamName}': {ex.Message}");
            return false;
        }
    }

    /// <summary>
    /// v1.1.11: the successor to a team or guild leader: the highest level first; the same level goes
    /// to the earliest joiner when a join time is recorded (guild_members.joined_at; teams record none,
    /// players.created_at is the account's age and last_join_at is per team), then the username in
    /// ordinal order. Null when there is no candidate.
    /// </summary>
    public static string? PickSuccessor(IEnumerable<(string Username, int Level, string? JoinedAt)> candidates) =>
        candidates.OrderByDescending(c => c.Level)
            .ThenBy(c => c.JoinedAt == null ? 1 : 0)
            .ThenBy(c => c.JoinedAt ?? "", StringComparer.Ordinal)
            .ThenBy(c => c.Username, StringComparer.Ordinal)
            .Select(c => c.Username).FirstOrDefault();

    /// <summary>
    /// v1.1.11: passes a team's leader key (created_by) from oldKey to the highest-level remaining
    /// player member, never excludeKey, a banned player or an emergency account. Only if the team still
    /// has oldKey; with requireOldLeaderGone, also only if oldKey's save no longer names the team. The
    /// key is left alone when there is no successor. True when the team was updated.
    /// v1.1.11: with respectJoinGrace (the world-save pass), also only if nobody joined within
    /// EmptyTeamJoinGraceMinutes, checked by the update itself: a returning leader's save may not have landed.
    /// </summary>
    /// <summary>v1.1.11: tests only; runs between the successor's selection and the update.</summary>
    internal static Action<string>? BeforeTeamLeaderUpdateForTests;

    public bool TryPassTeamLeadership(string teamName, string oldKey, string? excludeKey, bool requireOldLeaderGone, out string? newKey, bool respectJoinGrace = false)
    {
        newKey = null;
        try
        {
            using var connection = OpenConnection();
            var candidates = new List<(string, int, string?)>();
            using (var cmd = connection.CreateCommand())
            {
                cmd.CommandText = @"
                    SELECT p.username, CAST(json_extract(p.player_data, '$.player.level') AS INTEGER)
                    FROM players p
                    WHERE (CASE WHEN json_valid(p.player_data) THEN json_extract(p.player_data, '$.player.team') END) = @team
                    AND p.player_data != '{}' AND LENGTH(p.player_data) > 2
                    AND p.is_banned = 0 AND p.username NOT LIKE 'emergency_%'
                    AND LOWER(p.username) != LOWER(@old);";
                cmd.Parameters.AddWithValue("@team", teamName);
                cmd.Parameters.AddWithValue("@old", oldKey);
                using var reader = cmd.ExecuteReader();
                while (reader.Read())
                {
                    var key = reader.GetString(0);
                    if (excludeKey != null && string.Equals(key, excludeKey, StringComparison.OrdinalIgnoreCase)) continue;
                    candidates.Add((key, reader.IsDBNull(1) ? 0 : reader.GetInt32(1), null));
                }
            }
            var successor = PickSuccessor(candidates);
            if (successor == null) return false;
            BeforeTeamLeaderUpdateForTests?.Invoke(successor);
            using (var update = connection.CreateCommand())
            {
                // v1.1.11: a leader key changed meanwhile (an admin fix, another pass) wins, and the successor
                // must still be eligible when the update runs (not banned since, still on the team)
                update.CommandText = "UPDATE player_teams SET created_by = @new WHERE team_name = @team AND created_by = @old" + @"
                    AND EXISTS (SELECT 1 FROM players s WHERE LOWER(s.username) = LOWER(@new)
                        AND s.is_banned = 0 AND s.username NOT LIKE 'emergency_%'
                        AND s.player_data != '{}' AND LENGTH(s.player_data) > 2
                        AND (CASE WHEN json_valid(s.player_data) THEN json_extract(s.player_data, '$.player.team') END) = @team)" +
                    (requireOldLeaderGone ? @"
                    AND NOT EXISTS (SELECT 1 FROM players l WHERE l.username = @old
                        AND (NOT json_valid(l.player_data)
                             OR (CASE WHEN json_valid(l.player_data) THEN json_extract(l.player_data, '$.player.team') END) = @team))" : "") +
                    (respectJoinGrace ? " AND (last_join_at IS NULL OR last_join_at < datetime('now', '-' || @joinGrace || ' minutes'))" : "") + ";";
                if (respectJoinGrace) update.Parameters.AddWithValue("@joinGrace", GameConfig.EmptyTeamJoinGraceMinutes);
                update.Parameters.AddWithValue("@new", successor.ToLowerInvariant());
                update.Parameters.AddWithValue("@team", teamName);
                update.Parameters.AddWithValue("@old", oldKey);
                if (update.ExecuteNonQuery() != 1) return false;
            }
            newKey = successor.ToLowerInvariant();
            DebugLogger.Instance.LogInfo("TEAM", $"Team '{teamName}' leadership passed from '{oldKey}' to '{newKey}'");
            return true;
        }
        catch (Exception ex)
        {
            DebugLogger.Instance.LogError("TEAM", $"Failed to pass the leadership of team '{teamName}': {ex.Message}");
            return false;
        }
    }

    /// <summary>v1.1.11: passes on every team led by a character that is being deleted (its row still names the team).</summary>
    public int PassTeamLeadershipOfDeleted(string characterKey)
    {
        if (string.IsNullOrWhiteSpace(characterKey)) return 0;
        var teams = new List<(string Team, string Key)>();
        try
        {
            using var connection = OpenConnection();
            using var cmd = connection.CreateCommand();
            cmd.CommandText = "SELECT team_name, created_by FROM player_teams WHERE LOWER(created_by) = LOWER(@key);";
            cmd.Parameters.AddWithValue("@key", characterKey);
            using var reader = cmd.ExecuteReader();
            while (reader.Read()) teams.Add((reader.GetString(0), reader.GetString(1)));
        }
        catch (Exception ex)
        {
            DebugLogger.Instance.LogError("TEAM", $"Failed to list the teams led by '{characterKey}': {ex.Message}");
        }
        return teams.Count(t => TryPassTeamLeadership(t.Team, t.Key, characterKey, requireOldLeaderGone: false, out _));
    }

    /// <summary>
    /// v1.1.11: teams whose leader key is a known character (a players row) whose valid save no longer
    /// names the team, and that nobody joined within EmptyTeamJoinGraceMinutes (a joiner's save may not
    /// have landed). A key that matches no character is the admin's Fix Team Leaders screen's to map,
    /// so it is never listed here.
    /// </summary>
    public List<(string Team, string Leader)> GetTeamsLedByExMembers()
    {
        var teams = new List<(string, string)>();
        try
        {
            using var connection = OpenConnection();
            using var cmd = connection.CreateCommand();
            cmd.CommandText = @"
                SELECT t.team_name, t.created_by FROM player_teams t
                JOIN players l ON l.username = t.created_by
                -- a deleted character's row stays with '{}' (DeleteGameData), which names no team
                WHERE json_valid(l.player_data)
                AND COALESCE((CASE WHEN json_valid(l.player_data) THEN json_extract(l.player_data, '$.player.team') END), '') != t.team_name
                AND (t.last_join_at IS NULL OR t.last_join_at < datetime('now', '-' || @joinGrace || ' minutes'))
                ORDER BY t.team_name;";
            cmd.Parameters.AddWithValue("@joinGrace", GameConfig.EmptyTeamJoinGraceMinutes);
            using var reader = cmd.ExecuteReader();
            while (reader.Read()) teams.Add((reader.GetString(0), reader.GetString(1)));
        }
        catch (Exception ex)
        {
            DebugLogger.Instance.LogError("TEAM", $"Failed to list teams led by ex-members: {ex.Message}");
        }
        return teams;
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
    /// v1.1.10: queues a bequest for a team's leader, reading the leader key in the same statement, so
    /// a leader key changed by an admin while an estate is being queued cannot leave rows under the
    /// old key for the sweep (Codex review). False when the team does not exist or the write failed.
    /// </summary>
    public bool QueueTeamInheritance(string teamName, string sourceNpcName, string? itemJson, long goldAmount = 0)
    {
        try
        {
            using var connection = OpenConnection();
            using var cmd = connection.CreateCommand();
            cmd.CommandText = @"
                INSERT INTO pending_inheritance (player_username, source_npc_name, item_json, gold_amount)
                SELECT lower(created_by), @npc, @json, @gold FROM player_teams WHERE team_name = @team;";
            cmd.Parameters.AddWithValue("@team", teamName);
            cmd.Parameters.AddWithValue("@npc", sourceNpcName);
            cmd.Parameters.AddWithValue("@json", itemJson ?? "");
            cmd.Parameters.AddWithValue("@gold", goldAmount);
            return cmd.ExecuteNonQuery() == 1;
        }
        catch (Exception ex)
        {
            DebugLogger.Instance.LogError("SQL", $"Failed to queue inheritance for team '{teamName}' from '{sourceNpcName}': {ex.Message}");
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
            if (!VerifyPassword(password, storedHash)) return (true, false);
            // v1.1.11: stamp the join, so the empty-team cleanup (in this process or another) leaves the team
            // alone until the membership is saved; no row means it was removed a moment ago
            using var stamp = connection.CreateCommand();
            stamp.CommandText = "UPDATE player_teams SET last_join_at = datetime('now') WHERE team_name = @name;";
            stamp.Parameters.AddWithValue("@name", teamName);
            if (stamp.ExecuteNonQuery() != 1) return (false, false);
            return (true, true);
        }
        catch (Exception ex)
        {
            DebugLogger.Instance.LogError("SQL", $"Failed to verify player team '{teamName}': {ex.Message}");
            return (false, false);
        }
    }

    /// <summary>
    /// One row per player_teams row, with the player member count and sums from one pass over the saves.
    /// </summary>
    public async Task<List<PlayerTeamInfo>> GetPlayerTeams()
    {
        try
        {
            return await Task.Run(() =>
            {
                using var connection = OpenConnection();
                var stats = ReadTeamPlayerStats(connection, null);
                return ReadTeamRows(connection, stats).OrderByDescending(t => t.MemberCount).ToList();
            });
        }
        catch (Exception ex)
        {
            DebugLogger.Instance.LogError("SQL", $"Failed to get player teams: {ex.Message}");
            return new List<PlayerTeamInfo>();
        }
    }

    /// <summary>
    /// v1.1.12: the team rankings' player side: every player_teams row plus every team named only in player
    /// saves (HasTeamRow false), with the viewer's own save left out (excludeSaveKey, the players.username key)
    /// so the caller adds the in-memory character once, with its current level and team.
    /// </summary>
    public async Task<List<PlayerTeamInfo>> GetTeamRankingStats(string? excludeSaveKey)
    {
        try
        {
            return await Task.Run(() =>
            {
                using var connection = OpenConnection();
                var stats = ReadTeamPlayerStats(connection, excludeSaveKey);
                var teams = ReadTeamRows(connection, stats);
                var named = teams.Select(t => t.TeamName).ToHashSet(StringComparer.Ordinal);
                foreach (var (team, st) in stats)
                    if (!named.Contains(team))
                        teams.Add(new PlayerTeamInfo { TeamName = team, MemberCount = st.Members, LevelSum = st.LevelSum, PowerSum = st.PowerSum, HasTeamRow = false });
                return teams;
            });
        }
        catch (Exception ex)
        {
            DebugLogger.Instance.LogError("SQL", $"Failed to get team ranking stats: {ex.Message}");
            return new List<PlayerTeamInfo>();
        }
    }

    // v1.1.12: TOTAL, not SUM, since SUM throws on overflow and one absurd stat would blank the list
    private static long ClampToLong(double v) => v >= long.MaxValue ? long.MaxValue : v <= long.MinValue ? long.MinValue : (long)v;

    private static List<PlayerTeamInfo> ReadTeamRows(SqliteConnection connection, Dictionary<string, (int Members, long LevelSum, long PowerSum)> stats)
    {
        var teams = new List<PlayerTeamInfo>();
        using var cmd = connection.CreateCommand();
        cmd.CommandText = "SELECT team_name, created_by, controls_turf, created_at FROM player_teams;";
        using var reader = cmd.ExecuteReader();
        while (reader.Read())
        {
            var name = reader.GetString(0);
            stats.TryGetValue(name, out var st);
            teams.Add(new PlayerTeamInfo
            {
                TeamName = name,
                CreatedBy = reader.GetString(1),
                MemberCount = st.Members,
                LevelSum = st.LevelSum,
                PowerSum = st.PowerSum,
                ControlsTurf = reader.GetInt32(2) != 0,
                CreatedAt = DateTime.TryParse(reader.GetString(3), out var dt) ? dt : DateTime.Now
            });
        }
        return teams;
    }

    /// <summary>
    /// v1.1.12: player members per team in ONE pass over players. The v1.1.11 query counted each team with
    /// its own scan, so 66 teams parsed about 2.4 GB of save JSON (6.1 s on a 38 MB test set, now 0.1 s).
    /// The multi-path json_extract parses each blob once; MATERIALIZED stops SQLite from inlining it into
    /// each '$[n]' read below (inlined was twice as slow). json_valid keeps one malformed save from failing
    /// every team, and a team value that is not text is ignored.
    /// </summary>
    private static Dictionary<string, (int Members, long LevelSum, long PowerSum)> ReadTeamPlayerStats(SqliteConnection connection, string? excludeSaveKey)
    {
        var stats = new Dictionary<string, (int, long, long)>(StringComparer.Ordinal);
        using var cmd = connection.CreateCommand();
        cmd.CommandText = @"
            WITH s AS MATERIALIZED (
                SELECT CASE WHEN json_valid(player_data)
                            THEN json_extract(player_data, '$.player.team', '$.player.level', '$.player.strength', '$.player.defence') END AS v
                FROM players
                WHERE player_data != '{}' AND LENGTH(player_data) > 2
                  AND is_banned = 0 AND username NOT LIKE 'emergency_%'
                  AND (@me IS NULL OR LOWER(username) != @me))
            SELECT json_extract(v, '$[0]') AS team, COUNT(*),
                   TOTAL(COALESCE(CAST(json_extract(v, '$[1]') AS INTEGER), 0)),
                   TOTAL(COALESCE(CAST(json_extract(v, '$[1]') AS INTEGER), 0)
                     + COALESCE(CAST(json_extract(v, '$[2]') AS INTEGER), 0)
                     + COALESCE(CAST(json_extract(v, '$[3]') AS INTEGER), 0))
            FROM s
            WHERE json_type(v, '$[0]') = 'text' AND json_extract(v, '$[0]') != ''
            GROUP BY team;";
        cmd.Parameters.AddWithValue("@me", string.IsNullOrEmpty(excludeSaveKey) ? DBNull.Value : excludeSaveKey.ToLowerInvariant());
        using var reader = cmd.ExecuteReader();
        while (reader.Read())
            stats[reader.GetString(0)] = (reader.GetInt32(1), ClampToLong(reader.GetDouble(2)), ClampToLong(reader.GetDouble(3)));
        return stats;
    }

    /// <summary>
    /// Roster of PLAYER members of a team. Membership is read from each player's own
    /// save blob (player_data.player.team), so a member is only visible here once
    /// their save row has been written -- see TeamCornerLocation.PersistTeamMembershipChange.
    ///
    /// The exclude parameter is matched against display_name, NOT the account username.
    /// It is named accordingly: passing an account key here would silently fail to
    /// exclude the viewer, which is the same defect that produced the v0.57.7 arena
    /// "you can fight yourself" bug.
    /// </summary>
    public async Task<List<PlayerSummary>> GetPlayerTeamMembers(string teamName, string? excludeDisplayName = null)
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
                       CASE WHEN op.username IS NOT NULL THEN 1 ELSE 0 END as is_online,
                       p.username
                FROM players p
                LEFT JOIN online_players op ON LOWER(p.username) = LOWER(op.username)
                    AND op.last_heartbeat > datetime('now', '-120 seconds')
                -- v1.1.10: one malformed save blob made json_extract throw for every team (Codex review)
                WHERE (CASE WHEN json_valid(p.player_data) THEN json_extract(p.player_data, '$.player.team') END) = @teamName
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
                if (excludeDisplayName != null && name.Equals(excludeDisplayName, StringComparison.OrdinalIgnoreCase))
                    continue;
                members.Add(new PlayerSummary
                {
                    Username = reader.GetString(6), // v1.1.1: team wars load saves by username
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

    /// <summary>
    /// v1.1.11: player teams that no player's save names, banned players included (a ban can be lifted).
    /// Whether an NPC or an online player still carries the name is for the caller to check
    /// (WorldSimService.PruneEmptyTeams); only it knows the live roster.
    /// </summary>
    public List<string> GetTeamsWithoutPlayerMembers()
    {
        var teams = new List<string>();
        try
        {
            using var connection = OpenConnection();
            using var cmd = connection.CreateCommand();
            // A malformed save cannot say which team it names, so while one exists no team counts as empty.
            using (var bad = connection.CreateCommand())
            {
                bad.CommandText = "SELECT username FROM players WHERE NOT json_valid(player_data);";
                using var badReader = bad.ExecuteReader();
                var badKeys = new List<string>();
                while (badReader.Read()) badKeys.Add(badReader.GetString(0));
                if (badKeys.Count > 0)
                {
                    DebugLogger.Instance.LogWarning("SQL", $"Empty-team cleanup skipped: malformed save(s) for {string.Join(", ", badKeys)}");
                    return teams;
                }
            }
            // A character archived by permadeath can be restored within the window, so it is still a member (review).
            cmd.CommandText = @"
                SELECT t.team_name FROM player_teams t
                WHERE NOT EXISTS (SELECT 1 FROM players p
                    WHERE (CASE WHEN json_valid(p.player_data) THEN json_extract(p.player_data, '$.player.team') END) = t.team_name)
                AND NOT EXISTS (SELECT 1 FROM deleted_characters d WHERE d.expires_at > datetime('now')
                    AND (CASE WHEN json_valid(d.player_data) THEN json_extract(d.player_data, '$.player.team') END) = t.team_name);";
            using var reader = cmd.ExecuteReader();
            while (reader.Read()) teams.Add(reader.GetString(0));
        }
        catch (Exception ex)
        {
            DebugLogger.Instance.LogError("SQL", $"Failed to list teams without player members: {ex.Message}");
        }
        return teams;
    }

    /// <summary>
    /// v1.1.11: removes a team nobody is in, with its upgrades and vault, in one transaction; only if no
    /// player's save names it at the moment of the delete, and no save is malformed (it could name the
    /// team). True when it was removed.
    /// </summary>
    public bool DeleteEmptyTeam(string teamName)
    {
        try
        {
            using var connection = OpenConnection();
            using var tx = connection.BeginTransaction();
            using (var team = connection.CreateCommand())
            {
                team.Transaction = tx;
                team.CommandText = @"
                    DELETE FROM player_teams WHERE team_name = @team
                    AND NOT EXISTS (SELECT 1 FROM players p
                        WHERE NOT json_valid(p.player_data)
                        OR (CASE WHEN json_valid(p.player_data) THEN json_extract(p.player_data, '$.player.team') END) = @team)
                    AND NOT EXISTS (SELECT 1 FROM deleted_characters d WHERE d.expires_at > datetime('now')
                        AND (CASE WHEN json_valid(d.player_data) THEN json_extract(d.player_data, '$.player.team') END) = @team)
                    AND (last_join_at IS NULL OR last_join_at < datetime('now', '-' || @joinGrace || ' minutes'));";
                team.Parameters.AddWithValue("@team", teamName);
                team.Parameters.AddWithValue("@joinGrace", GameConfig.EmptyTeamJoinGraceMinutes);
                if (team.ExecuteNonQuery() != 1) return false;
            }
            foreach (var table in new[] { "team_upgrades", "team_vault" })
            {
                using var rest = connection.CreateCommand();
                rest.Transaction = tx;
                rest.CommandText = $"DELETE FROM {table} WHERE team_name = @team;";
                rest.Parameters.AddWithValue("@team", teamName);
                rest.ExecuteNonQuery();
            }
            tx.Commit();
            return true;
        }
        catch (Exception ex)
        {
            DebugLogger.Instance.LogError("SQL", $"Failed to remove empty team '{teamName}': {ex.Message}");
            return false;
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
                    WHERE (CASE WHEN json_valid(player_data) THEN json_extract(player_data, '$.player.team') END) = @teamName
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
            // v1.1.12: ignores case, as the protection list and the create guard do
            cmd.CommandText = "SELECT COUNT(*) FROM player_teams WHERE ulower(team_name) = ulower(@name);";
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

    /// <summary>
    /// v1.1.12: the team password lives in password_hash, which a join checks; the change used to set only
    /// the in-memory copies, so the new password was refused and the old one still worked. Only the leader
    /// (created_by) may change it, and only while the old password still matches (a compare-and-swap on
    /// the stored hash). True when the row changed.
    /// </summary>
    public bool ChangeTeamPassword(string teamName, string leaderKey, string oldPassword, string newPassword)
    {
        try
        {
            using var connection = OpenConnection();
            string? stored;
            using (var read = connection.CreateCommand())
            {
                read.CommandText = "SELECT password_hash FROM player_teams WHERE team_name = @name AND created_by = @leader;";
                read.Parameters.AddWithValue("@name", teamName);
                read.Parameters.AddWithValue("@leader", leaderKey.ToLowerInvariant());
                stored = read.ExecuteScalar()?.ToString();
            }
            if (stored == null || !VerifyPassword(oldPassword, stored)) return false;
            using var cmd = connection.CreateCommand();
            cmd.CommandText = @"UPDATE player_teams SET password_hash = @hash
                WHERE team_name = @name AND created_by = @leader AND password_hash = @old;";
            cmd.Parameters.AddWithValue("@hash", HashTeamPassword(newPassword));
            cmd.Parameters.AddWithValue("@name", teamName);
            cmd.Parameters.AddWithValue("@leader", leaderKey.ToLowerInvariant());
            cmd.Parameters.AddWithValue("@old", stored);
            return cmd.ExecuteNonQuery() == 1;
        }
        catch (Exception ex)
        {
            DebugLogger.Instance.LogError("SQL", $"Failed to change the password of team '{teamName}': {ex.Message}");
            return false;
        }
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
    /// v1.1.11: another player's row (a different key) uses the name, as its display name or its save's
    /// Name2. On a failed read the name counts as used, so the delete purge keeps that bounty.
    /// </summary>
    public bool IsNameUsedByAnotherPlayer(string name, string username)
    {
        if (string.IsNullOrWhiteSpace(name)) return false;
        try
        {
            using var connection = OpenConnection();
            using var cmd = connection.CreateCommand();
            cmd.CommandText = "SELECT EXISTS (SELECT 1 FROM players WHERE LOWER(username) != LOWER(@u) AND (" +
                "LOWER(display_name) = LOWER(@n) OR " +
                "LOWER(CASE WHEN json_valid(player_data) THEN json_extract(player_data, '$.player.name2') END) = LOWER(@n)));";
            cmd.Parameters.AddWithValue("@u", username ?? "");
            cmd.Parameters.AddWithValue("@n", name);
            return Convert.ToInt64(cmd.ExecuteScalar()) != 0;
        }
        catch (Exception ex)
        {
            DebugLogger.Instance.LogWarning("SQL", $"IsNameUsedByAnotherPlayer('{name}') failed: {ex.Message}");
            return true;
        }
    }

    /// <summary>
    /// v1.1.11: a player row of another character (its save Name2 is not ownName2) uses the name, as display
    /// name or Name2. A failed read counts as used, so a bounty under a shared name is not paid.
    /// </summary>
    public bool IsNameUsedByAnotherCharacter(string name, string ownName2)
    {
        if (string.IsNullOrWhiteSpace(name)) return false;
        try
        {
            using var connection = OpenConnection();
            using var cmd = connection.CreateCommand();
            cmd.CommandText = "SELECT EXISTS (SELECT 1 FROM players p, " +
                "(SELECT LOWER(CASE WHEN json_valid(player_data) THEN json_extract(player_data, '$.player.name2') END) AS n2, rowid AS rid FROM players) j " +
                "WHERE j.rid = p.rowid AND COALESCE(j.n2, '') != LOWER(@own) AND (LOWER(p.display_name) = LOWER(@n) OR j.n2 = LOWER(@n)));";
            cmd.Parameters.AddWithValue("@n", name);
            cmd.Parameters.AddWithValue("@own", ownName2 ?? "");
            return Convert.ToInt64(cmd.ExecuteScalar()) != 0;
        }
        catch (Exception ex)
        {
            DebugLogger.Instance.LogWarning("SQL", $"IsNameUsedByAnotherCharacter('{name}') failed: {ex.Message}");
            return true;
        }
    }

    /// <summary>v1.1.11: the players.display_name of one key (the married surname form), or null.</summary>
    public string? GetStoredDisplayName(string username)
    {
        try
        {
            using var connection = OpenConnection();
            using var cmd = connection.CreateCommand();
            cmd.CommandText = "SELECT display_name FROM players WHERE LOWER(username) = LOWER(@u) LIMIT 1;";
            cmd.Parameters.AddWithValue("@u", username);
            return cmd.ExecuteScalar() as string;
        }
        catch { return null; }
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
            // v1.1.1 (review): a username can equal another account's display name, so prefer the
            // exact username, then the exact display name.
            cmd.CommandText = "SELECT username FROM players WHERE (LOWER(username) = LOWER(@name) OR LOWER(display_name) = LOWER(@name)) AND is_banned = 0 AND username NOT LIKE 'emergency_%' ORDER BY (LOWER(username) = LOWER(@name)) DESC, (LOWER(display_name) = LOWER(@name)) DESC LIMIT 1;";
            cmd.Parameters.AddWithValue("@name", nameOrDisplay);
            var result = cmd.ExecuteScalar();
            return result?.ToString();
        }
        catch { return null; }
    }

    /// <summary>
    /// v1.1.1: resolve a character's Name2 to a username for mail. Exact username or display
    /// name first; otherwise a display name of the form "Name2 Surname" (a player with a family
    /// surname). Only for notifications: the surname prefix is too loose for anything that moves
    /// gold or leadership, which keep using ResolvePlayerUsername.
    /// </summary>
    public string? ResolvePlayerUsernameForMail(string characterName)
    {
        var exact = ResolvePlayerUsername(characterName);
        if (!string.IsNullOrEmpty(exact)) return exact;
        try
        {
            using var connection = OpenConnection();
            using var cmd = connection.CreateCommand();
            cmd.CommandText = "SELECT username FROM players WHERE LOWER(display_name) LIKE LOWER(@prefix) ESCAPE '\\' AND is_banned = 0 LIMIT 1";
            var escaped = characterName.Replace("\\", "\\\\").Replace("%", "\\%").Replace("_", "\\_");
            cmd.Parameters.AddWithValue("@prefix", escaped + " %");
            return cmd.ExecuteScalar()?.ToString();
        }
        catch { return null; }
    }

    /// <summary>
    /// v1.1.13: the save key of a player king. The throne keeps the display name (with any married surname),
    /// so that is matched first, then the character name (crowned before a marriage), then a username.
    /// Null when no player goes by the name.
    /// </summary>
    public string? ResolveKingSaveKey(string kingName)
    {
        if (string.IsNullOrWhiteSpace(kingName)) return null;
        try
        {
            using var connection = OpenConnection();
            using var cmd = connection.CreateCommand();
            const string name2 = "LOWER(CASE WHEN json_valid(player_data) THEN json_extract(player_data, '$.player.name2') END)";
            cmd.CommandText = "SELECT username FROM players WHERE username NOT LIKE 'emergency_%' AND LENGTH(player_data) > 2 " +
                $"AND (LOWER(display_name) = LOWER(@name) OR {name2} = LOWER(@name) OR LOWER(username) = LOWER(@name)) " +
                $"ORDER BY (LOWER(display_name) = LOWER(@name)) DESC, ({name2} = LOWER(@name)) DESC LIMIT 1;";
            cmd.Parameters.AddWithValue("@name", kingName);
            return cmd.ExecuteScalar()?.ToString();
        }
        catch { return null; }
    }

    /// <summary>v1.1.13: the player king's save, read by its resolved key; null when no player matches.</summary>
    public async Task<SaveGameData?> ReadKingSave(string kingName)
    {
        var key = ResolveKingSaveKey(kingName);
        return key == null ? null : await ReadGameData(key);
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

    /// <summary>
    /// v1.1.12: guarded, so two challenges at once cannot both start a war for the same team; the payer's
    /// key (the pending_gold_transfers recipient) is kept for a refund if the war is abandoned. -1 when
    /// not created.
    /// </summary>
    public async Task<int> CreateTeamWar(string challengerTeam, string defenderTeam, long goldWagered, string challengerKey = "")
    {
        try
        {
            ExpireStaleTeamWars();
            using var connection = OpenConnection();
            using var cmd = connection.CreateCommand();
            cmd.CommandText = @"INSERT INTO team_wars (challenger_team, defender_team, status, gold_wagered, challenger_key)
                                SELECT @challenger, @defender, 'active', @gold, @key
                                WHERE NOT EXISTS (SELECT 1 FROM team_wars WHERE status = 'active'
                                    AND (challenger_team IN (@challenger, @defender) OR defender_team IN (@challenger, @defender)))
                                RETURNING id;";
            cmd.Parameters.AddWithValue("@challenger", challengerTeam);
            cmd.Parameters.AddWithValue("@defender", defenderTeam);
            cmd.Parameters.AddWithValue("@gold", goldWagered);
            cmd.Parameters.AddWithValue("@key", (challengerKey ?? "").ToLowerInvariant());
            var result = await cmd.ExecuteScalarAsync();
            return result == null || result is DBNull ? -1 : Convert.ToInt32(result);
        }
        catch (Exception ex) { DebugLogger.Instance.LogError("SQL", $"Failed to create team war: {ex.Message}"); return -1; }
    }

    /// <summary>
    /// v1.1.12: a war still 'active' after GameConfig.TeamWarStaleMinutes was left by a lost session and
    /// would block both teams for ever. It is marked 'abandoned'; if no round was recorded, the wager goes
    /// back to its payer by a queued transfer, in the same transaction and only by the process that flipped
    /// the row. A war with rounds recorded is not refunded, so leaving a losing war does not pay. It is not
    /// settled by score either, since a challenger could leave while ahead; this holds too for a fought war
    /// whose own completion failed (TeamCornerLocation pays nothing then). Returns the number expired.
    /// </summary>
    public int ExpireStaleTeamWars(int? staleMinutes = null)
    {
        int expired = 0;
        try
        {
            using var connection = OpenConnection();
            using var tx = connection.BeginTransaction();
            var stale = new List<(int Id, long Wager, int Wins, string Key, string Team)>();
            using (var q = connection.CreateCommand())
            {
                q.Transaction = tx;
                q.CommandText = @"SELECT id, gold_wagered, challenger_wins + defender_wins, COALESCE(challenger_key, ''), challenger_team
                                  FROM team_wars WHERE status = 'active' AND started_at < datetime('now', '-' || @mins || ' minutes');";
                q.Parameters.AddWithValue("@mins", staleMinutes ?? GameConfig.TeamWarStaleMinutes);
                using var r = q.ExecuteReader();
                while (r.Read()) stale.Add((r.GetInt32(0), r.GetInt64(1), r.GetInt32(2), r.GetString(3), r.GetString(4)));
            }
            foreach (var war in stale)
            {
                using var flip = connection.CreateCommand();
                flip.Transaction = tx;
                flip.CommandText = "UPDATE team_wars SET status = 'abandoned', finished_at = datetime('now') WHERE id = @id AND status = 'active';";
                flip.Parameters.AddWithValue("@id", war.Id);
                if (flip.ExecuteNonQuery() != 1) continue;
                expired++;
                if (war.Wins != 0 || war.Wager <= 0 || string.IsNullOrEmpty(war.Key)) continue;
                using var refund = connection.CreateCommand();
                refund.Transaction = tx;
                refund.CommandText = @"INSERT INTO pending_gold_transfers (recipient_username, sender_display, amount, note)
                                       VALUES (@user, @sender, @amount, 'Team war refund');";
                refund.Parameters.AddWithValue("@user", war.Key);
                refund.Parameters.AddWithValue("@sender", war.Team);
                refund.Parameters.AddWithValue("@amount", war.Wager);
                refund.ExecuteNonQuery();
            }
            tx.Commit();
        }
        catch (Exception ex) { DebugLogger.Instance.LogError("SQL", $"Failed to expire stale team wars: {ex.Message}"); }
        return expired;
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

    /// <summary>
    /// v1.1.12: guarded on the war still being 'active' (the same flip ExpireStaleTeamWars makes), so a war
    /// is settled once. True only when this call settled it; a caller refunds only then.
    /// </summary>
    public async Task<bool> CompleteTeamWar(int warId, string result)
    {
        try
        {
            using var connection = OpenConnection();
            using var cmd = connection.CreateCommand();
            cmd.CommandText = @"UPDATE team_wars SET status = @result, finished_at = datetime('now') WHERE id = @id AND status = 'active';";
            cmd.Parameters.AddWithValue("@id", warId);
            cmd.Parameters.AddWithValue("@result", result);
            return await cmd.ExecuteNonQueryAsync() == 1;
        }
        catch (Exception ex) { DebugLogger.Instance.LogError("SQL", $"Failed to complete team war: {ex.Message}"); return false; }
    }

    /// <summary>v1.1.12: a war's status ('active', a result, or 'abandoned'); null if it cannot be read.</summary>
    public async Task<string?> GetTeamWarStatus(int warId)
    {
        try
        {
            using var connection = OpenConnection();
            using var cmd = connection.CreateCommand();
            cmd.CommandText = "SELECT status FROM team_wars WHERE id = @id;";
            cmd.Parameters.AddWithValue("@id", warId);
            return (await cmd.ExecuteScalarAsync())?.ToString();
        }
        catch (Exception ex) { DebugLogger.Instance.LogError("SQL", $"Failed to read team war status: {ex.Message}"); return null; }
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
            ExpireStaleTeamWars();   // v1.1.12: a war a lost session left active no longer blocks both teams
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

    public Task<WorldBossInfo?> GetActiveWorldBoss() =>
        QueryOneWorldBoss("WHERE status = 'active' AND expires_at > datetime('now') ORDER BY started_at DESC");

    /// <summary>v1.1.4: the old shape, kept for callers that do not know the player's level.</summary>
    public async Task<(long remainingHp, bool wasKillingBlow)> RecordWorldBossDamage(int bossId, string playerName, long damage)
    {
        var (remaining, kill, _) = await RecordWorldBossDamage(bossId, playerName, damage, 0);
        return (remaining, kill);
    }

    public async Task<List<WorldBossDamageEntry>> GetWorldBossDamageLeaderboard(int bossId, int limit = 20)
    {
        var entries = new List<WorldBossDamageEntry>();
        try
        {
            using var connection = OpenConnection();
            using var cmd = connection.CreateCommand();
            cmd.CommandText = @"SELECT player_name, damage_dealt, hits, COALESCE(night_damage, 0), COALESCE(player_level, 0),
                                       COALESCE(rounds, 0), COALESCE(sessions, 0), COALESCE(is_npc, 0), COALESCE(display_name, '')
                                FROM world_boss_damage
                                WHERE boss_id = @id AND damage_dealt > 0 ORDER BY damage_dealt DESC LIMIT @limit;";
            cmd.Parameters.AddWithValue("@id", bossId);
            cmd.Parameters.AddWithValue("@limit", limit);
            using var reader = await cmd.ExecuteReaderAsync();
            while (await reader.ReadAsync())
            {
                entries.Add(new WorldBossDamageEntry
                {
                    PlayerName = reader.GetString(0),
                    DamageDealt = reader.GetInt64(1),
                    Hits = reader.GetInt32(2),
                    NightDamage = reader.GetInt64(3),
                    PlayerLevel = reader.GetInt32(4),
                    Rounds = reader.GetInt32(5),
                    Sessions = reader.GetInt32(6),
                    IsNpc = reader.GetInt32(7) != 0,
                    DisplayName = reader.GetString(8),
                });
            }
        }
        catch (Exception ex) { DebugLogger.Instance.LogError("SQL", $"Failed to get boss damage leaderboard: {ex.Message}"); }
        return entries;
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

    /// <summary>
    /// v1.1.12: raises a facility one level only if it is still at expectedLevel and below the cap, so two
    /// members upgrading at once cannot both land (or pass the cap). When payFromVault, the cost comes out
    /// of the vault in the same transaction, and nothing changes unless both land. True when it landed.
    /// </summary>
    public bool TryUpgradeTeamFacility(string teamName, string upgradeType, int expectedLevel, long cost, bool payFromVault)
    {
        try
        {
            using var connection = OpenConnection();
            using var tx = connection.BeginTransaction();
            if (payFromVault)
            {
                using var pay = connection.CreateCommand();
                pay.Transaction = tx;
                pay.CommandText = "UPDATE team_vault SET gold = gold - @cost WHERE team_name = @team AND gold >= @cost;";
                pay.Parameters.AddWithValue("@team", teamName);
                pay.Parameters.AddWithValue("@cost", cost);
                if (pay.ExecuteNonQuery() != 1) return false;
            }
            using var cmd = connection.CreateCommand();
            cmd.Transaction = tx;
            cmd.CommandText = expectedLevel == 0
                ? @"INSERT INTO team_upgrades (team_name, upgrade_type, level, invested_gold)
                    VALUES (@team, @type, 1, @cost)
                    ON CONFLICT(team_name, upgrade_type) DO UPDATE SET
                        level = level + 1, invested_gold = invested_gold + @cost
                    WHERE team_upgrades.level = 0;"
                : @"UPDATE team_upgrades SET level = level + 1, invested_gold = invested_gold + @cost
                    WHERE team_name = @team AND upgrade_type = @type AND level = @expected AND level < @cap;";
            cmd.Parameters.AddWithValue("@team", teamName);
            cmd.Parameters.AddWithValue("@type", upgradeType);
            cmd.Parameters.AddWithValue("@cost", cost);
            cmd.Parameters.AddWithValue("@expected", expectedLevel);
            cmd.Parameters.AddWithValue("@cap", GameConfig.MaxTeamFacilityLevel);
            if (cmd.ExecuteNonQuery() != 1) return false;
            tx.Commit();
            return true;
        }
        catch (Exception ex) { DebugLogger.Instance.LogError("SQL", $"Failed to upgrade facility: {ex.Message}"); return false; }
    }

    /// <summary>v1.1.12: the vault's size at a vault level.</summary>
    public static long TeamVaultCapacity(int vaultLevel) => GameConfig.TeamVaultBaseCapacity + vaultLevel * GameConfig.TeamVaultCapacityPerLevel;

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
            // v1.1.12: the capacity is enforced here, from the vault level in the same statement, so two
            // deposits at once cannot overfill it; false when it would not fit
            cmd.CommandText = @"WITH cap AS (SELECT @base + @per * COALESCE((SELECT level FROM team_upgrades
                                    WHERE team_name = @team AND upgrade_type = 'vault'), 0) AS c)
                                INSERT INTO team_vault (team_name, gold)
                                SELECT @team, @amount WHERE @amount > 0 AND @amount <= (SELECT c FROM cap)
                                ON CONFLICT(team_name) DO UPDATE SET gold = gold + @amount
                                WHERE team_vault.gold + @amount <= (SELECT c FROM cap);";
            cmd.Parameters.AddWithValue("@team", teamName);
            cmd.Parameters.AddWithValue("@amount", amount);
            cmd.Parameters.AddWithValue("@base", GameConfig.TeamVaultBaseCapacity);
            cmd.Parameters.AddWithValue("@per", GameConfig.TeamVaultCapacityPerLevel);
            return await cmd.ExecuteNonQueryAsync() == 1;
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

    /// <summary>v1.1.11: every upgrade level of a team in one read (TeamHQBonus.RefreshLevels).</summary>
    public Dictionary<string, int> GetTeamUpgradeLevels(string teamName)
    {
        var levels = new Dictionary<string, int>();
        try
        {
            using var connection = OpenConnection();
            using var cmd = connection.CreateCommand();
            cmd.CommandText = "SELECT upgrade_type, level FROM team_upgrades WHERE team_name = @team;";
            cmd.Parameters.AddWithValue("@team", teamName);
            using var reader = cmd.ExecuteReader();
            while (reader.Read()) levels[reader.GetString(0)] = reader.GetInt32(1);
        }
        catch (Exception ex)
        {
            DebugLogger.Instance.LogError("SQL", $"Failed to read the upgrades of team '{teamName}': {ex.Message}");
        }
        return levels;
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

        /// <summary>
        /// v1.1.13: claim a pending admin command before running it. Only one of the game server and the
        /// web server's withdrawal wins: true when this call moved the row from pending to executing.
        /// </summary>
        public bool TryClaimAdminCommand(int id)
        {
            try
            {
                using var connection = OpenConnection();
                using var cmd = connection.CreateCommand();
                cmd.CommandText = "UPDATE admin_commands SET status = 'executing' WHERE id = @id AND status = 'pending';";
                cmd.Parameters.AddWithValue("@id", id);
                return cmd.ExecuteNonQuery() == 1;
            }
            catch (Exception ex)
            {
                DebugLogger.Instance.LogError("SQL", $"TryClaimAdminCommand failed: {ex.Message}");
                return false;
            }
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

        /// <summary>v1.1.13: the MUD's admin poller is alive; the web delete checks this before queueing.</summary>
        public void TouchMudHeartbeat()
        {
            try
            {
                using var connection = OpenConnection();
                using var cmd = connection.CreateCommand();
                cmd.CommandText = "INSERT INTO mud_heartbeat (id, beat_at) VALUES (1, datetime('now')) " +
                                  "ON CONFLICT(id) DO UPDATE SET beat_at = datetime('now');";
                cmd.ExecuteNonQuery();
            }
            catch { /* best-effort */ }
        }

        /// <summary>v1.1.13: a world purge queued by a web delete, with what the delete knew of the character then.</summary>
        public sealed record PendingPurge(long Id, string Username, string? Name2, string? DisplayName,
                                          DateTime? DeletedAt, string? PlayerId, bool? Untimed);

        /// <summary>
        /// v1.1.13: world purges queued by a web delete made while the MUD was down, oldest first. DeletedAt is
        /// the delete time as a local time (deleted_at is SQLite UTC text; memory times are local).
        /// </summary>
        public List<PendingPurge> GetPendingPurges()
        {
            var list = new List<PendingPurge>();
            try
            {
                using var connection = OpenConnection();
                using var cmd = connection.CreateCommand();
                cmd.CommandText = "SELECT id, username, name2, display_name, deleted_at, player_id, untimed FROM pending_purges ORDER BY id LIMIT 20;";
                using var reader = cmd.ExecuteReader();
                while (reader.Read())
                {
                    string? Text(int i) => reader.IsDBNull(i) ? null : Convert.ToString(reader.GetValue(i));
                    DateTime? at = DateTime.TryParseExact(Text(4), "yyyy-MM-dd HH:mm:ss", System.Globalization.CultureInfo.InvariantCulture,
                        System.Globalization.DateTimeStyles.AssumeUniversal | System.Globalization.DateTimeStyles.AdjustToUniversal, out var utc)
                        ? utc.ToLocalTime() : null;
                    bool? untimed = reader.IsDBNull(6) ? null : Convert.ToInt64(reader.GetValue(6)) != 0;
                    list.Add(new PendingPurge(reader.GetInt64(0), reader.GetString(1), Text(2), Text(3), at, Text(5), untimed));
                }
            }
            catch (Exception ex) { DebugLogger.Instance.LogError("SQL", $"GetPendingPurges failed: {ex.Message}"); }
            return list;
        }

        /// <summary>
        /// v1.1.13: a character was made on this key, or under one of these names, after deletedAt (local time): a
        /// player row with a save whose created_at or last_login (every save sets it) is at or after the delete.
        /// A queued purge then leaves that character's rows alone. A failed read counts as made again.
        /// </summary>
        public bool WasRecreatedSince(string username, IEnumerable<string> names, DateTime deletedAt)
        {
            try
            {
                string at = deletedAt.ToUniversalTime().ToString("yyyy-MM-dd HH:mm:ss", System.Globalization.CultureInfo.InvariantCulture);
                var lowered = names.Where(n => !string.IsNullOrWhiteSpace(n)).Select(n => n.ToLowerInvariant()).Distinct().ToList();
                using var connection = OpenConnection();
                using var cmd = connection.CreateCommand();
                var named = new List<string>();
                for (int i = 0; i < lowered.Count; i++)
                {
                    named.Add($"@n{i}");
                    cmd.Parameters.AddWithValue($"@n{i}", lowered[i]);
                }
                string nameClause = named.Count == 0 ? "" :
                    $" OR LOWER(display_name) IN ({string.Join(",", named)}) " +
                    $"OR LOWER(CASE WHEN json_valid(player_data) THEN json_extract(player_data, '$.player.name2') END) IN ({string.Join(",", named)})";
                cmd.CommandText = "SELECT EXISTS (SELECT 1 FROM players WHERE (LOWER(username) = LOWER(@u)" + nameClause + ") " +
                                  "AND player_data IS NOT NULL AND player_data != '{}' AND length(player_data) > 4 " +
                                  "AND (created_at >= @t OR last_login >= @t));";
                cmd.Parameters.AddWithValue("@u", username);
                cmd.Parameters.AddWithValue("@t", at);
                return Convert.ToInt64(cmd.ExecuteScalar()) != 0;
            }
            catch (Exception ex)
            {
                DebugLogger.Instance.LogWarning("SQL", $"WasRecreatedSince('{username}') failed: {ex.Message}");
                return true;
            }
        }

        /// <summary>v1.1.13: a queued purge that has run.</summary>
        public void RemovePendingPurge(long id)
        {
            try
            {
                using var connection = OpenConnection();
                using var cmd = connection.CreateCommand();
                cmd.CommandText = "DELETE FROM pending_purges WHERE id = @id;";
                cmd.Parameters.AddWithValue("@id", id);
                cmd.ExecuteNonQuery();
            }
            catch (Exception ex) { DebugLogger.Instance.LogError("SQL", $"RemovePendingPurge failed: {ex.Message}"); }
        }

        // v1.1.14: an admin command stays 'executing' while it runs; executed_at is empty then, and is stamped
        // when a restarted game server takes the stuck command over (TryClaimStuckAdminCommand)
        private const string StuckExecuting =
            "status = 'executing' AND ((executed_at IS NULL AND created_at < datetime('now', @age)) OR executed_at < datetime('now', @age))";

        /// <summary>
        /// v1.1.14: commands left 'executing' longer than olderThanSeconds: claimed by a game server that stopped
        /// before it marked them executed or failed (the web server then waits on them in vain).
        /// </summary>
        public List<AdminCommand> GetStuckAdminCommands(int olderThanSeconds)
        {
            var commands = new List<AdminCommand>();
            try
            {
                using var connection = OpenConnection();
                using var cmd = connection.CreateCommand();
                cmd.CommandText = "SELECT id, command, target_username, args FROM admin_commands WHERE " + StuckExecuting + " ORDER BY id LIMIT 20;";
                cmd.Parameters.AddWithValue("@age", $"-{olderThanSeconds} seconds");
                using var reader = cmd.ExecuteReader();
                while (reader.Read())
                    commands.Add(new AdminCommand
                    {
                        Id = reader.GetInt32(0),
                        Command = reader.GetString(1),
                        TargetUsername = reader.IsDBNull(2) ? null : reader.GetString(2),
                        Args = reader.IsDBNull(3) ? null : reader.GetString(3)
                    });
            }
            catch (Exception ex) { DebugLogger.Instance.LogError("SQL", $"GetStuckAdminCommands failed: {ex.Message}"); }
            return commands;
        }

        /// <summary>
        /// v1.1.14: take over a stuck command (see GetStuckAdminCommands) before recovering it: true when this
        /// call stamped it, so two recoveries never both run it. A recovery that stops too is taken over again
        /// once the stamp is older than olderThanSeconds.
        /// </summary>
        public bool TryClaimStuckAdminCommand(int id, int olderThanSeconds)
        {
            try
            {
                using var connection = OpenConnection();
                using var cmd = connection.CreateCommand();
                cmd.CommandText = "UPDATE admin_commands SET executed_at = datetime('now') WHERE id = @id AND " + StuckExecuting + ";";
                cmd.Parameters.AddWithValue("@id", id);
                cmd.Parameters.AddWithValue("@age", $"-{olderThanSeconds} seconds");
                return cmd.ExecuteNonQuery() == 1;
            }
            catch (Exception ex)
            {
                DebugLogger.Instance.LogError("SQL", $"TryClaimStuckAdminCommand failed: {ex.Message}");
                return false;
            }
        }

        /// <summary>
        /// v1.1.14: the archive row a delete of this account wrote at or after the admin command was queued (the
        /// delete landed), with the Name2 and character ID from the archived save; null when there is none.
        /// </summary>
        public (string? Name2, string? DisplayName, string? PlayerId, string DeletedAt)? GetArchivedDeleteForCommand(string username, int commandId)
        {
            try
            {
                using var connection = OpenConnection();
                using var cmd = connection.CreateCommand();
                cmd.CommandText = @"
                    SELECT CASE WHEN json_valid(player_data) THEN json_extract(player_data, '$.player.name2') END,
                           display_name,
                           CASE WHEN json_valid(player_data) THEN json_extract(player_data, '$.player.id') END,
                           deleted_at
                      FROM deleted_characters
                     WHERE LOWER(username) = LOWER(@u)
                       AND deleted_at >= (SELECT created_at FROM admin_commands WHERE id = @id)
                     ORDER BY id DESC LIMIT 1;";
                cmd.Parameters.AddWithValue("@u", username);
                cmd.Parameters.AddWithValue("@id", commandId);
                using var reader = cmd.ExecuteReader();
                if (!reader.Read()) return null;
                string? Text(int i) => reader.IsDBNull(i) ? null : Convert.ToString(reader.GetValue(i));
                return (Text(0), Text(1), Text(2), Text(3) ?? "");
            }
            catch (Exception ex)
            {
                DebugLogger.Instance.LogError("SQL", $"GetArchivedDeleteForCommand failed: {ex.Message}");
                return null;
            }
        }

        /// <summary>v1.1.14: the account still holds a character save (its delete has not run).</summary>
        public bool HasCharacterSave(string username)
        {
            try
            {
                using var connection = OpenConnection();
                using var cmd = connection.CreateCommand();
                cmd.CommandText = "SELECT EXISTS (SELECT 1 FROM players WHERE LOWER(username) = LOWER(@u) " +
                                  "AND player_data IS NOT NULL AND player_data != '{}' AND length(player_data) > 4);";
                cmd.Parameters.AddWithValue("@u", username);
                return Convert.ToInt64(cmd.ExecuteScalar()) != 0;
            }
            catch (Exception ex)
            {
                DebugLogger.Instance.LogError("SQL", $"HasCharacterSave failed: {ex.Message}");
                return false;
            }
        }

        /// <summary>v1.1.14: queue a world purge, as the web delete does when the game server is down.</summary>
        public void QueuePendingPurge(string username, string? name2, string? displayName, string deletedAt, string? playerId, string createdBy)
        {
            using var connection = OpenConnection();
            using var cmd = connection.CreateCommand();
            cmd.CommandText = "INSERT INTO pending_purges (username, name2, display_name, deleted_at, created_by, player_id) " +
                              "VALUES (@u, @n, @d, @at, @by, @pid);";
            cmd.Parameters.AddWithValue("@u", username);
            cmd.Parameters.AddWithValue("@n", (object?)name2 ?? DBNull.Value);
            cmd.Parameters.AddWithValue("@d", (object?)displayName ?? DBNull.Value);
            cmd.Parameters.AddWithValue("@at", deletedAt);
            cmd.Parameters.AddWithValue("@by", createdBy);
            cmd.Parameters.AddWithValue("@pid", (object?)playerId ?? DBNull.Value);
            cmd.ExecuteNonQuery();
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
    /// <summary>v1.1.13: a row of world_edits. Times are SQLite UTC text.</summary>
    public class WorldEdit
    {
        public long Id { get; set; }
        public string Kind { get; set; } = "";
        public string Payload { get; set; } = "";
        public string CreatedAt { get; set; } = "";
        public string CreatedBy { get; set; } = "";
        public string? AppliedAt { get; set; }
        public string? AppliedBy { get; set; }
    }

    public class AdminCommand
    {
        public int Id { get; set; }
        public string Command { get; set; } = "";
        public string? TargetUsername { get; set; }
        public string? Args { get; set; }
    }
}
