using System;
using System.Collections.Generic;
using System.Linq;
using Xunit;
using FluentAssertions;
using UsurperRemake.Systems;

namespace UsurperReborn.Tests;

/// <summary>
/// v1.0.4: NPC names never repeat. A permadead NPC's name used to become available
/// again once PrunePermanentlyDeadNPCs dropped the corpse, and the immigrant name
/// pool was small enough that "Lucinda Copperfield VII" showed up in live logs.
/// NPCNameRegistry retires every name for good and every generator consults it.
/// </summary>
[Collection("SharedGameSingletons")]
public class NPCNameRegistryTests
{
    [Fact]
    public void Reserve_IsCaseInsensitive_AndReportsFirstReservationOnly()
    {
        NPCNameRegistry.Reset();

        NPCNameRegistry.Reserve("Jocelyn Holloway").Should().BeTrue();
        NPCNameRegistry.Reserve("jocelyn holloway").Should().BeFalse();
        NPCNameRegistry.IsTaken("JOCELYN HOLLOWAY").Should().BeTrue();
        NPCNameRegistry.IsTaken("Jocelyn Holloway II").Should().BeFalse();
    }

    [Fact]
    public void Export_RoundTrips_ThroughReserveAll()
    {
        NPCNameRegistry.Reset();
        NPCNameRegistry.ReserveAll(new[] { "Halvar Ashwick", "Seth Able", "" , null! });
        var exported = NPCNameRegistry.Export();

        NPCNameRegistry.Reset();
        NPCNameRegistry.ReserveAll(exported);

        NPCNameRegistry.Count.Should().Be(2);
        NPCNameRegistry.IsTaken("Seth Able").Should().BeTrue();
        NPCNameRegistry.IsTaken("Halvar Ashwick").Should().BeTrue();
    }

    [Fact]
    public void Disambiguate_FreeName_IsReturnedAndReserved()
    {
        NPCNameRegistry.Reset();
        var spawner = NPCSpawnSystem.Instance;

        spawner.DisambiguateNPCName("Orrin Fernsby").Should().Be("Orrin Fernsby");
        NPCNameRegistry.IsTaken("Orrin Fernsby").Should().BeTrue();
    }

    [Fact]
    public void Disambiguate_RetiredName_GetsNumeralSuffix_EvenWithNoCorpseInRoster()
    {
        NPCNameRegistry.Reset();
        var spawner = NPCSpawnSystem.Instance;
        NPCNameRegistry.Reserve("Jocelyn Holloway"); // permadied and pruned long ago

        spawner.DisambiguateNPCName("Jocelyn Holloway").Should().Be("Jocelyn Holloway II");
        spawner.DisambiguateNPCName("Jocelyn Holloway").Should().Be("Jocelyn Holloway III");
    }

    [Fact]
    public void Disambiguate_AlreadyReserved_ReturnsExactNameWhenRosterIsClear()
    {
        NPCNameRegistry.Reset();
        var spawner = NPCSpawnSystem.Instance;
        NPCNameRegistry.Reserve("Bram Copperfield"); // reserved at birth for this child

        spawner.DisambiguateNPCName("Bram Copperfield", alreadyReserved: true).Should().Be("Bram Copperfield");
    }

    [Fact]
    public void Immigrants_NeverRepeatNames()
    {
        NPCNameRegistry.Reset();
        var spawner = NPCSpawnSystem.Instance;

        var names = new List<string>();
        for (int i = 0; i < 300; i++)
        {
            var sex = i % 2 == 0 ? CharacterSex.Male : CharacterSex.Female;
            var npc = spawner.GenerateImmigrantNPC(CharacterRace.Human, sex, 10);
            npc.Should().NotBeNull();
            names.Add(npc!.Name2);
        }

        names.Should().OnlyHaveUniqueItems();
        names.Should().OnlyContain(n => NPCNameRegistry.IsTaken(n));
    }

    [Fact]
    public void RegisteredChild_TakingARetiredName_IsSuffixed()
    {
        NPCNameRegistry.Reset();
        NPCNameRegistry.Reserve("Halvar Copperfield"); // an NPC carried this once

        var child = new Child
        {
            Name = "Halvar Copperfield",
            Mother = "Lucinda Copperfield",
            Father = "Bram Copperfield",
            MotherID = "m1",
            FatherID = "f1",
            BirthDate = new DateTime(2026, 9, 1, 12, 0, 0),
        };
        FamilySystem.Instance.RegisterChild(child);

        child.Name.Should().Be("Halvar Copperfield II");
        NPCNameRegistry.IsTaken("Halvar Copperfield II").Should().BeTrue();
    }

    /// <summary>
    /// The child-surname migration that runs on every load used to strip all Roman
    /// numerals. That would have undone a uniqueness suffix at the next login and
    /// recreated the duplicate. It now keeps a trailing numeral.
    /// </summary>
    [Fact]
    public void ChildNameMigrationOnLoad_KeepsUniquenessNumeral()
    {
        NPCNameRegistry.Reset();
        var family = FamilySystem.Instance;
        var birth = new DateTime(2026, 9, 1, 8, 0, 0);
        family.DeserializeChildren(new List<ChildData>
        {
            new ChildData { Name = "Halvar Copperfield II", Mother = "Lucinda Copperfield", Father = "Bram Copperfield",
                            MotherID = "m1", FatherID = "f1", BirthDate = birth, Named = true },
            // Father with no extractable surname: the migration generates one, numeral must survive
            new ChildData { Name = "Wren Placeholder III", Mother = "Lucinda Copperfield", Father = "Shadow",
                            MotherID = "m1", FatherID = "f2", BirthDate = birth.AddHours(1), Named = true },
            // Past ten namesakes DisambiguateNPCName appends a 4-char fragment; that must survive too
            new ChildData { Name = "Anna Copperfield a3f9", Mother = "Lucinda Copperfield", Father = "Bram Copperfield",
                            MotherID = "m1", FatherID = "f1", BirthDate = birth.AddHours(2), Named = true },
        });

        family.AllChildren.Single(c => c.BirthDate == birth.AddHours(2)).Name.Should().Be("Anna Copperfield a3f9");

        var kept = family.AllChildren.Single(c => c.FatherID == "f1" && c.BirthDate == birth);
        kept.Name.Should().Be("Halvar Copperfield II");
        NPCNameRegistry.IsTaken("Halvar Copperfield II").Should().BeTrue();

        var generated = family.AllChildren.Single(c => c.FatherID == "f2");
        // Wrong surname is rewritten (father "Shadow" has none, so one is generated); the suffix is dropped
        // with the wrong surname, and the rewrite goes through the registry
        generated.Name.Should().StartWith("Wren ").And.NotContain("Placeholder");
        NPCNameRegistry.IsTaken(generated.Name).Should().BeTrue();
    }
}
