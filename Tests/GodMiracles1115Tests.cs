using System;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text.Json;
using System.Text.RegularExpressions;
using FluentAssertions;
using UsurperRemake.Systems;
using Xunit;

namespace UsurperReborn.Tests;

/// <summary>
/// 1.2.0 Temple gods piece 5: Miracles. A Chosen follower has one Miracle a day from their god's
/// domain (a player-god's chosen domain for its followers); the day's use is saved and cleared by
/// the daily reset. Mortis's Miracle cheats death once that day, by itself.
/// </summary>
[Collection("SharedGameSingletons")]
public class GodMiracles1115Tests
{
    private static Character Hero(string name) => new Character
    {
        Name1 = name, Name2 = name, AI = CharacterAI.Human, Level = 10, HP = 100, MaxHP = 100,
        BaseMaxHP = 100, BaseStrength = 10, BaseDexterity = 10, BaseConstitution = 10, BaseIntelligence = 20,
        BaseWisdom = 20, BaseCharisma = 10, BaseDefence = 5, BaseStamina = 10, BaseAgility = 10,
        Mental = 80, Class = CharacterClass.Warrior, Race = CharacterRace.Human
    };

    /// <summary>A hero worshipping god with the given Favor, in its own GodSystem.</summary>
    internal static (Character c, GodSystem gods) Worshipper(string name, string god, int favor)
    {
        var gods = new GodSystem();
        var c = Hero(name);
        GodRegistry.SetWorshippedGod(c, god, gods).Should().BeTrue();
        c.GodFavor = favor;
        FavorSystem.Bind(c, gods);
        c.GodFavor.Should().Be(favor);
        c.MiracleUsedToday = false;
        return (c, gods);
    }

    private static string RepoRoot()
    {
        var dir = AppContext.BaseDirectory;
        while (dir != null && !File.Exists(Path.Combine(dir, "UsurperReborn.sln"))) dir = Path.GetDirectoryName(dir);
        return dir!;
    }

    internal static string Source(string file) => File.ReadAllText(Path.Combine(RepoRoot(), file));

    /// <summary>The text of a method from its signature to the next member at the same indent.</summary>
    internal static string Body(string file, string signature)
    {
        string src = Source(file);
        int start = src.IndexOf(signature, StringComparison.Ordinal);
        start.Should().BeGreaterThanOrEqualTo(0, $"{signature} must exist in {file}");
        var next = new Regex(@"\n    (private|public|internal|protected) ").Match(src, start + signature.Length);
        return next.Success ? src.Substring(start, next.Index - start) : src.Substring(start);
    }

    internal static int Count(string text, string part) => Regex.Matches(text, Regex.Escape(part)).Count;

    // ---------------- Who has a Miracle ----------------

    [Theory]
    [InlineData("Solarius", GodDomain.Light)]
    [InlineData("Valorian", GodDomain.War)]
    [InlineData("Amara", GodDomain.Love)]
    [InlineData("Judicar", GodDomain.Law)]
    [InlineData("Umbrath", GodDomain.Shadow)]
    [InlineData("Terran", GodDomain.Earth)]
    [InlineData("Mortis", GodDomain.Death)]
    [InlineData("Arcanus", GodDomain.Magic)]
    [InlineData("Sylvana", GodDomain.Nature)]
    [InlineData("Discordia", GodDomain.Chaos)]
    public void AChosenFollower_HasTheMiracleOfTheirGodsDomain(string god, GodDomain domain)
    {
        var (c, gods) = Worshipper("GmChosen" + god, god, GameConfig.GodFavorTierChosenMin);
        MiracleSystem.GetMiracle(c, gods).Should().Be(domain);
        MiracleSystem.IsReady(c, gods).Should().BeTrue();
        MiracleSystem.IsCalled(domain).Should().Be(domain != GodDomain.Death, "Mortis's Miracle acts by itself");
    }

    [Theory]
    [InlineData(0)]
    [InlineData(25)]
    [InlineData(50)]
    [InlineData(74)]
    public void BelowChosen_ThereIsNoMiracle(int favor)
    {
        var (c, gods) = Worshipper("GmBelow" + favor, "Terran", favor);
        MiracleSystem.GetMiracle(c, gods).Should().Be(GodDomain.None);
        MiracleSystem.IsReady(c, gods).Should().BeFalse();
        MiracleSystem.TryConsume(c, gods).Should().BeFalse();
        c.MiracleUsedToday.Should().BeFalse("nothing is spent without a Miracle");
    }

