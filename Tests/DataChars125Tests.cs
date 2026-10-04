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
/// v1.2.5: the spell, companion, boon, blessing, character creation, specialization, relationship and
/// visual novel tables. What is stored or compared (spell names, companion ids and saved history, boon ids and
/// alignments, class and specialization ids, starter item names, relationship stages) stays English; what is
/// shown goes through Loc keys in the reader's language, and every row fits 79 columns.
/// </summary>
[Collection("SharedGameSingletons")]
public class DataChars125Tests
{
    private const BindingFlags F = BindingFlags.NonPublic | BindingFlags.Instance;
    private const BindingFlags SNP = BindingFlags.NonPublic | BindingFlags.Static;
    private const int MaxWidth = 79;
    private static readonly string[] AllLanguages = { "en", "es", "fr", "hu", "it" };
    private static readonly string[] OtherLanguages = { "es", "fr", "hu", "it" };

    // GameConfig.MaxNameLength (30) characters, the longest name a player can have.
    private const string LongName = "Bartholomew Thistlewood Grande";

    // ---------- helpers ----------

    private sealed class Screen
    {
        public TerminalEmulator Term = null!;
        public MemoryStream Output = new();
        public string Text
        {
            get
            {
                Term.StreamWriterInternal?.Flush();
                return Regex.Replace(Encoding.UTF8.GetString(Output.ToArray()), "\u001b\\[[0-9;?]*[A-Za-z]", "");
            }
        }
    }

    private static Screen NewScreen(params string[] lines)
    {
        var s = new Screen();
        s.Term = new TerminalEmulator(new LineStream(lines.Concat(Enumerable.Repeat("", 12)), _ =>
        {
            s.Term.StreamWriterInternal?.Flush();
            s.Output.WriteByte((byte)'\n');
        }), s.Output);
        return s;
    }

    private static async Task<T> InLanguage<T>(string lang, Func<Task<T>> body)
    {
        var prev = GameConfig.Language;
        bool sr = GameConfig.ScreenReaderMode;
        try
        {
            GameConfig.Language = lang;
            GameConfig.ScreenReaderMode = false;
            return await body();
        }
        finally { GameConfig.Language = prev; GameConfig.ScreenReaderMode = sr; }
    }

    private static T InLang<T>(string lang, Func<T> body) =>
        InLanguage(lang, () => Task.FromResult(body())).GetAwaiter().GetResult();

    private static string L(string lang, string key, params object[] args) => Loc.GetIn(lang, key, args);

    private static List<string> Rows(string text) => text.Replace("\r", "").Split('\n').ToList();

    private static void Capture(string name, string text)
    {
        var dir = Environment.GetEnvironmentVariable("USURPER_EVIDENCE_DIR");
        if (!string.IsNullOrEmpty(dir)) File.WriteAllText(Path.Combine(dir, name), text);
    }

    private static void EveryRowFits(IEnumerable<string> rows, string screen)
    {
        foreach (var row in rows)
            row.Length.Should().BeLessOrEqualTo(MaxWidth, $"every row of the {screen} fits in {MaxWidth} columns: \"{row}\"");
    }

    private static string Src(params string[] parts) =>
        File.ReadAllText(Path.Combine(new[] { UsurperReborn.Tests.Localization.HardcodedTextScannerTests.RepoRoot(), "Scripts" }.Concat(parts).ToArray()));

    private static T At<T>(T location, TerminalEmulator term, Character? hero) where T : BaseLocation
    {
        typeof(BaseLocation).GetField("terminal", F)!.SetValue(location, term);
        typeof(BaseLocation).GetField("currentPlayer", F)!.SetValue(location, hero);
        return location;
    }

    /// <summary>The keys a source file reads with a literal Loc.Get or Loc.GetIn, matching the prefix.</summary>
    private static List<string> KeysIn(string source, string prefix) =>
        Regex.Matches(source, "Loc\\.Get(?:In)?\\((?:[a-z]+, )?\"(" + Regex.Escape(prefix) + "[a-z0-9_.]+)\"")
            .Select(m => m.Groups[1].Value).Distinct().ToList();

    /// <summary>Each key is in every language, and es fr hu it are not the English text.</summary>
    private static void TranslatedInFiveLanguages(IEnumerable<string> keys, ISet<string>? sameByDesign = null)
    {
        foreach (var key in keys)
        {
            Loc.HasIn("en", key).Should().BeTrue($"{key} is in en");
            foreach (var lang in OtherLanguages)
            {
                Loc.HasIn(lang, key).Should().BeTrue($"{key} is in {lang}");
                if (sameByDesign != null && sameByDesign.Contains(key)) continue;
                Loc.GetIn(lang, key).Should().NotBe(Loc.GetIn("en", key), $"{lang} {key} is translated");
            }
        }
    }

    // ---------- 1. spells ----------

    private static readonly CharacterClass[] SpellClasses =
    {
        CharacterClass.Cleric, CharacterClass.Magician, CharacterClass.Sage, CharacterClass.Tidesworn,
        CharacterClass.Wavecaller, CharacterClass.Cyclebreaker, CharacterClass.Abysswarden, CharacterClass.Voidreaver,
    };

