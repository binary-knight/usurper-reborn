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
/// 1.3.5 BP-135: phase immunity of the hit's kind and divine armour on every class ability path that takes HP
/// itself (the handler helper, the base block, the area handlers, their riders and bonus hits), through one step,
/// with one kind per ability for the protections and the weapon procs (KINDS c0d6c61ff57d8d06 and its rulings).
/// </summary>
[Collection("SharedGameSingletons")]
public class BossProtect135Tests
{
    private const BindingFlags F = BindingFlags.NonPublic | BindingFlags.Instance;
    private const int D = 1000;
    private const long GodHp = 10_000_000;

    private static double R => GameConfig.ModBossPhaseImmunityResidual;

    private static CombatEngine Engine(Character player, int seed)
    {
        var engine = new CombatEngine(new TerminalEmulator(new MemoryStream(), new MemoryStream()));
        engine.SeedRandomForTests(seed);
        typeof(CombatEngine).GetField("currentPlayer", F)!.SetValue(engine, player);
        typeof(CombatEngine).GetField("currentTeammates", F)!.SetValue(engine, new List<Character>());
        return engine;
    }

    private static Character Hero(string weapon = "Plain Blade", bool fire = false, int lifeSteal = 0)
    {
        var hero = new Character
        {
            Name1 = "Hero", Name2 = "Hero", Class = CharacterClass.Warrior, Race = CharacterRace.Human, Level = 60,
            Strength = 200, HP = 1_000, MaxHP = 1_000_000, CombatSpeed = CombatSpeed.Instant,
        };
        hero.EquippedItems[EquipmentSlot.MainHand] = EquipmentDatabase.RegisterDynamic(new Equipment
        {
            Name = weapon, Slot = EquipmentSlot.MainHand, WeaponType = WeaponType.Sword, WeaponPower = 100,
            MinLevel = 1, Rarity = EquipmentRarity.Common, HasFireEnchant = fire, LifeSteal = lifeSteal,
        });
        return hero;
    }

    private static Monster God(bool physical = false, bool magical = false, long hp = GodHp,
        MonsterClass kind = MonsterClass.Normal) => new Monster
    {
        Name = "Terravok", Level = 95, HP = hp, MaxHP = GodHp, Strength = 10, Defence = 0, ArmPow = 0,
        IsBoss = true, IsActive = true, IsPhysicalImmune = physical, IsMagicalImmune = magical, MonsterClass = kind,
    };

    private static Monster Weak() => new Monster
    {
        Name = "Thrall", Level = 10, HP = 10, MaxHP = 10, Strength = 1, Defence = 0, ArmPow = 0, IsActive = true,
    };

    private static ClassAbilityResult Use(string id, int damage = D)
    {
        var ability = ClassAbilitySystem.GetAbility(id);
        ability.Should().NotBeNull(id);
        return new ClassAbilityResult { Success = true, AbilityUsed = ability, Damage = damage, SpecialEffect = ability!.SpecialEffect };
    }

    private static double Divine(OldGodType god, Character hero) =>
        (double)typeof(OldGodBossSystem).GetMethod("DivineArmorReduction", BindingFlags.NonPublic | BindingFlags.Static)!
            .Invoke(null, new object[] { god, hero })!;

    private static T NoHitRoll<T>(Func<T> body)
    {
        bool saved = GameConfig.PlayerHitRollEnabled;
        try { GameConfig.PlayerHitRollEnabled = false; return body(); }
        finally { GameConfig.PlayerHitRollEnabled = saved; }
    }

    /// <summary>One ability action through ApplyAbilityEffectsMultiMonster; returns the HP each monster lost.</summary>
    private static long[] Act(string id, int seed, BossCombatContext? ctx, Character hero, params Monster[] monsters) =>
        NoHitRoll(() =>
        {
            var engine = Engine(hero, seed);
            engine.BossContext = ctx;
            var before = Array.ConvertAll(monsters, m => m.HP);
            var result = new CombatResult { Player = hero };
            foreach (var m in monsters) result.Monsters.Add(m);
            ((Task)typeof(CombatEngine).GetMethod("ApplyAbilityEffectsMultiMonster", F)!
                .Invoke(engine, new object?[] { hero, monsters[0], new List<Monster>(monsters), Use(id), result })!).GetAwaiter().GetResult();
            var lost = new long[monsters.Length];
            for (int i = 0; i < monsters.Length; i++) lost[i] = before[i] - Math.Max(0, monsters[i].HP);
            return lost;
        });

