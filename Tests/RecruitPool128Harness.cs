using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Runtime.Loader;
using System.Security.Cryptography;
using System.Text;
using UsurperRemake.BBS;
using UsurperRemake.Systems;

namespace UsurperReborn.Tests;

/// <summary>
/// v1.2.8 (H2): a headless, seeded world for the recruit pool tests. It builds the classic roster, seeds every
/// random source the world simulator reads on this thread, and runs WorldSimulator.SimulateStep tick by tick.
/// It names nothing that the base commit lacks (the switch and the clock are reached by reflection), so the
/// same file produces the golden of row 8 on the base commit.
/// </summary>
internal sealed class RecruitPoolWorld : IDisposable
{
    private const BindingFlags F = BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public;
    private const BindingFlags SF = BindingFlags.Static | BindingFlags.NonPublic | BindingFlags.Public;

    /// <summary>A seeded random that counts its draws and folds every drawn value into a running hash.</summary>
    internal sealed class CountingRandom : Random
    {
        public long Draws;
        public ulong Hash = 1469598103934665603UL;
        public CountingRandom(int seed) : base(seed) { }
        private void Fold(long v) { Draws++; unchecked { Hash = (Hash ^ (ulong)v) * 1099511628211UL; } }
        public override int Next() { int v = base.Next(); Fold(v); return v; }
        public override int Next(int maxValue) { int v = base.Next(maxValue); Fold(v); return v; }
        public override int Next(int minValue, int maxValue) { int v = base.Next(minValue, maxValue); Fold(v); return v; }
        public override double NextDouble() { double v = base.NextDouble(); Fold(BitConverter.DoubleToInt64Bits(v)); return v; }
        public override float NextSingle() { float v = base.NextSingle(); Fold(BitConverter.SingleToInt32Bits(v)); return v; }
        public override long NextInt64() { long v = base.NextInt64(); Fold(v); return v; }
        public override long NextInt64(long maxValue) { long v = base.NextInt64(maxValue); Fold(v); return v; }
        public override long NextInt64(long minValue, long maxValue) { long v = base.NextInt64(minValue, maxValue); Fold(v); return v; }
    }

    public readonly WorldSimulator Sim;
    public readonly CountingRandom SimRandom;
    public readonly CountingRandom SpawnRandom;
    public List<NPC> Npcs => NPCSpawnSystem.Instance.ActiveNPCs;

    private readonly List<NPC> _rosterBefore;
    private readonly object? _simRandomBefore;
    private readonly object? _spawnRandomBefore;
    private readonly bool _onlineBefore;
    private readonly float _xpBefore;
    private readonly int _tickBefore;
    private readonly WorldSimulator? _instanceBefore;
    private static readonly FieldInfo OnlineField = typeof(DoorMode).GetField("_onlineMode", SF)!;
    private static readonly FieldInfo TickField = typeof(WorldSimulator).GetField("_currentTick", SF)!;
    private static readonly FieldInfo InstanceField = typeof(WorldSimulator).GetField("_instance", SF)!;

    /// <summary>
    /// Seeds Random.Shared for the calling thread: the shared generator keeps one xoshiro state per thread, and
    /// its four state words are set here. Other threads (other test collections) are untouched.
    /// </summary>
    public static void SeedSharedRandomOnThisThread(int seed)
    {
        Random.Shared.Next();   // makes the thread's state exist
        var tsr = typeof(Random).GetNestedType("ThreadSafeRandom", BindingFlags.NonPublic)!;
        var local = tsr.GetField("t_random", BindingFlags.Static | BindingFlags.NonPublic)!.GetValue(null)!;
        var t = local.GetType();
        ulong s = (ulong)seed * 0x9E3779B97F4A7C15UL + 0x1234567UL;
        foreach (var name in new[] { "_s0", "_s1", "_s2", "_s3" })
        {
            unchecked { s ^= s >> 30; s *= 0xBF58476D1CE4E5B9UL; s ^= s >> 27; s *= 0x94D049BB133111EBUL; s ^= s >> 31; }
            t.GetField(name, F)!.SetValue(local, s | 1UL);
        }
    }

    // ---- every other random object of the game: seeded in place ----
    private static List<FieldInfo>? _randomHolders;
    private readonly HashSet<object> _seeded = new(ReferenceEqualityComparer.Instance);
    private int _seed;

