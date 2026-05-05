using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Threading.Tasks;
using UsurperRemake.BBS;

namespace UsurperRemake.Systems
{
    /// <summary>
    /// JSON file-based save backend for local and Steam single-player mode.
    /// Saves each game as a JSON file in the user's save directory.
    /// BBS door mode isolates saves per-BBS via subdirectories.
    /// </summary>
    public class FileSaveBackend : ISaveBackend
    {
        private readonly string baseSaveDirectory;
        private readonly JsonSerializerOptions jsonOptions;

        // v0.57.18 — single-writer guard. Without this, autosave + manual save +
        // emergency Ctrl+C save can race; FileShare.None made the second writer
        // throw on the FileStream constructor and the failure was silently swallowed
        // by the outer catch. SemaphoreSlim with capacity 1 serializes all write
        // operations across the process so writes can never collide.
        private readonly System.Threading.SemaphoreSlim _writeLock = new(1, 1);

        /// <summary>
        /// Get the active save directory (includes BBS namespace if in door mode)
        /// </summary>
        private string SaveDirectory
        {
            get
            {
                var bbsNamespace = DoorMode.GetSaveNamespace();
                if (!string.IsNullOrEmpty(bbsNamespace))
                {
                    var bbsDir = Path.Combine(baseSaveDirectory, bbsNamespace);
                    Directory.CreateDirectory(bbsDir);
                    return bbsDir;
                }
                return baseSaveDirectory;
            }
        }

        public FileSaveBackend()
        {
            baseSaveDirectory = Path.Combine(GetUserDataPath(), "saves");
            Directory.CreateDirectory(baseSaveDirectory);

            jsonOptions = new JsonSerializerOptions
            {
                WriteIndented = true,
                PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
                IncludeFields = true,
                // v0.57.18: explicit MaxDepth above the .NET 8 default of 64. Save graphs
                // are wide (130 NPCs × per-NPC fields) but rarely deeply nested; deeply
                // nested WorldEvent.Parameters dictionaries or recursive companion data
                // could theoretically trip the default. 256 covers any realistic future
                // shape without masking genuine cycles (which would still throw on
                // ReferenceLoopHandling, default ignore-loops behavior).
                MaxDepth = 256
            };
        }

