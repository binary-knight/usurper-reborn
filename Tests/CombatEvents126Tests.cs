using System;
using System.Collections.Generic;
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
/// 1.2.6: the combat event stream (CombatEngine.Observer) and the per-fight accumulator
/// (CombatResult.Tally). Every test drives a real PlayerVsMonsters fight through a scripted terminal
/// to its end and reads the values from the CombatResult; nothing feeds the accumulator by hand.
/// </summary>
[Collection("SharedGameSingletons")]
public class CombatEvents126Tests
{
    // ---------- harness ----------

    /// <summary>Serves the scripted bytes once, then reports EOF.</summary>
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

    private sealed class Recorder : ICombatObserver
    {
        public readonly List<CombatEvent> Events = new();
        public CombatFightStart? Start;
        public long? End;
        public int Calls;
        public bool Throw;
        public void OnFightStart(CombatFightStart start) { Calls++; Start = start; if (Throw) throw new InvalidOperationException("observer start"); }
        public void OnEvent(CombatEvent e) { Calls++; Events.Add(e); if (Throw) throw new InvalidOperationException("observer event"); }
        public void OnFightEnd(long playerHpEnd) { Calls++; End = playerHpEnd; if (Throw) throw new InvalidOperationException("observer end"); }
    }

    private sealed class Fight
    {
        public CombatEngine Engine = null!;
        public MemoryStream Output = null!;
        public TerminalEmulator Term = null!;
        public CombatResult? Result;
        public Exception? Error;
        public string Transcript => Encoding.UTF8.GetString(Output.ToArray());
    }

    private static Fight NewFight(string script, bool events = true, ICombatObserver? observer = null, int seed = 126)
    {
        var f = new Fight { Output = new MemoryStream() };
        f.Term = new TerminalEmulator(new ScriptedStream(script), f.Output);
        f.Engine = new CombatEngine(f.Term);
        f.Engine.SeedRandomForTests(seed);
        f.Engine.CombatEventsEnabled = events;
        f.Engine.Observer = observer;
        return f;
    }

    private static async Task<Fight> Run(Fight f, Func<CombatEngine, Task<CombatResult>> fight)
    {
        try
        {
            var task = fight(f.Engine);
            var done = await Task.WhenAny(task, Task.Delay(TimeSpan.FromSeconds(60)));
            if (done != task) throw new TimeoutException("the fight did not end");
            f.Result = await task;
        }
        catch (Exception ex) { f.Error = ex; }
        return f;
    }

    /// <summary>[A]ttack, then Enter for a random target when more than one foe stands (an Enter at the
    /// action prompt only re-prompts); [P]ass answers loot prompts after the fight.</summary>
    private static string Attacks(int n = 30) => string.Concat(Enumerable.Repeat("A\n\n", n)) + string.Concat(Enumerable.Repeat("P\n", 10));

    private static Character Hero(string name = "Tester", CharacterClass cls = CharacterClass.Warrior, long hp = 600, int level = 10) => new Character
    {
        Name1 = name, Name2 = name, AI = CharacterAI.Human,
        Class = cls, Race = CharacterRace.Human, Level = level,
        HP = hp, MaxHP = hp, BaseMaxHP = hp,
        Strength = 60, BaseStrength = 60, Defence = 20, BaseDefence = 20,
        Dexterity = 30, BaseDexterity = 30, Agility = 20, BaseAgility = 20,
        Constitution = 30, BaseConstitution = 30, Intelligence = 20, BaseIntelligence = 20,
        Wisdom = 20, BaseWisdom = 20, Stamina = 100, BaseStamina = 100,
        Mental = 80, Gold = 0, Healing = 0, AutoHeal = false,
        CombatSpeed = CombatSpeed.Instant,
    };

    private static Monster Brute(string name = "Brute", long hp = 400, long str = 40, params string[] abilities) => new Monster
    {
        Name = name, Level = 8, HP = hp, MaxHP = hp, Strength = str, Defence = 5, Experience = 1, Gold = 0,
        IsActive = true, SpecialAbilities = abilities.ToList(),
    };

