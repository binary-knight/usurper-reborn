using System;
using System.Collections.Generic;
using System.Globalization;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Security.Authentication;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Threading;
using System.Threading.Tasks;

namespace UsurperRemake.Systems
{
    /// <summary>1.2.7: the server's reply to one batch: the HTTP status and the body text.</summary>
    internal readonly record struct TelemetryReply(int Status, string Body);

    /// <summary>1.2.7: sends one request body to the telemetry endpoint. Throws on a network error or a
    /// timeout. Only ever obtained from <see cref="TelemetrySenderFactory"/>; tests pass their own.</summary>
    internal interface ITelemetrySender
    {
        Task<TelemetryReply> SendAsync(byte[] body);
    }

    /// <summary>1.2.7: the one place a real sender is made. The test run replaces it with a guard that
    /// throws, so no test can reach the network.</summary>
    internal static class TelemetrySenderFactory
    {
        internal static Func<ITelemetrySender> Create { get; set; } = () => new HttpTelemetrySender();
    }

    /// <summary>
    /// 1.2.7: the real sender (pattern BugReportSystem.CreateTlsClient): one fixed HTTPS host, TLS 1.2 and
    /// 1.3 with the normal certificate checks, a 10 second timeout, redirects not followed.
    /// </summary>
    internal sealed class HttpTelemetrySender : ITelemetrySender
    {
        internal const string Endpoint = "https://usurper-reborn.net/api/telemetry/v1/combat";
        internal static readonly TimeSpan RequestTimeout = TimeSpan.FromSeconds(10);
        internal const int MaxReplyBytes = 64 * 1024;

        internal HttpClientHandler Handler { get; }
        internal HttpClient Client { get; }

        internal HttpTelemetrySender()
        {
            Handler = new HttpClientHandler
            {
                SslProtocols = SslProtocols.Tls12 | SslProtocols.Tls13,
                AllowAutoRedirect = false,
            };
            Client = new HttpClient(Handler)
            {
                Timeout = RequestTimeout,
                MaxResponseContentBufferSize = MaxReplyBytes,
            };
        }

        public async Task<TelemetryReply> SendAsync(byte[] body)
        {
            using var content = new ByteArrayContent(body);
            content.Headers.ContentType = new MediaTypeHeaderValue("application/json");
            using var response = await Client.PostAsync(Endpoint, content).ConfigureAwait(false);
            string text = await response.Content.ReadAsStringAsync().ConfigureAwait(false);
            return new TelemetryReply((int)response.StatusCode, text);
        }
    }

    /// <summary>What one upload attempt did.</summary>
    internal enum TelemetryUploadOutcome
    {
        NothingQueued,
        NotAllowed,
        TooSoon,
        Busy,
        Sent,
        Dropped,
        Kept,
        Stopped,
        Failed,
    }

    /// <summary>
    /// 1.2.7: sends the queued rows of one telemetry folder (DESIGN.md section 3). One upload in the
    /// background at start, then when the queue reaches <see cref="TriggerRows"/> rows, at most once an
    /// hour by last_upload in state.json (shared by every process of the folder). Nothing at exit, never on
    /// the combat thread. At most <see cref="MaxBatchRows"/> rows a request, moved to a batch file under the
    /// queue lock; the batch is deleted on a 2xx, dropped on a 400, and put back otherwise.
    /// </summary>
    public sealed class TelemetryUploader
    {
        internal const int MaxBatchRows = 100;
        internal const int TriggerRows = 100;
        internal const long MinIntervalSeconds = 3600;
        internal const int Schema = 1;

        private readonly TelemetryStore _store;
        private readonly ITelemetrySender _sender;
        private readonly Func<DateTime> _utcNow;
        private readonly Func<TelemetrySource> _source;
        private int _running;
        private long _notBefore;          // no trigger before this (Unix seconds); the lock check decides
        private long _unrecordedLast;     // an upload time whose write to state.json failed, held here
        private bool _loggedFailure;

        /// <summary>Log lines written for failed uploads (one a session).</summary>
        internal int FailureLogs { get; private set; }
        /// <summary>Times the store reported the upload time not written (each logged).</summary>
        internal int UploadTimeNotWritten { get; private set; }
        /// <summary>Times the store reported the server's stop not fully written (each logged).</summary>
        internal int StopNotWritten { get; private set; }
        /// <summary>The upload the last queue trigger started (tests await it).</summary>
        internal Task<TelemetryUploadOutcome>? LastTriggered { get; private set; }