        public async Task<bool> WriteGameData(string playerName, SaveGameData data)
        {
            // v0.57.18 atomic write + concurrent guard + memory-stream pre-serialize.
            // Player report ("saves run out of memory after I quit") was actually a
            // WRITE-path corruption story, not just a load-side bloat story:
            //
            //   OLD FLOW: FileStream(filePath, FileMode.Create, ...) truncates the
            //   target file IMMEDIATELY, then JsonSerializer.SerializeAsync streams
            //   the new content in. ANY interrupt mid-write — OOM during serialize,
            //   Ctrl+C, antivirus scan lock, power loss, process kill — leaves the
            //   primary save half-written and corrupt. Next load fails.
            //
            //   NEW FLOW:
            //     1. Acquire single-writer SemaphoreSlim (no concurrent saves).
            //     2. Serialize the entire object graph to a MemoryStream FIRST so
            //        any OOM happens BEFORE we touch the target file.
            //     3. Write the byte buffer to <name>.json.tmp, FlushAsync to disk.
            //     4. Atomic File.Move(tmp, primary, overwrite=true). On NTFS / ext4 /
            //        APFS this rename is all-or-nothing — the primary file is either
            //        fully the old version OR fully the new version, never torn.
            //     5. If anything fails mid-flow, the primary is untouched and the
            //        pre-write CreateBackup() (called from SaveSystem) still has the
            //        previous good state.
            string fileName = GetSaveFileName(playerName);
            string filePath = Path.Combine(SaveDirectory, fileName);
            string tempPath = filePath + ".tmp";

            await _writeLock.WaitAsync();
            try
            {
                // Step 1: serialize to in-memory buffer. If the save graph is too
                // large to allocate as a single byte[] we OOM here, BEFORE the
                // primary file is touched. The player's existing save survives.
                byte[] bytes;
                try
                {
                    using var ms = new MemoryStream();
                    await JsonSerializer.SerializeAsync(ms, data, jsonOptions);
                    bytes = ms.ToArray();
                }
                catch (OutOfMemoryException oom)
                {
                    DebugLogger.Instance.LogSystemError("SAVE",
                        $"OOM serializing save for '{playerName}' — primary save preserved (NOT touched). Save graph too large to allocate; bloat surfaces should be capped.",
                        oom.StackTrace);
                    return false;
                }

                // Step 2: write bytes to temp file with explicit flush.
                // FileShare.None ensures no other process / save can read a
                // half-written temp file. useAsync = true is required for
                // FlushAsync to actually flush to disk.
                using (var fs = new FileStream(tempPath, FileMode.Create, FileAccess.Write, FileShare.None, 8192, useAsync: true))
                {
                    await fs.WriteAsync(bytes, 0, bytes.Length);
                    await fs.FlushAsync();
                }

                // Step 3: atomic rename. From here on the primary is either fully
                // old or fully new — there is no in-between state visible to other
                // readers. File.Move with overwrite=true is atomic on the same
                // filesystem on Windows NTFS, Linux ext4, macOS APFS.
                File.Move(tempPath, filePath, overwrite: true);

                // SAVE_AUDIT — log every save size and warn loudly above the
                // threshold. Forensic trail for the next bloat regression. Mirrors
                // the GOLD_AUDIT pattern.
                try
                {
                    long sizeBytes = bytes.LongLength;
                    long sizeKB = sizeBytes / 1024;
                    if (sizeBytes > GameConfig.SaveSizeWarningBytes)
                    {
                        long sizeMB = sizeBytes / (1024 * 1024);
                        DebugLogger.Instance.LogWarning("SAVE_AUDIT",
                            $"LARGE SAVE: '{playerName}' = {sizeMB} MB ({sizeBytes:N0} bytes) at {filePath}. " +
                            $"Threshold = {GameConfig.SaveSizeWarningBytes / (1024 * 1024)} MB. " +
                            $"Bloat surfaces are likely accumulating; check NPC/relationship/dialogue lists.");
                    }
                    else
                    {
                        DebugLogger.Instance.LogInfo("SAVE_AUDIT", $"SAVE: '{playerName}' = {sizeKB} KB");
                    }
                }
                catch { /* size logging is informational only */ }

                return true;
            }
            catch (Exception ex)
            {
                DebugLogger.Instance.LogSystemError("SAVE", $"Failed to write game data for '{playerName}': {ex.Message}", ex.StackTrace);
                // Clean up any leftover temp file from a failed write so it doesn't
                // accumulate. Failure to clean is non-fatal.
                try { if (File.Exists(tempPath)) File.Delete(tempPath); } catch { }
                return false;
            }
            finally
            {
                _writeLock.Release();
            }
        }

        public async Task<SaveGameData?> ReadGameData(string playerName)
        {
            try
            {
                var fileName = GetSaveFileName(playerName);
                var filePath = Path.Combine(SaveDirectory, fileName);

                if (!File.Exists(filePath))
                {
                    DebugLogger.Instance.LogDebug("LOAD", $"No save file found for '{playerName}'");
                    return null;
                }

                var json = await File.ReadAllTextAsync(filePath);
                var saveData = JsonSerializer.Deserialize<SaveGameData>(json, jsonOptions);

                if (saveData == null)
                {
                    DebugLogger.Instance.LogError("LOAD", "Failed to deserialize save data");
                    return null;
                }

                if (saveData.Version < GameConfig.MinSaveVersion)
                {
                    DebugLogger.Instance.LogError("LOAD", $"Save file version {saveData.Version} is too old (minimum: {GameConfig.MinSaveVersion})");
                    return null;
                }

                DebugLogger.Instance.LogDebug("LOAD", $"Save file loaded: {fileName} (v{saveData.Version})");
                return saveData;
            }
            catch (Exception ex)
            {
                DebugLogger.Instance.LogSystemError("LOAD", $"Failed to load game: {ex.Message}", ex.StackTrace);
                return null;
            }
        }

        public async Task<SaveGameData?> ReadGameDataByFileName(string fileName)
        {
            var (data, _) = await ReadGameDataByFileNameWithError(fileName);
            return data;
        }

