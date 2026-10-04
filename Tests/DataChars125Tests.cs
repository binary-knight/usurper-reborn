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

    [Fact]
    public void TheLongName_IsTheLongestNameAPlayerCanHave() => LongName.Length.Should().Be(GameConfig.MaxNameLength);

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

    /// <summary>A framed box row (WriteBoxHeader) is at most 80 wide; every other row fits in 79.</summary>
    private static void EveryRowFits(IEnumerable<string> rows, string screen)
    {
        foreach (var row in rows)
            row.Length.Should().BeLessOrEqualTo(row.Length > 0 && "╔║╚".IndexOf(row[0]) >= 0 ? MaxWidth + 1 : MaxWidth,
                $"every row of the {screen} fits in {MaxWidth} columns: \"{row}\"");
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

    [Theory]
    [InlineData("en")]
    [InlineData("es")]
    [InlineData("fr")]
    [InlineData("hu")]
    [InlineData("it")]
    public void TheLongestSpellName_FitsTheCastLines_WithALongName(string lang)
    {
        var longest = InLang(lang, () => SpellClasses.SelectMany(SpellSystem.GetAllSpellsForClass).Select(s => s.DisplayName).OrderByDescending(n => n.Length).First());
        var rows = new[] { L(lang, "combat.you_cast_spell", longest) };
        Capture($"datachars-longest-spell-{lang}.txt", string.Join("\n", rows.Append(L(lang, "combat.cast_spell_on_ally", longest, LongName))));
        EveryRowFits(rows, $"cast line with the longest spell name ({longest}) in {lang}");
        // The ally cast line (CombatEngine, combat.cast_spell_on_ally) is 80 in fr with the longest spell and a 30-character
        // ally; it is another piece's row and is listed in this piece's REPORT, not changed here.
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
            EveryRowFits(Rows(screen.Text), $"Inn skill screen in {lang}");
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

    // ---------- 2. companions ----------

    private static readonly CompanionId[] Companions = { CompanionId.Lyris, CompanionId.Aldric, CompanionId.Mira, CompanionId.Vex, CompanionId.Melodia };

    /// <summary>Runs body against a fresh CompanionSystem; the shared fallback instances it touches (companions,
    /// grief, the awakening) are put back after.</summary>
    private static T WithFreshCompanions<T>(Func<CompanionSystem, T> body)
    {
        var fields = new[] { typeof(CompanionSystem), typeof(GriefSystem), typeof(OceanPhilosophySystem) }
            .Select(t => t.GetField("_fallbackInstance", SNP)!).ToList();
        var previous = fields.Select(f => f.GetValue(null)).ToList();
        try
        {
            _ = new GriefSystem();
            _ = new OceanPhilosophySystem();
            return body(new CompanionSystem());
        }
        finally { for (int i = 0; i < fields.Count; i++) fields[i].SetValue(null, previous[i]); }
    }

    private static IEnumerable<(string english, Func<Companion, string> shown, string key)> CompanionTexts(Companion c)
    {
        string b = "companion." + c.Id.ToString().ToLowerInvariant();
        yield return (c.Title, x => x.LocTitle, b + ".title");
        yield return (c.Description, x => x.LocDescription, b + ".desc");
        yield return (c.BackstoryBrief, x => x.LocBackstory, b + ".backstory");
        yield return (c.PersonalQuestName, x => x.LocQuestName, b + ".quest_name");
        yield return (c.PersonalQuestDescription, x => x.LocQuestDescription, b + ".quest_desc");
        yield return (c.PersonalQuestLocationHint, x => x.LocQuestHint, b + ".quest_hint");
        for (int i = 0; i < c.DialogueHints.Length; i++)
        {
            int n = i;
            yield return (c.DialogueHints[n], x => x.LocDialogueHint(n), b + ".hint." + n);
        }
    }

    [Fact]
    public void EveryCompanionText_ShowsThroughItsKey_InFiveLanguages_AndTheTableKeepsEnglish()
    {
        WithFreshCompanions(system =>
        {
            foreach (var id in Companions)
            {
                var c = system.GetCompanion(id)!;
                c.Name.Should().Be(id.ToString(), "companion names are proper names, the same in every language");
                foreach (var (english, shown, key) in CompanionTexts(c))
                {
                    Loc.GetIn("en", key).Should().Be(english, $"{key} holds the table's English");
                    TranslatedInFiveLanguages(new[] { key });
                    foreach (var lang in AllLanguages)
                        InLang(lang, () => shown(c)).Should().Be(L(lang, key));
                }
                foreach (var ability in c.Abilities)
                {
                    var key = Companion.AbilityKey(ability);
                    key.Should().NotBeNull($"{ability} has a key");
                    TranslatedInFiveLanguages(new[] { key! }, new HashSet<string> { "ability.camouflage.name" });   // French writes it the same
                    InLang("hu", () => c.LocAbilities).Should().Contain(L("hu", key!));
                }
                // the English table text is what the companion keeps
                InLang("hu", () => c.Title).Should().Be(Loc.GetIn("en", "companion." + id.ToString().ToLowerInvariant() + ".title"));
            }
            return 0;
        });
        TranslatedInFiveLanguages(KeysIn(Src("Systems", "CompanionSystem.cs"), "companion."),
            new HashSet<string> { "companion.stat_mag" });
    }

    [Theory]
    [InlineData("en")]
    [InlineData("es")]
    [InlineData("fr")]
    [InlineData("hu")]
    [InlineData("it")]
    public async Task TheCompanionScreens_ShowTheReadersLanguage_AndFit79(string lang)
    {
        var all = new StringBuilder();
        foreach (var id in Companions)
        {
            var c = CompanionSystem.Instance.GetCompanion(id)!;
            // the recruitment scene (CompanionSystem)
            var scene = NewScreen();
            await InLanguage(lang, async () =>
            {
                await (Task)typeof(CompanionSystem).GetMethod("DisplayRecruitmentScene", F)!.Invoke(CompanionSystem.Instance, new object[] { c, scene.Term })!;
                return 0;
            });
            // the Inn: approach and hear the story, the summary row with the quest under way, the long talk
            var inn = NewScreen("T", "", "", "");
            bool started = c.PersonalQuestStarted;
            try
            {
                await InLanguage(lang, async () =>
                {
                    var loc = At(new InnLocation(), inn.Term, null);
                    await (Task)typeof(InnLocation).GetMethod("AttemptCompanionRecruitment", F)!.Invoke(loc, new object[] { c })!;
                    c.PersonalQuestStarted = true;
                    typeof(InnLocation).GetMethod("DisplayCompanionSummary", F)!.Invoke(loc, new object[] { c, true });
                    return 0;
                });
            }
            finally { c.PersonalQuestStarted = started; }

            string text = scene.Text + "\n" + inn.Text;
            all.AppendLine(text);
            EveryRowFits(Rows(text), $"{id} companion screens in {lang}");
            foreach (var expected in new[] { InLang(lang, () => c.LocTitle), InLang(lang, () => c.LocQuestName), InLang(lang, () => c.LocQuestHint), InLang(lang, () => c.LocAbilities[0]) })
                text.Should().Contain(expected);
            string flat = Regex.Replace(text, "\\s+", " ");
            foreach (var expected in new[] { InLang(lang, () => c.LocDescription), InLang(lang, () => c.LocBackstory), InLang(lang, () => c.LocDialogueHint(0)) })
                flat.Should().Contain(expected, "the wrapped rows hold the whole text");
            if (lang != "en")
                foreach (var (english, _, key) in CompanionTexts(c))
                    if (english.Length > 12 && !english.StartsWith("Dungeon floors")) flat.Should().NotContain(english, $"{key} shows in {lang}");
        }
        Capture($"datachars-companions-{lang}.txt", all.ToString());
    }

    [Theory]
    [InlineData("en")]
    [InlineData("hu")]
    [InlineData("it")]
    public void CompanionHistoryAndDeaths_StayEnglish_ThroughASaveAndReload_AndShowInTheReadersLanguage(string lang)
    {
        WithFreshCompanions(system =>
        {
            var lyris = system.GetCompanion(CompanionId.Lyris)!;
            var aldric = system.GetCompanion(CompanionId.Aldric)!;
            InLang(lang, () =>
            {
                foreach (var c in new[] { lyris, aldric }) { c.IsRecruited = true; c.LoyaltyLevel = 60; }
                system.StartPersonalQuest(CompanionId.Lyris).Should().BeTrue();
                system.CompletePersonalQuest(CompanionId.Lyris, false);
                system.AdvanceRomance(CompanionId.Lyris).Should().BeTrue();
                system.TriggerCompanionDeathByParadox("Aldric");
                return 0;
            });

            // saved and read back through the companion save data
            var json = JsonSerializer.Serialize(InLang(lang, system.Serialize));
            var reloaded = new CompanionSystem();
            reloaded.Deserialize(JsonSerializer.Deserialize<CompanionSystemData>(json)!);
            var back = reloaded.GetCompanion(CompanionId.Lyris)!;
            var history = back.History.Select(h => h.Description).ToList();
            history.Should().Contain(new[] { "Began personal quest: The Deepwood's Heart", "Quest failed: The Deepwood's Heart", "You caught her looking at you" },
                "the history is saved in English whatever the player's language");
            var death = reloaded.GetFallenCompanions().Single(f => f.Companion.Id == CompanionId.Aldric).Death;
            death.Circumstance.Should().Be("Died as a consequence of a moral choice");

            // and shown in the reader's language
            InLang(lang, () => back.History.Select(h => CompanionSystem.HistoryLabel(back, h)).ToList()).Should().Contain(new[]
            {
                L(lang, "companion.history_quest_started", L(lang, "companion.lyris.quest_name")),
                L(lang, "companion.history_quest_failed", L(lang, "companion.lyris.quest_name")),
                L(lang, "companion.romance_milestone.1"),
            });
            InLang(lang, () => CompanionSystem.CircumstanceLabel(death.Circumstance)).Should().Be(L(lang, "companion.death_reason_moral_choice"));
            // text another system wrote shows as stored
            InLang(lang, () => CompanionSystem.CircumstanceLabel("Slain by a Wolf in combat")).Should().Be("Slain by a Wolf in combat");
            return 0;
        });
    }

    [Fact]
    public void CompanionDeathTriggers_AndLevelUps_WriteTheirStoredTextInEnglish()
    {
        WithFreshCompanions(system =>
        {
            var vex = system.GetCompanion(CompanionId.Vex)!;
            vex.IsRecruited = true;
            vex.RecruitedDate = DateTime.UtcNow.AddDays(-400);
            var check = InLang("hu", () => system.CheckDeathTriggers(new Character { Name2 = "Zz Hero" }));
            check.TriggeredCompanion.Should().Be(CompanionId.Vex);
            check.TriggerReason.Should().Be("The disease has finally claimed Vex.", "the reason becomes the saved circumstance");
            InLang("hu", () => CompanionSystem.CircumstanceLabel(check.TriggerReason)).Should().Be(L("hu", "companion.death_reason_vex_disease"));
            return 0;
        });
        var src = Src("Systems", "CompanionSystem.cs");
        src.Should().Contain("ModifyLoyalty(companion.Id, 1, Loc.GetIn(\"en\", \"companion.history_level_combat\"))")
            .And.Contain("ModifyLoyalty(companion.Id, 1, Loc.GetIn(\"en\", \"companion.history_level_training\"))");
        InLang("hu", () => CompanionSystem.HistoryLabel(null!, new CompanionEvent { Description = "Leveled up through shared combat" }))
            .Should().Be(L("hu", "companion.history_level_combat"));
    }

    [Theory]
    [InlineData("en")]
    [InlineData("hu")]
    [InlineData("fr")]
    public void ALevelUpsStatGains_AndTheQuestNotice_Fit79(string lang)
    {
        var gains = InLang(lang, () => new[] { "ui.stat_atk", "stats.def", "companion.stat_spd", "companion.stat_mag", "companion.stat_heal", "stats.con", "stats.dex", "stats.agi", "stats.int", "stats.wis", "stats.cha", "ui.stat_hp" }
            .Select(k => $"{Loc.Get(k)} +{999}").ToList());
        var rows = CompanionSystem.StatGainRows(gains);
        rows.Count.Should().BeGreaterThan(1, "twelve gains do not fit one row");
        EveryRowFits(rows, "level-up stat gains");
        string.Join("  ", rows.Select(r => r.Trim())).Should().Be(string.Join("  ", gains));

        var vex = CompanionSystem.Instance.GetCompanion(CompanionId.Vex)!;
        var notice = InLang(lang, () =>
        {
            typeof(CompanionSystem).GetMethod("QueueQuestUnlockNotification", F)!.Invoke(CompanionSystem.Instance, new object[] { vex });
            return CompanionSystem.Instance.GetAndClearNotifications().Last();
        });
        EveryRowFits(Rows(notice), "quest notice");
        Regex.Replace(notice, "\\s+", " ").Should().Contain(L(lang, "companion.vex.quest_name")).And.Contain(L(lang, "companion.vex.quest_hint"));
    }

    // ---------- 3. boons and blessings ----------

    [Fact]
    public void EveryBoon_ShowsItsKeyedNameAndDescription_AndKeepsItsIdNameAndAlignments()
    {
        int longestEnglish = DivineBoonRegistry.AllBoons.Max(b => b.Name.Length);
        foreach (var boon in DivineBoonRegistry.AllBoons)
        {
            TranslatedInFiveLanguages(new[] { $"boon.{boon.Id}.name", $"boon.{boon.Id}.desc" });
            Loc.GetIn("en", $"boon.{boon.Id}.name").Should().Be(boon.Name);
            foreach (var lang in AllLanguages)
            {
                InLang(lang, () => boon.LocName).Should().Be(L(lang, $"boon.{boon.Id}.name"));
                InLang(lang, () => boon.LocName).Length.Should().BeLessOrEqualTo(longestEnglish, "no shown boon name is longer than the longest English one");
                InLang(lang, () => boon.LocDescription).Should().Be(L(lang, $"boon.{boon.Id}.desc"));
            }
            boon.Alignments.Except(new[] { "Light", "Dark", "Balance" }).Should().BeEmpty("alignments stay the English words");
        }
        TranslatedInFiveLanguages(KeysIn(Src("Systems", "DivineBoonRegistry.cs"), "boon."),
            new HashSet<string> { "boon.prose.sentence", "boon.effect.xp" });
        foreach (var t in new[] { 1, 2, 3 })
            foreach (var boon in DivineBoonRegistry.AllBoons.Select(b => b.Id).Append("blessing"))
                TranslatedInFiveLanguages(new[] { $"boon.prose.{boon}.{t}" });
    }

    [Theory]
    [InlineData("hu")]
    [InlineData("es")]
    [InlineData("it")]
    public void BoonEffectsAndProse_AreInTheReadersLanguage(string lang)
    {
        string config = DivineBoonRegistry.SerializeConfig(DivineBoonRegistry.AllBoons.Select((b, i) => (b.Id, i % 3 + 1)).ToList());
        var summary = InLang(lang, () => DivineBoonRegistry.GetEffectSummaryLines(config));
        summary.Should().HaveCount(DivineBoonRegistry.AllBoons.Count);
        summary[0].Should().StartWith(L(lang, "boon.warrior_fury.name") + " I -- ");
        var prose = InLang(lang, () => DivineBoonRegistry.GenerateDescription(config, "Light"));
        prose.Should().StartWith(L(lang, "boon.flavor.light")).And.Contain(L(lang, "boon.prose.shadow_strike.2")).And.Contain(L(lang, "boon.prose.mana_well.1"));
        InLang(lang, () => DivineBoonRegistry.GenerateDescription("", "Dark")).Should().Be(L(lang, "boon.prose.unconfigured", L(lang, "boon.flavor.dark")));
        string all = string.Join("\n", summary) + "\n" + prose;
        foreach (var english in new[] { " damage", "crit chance", "lifesteal", "shop discount", "flee chance", " luck", " attack", "empowers", "bestows", "grants", "radiant spirit", "minor", "moderate", "powerful" })
            all.Should().NotContain(english, $"the boon text shows in {lang}");
        // the English is unchanged
        InLang("en", () => DivineBoonRegistry.GetBoon("merchants_favor")!.GetEffectDescription(3)).Should().Be("10% shop discount");
        InLang("en", () => DivineBoonRegistry.GetBoon("warrior_fury")!.GetEffectDescription(2)).Should().Be("+10% damage");
        InLang("en", () => DivineBoonRegistry.GenerateDescription("warrior_fury:1,golden_touch:3,mana_well:2", "Balance")).Should().Be(
            "A spirit of harmony who walks between light and dark. Empowers followers with minor battle fury, and bestows powerful golden fortune, and grants moderate arcane reserves.");
    }

    [Theory]
    [InlineData("en")]
    [InlineData("es")]
    [InlineData("fr")]
    [InlineData("hu")]
    [InlineData("it")]
    public async Task ThePantheonBoonRows_ShowKeyedNames_AndFit79(string lang)
    {
        var god = new Character
        {
            Name1 = "zzgod", Name2 = LongName, Class = CharacterClass.Warrior, Level = 100, HP = 100, MaxHP = 100, AI = CharacterAI.Human,
            IsImmortal = true, DivineName = LongName, GodLevel = GameConfig.GodMaxLevel, GodAlignment = "Light",
            DivineBoonConfig = DivineBoonRegistry.SerializeConfig(DivineBoonRegistry.AllBoons.Take(5).Select(b => (b.Id, 2)).ToList()),
        };
        var screen = NewScreen("0");
        await InLanguage(lang, async () =>
        {
            var pantheon = At(new PantheonLocation(), screen.Term, god);
            await (Task)typeof(PantheonLocation).GetMethod("ConfigureBoons", F)!.Invoke(pantheon, Array.Empty<object>())!;
            return 0;
        });
        string text = screen.Text;
        Capture($"datachars-pantheon-{lang}.txt", text);
        // every row that shows a boon (the budget line above them is another piece's; see REPORT)
        var names = DivineBoonRegistry.AllBoons.Select(b => L(lang, $"boon.{b.Id}.name")).ToList();
        var boonRows = Rows(text).Where(r => names.Any(r.Contains)).ToList();
        boonRows.Count.Should().BeGreaterOrEqualTo(DivineBoonRegistry.AllBoons.Count);
        EveryRowFits(boonRows, $"boon rows in {lang}");
        foreach (var boon in DivineBoonRegistry.AllBoons)
            text.Should().Contain(L(lang, $"boon.{boon.Id}.name"));
        god.DivineBoonConfig.Should().Contain("warrior_fury:2", "the config keeps the boon ids");
    }

    [Theory]
    [InlineData("en")]
    [InlineData("es")]
    [InlineData("fr")]
    [InlineData("hu")]
    [InlineData("it")]
    public void BlessingNamesAndDescriptions_AreKeyed_AndFit79_WithALongGodName(string lang)
    {
        foreach (var kind in new[] { "protection", "fury", "favor", "daily" })
        {
            TranslatedInFiveLanguages(new[] { $"blessing.{kind}_name", $"blessing.{kind}_desc" });
            $"*** {L(lang, $"blessing.{kind}_name", LongName)} ***".Length.Should().BeLessOrEqualTo(MaxWidth);
            L(lang, $"blessing.{kind}_desc", 999).Length.Should().BeLessOrEqualTo(MaxWidth);
        }
        var src = Src("Systems", "DivineBlessingSystem.cs");
        src.Should().NotContain("'s Protection\"").And.NotContain("'s Daily Blessing\"").And.NotContain("\"Your morning prayers");
        InLang("en", () => Loc.Get("blessing.fury_name", "Maelketh")).Should().Be("Maelketh's Fury", "the English is unchanged");
    }

    // ---------- 4. character creation and specializations ----------

    private static readonly CharacterClass[] BaseClasses =
    {
        CharacterClass.Warrior, CharacterClass.Paladin, CharacterClass.Ranger, CharacterClass.Assassin, CharacterClass.Bard, CharacterClass.Jester,
        CharacterClass.Alchemist, CharacterClass.Magician, CharacterClass.Cleric, CharacterClass.Sage, CharacterClass.Barbarian, CharacterClass.MysticShaman,
    };

    [Theory]
    [InlineData("en")]
    [InlineData("es")]
    [InlineData("fr")]
    [InlineData("hu")]
    [InlineData("it")]
    public void ClassAbbreviations_AreThreeColumns_DistinctPerLanguage_AndTheRaceRowsFit79(string lang)
    {
        var abbrs = InLang(lang, () => BaseClasses.Select(CharacterCreationSystem.ClassAbbreviation).ToList());
        abbrs.Should().OnlyContain(a => a.Length == 3, $"each abbreviation is three columns in {lang}");
        abbrs.Should().OnlyHaveUniqueItems();
        InLang("en", () => BaseClasses.Select(CharacterCreationSystem.ClassAbbreviation).ToList())
            .Should().Equal("War", "Pal", "Ran", "Asn", "Brd", "Jst", "Alc", "Mag", "Clr", "Sge", "Bar", "Sha");

        var screen = NewScreen();
        InLang(lang, () =>
        {
            var creation = new CharacterCreationSystem(screen.Term);
            var show = typeof(CharacterCreationSystem).GetMethod("DisplayRaceOption", F)!;
            int n = 0;
            foreach (var race in Enum.GetValues<CharacterRace>())
                show.Invoke(creation, new object[] { n++, GameConfig.GetLocalizedRaceName(race), race, race == CharacterRace.Gnoll ? $"*{Loc.Get("creation.preview.poison_bite").ToLower()}" : race == CharacterRace.Troll ? $"*{Loc.Get("creation.preview.regen").ToLower()}" : "" });
            return 0;
        });
        Capture($"datachars-races-{lang}.txt", screen.Text);
        EveryRowFits(Rows(screen.Text), $"race list in {lang}");
        if (lang != "en") screen.Text.Should().NotContain("Jst");
    }

    [Theory]
    [InlineData("en")]
    [InlineData("hu")]
    [InlineData("fr")]
    public void StarterWeapons_StoreTheirEnglishName_ThroughASaveAndReload_AndShowTranslated(string lang)
    {
        foreach (var cls in BaseClasses)
        {
            var (name, _, _, _) = CharacterCreationSystem.StarterWeapon(cls);
            var key = ItemNames.KeyOf(name);
            key.Should().NotBeNull($"{name} shows through an item key");
            TranslatedInFiveLanguages(new[] { key! });
        }
        var hero = new Character { Name1 = "zzstarter" + lang, Name2 = "ZzStarter" + lang, Class = CharacterClass.Barbarian, Race = CharacterRace.Human, Level = 1, HP = 20, MaxHP = 20, AI = CharacterAI.Human };
        InLang(lang, () =>
        {
            typeof(CharacterCreationSystem).GetMethod("GiveStartingWeapon", F)!.Invoke(new CharacterCreationSystem(NewScreen().Term), new object[] { hero });
            return 0;
        });
        var weapon = hero.GetEquipment(EquipmentSlot.MainHand)!;
        weapon.Name.Should().Be("Crude Axe", "the item is created with its English name in every language");
        weapon.Description.Should().Be("A basic crude axe for new adventurers.");
        var data = InLang(lang, () => (PlayerData)typeof(SaveSystem).GetMethod("SerializePlayer", F)!.Invoke(SaveSystem.Instance, new object[] { hero })!);
        var json = JsonSerializer.Serialize(data);
        json.Should().Contain("Crude Axe");
        if (lang != "en") json.Should().NotContain(L(lang, "item.crude_axe"));
        InLang(lang, () => ItemNames.Display(weapon)).Should().Be(L(lang, "item.crude_axe"));
    }

    [Fact]
    public void EverySpecialization_ShowsItsKeyedName_AndTheSaveKeepsItsId()
    {
        var specs = Enum.GetValues<ClassSpecialization>().Where(s => s != ClassSpecialization.None).Select(SpecializationData.GetSpec).ToList();
        specs.Should().HaveCount(25).And.OnlyContain(s => s != null);
        var same = new HashSet<string> { "spec.warrior.protection.name", "spec.magician.destruction.name", "spec.mystic_shaman.chaos.name", "spec.bard.virtuoso.name", "spec.shaman.chaos.name" };
        foreach (var spec in specs)
        {
            spec!.NameKey.Should().EndWith(".name");
            Loc.GetIn("en", spec.NameKey).Should().Be(spec.Name);
            foreach (var lang in OtherLanguages)
            {
                Loc.HasIn(lang, spec.NameKey).Should().BeTrue();
                InLang(lang, () => spec.LocName).Should().Be(L(lang, spec.NameKey));
            }
            if (!same.Contains(spec.NameKey)) L("hu", spec.NameKey).Should().NotBe(spec.Name);
        }
        var hero = new Character { Name1 = "zzspec", Name2 = "ZzSpec", Class = CharacterClass.Ranger, Level = 20, HP = 50, MaxHP = 50, AI = CharacterAI.Human, Specialization = ClassSpecialization.Marksmanship };
        var data = InLang("hu", () => (PlayerData)typeof(SaveSystem).GetMethod("SerializePlayer", F)!.Invoke(SaveSystem.Instance, new object[] { hero })!);
        var back = JsonSerializer.Deserialize<PlayerData>(JsonSerializer.Serialize(data))!;
        JsonSerializer.Serialize(back).Should().NotContain(L("hu", "spec.ranger.marksmanship.name"));
        back.Specialization.Should().Be((int)ClassSpecialization.Marksmanship, "the save stores the specialization id");
        back.Class.Should().Be(CharacterClass.Ranger, "the save stores the class id");
        foreach (var src in new[] { Src("Locations", "TeamCornerLocation.cs"), Src("Locations", "LevelMasterLocation.cs") })
            Regex.IsMatch(src, @"(specDef|spec|cur|s|chosen)\??\.Name\b").Should().BeFalse("specialization names are shown through LocName");
    }

    [Fact]
    public void TheWikiExport_KeepsEnglishSpecNames_AndAddsTheOtherLanguages()
    {
        var dir = Path.Combine(Path.GetTempPath(), "dch125-wiki-" + Guid.NewGuid().ToString("N"));
        try
        {
            WikiDataExporter.Export(dir);
            using var classes = JsonDocument.Parse(File.ReadAllText(Path.Combine(dir, "classes.json")));
            int seen = 0;
            foreach (var cls in classes.RootElement.GetProperty("data").EnumerateArray())
                foreach (var spec in cls.GetProperty("specializations").EnumerateArray())
                {
                    var def = SpecializationData.GetSpec(Enum.Parse<ClassSpecialization>(spec.GetProperty("id").GetString()!))!;
                    var name = spec.GetProperty("name");
                    name.EnumerateObject().Select(p => p.Name).Should().Equal(AllLanguages);
                    name.GetProperty("en").GetString().Should().Be(def.Name, "the English export is unchanged");
                    name.GetProperty("hu").GetString().Should().Be(L("hu", def.NameKey));
                    seen++;
                }
            seen.Should().Be(25);
        }
        finally { try { Directory.Delete(dir, true); } catch { } }
    }

    // ---------- 5. relationships and the visual novel ----------

    private static (NPC npc, Character player) Couple(string tag)
    {
        var player = new Character { Name1 = "Zz" + tag + "P", Name2 = "Zz" + tag + "P", Sex = CharacterSex.Male, Age = 30, AI = CharacterAI.Human, Level = 10, HP = 50, MaxHP = 50, ID = "zz" + tag + "p" };
        var npc = new NPC { Name1 = "Zz" + tag + "N", Name2 = "Zz" + tag + "N", Sex = CharacterSex.Female, Age = 30, Level = 10, HP = 50, MaxHP = 50, ID = "zz" + tag + "n", IntimacyActs = 3 };
        var rel = RelationshipSystem.GetOrCreateRelationship(npc, player);
        rel.Relation1 = GameConfig.RelationLove;
        rel.Relation2 = GameConfig.RelationLove;
        rel.CreatedOnGameDay = -1000;
        return (npc, player);
    }

    [Theory]
    [InlineData("hu")]
    [InlineData("es")]
    public void AMarriageAndADivorce_InAnotherLanguage_StoreStagesAndTheReasonInEnglish(string lang)
    {
        var (npc, player) = Couple("Wed" + lang);
        string wed = "", divorce = "";
        try
        {
            InLang(lang, () => RelationshipSystem.PerformMarriage(npc, player, out wed)).Should().BeTrue(wed);
            wed.Should().Contain(L(lang, "relationship.wedding_complete")).And.Contain(L(lang, "relationship.now_married", npc.Name, player.Name));
            var rel = RelationshipSystem.GetOrCreateRelationship(npc, player);
            rel.Relation1.Should().Be(GameConfig.RelationMarried, "the stage is stored as its number");
            player.SpouseName.Should().Be(npc.Name2);

            InLang(lang, () => RelationshipSystem.ProcessDivorce(player, npc, out divorce)).Should().BeTrue();
            divorce.Should().Contain(L(lang, "relationship.divorce_finalized")).And.Contain(L(lang, "relationship.lost_custody"));
            rel.Relation1.Should().Be(GameConfig.RelationAnger);
            var ex = RomanceTracker.Instance.ExSpouses.Single(e => e.NPCId == npc.ID);
            ex.DivorceReason.Should().Be("Divorce", "the stored divorce reason stays English");

            // the relationship records and the ex-spouse record keep their stored forms through a save
            var saved = JsonSerializer.Serialize(RelationshipSystem.ExportAllRelationships());
            saved.Should().NotContain(L(lang, "relationship.divorce_finalized"));
            JsonSerializer.Deserialize<List<RelationshipSaveData>>(saved)!.Should().Contain(r => r.Relation1 == GameConfig.RelationAnger || r.Relation2 == GameConfig.RelationAnger);
        }
        finally
        {
            RomanceTracker.Instance.ExSpouses.RemoveAll(e => e.NPCId == npc.ID);
            RomanceTracker.Instance.Exes.Remove(npc.ID);
            RomanceTracker.Instance.Spouses.RemoveAll(s => s.NPCId == npc.ID);
        }
    }

    [Theory]
    [InlineData("en")]
    [InlineData("es")]
    [InlineData("fr")]
    [InlineData("hu")]
    [InlineData("it")]
    public void RelationshipMessages_AreKeyed_AndEveryRowFits79_WithTwoLongNames(string lang)
    {
        var keys = KeysIn(Src("Systems", "RelationshipSystem.cs"), "relationship.");
        TranslatedInFiveLanguages(keys);
        var rows = new List<string>();
        foreach (var key in keys.Where(k => !k.StartsWith("relationship.reject_")))
            rows.AddRange(RelationshipSystem.MessageRows("\"" + L(lang, key, LongName, LongName, 9999) + "\""));
        string wedding = L(lang, "relationship.wedding_complete") + "\n" + L(lang, "relationship.now_married", LongName, LongName) + "\n" + L(lang, "wedding.ceremony_msg_1") + "\n" + L(lang, "relationship.congrats_adopt_babies");
        rows.AddRange(RelationshipSystem.MessageRows(wedding));
        // the proposal refusals, each level and gender
        var reject = typeof(RelationshipSystem).GetMethod("GetProposalRejectionMessage", SNP)!;
        foreach (var sex in new[] { CharacterSex.Female, CharacterSex.Male })
            foreach (var chance in new[] { 20, 40, 80 })
            {
                var npc = new NPC { Name1 = LongName, Name2 = LongName, Sex = sex };
                string m = InLang(lang, () => (string)reject.Invoke(null, new object[] { npc, chance })!);
                m.Should().StartWith(LongName);
                rows.AddRange(RelationshipSystem.MessageRows($"\"{m}\""));
                if (lang == "en" && chance == 40) m.Should().Contain(sex == CharacterSex.Female ? "but her eyes" : "but his eyes");
            }
        EveryRowFits(rows, $"relationship messages in {lang}");
        L("en", "relationship.now_married", "A", "B").Should().Be("A and B are now married!");
    }

    [Fact]
    public void TheVisualNovelFallbacks_AreKeyed_AndTheQuestInitiatorStaysStored()
    {
        TranslatedInFiveLanguages(new[] { "dialogue.vn.winks_prefix", "dialogue.vn.their_spouse" });
        var src = Src("Systems", "VisualNovelDialogueSystem.cs");
        src.Should().NotContain("\"*winks* \"").And.NotContain("\"their spouse\"");
        src.Should().Contain("Initiator = npc.Name2 ?? npc.Name1 ?? \"An ally\"", "the initiator is stored on the saved quest");
    }
}
