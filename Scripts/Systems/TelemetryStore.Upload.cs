using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;

namespace UsurperRemake.Systems
{
    /// <summary>1.2.7: rows the uploader has taken from the queue: a batch file under telemetry/, held open
    /// with no sharing while the request is out, so another uploader (another BBS node) sees it is in flight.
    /// A batch file nobody holds was left by a crash and is sent by the next upload.</summary>
    internal sealed class TelemetryBatch
    {
        internal TelemetryBatch(string path, FileStream handle, List<string> lines, long generation)
        {
            Path = path;
            Handle = handle;
            Lines = lines;
            Generation = generation;
        }

        internal string Path { get; }
        /// <summary>The queue generation the batch was taken under. Its rows go back to the queue only while
        /// state.json still holds this generation.</summary>
        internal long Generation { get; }
        internal FileStream Handle { get; }
        /// <summary>The queue lines of the batch, every one checked (no damaged line, none too old).</summary>
        internal List<string> Lines { get; }
    }

    /// <summary>
    /// 1.2.7: the upload side of the telemetry folder (DESIGN.md section 3). Every method named Locked runs
    /// while the caller holds the queue lock (<see cref="WithQueueLock"/>), which is not reentrant.
    /// </summary>
    public partial class TelemetryStore
    {
        internal const string BatchPattern = "batch-*.jsonl";

        private static readonly Regex BatchName = new(@"^batch-g(\d{1,18})-[0-9a-f]{32}\.jsonl$", RegexOptions.Compiled);

        /// <summary>A new batch file name. It carries the queue generation the batch is taken under (the name
        /// stays local, it is never sent), so a batch left by a crash shows which generation it belongs to.</summary>
        internal static string NewBatchFileName(long generation) => $"batch-g{generation}-{Guid.NewGuid():N}.jsonl";

        /// <summary>The queue generation in a batch file's name, or null when the name has none.</summary>
        internal static long? BatchFileGeneration(string path)
        {
            var m = BatchName.Match(System.IO.Path.GetFileName(path));
            return m.Success && long.TryParse(m.Groups[1].Value, out long g) ? g : null;
        }

        // A server stop this process received: it holds even when its write to state.json failed.
        private volatile bool _stoppedHere;

        /// <summary>The uploader of this folder in this process (attached at start). Told after an append
        /// how many rows are queued.</summary>
        internal TelemetryUploader? Uploader { get; set; }

        /// <summary>A BBS door or a self hosted server (the operator switch applies).</summary>
        internal bool IsSharedInstall => Shared;

        private void QueueGrew(int queued)
        {
            try { Uploader?.QueueGrew(queued); }
            catch (Exception ex) { DebugLogger.Instance.LogWarning("TELEMETRY", $"telemetry upload not started: {ex.Message}"); }
        }

        /// <summary>True when <paramref name="stoppedVersion"/> is this game version: the server's stop holds
        /// until the version changes.</summary>
        internal static bool IsStoppedVersion(string? stoppedVersion) => stoppedVersion != null && stoppedVersion == GameConfig.Version;

        private bool IsStopped(TelemetryState s) => _stoppedHere || IsStoppedVersion(s.StoppedVersion);

        /// <summary>state.json read again (another node or copy may have changed it); the cache follows.</summary>
        internal TelemetryState ReReadStateLocked()
        {
            lock (_stateGate)
            {
                _state = null;
                return State;
            }
        }

        /// <summary>Rows may be sent: an install_id, no stop for this version, and on single and Steam the
        /// player's stored yes, on a shared install the operator switch on (a player's No there has already
        /// deleted the whole queue).</summary>
        internal bool MayUploadLocked(TelemetryState s)
        {
            if (IsStopped(s) || s.InstallId == null) return false;
            return Shared ? TelemetryConsent.OperatorAllows() : s.Asked && s.Yes;
        }

        /// <summary>Writes last_upload (Unix seconds). False when state.json could not be written.</summary>
        internal bool RecordUploadLocked(long unixSeconds)
        {
            try
            {
                WriteStateLocked(ReReadStateLocked() with { LastUpload = unixSeconds });
                return true;
            }
            catch (Exception ex)
            {
                DebugLogger.Instance.LogWarning("TELEMETRY", $"telemetry upload time not written: {ex.Message}");
                return false;
            }
        }

