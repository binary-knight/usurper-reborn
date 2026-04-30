using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using Microsoft.Data.Sqlite;
using System.Text.Json;

namespace UsurperRemake.Systems;

/// <summary>
/// Guild system for online multiplayer mode (v0.52.0).
/// Provides guild creation, membership management, guild bank (gold + items), rank system, and shared perks.
/// Guild data is stored in SQLite tables for persistence across sessions.
///
/// Ranks (highest to lowest):
///   Leader  — full control (invite, kick, promote/demote, withdraw gold/items, set ranks)
///   Officer — can invite, withdraw items, withdraw gold (up to 50k per withdrawal)
///   Member  — deposit gold/items only, no withdrawals
///
/// Chat commands:
///   /guild            — show guild info (your guild)
///   /gcreate name     — create a guild (costs 10,000 gold)
///   /ginvite player   — invite a player (Leader/Officer)
///   /gleave           — leave your current guild
///   /gkick player     — kick a member (Leader only)
///   /ginfo name       — view info about any guild
///   /gc message       — guild chat (members only)
///   /gbank deposit N  — deposit gold into guild bank
///   /gbank withdraw N — withdraw gold (Leader/Officer)
///   /gbank items      — view guild bank items
///   /gdeposit         — deposit an item from inventory into guild bank
///   /gwithdraw N      — withdraw item #N from guild bank (Leader/Officer)
///   /grank player R   — set a member's rank (Leader only)
/// </summary>
public class GuildSystem
{
    public static GuildSystem? Instance { get; private set; }

    private readonly string connectionString;

    // Cache of guild membership: username -> guild name (lowercased)
    private readonly ConcurrentDictionary<string, string> membershipCache = new(StringComparer.OrdinalIgnoreCase);

    // Cache of guild display names: lowercased guild name -> display name
    private readonly ConcurrentDictionary<string, string> guildDisplayNameCache = new(StringComparer.OrdinalIgnoreCase);

    // Pending guild invites: target username -> (guild name, inviter name, expiry)
    private readonly ConcurrentDictionary<string, GuildInvite> pendingInvites = new(StringComparer.OrdinalIgnoreCase);

    public const int GuildCreationCost = 10000;
    public const int MaxGuildMembers = 20;
    public const double GuildXPBonusPerMember = 0.02; // 2% per member
    public const double MaxGuildXPBonus = 0.10; // 10% cap
    public const long OfficerGoldWithdrawLimit = 50000; // Officers can withdraw up to 50k per transaction
    public const int MaxBankItems = 50; // Maximum items in guild bank

    // Rank hierarchy: Leader > Officer > Member
    public static readonly string[] RankHierarchy = { "Leader", "Officer", "Member" };

    /// <summary>
    /// Check if a rank can perform a given action.
    /// </summary>
    public static bool RankCanInvite(string rank) => rank == "Leader" || rank == "Officer";
    public static bool RankCanWithdraw(string rank) => rank == "Leader" || rank == "Officer";
    public static bool RankCanKick(string rank) => rank == "Leader";
    public static bool RankCanPromote(string rank) => rank == "Leader";

    public GuildSystem(string databasePath)
    {
        connectionString = $"Data Source={databasePath}";
        InitializeTables();
        LoadMembershipCache();
        Instance = this;
    }

    private void InitializeTables()
    {
        try
        {
            using var conn = new SqliteConnection(connectionString);
            conn.Open();

            using var cmd = conn.CreateCommand();
            cmd.CommandText = @"
                CREATE TABLE IF NOT EXISTS guilds (
                    name TEXT PRIMARY KEY COLLATE NOCASE,
                    display_name TEXT NOT NULL,
                    motto TEXT DEFAULT '',
                    leader_username TEXT NOT NULL,
                    bank_gold INTEGER DEFAULT 0,
                    created_at TEXT DEFAULT (datetime('now'))
                );

                CREATE TABLE IF NOT EXISTS guild_members (
                    username TEXT PRIMARY KEY COLLATE NOCASE,
                    guild_name TEXT NOT NULL COLLATE NOCASE,
                    rank TEXT NOT NULL DEFAULT 'Member',
                    joined_at TEXT DEFAULT (datetime('now')),
                    FOREIGN KEY (guild_name) REFERENCES guilds(name)
                );

                CREATE INDEX IF NOT EXISTS idx_guild_members_guild ON guild_members(guild_name);

                CREATE TABLE IF NOT EXISTS guild_bank_items (
                    id INTEGER PRIMARY KEY AUTOINCREMENT,
                    guild_name TEXT NOT NULL COLLATE NOCASE,
                    item_name TEXT NOT NULL,
                    item_json TEXT NOT NULL,
                    deposited_by TEXT NOT NULL,
                    deposited_at TEXT DEFAULT (datetime('now')),
                    FOREIGN KEY (guild_name) REFERENCES guilds(name)
                );

                CREATE INDEX IF NOT EXISTS idx_guild_bank_items_guild ON guild_bank_items(guild_name);
            ";
            cmd.ExecuteNonQuery();
        }
        catch (Exception ex)
        {
            DebugLogger.Instance?.LogError("GUILD", $"Failed to initialize guild tables: {ex.Message}");
        }
    }

