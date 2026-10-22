using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Threading.Tasks;
using FluentAssertions;
using UsurperRemake;
using UsurperRemake.Systems;
using Xunit;
using G = UsurperReborn.Tests.HitRoll128Golden;

namespace UsurperReborn.Tests;

/// <summary>
/// 1.3.6 G4 (CP3): the attack and defence slots as timed entries. Each slot shows its highest live positive entry plus
/// its live penalties, and its Duration is the rounds left on the entry shown. A same source recast does not stack, a
/// penalty never wipes a bonus, and when the highest ends the next highest shows again.
/// </summary>
[Collection("SharedGameSingletons")]
public class BonusSlots136Tests
{
    private const BindingFlags F = BindingFlags.NonPublic | BindingFlags.Instance;
    private const BindingFlags FS = BindingFlags.NonPublic | BindingFlags.Static;
    private const BonusSlot Atk = BonusSlot.Attack;
    private const BonusSlot Def = BonusSlot.Defense;

    private static Character Fresh()
    {
        var c = G.Hero(hp: 1_000_000, level: 50);
        c.ClearSlotBonuses();
        return c;
    }

    // ---------------------------------------------------------------- the entries

    [Fact]
    public void BonusSlots136_TheHighestPositiveEntryShows()
    {
        var c = Fresh();
        c.AddSlotBonus(Atk, "a", 20, 5);
        c.AddSlotBonus(Atk, "b", 50, 2);
        c.AddSlotBonus(Atk, "c", 30, 9);
        c.TempAttackBonus.Should().Be(50, "the highest of 20, 50 and 30 shows, they do not add");
        c.TempAttackBonusDuration.Should().Be(2, "the rounds left on the entry shown");
    }

    [Fact]
    public void BonusSlots136_WhenTheHighestEnds_TheNextHighestShows()
    {
        // Time Stop's short bonus over a held whole fight bonus (Wish): its end no longer zeroes the slot.
        var c = Fresh();
        c.AddSlotBonus(Atk, "wish", 40, 999);
        c.AddSlotBonus(Atk, "timestop", 90, 2);
        c.TempAttackBonus.Should().Be(90);
        c.TickSlotBonuses();
        c.TempAttackBonus.Should().Be(90);
        c.TempAttackBonusDuration.Should().Be(1);
        c.TickSlotBonuses();
        c.TempAttackBonus.Should().Be(40, "the short bonus ended; the whole fight bonus held under it shows again");
        c.TempAttackBonusDuration.Should().Be(997);
    }

    [Fact]
    public void BonusSlots136_ASameSourceRecast_DoesNotStack()
    {
        var c = Fresh();
        c.AddSlotBonus(Atk, "flametongue", 15, 5);
        c.TickSlotBonuses();
        c.AddSlotBonus(Atk, "flametongue", 15, 5);
        c.TempAttackBonus.Should().Be(15, "a recast refreshes, it does not add");
        c.TempAttackBonusDuration.Should().Be(5, "its rounds are refreshed to the longer");
        c.AddSlotBonus(Atk, "flametongue", 10, 2);
        c.TempAttackBonus.Should().Be(15, "a weaker recast keeps the higher value");
        c.TempAttackBonusDuration.Should().Be(5, "and the longer rounds");
        c.AddSlotBonus(Atk, "flametongue", 25, 1);
        c.TempAttackBonus.Should().Be(25, "a stronger recast takes the higher value");
        c.TempAttackBonusDuration.Should().Be(5);
    }

    [Fact]
    public void BonusSlots136_APenaltyAppliesOnTop_AndTheHeldBonusSurvives()
    {
        // rage over Shield Wall: the penalty is its own entry
        var c = Fresh();
        c.AddSlotBonus(Def, "shield_wall", 30, 3);
        c.AddSlotBonus(Def, "rage", -15, 5);
        c.TempDefenseBonus.Should().Be(15, "30 held, 15 taken off on top");
        c.TempDefenseBonusDuration.Should().Be(3, "the rounds of the bonus shown");
        c.TickSlotBonuses(); c.TickSlotBonuses(); c.TickSlotBonuses();
        c.TempDefenseBonus.Should().Be(-15, "Shield Wall ended; the penalty is left");
        c.TempDefenseBonusDuration.Should().Be(2, "the rounds left on the penalty");
        c.TickSlotBonuses(); c.TickSlotBonuses();
        c.TempDefenseBonus.Should().Be(0);
        c.TempDefenseBonusDuration.Should().Be(0);
    }

