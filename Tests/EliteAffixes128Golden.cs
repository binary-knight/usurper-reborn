using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Security.Cryptography;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using UsurperRemake;
using UsurperRemake.Systems;
using UsurperRemake.UI;

namespace UsurperReborn.Tests;

/// <summary>
/// 1.2.8 difficulty pass, piece P6b: seeded fights with no elite in them, run on the real
/// CombatEngine.PlayerVsMonsters with a random that writes down every number it gives. The signature of a
/// fight is the count and a hash of those draws up to the last foe's death, the outcome and the rounds (the
/// screen and the draws after the fight are not in it: victory flavour and loot use the shared random). This file uses
/// nothing that the affix piece adds, so the same file builds and runs on the code before the piece
/// (release-1.2.8 at 3a8dd9dc); the list in EliteAffixes128Tests.GoldenFights was written by it there.
/// </summary>
internal static class EliteAffixes128Golden
{
    internal sealed class RecordingRandom : Random
    {
        public readonly List<string> Calls = new();
        /// <summary>When set and true, a draw is given but not written down (the fight is over).</summary>
        public Func<bool>? Over;
        public RecordingRandom(int seed) : base(seed) { }
        private void Note(string call) { if (Over == null || !Over()) Calls.Add(call); }
        public override int Next() { int v = base.Next(); Note($"Next()={v}"); return v; }
        public override int Next(int maxValue) { int v = base.Next(maxValue); Note($"Next({maxValue})={v}"); return v; }
        public override int Next(int minValue, int maxValue) { int v = base.Next(minValue, maxValue); Note($"Next({minValue},{maxValue})={v}"); return v; }
        public override double NextDouble() { double v = base.NextDouble(); Note($"NextDouble()={v:R}"); return v; }
    }

    /// <summary>Serves the scripted bytes once, then reports the end of the input.</summary>
    internal sealed class ScriptedStream : Stream
    {
        private readonly byte[] _data;
        private int _pos;
        public ScriptedStream(string script) { _data = Encoding.UTF8.GetBytes(script); }
        public override int Read(byte[] buffer, int offset, int count)
        {
            if (_pos >= _data.Length) return 0;
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

    internal sealed class Run
    {
        public CombatResult? Result;
        public Exception? Error;
        public string Transcript = "";
        public List<string> Draws = new();
    }

    private static readonly FieldInfo EngineRandom = typeof(CombatEngine).GetField("random", BindingFlags.Instance | BindingFlags.NonPublic)!;

    /// <summary>[A]ttack, then the target's number (or Enter for a random one) while more than one foe
    /// stands; [P]ass answers what a victory asks.</summary>
    internal static string Attacks(int n = 200, string target = "") => string.Concat(Enumerable.Repeat("A\n" + target + "\n", n)) + string.Concat(Enumerable.Repeat("P\n", 10));

    internal static async Task<Run> Fight(Character hero, List<Monster> foes, List<Character>? team = null, bool ambush = false, int seed = 128, string? script = null, Action<CombatEngine>? setup = null)
    {
        var run = new Run();
        _ = new GriefSystem();   // a fresh fallback instance: grief left live by another test would speak and draw in this fight
        var output = new MemoryStream();
        var term = new TerminalEmulator(new ScriptedStream(script ?? Attacks()), output);
        var engine = new CombatEngine(term);
        // the draws of the fight itself: once the last foe is dead, what follows (loot, rewards) also rolls
        // outside the engine with the shared random, so its draws are not the same from run to run
        var rng = new RecordingRandom(seed) { Over = () => foes.All(m => !m.IsAlive) };
        EngineRandom.SetValue(engine, rng);
        setup?.Invoke(engine);
        try
        {
            var task = engine.PlayerVsMonsters(hero, foes, team, offerMonkEncounter: false, isAmbush: ambush);
            var done = await Task.WhenAny(task, Task.Delay(TimeSpan.FromSeconds(60)));
            if (done != task) throw new TimeoutException("the fight did not end");
            run.Result = await task;
        }
        catch (Exception ex) { run.Error = ex; }
        term.StreamWriterInternal?.Flush();
        run.Transcript = Encoding.UTF8.GetString(output.ToArray());
        run.Draws = rng.Calls;
        return run;
    }

    internal static Character Hero(long hp = 6000, long strength = 60, int level = 10, CharacterClass cls = CharacterClass.Warrior) => new Character
    {
        Name1 = "Tester", Name2 = "Tester", AI = CharacterAI.Human, Class = cls, Race = CharacterRace.Human, Level = level,
        HP = hp, MaxHP = hp, BaseMaxHP = hp, Strength = strength, BaseStrength = strength, Defence = 20, BaseDefence = 20,
        Dexterity = 30, BaseDexterity = 30, Agility = 20, BaseAgility = 20, Constitution = 30, BaseConstitution = 30,
        Intelligence = 20, BaseIntelligence = 20, Wisdom = 20, BaseWisdom = 20, Stamina = 100, BaseStamina = 100,
        Mental = 80, Gold = 0, Healing = 0, AutoHeal = false, CombatSpeed = CombatSpeed.Instant,
    };

    internal static Character Mate(string name, long hp = 3000) => new Character
    {
        Name1 = name, Name2 = name, AI = CharacterAI.Computer, Class = CharacterClass.Warrior, Race = CharacterRace.Human, Level = 10,
        HP = hp, MaxHP = hp, BaseMaxHP = hp, Strength = 40, BaseStrength = 40, Defence = 10, BaseDefence = 10,
        Dexterity = 20, BaseDexterity = 20, Agility = 10, BaseAgility = 10, Constitution = 20, BaseConstitution = 20,
        Stamina = 50, BaseStamina = 50, Mental = 80, CombatSpeed = CombatSpeed.Instant,
    };

    /// <summary>A group as the generator builds it for a caller with no encounter rules (no elite at any
    /// switch state): the first seed from `from` that gives `size` members. Nobody speaks, so no phrase
    /// is drawn from the shared random.</summary>
    internal static List<Monster> Group(int level, int size, int from)
    {
        for (int seed = from; seed < from + 5000; seed++)
        {
            var g = MonsterGenerator.GenerateMonsterGroup(level, new Random(seed), MonsterGenerator.OldGodApproachScale(level), null);
            if (g.Count != size || g.Any(m => m.IsMiniBoss || m.IsBoss)) continue;
            foreach (var m in g) { m.CanSpeak = false; m.Experience = 1; m.Gold = 0; }
            return g;
        }
        throw new InvalidOperationException("no group of that size");
    }

    private static string Sha(string s) => Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(s))).Substring(0, 16);

