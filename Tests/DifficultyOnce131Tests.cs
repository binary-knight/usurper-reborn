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

namespace UsurperReborn.Tests;

/// <summary>
/// 1.3.1 (C3): difficulty and the sysop XP and gold values apply once to a monster kill, at the player's
/// win, and not where the monster is created. The world simulator's NPC earnings follow neither.
/// Every test builds its state in memory; the difficulty, both sysop values and the static hooks are
/// put back in Dispose.
/// </summary>
[Collection("SharedGameSingletons")]
public class DifficultyOnce131Tests : IDisposable
{
    private const BindingFlags F = BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public;

    private readonly DifficultyMode _difficultyBefore = DifficultySystem.CurrentDifficulty;
    private readonly float _xpBefore = GameConfig.XPMultiplier;
    private readonly float _goldBefore = GameConfig.GoldMultiplier;
    private readonly float _npcXpBefore = WorldSimulator.NpcXpMultiplier;
    private readonly Action<string?, Func<string, string>>? _sinkBefore = CombatEngine.GroupBroadcastSink;
    private readonly Func<Character, string> _langBefore = CombatEngine.LanguageOf;

    public void Dispose()
    {
        DifficultySystem.CurrentDifficulty = _difficultyBefore;
        GameConfig.XPMultiplier = _xpBefore;
        GameConfig.GoldMultiplier = _goldBefore;
        WorldSimulator.NpcXpMultiplier = _npcXpBefore;
        CombatEngine.GroupBroadcastSink = _sinkBefore;
        CombatEngine.LanguageOf = _langBefore;
    }

    // ---------- settings ----------

    private sealed record Setting(string Name, DifficultyMode Mode, float Sysop);

    private static readonly Setting Normal = new("Normal", DifficultyMode.Normal, 1.0f);
    private static readonly Setting Easy = new("Easy", DifficultyMode.Easy, 1.0f);
    private static readonly Setting Hard = new("Hard", DifficultyMode.Hard, 1.0f);
    private static readonly Setting Nightmare = new("Nightmare", DifficultyMode.Nightmare, 1.0f);
    private static readonly Setting Sysop2 = new("Normal with sysop values 2.0", DifficultyMode.Normal, 2.0f);
    private static readonly Setting[] All = { Normal, Easy, Hard, Nightmare, Sysop2 };

    private static void Apply(Setting s)
    {
        DifficultySystem.CurrentDifficulty = s.Mode;
        GameConfig.XPMultiplier = s.Sysop;
        GameConfig.GoldMultiplier = s.Sysop;
    }

    /// <summary>The same monster every time: the creation gold roll comes from the shared generator,
    /// which is seeded for this thread just before.</summary>
    private static Monster Ogre(int level, long hp = 30, int seed = 131, string name = "Test Ogre")
    {
        RecruitPoolWorld.SeedSharedRandomOnThisThread(seed);
        return Monster.CreateMonster(level, name, hp, 20, 5, "", false, false, "", "", false, false, 2, 4, 6);
    }

    // ---------- 1. creation ----------

    [Fact]
    public void A_monster_is_created_with_the_same_stored_reward_on_every_difficulty_and_sysop_value()
    {
        foreach (int level in new[] { 1, 12, 50, 80 })
        {
            Apply(Normal);
            var baseline = Ogre(level);
            baseline.Experience.Should().BeGreaterThanOrEqualTo(15);
            baseline.Gold.Should().BeGreaterThanOrEqualTo(10);
            foreach (var s in All)
            {
                Apply(s);
                var m = Ogre(level);
                m.Experience.Should().Be(baseline.Experience, "stored XP at level {0} on {1}", level, s.Name);
                m.Gold.Should().Be(baseline.Gold, "stored gold at level {0} on {1}", level, s.Name);
            }
        }
    }

    // ---------- fight harness (as CombatEvents126Tests) ----------

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

    /// <summary>
    /// The engine's seeded generator, except that the win's gold roll (0 up to half of a monster's stored
    /// gold, CombatEngine.HandleVictoryMultiMonster) returns 0. The number of draws before the win differs
    /// with the difficulty, so without this the roll would differ between the runs being compared.
    /// </summary>
    private sealed class NoPurseRoll : Random
    {
        private readonly HashSet<int> _purseRolls;
        public NoPurseRoll(int seed, IEnumerable<Monster> monsters) : base(seed)
        {
            _purseRolls = monsters.Select(m => (int)Math.Min(int.MaxValue - 1L, (long)(m.Gold * 0.5))).ToHashSet();
        }
        public override int Next(int minValue, int maxValue) =>
            minValue == 0 && _purseRolls.Contains(maxValue) ? 0 : base.Next(minValue, maxValue);
    }

    private static string Attacks(int n = 40) => string.Concat(Enumerable.Repeat("A\n\n", n)) + string.Concat(Enumerable.Repeat("P\n", 10));

