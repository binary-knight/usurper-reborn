using System;
using System.IO;
using System.Runtime.InteropServices;
using System.Threading.Tasks;

namespace UsurperRemake.BBS
{
    /// <summary>
    /// BBS Door mode launcher - handles initialization when running as a door game
    /// </summary>
    public static class DoorMode
    {
        private static BBSSessionInfo? _sessionInfo;
        private static SocketTerminal? _socketTerminal;
        private static BBSTerminalAdapter? _terminalAdapter;
        private static bool _forceStdio = false;
        private static bool _verboseMode = false; // Verbose debug output for troubleshooting (also keeps console visible)
        private static bool _helpWasShown = false; // Flag to indicate --help was processed

        // Online multiplayer mode
        private static bool _onlineMode = false;
        private static string? _onlineUsername = null;
        private static string _onlineDatabasePath = "/var/usurper/usurper_online.db";

        // World simulator mode (headless 24/7 NPC simulation)
        private static bool _worldSimMode = false;
        private static int _simIntervalSeconds = 60;
        private static float _npcXpMultiplier = 0.25f;
        private static int _saveIntervalMinutes = 5;

        // Windows API for hiding console window
        [DllImport("kernel32.dll")]
        private static extern IntPtr GetConsoleWindow();

        [DllImport("user32.dll")]
        private static extern bool ShowWindow(IntPtr hWnd, int nCmdShow);

        private const int SW_HIDE = 0;
        private const int SW_SHOW = 5;

        public static BBSSessionInfo? SessionInfo => _sessionInfo;
        public static BBSTerminalAdapter? TerminalAdapter => _terminalAdapter;
        public static bool IsInDoorMode => _sessionInfo != null && _sessionInfo.SourceType != DropFileType.None;

        /// <summary>
        /// True when the terminal should emit ANSI escape codes instead of using Console.ForegroundColor.
        /// Covers BBS door mode AND online mode (where stdout goes through SSH pipe to the client).
        /// </summary>
        public static bool ShouldUseAnsiOutput => IsInDoorMode || _onlineMode;
        public static bool HelpWasShown => _helpWasShown;

        /// <summary>
        /// Check if the current user is a SysOp (security level >= SysOpSecurityLevel)
        /// SysOps can access the admin console to manage the game
        /// </summary>
        public static bool IsSysOp => _sessionInfo != null && _sessionInfo.SecurityLevel >= SysOpSecurityLevel;

        /// <summary>
        /// SysOp security level threshold (configurable)
        /// Default is 100, which is standard for most BBS software
        /// </summary>
        public static int SysOpSecurityLevel { get; set; } = 100;

        /// <summary>
        /// True when running in online multiplayer mode (--online flag).
        /// Uses SqlSaveBackend instead of FileSaveBackend.
        /// </summary>
        public static bool IsOnlineMode => _onlineMode;

        /// <summary>
        /// The username for the online session (from --user flag, SSH, or in-game auth).
        /// </summary>
        public static string? OnlineUsername => _onlineUsername;

        /// <summary>
        /// Set the online username after in-game authentication.
        /// Also updates the session info so the game engine uses the correct name.
        /// </summary>
        public static void SetOnlineUsername(string username)
        {
            _onlineUsername = username;
            if (_sessionInfo != null)
            {
                _sessionInfo.UserName = username;
                _sessionInfo.UserAlias = username;
            }
        }

        /// <summary>
        /// Path to the SQLite database for online mode.
        /// Default: /var/usurper/usurper_online.db (configurable via --db flag)
        /// </summary>
        public static string OnlineDatabasePath => _onlineDatabasePath;

        /// <summary>
        /// True when running in headless world simulator mode (--worldsim flag).
        /// Runs NPC simulation 24/7 without terminal, auth, or player tracking.
        /// </summary>
        public static bool IsWorldSimMode => _worldSimMode;

        /// <summary>Simulation tick interval in seconds (default: 60).</summary>
        public static int SimIntervalSeconds => _simIntervalSeconds;

        /// <summary>NPC XP gain multiplier (default: 0.25 = 25% of normal).</summary>
        public static float NpcXpMultiplier => _npcXpMultiplier;

        /// <summary>How often to persist NPC state to database, in minutes (default: 5).</summary>
        public static int SaveIntervalMinutes => _saveIntervalMinutes;

