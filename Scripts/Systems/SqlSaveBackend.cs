using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Threading.Tasks;
using Microsoft.Data.Sqlite;
using UsurperRemake.Server;

namespace UsurperRemake.Systems
{
    /// <summary>
    /// SQLite-based save backend for online multiplayer mode.
    /// All players share a single SQLite database on the server.
    /// Implements both ISaveBackend (core save/load) and IOnlineSaveBackend (online features).
    /// Uses WAL mode for concurrent read/write safety.
    /// </summary>
    public class SqlSaveBackend : IOnlineSaveBackend
    {
        private readonly string databasePath;
        private readonly string connectionString;
        private readonly JsonSerializerOptions jsonOptions;

        public SqlSaveBackend(string databasePath)
        {
            this.databasePath = databasePath;

            // Ensure directory exists
            var dir = Path.GetDirectoryName(databasePath);
            if (!string.IsNullOrEmpty(dir))
                Directory.CreateDirectory(dir);

            connectionString = $"Data Source={databasePath}";

            jsonOptions = new JsonSerializerOptions
            {
                WriteIndented = false, // compact for database storage
                PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
                IncludeFields = true
            };

            InitializeDatabase();
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
                        ban_reason TEXT
                    );

                    CREATE TABLE IF NOT EXISTS world_state (
                        key TEXT PRIMARY KEY,
                        value TEXT NOT NULL,
                        version INTEGER DEFAULT 1,
                        updated_at TEXT DEFAULT (datetime('now')),
                        updated_by TEXT
                    );

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

                    CREATE TABLE IF NOT EXISTS sleeping_players (
                        username TEXT PRIMARY KEY,
                        sleep_location TEXT NOT NULL DEFAULT 'dormitory',
                        sleeping_since TEXT DEFAULT (datetime('now')),
                        is_dead INTEGER DEFAULT 0,
                        guards TEXT DEFAULT '[]',
                        inn_defense_boost INTEGER DEFAULT 0,
                        attack_log TEXT DEFAULT '[]'
                    );

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

            // Migration: add wizard_level column to existing players table
            try
            {
                using var migCmd = connection.CreateCommand();
                migCmd.CommandText = "ALTER TABLE players ADD COLUMN wizard_level INTEGER DEFAULT 0;";
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
                var json = JsonSerializer.Serialize(data, jsonOptions);
                var displayName = data.Player?.Name2 ?? data.Player?.Name1 ?? playerName;
                var normalizedUsername = playerName.ToLower();

                using var connection = OpenConnection();
                using var cmd = connection.CreateCommand();
                cmd.CommandText = @"
                    INSERT INTO players (username, display_name, player_data, last_login)
                    VALUES (@username, @displayName, @data, datetime('now'))
                    ON CONFLICT(username) DO UPDATE SET
                        display_name = @displayName,
                        player_data = @data,
                        last_login = datetime('now');
                ";
                cmd.Parameters.AddWithValue("@username", normalizedUsername);
                cmd.Parameters.AddWithValue("@displayName", displayName);
                cmd.Parameters.AddWithValue("@data", json);

                await cmd.ExecuteNonQueryAsync();
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

        public bool DeleteGameData(string playerName)
        {
            try
            {
                using var connection = OpenConnection();
                using var cmd = connection.CreateCommand();
                cmd.CommandText = "DELETE FROM players WHERE LOWER(username) = LOWER(@username);";
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

                // Verify we own the lock before deleting
                using var readCmd = connection.CreateCommand();
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
                            return; // Not our lock
                        }
                    }
                    catch { /* corrupt data, safe to delete */ }
                }

