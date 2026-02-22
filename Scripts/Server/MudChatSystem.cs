using System;
using System.Linq;
using System.Threading.Tasks;

namespace UsurperRemake.Server;

/// <summary>
/// In-memory chat system for MUD mode. Replaces SQLite-polled OnlineChatSystem
/// with instant delivery via RoomRegistry and MudServer.
///
/// Commands:
///   /say message      → room-scoped broadcast (only players at same location see it)
///   /shout message    → global broadcast (all connected players see it)
///   /tell player msg  → instant private message to a specific player
///   /emote action     → room-scoped emote ("* PlayerName waves hello")
///   /gossip message   → global out-of-character chat channel (/gos shortcut)
///   /who              → show all online players and their locations
///
/// Wizard commands are routed to WizardCommandSystem before normal chat processing.
/// </summary>
public static class MudChatSystem
{
    private static readonly string[] ChatCommands = { "say", "s", "shout", "tell", "t", "emote", "me", "gossip", "gos" };

    /// <summary>
    /// Try to process a slash command as a MUD chat command.
    /// Returns true if the command was handled, false if it should fall through.
    /// Only active when SessionContext.IsActive (MUD mode).
    /// </summary>
    public static async Task<bool> TryProcessCommand(string input, TerminalEmulator terminal)
    {
        if (!SessionContext.IsActive || RoomRegistry.Instance == null)
            return false;

        var ctx = SessionContext.Current!;
        var username = ctx.Username;

        // Parse command and arguments
        if (!input.StartsWith("/"))
            return false;

        var spaceIndex = input.IndexOf(' ');
        var command = spaceIndex > 0 ? input.Substring(1, spaceIndex - 1).ToLowerInvariant() : input.Substring(1).ToLowerInvariant();
        var args = spaceIndex > 0 ? input.Substring(spaceIndex + 1).Trim() : "";

        // Check frozen status — frozen players can only use wizard commands (if they're a wizard)
        var session = MudServer.Instance?.ActiveSessions.TryGetValue(username.ToLowerInvariant(), out var s) == true ? s : null;
        if (session?.IsFrozen == true)
        {
            // Wizards can still use wizard commands even when frozen (shouldn't happen, but safety)
            if (ctx.WizardLevel > WizardLevel.Mortal)
            {
                var wizHandled = await WizardCommandSystem.TryProcessCommand(input, username, ctx.WizardLevel, terminal);
                if (wizHandled) return true;
            }
            terminal.SetColor("bright_cyan");
            terminal.WriteLine("  You are frozen solid! You can do nothing!");
            return true;
        }

        // Route to wizard commands first (if player has wizard level > 0)
        if (ctx.WizardLevel > WizardLevel.Mortal)
        {
            var wizHandled = await WizardCommandSystem.TryProcessCommand(input, username, ctx.WizardLevel, terminal);
            if (wizHandled) return true;
        }

        // Check muted status for chat commands
        if (session?.IsMuted == true && Array.IndexOf(ChatCommands, command) >= 0)
        {
            terminal.SetColor("bright_red");
            terminal.WriteLine("  You have been silenced by the gods. You cannot speak.");
            return true;
        }

        switch (command)
        {
            case "say":
            case "s":
                return HandleSay(username, args, terminal);

            case "shout":
                return HandleShout(username, args, terminal);

            case "tell":
            case "t":
                return HandleTell(username, args, terminal);

            case "emote":
            case "me":
                return HandleEmote(username, args, terminal);

            case "gossip":
            case "gos":
                return HandleGossip(username, args, terminal);

            case "who":
            case "w":
                return HandleWho(username, terminal);

            case "accept":
                return HandleAccept(username, terminal);

            case "deny":
            case "reject":
                return HandleDeny(username, terminal);

            case "spectators":
                return HandleListSpectators(username, terminal);

            case "nospec":
            case "nospectate":
                return HandleKickAllSpectators(username, terminal);

            case "group":
            case "g":
                return await HandleGroup(username, args, terminal);

            case "leave":
                return HandleLeaveGroup(username, terminal);

            case "disband":
                return HandleDisbandGroup(username, terminal);

            default:
                return false; // Not a MUD chat command
        }
    }