        /// <summary>
        /// Check command line args for door mode parameters
        /// Returns true if door mode should be used
        /// </summary>
        public static bool ParseCommandLineArgs(string[] args)
        {
            // First pass: process flags (--stdio, --verbose, etc.)
            // These need to be set before we load drop files
            for (int i = 0; i < args.Length; i++)
            {
                var arg = args[i].ToLowerInvariant();

                // --stdio forces console I/O even when drop file has socket handle
                if (arg == "--stdio")
                {
                    _forceStdio = true;
                }
                // --verbose enables detailed debug output (also keeps console visible for debugging)
                else if (arg == "--verbose" || arg == "-v")
                {
                    _verboseMode = true;
                    Console.Error.WriteLine("[VERBOSE] Verbose mode enabled - detailed debug output will be shown");
                }
                // --sysop-level <number> sets the minimum security level for SysOp access
                else if (arg == "--sysop-level" && i + 1 < args.Length)
                {
                    if (int.TryParse(args[i + 1], out int level) && level >= 0)
                    {
                        SysOpSecurityLevel = level;
                        Console.Error.WriteLine($"SysOp security level set to: {level}");
                    }
                }
                // --online enables online multiplayer mode (SQLite backend)
                else if (arg == "--online")
                {
                    _onlineMode = true;
                    Console.Error.WriteLine("[ONLINE] Online multiplayer mode enabled");
                }
                // --user <username> sets the online player username
                else if (arg == "--user" && i + 1 < args.Length)
                {
                    _onlineUsername = args[i + 1];
                    i++; // skip next arg (the username value)
                }
                // --db <path> sets the SQLite database path (default: /var/usurper/usurper_online.db)
                else if (arg == "--db" && i + 1 < args.Length)
                {
                    _onlineDatabasePath = args[i + 1];
                    i++; // skip next arg (the path value)
                }
                // --worldsim enables headless world simulator mode (24/7 NPC simulation)
                else if (arg == "--worldsim")
                {
                    _worldSimMode = true;
                    _onlineMode = true; // implies online mode
                    _forceStdio = true;
                    Console.Error.WriteLine("[WORLDSIM] World simulator mode enabled");
                }
                // --sim-interval <seconds> sets the simulation tick interval
                else if (arg == "--sim-interval" && i + 1 < args.Length)
                {
                    if (int.TryParse(args[i + 1], out int interval) && interval >= 10)
                        _simIntervalSeconds = interval;
                    i++;
                }
                // --npc-xp <multiplier> sets the NPC XP gain multiplier (0.01 - 2.0)
                else if (arg == "--npc-xp" && i + 1 < args.Length)
                {
                    if (float.TryParse(args[i + 1], System.Globalization.NumberStyles.Float,
                        System.Globalization.CultureInfo.InvariantCulture, out float mult) && mult >= 0.01f && mult <= 2.0f)
                        _npcXpMultiplier = mult;
                    i++;
                }
                // --save-interval <minutes> sets how often NPC state is persisted
                else if (arg == "--save-interval" && i + 1 < args.Length)
                {
                    if (int.TryParse(args[i + 1], out int mins) && mins >= 1)
                        _saveIntervalMinutes = mins;
                    i++;
                }
            }

            // Second pass: process commands (--door, --door32, etc.)
            for (int i = 0; i < args.Length; i++)
            {
                var arg = args[i].ToLowerInvariant();

                // --door or -d followed by drop file path
                if ((arg == "--door" || arg == "-d") && i + 1 < args.Length)
                {
                    var dropFilePath = args[i + 1];
                    return InitializeFromDropFile(dropFilePath);
                }

                // --door32 followed by path (explicit DOOR32.SYS)
                if (arg == "--door32" && i + 1 < args.Length)
                {
                    var path = args[i + 1];
                    return InitializeFromDoor32Sys(path);
                }

                // --doorsys followed by path (explicit DOOR.SYS)
                if (arg == "--doorsys" && i + 1 < args.Length)
                {
                    var path = args[i + 1];
                    return InitializeFromDoorSys(path);
                }

                // --node followed by node directory (auto-detect drop file)
                if ((arg == "--node" || arg == "-n") && i + 1 < args.Length)
                {
                    var nodeDir = args[i + 1];
                    return InitializeFromNodeDirectory(nodeDir);
                }

                // --local for local testing mode
                if (arg == "--local" || arg == "-l")
                {
                    _sessionInfo = DropFileParser.CreateLocalSession();
                    return true;
                }

                // --worldsim (handled in first pass for flag, trigger entry here)
                if (arg == "--worldsim")
                {
                    _sessionInfo = DropFileParser.CreateLocalSession();
                    _sessionInfo.UserName = "__worldsim__";
                    _sessionInfo.UserAlias = "__worldsim__";
                    Console.Error.WriteLine($"[WORLDSIM] Sim interval: {_simIntervalSeconds}s, NPC XP: {_npcXpMultiplier:F2}x, Save interval: {_saveIntervalMinutes}min");
                    Console.Error.WriteLine($"[WORLDSIM] Database: {_onlineDatabasePath}");
                    return true;
                }

                // --online (handled in first pass for flag, trigger entry here)
                if (arg == "--online")
                {
                    // Online mode uses stdio - create a local session for the online user
                    _forceStdio = true;
                    _sessionInfo = DropFileParser.CreateLocalSession();

                    // Override username if --user was provided
                    if (!string.IsNullOrEmpty(_onlineUsername) && _sessionInfo != null)
                    {
                        _sessionInfo.UserName = _onlineUsername;
                        _sessionInfo.UserAlias = _onlineUsername;
                    }

                    Console.Error.WriteLine($"[ONLINE] User: {_onlineUsername ?? "(in-game auth)"}");
                    Console.Error.WriteLine($"[ONLINE] Database: {_onlineDatabasePath}");
                    return true;
                }

                // --help
                if (arg == "--help" || arg == "-h" || arg == "-?")
                {
                    PrintDoorHelp();
                    _helpWasShown = true;
                    return false;
                }
            }

            return false;
        }

