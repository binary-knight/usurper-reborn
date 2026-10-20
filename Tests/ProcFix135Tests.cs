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
/// 1.3.5 enchant audit rows R1 to R3 (EF-8F). R1: every on-hit effect scales from the HP the hit removed (after
/// armour, divine armour and the Armory, a phase immunity residual included, never more than the target had); an
/// evaded hit procs nothing. R2: the frost proc and the Frostbrand slow go through the boss soft-control resist, and
/// every slow counts down once a round. R3: a magical handler ability fires no weapon proc and no lifesteal.
/// </summary>
[Collection("SharedGameSingletons")]
public class ProcFix135Tests
{
    private const BindingFlags F = BindingFlags.NonPublic | BindingFlags.Instance;
    private const long D = 1000;
    private const int LifeStealPct = 60;

    private static CombatEngine Engine(Character player, int seed)
    {
        var engine = new CombatEngine(new TerminalEmulator(new MemoryStream(), new MemoryStream()));
        engine.SeedRandomForTests(seed);
        typeof(CombatEngine).GetField("currentPlayer", F)!.SetValue(engine, player);
        typeof(CombatEngine).GetField("currentTeammates", F)!.SetValue(engine, new List<Character>());
        return engine;
    }

    private static Character Hero(bool fire = false, bool frost = false, int lifeSteal = 0)
    {
        var hero = new Character
        {
            Name1 = "Hero", Name2 = "Hero", Class = CharacterClass.Warrior, Race = CharacterRace.Human, Level = 60,
            Strength = 200, HP = 1_000, MaxHP = 1_000_000, CombatSpeed = CombatSpeed.Instant,
        };
        hero.EquippedItems[EquipmentSlot.MainHand] = EquipmentDatabase.RegisterDynamic(new Equipment
        {
            Name = "Proc Blade", Slot = EquipmentSlot.MainHand, WeaponType = WeaponType.Sword, WeaponPower = 100,
            MinLevel = 1, Rarity = EquipmentRarity.Common, HasFireEnchant = fire, HasFrostEnchant = frost, LifeSteal = lifeSteal,
        });
        return hero;
    }

    private static Monster God(bool physicalImmune = false) => new Monster
    {
        Name = "Terravok", Level = 60, HP = 10_000_000, MaxHP = 10_000_000, Strength = 10, Defence = 0, ArmPow = 0,
        IsBoss = true, IsActive = true, IsPhysicalImmune = physicalImmune,
    };

    private static Monster Plain() => new Monster
    {
        Name = "Cave Bear", Level = 60, HP = 10_000_000, MaxHP = 10_000_000, Strength = 10, Defence = 0, ArmPow = 0,
        IsActive = true,
    };

    private static double TerravokDivineArmor(Character hero) =>
        (double)typeof(OldGodBossSystem).GetMethod("DivineArmorReduction", BindingFlags.NonPublic | BindingFlags.Static)!
            .Invoke(null, new object[] { OldGodType.Terravok, hero })!;

    private static long Dealt(CombatEngine engine, Monster m, long damage, Character attacker, CombatResult result) =>
        ((Task<long>)typeof(CombatEngine).GetMethod("ApplySingleMonsterDamageDealt", F)!
            .Invoke(engine, new object?[] { m, damage, result, "your attack", attacker, false, false })!).GetAwaiter().GetResult();

    private static void OnHit(CombatEngine engine, Monster m, long damage, Character attacker, CombatResult result) =>
        typeof(CombatEngine).GetMethod("ApplyPlayerSwingOnHitEffects", F)!
            .Invoke(engine, new object[] { attacker, m, damage, false, result });

    private static void PowerAttack(CombatEngine engine, Character hero, Monster m, CombatResult result)
    {
        hero.CurrentCombatStamina = 1_000;
        ((Task)typeof(CombatEngine).GetMethod("ExecutePowerAttackMultiMonster", F)!
            .Invoke(engine, new object?[] { hero, new List<Monster> { m }, 0, result })!).GetAwaiter().GetResult();
    }

