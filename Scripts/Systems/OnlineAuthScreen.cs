using System;
using System.Threading.Tasks;
using UsurperRemake.BBS;

namespace UsurperRemake.Systems
{
    /// <summary>
    /// Handles player authentication for online multiplayer mode.
    /// Shows login/register screen when a player connects via SSH.
    /// Returns the authenticated username or null if the player disconnects.
    /// </summary>
    public class OnlineAuthScreen
    {
        private readonly SqlSaveBackend backend;
        private readonly BBSTerminalAdapter terminal;
        private const int MAX_ATTEMPTS = 5;

        public OnlineAuthScreen(SqlSaveBackend backend, BBSTerminalAdapter terminal)
        {
            this.backend = backend;
            this.terminal = terminal;
        }

        /// <summary>
        /// Run the authentication flow. Returns the authenticated username, or null if aborted.
        /// </summary>
        public async Task<string?> RunAsync()
        {
            int attempts = 0;

            while (attempts < MAX_ATTEMPTS)
            {
                ShowBanner();

                terminal.SetColor("darkgray");
                terminal.Write("  [");
                terminal.SetColor("bright_cyan");
                terminal.Write("L");
                terminal.SetColor("darkgray");
                terminal.Write("] ");
                terminal.SetColor("white");
                terminal.WriteLine(Loc.Get("auth.login"));

                terminal.SetColor("darkgray");
                terminal.Write("  [");
                terminal.SetColor("bright_green");
                terminal.Write("R");
                terminal.SetColor("darkgray");
                terminal.Write("] ");
                terminal.SetColor("green");
                terminal.WriteLine(Loc.Get("auth.register"));

                terminal.SetColor("darkgray");
                terminal.Write("  [");
                terminal.SetColor("bright_red");
                terminal.Write("Q");
                terminal.SetColor("darkgray");
                terminal.Write("] ");
                terminal.SetColor("red");
                terminal.WriteLine(Loc.Get("auth.quit"));

                terminal.WriteLine("");
                terminal.SetColor("bright_white");
                terminal.Write($"  {Loc.Get("auth.your_choice")}");

                string? choice = await ReadLineAsync();
                if (choice == null) return null;

                switch (choice.Trim().ToUpper())
                {
                    case "L":
                        var loginResult = await DoLogin();
                        if (loginResult != null) return loginResult;
                        attempts++;
                        break;

                    case "R":
                        var registerResult = await DoRegister();
                        if (registerResult != null) return registerResult;
                        break;

                    case "Q":
                    case "":
                        return null;

                    default:
                        terminal.SetColor("yellow");
                        terminal.WriteLine($"  {Loc.Get("auth.invalid_choice")}");
                        terminal.WriteLine("");
                        break;
                }
            }

            terminal.SetColor("bright_red");
            terminal.WriteLine("");
            terminal.WriteLine($"  {Loc.Get("auth.too_many_attempts")}");
            return null;
        }

        private void ShowBanner()
        {
            terminal.ClearScreen();

            string titleText = Loc.Get("auth.title");
            const int innerWidth = 78;
            int leftPad  = (innerWidth - titleText.Length) / 2;
            int rightPad = innerWidth - titleText.Length - leftPad;

            if (GameConfig.ScreenReaderMode)
            {
                terminal.SetColor("bright_cyan");
                terminal.WriteLine(titleText);
            }
            else
            {
                terminal.SetColor("bright_cyan");
                terminal.WriteLine($"╔{new string('═', innerWidth)}╗");
                terminal.WriteLine($"║{new string(' ', leftPad)}{titleText}{new string(' ', rightPad)}║");
                terminal.WriteLine($"╚{new string('═', innerWidth)}╝");
            }
            terminal.WriteLine("");
            terminal.SetColor("gray");
            terminal.WriteLine($"  {Loc.Get("auth.welcome")}");
            terminal.WriteLine($"  {Loc.Get("auth.welcome_instruction")}");
            terminal.WriteLine("");
        }