    /// <summary>Seeds every other random object of the game again (a second run of the same scenario).</summary>
    public void ReseedOtherRandoms(int seed)
    {
        _seed = seed;
        _seeded.RemoveWhere(o => o is not NPC);
        SeedOtherRandoms();
    }

    private static ulong Mix(ulong s)
    {
        unchecked { s ^= s >> 30; s *= 0xBF58476D1CE4E5B9UL; s ^= s >> 27; s *= 0x94D049BB133111EBUL; s ^= s >> 31; }
        return s;
    }

    private static ulong StableHash(string text)
    {
        ulong h = 1469598103934665603UL;
        foreach (char c in text) unchecked { h = (h ^ c) * 1099511628211UL; }
        return h;
    }

    /// <summary>Sets the xoshiro state of an unseeded Random; false for the shared one or a seeded one.</summary>
    private static bool SeedInPlace(Random r, ulong seed)
    {
        var impl = typeof(Random).GetField("_impl", F)?.GetValue(r);
        if (impl == null || impl.GetType().Name != "XoshiroImpl") return false;
        ulong s = seed;
        foreach (var name in new[] { "_s0", "_s1", "_s2", "_s3" })
        {
            s = Mix(s + 0x9E3779B97F4A7C15UL);
            impl.GetType().GetField(name, F)!.SetValue(impl, s | 1UL);
        }
        return true;
    }

    /// <summary>
    /// Static fields that are a Random, or that hold a game object with Random fields of its own (the
    /// singletons). The list is built once; the objects are seeded when first seen, each from its field name.
    /// </summary>
    private void SeedOtherRandoms()
    {
        var asm = typeof(WorldSimulator).Assembly;
        if (_randomHolders == null)
        {
            Type[] types;
            try { types = asm.GetTypes(); } catch (ReflectionTypeLoadException e) { types = e.Types.Where(t => t != null).ToArray()!; }
            types = types.Where(t => !t.ContainsGenericParameters).ToArray();
            var withRandom = new HashSet<Type>(types.Where(t => t.IsClass &&
                t.GetFields(F | BindingFlags.DeclaredOnly).Any(f => f.FieldType == typeof(Random))));
            // a singleton born in the middle of a tick would draw unseeded until the next sweep: make them now
            foreach (var t in withRandom)
                try { t.GetProperty("Instance", SF)?.GetValue(null); } catch { }
            _randomHolders = types.SelectMany(t => t.GetFields(SF | BindingFlags.DeclaredOnly))
                .Where(f => !f.IsLiteral && (f.FieldType == typeof(Random) || withRandom.Contains(f.FieldType)))
                .ToList();
        }
        foreach (var f in _randomHolders)
        {
            object? v;
            try { v = f.GetValue(null); } catch { continue; }
            if (v == null || !_seeded.Add(v)) continue;
            string key = f.DeclaringType!.FullName + "." + f.Name;
            if (v is Random r) { SeedInPlace(r, StableHash(key) ^ (ulong)_seed); continue; }
            foreach (var inner in v.GetType().GetFields(F).Where(x => x.FieldType == typeof(Random)))
                if (inner.GetValue(v) is Random ir && _seeded.Add(ir))
                    SeedInPlace(ir, StableHash(key + "." + inner.Name) ^ (ulong)_seed);
        }
    }

    public RecruitPoolWorld(int seed, bool online = true, float npcXp = 5.0f, bool classic = true)
    {
        _seed = seed;
        _rosterBefore = NPCSpawnSystem.Instance.ActiveNPCs.ToList();
        _onlineBefore = DoorMode.IsOnlineMode;
        _xpBefore = WorldSimulator.NpcXpMultiplier;
        _tickBefore = (int)TickField.GetValue(null)!;
        _instanceBefore = WorldSimulator.Instance;
        _spawnRandomBefore = typeof(NPCSpawnSystem).GetField("random", F)!.GetValue(NPCSpawnSystem.Instance);

        OnlineField.SetValue(null, online);
        WorldSimulator.NpcXpMultiplier = npcXp;
        TickField.SetValue(null, 0);
        SeedSharedRandomOnThisThread(seed);
        SeedOtherRandoms();
        NPCNameRegistry.Reset();
        SpawnRandom = new CountingRandom(seed + 1);
        typeof(NPCSpawnSystem).GetField("random", F)!.SetValue(NPCSpawnSystem.Instance, SpawnRandom);
        if (classic) NPCSpawnSystem.Instance.ForceReinitializeNPCs().GetAwaiter().GetResult();
        else NPCSpawnSystem.Instance.ActiveNPCs.Clear();

        Sim = new WorldSimulator();
        _simRandomBefore = typeof(WorldSimulator).GetField("random", F)!.GetValue(Sim);
        SimRandom = new CountingRandom(seed);
        typeof(WorldSimulator).GetField("random", F)!.SetValue(Sim, SimRandom);
        Sim.SetActive(true);
        StableIds();
    }

