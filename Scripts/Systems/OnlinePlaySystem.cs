using System;
using System.IO;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Renci.SshNet;

namespace UsurperRemake.Systems
{
    /// <summary>
    /// Client-side system for connecting to an online Usurper Reborn MUD server via SSH.
    /// Connects to the game server's SSH gateway (usurper@server:4000), which runs a relay
    /// that bridges to the MUD game server. Authentication (Login/Register) and credential
    /// storage are handled locally, with AUTH headers sent through the encrypted SSH channel.
    ///
    /// Flow:
    ///   1. SSH connect to server:4000 as gateway user "usurper" / password "play"
    ///   2. sshd ForceCommand launches relay client
    ///   3. Client sends AUTH:username:password:connectionType through SSH shell
    ///   4. Relay authenticates with MUD server and bridges I/O
    ///   5. All game traffic flows through the encrypted SSH tunnel
    /// </summary>
    public class OnlinePlaySystem
    {
        private const string OFFICIAL_SERVER = "play.usurper-reborn.net";
        private const int OFFICIAL_PORT = 4000;
        private const string SSH_GATEWAY_USER = "usurper";
        private const string SSH_GATEWAY_PASS = "play";
        private const string CREDENTIALS_FILE = "online_credentials.json";

        private readonly TerminalEmulator terminal;
        private SshClient? sshClient;
        private ShellStream? shellStream;
        private CancellationTokenSource? cancellationSource;
        private bool vtProcessingEnabled = false;

        public OnlinePlaySystem(TerminalEmulator terminal)
        {
            this.terminal = terminal;
        }

        /// <summary>
        /// Main entry point for the [O]nline Play menu option.
        /// Shows connection info and connects directly to the MUD server.
        /// </summary>
        public async Task StartOnlinePlay()
        {
            terminal.ClearScreen();

            terminal.SetColor("bright_cyan");
            terminal.WriteLine("╔══════════════════════════════════════════════════════════════════════════════╗");
            terminal.SetColor("bright_white");
            terminal.WriteLine("║                           ONLINE PLAY                                       ║");
            terminal.SetColor("bright_cyan");
            terminal.WriteLine("╚══════════════════════════════════════════════════════════════════════════════╝");
            terminal.WriteLine("");

            terminal.SetColor("gray");
            terminal.WriteLine("  Connect to the Usurper Reborn online server.");
            terminal.WriteLine("  Your online character is stored on the server - separate from local saves.");
            terminal.WriteLine("  You will login or create an account after connecting.");
            terminal.WriteLine("");

            terminal.SetColor("bright_white");
            terminal.Write("  Server: ");
            terminal.SetColor("bright_green");
            terminal.WriteLine($"{OFFICIAL_SERVER}:{OFFICIAL_PORT}");
            terminal.WriteLine("");

            terminal.SetColor("darkgray");
            terminal.Write("  [");
            terminal.SetColor("bright_green");
            terminal.Write("C");
            terminal.SetColor("darkgray");
            terminal.Write("] ");
            terminal.SetColor("white");
            terminal.WriteLine("Connect");

            terminal.SetColor("darkgray");
            terminal.Write("  [");
            terminal.SetColor("bright_red");
            terminal.Write("B");
            terminal.SetColor("darkgray");
            terminal.Write("] ");
            terminal.SetColor("gray");
            terminal.WriteLine("Back to Main Menu");

            terminal.WriteLine("");
            terminal.SetColor("bright_white");
            var menuChoice = await terminal.GetInput("  Your choice: ");

            if (menuChoice.Trim().ToUpper() != "C")
                return;

            await ConnectAndPlay(OFFICIAL_SERVER, OFFICIAL_PORT);
        }