    private static bool HandleSay(string username, string message, TerminalEmulator terminal)
    {
        if (string.IsNullOrWhiteSpace(message))
        {
            terminal.SetColor("gray");
            terminal.WriteLine("  Say what? Usage: /say <message>");
            return true;
        }

        // Get current location from room registry
        var location = RoomRegistry.Instance!.GetPlayerLocation(username);
        if (!location.HasValue)
            return true; // No location tracked yet

        // Show to sender
        terminal.SetColor("bright_white");
        terminal.WriteLine($"  You say: {message}");

        // Broadcast to others in the room
        RoomRegistry.Instance.BroadcastToRoom(
            location.Value,
            $"\u001b[1;37m  {username} says: {message}\u001b[0m",
            excludeUsername: username);

        return true;
    }

    private static bool HandleShout(string username, string message, TerminalEmulator terminal)
    {
        if (string.IsNullOrWhiteSpace(message))
        {
            terminal.SetColor("gray");
            terminal.WriteLine("  Shout what? Usage: /shout <message>");
            return true;
        }

        // Show to sender
        terminal.SetColor("bright_yellow");
        terminal.WriteLine($"  You shout: {message}");

        // Broadcast to ALL connected players
        RoomRegistry.Instance!.BroadcastGlobal(
            $"\u001b[1;33m  {username} shouts: {message}\u001b[0m",
            excludeUsername: username);

        return true;
    }

    private static bool HandleTell(string username, string args, TerminalEmulator terminal)
    {
        // Parse: /tell <playername> <message>
        var spaceIndex = args.IndexOf(' ');
        if (spaceIndex <= 0)
        {
            terminal.SetColor("gray");
            terminal.WriteLine("  Usage: /tell <player> <message>");
            return true;
        }

        var targetName = args.Substring(0, spaceIndex).Trim();
        var message = args.Substring(spaceIndex + 1).Trim();

        if (string.IsNullOrWhiteSpace(message))
        {
            terminal.SetColor("gray");
            terminal.WriteLine("  Usage: /tell <player> <message>");
            return true;
        }

        // Try to send in-memory first
        var server = MudServer.Instance;
        if (server != null && server.SendToPlayer(targetName,
            $"\u001b[35m  {username} tells you: {message}\u001b[0m"))
        {
            terminal.SetColor("magenta");
            terminal.WriteLine($"  You tell {targetName}: {message}");
        }
        else
        {
            terminal.SetColor("gray");
            terminal.WriteLine($"  {targetName} is not online.");
        }

        return true;
    }

    private static bool HandleEmote(string username, string action, TerminalEmulator terminal)
    {
        if (string.IsNullOrWhiteSpace(action))
        {
            terminal.SetColor("gray");
            terminal.WriteLine("  Usage: /emote <action> (e.g., /emote waves hello)");
            return true;
        }

        var location = RoomRegistry.Instance!.GetPlayerLocation(username);
        if (!location.HasValue)
            return true;

        // Show to sender
        terminal.SetColor("bright_cyan");
        terminal.WriteLine($"  * {username} {action}");

        // Broadcast to others in the room
        RoomRegistry.Instance.BroadcastToRoom(
            location.Value,
            $"\u001b[1;36m  * {username} {action}\u001b[0m",
            excludeUsername: username);

        return true;
    }

    private static bool HandleGossip(string username, string message, TerminalEmulator terminal)
    {
        if (string.IsNullOrWhiteSpace(message))
        {
            terminal.SetColor("gray");
            terminal.WriteLine("  Gossip what? Usage: /gossip <message>  (or /gos)");
            return true;
        }

        // Show to sender
        terminal.SetColor("bright_green");
        terminal.WriteLine($"  [Gossip] You: {message}");

        // Broadcast to ALL connected players (global out-of-character channel)
        RoomRegistry.Instance!.BroadcastGlobal(
            $"\u001b[92m  [Gossip] {username}: {message}\u001b[0m",
            excludeUsername: username);

        return true;
    }

