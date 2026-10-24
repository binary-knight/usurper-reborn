using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Security.Cryptography;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using FluentAssertions;
using UsurperRemake;
using UsurperRemake.Systems;
using Xunit;
using static UsurperReborn.Tests.PartySurvival133Harness;

namespace UsurperReborn.Tests;

/// <summary>
/// 1.4.0 W: a Cautious ally withdraws from a fight it is losing. On its own turn, before any other choice, an ally
/// that takes orders and can be Downed, with stance Cautious and below 20 percent of its max HP announces a try,
/// before a potion or a heal: 1.4.0 W2 took out the "no heal left" rule, so a potion of its own, one it may borrow
/// from the belt, a heal spell or a heal ability no longer keeps it in. It rolls the flee chance a player's R rolls (the boss chance in a boss fight). Nightmare blocks it. A
/// failure spends the turn. A success takes it out of this fight's list; every fight end puts it back at its own
/// place with the HP it left with, before the outcome branch, so it keeps its experience share.
/// New members are reached by name, so this file builds and runs on the code before the piece (release-1.4.0
/// at 54f1c3cd), where it is red.
/// </summary>
[Collection("SharedGameSingletons")]
public class W140Withdraw
{
    private const BindingFlags NPI = BindingFlags.NonPublic | BindingFlags.Instance;
    private const BindingFlags ANY = BindingFlags.NonPublic | BindingFlags.Public | BindingFlags.Instance;

    // ---------------------------------------------------------------- seams reached by name

    private static void ForceRoll(CombatEngine e, int roll)
    {
        var p = typeof(CombatEngine).GetProperty("WithdrawRollOverride", ANY);
        p.Should().NotBeNull("the withdraw roll seam exists");
        p!.SetValue(e, (Func<int>)(() => roll));
    }

    private static bool Withdrew(CombatEngine e, Character ally)
    {
        var stats = e.StatsFor(ally);
        var f = stats.GetType().GetField("Withdrew", ANY);
        f.Should().NotBeNull("AllyFightStats has a Withdrew flag");
        return (bool)f!.GetValue(stats)!;
    }

    private static double PolicyWithdraw(TeammateStance s)
    {
        var p = typeof(TeammatePolicy).GetProperty("Withdraw", ANY);
        p.Should().NotBeNull("the stance policy has a Withdraw threshold");
        return (double)p!.GetValue(TeammateStances.PolicyFor(s))!;
    }

    private static System.Collections.ICollection WithdrawnList(CombatEngine e)
    {
        var f = typeof(CombatEngine).GetField("_withdrawn", NPI);
        f.Should().NotBeNull("the fight keeps a _withdrawn list");
        return (System.Collections.ICollection)f!.GetValue(e)!;
    }

    private static string TryLine(Character a) => Loc.Get("combat.ally_withdraw_try", a.DisplayName);
    private static string DoneLine(Character a) => Loc.Get("combat.ally_withdraw_done", a.DisplayName);
    private static string FailLine(Character a) => Loc.Get("combat.ally_withdraw_fail", a.DisplayName);

    private static int Count(string text, string line)
    {
        int n = 0, i = 0;
        while ((i = text.IndexOf(line, i, StringComparison.Ordinal)) >= 0) { n++; i += line.Length; }
        return n;
    }

    /// <summary>A Barbarian ally: no spells, and no heal ability below level 36 but Second Wind (every class has it
    /// from level 1), which <see cref="Cautious"/> switches off as the owner's skill toggle does.</summary>
    private static Character Low(string name, long hp, long maxHp = 100)
    {
        var a = Ally(name, hp, maxHp);
        a.Class = CharacterClass.Barbarian;
        return a;
    }

