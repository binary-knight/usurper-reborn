using System;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using FluentAssertions;
using UsurperRemake;
using UsurperRemake.Systems;
using Xunit;

namespace UsurperReborn.Tests;

/// <summary>
/// v1.2.8: [S] Stats in a duel is an information screen. It shows the status and asks again,
/// so the round goes on and the opponent acts once. A stunned player still loses the turn.
/// </summary>
[Collection("SharedGameSingletons")]
public class CombatStatsFree128Tests
{
    /// <summary>Serves the scripted bytes once, then throws like a dropped socket, which ends the duel.</summary>
    private sealed class ScriptedStream : Stream
    {
        private readonly byte[] _data;
        private int _pos;
        public ScriptedStream(string script) { _data = Encoding.UTF8.GetBytes(script); }
        public override int Read(byte[] buffer, int offset, int count)
        {
            if (_pos >= _data.Length) throw new IOException("scripted disconnect");
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

    private static Character Duelist(string name) => new()
    {
        Name1 = name, Name2 = name, Class = CharacterClass.Warrior, Race = CharacterRace.Human, Level = 10,
        HP = 100_000, MaxHP = 100_000, BaseMaxHP = 100_000,
        Strength = 40, BaseStrength = 40, Defence = 20, BaseDefence = 20,
        Dexterity = 20, BaseDexterity = 20, Agility = 20, BaseAgility = 20,
        Constitution = 20, BaseConstitution = 20, CombatSpeed = CombatSpeed.Instant,
    };

    /// <summary>Runs a duel until the script runs out and returns the whole transcript without colour codes.</summary>
    private static async Task<string> Duel(string script, Character attacker, Character defender)
    {
        var output = new MemoryStream();
        var engine = new CombatEngine(new TerminalEmulator(new ScriptedStream(script), output));
        engine.SeedRandomForTests(128);
        Exception? error = null;
        try { await engine.PlayerVsPlayer(attacker, defender); }
        catch (Exception ex) { error = ex; }
        string text = Regex.Replace(Encoding.UTF8.GetString(output.ToArray()), "\u001b\\[[0-9;]*[A-Za-z]", "");
        error.Should().BeAssignableTo<IOException>("the duel ends when the script runs out; transcript: {0}", text);
        return text;
    }

    /// <summary>Counts lines that match a Loc format string, its placeholders read as wildcards.</summary>
    private static int CountLines(string text, string key)
    {
        var pattern = "^" + Regex.Replace(Regex.Escape(Loc.Get(key)), @"\\\{\d+\}|\{\d+\}", ".*") + "$";
        var re = new Regex(pattern);
        return text.Split('\n').Count(l => re.IsMatch(l.Trim()));
    }

    /// <summary>The opponent's turns: a strike, a class ability or a potion.</summary>
    private static int OpponentActions(string text) =>
        CountLines(text, "combat.pvp_ai_strikes") + CountLines(text, "combat.pvp_ai_uses_ability") + CountLines(text, "combat.pvp_ai_heals");

    [Fact]
    public async Task Stats_ThenAttack_InOneRound_TheOpponentActsOnce()
    {
        var attacker = Duelist("Tester");
        var defender = Duelist("Rival");
        defender.AI = CharacterAI.Computer;

        // S shows the status, Enter closes it, A attacks; the next prompt ends the duel.
        string text = await Duel("S\n\nA\n", attacker, defender);

        text.Should().Contain(Loc.Get("combat.status_combat"), "the status screen is shown");
        CountLines(text, "combat.pvp_your_hp").Should().Be(1, "the player attacks exactly once; transcript: {0}", text);
        OpponentActions(text).Should().Be(1, "S does not spend the round, so the opponent acts once; transcript: {0}", text);
    }

    [Fact]
    public async Task StunnedPlayer_StillLosesTheTurn()
    {
        var attacker = Duelist("Tester");
        var defender = Duelist("Rival");
        defender.AI = CharacterAI.Computer;
        attacker.ApplyStatus(StatusEffect.Stunned, 1);

        // Round 1 the stunned player reads no input; round 2 the A is the player's first action.
        string text = await Duel("A\n", attacker, defender);

        text.Should().Contain(Loc.Get("combat.you_status_prevented", StatusEffect.Stunned.GetDescription().ToLower()),
            "the stun is announced; transcript: {0}", text);
        CountLines(text, "combat.pvp_your_hp").Should().Be(1, "the stunned round has no attack, the A comes in round 2; transcript: {0}", text);
        OpponentActions(text).Should().Be(2, "the opponent acts in the stunned round and in round 2; transcript: {0}", text);
    }
}
