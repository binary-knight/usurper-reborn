using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using FluentAssertions;
using UsurperRemake;
using UsurperRemake.Systems;
using Xunit;
using static UsurperReborn.Tests.PartySurvival133Harness;

namespace UsurperReborn.Tests;

/// <summary>
/// 1.3.7 LC: the attack bonus a Warrior or Barbarian takes from a room feature ("The next battle will be easier.")
/// lasts through the next fight the player starts and ends with it. The fight start used to clear it before round 1.
/// </summary>
[Collection("SharedGameSingletons")]
public class LC137FeatureBonus
{
    private const string Src = "feature_weakness";

    /// <summary>Serves one byte per read and records, at each read (each prompt of the fight), whether the hero holds the feature entry.</summary>
    private sealed class WatchStream : Stream
    {
        private readonly byte[] _data;
        private int _pos;
        private readonly Func<int> _probe;
        public readonly List<int> Seen = new();
        public WatchStream(string script, Func<int> probe) { _data = Encoding.UTF8.GetBytes(script); _probe = probe; }
        public override int Read(byte[] buffer, int offset, int count)
        {
            Seen.Add(_probe());
            if (_pos >= _data.Length) return 0;
            buffer[offset] = _data[_pos++];
            return 1;
        }
        public override Task<int> ReadAsync(byte[] buffer, int offset, int count, CancellationToken ct) => Task.FromResult(Read(buffer, offset, count));
        public override bool CanRead => true;
        public override bool CanSeek => false;
        public override bool CanWrite => false;
        public override long Length => _data.Length;
        public override long Position { get => _pos; set => throw new NotSupportedException(); }
        public override void Flush() { }
        public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();
        public override void SetLength(long value) => throw new NotSupportedException();
        public override void Write(byte[] buffer, int offset, int count) => throw new NotSupportedException();
    }

    private static async Task TakeFeature(Character hero)
    {
        var term = new TerminalEmulator(new ScriptedStream(""), new MemoryStream());
        var m = typeof(FeatureInteractionSystem).GetMethod("ApplyClassSpecificBonus", BindingFlags.NonPublic | BindingFlags.Instance)!;
        await (Task)m.Invoke(FeatureInteractionSystem.Instance, new object?[] { hero, 20, term, new FeatureOutcome(), null })!;
    }

    private static (CombatEngine engine, WatchStream watch, MemoryStream output) Engine(Character hero, string script, int seed = 133)
    {
        var output = new MemoryStream();
        var watch = new WatchStream(script, () => hero.SlotBonusValue(BonusSlot.Attack, Src));
        var engine = new CombatEngine(new TerminalEmulator(watch, output));
        engine.SeedRandomForTests(seed);
        return (engine, watch, output);
    }

    private static async Task<CombatResult> Fight(CombatEngine e, Character hero, Monster mob)
    {
        var task = e.PlayerVsMonsters(hero, new List<Monster> { mob }, new List<Character>(), offerMonkEncounter: false);
        (await Task.WhenAny(task, Task.Delay(TimeSpan.FromSeconds(60)))).Should().BeSameAs(task, "the fight ends");
        return await task;
    }

    private static readonly string Attacks = string.Concat(Enumerable.Repeat("A\n\n", 20)) + string.Concat(Enumerable.Repeat("P\n\n", 60)) + new string((char)10, 300);

    [Fact]
    public async Task TheFeatureBonus_IsInTheNextFightsFirstRound()
    {
        var hero = Hero();
        await TakeFeature(hero);
        hero.SlotBonusValue(BonusSlot.Attack, Src).Should().Be(5 + 20 / 10, "the feature gave it");
        var (e, watch, output) = Engine(hero, Attacks);
        var res = await Fight(e, hero, Mob("Rat", "Goblinoid", hp: 1, str: 1));
        res.Outcome.Should().Be(CombatOutcome.Victory, "{0}", Encoding.UTF8.GetString(output.ToArray()));
        watch.Seen.Should().NotBeEmpty();
        watch.Seen[0].Should().Be(7, "the first prompt of the fight is round 1 and the bonus is still held");
    }

    [Fact]
    public async Task TheFeatureBonus_IsGoneAfterThatFight_AndAbsentInTheFightAfter()
    {
        var hero = Hero();
        await TakeFeature(hero);
        var (e, watch, output) = Engine(hero, Attacks + Attacks);
        await Fight(e, hero, Mob("Rat", "Goblinoid", hp: 1, str: 1));
        hero.SlotBonusValue(BonusSlot.Attack, Src).Should().Be(0, "the fight end clears it");
        hero.TempAttackBonus.Should().Be(0);
        watch.Seen.Clear();
        await Fight(e, hero, Mob("Rat", "Goblinoid", hp: 1, str: 1));
        watch.Seen.Should().NotBeEmpty();
        watch.Seen.Should().OnlyContain(v => v == 0, "the second fight starts without it");
    }

    [Fact]
    public async Task AnotherSlotBonus_IsStillClearedAtFightStart()
    {
        var hero = Hero();
        await TakeFeature(hero);
        hero.AddSlotBonus(BonusSlot.Attack, "other_buff", 4, 0);
        hero.AddSlotBonus(BonusSlot.Defense, "other_ward", 6, 0);
        int otherSeen = -1;
        var output = new MemoryStream();
        var watch = new WatchStream(Attacks, () => { if (otherSeen < 0) otherSeen = hero.SlotBonusValue(BonusSlot.Attack, "other_buff") + hero.SlotBonusValue(BonusSlot.Defense, "other_ward"); return 0; });
        var e = new CombatEngine(new TerminalEmulator(watch, output));
        e.SeedRandomForTests(133);
        await Fight(e, hero, Mob("Rat", "Goblinoid", hp: 1, str: 1));
        otherSeen.Should().Be(0, "any other bonus is cleared at the fight start as before");
    }

    [Fact]
    public async Task ALostFight_EndsTheBonusToo()
    {
        var hero = Hero(hp: 5);
        await TakeFeature(hero);
        var (e, watch, output) = Engine(hero, "A\n" + string.Concat(Enumerable.Repeat("1\n\n", 30)) + new string((char)10, 300));
        var res = await Fight(e, hero, Mob("Ogre", "Giant", hp: 100000, str: 5000));
        res.Outcome.Should().NotBe(CombatOutcome.Victory, "{0}", Encoding.UTF8.GetString(output.ToArray()));
        hero.SlotBonusValue(BonusSlot.Attack, Src).Should().Be(0, "a lost fight ends it");
    }
}
