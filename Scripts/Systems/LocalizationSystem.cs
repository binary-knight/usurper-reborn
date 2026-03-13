using System;
using System.Collections.Generic;
using System.IO;
using System.Text.Json;

namespace UsurperRemake.Systems
{
    /// <summary>
    /// Localization system for multi-language support.
    /// Uses flat key-value JSON files (one per language).
    /// Falls back to English if a key is missing, then to the raw key itself.
    /// Thread-safe for MUD mode via GameConfig.Language (AsyncLocal per-session).
    ///
    /// Usage:
    ///   Loc.Get("combat.miss", attackerName)     // "The Goblin misses!"
    ///   Loc.Get("menu.dungeon")                  // "The Dungeon"
    /// </summary>
    public static class Loc
    {
        private static readonly Dictionary<string, Dictionary<string, string>> _languages = new();
        private static readonly Dictionary<string, string> _empty = new();
        private static bool _loaded = false;
        private static (string Code, string Name)[] _availableLanguages = Array.Empty<(string, string)>();

        /// <summary>
        /// Known language code → display name mapping.
        /// Add entries here when new translations are created.
        /// Only languages with a corresponding .json file will appear in-game.
        /// </summary>
        private static readonly Dictionary<string, string> KnownLanguageNames = new(StringComparer.OrdinalIgnoreCase)
        {
            { "en", "English" },
            { "es", "Español (AI Translated)" },
            { "fr", "Français" },
            { "de", "Deutsch" },
            { "pt", "Português" },
            { "it", "Italiano (AI Translated)" },
            { "nl", "Nederlands" },
            { "pl", "Polski" },
            { "ru", "Русский" },
            { "ja", "日本語" },
            { "ko", "한국어" },
            { "zh", "中文" },
            { "sv", "Svenska" },
            { "da", "Dansk" },
            { "no", "Norsk" },
            { "fi", "Suomi" },
            { "hu", "Magyar (AI Translated)" },
        };

        /// <summary>
        /// Available languages auto-detected from loaded .json files.
        /// English is always first; others sorted alphabetically by display name.
        /// </summary>
        public static (string Code, string Name)[] AvailableLanguages => _availableLanguages;

        /// <summary>
        /// Load all language files from the Localization directory.
        /// Called once at startup. Safe to call multiple times (idempotent).
        /// </summary>
        public static void Initialize()
        {
            if (_loaded) return;

            // Look for Localization directory relative to the executable
            var exeDir = AppDomain.CurrentDomain.BaseDirectory;
            var searchPaths = new[]
            {
                Path.Combine(exeDir, "Localization"),
                Path.Combine(exeDir, "..", "Localization"),
                Path.Combine(Directory.GetCurrentDirectory(), "Localization"),
            };

            string? locDir = null;
            foreach (var path in searchPaths)
            {
                if (Directory.Exists(path))
                {
                    locDir = path;
                    break;
                }
            }

            if (locDir == null)
            {
                DebugLogger.Instance?.LogWarning("LOC", "Localization directory not found, using built-in English fallback");
                _languages["en"] = GetBuiltInEnglish();
                _availableLanguages = new[] { ("en", "English") };
                _loaded = true;
                return;
            }

            foreach (var file in Directory.GetFiles(locDir, "*.json"))
            {
                try
                {
                    var langCode = Path.GetFileNameWithoutExtension(file).ToLowerInvariant();
                    var json = File.ReadAllText(file);
                    var dict = JsonSerializer.Deserialize<Dictionary<string, string>>(json);
                    if (dict != null)
                    {
                        // Remove comment keys (convention: _comment, _note, etc.)
                        foreach (var key in dict.Keys.Where(k => k.StartsWith("_")).ToList())
                            dict.Remove(key);
                        _languages[langCode] = dict;
                        DebugLogger.Instance?.LogInfo("LOC", $"Loaded {dict.Count} strings for '{langCode}'");
                    }
                }
                catch (Exception ex)
                {
                    DebugLogger.Instance?.LogError("LOC", $"Failed to load {file}: {ex.Message}");
                }
            }

            // Build available languages list from what was actually loaded
            var langList = new List<(string Code, string Name)>();
            foreach (var code in _languages.Keys.OrderBy(k => k))
            {
                string name = KnownLanguageNames.TryGetValue(code, out var n) ? n : code.ToUpperInvariant();
                langList.Add((code, name));
            }
            // English first, then alphabetical by display name
            langList.Sort((a, b) =>
            {
                if (a.Code == "en") return -1;
                if (b.Code == "en") return 1;
                return string.Compare(a.Name, b.Name, StringComparison.Ordinal);
            });
            _availableLanguages = langList.ToArray();

            _loaded = true;
        }

