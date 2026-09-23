using System;
using System.IO;
using System.Reflection;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using FluentAssertions;
using UsurperRemake.Systems;
using Xunit;

namespace UsurperReborn.Tests;

/// <summary>
/// v1.1.11: an Old God kill paid its XP and gold three times: the fight's victory paid the god
/// monster's Experience and Gold, HandleBossDefeated paid Level x 2000 and x 500 again, and the dungeon
/// added the result's XPGained and GoldGained a third time. It is paid once, by the handler, at three
/// times the old per-level amount (the maintainer's choice), with the Team HQ Training bonus on it.
/// </summary>
[Collection("SharedGameSingletons")]
public class OldGodPayoutTests
{
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

    private static readonly BindingFlags F = BindingFlags.NonPublic | BindingFlags.Instance;

    [Fact]
    public void TheGodsFight_PaysNothingForTheGod()
    {
        var data = UsurperRemake.Data.OldGodsData.GetGodBossData(OldGodType.Maelketh);
        var god = (Monster)typeof(OldGodBossSystem).GetMethod("CreateBossMonster", F)!.Invoke(OldGodBossSystem.Instance, new object[] { data })!;
        god.Experience.Should().Be(0, "the victory used to pay it on top of the handler");
        god.Gold.Should().Be(0);
    }

    [Fact]
    public async Task TheDungeon_DoesNotPayTheGodsRewardAgain()
    {
        var player = new Character { Name1 = "god_payout", Name2 = "God Payout", Level = 30, HP = 500, MaxHP = 500, Experience = 1000, Gold = 1000 };
        var result = new BossEncounterResult { Success = true, Outcome = BossOutcome.Defeated, God = OldGodType.Maelketh, XPGained = 150_000, GoldGained = 37_500 };
        var term = new TerminalEmulator(new ScriptedStream(new string('\n', 40)), new MemoryStream());
        var dungeon = new DungeonLocation();
        try { await (Task)typeof(DungeonLocation).GetMethod("HandleGodEncounterResult", F)!.Invoke(dungeon, new object[] { result, player, term })!; }
        catch (LocationExitException) { }   // the defeated god's scene leaves the dungeon
        // Other awards on this screen (achievements) depend on state; the god's reward must not be among them.
        (player.Experience - 1000).Should().BeLessThan(result.XPGained, "HandleBossDefeated already paid it");
        (player.Gold - 1000).Should().BeLessThan(result.GoldGained);
    }

    [Fact]
    public void TheOnePayout_IsThreeTimesTheOldPerLevelAmount()
    {
        GameConfig.OldGodDefeatXPPerLevel.Should().Be(3 * 2000);
        GameConfig.OldGodDefeatGoldPerLevel.Should().Be(3 * 500);
    }

    [Fact]
    public async Task TheHandler_PaysTheKillOnce_WithTheTeamsTrainingBonus()
    {
        var data = UsurperRemake.Data.OldGodsData.GetGodBossData(OldGodType.Maelketh);
        var player = new Character { Name1 = "god_payout2", Name2 = "God Payout", Level = 30, HP = 500, MaxHP = 500, Experience = 0, Gold = 0, Team = "Iron Wolves" };
        player.HQTrainingLevel = 2; player.HQLevelsTeam = "Iron Wolves";   // +10% XP
        var term = new TerminalEmulator(new ScriptedStream(new string('\n', 40)), new MemoryStream());
        var res = await (Task<BossEncounterResult>)typeof(OldGodBossSystem).GetMethod("HandleBossDefeated", F)!
            .Invoke(OldGodBossSystem.Instance, new object[] { player, data, term })!;
        long expected = (long)Math.Round(data.Level * 6000 * 1.10);
        player.Experience.Should().Be(expected);
        player.Gold.Should().Be(data.Level * 1500);
        res.XPGained.Should().Be(expected, "reported for the screen, not paid again");
    }
}
