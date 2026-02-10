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
                terminal.WriteLine("Login to existing account");

                terminal.SetColor("darkgray");
                terminal.Write("  [");
                terminal.SetColor("bright_green");
                terminal.Write("R");
                terminal.SetColor("darkgray");
                terminal.Write("] ");
                terminal.SetColor("green");
                terminal.WriteLine("Register new account");

                terminal.SetColor("darkgray");
                terminal.Write("  [");
                terminal.SetColor("bright_red");
                terminal.Write("Q");
                terminal.SetColor("darkgray");
                terminal.Write("] ");
                terminal.SetColor("red");
                terminal.WriteLine("Quit");

                terminal.WriteLine("");
                terminal.SetColor("bright_white");
                terminal.Write("  Your choice: ");

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
                        terminal.WriteLine("  Invalid choice.");
                        terminal.WriteLine("");
                        break;
                }
            }

            terminal.SetColor("bright_red");
            terminal.WriteLine("");
            terminal.WriteLine("  Too many failed attempts. Disconnecting.");
            return null;
        }

        private void ShowBanner()
        {
            terminal.ClearScreen();
            terminal.SetColor("bright_cyan");
            terminal.WriteLine("╔══════════════════════════════════════════════════════════════════════════════╗");
            terminal.SetColor("bright_white");
            terminal.WriteLine("║                        USURPER REBORN - ONLINE                              ║");
            terminal.SetColor("bright_cyan");
            terminal.WriteLine("╚══════════════════════════════════════════════════════════════════════════════╝");
            terminal.WriteLine("");
            terminal.SetColor("gray");
            terminal.WriteLine("  Welcome to the Usurper Reborn online server!");
            terminal.WriteLine("  Login with your account or register a new one.");
            terminal.WriteLine("");
        }

        private async Task<string?> DoLogin()
        {
            terminal.WriteLine("");
            terminal.SetColor("bright_white");
            terminal.Write("  Username: ");
            string? username = await ReadLineAsync();
            if (string.IsNullOrWhiteSpace(username)) return null;

            terminal.Write("  Password: ");
            string? password = await ReadPasswordAsync();
            if (string.IsNullOrWhiteSpace(password)) return null;

            terminal.WriteLine("");
            terminal.SetColor("gray");
            terminal.WriteLine("  Authenticating...");

            var (success, displayName, message) = await backend.AuthenticatePlayer(username.Trim(), password);

            if (success)
            {
                terminal.SetColor("bright_green");
                terminal.WriteLine($"  Welcome back, {displayName}!");
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
            terminal.WriteLine("  ═══ Create New Account ═══");
            terminal.WriteLine("");

            terminal.SetColor("gray");
            terminal.WriteLine("  Choose a username (2-20 characters, letters/numbers/spaces).");
            terminal.WriteLine("  This will also be your character name in the game.");
            terminal.WriteLine("");

            terminal.SetColor("bright_white");
            terminal.Write("  Username: ");
            string? username = await ReadLineAsync();
            if (string.IsNullOrWhiteSpace(username)) return null;

            terminal.Write("  Password: ");
            string? password = await ReadPasswordAsync();
            if (string.IsNullOrWhiteSpace(password)) return null;

            terminal.Write("  Confirm password: ");
            string? confirm = await ReadPasswordAsync();
            if (string.IsNullOrWhiteSpace(confirm)) return null;

            if (password != confirm)
            {
                terminal.SetColor("bright_red");
                terminal.WriteLine("");
                terminal.WriteLine("  Passwords do not match.");
                await Task.Delay(1500);
                return null;
            }

            terminal.WriteLine("");
            terminal.SetColor("gray");
            terminal.WriteLine("  Creating account...");

            var (success, message) = await backend.RegisterPlayer(username.Trim(), password);

            if (success)
            {
                terminal.SetColor("bright_green");
                terminal.WriteLine($"  {message}");
                terminal.WriteLine("");
                terminal.SetColor("white");
                terminal.WriteLine($"  You are now logged in as: {username.Trim()}");
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
        /// Read a line of text. Uses Console.ReadLine since we're in stdio mode.
        /// </summary>
        private async Task<string?> ReadLineAsync()
        {
            return await Task.Run(() =>
            {
                try { return Console.ReadLine(); }
                catch { return null; }
            });
        }

        /// <summary>
        /// Read a password with asterisk masking.
        /// In stdio mode, we read char by char and echo asterisks.
        /// Falls back to plain ReadLine if char-by-char isn't available.
        /// </summary>
        private async Task<string?> ReadPasswordAsync()
        {
            return await Task.Run(() =>
            {
                try
                {
                    // In stdio mode (redirected I/O), we can't intercept individual keys.
                    // Just read the whole line - the SSH terminal handles echo on the client side.
                    if (Console.IsInputRedirected)
                    {
                        var line = Console.ReadLine();
                        return line;
                    }

                    // Local console: mask with asterisks
                    var password = new System.Text.StringBuilder();
                    while (true)
                    {
                        var key = Console.ReadKey(intercept: true);
                        if (key.Key == ConsoleKey.Enter)
                        {
                            Console.WriteLine();
                            break;
                        }
                        else if (key.Key == ConsoleKey.Backspace && password.Length > 0)
                        {
                            password.Remove(password.Length - 1, 1);
                            Console.Write("\b \b");
                        }
                        else if (key.KeyChar != '\0' && key.Key != ConsoleKey.Backspace)
                        {
                            password.Append(key.KeyChar);
                            Console.Write("*");
                        }
                    }
                    return password.ToString();
                }
                catch
                {
                    return null;
                }
            });
        }
    }
}
