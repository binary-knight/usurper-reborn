using System;
using System.Collections;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text;
using System.Threading.Tasks;
using UsurperRemake.Data;
using UsurperRemake.Systems;

namespace UsurperReborn.Tests;

/// <summary>
/// 1.3.4: shared pieces of the Old God build tests. An Old God fight is recorded as text: every public
/// Monster and BossCombatContext value the fight can read, one "name=value" line each, sorted. The
/// fixture Tests/Fixtures/oldgod-build-7ef83c36.txt holds that text for the eight boss rows, artifact
/// counts 0 and 4 and two dialogues, recorded through the real RunBossCombat at 7ef83c36.
/// </summary>
internal static class OldGodBuild134Support
{
    internal const string FixtureName = "oldgod-build-7ef83c36.txt";

    internal static string Root()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir != null && !Directory.Exists(Path.Combine(dir.FullName, "Scripts"))) dir = dir.Parent;
        return dir!.FullName;
    }

    internal static string FixturePath => Path.Combine(Root(), "Tests", "Fixtures", FixtureName);

    /// <summary>The eight boss rows: the seven gods, then Noctura's betrayal data.</summary>
    internal static List<(string Row, OldGodBossData Data)> Rows()
    {
        var all = OldGodsData.GetAllOldGodsWithVariants();
        return all.Select((d, i) => (i == all.Count - 1 ? "NocturaBetrayal" : d.Type.ToString(), d)).ToList();
    }

    internal static readonly int[] ArtifactCounts = { 0, 4 };

    /// <summary>
    /// The artifacts held for a count. The save artifacts (Soulweaver's Loom, Sunforged Blade) are left
    /// out: whether one is held decides CanSave for Veloura and Aurelion, which is not the artifact count.
    /// </summary>
    internal static readonly ArtifactType[] CountedArtifacts =
        { ArtifactType.CreatorsEye, ArtifactType.ScalesOfLaw, ArtifactType.ShadowCrown, ArtifactType.Worldstone };

    /// <summary>The two dialogues: none (every modifier at its default) and one with every field moved.</summary>
    internal static readonly (string Name, (string Field, object Value)[] Fields)[] Dialogues =
    {
        ("none", Array.Empty<(string, object)>()),
        ("all", new (string, object)[]
        {
            ("DamageMultiplier", 1.25), ("DefenseMultiplier", 1.20), ("BonusDamage", 40), ("BonusDefense", 25),
            ("CriticalChance", 0.15), ("CriticalMultiplier", 2.0), ("HasRageBoost", true), ("HasInsight", true),
            ("BossDamageMultiplier", 0.80), ("BossDefenseMultiplier", 0.85), ("BossConfused", true), ("BossWeakened", true),
            ("ApproachType", "cunning"), ("SpecialEffect", "og134"),
        }),
    };

    internal static string CaseKey(string row, int artifacts, string dialogue) => $"{row}|artifacts={artifacts}|dialogue={dialogue}";

    /// <summary>A fixed hero (as OldGodDialogueTests). Mental 0, so a real RunBossCombat returns before any round.</summary>
    internal static Character Hero() => new Character
    {
        Name2 = "Hero", Class = CharacterClass.Warrior, Race = CharacterRace.Human, Level = 25,
        HP = 900, MaxHP = 900, BaseMaxHP = 900,
        Strength = 100, BaseStrength = 100, WeapPow = 100, Defence = 50, BaseDefence = 50, ArmPow = 50,
        Dexterity = 30, BaseDexterity = 30, Agility = 25, BaseAgility = 25, Constitution = 30, BaseConstitution = 30,
        Stamina = 100, CombatSpeed = CombatSpeed.Instant, Mental = 0,
    };

    /// <summary>Puts the collected artifacts at `count` of CountedArtifacts; returns the set to put back.</summary>
    internal static HashSet<ArtifactType> SetArtifacts(int count)
    {
        var held = StoryProgressionSystem.Instance.CollectedArtifacts;
        var before = new HashSet<ArtifactType>(held);
        held.Clear();
        foreach (var a in CountedArtifacts.Take(count)) held.Add(a);
        return before;
    }

    internal static void RestoreArtifacts(HashSet<ArtifactType> before)
    {
        var held = StoryProgressionSystem.Instance.CollectedArtifacts;
        held.Clear();
        foreach (var a in before) held.Add(a);
    }

    private const BindingFlags Inst = BindingFlags.NonPublic | BindingFlags.Instance;

    /// <summary>Sets a god system's dialogue modifiers (the private CombatModifiers) to a dialogue.</summary>
    internal static void SetModifiers(OldGodBossSystem sys, (string Field, object Value)[] fields)
    {
        var mods = typeof(OldGodBossSystem).GetField("activeCombatModifiers", Inst)!.GetValue(sys)!;
        mods.GetType().GetMethod("Reset")!.Invoke(mods, null);
        foreach (var (field, value) in fields) mods.GetType().GetProperty(field)!.SetValue(mods, value);
    }

    /// <summary>Runs the real RunBossCombat for one case and returns the record of what the fight received.</summary>
    internal static async Task<string[]> RecordThroughRunBossCombat(OldGodBossData data, int artifacts, (string Field, object Value)[] dialogue)
    {
        var before = SetArtifacts(artifacts);
        string[]? captured = null;
        try
        {
            var sys = new OldGodBossSystem();
            var hero = Hero();
            OldGodBossSystem.DialogueSeamForTests = s => SetModifiers(s, dialogue);
            OldGodBossSystem.FightSeamForTests = (m, ctx) => captured = Describe(m, ctx, hero);
            var term = new TerminalEmulator(new MemoryStream(), new MemoryStream());
            var run = (Task<BossEncounterResult>)typeof(OldGodBossSystem).GetMethod("RunBossCombat", Inst)!
                .Invoke(sys, new object[] { hero, data, term })!;
            var result = await run;
            if (result.Outcome != BossOutcome.NotFought) throw new InvalidOperationException("the hero at Mental 0 must not fight");
        }
        finally
        {
            OldGodBossSystem.DialogueSeamForTests = null;
            OldGodBossSystem.FightSeamForTests = null;
            RestoreArtifacts(before);
        }
        return captured ?? throw new InvalidOperationException("RunBossCombat did not reach the fight seam");
    }

    /// <summary>Every case through the real RunBossCombat, in fixture order.</summary>
    internal static async Task<List<(string Key, string[] Lines)>> RecordAll()
    {
        var all = new List<(string, string[])>();
        foreach (var (row, data) in Rows())
            foreach (var n in ArtifactCounts)
                foreach (var (name, fields) in Dialogues)
                    all.Add((CaseKey(row, n, name), await RecordThroughRunBossCombat(data, n, fields)));
        return all;
    }

    internal static string FixtureText(List<(string Key, string[] Lines)> cases)
    {
        var sb = new StringBuilder();
        sb.Append("# OldGodBuild134 fixture: what RunBossCombat hands the fight (god Monster, BossCombatContext, hero stats)\n");
        sb.Append("# recorded at 7ef83c36 through its test seams by OldGodBuild134FixtureRecorderTests (OG134_FIXTURE_OUT)\n");
        sb.Append("# artifacts=4 holds " + string.Join(",", CountedArtifacts) + "\n");
        foreach (var (name, fields) in Dialogues)
            sb.Append("# dialogue=" + name + ": " + (fields.Length == 0 ? "every CombatModifiers field at its default"
                : string.Join(",", fields.Select(f => f.Field + "=" + Format(f.Value)))) + "\n");
        foreach (var (key, lines) in cases)
        {
            sb.Append('[').Append(key).Append("]\n");
            foreach (var l in lines) sb.Append(l).Append('\n');
        }
        return sb.ToString();
    }

    internal static Dictionary<string, string[]> ReadFixture()
    {
        var cases = new Dictionary<string, string[]>();
        string? key = null; var lines = new List<string>();
        foreach (var raw in File.ReadAllLines(FixturePath))
        {
            if (raw.StartsWith("#") || raw.Length == 0) continue;
            if (raw.StartsWith("[") && raw.EndsWith("]"))
            {
                if (key != null) cases[key] = lines.ToArray();
                key = raw[1..^1]; lines = new List<string>();
                continue;
            }
            lines.Add(raw);
        }
        if (key != null) cases[key] = lines.ToArray();
        return cases;
    }

    // ---------- the record ----------

    /// <summary>Monster values that are not the build's: the time the object was made.</summary>
    internal static readonly HashSet<string> SkippedMonsterMembers = new() { "LastAction" };

    /// <summary>
    /// The record of one built fight. Every public instance property and field of the god and of its context,
    /// sorted; delegates are called (the phrase) or applied to a fresh character (the player's dialogue hook);
    /// the boss data is named by type and name (the tests check it is the very object passed in separately);
    /// the hero's stats are written too, so a check knows it built from the same hero.
    /// </summary>
    internal static string[] Describe(Monster m, BossCombatContext ctx, Character hero)
    {
        var lines = new List<string>();
        AddMembers(lines, "monster.", m, SkippedMonsterMembers);
        AddMembers(lines, "context.", ctx, new HashSet<string>());
        var fresh = new Character { Name2 = "Fresh" };
        ctx.ApplyPlayerModifiers?.Invoke(fresh);
        lines.Add("context.ApplyPlayerModifiers(fresh)=" + (ctx.ApplyPlayerModifiers == null ? "null"
            : $"crit:{fresh.TempCritChanceBonus},bloodlust:{Format(fresh.HasBloodlust)},dodge:{Format(fresh.DodgeNextAttack)}," +
              $"atk:{fresh.TempAttackBonus},def:{fresh.TempDefenseBonus}"));
        lines.Add("monster.PhraseInLanguage()=" + (m.PhraseInLanguage == null ? "null" : Format(m.PhraseInLanguage())));
        foreach (var f in new[] { "Level", "Strength", "WeapPow", "Defence", "ArmPow", "MaxHP" })
            lines.Add("hero." + f + "=" + Format(typeof(Character).GetProperty(f)!.GetValue(hero)));
        lines.Sort(StringComparer.Ordinal);
        return lines.ToArray();
    }

    private static void AddMembers(List<string> lines, string prefix, object o, HashSet<string> skip)
    {
        var t = o.GetType();
        foreach (var p in t.GetProperties(BindingFlags.Public | BindingFlags.Instance))
        {
            if (!p.CanRead || p.GetIndexParameters().Length > 0 || skip.Contains(p.Name)) continue;
            AddValue(lines, prefix + p.Name, p.PropertyType, p.GetValue(o));
        }
        foreach (var f in t.GetFields(BindingFlags.Public | BindingFlags.Instance))
        {
            if (skip.Contains(f.Name)) continue;
            AddValue(lines, prefix + f.Name, f.FieldType, f.GetValue(o));
        }
    }

    private static void AddValue(List<string> lines, string name, Type type, object? value)
    {
        if (typeof(Delegate).IsAssignableFrom(type)) return;   // called or applied in Describe
        if (value is OldGodBossData d) { lines.Add(name + "=" + d.Type + ":" + d.Name); return; }
        lines.Add(name + "=" + Format(value));
    }

    internal static string Format(object? v) => v switch
    {
        null => "null",
        string s => "\"" + s.Replace("\\", "\\\\").Replace("\n", "\\n").Replace("\r", "\\r") + "\"",
        bool b => b ? "true" : "false",
        double x => x.ToString("R", CultureInfo.InvariantCulture),
        float x => x.ToString("R", CultureInfo.InvariantCulture),
        decimal x => x.ToString(CultureInfo.InvariantCulture),
        IFormattable f when v.GetType().IsPrimitive || v.GetType().IsEnum => f.ToString(null, CultureInfo.InvariantCulture),
        IDictionary dict => "{" + string.Join(",", dict.Keys.Cast<object>().Select(k => Format(k) + ":" + Format(dict[k])).OrderBy(x => x, StringComparer.Ordinal)) + "}",
        IEnumerable e => "[" + string.Join(",", e.Cast<object?>().Select(Format)) + "]",
        _ => "<" + v.GetType().Name + ">" + v,
    };
}
