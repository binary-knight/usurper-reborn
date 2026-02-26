using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Net.Sockets;
using System.Threading;
using System.Threading.Tasks;
using UsurperRemake.BBS;
using UsurperRemake.Systems;

namespace UsurperRemake.Server;

/// <summary>
/// TCP game server for MUD mode. Listens on a configurable port (default 4000),
/// accepts connections, authenticates via a simple text protocol, and spawns
/// a PlayerSession per connection (each running as an isolated async Task
/// with its own SessionContext).
///
/// Protocol:
///   Client sends: AUTH:username:connectionType\n
///   Server responds: OK\n  (or ERR:reason\n)
///   After AUTH, all I/O is the standard game terminal stream.
/// </summary>
public class MudServer
{
    private readonly int _port;
    private readonly string _databasePath;
    private TcpListener? _listener;
    private CancellationTokenSource? _cts;

    /// <summary>Idle timeout: disconnect players with no input for this long.</summary>
    public static readonly TimeSpan IdleTimeout = TimeSpan.FromMinutes(15);

    /// <summary>Persistent broadcast banner shown to all players on every screen refresh.
    /// Set by /broadcast wizard command or sysop console. Null = no active broadcast.</summary>
    public static volatile string? ActiveBroadcast;

    /// <summary>Usernames to bootstrap as God-level wizards on startup (from --admin flag).</summary>
    public HashSet<string> BootstrapAdminUsers { get; } = new(StringComparer.OrdinalIgnoreCase);

    /// <summary>All currently active player sessions, keyed by lowercase username.</summary>
    public ConcurrentDictionary<string, PlayerSession> ActiveSessions { get; } = new();

    /// <summary>Singleton for easy access from game code (e.g. chat broadcasts).</summary>
    private static MudServer? _instance;
    public static MudServer? Instance => _instance;

    /// <summary>Pending server shutdown countdown (null = not shutting down).</summary>
    public int? ShutdownCountdownSeconds { get; set; }

    public MudServer(int port, string databasePath)
    {
        _port = port;
        _databasePath = databasePath;
        _instance = this;
    }

    /// <summary>
    /// Start the MUD server. Blocks until cancellation.
    /// </summary>
    public async Task RunAsync(CancellationToken cancellationToken)
    {
        _cts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);

        // Initialize the shared SQLite backend
        var sqlBackend = new SqlSaveBackend(_databasePath);
        SaveSystem.InitializeWithBackend(sqlBackend);
        Console.Error.WriteLine($"[MUD] SQLite backend initialized: {_databasePath}");

        // Bootstrap --admin users as God-level wizards in the database
        foreach (var adminUser in BootstrapAdminUsers)
        {
            var currentLevel = await sqlBackend.GetWizardLevel(adminUser);
            if (currentLevel < WizardLevel.God)
            {
                await sqlBackend.SetWizardLevel(adminUser, WizardLevel.God);
                Console.Error.WriteLine($"[MUD] Bootstrapped '{adminUser}' to God wizard level");
            }
        }

        // Initialize the room registry for player presence tracking
        var roomRegistry = new RoomRegistry();
        Console.Error.WriteLine("[MUD] Room registry initialized");

        // Initialize the group system for cooperative dungeon play
        var groupSystem = new GroupSystem();
        Console.Error.WriteLine("[MUD] Group system initialized");

        // Start the world simulator as an in-process background task
        // This replaces the separate usurper-world.service process
        var worldSimService = new WorldSimService(
            sqlBackend,
            simIntervalSeconds: UsurperRemake.BBS.DoorMode.SimIntervalSeconds,
            npcXpMultiplier: UsurperRemake.BBS.DoorMode.NpcXpMultiplier,
            saveIntervalMinutes: UsurperRemake.BBS.DoorMode.SaveIntervalMinutes
        );
        var worldSimTask = Task.Run(() => worldSimService.RunAsync(_cts.Token));
        Console.Error.WriteLine("[MUD] World simulator started as background task");

        // Start idle timeout watchdog (checks every 60 seconds for idle players)
        var idleWatchdogTask = Task.Run(() => IdleWatchdogAsync(_cts.Token));
        Console.Error.WriteLine($"[MUD] Idle timeout watchdog started ({IdleTimeout.TotalMinutes} min)");