    internal static string Signature(Run run) =>
        $"{run.Draws.Count}:{Sha(string.Join("\n", run.Draws))}:{(run.Error != null ? "error " + run.Error.GetType().Name : run.Result!.Outcome + " in " + run.Result.CurrentRound)}";

    /// <summary>The golden fights by name. Each is run fresh; the language is the caller's to set.</summary>
    internal static readonly (string Name, Func<Task<Run>> Fight)[] Fights =
    {
        ("floor 7 pair, solo", () => Fight(Hero(), Group(7, 2, 0))),
        ("floor 7 pair, solo, ambush", () => Fight(Hero(), Group(7, 2, 100), ambush: true)),
        ("floor 35 trio, level 35 hero, two teammates", () => Fight(Hero(40_000, 2500, 35), Group(35, 3, 200), new List<Character> { Mate("Ally One", 15_000), Mate("Ally Two", 15_000) })),
        ("floor 35 trio, level 35 hero, one teammate, ambush", () => Fight(Hero(40_000, 2500, 35), Group(35, 3, 300), new List<Character> { Mate("Ally One", 15_000) }, ambush: true)),
        ("floor 50 four, level 50 hero, two teammates", () => Fight(Hero(70_000, 5000, 50), Group(50, 4, 400), new List<Character> { Mate("Ally One", 25_000), Mate("Ally Two", 25_000) }, seed: 5050)),
        // 1.3.3: monsters past floor 50 grow a full level a floor, so this hero (no armour) needs more HP to win
        ("floor 80 five, level 80 cleric, ambush", () => Fight(Hero(300_000, 12_000, 80, CharacterClass.Cleric), Group(80, 5, 500), ambush: true, seed: 8080)),
        ("floor 3 single", () => Fight(Hero(600, 30, 3), Group(3, 1, 600), seed: 3)),
    };
}
