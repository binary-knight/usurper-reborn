using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.RegularExpressions;
using System.Threading;
using UsurperRemake.BBS;

namespace UsurperRemake.Systems
{
    /// <summary>
    /// 1.2.7: the process side of opt-in telemetry: the operator switch, the source, the consent key and
    /// the store of the active save directory. No network code lives here (the uploader is separate).
    /// </summary>
    public static class TelemetryConsent
    {
        /// <summary>The operator switch (sysop_config on a BBS, the server setting on a self hosted
        /// server) when nothing has set the resolver: off.</summary>
        internal const bool OperatorDefault = false;

        /// <summary>The one resolver of the operator switch. Null means <see cref="OperatorDefault"/>.
        /// The settings code sets it; tests set it directly.</summary>
        internal static Func<bool>? OperatorResolver { get; set; }

        /// <summary>True when the operator of a shared install (BBS, server) allows telemetry.</summary>
        internal static bool OperatorAllows()
        {
            try { return OperatorResolver?.Invoke() ?? OperatorDefault; }
            catch { return false; }
        }

        /// <summary>The source sent with a batch: BBS door 3, else self hosted server 4, else Steam 2,
        /// else single 1. BBS doors set the online flag too, so the door check comes first.</summary>
        internal static TelemetrySource CurrentSource()
        {
            if (DoorMode.IsInDoorMode) return TelemetrySource.BbsDoor;
            if (DoorMode.IsOnlineMode) return TelemetrySource.Server;
            if (VersionChecker.Instance.IsSteamBuild) return TelemetrySource.Steam;
            return TelemetrySource.Single;
        }

        /// <summary>The source as far as consent is concerned (Steam and single keep their answer the same
        /// way), without the Steam file checks, so it is cheap on the combat thread.</summary>
        internal static TelemetrySource ConsentSource()
        {
            if (DoorMode.IsInDoorMode) return TelemetrySource.BbsDoor;
            if (DoorMode.IsOnlineMode) return TelemetrySource.Server;
            return TelemetrySource.Single;
        }

        internal static bool IsShared(TelemetrySource s) => s == TelemetrySource.BbsDoor || s == TelemetrySource.Server;

        /// <summary>
        /// The consent key of a login name: the account username (an alt slot's key is its account's),
        /// lower cased, as the hex of its UTF-8 bytes. Injective, unlike the save file sanitiser, and
        /// free of path characters. Null for an empty name.
        /// </summary>
        internal static string? PlayerKey(string? loginName)
        {
            if (string.IsNullOrEmpty(loginName)) return null;
            string account = SqlSaveBackend.GetAccountUsername(loginName).ToLowerInvariant();
            if (account.Length == 0) return null;
            return Convert.ToHexString(Encoding.UTF8.GetBytes(account)).ToLowerInvariant();
        }

        /// <summary>The login name of the session this code runs for: the MUD session's account, else the
        /// door or online player name. Read on the fight's own thread, never a process wide answer.</summary>
        internal static string? CurrentLoginName()
        {
            var ctx = UsurperRemake.Server.SessionContext.Current;
            if (ctx != null && !string.IsNullOrEmpty(ctx.Username)) return ctx.Username;
            return DoorMode.GetPlayerName();
        }

        private static readonly ConcurrentDictionary<string, TelemetryStore> _stores = new(StringComparer.Ordinal);

        /// <summary>A save directory as a full path. An empty one (a database given by a bare file name, see
        /// SqlSaveBackend.GetSaveDirectory) is the current directory, as sysop_config.json resolves it.</summary>
        internal static string FullSaveDirectory(string? saveDirectory) =>
            Path.GetFullPath(string.IsNullOrEmpty(saveDirectory) ? "." : saveDirectory);

        /// <summary>The store of one save directory (one per directory in a process, so the answers it
        /// caches are shared by every session of that directory).</summary>
        internal static TelemetryStore StoreFor(string saveDirectory) =>
            _stores.GetOrAdd(FullSaveDirectory(saveDirectory), d => new TelemetryStore(d));

