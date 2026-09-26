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
/// v1.1.15: the player's own Heal Ally menu (HandleHealAlly) wards the ally when the heal spell
/// carries a ward (Power Hat), the same as the combat cast and a teammate's single-target heal.
/// </summary>
[Collection("SharedGameSingletons")]
public class HealAllyPowerHatWard1115Tests
{
    private const BindingFlags F = BindingFlags.NonPublic | BindingFlags.Instance;
    private const int PowerHatSlot = 8;

    private static int StaffId() => EquipmentDatabase.GetAll().First(e => e.WeaponType == WeaponType.Staff).Id;

    /// <summary>A Magician who knows only Power Hat and carries no potions, so the menu offers only the spell.</summary>
    private static Character Magician()
    {
        var m = new Character
        {
            Name1 = "Magician", Name2 = "Magician", Class = CharacterClass.Magician, Level = 60,
            Wisdom = 50, Intelligence = 50, HP = 100_000, MaxHP = 100_000, Mana = 100_000, MaxMana = 100_000,
            Healing = 0, ManaPotions = 0, CombatSpeed = CombatSpeed.Instant,
            EquippedItems = new Dictionary<EquipmentSlot, int> { [EquipmentSlot.MainHand] = StaffId() },
        };
        m.Spell[PowerHatSlot - 1][0] = true;
        return m;
    }

    private static Character Ally() => new Character
    {
        Name1 = "Ally", Name2 = "Ally", Class = CharacterClass.Ranger, Level = 40,
        HP = 1000, MaxHP = 5_000, CombatSpeed = CombatSpeed.Instant,
    };

    /// <summary>Drives the menu: aid option 1 (the spell), ally 1, spell 1 (Power Hat). Retries a fizzle.</summary>
    private static Character HealAllyWithPowerHat(int existingWard)
    {
        for (int attempt = 0; attempt < 100; attempt++)
        {
            var player = Magician();
            var ally = Ally();
            if (existingWard > 0) CombatEngine.ApplyWardHighestWins(ally, existingWard, 999);
            var term = new TerminalEmulator(new MemoryStream(Encoding.UTF8.GetBytes("1\n1\n1\n")), new MemoryStream());
            var engine = new CombatEngine(term);
            typeof(CombatEngine).GetField("random", F)!.SetValue(engine, new Random(7));
            typeof(CombatEngine).GetField("currentPlayer", F)!.SetValue(engine, player);
            typeof(CombatEngine).GetField("currentTeammates", F)!.SetValue(engine, new List<Character> { ally });
            var action = ((Task<CombatAction?>)typeof(CombatEngine).GetMethod("HandleHealAlly", F)!
                .Invoke(engine, new object[] { player, new List<Monster>() })!).GetAwaiter().GetResult();
            action.Should().NotBeNull("the menu should reach the cast");
            if (ally.HP == 1000) continue; // the cast fizzled
            return ally;
        }
        throw new Exception("Power Hat never landed");
    }

    [Fact]
    public void HealAllyPowerHat_WardsTheAlly()
    {
        var ally = HealAllyWithPowerHat(0);
        ally.HP.Should().BeGreaterThan(1000);
        ally.MagicACBonus.Should().BeGreaterThan(0, "Power Hat carries a ward");
        ally.ActiveStatuses.Should().ContainKey(StatusEffect.Blessed);
    }

    [Fact]
    public void HealAllyPowerHat_KeepsAStrongerExistingWard()
    {
        var ally = HealAllyWithPowerHat(100_000);
        ally.MagicACBonus.Should().Be(100_000, "the highest ward wins on the Heal Ally path too");
    }
}
