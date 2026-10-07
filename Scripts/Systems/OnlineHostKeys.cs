using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text;

namespace UsurperRemake.Systems
{
    /// <summary>
    /// 1.2.8: the SSH host key fingerprints of the official server (sshd-usurper behind port 4000), one per key
    /// type. Whichever type the SSH client negotiates must match the pin for that type. If the server's host keys
    /// are ever regenerated, a game update with the new pins has to ship first, or every copy is refused.
    /// </summary>
    internal static class OfficialHostKeys
    {
        /// <summary>
        /// The official server as shipped. The pins apply to this address and port only, not to the Online Play
        /// server setting, which a sysop can point at their own server.
        /// </summary>
        internal const string Host = "play.usurper-reborn.net";
        internal const int Port = 4000;

        internal const string Ed25519 = "SHA256:WSDcsAljcssdVzgocPuc7BfF3ei370TOVlYsy2Q2l9s";
        internal const string Ecdsa = "SHA256:W1zNGJgPxWKyItXLSm2vQYH5tV5IT5O/QKbSXczzW28";
        internal const string Rsa = "SHA256:J3X2VcqzItS0d2CmKpHQ5/OQTWIzyW9qFPlj6wq174s";

        /// <summary>Pin by the key type named inside the key itself (an RSA key is "ssh-rsa" whatever signature is used).</summary>
        internal static readonly IReadOnlyDictionary<string, string> ByType = new Dictionary<string, string>(StringComparer.Ordinal)
        {
            ["ssh-ed25519"] = Ed25519,
            ["ecdsa-sha2-nistp256"] = Ecdsa,
            ["ssh-rsa"] = Rsa,
        };
    }

    /// <summary>The key type and the SHA256 fingerprint of an SSH public key blob, computed here from the blob.</summary>
    internal static class HostKeyFingerprint
    {
        /// <summary>"SHA256:" and the unpadded base64 of SHA-256 over the blob, as ssh-keygen -l prints it.</summary>
        internal static string Sha256(byte[] blob) =>
            "SHA256:" + Convert.ToBase64String(SHA256.HashData(blob)).TrimEnd('=');

        /// <summary>The type string at the start of the blob, reduced to safe characters; "unknown" when malformed.</summary>
        internal static string KeyType(byte[] blob)
        {
            if (blob == null || blob.Length < 4) return "unknown";
            uint length = (uint)(blob[0] << 24 | blob[1] << 16 | blob[2] << 8 | blob[3]);
            if (length == 0 || length > 64 || length > blob.Length - 4) return "unknown";
            var type = new string(Encoding.ASCII.GetString(blob, 4, (int)length)
                .Where(c => char.IsAsciiLetterOrDigit(c) || c == '-' || c == '@' || c == '.' || c == '_').ToArray());
            return type.Length == 0 ? "unknown" : type;
        }
    }

    internal enum HostKeyVerdict
    {
        /// <summary>The official server, with the pinned key for its type.</summary>
        Pinned,
        /// <summary>Another server, with the key remembered at its first connection.</summary>
        Known,
        /// <summary>Another server seen for the first time: its key is now remembered.</summary>
        FirstUse,
        /// <summary>The official address, with a key that is not the pin for its type.</summary>
        RefusedOfficial,
        /// <summary>Another server whose key differs from the one remembered.</summary>
        RefusedChanged,
    }

    /// <summary>One host key decision. Holds fingerprints only, never key material.</summary>
    internal sealed record HostKeyCheck(
        string Host, int Port, HostKeyVerdict Verdict, string KeyType, string Fingerprint,
        string? ExpectedKeyType, string? ExpectedFingerprint, string KnownHostsPath)
    {
        public bool Trusted => Verdict is HostKeyVerdict.Pinned or HostKeyVerdict.Known or HostKeyVerdict.FirstUse;

        /// <summary>The start of this server's line in the known hosts file.</summary>
        public string Entry => OnlineKnownHosts.EntryName(Host, Port);
    }