        /// <summary>
        /// v0.57.14: Load-path resilience. Returns the save data on success, or a
        /// specific human-readable error message on failure, so the caller can show
        /// the player what actually went wrong (file missing, disk error, malformed
        /// JSON, OOM on a bloated save, etc.) instead of a generic "corrupted" line.
        /// Error messages also include the file path so the player knows where to
        /// look for manual recovery (backup file, autosaves, text editor).
        /// </summary>
        public async Task<(SaveGameData? Data, string? Error)> ReadGameDataByFileNameWithError(string fileName)
        {
            string filePath = "";
            try
            {
                filePath = Path.Combine(SaveDirectory, fileName);

                if (!File.Exists(filePath))
                {
                    return (null, $"Save file not found on disk: {filePath}");
                }

                var fileInfo = new FileInfo(filePath);
                long fileSizeBytes = fileInfo.Length;

                string json;
                try
                {
                    json = await File.ReadAllTextAsync(filePath);
                }
                catch (OutOfMemoryException)
                {
                    long sizeMB = fileSizeBytes / (1024 * 1024);
                    return (null, $"Save file is too large to load ({sizeMB} MB at {filePath}). This indicates the save has accumulated state unexpectedly. Try the backup file if present.");
                }
                catch (IOException ex)
                {
                    return (null, $"Cannot read save file ({ex.Message}). Path: {filePath}. Another program may have the file open.");
                }
                catch (UnauthorizedAccessException ex)
                {
                    return (null, $"Permission denied reading save file ({ex.Message}). Path: {filePath}.");
                }

                SaveGameData? saveData;
                try
                {
                    saveData = JsonSerializer.Deserialize<SaveGameData>(json, jsonOptions);
                }
                catch (OutOfMemoryException)
                {
                    long sizeMB = fileSizeBytes / (1024 * 1024);
                    return (null, $"Not enough memory to parse save file ({sizeMB} MB at {filePath}). The save contains more data than the process can hold.");
                }
                catch (JsonException ex)
                {
                    return (null, $"Save file has malformed JSON near line {ex.LineNumber}, position {ex.BytePositionInLine} ({ex.Message}). Path: {filePath}.");
                }

                if (saveData == null)
                {
                    return (null, $"Save file deserialized to null (unexpected JSON structure). Path: {filePath}.");
                }

                if (saveData.Version < GameConfig.MinSaveVersion)
                {
                    return (null, $"Save file version {saveData.Version} is older than the minimum supported version {GameConfig.MinSaveVersion}. Path: {filePath}.");
                }

                return (saveData, null);
            }
            catch (Exception ex)
            {
                DebugLogger.Instance.LogError("SAVE", $"Unexpected error loading '{fileName}': {ex.GetType().Name}: {ex.Message}\n{ex.StackTrace}");
                return (null, $"Unexpected error loading save ({ex.GetType().Name}: {ex.Message}). Path: {filePath}.");
            }
        }

        public bool GameDataExists(string playerName)
        {
            var fileName = GetSaveFileName(playerName);
            var filePath = Path.Combine(SaveDirectory, fileName);
            return File.Exists(filePath);
        }

        public bool DeleteGameData(string playerName)
        {
            // v0.57.18: take the write lock so a delete can't race with an
            // in-flight save and produce a half-written file with no primary.
            _writeLock.Wait();
            try
            {
                var fileName = GetSaveFileName(playerName);
                var filePath = Path.Combine(SaveDirectory, fileName);

                if (File.Exists(filePath))
                {
                    File.Delete(filePath);
                    return true;
                }

                return false;
            }
            catch (Exception ex)
            {
                DebugLogger.Instance.LogWarning("SAVE", $"DeleteGameData failed for '{playerName}': {ex.Message}");
                return false;
            }
            finally
            {
                _writeLock.Release();
            }
        }