        /// <summary>The store of the active save directory, or null when there is none.</summary>
        internal static TelemetryStore? CurrentStore()
        {
            try
            {
                var save = SaveSystem.Instance;
                return save == null ? null : StoreFor(save.GetSaveDirectory());
            }
            catch { return null; }
        }

        /// <summary>At login on a BBS or a server: read this player's answer once and cache it, so a fight
        /// reads no file or table. Nothing is read or created while the operator switch is off.</summary>
        public static void OnLogin(string? loginName)
        {
            try
            {
                if (!IsShared(ConsentSource())) return;
                CurrentStore()?.LoadPlayerAnswer(loginName);
            }
            catch (Exception ex) { DebugLogger.Instance.LogWarning("TELEMETRY", $"consent not read at login: {ex.Message}"); }
        }

        /// <summary>A character was deleted: remove its answer (the BBS file, the server row) and its cached
        /// value, so the same name reads as not asked. Called from the save backends' delete methods.</summary>
        internal static void RemoveAnswer(string? saveDirectory, string? playerName, SqlSaveBackend? database)
        {
            string? key = PlayerKey(playerName);
            if (key == null) return;
            try
            {
                string dir = FullSaveDirectory(saveDirectory);
                if (_stores.TryGetValue(dir, out var store)) store.ForgetCached(key);
                string file = TelemetryStore.PlayerFilePath(dir, key);
                if (File.Exists(file)) File.Delete(file);
            }
            catch (Exception ex) { DebugLogger.Instance.LogWarning("TELEMETRY", $"consent answer file not removed: {ex.Message}"); }
            try { database?.DeleteTelemetryAnswer(key); }
            catch (Exception ex) { DebugLogger.Instance.LogWarning("TELEMETRY", $"consent answer row not removed: {ex.Message}"); }
        }

        /// <summary>Tests: forget every store and the operator resolver.</summary>
        internal static void ResetForTests()
        {
            _stores.Clear();
            OperatorResolver = null;
        }
    }

    /// <summary>1.2.7: telemetry/state.json. Asked and Yes are 0 or 1 on disk.</summary>
    internal sealed record TelemetryState(bool Asked, bool Yes, string? InstallId, string? StoppedVersion, long LastUpload)
    {
        internal static readonly TelemetryState NotAsked = new(false, false, null, null, 0);
    }

    /// <summary>
    /// 1.2.7: the telemetry folder of one save directory: the consent state, the per player answers of a
    /// shared install, and the local row queue. Every file lives under telemetry/, never in the save
    /// folder itself. On a BBS or server nothing is created while the operator switch is off.
    /// </summary>
    public class TelemetryStore
    {
        internal const int MaxRows = 2000;
        internal const int MaxAgeDays = 30;
        internal const string FolderName = "telemetry";

        private static readonly Regex InstallIdPattern = new("^[0-9a-f]{32}$", RegexOptions.Compiled);

        private readonly string _saveDirectory;
        private readonly Func<TelemetrySource> _source;
        private readonly Func<SqlSaveBackend?> _database;
        private readonly ConcurrentDictionary<string, bool> _answers = new(StringComparer.Ordinal);
        private readonly object _stateGate = new();
        private TelemetryState? _state;

        internal TelemetryStore(string saveDirectory, Func<TelemetrySource>? source = null,
            Func<SqlSaveBackend?>? database = null, Func<DateTime>? clock = null)
        {
            _saveDirectory = saveDirectory;
            _source = source ?? TelemetryConsent.ConsentSource;
            _database = database ?? (() => SaveSystem.Instance?.Backend as SqlSaveBackend);
            Clock = clock ?? (() => DateTime.Now);
        }

