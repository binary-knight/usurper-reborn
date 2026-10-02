using System;
using System.Collections.Concurrent;
using System.IO;
using System.Linq;
using System.Net.Sockets;
using System.Reflection;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using FluentAssertions;
using Microsoft.Data.Sqlite;
using UsurperRemake.BBS;
using UsurperRemake.Server;
using UsurperRemake.Systems;
using Xunit;

namespace UsurperReborn.Tests;

/// <summary>
/// v1.2.4: a player who hangs up during character creation is a disconnect, not an error. The
/// server's log watcher relays every [ERR] line of debug.log to Discord (and no other level), so
/// the hang-up must log no [ERR] line, must not show the retry prompt on the dead connection, and
/// must surface as the IOException PlayerSession treats as "connection lost". A real error keeps
/// logging [ERR] and offering the retry. Also: the save-lookup log line names the real mode, and
/// a session that never had a character registers no dormitory sleeper.
/// </summary>
[Collection("SharedGameSingletons")]
public class CharCreateDisconnect124Tests
{
    private const BindingFlags Priv = BindingFlags.NonPublic | BindingFlags.Instance;
    private const BindingFlags PrivStatic = BindingFlags.NonPublic | BindingFlags.Static;
    private static readonly string[] CreationTags = { "[CHARCREATE]", "[CRASH]", "[CREATE]" };

    // ---- DebugLogger capture: a timer-less logger swapped in as the instance, so nothing is
    // flushed away before the test reads it. The real-error tests read their [ERR] line through
    // this same capture, which proves the capture sees what the quiet-path tests say is absent.

    private sealed class LogCapture : IDisposable
    {
        private static readonly FieldInfo InstanceField = typeof(DebugLogger).GetField("_instance", PrivStatic)!;
        private readonly object? _previous;
        private readonly ConcurrentQueue<string> _queue = new();

        public LogCapture()
        {
            _previous = InstanceField.GetValue(null);
            var logger = (DebugLogger)System.Runtime.CompilerServices.RuntimeHelpers.GetUninitializedObject(typeof(DebugLogger));
            typeof(DebugLogger).GetField("logQueue", Priv)!.SetValue(logger, _queue);
            typeof(DebugLogger).GetField("isEnabled", Priv)!.SetValue(logger, true);
            typeof(DebugLogger).GetField("minimumLevel", Priv)!.SetValue(logger, DebugLogger.LogLevel.Debug);
            InstanceField.SetValue(null, logger);
        }

        /// <summary>Lines from the creation path only (other collections may log concurrently).</summary>
        public string[] CreationLines => _queue.ToArray().Where(l => CreationTags.Any(l.Contains)).ToArray();
        public string[] CreationErrors => CreationLines.Where(l => l.Contains("[ERR]")).ToArray();

        public void Dispose() => InstanceField.SetValue(null, _previous);
    }

    /// <summary>Input stream: serves <paramref name="script"/>, then either a 0-byte read
    /// (orderly client close) or, when <paramref name="failFirstRead"/>, throws a plain
    /// IOException on the first read and serves the script afterwards.</summary>
    private sealed class ScriptedInput : Stream
    {
        private readonly byte[] _data;
        private int _pos;
        private Exception? _failFirst;

        public ScriptedInput(string script, Exception? failFirstRead = null)
        {
            _data = Encoding.UTF8.GetBytes(script);
            _failFirst = failFirstRead;
        }

        public override int Read(byte[] buffer, int offset, int count) => Read(buffer.AsSpan(offset, count));

        public override int Read(Span<byte> buffer)
        {
            if (_failFirst != null) { var e = _failFirst; _failFirst = null; throw e; }
            int n = Math.Min(buffer.Length, _data.Length - _pos);
            _data.AsSpan(_pos, n).CopyTo(buffer);
            _pos += n;
            return n;
        }

        public override ValueTask<int> ReadAsync(Memory<byte> buffer, CancellationToken ct = default)
        {
            try { return new ValueTask<int>(Read(buffer.Span)); }
            catch (Exception e) { return ValueTask.FromException<int>(e); }
        }