    // ---- R1 ----

    [Fact]
    public void ProcFix135_R1a_PowerAttackOnADivineArmorGod_FireAndLifedrinkerScaleFromTheHpRemoved()
    {
        var hero = Hero(fire: true, lifeSteal: LifeStealPct);
        double divine = TerravokDivineArmor(hero);
        divine.Should().BeGreaterThan(0, "Terravok keeps divine armour against an enchanted weapon");
        var engine = Engine(hero, 135);
        engine.BossContext = new BossCombatContext { DivineArmorReduction = divine };

        int landed = 0, fired = 0;
        for (int i = 0; i < 300; i++)
        {
            var god = God();
            hero.HP = 1_000;
            PowerAttack(engine, hero, god, new CombatResult { Player = hero });
            long lost = god.MaxHP - god.HP;
            long healed = hero.HP - 1_000;
            if (lost == 0) continue;   // the to-hit roll missed
            landed++;

            // the hit removed d; Lifedrinker heals d * 60 / 100; a fire proc adds d * 15 / 100 on top of d
            bool matched = false, withFire = false;
            for (long d = Math.Max(1, healed * 100 / LifeStealPct - 3); d <= healed * 100 / LifeStealPct + 3; d++)
            {
                if (Math.Max(1, d * LifeStealPct / 100) != healed) continue;
                long fire = Math.Max(1, (long)(d * GameConfig.FireEnchantDamageMultiplier));
                if (lost == d) { matched = true; break; }
                if (lost == d + fire) { matched = true; withFire = true; break; }
            }
            matched.Should().BeTrue($"the heal ({healed}) and the fire proc scale from the HP the hit removed (lost {lost})");
            if (withFire) fired++;
        }
        engine.BossContext = null;
        landed.Should().BeGreaterThan(100);
        fired.Should().BeGreaterThan(0, "the fire proc fires on some of the landed hits");
    }

    [Fact]
    public void ProcFix135_R1b_AHitIntoAPhysicalImmunityPhase_ProcsFromTheResidual_AnEvadedHitProcsNothing()
    {
        var hero = Hero(fire: true, lifeSteal: LifeStealPct);
        var engine = Engine(hero, 135);
        engine.BossContext = new BossCombatContext { DivineArmorReduction = TerravokDivineArmor(hero), HasPhysicalImmunityPhase = true };

        // the residual lands, and the on-hit figure is that residual (supervisor, ledger 2026-10-18T21:17:17Z)
        var probe = God(physicalImmune: true);
        long dealt = Dealt(engine, probe, D, hero, new CombatResult { Player = hero });
        dealt.Should().BeGreaterThan(0, "the on-hit effects scale from the immunity residual the hit removed");
        dealt.Should().Be(probe.MaxHP - probe.HP);

        int landed = 0;
        for (int i = 0; i < 200; i++)
        {
            var god = God(physicalImmune: true);
            hero.HP = 1_000;
            PowerAttack(engine, hero, god, new CombatResult { Player = hero });
            long lost = god.MaxHP - god.HP;
            if (lost == 0) continue;
            landed++;
            long healed = hero.HP - 1_000;
            healed.Should().BeGreaterThan(0, "Lifedrinker heals from the residual of an immune hit");
            (healed * 100 / LifeStealPct).Should().BeLessThanOrEqualTo(lost, "the heal scales from the HP removed, not the swing");
        }
        engine.BossContext = null;
        landed.Should().BeGreaterThan(50);

        // an evaded hit (Incorporeal and the like) still procs nothing
        var ghost = Plain();
        ghost.EvasionRounds = 3; ghost.EvasionMissChance = 100;
        Dealt(engine, ghost, D, hero, new CombatResult { Player = hero }).Should().Be(0, "an evaded hit runs no on-hit effect");
        ghost.HP.Should().Be(ghost.MaxHP);
    }

