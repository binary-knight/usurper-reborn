using System;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using FluentAssertions;
using UsurperRemake.Systems;
using Xunit;

namespace UsurperReborn.Tests;

/// <summary>
/// 1.2.2 temple-gold-gate: a gold offering at a god's altar no longer needs a good deed left.
/// It follows only the Favor rules (the gold, the daily cap), the deeds message is not shown, and
/// a player with deeds left gets the identical result. An offering is not a deed: no gold or item
/// offering gives back a good deed (ChivNr) or a dark deed (DarkNr), while the capped alignment
/// change, the faction reputation and the Favor stay as they were.
/// </summary>
[Collection("SharedGameSingletons")]
public class TempleGoldGate122Tests
{
    private const BindingFlags F = BindingFlags.NonPublic | BindingFlags.Instance;

    private sealed class Rig : IDisposable
    {
        public TempleLocation Temple = null!;
        public MemoryStream Output = null!;
        public TerminalEmulator Term = null!;
        public Character Hero = null!;
        public GodSystem Gods = null!;

        public string Text()
        {
            Term.StreamWriterInternal?.Flush();
            return Regex.Replace(Encoding.UTF8.GetString(Output.ToArray()), "\u001b\\[[0-9;?]*[A-Za-z]", "").Replace("\r", "");
        }

        public void Dispose() => GodRegistry.SetWorshippedGod(Hero, null, Gods);
    }

    private static Rig Make(string name, int deeds, string god, params string[] lines)
    {
        var output = new MemoryStream();
        if (lines.Length == 0) lines = new[] { god, "G", "1000", "Y" };
        var term = new TerminalEmulator(new LineStream(lines.Concat(Enumerable.Repeat("", 40))), output);
        var gods = UsurperRemake.GodSystemSingleton.Instance;
        var temple = new TempleLocation(term, null!, gods);
        var hero = StatRewards1115Tests.Fresh(name);
        hero.Gold = 100_000;
        hero.ChivNr = deeds;
        hero.DarkNr = deeds;
        hero.Healing = 5;
        typeof(BaseLocation).GetField("currentPlayer", F)!.SetValue(temple, hero);
        var r = new Rig { Temple = temple, Output = output, Term = term, Hero = hero, Gods = gods };
        GodRegistry.SetWorshippedGod(hero, god, gods).Should().BeTrue();
        FavorSystem.Bind(hero, gods);
        return r;
    }

    private static Rig Make(string name, int deeds) => Make(name, deeds, "Solarius");

    private static void MakeEvil(Character c) { c.Chivalry = 0; c.Darkness = 900; }

    private static async Task Offer(Rig r, TempleLocation.TempleRoom room = TempleLocation.TempleRoom.Nave)
    {
        var m = typeof(TempleLocation).GetMethod("ProcessOffering", F);
        m.Should().NotBeNull();
        try { await (Task)m!.Invoke(r.Temple, new object[] { room })!; }
        catch (TargetInvocationException ex) when (ex.InnerException != null) { throw ex.InnerException; }
    }

    private static int Standing(Faction f) => FactionSystem.Instance.FactionStanding.GetValueOrDefault(f);

    // ---------------- No good deed needed ----------------

    [Fact]
    public async Task NoDeedsLeft_TheGoldOfferingStillGoesThrough()
    {
        using var r = Make("TgNoDeeds", 0);
        int favorBefore = FavorSystem.GetFavor(r.Hero, r.Gods);
        await Offer(r);
        r.Hero.Gold.Should().Be(99_000, "the altar takes the gold");
        int expected = Math.Min(FavorSystem.GoldSacrificeFavor(1000, r.Hero.Level), GameConfig.GodFavorGoldDailyCap);
        expected.Should().BeGreaterThan(0, "the offering is worth Favor, so the check is not vacuous");
        (FavorSystem.GetFavor(r.Hero, r.Gods) - favorBefore).Should().Be(expected);
        FavorSystem.GainedToday(r.Hero, FavorSource.GoldSacrifice).Should().Be(expected);
        r.Text().Should().NotContain(Loc.Get("temple.no_good_deeds"));
    }

