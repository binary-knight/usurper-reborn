using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using FluentAssertions;
using UsurperRemake;
using UsurperRemake.Data;
using UsurperRemake.Systems;
using UsurperRemake.UI;
using Xunit;
using G = UsurperReborn.Tests.HitRoll128Golden;

namespace UsurperReborn.Tests;

/// <summary>
/// 1.2.8 difficulty pass, DESIGN item 9 (rows G1 to G17 and G19 to G22; G18, the Warded elite, is not in this
/// piece) and the rows of the roll line: a player's physical attack on a monster rolls to hit
/// (TrainingSystem.RollPlayerHit) behind GameConfig.PlayerHitRollEnabled (default on), the roll is shown, and
/// with the switch off every fight draws the numbers it drew before the piece (the golden lists below were
/// written by Tests/HitRoll128Golden.cs on release-1.2.8 at 621721d6).
/// </summary>
[Collection("SharedGameSingletons")]
public class HitRoll128Tests
{
    private const BindingFlags F = BindingFlags.NonPublic | BindingFlags.Instance;
    private static readonly string[] Langs = { "en", "es", "fr", "hu", "it" };
    private static readonly string[] RollKeys = { "combat.hit_roll", "combat.hit_roll_nat1", "combat.hit_roll_nat20" };

    // ---------------------------------------------------------------- harness

    private static T WithSwitch<T>(bool on, Func<T> body)
    {
        bool saved = GameConfig.PlayerHitRollEnabled;
        string lang = GameConfig.Language;
        try { GameConfig.PlayerHitRollEnabled = on; GameConfig.Language = "en"; return body(); }
        finally { GameConfig.PlayerHitRollEnabled = saved; GameConfig.Language = lang; }
    }

    private static T In<T>(string lang, Func<T> body)
    {
        string saved = GameConfig.Language;
        try { GameConfig.Language = lang; return body(); }
        finally { GameConfig.Language = saved; }
    }

    /// <summary>A seeded random whose twenty-sided die (Next(1, 21)) can be set: Die gets the number of the
    /// die (0 for the first) and returns its face, or 0 to leave it as drawn. Every die is written down.</summary>
    private sealed class DieRandom : Random
    {
        public Func<int, int>? Die;
        public readonly List<int> Naturals = new();
        public readonly List<string> Calls = new();
        public DieRandom(int seed = 128, Func<int, int>? die = null) : base(seed) { Die = die; }
        public override int Next() { int v = base.Next(); Calls.Add("Next()"); return v; }
        public override int Next(int maxValue) { int v = base.Next(maxValue); Calls.Add($"Next({maxValue})"); return v; }
        public override double NextDouble() { double v = base.NextDouble(); Calls.Add("NextDouble()"); return v; }
        public override int Next(int minValue, int maxValue)
        {
            int v = base.Next(minValue, maxValue);
            if (minValue == 1 && maxValue == 21)
            {
                int set = Die?.Invoke(Naturals.Count) ?? 0;
                if (set > 0) v = set;
                Naturals.Add(v);
            }
            Calls.Add($"Next({minValue},{maxValue})");
            return v;
        }
    }

    private static DieRandom Always(int face) => new(128, _ => face);
    private static DieRandom Faces(params int[] faces) => new(128, i => i < faces.Length ? faces[i] : faces[^1]);

    /// <summary>A level 50 fighter. Its level keeps every other draw of a swing off the range of the die
    /// (the swing's variation is Next(1, 25) from level 44 on).</summary>
    private static Character Fighter(CharacterClass cls = CharacterClass.Warrior, long strength = 300, long dexterity = 30, int level = 50,
        int? main = null, int? off = null, WeaponType mainType = WeaponType.Sword, string name = "Tester")
        => G.Hero(50_000, strength, level, cls, dexterity, main, off, mainType, name);

    /// <summary>A foe at the fighter's level whose armour class is out of reach: the clamp sets the roll at 3.</summary>
    private static Monster AtLevel() => G.Dummy(50, 600);

    /// <summary>A foe 10 levels above the fighter, out of reach: the clamp sets the roll at 8.</summary>
    private static Monster Above() => G.Dummy(60, 600);

    /// <summary>A foe 10 levels above whose Defence leaves the attacker needing exactly `needed`, inside the window.</summary>
    private static Monster Needing(Character attacker, int needed)
    {
        for (int defence = 0; defence <= 3000; defence += 10)
        {
            var m = G.Dummy(attacker.Level + 10, defence);
            if (TrainingSystem.PlayerHitNeeded(attacker, m) == needed && TrainingSystem.PlayerHitTargetAC(attacker, m) - TrainingSystem.PlayerHitModifier(attacker) == needed) return m;
        }
        throw new InvalidOperationException("no Defence gives that roll");
    }

    private static string Plain(string transcript) => Regex.Replace(transcript, "\u001b\\[[0-9;?]*[A-Za-z]", "").Replace("\r", "");
    private static List<string> Rows(string transcript) => Plain(transcript).Split('\n').Select(r => r.TrimEnd()).Where(r => r.Length > 0).ToList();

    private sealed class Seen
    {
        public G.Rig Rig = null!;
        public List<string> Rows = new();
        public long Damage;
        public int RollRows => Rows.Count(r => r.StartsWith("[You roll") || Regex.IsMatch(r, @"^\[.+ rolls "));
        public int MissRows => Rows.Count(r => r.StartsWith("You miss ") || r.StartsWith("CRITICAL MISS!") || Regex.IsMatch(r, @"^.+ misses .+!$"));
        public double HitRate => RollRows == 0 ? double.NaN : 1.0 - MissRows / (double)RollRows;
    }

    /// <summary>The action `times` times, in English, with the switch as given.</summary>
    private static Seen Do(Character hero, CombatAction action, Monster foe, Random random, int times = 1, bool on = true,
        Action<G.Rig, int>? before = null, Action<G.Rig>? setup = null, List<Character>? team = null, Character? teammateTurn = null)
        => WithSwitch(on, () =>
        {
            var rig = G.NewRig(hero, random, team, foe);
            setup?.Invoke(rig);
            long start = foe.HP;
            for (int i = 0; i < times; i++)
            {
                before?.Invoke(rig, i);
                if (teammateTurn != null) Swings(rig, teammateTurn);
                else G.Act(rig, action);
            }
            return new Seen { Rig = rig, Rows = Rows(rig.Shown()), Damage = start - foe.HP };
        });

    private static readonly CombatAction Swing = G.A(CombatActionType.Attack);

    /// <summary>A teammate's turn with no stamina left for an ability: it swings.</summary>
    private static void Swings(G.Rig rig, Character teammate)
    {
        teammate.CurrentCombatStamina = 0;
        G.TeammateTurn(rig, teammate);
    }

    private static double PureRate(Character attacker, Monster target, int rolls = 20_000, int seed = 128, int neededBonus = 0)
    {
        var random = new Random(seed);
        int hits = 0;
        for (int i = 0; i < rolls; i++)
            if (TrainingSystem.RollPlayerHit(attacker, target, 0, random, neededBonus).Hit) hits++;
        return hits / (double)rolls;
    }