    /// <summary>Thrown by the SSH transport when the host key was refused; the connection is closed.</summary>
    internal sealed class HostKeyRefusedException : Exception
    {
        internal HostKeyCheck Check { get; }

        internal HostKeyRefusedException(HostKeyCheck check)
            : base($"Host key refused for {check.Host}:{check.Port}")
        {
            Check = check;
        }
    }

    /// <summary>
    /// 1.2.8: which SSH host keys Online Play trusts. The official server (the shipped official address and port,
    /// compared as text after case normalising, never by DNS result) only with its pinned fingerprints; any other
    /// server, a sysop's configured one included, by trust on first use, remembered per host and port, refused if
    /// the key later changes.
    /// </summary>
    internal sealed class OnlineHostKeyPolicy
    {
        internal string OfficialHost { get; }
        internal int OfficialPort { get; }
        internal IReadOnlyDictionary<string, string> Pins { get; }
        internal string KnownHostsPath { get; }

        internal OnlineHostKeyPolicy(string officialHost, int officialPort, IReadOnlyDictionary<string, string> pins, string knownHostsPath)
        {
            OfficialHost = officialHost;
            OfficialPort = officialPort;
            Pins = pins;
            KnownHostsPath = knownHostsPath;
        }

        /// <summary>The game's policy: the shipped official server, the shipped pins, the per-user known hosts file.</summary>
        internal static OnlineHostKeyPolicy ForGame() =>
            new(OfficialHostKeys.Host, OfficialHostKeys.Port, OfficialHostKeys.ByType, OnlineKnownHosts.DefaultPath);

        internal bool IsOfficial(string host, int port) =>
            port == OfficialPort && OnlineLoginStore.HostKey(host) == OnlineLoginStore.HostKey(OfficialHost);

        internal HostKeyCheck Check(string host, int port, byte[] hostKeyBlob)
        {
            string type = HostKeyFingerprint.KeyType(hostKeyBlob);
            string fingerprint = HostKeyFingerprint.Sha256(hostKeyBlob);
            string hostKey = OnlineLoginStore.HostKey(host);

            if (IsOfficial(host, port))
            {
                Pins.TryGetValue(type, out var pin);
                if (pin != null && string.Equals(pin, fingerprint, StringComparison.Ordinal))
                    return new HostKeyCheck(hostKey, port, HostKeyVerdict.Pinned, type, fingerprint, type, pin, KnownHostsPath);
                Refused(hostKey, port, type, fingerprint, pin == null ? null : type, pin);
                return new HostKeyCheck(hostKey, port, HostKeyVerdict.RefusedOfficial, type, fingerprint, pin == null ? null : type, pin, KnownHostsPath);
            }

            var known = OnlineKnownHosts.Find(KnownHostsPath, host, port);
            if (known == null)
            {
                if (OnlineKnownHosts.Add(KnownHostsPath, host, port, type, fingerprint))
                    DebugLogger.Instance.LogInfo("ONLINE_PLAY", $"Host key remembered for {hostKey}:{port}: {type} {fingerprint}");
                return new HostKeyCheck(hostKey, port, HostKeyVerdict.FirstUse, type, fingerprint, null, null, KnownHostsPath);
            }
            if (known.Value.Type == type && known.Value.Fingerprint == fingerprint)
                return new HostKeyCheck(hostKey, port, HostKeyVerdict.Known, type, fingerprint, type, fingerprint, KnownHostsPath);

            Refused(hostKey, port, type, fingerprint, known.Value.Type, known.Value.Fingerprint);
            return new HostKeyCheck(hostKey, port, HostKeyVerdict.RefusedChanged, type, fingerprint, known.Value.Type, known.Value.Fingerprint, KnownHostsPath);
        }

        private static void Refused(string host, int port, string type, string fingerprint, string? expectedType, string? expected) =>
            DebugLogger.Instance.LogWarning("ONLINE_PLAY",
                $"Host key refused for {host}:{port}: offered {type} {fingerprint}, expected {expectedType ?? "none"} {expected ?? "none"}");

