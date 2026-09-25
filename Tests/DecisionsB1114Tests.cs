using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Threading.Tasks;
using System.Text.RegularExpressions;
using FluentAssertions;
using UsurperRemake;
using UsurperRemake.Systems;
using Xunit;

namespace UsurperReborn.Tests;

/// <summary>
/// v1.1.14 maintainer decisions, balance and package rows: the SSH.NET upgrade, the alt slot
/// level, Engulf on a held target, the goblin critical strike and area freeze.
/// </summary>
[Collection("SharedGameSingletons")]
public class DecisionsB1114Tests
{
    private static string RepoRoot()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir != null && !(Directory.Exists(Path.Combine(dir.FullName, "Scripts")) && Directory.Exists(Path.Combine(dir.FullName, "Localization"))))
            dir = dir.Parent;
        dir.Should().NotBeNull("the test must run inside the repository");
        return dir!.FullName;
    }

    // ---- O4: SSH.NET ----

    [Fact]
    public void SshNet_IsPinnedToThePatchedRelease()
    {
        // v1.1.14: 2025.1.0 and earlier carry GHSA-q939-rpr3-3284 and GHSA-mggc-4xg6-vcxf (NU1903); 2026.0.0 fixes both.
        string csproj = File.ReadAllText(Path.Combine(RepoRoot(), "usurper-reloaded.csproj"));
        var m = Regex.Match(csproj, "<PackageReference Include=\"SSH.NET\" Version=\"([^\"]+)\"");
        m.Success.Should().BeTrue("the game references SSH.NET for the online client");
        m.Groups[1].Value.Should().Be("2026.0.0");

        var loaded = typeof(Renci.SshNet.SshClient).Assembly.GetName().Version!;
        loaded.Major.Should().BeGreaterOrEqualTo(2026, "the build must bind the patched assembly");
    }

    // ---- B4: alt slot at level 25 ----

    [Theory]
    [InlineData(false, false, 1, false)]
    [InlineData(false, false, 24, false)]   // one level short
    [InlineData(false, false, 25, true)]    // the threshold
    [InlineData(false, false, 60, true)]
    [InlineData(true, false, 1, true)]      // an immortal main still opens it
    [InlineData(false, true, 3, true)]      // an earned slot survives renouncing and a low level
    public void AltSlot_OpensAtLevel25_OrForAnImmortal(bool immortal, bool earned, int level, bool expected)
    {
        GameConfig.AltSlotUnlockLevel.Should().Be(25);
        GameEngine.AltSlotUnlocked(immortal, earned, level).Should().Be(expected);
    }

    [Fact]
    public void AltSlot_MenuUsesTheRule_AndKeepsOneAlt()
    {
        string src = File.ReadAllText(Path.Combine(RepoRoot(), "Scripts", "Core", "GameEngine.cs"));
        src.Should().Contain("bool canCreateAlt = AltSlotUnlocked(mainIsImmortal, hasAltSlot, mainLevel) && altSave == null",
            "the menu gate uses the level rule and still refuses a second alt");
        src.Should().NotContain("engine.immortal_required");
    }

    [Theory]
    [InlineData("en")]
    [InlineData("es")]
    [InlineData("fr")]
    [InlineData("hu")]
    [InlineData("it")]
    public void AltSlot_RefusalText_NamesTheLevel(string lang)
    {
        string json = File.ReadAllText(Path.Combine(RepoRoot(), "Localization", lang + ".json"));
        var m = Regex.Match(json, "\"engine\\.alt_level_required\": \"([^\"]*)\"");
        m.Success.Should().BeTrue($"{lang} has the new refusal line");
        m.Groups[1].Value.Should().Contain("{0}", "the level is filled in from GameConfig");
        json.Should().NotContain("\"engine.immortal_required\"");
    }

    // ---- B1: Engulf on a held target ----

    private static readonly BindingFlags NF = BindingFlags.NonPublic | BindingFlags.Instance;

    /// <summary>An engine whose every roll is lowest, so each special fires and each status lands.</summary>
    private static CombatEngine LowEngine()
    {
        var engine = new CombatEngine(new TerminalEmulator(new MemoryStream(), new MemoryStream()));
        typeof(CombatEngine).GetField("random", NF)!.SetValue(engine, new LowRandom());
        return engine;
    }

    private static Monster Cube(int i) => new Monster
    {
        Name = $"Gelatinous Cube {i}", Level = 40, HP = 100_000, MaxHP = 100_000,
        Strength = 200, WeapPow = 200, IsActive = true,
        SpecialAbilities = new List<string> { "Engulf" },
    };

    private static Character Tank() => new Character
    {
        Name1 = "tank", Name2 = "Aldric", Class = CharacterClass.Warrior, Level = 40,
        HP = 10_000_000, MaxHP = 10_000_000,
    };

    private static async Task CubeHitsCompanion(CombatEngine engine, Monster cube, Character tank, CombatResult result) =>
        await (Task)typeof(CombatEngine).GetMethod("MonsterAttacksCompanion", NF)!
            .Invoke(engine, new object?[] { cube, tank, result, null })!;

    private static int Engulfs(CombatResult result) => result.CombatLog.Count(l => l.Contains("uses Engulf"));

    [Fact]
    public async Task FourCubes_OnOneTank_OnlyTheFirstEngulfs()
    {
        var engine = LowEngine();
        var tank = Tank();
        var result = new CombatResult { CurrentRound = 5 };
        for (int i = 1; i <= 4; i++)
            await CubeHitsCompanion(engine, Cube(i), tank, result);

        Engulfs(result).Should().Be(1, "once the tank is held the other cubes make a normal attack");
        tank.HasStatus(StatusEffect.Stunned).Should().BeTrue("the first Engulf still holds the tank");
    }

    [Fact]
    public async Task Cubes_OnATankThatIsNotHeld_StillEngulf()
    {
        var engine = LowEngine();
        var result = new CombatResult { CurrentRound = 5 };
        for (int i = 1; i <= 3; i++)
            await CubeHitsCompanion(engine, Cube(i), Tank(), result);   // a fresh, unheld tank each time
        Engulfs(result).Should().Be(3);
    }

    [Theory]
    [InlineData(StatusEffect.Stunned)]
    [InlineData(StatusEffect.Frozen)]
    [InlineData(StatusEffect.Sleeping)]
    [InlineData(StatusEffect.Paralyzed)]
    public void Engulf_FallsBack_OnAnyHold(StatusEffect hold)
    {
        var tank = Tank();
        MonsterAbilities.FallsBackToNormalAttack(MonsterAbilities.AbilityType.Engulf, Cube(1), tank).Should().BeFalse();
        tank.ApplyStatus(hold, 3);
        MonsterAbilities.FallsBackToNormalAttack(MonsterAbilities.AbilityType.Engulf, Cube(1), tank).Should().BeTrue();
        MonsterAbilities.FallsBackToNormalAttack(MonsterAbilities.AbilityType.CrushingBlow, Cube(1), tank).Should().BeFalse("only Engulf is gated");
    }

    [Fact]
    public void Engulf_OnAHeldPlayer_IsANormalAttack()
    {
        var engine = LowEngine();
        var hero = new Character { Name1 = "Hero", Name2 = "Hero", Class = CharacterClass.Warrior, Level = 40, HP = 10_000_000, MaxHP = 10_000_000 };
        var result = new CombatResult { Player = hero };
        var m = typeof(CombatEngine).GetMethod("TryMonsterSpecialAbility", NF)!;
        bool Special(Monster cube) => ((Task<bool>)m.Invoke(engine, new object?[] { cube, hero, result, null })!).GetAwaiter().GetResult();

        Special(Cube(1)).Should().BeTrue("the first cube engulfs a free player");
        hero.HasStatus(StatusEffect.Stunned).Should().BeTrue();
        Special(Cube(2)).Should().BeFalse("a held player gets the second cube's normal attack instead");
    }
    // ---- B2: a goblin's Critical Strike, once per fight per goblin ----

    private static Monster Goblin(string name, string family = "Goblinoid") => new Monster
    {
        Name = name, FamilyName = family, Level = 40, HP = 100_000, MaxHP = 100_000,
        Strength = 200, WeapPow = 200, IsActive = true,
        SpecialAbilities = new List<string> { "CriticalStrike" },
    };

    private static int Crits(CombatResult result) => result.CombatLog.Count(l => l.Contains("uses CriticalStrike"));

    [Fact]
    public async Task Goblin_CriticalStrike_OncePerFight_OnACompanion()
    {
        var engine = LowEngine();
        var tank = Tank();
        var result = new CombatResult { CurrentRound = 2 };
        var champion = Goblin("Goblin Champion");
        for (int i = 0; i < 4; i++)
            await CubeHitsCompanion(engine, champion, tank, result);
        Crits(result).Should().Be(1, "the second and later turns are normal attacks");

        var warlord = Goblin("Goblin Warlord");
        await CubeHitsCompanion(engine, warlord, tank, result);
        Crits(result).Should().Be(2, "each goblin has its own once-per-fight strike");
    }

    [Fact]
    public void Goblin_CriticalStrike_OncePerFight_OnThePlayer()
    {
        var engine = LowEngine();
        var hero = new Character { Name1 = "Hero", Name2 = "Hero", Class = CharacterClass.Warrior, Level = 40, HP = 10_000_000, MaxHP = 10_000_000 };
        var result = new CombatResult { Player = hero };
        var m = typeof(CombatEngine).GetMethod("TryMonsterSpecialAbility", NF)!;
        bool Special(Monster mon) => ((Task<bool>)m.Invoke(engine, new object?[] { mon, hero, result, null })!).GetAwaiter().GetResult();

        var goblin = Goblin("Goblin King");
        Special(goblin).Should().BeTrue("the first Critical Strike lands");
        Special(goblin).Should().BeFalse("the goblin makes a normal attack after that");
        Special(Goblin("Goblin Champion")).Should().BeTrue("another goblin still has its own");
    }

    [Fact]
    public void CriticalStrike_OnANonGoblin_IsUnchanged()
    {
        var engine = LowEngine();
        var hero = new Character { Name1 = "Hero", Name2 = "Hero", Class = CharacterClass.Warrior, Level = 40, HP = 10_000_000, MaxHP = 10_000_000 };
        var result = new CombatResult { Player = hero };
        var m = typeof(CombatEngine).GetMethod("TryMonsterSpecialAbility", NF)!;
        var champion = Goblin("Arena Champion", family: "");
        for (int i = 0; i < 3; i++)
            ((Task<bool>)m.Invoke(engine, new object?[] { champion, hero, result, null })!).GetAwaiter().GetResult()
                .Should().BeTrue("the decision covers the goblin family only");
    }

    // ---- B3: no freeze on an area hit ----
    // v1.1.14: at cd43e9f no area hit freezes. Monster Frost Breath (Ice Troll) resolves on the one
    // target of that monster's turn; Ice Storm's frost slows; Sage Freeze, Alchemist Frost Bomb and
    // the Leviathan's Frost Bolt and Deep Freeze are single-target. These guards keep it that way.

    [Fact]
    public void NoWorldBossAreaAbility_Freezes()
    {
        foreach (var boss in UsurperRemake.Data.WorldBossDatabase.GetAllBosses())
            foreach (var ab in boss.Phase1Abilities.Concat(boss.Phase2Abilities).Concat(boss.Phase3Abilities))
                if (ab.IsAoE)
                    ab.AppliedStatus.Should().NotBe(StatusEffect.Frozen, $"{boss.Name}'s {ab.Name} hits an area");
    }

    [Fact]
    public void NoAreaSpell_Freezes()
    {
        string src = File.ReadAllText(Path.Combine(RepoRoot(), "Scripts", "Systems", "SpellSystem.cs"));
        int checkedSpells = 0;
        foreach (CharacterClass cls in Enum.GetValues(typeof(CharacterClass)))
            for (int level = 1; level <= 25; level++)
            {
                var info = SpellSystem.GetSpellInfo(cls, level);
                if (info == null || !info.IsMultiTarget) continue;
                var m = Regex.Match(src, @"case " + level + @": // " + Regex.Escape(info.Name) + @"\b(.*?)break;", RegexOptions.Singleline);
                m.Success.Should().BeTrue($"{info.Name} has an effect block");
                m.Groups[1].Value.Should().NotContain("\"freeze\"", $"{info.Name} hits every enemy");
                checkedSpells++;
            }
        checkedSpells.Should().BeGreaterThan(5);
    }

    [Fact]
    public void FrostBreath_StillFreezes_ItsOneTarget()
    {
        var troll = new Monster { Name = "Ice Troll", Level = 30, HP = 1000, MaxHP = 1000, Strength = 50 };
        var r = MonsterAbilities.ExecuteAbility(MonsterAbilities.AbilityType.FrostBreath, troll, Tank());
        r.InflictStatus.Should().Be(StatusEffect.Frozen);
        r.StatusChance.Should().Be(50);
    }
}
