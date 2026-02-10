using System;
using System.Runtime.InteropServices;
using System.Threading.Tasks;
using UsurperRemake;
using UsurperRemake.Systems;
using UsurperRemake.BBS;

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

        // Console control signal types
        private const int CTRL_C_EVENT = 0;
        private const int CTRL_BREAK_EVENT = 1;
        private const int CTRL_CLOSE_EVENT = 2;
        private const int CTRL_LOGOFF_EVENT = 5;
        private const int CTRL_SHUTDOWN_EVENT = 6;

        private static bool _exitRequested = false;

        static async Task Main(string[] args)
        {
            // Set up global exception handlers FIRST so we catch everything
            SetupGlobalExceptionHandlers();

            // Check for BBS door mode arguments
            if (args.Length > 0)
            {
                if (DoorMode.ParseCommandLineArgs(args))
                {
                    // BBS Door Mode - initialize door terminal
                    await RunDoorModeAsync();
                    return;
                }

                // Check if --help was shown - exit without launching game
                if (DoorMode.HelpWasShown)
                {
                    return;
                }
            }

            // Standard console mode
            // Set up console close handlers
            SetupConsoleCloseHandlers();

            // Initialize Steam integration (only works if launched through Steam)
            SteamIntegration.Initialize();

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
            try
            {
                DoorMode.Log("Initializing BBS door mode...");

                SqlSaveBackend? sqlBackend = null;

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

                // If online mode with no --user flag, show in-game auth screen
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
                    OnlineStateManager.Initialize(sqlBackend, onlineUsername);
                    DoorMode.Log($"Online state manager initialized for '{onlineUsername}'");

                    // Initialize inter-player chat system
                    OnlineChatSystem.Initialize(OnlineStateManager.Instance!);
                    DoorMode.Log("Online chat system initialized");

                    // Update session login timestamp
                    await sqlBackend.UpdatePlayerSession(onlineUsername, isLogin: true);

                    // Detect connection type for Who's Online display
                    var connectionType = DetectConnectionType();
                    DoorMode.Log($"Connection type detected: {connectionType}");

                    // Start online presence tracking (heartbeat, player list)
                    await OnlineStateManager.Instance!.StartOnlineTracking(onlineUsername, connectionType);
                    DoorMode.Log("Online presence tracking started");
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
                Console.Error.WriteLine(ex.ToString());
            }
            finally
            {
                // Shutdown chat system
                if (OnlineChatSystem.IsActive)
                {
                    OnlineChatSystem.Instance!.Shutdown();
                }

                // Shutdown online state manager (heartbeat, tracking, session)
                if (OnlineStateManager.IsActive)
                {
                    DoorMode.Log("Shutting down online state manager...");
                    await OnlineStateManager.Instance!.Shutdown();
                }

                DoorMode.Log("Shutting down door mode...");
                DoorMode.Shutdown();
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
                        // Synchronous save for emergency
                        SaveSystem.Instance.SaveGame("emergency_autosave", player).Wait(TimeSpan.FromSeconds(3));
                        Console.ForegroundColor = ConsoleColor.Green;
                        Console.WriteLine("  Emergency save completed!");
                        Console.WriteLine("  Look for 'emergency_autosave' in the save menu.");
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