    private static bool HandleWho(string username, TerminalEmulator terminal)
    {
        var server = MudServer.Instance;
        if (server == null) return true;

        terminal.SetColor("bright_cyan");
        terminal.WriteLine("╔══════════════════════════════════════════════════════════════════════════════╗");
        terminal.SetColor("bright_white");
        terminal.WriteLine("║                           Who's Online                                     ║");
        terminal.SetColor("bright_cyan");
        terminal.WriteLine("╠══════════════════════════════════════════════════════════════════════════════╣");

        var myWizLevel = SessionContext.Current?.WizardLevel ?? WizardLevel.Mortal;
        var sessions = server.ActiveSessions.Values
            .Where(s => !s.IsWizInvisible || myWizLevel >= s.WizardLevel) // Hide invisible wizards from lower-level
            .ToList();

        if (sessions.Count == 0)
        {
            terminal.SetColor("gray");
            terminal.WriteLine("║  No other players online.                                                  ║");
        }
        else
        {
            foreach (var session in sessions)
            {
                var loc = RoomRegistry.Instance?.GetPlayerLocation(session.Username);
                var locName = loc.HasValue ? BaseLocation.GetLocationName(loc.Value) : "Unknown";
                var isYou = session.Username.Equals(username, StringComparison.OrdinalIgnoreCase) ? " (you)" : "";
                var wizTag = session.WizardLevel > WizardLevel.Mortal
                    ? $" [{WizardConstants.GetTitle(session.WizardLevel)}]" : "";
                var invisTag = session.IsWizInvisible && myWizLevel >= session.WizardLevel
                    ? " [INVIS]" : "";
                var specTag = "";
                if (session.IsSpectating && session.SpectatingSession != null)
                    specTag = $" [watching {session.SpectatingSession.Username}]";
                else if (session.Spectators.Count > 0)
                    specTag = $" [{session.Spectators.Count} watching]";

                var groupTag = "";
                var playerGroup = GroupSystem.Instance?.GetGroupFor(session.Username);
                if (playerGroup != null)
                {
                    if (playerGroup.IsLeader(session.Username))
                        groupTag = $" [Group Leader]";
                    else
                        groupTag = $" [Group: {playerGroup.LeaderUsername}]";
                }

                if (session.WizardLevel > WizardLevel.Mortal)
                    terminal.SetColor(WizardConstants.GetColor(session.WizardLevel));
                else
                    terminal.SetColor(string.IsNullOrEmpty(isYou) ? "white" : "bright_green");

                var line = $"  {session.Username}{wizTag}{isYou}{invisTag}{specTag}{groupTag} - {locName} [{session.ConnectionType}]";
                terminal.WriteLine($"║{line.PadRight(78)}║");
            }
        }

        terminal.SetColor("bright_cyan");
        terminal.WriteLine($"╠══════════════════════════════════════════════════════════════════════════════╣");
        terminal.SetColor("gray");
        terminal.WriteLine($"║  {sessions.Count} player(s) online                                                       ║");
        terminal.SetColor("bright_cyan");
        terminal.WriteLine("╚══════════════════════════════════════════════════════════════════════════════╝");

        return true;
    }

    // ═══════════════════════════════════════════════════════════════════
    // ACCEPT / DENY (handles group invites first, then spectate requests)
    // ═══════════════════════════════════════════════════════════════════

    private static bool HandleAccept(string username, TerminalEmulator terminal)
    {
        var session = MudServer.Instance?.ActiveSessions
            .TryGetValue(username.ToLowerInvariant(), out var s) == true ? s : null;
        if (session == null) return true;

        // Check for pending group invite first
        if (session.PendingGroupInvite != null)
        {
            var invite = session.PendingGroupInvite;
            if (invite.IsExpired)
            {
                terminal.SetColor("gray");
                terminal.WriteLine("  That group invite has expired.");
                session.PendingGroupInvite = null;
                invite.Response.TrySetResult(false);
                return true;
            }

            session.PendingGroupInvite = null;
            invite.Response.TrySetResult(true);
            terminal.SetColor("bright_green");
            terminal.WriteLine($"  You accepted {invite.Inviter.Username}'s group invite.");
            return true;
        }

        // Check for pending spectate request
        if (session.PendingSpectateRequest != null)
        {
            var request = session.PendingSpectateRequest;
            if (request.IsExpired)
            {
                terminal.SetColor("gray");
                terminal.WriteLine("  That spectate request has expired.");
                session.PendingSpectateRequest = null;
                request.Response.TrySetResult(false);
                return true;
            }

            session.PendingSpectateRequest = null;
            request.Response.TrySetResult(true);
            terminal.SetColor("bright_green");
            terminal.WriteLine($"  You accepted {request.Requester.Username}'s spectate request.");
            return true;
        }

        terminal.SetColor("gray");
        terminal.WriteLine("  No pending request to accept.");
        return true;
    }

