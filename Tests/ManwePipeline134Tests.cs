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

namespace UsurperReborn.Tests;

/// <summary>
/// 1.3.4: Manwe's named abilities (TryManweBossAbility) go through one Manwe damage wrapper that applies the
/// difficulty, Shield Wall Formation, the 85 percent cap, flee grace, the Team HQ defence, Divine Mandate and
/// Death's Embrace, and skips the Old God solo adjustment and the first rounds cap. The other gods' named
/// abilities and a monster's basic swing are pinned to the numbers recorded at 77b6e035, before the change.
/// </summary>
[Collection("SharedGameSingletons")]
public class ManwePipeline134Tests
{
    private static readonly BindingFlags F = BindingFlags.NonPublic | BindingFlags.Instance;

    private static CombatEngine Engine(Character hero, int seed = 7)
    {
        var engine = new CombatEngine(new TerminalEmulator(new MemoryStream(), new MemoryStream()));
        typeof(CombatEngine).GetField("random", F)!.SetValue(engine, new Random(seed));
        typeof(CombatEngine).GetField("currentPlayer", F)!.SetValue(engine, hero);
        return engine;
    }

    private static Character Hero(long hp = 100_000) => new Character
    {
        Name1 = "pipeline", Name2 = "Pipeline", Class = CharacterClass.Warrior, Level = 60, HP = hp, MaxHP = 100_000,
        Defence = 0, CombatSpeed = CombatSpeed.Instant,
    };

    private static Character Ally() => new Character
    {
        Name1 = "ally", Name2 = "Ally", Class = CharacterClass.Cleric, Level = 60, HP = 50_000, MaxHP = 50_000,
    };

    private static Monster God(string name, long attack) => new Monster
    {
        Name = name, Level = 100, HP = 10_000_000, MaxHP = 10_000_000, Strength = attack, WeapPow = 0, Punch = 0,
        IsBoss = false, IsActive = true,
    };

    private static void Party(CombatEngine engine, bool solo)
    {
        engine.BossContext = new BossCombatContext();
        typeof(CombatEngine).GetField("currentTeammates", F)!.SetValue(engine, solo ? new List<Character>() : new List<Character> { Ally() });
    }

    private static async Task<bool> Ability(CombatEngine engine, string method, Monster god, Character hero, string ability, int round)
    {
        var result = new CombatResult { CurrentRound = round };
        return await (Task<bool>)typeof(CombatEngine).GetMethod(method, F)!
            .Invoke(engine, new object?[] { god, hero, ability, result, new List<Monster> { god } })!;
    }

    // ---------- the other gods and the basic swing, as at 77b6e035 ----------

    private static async Task<string> Generic(string ability, int round, bool solo, DifficultyMode mode, bool guards)
    {
        var before = DifficultySystem.CurrentDifficulty;
        DifficultySystem.CurrentDifficulty = mode;
        try
        {
            var hero = Hero();
            if (guards)
            {
                hero.TempDamageReductionPercent = 30; hero.TempDamageReductionDuration = 3;
                hero.TempThornReflectPercent = 15; hero.TempThornReflectDuration = 3;
                hero.FleeGraceThisRound = true;
            }
            var engine = Engine(hero);
            Party(engine, solo);
            var god = God("Maelketh", 20_000);
            bool handled = await Ability(engine, "TryGenericBossAbility", god, hero, ability, round);
            return $"{ability} r{round} {(solo ? "solo" : "party")} {mode} {(guards ? "guards" : "plain")}: handled={handled} hp={hero.HP} god={god.HP}";
        }
        finally { DifficultySystem.CurrentDifficulty = before; }
    }

    private static readonly string[] GenericRecorded =
    {
        "Cleave r1 solo Normal plain: handled=True hp=85000 god=10000000",
        "Cleave r5 solo Normal plain: handled=True hp=63201 god=10000000",
        "Cleave r5 party Normal plain: handled=True hp=60000 god=10000000",
        "Final Stand r5 party Nightmare plain: handled=True hp=15000 god=10000000",
        "Final Stand r5 party Easy guards: handled=True hp=84250 god=9997638",
        "Earthquake r5 solo Hard plain: handled=True hp=65501 god=10000000",
        "Void Drain r5 party Normal plain: handled=True hp=64000 god=10000000",
    };

