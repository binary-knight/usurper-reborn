using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using FluentAssertions;
using UsurperRemake;
using UsurperRemake.Systems;
using UsurperRemake.UI;
using Xunit;

namespace UsurperReborn.Tests;

/// <summary>
/// 1.2.1: Iron Rations raises MaxHP during a fight. The bonus used to be added at fight start and
/// subtracted at fight end, so a RecalculateStats in between dropped it and the subtraction left
/// MaxHP too low. RecalculateStats now adds it while the fight is open.
/// </summary>
[Collection("SharedGameSingletons")]
public class IronRations121Tests
{
    private static Character Fed(string name, long hp = 500, int foodType = 3, int combats = 2, float value = 0.15f)
    {
        var c = new Character
        {
            Name2 = name,
            Class = CharacterClass.Warrior,
            Race = CharacterRace.Human,
            Level = 10,
            HP = hp, MaxHP = hp, BaseMaxHP = hp,
            Strength = 80, BaseStrength = 80,
            Defence = 40, BaseDefence = 40,
            Dexterity = 30, BaseDexterity = 30,
            Agility = 25, BaseAgility = 25,
            Constitution = 30, BaseConstitution = 30,
            Gold = 100,
            Stamina = 100,
            CombatSpeed = CombatSpeed.Instant,
            FoodBuffType = foodType,
            FoodBuffCombats = combats,
            FoodBuffValue = value,
        };
        c.RecalculateStats();
        c.HP = c.MaxHP;
        return c;
    }

    // ---------------- the character ----------------

    [Fact]
    public void Begin_RaisesMaxHPAndHPByTheShare()
    {
        var c = Fed("IrBegin");
        long pre = c.MaxHP;
        long bonus = (long)(pre * 0.15f);
        bonus.Should().BeGreaterThan(0);
        c.BeginIronRationsFight();
        c.IronRationsFightOpen.Should().BeTrue();
        c.MaxHP.Should().Be(pre + bonus);
        c.HP.Should().Be(pre + bonus);
        c.EndIronRationsFight();
        c.IronRationsFightOpen.Should().BeFalse();
        c.MaxHP.Should().Be(pre);
        c.HP.Should().Be(pre, "HP is clamped to MaxHP at the end of the fight, as before");
    }

    [Fact]
    public void RecalcMidFight_KeepsTheBonus_AndTheEndRestoresTheExactMaxHP()
    {
        var c = Fed("IrRecalc");
        long pre = c.MaxHP;
        long bonus = (long)(pre * 0.15f);
        c.BeginIronRationsFight();
        c.RecalculateStats();
        c.MaxHP.Should().Be(pre + bonus, "a recalc in the fight keeps the Iron Rations bonus");
        c.HP.Should().Be(pre + bonus);
        c.RecalculateStats();
        c.MaxHP.Should().Be(pre + bonus);
        c.EndIronRationsFight();
        c.MaxHP.Should().Be(pre, "the end removes exactly what is in MaxHP, not a stale amount");
        c.RecalculateStats();
        c.MaxHP.Should().Be(pre);
    }

    [Theory]
    [InlineData(1, 2)]   // Dragon Steak
    [InlineData(2, 2)]   // Honey Bread
    [InlineData(4, 2)]   // Mushroom Soup
    [InlineData(5, 2)]   // food poisoning
    [InlineData(3, 0)]   // Iron Rations, expired
    [InlineData(0, 0)]   // no food
    public void OtherOrExpiredFood_GivesNoBonus(int foodType, int combats)
    {
        var c = Fed("IrOther", foodType: foodType, combats: combats);
        long pre = c.MaxHP;
        c.BeginIronRationsFight();
        c.MaxHP.Should().Be(pre);
        c.RecalculateStats();
        c.MaxHP.Should().Be(pre);
        c.EndIronRationsFight();
        c.MaxHP.Should().Be(pre);
    }

