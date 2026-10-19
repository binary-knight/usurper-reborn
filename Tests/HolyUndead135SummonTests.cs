using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using FluentAssertions;
using UsurperRemake;
using UsurperRemake.Systems;
using Xunit;

namespace UsurperReborn.Tests;

/// <summary>
/// 1.3.5: the undead a cursed scroll summons in the dungeon (DungeonLocation.CreateUndeadMonster: "Undead",
/// "Zombie" or "Skeleton Warrior") were built with the Normal class, so no class based undead check counted
/// them. They now carry MonsterClass.Undead and Undead 1. Checked with the holy enchant (doubled damage) and
/// with the Cleric's Turn Undead, whose live gate in ExecuteSpellMultiMonster passes only undead and demons.
/// </summary>
[Collection("SharedGameSingletons")]
public class HolyUndead135SummonTests
{
    private const BindingFlags F = BindingFlags.NonPublic | BindingFlags.Instance;
    private const long Damage = 1000;
    private static readonly long UndeadHoly = (long)(Damage * GameConfig.HolyEnchantDamageMultiplier * 2);

    /// <summary>A Random whose Next(n) always gives one index, to pick each summoned name in turn.</summary>
    private sealed class PickRandom : Random
    {
        private readonly int _index;
        public PickRandom(int index) { _index = index; }
        public override int Next(int maxValue) => _index;
    }

    private static Monster Summoned(int index, int floor = 20)
    {
        var d = new DungeonLocation();
        typeof(DungeonLocation).GetField("currentDungeonLevel", F)!.SetValue(d, floor);
        typeof(DungeonLocation).GetField("dungeonRandom", F)!.SetValue(d, new PickRandom(index));
        var m = (Monster)typeof(DungeonLocation).GetMethod("CreateUndeadMonster", F)!.Invoke(d, null)!;
        m.MaxHP = 1_000_000;
        m.HP = m.MaxHP;
        return m;
    }

    private static int ProcSeed() =>
        Enumerable.Range(1, 1000).First(s => new Random(s).NextDouble() < GameConfig.HolyEnchantProcChance);

    private static long HolyLoss(Monster target)
    {
        var engine = new CombatEngine(new TerminalEmulator(new MemoryStream(), new MemoryStream()));
        engine.SeedRandomForTests(ProcSeed());
        int id = EquipmentDatabase.RegisterDynamic(new Equipment
        {
            Name = "Dawn Blade", Slot = EquipmentSlot.MainHand, WeaponPower = 10, MinLevel = 1,
            HasHolyEnchant = true,
        });
        var hero = new Character { Name1 = "hero", Name2 = "Hero", Class = CharacterClass.Paladin, Level = 30, HP = 500, MaxHP = 500 };
        hero.EquippedItems[EquipmentSlot.MainHand] = id;
        long before = target.HP;
        typeof(CombatEngine).GetMethod("CheckElementalEnchantProcs", F)!
            .Invoke(engine, new object?[] { hero, target, Damage, new CombatResult(), EquipmentSlot.MainHand });
        return before - target.HP;
    }

    [Theory]
    [InlineData(0, "Undead")]
    [InlineData(1, "Zombie")]
    [InlineData(2, "Skeleton Warrior")]
    public void HolyUndead135_EachSummonedUndead_TakesTheDoubledHoly(int index, string name)
    {
        var m = Summoned(index);
        m.Name.Should().Be(name);
        m.MonsterClass.Should().Be(MonsterClass.Undead, $"a summoned {name} is undead");
        m.Undead.Should().Be(1);
        HolyLoss(m).Should().Be(UndeadHoly, $"a summoned {name} takes the doubled holy damage");
    }

    private static Character Cleric()
    {
        var c = new Character
        {
            Name1 = "hu135", Name2 = "Cleric", Class = CharacterClass.Cleric, Race = CharacterRace.Human, Level = 60,
            Intelligence = 60, Wisdom = 60, Strength = 40, HP = 10_000, MaxHP = 10_000,
            Mana = 100_000, MaxMana = 100_000, AI = CharacterAI.Human, CombatSpeed = CombatSpeed.Instant,
        };
        if (!SpellSystem.HasRequiredSpellWeapon(c))
            foreach (var w in EquipmentDatabase.GetAll().Where(e => e.Slot == EquipmentSlot.MainHand))
            {
                c.EquippedItems[EquipmentSlot.MainHand] = w.Id;
                if (SpellSystem.HasRequiredSpellWeapon(c)) break;
            }
        SpellSystem.HasRequiredSpellWeapon(c).Should().BeTrue();
        return c;
    }

    /// <summary>Casts Turn Undead (Cleric spell 6) through the live multi-monster spell path until a cast does not fizzle.</summary>
    private static async Task<(long lost, string text)> TurnUndead(Func<Monster> make)
    {
        for (int attempt = 0; attempt < 60; attempt++)
        {
            var caster = Cleric();
            var output = new MemoryStream();
            var engine = new CombatEngine(new TerminalEmulator(new MemoryStream(), output));
            engine.SeedRandomForTests(11 + attempt);
            typeof(CombatEngine).GetField("currentPlayer", F)!.SetValue(engine, caster);
            var m = make();
            var result = new CombatResult { Player = caster };
            result.Monsters.Add(m);
            var action = new CombatAction { Type = CombatActionType.CastSpell, SpellIndex = 6, TargetIndex = 0 };
            await (Task)typeof(CombatEngine).GetMethod("ExecuteSpellMultiMonster", F)!
                .Invoke(engine, new object?[] { caster, new List<Monster> { m }, action, result })!;
            var term = (TerminalEmulator)typeof(CombatEngine).GetField("terminal", F)!.GetValue(engine)!;
            term.StreamWriterInternal?.Flush();
            string text = Regex.Replace(Encoding.UTF8.GetString(output.ToArray()), "\u001b\\[[0-9;]*[A-Za-z]", "");
            if (text.Contains(Loc.Get("combat.spell_no_effect"))) continue;
            return (m.MaxHP - m.HP, text);
        }
        throw new InvalidOperationException("Turn Undead fizzled 60 times");
    }

    [Fact]
    public void HolyUndead135_TurnUndeadIsSpellSix()
    {
        SpellSystem.GetSpellInfo(CharacterClass.Cleric, 6)!.Name.Should().Be("Turn Undead");
    }

    [Theory]
    [InlineData(0)]
    [InlineData(1)]
    [InlineData(2)]
    public async Task HolyUndead135_TurnUndead_CountsTheSummonedUndead(int index)
    {
        var probe = Summoned(index);
        var (lost, text) = await TurnUndead(() => Summoned(index));
        text.Should().NotContain(Loc.Get("combat.spell_turn_undead_unaffected", MonsterNames.Display(probe)),
            $"Turn Undead counts a summoned {probe.Name} as undead");
        lost.Should().BeGreaterThan(0, $"Turn Undead hurts a summoned {probe.Name}");
    }

    [Fact]
    public async Task HolyUndead135_TurnUndead_StillSparesTheLiving()
    {
        Monster Kobold() => new Monster { Name = "Kobold", Level = 20, HP = 1_000_000, MaxHP = 1_000_000 };
        var (lost, text) = await TurnUndead(Kobold);
        text.Should().Contain(Loc.Get("combat.spell_turn_undead_unaffected", MonsterNames.Display(Kobold())));
        lost.Should().Be(0);
    }
}