    private static bool HandleDeny(string username, TerminalEmulator terminal)
    {
        var session = MudServer.Instance?.ActiveSessions
            .TryGetValue(username.ToLowerInvariant(), out var s) == true ? s : null;
        if (session == null) return true;

        // Check for pending group invite first
        if (session.PendingGroupInvite != null)
        {
            var invite = session.PendingGroupInvite;
            session.PendingGroupInvite = null;
            invite.Response.TrySetResult(false);
            terminal.SetColor("yellow");
            terminal.WriteLine($"  You denied {invite.Inviter.Username}'s group invite.");
            return true;
        }

        // Check for pending spectate request
        if (session.PendingSpectateRequest != null)
        {
            var request = session.PendingSpectateRequest;
            session.PendingSpectateRequest = null;
            request.Response.TrySetResult(false);
            terminal.SetColor("yellow");
            terminal.WriteLine($"  You denied {request.Requester.Username}'s spectate request.");
            return true;
        }

        terminal.SetColor("gray");
        terminal.WriteLine("  No pending request to deny.");
        return true;
    }

    private static bool HandleListSpectators(string username, TerminalEmulator terminal)
    {
        var session = MudServer.Instance?.ActiveSessions
            .TryGetValue(username.ToLowerInvariant(), out var s) == true ? s : null;
        if (session == null || session.Spectators.Count == 0)
        {
            terminal.SetColor("gray");
            terminal.WriteLine("  No one is watching your session.");
            return true;
        }

        terminal.SetColor("bright_cyan");
        terminal.WriteLine("  Current spectators:");
        foreach (var spectator in session.Spectators.ToArray())
        {
            terminal.SetColor("white");
            terminal.WriteLine($"    - {spectator.Username}");
        }
        return true;
    }

    private static bool HandleKickAllSpectators(string username, TerminalEmulator terminal)
    {
        var session = MudServer.Instance?.ActiveSessions
            .TryGetValue(username.ToLowerInvariant(), out var s) == true ? s : null;
        if (session == null || session.Spectators.Count == 0)
        {
            terminal.SetColor("gray");
            terminal.WriteLine("  No one is watching your session.");
            return true;
        }

        foreach (var spectator in session.Spectators.ToArray())
        {
            spectator.EnqueueMessage(
                $"\u001b[1;33m  * {username} has ended the spectator session.\u001b[0m");
            spectator.SpectatingSession = null;
            spectator.IsSpectating = false;
            session.Context?.Terminal?.RemoveSpectatorStream(spectator);
        }
        session.Spectators.Clear();

        terminal.SetColor("bright_green");
        terminal.WriteLine("  All spectators have been removed.");
        return true;
    }

    // ═══════════════════════════════════════════════════════════════════
    // GROUP COMMANDS
    // ═══════════════════════════════════════════════════════════════════