    [Fact]
    public void BonusSlots136_Clear_EmptiesTheEntries()
    {
        var c = Fresh();
        c.AddSlotBonus(Atk, "battle_cry", 80, 4);
        c.AddSlotBonus(Def, "shield_wall", 60, 4);
        c.ClearSlotBonuses();
        c.TempAttackBonus.Should().Be(0); c.TempAttackBonusDuration.Should().Be(0);
        c.TempDefenseBonus.Should().Be(0); c.TempDefenseBonusDuration.Should().Be(0);
        c.AttackBonusEntries.Should().BeEmpty(); c.DefenseBonusEntries.Should().BeEmpty();
        c.AddSlotBonus(Atk, "focus", 10, 2);
        c.TempAttackBonus.Should().Be(10, "no stale bonus comes back after a clear and an add of a smaller one");
        c.ClearSlotBonus(Atk, "focus");
        c.TempAttackBonus.Should().Be(0, "a one source clear drops that source");
    }

    [Fact]
    public void BonusSlots136_AValueSetOnTheFieldsDirectly_TicksAsBefore()
    {
        var c = Fresh();
        c.TempAttackBonus = 50; c.TempAttackBonusDuration = 2;
        c.TickSlotBonuses();
        c.TempAttackBonus.Should().Be(50); c.TempAttackBonusDuration.Should().Be(1);
        c.TickSlotBonuses();
        c.TempAttackBonus.Should().Be(0); c.TempAttackBonusDuration.Should().Be(0);
        c.TempDefenseBonus = 7; c.TempDefenseBonusDuration = 0;
        c.TickSlotBonuses();
        c.TempDefenseBonus.Should().Be(7, "a value with no rounds stays until a clear, as before");
    }

    // ---------------------------------------------------------------- one source in a fight (rule 6)

    /// <summary>The old slot rule for one source: the cast sets value and rounds; each round end takes a round, and the
    /// value goes to 0 with the last.</summary>
    private static List<(int value, int rounds)> TodayOneSource(int value, int rounds, int roundEnds)
    {
        var steps = new List<(int, int)> { (value, rounds) };
        for (int i = 0; i < roundEnds; i++)
        {
            if (rounds > 0) { rounds--; if (rounds <= 0) value = 0; }
            steps.Add((value, rounds));
        }
        return steps;
    }

    private static void EndRound(G.Rig rig) =>
        typeof(CombatEngine).GetMethod("ProcessEndOfRoundAbilityEffects", F)!.Invoke(rig.Engine, new object[] { rig.Hero });

    private static void Apply(G.Rig rig, Character actor, string id)
    {
        var r = ClassAbilitySystem.UseAbility(actor, id, new Random(136));
        r.Success.Should().BeTrue(id);
        ((Task)typeof(CombatEngine).GetMethod("ApplyAbilityEffectsMultiMonster", F)!
            .Invoke(rig.Engine, new object?[] { actor, rig.Monsters[0], rig.Monsters, r, rig.Result })!).GetAwaiter().GetResult();
    }

    [Theory]
    [InlineData("battle_cry", CharacterClass.Warrior, false, false)]
    [InlineData("battle_cry", CharacterClass.Warrior, false, true)]
    [InlineData("shield_wall", CharacterClass.Warrior, true, false)]
    [InlineData("shield_wall", CharacterClass.Warrior, true, true)]
    public void BonusSlots136_OneSource_ShowsTodaysValueAndRounds_AtEveryStepOfAFight(string id, CharacterClass cls, bool defence, bool teammate)
    {
        bool saved = GameConfig.PlayerHitRollEnabled;
        try
        {
            GameConfig.PlayerHitRollEnabled = false;
            var hero = Fresh(); hero.Class = cls;
            var mate = G.Mate("Ally", hp: 1_000_000, level: 50); mate.Class = cls; mate.ClearSlotBonuses();
            var rig = G.NewRig(hero, new G.RecordingRandom(136), new List<Character> { mate }, G.Dummy());
            var actor = teammate ? mate : hero;
            Apply(rig, actor, id);
            int v = defence ? actor.TempDefenseBonus : actor.TempAttackBonus;
            int d = defence ? actor.TempDefenseBonusDuration : actor.TempAttackBonusDuration;
            v.Should().BeGreaterThan(0, id); d.Should().BeGreaterThan(0, id);
            var today = TodayOneSource(v, d, d + 2);
            for (int step = 0; step < today.Count; step++)
            {
                int nv = defence ? actor.TempDefenseBonus : actor.TempAttackBonus;
                int nd = defence ? actor.TempDefenseBonusDuration : actor.TempAttackBonusDuration;
                (nv, nd).Should().Be(today[step], $"{id} at round end {step}");
                EndRound(rig);
            }
        }
        finally { GameConfig.PlayerHitRollEnabled = saved; }
    }

