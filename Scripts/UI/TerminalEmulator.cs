using UsurperRemake.Utils;
using UsurperRemake.BBS;
using System;
using System.Collections.Generic;
using System.IO;
using System.Threading.Tasks;
using System.Text;
using System.Linq;

public partial class TerminalEmulator
{
    private const int COLUMNS = 80;
    private const int ROWS = 25;

    private readonly object? display = null;   // Godot RichTextLabel - always null in console mode
    private readonly object? inputLine = null; // Godot LineEdit - always null in console mode

    private Queue<string> outputBuffer = new Queue<string>();
    private int cursorX = 0, cursorY = 0;
    private string currentColor = "white";

    // BBS 80x25 pagination — tracks lines written since last screen clear or user input
    private int _bbsLineCount = 0;
    private const int BBS_PAGE_LIMIT = 23; // Leave 2 rows for "-- More --" prompt

    // BBS idle timeout warning tracking
    private bool _idleWarningShown = false;

    // BBS disconnect detection — consecutive empty reads from a dead connection
    private int _consecutiveEmptyInputs = 0;
    private const int MAX_CONSECUTIVE_EMPTY_INPUTS = 5;

    // Output capture — when enabled, Write/WriteLine append ANSI text to this buffer
    // Used by group combat to capture action output for broadcasting to other players
    private StringBuilder? _captureBuffer;
    public void StartCapture() => _captureBuffer = new StringBuilder();
    public string? StopCapture() { var result = _captureBuffer?.ToString(); _captureBuffer = null; return result; }

    /// <summary>
    /// Show "-- More --" prompt and wait for keypress in BBS door mode.
    /// Resets the line counter so the next page of output can begin.
    /// </summary>
    private void ShowBBSMorePrompt()
    {
        if (ShouldUseBBSAdapter())
        {
            BBSTerminalAdapter.Instance!.Write("-- More --", "bright_yellow");
            BBSTerminalAdapter.Instance!.GetInput("").GetAwaiter().GetResult();
        }
        else
        {
            // Console/stdio BBS mode
            if (DoorMode.ShouldUseAnsiOutput)
                Console.Write($"\x1b[{GetAnsiColorCode("bright_yellow")}m-- More --\x1b[0m");
            else
            {
                var old = Console.ForegroundColor;
                Console.ForegroundColor = ConsoleColor.Yellow;
                Console.Write("-- More --");
                Console.ForegroundColor = old;
            }

            if (Console.IsInputRedirected)
                Console.In.ReadLine();
            else
                Console.ReadLine();
        }
        _bbsLineCount = 0;
    }

    // Stream-based I/O for MUD server mode (each session gets its own reader/writer)
    private StreamReader? _streamReader;
    private StreamWriter? _streamWriter;
    internal readonly object _streamWriterLock = new object(); // Protects all _streamWriter.Write calls
    private readonly SemaphoreSlim _streamReadLock = new(1, 1); // Serializes all _streamReader reads

    /// <summary>Returns true when this terminal is backed by a TCP stream (MUD mode).</summary>
    public bool IsStreamBacked => _streamWriter != null;

    /// <summary>Internal accessor for the stream writer, used by spectator mode to register streams.</summary>
    internal StreamWriter? StreamWriterInternal => _streamWriter;

    // Spectator mode: output is duplicated to all spectator terminals in real-time
    private readonly List<TerminalEmulator> _spectatorTerminals = new();
    private readonly object _spectatorLock = new object();

    /// <summary>
    /// When set, GetInput() runs a background pump that calls this function every ~100ms
    /// to check for incoming messages. Returns a pre-formatted ANSI string, or null if empty.
    /// Only used in MUD stream mode for real-time chat delivery.
    /// </summary>
    public Func<string?>? MessageSource { get; set; }

    /// <summary>
    /// Default constructor for Godot/Console mode (single-player, BBS door).
    /// </summary>
    public TerminalEmulator() { }

    /// <summary>
    /// Create a stream-backed terminal for MUD server sessions.
    /// Each player session gets its own TerminalEmulator connected to their TCP stream.
    /// </summary>
    public TerminalEmulator(System.IO.Stream inputStream, System.IO.Stream outputStream)
    {
        _streamReader = new StreamReader(inputStream, System.Text.Encoding.UTF8);
        _streamWriter = new StreamWriter(outputStream, new System.Text.UTF8Encoding(false))
        {
            AutoFlush = true,
            NewLine = "\r\n" // Telnet/terminal convention
        };
    }
    
    // Static instance property for compatibility
    // In MUD mode, per-session terminal is stored in SessionContext
    private static TerminalEmulator? _fallbackInstance;
    public static TerminalEmulator Instance
    {
        get
        {
            var ctx = UsurperRemake.Server.SessionContext.Current;
            if (ctx?.Terminal != null) return ctx.Terminal;
            return _fallbackInstance!;
        }
        private set
        {
            var ctx = UsurperRemake.Server.SessionContext.Current;
            if (ctx != null) ctx.Terminal = value;
            else _fallbackInstance = value;
        }
    }
    
    // ANSI color mappings - hex strings (was Godot Color, now plain strings)
    private readonly Dictionary<string, string> ansiColors = new Dictionary<string, string>
    {
        { "black", "000000" },
        { "darkred", "800000" },
        { "dark_red", "800000" },
        { "darkgreen", "008000" },
        { "dark_green", "008000" },
        { "darkyellow", "808000" },
        { "dark_yellow", "808000" },
        { "brown", "808000" },
        { "darkblue", "000080" },
        { "dark_blue", "000080" },
        { "darkmagenta", "800080" },
        { "dark_magenta", "800080" },
        { "darkcyan", "008080" },
        { "dark_cyan", "008080" },
        { "gray", "C0C0C0" },
        { "darkgray", "808080" },
        { "dark_gray", "808080" },
        { "red", "FF0000" },
        { "green", "00FF00" },
        { "yellow", "FFFF00" },
        { "blue", "0000FF" },
        { "magenta", "FF00FF" },
        { "cyan", "00FFFF" },
        { "white", "FFFFFF" },
        { "bright_white", "FFFFFF" },
        { "bright_red", "FF6060" },
        { "bright_green", "60FF60" },
        { "bright_yellow", "FFFF60" },
        { "bright_blue", "6060FF" },
        { "bright_magenta", "FF60FF" },
        { "bright_cyan", "60FFFF" }
    };
    
    /// <summary>
    /// Returns true when running in BBS socket mode (non-local, non-stdio).
    /// In this case, all I/O must go through BBSTerminalAdapter → SocketTerminal
    /// instead of Console, because Console output goes to the hidden local window.
    /// </summary>
    private bool ShouldUseBBSAdapter()
    {
        return display == null
            && BBSTerminalAdapter.Instance != null
            && DoorMode.SessionInfo?.CommType != ConnectionType.Local;
    }

    public void _Ready()
    {
        Instance = this; // Set static instance for compatibility
        // display and inputLine remain null — all I/O goes through console/BBS adapter
    }

    private void SetupDisplay()
    {
        // No-op in console mode — Godot UI not used
    }

    private void SetupInput()
    {
        // No-op in console mode — Godot UI not used
    }
    
    public void WriteLine(string text, string color = "white")
    {
        // Capture output for group combat broadcasting
        if (_captureBuffer != null)
        {
            if (text.Contains("[") && text.Contains("[/]"))
            {
                _captureBuffer.Append($"\x1b[{GetAnsiColorCode(color)}m");
                RenderMarkupToStringBuilder(_captureBuffer, text);
                _captureBuffer.AppendLine("\x1b[0m");
            }
            else
            {
                _captureBuffer.AppendLine($"\x1b[{GetAnsiColorCode(color)}m{text}\x1b[0m");
            }
        }

        // BBS 80x25 pagination — pause before overflow (only in BBS door mode, not MUD stream or Godot)
        if (_streamWriter == null && display == null && DoorMode.IsInDoorMode && _bbsLineCount >= BBS_PAGE_LIMIT)
        {
            ShowBBSMorePrompt();
        }

        if (_streamWriter != null)
        {
            // MUD stream mode — write ANSI-colored output to TCP stream
            WriteLineToStream(text, color);
        }
        // display is always null in console mode — Godot branch removed
        else if (ShouldUseBBSAdapter())
        {
            // BBS socket mode - route through BBSTerminalAdapter → SocketTerminal
            BBSTerminalAdapter.Instance!.WriteLine(text, color);
            _bbsLineCount++;
        }
        else
        {
            // Console fallback - parse and render inline color tags
            WriteLineWithColors(text, color);
            _bbsLineCount++;
        }
    }