    /// <summary>The owner sets the ally Cautious and, for a Barbarian, switches its Second Wind off (the skill
    /// toggle), so a turn the rule does not take (a second turn in a round) shows no heal ability.</summary>
    private static Character Cautious(Character owner, Character ally)
    {
        TeammateStances.Set(owner, TeammateStances.KeyFor(ally), TeammateStance.Cautious);
        if (ally.Class == CharacterClass.Barbarian)
            owner.TeammateDisabledAbilities[ally.GetSkillToggleKey()] = new List<string> { "second_wind" };
        return ally;
    }

    /// <summary>The ally's turn, as the round loop runs it.</summary>
    private static Task Turn(Rig r, Character ally) =>
        CallAsync(r.Engine, "ProcessTeammateActionMultiMonster", ally, r.Result.Monsters, r.Result);

    private static Rig TurnRig(Character ally, Character? owner = null, Monster? mob = null)
    {
        var o = owner ?? Hero();
        var r = NewRig(owner: o, team: new[] { ally }, monsters: new[] { mob ?? Mob("Rat", "Goblinoid", hp: 5000, str: 1) });
        return r;
    }

    // ---------------------------------------------------------------- the stance rule

    [Fact]
    public void TheThreshold_IsCautiousOnly()
    {
        PolicyWithdraw(TeammateStance.Cautious).Should().Be(0.20);
        PolicyWithdraw(TeammateStance.Balanced).Should().Be(0);
        PolicyWithdraw(TeammateStance.Aggressive).Should().Be(0);
        // Balanced is unchanged in every other column (the default every ally has)
        var b = TeammateStances.PolicyFor(TeammateStance.Balanced);
        (b.EmergencySelfPotion, b.PotionMostInjured, b.DefensiveFirst, b.Brace, b.MayTaunt, b.TargetWeightMultiplier)
            .Should().Be((0.30, 0.50, 0.40, 0.35, true, 1.00));
    }

    // ---------------------------------------------------------------- the try

    [Fact]
    public async Task ACautiousAllyBelowTwentyWithNoHeal_TriesAndOnASuccessLeavesTheFight()
    {
        var owner = Hero();
        var ally = Cautious(owner, Low("Mira", 15));
        var r = TurnRig(ally, owner);
        long mobHp = r.Result.Monsters[0].HP;
        ForceRoll(r.Engine, 1);
        await Turn(r, ally);
        r.Text.Should().Contain(TryLine(ally)).And.Contain(DoneLine(ally));
        r.Result.Teammates.Should().NotContain(ally, "a withdrawn ally is out of this fight's list");
        r.Team.Should().BeSameAs(r.Result.Teammates);
        WithdrawnList(r.Engine).Count.Should().Be(1);
        Withdrew(r.Engine, ally).Should().BeTrue();
        r.Result.Monsters[0].HP.Should().Be(mobHp, "the withdrawal is the ally's action");
        ally.HP.Should().Be(15);
        ally.IsDowned.Should().BeFalse();
    }

    [Fact]
    public async Task AForcedFailure_SpendsTheTurn_AndOnlyOneTryARound()
    {
        var owner = Hero();
        var ally = Cautious(owner, Low("Mira", 15));
        var r = TurnRig(ally, owner);
        long mobHp = r.Result.Monsters[0].HP;
        long stamina = ally.CurrentCombatStamina = 500;
        ForceRoll(r.Engine, 100);
        await Turn(r, ally);
        r.Text.Should().Contain(TryLine(ally)).And.Contain(FailLine(ally));
        string shown = System.Text.RegularExpressions.Regex.Replace(r.Text, "\u001b\\[[0-9;]*[A-Za-z]", "");
        shown.Split('\n').Select(l => l.Trim()).Where(l => l.Length > 0).Should()
            .Equal(new[] { TryLine(ally), FailLine(ally) }, "the try is the whole turn: {0}", shown);
        ally.CurrentCombatStamina.Should().Be(stamina, "no ability that turn");
        r.Text.Should().NotContain(DoneLine(ally));
        r.Result.Teammates.Should().Contain(ally);
        r.Result.Monsters[0].HP.Should().Be(mobHp, "a failed try spends the turn");
        ally.IsDefending.Should().BeFalse("nothing else that turn, not even a brace: {0}", r.Text);
        r.Text.Should().NotContain(Loc.Get("combat.teammate_defends", ally.DisplayName));
        Withdrew(r.Engine, ally).Should().BeFalse();
        ally.HP.Should().Be(15, "a failure has no other cost");

        // a second turn in the same round does not try again: the ally acts as it would without the rule
        await Turn(r, ally);
        Count(r.Text, TryLine(ally)).Should().Be(1, "at most one try per ally per round: {0}", r.Text);
        Count(r.Text, FailLine(ally)).Should().Be(1);

        // the next round it tries again
        r.Result.CurrentRound = 2;
        await Turn(r, ally);
        Count(r.Text, TryLine(ally)).Should().Be(2);
    }

