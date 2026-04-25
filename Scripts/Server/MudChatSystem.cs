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
    private static readonly string[] ChatCommands = { "say", "s", "shout", "tell", "t", "emote", "me", "gossip", "gos",
        "guild", "gcreate", "ginvite", "gleave", "gkick", "ginfo", "gc", "gbank", "gwithdraw", "gdeposit", "grank", "gtransfer" };

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
                return await HandleGroup(username, args, terminal);

            case "leave":
                return HandleLeaveGroup(username, terminal);

            case "disband":
                return HandleDisbandGroup(username, terminal);

            // Guild commands (v0.52.0)
            case "guild":
                return HandleGuildInfo(username, terminal);
            case "gcreate":
                return HandleGuildCreate(username, args, terminal);
            case "ginvite":
                return HandleGuildInvite(username, args, terminal);
            case "gleave":
                return HandleGuildLeave(username, terminal);
            case "gkick":
                return HandleGuildKick(username, args, terminal);
            case "ginfo":
                return HandleGuildLookup(username, args, terminal);
            case "gc":
                return HandleGuildChat(username, args, terminal);
            case "gbank":
                return HandleGuildBank(username, args, terminal);
            case "gwithdraw":
                return HandleGuildWithdrawItem(username, args, terminal);
            case "gdeposit":
                return HandleGuildDepositItem(username, args, terminal);
            case "grank":
                return HandleGuildRank(username, args, terminal);
            case "gtransfer":
                return HandleGuildTransfer(username, args, terminal);

            default:
                return false; // Not a MUD chat command
        }
    }

    /// <summary>
    /// Find a session by character display name or login username.
    /// Tries character display name first, then falls back to login username.
    /// </summary>
    private static PlayerSession? FindSessionByNameOrUsername(string name)
    {
        var server = MudServer.Instance;
        if (server == null) return null;

        // Try character display name first
        var session = server.ActiveSessions.Values.FirstOrDefault(s =>
            s.ActiveCharacterName?.Equals(name, StringComparison.OrdinalIgnoreCase) == true);
        if (session != null) return session;

        // Fall back to login username
        return server.ActiveSessions.Values.FirstOrDefault(s =>
            s.Username.Equals(name, StringComparison.OrdinalIgnoreCase));
    }

    /// <summary>
    /// Returns the display name for a player: character name (not login name).
    /// Gods: "DivineName the Lesser Spirit", others: Name2 → Name1 → login username fallback.
    /// </summary>
    // v0.57.12: PR #84 widened this to `public` with no new caller — reverted to `private` since the
    // prison chat integration uses TryProcessCommand (public) and doesn't need direct access.
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

    /// <summary>
    /// v0.57.14: Toggle a channel mute on the player attached to this session and
    /// echo the new state. Returns true (always handled). Used when a player types
    /// /gos / /shout / /gc / /tell with no message — empty args means "toggle mute"
    /// rather than "show usage."
    /// </summary>
    private static bool ToggleChannelMute(string username, string channelKey, string friendlyName, string sendUsage, TerminalEmulator terminal)
    {
        var server = MudServer.Instance;
        if (server == null || !server.ActiveSessions.TryGetValue(username.ToLowerInvariant(), out var session))
        {
            terminal.SetColor("gray");
            terminal.WriteLine($"  {sendUsage}");
            return true;
        }

        var player = session.Context?.Engine?.CurrentPlayer;
        if (player == null)
        {
            terminal.SetColor("gray");
            terminal.WriteLine($"  {sendUsage}");
            return true;
        }

        if (player.MutedChannels == null)
            player.MutedChannels = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        if (player.MutedChannels.Contains(channelKey))
        {
            player.MutedChannels.Remove(channelKey);
            terminal.SetColor("bright_green");
            terminal.WriteLine($"  {friendlyName} channel UNMUTED. You will receive {friendlyName.ToLowerInvariant()} messages again.");
            terminal.SetColor("gray");
            terminal.WriteLine($"  ({sendUsage}; type the command alone to mute again.)");
        }
        else
        {
            player.MutedChannels.Add(channelKey);
            terminal.SetColor("bright_yellow");
            terminal.WriteLine($"  {friendlyName} channel MUTED. You will no longer see {friendlyName.ToLowerInvariant()} messages.");
            terminal.SetColor("gray");
            terminal.WriteLine($"  ({sendUsage}; type the command alone to unmute.)");
        }

        return true;
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
        // v0.57.14: empty message toggles mute instead of showing usage hint.
        if (string.IsNullOrWhiteSpace(message))
        {
            return ToggleChannelMute(username, "shout", "Shout", "Usage: /shout <message>", terminal);
        }

        // Show to sender
        terminal.SetColor("bright_yellow");
        terminal.WriteLine($"  You shout: {message}");

        // Broadcast to ALL connected players
        var displayName = GetChatDisplayName(username);
        RoomRegistry.Instance!.BroadcastGlobal(
            $"\u001b[1;33m  {displayName} shouts: {message}\u001b[0m",
            excludeUsername: username,
            channelKey: "shout");

        return true;
    }

    private static bool HandleTell(string username, string args, TerminalEmulator terminal)
    {
        // v0.57.14: completely empty args toggles incoming-tell mute (anti-harassment).
        if (string.IsNullOrWhiteSpace(args))
        {
            return ToggleChannelMute(username, "tell", "Incoming tells", "Usage: /tell <player> <message>", terminal);
        }

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

        // Try to send in-memory first — resolve by display name or username
        var displayName = GetChatDisplayName(username);
        var targetSession = FindSessionByNameOrUsername(targetName);
        if (targetSession != null)
        {
            // v0.57.14: respect target's incoming-tell mute.
            var targetPlayer = targetSession.Context?.Engine?.CurrentPlayer;
            if (targetPlayer?.MutedChannels?.Contains("tell") == true)
            {
                var blockedTargetName = GetSessionDisplayName(targetSession, targetName);
                terminal.SetColor("gray");
                terminal.WriteLine($"  {blockedTargetName} has private messages muted. Your tell was not delivered.");
                return true;
            }

            targetSession.EnqueueMessage($"\u001b[35m  {displayName} tells you: {message}\u001b[0m");
            var targetDisplayName = GetSessionDisplayName(targetSession, targetName);
            terminal.SetColor("magenta");
            terminal.WriteLine($"  You tell {targetDisplayName}: {message}");
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
        // v0.57.14: empty message toggles mute instead of showing usage hint.
        if (string.IsNullOrWhiteSpace(message))
        {
            return ToggleChannelMute(username, "gossip", "Gossip", "Usage: /gossip <message>  (or /gos)", terminal);
        }

        // Show to sender
        terminal.SetColor("bright_green");
        terminal.WriteLine($"  [Gossip] You: {message}");

        // Broadcast to ALL connected players (global out-of-character channel)
        var displayName = GetChatDisplayName(username);
        RoomRegistry.Instance!.BroadcastGlobal(
            $"\u001b[92m  [Gossip] {displayName}: {message}\u001b[0m",
            excludeUsername: username,
            channelKey: "gossip");

        // v0.57.13: mirror to Discord #in-game-gossip if the bridge is configured.
        // No-op if DiscordBridge wasn't initialized (non-online modes, missing DB path).
        UsurperRemake.Systems.DiscordBridge.QueueOutbound(displayName, message);

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
        CharacterClass.MysticShaman => "Sham",
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

        if (GameConfig.ScreenReaderMode)
        {
            terminal.SetColor("bright_cyan");
            terminal.WriteLine("Who's Online");
        }
        else
        {
            terminal.SetColor("bright_cyan");
            terminal.WriteLine("━━━━━━━━━━━━━━━━━━━━━━━━━━━━━ Who's Online ━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━");
        }

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

                // Prepend noble title (Sir/Dame) for knighted players
                if (player?.IsKnighted == true)
                    name = $"{player.NobleTitle} {name}";

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

                // Guild tag
                string guildTag = "";
                var guildSystem = Systems.GuildSystem.Instance;
                if (guildSystem != null)
                {
                    var guildName = guildSystem.GetPlayerGuildDisplayName(session.Username);
                    if (!string.IsNullOrEmpty(guildName))
                        guildTag = $" <{guildName}>";
                }

                // Render line
                terminal.SetColor(color);
                terminal.Write($" [{tag}] ");
                if (isPlayerGod)
                    terminal.Write(GameConfig.ScreenReaderMode ? "(Immortal) " : "★ ", "bright_yellow");
                terminal.Write(name, color);
                if (!string.IsNullOrEmpty(title))
                    terminal.WriteRawAnsi(title);
                if (!string.IsNullOrEmpty(guildTag))
                    terminal.Write(guildTag, "dark_cyan");
                if (invisTag.Length > 0)
                {
                    terminal.Write(" ");
                    terminal.WriteRawAnsi(invisTag);
                }
                terminal.WriteLine("");
            }
        }

        if (!GameConfig.ScreenReaderMode)
        {
            terminal.SetColor("bright_cyan");
            terminal.WriteLine("━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━");
        }
        int wizCount = sessions.Count(s => s.WizardLevel > WizardLevel.Mortal);
        int immortalCount = sessions.Count(s => s.Context?.Engine?.CurrentPlayer?.IsImmortal == true && s.WizardLevel == WizardLevel.Mortal);
        string summary = $"  {sessions.Count} player(s) online";
        if (wizCount > 0) summary += $",  {wizCount} admin";
        if (immortalCount > 0) summary += $",  {immortalCount} immortal";
        terminal.SetColor("gray");
        terminal.WriteLine(summary);
        if (!GameConfig.ScreenReaderMode)
        {
            terminal.SetColor("bright_cyan");
            terminal.WriteLine("━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━");
        }
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

        // Check for pending guild invite
        var guildSystem = Systems.GuildSystem.Instance;
        if (guildSystem != null)
        {
            var guildInvite = guildSystem.GetPendingInvite(username);
            if (guildInvite != null)
            {
                var error = guildSystem.AcceptInvite(username);
                if (error != null)
                {
                    terminal.SetColor("red");
                    terminal.WriteLine($"  {error}");
                }
                else
                {
                    terminal.SetColor("bright_green");
                    terminal.WriteLine($"  {Systems.Loc.Get("guild.invite_accepted", guildInvite.GuildDisplayName)}");
                }
                return true;
            }
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

        // Check for pending guild invite
        var guildSystemDeny = Systems.GuildSystem.Instance;
        if (guildSystemDeny != null)
        {
            var guildInvite = guildSystemDeny.GetPendingInvite(username);
            if (guildInvite != null)
            {
                guildSystemDeny.DeclineInvite(username);
                terminal.SetColor("yellow");
                terminal.WriteLine($"  {Systems.Loc.Get("guild.invite_declined", guildInvite.InviterName)}");
                return true;
            }
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

            if (!GameConfig.ScreenReaderMode)
            {
                terminal.SetColor("bright_cyan");
                terminal.WriteLine("  ═══════════════════════════════════════════");
            }
            terminal.SetColor("bright_white");
            terminal.WriteLine("  Your Group:");
            if (!GameConfig.ScreenReaderMode)
            {
                terminal.SetColor("bright_cyan");
                terminal.WriteLine("  ═══════════════════════════════════════════");
            }

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

        // Find target player — resolve by display name or username
        var targetSession = FindSessionByNameOrUsername(targetName);
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

    // ============ GUILD COMMANDS (v0.52.0) ============

    private static bool HandleGuildInfo(string username, TerminalEmulator terminal)
    {
        var guild = Systems.GuildSystem.Instance;
        if (guild == null) { terminal.WriteLine($"  {Systems.Loc.Get("guild.not_available")}", "yellow"); return true; }

        var guildName = guild.GetPlayerGuild(username);
        if (guildName == null)
        {
            terminal.SetColor("yellow");
            terminal.WriteLine($"  {Systems.Loc.Get("guild.not_in_guild")}");
            terminal.WriteLine($"  {Systems.Loc.Get("guild.hint_no_guild_create")}");
            terminal.WriteLine($"  {Systems.Loc.Get("guild.hint_no_guild_info")}");
            return true;
        }

        var info = guild.GetGuildInfo(guildName);
        if (info == null) { terminal.WriteLine($"  {Systems.Loc.Get("guild.not_found")}", "red"); return true; }

        terminal.SetColor("bright_yellow");
        terminal.WriteLine($"  {Systems.Loc.Get("guild.label_guild", info.DisplayName)}");
        if (!string.IsNullOrEmpty(info.Motto))
            terminal.WriteLine($"  {Systems.Loc.Get("guild.label_motto", info.Motto)}");
        terminal.SetColor("cyan");
        string leaderDisplay = info.Members.FirstOrDefault(m => m.Rank == "Leader")?.DisplayName ?? info.LeaderUsername;
        terminal.WriteLine($"  {Systems.Loc.Get("guild.label_leader_members", leaderDisplay, info.MemberCount, Systems.GuildSystem.MaxGuildMembers)}");
        terminal.WriteLine($"  {Systems.Loc.Get("guild.label_bank_gold", info.BankGold.ToString("N0"))}");
        double bonus = Math.Min(info.MemberCount * Systems.GuildSystem.GuildXPBonusPerMember, Systems.GuildSystem.MaxGuildXPBonus) * 100;
        terminal.WriteLine($"  {Systems.Loc.Get("guild.label_xp_bonus", bonus.ToString("F0"))}");
        var bankItems = guild.GetBankItems(guildName);
        if (bankItems.Count > 0)
            terminal.WriteLine($"  {Systems.Loc.Get("guild.label_bank_items", bankItems.Count, Systems.GuildSystem.MaxBankItems)}");
        terminal.SetColor("gray");
        terminal.WriteLine($"  {Systems.Loc.Get("guild.label_members")}");
        foreach (var m in info.Members)
        {
            string rankTag = m.Rank != "Member" ? $" [{m.Rank}]" : "";
            terminal.WriteLine($"    {m.DisplayName}{rankTag}");
        }
        terminal.SetColor("gray");
        terminal.WriteLine("");
        terminal.WriteLine($"  {Systems.Loc.Get("guild.hint_gbank")}");
        terminal.WriteLine($"  {Systems.Loc.Get("guild.hint_gdeposit")}");
        terminal.WriteLine($"  {Systems.Loc.Get("guild.hint_grank")}");
        return true;
    }

    private static bool HandleGuildCreate(string username, string args, TerminalEmulator terminal)
    {
        var guild = Systems.GuildSystem.Instance;
        if (guild == null) { terminal.WriteLine($"  {Systems.Loc.Get("guild.not_available")}", "yellow"); return true; }

        if (string.IsNullOrWhiteSpace(args))
        {
            terminal.WriteLine($"  {Systems.Loc.Get("guild.usage_gcreate")}", "yellow");
            return true;
        }

        // Check gold
        var session = MudServer.Instance?.ActiveSessions.TryGetValue(username.ToLowerInvariant(), out var s) == true ? s : null;
        var player = session?.Context?.Engine?.CurrentPlayer;
        if (player == null) { terminal.WriteLine($"  {Systems.Loc.Get("guild.cannot_access_character")}", "red"); return true; }

        if (player.Gold < Systems.GuildSystem.GuildCreationCost)
        {
            terminal.WriteLine($"  {Systems.Loc.Get("guild.create_cost", Systems.GuildSystem.GuildCreationCost.ToString("N0"), player.Gold.ToString("N0"))}", "yellow");
            return true;
        }

        string guildName = args.Trim();
        var error = guild.CreateGuild(username, guildName, guildName);
        if (error != null)
        {
            terminal.WriteLine($"  {error}", "red");
            return true;
        }

        player.Gold -= Systems.GuildSystem.GuildCreationCost;
        _ = Systems.SaveSystem.Instance.AutoSave(player);
        terminal.SetColor("bright_green");
        terminal.WriteLine($"  {Systems.Loc.Get("guild.created", guildName)}");
        terminal.WriteLine($"  {Systems.Loc.Get("guild.hint_ginvite")}");

        // Broadcast
        try { MudServer.Instance?.BroadcastToAll($"\r\n\x1b[1;33m  {Systems.Loc.Get("guild.broadcast_founded", GetChatDisplayName(username), guildName)}\x1b[0m\r\n", excludeUsername: username); }
        catch { }

        return true;
    }

    private static bool HandleGuildInvite(string username, string args, TerminalEmulator terminal)
    {
        var guild = Systems.GuildSystem.Instance;
        if (guild == null) { terminal.WriteLine($"  {Systems.Loc.Get("guild.not_available")}", "yellow"); return true; }

        var guildName = guild.GetPlayerGuild(username);
        if (guildName == null) { terminal.WriteLine($"  {Systems.Loc.Get("guild.not_in_guild")}", "yellow"); return true; }

        string inviterRank = guild.GetMemberRank(username);
        if (!Systems.GuildSystem.RankCanInvite(inviterRank))
        {
            terminal.WriteLine($"  {Systems.Loc.Get("guild.only_leader_officer_invite")}", "yellow");
            return true;
        }

        if (string.IsNullOrWhiteSpace(args))
        {
            terminal.WriteLine($"  {Systems.Loc.Get("guild.usage_ginvite")}", "yellow");
            return true;
        }

        string target = args.Trim();
        var targetSession = MudServer.Instance?.ActiveSessions.TryGetValue(target.ToLowerInvariant(), out var ts) == true ? ts : null;
        if (targetSession == null)
        {
            terminal.WriteLine($"  {Systems.Loc.Get("guild.player_not_online", target)}", "yellow");
            return true;
        }

        // Check if target is already in a guild
        if (guild.GetPlayerGuild(target.ToLowerInvariant()) != null)
        {
            terminal.WriteLine($"  {Systems.Loc.Get("guild.player_already_in_guild", target)}", "yellow");
            return true;
        }

        // Get display name for the guild
        var guildInfo = guild.GetGuildInfo(guildName);
        string guildDisplayName = guildInfo?.DisplayName ?? guildName;

        guild.SendInvite(target, guildName, GetChatDisplayName(username), guildDisplayName);

        // Notify target
        try
        {
            targetSession.Context?.Terminal?.SetColor("bright_yellow");
            targetSession.Context?.Terminal?.WriteLine($"\r\n  {Systems.Loc.Get("guild.invite_received", GetChatDisplayName(username), guildDisplayName)}");
            targetSession.Context?.Terminal?.WriteLine($"  {Systems.Loc.Get("guild.invite_accept_deny")}\r\n");
        }
        catch { }

        terminal.SetColor("bright_green");
        terminal.WriteLine($"  {Systems.Loc.Get("guild.invite_sent", target)}");
        return true;
    }

    private static bool HandleGuildLeave(string username, TerminalEmulator terminal)
    {
        var guild = Systems.GuildSystem.Instance;
        if (guild == null) { terminal.WriteLine($"  {Systems.Loc.Get("guild.not_available")}", "yellow"); return true; }

        var guildName = guild.GetPlayerGuild(username);
        if (guildName == null) { terminal.WriteLine($"  {Systems.Loc.Get("guild.not_in_guild")}", "yellow"); return true; }

        // Broadcast to guild before leaving
        var online = guild.GetOnlineGuildMembers(guildName);
        string displayName = GetChatDisplayName(username);

        var error = guild.RemoveMember(username);
        if (error != null) { terminal.WriteLine($"  {error}", "red"); return true; }

        terminal.SetColor("bright_green");
        terminal.WriteLine($"  {Systems.Loc.Get("guild.you_left")}");

        // Notify remaining online members
        foreach (var member in online)
        {
            if (string.Equals(member, username, StringComparison.OrdinalIgnoreCase)) continue;
            var memberSession = MudServer.Instance?.ActiveSessions.TryGetValue(member.ToLowerInvariant(), out var ms) == true ? ms : null;
            try
            {
                memberSession?.Context?.Terminal?.SetColor("yellow");
                memberSession?.Context?.Terminal?.WriteLine($"\r\n  {Systems.Loc.Get("guild.member_left", displayName)}\r\n");
            }
            catch { }
        }
        return true;
    }

    private static bool HandleGuildKick(string username, string args, TerminalEmulator terminal)
    {
        var guild = Systems.GuildSystem.Instance;
        if (guild == null) { terminal.WriteLine($"  {Systems.Loc.Get("guild.not_available")}", "yellow"); return true; }

        var guildName = guild.GetPlayerGuild(username);
        if (guildName == null) { terminal.WriteLine($"  {Systems.Loc.Get("guild.not_in_guild")}", "yellow"); return true; }

        if (!guild.IsGuildLeader(username, guildName))
        {
            terminal.WriteLine($"  {Systems.Loc.Get("guild.only_leader_kick")}", "yellow");
            return true;
        }

        if (string.IsNullOrWhiteSpace(args))
        {
            terminal.WriteLine($"  {Systems.Loc.Get("guild.usage_gkick")}", "yellow");
            return true;
        }

        string target = args.Trim();
        if (string.Equals(target, username, StringComparison.OrdinalIgnoreCase))
        {
            terminal.WriteLine($"  {Systems.Loc.Get("guild.cannot_kick_self")}", "yellow");
            return true;
        }

        var targetGuild = guild.GetPlayerGuild(target);
        if (!string.Equals(targetGuild, guildName, StringComparison.OrdinalIgnoreCase))
        {
            terminal.WriteLine($"  {Systems.Loc.Get("guild.not_in_your_guild", target)}", "yellow");
            return true;
        }

        var error = guild.RemoveMember(target);
        if (error != null) { terminal.WriteLine($"  {error}", "red"); return true; }

        terminal.SetColor("bright_green");
        terminal.WriteLine($"  {Systems.Loc.Get("guild.player_kicked", target)}");

        // Notify kicked player
        var targetSession = MudServer.Instance?.ActiveSessions.TryGetValue(target.ToLowerInvariant(), out var ts) == true ? ts : null;
        try
        {
            targetSession?.Context?.Terminal?.SetColor("bright_red");
            targetSession?.Context?.Terminal?.WriteLine($"\r\n  {Systems.Loc.Get("guild.you_were_kicked", guildName, GetChatDisplayName(username))}\r\n");
        }
        catch { }

        // Notify remaining guild members
        NotifyGuildMembers(guild, guildName, username, Systems.Loc.Get("guild.broadcast_kicked", GetChatDisplayName(username), target));

        return true;
    }

    private static bool HandleGuildLookup(string username, string args, TerminalEmulator terminal)
    {
        var guild = Systems.GuildSystem.Instance;
        if (guild == null) { terminal.WriteLine($"  {Systems.Loc.Get("guild.not_available")}", "yellow"); return true; }

        if (string.IsNullOrWhiteSpace(args))
        {
            terminal.WriteLine($"  {Systems.Loc.Get("guild.usage_ginfo")}", "yellow");
            return true;
        }

        var info = guild.GetGuildInfo(args.Trim());
        if (info == null)
        {
            terminal.WriteLine($"  {Systems.Loc.Get("guild.lookup_not_found", args.Trim())}", "yellow");
            return true;
        }

        terminal.SetColor("bright_yellow");
        terminal.WriteLine($"  {Systems.Loc.Get("guild.label_guild", info.DisplayName)}");
        if (!string.IsNullOrEmpty(info.Motto))
            terminal.WriteLine($"  {Systems.Loc.Get("guild.label_motto", info.Motto)}");
        terminal.SetColor("cyan");
        string leaderLookup = info.Members.FirstOrDefault(m => m.Rank == "Leader")?.DisplayName ?? info.LeaderUsername;
        terminal.WriteLine($"  {Systems.Loc.Get("guild.label_leader_members", leaderLookup, info.MemberCount, Systems.GuildSystem.MaxGuildMembers)}");
        terminal.SetColor("gray");
        foreach (var m in info.Members)
        {
            string rankTag = m.Rank != "Member" ? $" [{m.Rank}]" : "";
            terminal.WriteLine($"    {m.DisplayName}{rankTag}");
        }
        return true;
    }

    private static bool HandleGuildChat(string username, string args, TerminalEmulator terminal)
    {
        var guild = Systems.GuildSystem.Instance;
        if (guild == null) { terminal.WriteLine($"  {Systems.Loc.Get("guild.not_available")}", "yellow"); return true; }

        var guildName = guild.GetPlayerGuild(username);
        if (guildName == null) { terminal.WriteLine($"  {Systems.Loc.Get("guild.not_in_guild")}", "yellow"); return true; }

        // v0.57.14: empty args toggles guild-chat mute
        if (string.IsNullOrWhiteSpace(args))
        {
            return ToggleChannelMute(username, "guild", "Guild chat", Systems.Loc.Get("guild.usage_gc"), terminal);
        }

        string chatLabel = Systems.Loc.Get("guild.chat_label");
        string displayName = GetChatDisplayName(username);
        var online = guild.GetOnlineGuildMembers(guildName);

        foreach (var member in online)
        {
            if (string.Equals(member, username, StringComparison.OrdinalIgnoreCase)) continue;
            var memberSession = MudServer.Instance?.ActiveSessions.TryGetValue(member.ToLowerInvariant(), out var ms) == true ? ms : null;

            // v0.57.14: skip recipients who have guild chat muted
            var memberPlayer = memberSession?.Context?.Engine?.CurrentPlayer;
            if (memberPlayer?.MutedChannels?.Contains("guild") == true) continue;

            try
            {
                memberSession?.Context?.Terminal?.SetColor("bright_green");
                memberSession?.Context?.Terminal?.WriteLine($"\r\n  {chatLabel} {displayName}: {args}\r\n");
            }
            catch { }
        }

        terminal.WriteLine($"  {Systems.Loc.Get("guild.chat_you", args)}", "bright_green");
        return true;
    }

    private static bool HandleGuildBank(string username, string args, TerminalEmulator terminal)
    {
        var guild = Systems.GuildSystem.Instance;
        if (guild == null) { terminal.WriteLine($"  {Systems.Loc.Get("guild.not_available")}", "yellow"); return true; }

        var guildName = guild.GetPlayerGuild(username);
        if (guildName == null) { terminal.WriteLine($"  {Systems.Loc.Get("guild.not_in_guild")}", "yellow"); return true; }

        var parts = (args ?? "").Trim().Split(' ', 2, StringSplitOptions.RemoveEmptyEntries);
        string subCommand = parts.Length > 0 ? parts[0].ToLowerInvariant() : "";
        string subArgs = parts.Length > 1 ? parts[1].Trim() : "";

        if (subCommand == "deposit" || subCommand == "d")
        {
            if (!long.TryParse(subArgs, out long amount) || amount <= 0)
            {
                terminal.WriteLine($"  {Systems.Loc.Get("guild.usage_gbank_deposit")}", "yellow");
                return true;
            }

            var session = MudServer.Instance?.ActiveSessions.TryGetValue(username.ToLowerInvariant(), out var s) == true ? s : null;
            var player = session?.Context?.Engine?.CurrentPlayer;
            if (player == null) { terminal.WriteLine($"  {Systems.Loc.Get("guild.cannot_access_character")}", "red"); return true; }

            if (player.Gold < amount)
            {
                terminal.WriteLine($"  {Systems.Loc.Get("guild.only_have_gold", player.Gold.ToString("N0"))}", "yellow");
                return true;
            }

            var error = guild.DepositGold(username, amount);
            if (error != null) { terminal.WriteLine($"  {error}", "red"); return true; }

            player.Gold -= amount;
            _ = Systems.SaveSystem.Instance.AutoSave(player);
            terminal.SetColor("bright_green");
            terminal.WriteLine($"  {Systems.Loc.Get("guild.deposited_gold", amount.ToString("N0"))}");

            NotifyGuildMembers(guild, guildName, username, Systems.Loc.Get("guild.broadcast_deposited_gold", GetChatDisplayName(username), amount.ToString("N0")));
            return true;
        }

        if (subCommand == "withdraw" || subCommand == "w")
        {
            if (!long.TryParse(subArgs, out long amount) || amount <= 0)
            {
                terminal.WriteLine($"  {Systems.Loc.Get("guild.usage_gbank_withdraw")}", "yellow");
                return true;
            }

            var session = MudServer.Instance?.ActiveSessions.TryGetValue(username.ToLowerInvariant(), out var s) == true ? s : null;
            var player = session?.Context?.Engine?.CurrentPlayer;
            if (player == null) { terminal.WriteLine($"  {Systems.Loc.Get("guild.cannot_access_character")}", "red"); return true; }

            var error = guild.WithdrawGold(username, amount);
            if (error != null) { terminal.WriteLine($"  {error}", "yellow"); return true; }

            player.Gold += amount;
            _ = Systems.SaveSystem.Instance.AutoSave(player);
            terminal.SetColor("bright_green");
            terminal.WriteLine($"  {Systems.Loc.Get("guild.withdrew_gold", amount.ToString("N0"))}");

            NotifyGuildMembers(guild, guildName, username, Systems.Loc.Get("guild.broadcast_withdrew_gold", GetChatDisplayName(username), amount.ToString("N0")));
            return true;
        }

        if (subCommand == "items" || subCommand == "i")
        {
            var items = guild.GetBankItems(guildName);
            if (items.Count == 0)
            {
                terminal.WriteLine($"  {Systems.Loc.Get("guild.bank_no_items")}", "gray");
                return true;
            }
            terminal.SetColor("bright_yellow");
            terminal.WriteLine($"  {Systems.Loc.Get("guild.bank_items_header", items.Count, Systems.GuildSystem.MaxBankItems)}");
            for (int i = 0; i < items.Count; i++)
            {
                terminal.SetColor("cyan");
                terminal.WriteLine($"    {Systems.Loc.Get("guild.bank_items_entry", items[i].Id, items[i].ItemName, items[i].DepositedBy)}");

                // Show item stats from stored JSON
                try
                {
                    var item = System.Text.Json.JsonSerializer.Deserialize<Item>(items[i].ItemJson);
                    if (item != null)
                    {
                        var stats = new System.Collections.Generic.List<string>();
                        if (item.Attack > 0) stats.Add($"ATK:{item.Attack}");
                        if (item.Armor > 0) stats.Add($"AP:{item.Armor}");
                        if (item.Strength != 0) stats.Add($"STR:{item.Strength:+#;-#}");
                        if (item.Defence != 0) stats.Add($"DEF:{item.Defence:+#;-#}");
                        if (item.Dexterity != 0) stats.Add($"DEX:{item.Dexterity:+#;-#}");
                        if (item.Agility != 0) stats.Add($"AGI:{item.Agility:+#;-#}");
                        if (item.Wisdom != 0) stats.Add($"WIS:{item.Wisdom:+#;-#}");
                        if (item.Charisma != 0) stats.Add($"CHA:{item.Charisma:+#;-#}");
                        if (item.HP != 0) stats.Add($"HP:{item.HP:+#;-#}");
                        if (item.Mana != 0) stats.Add($"MP:{item.Mana:+#;-#}");
                        if (stats.Count > 0)
                        {
                            terminal.SetColor("gray");
                            terminal.WriteLine($"      {string.Join("  ", stats)}");
                        }
                    }
                }
                catch { /* Skip stats if JSON can't be parsed */ }
            }
            terminal.SetColor("gray");
            terminal.WriteLine($"  {Systems.Loc.Get("guild.hint_gwithdraw_item")}");
            return true;
        }

        // Legacy: /gbank <number> treated as deposit for backwards compatibility
        if (long.TryParse(subCommand, out long legacyAmount) && legacyAmount > 0)
        {
            var session = MudServer.Instance?.ActiveSessions.TryGetValue(username.ToLowerInvariant(), out var s) == true ? s : null;
            var player = session?.Context?.Engine?.CurrentPlayer;
            if (player == null) { terminal.WriteLine($"  {Systems.Loc.Get("guild.cannot_access_character")}", "red"); return true; }

            if (player.Gold < legacyAmount)
            {
                terminal.WriteLine($"  {Systems.Loc.Get("guild.only_have_gold", player.Gold.ToString("N0"))}", "yellow");
                return true;
            }

            var error = guild.DepositGold(username, legacyAmount);
            if (error != null) { terminal.WriteLine($"  {error}", "red"); return true; }

            player.Gold -= legacyAmount;
            _ = Systems.SaveSystem.Instance.AutoSave(player);
            terminal.SetColor("bright_green");
            terminal.WriteLine($"  {Systems.Loc.Get("guild.deposited_gold", legacyAmount.ToString("N0"))}");

            NotifyGuildMembers(guild, guildName, username, Systems.Loc.Get("guild.broadcast_deposited_gold", GetChatDisplayName(username), legacyAmount.ToString("N0")));
            return true;
        }

        // Show usage
        var info = guild.GetGuildInfo(guildName);
        terminal.SetColor("bright_yellow");
        terminal.WriteLine($"  {Systems.Loc.Get("guild.bank_gold_label", (info?.BankGold ?? 0).ToString("N0"))}");
        terminal.SetColor("gray");
        terminal.WriteLine($"  {Systems.Loc.Get("guild.hint_bank_deposit")}");
        terminal.WriteLine($"  {Systems.Loc.Get("guild.hint_bank_withdraw")}");
        terminal.WriteLine($"  {Systems.Loc.Get("guild.hint_bank_items")}");
        terminal.WriteLine($"  {Systems.Loc.Get("guild.hint_deposit_item")}");
        terminal.WriteLine($"  {Systems.Loc.Get("guild.hint_withdraw_item")}");
        return true;
    }

    private static bool HandleGuildDepositItem(string username, string args, TerminalEmulator terminal)
    {
        var guild = Systems.GuildSystem.Instance;
        if (guild == null) { terminal.WriteLine($"  {Systems.Loc.Get("guild.not_available")}", "yellow"); return true; }

        var guildName = guild.GetPlayerGuild(username);
        if (guildName == null) { terminal.WriteLine($"  {Systems.Loc.Get("guild.not_in_guild")}", "yellow"); return true; }

        var session = MudServer.Instance?.ActiveSessions.TryGetValue(username.ToLowerInvariant(), out var s) == true ? s : null;
        var player = session?.Context?.Engine?.CurrentPlayer;
        if (player == null) { terminal.WriteLine($"  {Systems.Loc.Get("guild.cannot_access_character")}", "red"); return true; }

        // Check if player has items in inventory (backpack)
        var inventory = player.Inventory;
        if (inventory == null || inventory.Count == 0)
        {
            terminal.WriteLine($"  {Systems.Loc.Get("guild.your_inventory_empty")}", "yellow");
            return true;
        }

        // If no arg, show numbered list
        if (string.IsNullOrWhiteSpace(args))
        {
            terminal.SetColor("cyan");
            terminal.WriteLine($"  {Systems.Loc.Get("guild.select_item_deposit")}");
            for (int i = 0; i < inventory.Count; i++)
            {
                terminal.WriteLine($"    {i + 1}. {inventory[i].Name}");
            }
            terminal.SetColor("gray");
            terminal.WriteLine($"  {Systems.Loc.Get("guild.usage_gdeposit")}");
            return true;
        }

        if (!int.TryParse(args.Trim(), out int choice) || choice < 1 || choice > inventory.Count)
        {
            terminal.WriteLine($"  {Systems.Loc.Get("guild.invalid_choice", inventory.Count)}", "yellow");
            return true;
        }

        var item = inventory[choice - 1];
        string itemJson = System.Text.Json.JsonSerializer.Serialize(item);
        var error = guild.DepositItem(username, item.Name, itemJson);
        if (error != null) { terminal.WriteLine($"  {error}", "red"); return true; }

        inventory.RemoveAt(choice - 1);
        _ = Systems.SaveSystem.Instance.AutoSave(player);
        terminal.SetColor("bright_green");
        terminal.WriteLine($"  {Systems.Loc.Get("guild.deposited_item", item.Name)}");

        NotifyGuildMembers(guild, guildName, username, Systems.Loc.Get("guild.broadcast_deposited_item", GetChatDisplayName(username), item.Name));
        return true;
    }

    private static bool HandleGuildWithdrawItem(string username, string args, TerminalEmulator terminal)
    {
        var guild = Systems.GuildSystem.Instance;
        if (guild == null) { terminal.WriteLine($"  {Systems.Loc.Get("guild.not_available")}", "yellow"); return true; }

        var guildName = guild.GetPlayerGuild(username);
        if (guildName == null) { terminal.WriteLine($"  {Systems.Loc.Get("guild.not_in_guild")}", "yellow"); return true; }

        if (string.IsNullOrWhiteSpace(args))
        {
            // Show bank items
            var items = guild.GetBankItems(guildName);
            if (items.Count == 0) { terminal.WriteLine($"  {Systems.Loc.Get("guild.bank_no_items")}", "gray"); return true; }

            terminal.SetColor("cyan");
            terminal.WriteLine($"  {Systems.Loc.Get("guild.bank_items_header", items.Count, Systems.GuildSystem.MaxBankItems)}");
            foreach (var item in items)
                terminal.WriteLine($"    {Systems.Loc.Get("guild.bank_items_entry", item.Id, item.ItemName, item.DepositedBy)}");
            terminal.SetColor("gray");
            terminal.WriteLine($"  {Systems.Loc.Get("guild.usage_gwithdraw")}");
            return true;
        }

        if (!int.TryParse(args.Trim(), out int itemId))
        {
            terminal.WriteLine($"  {Systems.Loc.Get("guild.usage_gwithdraw_num")}", "yellow");
            return true;
        }

        var session = MudServer.Instance?.ActiveSessions.TryGetValue(username.ToLowerInvariant(), out var s) == true ? s : null;
        var player = session?.Context?.Engine?.CurrentPlayer;
        if (player == null) { terminal.WriteLine($"  {Systems.Loc.Get("guild.cannot_access_character")}", "red"); return true; }

        string? itemJson = guild.WithdrawItem(username, itemId, out string? errorMessage);
        if (itemJson == null) { terminal.WriteLine($"  {errorMessage}", "yellow"); return true; }

        try
        {
            var item = System.Text.Json.JsonSerializer.Deserialize<global::Item>(itemJson);
            if (item != null)
            {
                player.Inventory.Add(item);
                _ = Systems.SaveSystem.Instance.AutoSave(player);
                terminal.SetColor("bright_green");
                terminal.WriteLine($"  {Systems.Loc.Get("guild.withdrew_item", item.Name)}");

                NotifyGuildMembers(guild, guildName, username, Systems.Loc.Get("guild.broadcast_withdrew_item", GetChatDisplayName(username), item.Name));
            }
            else
            {
                terminal.WriteLine($"  {Systems.Loc.Get("guild.deserialize_failed")}", "red");
            }
        }
        catch (Exception ex)
        {
            terminal.WriteLine($"  {Systems.Loc.Get("guild.process_item_failed", ex.Message)}", "red");
        }
        return true;
    }

    private static bool HandleGuildRank(string username, string args, TerminalEmulator terminal)
    {
        var guild = Systems.GuildSystem.Instance;
        if (guild == null) { terminal.WriteLine($"  {Systems.Loc.Get("guild.not_available")}", "yellow"); return true; }

        var guildName = guild.GetPlayerGuild(username);
        if (guildName == null) { terminal.WriteLine($"  {Systems.Loc.Get("guild.not_in_guild")}", "yellow"); return true; }

        if (string.IsNullOrWhiteSpace(args))
        {
            terminal.SetColor("gray");
            terminal.WriteLine($"  {Systems.Loc.Get("guild.usage_grank")}");
            terminal.WriteLine($"  {Systems.Loc.Get("guild.ranks_list")}");
            terminal.WriteLine($"  {Systems.Loc.Get("guild.only_leader_ranks")}");
            return true;
        }

        var parts = args.Trim().Split(' ', 2, StringSplitOptions.RemoveEmptyEntries);
        if (parts.Length < 2)
        {
            terminal.WriteLine($"  {Systems.Loc.Get("guild.usage_grank")}", "yellow");
            terminal.WriteLine($"  {Systems.Loc.Get("guild.ranks_list")}", "gray");
            return true;
        }

        string targetPlayer = parts[0];
        string newRank = parts[1].Trim();
        // Normalize rank casing
        if (newRank.Equals("officer", StringComparison.OrdinalIgnoreCase)) newRank = "Officer";
        else if (newRank.Equals("member", StringComparison.OrdinalIgnoreCase)) newRank = "Member";

        var error = guild.SetMemberRank(username, targetPlayer, newRank);
        if (error != null) { terminal.WriteLine($"  {error}", "yellow"); return true; }

        terminal.SetColor("bright_green");
        terminal.WriteLine($"  {Systems.Loc.Get("guild.rank_set", targetPlayer, newRank)}");

        // Notify target
        var targetSession = MudServer.Instance?.ActiveSessions.TryGetValue(targetPlayer.ToLowerInvariant(), out var ts) == true ? ts : null;
        try
        {
            targetSession?.Context?.Terminal?.SetColor("bright_yellow");
            targetSession?.Context?.Terminal?.WriteLine($"\r\n  {Systems.Loc.Get("guild.rank_changed_notification", newRank, GetChatDisplayName(username))}\r\n");
        }
        catch { }

        return true;
    }

    private static bool HandleGuildTransfer(string username, string args, TerminalEmulator terminal)
    {
        var guild = Systems.GuildSystem.Instance;
        if (guild == null) { terminal.WriteLine($"  {Systems.Loc.Get("guild.not_available")}", "yellow"); return true; }

        var guildName = guild.GetPlayerGuild(username);
        if (guildName == null) { terminal.WriteLine($"  {Systems.Loc.Get("guild.not_in_guild")}", "yellow"); return true; }

        if (string.IsNullOrWhiteSpace(args))
        {
            terminal.WriteLine($"  {Systems.Loc.Get("guild.usage_gtransfer")}", "yellow");
            terminal.WriteLine($"  {Systems.Loc.Get("guild.transfer_hint")}", "gray");
            return true;
        }

        string target = args.Trim();
        var error = guild.TransferLeadership(username, target);
        if (error != null) { terminal.WriteLine($"  {error}", "yellow"); return true; }

        terminal.SetColor("bright_green");
        terminal.WriteLine($"  {Systems.Loc.Get("guild.transfer_done", target)}");

        // Notify remaining guild members (exclude both actor and target to avoid double-notification)
        string transferMsg = Systems.Loc.Get("guild.broadcast_transfer", GetChatDisplayName(username), target);
        var onlineTransfer = guild.GetOnlineGuildMembers(guildName);
        foreach (var member in onlineTransfer)
        {
            if (string.Equals(member, username, StringComparison.OrdinalIgnoreCase)) continue;
            if (string.Equals(member, target, StringComparison.OrdinalIgnoreCase)) continue;
            var memberSession = MudServer.Instance?.ActiveSessions.TryGetValue(member.ToLowerInvariant(), out var ms) == true ? ms : null;
            try
            {
                memberSession?.Context?.Terminal?.SetColor("cyan");
                memberSession?.Context?.Terminal?.WriteLine($"\r\n  {transferMsg}\r\n");
            }
            catch { }
        }

        // Notify the new leader directly with a personalized message
        var targetSession = MudServer.Instance?.ActiveSessions.TryGetValue(target.ToLowerInvariant(), out var ts) == true ? ts : null;
        try
        {
            targetSession?.Context?.Terminal?.SetColor("bright_yellow");
            targetSession?.Context?.Terminal?.WriteLine($"\r\n  {Systems.Loc.Get("guild.transfer_notification", GetChatDisplayName(username))}\r\n");
        }
        catch { }

        return true;
    }

    /// <summary>
    /// Notify all online guild members (except the actor) about a guild bank action.
    /// </summary>
    private static void NotifyGuildMembers(Systems.GuildSystem guild, string guildName, string excludeUsername, string message)
    {
        var online = guild.GetOnlineGuildMembers(guildName);
        foreach (var member in online)
        {
            if (string.Equals(member, excludeUsername, StringComparison.OrdinalIgnoreCase)) continue;
            var memberSession = MudServer.Instance?.ActiveSessions.TryGetValue(member.ToLowerInvariant(), out var ms) == true ? ms : null;
            try
            {
                memberSession?.Context?.Terminal?.SetColor("cyan");
                memberSession?.Context?.Terminal?.WriteLine($"\r\n  {message}\r\n");
            }
            catch { }
        }
    }
}
