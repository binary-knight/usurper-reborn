using System;
using System.Collections.Concurrent;
using System.IO;
using System.Net.Sockets;
using System.Threading;
using System.Threading.Tasks;
using UsurperRemake.BBS;
using UsurperRemake.Systems;

namespace UsurperRemake.Server;

/// <summary>
/// Represents a single player's game session inside the MUD server.
/// Creates a SessionContext with isolated per-player system instances,
/// sets it on the current async flow via AsyncLocal, then runs the
/// standard game loop (RunBBSDoorMode).
///
/// All SomeSystem.Instance calls within this async flow automatically
/// resolve to this session's instances via the SessionContext shim.
/// </summary>
public class PlayerSession : IDisposable
{
    public string Username { get; }
    public string ConnectionType { get; }

    private readonly TcpClient _tcpClient;
    private readonly NetworkStream _stream;
    private readonly SqlSaveBackend _sqlBackend;
    private readonly MudServer _server;
    private readonly CancellationToken _serverCancellationToken;
    private CancellationTokenSource? _sessionCts;

    /// <summary>Incoming async messages (chat, room events, etc.) to display at next prompt.</summary>
    public ConcurrentQueue<string> IncomingMessages { get; } = new();

    /// <summary>The SessionContext for this player (set during RunAsync).</summary>
    public SessionContext? Context { get; private set; }

    /// <summary>Last time the player sent any input. Used for idle timeout detection.</summary>
    public DateTime LastActivityTime { get; set; } = DateTime.UtcNow;

    /// <summary>True if this player has admin privileges. Computed from WizardLevel.</summary>
    public bool IsAdmin => WizardLevel >= WizardLevel.Archwizard;

    /// <summary>This player's wizard level (Mortal=0 through Implementor=6).</summary>
    public WizardLevel WizardLevel { get; set; } = WizardLevel.Mortal;

    /// <summary>When true, wizard takes no combat damage.</summary>
    public bool WizardGodMode { get; set; }

    /// <summary>When true, wizard is hidden from /who, room presence, and arrival/departure messages.</summary>
    public bool IsWizInvisible { get; set; }

    /// <summary>When true, player cannot execute any commands.</summary>
    public bool IsFrozen { get; set; }

    /// <summary>When true, player cannot use chat commands (/say, /shout, /tell, /emote).</summary>
    public bool IsMuted { get; set; }

    /// <summary>Wizards currently snooping this session's output.</summary>
    public List<PlayerSession> SnoopedBy { get; } = new();

    /// <summary>Commands injected by a wizard via /force. Processed before normal input.</summary>
    public ConcurrentQueue<string> ForcedCommands { get; } = new();

    public PlayerSession(
        string username,
        string connectionType,
        TcpClient tcpClient,
        NetworkStream stream,
        SqlSaveBackend sqlBackend,
        MudServer server,
        CancellationToken cancellationToken)
    {
        Username = username;
        ConnectionType = connectionType;
        _tcpClient = tcpClient;
        _stream = stream;
        _sqlBackend = sqlBackend;
        _server = server;
        _serverCancellationToken = cancellationToken;
    }

