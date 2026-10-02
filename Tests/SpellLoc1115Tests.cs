using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Text.RegularExpressions;
using FluentAssertions;
using UsurperRemake.Systems;
using Xunit;

namespace UsurperReborn.Tests;

/// <summary>
/// v1.1.15 (spell loc): every built-in spell's name and description reach the player
/// through Loc (spell.&lt;class&gt;.&lt;level&gt;.name / .desc) in all five Localization
/// files, translated numbers match the English literal digit-for-digit, and the known
/// display sites route through SpellInfo.DisplayName / DisplayDescription rather than
/// the raw .Name / .Description identifier fields.
/// </summary>
public class SpellLoc1115Tests
{
    private static readonly string[] Langs = { "en", "es", "fr", "hu", "it" };

    private static string RepoRoot()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir != null && !(Directory.Exists(Path.Combine(dir.FullName, "Scripts")) && Directory.Exists(Path.Combine(dir.FullName, "Localization"))))
            dir = dir.Parent;
        if (dir == null) throw new InvalidOperationException("repo root not found above " + AppContext.BaseDirectory);
        return dir.FullName;
    }

    private static readonly Dictionary<string, Dictionary<string, string>> _cache = new();

    private static Dictionary<string, string> Load(string lang)
    {
        if (_cache.TryGetValue(lang, out var cached)) return cached;
        string path = Path.Combine(RepoRoot(), "Localization", lang + ".json");
        var doc = JsonSerializer.Deserialize<Dictionary<string, JsonElement>>(File.ReadAllText(path))!;
        var dict = doc.Where(kv => kv.Value.ValueKind == JsonValueKind.String)
                       .ToDictionary(kv => kv.Key, kv => kv.Value.GetString() ?? "");
        _cache[lang] = dict;
        return dict;
    }

    private static IEnumerable<(string Key, string Name, string Desc)> AllSpellKeys()
    {
        foreach (var entry in SpellSystem.ExportOverrideTemplate())
        {
            string baseKey = $"spell.{entry.Class.ToString().ToLowerInvariant()}.{entry.Level}";
            yield return (baseKey, entry.Name ?? "", entry.Description ?? "");
        }
    }

    [Fact]
    public void Every_spell_has_a_localized_name_and_description_in_every_language()
    {
        var en = Load("en");
        var missing = new List<string>();
        foreach (var (baseKey, _, _) in AllSpellKeys())
        {
            foreach (var lang in Langs)
            {
                var d = Load(lang);
                if (!d.TryGetValue(baseKey + ".name", out var n) || string.IsNullOrWhiteSpace(n))
                    missing.Add($"{lang}:{baseKey}.name");
                if (!d.TryGetValue(baseKey + ".desc", out var de) || string.IsNullOrWhiteSpace(de))
                    missing.Add($"{lang}:{baseKey}.desc");
            }
        }
        missing.Should().BeEmpty($"every spell name/desc must be localized in all 5 files, missing: {string.Join(", ", missing.Take(20))}");
    }

    private static HashSet<string> DigitRuns(string s) => Regex.Matches(s, @"\d+").Select(m => m.Value).ToHashSet();

    [Fact]
    public void Every_translated_spell_desc_keeps_the_english_numbers_verbatim()
    {
        var problems = new List<string>();
        foreach (var (baseKey, _, _) in AllSpellKeys())
        {
            string key = baseKey + ".desc";
            var enText = Load("en").GetValueOrDefault(key, "");
            var enDigits = DigitRuns(enText);
            if (enDigits.Count == 0) continue;
            foreach (var lang in Langs)
            {
                if (lang == "en") continue;
                var text = Load(lang).GetValueOrDefault(key, "");
                foreach (var digits in enDigits)
                {
                    if (!text.Contains(digits))
                        problems.Add($"{lang}:{key} missing digit run '{digits}'");
                }
            }
        }
        problems.Should().BeEmpty($"translations must not alter numbers/percentages/ranges, e.g.: {string.Join(", ", problems.Take(20))}");
    }

    // --- display-site source checks (commit 1a): a revert to the raw .Name / .Description
    // identifier at any of these known call sites must fail this test. ---

    private static string ReadScript(string relativePath) => File.ReadAllText(Path.Combine(RepoRoot(), relativePath));

    [Fact]
    public void Skill_editor_lists_spells_by_DisplayName_and_DisplayDescription()
    {
        string src = ReadScript(Path.Combine("Scripts", "Locations", "InnLocation.cs"));
        // 1.2.5: the row is drawn by WriteSkillRow (columns from the longest name) and the toggle lines are keys.
        src.Should().Contain("WriteSkillRow(layout, displayIdx, isDisabled, spell.DisplayName,", "the teammate skill editor's spell row must show the localized name");
        src.Should().Contain("spell.DisplayDescription", "the teammate skill editor's spell row must show the localized description");
        src.Should().Contain("Loc.Get(\"inn.enabled\", spell.DisplayName)");
        src.Should().Contain("Loc.Get(\"inn.disabled\", spell.DisplayName)");
        // .Name must still be used for the disabled-spell list itself (identifier, not display).
        src.Should().Contain("companion.DisabledSpells.Contains(spell.Name)");
    }

    [Fact]
    public void Quickbar_row_shows_DisplayName_for_a_bound_spell()
    {
        string src = ReadScript(Path.Combine("Scripts", "Systems", "ClassAbilitySystem.cs"));
        src.Should().Contain("spell?.DisplayName ?? slotId");
    }

    [Fact]
    public void Combat_spell_list_and_AoE_message_use_DisplayName_and_DisplayDescription()
    {
        string src = ReadScript(Path.Combine("Scripts", "Systems", "CombatEngine.cs"));
        src.Should().Contain("spellInfo.DisplayName", "the AoE hit message must show the localized spell name");
        Regex.Matches(src, Regex.Escape("sp.DisplayName")).Count.Should().BeGreaterThanOrEqualTo(2,
            "the cast menu and the teammate follower quickbar row must both show DisplayName");
        src.Should().Contain("name = spell.DisplayName; desc = spell.DisplayDescription;",
            "the in-combat quickbar-info lookup must show the localized name and description");
    }

    [Fact]
    public void Gmcp_bridge_reports_DisplayName_to_the_external_client()
    {
        string src = ReadScript(Path.Combine("Scripts", "Server", "GmcpBridge.cs"));
        src.Should().Contain("name = spell.DisplayName,");
    }

    // --- commit 2: Sage cast messages route through Loc in all languages ---

    private static readonly string[] SageCastKeys =
    {
        "combat.sage_fog_cast", "combat.sage_poison_touch_cast", "combat.sage_mind_spike_cast",
        "combat.sage_confusion_cast", "combat.sage_hit_self_cast", "combat.sage_escape_cast",
        "combat.sage_steal_life_cast", "combat.sage_psychic_scream_cast", "combat.sage_shadow_cloak_cast",
        "combat.sage_energy_drain_cast", "combat.sage_mind_blank_cast", "combat.sage_shadow_step_cast",
        "combat.sage_mass_confusion_cast", "combat.sage_noctura_veil_cast", "combat.sage_soul_rend_cast",
        "combat.sage_ocean_memory_cast", "combat.sage_temporal_paradox_cast", "combat.sage_veloura_embrace_cast",
        "combat.sage_death_kiss_cast",
        "combat.sage_the_enemy", "combat.sage_the_target",
    };

    [Fact]
    public void Every_sage_cast_message_key_exists_in_every_language()
    {
        var missing = new List<string>();
        foreach (var key in SageCastKeys)
            foreach (var lang in Langs)
            {
                var d = Load(lang);
                if (!d.TryGetValue(key, out var v) || string.IsNullOrWhiteSpace(v))
                    missing.Add($"{lang}:{key}");
            }
        missing.Should().BeEmpty($"missing sage cast-message keys: {string.Join(", ", missing.Take(20))}");
    }

    [Fact]
    public void ExecuteSageSpell_no_longer_builds_cast_messages_from_english_literals()
    {
        string src = ReadScript(Path.Combine("Scripts", "Systems", "SpellSystem.cs"));
        int start = src.IndexOf("private static void ExecuteSageSpell(");
        start.Should().BeGreaterThan(0);
        int end = src.IndexOf("\n    }\n", start);
        string body = src.Substring(start, end - start);
        foreach (var key in SageCastKeys.Where(k => k.EndsWith("_cast")))
            body.Should().Contain(key, $"ExecuteSageSpell must build its message through Loc.Get(\"{key}\")");
    }
}
