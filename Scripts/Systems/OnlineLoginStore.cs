using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.Json;
using UsurperRemake.Server;

namespace UsurperRemake.Systems
{
    /// <summary>1.2.8: one remembered Online Play login, bound to its own server and port. There is no password field.</summary>
    internal sealed class OnlineLoginEntry
    {
        public string Server { get; set; } = "";
        public int Port { get; set; }
        public string Username { get; set; } = "";
        /// <summary>The remember-me token, "&lt;selector&gt;.&lt;verifier&gt;", or null for a username-only entry.</summary>
        public string? Token { get; set; }
        public DateTime? ExpiresUtc { get; set; }

        /// <summary>A well formed token that has not expired by its own expiry.</summary>
        public bool HasUsableToken(DateTime nowUtc) =>
            !string.IsNullOrEmpty(Token) && ExpiresUtc is { } expires && expires.ToUniversalTime() > nowUtc
            && LoginTokenProtocol.TrySplitToken(Token, out _, out _);
    }

    /// <summary>1.2.8: the file <c>online_login.json</c> in the per-user data directory.</summary>
    internal sealed class OnlineLoginFile
    {
        public int Version { get; set; } = 2;
        public bool NeverAskToSave { get; set; }
        public List<OnlineLoginEntry> Logins { get; set; } = new();
    }

    /// <summary>The 1.2.7 and older saved login beside the executable, read once for its conversion. Never written back.</summary>
    internal sealed class LegacyOnlineLogin
    {
        public string Server { get; init; } = "";
        public int Port { get; init; }
        public string Username { get; init; } = "";
        public string Password { get; init; } = "";
        public bool NeverAskToSave { get; init; }

        /// <summary>The legacy password goes only to the server and port it was saved for.</summary>
        public bool IsFor(string server, int port) =>
            OnlineLoginStore.HostKey(Server) == OnlineLoginStore.HostKey(server) && Port == port;
    }

    /// <summary>
    /// 1.2.8: the client's remembered logins. The client never stores a password: an entry holds the username and,
    /// after a remembered login, the server's token for that one server and port. The file lives in the per-user
    /// data directory (beside the saves), is written to a temporary file created with mode 0600 on Unix and then
    /// moved into place, and is never read or written in BBS door mode (a shared install). The old
    /// <c>online_credentials.json</c> beside the executable is converted at the next login to its own server and
    /// its password erased whatever the outcome.
    /// </summary>
    internal static class OnlineLoginStore
    {
        internal const string FileName = "online_login.json";
        internal const string LegacyFileName = "online_credentials.json";
        internal const int MaxEntries = 8;

        /// <summary>Test seam: the directory of the login file. Default is the per-user data directory.</summary>
        internal static Func<string>? DirectoryOverride;
        /// <summary>Test seam: the directory of the legacy file. Default is the executable's directory.</summary>
        internal static Func<string>? LegacyDirectoryOverride;
        /// <summary>Test seam: door mode. The store refuses to read or write while it is true.</summary>
        internal static Func<bool> DoorModeCheck = () => UsurperRemake.BBS.DoorMode.IsInDoorMode;

        private static readonly JsonSerializerOptions Indented = new() { WriteIndented = true };

        internal static string DirectoryPath => DirectoryOverride?.Invoke() ?? UserDataPaths.Get();
        internal static string FilePath => Path.Combine(DirectoryPath, FileName);
        internal static string LegacyPath => Path.Combine(LegacyDirectoryOverride?.Invoke() ?? AppContext.BaseDirectory, LegacyFileName);

        /// <summary>True in BBS door mode: nothing is read or written.</summary>
        internal static bool Refused => DoorModeCheck();

        internal static string HostKey(string? server) => (server ?? "").Trim().ToLowerInvariant();

        public static OnlineLoginFile Load()
        {
            if (Refused) return new OnlineLoginFile();
            string path = FilePath;
            try
            {
                if (!File.Exists(path)) return new OnlineLoginFile();
                var file = JsonSerializer.Deserialize<OnlineLoginFile>(File.ReadAllText(path)) ?? new OnlineLoginFile();
                file.Logins = (file.Logins ?? new List<OnlineLoginEntry>()).Where(e => e != null && !string.IsNullOrEmpty(e.Server)).ToList();
                return file;
            }
            catch (Exception ex)
            {
                // the type only: the file holds tokens, and a parser message could quote it
                DebugLogger.Instance.LogWarning("ONLINE_PLAY", $"Could not read {path} ({ex.GetType().Name})");
                return new OnlineLoginFile();
            }
        }

        /// <summary>Write the file: a new temporary file (mode 0600 on Unix), then a move over the old one.</summary>
        public static bool Save(OnlineLoginFile file)
        {
            if (Refused) return false;
            string path = FilePath;
            string tmp = path + "." + Guid.NewGuid().ToString("N") + ".tmp";
            try
            {
                Directory.CreateDirectory(Path.GetDirectoryName(path)!);
                var bytes = JsonSerializer.SerializeToUtf8Bytes(file, Indented);
                var options = new FileStreamOptions { Mode = FileMode.CreateNew, Access = FileAccess.Write, Share = FileShare.None };
                if (!OperatingSystem.IsWindows())
                    options.UnixCreateMode = UnixFileMode.UserRead | UnixFileMode.UserWrite;
                using (var stream = new FileStream(tmp, options))
                {
                    stream.Write(bytes, 0, bytes.Length);
                    stream.Flush(true);
                }
                File.Move(tmp, path, overwrite: true);
                return true;
            }
            catch (Exception ex)
            {
                try { if (File.Exists(tmp)) File.Delete(tmp); } catch { }
                DebugLogger.Instance.LogWarning("ONLINE_PLAY", $"Could not write {path} ({ex.GetType().Name})");
                return false;
            }
        }

