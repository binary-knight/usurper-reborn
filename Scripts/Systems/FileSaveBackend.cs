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
                IncludeFields = true
            };
        }

        public async Task<bool> WriteGameData(string playerName, SaveGameData data)
        {
            try
            {
                var fileName = GetSaveFileName(playerName);
                var filePath = Path.Combine(SaveDirectory, fileName);
                var json = JsonSerializer.Serialize(data, jsonOptions);
                await File.WriteAllTextAsync(filePath, json);
                return true;
            }
            catch (Exception ex)
            {
                DebugLogger.Instance.LogSystemError("SAVE", $"Failed to write game data: {ex.Message}", ex.StackTrace);
                return false;
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
            try
            {
                var filePath = Path.Combine(SaveDirectory, fileName);

                if (!File.Exists(filePath))
                    return null;

                var json = await File.ReadAllTextAsync(filePath);
                var saveData = JsonSerializer.Deserialize<SaveGameData>(json, jsonOptions);

                if (saveData == null)
                {
                    return null;
                }

                if (saveData.Version < GameConfig.MinSaveVersion)
                {
                    return null;
                }

                return saveData;
            }
            catch (Exception ex)
            {
                return null;
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
                return false;
            }
        }

        public List<SaveInfo> GetAllSaves()
        {
            var saves = new List<SaveInfo>();

            try
            {
                var files = Directory.GetFiles(SaveDirectory, "*.json");

                foreach (var file in files)
                {
                    try
                    {
                        var json = File.ReadAllText(file);
                        var saveData = JsonSerializer.Deserialize<SaveGameData>(json, jsonOptions);

                        if (saveData?.Player != null)
                        {
                            saves.Add(new SaveInfo
                            {
                                PlayerName = saveData.Player.Name2 ?? saveData.Player.Name1,
                                SaveTime = saveData.SaveTime,
                                Level = saveData.Player.Level,
                                CurrentDay = saveData.CurrentDay,
                                TurnsRemaining = saveData.Player.TurnsRemaining,
                                FileName = Path.GetFileName(file)
                            });
                        }
                    }
                    catch (Exception ex)
                    {
                    }
                }
            }
            catch (Exception ex)
            {
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
                    try
                    {
                        var json = File.ReadAllText(file);
                        var saveData = JsonSerializer.Deserialize<SaveGameData>(json, jsonOptions);

                        if (saveData?.Player != null)
                        {
                            var fileName = Path.GetFileName(file);
                            var isAutosave = fileName.Contains("_autosave_");

                            saves.Add(new SaveInfo
                            {
                                PlayerName = saveData.Player.Name2 ?? saveData.Player.Name1,
                                ClassName = saveData.Player.Class.ToString(),
                                SaveTime = saveData.SaveTime,
                                Level = saveData.Player.Level,
                                CurrentDay = saveData.CurrentDay,
                                TurnsRemaining = saveData.Player.TurnsRemaining,
                                FileName = fileName,
                                IsAutosave = isAutosave,
                                SaveType = isAutosave ? "Autosave" : "Manual Save"
                            });
                        }
                    }
                    catch (Exception ex)
                    {
                    }
                }

                saves = saves.OrderByDescending(s => s.SaveTime).ToList();
            }
            catch (Exception ex)
            {
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
                            }
                        }
                    }
                    catch
                    {
                        // Skip invalid save files
                    }
                }
            }
            catch (Exception ex)
            {
            }

            return playerNames.OrderBy(n => n).ToList();
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

                var sortedFiles = autosaveFiles
                    .Select(f => new FileInfo(f))
                    .OrderByDescending(f => f.CreationTime)
                    .ToList();

                for (int i = 5; i < sortedFiles.Count; i++)
                {
                    sortedFiles[i].Delete();
                }
            }
            catch (Exception ex)
            {
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