        /// <summary>
        /// Initialize from auto-detected drop file
        /// </summary>
        private static bool InitializeFromDropFile(string path)
        {
            try
            {
                // In verbose mode, dump the raw drop file contents first
                if (_verboseMode)
                {
                    DumpDropFileContents(path);
                }

                _sessionInfo = DropFileParser.ParseDropFileAsync(path).GetAwaiter().GetResult();

                if (_sessionInfo == null)
                {
                    Console.Error.WriteLine($"Could not parse drop file: {path}");
                    if (_verboseMode)
                    {
                        Console.Error.WriteLine("[VERBOSE] (continuing...)");
                    }
                    return false;
                }

                Console.Error.WriteLine($"Loaded {_sessionInfo.SourceType} from: {_sessionInfo.SourcePath}");
                Console.Error.WriteLine($"User: {_sessionInfo.UserName} ({_sessionInfo.UserAlias})");
                Console.Error.WriteLine($"Connection: {_sessionInfo.CommType}, Handle: {_sessionInfo.SocketHandle}");

                return true;
            }
            catch (Exception ex)
            {
                Console.Error.WriteLine($"Error loading drop file: {ex.Message}");
                if (_verboseMode)
                {
                    Console.Error.WriteLine("[VERBOSE] (continuing...)");
                }
                return false;
            }
        }

        /// <summary>
        /// Dump raw drop file contents for debugging
        /// </summary>
        private static void DumpDropFileContents(string path)
        {
            try
            {
                string actualPath = path;

                // If directory, find the drop file
                if (Directory.Exists(path))
                {
                    var door32Path = Path.Combine(path, "door32.sys");
                    if (File.Exists(door32Path))
                        actualPath = door32Path;
                    else
                    {
                        door32Path = Path.Combine(path, "DOOR32.SYS");
                        if (File.Exists(door32Path))
                            actualPath = door32Path;
                        else
                        {
                            var doorPath = Path.Combine(path, "door.sys");
                            if (File.Exists(doorPath))
                                actualPath = doorPath;
                            else
                            {
                                doorPath = Path.Combine(path, "DOOR.SYS");
                                if (File.Exists(doorPath))
                                    actualPath = doorPath;
                            }
                        }
                    }
                }

                if (!File.Exists(actualPath))
                {
                    Console.Error.WriteLine($"[VERBOSE] Drop file not found: {actualPath}");
                    Console.Error.WriteLine("[VERBOSE] (continuing...)");
                    return;
                }

                Console.Error.WriteLine($"[VERBOSE] === RAW DROP FILE CONTENTS: {actualPath} ===");
                var lines = File.ReadAllLines(actualPath);
                for (int i = 0; i < lines.Length && i < 20; i++) // First 20 lines
                {
                    Console.Error.WriteLine($"[VERBOSE] Line {i + 1}: {lines[i]}");
                }
                if (lines.Length > 20)
                {
                    Console.Error.WriteLine($"[VERBOSE] ... ({lines.Length - 20} more lines)");
                }
                Console.Error.WriteLine("[VERBOSE] === END DROP FILE ===");
            }
            catch (Exception ex)
            {
                Console.Error.WriteLine($"[VERBOSE] Error reading drop file: {ex.Message}");
                Console.Error.WriteLine("[VERBOSE] (continuing...)");
            }
        }

