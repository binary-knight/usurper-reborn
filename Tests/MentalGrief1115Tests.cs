using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using FluentAssertions;
using UsurperRemake.Systems;
using Xunit;
using static UsurperReborn.Tests.MentalRecovery1115Tests;
using static UsurperReborn.Tests.MentalRecoveryB1115Tests;

namespace UsurperReborn.Tests;

/// <summary>
/// v1.1.15 Mental piece 6, commit 1: grief (a companion or NPC teammate death, entering
/// Depression, reaching Acceptance) and witnessing a town NPC death or a world disaster.
/// </summary>
[Collection("SharedGameSingletons")]
public class MentalGrief1115Tests
{
    // Behaviour

    [Fact]
    public void A_companion_death_costs_ten_and_an_npc_death_six()
    {
        GameConfig.MentalCompanionGriefLoss.Should().Be(10);
        GameConfig.MentalNpcGriefLoss.Should().Be(6);
        var c = Hero("Mourner", 50);
        MentalSystem.ApplyCompanionGrief(c).Should().Be(-10);
        c.Mental.Should().Be(40);
        MentalSystem.ApplyNpcGrief(c).Should().Be(-6);
        c.Mental.Should().Be(34);
        var npc = Hero("Npc", 50); npc.AI = CharacterAI.Computer;
        MentalSystem.ApplyCompanionGrief(npc).Should().Be(0, "NPCs are skipped");
    }

    [Fact]
    public void Depression_costs_eight_and_acceptance_gains_ten_up_to_the_cap()
    {
        GameConfig.MentalGriefDepressionLoss.Should().Be(8);
        GameConfig.MentalGriefAcceptanceGain.Should().Be(10);
        var c = Hero("Mourner", 50);
        MentalSystem.ApplyGriefStage(c, GriefStage.Depression).Should().Be(-8);
        MentalSystem.ApplyGriefStage(c, GriefStage.Acceptance).Should().Be(10);
        MentalSystem.ApplyGriefStage(c, GriefStage.Anger).Should().Be(0);
        MentalSystem.ApplyGriefStage(c, GriefStage.Bargaining).Should().Be(0);
        c.Mental.Should().Be(52);
        var capped = Hero("Addict", 75, addict: 40);   // cap 80
        MentalSystem.ApplyGriefStage(capped, GriefStage.Acceptance).Should().Be(5, "a gain stops at the cap");
    }

    [Fact]
    public void UpdateGrief_returns_the_stage_each_grief_entered()
    {
        try
        {
            var grief = new GriefSystem();
            int day = StoryProgressionSystem.Instance.CurrentGameDay;
            grief.BeginGrief(CompanionId.Lyris, "Lyris", DeathType.Combat);
            grief.BeginNpcGrief("npc_grief_1", "Friend", DeathType.Combat).Should().BeTrue();
            grief.BeginNpcGrief("npc_grief_1", "Friend", DeathType.Combat).Should().BeFalse("a duplicate begins nothing");
            grief.UpdateGrief(day).Should().BeEmpty("no stage has run its course");
            grief.UpdateGrief(day + 100).Should().Equal(GriefStage.Anger, GriefStage.Anger);
        }
        finally
        {
            _ = new GriefSystem();   // a fresh fallback instance, so later tests see no live grief
        }
    }

    [Fact]
    public void Grief_stages_reach_mental_through_the_daily_helper()
    {
        var c = Hero("Mourner", 50);
        using var output = new MemoryStream();
        DailySystemManager.ApplyGriefStagesToMental(c, Term(output),
            new List<GriefStage> { GriefStage.Depression, GriefStage.Acceptance, GriefStage.Anger });
        c.Mental.Should().Be(52);
        Text(output).Should().Contain(Loc.Get("mental.gain", 10));
    }

    [Fact]
    public void Witnessing_costs_three_once_a_day()
    {
        GameConfig.MentalWitnessLoss.Should().Be(3);
        var c = Hero("Witness", 50);
        MentalSystem.ApplyWitnessLoss(c).Should().Be(-3);
        MentalSystem.UsedToday(c, MentalDailySource.WitnessLoss).Should().BeTrue();
        MentalSystem.ApplyWitnessLoss(c).Should().Be(0, "once a day");
        c.Mental.Should().Be(47);
        MentalSystem.ApplyDailyReset(c);
        int afterReset = c.Mental;
        MentalSystem.ApplyWitnessLoss(c).Should().Be(-3, "a new day");
        c.Mental.Should().Be(afterReset - 3);
    }

