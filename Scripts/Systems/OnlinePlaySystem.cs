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
    /// Client-side system for connecting to an online Usurper Reborn server via SSH.
    /// Handles connection, authentication, credential storage, and I/O piping
    /// between the local terminal and the remote game session.
    /// </summary>
    public class OnlinePlaySystem
    {
        private const string OFFICIAL_SERVER = "play.usurper-reborn.net";
        private const int OFFICIAL_PORT = 4000;
        private const string GATEWAY_USERNAME = "usurper";
        private const string GATEWAY_PASSWORD = "play";
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
        /// Connects directly to the official Usurper Reborn server.
        /// The gateway account handles SSH transport; real auth is in-game.
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
            terminal.WriteLine("  You will create an account or login after connecting.");
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

            await ConnectAndPlay(OFFICIAL_SERVER, OFFICIAL_PORT, GATEWAY_USERNAME, GATEWAY_PASSWORD);
        }

        /// <summary>
        /// Prompt user for server, username, and password.
        /// </summary>
        private async Task<(string server, int port, string username, string password)?> PromptForCredentials()
        {
            terminal.WriteLine("");
            terminal.SetColor("bright_white");
            var serverInput = await terminal.GetInput($"  Server [{OFFICIAL_SERVER}]: ");
            var server = string.IsNullOrWhiteSpace(serverInput) ? OFFICIAL_SERVER : serverInput.Trim();

            int port = OFFICIAL_PORT;
            var portInput = await terminal.GetInput($"  Port [{OFFICIAL_PORT}]: ");
            if (!string.IsNullOrWhiteSpace(portInput) && int.TryParse(portInput.Trim(), out int parsedPort))
                port = parsedPort;

            var username = await terminal.GetInput("  Username: ");
            if (string.IsNullOrWhiteSpace(username))
            {
                terminal.SetColor("red");
                terminal.WriteLine("  Username is required.");
                await terminal.PressAnyKey();
                return null;
            }
            username = username.Trim();

            var password = await terminal.GetInput("  Password: ");
            if (string.IsNullOrWhiteSpace(password))
            {
                terminal.SetColor("red");
                terminal.WriteLine("  Password is required.");
                await terminal.PressAnyKey();
                return null;
            }

            // Offer to save
            var save = await terminal.GetInput("  Save credentials for next time? (Y/N): ");
            if (save.Trim().ToUpper() == "Y")
            {
                SaveCredentials(server, port, username, password);
                terminal.SetColor("green");
                terminal.WriteLine("  Credentials saved.");
            }

            return (server, port, username, password);
        }

        /// <summary>
        /// Connect to the server via SSH and pipe I/O between local terminal and remote game.
        /// </summary>
        private async Task ConnectAndPlay(string server, int port, string username, string password)
        {
            terminal.WriteLine("");
            terminal.SetColor("bright_cyan");
            terminal.WriteLine($"  Connecting to {server}:{port}...");

            try
            {
                // Create SSH connection
                var connectionInfo = new ConnectionInfo(server, port, username,
                    new PasswordAuthenticationMethod(username, password))
                {
                    Timeout = TimeSpan.FromSeconds(15),
                    RetryAttempts = 2
                };

                sshClient = new SshClient(connectionInfo);
                sshClient.Connect();

                terminal.SetColor("bright_green");
                terminal.WriteLine("  Connected! Starting game session...");
                terminal.WriteLine("");

                // Create shell stream (80x24 terminal, like classic BBS)
                shellStream = sshClient.CreateShellStream("usurper", 80, 24, 800, 600, 4096);

                // Pipe I/O between local terminal and remote shell
                cancellationSource = new CancellationTokenSource();
                await PipeIO(cancellationSource.Token);
            }
            catch (Renci.SshNet.Common.SshAuthenticationException)
            {
                terminal.SetColor("bright_red");
                terminal.WriteLine("");
                terminal.WriteLine("  Authentication failed. Check your username and password.");
                terminal.SetColor("gray");
                terminal.WriteLine("  If this is your first time, the server may require registration.");
                await terminal.PressAnyKey();
            }
            catch (Renci.SshNet.Common.SshConnectionException ex)
            {
                terminal.SetColor("bright_red");
                terminal.WriteLine("");
                terminal.WriteLine($"  Connection failed: {ex.Message}");
                terminal.SetColor("gray");
                terminal.WriteLine("  Check that the server is running and reachable.");
                await terminal.PressAnyKey();
            }
            catch (System.Net.Sockets.SocketException ex)
            {
                terminal.SetColor("bright_red");
                terminal.WriteLine("");
                terminal.WriteLine($"  Network error: {ex.Message}");
                terminal.SetColor("gray");
                terminal.WriteLine("  Check your internet connection and the server address.");
                await terminal.PressAnyKey();
            }
            catch (TimeoutException)
            {
                terminal.SetColor("bright_red");
                terminal.WriteLine("");
                terminal.WriteLine("  Connection timed out. The server may be down or unreachable.");
                await terminal.PressAnyKey();
            }
            catch (Exception ex)
            {
                terminal.SetColor("bright_red");
                terminal.WriteLine("");
                terminal.WriteLine($"  Error: {ex.Message}");
                DebugLogger.Instance.LogError("ONLINE_PLAY", $"SSH connection error: {ex}");
                await terminal.PressAnyKey();
            }
            finally
            {
                Disconnect();
            }
        }

        /// <summary>
        /// Pipe input/output between the local console and the remote SSH shell.
        /// The remote game sends ANSI escape codes for colors. We try to enable
        /// native VT processing on Windows, with a fallback ANSI parser that
        /// translates escape codes to Console.ForegroundColor calls.
        /// Runs until the connection is closed or the player disconnects (Ctrl+]).
        /// </summary>
        private async Task PipeIO(CancellationToken ct)
        {
            // Always use our fallback ANSI parser which translates escape codes to
            // Console.ForegroundColor calls. Windows VT processing claims to work
            // (escape chars get consumed) but often fails to actually render colors.
            // Our parser uses Console.ForegroundColor which reliably works everywhere.
            vtProcessingEnabled = false;

            Console.OutputEncoding = Encoding.UTF8;

            terminal.SetColor("gray");
            terminal.WriteLine("  Press Ctrl+] to disconnect.");
            terminal.WriteLine("");

            // Read thread: read from SSH and write to Console.Out
            var readTask = Task.Run(async () =>
            {
                var buffer = new byte[4096];
                try
                {
                    while (!ct.IsCancellationRequested && sshClient!.IsConnected)
                    {
                        if (shellStream!.DataAvailable)
                        {
                            int bytesRead = await shellStream.ReadAsync(buffer, 0, buffer.Length, ct);
                            if (bytesRead > 0)
                            {
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
                        }
                        else
                        {
                            await Task.Delay(20, ct);
                        }
                    }
                }
                catch (OperationCanceledException) { }
                catch (Exception ex)
                {
                    DebugLogger.Instance.LogError("ONLINE_PLAY", $"Read error: {ex.Message}");
                }
            }, ct);

            // Write loop: read keys from Console and send to SSH
            try
            {
                while (!ct.IsCancellationRequested && sshClient!.IsConnected)
                {
                    if (Console.KeyAvailable)
                    {
                        var keyInfo = Console.ReadKey(intercept: true);

                        // Ctrl+] = disconnect (classic telnet escape)
                        if (keyInfo.Key == ConsoleKey.Oem6 && keyInfo.Modifiers.HasFlag(ConsoleModifiers.Control))
                        {
                            break;
                        }

                        // Convert key to string and send to SSH
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

        /// <summary>
        /// Try to enable ANSI/VT100 escape sequence processing on Windows 10+ console.
        /// Returns true if VT processing was confirmed enabled, false if fallback is needed.
        /// </summary>
        private static bool TryEnableVirtualTerminalProcessing()
        {
            if (!System.Runtime.InteropServices.RuntimeInformation.IsOSPlatform(
                System.Runtime.InteropServices.OSPlatform.Windows))
                return true; // Linux/macOS terminals support ANSI natively

            try
            {
                var handle = GetStdHandle(-11); // STD_OUTPUT_HANDLE
                if (handle == IntPtr.Zero || handle == new IntPtr(-1))
                    return false;

                if (!GetConsoleMode(handle, out uint mode))
                    return false;

                mode |= 0x0004; // ENABLE_VIRTUAL_TERMINAL_PROCESSING
                if (!SetConsoleMode(handle, mode))
                    return false;

                // Verify it actually stuck
                if (!GetConsoleMode(handle, out uint verifyMode))
                    return false;

                return (verifyMode & 0x0004) != 0;
            }
            catch
            {
                return false;
            }
        }

        [System.Runtime.InteropServices.DllImport("kernel32.dll", SetLastError = true)]
        private static extern IntPtr GetStdHandle(int nStdHandle);

        [System.Runtime.InteropServices.DllImport("kernel32.dll", SetLastError = true)]
        private static extern bool GetConsoleMode(IntPtr hConsoleHandle, out uint lpMode);

        [System.Runtime.InteropServices.DllImport("kernel32.dll", SetLastError = true)]
        private static extern bool SetConsoleMode(IntPtr hConsoleHandle, uint dwMode);

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
        /// Disconnect from the SSH server and clean up resources.
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
