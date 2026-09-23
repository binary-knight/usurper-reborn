using System.Collections.Generic;
using System.IO;
using System.Reflection;
using System.Text;
using System.Threading.Tasks;
using FluentAssertions;
using UsurperRemake;
using UsurperRemake.Systems;
using Xunit;

namespace UsurperReborn.Tests;

/// <summary>
/// v1.1.10: an Old God's power surge (War Cry and its kin) was commented "increase boss damage for a
/// few rounds" but added 30 percent of the boss's attack to its Strength for the rest of the fight,
/// compounding with every cast. A player met Mael'Keth after easy floors and lost three companions.
/// The surge now lasts GameConfig.BossPowerSurgeRounds of the boss's rounds, a second cast renews
/// it instead of stacking, and its Strength comes back off when it ends.
/// </summary>
[Collection("SharedGameSingletons")]
public class BossPowerSurgeTests
{
    private static readonly BindingFlags F = BindingFlags.NonPublic | BindingFlags.Instance;

    private static Monster God() => new Monster { Name = "Maelketh", Level = 28, HP = 123_750, MaxHP = 123_750, Strength = 472, WeapPow = 472, Defence = 202, ArmPow = 202, IsBoss = true, IsActive = true };

    private static async Task Cast(CombatEngine engine, Monster god, string ability)
    {
        var hero = new Character { Name1 = "surge", Name2 = "Surge", Class = CharacterClass.Warrior, Level = 25, HP = 900, MaxHP = 900 };
        var handled = await (Task<bool>)typeof(CombatEngine).GetMethod("TryGenericBossAbility", F)!
            .Invoke(engine, new object?[] { god, hero, ability, new CombatResult(), null })!;
        handled.Should().BeTrue(ability);
    }

    private static CombatEngine Engine() => new CombatEngine(new TerminalEmulator(new MemoryStream(), new MemoryStream()));

    [Theory]
    [InlineData("War Cry")]
    [InlineData("Berserker Rage")]
    [InlineData("Martial Law")]
    [InlineData("Absolute Order")]
    [InlineData("Entomb")]
    public async Task ASecondCast_RenewsTheSurge_ItDoesNotStack(string ability)
    {
        var god = God();
        var engine = Engine();
        long start = god.Strength;

        await Cast(engine, god, ability);
        long surged = god.Strength;
        surged.Should().BeGreaterThan(start);

        await Cast(engine, god, ability);
        await Cast(engine, god, ability);
        god.Strength.Should().Be(surged, "three casts used to compound to about 2.6 times the first surge");
        god.PowerSurgeRounds.Should().Be(GameConfig.BossPowerSurgeRounds);
    }

    [Fact]
    public async Task TheSurge_EndsAfterItsRounds_AndTakesItsStrengthWithIt()
    {
        var god = God();
        long start = god.Strength;
        await Cast(Engine(), god, "War Cry");

        for (int round = 1; round < GameConfig.BossPowerSurgeRounds; round++)
        {
            CombatEngine.TickPowerSurge(god).Should().BeFalse($"round {round} is still inside the surge");
            god.Strength.Should().BeGreaterThan(start);
        }
        CombatEngine.TickPowerSurge(god).Should().BeTrue("the last round ends it");
        god.Strength.Should().Be(start);
        god.PowerSurgeStrength.Should().Be(0);
        CombatEngine.TickPowerSurge(god).Should().BeFalse("nothing is left to end");
    }

    [Fact]
    public async Task AStunnedGod_StillLosesItsSurgeOnTime()
    {
        // The surge counts down on the boss's rounds whether or not it acts: a pause while it was
        // held would let the player's own stun stretch the boss's strongest rounds (supervisor).
        var god = God();
        long start = god.Strength;
        var engine = Engine();
        await Cast(engine, god, "War Cry");
        god.StunRounds = GameConfig.BossPowerSurgeRounds + 2;
        var hero = new Character { Name1 = "held", Name2 = "Held", Class = CharacterClass.Warrior, Level = 25, HP = 900, MaxHP = 900 };
        var turn = typeof(CombatEngine).GetMethod("ProcessMonsterAction", F)!;
        for (int round = 0; round < GameConfig.BossPowerSurgeRounds; round++)
        {
            god.StatusTickedThisRound = false;   // a new round
            await (Task)turn.Invoke(engine, new object?[] { god, hero, new CombatResult(), null })!;
        }
        god.StunRounds.Should().BeGreaterThan(0, "the god was held the whole time");
        god.Strength.Should().Be(start, "its surge ran out while it was held");
    }

    [Fact]
    public async Task AfterItEnds_ANewCast_SurgesAgainFromTheBase()
    {
        var god = God();
        long start = god.Strength;
        var engine = Engine();
        await Cast(engine, god, "War Cry");
        long firstSurge = god.Strength - start;
        for (int i = 0; i < GameConfig.BossPowerSurgeRounds; i++) CombatEngine.TickPowerSurge(god);

        await Cast(engine, god, "War Cry");
        (god.Strength - start).Should().Be(firstSurge, "the same surge, not one built on the last");
    }
}
