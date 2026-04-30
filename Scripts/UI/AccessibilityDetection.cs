using System;
using System.Runtime.InteropServices;

namespace UsurperRemake.UI
{
    /// <summary>
    /// Detects whether an active screen reader is running on the host so the game can
    /// auto-enable screen-reader mode for blind users who don't know about the
    /// `--screen-reader` launch flag (issue #92).
    ///
    /// Windows: queries SPI_GETSCREENREADER via SystemParametersInfo. NVDA, JAWS,
    /// Narrator, and most other Windows screen readers set this system flag while
    /// running, which is the official way to detect them per Microsoft's accessibility
    /// API documentation.
    ///
    /// macOS / Linux: returns false. VoiceOver and Orca don't expose an equivalent
    /// system-wide flag the way Windows does, so users on those platforms still need
    /// the explicit `--screen-reader` flag (or in-game preferences toggle). Steam's
    /// blind audience is overwhelmingly on Windows so this is the high-value target.
    /// </summary>
    public static class AccessibilityDetection
    {
        private const uint SPI_GETSCREENREADER = 0x0046;

        [DllImport("user32.dll", SetLastError = true)]
        private static extern bool SystemParametersInfo(
            uint uiAction,
            uint uiParam,
            out bool pvParam,
            uint fWinIni);

        /// <summary>
        /// Returns true if the OS reports an active screen reader. Always false on
        /// non-Windows platforms or if the API call fails.
        /// </summary>
        public static bool IsScreenReaderActive()
        {
            if (!RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
                return false;

            try
            {
                if (SystemParametersInfo(SPI_GETSCREENREADER, 0, out bool active, 0))
                    return active;
            }
            catch
            {
                // P/Invoke can throw on unusual hosts (e.g., container without user32.dll).
                // Treat any failure as "no screen reader detected"; the explicit flag still works.
            }
            return false;
        }
    }
}
