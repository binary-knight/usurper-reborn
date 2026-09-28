using System;
using System.IO;
using System.Reflection;
using System.Threading.Tasks;
using FluentAssertions;
using UsurperRemake.Systems;
using Xunit;
using static UsurperReborn.Tests.MentalRecovery1115Tests;
using static UsurperReborn.Tests.MentalRecoveryB1115Tests;

namespace UsurperReborn.Tests;

/// <summary>
/// v1.2.0: BaseLocation.ApplyMurderConsequences rolled a 50% execution for every capture, including
/// a murder arrest fight refused at Mental 0 (result.MentalCollapseNotFought) and a voluntary Surrender
/// chosen at Mental 0. Both are the same non-choice under MentalSystem.CollapseDue, so neither rolls
/// now; the player is still captured and sentenced to prison exactly as before. A Surrender above
/// Mental 0 and a real defeat (lost the guard fight) still roll as before.
/// </summary>
[Collection("SharedGameSingletons")]
public class ArrestNoExecution1115Tests
{
    private const BindingFlags F = BindingFlags.NonPublic | BindingFlags.Instance;

    private static Player HeroPlayer(string name, int mental) => new Player
    {
        Name1 = name, Name2 = name, AI = CharacterAI.Human, Class = CharacterClass.Warrior, Race = CharacterRace.Human,
        Mental = mental, HP = 100, MaxHP = 100, MentalHintShown = true, Gold = 1000, DaysInPrison = 0,
    };

    private static Task RunApplyMurderConsequences(BaseLocation street, Character player, NPC victim) =>
        (Task)typeof(BaseLocation).GetMethod("ApplyMurderConsequences", F)!
            .Invoke(street, new object[] { player, victim })!;

    // Behaviour: the collapse-not-fought path reaches the prison sentence without the execution roll.
    // The fix makes the roll unreachable on this path (skipExecutionRoll short-circuits it), so unlike
    // a real 50/50 roll this outcome is deterministic and one run is enough to prove it.
    [Fact]
    public async Task A_refused_arrest_fight_is_captured_and_sentenced_without_the_execution_roll()
    {
        var street = new MainStreetLocation();
        var output = new MemoryStream();
        var term = new TerminalEmulator(new LineStream(new[] { "F", "", "", "" }), output);
        typeof(BaseLocation).GetField("terminal", F)!.SetValue(street, term);
        var hero = HeroPlayer("Spent", 0); // Mental 0: the guard fight is refused (MentalCollapseNotFought)
        typeof(BaseLocation).GetField("currentPlayer", F)!.SetValue(street, hero);
        var victim = new NPC { Name1 = "Victim", Name2 = "Victim", Level = 5, HP = 50, MaxHP = 50 };

        var task = RunApplyMurderConsequences(street, hero, victim);
        var exit = await Assert.ThrowsAsync<LocationExitException>(() => task);

        exit.DestinationLocation.Should().Be(GameLocation.Prison, "capture still sends the player to prison");
        hero.DaysInPrison.Should().Be(2, "the prison sentence is applied exactly as on a surrender");
        hero.IsMurderConvict.Should().BeTrue();
        hero.IsAlive.Should().BeTrue("the execution roll never runs on a refused arrest fight");
        term.StreamWriterInternal?.Flush();
        Text(output).Should().Contain(Loc.Get("mental.collapse_before_fight"));
        Text(output).Should().NotContain(Loc.Get("base.death_sentence"));
    }

