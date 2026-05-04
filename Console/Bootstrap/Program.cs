using System;
using System.Runtime.InteropServices;
using System.Threading;
using System.Threading.Tasks;
using UsurperRemake;
using UsurperRemake.Systems;
using UsurperRemake.BBS;
using UsurperRemake.Server;

// Console bootstrapper for Usurper Reborn
//
// BBS DOOR MODE: Use command-line arguments to run as a BBS door:
//   --door <path>     Load drop file (auto-detect DOOR32.SYS or DOOR.SYS)
//   --door32 <path>   Load DOOR32.SYS explicitly
//   --node <dir>      Search node directory for drop files
//   --local           Run in local mode (no BBS connection)

namespace UsurperConsole
{
    internal static class Program
    {
        // Windows console handler delegate
        private delegate bool ConsoleCtrlHandlerDelegate(int sig);
        private static ConsoleCtrlHandlerDelegate? _handler;

        [DllImport("Kernel32")]
        private static extern bool SetConsoleCtrlHandler(ConsoleCtrlHandlerDelegate handler, bool add);

        // Native ExitProcess — terminates without running .NET finalizers.
        // Prevents Socket finalizers from calling closesocket() on inherited BBS handles.
        [DllImport("kernel32.dll", EntryPoint = "ExitProcess")]
        private static extern void NativeExitProcess(uint uExitCode);

        // Windows API for enabling ANSI escape code processing in cmd.exe/PowerShell
        [DllImport("kernel32.dll", SetLastError = true)]
        private static extern IntPtr GetStdHandle(int nStdHandle);
        [DllImport("kernel32.dll", SetLastError = true)]
        private static extern bool GetConsoleMode(IntPtr hConsoleHandle, out uint lpMode);
        [DllImport("kernel32.dll", SetLastError = true)]
        private static extern bool SetConsoleMode(IntPtr hConsoleHandle, uint dwMode);
        private const int STD_OUTPUT_HANDLE = -11;
        private const uint ENABLE_VIRTUAL_TERMINAL_PROCESSING = 0x0004;

        // Console control signal types
        private const int CTRL_C_EVENT = 0;
        private const int CTRL_BREAK_EVENT = 1;
        private const int CTRL_CLOSE_EVENT = 2;
        private const int CTRL_LOGOFF_EVENT = 5;
        private const int CTRL_SHUTDOWN_EVENT = 6;

        private static bool _exitRequested = false;

        static async Task Main(string[] args)
        {
            // Enable ANSI escape code processing on Windows cmd.exe/PowerShell.
            // Without this, raw ANSI sequences (splash screen, ANSI art portraits)
            // display as garbled text like "[1;31m" instead of rendering colors.
            // WezTerm handles this natively, but users running the exe directly need it.
            EnableWindowsAnsiSupport();

            // Defense-in-depth UTF-8 setup. The MUD server writes player-facing
            // bytes via explicit `Encoding.UTF8.GetBytes()` calls on the network
            // stream, but anything that touches Console.Out / stdout (relay
            // forwarding, debug logging, error fallback paths) inherits whatever
            // the OS default is. On Linux systemd units without a UTF-8 locale
            // set (LANG=C.UTF-8 / en_US.UTF-8), Console.OutputEncoding falls
            // back to ASCII, which silently truncates accented characters in
            // French/Spanish/Italian translated strings. Force UTF-8 explicitly
            // here unless we're in BBS-door-stdio mode (where DoorMode.cs may
            // need to set CP437 instead). This is idempotent and safe to call
            // before mode-specific overrides further down the stack.
            try
            {
                if (!args.Contains("--door") && !args.Contains("--door32") && !args.Contains("--doorsys"))
                {
                    Console.OutputEncoding = System.Text.Encoding.UTF8;
                    Console.InputEncoding = System.Text.Encoding.UTF8;
                }
            }
            catch { /* Best effort. Never block startup on encoding setup */ }

            // Enable System.Text.Json reflection-based serialization.
            // The trimmer sets IsReflectionEnabledByDefault=false in runtimeconfig,
            // but we use reflection-based JsonSerializer.Deserialize<T>() everywhere.
            // Must be called before any JSON operations.
            AppContext.SetSwitch("System.Text.Json.JsonSerializer.IsReflectionEnabledByDefault", true);

            // Set up global exception handlers FIRST so we catch everything
            SetupGlobalExceptionHandlers();

            // Initialize localization system (loads language JSON files)
            UsurperRemake.Systems.Loc.Initialize();

            // Initialize moddable game data loader (loads GameData/ JSON overrides if present)
            UsurperRemake.Systems.GameDataLoader.Initialize();

            // Handle --export-data flag: export all default game data to GameData/ directory and exit
            if (args.Contains("--export-data"))
            {
                var outputDir = System.IO.Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "GameData");
                UsurperRemake.Systems.GameDataLoader.ExportDefaults(outputDir);
                Console.WriteLine($"Default game data exported to: {outputDir}");
                return;
            }