        /// <summary>The server said stop: the queue and every batch go, and stopped_version is this game
        /// version, so appends are refused until the version changes. The stop holds in this process at once.
        /// False when a file could not be deleted or state.json could not be written.</summary>
        internal bool StopLocked()
        {
            _stoppedHere = true;
            bool ok = true;
            try { DeleteQueueFilesLocked(); }
            catch (Exception ex)
            {
                ok = false;
                DebugLogger.Instance.LogWarning("TELEMETRY", $"telemetry queue not deleted on the server's stop: {ex.Message}");
            }
            try { WriteStateLocked(Withdrawn(ReReadStateLocked()) with { StoppedVersion = GameConfig.Version }); }
            catch (Exception ex)
            {
                ok = false;
                DebugLogger.Instance.LogWarning("TELEMETRY", $"telemetry stop not written: {ex.Message}");
            }
            return ok;
        }

        /// <summary>How a withdrawal deletes a batch file. Tests replace it to stand for a batch another node
        /// holds open on Windows, which cannot be deleted.</summary>
        internal Action<string> DeleteBatchFile { get; set; } = File.Delete;

        private string[] BatchFiles() => Directory.Exists(Folder) ? Directory.GetFiles(Folder, BatchPattern) : Array.Empty<string>();

        /// <summary>Deletes the queue and every batch file. A batch another node is sending may refuse to go
        /// (Windows); that node re-reads the answer before it puts anything back, so it is dropped there.</summary>
        private void DeleteQueueFilesLocked()
        {
            if (File.Exists(QueuePath)) File.Delete(QueuePath);
            foreach (var batch in BatchFiles())
            {
                try { DeleteBatchFile(batch); }
                catch (Exception ex) { DebugLogger.Instance.LogWarning("TELEMETRY", $"telemetry batch in flight not deleted, its uploader drops it: {ex.Message}"); }
            }
        }

        private bool Sendable(string line, long today)
        {
            var parsed = ParseLine(line);
            return parsed != null && today - parsed.Value.Day <= MaxAgeDays;
        }

        private void WriteQueueLocked(IReadOnlyCollection<string> lines)
        {
            if (lines.Count == 0)
            {
                if (File.Exists(QueuePath)) File.Delete(QueuePath);
                return;
            }
            WriteAtomic(QueuePath, string.Concat(lines.Select(l => l + "\n")));
        }

        private static List<string> ReadLines(FileStream handle)
        {
            handle.Position = 0;
            using var reader = new StreamReader(handle, Encoding.UTF8, false, 4096, leaveOpen: true);
            return reader.ReadToEnd().Split('\n').Where(l => l.Length > 0).ToList();
        }

        private static void Rewrite(FileStream handle, List<string> lines)
        {
            handle.SetLength(0);
            handle.Position = 0;
            byte[] bytes = Encoding.UTF8.GetBytes(string.Concat(lines.Select(l => l + "\n")));
            handle.Write(bytes, 0, bytes.Length);
            handle.Flush(true);
        }

