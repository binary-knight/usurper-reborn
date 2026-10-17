using System;
using System.Text;
using System.Threading.Tasks;
using UsurperRemake.Server;

namespace UsurperRemake.Systems
{
    /// <summary>
    /// 1.2.8: one Online Play login connection as the auth flow sees it: the SSH shell to the relay (Local and
    /// Steam) or the direct TCP stream (BBS door). The real ones are made only by <see cref="OnlineTransport.Factory"/>.
    /// </summary>
    internal interface IOnlineAuthTransport : IDisposable
    {
        /// <summary>Send the text as is (the caller adds the newline).</summary>
        void Write(string text);

        /// <summary>The text that arrives within <paramref name="timeout"/>, or null on a timeout or a closed connection.</summary>
        Task<string?> ReadAsync(TimeSpan timeout);

        /// <summary>Discard what has already arrived (the relay's menu).</summary>
        void Drain();
    }

    internal enum OnlineAuthOutcome { Authenticated, Quit, Failed }

    /// <summary>One reply to an AUTH line, matched by whole lines only.</summary>
    internal sealed class OnlineAuthReply
    {
        public enum ReplyKind { None, Ok, Err, Other }
        public ReplyKind Kind { get; init; }
        /// <summary>The text after "ERR:" (without the token mark).</summary>
        public string ErrText { get; init; } = "";
        /// <summary>The ERR carried the token refused mark: the saved token is no good.</summary>
        public bool TokenRefused { get; init; }
        /// <summary>What arrived after the OK line (the TOKEN line, then game output).</summary>
        public string? Leftover { get; init; }
        /// <summary>How many characters arrived in all (for the log, which never holds the reply itself).</summary>
        public int Length { get; init; }
    }

    /// <summary>
    /// 1.2.8: the Online Play login state machine (design C3 section 4.3), over an <see cref="IOnlineAuthTransport"/>
    /// so tests drive it with a scripted transport. Order on the SSH relay path:
    ///   probe with AUTH:CAPS (a new relay answers CAPS:token=1; an old one only redraws its menu, which ends the
    ///   wait at once, 3 s at most); a remembered token for this very server and port, only after token=1; else
    ///   the old saved password for this very server and port (erased after that one try, whatever the outcome);
    ///   else the Login/Register menu, asking whether to remember the login before it is sent, only after token=1.
    /// After an OK with ";token=1" the next line is the TOKEN line: it is saved and never rendered. A token is
    /// forgotten only on the refused mark; a throttle or ban reply keeps it. The BBS door (direct TCP) never probes
    /// and never reads or writes the login file. Replies, AUTH lines and TOKEN lines are never logged.
    /// </summary>
    internal sealed class OnlineAuthFlow
    {
        internal const int MaxAttempts = 5;
        internal const string MenuClear = "\u001b[2J";
        internal static TimeSpan ProbeTimeout = TimeSpan.FromSeconds(3);
        /// <summary>After the menu clear, the rest of the relay's menu (its prompt has no line end) is read until the line is quiet this long.</summary>
        internal static TimeSpan MenuQuiet = TimeSpan.FromMilliseconds(250);
        internal static TimeSpan ReplyTimeout = TimeSpan.FromSeconds(5);
        internal static TimeSpan TokenLineTimeout = TimeSpan.FromSeconds(2);

        private readonly TerminalEmulator terminal;
        private readonly string server;
        private readonly int port;
        private readonly bool directTcp;
        private readonly string connectionType;
        private readonly string clientVersion;
        private readonly Func<Task<IOnlineAuthTransport?>> reconnect;
        private bool noRememberShown;

        public IOnlineAuthTransport Transport { get; private set; }
        /// <summary>Game output that arrived with the OK (the TOKEN line removed), for the session to show first.</summary>
        public string? Leftover { get; private set; }
        /// <summary>The probe's answer: null when no probe was sent.</summary>
        public bool? TokenCapable { get; private set; }

