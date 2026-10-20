using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Runtime.CompilerServices;
using System.Text.RegularExpressions;
using FluentAssertions;
using UsurperRemake;
using UsurperRemake.Server;
using UsurperRemake.Systems;
using Xunit;

namespace UsurperReborn.Tests;

/// <summary>
/// 1.3.5: in MUD mode (a session active, or the online server) NPCSpawnSystem.ActiveNPCs is a snapshot copy.
/// The dead-NPC prune removes through NPCSpawnSystem.RemoveNPC, so corpses leave the real roster and the
/// log counts what was removed; InvalidateSnapshot lets a test that edits the real list directly be seen
/// by the next session read.
/// </summary>
[Collection("SharedGameSingletons")]
public class NpcSnapshot135Tests
{
    private const BindingFlags Priv = BindingFlags.NonPublic | BindingFlags.Instance;
    private const BindingFlags PrivStatic = BindingFlags.NonPublic | BindingFlags.Static;

    private static List<NPC> Real() =>
        (List<NPC>)typeof(NPCSpawnSystem).GetField("spawnedNPCs", Priv)!.GetValue(NPCSpawnSystem.Instance)!;

    private static SessionContext Session(string key) =>
        new SessionContext { InputStream = Stream.Null, OutputStream = Stream.Null, Username = key, CharacterKey = key };

    private static NPC Corpse(string id, string name, int daysDead, bool aged) => new NPC
    {
        ID = id, Name1 = name, Name2 = name, Class = CharacterClass.Warrior, Level = 10, HP = 0, MaxHP = 100,
        IsDead = true, IsAgedDeath = aged, IsPermaDead = !aged, DeathDate = DateTime.UtcNow.AddDays(-daysDead),
        CurrentLocation = "Main Street",
    };

    private static NPC Living(string id, string name) => new NPC
    {
        ID = id, Name1 = name, Name2 = name, Class = CharacterClass.Warrior, Level = 10, HP = 100, MaxHP = 100,
        CurrentLocation = "Main Street",
    };

    /// <summary>A timer-less logger swapped in as the instance, so nothing is flushed away before the test reads it.</summary>
    private sealed class LogCapture : IDisposable
    {
        private static readonly FieldInfo InstanceField = typeof(DebugLogger).GetField("_instance", PrivStatic)!;
        private readonly object? _previous;
        private readonly ConcurrentQueue<string> _queue = new();

        public LogCapture()
        {
            _previous = InstanceField.GetValue(null);
            var logger = (DebugLogger)RuntimeHelpers.GetUninitializedObject(typeof(DebugLogger));
            typeof(DebugLogger).GetField("logQueue", Priv)!.SetValue(logger, _queue);
            typeof(DebugLogger).GetField("isEnabled", Priv)!.SetValue(logger, true);
            typeof(DebugLogger).GetField("minimumLevel", Priv)!.SetValue(logger, DebugLogger.LogLevel.Debug);
            InstanceField.SetValue(null, logger);
        }

        public string[] Lines(string text) => _queue.ToArray().Where(l => l.Contains(text)).ToArray();

        public void Dispose() => InstanceField.SetValue(null, _previous);
    }

    private static void Prune()
    {
        var field = typeof(WorldSimulator).GetField("_instance", PrivStatic)!;
        var before = field.GetValue(null);
        try { typeof(WorldSimulator).GetMethod("PrunePermanentlyDeadNPCs", Priv)!.Invoke(new WorldSimulator(), null); }
        finally { field.SetValue(null, before); }
    }

