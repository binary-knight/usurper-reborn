using System;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using System.Threading.Tasks;
using FluentAssertions;
using UsurperRemake;
using UsurperRemake.Systems;
using Xunit;

namespace UsurperReborn.Tests;

/// <summary>
/// 1.3.6 G8: riders and ticks through the boss protections (CP7, Q3), Cycle's End and Entropic Blade add no Defence
/// (CP8), Weaken's 20 percent defence cut on every live hit path (Q4), and the Poisoned status adds no attack (X5).
/// Each protected case runs beside an unprotected twin on the same seed, so the draws match and the only difference
/// is the protection.
/// </summary>
[Collection("SharedGameSingletons")]
public class ProtectRiders136Tests
{
    private const BindingFlags F = BindingFlags.NonPublic | BindingFlags.Instance;
    private const double Divine = 0.25;
    private const int Seed = 136;

    private static double R => GameConfig.ModBossPhaseImmunityResidual;

    // ---------- rig ----------

    private static Character Hero(CharacterClass cls = CharacterClass.Warrior) => new Character
    {
        Name1 = "Hero", Name2 = "Hero", Class = cls, Race = CharacterRace.Human, Level = 60,
        Strength = 200, Intelligence = 100, HP = 1_000, MaxHP = 1_000_000, CombatSpeed = CombatSpeed.Instant,
    };

    private static Monster Foe(bool physical = false, bool magical = false, long armPow = 0, int defence = 0) => new Monster
    {
        Name = "Wolf", Level = 60, HP = 10_000_000, MaxHP = 10_000_000, Strength = 10, Defence = defence, ArmPow = armPow,
        IsActive = true, IsBoss = physical || magical, IsPhysicalImmune = physical, IsMagicalImmune = magical,
    };

    private static CombatEngine Engine(Character hero, BossCombatContext? ctx)
    {
        var engine = new CombatEngine(new TerminalEmulator(new MemoryStream(), new MemoryStream()));
        engine.SeedRandomForTests(Seed);
        typeof(CombatEngine).GetField("currentPlayer", F)!.SetValue(engine, hero);
        typeof(CombatEngine).GetField("currentTeammates", F)!.SetValue(engine, new List<Character>());
        engine.BossContext = ctx;
        return engine;
    }

    private static BossCombatContext Armour() => new BossCombatContext { DivineArmorReduction = Divine };

    private static long Immune(long full) => (long)(full * R);
    private static long Armoured(long full) => Math.Max(1, (long)(full * (1.0 - Divine)));

    private static T NoHitRoll<T>(Func<T> body)
    {
        bool saved = GameConfig.PlayerHitRollEnabled;
        try { GameConfig.PlayerHitRollEnabled = false; return body(); }
        finally { GameConfig.PlayerHitRollEnabled = saved; }
    }

    private static void Wait(object? task) => ((Task)task!).GetAwaiter().GetResult();

    /// <summary>The monster's status step for a new round, held after its ticks so it does nothing else.</summary>
    private static long Step(Monster foe, BossCombatContext? ctx)
    {
        var hero = Hero();
        var engine = Engine(hero, ctx);
        long before = foe.HP;
        foe.StatusTickedThisRound = false;
        foe.StunRounds = 1;
        Wait(typeof(CombatEngine).GetMethod("ProcessMonsterAction", F)!
            .Invoke(engine, new object?[] { foe, hero, new CombatResult { Player = hero }, new List<Monster> { foe } }));
        return before - foe.HP;
    }

    private static ClassAbilityResult Use(string id, int damage)
    {
        var ability = ClassAbilitySystem.GetAbility(id);
        ability.Should().NotBeNull(id);
        return new ClassAbilityResult { Success = true, AbilityUsed = ability, Damage = damage, SpecialEffect = ability!.SpecialEffect };
    }