    private static Character Caster(CharacterClass cls) => new()
    {
        Name1 = LongName, Name2 = LongName, Class = cls, Level = 100, HP = 5000, MaxHP = 5000, Mana = 9000, MaxMana = 9000,
        Intelligence = 200, Wisdom = 200, Charisma = 50, AI = CharacterAI.Human,
    };

    /// <summary>What CastSpell writes for a successful critical cast of this spell, in the session's language.</summary>
    private static SpellSystem.SpellResult CastText(CharacterClass cls, int level)
    {
        var caster = Caster(cls);
        var info = SpellSystem.GetSpellInfo(cls, level);
        var result = new SpellSystem.SpellResult { Success = true, ProficiencyMultiplier = 1.0f };
        result.Message = Loc.Get("combat.spell_utters", caster.Name2, info.MagicWords) + " " + Loc.Get("combat.spell_critical_cast");
        result.IsCriticalCast = true;
        result.CastLine = result.Message;
        typeof(SpellSystem).GetMethod("ExecuteSpellEffect", SNP)!.Invoke(null, new object?[] { caster, level, null, null, result });
        return result;
    }

    private static IEnumerable<(CharacterClass cls, int level)> AllSpells() =>
        SpellClasses.SelectMany(c => SpellSystem.GetAllSpellsForClass(c).Select(s => (c, s.Level)));

    [Fact]
    public void EverySpellMessageKey_IsInFiveLanguages_Translated()
    {
        var keys = KeysIn(Src("Systems", "SpellSystem.cs"), "combat.");
        keys.Should().Contain(new[] { "combat.spell_utters", "combat.spell_critical_cast", "combat.spell_roll_info", "combat.spell_magic_missile_cast", "combat.spell_unmaking_cast" });
        TranslatedInFiveLanguages(keys);
        // the spell table itself holds no English message any more
        var src = Src("Systems", "SpellSystem.cs");
        Regex.IsMatch(src, "result\\.Message \\+?= \\$?\" ?[A-Za-z]").Should().BeFalse("every spell message line goes through a key");
        src.Should().NotContain("CRITICAL CAST!\"").And.NotContain("\"the target\"").And.NotContain("\"the enemy\"");
    }

    [Theory]
    [InlineData("en")]
    [InlineData("es")]
    [InlineData("fr")]
    [InlineData("hu")]
    [InlineData("it")]
    public void EverySpellMessage_ShowsInTheReadersLanguage_AndFits79_WithALongName(string lang)
    {
        var all = new StringBuilder();
        foreach (var (cls, level) in AllSpells())
        {
            var result = InLang(lang, () => CastText(cls, level));
            result.Message.Should().StartWith(L(lang, "combat.spell_utters", LongName, SpellSystem.GetSpellInfo(cls, level).MagicWords));
            var rows = SpellSystem.MessageRows(result.Message);
            EveryRowFits(rows, $"{cls} spell {level} in {lang}");
            all.AppendLine(string.Join("\n", rows));
            if (lang == "en") continue;
            foreach (var english in new[] { " damage", " defense", "utters", "CRITICAL CAST", " attack)", "the target", "the enemy", "HP!" })
                result.Message.Should().NotContain(english, $"{cls} spell {level} in {lang} shows no English");
        }
        // the roll line and the fumble lines
        var fumble = InLang(lang, () => L(lang, "combat.spell_fumble", LongName) + "\n  " + L(lang, "combat.spell_miscast_hint"));
        EveryRowFits(SpellSystem.MessageRows(fumble), "fumble message");
        var failed = L(lang, "combat.spell_utters_fails", LongName, "Admoriasumumarie") + "\n  " + L(lang, "combat.spell_roll_info", 20, 99, 119, 33);
        EveryRowFits(SpellSystem.MessageRows(failed), "failed cast");
        Capture($"datachars-spells-{lang}.txt", all.ToString());
    }

    [Fact]
    public void MessageRows_KeepsAShortMessage_AndEachLinesIndent()
    {
        SpellSystem.MessageRows("Tester utters 'Exmamarie'!").Should().Equal("Tester utters 'Exmamarie'!");
        var rows = SpellSystem.MessageRows(new string('a', 10) + " " + string.Join(" ", Enumerable.Repeat("word", 30)) + "\n  " + string.Join(" ", Enumerable.Repeat("tail", 30)));
        rows.Count.Should().BeGreaterThan(3);
        rows.Should().OnlyContain(r => r.Length <= MaxWidth);
        rows.Last().Should().StartWith("  tail");
    }