        internal string Folder => Path.Combine(_saveDirectory, FolderName);
        internal string StatePath => Path.Combine(Folder, "state.json");
        internal string QueuePath => Path.Combine(Folder, "queue.jsonl");
        internal string LockPath => Path.Combine(Folder, "queue.lock");
        internal static string PlayerFilePath(string saveDirectory, string key) =>
            Path.Combine(saveDirectory, FolderName, "players", key + ".json");

        /// <summary>Local time, for the queue day. Tests inject a clock.</summary>
        internal Func<DateTime> Clock { get; set; }

        /// <summary>How long an append waits for the queue lock (another node, or an upload) before it
        /// gives up and the row is lost.</summary>
        internal TimeSpan LockTimeout { get; set; } = TimeSpan.FromSeconds(10);

        /// <summary>Reads of state.json since this store was made.</summary>
        internal int StateReads { get; private set; }
        /// <summary>Reads of a player answer (file or table) since this store was made.</summary>
        internal int AnswerReads { get; private set; }
        /// <summary>Appends that failed.</summary>
        internal int AppendFailures { get; private set; }
        /// <summary>Log lines written for failed appends.</summary>
        internal int AppendErrorLogs { get; private set; }

        private TelemetrySource Source => _source();
        private bool Shared => TelemetryConsent.IsShared(Source);
        /// <summary>A file or table may be written: always on single and Steam, on a shared install only
        /// with the operator switch on.</summary>
        private bool WritesAllowed => !Shared || TelemetryConsent.OperatorAllows();

        internal static long DayNumber(DateTime local) => (long)(local.Date - new DateTime(1970, 1, 1)).TotalDays;

        // ---------- the queue lock ----------

        /// <summary>The OS file lock telemetry/queue.lock (FileShare.None), shared by every process that
        /// uses this folder. Waits up to <see cref="LockTimeout"/>.</summary>
        private FileStream AcquireLock()
        {
            Directory.CreateDirectory(Folder);
            var sw = Stopwatch.StartNew();
            while (true)
            {
                try { return new FileStream(LockPath, FileMode.OpenOrCreate, FileAccess.ReadWrite, FileShare.None); }
                catch (IOException) when (sw.Elapsed < LockTimeout) { Thread.Sleep(20); }
            }
        }

        /// <summary>Runs <paramref name="action"/> while holding the queue lock.</summary>
        internal void WithQueueLock(Action action)
        {
            using var held = AcquireLock();
            action();
        }

        private static void WriteAtomic(string path, string text)
        {
            Directory.CreateDirectory(Path.GetDirectoryName(path)!);
            string tmp = path + ".tmp";
            File.WriteAllText(tmp, text);
            File.Move(tmp, path, overwrite: true);
        }

        // ---------- state.json ----------

        /// <summary>The install's state, read once and cached. A missing or damaged file reads as not asked.</summary>
        internal TelemetryState State
        {
            get
            {
                lock (_stateGate)
                {
                    return _state ??= ReadStateFile();
                }
            }
        }

        private TelemetryState ReadStateFile()
        {
            try
            {
                if (!File.Exists(StatePath)) return TelemetryState.NotAsked;
                StateReads++;
                using var doc = JsonDocument.Parse(File.ReadAllText(StatePath));
                var e = doc.RootElement;
                if (e.ValueKind != JsonValueKind.Object) return TelemetryState.NotAsked;
                if (!TryFlag(e, "asked", out bool asked) || !TryFlag(e, "yes", out bool yes)) return TelemetryState.NotAsked;
                string? id = e.TryGetProperty("install_id", out var idEl) && idEl.ValueKind == JsonValueKind.String
                    && InstallIdPattern.IsMatch(idEl.GetString()!) ? idEl.GetString() : null;
                string? stopped = e.TryGetProperty("stopped_version", out var sv) && sv.ValueKind == JsonValueKind.String ? sv.GetString() : null;
                long last = e.TryGetProperty("last_upload", out var lu) && lu.ValueKind == JsonValueKind.Number && lu.TryGetInt64(out long l) && l >= 0 ? l : 0;
                return new TelemetryState(asked, asked && yes, id, stopped, last);
            }
            catch (Exception ex)
            {
                DebugLogger.Instance.LogWarning("TELEMETRY", $"state.json unreadable, read as not asked: {ex.Message}");
                return TelemetryState.NotAsked;
            }
        }