    private static List<Monster> Two() => new() { Brute("Brute A", 260), Brute("Brute B", 260) };

    // ---------- 1. logging does not change combat (supervisor condition 1) ----------

    private static async Task<(Fight off1, Fight off2, Fight on, Recorder rec)> ThreeRuns(
        Func<Fight, Func<CombatEngine, Task<CombatResult>>> build, string script, Action<Fight>? prepare = null)
    {
        async Task<(Fight, Recorder?)> One(bool events)
        {
            var rec = events ? new Recorder() : null;
            var f = NewFight(script, events, rec);
            prepare?.Invoke(f);
            await Run(f, build(f));
            return (f, rec);
        }
        var (off1, _) = await One(false);
        var (off2, _) = await One(false);
        var (on, r) = await One(true);
        return (off1, off2, on, r!);
    }

    private static void SameFight(Fight off1, Fight off2, Fight on, Recorder rec, Func<Fight, string> hpTrail)
    {
        off1.Error.Should().BeNull("{0}", off1.Transcript);
        off2.Error.Should().BeNull();
        on.Error.Should().BeNull("{0}", on.Transcript);
        // the baseline is deterministic, so a difference below would come from the events
        FightPart(off1).Should().Contain(Loc.Get("combat.round_label", 2), "the comparison covers the rounds of the fight");
        FightPart(off2).Should().Be(FightPart(off1), "the seeded fight with events off repeats exactly");
        FightPart(on).Should().Be(FightPart(off1), "the same seeded fight reads the same with the events on");
        hpTrail(on).Should().Be(hpTrail(off1));
        on.Result!.Outcome.Should().Be(off1.Result!.Outcome);
        on.Result.CurrentRound.Should().Be(off1.Result.CurrentRound);
        on.Result.TotalDamageDealt.Should().Be(off1.Result.TotalDamageDealt);
        on.Result.TotalDamageTaken.Should().Be(off1.Result.TotalDamageTaken);
        // events were really on in one run and off in the other
        off1.Result.Tally.Started.Should().BeFalse("events off feed nothing");
        on.Result.Tally.Started.Should().BeTrue();
        rec.Events.Should().NotBeEmpty();
    }

    /// <summary>The screen up to the victory's rewards. The loot found after a win comes from
    /// Random.Shared (LootGenerator), which a test seed does not cover; the fight itself is all the
    /// engine's seeded rolls.</summary>
    private static string FightPart(Fight f)
    {
        string t = f.Transcript;
        int cut = t.IndexOf(Loc.Get("combat.defeated_count", 2), StringComparison.Ordinal);
        return cut < 0 ? t : t[..cut];
    }

    /// <summary>Every HP number the fight printed, in order: the HP trail of all combatants.</summary>
    private static string HpTrail(Fight f) =>
        string.Join(",", Regex.Matches(Regex.Replace(FightPart(f), "\u001b\\[[0-9;?]*[A-Za-z]", ""), @"\d+/\d+").Select(m => m.Value));

    [Fact]
    public async Task EventsOnAndOff_SameSeededSoloFight_SameResultAndHpTrail()
    {
        Character? last = null;
        var (off1, off2, on, rec) = await ThreeRuns(
            f => e => e.PlayerVsMonsters(last = Hero(), Two(), offerMonkEncounter: false), Attacks());
        SameFight(off1, off2, on, rec, HpTrail);
    }

    private static Character Mate(string name, long hp = 500) => new Character
    {
        Name1 = name, Name2 = name, AI = CharacterAI.Computer,
        Class = CharacterClass.Warrior, Race = CharacterRace.Human, Level = 1,
        HP = hp, MaxHP = hp, BaseMaxHP = hp,
        Strength = 40, BaseStrength = 40, Defence = 10, BaseDefence = 10,
        Dexterity = 20, BaseDexterity = 20, Agility = 10, BaseAgility = 10,
        Constitution = 20, BaseConstitution = 20, Stamina = 50, BaseStamina = 50,
        Mental = 80, CombatSpeed = CombatSpeed.Instant,
    };

