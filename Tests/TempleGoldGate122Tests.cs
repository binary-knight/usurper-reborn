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
/// a player with deeds left gets the identical result.
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

    private static Rig Make(string name, int deeds)
    {
        var output = new MemoryStream();
        var lines = new[] { "Solarius", "G", "1000", "Y" };
        var term = new TerminalEmulator(new LineStream(lines.Concat(Enumerable.Repeat("", 40))), output);
        var gods = UsurperRemake.GodSystemSingleton.Instance;
        var temple = new TempleLocation(term, null!, gods);
        var hero = StatRewards1115Tests.Fresh(name);
        hero.Gold = 100_000;
        hero.ChivNr = deeds;
        hero.DarkNr = 3;
        typeof(BaseLocation).GetField("currentPlayer", F)!.SetValue(temple, hero);
        var r = new Rig { Temple = temple, Output = output, Term = term, Hero = hero, Gods = gods };
        GodRegistry.SetWorshippedGod(hero, "Solarius", gods).Should().BeTrue();
        FavorSystem.Bind(hero, gods);
        return r;
    }

    private static async Task Offer(Rig r)
    {
        var m = typeof(TempleLocation).GetMethod("ProcessOffering", F);
        m.Should().NotBeNull();
        try { await (Task)m!.Invoke(r.Temple, new object[] { TempleLocation.TempleRoom.Nave })!; }
        catch (TargetInvocationException ex) when (ex.InnerException != null) { throw ex.InnerException; }
    }

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
        (none.Hero.ChivNr - 0).Should().Be(some.Hero.ChivNr - 3, "the offering moves the deed count the same way");
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
}
