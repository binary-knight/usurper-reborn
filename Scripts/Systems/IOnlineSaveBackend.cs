using System;
using System.Collections.Generic;
using System.Threading.Tasks;

namespace UsurperRemake.Systems
{
    /// <summary>
    /// Extended save backend interface for online multiplayer mode.
    /// Adds shared world state, online player tracking, news, and messaging.
    /// Implemented by SqlSaveBackend for centralized SQLite server.
    /// </summary>
    public interface IOnlineSaveBackend : ISaveBackend
    {
        // === World State (shared across all players) ===

        /// <summary>
        /// Save a shared world state value (king, economy, NPCs, events, etc.)
        /// Uses optimistic locking via version numbers for concurrent writes.
        /// </summary>
        Task SaveWorldState(string key, string jsonValue);

        /// <summary>
        /// Load a shared world state value by key.
        /// Returns null if key doesn't exist.
        /// </summary>
        Task<string?> LoadWorldState(string key);

        /// <summary>
        /// Atomically update a world state value using a transform function.
        /// Reads current value, applies transform, writes back - all in one transaction.
        /// Returns false if another player modified the value concurrently.
        /// </summary>
        Task<bool> TryAtomicUpdate(string key, Func<string, string> transform);

        // === News ===

        /// <summary>
        /// Add a news entry visible to all players.
        /// </summary>
        Task AddNews(string message, string category, string playerName);

        /// <summary>
        /// Get recent news entries, newest first.
        /// </summary>
        Task<List<NewsEntry>> GetRecentNews(int count = 20);

        // === Online Player Tracking ===

        /// <summary>
        /// Register this player as online (called on connect).
        /// </summary>
        Task RegisterOnline(string username, string displayName, string location, string connectionType = "Unknown");

        /// <summary>
        /// Update heartbeat and current location (called every 30s).
        /// </summary>
        Task UpdateHeartbeat(string username, string location);

        /// <summary>
        /// Unregister this player (called on disconnect/logout).
        /// </summary>
        Task UnregisterOnline(string username);

        /// <summary>
        /// Get list of currently online players.
        /// Excludes stale entries (no heartbeat for 120+ seconds).
        /// </summary>
        Task<List<OnlinePlayerInfo>> GetOnlinePlayers();

        /// <summary>
        /// Clean up stale online entries (no heartbeat for 120+ seconds).
        /// Called periodically by the server.
        /// </summary>
        Task CleanupStaleOnlinePlayers();

        // === Messaging ===

        /// <summary>
        /// Send a message to another player (or '*' for broadcast).
        /// </summary>
        Task SendMessage(string from, string to, string messageType, string message);

        /// <summary>
        /// Get unread messages for a player (newer than afterMessageId to avoid re-fetching broadcasts).
        /// </summary>
        Task<List<PlayerMessage>> GetUnreadMessages(string username, long afterMessageId = 0);

        /// <summary>
        /// Mark all messages for a player as read.
        /// </summary>
        Task MarkMessagesRead(string username);

        /// <summary>
        /// Get the highest message ID in the database (for initializing watermark on connect).
        /// </summary>
        Task<long> GetMaxMessageId();

        // === Player Management ===

        /// <summary>
        /// Get summary info for all players (for Hall of Fame leaderboard).
        /// Returns display name, level, class, experience extracted from saved JSON.
        /// </summary>
        Task<List<PlayerSummary>> GetAllPlayerSummaries();

        /// <summary>
        /// Check if a player account is banned.
        /// </summary>
        Task<bool> IsPlayerBanned(string username);

        /// <summary>
        /// Ban a player with a reason.
        /// </summary>
        Task BanPlayer(string username, string reason);

        /// <summary>
        /// Unban a previously banned player.
        /// </summary>
        Task UnbanPlayer(string username);

        /// <summary>
        /// Update player login/logout timestamps and playtime.
        /// </summary>
        Task UpdatePlayerSession(string username, bool isLogin);
    }

    /// <summary>
    /// News entry from the shared news table.
    /// </summary>
    public class NewsEntry
    {
        public int Id { get; set; }
        public string Message { get; set; } = "";
        public string Category { get; set; } = "";
        public string PlayerName { get; set; } = "";
        public DateTime CreatedAt { get; set; }
    }

    /// <summary>
    /// Info about a currently online player.
    /// </summary>
    public class OnlinePlayerInfo
    {
        public string Username { get; set; } = "";
        public string DisplayName { get; set; } = "";
        public string Location { get; set; } = "";
        public string ConnectionType { get; set; } = "Unknown";
        public DateTime ConnectedAt { get; set; }
        public DateTime LastHeartbeat { get; set; }
    }

    /// <summary>
    /// Summary info for a player (for leaderboard/Hall of Fame).
    /// </summary>
    public class PlayerSummary
    {
        public string DisplayName { get; set; } = "";
        public int Level { get; set; }
        public int ClassId { get; set; }
        public long Experience { get; set; }
        public bool IsOnline { get; set; }
    }

    /// <summary>
    /// Inter-player message.
    /// </summary>
    public class PlayerMessage
    {
        public int Id { get; set; }
        public string FromPlayer { get; set; } = "";
        public string ToPlayer { get; set; } = "";
        public string MessageType { get; set; } = "";
        public string Message { get; set; } = "";
        public DateTime CreatedAt { get; set; }
    }

    /// <summary>
    /// PvP leaderboard entry.
    /// </summary>
    public class PvPLeaderboardEntry
    {
        public int Rank { get; set; }
        public string Username { get; set; } = "";
        public string DisplayName { get; set; } = "";
        public int Level { get; set; }
        public int ClassId { get; set; }
        public int Wins { get; set; }
        public int Losses { get; set; }
        public long TotalGoldStolen { get; set; }
    }

    /// <summary>
    /// PvP combat log entry.
    /// </summary>
    public class PvPLogEntry
    {
        public string AttackerName { get; set; } = "";
        public string DefenderName { get; set; } = "";
        public string WinnerUsername { get; set; } = "";
        public long GoldStolen { get; set; }
        public int AttackerLevel { get; set; }
        public int DefenderLevel { get; set; }
        public DateTime CreatedAt { get; set; }
    }
}