    private void LoadMembershipCache()
    {
        try
        {
            using var conn = new SqliteConnection(connectionString);
            conn.Open();

            using var cmd = conn.CreateCommand();
            cmd.CommandText = "SELECT username, guild_name FROM guild_members";
            using var reader = cmd.ExecuteReader();
            while (reader.Read())
            {
                membershipCache[reader.GetString(0)] = reader.GetString(1);
            }

            // Load guild display names
            using var dnCmd = conn.CreateCommand();
            dnCmd.CommandText = "SELECT name, display_name FROM guilds";
            using var dnReader = dnCmd.ExecuteReader();
            while (dnReader.Read())
            {
                guildDisplayNameCache[dnReader.GetString(0)] = dnReader.GetString(1);
            }
        }
        catch (Exception ex)
        {
            DebugLogger.Instance?.LogError("GUILD", $"Failed to load membership cache: {ex.Message}");
        }
    }

    /// <summary>
    /// Get the guild name for a player, or null if not in a guild.
    /// </summary>
    public string? GetPlayerGuild(string username)
    {
        return membershipCache.TryGetValue(username, out var guild) ? guild : null;
    }

    /// <summary>
    /// Get the display name of a player's guild, or null if not in a guild.
    /// </summary>
    public string? GetPlayerGuildDisplayName(string username)
    {
        var guildKey = GetPlayerGuild(username);
        if (guildKey == null) return null;
        return guildDisplayNameCache.TryGetValue(guildKey, out var displayName) ? displayName : guildKey;
    }

    /// <summary>
    /// Get the XP bonus multiplier for a player based on their guild size.
    /// Returns 1.0 if not in a guild.
    /// </summary>
    public double GetGuildXPMultiplier(string username)
    {
        var guildName = GetPlayerGuild(username);
        if (guildName == null) return 1.0;

        int memberCount = GetMemberCount(guildName);
        double bonus = Math.Min(memberCount * GuildXPBonusPerMember, MaxGuildXPBonus);
        return 1.0 + bonus;
    }

    /// <summary>
    /// Create a new guild. Returns null on success, error message on failure.
    /// </summary>
    public string? CreateGuild(string leaderUsername, string guildName, string displayName)
    {
        if (string.IsNullOrWhiteSpace(guildName) || guildName.Length < 2 || guildName.Length > 30)
            return "Guild name must be 2-30 characters.";

        if (GetPlayerGuild(leaderUsername) != null)
            return "You are already in a guild. Leave first with /gleave.";

        // v0.60.0: gods don't create or join guilds (parity with TeamSystem and the
        // AddMember filter below). An ascended player is venerated, not enrolled.
        var ctxCreate = UsurperRemake.Server.SessionContext.Current;
        if (ctxCreate?.Player != null && string.Equals(ctxCreate.Username, leaderUsername, StringComparison.OrdinalIgnoreCase) && ctxCreate.Player.IsImmortal)
            return "Gods do not lead guilds.";

        try
        {
            using var conn = new SqliteConnection(connectionString);
            conn.Open();

            // Check if guild name is taken
            using (var checkCmd = conn.CreateCommand())
            {
                checkCmd.CommandText = "SELECT COUNT(*) FROM guilds WHERE name = @name COLLATE NOCASE";
                checkCmd.Parameters.AddWithValue("@name", guildName);
                if (Convert.ToInt64(checkCmd.ExecuteScalar()) > 0)
                    return $"A guild named '{guildName}' already exists.";
            }

            // Create guild
            using (var createCmd = conn.CreateCommand())
            {
                createCmd.CommandText = @"
                    INSERT INTO guilds (name, display_name, leader_username)
                    VALUES (@name, @display, @leader)";
                createCmd.Parameters.AddWithValue("@name", guildName.ToLowerInvariant());
                createCmd.Parameters.AddWithValue("@display", displayName);
                createCmd.Parameters.AddWithValue("@leader", leaderUsername.ToLowerInvariant());
                createCmd.ExecuteNonQuery();
            }

            // Add leader as first member
            using (var memberCmd = conn.CreateCommand())
            {
                memberCmd.CommandText = @"
                    INSERT INTO guild_members (username, guild_name, rank)
                    VALUES (@user, @guild, 'Leader')";
                memberCmd.Parameters.AddWithValue("@user", leaderUsername.ToLowerInvariant());
                memberCmd.Parameters.AddWithValue("@guild", guildName.ToLowerInvariant());
                memberCmd.ExecuteNonQuery();
            }

            membershipCache[leaderUsername] = guildName.ToLowerInvariant();
            guildDisplayNameCache[guildName.ToLowerInvariant()] = displayName;
            return null; // success
        }
        catch (Exception ex)
        {
            DebugLogger.Instance?.LogError("GUILD", $"Failed to create guild: {ex.Message}");
            return "Failed to create guild. Please try again.";
        }
    }