        public override Task<int> ReadAsync(byte[] buffer, int offset, int count, CancellationToken ct)
            => ReadAsync(buffer.AsMemory(offset, count), ct).AsTask();

        public override bool CanRead => true;
        public override bool CanSeek => false;
        public override bool CanWrite => false;
        public override long Length => throw new NotSupportedException();
        public override long Position { get => throw new NotSupportedException(); set => throw new NotSupportedException(); }
        public override void Flush() { }
        public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();
        public override void SetLength(long value) => throw new NotSupportedException();
        public override void Write(byte[] buffer, int offset, int count) => throw new NotSupportedException();
    }

    private static async Task<(Task<Character> run, MemoryStream output)> RunCreateNewPlayer(Stream input)
    {
        DoorMode.IsDisconnected = false;
        var output = new MemoryStream();
        var terminal = new TerminalEmulator(input, output);
        var engine = (GameEngine)System.Runtime.CompilerServices.RuntimeHelpers.GetUninitializedObject(typeof(GameEngine));
        typeof(GameEngine).GetField("terminal", Priv)!.SetValue(engine, terminal);
        var method = typeof(GameEngine).GetMethod("CreateNewPlayer", Priv)!;
        var task = (Task<Character>)method.Invoke(engine, new object[] { "aplayer" })!;
        try { await task.WaitAsync(TimeSpan.FromSeconds(20)); } catch { /* inspected by the caller */ }
        return (task, output);
    }

    private static string Text(MemoryStream output) => Encoding.UTF8.GetString(output.ToArray());

    private static string RetryPrompt => Loc.Get("engine.retry_prompt").Trim();

    private static void ShouldBeADisconnect(Task<Character> run)
    {
        run.IsFaulted.Should().BeTrue("a hang-up must surface, not return a character or null");
        var ex = run.Exception!.InnerException!;
        ex.Should().BeAssignableTo<IOException>("PlayerSession ends the session on IOException");
        ConnectionClosedException.IsDisconnect(ex).Should().BeTrue();
    }

    [Fact]
    public async Task HangUpAtTheQuickStartPrompt_LogsNoError_ShowsNoRetry_AndIsADisconnect()
    {
        using var log = new LogCapture();
        var (run, output) = await RunCreateNewPlayer(new ScriptedInput(""));

        ShouldBeADisconnect(run);
        log.CreationErrors.Should().BeEmpty("the log watcher posts every [ERR] line to Discord");
        log.CreationLines.Should().Contain(l => l.Contains("[INF] [CHARCREATE]") && l.Contains("Connection closed"));
        var text = Text(output);
        text.Should().Contain(Loc.Get("creation.quick_start"), "the hang-up happened at the Quick Start prompt");
        text.Should().NotContain(RetryPrompt);
    }

    [Fact]
    public async Task HangUpAtALaterCreationPrompt_LogsNoError_ShowsNoRetry_AndIsADisconnect()
    {
        using var log = new LogCapture();
        // Custom path, then the connection closes at the next prompt (gender).
        var (run, output) = await RunCreateNewPlayer(new ScriptedInput("C\r\n"));

        ShouldBeADisconnect(run);
        log.CreationErrors.Should().BeEmpty();
        log.CreationLines.Should().Contain(l => l.Contains("[INF] [CHARCREATE]") && l.Contains("Connection closed"));
        Text(output).Should().NotContain(RetryPrompt);
    }

    [Fact]
    public async Task RealIOErrorDuringCreation_StillLogsAnError_AndOffersTheRetry()
    {
        using var log = new LogCapture();
        // A plain IOException (no socket cause, connection not known lost) is a real error.
        var (run, output) = await RunCreateNewPlayer(new ScriptedInput("n\r\n", new IOException("disk read failed")));

        run.IsCompletedSuccessfully.Should().BeTrue("the player declined the retry");
        run.Result.Should().BeNull();
        log.CreationErrors.Should().Contain(l => l.Contains("[ERR] [CHARCREATE]") && l.Contains("disk read failed"));
        Text(output).Should().Contain(RetryPrompt);
    }

