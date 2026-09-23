using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using FluentAssertions;
using UsurperRemake.Systems;
using Xunit;

namespace UsurperReborn.Tests;

/// <summary>
/// v1.1.11: findings of the first review of the 1.1.11 branch as a whole (Codex).
/// </summary>
[Collection("SharedGameSingletons")]
public class HuntRoundOneTests
{
    private static readonly BindingFlags F = BindingFlags.NonPublic | BindingFlags.Instance;

    private sealed class ScriptedStream : Stream
    {
        private readonly byte[] _data; private int _pos;
        public ScriptedStream(string script) { _data = Encoding.UTF8.GetBytes(script); }
        public override int Read(byte[] buffer, int offset, int count)
        {
            if (_pos >= _data.Length) return 0;
            int n = Math.Min(count, _data.Length - _pos); Array.Copy(_data, _pos, buffer, offset, n); _pos += n; return n;
        }
        public override Task<int> ReadAsync(byte[] buffer, int offset, int count, CancellationToken ct) => Task.FromResult(Read(buffer, offset, count));
        public override bool CanRead => true; public override bool CanSeek => false; public override bool CanWrite => false;
        public override long Length => _data.Length; public override long Position { get => _pos; set => throw new NotSupportedException(); }
        public override void Flush() { }
        public override long Seek(long o, SeekOrigin s) => throw new NotSupportedException();
        public override void SetLength(long v) => throw new NotSupportedException();
        public override void Write(byte[] b, int o, int c) => throw new NotSupportedException();
    }

    private static string Source(string relative)
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir != null && !Directory.Exists(Path.Combine(dir.FullName, "Scripts"))) dir = dir.Parent;
        return File.ReadAllText(Path.Combine(dir!.FullName, relative));
    }

    private static string MethodBody(string src, string signature)
    {
        int start = src.IndexOf(signature, StringComparison.Ordinal);
        start.Should().BeGreaterThan(0, signature);
        int end = src.IndexOf("\n    private ", start + 10, StringComparison.Ordinal);
        int endPublic = src.IndexOf("\n    public ", start + 10, StringComparison.Ordinal);
        if (endPublic > 0 && (end < 0 || endPublic < end)) end = endPublic;
        return src.Substring(start, end - start);
    }

    [Fact]
    public async Task AnOldGodsFight_PaysNoEngineXP_NotEvenTheTenXPFloor()
    {
        var data = UsurperRemake.Data.OldGodsData.GetGodBossData(OldGodType.Maelketh);
        var god = (Monster)typeof(OldGodBossSystem).GetMethod("CreateBossMonster", F)!.Invoke(OldGodBossSystem.Instance, new object[] { data })!;
        god.HP = 0;
        var hero = new Character { Name1 = "godslayer", Name2 = "Godslayer", Class = CharacterClass.Warrior, Level = 30, HP = 5000, MaxHP = 5000, AutoLevelUp = false, CombatSpeed = CombatSpeed.Instant, MKills = 100 };
        var result = new CombatResult { Player = hero, Outcome = CombatOutcome.Victory };
        result.DefeatedMonsters.Add(god);
        var engine = new CombatEngine(new TerminalEmulator(new ScriptedStream(string.Concat(Enumerable.Repeat("P\n", 40))), new MemoryStream()));
        typeof(CombatEngine).GetField("currentPlayer", F)!.SetValue(engine, hero);
        await (Task)typeof(CombatEngine).GetMethod("HandleVictoryMultiMonster", F)!.Invoke(engine, new object[] { result, false })!;
        result.ExperienceGained.Should().Be(0, "HandleBossDefeated pays the god's reward, once");
    }

    [Fact]
    public void TheSwingRiders_ScaleFromTheArmoryBoostedHit()
    {
        var calls = Regex.Matches(Source("Scripts/Systems/CombatEngine.cs"), @"ApplyPlayerSwingOnHitEffects\(player, \w+, ([^,]+),");
        calls.Count.Should().Be(3);
        foreach (Match c in calls) c.Groups[1].Value.Should().Be("TeamHQBonus.ApplyAttack(player");
    }

    [Fact]
    public void AWorldBossTelegraph_AppliesTheBarracks()
    {
        MethodBody(Source("Scripts/Systems/WorldBossSystem.cs"), "ApplyLandedTelegraph(").Should().Contain("TeamHQBonus.ApplyDefense(player, dmg)");
    }

    [Fact]
    public void TheFightStartRefresh_EnumeratesACopyOfTheParty()
    {
        Source("Scripts/Systems/CombatEngine.cs").Should().Contain("foreach (var mate in teammates.ToList().Where(t => t is not NPC)) TeamHQBonus.RefreshLevels(mate);");
    }
}