        /// <summary>
        /// The next batch, at most <paramref name="max"/> rows: a batch file nobody holds (left by a crash)
        /// first, unless its name is not of <paramref name="generation"/> (then it is deleted unsent), else the oldest rows of the queue moved to a new batch file. Damaged lines and lines older
        /// than <see cref="MaxAgeDays"/> days are dropped on the way and never sent. Null when nothing is
        /// queued. The batch file stays open with no sharing until <see cref="ReturnBatchLocked"/>. The batch
        /// records <paramref name="generation"/>, the queue generation of the state read under this lock.
        /// </summary>
        internal TelemetryBatch? TakeBatchLocked(int max, long generation)
        {
            long today = DayNumber(Clock());
            foreach (var path in BatchFiles().OrderBy(p => p, StringComparer.Ordinal))
            {
                FileStream handle;
                try { handle = new FileStream(path, FileMode.Open, FileAccess.ReadWrite, FileShare.None); }
                catch (IOException) { continue; }    // another uploader is sending it
                catch (UnauthorizedAccessException) { continue; }
                if (BatchFileGeneration(path) != generation)
                {
                    // taken before a withdrawal (a No, the switch off, a stop) whose delete failed, then left
                    // by a crash; or a name with no generation: dropped unsent
                    handle.Dispose();
                    File.Delete(path);
                    DebugLogger.Instance.LogWarning("TELEMETRY", "telemetry batch left from before a withdrawal dropped unsent");
                    continue;
                }
                try
                {
                    var lines = ReadLines(handle);
                    var good = lines.Where(l => Sendable(l, today)).ToList();
                    if (good.Count == 0)
                    {
                        handle.Dispose();
                        File.Delete(path);
                        continue;
                    }
                    if (good.Count > max)
                    {
                        var queued = File.Exists(QueuePath) ? File.ReadAllLines(QueuePath).Where(l => l.Length > 0) : Enumerable.Empty<string>();
                        WriteQueueLocked(good.Skip(max).Concat(queued).ToList());
                        good = good.Take(max).ToList();
                    }
                    if (good.Count != lines.Count) Rewrite(handle, good);
                    return new TelemetryBatch(path, handle, good, generation);
                }
                catch
                {
                    handle.Dispose();
                    throw;
                }
            }

            if (!File.Exists(QueuePath)) return null;
            var batch = new List<string>();
            var rest = new List<string>();
            bool dropped = false;
            foreach (var line in File.ReadAllLines(QueuePath))
            {
                if (!Sendable(line, today)) { dropped = true; continue; }
                if (batch.Count < max) batch.Add(line);
                else rest.Add(line);
            }
            if (batch.Count == 0)
            {
                if (dropped) WriteQueueLocked(rest);
                return null;
            }
            string newPath = Path.Combine(Folder, NewBatchFileName(generation));
            var created = new FileStream(newPath, FileMode.CreateNew, FileAccess.ReadWrite, FileShare.None);
            try
            {
                Rewrite(created, batch);
                WriteQueueLocked(rest);    // the rows are in the batch file before they leave the queue
            }
            catch
            {
                created.Dispose();
                try { File.Delete(newPath); } catch { }
                throw;
            }
            return new TelemetryBatch(newPath, created, batch, generation);
        }

        /// <summary>
        /// After the reply. <paramref name="keep"/>: the rows go back to the head of the queue, but only while
        /// the batch file still exists (a No or a stop since deletes it), the answer, read again, still allows
        /// sending, and the queue generation is the one the batch was taken under (a withdrawal since advanced
        /// it, also when the batch file could not be deleted, as on Windows while this node held it). Then the batch file is closed and deleted. When the queue cannot be written the
        /// batch file stays, so its rows are sent by a later upload instead of being lost.
        /// </summary>
        internal void ReturnBatchLocked(TelemetryBatch batch, bool keep)
        {
            bool deleteBatch = true;
            try
            {
                if (keep && File.Exists(batch.Path) && ReReadStateLocked() is var now && MayUploadLocked(now)
                    && now.Generation == batch.Generation)
                {
                    try
                    {
                        var queued = File.Exists(QueuePath) ? File.ReadAllLines(QueuePath).Where(l => l.Length > 0) : Enumerable.Empty<string>();
                        var all = batch.Lines.Concat(queued).ToList();
                        if (all.Count > MaxRows) all.RemoveRange(0, all.Count - MaxRows);
                        WriteQueueLocked(all);
                    }
                    catch (Exception ex)
                    {
                        deleteBatch = false;
                        DebugLogger.Instance.LogWarning("TELEMETRY", $"telemetry rows not put back, the batch is kept: {ex.Message}");
                    }
                }
            }
            finally
            {
                batch.Handle.Dispose();
                if (deleteBatch)
                {
                    try { if (File.Exists(batch.Path)) File.Delete(batch.Path); }
                    catch (Exception ex) { DebugLogger.Instance.LogWarning("TELEMETRY", $"telemetry batch not deleted: {ex.Message}"); }
                }
            }
        }
    }
}
