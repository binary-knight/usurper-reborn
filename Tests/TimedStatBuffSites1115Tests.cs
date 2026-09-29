using System;
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
using static UsurperReborn.Tests.TimedStatBuffs1115Tests;

namespace UsurperReborn.Tests;

/// <summary>
/// 1.2.0 lost stat rewards, piece 3, commit 2: the temporary stat sites use the timed buffs.
/// Groggo's Shadow Blessing keeps its saved field, is added in RecalculateStats and is cleared by a
/// rest, so no rest subtracts Dexterity. The Inn ale and the evil alignment event last until the
/// next rest and refresh on a repeat; the settlement lockpick and smoke bomb last through the next
/// fight.
/// </summary>
[Collection("SharedGameSingletons")]
public class TimedStatBuffSites1115Tests
{
    private const BindingFlags F = BindingFlags.NonPublic | BindingFlags.Instance;

    // ---------------- Groggo ----------------

    private static async Task BuyGroggo(Character c)
    {
        c.Gold = 1_000_000;
        await Call(At(new DarkAlleyLocation(), c, Term("3\n")), "VisitGroggoMagic");
        c.GroggoShadowBlessingDex.Should().Be(3, "the blessing was bought");
    }

    [Fact]
    public async Task Groggo_LastsThroughAFight_AndASave_AndEndsAtRest_NeverBelowTrue()
    {
        var c = StatRewards1115Tests.Fresh("TssGroggo");
        await BuyGroggo(c);
        c.Dexterity.Should().Be(13);
        c.BaseDexterity.Should().Be(10, "the blessing never touches the Base field");

        long seen = 0;
        await Fight(c, () => seen = c.Dexterity);
        seen.Should().Be(13, "the blessing is in force during the fight");
        c.Dexterity.Should().Be(13);

        var r = StatRewards1115Tests.RoundTrip(c);
        r.GroggoShadowBlessingDex.Should().Be(3);
        r.Dexterity.Should().Be(13, "the blessing survives a save round trip");

        var output = new MemoryStream();
        await Call(At(new InnLocation(), r, Term("", output)), "RestAtTable");
        Encoding.UTF8.GetString(output.ToArray()).Should().Contain("The Blessing of Shadows fades", "the rest says the blessing ended");
        r.GroggoShadowBlessingDex.Should().Be(0);
        r.Dexterity.Should().Be(10, "a rest ends the blessing and leaves Dexterity at its true value");

        await Fight(r);
        r.Dexterity.Should().Be(10, "the next fight finds the true value, never less");
    }

    [Fact]
    public async Task Groggo_ARestRightAfterBuying_LeavesTheTrueValue()
    {
        var c = StatRewards1115Tests.Fresh("TssGroggoRest");
        await BuyGroggo(c);
        c.HomeRestsToday = 0;
        await Call(At(new HomeLocation(), c, Term("")), "DoRest");
        c.Dexterity.Should().Be(10);
        c.RecalculateStats();
        c.Dexterity.Should().Be(10);
    }

    [Fact]
    public void Groggo_ASavedBlessing_IsAppliedOnLoad()
    {
        var c = StatRewards1115Tests.Fresh("TssGroggoLoad");
        c.GroggoShadowBlessingDex = 3;
        var r = StatRewards1115Tests.RoundTrip(c);
        r.Dexterity.Should().Be(13, "the load recalc applies the saved blessing");
        var ser = typeof(SaveSystem).GetMethod("SerializePlayer", F)!;
        var data = (PlayerData)ser.Invoke(SaveSystem.Instance, new object[] { c })!;
        PlayerCharacterLoader.CreateFromSaveData(data, "TssGroggoLoad").Dexterity.Should().Be(13, "the PvP snapshot carries it too");
    }

    private static readonly Regex DexterityWrite = new(@"\.Dexterity\s*(\+=|-=|\+\+|--|=(?!=))");

    [Theory]
    [InlineData("Scripts/Locations/InnLocation.cs", "private async Task RestAtTable(")]
    [InlineData("Scripts/Locations/InnLocation.cs", "private async Task RentRoom(")]
    [InlineData("Scripts/Locations/CastleLocation.cs", "private async Task RoyalSleep(")]
    [InlineData("Scripts/Locations/DarkAlleyLocation.cs", "private async Task VisitGroggoMagic(")]
    public void Groggo_NoSite_WritesDexterity(string file, string signature)
    {
        DexterityWrite.Matches(Body(Src(file), signature)).Select(m => m.Value).Should().BeEmpty($"{signature} leaves Groggo's Dexterity to RecalculateStats");
    }

    // ---------------- Inn ale ----------------