                using var deleteCmd = connection.CreateCommand();
                deleteCmd.CommandText = "DELETE FROM world_state WHERE key = @key;";
                deleteCmd.Parameters.AddWithValue("@key", WORLDSIM_LOCK_KEY);
                deleteCmd.ExecuteNonQuery();

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
            catch (Exception ex)
            {
                DebugLogger.Instance.LogError("SQL", $"Failed to add news: {ex.Message}");
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
        /// Prune all news categories by age, then enforce a global row cap.
        /// Keeps the most recent maxTotal entries regardless of category.
        /// </summary>
        public async Task PruneAllNews(int hoursToKeep = 48, int maxTotal = 500)
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

                // 2. Enforce global row cap — keep only the newest maxTotal entries
                using (var cmd = connection.CreateCommand())
                {
                    cmd.CommandText = @"
                        DELETE FROM news WHERE id NOT IN (
                            SELECT id FROM news ORDER BY created_at DESC LIMIT @maxTotal
                        );
                    ";
                    cmd.Parameters.AddWithValue("@maxTotal", maxTotal);
                    await cmd.ExecuteNonQueryAsync();
                }
            }
            catch (Exception ex)
            {
                DebugLogger.Instance.LogError("SQL", $"Failed to prune all news: {ex.Message}");
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
                      AND last_heartbeat >= datetime('now', '-120 seconds');
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

        public async Task RegisterOnline(string username, string displayName, string location, string connectionType = "Unknown")
        {
            try
            {
                using var connection = OpenConnection();
                using var cmd = connection.CreateCommand();
                cmd.CommandText = @"
                    INSERT INTO online_players (username, display_name, location, node_id, connection_type, connected_at, last_heartbeat)
                    VALUES (@username, @displayName, @location, @nodeId, @connectionType, datetime('now'), datetime('now'))
                    ON CONFLICT(username) DO UPDATE SET
                        display_name = @displayName,
                        location = @location,
                        node_id = @nodeId,
                        connection_type = @connectionType,
                        connected_at = datetime('now'),
                        last_heartbeat = datetime('now');
                ";
                cmd.Parameters.AddWithValue("@username", username);
                cmd.Parameters.AddWithValue("@displayName", displayName);
                cmd.Parameters.AddWithValue("@location", location);
                cmd.Parameters.AddWithValue("@nodeId", Environment.ProcessId.ToString());
                cmd.Parameters.AddWithValue("@connectionType", connectionType);
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

        public async Task UpdateHeartbeat(string username, string location)
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
                await cmd.ExecuteNonQueryAsync();
            }
            catch (Exception ex)
            {
                DebugLogger.Instance.LogError("SQL", $"Failed to update heartbeat: {ex.Message}");
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
                    WHERE last_heartbeat >= datetime('now', '-120 seconds')
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
                cmd.CommandText = "DELETE FROM online_players WHERE last_heartbeat < datetime('now', '-120 seconds');";
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
                    WHERE ((LOWER(to_player) = LOWER(@username) AND is_read = 0)
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
                cmd.CommandText = "UPDATE messages SET is_read = 1 WHERE LOWER(to_player) = LOWER(@username) AND is_read = 0;";
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
                        p.display_name,
                        json_extract(p.player_data, '$.player.level') as level,
                        json_extract(p.player_data, '$.player.class') as class_id,
                        json_extract(p.player_data, '$.player.experience') as xp,
                        CASE WHEN op.username IS NOT NULL THEN 1 ELSE 0 END as is_online
                    FROM players p
                    LEFT JOIN online_players op ON LOWER(p.username) = LOWER(op.username)
                        AND op.last_heartbeat >= datetime('now', '-120 seconds')
                    WHERE p.is_banned = 0
                        AND p.player_data != '{}'
                        AND LENGTH(p.player_data) > 2
                        AND json_extract(p.player_data, '$.player.level') IS NOT NULL
                        AND p.username NOT LIKE 'emergency_%';
                ";

                using var reader = await cmd.ExecuteReaderAsync();
                while (await reader.ReadAsync())
                {
                    summaries.Add(new PlayerSummary
                    {
                        DisplayName = reader.GetString(0),
                        Level = reader.IsDBNull(1) ? 1 : Convert.ToInt32(reader.GetValue(1)),
                        ClassId = reader.IsDBNull(2) ? 0 : Convert.ToInt32(reader.GetValue(2)),
                        Experience = reader.IsDBNull(3) ? 0 : Convert.ToInt64(reader.GetValue(3)),
                        IsOnline = reader.GetInt32(4) == 1
                    });
                }
            }
            catch (Exception ex)
            {
                DebugLogger.Instance.LogError("SQL", $"Failed to get player summaries: {ex.Message}");
            }
            return summaries;
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

                DebugLogger.Instance.LogInfo("SQL", $"Player '{username}' banned: {reason}");
            }
            catch (Exception ex)
            {
                DebugLogger.Instance.LogError("SQL", $"Failed to ban player: {ex.Message}");
            }
        }

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
                DebugLogger.Instance.LogInfo("SQL", $"Player '{username}' unbanned by admin");
            }
            catch (Exception ex)
            {
                DebugLogger.Instance.LogError("SQL", $"Failed to unban player: {ex.Message}");
            }
        }

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
                        AND op.last_heartbeat >= datetime('now', '-120 seconds')
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
                            AND op.last_heartbeat >= datetime('now', '-120 seconds');
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