    [Fact]
    public async Task AboveTwenty_OrAtTwenty_ItDoesNotTry()
    {
        // at 25 percent with no heal: above the threshold, it fights on
        var owner2 = Hero();
        var above = Cautious(owner2, Low("Kade", 25));
        var r2 = TurnRig(above, owner2);
        ForceRoll(r2.Engine, 1);
        await Turn(r2, above);
        r2.Text.Should().NotContain(TryLine(above));
        r2.Result.Teammates.Should().Contain(above);

        // at exactly 20 percent: compared strictly below
        var owner3 = Hero();
        var at = Cautious(owner3, Low("Pip", 20));
        var r3 = TurnRig(at, owner3);
        ForceRoll(r3.Engine, 1);
        await Turn(r3, at);
        r3.Text.Should().NotContain(TryLine(at));

        // at 25 percent with a potion of its own: it drinks it (the Cautious emergency potion is at 40 percent)
        var owner5 = Hero();
        var drinker = Cautious(owner5, Low("Lio", 25));
        drinker.Healing = 1;
        var r5 = TurnRig(drinker, owner5);
        ForceRoll(r5.Engine, 1);
        await Turn(r5, drinker);
        r5.Text.Should().NotContain(TryLine(drinker));
        drinker.Healing.Should().Be(0, "above 20 percent it drank its potion: {0}", r5.Text);
    }

    [Fact]
    public async Task WithAPotion_OrABeltToBorrowFrom_BelowTwenty_ItTriesInsteadOfDrinking()
    {
        // 1.4.0 W2: its own potion no longer keeps it in; it tries and does not drink that turn
        var owner = Hero();
        var withPotion = Cautious(owner, Low("Mira", 15));
        withPotion.Healing = 1;
        var r = TurnRig(withPotion, owner);
        ForceRoll(r.Engine, 1);
        await Turn(r, withPotion);
        r.Text.Should().Contain(TryLine(withPotion)).And.Contain(DoneLine(withPotion));
        withPotion.Healing.Should().Be(1, "it pulled back instead of drinking: {0}", r.Text);
        withPotion.HP.Should().Be(15);
        r.Result.Teammates.Should().NotContain(withPotion);

        // a failed try is the whole turn: still no potion
        var owner2 = Hero();
        var failing = Cautious(owner2, Low("Kade", 15));
        failing.Healing = 1;
        var r2 = TurnRig(failing, owner2);
        ForceRoll(r2.Engine, 100);
        await Turn(r2, failing);
        r2.Text.Should().Contain(TryLine(failing)).And.Contain(FailLine(failing));
        failing.Healing.Should().Be(1, "a failed try spends the turn, so no potion: {0}", r2.Text);
        failing.HP.Should().Be(15);
        r2.Result.Teammates.Should().Contain(failing);

        // no potion of its own, but the owner's shared belt would lend one: it tries and borrows none
        var owner4 = Hero(potions: 10);
        owner4.SharedPotionBelt = true;
        var borrower = Cautious(owner4, Low("Tam", 15));
        var r4 = TurnRig(borrower, owner4);
        ForceRoll(r4.Engine, 1);
        await Turn(r4, borrower);
        r4.Text.Should().Contain(TryLine(borrower));
        owner4.Healing.Should().Be(10, "it borrowed nothing from the belt: {0}", r4.Text);
        borrower.HP.Should().Be(15);
    }