        /// <summary>
        /// Initialize from explicit DOOR32.SYS path
        /// </summary>
        private static bool InitializeFromDoor32Sys(string path)
        {
            try
            {
                if (_verboseMode)
                {
                    DumpDropFileContents(path);
                }

                _sessionInfo = DropFileParser.ParseDoor32SysAsync(path).GetAwaiter().GetResult();
                Console.Error.WriteLine($"Loaded DOOR32.SYS: {path}");
                Console.Error.WriteLine($"User: {_sessionInfo.UserName} ({_sessionInfo.UserAlias})");
                Console.Error.WriteLine($"Connection: {_sessionInfo.CommType}, Handle: {_sessionInfo.SocketHandle}");
                return true;
            }
            catch (Exception ex)
            {
                Console.Error.WriteLine($"Error loading DOOR32.SYS: {ex.Message}");
                if (_verboseMode)
                {
                    Console.Error.WriteLine("[VERBOSE] (continuing...)");
                }
                return false;
            }
        }

        /// <summary>
        /// Initialize from explicit DOOR.SYS path
        /// </summary>
        private static bool InitializeFromDoorSys(string path)
        {
            try
            {
                if (_verboseMode)
                {
                    DumpDropFileContents(path);
                }

                _sessionInfo = DropFileParser.ParseDoorSysAsync(path).GetAwaiter().GetResult();
                Console.Error.WriteLine($"Loaded DOOR.SYS: {path}");
                Console.Error.WriteLine($"User: {_sessionInfo.UserName} ({_sessionInfo.UserAlias})");
                Console.Error.WriteLine($"Connection: {_sessionInfo.CommType}, ComPort: {_sessionInfo.ComPort}");
                return true;
            }
            catch (Exception ex)
            {
                Console.Error.WriteLine($"Error loading DOOR.SYS: {ex.Message}");
                if (_verboseMode)
                {
                    Console.Error.WriteLine("[VERBOSE] (continuing...)");
                }
                return false;
            }
        }

        /// <summary>
        /// Initialize from a node directory (search for drop files)
        /// </summary>
        private static bool InitializeFromNodeDirectory(string nodeDir)
        {
            if (!Directory.Exists(nodeDir))
            {
                Console.Error.WriteLine($"Node directory not found: {nodeDir}");
                return false;
            }

            return InitializeFromDropFile(nodeDir);
        }

