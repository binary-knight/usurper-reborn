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

        // Banned punctuation (em-dash, en-dash, ellipsis characters) is cleaned out
        // of every value in every language. Use ASCII: "--" for dashes, "-" for
        // number ranges, "..." for ellipsis. The ceiling is zero.
        private static readonly Dictionary<string, int> PunctCeiling = new()
        {
            { "en", 0 }, { "es", 0 }, { "fr", 0 }, { "it", 0 }, { "hu", 0 },
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

        // v1.2 (issue E of DOCS/DESIGN_1.2_OPEN_ISSUES.md): placeholders a translation may
        // omit. IntimacySystem fills these positions with pronoun helpers (gender, genderCap,
        // their, theirCap, them, babyPronoun) that resolve to the deliberately empty
        // ui.pronoun_* keys in the four pro-drop languages, so dropping them is correct. A
        // position that carries a name is never listed. The love_street entry is the
        // English plural suffix, which Hungarian forms inside the word. Add here ONLY after
        // reading the caller and naming the argument in a comment.
        private static readonly Dictionary<string, HashSet<string>> OptionalPlaceholders = new()
        {
            { "intimacy.afterglow.amazing_l1", new HashSet<string> { "{0}" } },
            { "intimacy.afterglow.casual_l1", new HashSet<string> { "{0}", "{1}" } },
            { "intimacy.afterglow.casual_l2", new HashSet<string> { "{0}" } },
            { "intimacy.afterglow.lover_l1", new HashSet<string> { "{0}" } },
            { "intimacy.afterglow.silent_l3", new HashSet<string> { "{0}" } },
            { "intimacy.afterglow.spouse_l1", new HashSet<string> { "{0}" } },
            { "intimacy.afterglow.stay_l1", new HashSet<string> { "{0}" } },
            { "intimacy.anticipation.passive_l2", new HashSet<string> { "{0}" } },
            { "intimacy.anticipation.passive_l3", new HashSet<string> { "{0}" } },
            { "intimacy.anticipation.passive_l4", new HashSet<string> { "{0}" } },
            { "intimacy.anticipation.slow_l1", new HashSet<string> { "{0}" } },
            { "intimacy.anticipation.slow_l2", new HashSet<string> { "{0}" } },
            { "intimacy.anticipation.slow_l4", new HashSet<string> { "{0}" } },
            { "intimacy.anticipation.slow_l5", new HashSet<string> { "{0}" } },
            { "intimacy.anticipation.urgent_l2", new HashSet<string> { "{0}" } },
            { "intimacy.anticipation.urgent_l3", new HashSet<string> { "{0}", "{1}" } },
            { "intimacy.anticipation.urgent_l5", new HashSet<string> { "{0}" } },
            { "intimacy.climax.crest_l2", new HashSet<string> { "{0}" } },
            { "intimacy.climax.loud_l2", new HashSet<string> { "{0}" } },
            { "intimacy.climax.soft_l2", new HashSet<string> { "{0}", "{1}" } },
            { "intimacy.escalation.neutral_l1", new HashSet<string> { "{0}" } },
            { "intimacy.escalation.passion_l1", new HashSet<string> { "{0}" } },
            { "intimacy.escalation.passion_l3", new HashSet<string> { "{0}" } },
            { "intimacy.escalation.tender_l1", new HashSet<string> { "{0}" } },
            { "intimacy.escalation.tender_l2", new HashSet<string> { "{0}" } },
            { "intimacy.escalation.tender_l3", new HashSet<string> { "{0}" } },
            { "intimacy.escalation.whisper_beautiful_l1", new HashSet<string> { "{0}" } },
            { "intimacy.escalation.whisper_beautiful_l3", new HashSet<string> { "{0}" } },
            { "intimacy.escalation.whisper_need_l3", new HashSet<string> { "{0}" } },
            { "intimacy.escalation.whisper_tell_l1", new HashSet<string> { "{0}" } },
            { "intimacy.escalation.whisper_tell_l3", new HashSet<string> { "{0}" } },
            { "intimacy.exploration.body_default", new HashSet<string> { "{0}" } },
            { "intimacy.exploration.body_dwarf", new HashSet<string> { "{0}" } },
            { "intimacy.exploration.body_elf", new HashSet<string> { "{0}" } },
            { "intimacy.exploration.body_hobbit", new HashSet<string> { "{0}" } },
            { "intimacy.exploration.body_orc", new HashSet<string> { "{0}" } },
            { "intimacy.exploration.kiss_first_l2", new HashSet<string> { "{0}" } },
            { "intimacy.exploration.undress_shy_l1", new HashSet<string> { "{0}", "{1}" } },
            { "intimacy.anticipation.slow_l3", new HashSet<string> { "{1}" } },
            { "intimacy.escalation.explore_intro", new HashSet<string> { "{1}" } },
            { "intimacy.escalation.whisper_need_l2", new HashSet<string> { "{1}" } },
            { "intimacy.name_prompt", new HashSet<string> { "{0}" } },
            { "love_street.potion_forget_effect", new HashSet<string> { "{1}" } },
            // v1.2.5: NewsSystem.WriteBirthdayNews passes the English ordinal suffix (st, nd, rd, th
            // from GetOrdinalSuffix) as {3}; the other languages write the ordinal their own way.
            { "news.birthday", new HashSet<string> { "{3}" } },
            { "worldsim.news_immigrant", new HashSet<string> { "{0}" } },   // v1.2.5: the English article ("A", "An")
        };

        [Fact]
        public void Translations_KeepEveryMandatoryPlaceholder()
        {
            // The silent direction: a translation that drops {0} where English carries a
            // name renders the scene without the partner (es/hu afterglow.silent_l1, fixed
            // 2026-09-07). Pronoun positions are exempt through OptionalPlaceholders.
            var en = Load("en");
            foreach (var lang in TargetLangs)
            {
                var d = Load(lang);
                var bad = new List<string>();
                foreach (var (k, v) in en)
                {
                    if (!IsRealKey(k) || !d.TryGetValue(k, out var t)) continue;
                    var optional = OptionalPlaceholders.TryGetValue(k, out var o) ? o : new HashSet<string>();
                    var missing = Args(v).Except(Args(t)).Except(optional).ToList();
                    if (missing.Count > 0) bad.Add($"{k} ({string.Join(",", missing)})");
                }
                Assert.True(bad.Count == 0,
                    $"{lang}.json drops placeholders English carries: {string.Join("; ", bad.Take(8))}");
            }
        }

        [Fact]
        public void OptionalPlaceholders_OnlyNameLiveKeysAndPositions()
        {
            var en = Load("en");
            foreach (var (k, positions) in OptionalPlaceholders)
            {
                Assert.True(en.ContainsKey(k), $"OptionalPlaceholders lists a key en.json no longer has: {k}");
                var stale = positions.Except(Args(en[k])).ToList();
                Assert.True(stale.Count == 0, $"OptionalPlaceholders lists positions {k} no longer carries: {string.Join(",", stale)}");
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
        public void BannedPunctuation_NoneInAnyValue()
        {
            char[] banned = { '\u2014', '\u2013', '\u2026' };
            foreach (var (lang, ceiling) in PunctCeiling)
            {
                var d = Load(lang);
                var offenders = d.Where(kv => kv.Value.IndexOfAny(banned) >= 0)
                                 .Select(kv => kv.Key).ToList();
                int count = d.Values.Sum(v => v.Count(c => Array.IndexOf(banned, c) >= 0));
                Assert.True(count <= ceiling && offenders.Count == 0,
                    $"{lang}.json has {count} banned-punctuation characters (ceiling {ceiling}) in: " +
                    string.Join(", ", offenders.Take(10)) +
                    ". Use ASCII punctuation: -- for dashes, - for number ranges, ... for ellipsis.");
            }
        }
    }
}