    /// <summary>
    /// Add a member to a guild. Returns null on success, error message on failure.
    /// </summary>
    public string? AddMember(string username, string guildName)
    {
        if (GetPlayerGuild(username) != null)
            return "That player is already in a guild.";

        // v0.60.0: gods-not-recruitable parity with TeamSystem.IsRecruitable. An ascended
        // player is venerated, not joined. Without this filter, an immortal could accept
        // a guild invite (or be added by the leader) and would appear on the guild
        // roster -- mortal players could then "have a god in our guild" which breaks
        // the ascension narrative the same way "have a god on our team" did. We check
        // via the live SessionContext player object since GuildSystem is keyed on
        // username (account-level), not on a Character reference.
        var ctx = UsurperRemake.Server.SessionContext.Current;
        if (ctx?.Player != null && string.Equals(ctx.Username, username, StringComparison.OrdinalIgnoreCase) && ctx.Player.IsImmortal)
            return "Gods do not join guilds.";

        int count = GetMemberCount(guildName);
        if (count >= MaxGuildMembers)
            return "Guild is full.";

        try
        {
            using var conn = new SqliteConnection(connectionString);
            conn.Open();

            using var cmd = conn.CreateCommand();
            cmd.CommandText = @"
                INSERT OR IGNORE INTO guild_members (username, guild_name, rank)
                VALUES (@user, @guild, 'Member')";
            cmd.Parameters.AddWithValue("@user", username.ToLowerInvariant());
            cmd.Parameters.AddWithValue("@guild", guildName.ToLowerInvariant());
            cmd.ExecuteNonQuery();

            membershipCache[username] = guildName.ToLowerInvariant();
            return null;
        }
        catch (Exception ex)
        {
            DebugLogger.Instance?.LogError("GUILD", $"Failed to add member: {ex.Message}");
            return "Failed to add member.";
        }
    }

