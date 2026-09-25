using System.Threading.Tasks;
using FluentAssertions;
using UsurperRemake.Locations;
using Xunit;

namespace UsurperReborn.Tests;

/// <summary>v1.1.13: Equip Best weighs the stats the companion's role uses, so a tank no longer gets a caster necklace.</summary>
[Collection("SharedGameSingletons")]
public class RoleAwareEquipBest1113Tests
{
    // Under the old flat weights the Wis/Int necklace (60) outscored the Con/HP one (55)
    private static Item ToughNecklace()
    {
        var it = new Item { Name = "Test Necklace of Toughness", Type = ObjType.Neck, HP = 20, IsIdentified = true };
        it.LootEffects.Add(((int)LootGenerator.SpecialEffect.Constitution, 5));
        return it;
    }

    private static Item SageNecklace()
    {
        var it = new Item { Name = "Test Necklace of Insight", Type = ObjType.Neck, Wisdom = 10, IsIdentified = true };
        it.LootEffects.Add(((int)LootGenerator.SpecialEffect.Intelligence, 10));
        return it;
    }

    private static async Task<string> EquipBestNeck(CharacterClass cls)
    {
        var npc = TeamCornerRig.Npc("npc-role-" + cls, "Roler", "");
        npc.Class = cls;
        var hero = TeamCornerRig.Hero();
        hero.Inventory.Add(SageNecklace());
        hero.Inventory.Add(ToughNecklace());
        BaseLocation.GearSaveHookForTests = _ => Task.CompletedTask;
        try { await new TeamCornerRig(hero, new[] { "Y" }).Run("RunEquipBestGear", npc); }
        finally { BaseLocation.GearSaveHookForTests = null; }
        hero.Inventory.Should().ContainSingle("one necklace went on, the other stayed in the pack");
        return npc.GetEquipment(EquipmentSlot.Neck)!.Name;
    }

    [Fact]
    public async Task Tank_PrefersTheConstitutionHpNecklace()
    {
        (await EquipBestNeck(CharacterClass.Warrior)).Should().Be("Test Necklace of Toughness");
    }

    [Fact]
    public async Task Caster_PrefersTheWisdomIntelligenceNecklace()
    {
        (await EquipBestNeck(CharacterClass.Magician)).Should().Be("Test Necklace of Insight");
    }
}