    /// <summary>One hit through ApplyHandlerAbilityDamage; returns (dealt, HP lost).</summary>
    private static (long dealt, long lost) Handler(string id, int seed, BossCombatContext? ctx, Character hero, Monster m)
    {
        var engine = Engine(hero, seed);
        engine.BossContext = ctx;
        long before = m.HP;
        long dealt = (long)typeof(CombatEngine).GetMethod("ApplyHandlerAbilityDamage", F)!
            .Invoke(engine, new object[] { hero, m, (long)D, new CombatResult { Player = hero }, Use(id) })!;
        return (dealt, before - m.HP);
    }

    private static long Cut(long full) => (long)(full * R);
    private static long Armoured(long full, double divine) => Math.Max(1, (long)(full * (1.0 - divine)));

    // ---- the kind of each ability (KINDS rows A to D, with the supervisor's rulings) ----

    private static readonly string[] MagicalIds =
    {
        // STOP rows that keep the kind the code gave them (MagicalAbilityEffects says magical)
        "divine_smite", "holy_smite", "dissonant_whispers", "deadly_joke", "last_laugh", "throw_bomb",
        // decided magical where the set agrees
        "lightning_bolt",
        // the nine rows where the description wins
        "holy_avenger", "purifying_flame", "searing_totem", "corrupting_touch",
        "fracture_reality", "seal_fracture", "soul_leech", "consume_soul", "annihilation",
        // area handler rows decided magical
        "judgment_day", "wrath_of_heaven", "void_rupture", "chain_lightning", "sanctified_torrent",
        "entropy_cascade", "singularity", "abyssal_eruption", "abyss_unchained",
    };

    private static readonly string[] PhysicalIds =
    {
        "power_strike", "shield_bash", "holy_shield_slam", "execute", "crushing_blow", "last_stand", "savage_charge",
        "maelketh_fury", "champion_strike", "wild_swing", "reckless_attack", "frenzy", "avatar_of_destruction",
        "backstab", "poison_blade", "biaxin", "assassinate", "precise_shot", "legendary_shot", "mock", "cutting_words",
        "ballad_of_blades", "jugglers_trick", "pratfall", "acid_splash", "frost_bomb", "desperate_strike",
        "riptide_strike", "wave_echo", "resonance_cascade", "harmonic_crescendo", "shadow_harvest", "hungering_strike",
        "wrath_of_the_deep", "cycles_end", "reap", "devour", "entropic_blade",
        "crescendo", "grand_finale", "corrosive_cloud", "death_blossom",
    };

    [Fact]
    public void BossProtect135_Kinds_EveryAbilityOnAnUnprotectedPath_HasItsRuledKind()
    {
        (MagicalIds.Length + PhysicalIds.Length).Should().Be(67, "KINDS has 69 rows on 67 abilities (two riders repeat one)");
        foreach (var id in MagicalIds)
            CombatEngine.IsMagicalAbility(Use(id)).Should().BeTrue($"{id} is magical");
        foreach (var id in PhysicalIds)
            CombatEngine.IsMagicalAbility(Use(id)).Should().BeFalse($"{id} is physical");
        // a result with no ability keeps the effect's kind (the older callers)
        CombatEngine.IsMagicalAbility(new ClassAbilityResult { SpecialEffect = "shaman_lightning_bolt" }).Should().BeTrue();
        CombatEngine.IsMagicalAbility(new ClassAbilityResult { SpecialEffect = "execute" }).Should().BeFalse();
        // the kind is keyed by ability: the other ability with purifying_flame's effect id stays physical
        foreach (var other in new[] { "cleanse", "holy_avenger", "soul_leech" })
        {
            var shared = new ClassAbilityResult { SpecialEffect = other };
            CombatEngine.IsMagicalAbility(shared).Should().BeFalse($"the effect id {other} alone is not magical");
        }
    }

    // ---- the handler path ----