    /// <summary>
    /// Console-mode output with inline color tag parsing
    /// Handles [colorname]text[/] format for console output
    /// </summary>
    private void WriteLineWithColors(string text, string baseColor)
    {
        if (string.IsNullOrEmpty(text))
        {
            Console.WriteLine();
            return;
        }

        // In door/online mode, use ANSI escape codes since Console.ForegroundColor doesn't travel through SSH
        if (DoorMode.ShouldUseAnsiOutput)
        {
            if (!text.Contains("[") || !text.Contains("[/]"))
            {
                Console.Write($"\x1b[{GetAnsiColorCode(baseColor)}m");
                Console.WriteLine(text);
                Console.Write("\x1b[0m");
            }
            else
            {
                Console.Write($"\x1b[{GetAnsiColorCode(baseColor)}m");
                WriteMarkupToConsoleAnsi(text);
                Console.WriteLine();
                Console.Write("\x1b[0m");
            }
            return;
        }

        // Check if text contains color tags
        if (!text.Contains("[") || !text.Contains("[/]"))
        {
            // No tags, just output with base color
            var oldColor = Console.ForegroundColor;
            Console.ForegroundColor = ColorNameToConsole(baseColor);
            Console.WriteLine(text);
            Console.ForegroundColor = oldColor;
            return;
        }

        // Parse and render with colors
        var savedColor = Console.ForegroundColor;
        Console.ForegroundColor = ColorNameToConsole(baseColor);
        WriteMarkupToConsole(text);
        Console.WriteLine();
        Console.ForegroundColor = savedColor;
    }

    /// <summary>
    /// Parse inline [colorname]text[/] markup and output to console with colors
    /// </summary>
    private void WriteMarkupToConsole(string text)
    {
        var oldColor = Console.ForegroundColor;
        int pos = 0;

        while (pos < text.Length)
        {
            // Look for opening color tag [colorname]
            var tagMatch = System.Text.RegularExpressions.Regex.Match(
                text.Substring(pos), @"^\[([a-z_]+)\]", System.Text.RegularExpressions.RegexOptions.IgnoreCase);

            if (tagMatch.Success)
            {
                string colorName = tagMatch.Groups[1].Value;
                var color = ColorNameToConsole(colorName);
                pos += tagMatch.Length;

                // Find content and closing tag
                int depth = 1;
                int contentStart = pos;
                int contentEnd = pos;

                while (pos < text.Length && depth > 0)
                {
                    // Check for [/]
                    if (pos + 2 <= text.Length - 1 && text[pos] == '[' && text[pos + 1] == '/' && text[pos + 2] == ']')
                    {
                        depth--;
                        if (depth == 0)
                        {
                            contentEnd = pos;
                            pos += 3;
                            break;
                        }
                        pos += 3;
                        continue;
                    }

                    // Check for nested opening tag
                    var nestedMatch = System.Text.RegularExpressions.Regex.Match(
                        text.Substring(pos), @"^\[([a-z_]+)\]", System.Text.RegularExpressions.RegexOptions.IgnoreCase);
                    if (nestedMatch.Success)
                    {
                        depth++;
                        pos += nestedMatch.Length;
                        continue;
                    }

                    pos++;
                }

                // Render content with color
                string content = text.Substring(contentStart, contentEnd - contentStart);
                Console.ForegroundColor = color;
                WriteMarkupToConsole(content); // Recursive for nested tags
                Console.ForegroundColor = oldColor;
                continue;
            }

            // Check for stray [/]
            if (pos + 2 <= text.Length - 1 && text[pos] == '[' && text[pos + 1] == '/' && text[pos + 2] == ']')
            {
                pos += 3;
                continue;
            }

            // Regular character
            Console.Write(text[pos]);
            pos++;
        }

        Console.ForegroundColor = oldColor;
    }

    /// <summary>
    /// Convert simplified [colorname]text[/] format to Godot BBCode [color=#hex]text[/color]
    /// This allows combat messages to use simple color codes that work in both Godot and console modes
    /// </summary>
    private string ConvertSimplifiedColorToBBCode(string text)
    {
        if (string.IsNullOrEmpty(text)) return text;

        // Replace [colorname] with [color=#hex] using actual hex values from ansiColors
        text = System.Text.RegularExpressions.Regex.Replace(
            text,
            @"\[([a-z_]+)\]",
            match =>
            {
                string colorName = match.Groups[1].Value;
                if (ansiColors.TryGetValue(colorName, out string? hex))
                {
                    return $"[color=#{hex}]";
                }
                // Fallback to white if color not found
                return $"[color=#{ansiColors["white"]}]";
            }
        );

        // Replace [/] with [/color]
        text = text.Replace("[/]", "[/color]");

        return text;
    }
    
    // Overload for cases with no text parameter
    public void WriteLine()
    {
        WriteLine("", currentColor);
    }

    // Overload for single string parameter - uses currentColor set by SetColor()
    public void WriteLine(string text)
    {
        WriteLine(text, currentColor);
    }
    
    public void Write(string text, string? color = null)
    {
        // Use current color if no color specified
        string effectiveColor = color ?? currentColor;

        // Capture output for group combat broadcasting
        if (_captureBuffer != null)
        {
            if (text.Contains("[") && text.Contains("[/]"))
            {
                _captureBuffer.Append($"\x1b[{GetAnsiColorCode(effectiveColor)}m");
                RenderMarkupToStringBuilder(_captureBuffer, text);
            }
            else
            {
                _captureBuffer.Append($"\x1b[{GetAnsiColorCode(effectiveColor)}m{text}");
            }
        }

        if (_streamWriter != null)
        {
            // MUD stream mode — write ANSI-colored output to TCP stream
            WriteToStream(text, effectiveColor);
        }
        // display is always null in console mode — Godot branch removed
        else if (ShouldUseBBSAdapter())
        {
            // BBS socket mode - route through BBSTerminalAdapter → SocketTerminal
            BBSTerminalAdapter.Instance!.Write(text, effectiveColor);
        }
        else if (DoorMode.ShouldUseAnsiOutput)
        {
            // In door/online mode, use ANSI escape codes since Console.ForegroundColor doesn't travel through SSH
            Console.Write($"\x1b[{GetAnsiColorCode(effectiveColor)}m");
            Console.Write(text);
            // Don't reset here - let next Write/WriteLine handle color
        }
        else
        {
            // Console fallback – approximate ANSI colour mapping
            ConsoleColor oldColor = Console.ForegroundColor;
            Console.ForegroundColor = ColorNameToConsole(effectiveColor);
            Console.Write(text);
            Console.ForegroundColor = oldColor;
        }

        cursorX += text.Length;
    }

    /// <summary>
    /// Write raw ANSI escape-coded text directly to the output stream.
    /// Used for pre-rendered ANSI art (e.g. race/class portraits).
    /// </summary>
    public void WriteRawAnsi(string text)
    {
        if (_streamWriter != null)
        {
            lock (_streamWriterLock)
            {
                _streamWriter.Write(text);
                _streamWriter.Flush();
            }
            ForwardToSpectators(text);
        }
        else if (display != null)
        {
            // Godot mode removed — display is always null in console mode
        }
        else if (ShouldUseBBSAdapter())
        {
            // BBS adapter: use Write with neutral color; the ANSI codes in text override it
            BBSTerminalAdapter.Instance!.Write(text, "white");
        }
        else
        {
            // Console mode — raw ANSI pass-through (VT processing required)
            Console.Write(text);
        }
    }

