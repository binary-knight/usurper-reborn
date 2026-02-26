using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;

namespace UsurperRemake.Server;

/// <summary>
/// Manages dungeon groups for real-time cooperative play in MUD mode.
/// Groups are transient (in-memory only, not serialized).
/// </summary>
public class GroupSystem
{
    public static GroupSystem? Instance { get; private set; }

    /// <summary>Maps leader username (lowercase) to group.</summary>
    private readonly ConcurrentDictionary<string, DungeonGroup> _groups = new();

    /// <summary>Maps any member username (lowercase) to the group they belong to.</summary>
    private readonly ConcurrentDictionary<string, DungeonGroup> _memberIndex = new();

    public GroupSystem()
    {
        Instance = this;
    }

    /// <summary>Get the group a player belongs to (as leader or member), or null.</summary>
    public DungeonGroup? GetGroupFor(string username)
    {
        _memberIndex.TryGetValue(username.ToLowerInvariant(), out var group);
        return group;
    }

    /// <summary>Create a new group with the given player as leader.</summary>
    public DungeonGroup CreateGroup(string leaderUsername)
    {
        var key = leaderUsername.ToLowerInvariant();
        var group = new DungeonGroup(leaderUsername);
        _groups[key] = group;
        _memberIndex[key] = group;
        return group;
    }

    /// <summary>Add a member to an existing group.</summary>
    public bool AddMember(DungeonGroup group, string username)
    {
        if (group.IsFull) return false;

        var key = username.ToLowerInvariant();
        lock (group.MemberUsernames)
        {
            if (group.IsFull) return false;
            group.MemberUsernames.Add(username);
        }
        _memberIndex[key] = group;
        return true;
    }

    /// <summary>Remove a single member from their group (not the leader). Returns the group they were in.</summary>
    public DungeonGroup? RemoveMember(string username, string reason)
    {
        var key = username.ToLowerInvariant();
        if (!_memberIndex.TryGetValue(key, out var group))
            return null;

        // If this player is the leader, disband the group instead
        if (group.IsLeader(username))
        {
            DisbandGroup(username, reason);
            return group;
        }

        _memberIndex.TryRemove(key, out _);
        lock (group.MemberUsernames)
        {
            group.MemberUsernames.RemoveAll(u => u.Equals(username, StringComparison.OrdinalIgnoreCase));
        }

        // Notify remaining group members
        var leavingSession = GetSession(username);
        var leavingPlayer = leavingSession?.Context?.Engine?.CurrentPlayer;
        var leavingName = !string.IsNullOrEmpty(leavingPlayer?.Name2) ? leavingPlayer.Name2
            : !string.IsNullOrEmpty(leavingPlayer?.Name1) ? leavingPlayer.Name1
            : username;
        NotifyGroup(group, $"\u001b[1;33m  * {leavingName} has left the group ({reason}).\u001b[0m", excludeUsername: username);

        // If only leader remains, disband
        lock (group.MemberUsernames)
        {
            if (group.MemberUsernames.Count <= 1)
            {
                DisbandGroup(group.LeaderUsername, "group too small");
            }
        }

        return group;
    }

    /// <summary>Disband a group entirely. All members are notified and removed.</summary>
    public void DisbandGroup(string leaderUsername, string reason)
    {
        var leaderKey = leaderUsername.ToLowerInvariant();
        if (!_groups.TryRemove(leaderKey, out var group))
        {
            // Maybe the username isn't the leader — find their group and disband it
            if (_memberIndex.TryGetValue(leaderKey, out group))
            {
                _groups.TryRemove(group.LeaderUsername.ToLowerInvariant(), out _);
            }
            else
            {
                return;
            }
        }

        List<string> members;
        lock (group.MemberUsernames)
        {
            members = new List<string>(group.MemberUsernames);
            group.MemberUsernames.Clear();
        }

        // Clean up all member index entries and notify
        foreach (var member in members)
        {
            _memberIndex.TryRemove(member.ToLowerInvariant(), out _);

            // Clear group follower state on player sessions
            var session = MudServer.Instance?.ActiveSessions
                .TryGetValue(member.ToLowerInvariant(), out var s) == true ? s : null;
            if (session != null)
            {
                session.EnqueueMessage(
                    $"\u001b[1;33m  * Your group has been disbanded ({reason}).\u001b[0m");

                // Signal follower loops to exit
                session.IsGroupFollower = false;
                session.GroupLeaderSession = null;
                session.PendingGroupInvite = null;
            }
        }

        group.IsInDungeon = false;
    }