    /// <summary>
    /// Remove a member from their guild.
    /// </summary>
    public string? RemoveMember(string username)
    {
        var guildName = GetPlayerGuild(username);
        if (guildName == null)
            return "Not in a guild.";

        try
        {
            using var conn = new SqliteConnection(connectionString);
            conn.Open();

            // Check if they're the leader
            bool isLeader;
            using (var checkCmd = conn.CreateCommand())
            {
                checkCmd.CommandText = "SELECT leader_username FROM guilds WHERE name = @guild";
                checkCmd.Parameters.AddWithValue("@guild", guildName);
                var leader = checkCmd.ExecuteScalar()?.ToString();
                isLeader = string.Equals(leader, username, StringComparison.OrdinalIgnoreCase);
            }

            // Remove member
            using (var delCmd = conn.CreateCommand())
            {
                delCmd.CommandText = "DELETE FROM guild_members WHERE username = @user COLLATE NOCASE";
                delCmd.Parameters.AddWithValue("@user", username.ToLowerInvariant());
                delCmd.ExecuteNonQuery();
            }

            membershipCache.TryRemove(username, out _);

            // If leader left, promote next member or disband
            if (isLeader)
            {
                // Prefer promoting an Officer, otherwise oldest member
                using var nextCmd = conn.CreateCommand();
                nextCmd.CommandText = @"SELECT username FROM guild_members WHERE guild_name = @guild
                    ORDER BY CASE rank WHEN 'Officer' THEN 0 ELSE 1 END, joined_at ASC LIMIT 1";
                nextCmd.Parameters.AddWithValue("@guild", guildName);
                var nextMember = nextCmd.ExecuteScalar()?.ToString();

                if (nextMember != null)
                {
                    // Promote next member to leader
                    using var promoteCmd = conn.CreateCommand();
                    promoteCmd.CommandText = @"
                        UPDATE guilds SET leader_username = @user WHERE name = @guild;
                        UPDATE guild_members SET rank = 'Leader' WHERE username = @user AND guild_name = @guild";
                    promoteCmd.Parameters.AddWithValue("@user", nextMember);
                    promoteCmd.Parameters.AddWithValue("@guild", guildName);
                    promoteCmd.ExecuteNonQuery();
                }
                else
                {
                    // No members left — disband (clean up bank items too)
                    using var cleanupCmd = conn.CreateCommand();
                    cleanupCmd.CommandText = "DELETE FROM guild_bank_items WHERE guild_name = @guild";
                    cleanupCmd.Parameters.AddWithValue("@guild", guildName);
                    cleanupCmd.ExecuteNonQuery();

                    using var disbandCmd = conn.CreateCommand();
                    disbandCmd.CommandText = "DELETE FROM guilds WHERE name = @guild";
                    disbandCmd.Parameters.AddWithValue("@guild", guildName);
                    disbandCmd.ExecuteNonQuery();
                }
            }

            return null;
        }
        catch (Exception ex)
        {
            DebugLogger.Instance?.LogError("GUILD", $"Failed to remove member: {ex.Message}");
            return "Failed to leave guild.";
        }
    }

    /// <summary>
    /// Deposit gold into the guild bank.
    /// </summary>
    public string? DepositGold(string username, long amount)
    {
        var guildName = GetPlayerGuild(username);
        if (guildName == null)
            return "Not in a guild.";
        if (amount <= 0)
            return "Amount must be positive.";

        try
        {
            using var conn = new SqliteConnection(connectionString);
            conn.Open();

            using var cmd = conn.CreateCommand();
            cmd.CommandText = "UPDATE guilds SET bank_gold = bank_gold + @amount WHERE name = @guild";
            cmd.Parameters.AddWithValue("@amount", amount);
            cmd.Parameters.AddWithValue("@guild", guildName);
            cmd.ExecuteNonQuery();
            return null;
        }
        catch (Exception ex)
        {
            DebugLogger.Instance?.LogError("GUILD", $"Failed to deposit: {ex.Message}");
            return "Failed to deposit gold.";
        }
    }

    /// <summary>
    /// Get guild info for display.
    /// </summary>
    public GuildInfo? GetGuildInfo(string guildName)
    {
        try
        {
            using var conn = new SqliteConnection(connectionString);
            conn.Open();

            using var cmd = conn.CreateCommand();
            cmd.CommandText = @"
                SELECT g.display_name, g.motto, g.leader_username, g.bank_gold, g.created_at,
                       (SELECT COUNT(*) FROM guild_members WHERE guild_name = g.name) as member_count
                FROM guilds g WHERE g.name = @name COLLATE NOCASE";
            cmd.Parameters.AddWithValue("@name", guildName);
            using var reader = cmd.ExecuteReader();
            if (!reader.Read()) return null;

            var info = new GuildInfo
            {
                Name = guildName,
                DisplayName = reader.GetString(0),
                Motto = reader.IsDBNull(1) ? "" : reader.GetString(1),
                LeaderUsername = reader.GetString(2),
                BankGold = reader.GetInt64(3),
                CreatedAt = reader.GetString(4),
                MemberCount = reader.GetInt32(5)
            };

            // Load members
            using var membersCmd = conn.CreateCommand();
            membersCmd.CommandText = @"
                SELECT gm.username, gm.rank, p.display_name
                FROM guild_members gm
                LEFT JOIN players p ON gm.username = p.username COLLATE NOCASE
                WHERE gm.guild_name = @guild
                ORDER BY CASE gm.rank WHEN 'Leader' THEN 0 WHEN 'Officer' THEN 1 ELSE 2 END, gm.joined_at ASC";
            membersCmd.Parameters.AddWithValue("@guild", guildName);
            using var mReader = membersCmd.ExecuteReader();
            info.Members = new List<GuildMemberInfo>();
            while (mReader.Read())
            {
                info.Members.Add(new GuildMemberInfo
                {
                    Username = mReader.GetString(0),
                    Rank = mReader.GetString(1),
                    DisplayName = mReader.IsDBNull(2) ? mReader.GetString(0) : mReader.GetString(2)
                });
            }

            return info;
        }
        catch (Exception ex)
        {
            DebugLogger.Instance?.LogError("GUILD", $"Failed to get guild info: {ex.Message}");
            return null;
        }
    }