    private ConsoleColor ColorNameToConsole(string colorName)
    {
        var resolved = ColorTheme.Resolve(colorName);
        // Full color map supporting dark_*, bright_*, and base color names
        return resolved?.ToLower() switch
        {
            "black" => ConsoleColor.Black,

            // Red variants
            "dark_red" or "darkred" => ConsoleColor.DarkRed,
            "red" => ConsoleColor.Red,
            "bright_red" => ConsoleColor.Red,

            // Green variants
            "dark_green" or "darkgreen" or "dim_green" => ConsoleColor.DarkGreen,
            "green" => ConsoleColor.Green,
            "bright_green" => ConsoleColor.Green,

            // Yellow/Brown variants
            "dark_yellow" or "darkyellow" or "brown" => ConsoleColor.DarkYellow,
            "yellow" => ConsoleColor.Yellow,
            "bright_yellow" => ConsoleColor.Yellow,

            // Blue variants
            "dark_blue" or "darkblue" => ConsoleColor.DarkBlue,
            "blue" => ConsoleColor.Blue,
            "bright_blue" => ConsoleColor.Blue,

            // Magenta variants
            "dark_magenta" or "darkmagenta" => ConsoleColor.DarkMagenta,
            "magenta" => ConsoleColor.Magenta,
            "bright_magenta" => ConsoleColor.Magenta,

            // Cyan variants
            "dark_cyan" or "darkcyan" => ConsoleColor.DarkCyan,
            "cyan" => ConsoleColor.Cyan,
            "bright_cyan" => ConsoleColor.Cyan,

            // Gray/White variants
            "dark_gray" or "darkgray" => ConsoleColor.DarkGray,
            "gray" or "grey" => ConsoleColor.Gray,
            "white" => ConsoleColor.White,
            "bright_white" => ConsoleColor.White,

            null or _ => ConsoleColor.White
        };
    }

    // ANSI color codes for door mode (when Console.ForegroundColor doesn't work)
    private static readonly Dictionary<string, string> AnsiColorCodes = new()
    {
        { "black", "30" },
        { "red", "31" }, { "bright_red", "91" },
        { "green", "32" }, { "bright_green", "92" },
        { "yellow", "33" }, { "bright_yellow", "93" },
        { "blue", "34" }, { "bright_blue", "94" },
        { "magenta", "35" }, { "bright_magenta", "95" },
        { "cyan", "36" }, { "bright_cyan", "96" },
        { "white", "37" }, { "bright_white", "97" },
        { "gray", "90" }, { "grey", "90" },
        { "darkgray", "90" }, { "dark_gray", "90" },
        { "darkred", "31" }, { "dark_red", "31" },
        { "darkgreen", "32" }, { "dark_green", "32" },
        { "dim_green", "2;32" },
        { "darkyellow", "33" }, { "dark_yellow", "33" }, { "brown", "33" },
        { "darkblue", "34" }, { "dark_blue", "34" },
        { "darkmagenta", "35" }, { "dark_magenta", "35" },
        { "darkcyan", "36" }, { "dark_cyan", "36" }
    };

    private string GetAnsiColorCode(string color)
    {
        var resolved = ColorTheme.Resolve(color);
        if (AnsiColorCodes.TryGetValue(resolved?.ToLower() ?? "white", out var code))
            return code;
        return "37"; // Default white
    }

    /// <summary>
    /// Parse inline [colorname]text[/] markup and output to console using ANSI codes (for door mode)
    /// </summary>
    private void WriteMarkupToConsoleAnsi(string text)
    {
        int pos = 0;

        while (pos < text.Length)
        {
            // Look for opening color tag [colorname]
            var tagMatch = System.Text.RegularExpressions.Regex.Match(
                text.Substring(pos), @"^\[([a-z_]+)\]", System.Text.RegularExpressions.RegexOptions.IgnoreCase);

            if (tagMatch.Success)
            {
                string colorName = tagMatch.Groups[1].Value;
                pos += tagMatch.Length;

                // Find content and closing tag
                int depth = 1;
                int contentStart = pos;
                int contentEnd = pos;

                while (pos < text.Length && depth > 0)
                {
                    // Check for [/]
                    if (pos + 2 <= text.Length - 1 && text[pos] == '[' && text[pos + 1] == '/' && text[pos + 2] == ']')
                    {
                        depth--;
                        if (depth == 0)
                        {
                            contentEnd = pos;
                            pos += 3;
                            break;
                        }
                        pos += 3;
                        continue;
                    }

                    // Check for nested opening tag
                    var nestedMatch = System.Text.RegularExpressions.Regex.Match(
                        text.Substring(pos), @"^\[([a-z_]+)\]", System.Text.RegularExpressions.RegexOptions.IgnoreCase);
                    if (nestedMatch.Success)
                    {
                        depth++;
                        pos += nestedMatch.Length;
                        continue;
                    }

                    pos++;
                }

                // Render content with ANSI color
                string content = text.Substring(contentStart, contentEnd - contentStart);
                Console.Write($"\x1b[{GetAnsiColorCode(colorName)}m");
                WriteMarkupToConsoleAnsi(content); // Recursive for nested tags
                Console.Write("\x1b[0m");
                continue;
            }

            // Check for stray [/]
            if (pos + 2 <= text.Length - 1 && text[pos] == '[' && text[pos + 1] == '/' && text[pos + 2] == ']')
            {
                pos += 3;
                continue;
            }

            // Regular character
            Console.Write(text[pos]);
            pos++;
        }
    }

    public void ClearScreen()
    {
        // Screen reader mode: don't clear the screen — screen readers lose their
        // reading position when the buffer is wiped. Instead, output a separator
        // so the screen reader buffer grows naturally and the user hears each new
        // section announced in order.
        if (GameConfig.ScreenReaderMode)
        {
            // Reset line count FIRST so the separator lines don't trigger
            // BBS pagination ("-- More --") which interrupts the screen reader
            _bbsLineCount = 0;
            cursorX = 0;
            cursorY = 0;
            WriteLine("");
            WriteLine("────────────────────────────────────────");
            WriteLine("");
            return;
        }

        if (_streamWriter != null)
        {
            // MUD stream mode — ANSI clear screen
            var clearAnsi = "\x1b[2J\x1b[H";
            lock (_streamWriterLock)
            {
                _streamWriter.Write(clearAnsi);
                _streamWriter.Flush();
            }
            ForwardToSpectators(clearAnsi);
        }
        // display is always null in console mode — Godot branch removed
        else if (ShouldUseBBSAdapter())
        {
            // BBS socket mode - route through BBSTerminalAdapter → SocketTerminal
            BBSTerminalAdapter.Instance!.ClearScreen();
        }
        else if (DoorMode.ShouldUseAnsiOutput)
        {
            // In door/online mode, use ANSI escape codes instead of Console.Clear()
            // Console.Clear() throws when stdin/stdout are redirected pipes
            Console.Write("\x1b[2J\x1b[H"); // Clear screen and move cursor to home
        }
        else
        {
            try
            {
                Console.Clear();
            }
            catch (System.IO.IOException)
            {
                // Fallback to ANSI if Console.Clear fails (redirected I/O)
                Console.Write("\x1b[2J\x1b[H");
            }
        }
        cursorX = 0;
        cursorY = 0;
        _bbsLineCount = 0; // Reset BBS pagination counter
    }

    public void SetCursorPosition(int x, int y)
    {
        cursorX = x;
        cursorY = y;
        // Note: In a full ANSI implementation, this would move cursor
        // For now, we'll just track position for box drawing
    }
    
