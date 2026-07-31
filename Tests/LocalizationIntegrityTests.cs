using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Text.RegularExpressions;
using Xunit;

namespace UsurperRemake.Tests
{
    /// <summary>
    /// v0.65.12 (loc audit): the localization CI gate. Every historical loc
    /// regression class -- missing keys, format-arg drift (raw-template
    /// crashes), empty translations, banned-punctuation growth -- becomes a
    /// build failure here instead of a player report.
    /// </summary>
    public class LocalizationIntegrityTests
    {
        private static readonly string[] TargetLangs = { "es", "fr", "it", "hu" };

        // Intentionally-empty translations: pro-drop pronouns (v0.61.5 design)
        // and no-plural-after-numeral suffixes. Add here ONLY with a reason.
        private static readonly HashSet<string> IntentionalEmpty = new()
        {
            "ui.pronoun_possessive_male", "ui.pronoun_possessive_female",
            "ui.pronoun_subject_male", "ui.pronoun_subject_female",
            "main_street.player_plural", "love_corner.child_count_ren",
            "home.children_ren", "ending.legacy_children_plural",
            "engine.story_begins_4",
        };

        // Banned-punctuation debt ceilings (em-dash + en-dash + ellipsis chars,
        // counted as OCCURRENCES per file) as measured at the v0.65.12 audit.
        // New keys must use ASCII punctuation ("--", "...", "-"); these counts
        // may only go DOWN (lower a ceiling after any cleanup pass).
        private static readonly Dictionary<string, int> PunctCeiling = new()
        {
            { "en", 223 }, { "es", 290 }, { "fr", 467 }, { "it", 356 }, { "hu", 408 },
        };

        private static string LocDir()
        {
            // Walk up from the test bin dir to find Localization/.
            var dir = new DirectoryInfo(AppContext.BaseDirectory);
            while (dir != null)
            {
                string candidate = Path.Combine(dir.FullName, "Localization");
                if (File.Exists(Path.Combine(candidate, "en.json"))) return candidate;
                dir = dir.Parent;
            }
            throw new InvalidOperationException("Localization directory not found above " + AppContext.BaseDirectory);
        }

        private static Dictionary<string, string> Load(string lang)
        {
            string path = Path.Combine(LocDir(), lang + ".json");
            var doc = JsonSerializer.Deserialize<Dictionary<string, JsonElement>>(File.ReadAllText(path))!;
            return doc.Where(kv => kv.Value.ValueKind == JsonValueKind.String)
                      .ToDictionary(kv => kv.Key, kv => kv.Value.GetString() ?? "");
        }

        private static bool IsRealKey(string k) =>
            !k.StartsWith("//") && !k.StartsWith("_") && !k.Contains("comment");

        private static HashSet<string> Args(string s) =>
            Regex.Matches(s, @"\{\d+\}").Select(m => m.Value).ToHashSet();

        [Fact]
        public void AllLanguages_HaveEveryEnglishKey()
        {
            var en = Load("en");
            var enKeys = en.Keys.Where(IsRealKey).ToHashSet();
            foreach (var lang in TargetLangs)
            {
                var d = Load(lang);
                var missing = enKeys.Where(k => !d.ContainsKey(k)).Take(20).ToList();
                Assert.True(missing.Count == 0,
                    $"{lang}.json missing {missing.Count}+ keys, e.g.: {string.Join(", ", missing.Take(8))}");
            }
        }

        [Fact]
        public void Translations_NeverReferenceArgsEnglishLacks()
        {
            // The crash direction: a translation referencing {N} the caller
            // never passes throws FormatException at runtime -> raw template.
            var en = Load("en");
            foreach (var lang in TargetLangs)
            {
                var d = Load(lang);
                var bad = new List<string>();
                foreach (var (k, v) in d)
                {
                    if (!IsRealKey(k) || !en.ContainsKey(k)) continue;
                    var extra = Args(v).Except(Args(en[k])).ToList();
                    if (extra.Count > 0) bad.Add($"{k} ({string.Join(",", extra)})");
                }
                Assert.True(bad.Count == 0,
                    $"{lang}.json has crash-risk arg refs (translation uses args EN never passes): {string.Join("; ", bad.Take(8))}");
            }
        }

        [Fact]
        public void Translations_NotEmptyWhereEnglishHasText()
        {
            var en = Load("en");
            foreach (var lang in TargetLangs)
            {
                var d = Load(lang);
                var bad = d.Where(kv => IsRealKey(kv.Key)
                        && en.TryGetValue(kv.Key, out var ev) && ev.Trim().Length > 0
                        && kv.Value.Trim().Length == 0
                        && !IntentionalEmpty.Contains(kv.Key))
                    .Select(kv => kv.Key).Take(10).ToList();
                Assert.True(bad.Count == 0,
                    $"{lang}.json has empty translations for non-empty EN keys: {string.Join(", ", bad)} " +
                    "(if intentional, add to IntentionalEmpty with a reason)");
            }
        }

        [Fact]
        public void BannedPunctuation_DoesNotGrow()
        {
            foreach (var (lang, ceiling) in PunctCeiling)
            {
                var d = Load(lang);
                int count = d.Values.Sum(v => v.Count(c => c == '—' || c == '–' || c == '…'));
                Assert.True(count <= ceiling,
                    $"{lang}.json banned-punctuation count {count} exceeds ceiling {ceiling}. " +
                    "New keys must use ASCII punctuation: -- for dashes, ... for ellipsis.");
            }
        }
    }
}