    [Fact]
    public async Task AHealSpellOrAHealAbilityItCanUse_NoLongerKeepsItIn()
    {
        // a Cleric with the mana for a heal spell tries and casts nothing
        var owner = Hero();
        var cleric = Cautious(owner, Low("Mira", 15));
        cleric.Class = CharacterClass.Cleric;
        cleric.Level = 20;
        cleric.Mana = cleric.MaxMana = 500;
        cleric.Wisdom = cleric.BaseWisdom = 60;
        cleric.Intelligence = cleric.BaseIntelligence = 60;
        var r = TurnRig(cleric, owner);
        ForceRoll(r.Engine, 100);
        await Turn(r, cleric);
        r.Text.Should().Contain(TryLine(cleric), "{0}", r.Text);
        cleric.Mana.Should().Be(500, "no heal spell that turn: {0}", r.Text);
        cleric.HP.Should().Be(15);

        // a Paladin with no mana but the stamina for Lay on Hands tries and uses no ability
        var owner2 = Hero();
        var paladin = Cautious(owner2, Ally("Kade", 15, 100));
        paladin.Class = CharacterClass.Paladin;
        paladin.Level = 5;
        paladin.Mana = paladin.MaxMana = 0;
        paladin.CurrentCombatStamina = 500;
        ClassAbilitySystem.GetAbility("lay_on_hands").Should().NotBeNull();
        var r2 = TurnRig(paladin, owner2);
        ForceRoll(r2.Engine, 100);
        await Turn(r2, paladin);
        r2.Text.Should().Contain(TryLine(paladin), "{0}", r2.Text);
        paladin.CurrentCombatStamina.Should().Be(500, "no Lay on Hands that turn: {0}", r2.Text);
        paladin.HP.Should().Be(15);

        // a Warrior with the stamina for Second Wind and a potion of its own: it tries, and neither drinks nor heals
        var owner3 = Hero();
        var warrior = Cautious(owner3, Ally("Pip", 15, 100));
        warrior.CurrentCombatStamina = 500;
        warrior.Healing = 1;
        var r3 = TurnRig(warrior, owner3);
        ForceRoll(r3.Engine, 100);
        await Turn(r3, warrior);
        r3.Text.Should().Contain(TryLine(warrior), "{0}", r3.Text);
        warrior.Healing.Should().Be(1, "{0}", r3.Text);
        warrior.CurrentCombatStamina.Should().Be(500, "no Second Wind that turn: {0}", r3.Text);
        warrior.HP.Should().Be(15);
    }

    [Theory]
    [InlineData(TeammateStance.Balanced)]
    [InlineData(TeammateStance.Aggressive)]
    public async Task BalancedAndAggressive_NeverTry(TeammateStance stance)
    {
        var owner = Hero();
        var ally = Low("Mira", 5);
        TeammateStances.Set(owner, TeammateStances.KeyFor(ally), stance);
        owner.TeammateDisabledAbilities[ally.GetSkillToggleKey()] = new List<string> { "second_wind" };
        var r = TurnRig(ally, owner);
        ForceRoll(r.Engine, 1);
        long mobHp = r.Result.Monsters[0].HP;
        await Turn(r, ally);
        r.Text.Should().NotContain(TryLine(ally));
        r.Result.Teammates.Should().Contain(ally);
    }

    [Fact]
    public async Task Nightmare_BlocksIt()
    {
        var before = DifficultySystem.CurrentDifficulty;
        try
        {
            DifficultySystem.CurrentDifficulty = DifficultyMode.Nightmare;
            var owner = Hero();
            var ally = Cautious(owner, Low("Mira", 15));
            var r = TurnRig(ally, owner);
            ForceRoll(r.Engine, 1);
            long mobHp = r.Result.Monsters[0].HP;
            await Turn(r, ally);
            r.Text.Should().NotContain(TryLine(ally));
            r.Result.Teammates.Should().Contain(ally);
        }
        finally { DifficultySystem.CurrentDifficulty = before; }
    }