    [Fact]
    public void NoGod_OrAnNpc_HasNoMiracle()
    {
        var gods = new GodSystem();
        var godless = Hero("GmGodless");
        MiracleSystem.GetMiracle(godless, gods).Should().Be(GodDomain.None);

        var npc = new Character { Name1 = "GmNpc", Name2 = "GmNpc", AI = CharacterAI.Computer, WorshippedGod = "Zephyrine", GodFavor = 90, GodFavorGod = "Zephyrine" };
        MiracleSystem.GetMiracle(npc, gods).Should().Be(GodDomain.None);
        npc.HP = 0;
        MiracleSystem.TryCheatDeath(npc, gods).Should().BeFalse();
    }

    [Theory]
    [InlineData(GodDomain.Nature)]
    [InlineData(GodDomain.Death)]
    [InlineData(GodDomain.Law)]
    public void APlayerGodsChosenFollower_GetsTheMiracleOfTheDomainTheImmortalChose(GodDomain chosen)
    {
        var (c, gods) = Worshipper("GmPlayerGod" + chosen, "Zephyrine", 80);
        GodBoonSystem.SetPlayerGodBoon(c, "Zephyrine", chosen, 60);
        MiracleSystem.GetMiracle(c, gods).Should().Be(chosen);

        // the cache of another god does not count
        GodBoonSystem.SetPlayerGodBoon(c, "Korvessa", chosen, 60);
        MiracleSystem.GetMiracle(c, gods).Should().Be(GodDomain.None);
    }

    // ---------------- Once a day ----------------

    [Fact]
    public void TheMiracle_IsOnceADay_AndTheDailyResetReadiesItAgain()
    {
        var (c, gods) = Worshipper("GmOnce", "Arcanus", 90);
        MiracleSystem.TryConsume(c, gods).Should().BeTrue();
        c.MiracleUsedToday.Should().BeTrue();
        MiracleSystem.IsReady(c, gods).Should().BeFalse();
        MiracleSystem.TryConsume(c, gods).Should().BeFalse("one a day");

        MiracleSystem.ApplyDailyReset(c);
        c.MiracleUsedToday.Should().BeFalse();
        MiracleSystem.IsReady(c, gods).Should().BeTrue();
        MiracleSystem.TryConsume(c, gods).Should().BeTrue();
    }

    [Fact]
    public void TheDailyReset_SkipsNpcs()
    {
        var npc = new Character { Name1 = "GmNpcReset", Name2 = "GmNpcReset", AI = CharacterAI.Computer, MiracleUsedToday = true };
        MiracleSystem.ApplyDailyReset(npc);
        npc.MiracleUsedToday.Should().BeTrue();
    }

    [Fact]
    public void TheDailyReset_HasOneProductionCaller_InRunBasicDailyReset()
    {
        string daily = Source("Scripts/Systems/DailySystemManager.cs");
        Count(daily, "MiracleSystem.ApplyDailyReset(player)").Should().Be(1);
        int basic = daily.IndexOf("private async Task RunBasicDailyReset()", StringComparison.Ordinal);
        int favor = daily.IndexOf("FavorSystem.ApplyDailyReset(player)", StringComparison.Ordinal);
        int miracle = daily.IndexOf("MiracleSystem.ApplyDailyReset(player)", StringComparison.Ordinal);
        basic.Should().BeGreaterThan(0);
        miracle.Should().BeGreaterThan(favor).And.BeGreaterThan(basic);

        var scripts = Directory.GetFiles(Path.Combine(RepoRoot(), "Scripts"), "*.cs", SearchOption.AllDirectories);
        scripts.Sum(f => Count(File.ReadAllText(f), "MiracleSystem.ApplyDailyReset(")).Should().Be(1);
    }

    [Fact]
    public void AUsedMiracle_SurvivesSaveAndLoad()
    {
        var (c, gods) = Worshipper("GmSave", "Valorian", 80);
        MiracleSystem.TryConsume(c, gods).Should().BeTrue();

        var method = typeof(SaveSystem).GetMethod("SerializePlayer", BindingFlags.NonPublic | BindingFlags.Instance)!;
        var data = (PlayerData)method.Invoke(SaveSystem.Instance, new object[] { c })!;
        data.MiracleUsedToday.Should().BeTrue();
        var back = JsonSerializer.Deserialize<PlayerData>(JsonSerializer.Serialize(data))!;
        var restored = MenuKeysNeedEnterPref1115Tests.Restore(back);
        var loadGods = new GodSystem();
        loadGods.SetPlayerGod(restored.Name2, "Valorian");
        GodRegistry.ApplyLoad(restored, loadGods);

        restored.MiracleUsedToday.Should().BeTrue("a reload does not give the Miracle back");
        MiracleSystem.IsReady(restored, loadGods).Should().BeFalse();
        MiracleSystem.GetMiracle(restored, loadGods).Should().Be(GodDomain.War);
    }