    public async Task<string> GetInput(string prompt = "> ")
    {
        // MUD stream mode - read from TCP stream
        if (_streamWriter != null && _streamReader != null)
        {
            // Check for wizard /force injected commands before blocking on input
            var forceCtx = UsurperRemake.Server.SessionContext.Current;
            if (forceCtx != null)
            {
                var forceServer = UsurperRemake.Server.MudServer.Instance;
                if (forceServer != null && forceServer.ActiveSessions.TryGetValue(forceCtx.Username.ToLowerInvariant(), out var forceSess)
                    && forceSess.ForcedCommands.TryDequeue(out var forcedCmd))
                {
                    Write(prompt, "bright_white");
                    WriteLine(forcedCmd, "bright_magenta");
                    return forcedCmd;
                }
            }

            Write(prompt, "bright_white");

            // Serialize reads — prevents concurrent ReadLineAsync from
            // GroupFollowerLoop and CombatEngine.ProcessGroupedPlayerTurn
            await _streamReadLock.WaitAsync();
            string? line;
            try
            {
                if (MessageSource != null)
                {
                    // Run message pump concurrently with input reading for real-time chat
                    using var pumpCts = new CancellationTokenSource();
                    var pumpTask = RunMessagePumpAsync(prompt, pumpCts.Token);
                    try
                    {
                        line = await _streamReader.ReadLineAsync();
                    }
                    finally
                    {
                        pumpCts.Cancel();
                        try { await pumpTask; } catch (OperationCanceledException) { }
                    }
                }
                else
                {
                    line = await _streamReader.ReadLineAsync();
                }
            }
            finally
            {
                _streamReadLock.Release();
            }

            // Update idle timeout tracker
            var ctx = UsurperRemake.Server.SessionContext.Current;
            if (ctx != null)
            {
                var server = UsurperRemake.Server.MudServer.Instance;
                if (server != null && server.ActiveSessions.TryGetValue(ctx.Username.ToLowerInvariant(), out var session))
                    session.LastActivityTime = DateTime.UtcNow;
            }

            return line ?? string.Empty;
        }

        // BBS/Online disconnect and timeout checks
        if (DoorMode.IsInDoorMode || DoorMode.IsOnlineMode)
        {
            // Check for detected disconnection (socket closed, stdin EOF, or repeated empty reads)
            if (DoorMode.IsDisconnected)
            {
                await AutoSaveAndExit("connection lost");
                return "";
            }

            // Check for session time expiry (from drop file TimeLeftMinutes)
            if (DoorMode.IsSessionExpired)
            {
                await HandleSessionExpired();
                return "";
            }

            // Check for idle timeout
            if (DoorMode.IsIdleTimedOut)
            {
                await HandleIdleTimeout();
                return "";
            }

            // Show warning 1 minute before idle timeout
            var idleMinutes = (DateTime.UtcNow - DoorMode.LastInputTime).TotalMinutes;
            if (idleMinutes >= DoorMode.IdleTimeoutMinutes - 1 && !_idleWarningShown)
            {
                WriteLine("", "white");
                WriteLine("*** WARNING: You will be disconnected in 1 minute due to inactivity! ***", "bright_yellow");
                WriteLine("", "white");
                _idleWarningShown = true;
            }
        }

        // BBS socket mode - delegate entirely to BBSTerminalAdapter which reads from socket
        if (ShouldUseBBSAdapter())
        {
            _bbsLineCount = 0; // Reset BBS pagination counter on input
            var bbsResult = await BBSTerminalAdapter.Instance!.GetInput(prompt);

            // Only reset idle timer on real input, not empty reads from a dead connection
            if (!string.IsNullOrEmpty(bbsResult))
            {
                DoorMode.LastInputTime = DateTime.UtcNow;
                _idleWarningShown = false;
                _consecutiveEmptyInputs = 0;
            }
            else if (DoorMode.IsDisconnected)
            {
                await AutoSaveAndExit("connection lost");
                return "";
            }
            else
            {
                _consecutiveEmptyInputs++;
                if (_consecutiveEmptyInputs >= MAX_CONSECUTIVE_EMPTY_INPUTS)
                {
                    DoorMode.IsDisconnected = true;
                    await AutoSaveAndExit("connection lost (repeated empty input)");
                    return "";
                }
            }
            return bbsResult;
        }

        Write(prompt, "bright_white");

        // In online/door mode, Console.ReadLine() doesn't handle backspace properly.
        {
            // In online/door mode, Console.ReadLine() doesn't handle backspace properly.
            // On redirected stdin: raw 0x7F/0x08 bytes leak through.
            // On PTY (SSH): .NET console may not process DEL correctly.
            // Use our robust ReadLineWithBackspace() for all remote connections.
            string result;
            if (DoorMode.ShouldUseAnsiOutput || Console.IsInputRedirected)
            {
                result = ReadLineWithBackspace();
            }
            else
            {
                var rawLine = Console.ReadLine();
                if (rawLine == null && (DoorMode.IsInDoorMode || DoorMode.IsOnlineMode))
                    DoorMode.IsDisconnected = true;
                result = rawLine ?? string.Empty;
            }
            _bbsLineCount = 0; // Reset BBS pagination counter on input

            // Update idle timeout tracker for non-BBS door/online modes
            // Only reset on real input — empty reads from dead connections should NOT reset the timer
            if (DoorMode.IsInDoorMode || DoorMode.IsOnlineMode)
            {
                if (!string.IsNullOrEmpty(result))
                {
                    DoorMode.LastInputTime = DateTime.UtcNow;
                    _idleWarningShown = false;
                    _consecutiveEmptyInputs = 0;
                }
                else if (DoorMode.IsDisconnected)
                {
                    await AutoSaveAndExit("connection lost");
                    return "";
                }
                else
                {
                    _consecutiveEmptyInputs++;
                    if (_consecutiveEmptyInputs >= MAX_CONSECUTIVE_EMPTY_INPUTS)
                    {
                        DoorMode.IsDisconnected = true;
                        await AutoSaveAndExit("connection lost (repeated empty input)");
                        return "";
                    }
                }
            }

            return result;
        }
    }

    /// <summary>
    /// Handle idle timeout — auto-save and disconnect the player.
    /// </summary>
    private async Task HandleIdleTimeout()
    {
        WriteLine("", "white");
        WriteLine("*** IDLE TIMEOUT ***", "bright_red");
        WriteLine($"No input received for {DoorMode.IdleTimeoutMinutes} minutes.", "yellow");
        WriteLine("Auto-saving and disconnecting...", "yellow");
        await AutoSaveAndExit("idle timeout");
    }

    /// <summary>
    /// Handle session time expiry — auto-save and disconnect the player.
    /// </summary>
    private async Task HandleSessionExpired()
    {
        WriteLine("", "white");
        WriteLine("*** TIME LIMIT REACHED ***", "bright_red");
        WriteLine("Your session time has expired.", "yellow");
        WriteLine("Auto-saving and disconnecting...", "yellow");
        await AutoSaveAndExit("session time expired");
    }

    /// <summary>
    /// Auto-save the current player and exit the process.
    /// Used by idle timeout and session expiry handlers.
    /// </summary>
    private async Task AutoSaveAndExit(string reason)
    {
        var engine = GameEngine.Instance;
        if (engine?.CurrentPlayer != null)
        {
            try
            {
                string name = engine.CurrentPlayer.Name2 ?? engine.CurrentPlayer.Name1;
                await UsurperRemake.Systems.SaveSystem.Instance.SaveGame(name, engine.CurrentPlayer);
                WriteLine("Game saved.", "bright_green");
            }
            catch (Exception ex)
            {
                UsurperRemake.Systems.DebugLogger.Instance.LogError("TIMEOUT", $"Auto-save failed: {ex.Message}");
            }
            UsurperRemake.Systems.DebugLogger.Instance.LogGameExit(engine.CurrentPlayer.Name, reason);
        }
        else
        {
            UsurperRemake.Systems.DebugLogger.Instance.LogInfo("TIMEOUT", $"Disconnecting ({reason}) — no active player to save");
        }
        await Task.Delay(2000);
        Environment.Exit(0);
    }