    /// <summary>Get all active groups.</summary>
    public IReadOnlyCollection<DungeonGroup> GetAllGroups()
    {
        return _groups.Values.ToList().AsReadOnly();
    }

    /// <summary>Send a message to all members of a group.</summary>
    public void NotifyGroup(DungeonGroup group, string message, string? excludeUsername = null)
    {
        List<string> members;
        lock (group.MemberUsernames)
        {
            members = new List<string>(group.MemberUsernames);
        }

        foreach (var member in members)
        {
            if (excludeUsername != null && member.Equals(excludeUsername, StringComparison.OrdinalIgnoreCase))
                continue;

            var session = MudServer.Instance?.ActiveSessions
                .TryGetValue(member.ToLowerInvariant(), out var s) == true ? s : null;
            session?.EnqueueMessage(message);
        }
    }

    /// <summary>
    /// Broadcast a perspective-correct message to all group members.
    /// The actor sees actorMessage (2nd person), everyone else sees observerMessage (3rd person).
    /// </summary>
    public void BroadcastToGroupSessions(DungeonGroup group, string actorUsername,
        string actorMessage, string observerMessage)
    {
        List<string> members;
        lock (group.MemberUsernames)
        {
            members = new List<string>(group.MemberUsernames);
        }

        foreach (var member in members)
        {
            var session = MudServer.Instance?.ActiveSessions
                .TryGetValue(member.ToLowerInvariant(), out var s) == true ? s : null;
            if (session == null) continue;

            if (member.Equals(actorUsername, StringComparison.OrdinalIgnoreCase))
                session.EnqueueMessage(actorMessage);
            else
                session.EnqueueMessage(observerMessage);
        }
    }

    /// <summary>
    /// Broadcast the same message to all group members.
    /// </summary>
    public void BroadcastToAllGroupSessions(DungeonGroup group, string message,
        string? excludeUsername = null)
    {
        List<string> members;
        lock (group.MemberUsernames)
        {
            members = new List<string>(group.MemberUsernames);
        }

        foreach (var member in members)
        {
            if (excludeUsername != null && member.Equals(excludeUsername, StringComparison.OrdinalIgnoreCase))
                continue;

            var session = MudServer.Instance?.ActiveSessions
                .TryGetValue(member.ToLowerInvariant(), out var s) == true ? s : null;
            session?.EnqueueMessage(message);
        }
    }

    /// <summary>Get the PlayerSession for a username.</summary>
    public static PlayerSession? GetSession(string username)
    {
        return MudServer.Instance?.ActiveSessions
            .TryGetValue(username.ToLowerInvariant(), out var s) == true ? s : null;
    }

    /// <summary>Calculate the group XP multiplier for a player based on level gap with highest in group.</summary>
    public static float GetGroupXPMultiplier(int playerLevel, int highestLevelInGroup)
    {
        int gap = highestLevelInGroup - playerLevel;
        if (gap <= 0) return 1.0f;

        foreach (var tier in GameConfig.GroupXPPenaltyTiers)
        {
            if (gap <= tier.MaxGap)
                return tier.Multiplier;
        }

        return GameConfig.GroupXPPenaltyMinimum;
    }
}

/// <summary>
/// Represents an active dungeon group.
/// </summary>
public class DungeonGroup
{
    /// <summary>Username of the group leader (original casing).</summary>
    public string LeaderUsername { get; }

    /// <summary>All member usernames including leader. Leader is always at index [0].</summary>
    public List<string> MemberUsernames { get; } = new();

    /// <summary>True when the leader has entered the dungeon.</summary>
    public bool IsInDungeon { get; set; }

    /// <summary>Current dungeon floor the group is on.</summary>
    public int CurrentFloor { get; set; }

    public bool IsFull => MemberUsernames.Count >= GameConfig.GroupMaxSize;

    public bool IsLeader(string username) =>
        LeaderUsername.Equals(username, StringComparison.OrdinalIgnoreCase);

    public DungeonGroup(string leaderUsername)
    {
        LeaderUsername = leaderUsername;
        MemberUsernames.Add(leaderUsername);
    }
}

/// <summary>
/// A pending group invite from one player to another.
/// The inviter waits on Response.Task; the target resolves it via /accept or /deny.
/// </summary>
public class GroupInvite
{
    public PlayerSession Inviter { get; init; } = null!;
    public TaskCompletionSource<bool> Response { get; } = new();
    public DateTime InvitedAt { get; } = DateTime.UtcNow;
    public bool IsExpired => (DateTime.UtcNow - InvitedAt).TotalSeconds > GameConfig.GroupInviteTimeoutSeconds;
}