    /// <summary>
    /// Get all guilds ordered by member count (descending), then bank gold.
    /// Used by the Guild Board on Main Street.
    /// </summary>
    public List<GuildInfo> GetAllGuilds()
    {
        var guilds = new List<GuildInfo>();
        try
        {
            using var conn = new SqliteConnection(connectionString);
            conn.Open();

            using var cmd = conn.CreateCommand();
            cmd.CommandText = @"
                SELECT g.name, g.display_name, g.motto, g.leader_username, g.bank_gold, g.created_at,
                       (SELECT COUNT(*) FROM guild_members WHERE guild_name = g.name) as member_count,
                       p.display_name as leader_display
                FROM guilds g
                LEFT JOIN players p ON g.leader_username = p.username COLLATE NOCASE
                ORDER BY member_count DESC, g.bank_gold DESC";
            using var reader = cmd.ExecuteReader();
            while (reader.Read())
            {
                string leaderLogin = reader.GetString(3);
                guilds.Add(new GuildInfo
                {
                    Name = reader.GetString(0),
                    DisplayName = reader.GetString(1),
                    Motto = reader.IsDBNull(2) ? "" : reader.GetString(2),
                    LeaderUsername = reader.GetString(3),
                    LeaderDisplayName = reader.IsDBNull(7) ? leaderLogin : reader.GetString(7),
                    BankGold = reader.GetInt64(4),
                    CreatedAt = reader.GetString(5),
                    MemberCount = reader.GetInt32(6)
                });
            }
        }
        catch (Exception ex)
        {
            DebugLogger.Instance?.LogError("GUILD", $"Failed to get all guilds: {ex.Message}");
        }
        return guilds;
    }

    /// <summary>
    /// Get online members of a guild for chat broadcast.
    /// </summary>
    public List<string> GetOnlineGuildMembers(string guildName)
    {
        var members = new List<string>();
        var server = Server.MudServer.Instance;
        if (server == null) return members;

        foreach (var kvp in membershipCache)
        {
            if (string.Equals(kvp.Value, guildName, StringComparison.OrdinalIgnoreCase))
            {
                if (server.ActiveSessions.ContainsKey(kvp.Key.ToLowerInvariant()))
                    members.Add(kvp.Key);
            }
        }
        return members;
    }

    /// <summary>
    /// Send a guild invite to a player.
    /// </summary>
    public void SendInvite(string targetUsername, string guildName, string inviterName, string? guildDisplayName = null)
    {
        pendingInvites[targetUsername] = new GuildInvite
        {
            GuildName = guildName,
            GuildDisplayName = guildDisplayName ?? guildName,
            InviterName = inviterName,
            ExpiresAt = DateTime.UtcNow.AddMinutes(2)
        };
    }

    /// <summary>
    /// Accept a pending guild invite.
    /// </summary>
    public string? AcceptInvite(string username)
    {
        if (!pendingInvites.TryRemove(username, out var invite))
            return "No pending guild invite.";

        if (DateTime.UtcNow > invite.ExpiresAt)
            return "That invite has expired.";

        return AddMember(username, invite.GuildName);
    }

    /// <summary>
    /// Decline a pending guild invite.
    /// </summary>
    public bool DeclineInvite(string username)
    {
        return pendingInvites.TryRemove(username, out _);
    }

    /// <summary>
    /// Check if a player has a pending guild invite.
    /// </summary>
    public GuildInvite? GetPendingInvite(string username)
    {
        if (pendingInvites.TryGetValue(username, out var invite))
        {
            if (DateTime.UtcNow > invite.ExpiresAt)
            {
                pendingInvites.TryRemove(username, out _);
                return null;
            }
            return invite;
        }
        return null;
    }