        public OnlineAuthFlow(TerminalEmulator terminal, IOnlineAuthTransport transport, string server, int port,
            bool directTcp, string connectionType, string clientVersion, Func<Task<IOnlineAuthTransport?>> reconnect)
        {
            this.terminal = terminal;
            Transport = transport;
            this.server = server;
            this.port = port;
            this.directTcp = directTcp;
            this.connectionType = connectionType;
            this.clientVersion = clientVersion;
            this.reconnect = reconnect;
        }

        private enum Step { Authenticated, Continue, Failed }

        public async Task<OnlineAuthOutcome> RunAsync()
        {
            // The BBS door is a shared install on direct TCP: no login file, no old file, no probe.
            bool storeOn = !directTcp && !OnlineLoginStore.Refused;
            var file = storeOn ? OnlineLoginStore.Load() : new OnlineLoginFile();
            var entry = storeOn ? OnlineLoginStore.Find(file, server, port) : null;
            var legacy = storeOn ? OnlineLoginStore.PrepareLegacy() : null;
            bool neverAsk = storeOn && (file.NeverAskToSave || legacy?.NeverAskToSave == true);
            if (legacy != null && !legacy.IsFor(server, port))
                legacy = null; // its password is for another server: never sent here, kept for its own
            bool tokenUsable = entry != null && entry.HasUsableToken(DateTime.UtcNow);
            if (legacy != null && tokenUsable)
            {
                OnlineLoginStore.EraseLegacy(legacy.NeverAskToSave); // a token already stands in for it
                legacy = null;
            }

            string? defaultUser = directTcp ? UsurperRemake.BBS.DoorMode.GetPlayerName() : entry?.Username;
            if (string.IsNullOrEmpty(defaultUser)) defaultUser = legacy?.Username;

            if (storeOn && (tokenUsable || legacy != null || !neverAsk))
            {
                TokenCapable = await ProbeAsync();
                if (TokenCapable != true && tokenUsable) ShowNoRemember();
            }

            bool authenticated = false;
            int attempts = 0;
            while (!authenticated && attempts < MaxAttempts)
            {
                attempts++;

                if (attempts == 1 && tokenUsable && TokenCapable == true)
                {
                    var step = await TokenLoginAsync(entry!);
                    if (step == Step.Authenticated) return OnlineAuthOutcome.Authenticated;
                    if (step == Step.Failed) return OnlineAuthOutcome.Failed;
                }
                else if (attempts == 1 && legacy != null)
                {
                    var step = await LegacyLoginAsync(legacy);
                    legacy = null;
                    if (step == Step.Authenticated) return OnlineAuthOutcome.Authenticated;
                    if (step == Step.Failed) return OnlineAuthOutcome.Failed;
                }

                // Show Login/Register menu
                if (!GameConfig.ScreenReaderMode)
                {
                    terminal.SetColor("bright_cyan");
                    terminal.WriteLine("  ──────────────────────────────────────");
                }
                terminal.SetColor("bright_white");
                terminal.WriteLine(Loc.Get("online.account_login"));
                if (!GameConfig.ScreenReaderMode)
                {
                    terminal.SetColor("bright_cyan");
                    terminal.WriteLine("  ──────────────────────────────────────");
                }
                terminal.WriteLine("");

                if (GameConfig.ScreenReaderMode)
                {
                    terminal.WriteLine($"  L. {Loc.Get("online.login_existing")}", "white");
                    terminal.WriteLine($"  R. {Loc.Get("online.register_new")}", "white");
                    terminal.WriteLine($"  Q. {Loc.Get("online.quit")}", "gray");
                }
                else
                {
                    terminal.SetColor("darkgray");
                    terminal.Write("  [");
                    terminal.SetColor("bright_cyan");
                    terminal.Write("L");
                    terminal.SetColor("darkgray");
                    terminal.Write("] ");
                    terminal.SetColor("white");
                    terminal.WriteLine(Loc.Get("online.login_existing"));

                    terminal.SetColor("darkgray");
                    terminal.Write("  [");
                    terminal.SetColor("bright_green");
                    terminal.Write("R");
                    terminal.SetColor("darkgray");
                    terminal.Write("] ");
                    terminal.SetColor("white");
                    terminal.WriteLine(Loc.Get("online.register_new"));

                    terminal.SetColor("darkgray");
                    terminal.Write("  [");
                    terminal.SetColor("bright_red");
                    terminal.Write("Q");
                    terminal.SetColor("darkgray");
                    terminal.Write("] ");
                    terminal.SetColor("gray");
                    terminal.WriteLine(Loc.Get("online.quit"));
                }

                terminal.WriteLine("");
                terminal.SetColor("bright_white");
                var choice = (await terminal.GetInput($"  {Loc.Get("ui.choice")}")).Trim().ToUpper();

                if (choice == "Q" || string.IsNullOrEmpty(choice))
                    return OnlineAuthOutcome.Quit;

                string? username = null;
                string? password = null;
                bool isRegistration = false;

                if (choice == "L")
                {
                    terminal.WriteLine("");
                    terminal.SetColor("bright_white");
                    string usernamePrompt = string.IsNullOrEmpty(defaultUser)
                        ? $"  {Loc.Get("online.username_prompt")}"
                        : $"  {Loc.Get("online.username_prompt")}[{defaultUser}] ";
                    username = (await terminal.GetInput(usernamePrompt)).Trim();
                    if (string.IsNullOrEmpty(username))
                    {
                        if (!string.IsNullOrEmpty(defaultUser)) username = defaultUser;
                        else continue;
                    }

                    terminal.Write($"  {Loc.Get("online.password_prompt")}", "bright_white");
                    password = (await terminal.GetMaskedInput()).Trim();
                    if (string.IsNullOrEmpty(password)) continue;
                }
                else if (choice == "R")
                {
                    terminal.WriteLine("");
                    terminal.SetColor("bright_green");
                    string regUsernamePrompt = string.IsNullOrEmpty(defaultUser)
                        ? $"  {Loc.Get("online.choose_username")}"
                        : $"  {Loc.Get("online.choose_username")}[{defaultUser}] ";
                    username = (await terminal.GetInput(regUsernamePrompt)).Trim();
                    if (string.IsNullOrEmpty(username))
                    {
                        if (!string.IsNullOrEmpty(defaultUser)) username = defaultUser;
                        else continue;
                    }

                    if (username.Length < 2 || username.Length > 20)
                    {
                        terminal.SetColor("bright_red");
                        terminal.WriteLine(Loc.Get("online.username_length"));
                        terminal.WriteLine("");
                        continue;
                    }

                    terminal.Write($"  {Loc.Get("online.choose_password")}", "bright_green");
                    password = (await terminal.GetMaskedInput()).Trim();
                    if (string.IsNullOrEmpty(password)) continue;

                    if (password.Length < GameConfig.MinPasswordLength)
                    {
                        terminal.SetColor("bright_red");
                        terminal.WriteLine(Loc.Get("online.password_length"));
                        terminal.WriteLine("");
                        continue;
                    }

                    terminal.Write($"  {Loc.Get("online.confirm_password")}", "bright_green");
                    var confirm = (await terminal.GetMaskedInput()).Trim();
                    if (password != confirm)
                    {
                        terminal.SetColor("bright_red");
                        terminal.WriteLine(Loc.Get("online.passwords_no_match"));
                        terminal.WriteLine("");
                        continue;
                    }

                    isRegistration = true;
                }
                else
                {
                    continue; // Invalid choice
                }

                // 1.2.8: the remember question comes before the login is sent (the token is asked for on the
                // AUTH line), and only when this server said it can issue one.
                bool remember = false;
                if (storeOn && TokenCapable == true && !neverAsk)
                {
                    terminal.SetColor("gray");
                    var save = (await terminal.GetInput($"  {Loc.Get("online.remember_prompt")}")).Trim().ToUpper();
                    if (GameConfig.IsAffirmative(save)) // v1.1.15: yesno-exempt: three-way menu (Y/N/D "don't ask again"), not a plain yes/no
                    {
                        remember = true;
                    }
                    else if (save == "D")
                    {
                        OnlineLoginStore.SetNeverAsk();
                        neverAsk = true;
                        terminal.SetColor("green");
                        terminal.WriteLine(Loc.Get("online.credentials_never_ask"));
                    }
                }

                try
                {
                    Transport.Write(PasswordLine(username!, password!, isRegistration, remember) + "\n");
                }
                catch
                {
                    terminal.SetColor("bright_red");
                    terminal.WriteLine(Loc.Get("online.lost_connection"));
                    await terminal.PressAnyKey();
                    return OnlineAuthOutcome.Failed;
                }

                var reply = await ReadReplyAsync();
                if (reply.Kind == OnlineAuthReply.ReplyKind.None)
                {
                    terminal.SetColor("bright_red");
                    terminal.WriteLine(Loc.Get("online.server_closed"));
                    await terminal.PressAnyKey();
                    return OnlineAuthOutcome.Failed;
                }

                if (reply.Kind == OnlineAuthReply.ReplyKind.Err)
                {
                    terminal.SetColor("bright_red");
                    terminal.WriteLine($"  {reply.ErrText}");
                    terminal.WriteLine("");
                    if (!await ReconnectAsync()) return OnlineAuthOutcome.Failed;
                    continue;
                }

                if (reply.Kind == OnlineAuthReply.ReplyKind.Ok)
                {
                    authenticated = true;
                    Leftover = reply.Leftover;
                    terminal.SetColor("bright_green");
                    terminal.WriteLine(Loc.Get("online.authenticated"));
                    if (remember) await TakeTokenAsync(username!);
                }
                else
                {
                    terminal.SetColor("bright_red");
                    terminal.WriteLine(Loc.Get("online.unexpected_response"));
                    DebugLogger.Instance.LogError("ONLINE_PLAY", $"Unexpected auth response ({reply.Length} bytes)");
                    return OnlineAuthOutcome.Failed;
                }
            }

            if (!authenticated)
            {
                terminal.SetColor("bright_red");
                terminal.WriteLine(Loc.Get("online.too_many_attempts"));
                await terminal.PressAnyKey();
                return OnlineAuthOutcome.Failed;
            }
            return OnlineAuthOutcome.Authenticated;
        }