        public static OnlineLoginEntry? Find(string server, int port) => Find(Load(), server, port);

        public static OnlineLoginEntry? Find(OnlineLoginFile file, string server, int port)
        {
            string key = HostKey(server);
            return file.Logins.FirstOrDefault(e => HostKey(e.Server) == key && e.Port == port);
        }

        /// <summary>Remember the token for this server and port (replaces whatever was there).</summary>
        public static void SetToken(string server, int port, string username, string token, DateTime expiresUtc) =>
            Upsert(server, port, e => { e.Username = username; e.Token = token; e.ExpiresUtc = expiresUtc.ToUniversalTime(); });

        /// <summary>Keep only the username for this server and port (no token).</summary>
        public static void SetUsername(string server, int port, string username) =>
            Upsert(server, port, e => { e.Username = username; e.Token = null; e.ExpiresUtc = null; });

        /// <summary>Forget the token for this server and port, keeping the username.</summary>
        public static void ClearToken(string server, int port)
        {
            if (Refused) return;
            var file = Load();
            var entry = Find(file, server, port);
            if (entry == null || entry.Token == null) return;
            entry.Token = null;
            entry.ExpiresUtc = null;
            Save(file);
        }

        public static void SetNeverAsk()
        {
            if (Refused) return;
            var file = Load();
            if (file.NeverAskToSave) return;
            file.NeverAskToSave = true;
            Save(file);
        }

        private static void Upsert(string server, int port, Action<OnlineLoginEntry> apply)
        {
            if (Refused) return;
            var file = Load();
            var entry = Find(file, server, port) ?? new OnlineLoginEntry { Server = HostKey(server), Port = port };
            file.Logins.Remove(entry);
            entry.Server = HostKey(server);
            entry.Port = port;
            apply(entry);
            file.Logins.Insert(0, entry); // most recent first; the oldest falls off past the cap
            if (file.Logins.Count > MaxEntries) file.Logins.RemoveRange(MaxEntries, file.Logins.Count - MaxEntries);
            Save(file);
        }

        // ---------------- the 1.2.7 and older file ----------------

        /// <summary>The legacy file's shape (1.2.7 and older): the password base64, defaults to the official server.</summary>
        private sealed class LegacyFile
        {
            public string Server { get; set; } = GameConfig.OnlineServerAddress;
            public int Port { get; set; } = GameConfig.OnlineServerPort;
            public string Username { get; set; } = "";
            public bool NeverAskToSave { get; set; }
            public string Password { get; set; } = "";
        }

        /// <summary>
        /// Read the legacy file, if any. Its NeverAskToSave is copied into the new file at once. A file with no
        /// username, no password or that cannot be read is erased at once (a username alone is kept in the new
        /// file). Otherwise the login comes back for the flow, which sends its password only to its own server and
        /// port and then erases the file. Null in door mode or when there is nothing left to convert.
        /// </summary>
        public static LegacyOnlineLogin? PrepareLegacy()
        {
            if (Refused) return null;
            string path = LegacyPath;
            if (!File.Exists(path)) return null;

            LegacyFile? legacy = null;
            try { legacy = JsonSerializer.Deserialize<LegacyFile>(File.ReadAllText(path)); }
            catch (Exception ex)
            {
                DebugLogger.Instance.LogWarning("ONLINE_PLAY", $"Could not read {path} ({ex.GetType().Name})");
            }
            if (legacy == null)
            {
                EraseLegacy(false);
                return null;
            }

            if (legacy.NeverAskToSave) SetNeverAsk();
            if (string.IsNullOrEmpty(legacy.Username))
            {
                EraseLegacy(legacy.NeverAskToSave);
                return null;
            }
            string password = Decode(legacy.Password);
            if (string.IsNullOrEmpty(password))
            {
                SetUsername(legacy.Server, legacy.Port, legacy.Username);
                EraseLegacy(legacy.NeverAskToSave);
                return null;
            }
            return new LegacyOnlineLogin
            {
                Server = legacy.Server ?? "",
                Port = legacy.Port,
                Username = legacy.Username,
                Password = password,
                NeverAskToSave = legacy.NeverAskToSave,
            };
        }

        private static string Decode(string? stored)
        {
            if (string.IsNullOrEmpty(stored)) return "";
            try { return Encoding.UTF8.GetString(Convert.FromBase64String(stored)); }
            catch { return stored; } // as 1.2.7 read it: not base64, so as is
        }

        /// <summary>
        /// Erase the legacy file: delete it; if that fails, overwrite it with only <c>{"NeverAskToSave":..}</c>; if
        /// that fails too, log a warning naming the path only.
        /// </summary>
        public static void EraseLegacy(bool neverAsk)
        {
            string path = LegacyPath;
            try
            {
                File.Delete(path);
                if (!File.Exists(path)) return;
            }
            catch { }
            try
            {
                File.WriteAllText(path, "{\"NeverAskToSave\":" + (neverAsk ? "true" : "false") + "}");
                return;
            }
            catch { }
            DebugLogger.Instance.LogWarning("ONLINE_PLAY", $"Could not erase the old saved login at {path}");
        }
    }
}