    [Fact]
    public void NoOpenFight_RecalcGivesNoBonus()
    {
        var c = Fed("IrClosed");
        long pre = c.MaxHP;
        c.RecalculateStats();
        c.MaxHP.Should().Be(pre);
        c.EndIronRationsFight();
        c.MaxHP.Should().Be(pre, "closing a fight that was never opened changes nothing");
    }

    [Fact]
    public void TheFlag_IsNotSaved()
    {
        var c = Fed("IrSave");
        c.BeginIronRationsFight();
        JsonSerializer.Serialize(c).Should().NotContain("IronRationsFightOpen");
        typeof(PlayerData).GetProperties().Select(p => p.Name).Should().NotContain(n => n.Contains("IronRations"));
        var back = StatRewards1115Tests.RoundTrip(c);
        back.IronRationsFightOpen.Should().BeFalse();
        c.EndIronRationsFight();
    }

    // ---------------- driven fights ----------------

    private sealed class ScriptedStream : Stream
    {
        private readonly byte[] _data;
        private int _pos;
        private readonly bool _throwWhenDrained;
        public ScriptedStream(string script, bool throwWhenDrained)
        {
            _data = Encoding.UTF8.GetBytes(script);
            _throwWhenDrained = throwWhenDrained;
        }
        public override int Read(byte[] buffer, int offset, int count)
        {
            if (_pos >= _data.Length)
            {
                if (_throwWhenDrained) throw new IOException("scripted disconnect");
                return 0;
            }
            int n = Math.Min(count, _data.Length - _pos);
            Array.Copy(_data, _pos, buffer, offset, n);
            _pos += n;
            return n;
        }
        public override Task<int> ReadAsync(byte[] buffer, int offset, int count, CancellationToken ct)
            => Task.FromResult(Read(buffer, offset, count));
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

    private static (CombatEngine engine, MemoryStream output) MakeEngine(string script, bool throwWhenDrained = false)
    {
        var output = new MemoryStream();
        var engine = new CombatEngine(new TerminalEmulator(new ScriptedStream(script, throwWhenDrained), output));
        engine.SeedRandomForTests(121);
        return (engine, output);
    }

    private static async Task<(CombatResult? result, Exception? error, string transcript)> Run(Func<Task<CombatResult>> fight, MemoryStream output)
    {
        try { var r = await fight(); return (r, null, Transcript(output)); }
        catch (Exception ex) { return (null, ex, Transcript(output)); }
    }

    private static string Transcript(MemoryStream output)
    {
        var text = Encoding.UTF8.GetString(output.ToArray());
        text = System.Text.RegularExpressions.Regex.Replace(text, "\u001b\\[[0-9;]*[A-Za-z]", "");
        return text.Length > 1500 ? text[^1500..] : text;
    }

    private static Monster Rat() => new Monster
    {
        Name = "Sewer Rat", Level = 1, HP = 1, MaxHP = 1, Strength = 1, Defence = 0, Experience = 5, Gold = 3,
    };

    private static Monster Dummy() => new Monster
    {
        Name = "Training Dummy", Level = 1, HP = 5_000_000, MaxHP = 5_000_000, Strength = 0, Defence = 0, Experience = 1, Gold = 0,
    };

    private static Monster Doom() => new Monster
    {
        Name = "Doom Engine", Level = 60, HP = 5_000_000, MaxHP = 5_000_000, Strength = 5_000_000, Defence = 0, Experience = 1, Gold = 0,
    };

    private static void AssertClosed(Character p, long pre, string exit, string transcript)
    {
        p.IronRationsFightOpen.Should().BeFalse($"the fight must be closed after {exit}; transcript: {transcript}");
        long after = p.MaxHP;
        p.RecalculateStats();
        after.Should().Be(p.MaxHP, $"no bonus may be left in or taken out of MaxHP after {exit}");
        after.Should().Be(pre, $"MaxHP after {exit} equals MaxHP before");
    }

