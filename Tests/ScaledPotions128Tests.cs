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
using UsurperRemake.Systems;
using Xunit;

namespace UsurperReborn.Tests;

/// <summary>
/// 1.2.8 difficulty pass, DESIGN item 4 (rows C1 to C6): a healing potion heals
/// max(30 + 5 x Level + roll, 20% of MaxHP) through PotionBonus.BaseHeal, and the estimates use
/// PotionBonus.AverageHeal, behind GameConfig.ScaledPotionsEnabled (default on). Off, every site heals
/// and draws exactly as before.
/// </summary>
[Collection("SharedGameSingletons")]
public class ScaledPotions128Tests
{
    private static readonly BindingFlags F = BindingFlags.NonPublic | BindingFlags.Instance;

    private static Character Hero(int level, long maxHp, CharacterClass cls = CharacterClass.Warrior) => new Character
    {
        Name1 = "potion", Name2 = "Potion", Class = cls, Level = level, MaxHP = maxHp, HP = 1,
        Healing = 10, CombatSpeed = CombatSpeed.Instant,
    };

    private static T WithSwitch<T>(bool on, Func<T> body)
    {
        bool saved = GameConfig.ScaledPotionsEnabled;
        try { GameConfig.ScaledPotionsEnabled = on; return body(); }
        finally { GameConfig.ScaledPotionsEnabled = saved; }
    }

    private static int Roll(int seed, int maxExclusive = 30) => new Random(seed).Next(10, maxExclusive);

    // C1
    [Fact]
    public void C1_AtLevel50With1950HP_APotionHeals20PercentOfMaxHP()
    {
        var hero = Hero(50, 1950);
        for (int seed = 1; seed <= 20; seed++)
        {
            long flat = 30 + 50 * 5 + Roll(seed);
            flat.Should().BeLessThan(390, "the flat heal at level 50 is 290 to 309");
            WithSwitch(true, () => PotionBonus.BaseHeal(hero, new Random(seed))).Should().Be(390);
            WithSwitch(true, () => PotionBonus.BaseHeal(hero, new Random(seed), 31)).Should().Be(390);
        }
        WithSwitch(true, () => PotionBonus.AverageHeal(hero)).Should().Be(390);
    }

    // C2
    [Theory]
    [InlineData(5, 150)]
    [InlineData(25, 857)]
    public void C2_LowAndMidLevels_KeepTheFlatHeal(int level, long maxHp)
    {
        var hero = Hero(level, maxHp);
        long floor = (long)Math.Round(maxHp * 0.20);
        int checkedSeeds = 0;
        for (int seed = 1; seed <= 200; seed++)
        {
            long flat = 30 + level * 5 + Roll(seed);
            if (flat < floor) continue; // level 25 only: rolls 10 to 15 sit under 171 (see the next test)
            checkedSeeds++;
            WithSwitch(true, () => PotionBonus.BaseHeal(hero, new Random(seed))).Should().Be(flat);
            long flatDungeon = 30 + level * 5 + Roll(seed, 31);
            if (flatDungeon >= floor)
                WithSwitch(true, () => PotionBonus.BaseHeal(hero, new Random(seed), 31)).Should().Be(flatDungeon);
        }
        checkedSeeds.Should().BeGreaterThan(100);
        WithSwitch(true, () => PotionBonus.AverageHeal(hero)).Should().Be(50 + level * 5);
    }

    [Fact]
    public void C2_AtLevel25With857HP_TheLowestRollsAreLiftedTo171()
    {
        // 20% of 857 is 171 and the flat heal is 165 to 184, so a roll of 10 to 15 heals 171.
        var hero = Hero(25, 857);
        int seed = Enumerable.Range(1, 500).First(s => Roll(s) <= 15);
        WithSwitch(true, () => PotionBonus.BaseHeal(hero, new Random(seed))).Should().Be(171);
        WithSwitch(false, () => PotionBonus.BaseHeal(hero, new Random(seed))).Should().Be(155 + Roll(seed));
    }