        private static bool TryFlag(JsonElement e, string name, out bool value)
        {
            value = false;
            if (!e.TryGetProperty(name, out var p) || p.ValueKind != JsonValueKind.Number || !p.TryGetInt32(out int v)) return false;
            if (v != 0 && v != 1) return false;
            value = v == 1;
            return true;
        }

        private void WriteStateLocked(TelemetryState s)
        {
            var o = new JsonObject
            {
                ["asked"] = s.Asked ? 1 : 0,
                ["yes"] = s.Yes ? 1 : 0,
            };
            if (s.InstallId != null) o["install_id"] = s.InstallId;
            if (s.StoppedVersion != null) o["stopped_version"] = s.StoppedVersion;
            o["last_upload"] = s.LastUpload;
            WriteAtomic(StatePath, o.ToJsonString());
            lock (_stateGate) _state = s;
        }

        /// <summary>Read, change and write state.json under the queue lock (another node may write it too).</summary>
        private TelemetryState UpdateState(Func<TelemetryState, TelemetryState> change, bool deleteQueue)
        {
            TelemetryState result = TelemetryState.NotAsked;
            WithQueueLock(() =>
            {
                lock (_stateGate) _state = null;
                var s = change(State);
                WriteStateLocked(s);
                if (deleteQueue && File.Exists(QueuePath)) File.Delete(QueuePath);
                result = s;
            });
            return result;
        }

        private static string NewId()
        {
            Span<byte> bytes = stackalloc byte[16];
            RandomNumberGenerator.Fill(bytes);
            return Convert.ToHexString(bytes).ToLowerInvariant();
        }

        // ---------- the install's answer (single, Steam) ----------

        /// <summary>The player's answer on a single or Steam install. Yes makes the install_id if there is
        /// none; No deletes the queue and the install_id.</summary>
        public void SetInstallAnswer(bool yes)
        {
            if (Shared) return;
            UpdateState(s => yes
                ? s with { Asked = true, Yes = true, InstallId = s.InstallId ?? NewId() }
                : s with { Asked = true, Yes = false, InstallId = null }, deleteQueue: !yes);
        }

        /// <summary>The prompt got no answer (disconnect, end of input, a non interactive run): stored as not
        /// asked, never a yes. Nothing from an earlier yes is kept.</summary>
        public void InstallAskInterrupted()
        {
            if (Shared) return;
            UpdateState(s => s with { Asked = false, Yes = false, InstallId = null }, deleteQueue: true);
        }

        /// <summary>The "new id" setting: a fresh install_id. Only while an id exists (a yes on single or
        /// Steam, the operator switch on a shared install). Returns the id, or null.</summary>
        public string? NewInstallId()
        {
            if (!WritesAllowed || State.InstallId == null) return null;
            return UpdateState(s => s.InstallId == null ? s : s with { InstallId = NewId() }, deleteQueue: false).InstallId;
        }

        /// <summary>The operator switched telemetry off on a shared install: the queue and the install_id go.
        /// Nothing is created when there is no telemetry folder.</summary>
        public void OperatorTurnedOff()
        {
            if (!Directory.Exists(Folder)) return;
            WithQueueLock(() =>
            {
                if (File.Exists(QueuePath)) File.Delete(QueuePath);
                lock (_stateGate) _state = null;
                var s = State;
                if (File.Exists(StatePath)) WriteStateLocked(s with { InstallId = null });
            });
            _answers.Clear();
        }

