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

            if (!GameConfig.ScreenReaderMode)
            {
                terminal.SetColor("cyan");
                terminal.WriteLine("════════════════════════════════════════════════════════════");
            }
            terminal.SetColor("bright_cyan");
            string whoTitle = Loc.Get("main_street.whos_online").ToUpperInvariant();
            terminal.WriteLine(GameConfig.ScreenReaderMode ? whoTitle : "                     " + whoTitle);
            if (!GameConfig.ScreenReaderMode)
            {
                terminal.SetColor("cyan");
                terminal.WriteLine("════════════════════════════════════════════════════════════");
            }
            terminal.WriteLine("");

            if (players.Count == 0)
            {
                terminal.SetColor("gray");
                terminal.WriteLine("  " + Loc.Get("chat.who_no_others"));
            }
            else
            {
                terminal.SetColor("yellow");
                terminal.WriteLine(WhoColumnsRow());
                if (!GameConfig.ScreenReaderMode)
                {
                    terminal.SetColor("darkgray");
                    terminal.WriteLine($"  {"──────────────────"} {"────────────────"} {"─────"} {"─────────────────"}");
                }

                foreach (var player in players)
                {
                    var duration = DateTime.UtcNow - player.ConnectedAt;
                    var durationStr = duration.TotalHours >= 1
                        ? $"{(int)duration.TotalHours}h {duration.Minutes}m"
                        : $"{duration.Minutes}m";

                    var viaTag = FormatConnectionType(player.ConnectionType);

                    // Check live session for knight title and spectator status
                    var specTag = "";
                    string displayName = player.DisplayName;
                    var mudServer = UsurperRemake.Server.MudServer.Instance;
                    if (mudServer != null && mudServer.ActiveSessions.TryGetValue(
                        player.DisplayName.ToLowerInvariant(), out var session))
                    {
                        // Arena Champion tiers flow through NobleTitle now, so the
                        // player controls which title (if any) shows up here.
                        var livePlayer = session.Context?.Engine?.CurrentPlayer;
                        if (!string.IsNullOrEmpty(livePlayer?.NobleTitle))
                            displayName = $"{livePlayer.NobleTitle} {displayName}";

                        if (session.IsSpectating && session.SpectatingSession != null)
                            specTag = Loc.Get("chat.who_watching_tag", session.SpectatingSession.Username);
                        else if (session.Spectators.Count > 0)
                            specTag = Loc.Get("chat.who_watchers_tag", session.Spectators.Count);
                    }

                    WriteWhoRow(terminal, displayName, FormatLocation(player.Location), viaTag, durationStr, specTag);
                }
            }

            terminal.WriteLine("");
            terminal.SetColor("cyan");
            terminal.WriteLine("  " + Loc.Get(players.Count == 1 ? "chat.who_count_one" : "chat.who_count_many", players.Count));
            if (!GameConfig.ScreenReaderMode)
            {
                terminal.SetColor("cyan");
                terminal.WriteLine("════════════════════════════════════════════════════════════");
            }
            await terminal.PressAnyKey();
        }

        /// <summary>v1.2.5: the column titles of the who list, in the reader's language.</summary>
        internal static string WhoColumnsRow() =>
            $"  {Loc.Get("chat.who_col_player").PadRight(18)} {Loc.Get("chat.who_col_location").PadRight(16)} " +
            $"{Loc.Get("chat.who_col_via").PadRight(5)} {Loc.Get("chat.who_col_connected")}";

        /// <summary>
        /// v1.2.5: one who list entry as rows of (text, colour) pieces. The columns pad as before; a piece
        /// that would pass column 79 starts a new row, indented under the location column.
        /// </summary>
        internal static List<List<(string Text, string Color)>> WhoRows(string displayName, string location, string via, string duration, string specTag)
        {
            const int Width = 79;
            string indent = new string(' ', 21);
            var pieces = new List<(string Text, string Color)>
            {
                ($"{location,-16} ", "green"),
                ($"{via,-5} ", "darkgray"),
                (duration, "gray"),
            };
            if (!string.IsNullOrEmpty(specTag)) pieces.Add((" " + specTag, "bright_magenta"));

            var rows = new List<List<(string Text, string Color)>>();
            var row = new List<(string Text, string Color)> { ($"  {displayName,-18} ", "white") };
            int used = row[0].Text.Length;
            foreach (var (text, color) in pieces)
            {
                if (used > indent.Length && used + text.Length > Width)
                {
                    rows.Add(row);
                    row = new List<(string Text, string Color)> { (indent, "white") };
                    used = indent.Length;
                    string moved = text.TrimStart();
                    row.Add((moved, color));
                    used += moved.Length;
                    continue;
                }
                row.Add((text, color));
                used += text.Length;
            }
            rows.Add(row);
            return rows;
        }

        private static void WriteWhoRow(TerminalEmulator terminal, string displayName, string location, string via, string duration, string specTag)
        {
            foreach (var row in WhoRows(displayName, location, via, duration, specTag))
            {
                foreach (var (text, color) in row)
                {
                    terminal.SetColor(color);
                    terminal.Write(text);
                }
                terminal.WriteLine("");
            }
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

            if (!GameConfig.ScreenReaderMode)
            {
                terminal.SetColor("yellow");
                terminal.WriteLine("════════════════════════════════════════════════════════════");
            }
            terminal.SetColor("bright_yellow");
            string townNews = Loc.Get("news.town_news_header");
            terminal.WriteLine(GameConfig.ScreenReaderMode ? townNews : townNews.PadLeft(22 + townNews.Length / 2));
            if (!GameConfig.ScreenReaderMode)
            {
                terminal.SetColor("yellow");
                terminal.WriteLine("════════════════════════════════════════════════════════════");
            }
            terminal.WriteLine("");

            if (news.Count == 0)
            {
                terminal.SetColor("gray");
                terminal.WriteLine("  " + Loc.Get("news.none_recent"));
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

                    WriteNewsEntry(terminal, $"  [{GameConfig.FormatShortDate(entry.CreatedAt, GameConfig.DateFormat)} {entry.CreatedAt:HH:mm}] ", entry.Message, color);
                }
            }

            terminal.WriteLine("");
            if (!GameConfig.ScreenReaderMode)
            {
                terminal.SetColor("yellow");
                terminal.WriteLine("════════════════════════════════════════════════════════════");
            }
            await terminal.PressAnyKey();
        }

        /// <summary>
        /// v1.2.4: one news entry, its date stamp in gray. An entry wider than 79 columns wraps, its
        /// later rows indented under the text after the stamp.
        /// </summary>
        internal static void WriteNewsEntry(TerminalEmulator terminal, string stamp, string message, string color)
        {
            var parts = UsurperRemake.UI.UIHelper.WrapAfterPrefix(stamp, message);
            terminal.SetColor("darkgray");
            terminal.Write(stamp);
            terminal.SetColor(color);
            terminal.WriteLine(parts[0]);
            string indent = new string(' ', UsurperRemake.UI.UIHelper.VisibleLength(stamp));
            for (int i = 1; i < parts.Count; i++)
                terminal.WriteLine(indent + parts[i]);
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
        internal static void DisplayMessage(TerminalEmulator terminal, ChatMessage msg)
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
                    WriteTagged(terminal, Loc.Get("chat.pm_from_tag", msg.From), "magenta", msg.Text, "bright_magenta");
                    break;

                case "system":
                    WriteTagged(terminal, Loc.Get("chat.system_tag"), "bright_yellow", msg.Text, "yellow");
                    break;

                case "duel":
                    WriteTagged(terminal, Loc.Get("chat.duel_tag"), "red", Loc.Get("chat.duel_challenge", msg.From), "bright_red");
                    break;

                case "trade":
                    WriteTagged(terminal, Loc.Get("chat.trade_tag"), "green", Loc.Get("chat.trade_offer", msg.From), "bright_green");
                    break;

                default:
                    terminal.SetColor("gray");
                    terminal.WriteLine($"[{msg.From}] {msg.Text}");
                    break;
            }
        }

        /// <summary>
        /// v1.2.5: a tag ("[DUEL]") and its text; a text wider than the row wraps, its later rows indented
        /// under the text after the tag.
        /// </summary>
        internal static void WriteTagged(TerminalEmulator terminal, string tag, string tagColor, string text, string textColor)
        {
            string prefix = tag + " ";
            var parts = UsurperRemake.UI.UIHelper.WrapAfterPrefix(prefix, text);
            terminal.SetColor(tagColor);
            terminal.Write(prefix);
            terminal.SetColor(textColor);
            terminal.WriteLine(parts[0]);
            string indent = new string(' ', UsurperRemake.UI.UIHelper.VisibleLength(prefix));
            for (int i = 1; i < parts.Count; i++)
                terminal.WriteLine(indent + parts[i]);
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
                    terminal.WriteLine(Loc.Get("chat.you_line", message));
                    terminal.SetColor("green");
                    terminal.WriteLine("  " + Loc.Get("chat.message_sent"));
                    await Pacing.Wait(1500);
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
                        // Validate and resolve recipient (handles username or display name)
                        var sqlBackend = SaveSystem.Instance?.Backend as SqlSaveBackend;
                        string? resolvedTarget = sqlBackend?.ResolvePlayerDisplayName(targetPlayer);
                        if (sqlBackend != null && resolvedTarget == null)
                        {
                            terminal.SetColor("red");
                            terminal.WriteLine(Loc.Get("chat.player_not_found", targetPlayer));
                            await Pacing.Wait(1500);
                        }
                        else
                        {
                            if (resolvedTarget != null) targetPlayer = resolvedTarget;
                            await Tell(targetPlayer, message);
                            terminal.SetColor("magenta");
                            terminal.WriteLine(Loc.Get("chat.to_line", targetPlayer, message));

                            // Check if target is online
                            var onlinePlayers = await stateManager.GetOnlinePlayers();
                            bool isOnline = onlinePlayers.Any(p =>
                                p.DisplayName.Equals(targetPlayer, StringComparison.OrdinalIgnoreCase) ||
                                p.Username.Equals(targetPlayer, StringComparison.OrdinalIgnoreCase));

                            terminal.SetColor("green");
                            if (isOnline)
                                terminal.WriteLine("  " + Loc.Get("chat.message_sent"));
                            else
                                UsurperRemake.UI.UIHelper.WriteWrapped(terminal, Loc.Get("chat.message_sent_offline", targetPlayer), "  ");
                            await Pacing.Wait(1500);
                        }
                    }
                }
                else
                {
                    terminal.SetColor("yellow");
                    terminal.WriteLine(Loc.Get("chat.tell_usage"));
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
                "MUD" => "MUD",
                "BBS" => "BBS",
                "Steam" => "Steam",
                "Local" => Loc.Get("chat.via_local"),
                "Electron" => "App",
                _ => "?"
            };
        }

        /// <summary>
        /// Format a location enum string into a readable name.
        /// v1.2.4: a location that already has a space ("Dungeon (Group: Name)", "SysOp Console",
        /// "Spectating Name") is shown as it is; a joined one ("MainStreet") gets a space only where a
        /// capital follows a lowercase letter, never after "(" or another non-letter.
        /// </summary>
        internal static string FormatLocation(string location)
        {
            if (string.IsNullOrEmpty(location))
                return Loc.Get("combat.unknown_name");
            if (location.Contains(' '))
                return location;

            // Convert "MainStreet" to "Main Street", "TheInn" to "The Inn", etc.
            var result = new System.Text.StringBuilder();
            for (int i = 0; i < location.Length; i++)
            {
                if (i > 0 && char.IsUpper(location[i]) && char.IsLower(location[i - 1]))
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