    // C3
    private static string RepoRoot()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir != null && !(Directory.Exists(Path.Combine(dir.FullName, "Scripts")) && Directory.Exists(Path.Combine(dir.FullName, "Localization"))))
            dir = dir.Parent;
        dir.Should().NotBeNull("the test must run inside the repository");
        return dir!.FullName;
    }

    // The flat heal and its midpoint ("30 + x.Level * 5 + roll" or "+ 20"); the mana potion lines
    // ("30 + x.Level * 5;") do not match.
    private static readonly Regex FlatHeal = new(@"30 \+ \w+\.Level \* 5 \+");
    // The old "how many" estimate; only searched in the potion files (50 + Level * 5 is also an XP and a gold amount).
    private static readonly Regex FlatAverage = new(@"50 \+ \w+\.Level \* 5\b");
    private static readonly string[] PotionFiles =
    {
        "Scripts/Systems/CombatEngine.cs", "Scripts/Locations/DungeonLocation.cs", "Scripts/Locations/BaseLocation.cs", "Scripts/Systems/WorldBossSystem.cs",
    };

    private static int Count(string path, string what) => Regex.Matches(File.ReadAllText(Path.Combine(RepoRoot(), path)), Regex.Escape(what)).Count;

    [Fact]
    public void C3_EveryLiveSite_UsesTheHelper()
    {
        var hits = new List<string>();
        foreach (var path in Directory.EnumerateFiles(Path.Combine(RepoRoot(), "Scripts"), "*.cs", SearchOption.AllDirectories))
        {
            if (Path.GetFileName(path) == "PotionBonus.cs") continue;
            var lines = File.ReadAllLines(path);
            for (int i = 0; i < lines.Length; i++)
                if (FlatHeal.IsMatch(lines[i])) hits.Add($"{Path.GetFileName(path)}:{i + 1}: {lines[i].Trim()}");
        }
        foreach (var rel in PotionFiles)
        {
            var lines = File.ReadAllLines(Path.Combine(RepoRoot(), rel));
            for (int i = 0; i < lines.Length; i++)
                if (FlatAverage.IsMatch(lines[i])) hits.Add($"{Path.GetFileName(rel)}:{i + 1}: {lines[i].Trim()}");
        }
        hits.Should().BeEmpty("every healing potion site calls PotionBonus.BaseHeal or AverageHeal");

        File.ReadAllText(Path.Combine(RepoRoot(), "Scripts/Systems/PotionBonus.cs")).Should().MatchRegex(FlatHeal.ToString(), "the scan must know the formula it looks for");
        Count("Scripts/Systems/CombatEngine.cs", "PotionBonus.BaseHeal(").Should().Be(7);
        Count("Scripts/Systems/CombatEngine.cs", "PotionBonus.AverageHeal(").Should().Be(3);
        Count("Scripts/Locations/DungeonLocation.cs", "PotionBonus.BaseHeal(").Should().Be(4);
        Count("Scripts/Locations/DungeonLocation.cs", "PotionBonus.AverageHeal(").Should().Be(2);
        Count("Scripts/Locations/BaseLocation.cs", "PotionBonus.BaseHeal(").Should().Be(1);
        Regex.Matches(File.ReadAllText(Path.Combine(RepoRoot(), "Scripts/Locations/DungeonLocation.cs")), @"PotionBonus\.BaseHeal\([^;]*, 31[,)]").Count
            .Should().Be(4, "the dungeon sites keep their roll to 31");
    }

    // C4
    private static (CombatEngine engine, MemoryStream output) Engine(Character hero, IEnumerable<string> input, int seed)
    {
        var output = new MemoryStream();
        var term = new TerminalEmulator(new LineStream(input.Concat(Enumerable.Repeat("", 20))), output);
        var engine = new CombatEngine(term);
        typeof(CombatEngine).GetField("random", F)!.SetValue(engine, new Random(seed));
        typeof(CombatEngine).GetField("currentPlayer", F)!.SetValue(engine, hero);
        return (engine, output);
    }

    private static string Text(MemoryStream output) =>
        Regex.Replace(Encoding.UTF8.GetString(output.ToArray()), "\u001b\\[[0-9;?]*[A-Za-z]", "").Replace("\r", "");

    [Fact]
    public async Task C4_TheHowManyEstimate_ShowsTheScaledCount()
    {
        bool saved = GameConfig.ScaledPotionsEnabled;
        try
        {
            GameConfig.ScaledPotionsEnabled = true;
            var hero = Hero(60, 3000);
            hero.HP = 600; // 2,400 missing: 4 scaled potions of 600, 7 flat potions of 350
            var (engine, output) = Engine(hero, new[] { "1" }, 7);
            await (Task)typeof(CombatEngine).GetMethod("ExecuteHeal", F)!.Invoke(engine, new object[] { hero, new CombatResult(), false })!;
            Text(output).Should().Contain(Loc.Get("combat.potions_missing_hp", 2400, 4));
            hero.Healing.Should().Be(9);
            hero.HP.Should().Be(1200, "one potion heals 600");
        }
        finally { GameConfig.ScaledPotionsEnabled = saved; }
    }

    [Fact]
    public void C4_AutoHeal_SkipsADeficitUnderHalfAScaledPotion()
    {
        bool saved = GameConfig.ScaledPotionsEnabled;
        try
        {
            var hero = Hero(60, 3000);
            hero.AutoHeal = true;
            hero.HP = 2800; // 200 missing: over half a flat potion (175), under half a scaled one (300)
            GameConfig.ScaledPotionsEnabled = true;
            var (engine, _) = Engine(hero, Array.Empty<string>(), 7);
            typeof(CombatEngine).GetMethod("AutoHealWithPotions", F)!.Invoke(engine, new object[] { hero });
            hero.Healing.Should().Be(10, "the scaled estimate says the deficit is not worth a potion");

            GameConfig.ScaledPotionsEnabled = false;
            (engine, _) = Engine(hero, Array.Empty<string>(), 7);
            typeof(CombatEngine).GetMethod("AutoHealWithPotions", F)!.Invoke(engine, new object[] { hero });
            hero.Healing.Should().Be(9, "with the switch off the flat estimate drinks one");
        }
        finally { GameConfig.ScaledPotionsEnabled = saved; }
    }

    // C5
    [Theory]
    [InlineData(2000, 1125)] // 600 x 1.25 (Easy) = 750, x 1.5 (Potion Mastery) = 1,125
    [InlineData(700, 700)]   // the same chain, then the cap to missing HP
    public async Task C5_AnAlchemistOnEasy_GetsTheDifficultyMultiplier_ThenTheBonuses_ThenTheCap(long missing, long expected)
    {
        bool saved = GameConfig.ScaledPotionsEnabled;
        var savedDifficulty = DifficultySystem.CurrentDifficulty;
        try
        {
            GameConfig.ScaledPotionsEnabled = true;
            DifficultySystem.CurrentDifficulty = DifficultyMode.Easy;
            var hero = Hero(60, 3000, CharacterClass.Alchemist);
            hero.HP = 3000 - missing;
            var (engine, _) = Engine(hero, Array.Empty<string>(), 11);
            await (Task)typeof(CombatEngine).GetMethod("ExecuteHeal", F)!.Invoke(engine, new object[] { hero, new CombatResult(), true })!;
            (hero.HP - (3000 - missing)).Should().Be(expected);
            hero.HP.Should().BeLessThanOrEqualTo(hero.MaxHP);
        }
        finally
        {
            GameConfig.ScaledPotionsEnabled = saved;
            DifficultySystem.CurrentDifficulty = savedDifficulty;
        }
    }

    // C6
    [Fact]
    public void C6_SwitchOff_GivesTodaysHeal_AndTheSameNextRandom()
    {
        var hero = Hero(50, 1950);
        for (int seed = 1; seed <= 20; seed++)
        {
            foreach (int max in new[] { 30, 31 })
            {
                var reference = new Random(seed);
                long today = 30 + 50 * 5 + reference.Next(10, max);
                int nextToday = reference.Next();

                var rng = new Random(seed);
                WithSwitch(false, () => PotionBonus.BaseHeal(hero, rng, max)).Should().Be(today);
                rng.Next().Should().Be(nextToday, "switch off draws exactly one roll, as before");

                var rngOn = new Random(seed);
                WithSwitch(true, () => PotionBonus.BaseHeal(hero, rngOn, max)).Should().Be(390);
                rngOn.Next().Should().Be(nextToday, "switch on draws the same single roll");
            }
        }
        WithSwitch(false, () => PotionBonus.AverageHeal(hero)).Should().Be(300, "50 + 5 x 50");
        GameConfig.ScaledPotionsEnabled.Should().BeTrue("the switch is on by default");
    }

    // C7 (supervisor ruling 2026-10-08): a potion given to an ally heals at least 20% of the ALLY's MaxHP.
    // The flat part still reads the giver's level.
    private static Character Ally(long maxHp) => new Character
    {
        Name1 = "ally", Name2 = "Ally", Class = CharacterClass.Warrior, Level = 50, MaxHP = maxHp, HP = 1,
        Healing = 0, CombatSpeed = CombatSpeed.Instant,
    };

    [Fact]
    public void C7_APotionGivenToAnAlly_IsScaledByTheAllysMaxHP()
    {
        var giver = Hero(50, 1950);
        for (int seed = 1; seed <= 20; seed++)
        {
            WithSwitch(true, () => PotionBonus.BaseHeal(giver, new Random(seed), receiver: Ally(10_000))).Should().Be(2000, "20% of the ally's 10,000");
            WithSwitch(true, () => PotionBonus.BaseHeal(giver, new Random(seed), 31, Ally(10_000))).Should().Be(2000);
            WithSwitch(true, () => PotionBonus.BaseHeal(giver, new Random(seed), receiver: Ally(500)))
                .Should().Be(280 + Roll(seed), "20% of the ally's 500 is under the giver's flat heal; the giver's 390 does not apply");
            WithSwitch(false, () => PotionBonus.BaseHeal(giver, new Random(seed), receiver: Ally(10_000))).Should().Be(280 + Roll(seed));
        }
        WithSwitch(true, () => PotionBonus.AverageHeal(giver, Ally(10_000))).Should().Be(2000);
        WithSwitch(true, () => PotionBonus.AverageHeal(giver, Ally(500))).Should().Be(300);
    }

    [Theory]
    [InlineData(10_000, 2000, 2000)]
    [InlineData(500, 290, 309)]
    public async Task C7_ATeammatesPotion_IsScaledByTheTargetsMaxHP(long targetMaxHp, long min, long max)
    {
        bool saved = GameConfig.ScaledPotionsEnabled;
        try
        {
            GameConfig.ScaledPotionsEnabled = true;
            var teammate = Hero(50, 1950);
            var target = Ally(targetMaxHp);
            var (engine, _) = Engine(Hero(10, 100), Array.Empty<string>(), 5);
            await (Task<bool>)typeof(CombatEngine).GetMethod("TeammateHealWithPotion", F)!
                .Invoke(engine, new object[] { teammate, target, new CombatResult(), false })!;
            teammate.Healing.Should().Be(9);
            (target.HP - 1).Should().BeInRange(min, max);
        }
        finally { GameConfig.ScaledPotionsEnabled = saved; }
    }

    [Fact]
    public async Task C7_ADungeonPotionGivenToATeammate_IsScaledByTheTeammatesMaxHP()
    {
        bool saved = GameConfig.ScaledPotionsEnabled;
        try
        {
            GameConfig.ScaledPotionsEnabled = true;
            var player = Hero(50, 1950);
            player.HP = player.MaxHP;
            var ally = Ally(10_000);
            var output = new MemoryStream();
            var dungeon = new DungeonLocation();
            typeof(BaseLocation).GetField("terminal", F)!.SetValue(dungeon,
                new TerminalEmulator(new LineStream(new[] { "1", "1" }.Concat(Enumerable.Repeat("", 20))), output));
            await (Task)typeof(DungeonLocation).GetMethod("HealTeammate", F)!
                .Invoke(dungeon, new object[] { player, new List<Character> { ally } })!;
            player.Healing.Should().Be(9);
            (ally.HP - 1).Should().Be(2000, "20% of the teammate's 10,000, not the giver's 390");
            Text(output).Should().Contain(Loc.Get("dungeon.each_potion_heals", 2000));
        }
        finally { GameConfig.ScaledPotionsEnabled = saved; }
    }
}
