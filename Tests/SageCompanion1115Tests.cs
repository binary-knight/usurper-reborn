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

/// <summary>v1.1.15 Sage piece 4: a Sage teammate casts control and party wards.</summary>
[Collection("SharedGameSingletons")]
public class SageCompanion1115Tests
{
    private const BindingFlags F = BindingFlags.NonPublic | BindingFlags.Instance;

    private static Character Sage(int level)
    {
        var sage = new Character
        {
            Name1 = "Sage", Name2 = "Sage", Class = CharacterClass.Sage, Level = level, Wisdom = 50, Intelligence = 50,
            HP = 100_000, MaxHP = 100_000, Mana = 100_000, MaxMana = 100_000, CombatSpeed = CombatSpeed.Instant,
        };
        var staff = EquipmentDatabase.GetAll().First(e => e.WeaponType == WeaponType.Staff);
        sage.EquippedItems[EquipmentSlot.MainHand] = staff.Id;
        return sage;
    }

    private static Character Ally(string name, CharacterClass cls) => new Character
    {
        Name1 = name, Name2 = name, Class = cls, Level = 40, HP = 5_000, MaxHP = 5_000, CombatSpeed = CombatSpeed.Instant,
    };

    private static Monster Ogre(int level = 20, bool boss = false, string family = "") => new Monster
    {
        Name = "Ogre", Level = level, HP = 5000, MaxHP = 5000, Strength = 50, Defence = 20, IsBoss = boss, FamilyName = family,
    };

    /// <summary>A Sage teammate beside a leader and a tank. The leader is result.Player.</summary>
    private static (CombatEngine engine, Character sage, Character leader, Character tank, CombatResult result) Party(int level)
    {
        var sage = Sage(level);
        var leader = Ally("Leader", CharacterClass.Ranger);
        var tank = Ally("Tank", CharacterClass.Warrior);
        var engine = new CombatEngine(new TerminalEmulator(new MemoryStream(), new MemoryStream()));
        typeof(CombatEngine).GetField("random", F)!.SetValue(engine, new Random(7));
        typeof(CombatEngine).GetField("currentPlayer", F)!.SetValue(engine, leader);
        typeof(CombatEngine).GetField("currentTeammates", F)!.SetValue(engine, new List<Character> { sage, tank });
        return (engine, sage, leader, tank, new CombatResult { Player = leader });
    }

    /// <summary>Every kind of Sage ward already up on everyone.</summary>
    private static void WardEveryone(params Character[] party)
    {
        foreach (var c in party)
        {
            CombatEngine.ApplyWardHighestWins(c, 500, 999);
            c.ApplyStatus(StatusEffect.Blur, 999);
            c.HasStatusImmunity = true;
            c.HasOceanMemory = true;
        }
    }

    private static int? Choose(CombatEngine engine, Character sage, List<Monster> monsters, CombatResult result, out bool isWard, out Monster? target)
    {
        var c = engine.ChooseSageTeammateSpell(sage, monsters, result);
        isWard = c?.isWard ?? false;
        target = c?.target;
        return c?.spell.Level;
    }

    // ---- area control ----

    [Fact]
    public void APack_GetsAreaControl()
    {
        var (engine, sage, leader, tank, result) = Party(75);
        WardEveryone(sage, leader, tank);
        var pack = new List<Monster> { Ogre(), Ogre(), Ogre() };
        Choose(engine, sage, pack, result, out bool ward, out var target).Should().Be(19, "Mass Confusion on a pack of three");
        ward.Should().BeFalse();
        target.Should().BeNull("an area spell has no single target");
    }

    [Theory]
    [InlineData(1)]
    [InlineData(2)]
    public void OneOrTwoWeakMonsters_GetNoAreaControl(int count)
    {
        var (engine, sage, leader, tank, result) = Party(75);
        WardEveryone(sage, leader, tank);
        var few = Enumerable.Range(0, count).Select(_ => Ogre()).ToList();
        Choose(engine, sage, few, result, out _, out _).Should().BeNull("no pack and no strong target: the Sage attacks");
    }

    [Fact]
    public void AreaControl_SkipsAPackItAlreadyHolds_AndTakesTheNextSpell()
    {
        var (engine, sage, leader, tank, result) = Party(75);
        WardEveryone(sage, leader, tank);
        var pack = new List<Monster> { Ogre(), Ogre(), Ogre() };
        foreach (var m in pack) { m.IsConfused = true; m.ConfusedDuration = 2; }
        Choose(engine, sage, pack, result, out _, out _).Should().Be(18, "the pack is confused already, so Unveil the Pattern");
    }

    [Fact]
    public void OldGods_AreNotCompelled()
    {
        var (engine, sage, leader, tank, result) = Party(55);
        WardEveryone(sage, leader, tank);
        var gods = new List<Monster> { Ogre(60, true, "OldGod"), Ogre(60, true, "OldGod"), Ogre(60, true, "OldGod") };
        foreach (var m in gods) { m.IsSlowed = true; m.SlowDuration = 2; }
        var spell = Choose(engine, sage, gods, result, out _, out var target);
        spell.Should().NotBe(14, "Old Gods resist Compel");
        spell.Should().Be(6, "Scholar's Mark on the strongest god instead");
        target.Should().NotBeNull();
    }