    private int GetMemberCount(string guildName)
    {
        try
        {
            using var conn = new SqliteConnection(connectionString);
            conn.Open();
            using var cmd = conn.CreateCommand();
            cmd.CommandText = "SELECT COUNT(*) FROM guild_members WHERE guild_name = @guild COLLATE NOCASE";
            cmd.Parameters.AddWithValue("@guild", guildName);
            return Convert.ToInt32(cmd.ExecuteScalar());
        }
        catch { return 0; }
    }

    /// <summary>
    /// Check if user is the guild leader.
    /// </summary>
    public bool IsGuildLeader(string username, string guildName)
    {
        try
        {
            using var conn = new SqliteConnection(connectionString);
            conn.Open();
            using var cmd = conn.CreateCommand();
            cmd.CommandText = "SELECT leader_username FROM guilds WHERE name = @guild COLLATE NOCASE";
            cmd.Parameters.AddWithValue("@guild", guildName);
            var leader = cmd.ExecuteScalar()?.ToString();
            return string.Equals(leader, username, StringComparison.OrdinalIgnoreCase);
        }
        catch { return false; }
    }

    /// <summary>
    /// Get a member's rank in their guild.
    /// </summary>
    public string GetMemberRank(string username)
    {
        try
        {
            using var conn = new SqliteConnection(connectionString);
            conn.Open();
            using var cmd = conn.CreateCommand();
            cmd.CommandText = "SELECT rank FROM guild_members WHERE username = @user COLLATE NOCASE";
            cmd.Parameters.AddWithValue("@user", username.ToLowerInvariant());
            return cmd.ExecuteScalar()?.ToString() ?? "Member";
        }
        catch { return "Member"; }
    }

    /// <summary>
    /// Set a member's rank. Only the Leader can do this. Cannot change own rank or set someone to Leader.
    /// </summary>
    public string? SetMemberRank(string leaderUsername, string targetUsername, string newRank)
    {
        var guildName = GetPlayerGuild(leaderUsername);
        if (guildName == null) return "You are not in a guild.";
        if (!IsGuildLeader(leaderUsername, guildName)) return "Only the guild leader can set ranks.";
        if (string.Equals(leaderUsername, targetUsername, StringComparison.OrdinalIgnoreCase))
            return "You cannot change your own rank.";

        var targetGuild = GetPlayerGuild(targetUsername);
        if (!string.Equals(targetGuild, guildName, StringComparison.OrdinalIgnoreCase))
            return $"{targetUsername} is not in your guild.";

        // Validate rank
        if (newRank != "Officer" && newRank != "Member")
            return "Valid ranks: Officer, Member";

        try
        {
            using var conn = new SqliteConnection(connectionString);
            conn.Open();
            using var cmd = conn.CreateCommand();
            cmd.CommandText = "UPDATE guild_members SET rank = @rank WHERE username = @user COLLATE NOCASE";
            cmd.Parameters.AddWithValue("@rank", newRank);
            cmd.Parameters.AddWithValue("@user", targetUsername.ToLowerInvariant());
            cmd.ExecuteNonQuery();
            return null;
        }
        catch (Exception ex)
        {
            DebugLogger.Instance?.LogError("GUILD", $"Failed to set rank: {ex.Message}");
            return "Failed to set rank.";
        }
    }

    /// <summary>
    /// Transfer leadership to another guild member. Current leader becomes Officer.
    /// </summary>
    public string? TransferLeadership(string currentLeader, string newLeader)
    {
        var guildName = GetPlayerGuild(currentLeader);
        if (guildName == null) return "You are not in a guild.";
        if (!IsGuildLeader(currentLeader, guildName)) return "Only the guild leader can transfer leadership.";
        if (string.Equals(currentLeader, newLeader, StringComparison.OrdinalIgnoreCase))
            return "You are already the leader.";

        var targetGuild = GetPlayerGuild(newLeader);
        if (!string.Equals(targetGuild, guildName, StringComparison.OrdinalIgnoreCase))
            return $"{newLeader} is not in your guild.";

        try
        {
            using var conn = new SqliteConnection(connectionString);
            conn.Open();

            using var cmd = conn.CreateCommand();
            cmd.CommandText = @"
                UPDATE guilds SET leader_username = @new WHERE name = @guild;
                UPDATE guild_members SET rank = 'Leader' WHERE username = @new AND guild_name = @guild;
                UPDATE guild_members SET rank = 'Officer' WHERE username = @old AND guild_name = @guild";
            cmd.Parameters.AddWithValue("@new", newLeader.ToLowerInvariant());
            cmd.Parameters.AddWithValue("@old", currentLeader.ToLowerInvariant());
            cmd.Parameters.AddWithValue("@guild", guildName);
            cmd.ExecuteNonQuery();
            return null;
        }
        catch (Exception ex)
        {
            DebugLogger.Instance?.LogError("GUILD", $"Failed to transfer leadership: {ex.Message}");
            return "Failed to transfer leadership.";
        }
    }

