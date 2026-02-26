using System;
using System.Collections.Generic;
using System.IO;
using System.Net.Sockets;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading.Tasks;
using Microsoft.Win32.SafeHandles;

namespace UsurperRemake.BBS
{
    /// <summary>
    /// Terminal I/O implementation that reads/writes to an inherited socket handle
    /// Used when running as a BBS door game
    /// </summary>
    public class SocketTerminal : IDisposable
    {
        private Socket? _socket;
        private NetworkStream? _stream;
        private StreamReader? _reader;
        private StreamWriter? _writer;
        private FileStream? _rawHandleStream; // Fallback for raw handle I/O
        private readonly BBSSessionInfo _sessionInfo;
        private bool _disposed = false;
        private bool _usingRawHandle = false; // True if using FileStream fallback
        private string _currentColor = "white";

        // Windows API imports for handle validation and duplication
        [DllImport("kernel32.dll", SetLastError = true)]
        private static extern int GetFileType(IntPtr hFile);

        [DllImport("kernel32.dll", SetLastError = true)]
        private static extern bool GetHandleInformation(IntPtr hObject, out uint lpdwFlags);

        [DllImport("kernel32.dll", SetLastError = true)]
        private static extern bool DuplicateHandle(
            IntPtr hSourceProcessHandle,
            IntPtr hSourceHandle,
            IntPtr hTargetProcessHandle,
            out IntPtr lpTargetHandle,
            uint dwDesiredAccess,
            bool bInheritHandle,
            uint dwOptions);

        [DllImport("kernel32.dll")]
        private static extern IntPtr GetCurrentProcess();

        private const int FILE_TYPE_UNKNOWN = 0x0000;
        private const int FILE_TYPE_DISK = 0x0001;
        private const int FILE_TYPE_CHAR = 0x0002;
        private const int FILE_TYPE_PIPE = 0x0003;
        private const uint DUPLICATE_SAME_ACCESS = 0x00000002;

        // ANSI escape codes
        private const string ESC = "\x1b";
        private const string CSI = "\x1b[";

        // ANSI color codes
        // BBS-compatible ANSI color codes using bold attribute (SGR 1) for bright colors.
        // Traditional BBS terminals expect ESC[1;33m (bold+yellow) not ESC[93m (extended bright yellow).
        // Uses "0;" prefix on standard colors to reset bold when transitioning from bright colors.
        private static readonly Dictionary<string, string> AnsiColors = new()
        {
            // Standard colors with explicit reset (0;3X clears bold)
            { "black", "0;30" },
            { "red", "0;31" },
            { "green", "0;32" },
            { "yellow", "0;33" },
            { "blue", "0;34" },
            { "magenta", "0;35" },
            { "cyan", "0;36" },
            { "white", "0;37" },
            { "gray", "0;37" },
            { "grey", "0;37" },

            // Dark variants with explicit reset
            { "darkred", "0;31" },
            { "dark_red", "0;31" },
            { "darkgreen", "0;32" },
            { "dark_green", "0;32" },
            { "darkyellow", "0;33" },
            { "dark_yellow", "0;33" },
            { "brown", "0;33" },
            { "darkblue", "0;34" },
            { "dark_blue", "0;34" },
            { "darkmagenta", "0;35" },
            { "dark_magenta", "0;35" },
            { "darkcyan", "0;36" },
            { "dark_cyan", "0;36" },
            { "darkgray", "1;30" },
            { "dark_gray", "1;30" },
            { "darkgrey", "1;30" },

            // Bright variants using bold (1;3X)
            { "bright_black", "1;30" },
            { "bright_red", "1;31" },
            { "bright_green", "1;32" },
            { "bright_yellow", "1;33" },
            { "bright_blue", "1;34" },
            { "bright_magenta", "1;35" },
            { "bright_cyan", "1;36" },
            { "bright_white", "1;37" }
        };

