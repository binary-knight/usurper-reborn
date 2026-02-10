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
        /// Uses Console directly (bypassing TerminalEmulator) since the remote game
        /// sends ANSI escape codes that Windows 10+ console handles natively.
        /// Runs until the connection is closed or the player disconnects (Ctrl+]).
        /// </summary>
        private async Task PipeIO(CancellationToken ct)
        {
            // Enable ANSI/VT processing on Windows console
            EnableVirtualTerminalProcessing();

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
                                Console.Write(text);
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
        /// Enable ANSI/VT100 escape sequence processing on Windows 10+ console.
        /// This allows the remote game's ANSI color codes to render correctly.
        /// </summary>
        private static void EnableVirtualTerminalProcessing()
        {
            if (!System.Runtime.InteropServices.RuntimeInformation.IsOSPlatform(
                System.Runtime.InteropServices.OSPlatform.Windows))
                return;

            try
            {
                var handle = GetStdHandle(-11); // STD_OUTPUT_HANDLE
                GetConsoleMode(handle, out uint mode);
                mode |= 0x0004; // ENABLE_VIRTUAL_TERMINAL_PROCESSING
                SetConsoleMode(handle, mode);
            }
            catch { }
        }

        [System.Runtime.InteropServices.DllImport("kernel32.dll")]
        private static extern IntPtr GetStdHandle(int nStdHandle);

        [System.Runtime.InteropServices.DllImport("kernel32.dll")]
        private static extern bool GetConsoleMode(IntPtr hConsoleHandle, out uint lpMode);

        [System.Runtime.InteropServices.DllImport("kernel32.dll")]
        private static extern bool SetConsoleMode(IntPtr hConsoleHandle, uint dwMode);

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
