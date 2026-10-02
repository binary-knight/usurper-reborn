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
        /// Modder-added custom equipment (v0.57.3). Loaded from equipment.json.
        /// Appended to EquipmentDatabase at startup AFTER the built-in items, so
        /// custom entries can reference any built-in concept but never collide
        /// with built-in IDs. Modders must assign IDs in the 200000+ range.
        /// </summary>
        public static List<Equipment>? CustomEquipment { get; private set; }

        /// <summary>v1.2 (design item H2): scalar tuning of built-in abilities and spells,
        /// replace-by-key, applied at startup. A file with any invalid entry is rejected whole.</summary>
        public static List<AbilityOverride>? AbilityOverrides { get; private set; }
        public static List<SpellOverride>? SpellOverrides { get; private set; }

        /// <summary>ID range reserved for modder-added equipment. Below this = game-managed.</summary>
        public const int ModdedEquipmentIdStart = 200000;

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
            CustomEquipment = TryLoadFile<List<Equipment>>("equipment.json");

            if (Balance != null)
            {
                Balance.ApplyToGameConfig();
                DebugLogger.Instance.LogInfo("GAMEDATA", "Balance config applied to GameConfig");
            }

            // v1.2 (design item H2): abilities.json and spells.json, validated then applied
            AbilityOverrides = LoadValidated("abilities.json", TryLoadFile<List<AbilityOverride>>("abilities.json"),
                list => OverrideValidation.ValidateAbilities(list, ClassAbilitySystem.HasAbility));
            if (AbilityOverrides != null)
                DebugLogger.Instance.LogInfo("GAMEDATA", $"abilities.json: {ClassAbilitySystem.ApplyOverrides(AbilityOverrides)} abilities tuned");
            SpellOverrides = LoadValidated("spells.json", TryLoadFile<List<SpellOverride>>("spells.json"),
                list => OverrideValidation.ValidateSpells(list, SpellSystem.HasSpell));
            if (SpellOverrides != null)
                DebugLogger.Instance.LogInfo("GAMEDATA", $"spells.json: {SpellSystem.ApplyOverrides(SpellOverrides)} spells tuned");

            int loaded = new object?[] { NPCs, MonsterFamilies, Dreams, Achievements, DialogueLines, Balance, CustomEquipment, AbilityOverrides, SpellOverrides }
                .Count(x => x != null);
            DebugLogger.Instance.LogInfo("GAMEDATA", $"Loaded {loaded}/9 moddable data files");
            } // lock
        }

        /// <summary>
        /// Export all built-in default data to JSON files for modders.
        /// </summary>
        public static void ExportDefaults(string outputDir)
        {
            Directory.CreateDirectory(outputDir);

            foreach (var (fileName, build) in DefaultExports)
                ExportFile(outputDir, fileName, build());

            DebugLogger.Instance.LogInfo("GAMEDATA", $"Exported {DefaultExports.Count} default data files to: {outputDir}");
        }

        /// <summary>
        /// v1.2.4: the files ExportDefaults writes, in order. The editor's prompt counts this list, so the
        /// number it shows is always the number written.
        /// </summary>
        public static readonly IReadOnlyList<(string FileName, Func<object> Build)> DefaultExports = new List<(string, Func<object>)>
        {
            ("npcs.json", () => ClassicNPCs.GetBuiltInNPCs()),
            ("monster_families.json", () => global::MonsterFamilies.GetBuiltInFamilies()),
            ("dreams.json", () => DreamSystem.GetBuiltInDreams()),
            ("achievements.json", () => AchievementSystem.GetBuiltInAchievements()),
            ("dialogue.json", () => NPCDialogueDatabase.GetAllBuiltInLines()),
            ("balance.json", () => new BalanceConfig()),

            // Equipment is additive-only: export a small example rather than all
            // ~700 built-in items. Modders add NEW items at ID 200000+; built-ins
            // stay read-only for save compatibility.
            ("equipment.json", () => GetExampleCustomEquipment()),

            // v1.2 (design item H2): every built-in ability and spell with its current numbers,
            // as a template: delete what you do not change, edit what you do.
            ("abilities.json", () => ClassAbilitySystem.ExportOverrideTemplate()),
            ("spells.json", () => SpellSystem.ExportOverrideTemplate()),
        };

        /// <summary>A loaded override file is kept only when its validator finds nothing wrong.</summary>
        private static List<T>? LoadValidated<T>(string fileName, List<T>? loaded, Func<List<T>, List<string>> validate) where T : class
        {
            if (loaded == null) return null;
            var errors = validate(loaded);
            if (errors.Count == 0) return loaded;
            DebugLogger.Instance.LogError("GAMEDATA", $"{fileName} rejected, built-in values kept: {errors.Count} problem(s)");
            foreach (var e in errors.Take(20)) DebugLogger.Instance.LogError("GAMEDATA", $"  {e}");
            return null;
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

        /// <summary>
        /// Example custom equipment set written on --export-data. Shows modders
        /// the shape of the JSON and how to assign IDs in the 200000+ range.
        /// Not loaded by the game unless the modder renames/edits this file.
        /// </summary>
        private static List<Equipment> GetExampleCustomEquipment()
        {
            return new List<Equipment>
            {
                new Equipment
                {
                    Id = ModdedEquipmentIdStart + 1,
                    Name = "Modder's Test Sword",
                    Description = "An example custom weapon. Edit equipment.json to change.",
                    Slot = EquipmentSlot.MainHand,
                    Handedness = WeaponHandedness.OneHanded,
                    WeaponType = WeaponType.Sword,
                    WeaponPower = 50,
                    StrengthBonus = 5,
                    Value = 10000,
                    MinLevel = 10,
                    Rarity = EquipmentRarity.Rare,
                },
                new Equipment
                {
                    Id = ModdedEquipmentIdStart + 2,
                    Name = "Modder's Test Armor",
                    Description = "An example custom chest armor.",
                    Slot = EquipmentSlot.Body,
                    ArmorType = ArmorType.Chain,
                    WeightClass = ArmorWeightClass.Medium,
                    ArmorClass = 40,
                    ConstitutionBonus = 4,
                    Value = 8000,
                    MinLevel = 10,
                    Rarity = EquipmentRarity.Rare,
                },
            };
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
