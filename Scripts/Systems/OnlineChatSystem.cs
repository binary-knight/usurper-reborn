using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;

namespace UsurperRemake.Systems
{
    /// <summary>
    /// Player-facing online communication system.
    /// Handles chat display, "Who's Online", message queue, and inter-player commands.
    /// Sits on top of OnlineStateManager's messaging infrastructure.
    /// </summary>
    public class OnlineChatSystem
    {
        private static OnlineChatSystem? _fallbackInstance;

        /// <summary>
        /// Returns the per-session OnlineChatSystem when in MUD mode (via SessionContext),
        /// or the static fallback instance for SSH-per-process mode.
        /// </summary>
        public static OnlineChatSystem? Instance =>
            UsurperRemake.Server.SessionContext.Current?.OnlineChat ?? _fallbackInstance;

        private readonly OnlineStateManager stateManager;
        private readonly Queue<ChatMessage> pendingMessages = new();
        private readonly List<ChatMessage> messageHistory = new();
        private const int MAX_HISTORY = 100;

        public static bool IsActive => Instance != null;

        /// <summary>
        /// Number of unread messages waiting to be displayed.
        /// </summary>
        public int PendingMessageCount => pendingMessages.Count;

        /// <summary>
        /// Initialize the chat system. Call after OnlineStateManager is initialized.
        /// In MUD mode, stored on SessionContext for per-session isolation.
        /// </summary>
        public static OnlineChatSystem Initialize(OnlineStateManager stateManager)
        {
            var chat = new OnlineChatSystem(stateManager);

            var ctx = UsurperRemake.Server.SessionContext.Current;
            if (ctx != null)
                ctx.OnlineChat = chat;
            else
                _fallbackInstance = chat;

            return chat;
        }

        private OnlineChatSystem(OnlineStateManager stateManager)
        {
            this.stateManager = stateManager;
        }

        // =====================================================================
        // Chat Commands (player-initiated)
        // =====================================================================

        /// <summary>
        /// Send a chat message to all online players.
        /// Usage: /say Hello everyone!
        /// </summary>
        public async Task Say(string message)
        {
            await stateManager.BroadcastMessage("chat", message);
        }

        /// <summary>
        /// Send a private message to a specific player.
        /// Usage: /tell PlayerName Your message here
        /// </summary>
        public async Task Tell(string targetPlayer, string message)
        {
            await stateManager.SendMessage(targetPlayer, "chat_private", message);
        }

        /// <summary>
        /// Send a system announcement (SysOp only).
        /// </summary>
        public async Task Announce(string message)
        {
            await stateManager.BroadcastMessage("system", message);
        }

        // =====================================================================
        // Who's Online
        // =====================================================================

        /// <summary>
        /// Display the "Who's Online" list using the terminal.
        /// </summary>
        public async Task ShowWhosOnline(TerminalEmulator terminal)
        {
            var players = await stateManager.GetOnlinePlayers();

            terminal.SetColor("cyan");
            terminal.WriteLine("════════════════════════════════════════════════════════════");
            terminal.SetColor("bright_cyan");
            terminal.WriteLine("                     WHO'S ONLINE");
            terminal.SetColor("cyan");
            terminal.WriteLine("════════════════════════════════════════════════════════════");
            terminal.WriteLine("");

            if (players.Count == 0)
            {
                terminal.SetColor("gray");
                terminal.WriteLine("  No other players currently online.");
            }
            else
            {
                terminal.SetColor("yellow");
                terminal.WriteLine($"  {"Player",-18} {"Location",-16} {"Via",-5} {"Connected"}");
                terminal.SetColor("darkgray");
                terminal.WriteLine($"  {"──────────────────"} {"────────────────"} {"─────"} {"─────────────────"}");

                foreach (var player in players)
                {
                    var duration = DateTime.UtcNow - player.ConnectedAt;
                    var durationStr = duration.TotalHours >= 1
                        ? $"{(int)duration.TotalHours}h {duration.Minutes}m"
                        : $"{duration.Minutes}m";

                    var viaTag = FormatConnectionType(player.ConnectionType);

                    terminal.SetColor("white");
                    terminal.Write($"  {player.DisplayName,-18} ");
                    terminal.SetColor("green");
                    terminal.Write($"{FormatLocation(player.Location),-16} ");
                    terminal.SetColor("darkgray");
                    terminal.Write($"{viaTag,-5} ");
                    terminal.SetColor("gray");
                    terminal.WriteLine(durationStr);
                }
            }

            terminal.WriteLine("");
            terminal.SetColor("cyan");
            terminal.WriteLine($"  {players.Count} player{(players.Count != 1 ? "s" : "")} online");
            terminal.SetColor("cyan");
            terminal.WriteLine("════════════════════════════════════════════════════════════");
            await terminal.PressAnyKey();
        }

