using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using FluentAssertions;
using UsurperRemake;
using UsurperRemake.Data;
using UsurperRemake.Locations;
using UsurperRemake.Systems;
using Xunit;

namespace UsurperReborn.Tests;

/// <summary>
/// v1.2.8: the level and the mana shown for a spell are the ones the game applies. The wiki export gave
/// the stored template values (a level equal to the slot number and a mana value no cast charges), and
/// three screens showed the template mana: the world boss spell list, the duel spell list and the
/// companion combat skills list at the Inn.
/// </summary>
[Collection("SharedGameSingletons")]
public class SpellFacts128Tests
{
    private const BindingFlags F = BindingFlags.NonPublic | BindingFlags.Instance;

    private static string Strip(string s) => Regex.Replace(s, "\u001b\\[[0-9;?]*[A-Za-z]", "");

    private static (TerminalEmulator term, MemoryStream output) Term(string input)
    {
        var output = new MemoryStream();
        return (new TerminalEmulator(new MemoryStream(Encoding.UTF8.GetBytes(input)), output), output);
    }

    private static string Shown(TerminalEmulator term, MemoryStream output)
    {
        term.StreamWriterInternal?.Flush();
        return Strip(Encoding.UTF8.GetString(output.ToArray()));
    }

    private static async Task<T> WithLanguage<T>(string lang, Func<Task<T>> body)
    {
        var prev = GameConfig.Language;
        try
        {
            GameConfig.Language = lang;
            return await body();
        }
        finally { GameConfig.Language = prev; }
    }

    /// <summary>A level 100 Cleric who knows slots 1 to 3, with the Wisdom given.</summary>
    private static Character Cleric(long wisdom)
    {
        var p = new Character
        {
            Name1 = "cleric", Name2 = "Cleric", Class = CharacterClass.Cleric, Level = 100, HP = 500, MaxHP = 500,
            Mana = 5000, MaxMana = 5000, AI = CharacterAI.Human, Wisdom = wisdom, BaseWisdom = wisdom,
        };
        foreach (var s in SpellSystem.GetAllSpellsForClass(CharacterClass.Cleric).Where(s => s.Level <= 3))
        {
            typeof(SpellLearningSystem).GetMethod("EnsureSpellSlot", BindingFlags.NonPublic | BindingFlags.Static)!.Invoke(null, new object?[] { p, s.Level });
            p.Spell[s.Level - 1][0] = true;
        }
        return p;
    }

    // ---------- the rule itself ----------

    [Fact]
    public void ACast_ChargesTenPlusFivePerSlot_LoweredByWisdom_NeverTheTemplateValue()
    {
        foreach (var spell in SpellSystem.GetAllSpellsForClass(CharacterClass.Cleric))
        {
            SpellSystem.BaseManaCost(spell.Level).Should().Be(10 + 5 * spell.Level);
            SpellSystem.CalculateManaCost(spell, new Character { Class = CharacterClass.Cleric, Wisdom = 10 })
                .Should().Be(SpellSystem.BaseManaCost(spell.Level), "Wisdom 10 lowers nothing (slot {0})", spell.Level);
            SpellSystem.CalculateManaCost(spell, new Character { Class = CharacterClass.Cleric, Wisdom = 30 })
                .Should().BeLessThan(SpellSystem.BaseManaCost(spell.Level), "Wisdom 30 takes 30 percent off (slot {0})", spell.Level);
        }
        var first = SpellSystem.GetSpellInfo(CharacterClass.Cleric, 1)!;
        var caster = Cleric(10);
        long before = caster.Mana;
        SpellSystem.CastSpell(caster, 1);
        (before - caster.Mana).Should().Be(15, "slot 1 costs 10 + 5");
        first.ManaCost.Should().NotBe(15, "the template value of Cure Light is another number, which no cast charges");
    }

    // ---------- the wiki export ----------

    private static JsonElement[] ExportedSpells()
    {
        string dir = Path.Combine(Path.GetTempPath(), "usurper-wiki-l1-" + Guid.NewGuid().ToString("N"));
        try
        {
            WikiDataExporter.Export(dir);
            using var doc = JsonDocument.Parse(File.ReadAllText(Path.Combine(dir, "spells.json")));
            return doc.RootElement.GetProperty("data").Clone().EnumerateArray().ToArray();
        }
        finally { if (Directory.Exists(dir)) Directory.Delete(dir, recursive: true); }
    }