        /// <summary>
        /// Establish SSH connection to the game server's SSH gateway.
        /// Returns true if connected, false if connection failed.
        /// </summary>
        private bool EstablishSSHConnection(string server, int port)
        {
            terminal.WriteLine("");
            terminal.SetColor("bright_cyan");
            terminal.WriteLine($"  Connecting to {server}:{port} (encrypted)...");

            try
            {
                sshClient = new SshClient(server, port, SSH_GATEWAY_USER, SSH_GATEWAY_PASS);
                sshClient.ConnectionInfo.Timeout = TimeSpan.FromSeconds(10);
                sshClient.Connect();

                // Open a shell stream (pseudo-terminal) for interactive I/O
                shellStream = sshClient.CreateShellStream("xterm", 80, 24, 800, 600, 4096);

                terminal.SetColor("bright_green");
                terminal.WriteLine("  Connected (SSH encrypted)!");
                terminal.WriteLine("");
                return true;
            }
            catch (Renci.SshNet.Common.SshConnectionException ex)
            {
                terminal.SetColor("bright_red");
                terminal.WriteLine("");
                terminal.WriteLine($"  SSH connection failed: {ex.Message}");
                terminal.SetColor("gray");
                terminal.WriteLine("  The server may be down or unreachable.");
                return false;
            }
            catch (System.Net.Sockets.SocketException ex)
            {
                terminal.SetColor("bright_red");
                terminal.WriteLine("");
                terminal.WriteLine($"  Connection failed: {ex.Message}");
                terminal.SetColor("gray");
                terminal.WriteLine("  The server may be down or unreachable.");
                return false;
            }
            catch (Exception ex)
            {
                terminal.SetColor("bright_red");
                terminal.WriteLine("");
                terminal.WriteLine($"  Error: {ex.Message}");
                DebugLogger.Instance.LogError("ONLINE_PLAY", $"SSH connection error: {ex}");
                return false;
            }
        }