            // --editor launches the standalone USEDIT-analog editor. Analogous to the
            // DOS-era companion tool that shipped alongside the original Usurper.
            // Edits GameData/*.json + save files; does NOT start any game systems
            // or network stack, so it's safe to run even if a server process is
            // live (though editing a save while the game holds it is not
            // recommended — the editor warns about this).
            if (args.Contains("--editor") || args.Contains("--usedit"))
            {
                var exitCode = await UsurperRemake.Editor.EditorMain.RunAsync(args);
                System.Environment.Exit(exitCode);
                return;
            }

            // Handle --version flag: print version and exit immediately
            if (args.Length > 0 && (args[0] == "--version" || args[0] == "-v" || args[0] == "-V"))
            {
                Console.WriteLine($"Usurper Reborn v{GameConfig.Version}");
                return;
            }

            // Initialize Steam integration early — before door mode branch,
            // so Steam works for both --local (WezTerm/accessible launchers) and standard mode.
            SteamIntegration.Initialize();

            // Check for BBS door mode arguments
            bool useDoorMode = false;
            if (args.Length > 0)
            {
                if (DoorMode.ParseCommandLineArgs(args))
                {
                    useDoorMode = true;
                }
                else if (DoorMode.HelpWasShown)
                {
                    return;
                }
            }

            // v0.60.0, issue #92 (fastfinge): auto-enable screen-reader mode if a
            // screen reader is running on the host. Windows queries SPI_GETSCREENREADER,
            // which NVDA / JAWS / Narrator / most others set while active.
            //
            // Order matters: this runs AFTER ParseCommandLineArgs (so an explicit
            // `--screen-reader` flag sets ScreenReaderMode first and our `if`
            // short-circuits, no announcement) but BEFORE branching into door-mode
            // or standard-console flows so both paths benefit equally. The WezTerm
            // Steam launcher passes `--local` which routes through RunDoorModeAsync,
            // so injecting only into the standard branch would have missed the
            // most common Steam install.
            //
            // Skip the announcement in ANY door mode. Door modes always run on
            // a separate machine from the player (BBS host, MUD server, relay)
            // and the SPI_GETSCREENREADER flag we'd be reading is from THAT
            // machine's Windows session, not the remote player's. Inheriting
            // the host's flag bumped real BBS players into accessible mode
            // because the BBS sysop's machine had the SR flag stuck on (the
            // flag can be left set by browsers, accessibility tools, or apps
            // that crashed without clearing it). Door players who actually
            // need accessible mode either: (a) have it negotiated via TTYPE
            // in their MUD client, (b) toggle it in the in-game preferences
            // menu, or (c) launch via Play-Accessible.bat which passes
            // --screen-reader explicitly. The auto-detect is only useful for
            // the standard console launch where the player is genuinely on
            // the local Windows session that the API queries.
            // Saved characters' explicit ScreenReaderMode still wins on character
            // load via GameConfig.ScreenReaderMode = player.ScreenReaderMode in
            // GameEngine.LoadSaveByFileName, so this only sets the pre-character
            // default and the inheritance for fresh characters.
            bool isRemoteOrHeadlessMode = useDoorMode;
            // v0.60.5: skip auto-detect when running inside WezTerm. WezTerm is what
            // Play.bat / play.sh (the default Steam launcher) wraps; screen reader
            // users on Steam are expected to use Play-Accessible.bat which passes
            // --screen-reader explicitly. SPI_GETSCREENREADER is unreliable on
            // Windows -- the flag can be left set by browsers, accessibility tools,
            // or apps that crashed without clearing it -- so a Steam user launching
            // the normal way was getting flipped into accessible mode despite no
            // screen reader actually running. Player report: "Screen reader detected"
            // showing on a clean Steam + WezTerm launch with no SR active.
            // TERM_PROGRAM=WezTerm is set by WezTerm for all child processes.
            bool isWezTerm = string.Equals(
                Environment.GetEnvironmentVariable("TERM_PROGRAM"),
                "WezTerm",
                StringComparison.OrdinalIgnoreCase);
            if (!isRemoteOrHeadlessMode && !isWezTerm && !GameConfig.ScreenReaderMode &&
                UsurperRemake.UI.AccessibilityDetection.IsScreenReaderActive())
            {
                GameConfig.ScreenReaderMode = true;
                Console.WriteLine("Screen reader detected. Accessible mode enabled automatically.");
                Console.WriteLine("(Disable in-game via the Preferences menu if you don't want this.)");
                Console.WriteLine();
            }

