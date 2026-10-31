using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.Json;
using System.Threading;
using FluentAssertions;
using UsurperRemake.Server;
using Xunit;

/// <summary>
/// 1.3.9: in group play, Char.Vitals for each grouped player goes to that player's own session.
/// Group combat and shared rests run on the leader's session, so before this a follower's client
/// never saw its own HP, mana or stamina change during a fight.
/// </summary>
[Collection("SharedGameSingletons")]
public class GmcpGroupVitals139Tests
{
    // A stream that records, at every Write and Flush, whether the writer lock of the terminal on
    // it and this stream's own lock are held, and can be told to throw.
    private sealed class ProbeStream : MemoryStream
    {
        public object? TerminalLock;
        public object? VitalsLock;
        public bool Throw;
        public readonly List<(string op, bool term, bool stream, int length, bool vitals)> Events = new();
        private void Record(string op, int length) =>
            Events.Add((op, TerminalLock != null && Monitor.IsEntered(TerminalLock), Monitor.IsEntered(this), length,
                VitalsLock != null && Monitor.IsEntered(VitalsLock)));
        public override void Write(byte[] buffer, int offset, int count)
        {
            Record("write", count);
            if (Throw) throw new IOException("gone");
            base.Write(buffer, offset, count);
        }
        public override void Write(ReadOnlySpan<byte> buffer)
        {
            Record("write", buffer.Length);
            if (Throw) throw new IOException("gone");
            base.Write(buffer);
        }
        public override void Flush() { Record("flush", 0); base.Flush(); }
    }

    private static Character Npc(string name) => new Character
    {
        Name1 = name, Name2 = name, AI = CharacterAI.Computer,
        Class = CharacterClass.Warrior, Race = CharacterRace.Human, Level = 5, HP = 40, MaxHP = 50,
    };

    private static (Character follower, SessionContext ctx, ProbeStream stream) Follower(string username, long hp)
    {
        var stream = new ProbeStream();
        var term = new TerminalEmulator(new MemoryStream(), stream);
        stream.TerminalLock = term._streamWriterLock;
        var c = new Character
        {
            Name1 = username, Name2 = username, AI = CharacterAI.Human,
            Class = CharacterClass.Cleric, Race = CharacterRace.Dwarf, Level = 8,
            HP = hp, MaxHP = 90, Mana = 20, MaxMana = 30, Stamina = 11,
            RemoteTerminal = term, GroupPlayerUsername = username,
        };
        c.CurrentCombatStamina = 70;
        var ctx = new SessionContext { OutputStream = stream, GmcpEnabled = true, Username = username, Terminal = term };
        stream.VitalsLock = ctx.GmcpVitalsLock;
        return (c, ctx, stream);
    }

    // The JSON of every Char.Vitals frame in the bytes, in order.
    private static JsonElement[] Vitals(byte[] bytes)
    {
        var frames = new List<JsonElement>();
        const string head = "Char.Vitals ";
        for (int i = 0; i + 3 < bytes.Length; i++)
        {
            if (bytes[i] != 0xFF || bytes[i + 1] != 0xFA || bytes[i + 2] != 0xC9) continue;
            int start = i + 3, end = start;
            while (end + 1 < bytes.Length && !(bytes[end] == 0xFF && bytes[end + 1] == 0xF0)) end++;
            string body = Encoding.UTF8.GetString(bytes, start, end - start);
            if (body.StartsWith(head)) frames.Add(JsonDocument.Parse(body.Substring(head.Length)).RootElement.Clone());
            i = end + 1;
        }
        return frames.ToArray();
    }

    private static T WithLeader<T>(Func<MemoryStream, T> run)
    {
        var saved = SessionContext.Current;
        var leaderStream = new MemoryStream();
        SessionContext.Current = new SessionContext { OutputStream = leaderStream, GmcpEnabled = true, Username = "lead" };
        try { return run(leaderStream); }
        finally { SessionContext.Current = saved; }
    }