    private static Character Hero(int level, string name = "Tester") => new Character
    {
        Name1 = name, Name2 = name, AI = CharacterAI.Human,
        Class = CharacterClass.Warrior, Race = CharacterRace.Human, Level = level,
        HP = 90000, MaxHP = 90000, BaseMaxHP = 90000,
        Strength = 900, BaseStrength = 900, Defence = 400, BaseDefence = 400,
        Dexterity = 300, BaseDexterity = 300, Agility = 200, BaseAgility = 200,
        Constitution = 30, BaseConstitution = 30, Intelligence = 20, BaseIntelligence = 20,
        Wisdom = 20, BaseWisdom = 20, Stamina = 100, BaseStamina = 100,
        Mental = 80, Gold = 0, Healing = 0, AutoHeal = false,
        CombatSpeed = CombatSpeed.Instant,
    };

    private static async Task<CombatResult> Fight(Character hero, List<Monster> monsters, List<Character>? mates = null)
    {
        var output = new MemoryStream();
        var engine = new CombatEngine(new TerminalEmulator(new ScriptedStream(Attacks()), output));
        typeof(CombatEngine).GetField("random", F)!.SetValue(engine, new NoPurseRoll(131, monsters));
        var task = engine.PlayerVsMonsters(hero, monsters, mates, offerMonkEncounter: false);
        var done = await Task.WhenAny(task, Task.Delay(TimeSpan.FromSeconds(60)));
        if (done != task) throw new TimeoutException("the fight did not end: " + Encoding.UTF8.GetString(output.ToArray()));
        return await task;
    }

    /// <summary>One solo win over one level 50 monster by a level 50 hero (no level factor, no early
    /// game factor): the XP and gold the hero was paid.</summary>
    private static async Task<(long Xp, long Gold)> SoloWin(Setting s)
    {
        Apply(s);
        var hero = Hero(50);
        var result = await Fight(hero, new List<Monster> { Ogre(50) });
        result.Outcome.Should().Be(CombatOutcome.Victory, "the hero wins on {0}", s.Name);
        // the result carries the kill's XP and gold, which the combat_events row and telemetry log
        return (result.ExperienceGained, result.GoldGained);
    }

    // Tolerance of tests 2 and 3: with every other factor at 1 the win makes one (long) cut per reward
    // after the difficulty and sysop factor, so the paid number is the Normal number times the factor,
    // cut to a whole number: at most 1 below. One more unit is allowed for a float product.
    private const long Cut = 2;

    // ---------- 2. the live single-player win pays the difficulty factor once ----------

    [Fact]
    public async Task The_win_pays_Easy_one_and_a_half_Hard_three_quarters_and_Nightmare_half_of_Normal()
    {
        var normal = await SoloWin(Normal);
        normal.Xp.Should().BeGreaterThan(4000, "a level 50 monster");
        normal.Gold.Should().BeGreaterThan(2000);
        foreach (var (s, factor) in new[] { (Easy, 1.5), (Hard, 0.75), (Nightmare, 0.5) })
        {
            var paid = await SoloWin(s);
            ((double)paid.Xp).Should().BeApproximately(normal.Xp * factor, Cut, "XP on {0}", s.Name);
            ((double)paid.Gold).Should().BeApproximately(normal.Gold * factor, Cut, "gold on {0}", s.Name);
        }
    }

    // ---------- 3. the sysop values apply once ----------

    [Fact]
    public async Task The_win_pays_double_and_not_fourfold_with_the_sysop_values_at_two()
    {
        var normal = await SoloWin(Normal);
        var doubled = await SoloWin(Sysop2);
        ((double)doubled.Xp).Should().BeApproximately(normal.Xp * 2.0, Cut);
        ((double)doubled.Gold).Should().BeApproximately(normal.Gold * 2.0, Cut);
    }

    // ---------- 4. the group share (M2): a grouped player is paid the sysop value once ----------

    private static Character Follower() => new Character
    {
        Name1 = "Grouped Friend", Name2 = "Grouped Friend", AI = CharacterAI.Human, Level = 30,
        HP = 90000, MaxHP = 90000, BaseMaxHP = 90000,
        Strength = 900, BaseStrength = 900, Defence = 400, BaseDefence = 400,
        Dexterity = 300, BaseDexterity = 300, Agility = 200, BaseAgility = 200,
        Constitution = 25, BaseConstitution = 25, Stamina = 80, BaseStamina = 80, Mental = 80,
        Class = CharacterClass.Warrior, Race = CharacterRace.Human, CombatSpeed = CombatSpeed.Instant,
        AutoLevelUp = false,
        GroupPlayerUsername = "p131follower",
        RemoteTerminal = new TerminalEmulator(new MemoryStream(), new MemoryStream()),
        CombatInputChannel = System.Threading.Channels.Channel.CreateBounded<string>(1),
    };