    /// <summary>NPC.Id is a fresh Guid; pair keys compare it, so the harness gives each NPC an id made from its name.</summary>
    private void StableIds()
    {
        foreach (var n in Npcs)
            if (_seeded.Add(n)) n.Id = "h2-" + n.Name2;
    }

    public void Step() { SeedOtherRandoms(); StableIds(); Sim.SimulateStep(); StableIds(); }

    public int Tick { get => (int)TickField.GetValue(null)!; set => TickField.SetValue(null, value); }

    /// <summary>Calls a private method of the world simulator by name.</summary>
    public object? Call(string method, params object[] args) =>
        typeof(WorldSimulator).GetMethod(method, F)!.Invoke(Sim, args);

    /// <summary>
    /// Lets real time pass for the world without waiting: every date the lifecycle reads against the clock
    /// (birth dates of NPCs, children and orphans, pregnancy due dates, death dates, the player team stamp)
    /// is set back by the span, which is what the clock moving forward by it looks like to the simulator.
    /// </summary>
    public void Elapse(TimeSpan span)
    {
        var stamp = typeof(NPC).GetProperty("LastOnPlayerTeamAt", F);
        foreach (var n in Npcs)
        {
            if (n.BirthDate > DateTime.MinValue.AddYears(1)) n.BirthDate -= span;
            if (n.PregnancyDueDate is DateTime due) n.PregnancyDueDate = due - span;
            if (n.DeathDate is DateTime died) n.DeathDate = died - span;
            if (stamp?.GetValue(n) is DateTime seen) stamp.SetValue(n, seen - span);
        }
        foreach (var c in FamilySystem.Instance.AllChildren)
            if (c.BirthDate > DateTime.MinValue.AddYears(1)) c.BirthDate -= span;
        var king = CastleLocation.GetCurrentKing();
        if (king?.Orphans != null)
            foreach (var o in king.Orphans)
                if (o.BirthDate > DateTime.MinValue.AddYears(1)) o.BirthDate -= span;
    }

    public List<NPC> Alive() => Npcs.Where(n => n.IsAlive && !n.IsDead && !n.IsAgedDeath && !n.IsPermaDead).ToList();

    /// <summary>The NPC list as one line per NPC, sorted by name (no wall-clock field, no generated id).</summary>
    public string Roster()
    {
        var sb = new StringBuilder();
        foreach (var n in Npcs.OrderBy(n => n.Name2, StringComparer.Ordinal))
            sb.Append(n.Name2).Append('|').Append(n.Race).Append('|').Append(n.Class).Append('|').Append(n.Sex).Append('|')
              .Append(n.Level).Append('|').Append(n.Experience).Append('|').Append(n.Team).Append('|')
              .Append(n.IsDead ? 'D' : '.').Append(n.IsAgedDeath ? 'A' : '.').Append(n.IsPermaDead ? 'P' : '.').Append('|')
              .Append(n.CurrentLocation).Append('|').Append(n.Gold).Append('|').Append(n.HP).Append('/').Append(n.MaxHP)
              .Append('\n');
        return sb.ToString();
    }

