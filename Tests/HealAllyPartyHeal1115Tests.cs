using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text;
using System.Threading.Tasks;
using FluentAssertions;
using UsurperRemake;
using UsurperRemake.Systems;
using Xunit;

namespace UsurperReborn.Tests;

/// <summary>
/// v1.1.15: a party heal (Veloura's Embrace) chosen from the player's Heal Ally menu asks for no
/// ally and becomes the spell menu's cast, so it heals and wards every living ally and skips the
/// fallen. A single-target heal from the same menu still asks for an ally and touches only that one.
/// </summary>
[Collection("SharedGameSingletons")]
public class HealAllyPartyHeal1115Tests
{
    private const BindingFlags F = BindingFlags.NonPublic | BindingFlags.Instance;
    private const int VelourasEmbraceSlot = 24;
    private const int PowerHatSlot = 8;

    private static int StaffId() => EquipmentDatabase.GetAll().First(e => e.WeaponType == WeaponType.Staff).Id;

    /// <summary>A caster who knows one heal spell and carries no potions, so the menu offers only the spell.</summary>
    private static Character Caster(CharacterClass cls, int slot)
    {
        var c = new Character
        {
            Name1 = "Caster", Name2 = "Caster", Class = cls, Level = 100,
            Wisdom = 50, Intelligence = 50, HP = 100_000, MaxHP = 100_000, Mana = 100_000, MaxMana = 100_000,
            Healing = 0, ManaPotions = 0, CombatSpeed = CombatSpeed.Instant,
            EquippedItems = new Dictionary<EquipmentSlot, int> { [EquipmentSlot.MainHand] = StaffId() },
        };
        c.Spell[slot - 1][0] = true;
        return c;
    }

    private static Character Ally(string name, long hp) => new Character
    {
        Name1 = name, Name2 = name, Class = CharacterClass.Ranger, Level = 40,
        HP = hp, MaxHP = 100_000, CombatSpeed = CombatSpeed.Instant,
    };

    private static CombatEngine Engine(string input, Character player, List<Character> teammates)
    {
        var engine = new CombatEngine(new TerminalEmulator(new MemoryStream(Encoding.UTF8.GetBytes(input)), new MemoryStream()));
        typeof(CombatEngine).GetField("random", F)!.SetValue(engine, new Random(7));
        typeof(CombatEngine).GetField("currentPlayer", F)!.SetValue(engine, player);
        typeof(CombatEngine).GetField("currentTeammates", F)!.SetValue(engine, teammates);
        return engine;
    }

    private static CombatAction? HealAlly(CombatEngine engine, Character player) =>
        ((Task<CombatAction?>)typeof(CombatEngine).GetMethod("HandleHealAlly", F)!
            .Invoke(engine, new object[] { player, new List<Monster>() })!).GetAwaiter().GetResult();

    [Fact]
    public void APartyHealFromHealAlly_HealsAndWardsEveryLivingAlly_AndSkipsTheFallen()
    {
        for (int attempt = 0; attempt < 100; attempt++)
        {
            var sage = Caster(CharacterClass.Sage, VelourasEmbraceSlot);
            sage.HP = 1000;
            var first = Ally("First", 1000);
            var fallen = Ally("Fallen", 0);
            var second = Ally("Second", 1000);
            // Input: aid option 1 (the spell), spell 1. No ally pick follows for a party heal.
            var engine = Engine("1\n1\n", sage, new List<Character> { first, fallen, second });

            var action = HealAlly(engine, sage);
            action.Should().NotBeNull("a party heal needs no ally pick");
            action!.Type.Should().Be(CombatActionType.CastSpell, "it becomes the spell menu's cast");
            action.SpellIndex.Should().Be(VelourasEmbraceSlot);
            action.AllyTargetIndex.Should().BeNull();
            action.TargetAllMonsters.Should().BeFalse();

            var result = new CombatResult { Player = sage };
            ((Task)typeof(CombatEngine).GetMethod("ExecuteSpellMultiMonster", F)!
                .Invoke(engine, new object[] { sage, new List<Monster>(), action, result })!).GetAwaiter().GetResult();
            if (first.HP == 1000) continue; // the cast fizzled

            long healed = first.HP - 1000;
            healed.Should().BeGreaterThan(0);
            second.HP.Should().Be(1000 + healed, "every living ally is healed");
            sage.HP.Should().Be(1000 + healed, "the caster is in the party");
            first.MagicACBonus.Should().BeGreaterThan(0, "the party heal wards every living ally");
            second.MagicACBonus.Should().BeGreaterThan(0);
            sage.MagicACBonus.Should().BeGreaterThan(0);
            fallen.HP.Should().Be(0, "the fallen are not healed");
            fallen.MagicACBonus.Should().Be(0, "the fallen are not warded");
            return;
        }
        throw new Exception("Veloura's Embrace never landed");
    }

    [Fact]
    public void APartyHealFromHealAlly_StillCountsAsAid()
    {
        var sage = Caster(CharacterClass.Sage, VelourasEmbraceSlot);
        var ally = Ally("Ally", 1000);
        var engine = Engine("1\n1\n", sage, new List<Character> { ally });
        var action = HealAlly(engine, sage);
        action.Should().NotBeNull();
        engine.NoteOwnerTurn(sage, new List<Character> { ally }, action!);
        ((bool)typeof(CombatEngine).GetField("_ownerAidedThisTurn", F)!.GetValue(engine)!)
            .Should().BeTrue("a party heal from the Heal Ally menu is aid to an ally");
    }

    [Fact]
    public void ASingleTargetHealFromHealAlly_TouchesOnlyTheChosenAlly()
    {
        for (int attempt = 0; attempt < 100; attempt++)
        {
            var magician = Caster(CharacterClass.Magician, PowerHatSlot);
            var lower = Ally("Lower", 1000);   // listed first (lowest HP share)
            var chosen = Ally("Chosen", 2000); // listed second
            // Input: aid option 1 (the spell), spell 1 (Power Hat), ally 2.
            var engine = Engine("1\n1\n2\n", magician, new List<Character> { lower, chosen });

            var action = HealAlly(engine, magician);
            action.Should().NotBeNull("the menu should reach the cast");
            action!.Type.Should().Be(CombatActionType.HealAlly, "a single-target heal is cast from the menu itself");
            if (chosen.HP == 2000) continue; // the cast fizzled

            chosen.HP.Should().BeGreaterThan(2000);
            chosen.MagicACBonus.Should().BeGreaterThan(0, "Power Hat wards the chosen ally");
            lower.HP.Should().Be(1000, "only the chosen ally is healed");
            lower.MagicACBonus.Should().Be(0, "only the chosen ally is warded");
            magician.MagicACBonus.Should().Be(0, "the caster is not warded by a single-target heal on an ally");
            return;
        }
        throw new Exception("Power Hat never landed");
    }
}