    [Fact]
    public async Task RealNonIOErrorDuringCreation_StillLogsAnError_AndOffersTheRetry()
    {
        using var log = new LogCapture();
        var (run, output) = await RunCreateNewPlayer(new ScriptedInput("n\r\n", new InvalidOperationException("broken state")));

        run.IsCompletedSuccessfully.Should().BeTrue();
        run.Result.Should().BeNull();
        log.CreationErrors.Should().Contain(l => l.Contains("[ERR] [CHARCREATE]") && l.Contains("broken state"));
        Text(output).Should().Contain(RetryPrompt);
    }

    [Fact]
    public void IsDisconnect_IsNarrow()
    {
        DoorMode.IsDisconnected = false;
        ConnectionClosedException.IsDisconnect(new ConnectionClosedException()).Should().BeTrue();
        ConnectionClosedException.IsDisconnect(new IOException("reset", new SocketException(104))).Should().BeTrue();
        ConnectionClosedException.IsDisconnect(new AggregateException(new ConnectionClosedException())).Should().BeTrue();
        ConnectionClosedException.IsDisconnect(new IOException("disk full")).Should().BeFalse();
        ConnectionClosedException.IsDisconnect(new InvalidOperationException("x")).Should().BeFalse();
        ConnectionClosedException.IsDisconnect(null).Should().BeFalse();
    }

    // ---- the save-lookup log line names the real mode ----

    [Fact]
    public void SaveLookupLogLine_InMudMode_DoesNotSayBbsDoor()
    {
        var mud = typeof(DoorMode).GetField("_mudServerMode", PrivStatic)!;
        var online = typeof(DoorMode).GetField("_onlineMode", PrivStatic)!;
        var (oldMud, oldOnline) = ((bool)mud.GetValue(null)!, (bool)online.GetValue(null)!);
        try
        {
            mud.SetValue(null, true);
            online.SetValue(null, true);
            var line = DoorMode.SaveLookupLogMessage("aplayer");
            line.Should().NotContainEquivalentOf("BBS Door");
            line.Should().StartWith("MUD session").And.Contain("'aplayer'");

            mud.SetValue(null, false);
            online.SetValue(null, false);
            DoorMode.SaveLookupLogMessage("aplayer").Should().StartWith("Local");
        }
        finally
        {
            mud.SetValue(null, oldMud);
            online.SetValue(null, oldOnline);
        }
    }

    [Fact]
    public void GameEngine_LogsTheSaveLookupThroughTheModeHelper()
    {
        var src = File.ReadAllText(Path.Combine(RepoRoot(), "Scripts/Core/GameEngine.cs"));
        src.Should().NotContain("BBS Door mode: Looking for save");
        src.Should().Contain("DoorMode.Log(UsurperRemake.BBS.DoorMode.SaveLookupLogMessage(playerName))");
    }