    [Fact]
    public async Task ABossFight_RollsTheBossChance()
    {
        // BossContext set: the chance is CalculateFleeChance with the boss flag (20, up to 60 below half HP);
        // at 15 percent HP it is 20 + min(40, 35 * 1.75) = 60, so a roll of 60 lands and 61 fails
        var owner = Hero();
        var ally = Cautious(owner, Low("Mira", 15));
        int chance = (int)Call(new CombatEngine(new TerminalEmulator(new MemoryStream(), new MemoryStream())), "CalculateFleeChance", ally, true)!;
        chance.Should().Be(60);
        var r = TurnRig(ally, owner);
        r.Engine.BossContext = new BossCombatContext();
        try
        {
            ForceRoll(r.Engine, 61);
            await Turn(r, ally);
            r.Text.Should().Contain(FailLine(ally), "{0}", r.Text);
            r.Result.CurrentRound = 2;
            ForceRoll(r.Engine, 60);
            await Turn(r, ally);
            r.Text.Should().Contain(DoneLine(ally), "{0}", r.Text);
            r.Result.Teammates.Should().NotContain(ally);
        }
        finally { r.Engine.BossContext = null; }
    }

    [Fact]
    public async Task GroupedPlayerMercenaryEchoAndMiracleBeast_NeverTry()
    {
        foreach (var setup in new Action<Character>[]
                 {
                     c => c.IsMercenary = true,
                     c => c.IsEcho = true,
                     c => c.IsMiracleAlly = true,
                     c => c.RemoteTerminal = new TerminalEmulator(new MemoryStream(), new MemoryStream()),
                 })
        {
            var owner = Hero();
            var ally = Low("Mira", 15);
            setup(ally);
            owner.TeammateStances[TeammateStances.KeyFor(ally)] = (int)TeammateStance.Cautious;   // stored, but no orders taken
            owner.TeammateDisabledAbilities[ally.GetSkillToggleKey()] = new List<string> { "second_wind" };
            var r = TurnRig(ally, owner);
            ForceRoll(r.Engine, 1);
            await Turn(r, ally);
            r.Text.Should().NotContain(TryLine(ally));
            r.Result.Teammates.Should().Contain(ally);
        }
    }

    // ---------------------------------------------------------------- whole fights

    private sealed record Fight(CombatResult? Result, Exception? Error, string Text, CombatEngine Engine);

    private static async Task<Fight> RunFight(Character hero, List<Monster> monsters, List<Character> team, string script,
        int? roll = 1, int seed = 140, Stream? output = null, Action<CombatEngine>? setup = null)
    {
        var outStream = output ?? new MemoryStream();
        var term = new TerminalEmulator(new ScriptedStream(script), outStream);
        var e = new CombatEngine(term) { DivineFavorChanceOverride = () => 0f };
        e.SeedRandomForTests(seed);
        if (roll.HasValue) ForceRoll(e, roll.Value);
        setup?.Invoke(e);
        string Text() => outStream is MemoryStream ms ? Encoding.UTF8.GetString(ms.ToArray()) : outStream.ToString() ?? "";
        try
        {
            var task = e.PlayerVsMonsters(hero, monsters, team, offerMonkEncounter: false);
            var done = await Task.WhenAny(task, Task.Delay(TimeSpan.FromSeconds(60)));
            if (done != task) throw new TimeoutException("the fight did not end");
            return new Fight(await task, null, Text(), e);
        }
        catch (Exception ex) { return new Fight(null, ex, Text(), e); }
    }

    private static string Attacks(int n = 40) => string.Concat(Enumerable.Repeat("A\n\n", n)) + string.Concat(Enumerable.Repeat("P\n", 20));