    [Fact]
    public void ProcFix135_R1e_AKillingBlow_ScalesFromTheHpTheTargetHad_NotTheOverkill()
    {
        var hero = Hero(lifeSteal: LifeStealPct);
        var engine = Engine(hero, 135);
        var result = new CombatResult { Player = hero };

        var weak = Plain();
        weak.HP = 100;
        Dealt(engine, weak, D, hero, result).Should().Be(100, "the on-hit base is capped at the HP before the hit");

        // the live Power Attack on a target with little HP left heals from that HP only
        int kills = 0;
        for (int i = 0; i < 100; i++)
        {
            var m = Plain();
            m.HP = 100;
            hero.HP = 1_000;
            PowerAttack(engine, hero, m, new CombatResult { Player = hero });
            if (m.HP == 100) continue;
            kills++;
            (hero.HP - 1_000).Should().Be(100 * LifeStealPct / 100, "Lifedrinker heals from the 100 HP removed, not the overkill");
        }
        kills.Should().BeGreaterThan(20);
    }

    [Fact]
    public void ProcFix135_R1c_AnOrdinaryMonsterWithNoArmourCut_IsUnchanged()
    {
        var hero = Hero(lifeSteal: LifeStealPct);
        var engine = Engine(hero, 135);
        var result = new CombatResult { Player = hero };

        var bear = Plain();
        long dealt = Dealt(engine, bear, D, hero, result);
        dealt.Should().Be(TeamHQBonus.ApplyAttack(hero, D), "the figure the callers passed before 1.3.5");
        (bear.MaxHP - bear.HP).Should().Be(dealt);

        hero.HP = 1_000;
        OnHit(engine, bear, dealt, hero, result);
        (hero.HP - 1_000).Should().Be(D * LifeStealPct / 100, "the same heal as before 1.3.5");

        // the live Power Attack heals the same share of the hit as before
        for (int i = 0; i < 50; i++)
        {
            var m = Plain();
            hero.HP = 1_000;
            PowerAttack(engine, hero, m, new CombatResult { Player = hero });
            long lost = m.MaxHP - m.HP;
            if (lost == 0) continue;
            (hero.HP - 1_000).Should().Be(Math.Max(1, lost * LifeStealPct / 100));
        }
    }

    [Fact]
    public void ProcFix135_R1d_EveryOnHitCallerOfTheSingleHit_PassesTheHpRemoved()
    {
        var dir = new DirectoryInfo(Directory.GetCurrentDirectory());
        while (dir != null && !File.Exists(Path.Combine(dir.FullName, "usurper-reloaded.csproj")))
            dir = dir.Parent;
        dir.Should().NotBeNull();
        var src = File.ReadAllText(Path.Combine(dir!.FullName, "Scripts", "Systems", "CombatEngine.cs"));

        // the basic swing, its off hand after a Power Attack, the ability off hand and the teammate swing
        string[] callers =
        {
            "ApplyPlayerSwingOnHitEffects(player, target, swingDealt, isOffHandAttack, result);",
            "ApplyPlayerSwingOnHitEffects(player, target, powerDealt, isOffHandAttack: false, result);",
            "ApplyPlayerSwingOnHitEffects(player, offHandTarget, ohDealt, isOffHandAttack: true, result);",
            "ApplyPostHitEnchantments(player, offHandTarget, ohDealt, result, weaponSlot: EquipmentSlot.OffHand);",
            "ApplyPostHitEnchantments(teammate, target, tmDealt, result,",
        };
        foreach (var c in callers)
            src.Should().Contain(c);
        src.Should().NotContain("ApplyPlayerSwingOnHitEffects(player, target, TeamHQBonus.ApplyAttack(");
        src.Should().NotContain("ApplyPlayerSwingOnHitEffects(player, offHandTarget, TeamHQBonus.ApplyAttack(");
        src.Should().NotContain("ApplyPostHitEnchantments(player, offHandTarget, TeamHQBonus.ApplyAttack(");
        src.Should().NotContain("ApplyPostHitEnchantments(teammate, target, TeamHQBonus.ApplyAttack(");
    }

    // ---- R2 ----