    [Fact]
    public async Task EventsOnAndOff_SameSeededFightWithTeammates_SameResultAndHpTrail()
    {
        var (off1, off2, on, rec) = await ThreeRuns(
            f => e => e.PlayerVsMonsters(Hero(), Two(), new List<Character> { Mate("Ally One"), Mate("Ally Two") }, offerMonkEncounter: false),
            Attacks());
        SameFight(off1, off2, on, rec, HpTrail);
        rec.Events.Should().Contain(e => e.Actor == CombatSide.Team, "the teammates fought");
    }

    // ---------- group fight harness ----------

    private static Character Follower(string name, string username) => new Character
    {
        Name1 = name, Name2 = name, AI = CharacterAI.Human, Level = 10,
        HP = 500, MaxHP = 500, BaseMaxHP = 500,
        Strength = 50, BaseStrength = 50, Defence = 15, BaseDefence = 15,
        Dexterity = 25, BaseDexterity = 25, Agility = 15, BaseAgility = 15,
        Constitution = 25, BaseConstitution = 25, Stamina = 80, BaseStamina = 80, Mental = 80,
        Class = CharacterClass.Warrior, Race = CharacterRace.Human, CombatSpeed = CombatSpeed.Instant,
        GroupPlayerUsername = username,
        RemoteTerminal = new TerminalEmulator(new MemoryStream(), new MemoryStream()),
        CombatInputChannel = System.Threading.Channels.Channel.CreateBounded<string>(1),
    };

    /// <summary>A group fight on the leader's engine: the follower's keystrokes ("A") are fed to the
    /// combat input channel whenever the engine waits for them, as GroupFollowerLoop does.</summary>
    private static async Task<(Fight f, Character follower)> GroupFight(bool events, ICombatObserver? rec, TelemetryStore? telemetry = null)
    {
        var prevSink = CombatEngine.GroupBroadcastSink;
        var prevLang = CombatEngine.LanguageOf;
        CombatEngine.GroupBroadcastSink = (_, _) => { };
        CombatEngine.LanguageOf = _ => "en";
        try
        {
            var follower = Follower("Grouped Friend", "p126follower");
            var f = NewFight(Attacks(), events, rec);
            f.Engine.TelemetryStoreOverride = telemetry;
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
            await Run(f, e => e.PlayerVsMonsters(Hero(), Two(), new List<Character> { follower }, offerMonkEncounter: false));
            stop.Cancel();
            await feeder;
            return (f, follower);
        }
        finally
        {
            CombatEngine.GroupBroadcastSink = prevSink;
            CombatEngine.LanguageOf = prevLang;
        }
    }

    [Fact]
    public async Task EventsOnAndOff_SameSeededGroupFight_SameResultAndHpTrail()
    {
        var (off1, _) = await GroupFight(false, null);
        var (off2, _) = await GroupFight(false, null);
        var rec = new Recorder();
        var (on, follower) = await GroupFight(true, rec);
        SameFight(off1, off2, on, rec, HpTrail);
        // supervisor condition 4: the follower's blows are the team's, on the leader's one result
        on.Result!.Tally.DmgByTeam.Should().BeGreaterThan(0, "the follower's hits count for the team");
        on.Result.Tally.PartySize.Should().Be(2);
        rec.Events.Where(e => e.Actor == CombatSide.Team && e.Target == CombatSide.Monster).Sum(e => e.Amount)
            .Should().Be(on.Result.Tally.DmgByTeam);
    }

    // ---------- 1.2.7: the telemetry row does not change combat (T1-tests row 9) ----------

    private static readonly System.Reflection.FieldInfo EngineRandom =
        typeof(CombatEngine).GetField("random", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance)!;
    private static readonly System.Reflection.MethodInfo QueueHook =
        typeof(CombatEngine).GetMethod("QueueTelemetryRow", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance)!;