        /// <summary>
        /// Connect to the MUD server via SSH, authenticate, and pipe I/O.
        /// Shows a Login/Register menu, sends AUTH header through SSH tunnel, then bridges I/O.
        /// </summary>
        private async Task ConnectAndPlay(string server, int port)
        {
            if (!EstablishSSHConnection(server, port))
            {
                await terminal.PressAnyKey();
                return;
            }

            // Drain any initial relay output (menu banner) before sending AUTH
            await Task.Delay(500);
            DrainShellStream();

            // Auth loop — prompt for Login/Register until success or user quits
            bool authenticated = false;
            int attempts = 0;
            const int MAX_ATTEMPTS = 5;

            // Check for saved credentials
            var savedCreds = LoadSavedCredentials();

            while (!authenticated && attempts < MAX_ATTEMPTS)
            {
                attempts++;

                // If we have saved credentials on first attempt, try them automatically
                if (savedCreds != null && attempts == 1)
                {
                    terminal.SetColor("gray");
                    terminal.WriteLine($"  Logging in as {savedCreds.Username}...");

                    string authLine = $"AUTH:{savedCreds.Username}:{savedCreds.GetDecodedPassword()}:{GetConnectionType()}\n";
                    shellStream!.Write(authLine);
                    shellStream.Flush();

                    var response = await ReadLineFromShell();
                    if (response != null && response.Contains("OK"))
                    {
                        terminal.SetColor("bright_green");
                        terminal.WriteLine("  Logged in!");
                        authenticated = true;
                        break;
                    }
                    else
                    {
                        // Saved credentials failed — need to reconnect
                        terminal.SetColor("yellow");
                        var errMsg = response?.Contains("ERR:") == true
                            ? response.Substring(response.IndexOf("ERR:") + 4).Trim()
                            : "Connection lost";
                        terminal.WriteLine($"  Saved login failed: {errMsg}");
                        terminal.WriteLine("  Please log in manually.");
                        terminal.WriteLine("");
                        DeleteSavedCredentials();
                        savedCreds = null;

                        // Reconnect SSH for next attempt
                        Disconnect();
                        if (!EstablishSSHConnection(server, port))
                        {
                            await terminal.PressAnyKey();
                            return;
                        }
                        await Task.Delay(500);
                        DrainShellStream();
                    }
                }

                // Show Login/Register menu
                terminal.SetColor("bright_cyan");
                terminal.WriteLine("  ──────────────────────────────────────");
                terminal.SetColor("bright_white");
                terminal.WriteLine("  Account Login");
                terminal.SetColor("bright_cyan");
                terminal.WriteLine("  ──────────────────────────────────────");
                terminal.WriteLine("");

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
                terminal.SetColor("white");
                terminal.WriteLine("Register new account");

                terminal.SetColor("darkgray");
                terminal.Write("  [");
                terminal.SetColor("bright_red");
                terminal.Write("Q");
                terminal.SetColor("darkgray");
                terminal.Write("] ");
                terminal.SetColor("gray");
                terminal.WriteLine("Quit");

                terminal.WriteLine("");
                terminal.SetColor("bright_white");
                var choice = (await terminal.GetInput("  Choice: ")).Trim().ToUpper();

                if (choice == "Q" || string.IsNullOrEmpty(choice))
                {
                    Disconnect();
                    return;
                }

                string? username = null;
                string? password = null;
                bool isRegistration = false;

                if (choice == "L")
                {
                    // Login
                    terminal.WriteLine("");
                    terminal.SetColor("bright_white");
                    username = (await terminal.GetInput("  Username: ")).Trim();
                    if (string.IsNullOrEmpty(username)) continue;

                    terminal.Write("  Password: ", "bright_white");
                    password = (await Task.Run(() => TerminalEmulator.ReadLineWithBackspace(maskPassword: true))).Trim();
                    if (string.IsNullOrEmpty(password)) continue;
                }
                else if (choice == "R")
                {
                    // Register
                    terminal.WriteLine("");
                    terminal.SetColor("bright_green");
                    username = (await terminal.GetInput("  Choose a username: ")).Trim();
                    if (string.IsNullOrEmpty(username)) continue;

                    if (username.Length < 2 || username.Length > 20)
                    {
                        terminal.SetColor("bright_red");
                        terminal.WriteLine("  Username must be 2-20 characters.");
                        terminal.WriteLine("");
                        continue;
                    }

                    terminal.Write("  Choose a password: ", "bright_green");
                    password = (await Task.Run(() => TerminalEmulator.ReadLineWithBackspace(maskPassword: true))).Trim();
                    if (string.IsNullOrEmpty(password)) continue;

                    if (password.Length < 4)
                    {
                        terminal.SetColor("bright_red");
                        terminal.WriteLine("  Password must be at least 4 characters.");
                        terminal.WriteLine("");
                        continue;
                    }

                    terminal.Write("  Confirm password: ", "bright_green");
                    var confirm = (await Task.Run(() => TerminalEmulator.ReadLineWithBackspace(maskPassword: true))).Trim();
                    if (password != confirm)
                    {
                        terminal.SetColor("bright_red");
                        terminal.WriteLine("  Passwords do not match.");
                        terminal.WriteLine("");
                        continue;
                    }

                    isRegistration = true;
                }
                else
                {
                    continue; // Invalid choice
                }

                // Send AUTH header through SSH tunnel to the relay
                string connType = GetConnectionType();
                string authHeader;
                if (isRegistration)
                    authHeader = $"AUTH:{username}:{password}:REGISTER:{connType}\n";
                else
                    authHeader = $"AUTH:{username}:{password}:{connType}\n";

                try
                {
                    shellStream!.Write(authHeader);
                    shellStream.Flush();
                }
                catch
                {
                    terminal.SetColor("bright_red");
                    terminal.WriteLine("  Lost connection to server.");
                    await terminal.PressAnyKey();
                    Disconnect();
                    return;
                }

                // Read response from relay (may include relay menu output mixed in)
                var authResponse = await ReadLineFromShell();
                if (authResponse == null)
                {
                    terminal.SetColor("bright_red");
                    terminal.WriteLine("  Server closed the connection.");
                    await terminal.PressAnyKey();
                    Disconnect();
                    return;
                }

                // The relay echoes back through the SSH shell — look for OK or ERR in response
                if (authResponse.Contains("ERR:"))
                {
                    var errStart = authResponse.IndexOf("ERR:") + 4;
                    var errEnd = authResponse.IndexOf('\n', errStart);
                    var errText = errEnd > errStart
                        ? authResponse.Substring(errStart, errEnd - errStart).Trim()
                        : authResponse.Substring(errStart).Trim();

                    terminal.SetColor("bright_red");
                    terminal.WriteLine($"  {errText}");
                    terminal.WriteLine("");

                    // Reconnect SSH for next attempt
                    Disconnect();
                    if (!EstablishSSHConnection(server, port))
                    {
                        await terminal.PressAnyKey();
                        return;
                    }
                    await Task.Delay(500);
                    DrainShellStream();
                    continue;
                }

                if (authResponse.Contains("OK"))
                {
                    authenticated = true;

                    // Offer to save credentials
                    terminal.SetColor("bright_green");
                    terminal.Write("  Authenticated! ");
                    terminal.SetColor("gray");
                    var save = (await terminal.GetInput("Save credentials for next time? (Y/N): ")).Trim().ToUpper();
                    if (save == "Y")
                    {
                        SaveCredentials(server, port, username!, password!);
                        terminal.SetColor("green");
                        terminal.WriteLine("  Credentials saved.");
                    }
                }
                else
                {
                    terminal.SetColor("bright_red");
                    terminal.WriteLine($"  Unexpected response from server.");
                    DebugLogger.Instance.LogError("ONLINE_PLAY", $"Unexpected auth response: {authResponse}");
                    Disconnect();
                    return;
                }
            }

            if (!authenticated)
            {
                terminal.SetColor("bright_red");
                terminal.WriteLine("  Too many failed attempts. Returning to menu.");
                await terminal.PressAnyKey();
                Disconnect();
                return;
            }

            // Auth succeeded — bridge local terminal ↔ SSH stream
            terminal.SetColor("bright_green");
            terminal.WriteLine("  Starting game session...");
            terminal.WriteLine("");

            cancellationSource = new CancellationTokenSource();
            try
            {
                await PipeIO(cancellationSource.Token);
            }
            finally
            {
                Disconnect();
            }
        }