    [Theory]
    [InlineData("reap", false)]
    [InlineData("fracture_reality", true)]
    [InlineData("lightning_bolt", true)]
    [InlineData("soul_leech", true)]
    public void BossProtect135_Handler_ImmunityCutsTheHitOfItsKind_AndLetsTheOtherKindThrough(string id, bool magical)
    {
        for (int seed = 0; seed < 20; seed++)
        {
            long full = Handler(id, seed, null, Hero(), God()).lost;
            var (pDealt, pLost) = Handler(id, seed, null, Hero(), God(physical: true));
            var (mDealt, mLost) = Handler(id, seed, null, Hero(), God(magical: true));
            pLost.Should().Be(magical ? full : Cut(full), $"{id} in a physical immunity phase");
            mLost.Should().Be(magical ? Cut(full) : full, $"{id} in a magical immunity phase");
            pDealt.Should().Be(pLost);
            mDealt.Should().Be(mLost);
        }
    }

    [Theory]
    [InlineData("Plain Blade", false, GameConfig.TerravokStoneSkin)]
    [InlineData("Ember Blade", true, GameConfig.TerravokStoneSkin * (1.0 - GameConfig.BossDivineArmorEnchantedBypass))]
    [InlineData("Artifact Blade", false, 0.0)]
    public void BossProtect135_DivineArmour_CutsEveryPathAsTheSwingDoes(string weapon, bool fire, double expected)
    {
        var armed = Hero(weapon, fire);
        double divine = Divine(OldGodType.Terravok, armed);
        divine.Should().BeApproximately(expected, 1e-9, "full, halved by an enchanted weapon, removed by an Artifact weapon");
        var ctx = new BossCombatContext { DivineArmorReduction = divine };
        for (int seed = 0; seed < 10; seed++)
        {
            // handler, base block, area case: the hit itself is the same plain hero's, the divine armour is the fight's
            long hFull = Handler("reap", seed, null, Hero(), God()).lost;
            Handler("reap", seed, ctx, Hero(), God()).lost.Should().Be(Armoured(hFull, divine), "the handler path");
            long gFull = Act("power_strike", seed, null, Hero(), God())[0];
            Act("power_strike", seed, ctx, Hero(), God())[0].Should().Be(Armoured(gFull, divine), "the base block");
            long aFull = Act("singularity", seed, null, Hero(), God())[0];
            Act("singularity", seed, ctx, Hero(), God())[0].Should().Be(Armoured(aFull, divine), "a direct-HP area case");
        }
    }

    [Fact]
    public void BossProtect135_MaelkethVelouraAndARegularMonster_AreUnchanged()
    {
        var hero = Hero();
        Divine(OldGodType.Maelketh, hero).Should().Be(0);
        Divine(OldGodType.Veloura, hero).Should().Be(0);
        foreach (var god in new[] { OldGodType.Maelketh, OldGodType.Veloura })
        {
            var ctx = new BossCombatContext { GodType = god, DivineArmorReduction = Divine(god, hero) };
            for (int seed = 0; seed < 10; seed++)
            {
                Handler("reap", seed, ctx, Hero(), God()).lost.Should().Be(Handler("reap", seed, null, Hero(), God()).lost);
                Act("power_strike", seed, ctx, Hero(), God())[0].Should().Be(Act("power_strike", seed, null, Hero(), God())[0]);
                Act("singularity", seed, ctx, Hero(), God())[0].Should().Be(Act("singularity", seed, null, Hero(), God())[0]);
            }
        }
        // a monster that is not a god takes the hit as before: the damage, or the damage times the crit
        for (int seed = 0; seed < 20; seed++)
        {
            var regular = new Monster { Name = "Cave Bear", Level = 60, HP = GodHp, MaxHP = GodHp, IsActive = true };
            long lost = Handler("reap", seed, null, Hero(), regular).lost;
            (lost == D || lost > D).Should().BeTrue("no protection reduces a regular monster's hit");
        }
    }

    // ---- the base block ----

    [Theory]
    [InlineData("power_strike", false)]
    [InlineData("holy_avenger", true)]
    [InlineData("divine_smite", true)]
    public void BossProtect135_BaseBlock_ImmunityCutsTheHitOfItsKind_AndLetsTheOtherKindThrough(string id, bool magical)
    {
        for (int seed = 0; seed < 20; seed++)
        {
            long full = Act(id, seed, null, Hero(), God())[0];
            Act(id, seed, null, Hero(), God(physical: true))[0].Should().Be(magical ? full : Math.Max(1, Cut(full)), $"{id}, physical phase");
            Act(id, seed, null, Hero(), God(magical: true))[0].Should().Be(magical ? Math.Max(1, Cut(full)) : full, $"{id}, magical phase");
        }
    }

