using System;
using System.IO;
using FluentAssertions;
using UsurperRemake.Systems;
using Xunit;

namespace UsurperReborn.Tests;

/// <summary>
/// 1.2.0 Temple gods: at W, a canon worshipper who leaves their god and picks another in the same
/// run has the wrath name the god they left for (BetrayedForGodName), not "leaving their faith".
/// The wrath level is not changed.
/// </summary>
[Collection("SharedGameSingletons")]
public class GodLeaveThenChoose1115Tests
{
    private static (Character c, GodSystem gods) Worshipper(string name, string god, int favor)
    {
        var gods = new GodSystem();
        var c = new Character { Name1 = name, Name2 = name, AI = CharacterAI.Human, Level = 5, HP = 50, MaxHP = 50, Class = CharacterClass.Warrior };
        GodRegistry.SetWorshippedGod(c, god, gods).Should().BeTrue();
        c.GodFavor = favor;
        FavorSystem.Bind(c, gods);
        c.GodFavor = favor;
        return (c, gods);
    }

    [Fact]
    public void LeaveThenChoose_NamesTheChosenGod_AndKeepsTheLevel()
    {
        var (c, gods) = Worshipper("GdsLtc", "Solarius", 40);
        var leave = GodSwitchSystem.Switch(c, null, GodChangeBy.Player, gods);
        leave!.Value.WrathLevel.Should().BeGreaterThan(0);
        c.AngeredGodName.Should().Be("Solarius");
        c.BetrayedForGodName.Should().BeEmpty();
        int level = c.DivineWrathLevel;

        GodSwitchSystem.Switch(c, "Mortis", GodChangeBy.Player, gods);
        GodSwitchSystem.NameBetrayedFor(c, leave, "Mortis");

        c.BetrayedForGodName.Should().Be("Mortis");
        c.AngeredGodName.Should().Be("Solarius");
        c.DivineWrathLevel.Should().Be(level, "naming the god does not add wrath");
    }

    [Fact]
    public void NameBetrayedFor_LeavesOtherCasesAlone()
    {
        var (c, gods) = Worshipper("GdsLtcNo", "Solarius", 40);
        var leave = GodSwitchSystem.Switch(c, null, GodChangeBy.Player, gods);

        GodSwitchSystem.NameBetrayedFor(c, leave, "Solarius");
        c.BetrayedForGodName.Should().BeEmpty("returning to the angered god is not leaving for another");

        GodSwitchSystem.NameBetrayedFor(c, null, "Mortis");
        c.BetrayedForGodName.Should().BeEmpty("no leave in this run");

        c.RecordDivineWrath("Umbrath", "", 1);   // an older wrath from another god
        GodSwitchSystem.NameBetrayedFor(c, leave, "Mortis");
        c.BetrayedForGodName.Should().BeEmpty("the recorded wrath is not the one from this leave");

        c.RecordDivineWrath("Solarius", "Amara", 1);
        GodSwitchSystem.NameBetrayedFor(c, leave, "Mortis");
        c.BetrayedForGodName.Should().Be("Amara", "an already named god is kept");
    }

    [Fact]
    public void TempleW_NamesTheChosenGod_AfterTheSecondSwitch()
    {
        var dir = AppContext.BaseDirectory;
        while (dir != null && !File.Exists(Path.Combine(dir, "UsurperReborn.sln"))) dir = Path.GetDirectoryName(dir);
        string src = File.ReadAllText(Path.Combine(dir!, "Scripts/Locations/TempleLocation.cs"));
        int start = src.IndexOf("private async Task ProcessWorship(", StringComparison.Ordinal);
        start.Should().BeGreaterThan(0);
        string body = src.Substring(start, src.IndexOf("private async Task ProcessDesecrateAltar(", start, StringComparison.Ordinal) - start);
        body.Should().Contain("leftFaith = await SwitchGodAsync(null);");
        int second = body.IndexOf("await SwitchGodAsync(selectedGod.Name);", StringComparison.Ordinal);
        second.Should().BeGreaterThan(0);
        body.IndexOf("GodSwitchSystem.NameBetrayedFor(currentPlayer, leftFaith, selectedGod.Name);", StringComparison.Ordinal)
            .Should().BeGreaterThan(second);
    }
}