        // Unicode to CP437 character mapping for BBS compatibility
        // Maps Unicode box-drawing and special characters to their CP437 byte equivalents
        private static readonly Dictionary<char, byte> UnicodeToCp437 = new()
        {
            // Box drawing - single line
            { '─', 196 }, // Horizontal line
            { '│', 179 }, // Vertical line
            { '┌', 218 }, // Top-left corner
            { '┐', 191 }, // Top-right corner
            { '└', 192 }, // Bottom-left corner
            { '┘', 217 }, // Bottom-right corner
            { '├', 195 }, // Left T
            { '┤', 180 }, // Right T
            { '┬', 194 }, // Top T
            { '┴', 193 }, // Bottom T
            { '┼', 197 }, // Cross

            // Box drawing - double line
            { '═', 205 }, // Double horizontal
            { '║', 186 }, // Double vertical
            { '╔', 201 }, // Double top-left
            { '╗', 187 }, // Double top-right
            { '╚', 200 }, // Double bottom-left
            { '╝', 188 }, // Double bottom-right
            { '╠', 204 }, // Double left T
            { '╣', 185 }, // Double right T
            { '╦', 203 }, // Double top T
            { '╩', 202 }, // Double bottom T
            { '╬', 206 }, // Double cross

            // Box drawing - mixed single/double
            { '╒', 213 }, // Down single, right double
            { '╓', 214 }, // Down double, right single
            { '╕', 184 }, // Down single, left double
            { '╖', 183 }, // Down double, left single
            { '╘', 212 }, // Up single, right double
            { '╙', 211 }, // Up double, right single
            { '╛', 190 }, // Up single, left double
            { '╜', 189 }, // Up double, left single
            { '╞', 198 }, // Vertical single, right double
            { '╟', 199 }, // Vertical double, right single
            { '╡', 181 }, // Vertical single, left double
            { '╢', 182 }, // Vertical double, left single
            { '╤', 209 }, // Down single, horizontal double
            { '╥', 210 }, // Down double, horizontal single
            { '╧', 207 }, // Up single, horizontal double
            { '╨', 208 }, // Up double, horizontal single
            { '╪', 216 }, // Vertical single, horizontal double
            { '╫', 215 }, // Vertical double, horizontal single

            // Block elements
            { '█', 219 }, // Full block
            { '▄', 220 }, // Lower half block
            { '▀', 223 }, // Upper half block
            { '▌', 221 }, // Left half block
            { '▐', 222 }, // Right half block
            { '░', 176 }, // Light shade
            { '▒', 177 }, // Medium shade
            { '▓', 178 }, // Dark shade

            // Arrows
            { '↑', 24 },
            { '↓', 25 },
            { '→', 26 },
            { '←', 27 },
            { '↔', 29 },
            { '↕', 18 },

            // Math and symbols
            { '≡', 240 }, // Identical to
            { '±', 241 }, // Plus-minus
            { '≥', 242 }, // Greater than or equal
            { '≤', 243 }, // Less than or equal
            { '÷', 246 }, // Division
            { '≈', 247 }, // Almost equal
            { '°', 248 }, // Degree
            { '•', 249 }, // Bullet
            { '·', 250 }, // Middle dot
            { '√', 251 }, // Square root
            { '²', 253 }, // Superscript 2
            { '■', 254 }, // Black square

            // Currency and misc
            { '¢', 155 },
            { '£', 156 },
            { '¥', 157 },
            { '₧', 158 }, // Peseta
            { 'ƒ', 159 }, // Florin

            // Greek letters (commonly used)
            { 'α', 224 },
            { 'β', 225 },
            { 'Γ', 226 },
            { 'π', 227 },
            { 'Σ', 228 },
            { 'σ', 229 },
            { 'µ', 230 },
            { 'τ', 231 },
            { 'Φ', 232 },
            { 'Θ', 233 },
            { 'Ω', 234 },
            { 'δ', 235 },
            { '∞', 236 },
            { 'φ', 237 },
            { 'ε', 238 },
            { '∩', 239 },

            // Special characters
            { '♠', 6 },
            { '♣', 5 },
            { '♥', 3 },
            { '♦', 4 },
            { '☺', 1 },
            { '☻', 2 },
            { '☼', 15 },
            { '♪', 13 },
            { '♫', 14 },

            // Accented vowels (common ones)
            { 'á', 160 },
            { 'í', 161 },
            { 'ó', 162 },
            { 'ú', 163 },
            { 'ñ', 164 },
            { 'Ñ', 165 },
            { 'ª', 166 },
            { 'º', 167 },
            { '¿', 168 },
            { '¡', 173 },
            { 'ä', 132 },
            { 'Ä', 142 },
            { 'ö', 148 },
            { 'Ö', 153 },
            { 'ü', 129 },
            { 'Ü', 154 },
            { 'é', 130 },
            { 'è', 138 },
            { 'ê', 136 },
            { 'ë', 137 },
            { 'â', 131 },
            { 'à', 133 },
            { 'ç', 135 },
            { 'Ç', 128 },
        };

        public bool IsConnected => _socket?.Connected ?? false;
        public BBSSessionInfo SessionInfo => _sessionInfo;

        /// <summary>
        /// Get the raw output stream for direct byte-level relay (bypasses all encoding/color).
        /// Returns the raw handle stream or network stream, whichever is active.
        /// Returns null if in stdio/local mode (no socket).
        /// </summary>
        public Stream? GetRawOutputStream()
        {
            if (_usingRawHandle && _rawHandleStream != null)
                return _rawHandleStream;
            if (_stream != null)
                return _stream;
            return null;
        }

        public SocketTerminal(BBSSessionInfo sessionInfo)
        {
            _sessionInfo = sessionInfo;
        }

        /// <summary>
        /// Verbose logging for debugging
        /// </summary>
        public static bool VerboseLogging { get; set; } = false;

        private static void LogVerbose(string message)
        {
            if (VerboseLogging)
                Console.Error.WriteLine($"[SOCKET] {message}");
        }

