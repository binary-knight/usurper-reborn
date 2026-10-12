using System;
using System.IO;

namespace UsurperRemake.Systems
{
    /// <summary>
    /// 1.2.8: the per-user data directory, shared by the local saves and the Online Play login file. Windows
    /// %APPDATA%\UsurperReloaded, otherwise $HOME/.local/share/UsurperReloaded (.NET reports macOS as Unix, so
    /// macOS lands there too; the Library branch is kept as it was so no save moves).
    /// </summary>
    public static class UserDataPaths
    {
        private const string AppName = "UsurperReloaded";

        public static string Get()
        {
            if (Environment.OSVersion.Platform == PlatformID.Win32NT)
            {
                return Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), AppName);
            }
            else if (Environment.OSVersion.Platform == PlatformID.Unix)
            {
                var home = Environment.GetEnvironmentVariable("HOME");
                return Path.Combine(home ?? "/tmp", ".local", "share", AppName);
            }
            else
            {
                var home = Environment.GetEnvironmentVariable("HOME");
                return Path.Combine(home ?? "/tmp", "Library", "Application Support", AppName);
            }
        }
    }
}