        private async Task<string?> DoLogin()
        {
            terminal.WriteLine("");
            terminal.SetColor("bright_white");
            terminal.Write($"  {Loc.Get("auth.username")}");
            string? username = await ReadLineAsync();
            if (string.IsNullOrWhiteSpace(username)) return null;

            terminal.Write($"  {Loc.Get("auth.password")}");
            string? password = await ReadPasswordAsync();
            if (string.IsNullOrWhiteSpace(password)) return null;

            terminal.WriteLine("");
            terminal.SetColor("gray");
            terminal.WriteLine($"  {Loc.Get("auth.authenticating")}");

            var (success, displayName, message, screenReader, language) = await backend.AuthenticatePlayer(username.Trim(), password);

            if (success)
            {
                // Apply account-level preferences
                if (screenReader)
                    GameConfig.ScreenReaderMode = true;
                if (!string.IsNullOrEmpty(language))
                    GameConfig.Language = language;
                // If player appears online from a stale/crashed session, clear it and continue
                if (await backend.IsPlayerOnline(username.Trim()))
                {
                    await backend.UnregisterOnline(username.Trim());
                    terminal.SetColor("yellow");
                    terminal.WriteLine($"  {Loc.Get("auth.prev_session_disconnected")}");
                    terminal.SetColor("white");
                    terminal.WriteLine("");
                }

                terminal.SetColor("bright_green");
                terminal.WriteLine($"  {Loc.Get("auth.welcome_back", displayName)}");
                terminal.WriteLine("");
                await Task.Delay(1000);
                return displayName;
            }
            else
            {
                terminal.SetColor("bright_red");
                terminal.WriteLine($"  {message}");
                terminal.WriteLine("");
                await Task.Delay(1500);
                return null;
            }
        }

        private async Task<string?> DoRegister()
        {
            terminal.WriteLine("");
            terminal.SetColor("bright_green");
            if (GameConfig.ScreenReaderMode)
                terminal.WriteLine($"  {Loc.Get("auth.create_sr")}");
            else
                terminal.WriteLine($"  ═══ {Loc.Get("auth.create_new_account")} ═══");
            terminal.WriteLine("");

            terminal.SetColor("gray");
            terminal.WriteLine($"  {Loc.Get("auth.username_instructions")}");
            terminal.WriteLine($"  {Loc.Get("auth.username_also_name")}");
            terminal.WriteLine("");

            terminal.SetColor("bright_white");
            terminal.Write($"  {Loc.Get("auth.username")}");
            string? username = await ReadLineAsync();
            if (string.IsNullOrWhiteSpace(username)) return null;

            terminal.Write($"  {Loc.Get("auth.password")}");
            string? password = await ReadPasswordAsync();
            if (string.IsNullOrWhiteSpace(password)) return null;

            terminal.Write($"  {Loc.Get("auth.confirm_password")}");
            string? confirm = await ReadPasswordAsync();
            if (string.IsNullOrWhiteSpace(confirm)) return null;

            if (password != confirm)
            {
                terminal.SetColor("bright_red");
                terminal.WriteLine("");
                terminal.WriteLine($"  {Loc.Get("auth.passwords_no_match")}");
                await Task.Delay(1500);
                return null;
            }

            terminal.WriteLine("");
            terminal.SetColor("gray");
            terminal.WriteLine($"  {Loc.Get("auth.creating_account")}");

            var (success, message) = await backend.RegisterPlayer(username.Trim(), password);

            if (success)
            {
                terminal.SetColor("bright_green");
                terminal.WriteLine($"  {message}");
                terminal.WriteLine("");
                terminal.SetColor("white");
                terminal.WriteLine($"  {Loc.Get("auth.logged_in_as", username.Trim())}");
                terminal.WriteLine("");
                await Task.Delay(1500);
                return username.Trim();
            }
            else
            {
                terminal.SetColor("bright_red");
                terminal.WriteLine($"  {message}");
                terminal.WriteLine("");
                await Task.Delay(1500);
                return null;
            }
        }

        /// <summary>
        /// Read a line of text with proper backspace handling for SSH/online mode.
        /// </summary>
        private async Task<string?> ReadLineAsync()
        {
            return await Task.Run(() =>
            {
                try { return TerminalEmulator.ReadLineWithBackspace(); }
                catch { return null; }
            });
        }

        /// <summary>
        /// Read a password with asterisk masking and backspace support.
        /// Works on both redirected stdin (pipes) and PTY (SSH terminals).
        /// </summary>
        private async Task<string?> ReadPasswordAsync()
        {
            return await Task.Run(() =>
            {
                try
                {
                    return TerminalEmulator.ReadLineWithBackspace(maskPassword: true);
                }
                catch
                {
                    return null;
                }
            });
        }
    }
}
