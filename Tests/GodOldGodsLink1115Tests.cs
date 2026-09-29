using System;
using System.IO;
using System.Linq;
using System.Text.Json;
using FluentAssertions;
using UsurperRemake.Systems;
using Xunit;

namespace UsurperReborn.Tests;

/// <summary>
/// 1.2.0 Temple gods piece 6: the Old Gods link and the god text cleanup. A Zealot or Chosen follower
/// of a god that echoes an Old God hears one extra line as that encounter opens and deals 10% more
/// damage to it; a Chosen follower's god speaks when it falls. God epithets and descriptions are in
/// Loc in five languages, the four unused piece-4 keys are gone, and "elder" no longer names the new gods.
/// </summary>
[Collection("SharedGameSingletons")]
public class GodOldGodsLink1115Tests
{
    private static string Root()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir != null && !Directory.Exists(Path.Combine(dir.FullName, "Scripts"))) dir = dir.Parent;
        return dir!.FullName;
    }

    private static string Source(string folder, string file) => File.ReadAllText(Path.Combine(Root(), "Scripts", folder, file));

    private static JsonElement Lang(string lang) =>
        JsonDocument.Parse(File.ReadAllText(Path.Combine(Root(), "Localization", lang + ".json"))).RootElement;

    private static readonly string[] Langs = { "en", "es", "fr", "hu", "it" };

    private static (Character C, GodSystem Gods) Follower(string name, string god, int favor)
    {
        var gods = new GodSystem();
        var c = new Character { Name1 = name, Name2 = name, AI = CharacterAI.Human, Level = 50 };
        GodRegistry.SetWorshippedGod(c, god, gods);
        FavorSystem.Change(c, favor, gods);
        return (c, gods);
    }

    private static Monster OldGod(OldGodType t) => new Monster { Name = t.ToString(), HP = 1000, MaxHP = 1000, IsBoss = true, FamilyName = "OldGod", OldGod = t };

    [Theory]
    [InlineData("Solarius", OldGodType.Aurelion)]
    [InlineData("Valorian", OldGodType.Maelketh)]
    [InlineData("Amara", OldGodType.Veloura)]
    [InlineData("Judicar", OldGodType.Thorgrim)]
    [InlineData("Umbrath", OldGodType.Noctura)]
    [InlineData("Terran", OldGodType.Terravok)]
    [InlineData("Arcanus", OldGodType.Manwe)]
    public void EachEchoingGod_MapsToItsOldGod(string god, OldGodType oldGod) =>
        OldGodEchoSystem.EchoOfCanon(god).Should().Be(oldGod);

    [Fact]
    public void MortisSylvanaAndDiscordia_EchoNoOldGodThatIsFought()
    {
        OldGodEchoSystem.EchoOfCanon("Mortis").Should().BeNull();
        OldGodEchoSystem.EchoOfCanon("Sylvana").Should().BeNull();
        OldGodEchoSystem.EchoOfCanon("Discordia").Should().BeNull("the Sundering is not an Old God fight");
    }

    [Fact]
    public void TenPercent_OnlyAtZealotAndChosen_AndOnlyAgainstTheEchoedOldGod()
    {
        GameConfig.GodEchoDamagePct.Should().Be(10);
        var (zealot, g1) = Follower("OgZealot", "Solarius", GameConfig.GodFavorTierZealotMin);
        OldGodEchoSystem.BonusDamage(zealot, OldGod(OldGodType.Aurelion), 500, g1).Should().Be(50);
        OldGodEchoSystem.BonusDamage(zealot, OldGod(OldGodType.Noctura), 500, g1).Should().Be(0, "not the echoed Old God");
        OldGodEchoSystem.BonusDamage(zealot, new Monster { Name = "Aurelion", HP = 10, MaxHP = 10 }, 500, g1).Should().Be(0, "not an Old God fight");

        var (chosen, g2) = Follower("OgChosen", "Solarius", GameConfig.GodFavorTierChosenMin);
        OldGodEchoSystem.BonusDamage(chosen, OldGod(OldGodType.Aurelion), 500, g2).Should().Be(50);

        var (devout, g3) = Follower("OgDevout", "Solarius", GameConfig.GodFavorTierZealotMin - 1);
        OldGodEchoSystem.BonusDamage(devout, OldGod(OldGodType.Aurelion), 500, g3).Should().Be(0, "Devout is below Zealot");
    }

    [Fact]
    public void TheBonus_RidesTheBoonDamagePath()
    {
        var (zealot, gods) = Follower("OgPath", "Valorian", 60);
        zealot.HP = zealot.MaxHP = 1000;   // above half HP, so Valorian's own boon stays out
        DivineBlessingSystem.Instance.CalculateBonusDamage(zealot, OldGod(OldGodType.Maelketh), 1000, gods).Should().Be(100);
        DivineBlessingSystem.Instance.CalculateBonusDamage(zealot, OldGod(OldGodType.Thorgrim), 1000, gods).Should().Be(0);
        Source("Systems", "OldGodBossSystem.cs").Should().Contain("OldGod = boss.Type,");
    }

    [Fact]
    public void APlayerGodFollower_EchoesThroughItsDomain()
    {
        var (c, gods) = Follower("OgPlayerGod", "Zephyrine", 80);
        GodBoonSystem.SetPlayerGodBoon(c, "Zephyrine", GodDomain.Earth, 100);
        OldGodEchoSystem.EchoFor(c, gods).Should().Be(OldGodType.Terravok);
        OldGodEchoSystem.ChosenSpeaks(c, OldGodType.Terravok, gods).Should().BeTrue();
    }

    [Fact]
    public void TheLines_OnlyAtZealotAndChosen_AndTheFallOnlyAtChosen()
    {
        var (zealot, g1) = Follower("OgLineZ", "Umbrath", 60);
        OldGodEchoSystem.EncounterLine(zealot, OldGodType.Noctura, g1).Should().Be(Loc.Get("old_god.echo.noctura", "Umbrath"));
        OldGodEchoSystem.EncounterLine(zealot, OldGodType.Veloura, g1).Should().BeNull();
        OldGodEchoSystem.FallLine(zealot, OldGodType.Noctura, "Noctura", g1).Should().BeNull("only the Chosen hear their god at the fall");

        var (chosen, g2) = Follower("OgLineC", "Umbrath", 90);
        OldGodEchoSystem.FallLine(chosen, OldGodType.Noctura, "Noctura", g2).Should().Be(Loc.Get("old_god.echo_fall", "Umbrath", "Noctura"));

        var (follower, g3) = Follower("OgLineF", "Umbrath", 20);
        OldGodEchoSystem.EncounterLine(follower, OldGodType.Noctura, g3).Should().BeNull();
    }

    [Fact]
    public void TheHooks_AreInTheEncounterAndTheFall()
    {
        var src = Source("Systems", "OldGodBossSystem.cs");
        int react = src.IndexOf("await PlayCompanionBossReaction(type, player, terminal);", StringComparison.Ordinal);
        int echo = src.IndexOf("OldGodEchoSystem.EncounterLine(player, type)", StringComparison.Ordinal);
        int dialogue = src.IndexOf("DialogueSystem.Instance.StartDialogue(", StringComparison.Ordinal);
        echo.Should().BeGreaterThan(react).And.BeLessThan(dialogue);
        int defeated = src.IndexOf("private async Task<BossEncounterResult> HandleBossDefeated(", StringComparison.Ordinal);
        int fall = src.IndexOf("OldGodEchoSystem.FallLine(player, boss.Type, boss.Name)", StringComparison.Ordinal);
        fall.Should().BeGreaterThan(defeated).And.BeLessThan(src.IndexOf("private async Task<bool> HandleNocturaBetrayal(", StringComparison.Ordinal));
        Source("Systems", "DivineBlessingSystem.cs").Should().Contain("OldGodEchoSystem.BonusDamage(attacker, defender, baseDamage, gods)");
    }

    // ---------------- Loc cleanup ----------------

    [Fact]
    public void GodEpithetsDescriptionsAndOldGodLines_AreInAllFiveLanguages()
    {
        var keys = GameConfig.CanonGodNames.SelectMany(g => new[] { "god.epithet." + g.ToLowerInvariant(), "god.desc." + g.ToLowerInvariant() })
            .Concat(new[] { "aurelion", "maelketh", "veloura", "thorgrim", "noctura", "terravok", "manwe" }.Select(o => "old_god.echo." + o))
            .Append("old_god.echo_fall")
            .Concat(new[] { "light", "war", "love", "law", "shadow", "earth", "death", "magic", "nature", "chaos" }
                .SelectMany(d => new[] { "god.boon." + d, "god.domain." + d, "miracle.name." + d, "miracle.desc." + d }))
            .ToList();
        foreach (var lang in Langs)
        {
            var file = Lang(lang);
            foreach (var k in keys)
                file.TryGetProperty(k, out _).Should().BeTrue($"{lang} has {k}");
        }
        Loc.Get("god.epithet.solarius").Should().Be("The Radiant");
    }

    [Fact]
    public void TheTemple_ShowsEpithetsAndDescriptions_FromLoc()
    {
        var src = Source("Locations", "TempleLocation.cs");
        src.Should().NotContain("god.Properties[\"Domain\"]").And.NotContain("god.Properties[\"Description\"]");
        src.Split("GodText.Epithet(god)").Length.Should().Be(3, "both god lists");
        src.Should().Contain("GodText.Description(god)");
        var gods = new GodSystem();
        GodText.Epithet(gods.GetGod("Mortis")!).Should().Be(Loc.Get("god.epithet.mortis"));
        GodText.Description(gods.GetGod("Mortis")!).Should().Be(Loc.Get("god.desc.mortis"));
    }

    [Fact]
    public void TheUnusedPieceFourKeys_AreGone_AndElderNoLongerNamesTheNewGods()
    {
        string[] gone = { "temple.bond_severed", "temple.elder_strikes", "temple.elder_damage", "temple.elder_watches" };
        var scripts = Directory.GetFiles(Path.Combine(Root(), "Scripts"), "*.cs", SearchOption.AllDirectories).Select(File.ReadAllText).ToList();
        foreach (var k in gone)
        {
            scripts.Should().NotContain(s => s.Contains("\"" + k + "\""), $"nothing reads {k}");
            foreach (var lang in Langs) Lang(lang).TryGetProperty(k, out _).Should().BeFalse($"{lang} dropped {k}");
        }
        Loc.Get("temple.abandon_for_elder", "X").Should().NotContainEquivalentOf("elder");
        Loc.Get("temple.currently_worship_elder", "X").Should().NotContainEquivalentOf("elder");
        Loc.Get("temple.abandon_for_elder", "X").Should().Contain("new gods");
    }
}
