using System;
using System.IO;
using System.Linq;
using System.Reflection;
using FluentAssertions;
using UsurperRemake;
using UsurperRemake.Systems;
using UsurperRemake.UI;
using Xunit;

namespace UsurperReborn.Tests;

/// <summary>
/// 1.2.8: the Gnoll racial bite poisons on a melee hit only. A spell, a magical ability or a
/// ranged hit never bites. The 15% roll is forced by running many seeded hits.
/// </summary>
[Collection("SharedGameSingletons")]
public class GnollBiteMeleeOnly128Tests
{
    private const int Hits = 300;

    private static readonly MethodInfo PostHit = typeof(CombatEngine).GetMethod(
        "ApplyPostHitEnchantments", BindingFlags.Instance | BindingFlags.NonPublic)!;

    private static int PoisonedHits(CharacterRace race, bool isSpell, bool notMelee, WeaponType? weapon = null)
    {
        var engine = new CombatEngine(new TerminalEmulator(new MemoryStream(), new MemoryStream()));
        engine.SeedRandomForTests(128);
        var attacker = new Character { Name2 = "Biter", Class = CharacterClass.Warrior, Race = race, Level = 10, HP = 200, MaxHP = 200 };
        if (weapon != null)
            attacker.EquippedItems[EquipmentSlot.MainHand] = EquipmentDatabase.RegisterDynamic(new Equipment
            {
                Name = "Test " + weapon, Slot = EquipmentSlot.MainHand, WeaponType = weapon.Value,
                WeaponPower = 10, MinLevel = 1, Rarity = EquipmentRarity.Common,
            });
        var result = new CombatResult { Player = attacker };
        int poisoned = 0;
        for (int i = 0; i < Hits; i++)
        {
            var monster = new Monster { Name = "Rat", Level = 5, HP = 1000, MaxHP = 1000, IsActive = true };
            PostHit.Invoke(engine, new object[] { attacker, monster, 10L, result, isSpell, EquipmentSlot.MainHand, notMelee });
            if (monster.Poisoned) poisoned++;
        }
        return poisoned;
    }

    [Fact]
    public void GnollMeleeHit_CanPoison() =>
        PoisonedHits(CharacterRace.Gnoll, isSpell: false, notMelee: false).Should().BeGreaterThan(0);

    [Fact]
    public void GnollSpellHit_NeverPoisons() =>
        PoisonedHits(CharacterRace.Gnoll, isSpell: true, notMelee: false).Should().Be(0);

    [Fact]
    public void GnollRangedHit_NeverPoisons() =>
        PoisonedHits(CharacterRace.Gnoll, isSpell: false, notMelee: true).Should().Be(0);

    [Fact]
    public void GnollSwingWithABowInHand_NeverPoisons() =>
        PoisonedHits(CharacterRace.Gnoll, isSpell: false, notMelee: false, WeaponType.Bow).Should().Be(0);

    [Fact]
    public void GnollSwingWithAMeleeWeapon_CanPoison() =>
        PoisonedHits(CharacterRace.Gnoll, isSpell: false, notMelee: false, WeaponType.Sword).Should().BeGreaterThan(0);

    [Fact]
    public void NonGnollMeleeHit_NeverPoisons() =>
        PoisonedHits(CharacterRace.Human, isSpell: false, notMelee: false).Should().Be(0);

    [Fact]
    public void BowAbilitiesAreRanged_MeleeAbilitiesAreNot()
    {
        foreach (var id in new[] { "volley", "arrow_storm", "legendary_shot" })
            CombatEngine.IsRangedAbility(ClassAbilitySystem.GetAbility(id)).Should().BeTrue(id);
        CombatEngine.IsRangedAbility(ClassAbilitySystem.GetAbility("backstab")).Should().BeFalse();
        CombatEngine.IsRangedAbility(null).Should().BeFalse();
    }
}