    [Fact]
    public async Task Victory_ClosesTheFight_AndRestoresMaxHP()
    {
        var (engine, output) = MakeEngine(string.Concat(Enumerable.Repeat("A\n", 10)) + string.Concat(Enumerable.Repeat("P\n", 6)));
        var p = Fed("IrWin");
        long pre = p.MaxHP;
        var (result, error, transcript) = await Run(() => engine.PlayerVsMonsters(p, new List<Monster> { Rat() }, offerMonkEncounter: false), output);
        error.Should().BeNull("transcript: {0}", transcript);
        result!.Outcome.Should().Be(CombatOutcome.Victory, "transcript: {0}", transcript);
        AssertClosed(p, pre, "victory", transcript);
    }

    [Fact]
    public async Task Retreat_ClosesTheFight_AndRestoresMaxHP()
    {
        var (engine, output) = MakeEngine(string.Concat(Enumerable.Repeat("R\n", 8)));
        var p = Fed("IrFlee");
        p.SmokeBombs = 1;
        long pre = p.MaxHP;
        var (result, error, transcript) = await Run(() => engine.PlayerVsMonsters(p, new List<Monster> { Dummy() }), output);
        error.Should().BeNull("transcript: {0}", transcript);
        result!.Outcome.Should().Be(CombatOutcome.PlayerEscaped, "transcript: {0}", transcript);
        AssertClosed(p, pre, "retreat", transcript);
    }

    [Fact]
    public async Task Defeat_ClosesTheFight()
    {
        var (engine, output) = MakeEngine(string.Concat(Enumerable.Repeat("A\n", 6)) + "\n\n\n1\n" + string.Concat(Enumerable.Repeat("\n", 8)));
        var p = Fed("IrDie", hp: 5);
        var difficulty = DifficultySystem.CurrentDifficulty;
        DifficultySystem.CurrentDifficulty = DifficultyMode.Normal;
        CombatResult? result; Exception? error; string transcript;
        try { (result, error, transcript) = await Run(() => engine.PlayerVsMonsters(p, new List<Monster> { Doom() }), output); }
        finally { DifficultySystem.CurrentDifficulty = difficulty; }
        error.Should().BeNull("transcript: {0}", transcript);
        result!.Outcome.Should().BeOneOf(new[] { CombatOutcome.PlayerDied, CombatOutcome.PlayerEscaped }, "transcript: {0}", transcript);
        p.IronRationsFightOpen.Should().BeFalse("transcript: {0}", transcript);
        long after = p.MaxHP;
        p.RecalculateStats();
        after.Should().Be(p.MaxHP, "no bonus may be left in or taken out of MaxHP after a defeat");
    }

    [Fact]
    public async Task Disconnect_ClosesTheFight_AndRestoresMaxHP()
    {
        var (engine, output) = MakeEngine("A\n", throwWhenDrained: true);
        var p = Fed("IrDrop");
        long pre = p.MaxHP;
        var (_, error, transcript) = await Run(() => engine.PlayerVsMonsters(p, new List<Monster> { Dummy() }), output);
        error.Should().BeOfType<IOException>("transcript: {0}", transcript);
        AssertClosed(p, pre, "a disconnect", transcript);
    }

    [Fact]
    public async Task PvP_Disconnect_ClosesTheFight_AndRestoresMaxHP()
    {
        var (engine, output) = MakeEngine("A\n", throwWhenDrained: true);
        var attacker = Fed("IrDuel", hp: 100_000);
        var defender = Fed("IrRival", hp: 100_000);
        long pre = attacker.MaxHP;
        long defPre = defender.MaxHP;
        var (_, error, transcript) = await Run(() => engine.PlayerVsPlayer(attacker, defender), output);
        error.Should().BeOfType<IOException>("transcript: {0}", transcript);
        AssertClosed(attacker, pre, "a duel disconnect", transcript);
        defender.IronRationsFightOpen.Should().BeFalse("a duel never opens the defender's Iron Rations");
        defender.MaxHP.Should().Be(defPre);
    }
}
