using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Threading.Tasks;
using FluentAssertions;
using UsurperRemake;
using UsurperRemake.Systems;
using Xunit;

namespace UsurperReborn.Tests;

/// <summary>
/// 1.3.2: a summon, flee or utility monster ability lands no hit. AbilityResult.DamageMultiplier defaults to 1,
/// and the engine's multiplier hit (player path and companion path) ran for every ability that left it alone,
/// so a summon or a mana drain also dealt a full hit. The ten cases set it to 0; the status abilities
/// (Enfeeble, WebTrap and the rest) keep their hit.
/// </summary>
[Collection("SharedGameSingletons")]
public class AbilityHit132Tests
{
    private static readonly BindingFlags F = BindingFlags.NonPublic | BindingFlags.Instance;

    private static readonly MonsterAbilities.AbilityType[] NoHit =
    {
        MonsterAbilities.AbilityType.SummonMinions, MonsterAbilities.AbilityType.CallForHelp,
        MonsterAbilities.AbilityType.Flee, MonsterAbilities.AbilityType.CommandArmy,
        MonsterAbilities.AbilityType.Split, MonsterAbilities.AbilityType.SummonSpiders,
        MonsterAbilities.AbilityType.AnimateTrees, MonsterAbilities.AbilityType.Resurrection,
        MonsterAbilities.AbilityType.ManaDrain, MonsterAbilities.AbilityType.Explosion,
    };

    private static T WithTactics<T>(Func<T> body)
    {
        bool saved = GameConfig.MonsterTacticsEnabled;
        try { GameConfig.MonsterTacticsEnabled = true; return body(); }
        finally { GameConfig.MonsterTacticsEnabled = saved; }
    }

    /// <summary>Level 400: the ability chance is 30 + 80, so the monster uses its ability every turn.</summary>
    private static Monster Mon(string name, string family, params string[] abilities) => new Monster
    {
        Name = name, TierName = name, FamilyName = family, Level = 400, HP = 100_000, MaxHP = 100_000,
        Strength = 60, WeapPow = 20, IsActive = true, SpecialAbilities = abilities.ToList(),
    };

    private static Character Fighter(string name) => new Character
    {
        Name1 = name.ToLowerInvariant(), Name2 = name, Class = CharacterClass.Warrior, Race = CharacterRace.Human, Level = 20,
        HP = 1_000_000, MaxHP = 1_000_000, Mana = 100_000, MaxMana = 100_000, ArmPow = 20, Defence = 10,
        CombatSpeed = CombatSpeed.Instant,
    };

    private sealed record Rig(CombatEngine Engine, Character Hero, CombatResult Result);

    private static Rig NewRig(int seed, params Character[] teammates)
    {
        var engine = new CombatEngine(new TerminalEmulator(new MemoryStream(), new MemoryStream()));
        var hero = Fighter("Hero");
        typeof(CombatEngine).GetField("random", F)!.SetValue(engine, new Random(seed));
        typeof(CombatEngine).GetField("_combatOwner", F)!.SetValue(engine, hero);
        typeof(CombatEngine).GetField("currentPlayer", F)!.SetValue(engine, hero);
        var result = new CombatResult { Player = hero, Teammates = teammates.ToList(), CombatLog = new List<string>(), CurrentRound = 2 };
        return new Rig(engine, hero, result);
    }

    private static bool Special(Rig rig, Monster m, List<Monster> live) =>
        ((Task<bool>)typeof(CombatEngine).GetMethod("TryMonsterSpecialAbility", F)!.Invoke(rig.Engine, new object?[] { m, rig.Hero, rig.Result, live })!).GetAwaiter().GetResult();

    private static void HitCompanion(Rig rig, Monster m, Character companion, List<Monster> live) =>
        ((Task)typeof(CombatEngine).GetMethod("MonsterAttacksCompanion", F)!.Invoke(rig.Engine, new object?[] { m, companion, rig.Result, live })!).GetAwaiter().GetResult();

    [Fact]
    public void TheTenCases_SetNoHitMultiplier()
    {
        foreach (var ability in NoHit)
        {
            var r = MonsterAbilities.ExecuteAbility(ability, Mon("Any", "Beast"), Fighter("Hero"), new Random(1));
            r.DamageMultiplier.Should().Be(0f, $"{ability} promises no attack");
        }
    }