    /// <summary>
    /// Read a line with manual backspace handling. Works on redirected stdin, PTY, and console.
    /// Handles DEL (0x7F), BS (0x08), ConsoleKey.Backspace, and ANSI escape sequences.
    /// Used for all online/door mode input where Console.ReadLine() may not handle backspace.
    /// </summary>
    internal static string ReadLineWithBackspace(bool maskPassword = false)
    {
        var buffer = new System.Text.StringBuilder();

        if (Console.IsInputRedirected)
        {
            // In BBS door mode, the BBS software (e.g. Synchronet) echoes characters back
            // to the user through the telnet session. If the game also echoes, the user sees
            // every character twice (e.g. "dd" instead of "d").
            // Backspace handling is tricky: some BBSes echo 0x7F as a visible ⌂ glyph (CP437),
            // others process it as cursor-back. We can't know which, so we use ANSI
            // save/restore cursor to always redraw from the correct position.
            bool bbsEchoes = DoorMode.IsInDoorMode;

            // Save cursor position at the start of the input area (right after prompt)
            if (bbsEchoes)
            {
                Console.Out.Write("\x1b[s"); // ANSI save cursor position
                Console.Out.Flush();
            }

            // Redirected stdin (pipe) - read raw bytes, handle 0x7F/0x08 manually
            while (true)
            {
                int ch = Console.In.Read();
                if (ch == -1 || ch == '\n' || ch == '\r')
                {
                    if (ch == -1)
                        DoorMode.IsDisconnected = true;
                    if (ch == '\r' && Console.In.Peek() == '\n')
                        Console.In.Read();
                    if (!bbsEchoes)
                    {
                        Console.Out.Write("\r\n");
                        Console.Out.Flush();
                    }
                    break;
                }
                else if (ch == 0x7F || ch == 0x08) // DEL or BS
                {
                    bool hadContent = buffer.Length > 0;
                    if (hadContent)
                        buffer.Remove(buffer.Length - 1, 1);

                    if (bbsEchoes)
                    {
                        // Restore cursor to saved position (start of input area),
                        // rewrite the current buffer, then clear to end of line.
                        // This handles all BBS behaviors: ⌂ echo, cursor-back, or anything else.
                        Console.Out.Write("\x1b[u");  // Restore cursor to start of input
                        Console.Out.Write(maskPassword ? new string('*', buffer.Length) : buffer.ToString());
                        Console.Out.Write("\x1b[K");  // Clear from cursor to end of line
                        Console.Out.Flush();
                    }
                    else if (hadContent)
                    {
                        // Non-BBS redirected stdin: standard single backspace erase
                        Console.Out.Write("\b \b");
                        Console.Out.Flush();
                    }
                }
                else if (ch == 0x1B) // ESC - skip escape sequences
                {
                    if (Console.In.Peek() == '[')
                    {
                        Console.In.Read(); // consume '['
                        while (true)
                        {
                            int next = Console.In.Peek();
                            if (next == -1) break;
                            Console.In.Read();
                            if ((next >= 'A' && next <= 'Z') || (next >= 'a' && next <= 'z') || next == '~')
                                break;
                        }
                    }
                }
                else if (ch >= 32) // Printable characters only
                {
                    buffer.Append((char)ch);
                    if (!bbsEchoes)
                    {
                        Console.Out.Write(maskPassword ? '*' : (char)ch);
                        Console.Out.Flush();
                    }
                }
            }
        }
        else
        {
            // PTY or real console - use ReadKey for reliable keystroke capture
            // Console.ReadLine() on a PTY may not handle backspace (0x7F) correctly
            // when .NET's console subsystem interferes with terminal settings.
            while (true)
            {
                try
                {
                    var key = Console.ReadKey(intercept: true);
                    if (key.Key == ConsoleKey.Enter)
                    {
                        Console.Out.Write("\r\n");
                        Console.Out.Flush();
                        break;
                    }
                    else if (key.Key == ConsoleKey.Backspace)
                    {
                        if (buffer.Length > 0)
                        {
                            buffer.Remove(buffer.Length - 1, 1);
                            Console.Out.Write("\b \b");
                            Console.Out.Flush();
                        }
                    }
                    else if (key.KeyChar >= 32) // Printable characters
                    {
                        buffer.Append(key.KeyChar);
                        Console.Out.Write(maskPassword ? '*' : key.KeyChar);
                        Console.Out.Flush();
                    }
                }
                catch (InvalidOperationException)
                {
                    // ReadKey not supported (rare edge case) - fall back to ReadLine
                    return Console.ReadLine() ?? string.Empty;
                }
            }
        }

        return buffer.ToString();
    }
    
    public async Task<int> GetMenuChoice(List<MenuOption> options)
    {
        WriteLine("");
        for (int i = 0; i < options.Count; i++)
        {
            WriteLine($"{i + 1}. {options[i].Text}", "yellow");
        }
        WriteLine("0. Go back", "gray");
        WriteLine("");
        
        while (true)
        {
            var input = await GetInput("Your choice: ");
            if (int.TryParse(input, out int choice))
            {
                if (choice == 0) return -1;
                if (choice > 0 && choice <= options.Count)
                    return choice - 1;
            }
            
            WriteLine("Invalid choice!", "red");
        }
    }
    
    public void ShowASCIIArt(string artName)
    {
        var artPath = $"Assets/ASCII/{artName}.ans";
        if (File.Exists(artPath))
        {
            var content = File.ReadAllText(artPath);
            // Parse ANSI art and display
            DisplayANSI(content);
        }
        else
        {
            // Show ASCII title as fallback
            ShowUsurperTitle();
        }
    }
    
    private void ShowUsurperTitle()
    {
        WriteLine("", "white");
        WriteLine("╔══════════════════════════════════════════════════════════════════════════════╗", "bright_blue");
        WriteLine("║                                                                              ║", "bright_blue");
        WriteLine("║  ██╗   ██╗███████╗██╗   ██╗██████╗ ██████╗ ███████╗██████╗                ║", "bright_red");
        WriteLine("║  ██║   ██║██╔════╝██║   ██║██╔══██╗██╔══██╗██╔════╝██╔══██╗               ║", "bright_red");
        WriteLine("║  ██║   ██║███████╗██║   ██║██████╔╝██████╔╝█████╗  ██████╔╝               ║", "bright_red");
        WriteLine("║  ██║   ██║╚════██║██║   ██║██╔══██╗██╔═══╝ ██╔══╝  ██╔══██╗               ║", "bright_red");
        WriteLine("║  ╚██████╔╝███████║╚██████╔╝██║  ██║██║     ███████╗██║  ██║               ║", "bright_red");
        WriteLine("║   ╚═════╝ ╚══════╝ ╚═════╝ ╚═╝  ╚═╝╚═╝     ╚══════╝╚═╝  ╚═╝               ║", "bright_red");
        WriteLine("║                                                                              ║", "bright_blue");
        WriteLine("║                         ██████╗ ███████╗██████╗  ██████╗ ██████╗ ███╗   ██╗║", "bright_yellow");
        WriteLine("║                         ██╔══██╗██╔════╝██╔══██╗██╔═══██╗██╔══██╗████╗  ██║║", "bright_yellow");
        WriteLine("║                         ██████╔╝█████╗  ██████╔╝██║   ██║██████╔╝██╔██╗ ██║║", "bright_yellow");
        WriteLine("║                         ██╔══██╗██╔══╝  ██╔══██╗██║   ██║██╔══██╗██║╚██╗██║║", "bright_yellow");
        WriteLine("║                         ██║  ██║███████╗██████╔╝╚██████╔╝██║  ██║██║ ╚████║║", "bright_yellow");
        WriteLine("║                         ╚═╝  ╚═╝╚══════╝╚═════╝  ╚═════╝ ╚═╝  ╚═╝╚═╝  ╚═══╝║", "bright_yellow");
        WriteLine("║                                                                              ║", "bright_blue");
        WriteLine("║                          A Classic BBS Door Game Remake                     ║", "bright_cyan");
        WriteLine("║                              With Advanced NPC AI                           ║", "bright_cyan");
        WriteLine("║                                                                              ║", "bright_blue");
        WriteLine("╚══════════════════════════════════════════════════════════════════════════════╝", "bright_blue");
        WriteLine("", "white");
    }
    
    private void DisplayANSI(string ansiContent)
    {
        // Simple ANSI parser - in a full implementation this would be much more complex
        var lines = ansiContent.Split('\n');
        foreach (var line in lines)
        {
            WriteLine(line.Replace("\r", ""), "white");
        }
    }
    
    public void DrawBox(int x, int y, int width, int height, string color = "white")
    {
        // Box drawing characters
        const string TL = "╔"; const string TR = "╗";
        const string BL = "╚"; const string BR = "╝";
        const string V = "║";

        // Draw top
        var topLine = TL + new string('═', width - 2) + TR;
        WriteLine(topLine, color);

        // Draw sides
        for (int i = 1; i < height - 1; i++)
        {
            var middleLine = V + new string(' ', width - 2) + V;
            WriteLine(middleLine, color);
        }

        // Draw bottom
        var bottomLine = BL + new string('═', width - 2) + BR;
        WriteLine(bottomLine, color);
    }
    
    public void ShowStatusBar(string playerName, int level, int hp, int maxHp, int gold, int turns)
    {
        var statusText = $"Player: {playerName} | Level: {level} | HP: {hp}/{maxHp} | Gold: {gold} | Turns: {turns}";
        SetStatusLine(statusText);
    }
    
