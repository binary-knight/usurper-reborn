using System.Collections.Concurrent;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Runtime.CompilerServices;
using System.Threading.Tasks;
using FluentAssertions;
using UsurperRemake.Server;
using UsurperRemake.Systems;
using Xunit;

namespace UsurperReborn.Tests;

/// <summary>
/// 1.2.4: every player's home is the one GameLocation.Home, so the room registry and the
/// online presence list put everyone at home in the same room. Homes are private: no
/// "Also here" there, and no room line (say, emote, the disconnect notice) crosses homes.
/// The cached "Also here" path also left out the viewer by shown name, so a player whose
/// shown name carries a family name saw herself listed.
/// </summary>
[Collection("SharedGameSingletons")]
public class PrivateHomes124Tests
{
    private static PlayerSession Session(string username, string characterName)
    {
        var s = (PlayerSession)RuntimeHelpers.GetUninitializedObject(typeof(PlayerSession));
        typeof(PlayerSession).GetField("<Username>k__BackingField", BindingFlags.Instance | BindingFlags.NonPublic)!
            .SetValue(s, username);
        typeof(PlayerSession).GetField("<IncomingMessages>k__BackingField", BindingFlags.Instance | BindingFlags.NonPublic)!
            .SetValue(s, new ConcurrentQueue<string>());
        s.ActiveCharacterName = characterName;
        return s;
    }

    private static (TerminalEmulator term, MemoryStream output) Terminal()
    {
        var output = new MemoryStream();
        return (new TerminalEmulator(new MemoryStream(), output), output);
    }

    private static void InvokeChat(string handler, string username, string text, TerminalEmulator term)
    {
        var m = typeof(MudChatSystem).GetMethod(handler, BindingFlags.Static | BindingFlags.NonPublic)!;
        m.Invoke(null, new object[] { username, text, term });
    }

    // ---- room lines at Home ----

    [Theory]
    [InlineData("HandleSay")]
    [InlineData("HandleEmote")]
    public void SayAndEmote_AtHome_ReachNoOtherPlayerAtHome(string handler)
    {
        var rooms = new RoomRegistry();
        var speaker = Session("acct1", "Mira");
        var other = Session("acct2", "Tovin");
        rooms.PlayerEntered(GameLocation.Home, speaker);
        rooms.PlayerEntered(GameLocation.Home, other);

        var (term, _) = Terminal();
        InvokeChat(handler, "acct1", "hello there", term);

        other.IncomingMessages.Should().BeEmpty();
        speaker.IncomingMessages.Should().BeEmpty();
    }

    [Fact]
    public void Say_AtHome_StillEchoesToTheSpeaker()
    {
        var rooms = new RoomRegistry();
        rooms.PlayerEntered(GameLocation.Home, Session("acct1", "Mira"));
        rooms.PlayerEntered(GameLocation.Home, Session("acct2", "Tovin"));

        var (term, output) = Terminal();
        InvokeChat("HandleSay", "acct1", "hello there", term);

        System.Text.Encoding.UTF8.GetString(output.ToArray()).Should().Contain("hello there");
    }

    [Theory]
    [InlineData("HandleSay", GameLocation.TheInn)]
    [InlineData("HandleEmote", GameLocation.TheInn)]
    [InlineData("HandleSay", GameLocation.MainStreet)]
    [InlineData("HandleSay", GameLocation.Dungeons)]
    [InlineData("HandleEmote", GameLocation.Dungeons)]
    public void SayAndEmote_InASharedLocation_StillReachTheRoom(string handler, GameLocation where)
    {
        var rooms = new RoomRegistry();
        var speaker = Session("acct1", "Mira");
        var other = Session("acct2", "Tovin");
        rooms.PlayerEntered(where, speaker);
        rooms.PlayerEntered(where, other);

        var (term, _) = Terminal();
        InvokeChat(handler, "acct1", "hello there", term);

        other.IncomingMessages.Should().ContainSingle().Which.Should().Contain("hello there");
        speaker.IncomingMessages.Should().BeEmpty();
    }

    [Fact]
    public void DisconnectNotice_AtHome_ReachesNoOtherPlayerAtHome()
    {
        var rooms = new RoomRegistry();
        var leaving = Session("acct1", "Mira");
        var other = Session("acct2", "Tovin");
        rooms.PlayerEntered(GameLocation.Home, leaving);
        rooms.PlayerEntered(GameLocation.Home, other);

        rooms.PlayerDisconnected(leaving);

        other.IncomingMessages.Should().BeEmpty();
    }

    [Fact]
    public void DisconnectNotice_InASharedLocation_StillReachesTheRoom()
    {
        var rooms = new RoomRegistry();
        var leaving = Session("acct1", "Mira");
        var other = Session("acct2", "Tovin");
        rooms.PlayerEntered(GameLocation.TheInn, leaving);
        rooms.PlayerEntered(GameLocation.TheInn, other);

        rooms.PlayerDisconnected(leaving);

        other.IncomingMessages.Should().ContainSingle().Which.Should().Contain("Mira");
    }