    // ---------------------------------------------------------------- each kind of clear

    private static Character ArmedHero()
    {
        var hero = new Character
        {
            Name2 = "Hero", Class = CharacterClass.Warrior, Race = CharacterRace.Human, Level = 25,
            HP = 900, MaxHP = 900, BaseMaxHP = 900,
            Strength = 100, BaseStrength = 100, WeapPow = 100, Defence = 50, BaseDefence = 50, ArmPow = 50,
            Dexterity = 30, BaseDexterity = 30, Agility = 25, BaseAgility = 25, Constitution = 30, BaseConstitution = 30,
            Stamina = 100, CombatSpeed = CombatSpeed.Instant,
        };
        hero.AddSlotBonus(Atk, "battle_cry", 777, 0);
        hero.AddSlotBonus(Def, "shield_wall", 555, 9);
        return hero;
    }

    private static void ShouldBeEmpty(Character c, string why)
    {
        c.AttackBonusEntries.Should().BeEmpty(why); c.DefenseBonusEntries.Should().BeEmpty(why);
        c.TempAttackBonus.Should().Be(0, why); c.TempAttackBonusDuration.Should().Be(0, why);
        c.TempDefenseBonus.Should().Be(0, why); c.TempDefenseBonusDuration.Should().Be(0, why);
    }

    [Fact]
    public async Task BonusSlots136_FightStart_ClearsTheEntries()
    {
        var term = new TerminalEmulator(new G.ScriptedStream(string.Concat(Enumerable.Repeat("A\n", 10)) + string.Concat(Enumerable.Repeat("P\n", 6))), new MemoryStream());
        var engine = new CombatEngine(term);
        engine.SeedRandomForTests(136);
        var hero = ArmedHero();
        var mate = G.Mate("Ally"); mate.AddSlotBonus(Atk, "inspire", 444, 0);
        int atkSeen = -1, defSeen = -1, entriesSeen = -1;
        engine.BossContext = new BossCombatContext
        {
            BossData = UsurperRemake.Data.OldGodsData.GetGodBossData(OldGodType.Maelketh),
            ApplyPlayerModifiers = p =>
            {
                atkSeen = p.TempAttackBonus; defSeen = p.TempDefenseBonus;
                entriesSeen = p.AttackBonusEntries.Count + p.DefenseBonusEntries.Count;
            },
        };
        var rat = new Monster { Name = "Sewer Rat", Level = 1, HP = 1, MaxHP = 1, Strength = 1, Defence = 0, Experience = 5, Gold = 3 };
        try { await engine.PlayerVsMonsters(hero, new List<Monster> { rat }, new List<Character> { mate }, offerMonkEncounter: false); }
        finally { engine.BossContext = null; }
        atkSeen.Should().Be(0, "the fight start clears the attack entries");
        defSeen.Should().Be(0, "and the defence entries");
        entriesSeen.Should().Be(0);
        mate.AttackBonusEntries.Should().NotContain(e => e.Source == "inspire", "a teammate's entries are cleared at the start too");
        mate.TempAttackBonus.Should().NotBe(444);
    }

    [Fact]
    public async Task BonusSlots136_FightEnd_ClearsTheEntries_AfterAWin()
    {
        var term = new TerminalEmulator(new G.ScriptedStream(string.Concat(Enumerable.Repeat("A\n", 10)) + string.Concat(Enumerable.Repeat("P\n", 6))), new MemoryStream());
        var engine = new CombatEngine(term);
        engine.SeedRandomForTests(136);
        var hero = ArmedHero();
        var rat = new Monster { Name = "Sewer Rat", Level = 1, HP = 1, MaxHP = 1, Strength = 1, Defence = 0, Experience = 5, Gold = 3 };
        // an entry added inside the fight (the first round) must be gone after it
        engine.BossContext = new BossCombatContext
        {
            BossData = UsurperRemake.Data.OldGodsData.GetGodBossData(OldGodType.Maelketh),
            ApplyPlayerModifiers = p => p.AddSlotBonus(Atk, "wish", 300, 999),
        };
        CombatResult res;
        try { res = await engine.PlayerVsMonsters(hero, new List<Monster> { rat }, offerMonkEncounter: false); }
        finally { engine.BossContext = null; }
        res.Outcome.Should().Be(CombatOutcome.Victory);
        ShouldBeEmpty(hero, "the fight end clears the entries");
    }