    [Fact]
    public void ProcFix135_R2a_FrostProcOnABoss_GoesThroughTheSoftControlResist_ARegularMonsterIsUnchanged()
    {
        var hero = Hero(frost: true);
        var postHit = typeof(CombatEngine).GetMethod("ApplyPostHitEnchantments", F)!;
        int bossRounds = Math.Max(1, GameConfig.FrostEnchantDuration / 2);

        int regularSlowed = 0, bossSlowed = 0, resisted = 0;
        for (int seed = 0; seed < 600; seed++)
        {
            // the same seed for both: the draws before the frost proc are the same, so a boss proc is a regular proc
            var regular = Plain();
            postHit.Invoke(Engine(hero, seed), new object[] { hero, regular, D, new CombatResult { Player = hero }, false, EquipmentSlot.MainHand, false });
            var boss = God();
            postHit.Invoke(Engine(hero, seed), new object[] { hero, boss, D, new CombatResult { Player = hero }, false, EquipmentSlot.MainHand, false });

            if (regular.IsSlowed)
            {
                regularSlowed++;
                regular.SlowDuration.Should().Be(GameConfig.FrostEnchantDuration, "a regular monster takes the full frost slow");
            }
            if (boss.IsSlowed)
            {
                bossSlowed++;
                regular.IsSlowed.Should().BeTrue();
                boss.SlowDuration.Should().Be(bossRounds, "a boss takes SoftControlRounds' rounds of the frost slow");
            }
            else if (regular.IsSlowed)
            {
                resisted++;
                boss.SlowDuration.Should().Be(0, "a resisted proc applies no slow");
            }
        }
        regularSlowed.Should().BeGreaterThan(0);
        bossSlowed.Should().BeGreaterThan(0);
        resisted.Should().BeGreaterThan(0, "a boss resists the frost proc a share of the time");
    }

    private static void Round(CombatEngine engine, Monster m, Character hero, int actions)
    {
        var process = typeof(CombatEngine).GetMethod("ProcessMonsterAction", F)!;
        m.StatusTickedThisRound = false;
        for (int a = 0; a < actions; a++)
            ((Task)process.Invoke(engine, new object?[] { m, hero, new CombatResult { Player = hero }, null })!).GetAwaiter().GetResult();
    }

    [Fact]
    public void ProcFix135_R2b_ASlowOnABossActingTwiceARound_LosesOneRoundPerRound_ASageSlowToo()
    {
        var hero = Hero();
        hero.HP = 1_000_000;
        var engine = Engine(hero, 135);

        var boss = God();
        boss.IsSlowed = true;
        boss.SlowDuration = GameConfig.FrostEnchantDuration;
        Round(engine, boss, hero, 2);
        boss.SlowDuration.Should().Be(GameConfig.FrostEnchantDuration - 1, "a slow counts down once a round");
        boss.IsSlowed.Should().BeTrue();
        Round(engine, boss, hero, 2);
        boss.SlowDuration.Should().Be(GameConfig.FrostEnchantDuration - 2);
        boss.IsSlowed.Should().BeFalse();

        // the Sage's Dulling Mist (2 rounds on a regular monster) on a monster acting twice a round
        var sage = typeof(CombatEngine).GetMethod("ApplySageControl", F)!;
        var m = Plain();
        sage.Invoke(engine, new object?[] { m, "dulling_mist", 2, hero, new CombatResult { Player = hero } });
        m.SlowDuration.Should().Be(2);
        Round(engine, m, hero, 2);
        m.SlowDuration.Should().Be(1, "Dulling Mist's 2 rounds count once a round");
        m.IsSlowed.Should().BeTrue();
    }

