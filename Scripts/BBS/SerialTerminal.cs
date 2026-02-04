using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Ports;
using System.Text;
using System.Threading.Tasks;

namespace UsurperRemake.BBS
{
    /// <summary>
    /// Terminal I/O implementation that reads/writes to a serial (COM) port
    /// Used when running as a BBS door game with FOSSIL driver
    /// </summary>
    public class SerialTerminal : IDisposable
    {
        private SerialPort? _serialPort;
        private readonly BBSSessionInfo _sessionInfo;
        private bool _disposed = false;
        private string _currentColor = "white";

        // ANSI escape codes
        private const string ESC = "\x1b";
        private const string CSI = "\x1b[";

        // ANSI color codes (same as SocketTerminal)
        private static readonly Dictionary<string, string> AnsiColors = new()
        {
            { "black", "30" },
            { "red", "31" },
            { "green", "32" },
            { "yellow", "33" },
            { "blue", "34" },
            { "magenta", "35" },
            { "cyan", "36" },
            { "white", "37" },
            { "gray", "90" },
            { "grey", "90" },
            { "darkred", "31" },
            { "dark_red", "31" },
            { "darkgreen", "32" },
            { "dark_green", "32" },
            { "darkyellow", "33" },
            { "dark_yellow", "33" },
            { "brown", "33" },
            { "darkblue", "34" },
            { "dark_blue", "34" },
            { "darkmagenta", "35" },
            { "dark_magenta", "35" },
            { "darkcyan", "36" },
            { "dark_cyan", "36" },
            { "darkgray", "90" },
            { "dark_gray", "90" },
            { "darkgrey", "90" },
            { "bright_black", "90" },
            { "bright_red", "91" },
            { "bright_green", "92" },
            { "bright_yellow", "93" },
            { "bright_blue", "94" },
            { "bright_magenta", "95" },
            { "bright_cyan", "96" },
            { "bright_white", "97" }
        };

        // Unicode to CP437 character mapping for BBS compatibility
        private static readonly Dictionary<char, byte> UnicodeToCp437 = new()
        {
            // Box drawing - single line
            { '─', 196 }, { '│', 179 }, { '┌', 218 }, { '┐', 191 },
            { '└', 192 }, { '┘', 217 }, { '├', 195 }, { '┤', 180 },
            { '┬', 194 }, { '┴', 193 }, { '┼', 197 },
            // Box drawing - double line
            { '═', 205 }, { '║', 186 }, { '╔', 201 }, { '╗', 187 },
            { '╚', 200 }, { '╝', 188 }, { '╠', 204 }, { '╣', 185 },
            { '╦', 203 }, { '╩', 202 }, { '╬', 206 },
            // Box drawing - mixed
            { '╒', 213 }, { '╓', 214 }, { '╘', 212 }, { '╙', 211 },
            { '╞', 198 }, { '╟', 199 }, { '╤', 209 }, { '╥', 210 },
            { '╧', 207 }, { '╨', 208 }, { '╪', 216 }, { '╫', 215 },
            // Shade blocks
            { '░', 176 }, { '▒', 177 }, { '▓', 178 }, { '█', 219 },
            { '▄', 220 }, { '▀', 223 }, { '▌', 221 }, { '▐', 222 },
            // Symbols
            { '♠', 6 }, { '♣', 5 }, { '♥', 3 }, { '♦', 4 },
            { '•', 7 }, { '◘', 8 }, { '○', 9 }, { '◙', 10 },
            { '♂', 11 }, { '♀', 12 }, { '♪', 13 }, { '♫', 14 },
            { '☼', 15 }, { '►', 16 }, { '◄', 17 }, { '↕', 18 },
            { '‼', 19 }, { '¶', 20 }, { '§', 21 }, { '▬', 22 },
            { '↨', 23 }, { '↑', 24 }, { '↓', 25 }, { '→', 26 },
            { '←', 27 }, { '∟', 28 }, { '↔', 29 }, { '▲', 30 },
            { '▼', 31 },
            // Other common characters
            { '†', 197 }, { '✗', 158 }, { '♔', 2 }, { '✝', 197 },
            { '⚔', 197 }, { '⚑', 16 }, { '⛓', 45 }
        };

        public BBSSessionInfo SessionInfo => _sessionInfo;

        /// <summary>
        /// Verbose logging for debugging (set by DoorMode)
        /// </summary>
        public static bool VerboseLogging { get; set; } = false;

        private static void LogVerbose(string message)
        {
            if (VerboseLogging)
                Console.Error.WriteLine($"[SERIAL] {message}");
        }