    private static async Task<(long Xp, long Gold)> GroupWin(Setting s)
    {
        Apply(s);
        CombatEngine.GroupBroadcastSink = (_, _) => { };
        CombatEngine.LanguageOf = _ => "en";
        var follower = Follower();
        using var stop = new CancellationTokenSource();
        var feeder = Task.Run(async () =>
        {
            while (!stop.IsCancellationRequested)
            {
                if (follower.IsAwaitingCombatInput && follower.CombatInputChannel!.Reader.Count == 0)
                    follower.CombatInputChannel.Writer.TryWrite("A");
                await Task.Delay(2);
            }
        });
        CombatResult result;
        try
        {
            result = await Fight(Hero(30), new List<Monster> { Ogre(30, seed: 131, name: "Test Ogre A"), Ogre(30, seed: 132, name: "Test Ogre B") },
                new List<Character> { follower });
        }
        finally
        {
            stop.Cancel();
            await feeder;
        }
        result.Outcome.Should().Be(CombatOutcome.Victory, "the group wins on {0}", s.Name);
        return (follower.Experience, follower.Gold);
    }

    [Fact]
    public async Task A_grouped_player_is_paid_the_sysop_values_once()
    {
        var normal = await GroupWin(Normal);
        var doubled = await GroupWin(Sysop2);
        normal.Xp.Should().BeGreaterThan(1000, "two level 30 monsters");
        normal.Gold.Should().BeGreaterThan(500);
        // the share is cut to a whole number after the sysop factor, after the team bonus and after the
        // gold split: up to 1 lost per cut on the x1 run, so up to 2 per cut after doubling
        ((double)doubled.Xp).Should().BeApproximately(normal.Xp * 2.0, 8, "grouped XP");
        ((double)doubled.Gold).Should().BeApproximately(normal.Gold * 2.0, 8, "grouped gold");
    }

    // ---------- M1: NPC earnings in the world simulator follow neither ----------

    private static int _made;

    private static NPC Fighter(long hp, long strength, int level = 30)
    {
        _made++;
        return new NPC
        {
            ID = "npc_c3_" + _made, Id = "c3-" + _made, Name1 = "Sim Fighter " + _made, Name2 = "Sim Fighter " + _made,
            Race = CharacterRace.Human, Sex = CharacterSex.Male, Class = CharacterClass.Warrior, Level = level,
            HP = hp, MaxHP = hp, BaseMaxHP = hp, Strength = strength, Defence = 30, Agility = 30, Stamina = 30,
            Gold = 0, Team = "", CurrentLocation = "Main Street", AI = CharacterAI.Computer, Age = 30,
            BirthDate = DateTime.Now.AddHours(-300),
        };
    }

    /// <summary>Z5: the round simulator's totals (NPCCombatSimulator.Simulate) for one level 40 monster.</summary>
    private static (long Xp, long Gold) RoundSimulator(Setting s)
    {
        Apply(s);
        var npc = Fighter(90000, 90000);
        npc.WeapPow = 500;
        var monster = Ogre(40);
        RecruitPoolWorld.SeedSharedRandomOnThisThread(77);
        var r = NPCCombatSimulator.Simulate(npc, new List<Monster> { monster }, null, new Random(42));
        r.Outcome.Should().Be(NPCCombatOutcome.Won, "the NPC wins on {0}", s.Name);
        return (r.ExpReward, r.GoldReward);
    }

    [Fact]
    public void The_round_simulator_pays_an_NPC_the_same_on_every_difficulty_and_sysop_value()
    {
        var baseline = RoundSimulator(Normal);
        baseline.Xp.Should().BeGreaterThan(3000);
        baseline.Gold.Should().BeGreaterThan(1500);
        foreach (var s in All)
            RoundSimulator(s).Should().Be(baseline, "Z5 on {0}", s.Name);
    }

    /// <summary>Z3: the kill totals of the team fight (WorldSimulator.SimulateTeamVsMonsterCombat).</summary>
    private static (long Xp, long Gold) TeamFight(RecruitPoolWorld w, Setting s)
    {
        Apply(s);
        var team = new List<NPC> { Fighter(90000, 90000), Fighter(90000, 90000) };
        foreach (var n in team) n.UpdateLocation("Dungeon");
        var monsters = new List<Monster> { Ogre(40, seed: 131), Ogre(40, seed: 132) };
        typeof(WorldSimulator).GetField("random", F)!.SetValue(w.Sim, new Random(9));
        RecruitPoolWorld.SeedSharedRandomOnThisThread(78);
        var args = new object?[] { team, monsters, null, null };
        bool won = (bool)typeof(WorldSimulator).GetMethod("SimulateTeamVsMonsterCombat", F)!.Invoke(w.Sim, args)!;
        won.Should().BeTrue("the team wins on {0}", s.Name);
        return ((long)args[2]!, (long)args[3]!);
    }

