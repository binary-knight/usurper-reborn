using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using UsurperRemake;
using UsurperRemake.Systems;

namespace UsurperReborn.Tests;

/// <summary>1.3.3 party survival: the shared seams of the PartySurvival133 test classes. An engine on a
/// scripted terminal with a seeded Random, the combat owner and teammates set as a fight sets them, and
/// fresh companion and grief singletons.</summary>
internal static class PartySurvival133Harness
{
    private const BindingFlags NPI = BindingFlags.NonPublic | BindingFlags.Instance;
    private const BindingFlags SNP = BindingFlags.NonPublic | BindingFlags.Static;

    /// <summary>Serves the scripted bytes once, then reports EOF.</summary>
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

    internal sealed class Rig
    {
        public CombatEngine Engine = null!;
        public MemoryStream Output = new();
        public TerminalEmulator Term = null!;
        public Character Owner = null!;
        public List<Character> Team = new();
        public CombatResult Result = null!;
        public string Text => Encoding.UTF8.GetString(Output.ToArray());
    }

    internal static Character Hero(long hp = 600, long potions = 5) => new Character
    {
        Name1 = "Tester", Name2 = "Tester", AI = CharacterAI.Human,
        Class = CharacterClass.Warrior, Race = CharacterRace.Human, Level = 10,
        HP = hp, MaxHP = hp, BaseMaxHP = hp,
        Strength = 60, BaseStrength = 60, Defence = 20, BaseDefence = 20,
        Dexterity = 30, BaseDexterity = 30, Agility = 20, BaseAgility = 20,
        Constitution = 30, BaseConstitution = 30, Intelligence = 20, BaseIntelligence = 20,
        Wisdom = 20, BaseWisdom = 20, Stamina = 100, BaseStamina = 100,
        Mental = 80, Gold = 0, Healing = potions, AutoHeal = false,
        CombatSpeed = CombatSpeed.Instant,
    };

    /// <summary>An NPC ally (the dispatch's else branch): in scope.</summary>
    internal static Character Ally(string name = "Mira", long hp = 100, long maxHp = 100) => new Character
    {
        Name1 = name, Name2 = name, AI = CharacterAI.Computer,
        Class = CharacterClass.Warrior, Race = CharacterRace.Human, Level = 5,
        HP = hp, MaxHP = maxHp, BaseMaxHP = maxHp,
        Strength = 30, BaseStrength = 30, Defence = 10, BaseDefence = 10,
        Dexterity = 20, BaseDexterity = 20, Agility = 10, BaseAgility = 10,
        Constitution = 20, BaseConstitution = 20, Stamina = 50, BaseStamina = 50,
        Mental = 80, CombatSpeed = CombatSpeed.Instant,
    };

    internal static Monster Mob(string name = "Wolf", string family = "Beast", bool boss = false, long hp = 400, long str = 40) => new Monster
    {
        Name = name, Level = 8, HP = hp, MaxHP = hp, Strength = str, Defence = 5, Experience = 1, Gold = 0,
        IsActive = true, FamilyName = family, IsBoss = boss,
    };

    internal static Rig NewRig(Character? owner = null, IEnumerable<Character>? team = null, IEnumerable<Monster>? monsters = null,
        string script = "", int seed = 133)
    {
        var r = new Rig { Owner = owner ?? Hero() };
        r.Team = team?.ToList() ?? new List<Character>();
        r.Term = new TerminalEmulator(new ScriptedStream(script), r.Output);
        r.Engine = new CombatEngine(r.Term);
        r.Engine.SeedRandomForTests(seed);
        typeof(CombatEngine).GetField("_combatOwner", NPI)!.SetValue(r.Engine, r.Owner);
        typeof(CombatEngine).GetField("currentPlayer", NPI)!.SetValue(r.Engine, r.Owner);
        typeof(CombatEngine).GetField("currentTeammates", NPI)!.SetValue(r.Engine, r.Team);
        r.Result = new CombatResult
        {
            Player = r.Owner, Teammates = r.Team, CombatLog = new List<string>(),
            Monsters = monsters?.ToList() ?? new List<Monster>(), CurrentRound = 1,
        };
        return r;
    }

    /// <summary>Starts the event accumulator as a fight start does (BeginFightEvents).</summary>
    internal static void BeginEvents(Rig r) =>
        typeof(CombatEngine).GetMethod("BeginFightEvents", NPI)!.Invoke(r.Engine, new object[] { r.Result });

    internal static object? Call(CombatEngine e, string method, params object?[] args)
    {
        var m = typeof(CombatEngine).GetMethod(method, NPI)!;
        try { return m.Invoke(e, args); }
        catch (TargetInvocationException ex) when (ex.InnerException != null) { throw ex.InnerException; }
    }

    internal static async Task CallAsync(CombatEngine e, string method, params object?[] args)
    {
        var t = (Task)Call(e, method, args)!;
        await t;
    }

    internal static Task Down(Rig r, Character ally, Monster? killer = null)
    {
        ally.HP = 0;
        return r.Engine.EnterDownedOrDie(ally, killer?.Name ?? "Wolf", killer, r.Result);
    }

    /// <summary>Fresh companion and grief singletons, with one recruited companion in the active party;
    /// returns its combat wrapper. The death scene's pause is off.</summary>
    internal static Character FreshCompanion(CompanionId id = CompanionId.Aldric, int loyalty = 50)
    {
        typeof(CompanionSystem).GetField("_fallbackInstance", SNP)!.SetValue(null, new CompanionSystem());
        _ = new GriefSystem();   // becomes the fallback
        var c = CompanionSystem.Instance.GetCompanion(id)!;
        c.IsRecruited = true;
        c.IsDead = false;
        c.LoyaltyLevel = loyalty;
        CompanionSystem.Instance.ActivateCompanion(id);
        GameConfig.MomentOfSilenceEnabled = false;
        return CompanionSystem.Instance.GetCompanionsAsCharacters().Single(w => w.CompanionId == id);
    }

    /// <summary>Puts back the pause and fresh companion and grief singletons, so no companion death or grief
    /// leaks into another class of the shared collection.</summary>
    /// <summary>Every grief begun, live or not (IsGrieving leaves out a grief for a companion still alive).</summary>
    internal static int GriefsBegun()
    {
        var g = GriefSystem.Instance;
        int n = 0;
        foreach (var name in new[] { "activeGrief", "activeNpcGrief" })
            n += ((System.Collections.IDictionary)typeof(GriefSystem).GetField(name, NPI)!.GetValue(g)!).Count;
        return n;
    }

    internal static void RestoreSilence()
    {
        GameConfig.MomentOfSilenceEnabled = true;
        typeof(CompanionSystem).GetField("_fallbackInstance", SNP)!.SetValue(null, new CompanionSystem());
        _ = new GriefSystem();
        GriefSystem.Instance.Reset();
    }

    /// <summary>Enough Enter presses for a companion's death scene.</summary>
    internal const string Enters = "\n\n\n\n\n\n\n\n\n\n\n\n\n\n\n\n\n\n\n\n";
}