    /// <summary>One class ability action through ApplyAbilityEffectsMultiMonster; the HP the target lost.</summary>
    private static long Act(string id, int damage, Monster target, Character? hero = null, BossCombatContext? ctx = null) =>
        NoHitRoll(() =>
        {
            hero ??= Hero();
            var engine = Engine(hero, ctx);
            long before = target.HP;
            var result = new CombatResult { Player = hero };
            result.Monsters.Add(target);
            Wait(typeof(CombatEngine).GetMethod("ApplyAbilityEffectsMultiMonster", F)!
                .Invoke(engine, new object?[] { hero, target, new List<Monster> { target }, Use(id, damage), result }));
            return before - target.HP;
        });

    /// <summary>A spell's effect on a monster through HandleSpecialSpellEffectOnMonster; the HP it lost.</summary>
    private static long SpellEffect(string effect, Monster target, BossCombatContext? ctx, long totalDealt = 0)
    {
        var hero = Hero(CharacterClass.Magician);
        var engine = Engine(hero, ctx);
        long before = target.HP;
        var result = new CombatResult { Player = hero, TotalDamageDealt = totalDealt };
        typeof(CombatEngine).GetMethod("HandleSpecialSpellEffectOnMonster", F)!
            .Invoke(engine, new object?[] { target, effect, 0, hero, 0L, result });
        return before - target.HP;
    }

    /// <summary>One pulse of a Searing Totem of the given power; the HP the monster lost.</summary>
    private static long Totem(Monster foe, BossCombatContext? ctx, int power)
    {
        var hero = Hero(CharacterClass.MysticShaman);
        hero.ActiveTotemType = 3;
        hero.ActiveTotemRounds = 3;
        hero.ActiveTotemPower = power;
        var engine = Engine(hero, ctx);
        long before = foe.HP;
        typeof(CombatEngine).GetMethod("ProcessShamanTotemEffects", F)!
            .Invoke(engine, new object?[] { hero, new List<Monster> { foe }, new CombatResult { Player = hero } });
        return before - foe.HP;
    }

    // ---------- rule 1: the Poisoned tick (Q3) ----------

    private static Monster PoisonedFoe(bool physical = false, bool magical = false)
    {
        var m = Foe(physical, magical);
        m.Poisoned = true;
        m.PoisonRounds = 3;
        return m;
    }

    [Fact]
    public void ProtectRiders136_PoisonedTick_OnABossInPhysicalImmunityAndDivineArmour_IsCutAsWeaponPoison()
    {
        long full = Step(PoisonedFoe(), null);
        long cut = Step(PoisonedFoe(physical: true), Armour());
        full.Should().BeGreaterThan(300_000, "3 to 5 percent of 10 million");
        cut.Should().Be(Math.Max(1, (long)(Immune(full) * (1.0 - Divine))),
            "physical phase immunity, then divine armour, at least 1, as the weapon poison tick beside it");
    }

    [Fact]
    public void ProtectRiders136_PoisonedTick_DivineArmourAlone_CutsIt()
    {
        long full = Step(PoisonedFoe(), null);
        Step(PoisonedFoe(), Armour()).Should().Be(Math.Max(1, (long)(full * (1.0 - Divine))));
    }

    [Fact]
    public void ProtectRiders136_PoisonedTick_PhysicalImmunityAlone_CutsIt()
    {
        long full = Step(PoisonedFoe(), null);
        Step(PoisonedFoe(physical: true), null).Should().Be(Math.Max(1, Immune(full)));
    }

    [Fact]
    public void ProtectRiders136_PoisonedTick_MagicalImmunity_DoesNotCutIt_AsWeaponPoison()
    {
        long full = Step(PoisonedFoe(), null);
        Step(PoisonedFoe(magical: true), null).Should().Be(full, "the weapon poison tick counts as physical");
    }

    [Fact]
    public void ProtectRiders136_PoisonedTick_OnANormalMonster_IsTheOldThreeToFivePercent()
    {
        var foe = PoisonedFoe();
        long full = Step(foe, null);
        full.Should().BeInRange(300_000 + 1, 500_000 + 60 / 5 + 1, "3 to 5 percent of max HP plus 1 to level / 5 + 1");
        foe.PoisonRounds.Should().Be(2);
    }

    // ---------- rule 2: the six riders and ticks (CP7) ----------

