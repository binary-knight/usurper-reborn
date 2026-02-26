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

            case "title":
                return HandleTitle(username, args, terminal);

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

    /// <summary>
    /// Returns the display name for a player: character name (not login name).
    /// Gods: "DivineName the Lesser Spirit", others: Name2 → Name1 → login username fallback.
    /// </summary>
    private static string GetChatDisplayName(string username)
    {
        var server = MudServer.Instance;
        if (server != null && server.ActiveSessions.TryGetValue(username.ToLowerInvariant(), out var session))
            return GetSessionDisplayName(session, username);
        return username;
    }

    /// <summary>
    /// Returns the character display name for a session (never the raw login username unless no character exists yet).
    /// </summary>
    private static string GetSessionDisplayName(PlayerSession session, string fallback = "")
    {
        var playerObj = session.Context?.Engine?.CurrentPlayer;
        if (playerObj?.IsImmortal == true && !string.IsNullOrEmpty(playerObj.DivineName))
        {
            int godIdx = Math.Clamp(playerObj.GodLevel - 1, 0, GameConfig.GodTitles.Length - 1);
            return $"{playerObj.DivineName} the {GameConfig.GodTitles[godIdx]}";
        }
        if (!string.IsNullOrEmpty(playerObj?.Name2)) return playerObj.Name2;
        if (!string.IsNullOrEmpty(playerObj?.Name1)) return playerObj.Name1;
        return string.IsNullOrEmpty(fallback) ? session.Username : fallback;
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
        var displayName = GetChatDisplayName(username);
        RoomRegistry.Instance.BroadcastToRoom(
            location.Value,
            $"\u001b[1;37m  {displayName} says: {message}\u001b[0m",
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
        var displayName = GetChatDisplayName(username);
        RoomRegistry.Instance!.BroadcastGlobal(
            $"\u001b[1;33m  {displayName} shouts: {message}\u001b[0m",
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
        var displayName = GetChatDisplayName(username);
        var server = MudServer.Instance;
        if (server != null && server.SendToPlayer(targetName,
            $"\u001b[35m  {displayName} tells you: {message}\u001b[0m"))
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
        var displayName = GetChatDisplayName(username);
        terminal.SetColor("bright_cyan");
        terminal.WriteLine($"  * {displayName} {action}");

        // Broadcast to others in the room
        RoomRegistry.Instance.BroadcastToRoom(
            location.Value,
            $"\u001b[1;36m  * {displayName} {action}\u001b[0m",
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
        var displayName = GetChatDisplayName(username);
        RoomRegistry.Instance!.BroadcastGlobal(
            $"\u001b[92m  [Gossip] {displayName}: {message}\u001b[0m",
            excludeUsername: username);

        return true;
    }

    private static bool HandleTitle(string username, string args, TerminalEmulator terminal)
    {
        var session = MudServer.Instance?.ActiveSessions.GetValueOrDefault(username.ToLowerInvariant());
        var player = session?.Context?.Engine?.CurrentPlayer;
        if (player == null) return true;

        if (string.IsNullOrWhiteSpace(args))
        {
            player.MudTitle = "";
            terminal.SetColor("gray");
            terminal.WriteLine("  Your title has been cleared.");
        }
        else
        {
            player.MudTitle = args.Trim();
            terminal.SetColor("gray");
            terminal.Write("  Title set to: ");
            terminal.WriteRawAnsi(player.MudTitle);
            terminal.WriteLine("");
        }

        // Save immediately so the title persists even if the server restarts before the next auto-save
        _ = UsurperRemake.Systems.SaveSystem.Instance.AutoSave(player);

        return true;
    }

    private static string WhoClassTag(Character? player, WizardLevel wizLevel)
    {
        int lv = player?.Level ?? 0;
        return wizLevel switch
        {
            WizardLevel.Implementor => "-- IMP",
            WizardLevel.God        => $"{lv,2} GOD",
            WizardLevel.Archwizard => $"{lv,2} AWiz",
            WizardLevel.Wizard     => $"{lv,2}  Wiz",
            WizardLevel.Immortal   => $"{lv,2}  Imm",
            WizardLevel.Builder    => $"{lv,2}  Bld",
            _ => player?.IsImmortal == true
                    ? $"-- Gd{Math.Clamp(player.GodLevel, 1, 9)}"
                    : $"{lv,2} {WhoClassAbbrev(player?.Class ?? CharacterClass.Warrior)}"
        };
    }

    private static string WhoClassAbbrev(CharacterClass cls) => cls switch
    {
        CharacterClass.Alchemist    => "Alch",
        CharacterClass.Assassin     => "Assn",
        CharacterClass.Barbarian    => "Barb",
        CharacterClass.Bard         => "Bard",
        CharacterClass.Cleric       => "Cler",
        CharacterClass.Jester       => "Jest",
        CharacterClass.Magician     => "Magi",
        CharacterClass.Paladin      => "Pala",
        CharacterClass.Ranger       => "Rang",
        CharacterClass.Sage         => "Sage",
        CharacterClass.Warrior      => "Warr",
        CharacterClass.Tidesworn    => "Tide",
        CharacterClass.Wavecaller   => "Wave",
        CharacterClass.Cyclebreaker => "Cycl",
        CharacterClass.Abysswarden  => "Abys",
        CharacterClass.Voidreaver   => "Void",
        _                           => "????"
    };

    private static string WhoColor(WizardLevel wizLevel, bool isPlayerGod, bool isYou)
    {
        if (isYou) return "bright_green";
        return wizLevel switch
        {
            WizardLevel.Implementor => "bright_white",
            WizardLevel.God        => "bright_red",
            WizardLevel.Archwizard => "bright_magenta",
            WizardLevel.Wizard     => "bright_yellow",
            WizardLevel.Immortal   => "bright_cyan",
            WizardLevel.Builder    => "cyan",
            _ => isPlayerGod ? "bright_yellow" : "white"
        };
    }

    private static bool HandleWho(string username, TerminalEmulator terminal)
    {
        var server = MudServer.Instance;
        if (server == null) return true;

        var myWizLevel = SessionContext.Current?.WizardLevel ?? WizardLevel.Mortal;
        var sessions = server.ActiveSessions.Values
            .Where(s => !s.IsWizInvisible || myWizLevel >= s.WizardLevel)
            .OrderByDescending(s => (int)s.WizardLevel)
            .ThenByDescending(s => s.Context?.Engine?.CurrentPlayer?.IsImmortal == true ? 1 : 0)
            .ThenByDescending(s => s.Context?.Engine?.CurrentPlayer?.Level ?? 0)
            .ThenBy(s => s.Username)
            .ToList();

        terminal.SetColor("bright_cyan");
        terminal.WriteLine("━━━━━━━━━━━━━━━━━━━━━━━━━━━━━ Who's Online ━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━");

        if (sessions.Count == 0)
        {
            terminal.SetColor("gray");
            terminal.WriteLine("  No players online.");
        }
        else
        {
            foreach (var session in sessions)
            {
                var player = session.Context?.Engine?.CurrentPlayer;
                var wizLevel = session.WizardLevel;
                bool isYou = session.Username.Equals(username, StringComparison.OrdinalIgnoreCase);
                bool isPlayerGod = player?.IsImmortal == true && wizLevel == WizardLevel.Mortal;

                string tag  = WhoClassTag(player, wizLevel);
                string color = WhoColor(wizLevel, isPlayerGod, isYou);

                // Display name: DivineName for player-gods, character name (Name2/Name1) for everyone else,
                // falling back to the login username only if no character name is set yet.
                string rawName;
                if (isPlayerGod && !string.IsNullOrEmpty(player!.DivineName))
                    rawName = player.DivineName;
                else if (!string.IsNullOrEmpty(player?.Name2))
                    rawName = player.Name2;
                else if (!string.IsNullOrEmpty(player?.Name1))
                    rawName = player.Name1;
                else
                    rawName = session.Username;
                string name = rawName.Length > 0
                    ? char.ToUpper(rawName[0]) + rawName.Substring(1)
                    : rawName;

                // Title priority: custom > wizard rank > god rank > (none)
                string title = "";
                if (!string.IsNullOrEmpty(player?.MudTitle))
                    title = " " + player.MudTitle;
                else if (wizLevel > WizardLevel.Mortal)
                    title = $" the {WizardConstants.GetTitle(wizLevel)}";
                else if (isPlayerGod)
                {
                    int godIdx = Math.Clamp(player!.GodLevel - 1, 0, GameConfig.GodTitles.Length - 1);
                    title = $" the {GameConfig.GodTitles[godIdx]}";
                }

                // Extra tags
                string invisTag = (session.IsWizInvisible && myWizLevel >= wizLevel) ? " \u001b[1;31m[INVIS]\u001b[0m" : "";

                // Render line
                terminal.SetColor(color);
                terminal.Write($" [{tag}] ");
                if (isPlayerGod)
                    terminal.Write("★ ", "bright_yellow");
                terminal.Write(name, color);
                if (!string.IsNullOrEmpty(title))
                    terminal.WriteRawAnsi(title);
                if (invisTag.Length > 0)
                {
                    terminal.Write(" ");
                    terminal.WriteRawAnsi(invisTag);
                }
                terminal.WriteLine("");
            }
        }

        terminal.SetColor("bright_cyan");
        terminal.WriteLine("━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━");
        int wizCount = sessions.Count(s => s.WizardLevel > WizardLevel.Mortal);
        int immortalCount = sessions.Count(s => s.Context?.Engine?.CurrentPlayer?.IsImmortal == true && s.WizardLevel == WizardLevel.Mortal);
        string summary = $"  {sessions.Count} player(s) online";
        if (wizCount > 0) summary += $",  {wizCount} admin";
        if (immortalCount > 0) summary += $",  {immortalCount} immortal";
        terminal.SetColor("gray");
        terminal.WriteLine(summary);
        terminal.SetColor("bright_cyan");
        terminal.WriteLine("━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━");
        terminal.WriteLine("  Tip: /title <text>  to set your title  |  ANSI color codes supported");

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
            terminal.WriteLine($"  You accepted {GetSessionDisplayName(invite.Inviter, invite.Inviter.Username)}'s group invite.");
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
            terminal.WriteLine($"  You accepted {GetSessionDisplayName(request.Requester, request.Requester.Username)}'s spectate request.");
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
            terminal.WriteLine($"  You denied {GetSessionDisplayName(invite.Inviter, invite.Inviter.Username)}'s group invite.");
            return true;
        }

        // Check for pending spectate request
        if (session.PendingSpectateRequest != null)
        {
            var request = session.PendingSpectateRequest;
            session.PendingSpectateRequest = null;
            request.Response.TrySetResult(false);
            terminal.SetColor("yellow");
            terminal.WriteLine($"  You denied {GetSessionDisplayName(request.Requester, request.Requester.Username)}'s spectate request.");
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
            terminal.WriteLine($"    - {GetSessionDisplayName(spectator, spectator.Username)}");
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
                $"\u001b[1;33m  * {GetChatDisplayName(username)} has ended the spectator session.\u001b[0m");
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
                var displayName = memberSession != null
                    ? GetSessionDisplayName(memberSession, member)
                    : member;

                terminal.SetColor(isLeader ? "bright_yellow" : "white");
                terminal.WriteLine($"    {displayName}{statusTag}{levelStr}");
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
            terminal.WriteLine($"  {GetSessionDisplayName(targetSession, targetSession.Username)} is currently spectating and cannot be invited.");
            return true;
        }

        // Can't invite someone already in a group
        var targetGroup = groupSystem.GetGroupFor(targetSession.Username);
        if (targetGroup != null)
        {
            terminal.SetColor("yellow");
            terminal.WriteLine($"  {GetSessionDisplayName(targetSession, targetSession.Username)} is already in a group.");
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
                terminal.WriteLine($"  {GetSessionDisplayName(targetSession, targetSession.Username)} is not on your team ({myPlayer.Team}).");
                return true;
            }
        }

        // Check target level
        if (targetPlayer != null && targetPlayer.Level < GameConfig.GroupMinLevel)
        {
            terminal.SetColor("yellow");
            terminal.WriteLine($"  {GetSessionDisplayName(targetSession, targetSession.Username)} must be at least level {GameConfig.GroupMinLevel} to join a group.");
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
            terminal.WriteLine($"  {GetSessionDisplayName(targetSession, targetSession.Username)} already has a pending group invite.");
            return true;
        }

        // Send the invite
        var invite = new GroupInvite { Inviter = mySession };
        targetSession.PendingGroupInvite = invite;

        var myDisplayName = GetChatDisplayName(username);
        var targetDisplayName = GetSessionDisplayName(targetSession, targetSession.Username);
        targetSession.EnqueueMessage(
            $"\u001b[1;33m  * {myDisplayName} has invited you to join their dungeon group.\u001b[0m");
        targetSession.EnqueueMessage(
            $"\u001b[1;33m  * Type /accept to join or /deny to refuse. ({GameConfig.GroupInviteTimeoutSeconds}s)\u001b[0m");

        terminal.SetColor("bright_cyan");
        terminal.WriteLine($"  Group invite sent to {targetDisplayName}. ({GameConfig.GroupInviteTimeoutSeconds}s to respond)");

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
                    $"\u001b[1;33m  * The group invite from {GetSessionDisplayName(leaderSession, leaderSession.Username)} has expired.\u001b[0m");
            }
        }
        catch
        {
            accepted = false;
            invite.Response.TrySetResult(false);
            targetSession.PendingGroupInvite = null;
        }

        var targetName = GetSessionDisplayName(targetSession, targetSession.Username);
        if (!accepted)
        {
            leaderSession.EnqueueMessage(
                $"\u001b[1;33m  * {targetName} denied your group invite (or it timed out).\u001b[0m");

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
                $"\u001b[1;33m  * Failed to add {targetName} — group may be full.\u001b[0m");
            return;
        }

        leaderSession.EnqueueMessage(
            $"\u001b[1;32m  * {targetName} has joined your group!\u001b[0m");

        // Notify all other group members
        groupSystem.NotifyGroup(group,
            $"\u001b[1;32m  * {targetName} has joined the group!\u001b[0m",
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
