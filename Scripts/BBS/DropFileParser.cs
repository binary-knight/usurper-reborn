using System;
using System.IO;
using System.Threading.Tasks;

namespace UsurperRemake.BBS
{
    /// <summary>
    /// Represents user session data parsed from BBS drop files
    /// </summary>
    public class BBSSessionInfo
    {
        // Connection info
        public ConnectionType CommType { get; set; } = ConnectionType.Local;
        public int SocketHandle { get; set; } = -1;
        public string ComPort { get; set; } = ""; // COM port name from DOOR.SYS (e.g., "COM1") - not used in .NET
        public int BaudRate { get; set; } = 0;
        public int NodeNumber { get; set; } = 1;

        // User info
        public string UserName { get; set; } = "Player";
        public string UserAlias { get; set; } = "Player";
        public string UserLocation { get; set; } = "Unknown";
        public int SecurityLevel { get; set; } = 0;
        public int TimeLeftMinutes { get; set; } = 60;
        public int UserRecordNumber { get; set; } = 0;

        // Terminal info
        public TerminalEmulation Emulation { get; set; } = TerminalEmulation.ANSI;
        public int ScreenWidth { get; set; } = 80;
        public int ScreenHeight { get; set; } = 24;
        public bool GraphicsEnabled { get; set; } = true;

        // BBS info
        public string BBSName { get; set; } = "Unknown BBS";
        public string SysopName { get; set; } = "Sysop";

        // Drop file source
        public DropFileType SourceType { get; set; } = DropFileType.None;
        public string SourcePath { get; set; } = "";
    }

    public enum ConnectionType
    {
        Local = 0,
        Serial = 1,
        Telnet = 2
    }

    public enum TerminalEmulation
    {
        ASCII = 0,
        ANSI = 1,
        Avatar = 2,
        RIP = 3,
        MaxGraphics = 4
    }

    public enum DropFileType
    {
        None,
        Door32Sys,
        DoorSys
    }

    /// <summary>
    /// Parses BBS drop files (DOOR32.SYS, DOOR.SYS) to extract session information
    /// </summary>
    public static class DropFileParser
    {
        /// <summary>
        /// Auto-detect and parse a drop file from the given path
        /// Tries DOOR32.SYS first, then DOOR.SYS
        /// </summary>
        public static async Task<BBSSessionInfo?> ParseDropFileAsync(string path)
        {
            // If path is a directory, look for drop files
            if (Directory.Exists(path))
            {
                // Try DOOR32.SYS first (modern)
                var door32Path = Path.Combine(path, "door32.sys");
                if (File.Exists(door32Path))
                    return await ParseDoor32SysAsync(door32Path);

                // Case-insensitive search for DOOR32.SYS
                door32Path = Path.Combine(path, "DOOR32.SYS");
                if (File.Exists(door32Path))
                    return await ParseDoor32SysAsync(door32Path);

                // Try DOOR.SYS (legacy)
                var doorPath = Path.Combine(path, "door.sys");
                if (File.Exists(doorPath))
                    return await ParseDoorSysAsync(doorPath);

                doorPath = Path.Combine(path, "DOOR.SYS");
                if (File.Exists(doorPath))
                    return await ParseDoorSysAsync(doorPath);

                return null;
            }

            // If path is a file, detect type by name
            var fileName = Path.GetFileName(path).ToLowerInvariant();

            if (fileName == "door32.sys")
                return await ParseDoor32SysAsync(path);

            if (fileName == "door.sys")
                return await ParseDoorSysAsync(path);

            // Unknown file type - try to detect from content
            return await TryParseUnknownAsync(path);
        }

        /// <summary>
        /// Parse DOOR32.SYS format (11 lines)
        /// </summary>
        public static async Task<BBSSessionInfo> ParseDoor32SysAsync(string path)
        {
            var lines = await File.ReadAllLinesAsync(path);
            var info = new BBSSessionInfo
            {
                SourceType = DropFileType.Door32Sys,
                SourcePath = path
            };

            if (lines.Length < 11)
                throw new FormatException($"DOOR32.SYS has {lines.Length} lines, expected at least 11");

            // Line 1: Comm type (0=local, 1=serial, 2=telnet)
            if (int.TryParse(lines[0].Trim(), out int commType))
                info.CommType = (ConnectionType)commType;

            // Line 2: Comm or socket handle
            if (int.TryParse(lines[1].Trim(), out int socketHandle))
                info.SocketHandle = socketHandle;

            // Line 3: Baud rate
            if (int.TryParse(lines[2].Trim(), out int baudRate))
                info.BaudRate = baudRate;

            // Line 4: BBSID (software name and version)
            info.BBSName = lines[3].Trim();

            // Line 5: User record position (1-based)
            if (int.TryParse(lines[4].Trim(), out int userRecord))
                info.UserRecordNumber = userRecord;

            // Line 6: User's real name
            info.UserName = lines[5].Trim();

            // Line 7: User's handle/alias
            info.UserAlias = lines[6].Trim();

            // Line 8: User's security level
            if (int.TryParse(lines[7].Trim(), out int secLevel))
                info.SecurityLevel = secLevel;

            // Line 9: User's time left in minutes
            if (int.TryParse(lines[8].Trim(), out int timeLeft))
                info.TimeLeftMinutes = timeLeft;

            // Line 10: Emulation (0=Ascii, 1=Ansi, 2=Avatar, 3=RIP, 4=Max Graphics)
            if (int.TryParse(lines[9].Trim(), out int emulation))
            {
                info.Emulation = (TerminalEmulation)emulation;
                info.GraphicsEnabled = emulation > 0;
            }

            // Line 11: Current node number
            if (int.TryParse(lines[10].Trim(), out int nodeNum))
                info.NodeNumber = nodeNum;

            return info;
        }