    [Fact]
    public void OldSaves_ReadTheMiracleAsReady_AndTheEditorShowsIt()
    {
        var data = JsonSerializer.Deserialize<PlayerData>("{}")!;
        data.MiracleUsedToday.Should().BeFalse();
        MenuKeysNeedEnterPref1115Tests.Restore(data).MiracleUsedToday.Should().BeFalse();
        Source("Scripts/Editor/PlayerSaveEditor.cs").Should().Contain("p.MiracleUsedToday = EditorIO.PromptBool(");
    }

    // ---------------- Mortis cheats death ----------------

    [Fact]
    public void Mortis_CheatsDeathOnlyAtADeath_AndOnlyOnce()
    {
        var (c, gods) = Worshipper("GmMortis", "Mortis", 75);

        c.HP = 5;
        MiracleSystem.TryCheatDeath(c, gods).Should().BeFalse("only at a death");
        c.MiracleUsedToday.Should().BeFalse();

        c.HP = -40;
        MiracleSystem.TryCheatDeath(c, gods).Should().BeTrue();
        c.HP.Should().Be(1);
        c.MiracleUsedToday.Should().BeTrue();

        c.HP = 0;
        MiracleSystem.TryCheatDeath(c, gods).Should().BeFalse("once a day");
        c.HP.Should().Be(0);

        MiracleSystem.ApplyDailyReset(c);
        MiracleSystem.TryCheatDeath(c, gods).Should().BeTrue("ready again after the daily reset");
        c.HP.Should().Be(1);
    }

    [Theory]
    [InlineData("Mortis", 74)]
    [InlineData("Terran", 100)]
    public void Mortis_DoesNotFire_BelowChosen_OrForAnotherGod(string god, int favor)
    {
        var (c, gods) = Worshipper("GmNoMortis" + god, god, favor);
        c.HP = 0;
        MiracleSystem.TryCheatDeath(c, gods).Should().BeFalse();
        c.HP.Should().Be(0);
        c.MiracleUsedToday.Should().BeFalse();
    }

    [Fact]
    public void Mortis_ForAPlayerGodOfDeath()
    {
        var (c, gods) = Worshipper("GmMortisPg", "Zephyrine", 80);
        GodBoonSystem.SetPlayerGodBoon(c, "Zephyrine", GodDomain.Death, 40);
        c.HP = 0;
        MiracleSystem.TryCheatDeath(c, gods).Should().BeTrue();
        c.HP.Should().Be(1);
    }

    [Fact]
    public void Mortis_RunsInHandlePlayerDeath_AfterLastStand_BeforeTheDeathPipeline()
    {
        string body = Body("Scripts/Systems/CombatEngine.cs", "private async Task HandlePlayerDeath(CombatResult result)");
        int lastStand = body.IndexOf("LastStandCheckAndApply", StringComparison.Ordinal);
        int mortis = body.IndexOf("await TryMortisMiracle(result)", StringComparison.Ordinal);
        int died = body.IndexOf("result.PlayerActuallyDied = true", StringComparison.Ordinal);
        int arrest = body.IndexOf("if (result.Player.IsArrestCombat)", StringComparison.Ordinal);
        lastStand.Should().BeGreaterThan(0);
        mortis.Should().BeGreaterThan(lastStand, "the free rescues first; only a real death spends the Miracle");
        died.Should().BeGreaterThan(mortis);
        arrest.Should().BeGreaterThan(mortis);

        string helper = Body("Scripts/Systems/CombatEngine.cs", "private async Task<bool> TryMortisMiracle(CombatResult result)");
        helper.Should().Contain("result.MentalCollapseDeath").And.Contain("IsArrestCombat").And.Contain("IsExhibitionCombat");
        helper.Should().Contain("MiracleSystem.TryCheatDeath(player)").And.Contain("Loc.Get(\"miracle.mortis_fires\")");
    }

    [Fact]
    public void Mortis_ForGroupedFollowers_AtEachFollowerDeathSite()
    {
        string src = Source("Scripts/Systems/CombatEngine.cs");
        Count(src, "TryMortisMiracleForFollower(").Should().Be(4, "the helper and the three follower death sites");
        Count(src, "GroupFollowerDeath.Mark(").Should().Be(4, "the three fight death sites and the Mental collapse, as before");

        string dispatch = Body("Scripts/Systems/CombatEngine.cs", "private async Task HandleTeammateDeathDispatch(");
        dispatch.IndexOf("TryMortisMiracleForFollower(tm, result)", StringComparison.Ordinal)
            .Should().BeLessThan(dispatch.IndexOf("GroupFollowerDeath.Mark", StringComparison.Ordinal));

        string helper = Body("Scripts/Systems/CombatEngine.cs", "private bool TryMortisMiracleForFollower(");
        helper.Should().Contain("IsGroupedPlayer").And.Contain("IsArrestCombat").And.Contain("IsExhibitionCombat").And.Contain("Opponent != null");
    }