        /// <summary>The password or REGISTER line, with ";token=1" on the connection type when a token is asked for.</summary>
        internal string PasswordLine(string username, string password, bool isRegistration, bool remember)
        {
            string type = remember ? $"{connectionType};{LoginTokenProtocol.RequestParam}=1" : connectionType;
            // REGISTER stays 5-part: appending a version here would fold into the type field on pre-0.65.13
            // servers (Split limit 5). v0.65.13: the login's 5th field is the client version.
            return isRegistration
                ? $"AUTH:{username}:{password}:REGISTER:{type}"
                : $"AUTH:{username}:{password}:{type}:{clientVersion}";
        }

        /// <summary>A remembered token, for this server and port only, after token=1.</summary>
        private async Task<Step> TokenLoginAsync(OnlineLoginEntry entry)
        {
            terminal.SetColor("gray");
            terminal.WriteLine(Loc.Get("online.logging_in_as", entry.Username));

            OnlineAuthReply reply;
            try
            {
                Transport.Drain();
                Transport.Write(LoginTokenProtocol.FormatTokenLogin(entry.Token!, connectionType, clientVersion) + "\n");
                reply = await ReadReplyAsync();
            }
            catch
            {
                reply = new OnlineAuthReply { Kind = OnlineAuthReply.ReplyKind.None };
            }

            if (reply.Kind == OnlineAuthReply.ReplyKind.Ok)
            {
                Leftover = StripTokenLine(reply.Leftover, out _);
                terminal.SetColor("bright_green");
                terminal.WriteLine(Loc.Get("online.logged_in"));
                return Step.Authenticated;
            }

            if (reply.Kind == OnlineAuthReply.ReplyKind.Err && reply.TokenRefused)
            {
                // the token is no good (expired, revoked, unknown): forget it, keep the username
                OnlineLoginStore.ClearToken(server, port);
                terminal.SetColor("yellow");
                terminal.WriteLine(Loc.Get("online.token_refused"));
            }
            else if (reply.Kind == OnlineAuthReply.ReplyKind.Err)
            {
                // a throttle or a ban: the token itself may still be good, so it is kept
                terminal.SetColor("bright_red");
                terminal.WriteLine($"  {reply.ErrText}");
            }
            else
            {
                terminal.SetColor("yellow");
                terminal.WriteLine(Loc.Get("online.lost_connection"));
            }
            terminal.WriteLine("");
            return await ReconnectAsync() ? Step.Continue : Step.Failed;
        }