    [Fact]
    public void ProtectRiders136_SearingTotemTick_MagicalImmunityAndDivineArmour_CutIt()
    {
        long full = Totem(Foe(), null, 10_000);
        full.Should().BeInRange(10_000, 10_019);
        Totem(Foe(magical: true), Armour(), 10_000).Should().Be(Armoured(Immune(full)), "a magical class ability's tick");
    }

    [Fact]
    public void ProtectRiders136_SearingTotemTick_IsMagical_PhysicalImmunityDoesNotCutIt()
    {
        long full = Totem(Foe(), null, 10_000);
        Totem(Foe(physical: true), null, 10_000).Should().Be(full);
        Totem(Foe(), Armour(), 10_000).Should().Be(Armoured(full));
    }

    private static Monster CorruptedFoe(bool physical = false, bool magical = false)
    {
        var m = Foe(physical, magical);
        m.CorruptingDotRounds = 3;
        m.CorruptingDotTickDamage = 10_000;
        return m;
    }

    [Fact]
    public void ProtectRiders136_CorruptingTouchTick_MagicalImmunityAndDivineArmour_CutIt()
    {
        Step(CorruptedFoe(), null).Should().Be(10_000, "unprotected, unchanged");
        Step(CorruptedFoe(magical: true), Armour()).Should().Be(Armoured(Immune(10_000)));
        Step(CorruptedFoe(), Armour()).Should().Be(Armoured(10_000));
        Step(CorruptedFoe(physical: true), null).Should().Be(10_000, "a magical ability's tick: physical immunity does not cut it");
    }

    [Fact]
    public void ProtectRiders136_CorruptingTouchTick_HealsHalfOfTheCutTick()
    {
        var hero = Hero();
        var engine = Engine(hero, Armour());
        var foe = CorruptedFoe(magical: true);
        foe.StatusTickedThisRound = false;
        foe.StunRounds = 1;
        long hp = hero.HP;
        Wait(typeof(CombatEngine).GetMethod("ProcessMonsterAction", F)!
            .Invoke(engine, new object?[] { foe, hero, new CombatResult { Player = hero }, new List<Monster> { foe } }));
        (hero.HP - hp).Should().Be(Math.Max(1, Armoured(Immune(10_000)) / 2));
    }

    // Void Bolt (Voidreaver.3, ignore_defense), Shadow Step (Sage.17, shadowstep), Pillar of Fire (Magician.17,
    // piercing_fire): a bonus equal to the target's armour. Paradox Collapse (Cyclebreaker.5): 10 percent of the fight's damage.
    [Theory]
    [InlineData("ignore_defense")]
    [InlineData("shadowstep")]
    [InlineData("piercing_fire")]
    [InlineData("paradox_collapse")]
    public void ProtectRiders136_SpellRider_MagicalImmunityAndDivineArmour_CutIt(string effect)
    {
        SpellEffect(effect, Foe(armPow: 10_000), null, totalDealt: 100_000).Should().Be(10_000, "unprotected, unchanged");
        SpellEffect(effect, Foe(magical: true, armPow: 10_000), Armour(), totalDealt: 100_000).Should().Be(Armoured(Immune(10_000)));
        SpellEffect(effect, Foe(armPow: 10_000), Armour(), totalDealt: 100_000).Should().Be(Armoured(10_000));
        SpellEffect(effect, Foe(physical: true, armPow: 10_000), null, totalDealt: 100_000).Should().Be(10_000,
            "a spell is magical: physical immunity does not cut it");
    }

    // ---------- rule 3: Cycle's End and Entropic Blade (CP8) ----------

    [Theory]
    [InlineData("cycles_end", CharacterClass.Cyclebreaker)]
    [InlineData("entropic_blade", CharacterClass.Voidreaver)]
    public void ProtectRiders136_OwnDamageHit_IsTheSameOnHighAndLowDefence(string id, CharacterClass cls)
    {
        long low = Act(id, 1_000, Foe(defence: 0), Hero(cls));
        long high = Act(id, 1_000, Foe(defence: 4_000), Hero(cls));
        low.Should().BeGreaterThan(0);
        high.Should().Be(low, "none of the target's Defence is added, and the helper takes none off");
    }

