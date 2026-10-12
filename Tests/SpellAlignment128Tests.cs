using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using FluentAssertions;
using UsurperRemake;
using UsurperRemake.Locations;
using UsurperRemake.Systems;
using Xunit;

namespace UsurperReborn.Tests;

/// <summary>
/// v1.2.8: Cure Critical and Judgment said "+10 Chivalry" and "+30 Chivalry". A cast takes Darkness off
/// first (down to 0, never below) and adds Chivalry only when the caster has no Darkness. The descriptions
/// now say so in five languages, with the numbers of the code.
/// </summary>
[Collection("SharedGameSingletons")]
public class SpellAlignment128Tests
{
    private const BindingFlags F = BindingFlags.NonPublic | BindingFlags.Instance;
    private static readonly string[] Languages = { "en", "es", "fr", "hu", "it" };

    /// <summary>The two Cleric slots whose cast moves alignment.</summary>
    public static IEnumerable<object[]> Slots() => new[] { new object[] { 8 }, new object[] { 24 } };

    public static IEnumerable<object[]> SlotsAndLanguages() =>
        from slot in new[] { 8, 24 } from lang in Languages select new object[] { slot, lang };

    private static Character Caster(long darkness, long chivalry) => new()
    {
        Name1 = "cleric", Name2 = "Cleric", Class = CharacterClass.Cleric, Level = 100, HP = 100, MaxHP = 5000,
        Mana = 100_000, MaxMana = 100_000, AI = CharacterAI.Human, Darkness = darkness, Chivalry = chivalry,
    };

    /// <summary>One successful cast: what it did to Darkness and to Chivalry.</summary>
    private static (long darkness, long chivalry) CastOnce(int slot, long darkness, long chivalry)
    {
        var target = new Character { Name1 = "rival", Name2 = "Rival", Class = CharacterClass.Warrior, Level = 100, HP = 1_000_000, MaxHP = 1_000_000 };
        for (int attempt = 0; attempt < 500; attempt++)
        {
            var caster = Caster(darkness, chivalry);
            TrainingSystem.SetSkillProficiency(caster, TrainingSystem.GetSpellSkillId(CharacterClass.Cleric, slot), TrainingSystem.ProficiencyLevel.Legendary);
            var result = SpellSystem.CastSpell(caster, slot, target);
            if (!result.Success)
            {
                (caster.Darkness, caster.Chivalry).Should().Be((darkness, chivalry), "a failed cast moves nothing");
                continue;
            }
            return (caster.Darkness - darkness, caster.Chivalry - chivalry);
        }
        throw new InvalidOperationException("no cast succeeded in 500 attempts");
    }

    /// <summary>The number of the code for a slot: what one cast takes off a large Darkness.</summary>
    private static int CodeNumber(int slot) => (int)-CastOnce(slot, 1000, 50).darkness;

    [Theory]
    [InlineData(8, 10)]
    [InlineData(24, 30)]
    public void ACast_TakesDarknessFirst_AndAddsChivalryOnlyWithNone(int slot, int n)
    {
        CastOnce(slot, 1000, 50).Should().Be((-n, 0L), "with Darkness the cast takes {0} off and leaves Chivalry", n);
        CastOnce(slot, n - 3, 50).Should().Be((-(n - 3), 0L), "Darkness stops at 0 and the rest is not turned into Chivalry");
        CastOnce(slot, 0, 50).Should().Be((0L, n), "with no Darkness the cast adds {0} Chivalry", n);
        CodeNumber(slot).Should().Be(n);
    }

    /// <summary>The whole numbers of a text that are not part of a range such as 50-75.</summary>
    private static List<int> LoneNumbers(string text) =>
        Regex.Matches(text, @"(?<![\d-])\d+(?![\d-])").Select(m => int.Parse(m.Value)).ToList();

    [Theory]
    [MemberData(nameof(SlotsAndLanguages))]
    public void TheDescription_SaysWhatTheCastDoes_WithTheCodesNumber(int slot, string lang)
    {
        int n = CodeNumber(slot);
        string key = $"spell.cleric.{slot}.desc";
        Loc.HasIn(lang, key).Should().BeTrue();
        string desc = Loc.GetIn(lang, key);
        string darkness = Loc.GetIn(lang, "team.examine_darkness");
        string chivalry = Loc.GetIn(lang, "team.examine_chivalry");

        LoneNumbers(desc).Should().Equal(new[] { n, n }, "the Darkness taken and the Chivalry added are both {0}: {1}", n, desc);
        desc.Should().EndWith($"+{n} {chivalry}.", "the Chivalry is the last thing said, and conditional: {0}", desc);
        Regex.Matches(desc, Regex.Escape(darkness)).Count.Should().BeGreaterOrEqualTo(2, "Darkness is taken first, and Chivalry needs none: {0}", desc);
        desc.IndexOf(darkness, StringComparison.Ordinal).Should().BeLessThan(desc.IndexOf(chivalry, StringComparison.Ordinal));
        Regex.IsMatch(desc, "[\u2013\u2014]").Should().BeFalse("no dash of either long kind");
    }

