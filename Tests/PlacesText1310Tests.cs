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
using UsurperRemake.Locations;
using UsurperRemake.Systems;
using UsurperReborn.Tests.Localization;
using Xunit;

namespace UsurperReborn.Tests;

/// <summary>
/// 1.3.10 audit, places text: the dungeon tutorial names the keys the combat and room handlers take; the Loaded Dice
/// label describes its game; the tax relief petition shows gold per citizen; and the small text and menu fixes (the
/// failed siege line, the castle screen reader menus, the Progress tier, the tax and team tips, the Inn food line,
/// Love Street prompts, Church alignment words, Temple pointers, expert prompts, the castle rumour, the Electron menu
/// data). Every changed row fits 79 columns in English and Hungarian.
/// </summary>
[Collection("SharedGameSingletons")]
public class PlacesText1310Tests : IDisposable
{
    private const BindingFlags F = BindingFlags.NonPublic | BindingFlags.Instance;
    private const int MaxWidth = 79;
    private static readonly string[] Langs = { "en", "es", "fr", "hu", "it" };

    private readonly King? _kingBefore = CastleLocation.GetCurrentKing();
    private readonly string _langBefore = GameConfig.Language;
    private readonly bool _srBefore = GameConfig.ScreenReaderMode;

    public void Dispose()
    {
        CastleLocation.SetKing(_kingBefore!);
        GameConfig.Language = _langBefore;
        GameConfig.ScreenReaderMode = _srBefore;
    }

    private static string RepoRoot() => HardcodedTextScannerTests.RepoRoot();
    private static string Src(string rel) => File.ReadAllText(Path.Combine(RepoRoot(), rel));

    private static Dictionary<string, string> Table(string lang) =>
        JsonSerializer.Deserialize<Dictionary<string, JsonElement>>(File.ReadAllText(Path.Combine(RepoRoot(), $"Localization/{lang}.json")))!
            .Where(kv => kv.Value.ValueKind == JsonValueKind.String).ToDictionary(kv => kv.Key, kv => kv.Value.GetString()!);

    private static List<string> Bracketed(string text) => Regex.Matches(text, @"\[([^\]\s]{1,3})\]").Select(m => m.Groups[1].Value).ToList();

    private static (TerminalEmulator Term, MemoryStream Output) Stream(params string[] lines)
    {
        var output = new MemoryStream();
        return (new TerminalEmulator(new LineStream(lines.Concat(Enumerable.Repeat("", 20))), output), output);
    }

    private static string Plain(TerminalEmulator term, MemoryStream output)
    {
        term.StreamWriterInternal?.Flush();
        return Regex.Replace(Encoding.UTF8.GetString(output.ToArray()), "\u001b\\[[0-9;?]*[A-Za-z]", "").Replace("\r", "");
    }

    private static T At<T>(T loc, Character p, TerminalEmulator term) where T : BaseLocation
    {
        typeof(BaseLocation).GetField("terminal", F)!.SetValue(loc, term);
        typeof(BaseLocation).GetField("currentPlayer", F)!.SetValue(loc, p);
        return loc;
    }

    private static async Task<object?> Call(object target, string method, params object[] args)
    {
        var m = target.GetType().GetMethod(method, F) ?? typeof(BaseLocation).GetMethod(method, F);
        m.Should().NotBeNull($"{method} must exist");
        var r = m!.Invoke(target, args);
        if (r is Task t) { await t; return t.GetType().GetProperty("Result")?.GetValue(t); }
        return r;
    }

    private static void EveryRowFits(string text, string what)
    {
        foreach (var row in text.Split('\n'))
            row.Length.Should().BeLessOrEqualTo(MaxWidth, $"{what}: \"{row}\"");
    }

    /// <summary>The source of a method from its signature to the next member at the same indent.</summary>
    private static string Body(string src, string signature) => TimedStatBuffs1115Tests.Body(src, signature);

    // ================= E4: the dungeon tutorial =================

    private static async Task<string> Tutorial(string lang)
    {
        var prev = GameConfig.Language;
        try
        {
            GameConfig.Language = lang;
            var (term, output) = Stream("Y");
            var loc = new DungeonLocation();
            var mi = typeof(DungeonLocation).GetMethod("RunDungeonTutorial", F)!;
            var hero = new Character { Name1 = "Tut", Name2 = "Tut", Class = CharacterClass.Warrior, Level = 1, HP = 20, MaxHP = 20 };
            (await (Task<bool>)mi.Invoke(loc, new object[] { hero, term })!).Should().BeTrue();
            return Plain(term, output);
        }
        finally { GameConfig.Language = prev; }
    }