    [Fact]
    public void EachFollowerGetsItsOwnVitals_OnItsOwnSession_AndNpcsAreSkipped()
    {
        WithLeader(leaderStream =>
        {
            var (ana, anaCtx, anaStream) = Follower("ana", 61);
            var (bo, boCtx, boStream) = Follower("bo", 33);
            var asked = new List<string>();
            var map = new Dictionary<string, SessionContext> { ["ana"] = anaCtx, ["bo"] = boCtx };
            GmcpBridge.EmitVitalsToGroup(new[] { Npc("Vex"), ana, bo }, n => { asked.Add(n); return map[n]; });

            asked.Should().Equal("ana", "bo");
            Vitals(anaStream.ToArray()).Should().ContainSingle().Which.GetProperty("hp").GetInt64().Should().Be(61);
            Vitals(boStream.ToArray()).Should().ContainSingle().Which.GetProperty("hp").GetInt64().Should().Be(33);
            var f = Vitals(anaStream.ToArray())[0];
            f.GetProperty("maxHp").GetInt64().Should().Be(90);
            f.GetProperty("mp").GetInt64().Should().Be(20);
            f.GetProperty("sp").GetInt64().Should().Be(11);
            f.GetProperty("combatSp").GetInt64().Should().Be(70);
            leaderStream.Length.Should().Be(0, "nothing for the followers goes to the leader's client");
            return 0;
        });
    }

    [Fact]
    public void ChangeDetectionIsPerSession()
    {
        WithLeader(leaderStream =>
        {
            var (ana, anaCtx, anaStream) = Follower("ana", 61);
            Func<string, SessionContext?> resolve = _ => anaCtx;
            GmcpBridge.EmitVitalsToFollower(ana, resolve);
            GmcpBridge.EmitVitalsToFollower(ana, resolve);
            Vitals(anaStream.ToArray()).Should().HaveCount(1, "nothing changed the second time");

            ana.HP = 40;
            GmcpBridge.EmitVitalsToFollower(ana, resolve);
            Vitals(anaStream.ToArray()).Should().HaveCount(2);
            Vitals(anaStream.ToArray())[1].GetProperty("hp").GetInt64().Should().Be(40);

            // The leader's own baseline is untouched by the follower's frames.
            var leader = Npc("Lead"); leader.AI = CharacterAI.Human; leader.HP = 61; leader.MaxHP = 90;
            GmcpBridge.EmitVitalsIfChanged(leader);
            Vitals(leaderStream.ToArray()).Should().ContainSingle();
            return 0;
        });
    }

    [Fact]
    public void SoloAndNpcOnlyPartiesSendNothingMore()
    {
        WithLeader(leaderStream =>
        {
            int asked = 0;
            GmcpBridge.EmitVitalsToGroup(null, _ => { asked++; return null; });
            GmcpBridge.EmitVitalsToGroup(new List<Character>(), _ => { asked++; return null; });
            GmcpBridge.EmitVitalsToGroup(new[] { Npc("Vex"), Npc("Lou") }, _ => { asked++; return null; });
            GmcpBridge.EmitVitalsToFollower(null, _ => { asked++; return null; });
            asked.Should().Be(0);
            leaderStream.Length.Should().Be(0);
            return 0;
        });
    }

    [Fact]
    public void AFollowerWhoLeftOrBrokeNeverThrows()
    {
        WithLeader(leaderStream =>
        {
            var (ana, anaCtx, anaStream) = Follower("ana", 61);
            Action gone = () => GmcpBridge.EmitVitalsToFollower(ana, _ => null);
            gone.Should().NotThrow();
            Action lookupThrows = () => GmcpBridge.EmitVitalsToFollower(ana, _ => throw new InvalidOperationException());
            lookupThrows.Should().NotThrow();

            anaCtx.GmcpEnabled = false;
            GmcpBridge.EmitVitalsToFollower(ana, _ => anaCtx);
            anaStream.Length.Should().Be(0, "GMCP off for that session sends nothing");

            anaCtx.GmcpEnabled = true;
            anaStream.Throw = true;
            Action broken = () => GmcpBridge.EmitVitalsToFollower(ana, _ => anaCtx);
            broken.Should().NotThrow();

            ana.RemoteTerminal = null; // the group cleanup clears it on leave
            Action left = () => GmcpBridge.EmitVitalsToFollower(ana, _ => anaCtx);
            left.Should().NotThrow();
            leaderStream.Length.Should().Be(0);
            return 0;
        });
    }

