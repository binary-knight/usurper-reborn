using System;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text;
using System.Text.RegularExpressions;
using FluentAssertions;
using UsurperRemake;
using UsurperRemake.Systems;
using Xunit;

namespace UsurperReborn.Tests;

/// <summary>
/// 1.3.5: the holy weapon enchant doubles its damage against undead. It decided "undead" by a name list
/// (Skeleton, Zombie, Ghost, Lich, Wraith, Vampire, Undead, Revenant), so a Ghoul (floors 16 to 30) and a
/// Wight (floors 31 to 50), whose class is Undead, took only the plain bonus. It now reads the monster's
/// class, as the rest of combat does. Angels stay immune. The proc is forced with the engine's own test
/// hook, CombatEngine.SeedRandomForTests, on a seed whose first roll is under HolyEnchantProcChance; the
/// weapon carries only the holy enchant, so that roll is the holy proc's.
/// </summary>
[Collection("SharedGameSingletons")]
public class HolyUndead135Tests
{
    private const BindingFlags F = BindingFlags.NonPublic | BindingFlags.Instance;
    private const long Damage = 1000;

    private static readonly long PlainHoly = (long)(Damage * GameConfig.HolyEnchantDamageMultiplier);
    private static readonly long UndeadHoly = (long)(Damage * GameConfig.HolyEnchantDamageMultiplier * 2);

    /// <summary>The first seed in 1 to 1000 whose first roll fires the holy proc.</summary>
    private static int ProcSeed() =>
        Enumerable.Range(1, 1000).First(s => new Random(s).NextDouble() < GameConfig.HolyEnchantProcChance);

    /// <summary>A monster from the game's own families: the first seed in 1 to 5000 that rolls this tier.</summary>
    private static Monster FromData(int level, string tier)
    {
        foreach (int seed in Enumerable.Range(1, 5000))
        {
            var m = MonsterGenerator.GenerateMonster(level, random: new Random(seed));
            if (m.TierName == tier) return m;
        }
        throw new InvalidOperationException($"no seed in 1 to 5000 rolls a {tier} at level {level}");
    }

    /// <summary>Runs the live post-hit enchant step once for a holy-only weapon; returns HP lost and the text shown.</summary>
    private static (long lost, string shown) HolyProc(Monster target)
    {
        var output = new MemoryStream();
        var term = new TerminalEmulator(new MemoryStream(), output);
        var engine = new CombatEngine(term);
        engine.SeedRandomForTests(ProcSeed());
        int id = EquipmentDatabase.RegisterDynamic(new Equipment
        {
            Name = "Dawn Blade", Slot = EquipmentSlot.MainHand, WeaponPower = 10, MinLevel = 1,
            HasHolyEnchant = true,
        });
        var hero = new Character { Name1 = "hero", Name2 = "Hero", Class = CharacterClass.Paladin, Level = 30, HP = 500, MaxHP = 500 };
        hero.EquippedItems[EquipmentSlot.MainHand] = id;
        hero.IsAttunedTo("aurelion").Should().BeFalse("the shrine bonus would change the numbers");
        target.MaxHP = 1_000_000;
        target.HP = target.MaxHP;
        typeof(CombatEngine).GetMethod("CheckElementalEnchantProcs", F)!
            .Invoke(engine, new object?[] { hero, target, Damage, new CombatResult(), EquipmentSlot.MainHand });
        term.StreamWriterInternal?.Flush();
        string shown = Regex.Replace(Encoding.UTF8.GetString(output.ToArray()), "\u001b\\[[0-9;]*[A-Za-z]", "");
        return (target.MaxHP - target.HP, shown);
    }

    [Fact]
    public void HolyUndead135_TheForcedSeedFiresTheProc()
    {
        new Random(ProcSeed()).NextDouble().Should().BeLessThan(GameConfig.HolyEnchantProcChance);
        UndeadHoly.Should().Be(2 * PlainHoly).And.BeGreaterThan(0);
    }

    [Theory]
    [InlineData(20, "Ghoul")]
    [InlineData(40, "Wight")]
    public void HolyUndead135_AGhoulAndAWight_TakeTheUndeadBonus(int level, string tier)
    {
        var m = FromData(level, tier);
        m.MonsterClass.Should().Be(MonsterClass.Undead, $"the game's data makes a {tier} undead");
        new[] { "Skeleton", "Zombie", "Ghost", "Lich", "Wraith", "Vampire", "Undead", "Revenant" }
            .Should().NotContain(w => m.Name.Contains(w), "the name holds none of the old list's words");
        var (lost, shown) = HolyProc(m);
        lost.Should().Be(UndeadHoly, $"a {tier} is undead and takes the doubled holy damage");
        shown.Should().Contain(Loc.Get("combat.enchant_holy_undead_tm", "Hero", UndeadHoly));
    }

    [Fact]
    public void HolyUndead135_ASkeletonOfTheUndeadClass_StillTakesTheBonus()
    {
        var m = FromData(10, "Zombie");
        m.Name = "Skeleton Warrior";
        m.MonsterClass.Should().Be(MonsterClass.Undead);
        var (lost, _) = HolyProc(m);
        lost.Should().Be(UndeadHoly);
    }

    [Fact]
    public void HolyUndead135_ALivingMonster_TakesThePlainHoly()
    {
        var m = FromData(20, "Shadow");
        m.MonsterClass.Should().NotBe(MonsterClass.Undead);
        m.Undead.Should().Be(0);
        var (lost, shown) = HolyProc(m);
        lost.Should().Be(PlainHoly, "a living monster takes the plain holy damage");
        shown.Should().Contain(Loc.Get("combat.enchant_holy_tm", "Hero", PlainHoly));
    }

    [Fact]
    public void HolyUndead135_AnAngel_IsStillImmune()
    {
        var m = FromData(20, "Angel");
        var (lost, shown) = HolyProc(m);
        lost.Should().Be(0, "angels absorb holy damage");
        shown.Should().Contain(Loc.Get("combat.enchant_holy_immune_angel", MonsterNames.Display(m)));
    }

    [Fact]
    public void HolyUndead135_AnUndeadAngel_IsStillImmune()
    {
        var m = FromData(20, "Angel");
        m.MonsterClass = MonsterClass.Undead;
        m.Undead = 1;
        var (lost, _) = HolyProc(m);
        lost.Should().Be(0, "the angel immunity is checked before the undead bonus");
    }

    [Fact]
    public void HolyUndead135_TheLegacyUndeadFlag_AlsoCounts()
    {
        var m = FromData(20, "Shadow");
        m.Undead = 1;
        var (lost, _) = HolyProc(m);
        lost.Should().Be(UndeadHoly, "Undead greater than 0 marks an undead monster, as elsewhere in combat");
    }
}