    [Fact]
    public async Task DeedsLeftOrNot_TheOfferingIsIdentical()
    {
        using var none = Make("TgDeedsNone", 0);
        await Offer(none);
        using var some = Make("TgDeedsSome", 3);
        await Offer(some);

        none.Hero.Gold.Should().Be(some.Hero.Gold);
        FavorSystem.GetFavor(none.Hero, none.Gods).Should().Be(FavorSystem.GetFavor(some.Hero, some.Gods));
        FavorSystem.GainedToday(none.Hero, FavorSource.GoldSacrifice)
            .Should().Be(FavorSystem.GainedToday(some.Hero, FavorSource.GoldSacrifice));
        none.Hero.Chivalry.Should().Be(some.Hero.Chivalry);
        none.Hero.ChivNr.Should().Be(0);
        some.Hero.ChivNr.Should().Be(3);
        none.Text().Should().NotContain(Loc.Get("temple.no_good_deeds"));
        some.Text().Should().NotContain(Loc.Get("temple.no_good_deeds"));
    }

    [Fact]
    public async Task NoDeedsLeft_TheDailyFavorCapStillApplies()
    {
        using var r = Make("TgNoDeedsCap", 0);
        r.Hero.GodFavorDayGains[FavorSource.GoldSacrifice.ToString()] = GameConfig.GodFavorGoldDailyCap;
        int favorBefore = FavorSystem.GetFavor(r.Hero, r.Gods);
        await Offer(r);
        r.Hero.Gold.Should().Be(99_000, "the altar still takes the gold");
        FavorSystem.GetFavor(r.Hero, r.Gods).Should().Be(favorBefore, "today's gold Favor is spent");
        r.Text().Should().NotContain(Loc.Get("temple.no_good_deeds"));
    }

    // ---------------- An offering is not a deed ----------------

    [Fact]
    public async Task GoodGodGold_LeavesTheGoodDeedCount_AlignmentReputationFavorAsBefore()
    {
        using var r = Make("TgGoodNoDeed", 2);
        var twin = StatRewards1115Tests.Fresh("TgGoodTwin");
        int standingGain = Math.Min(GameConfig.MaxAlignmentGainPerTempleSacrifice, 1000 / 100);
        AlignmentSystem.Instance.ChangeAlignment(twin, standingGain, isGood: true, "test");
        int faithBefore = Standing(Faction.TheFaith);
        int favorBefore = FavorSystem.GetFavor(r.Hero, r.Gods);

        await Offer(r);

        r.Hero.Gold.Should().Be(99_000);
        r.Hero.ChivNr.Should().Be(2, "an offering does not give back a good deed");
        r.Hero.DarkNr.Should().Be(2);
        r.Hero.Chivalry.Should().Be(twin.Chivalry, "the capped alignment change is unchanged");
        r.Hero.Darkness.Should().Be(twin.Darkness);
        (Standing(Faction.TheFaith) - faithBefore).Should().Be(standingGain, "Faith reputation as before");
        (FavorSystem.GetFavor(r.Hero, r.Gods) - favorBefore)
            .Should().Be(Math.Min(FavorSystem.GoldSacrificeFavor(1000, r.Hero.Level), GameConfig.GodFavorGoldDailyCap));
    }