        /// <summary>
        /// Get a localized string by key. Uses the current session's language.
        /// Falls back: current language → English → raw key.
        /// </summary>
        public static string Get(string key)
        {
            if (!_loaded) Initialize();

            var lang = GameConfig.Language;

            // Try current language
            if (lang != "en" && _languages.TryGetValue(lang, out var langDict) && langDict.TryGetValue(key, out var localized))
                return localized;

            // Fall back to English
            if (_languages.TryGetValue("en", out var enDict) && enDict.TryGetValue(key, out var english))
                return english;

            // Key not found in any language — return the key itself
            return key;
        }

        /// <summary>
        /// Get a localized format string and apply arguments.
        /// Example: Loc.Get("combat.damage", "Goblin", 42) → "The Goblin deals 42 damage!"
        /// </summary>
        public static string Get(string key, params object[] args)
        {
            var template = Get(key);
            try
            {
                return string.Format(template, args);
            }
            catch (FormatException)
            {
                // If format string is malformed, return template with args appended
                return template;
            }
        }

        /// <summary>
        /// Check if a key exists in the current language (or English fallback).
        /// </summary>
        public static bool Has(string key)
        {
            if (!_loaded) Initialize();
            var lang = GameConfig.Language;

            if (lang != "en" && _languages.TryGetValue(lang, out var langDict) && langDict.ContainsKey(key))
                return true;

            return _languages.TryGetValue("en", out var enDict) && enDict.ContainsKey(key);
        }

        /// <summary>
        /// Get the display name for a language code.
        /// </summary>
        public static string GetLanguageName(string code)
        {
            if (KnownLanguageNames.TryGetValue(code, out var name))
                return name;
            return code;
        }

        /// <summary>
        /// Get the next language code in the cycle (for toggle-style selection).
        /// </summary>
        public static string GetNextLanguage(string current)
        {
            for (int i = 0; i < AvailableLanguages.Length; i++)
            {
                if (AvailableLanguages[i].Code.Equals(current, StringComparison.OrdinalIgnoreCase))
                    return AvailableLanguages[i + 1 < AvailableLanguages.Length ? i + 1 : 0].Code;
            }
            return "en";
        }

        /// <summary>
        /// Get all loaded language codes.
        /// </summary>
        public static IReadOnlyList<string> LoadedLanguages => new List<string>(_languages.Keys);

        /// <summary>
        /// Built-in English fallback for the most critical UI keys.
        /// Used when the Localization directory is missing (e.g., BBS sysop deployment
        /// that only copies exe+dll). Covers prompts, status bar, combat actions,
        /// and common labels that appear on every screen.
        /// </summary>
        private static Dictionary<string, string> GetBuiltInEnglish()
        {
            return new Dictionary<string, string>
            {
                // Core UI prompts
                { "ui.your_choice", "Your choice: " },
                { "ui.press_any_key", "Press any key to continue..." },
                { "ui.press_enter", "[Press Enter]" },
                { "ui.return", "Return" },
                { "ui.none", "None" },
                { "ui.yes", "Yes" },
                { "ui.no", "No" },
                { "ui.back", "Back" },
                { "ui.cancel", "Cancel" },

                // Status bar
                { "status.hp", "HP" },
                { "status.gold", "Gold" },
                { "status.mana", "Mana" },
                { "status.stamina", "Stamina" },
                { "status.level", "Level" },
                { "status.mp", "MP" },
                { "status.sta", "STA" },

                // Combat actions
                { "combat.action_attack", "Attack" },
                { "combat.action_defend", "Defend" },
                { "combat.action_power", "Power Attack" },
                { "combat.action_precise", "Precise Strike" },
                { "combat.action_disarm", "Disarm" },
                { "combat.action_taunt", "Taunt" },
                { "combat.action_hide", "Hide" },
                { "combat.action_flee", "Flee" },
                { "combat.action_cast", "Cast Spell" },
                { "combat.action_use", "Use Item" },
                { "combat.action_ability", "Ability" },
                { "combat.action_herb", "Use Herb" },
                { "combat.action_aid", "Aid Ally" },

                // Equipment slots
                { "ui.main_hand", "Main Hand" },
                { "ui.off_hand", "Off Hand" },
                { "ui.head", "Head" },
                { "ui.body", "Body" },
                { "ui.arms", "Arms" },
                { "ui.hands", "Hands" },
                { "ui.legs", "Legs" },
                { "ui.feet", "Feet" },
                { "ui.waist", "Waist" },
                { "ui.cloak", "Cloak" },
                { "ui.neck", "Neck" },
                { "ui.neck_2", "Neck 2" },
                { "ui.face", "Face" },
                { "ui.left_ring", "Left Ring" },
                { "ui.right_ring", "Right Ring" },

                // Save/load
                { "save.saving", "Saving game..." },
                { "save.saved", "Game saved!" },
                { "save.loading", "Loading game..." },

                // Common location labels
                { "dungeon.status", "Status" },
                { "dungeon.inventory", "Inventory" },
            };
        }
    }
}
