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
                authCts.CancelAfter(TimeSpan.FromSeconds(3));
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

            if (isInteractive)
            {
                // Interactive mode: present login/register menu directly over TCP
                Console.Error.WriteLine($"[MUD] Interactive connection from {client.Client.RemoteEndPoint}");
                var result = await InteractiveAuthAsync(stream, sqlBackend, ct);
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
                    if (!string.IsNullOrEmpty(displayName))
                        username = displayName;
                }

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
                    cancellationToken: ct
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
        NetworkStream stream, SqlSaveBackend sqlBackend, CancellationToken ct)
    {
        const int MAX_ATTEMPTS = 5;

        for (int attempt = 0; attempt < MAX_ATTEMPTS; attempt++)
        {
            if (ct.IsCancellationRequested) return null;

            // Show auth menu
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

            var choice = (await ReadLineAsync(stream, ct))?.Trim().ToUpperInvariant();
            if (string.IsNullOrEmpty(choice)) continue;
            if (choice == "Q") return null;

            string? username = null;
            string? password = null;
            bool isRegistration = false;

            if (choice == "L")
            {
                await WriteAnsiAsync(stream, "\r\n\u001b[1;37m  Username: \u001b[0m");
                username = (await ReadLineAsync(stream, ct))?.Trim();
                if (string.IsNullOrEmpty(username)) continue;

                await WriteAnsiAsync(stream, "\u001b[1;37m  Password: \u001b[0m");
                password = (await ReadLineAsync(stream, ct))?.Trim();
                if (string.IsNullOrEmpty(password)) continue;
            }
            else if (choice == "R")
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
                await WriteAnsiAsync(stream, "\r\n\u001b[1;33m  Previous session disconnected.\u001b[0m\r\n");
            }

            await WriteAnsiAsync(stream, $"\r\n\u001b[1;32m  Welcome, {username}!\u001b[0m\r\n\r\n");
            Console.Error.WriteLine($"[MUD] Interactive auth succeeded for '{username}'");
            return (username!, "Web");
        }

        await WriteAnsiAsync(stream, "\r\n\u001b[1;31m  Too many attempts. Goodbye.\u001b[0m\r\n");
        return null;
    }

    /// <summary>Write ANSI text to a network stream.</summary>
    private static async Task WriteAnsiAsync(NetworkStream stream, string text)
    {
        var bytes = System.Text.Encoding.UTF8.GetBytes(text);
        await stream.WriteAsync(bytes, 0, bytes.Length);
    }

    /// <summary>Read a single line from a network stream (up to \n, strips \r).</summary>
    private static async Task<string?> ReadLineAsync(NetworkStream stream, CancellationToken ct)
    {
        var buffer = new byte[1];
        var line = new System.Text.StringBuilder();

        while (!ct.IsCancellationRequested)
        {
            int read = await stream.ReadAsync(buffer, 0, 1, ct);
            if (read == 0) return null; // Connection closed

            char c = (char)buffer[0];
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