    [Fact]
    public void TheFrameIsWrittenUnderTheTerminalLock_TakenBeforeTheStreamLock()
    {
        WithLeader(_ =>
        {
            var (ana, anaCtx, anaStream) = Follower("ana", 61);
            GmcpBridge.EmitVitalsToFollower(ana, n => anaCtx);

            var frameWrite = anaStream.Events.FindIndex(e => e.op == "write" && e.length > 3);
            frameWrite.Should().BeGreaterThan(0, "the terminal flush comes before the frame");
            anaStream.Events[frameWrite].term.Should().BeTrue("the frame is written holding the follower terminal's writer lock");
            anaStream.Events[frameWrite].stream.Should().BeTrue();
            // The terminal's own flush, taken under its lock first, must not hold the stream lock:
            // if the stream lock were taken first, this flush would run inside it.
            var before = anaStream.Events.Take(frameWrite).ToList();
            before.Should().Contain(e => e.op == "flush" && e.term);
            before.Should().OnlyContain(e => !e.stream, "no stream lock before the terminal lock is held");
            return 0;
        });
    }

    [Fact]
    public void ANewLoginUnderTheSameNameGetsNothingForTheOldCharacter()
    {
        WithLeader(leaderStream =>
        {
            var (ana, _, oldStream) = Follower("ana", 61);
            var (_, newCtx, newStream) = Follower("ana", 99);   // the replacement session has its own terminal
            GmcpBridge.EmitVitalsToFollower(ana, _ => newCtx);
            newStream.Length.Should().Be(0);
            oldStream.Length.Should().Be(0);
            leaderStream.Length.Should().Be(0);
            return 0;
        });
    }

    [Fact]
    public void TheChangeCheckAndTheWriteHappenUnderTheSessionVitalsLock_OnBothPaths()
    {
        // Follower path (from the leader's thread).
        WithLeader(_ =>
        {
            var (ana, anaCtx, anaStream) = Follower("ana", 61);
            GmcpBridge.EmitVitalsToFollower(ana, n => anaCtx);
            anaStream.Events.Where(e => e.op == "write" && e.length > 3).Should().OnlyContain(e => e.vitals);
            return 0;
        });
        // The session's own path (the follower's loop after the group, or any player).
        var (me, meCtx, meStream) = Follower("me", 50);
        var saved = SessionContext.Current;
        SessionContext.Current = meCtx;
        try { GmcpBridge.EmitVitalsIfChanged(me); }
        finally { SessionContext.Current = saved; }
        meStream.Events.Where(e => e.op == "write" && e.length > 3).Should().NotBeEmpty().And.OnlyContain(e => e.vitals);
    }

    [Fact]
    public void CallSitesSendToTheGroupBesideEveryLeaderVitalsSend()
    {
        var root = new DirectoryInfo(AppContext.BaseDirectory);
        while (root != null && !File.Exists(Path.Combine(root.FullName, "usurper-reloaded.csproj"))) root = root.Parent;
        string combat = File.ReadAllText(Path.Combine(root!.FullName, "Scripts", "Systems", "CombatEngine.cs"));
        string dungeon = File.ReadAllText(Path.Combine(root.FullName, "Scripts", "Locations", "DungeonLocation.cs"));

        // Three leader-side Char.Vitals sends in combat are followed by the group send; the fourth, at
        // combat end, sits inside the leader's IsActive check, so the group send comes after that block.
        CountOf(combat, "GmcpBridge.EmitVitalsIfChanged(player);").Should().Be(4);
        CountOf(combat, "GmcpBridge.EmitVitalsIfChanged(player);\n            UsurperRemake.Server.GmcpBridge.EmitVitalsToGroup(result.Teammates);").Should().Be(3);
        int end = combat.IndexOf("GmcpBridge.Emit(\"Char.Combat.End\"");
        end.Should().BeGreaterThan(0);
        int blockClose = combat.IndexOf("\n        }\n", end);
        int groupAtEnd = combat.IndexOf("GmcpBridge.EmitVitalsToGroup(result.Teammates);", end);
        groupAtEnd.Should().BeGreaterThan(blockClose, "the combat-end group send is outside the leader's GMCP check");
        groupAtEnd.Should().BeLessThan(combat.IndexOf("finally", end));
        CountOf(combat, "GmcpBridge.EmitVitalsToGroup(result.Teammates);").Should().Be(4);
        // The follower's own turn, and the two shared rests.
        int turn = combat.IndexOf("private async Task ProcessGroupedPlayerTurn(");
        combat.IndexOf("GmcpBridge.EmitVitalsToFollower(teammate);", turn).Should().BeGreaterThan(turn);
        CountOf(dungeon, "GmcpBridge.EmitVitalsToFollower(mate);").Should().Be(2);
    }

    private static int CountOf(string text, string needle)
    {
        int n = 0, i = 0;
        while ((i = text.IndexOf(needle, i, StringComparison.Ordinal)) >= 0) { n++; i += needle.Length; }
        return n;
    }
}
