using System;
using System.Collections.Concurrent;
using System.IO;
using System.Net.Sockets;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using FluentAssertions;
using Microsoft.Data.Sqlite;
using UsurperRemake.Server;
using UsurperRemake.Systems;
using Xunit;

namespace UsurperReborn.Tests;

/// <summary>
/// v1.2.4: a reconnect kicks the old session; the old connection's cleanup must remove only its
/// own ActiveSessions entry, must not register a dormitory sleeper or write its emergency save
/// once a newer session for the account is active, and the new session waits (bounded) for the
/// old cleanup instead of a fixed 500 ms.
/// </summary>
[Collection("SharedGameSingletons")]
public class ReconnectRace124Tests : IDisposable
{
    private const string Key = "aplayer";
    private readonly string _path = Path.Combine(Path.GetTempPath(), $"usurper-reconnect-{Guid.NewGuid():N}.db");
    private readonly SqlSaveBackend _db;

    public ReconnectRace124Tests() { _db = new SqlSaveBackend(_path); }
    public void Dispose() { SqliteConnection.ClearAllPools(); try { File.Delete(_path); } catch { } }

    private static MudServer NewServer(TimeSpan? cleanupTimeout = null)
    {
        var t = typeof(MudServer);
        var server = (MudServer)System.Runtime.CompilerServices.RuntimeHelpers.GetUninitializedObject(t);
        const System.Reflection.BindingFlags F = System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance;
        t.GetField("<ActiveSessions>k__BackingField", F)!.SetValue(server, new ConcurrentDictionary<string, PlayerSession>());
        server.StaleCleanupTimeoutOverride = cleanupTimeout;
        return server;
    }

    private PlayerSession NewSession(MudServer server) =>
        new PlayerSession(Key, "Web", new TcpClient(), new MemoryStream(), _db, server, CancellationToken.None);

    /// <summary>The old connection's tail: RunAsync's cleanup, then the handler's finally.</summary>
    private static async Task OldConnectionEnds(MudServer server, PlayerSession old, Func<Task>? save)
    {
        await old.PersistOnDisconnectAsync(save, Key);
        old.MarkCleanupComplete();
        server.EndConnection(Key, old);
    }

    [Fact]
    public async Task OldCleanupSlowerThanTheKickWait_KeepsTheNewSessionListed()
    {
        var server = NewServer(TimeSpan.FromMilliseconds(100));
        var oldS = NewSession(server);
        (await server.RegisterSessionAsync(Key, oldS)).Should().BeTrue();

        var saveGate = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var oldEnd = OldConnectionEnds(server, oldS, () => saveGate.Task);

        var newS = NewSession(server);
        await server.KickStaleSessionAsync(Key, oldS, "reconnect");
        await server.WaitForStaleCleanupAsync(oldS);   // times out: the old save is still running
        (await server.RegisterSessionAsync(Key, newS)).Should().BeTrue();
        server.ActiveSessions[Key].Should().BeSameAs(newS);

        saveGate.SetResult();
        await oldEnd;

        server.ActiveSessions.TryGetValue(Key, out var listed).Should().BeTrue("the old connection's finally must not remove the new session's entry");
        listed.Should().BeSameAs(newS);
    }

    [Fact]
    public async Task OldCleanup_RegistersNoSleeper_WhileANewerSessionExists()
    {
        var server = NewServer();
        var oldS = NewSession(server);
        var newS = NewSession(server);
        server.ActiveSessions[Key] = newS;

        await oldS.PersistOnDisconnectAsync(null, Key);

        (await _db.GetSleepingPlayerInfo(Key)).Should().BeNull("the player is online in the newer session");
    }

    [Fact]
    public async Task OldCleanup_SkipsItsEmergencySave_WhileANewerSessionExists()
    {
        var server = NewServer();
        var oldS = NewSession(server);
        server.ActiveSessions[Key] = NewSession(server);
        bool saved = false;

        await oldS.PersistOnDisconnectAsync(() => { saved = true; return Task.CompletedTask; }, Key);

        saved.Should().BeFalse("the newer session holds newer data; the stale save would overwrite it");
    }

    [Fact]
    public async Task NewSession_WaitsForTheOldEmergencySave_BeyondTheOldFixedDelay()
    {
        var server = NewServer(TimeSpan.FromSeconds(10));
        var oldS = NewSession(server);
        (await server.RegisterSessionAsync(Key, oldS)).Should().BeTrue();

        var saveGate = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        bool saved = false;
        var oldEnd = OldConnectionEnds(server, oldS, async () => { await saveGate.Task; saved = true; });

        var newS = NewSession(server);
        await server.KickStaleSessionAsync(Key, oldS, "reconnect");
        bool savedWhenRegistered = false;
        var newConn = Task.Run(async () =>
        {
            await server.WaitForStaleCleanupAsync(oldS);
            savedWhenRegistered = saved;
            return await server.RegisterSessionAsync(Key, newS);
        });

        await Task.Delay(1200);   // well past the old fixed 500 ms
        newConn.IsCompleted.Should().BeFalse("the new session must wait for the old save");
        server.ActiveSessions.ContainsKey(Key).Should().BeFalse();

        saveGate.SetResult();
        (await newConn).Should().BeTrue();
        await oldEnd;
        savedWhenRegistered.Should().BeTrue("the old save landed before the new session registered");
        saved.Should().BeTrue("with no newer session at save time, the old save still runs");
        server.ActiveSessions[Key].Should().BeSameAs(newS);
    }

    [Fact]
    public async Task NormalDisconnect_SavesRegistersTheSleeper_AndRemovesTheEntry()
    {
        var server = NewServer();
        var s = NewSession(server);
        (await server.RegisterSessionAsync(Key, s)).Should().BeTrue();
        bool saved = false;

        await OldConnectionEnds(server, s, () => { saved = true; return Task.CompletedTask; });

        saved.Should().BeTrue();
        (await _db.GetSleepingPlayerInfo(Key)).Should().NotBeNull();
        (await _db.GetSleepingPlayerInfo(Key))!.SleepLocation.Should().Be("dormitory");
        server.ActiveSessions.ContainsKey(Key).Should().BeFalse();
    }

    [Fact]
    public async Task Kick_RemovesOnlyTheKickedSession_NotOneThatReplacedIt()
    {
        var server = NewServer();
        var kicked = NewSession(server);
        var other = NewSession(server);
        server.ActiveSessions[Key] = other;

        await server.KickStaleSessionAsync(Key, kicked, "reconnect");

        server.ActiveSessions[Key].Should().BeSameAs(other);
    }

    [Fact]
    public void MudServer_HasNoByKeyRemoval_AndEveryKickPathUsesTheHelper()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir != null && !Directory.Exists(Path.Combine(dir.FullName, "Scripts"))) dir = dir.Parent;
        var src = File.ReadAllText(Path.Combine(dir!.FullName, "Scripts", "Server", "MudServer.cs"));

        Regex.IsMatch(src, @"ActiveSessions\.TryRemove\([^)]*out\s+_").Should().BeFalse("removal is by session reference only");
        Regex.Matches(src, @"await KickStaleSessionAsync\(").Count.Should().Be(3, "trusted auth, interactive auth and the TryAdd race path");
        src.Should().NotContain("await existingSession.DisconnectAsync(");
        src.Should().NotContain("await existingInteractive.DisconnectAsync(");
    }
}