    [Theory]
    [InlineData("en")]
    [InlineData("hu")]
    [InlineData("fr")]
    public void SirensLament_ReadsTheCriticalFlag_NotTheShownText(string lang)
    {
        static (int duration, int damage) Run(string lang, bool crit) => InLang(lang, () =>
        {
            var caster = Caster(CharacterClass.Wavecaller);
            var result = new SpellSystem.SpellResult { Success = true, ProficiencyMultiplier = 1.0f, IsCriticalCast = crit };
            result.Message = Loc.Get("combat.spell_utters", caster.Name2, "Sirenlamentarie") + (crit ? " " + Loc.Get("combat.spell_critical_cast") : "");
            typeof(SpellSystem).GetMethod("ExecuteWavecallerSpell", SNP)!.Invoke(null, new object?[] { caster, 3, null, null, result, new Random(11), 1.0f });
            return (result.Duration, result.Damage);
        });
        var crit = Run(lang, true);
        crit.duration.Should().Be(8, $"a critical Siren's Lament doubles its duration in {lang}");
        Run(lang, false).duration.Should().Be(4);
        // the same numbers in every language
        crit.Should().Be(Run("en", true));
        Run(lang, false).Should().Be(Run("en", false));
    }

    [Theory]
    [InlineData("en")]
    [InlineData("hu")]
    public void ACast_KeepsItsIncantationLine_AsData(string lang)
    {
        var mage = Caster(CharacterClass.Cleric);
        var result = InLang(lang, () =>
        {
            for (int i = 0; i < 300; i++)
            {
                mage.Mana = mage.MaxMana;
                var r = SpellSystem.CastSpell(mage, 1);
                if (r.Success) return r;
            }
            return null;
        });
        result.Should().NotBeNull("a level 100 cleric casts Cure Light (a heal never fizzles)");
        result!.CastLine.Should().StartWith(L(lang, "combat.spell_utters", mage.Name2, "Sularahamasturie"));
        result.Message.Should().StartWith(result.CastLine).And.NotBe(result.CastLine, "the effect follows the incantation");
        result.IsCriticalCast.Should().Be(result.CastLine.Contains(L(lang, "combat.spell_critical_cast")));

        // the combat engine strips an ally cast down to that line, and tells a cooldown by its flag
        var engine = Src("Systems", "CombatEngine.cs");
        engine.Should().Contain("displayMsg = spellResult.CastLine;").And.Contain("if (!spellResult.CooldownBlocked)");
        engine.Should().NotContain("IndexOf(\"CRITICAL CAST!\")").And.NotContain("Message.Contains(\"recovered\")");
    }

    [Fact]
    public void UnmakingOnCooldown_IsFlagged_InEveryLanguage()
    {
        foreach (var lang in AllLanguages)
        {
            var result = InLang(lang, () =>
            {
                var caster = Caster(CharacterClass.Voidreaver);
                caster.UnmakingCooldown = 2;
                var r = new SpellSystem.SpellResult { Success = true, ProficiencyMultiplier = 1.0f };
                typeof(SpellSystem).GetMethod("ExecuteVoidreaverSpell", SNP)!.Invoke(null, new object?[] { caster, 5, null, null, r, new Random(3), 1.0f });
                return r;
            });
            result.CooldownBlocked.Should().BeTrue();
            result.Success.Should().BeFalse();
            result.Message.Trim().Should().Be(L(lang, "combat.spell_unmaking_recovering"));
        }
    }

    [Theory]
    [InlineData("hu")]
    [InlineData("es")]
    public async Task DisablingASpell_InAnotherLanguage_StoresItsEnglishName_AndCombatSkipsIt(string lang)
    {
        var mira = CompanionSystem.Instance.GetCompanion(CompanionId.Mira)!;
        int level = mira.Level;
        var disabled = mira.DisabledSpells.ToList();
        var disabledAbilities = mira.DisabledAbilities.ToList();
        try
        {
            mira.Level = 30;
            mira.DisabledSpells.Clear();
            mira.DisabledAbilities.Clear();
            int abilities = ClassAbilitySystem.GetAvailableAbilities(new Character { Class = CharacterClass.Cleric, Level = 30 }).Count;
            var first = SpellSystem.GetAllSpellsForClass(CharacterClass.Cleric).OrderBy(s => s.Level).First();
            var screen = NewScreen((abilities + 1).ToString(), "0");
            await InLanguage(lang, async () =>
            {
                var inn = At(new InnLocation(), screen.Term, null);
                await (Task)typeof(InnLocation).GetMethod("ManageCompanionAbilities", F)!.Invoke(inn, new object[] { mira })!;
                return 0;
            });
            screen.Text.Should().Contain(first.DisplayName.Length > 0 ? InLang(lang, () => first.DisplayName) : first.Name);
            mira.DisabledSpells.Should().Equal(new[] { first.Name }, "the disabled spell is stored by its English name, its id");
            first.Name.Should().Be("Cure Light");

            // combat reads the same id
            var engine = new CombatEngine(NewScreen().Term);
            var caster = new Character { Name2 = "Mira", Class = CharacterClass.Cleric, IsCompanion = true, CompanionId = CompanionId.Mira };
            var set = (HashSet<string>)typeof(CombatEngine).GetMethod("GetDisabledSpellsFor", F)!.Invoke(engine, new object[] { caster })!;
            set.Should().Contain("Cure Light");
        }
        finally
        {
            mira.Level = level;
            mira.DisabledSpells.Clear();
            foreach (var s in disabled) mira.DisabledSpells.Add(s);
            mira.DisabledAbilities.Clear();
            foreach (var a in disabledAbilities) mira.DisabledAbilities.Add(a);
        }
    }
}