    [Theory]
    [InlineData("en")] [InlineData("es")] [InlineData("fr")] [InlineData("hu")] [InlineData("it")]
    public async Task DungeonTutorial_TeachesTheKeysTheHandlersTake(string lang)
    {
        string text = await Tutorial(lang);
        var t = Table(lang);

        text.Should().Contain(t["dungeon.tut.p3.s"]).And.Contain(t["dungeon.tut.p3.e"]).And.Contain(t["dungeon.tut.p4.l2"]);
        Bracketed(t["dungeon.tut.p3.s"]).Should().Equal("E");     // Precise Strike
        Bracketed(t["dungeon.tut.p3.e"]).Should().Equal("W");     // Disarm
        Bracketed(t["dungeon.tut.p3.num"]).Should().Equal("1-9"); // spells and abilities from the quickbar
        Bracketed(t["dungeon.tut.p4.l2"]).Should().Equal("I");    // a potion in combat
        Bracketed(t["dungeon.tut.p8.quit"]).Should().Equal("Q");

        // no key the handlers do not take: no S or C in combat, no U for a potion, no B room key, no room number
        var keys = Bracketed(text);
        keys.Should().NotContain(new[] { "S", "C", "U", "B" });
        t.Keys.Should().NotContain(new[] { "dungeon.tut.p1.move3", "dungeon.tut.p3.c", "dungeon.tut.p8.back" });
        if (lang is "en" or "hu") EveryRowFits(text, $"{lang} tutorial");
    }

    [Fact]
    public void DungeonTutorial_CombatKeys_AreTheCombatHandlersKeys()
    {
        string ce = Src("Scripts/Systems/CombatEngine.cs");
        Regex.IsMatch(ce, @"case ""E"":\s*action\.Type = CombatActionType\.PreciseStrike").Should().BeTrue("E is Precise Strike");
        Regex.IsMatch(ce, @"case ""W"":\s*action\.Type = CombatActionType\.Disarm").Should().BeTrue("W is Disarm");
        Regex.IsMatch(ce, @"case ""I"":\s*bool hasHealPots").Should().BeTrue("I uses a potion");
        Regex.IsMatch(ce, @"case ""9"":\s*var qbAction = await HandleQuickbarAction").Should().BeTrue("1 to 9 are the quickbar");
        string dl = Src("Scripts/Locations/DungeonLocation.cs");
        Body(dl, "private async Task<bool> RunDungeonTutorial(").Should().NotContain("dungeon.tut.p3.c")
            .And.NotContain("dungeon.tut.p8.back").And.NotContain("dungeon.tut.p1.move3");
    }

    // ================= E7: Loaded Dice =================

    [Fact]
    public void LoadedDiceLabel_DescribesTheGameItPlays()
    {
        string src = Body(Src("Scripts/Locations/DarkAlleyLocation.cs"), "private async Task<(bool won, long winnings)> PlayLoadedDice(");
        src.Should().Contain("(30, 14)").And.Contain("(20, 12)").And.Contain("(14, 10)");   // 3.0x, 2.0x, 1.4x
        foreach (var lang in Langs)
        {
            string label = Table(lang)["dark_alley.game_dice"];
            Regex.IsMatch(label, @"3x/2x/1[.,]4x").Should().BeTrue($"{lang}: {label}");
            label.Should().NotContain("1.8").And.NotContain("1,8").And.NotContain("7", $"{lang} has no over or under 7");
            ("[1] " + label).Length.Should().BeLessOrEqualTo(MaxWidth, lang);
        }
    }

    // ================= E8: tax relief petition =================

    private static Character Petitioner(long gold) => new()
    {
        Name1 = "Petitioner", Name2 = "Petitioner", Level = 10, Chivalry = 400, Fame = 400, Gold = gold, HP = 100, MaxHP = 100,
        AI = CharacterAI.Human,
    };

    [Theory]
    [InlineData("en")] [InlineData("hu")]
    public async Task TaxReliefPetition_ShowsGoldPerCitizen(string lang)
    {
        var king = King.CreateNewKing("Tax Monarch", CharacterAI.Computer, CharacterSex.Female);
        king.TaxRate = 20;
        CastleLocation.SetKing(king);
        var hero = Petitioner(1_000_000);
        GameConfig.Language = lang;
        var (term, output) = Stream("Y");
        var castle = At(new CastleLocation(), hero, term);

        await Call(castle, "AudiencePetitionTaxRelief");

        string text = Plain(term, output);
        text.Should().Contain(Loc.GetIn(lang, "castle.tax_current_rate", 20L));
        text.Should().Contain(Loc.GetIn(lang, "castle.tax_rate_reduced", 20L, 15L));
        CastleLocation.GetCurrentKing().TaxRate.Should().Be(15);
        foreach (var key in new[] { "castle.tax_current_rate", "castle.tax_rate_reduced", "castle.news_tax_relief" })
            foreach (var l in Langs)
                Table(l)[key].Should().NotContain("%", $"{l} {key}: the citizen tax is gold per citizen");
        EveryRowFits(text, $"{lang} petition");
    }

