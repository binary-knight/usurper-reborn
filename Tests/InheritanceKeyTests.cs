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

    [Fact]
    public void WithoutASession_TheOldFallbacksStillApply()
    {
        SessionContext.Current = null;
        GameEngine.InheritanceKey(new Character { Name1 = "solo", Name2 = "Solo" })
            .Should().Be((UsurperRemake.BBS.DoorMode.GetPlayerName() ?? "Solo").ToLowerInvariant());
    }
}