    [Fact]
    public async Task AWonFight_PutsItBackInItsPlace_WithItsHp_AndItsExperienceShare()
    {
        var hero = Hero();
        hero.TeamXPPercent = new[] { 40, 50, 10, 0, 0 };   // custom: place 1 takes 50, place 2 takes 10
        hero.TeamXPIsExplicit = true;
        hero.TeamXPEvenSplit = false;
        var a = Cautious(hero, Low("Mira", 15));
        var b = Ally("Kade", 100, 100);
        var mob = Mob("Rat", "Goblinoid", hp: 300, str: 1);
        mob.Experience = 2000;
        long aXp = a.Experience, bXp = b.Experience;
        var f = await RunFight(hero, new List<Monster> { mob }, new List<Character> { a, b }, Attacks());
        f.Error.Should().BeNull("{0}", f.Text);
        f.Result!.Outcome.Should().Be(CombatOutcome.Victory, "{0}", f.Text);
        f.Text.Should().Contain(DoneLine(a), "{0}", f.Text);
        Withdrew(f.Engine, a).Should().BeTrue();
        f.Result.Teammates.Should().Equal(new[] { a, b }, "it is back in its own place, ahead of Kade");
        a.HP.Should().Be(15, "it comes back with the HP it left with");
        a.IsDowned.Should().BeFalse();
        a.Lifecycle.Should().Be(AllyLifecycle.Active);
        WithdrawnList(f.Engine).Count.Should().Be(0);
        long aGain = a.Experience - aXp, bGain = b.Experience - bXp;
        aGain.Should().BeGreaterThan(0, "the split ran after the put back: {0}", f.Text);
        aGain.Should().BeGreaterThan(bGain * 3, "place 1 (50) is still Mira's, place 2 (10) Kade's: {0} against {1}", aGain, bGain);
    }

    [Fact]
    public async Task AFledFight_PutsItBack_BeforeThePartialShare()
    {
        var hero = Hero();
        hero.SmokeBombs = 1;   // R with a smoke bomb out of a fight with no boss is a certain escape
        var a = Cautious(hero, Low("Mira", 15));
        var rat = Mob("Rat", "Goblinoid", hp: 1, str: 1);
        rat.Experience = 2000;
        var wolf = Mob("Wolf", "Goblinoid", hp: 5000, str: 1);
        long aXp = a.Experience;
        // round 1: attack the rat (kills it), the ally withdraws; round 2: R
        string script = "A\n1\n\n" + string.Concat(Enumerable.Repeat("R\n\n", 10));
        var f = await RunFight(hero, new List<Monster> { rat, wolf }, new List<Character> { a }, script);
        f.Error.Should().BeNull("{0}", f.Text);
        f.Result!.Outcome.Should().Be(CombatOutcome.PlayerEscaped, "{0}", f.Text);
        f.Text.Should().Contain(DoneLine(a), "{0}", f.Text);
        rat.IsAlive.Should().BeFalse("{0}", f.Text);
        f.Result.Teammates.Should().Equal(new[] { a });
        a.HP.Should().Be(15);
        (a.Experience - aXp).Should().BeGreaterThan(0, "the partial share is paid after the put back: {0}", f.Text);
    }