    [Theory]
    [InlineData("SummonMinions")]
    [InlineData("Split")]
    public void ASummon_OnThePlayer_SpawnsAndDealsNoDamage(string ability)
    {
        WithTactics(() =>
        {
            for (int seed = 0; seed < 20; seed++)
            {
                var rig = NewRig(seed);
                var summoner = Mon("Summoner", "Beast", ability);
                var live = new List<Monster> { summoner };
                bool used = Special(rig, summoner, live);
                used.Should().BeTrue($"{ability} takes the monster's turn");
                live.Count.Should().BeGreaterThan(1, $"{ability} brings minions into the fight");
                rig.Hero.HP.Should().Be(rig.Hero.MaxHP, $"{ability} deals no hit");
            }
            return 0;
        });
    }

    [Theory]
    [InlineData("SummonMinions")]
    [InlineData("Split")]
    public void ASummon_OnACompanion_SpawnsAndDealsNoDamage(string ability)
    {
        WithTactics(() =>
        {
            for (int seed = 0; seed < 20; seed++)
            {
                var companion = Fighter("Lyra");
                var rig = NewRig(seed, companion);
                var summoner = Mon("Summoner", "Beast", ability);
                var live = new List<Monster> { summoner };
                HitCompanion(rig, summoner, companion, live);
                live.Count.Should().BeGreaterThan(1, $"{ability} brings minions into the fight");
                companion.HP.Should().Be(companion.MaxHP, $"{ability} deals no hit and the turn is used, so no normal attack follows");
            }
            return 0;
        });
    }

    [Fact]
    public void ManaDrain_StillDrains_AndDealsNoHit()
    {
        WithTactics(() =>
        {
            for (int seed = 0; seed < 20; seed++)
            {
                var rig = NewRig(seed);
                var drainer = Mon("Drainer", "Beast", "ManaDrain");
                Special(rig, drainer, new List<Monster> { drainer }).Should().BeTrue("the drain takes the monster's turn");
                rig.Hero.Mana.Should().BeLessThan(rig.Hero.MaxMana, "the player's mana is drained");
                rig.Hero.HP.Should().Be(rig.Hero.MaxHP, "a mana drain deals no hit");

                var companion = Fighter("Lyra");
                var crig = NewRig(seed, companion);
                HitCompanion(crig, drainer, companion, new List<Monster> { drainer });
                companion.Mana.Should().BeLessThan(companion.MaxMana, "the companion's mana is drained");
                companion.HP.Should().Be(companion.MaxHP, "a mana drain deals no hit to a companion");
            }
            return 0;
        });
    }

    [Theory]
    [InlineData("Enfeeble", StatusEffect.Weakened)]
    [InlineData("WebTrap", StatusEffect.Stunned)]
    public void AStatusAbility_StillHits_AndAfflicts(string ability, StatusEffect status)
    {
        WithTactics(() =>
        {
            int afflicted = 0, companionAfflicted = 0;
            for (int seed = 0; seed < 40; seed++)
            {
                var rig = NewRig(seed);
                var caster = Mon("Caster", "Beast", ability);
                Special(rig, caster, new List<Monster> { caster }).Should().BeTrue($"{ability} takes the monster's turn");
                rig.Hero.HP.Should().BeLessThan(rig.Hero.MaxHP, $"{ability} keeps its hit on the player");
                if (rig.Hero.HasStatus(status)) afflicted++;

                var companion = Fighter("Lyra");
                var crig = NewRig(seed, companion);
                HitCompanion(crig, caster, companion, new List<Monster> { caster });
                companion.HP.Should().BeLessThan(companion.MaxHP, $"{ability} keeps its hit on a companion");
                if (companion.HasStatus(status)) companionAfflicted++;
            }
            afflicted.Should().BeGreaterThan(0, $"{ability} still puts {status} on the player");
            companionAfflicted.Should().BeGreaterThan(0, $"{ability} still puts {status} on a companion");
            return 0;
        });
    }
}