    // ---- the direct-HP area cases ----

    [Theory]
    [InlineData("death_blossom", false)]
    [InlineData("crescendo", false)]
    [InlineData("singularity", true)]
    [InlineData("chain_lightning", true)]
    [InlineData("judgment_day", true)]
    public void BossProtect135_AreaCase_ImmunityCutsTheHitOfItsKind_AndLetsTheOtherKindThrough(string id, bool magical)
    {
        for (int seed = 0; seed < 10; seed++)
        {
            long full = Act(id, seed, null, Hero(), God())[0];
            full.Should().BeGreaterThan(0);
            Act(id, seed, null, Hero(), God(physical: true))[0].Should().Be(magical ? full : Cut(full), $"{id}, physical phase");
            Act(id, seed, null, Hero(), God(magical: true))[0].Should().Be(magical ? Cut(full) : full, $"{id}, magical phase");
        }
    }

    // ---- the riders ----

    [Fact]
    public void BossProtect135_OverflowSpread_TakesTheGodsProtections()
    {
        // Seal Fracture kills the thrall; the overflow spreads to the god, a magical hit
        for (int seed = 0; seed < 10; seed++)
        {
            long full = Act("seal_fracture", seed, null, Hero(), Weak(), God())[1];
            full.Should().BeGreaterThan(0, "the overflow reaches the god");
            Act("seal_fracture", seed, null, Hero(), Weak(), God(physical: true))[1].Should().Be(full, "a magical spread in a physical phase");
            Act("seal_fracture", seed, null, Hero(), Weak(), God(magical: true))[1].Should().Be(Cut(full), "a magical spread in a magical phase");
        }
    }

    [Fact]
    public void BossProtect135_EchoRepeat_RepeatsTheProtectedHit()
    {
        int echoes = 0;
        for (int seed = 0; seed < 60; seed++)
        {
            long full = Act("fracture_reality", seed, null, Hero(), God())[0];
            long cut = Act("fracture_reality", seed, null, Hero(), God(magical: true))[0];
            var (one, _) = Handler("fracture_reality", seed, null, Hero(), God());
            bool echoed = full == 2 * one;
            if (echoed) echoes++;
            cut.Should().Be(echoed ? 2 * Cut(one) : Cut(one), "the echo deals the hit the protections left, once cut");
        }
        echoes.Should().BeGreaterThan(0, "a quarter of the casts echo");
    }

    [Theory]
    [InlineData("soul_leech", 0.40, true)]
    [InlineData("wrath_of_the_deep", 0.50, false)]
    [InlineData("devour", 0.30, false)]
    public void BossProtect135_HealRiders_HealFromTheHpTheProtectedHitRemoved(string id, double rate, bool magical)
    {
        for (int seed = 0; seed < 10; seed++)
        {
            var hero = Hero();
            var god = God(physical: !magical, magical: magical);
            long lost = Act(id, seed, null, hero, god)[0];
            lost.Should().BeLessThan(D, "the immunity phase of its kind cuts the hit");
            (hero.HP - 1_000).Should().Be((long)(lost * rate), $"{id} heals from what the protected hit removed");

            // a killing blow heals from the HP the target had, not the overkill
            var killer = Hero();
            var weak = Weak();
            weak.IsBoss = true;   // no instant kill branch for Wrath of the Deep
            Act(id, seed, null, killer, weak);
            (killer.HP - 1_000).Should().Be((long)(10 * rate), $"{id} heals from the 10 HP the thrall had");
        }
    }

    // ---- bonus damage riders and the two-hit loops (KINDS section E) ----

    [Fact]
    public void BossProtect135_ExecuteBonus_IsCutInAPhysicalPhase()
    {
        for (int seed = 0; seed < 10; seed++)
        {
            long full = Act("execute", seed, null, Hero(), God(hp: 2_000_000))[0];
            long baseHit = full - D;   // below 30 percent the bonus is the ability's damage
            Act("execute", seed, null, Hero(), God(physical: true, hp: 2_000_000))[0].Should().Be(Cut(baseHit) + Cut(D));
        }
    }

    [Fact]
    public void BossProtect135_DesperateBonus_IsCutInAPhysicalPhase()
    {
        for (int seed = 0; seed < 10; seed++)
        {
            long full = Act("desperate_strike", seed, null, Hero(), God())[0];
            long baseHit = full - D / 2;
            Act("desperate_strike", seed, null, Hero(), God(physical: true))[0].Should().Be(Cut(baseHit) + Cut(D / 2));
        }
    }