    [Fact]
    public async Task Ale_RepeatedDrinks_RefreshAndNeverStack_AndEndAtRest()
    {
        var c = StatRewards1115Tests.Fresh("TssAle");
        c.Gold = 1_000_000;
        c.DrinksLeft = 50;
        var inn = new InnLocation();
        for (int i = 0; i < 6; i++)
        {
            At(inn, c, Term(""));
            await Call(inn, "BuyDrink");
        }
        c.TimedStatBuffs.Should().OnlyContain(b => b.Source == InnLocation.AleBuffSource && b.EndsOn == StatBuffEnd.Rest);
        c.TimedStatBuffs.Should().HaveCountLessThanOrEqualTo(3);
        c.TimedStatBuffs.GroupBy(b => b.Stat).Should().OnlyContain(g => g.Count() == 1, "one buff per stat");
        c.Charisma.Should().BeInRange(10, 12, "the ale Charisma never stacks past +2");
        c.Strength.Should().BeInRange(10, 11, "the ale Strength never stacks past +1");
        c.Wisdom.Should().BeInRange(19, 20, "the ale Wisdom never stacks past -1");

        await Fight(c);
        c.Charisma.Should().Be(10 + c.TimedStatBuffs.Where(b => b.Stat == StatKind.Charisma).Sum(b => b.Amount), "the ale lasts through a fight");

        c.OnRest();
        c.TimedStatBuffs.Should().BeEmpty();
        (c.Charisma, c.Strength, c.Wisdom).Should().Be((10L, 10L, 20L), "a rest ends the ale");
    }

    [Fact]
    public void Ale_EachEffect_IsARestBuffOfItsStat()
    {
        string body = Body(Src("Scripts/Locations/InnLocation.cs"), "private async Task BuyDrink(");
        body.Should().Contain("AddTimedStatBuff(AleBuffSource, StatKind.Charisma, 2, StatBuffEnd.Rest)");
        body.Should().Contain("AddTimedStatBuff(AleBuffSource, StatKind.Strength, 1, StatBuffEnd.Rest)");
        body.Should().Contain("AddTimedStatBuff(AleBuffSource, StatKind.Wisdom, -1, StatBuffEnd.Rest)");
        Regex.Matches(body, @"currentPlayer\.(Charisma|Strength|Wisdom)\s*(\+=|-=|=(?!=))").Should().BeEmpty();
    }

    // ---------------- evil alignment event ----------------

    private sealed class ZeroRandom : Random
    {
        public override int Next(int maxValue) => 0;
        public override int Next(int minValue, int maxValue) => minValue;
    }

    [Fact]
    public async Task EvilEvent_Strength_RefreshesAndLastsUntilRest()
    {
        var c = StatRewards1115Tests.Fresh("TssEvil");
        c.Darkness = 1000;
        c.Chivalry = 0;
        var sys = AlignmentSystem.Instance;
        sys.GetAlignment(c).Should().Be(AlignmentSystem.AlignmentType.Evil);
        var field = typeof(AlignmentSystem).GetField("_random", F)!;
        var old = field.GetValue(sys);
        field.SetValue(sys, new ZeroRandom());
        try
        {
            (await sys.CheckAlignmentEvent(c, Term(""))).Should().BeTrue("the 5% roll comes up");
            await sys.CheckAlignmentEvent(c, Term(""));
        }
        finally { field.SetValue(sys, old); }

        c.BaseStrength.Should().Be(10);
        c.Strength.Should().Be(11, "a second event refreshes, never stacks");
        await Fight(c);
        c.Strength.Should().Be(11, "the Strength lasts through a fight");
        StatRewards1115Tests.RoundTrip(c).Strength.Should().Be(11);
        c.OnRest();
        c.Strength.Should().Be(10, "a rest ends it");
    }

    // ---------------- settlement lockpick and smoke bomb ----------------

    [Theory]
    [InlineData("lockpick", StatKind.Dexterity)]
    [InlineData("smoke_bomb", StatKind.Agility)]
    public async Task SettlementBuy_LastsThroughTheNextFightOnly(string item, StatKind stat)
    {
        var c = StatRewards1115Tests.Fresh("TssSettle" + item);
        var m = typeof(DungeonLocation).GetMethod("ApplySettlementPurchase", F)!;
        m.Invoke(new DungeonLocation(), new object[] { c, item });
        m.Invoke(new DungeonLocation(), new object[] { c, item });
        StatRewards1115Tests.Derived(c, stat).Should().Be(12, "a second purchase refreshes, never stacks");
        c.GetBaseStat(stat).Should().Be(10);

        c.OnRest();
        StatRewards1115Tests.Derived(c, stat).Should().Be(12, "a rest does not end it");

        long seen = 0;
        await Fight(c, () => seen = StatRewards1115Tests.Derived(c, stat));
        seen.Should().Be(12, "it is in force during the next fight");
        StatRewards1115Tests.Derived(c, stat).Should().Be(10, "it ends with that fight");
        c.TimedStatBuffs.Should().BeEmpty();
    }
}