            if (useDoorMode)
            {
                await RunDoorModeAsync();
                return;
            }

            // Standard console mode
            // Set up console close handlers
            SetupConsoleCloseHandlers();

            Console.WriteLine("Launching Usurper Reborn – Console Mode");

            try
            {
                // Spin up the full engine in console mode.
                await GameEngine.RunConsoleAsync();
            }
            finally
            {
                // Shutdown Steam integration
                SteamIntegration.Shutdown();
            }
        }

        /// <summary>
        /// Set up global exception handlers to log all unhandled exceptions to debug.log
        /// </summary>
        private static void SetupGlobalExceptionHandlers()
        {
            // Handle unhandled exceptions on any thread
            AppDomain.CurrentDomain.UnhandledException += (sender, e) =>
            {
                var ex = e.ExceptionObject as Exception;
                var message = ex?.ToString() ?? e.ExceptionObject?.ToString() ?? "Unknown exception";

                // Log to debug file
                DebugLogger.Instance.LogError("CRASH", $"Unhandled exception (IsTerminating={e.IsTerminating}):\n{message}");
                DebugLogger.Instance.Flush(); // Force immediate write

                // Also write to stderr
                Console.Error.WriteLine($"[CRASH] Unhandled exception: {message}");
            };

            // Handle unobserved task exceptions (async exceptions that weren't awaited)
            TaskScheduler.UnobservedTaskException += (sender, e) =>
            {
                var message = e.Exception?.ToString() ?? "Unknown task exception";

                // Log to debug file
                DebugLogger.Instance.LogError("CRASH", $"Unobserved task exception:\n{message}");
                DebugLogger.Instance.Flush(); // Force immediate write

                // Also write to stderr
                Console.Error.WriteLine($"[CRASH] Unobserved task exception: {message}");

                // Mark as observed to prevent crash
                e.SetObserved();
            };
        }