    /// <summary>
    /// Withdraw gold from the guild bank. Officers limited to OfficerGoldWithdrawLimit per transaction.
    /// </summary>
    public string? WithdrawGold(string username, long amount)
    {
        var guildName = GetPlayerGuild(username);
        if (guildName == null) return "Not in a guild.";
        if (amount <= 0) return "Amount must be positive.";

        string rank = GetMemberRank(username);
        if (!RankCanWithdraw(rank)) return "Your rank does not allow gold withdrawals.";

        if (rank == "Officer" && amount > OfficerGoldWithdrawLimit)
            return $"Officers can withdraw up to {OfficerGoldWithdrawLimit:N0} gold per transaction.";

        try
        {
            using var conn = new SqliteConnection(connectionString);
            conn.Open();

            // Check bank balance
            using (var checkCmd = conn.CreateCommand())
            {
                checkCmd.CommandText = "SELECT bank_gold FROM guilds WHERE name = @guild COLLATE NOCASE";
                checkCmd.Parameters.AddWithValue("@guild", guildName);
                long bankGold = Convert.ToInt64(checkCmd.ExecuteScalar() ?? 0);
                if (bankGold < amount) return $"Guild bank only has {bankGold:N0} gold.";
            }

            using var cmd = conn.CreateCommand();
            cmd.CommandText = "UPDATE guilds SET bank_gold = bank_gold - @amount WHERE name = @guild AND bank_gold >= @amount";
            cmd.Parameters.AddWithValue("@amount", amount);
            cmd.Parameters.AddWithValue("@guild", guildName);
            int rows = cmd.ExecuteNonQuery();
            if (rows == 0) return "Insufficient gold in guild bank.";
            return null;
        }
        catch (Exception ex)
        {
            DebugLogger.Instance?.LogError("GUILD", $"Failed to withdraw gold: {ex.Message}");
            return "Failed to withdraw gold.";
        }
    }

    /// <summary>
    /// Deposit an item into the guild bank. Any member can deposit.
    /// </summary>
    public string? DepositItem(string username, string itemName, string itemJson)
    {
        var guildName = GetPlayerGuild(username);
        if (guildName == null) return "Not in a guild.";

        // Check item count
        int itemCount = GetBankItemCount(guildName);
        if (itemCount >= MaxBankItems)
            return $"Guild bank is full ({MaxBankItems} items max).";

        try
        {
            using var conn = new SqliteConnection(connectionString);
            conn.Open();
            using var cmd = conn.CreateCommand();
            cmd.CommandText = @"INSERT INTO guild_bank_items (guild_name, item_name, item_json, deposited_by)
                                VALUES (@guild, @name, @json, @user)";
            cmd.Parameters.AddWithValue("@guild", guildName);
            cmd.Parameters.AddWithValue("@name", itemName);
            cmd.Parameters.AddWithValue("@json", itemJson);
            cmd.Parameters.AddWithValue("@user", username.ToLowerInvariant());
            cmd.ExecuteNonQuery();
            return null;
        }
        catch (Exception ex)
        {
            DebugLogger.Instance?.LogError("GUILD", $"Failed to deposit item: {ex.Message}");
            return "Failed to deposit item.";
        }
    }