    [Fact]
    public void Bosses_AreNotSlumbered()
    {
        var (engine, sage, leader, tank, result) = Party(55);
        WardEveryone(sage, leader, tank);
        var bosses = new List<Monster> { Ogre(60, true), Ogre(60, true), Ogre(60, true) };
        foreach (var m in bosses) { m.IsSlowed = true; m.SlowDuration = 2; m.TauntRoundsLeft = 2; }
        Choose(engine, sage, bosses, result, out _, out _).Should().NotBe(10, "bosses are immune to Slumber Mist");
    }

    [Fact]
    public void ADisabledSpell_IsNotCast()
    {
        var (engine, sage, leader, tank, result) = Party(75);
        WardEveryone(sage, leader, tank);
        leader.TeammateDisabledSpells[sage.GetSkillToggleKey()] = new List<string> { "Mass Confusion" };
        Choose(engine, sage, new List<Monster> { Ogre(), Ogre(), Ogre() }, result, out _, out _).Should().Be(18);
    }

    // ---- single strong target ----

    [Fact]
    public void AStrongMonster_IsFrozen_ThroughTheHoldBudget()
    {
        var (engine, sage, leader, tank, result) = Party(40);
        WardEveryone(sage, leader, tank);
        var brute = Ogre(45);
        Choose(engine, sage, new List<Monster> { brute }, result, out _, out var target).Should().Be(4, "Freeze");
        target.Should().BeSameAs(brute);

        for (int i = 0; i < 40 && !brute.IsFrozen; i++)
        {
            sage.Mana = sage.MaxMana;
            engine.TryTeammateSageSpell(sage, new List<Monster> { brute }, result).GetAwaiter().GetResult().Should().BeTrue();
        }
        brute.IsFrozen.Should().BeTrue();
        brute.FrozenDuration.Should().BeInRange(1, GameConfig.MaxStunDurationNormal);
        brute.HoldsThisFight.Should().Be(1, "the freeze counts against the shared hold budget");
        Choose(engine, sage, new List<Monster> { brute }, result, out _, out _).Should().NotBe(4, "a held monster is not frozen again");
    }

    // ---- party wards ----

    [Fact]
    public void AnUnwardedParty_GetsAWard()
    {
        var (engine, sage, _, _, result) = Party(75);
        Choose(engine, sage, new List<Monster> { Ogre() }, result, out bool ward, out _).Should().Be(20, "Noctura's Veil, the strongest ward");
        ward.Should().BeTrue();
    }

    [Fact]
    public void AWardedParty_GetsNoWard()
    {
        var (engine, sage, leader, tank, result) = Party(75);
        WardEveryone(sage, leader, tank);
        Choose(engine, sage, new List<Monster> { Ogre() }, result, out bool ward, out _).Should().BeNull();
        ward.Should().BeFalse();
    }

    [Fact]
    public void Wards_AreNotSpammed()
    {
        var (engine, sage, leader, tank, result) = Party(95);
        var one = new List<Monster> { Ogre() };
        var cast = new List<int>();
        for (int turn = 0; turn < 30; turn++)
        {
            sage.Mana = sage.MaxMana;
            var c = engine.ChooseSageTeammateSpell(sage, one, result);
            if (c == null || !c.Value.isWard) break;
            // a fizzle repeats the same ward next turn; count each run once
            if (cast.Count == 0 || cast[^1] != c.Value.spell.Level) cast.Add(c.Value.spell.Level);
            engine.TryTeammateSageSpell(sage, one, result).GetAwaiter().GetResult();
        }
        cast.Should().Equal(new[] { 20, 16, 22 }, "Noctura's Veil, then Mind Blank for the immunity, then Ocean's Memory, each once");
        foreach (var c in new[] { sage, leader, tank })
        {
            c.MagicACBonus.Should().BeGreaterThan(0, c.Name2);
            c.HasOceanMemory.Should().BeTrue(c.Name2);
        }
    }

    [Fact]
    public void ADulledPack_IsSlowed_ByTheTeammatesCast()
    {
        var (engine, sage, leader, tank, result) = Party(20);
        WardEveryone(sage, leader, tank);
        var pack = new List<Monster> { Ogre(10), Ogre(10), Ogre(10), Ogre(10) };
        for (int i = 0; i < 40 && !pack.All(m => m.IsSlowed); i++)
        {
            sage.Mana = sage.MaxMana;
            engine.TryTeammateSageSpell(sage, pack, result).GetAwaiter().GetResult().Should().BeTrue();
        }
        pack.Should().OnlyContain(m => m.IsSlowed, "Dulling Mist reaches every enemy");
    }

    [Fact]
    public void TheTeammateTurn_TriesTheSageSpellsFirst()
    {
        string src = File.ReadAllText(Path.Combine(Leftovers1114BTests.RepoRoot(), "Scripts", "Systems", "CombatEngine.cs"));
        int sage = src.IndexOf("if (teammate.Class == CharacterClass.Sage && await TryTeammateSageSpell(teammate, monsters, result))", StringComparison.Ordinal);
        int offense = src.IndexOf("var spellAction = await TryTeammateOffensiveSpell(teammate, monsters, result);", StringComparison.Ordinal);
        sage.Should().BeGreaterThan(0);
        offense.Should().BeGreaterThan(sage);
    }
}
