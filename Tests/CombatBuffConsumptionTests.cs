using System;
using System.Collections.Generic;
using System.Linq;
using System.IO;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using FluentAssertions;
using UsurperRemake;
using UsurperRemake.Systems;
using UsurperRemake.UI;
using Xunit;

namespace UsurperReborn.Tests;

/// <summary>
/// v1.1.1 moved per-combat buff consumption from the start of a fight to its end.
/// That is only safe if every exit from PlayerVsMonsters / PlayerVsPlayer reaches the
/// consume point, otherwise "buff, flee, keep the buff" is a free permanent buff.
/// These tests drive real combat through a scripted stream terminal and assert the
/// buff counters are decremented on victory, retreat, defeat, and a thrown disconnect.
/// </summary>
[Collection("SharedGameSingletons")]
public class CombatBuffConsumptionTests
{
    /// <summary>Serves the scripted bytes once, then either reports EOF or throws like a dropped socket.</summary>
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
        var term = new TerminalEmulator(new ScriptedStream(script, throwWhenDrained), output);
        return (new CombatEngine(term), output);
    }

    private static Character MakeBuffedPlayer(long hp = 500)
    {
        return new Character
        {
            Name2 = "Buffy",
            Class = CharacterClass.Warrior,
            Race = CharacterRace.Human,
            Level = 10,
            // Combat start runs RecalculateStats, which rebuilds the derived stats from the
            // Base* fields, so those are what actually decide the fight.
            HP = hp,
            MaxHP = hp,
            BaseMaxHP = hp,
            Strength = 80,
            BaseStrength = 80,
            Defence = 40,
            BaseDefence = 40,
            Dexterity = 30,
            BaseDexterity = 30,
            Agility = 25,
            BaseAgility = 25,
            Constitution = 30,
            BaseConstitution = 30,
            Gold = 100,
            Stamina = 100,
            CombatSpeed = CombatSpeed.Instant,
            WellRestedCombats = 2,
            HerbBuffType = 2,
            HerbBuffCombats = 1,
            HerbBuffValue = 0.1f,
        };
    }

    private static Monster WeakMonster() => new Monster
    {
        Name = "Sewer Rat", Level = 1, HP = 1, MaxHP = 1, Strength = 1, Defence = 0, Experience = 5, Gold = 3,
    };

    private static Monster LethalMonster() => new Monster
    {
        Name = "Doom Engine", Level = 60, HP = 5_000_000, MaxHP = 5_000_000, Strength = 5_000_000, Defence = 0, Experience = 1, Gold = 0,
    };

    private static void AssertBuffsConsumed(Character p, string exit)
    {
        p.WellRestedCombats.Should().Be(1, $"well-rested must count the fight on {exit}");
        p.HerbBuffCombats.Should().Be(0, $"the herb buff must count the fight on {exit}");
        p.HerbBuffType.Should().Be(0, $"an expired herb buff must clear its type on {exit}");
    }

    [Fact]
    public async Task Victory_ConsumesOneCombatOfEveryBuff()
    {
        // Attacks until the rat is dead, then [P]ass answers a possible loot prompt.
        var (engine, output) = MakeEngine(string.Concat(Enumerable.Repeat("A\n", 10)) + string.Concat(Enumerable.Repeat("P\n", 6)));
        var player = MakeBuffedPlayer();
        var (result, error, transcript) = await Run(() => engine.PlayerVsMonsters(player, new List<Monster> { WeakMonster() }, offerMonkEncounter: false), output);
        error.Should().BeNull("transcript: {0}", transcript);
        result!.Outcome.Should().Be(CombatOutcome.Victory, "transcript: {0}", transcript);
        AssertBuffsConsumed(player, "victory");
    }

    [Fact]
    public async Task Retreat_ConsumesOneCombatOfEveryBuff()
    {
        var (engine, output) = MakeEngine(string.Concat(Enumerable.Repeat("R\n", 8)));
        var player = MakeBuffedPlayer();
        player.SmokeBombs = 1; // guaranteed escape so the retreat cannot fail into a normal round
        var (result, error, transcript) = await Run(() => engine.PlayerVsMonsters(player, new List<Monster> { LethalMonsterThatMisses() }), output);
        error.Should().BeNull("transcript: {0}", transcript);
        result!.Outcome.Should().Be(CombatOutcome.PlayerEscaped, "transcript: {0}", transcript);
        AssertBuffsConsumed(player, "retreat");
    }

    [Fact]
    public async Task Defeat_ConsumesOneCombatOfEveryBuff()
    {
        // Attacks until the blow lands, then "press any key" past the epitaph, then [1] temple
        // resurrection at the veil, then enough blank lines to clear any further "press any key".
        var (engine, output) = MakeEngine(string.Concat(Enumerable.Repeat("A\n", 6)) + "\n\n\n1\n" + string.Concat(Enumerable.Repeat("\n", 8)));
        var player = MakeBuffedPlayer(hp: 5);
        var (result, error, transcript) = await Run(() => engine.PlayerVsMonsters(player, new List<Monster> { LethalMonster() }), output);
        error.Should().BeNull("transcript: {0}", transcript);
        // The first lethal blow can trigger Last Stand / Death's Door, which rewrites the outcome
        // to PlayerEscaped with the fight over; either way the fight ended in defeat, not victory.
        result!.Outcome.Should().BeOneOf(new[] { CombatOutcome.PlayerDied, CombatOutcome.PlayerEscaped }, "transcript: {0}", transcript);
        AssertBuffsConsumed(player, "defeat");
    }

    [Fact]
    public async Task Disconnect_MidFight_StillConsumesBuffs()
    {
        // One attack round, then the input stream throws like a dropped socket.
        var (engine, output) = MakeEngine("A\n", throwWhenDrained: true);
        var player = MakeBuffedPlayer();
        var monster = LethalMonsterThatMisses();
        var (_, error, transcript) = await Run(() => engine.PlayerVsMonsters(player, new List<Monster> { monster }), output);
        error.Should().BeOfType<IOException>("the disconnect must propagate, not spin on empty input; transcript: {0}", transcript);
        AssertBuffsConsumed(player, "disconnect");
    }

    [Fact]
    public async Task ClosedPeer_ThrowsInsteadOfSpinningOnEmptyInput()
    {
        // A MUD client that closes its socket used to read as an endless stream of empty
        // lines; every re-prompt loop then spun at full CPU. EOF must surface as an IOException.
        var (engine, output) = MakeEngine("A\n", throwWhenDrained: false);
        var player = MakeBuffedPlayer();
        var (_, error, transcript) = await Run(() => engine.PlayerVsMonsters(player, new List<Monster> { LethalMonsterThatMisses() }), output);
        error.Should().BeOfType<IOException>("transcript: {0}", transcript);
        AssertBuffsConsumed(player, "closed peer");
    }

    [Fact]
    public async Task PvP_Disconnect_MidDuel_StillConsumesBuffsAndRestoresState()
    {
        var (engine, output) = MakeEngine("A\n", throwWhenDrained: true);
        var attacker = MakeBuffedPlayer(hp: 100_000);
        var defender = MakeBuffedPlayer(hp: 100_000);
        defender.Name2 = "Rival";
        var (_, error, transcript) = await Run(() => engine.PlayerVsPlayer(attacker, defender), output);
        error.Should().BeOfType<IOException>("transcript: {0}", transcript);
        // A duel has always consumed only the attacker's buffs (the defender is an NPC or an
        // offline player whose own fights count their own buffs), so only the attacker is asserted.
        AssertBuffsConsumed(attacker, "pvp disconnect");
        defender.WellRestedCombats.Should().Be(2, "a duel never consumes the defender's buffs");
    }

    /// <summary>Runs a fight and returns its result, or the exception it threw, plus the terminal transcript.</summary>
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

    /// <summary>A monster with a mountain of HP that cannot hurt the player, so the fight only ends the way the script says.</summary>
    private static Monster LethalMonsterThatMisses() => new Monster
    {
        Name = "Training Dummy", Level = 1, HP = 5_000_000, MaxHP = 5_000_000, Strength = 0, Defence = 0, Experience = 1, Gold = 0,
    };
}