    /// <summary>A Random that counts every draw.</summary>
    private sealed class CountingRandom : Random
    {
        public int Draws;
        public override int Next() { Draws++; return base.Next(); }
        public override int Next(int maxValue) { Draws++; return base.Next(maxValue); }
        public override int Next(int minValue, int maxValue) { Draws++; return base.Next(minValue, maxValue); }
        public override long NextInt64() { Draws++; return base.NextInt64(); }
        public override long NextInt64(long maxValue) { Draws++; return base.NextInt64(maxValue); }
        public override long NextInt64(long minValue, long maxValue) { Draws++; return base.NextInt64(minValue, maxValue); }
        public override double NextDouble() { Draws++; return base.NextDouble(); }
        public override float NextSingle() { Draws++; return base.NextSingle(); }
        public override void NextBytes(byte[] buffer) { Draws++; base.NextBytes(buffer); }
        public override void NextBytes(Span<byte> buffer) { Draws++; base.NextBytes(buffer); }
    }

    /// <summary>The same seeded fight three times: twice with no consent state, once with a stored yes.
    /// The screen, the HP trail and the result are the same, and the hook draws nothing from the engine's random.</summary>
    private static async Task SameFightWithTelemetryOnAndOff(Func<TelemetryStore, Task<Fight>> run)
    {
        var dirs = new List<string>();
        TelemetryStore NewStore(bool yes)
        {
            string dir = Path.Combine(Path.GetTempPath(), $"usurper-telemetry126-{Guid.NewGuid():N}");
            dirs.Add(dir);
            Directory.CreateDirectory(dir);
            var store = new TelemetryStore(dir, () => TelemetrySource.Single);
            if (yes) store.SetInstallAnswer(true);
            return store;
        }
        try
        {
            var absent1 = await run(NewStore(false));
            var absent2 = await run(NewStore(false));
            var yesStore = NewStore(true);
            var yes = await run(yesStore);
            absent1.Error.Should().BeNull("{0}", absent1.Transcript);
            absent2.Error.Should().BeNull();
            yes.Error.Should().BeNull("{0}", yes.Transcript);
            FightPart(absent1).Should().Contain(Loc.Get("combat.round_label", 2), "the comparison covers the rounds of the fight");
            FightPart(absent2).Should().Be(FightPart(absent1), "the seeded fight with no consent repeats exactly");
            FightPart(yes).Should().Be(FightPart(absent1), "the same seeded fight reads the same with a stored yes");
            HpTrail(yes).Should().Be(HpTrail(absent1));
            yes.Result!.Outcome.Should().Be(absent1.Result!.Outcome);
            yes.Result.CurrentRound.Should().Be(absent1.Result.CurrentRound);
            yes.Result.TotalDamageDealt.Should().Be(absent1.Result.TotalDamageDealt);
            yes.Result.TotalDamageTaken.Should().Be(absent1.Result.TotalDamageTaken);
            yes.Result.Tally.DmgByPlayer.Should().Be(absent1.Result.Tally.DmgByPlayer);
            yes.Result.Tally.PlayerHpEnd.Should().Be(absent1.Result.Tally.PlayerHpEnd);
            // the row was really queued in one run and not in the others
            absent1.Engine.LastTelemetryAppend.Should().BeNull();
            yes.Engine.LastTelemetryAppend.Should().NotBeNull();
            await yes.Engine.LastTelemetryAppend!;
            yesStore.ReadQueue().Should().HaveCount(1);
            // queueing the row draws nothing from the engine's random: the hook run again on the finished
            // fight with a counting random (after the fight, loot from Random.Shared can change how often
            // the engine's random is drawn, so the draws are counted around the hook itself)
            var counting = new CountingRandom();
            EngineRandom.SetValue(yes.Engine, counting);
            QueueHook.Invoke(yes.Engine, new object[] { yes.Result, "victory", 0L, 0L });
            counting.Draws.Should().Be(0, "queueing the row draws nothing from the engine's random");
            await yes.Engine.LastTelemetryAppend!;
            yesStore.ReadQueue().Should().HaveCount(2, "the hook queued the row again");
        }
        finally
        {
            foreach (var d in dirs) try { Directory.Delete(d, true); } catch { }
        }
    }