        /// <summary>An uploader of <paramref name="store"/>. With no sender given, the factory's.</summary>
        internal TelemetryUploader(TelemetryStore store, ITelemetrySender? sender = null,
            Func<DateTime>? utcNow = null, Func<TelemetrySource>? source = null)
        {
            _store = store;
            _sender = sender ?? TelemetrySenderFactory.Create();
            _utcNow = utcNow ?? (() => DateTime.UtcNow);
            _source = source ?? TelemetryConsent.CurrentSource;
        }

        // ---------- start ----------

        private static int _started;

        /// <summary>The start upload (tests await it).</summary>
        internal static Task? StartTask { get; private set; }
        private static readonly object _attachGate = new();

        /// <summary>The store's uploader in this process, made once.</summary>
        internal static TelemetryUploader AttachTo(TelemetryStore store)
        {
            lock (_attachGate)
            {
                return store.Uploader ??= new TelemetryUploader(store);
            }
        }

        /// <summary>
        /// Once a process (single, Steam, BBS door, MUD server; a MUD login calls it again and nothing
        /// happens): attach the uploader of the active save directory and run one upload, in the background.
        /// Never throws.
        /// </summary>
        public static void StartInBackground()
        {
            if (Interlocked.Exchange(ref _started, 1) != 0) return;
            StartTask = Task.Run(async () =>
            {
                try
                {
                    var store = TelemetryConsent.CurrentStore();
                    if (store == null) return;
                    await AttachTo(store).UploadOnceAsync().ConfigureAwait(false);
                }
                catch (Exception ex)
                {
                    DebugLogger.Instance.LogWarning("TELEMETRY", $"telemetry start upload not run: {ex.Message}");
                }
            });
        }

        // ---------- the queue trigger ----------

        /// <summary>After an append: <paramref name="queued"/> rows wait. At <see cref="TriggerRows"/> or more,
        /// one upload in the background, unless one is running or the hour since the last is not over.</summary>
        internal void QueueGrew(int queued)
        {
            if (queued < TriggerRows) return;
            if (Unix(_utcNow()) < Interlocked.Read(ref _notBefore)) return;
            if (Volatile.Read(ref _running) != 0) return;
            LastTriggered = Task.Run(UploadOnceAsync);
        }

        // ---------- one upload ----------

        internal static long Unix(DateTime utc) =>
            new DateTimeOffset(DateTime.SpecifyKind(utc, DateTimeKind.Utc)).ToUnixTimeSeconds();

        /// <summary>One upload attempt. Never throws; a failure is logged once a session.</summary>
        internal async Task<TelemetryUploadOutcome> UploadOnceAsync()
        {
            if (Interlocked.Exchange(ref _running, 1) != 0) return TelemetryUploadOutcome.Busy;
            try
            {
                return await UploadCoreAsync().ConfigureAwait(false);
            }
            catch (Exception ex)
            {
                LogFailure($"telemetry upload not done: {ex.Message}");
                return TelemetryUploadOutcome.Failed;
            }
            finally
            {
                Volatile.Write(ref _running, 0);
            }
        }

        private async Task<TelemetryUploadOutcome> UploadCoreAsync()
        {
            // The operator switch before anything else: the queue lock creates the folder.
            if (_store.IsSharedInstall && !TelemetryConsent.OperatorAllows()) return TelemetryUploadOutcome.NotAllowed;
            if (!System.IO.Directory.Exists(_store.Folder)) return TelemetryUploadOutcome.NothingQueued;

            long now = Unix(_utcNow());
            TelemetryBatch? batch = null;
            string? installId = null;
            var outcome = TelemetryUploadOutcome.NothingQueued;
            _store.WithQueueLock(() =>
            {
                var s = _store.ReReadStateLocked();    // the answer as it is now, not as this process cached it
                if (!_store.MayUploadLocked(s)) { outcome = TelemetryUploadOutcome.NotAllowed; return; }
                long last = Math.Max(s.LastUpload, _unrecordedLast);
                if (now >= last && now - last < MinIntervalSeconds)
                {
                    Interlocked.Exchange(ref _notBefore, last + MinIntervalSeconds);
                    outcome = TelemetryUploadOutcome.TooSoon;
                    return;
                }
                batch = _store.TakeBatchLocked(MaxBatchRows);
                if (batch == null) return;
                installId = s.InstallId;
                Interlocked.Exchange(ref _notBefore, now + MinIntervalSeconds);
                if (_store.RecordUploadLocked(now)) _unrecordedLast = 0;
                else
                {
                    _unrecordedLast = now;    // the hour still holds in this process
                    UploadTimeNotWritten++;
                    DebugLogger.Instance.LogWarning("TELEMETRY", "telemetry upload time not written, held in memory");
                }
            });
            if (batch == null) return outcome;

            TelemetryReply? reply = null;
            Exception? error = null;
            try
            {
                byte[] body = BuildBody(batch.Lines, installId!, _source());
                reply = await _sender.SendAsync(body).ConfigureAwait(false);
            }
            catch (Exception ex) { error = ex; }

            var result = Classify(reply, error);
            try
            {
                _store.WithQueueLock(() =>
                {
                    switch (result)
                    {
                        case TelemetryUploadOutcome.Sent:
                        case TelemetryUploadOutcome.Dropped:
                            _store.ReturnBatchLocked(batch, keep: false);
                            break;
                        case TelemetryUploadOutcome.Stopped:
                            _store.ReturnBatchLocked(batch, keep: false);
                            if (!_store.StopLocked())
                            {
                                StopNotWritten++;
                                DebugLogger.Instance.LogWarning("TELEMETRY", "telemetry stop not fully written, held in this process");
                            }
                            break;
                        default:
                            _store.ReturnBatchLocked(batch, keep: true);
                            break;
                    }
                });
            }
            finally
            {
                batch.Handle.Dispose();    // without the lock the batch stays for a later upload
            }
            return result;
        }