        /// <summary>
        /// The 1.2.7 saved password, for its own server and port only, sent once: with ";token=1" when the server
        /// can issue a token (the player chose to save it). The file is erased after the reply, whatever it was.
        /// </summary>
        private async Task<Step> LegacyLoginAsync(LegacyOnlineLogin legacy)
        {
            terminal.SetColor("gray");
            terminal.WriteLine(Loc.Get("online.logging_in_as", legacy.Username));

            bool remember = TokenCapable == true;
            OnlineAuthReply reply;
            try
            {
                Transport.Drain();
                Transport.Write(PasswordLine(legacy.Username, legacy.Password, false, remember) + "\n");
                reply = await ReadReplyAsync();
            }
            catch
            {
                reply = new OnlineAuthReply { Kind = OnlineAuthReply.ReplyKind.None };
            }
            finally
            {
                OnlineLoginStore.EraseLegacy(legacy.NeverAskToSave);
            }

            if (reply.Kind == OnlineAuthReply.ReplyKind.Ok)
            {
                Leftover = reply.Leftover;
                terminal.SetColor("bright_green");
                terminal.WriteLine(Loc.Get("online.logged_in"));
                if (remember)
                {
                    await TakeTokenAsync(legacy.Username);
                }
                else
                {
                    OnlineLoginStore.SetUsername(server, port, legacy.Username);
                    ShowNoRemember();
                }
                return Step.Authenticated;
            }

            OnlineLoginStore.SetUsername(server, port, legacy.Username);
            string why = reply.Kind == OnlineAuthReply.ReplyKind.Err ? reply.ErrText : Loc.Get("online.lost_connection").Trim();
            terminal.SetColor("yellow");
            terminal.WriteLine(Loc.Get("online.saved_login_failed", why));
            terminal.WriteLine(Loc.Get("online.login_manually"));
            terminal.WriteLine("");
            return await ReconnectAsync() ? Step.Continue : Step.Failed;
        }