    [Fact]
    public async Task BonusSlots136_Death_ClearsTheEntries()
    {
        // A death ends the fight through the same end of fight cleanup (no death only clear exists). The script, as in
        // CombatBuffConsumptionTests: attacks, the epitaph, the temple at the veil, then blank lines.
        var term = new TerminalEmulator(new G.ScriptedStream(string.Concat(Enumerable.Repeat("A\n", 6)) + "\n\n\n1\n" + string.Concat(Enumerable.Repeat("\n", 8))), new MemoryStream());
        var engine = new CombatEngine(term);
        engine.SeedRandomForTests(1114);
        var hero = ArmedHero();
        hero.HP = 5;
        var doom = new Monster { Name = "Doom Engine", Level = 60, HP = 5_000_000, MaxHP = 5_000_000, Strength = 5_000_000, Defence = 0, Experience = 1, Gold = 0 };
        var difficulty = DifficultySystem.CurrentDifficulty;
        DifficultySystem.CurrentDifficulty = DifficultyMode.Normal;
        CombatResult res;
        try
        {
            var task = engine.PlayerVsMonsters(hero, new List<Monster> { doom });
            var done = await Task.WhenAny(task, Task.Delay(TimeSpan.FromSeconds(60)));
            done.Should().Be(task, "the fight ends");
            res = await task;
        }
        finally { DifficultySystem.CurrentDifficulty = difficulty; }
        res.Outcome.Should().BeOneOf(new[] { CombatOutcome.PlayerDied, CombatOutcome.PlayerEscaped });
        ShouldBeEmpty(hero, "a death clears the entries");
    }

    [Fact]
    public void BonusSlots136_ADuel_ClearsBothSides()
    {
        var a = ArmedHero(); var b = ArmedHero();
        var m = typeof(CombatEngine).GetMethod("ScrubTransientCombatState", FS)!;
        m.Invoke(null, new object[] { a }); m.Invoke(null, new object[] { b });
        ShouldBeEmpty(a, "the duel clear"); ShouldBeEmpty(b, "the duel clear");
    }

    [Fact]
    public void BonusSlots136_TheOldGodFight_ClearsTheEntries()
    {
        var hero = ArmedHero();
        typeof(OldGodBossSystem).GetMethod("ClearPlayerModifiers", F)!.Invoke(OldGodBossSystem.Instance, new object[] { hero });
        ShouldBeEmpty(hero, "the Old God fight's clear");
    }

