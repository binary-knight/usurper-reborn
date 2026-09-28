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
/// v1.2.0: a collapse already due when a location is entered (a resumed save, a login after a fight that
/// ended at Mental 0) is carried out before the entry encounters, so no grudge, spouse or street fight is
/// refused and then scored as a loss. The murder arrest fight, reachable at Mental 0 inside one action
/// (the witness loss comes between the murder and the guards), reads the flag (row in
/// MentalCollapseCallers1115Tests). A clean escape from a murder revenge no longer runs the defeat branch.
/// </summary>
[Collection("SharedGameSingletons")]
public class CollapseBeforeEncounters1115Tests
{
    private const BindingFlags F = BindingFlags.NonPublic | BindingFlags.Instance;
    private static readonly FieldInfo EncounterCounter =
        typeof(StreetEncounterSystem).GetField("_consequenceLocationChanges", BindingFlags.NonPublic | BindingFlags.Static)!;

    private sealed class ZeroRandom : Random { public override int Next(int maxValue) => 0; public override int Next(int min, int max) => min; }

    private static int Rolls() => (int)EncounterCounter.GetValue(null)!;

    [Fact]
    public async Task The_probe_counts_an_encounter_roll()
    {
        int before = Rolls();
        await StreetEncounterSystem.Instance.CheckForConsequenceEncounter(Hero("Probe"), GameLocation.Home, Term(new MemoryStream()));
        Rolls().Should().NotBe(before, "every consequence roll moves the counter, so an unchanged counter means no roll");
    }

    [Fact]
    public async Task An_entry_with_a_collapse_pending_collapses_before_any_encounter_roll()
    {
        var street = new MainStreetLocation();
        var output = new MemoryStream();
        var term = new TerminalEmulator(new LineStream(new[] { "", "", "", "" }), output);
        typeof(BaseLocation).GetField("terminal", F)!.SetValue(street, term);
        var hero = Hero("Spent", 0);
        hero.Gold = 1000;
        typeof(BaseLocation).GetField("currentPlayer", F)!.SetValue(street, hero);

        int before = Rolls();
        var loop = (Task)typeof(BaseLocation).GetMethod("LocationLoop", F)!.Invoke(street, null)!;
        (await Task.WhenAny(loop, Task.Delay(15000))).Should().BeSameAs(loop, "the collapse must end the entry at once");
        var exit = await Assert.ThrowsAsync<LocationExitException>(() => loop);

        Rolls().Should().Be(before, "no encounter is rolled before the collapse");
        exit.DestinationLocation.Should().Be(GameLocation.Healer);
        hero.Mental.Should().Be(GameConfig.MentalCollapseRescueMental);
        hero.MentalBroken.Should().BeTrue();
        term.StreamWriterInternal?.Flush();
        Text(output).Should().Contain(Loc.Get("mental.collapse_rescue"));
    }

    [Fact]
    public void The_entry_collapse_comes_before_every_entry_encounter()
    {
        var loop = MentalBands1115Tests.Method(Src("Locations", "BaseLocation.cs"), "LocationLoop");
        int collapse = At(loop, "if (MentalSystem.CollapseDue(currentPlayer))");
        int handled = At(loop, "await HandleMentalCollapse();");
        handled.Should().BeGreaterThan(collapse);
        handled.Should().BeLessThan(At(loop, ".CheckForConsequenceEncounter("));
        handled.Should().BeLessThan(At(loop, ".CheckForEncounter("));
        handled.Should().BeLessThan(At(loop, "await CheckNarrativeEncounters();"));
        handled.Should().BeLessThan(At(loop, ".CheckForPetition("));
        handled.Should().BeLessThan(At(loop, "while (!exitLocation"));
    }

    [Fact]
    public async Task A_clean_escape_from_murder_revenge_costs_nothing()
    {
        var street = (StreetEncounterSystem)Activator.CreateInstance(typeof(StreetEncounterSystem), nonPublic: true)!;
        typeof(StreetEncounterSystem).GetField("_random", F)!.SetValue(street, new ZeroRandom());   // the escape roll succeeds
        var hero = Hero("Runner", 50);
        hero.Level = 10;
        hero.Gold = 1000;
        var avenger = new NPC { Name1 = "Avenger", Name2 = "Avenger", Level = 10, HP = 100, MaxHP = 100, Memory = new MemorySystem() };
        avenger.Memory.RecordEvent(new MemoryEvent { Type = MemoryType.Murdered, InvolvedCharacter = hero.Name2, Description = "Murdered by Runner" });
        var output = new MemoryStream();
        var term = new TerminalEmulator(new LineStream(new[] { "R", "", "" }), output);
        var result = new EncounterResult();

        await (Task)typeof(StreetEncounterSystem).GetMethod("ExecuteGrudgeConfrontation", F)!
            .Invoke(street, new object[] { avenger, hero, term, result })!;

        term.StreamWriterInternal?.Flush();
        var text = Text(output);
        text.Should().Contain(Loc.Get("street_encounter.grudge.barely_escape", "Avenger"));
        hero.Gold.Should().Be(1000, "no fight was fought, so nothing is taken");
        result.GoldLost.Should().Be(0);
        text.Should().NotContain(Loc.Get("street_encounter.grudge.now_even"));
    }
}