    [Fact]
    public void Only_disaster_events_count_as_a_world_disaster()
    {
        WorldEventSystem.IsDisaster(WorldEventSystem.EventType.PlagueOutbreak).Should().BeTrue();
        WorldEventSystem.IsDisaster(WorldEventSystem.EventType.MonsterInvasion).Should().BeTrue();
        WorldEventSystem.IsDisaster(WorldEventSystem.EventType.DemonPortal).Should().BeTrue();
        WorldEventSystem.IsDisaster(WorldEventSystem.EventType.HarvestFestival).Should().BeFalse();
        WorldEventSystem.IsDisaster(WorldEventSystem.EventType.PlagueEnds).Should().BeFalse();
        var world = WorldEventSystem.Instance;
        try
        {
            world.ClearAllEvents();
            world.ForceEvent(WorldEventSystem.EventType.HarvestFestival, 1);
            world.HasActiveDisaster.Should().BeFalse();
            world.ForceEvent(WorldEventSystem.EventType.PlagueOutbreak, 1);
            world.HasActiveDisaster.Should().BeTrue();
        }
        finally { world.ClearAllEvents(); }
    }

    // Wiring

    [Fact]
    public void Witnessing_is_wired_where_the_player_sees_it()
    {
        var murder = Body(Src("Locations", "BaseLocation.cs"), "AttackNPC");
        At(murder, "MentalSystem.ApplyWitnessLoss(currentPlayer)").Should().BeGreaterThan(At(murder, "Loc.Get(\"base.attack_killed\", npc.Name2)"));

        var execute = Body(Src("Locations", "CastleLocation.cs"), "ExecutePrisoner");
        int shown = At(execute, "Loc.Get(\"castle.executed_confirm\", name)");
        At(execute, "MentalSystem.ApplyWitnessLoss(currentPlayer)").Should().BeGreaterThan(shown);

        var events = Body(Src("Locations", "MainStreetLocation.cs"), "ShowWorldEvents");
        int gate = At(events, "if (WorldEventSystem.Instance.HasActiveDisaster)");
        gate.Should().BeGreaterThan(At(events, "WorldEventSystem.Instance.DisplayWorldStatus(terminal)"));
        At(events, "MentalSystem.ApplyWitnessLoss(currentPlayer)").Should().BeGreaterThan(gate);
    }

    [Fact]
    public void A_companion_death_is_wired_after_grief_begins()
    {
        var src = Src("Systems", "CompanionSystem.cs");
        var kill = Body(src, "KillCompanion");
        At(kill, "MentalSystem.ApplyCompanionGrief(griefPlayer)").Should().BeGreaterThan(At(kill, "GriefSystem.Instance.BeginGrief(id, companion.Name, type)"));
        var paradox = Body(src, "TriggerCompanionDeathByParadox");
        At(paradox, "MentalSystem.ApplyCompanionGrief(griefPlayer)").Should().BeGreaterThan(At(paradox, "GriefSystem.Instance.BeginGrief(companion.Id"));
    }

    [Fact]
    public void An_npc_teammate_death_is_wired_once_per_new_grief()
    {
        var body = Body(Src("Systems", "CombatEngine.cs"), "HandleNpcTeammateDeath");
        int begin = At(body, "bool griefBegan = UsurperRemake.Systems.GriefSystem.Instance.BeginNpcGrief(");
        int gate = At(body, "if (griefBegan && result.Player != null)");
        At(body, "MentalSystem.ApplyNpcGrief(result.Player)").Should().BeGreaterThan(gate);
        gate.Should().BeGreaterThan(begin);
    }

    [Fact]
    public void Both_daily_paths_apply_the_grief_stages()
    {
        var src = Src("Systems", "DailySystemManager.cs");
        foreach (var method in new[] { "ProcessPlayerDailyEvents", "ProcessDailyEvents" })
        {
            var body = Body(src, method);
            int update = At(body, "var griefEntered = grief.UpdateGrief(currentDay);");
            At(body, "ApplyGriefStagesToMental(").Should().BeGreaterThan(update, method);
            At(body, ", terminal, griefEntered)").Should().BeGreaterThan(update, method);
        }
    }
}
