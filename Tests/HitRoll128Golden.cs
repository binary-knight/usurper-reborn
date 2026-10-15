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

namespace UsurperReborn.Tests;

/// <summary>
/// 1.2.8 difficulty pass, piece P7 (the player to-hit roll): seeded fights and seeded single actions, run on
/// the real CombatEngine with a random that writes down every number it gives. A signature is the count and
/// a hash of the draws, then what the draws did (the outcome and the rounds of a fight, the damage of an
/// action). This file uses nothing that the piece adds, so the same file builds and runs on the code before
/// the piece (release-1.2.8 at 621721d6); the lists in HitRoll128Tests.GoldenFights and GoldenActions were
/// written by it there.
/// </summary>
internal static class HitRoll128Golden
{
    private const BindingFlags F = BindingFlags.NonPublic | BindingFlags.Instance;

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
        public long Damage;
    }

    private static string Sha(string s) => Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(s))).Substring(0, 16);

    // ---------------------------------------------------------------- characters and foes

    internal static int Weapon(int power, WeaponType type = WeaponType.Sword, WeaponHandedness hand = WeaponHandedness.OneHanded) =>
        EquipmentDatabase.RegisterDynamic(new Equipment
        {
            Name = $"Test {type} {power}", Slot = EquipmentSlot.MainHand, WeaponType = type, Handedness = hand,
            WeaponPower = power, MinLevel = 1, Rarity = EquipmentRarity.Common,
        });

    internal static Character Hero(long hp = 6000, long strength = 60, int level = 10, CharacterClass cls = CharacterClass.Warrior,
        long dexterity = 30, int? main = null, int? off = null, WeaponType mainType = WeaponType.Sword, string name = "Tester")
    {
        var c = new Character
        {
            Name1 = name, Name2 = name, AI = CharacterAI.Human, Class = cls, Race = CharacterRace.Human, Level = level,
            HP = hp, MaxHP = hp, BaseMaxHP = hp, Strength = strength, BaseStrength = strength, Defence = 20, BaseDefence = 20,
            Dexterity = dexterity, BaseDexterity = dexterity, Agility = 20, BaseAgility = 20, Constitution = 30, BaseConstitution = 30,
            Intelligence = 20, BaseIntelligence = 20, Wisdom = 20, BaseWisdom = 20, Charisma = 20, BaseCharisma = 20,
            Stamina = 100, BaseStamina = 100, Mental = 80, Gold = 0, Healing = 0, AutoHeal = false, CombatSpeed = CombatSpeed.Instant,
        };
        if (main.HasValue || off.HasValue)
        {
            if (main.HasValue)
                c.EquippedItems[EquipmentSlot.MainHand] = Weapon(main.Value, mainType,
                    mainType == WeaponType.Bow ? WeaponHandedness.TwoHanded : WeaponHandedness.OneHanded);
            if (off.HasValue) c.EquippedItems[EquipmentSlot.OffHand] = Weapon(off.Value);
            c.RecalculateStats();
            c.HP = c.MaxHP;
        }
        return c;
    }

    internal static Character Mate(string name, long hp = 3000, int level = 10, long strength = 40, long dexterity = 20) => new Character
    {
        Name1 = name, Name2 = name, AI = CharacterAI.Computer, Class = CharacterClass.Warrior, Race = CharacterRace.Human, Level = level,
        HP = hp, MaxHP = hp, BaseMaxHP = hp, Strength = strength, BaseStrength = strength, Defence = 10, BaseDefence = 10,
        Dexterity = dexterity, BaseDexterity = dexterity, Agility = 10, BaseAgility = 10, Constitution = 20, BaseConstitution = 20,
        Stamina = 50, BaseStamina = 50, Mental = 80, CombatSpeed = CombatSpeed.Instant,
    };

    /// <summary>A monster of a real tier name, with no abilities and nothing to say.</summary>
    internal static Monster Foe(string tier, long hp, long strength, int level = 8, int defence = 5) => new Monster
    {
        Name = tier, TierName = tier, Level = level, HP = hp, MaxHP = hp, Strength = strength, Defence = defence, Experience = 1, Gold = 0,
        IsActive = true, CanSpeak = false,
    };

    /// <summary>A group as the generator builds it for a caller with no encounter rules: the first seed from
    /// `from` that gives `size` members. Nobody speaks, so no phrase is drawn from the shared random.</summary>
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

    // ---------------------------------------------------------------- whole fights

    /// <summary>[A]ttack, then the target's number (or Enter for a random one) while more than one foe
    /// stands; [P]ass answers what a victory asks.</summary>
    internal static string Attacks(int n = 200, string target = "") => string.Concat(Enumerable.Repeat("A\n" + target + "\n", n)) + string.Concat(Enumerable.Repeat("P\n", 10));

    internal static async Task<Run> Fight(Character hero, List<Monster> foes, List<Character>? team = null, int seed = 128, string? script = null, Action<CombatEngine>? setup = null)
    {
        var run = new Run();
        _ = new GriefSystem();   // a fresh fallback instance: grief left live by another test would speak and draw in this fight
        var output = new MemoryStream();
        var term = new TerminalEmulator(new ScriptedStream(script ?? Attacks()), output);
        var engine = new CombatEngine(term);
        // the draws of the fight itself: once the last foe is dead, what follows (loot, rewards) also rolls
        // outside the engine with the shared random, so its draws are not the same from run to run
        var rng = new RecordingRandom(seed) { Over = () => foes.All(m => !m.IsAlive) };
        typeof(CombatEngine).GetField("random", F)!.SetValue(engine, rng);
        setup?.Invoke(engine);
        try
        {
            var task = engine.PlayerVsMonsters(hero, foes, team, offerMonkEncounter: false, isAmbush: false);
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

    internal static string Signature(Run run) =>
        $"{run.Draws.Count}:{Sha(string.Join("\n", run.Draws))}:{(run.Error != null ? "error " + run.Error.GetType().Name : run.Result!.Outcome + " in " + run.Result.CurrentRound)}";

    internal static BossCombatContext Boss() => new BossCombatContext { BossData = UsurperRemake.Data.OldGodsData.GetGodBossData(OldGodType.Maelketh) };

    /// <summary>The golden fights by name. Each is run fresh; the language is the caller's to set.</summary>
    internal static readonly (string Name, Func<Task<Run>> Fight)[] Fights =
    {
        ("solo, floor 7 pair", () => Fight(Hero(), Group(7, 2, 0))),
        ("solo, floor 20 trio, level 20 hero", () => Fight(Hero(20_000, 600, 20), Group(20, 3, 100), seed: 2020)),
        ("solo, dual wield, floor 12 pair", () => Fight(Hero(9000, 120, 12, CharacterClass.Assassin, main: 40, off: 35), Group(12, 2, 150), seed: 1212)),
        ("one teammate, floor 7 pair", () => Fight(Hero(), Group(7, 2, 50), new List<Character> { Mate("Ally One") }, seed: 77)),
        ("two teammates, floor 35 trio, level 35 hero", () => Fight(Hero(40_000, 2500, 35), Group(35, 3, 200),
            new List<Character> { Mate("Ally One", 15_000, 35, 400), Mate("Ally Two", 15_000, 35, 400) })),
        ("boss context, solo", () => Fight(Hero(60_000, 400, 28), new List<Monster> { Foe("Wolf", 30_000, 120, 28, 40) },
            script: Attacks(400), setup: e => e.BossContext = Boss())),
        ("boss context, one teammate", () => Fight(Hero(60_000, 400, 28), new List<Monster> { Foe("Wolf", 40_000, 120, 28, 40) },
            new List<Character> { Mate("Ally One", 30_000, 28, 300) }, seed: 2828, script: Attacks(400), setup: e => e.BossContext = Boss())),
    };

    // ---------------------------------------------------------------- single actions

    internal sealed class Rig
    {
        public CombatEngine Engine = null!;
        public MemoryStream Output = new();
        public Character Hero = null!;
        public CombatResult Result = null!;
        public List<Monster> Monsters = new();
        public Random Random = null!;
        public TerminalEmulator Terminal = null!;
        public string Shown()
        {
            Terminal.StreamWriterInternal?.Flush();
            return Encoding.UTF8.GetString(Output.ToArray());
        }
    }

    internal static Monster Dummy(int level = 10, int defence = 0, int armour = 0) => new Monster
    {
        Name = "Wolf", TierName = "Wolf", Level = level, HP = 1_000_000_000L, MaxHP = 1_000_000_000L, Strength = 1, Defence = defence,
        ArmPow = armour, Experience = 1, Gold = 0, IsActive = true, CanSpeak = false,
    };

    internal static Rig NewRig(Character hero, Random random, List<Character>? teammates = null, params Monster[] monsters)
    {
        _ = new GriefSystem();
        var rig = new Rig { Hero = hero, Random = random };
        rig.Terminal = new TerminalEmulator(new MemoryStream(), rig.Output);
        rig.Engine = new CombatEngine(rig.Terminal);
        var team = teammates ?? new List<Character>();
        typeof(CombatEngine).GetField("random", F)!.SetValue(rig.Engine, random);
        typeof(CombatEngine).GetField("_combatOwner", F)!.SetValue(rig.Engine, hero);
        typeof(CombatEngine).GetField("currentPlayer", F)!.SetValue(rig.Engine, hero);
        typeof(CombatEngine).GetField("currentTeammates", F)!.SetValue(rig.Engine, team);
        rig.Result = new CombatResult { Player = hero, Teammates = team, CombatLog = new List<string>(), CurrentRound = 2 };
        rig.Monsters = monsters.Length > 0 ? monsters.ToList() : new List<Monster> { Dummy() };
        return rig;
    }

    internal static void Act(Rig rig, CombatAction action, Character? actor = null)
    {
        var who = actor ?? rig.Hero;
        who.CurrentCombatStamina = 500;
        ((Dictionary<string, int>)typeof(CombatEngine).GetField("abilityCooldowns", F)!.GetValue(rig.Engine)!).Clear();
        ((Task)typeof(CombatEngine).GetMethod("ProcessPlayerActionMultiMonster", F)!
            .Invoke(rig.Engine, new object?[] { action, who, rig.Monsters, rig.Result })!).GetAwaiter().GetResult();
    }

    internal static void TeammateTurn(Rig rig, Character teammate)
    {
        ((Task)typeof(CombatEngine).GetMethod("ProcessTeammateActionMultiMonster", F)!
            .Invoke(rig.Engine, new object?[] { teammate, rig.Monsters, rig.Result })!).GetAwaiter().GetResult();
    }

    internal static CombatAction A(CombatActionType type, string ability = "") => new CombatAction { Type = type, AbilityId = ability, TargetIndex = 0 };

    /// <summary>The action `times` times on a seeded engine: the draws and the damage done.</summary>
    internal static Run Repeat(Character hero, CombatAction action, int times = 30, int seed = 128, Action<Rig, int>? before = null, Monster? foe = null)
    {
        var random = new RecordingRandom(seed);
        var rig = NewRig(hero, random, null, foe ?? Dummy());
        long start = rig.Monsters.Sum(m => m.HP);
        var run = new Run();
        try
        {
            for (int i = 0; i < times; i++)
            {
                before?.Invoke(rig, i);
                Act(rig, action);
            }
        }
        catch (Exception ex) { run.Error = ex.InnerException ?? ex; }
        run.Draws = random.Calls;
        run.Damage = start - rig.Monsters.Sum(m => m.HP);
        run.Transcript = rig.Shown();
        return run;
    }

    internal static Run TeammateRepeat(Character mate, int times = 30, int seed = 128)
    {
        var random = new RecordingRandom(seed);
        var hero = Hero();
        var rig = NewRig(hero, random, new List<Character> { mate }, Dummy());
        long start = rig.Monsters.Sum(m => m.HP);
        var run = new Run();
        try { for (int i = 0; i < times; i++) TeammateTurn(rig, mate); }
        catch (Exception ex) { run.Error = ex.InnerException ?? ex; }
        run.Draws = random.Calls;
        run.Damage = start - rig.Monsters.Sum(m => m.HP);
        run.Transcript = rig.Shown();
        return run;
    }

    internal static string ActionSignature(Run run) =>
        $"{run.Draws.Count}:{Sha(string.Join("\n", run.Draws))}:{(run.Error != null ? "error " + run.Error.GetType().Name : "damage " + run.Damage)}";

    private static Monster Evasive()
    {
        var m = Dummy();
        m.EvasionRounds = 1000;
        m.EvasionMissChance = 50;
        return m;
    }

    /// <summary>The golden actions by name: every path that the piece touches, and those it must leave alone.</summary>
    internal static readonly (string Name, Func<Run> Action)[] Actions =
    {
        ("basic swing", () => Repeat(Hero(), A(CombatActionType.Attack))),
        ("basic swing, dual wield", () => Repeat(Hero(cls: CharacterClass.Assassin, main: 40, off: 35), A(CombatActionType.Attack))),
        ("basic swing, evasive foe", () => Repeat(Hero(), A(CombatActionType.Attack), foe: Evasive())),
        ("basic swing, hidden", () => Repeat(Hero(), A(CombatActionType.Attack), before: (r, i) => r.Hero.ApplyStatus(StatusEffect.Hidden, 2))),
        ("power attack, dual wield", () => Repeat(Hero(cls: CharacterClass.Assassin, main: 40, off: 35), A(CombatActionType.PowerAttack))),
        ("precise strike", () => Repeat(Hero(), A(CombatActionType.PreciseStrike), foe: Dummy(10, 40))),
        // 1.3.2: each Smite spends a daily charge, so the hero has one before each of the thirty
        ("smite", () => Repeat(Hero(cls: CharacterClass.Paladin), A(CombatActionType.Smite), before: (r, i) => r.Hero.SmiteChargesRemaining = 1)),
        ("backstab", () => Repeat(Hero(cls: CharacterClass.Assassin), A(CombatActionType.Backstab))),
        ("ranged attack", () => Repeat(Hero(cls: CharacterClass.Ranger, main: 30, mainType: WeaponType.Bow), A(CombatActionType.RangedAttack))),
        ("soul strike", () => Repeat(Hero(cls: CharacterClass.Paladin), A(CombatActionType.SoulStrike))),
        ("ability, power strike", () => Repeat(Hero(), A(CombatActionType.UseAbility, "power_strike"))),
        ("ability, execute, dual wield", () => Repeat(Hero(level: 30, cls: CharacterClass.Assassin, main: 40, off: 35), A(CombatActionType.UseAbility, "execute"))),
        ("ability, whirlwind (area)", () => Repeat(Hero(level: 60), A(CombatActionType.UseAbility, "whirlwind"))),
        ("teammate swings", () => TeammateRepeat(Mate("Ally One"))),
    };
}