    private static async Task<bool> HandleGroup(string username, string args, TerminalEmulator terminal)
    {
        var server = MudServer.Instance;
        if (server == null) return true;

        var mySession = server.ActiveSessions
            .TryGetValue(username.ToLowerInvariant(), out var s) ? s : null;
        if (mySession == null) return true;

        var groupSystem = GroupSystem.Instance;
        if (groupSystem == null) return true;

        // /group with no args — show current group info
        if (string.IsNullOrWhiteSpace(args))
        {
            var existingGroup = groupSystem.GetGroupFor(username);
            if (existingGroup == null)
            {
                terminal.SetColor("gray");
                terminal.WriteLine("  You are not in a group.");
                terminal.SetColor("bright_cyan");
                terminal.WriteLine("  Usage: /group <player> — invite a player to your group");
                terminal.WriteLine("  All group members must be on the same team.");
                return true;
            }

            terminal.SetColor("bright_cyan");
            terminal.WriteLine("  ═══════════════════════════════════════════");
            terminal.SetColor("bright_white");
            terminal.WriteLine("  Your Group:");
            terminal.SetColor("bright_cyan");
            terminal.WriteLine("  ═══════════════════════════════════════════");

            List<string> members;
            lock (existingGroup.MemberUsernames)
            {
                members = new System.Collections.Generic.List<string>(existingGroup.MemberUsernames);
            }

            foreach (var member in members)
            {
                bool isLeader = existingGroup.IsLeader(member);
                var memberSession = GroupSystem.GetSession(member);
                var player = memberSession?.Context?.Engine?.CurrentPlayer;
                var levelStr = player != null ? $" (Lv {player.Level})" : "";
                var statusTag = isLeader ? " [Leader]" : "";

                terminal.SetColor(isLeader ? "bright_yellow" : "white");
                terminal.WriteLine($"    {member}{statusTag}{levelStr}");
            }

            terminal.SetColor("gray");
            terminal.WriteLine($"  {members.Count}/{GameConfig.GroupMaxSize} members");
            if (existingGroup.IsInDungeon)
            {
                terminal.SetColor("bright_green");
                terminal.WriteLine($"  Status: In Dungeon (Floor {existingGroup.CurrentFloor})");
            }
            return true;
        }

        // /group <player> — invite a player
        var targetName = args.Trim();

        // Can't invite yourself
        if (targetName.Equals(username, StringComparison.OrdinalIgnoreCase))
        {
            terminal.SetColor("gray");
            terminal.WriteLine("  You can't invite yourself.");
            return true;
        }

        // Check level requirement
        var myPlayer = mySession.Context?.Engine?.CurrentPlayer;
        if (myPlayer != null && myPlayer.Level < GameConfig.GroupMinLevel)
        {
            terminal.SetColor("yellow");
            terminal.WriteLine($"  You must be at least level {GameConfig.GroupMinLevel} to form a group.");
            return true;
        }

        // Can't be a group follower and invite (leader or unaffiliated only)
        if (mySession.IsGroupFollower)
        {
            terminal.SetColor("yellow");
            terminal.WriteLine("  Only the group leader can invite new members.");
            return true;
        }

        // Find target player
        var targetSession = server.ActiveSessions.Values
            .FirstOrDefault(p => p.Username.Equals(targetName, StringComparison.OrdinalIgnoreCase));
        if (targetSession == null || !targetSession.IsInGame)
        {
            terminal.SetColor("gray");
            terminal.WriteLine($"  {targetName} is not online.");
            return true;
        }

        // Can't invite spectators
        if (targetSession.IsSpectating)
        {
            terminal.SetColor("yellow");
            terminal.WriteLine($"  {targetSession.Username} is currently spectating and cannot be invited.");
            return true;
        }

        // Can't invite someone already in a group
        var targetGroup = groupSystem.GetGroupFor(targetSession.Username);
        if (targetGroup != null)
        {
            terminal.SetColor("yellow");
            terminal.WriteLine($"  {targetSession.Username} is already in a group.");
            return true;
        }

        // Check team requirement
        var targetPlayer = targetSession.Context?.Engine?.CurrentPlayer;
        if (myPlayer != null && targetPlayer != null)
        {
            if (string.IsNullOrEmpty(myPlayer.Team) || string.IsNullOrEmpty(targetPlayer.Team))
            {
                terminal.SetColor("yellow");
                terminal.WriteLine("  Both players must be on a team to form a group.");
                return true;
            }
            if (!myPlayer.Team.Equals(targetPlayer.Team, StringComparison.OrdinalIgnoreCase))
            {
                terminal.SetColor("yellow");
                terminal.WriteLine($"  {targetSession.Username} is not on your team ({myPlayer.Team}).");
                return true;
            }
        }

        // Check target level
        if (targetPlayer != null && targetPlayer.Level < GameConfig.GroupMinLevel)
        {
            terminal.SetColor("yellow");
            terminal.WriteLine($"  {targetSession.Username} must be at least level {GameConfig.GroupMinLevel} to join a group.");
            return true;
        }

        // Get or create group
        var myGroup = groupSystem.GetGroupFor(username);
        if (myGroup == null)
        {
            myGroup = groupSystem.CreateGroup(username);
        }
        else if (!myGroup.IsLeader(username))
        {
            terminal.SetColor("yellow");
            terminal.WriteLine("  Only the group leader can invite new members.");
            return true;
        }

        // Check if group is full
        if (myGroup.IsFull)
        {
            terminal.SetColor("yellow");
            terminal.WriteLine($"  Your group is full ({GameConfig.GroupMaxSize}/{GameConfig.GroupMaxSize}).");
            return true;
        }

        // Check if group is in dungeon (can't invite mid-dungeon)
        if (myGroup.IsInDungeon)
        {
            terminal.SetColor("yellow");
            terminal.WriteLine("  You can't invite players while the group is in the dungeon.");
            return true;
        }

        // Check if target already has a pending invite
        if (targetSession.PendingGroupInvite != null && !targetSession.PendingGroupInvite.IsExpired)
        {
            terminal.SetColor("yellow");
            terminal.WriteLine($"  {targetSession.Username} already has a pending group invite.");
            return true;
        }

        // Send the invite
        var invite = new GroupInvite { Inviter = mySession };
        targetSession.PendingGroupInvite = invite;

        targetSession.EnqueueMessage(
            $"\u001b[1;33m  * {username} has invited you to join their dungeon group.\u001b[0m");
        targetSession.EnqueueMessage(
            $"\u001b[1;33m  * Type /accept to join or /deny to refuse. ({GameConfig.GroupInviteTimeoutSeconds}s)\u001b[0m");

        terminal.SetColor("bright_cyan");
        terminal.WriteLine($"  Group invite sent to {targetSession.Username}. ({GameConfig.GroupInviteTimeoutSeconds}s to respond)");

        // Fire-and-forget: background task handles the accept/deny/timeout
        _ = ProcessGroupInviteAsync(invite, mySession, targetSession, myGroup, groupSystem);

        return true;
    }