        // ---------- per player answers (BBS file, server table) ----------

        internal void ForgetCached(string key) => _answers.TryRemove(key, out _);

        private bool UsesTable => Source == TelemetrySource.Server;

        /// <summary>Read one answer from its file or row. A missing, damaged or unknown answer is not asked.</summary>
        private bool ReadAnswer(string key)
        {
            AnswerReads++;
            try
            {
                if (UsesTable)
                {
                    var row = _database()?.ReadTelemetryAnswer(key);
                    return row is { Asked: true, Yes: true };
                }
                string file = PlayerFilePath(_saveDirectory, key);
                if (!File.Exists(file)) return false;
                using var doc = JsonDocument.Parse(File.ReadAllText(file));
                var e = doc.RootElement;
                return e.ValueKind == JsonValueKind.Object && TryFlag(e, "asked", out bool asked) && TryFlag(e, "yes", out bool yes) && asked && yes;
            }
            catch (Exception ex)
            {
                DebugLogger.Instance.LogWarning("TELEMETRY", $"consent answer unreadable, read as not asked: {ex.Message}");
                return false;
            }
        }

        private void WriteAnswer(string key, bool asked, bool yes)
        {
            if (UsesTable)
            {
                _database()?.WriteTelemetryAnswer(key, asked, yes);
                return;
            }
            WriteAtomic(PlayerFilePath(_saveDirectory, key), new JsonObject { ["asked"] = asked ? 1 : 0, ["yes"] = yes ? 1 : 0 }.ToJsonString());
        }

        /// <summary>At login: the player's answer is read once and cached for the fights of the session.</summary>
        public void LoadPlayerAnswer(string? loginName)
        {
            string? key = TelemetryConsent.PlayerKey(loginName);
            if (key == null || !Shared) return;
            if (!TelemetryConsent.OperatorAllows()) { _answers.TryRemove(key, out _); return; }
            _answers[key] = ReadAnswer(key);
        }

        /// <summary>A player's answer on a shared install (the prompt or the settings line). Yes makes the
        /// install's id if there is none; No deletes the whole local queue (every player's rows not yet
        /// sent) and keeps the id, which belongs to the operator setting. Refused while the switch is off.</summary>
        public void SetPlayerAnswer(string? loginName, bool yes)
        {
            string? key = TelemetryConsent.PlayerKey(loginName);
            if (key == null || !Shared) return;
            if (!TelemetryConsent.OperatorAllows()) { _answers.TryRemove(key, out _); return; }
            WithQueueLock(() =>
            {
                WriteAnswer(key, true, yes);
                if (yes)
                {
                    lock (_stateGate) _state = null;
                    var s = State;
                    if (s.InstallId == null || !File.Exists(StatePath)) WriteStateLocked(s with { InstallId = s.InstallId ?? NewId() });
                }
                else if (File.Exists(QueuePath)) File.Delete(QueuePath);
            });
            _answers[key] = yes;
        }

        /// <summary>A player's prompt got no answer: stored as not asked, never a yes.</summary>
        public void PlayerAskInterrupted(string? loginName)
        {
            string? key = TelemetryConsent.PlayerKey(loginName);
            if (key == null || !Shared) return;
            _answers[key] = false;
            if (!TelemetryConsent.OperatorAllows()) return;
            WithQueueLock(() => WriteAnswer(key, false, false));
        }

        // ---------- the fight's gate ----------

        /// <summary>
        /// True when a fight of this login may be queued. Single and Steam: the install's stored yes with
        /// its id. BBS and server: the switch on and this player's answer cached at login; a player with no
        /// cached answer is not queued, and nothing is read here (this runs on the combat thread).
        /// </summary>
        internal bool ShouldQueue(string? loginName)
        {
            if (!Shared)
            {
                var s = State;
                return s.Asked && s.Yes && s.InstallId != null;
            }
            if (!TelemetryConsent.OperatorAllows()) return false;
            string? key = TelemetryConsent.PlayerKey(loginName);
            return key != null && _answers.TryGetValue(key, out bool yes) && yes;
        }

