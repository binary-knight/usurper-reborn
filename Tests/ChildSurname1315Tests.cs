using System;
using System.Collections.Generic;
using System.Linq;
using Xunit;
using FluentAssertions;
using UsurperRemake.Systems;

namespace UsurperReborn.Tests;

/// <summary>
/// 1.3.15: a child's last name is the parent's to keep or change.
/// A player with a one-word name (no surname of their own) had children who took the mother's
/// surname at birth, Ravenswood; the load pass replaced it at every load with a surname generated
/// from the father's name, and "Bubbles" hashes to Oldfield. The load pass now only gives a surname
/// to a child who has none, and Home offers [L] to set a child's last name (or all the children's).
/// </summary>
[Collection("SharedGameSingletons")]
public class ChildSurname1315Tests : IDisposable
{
    private readonly List<ChildData> _childrenBefore;
    private static readonly DateTime Birth = new DateTime(2026, 11, 25, 9, 0, 0);

    public ChildSurname1315Tests()
    {
        _childrenBefore = FamilySystem.Instance.SerializeChildren();
    }

    public void Dispose()
    {
        FamilySystem.Instance.DeserializeChildren(_childrenBefore);
    }

    private static ChildData Kid(string name, string father, string mother, string fatherId, int hour) => new ChildData
    {
        Name = name, Father = father, Mother = mother, FatherID = fatherId, MotherID = "m-1315",
        Sex = (int)CharacterSex.Female, BirthDate = Birth.AddHours(hour), Named = true,
    };

    private static string NameOf(string fatherId) => FamilySystem.Instance.AllChildren.Single(c => c.FatherID == fatherId).Name;

    [Fact]
    public void Load_KeepsTheMothersSurname_WhenTheFathersNameIsOneWord()
    {
        NPCNameRegistry.Reset();
        FamilySystem.Instance.DeserializeChildren(new List<ChildData>
        {
            Kid("Alice Ravenswood", "Bubbles", "Nessa Ravenswood", "f-1315-a", 0),
        });

        // Before 1.3.15 this load gave "Alice Oldfield" (the surname generated from "Bubbles")
        NameOf("f-1315-a").Should().Be("Alice Ravenswood");
    }

    [Fact]
    public void Load_KeepsAnySurnameTheChildHas_EvenOneThatDiffersFromTheFathers()
    {
        NPCNameRegistry.Reset();
        FamilySystem.Instance.DeserializeChildren(new List<ChildData>
        {
            Kid("Grace Oakshield", "Bram Copperfield", "Lucinda Copperfield", "f-1315-b", 1),
        });
        NameOf("f-1315-b").Should().Be("Grace Oakshield");
    }

    [Fact]
    public void Load_StillGivesASurname_ToAChildWithAOneWordName()
    {
        NPCNameRegistry.Reset();
        FamilySystem.Instance.DeserializeChildren(new List<ChildData>
        {
            Kid("Diana", "Bram Copperfield", "Lucinda Copperfield", "f-1315-c", 2),
        });
        NameOf("f-1315-c").Should().Be("Diana Copperfield");
    }

    [Fact]
    public void ASurnameSetAtHome_SurvivesTheNextLoad()
    {
        NPCNameRegistry.Reset();
        FamilySystem.Instance.DeserializeChildren(new List<ChildData>
        {
            Kid("Henry Oldfield", "Bubbles", "Nessa Ravenswood", "f-1315-d", 3),
        });
        var child = FamilySystem.Instance.AllChildren.Single(c => c.FatherID == "f-1315-d");
        child.Name = FamilySystem.WithSurname(child.Name, "Ravenswood");
        child.Name.Should().Be("Henry Ravenswood");

        var saved = FamilySystem.Instance.SerializeChildren().Single(c => c.FatherID == "f-1315-d");
        FamilySystem.Instance.AllChildren.Remove(child);
        FamilySystem.Instance.DeserializeChildren(new List<ChildData> { saved });
        NameOf("f-1315-d").Should().Be("Henry Ravenswood");
    }

    [Fact]
    public void TryGiveSurname_AllowsTheChildsOwnEarlierName_ThoughTheRegistryHoldsIt()
    {
        NPCNameRegistry.Reset();
        NPCNameRegistry.Reserve("Henry Ravenswood");   // the birth name, kept by the registry after the load pass renamed him
        FamilySystem.Instance.DeserializeChildren(new List<ChildData>
        {
            Kid("Henry Oldfield", "Bubbles", "Nessa Ravenswood", "f-1315-e", 4),
        });
        var child = FamilySystem.Instance.AllChildren.Single(c => c.FatherID == "f-1315-e");
        FamilySystem.Instance.TryGiveSurname(child, "Ravenswood", null, out var newName).Should().BeTrue();
        newName.Should().Be("Henry Ravenswood");
        child.Name.Should().Be("Henry Ravenswood");
    }

