using System;
using System.Linq;
using System.Reflection;
using Xunit;
using FluentAssertions;
using UsurperRemake.Systems;

namespace UsurperReborn.Tests;

/// <summary>v1.1: NPCs get gear set bonuses, so the outfitter must actually dress some of them in sets.</summary>
[Collection("SharedGameSingletons")]
public class NPCSetOutfittingTests
{
    private static void Outfit(NPC npc)
    {
        var sys = NPCSpawnSystem.Instance ?? (NPCSpawnSystem)Activator.CreateInstance(typeof(NPCSpawnSystem), true)!;
        var m = typeof(NPCSpawnSystem).GetMethod("GiveStartingEquipment", BindingFlags.NonPublic | BindingFlags.Instance)!;
        m.Invoke(sys, new object[] { npc });
    }

    [Theory]
    [InlineData(15)]
    [InlineData(30)]
    [InlineData(70)]
    public void A_share_of_spawned_npcs_reach_a_set_tier(int level)
    {
        int reached = 0;
        for (int i = 0; i < 120; i++)
        {
            var npc = new NPC { Level = level, AI = CharacterAI.Computer, Class = (CharacterClass)(i % 8) };
            Outfit(npc);
            var best = GearSetRegistry.CountEquipped(npc).Select(c => c.Count).DefaultIfEmpty(0).Max();
            if (best >= 2) reached++;
        }
        reached.Should().BeGreaterThan(0, "set wearers must exist at level {0}", level);
        reached.Should().BeLessThan(120, "not every NPC should be a set wearer");
    }

    [Fact]
    public void Set_pieces_come_only_from_the_requested_families()
    {
        var reinforced = GearSetRegistry.Sets.First(s => s.Id == "reinforced");
        var piece = EquipmentDatabase.GetBestAffordableInFamilies(EquipmentSlot.Head, 1_000_000, reinforced.Families);
        piece.Should().NotBeNull();
        reinforced.Families.Should().Contain(piece!.Family);
        EquipmentDatabase.GetBestAffordableInFamilies(EquipmentSlot.Head, 0, reinforced.Families).Should().BeNull();
    }
}
