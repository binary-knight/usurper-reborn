using System;
using System.IO;
using System.Net.Sockets;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Renci.SshNet;

namespace UsurperRemake.Systems
{
    /// <summary>
    /// Client-side system for connecting to an online Usurper Reborn MUD server.
    ///
    /// Two connection modes:
    ///   - BBS door mode: Direct TCP to server:4000 (sslh routes raw TCP to MUD server).
    ///     Sends AUTH header directly on the TCP stream. No SSH overhead or echo issues.
    ///   - Local/Steam mode: SSH to server:4000 as gateway user "usurper"/"play".
    ///     sshd ForceCommand launches relay client which bridges to MUD server.
    /// </summary>
    public class OnlinePlaySystem
    {
        private const string OFFICIAL_SERVER = "play.usurper-reborn.net";
        private const int OFFICIAL_PORT = 4000;
        private const string SSH_GATEWAY_USER = "usurper";
        private const string SSH_GATEWAY_PASS = "play";
        private const string CREDENTIALS_FILE = "online_credentials.json";

        private readonly TerminalEmulator terminal;
        // SSH mode (Local/Steam)
        private SshClient? sshClient;
        private ShellStream? shellStream;
        // TCP mode (BBS)
        private TcpClient? tcpClient;
        private NetworkStream? tcpStream;
        // Shared
        private CancellationTokenSource? cancellationSource;
        private bool vtProcessingEnabled = false;
        private bool useTcpMode = false;

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
            terminal.Write("║");
            terminal.SetColor("bright_white");
            { const string t = "ONLINE PLAY"; int l = (78 - t.Length) / 2, r = 78 - t.Length - l; terminal.Write(new string(' ', l) + t + new string(' ', r)); }
            terminal.SetColor("bright_cyan");
            terminal.WriteLine("║");
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

                // Open a shell stream with PTY echo disabled.
                // We handle all line editing (backspace, echo) locally on the client
                // and only send complete lines. This prevents PTY echo responses from
                // corrupting the client's ANSI parser (which caused garbage output on backspace).
                var terminalModes = new System.Collections.Generic.Dictionary<Renci.SshNet.Common.TerminalModes, uint>
                {
                    { Renci.SshNet.Common.TerminalModes.ECHO, 0 }
                };
                shellStream = sshClient.CreateShellStream("xterm", 80, 24, 800, 600, 4096, terminalModes);

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
        /// Establish a direct TCP connection to the game server (for BBS door mode).
        /// sslh on port 4000 routes non-SSH traffic to the MUD server on port 4001.
        /// </summary>
        private async Task<bool> EstablishTcpConnection(string server, int port)
        {
            terminal.WriteLine("");
            terminal.SetColor("bright_cyan");
            terminal.WriteLine($"  Connecting to {server}:{port} (telnet)...");

            try
            {
                tcpClient = new TcpClient();
                tcpClient.NoDelay = true;
                // sslh needs a moment to detect protocol — connect with timeout
                var connectTask = tcpClient.ConnectAsync(server, port);
                if (await Task.WhenAny(connectTask, Task.Delay(10000)) != connectTask)
                {
                    terminal.SetColor("bright_red");
                    terminal.WriteLine("  Connection timed out.");
                    return false;
                }
                await connectTask; // propagate any exception

                tcpStream = tcpClient.GetStream();
                tcpStream.ReadTimeout = 30000;
                tcpStream.WriteTimeout = 10000;

                terminal.SetColor("bright_green");
                terminal.WriteLine("  Connected!");
                terminal.WriteLine("");
                return true;
            }
            catch (SocketException ex)
            {
                terminal.SetColor("bright_red");
                terminal.WriteLine($"  Connection failed: {ex.Message}");
                terminal.SetColor("gray");
                terminal.WriteLine("  The server may be down or unreachable.");
                return false;
            }
            catch (Exception ex)
            {
                terminal.SetColor("bright_red");
                terminal.WriteLine($"  Error: {ex.Message}");
                DebugLogger.Instance.LogError("ONLINE_PLAY", $"TCP connection error: {ex}");
                return false;
            }
        }