    [Fact]
    public void TheExport_GivesTheLevelThatOpensTheSpell_AndTheManaACastCharges()
    {
        var spells = ExportedSpells();
        var template = SpellSystem.BuiltInTemplate();
        spells.Length.Should().Be(template.Count);
        int levelDiffers = 0, manaDiffers = 0;
        foreach (var t in template)
        {
            var e = spells.Single(s => s.GetProperty("id").GetString() == t.Class + ":" + t.Level);
            int level = e.GetProperty("levelRequired").GetInt32();
            int mana = e.GetProperty("manaCost").GetInt32();
            var info = SpellSystem.GetSpellInfo(t.Class, t.Level)!;

            level.Should().Be(SpellSystem.GetLevelRequired(t.Class, t.Level), "{0} slot {1}: the level CanCastSpell asks for", t.Class, t.Level);
            mana.Should().Be(SpellSystem.CalculateManaCost(info, new Character { Class = t.Class, Wisdom = 10 }),
                "{0} slot {1}: what a cast charges before Wisdom lowers it", t.Class, t.Level);
            mana.Should().Be(10 + 5 * t.Level);
            if (level != t.LevelRequired) levelDiffers++;
            if (mana != t.ManaCost) manaDiffers++;
        }
        levelDiffers.Should().BeGreaterThan(40, "the 25-spell classes list a level equal to the slot number");
        manaDiffers.Should().BeGreaterThan(40, "the template mana is not what a cast charges");

        JsonElement Spell(string id) => spells.Single(s => s.GetProperty("id").GetString() == id);
        Spell("Cleric:1").GetProperty("levelRequired").GetInt32().Should().Be(1);
        Spell("Cleric:1").GetProperty("manaCost").GetInt32().Should().Be(15);
        Spell("Cleric:5").GetProperty("levelRequired").GetInt32().Should().Be(16, "slot 5 opens at level 16");
        Spell("Cleric:5").GetProperty("manaCost").GetInt32().Should().Be(35);
        Spell("Cleric:25").GetProperty("levelRequired").GetInt32().Should().Be(100);
        Spell("Cleric:25").GetProperty("manaCost").GetInt32().Should().Be(135);
        Spell("Wavecaller:5").GetProperty("levelRequired").GetInt32().Should().Be(22, "the listed level is the higher one here");
    }

    // ---------- the world boss spell list ----------

    private static async Task<(string text, Character caster)> WorldBossSpells(long wisdom, string input)
    {
        var (term, output) = Term(input);
        var cleric = Cleric(wisdom);
        var method = typeof(WorldBossSystem).GetMethod("ProcessSpellCast", F)!;
        await (Task<long>)method.Invoke(WorldBossSystem.Instance, new object?[] { cleric, term, new WorldBossDefinition(), new WorldBossRuntimeData(), new Random(1) })!;
        return (Shown(term, output), cleric);
    }

    [Theory]
    [InlineData("en", 10)] [InlineData("en", 40)] [InlineData("hu", 40)]
    public async Task WorldBossSpellList_ShowsWhatTheCastCharges(string lang, long wisdom)
    {
        var (text, caster) = await WithLanguage(lang, () => WorldBossSpells(wisdom, "1\n"));
        var known = SpellSystem.GetAllSpellsForClass(CharacterClass.Cleric).Where(s => s.Level <= 3).OrderBy(s => s.Level).ToList();
        for (int i = 0; i < known.Count; i++)
        {
            int charged = SpellSystem.CalculateManaCost(known[i], Cleric(wisdom));
            string name = await WithLanguage(lang, () => Task.FromResult(known[i].DisplayName));
            string row = "  " + Loc.GetIn(lang, "world_boss.spell_option", i + 1, name, charged);
            text.Split('\n').Select(r => r.TrimEnd('\r')).Should().Contain(row, "screen: {0}", text);
            row.Length.Should().BeLessThanOrEqualTo(79);
        }
        int first = SpellSystem.CalculateManaCost(known[0], Cleric(wisdom));
        (5000 - caster.Mana).Should().Be(first, "choosing 1 charges the number shown beside it");
        if (wisdom == 40) first.Should().Be(9, "15 less 45 percent, rounded as the cast rounds it");
    }

    // ---------- the duel spell list ----------

    [Theory]
    [InlineData("en", 10)] [InlineData("en", 40)] [InlineData("hu", 40)]
    public async Task DuelSpellList_ShowsWhatTheCastCharges(string lang, long wisdom)
    {
        var caster = Cleric(wisdom);
        var rival = new Character { Name1 = "rival", Name2 = "Rival", Class = CharacterClass.Warrior, Level = 100, HP = 100_000, MaxHP = 100_000 };
        var (term, output) = Term("1\n");
        string text = await WithLanguage(lang, async () =>
        {
            var engine = new CombatEngine(term);
            var method = typeof(CombatEngine).GetMethod("ExecutePvPSpell", F)!;
            await (Task)method.Invoke(engine, new object[] { caster, rival, new CombatAction { Type = CombatActionType.CastSpell }, new CombatResult { CombatLog = new List<string>() } })!;
            return Shown(term, output);
        });

        var known = SpellSystem.GetAllSpellsForClass(CharacterClass.Cleric).Where(s => s.Level <= 3).OrderBy(s => s.Level).ToList();
        for (int i = 0; i < known.Count; i++)
        {
            int charged = SpellSystem.CalculateManaCost(known[i], Cleric(wisdom));
            string name = await WithLanguage(lang, () => Task.FromResult(known[i].DisplayName));
            string row = $"  [{i + 1}] {name} ({Loc.GetIn(lang, "combat.level_label")} {known[i].Level}, {Loc.GetIn(lang, "combat.cost_label")}: {charged})";
            text.Split('\n').Select(r => r.TrimEnd('\r')).Should().Contain(row, "screen: {0}", text);
            row.Length.Should().BeLessThanOrEqualTo(79);
        }
        (5000 - caster.Mana).Should().Be(SpellSystem.CalculateManaCost(known[0], Cleric(wisdom)), "choosing 1 charges the number shown beside it");
    }