    [Fact]
    public void LocalizedRoomBroadcast_AtHome_ReachesNoOne_ButDoesInTheDungeons()
    {
        var rooms = new RoomRegistry();
        var homeA = Session("acct1", "Mira");
        var homeB = Session("acct2", "Tovin");
        var dunA = Session("acct3", "Bea");
        var dunB = Session("acct4", "Cal");
        rooms.PlayerEntered(GameLocation.Home, homeA);
        rooms.PlayerEntered(GameLocation.Home, homeB);
        rooms.PlayerEntered(GameLocation.Dungeons, dunA);
        rooms.PlayerEntered(GameLocation.Dungeons, dunB);

        rooms.BroadcastToRoomLocalized(GameLocation.Home, _ => "line", excludeUsername: "acct1");
        rooms.BroadcastToRoomLocalized(GameLocation.Dungeons, _ => "line", excludeUsername: "acct3");

        homeB.IncomingMessages.Should().BeEmpty();
        dunB.IncomingMessages.Should().ContainSingle();
    }

    // ---- "Also here", registry path ----

    [Fact]
    public void AlsoHere_RegistryPath_TwoPlayersAtHome_DoNotSeeEachOther()
    {
        var rooms = new RoomRegistry();
        rooms.PlayerEntered(GameLocation.Home, Session("acct1", "Mira"));
        rooms.PlayerEntered(GameLocation.Home, Session("acct2", "Tovin"));

        rooms.GetPlayerNamesAt(GameLocation.Home, "acct1").Should().BeEmpty();
        rooms.GetPlayerNamesAt(GameLocation.Home, "acct2").Should().BeEmpty();
    }

    [Fact]
    public void AlsoHere_RegistryPath_ViewerWithAFamilyName_IsNotListed()
    {
        var rooms = new RoomRegistry();
        rooms.PlayerEntered(GameLocation.TheInn, Session("acct1", "Mira Ashwick"));
        rooms.PlayerEntered(GameLocation.TheInn, Session("acct2", "Tovin"));

        rooms.GetPlayerNamesAt(GameLocation.TheInn, "acct1").Should().Equal("Tovin");
    }

    // ---- "Also here", cached online-presence path ----

    private static OnlinePlayerInfo Row(string username, string displayName, string location) =>
        new OnlinePlayerInfo { Username = username, DisplayName = displayName, Location = location };

    [Fact]
    public void AlsoHere_CachedPath_ViewerWithAFamilyName_IsNotListed()
    {
        // The viewer's character is Mira (Name2); her shown name carries her married family name.
        var online = new[]
        {
            Row("acct1", "Mira Ashwick", "The Inn"),
            Row("acct2", "Tovin", "The Inn"),
            Row("acct3", "Bea", "Main Street"),
        };

        BaseLocation.CoPresenceOthers(online, GameLocation.TheInn, "The Inn", "acct1", "acct1")
            .Select(p => p.DisplayName).Should().Equal("Tovin");
    }

    [Fact]
    public void AlsoHere_CachedPath_LeavesOutTheViewer_CaseInsensitive_AndAfterAnAltSwitch()
    {
        // After an alt switch the presence row is keyed by the alt key; the session keeps the account.
        var online = new[]
        {
            Row("acct1__alt", "Rook", "The Inn"),
            Row("acct2", "Tovin", "The Inn"),
        };

        BaseLocation.CoPresenceOthers(online, GameLocation.TheInn, "The Inn", "ACCT1__ALT", "acct1")
            .Select(p => p.DisplayName).Should().Equal("Tovin");
        BaseLocation.CoPresenceOthers(online, GameLocation.TheInn, "The Inn", null, null)
            .Select(p => p.DisplayName).Should().BeEquivalentTo("Rook", "Tovin");
    }

    [Fact]
    public void AlsoHere_CachedPath_TwoPlayersAtHome_DoNotSeeEachOther()
    {
        var online = new[]
        {
            Row("acct1", "Mira", "Your Home"),
            Row("acct2", "Tovin", "Your Home"),
        };

        BaseLocation.CoPresenceOthers(online, GameLocation.Home, "Your Home", "acct1").Should().BeEmpty();
        BaseLocation.CoPresenceOthers(online, GameLocation.Home, "Your Home", "acct2").Should().BeEmpty();
    }

    [Fact]
    public void AlsoHere_Dungeons_StaysHidden_AndRoomLinesThereStillFlow()
    {
        RoomRegistry.ShowsCoPresence(GameLocation.Dungeons).Should().BeFalse();
        RoomRegistry.IsPrivateLocation(GameLocation.Dungeons).Should().BeFalse();
        RoomRegistry.ShowsCoPresence(GameLocation.TheInn).Should().BeTrue();
        BaseLocation.CoPresenceOthers(new[] { Row("acct2", "Tovin", "Dungeons") }, GameLocation.Dungeons, "Dungeons", "acct1")
            .Should().BeEmpty();

        var rooms = new RoomRegistry();
        rooms.PlayerEntered(GameLocation.Dungeons, Session("acct1", "Mira"));
        rooms.PlayerEntered(GameLocation.Dungeons, Session("acct2", "Tovin"));
        rooms.GetPlayerNamesAt(GameLocation.Dungeons, "acct1").Should().Equal("Tovin");
    }
}