        /// <summary>
        /// v0.60.7: predicate for auxiliary / non-character JSON files that live
        /// alongside saves. Returns true for files that should be excluded from
        /// every listing path (`GetAllSaves`, `GetPlayerSaves`, `GetAllPlayerNames`).
        ///
        /// `sysop_config.json` is the case that motivated this -- player report
        /// (single-player Linux): the game creates the SysOpConfig file in the
        /// save directory on startup, and the listing methods would parse it as
        /// a save (fail), then fall back to the filename-based recovery path
        /// and surface "sysop_config" as a phantom character in the load menu.
        ///
        /// The SysOpConfig file is intentionally BBS-namespaced (per-BBS-instance
        /// configuration), so it has to live in the save directory for the
        /// namespacing to work. Filtering at listing time is the cleanest fix.
        /// </summary>
        private static bool IsAuxiliaryFile(string fileName)
        {
            return string.Equals(fileName, "sysop_config.json", StringComparison.OrdinalIgnoreCase);
        }

        public List<SaveInfo> GetAllSaves()
        {
            var saves = new List<SaveInfo>();
            var primaryNames = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

            try
            {
                var files = Directory.GetFiles(SaveDirectory, "*.json");

                foreach (var file in files)
                {
                    string fileName = Path.GetFileName(file);

                    // Skip obvious non-character files. emergency_*.json is also skipped
                    // here for primary listing — it's surfaced separately as a recovery
                    // slot below if no real save exists for that character.
                    if (fileName.Contains("_autosave") ||
                        fileName.Contains("_backup") ||
                        fileName.StartsWith("emergency_") ||
                        IsAuxiliaryFile(fileName))
                    {
                        continue;
                    }

                    // Try full deserialize first; on ANY failure, fall back to filename-
                    // based metadata so the slot is still visible. v0.57.18 (Miyabi
                    // report — load menu showed empty despite Coosh.json on disk):
                    // previously the inner-fallback's catch could itself silently drop
                    // the save if FileInfo / Path operations threw on a race or odd
                    // filesystem state. Now the fallback is guaranteed to add a slot
                    // unless the OS itself can't tell us the filename, in which case
                    // we synthesize from the path string.
                    try
                    {
                        var json = File.ReadAllText(file);
                        var saveData = JsonSerializer.Deserialize<SaveGameData>(json, jsonOptions);

                        if (saveData?.Player != null)
                        {
                            string playerName = saveData.Player.Name2 ?? saveData.Player.Name1 ?? Path.GetFileNameWithoutExtension(file);
                            primaryNames.Add(playerName);
                            saves.Add(new SaveInfo
                            {
                                PlayerName = playerName,
                                SaveTime = saveData.SaveTime,
                                Level = saveData.Player.Level,
                                CurrentDay = saveData.CurrentDay,
                                TurnsRemaining = saveData.Player.TurnsRemaining,
                                FileName = fileName
                            });
                            continue;
                        }
                    }
                    catch (Exception ex)
                    {
                        DebugLogger.Instance.LogWarning("SAVE", $"Could not parse '{fileName}' ({ex.GetType().Name}: {ex.Message}). Listing with filename-only metadata so player can still load it.");
                    }

                    // Fallback path: ALWAYS add the slot. Previously a second exception
                    // here (FileInfo race, etc.) silently dropped the save. Now we wrap
                    // each operation independently and synthesize whatever we can.
                    string nameFromFile;
                    DateTime saveTime;
                    try { nameFromFile = Path.GetFileNameWithoutExtension(file); }
                    catch { nameFromFile = fileName.Replace(".json", "", StringComparison.OrdinalIgnoreCase); }
                    try { saveTime = new FileInfo(file).LastWriteTime; }
                    catch { saveTime = DateTime.Now; }

                    primaryNames.Add(nameFromFile);
                    saves.Add(new SaveInfo
                    {
                        PlayerName = nameFromFile,
                        SaveTime = saveTime,
                        Level = 0,
                        CurrentDay = 0,
                        TurnsRemaining = 0,
                        FileName = fileName,
                        IsRecovered = true
                    });
                }

                // Surface emergency_<name>_<ts>.json files as recovery slots ONLY when
                // the character has no primary save on disk. If the player has both
                // Coosh.json AND emergency_Coosh_*.json, the primary takes precedence
                // (the recovery flow inside ShowLoadFailureWithRecovery offers the
                // emergency file separately if the primary fails to load).
                foreach (var file in files)
                {
                    string fileName = Path.GetFileName(file);
                    if (!fileName.StartsWith("emergency_")) continue;

                    string charName = ParseCharacterNameFromEmergencyFile(fileName);
                    if (string.IsNullOrEmpty(charName)) continue;
                    if (primaryNames.Contains(charName)) continue; // already covered

                    DateTime saveTime;
                    try { saveTime = new FileInfo(file).LastWriteTime; }
                    catch { saveTime = DateTime.Now; }

                    primaryNames.Add(charName);
                    saves.Add(new SaveInfo
                    {
                        PlayerName = charName,
                        SaveTime = saveTime,
                        Level = 0,
                        CurrentDay = 0,
                        TurnsRemaining = 0,
                        FileName = fileName,
                        IsRecovered = true,
                        IsEmergency = true
                    });
                }
            }
            catch (Exception ex)
            {
                DebugLogger.Instance.LogError("SAVE", $"GetAllSaves enumeration failed: {ex.Message}");
            }

            return saves;
        }