    [Theory]
    [InlineData("throw_bomb", MonsterClass.Beast, D / 2)]
    [InlineData("divine_smite", MonsterClass.Undead, D)]
    [InlineData("holy_avenger", MonsterClass.Undead, D * 3 / 4)]
    public void BossProtect135_MagicalBonusRiders_AreCutInAMagicalPhase_AndPassInAPhysicalOne(string id, MonsterClass kind, int bonus)
    {
        for (int seed = 0; seed < 10; seed++)
        {
            long full = Act(id, seed, null, Hero(), God(kind: kind))[0];
            long baseHit = full - bonus;
            Act(id, seed, null, Hero(), God(magical: true, kind: kind))[0].Should().Be(Cut(baseHit) + Cut(bonus), $"{id}, magical phase");
            Act(id, seed, null, Hero(), God(physical: true, kind: kind))[0].Should().Be(full, $"{id}, physical phase");
        }
    }

    [Fact]
    public void BossProtect135_FrenzyHits_AreEachCutInAPhysicalPhase()
    {
        for (int seed = 0; seed < 10; seed++)
        {
            long full = Act("frenzy", seed, null, Hero(), God())[0];
            long cut = Act("frenzy", seed, null, Hero(), God(physical: true))[0];
            cut.Should().BeLessOrEqualTo((long)(full * R) + 1, "every frenzy hit is cut");
            cut.Should().BeGreaterOrEqualTo((long)(full * R) - 6, "each of at most five hits loses under 1 to rounding");
        }
    }

    [Fact]
    public void BossProtect135_VoidRuptureExplosion_IsCutInAMagicalPhase()
    {
        for (int seed = 0; seed < 10; seed++)
        {
            long full = Act("void_rupture", seed, null, Hero(), Weak(), God())[1];
            long cut = Act("void_rupture", seed, null, Hero(), Weak(), God(magical: true))[1];
            // the god takes its loop hit (diminished as the second target) and the thrall's explosion, each cut
            long loopHit = (long)(D * 0.75);
            full.Should().BeGreaterThan(loopHit, "the explosion reaches the god");
            cut.Should().Be(Cut(loopHit) + Cut(full - loopHit));
        }
    }

    [Theory]
    [InlineData("resonance_cascade")]
    [InlineData("harmonic_crescendo")]
    public void BossProtect135_TwoHitLoops_EachHitGoesThroughTheStep(string id)
    {
        for (int seed = 0; seed < 10; seed++)
        {
            long full = Act(id, seed, null, Hero(), God())[0];
            long baseHit = full - D;   // one god: the loop hits it again for the ability's damage
            baseHit.Should().BeGreaterOrEqualTo(D, "the base block hit lands first");
            Act(id, seed, null, Hero(), God(physical: true))[0].Should().Be(Cut(baseHit) + Cut(D), "both hits are cut");
        }
    }

    // ---- the nine description rows: magical immunity cuts them, and they fire no weapon proc or lifesteal ----

    [Theory]
    [InlineData("holy_avenger")]
    [InlineData("purifying_flame")]
    [InlineData("searing_totem")]
    [InlineData("corrupting_touch")]
    [InlineData("fracture_reality")]
    [InlineData("seal_fracture")]
    [InlineData("soul_leech")]
    [InlineData("consume_soul")]
    [InlineData("annihilation")]
    public void BossProtect135_DescriptionRows_AreMagical_CutByMagicalImmunity_NoWeaponProcOrLifesteal(string id)
    {
        for (int seed = 0; seed < 20; seed++)
        {
            long full = Act(id, seed, null, Hero(), God())[0];
            long cut = Act(id, seed, null, Hero(), God(magical: true))[0];
            cut.Should().BeInRange((long)(full * R) - 2, (long)(full * R) + 1, $"{id} is cut by magical immunity");
            Act(id, seed, null, Hero(), God(physical: true))[0].Should().Be(full, $"{id} passes physical immunity");

            // a fire and Lifedrinker weapon adds nothing: the same HP lost and the same heal as a plain weapon
            var plain = Hero();
            var procs = Hero("Ember Blade", fire: true, lifeSteal: 60);
            var a = new Monster { Name = "Cave Bear", Level = 60, HP = GodHp, MaxHP = GodHp, IsActive = true };
            var b = new Monster { Name = "Cave Bear", Level = 60, HP = GodHp, MaxHP = GodHp, IsActive = true };
            long plainLost = Act(id, seed, null, plain, a)[0];
            long procLost = Act(id, seed, null, procs, b)[0];
            procLost.Should().Be(plainLost, $"{id} fires no fire proc");
            procs.HP.Should().Be(plain.HP, $"{id} fires no Lifedrinker");
        }
    }

