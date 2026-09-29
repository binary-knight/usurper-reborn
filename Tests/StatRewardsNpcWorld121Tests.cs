using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using FluentAssertions;
using UsurperRemake;
using UsurperRemake.Systems;
using Xunit;

namespace UsurperReborn.Tests;

/// <summary>
/// 1.2.1 stat rewards piece 5: an NPC's world gains (pre-history training and level-ups, a Temple
/// blessing, prison activities) go through GrantPermanentStat, so they survive the NPC's next
/// RecalculateStats and a world save. A street-fight rage buff raises only the fight's Monster; the
/// world NPC keeps its Strength.
/// </summary>
[Collection("SharedGameSingletons")]
public class StatRewardsNpcWorld121Tests : IDisposable
{
    private const BindingFlags F = BindingFlags.NonPublic | BindingFlags.Instance;

    private static readonly JsonSerializerOptions Json = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        IncludeFields = true,
    };

    private readonly List<NPC> _rosterBefore;

    public StatRewardsNpcWorld121Tests()
    {
        _rosterBefore = NPCSpawnSystem.Instance.ActiveNPCs.ToList();
        NPCSpawnSystem.Instance.ActiveNPCs.Clear();
    }

    public void Dispose()
    {
        var roster = NPCSpawnSystem.Instance.ActiveNPCs;
        roster.Clear();
        roster.AddRange(_rosterBefore);
    }

    /// <summary>An NPC whose Base fields match its stats, as a spawned or restored NPC has them.</summary>
    private static NPC Npc(string name, int level = 10)
    {
        var npc = new NPC
        {
            ID = "id_" + name, Id = "id_" + name, Name1 = name, Name2 = name, Level = level,
            HP = 200, MaxHP = 200, Strength = 40, Defence = 20, Agility = 15, Dexterity = 15,
            Intelligence = 12, Wisdom = 12, Charisma = 11, Stamina = 14, Constitution = 13, Class = CharacterClass.Warrior
        };
        npc.InitializeBaseStats();
        npc.EnsureSystemsInitialized();
        npc.RecalculateStats();
        npc.HP = npc.MaxHP;
        return npc;
    }

    /// <summary>NextDouble returns <c>d</c>; Next(max) returns the queued values (then 0); Next(min, max) returns min.</summary>
    private sealed class SeqRandom : Random
    {
        private readonly double _d;
        private readonly Queue<int> _next;
        public SeqRandom(double d, params int[] next) { _d = d; _next = new Queue<int>(next); }
        public override double NextDouble() => _d;
        public override int Next(int maxValue) => _next.Count > 0 ? _next.Dequeue() : 0;
        public override int Next(int minValue, int maxValue) => minValue;
    }

    /// <summary>Next returns its top value, so every percent roll fails.</summary>
    private sealed class HighRandom : Random
    {
        public override double NextDouble() => 0.99;
        public override int Next(int maxValue) => Math.Max(0, maxValue - 1);
        public override int Next(int minValue, int maxValue) => Math.Max(minValue, maxValue - 1);
    }

    private static void Invoke(object target, string method, params object[] args)
    {
        var m = target.GetType().GetMethod(method, F);
        m.Should().NotBeNull($"{method} must exist");
        try { m!.Invoke(target, args); }
        catch (TargetInvocationException ex) when (ex.InnerException != null) { throw ex.InnerException; }
    }

    private static void Recalc(NPC npc) => npc.RecalculateStats();

    // ---------------- world creation (pre-history) ----------------

    private static WorldInitializerSystem World(Random r)
    {
        var w = (WorldInitializerSystem)Activator.CreateInstance(typeof(WorldInitializerSystem), nonPublic: true)!;
        typeof(WorldInitializerSystem).GetField("random", F)!.SetValue(w, r);
        return w;
    }

    [Theory]
    [InlineData(0, StatKind.Strength, 1)]
    [InlineData(1, StatKind.Defence, 1)]
    [InlineData(2, StatKind.Agility, 1)]
    [InlineData(3, StatKind.MaxHP, 5)]
    public void PreHistoryTraining_Gain_SurvivesRecalculateStats(int choice, StatKind stat, int gain)
    {
        var npc = Npc("P5Train" + choice);
        long baseBefore = npc.GetBaseStat(stat);
        long before = StatRewards1115Tests.Derived(npc, stat);

        Invoke(World(new SeqRandom(0.0, choice)), "SimulateNPCTraining", npc);

        npc.GetBaseStat(stat).Should().Be(baseBefore + gain, "the training gain is written to the Base field");
        StatRewards1115Tests.Derived(npc, stat).Should().Be(before + gain);
        Recalc(npc);
        StatRewards1115Tests.Derived(npc, stat).Should().Be(before + gain, "the training gain survives the NPC's next recalc");
    }

    [Fact]
    public void PreHistoryTraining_MaxHP_RaisesHP_AsBefore()
    {
        var npc = Npc("P5TrainHp");
        npc.HP = 100;
        long maxBefore = npc.MaxHP;
        Invoke(World(new SeqRandom(0.0, 3)), "SimulateNPCTraining", npc);
        npc.MaxHP.Should().Be(maxBefore + 5);
        npc.HP.Should().Be(105, "HP rises by the same 5, clamped to the new maximum");

        var full = Npc("P5TrainHpFull");
        Invoke(World(new SeqRandom(0.0, 3)), "SimulateNPCTraining", full);
        full.HP.Should().Be(full.MaxHP);
    }

    [Fact]
    public void PreHistoryLevelUp_Gains_SurviveRecalculateStats()
    {
        var npc = Npc("P5LevelUp");
        npc.Experience = GameConfig.GetExperienceForLevel(npc.Level + 1);
        npc.HP = 50;
        long maxBefore = npc.MaxHP, baseMaxBefore = npc.BaseMaxHP, strBefore = npc.Strength, defBefore = npc.Defence;

        Invoke(World(new SeqRandom(0.0)), "CheckNPCLevelUp", npc, 70);

        // SeqRandom gives each range its low end: MaxHP +15, Strength +1, Defence +1
        npc.Level.Should().Be(11);
        npc.BaseMaxHP.Should().Be(baseMaxBefore + 15);
        npc.BaseStrength.Should().Be(41);
        npc.BaseDefence.Should().Be(21);
        npc.Strength.Should().Be(strBefore + 1);
        npc.Defence.Should().Be(defBefore + 1);
        npc.HP.Should().Be(npc.MaxHP, "a level-up heals to full, as before");
        Recalc(npc);
        npc.MaxHP.Should().Be(maxBefore + 15);
        npc.Strength.Should().Be(strBefore + 1);
        npc.Defence.Should().Be(defBefore + 1, "the level-up gains survive the NPC's next recalc");
    }

    [Fact]
    public void AReplacementNpc_HasBaseFields_SoARecalcKeepsItsStats()
    {
        var npc = (NPC)typeof(WorldInitializerSystem).GetMethod("CreateReplacementNPC", F)!.Invoke(World(new Random(121)), null)!;
        long str = npc.Strength, def = npc.Defence, maxHp = npc.MaxHP;
        str.Should().BeGreaterThan(0);
        npc.BaseStrength.Should().Be(str);
        npc.BaseDefence.Should().Be(def);
        npc.BaseMaxHP.Should().Be(maxHp);
        Recalc(npc);
        npc.Strength.Should().Be(str, "a recalc rebuilds Strength from the Base field, not from 0");
    }

    // ---------------- Temple blessing (world sim) ----------------

    [Theory]
    [InlineData(0, StatKind.Strength)]
    [InlineData(1, StatKind.Wisdom)]
    public async Task ATempleBlessing_SurvivesAWorldSaveAndRestore(int which, StatKind stat)
    {
        var npc = Npc("P5Temple" + which);
        NPCSpawnSystem.Instance.AddRestoredNPC(npc);
        long baseBefore = npc.GetBaseStat(stat);

        var sim = new WorldSimulator();
        typeof(WorldSimulator).GetField("random", F)!.SetValue(sim, new SeqRandom(0.0, which));
        Invoke(sim, "NPCVisitTemple", npc);

        npc.GetBaseStat(stat).Should().Be(baseBefore + 1, "the blessing is written to the Base field");
        long blessed = StatRewards1115Tests.Derived(npc, stat);
        blessed.Should().Be(baseBefore + 1);

        var stored = JsonSerializer.Deserialize<List<NPCData>>(JsonSerializer.Serialize(OnlineStateManager.SerializeCurrentNPCs(), Json), Json)!;
        NPCSpawnSystem.Instance.ActiveNPCs.Clear();
        await GameEngine.Instance.RestoreNPCs(stored);
        var back = NPCSpawnSystem.Instance.ActiveNPCs.Single(n => n.Name2 == npc.Name2);
        back.RecalculateStats();
        back.GetBaseStat(stat).Should().Be(baseBefore + 1);
        StatRewards1115Tests.Derived(back, stat).Should().Be(blessed, "the blessing survives a world save, a restore and a recalc");
    }

    // ---------------- prison activities ----------------

    [Theory]
    [InlineData(0, StatKind.Strength, 1)]      // Pushups
    [InlineData(1, StatKind.Dexterity, 1)]     // Yoga
    [InlineData(2, StatKind.Intelligence, 1)]  // Reading
    [InlineData(3, StatKind.Wisdom, 1)]        // Meditation
    [InlineData(4, StatKind.Defence, 1)]       // Shadow boxing
    [InlineData(5, StatKind.MaxHP, 3)]         // Stretching
    [InlineData(6, StatKind.Charisma, 1)]      // Planning
    public void APrisonActivityGain_SurvivesRecalculateStats(int activity, StatKind stat, int gain)
    {
        var npc = Npc("P5Prison" + activity);
        npc.DaysInPrison = 3;
        long baseBefore = npc.GetBaseStat(stat);
        long before = StatRewards1115Tests.Derived(npc, stat);

        var prison = (PrisonActivitySystem)Activator.CreateInstance(typeof(PrisonActivitySystem), nonPublic: true)!;
        typeof(PrisonActivitySystem).GetField("random", F)!.SetValue(prison, new SeqRandom(0.0, activity));
        prison.ProcessNPCPrisonerActivity(npc);

        npc.GetBaseStat(stat).Should().Be(baseBefore + gain, "the prison gain is written to the Base field");
        Recalc(npc);
        StatRewards1115Tests.Derived(npc, stat).Should().Be(before + gain, "the prison gain survives the NPC's next recalc");
    }

    [Fact]
    public void PrisonMeditation_StillHeals_AndStretchingLeavesHP()
    {
        var npc = Npc("P5PrisonHp");
        npc.DaysInPrison = 3;
        npc.HP = 100;
        long maxBefore = npc.MaxHP;
        var prison = (PrisonActivitySystem)Activator.CreateInstance(typeof(PrisonActivitySystem), nonPublic: true)!;
        typeof(PrisonActivitySystem).GetField("random", F)!.SetValue(prison, new SeqRandom(0.0, 3, 5));
        prison.ProcessNPCPrisonerActivity(npc);
        npc.HP.Should().Be(100 + maxBefore / 10, "meditation heals a tenth of MaxHP");
        prison.ProcessNPCPrisonerActivity(npc);
        npc.MaxHP.Should().Be(maxBefore + 3);
        npc.HP.Should().Be(100 + maxBefore / 10, "stretching raises MaxHP only, as before");
    }

    // ---------------- street-fight rage ----------------

    private static TerminalEmulator Term(string script) =>
        new TerminalEmulator(new ScriptedStream(script + string.Concat(Enumerable.Repeat("R\n", 12)) + string.Concat(Enumerable.Repeat("\n", 40))), new MemoryStream());

    private static Character Player(string name)
    {
        var p = StatRewards1115Tests.Fresh(name);
        p.Level = 10;
        p.SmokeBombs = 5;
        p.CombatSpeed = CombatSpeed.Instant;
        p.Gold = 0;
        return p;
    }

    private static StreetEncounterSystem Street(Random r)
    {
        var street = (StreetEncounterSystem)Activator.CreateInstance(typeof(StreetEncounterSystem), nonPublic: true)!;
        typeof(StreetEncounterSystem).GetField("_random", F)!.SetValue(street, r);
        return street;
    }

    private static async Task Run(StreetEncounterSystem street, string method, params object?[] args)
    {
        var m = typeof(StreetEncounterSystem).GetMethod(method, F);
        m.Should().NotBeNull($"{method} must exist");
        try { await (Task)m!.Invoke(street, args)!; }
        catch (TargetInvocationException ex) when (ex.InnerException != null) { throw ex.InnerException; }
    }

    [Fact]
    public async Task AMurderGrudgeFight_BoostsTheMonster_NotTheWorldNpc()
    {
        var player = Player("P5GrudgeHero");
        var npc = Npc("P5GrudgeNpc");
        npc.Memory!.RecordEvent(new MemoryEvent { Type = MemoryType.Murdered, InvolvedCharacter = player.Name2, Description = "murdered", Timestamp = DateTime.Now });
        long str = npc.Strength, baseStr = npc.BaseStrength;

        var street = Street(new HighRandom());
        await Run(street, "ExecuteGrudgeConfrontation", npc, player, Term("F\n"), new EncounterResult());

        street.LastFightMonster.Should().NotBeNull("the grudge fight took place");
        street.LastFightMonster!.Strength.Should().Be((long)(str * (1.0f + GameConfig.MurderGrudgeRageBonusSTR)), "the rage raises the fight's Monster");
        street.LastFightMonster.Punch.Should().Be(street.LastFightMonster.Strength / 2);
        npc.Strength.Should().Be(str, "the world NPC keeps its Strength after the fight");
        npc.BaseStrength.Should().Be(baseStr);
    }

    [Fact]
    public async Task ARefusedGrudgeApology_BoostsTheMonster_NotTheWorldNpc()
    {
        var player = Player("P5ApologyHero");
        var npc = Npc("P5ApologyNpc");
        long str = npc.Strength, baseStr = npc.BaseStrength;

        var street = Street(new HighRandom());   // the apology roll fails
        await Run(street, "ExecuteGrudgeConfrontation", npc, player, Term("A\n"), new EncounterResult());

        street.LastFightMonster.Should().NotBeNull("the refused apology leads to a fight");
        street.LastFightMonster!.Strength.Should().Be((long)(str * 1.15));
        npc.Strength.Should().Be(str, "the world NPC keeps its Strength after the fight");
        npc.BaseStrength.Should().Be(baseStr);
    }

    [Fact]
    public async Task AnInsultedChallenger_BoostsTheMonster_NotTheWorldNpc()
    {
        var player = Player("P5InsultHero");
        var npc = Npc("P5InsultNpc");
        NPCSpawnSystem.Instance.AddRestoredNPC(npc);
        long str = npc.Strength, baseStr = npc.BaseStrength;

        var street = Street(new SeqRandom(0.0));
        await Run(street, "ProcessChallengeEncounter", player, null, new EncounterResult(), Term("I\n"));

        street.LastFightMonster.Should().NotBeNull("the insult leads to a fight");
        street.LastFightMonster!.Name.Should().Be(npc.Name);
        street.LastFightMonster.Strength.Should().Be(str + 5);
        npc.Strength.Should().Be(str, "the world NPC keeps its Strength after the fight");
        npc.BaseStrength.Should().Be(baseStr);
    }

    [Fact]
    public void NoRageSite_WritesTheWorldNpcsStrength()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir != null && !Directory.Exists(Path.Combine(dir.FullName, "Scripts"))) dir = dir.Parent;
        string src = File.ReadAllText(Path.Combine(dir!.FullName, "Scripts", "Systems", "StreetEncounterSystem.cs"));
        src.Should().NotContain("challenger.Strength +=");
        src.Should().NotContain("grudgeNpc.Strength =");
        src.Should().NotContain("spouse.Strength =");
        src.Should().Contain("rageStrengthMult: 1.25", "the spouse taunt rage is passed to the fight");
    }

    private sealed class ScriptedStream : Stream
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
}