        public SerialTerminal(BBSSessionInfo sessionInfo)
        {
            _sessionInfo = sessionInfo;
        }

        /// <summary>
        /// Initialize the serial port connection
        /// </summary>
        public bool Initialize()
        {
            LogVerbose("Initialize() called");
            LogVerbose($"Requested COM port: {_sessionInfo.ComPort}");
            LogVerbose($"Requested baud rate: {_sessionInfo.BaudRate}");

            // List available COM ports for debugging
            try
            {
                var availablePorts = SerialPort.GetPortNames();
                LogVerbose($"Available COM ports on system: {(availablePorts.Length > 0 ? string.Join(", ", availablePorts) : "(none found)")}");
            }
            catch (Exception ex)
            {
                LogVerbose($"Could not enumerate COM ports: {ex.Message}");
            }

            try
            {
                if (string.IsNullOrEmpty(_sessionInfo.ComPort))
                {
                    Console.Error.WriteLine("No COM port specified in session info");
                    LogVerbose("ERROR: ComPort is null or empty");
                    if (VerboseLogging)
                    {
                        Console.Error.WriteLine("[SERIAL] Press Enter to continue...");
                        Console.ReadLine();
                    }
                    return false;
                }

                Console.Error.WriteLine($"Opening serial port: {_sessionInfo.ComPort} at {_sessionInfo.BaudRate} baud");
                LogVerbose($"Creating SerialPort object...");

                int baudRate = _sessionInfo.BaudRate > 0 ? _sessionInfo.BaudRate : 115200;
                LogVerbose($"Using baud rate: {baudRate}");

                _serialPort = new SerialPort(
                    _sessionInfo.ComPort,
                    baudRate,
                    Parity.None,
                    8,
                    StopBits.One
                );

                LogVerbose("SerialPort object created");
                LogVerbose($"Setting timeouts: Read=30000ms, Write=5000ms");

                _serialPort.ReadTimeout = 30000; // 30 second timeout
                _serialPort.WriteTimeout = 5000;
                _serialPort.Encoding = Encoding.GetEncoding(437); // CP437 for BBS

                LogVerbose($"Attempting to open {_sessionInfo.ComPort}...");
                _serialPort.Open();

                LogVerbose($"Serial port opened successfully");
                LogVerbose($"IsOpen: {_serialPort.IsOpen}");
                LogVerbose($"BytesToRead: {_serialPort.BytesToRead}");
                LogVerbose($"BytesToWrite: {_serialPort.BytesToWrite}");

                Console.Error.WriteLine($"Serial port {_sessionInfo.ComPort} opened successfully");

                // Pause in verbose mode
                if (VerboseLogging)
                {
                    Console.Error.WriteLine("[SERIAL] Serial initialization complete. Press Enter to continue...");
                    Console.ReadLine();
                }

                return true;
            }
            catch (UnauthorizedAccessException ex)
            {
                Console.Error.WriteLine($"Failed to open serial port: Access denied - {ex.Message}");
                LogVerbose($"UnauthorizedAccessException: {ex.Message}");
                LogVerbose("This usually means the COM port is in use by another application");
                LogVerbose("Or the port exists but you don't have permission to access it");
                if (VerboseLogging)
                {
                    Console.Error.WriteLine("[SERIAL] Press Enter to continue...");
                    Console.ReadLine();
                }
                return false;
            }
            catch (System.IO.IOException ex)
            {
                Console.Error.WriteLine($"Failed to open serial port: I/O error - {ex.Message}");
                LogVerbose($"IOException: {ex.Message}");
                LogVerbose("This usually means the COM port does not exist or is not accessible");
                if (VerboseLogging)
                {
                    Console.Error.WriteLine("[SERIAL] Press Enter to continue...");
                    Console.ReadLine();
                }
                return false;
            }
            catch (ArgumentException ex)
            {
                Console.Error.WriteLine($"Failed to open serial port: Invalid argument - {ex.Message}");
                LogVerbose($"ArgumentException: {ex.Message}");
                LogVerbose("This usually means the COM port name or settings are invalid");
                if (VerboseLogging)
                {
                    Console.Error.WriteLine("[SERIAL] Press Enter to continue...");
                    Console.ReadLine();
                }
                return false;
            }
            catch (Exception ex)
            {
                Console.Error.WriteLine($"Failed to open serial port: {ex.Message}");
                LogVerbose($"Exception type: {ex.GetType().Name}");
                LogVerbose($"Message: {ex.Message}");
                LogVerbose($"Stack trace: {ex.StackTrace}");
                if (VerboseLogging)
                {
                    Console.Error.WriteLine("[SERIAL] Press Enter to continue...");
                    Console.ReadLine();
                }
                return false;
            }
        }