    /// <summary>
    /// Set status line for compatibility with GameEngine
    /// </summary>
    public void SetStatusLine(string statusText)
    {
        // For now, just display it as a regular line
        // In a full implementation, this would set a persistent status bar
        WriteLine($"[Status] {statusText}", "bright_cyan");
    }
    
    public async Task PressAnyKey(string message = "Press Enter to continue...")
    {
        await GetInput(message);
    }
    
    // Missing API methods for compatibility
    public void SetColor(string color)
    {
        currentColor = color;
        if (_streamWriter != null)
        {
            lock (_streamWriterLock)
            {
                _streamWriter.Write($"\x1b[{GetAnsiColorCode(color)}m");
            }
        }
        else if (ShouldUseBBSAdapter())
        {
            BBSTerminalAdapter.Instance!.SetColor(color);
        }
        else if (DoorMode.ShouldUseAnsiOutput)
        {
            // Emit ANSI color code for stdio door/online mode
            Console.Write($"\x1b[{GetAnsiColorCode(color)}m");
        }
    }
    
    public async Task<string> GetKeyInput()
    {
        // MUD stream mode - use line input since we can't read single keys from TCP
        if (_streamWriter != null && _streamReader != null)
        {
            var input = await GetInput("");
            return string.IsNullOrEmpty(input) ? "" : input[0].ToString();
        }
        // If running inside Godot, use line input and take first char
        else if (inputLine != null && display != null)
        {
            var input = await GetInput("");
            return string.IsNullOrEmpty(input) ? "" : input[0].ToString();
        }
        else if (ShouldUseBBSAdapter())
        {
            // BBS socket mode - read key from socket via BBSTerminalAdapter
            return await BBSTerminalAdapter.Instance!.GetKeyInput();
        }
        else if (DoorMode.ShouldUseAnsiOutput)
        {
            // Door/online mode (stdio) - use line input since ReadKey doesn't work with redirected I/O
            var input = await GetInput("");
            return string.IsNullOrEmpty(input) ? "" : input[0].ToString();
        }
        else
        {
            // Console mode - read single key without Enter
            try
            {
                var keyInfo = Console.ReadKey(intercept: true);
                var result = keyInfo.KeyChar.ToString();
                WriteLine(result, "cyan");
                return result;
            }
            catch (System.InvalidOperationException)
            {
                // Fallback for redirected I/O
                var input = await GetInput("");
                return string.IsNullOrEmpty(input) ? "" : input[0].ToString();
            }
        }
    }
    
    public async Task<string> GetStringInput(string prompt = "")
    {
        return await GetInput(prompt);
    }
    
    public async Task WaitForKeyPress(string message = "Press Enter to continue...")
    {
        await GetInput(message);
    }
    
    // Additional compatibility methods
    public void Clear() => ClearScreen();
    
    // Additional missing async methods
    public async Task<string> GetInputAsync(string prompt = "")
    {
        return await GetInput(prompt);
    }
    
    public async Task<string> ReadLineAsync()
    {
        return await GetInput("");
    }

    /// <summary>
    /// Read a line of input with password masking (shows * for each character).
    /// Works across all I/O modes: MUD stream, BBS stdio, BBS socket, and console.
    /// </summary>
    public async Task<string> GetMaskedInput(string prompt = "")
    {
        // MUD stream mode - read character by character from TCP stream
        if (_streamWriter != null && _streamReader != null)
        {
            if (!string.IsNullOrEmpty(prompt))
                Write(prompt, "bright_white");

            await _streamReadLock.WaitAsync();
            try
            {
                var buffer = new System.Text.StringBuilder();
                var charBuf = new char[1];

                while (true)
                {
                    int read = await _streamReader.ReadAsync(charBuf, 0, 1);
                    if (read == 0) break; // EOF

                    char ch = charBuf[0];
                    if (ch == '\r' || ch == '\n')
                    {
                        // Consume trailing \n after \r if present
                        if (ch == '\r' && !_streamReader.EndOfStream)
                        {
                            char[] peek = new char[1];
                            // Can't easily peek StreamReader, just break on \r
                        }
                        lock (_streamWriterLock)
                        {
                            _streamWriter.Write("\r\n");
                            _streamWriter.Flush();
                        }
                        break;
                    }
                    else if (ch == (char)0x7F || ch == (char)0x08) // DEL or BS
                    {
                        if (buffer.Length > 0)
                        {
                            buffer.Remove(buffer.Length - 1, 1);
                            lock (_streamWriterLock)
                            {
                                _streamWriter.Write("\b \b");
                                _streamWriter.Flush();
                            }
                        }
                    }
                    else if (ch >= ' ') // Printable
                    {
                        buffer.Append(ch);
                        lock (_streamWriterLock)
                        {
                            _streamWriter.Write('*');
                            _streamWriter.Flush();
                        }
                    }
                }
                return buffer.ToString();
            }
            finally
            {
                _streamReadLock.Release();
            }
        }

        // BBS socket mode - delegate to BBSTerminalAdapter
        if (ShouldUseBBSAdapter())
        {
            if (!string.IsNullOrEmpty(prompt))
                BBSTerminalAdapter.Instance!.Write(prompt, "bright_white");
            return await Task.Run(() => ReadLineWithBackspace(maskPassword: true));
        }

        // Console/stdio mode - use ReadLineWithBackspace with masking
        if (!string.IsNullOrEmpty(prompt))
            Write(prompt, "bright_white");
        return await Task.Run(() => ReadLineWithBackspace(maskPassword: true));
    }
    
    public async Task<string> ReadKeyAsync()
    {
        return await GetKeyInput();
    }
    
    // Additional missing async methods for API compatibility
    public async Task WriteLineAsync(string text = "")
    {
        WriteLine(text);
        await Task.CompletedTask;
    }
    
    public async Task WriteColorLineAsync(string text, string color)
    {
        WriteLine(text, color);
        await Task.CompletedTask;
    }
    
    public async Task WriteAsync(string text)
    {
        Write(text);
        await Task.CompletedTask;
    }
    
    public async Task WriteColorAsync(string text, string color)
    {
        Write(text, color);
        await Task.CompletedTask;
    }
    
    /// <summary>
    /// Get a single character of input from the user (first character typed).
    /// </summary>
    /// <remarks>
    /// Older Pascal code frequently worked with single-character commands.  Returning the first
    /// character of the line that the user enters gives the same behaviour while still allowing
    /// users to press <Enter> as usual.  If the user simply presses <Enter> we return a null-char
    /// (\0) so the caller can treat it as a cancel/no-input event.
    /// </remarks>
    public async Task<char> GetCharAsync()
    {
        var input = await GetKeyInput();
        return string.IsNullOrEmpty(input) ? '\0' : input[0];
    }
    
    /// <summary>
    /// Convenience alias – behaves exactly the same as <see cref="GetCharAsync"/> but has a more
    /// descriptive name when reading yes/no style keystrokes.
    /// </summary>
    public async Task<char> GetKeyCharAsync() => await GetCharAsync();
    
    // Missing methods that are being called throughout the codebase
    public async Task<bool> ConfirmAsync(string message = "Are you sure? (Y/N): ")
    {
        while (true)
        {
            WriteLine(message, "yellow");
            var input = await GetInput();
            var response = input.ToUpper().Trim();
            
            if (response == "Y" || response == "YES")
                return true;
            if (response == "N" || response == "NO")
                return false;
                
            WriteLine("Please answer Y or N.", "red");
        }
    }
    
    // Overload for ConfirmAsync that takes a boolean parameter
    public async Task<bool> ConfirmAsync(string message, bool defaultValue)
    {
        while (true)
        {
            string prompt = defaultValue ? $"{message} (Y/n): " : $"{message} (y/N): ";
            WriteLine(prompt, "yellow");
            var input = await GetInput();
            var response = input.ToUpper().Trim();
            
            if (string.IsNullOrEmpty(response))
                return defaultValue;
            
            if (response == "Y" || response == "YES")
                return true;
            if (response == "N" || response == "NO")
                return false;
                
            WriteLine("Please answer Y or N.", "red");
        }
    }
    
    public async Task<string> GetStringAsync(string prompt = "")
    {
        return await GetInput(prompt);
    }
    