    [Fact]
    public async Task TheOtherGods_NamedAbilities_DealWhatTheyDidBefore()
    {
        var cases = new (string ability, int round, bool solo, DifficultyMode mode, bool guards)[]
        {
            ("Cleave", 1, true, DifficultyMode.Normal, false),
            ("Cleave", 5, true, DifficultyMode.Normal, false),
            ("Cleave", 5, false, DifficultyMode.Normal, false),
            ("Final Stand", 5, false, DifficultyMode.Nightmare, false),
            ("Final Stand", 5, false, DifficultyMode.Easy, true),
            ("Earthquake", 5, true, DifficultyMode.Hard, false),
            ("Void Drain", 5, false, DifficultyMode.Normal, false),
        };
        var lines = new List<string>();
        foreach (var c in cases) lines.Add(await Generic(c.ability, c.round, c.solo, c.mode, c.guards));
        lines.Should().Equal(GenericRecorded);
    }

    private static async Task<long> Swing(int seed)
    {
        var hero = Hero();
        var engine = Engine(hero, seed);
        var brute = new Monster { Name = "Brute", Level = 20, HP = 1_000_000, MaxHP = 1_000_000, Strength = 1000, WeapPow = 200, IsActive = true };
        long before = hero.HP;
        await (Task)typeof(CombatEngine).GetMethod("ProcessMonsterAction", F)!
            .Invoke(engine, new object?[] { brute, hero, new CombatResult(), null })!;
        long taken = before - hero.HP;
        return taken > 0 ? taken : -1;
    }

    // seed -> damage of a landed swing, recorded at 77b6e035 (dodges use unseeded dice, so a seed may miss)
    private static readonly Dictionary<int, long> SwingRecorded = new()
    {
        [1] = 1806, [2] = 1801, [3] = 1812, [4] = 1807, [5] = 1803, [6] = 1813,
        [7] = 1809, [8] = 1804, [9] = 1800, [10] = 2414, [12] = 2402,
    };

    [Fact]
    public async Task AMonsterBasicSwing_DealsWhatItDidBefore()
    {
        int compared = 0;
        foreach (var (seed, recorded) in SwingRecorded)
        {
            long taken = await Swing(seed);
            if (taken < 0) continue;
            taken.Should().Be(recorded, $"seed {seed}");
            compared++;
        }
        compared.Should().BeGreaterThan(3, "enough swings must land for the pin to mean anything");
    }

    // ---------- Manwe's named abilities through the wrapper ----------

    private const long Raw = 30_000;   // Light Incarnate: 3 x an attack of 10,000, less Defence 0

    private static async Task<(Character hero, Monster god, CombatResult result)> Manwe(string ability = "Light Incarnate",
        DifficultyMode mode = DifficultyMode.Normal, long attack = 10_000, long hp = 100_000, int round = 5, bool solo = true,
        Action<Character>? setup = null, long godHp = 10_000_000)
    {
        var before = DifficultySystem.CurrentDifficulty;
        var held = OldGodBuild134Support.SetArtifacts(0);   // no Worldstone
        DifficultySystem.CurrentDifficulty = mode;
        try
        {
            var hero = Hero(hp);
            setup?.Invoke(hero);
            var engine = Engine(hero);
            Party(engine, solo);
            var god = God("Manwe", attack);
            god.HP = godHp;
            var result = new CombatResult { CurrentRound = round };
            bool handled = await (Task<bool>)typeof(CombatEngine).GetMethod("TryManweBossAbility", F)!
                .Invoke(engine, new object?[] { god, hero, ability, result, new List<Monster> { god } })!;
            handled.Should().BeTrue();
            return (hero, god, result);
        }
        finally
        {
            DifficultySystem.CurrentDifficulty = before;
            OldGodBuild134Support.RestoreArtifacts(held);
        }
    }

    private static long Taken(Character hero) => hero.MaxHP - hero.HP;

    [Fact]
    public async Task OnNormal_LightIncarnate_DealsItsRawDamage()
    {
        var (hero, _, result) = await Manwe();
        Taken(hero).Should().Be(Raw);
        result.CombatLog.Should().Contain($"Manwe uses Light Incarnate for {Raw} damage", "the log reports the damage dealt");
    }

