using System.IO;
using System.Linq;
using System.Reflection;
using System.Runtime.CompilerServices;
using FluentAssertions;
using UsurperRemake.Server;
using Xunit;

namespace UsurperReborn.Tests;

/// <summary>
/// 1.2.0: the online "Also here" line leaves out the viewer by account username. It compared a
/// character name before, so a player whose shown name differed from the session's (a family
/// name taken at marriage, an alt) saw their own name listed at every location. The auction
/// broadcast excluded its seller the same wrong way.
/// </summary>
[Collection("SharedGameSingletons")]
public class PresenceSelfExclude120Tests
{
    private static PlayerSession Session(string username, string characterName)
    {
        var s = (PlayerSession)RuntimeHelpers.GetUninitializedObject(typeof(PlayerSession));
        typeof(PlayerSession).GetField("<Username>k__BackingField", BindingFlags.Instance | BindingFlags.NonPublic)!
            .SetValue(s, username);
        s.ActiveCharacterName = characterName;
        return s;
    }

    [Fact]
    public void AlsoHere_LeavesOutTheViewerByAccount_WhateverTheirShownName()
    {
        var rooms = new RoomRegistry();
        rooms.PlayerEntered(GameLocation.MainStreet, Session("Arn", "Arn"));
        rooms.PlayerEntered(GameLocation.MainStreet, Session("bea", "Bea"));

        // The viewer's shown name is now "Arn Ashwick"; the room still knows them as account "Arn".
        var names = rooms.GetPlayerNamesAt(GameLocation.MainStreet, "Arn");

        names.Should().Equal("Bea");
    }

    [Fact]
    public void AlsoHere_LeavesOutAnAltViewer_WhoseCharacterNameIsNotTheAccount()
    {
        var rooms = new RoomRegistry();
        rooms.PlayerEntered(GameLocation.TheInn, Session("acct1", "Mira"));
        rooms.PlayerEntered(GameLocation.TheInn, Session("acct2", "Tovin"));

        rooms.GetPlayerNamesAt(GameLocation.TheInn, "ACCT1").Should().Equal("Tovin");
    }

    [Fact]
    public void AlsoHere_WithNoViewer_ListsEveryone()
    {
        var rooms = new RoomRegistry();
        rooms.PlayerEntered(GameLocation.TheInn, Session("acct1", "Mira"));
        rooms.PlayerEntered(GameLocation.TheInn, Session("acct2", "Tovin"));

        rooms.GetPlayerNamesAt(GameLocation.TheInn).Should().BeEquivalentTo("Mira", "Tovin");
    }

    [Fact]
    public void BaseLocation_ExcludesTheViewerByAccount_ForAlsoHereAndTheAuctionBroadcast()
    {
        var src = File.ReadAllText(Path.Combine(RepoRoot(), "Scripts", "Locations", "BaseLocation.cs"));

        src.Should().Contain("GetPlayerNamesAt(LocationId, UsurperRemake.Server.SessionContext.Current?.Username)");
        src.Should().NotContain("GetPlayerNamesAt(LocationId, player.DisplayName)");
        src.Should().NotContain("excludeUsername: currentPlayer.DisplayName");
    }

    private static string RepoRoot()
    {
        var dir = new DirectoryInfo(System.AppContext.BaseDirectory);
        while (dir != null && !File.Exists(Path.Combine(dir.FullName, "Scripts", "Locations", "BaseLocation.cs")))
            dir = dir.Parent;
        return dir!.FullName;
    }
}