        public async Task UpdatePlayerSession(string username, bool isLogin)
        {
            try
            {
                using var connection = OpenConnection();
                using var cmd = connection.CreateCommand();

                if (isLogin)
                {
                    cmd.CommandText = "UPDATE players SET last_login = datetime('now') WHERE LOWER(username) = LOWER(@username);";
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
        public async Task<(bool success, string message)> RegisterPlayer(string username, string password)
        {
            try
            {
                if (string.IsNullOrWhiteSpace(username) || username.Length < 2 || username.Length > 20)
                    return (false, "Username must be 2-20 characters.");

                if (password.Length < 4)
                    return (false, "Password must be at least 4 characters.");

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

                // Insert the new player with hashed password and empty player data
                string passwordHash = HashPassword(password);
                using var insertCmd = connection.CreateCommand();
                insertCmd.CommandText = @"
                    INSERT INTO players (username, display_name, password_hash, player_data, created_at)
                    VALUES (@username, @display_name, @password_hash, '{}', datetime('now'));
                ";
                insertCmd.Parameters.AddWithValue("@username", username.ToLower());
                insertCmd.Parameters.AddWithValue("@display_name", username);
                insertCmd.Parameters.AddWithValue("@password_hash", passwordHash);
                await insertCmd.ExecuteNonQueryAsync();

                DebugLogger.Instance.LogInfo("SQL", $"New player registered: '{username}'");
                return (true, "Account created successfully!");
            }
            catch (Exception ex)
            {
                DebugLogger.Instance.LogError("SQL", $"Failed to register player: {ex.Message}");
                return (false, "Registration failed. Please try again.");
            }
        }

        /// <summary>
        /// Authenticate a player. Returns (success, displayName, message).
        /// </summary>
        public async Task<(bool success, string displayName, string message)> AuthenticatePlayer(string username, string password)
        {
            try
            {
                using var connection = OpenConnection();
                using var cmd = connection.CreateCommand();
                cmd.CommandText = "SELECT display_name, password_hash, is_banned, ban_reason FROM players WHERE LOWER(username) = LOWER(@username);";
                cmd.Parameters.AddWithValue("@username", username);

                using var reader = await cmd.ExecuteReaderAsync();
                if (!await reader.ReadAsync())
                    return (false, "", "Unknown username. Type 'R' to register a new account.");

                string displayName = reader.GetString(0);
                string storedHash = reader.GetString(1);
                bool isBanned = reader.GetInt32(2) != 0;
                string? banReason = reader.IsDBNull(3) ? null : reader.GetString(3);

                if (isBanned)
                {
                    string msg = "Your account has been banned.";
                    if (!string.IsNullOrEmpty(banReason))
                        msg += $" Reason: {banReason}";
                    return (false, "", msg);
                }

                if (!VerifyPassword(password, storedHash))
                    return (false, "", "Incorrect password.");

                return (true, displayName, "Login successful!");
            }
            catch (Exception ex)
            {
                DebugLogger.Instance.LogError("SQL", $"Failed to authenticate player: {ex.Message}");
                return (false, "", "Authentication failed. Please try again.");
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
                var (authenticated, _, _) = await AuthenticatePlayer(username, oldPassword);
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
                    WHERE LOWER(username) = LOWER(@username)
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
                    WHERE LOWER(username) = LOWER(@username)
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
                    WHERE LOWER(username) = LOWER(@username)
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

    public bool PlayerExists(string username)
    {
        try
        {
            using var connection = OpenConnection();
            using var cmd = connection.CreateCommand();
            cmd.CommandText = "SELECT COUNT(*) FROM players WHERE LOWER(username) = LOWER(@username) AND is_banned = 0;";
            cmd.Parameters.AddWithValue("@username", username);
            return Convert.ToInt32(cmd.ExecuteScalar()) > 0;
        }
        catch { return false; }
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

    public async Task UpdateTradeOfferStatus(long offerId, string status)
    {
        try
        {
            using var connection = OpenConnection();
            using var cmd = connection.CreateCommand();
            cmd.CommandText = "UPDATE trade_offers SET status = @status, resolved_at = datetime('now') WHERE id = @id;";
            cmd.Parameters.AddWithValue("@id", offerId);
            cmd.Parameters.AddWithValue("@status", status);
            await Task.Run(() => cmd.ExecuteNonQuery());
        }
        catch (Exception ex)
        {
            DebugLogger.Instance.LogError("SQL", $"Failed to update trade offer {offerId}: {ex.Message}");
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
            // Get expired offers to return gold
            using var selectCmd = connection.CreateCommand();
            selectCmd.CommandText = @"
                SELECT id, from_player, gold FROM trade_offers
                WHERE status = 'pending' AND created_at < datetime('now', '-7 days');
            ";
            var expiredOffers = new List<(long id, string fromPlayer, long gold)>();
            using (var reader = await Task.Run(() => selectCmd.ExecuteReader()))
            {
                while (reader.Read())
                {
                    expiredOffers.Add((reader.GetInt64(0), reader.GetString(1), reader.GetInt64(2)));
                }
            }

            // Mark expired and return gold
            foreach (var (id, fromPlayer, gold) in expiredOffers)
            {
                await UpdateTradeOfferStatus(id, "expired");
                if (gold > 0) await AddGoldToPlayer(fromPlayer, gold);
                await SendMessage("System", fromPlayer, "trade", $"Your trade package expired and {gold:N0} gold was returned.");
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
            string col = challengerWon ? "challenger_wins" : "defender_wins";
            cmd.CommandText = $"UPDATE team_wars SET {col} = {col} + 1 WHERE id = @id;";
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

    public async Task<int> SpawnWorldBoss(string bossName, int bossLevel, long maxHp, int hoursToExpire = 24)
    {
        try
        {
            using var connection = OpenConnection();
            using var cmd = connection.CreateCommand();
            cmd.CommandText = @"INSERT INTO world_bosses (boss_name, boss_level, max_hp, current_hp, expires_at)
                                VALUES (@name, @level, @hp, @hp, datetime('now', '+' || @hours || ' hours'))
                                RETURNING id;";
            cmd.Parameters.AddWithValue("@name", bossName);
            cmd.Parameters.AddWithValue("@level", bossLevel);
            cmd.Parameters.AddWithValue("@hp", maxHp);
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
            cmd.CommandText = @"SELECT id, boss_name, boss_level, max_hp, current_hp, started_at, expires_at
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
                    ExpiresAt = DateTime.TryParse(reader.GetString(6), out var et) ? et : DateTime.Now
                };
            }
        }
        catch (Exception ex) { DebugLogger.Instance.LogError("SQL", $"Failed to get world boss: {ex.Message}"); }
        return null;
    }

    public async Task<long> RecordWorldBossDamage(int bossId, string playerName, long damage)
    {
        long remainingHp = 0;
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

            // If dead, mark defeated
            if (remainingHp <= 0)
            {
                using var defeatCmd = connection.CreateCommand();
                defeatCmd.Transaction = transaction;
                defeatCmd.CommandText = @"UPDATE world_bosses SET status = 'defeated' WHERE id = @id;";
                defeatCmd.Parameters.AddWithValue("@id", bossId);
                await defeatCmd.ExecuteNonQueryAsync();
            }

            transaction.Commit();
        }
        catch (Exception ex) { DebugLogger.Instance.LogError("SQL", $"Failed to record world boss damage: {ex.Message}"); }
        return remainingHp;
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
}