    [Fact]
    public async Task EventsOnAndOff_TelemetryYesAndAbsent_SameSeededSoloFight_SameResultAndHpTrail()
    {
        await SameFightWithTelemetryOnAndOff(store =>
        {
            var f = NewFight(Attacks());
            f.Engine.TelemetryStoreOverride = store;
            return Run(f, e => e.PlayerVsMonsters(Hero(), Two(), offerMonkEncounter: false));
        });
    }

    [Fact]
    public async Task EventsOnAndOff_TelemetryYesAndAbsent_SameSeededFightWithTeammates_SameResultAndHpTrail()
    {
        await SameFightWithTelemetryOnAndOff(store =>
        {
            var f = NewFight(Attacks());
            f.Engine.TelemetryStoreOverride = store;
            return Run(f, e => e.PlayerVsMonsters(Hero(), Two(), new List<Character> { Mate("Ally One"), Mate("Ally Two") }, offerMonkEncounter: false));
        });
    }

    [Fact]
    public async Task EventsOnAndOff_TelemetryYesAndAbsent_SameSeededGroupFight_SameResultAndHpTrail()
    {
        await SameFightWithTelemetryOnAndOff(async store => (await GroupFight(true, null, store)).f);
    }

    // ---------- 2. the values, from a real fight (supervisor condition 2) ----------

    [Fact]
    public async Task SoloFight_TallyHoldsTheFight()
    {
        var hero = Hero();
        hero.Location = (int)GameLocation.Dungeons;
        hero.LastDungeonFloor = 7;
        hero.Difficulty = DifficultyMode.Hard;
        var rec = new Recorder();
        var f = await Run(NewFight(Attacks(), observer: rec), e => e.PlayerVsMonsters(hero, Two(), offerMonkEncounter: false));
        f.Error.Should().BeNull("{0}", f.Transcript);
        var r = f.Result!;
        r.Outcome.Should().Be(CombatOutcome.Victory, f.Transcript);
        var t = r.Tally;
        t.FloorActual.Should().Be(7);
        t.Difficulty.Should().Be((int)DifficultyMode.Hard);
        t.PartySize.Should().Be(1);
        t.EncounterSize.Should().Be(2);
        t.FirstActor.Should().Be(0);
        // a fight of plain attacks: the old totals and the events agree
        t.DmgByPlayer.Should().BeGreaterThan(0).And.Be(r.TotalDamageDealt);
        t.DmgToPlayerBasic.Should().BeGreaterThan(0).And.Be(r.TotalDamageTaken);
        t.DmgToPlayerAbility.Should().Be(0);
        t.DmgToPlayerSpell.Should().Be(0);
        t.DmgToPlayerDot.Should().Be(0);
        t.DmgToTeam.Should().Be(0);
        t.DmgByTeam.Should().Be(0);
        t.TeammatesLost.Should().Be(0);
        t.PlayerHpEnd.Should().Be(hero.HP).And.BeLessThan(hero.MaxHP);
        // the observer saw what the accumulator counted
        rec.Start.Should().Be(new CombatFightStart(7, (int)DifficultyMode.Hard, 1, 2, 0));
        rec.End.Should().Be(t.PlayerHpEnd);
        rec.Events.Where(e => e.Actor == CombatSide.Player && e.Target == CombatSide.Monster).Sum(e => e.Amount).Should().Be(t.DmgByPlayer);
        rec.Events.Should().OnlyContain(e => e.Kind == CombatEventKind.Basic);
    }

    [Fact]
    public async Task OutsideTheDungeon_FloorIsZero_AndTheDefaultsHold()
    {
        var hero = Hero();
        hero.Location = (int)GameLocation.MainStreet;
        var f = await Run(NewFight(Attacks()), e => e.PlayerVsMonsters(hero, new List<Monster> { Brute(hp: 60) }, offerMonkEncounter: false));
        f.Error.Should().BeNull("{0}", f.Transcript);
        f.Result!.Tally.FloorActual.Should().Be(0);
        f.Result.Tally.Difficulty.Should().Be((int)DifficultyMode.Normal);
        f.Result.Tally.EncounterSize.Should().Be(1);
    }