        /// <summary>After an OK to a line with ";token=1": the TOKEN line is saved and taken out of the leftover.</summary>
        private async Task TakeTokenAsync(string username)
        {
            string rest = Leftover ?? "";
            var deadline = DateTime.UtcNow + TokenLineTimeout;
            while (rest.IndexOf('\n') < 0 && MayBeTokenLine(rest))
            {
                var remaining = deadline - DateTime.UtcNow;
                if (remaining <= TimeSpan.Zero) break;
                string? more;
                try { more = await Transport.ReadAsync(remaining); }
                catch { more = null; }
                if (more == null) break;
                rest += more;
            }

            Leftover = StripTokenLine(rest, out var tokenLine);
            if (TryParseTokenLine(tokenLine, out var token, out var expiresUtc))
            {
                OnlineLoginStore.SetToken(server, port, username, token, expiresUtc);
                terminal.SetColor("green");
                terminal.WriteLine(Loc.Get("online.remembered"));
            }
            else
            {
                OnlineLoginStore.SetUsername(server, port, username);
                terminal.SetColor("yellow");
                terminal.WriteLine(Loc.Get("online.remember_failed"));
            }
        }

        private static bool MayBeTokenLine(string text) =>
            text.Length < LoginTokenProtocol.TokenReplyPrefix.Length
                ? LoginTokenProtocol.TokenReplyPrefix.StartsWith(text, StringComparison.Ordinal)
                : text.StartsWith(LoginTokenProtocol.TokenReplyPrefix, StringComparison.Ordinal);

