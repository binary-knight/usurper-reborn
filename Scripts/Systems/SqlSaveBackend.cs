using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Threading.Tasks;
using Microsoft.Data.Sqlite;

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

                    CREATE INDEX IF NOT EXISTS idx_news_created ON news(created_at DESC);
                    CREATE INDEX IF NOT EXISTS idx_messages_to ON messages(to_player, is_read);
                    CREATE INDEX IF NOT EXISTS idx_online_heartbeat ON online_players(last_heartbeat);
                    CREATE INDEX IF NOT EXISTS idx_pvp_attacker ON pvp_log(attacker, created_at);
                    CREATE INDEX IF NOT EXISTS idx_pvp_winner ON pvp_log(winner);
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

                // Clear world state
                using (var cmd = connection.CreateCommand())
                {
                    cmd.Transaction = transaction;
                    cmd.CommandText = "DELETE FROM world_state;";
                    await cmd.ExecuteNonQueryAsync();
                }

                // Clear news
                using (var cmd = connection.CreateCommand())
                {
                    cmd.Transaction = transaction;
                    cmd.CommandText = "DELETE FROM news;";
                    await cmd.ExecuteNonQueryAsync();
                }

                // Clear messages
                using (var cmd = connection.CreateCommand())
                {
                    cmd.Transaction = transaction;
                    cmd.CommandText = "DELETE FROM messages;";
                    await cmd.ExecuteNonQueryAsync();
                }

                // Clear online players (they'll re-register on next heartbeat)
                using (var cmd = connection.CreateCommand())
                {
                    cmd.Transaction = transaction;
                    cmd.CommandText = "DELETE FROM online_players;";
                    await cmd.ExecuteNonQueryAsync();
                }

                transaction.Commit();
                DebugLogger.Instance.LogWarning("SQL", "Full game reset performed by admin");
            }
            catch (Exception ex)
            {
                DebugLogger.Instance.LogError("SQL", $"Failed to perform full game reset: {ex.Message}");
                throw;
            }
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
    }

    /// <summary>
    /// Detailed player info for admin console.
    /// </summary>
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
}