        /// <summary>The reply as an outcome: 2xx with ok sent, 2xx with stop stopped, 400 dropped; anything
        /// else (a network error, a timeout, 429, 5xx, a 2xx that is neither) keeps the rows. Only a 2xx can
        /// stop.</summary>
        private TelemetryUploadOutcome Classify(TelemetryReply? reply, Exception? error)
        {
            if (error != null || reply == null)
            {
                LogFailure($"telemetry upload failed, rows kept: {error?.GetType().Name}: {error?.Message}");
                return TelemetryUploadOutcome.Kept;
            }
            int status = reply.Value.Status;
            if (status >= 200 && status < 300)
            {
                switch (ReadReply(reply.Value.Body))
                {
                    case true: return TelemetryUploadOutcome.Stopped;
                    case false: return TelemetryUploadOutcome.Sent;
                    default:
                        LogFailure($"telemetry upload reply not understood (HTTP {status}), rows kept");
                        return TelemetryUploadOutcome.Kept;
                }
            }
            if (status == 400)
            {
                LogFailure("telemetry upload refused (HTTP 400), batch dropped");
                return TelemetryUploadOutcome.Dropped;
            }
            LogFailure($"telemetry upload not accepted (HTTP {status}), rows kept");
            return TelemetryUploadOutcome.Kept;
        }

        /// <summary>True for {"stop":true}, false for {"ok":true}, null for anything else.</summary>
        internal static bool? ReadReply(string body)
        {
            try
            {
                using var doc = JsonDocument.Parse(body);
                var e = doc.RootElement;
                if (e.ValueKind != JsonValueKind.Object) return null;
                if (e.TryGetProperty("stop", out var stop) && stop.ValueKind == JsonValueKind.True) return true;
                if (e.TryGetProperty("ok", out var ok) && ok.ValueKind == JsonValueKind.True) return false;
                return null;
            }
            catch { return null; }
        }

        /// <summary>The game version as three integers, or null.</summary>
        internal static long[]? VersionTriple(string version)
        {
            var parts = version.Split('.');
            if (parts.Length < 3) return null;
            var v = new long[3];
            for (int i = 0; i < 3; i++)
                if (!long.TryParse(parts[i], NumberStyles.None, CultureInfo.InvariantCulture, out v[i])) return null;
            return v;
        }

        /// <summary>
        /// The request body: schema, the game version, the source, the install_id and at most
        /// <see cref="MaxBatchRows"/> rows. A row is the row object alone: the local queue day and the rest
        /// of the line's wrapper stay local, and there is no client time and no player key. A damaged line
        /// is never sent.
        /// </summary>
        internal static byte[] BuildBody(IReadOnlyList<string> lines, string installId, TelemetrySource source)
        {
            var version = VersionTriple(GameConfig.Version)
                ?? throw new InvalidOperationException("the game version is not three numbers");
            var rows = new JsonArray();
            foreach (var line in lines)
            {
                if (rows.Count >= MaxBatchRows) break;
                var parsed = TelemetryStore.ParseLine(line);
                if (parsed == null) continue;
                rows.Add(parsed.Value.Row.ToJson());
            }
            var body = new JsonObject
            {
                ["schema"] = Schema,
                ["version"] = new JsonArray(version[0], version[1], version[2]),
                ["source"] = (int)source,
                ["install_id"] = installId,
                ["rows"] = rows,
            };
            return Encoding.UTF8.GetBytes(body.ToJsonString());
        }

        private void LogFailure(string message)
        {
            if (_loggedFailure) return;
            _loggedFailure = true;
            FailureLogs++;
            DebugLogger.Instance.LogWarning("TELEMETRY", message + "; further upload failures this session are not logged");
        }
    }
}