    [Fact]
    public async Task MonsterAbility_CountsAsAbilityDamage()
    {
        var hero = Hero(hp: 3000);
        var f = await Run(NewFight(Attacks(40)), e => e.PlayerVsMonsters(hero,
            new List<Monster> { Brute("Sea Thing", 900, 30, "TidalWave") }, offerMonkEncounter: false));
        f.Error.Should().BeNull("{0}", f.Transcript);
        f.Result!.Tally.DmgToPlayerAbility.Should().BeGreaterThan(0, f.Transcript);
        f.Result.Tally.DmgToPlayerSpell.Should().Be(0);
    }

    [Fact]
    public async Task MonsterSpell_CountsAsSpellDamage()
    {
        var hero = Hero(hp: 3000);
        var f = await Run(NewFight(Attacks(40)), e => e.PlayerVsMonsters(hero,
            new List<Monster> { Brute("Fire Thing", 900, 30, "Fireball") }, offerMonkEncounter: false));
        f.Error.Should().BeNull("{0}", f.Transcript);
        f.Result!.Tally.DmgToPlayerSpell.Should().BeGreaterThan(0, f.Transcript);
        f.Result.Tally.DmgToPlayerAbility.Should().Be(0);
    }

    [Fact]
    public async Task StatusTicksOnThePlayer_CountAsDamageOverTime()
    {
        var hero = Hero();
        hero.ApplyStatus(StatusEffect.Bleeding, 50);
        var f = await Run(NewFight(Attacks()), e => e.PlayerVsMonsters(hero, Two(), offerMonkEncounter: false));
        f.Error.Should().BeNull("{0}", f.Transcript);
        f.Result!.Tally.DmgToPlayerDot.Should().BeGreaterThan(0, f.Transcript);
        f.Result.Tally.DmgToPlayerBasic.Should().Be(f.Result.TotalDamageTaken, "a tick is not a basic hit");
    }

    [Fact]
    public async Task Teammates_DealAndTakeDamage_ForTheTeam()
    {
        var f = await Run(NewFight(Attacks()), e => e.PlayerVsMonsters(Hero(), Two(),
            new List<Character> { Mate("Ally One"), Mate("Ally Two") }, offerMonkEncounter: false));
        f.Error.Should().BeNull("{0}", f.Transcript);
        var t = f.Result!.Tally;
        t.PartySize.Should().Be(3);
        t.DmgByTeam.Should().BeGreaterThan(0, f.Transcript);
        t.DmgToTeam.Should().BeGreaterThan(0, f.Transcript);
        t.TeammatesLost.Should().Be(0);
    }

    [Fact]
    public async Task ATeammateWhoFalls_IsCountedOnce()
    {
        var frail = Mate("Frail Ally", 3);
        var f = await Run(NewFight(Attacks(60)), e => e.PlayerVsMonsters(Hero(hp: 5000),
            new List<Monster> { Brute("Brute A", 1500), Brute("Brute B", 1500) },
            new List<Character> { frail }, offerMonkEncounter: false));
        f.Error.Should().BeNull("{0}", f.Transcript);
        frail.IsAlive.Should().BeFalse(f.Transcript);
        f.Result!.Tally.TeammatesLost.Should().Be(1);
    }

    [Fact]
    public async Task APotion_CountsItsUseAndItsHeal()
    {
        var hero = Hero(hp: 600);
        hero.HP = 200;
        hero.Healing = 5;
        var f = await Run(NewFight("I\n" + Attacks()), e => e.PlayerVsMonsters(hero, Two(), offerMonkEncounter: false));
        f.Error.Should().BeNull("{0}", f.Transcript);
        var t = f.Result!.Tally;
        t.PotionsUsed.Should().BeGreaterThan(0, f.Transcript).And.Be(5 - (int)hero.Healing);
        t.HealPlayer.Should().BeGreaterThan(0);
    }

