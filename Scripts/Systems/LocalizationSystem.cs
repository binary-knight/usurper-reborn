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
        private static readonly object _initLock = new object();
        private static volatile bool _loaded = false;
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
            { "fr", "Français (AI Translated)" },
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
            lock (_initLock)
            {
                if (_loaded) return;
                InitializeLocked();
                _loaded = true;
            }
        }

        private static void InitializeLocked()
        {
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
        }

        /// <summary>
        /// Get a localized string by key. Uses the current session's language.
        /// Falls back: current language → English → raw key.
        /// </summary>
        public static string Get(string key)
        {
            string text = Resolve(key);
            _recording.Value?.Add(text, key, Array.Empty<object>());
            return text;
        }

        private static string Resolve(string key)
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
            var template = Resolve(key);
            string text;
            try
            {
                text = string.Format(template, args);
            }
            catch (FormatException)
            {
                // If format string is malformed, return template with args appended
                text = template;
            }
            _recording.Value?.Add(text, key, args);
            return text;
        }

        private static readonly System.Threading.AsyncLocal<LocRecording?> _recording = new();

        /// <summary>
        /// v1.2.2: starts recording the Get calls of this session's flow, so text already written in the
        /// session's language can be re-rendered in another one (LocRecording.Render). Used for captured
        /// combat output that is sent to group members who read another language. Stop with EndRecording.
        /// </summary>
        public static LocRecording BeginRecording()
        {
            var rec = new LocRecording(GameConfig.Language) { Previous = _recording.Value };
            _recording.Value = rec;
            return rec;
        }

        /// <summary>v1.2.4: ends the innermost recording; one begun around it records again.</summary>
        public static void EndRecording() => _recording.Value = _recording.Value?.Previous;

        /// <summary>v1.2.4: ends `rec` and any recording begun inside it and left open.</summary>
        public static void EndRecording(LocRecording rec) => _recording.Value = rec.Previous;

        private static readonly System.Threading.AsyncLocal<string?> _renderLanguage = new();

        /// <summary>v1.2.4: the language of an open RenderLanguage scope, or null.</summary>
        public static string? RenderLanguageOverride => _renderLanguage.Value;

        /// <summary>
        /// v1.2.4: until the returned scope is disposed, Loc.Get and GameConfig.Language in this flow use
        /// `lang`, for text drawn on another player's terminal (a group follower's turn runs on the
        /// leader's session). The session's own Language is not written; GameConfig.Language's setter
        /// does nothing while a scope is open. Scopes nest; null opens a scope with no override, for
        /// text that goes into shared state. Enter and dispose it in the same method (a using block).
        /// </summary>
        public static IDisposable RenderLanguage(string? lang)
        {
            var scope = new RenderScope(_renderLanguage.Value);
            _renderLanguage.Value = string.IsNullOrEmpty(lang) ? null : lang;
            return scope;
        }

        /// <summary>v1.2.4: a scope in the session's own language, for text stored in shared or saved state.</summary>
        public static IDisposable SessionLanguage() => RenderLanguage(null);

        private sealed class RenderScope : IDisposable
        {
            private readonly string? _previous;
            private bool _disposed;
            public RenderScope(string? previous) { _previous = previous; }
            public void Dispose()
            {
                if (_disposed) return;
                _disposed = true;
                _renderLanguage.Value = _previous;
            }
        }

        /// <summary>
        /// Get a localized string in an explicitly specified language, independent of the current
        /// session's language. For real-time server broadcasts that are built once but delivered to
        /// many sessions in different languages (e.g. world boss spawn), so each recipient sees the
        /// announcement in their own language. Falls back: given language → English → raw key.
        /// </summary>
        public static string GetIn(string lang, string key)
        {
            if (!_loaded) Initialize();

            if (!string.IsNullOrEmpty(lang) && lang != "en"
                && _languages.TryGetValue(lang, out var langDict) && langDict.TryGetValue(key, out var localized))
                return localized;

            if (_languages.TryGetValue("en", out var enDict) && enDict.TryGetValue(key, out var english))
                return english;

            return key;
        }

        /// <summary>Whether a key is present in this language, without English fallback.</summary>
        public static bool HasIn(string lang, string key)
        {
            if (!_loaded) Initialize();
            return _languages.TryGetValue(lang, out var entries) && entries.ContainsKey(key);
        }

        /// <summary>Get a localized format string in an explicit language and apply arguments.</summary>
        public static string GetIn(string lang, string key, params object[] args)
        {
            var template = GetIn(lang, key);
            try
            {
                return string.Format(template, args);
            }
            catch (FormatException)
            {
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

    /// <summary>
    /// v1.2.2: the Loc.Get calls made while a recording was active (Loc.BeginRecording), in call order.
    /// Render swaps each recorded text in a captured screen for the same key rendered in another
    /// language; a string argument that is itself a recorded text is translated first, so nested
    /// lookups (a status name inside a status line) follow. Text not produced by Loc stays as written.
    /// </summary>
    public sealed class LocRecording
    {
        private const int MaxCalls = 500;   // a recording left open cannot grow without bound
        private readonly List<(string text, string key, object[] args)> _calls = new();
        private readonly object _lock = new();

        public LocRecording(string language) { Language = language; }

        /// <summary>v1.2.4: the recording that was open when this one began; Add records there too.</summary>
        internal LocRecording? Previous { get; init; }

        /// <summary>The language the recorded text was written in.</summary>
        public string Language { get; }

        internal void Add(string text, string key, object[] args)
        {
            lock (_lock)
            {
                if (_calls.Count < MaxCalls) _calls.Add((text, key, args));
            }
            Previous?.Add(text, key, args);
        }

        /// <summary>`captured` with each recorded text replaced by its rendering in `lang`.</summary>
        public string Render(string captured, string lang)
        {
            if (string.IsNullOrEmpty(captured) || lang == Language) return captured;
            List<(string text, string key, object[] args)> calls;
            lock (_lock) calls = new List<(string, string, object[])>(_calls);

            var map = new Dictionary<string, string>(StringComparer.Ordinal);
            foreach (var (text, key, args) in calls)
            {
                if (string.IsNullOrWhiteSpace(text) || map.ContainsKey(text)) continue;
                var translatedArgs = new object[args.Length];
                for (int i = 0; i < args.Length; i++)
                    translatedArgs[i] = args[i] is string s && map.TryGetValue(s, out var t) ? t : args[i];
                string rendered = args.Length == 0 ? Loc.GetIn(lang, key) : Loc.GetIn(lang, key, translatedArgs);
                if (rendered != text) map[text] = rendered;
            }
            if (map.Count == 0) return captured;

            // one pass, longest text first, so a replacement is never replaced again
            var keys = new List<string>(map.Keys);
            keys.Sort((a, b) => b.Length.CompareTo(a.Length));
            var pattern = string.Join("|", keys.ConvertAll(System.Text.RegularExpressions.Regex.Escape));
            return System.Text.RegularExpressions.Regex.Replace(captured, pattern, m => map[m.Value]);
        }
    }
}
