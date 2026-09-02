using System;
using System.Runtime.InteropServices;

namespace UsurperRemake.Server;

/// <summary>
/// v1.0.6: puts the relay's controlling terminal into raw, no-echo mode so that
/// keystrokes reach the MUD server one at a time and the server can own the input
/// line (echo, and erase-and-redraw when a message lands mid-typing).
///
/// Why P/Invoke rather than spawning stty: on Unix the .NET runtime restores its
/// saved termios around every child process it starts, so a child's change is
/// undone the moment it exits. Linux only; other platforms return null and the
/// relay keeps its line-mode behaviour.
///
/// struct termios (glibc, x86_64 and aarch64): c_iflag@0 c_oflag@4 c_cflag@8
/// c_lflag@12 (uint32 each), c_line@16, c_cc[32]@17, c_ispeed@52, c_ospeed@56.
/// </summary>
internal static class TerminalRawMode
{
    private const int STDIN_FILENO = 0;
    private const int TCSANOW = 0;
    private const uint ICANON = 0x0002;
    private const uint ECHO = 0x0008;
    private const int LFLAG_OFFSET = 12;
    private const int CC_OFFSET = 17;
    private const int VTIME = 5;
    private const int VMIN = 6;
    private const int TermiosBufferSize = 128; // real size is 60; over-allocate defensively

    [DllImport("libc", SetLastError = true)] private static extern int tcgetattr(int fd, byte[] termios);
    [DllImport("libc", SetLastError = true)] private static extern int tcsetattr(int fd, int optionalActions, byte[] termios);
    [DllImport("libc")] private static extern int isatty(int fd);

    public static bool IsSupported => OperatingSystem.IsLinux();

    /// <summary>Enter raw mode on stdin. Returns a restorer, or null if stdin is not a terminal or the call failed.</summary>
    public static IDisposable? TryEnter()
    {
        if (!IsSupported) return null;
        try
        {
            if (isatty(STDIN_FILENO) != 1) return null;
            var original = new byte[TermiosBufferSize];
            if (tcgetattr(STDIN_FILENO, original) != 0) return null;

            var raw = (byte[])original.Clone();
            uint lflag = BitConverter.ToUInt32(raw, LFLAG_OFFSET);
            lflag &= ~(ICANON | ECHO);
            BitConverter.GetBytes(lflag).CopyTo(raw, LFLAG_OFFSET);
            raw[CC_OFFSET + VMIN] = 1;
            raw[CC_OFFSET + VTIME] = 0;
            if (tcsetattr(STDIN_FILENO, TCSANOW, raw) != 0) return null;
            return new Restorer(original);
        }
        catch
        {
            return null; // libc missing or layout mismatch: fall back to line mode
        }
    }

    private sealed class Restorer : IDisposable
    {
        private byte[]? _original;
        private readonly EventHandler _onExit;

        public Restorer(byte[] original)
        {
            _original = original;
            _onExit = (_, _) => Dispose();
            AppDomain.CurrentDomain.ProcessExit += _onExit;
        }

        public void Dispose()
        {
            var orig = _original;
            if (orig == null) return;
            _original = null;
            AppDomain.CurrentDomain.ProcessExit -= _onExit;
            try { tcsetattr(STDIN_FILENO, TCSANOW, orig); } catch { }
        }
    }
}