    [Fact]
    public async Task EveryActiveFighterDown_WhileOneIsWithdrawn_IsALoss_AndPutsItBack()
    {
        var hero = Hero(30);
        hero.MaxHP = hero.BaseMaxHP = 600;
        hero.Healing = 0;
        hero.IsExhibitionCombat = true;   // the loss ends without the death screens, and Death's Door is off
        var a = Cautious(hero, Low("Mira", 15));
        var b = Ally("Kade", 10, 100);   // Balanced: fights on and is Downed
        var crusher = new Monster
        {
            Name = "Crusher", Level = 8, HP = 50000, MaxHP = 50000, Strength = 60000, Defence = 5, Experience = 1, Gold = 0,
            IsActive = true, FamilyName = "Goblinoid",
        };
        var f = await RunFight(hero, new List<Monster> { crusher }, new List<Character> { a, b }, Attacks() + string.Concat(Enumerable.Repeat("P\n", 10)));
        f.Error.Should().BeNull("{0}", f.Text);
        f.Text.Should().Contain(DoneLine(a), "{0}", f.Text);
        b.IsDowned.Should().BeFalse("Kade went down and stood up at the end");
        f.Engine.StatsFor(b).Downs.Should().BeGreaterThan(0, "every Active ally fell: {0}", f.Text);
        f.Result!.Outcome.Should().Be(CombatOutcome.PlayerDied, "a withdrawn ally is no living fighter: {0}", f.Text);
        f.Result.Teammates.Should().Contain(a, "the loss puts it back");
        a.HP.Should().Be(15);
        a.IsDowned.Should().BeFalse();
        WithdrawnList(f.Engine).Count.Should().Be(0);
    }

    /// <summary>An output stream that throws on the first write after a marker text has been written.</summary>
    private sealed class ThrowAfter : MemoryStream
    {
        private readonly string _marker;
        private bool _armed;
        public ThrowAfter(string marker) { _marker = marker; }
        public override void Write(byte[] buffer, int offset, int count)
        {
            if (_armed) throw new InvalidOperationException("W140 test throw");
            base.Write(buffer, offset, count);
            if (Encoding.UTF8.GetString(ToArray()).Contains(_marker)) _armed = true;
        }
        public override void Write(ReadOnlySpan<byte> buffer) => Write(buffer.ToArray(), 0, buffer.Length);
        public override Task WriteAsync(byte[] buffer, int offset, int count, CancellationToken ct) { Write(buffer, offset, count); return Task.CompletedTask; }
        public override ValueTask WriteAsync(ReadOnlyMemory<byte> buffer, CancellationToken ct = default) { Write(buffer.ToArray(), 0, buffer.Length); return ValueTask.CompletedTask; }
        public override string ToString() => Encoding.UTF8.GetString(ToArray());
    }

    [Fact]
    public async Task AFightLeftByAThrow_PutsItBackInTheFinally()
    {
        var hero = Hero();
        var a = Cautious(hero, Low("Mira", 15));
        var mob = Mob("Rat", "Goblinoid", hp: 5000, str: 1);
        var team = new List<Character> { a };
        var output = new ThrowAfter(DoneLine(a));
        var f = await RunFight(hero, new List<Monster> { mob }, team, Attacks(), output: output);
        f.Error.Should().NotBeNull("the stream throws after the withdrawal line: {0}", f.Text);
        f.Error.Should().BeOfType<InvalidOperationException>();
        f.Text.Should().Contain(DoneLine(a));
        WithdrawnList(f.Engine).Count.Should().Be(0, "the finally put it back and cleared the list");
        var list = (List<Character>)typeof(CombatEngine).GetField("currentTeammates", NPI)!.GetValue(f.Engine)!;
        list.Should().Contain(a, "the fight's list holds it again");
        a.HP.Should().Be(15);
    }

    // ---------------------------------------------------------------- Balanced is unchanged

