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
/// v1.1.15: a teammate's party heal (Veloura's Embrace) skips a fallen player instead of healing
/// (and, via WardPartyFromHeal, warding) a corpse.
/// </summary>
[Collection("SharedGameSingletons")]
public class TeammatePartyHealAlive1115Tests
{
    private const BindingFlags F = BindingFlags.NonPublic | BindingFlags.Instance;

    private static int StaffId() => EquipmentDatabase.GetAll().First(e => e.WeaponType == WeaponType.Staff).Id;

    private static Character SageTeammate(int level) => new Character
    {
        Name1 = "Sage", Name2 = "Sage", Class = CharacterClass.Sage, Level = level,
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

    /// <summary>Casts Veloura's Embrace (party heal) until it lands; the caster's own heal marks a land.</summary>
    private static bool CastSagePartyHeal(Character sage, Character leader, CombatEngine engine, CombatResult result)
    {
        for (int attempt = 0; attempt < 100; attempt++)
        {
            sage.HP = 1000;
            var cast = CastHeal(engine, sage, leader, result);
            cast.Should().BeTrue();
            if (sage.HP != 1000) return true; // landed
        }
        return false;
    }

    [Fact]
    public void ATeammatesPartyHeal_GivesADeadPlayerNoHealAndNoWard()
    {
        var sage = SageTeammate(100);
        var (engine, leader, result) = Party(sage);
        leader.HP = 0; // the player has fallen
        CastSagePartyHeal(sage, leader, engine, result).Should().BeTrue("the party heal never landed");
        leader.HP.Should().Be(0, "a fallen player gets no heal");
        leader.MagicACBonus.Should().Be(0, "a fallen player gets no ward");
        leader.ActiveStatuses.Should().NotContainKey(StatusEffect.Blessed);
    }

    [Fact]
    public void ATeammatesPartyHeal_StillHealsAndWardsALivingPlayer()
    {
        var sage = SageTeammate(100);
        var (engine, leader, result) = Party(sage);
        leader.HP = 1000;
        CastSagePartyHeal(sage, leader, engine, result).Should().BeTrue("the party heal never landed");
        leader.HP.Should().BeGreaterThan(1000, "a living player is still healed by the party heal");
        leader.MagicACBonus.Should().BeGreaterThan(0, "a living player is still warded by the party heal");
    }
}
