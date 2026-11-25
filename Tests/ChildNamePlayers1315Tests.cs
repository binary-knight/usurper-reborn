using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Threading.Tasks;
using FluentAssertions;
using UsurperRemake.BBS;
using UsurperRemake.Systems;
using Xunit;

namespace UsurperReborn.Tests;

/// <summary>
/// 1.3.15: a child renamed at Home may not take a player's name. FamilySystem.GetParentNamesOf matches a
/// child's full name against a character's Name (for a player, Character.Name is Name2), so a child named
/// exactly like a player would be read as that player's own record. Both Home renames, the first name
/// ([N]) and the last name ([L]), go through FamilySystem.TryRename, which refuses the renaming player's
/// Name and DisplayName and, online, any player's display name (SaveSystem.IsDisplayNameTaken, as at
/// character creation).
/// </summary>
[Collection("SharedGameSingletons")]
public class ChildNamePlayers1315Tests : IDisposable
{
    private const BindingFlags SNP = BindingFlags.NonPublic | BindingFlags.Static;
    private static readonly FieldInfo OnlineField = typeof(DoorMode).GetField("_onlineMode", SNP)!;
    private static readonly FieldInfo SaveSystemField = typeof(SaveSystem).GetField("instance", SNP)!;
    private static readonly DateTime Birth = new DateTime(2026, 11, 25, 15, 0, 0);

    private readonly List<ChildData> _childrenBefore;
    private readonly object? _onlineBefore;
    private readonly object? _saveBefore;

    public ChildNamePlayers1315Tests()
    {
        _childrenBefore = FamilySystem.Instance.SerializeChildren();
        _onlineBefore = OnlineField.GetValue(null);
        _saveBefore = SaveSystemField.GetValue(null);
    }

    public void Dispose()
    {
        OnlineField.SetValue(null, _onlineBefore);
        SaveSystemField.SetValue(null, _saveBefore);
        FamilySystem.Instance.DeserializeChildren(_childrenBefore);
    }

    /// <summary>A backend that knows one other online player's display name.</summary>
    private sealed class OnePlayerBackend : ISaveBackend
    {
        private readonly string _taken;
        public OnePlayerBackend(string taken) { _taken = taken; }
        public bool IsDisplayNameTaken(string displayName, string excludeUsername) =>
            string.Equals(displayName, _taken, StringComparison.OrdinalIgnoreCase);
        public Task<bool> WriteGameData(string playerName, SaveGameData data) => Task.FromResult(true);
        public Task<SaveGameData?> ReadGameData(string playerName) => Task.FromResult<SaveGameData?>(null);
        public Task<SaveGameData?> ReadGameDataByFileName(string fileName) => Task.FromResult<SaveGameData?>(null);
        public bool GameDataExists(string playerName) => false;
        public bool DeleteGameData(string playerName) => false;
        public List<SaveInfo> GetAllSaves() => new();
        public List<SaveInfo> GetPlayerSaves(string playerName) => new();
        public SaveInfo? GetMostRecentSave(string playerName) => null;
        public List<string> GetAllPlayerNames() => new();
        public Task<bool> WriteAutoSave(string playerName, SaveGameData data) => Task.FromResult(true);
        public void CreateBackup(string playerName) { }
        public string GetSaveDirectory() => Path.GetTempPath();
    }

    private static Child Kid(string name, string fatherId, int hour)
    {
        FamilySystem.Instance.DeserializeChildren(new List<ChildData>
        {
            new ChildData { Name = name, Father = "Bubbles", Mother = "Nessa Ravenswood", FatherID = fatherId, MotherID = "m-1315p",
                            Sex = (int)CharacterSex.Male, BirthDate = Birth.AddHours(hour), Named = true },
        });
        return FamilySystem.Instance.AllChildren.Single(c => c.FatherID == fatherId);
    }

    private static Character Player(string name2, string familySurname = "") =>
        new Character { Name1 = name2, Name2 = name2, FamilySurname = familySurname, AI = CharacterAI.Human };

    [Fact]
    public void NEGATIVE_ALastName_ThatGivesTheChild_TheRenamingPlayersName_IsRefused()
    {
        NPCNameRegistry.Reset();
        var parent = Player("Bubbles Ravenswood");
        parent.Name.Should().Be("Bubbles Ravenswood", "Character.Name is Name2, the field GetParentNamesOf reads");
        var child = Kid("Bubbles Oldfield", "f-1315p-a", 0);

        FamilySystem.Instance.TryGiveSurname(child, "Ravenswood", parent, out var newName).Should().BeFalse();
        newName.Should().Be("Bubbles Ravenswood");
        child.Name.Should().Be("Bubbles Oldfield");
    }

    [Fact]
    public void NEGATIVE_ALastName_ThatGivesTheChild_ThePlayersDisplayName_IsRefused()
    {
        NPCNameRegistry.Reset();
        var parent = Player("Bubbles", "Ravenswood");      // took a family surname at marriage
        parent.DisplayName.Should().Be("Bubbles Ravenswood");
        var child = Kid("Bubbles Oldfield", "f-1315p-b", 1);

        FamilySystem.Instance.TryGiveSurname(child, "Ravenswood", parent, out _).Should().BeFalse();
        child.Name.Should().Be("Bubbles Oldfield");
    }

    [Fact]
    public void NEGATIVE_AFirstName_ThatGivesTheChild_ThePlayersName_IsRefused_AndAnotherIsTaken()
    {
        NPCNameRegistry.Reset();
        var parent = Player("Bubbles Ravenswood");
        var child = Kid("Henry Ravenswood", "f-1315p-c", 2);

        FamilySystem.Instance.TryRename(child, "Bubbles Ravenswood", parent).Should().BeFalse();
        child.Name.Should().Be("Henry Ravenswood");
        FamilySystem.Instance.TryRename(child, "Edward Ravenswood", parent).Should().BeTrue();
        child.Name.Should().Be("Edward Ravenswood");
    }

    [Fact]
    public void NEGATIVE_Online_AnotherPlayersDisplayName_IsRefused_ButOfflineTheSameNameIsAllowed()
    {
        NPCNameRegistry.Reset();
        SaveSystemField.SetValue(null, new SaveSystem(new OnePlayerBackend("Wren Ravenswood")));
        var parent = Player("Bubbles");

        OnlineField.SetValue(null, true);
        var online = Kid("Wren Oldfield", "f-1315p-d", 3);
        FamilySystem.Instance.TryGiveSurname(online, "Ravenswood", parent, out _).Should().BeFalse();
        online.Name.Should().Be("Wren Oldfield");

        OnlineField.SetValue(null, false);      // single player: no other players in the world
        var offline = Kid("Wren Oldfield", "f-1315p-e", 4);
        FamilySystem.Instance.TryGiveSurname(offline, "Ravenswood", parent, out _).Should().BeTrue();
        offline.Name.Should().Be("Wren Ravenswood");
    }

    [Fact]
    public void BothHomeRenames_GoThroughTheCheck()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir != null && !File.Exists(Path.Combine(dir.FullName, "UsurperReborn.sln"))) dir = dir.Parent;
        string home = File.ReadAllText(Path.Combine(dir!.FullName, "Scripts", "Locations", "HomeLocation.cs"));
        home.Should().Contain("FamilySystem.Instance.TryRename(selectedChild, newFirst + surname, currentPlayer)");
        home.Should().Contain("FamilySystem.Instance.TryGiveSurname(child, surname, currentPlayer, out string newName)");
        home.Should().NotContain("selectedChild.Name = newFirst + surname;");
    }
}