    [Fact]
    public async Task EvilGodGold_LeavesTheDarkDeedCount_AlignmentReputationFavorAsBefore()
    {
        using var r = Make("TgEvilNoDeed", 2, "Umbrath");
        MakeEvil(r.Hero);
        var twin = StatRewards1115Tests.Fresh("TgEvilTwin");
        MakeEvil(twin);
        int standingGain = Math.Min(GameConfig.MaxAlignmentGainPerTempleSacrifice, 1000 / 100);
        AlignmentSystem.Instance.ChangeAlignment(twin, standingGain, isGood: false, "test");
        int shadowsBefore = Standing(Faction.TheShadows);
        int favorBefore = FavorSystem.GetFavor(r.Hero, r.Gods);

        await Offer(r, TempleLocation.TempleRoom.Undercroft);

        r.Hero.Gold.Should().Be(99_000);
        r.Hero.DarkNr.Should().Be(2, "an offering does not give back a dark deed");
        r.Hero.ChivNr.Should().Be(2);
        r.Hero.Darkness.Should().Be(twin.Darkness, "the capped alignment change is unchanged");
        r.Hero.Chivalry.Should().Be(twin.Chivalry);
        (Standing(Faction.TheShadows) - shadowsBefore).Should().Be(standingGain, "Shadows reputation as before");
        (FavorSystem.GetFavor(r.Hero, r.Gods) - favorBefore)
            .Should().Be(Math.Min(FavorSystem.GoldSacrificeFavor(1000, r.Hero.Level), GameConfig.GodFavorGoldDailyCap));
    }

    [Fact]
    public async Task ItemOffering_LeavesTheDeedCounts()
    {
        using var r = Make("TgPotionNoDeed", 2, "Solarius", "Solarius", "I", "H", "2", "Y");
        int chivalryBefore = (int)r.Hero.Chivalry;
        await Offer(r);
        r.Hero.Healing.Should().Be(3, "two potions laid on the altar");
        r.Hero.ChivNr.Should().Be(2);
        r.Hero.DarkNr.Should().Be(2);
        r.Hero.Chivalry.Should().BeGreaterThan(chivalryBefore, "the alignment change still applies");
    }

    [Fact]
    public async Task PlayerGodGold_LeavesTheGoodDeedCount()
    {
        using var r = Make("TgPgNoDeed", 2, "Zephyrine", "Zephyrine", "G", "500", "");
        r.Temple.ImmortalGodsForTests = new()
        {
            new TempleLocation.ImmortalGodInfo { DivineName = "Zephyrine", GodAlignment = "Light", GodLevel = 3, Believers = 2 }
        };
        int chivalryBefore = (int)r.Hero.Chivalry;
        await Offer(r);
        r.Hero.Gold.Should().Be(99_500, "gold at the player-god's altar");
        r.Hero.ChivNr.Should().Be(2);
        r.Hero.Chivalry.Should().BeGreaterThan(chivalryBefore, "the alignment change still applies");
    }

    [Fact]
    public async Task OneGoldThenAMainStreetDeed_CannotRepeatOnceDeedsAreSpent()
    {
        using var r = Make("TgLoop", 1, "Solarius", "Solarius", "G", "1", "Y");
        var hero = r.Hero;

        var output = new MemoryStream();
        var term = new TerminalEmulator(new LineStream(new[] { "1", "", "", "" }.Concat(Enumerable.Repeat("", 20))), output);
        var street = new MainStreetLocation();
        typeof(BaseLocation).GetField("terminal", F)!.SetValue(street, term);
        typeof(BaseLocation).GetField("currentPlayer", F)!.SetValue(street, hero);
        var showDeeds = typeof(MainStreetLocation).GetMethod("ShowGoodDeeds", F)!;

        await (Task)showDeeds.Invoke(street, null)!;
        hero.ChivNr.Should().Be(0, "the Main Street deed spends the last deed");

        await Offer(r);
        hero.Gold.Should().Be(99_999, "the one gold offering goes through");
        hero.ChivNr.Should().Be(0, "the offering does not give the deed back");
        long chivalryAfterOffer = hero.Chivalry;

        await (Task)showDeeds.Invoke(street, null)!;
        hero.ChivNr.Should().Be(0);
        hero.Chivalry.Should().Be(chivalryAfterOffer, "no second deed was possible");
        term.StreamWriterInternal?.Flush();
        var text = Regex.Replace(Encoding.UTF8.GetString(output.ToArray()), "\u001b\\[[0-9;?]*[A-Za-z]", "");
        text.Should().Contain(Loc.Get("main_street.deed_done_today"), "the deed menu is closed once deeds are spent");
    }
}