    [Fact]
    public void The_team_fight_totals_are_the_same_on_every_difficulty_and_sysop_value()
    {
        RecruitPoolRuns.SilenceNews();
        using var w = new RecruitPoolWorld(131, online: false, classic: false);
        var baseline = TeamFight(w, Normal);
        baseline.Xp.Should().BeGreaterThan(6000, "two level 40 monsters");
        baseline.Gold.Should().BeGreaterThan(3000);
        foreach (var s in All)
            TeamFight(w, s).Should().Be(baseline, "Z3 on {0}", s.Name);
    }

    /// <summary>Z1 and Z2: one solo dungeon visit (WorldSimulator.NPCExploreDungeon); what the NPC gained.
    /// Below level 30 the visit takes the abstract fight (tier B); from level 30 the round simulator
    /// (tier A), whose totals are paid by the same method.</summary>
    private static (long Xp, long Gold, bool Alive) Visit(RecruitPoolWorld w, Setting s, int seed, long hp, long strength, int level = 20)
    {
        Apply(s);
        var npc = Fighter(hp, strength, level);
        w.Npcs.Add(npc);
        try
        {
            WorldSimulator.IsTierANPC(npc).Should().Be(level >= 30, "level {0} decides the path of the visit", level);
            WorldSimulator.NpcXpMultiplier = 1.0f;
            typeof(WorldSimulator).GetField("random", F)!.SetValue(w.Sim, new Random(seed));
            w.ReseedOtherRandoms(seed);
            RecruitPoolWorld.SeedSharedRandomOnThisThread(seed + 500);
            w.Call("NPCExploreDungeon", npc);
            return (npc.Experience, npc.Gold, npc.IsAlive && !npc.IsDead);
        }
        finally { w.Npcs.Remove(npc); }
    }

    [Fact]
    public void A_solo_dungeon_win_pays_an_NPC_the_same_on_every_difficulty_and_sysop_value()
    {
        RecruitPoolRuns.SilenceNews();
        using var w = new RecruitPoolWorld(131, online: false, classic: false);
        Visit(w, Normal, 999, 90000, 90000);   // the first visit of a process builds tables as it goes: not compared
        int wins = 0;
        for (int seed = 1; seed <= 12; seed++)
        {
            // the NPC kills with its first blow on every difficulty, so the draws are the same
            var baseline = Visit(w, Normal, seed, 90000, 90000);
            if (baseline.Xp > 0 && baseline.Gold > 0) wins++;
            foreach (var s in All)
                Visit(w, s, seed, 90000, 90000).Should().Be(baseline, "Z1 at seed {0} on {1}", seed, s.Name);
        }
        wins.Should().BeGreaterThan(5, "the visits are wins that pay XP and gold");
    }

    [Fact]
    public void A_solo_dungeon_win_through_the_round_simulator_pays_an_NPC_the_same_on_both_sysop_values()
    {
        // Difficulty is not compared here: it scales the generated monster's health, and the round
        // simulator's fight (and so its draws) changes with it.
        RecruitPoolRuns.SilenceNews();
        using var w = new RecruitPoolWorld(131, online: false, classic: false);
        Visit(w, Normal, 999, 90000, 90000, 30);
        int wins = 0;
        for (int seed = 1; seed <= 12; seed++)
        {
            var baseline = Visit(w, Normal, seed, 90000, 90000, 30);
            if (baseline.Xp > 0 && baseline.Gold > 0) wins++;
            Visit(w, Sysop2, seed, 90000, 90000, 30).Should().Be(baseline, "Z5 through the visit at seed {0}", seed);
        }
        wins.Should().BeGreaterThan(5, "the visits are wins that pay XP and gold");
    }

    [Fact]
    public void A_solo_dungeon_flight_pays_an_NPC_the_same_partial_XP_on_both_sysop_values()
    {
        // Difficulty is not compared here: it scales the generated monster's health, and the partial XP
        // is a share of the health the NPC took off, so it differs by difficulty for that reason alone.
        RecruitPoolRuns.SilenceNews();
        using var w = new RecruitPoolWorld(131, online: false, classic: false);
        Visit(w, Normal, 999, 90000, 90000);
        int flights = 0;
        for (int seed = 1; seed <= 200; seed++)
        {
            long hp = 200 + 40 * (seed % 10);
            var baseline = Visit(w, Normal, seed, hp, 30);
            if (baseline.Alive && baseline.Xp > 0 && baseline.Gold == 0) flights++;
            Visit(w, Sysop2, seed, hp, 30).Should().Be(baseline, "Z2 at seed {0}", seed);
        }
        flights.Should().BeGreaterThan(0, "some visits end in a flight that pays partial XP");
    }
}