    // ================= E10 =================

    [Fact]
    public void FailedSiege_PrintsTheTally_NotTheSuccessLine()
    {
        string src = Src("Scripts/Locations/CastleLocation.cs");
        int failed = src.IndexOf("castle.siege_team_retreats", StringComparison.Ordinal);
        int lostHalf = src.IndexOf("castle.siege_lost_half_hp", failed, StringComparison.Ordinal);
        string branch = src.Substring(failed, lostHalf - failed);
        branch.Should().Contain("castle.siege_guards_beaten").And.NotContain("castle.siege_guards_defeated");
        foreach (var lang in Langs)
        {
            string line = Loc.GetIn(lang, "castle.siege_guards_beaten", 2, 5);
            line.Should().Contain("2/5").And.NotContain(Table(lang)["castle.siege_guards_defeated"].Trim());
        }
    }

    private static List<string> SrKeys(string text) =>
        text.Split('\n').Select(r => Regex.Match(r.TrimStart(), @"^([A-Z0-9])\.\s")).Where(m => m.Success).Select(m => m.Groups[1].Value).ToList();

    [Fact]
    public async Task CastleScreenReaderMenus_ShowTheKeysTheVisualMenusShow()
    {
        CastleLocation.SetKing(King.CreateNewKing("Menu Monarch", CharacterAI.Computer, CharacterSex.Female));
        var hero = Petitioner(100);
        hero.ScreenReaderMode = true;
        GameConfig.ScreenReaderMode = true;

        var (term, output) = Stream();
        await Call(At(new CastleLocation(), hero, term), "ShowRoyalMenu");
        SrKeys(Plain(term, output)).Should().Contain(new[] { "V", "Y", "Z" }, "the royal screen reader menu lists the statues, the heir and the bloodlines");

        (term, output) = Stream();
        await Call(At(new CastleLocation(), hero, term), "ShowCastleExteriorMenu");
        SrKeys(Plain(term, output)).Should().Contain(new[] { "V", "Z" }, "the commoner screen reader menu lists the statues and the bloodlines");
    }

    [Theory]
    [InlineData("en")] [InlineData("es")] [InlineData("fr")] [InlineData("hu")] [InlineData("it")]
    public void AwakeningPointers_SayProgressOpensAtTheFullMenuTier(string lang)
    {
        foreach (var key in new[] { "base.stat_awakening_hint", "hint.awakening.msg" })
        {
            string text = Loc.GetIn(lang, key, GameConfig.MenuTier3Level);
            text.Should().Contain(GameConfig.MenuTier3Level.ToString(), $"{lang} {key} names the level Progress opens");
        }
        ("  " + Loc.GetIn(lang, "base.stat_awakening_hint", GameConfig.MenuTier3Level)).Length.Should().BeLessOrEqualTo(MaxWidth);
        Src("Scripts/Locations/BaseLocation.cs").Should().Contain("\"base.stat_awakening_hint\", currentPlayer), GameConfig.MenuTier3Level)")
            .And.Contain("HINT_AWAKENING, terminal, currentPlayer, GameConfig.MenuTier3Level)");
        Src("Scripts/Systems/AwakeningScreens.cs").Should().Contain("HINT_AWAKENING, term, player, GameConfig.MenuTier3Level)");
    }

    [Fact]
    public void TaxAndTeamTips_StateWhatTheGameDoes()
    {
        foreach (var lang in Langs)
        {
            var t = Table(lang);
            t["hint.first_purchase_tax.msg"].Should().NotContain("5%").And.NotContain("2%", $"{lang}: the ruler sets both rates");
            t["hint.team_combat.msg"].Should().Contain("15%");
        }
        Table("en")["hint.team_combat.msg"].Should().Contain("story companion").And.Contain("grouped player");
        // the bonus the tip describes: a grouped player or a story companion
        Src("Scripts/Systems/CombatEngine.cs").Should().Contain("t.IsAlive && (t.IsGroupedPlayer || t.IsCompanion)");
        // the tip line in CombatEngine is unchanged
        Src("Scripts/Systems/CombatEngine.cs").Should().Contain("HintSystem.Instance.TryShowHint(HintSystem.HINT_TEAM_COMBAT, terminal, player.HintsShown);");
    }

    [Fact]
    public void InnScreenReaderFood_NamesNoWrongPrice()
    {
        Src("Scripts/Locations/InnLocation.cs").Should().NotContain("{Loc.Get(\"inn.order_food\")} (10 ");
    }