    // ---------- rule 4: Weaken's defence cut (Q4) ----------

    private static Monster Weakened(Monster m) { m.WeakenRounds = 3; return m; }

    private static long Single(Monster target, long damage, bool spell)
    {
        var hero = Hero();
        var engine = Engine(hero, null);
        long before = target.HP;
        Wait(typeof(CombatEngine).GetMethod("ApplySingleMonsterDamage", F)!
            .Invoke(engine, new object?[] { target, damage, new CombatResult { Player = hero }, spell ? "spell" : "attack", hero, spell }));
        return before - target.HP;
    }

    private static long Area(Monster target, long damage, bool spell)
    {
        var hero = Hero();
        var engine = Engine(hero, null);
        long before = target.HP;
        Wait(typeof(CombatEngine).GetMethod("ApplyAoEDamageHits", F)!
            .Invoke(engine, new object?[] { new List<Monster> { target }, damage, new CombatResult { Player = hero }, "area",
                spell, spell ? null : hero, null, false, true }));
        return before - target.HP;
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void ProtectRiders136_Weaken_TheSwingAndTheSingleSpell_TakeTwentyPercentOffTheArmourOnce(bool spell)
    {
        Single(Foe(armPow: 1_000), 5_000, spell).Should().Be(4_000, "unweakened, unchanged");
        Single(Weakened(Foe(armPow: 1_000)), 5_000, spell).Should().Be(4_200, "800 armour, not 640");
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void ProtectRiders136_Weaken_TheAreaHit_TakesTwentyPercentOffTheArmourOnce(bool spell)
    {
        Area(Foe(armPow: 1_000), 5_000, spell).Should().Be(4_000, "unweakened, unchanged");
        Area(Weakened(Foe(armPow: 1_000)), 5_000, spell).Should().Be(4_200);
    }

    [Fact]
    public void ProtectRiders136_Weaken_TheAbilityBaseBlock_TakesTwentyPercentOffItsDefenceOnce()
    {
        // power_strike: the base block takes Defence / 2 = 500 off; weakened, 400
        long full = Act("power_strike", 5_000, Foe(defence: 1_000));
        long weak = Act("power_strike", 5_000, Weakened(Foe(defence: 1_000)));
        (weak - full).Should().Be(100);
    }

    private static long Swing(string kind, Monster target)
    {
        var hero = Hero(CharacterClass.Ranger);
        hero.Strength = 5_000;   // a hit well above the Defence, so no floor of 1 hides the cut
        hero.Dexterity = 5_000;  // the ranged hit is Dexterity based
        if (kind == "ranged")
            hero.EquippedItems[EquipmentSlot.MainHand] = EquipmentDatabase.RegisterDynamic(new Equipment
            {
                Name = "PR136 Bow", Slot = EquipmentSlot.MainHand, WeaponType = WeaponType.Bow, WeaponPower = 100,
                MinLevel = 1, Rarity = EquipmentRarity.Common,
            });
        return NoHitRoll(() =>
        {
            var engine = Engine(hero, null);
            long before = target.HP;
            string method = kind switch
            {
                "power" => "ExecutePowerAttackMultiMonster",
                "precise" => "ExecutePreciseStrikeMultiMonster",
                _ => "ExecuteRangedAttackMultiMonster",
            };
            Wait(typeof(CombatEngine).GetMethod(method, F)!
                .Invoke(engine, new object?[] { hero, new List<Monster> { target }, (int?)0, new CombatResult { Player = hero } }));
            return before - target.HP;
        });
    }

    [Theory]
    [InlineData("power", 160, 180)]     // d = 800 + up to 100; d - 0.8 d
    [InlineData("precise", 115, 140)]   // the same, then the 0.75 aim cut
    [InlineData("ranged", 160, 180)]
    public void ProtectRiders136_Weaken_PowerPreciseAndRanged_TakeTwentyPercentOffTheirDefenceOnce(string kind, long lo, long hi)
    {
        long full = Swing(kind, Foe(defence: 800));
        long weak = Swing(kind, Weakened(Foe(defence: 800)));
        full.Should().BeGreaterThan(1_000);
        (weak - full).Should().BeInRange(lo, hi, "20 percent of the Defence it reads, once (twice would be about 290 to 320)");
    }

    [Fact]
    public void ProtectRiders136_Weaken_DesperateStrikesRider_TakesTheCutToo()
    {
        // base block Defence / 2 (800 to 640: 160) plus the rider's Defence / 4 (400 to 320: 80)
        long full = Act("desperate_strike", 5_000, Foe(defence: 1_600));
        long weak = Act("desperate_strike", 5_000, Weakened(Foe(defence: 1_600)));
        (weak - full).Should().Be(240);
    }

    [Fact]
    public void ProtectRiders136_Weaken_JudgmentDaysHolyHit_TakesTheCutToo()
    {
        // aoe_holy: ArmPow / 2 = 1000; weakened, 800
        long full = Act("judgment_day", 5_000, Foe(armPow: 2_000), Hero(CharacterClass.Paladin));
        long weak = Act("judgment_day", 5_000, Weakened(Foe(armPow: 2_000)), Hero(CharacterClass.Paladin));
        full.Should().BeGreaterThan(0);
        (weak - full).Should().Be(200, "its own handler only (an own-damage effect), one target");
    }

    [Fact]
    public void ProtectRiders136_Weaken_FrenzysExtraHits_TakeTheCutToo()
    {
        // the base block's Defence / 2 gives 200 more (1000 to 800); each extra hit's Defence / 4 adds more
        long full = Act("frenzy", 9_000, Foe(defence: 2_000), Hero(CharacterClass.Barbarian));
        long weak = Act("frenzy", 9_000, Weakened(Foe(defence: 2_000)), Hero(CharacterClass.Barbarian));
        (weak - full).Should().BeGreaterThan(200, "the extra hits take the cut as well as the base block");
    }

    [Fact]
    public void ProtectRiders136_CuttingWords_ItsWeakenCutsTheDefenceOfTheNextHit()
    {
        var foe = Foe(armPow: 1_000);
        Act("cutting_words", 100, foe, Hero(CharacterClass.Bard));
        foe.WeakenRounds.Should().BeGreaterThan(0, "Cutting Words weakens");
        Single(foe, 5_000, spell: false).Should().Be(4_200, "-20 percent defence, as its text says");
        foe.GetDefensePower().Should().Be((long)(1_000 * 0.80f), "GetDefensePower keeps its one cut");
    }

    // ---------- rule 5: the Poisoned status adds no attack (X5) ----------

    private static Monster Created(bool poisoned) => Monster.CreateMonster(20, "Spider", 1_000, 100, 10, "", false, false,
        "", "", poisoned, false, 30, 10, 40);

    [Fact]
    public void ProtectRiders136_APoisonedMonstersAttack_EqualsItsUnpoisonedAttack()
    {
        var m = Foe();
        long clean = m.GetAttackPower();
        m.Poisoned = true;
        m.PoisonRounds = 5;
        m.GetAttackPower().Should().Be(clean);
    }

    [Fact]
    public void ProtectRiders136_PoisonFromASpellOrAnAbility_AddsNoAttack()
    {
        var a = Created(poisoned: false);
        long clean = a.GetAttackPower();
        SpellEffect("poison", a, null);
        a.Poisoned.Should().BeTrue("Poison Touch poisons");
        a.GetAttackPower().Should().Be(clean);

        var b = Foe();
        long cleanB = b.GetAttackPower();
        Act("poison_blade", 100, b);
        b.Poisoned.Should().BeTrue("Poison Blade poisons");
        b.GetAttackPower().Should().Be(cleanB);
    }

    [Fact]
    public void ProtectRiders136_AMonsterPoisonousByItsOwnData_KeepsItsFive()
    {
        // the Portal Guardian (DungeonLocation) is created poisoned
        Created(poisoned: true).GetAttackPower().Should().Be(Created(poisoned: false).GetAttackPower() + 5);
    }
}
