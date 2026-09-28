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
/// v1.2.0: BaseLocation.ApplyMurderConsequences rolled a 50% execution for every capture, including a
/// murder arrest fight refused at Mental 0 (result.MentalCollapseNotFought), which is taken as a
/// surrender. A refused arrest is not a choice to face the Crown, so that roll is now skipped; the
/// player is still captured and sentenced to prison exactly as before. A voluntary Surrender and a real
/// defeat (lost the guard fight) still roll as before.
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

    // Source: the skip is wired to the collapse-not-fought branch only. A voluntary Surrender
    // (choice == "S") and a real defeat (the else / lost-the-fight branch) never set it, so the roll
    // in those branches is unchanged.
    [Fact]
    public void Only_the_collapse_not_fought_branch_skips_the_execution_roll()
    {
        var body = Body(Src("Locations", "BaseLocation.cs"), "ApplyMurderConsequences");

        int declared = At(body, "bool skipExecutionRoll = false;");
        int surrender = At(body, "if (choice == \"S\")");
        int collapseCheck = At(body, "if (result.MentalCollapseNotFought)");
        int skipSet = At(body, "skipExecutionRoll = true;");
        int playerWon = At(body, "else if (playerWon)");
        int roll = At(body, "bool isExecuted =");

        declared.Should().BeLessThan(surrender, "the flag defaults to false before either branch runs");
        skipSet.Should().BeInRange(collapseCheck, playerWon, "the skip is set inside the collapse-not-fought branch only");
        roll.Should().BeGreaterThan(skipSet, "the flag is set before the roll reads it");

        var surrenderBranch = body.Substring(surrender, collapseCheck - surrender);
        surrenderBranch.Should().NotContain("skipExecutionRoll = true", "a voluntary Surrender still rolls");

        var rollLine = body.Substring(roll, body.IndexOf('\n', roll) - roll);
        rollLine.Should().Contain("skipExecutionRoll").And.Contain("Random.Shared.Next(100) < 50");

        System.Text.RegularExpressions.Regex.Matches(body, "skipExecutionRoll = true;").Count
            .Should().Be(1, "only the collapse-not-fought branch skips the roll");
    }
}