    public async Task<int> GetNumberInput(string prompt = "", int min = 0, int max = int.MaxValue)
    {
        while (true)
        {
            var input = await GetInput(prompt);
            if (int.TryParse(input, out int result))
            {
                if (result >= min && result <= max)
                    return result;
                WriteLine($"Please enter a number between {min} and {max}.", "red");
            }
            else
            {
                WriteLine("Please enter a valid number.", "red");
            }
        }
    }
    
    /// <summary>
    /// Overload that omits the prompt string – maintains backwards-compatibility with legacy code that
    /// expected the Pascal signature GetNumberInput(min, max).
    /// </summary>
    public async Task<int> GetNumberInput(int min, int max)
    {
        return await GetNumberInput("", min, max);
    }
    
    // Add DisplayMessage method to handle ConsoleColor parameters
    public void DisplayMessage(string message, ConsoleColor color, bool newLine = true)
    {
        string colorName = color switch
        {
            ConsoleColor.Red => "red",
            ConsoleColor.Green => "green",
            ConsoleColor.Blue => "blue",
            ConsoleColor.Yellow => "yellow",
            ConsoleColor.Cyan => "cyan",
            ConsoleColor.Magenta => "magenta",
            ConsoleColor.White => "white",
            ConsoleColor.Gray => "gray",
            ConsoleColor.DarkGray => "darkgray",
            ConsoleColor.DarkRed => "darkred",
            ConsoleColor.DarkGreen => "darkgreen",
            ConsoleColor.DarkBlue => "darkblue",
            ConsoleColor.DarkYellow => "darkyellow",
            ConsoleColor.DarkCyan => "darkcyan",
            ConsoleColor.DarkMagenta => "darkmagenta",
            ConsoleColor.Black => "black",
            _ => "white"
        };
        
        if (newLine)
            WriteLine(message, colorName);
        else
            Write(message, colorName);
    }
    
    public void DisplayMessage(string message, string color, bool newLine = true)
    {
        if (newLine)
            WriteLine(message, color);
        else
            Write(message, color);
    }
    
    // Overload for DisplayMessage that takes 3 arguments with ConsoleColor
    public void DisplayMessage(string message, string color, ConsoleColor backgroundColor)
    {
        // For now, ignore the background color and just display the message
        WriteLine(message, color);
    }
    
    public void DisplayMessage(string message)
    {
        WriteLine(message, "white");
    }

    // Additional missing API methods for compatibility
    // WARNING: These synchronous methods can cause deadlocks in async contexts.
    // Use GetInput()/GetKeyInput() async methods when possible.
    [Obsolete("Use GetInput() async method instead to avoid potential deadlocks")]
    public string ReadLine()
    {
        // Use GetAwaiter().GetResult() instead of .Result - slightly safer in some contexts
        // but still blocking. Callers should migrate to async versions.
        return GetInput().GetAwaiter().GetResult();
    }

    [Obsolete("Use GetKeyInput() async method instead to avoid potential deadlocks")]
    public string ReadKey()
    {
        // Use GetAwaiter().GetResult() instead of .Result - slightly safer in some contexts
        // but still blocking. Callers should migrate to async versions.
        return GetKeyInput().GetAwaiter().GetResult();
    }

    public async Task ClearScreenAsync()
    {
        ClearScreen();
        await Task.CompletedTask;
    }

    public static string ColorWhite = "white";
    public static string ColorCyan = "cyan";
    public static string ColorGreen = "green";
    public static string ColorRed = "red";
    public static string ColorYellow = "yellow";
    public static string ColorBlue = "blue";
    public static string ColorMagenta = "magenta";
    public static string ColorDarkGray = "darkgray";
    
    // Missing methods causing CS1061 errors
    public void ShowANSIArt(string artName)
    {
        ShowASCIIArt(artName); // Delegate to existing method
    }
    
    /// <summary>
    /// Background read task for detecting input on redirected stdin (BBS stdio mode).
    /// Started lazily by IsInputAvailable() when stdin is redirected.
    /// Completes when ANY byte arrives, signaling "user pressed a key".
    /// </summary>
    private Task<int>? _stdinReadTask;

    /// <summary>
    /// Non-blocking check if input is available. Used by auto-combat to detect "stop" key presses.
    /// Returns true if there is pending input that can be read.
    /// </summary>
    public bool IsInputAvailable()
    {
        try
        {
            // MUD stream mode - check if the underlying stream has data
            if (_streamReader != null)
            {
                return _streamReader.BaseStream.CanRead &&
                       (_streamReader.BaseStream is System.Net.Sockets.NetworkStream ns
                           ? ns.DataAvailable
                           : _streamReader.Peek() != -1);
            }

            // Console mode (local)
            if (!Console.IsInputRedirected)
            {
                return Console.KeyAvailable;
            }

            // Redirected stdin (BBS stdio mode) — start a background read and check if it completed
            if (_stdinReadTask == null)
            {
                var buf = new char[1];
                _stdinReadTask = Console.In.ReadAsync(buf, 0, 1);
            }
            return _stdinReadTask.IsCompleted;
        }
        catch
        {
            return false;
        }
    }

    /// <summary>
    /// Consume any pending input without blocking. Call after IsInputAvailable() returns true
    /// to clear the buffer so the input doesn't leak into the next prompt.
    /// </summary>
    public void FlushPendingInput()
    {
        try
        {
            // Clear the background stdin read task if it completed
            if (_stdinReadTask != null && _stdinReadTask.IsCompleted)
            {
                _stdinReadTask = null;
            }

            if (_streamReader != null)
            {
                // Read whatever is buffered
                while (_streamReader.BaseStream is System.Net.Sockets.NetworkStream ns && ns.DataAvailable)
                {
                    _streamReader.Read();
                }
                return;
            }

            if (!Console.IsInputRedirected)
            {
                while (Console.KeyAvailable)
                {
                    Console.ReadKey(intercept: true);
                }
            }
        }
        catch { /* swallow */ }
    }

    public async Task WaitForKey()
    {
        await GetKeyInput();
    }
    
    public async Task WaitForKey(string message)
    {
        await PressAnyKey(message);
    }

    // Added overloads to accept ConsoleColor for legacy compatibility
    public void WriteLine(string text, ConsoleColor color, bool newLine = true)
    {
        var colorName = color.ToString().ToLower();
        if (newLine)
            WriteLine(text, colorName);
        else
            Write(text, colorName);
    }

    public void WriteLine(string text, ConsoleColor color)
    {
        WriteLine(text, color.ToString().ToLower());
    }

    public void Write(string text, ConsoleColor color)
    {
        Write(text, color.ToString().ToLower());
    }

    public void SetColor(ConsoleColor color)
    {
        SetColor(color.ToString().ToLower());
    }

    // Synchronous helper for legacy code paths
    public string GetInputSync(string prompt = "> ")
    {
        var result = GetInput(prompt).GetAwaiter().GetResult();
        _bbsLineCount = 0; // Reset BBS pagination counter on input
        return result;
    }

    // ═══════════════════════════════════════════════════════════════════
    // REAL-TIME MESSAGE PUMP (MUD mode only)
    // ═══════════════════════════════════════════════════════════════════

    /// <summary>
    /// Background task that polls MessageSource every 100ms and writes incoming
    /// messages directly to the TCP stream while ReadLineAsync is pending.
    /// Moves to a new line (preserving the player's partially-typed input),
    /// writes message(s), then redraws the prompt below.
    /// </summary>
    private async Task RunMessagePumpAsync(string prompt, CancellationToken ct)
    {
        if (_streamWriter == null || MessageSource == null) return;

        try
        {
            while (!ct.IsCancellationRequested)
            {
                await Task.Delay(100, ct);

                bool anyWritten = false;
                string? msg;
                while ((msg = MessageSource()) != null)
                {
                    // Move to a new line instead of erasing — preserves any text the
                    // player has typed so far (it scrolls up but stays in the PTY buffer)
                    lock (_streamWriterLock)
                    {
                        _streamWriter.Write("\r\n");       // new line, preserving current input
                        _streamWriter.Write(msg);
                        _streamWriter.Write("\r\n\x1b[0m"); // newline + reset color
                    }
                    anyWritten = true;
                }

                if (anyWritten)
                {
                    // Redraw the prompt on the new line
                    lock (_streamWriterLock)
                    {
                        _streamWriter.Write($"\x1b[{GetAnsiColorCode("bright_white")}m{prompt}");
                        _streamWriter.Flush();
                    }
                }
            }
        }
        catch (OperationCanceledException) { /* Normal cancellation when input received */ }
    }

