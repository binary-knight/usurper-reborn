using System;
using System.Collections.Generic;
using System.Reflection;
using System.Threading.Tasks;
using FluentAssertions;
using UsurperRemake;
using UsurperRemake.Systems;
using Xunit;
using G = UsurperReborn.Tests.HitRoll128Golden;
using P = UsurperReborn.Tests.PrestigeSingles136Tests;

namespace UsurperReborn.Tests;

/// <summary>
/// 1.3.6 G11, the supervisor's rulings of 2026-10-21T22:57:03Z: Empathic Link splits the ally's damage after the ally's
/// own reductions, so the pair never takes more than the ally alone would have; Prison Warden's Command is the timed cut
/// alone, with no Weaken or Marked rider.
/// </summary>
[Collection("SharedGameSingletons")]
public class PrestigeSingles136RulingTests
{
    private const BindingFlags F = BindingFlags.NonPublic | BindingFlags.Instance;

    private static T WithTactics<T>(Func<T> body)
    {
        bool saved = GameConfig.MonsterTacticsEnabled;
        try { GameConfig.MonsterTacticsEnabled = true; return body(); }
        finally { GameConfig.MonsterTacticsEnabled = saved; }
    }

    private static Monster Ability(string ability) => new Monster
    {
        Name = "Drake", TierName = "Drake", FamilyName = "Beast", Level = 400, HP = 1_000_000, MaxHP = 1_000_000,
        Strength = 600, WeapPow = 50, IsActive = true, CanSpeak = false, SpecialAbilities = new List<string> { ability },
    };

    // ---------------------------------------------------------------- ruling 1: the split after the ally's reductions

    /// <summary>One monster ability on the ally (the leader's or a teammate's path), linked or not: the ally's and the
    /// caster's HP lost.</summary>
    private static (long ally, long caster) Hit(string ability, bool onLeader, bool linked, int seed)
    {
        var hero = P.Hero(CharacterClass.Warrior, hp: 1_000_000); hero.HP = hero.MaxHP;
        var caster = P.Mate("Wave", CharacterClass.Wavecaller, hp: 1_000_000); caster.HP = caster.MaxHP;
        var mate = P.Mate("Ann", hp: 1_000_000); mate.HP = mate.MaxHP;
        var rig = G.NewRig(hero, new Random(seed), new List<Character> { caster, mate }, Ability(ability));
        rig.Result.Monsters = rig.Monsters;
        var ally = onLeader ? hero : mate;
        if (onLeader) hero.AwakeningStage = 7;   // a reduction late on the leader's path: the Barracks step's defence factor
        else mate.IsDefending = true;             // a reduction late on the companion's path: its mitigation's brace half
        if (linked) { ally.EmpathicLinkCaster = caster; ally.EmpathicLinkRounds = 4; }
        var m = rig.Monsters[0];
        if (onLeader)
            ((Task<bool>)typeof(CombatEngine).GetMethod("TryMonsterSpecialAbility", F)!
                .Invoke(rig.Engine, new object?[] { m, hero, rig.Result, new List<Monster> { m } })!).GetAwaiter().GetResult();
        else
            ((Task)typeof(CombatEngine).GetMethod("MonsterAttacksCompanion", F)!
                .Invoke(rig.Engine, new object?[] { m, mate, rig.Result, new List<Monster> { m } })!).GetAwaiter().GetResult();
        return (ally.MaxHP - ally.HP, caster.MaxHP - caster.HP);
    }

    [Theory]
    [InlineData("LifeDrain", true)]      // the leader's life drain branch
    [InlineData("CrushingBlow", true)]   // the leader's multiplier branch
    [InlineData("CrushingBlow", false)]  // the companion's multiplier branch
    public void PrestigeSingles136_EmpathicLink_ThePairNeverTakesMoreThanTheAllyAlone(string ability, bool onLeader)
    {
        WithTactics(() =>
        {
            int hits = 0;
            for (int seed = 1; seed <= 12; seed++)
            {
                var alone = Hit(ability, onLeader, linked: false, seed);
                if (alone.ally <= 1) continue;   // missed or dodged
                var pair = Hit(ability, onLeader, linked: true, seed);
                alone.caster.Should().Be(0);
                (pair.ally + pair.caster).Should().Be(alone.ally, $"seed {seed}: the pair takes what the ally alone took");
                pair.caster.Should().Be(alone.ally / 2, $"seed {seed}: half of the ally's reduced damage");
                hits++;
            }
            hits.Should().BeGreaterThan(0, $"{ability}: at least one hit lands");
            return 0;
        });
    }

    // ---------------------------------------------------------------- ruling 2: the cut alone

    [Theory]
    [InlineData(false, false, 50)]
    [InlineData(true, false, 75)]
    [InlineData(false, true, 75)]
    public void PrestigeSingles136_PrisonWardensCommand_TheCutAlone_NoWeakenNoMark(bool boss, bool mini, int keptPct)
    {
        var plain = new Monster
        {
            Name = "Ogre", TierName = "Ogre", Level = 50, HP = 1_000_000, MaxHP = 1_000_000, Strength = 100, Defence = 40,
            ArmPow = 20, IsBoss = boss, IsMiniBoss = mini, IsActive = true, CanSpeak = false, Experience = 1,
        };
        long atk0 = plain.GetAttackPower();
        var hero = P.Hero(CharacterClass.Abysswarden);
        var rig = G.NewRig(hero, new Random(136), null, plain);
        bool saved = GameConfig.PlayerHitRollEnabled;
        try { GameConfig.PlayerHitRollEnabled = false; P.Cast(rig, hero, "prison_wardens_command", plain); }
        finally { GameConfig.PlayerHitRollEnabled = saved; }
        plain.WeakenRounds.Should().Be(0, "no Weakened rider");
        plain.IsMarked.Should().BeFalse("no Marked rider");
        plain.MarkedDuration.Should().Be(0);
        plain.GetAttackPower().Should().Be(atk0 - atk0 * (100 - keptPct) / 100, $"one cast leaves {keptPct} percent of the attack");
    }
}
