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

                if (session.WizardLevel > WizardLevel.Mortal)
                    terminal.SetColor(WizardConstants.GetColor(session.WizardLevel));
                else
                    terminal.SetColor(string.IsNullOrEmpty(isYou) ? "white" : "bright_green");

                var line = $"  {session.Username}{wizTag}{isYou}{invisTag} - {locName} [{session.ConnectionType}]";
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
}
