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
/// v1.1.15: a teammate's party heal heals and wards one list (LivingPartyOf): the caster, the living
/// teammates and the living combat owner (result.Player). Before, the heal went to currentPlayer while
/// the ward went to result.Player, so when the two differed (a grouped follower's turn) the leader was
/// warded but never healed and the follower was healed twice.
/// </summary>
[Collection("SharedGameSingletons")]
public class TeammatePartyHealSet1115Tests
{
    private const BindingFlags F = BindingFlags.NonPublic | BindingFlags.Instance;

    private static int StaffId() => EquipmentDatabase.GetAll().First(e => e.WeaponType == WeaponType.Staff).Id;

    private static Character SageTeammate() => new Character
    {
        Name1 = "Sage", Name2 = "Sage", Class = CharacterClass.Sage, Level = 100,
        Wisdom = 50, Intelligence = 50, HP = 1000, MaxHP = 100_000, Mana = 100_000, MaxMana = 100_000,
        CombatSpeed = CombatSpeed.Instant,
        EquippedItems = new Dictionary<EquipmentSlot, int> { [EquipmentSlot.MainHand] = StaffId() },
    };

    private static Character Ally(string name) => new Character
    {
        Name1 = name, Name2 = name, Class = CharacterClass.Ranger, Level = 40,
        HP = 1000, MaxHP = 100_000, CombatSpeed = CombatSpeed.Instant,
    };

    /// <summary>
    /// The leader owns the combat (result.Player, not in currentTeammates); currentPlayer is a
    /// follower who sits in currentTeammates, as during a grouped follower's turn.
    /// </summary>
    private static (Character sage, Character leader, Character follower) CastUntilLanded()
    {
        for (int attempt = 0; attempt < 100; attempt++)
        {
            var sage = SageTeammate();
            var leader = Ally("Leader");
            var follower = Ally("Follower");
            var engine = new CombatEngine(new TerminalEmulator(new MemoryStream(), new MemoryStream()));
            typeof(CombatEngine).GetField("random", F)!.SetValue(engine, new Random(7));
            typeof(CombatEngine).GetField("currentPlayer", F)!.SetValue(engine, follower);
            typeof(CombatEngine).GetField("currentTeammates", F)!.SetValue(engine, new List<Character> { sage, follower });
            var result = new CombatResult { Player = leader };
            var cast = ((Task<bool>)typeof(CombatEngine).GetMethod("TeammateHealWithSpell", F)!
                .Invoke(engine, new object[] { sage, leader, result })!).GetAwaiter().GetResult();
            cast.Should().BeTrue();
            if (sage.HP == 1000) continue; // the cast fizzled
            return (sage, leader, follower);
        }
        throw new Exception("the party heal never landed");
    }

    [Fact]
    public void ATeammatesPartyHeal_HealsEveryoneItWards()
    {
        var (sage, leader, follower) = CastUntilLanded();
        long healed = sage.HP - 1000;
        leader.MagicACBonus.Should().BeGreaterThan(0, "the party heal wards the combat owner");
        leader.HP.Should().Be(1000 + healed, "whoever the party heal wards, it also heals");
        follower.HP.Should().Be(1000 + healed, "a follower is healed once, not twice");
        follower.MagicACBonus.Should().BeGreaterThan(0);
    }
}
