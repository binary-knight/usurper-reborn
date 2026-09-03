using System.Linq;
using Xunit;
using FluentAssertions;
using UsurperRemake.Systems;

namespace UsurperReborn.Tests;

/// <summary>v1.1 gear set bonuses: player-only, threshold-exact, reversible.</summary>
[Collection("SharedGameSingletons")]
public class GearSetTests
{
    private static int Piece(string family, EquipmentSlot slot) =>
        EquipmentDatabase.RegisterDynamic(new Equipment { Name = family, Family = family, Slot = slot, ArmorClass = 1, MinLevel = 1 });

    private static void Wear(Character c, params (string family, EquipmentSlot slot)[] pieces)
    {
        foreach (var (f, s) in pieces) c.EquippedItems[s] = Piece(f, s);
        c.RecalculateStats();
    }

    private static Player Fresh()
    {
        var p = new Player { AI = CharacterAI.Human, Level = 30 };
        p.BaseStrength = 10; p.BaseDexterity = 10; p.BaseConstitution = 10; p.BaseMaxHP = 100;
        p.RecalculateStats();
        return p;
    }

    [Fact]
    public void Two_four_and_six_pieces_unlock_exactly_their_tiers()
    {
        var p = Fresh();
        long baseArm = p.ArmPow, baseHp = p.MaxHP, baseDef = p.Defence;
        Wear(p, ("Reinforced Helm", EquipmentSlot.Head));
        p.ArmPow.Should().Be(baseArm + 1, "one piece: only the item's own armor");
        Wear(p, ("Reinforced Boots", EquipmentSlot.Feet));
        p.ArmPow.Should().Be(baseArm + 2 + 3, "two pieces: +3 armor set bonus");
        Wear(p, ("Reinforced Gloves", EquipmentSlot.Hands), ("Reinforced Leggings", EquipmentSlot.Legs));
        p.MaxHP.Should().BeGreaterThan(baseHp + 30, "four pieces add 30 HP and 2 CON, and CON feeds HP");
        p.Defence.Should().Be(baseDef, "defence tier not reached at four");
        Wear(p, ("Reinforced Bracers", EquipmentSlot.Arms), ("Reinforced Belt", EquipmentSlot.Waist));
        p.ArmPow.Should().Be(baseArm + 6 + 3 + 5, "six pieces: items plus the 2- and 6-piece armor bonuses");
        p.Defence.Should().Be(baseDef + 6);
    }

    [Fact]
    public void Removing_a_piece_drops_the_tier_and_restores_base_exactly()
    {
        var p = Fresh();
        long baseArm = p.ArmPow;
        Wear(p, ("Steel Helm", EquipmentSlot.Head), ("Steel Greaves", EquipmentSlot.Legs));
        p.ArmPow.Should().Be(baseArm + 2 + 3);
        p.EquippedItems.Remove(EquipmentSlot.Legs);
        p.RecalculateStats();
        p.ArmPow.Should().Be(baseArm + 1);
    }

    [Fact]
    public void Npcs_and_companions_get_nothing_from_the_same_gear()
    {
        var npc = new Character { AI = CharacterAI.Computer, Level = 30, BaseMaxHP = 100 };
        npc.RecalculateStats();
        long baseArm = npc.ArmPow;
        Wear(npc, ("Steel Helm", EquipmentSlot.Head), ("Steel Greaves", EquipmentSlot.Legs), ("Steel Gauntlets", EquipmentSlot.Hands), ("Steel Sabatons", EquipmentSlot.Feet));
        npc.ArmPow.Should().Be(baseArm + 4, "four items, no set bonus");
        npc.Strength.Should().Be(npc.BaseStrength);
    }

    [Fact]
    public void A_player_snapshot_used_as_a_pvp_defender_gets_the_bonus()
    {
        var snap = new Character { AI = CharacterAI.Computer, IsPlayerSnapshot = true, Level = 30, BaseMaxHP = 100 };
        snap.RecalculateStats();
        long baseArm = snap.ArmPow;
        Wear(snap, ("Steel Helm", EquipmentSlot.Head), ("Steel Greaves", EquipmentSlot.Legs));
        snap.ArmPow.Should().Be(baseArm + 2 + 3);
    }

    [Theory]
    [InlineData("Forged-Thread Cape")]
    [InlineData("Titan Cleaver")]
    [InlineData("Cloak of Shadows")]
    [InlineData("Elven Chainweave")]
    [InlineData("Dragonscale Vest")]
    [InlineData("")]
    [InlineData(null)]
    public void Lookalike_names_are_not_set_pieces(string? family)
    {
        GearSetRegistry.ForFamily(family).Should().BeNull();
    }

    [Fact]
    public void Every_registered_family_is_a_real_loot_template_and_tiers_ascend()
    {
        foreach (var set in GearSetRegistry.Sets)
        {
            set.Families.Should().OnlyHaveUniqueItems();
            set.Tiers.Select(t => t.Pieces).Should().BeInAscendingOrder();
            set.MaxPieces.Should().BeLessOrEqualTo(set.Families.Count);
        }
    }

    [Fact]
    public void Clone_keeps_the_family()
    {
        var piece = new Equipment { Name = "Forged Helm", Family = "Forged Helm", Slot = EquipmentSlot.Head };
        piece.Clone().Family.Should().Be("Forged Helm");
    }
}