    [Fact]
    public void BossProtect135_DescriptionRowsInTheBaseBlock_DoNotRollToHit_AsTheAbilitiesGuideSaysOfMagic()
    {
        // characters/abilities.md: "Area abilities, magical abilities, ... do not roll"
        foreach (var id in new[] { "holy_avenger", "purifying_flame", "searing_totem", "corrupting_touch" })
        {
            var used = Use(id, 50);
            CombatEngine.AbilityRollsToHit(used.AbilityUsed!, used, false).Should().BeFalse($"{id} is magical");
        }
        var strike = Use("power_strike", 50);
        CombatEngine.AbilityRollsToHit(strike.AbilityUsed!, strike, false).Should().BeTrue("a physical strike still rolls");
    }

    // ---- the protected paths are unchanged ----

    [Fact]
    public void BossProtect135_ProtectedPaths_Unchanged()
    {
        var hero = Hero();
        var engine = Engine(hero, 135);
        var dealt = typeof(CombatEngine).GetMethod("ApplySingleMonsterDamageDealt", F)!;
        long Single(Monster m, BossCombatContext? ctx)
        {
            engine.BossContext = ctx;
            return ((Task<long>)dealt.Invoke(engine, new object?[] { m, (long)D, new CombatResult { Player = hero }, "your attack", hero, false, false })!)
                .GetAwaiter().GetResult();
        }
        long open = Single(God(), null);
        Single(God(physical: true), null).Should().BeInRange(Cut(open) - 1, Cut(open) + 1, "the single-target path cuts a physical hit once");
        Single(God(magical: true), null).Should().Be(open);
        Single(God(), new BossCombatContext { DivineArmorReduction = GameConfig.TerravokStoneSkin }).Should()
            .BeInRange(Armoured(open, GameConfig.TerravokStoneSkin) - 1, Armoured(open, GameConfig.TerravokStoneSkin) + 1);

        // an "aoe" ability goes through the area core, which protects it once: the base block adds no second cut
        for (int seed = 0; seed < 10; seed++)
        {
            long full = Act("whirlwind", seed, null, Hero(), God())[0];
            Act("whirlwind", seed, null, Hero(), God(physical: true))[0].Should().Be(Cut(full));
            Act("whirlwind", seed, null, Hero(), God(magical: true))[0].Should().Be(full);
        }
    }

    [Fact]
    public void BossProtect135_ImmunityLine_PrintsOncePerTargetPerAction()
    {
        NoHitRoll(() =>
        {
            var hero = Hero();
            var output = new MemoryStream();
            var term = new TerminalEmulator(new MemoryStream(), output);
            var engine = new CombatEngine(term);
            engine.SeedRandomForTests(7);
            typeof(CombatEngine).GetField("currentPlayer", F)!.SetValue(engine, hero);
            typeof(CombatEngine).GetField("currentTeammates", F)!.SetValue(engine, new List<Character>());
            var god = God(physical: true);
            var result = new CombatResult { Player = hero };
            result.Monsters.Add(god);
            ((Task)typeof(CombatEngine).GetMethod("ApplyAbilityEffectsMultiMonster", F)!
                .Invoke(engine, new object?[] { hero, god, new List<Monster> { god }, Use("resonance_cascade"), result })!).GetAwaiter().GetResult();
            term.StreamWriterInternal?.Flush();
            string text = System.Text.RegularExpressions.Regex.Replace(System.Text.Encoding.UTF8.GetString(output.ToArray()), "\u001b\\[[0-9;]*[A-Za-z]", "");
            string line = Loc.Get("combat.physical_immunity_absorbs", MonsterNames.Display(god));
            int count = 0, at = 0;
            while ((at = text.IndexOf(line, at, StringComparison.Ordinal)) >= 0) { count++; at += line.Length; }
            count.Should().Be(1, "two hits on one target in one action print the absorbs line once");
            return 0;
        });
    }
}