        // Start listening
        _listener = new TcpListener(IPAddress.Any, _port);
        _listener.Start();
        Console.Error.WriteLine($"[MUD] Game server listening on 0.0.0.0:{_port}");

        try
        {
            while (!_cts.Token.IsCancellationRequested)
            {
                TcpClient client;
                try
                {
                    client = await _listener.AcceptTcpClientAsync(_cts.Token);
                }
                catch (OperationCanceledException)
                {
                    break;
                }

                // Handle each connection in a fire-and-forget task
                _ = HandleConnectionAsync(client, sqlBackend, _cts.Token);
            }
        }
        finally
        {
            _listener.Stop();
            Console.Error.WriteLine("[MUD] Server stopped.");

            // Gracefully disconnect all sessions
            var disconnectTasks = ActiveSessions.Values.Select(s => s.DisconnectAsync("Server shutting down")).ToArray();
            await Task.WhenAll(disconnectTasks);

            // Wait for world simulator to finish its final save
            Console.Error.WriteLine("[MUD] Waiting for world simulator to shut down...");
            try { await worldSimTask; } catch (OperationCanceledException) { }
            Console.Error.WriteLine("[MUD] World simulator shut down.");
        }
    }

    /// <summary>
    /// Handle a single incoming TCP connection.
    /// Supports two modes:
    ///   1. Protocol mode: first line is AUTH:... header (used by game client and relay)
    ///   2. Interactive mode: no AUTH header → show login/register menu over TCP (used by web terminal, raw telnet)
    /// </summary>
    private async Task HandleConnectionAsync(TcpClient client, SqlSaveBackend sqlBackend, CancellationToken ct)
    {
        string? username = null;
        try
        {
            client.NoDelay = true;
            var stream = client.GetStream();

            // Try to read the AUTH header line (timeout after 3 seconds)
            // If we get an AUTH: header, use protocol mode.
            // If we get anything else or timeout, switch to interactive mode.
            string? authLine = null;
            bool isInteractive = false;
            byte[]? firstBytes = null;

            try
            {
                using var authCts = CancellationTokenSource.CreateLinkedTokenSource(ct);
                authCts.CancelAfter(TimeSpan.FromMilliseconds(500));
                authLine = await ReadLineAsync(stream, authCts.Token);
            }
            catch (OperationCanceledException) when (!ct.IsCancellationRequested)
            {
                // Timeout — no AUTH header received, switch to interactive mode
                isInteractive = true;
            }

            if (authLine != null && !authLine.StartsWith("AUTH:"))
            {
                // Got a line but it's not AUTH — treat as interactive
                isInteractive = true;
                firstBytes = System.Text.Encoding.UTF8.GetBytes(authLine);
            }

            string connectionType;
            bool isPlainText = false;

            if (isInteractive)
            {
                // Telnet negotiation: tell MUD clients the server will handle echo,
                // suppressing their local echo. Without this, Mudlet (and other MUD
                // clients) echo keystrokes locally AND the server echoes them back via
                // ReadLineInteractiveAsync, causing every character to appear twice.
                //   IAC WILL ECHO  (0xFF 0xFB 0x01) — server will echo, disable client echo
                //   IAC WILL SGA   (0xFF 0xFB 0x03) — suppress go-ahead (character mode)
                await stream.WriteAsync(new byte[] { 0xFF, 0xFB, 0x01, 0xFF, 0xFB, 0x03 }, 0, 6, ct);
                await stream.FlushAsync(ct);

                // Probe terminal type to detect screen-reader clients (e.g. VIP Mud)
                isPlainText = await ProbeTtypeAsync(stream, ct);
                if (isPlainText)
                    Console.Error.WriteLine($"[MUD] Screen-reader client detected from {client.Client.RemoteEndPoint} — plain text mode");

                // Interactive mode: present login/register menu directly over TCP
                Console.Error.WriteLine($"[MUD] Interactive connection from {client.Client.RemoteEndPoint}");
                var result = await InteractiveAuthAsync(stream, sqlBackend, ct, isPlainText);
                if (result == null)
                {
                    client.Close();
                    return;
                }
                username = result.Value.username;
                connectionType = result.Value.connectionType;
            }
            else
            {
                // Protocol mode: parse AUTH header
                var parts = authLine!.Split(':', 5);
                if (parts.Length < 3 || string.IsNullOrWhiteSpace(parts[1]))
                {
                    await WriteLineAsync(stream, "ERR:Invalid auth format. Expected AUTH:username:connectionType");
                    client.Close();
                    return;
                }

                string? password = null;
                bool isRegistration = false;

                if (parts.Length == 5 && parts[3].Trim().Equals("REGISTER", StringComparison.OrdinalIgnoreCase))
                {
                    username = parts[1].Trim();
                    password = parts[2];
                    connectionType = parts[4].Trim();
                    isRegistration = true;
                }
                else if (parts.Length >= 4)
                {
                    username = parts[1].Trim();
                    password = parts[2];
                    connectionType = parts[3].Trim();
                }
                else
                {
                    username = parts[1].Trim();
                    connectionType = parts[2].Trim();
                }

                var usernameKey = username.ToLowerInvariant();
                Console.Error.WriteLine($"[MUD] Connection from {client.Client.RemoteEndPoint}: user={username}, type={connectionType}, auth={( password != null ? (isRegistration ? "register" : "password") : "trusted" )}");

                // Handle registration
                if (isRegistration && password != null)
                {
                    var (regSuccess, regMessage) = await sqlBackend.RegisterPlayer(username, password);
                    if (!regSuccess)
                    {
                        Console.Error.WriteLine($"[MUD] Registration failed for '{username}': {regMessage}");
                        await WriteLineAsync(stream, $"ERR:{regMessage}");
                        client.Close();
                        return;
                    }
                    Console.Error.WriteLine($"[MUD] New player registered: '{username}'");
                }

                // If password was provided, verify it against the database
                if (password != null)
                {
                    var (success, displayName, message) = await sqlBackend.AuthenticatePlayer(username, password);
                    if (!success)
                    {
                        Console.Error.WriteLine($"[MUD] Auth failed for '{username}': {message}");
                        await WriteLineAsync(stream, $"ERR:{message}");
                        client.Close();
                        return;
                    }
                    // Do NOT replace username with displayName — displayName is a cosmetic
                    // value from save data that can be corrupted (e.g., by alt character bugs).
                    // The session identity must always be the login key (account name).
                }

                // Normalize to lowercase for consistent key usage
                username = usernameKey;

                // Kick existing session if duplicate (reconnect takes priority)
                if (ActiveSessions.TryGetValue(usernameKey, out var existingSession))
                {
                    Console.Error.WriteLine($"[MUD] Kicking stale session for '{username}' (reconnect)");
                    await existingSession.DisconnectAsync("Disconnected: logged in from another session");
                    ActiveSessions.TryRemove(usernameKey, out _);
                    await Task.Delay(500); // Brief delay for cleanup
                }

                // Send OK to signal auth success
                await WriteLineAsync(stream, "OK");
            }

            // Create and start the player session
            var sessionUsernameKey = username.ToLowerInvariant();

            // Create and start the player session
            {
                var session = new PlayerSession(
                    username: username,
                    connectionType: connectionType,
                    tcpClient: client,
                    stream: stream,
                    sqlBackend: sqlBackend,
                    server: this,
                    cancellationToken: ct,
                    isPlainText: isPlainText
                );

                // If TryAdd fails (race condition), kick stale session and retry
                if (!ActiveSessions.TryAdd(sessionUsernameKey, session))
                {
                    if (ActiveSessions.TryGetValue(sessionUsernameKey, out var staleSession))
                    {
                        Console.Error.WriteLine($"[MUD] Kicking stale session for '{username}' (race condition)");
                        await staleSession.DisconnectAsync("Disconnected: logged in from another session");
                        ActiveSessions.TryRemove(sessionUsernameKey, out _);
                        await Task.Delay(500);
                    }
                    if (!ActiveSessions.TryAdd(sessionUsernameKey, session))
                    {
                        await WriteAnsiAsync(stream, "\r\n\u001b[1;31m  Could not start session. Try again.\u001b[0m\r\n");
                        client.Close();
                        return;
                    }
                }

                Console.Error.WriteLine($"[MUD] Session started for '{username}' ({connectionType}). Active sessions: {ActiveSessions.Count}");
                await session.RunAsync();
            }
        }
        catch (OperationCanceledException)
        {
            // Normal shutdown
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine($"[MUD] Connection error for '{username ?? "unknown"}': {ex.Message}");
        }
        finally
        {
            // Clean up session
            if (username != null)
            {
                var usernameKey = username.ToLowerInvariant();
                ActiveSessions.TryRemove(usernameKey, out _);
                Console.Error.WriteLine($"[MUD] Session ended for '{username}'. Active sessions: {ActiveSessions.Count}");
            }

            try { client.Close(); } catch { }
        }
    }

    /// <summary>
    /// Interactive authentication over TCP. Shows a login/register menu and
    /// collects credentials directly from the terminal. Used by web terminal
    /// and raw telnet connections that don't send an AUTH header.
    /// </summary>
    private async Task<(string username, string connectionType)?> InteractiveAuthAsync(
        NetworkStream stream, SqlSaveBackend sqlBackend, CancellationToken ct, bool isPlainText = false)
    {
        const int MAX_ATTEMPTS = 5;

        for (int attempt = 0; attempt < MAX_ATTEMPTS; attempt++)
        {
            if (ct.IsCancellationRequested) return null;

            // Show auth menu
            if (isPlainText)
            {
                await WriteAnsiAsync(stream, "\r\n=== Usurper Reborn Online ===\r\n");
                await WriteAnsiAsync(stream, "[L] Login\r\n");
                await WriteAnsiAsync(stream, "[R] Register\r\n");
                await WriteAnsiAsync(stream, "[Q] Quit\r\n");
                await WriteAnsiAsync(stream, "Choice: ");
            }
            else
            {
                await WriteAnsiAsync(stream, "\u001b[2J\u001b[H"); // Clear screen
                await WriteAnsiAsync(stream, "\u001b[1;36m");
                await WriteAnsiAsync(stream, "╔══════════════════════════════════════════════════════════════════════════════╗\r\n");
                await WriteAnsiAsync(stream, "\u001b[1;37m");
                await WriteAnsiAsync(stream, "║                    Welcome to Usurper Reborn Online                        ║\r\n");
                await WriteAnsiAsync(stream, "\u001b[1;36m");
                await WriteAnsiAsync(stream, "╠══════════════════════════════════════════════════════════════════════════════╣\r\n");
                await WriteAnsiAsync(stream, "\u001b[0;37m");
                await WriteAnsiAsync(stream, "║                                                                            ║\r\n");
                await WriteAnsiAsync(stream, "║  \u001b[1;36m[L]\u001b[0;37m Login to existing account                                           ║\r\n");
                await WriteAnsiAsync(stream, "║  \u001b[1;32m[R]\u001b[0;37m Register new account                                                ║\r\n");
                await WriteAnsiAsync(stream, "║  \u001b[1;31m[Q]\u001b[0;37m Quit                                                                ║\r\n");
                await WriteAnsiAsync(stream, "║                                                                            ║\r\n");
                await WriteAnsiAsync(stream, "\u001b[1;36m");
                await WriteAnsiAsync(stream, "╚══════════════════════════════════════════════════════════════════════════════╝\r\n");
                await WriteAnsiAsync(stream, "\u001b[0m");
                await WriteAnsiAsync(stream, "\r\n  Choice: ");
            }

            var choice = (await ReadLineAsync(stream, ct))?.Trim().ToUpperInvariant();
            if (string.IsNullOrEmpty(choice)) continue;
            if (choice == "Q") return null;

            string? username = null;
            string? password = null;
            bool isRegistration = false;

            if (choice == "L")
            {
                if (isPlainText)
                {
                    await WriteAnsiAsync(stream, "Username: ");
                    username = (await ReadLineAsync(stream, ct))?.Trim();
                    if (string.IsNullOrEmpty(username)) continue;
                    await WriteAnsiAsync(stream, "Password: ");
                    password = (await ReadLineAsync(stream, ct))?.Trim();
                    if (string.IsNullOrEmpty(password)) continue;
                }
                else
                {
                    await WriteAnsiAsync(stream, "\r\n\u001b[1;37m  Username: \u001b[0m");
                    username = (await ReadLineAsync(stream, ct))?.Trim();
                    if (string.IsNullOrEmpty(username)) continue;
                    await WriteAnsiAsync(stream, "\u001b[1;37m  Password: \u001b[0m");
                    password = (await ReadLineAsync(stream, ct))?.Trim();
                    if (string.IsNullOrEmpty(password)) continue;
                }
            }
            else if (choice == "R")
            {
                if (isPlainText)
                {
                    await WriteAnsiAsync(stream, "Choose a username: ");
                    username = (await ReadLineAsync(stream, ct))?.Trim();
                    if (string.IsNullOrEmpty(username)) continue;
                    if (username.Length < 2 || username.Length > 20)
                    {
                        await WriteAnsiAsync(stream, "Username must be 2-20 characters.\r\n\r\n");
                        continue;
                    }
                    await WriteAnsiAsync(stream, "Choose a password: ");
                    password = (await ReadLineAsync(stream, ct))?.Trim();
                    if (string.IsNullOrEmpty(password)) continue;
                    if (password.Length < 4)
                    {
                        await WriteAnsiAsync(stream, "Password must be at least 4 characters.\r\n\r\n");
                        continue;
                    }
                    await WriteAnsiAsync(stream, "Confirm password: ");
                    var confirm = (await ReadLineAsync(stream, ct))?.Trim();
                    if (password != confirm)
                    {
                        await WriteAnsiAsync(stream, "Passwords do not match.\r\n\r\n");
                        continue;
                    }
                }
                else
                {
                    await WriteAnsiAsync(stream, "\r\n\u001b[1;32m  Choose a username: \u001b[0m");
                    username = (await ReadLineAsync(stream, ct))?.Trim();
                    if (string.IsNullOrEmpty(username)) continue;
                    if (username.Length < 2 || username.Length > 20)
                    {
                        await WriteAnsiAsync(stream, "\r\n\u001b[1;31m  Username must be 2-20 characters.\u001b[0m\r\n\r\n");
                        continue;
                    }
                    await WriteAnsiAsync(stream, "\u001b[1;32m  Choose a password: \u001b[0m");
                    password = (await ReadLineAsync(stream, ct))?.Trim();
                    if (string.IsNullOrEmpty(password)) continue;
                    if (password.Length < 4)
                    {
                        await WriteAnsiAsync(stream, "\r\n\u001b[1;31m  Password must be at least 4 characters.\u001b[0m\r\n\r\n");
                        continue;
                    }
                    await WriteAnsiAsync(stream, "\u001b[1;32m  Confirm password: \u001b[0m");
                    var confirm = (await ReadLineAsync(stream, ct))?.Trim();
                    if (password != confirm)
                    {
                        await WriteAnsiAsync(stream, "\r\n\u001b[1;31m  Passwords do not match.\u001b[0m\r\n\r\n");
                        continue;
                    }
                }
                isRegistration = true;
            }
            else
            {
                continue;
            }

            // Process registration
            if (isRegistration)
            {
                var (regSuccess, regMessage) = await sqlBackend.RegisterPlayer(username!, password!);
                if (!regSuccess)
                {
                    Console.Error.WriteLine($"[MUD] Registration failed for '{username}': {regMessage}");
                    if (isPlainText)
                        await WriteAnsiAsync(stream, $"Error: {regMessage}\r\n\r\n");
                    else
                        await WriteAnsiAsync(stream, $"\r\n\u001b[1;31m  {regMessage}\u001b[0m\r\n\r\n");
                    continue;
                }
                Console.Error.WriteLine($"[MUD] New player registered: '{username}'");
            }

            // Authenticate
            var (success, displayName, message) = await sqlBackend.AuthenticatePlayer(username!, password!);
            if (!success)
            {
                Console.Error.WriteLine($"[MUD] Auth failed for '{username}': {message}");
                if (isPlainText)
                    await WriteAnsiAsync(stream, $"Error: {message}\r\n\r\n");
                else
                    await WriteAnsiAsync(stream, $"\r\n\u001b[1;31m  {message}\u001b[0m\r\n\r\n");
                continue;
            }

            if (!string.IsNullOrEmpty(displayName))
                username = displayName;

            // Kick existing session if duplicate (reconnect takes priority)
            var interactiveKey = username!.ToLowerInvariant();
            if (ActiveSessions.TryGetValue(interactiveKey, out var existingInteractive))
            {
                Console.Error.WriteLine($"[MUD] Kicking stale session for '{username}' (reconnect)");
                await existingInteractive.DisconnectAsync("Disconnected: logged in from another session");
                ActiveSessions.TryRemove(interactiveKey, out _);
                await Task.Delay(500);
                if (isPlainText)
                    await WriteAnsiAsync(stream, "Previous session disconnected.\r\n");
                else
                    await WriteAnsiAsync(stream, "\r\n\u001b[1;33m  Previous session disconnected.\u001b[0m\r\n");
            }

            if (isPlainText)
                await WriteAnsiAsync(stream, $"Welcome, {username}!\r\n\r\n");
            else
                await WriteAnsiAsync(stream, $"\r\n\u001b[1;32m  Welcome, {username}!\u001b[0m\r\n\r\n");
            Console.Error.WriteLine($"[MUD] Interactive auth succeeded for '{username}'");
            return (username!, "MUD");
        }

        if (isPlainText)
            await WriteAnsiAsync(stream, "Too many attempts. Goodbye.\r\n");
        else
            await WriteAnsiAsync(stream, "\r\n\u001b[1;31m  Too many attempts. Goodbye.\u001b[0m\r\n");
        return null;
    }

    /// <summary>Write ANSI text to a network stream.</summary>
    private static async Task WriteAnsiAsync(NetworkStream stream, string text)
    {
        var bytes = System.Text.Encoding.UTF8.GetBytes(text);
        await stream.WriteAsync(bytes, 0, bytes.Length);
    }

    /// <summary>
    /// Probe the client's terminal type via telnet TTYPE negotiation.
    /// Sends IAC DO TTYPE + IAC SB TTYPE SEND, waits up to 250ms for a response,
    /// and returns true if the client identifies as a screen-reader (e.g. VIP Mud).
    /// Called only for interactive (non-AUTH) connections after the 500ms AUTH timeout.
    /// </summary>
    private static async Task<bool> ProbeTtypeAsync(NetworkStream stream, CancellationToken ct)
    {
        try
        {
            using var ttypeCts = CancellationTokenSource.CreateLinkedTokenSource(ct);
            ttypeCts.CancelAfter(TimeSpan.FromMilliseconds(250));

            // IAC DO TTYPE — ask client to advertise its terminal type
            await stream.WriteAsync(new byte[] { 0xFF, 0xFD, 0x18 }, 0, 3, ct);
            // IAC SB TTYPE SEND IAC SE — request the actual type string
            await stream.WriteAsync(new byte[] { 0xFF, 0xFA, 0x18, 0x01, 0xFF, 0xF0 }, 0, 6, ct);
            await stream.FlushAsync(ct);

            var buf = new byte[1];
            var ttype = new System.Text.StringBuilder();
            bool gotTtype = false;

            while (!ttypeCts.Token.IsCancellationRequested)
            {
                int read = await stream.ReadAsync(buf, 0, 1, ttypeCts.Token);
                if (read == 0) break;

                if (buf[0] != 0xFF) continue; // skip non-IAC bytes during probe

                // IAC — read command
                if (await stream.ReadAsync(buf, 0, 1, ttypeCts.Token) == 0) break;
                byte cmd = buf[0];

                if (cmd == 0xFB || cmd == 0xFC || cmd == 0xFD || cmd == 0xFE) // WILL/WONT/DO/DONT
                {
                    await stream.ReadAsync(buf, 0, 1, ttypeCts.Token); // consume option
                }
                else if (cmd == 0xFA) // SB — subnegotiation (TTYPE IS "..." IAC SE)
                {
                    if (await stream.ReadAsync(buf, 0, 1, ttypeCts.Token) == 0) break;
                    if (buf[0] == 0x18) // TTYPE option
                    {
                        if (await stream.ReadAsync(buf, 0, 1, ttypeCts.Token) == 0) break;
                        // buf[0] should be 0x00 (IS) — consume and read the type string
                        while (!ct.IsCancellationRequested)
                        {
                            if (await stream.ReadAsync(buf, 0, 1, ct) == 0) break;
                            if (buf[0] == 0xFF) // IAC inside SB
                            {
                                if (await stream.ReadAsync(buf, 0, 1, ct) == 0) break;
                                if (buf[0] == 0xF0) break; // SE — end of subneg
                            }
                            else
                            {
                                ttype.Append((char)buf[0]);
                            }
                        }
                        gotTtype = true;
                        break;
                    }
                    else
                    {
                        // Different option in SB — drain until IAC SE
                        while (!ct.IsCancellationRequested)
                        {
                            if (await stream.ReadAsync(buf, 0, 1, ct) == 0) break;
                            if (buf[0] == 0xFF)
                            {
                                if (await stream.ReadAsync(buf, 0, 1, ct) == 0) break;
                                if (buf[0] == 0xF0) break;
                            }
                        }
                    }
                }
            }

            if (gotTtype && ttype.Length > 0)
            {
                var ttypeStr = ttype.ToString().ToUpperInvariant();
                Console.Error.WriteLine($"[MUD] TTYPE detected: {ttypeStr}");
                // VIP Mud identifies as "VIPMUD" or starts with "VIP"
                return ttypeStr.StartsWith("VIP") || ttypeStr == "DUMB" || ttypeStr == "UNKNOWN";
            }
        }
        catch (OperationCanceledException) when (!ct.IsCancellationRequested) { }
        catch (Exception ex)
        {
            Console.Error.WriteLine($"[MUD] TTYPE probe error: {ex.Message}");
        }

        return false;
    }

    /// <summary>Read a single line from a network stream (up to \n, strips \r).
    /// Strips telnet IAC negotiation sequences sent by MUD clients:
    ///   0xFF (IAC) + cmd + [option]  — WILL/WONT/DO/DONT (0xFB-0xFE) + 1 option byte
    ///   0xFF (IAC) + 0xFA (SB) + data + 0xFF + 0xF0 (SE)  — subnegotiation block
    /// </summary>
    private static async Task<string?> ReadLineAsync(NetworkStream stream, CancellationToken ct)
    {
        var buffer = new byte[1];
        var line = new System.Text.StringBuilder();

        while (!ct.IsCancellationRequested)
        {
            int read = await stream.ReadAsync(buffer, 0, 1, ct);
            if (read == 0) return null; // Connection closed

            byte b = buffer[0];

            // Telnet IAC (0xFF) — consume the command and optional option byte
            if (b == 0xFF)
            {
                if (await stream.ReadAsync(buffer, 0, 1, ct) == 0) return null;
                byte cmd = buffer[0];
                if (cmd >= 0xFB && cmd <= 0xFE) // WILL / WONT / DO / DONT
                {
                    await stream.ReadAsync(buffer, 0, 1, ct); // consume option byte
                }
                else if (cmd == 0xFA) // SB — subnegotiation, read until IAC SE
                {
                    while (!ct.IsCancellationRequested)
                    {
                        if (await stream.ReadAsync(buffer, 0, 1, ct) == 0) return null;
                        if (buffer[0] == 0xFF) // nested IAC
                        {
                            if (await stream.ReadAsync(buffer, 0, 1, ct) == 0) return null;
                            if (buffer[0] == 0xF0) break; // SE — end of subnegotiation
                        }
                    }
                }
                continue; // skip the entire IAC sequence
            }

            char c = (char)b;
            if (c == '\n') return line.ToString().TrimEnd('\r');
            line.Append(c);

            if (line.Length > 1024) return null; // Safety limit
        }

        return null;
    }

    /// <summary>Write a line to a network stream with \r\n terminator.</summary>
    private static async Task WriteLineAsync(NetworkStream stream, string message)
    {
        var bytes = System.Text.Encoding.UTF8.GetBytes(message + "\r\n");
        await stream.WriteAsync(bytes, 0, bytes.Length);
    }

    /// <summary>Broadcast a message to all active sessions.</summary>
    public void BroadcastToAll(string message, string? excludeUsername = null)
    {
        foreach (var kvp in ActiveSessions)
        {
            if (excludeUsername != null && kvp.Key == excludeUsername.ToLowerInvariant())
                continue;

            // Don't send broadcasts to players still in login/character creation
            if (!kvp.Value.IsInGame)
                continue;

            // Don't send broadcasts to spectators (they see the target's output)
            if (kvp.Value.IsSpectating)
                continue;

            // Don't send broadcasts to group followers (they see the leader's output)
            if (kvp.Value.IsGroupFollower)
                continue;

            kvp.Value.EnqueueMessage(message);
        }
    }

    /// <summary>Send a message to a specific player by username.</summary>
    public bool SendToPlayer(string username, string message)
    {
        if (ActiveSessions.TryGetValue(username.ToLowerInvariant(), out var session))
        {
            session.EnqueueMessage(message);
            return true;
        }
        return false;
    }

    /// <summary>Get all currently online player usernames.</summary>
    public IReadOnlyList<string> GetOnlinePlayerNames()
    {
        return ActiveSessions.Keys.ToList().AsReadOnly();
    }

    /// <summary>
    /// Periodically check for idle players and disconnect them.
    /// Players with no input for IdleTimeout are auto-saved and disconnected.
    /// </summary>
    private async Task IdleWatchdogAsync(CancellationToken ct)
    {
        try
        {
            while (!ct.IsCancellationRequested)
            {
                await Task.Delay(TimeSpan.FromSeconds(60), ct);

                var now = DateTime.UtcNow;
                foreach (var kvp in ActiveSessions)
                {
                    var session = kvp.Value;

                    // Spectators and group followers don't produce input — exempt from idle timeout
                    if (session.IsSpectating || session.IsGroupFollower)
                        continue;

                    var idleTime = now - session.LastActivityTime;
                    if (idleTime >= IdleTimeout)
                    {
                        Console.Error.WriteLine($"[MUD] [{session.Username}] Idle timeout ({idleTime.TotalMinutes:F0} min) — disconnecting");
                        _ = session.DisconnectAsync($"Disconnected: idle for {(int)idleTime.TotalMinutes} minutes.");
                    }
                }
            }
        }
        catch (OperationCanceledException) { }
    }

    /// <summary>
    /// Initiate a graceful server shutdown with a countdown.
    /// Broadcasts warnings at intervals, then cancels the server token.
    /// </summary>
    public async Task InitiateShutdown(int seconds, string? reason = null)
    {
        if (ShutdownCountdownSeconds.HasValue)
            return; // Already shutting down

        ShutdownCountdownSeconds = seconds;
        var shutdownReason = reason ?? "Server shutting down";

        // Broadcast warnings at decreasing intervals
        int remaining = seconds;
        int[] warnAt = { 300, 120, 60, 30, 10, 5, 3, 2, 1 };

        BroadcastToAll($"\u001b[1;31m  *** SERVER SHUTDOWN in {remaining} seconds: {shutdownReason} ***\u001b[0m");

        while (remaining > 0 && !(_cts?.IsCancellationRequested ?? true))
        {
            await Task.Delay(1000);
            remaining--;
            ShutdownCountdownSeconds = remaining;

            if (Array.IndexOf(warnAt, remaining) >= 0)
            {
                BroadcastToAll($"\u001b[1;33m  *** SERVER SHUTDOWN in {remaining} seconds ***\u001b[0m");
            }
        }

        if (!(_cts?.IsCancellationRequested ?? true))
        {
            BroadcastToAll("\u001b[1;31m  *** SERVER SHUTTING DOWN NOW ***\u001b[0m");
            await Task.Delay(500);
            _cts?.Cancel();
        }
    }

    /// <summary>
    /// Kick a specific player by username with a reason message.
    /// </summary>
    public async Task<bool> KickPlayer(string username, string reason)
    {
        if (ActiveSessions.TryGetValue(username.ToLowerInvariant(), out var session))
        {
            Console.Error.WriteLine($"[MUD] Kicking player '{username}': {reason}");
            await session.DisconnectAsync($"Kicked: {reason}");
            return true;
        }
        return false;
    }
}
