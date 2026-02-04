using System;
using System.IO;
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
        private static string? _forceFossilPort = null; // Force FOSSIL mode on this COM port
        private static bool _verboseMode = false; // Verbose debug output for troubleshooting

        public static BBSSessionInfo? SessionInfo => _sessionInfo;
        public static BBSTerminalAdapter? TerminalAdapter => _terminalAdapter;
        public static bool IsInDoorMode => _sessionInfo != null && _sessionInfo.SourceType != DropFileType.None;

        /// <summary>
        /// Check command line args for door mode parameters
        /// Returns true if door mode should be used
        /// </summary>
        public static bool ParseCommandLineArgs(string[] args)
        {
            // First pass: process flags (--stdio, --verbose, --fossil, etc.)
            // These need to be set before we load drop files
            for (int i = 0; i < args.Length; i++)
            {
                var arg = args[i].ToLowerInvariant();

                // --stdio forces console I/O even when drop file has socket handle
                if (arg == "--stdio")
                {
                    _forceStdio = true;
                }
                // --verbose enables detailed debug output
                else if (arg == "--verbose" || arg == "-v")
                {
                    _verboseMode = true;
                    Console.Error.WriteLine("[VERBOSE] Verbose mode enabled - detailed debug output will be shown");
                }
                // --fossil or --com forces FOSSIL/serial mode
                else if ((arg == "--fossil" || arg == "--com") && i + 1 < args.Length)
                {
                    _forceFossilPort = args[i + 1].ToUpperInvariant();
                    if (!_forceFossilPort.StartsWith("COM"))
                        _forceFossilPort = "COM" + _forceFossilPort;
                    i++; // Skip the port arg
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

                // --help
                if (arg == "--help" || arg == "-h" || arg == "-?")
                {
                    PrintDoorHelp();
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
                        Console.Error.WriteLine("[VERBOSE] Press Enter to continue...");
                        Console.ReadLine();
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
                    Console.Error.WriteLine("[VERBOSE] Press Enter to continue...");
                    Console.ReadLine();
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
                    Console.Error.WriteLine("[VERBOSE] Press Enter to continue...");
                    Console.ReadLine();
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
                Console.Error.WriteLine("[VERBOSE] Press Enter to continue...");
                Console.ReadLine();
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
                    Console.Error.WriteLine("[VERBOSE] Press Enter to continue...");
                    Console.ReadLine();
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
                    Console.Error.WriteLine("[VERBOSE] Press Enter to continue...");
                    Console.ReadLine();
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
                    Console.Error.WriteLine("[VERBOSE] Press Enter to continue...");
                    Console.ReadLine();
                }

                // If --stdio flag was used, force console I/O mode
                // This is for Synchronet's "Standard" I/O mode where stdin/stdout are redirected
                if (_forceStdio)
                {
                    Console.Error.WriteLine("Using Standard I/O mode (--stdio flag)");
                    _sessionInfo.CommType = ConnectionType.Local;
                }

                // If --fossil flag was used, force serial/FOSSIL mode
                if (!string.IsNullOrEmpty(_forceFossilPort))
                {
                    Console.Error.WriteLine($"Forcing FOSSIL mode on {_forceFossilPort}");
                    _sessionInfo.CommType = ConnectionType.Serial;
                    _sessionInfo.ComPort = _forceFossilPort;
                }

                // Use serial terminal for FOSSIL/COM port connections
                if (_sessionInfo.CommType == ConnectionType.Serial)
                {
                    Console.Error.WriteLine($"Using Serial/FOSSIL mode on {_sessionInfo.ComPort}");
                    var serialTerminal = new SerialTerminal(_sessionInfo);

                    if (!serialTerminal.Initialize())
                    {
                        Console.Error.WriteLine("Failed to initialize serial terminal");
                        Console.Error.WriteLine("Falling back to local console mode");
                        _sessionInfo.CommType = ConnectionType.Local;

                        // Fall back to socket terminal in local mode
                        _socketTerminal = new SocketTerminal(_sessionInfo);
                        _socketTerminal.Initialize();
                        _terminalAdapter = new BBSTerminalAdapter(_socketTerminal, _forceStdio);
                    }
                    else
                    {
                        _terminalAdapter = new BBSTerminalAdapter(serialTerminal);
                    }

                    return _terminalAdapter;
                }

                // Use socket terminal for telnet or local connections
                _socketTerminal = new SocketTerminal(_sessionInfo);

                if (!_socketTerminal.Initialize())
                {
                    Console.Error.WriteLine("Failed to initialize socket terminal");

                    // Fall back to local mode
                    if (_sessionInfo.CommType != ConnectionType.Local)
                    {
                        Console.Error.WriteLine("Falling back to local console mode");
                        _sessionInfo.CommType = ConnectionType.Local;
                    }
                }

                // Pass _forceStdio to tell adapter to use ANSI codes instead of Console.ForegroundColor
                _terminalAdapter = new BBSTerminalAdapter(_socketTerminal, _forceStdio);
                return _terminalAdapter;
            }
            catch (Exception ex)
            {
                Console.Error.WriteLine($"Terminal initialization failed: {ex.Message}");
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
            Console.WriteLine("  --stdio              Use Standard I/O instead of socket (for Synchronet)");
            Console.WriteLine("  --fossil <port>      Force FOSSIL/serial mode on COM port (e.g., COM1)");
            Console.WriteLine("  --com <port>         Same as --fossil");
            Console.WriteLine("  --verbose, -v        Enable detailed debug output for troubleshooting");
            Console.WriteLine("");
            Console.WriteLine("Examples:");
            Console.WriteLine("  UsurperReborn --door /sbbs/node1/door32.sys");
            Console.WriteLine("  UsurperReborn --node /sbbs/node1");
            Console.WriteLine("  UsurperReborn -d C:\\SBBS\\NODE1\\");
            Console.WriteLine("  UsurperReborn --doorsys door.sys --fossil COM1");
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
            Console.WriteLine("For FOSSIL-based BBS (EleBBS, etc.):");
            Console.WriteLine("  Command: UsurperReborn --doorsys %f --fossil COM1");
            Console.WriteLine("  Drop File Type: Door.sys");
            Console.WriteLine("  The COM port should match your FOSSIL driver's virtual port");
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