    [Theory]
    [InlineData("en")] [InlineData("es")] [InlineData("fr")] [InlineData("hu")] [InlineData("it")]
    public void LoveStreetPrompts_MatchWhatTheyTake(string lang)
    {
        Loc.GetIn(lang, "love_street.what_buy", LoveStreetLocation.Gifts.Length).Should().Contain($"1-{LoveStreetLocation.Gifts.Length}");
        Src("Scripts/Locations/LoveStreetLocation.cs").Should().Contain("Loc.Get(\"love_street.what_buy\", gifts.Length)");
        var en = Table("en");
        en["love_street.give_to"].Should().Contain("number").And.NotContain("name");
        en["love_street.investigate_who"].Should().Contain("number").And.NotContain("name");
        Table(lang)["love_street.give_to"].Should().NotBe(Table(lang)["love_street.investigate_who"]);
    }

    [Theory]
    [InlineData(100, 50, "church.align_good")]
    [InlineData(100, 100, "church.align_neutral")]
    [InlineData(100, 150, "church.align_evil")]
    [InlineData(100, 250, "church.align_very_evil")]
    [InlineData(100, 400, "church.align_demonic")]
    [InlineData(0, 10, "church.align_demonic")]
    [InlineData(400, 100, "church.align_saintly")]
    public void ChurchAlignmentWords_MirrorTheGoodSide(long chivalry, long darkness, string key)
    {
        var church = new ChurchLocation();
        var p = new Character { Name2 = "Pilgrim", Chivalry = chivalry, Darkness = darkness };
        var m = typeof(ChurchLocation).GetMethod("GetAlignmentDescription", F)!;
        ((string)m.Invoke(church, new object[] { p })!).Should().Be(Loc.Get(key));
    }

    [Fact]
    public void TemplePointers_NameKeysThatWork()
    {
        foreach (var lang in Langs)
        {
            var t = Table(lang);
            t["temple.must_worship_immortal"].Should().Contain("[W]").And.NotContain("[J]");
            t["temple.must_worship_immortal_sr"].Should().NotContain(" J ");
            t["temple.moved.faith"].Should().Contain("F").And.Contain("C");
        }
        Table("en")["temple.moved.faith"].Should().Contain("sworn to the Faith");
        // W in the Nave is worship, and the Cloister (C) is offered only with the Faith's access
        string src = Src("Scripts/Locations/TempleLocation.cs");
        src.Should().Contain("items.Add(Item(\"W\", \"temple.nave.worship\"");
        src.Should().Contain("if (FactionSystem.Instance?.HasTempleAccess() == true)");
    }

    [Fact]
    public void ExpertPrompts_ListTheKeysTheScreensTake()
    {
        string prison = Body(Src("Scripts/Locations/PrisonLocation.cs"), "private async Task<bool> ProcessPrisonChoice(");
        var prisonCases = Regex.Matches(prison, @"case '(.)':").Select(m => m.Groups[1].Value).Where(k => k != "V").ToHashSet();
        string healer = Src("Scripts/Locations/HealerLocation.cs");
        foreach (var lang in Langs)
        {
            var listed = Regex.Match(Table(lang)["prison.prompt_expert"], @"\(([^)]*)\)").Groups[1].Value.Split(',').ToHashSet();
            listed.Should().BeEquivalentTo(prisonCases, $"{lang}: the prison prompt lists the keys it takes (V only shows with Vex)");
            var healerKeys = Regex.Match(Table(lang)["healer.expert_prompt"], @"\(([^)]*)\)").Groups[1].Value.Split(',');
            foreach (var k in healerKeys) healer.Should().Contain($"case \"{k}\":", $"{lang} healer key {k}");
            healerKeys.Should().Contain("N", "Buy Antidotes is taken");
        }
    }

    [Fact]
    public void CastleRumour_NamesNoPassage()
    {
        foreach (var lang in Langs)
            Table(lang)["base.rumor_castle"].ToLowerInvariant().Should()
                .NotContainAny(new[] { "passage", "pasaje", "átjáró", "passaggio" }, $"{lang}: the castle has no secret passage");
    }

    [Fact]
    public void ElectronMenus_UseTheKeysTheHandlersTake()
    {
        string bank = Src("Scripts/Locations/BankLocation.cs");
        bank.Should().Contain("Key = currentPlayer.BankGuard ? \"X\" : \"G\"");
        bank.Should().Contain("case \"X\":").And.Contain("await ResignGuardDuty();");
        string st = Src("Scripts/Locations/SettlementLocation.cs");
        st.Should().NotContain("Key = \"T\", Label = Loc.Get(\"settlement.electron_talk\")")
            .And.NotContain("Key = \"O\", Label = Loc.Get(\"settlement.electron_oracle\")")
            .And.Contain("Key = \"S\", Label = Loc.Get(\"settlement.electron_services\")")
            .And.Contain("case \"S\":");
        foreach (var lang in Langs) Table(lang).Should().ContainKey("settlement.electron_services");
    }
}