    [Fact]
    public void Prune_InSnapshotMode_RemovesOldCorpsesFromTheRealRoster_AndLogsTheCount()
    {
        var aged = Corpse("ns135_aged", "Snapshot Aged", 8, aged: true);
        var slain = Corpse("ns135_slain", "Snapshot Slain", 10, aged: false);
        var recent = Corpse("ns135_recent", "Snapshot Recent", 1, aged: false);
        var added = new[] { aged, slain, recent };
        Real().AddRange(added);
        NPCSpawnSystem.Instance.InvalidateSnapshot();
        var saved = SessionContext.Current;
        try
        {
            using var log = new LogCapture();
            SessionContext.Current = Session("ns135prune");
            NPCSpawnSystem.Instance.ActiveNPCs.Should().Contain(aged, "the session read is the snapshot of the roster");
            int before = Real().Count;
            Prune();
            int after = Real().Count;

            Real().Should().NotContain(aged, "an old-age corpse dead more than 7 days leaves the real roster");
            Real().Should().NotContain(slain, "a permanent combat corpse dead more than 7 days leaves the real roster");
            Real().Should().Contain(recent, "a corpse inside the 7 day window stays");
            NPCSpawnSystem.Instance.ActiveNPCs.Should().NotContain(aged, "the next session read is rebuilt without it");

            var line = log.Lines("PrunePermanentlyDeadNPCs: removed").Should().ContainSingle().Subject;
            var m = Regex.Match(line, @"removed (\d+) aged");
            m.Success.Should().BeTrue(line);
            int.Parse(m.Groups[1].Value).Should().Be(before - after, "the log counts what actually left the roster");
            (before - after).Should().BeGreaterThanOrEqualTo(2);
        }
        finally
        {
            SessionContext.Current = saved;
            foreach (var n in added) Real().Remove(n);
            NPCSpawnSystem.Instance.InvalidateSnapshot();
        }
    }

    [Fact]
    public void Prune_InSnapshotMode_KeepsACorpseInsideTheWindow()
    {
        var recent = Corpse("ns135_keep", "Snapshot Keep", 6, aged: true);
        Real().Add(recent);
        NPCSpawnSystem.Instance.InvalidateSnapshot();
        var saved = SessionContext.Current;
        try
        {
            SessionContext.Current = Session("ns135keep");
            Prune();
            Real().Should().Contain(recent, "a corpse dead 6 days is inside the 7 day window");
            NPCSpawnSystem.Instance.ActiveNPCs.Should().Contain(recent);
        }
        finally
        {
            SessionContext.Current = saved;
            Real().Remove(recent);
            NPCSpawnSystem.Instance.InvalidateSnapshot();
        }
    }

    [Fact]
    public void RemoveNPC_TakesItFromTheRealRoster_AndTheNextSessionReadNoLongerShowsIt()
    {
        var npc = Living("ns135_rm", "Snapshot Leaver");
        Real().Add(npc);
        NPCSpawnSystem.Instance.InvalidateSnapshot();
        var saved = SessionContext.Current;
        try
        {
            SessionContext.Current = Session("ns135rm");
            NPCSpawnSystem.Instance.ActiveNPCs.Should().Contain(npc, "the snapshot holds it before the removal");
            NPCSpawnSystem.Instance.RemoveNPC(npc).Should().BeTrue();
            Real().Should().NotContain(npc);
            NPCSpawnSystem.Instance.ActiveNPCs.Should().NotContain(npc, "the removal bumps the version, so the snapshot is rebuilt");
            NPCSpawnSystem.Instance.RemoveNPC(npc).Should().BeFalse("it is no longer in the roster");
        }
        finally
        {
            SessionContext.Current = saved;
            Real().Remove(npc);
            NPCSpawnSystem.Instance.InvalidateSnapshot();
        }
    }

    [Fact]
    public void InvalidateSnapshot_LetsTheNextSessionReadSeeADirectEdit()
    {
        var npc = Living("ns135_inv", "Snapshot Newcomer");
        var saved = SessionContext.Current;
        try
        {
            SessionContext.Current = Session("ns135inv");
            _ = NPCSpawnSystem.Instance.ActiveNPCs;   // a snapshot exists
            SessionContext.Current = null;
            Real().Add(npc);                           // a direct edit: no version bump
            SessionContext.Current = Session("ns135inv");
            NPCSpawnSystem.Instance.ActiveNPCs.Should().NotContain(npc, "without the bump the session reads the old snapshot");
            NPCSpawnSystem.Instance.InvalidateSnapshot();
            NPCSpawnSystem.Instance.ActiveNPCs.Should().Contain(npc, "after the bump the snapshot is rebuilt from the real roster");
        }
        finally
        {
            SessionContext.Current = saved;
            Real().Remove(npc);
            NPCSpawnSystem.Instance.InvalidateSnapshot();
        }
    }
}
