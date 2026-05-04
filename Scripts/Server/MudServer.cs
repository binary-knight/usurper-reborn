using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Net.Sockets;
using System.Text.Json;
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

    /// <summary>How long before idle timeout to show a warning.</summary>
    public static readonly TimeSpan IdleWarningBefore = TimeSpan.FromMinutes(2);

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

    /// <summary>Shared SQL backend for admin command queue access.</summary>
    private SqlSaveBackend? _sqlBackend;

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
        _sqlBackend = sqlBackend;
        SaveSystem.InitializeWithBackend(sqlBackend);
        Console.Error.WriteLine($"[MUD] SQLite backend initialized: {_databasePath}");

        // v0.60.5: wire SqlSaveBackend's kick hook so BanPlayer can drop the
        // target's TCP session immediately. Lookup is by lowercased username
        // (matches ActiveSessions key convention).
        SqlSaveBackend.KickActiveSessionHook = (username, reason) =>
        {
            try
            {
                var key = username?.ToLowerInvariant() ?? "";
                if (string.IsNullOrEmpty(key)) return;
                if (ActiveSessions.TryGetValue(key, out var s) && s != null)
                {
                    Console.Error.WriteLine($"[MUD] Banning kick: '{username}' ({reason})");
                    _ = s.DisconnectAsync(reason);
                }
            }
            catch (Exception ex)
            {
                Console.Error.WriteLine($"[MUD] KickActiveSessionHook error for '{username}': {ex.Message}");
            }
        };

        // v0.60.5: wire the in-memory permadeath cleanup hook. Player report
        // (Rage): "lost all 4 lives, made a new char, came back in my guild
        // still, actually still worshiping the same god." DB tables are handled
        // by PurgePlayerWorldState; this hook clears the per-process state that
        // doesn't live in SQLite -- god worship dict, relationship cache, etc.
        SqlSaveBackend.PermadeathPurgeHook = (username) =>
        {
            try
            {
                if (string.IsNullOrWhiteSpace(username)) return;
                // God worship: clears the player's entry AND decrements the
                // god's believer count atomically.
                try { GodSystemSingleton.Instance.SetPlayerGod(username, ""); }
                catch (Exception gex) { Console.Error.WriteLine($"[MUD] PurgeHook god clear failed for '{username}': {gex.Message}"); }

                // Relationship cache: drop all per-player relationship entries.
                try { RelationshipSystem.Instance?.ResetPlayerRelationships(username); }
                catch (Exception rex) { Console.Error.WriteLine($"[MUD] PurgeHook relationships clear failed for '{username}': {rex.Message}"); }
            }
            catch (Exception ex)
            {
                Console.Error.WriteLine($"[MUD] PermadeathPurgeHook error for '{username}': {ex.Message}");
            }
        };

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

        // Initialize the guild system for persistent guilds
        var guildSystem = new UsurperRemake.Systems.GuildSystem(_databasePath);
        Console.Error.WriteLine("[MUD] Guild system initialized");

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

        // v0.60.4: bot detection snapshot writer — every 30s, capture the
        // BotDetectionSystem ring buffers and UPSERT them to a single SQLite row
        // for the admin dashboard to read. No-op when no players are tracked.
        var botSnapshotTask = Task.Run(() => BotDetectionSnapshotLoopAsync(_cts.Token));
        Console.Error.WriteLine("[MUD] Bot detection snapshot writer started (30s interval)");

        // Start admin command queue poller (checks every 3 seconds for web dashboard commands)
        var adminPollerTask = Task.Run(() => AdminCommandPollerAsync(_cts.Token));
        Console.Error.WriteLine("[MUD] Admin command queue poller started (3s interval)");

        // v0.57.13: Discord bridge — ensure schema exists, then start the inbound poller.
        // The game side has no Discord secrets; the Node bot (ssh-proxy.js) handles the
        // actual Discord API connection. We communicate via the shared SQLite `discord_gossip` table.
        UsurperRemake.Systems.DiscordBridge.Initialize(_databasePath);
        var discordBridgeTask = Task.Run(() => DiscordBridgePollerAsync(_cts.Token));
        Console.Error.WriteLine("[MUD] Discord bridge poller started (250ms interval)");

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

                // Handle each connection in a tracked task so we can wait for
                // emergency saves to complete on shutdown
                var sessionTask = Task.Run(async () =>
                {
                    try
                    {
                        await HandleConnectionAsync(client, sqlBackend, _cts.Token);
                    }
                    catch (Exception ex)
                    {
                        Console.Error.WriteLine($"[MUD] Connection handler crashed: {ex.Message}");
                        DebugLogger.Instance.LogError("MUD", $"Connection handler crashed: {ex}");
                    }
                });
                lock (_sessionTasks)
                {
                    _sessionTasks.Add(sessionTask);
                    // Opportunistic cleanup of completed tasks to prevent unbounded growth
                    _sessionTasks.RemoveAll(t => t.IsCompleted);
                }
            }
        }
        finally
        {
            _listener.Stop();
            Console.Error.WriteLine("[MUD] Server stopped.");

            // Gracefully disconnect all sessions (closes streams, unblocks ReadLines)
            var disconnectTasks = ActiveSessions.Values.Select(s => s.DisconnectAsync("Server shutting down")).ToArray();
            await Task.WhenAll(disconnectTasks);

            // CRITICAL: Wait for all session task finally blocks to complete (emergency saves).
            // Without this, the process may exit before SaveGame() finishes writing companion
            // and player data, causing equipment and progress loss on server restart/deploy.
            Task[] pendingSessions;
            lock (_sessionTasks)
            {
                pendingSessions = _sessionTasks.Where(t => !t.IsCompleted).ToArray();
            }
            if (pendingSessions.Length > 0)
            {
                Console.Error.WriteLine($"[MUD] Waiting for {pendingSessions.Length} session emergency saves to complete...");
                try
                {
                    await Task.WhenAll(pendingSessions).WaitAsync(TimeSpan.FromSeconds(30));
                    Console.Error.WriteLine("[MUD] All session emergency saves completed.");
                }
                catch (TimeoutException)
                {
                    Console.Error.WriteLine("[MUD] WARNING: Some session emergency saves timed out after 30s.");
                }
                catch (Exception ex)
                {
                    Console.Error.WriteLine($"[MUD] Session task wait error: {ex.Message}");
                }
            }

            // Wait for world simulator to finish its final save
            Console.Error.WriteLine("[MUD] Waiting for world simulator to shut down...");
            try { await worldSimTask; } catch (OperationCanceledException) { }
            Console.Error.WriteLine("[MUD] World simulator shut down.");
        }
    }

    private readonly List<Task> _sessionTasks = new();

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
            string? forwardedIP = null;

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

            // Check for X-IP forwarded client IP from relay/proxy
            string? proxyClientType = null;
            if (authLine != null && authLine.StartsWith("X-IP:"))
            {
                forwardedIP = authLine.Substring(5).Trim();
                Console.Error.WriteLine($"[MUD] Forwarded client IP: {forwardedIP}");
                // Read the next line for AUTH header or X-Client
                authLine = null;
                try
                {
                    using var authCts2 = CancellationTokenSource.CreateLinkedTokenSource(ct);
                    authCts2.CancelAfter(TimeSpan.FromMilliseconds(500));
                    authLine = await ReadLineAsync(stream, authCts2.Token);
                    // Check for X-Client type header (Web, Electron, etc.)
                    if (authLine != null && authLine.StartsWith("X-Client:"))
                    {
                        proxyClientType = authLine.Substring(9).Trim();
                        Console.Error.WriteLine($"[MUD] Client type: {proxyClientType}");
                        // Read one more line for potential AUTH header
                        authLine = null;
                        try
                        {
                            using var authCts3 = CancellationTokenSource.CreateLinkedTokenSource(ct);
                            authCts3.CancelAfter(TimeSpan.FromMilliseconds(500));
                            authLine = await ReadLineAsync(stream, authCts3.Token);
                        }
                        catch (OperationCanceledException) when (!ct.IsCancellationRequested)
                        {
                            isInteractive = true;
                        }
                    }
                }
                catch (OperationCanceledException) when (!ct.IsCancellationRequested)
                {
                    isInteractive = true;
                }
            }

            if (authLine != null && !authLine.StartsWith("AUTH:"))
            {
                // Got a line but it's not AUTH — treat as interactive
                isInteractive = true;
                firstBytes = System.Text.Encoding.UTF8.GetBytes(authLine);
            }

            // Capture the RAW TCP peer IP separately from effectiveIp. effectiveIp
            // is the public-facing address (X-IP from a relay if set), used for ban
            // checks and rate-limit attribution. rawPeerIp is the actual socket peer,
            // used for trust-gating: only loopback connections may use trusted auth
            // (no password). Anyone can spoof X-IP, only loopback can spoof rawPeerIp.
            string? rawPeerIp = null;
            try { rawPeerIp = (client.Client.RemoteEndPoint as System.Net.IPEndPoint)?.Address.ToString(); }
            catch { /* socket may have closed */ }
            bool isLoopbackPeer = false;
            if (!string.IsNullOrEmpty(rawPeerIp))
            {
                try
                {
                    var addr = System.Net.IPAddress.Parse(rawPeerIp);
                    isLoopbackPeer = System.Net.IPAddress.IsLoopback(addr);
                }
                catch { /* malformed; not loopback */ }
            }

            // v0.60.5: IP-ban check at the earliest practical point. Effective IP
            // is the X-IP forwarded header value if a relay set one (web/SSH gateway),
            // otherwise the raw TCP peer address. Banned IPs get a polite message and
            // a closed socket — no auth attempt, no register attempt, no session.
            string? effectiveIp = forwardedIP;
            if (string.IsNullOrEmpty(effectiveIp))
                effectiveIp = rawPeerIp;
            if (!string.IsNullOrEmpty(effectiveIp) && sqlBackend.IsIpBanned(effectiveIp))
            {
                var ipReason = sqlBackend.GetIpBanReason(effectiveIp);
                try
                {
                    await WriteLineAsync(stream, "");
                    await WriteLineAsync(stream, "  Connection refused: this address is banned from the server.");
                    if (!string.IsNullOrEmpty(ipReason))
                        await WriteLineAsync(stream, $"  Reason: {ipReason}");
                    await WriteLineAsync(stream, "");
                }
                catch { /* socket may already be dead */ }
                Console.Error.WriteLine($"[MUD] Refused banned IP {effectiveIp} (reason: {ipReason ?? "none"})");
                client.Close();
                return;
            }

            string connectionType;
            bool isPlainText = false;
            bool isCp437 = false;
            bool gmcpEnabled = false;

            if (isInteractive)
            {
                // Telnet negotiation: tell MUD clients the server will handle echo,
                // suppressing their local echo. Without this, Mudlet (and other MUD
                // clients) echo keystrokes locally AND the server echoes them back via
                // ReadLineInteractiveAsync, causing every character to appear twice.
                //   IAC WILL ECHO  (0xFF 0xFB 0x01) — server will echo, disable client echo
                //   IAC WILL SGA   (0xFF 0xFB 0x03) — suppress go-ahead (character mode)
                //   IAC WILL GMCP  (0xFF 0xFB 0xC9) — v0.57.21: offer GMCP out-of-band events
                await stream.WriteAsync(new byte[] { 0xFF, 0xFB, 0x01, 0xFF, 0xFB, 0x03, 0xFF, 0xFB, 0xC9 }, 0, 9, ct);
                await stream.FlushAsync(ct);

                // Probe terminal type to detect screen-reader and CP437 BBS clients,
                // and capture any GMCP DO/DONT response that arrives in the same window.
                var probeResult = await ProbeTtypeAsync(stream, ct);
                isPlainText = probeResult.isPlainText;
                isCp437 = probeResult.isCp437;
                gmcpEnabled = probeResult.gmcpEnabled;
                if (gmcpEnabled)
                    Console.Error.WriteLine($"[MUD] GMCP enabled for {client.Client.RemoteEndPoint}");
                if (isPlainText)
                    Console.Error.WriteLine($"[MUD] Screen-reader client detected from {client.Client.RemoteEndPoint} — plain text mode");
                if (isCp437)
                    Console.Error.WriteLine($"[MUD] CP437 BBS terminal detected ({probeResult.ttype}) from {client.Client.RemoteEndPoint}");

                // Interactive mode: present login/register menu directly over TCP
                Console.Error.WriteLine($"[MUD] Interactive connection from {client.Client.RemoteEndPoint}");
                var result = await InteractiveAuthAsync(stream, sqlBackend, ct, isPlainText, isCp437, effectiveIp);
                if (result == null)
                {
                    client.Close();
                    return;
                }
                username = result.Value.username;
                connectionType = proxyClientType ?? result.Value.connectionType;
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

                // SECURITY: trusted auth (no password) is only allowed from loopback.
                // The 3-part AUTH:user:type form was originally intended for the local
                // SSH relay (sshd-usurper -> --mud-relay -> 127.0.0.1:4001) and the BBS
                // gateway. Without this gate, any external client could telnet to the
                // game port and send "AUTH:targetUser:Web" to log in as that account
                // with no password verification. Reproduces against any public server.
                // Loopback-only because the raw TCP peer is the only thing the server
                // can trust (X-IP forwarded headers can be spoofed by external clients).
                // Remote BBS deployments that previously used trusted Online Play must
                // either co-locate with the game server or migrate to password auth.
                if (password == null && !isLoopbackPeer)
                {
                    Console.Error.WriteLine($"[MUD] SECURITY: rejected trusted-auth from non-loopback peer {rawPeerIp ?? "unknown"} (claimed user='{username}', type='{connectionType}')");
                    await WriteLineAsync(stream, "ERR:Authentication required. Trusted auth is only available from local relays.");
                    client.Close();
                    return;
                }

                // Handle registration
                if (isRegistration && password != null)
                {
                    var (regSuccess, regMessage) = await sqlBackend.RegisterPlayer(username, password, effectiveIp);
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
                    var (success, displayName, message, screenReader, language) = await sqlBackend.AuthenticatePlayer(username, password, effectiveIp);
                    if (!success)
                    {
                        Console.Error.WriteLine($"[MUD] Auth failed for '{username}': {message}");
                        await WriteLineAsync(stream, $"ERR:{message}");
                        client.Close();
                        return;
                    }
                    // Account-level preferences (screenReader, language) are loaded by PlayerSession
                    // directly from DB after SessionContext is created (per-session AsyncLocal).
                    // Do NOT replace username with displayName — displayName is a cosmetic
                    // value from save data that can be corrupted (e.g., by alt character bugs).
                    // The session identity must always be the login key (account name).
                }

                // Auto-provision: create account if it doesn't exist (trusted auth only)
                if (password == null && UsurperRemake.BBS.DoorMode.AutoProvision)
                {
                    if (!sqlBackend.PlayerExists(username))
                    {
                        var (provSuccess, provMessage) = await sqlBackend.AutoProvisionPlayer(username);
                        if (!provSuccess)
                        {
                            Console.Error.WriteLine($"[MUD] Auto-provision failed for '{username}': {provMessage}");
                            await WriteLineAsync(stream, $"ERR:{provMessage}");
                            client.Close();
                            return;
                        }
                        Console.Error.WriteLine($"[MUD] Auto-provisioned new account: '{username}'");
                    }
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
                    isPlainText: isPlainText,
                    isCp437: isCp437,
                    gmcpEnabled: gmcpEnabled,
                    forwardedIP: forwardedIP
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
                        await WriteAnsiAsync(stream, "\r\n\u001b[1;31m  Could not start session. Try again.\u001b[0m\r\n", isCp437);
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
                // v0.60.0 bot-detection Tier 1: drop the player's input-timing ring
                // buffer so reconnects start fresh (otherwise a fast player who
                // reconnects inherits a stale fast-streak count).
                BotDetectionSystem.ClearSession(usernameKey);
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
        NetworkStream stream, SqlSaveBackend sqlBackend, CancellationToken ct, bool isPlainText = false, bool isCp437 = false, string? effectiveIp = null)
    {
        const int MAX_ATTEMPTS = 5;

        for (int attempt = 0; attempt < MAX_ATTEMPTS; attempt++)
        {
            if (ct.IsCancellationRequested) return null;

            // Show auth menu
            if (isPlainText)
            {
                await WriteAnsiAsync(stream, "\r\n=== Usurper Reborn Online ===\r\n", isCp437);
                await WriteAnsiAsync(stream, "[L] Login\r\n", isCp437);
                await WriteAnsiAsync(stream, "[R] Register\r\n", isCp437);
                await WriteAnsiAsync(stream, "[Q] Quit\r\n", isCp437);
                await WriteAnsiAsync(stream, "Choice: ", isCp437);
            }
            else
            {
                await WriteAnsiAsync(stream, "\u001b[2J\u001b[H", isCp437); // Clear screen
                await WriteAnsiAsync(stream, "\u001b[1;36m", isCp437);
                await WriteAnsiAsync(stream, "╔══════════════════════════════════════════════════════════════════════════════╗\r\n", isCp437);
                await WriteAnsiAsync(stream, "\u001b[1;37m", isCp437);
                await WriteAnsiAsync(stream, "║                      Welcome to Usurper Reborn Online                      ║\r\n", isCp437);
                await WriteAnsiAsync(stream, "\u001b[1;36m", isCp437);
                await WriteAnsiAsync(stream, "╠══════════════════════════════════════════════════════════════════════════════╣\r\n", isCp437);
                await WriteAnsiAsync(stream, "\u001b[0;37m", isCp437);
                await WriteAnsiAsync(stream, "║                                                                              ║\r\n", isCp437);
                await WriteAnsiAsync(stream, "║  \u001b[1;36m[L]\u001b[0;37m Login to existing account                                             ║\r\n", isCp437);
                await WriteAnsiAsync(stream, "║  \u001b[1;32m[R]\u001b[0;37m Register new account                                                  ║\r\n", isCp437);
                await WriteAnsiAsync(stream, "║  \u001b[1;31m[Q]\u001b[0;37m Quit                                                                  ║\r\n", isCp437);
                await WriteAnsiAsync(stream, "║                                                                              ║\r\n", isCp437);
                await WriteAnsiAsync(stream, "\u001b[1;36m", isCp437);
                await WriteAnsiAsync(stream, "╚══════════════════════════════════════════════════════════════════════════════╝\r\n", isCp437);
                await WriteAnsiAsync(stream, "\u001b[0m", isCp437);
                await WriteAnsiAsync(stream, "\r\n  Choice: ", isCp437);
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
                    await WriteAnsiAsync(stream, "Username: ", isCp437);
                    username = (await ReadLineAsync(stream, ct))?.Trim();
                    if (string.IsNullOrEmpty(username)) continue;
                    await WriteAnsiAsync(stream, "Password: ", isCp437);
                    password = (await ReadLineAsync(stream, ct))?.Trim();
                    if (string.IsNullOrEmpty(password)) continue;
                }
                else
                {
                    await WriteAnsiAsync(stream, "\r\n\u001b[1;37m  Username: \u001b[0m", isCp437);
                    username = (await ReadLineAsync(stream, ct))?.Trim();
                    if (string.IsNullOrEmpty(username)) continue;
                    await WriteAnsiAsync(stream, "\u001b[1;37m  Password: \u001b[0m", isCp437);
                    password = (await ReadLineAsync(stream, ct))?.Trim();
                    if (string.IsNullOrEmpty(password)) continue;
                }
            }
            else if (choice == "R")
            {
                if (isPlainText)
                {
                    await WriteAnsiAsync(stream, "Choose a username: ", isCp437);
                    username = (await ReadLineAsync(stream, ct))?.Trim();
                    if (string.IsNullOrEmpty(username)) continue;
                    if (username.Length < 2 || username.Length > 20)
                    {
                        await WriteAnsiAsync(stream, "Username must be 2-20 characters.\r\n\r\n", isCp437);
                        continue;
                    }
                    await WriteAnsiAsync(stream, "Choose a password: ", isCp437);
                    password = (await ReadLineAsync(stream, ct))?.Trim();
                    if (string.IsNullOrEmpty(password)) continue;
                    if (password.Length < 4)
                    {
                        await WriteAnsiAsync(stream, "Password must be at least 4 characters.\r\n\r\n", isCp437);
                        continue;
                    }
                    await WriteAnsiAsync(stream, "Confirm password: ", isCp437);
                    var confirm = (await ReadLineAsync(stream, ct))?.Trim();
                    if (password != confirm)
                    {
                        await WriteAnsiAsync(stream, "Passwords do not match.\r\n\r\n", isCp437);
                        continue;
                    }
                }
                else
                {
                    await WriteAnsiAsync(stream, "\r\n\u001b[1;32m  Choose a username: \u001b[0m", isCp437);
                    username = (await ReadLineAsync(stream, ct))?.Trim();
                    if (string.IsNullOrEmpty(username)) continue;
                    if (username.Length < 2 || username.Length > 20)
                    {
                        await WriteAnsiAsync(stream, "\r\n\u001b[1;31m  Username must be 2-20 characters.\u001b[0m\r\n\r\n", isCp437);
                        continue;
                    }
                    await WriteAnsiAsync(stream, "\u001b[1;32m  Choose a password: \u001b[0m", isCp437);
                    password = (await ReadLineAsync(stream, ct))?.Trim();
                    if (string.IsNullOrEmpty(password)) continue;
                    if (password.Length < 4)
                    {
                        await WriteAnsiAsync(stream, "\r\n\u001b[1;31m  Password must be at least 4 characters.\u001b[0m\r\n\r\n", isCp437);
                        continue;
                    }
                    await WriteAnsiAsync(stream, "\u001b[1;32m  Confirm password: \u001b[0m", isCp437);
                    var confirm = (await ReadLineAsync(stream, ct))?.Trim();
                    if (password != confirm)
                    {
                        await WriteAnsiAsync(stream, "\r\n\u001b[1;31m  Passwords do not match.\u001b[0m\r\n\r\n", isCp437);
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
                var (regSuccess, regMessage) = await sqlBackend.RegisterPlayer(username!, password!, effectiveIp);
                if (!regSuccess)
                {
                    Console.Error.WriteLine($"[MUD] Registration failed for '{username}': {regMessage}");
                    if (isPlainText)
                        await WriteAnsiAsync(stream, $"Error: {regMessage}\r\n\r\n", isCp437);
                    else
                        await WriteAnsiAsync(stream, $"\r\n\u001b[1;31m  {regMessage}\u001b[0m\r\n\r\n", isCp437);
                    continue;
                }
                Console.Error.WriteLine($"[MUD] New player registered: '{username}'");
            }

            // Authenticate
            var (success, displayName, message, screenReader, language) = await sqlBackend.AuthenticatePlayer(username!, password!, effectiveIp);
            if (!success)
            {
                Console.Error.WriteLine($"[MUD] Auth failed for '{username}': {message}");
                if (isPlainText)
                    await WriteAnsiAsync(stream, $"Error: {message}\r\n\r\n", isCp437);
                else
                    await WriteAnsiAsync(stream, $"\r\n\u001b[1;31m  {message}\u001b[0m\r\n\r\n", isCp437);
                continue;
            }

            // Apply account-level preferences before game starts
            // Account-level preferences applied in PlayerSession after SessionContext is created
            // (see trusted auth path comment for explanation)

            // Do NOT replace username with displayName — displayName is cosmetic.
            // The session identity must always be the login key (account name).

            // Kick existing session if duplicate (reconnect takes priority)
            var interactiveKey = username!.ToLowerInvariant();
            if (ActiveSessions.TryGetValue(interactiveKey, out var existingInteractive))
            {
                Console.Error.WriteLine($"[MUD] Kicking stale session for '{username}' (reconnect)");
                await existingInteractive.DisconnectAsync("Disconnected: logged in from another session");
                ActiveSessions.TryRemove(interactiveKey, out _);
                await Task.Delay(500);
                if (isPlainText)
                    await WriteAnsiAsync(stream, "Previous session disconnected.\r\n", isCp437);
                else
                    await WriteAnsiAsync(stream, "\r\n\u001b[1;33m  Previous session disconnected.\u001b[0m\r\n", isCp437);
            }

            if (isPlainText)
                await WriteAnsiAsync(stream, $"Welcome, {username}!\r\n\r\n", isCp437);
            else
                await WriteAnsiAsync(stream, $"\r\n\u001b[1;32m  Welcome, {username}!\u001b[0m\r\n\r\n", isCp437);
            Console.Error.WriteLine($"[MUD] Interactive auth succeeded for '{username}'");
            return (username!, "MUD");
        }

        if (isPlainText)
            await WriteAnsiAsync(stream, "Too many attempts. Goodbye.\r\n", isCp437);
        else
            await WriteAnsiAsync(stream, "\r\n\u001b[1;31m  Too many attempts. Goodbye.\u001b[0m\r\n", isCp437);
        return null;
    }

    /// <summary>Cached CP437 encoding instance (initialized once on first use).</summary>
    private static System.Text.Encoding? _cp437Encoding;

    /// <summary>Write ANSI text to a network stream, optionally encoding as CP437.</summary>
    private static async Task WriteAnsiAsync(NetworkStream stream, string text, bool cp437 = false)
    {
        byte[] bytes;
        if (cp437)
        {
            if (_cp437Encoding == null)
            {
                System.Text.Encoding.RegisterProvider(System.Text.CodePagesEncodingProvider.Instance);
                _cp437Encoding = System.Text.Encoding.GetEncoding(437,
                    new System.Text.EncoderReplacementFallback("?"),
                    new System.Text.DecoderReplacementFallback("?"));
            }
            bytes = _cp437Encoding.GetBytes(text);
        }
        else
        {
            bytes = System.Text.Encoding.UTF8.GetBytes(text);
        }
        await stream.WriteAsync(bytes, 0, bytes.Length);
    }

    /// <summary>
    /// Probe the client's terminal type via telnet TTYPE negotiation.
    /// Sends IAC DO TTYPE + IAC SB TTYPE SEND, waits up to 250ms for a response.
    /// Returns (isPlainText, isCp437, ttypeString, gmcpEnabled).
    /// isPlainText=true for screen readers (VIP Mud). isCp437=true for BBS terminals (SyncTerm, etc.).
    /// gmcpEnabled=true if the client responded IAC DO GMCP to our IAC WILL GMCP offer.
    /// Called only for interactive (non-AUTH) connections after the 500ms AUTH timeout.
    /// </summary>
    private static async Task<(bool isPlainText, bool isCp437, string? ttype, bool gmcpEnabled)> ProbeTtypeAsync(NetworkStream stream, CancellationToken ct)
    {
        bool gmcpEnabled = false;
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
                    if (await stream.ReadAsync(buf, 0, 1, ttypeCts.Token) == 0) break; // consume option
                    // v0.57.21: detect IAC DO GMCP (0xFD + 0xC9) as "client supports GMCP".
                    // IAC DONT GMCP (0xFE + 0xC9) explicitly leaves it disabled.
                    if (buf[0] == 0xC9 && cmd == 0xFD)
                        gmcpEnabled = true;
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
                        // Don't break — keep draining so GMCP DO/DONT also has a chance to arrive.
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
                bool isPlain = ttypeStr.StartsWith("VIP") || ttypeStr == "DUMB" || ttypeStr == "UNKNOWN";
                // BBS terminals that expect CP437 encoding instead of UTF-8
                bool isCp437 = ttypeStr.Contains("SYNCTERM") || ttypeStr.Contains("NETRUNNER")
                    || ttypeStr.Contains("MTELNET") || ttypeStr.Contains("FTELNET")
                    || ttypeStr.Contains("CTERM") || ttypeStr.Contains("ANSI-BBS");
                return (isPlain, isCp437, ttypeStr, gmcpEnabled);
            }
        }
        catch (OperationCanceledException) when (!ct.IsCancellationRequested) { }
        catch (Exception ex)
        {
            Console.Error.WriteLine($"[MUD] TTYPE probe error: {ex.Message}");
        }

        return (false, false, null, gmcpEnabled);
    }

    /// <summary>Read a single line from a network stream (up to \n, strips \r).
    /// Strips telnet IAC negotiation sequences sent by MUD clients:
    ///   0xFF (IAC) + cmd + [option]  — WILL/WONT/DO/DONT (0xFB-0xFE) + 1 option byte
    ///   0xFF (IAC) + 0xFA (SB) + data + 0xFF + 0xF0 (SE)  — subnegotiation block
    /// </summary>
    /// <param name="echo">If true, echo each typed character back to the stream (for MUD clients
    /// that disabled local echo after receiving IAC WILL ECHO).</param>
    /// <param name="maskChar">If set and echo is true, echo this character instead of the real one (for passwords).</param>
    private static async Task<string?> ReadLineAsync(NetworkStream stream, CancellationToken ct,
        bool echo = false, char? maskChar = null)
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

            // Handle line endings: \r, \n, or \r\n
            // MUD clients (Mudlet, MUSHclient) typically send \r\n as a single packet.
            // After reading \r, we consume a trailing \n if immediately available so it
            // doesn't leak into the NEXT ReadLineAsync call as an empty-string return.
            if (c == '\n')
            {
                return line.ToString();
            }
            if (c == '\r')
            {
                // Drain trailing \n if it's already in the buffer (sent as \r\n packet)
                if (stream.DataAvailable)
                {
                    int peek = await stream.ReadAsync(buffer, 0, 1, ct);
                    // If it wasn't \n, we lost a byte — but \r without \n is extremely rare
                }
                return line.ToString();
            }

            // Handle backspace (BS 0x08 or DEL 0x7F)
            if (c == '\b' || c == 0x7F)
            {
                if (line.Length > 0)
                {
                    line.Remove(line.Length - 1, 1);
                    if (echo)
                        await stream.WriteAsync(new byte[] { (byte)'\b', (byte)' ', (byte)'\b' }, 0, 3, ct);
                }
                continue;
            }

            // Skip non-printable control characters
            if (c < ' ') continue;

            line.Append(c);

            // Echo the character (or mask for passwords)
            if (echo)
            {
                byte echoChar = (byte)(maskChar ?? c);
                await stream.WriteAsync(new byte[] { echoChar }, 0, 1, ct);
            }

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

    /// <summary>
    /// Broadcast a message to all active sessions.
    /// v0.57.14: optional channelKey enables per-channel mutes — if a recipient has
    /// the channel in their MutedChannels set, they don't receive this broadcast.
    /// channelKey = null means "always deliver" (system messages, server events).
    /// </summary>
    public void BroadcastToAll(string message, string? excludeUsername = null, string? channelKey = null)
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

            // v0.57.14: per-channel mute filter
            if (channelKey != null)
            {
                var recipientPlayer = kvp.Value.Context?.Engine?.CurrentPlayer;
                if (recipientPlayer?.MutedChannels?.Contains(channelKey) == true)
                    continue;
            }

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
    /// <summary>
    /// v0.60.4: periodic snapshot writer for the BotDetectionSystem. Every 30s
    /// dumps the current ring buffer state to a single SQLite row that the admin
    /// dashboard polls. Cheap when no sessions are in the dictionary (early return).
    /// </summary>
    private async Task BotDetectionSnapshotLoopAsync(CancellationToken ct)
    {
        try
        {
            while (!ct.IsCancellationRequested)
            {
                await Task.Delay(TimeSpan.FromSeconds(30), ct);
                try { BotDetectionSystem.WriteSnapshotToDb(_sqlBackend); }
                catch (Exception ex)
                {
                    DebugLogger.Instance.LogWarning("BOT_SNAPSHOT",
                        $"snapshot loop tick failed: {ex.GetType().Name}: {ex.Message}");
                }
            }
        }
        catch (OperationCanceledException) { /* shutdown — expected */ }
    }

    private async Task IdleWatchdogAsync(CancellationToken ct)
    {
        try
        {
            while (!ct.IsCancellationRequested)
            {
                await Task.Delay(TimeSpan.FromSeconds(30), ct);

                var now = DateTime.UtcNow;
                foreach (var kvp in ActiveSessions)
                {
                    var session = kvp.Value;

                    // Spectators and group followers don't produce input — exempt from idle timeout
                    if (session.IsSpectating || session.IsGroupFollower)
                        continue;

                    var idleTime = now - session.LastActivityTime;

                    // Reset warning flag when player becomes active again
                    if (idleTime < IdleTimeout - IdleWarningBefore)
                    {
                        session.IdleWarningShown = false;
                        continue;
                    }

                    // Show warning before disconnect
                    if (idleTime >= IdleTimeout - IdleWarningBefore && !session.IdleWarningShown)
                    {
                        session.IdleWarningShown = true;
                        var minutesLeft = (int)Math.Ceiling((IdleTimeout - idleTime).TotalMinutes);
                        try
                        {
                            // v0.60.0, issue #90 (fastfinge): emit ASCII BEL before the
                            // warning text so the user gets an audible alert when the
                            // terminal window is minimized. Universal across every MUD
                            // client we ship to: xterm/WezTerm play the system beep,
                            // Mudlet/TinTin++ pass it through, VIP Mud and NVDA-paired
                            // screen-reader clients speak/chirp on BEL, web terminal
                            // (xterm.js) plays the bell sound when audible-bell is on,
                            // BBS terminals (SyncTERM/NetRunner) honor BEL natively.
                            // Two BELs because some terminals coalesce a single bell
                            // into one short tick that's easy to miss; two is reliably
                            // noticeable without being obnoxious.
                            session.Context?.Terminal?.WriteRawAnsi("\x07\x07");
                            session.Context?.Terminal?.WriteLine("");
                            session.Context?.Terminal?.SetColor("bright_yellow");
                            session.Context?.Terminal?.WriteLine($"  *** WARNING: You will be disconnected in ~{minutesLeft} minute{(minutesLeft != 1 ? "s" : "")} due to inactivity! Press any key. ***");
                            session.Context?.Terminal?.SetColor("white");

                            // Electron client: emit a structured sound event so the
                            // graphical client can play a dedicated chirp through its
                            // audio mixer regardless of terminal-bell handling. The
                            // sound asset isn't shipped yet (Phase 9.5 audio
                            // scaffolding) but emitting now is harmless: the JS side
                            // gracefully ignores unknown soundIds.
                            if (global::GameConfig.ElectronMode)
                            {
                                global::ElectronBridge.EmitSound("sfx.idle_warning", channel: "ui");
                            }
                        }
                        catch { }
                        Console.Error.WriteLine($"[MUD] [{session.Username}] Idle warning sent ({idleTime.TotalMinutes:F0} min idle)");
                    }

                    // Disconnect after full timeout
                    if (idleTime >= IdleTimeout)
                    {
                        Console.Error.WriteLine($"[MUD] [{session.Username}] Idle timeout ({idleTime.TotalMinutes:F0} min) — disconnecting");
                        await session.DisconnectAsync($"Disconnected: idle for {(int)idleTime.TotalMinutes} minutes.");
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

    /// <summary>
    /// Polls admin_commands table every 3 seconds for commands from the web dashboard.
    /// </summary>
    private async Task AdminCommandPollerAsync(CancellationToken ct)
    {
        while (!ct.IsCancellationRequested)
        {
            try
            {
                await Task.Delay(TimeSpan.FromSeconds(3), ct);
            }
            catch (OperationCanceledException) { break; }

            if (_sqlBackend == null) continue;

            try
            {
                var commands = _sqlBackend.GetPendingAdminCommands();
                foreach (var cmd in commands)
                {
                    await ExecuteAdminCommand(cmd);
                }

                // Periodic cleanup
                _sqlBackend.PruneSnoopBuffer();
                _sqlBackend.ExpireStaleAdminCommands();
            }
            catch (Exception ex)
            {
                Console.Error.WriteLine($"[MUD] Admin command poller error: {ex.Message}");
            }
        }
    }

    /// <summary>
    /// v0.57.13: poll the Discord bridge inbound queue every 2 seconds. Any messages the Node
    /// bot has written from the #in-game-gossip channel get re-broadcast through the same
    /// global gossip channel everyone else sees. Authors arrive tagged like "Rage (Discord)".
    /// </summary>
    private async Task DiscordBridgePollerAsync(CancellationToken ct)
    {
        while (!ct.IsCancellationRequested)
        {
            try
            {
                // v0.57.13: 2s → 250ms for near-real-time gossip bridge
                await Task.Delay(TimeSpan.FromMilliseconds(250), ct);
            }
            catch (OperationCanceledException) { break; }

            try
            {
                var messages = UsurperRemake.Systems.DiscordBridge.DrainInbound();
                foreach (var msg in messages)
                {
                    // Match the format used by HandleGossip so Discord-originated lines look
                    // identical to in-game /gos in everyone's scroll (author already has the
                    // "(Discord)" suffix applied by the Node bot).
                    // v0.60.0: pass channelKey: "gossip" so per-channel mutes apply
                    // to Discord-originated gossips too. Pre-fix the Discord inbound
                    // path bypassed the mute filter entirely, players who had typed
                    // `/gos` to mute the gossip channel still saw Discord posts.
                    // Also fan out via GMCP so chat-capture clients (Mudlet/MUSHclient
                    // split panes) see Discord posts on the same Comm.Channel.Text
                    // stream as in-game gossip, with the same mute-respecting logic.
                    RoomRegistry.Instance?.BroadcastGlobal(
                        $"[92m  [Gossip] {msg.Author}: {msg.Message}[0m",
                        excludeUsername: null,
                        channelKey: "gossip");
                    UsurperRemake.Server.MudChatSystem.FanoutChannelText("gossip", msg.Author, msg.Message);
                }
            }
            catch (Exception ex)
            {
                Console.Error.WriteLine($"[MUD] Discord bridge poller error: {ex.Message}");
            }
        }
    }

    /// <summary>
    /// Execute a single admin command from the web dashboard.
    /// </summary>
    private async Task ExecuteAdminCommand(AdminCommand cmd)
    {
        if (_sqlBackend == null) return;

        string? reason = null;
        string? message = null;
        try
        {
            // Parse args JSON if present
            if (!string.IsNullOrEmpty(cmd.Args))
            {
                using var doc = JsonDocument.Parse(cmd.Args);
                var root = doc.RootElement;
                if (root.TryGetProperty("reason", out var reasonEl))
                    reason = reasonEl.GetString();
                if (root.TryGetProperty("message", out var messageEl))
                    message = messageEl.GetString();
            }
        }
        catch { /* Args parsing is best-effort */ }

        var target = cmd.TargetUsername?.ToLowerInvariant();
        ActiveSessions.TryGetValue(target ?? "", out var session);

        try
        {
            switch (cmd.Command)
            {
                case "kick":
                    if (target == null)
                    {
                        _sqlBackend.MarkAdminCommandFailed(cmd.Id, "No target specified");
                        return;
                    }
                    if (await KickPlayer(target, reason ?? "Kicked by admin"))
                        _sqlBackend.MarkAdminCommandExecuted(cmd.Id, $"Kicked {target}");
                    else
                        _sqlBackend.MarkAdminCommandFailed(cmd.Id, $"Player '{target}' is not online");
                    break;

                case "freeze":
                    if (target == null) { _sqlBackend.MarkAdminCommandFailed(cmd.Id, "No target"); return; }
                    await _sqlBackend.SetFrozen(target, true, "admin-web");
                    if (session != null)
                    {
                        session.IsFrozen = true;
                        session.EnqueueMessage("\u001b[1;36m  *** You have been frozen by the gods. ***\u001b[0m");
                    }
                    _sqlBackend.MarkAdminCommandExecuted(cmd.Id, $"Frozen {target}" + (session != null ? " (live)" : " (DB only, offline)"));
                    break;

                case "thaw":
                    if (target == null) { _sqlBackend.MarkAdminCommandFailed(cmd.Id, "No target"); return; }
                    await _sqlBackend.SetFrozen(target, false);
                    if (session != null)
                    {
                        session.IsFrozen = false;
                        session.EnqueueMessage("\u001b[1;32m  *** The gods have thawed you. You may move again. ***\u001b[0m");
                    }
                    _sqlBackend.MarkAdminCommandExecuted(cmd.Id, $"Thawed {target}");
                    break;

                case "mute":
                    if (target == null) { _sqlBackend.MarkAdminCommandFailed(cmd.Id, "No target"); return; }
                    await _sqlBackend.SetMuted(target, true, "admin-web");
                    if (session != null)
                    {
                        session.IsMuted = true;
                        session.EnqueueMessage("\u001b[1;33m  *** You have been silenced by the gods. ***\u001b[0m");
                    }
                    _sqlBackend.MarkAdminCommandExecuted(cmd.Id, $"Muted {target}");
                    break;

                case "unmute":
                    if (target == null) { _sqlBackend.MarkAdminCommandFailed(cmd.Id, "No target"); return; }
                    await _sqlBackend.SetMuted(target, false);
                    if (session != null)
                    {
                        session.IsMuted = false;
                        session.EnqueueMessage("\u001b[1;32m  *** The gods have restored your voice. ***\u001b[0m");
                    }
                    _sqlBackend.MarkAdminCommandExecuted(cmd.Id, $"Unmuted {target}");
                    break;

                case "slay":
                    if (target == null) { _sqlBackend.MarkAdminCommandFailed(cmd.Id, "No target"); return; }
                    if (session != null)
                    {
                        session.EnqueueMessage("\u001b[1;31m  *** The gods have struck you down! ***\u001b[0m");
                        // The player will die on their next action when HP is checked
                        // We send a force-kill message — the session's game engine will handle death
                    }
                    _sqlBackend.MarkAdminCommandExecuted(cmd.Id, $"Slay command sent to {target}" + (session == null ? " (offline — edit HP via player editor)" : ""));
                    break;

                case "message":
                    if (target == null || string.IsNullOrEmpty(message))
                    {
                        _sqlBackend.MarkAdminCommandFailed(cmd.Id, "Missing target or message");
                        return;
                    }
                    await _sqlBackend.SendMessage("Admin", target, "system", message);
                    if (session != null)
                        session.EnqueueMessage($"\u001b[1;33m  [Admin Message] {message}\u001b[0m");
                    _sqlBackend.MarkAdminCommandExecuted(cmd.Id, $"Message sent to {target}");
                    break;

                case "broadcast":
                    ActiveBroadcast = string.IsNullOrEmpty(message) ? null : message;
                    if (!string.IsNullOrEmpty(message))
                        BroadcastToAll($"\u001b[1;33m  *** {message} ***\u001b[0m");
                    _sqlBackend.MarkAdminCommandExecuted(cmd.Id, string.IsNullOrEmpty(message) ? "Broadcast cleared" : $"Broadcast set: {message}");
                    break;

                case "shutdown":
                    int shutdownSeconds = 120; // Default 2 minutes
                    if (!string.IsNullOrEmpty(cmd.Args))
                    {
                        try
                        {
                            using var sdDoc = JsonDocument.Parse(cmd.Args);
                            if (sdDoc.RootElement.TryGetProperty("seconds", out var secEl))
                                shutdownSeconds = secEl.GetInt32();
                        }
                        catch { }
                    }
                    _sqlBackend.MarkAdminCommandExecuted(cmd.Id, $"Shutdown initiated ({shutdownSeconds}s): {reason ?? "Server update"}");
                    _ = InitiateShutdown(shutdownSeconds, reason ?? "Server update");
                    break;

                case "snoop_start":
                    if (target == null) { _sqlBackend.MarkAdminCommandFailed(cmd.Id, "No target"); return; }
                    if (session?.Context?.Terminal != null)
                    {
                        var snoopTarget = target;
                        session.Context.Terminal.SetDbSnoopCallback(line =>
                            _sqlBackend.WriteSnoopLine(snoopTarget, line));
                        _sqlBackend.MarkAdminCommandExecuted(cmd.Id, $"Snoop started on {target}");
                    }
                    else
                    {
                        _sqlBackend.MarkAdminCommandFailed(cmd.Id, $"Player '{target}' is not online or has no terminal");
                    }
                    break;

                case "snoop_stop":
                    if (target == null) { _sqlBackend.MarkAdminCommandFailed(cmd.Id, "No target"); return; }
                    if (session?.Context?.Terminal != null)
                    {
                        session.Context.Terminal.SetDbSnoopCallback(null);
                        _sqlBackend.MarkAdminCommandExecuted(cmd.Id, $"Snoop stopped on {target}");
                    }
                    else
                    {
                        _sqlBackend.MarkAdminCommandExecuted(cmd.Id, $"Snoop stop for {target} (already offline)");
                    }
                    break;

                default:
                    _sqlBackend.MarkAdminCommandFailed(cmd.Id, $"Unknown command: {cmd.Command}");
                    break;
            }

            // Log all admin commands to the wizard audit log
            _sqlBackend.LogWizardAction("admin-web", cmd.Command, target, cmd.Args);
        }
        catch (Exception ex)
        {
            _sqlBackend.MarkAdminCommandFailed(cmd.Id, $"Error: {ex.Message}");
            Console.Error.WriteLine($"[MUD] Admin command '{cmd.Command}' failed for '{target}': {ex.Message}");
        }
    }
}