    // ═══════════════════════════════════════════════════════════════════
    // SPECTATOR OUTPUT FORWARDING
    // ═══════════════════════════════════════════════════════════════════

    /// <summary>Add a spectator's stream writer to receive duplicated output.</summary>
    public void AddSpectatorStream(TerminalEmulator spectatorTerminal)
    {
        lock (_spectatorLock)
        {
            if (!_spectatorTerminals.Contains(spectatorTerminal))
                _spectatorTerminals.Add(spectatorTerminal);
        }
    }

    /// <summary>Remove a spectator terminal.</summary>
    public void RemoveSpectatorStream(TerminalEmulator spectatorTerminal)
    {
        lock (_spectatorLock)
        {
            _spectatorTerminals.Remove(spectatorTerminal);
        }
    }

    /// <summary>Remove a spectator's stream writer (backward compat).</summary>
    public void RemoveSpectatorStream(StreamWriter writer)
    {
        lock (_spectatorLock)
        {
            _spectatorTerminals.RemoveAll(t => t.StreamWriterInternal == writer);
        }
    }

    /// <summary>Remove spectator by PlayerSession reference.</summary>
    public void RemoveSpectatorStream(UsurperRemake.Server.PlayerSession session)
    {
        var term = session.Context?.Terminal;
        if (term != null) RemoveSpectatorStream(term);
    }

    /// <summary>Remove all spectator streams.</summary>
    public void ClearSpectatorStreams()
    {
        lock (_spectatorLock)
        {
            _spectatorTerminals.Clear();
        }
    }

    /// <summary>Forward raw ANSI output to all spectator streams (thread-safe).</summary>
    private void ForwardToSpectators(string rawAnsi)
    {
        List<TerminalEmulator> snapshot;
        lock (_spectatorLock)
        {
            if (_spectatorTerminals.Count == 0) return;
            snapshot = new List<TerminalEmulator>(_spectatorTerminals);
        }

        foreach (var spectator in snapshot)
        {
            try
            {
                var sw = spectator.StreamWriterInternal;
                if (sw == null) continue;
                lock (spectator._streamWriterLock)
                {
                    sw.Write(rawAnsi);
                    sw.Flush();
                }
            }
            catch
            {
                // Spectator disconnected — remove silently
                lock (_spectatorLock) { _spectatorTerminals.Remove(spectator); }
            }
        }
    }

    /// <summary>Render markup text to a StringBuilder (for spectator forwarding).</summary>
    private void RenderMarkupToStringBuilder(StringBuilder sb, string text)
    {
        int pos = 0;
        while (pos < text.Length)
        {
            var tagMatch = System.Text.RegularExpressions.Regex.Match(
                text.Substring(pos), @"^\[([a-z_]+)\]", System.Text.RegularExpressions.RegexOptions.IgnoreCase);

            if (tagMatch.Success)
            {
                string colorName = tagMatch.Groups[1].Value;
                pos += tagMatch.Length;
                int depth = 1;
                int contentStart = pos;
                int contentEnd = pos;

                while (pos < text.Length && depth > 0)
                {
                    if (pos + 2 <= text.Length - 1 && text[pos] == '[' && text[pos + 1] == '/' && text[pos + 2] == ']')
                    {
                        depth--;
                        if (depth == 0) { contentEnd = pos; pos += 3; break; }
                        pos += 3;
                        continue;
                    }
                    var nestedMatch = System.Text.RegularExpressions.Regex.Match(
                        text.Substring(pos), @"^\[([a-z_]+)\]", System.Text.RegularExpressions.RegexOptions.IgnoreCase);
                    if (nestedMatch.Success) { depth++; pos += nestedMatch.Length; continue; }
                    pos++;
                }

                string content = text.Substring(contentStart, contentEnd - contentStart);
                sb.Append($"\x1b[{GetAnsiColorCode(colorName)}m");
                RenderMarkupToStringBuilder(sb, content);
                sb.Append("\x1b[0m");
                continue;
            }

            if (pos + 2 <= text.Length - 1 && text[pos] == '[' && text[pos + 1] == '/' && text[pos + 2] == ']')
            {
                pos += 3;
                continue;
            }

            sb.Append(text[pos]);
            pos++;
        }
    }

    // ═══════════════════════════════════════════════════════════════════
    // MUD STREAM I/O HELPERS
    // ═══════════════════════════════════════════════════════════════════

    /// <summary>Write a line with ANSI color to the MUD TCP stream.</summary>
    private void WriteLineToStream(string text, string baseColor)
    {
        if (_streamWriter == null) return;

        if (string.IsNullOrEmpty(text))
        {
            lock (_streamWriterLock)
            {
                _streamWriter.WriteLine();
            }
            ForwardToSpectators("\r\n");
            return;
        }

        // Check for inline color tags
        if (!text.Contains("[") || !text.Contains("[/]"))
        {
            var ansi = $"\x1b[{GetAnsiColorCode(baseColor)}m{text}\r\n\x1b[0m";
            lock (_streamWriterLock)
            {
                _streamWriter.Write(ansi);
            }
            ForwardToSpectators(ansi);
        }
        else
        {
            // Build the full ANSI string, then write atomically
            var sb = new StringBuilder();
            sb.Append($"\x1b[{GetAnsiColorCode(baseColor)}m");
            RenderMarkupToStringBuilder(sb, text);
            sb.Append("\r\n\x1b[0m");
            var fullAnsi = sb.ToString();

            lock (_streamWriterLock)
            {
                _streamWriter.Write(fullAnsi);
            }

            // Forward to spectators
            ForwardToSpectators(fullAnsi);
        }
    }

    /// <summary>Write text (no newline) with ANSI color to the MUD TCP stream.</summary>
    private void WriteToStream(string text, string color)
    {
        if (_streamWriter == null) return;
        var ansi = $"\x1b[{GetAnsiColorCode(color)}m{text}";
        lock (_streamWriterLock)
        {
            _streamWriter.Write(ansi);
        }
        ForwardToSpectators(ansi);
    }

    /// <summary>Parse [colorname]text[/] markup and write to the MUD TCP stream using ANSI codes.</summary>
    private void WriteMarkupToStream(string text)
    {
        if (_streamWriter == null) return;
        int pos = 0;

        while (pos < text.Length)
        {
            var tagMatch = System.Text.RegularExpressions.Regex.Match(
                text.Substring(pos), @"^\[([a-z_]+)\]", System.Text.RegularExpressions.RegexOptions.IgnoreCase);

            if (tagMatch.Success)
            {
                string colorName = tagMatch.Groups[1].Value;
                pos += tagMatch.Length;
                int depth = 1;
                int contentStart = pos;
                int contentEnd = pos;

                while (pos < text.Length && depth > 0)
                {
                    if (pos + 2 <= text.Length - 1 && text[pos] == '[' && text[pos + 1] == '/' && text[pos + 2] == ']')
                    {
                        depth--;
                        if (depth == 0) { contentEnd = pos; pos += 3; break; }
                        pos += 3;
                        continue;
                    }
                    var nestedMatch = System.Text.RegularExpressions.Regex.Match(
                        text.Substring(pos), @"^\[([a-z_]+)\]", System.Text.RegularExpressions.RegexOptions.IgnoreCase);
                    if (nestedMatch.Success) { depth++; pos += nestedMatch.Length; continue; }
                    pos++;
                }

                string content = text.Substring(contentStart, contentEnd - contentStart);
                _streamWriter.Write($"\x1b[{GetAnsiColorCode(colorName)}m");
                WriteMarkupToStream(content);
                _streamWriter.Write("\x1b[0m");
                continue;
            }

            if (pos + 2 <= text.Length - 1 && text[pos] == '[' && text[pos + 1] == '/' && text[pos + 2] == ']')
            {
                pos += 3;
                continue;
            }

            _streamWriter.Write(text[pos]);
            pos++;
        }
    }
} 