        /// <summary>
        /// Get a short status string for the location bar (e.g., "3 Online").
        /// </summary>
        public async Task<string> GetOnlineStatusText()
        {
            var count = await stateManager.GetOnlinePlayerCount();
            return $"{count} Online";
        }

        // =====================================================================
        // News Feed
        // =====================================================================

        /// <summary>
        /// Display recent news from the shared news feed.
        /// </summary>
        public async Task ShowNews(TerminalEmulator terminal, int count = 15)
        {
            var news = await stateManager.GetRecentNews(count);

            terminal.SetColor("yellow");
            terminal.WriteLine("════════════════════════════════════════════════════════════");
            terminal.SetColor("bright_yellow");
            terminal.WriteLine("                      TOWN NEWS");
            terminal.SetColor("yellow");
            terminal.WriteLine("════════════════════════════════════════════════════════════");
            terminal.WriteLine("");

            if (news.Count == 0)
            {
                terminal.SetColor("gray");
                terminal.WriteLine("  No recent news.");
            }
            else
            {
                foreach (var entry in news)
                {
                    var color = entry.Category switch
                    {
                        "combat" => "red",
                        "politics" => "cyan",
                        "romance" => "magenta",
                        "economy" => "yellow",
                        "quest" => "green",
                        _ => "white"
                    };

                    terminal.SetColor("darkgray");
                    terminal.Write($"  [{entry.CreatedAt:MMM dd HH:mm}] ");
                    terminal.SetColor(color);
                    terminal.WriteLine(entry.Message);
                }
            }

            terminal.WriteLine("");
            terminal.SetColor("yellow");
            terminal.WriteLine("════════════════════════════════════════════════════════════");
            await terminal.PressAnyKey();
        }

        // =====================================================================
        // Incoming Message Queue
        // =====================================================================

        /// <summary>
        /// Queue an incoming message for display at the next opportunity.
        /// Called by OnlineStateManager when messages arrive.
        /// </summary>
        public void QueueIncomingMessage(string from, string type, string message)
        {
            var chatMsg = new ChatMessage
            {
                From = from,
                Type = type,
                Text = message,
                Timestamp = DateTime.Now
            };

            pendingMessages.Enqueue(chatMsg);
            messageHistory.Add(chatMsg);

            // Trim history
            while (messageHistory.Count > MAX_HISTORY)
                messageHistory.RemoveAt(0);
        }

        /// <summary>
        /// Display all pending messages to the terminal, then clear the queue.
        /// Call this at safe display points (between turns, at location menus, etc.)
        /// </summary>
        public void DisplayPendingMessages(TerminalEmulator terminal)
        {
            while (pendingMessages.Count > 0)
            {
                var msg = pendingMessages.Dequeue();
                DisplayMessage(terminal, msg);
            }
        }

        /// <summary>
        /// Display a single chat message.
        /// </summary>
        private void DisplayMessage(TerminalEmulator terminal, ChatMessage msg)
        {
            switch (msg.Type)
            {
                case "chat":
                    terminal.SetColor("bright_cyan");
                    terminal.Write($"[{msg.From}] ");
                    terminal.SetColor("white");
                    terminal.WriteLine(msg.Text);
                    break;

                case "chat_private":
                    terminal.SetColor("magenta");
                    terminal.Write($"[PM from {msg.From}] ");
                    terminal.SetColor("bright_magenta");
                    terminal.WriteLine(msg.Text);
                    break;

                case "system":
                    terminal.SetColor("bright_yellow");
                    terminal.Write("[SYSTEM] ");
                    terminal.SetColor("yellow");
                    terminal.WriteLine(msg.Text);
                    break;

                case "duel":
                    terminal.SetColor("red");
                    terminal.Write($"[DUEL] ");
                    terminal.SetColor("bright_red");
                    terminal.WriteLine($"{msg.From} challenges you to a duel!");
                    break;

                case "trade":
                    terminal.SetColor("green");
                    terminal.Write($"[TRADE] ");
                    terminal.SetColor("bright_green");
                    terminal.WriteLine($"{msg.From} sent you a trade offer.");
                    break;

                default:
                    terminal.SetColor("gray");
                    terminal.WriteLine($"[{msg.From}] {msg.Text}");
                    break;
            }
        }