    /// <summary>
    /// Withdraw an item from the guild bank by ID. Leader/Officer only.
    /// Returns the item JSON on success, or null on failure (error message set via out param).
    /// </summary>
    public string? WithdrawItem(string username, int itemId, out string? errorMessage)
    {
        errorMessage = null;
        var guildName = GetPlayerGuild(username);
        if (guildName == null) { errorMessage = "Not in a guild."; return null; }

        string rank = GetMemberRank(username);
        if (!RankCanWithdraw(rank)) { errorMessage = "Your rank does not allow item withdrawals."; return null; }

        try
        {
            using var conn = new SqliteConnection(connectionString);
            conn.Open();

            // Use transaction to prevent race condition (two players withdrawing same item)
            using var transaction = conn.BeginTransaction();

            // Get the item
            string? itemJson;
            using (var getCmd = conn.CreateCommand())
            {
                getCmd.CommandText = "SELECT item_json FROM guild_bank_items WHERE id = @id AND guild_name = @guild COLLATE NOCASE";
                getCmd.Parameters.AddWithValue("@id", itemId);
                getCmd.Parameters.AddWithValue("@guild", guildName);
                itemJson = getCmd.ExecuteScalar()?.ToString();
            }

            if (itemJson == null) { transaction.Rollback(); errorMessage = "Item not found in guild bank."; return null; }

            // Delete the item
            using var delCmd = conn.CreateCommand();
            delCmd.CommandText = "DELETE FROM guild_bank_items WHERE id = @id AND guild_name = @guild COLLATE NOCASE";
            delCmd.Parameters.AddWithValue("@id", itemId);
            delCmd.Parameters.AddWithValue("@guild", guildName);
            delCmd.ExecuteNonQuery();

            transaction.Commit();
            return itemJson;
        }
        catch (Exception ex)
        {
            DebugLogger.Instance?.LogError("GUILD", $"Failed to withdraw item: {ex.Message}");
            errorMessage = "Failed to withdraw item.";
            return null;
        }
    }

    /// <summary>
    /// Get all items in a guild's bank.
    /// </summary>
    public List<GuildBankItem> GetBankItems(string guildName)
    {
        var items = new List<GuildBankItem>();
        try
        {
            using var conn = new SqliteConnection(connectionString);
            conn.Open();
            using var cmd = conn.CreateCommand();
            cmd.CommandText = @"SELECT id, item_name, item_json, deposited_by, deposited_at
                                FROM guild_bank_items WHERE guild_name = @guild COLLATE NOCASE
                                ORDER BY deposited_at DESC";
            cmd.Parameters.AddWithValue("@guild", guildName);
            using var reader = cmd.ExecuteReader();
            while (reader.Read())
            {
                items.Add(new GuildBankItem
                {
                    Id = reader.GetInt32(0),
                    ItemName = reader.GetString(1),
                    ItemJson = reader.GetString(2),
                    DepositedBy = reader.GetString(3),
                    DepositedAt = reader.GetString(4)
                });
            }
        }
        catch (Exception ex)
        {
            DebugLogger.Instance?.LogError("GUILD", $"Failed to get bank items: {ex.Message}");
        }
        return items;
    }

    private int GetBankItemCount(string guildName)
    {
        try
        {
            using var conn = new SqliteConnection(connectionString);
            conn.Open();
            using var cmd = conn.CreateCommand();
            cmd.CommandText = "SELECT COUNT(*) FROM guild_bank_items WHERE guild_name = @guild COLLATE NOCASE";
            cmd.Parameters.AddWithValue("@guild", guildName);
            return Convert.ToInt32(cmd.ExecuteScalar());
        }
        catch { return 0; }
    }
}

public class GuildInfo
{
    public string Name { get; set; } = ""; // Lowercased key used in DB/cache lookups
    public string DisplayName { get; set; } = "";
    public string Motto { get; set; } = "";
    public string LeaderUsername { get; set; } = "";
    public string LeaderDisplayName { get; set; } = ""; // Resolved display name for leader
    public long BankGold { get; set; }
    public string CreatedAt { get; set; } = "";
    public int MemberCount { get; set; }
    public List<GuildMemberInfo> Members { get; set; } = new();
}

public class GuildMemberInfo
{
    public string Username { get; set; } = "";
    public string DisplayName { get; set; } = "";
    public string Rank { get; set; } = "Member";
}

public class GuildInvite
{
    public string GuildName { get; set; } = "";
    public string GuildDisplayName { get; set; } = ""; // Human-readable display name
    public string InviterName { get; set; } = "";
    public DateTime ExpiresAt { get; set; }
}

public class GuildBankItem
{
    public int Id { get; set; }
    public string ItemName { get; set; } = "";
    public string ItemJson { get; set; } = "";
    public string DepositedBy { get; set; } = "";
    public string DepositedAt { get; set; } = "";
}