        public List<SaveInfo> GetPlayerSaves(string playerName)
        {
            var saves = new List<SaveInfo>();
            var sanitizedName = string.Join("_", playerName.Split(Path.GetInvalidFileNameChars()));

            try
            {
                var pattern = $"{sanitizedName}*.json";
                var files = Directory.GetFiles(SaveDirectory, pattern);

                foreach (var file in files)
                {
                    string fileName = Path.GetFileName(file);
                    if (IsAuxiliaryFile(fileName)) continue;
                    bool isAutosave = fileName.Contains("_autosave_");
                    bool isBackup = fileName.Contains("_backup");
                    bool isEmergency = fileName.StartsWith("emergency_");

                    try
                    {
                        var json = File.ReadAllText(file);
                        var saveData = JsonSerializer.Deserialize<SaveGameData>(json, jsonOptions);

                        if (saveData?.Player != null)
                        {
                            saves.Add(new SaveInfo
                            {
                                PlayerName = saveData.Player.Name2 ?? saveData.Player.Name1 ?? sanitizedName,
                                ClassName = saveData.Player.Class.ToString(),
                                SaveTime = saveData.SaveTime,
                                Level = saveData.Player.Level,
                                CurrentDay = saveData.CurrentDay,
                                TurnsRemaining = saveData.Player.TurnsRemaining,
                                FileName = fileName,
                                IsAutosave = isAutosave,
                                IsEmergency = isEmergency,
                                SaveType = isEmergency ? "Emergency" : isBackup ? "Backup" : isAutosave ? "Autosave" : "Manual Save"
                            });
                            continue;
                        }
                    }
                    catch (Exception ex)
                    {
                        DebugLogger.Instance.LogWarning("SAVE", $"Could not parse '{fileName}' ({ex.GetType().Name}: {ex.Message}). Listing as recovery slot so player can still see it.");
                    }

                    // v0.57.18 — fallback also for the per-player listing. Without this,
                    // a single bloated save would vanish from the recent-saves picker
                    // and the player would see "no saves for this character" despite
                    // the file being on disk. Now: list it with filename-derived
                    // metadata and IsRecovered=true so the load flow routes through
                    // ShowLoadFailureWithRecovery.
                    DateTime saveTime;
                    try { saveTime = new FileInfo(file).LastWriteTime; }
                    catch { saveTime = DateTime.Now; }

                    saves.Add(new SaveInfo
                    {
                        PlayerName = sanitizedName,
                        ClassName = "?",
                        SaveTime = saveTime,
                        Level = 0,
                        CurrentDay = 0,
                        TurnsRemaining = 0,
                        FileName = fileName,
                        IsAutosave = isAutosave,
                        IsEmergency = isEmergency,
                        IsRecovered = true,
                        SaveType = isEmergency ? "Emergency" : isBackup ? "Backup" : isAutosave ? "Autosave" : "Recovery"
                    });
                }

                saves = saves.OrderByDescending(s => s.SaveTime).ToList();
            }
            catch (Exception ex)
            {
                DebugLogger.Instance.LogWarning("SAVE", $"GetPlayerSaves enumeration failed for '{playerName}': {ex.Message}");
            }

            return saves;
        }

        public SaveInfo? GetMostRecentSave(string playerName)
        {
            var saves = GetPlayerSaves(playerName);
            return saves.FirstOrDefault();
        }