    /// <summary>
    /// Background task that waits for a group invite response or timeout,
    /// then adds the player to the group or notifies the leader of denial.
    /// </summary>
    private static async Task ProcessGroupInviteAsync(
        GroupInvite invite, PlayerSession leaderSession, PlayerSession targetSession,
        DungeonGroup group, GroupSystem groupSystem)
    {
        bool accepted;
        try
        {
            var responseTask = invite.Response.Task;
            var timeoutTask = Task.Delay(TimeSpan.FromSeconds(GameConfig.GroupInviteTimeoutSeconds));
            var completedTask = await Task.WhenAny(responseTask, timeoutTask);

            if (completedTask == responseTask)
            {
                accepted = responseTask.Result;
            }
            else
            {
                accepted = false;
                invite.Response.TrySetResult(false);
                targetSession.PendingGroupInvite = null;
                targetSession.EnqueueMessage(
                    $"\u001b[1;33m  * The group invite from {leaderSession.Username} has expired.\u001b[0m");
            }
        }
        catch
        {
            accepted = false;
            invite.Response.TrySetResult(false);
            targetSession.PendingGroupInvite = null;
        }

        if (!accepted)
        {
            leaderSession.EnqueueMessage(
                $"\u001b[1;33m  * {targetSession.Username} denied your group invite (or it timed out).\u001b[0m");

            // If group only has the leader (self), disband it
            lock (group.MemberUsernames)
            {
                if (group.MemberUsernames.Count <= 1)
                    groupSystem.DisbandGroup(leaderSession.Username, "no members joined");
            }
            return;
        }

        // Accepted — add to group
        if (!groupSystem.AddMember(group, targetSession.Username))
        {
            leaderSession.EnqueueMessage(
                $"\u001b[1;33m  * Failed to add {targetSession.Username} — group may be full.\u001b[0m");
            return;
        }

        leaderSession.EnqueueMessage(
            $"\u001b[1;32m  * {targetSession.Username} has joined your group!\u001b[0m");

        // Notify all other group members
        groupSystem.NotifyGroup(group,
            $"\u001b[1;32m  * {targetSession.Username} has joined the group!\u001b[0m",
            excludeUsername: leaderSession.Username);
    }

    private static bool HandleLeaveGroup(string username, TerminalEmulator terminal)
    {
        var groupSystem = GroupSystem.Instance;
        if (groupSystem == null) return true;

        var group = groupSystem.GetGroupFor(username);
        if (group == null)
        {
            terminal.SetColor("gray");
            terminal.WriteLine("  You are not in a group.");
            return true;
        }

        if (group.IsLeader(username))
        {
            terminal.SetColor("yellow");
            terminal.WriteLine("  You are the group leader. Use /disband to disband the group.");
            return true;
        }

        groupSystem.RemoveMember(username, "left voluntarily");
        terminal.SetColor("bright_green");
        terminal.WriteLine("  You have left the group.");
        return true;
    }

    private static bool HandleDisbandGroup(string username, TerminalEmulator terminal)
    {
        var groupSystem = GroupSystem.Instance;
        if (groupSystem == null) return true;

        var group = groupSystem.GetGroupFor(username);
        if (group == null)
        {
            terminal.SetColor("gray");
            terminal.WriteLine("  You are not in a group.");
            return true;
        }

        if (!group.IsLeader(username))
        {
            terminal.SetColor("yellow");
            terminal.WriteLine("  Only the group leader can disband the group.");
            return true;
        }

        groupSystem.DisbandGroup(username, "leader disbanded the group");
        terminal.SetColor("bright_green");
        terminal.WriteLine("  Your group has been disbanded.");
        return true;
    }
}