        /// <summary>
        /// Set the current text color using ANSI codes
        /// </summary>
        public void SetColor(string colorName)
        {
            if (_serialPort == null || !_serialPort.IsOpen) return;

            _currentColor = colorName.ToLowerInvariant();

            if (AnsiColors.TryGetValue(_currentColor, out var ansiCode))
            {
                bool isBright = _currentColor.StartsWith("bright_") || _currentColor == "white";
                string attr = isBright ? "1" : "0";
                WriteRaw($"{CSI}{attr};{ansiCode}m");
            }
        }

        /// <summary>
        /// Write text without newline
        /// </summary>
        public void Write(string text)
        {
            if (_serialPort == null || !_serialPort.IsOpen) return;

            var bytes = ConvertToCP437(text);
            _serialPort.Write(bytes, 0, bytes.Length);
        }

        /// <summary>
        /// Write text with newline
        /// </summary>
        public void WriteLine(string text = "")
        {
            Write(text + "\r\n");
        }

        /// <summary>
        /// Write raw bytes/string without conversion
        /// </summary>
        public void WriteRaw(string text)
        {
            if (_serialPort == null || !_serialPort.IsOpen) return;

            var bytes = Encoding.ASCII.GetBytes(text);
            _serialPort.Write(bytes, 0, bytes.Length);
        }

        /// <summary>
        /// Clear the screen
        /// </summary>
        public void ClearScreen()
        {
            WriteRaw($"{CSI}2J{CSI}1;1H");
        }

        /// <summary>
        /// Read a line of input
        /// </summary>
        public async Task<string> ReadLineAsync()
        {
            if (_serialPort == null || !_serialPort.IsOpen)
                return await Task.FromResult(Console.ReadLine() ?? "");

            return await Task.Run(() =>
            {
                var sb = new StringBuilder();

                while (true)
                {
                    try
                    {
                        int b = _serialPort.ReadByte();
                        if (b == -1) break;

                        char c = (char)b;

                        // Handle enter (CR or LF)
                        if (c == '\r' || c == '\n')
                        {
                            WriteLine(); // Echo newline
                            break;
                        }

                        // Handle backspace
                        if (c == '\b' || c == 127)
                        {
                            if (sb.Length > 0)
                            {
                                sb.Length--;
                                WriteRaw("\b \b"); // Erase character
                            }
                            continue;
                        }

                        // Handle escape (cancel input)
                        if (c == 27)
                        {
                            sb.Clear();
                            continue;
                        }

                        // Normal character
                        if (c >= 32 && c < 127)
                        {
                            sb.Append(c);
                            Write(c.ToString()); // Echo character
                        }
                    }
                    catch (TimeoutException)
                    {
                        break;
                    }
                }

                return sb.ToString();
            });
        }

        /// <summary>
        /// Read a single key
        /// </summary>
        public async Task<char> ReadKeyAsync()
        {
            if (_serialPort == null || !_serialPort.IsOpen)
            {
                var key = await Task.Run(() => Console.ReadKey(true));
                return key.KeyChar;
            }

            return await Task.Run(() =>
            {
                try
                {
                    int b = _serialPort.ReadByte();
                    return b >= 0 ? (char)b : '\0';
                }
                catch (TimeoutException)
                {
                    return '\0';
                }
            });
        }

        /// <summary>
        /// Check if data is available to read
        /// </summary>
        public bool DataAvailable()
        {
            return _serialPort?.BytesToRead > 0;
        }

        /// <summary>
        /// Convert Unicode string to CP437 bytes
        /// </summary>
        private byte[] ConvertToCP437(string text)
        {
            var result = new List<byte>();

            foreach (char c in text)
            {
                if (UnicodeToCp437.TryGetValue(c, out byte cp437Byte))
                {
                    result.Add(cp437Byte);
                }
                else if (c < 128)
                {
                    result.Add((byte)c);
                }
                else
                {
                    // Try to convert via encoding, fallback to ?
                    try
                    {
                        var bytes = Encoding.GetEncoding(437).GetBytes(new[] { c });
                        result.AddRange(bytes);
                    }
                    catch
                    {
                        result.Add((byte)'?');
                    }
                }
            }

            return result.ToArray();
        }

        public void Dispose()
        {
            if (!_disposed)
            {
                _serialPort?.Close();
                _serialPort?.Dispose();
                _disposed = true;
            }
        }
    }
}