    // Behaviour: choosing Surrender directly (not a refused fight) at Mental 0 is the same non-choice,
    // so it also skips the roll: captured, sentenced to prison, alive, no death sentence text.
    [Fact]
    public async Task A_surrender_at_mental_zero_is_captured_and_sentenced_without_the_execution_roll()
    {
        var street = new MainStreetLocation();
        var output = new MemoryStream();
        var term = new TerminalEmulator(new LineStream(new[] { "S", "", "", "" }), output);
        typeof(BaseLocation).GetField("terminal", F)!.SetValue(street, term);
        var hero = HeroPlayer("Spent", 0); // Mental 0: MentalSystem.CollapseDue is true
        typeof(BaseLocation).GetField("currentPlayer", F)!.SetValue(street, hero);
        var victim = new NPC { Name1 = "Victim", Name2 = "Victim", Level = 5, HP = 50, MaxHP = 50 };

        var task = RunApplyMurderConsequences(street, hero, victim);
        var exit = await Assert.ThrowsAsync<LocationExitException>(() => task);

        exit.DestinationLocation.Should().Be(GameLocation.Prison, "a Surrender is still a capture");
        hero.DaysInPrison.Should().Be(2, "the prison sentence is applied as on any capture");
        hero.IsMurderConvict.Should().BeTrue();
        hero.IsAlive.Should().BeTrue("the execution roll is skipped for a Surrender at Mental 0");
        term.StreamWriterInternal?.Flush();
        Text(output).Should().NotContain(Loc.Get("base.death_sentence"));
    }

    // Source: a Surrender above Mental 0 is not CollapseDue, so the Surrender branch falls through to
    // the same roll as before. Random.Shared makes a direct behaviour assertion flaky at 50%, so this
    // checks the guard in source: the Surrender branch sets skipExecutionRoll only inside the
    // CollapseDue check, and that check is not the unconditional default.
    [Fact]
    public void A_surrender_above_mental_zero_still_reaches_the_execution_roll()
    {
        var body = Body(Src("Locations", "BaseLocation.cs"), "ApplyMurderConsequences");

        int surrender = At(body, "if (choice == \"S\")");
        int collapseGuard = At(body, "if (MentalSystem.CollapseDue(currentPlayer))");
        int fightBranch = At(body, "var guards = new Monster[5];");

        collapseGuard.Should().BeInRange(surrender, fightBranch, "the Surrender skip sits inside the Surrender branch");

        var surrenderBranch = body.Substring(surrender, fightBranch - surrender);
        System.Text.RegularExpressions.Regex.Matches(surrenderBranch, "skipExecutionRoll = true;").Count
            .Should().Be(1, "the Surrender branch sets the skip exactly once, guarded by CollapseDue");

        int guardIndex = surrenderBranch.IndexOf("if (MentalSystem.CollapseDue(currentPlayer))", StringComparison.Ordinal);
        int skipIndex = surrenderBranch.IndexOf("skipExecutionRoll = true;", StringComparison.Ordinal);
        skipIndex.Should().BeGreaterThan(guardIndex,
            "the skip is set only after the CollapseDue check, so a Surrender above Mental 0 falls through to the roll");
    }

    // Source: the skip is wired to the guarded Surrender branch and the collapse-not-fought branch
    // only. A real defeat (the lost-the-fight branch, reached when playerWon is false and the fight
    // was not refused) never sets it, so the roll there is unchanged.
    [Fact]
    public void Only_the_surrender_and_collapse_not_fought_branches_skip_the_execution_roll()
    {
        var body = Body(Src("Locations", "BaseLocation.cs"), "ApplyMurderConsequences");

        int declared = At(body, "bool skipExecutionRoll = false;");
        int surrender = At(body, "if (choice == \"S\")");
        int collapseCheck = At(body, "if (result.MentalCollapseNotFought)");
        int playerWon = At(body, "else if (playerWon)");
        int roll = At(body, "bool isExecuted =");

        declared.Should().BeLessThan(surrender, "the flag defaults to false before either branch runs");
        roll.Should().BeGreaterThan(collapseCheck, "the flag is set before the roll reads it");

        var lostFightBranch = body.Substring(playerWon, roll - playerWon);
        lostFightBranch.Should().NotContain("skipExecutionRoll = true", "a real defeat still rolls");

        var rollLine = body.Substring(roll, body.IndexOf('\n', roll) - roll);
        rollLine.Should().Contain("skipExecutionRoll").And.Contain("Random.Shared.Next(100) < 50");

        System.Text.RegularExpressions.Regex.Matches(body, "skipExecutionRoll = true;").Count
            .Should().Be(2, "the Surrender branch and the collapse-not-fought branch each skip the roll once");
    }
}
