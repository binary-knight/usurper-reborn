using System;
using System.IO;
using System.Text.Json;
using UsurperRemake.BBS;

namespace UsurperRemake.Systems
{
    /// <summary>
    /// Manages persistent SysOp configuration settings for BBS door mode.
    /// Settings are saved per-BBS in the save directory.
    /// </summary>
    public class SysOpConfigSystem
    {
        private static SysOpConfigSystem? _instance;
        public static SysOpConfigSystem Instance => _instance ??= new SysOpConfigSystem();

        private const string ConfigFileName = "sysop_config.json";
        private SysOpConfig _config = new();
        private bool _isLoaded = false;

        /// <summary>
        /// Load SysOp configuration from file. Called on game startup.
        /// </summary>
        public void LoadConfig()
        {
            if (_isLoaded) return;

            try
            {
                var configPath = GetConfigPath();
                if (File.Exists(configPath))
                {
                    var json = File.ReadAllText(configPath);
                    var loaded = JsonSerializer.Deserialize<SysOpConfig>(json);
                    if (loaded != null)
                    {
                        _config = loaded;
                        ApplyConfigToGameConfig();
                        DebugLogger.Instance.LogInfo("SYSOP_CONFIG", $"Loaded config from {configPath}");
                    }
                }
            }
            catch (Exception ex)
            {
                DebugLogger.Instance.LogError("SYSOP_CONFIG", $"Failed to load config: {ex.Message}");
            }

            _isLoaded = true;
        }

        /// <summary>
        /// Save current configuration to file.
        /// </summary>
        public void SaveConfig()
        {
            try
            {
                // Sync from GameConfig before saving
                SyncFromGameConfig();

                var configPath = GetConfigPath();
                var directory = Path.GetDirectoryName(configPath);
                if (!string.IsNullOrEmpty(directory))
                {
                    Directory.CreateDirectory(directory);
                }

                var options = new JsonSerializerOptions { WriteIndented = true };
                var json = JsonSerializer.Serialize(_config, options);
                File.WriteAllText(configPath, json);

                DebugLogger.Instance.LogInfo("SYSOP_CONFIG", $"Saved config to {configPath}");
            }
            catch (Exception ex)
            {
                DebugLogger.Instance.LogError("SYSOP_CONFIG", $"Failed to save config: {ex.Message}");
            }
        }

        /// <summary>
        /// Apply loaded config values to GameConfig static properties
        /// Validates values and clamps them to safe ranges
        /// </summary>
        private void ApplyConfigToGameConfig()
        {
            // Validate and apply each setting with safe bounds
            GameConfig.MessageOfTheDay = _config.MessageOfTheDay ?? "";

            // Turns: 1-9999 (same as UI validation)
            GameConfig.DefaultDailyTurns = Math.Clamp(_config.DefaultDailyTurns, 1, 9999);

            // Multipliers: 0.1-10.0 (same as UI validation)
            GameConfig.XPMultiplier = Math.Clamp(_config.XPMultiplier, 0.1f, 10.0f);
            GameConfig.GoldMultiplier = Math.Clamp(_config.GoldMultiplier, 0.1f, 10.0f);
            GameConfig.MonsterHPMultiplier = Math.Clamp(_config.MonsterHPMultiplier, 0.1f, 10.0f);
            GameConfig.MonsterDamageMultiplier = Math.Clamp(_config.MonsterDamageMultiplier, 0.1f, 10.0f);

            // MaxDungeonLevel: 1-100 (can't exceed story content)
            GameConfig.MaxDungeonLevel = Math.Clamp(_config.MaxDungeonLevel, 1, 100);

            // Log if any values were clamped
            bool wasClamped = false;
            if (_config.DefaultDailyTurns < 1 || _config.DefaultDailyTurns > 9999) wasClamped = true;
            if (_config.XPMultiplier < 0.1f || _config.XPMultiplier > 10.0f) wasClamped = true;
            if (_config.GoldMultiplier < 0.1f || _config.GoldMultiplier > 10.0f) wasClamped = true;
            if (_config.MonsterHPMultiplier < 0.1f || _config.MonsterHPMultiplier > 10.0f) wasClamped = true;
            if (_config.MonsterDamageMultiplier < 0.1f || _config.MonsterDamageMultiplier > 10.0f) wasClamped = true;
            if (_config.MaxDungeonLevel < 1 || _config.MaxDungeonLevel > 100) wasClamped = true;

            if (wasClamped)
            {
                DebugLogger.Instance.LogWarning("SYSOP_CONFIG", "Some config values were outside valid ranges and have been clamped");
            }
        }

        /// <summary>
        /// Sync current GameConfig values to internal config before saving
        /// </summary>
        private void SyncFromGameConfig()
        {
            _config.MessageOfTheDay = GameConfig.MessageOfTheDay;
            _config.DefaultDailyTurns = GameConfig.DefaultDailyTurns;
            _config.XPMultiplier = GameConfig.XPMultiplier;
            _config.GoldMultiplier = GameConfig.GoldMultiplier;
            _config.MonsterHPMultiplier = GameConfig.MonsterHPMultiplier;
            _config.MonsterDamageMultiplier = GameConfig.MonsterDamageMultiplier;
            _config.MaxDungeonLevel = GameConfig.MaxDungeonLevel;
        }

        /// <summary>
        /// Get the path to the config file (in the BBS-specific save directory)
        /// </summary>
        private string GetConfigPath()
        {
            var saveDir = SaveSystem.Instance.GetSaveDirectory();
            return Path.Combine(saveDir, ConfigFileName);
        }

        /// <summary>
        /// Reset config to defaults
        /// </summary>
        public void ResetToDefaults()
        {
            _config = new SysOpConfig();
            ApplyConfigToGameConfig();
            SaveConfig();
        }
    }

    /// <summary>
    /// Serializable configuration data for SysOp settings
    /// </summary>
    public class SysOpConfig
    {
        public string MessageOfTheDay { get; set; } = "";
        public int DefaultDailyTurns { get; set; } = 325;
        public float XPMultiplier { get; set; } = 1.0f;
        public float GoldMultiplier { get; set; } = 1.0f;
        public float MonsterHPMultiplier { get; set; } = 1.0f;
        public float MonsterDamageMultiplier { get; set; } = 1.0f;
        public int MaxDungeonLevel { get; set; } = 100;

        // Metadata
        public DateTime LastModified { get; set; } = DateTime.UtcNow;
        public string LastModifiedBy { get; set; } = "";
    }
}
