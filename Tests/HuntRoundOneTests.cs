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
    public void EveryLiveRiderCaller_PassesTheArmoryBoostedHit()
    {
        string src = Source("Scripts/Systems/CombatEngine.cs");
        Regex.Matches(src, @"ApplyPlayerSwingOnHitEffects\(player, \w+, TeamHQBonus\.ApplyAttack\(player, ").Count.Should().Be(3);
        src.Should().Contain("ApplyPostHitEnchantments(player, offHandTarget, TeamHQBonus.ApplyAttack(player, ohDamage)");
        src.Should().Contain("ApplyPostHitEnchantments(teammate, target, TeamHQBonus.ApplyAttack(teammate, damage)");
        // riders that do not come from the hit take the Armory themselves
        src.Should().Contain("enchantDamage = TeamHQBonus.ApplyAttack(player, enchantDamage);");
        src.Should().Contain("Math.Max(1, TeamHQBonus.ApplyAttack(attacker, weapon.PoisonDamage))");
    }

    [Fact]
    public void ADuelDefenderFromASave_KeepsItsTeam_AndAnEchoDoesNot()
    {
        var data = new PlayerData { Name1 = "defender", Name2 = "Defender", Level = 30, MaxHP = 500, Team = "Iron Wolves" };
        PlayerCharacterLoader.CreateFromSaveData(data, "Defender").Team.Should().Be("Iron Wolves");
        PlayerCharacterLoader.CreateFromSaveData(data, "Defender", isEcho: true).Team.Should().Be("");
    }

    [Fact]
    public void SparingAnAssassinationTarget_DoesNotMeetItsObjective()
    {
        var hunter = new Character { Name1 = "assassin_h", Name2 = "Assassin H", Level = 32 };
        var target = new NPC { ID = "npc_contract_obj", Name1 = "Contract Obj", Name2 = "Contract Obj", Level = 30 };
        var contract = new Quest { Title = "Contract", QuestTarget = QuestTarget.Assassin, Occupier = hunter.Name2, TargetNPCName = target.Name, Date = DateTime.Now, DaysToComplete = 30 };
        contract.Objectives.Add(new QuestObjective(QuestObjectiveType.DefeatNPC, "Kill Contract Obj", 1, target.Name, target.Name));
        QuestSystem.AddQuestToDatabase(contract);
        QuestSystem.RecordNPCDefeat(hunter, target, killed: false);
        contract.Objectives[0].IsComplete.Should().BeFalse("a manual turn-in must not accept a target who walked away");
        contract.Deleted.Should().BeFalse();
        QuestSystem.RecordNPCDefeat(hunter, target, killed: true);
        contract.Deleted.Should().BeTrue("a kill meets it");
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

    [Fact]
    public void TheCastleSiegeKing_HitsThroughTheBarracks()
    {
        Source("Scripts/Locations/CastleLocation.cs").Should().Contain("kingDamage = TeamHQBonus.ApplyDefense(currentPlayer, kingDamage);");
    }

    [Fact]
    public void TheCastleSiege_ReadsTheTeamsCurrentLevels()
    {
        MethodBody(Source("Scripts/Locations/CastleLocation.cs"), "private async Task CastleSiegeMenu(").Should().Contain("TeamHQBonus.RefreshLevels(currentPlayer, backend);");
    }
}
