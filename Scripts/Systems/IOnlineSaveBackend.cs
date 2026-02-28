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
        /// Update display name in online_players (called when character loads with custom Name2).
        /// </summary>
        Task UpdateOnlineDisplayName(string username, string displayName);

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

        // === Divine System (God-Mortal Interactions) ===

        /// <summary>Get all immortal (ascended) players.</summary>
        Task<List<ImmortalPlayerInfo>> GetImmortalPlayers();

        /// <summary>Get mortal (non-immortal) players for divine deed targeting.</summary>
        Task<List<MortalPlayerInfo>> GetMortalPlayers(int limit = 30);

        /// <summary>Count mortal players who worship a specific god.</summary>
        Task<int> CountPlayerBelievers(string divineName);

        /// <summary>Atomically apply a divine blessing to an offline player.</summary>
        Task ApplyDivineBlessing(string username, int combats, float bonus);

        /// <summary>Atomically reduce an offline player's HP by a percentage (never kills).</summary>
        Task ApplyDivineSmite(string username, float damagePercent);

        /// <summary>Atomically set an offline player's WorshippedGod field.</summary>
        Task SetPlayerWorshippedGod(string username, string divineName);

        /// <summary>Add god experience to an immortal player identified by divine name.</summary>
        Task AddGodExperience(string divineName, long amount);

        /// <summary>Get an immortal god's boon configuration by their divine name.</summary>
        Task<string> GetGodBoonConfig(string divineName);

        /// <summary>Atomically update a god's boon configuration.</summary>
        Task SetGodBoonConfig(string username, string boonConfig);
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
        public bool IsRead { get; set; }
        public DateTime CreatedAt { get; set; }
    }

    /// <summary>
    /// Player team info for rankings and listings.
    /// </summary>
    public class PlayerTeamInfo
    {
        public string TeamName { get; set; } = "";
        public string CreatedBy { get; set; } = "";
        public int MemberCount { get; set; }
        public bool ControlsTurf { get; set; }
        public DateTime CreatedAt { get; set; }
    }

    /// <summary>
    /// Async trade offer between players.
    /// </summary>
    public class TradeOffer
    {
        public long Id { get; set; }
        public string FromPlayer { get; set; } = "";
        public string ToPlayer { get; set; } = "";
        public string ItemsJson { get; set; } = "[]";
        public long Gold { get; set; }
        public string Status { get; set; } = "pending";
        public string Message { get; set; } = "";
        public DateTime CreatedAt { get; set; }
        public string FromDisplayName { get; set; } = "";
        public string ToDisplayName { get; set; } = "";
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

    // ═══════════════════════════════════════════════════════════════════════
    // Bounties
    // ═══════════════════════════════════════════════════════════════════════

    public class BountyInfo
    {
        public int Id { get; set; }
        public string TargetPlayer { get; set; } = "";
        public string PlacedBy { get; set; } = "";
        public long Amount { get; set; }
        public DateTime PlacedAt { get; set; }
        public string Status { get; set; } = "active";
    }

    // ═══════════════════════════════════════════════════════════════════════
    // Auction House
    // ═══════════════════════════════════════════════════════════════════════

    public class AuctionListing
    {
        public int Id { get; set; }
        public string Seller { get; set; } = "";
        public string ItemName { get; set; } = "";
        public string ItemJson { get; set; } = "{}";
        public long Price { get; set; }
        public DateTime ListedAt { get; set; }
        public DateTime ExpiresAt { get; set; }
        public string? Buyer { get; set; }
        public string Status { get; set; } = "active";
        public bool GoldCollected { get; set; }
    }

    // ═══════════════════════════════════════════════════════════════════════
    // Team Wars
    // ═══════════════════════════════════════════════════════════════════════

    public class TeamWarInfo
    {
        public int Id { get; set; }
        public string ChallengerTeam { get; set; } = "";
        public string DefenderTeam { get; set; } = "";
        public string Status { get; set; } = "pending";
        public int ChallengerWins { get; set; }
        public int DefenderWins { get; set; }
        public long GoldWagered { get; set; }
        public DateTime StartedAt { get; set; }
        public DateTime? FinishedAt { get; set; }
    }

    // ═══════════════════════════════════════════════════════════════════════
    // World Bosses
    // ═══════════════════════════════════════════════════════════════════════

    public class WorldBossInfo
    {
        public int Id { get; set; }
        public string BossName { get; set; } = "";
        public int BossLevel { get; set; }
        public long MaxHP { get; set; }
        public long CurrentHP { get; set; }
        public DateTime StartedAt { get; set; }
        public DateTime ExpiresAt { get; set; }
        public string Status { get; set; } = "active";
        public string BossDataJson { get; set; } = "{}";
    }

    public class WorldBossDamageEntry
    {
        public string PlayerName { get; set; } = "";
        public long DamageDealt { get; set; }
        public int Hits { get; set; }
    }

    // ═══════════════════════════════════════════════════════════════════════
    // Castle Sieges
    // ═══════════════════════════════════════════════════════════════════════

    public class CastleSiegeInfo
    {
        public int Id { get; set; }
        public string TeamName { get; set; } = "";
        public int GuardsDefeated { get; set; }
        public int TotalGuards { get; set; }
        public string Result { get; set; } = "in_progress";
        public DateTime StartedAt { get; set; }
    }

    // ═══════════════════════════════════════════════════════════════════════
    // Team Headquarters
    // ═══════════════════════════════════════════════════════════════════════

    public class TeamUpgradeInfo
    {
        public string UpgradeType { get; set; } = "";
        public int Level { get; set; }
        public long InvestedGold { get; set; }
    }

    // ═══════════════════════════════════════════════════════════════════════
    // Divine System (God-Mortal Interactions)
    // ═══════════════════════════════════════════════════════════════════════

    public class ImmortalPlayerInfo
    {
        public string MortalName { get; set; } = "";
        public string Username { get; set; } = "";
        public string DivineName { get; set; } = "";
        public int GodLevel { get; set; }
        public long GodExperience { get; set; }
        public string GodAlignment { get; set; } = "";
        public bool IsOnline { get; set; }
        public string DivineBoonConfig { get; set; } = "";  // Configured boons for followers
    }

    public class MortalPlayerInfo
    {
        public string DisplayName { get; set; } = "";
        public string Username { get; set; } = "";
        public int Level { get; set; }
        public int ClassId { get; set; }
        public string WorshippedGod { get; set; } = "";
        public int BlessingCombats { get; set; }
        public long HP { get; set; }
        public long MaxHP { get; set; }
        public bool IsOnline { get; set; }
    }
}