    /// <summary>
    /// Run the game loop for this player session. Blocks until the player
    /// disconnects, quits, or the server shuts down.
    /// </summary>
    public async Task RunAsync()
    {
        _sessionCts = CancellationTokenSource.CreateLinkedTokenSource(_serverCancellationToken);

        // Create the per-session context
        using var ctx = new SessionContext
        {
            InputStream = _stream,
            OutputStream = _stream,
            Username = Username,
            ConnectionType = ConnectionType,
            CancellationToken = _sessionCts.Token
        };
        Context = ctx;

        // Set AsyncLocal so all Instance properties resolve to this session
        SessionContext.Current = ctx;

        try
        {
            // Create per-session terminal backed by the TCP stream
            ctx.Terminal = new TerminalEmulator(_stream, _stream);

            // Enable real-time message delivery: terminal polls this during GetInput()
            ctx.Terminal.MessageSource = () =>
                IncomingMessages.TryDequeue(out var msg) ? msg : null;

            // Initialize all per-session story/mechanics systems
            ctx.InitializeSystems();

            // Configure DoorMode flags for this session's online behavior
            // The game checks DoorMode.IsOnlineMode in many places
            DoorMode.SetOnlineUsername(Username);

            // Initialize the shared save backend (already done at server level,
            // but ensure the save system knows about it for this session)
            SaveSystem.InitializeWithBackend(_sqlBackend);

            // Initialize OnlineStateManager for this session
            OnlineStateManager.Initialize(_sqlBackend, Username);

            // Load wizard level from database
            ctx.WizardLevel = await _sqlBackend.GetWizardLevel(Username);
            this.WizardLevel = ctx.WizardLevel;

            // Auto-promote Rage to Implementor on every login
            if (Username.Equals(WizardConstants.IMPLEMENTOR_USERNAME, StringComparison.OrdinalIgnoreCase))
            {
                if (ctx.WizardLevel < WizardLevel.Implementor)
                {
                    await _sqlBackend.SetWizardLevel(Username, WizardLevel.Implementor);
                    ctx.WizardLevel = WizardLevel.Implementor;
                    this.WizardLevel = WizardLevel.Implementor;
                    Console.Error.WriteLine($"[MUD] [{Username}] Auto-promoted to Implementor");
                }
            }

            // Load freeze/mute flags
            (this.IsFrozen, this.IsMuted) = await _sqlBackend.GetWizardFlags(Username);

            if (ctx.WizardLevel > WizardLevel.Mortal)
                Console.Error.WriteLine($"[MUD] [{Username}] Wizard level: {WizardConstants.GetTitle(ctx.WizardLevel)}");

            // Initialize chat system
            OnlineChatSystem.Initialize(OnlineStateManager.Instance!);

            // Start online presence tracking
            await OnlineStateManager.Instance!.StartOnlineTracking(Username, ConnectionType);

            // Create a per-session GameEngine that uses the session's terminal
            var engine = new GameEngine();
            ctx.Engine = engine;

            // Create per-session LocationManager
            var locManager = new LocationManager(ctx.Terminal);
            ctx.LocationManager = locManager;

            Console.Error.WriteLine($"[MUD] [{Username}] Session systems initialized, entering game loop");

            // Notify WizNet of wizard login
            if (WizardLevel >= WizardLevel.Builder)
                WizNet.SystemNotify($"{WizardConstants.GetTitle(WizardLevel)} {Username} has connected.");

            // Global login announcement (suppress for invisible wizards)
            if (!IsWizInvisible)
            {
                _server.BroadcastToAll(
                    $"\u001b[1;33m  {Username} has entered the realm. [{ConnectionType}]\u001b[0m",
                    excludeUsername: Username);
            }

            // Run the standard BBS door mode game loop
            // This is the same path SSH players use today, but now backed by
            // the TCP stream instead of Console stdin/stdout
            await GameEngine.RunConsoleAsync();
        }
        catch (OperationCanceledException)
        {
            Console.Error.WriteLine($"[MUD] [{Username}] Session cancelled");
        }
        catch (IOException ex)
        {
            Console.Error.WriteLine($"[MUD] [{Username}] Connection lost: {ex.Message}");
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine($"[MUD] [{Username}] Session error: {ex}");
        }
        finally
        {
            // Emergency save on disconnect
            try
            {
                var player = ctx.Engine?.CurrentPlayer;
                if (player != null)
                {
                    Console.Error.WriteLine($"[MUD] [{Username}] Performing emergency save...");
                    await SaveSystem.Instance.SaveGame($"emergency_{Username.ToLowerInvariant()}", player);
                    Console.Error.WriteLine($"[MUD] [{Username}] Emergency save completed");
                }
            }
            catch (Exception ex)
            {
                Console.Error.WriteLine($"[MUD] [{Username}] Emergency save failed: {ex.Message}");
            }
            // Notify WizNet of logout
            try
            {
                if (WizardLevel >= WizardLevel.Builder)
                    WizNet.SystemNotify($"{WizardConstants.GetTitle(WizardLevel)} {Username} has disconnected.");
            }
            catch { }

            // Clean up snoop references
            try
            {
                foreach (var snooper in SnoopedBy.ToArray())
                {
                    snooper.EnqueueMessage($"\u001b[90m  [Snoop] {Username} has disconnected.\u001b[0m");
                }
                SnoopedBy.Clear();
            }
            catch { }

            // Global logout announcement (suppress for invisible wizards)
            try
            {
                if (!IsWizInvisible)
                {
                    _server.BroadcastToAll(
                        $"\u001b[1;33m  {Username} has left the realm.\u001b[0m",
                        excludeUsername: Username);
                }
            }
            catch { }

            // Remove from room registry
            try
            {
                RoomRegistry.Instance?.PlayerDisconnected(this);
            }
            catch { }

            // Clean up online tracking (use session-local references, not static singleton)
            try
            {
                var sessionChat = ctx.OnlineChat;
                var sessionOsm = ctx.OnlineState;

                sessionChat?.Shutdown();

                if (sessionOsm != null)
                    await sessionOsm.Shutdown();
            }
            catch (Exception ex)
            {
                Console.Error.WriteLine($"[MUD] [{Username}] Cleanup error: {ex.Message}");
            }

            // Clear the AsyncLocal so this context doesn't leak
            SessionContext.Current = null;
            Context = null;

            Console.Error.WriteLine($"[MUD] [{Username}] Session fully cleaned up");
        }
    }

    /// <summary>Enqueue a message to be displayed at the player's next input prompt.</summary>
    public void EnqueueMessage(string message)
    {
        IncomingMessages.Enqueue(message);
    }

    /// <summary>Gracefully disconnect this session with a message.</summary>
    public async Task DisconnectAsync(string reason)
    {
        try
        {
            if (_tcpClient.Connected && Context?.Terminal != null)
            {
                Context.Terminal.WriteLine("");
                Context.Terminal.SetColor("bright_red");
                Context.Terminal.WriteLine($"  {reason}");
                Context.Terminal.SetColor("white");
                await Task.Delay(500);
            }
        }
        catch { }

        _sessionCts?.Cancel();
    }

    public void Dispose()
    {
        _sessionCts?.Dispose();
        try { _tcpClient.Close(); } catch { }
    }
}