    [Fact]
    public void ProcFix135_R2c_AFrostSpellSlow_CountsOnceARound()
    {
        var hero = Hero();
        hero.HP = 1_000_000;
        var engine = Engine(hero, 135);
        var spellEffect = typeof(CombatEngine).GetMethod("HandleSpecialSpellEffectOnMonster", F)!;
        var boss = God();
        spellEffect.Invoke(engine, new object?[] { boss, "frost", 0, hero, 0L, new CombatResult { Player = hero } });
        boss.SlowDuration.Should().Be(2, "Frost Touch and Ice Storm slow for 2 rounds");
        Round(engine, boss, hero, 2);
        boss.SlowDuration.Should().Be(1, "a frost spell's slow joins the once-a-round countdown");
        boss.IsSlowed.Should().BeTrue();
        Round(engine, boss, hero, 2);
        boss.IsSlowed.Should().BeFalse();
    }

    [Fact]
    public void ProcFix135_R2d_FrostbrandSlowOnABoss_GoesThroughTheSoftControlResist_ARegularMonsterIsUnchanged()
    {
        var hero = Hero();
        hero.Class = CharacterClass.MysticShaman;
        int bossRounds = Math.Max(1, GameConfig.FrostEnchantDuration / 2);
        int bossSlowed = 0, resisted = 0;
        for (int seed = 0; seed < 200; seed++)
        {
            hero.ShamanEnchantType = 2; hero.ShamanEnchantRounds = 5; hero.ShamanEnchantPower = 50;
            var regular = Plain();
            OnHit(Engine(hero, seed), regular, D, hero, new CombatResult { Player = hero });
            regular.IsSlowed.Should().BeTrue("Frostbrand slows a regular monster on every hit");
            regular.SlowDuration.Should().Be(GameConfig.FrostEnchantDuration);

            var boss = God();
            OnHit(Engine(hero, seed), boss, D, hero, new CombatResult { Player = hero });
            if (boss.IsSlowed)
            {
                bossSlowed++;
                boss.SlowDuration.Should().Be(bossRounds, "a boss takes SoftControlRounds' rounds of the Frostbrand slow");
            }
            else
            {
                resisted++;
                boss.SlowDuration.Should().Be(0, "a resisted slow applies nothing");
            }
        }
        bossSlowed.Should().BeGreaterThan(0);
        resisted.Should().BeGreaterThan(0, "a boss resists the Frostbrand slow a share of the time");
    }

    // ---- R3 ----

    [Fact]
    public void ProcFix135_R3a_LightningBoltThroughTheHandler_FiresNoLifestealAndNoProc()
    {
        var hero = Hero(fire: true, lifeSteal: LifeStealPct);
        var engine = Engine(hero, 135);
        var handler = typeof(CombatEngine).GetMethod("ApplyHandlerAbilityDamage", F)!;
        var bolt = new ClassAbilityResult { SpecialEffect = "shaman_lightning_bolt", Damage = (int)D };

        for (int i = 0; i < 100; i++)
        {
            var target = Plain();
            hero.HP = 1_000;
            long dealt = (long)handler.Invoke(engine, new object[] { hero, target, D, new CombatResult { Player = hero }, bolt })!;
            hero.HP.Should().Be(1_000, "a magical handler ability fires no Lifedrinker");
            (target.MaxHP - target.HP).Should().Be(dealt, "a magical handler ability fires no fire proc");
        }
    }

    [Fact]
    public void ProcFix135_R3b_AMeleeHandlerAbility_StillFiresLifestealAndProcs()
    {
        var hero = Hero(fire: true, lifeSteal: LifeStealPct);
        var engine = Engine(hero, 135);
        var handler = typeof(CombatEngine).GetMethod("ApplyHandlerAbilityDamage", F)!;
        var reap = new ClassAbilityResult { SpecialEffect = "execute", Damage = (int)D };

        int healed = 0, fired = 0;
        for (int i = 0; i < 100; i++)
        {
            var target = Plain();
            hero.HP = 1_000;
            long dealt = (long)handler.Invoke(engine, new object[] { hero, target, D, new CombatResult { Player = hero }, reap })!;
            if (hero.HP > 1_000) healed++;
            if (target.MaxHP - target.HP > dealt) fired++;
        }
        healed.Should().Be(100, "Lifedrinker heals on every melee handler hit");
        fired.Should().BeGreaterThan(0, "the fire proc rides on a melee handler hit");
    }
}