    public static string Sha16(string s) =>
        Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(s))).Substring(0, 16).ToLowerInvariant();

    /// <summary>The first state word of this thread's shared generator (it moves with every shared draw).</summary>
    public static ulong SharedState()
    {
        var tsr = typeof(Random).GetNestedType("ThreadSafeRandom", BindingFlags.NonPublic)!;
        var local = tsr.GetField("t_random", BindingFlags.Static | BindingFlags.NonPublic)!.GetValue(null)!;
        return (ulong)local.GetType().GetField("_s0", F)!.GetValue(local)!;
    }

    public string Digest() => $"shared={SharedState():x16} " + $"npcs={Npcs.Count} alive={Alive().Count} roster={Sha16(Roster())} " +
        $"simDraws={SimRandom.Draws} simHash={SimRandom.Hash:x16} spawnDraws={SpawnRandom.Draws} spawnHash={SpawnRandom.Hash:x16}";

    public void Dispose()
    {
        Sim.SetActive(false);
        typeof(NPCSpawnSystem).GetField("random", F)!.SetValue(NPCSpawnSystem.Instance, _spawnRandomBefore);
        NPCSpawnSystem.Instance.ActiveNPCs.Clear();
        NPCSpawnSystem.Instance.ActiveNPCs.AddRange(_rosterBefore);
        OnlineField.SetValue(null, _onlineBefore);
        WorldSimulator.NpcXpMultiplier = _xpBefore;
        TickField.SetValue(null, _tickBefore);
        InstanceField.SetValue(null, _instanceBefore);
    }
}

/// <summary>
/// v1.2.8 (H2): runs a world in a copy of the game assembly of its own (a separate load context), so the
/// singletons and static tables of the run are fresh and nothing leaks to or from the other tests. The entry
/// methods of RecruitPoolRuns are called by name and take and return text.
/// </summary>
internal static class IsolatedGame
{
    private sealed class Context : AssemblyLoadContext
    {
        private readonly Dictionary<string, string> _own;
        public Context(Dictionary<string, string> own) : base("h2-world") { _own = own; }
        protected override Assembly? Load(AssemblyName name) =>
            name.Name != null && _own.TryGetValue(name.Name, out var path) ? LoadFromAssemblyPath(path) : null;
    }

    public static string Run(string method, string arg)
    {
        var game = typeof(WorldSimulator).Assembly;
        var tests = typeof(IsolatedGame).Assembly;
        var ctx = new Context(new Dictionary<string, string>
        {
            [game.GetName().Name!] = game.Location,
            [tests.GetName().Name!] = tests.Location,
        });
        var runs = ctx.LoadFromAssemblyPath(tests.Location).GetType("UsurperReborn.Tests.RecruitPoolRuns")!;
        try
        {
            return (string)runs.GetMethod(method, BindingFlags.Static | BindingFlags.NonPublic | BindingFlags.Public)!
                .Invoke(null, new object[] { arg })!;
        }
        catch (TargetInvocationException e) when (e.InnerException != null)
        {
            System.Runtime.ExceptionServices.ExceptionDispatchInfo.Capture(e.InnerException).Throw();
            throw;
        }
    }
}

/// <summary>The world runs of the recruit pool tests; each runs inside IsolatedGame.</summary>
internal static partial class RecruitPoolRuns
{
    /// <summary>Sets GameConfig.RecruitPoolEnabled when the commit has it (the base commit does not).</summary>
    internal static void SetSwitch(bool on) =>
        typeof(GameConfig).GetProperty("RecruitPoolEnabled", BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic)
            ?.SetValue(null, on);

    /// <summary>News goes to a list of no capacity: no file is written by a run.</summary>
    internal static void SilenceNews() => NewsSystem.Instance.SetCatchUpBuffer(new List<string>(), 0);

    /// <summary>
    /// The part of the day the world simulator reads from the clock (online: the real hour): the activity
    /// weights change at 06, 12, 18 and 23 (ApplyTimeOfDayWeights), and the Inn weight at 18 and 06.
    /// </summary>
    internal static string DayPart(int hour) =>
        hour >= 6 && hour < 12 ? "morning" : hour >= 12 && hour < 18 ? "afternoon" : hour >= 18 && hour < 23 ? "evening" : "night";

    /// <summary>
    /// Row 8: "seed,ticks" with the switch off; returns the part of the day the run fell in ("straddle" when
    /// it crossed a boundary) and the digest of the run.
    /// </summary>
    public static string Golden(string arg)
    {
        var a = arg.Split(',');
        int seed = int.Parse(a[0]), ticks = int.Parse(a[1]);
        bool on = a.Length > 2 && a[2] == "on";
        SetSwitch(on);
        SilenceNews();
        int h0 = DateTime.Now.Hour;
        using var w = new RecruitPoolWorld(seed);
        for (int i = 0; i < ticks; i++) w.Step();
        int h1 = DateTime.Now.Hour;
        string mark = DayPart(h0) != DayPart(h1) ? "straddle" : DayPart(h0);
        return $"{mark} {w.Digest()}";
    }
}