        /// <summary>The text without a leading TOKEN line (returned in <paramref name="tokenLine"/>), or null when nothing is left.</summary>
        internal static string? StripTokenLine(string? text, out string? tokenLine)
        {
            tokenLine = null;
            if (string.IsNullOrEmpty(text)) return null;
            if (!text.StartsWith(LoginTokenProtocol.TokenReplyPrefix, StringComparison.Ordinal)) return text;
            int nl = text.IndexOf('\n');
            tokenLine = (nl >= 0 ? text.Substring(0, nl) : text).TrimEnd('\r');
            string rest = nl >= 0 ? text.Substring(nl + 1) : "";
            return rest.Length > 0 ? rest : null;
        }

        /// <summary>"TOKEN:&lt;selector&gt;.&lt;verifier&gt;:&lt;expiresUnix&gt;"; false for "TOKEN:-" and anything else.</summary>
        internal static bool TryParseTokenLine(string? line, out string token, out DateTime expiresUtc)
        {
            token = "";
            expiresUtc = default;
            if (line == null || !line.StartsWith(LoginTokenProtocol.TokenReplyPrefix, StringComparison.Ordinal)) return false;
            string body = line.Substring(LoginTokenProtocol.TokenReplyPrefix.Length);
            int colon = body.LastIndexOf(':');
            if (colon <= 0) return false;
            string candidate = body.Substring(0, colon);
            if (!LoginTokenProtocol.TrySplitToken(candidate, out _, out _)) return false;
            if (!long.TryParse(body.Substring(colon + 1), System.Globalization.NumberStyles.None,
                    System.Globalization.CultureInfo.InvariantCulture, out long unix)) return false;
            try { expiresUtc = DateTimeOffset.FromUnixTimeSeconds(unix).UtcDateTime; }
            catch (ArgumentOutOfRangeException) { return false; }
            token = candidate;
            return true;
        }

        /// <summary>
        /// The capability probe on the relay path: send AUTH:CAPS and read until the relay's menu clear (a new relay
        /// answers CAPS:token=1 first; an old one only redraws its menu) or 3 s, then the rest of the menu. True
        /// only for token=1.
        /// </summary>
        internal async Task<bool> ProbeAsync()
        {
            var text = new StringBuilder();
            try
            {
                Transport.Drain();
                Transport.Write(LoginTokenProtocol.CapsLine + "\n");
                var deadline = DateTime.UtcNow + ProbeTimeout;
                while (text.ToString().IndexOf(MenuClear, StringComparison.Ordinal) < 0)
                {
                    var remaining = deadline - DateTime.UtcNow;
                    if (remaining <= TimeSpan.Zero) break;
                    var chunk = await Transport.ReadAsync(remaining);
                    if (chunk == null) break;
                    text.Append(chunk);
                }
                // The menu's last row is a prompt with no line end: a reply glued to it ("Choice: OK") would not
                // be a whole OK line. So the menu is read to its end (quiet for a moment) before the next line.
                while (text.ToString().IndexOf(MenuClear, StringComparison.Ordinal) >= 0)
                {
                    var remaining = deadline - DateTime.UtcNow;
                    if (remaining <= TimeSpan.Zero) break;
                    var tail = await Transport.ReadAsync(remaining < MenuQuiet ? remaining : MenuQuiet);
                    if (tail == null) break;
                    text.Append(tail);
                }
                Transport.Drain();
            }
            catch
            {
                return false;
            }
            return HasTokenCapability(text.ToString());
        }