    private static string RepoRoot()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir != null && !File.Exists(Path.Combine(dir.FullName, "usurper-reloaded.csproj"))) dir = dir.Parent;
        return dir!.FullName;
    }

    // ---------------------------------------------------------------- G1 to G5, G15: the roll itself

    [Fact]
    public void G1_Dexterity_EntersTheModifier()
    {
        var clumsy = Fighter(dexterity: 10);
        var deft = Fighter(dexterity: 250);
        TrainingSystem.PlayerHitModifier(deft).Should().BeGreaterThan(TrainingSystem.PlayerHitModifier(clumsy), "Dexterity is in the diminished sum");
        (TrainingSystem.PlayerHitModifier(deft) - TrainingSystem.PlayerHitModifier(clumsy)).Should().Be((250 - 10) / 4 / 3, "(DEX - 10) / 4 at a third above +6");
        var foe = Needing(clumsy, 7);
        TrainingSystem.PlayerHitNeeded(deft, foe).Should().BeLessThan(7);
        double low = PureRate(clumsy, foe), high = PureRate(deft, foe);
        low.Should().BeInRange(0.69, 0.71, "a 7 or more on the same seeded rolls");
        high.Should().BeGreaterThan(low + 0.04, "the same rolls hit more often with more Dexterity");
    }

    [Fact]
    public void G2_AtLevel_TheClampHoldsTheHitRateAt90Percent()
    {
        var hero = Fighter();
        var foe = AtLevel();
        (TrainingSystem.PlayerHitTargetAC(hero, foe) - TrainingSystem.PlayerHitModifier(hero)).Should().BeGreaterThan(20, "unclamped, only a natural 20 would hit");
        TrainingSystem.PlayerHitNeeded(hero, foe).Should().Be(3);
        PureRate(hero, foe).Should().BeInRange(0.89, 0.91);
        // five levels above is still at level; a generated floor-50 monster against the level 50 fighter too
        TrainingSystem.PlayerHitNeeded(hero, G.Dummy(55, 600)).Should().Be(3);
        var generated = MonsterGenerator.GenerateMonster(50, false, false, new Random(5));
        TrainingSystem.PlayerHitNeeded(hero, generated).Should().BeInRange(2, 3);
    }

    [Fact]
    public void G3_ATargetMoreThanFiveLevelsAbove_Is65Percent()
    {
        var hero = Fighter(level: 20, strength: 60);
        var foe = G.Dummy(30, 600);
        TrainingSystem.PlayerHitNeeded(hero, foe).Should().Be(8);
        PureRate(hero, foe).Should().BeInRange(0.64, 0.66);
        TrainingSystem.PlayerHitNeeded(hero, G.Dummy(26, 600)).Should().Be(8, "six levels above is past the at-level gap");
        TrainingSystem.PlayerHitNeeded(hero, G.Dummy(25, 600)).Should().Be(3);
    }

    [Fact]
    public void G4_ANatural1_AlwaysMisses()
    {
        var hero = Fighter(strength: 2000);
        var foe = G.Dummy(1, 0);
        TrainingSystem.PlayerHitModifier(hero).Should().BeGreaterThan(40);
        (TrainingSystem.PlayerHitTargetAC(hero, foe) - TrainingSystem.PlayerHitModifier(hero)).Should().BeLessThan(-20, "1 plus the modifier beats the armour class by far");
        TrainingSystem.PlayerHitNeeded(hero, foe).Should().Be(2, "never below 2");
        var one = TrainingSystem.RollPlayerHit(hero, foe, 0, Always(1));
        one.Natural.Should().Be(1);
        one.IsNatural1.Should().BeTrue();
        one.Hit.Should().BeFalse("a natural 1 always misses");
        TrainingSystem.RollPlayerHit(hero, foe, 0, Always(2)).Hit.Should().BeTrue();
    }

    [Fact]
    public void G5_ANatural20_AlwaysHits()
    {
        var hero = Fighter(strength: 10, dexterity: 10, level: 1);
        var foe = G.Dummy(100, 3000);
        var twenty = TrainingSystem.RollPlayerHit(hero, foe, 20, Always(20));
        twenty.Needed.Should().BeGreaterThan(20, "with the armour bonus the needed roll is past the die");
        twenty.IsNatural20.Should().BeTrue();
        twenty.Hit.Should().BeTrue("a natural 20 always hits");
        TrainingSystem.RollPlayerHit(hero, foe, 20, Always(19)).Hit.Should().BeFalse();
    }

    [Fact]
    public void G5_TheHitDiesNatural20_IsTheCriticalHit_AndNoSecondDieIsDrawn()
    {
        var random = new DieRandom(128);
        var seen = Do(Fighter(), Swing, AtLevel(), random, times: 600);
        seen.RollRows.Should().Be(600);
        random.Naturals.Count.Should().Be(600, "one twenty-sided die a swing: the hit die");
        int twenties = random.Naturals.Count(n => n == 20);
        twenties.Should().BeGreaterThan(10);
        seen.Rows.Count(r => r == "CRITICAL HIT!").Should().Be(twenties, "a critical hit on the hit die's 20 and only there");
        seen.Rows.Count(r => r == "[You roll a natural 20: always a hit]").Should().Be(twenties);
        // switch off: the separate die, one a swing, and no roll line
        var offRandom = new DieRandom(128);
        var off = Do(Fighter(), Swing, AtLevel(), offRandom, times: 600, on: false);
        off.RollRows.Should().Be(0);
        offRandom.Naturals.Count.Should().Be(600, "off, the separate critical die is drawn as before");
        off.Rows.Count(r => r == "CRITICAL HIT!").Should().Be(offRandom.Naturals.Count(n => n == 20));
    }

    [Fact]
    public void G15_Rage_Stance_Blessings_AndBlindness_MoveTheArmourClass()
    {
        var foe = G.Dummy(60, 100);
        int Ac(Action<Character> with) { var c = Fighter(); with(c); return TrainingSystem.PlayerHitTargetAC(c, foe); }
        int plain = Ac(_ => { });
        plain.Should().Be(10 + 60 / 2 + 100 / 10);
        Ac(c => c.IsRaging = true).Should().Be(plain + 4);
        Ac(c => c.ApplyStatus(StatusEffect.PowerStance, 1)).Should().Be(plain + 2);
        Ac(c => c.ApplyStatus(StatusEffect.Blessed, 3)).Should().Be(plain - 2);
        Ac(c => c.ApplyStatus(StatusEffect.RoyalBlessing, 3)).Should().Be(plain - 2);
        Ac(c => c.Blind = true).Should().Be(plain + 6);
        Ac(c => c.ApplyStatus(StatusEffect.Blinded, 3)).Should().Be(plain + 6);
        TrainingSystem.PlayerHitTargetAC(Fighter(), foe, 3).Should().Be(plain + 3, "the armour bonus parameter");

        // a blind attacker hits less often, inside the window
        var sighted = Fighter();
        var target = Needing(sighted, 2);
        var blind = Fighter();
        blind.Blind = true;
        TrainingSystem.PlayerHitNeeded(blind, target).Should().Be(8);
        PureRate(sighted, target).Should().BeInRange(0.94, 0.96);
        PureRate(blind, target).Should().BeInRange(0.64, 0.66);
    }

    // ---------------------------------------------------------------- G6, G7: a miss, and the off hand

    private static int LeechBlade() => EquipmentDatabase.RegisterDynamic(new Equipment
    {
        Name = "Test Leech Blade", Slot = EquipmentSlot.MainHand, WeaponType = WeaponType.Sword, Handedness = WeaponHandedness.OneHanded,
        WeaponPower = 60, LifeSteal = 25, MinLevel = 1, Rarity = EquipmentRarity.Common,
    });

    private static Character Leech()
    {
        var hero = Fighter();
        hero.EquippedItems[EquipmentSlot.MainHand] = LeechBlade();
        hero.RecalculateStats();
        hero.HP = hero.MaxHP / 2;
        hero.ShamanEnchantType = 1;
        hero.ShamanEnchantRounds = 99;
        hero.ShamanEnchantPower = 50;
        return hero;
    }

    [Fact]
    public void G6_AMiss_DealsNothing_AndFiresNoOnHitEffect()
    {
        var miss = Leech();
        long hp = miss.HP;
        var missed = Do(miss, Swing, AtLevel(), Always(2), times: 20);
        int swings = missed.Rows.Count(r => r.StartsWith("You attack"));
        swings.Should().BeGreaterThanOrEqualTo(20);
        missed.RollRows.Should().Be(swings);
        missed.Rows.Count(r => r == "You miss Wolf!").Should().Be(swings, "a 2 against a needed 3");
        missed.Damage.Should().Be(0, "a miss deals nothing");
        miss.HP.Should().Be(hp, "no life is stolen on a miss");
        missed.Rows.Should().NotContain(r => r.Contains("Flametongue"), "the weapon enchant does not fire on a miss");

        var hit = Leech();
        var landed = Do(hit, Swing, AtLevel(), Always(10), times: 20);
        landed.MissRows.Should().Be(0);
        landed.Damage.Should().BeGreaterThan(0);
        hit.HP.Should().BeGreaterThan(hp, "the same weapon steals life on a hit");
        landed.Rows.Count(r => r.Contains("Flametongue")).Should().Be(landed.Rows.Count(r => r.StartsWith("You attack")));
    }

    [Fact]
    public void G7_TheOffHandSwing_RollsOnItsOwn()
    {
        var hero = Fighter(CharacterClass.Assassin, main: 40, off: 35);
        hero.IsDualWielding.Should().BeTrue();
        // every main-hand die a 10, every off-hand die a 2: the rig tells the hands apart by the line before the roll
        var random = new DieRandom(128);
        var seen = WithSwitch(true, () =>
        {
            var foe = AtLevel();
            var rig = G.NewRig(hero, random, null, foe);
            random.Die = _ => Rows(rig.Shown()).Last().StartsWith("Off-hand") ? 2 : 10;
            long start = foe.HP;
            for (int i = 0; i < 20; i++) G.Act(rig, Swing);
            return new Seen { Rig = rig, Rows = Rows(rig.Shown()), Damage = start - foe.HP };
        });
        var rows = seen.Rows;
        int mains = rows.Count(r => r.StartsWith("You attack")), offs = rows.Count(r => r.StartsWith("Off-hand"));
        mains.Should().BeGreaterThanOrEqualTo(20);
        offs.Should().BeGreaterThanOrEqualTo(20);
        seen.RollRows.Should().Be(mains + offs, "each swing of the round rolls its own die");
        rows.Count(r => r == "You miss Wolf!").Should().Be(offs, "the main hand hits and the off hand misses in the same round");
        for (int i = 0; i < rows.Count - 1; i++)
        {
            if (rows[i].StartsWith("You attack")) rows[i + 1].Should().Be("[You roll 10; you need 3 or more]");
            if (rows[i].StartsWith("Off-hand")) { rows[i + 1].Should().Be("[You roll 2; you need 3 or more]"); rows[i + 2].Should().Be("You miss Wolf!"); }
        }
        seen.Damage.Should().BeGreaterThan(0);
    }

    [Fact]
    public void G7_TheOffHandFollowUps_OfPowerAttackAndOfAnAbility_RollOnTheirOwn()
    {
        foreach (var action in new[] { G.A(CombatActionType.PowerAttack), G.A(CombatActionType.UseAbility, "execute") })
        {
            var hero = Fighter(CharacterClass.Assassin, main: 40, off: 35);
            // first die (the main strike) misses, second (the off hand) hits, and so on
            var random = new DieRandom(128, i => i % 2 == 0 ? 2 : 10);
            var seen = Do(hero, action, AtLevel(), random, times: 10, before: (r, _) => r.Hero.ActiveStatuses.Remove(StatusEffect.PowerStance));
            random.Naturals.Count.Should().Be(20, "{0}: the strike and its off-hand follow-up each roll", action.Type);
            seen.RollRows.Should().Be(20);
            seen.MissRows.Should().Be(10, "the main strike misses and the off hand lands");
            seen.Damage.Should().BeGreaterThan(0, "the off hand follows a main strike that missed");
        }
    }

    // ---------------------------------------------------------------- G8, G9: Power Attack and Precise Strike

    [Fact]
    public void G8_PowerAttack_KeepsItsStancePlus2()
    {
        var foe = Needing(Fighter(), 5);
        var basic = Do(Fighter(), Swing, foe, new Random(128), times: 4000);
        var power = Do(Fighter(), G.A(CombatActionType.PowerAttack), Needing(Fighter(), 5), new Random(128), times: 4000);
        basic.RollRows.Should().Be(4000);
        power.RollRows.Should().Be(4000);
        basic.Rows.Should().Contain(r => r.EndsWith("you need 5 or more]"));
        power.Rows.Should().Contain(r => r.EndsWith("you need 7 or more]"), "the power stance adds 2 to the armour class");
        basic.HitRate.Should().BeInRange(0.78, 0.82);
        power.HitRate.Should().BeInRange(0.68, 0.72, "below the basic rate");
    }

    [Fact]
    public void G9_PreciseStrike_Needs4Less()
    {
        var basic = Do(Fighter(), Swing, Needing(Fighter(), 7), new Random(128), times: 4000);
        var precise = Do(Fighter(), G.A(CombatActionType.PreciseStrike), Needing(Fighter(), 7), new Random(128), times: 4000);
        precise.RollRows.Should().Be(4000);
        precise.Rows.Should().Contain(r => r.EndsWith("you need 3 or more]"));
        basic.HitRate.Should().BeInRange(0.68, 0.72);
        precise.HitRate.Should().BeInRange(0.88, 0.92, "above the basic rate");
        // never below 2, and at the clamp the 4 comes off the clamped roll
        TrainingSystem.PlayerHitNeeded(Fighter(), AtLevel(), 0, GameConfig.PreciseStrikeNeededBonus).Should().Be(2);
        TrainingSystem.PlayerHitNeeded(Fighter(), Above(), 0, GameConfig.PreciseStrikeNeededBonus).Should().Be(4);
    }

    [Fact]
    public void G9_PreciseStrike_TheDefenceCut_IsReplacedByTheRoll_OnlyWhileTheSwitchIsOn()
    {
        // the same seeded damage draws: on, the full Defence is taken off; off, three quarters of it as before
        // FlatRandom: the Defence roll of Next(0, 400 / 8) is 25, so the Defence taken off is 425, or 318 with the cut
        long Damage(bool on) => Do(Fighter(strength: 2000), G.A(CombatActionType.PreciseStrike), G.Dummy(50, 400), new FlatRandom(10), times: 1, on: on).Damage;
        long off = Damage(false), onDamage = Damage(true);
        onDamage.Should().BeGreaterThan(1000);
        (off - onDamage).Should().Be(425 - 318, "on, the full Defence is taken off; off, three quarters of it");
    }

    // ---------------------------------------------------------------- G10 to G13: who rolls

    [Theory]
    [InlineData("power_strike", CharacterClass.Warrior)]
    [InlineData("execute", CharacterClass.Warrior)]
    [InlineData("backstab", CharacterClass.Assassin)]
    public void G10_APhysicalSingleTargetAbility_Rolls(string abilityId, CharacterClass cls)
    {
        var action = G.A(CombatActionType.UseAbility, abilityId);
        var seeded = Do(Fighter(cls), action, AtLevel(), new Random(128), times: 1000);
        seeded.RollRows.Should().Be(1000, "one roll an ability");
        seeded.HitRate.Should().BeInRange(0.88, 0.92, "misses occur, at the at-level rate");

        var hero = Fighter(cls);
        var missed = Do(hero, action, AtLevel(), Always(2), times: 10);
        missed.Rows.Count(r => r == "You miss Wolf!").Should().Be(10);
        missed.Damage.Should().Be(0, "a missed ability deals nothing");
        missed.Rows.Should().NotContain(r => r.StartsWith("You deal "));
        hero.CurrentCombatStamina.Should().BeLessThan(500, "the stamina is spent all the same");
    }

    [Fact]
    public void G10_AMissedAbility_DoesNothing_NoHealingNoBuffNoEffect()
    {
        // Execute's effect is extra damage to a wounded foe; a stun or a heal would sit in the same places
        var foe = AtLevel();
        foe.HP = foe.MaxHP / 10;
        var hero = Fighter();
        var missed = Do(hero, G.A(CombatActionType.UseAbility, "execute"), foe, Always(2), times: 5);
        missed.Damage.Should().Be(0);
        foe.Stunned.Should().BeFalse();
        hero.TempAttackBonus.Should().Be(0);
        var ability = ClassAbilitySystem.GetAbility("execute")!;
        var result = new ClassAbilityResult { AbilityUsed = ability, Damage = 100, SpecialEffect = "execute" };
        CombatEngine.AbilityRollsToHit(ability, result, false).Should().BeTrue();
        CombatEngine.AbilityRollsToHit(ability, new ClassAbilityResult { AbilityUsed = ability, Damage = 0, SpecialEffect = "execute" }, false).Should().BeFalse("no damage, no roll");
        CombatEngine.AbilityRollsToHit(ability, result, true).Should().BeFalse("an effect with its own damage handler is area or prestige magic");
    }

    [Theory]
    [InlineData("whirlwind", CharacterClass.Warrior, 60)]        // area
    [InlineData("precise_shot", CharacterClass.Ranger, 50)]      // "a guaranteed hit"
    public void G11_AreaAndCertainAbilities_DoNotRoll(string abilityId, CharacterClass cls, int level)
    {
        var hero = Fighter(cls, level: level, main: cls == CharacterClass.Ranger ? 30 : null, mainType: cls == CharacterClass.Ranger ? WeaponType.Bow : WeaponType.Sword);
        var random = new DieRandom(128, _ => 1);
        var seen = Do(hero, G.A(CombatActionType.UseAbility, abilityId), G.Dummy(level, 600), random, times: 200);
        seen.RollRows.Should().Be(0, "never a roll line");
        seen.MissRows.Should().Be(0, "never a miss line");
        random.Naturals.Should().BeEmpty("no hit die is drawn");
        seen.Damage.Should().BeGreaterThan(0);
    }

    [Fact]
    public void G11_MagicalAreaAndMentalEffects_AreLeftOut_ByTheTest()
    {
        var attack = ClassAbilitySystem.GetAbility("power_strike")!;
        bool Rolls(string effect) => CombatEngine.AbilityRollsToHit(attack, new ClassAbilityResult { AbilityUsed = attack, Damage = 50, SpecialEffect = effect }, false);
        foreach (string physical in new[] { "", "execute", "backstab", "last_stand", "armor_pierce", "shield_bash", "stun", "poison", "multi_hit", "lifesteal_20" })
            Rolls(physical).Should().BeTrue("{0} is a physical strike", physical);
        foreach (string not in new[] { "aoe", "aoe_holy", "aoe_confusion", "crescendo_aoe", "holy", "fire", "frost", "lightning", "shadow", "void", "psychic", "magic", "charm", "fear", "confusion", "distract", "drain", "guaranteed_hit" })
            Rolls(not).Should().BeFalse("{0} is area, magical, mental or certain", not);
        // a song, a buff and a heal are not attacks
        foreach (var ability in new[] { ClassAbilitySystem.GetAbility("shield_wall")!, ClassAbilitySystem.GetAbility("battle_cry")! })
            CombatEngine.AbilityRollsToHit(ability, new ClassAbilityResult { AbilityUsed = ability, Damage = 50 }, false).Should().BeFalse();
        foreach (var song in ClassAbilitySystem.GetAllAbilities().Where(a => a.SpecialEffect == "party_song"))
            CombatEngine.AbilityRollsToHit(song, new ClassAbilityResult { AbilityUsed = song, Damage = 50, SpecialEffect = song.SpecialEffect }, false).Should().BeFalse("{0}", song.Id);
    }

    private static Character Caster()
    {
        var hero = Fighter(CharacterClass.Magician, main: 30, mainType: WeaponType.Staff);
        hero.Intelligence = hero.BaseIntelligence = 300;
        hero.Mana = hero.MaxMana = 1_000_000;
        return hero;
    }

    [Fact]
    public void G12_Spells_DoNotRollToHit()
    {
        List<string> Cast(bool on, out Seen seen)
        {
            var random = new DieRandom(128);
            var hero = Caster();
            seen = Do(hero, new CombatAction { Type = CombatActionType.CastSpell, SpellIndex = 1, TargetIndex = 0 }, AtLevel(), random, times: 200, on: on,
                before: (r, _) => r.Hero.Mana = r.Hero.MaxMana);
            return random.Calls;
        }
        var onCalls = Cast(true, out var onSeen);
        var offCalls = Cast(false, out var offSeen);
        onSeen.Damage.Should().BeGreaterThan(0, "the spell is cast and lands");
        onSeen.RollRows.Should().Be(0, "no to-hit roll line for a spell");
        onSeen.Rows.Should().NotContain(r => r.StartsWith("You miss "));
        onCalls.Should().Equal(offCalls, "a spell draws from the fight's random what it drew: nothing is added for a to-hit roll");
        offSeen.Damage.Should().BeGreaterThan(0);
        onCalls.Should().NotContain("Next(1,21)", "no twenty-sided die from the fight's random (the casting check rolls in SpellSystem)");
    }

    [Fact]
    public void G13_ATeammate_RollsWithItsOwnStats_AndMissesInTheThirdPerson()
    {
        var hero = Fighter();
        var mate = G.Mate("Ally One", 30_000, 50, 300, 20);
        var strong = G.Mate("Ally Two", 30_000, 50, 300, 250);
        var foe = Needing(mate, 7);
        TrainingSystem.PlayerHitNeeded(strong, foe).Should().BeLessThan(7, "the second teammate's Dexterity lowers its own roll");
        var seen = Do(hero, Swing, foe, Faces(6, 7, 1, 20), times: 4, team: new List<Character> { mate }, teammateTurn: mate);
        seen.Rows.Should().ContainInOrder(
            "[Ally One rolls 6; needs 7 or more]", "Ally One misses Wolf!",
            "[Ally One rolls 7; needs 7 or more]",
            "[Ally One rolls a natural 1: always a miss]", "Ally One misses Wolf!",
            "[Ally One rolls a natural 20: always a hit]");
        seen.MissRows.Should().Be(2);
        seen.Rows.Should().NotContain(r => r.Contains("You roll") || r.StartsWith("You miss") || r.StartsWith("CRITICAL"));

        var other = Do(hero, Swing, Needing(mate, 7), Always(6), times: 3, team: new List<Character> { strong }, teammateTurn: strong);
        other.Rows.Should().Contain($"[Ally Two rolls 6; needs {TrainingSystem.PlayerHitNeeded(strong, foe)} or more]", "the roll is the teammate's, not the player's");

        var seeded = Do(hero, Swing, AtLevel(), new Random(128), times: 2000, team: new List<Character> { mate }, teammateTurn: mate);
        seeded.RollRows.Should().BeGreaterThanOrEqualTo(2000);
        seeded.HitRate.Should().BeInRange(0.88, 0.92);
    }

    [Fact]
    public void G13_ATeammatesAbility_RollsInTheThirdPerson()
    {
        var hero = Fighter();
        var mate = G.Mate("Ally One", 30_000, 50, 300, 20);
        var seen = WithSwitch(true, () =>
        {
            var foe = AtLevel();
            var rig = G.NewRig(hero, Always(2), new List<Character> { mate }, foe);
            var ability = ClassAbilitySystem.GetAbility("power_strike")!;
            var used = new ClassAbilityResult { Success = true, AbilityUsed = ability, Damage = 500, SpecialEffect = "" };
            ((Task)typeof(CombatEngine).GetMethod("ApplyAbilityEffectsMultiMonster", F)!
                .Invoke(rig.Engine, new object?[] { mate, foe, rig.Monsters, used, rig.Result })!).GetAwaiter().GetResult();
            return new Seen { Rig = rig, Rows = Rows(rig.Shown()), Damage = foe.MaxHP - foe.HP };
        });
        seen.Rows.Should().ContainInOrder("[Ally One rolls 2; needs 3 or more]", "Ally One misses Wolf!");
        seen.Damage.Should().Be(0);
    }

    [Fact]
    public void R1_Backstab_Smite_AndTheRangedAttack_Roll()
    {
        foreach (var (type, cls) in new[] { (CombatActionType.Backstab, CharacterClass.Assassin), (CombatActionType.Smite, CharacterClass.Paladin), (CombatActionType.RangedAttack, CharacterClass.Ranger) })
        {
            Character Hero() => Fighter(cls, dexterity: 200, main: cls == CharacterClass.Ranger ? 30 : null, mainType: cls == CharacterClass.Ranger ? WeaponType.Bow : WeaponType.Sword);
            var missed = Do(Hero(), G.A(type), AtLevel(), new DieRandom(128, i => 2), times: 30);
            missed.RollRows.Should().BeGreaterThan(20, "{0} rolls", type);
            missed.MissRows.Should().Be(missed.RollRows);
            missed.Damage.Should().Be(0, "{0}: a miss deals nothing", type);
            // the ranged attack's own damage die is the same Next(1, 21): a set 10 for every die still lands every shot
            var landed = Do(Hero(), G.A(type), AtLevel(), Always(10), times: 30);
            landed.MissRows.Should().Be(0);
            landed.Damage.Should().BeGreaterThan(0);
        }
        // Soul Strike is holy damage: no roll
        var soul = Do(Fighter(CharacterClass.Paladin), G.A(CombatActionType.SoulStrike), AtLevel(), Always(1), times: 30);
        soul.RollRows.Should().Be(0);
        soul.Damage.Should().BeGreaterThan(0);
    }

    // ---------------------------------------------------------------- G14, G16, G17

    [Fact]
    public void G14_ASwingFromHiding_AndAMiracleCrit_AlwaysHit()
    {
        var random = new DieRandom(128, _ => 1);
        var hidden = Do(Fighter(), Swing, Above(), random, times: 200, before: (r, _) => r.Hero.ApplyStatus(StatusEffect.Hidden, 2));
        hidden.RollRows.Should().Be(0, "a certain hit is not rolled");
        hidden.MissRows.Should().Be(0);
        random.Naturals.Should().BeEmpty();
        hidden.Rows.Count(r => r.Contains("strike from the shadows") || r.Contains("STEALTH") || r.Contains("shadows")).Should().BeGreaterThanOrEqualTo(200);
        hidden.Damage.Should().BeGreaterThan(0);

        var miracle = Do(Fighter(), Swing, Above(), new DieRandom(128, _ => 1), times: 200, before: (r, _) => r.Hero.MiracleCritPending = true);
        miracle.RollRows.Should().Be(0);
        miracle.MissRows.Should().Be(0);
        miracle.Damage.Should().BeGreaterThan(0);

        // Power Attack and a physical ability from hiding are certain too
        foreach (var action in new[] { G.A(CombatActionType.PowerAttack), G.A(CombatActionType.UseAbility, "power_strike") })
        {
            var seen = Do(Fighter(), action, Above(), new DieRandom(128, _ => 1), times: 50, before: (r, _) => r.Hero.ApplyStatus(StatusEffect.Hidden, 2));
            seen.RollRows.Should().Be(0, "{0} from hiding", action.Type);
            seen.MissRows.Should().Be(0);
        }
    }

    private static Monster Ghost(int chance)
    {
        var m = AtLevel();
        m.EvasionRounds = 1_000_000;
        m.EvasionMissChance = chance;
        return m;
    }

    [Fact]
    public void G16_TheEvasionCheck_StaysFirst_AndIsUnchanged()
    {
        // a certain evasion: the pass-through line, and no die is drawn
        var random = new DieRandom(128);
        var seen = Do(Fighter(), Swing, Ghost(100), random, times: 100);
        seen.Rows.Count(r => r.Contains("passes harmlessly through") || r.Contains("Wolf")).Should().BeGreaterThan(0);
        string evasion = In("en", () => Loc.Get("combat.evasion_miss", "Wolf"));
        seen.Rows.Count(r => r == evasion).Should().Be(100);
        seen.RollRows.Should().Be(0, "no roll after an evasion");
        random.Naturals.Should().BeEmpty("no hit die is drawn");
        random.Calls.Should().OnlyContain(c => c == "Next(100)", "the evasion draw is the only draw of an evaded swing");
        seen.Damage.Should().Be(0);

        // half the swings evaded: the evasion draw comes first, and the roll only when the swing was not evaded
        var half = new DieRandom(128);
        var mixed = Do(Fighter(), Swing, Ghost(50), half, times: 2000);
        int evaded = mixed.Rows.Count(r => r == evasion);
        evaded.Should().BeInRange(920, 1080, "the same 50% as before");
        mixed.RollRows.Should().Be(2000 - evaded);
        half.Naturals.Count.Should().Be(2000 - evaded);
        // every swing starts with the evasion draw
        var first = new List<string>();
        var one = new DieRandom(128);
        Do(Fighter(), Swing, Ghost(50), one, times: 1);
        one.Calls[0].Should().Be("Next(100)");

        // switch off: the same evasion rate, drawn where it was drawn before (after the damage draws)
        var offRandom = new DieRandom(128);
        var off = Do(Fighter(), Swing, Ghost(50), offRandom, times: 2000, on: false);
        off.Rows.Count(r => r == evasion).Should().BeInRange(920, 1080);
        offRandom.Calls[0].Should().NotBe("Next(100)");
    }

    private static readonly FieldInfo StoryField = typeof(StoryProgressionSystem).GetField("_fallbackInstance", BindingFlags.NonPublic | BindingFlags.Static)!;

    private static T WithSunforged<T>(bool held, Func<T> body)
    {
        object? saved = StoryField.GetValue(null);
        try
        {
            var story = new StoryProgressionSystem();
            if (held) story.CollectArtifact(ArtifactType.SunforgedBlade);
            StoryField.SetValue(null, story);
            ArtifactSystem.Instance.HasSunforgedBlade().Should().Be(held);
            return body();
        }
        finally { StoryField.SetValue(null, saved); }
    }

    [Fact]
    public void G17_WithTheSunforgedBlade_OnlyANatural1Misses()
    {
        // a target 10 levels above (the clamp is 8): every face from 2 lands with the blade
        var held = WithSunforged(true, () => Do(Fighter(), Swing, Above(), new DieRandom(128, i => 1 + i % 20), times: 200));
        held.RollRows.Should().Be(200);
        held.MissRows.Should().Be(10, "only the ten natural 1s miss");
        held.Rows.Count(r => r == "CRITICAL MISS! You stumble badly!").Should().Be(10);
        held.Rows.Count(r => r == "The Sunforged Blade guides your strike true!").Should().Be(60, "the faces 2 to 7, ten times each");
        held.Rows.Should().Contain("[You roll 5; you need 8 or more]", "the roll line still shows the roll and what it needed");

        var without = WithSunforged(false, () => Do(Fighter(), Swing, Above(), new DieRandom(128, i => 1 + i % 20), times: 200));
        without.MissRows.Should().Be(70, "without the blade the faces 1 to 7 miss");

        // a teammate does not hold the player's blade
        var mate = G.Mate("Ally One", 30_000, 50, 300, 20);
        var allyDice = new DieRandom(128, i => 1 + i % 20);
        var ally = WithSunforged(true, () => Do(Fighter(), Swing, Above(), allyDice, times: 200, team: new List<Character> { mate }, teammateTurn: mate));
        ally.RollRows.Should().BeGreaterThan(150);
        ally.MissRows.Should().Be(allyDice.Naturals.Count(n => n < 8), "every face below 8 misses for the teammate");
    }

    // ---------------------------------------------------------------- Warded is not in this piece

    [Fact]
    public void W_AWardedElite_IsRolledAgainstLikeAnyOtherMonster()
    {
        Monster Twin(bool warded)
        {
            var m = Above();
            if (!warded) return m;
            m.IsElite = true;
            m.EliteAffixes.Add("warded");
            m.Name = MonsterNames.FromKey("monster.elite.warded", m.Name);
            return m;
        }
        var hero = Fighter();
        TrainingSystem.PlayerHitNeeded(hero, Twin(true)).Should().Be(TrainingSystem.PlayerHitNeeded(hero, Twin(false)));
        var plain = Do(Fighter(), Swing, Twin(false), new Random(128), times: 2000);
        var warded = Do(Fighter(), Swing, Twin(true), new Random(128), times: 2000);
        warded.Rows.Should().Contain(r => r.EndsWith("you need 8 or more]")).And.NotContain(r => r.EndsWith("you need 11 or more]"));
        warded.MissRows.Should().Be(plain.MissRows, "the same seeded rolls miss the same swings");
        warded.HitRate.Should().BeInRange(0.63, 0.67);
        // the parameter stays, and nothing passes a value other than 0
        string src = File.ReadAllText(Path.Combine(RepoRoot(), "Scripts", "Systems", "CombatEngine.cs"));
        Regex.Matches(src, @"TrainingSystem\.RollPlayerHit\(").Count.Should().Be(1);
        src.Should().Contain("TrainingSystem.RollPlayerHit(attacker, target, 0, random, neededBonus)");
        TrainingSystem.PlayerHitNeeded(hero, Twin(false), 3).Should().Be(11, "the parameter itself adds to the roll and to the clamp");
    }

    // ---------------------------------------------------------------- G19, G20

    [Fact]
    public void G19_PvP_DoesNotRoll()
    {
        (List<string> Rows, List<string> Calls, long Damage) Duel(bool on) => WithSwitch(on, () =>
        {
            var random = new DieRandom(128);
            var attacker = Fighter(CharacterClass.Assassin, main: 40, off: 35);
            var defender = Fighter(name: "Rival");
            defender.HP = defender.MaxHP = 1_000_000_000L;
            var rig = G.NewRig(attacker, random);
            var method = typeof(CombatEngine).GetMethod("ExecutePvPSingleHit", F)!;
            for (int i = 0; i < 2000; i++)
                ((Task)method.Invoke(rig.Engine, new object?[] { attacker, defender, rig.Result, i % 2 == 1 })!).GetAwaiter().GetResult();
            return (Rows(rig.Shown()), random.Calls, defender.MaxHP - defender.HP);
        });
        var on = Duel(true);
        var off = Duel(false);
        on.Damage.Should().BeGreaterThan(0);
        on.Rows.Should().NotContain(r => r.Contains("You roll") || r.Contains(" rolls ") || r.StartsWith("You miss ") || r.StartsWith("CRITICAL MISS"), "no roll line and no miss line in 2,000 swings");
        on.Calls.Should().Equal(off.Calls, "a PvP swing draws what it drew");
        on.Damage.Should().Be(off.Damage);
        on.Rows.Should().Equal(off.Rows);
    }

    [Fact]
    public void G20_AnOldGodFight_Rolls()
    {
        var seen = Do(Fighter(), Swing, AtLevel(), new Random(128), times: 2000, setup: r => r.Engine.BossContext = G.Boss());
        seen.RollRows.Should().Be(2000, "every swing of a BossContext fight rolls");
        seen.MissRows.Should().BeGreaterThan(0, "misses occur");
        seen.HitRate.Should().BeInRange(0.88, 0.92, "at the at-level rate");

        var mate = G.Mate("Ally One", 30_000, 50, 300, 20);
        var team = Do(Fighter(), Swing, AtLevel(), new Random(128), times: 1000, setup: r => r.Engine.BossContext = G.Boss(), team: new List<Character> { mate }, teammateTurn: mate);
        team.RollRows.Should().BeGreaterThan(0, "a teammate rolls in an Old God fight too");
        team.MissRows.Should().BeGreaterThan(0);
    }

    // ---------------------------------------------------------------- G21: switch off, the draws of before

    /// <summary>Written by Tests/HitRoll128Golden.cs (this same file) on release-1.2.8 at 621721d6, the code
    /// before the piece: the draws up to the last foe's death, a hash of them, the outcome and the rounds.</summary>
    private static readonly Dictionary<string, string> GoldenFights = new()
    {
        ["solo, floor 7 pair"] = "172:09EB7427C6AB97E1:Victory in 16",
        ["solo, floor 20 trio, level 20 hero"] = "236:92DDB918DF2B3297:Victory in 16",
        ["solo, dual wield, floor 12 pair"] = "219:B81D531085732DE8:Victory in 13",
        ["one teammate, floor 7 pair"] = "167:7AEB06910EB5C40C:Victory in 11",
        ["two teammates, floor 35 trio, level 35 hero"] = "196:7A90196A8DDE9163:Victory in 9",
        ["boss context, solo"] = "390:B5F1D953200E5375:Victory in 50",
        ["boss context, one teammate"] = "496:F484960F59CC372E:Victory in 46",
    };

    /// <summary>Written there too: thirty of each action on a seeded engine, the draws, a hash of them and the damage done.</summary>
    private static readonly Dictionary<string, string> GoldenActions = new()
    {
        ["basic swing"] = "120:CE4A9DC8BB244B77:damage 3092",
        ["basic swing, dual wield"] = "355:3D0303E6A7D807A5:damage 8202",
        ["basic swing, evasive foe"] = "133:1E89B74D806F72F5:damage 1519",
        ["basic swing, hidden"] = "60:6B3D890660EC3F4B:damage 5508",
        ["power attack, dual wield"] = "302:5D59C1911EC791CC:damage 12082",
        ["precise strike"] = "60:8B6AC522FECD9C30:damage 1121",
        ["smite"] = "60:835BA8452318FE9E:damage 3541",
        ["backstab"] = "70:4AFA56909D17EF9D:damage 3807",
        ["ranged attack"] = "90:DFBCE467FB180184:damage 3258",
        ["soul strike"] = "60:53AEE4521E492843:damage 2127",
        ["ability, power strike"] = "89:72BF8C2FBED1B85A:damage 4626",
        ["ability, execute, dual wield"] = "240:F8BD0ED1EE3A9A46:damage 22979",
        ["ability, whirlwind (area)"] = "89:72BF8C2FBED1B85A:damage 11095",
        ["teammate swings"] = "121:C7A66D3991FB6491:damage 2125",
    };

    /// <summary>The actions whose draws the roll must not change even when it is on.</summary>
    private static readonly string[] NeverRolled = { "basic swing, hidden", "soul strike", "ability, whirlwind (area)" };

    [Fact]
    public async Task G21_SwitchOff_ASoloFight_ATeamFight_AndABossFight_DrawTheNumbersOfBeforeThePiece()
    {
        bool saved = GameConfig.PlayerHitRollEnabled;
        string lang = GameConfig.Language;
        try
        {
            GameConfig.PlayerHitRollEnabled = false;
            GameConfig.Language = "en";
            G.Fights.Select(f => f.Name).Should().BeEquivalentTo(GoldenFights.Keys);
            foreach (var (name, fight) in G.Fights)
            {
                var run = await fight();
                run.Error.Should().BeNull(name);
                G.Signature(run).Should().Be(GoldenFights[name], name);
                Rows(run.Transcript).Should().NotContain(r => r.StartsWith("[You roll") || r.StartsWith("You miss "), name);
            }
        }
        finally { GameConfig.PlayerHitRollEnabled = saved; GameConfig.Language = lang; }
    }

    [Fact]
    public async Task G21_SwitchOn_TheSameFights_DrawOtherNumbers_AndShowTheRoll()
    {
        bool saved = GameConfig.PlayerHitRollEnabled;
        string lang = GameConfig.Language;
        try
        {
            GameConfig.PlayerHitRollEnabled = true;
            GameConfig.Language = "en";
            foreach (var (name, fight) in G.Fights)
            {
                var run = await fight();
                run.Error.Should().BeNull(name);
                G.Signature(run).Should().NotBe(GoldenFights[name], name);
                Rows(run.Transcript).Should().Contain(r => r.StartsWith("[You roll"), name);
                if (name.Contains("teammate")) Rows(run.Transcript).Should().Contain(r => r.StartsWith("[Ally One rolls"), name);
            }
        }
        finally { GameConfig.PlayerHitRollEnabled = saved; GameConfig.Language = lang; }
    }

    [Fact]
    public void G21_SwitchOff_EveryAction_DrawsTheNumbersOfBeforeThePiece()
    {
        WithSwitch(false, () =>
        {
            G.Actions.Select(a => a.Name).Should().BeEquivalentTo(GoldenActions.Keys);
            foreach (var (name, action) in G.Actions)
            {
                var run = action();
                run.Error.Should().BeNull(name);
                G.ActionSignature(run).Should().Be(GoldenActions[name], name);
            }
            return 0;
        });
        WithSwitch(true, () =>
        {
            foreach (var (name, action) in G.Actions)
            {
                var run = action();
                run.Error.Should().BeNull(name);
                if (NeverRolled.Contains(name)) G.ActionSignature(run).Should().Be(GoldenActions[name], "{0} does not roll", name);
                else G.ActionSignature(run).Should().NotBe(GoldenActions[name], "{0} rolls", name);
            }
            return 0;
        });
    }

    // ---------------------------------------------------------------- G22

    /// <summary>Every draw is a fixed function of its range, so the damage draws of a swing are the same
    /// whatever their order; the twenty-sided die shows the face given.</summary>
    private sealed class FlatRandom : Random
    {
        private readonly int _face;
        public FlatRandom(int face) { _face = face; }
        public override int Next() => 7;
        public override int Next(int maxValue) => maxValue <= 0 ? 0 : maxValue - 1;   // no chance roll succeeds
        public override int Next(int minValue, int maxValue) => minValue == 1 && maxValue == 21 ? _face : minValue + (maxValue - minValue) / 2;
        public override double NextDouble() => 0.999;
        protected override double Sample() => 0.999;
    }

    [Theory]
    [InlineData(3)]
    [InlineData(15)]
    [InlineData(19)]
    public void G22_ANonCriticalHit_DealsTodaysDamage_WhateverTheMargin(int face)
    {
        // at the clamp (needed 3): a 3, a 15 and a 19 beat it by 0, 12 and 16; the damage is the swing's of before
        long on = Do(Fighter(main: 80), Swing, AtLevel(), new FlatRandom(face), times: 1).Damage;
        long off = Do(Fighter(main: 80), Swing, AtLevel(), new FlatRandom(face), times: 1, on: false).Damage;
        off.Should().BeGreaterThan(100);
        on.Should().Be(off, "a hit with a natural {0} deals the damage of the same swing before the piece", face);
    }

    [Fact]
    public void G22_TheNatural20_DealsTheCriticalHitOfBefore()
    {
        long on = Do(Fighter(main: 80), Swing, AtLevel(), new FlatRandom(20), times: 1).Damage;
        long off = Do(Fighter(main: 80), Swing, AtLevel(), new FlatRandom(20), times: 1, on: false).Damage;
        long plain = Do(Fighter(main: 80), Swing, AtLevel(), new FlatRandom(15), times: 1).Damage;
        on.Should().Be(off, "the hit die's 20 gives the critical multiplier the separate die's 20 gave");
        on.Should().BeGreaterThan(plain);
    }

    // ---------------------------------------------------------------- the roll line

    [Fact]
    public void T1_TheRollLine_ShowsTheNaturalRollAndTheNeededRoll_OneLineARoll()
    {
        var seen = Do(Fighter(), Swing, AtLevel(), Faces(10, 2, 1, 20, 3), times: 5);
        seen.Rows.Where(r => !r.StartsWith("You attack") && !r.Contains("Wolf for") && !r.Contains("damage") && !r.StartsWith("CRITICAL HIT") && !r.Contains("hit ")).Should().NotBeEmpty();
        var story = seen.Rows.Where(r => r.StartsWith("[") || r.StartsWith("You miss") || r.StartsWith("CRITICAL")).ToList();
        story.Should().Equal(
            "[You roll 10; you need 3 or more]",
            "[You roll 2; you need 3 or more]", "You miss Wolf!",
            "[You roll a natural 1: always a miss]", "CRITICAL MISS! You stumble badly!",
            "[You roll a natural 20: always a hit]", "CRITICAL HIT!",
            "[You roll 3; you need 3 or more]");
        seen.RollRows.Should().Be(5, "one line a roll");
        seen.Rows.Should().NotContain(r => r.Contains(" vs ") || r.Contains("DC") || r.Contains(" = "), "never a total against a DC");
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void T2_TheRollLine_IsPlainText_OneRowARoll_AlsoInScreenReaderMode(bool screenReader)
    {
        bool saved = GameConfig.ScreenReaderMode;
        try
        {
            GameConfig.ScreenReaderMode = screenReader;
            var random = new DieRandom(128);
            var seen = Do(Fighter(), Swing, AtLevel(), random, times: 300);
            seen.RollRows.Should().Be(300);
            random.Naturals.Count.Should().Be(300);
            var rollRows = seen.Rows.Where(r => r.StartsWith("[You roll")).ToList();
            rollRows.Should().OnlyContain(r => Regex.IsMatch(r, @"^\[You roll (\d+; you need \d+ or more|a natural 1: always a miss|a natural 20: always a hit)\]$"));
            // the row on the stream is the bytes of the monster's roll row: the Derived colour, then the text as
            // it is through UIHelper.WriteRow, nothing else (no markup, no second colour)
            string raw = seen.Rig.Shown();
            foreach (string text in rollRows.Distinct())
            {
                var output = new MemoryStream();
                var term = new TerminalEmulator(new MemoryStream(), output);
                term.SetColor(ColorRole.Derived);
                UIHelper.WriteRow(term, text);
                term.StreamWriterInternal?.Flush();
                string model = Encoding.UTF8.GetString(output.ToArray());
                model.Should().Contain(text);
                raw.Should().Contain(model, "the row is written like the monster's roll row");
            }
            raw.Should().NotContain("[/]");
            foreach (string key in RollKeys.Concat(RollKeys.Select(k => k + "_third")))
                foreach (string lang in Langs)
                    Regex.IsMatch(Loc.GetIn(lang, key, "A", "B", "C"), @"\[/?[a-z_]+\]").Should().BeFalse("{0} in {1} holds no markup tag", key, lang);
        }
        finally { GameConfig.ScreenReaderMode = saved; }
    }

    [Fact]
    public void T3_TheKeys_AreInFiveLanguages_WithRealTranslations()
    {
        foreach (string key in RollKeys.Concat(RollKeys.Select(k => k + "_third")))
        {
            string en = Loc.GetIn("en", key);
            foreach (string lang in Langs)
            {
                Loc.HasIn(lang, key).Should().BeTrue("{0} in {1}", key, lang);
                string text = Loc.GetIn(lang, key);
                if (lang != "en") text.Should().NotBe(en, "{0} in {1} is translated", key, lang);
                Regex.Matches(text, @"\{\d\}").Select(m => m.Value).OrderBy(v => v).Should().Equal(Regex.Matches(en, @"\{\d\}").Select(m => m.Value).OrderBy(v => v), "{0} in {1} keeps the placeholders", key, lang);
                text.Should().StartWith("[").And.EndWith("]");
                text.Should().NotContain("\u2014").And.NotContain("\u2013");
            }
        }
        // the third person form takes the name first, then the second person form's arguments
        Loc.GetIn("en", "combat.hit_roll", 7, 3).Should().Be("[You roll 7; you need 3 or more]");
        Loc.GetIn("en", "combat.hit_roll_third", "Ally", 7, 3).Should().Be("[Ally rolls 7; needs 3 or more]");
        Loc.GetIn("hu", "combat.hit_roll", 7, 3).Should().Be("[Dobásod: 7; legalább 3 kell]");
        Loc.GetIn("hu", "combat.hit_roll_third", "Ally", 7, 3).Should().Be("[Ally dobása: 7; legalább 3 kell]");
    }

    private static string LongestEliteCaster(string lang)
    {
        string[] affixes = { "frenzied", "warded", "venomous", "commanding", "mending", "swift" };
        string best = "";
        int bestLength = 0;
        foreach (var family in MonsterFamilies.AllFamilies.Where(f => MonsterGenerator.CasterSpells.ContainsKey(f.FamilyName)))
            foreach (var tier in family.Tiers)
                foreach (string a in affixes)
                    foreach (string b in affixes.Where(b => b != a))
                    {
                        string name = MonsterNames.FromKey("monster.elite." + a, MonsterNames.FromKey("monster.elite." + b, MonsterNames.FromKey("monster.role.caster", tier.Name)));
                        int length = MonsterNames.DisplayIn(lang, name).Length;
                        if (length > bestLength) { best = name; bestLength = length; }
                    }
        return best;
    }

    private const string LongMate = "Seraphina Blackthorn-Vanderholt";

    [Theory]
    [InlineData("en")]
    [InlineData("hu")]
    [InlineData("es")]
    [InlineData("fr")]
    [InlineData("it")]
    public void T4_TheRollLine_AndTheMissLines_FitIn79Columns_WithTheLongestNames(string lang)
    {
        UIHelper.WrapWidth.Should().Be(79);
        string name = LongestEliteCaster(lang);
        string shownName = MonsterNames.DisplayIn(lang, name);
        shownName.Length.Should().BeGreaterThan(30, "a two-affix elite caster: {0}", shownName);
        Monster Foe() { var m = Above(); m.Name = name; m.TierName = name; return m; }
        var mate = G.Mate(LongMate, 30_000, 50, 300, 20);

        List<string> RowsOf(Func<Seen> run) => In(lang, () => { bool saved = GameConfig.PlayerHitRollEnabled; try { GameConfig.PlayerHitRollEnabled = true; return run(); } finally { GameConfig.PlayerHitRollEnabled = saved; } }).Rows;
        Seen Raw(Character hero, Random random, Character? teammate)
        {
            var foe = Foe();
            var rig = G.NewRig(hero, random, teammate == null ? null : new List<Character> { teammate }, foe);
            for (int i = 0; i < 4; i++) { if (teammate != null) Swings(rig, teammate); else G.Act(rig, Swing); }
            return new Seen { Rig = rig, Rows = Rows(rig.Shown()) };
        }
        var own = RowsOf(() => Raw(Fighter(), Faces(2, 1, 20, 9), null));
        var team = RowsOf(() => Raw(Fighter(), Faces(2, 1, 20, 9), mate));

        own.Should().Contain(Loc.GetIn(lang, "combat.hit_roll", 2, 8));
        own.Should().Contain(Loc.GetIn(lang, "combat.hit_roll_nat1")).And.Contain(Loc.GetIn(lang, "combat.hit_roll_nat20")).And.Contain(Loc.GetIn(lang, "combat.critical_miss"));
        team.Should().Contain(Loc.GetIn(lang, "combat.hit_roll_third", LongMate, 2, 8));
        team.Should().Contain(Loc.GetIn(lang, "combat.hit_roll_nat1_third", LongMate)).And.Contain(Loc.GetIn(lang, "combat.hit_roll_nat20_third", LongMate));
        string ownMiss = Loc.GetIn(lang, "combat.you_miss", shownName), teamMiss = Loc.GetIn(lang, "combat.you_miss_third", LongMate, shownName);
        teamMiss.Length.Should().BeGreaterThan(79, "the teammate's miss line is longer than a row with these names: it must be wrapped");
        string.Join(" ", own).Should().Contain(ownMiss);
        string.Join(" ", team).Should().Contain(teamMiss, "the wrapped rows hold the whole line");
        UIHelper.MessageRows(teamMiss).Count.Should().BeGreaterThan(1);
        foreach (string piece in UIHelper.MessageRows(teamMiss)) team.Should().Contain(piece, "the teammate's miss line is written as wrapped rows");
        foreach (string piece in UIHelper.MessageRows(ownMiss)) own.Should().Contain(piece, "the player's miss line is written through the same wrap");
        team.Should().NotContain(teamMiss, "the line is not written as one row");
        // the rows of this piece: the roll rows and the miss rows (the rows that only carry a monster name are listed in the report)
        bool Mine(string row) => row.StartsWith("[") || UIHelper.MessageRows(ownMiss).Contains(row) || UIHelper.MessageRows(teamMiss).Contains(row) || row == Loc.GetIn(lang, "combat.critical_miss");
        own.Concat(team).Where(Mine).Should().NotBeEmpty();
        foreach (string row in own.Concat(team).Where(Mine))
            row.Length.Should().BeLessThanOrEqualTo(79, "{0}: [{1}]", lang, row);
        // every form of the roll line with the long teammate name, all faces and both clamps
        foreach (int needed in new[] { 2, 3, 8, 11 })
            foreach (int face in new[] { 2, 19 })
                foreach (string line in new[] { Loc.GetIn(lang, "combat.hit_roll", face, needed), Loc.GetIn(lang, "combat.hit_roll_third", LongMate, face, needed),
                             Loc.GetIn(lang, "combat.hit_roll_nat1_third", LongMate), Loc.GetIn(lang, "combat.hit_roll_nat20_third", LongMate) })
                    line.Length.Should().BeLessThanOrEqualTo(79, "{0}: [{1}]", lang, line);
    }

    [Theory]
    [InlineData("hu")]
    [InlineData("fr")]
    [InlineData("en")]
    public void T5_AnotherReader_SeesTheRollLineInTheThirdPerson_InTheirLanguage(string lang)
    {
        // the leader's (or a follower's) captured action, recorded as the combat loop records it
        string prev = GameConfig.Language;
        GameConfig.Language = "en";
        var term = new TerminalEmulator(new MemoryStream(), new MemoryStream());
        term.StartCapture();
        var rec = Loc.BeginRecording();
        try
        {
            UIHelper.WriteRow(term, Loc.Get("combat.hit_roll", 7, 3));
            UIHelper.WriteRow(term, Loc.Get("combat.hit_roll_nat1"));
            UIHelper.WriteRow(term, Loc.Get("combat.hit_roll_nat20"));
            UIHelper.WriteRow(term, Loc.Get("combat.you_miss", "Wolf"));
        }
        finally { Loc.EndRecording(rec); GameConfig.Language = prev; }
        string shown = Plain(CombatEngine.CapturedInLanguage(rec, term.StopCapture()!, lang, "Rage"));
        shown.Should().Contain(Loc.GetIn(lang, "combat.hit_roll_third", "Rage", 7, 3));
        shown.Should().Contain(Loc.GetIn(lang, "combat.hit_roll_nat1_third", "Rage"));
        shown.Should().Contain(Loc.GetIn(lang, "combat.hit_roll_nat20_third", "Rage"));
        shown.Should().Contain(Loc.GetIn(lang, "combat.you_miss_third", "Rage", "Wolf"));
        shown.Should().NotContain(Loc.GetIn(lang, "combat.hit_roll", 7, 3)).And.NotContain("You roll");
        foreach (string key in RollKeys) CombatEngine.ThirdPersonKeys[key].Should().Be(key + "_third");
    }

    [Fact]
    public void T6_RollLinesInOneRound_OneForEachSwingOfThePlayerAndOfEachTeammate()
    {
        // a dual-wielding player and two teammates: the round carries one roll line a swing, and no more
        var hero = Fighter(CharacterClass.Assassin, main: 40, off: 35);
        var mates = new List<Character> { G.Mate("Ally One", 30_000, 50, 300, 20), G.Mate("Ally Two", 30_000, 50, 300, 20) };
        var seen = WithSwitch(true, () =>
        {
            var foe = AtLevel();
            var rig = G.NewRig(hero, Always(10), mates, foe);
            G.Act(rig, Swing);
            foreach (var m in mates) Swings(rig, m);
            return new Seen { Rig = rig, Rows = Rows(rig.Shown()) };
        });
        int swings = seen.Rows.Count(r => r.StartsWith("You attack") || r.StartsWith("Off-hand") || Regex.IsMatch(r, "^Ally (One|Two) (attacks|strikes)"));
        seen.Rows.Count(r => r.StartsWith("[You roll")).Should().Be(seen.Rows.Count(r => r.StartsWith("You attack") || r.StartsWith("Off-hand")));
        seen.RollRows.Should().Be(swings);
        seen.Rows.Count(r => r.StartsWith("[Ally One rolls")).Should().BeGreaterThan(0);
        seen.Rows.Count(r => r.StartsWith("[Ally Two rolls")).Should().BeGreaterThan(0);
        seen.RollRows.Should().BeInRange(4, 16);
    }

    [Fact]
    public void TheSwitch_IsOnByDefault_AndNotASysopSetting()
    {
        var p = typeof(GameConfig).GetProperty(nameof(GameConfig.PlayerHitRollEnabled))!;
        GameConfig.PlayerHitRollEnabled.Should().BeTrue();
        p.SetMethod!.IsAssembly.Should().BeTrue("the setter is internal");
        GameConfig.PlayerHitMinNeeded.Should().Be(2);
        GameConfig.PlayerHitMaxNeededAtLevel.Should().Be(3);
        GameConfig.PlayerHitMaxNeededAbove.Should().Be(8);
        GameConfig.AtLevelAccuracyGap.Should().Be(5);
        string root = RepoRoot();
        foreach (string file in Directory.EnumerateFiles(Path.Combine(root, "Scripts"), "*.cs", SearchOption.AllDirectories))
        {
            string name = Path.GetFileName(file);
            if (name is "GameConfig.cs" or "CombatEngine.cs") continue;
            File.ReadAllText(file).Should().NotContain("PlayerHitRollEnabled", $"{name} must not read or set the switch");
        }
        foreach (string file in Directory.EnumerateFiles(root, "balance*.json", SearchOption.TopDirectoryOnly))
            File.ReadAllText(file).Should().NotContain("PlayerHitRoll");
    }
}