    private static string RepoRoot()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir != null && !File.Exists(Path.Combine(dir.FullName, "usurper-reloaded.csproj")))
            dir = dir.Parent;
        dir.Should().NotBeNull("the tests run inside the repository");
        return dir!.FullName;
    }

    // ---- no sleeper for a session that never had a character ----

    private static MudServer NewServer()
    {
        var t = typeof(MudServer);
        var server = (MudServer)System.Runtime.CompilerServices.RuntimeHelpers.GetUninitializedObject(t);
        t.GetField("<ActiveSessions>k__BackingField", Priv)!.SetValue(server, new ConcurrentDictionary<string, PlayerSession>());
        return server;
    }

    private static async Task<bool> SleeperRegisteredAfterDisconnect(bool hadCharacter)
    {
        var path = Path.Combine(Path.GetTempPath(), $"usurper-charcreate-{Guid.NewGuid():N}.db");
        try
        {
            var db = new SqlSaveBackend(path);
            var session = new PlayerSession("aplayer", "MUD", new TcpClient(), new MemoryStream(), db, NewServer(), CancellationToken.None);
            await session.PersistOnDisconnectAsync(null, "aplayer", hadCharacter);
            return await db.GetSleepingPlayerInfo("aplayer") != null;
        }
        finally
        {
            SqliteConnection.ClearAllPools();
            try { File.Delete(path); } catch { }
        }
    }

    [Fact]
    public async Task DisconnectBeforeACharacterExists_RegistersNoSleeper()
    {
        (await SleeperRegisteredAfterDisconnect(hadCharacter: false)).Should().BeFalse();
    }

    [Fact]
    public async Task UncleanDisconnectWithACharacter_StillRegistersTheSleeper()
    {
        (await SleeperRegisteredAfterDisconnect(hadCharacter: true)).Should().BeTrue();
    }

    private static GameEngine EngineWith(Character? player)
    {
        var engine = (GameEngine)System.Runtime.CompilerServices.RuntimeHelpers.GetUninitializedObject(typeof(GameEngine));
        engine.CurrentPlayer = player;
        return engine;
    }

    /// <summary>Drives RunAsync's disconnect persistence through the seam it calls. The save is
    /// suppressed (as for a deleted character) so no real save runs; the sleeper is what is checked.</summary>
    private static async Task<bool> SessionSleeperAfterDisconnect(Character? player)
    {
        var path = Path.Combine(Path.GetTempPath(), $"usurper-charcreate-{Guid.NewGuid():N}.db");
        try
        {
            var db = new SqlSaveBackend(path);
            var session = new PlayerSession("aplayer", "MUD", new TcpClient(), new MemoryStream(), db, NewServer(), CancellationToken.None)
            { SuppressDisconnectSave = true };
            await session.FinishDisconnectPersistAsync(EngineWith(player));
            return await db.GetSleepingPlayerInfo("aplayer") != null;
        }
        finally
        {
            SqliteConnection.ClearAllPools();
            try { File.Delete(path); } catch { }
        }
    }

    [Fact]
    public async Task SessionDisconnect_NoCharacterLoaded_RegistersNoSleeper()
    {
        (await SessionSleeperAfterDisconnect(null)).Should().BeFalse();
    }

    [Fact]
    public async Task SessionDisconnect_WithACharacter_RegistersTheSleeper()
    {
        (await SessionSleeperAfterDisconnect(new Character { Name1 = "aplayer", Name2 = "aplayer" })).Should().BeTrue();
    }

    [Fact]
    public void PlayerSession_RunAsyncUsesTheDisconnectSeam()
    {
        var src = File.ReadAllText(Path.Combine(RepoRoot(), "Scripts/Server/PlayerSession.cs"));
        src.Should().Contain("await FinishDisconnectPersistAsync(ctx.Engine);");
    }

    // ---- door mode (online) cleanup: same guard ----

    private static async Task<bool> DoorSleeperAfterHangUp(bool hadCharacter)
    {
        var path = Path.Combine(Path.GetTempPath(), $"usurper-charcreate-door-{Guid.NewGuid():N}.db");
        try
        {
            var db = new SqlSaveBackend(path);
            await UsurperConsole.Program.RegisterDoorSleeperAsync(db, "aplayer", hadCharacter);
            return await db.GetSleepingPlayerInfo("aplayer") != null;
        }
        finally
        {
            SqliteConnection.ClearAllPools();
            try { File.Delete(path); } catch { }
        }
    }

    [Fact]
    public async Task DoorHangUpBeforeACharacterExists_RegistersNoSleeper()
    {
        (await DoorSleeperAfterHangUp(hadCharacter: false)).Should().BeFalse();
    }

    [Fact]
    public async Task DoorUncleanExitWithACharacter_StillRegistersTheSleeper()
    {
        (await DoorSleeperAfterHangUp(hadCharacter: true)).Should().BeTrue();
    }

    [Fact]
    public void DoorCleanup_PassesWhetherACharacterWasLoaded()
    {
        var src = File.ReadAllText(Path.Combine(RepoRoot(), "Console/Bootstrap/Program.cs"));
        src.Should().Contain("hadCharacter: GameEngine.Instance?.CurrentPlayer != null");
    }
}