        /// <summary>
        /// Check if a command is an online chat command and process it.
        /// Returns true if the command was handled.
        /// </summary>
        public async Task<bool> TryProcessCommand(string input, TerminalEmulator terminal)
        {
            if (string.IsNullOrWhiteSpace(input))
                return false;

            var trimmed = input.Trim();

            // /say <message> - broadcast chat
            if (trimmed.StartsWith("/say ", StringComparison.OrdinalIgnoreCase))
            {
                var message = trimmed.Substring(5).Trim();
                if (!string.IsNullOrEmpty(message))
                {
                    await Say(message);
                    terminal.SetColor("cyan");
                    terminal.WriteLine($"[You] {message}");
                    terminal.SetColor("green");
                    terminal.WriteLine("  Message sent!");
                    await Task.Delay(1500);
                }
                return true;
            }

            // /tell <player> <message> - private message (works online and offline)
            if (trimmed.StartsWith("/tell ", StringComparison.OrdinalIgnoreCase))
            {
                var parts = trimmed.Substring(6).Trim();
                var spaceIdx = parts.IndexOf(' ');
                if (spaceIdx > 0)
                {
                    var targetPlayer = parts.Substring(0, spaceIdx);
                    var message = parts.Substring(spaceIdx + 1).Trim();
                    if (!string.IsNullOrEmpty(message))
                    {
                        // Validate recipient exists
                        var sqlBackend = SaveSystem.Instance?.Backend as SqlSaveBackend;
                        if (sqlBackend != null && !sqlBackend.PlayerExists(targetPlayer))
                        {
                            terminal.SetColor("red");
                            terminal.WriteLine($"Player '{targetPlayer}' not found.");
                            await Task.Delay(1500);
                        }
                        else
                        {
                            await Tell(targetPlayer, message);
                            terminal.SetColor("magenta");
                            terminal.WriteLine($"[To {targetPlayer}] {message}");

                            // Check if target is online
                            var onlinePlayers = await stateManager.GetOnlinePlayers();
                            bool isOnline = onlinePlayers.Any(p =>
                                p.DisplayName.Equals(targetPlayer, StringComparison.OrdinalIgnoreCase) ||
                                p.Username.Equals(targetPlayer, StringComparison.OrdinalIgnoreCase));

                            terminal.SetColor("green");
                            if (isOnline)
                                terminal.WriteLine("  Message sent!");
                            else
                                terminal.WriteLine($"  Message sent to {targetPlayer} (offline - they'll see it next login).");
                            await Task.Delay(1500);
                        }
                    }
                }
                else
                {
                    terminal.SetColor("yellow");
                    terminal.WriteLine("Usage: /tell <player> <message>");
                }
                return true;
            }

            // /who - who's online (PressAnyKey is inside ShowWhosOnline)
            if (trimmed.Equals("/who", StringComparison.OrdinalIgnoreCase) ||
                trimmed.Equals("/online", StringComparison.OrdinalIgnoreCase))
            {
                await ShowWhosOnline(terminal);
                return true;
            }

            // /news - show recent news (PressAnyKey is inside ShowNews)
            if (trimmed.Equals("/news", StringComparison.OrdinalIgnoreCase))
            {
                await ShowNews(terminal);
                return true;
            }

            return false;
        }

        /// <summary>
        /// Format connection type into a short display tag.
        /// </summary>
        private string FormatConnectionType(string connectionType)
        {
            return connectionType switch
            {
                "Web" => "Web",
                "SSH" => "SSH",
                "BBS" => "BBS",
                "Steam" => "Steam",
                "Local" => "Local",
                _ => "?"
            };
        }

        /// <summary>
        /// Format a location enum string into a readable name.
        /// </summary>
        private string FormatLocation(string location)
        {
            if (string.IsNullOrEmpty(location))
                return "Unknown";

            // Convert "MainStreet" to "Main Street", "TheInn" to "The Inn", etc.
            var result = new System.Text.StringBuilder();
            for (int i = 0; i < location.Length; i++)
            {
                if (i > 0 && char.IsUpper(location[i]) && !char.IsUpper(location[i - 1]))
                    result.Append(' ');
                result.Append(location[i]);
            }
            return result.ToString();
        }

        /// <summary>
        /// Shutdown and cleanup.
        /// </summary>
        public void Shutdown()
        {
            var ctx = UsurperRemake.Server.SessionContext.Current;
            if (ctx != null && ctx.OnlineChat == this)
                ctx.OnlineChat = null;
            else if (_fallbackInstance == this)
                _fallbackInstance = null;
        }
    }

    /// <summary>
    /// A chat message in the display queue.
    /// </summary>
    public class ChatMessage
    {
        public string From { get; set; } = "";
        public string Type { get; set; } = "";
        public string Text { get; set; } = "";
        public DateTime Timestamp { get; set; }
    }
}