        /// <summary>
        /// Parse DOOR.SYS format (52 lines for extended, 31 for original)
        /// </summary>
        public static async Task<BBSSessionInfo> ParseDoorSysAsync(string path)
        {
            var lines = await File.ReadAllLinesAsync(path);
            var info = new BBSSessionInfo
            {
                SourceType = DropFileType.DoorSys,
                SourcePath = path,
                CommType = ConnectionType.Local // DOOR.SYS doesn't support socket handles
            };

            if (lines.Length < 20)
                throw new FormatException($"DOOR.SYS has {lines.Length} lines, expected at least 20");

            // Line 1: COM port (COM0: = local mode)
            var comPort = lines[0].Trim().ToUpperInvariant();
            if (comPort.StartsWith("COM") && comPort != "COM0:" && comPort != "COM0")
            {
                info.CommType = ConnectionType.Serial;
                // Store COM port name without colon (e.g., "COM1:" -> "COM1")
                info.ComPort = comPort.TrimEnd(':');
            }

            // Line 2: Baud rate
            if (int.TryParse(lines[1].Trim(), out int baudRate))
                info.BaudRate = baudRate;

            // Line 3: Parity (skip)

            // Line 4: Node number
            if (int.TryParse(lines[3].Trim(), out int nodeNum))
                info.NodeNumber = nodeNum;

            // Line 5: DTE rate (skip)
            // Line 6-9: Display/printer/bell toggles (skip)

            // Line 10: User's full name
            if (lines.Length > 9)
                info.UserName = lines[9].Trim();

            // Line 11: Calling from (location)
            if (lines.Length > 10)
                info.UserLocation = lines[10].Trim();

            // Line 12-14: Phone numbers (skip)

            // Line 15: Password (skip for security)

            // Line 16: Security level
            if (lines.Length > 15 && int.TryParse(lines[15].Trim(), out int secLevel))
                info.SecurityLevel = secLevel;

            // Line 17: Total times on (skip)

            // Line 18: Last date called (skip)

            // Line 19: Seconds remaining this call (skip, we use minutes)

            // Line 20: Minutes remaining this call
            if (lines.Length > 19 && int.TryParse(lines[19].Trim(), out int timeLeft))
                info.TimeLeftMinutes = timeLeft;

            // Line 21: Graphics mode (GR=Graphics, NG=Non-Graphics)
            if (lines.Length > 20)
            {
                var gfx = lines[20].Trim().ToUpperInvariant();
                info.GraphicsEnabled = gfx == "GR" || gfx.Contains("GRAPH");
                info.Emulation = info.GraphicsEnabled ? TerminalEmulation.ANSI : TerminalEmulation.ASCII;
            }

            // Line 22: Page/screen length
            if (lines.Length > 21 && int.TryParse(lines[21].Trim(), out int pageLen))
                info.ScreenHeight = pageLen > 0 ? pageLen : 24;

            // Line 26: User record number (1-based)
            if (lines.Length > 25 && int.TryParse(lines[25].Trim(), out int userRec))
                info.UserRecordNumber = userRec;

            // Extended DOOR.SYS (lines 32-52)
            if (lines.Length >= 52)
            {
                // Line 36: Sysop name
                if (!string.IsNullOrWhiteSpace(lines[35]))
                    info.SysopName = lines[35].Trim();

                // Line 37: User's handle/alias
                if (!string.IsNullOrWhiteSpace(lines[36]))
                    info.UserAlias = lines[36].Trim();
                else
                    info.UserAlias = info.UserName; // Use real name if no alias
            }
            else
            {
                info.UserAlias = info.UserName;
            }

            return info;
        }

        /// <summary>
        /// Try to detect and parse an unknown drop file format
        /// </summary>
        private static async Task<BBSSessionInfo?> TryParseUnknownAsync(string path)
        {
            if (!File.Exists(path))
                return null;

            var lines = await File.ReadAllLinesAsync(path);

            // DOOR32.SYS has exactly 11 lines and first line is 0, 1, or 2
            if (lines.Length >= 11 && lines.Length <= 15)
            {
                if (int.TryParse(lines[0].Trim(), out int firstLine) && firstLine >= 0 && firstLine <= 2)
                {
                    try
                    {
                        return await ParseDoor32SysAsync(path);
                    }
                    catch { }
                }
            }

            // DOOR.SYS has 31-52 lines and first line usually contains "COM"
            if (lines.Length >= 20 && lines[0].Trim().ToUpperInvariant().Contains("COM"))
            {
                try
                {
                    return await ParseDoorSysAsync(path);
                }
                catch { }
            }

            return null;
        }

        /// <summary>
        /// Create a local session (no BBS, direct console)
        /// </summary>
        public static BBSSessionInfo CreateLocalSession(string playerName = "Player")
        {
            return new BBSSessionInfo
            {
                CommType = ConnectionType.Local,
                UserName = playerName,
                UserAlias = playerName,
                Emulation = TerminalEmulation.ANSI,
                GraphicsEnabled = true,
                TimeLeftMinutes = int.MaxValue,
                SourceType = DropFileType.None
            };
        }
    }
}
