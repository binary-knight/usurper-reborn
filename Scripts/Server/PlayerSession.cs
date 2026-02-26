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

    /// <summary>
    /// The display name for the currently active character. Defaults to Username (account name).
    /// Updated when switching to alt characters so room presence, broadcasts, and "Also here"
    /// display the correct character name instead of the account name.
    /// </summary>
    public string ActiveCharacterName { get; set; }

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

    /// <summary>True once the player has loaded/created a character and entered the game world.
    /// While false, broadcast messages (gossip, shouts, etc.) are suppressed.</summary>
    public bool IsInGame { get; set; }

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

    /// <summary>Sessions currently spectating this player's terminal output.</summary>
    public List<PlayerSession> Spectators { get; } = new();

    /// <summary>The session this player is currently spectating (null if not spectating).</summary>
    public PlayerSession? SpectatingSession { get; set; }

    /// <summary>True while this session is in spectator mode (no game loaded).</summary>
    public bool IsSpectating { get; set; }

    /// <summary>Pending spectate request awaiting this player's /accept or /deny.</summary>
    public SpectateRequest? PendingSpectateRequest { get; set; }

    /// <summary>Pending group invite awaiting this player's /accept or /deny.</summary>
    public GroupInvite? PendingGroupInvite { get; set; }

    /// <summary>True while this session is in the GroupFollowerLoop (passively following the leader).</summary>
    public bool IsGroupFollower { get; set; }

    /// <summary>The session of the group leader this player is following (null if not following).</summary>
    public PlayerSession? GroupLeaderSession { get; set; }

    /// <summary>Commands injected by a wizard via /force. Processed before normal input.</summary>
    public ConcurrentQueue<string> ForcedCommands { get; } = new();

    public PlayerSession(
        string username,
        string connectionType,
        TcpClient tcpClient,
        NetworkStream stream,
        SqlSaveBackend sqlBackend,
        MudServer server,
        CancellationToken cancellationToken,
        bool isPlainText = false)
    {
        Username = username;
        ActiveCharacterName = username;
        ConnectionType = connectionType;
        _tcpClient = tcpClient;
        _stream = stream;
        _sqlBackend = sqlBackend;
        _server = server;
        _serverCancellationToken = cancellationToken;
        _isPlainText = isPlainText;
    }

    private readonly bool _isPlainText;

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
            CharacterKey = Username,  // Default to account name; updated if playing alt character
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

            // Screen-reader / plain text mode (e.g. VIP Mud) — strips ANSI art
            if (_isPlainText)
                ctx.Terminal.IsPlainText = true;

            // ServerEchoes: true only for direct raw-TCP MUD connections (Mudlet, etc.)
            // where we sent IAC WILL ECHO and the client disabled its local echo.
            // SSH relay connections (web terminal, direct SSH) use PTY echo — server
            // must not double-echo or every character appears twice in the terminal.
            ctx.Terminal.ServerEchoes = (ConnectionType == "MUD");

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
            // Emergency save on disconnect — save to main player key so it persists
            try
            {
                var player = ctx.Engine?.CurrentPlayer;
                if (player != null)
                {
                    var saveKey = (Context?.CharacterKey ?? Username).ToLowerInvariant();
                    Console.Error.WriteLine($"[MUD] [{Username}] Performing emergency save (key: {saveKey})...");
                    await SaveSystem.Instance.SaveGame(saveKey, player);
                    Console.Error.WriteLine($"[MUD] [{Username}] Emergency save completed");
                }
            }
            catch (Exception ex)
            {
                Console.Error.WriteLine($"[MUD] [{Username}] Emergency save failed: {ex.Message}");
            }
            // Register as dormitory sleeper if not already sleeping
            // This catches disconnects, crashes, and players who just close the terminal
            try
            {
                var dormKey = (Context?.CharacterKey ?? Username).ToLowerInvariant();
                var sleepInfo = await _sqlBackend.GetSleepingPlayerInfo(dormKey);
                if (sleepInfo == null)
                {
                    // Player wasn't registered as sleeping — force dormitory
                    await _sqlBackend.RegisterSleepingPlayer(dormKey, "dormitory", "[]", 0);
                    Console.Error.WriteLine($"[MUD] [{Username}] Registered as dormitory sleeper (key: {dormKey}, unclean disconnect)");
                }
            }
            catch (Exception ex)
            {
                Console.Error.WriteLine($"[MUD] [{Username}] Failed to register dormitory sleep: {ex.Message}");
            }

            // Notify WizNet of logout
            try
            {
                if (WizardLevel >= WizardLevel.Builder)
                    WizNet.SystemNotify($"{WizardConstants.GetTitle(WizardLevel)} {Username} has disconnected.");
            }
            catch { }

            // Clean up snoop references — remove spectator streams too
            try
            {
                foreach (var snooper in SnoopedBy.ToArray())
                {
                    snooper.EnqueueMessage($"\u001b[90m  [Snoop] {Username} has disconnected.\u001b[0m");
                    // Remove snooper's terminal from our spectator list
                    if (snooper.Context?.Terminal != null)
                        Context?.Terminal?.RemoveSpectatorStream(snooper.Context.Terminal);
                }
                SnoopedBy.Clear();
            }
            catch { }

            // Clean up group references
            try
            {
                var group = GroupSystem.Instance?.GetGroupFor(Username);
                if (group != null)
                {
                    if (group.IsLeader(Username))
                        GroupSystem.Instance?.DisbandGroup(Username, "leader disconnected");
                    else
                        GroupSystem.Instance?.RemoveMember(Username, "disconnected");
                }
                IsGroupFollower = false;
                GroupLeaderSession = null;
                PendingGroupInvite = null;
            }
            catch { }

            // Clean up spectator references
            try
            {
                // Notify anyone spectating us that we disconnected
                foreach (var spectator in Spectators.ToArray())
                {
                    spectator.EnqueueMessage($"\u001b[1;33m  * The player you were watching has disconnected.\u001b[0m");
                    spectator.SpectatingSession = null;
                    spectator.IsSpectating = false;
                }
                ctx.Terminal?.ClearSpectatorStreams();
                Spectators.Clear();

                // If we were spectating someone, remove ourselves
                if (SpectatingSession != null)
                {
                    SpectatingSession.Spectators.Remove(this);
                    SpectatingSession.Context?.Terminal?.RemoveSpectatorStream(this);
                    SpectatingSession.EnqueueMessage(
                        $"\u001b[1;33m  * {Username} stopped watching your session.\u001b[0m");
                    SpectatingSession = null;
                    IsSpectating = false;
                }
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

        // Close the TCP connection so any blocking ReadLineAsync() unblocks
        try { _stream.Close(); } catch { }
        try { _tcpClient.Close(); } catch { }
    }

    public void Dispose()
    {
        _sessionCts?.Dispose();
        try { _tcpClient.Close(); } catch { }
    }
}

/// <summary>
/// A pending spectate request from one player to another.
/// The requester waits on Response.Task; the target resolves it via /accept or /deny.
/// </summary>
public class SpectateRequest
{
    public PlayerSession Requester { get; init; } = null!;
    public TaskCompletionSource<bool> Response { get; } = new();
    public DateTime RequestedAt { get; } = DateTime.UtcNow;
    /// <summary>Auto-expire after 60 seconds.</summary>
    public bool IsExpired => (DateTime.UtcNow - RequestedAt).TotalSeconds > 60;
}