    private static string Src(string rel)
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir != null && !File.Exists(Path.Combine(dir.FullName, "usurper-reloaded.csproj"))) dir = dir.Parent;
        dir.Should().NotBeNull("the repository root holds usurper-reloaded.csproj");
        return File.ReadAllText(Path.Combine(dir!.FullName, rel));
    }

    [Fact]
    public void BonusSlots136_TheWorldBossFight_ClearsAtItsStart_AndTicksTheEntries()
    {
        // The world boss fight needs the online backend; its clear and tick are read from the source.
        var src = Src(Path.Combine("Scripts", "Systems", "WorldBossSystem.cs"));
        int clear = src.IndexOf("player.ClearSlotBonuses();", StringComparison.Ordinal);
        int tick = src.IndexOf("player.TickSlotBonuses();", StringComparison.Ordinal);
        clear.Should().BeGreaterThan(0, "the world boss fight clears the entries as it starts");
        tick.Should().BeGreaterThan(clear, "and ticks them at each round end of the fight");
        src.IndexOf("Reset transient combat buffs", StringComparison.Ordinal).Should().BeLessThan(clear);
    }

    // ---------------------------------------------------------------- the writers through the entries

    internal static SpellSystem.SpellResult CastOk(Character caster, int level)
    {
        for (int i = 0; i < 200; i++)
        {
            caster.Mana = caster.MaxMana;
            var r = SpellSystem.CastSpell(caster, level, null);
            if (r.Success) return r;
        }
        throw new Exception($"spell {level} never succeeded in 200 casts");
    }

    internal static Character Caster(CharacterClass cls, int spell, int level = 60)
    {
        var c = cls == CharacterClass.Magician || cls == CharacterClass.Sage
            ? G.Hero(hp: 100_000, level: level, cls: cls, main: 30, mainType: WeaponType.Staff)   // their spells need a staff
            : G.Hero(hp: 100_000, level: level, cls: cls);
        c.Intelligence = 40; c.BaseIntelligence = 40; c.Wisdom = 40; c.BaseWisdom = 40;
        c.MaxMana = 100_000; c.Mana = 100_000;
        c.Spell = Enumerable.Range(1, 25).Select(slot => new List<bool> { slot == spell }).ToList();
        c.ClearSlotBonuses();
        return c;
    }

    internal static void ApplySpell(G.Rig rig, Character caster, SpellSystem.SpellResult r) =>
        typeof(CombatEngine).GetMethod("ApplySpellEffects", F)!
            .Invoke(rig.Engine, new object?[] { caster, null, r, rig.Result, rig.Monsters, null });

    [Fact]
    public void BonusSlots136_Flametongue_TwiceInARow_DoesNotStack()
    {
        var once = OneApply136Tests.Run("flametongue", CharacterClass.MysticShaman);
        var twice = OneApply136Tests.Run("flametongue", CharacterClass.MysticShaman, casts: 2);
        once.Attack.Should().BeGreaterThan(0);
        twice.Attack.Should().Be(once.Attack, "a recast (cooldown 0) refreshes its entry, it does not add again");
        twice.AttackRounds.Should().Be(once.AttackRounds);
    }

    [Fact]
    public void BonusSlots136_Haste_TwiceInARow_DoesNotStack()
    {
        var mage = Caster(CharacterClass.Magician, 7);
        var rig = G.NewRig(mage, new G.RecordingRandom(136));
        var r = CastOk(mage, 7);
        r.SpecialEffect.Should().Be("haste");
        ApplySpell(rig, mage, r);
        int once = mage.TempAttackBonus;
        once.Should().Be(r.AttackBonus);
        ApplySpell(rig, mage, CastOk(mage, 7));
        mage.TempAttackBonus.Should().BeLessThan(2 * once, "a recast refreshes, it does not add");
        mage.AttackBonusEntries.Should().ContainSingle(e => e.Source == "spell_haste");
    }

    [Theory]
    [InlineData("inspiring_cadence", CharacterClass.Wavecaller)]
    [InlineData("crescendo", CharacterClass.Wavecaller)]
    [InlineData("harmonic_shield", CharacterClass.Wavecaller)]
    [InlineData("oceans_voice", CharacterClass.Wavecaller)]
    [InlineData("borrowed_power", CharacterClass.Cyclebreaker)]
    public void BonusSlots136_TheAddingHandlers_TwiceInARow_DoNotStack(string id, CharacterClass cls)
    {
        var once = OneApply136Tests.Run(id, cls, mates: 1);
        var twice = OneApply136Tests.Run(id, cls, mates: 1, casts: 2);
        (once.Attack + once.Defense + once.MateAttack.Sum() + once.MateDefense.Sum()).Should().BeGreaterThan(0, id);
        twice.Attack.Should().Be(once.Attack, $"{id}: the caster's attack entry refreshes");
        twice.Defense.Should().Be(once.Defense, $"{id}: the caster's defence entry refreshes");
        twice.MateAttack.Should().Equal(once.MateAttack, $"{id}: each teammate's entry refreshes");
        twice.MateDefense.Should().Equal(once.MateDefense, $"{id}: each teammate's entry refreshes");
    }

    [Fact]
    public void BonusSlots136_Rage_OverAHeldDefenceBonus_TakesItsPenaltyOnTop()
    {
        var hero = OneApply136Tests.Caster(CharacterClass.Barbarian);
        hero.ClearSlotBonuses();
        hero.AddSlotBonus(Def, "shield_wall", 300, 3);
        var c = OneApply136Tests.Run("rage", CharacterClass.Barbarian, caster: hero);
        c.Result.DefenseBonus.Should().BeLessThan(0);
        hero.TempDefenseBonus.Should().Be(300 + c.Result.DefenseBonus, "the held 300 stays and rage's penalty comes off it");
        hero.DefenseBonusEntries.Should().Contain(e => e.Source == "shield_wall" && e.Value == 300);
    }

    [Fact]
    public void BonusSlots136_RecklessAttack_OverAHeldDefenceBonus_TakesItsPenaltyOnTop()
    {
        var hero = OneApply136Tests.Caster(CharacterClass.Barbarian);
        hero.ClearSlotBonuses();
        hero.AddSlotBonus(Def, "shield_wall", 300, 3);
        OneApply136Tests.Run("reckless_attack", CharacterClass.Barbarian, caster: hero);
        hero.TempDefenseBonus.Should().Be(280, "300 held, 20 taken off on top");
        hero.TickSlotBonuses();
        hero.TempDefenseBonus.Should().Be(300, "the one round penalty ended; the held bonus is whole again");
    }

    // ---------------------------------------------------------------- the value rows (rule 5)

    private static Monster Frail(string name = "Rat") => new Monster
    {
        Name = name, TierName = name, Level = 1, HP = 1, MaxHP = 1, Strength = 1, Defence = 0, Experience = 1, Gold = 0, IsActive = true,
    };

    private static G.Rig RigFor(Character hero, List<Character>? team = null, params Monster[] foes) =>
        G.NewRig(hero, new G.RecordingRandom(136), team ?? new List<Character>(), foes.Length > 0 ? foes : new[] { G.Dummy() });

    private static T NoHitRoll<T>(Func<T> body)
    {
        bool saved = GameConfig.PlayerHitRollEnabled;
        try { GameConfig.PlayerHitRollEnabled = false; return body(); }
        finally { GameConfig.PlayerHitRollEnabled = saved; }
    }

    [Fact]
    public void BonusSlots136_Focus_ReachesTheNextSwing()
    {
        ClassAbilitySystem.GetAbility("focus")!.Duration.Should().Be(2);
        var hero = Fresh(); hero.Class = CharacterClass.Warrior;
        var rig = RigFor(hero);
        NoHitRoll(() => { Apply(rig, hero, "focus"); return 0; });
        hero.TempAttackBonus.Should().BeGreaterThan(0);
        EndRound(rig);
        hero.TempAttackBonus.Should().BeGreaterThan(0, "the bonus is still there for the swing of the next round");
        EndRound(rig);
        hero.TempAttackBonus.Should().Be(0);
    }

    [Fact]
    public void BonusSlots136_TimeStop_ReachesTheNextSwing()
    {
        var mage = Caster(CharacterClass.Magician, 18, level: 100);
        var rig = RigFor(mage);
        var r = CastOk(mage, 18);
        r.Duration.Should().Be(2);
        ApplySpell(rig, mage, r);
        mage.TempAttackBonus.Should().Be(r.AttackBonus);
        EndRound(rig);
        mage.TempAttackBonus.Should().Be(r.AttackBonus, "Time Stop's bonus is there for the next round's swing");
    }

    [Theory]
    [InlineData("temporal_feint", CharacterClass.Cyclebreaker)]
    [InlineData("umbral_step", CharacterClass.Abysswarden)]
    public void BonusSlots136_FeintAndUmbralStep_HoldNo999_AndStillHitForSure(string id, CharacterClass cls)
    {
        var hero = Fresh(); hero.Class = cls;
        hero.AddSlotBonus(Atk, "battle_cry", 40, 3);
        var rig = RigFor(hero);
        NoHitRoll(() => { Apply(rig, hero, id); return 0; });
        hero.TempAttackBonus.Should().Be(40, $"{id} adds no +999; the held bonus shows");
        hero.AttackBonusEntries.Should().NotContain(e => e.Value >= 999, id);
        hero.HasStatus(StatusEffect.Hidden).Should().BeTrue($"{id}: Hidden gives the sure hit and the crit");
        EndRound(rig);
        hero.TempAttackBonus.Should().Be(40, $"{id}: its round end no longer zeroes the held bonus");
    }

    [Fact]
    public void BonusSlots136_ConsumeSoul_GrowsBy5AKill_AndSurvivesALargerTimedBonus()
    {
        var hero = Fresh(); hero.Class = CharacterClass.Abysswarden; hero.Level = 100;
        hero.CurrentCombatStamina = 10_000;
        var rig = RigFor(hero, null, Frail("Rat1"), Frail("Rat2"));
        NoHitRoll(() =>
        {
            foreach (var foe in rig.Monsters.ToList())
            {
                var r = ClassAbilitySystem.UseAbility(hero, "consume_soul", new Random(136));
                ((Task)typeof(CombatEngine).GetMethod("ApplyAbilityEffectsMultiMonster", F)!
                    .Invoke(rig.Engine, new object?[] { hero, foe, rig.Monsters, r, rig.Result })!).GetAwaiter().GetResult();
            }
            return 0;
        });
        rig.Monsters.Should().OnlyContain(m => m.HP <= 0);
        hero.SlotBonusValue(Atk, "consume_soul").Should().Be(10, "+5 for each of the two kills");
        hero.AddSlotBonus(Atk, "battle_cry", 200, 1);
        hero.TempAttackBonus.Should().Be(200);
        hero.TickSlotBonuses();
        hero.TempAttackBonus.Should().Be(10, "the larger timed bonus ended; Consume Soul's whole fight entry shows again");
    }

    [Fact]
    public void BonusSlots136_Wish_SurvivesATimedBonusEnding()
    {
        var mage = Caster(CharacterClass.Magician, 25, level: 100);
        mage.Strength = 300; mage.Defence = 120;
        var rig = RigFor(mage);
        var r = CastOk(mage, 25);
        r.SpecialEffect.Should().Be("wish");
        ApplySpell(rig, mage, r);
        mage.TempAttackBonus.Should().Be(300);
        mage.TempAttackBonusDuration.Should().Be(999, "one whole fight entry");
        mage.TempDefenseBonus.Should().Be(120);
        mage.AddSlotBonus(Atk, "battle_cry", 500, 1);
        EndRound(rig);
        mage.TempAttackBonus.Should().Be(300, "a timed bonus ending leaves Wish's entry");
        mage.TempDefenseBonus.Should().Be(120);
    }

    [Fact]
    public void BonusSlots136_Haste_TwoDoubledTurns_AndItsBonusLastsThem()
    {
        var mage = Caster(CharacterClass.Magician, 7);
        var rig = RigFor(mage);
        var r = CastOk(mage, 7);
        r.Duration.Should().Be(3);
        ApplySpell(rig, mage, r);          // round 1: the cast is the turn
        EndRound(rig);
        int doubled = 0;
        for (int round = 2; round <= 5; round++)
        {
            mage.ProcessStatusEffects();   // the round's start
            bool hasted = mage.HasStatus(StatusEffect.Haste);
            if (hasted) doubled++;
            (mage.TempAttackBonus > 0).Should().Be(hasted, $"round {round}: the bonus lasts exactly the hasted turns");
            EndRound(rig);
        }
        doubled.Should().Be(2, "two doubled turns");
        mage.TempAttackBonus.Should().Be(0);
    }

    [Fact]
    public void BonusSlots136_OfferFlesh_BelowAQuarter_DoublesTheScaledBonus_AndTheLineShowsIt()
    {
        var low = OneApply136Tests.Caster(CharacterClass.Voidreaver);
        low.MaxHP = 1000; low.HP = 300;
        var c = OneApply136Tests.Run("offer_flesh", CharacterClass.Voidreaver, caster: low);
        low.HP.Should().BeLessThan(250);
        c.Attack.Should().Be(2 * c.Result.AttackBonus, "twice the scaled bonus, not a flat +60 on top");
        c.Shown.Should().Contain($"(+{2 * c.Result.AttackBonus} ATK total)");
        var high = OneApply136Tests.Caster(CharacterClass.Voidreaver);
        high.MaxHP = 1000; high.HP = 1000;
        var h = OneApply136Tests.Run("offer_flesh", CharacterClass.Voidreaver, caster: high);
        h.Attack.Should().Be(h.Result.AttackBonus, "above a quarter the scaled bonus alone");
    }

    [Fact]
    public void BonusSlots136_AvatarOfDestruction_HitGrowsWithStrength_NotCharisma()
    {
        int Hit(long str, long cha)
        {
            var b = G.Hero(hp: 100_000, strength: str, level: 90, cls: CharacterClass.Barbarian);
            b.Charisma = cha; b.BaseCharisma = cha;
            var r = ClassAbilitySystem.UseAbility(b, "avatar_of_destruction", new Random(11));
            r.Success.Should().BeTrue();
            return r.Damage;
        }
        Hit(150, 10).Should().BeGreaterThan(Hit(50, 10), "more Strength, a bigger hit");
        Hit(50, 150).Should().Be(Hit(50, 10), "Charisma does not change the hit");
    }

    [Fact]
    public void BonusSlots136_BerserkerRage_FromTheAbility_EndsAfterItsRounds()
    {
        var hero = Fresh(); hero.Class = CharacterClass.Barbarian;
        var rig = RigFor(hero);
        NoHitRoll(() => { Apply(rig, hero, "rage"); return 0; });
        hero.IsRaging.Should().BeTrue();
        hero.HasStatus(StatusEffect.Raging).Should().BeTrue("the Raging status, as the [G] key gives");
        hero.TempDefenseBonus.Should().BeLessThan(0);
        for (int i = 0; i < 5; i++) { hero.ProcessStatusEffects(); EndRound(rig); }
        hero.IsRaging.Should().BeFalse("the rage ends with its 5 rounds");
        hero.TempDefenseBonus.Should().Be(0, "and so does its penalty");
    }

    [Fact]
    public void BonusSlots136_Bloodlust_EndsAfter5Rounds()
    {
        var hero = Fresh(); hero.Class = CharacterClass.Barbarian;
        var rig = RigFor(hero);
        NoHitRoll(() => { Apply(rig, hero, "bloodlust"); return 0; });
        hero.HasBloodlust.Should().BeTrue();
        for (int i = 0; i < 4; i++) EndRound(rig);
        hero.HasBloodlust.Should().BeTrue("4 rounds in");
        EndRound(rig);
        hero.HasBloodlust.Should().BeFalse("the template's 5 rounds, not the whole fight");
    }

    [Fact]
    public void BonusSlots136_Bloodlust_HealsOnItsHoldersOwnAbilityKill()
    {
        var hero = Fresh(); hero.Class = CharacterClass.Barbarian; hero.MaxHP = 100_000; hero.HP = 1000;
        hero.CurrentCombatStamina = 10_000;
        var rig = RigFor(hero, null, Frail());
        NoHitRoll(() => { Apply(rig, hero, "bloodlust"); return 0; });
        long before = hero.HP;
        NoHitRoll(() => { Apply(rig, hero, "reckless_attack"); return 0; });
        rig.Monsters[0].HP.Should().BeLessOrEqualTo(0);
        hero.HP.Should().Be(before + hero.Level * 2 + 20, "a single target ability kill heals the holder");
    }

    [Fact]
    public void BonusSlots136_Bloodlust_DoesNotHealOnATeammatesSwing()
    {
        var hero = Fresh(); hero.Class = CharacterClass.Barbarian; hero.MaxHP = 100_000; hero.HP = 1000;
        var mate = G.Mate("Ally");
        var rig = RigFor(hero, new List<Character> { mate }, Frail());
        NoHitRoll(() => { Apply(rig, hero, "bloodlust"); return 0; });
        long before = hero.HP;
        ((Task<bool>)typeof(CombatEngine).GetMethod("ApplySingleMonsterDamage", F)!
            .Invoke(rig.Engine, new object?[] { rig.Monsters[0], 50L, rig.Result, "attack", mate, false })!).GetAwaiter().GetResult();
        rig.Monsters[0].HP.Should().BeLessOrEqualTo(0, "the teammate's swing kills");
        hero.HP.Should().Be(before, "a teammate's kill does not heal the holder");
        var own = Frail("Rat2"); rig.Monsters.Add(own);
        ((Task<bool>)typeof(CombatEngine).GetMethod("ApplySingleMonsterDamage", F)!
            .Invoke(rig.Engine, new object?[] { own, 50L, rig.Result, "attack", hero, false })!).GetAwaiter().GetResult();
        hero.HP.Should().Be(before + hero.Level * 2 + 20, "the holder's own swing heals");
    }

    [Fact]
    public void BonusSlots136_TheChangedRows_Fit79Columns()
    {
        foreach (var lang in new[] { "en", "hu" })
            using (Loc.RenderLanguage(lang))
                foreach (var row in new[]
                {
                    Loc.Get("spell.magician.7.desc"), Loc.Get("spell.magician.18.desc"),
                    Loc.Get("combat.ability_offer_flesh_desperate", 99999, 9999),
                })
                    row.Length.Should().BeLessOrEqualTo(79, $"{lang}: {row}");
    }
}