        /// <summary>
        /// Initialize the socket from the inherited handle in the session info
        /// </summary>
        public bool Initialize()
        {
            LogVerbose($"Initialize() called, CommType={_sessionInfo.CommType}");

            if (_sessionInfo.CommType == ConnectionType.Local)
            {
                LogVerbose("Local mode - no socket needed");
                return true;
            }

            LogVerbose($"Socket handle from drop file: {_sessionInfo.SocketHandle}");
            LogVerbose($"Handle as hex: 0x{_sessionInfo.SocketHandle:X8}");

            // Note: Socket handles on Windows can be any positive integer
            // Handle 0 is technically valid but usually means "no handle"
            if (_sessionInfo.SocketHandle < 0)
            {
                Console.Error.WriteLine($"Invalid socket handle: {_sessionInfo.SocketHandle}");
                LogVerbose("Handle is negative - this is definitely invalid");
                return false;
            }

            if (_sessionInfo.SocketHandle == 0)
            {
                Console.Error.WriteLine("Socket handle is 0 - this usually means no socket was passed");
                LogVerbose("Handle is 0 - the BBS may not be passing a socket handle");
                LogVerbose("Try using --stdio flag instead for Standard I/O mode");
                return false;
            }

            // On Windows, perform additional handle diagnostics
            if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
            {
                DiagnoseWindowsHandle(_sessionInfo.SocketHandle);
            }

            try
            {
                // Create socket from inherited handle
                LogVerbose($"Attempting to create socket from handle {_sessionInfo.SocketHandle}...");
                _socket = CreateSocketFromHandle(_sessionInfo.SocketHandle);

                if (_socket == null)
                {
                    LogVerbose("CreateSocketFromHandle returned null");

                    // Try fallback to raw handle I/O on Windows
                    if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
                    {
                        LogVerbose("Trying raw handle fallback for Windows...");
                        if (TryInitializeRawHandle(_sessionInfo.SocketHandle))
                        {
                            return true;
                        }
                    }

                    Console.Error.WriteLine("Failed to create socket from handle");
                    return false;
                }

                LogVerbose($"Socket created. AddressFamily={_socket.AddressFamily}, SocketType={_socket.SocketType}");
                LogVerbose($"Socket.Connected property: {_socket.Connected}");

                // Note: Connected may return false for valid inherited sockets
                // We try to create the stream anyway and see if I/O works
                if (!_socket.Connected)
                {
                    LogVerbose("Socket reports not connected, but trying to use it anyway...");
                    Console.Error.WriteLine("Warning: Socket reports not connected (this may be normal for inherited handles)");
                }

                LogVerbose("Creating NetworkStream...");
                _stream = new NetworkStream(_socket, ownsSocket: false);
                LogVerbose($"NetworkStream created. CanRead={_stream.CanRead}, CanWrite={_stream.CanWrite}");

                _reader = new StreamReader(_stream, Encoding.ASCII);
                _writer = new StreamWriter(_stream, Encoding.ASCII) { AutoFlush = true };

                LogVerbose("Socket initialization complete - verifying socket");

                // Verify the socket is usable without sending any data.
                // IMPORTANT: Do NOT send telnet bytes (IAC NOP etc.) here because the
                // connection may be SSH (e.g., Mystic BBS over SSH) where raw telnet
                // bytes corrupt the SSH protocol stream.
                bool testSucceeded = false;
                try
                {
                    // Use Socket.Poll to check if the socket is writable
                    // Poll with SelectWrite returns true if the socket can send data
                    if (_socket.Poll(1000000, SelectMode.SelectWrite)) // 1 second timeout
                    {
                        LogVerbose("Socket poll succeeded - socket is writable");
                        testSucceeded = true;
                    }
                    else
                    {
                        LogVerbose("Socket poll failed - socket is not writable");
                    }
                }
                catch (Exception pollEx)
                {
                    LogVerbose($"Socket poll failed: {pollEx.Message}");
                    Console.Error.WriteLine($"Socket verification failed: {pollEx.Message}");
                }

                // If socket test failed, try the raw handle fallback
                if (!testSucceeded && RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
                {
                    LogVerbose("Socket test failed, trying raw handle fallback...");
                    _stream?.Dispose();
                    _stream = null;
                    _reader = null;
                    _writer = null;
                    _socket = null;

                    if (TryInitializeRawHandle(_sessionInfo.SocketHandle))
                    {
                        return true;
                    }

                    // Both socket and raw handle failed - initialization truly failed
                    LogVerbose("Both socket and raw handle initialization failed");
                    Console.Error.WriteLine("Socket I/O failed. Try using --stdio flag for Standard I/O mode.");
                    return false;
                }

                // Send telnet negotiation only for confirmed TCP sockets (not pipes/SSH)
                // This tells the telnet client: "I will echo your keystrokes" and
                // "I support character-at-a-time mode" - essential for BBS door games
                if (_socket != null && (_socket.AddressFamily == AddressFamily.InterNetwork
                    || _socket.AddressFamily == AddressFamily.InterNetworkV6))
                {
                    SendTelnetNegotiation();
                }

                return true;
            }
            catch (Exception ex)
            {
                Console.Error.WriteLine($"Failed to initialize socket: {ex.Message}");
                LogVerbose($"Exception type: {ex.GetType().Name}");
                LogVerbose($"Stack trace: {ex.StackTrace}");

                // Try fallback on failure
                if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
                {
                    LogVerbose("Exception during socket init, trying raw handle fallback...");
                    if (TryInitializeRawHandle(_sessionInfo.SocketHandle))
                    {
                        return true;
                    }
                }

                return false;
            }
        }

        /// <summary>
        /// Diagnose a Windows handle to determine its type
        /// </summary>
        private void DiagnoseWindowsHandle(int handle)
        {
            try
            {
                var ptr = new IntPtr(handle);

                // Check if handle is valid
                if (GetHandleInformation(ptr, out uint flags))
                {
                    LogVerbose($"Handle is valid. Flags: 0x{flags:X}");
                    if ((flags & 0x01) != 0) LogVerbose("  HANDLE_FLAG_INHERIT is set");
                    if ((flags & 0x02) != 0) LogVerbose("  HANDLE_FLAG_PROTECT_FROM_CLOSE is set");
                }
                else
                {
                    int error = Marshal.GetLastWin32Error();
                    LogVerbose($"GetHandleInformation failed with error {error}");
                }

                // Get file type
                int fileType = GetFileType(ptr);
                string typeDesc = fileType switch
                {
                    FILE_TYPE_DISK => "FILE_TYPE_DISK (regular file)",
                    FILE_TYPE_CHAR => "FILE_TYPE_CHAR (character device like console)",
                    FILE_TYPE_PIPE => "FILE_TYPE_PIPE (pipe or socket)",
                    _ => $"FILE_TYPE_UNKNOWN ({fileType})"
                };
                LogVerbose($"Handle type: {typeDesc}");

                if (fileType == FILE_TYPE_PIPE)
                {
                    LogVerbose("Handle appears to be a pipe or socket - good!");
                }
                else if (fileType == FILE_TYPE_CHAR)
                {
                    LogVerbose("Handle is a character device - might be console I/O, try --stdio");
                }
                else if (fileType == FILE_TYPE_UNKNOWN)
                {
                    LogVerbose("Handle type unknown - the handle may be invalid or a different type");
                }
            }
            catch (Exception ex)
            {
                LogVerbose($"Handle diagnosis failed: {ex.Message}");
            }
        }

        /// <summary>
        /// Try to initialize I/O using a raw Windows handle via FileStream
        /// This is a fallback when Socket wrapping doesn't work
        /// </summary>
        private bool TryInitializeRawHandle(int handle)
        {
            LogVerbose($"TryInitializeRawHandle({handle})");

            try
            {
                var ptr = new IntPtr(handle);

                // First, try to duplicate the handle to ensure we have access
                IntPtr duplicatedHandle = IntPtr.Zero;
                bool duplicated = false;

                try
                {
                    duplicated = DuplicateHandle(
                        GetCurrentProcess(),
                        ptr,
                        GetCurrentProcess(),
                        out duplicatedHandle,
                        0,
                        false,
                        DUPLICATE_SAME_ACCESS);

                    if (duplicated)
                    {
                        LogVerbose($"Handle duplicated successfully: {duplicatedHandle}");
                        ptr = duplicatedHandle;
                    }
                    else
                    {
                        int error = Marshal.GetLastWin32Error();
                        LogVerbose($"DuplicateHandle failed with error {error}, using original handle");
                    }
                }
                catch (Exception dupEx)
                {
                    LogVerbose($"DuplicateHandle exception: {dupEx.Message}");
                }

                // Create a SafeFileHandle from the handle
                var safeHandle = new SafeFileHandle(ptr, ownsHandle: duplicated);
                LogVerbose($"SafeFileHandle created. IsInvalid={safeHandle.IsInvalid}");

                if (safeHandle.IsInvalid)
                {
                    LogVerbose("SafeFileHandle is invalid");
                    return false;
                }

                // Create FileStream for read/write
                // Try sync first, then async mode (some BBS software uses overlapped I/O handles)
                try
                {
                    _rawHandleStream = new FileStream(safeHandle, FileAccess.ReadWrite, bufferSize: 1, isAsync: false);
                    LogVerbose($"FileStream created (sync mode). CanRead={_rawHandleStream.CanRead}, CanWrite={_rawHandleStream.CanWrite}");
                }
                catch (Exception syncEx)
                {
                    LogVerbose($"Sync FileStream failed: {syncEx.Message}");
                    LogVerbose("Retrying with async mode (overlapped I/O handle)...");

                    // Re-duplicate handle for async attempt since the sync attempt may have invalidated it
                    if (duplicated)
                    {
                        safeHandle = new SafeFileHandle(IntPtr.Zero, false); // Release old
                        duplicated = DuplicateHandle(
                            GetCurrentProcess(),
                            new IntPtr(handle),
                            GetCurrentProcess(),
                            out duplicatedHandle,
                            0,
                            false,
                            DUPLICATE_SAME_ACCESS);
                        if (duplicated)
                        {
                            safeHandle = new SafeFileHandle(duplicatedHandle, ownsHandle: true);
                        }
                        else
                        {
                            safeHandle = new SafeFileHandle(new IntPtr(handle), ownsHandle: false);
                        }
                    }

                    _rawHandleStream = new FileStream(safeHandle, FileAccess.ReadWrite, bufferSize: 4096, isAsync: true);
                    LogVerbose($"FileStream created (async mode). CanRead={_rawHandleStream.CanRead}, CanWrite={_rawHandleStream.CanWrite}");
                }

                _usingRawHandle = true;

                // Try a test write
                try
                {
                    // Write a visible test message to verify output works
                    var testMsg = Encoding.ASCII.GetBytes("\r\n[Usurper Raw Handle Mode]\r\n");
                    _rawHandleStream.Write(testMsg, 0, testMsg.Length);
                    _rawHandleStream.Flush();
                    LogVerbose("Raw handle test write succeeded!");
                }
                catch (Exception writeEx)
                {
                    LogVerbose($"Raw handle test write failed: {writeEx.Message}");
                    _rawHandleStream?.Dispose();
                    _rawHandleStream = null;
                    _usingRawHandle = false;
                    return false;
                }

                Console.Error.WriteLine("[SOCKET] Using raw handle I/O mode");

                // NOTE: Do NOT send telnet negotiation for raw handles.
                // Raw handles may be pipes from SSH-based BBS (e.g., Mystic over SSH),
                // and telnet IAC bytes would corrupt the SSH protocol stream.

                return true;
            }
            catch (Exception ex)
            {
                LogVerbose($"TryInitializeRawHandle failed: {ex.Message}");
                LogVerbose($"Stack trace: {ex.StackTrace}");
                return false;
            }
        }

        /// <summary>
        /// Create a Socket object from an inherited handle
        /// Works on both Windows (socket handle) and Linux (file descriptor)
        /// </summary>
        private Socket? CreateSocketFromHandle(int handle)
        {
            LogVerbose($"CreateSocketFromHandle({handle})");
            LogVerbose($"Running on: {Environment.OSVersion.Platform} ({Environment.OSVersion})");
            LogVerbose($"Is 64-bit process: {Environment.Is64BitProcess}");

            try
            {
                // Create SafeSocketHandle from the inherited handle
                // On Windows: this is a SOCKET handle (UINT_PTR)
                // On Linux: this is a file descriptor (int)
                var intPtrHandle = new IntPtr(handle);
                LogVerbose($"IntPtr created: {intPtrHandle}");

                var safeHandle = new SafeSocketHandle(intPtrHandle, ownsHandle: false);
                LogVerbose($"SafeSocketHandle created. IsInvalid={safeHandle.IsInvalid}, IsClosed={safeHandle.IsClosed}");

                if (safeHandle.IsInvalid)
                {
                    Console.Error.WriteLine($"Handle {handle} is reported as invalid by SafeSocketHandle");
                    LogVerbose("The handle may not be a valid socket, or it may not have been inherited properly");
                    // Continue anyway - sometimes IsInvalid gives false positives
                }

                var socket = new Socket(safeHandle);
                LogVerbose("Socket object created from SafeSocketHandle");

                // Log socket properties
                try
                {
                    LogVerbose($"AddressFamily: {socket.AddressFamily}");
                    LogVerbose($"SocketType: {socket.SocketType}");
                    LogVerbose($"ProtocolType: {socket.ProtocolType}");
                    LogVerbose($"Blocking: {socket.Blocking}");
                    LogVerbose($"Connected: {socket.Connected}");
                    LogVerbose($"Available bytes: {socket.Available}");
                }
                catch (Exception propEx)
                {
                    LogVerbose($"Error reading socket properties: {propEx.Message}");
                }

                // On Windows, try to get local/remote endpoints for diagnostics
                try
                {
                    if (socket.LocalEndPoint != null)
                        LogVerbose($"LocalEndPoint: {socket.LocalEndPoint}");
                    if (socket.RemoteEndPoint != null)
                        LogVerbose($"RemoteEndPoint: {socket.RemoteEndPoint}");
                }
                catch (Exception epEx)
                {
                    LogVerbose($"Could not get endpoints: {epEx.Message}");
                }

                return socket;
            }
            catch (Exception ex)
            {
                Console.Error.WriteLine($"CreateSocketFromHandle failed: {ex.Message}");
                LogVerbose($"Exception type: {ex.GetType().Name}");
                LogVerbose($"Stack trace: {ex.StackTrace}");
                Console.Error.WriteLine("This may be normal on some BBSes - try using --stdio flag for Standard I/O mode");
                return null;
            }
        }

        #region Output Methods

        public async Task WriteAsync(string text)
        {
            if (_sessionInfo.CommType == ConnectionType.Local)
            {
                Console.Write(text);
                return;
            }

            // Use raw handle mode if active
            if (_usingRawHandle && _rawHandleStream != null)
            {
                try
                {
                    var bytes = ConvertToCp437(text);
                    await _rawHandleStream.WriteAsync(bytes, 0, bytes.Length);
                    await _rawHandleStream.FlushAsync();
                }
                catch (Exception ex)
                {
                    Console.Error.WriteLine($"Raw handle write error: {ex.Message}");
                }
                return;
            }

            if (_stream == null)
            {
                Console.Write(text);
                return;
            }

            try
            {
                // Convert text to CP437 bytes for BBS compatibility
                var bytes = ConvertToCp437(text);
                await _stream.WriteAsync(bytes, 0, bytes.Length);
                await _stream.FlushAsync();
            }
            catch (Exception ex)
            {
                Console.Error.WriteLine($"Socket write error: {ex.Message}");
                DoorMode.IsDisconnected = true;
            }
        }

        /// <summary>
        /// Convert a Unicode string to CP437 bytes for BBS terminal compatibility.
        /// Characters with known CP437 mappings are converted, others are passed through as ASCII.
        /// </summary>
        private byte[] ConvertToCp437(string text)
        {
            var result = new byte[text.Length];
            for (int i = 0; i < text.Length; i++)
            {
                char c = text[i];

                // Check if we have a CP437 mapping for this character
                if (UnicodeToCp437.TryGetValue(c, out byte cp437Byte))
                {
                    result[i] = cp437Byte;
                }
                else if (c <= 127)
                {
                    // Standard ASCII - pass through directly
                    result[i] = (byte)c;
                }
                else
                {
                    // Unknown character - use '?' as fallback
                    result[i] = (byte)'?';
                }
            }
            return result;
        }

        public async Task WriteLineAsync(string text = "")
        {
            await WriteAsync(text + "\r\n");
        }

        public async Task WriteAsync(string text, string color)
        {
            await SetColorAsync(color);
            await WriteAsync(text);
        }

        public async Task WriteLineAsync(string text, string color)
        {
            await SetColorAsync(color);
            await WriteLineAsync(text);
        }

        public async Task SetColorAsync(string color)
        {
            if (string.IsNullOrEmpty(color) || color == _currentColor)
                return;

            _currentColor = color.ToLowerInvariant();

            if (_sessionInfo.Emulation == TerminalEmulation.ASCII)
                return; // No colors in ASCII mode

            string ansiCode = GetAnsiColorCode(_currentColor);
            await WriteRawAsync($"{CSI}{ansiCode}m");
        }

        public async Task ClearScreenAsync()
        {
            if (_sessionInfo.Emulation >= TerminalEmulation.ANSI)
            {
                await WriteRawAsync($"{CSI}2J{CSI}H"); // Clear screen and move to home
            }
            else
            {
                // ASCII mode - send form feed or many newlines
                await WriteAsync("\f");
            }
        }

        public async Task MoveCursorAsync(int row, int col)
        {
            if (_sessionInfo.Emulation >= TerminalEmulation.ANSI)
            {
                await WriteRawAsync($"{CSI}{row};{col}H");
            }
        }

        public async Task ResetAttributesAsync()
        {
            if (_sessionInfo.Emulation >= TerminalEmulation.ANSI)
            {
                await WriteRawAsync($"{CSI}0m");
                _currentColor = "white";
            }
        }

        /// <summary>
        /// Write raw bytes/text without any CP437 processing (used for ANSI escape codes)
        /// </summary>
        private async Task WriteRawAsync(string data)
        {
            if (_sessionInfo.CommType == ConnectionType.Local)
            {
                Console.Write(data);
                return;
            }

            // Use raw handle mode if active
            if (_usingRawHandle && _rawHandleStream != null)
            {
                try
                {
                    var bytes = Encoding.ASCII.GetBytes(data);
                    await _rawHandleStream.WriteAsync(bytes, 0, bytes.Length);
                    await _rawHandleStream.FlushAsync();
                }
                catch { DoorMode.IsDisconnected = true; }
                return;
            }

            if (_stream == null)
            {
                Console.Write(data);
                return;
            }

            try
            {
                // Write raw ASCII bytes without CP437 conversion (for ANSI escape codes)
                var bytes = Encoding.ASCII.GetBytes(data);
                await _stream.WriteAsync(bytes, 0, bytes.Length);
                await _stream.FlushAsync();
            }
            catch { DoorMode.IsDisconnected = true; }
        }

        /// <summary>
        /// Write text with inline color markup [colorname]text[/]
        /// </summary>
        public async Task WriteMarkupAsync(string text)
        {
            var segments = ParseColorMarkup(text);
            foreach (var (content, color) in segments)
            {
                if (!string.IsNullOrEmpty(color))
                    await SetColorAsync(color);
                await WriteAsync(content);
            }
        }

        public async Task WriteMarkupLineAsync(string text)
        {
            await WriteMarkupAsync(text);
            await WriteLineAsync();
        }

        #endregion

        #region Input Methods

        public async Task<string> GetInputAsync(string prompt = "")
        {
            if (!string.IsNullOrEmpty(prompt))
                await WriteAsync(prompt);

            if (_sessionInfo.CommType == ConnectionType.Local)
            {
                return Console.ReadLine() ?? "";
            }

            // Use raw handle mode if active
            if (_usingRawHandle && _rawHandleStream != null)
            {
                try
                {
                    var line = await ReadLineFromRawHandleAsync();
                    return line ?? "";
                }
                catch
                {
                    DoorMode.IsDisconnected = true;
                    return "";
                }
            }

            if (_reader == null)
            {
                return Console.ReadLine() ?? "";
            }

            try
            {
                var line = await ReadLineFromSocketAsync();
                return line ?? "";
            }
            catch
            {
                DoorMode.IsDisconnected = true;
                return "";
            }
        }

        public async Task<string> GetKeyInputAsync(string prompt = "")
        {
            if (!string.IsNullOrEmpty(prompt))
                await WriteAsync(prompt);

            if (_sessionInfo.CommType == ConnectionType.Local)
            {
                var key = Console.ReadKey(true);
                return key.KeyChar.ToString();
            }

            // Use raw handle mode if active
            if (_usingRawHandle && _rawHandleStream != null)
            {
                try
                {
                    var buffer = new byte[1];
                    var bytesRead = await _rawHandleStream.ReadAsync(buffer, 0, 1);
                    if (bytesRead == 0)
                    {
                        DoorMode.IsDisconnected = true;
                        return "";
                    }
                    return Encoding.ASCII.GetString(buffer, 0, 1);
                }
                catch
                {
                    DoorMode.IsDisconnected = true;
                    return "";
                }
            }

            if (_stream == null)
            {
                var key = Console.ReadKey(true);
                return key.KeyChar.ToString();
            }

            try
            {
                // Read a single character from socket
                var buffer = new byte[1];
                var bytesRead = await _stream.ReadAsync(buffer, 0, 1);

                if (bytesRead == 0)
                {
                    DoorMode.IsDisconnected = true;
                    return "";
                }

                return Encoding.ASCII.GetString(buffer, 0, 1);
            }
            catch
            {
                DoorMode.IsDisconnected = true;
                return "";
            }
        }

        /// <summary>
        /// Read a line from the socket, handling telnet negotiation and line endings
        /// </summary>
        private async Task<string?> ReadLineFromSocketAsync()
        {
            if (_stream == null) return null;

            var buffer = new StringBuilder();
            var charBuffer = new byte[1];

            while (true)
            {
                var bytesRead = await _stream.ReadAsync(charBuffer, 0, 1);
                if (bytesRead == 0)
                {
                    DoorMode.IsDisconnected = true;
                    return buffer.Length > 0 ? buffer.ToString() : null;
                }

                byte b = charBuffer[0];

                // Handle telnet IAC commands (255 = IAC)
                if (b == 255)
                {
                    await HandleTelnetCommandAsync();
                    continue;
                }

                // CR or LF ends the line
                if (b == '\r' || b == '\n')
                {
                    // Consume any following LF after CR
                    if (b == '\r' && _stream.DataAvailable)
                    {
                        var peek = new byte[1];
                        await _stream.ReadAsync(peek, 0, 1);
                        // If it's not LF, we'd need to push it back, but for simplicity we accept it
                    }
                    return buffer.ToString();
                }

                // Backspace handling
                if (b == 8 || b == 127) // BS or DEL
                {
                    if (buffer.Length > 0)
                    {
                        buffer.Remove(buffer.Length - 1, 1);
                        // Echo backspace-space-backspace to erase character
                        await WriteRawAsync("\b \b");
                    }
                    continue;
                }

                // Ignore control characters except printable ASCII
                if (b < 32)
                    continue;

                buffer.Append((char)b);

                // Echo the character back (most telnet clients expect echo)
                await WriteRawAsync(((char)b).ToString());
            }
        }

        /// <summary>
        /// Read a line from the raw handle stream
        /// </summary>
        private async Task<string?> ReadLineFromRawHandleAsync()
        {
            if (_rawHandleStream == null) return null;

            var buffer = new StringBuilder();
            var charBuffer = new byte[1];

            while (true)
            {
                var bytesRead = await _rawHandleStream.ReadAsync(charBuffer, 0, 1);
                if (bytesRead == 0)
                {
                    DoorMode.IsDisconnected = true;
                    return buffer.Length > 0 ? buffer.ToString() : null;
                }

                byte b = charBuffer[0];

                // Handle telnet IAC commands (255 = IAC)
                if (b == 255)
                {
                    // Skip next 2 bytes (simplified telnet handling)
                    await _rawHandleStream.ReadAsync(new byte[2], 0, 2);
                    continue;
                }

                // CR or LF ends the line
                if (b == '\r' || b == '\n')
                {
                    return buffer.ToString();
                }

                // Backspace handling
                if (b == 8 || b == 127) // BS or DEL
                {
                    if (buffer.Length > 0)
                    {
                        buffer.Remove(buffer.Length - 1, 1);
                        await WriteRawAsync("\b \b");
                    }
                    continue;
                }

                // Ignore control characters except printable ASCII
                if (b < 32)
                    continue;

                buffer.Append((char)b);

                // Echo the character back
                await WriteRawAsync(((char)b).ToString());
            }
        }

        /// <summary>
        /// Handle telnet IAC (Interpret As Command) sequences
        /// </summary>
        // Telnet option constants
        private const byte TELOPT_ECHO = 1;   // Echo
        private const byte TELOPT_SGA = 3;    // Suppress Go-Ahead

        /// <summary>
        /// Send initial telnet negotiation after socket connection.
        /// Tells the client: "I will echo your keystrokes" and "I support character-at-a-time mode"
        /// </summary>
        private void SendTelnetNegotiation()
        {
            try
            {
                // IAC WILL ECHO (255, 251, 1) - Server will echo keystrokes
                // IAC WILL SGA  (255, 251, 3) - Suppress Go-Ahead (character-at-a-time mode)
                var negotiation = new byte[]
                {
                    0xFF, 0xFB, TELOPT_ECHO,  // IAC WILL ECHO
                    0xFF, 0xFB, TELOPT_SGA    // IAC WILL SGA
                };

                if (_usingRawHandle && _rawHandleStream != null)
                {
                    _rawHandleStream.Write(negotiation, 0, negotiation.Length);
                    _rawHandleStream.Flush();
                }
                else if (_stream != null)
                {
                    _stream.Write(negotiation, 0, negotiation.Length);
                    _stream.Flush();
                }

                LogVerbose("Sent telnet WILL ECHO + WILL SGA negotiation");
            }
            catch (Exception ex)
            {
                LogVerbose($"Telnet negotiation send failed: {ex.Message}");
            }
        }

        private async Task HandleTelnetCommandAsync()
        {
            if (_stream == null) return;

            var cmdBuffer = new byte[2];
            var bytesRead = await _stream.ReadAsync(cmdBuffer, 0, 2);

            if (bytesRead < 2) return;

            byte cmd = cmdBuffer[0];
            byte option = cmdBuffer[1];

            // Respond to common telnet negotiations
            // 251 = WILL, 252 = WON'T, 253 = DO, 254 = DON'T

            switch (cmd)
            {
                case 251: // WILL - client offers to do something
                    if (option == TELOPT_SGA)
                    {
                        // Accept SGA from client (character-at-a-time mode)
                        await WriteRawAsync($"\xff\xfd{(char)option}"); // IAC DO option
                    }
                    else
                    {
                        await WriteRawAsync($"\xff\xfe{(char)option}"); // IAC DON'T option
                    }
                    break;
                case 253: // DO - client asks us to do something
                    if (option == TELOPT_ECHO || option == TELOPT_SGA)
                    {
                        // Accept ECHO and SGA - we handle these
                        await WriteRawAsync($"\xff\xfb{(char)option}"); // IAC WILL option
                    }
                    else
                    {
                        await WriteRawAsync($"\xff\xfc{(char)option}"); // IAC WON'T option
                    }
                    break;
            }
        }

        #endregion

        #region Helper Methods

        private string GetAnsiColorCode(string color)
        {
            color = color.ToLowerInvariant();

            // Try exact match first (e.g., "bright_red", "dark_green")
            if (AnsiColors.TryGetValue(color, out var code))
                return code;

            // Try without underscores (e.g., "brightred" → "bright_red" won't match,
            // but "darkred" is in the dictionary directly)
            if (AnsiColors.TryGetValue(color.Replace("_", ""), out code))
                return code;

            return "37"; // Default to white
        }

        /// <summary>
        /// Parse color markup like [red]text[/] into segments
        /// </summary>
        private List<(string content, string? color)> ParseColorMarkup(string text)
        {
            var result = new List<(string content, string? color)>();
            var current = new StringBuilder();
            string? currentColor = null;
            int i = 0;

            while (i < text.Length)
            {
                if (text[i] == '[')
                {
                    // Look for closing bracket
                    int end = text.IndexOf(']', i + 1);
                    if (end > i)
                    {
                        var tag = text.Substring(i + 1, end - i - 1).ToLowerInvariant();

                        if (tag == "/" || tag == "/color")
                        {
                            // End tag - save current segment
                            if (current.Length > 0)
                            {
                                result.Add((current.ToString(), currentColor));
                                current.Clear();
                            }
                            currentColor = null;
                            i = end + 1;
                            continue;
                        }
                        else if (IsValidColor(tag))
                        {
                            // Color tag - save current segment and start new
                            if (current.Length > 0)
                            {
                                result.Add((current.ToString(), currentColor));
                                current.Clear();
                            }
                            currentColor = tag;
                            i = end + 1;
                            continue;
                        }
                    }
                }

                current.Append(text[i]);
                i++;
            }

            if (current.Length > 0)
                result.Add((current.ToString(), currentColor));

            return result;
        }

        private bool IsValidColor(string color)
        {
            return AnsiColors.ContainsKey(color) ||
                   AnsiColors.ContainsKey(color.Replace("_", ""));
        }

        #endregion

        #region IDisposable

        public void Dispose()
        {
            if (_disposed) return;

            _disposed = true;

            _writer?.Dispose();
            _reader?.Dispose();
            _stream?.Dispose();
            _rawHandleStream?.Dispose();
            // Don't dispose the socket - we don't own it (BBS does)
        }

        #endregion
    }
}
