using System;
using System.IO;
using FluentAssertions;
using Microsoft.Data.Sqlite;
using UsurperRemake;
using UsurperRemake.Server;
using UsurperRemake.Systems;
using Xunit;

namespace UsurperReborn.Tests;

/// <summary>
/// v1.1.10: a world boss item that does not fit the pack is queued under the character's save key
/// (WorldBossSystem.RowKey: "name__alt" for an alt), but login delivery looked the queue up by the
/// account name. An alt's item was never found, and an alt's session took its main's items. Both
/// sides now use the save key of the character being played.
/// </summary>
[Collection("SharedGameSingletons")]
public class InheritanceKeyTests : IDisposable
{
    private readonly string _path = Path.Combine(Path.GetTempPath(), $"usurper-inh-{Guid.NewGuid():N}.db");
    private readonly SqlSaveBackend _db;
    private readonly SessionContext? _savedContext = SessionContext.Current;

    public InheritanceKeyTests() { _db = new SqlSaveBackend(_path); }

    public void Dispose()
    {
        SessionContext.Current = _savedContext;
        SqliteConnection.ClearAllPools();
        try { File.Delete(_path); } catch { }
    }

    private static void Playing(string account, string characterKey) =>
        SessionContext.Current = new SessionContext { InputStream = Stream.Null, OutputStream = Stream.Null, Username = account, CharacterKey = characterKey };

    [Fact]
    public void AnAltsQueuedItem_IsFoundByTheAltsSession_AndNotByTheMains()
    {
        var alt = new Character { Name1 = "rage__alt", Name2 = "Rage" };
        var main = new Character { Name1 = "rage", Name2 = "Rage" };
        _db.QueueInheritance(WorldBossSystem.RowKey(alt), "Abyssal Leviathan", "{\"name\":\"alt item\"}").Should().BeTrue();
        _db.QueueInheritance(WorldBossSystem.RowKey(main), "Abyssal Leviathan", "{\"name\":\"main item\"}").Should().BeTrue();

        Playing("rage", "rage__alt");
        var altKey = GameEngine.InheritanceKey(alt);
        altKey.Should().Be("rage__alt");
        _db.GetPendingInheritance(altKey).Should().ContainSingle()
            .Which.ItemJson.Should().Contain("alt item", "the alt's session finds the alt's item, and only that");

        Playing("rage", "rage");
        var mainKey = GameEngine.InheritanceKey(main);
        mainKey.Should().Be("rage");
        _db.GetPendingInheritance(mainKey).Should().ContainSingle()
            .Which.ItemJson.Should().Contain("main item");
    }

    [Theory]
    [InlineData("rage", "rage__alt", "rage__alt", null)]      // an alt
    [InlineData("rage", "rage", "rage", "Stormborn")]           // a main who was married when founding the team
    public async System.Threading.Tasks.Task ABequestFromADyingTeammate_ReachesTheLeader(string account, string characterKey, string name1, string? surname)
    {
        // A team's leader key is recorded when the team is founded (TeamCornerLocation, from
        // GameEngine.InheritanceKey) and read back when an NPC member dies, to queue the belongings
        // (WorldSimulator.BequeathItemsToTeamLeader). It used to be the display name, which is not
        // an alt's save key and changes with a marriage, so those bequests could never be delivered.
        Playing(account, characterKey);
        var leader = new Character { Name1 = name1, Name2 = "Rage" };
        if (surname != null) leader.FamilySurname = surname;
        leader.DisplayName.ToLower().Should().NotBe(GameEngine.InheritanceKey(leader), "the old display-name key would have missed this leader");
        await _db.CreatePlayerTeam("The Unbroken", SqlSaveBackend.HashTeamPassword("pw"), GameEngine.InheritanceKey(leader));

        var queuedUnder = await _db.GetTeamLeaderUsername("The Unbroken");
        _db.QueueInheritance(queuedUnder!, "Aldric", "{\"name\":\"Aldric's sword\"}").Should().BeTrue();
        _db.GetPendingInheritance(GameEngine.InheritanceKey(leader)).Should().ContainSingle("login delivery finds it under the same key");
    }

    [Fact]
    public async System.Threading.Tasks.Task AWaitingItem_ArrivesOnceThereIsRoom_WithoutLoggingOut()
    {
        // v1.1.10: delivery is callable during play (the /boss screen calls it), not only at login.
        Playing("rage", "rage");
        var hero = new Character { Name1 = "rage", Name2 = "Rage" };
        _db.QueueInheritance("rage", "Abyssal Leviathan", "{\"name\":\"Tidebreaker\"}").Should().BeTrue();
        var term = new TerminalEmulator(new MemoryStream(), new MemoryStream());

        for (int i = 0; i < 50; i++) hero.Inventory.Add(new Item { Name = $"junk {i}" });
        (await GameEngine.DeliverPendingInheritance(hero, term, _db)).Should().Be(0, "the pack is still full");
        _db.GetPendingInheritance("rage").Should().ContainSingle("it keeps waiting");

        hero.Inventory.RemoveAt(0);
        (await GameEngine.DeliverPendingInheritance(hero, term, _db)).Should().Be(1);
        hero.Inventory.Should().Contain(i => i.Name == "Tidebreaker");
        _db.GetPendingInheritance("rage").Should().BeEmpty();
    }

    [Fact]
    public async System.Threading.Tasks.Task AFullPack_IsToldWhatWaits_NotShownABequestThatNeverCame()
    {
        // v1.1.10: with a full pack nothing can be handed over, but the bequest header used to print
        // anyway (maintainer report: saw the bequest, the items never arrived).
        Playing("rage", "rage");
        var hero = new Character { Name1 = "rage", Name2 = "Rage" };
        for (int i = 0; i < 50; i++) hero.Inventory.Add(new Item { Name = $"junk {i}" });
        _db.QueueInheritance("rage", "Aldric", "{\"name\":\"Aldric's sword\"}").Should().BeTrue();
        _db.QueueInheritance("rage", "Mira", "{\"name\":\"Mira's ring\"}").Should().BeTrue();
        var output = new MemoryStream();
        var term = new TerminalEmulator(new MemoryStream(), output);

        (await GameEngine.DeliverPendingInheritance(hero, term, _db)).Should().Be(0);
        term.StreamWriterInternal!.Flush();
        string shown = System.Text.Encoding.UTF8.GetString(output.ToArray());
        shown.Should().Contain(Loc.Get("engine.inheritance_waiting", 2)).And.NotContain(Loc.Get("engine.inheritance_header"));
        _db.GetPendingInheritance("rage").Should().HaveCount(2, "they keep waiting");
    }

    [Fact]
    public void WithoutASession_TheOldFallbacksStillApply()
    {
        SessionContext.Current = null;
        GameEngine.InheritanceKey(new Character { Name1 = "solo", Name2 = "Solo" })
            .Should().Be((UsurperRemake.BBS.DoorMode.GetPlayerName() ?? "Solo").ToLowerInvariant());
    }
}