        public List<string> GetAllPlayerNames()
        {
            var playerNames = new HashSet<string>();

            try
            {
                var files = Directory.GetFiles(SaveDirectory, "*.json");

                foreach (var file in files)
                {
                    string fileName = Path.GetFileName(file);

                    // Skip rotation/backup files — they don't establish a "character
                    // exists" claim by themselves. Emergency files are handled below
                    // because they may be the ONLY surviving evidence of a character
                    // when the primary save was lost.
                    if (fileName.Contains("_autosave_") || fileName.Contains("_backup") || IsAuxiliaryFile(fileName))
                        continue;

                    bool isEmergency = fileName.StartsWith("emergency_");

                    try
                    {
                        var json = File.ReadAllText(file);
                        var saveData = JsonSerializer.Deserialize<SaveGameData>(json, jsonOptions);

                        if (saveData?.Player != null)
                        {
                            var playerName = saveData.Player.Name2 ?? saveData.Player.Name1;
                            if (!string.IsNullOrWhiteSpace(playerName))
                            {
                                playerNames.Add(playerName);
                                continue;
                            }
                        }
                    }
                    catch (Exception ex)
                    {
                        DebugLogger.Instance.LogWarning("SAVE", $"GetAllPlayerNames could not parse '{fileName}' ({ex.GetType().Name}). Falling back to filename to keep the character visible in the load menu.");
                    }

                    // v0.57.18 (Miyabi report — single-player saves vanished from the
                    // load menu despite Coosh.json being on disk). Previously this
                    // method silently skipped any file that failed to deserialize, so
                    // bloated saves (the v0.57.16 NPC-memory class of bug) and saves
                    // truncated by Ctrl+C interruption ended up invisible. Now: derive
                    // the character name from the filename so the player can at least
                    // see the slot and route through ShowLoadFailureWithRecovery.
                    string fallbackName = isEmergency
                        ? ParseCharacterNameFromEmergencyFile(fileName)
                        : Path.GetFileNameWithoutExtension(file);
                    if (!string.IsNullOrWhiteSpace(fallbackName))
                        playerNames.Add(fallbackName);
                }
            }
            catch (Exception ex)
            {
                DebugLogger.Instance.LogWarning("SAVE", $"GetAllPlayerNames enumeration failed: {ex.Message}");
            }

            return playerNames.OrderBy(n => n).ToList();
        }

        /// <summary>
        /// Extract the character name from an emergency save filename. Format:
        /// `emergency_<character>_<timestamp>.json` or `emergency_autosave.json`
        /// (the global emergency file from Ctrl+C). Returns "" for the global
        /// emergency file (no character context) so we don't list a phantom name.
        /// </summary>
        private string ParseCharacterNameFromEmergencyFile(string fileName)
        {
            if (!fileName.StartsWith("emergency_")) return "";
            string stem = Path.GetFileNameWithoutExtension(fileName);
            string body = stem.Substring("emergency_".Length);
            if (body == "autosave") return ""; // global emergency dump, not character-specific
            // Strip a trailing _<timestamp> if present. Timestamps are
            // yyyy-MM-dd_HH-mm-ss but emergency saves may use other formats; we
            // peel back at most two underscore-separated trailing chunks that
            // start with a digit.
            for (int i = 0; i < 2; i++)
            {
                int lastUnderscore = body.LastIndexOf('_');
                if (lastUnderscore <= 0) break;
                string tail = body.Substring(lastUnderscore + 1);
                if (tail.Length == 0 || !char.IsDigit(tail[0])) break;
                body = body.Substring(0, lastUnderscore);
            }
            return body;
        }

        public bool IsDisplayNameTaken(string displayName, string excludeUsername)
        {
            // Single-player file saves don't need duplicate display name checks
            return false;
        }

        public async Task<bool> WriteAutoSave(string playerName, SaveGameData data)
        {
            var timestamp = DateTime.Now.ToString("yyyy-MM-dd_HH-mm-ss");
            var autosaveName = $"{playerName}_autosave_{timestamp}";

            var success = await WriteGameData(autosaveName, data);

            if (success)
            {
                RotateAutosaves(playerName);
            }

            return success;
        }