    [Theory]
    [InlineData(DifficultyMode.Easy, 22_500)]
    [InlineData(DifficultyMode.Hard, 37_500)]
    [InlineData(DifficultyMode.Nightmare, 45_000)]
    public async Task TheDifficulty_ScalesManwesAbilities(DifficultyMode mode, long expected)
    {
        var (hero, _, _) = await Manwe(mode: mode);
        Taken(hero).Should().Be(expected, $"{mode} multiplies monster damage");
    }

    private static void Wall(Character c) { c.TempDamageReductionPercent = 30; c.TempDamageReductionDuration = 3; }

    [Fact]
    public async Task ShieldWallFormation_LowersManwesAbilities_ByItsPercent()
    {
        var (hero, _, result) = await Manwe(setup: Wall);
        Taken(hero).Should().Be(Raw - Raw * 30 / 100);
        result.CombatLog.Should().Contain($"Manwe uses Light Incarnate for {Raw - Raw * 30 / 100} damage", "the ability reports the damage dealt");
    }

    [Fact]
    public async Task DivineMandate_ReflectsItsPercent_ToManwe()
    {
        var (hero, god, _) = await Manwe(setup: c => { c.TempThornReflectPercent = 15; c.TempThornReflectDuration = 3; });
        Taken(hero).Should().Be(Raw);
        god.HP.Should().Be(10_000_000 - Raw * 15 / 100);
    }

    [Fact]
    public async Task AFailedFlee_HalvesManwesAbility()
    {
        var (hero, _, _) = await Manwe(setup: c => c.FleeGraceThisRound = true);
        Taken(hero).Should().Be(Raw / 2);
    }

    [Fact]
    public async Task DeathsEmbrace_Revives_OnLethalAbilityDamage()
    {
        var (hero, _, _) = await Manwe(hp: 1_000, setup: c => c.DeathsEmbraceActive = true);
        hero.HP.Should().Be(15_000, "Death's Embrace revives at 15 percent of max HP");
        hero.DeathsEmbraceActive.Should().BeFalse();
        var (control, _, _) = await Manwe(hp: 1_000);
        control.HP.Should().Be(0, "without Death's Embrace the blow is lethal");
    }

    [Fact]
    public async Task DeathsEmbrace_Revives_FromCreationsEndsKill()
    {
        var (hero, _, _) = await Manwe("Creation's End", hp: 10_000, setup: c => c.DeathsEmbraceActive = true);
        hero.HP.Should().Be(15_000, "the kill takes HP to 0, and Death's Embrace revives \"if HP reaches 0\"");
        hero.DeathsEmbraceActive.Should().BeFalse();
        var (control, _, _) = await Manwe("Creation's End", hp: 10_000);
        control.HP.Should().Be(0, "without Death's Embrace or the Worldstone the kill stands");
    }

    [Fact]
    public async Task TheSoloAdjustment_StaysSkipped_OnManwe()
    {
        var (solo, _, _) = await Manwe(solo: true);
        var (party, _, _) = await Manwe(solo: false);
        Taken(solo).Should().Be(Raw, "Manwe's own abilities skip the solo adjustment");
        Taken(party).Should().Be(Taken(solo));
    }

    [Fact]
    public async Task RoundOne_IsCappedAt85Percent_NotTheFirstRoundsCap()
    {
        var (hero, _, _) = await Manwe(attack: 40_000, round: 1);
        Taken(hero).Should().Be(85_000, "the cap on Manwe's own abilities is 85 percent of max HP from the first round");
        var (later, _, _) = await Manwe(attack: 40_000, round: 5);
        Taken(later).Should().Be(85_000);
    }

    [Fact]
    public async Task ShadowIncarnate_HealsManwe_30PercentOfTheDamageDealt()
    {
        var (hero, god, result) = await Manwe("Shadow Incarnate", setup: Wall, godHp: 5_000_000);
        long dealt = 20_000 - 20_000 * 30 / 100;   // 2 x 10,000, then Shield Wall Formation
        Taken(hero).Should().Be(dealt);
        god.HP.Should().Be(5_000_000 + dealt * 30 / 100);
        result.CombatLog.Should().Contain($"Manwe uses Shadow Incarnate: {dealt} damage, heals {dealt * 30 / 100}");
    }

    [Fact]
    public async Task Invulnerable_StillBlocksManwesAbility()
    {
        var (hero, _, _) = await Manwe(setup: c => c.ApplyStatus(StatusEffect.Invulnerable, 2));
        hero.HP.Should().Be(100_000);
    }
}
