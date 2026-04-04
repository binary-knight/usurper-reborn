using System;
using System.Collections.Generic;
using System.IO;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Linq;
using UsurperRemake.Utils;
using UsurperRemake.Data;

namespace UsurperRemake.Systems
{
    /// <summary>
    /// Loads moddable game data from external JSON files in the GameData/ directory.
    /// If no JSON files exist, built-in C# defaults are used (zero behavior change).
    /// Follows the same file discovery pattern as LocalizationSystem.
    /// </summary>
    public static class GameDataLoader
    {
        private static readonly object _lock = new();
        private static bool _initialized;
        private static string? _gameDataDirectory;

        // Cached loaded data (null = use built-in defaults)
        public static List<NPCTemplate>? NPCs { get; private set; }
        public static List<MonsterFamilies.MonsterFamily>? MonsterFamilies { get; private set; }
        public static List<NarrativeDreamData>? Dreams { get; private set; }
        public static List<Achievement>? Achievements { get; private set; }
        public static List<NPCDialogueDatabase.DialogueLine>? DialogueLines { get; private set; }
        public static BalanceConfig? Balance { get; private set; }

        /// <summary>
        /// Shared JSON options with tolerant enum handling and camelCase naming.
        /// </summary>
        public static readonly JsonSerializerOptions JsonOptions = new()
        {
            PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
            PropertyNameCaseInsensitive = true,
            WriteIndented = true,
            DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
            Converters = { new TolerantEnumConverterFactory() }
        };

        /// <summary>
        /// Initialize the data loader. Safe to call multiple times (idempotent).
        /// </summary>
        public static void Initialize()
        {
            lock (_lock)
            {
            if (_initialized) return;
            _initialized = true;

            _gameDataDirectory = FindGameDataDirectory();
            if (_gameDataDirectory == null)
            {
                DebugLogger.Instance.LogInfo("GAMEDATA", "No GameData/ directory found — using built-in defaults");
                return;
            }

            DebugLogger.Instance.LogInfo("GAMEDATA", $"Loading moddable data from: {_gameDataDirectory}");

            NPCs = TryLoadFile<List<NPCTemplate>>("npcs.json");
            MonsterFamilies = TryLoadFile<List<global::MonsterFamilies.MonsterFamily>>("monster_families.json");
            Dreams = TryLoadFile<List<NarrativeDreamData>>("dreams.json");
            Achievements = TryLoadFile<List<Achievement>>("achievements.json");
            DialogueLines = TryLoadFile<List<NPCDialogueDatabase.DialogueLine>>("dialogue.json");
            Balance = TryLoadFile<BalanceConfig>("balance.json");

            if (Balance != null)
            {
                Balance.ApplyToGameConfig();
                DebugLogger.Instance.LogInfo("GAMEDATA", "Balance config applied to GameConfig");
            }

            int loaded = new object?[] { NPCs, MonsterFamilies, Dreams, Achievements, DialogueLines, Balance }
                .Count(x => x != null);
            DebugLogger.Instance.LogInfo("GAMEDATA", $"Loaded {loaded}/6 moddable data files");
            } // lock
        }

        /// <summary>
        /// Export all built-in default data to JSON files for modders.
        /// </summary>
        public static void ExportDefaults(string outputDir)
        {
            Directory.CreateDirectory(outputDir);

            ExportFile(outputDir, "npcs.json", ClassicNPCs.GetBuiltInNPCs());
            ExportFile(outputDir, "monster_families.json", global::MonsterFamilies.GetBuiltInFamilies());
            ExportFile(outputDir, "dreams.json", DreamSystem.GetBuiltInDreams());
            ExportFile(outputDir, "achievements.json", AchievementSystem.GetBuiltInAchievements());
            ExportFile(outputDir, "dialogue.json", NPCDialogueDatabase.GetAllBuiltInLines());
            ExportFile(outputDir, "balance.json", new BalanceConfig());

            DebugLogger.Instance.LogInfo("GAMEDATA", $"Exported 6 default data files to: {outputDir}");
        }

        private static string? FindGameDataDirectory()
        {
            var exeDir = AppDomain.CurrentDomain.BaseDirectory;
            var searchPaths = new[]
            {
                Path.Combine(exeDir, "GameData"),
                Path.Combine(exeDir, "..", "GameData"),
                Path.Combine(Directory.GetCurrentDirectory(), "GameData"),
            };
            return searchPaths.FirstOrDefault(Directory.Exists);
        }

        private static T? TryLoadFile<T>(string fileName) where T : class
        {
            if (_gameDataDirectory == null) return null;

            var filePath = Path.Combine(_gameDataDirectory, fileName);
            if (!File.Exists(filePath)) return null;

            try
            {
                var json = File.ReadAllText(filePath);
                var result = JsonSerializer.Deserialize<T>(json, JsonOptions);

                if (result == null)
                {
                    DebugLogger.Instance.LogWarning("GAMEDATA", $"{fileName}: deserialized to null, using defaults");
                    return null;
                }

                DebugLogger.Instance.LogInfo("GAMEDATA", $"Loaded {fileName}");
                return result;
            }
            catch (Exception ex)
            {
                DebugLogger.Instance.LogError("GAMEDATA", $"Failed to load {fileName}: {ex.Message} — using defaults");
                return null;
            }
        }

        private static void ExportFile<T>(string outputDir, string fileName, T data)
        {
            try
            {
                var json = JsonSerializer.Serialize(data, JsonOptions);
                File.WriteAllText(Path.Combine(outputDir, fileName), json);
            }
            catch (Exception ex)
            {
                DebugLogger.Instance.LogError("GAMEDATA", $"Failed to export {fileName}: {ex.Message}");
            }
        }
    }
}