        /// <summary>The rows the player sees for a refused key (fingerprints only), in the current language or the one given.</summary>
        internal static List<string> RefusalLines(HostKeyCheck check, string? lang = null)
        {
            string L(string key, params object[] args) => lang == null ? Loc.Get(key, args) : Loc.GetIn(lang, key, args);
            var lines = new List<string>();
            if (check.Verdict == HostKeyVerdict.RefusedOfficial)
            {
                lines.Add(L("online.hostkey_official_1"));
                lines.Add(L("online.hostkey_official_2"));
                lines.Add(L("online.hostkey_official_3"));
                lines.Add(L("online.hostkey_offered"));
                lines.Add($"    {check.KeyType} {check.Fingerprint}");
            }
            else
            {
                lines.Add(L("online.hostkey_changed_1"));
                lines.Add(L("online.hostkey_changed_2"));
                lines.Add(L("online.hostkey_remembered"));
                lines.Add($"    {check.ExpectedKeyType} {check.ExpectedFingerprint}");
                lines.Add(L("online.hostkey_offered"));
                lines.Add($"    {check.KeyType} {check.Fingerprint}");
                lines.Add(L("online.hostkey_changed_3"));
                lines.Add($"    {check.KnownHostsPath}");
                lines.Add(L("online.hostkey_changed_4"));
                lines.Add($"    {check.Entry}");
            }
            return lines;
        }
    }

    /// <summary>
    /// 1.2.8: the file <c>online_known_hosts.txt</c> beside <c>online_login.json</c> in the per-user data directory.
    /// One line per server: <c>[host]:port type SHA256:fingerprint</c>. Written to a new temporary file (mode 0600
    /// on Unix) and moved into place. Deleting a server's line makes the next connection a first connection.
    /// </summary>
    internal static class OnlineKnownHosts
    {
        internal const string FileName = "online_known_hosts.txt";

        internal static string DefaultPath => Path.Combine(OnlineLoginStore.DirectoryPath, FileName);

        internal static string EntryName(string host, int port) => $"[{OnlineLoginStore.HostKey(host)}]:{port}";

        internal static (string Type, string Fingerprint)? Find(string path, string host, int port)
        {
            string name = EntryName(host, port);
            foreach (var line in ReadLines(path) ?? new List<string>())
            {
                var parts = line.Split(' ', StringSplitOptions.RemoveEmptyEntries);
                if (parts.Length == 3 && parts[0] == name) return (parts[1], parts[2]);
            }
            return null;
        }

        /// <summary>Remember the key of a server seen for the first time. False when the file could not be written.</summary>
        internal static bool Add(string path, string host, int port, string type, string fingerprint)
        {
            string name = EntryName(host, port);
            var existing = ReadLines(path);
            if (existing == null) return false; // unreadable: never written over
            var lines = existing.Where(l => l.Split(' ', StringSplitOptions.RemoveEmptyEntries).FirstOrDefault() != name).ToList();
            lines.Add($"{name} {type} {fingerprint}");
            string tmp = path + "." + Guid.NewGuid().ToString("N") + ".tmp";
            try
            {
                Directory.CreateDirectory(Path.GetDirectoryName(path)!);
                var bytes = Encoding.UTF8.GetBytes(string.Join("\n", lines) + "\n");
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

        /// <summary>The non-empty lines; null when the file exists but cannot be read.</summary>
        private static List<string>? ReadLines(string path)
        {
            try
            {
                if (!File.Exists(path)) return new List<string>();
                return File.ReadAllLines(path).Select(l => l.Trim()).Where(l => l.Length > 0).ToList();
            }
            catch (Exception ex)
            {
                DebugLogger.Instance.LogWarning("ONLINE_PLAY", $"Could not read {path} ({ex.GetType().Name})");
                return null;
            }
        }
    }
}
