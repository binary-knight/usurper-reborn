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
    public void Disambiguate_RetiredName_GetsAnotherSurname_EvenWithNoCorpseInRoster()
    {
        NPCNameRegistry.Reset();
        var spawner = NPCSpawnSystem.Instance;
        NPCNameRegistry.Reserve("Jocelyn Holloway"); // permadied and pruned long ago

        // v1.1.13: first name kept, surname changed, no numeral
        var first = spawner.DisambiguateNPCName("Jocelyn Holloway");
        var second = spawner.DisambiguateNPCName("Jocelyn Holloway");
        foreach (var name in new[] { first, second })
        {
            name.Should().StartWith("Jocelyn ").And.NotBe("Jocelyn Holloway");
            name.Split(' ').Should().HaveCount(2);
            NPCSpawnSystem.AllSurnames.Should().Contain(name.Split(' ')[1]);
            HasNumeral(name).Should().BeFalse();
        }
        first.Should().NotBe(second);
    }

    private static bool HasNumeral(string name) =>
        name.Split(' ', StringSplitOptions.RemoveEmptyEntries).Any(NPCSpawnSystem.IsRomanNumeralToken);

    private static string RepoRoot()
    {
        var dir = new System.IO.DirectoryInfo(AppContext.BaseDirectory);
        while (dir != null && !(System.IO.Directory.Exists(System.IO.Path.Combine(dir.FullName, "Scripts"))
                                && System.IO.Directory.Exists(System.IO.Path.Combine(dir.FullName, "Localization"))))
            dir = dir.Parent;
        dir.Should().NotBeNull("the test must run inside the repository");
        return dir!.FullName;
    }

    /// <summary>v1.1.13: "Ursula II" taken yields "Ursula Surname", never "Ursula II III".</summary>
    [Fact]
    public void Disambiguate_NumeralCandidate_IsStripped_AndGetsASurname()
    {
        NPCNameRegistry.Reset();
        var spawner = NPCSpawnSystem.Instance;
        NPCNameRegistry.Reserve("Ursula II");
        NPCNameRegistry.Reserve("Ursula");

        var name = spawner.DisambiguateNPCName("Ursula II");
        name.Should().StartWith("Ursula ");
        name.Split(' ').Should().HaveCount(2);
        NPCSpawnSystem.AllSurnames.Should().Contain(name.Split(' ')[1]);
        HasNumeral(name).Should().BeFalse();

        // A free stripped name is used as is
        spawner.DisambiguateNPCName("Darian IV III").Should().Be("Darian");
    }

    [Fact]
    public void Disambiguate_NumeralOnlyCandidate_NeverEmpty_NeverNumeral()
    {
        NPCNameRegistry.Reset();
        var spawner = NPCSpawnSystem.Instance;
        spawner.DisambiguateNPCName("II").Should().Be("II"); // free: returned unchanged, not emptied
        var name = spawner.DisambiguateNPCName("II");
        name.Should().NotBeNullOrWhiteSpace();
        HasNumeral(name).Should().BeFalse();
    }

    [Fact]
    public void Disambiguate_ManyForcedCollisions_NoNumerals_AllUnique()
    {
        NPCNameRegistry.Reset();
        var spawner = NPCSpawnSystem.Instance;
        var names = new List<string>();
        var seeds = new[] { "Ursula", "Ursula II", "Ansel II VI", "Erland II II", "Jocelyn Holloway", "Jocelyn Holloway III" };
        for (int i = 0; i < 400; i++)
            names.Add(spawner.DisambiguateNPCName(seeds[i % seeds.Length]));
        for (int i = 0; i < 200; i++)
            names.Add(spawner.DisambiguateNPCName(seeds[i % seeds.Length], keepSurname: true));

        names.Should().OnlyHaveUniqueItems(n => n.ToLowerInvariant());
        names.Should().OnlyContain(n => !HasNumeral(n) && !string.IsNullOrWhiteSpace(n));
        names.Should().OnlyContain(n => !System.Text.RegularExpressions.Regex.IsMatch(n, @"\b[0-9a-f]{4}$"));
    }

    /// <summary>v1.1.13: a taken child name keeps the family surname and gets another first name, exactly two words.</summary>
    [Fact]
    public void Disambiguate_KeepSurname_GetsOtherFirstNameWithFamilySurname()
    {
        NPCNameRegistry.Reset();
        var spawner = NPCSpawnSystem.Instance;
        NPCNameRegistry.Reserve("Wren Holloway");

        var name = spawner.DisambiguateNPCName("Wren Holloway", keepSurname: true, sex: CharacterSex.Female);
        var parts = name.Split(' ');
        parts.Should().HaveCount(2, "no middle surname");
        parts[1].Should().Be("Holloway");
        parts[0].Should().NotBe("Wren");
        NPCSpawnSystem.ImmigrantFemaleNames.Should().Contain(parts[0]);
        NPCSpawnSystem.AllSurnames.Should().NotContain(parts[0]);
        HasNumeral(name).Should().BeFalse();
        NPCNameRegistry.IsTaken(name).Should().BeTrue();

        // Sex decides the pool even when the typed first name is from the other one
        NPCNameRegistry.Reserve("Bram Holloway");
        var boy = spawner.DisambiguateNPCName("Bram Holloway", keepSurname: true, sex: CharacterSex.Female).Split(' ');
        NPCSpawnSystem.ImmigrantFemaleNames.Should().Contain(boy[0]);
        boy[1].Should().Be("Holloway");
    }

    [Fact]
    public void Disambiguate_KeepSurname_ManyCollisions_AllUniqueTwoWordsSameSurname()
    {
        NPCNameRegistry.Reset();
        var spawner = NPCSpawnSystem.Instance;
        NPCNameRegistry.Reserve("Bram Holloway");
        var names = new List<string>();
        for (int i = 0; i < 60; i++)
            names.Add(spawner.DisambiguateNPCName("Bram Holloway", keepSurname: true, sex: CharacterSex.Male));

        names.Should().OnlyHaveUniqueItems(n => n.ToLowerInvariant());
        names.Should().NotContain("Bram Holloway");
        foreach (var n in names)
        {
            n.Split(' ').Should().HaveCount(2, n);
            n.Should().EndWith(" Holloway");
            HasNumeral(n).Should().BeFalse();
            NPCSpawnSystem.ImmigrantMaleNames.Should().Contain(n.Split(' ')[0]);
        }
    }

    /// <summary>v1.1.13: every first name taken with the surname: two first names, surname last, stable on reload.</summary>
    [Fact]
    public void Disambiguate_KeepSurname_FirstNamesExhausted_TwoFirstNamesThenSurname()
    {
        NPCNameRegistry.Reset();
        var spawner = NPCSpawnSystem.Instance;
        foreach (var f in NPCSpawnSystem.ImmigrantFemaleNames)
            NPCNameRegistry.Reserve($"{f} Copperfield");

        var name = spawner.DisambiguateNPCName("Wren Copperfield", keepSurname: true, sex: CharacterSex.Female);
        var parts = name.Split(' ');
        parts.Should().HaveCount(3);
        parts[2].Should().Be("Copperfield");
        NPCSpawnSystem.ImmigrantFemaleNames.Should().Contain(parts[0]).And.Contain(parts[1]);
        NPCSpawnSystem.AllSurnames.Should().NotContain(parts[1]);
        HasNumeral(name).Should().BeFalse();

        var birth = new DateTime(2026, 9, 2, 12, 0, 0);
        FamilySystem.Instance.DeserializeChildren(new List<ChildData>
        {
            new ChildData { Name = name, Mother = "Lucinda Copperfield", Father = "Bram Copperfield", Sex = (int)CharacterSex.Female,
                            MotherID = "m1", FatherID = "f1", BirthDate = birth, Named = true },
        });
        FamilySystem.Instance.AllChildren.Single(c => c.BirthDate == birth).Name.Should().Be(name);
    }

    [Fact]
    public void Disambiguate_PoolExhausted_StillUnique_NoNumerals()
    {
        NPCNameRegistry.Reset();
        var spawner = NPCSpawnSystem.Instance;
        NPCNameRegistry.Reserve("Ursula");
        foreach (var s in NPCSpawnSystem.AllSurnames)
            NPCNameRegistry.Reserve($"Ursula {s}");

        var name = spawner.DisambiguateNPCName("Ursula");
        name.Should().NotStartWith("Ursula ");
        name.Split(' ').Should().HaveCount(2);
        NPCSpawnSystem.ImmigrantFemaleNames.Should().Contain(name.Split(' ')[0]);
        HasNumeral(name).Should().BeFalse();
        NPCNameRegistry.IsTaken(name).Should().BeTrue();
    }

    [Fact]
    public void Disambiguate_ExistingNumeralNamesInRoster_StayUntouched()
    {
        NPCNameRegistry.Reset();
        var spawner = NPCSpawnSystem.Instance;
        spawner.ClearAllNPCs();
        try
        {
            var old = new NPC { Name1 = "Ansel II VI", Name2 = "Ansel II VI", ID = "npc_legacy_1" };
            spawner.AddRestoredNPC(old);

            var fresh = spawner.DisambiguateNPCName("Ansel II VI");
            fresh.Should().Be("Ansel");
            spawner.DisambiguateNPCName("Ansel").Should().StartWith("Ansel ").And.NotContain(" II");

            old.Name2.Should().Be("Ansel II VI");
            spawner.ActiveNPCs.Should().Contain(n => n.Name2 == "Ansel II VI");
            // A child reserved at birth under a legacy numeral name graduates under it
            NPCNameRegistry.Reserve("Bram Copperfield II");
            spawner.DisambiguateNPCName("Bram Copperfield II", alreadyReserved: true).Should().Be("Bram Copperfield II");
        }
        finally
        {
            spawner.ClearAllNPCs();
        }
    }

    /// <summary>v1.1.13: no naming path may append a numeral or a guid fragment again.</summary>
    [Fact]
    public void SourceGuard_NoNumeralOrFragmentSuffixInNaming()
    {
        foreach (var file in System.IO.Directory.EnumerateFiles(System.IO.Path.Combine(RepoRoot(), "Scripts"), "*.cs", System.IO.SearchOption.AllDirectories))
        {
            var src = System.IO.File.ReadAllText(file);
            src.Should().NotContain("ToRomanNumeral", file);
        }
        var spawn = System.IO.File.ReadAllText(System.IO.Path.Combine(RepoRoot(), "Scripts", "Systems", "NPCSpawnSystem.cs"));
        spawn.Should().NotContain("Guid.NewGuid().ToString(\"N\").Substring(0, 4)");
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
    public void RegisteredChild_TakingARetiredName_GetsOtherFirstName()
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
            Sex = CharacterSex.Male,
            BirthDate = new DateTime(2026, 9, 1, 12, 0, 0),
        };
        FamilySystem.Instance.RegisterChild(child);

        // v1.1.13: family surname kept, another male first name, no middle surname, no numeral
        var parts = child.Name.Split(' ');
        parts.Should().HaveCount(2);
        parts[1].Should().Be("Copperfield");
        parts[0].Should().NotBe("Halvar");
        NPCSpawnSystem.ImmigrantMaleNames.Should().Contain(parts[0]);
        HasNumeral(child.Name).Should().BeFalse();
        NPCNameRegistry.IsTaken(child.Name).Should().BeTrue();

        // The load migration leaves the new first name alone: no rename on the next load
        var saved = child.Name;
        FamilySystem.Instance.DeserializeChildren(new List<ChildData>
        {
            new ChildData { Name = saved, Mother = "Lucinda Copperfield", Father = "Bram Copperfield",
                            MotherID = "m1", FatherID = "f1", BirthDate = child.BirthDate, Named = true },
            // Legacy: father with a numeral gave the child that numeral as surname; left alone
            new ChildData { Name = "Wren III", Mother = "Lucinda Copperfield", Father = "Darian IV III",
                            MotherID = "m1", FatherID = "f3", BirthDate = child.BirthDate.AddHours(1), Named = true },
        });
        FamilySystem.Instance.AllChildren.Single(c => c.FatherID == "f1").Name.Should().Be(saved);
        FamilySystem.Instance.AllChildren.Single(c => c.FatherID == "f3").Name.Should().Be("Wren III");
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