        /// <summary>
        /// Write a string to the active connection (SSH shell stream or TCP stream).
        /// </summary>
        private void WriteToServer(string text)
        {
            if (useTcpMode && tcpStream != null)
            {
                var bytes = Encoding.UTF8.GetBytes(text);
                tcpStream.Write(bytes, 0, bytes.Length);
                tcpStream.Flush();
            }
            else if (shellStream != null)
            {
                shellStream.Write(text);
                shellStream.Flush();
            }
        }

        /// <summary>
        /// Read a line response from the active connection (AUTH response).
        /// Waits up to timeout for OK or ERR: response.
        /// </summary>
        private async Task<string?> ReadAuthResponse()
        {
            if (useTcpMode)
                return await ReadLineFromTcp();
            else
                return await ReadLineFromShell();
        }

        /// <summary>
        /// Read an AUTH response from the TCP stream.
        /// Uses blocking ReadAsync with cancellation instead of polling DataAvailable,
        /// because the server may send ERR: and close the connection simultaneously.
        /// </summary>
        private async Task<string?> ReadLineFromTcp()
        {
            try
            {
                var result = new StringBuilder();
                var buffer = new byte[4096];
                using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(5));

                while (!cts.Token.IsCancellationRequested)
                {
                    int read = await tcpStream!.ReadAsync(buffer, 0, buffer.Length, cts.Token);
                    if (read == 0)
                    {
                        // Connection closed — return whatever we have
                        break;
                    }

                    result.Append(Encoding.UTF8.GetString(buffer, 0, read));

                    // Check for auth response markers
                    var text = result.ToString();
                    var lines = text.Split(new[] { '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries);
                    foreach (var line in lines)
                    {
                        var trimmed = line.Trim();
                        if (trimmed == "OK" || trimmed.StartsWith("ERR:"))
                            return text;
                    }
                }

                return result.Length > 0 ? result.ToString() : null;
            }
            catch (OperationCanceledException)
            {
                return null;
            }
            catch
            {
                return null;
            }
        }

        /// <summary>
        /// Check if the connection is still alive.
        /// </summary>
        private bool IsConnected()
        {
            if (useTcpMode)
                return tcpClient?.Connected == true;
            else
                return sshClient?.IsConnected == true;
        }

        /// <summary>
        /// Connect to the MUD server, authenticate, and pipe I/O.
        /// BBS mode uses direct TCP; Local/Steam uses SSH tunnel.
        /// </summary>
        private async Task ConnectAndPlay(string server, int port)
        {
            // Detect telnet BBS: socket mode (raw handle) means the BBS user connected via telnet.
            // In that case, use direct TCP to MUD server on port 4001 — SSH relay output goes
            // through SocketTerminal which strips ANSI colors, but raw TCP bytes are clean.
            // SSH BBS (stdio mode) and Local/Steam use SSH tunnel on port 4000.
            bool isTelnetBBS = UsurperRemake.BBS.DoorMode.IsInDoorMode
                && UsurperRemake.BBS.BBSTerminalAdapter.Instance?.GetRawOutputStream() != null;
            useTcpMode = isTelnetBBS;

            bool connected;
            if (useTcpMode)
            {
                // Direct TCP to MUD server on port 4001 (bypasses sslh on 4000)
                connected = await EstablishTcpConnection(server, 4001);
            }
            else
            {
                connected = EstablishSSHConnection(server, port);
            }

            if (!connected)
            {
                await terminal.PressAnyKey();
                return;
            }

            if (!useTcpMode)
            {
                // SSH mode: drain relay banner before sending AUTH
                await Task.Delay(500);
                DrainShellStream();
            }

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
                    WriteToServer(authLine);

                    var response = await ReadAuthResponse();
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

                        // Reconnect for next attempt
                        Disconnect();
                        bool reconnected = useTcpMode
                            ? await EstablishTcpConnection(server, 4001)
                            : EstablishSSHConnection(server, port);
                        if (!reconnected)
                        {
                            await terminal.PressAnyKey();
                            return;
                        }
                        if (!useTcpMode)
                        {
                            await Task.Delay(500);
                            DrainShellStream();
                        }
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
                    password = (await terminal.GetMaskedInput()).Trim();
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
                    password = (await terminal.GetMaskedInput()).Trim();
                    if (string.IsNullOrEmpty(password)) continue;

                    if (password.Length < 4)
                    {
                        terminal.SetColor("bright_red");
                        terminal.WriteLine("  Password must be at least 4 characters.");
                        terminal.WriteLine("");
                        continue;
                    }

                    terminal.Write("  Confirm password: ", "bright_green");
                    var confirm = (await terminal.GetMaskedInput()).Trim();
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

                // Send AUTH header to the server
                string connType = GetConnectionType();
                string authHeader;
                if (isRegistration)
                    authHeader = $"AUTH:{username}:{password}:REGISTER:{connType}\n";
                else
                    authHeader = $"AUTH:{username}:{password}:{connType}\n";

                try
                {
                    WriteToServer(authHeader);
                }
                catch
                {
                    terminal.SetColor("bright_red");
                    terminal.WriteLine("  Lost connection to server.");
                    await terminal.PressAnyKey();
                    Disconnect();
                    return;
                }

                // Read AUTH response
                var authResponse = await ReadAuthResponse();
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

                    // Reconnect for next attempt
                    Disconnect();
                    bool reconnected = useTcpMode
                        ? await EstablishTcpConnection(server, 4001)
                        : EstablishSSHConnection(server, port);
                    if (!reconnected)
                    {
                        await terminal.PressAnyKey();
                        return;
                    }
                    if (!useTcpMode)
                    {
                        await Task.Delay(500);
                        DrainShellStream();
                    }
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
            if (UsurperRemake.BBS.DoorMode.IsInDoorMode) return "BBS";
#if STEAM_BUILD
            return "Steam";
#else
            return "Local";
#endif
        }

        /// <summary>
        /// Pipe input/output between the local terminal and the remote game server.
        /// TCP mode: reads/writes directly on NetworkStream.
        /// SSH mode: reads/writes on ShellStream (SSH tunnel).
        /// </summary>
        private async Task PipeIO(CancellationToken ct)
        {
            vtProcessingEnabled = false;

            if (!UsurperRemake.BBS.DoorMode.IsInDoorMode)
                Console.OutputEncoding = Encoding.UTF8;

            // For telnet BBS: get the raw BBS socket stream for direct byte relay.
            // This bypasses all terminal/encoding processing, preserving ANSI colors.
            Stream? bbsRawStream = UsurperRemake.BBS.BBSTerminalAdapter.Instance?.GetRawOutputStream();

            if (!useTcpMode)
            {
                terminal.SetColor("gray");
                terminal.WriteLine("  Press Ctrl+] to disconnect.");
                terminal.WriteLine("");
            }

            // Flag set when server disconnect is detected by any path.
            // Checked by input loops to break out without waiting for user input.
            // Wrapped in array for capture by lambdas (local bools can't be volatile).
            var serverDead = new bool[] { false };

            // Read task: server → local terminal
            var readTask = Task.Run(async () =>
            {
                var buffer = new byte[4096];
                try
                {
                    while (!ct.IsCancellationRequested)
                    {
                        int bytesRead = 0;

                        if (useTcpMode)
                        {
                            // ReadAsync blocks until data arrives or connection closes.
                            // Returns 0 immediately on FIN — no stale IsConnected() polling.
                            bytesRead = await tcpStream!.ReadAsync(buffer, 0, buffer.Length, ct);
                        }
                        else
                        {
                            // SSH ShellStream: blocking read detects channel close instantly
                            // (returns 0 or throws) instead of polling DataAvailable with
                            // stale IsConnected() which can take seconds to update.
                            // Cancellation handled by closing the stream from PipeIO cleanup.
                            bytesRead = shellStream!.Read(buffer, 0, buffer.Length);
                        }

                        if (bytesRead == 0) break; // Server closed connection

                        if (bbsRawStream != null)
                        {
                            // Telnet BBS: write raw bytes directly to BBS socket.
                            // Bypasses all terminal/encoding processing — ANSI colors preserved.
                            bbsRawStream.Write(buffer, 0, bytesRead);
                            bbsRawStream.Flush();
                        }
                        else
                        {
                            var text = Encoding.UTF8.GetString(buffer, 0, bytesRead);
                            if (UsurperRemake.BBS.DoorMode.IsInDoorMode)
                            {
                                terminal.WriteRawAnsi(text);
                            }
                            else if (vtProcessingEnabled)
                            {
                                Console.Write(text);
                            }
                            else
                            {
                                WriteAnsiToConsole(text);
                            }
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
                finally
                {
                    serverDead[0] = true;
                }
            }, ct);

            // Watchdog task: detect stale SSH disconnects that shellStream.Read() misses.
            // sshClient.IsConnected can lag seconds behind actual close, but this catches
            // cases where Read() blocks indefinitely after the channel closes.
            var watchdog = Task.Run(async () =>
            {
                try
                {
                    while (!ct.IsCancellationRequested && !serverDead[0])
                    {
                        await Task.Delay(500, ct);
                        if (useTcpMode)
                        {
                            if (tcpClient != null && !tcpClient.Connected) break;
                        }
                        else
                        {
                            if (sshClient != null && !sshClient.IsConnected) break;
                        }
                    }
                }
                catch (OperationCanceledException) { }
                serverDead[0] = true;
            }, ct);

            // Combined disconnect signal: fires when either read task or watchdog detects server gone.
            var disconnectSignal = Task.WhenAny(readTask, watchdog);

            try
            {
                if (UsurperRemake.BBS.DoorMode.IsInDoorMode)
                {
                    // BBS mode: terminal.GetInput() blocks until Enter with no cancellation.
                    // Race each GetInput call against the disconnect signal so we break
                    // instantly when the server dies instead of waiting for the user to press Enter.
                    while (!ct.IsCancellationRequested && !serverDead[0])
                    {
                        var inputTask = terminal.GetInput("");
                        // Wait for either: user presses Enter, or server disconnects
                        await Task.WhenAny(inputTask, disconnectSignal);

                        if (serverDead[0] || readTask.IsCompleted || watchdog.IsCompleted) break;

                        // inputTask completed — send the line to the server
                        var line = await inputTask;
                        if (line == null) continue;
                        try
                        {
                            WriteToServer(line + "\n");
                        }
                        catch
                        {
                            break; // Server dead — stop immediately
                        }
                    }
                }
                else
                {
                    // Local/Steam mode: poll Console.KeyAvailable for non-blocking char-by-char input.
                    // Ctrl+] disconnects (classic telnet escape).
                    var inputBuffer = new StringBuilder();
                    while (!ct.IsCancellationRequested)
                    {
                        if (readTask.IsCompleted || serverDead[0])
                            break;

                        if (Console.KeyAvailable)
                        {
                            var keyInfo = Console.ReadKey(intercept: true);

                            // Ctrl+] = disconnect (classic telnet escape)
                            if (keyInfo.Key == ConsoleKey.Oem6 && keyInfo.Modifiers.HasFlag(ConsoleModifiers.Control))
                            {
                                break;
                            }

                            if (keyInfo.Key == ConsoleKey.Enter)
                            {
                                try
                                {
                                    WriteToServer(inputBuffer.ToString() + "\n");
                                }
                                catch
                                {
                                    break; // Server dead — stop immediately
                                }
                                Console.Write("\r\n"); // Local newline echo
                                inputBuffer.Clear();
                            }
                            else if (keyInfo.Key == ConsoleKey.Backspace)
                            {
                                if (inputBuffer.Length > 0)
                                {
                                    inputBuffer.Remove(inputBuffer.Length - 1, 1);
                                    Console.Write("\b \b");
                                }
                            }
                            else if (keyInfo.KeyChar >= ' ' && keyInfo.KeyChar != '\x7f')
                            {
                                inputBuffer.Append(keyInfo.KeyChar);
                                Console.Write(keyInfo.KeyChar);
                            }
                            // Non-printable keys (arrows, escape, tab) are ignored
                        }
                        else
                        {
                            await Task.Delay(10, ct);
                        }
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

            // Wait for read task and watchdog to finish.
            // Close streams first to unblock any blocking Read/ReadAsync in the read task.
            serverDead[0] = true;
            cancellationSource?.Cancel();
            try { shellStream?.Close(); } catch { }
            try { tcpStream?.Close(); } catch { }
            try { await readTask; } catch { }
            try { await watchdog; } catch { }

            terminal.WriteLine("");
            terminal.SetColor("yellow");
            terminal.WriteLine("  Disconnected from server.");
            await Task.Delay(1500);
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

            // TCP mode cleanup
            try { tcpStream?.Close(); } catch { }
            try { tcpStream?.Dispose(); } catch { }
            tcpStream = null;

            try { tcpClient?.Close(); } catch { }
            try { tcpClient?.Dispose(); } catch { }
            tcpClient = null;

            // SSH mode cleanup
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
            var dir = AppContext.BaseDirectory;
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