    [Fact]
    public void NEGATIVE_TryGiveSurname_RefusesANameAnotherChildBears_AndLeavesTheNameAsItWas()
    {
        NPCNameRegistry.Reset();
        FamilySystem.Instance.DeserializeChildren(new List<ChildData>
        {
            Kid("Jane Ravenswood", "Bram Ravenswood", "Lucinda Ravenswood", "f-1315-f", 5),
            Kid("Jane Oldfield", "Bubbles", "Nessa Ravenswood", "f-1315-g", 6),
        });
        var child = FamilySystem.Instance.AllChildren.Single(c => c.FatherID == "f-1315-g");
        FamilySystem.Instance.TryGiveSurname(child, "ravenswood", null, out var newName).Should().BeFalse();
        newName.Should().Be("Jane ravenswood");
        child.Name.Should().Be("Jane Oldfield");
    }

    [Fact]
    public void NEGATIVE_TryGiveSurname_RefusesANameALivingNpcBears()
    {
        NPCNameRegistry.Reset();
        var spawner = NPCSpawnSystem.Instance;
        var npc = new NPC { Name1 = "Isaac Ravenswood", Name2 = "Isaac Ravenswood", ID = "npc_1315_isaac" };
        spawner.AddRestoredNPC(npc);
        try
        {
            FamilySystem.Instance.DeserializeChildren(new List<ChildData>
            {
                Kid("Isaac Oldfield", "Bubbles", "Nessa Ravenswood", "f-1315-h", 7),
            });
            var child = FamilySystem.Instance.AllChildren.Single(c => c.FatherID == "f-1315-h");
            FamilySystem.Instance.TryGiveSurname(child, "Ravenswood", null, out _).Should().BeFalse();
            child.Name.Should().Be("Isaac Oldfield");
            FamilySystem.Instance.TryGiveSurname(child, "Thornbury", null, out var other).Should().BeTrue();
            other.Should().Be("Isaac Thornbury");
        }
        finally
        {
            spawner.ActiveNPCs.Remove(npc);
        }
    }

    [Theory]
    [InlineData("Alice Oldfield", "Ravenswood", "Alice Ravenswood")]
    [InlineData("Alice", "Ravenswood", "Alice Ravenswood")]
    [InlineData("Halvar Copperfield II", "Ravenswood", "Halvar Ravenswood")]
    [InlineData("Anna Copperfield a3f9", "Ravenswood", "Anna Ravenswood")]
    [InlineData("Wren Nessa Holloway", "Ravenswood", "Wren Nessa Ravenswood")]
    [InlineData("Alice Oldfield", "Van Der Berg", "Alice Van Der Berg")]
    [InlineData("Alice Oldfield", "  ", "Alice")]
    public void WithSurname_KeepsTheGivenName_AndReplacesTheRest(string name, string surname, string expected)
    {
        FamilySystem.WithSurname(name, surname).Should().Be(expected);
    }

    [Theory]
    [InlineData("Ravenswood", "Ravenswood")]
    [InlineData("  O'Neil-Hart  ", "O'Neil-Hart")]
    [InlineData("Raven[red]wood", "Ravenredwood")]
    [InlineData("12345", "")]
    [InlineData("Abcdefghijklmnopqrstuvwxyz", "Abcdefghijklmnopqrst")]
    public void TheEnteredSurname_IsCleanedLikeAMarriageSurname(string raw, string expected)
    {
        MarriageSurnameHelper.SanitizeSurname(raw).Should().Be(expected);
    }

    [Fact]
    public void MarkupInTheEnteredSurname_IsRefusedBeforeItIsCleaned()
    {
        NameRules.HasMarkupChars("Raven<b>wood").Should().BeTrue();
        NameRules.HasMarkupChars("O'Neil-Hart").Should().BeFalse();
    }

    [Theory]
    [InlineData("en")]
    [InlineData("es")]
    [InlineData("fr")]
    [InlineData("hu")]
    [InlineData("it")]
    public void TheHomeSurnameStrings_ExistInEveryLanguage(string lang)
    {
        var old = GameConfig.Language;
        try
        {
            GameConfig.Language = lang;
            foreach (var key in new[] { "home.child_action_surname", "home.child_new_surname_prompt", "home.child_surname_all_prompt", "home.child_surname_taken" })
                Loc.Get(key).Should().NotBe(key, $"{key} in {lang}");
            Loc.Get("home.child_surname_all_prompt", 3, "Ravenswood").Should().Contain("3").And.Contain("Ravenswood");
        }
        finally
        {
            GameConfig.Language = old;
        }
    }
}