        /// <summary>
        /// Run the game in BBS door mode
        /// </summary>
        private static async Task RunDoorModeAsync()
        {
            // Declare variables used across try/finally before the try block
            SqlSaveBackend? sqlBackend = null;
            CancellationTokenSource? embeddedWorldSimCts = null;
            Task? embeddedWorldSimTask = null;
            string? worldSimOwnerId = null;

            try
            {
                // World sim mode: bypass all terminal/auth/player logic
                if (DoorMode.IsWorldSimMode)
                {
                    await RunWorldSimMode();
                    return;
                }

                // MUD server mode: start the TCP game server
                if (DoorMode.IsMudServerMode)
                {
                    await RunMudServerMode();
                    return;
                }

                // MUD relay mode: thin stdin/stdout ↔ TCP bridge
                if (DoorMode.IsMudRelayMode)
                {
                    await RunMudRelayMode();
                    return;
                }

                DoorMode.Log("Initializing BBS door mode...");

                // If online mode is active, initialize SqlSaveBackend early (before auth)
                if (DoorMode.IsOnlineMode)
                {
                    DoorMode.Log($"Online mode: initializing SQLite backend at {DoorMode.OnlineDatabasePath}");
                    sqlBackend = new SqlSaveBackend(DoorMode.OnlineDatabasePath);
                    SaveSystem.InitializeWithBackend(sqlBackend);
                    DoorMode.Log("SQLite save backend initialized");
                }

                // Initialize the terminal adapter
                var terminal = DoorMode.InitializeTerminal();
                if (terminal == null)
                {
                    DoorMode.Log("Failed to initialize terminal - aborting");
                    return;
                }

                // BBS Online mode: use BBS-authenticated username directly (skip in-game auth)
                // The BBS has already verified the user's identity via its own login system.
                if (DoorMode.IsOnlineMode && DoorMode.IsInDoorMode && string.IsNullOrEmpty(DoorMode.OnlineUsername))
                {
                    var bbsUsername = DoorMode.GetPlayerName();
                    DoorMode.SetOnlineUsername(bbsUsername);
                    DoorMode.Log($"BBS Online mode: using BBS-authenticated username '{bbsUsername}'");
                }

                // If online mode with no --user flag (and not BBS-authenticated), show in-game auth screen
                if (DoorMode.IsOnlineMode && string.IsNullOrEmpty(DoorMode.OnlineUsername) && sqlBackend != null)
                {
                    DoorMode.Log("No --user flag, showing in-game auth screen...");
                    var authScreen = new OnlineAuthScreen(sqlBackend, terminal);
                    var authenticatedUser = await authScreen.RunAsync();

                    if (string.IsNullOrEmpty(authenticatedUser))
                    {
                        DoorMode.Log("Player did not authenticate - disconnecting");
                        terminal.SetColor("gray");
                        terminal.WriteLine("  Goodbye!");
                        // Force exit - Console.ReadLine background threads may prevent clean shutdown
                        await Task.Delay(500);
                        Environment.Exit(0);
                        return;
                    }

                    // Set the authenticated username
                    DoorMode.SetOnlineUsername(authenticatedUser);
                    DoorMode.Log($"Player authenticated as: '{authenticatedUser}'");
                }

                // Now initialize OnlineStateManager with the (possibly auth-derived) username
                if (DoorMode.IsOnlineMode && sqlBackend != null)
                {
                    var onlineUsername = DoorMode.OnlineUsername ?? "anonymous";

                    // If player appears online from a stale/crashed session, clear it and continue
                    if (await sqlBackend.IsPlayerOnline(onlineUsername))
                    {
                        DoorMode.Log($"Player '{onlineUsername}' has stale session - clearing for reconnect");
                        await sqlBackend.UnregisterOnline(onlineUsername);
                        terminal.SetColor("yellow");
                        terminal.WriteLine("  Previous session disconnected.");
                        terminal.SetColor("white");
                        terminal.WriteLine("");
                    }

                    OnlineStateManager.Initialize(sqlBackend, onlineUsername);
                    DoorMode.Log($"Online state manager initialized for '{onlineUsername}'");

                    // Initialize inter-player chat system
                    OnlineChatSystem.Initialize(OnlineStateManager.Instance!);
                    DoorMode.Log("Online chat system initialized");

                    // Update session login timestamp
                    await sqlBackend.UpdatePlayerSession(onlineUsername, isLogin: true);

                    // Detect connection type for Who's Online display
                    // (presence tracking deferred to after character load in GameEngine.LoadSaveByFileName)
                    var connectionType = DetectConnectionType();
                    DoorMode.Log($"Connection type detected: {connectionType}");
                    OnlineStateManager.Instance!.DeferredConnectionType = connectionType;
                }

                // BBS Online mode: start embedded world simulator if no other is running
                if (DoorMode.IsOnlineMode && DoorMode.IsInDoorMode && !DoorMode.NoAutoWorldSim && sqlBackend != null)
                {
                    worldSimOwnerId = $"bbs_{Environment.ProcessId}_{DateTime.UtcNow.Ticks}";

                    if (sqlBackend.TryAcquireWorldSimLock(worldSimOwnerId))
                    {
                        DoorMode.Log($"Acquired worldsim lock — this session will host the world simulator");

                        embeddedWorldSimCts = new CancellationTokenSource();
                        var worldSimService = new WorldSimService(
                            sqlBackend,
                            simIntervalSeconds: DoorMode.SimIntervalSeconds,
                            npcXpMultiplier: DoorMode.NpcXpMultiplier,
                            saveIntervalMinutes: DoorMode.SaveIntervalMinutes,
                            heartbeatOwnerId: worldSimOwnerId
                        );

                        // Start worldsim on background thread
                        embeddedWorldSimTask = Task.Run(() => worldSimService.RunAsync(embeddedWorldSimCts.Token));

                        // Wait for initialization to complete (NPCs loaded, systems ready)
                        // Timeout after 30 seconds to prevent hanging if something goes wrong
                        var initTask = worldSimService.InitializationComplete.Task;
                        if (await Task.WhenAny(initTask, Task.Delay(30000)) == initTask)
                        {
                            var success = await initTask;
                            if (success)
                                DoorMode.Log("Embedded world simulator initialized successfully");
                            else
                                DoorMode.Log("Embedded world simulator initialization failed");
                        }
                        else
                        {
                            DoorMode.Log("WARNING: World simulator initialization timed out (30s)");
                        }
                    }
                    else
                    {
                        DoorMode.Log("Another session is hosting the world simulator — skipping");
                    }
                }

                var sessionInfo = DoorMode.SessionInfo;
                if (sessionInfo != null)
                {
                    DoorMode.Log($"Session: {sessionInfo.UserName} from {sessionInfo.BBSName}");
                    DoorMode.Log($"Connection: {sessionInfo.CommType}, Node: {sessionInfo.NodeNumber}");
                }

                // Set up console close handlers (for local mode fallback)
                SetupConsoleCloseHandlers();

                // Run the game engine in door mode
                // The terminal adapter will handle all I/O
                await GameEngine.RunConsoleAsync();
            }
            catch (Exception ex)
            {
                DoorMode.Log($"Door mode error: {ex.Message}");
                UsurperRemake.Systems.DebugLogger.Instance.LogError("BBS", $"Door mode exception: {ex}");
            }
            finally
            {
                // All cleanup wrapped in a hard 5-second timeout.
                // If ANY async operation hangs (SQLite lock, dead socket, etc.),
                // we MUST still reach Environment.Exit(0) or the BBS thinks the door
                // is still running and the user gets stuck.
                var cleanupTask = Task.Run(async () =>
                {
                    try
                    {
                        // Shut down embedded world simulator if this session was hosting it
                        if (embeddedWorldSimCts != null && embeddedWorldSimTask != null)
                        {
                            DoorMode.Log("Shutting down embedded world simulator...");
                            embeddedWorldSimCts.Cancel();
                            try
                            {
                                await Task.WhenAny(embeddedWorldSimTask, Task.Delay(3000));
                            }
                            catch { }
                            embeddedWorldSimCts.Dispose();
                            DoorMode.Log("Embedded world simulator stopped");
                        }
                        else if (worldSimOwnerId != null && sqlBackend != null)
                        {
                            sqlBackend.ReleaseWorldSimLock(worldSimOwnerId);
                        }

                        // Register as dormitory sleeper if not already sleeping (online mode only)
                        if (DoorMode.IsOnlineMode && SaveSystem.Instance?.Backend is SqlSaveBackend sleepBackend)
                        {
                            try
                            {
                                var username = DoorMode.OnlineUsername;
                                if (!string.IsNullOrEmpty(username))
                                {
                                    var sleepInfo = await sleepBackend.GetSleepingPlayerInfo(username);
                                    if (sleepInfo == null)
                                    {
                                        await sleepBackend.RegisterSleepingPlayer(username, "dormitory", "[]", 0);
                                        DoorMode.Log($"Registered '{username}' as dormitory sleeper (unclean disconnect)");
                                    }
                                }
                            }
                            catch (Exception ex)
                            {
                                DoorMode.Log($"Failed to register dormitory sleep: {ex.Message}");
                            }
                        }

                        // Shutdown chat system
                        if (OnlineChatSystem.IsActive)
                        {
                            try { OnlineChatSystem.Instance!.Shutdown(); } catch { }
                        }

                        // Shutdown online state manager (heartbeat, tracking, session)
                        if (OnlineStateManager.IsActive)
                        {
                            DoorMode.Log("Shutting down online state manager...");
                            try { await OnlineStateManager.Instance!.Shutdown(); } catch { }
                        }
                    }
                    catch (Exception ex)
                    {
                        DoorMode.Log($"Cleanup error: {ex.Message}");
                    }
                });

                // Wait up to 5 seconds for cleanup, then force exit regardless
                cleanupTask.Wait(5000);

                DoorMode.Log("Shutting down door mode...");
                DoorMode.Shutdown();
                SteamIntegration.Shutdown();

                // v0.54.7 (Mystic/EleBBS fix): when we own an inherited TCP socket
                // handle from DOOR32.SYS, Environment.Exit(0) runs .NET Socket
                // finalizers which call closesocket()/Shutdown() on that handle —
                // killing the BBS's TCP connection and preventing relaunch without
                // a full reconnect. NativeExitProcess bypasses all finalizers so the
                // OS just closes handles cleanly without emitting TCP shutdown.
                //
                // v0.57.10 (issue #75 — Renegade + NFU): that guard was too broad.
                // In stdio mode (NFU, Synchronet, MUD relay, auto-detected pipe I/O)
                // we don't own any socket handle — stdin/stdout are pipes owned by
                // the parent. NativeExitProcess tears those pipes down before .NET
                // flushes stdout, so the parent sees a truncated stream instead of
                // a clean EOF. NFU in particular hangs its FOSSIL emulation, the
                // NTVDM subsystem locks up, and the node becomes unusable until the
                // host reboots.
                //
                // Fix: only take the NativeExitProcess path when we actually have
                // an inherited socket to protect. Everything else (stdio, non-door,
                // non-Windows) gets a proper Console.Out.Flush() and normal
                // Environment.Exit() so parents get a clean EOF.
                if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows) && DoorMode.IsNativeSocketMode)
                {
                    NativeExitProcess(0);
                }
                else
                {
                    try { Console.Out.Flush(); } catch { }
                    try { Console.Error.Flush(); } catch { }
                    Environment.Exit(0);
                }
            }
        }

        /// <summary>
        /// Run the MUD game server: single process, all players connect via TCP.
        /// </summary>
        private static async Task RunMudServerMode()
        {
            Console.Error.WriteLine($"[MUD] Starting Usurper Reborn MUD Server v{GameConfig.Version}");
            Console.Error.WriteLine($"[MUD] Port: {DoorMode.MudPort}, Database: {DoorMode.OnlineDatabasePath}");

            // Set up cancellation for graceful shutdown
            using var cts = new CancellationTokenSource();

            Console.CancelKeyPress += (sender, e) =>
            {
                e.Cancel = true;
                Console.Error.WriteLine("[MUD] Shutdown signal received (Ctrl+C)...");
                // v0.60.0 alpha audit: guard against ObjectDisposedException.
                // The using-block can dispose cts before this handler fires
                // during shutdown, causing a fatal core-dump on the way out.
                try { cts.Cancel(); } catch (ObjectDisposedException) { }
            };
            AppDomain.CurrentDomain.ProcessExit += (sender, e) =>
            {
                if (!cts.IsCancellationRequested)
                {
                    Console.Error.WriteLine("[MUD] Process exit signal received...");
                    try { cts.Cancel(); } catch (ObjectDisposedException) { }
                }
            };

            var server = new MudServer(DoorMode.MudPort, DoorMode.OnlineDatabasePath);

            // Add admin users from --admin flags (bootstrapped as God in DB on startup)
            foreach (var admin in DoorMode.MudAdminUsers)
            {
                server.BootstrapAdminUsers.Add(admin);
                Console.Error.WriteLine($"[MUD] Bootstrap admin: {admin}");
            }

            await server.RunAsync(cts.Token);
        }

        /// <summary>
        /// Run the thin relay client: bridges stdin/stdout to the MUD server TCP port.
        /// Used as SSH ForceCommand target.
        /// </summary>
        private static async Task RunMudRelayMode()
        {
            var username = DoorMode.OnlineUsername ?? "anonymous";
            var connectionType = DetectConnectionType();

            Console.Error.WriteLine($"[RELAY] Connecting to MUD server on port {DoorMode.MudPort} as '{username}' ({connectionType})");

            using var cts = new CancellationTokenSource();

            Console.CancelKeyPress += (sender, e) =>
            {
                e.Cancel = true;
                cts.Cancel();
            };

            await RelayClient.RunAsync(username, DoorMode.MudPort, connectionType, cts.Token);
        }

        /// <summary>
        /// Run the headless 24/7 world simulator service.
        /// No terminal, no auth, no player tracking.
        /// </summary>
        private static async Task RunWorldSimMode()
        {
            DebugLogger.Instance.LogInfo("WORLDSIM", "Initializing persistent world simulator...");

            // Initialize SQLite backend
            var sqlBackend = new SqlSaveBackend(DoorMode.OnlineDatabasePath);
            DebugLogger.Instance.LogInfo("WORLDSIM", $"Database: {DoorMode.OnlineDatabasePath}");

            // Set up cancellation for graceful shutdown
            using var cts = new CancellationTokenSource();

            // Handle SIGTERM (systemd stop) and SIGINT (Ctrl+C)
            Console.CancelKeyPress += (sender, e) =>
            {
                e.Cancel = true; // Prevent immediate exit
                DebugLogger.Instance.LogInfo("WORLDSIM", "Shutdown signal received (Ctrl+C)...");
                cts.Cancel();
            };
            AppDomain.CurrentDomain.ProcessExit += (sender, e) =>
            {
                if (!cts.IsCancellationRequested)
                {
                    DebugLogger.Instance.LogInfo("WORLDSIM", "Process exit signal received...");
                    cts.Cancel();
                }
            };

            // Create and run the world sim service
            var service = new WorldSimService(
                sqlBackend,
                simIntervalSeconds: DoorMode.SimIntervalSeconds,
                npcXpMultiplier: DoorMode.NpcXpMultiplier,
                saveIntervalMinutes: DoorMode.SaveIntervalMinutes
            );

            await service.RunAsync(cts.Token);
        }

        /// <summary>
        /// Enable Virtual Terminal Processing on Windows so cmd.exe/PowerShell
        /// render ANSI escape codes as colors instead of printing them as text.
        /// Safe no-op on Linux/macOS (terminals support ANSI natively).
        /// </summary>
        private static void EnableWindowsAnsiSupport()
        {
            if (!RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
                return;

            try
            {
                var handle = GetStdHandle(STD_OUTPUT_HANDLE);
                if (handle == IntPtr.Zero || handle == new IntPtr(-1))
                    return;

                if (GetConsoleMode(handle, out uint mode))
                {
                    SetConsoleMode(handle, mode | ENABLE_VIRTUAL_TERMINAL_PROCESSING);
                }
            }
            catch
            {
                // Silently fail — worst case, ANSI art looks garbled but game still works
            }
        }

        private static void SetupConsoleCloseHandlers()
        {
            // Handle Ctrl+C gracefully
            Console.CancelKeyPress += (sender, e) =>
            {
                e.Cancel = true; // Prevent immediate termination
                HandleConsoleClose("Ctrl+C detected");
            };

            // Handle process exit (called when process is terminating)
            AppDomain.CurrentDomain.ProcessExit += (sender, e) =>
            {
                if (!_exitRequested)
                {
                    HandleConsoleClose("Process exit detected");
                }
            };

            // Windows-specific: Handle console close button (X), shutdown, logoff
            if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
            {
                try
                {
                    _handler = new ConsoleCtrlHandlerDelegate(ConsoleCtrlHandler);
                    SetConsoleCtrlHandler(_handler, true);
                }
                catch
                {
                    // Ignore if P/Invoke fails (e.g., running in non-console context)
                }
            }
        }

        private static bool ConsoleCtrlHandler(int sig)
        {
            switch (sig)
            {
                case CTRL_C_EVENT:
                case CTRL_BREAK_EVENT:
                    HandleConsoleClose("Ctrl+C/Break detected");
                    return true; // Handled - don't terminate immediately

                case CTRL_CLOSE_EVENT:
                    // User clicked the X button on the console window
                    HandleConsoleClose("Console window closed");
                    // Give time for save operation
                    System.Threading.Thread.Sleep(2000);
                    return false; // Allow termination after we've handled it

                case CTRL_LOGOFF_EVENT:
                case CTRL_SHUTDOWN_EVENT:
                    HandleConsoleClose("System shutdown/logoff");
                    System.Threading.Thread.Sleep(2000);
                    return false;

                default:
                    return false;
            }
        }

        /// <summary>
        /// Detect how the player is connecting: Web, SSH, BBS, Steam, or Local.
        /// Web = SSH proxy on localhost (127.0.0.1), SSH = direct SSH, BBS = door mode.
        /// </summary>
        private static string DetectConnectionType()
        {
            // BBS Online mode: report as BBS connection (BBS handles the transport)
            if (DoorMode.IsOnlineMode && DoorMode.IsInDoorMode)
                return "BBS";

            // Online mode: distinguish Web proxy vs direct SSH
            if (DoorMode.IsOnlineMode)
            {
                // SSH_CLIENT env var format: "client_ip client_port server_port"
                var sshClient = Environment.GetEnvironmentVariable("SSH_CLIENT");
                if (!string.IsNullOrEmpty(sshClient))
                {
                    // Web proxy connects from 127.0.0.1 (ssh-proxy.js on the same server)
                    if (sshClient.StartsWith("127.0.0.1") || sshClient.StartsWith("::1"))
                        return "Web";
                    return "SSH";
                }

                // SSH_CONNECTION is another indicator
                var sshConnection = Environment.GetEnvironmentVariable("SSH_CONNECTION");
                if (!string.IsNullOrEmpty(sshConnection))
                {
                    if (sshConnection.StartsWith("127.0.0.1") || sshConnection.StartsWith("::1"))
                        return "Web";
                    return "SSH";
                }

                // Online mode but no SSH env vars - likely local testing
                return "Local";
            }

            // Traditional BBS door mode
            if (DoorMode.IsInDoorMode)
                return "BBS";

            // Steam build
            if (SteamIntegration.IsAvailable)
                return "Steam";

            return "Local";
        }

        private static void HandleConsoleClose(string reason)
        {
            if (_exitRequested) return;
            _exitRequested = true;

            // If this is an intentional exit from the game menu, don't show warning
            if (GameEngine.IsIntentionalExit) return;

            try
            {
                Console.ForegroundColor = ConsoleColor.Red;
                Console.WriteLine();
                Console.WriteLine("═══════════════════════════════════════════════════════════");
                Console.WriteLine("                    WARNING!");
                Console.WriteLine("═══════════════════════════════════════════════════════════");
                Console.ForegroundColor = ConsoleColor.Yellow;
                Console.WriteLine($"  {reason}");
                Console.WriteLine();
                Console.WriteLine("  Your progress since your last save may be lost!");
                Console.WriteLine("  Please use 'Quit to Main Menu' or go to sleep at the Inn");
                Console.WriteLine("  to save your game properly.");
                Console.WriteLine();
                Console.ForegroundColor = ConsoleColor.Cyan;
                Console.WriteLine("  Attempting emergency save...");

                // Try to perform an emergency save
                var player = GameEngine.Instance?.CurrentPlayer;
                if (player != null)
                {
                    try
                    {
                        // v0.57.18: per-character emergency name. Old code wrote to a
                        // fixed "emergency_autosave" filename which had two problems:
                        //   (1) different characters would clobber each other's
                        //       emergency dumps — only the most recent Ctrl+C survived.
                        //   (2) The recovery flow couldn't tell WHICH character the
                        //       emergency belonged to (filename had no character info),
                        //       so it surfaced as a global "emergency_autosave.json"
                        //       option in every character's recovery menu.
                        // Now: emergency_<charactername>_<timestamp>.json. The
                        // FileSaveBackend's emergency-aware listing parses this back
                        // into a recovery slot for that specific character.
                        string charName = player.Name2 ?? player.Name1 ?? "unknown";
                        string sanitized = string.Join("_", charName.Split(System.IO.Path.GetInvalidFileNameChars()));
                        string ts = DateTime.Now.ToString("yyyy-MM-dd_HH-mm-ss");
                        string emergencyKey = $"emergency_{sanitized}_{ts}";
                        SaveSystem.Instance.SaveGame(emergencyKey, player).Wait(TimeSpan.FromSeconds(3));

                        // Keep only the 3 most recent emergency saves per character so
                        // a player who repeatedly Ctrl+Cs doesn't accumulate hundreds of
                        // recovery slots in their load menu.
                        (SaveSystem.Instance.Backend as FileSaveBackend)?.RotateEmergencySaves(charName);

                        Console.ForegroundColor = ConsoleColor.Green;
                        Console.WriteLine("  Emergency save completed!");
                        Console.WriteLine($"  Look for '{charName}' in the save menu — it will appear");
                        Console.WriteLine("  marked [EMERGENCY SAVE] (or [RECOVERY] if other saves exist).");
                    }
                    catch
                    {
                        Console.ForegroundColor = ConsoleColor.Red;
                        Console.WriteLine("  Emergency save failed - progress may be lost.");
                    }
                }
                else
                {
                    Console.ForegroundColor = ConsoleColor.Gray;
                    Console.WriteLine("  No active game session to save.");
                }

                Console.ForegroundColor = ConsoleColor.White;
                Console.WriteLine("═══════════════════════════════════════════════════════════");
                Console.ResetColor();
            }
            catch
            {
                // Ignore any errors during shutdown message
            }
        }
    }
} 