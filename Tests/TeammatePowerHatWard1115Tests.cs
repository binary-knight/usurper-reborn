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
/// v1.1.15: a teammate's single-target heal that carries a ward (Power Hat) now wards the target
/// the way the player's own cast does (ApplyHealTo / WardAlly), instead of healing with no ward.
/// </summary>
[Collection("SharedGameSingletons")]
public class TeammatePowerHatWard1115Tests
{
    private const BindingFlags F = BindingFlags.NonPublic | BindingFlags.Instance;

    private static int StaffId() => EquipmentDatabase.GetAll().First(e => e.WeaponType == WeaponType.Staff).Id;

    private static Character Magician(int level) => new Character
    {
        Name1 = "Magician", Name2 = "Magician", Class = CharacterClass.Magician, Level = level,
        Wisdom = 50, Intelligence = 50, HP = 100_000, MaxHP = 100_000, Mana = 100_000, MaxMana = 100_000,
        CombatSpeed = CombatSpeed.Instant,
        EquippedItems = new Dictionary<EquipmentSlot, int> { [EquipmentSlot.MainHand] = StaffId() },
    };

    private static Character Ally(string name, CharacterClass cls) => new Character
    {
        Name1 = name, Name2 = name, Class = cls, Level = 40, HP = 5_000, MaxHP = 5_000, CombatSpeed = CombatSpeed.Instant,
    };

    /// <summary>A teammate beside a leader (leader is both currentPlayer and result.Player).</summary>
    private static (CombatEngine engine, Character leader, CombatResult result) Party(Character teammate)
    {
        var leader = Ally("Leader", CharacterClass.Ranger);
        var engine = new CombatEngine(new TerminalEmulator(new MemoryStream(), new MemoryStream()));
        typeof(CombatEngine).GetField("random", F)!.SetValue(engine, new Random(7));
        typeof(CombatEngine).GetField("currentPlayer", F)!.SetValue(engine, leader);
        typeof(CombatEngine).GetField("currentTeammates", F)!.SetValue(engine, new List<Character> { teammate });
        return (engine, leader, new CombatResult { Player = leader });
    }

    private static bool CastHeal(CombatEngine engine, Character teammate, Character target, CombatResult result) =>
        ((Task<bool>)typeof(CombatEngine).GetMethod("TeammateHealWithSpell", F)!
            .Invoke(engine, new object[] { teammate, target, result })!).GetAwaiter().GetResult();

    /// <summary>Casts Power Hat on the leader until the cast lands (SpellSystem.CastSpell can fizzle).</summary>
    private static (Character magician, Character leader, int ward) TeammatePowerHat(int existingLeaderWard)
    {
        for (int attempt = 0; attempt < 100; attempt++)
        {
            var magician = Magician(60);
            var (engine, leader, result) = Party(magician);
            leader.HP = 1000;
            if (existingLeaderWard > 0) CombatEngine.ApplyWardHighestWins(leader, existingLeaderWard, 999);
            var cast = CastHeal(engine, magician, leader, result);
            cast.Should().BeTrue();
            if (leader.HP == 1000) continue; // the cast fizzled
            return (magician, leader, leader.MagicACBonus);
        }
        throw new Exception("Power Hat never landed");
    }

    [Fact]
    public void ATeammatesPowerHat_WardsTheTarget()
    {
        var (_, leader, ward) = TeammatePowerHat(0);
        ward.Should().BeGreaterThan(0, "Power Hat carries a ward");
        leader.ActiveStatuses.Should().ContainKey(StatusEffect.Blessed);
    }

    [Fact]
    public void ATeammatesPowerHat_KeepsAStrongerExistingWard()
    {
        var (_, leader, ward) = TeammatePowerHat(100_000);
        ward.Should().Be(100_000, "the highest ward wins on the teammate single-target path too");
    }
}