    [Fact]
    public void Mortis_IsNotInPvP()
    {
        Body("Scripts/Systems/CombatEngine.cs", "public async Task<CombatResult> PlayerVsPlayer(")
            .Should().NotContain("Miracle");
        Source("Scripts/Systems/WorldBossSystem.cs").Should().NotContain("Miracle", "the world boss has no Miracles in this piece");
    }

    // ---------------- Temple ----------------

    [Fact]
    public void TheTempleLine_AtChosen_NamesTheMiracle_AndWhetherItIsReady()
    {
        var (c, gods) = Worshipper("GmTemple", "Sylvana", 80);
        string ready = MiracleSystem.TempleLine(c, gods);
        ready.Should().Contain(MiracleSystem.Name(GodDomain.Nature)).And.Contain(MiracleSystem.Describe(GodDomain.Nature))
            .And.Contain(Loc.Get("miracle.state_ready"));
        MiracleSystem.TryConsume(c, gods);
        MiracleSystem.TempleLine(c, gods).Should().Contain(Loc.Get("miracle.state_used"));
    }

    [Fact]
    public void TheTempleLine_BelowChosen_SaysWhichTierUnlocksIt()
    {
        var (c, gods) = Worshipper("GmTempleLow", "Judicar", 60);
        MiracleSystem.TempleLine(c, gods).Should().Be(
            Loc.Get("miracle.temple_locked", MiracleSystem.Name(GodDomain.Law), GameConfig.GodFavorTierChosenMin));
        MiracleSystem.TempleLine(Hero("GmTempleNone"), new GodSystem()).Should().BeEmpty();
    }

    [Fact]
    public void TheTemple_ShowsTheMiracle_InTheStatusAndTheAltars()
    {
        Body("Scripts/Locations/TempleLocation.cs", "private async Task DisplayPlayerStatus()")
            .Should().Contain("MiracleSystem.TempleLine(currentPlayer, godSystem)");
        Source("Scripts/Locations/TempleLocation.cs").Should().Contain("Loc.Get(\"miracle.altar_line\", MiracleSystem.Name(boonDomain), MiracleSystem.Describe(boonDomain))");
    }

    [Theory]
    [InlineData("miracle.name.light", 0)]
    [InlineData("miracle.name.war", 0)]
    [InlineData("miracle.name.love", 0)]
    [InlineData("miracle.name.law", 0)]
    [InlineData("miracle.name.shadow", 0)]
    [InlineData("miracle.name.earth", 0)]
    [InlineData("miracle.name.death", 0)]
    [InlineData("miracle.name.magic", 0)]
    [InlineData("miracle.name.nature", 0)]
    [InlineData("miracle.name.chaos", 0)]
    [InlineData("miracle.desc.light", 1)]
    [InlineData("miracle.desc.war", 0)]
    [InlineData("miracle.desc.love", 0)]
    [InlineData("miracle.desc.law", 1)]
    [InlineData("miracle.desc.shadow", 0)]
    [InlineData("miracle.desc.earth", 0)]
    [InlineData("miracle.desc.death", 0)]
    [InlineData("miracle.desc.magic", 0)]
    [InlineData("miracle.desc.nature", 0)]
    [InlineData("miracle.desc.chaos", 1)]
    [InlineData("miracle.temple_line", 3)]
    [InlineData("miracle.state_ready", 0)]
    [InlineData("miracle.state_used", 0)]
    [InlineData("miracle.temple_locked", 2)]
    [InlineData("miracle.altar_line", 2)]
    [InlineData("miracle.mortis_fires", 0)]
    [InlineData("miracle.mortis_fires_other", 1)]
    public void MiracleText_IsInAllFiveLanguages(string key, int placeholders)
    {
        foreach (var lang in new[] { "en", "es", "fr", "hu", "it" })
        {
            using var doc = JsonDocument.Parse(Source($"Localization/{lang}.json"));
            doc.RootElement.TryGetProperty(key, out var v).Should().BeTrue($"{lang} {key}");
            string s = v.GetString()!;
            s.Should().NotBeNullOrWhiteSpace();
            for (int i = 0; i < placeholders; i++) s.Should().Contain("{" + i + "}", $"{lang} {key}");
            s.Should().NotContain("{" + placeholders + "}", $"{lang} {key}");
            s.Should().NotContain("\u2014").And.NotContain("\u2013");
        }
    }
}