        // ---------- the queue ----------

        /// <summary>A failed append is logged at most once in this window.</summary>
        internal static readonly TimeSpan AppendErrorLogWindow = TimeSpan.FromMinutes(10);
        private readonly object _errorGate = new();
        private DateTime _lastErrorLog = DateTime.MinValue;

        private static string Line(long day, TelemetryRow row) =>
            new JsonObject { ["d"] = day, ["r"] = row.ToJson() }.ToJsonString();

        /// <summary>One queue line, or null when it is damaged (not JSON, a wrapper other than the day and
        /// the row, or a row that fails the bounds).</summary>
        internal static (long Day, TelemetryRow Row)? ParseLine(string line)
        {
            try
            {
                using var doc = JsonDocument.Parse(line);
                var e = doc.RootElement;
                if (e.ValueKind != JsonValueKind.Object) return null;
                int n = 0;
                foreach (var _ in e.EnumerateObject()) n++;
                if (n != 2) return null;
                if (!e.TryGetProperty("d", out var d) || d.ValueKind != JsonValueKind.Number || !d.TryGetInt64(out long day)) return null;
                if (!e.TryGetProperty("r", out var r)) return null;
                var row = TelemetryRow.FromJson(r);
                return row == null ? null : (day, row);
            }
            catch { return null; }
        }

        /// <summary>The queued rows, oldest first; damaged lines are skipped.</summary>
        internal List<(long Day, TelemetryRow Row)> ReadQueue()
        {
            var list = new List<(long, TelemetryRow)>();
            if (!File.Exists(QueuePath)) return list;
            foreach (var line in File.ReadAllLines(QueuePath))
            {
                var parsed = ParseLine(line);
                if (parsed != null) list.Add(parsed.Value);
            }
            return list;
        }

        /// <summary>
        /// Add one row under the queue lock. Damaged lines and lines older than <see cref="MaxAgeDays"/>
        /// days are dropped, and only the newest <see cref="MaxRows"/> rows are kept. Runs off the combat
        /// thread; a failure is logged (at most once a window) and never thrown.
        /// </summary>
        public virtual void Append(TelemetryRow row)
        {
            try
            {
                if (!WritesAllowed) return;
                using var held = AcquireLock();
                long today = DayNumber(Clock());
                string[] existing = File.Exists(QueuePath) ? File.ReadAllLines(QueuePath) : Array.Empty<string>();
                var kept = new List<string>(existing.Length + 1);
                bool dropped = false;
                foreach (var line in existing)
                {
                    var parsed = ParseLine(line);
                    if (parsed == null || today - parsed.Value.Day > MaxAgeDays) { dropped = true; continue; }
                    kept.Add(line);
                }
                string add = Line(today, row);
                if (!dropped && kept.Count + 1 <= MaxRows)
                {
                    File.AppendAllText(QueuePath, add + "\n");
                    return;
                }
                kept.Add(add);
                if (kept.Count > MaxRows) kept.RemoveRange(0, kept.Count - MaxRows);
                WriteAtomic(QueuePath, string.Concat(kept.Select(l => l + "\n")));
            }
            catch (Exception ex)
            {
                AppendFailed(ex);
            }
        }

        private void AppendFailed(Exception ex)
        {
            lock (_errorGate)
            {
                AppendFailures++;
                var now = DateTime.UtcNow;
                if (now - _lastErrorLog < AppendErrorLogWindow) return;
                _lastErrorLog = now;
                AppendErrorLogs++;
                DebugLogger.Instance.LogWarning("TELEMETRY", $"telemetry row not queued: {ex.Message}; further failures are reported at most every {AppendErrorLogWindow.TotalMinutes:0} minutes");
            }
        }
    }
}