    private static string Sha(string s) => Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(s))).Substring(0, 16);

    /// <summary>A Balanced fight on a fixed seed: the outcome, the rounds, every HP at the end, and the count and a
    /// hash of the random draws until the monster falls (HitRoll128Golden's recording random). The screen text is
    /// left out: other classes of the shared collection change settings that only change how lines look.</summary>
    private static async Task<string> BalancedSignature()
    {
        var hero = Hero();
        var a = Ally("Mira", 15, 100);   // Balanced: would withdraw if Cautious
        var b = Ally("Kade", 100, 100);
        var mob = Mob("Rat", "Goblinoid", hp: 600, str: 30);
        var output = new MemoryStream();
        var term = new TerminalEmulator(new ScriptedStream(Attacks()), output);
        var e = new CombatEngine(term) { DivineFavorChanceOverride = () => 0f };
        var rng = new HitRoll128Golden.RecordingRandom(1404) { Over = () => !mob.IsAlive || !hero.IsAlive };
        typeof(CombatEngine).GetField("random", NPI)!.SetValue(e, rng);
        var task = e.PlayerVsMonsters(hero, new List<Monster> { mob }, new List<Character> { a, b }, offerMonkEncounter: false);
        (await Task.WhenAny(task, Task.Delay(TimeSpan.FromSeconds(60)))).Should().BeSameAs(task);
        var res = await task;
        return $"{res.Outcome}|{res.CurrentRound}|{hero.HP}|{a.HP}|{b.HP}|{a.IsDowned}|{b.IsDowned}|{mob.HP}|{rng.Calls.Count}|{Sha(string.Join("\n", rng.Calls))}";
    }

    /// <summary>Written by this test on release-1.4.0 at 54f1c3cd, before the piece.</summary>
    private const string BalancedOnTheBase = "Victory|4|600|50|50|False|False|0|57|E27EDBC212B3BCEF";

    [Fact]
    public async Task ABalancedFight_IsByteEqualToTheBase_OnAFixedSeed()
    {
        // The base was written with no artifact collected. The story's collected artifacts outlive a test (the fallback
        // StoryProgressionSystem when no session is set): OldGodPayoutTests runs HandleBossDefeated on Maelketh, which
        // collects the Creator's Eye, and its crit bonus made the hero's first swing a precision strike in this fight.
        // So the fight runs on an empty set, and the set is put back after it.
        var artifacts = StoryProgressionSystem.Instance.CollectedArtifacts;
        var held = artifacts.ToList();
        artifacts.Clear();
        try
        {
            string first = await BalancedSignature();
            string second = await BalancedSignature();
            second.Should().Be(first, "the fight is deterministic on its seed");
            first.Should().Be(BalancedOnTheBase);
        }
        finally
        {
            artifacts.Clear();
            foreach (var a in held) artifacts.Add(a);
        }
    }

    // ---------------------------------------------------------------- texts

    [Theory]
    [InlineData("en")]
    [InlineData("hu")]
    [InlineData("es")]
    [InlineData("fr")]
    [InlineData("it")]
    public void TheLines_AreInEveryLanguage_AndFit79(string lang)
    {
        string name = new string('W', 20);
        foreach (var key in new[] { "combat.ally_withdraw_try", "combat.ally_withdraw_done", "combat.ally_withdraw_fail" })
        {
            Loc.HasIn(lang, key).Should().BeTrue(key);
            string row = "  " + Loc.GetIn(lang, key, name);
            row.Should().Contain(name);
            if (lang != "en") Loc.GetIn(lang, key, name).Should().NotBe(Loc.GetIn("en", key, name), "{0} {1} is translated", lang, key);
            if (lang == "en" || lang == "hu")
                row.Length.Should().BeLessThanOrEqualTo(79, row);
            foreach (var shown in UsurperRemake.UI.UIHelper.MessageRows(row))
                shown.Length.Should().BeLessThanOrEqualTo(79, shown);
        }
        // the party menu's Cautious row, as PromptStanceChoice and the Inn print it
        string cautious = $"  [3] {Loc.GetIn(lang, "party.stance.cautious")}  {Loc.GetIn(lang, "party.stance.cautious.desc")}";
        if (lang == "en" || lang == "hu")
            cautious.Length.Should().BeLessThanOrEqualTo(79, cautious);
        Loc.GetIn(lang, "party.stance.cautious.desc").Should().Contain("20", "{0}: the stance line says it pulls back below 20 percent", lang);
        // 1.4.0 W2: it pulls back whatever heals it has left, so the en line no longer names heals
        if (lang == "en") Loc.GetIn(lang, "party.stance.cautious.desc").Should().NotContainEquivalentOf("heal");
    }
}