    // ---------- the companion combat skills list at the Inn ----------

    private static Companion Healer(int healingPower, int wisdom) => new()
    {
        Id = CompanionId.Mira, Name = "Tester", Title = "the Tested", CombatRole = CombatRole.Healer, Level = 30, RecruitLevel = 30,
        IsRecruited = true, IsActive = true, Wisdom = wisdom,
        BaseStats = new CompanionStats { HP = 200, Attack = 10, Defense = 10, MagicPower = 40, Speed = 10, HealingPower = healingPower },
    };

    private static async Task<string> InnSkills(string lang, Companion companion)
    {
        var backend = typeof(SaveSystem).GetField("backend", F)!;
        var real = backend.GetValue(SaveSystem.Instance);
        try
        {
            backend.SetValue(SaveSystem.Instance, DispatchProxy.Create<ISaveBackend, MagicHaggle124Tests.NullBackend>());
            return await WithLanguage(lang, async () =>
            {
                var (term, output) = Term("0\n");
                var inn = new InnLocation();
                typeof(BaseLocation).GetField("terminal", F)!.SetValue(inn, term);
                typeof(BaseLocation).GetField("currentPlayer", F)!.SetValue(inn, new Character { Name1 = "tester", Name2 = "Tester", Level = 30, HP = 100, MaxHP = 100 });
                await (Task)typeof(InnLocation).GetMethod("ManageCompanionAbilities", F)!.Invoke(inn, new object[] { companion })!;
                return Shown(term, output);
            });
        }
        finally { backend.SetValue(SaveSystem.Instance, real); }
    }

    [Theory]
    [InlineData("en", 12, 10)]   // Wisdom from the healing power alone
    [InlineData("en", 12, 40)]   // the companion's own Wisdom is the higher one: the fight uses it
    [InlineData("hu", 12, 40)]
    public async Task InnSkillsList_ShowsWhatACastChargesTheCompanionInAFight(string lang, int healingPower, int wisdom)
    {
        var companion = Healer(healingPower, wisdom);
        string text = await InnSkills(lang, companion);

        var fighter = CompanionSystem.BuildCombatCharacter(companion);
        fighter.Wisdom.Should().Be(Math.Max(healingPower, wisdom), "the fight takes the higher of the two");
        var spells = SpellSystem.GetAllSpellsForClass(CharacterClass.Cleric)
            .Where(s => companion.Level >= SpellSystem.GetLevelRequired(CharacterClass.Cleric, s.Level)).OrderBy(s => s.Level).ToList();
        spells.Should().NotBeEmpty();
        var rows = text.Split('\n').Select(r => r.TrimEnd('\r')).ToList();
        foreach (var spell in spells)
        {
            int charged = SpellSystem.CalculateManaCost(spell, fighter);
            string name = await WithLanguage(lang, () => Task.FromResult(spell.DisplayName));
            string cost = Loc.GetIn(lang, "inn.skill_cost_mp", $"{charged,2}");
            rows.Should().Contain(r => r.Contains(name + " ") && r.Contains(" " + cost + "  "), "{0} costs {1}; screen: {2}", spell.Name, cost, text);
        }
        foreach (var row in rows.Where(r => r.Length > 0 && "╔║╚".IndexOf(r[0]) < 0)) row.Length.Should().BeLessThanOrEqualTo(79, row);
        if (wisdom == 40)
            SpellSystem.CalculateManaCost(spells[0], fighter).Should().Be(9, "15 less 45 percent");
    }

    [Fact]
    public void TheFightsCharacter_IsBuiltInOnePlace()
    {
        string src = File.ReadAllText(Path.Combine(RepoRoot(), "Scripts", "Systems", "CompanionSystem.cs"));
        // the fight's Wisdom: once where the character is built, once where a level-up refreshes it
        Regex.Matches(src, @"BaseWisdom = Math\.Max\(companion\.BaseStats\.HealingPower, companion\.Wisdom\)").Count.Should().Be(2);
        src.Should().Contain("var charWrapper = BuildCombatCharacter(companion);");
        string inn = File.ReadAllText(Path.Combine(RepoRoot(), "Scripts", "Locations", "InnLocation.cs"));
        Regex.Matches(inn, @"SkillCost\(""inn\.skill_cost_mp"", SpellSystem\.CalculateManaCost\((sp|spell), caster\)\)").Count.Should().Be(2, "the layout and the row show the same number");
        inn.Should().NotContain("SkillCost(\"inn.skill_cost_mp\", sp.ManaCost)").And.NotContain("SkillCost(\"inn.skill_cost_mp\", spell.ManaCost)");
    }

    private static string RepoRoot()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir != null && !File.Exists(Path.Combine(dir.FullName, "usurper-reloaded.csproj"))) dir = dir.Parent;
        return dir!.FullName;
    }
}