        public void CreateBackup(string playerName)
        {
            // v0.57.18: take the write lock so the backup snapshot can't capture
            // a half-written file from a concurrent in-flight save. Atomic write
            // makes the half-written case impossible from THIS backend, but external
            // tools / antivirus could still hold the file open mid-rename.
            _writeLock.Wait();
            try
            {
                var fileName = GetSaveFileName(playerName);
                var filePath = Path.Combine(SaveDirectory, fileName);

                if (File.Exists(filePath))
                {
                    var backupPath = Path.Combine(SaveDirectory, $"{Path.GetFileNameWithoutExtension(fileName)}_backup.json");
                    File.Copy(filePath, backupPath, true);
                }
            }
            catch (Exception ex)
            {
                DebugLogger.Instance.LogWarning("SAVE", $"CreateBackup failed for '{playerName}': {ex.Message}");
            }
            finally
            {
                _writeLock.Release();
            }
        }

        public string GetSaveDirectory() => SaveDirectory;

        private string GetSaveFileName(string playerName)
        {
            var sanitized = string.Join("_", playerName.Split(Path.GetInvalidFileNameChars()));
            return $"{sanitized}.json";
        }

        private void RotateAutosaves(string playerName)
        {
            try
            {
                var autosavePattern = $"{string.Join("_", playerName.Split(Path.GetInvalidFileNameChars()))}_autosave_*.json";
                var autosaveFiles = Directory.GetFiles(SaveDirectory, autosavePattern);

                // v0.57.18: order by LastWriteTime instead of CreationTime so synced
                // / restored / copied files don't trick rotation into deleting the
                // most recent saves. CreationTime is the original file-creation
                // moment; LastWriteTime is when we actually wrote to it. The latter
                // is what we want for "keep the 5 most recent saves".
                var sortedFiles = autosaveFiles
                    .Select(f => new FileInfo(f))
                    .OrderByDescending(f => f.LastWriteTime)
                    .ToList();

                for (int i = 5; i < sortedFiles.Count; i++)
                {
                    sortedFiles[i].Delete();
                }
            }
            catch (Exception ex)
            {
                DebugLogger.Instance.LogWarning("SAVE", $"RotateAutosaves failed: {ex.Message}");
            }
        }

        /// <summary>
        /// v0.57.18: keep only the 3 most recent emergency saves per character.
        /// Without this, every Ctrl+C accumulates a new emergency_*.json file
        /// forever (the v0.57.18 per-character + timestamp pattern is unique on
        /// every dump). Called from the emergency save site in Program.cs.
        /// Public so the Ctrl+C handler can call it after a successful save.
        /// </summary>
        public void RotateEmergencySaves(string playerName)
        {
            try
            {
                var sanitized = string.Join("_", playerName.Split(Path.GetInvalidFileNameChars()));
                var pattern = $"emergency_{sanitized}_*.json";
                var files = Directory.GetFiles(SaveDirectory, pattern);

                var sorted = files
                    .Select(f => new FileInfo(f))
                    .OrderByDescending(f => f.LastWriteTime)
                    .ToList();

                for (int i = 3; i < sorted.Count; i++)
                {
                    try { sorted[i].Delete(); } catch { /* one deletion failure shouldn't block others */ }
                }
            }
            catch (Exception ex)
            {
                DebugLogger.Instance.LogWarning("SAVE", $"RotateEmergencySaves failed for '{playerName}': {ex.Message}");
            }
        }

        private string GetUserDataPath()
        {
            var appName = "UsurperReloaded";

            if (System.Environment.OSVersion.Platform == PlatformID.Win32NT)
            {
                return Path.Combine(System.Environment.GetFolderPath(System.Environment.SpecialFolder.ApplicationData), appName);
            }
            else if (System.Environment.OSVersion.Platform == PlatformID.Unix)
            {
                var home = System.Environment.GetEnvironmentVariable("HOME");
                return Path.Combine(home ?? "/tmp", ".local", "share", appName);
            }
            else
            {
                var home = System.Environment.GetEnvironmentVariable("HOME");
                return Path.Combine(home ?? "/tmp", "Library", "Application Support", appName);
            }
        }
    }
}