    [Theory]
    [MemberData(nameof(Slots))]
    public void TheEnglishText_AndTheSpellBook_AreTheSame(int slot)
    {
        var builtIn = SpellSystem.BuiltInTemplate().Single(s => s.Class == CharacterClass.Cleric && s.Level == slot);
        builtIn.Description.Should().Be(Loc.GetIn("en", $"spell.cleric.{slot}.desc"));
        int n = CodeNumber(slot);
        builtIn.Description.Should().EndWith($"Removes up to {n} Darkness; with no Darkness, +{n} Chivalry.");
    }

    [Fact]
    public void NoOtherSpell_NamesOrMakesAnAlignmentChange()
    {
        // every built-in spell description, in English: only the two name Chivalry or Darkness
        var naming = SpellSystem.BuiltInTemplate()
            .Where(s => Regex.IsMatch(Loc.GetIn("en", $"spell.{s.Class.ToString().ToLowerInvariant()}.{s.Level}.desc"), "Chivalry|Darkness|[Aa]lignment"))
            .Select(s => $"{s.Class}:{s.Level}").ToList();
        naming.Should().BeEquivalentTo(new[] { "Cleric:8", "Cleric:24" });

        // and the spell code moves alignment in those two cases only
        string src = File.ReadAllText(Path.Combine(RepoRoot(), "Scripts", "Systems", "SpellSystem.cs"));
        Regex.Matches(src, @"caster\.Darkness = Math\.Max\(0, caster\.Darkness - (\d+)\);").Select(m => m.Groups[1].Value)
            .Should().Equal("10", "30");
        Regex.Matches(src, @"else caster\.Chivalry \+= (\d+);").Select(m => m.Groups[1].Value).Should().Equal("10", "30");
        Regex.Matches(src, @"\.(Chivalry|Darkness)\s*(\+|-)?=(?!=)").Count.Should().Be(4, "no other line of the spell code writes Chivalry or Darkness");
    }

    // ---------- the rows on a screen: the companion combat skills list shows both descriptions ----------

    private static string Strip(string s) => Regex.Replace(s, "\u001b\\[[0-9;?]*[A-Za-z]", "");

    private static async Task<string> InnSkills(string lang)
    {
        var companion = new Companion
        {
            Id = CompanionId.Mira, Name = "Tester", Title = "the Tested", CombatRole = CombatRole.Healer, Level = 100, RecruitLevel = 100,
            IsRecruited = true, IsActive = true,
        };
        var backend = typeof(SaveSystem).GetField("backend", F)!;
        var real = backend.GetValue(SaveSystem.Instance);
        var prev = GameConfig.Language;
        try
        {
            backend.SetValue(SaveSystem.Instance, DispatchProxy.Create<ISaveBackend, MagicHaggle124Tests.NullBackend>());
            GameConfig.Language = lang;
            var output = new MemoryStream();
            var term = new TerminalEmulator(new MemoryStream(Encoding.UTF8.GetBytes("0\n")), output);
            var inn = new InnLocation();
            typeof(BaseLocation).GetField("terminal", F)!.SetValue(inn, term);
            typeof(BaseLocation).GetField("currentPlayer", F)!.SetValue(inn, new Character { Name1 = "tester", Name2 = "Tester", Level = 100, HP = 100, MaxHP = 100 });
            await (Task)typeof(InnLocation).GetMethod("ManageCompanionAbilities", F)!.Invoke(inn, new object[] { companion })!;
            term.StreamWriterInternal?.Flush();
            return Strip(Encoding.UTF8.GetString(output.ToArray()));
        }
        finally
        {
            GameConfig.Language = prev;
            backend.SetValue(SaveSystem.Instance, real);
        }
    }

    [Theory]
    [InlineData("en")]
    [InlineData("hu")]
    public async Task TheDescriptions_FitTheScreen(string lang)
    {
        string text = await InnSkills(lang);
        var rows = text.Split('\n').Select(r => r.TrimEnd('\r')).ToList();
        foreach (var row in rows.Where(r => r.Length > 0 && "╔║╚".IndexOf(r[0]) < 0))
            row.Length.Should().BeLessThanOrEqualTo(79, row);

        string flat = Regex.Replace(text, @"\s+", " ");
        foreach (int slot in new[] { 8, 24 })
        {
            string desc = Loc.GetIn(lang, $"spell.cleric.{slot}.desc");
            string tail = desc.Substring(desc.LastIndexOf(". ", desc.Length - 3, StringComparison.Ordinal) + 2);
            flat.Should().Contain(tail, "the whole sentence is on the screen");
        }
    }

    private static string RepoRoot()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir != null && !File.Exists(Path.Combine(dir.FullName, "usurper-reloaded.csproj"))) dir = dir.Parent;
        return dir!.FullName;
    }
}