        /// <summary>A whole "CAPS:" line whose comma list holds token=1 (other names are ignored).</summary>
        internal static bool HasTokenCapability(string text)
        {
            foreach (var raw in text.Split('\n'))
            {
                var line = raw.Trim();
                if (!line.StartsWith("CAPS:", StringComparison.Ordinal)) continue;
                foreach (var item in line.Substring(5).Split(','))
                    if (item.Trim() == "token=1") return true;
            }
            return false;
        }

        /// <summary>Read until a whole "OK" line or a line starting "ERR:", or 5 s, or the connection closes.</summary>
        internal async Task<OnlineAuthReply> ReadReplyAsync()
        {
            var text = new StringBuilder();
            var deadline = DateTime.UtcNow + ReplyTimeout;
            while (true)
            {
                var matched = MatchReply(text.ToString(), complete: false);
                if (matched != null) return matched;
                var remaining = deadline - DateTime.UtcNow;
                if (remaining <= TimeSpan.Zero) break;
                var chunk = await Transport.ReadAsync(remaining);
                if (chunk == null) break;
                text.Append(chunk);
            }
            var all = text.ToString();
            return MatchReply(all, complete: true) ?? new OnlineAuthReply
            {
                Kind = all.Length == 0 ? OnlineAuthReply.ReplyKind.None : OnlineAuthReply.ReplyKind.Other,
                Length = all.Length,
            };
        }

        /// <summary>
        /// The first line that is exactly "OK" or starts "ERR:" (after trimming), or null. Only lines ended by a
        /// newline count unless <paramref name="complete"/> (nothing more will arrive). An "OK" inside an echoed
        /// line never matches; the leftover starts after the matched line's own end.
        /// </summary>
        internal static OnlineAuthReply? MatchReply(string text, bool complete)
        {
            int pos = 0;
            while (pos < text.Length)
            {
                int nl = text.IndexOf('\n', pos);
                if (nl < 0 && !complete) return null;
                int end = nl < 0 ? text.Length : nl;
                string line = text.Substring(pos, end - pos).Trim();
                int after = nl < 0 ? text.Length : nl + 1;
                if (line == LoginTokenProtocol.OkLine)
                {
                    while (after < text.Length && (text[after] == '\r' || text[after] == '\n')) after++;
                    return new OnlineAuthReply
                    {
                        Kind = OnlineAuthReply.ReplyKind.Ok,
                        Leftover = after < text.Length ? text.Substring(after) : null,
                        Length = text.Length,
                    };
                }
                if (line.StartsWith(LoginTokenProtocol.ErrPrefix, StringComparison.Ordinal))
                {
                    bool refused = line.StartsWith(LoginTokenProtocol.TokenRefusedPrefix.TrimEnd(), StringComparison.Ordinal);
                    string message = refused
                        ? line.Substring(LoginTokenProtocol.TokenRefusedPrefix.TrimEnd().Length).Trim()
                        : line.Substring(LoginTokenProtocol.ErrPrefix.Length).Trim();
                    return new OnlineAuthReply
                    {
                        Kind = OnlineAuthReply.ReplyKind.Err,
                        ErrText = message,
                        TokenRefused = refused,
                        Length = text.Length,
                    };
                }
                pos = after;
            }
            return null;
        }

        private void ShowNoRemember()
        {
            if (noRememberShown) return;
            noRememberShown = true;
            terminal.SetColor("yellow");
            terminal.WriteLine(Loc.Get("online.no_remember_server"));
        }

        private async Task<bool> ReconnectAsync()
        {
            var next = await reconnect();
            if (next == null)
            {
                await terminal.PressAnyKey();
                return false;
            }
            Transport = next;
            return true;
        }
    }
}