        /// <summary>
        /// Drain any pending data from the SSH shell stream (e.g. relay's menu banner).
        /// Discards the data since we handle auth locally.
        /// </summary>
        private void DrainShellStream()
        {
            try
            {
                if (shellStream == null) return;
                while (shellStream.DataAvailable)
                {
                    var buffer = new byte[4096];
                    shellStream.Read(buffer, 0, buffer.Length);
                }
            }
            catch { }
        }

        /// <summary>
        /// Read response data from the SSH shell stream after sending an AUTH header.
        /// Waits up to 5 seconds for data, returns the accumulated text.
        /// The SSH PTY echoes our AUTH input back, so we look for "OK" or "ERR:"
        /// as discrete lines (not just substring matches) to avoid false positives.
        /// </summary>
        private async Task<string?> ReadLineFromShell()
        {
            try
            {
                var result = new StringBuilder();
                var deadline = DateTime.UtcNow.AddSeconds(5);

                while (DateTime.UtcNow < deadline)
                {
                    if (shellStream!.DataAvailable)
                    {
                        var buffer = new byte[4096];
                        int read = shellStream.Read(buffer, 0, buffer.Length);
                        if (read > 0)
                        {
                            result.Append(Encoding.UTF8.GetString(buffer, 0, read));

                            // Check each line for auth response markers.
                            // The PTY echoes our AUTH input, so we need to look for
                            // "OK" or "ERR:" as their own lines, not inside the echo.
                            var text = result.ToString();
                            var lines = text.Split(new[] { '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries);
                            foreach (var line in lines)
                            {
                                var trimmed = line.Trim();
                                if (trimmed == "OK" || trimmed.StartsWith("ERR:"))
                                    return text;
                            }
                        }
                    }
                    else
                    {
                        await Task.Delay(50);
                    }
                }

                return result.Length > 0 ? result.ToString() : null;
            }
            catch
            {
                return null;
            }
        }

        /// <summary>
        /// Determine connection type string for the AUTH header.
        /// </summary>
        private static string GetConnectionType()
        {
#if STEAM_BUILD
            return "Steam";
#else
            return "Local";
#endif
        }

        /// <summary>
        /// Pipe input/output between the local console and the remote SSH shell stream.
        /// The remote game sends ANSI escape codes for colors. We use a fallback ANSI
        /// parser that translates escape codes to Console.ForegroundColor calls.
        /// Runs until the connection is closed or the player disconnects (Ctrl+]).
        /// </summary>
        private async Task PipeIO(CancellationToken ct)
        {
            // Always use our fallback ANSI parser which translates escape codes to
            // Console.ForegroundColor calls. Windows VT processing claims to work
            // (escape chars get consumed) but often fails to actually render colors.
            vtProcessingEnabled = false;

            Console.OutputEncoding = Encoding.UTF8;

            terminal.SetColor("gray");
            terminal.WriteLine("  Press Ctrl+] to disconnect.");
            terminal.WriteLine("");

            // Read thread: read from SSH shell and write to Console.Out
            var readTask = Task.Run(async () =>
            {
                var buffer = new byte[4096];
                try
                {
                    while (!ct.IsCancellationRequested)
                    {
                        if (shellStream!.DataAvailable)
                        {
                            int bytesRead = shellStream.Read(buffer, 0, buffer.Length);
                            if (bytesRead == 0) break; // Server closed connection

                            var text = Encoding.UTF8.GetString(buffer, 0, bytesRead);
                            if (vtProcessingEnabled)
                            {
                                Console.Write(text);
                            }
                            else
                            {
                                WriteAnsiToConsole(text);
                            }
                        }
                        else
                        {
                            // Check if SSH is still connected
                            if (sshClient == null || !sshClient.IsConnected)
                                break;
                            await Task.Delay(10, ct);
                        }
                    }
                }
                catch (OperationCanceledException) { }
                catch (IOException) { }
                catch (ObjectDisposedException) { }
                catch (Exception ex)
                {
                    DebugLogger.Instance.LogError("ONLINE_PLAY", $"Read error: {ex.Message}");
                }
            }, ct);

            // Write loop: read keys from Console and send to SSH shell
            try
            {
                while (!ct.IsCancellationRequested)
                {
                    // Check if the read task ended (server disconnected)
                    if (readTask.IsCompleted)
                        break;

                    if (Console.KeyAvailable)
                    {
                        var keyInfo = Console.ReadKey(intercept: true);

                        // Ctrl+] = disconnect (classic telnet escape)
                        if (keyInfo.Key == ConsoleKey.Oem6 && keyInfo.Modifiers.HasFlag(ConsoleModifiers.Control))
                        {
                            break;
                        }

                        // Convert key to string and send to server
                        string keyStr;
                        if (keyInfo.Key == ConsoleKey.Enter)
                            keyStr = "\r";
                        else if (keyInfo.Key == ConsoleKey.Backspace)
                            keyStr = "\x7f";
                        else if (keyInfo.Key == ConsoleKey.Escape)
                            keyStr = "\x1b";
                        else if (keyInfo.Key == ConsoleKey.Tab)
                            keyStr = "\t";
                        else if (keyInfo.Key == ConsoleKey.UpArrow)
                            keyStr = "\x1b[A";
                        else if (keyInfo.Key == ConsoleKey.DownArrow)
                            keyStr = "\x1b[B";
                        else if (keyInfo.Key == ConsoleKey.RightArrow)
                            keyStr = "\x1b[C";
                        else if (keyInfo.Key == ConsoleKey.LeftArrow)
                            keyStr = "\x1b[D";
                        else if (keyInfo.KeyChar != '\0')
                            keyStr = keyInfo.KeyChar.ToString();
                        else
                            continue;

                        shellStream!.Write(keyStr);
                        shellStream.Flush();
                    }
                    else
                    {
                        await Task.Delay(10, ct);
                    }
                }
            }
            catch (OperationCanceledException) { }
            catch (IOException) { }
            catch (ObjectDisposedException) { }
            catch (Exception ex)
            {
                DebugLogger.Instance.LogError("ONLINE_PLAY", $"Write error: {ex.Message}");
            }

            // Wait for read task to finish
            cancellationSource?.Cancel();
            try { await readTask; } catch { }

            terminal.WriteLine("");
            terminal.SetColor("yellow");
            terminal.WriteLine("  Disconnected from server.");
            await terminal.PressAnyKey();
        }

        // =====================================================================
        // Fallback ANSI Parser - translates ANSI escape codes to Console colors
        // when native VT processing is not available
        // =====================================================================

        private bool ansiBold = false;
        private string ansiBuffer = "";

        /// <summary>
        /// Parse ANSI escape codes and translate them to Console.ForegroundColor/
        /// Console.BackgroundColor calls. Handles SGR (colors), clear screen,
        /// and cursor positioning.
        /// </summary>
        private void WriteAnsiToConsole(string text)
        {
            ansiBuffer += text;
            int i = 0;

            while (i < ansiBuffer.Length)
            {
                if (ansiBuffer[i] == '\x1b')
                {
                    // Check if we have enough data for a complete escape sequence
                    if (i + 1 >= ansiBuffer.Length)
                    {
                        // Incomplete sequence - save for next call
                        ansiBuffer = ansiBuffer.Substring(i);
                        return;
                    }

                    if (ansiBuffer[i + 1] == '[')
                    {
                        // CSI sequence: \x1b[...X
                        int seqStart = i + 2;
                        int seqEnd = seqStart;
                        while (seqEnd < ansiBuffer.Length && ansiBuffer[seqEnd] >= 0x20 && ansiBuffer[seqEnd] <= 0x3F)
                            seqEnd++; // parameter bytes

                        if (seqEnd >= ansiBuffer.Length)
                        {
                            // Incomplete sequence
                            ansiBuffer = ansiBuffer.Substring(i);
                            return;
                        }

                        char command = ansiBuffer[seqEnd];
                        string parameters = ansiBuffer.Substring(seqStart, seqEnd - seqStart);
                        i = seqEnd + 1;

                        ProcessCsiSequence(command, parameters);
                    }
                    else
                    {
                        // Unknown escape - skip \x1b and the next char
                        i += 2;
                    }
                }
                else
                {
                    // Regular character - find the next escape or end
                    int next = ansiBuffer.IndexOf('\x1b', i);
                    if (next == -1)
                    {
                        Console.Write(ansiBuffer.Substring(i));
                        i = ansiBuffer.Length;
                    }
                    else
                    {
                        Console.Write(ansiBuffer.Substring(i, next - i));
                        i = next;
                    }
                }
            }

            ansiBuffer = "";
        }

        /// <summary>
        /// Process a CSI (Control Sequence Introducer) escape sequence.
        /// </summary>
        private void ProcessCsiSequence(char command, string parameters)
        {
            switch (command)
            {
                case 'm': // SGR - Select Graphic Rendition (colors)
                    ProcessSgr(parameters);
                    break;

                case 'J': // Erase in Display
                    if (parameters == "2" || parameters == "")
                    {
                        try { Console.Clear(); } catch { }
                    }
                    break;

                case 'H': // Cursor Position
                case 'f': // Horizontal Vertical Position
                    ProcessCursorPosition(parameters);
                    break;

                case 'K': // Erase in Line
                    try
                    {
                        int clearLen = Console.BufferWidth - Console.CursorLeft;
                        if (clearLen > 0)
                            Console.Write(new string(' ', clearLen));
                        Console.CursorLeft = Math.Max(0, Console.CursorLeft - clearLen);
                    }
                    catch { }
                    break;

                case 'A': // Cursor Up
                    try { Console.CursorTop = Math.Max(0, Console.CursorTop - ParseInt(parameters, 1)); } catch { }
                    break;

                case 'B': // Cursor Down
                    try { Console.CursorTop = Math.Min(Console.BufferHeight - 1, Console.CursorTop + ParseInt(parameters, 1)); } catch { }
                    break;

                case 'C': // Cursor Forward
                    try { Console.CursorLeft = Math.Min(Console.BufferWidth - 1, Console.CursorLeft + ParseInt(parameters, 1)); } catch { }
                    break;

                case 'D': // Cursor Back
                    try { Console.CursorLeft = Math.Max(0, Console.CursorLeft - ParseInt(parameters, 1)); } catch { }
                    break;

                // Other sequences silently ignored
            }
        }

        private void ProcessCursorPosition(string parameters)
        {
            try
            {
                if (string.IsNullOrEmpty(parameters))
                {
                    Console.SetCursorPosition(0, 0);
                    return;
                }
                var parts = parameters.Split(';');
                int row = parts.Length > 0 && int.TryParse(parts[0], out int r) ? Math.Max(1, r) : 1;
                int col = parts.Length > 1 && int.TryParse(parts[1], out int c) ? Math.Max(1, c) : 1;
                Console.SetCursorPosition(
                    Math.Min(col - 1, Console.BufferWidth - 1),
                    Math.Min(row - 1, Console.BufferHeight - 1));
            }
            catch { }
        }

        /// <summary>
        /// Process SGR (Select Graphic Rendition) parameters for color changes.
        /// </summary>
        private void ProcessSgr(string parameters)
        {
            if (string.IsNullOrEmpty(parameters))
            {
                // \x1b[m is equivalent to \x1b[0m (reset)
                Console.ResetColor();
                ansiBold = false;
                return;
            }

            var codes = parameters.Split(';');
            foreach (var codeStr in codes)
            {
                if (!int.TryParse(codeStr, out int code))
                    continue;

                switch (code)
                {
                    case 0: // Reset
                        Console.ResetColor();
                        ansiBold = false;
                        break;
                    case 1: // Bold/Bright
                        ansiBold = true;
                        // If we already have a foreground color set, brighten it
                        Console.ForegroundColor = BrightenColor(Console.ForegroundColor);
                        break;
                    case 22: // Normal intensity
                        ansiBold = false;
                        break;

                    // Standard foreground colors (30-37)
                    case 30: Console.ForegroundColor = ansiBold ? ConsoleColor.DarkGray : ConsoleColor.Black; break;
                    case 31: Console.ForegroundColor = ansiBold ? ConsoleColor.Red : ConsoleColor.DarkRed; break;
                    case 32: Console.ForegroundColor = ansiBold ? ConsoleColor.Green : ConsoleColor.DarkGreen; break;
                    case 33: Console.ForegroundColor = ansiBold ? ConsoleColor.Yellow : ConsoleColor.DarkYellow; break;
                    case 34: Console.ForegroundColor = ansiBold ? ConsoleColor.Blue : ConsoleColor.DarkBlue; break;
                    case 35: Console.ForegroundColor = ansiBold ? ConsoleColor.Magenta : ConsoleColor.DarkMagenta; break;
                    case 36: Console.ForegroundColor = ansiBold ? ConsoleColor.Cyan : ConsoleColor.DarkCyan; break;
                    case 37: Console.ForegroundColor = ansiBold ? ConsoleColor.White : ConsoleColor.Gray; break;
                    case 39: Console.ForegroundColor = ConsoleColor.Gray; break; // Default foreground

                    // Standard background colors (40-47)
                    case 40: Console.BackgroundColor = ConsoleColor.Black; break;
                    case 41: Console.BackgroundColor = ConsoleColor.DarkRed; break;
                    case 42: Console.BackgroundColor = ConsoleColor.DarkGreen; break;
                    case 43: Console.BackgroundColor = ConsoleColor.DarkYellow; break;
                    case 44: Console.BackgroundColor = ConsoleColor.DarkBlue; break;
                    case 45: Console.BackgroundColor = ConsoleColor.DarkMagenta; break;
                    case 46: Console.BackgroundColor = ConsoleColor.DarkCyan; break;
                    case 47: Console.BackgroundColor = ConsoleColor.Gray; break;
                    case 49: Console.BackgroundColor = ConsoleColor.Black; break; // Default background

                    // Bright foreground colors (90-97)
                    case 90: Console.ForegroundColor = ConsoleColor.DarkGray; break;
                    case 91: Console.ForegroundColor = ConsoleColor.Red; break;
                    case 92: Console.ForegroundColor = ConsoleColor.Green; break;
                    case 93: Console.ForegroundColor = ConsoleColor.Yellow; break;
                    case 94: Console.ForegroundColor = ConsoleColor.Blue; break;
                    case 95: Console.ForegroundColor = ConsoleColor.Magenta; break;
                    case 96: Console.ForegroundColor = ConsoleColor.Cyan; break;
                    case 97: Console.ForegroundColor = ConsoleColor.White; break;

                    // Bright background colors (100-107)
                    case 100: Console.BackgroundColor = ConsoleColor.DarkGray; break;
                    case 101: Console.BackgroundColor = ConsoleColor.Red; break;
                    case 102: Console.BackgroundColor = ConsoleColor.Green; break;
                    case 103: Console.BackgroundColor = ConsoleColor.Yellow; break;
                    case 104: Console.BackgroundColor = ConsoleColor.Blue; break;
                    case 105: Console.BackgroundColor = ConsoleColor.Magenta; break;
                    case 106: Console.BackgroundColor = ConsoleColor.Cyan; break;
                    case 107: Console.BackgroundColor = ConsoleColor.White; break;
                }
            }
        }

        /// <summary>
        /// Convert a dim ConsoleColor to its bright counterpart.
        /// </summary>
        private static ConsoleColor BrightenColor(ConsoleColor color)
        {
            return color switch
            {
                ConsoleColor.Black => ConsoleColor.DarkGray,
                ConsoleColor.DarkRed => ConsoleColor.Red,
                ConsoleColor.DarkGreen => ConsoleColor.Green,
                ConsoleColor.DarkYellow => ConsoleColor.Yellow,
                ConsoleColor.DarkBlue => ConsoleColor.Blue,
                ConsoleColor.DarkMagenta => ConsoleColor.Magenta,
                ConsoleColor.DarkCyan => ConsoleColor.Cyan,
                ConsoleColor.Gray => ConsoleColor.White,
                _ => color // Already bright
            };
        }

        private static int ParseInt(string s, int defaultValue)
        {
            return int.TryParse(s, out int result) ? result : defaultValue;
        }

        /// <summary>
        /// Disconnect from the server and clean up resources.
        /// </summary>
        private void Disconnect()
        {
            cancellationSource?.Cancel();

            try { shellStream?.Close(); } catch { }
            try { shellStream?.Dispose(); } catch { }
            shellStream = null;

            try { sshClient?.Disconnect(); } catch { }
            try { sshClient?.Dispose(); } catch { }
            sshClient = null;

            cancellationSource?.Dispose();
            cancellationSource = null;
        }

        // =====================================================================
        // Credential Storage
        // =====================================================================

        private OnlineCredentials? LoadSavedCredentials()
        {
            try
            {
                var path = GetCredentialsPath();
                if (!File.Exists(path))
                    return null;

                var json = File.ReadAllText(path);
                var creds = JsonSerializer.Deserialize<OnlineCredentials>(json);
                if (creds != null && !string.IsNullOrEmpty(creds.Username))
                    return creds;
            }
            catch (Exception ex)
            {
                DebugLogger.Instance.LogWarning("ONLINE_PLAY", $"Failed to load credentials: {ex.Message}");
            }
            return null;
        }

        private void SaveCredentials(string server, int port, string username, string password)
        {
            try
            {
                var creds = new OnlineCredentials
                {
                    Server = server,
                    Port = port,
                    Username = username,
                    Password = Convert.ToBase64String(Encoding.UTF8.GetBytes(password))
                };

                var json = JsonSerializer.Serialize(creds, new JsonSerializerOptions { WriteIndented = true });
                File.WriteAllText(GetCredentialsPath(), json);
            }
            catch (Exception ex)
            {
                DebugLogger.Instance.LogWarning("ONLINE_PLAY", $"Failed to save credentials: {ex.Message}");
            }
        }

        private void DeleteSavedCredentials()
        {
            try
            {
                var path = GetCredentialsPath();
                if (File.Exists(path))
                    File.Delete(path);
            }
            catch { }
        }

        private string GetCredentialsPath()
        {
            var dir = Path.GetDirectoryName(System.Reflection.Assembly.GetExecutingAssembly().Location) ?? ".";
            return Path.Combine(dir, CREDENTIALS_FILE);
        }

        /// <summary>
        /// Stored credentials with base64-encoded password.
        /// Not truly secure - just obfuscation to prevent casual reading.
        /// </summary>
        private class OnlineCredentials
        {
            public string Server { get; set; } = OFFICIAL_SERVER;
            public int Port { get; set; } = OFFICIAL_PORT;
            public string Username { get; set; } = "";
            public string Password
            {
                get => _password;
                set => _password = value;
            }
            private string _password = "";

            /// <summary>
            /// Get the actual password (decode from base64).
            /// </summary>
            public string GetDecodedPassword()
            {
                try
                {
                    return Encoding.UTF8.GetString(Convert.FromBase64String(_password));
                }
                catch
                {
                    return _password; // Return as-is if not base64
                }
            }
        }
    }
}