        /// <summary>
        /// Initialize the terminal for door mode
        /// Call this after ParseCommandLineArgs returns true
        /// </summary>
        public static BBSTerminalAdapter? InitializeTerminal()
        {
            if (_sessionInfo == null)
            {
                Console.Error.WriteLine("No session info - call ParseCommandLineArgs first");
                return null;
            }

            try
            {
                // Enable verbose logging if requested
                if (_verboseMode)
                {
                    SocketTerminal.VerboseLogging = true;
                    Console.Error.WriteLine("[VERBOSE] Session info from drop file:");
                    Console.Error.WriteLine($"[VERBOSE]   CommType: {_sessionInfo.CommType}");
                    Console.Error.WriteLine($"[VERBOSE]   SocketHandle: {_sessionInfo.SocketHandle} (0x{_sessionInfo.SocketHandle:X8})");
                    Console.Error.WriteLine($"[VERBOSE]   ComPort: {_sessionInfo.ComPort}");
                    Console.Error.WriteLine($"[VERBOSE]   BaudRate: {_sessionInfo.BaudRate}");
                    Console.Error.WriteLine($"[VERBOSE]   UserName: {_sessionInfo.UserName}");
                    Console.Error.WriteLine($"[VERBOSE]   UserAlias: {_sessionInfo.UserAlias}");
                    Console.Error.WriteLine($"[VERBOSE]   BBSName: {_sessionInfo.BBSName}");
                    Console.Error.WriteLine($"[VERBOSE]   Emulation: {_sessionInfo.Emulation}");
                    Console.Error.WriteLine($"[VERBOSE]   SourceType: {_sessionInfo.SourceType}");
                    Console.Error.WriteLine($"[VERBOSE]   SourcePath: {_sessionInfo.SourcePath}");
                    Console.Error.WriteLine("");
                    Console.Error.WriteLine("[VERBOSE] (continuing...)");
                }

                if (_verboseMode)
                {
                    Console.Error.WriteLine($"[VERBOSE] CommType check: {_sessionInfo.CommType}");
                    Console.Error.WriteLine($"[VERBOSE] _forceStdio={_forceStdio}");
                }

                // Auto-detect BBS software that requires stdio mode
                // These BBS types pass socket handles but expect doors to use stdin/stdout for terminal I/O
                if (!_forceStdio && !string.IsNullOrEmpty(_sessionInfo.BBSName))
                {
                    string bbsName = _sessionInfo.BBSName;
                    string? detectedBBS = null;

                    // Check for known BBS software that needs stdio mode
                    if (bbsName.Contains("Synchronet", StringComparison.OrdinalIgnoreCase))
                        detectedBBS = "Synchronet";
                    else if (bbsName.Contains("GameSrv", StringComparison.OrdinalIgnoreCase))
                        detectedBBS = "GameSrv";
                    else if (bbsName.Contains("ENiGMA", StringComparison.OrdinalIgnoreCase))
                        detectedBBS = "ENiGMA";
                    else if (bbsName.Contains("WWIV", StringComparison.OrdinalIgnoreCase))
                        detectedBBS = "WWIV";

                    if (detectedBBS != null)
                    {
                        _forceStdio = true;
                        Console.Error.WriteLine($"Detected {detectedBBS} BBS - automatically using Standard I/O mode");
                        if (_verboseMode)
                        {
                            Console.Error.WriteLine($"[VERBOSE] {detectedBBS} requires --stdio mode. Auto-enabled.");
                        }
                    }
                }

                // Auto-detect redirected I/O (handles Mystic SSH and other BBS software)
                // When a BBS redirects stdin/stdout, it expects the door to use them.
                // The socket handle in DOOR32.SYS may be the raw TCP socket (pre-encryption),
                // and writing directly to it would bypass SSH/TLS encryption, corrupting the stream.
                // Using stdio mode routes I/O through the BBS's transport layer instead.
                if (!_forceStdio && (Console.IsInputRedirected || Console.IsOutputRedirected))
                {
                    _forceStdio = true;
                    Console.Error.WriteLine("Detected redirected I/O - automatically using Standard I/O mode");
                    Console.Error.WriteLine("(BBS is handling the transport layer - stdin/stdout will be used)");
                    if (_verboseMode)
                    {
                        Console.Error.WriteLine($"[VERBOSE] Console.IsInputRedirected={Console.IsInputRedirected}");
                        Console.Error.WriteLine($"[VERBOSE] Console.IsOutputRedirected={Console.IsOutputRedirected}");
                        Console.Error.WriteLine("[VERBOSE] This typically means SSH, TLS, or pipe-based transport.");
                        Console.Error.WriteLine("[VERBOSE] Socket I/O would bypass encryption. Using stdio instead.");
                    }
                }

                // If --stdio flag was used (or auto-detected), force console I/O mode
                // This is for Synchronet's "Standard" I/O mode where stdin/stdout are redirected
                if (_forceStdio)
                {
                    Console.Error.WriteLine("Using Standard I/O mode (--stdio flag)");
                    _sessionInfo.CommType = ConnectionType.Local;
                }

                // Use socket terminal for telnet or local connections
                if (_verboseMode)
                {
                    Console.Error.WriteLine("[VERBOSE] Creating SocketTerminal...");
                }
                _socketTerminal = new SocketTerminal(_sessionInfo);

                if (_verboseMode)
                {
                    Console.Error.WriteLine("[VERBOSE] Calling SocketTerminal.Initialize()...");
                }
                if (!_socketTerminal.Initialize())
                {
                    Console.Error.WriteLine("Failed to initialize socket terminal");

                    // Fall back to local mode
                    if (_sessionInfo.CommType != ConnectionType.Local)
                    {
                        Console.Error.WriteLine("Falling back to local console mode");
                        if (_verboseMode)
                        {
                            Console.Error.WriteLine("[VERBOSE] Socket initialization failed. (continuing...)");
                        }
                        _sessionInfo.CommType = ConnectionType.Local;
                    }
                }

                // Pass _forceStdio to tell adapter to use ANSI codes instead of Console.ForegroundColor
                _terminalAdapter = new BBSTerminalAdapter(_socketTerminal, _forceStdio);

                // Final verbose pause - so sysop can read/copy all diagnostic output
                if (_verboseMode)
                {
                    Console.Error.WriteLine("");
                    Console.Error.WriteLine("[VERBOSE] === Initialization complete. Press Enter to continue... ===");
                    Console.ReadLine();
                }

                // Auto-hide the console window in BBS socket mode (unless verbose mode is on for debugging)
                // This prevents the door from showing a visible console window on Windows
                // All I/O goes through the socket, so the console window is not needed
                bool shouldHideConsole = _sessionInfo.CommType != ConnectionType.Local && !_verboseMode;
                if (shouldHideConsole && RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
                {
                    try
                    {
                        var consoleWindow = GetConsoleWindow();
                        if (consoleWindow != IntPtr.Zero)
                        {
                            ShowWindow(consoleWindow, SW_HIDE);
                        }
                    }
                    catch
                    {
                        // Silently ignore - console hiding is optional
                    }
                }

                return _terminalAdapter;
            }
            catch (Exception ex)
            {
                Console.Error.WriteLine($"Terminal initialization failed: {ex.Message}");
                if (_verboseMode)
                {
                    Console.Error.WriteLine($"[VERBOSE] Exception type: {ex.GetType().Name}");
                    Console.Error.WriteLine($"[VERBOSE] Stack trace: {ex.StackTrace}");
                    Console.Error.WriteLine("[VERBOSE] (continuing...)");
                }
                return null;
            }
        }

        /// <summary>
        /// Get the player name from the drop file for character lookup/creation
        /// </summary>
        public static string GetPlayerName()
        {
            if (_sessionInfo == null)
                return "Player";

            // Prefer alias, fall back to real name
            return !string.IsNullOrWhiteSpace(_sessionInfo.UserAlias)
                ? _sessionInfo.UserAlias
                : _sessionInfo.UserName;
        }

        /// <summary>
        /// Get a unique save namespace for this BBS to isolate saves from different BBSes.
        /// Uses the BBS name from the drop file, sanitized for use as a directory name.
        /// Returns null if not in door mode (use default saves directory).
        /// </summary>
        public static string? GetSaveNamespace()
        {
            if (_sessionInfo == null || !IsInDoorMode)
                return null;

            // Sanitize the BBS name for use as a directory
            var bbsName = _sessionInfo.BBSName;
            if (string.IsNullOrWhiteSpace(bbsName))
                bbsName = "BBS";

            // Remove invalid path characters
            var invalidChars = Path.GetInvalidFileNameChars();
            var sanitized = string.Join("_", bbsName.Split(invalidChars, StringSplitOptions.RemoveEmptyEntries));

            // Limit length
            if (sanitized.Length > 32)
                sanitized = sanitized.Substring(0, 32);

            return sanitized;
        }

        /// <summary>
        /// Get the user record number from the drop file (unique ID per BBS user)
        /// </summary>
        public static int GetUserRecordNumber()
        {
            return _sessionInfo?.UserRecordNumber ?? 0;
        }

        /// <summary>
        /// Clean shutdown of door mode
        /// </summary>
        public static void Shutdown()
        {
            try
            {
                _socketTerminal?.Dispose();
            }
            catch { }

            _socketTerminal = null;
            _terminalAdapter = null;
            _sessionInfo = null;
        }

        /// <summary>
        /// Print help for door mode command line options
        /// </summary>
        private static void PrintDoorHelp()
        {
            Console.WriteLine("Usurper Reborn - BBS Door Mode");
            Console.WriteLine("");
            Console.WriteLine("Usage: UsurperReborn [options]");
            Console.WriteLine("");
            Console.WriteLine("Door Mode Options:");
            Console.WriteLine("  --door, -d <path>    Load drop file (auto-detect DOOR32.SYS or DOOR.SYS)");
            Console.WriteLine("  --door32 <path>      Load DOOR32.SYS explicitly");
            Console.WriteLine("  --doorsys <path>     Load DOOR.SYS explicitly");
            Console.WriteLine("  --node, -n <dir>     Search node directory for drop files");
            Console.WriteLine("  --local, -l          Run in local mode (no BBS connection)");
            Console.WriteLine("  --stdio              Force Standard I/O mode (usually auto-detected)");
            Console.WriteLine("  --verbose, -v        Enable detailed debug output (keeps console visible)");
            Console.WriteLine("  --sysop-level <num>  Set SysOp security level threshold (default: 100)");
            Console.WriteLine("");
            Console.WriteLine("Online Multiplayer Options:");
            Console.WriteLine("  --online             Run in online multiplayer mode (SQLite backend)");
            Console.WriteLine("  --user <name>        Set player username (for SSH ForceCommand)");
            Console.WriteLine("  --db <path>          SQLite database path (default: /var/usurper/usurper_online.db)");
            Console.WriteLine("");
            Console.WriteLine("World Simulator Options:");
            Console.WriteLine("  --worldsim           Run headless 24/7 world simulator (no terminal/auth)");
            Console.WriteLine("  --sim-interval <sec> Simulation tick interval in seconds (default: 60)");
            Console.WriteLine("  --npc-xp <mult>      NPC XP gain multiplier, 0.01-2.0 (default: 0.25)");
            Console.WriteLine("  --save-interval <min> State persistence interval in minutes (default: 5)");
            Console.WriteLine("");
            Console.WriteLine("Examples:");
            Console.WriteLine("  UsurperReborn --door32 /sbbs/node1/door32.sys");
            Console.WriteLine("  UsurperReborn --node /sbbs/node1");
            Console.WriteLine("  UsurperReborn -d C:\\SBBS\\NODE1\\");
            Console.WriteLine("  UsurperReborn --online --user PlayerName --stdio");
            Console.WriteLine("  UsurperReborn --online --db /var/usurper/game.db");
            Console.WriteLine("");
            Console.WriteLine("Drop File Support:");
            Console.WriteLine("  DOOR32.SYS - Modern format with socket handle (recommended)");
            Console.WriteLine("  DOOR.SYS   - Legacy format (52 lines, no socket - uses console)");
            Console.WriteLine("");
            Console.WriteLine("For Synchronet BBS (Socket I/O mode):");
            Console.WriteLine("  Command: UsurperReborn --door %f");
            Console.WriteLine("  Drop File Type: Door32.sys");
            Console.WriteLine("  I/O Method: Socket");
            Console.WriteLine("");
            Console.WriteLine("For Synchronet BBS (Standard I/O mode - recommended):");
            Console.WriteLine("  Command: UsurperReborn --door32 %f --stdio");
            Console.WriteLine("  Drop File Type: Door32.sys");
            Console.WriteLine("  I/O Method: Standard");
            Console.WriteLine("  Native Executable: Yes");
            Console.WriteLine("");
            Console.WriteLine("For EleBBS (Socket mode):");
            Console.WriteLine("  Command: UsurperReborn --door32 *N\\door32.sys");
            Console.WriteLine("  Drop File Type: Door32.sys");
            Console.WriteLine("  Console window is automatically hidden in socket mode");
            Console.WriteLine("");
            Console.WriteLine("For Mystic BBS (auto-detected):");
            Console.WriteLine("  Command: UsurperReborn --door32 %f");
            Console.WriteLine("  Works with both telnet and SSH connections");
            Console.WriteLine("  SSH connections auto-detected via redirected I/O");
            Console.WriteLine("");
            Console.WriteLine("Troubleshooting:");
            Console.WriteLine("  If output shows locally but not remotely:");
            Console.WriteLine("  1. Try --stdio flag for Standard I/O mode");
            Console.WriteLine("  2. Use --verbose flag to see detailed connection info");
            Console.WriteLine("  3. Check your DOOR32.SYS has correct CommType (2=telnet) and socket handle");
            Console.WriteLine("");
            Console.WriteLine("  Example with verbose debugging:");
            Console.WriteLine("  UsurperReborn --door32 door32.sys --verbose");
            Console.WriteLine("");
        }

        /// <summary>
        /// Write a message to the BBS log (stderr)
        /// </summary>
        public static void Log(string message)
        {
            var timestamp = DateTime.Now.ToString("HH:mm:ss");
            Console.Error.WriteLine($"[{timestamp}] USURPER: {message}");
        }
    }
}