    [Fact]
    public async Task AClassAbility_CountsItsUse_AndItsHitsAsAbility()
    {
        var hero = Hero();
        hero.Quickbar[0] = "power_strike";
        var rec = new Recorder();
        var f = await Run(NewFight("1\n\n" + Attacks(), observer: rec), e => e.PlayerVsMonsters(hero, Two(), offerMonkEncounter: false));
        f.Error.Should().BeNull("{0}", f.Transcript);
        f.Result!.Tally.AbilitiesUsed.Should().Be(1, f.Transcript);
        rec.Events.Should().Contain(e => e.Actor == CombatSide.Player && e.Kind == CombatEventKind.Ability && e.Amount > 0);
    }

    [Fact]
    public async Task ASpell_CountsItsCast()
    {
        var hero = Hero(cls: CharacterClass.Cleric);
        hero.Mana = hero.MaxMana = hero.BaseMaxMana = 500;
        hero.Quickbar[0] = "spell:1";
        hero.HP = 300;
        var f = await Run(NewFight("1\n" + Attacks()), e => e.PlayerVsMonsters(hero, Two(), offerMonkEncounter: false));
        f.Error.Should().BeNull("{0}", f.Transcript);
        f.Result!.Tally.SpellsUsed.Should().Be(1, f.Transcript);
    }

    [Fact]
    public async Task AnAmbush_PutsTheMonstersFirst()
    {
        var hero = Hero();
        hero.BaseAgility = 1; hero.BaseDexterity = 1; hero.Level = 1;
        var foes = new List<Monster> { Brute("Lurker", 200), Brute("Lurker Two", 200) };
        foreach (var m in foes) m.Level = 40;
        var f = await Run(NewFight(Attacks()), e => e.PlayerVsMonsters(hero, foes, offerMonkEncounter: false, isAmbush: true));
        f.Error.Should().BeNull("{0}", f.Transcript);
        f.Result!.Tally.FirstActor.Should().Be(1, f.Transcript);
        f.Result.Tally.EncounterSize.Should().Be(2);
    }

    // ---------- 5. a throwing observer never breaks a fight ----------

    [Fact]
    public async Task AThrowingObserver_TheFightFinishes_AndIsLoggedOncePerFight()
    {
        var clean = await Run(NewFight(Attacks()), e => e.PlayerVsMonsters(Hero(), Two(), offerMonkEncounter: false));
        var rec = new Recorder { Throw = true };
        var f = await Run(NewFight(Attacks(), observer: rec), e => e.PlayerVsMonsters(Hero(), Two(), offerMonkEncounter: false));
        f.Error.Should().BeNull("{0}", f.Transcript);
        rec.Calls.Should().BeGreaterThan(2, "the observer was called, and threw, many times");
        f.Engine.ObserverFailureLogs.Should().Be(1, "one log line per fight, not per hit");
        f.Result!.Outcome.Should().Be(clean.Result!.Outcome);
        FightPart(f).Should().Be(FightPart(clean), "the fight went on exactly as without the observer");
        f.Result.Tally.DmgByPlayer.Should().Be(clean.Result.Tally.DmgByPlayer, "the accumulator is not the observer");
    }

    // ---------- 4 and 6: one row per fight, written by the leader's engine only ----------

    [Fact]
    public void TheCombatRow_IsWrittenOnlyByTheCombatEngine()
    {
        string root = Leftovers1114BTests.RepoRoot();
        var writers = Directory.GetFiles(Path.Combine(root, "Scripts"), "*.cs", SearchOption.AllDirectories)
            .Where(p => File.ReadAllText(p).Contains("LogCombatEvent("))
            .Select(p => Path.GetFileName(p)).OrderBy(n => n).ToList();
        writers.Should().Equal("CombatEngine.cs", "SqlSaveBackend.CombatEvents.cs");
        string events = File.ReadAllText(Path.Combine(root, "Scripts", "Systems", "CombatEvents.cs"));
        events.Should().NotContain("File.").And.NotContain("Backend").And.NotContain("Random");
    }
}
