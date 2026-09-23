using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using FluentAssertions;
using UsurperRemake;
using UsurperRemake.Systems;
using Xunit;

namespace UsurperReborn.Tests;

/// <summary>
/// v1.1.11: the Team HQ Infirmary (+10% per level) raises what every healing potion restores, in and
/// out of combat, after every other modifier of the heal and before the cap to missing HP. The bonus is
/// the potion owner's: the character whose Healing count drops. There is no single choke point, so every
/// method in Scripts/ that decrements a Healing count must either call TeamHQBonus.ApplyPotionHeal, be
/// listed below with its reason, or carry an "hq-infirmary: out (reason)" comment on the decrement.
/// </summary>
[Collection("SharedGameSingletons")]
public class InfirmaryCoverageTests
{
    private static string RepoRoot()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir != null && !(Directory.Exists(Path.Combine(dir.FullName, "Scripts")) && Directory.Exists(Path.Combine(dir.FullName, "Localization"))))
            dir = dir.Parent;
        dir.Should().NotBeNull("the test must run inside the repository");
        return dir!.FullName;
    }

    // "<var>.Healing--", "--<var>.Healing", "<var>.Healing -= ..." or "<var>.Healing = Math.Max(0, <var>.Healing - ...)".
    // HerbHealing and ManaPotions never match: the name must be exactly Healing after the dot.
    private static readonly Regex PotionDecrement = new(@"(?:\b([A-Za-z_]\w*)\.Healing\s*(?:--|-=|=\s*Math\.Max\(\s*0\s*,\s*\1\.Healing\s*-)|--\s*[A-Za-z_]\w*\.Healing\b)");

    private static readonly Regex MethodSignature = new(@"^\s*(?:public|private|internal|protected)\b[^=;]*?(\w+)\s*(?:<[^>]*>)?\s*\(");

    // "File.Method" -> why a potion is used up there without a heal to boost.
    private static readonly Dictionary<string, string> ExcludedMethods = new()
    {
        ["MaintenanceSystem.ProcessHealingSpoilage"] = "surplus potions spoil; nothing is drunk",
        ["TempleLocation.SacrificePotions"] = "potions are destroyed as an offering; any heal is divine and to full",
        ["DungeonLocation.WoundedManEncounter"] = "the potion goes to a stranger; no Character's HP rises",
        ["DungeonLocation.IssuePotionToTeammateStash"] = "moves potions into a teammate's stock; they are drunk later through TeammateHealWithPotion",
        ["NPCCombatSimulator.DrinkHealingPotion"] = "NPC world-sim: only NPCs drink here, and an NPC's Infirmary is always 0",
    };

    private static IEnumerable<string> ScriptFiles() =>
        Directory.EnumerateFiles(Path.Combine(RepoRoot(), "Scripts"), "*.cs", SearchOption.AllDirectories);

    private static (int start, int end, string name) MethodAround(string[] lines, int index)
    {
        int start = -1; string name = "?";
        for (int j = index; j >= 0; j--)
        {
            var m = MethodSignature.Match(lines[j]);
            if (m.Success) { start = j; name = m.Groups[1].Value; break; }
        }
        int end = index + 1;
        while (end < lines.Length && !MethodSignature.IsMatch(lines[end])) end++;
        return (Math.Max(0, start), end, name);
    }

    private static List<(string file, int line, string method, string text, string body)> Decrements()
    {
        var hits = new List<(string, int, string, string, string)>();
        foreach (var path in ScriptFiles())
        {
            var lines = File.ReadAllLines(path);
            for (int i = 0; i < lines.Length; i++)
            {
                string code = lines[i].TrimStart();
                if (code.StartsWith("//") || code.StartsWith("*")) continue;
                if (!PotionDecrement.IsMatch(lines[i])) continue;
                var (start, end, name) = MethodAround(lines, i);
                string body = string.Join("\n", lines.Skip(start).Take(end - start));
                hits.Add((Path.GetFileNameWithoutExtension(path), i + 1, name, lines[i].Trim(), body));
            }
        }
        return hits;
    }

    [Fact]
    public void EveryHealingPotionDecrement_AppliesTheInfirmaryOrSaysWhyNot()
    {
        var hits = Decrements();
        hits.Count.Should().BeGreaterThan(20, "the scan must find the potion-drinking sites, or it proves nothing");

        var problems = new List<string>();
        foreach (var (file, line, method, text, body) in hits)
        {
            if (ExcludedMethods.ContainsKey($"{file}.{method}")) continue;
            if (body.Contains("TeamHQBonus.ApplyPotionHeal")) continue;
            int comment = text.IndexOf("//", StringComparison.Ordinal);
            bool markedOut = comment >= 0 && Regex.IsMatch(text.Substring(comment), @"hq-infirmary: out \(\S[^)]*\)");
            if (!markedOut)
                problems.Add($"{file}.cs:{line} in {method}: {text}");
        }
        problems.Should().BeEmpty("each method that uses up a healing potion applies TeamHQBonus.ApplyPotionHeal or is marked out with a reason");
    }

    [Fact]
    public void TheScan_SeesTheKnownSites_AndIgnoresHerbsAndManaPotions()
    {
        var hits = Decrements();
        var methods = hits.Select(h => $"{h.file}.{h.method}").ToHashSet();
        methods.Should().Contain(new[]
        {
            "CombatEngine.ExecuteHeal", "CombatEngine.ExecuteUseItem", "CombatEngine.AutoHealWithPotions", "CombatEngine.HandleHealAlly",
            "CombatEngine.TeammateHealWithPotion", "CombatEngine.ProcessComputerPlayerAction", "BaseLocation.UseQuickPotion",
            "HomeLocation.UseHealingPotion", "DungeonLocation.HealTeammate", "DungeonLocation.HealEntireParty",
            "DungeonLocation.UseHealingPotion", "DungeonLocation.HealToFull", "DungeonLocation.UseFollowerPotion",
            "WorldBossSystem.DrinkHealingPotions",
        });
        hits.Should().NotContain(h => h.text.Contains("HerbHealing") || h.text.Contains("ManaPotions"));
        PotionDecrement.IsMatch("player.HerbHealing--;").Should().BeFalse();
        PotionDecrement.IsMatch("player.Healing = Math.Max(0, player.Healing - 2);").Should().BeTrue();
    }

    [Fact]
    public void TheExcludedMethods_StillExist()
    {
        var present = new HashSet<string>();
        foreach (var path in ScriptFiles())
        {
            string file = Path.GetFileNameWithoutExtension(path);
            foreach (var l in File.ReadLines(path))
            {
                var m = MethodSignature.Match(l);
                if (m.Success) present.Add($"{file}.{m.Groups[1].Value}");
            }
        }
        ExcludedMethods.Keys.Where(k => !present.Contains(k)).Should().BeEmpty("a stale exclusion would hide a new method of the same name");
    }

    [Fact]
    public void TheOldInlineInfirmaryReader_IsGone()
    {
        string src = File.ReadAllText(Path.Combine(RepoRoot(), "Scripts", "Systems", "CombatEngine.cs"));
        src.Should().NotContain("TeamHQBonus.Infirmary(", "the inline reader truncated and sat before the difficulty multiplier");
    }

    private static string MethodBody(string file, string method)
    {
        var lines = File.ReadAllLines(Path.Combine(RepoRoot(), file));
        int start = Array.FindIndex(lines, l => { var m = MethodSignature.Match(l); return m.Success && m.Groups[1].Value == method; });
        start.Should().BeGreaterThanOrEqualTo(0, $"{method} must exist in {file}");
        int end = start + 1;
        while (end < lines.Length && !MethodSignature.IsMatch(lines[end])) end++;
        return string.Join("\n", lines.Skip(start).Take(end - start));
    }

    private static int Count(string text, string what) => Regex.Matches(text, Regex.Escape(what)).Count;

    // The heal sites and the per-potion estimates and displays that must match them.
    [Theory]
    [InlineData("Scripts/Systems/CombatEngine.cs", "ExecuteHeal", 3)]            // quick, multi-potion, the F estimate
    [InlineData("Scripts/Systems/CombatEngine.cs", "ExecuteUseItem", 1)]
    [InlineData("Scripts/Systems/CombatEngine.cs", "AutoHealWithPotions", 2)]    // heal and threshold
    [InlineData("Scripts/Systems/CombatEngine.cs", "HandleHealAlly", 2)]         // heal and estimate
    [InlineData("Scripts/Systems/CombatEngine.cs", "TeammateHealWithPotion", 1)]
    [InlineData("Scripts/Systems/CombatEngine.cs", "ProcessComputerPlayerAction", 1)]
    [InlineData("Scripts/Locations/BaseLocation.cs", "UseQuickPotion", 1)]
    [InlineData("Scripts/Locations/HomeLocation.cs", "UseHealingPotion", 1)]
    [InlineData("Scripts/Locations/DungeonLocation.cs", "UsePotions", 2)]         // Electron payload and text menu
    [InlineData("Scripts/Locations/DungeonLocation.cs", "HealTeammate", 2)]
    [InlineData("Scripts/Locations/DungeonLocation.cs", "HealEntireParty", 3)]
    [InlineData("Scripts/Locations/DungeonLocation.cs", "UseHealingPotion", 1)]
    [InlineData("Scripts/Locations/DungeonLocation.cs", "HealToFull", 1)]
    [InlineData("Scripts/Locations/DungeonLocation.cs", "UseFollowerPotion", 1)]
    [InlineData("Scripts/Systems/WorldBossSystem.cs", "DrinkHealingPotions", 1)]
    public void EachSite_AppliesTheInfirmary(string file, string method, int calls)
    {
        Count(MethodBody(file, method), "TeamHQBonus.ApplyPotionHeal(").Should().Be(calls);
    }

    [Fact]
    public void TheCombatPotion_AppliesTheInfirmary_AfterTheDifficultyMultiplier()
    {
        string body = MethodBody("Scripts/Systems/CombatEngine.cs", "ExecuteHeal");
        int from = 0;
        for (int n = 0; n < 2; n++)
        {
            int difficulty = body.IndexOf("DifficultySystem.ApplyHealingMultiplier(healAmount)", from, StringComparison.Ordinal);
            int bonus = body.IndexOf("TeamHQBonus.ApplyPotionHeal(player, healAmount)", from, StringComparison.Ordinal);
            int cap = body.IndexOf("Math.Min(healAmount, player.MaxHP - player.HP)", from, StringComparison.Ordinal);
            difficulty.Should().BeGreaterThan(0);
            bonus.Should().BeGreaterThan(difficulty, "the Infirmary is the last modifier");
            cap.Should().BeGreaterThan(bonus, "and comes before the cap to missing HP");
            from = cap + 1;
        }
    }

    [Fact]
    public void TheTeammatePotion_UsesTheOwnersInfirmary()
    {
        string body = MethodBody("Scripts/Systems/CombatEngine.cs", "TeammateHealWithPotion");
        body.Should().Contain("potionOwner = owner;", "a potion from the player's belt carries the belt owner's bonus");
        body.Should().Contain("TeamHQBonus.ApplyPotionHeal(potionOwner, healAmount)");
    }

    [Fact]
    public void TheHomePotion_PutsTheInfirmaryInsideTheFloor()
    {
        MethodBody("Scripts/Locations/HomeLocation.cs", "UseHealingPotion")
            .Should().Contain("Math.Max(50, TeamHQBonus.ApplyPotionHeal(currentPlayer, currentPlayer.MaxHP / 4))");
    }

    // Behaviour.
    private static readonly BindingFlags F = BindingFlags.NonPublic | BindingFlags.Instance;

    private static Character Drinker(int infirmary) => new Character
    {
        Name1 = "infirmary", Name2 = "Infirmary", Class = CharacterClass.Warrior, Level = 20, HP = 1, MaxHP = 100_000,
        Healing = 5, CombatSpeed = CombatSpeed.Instant,
        Team = "X", HQLevelsTeam = infirmary > 0 ? "X" : "", HQInfirmaryLevel = infirmary,
    };

    private static async Task<long> QuickHeal(Character hero, int seed)
    {
        var engine = new CombatEngine(new TerminalEmulator(new MemoryStream(), new MemoryStream()));
        typeof(CombatEngine).GetField("random", F)!.SetValue(engine, new Random(seed));
        typeof(CombatEngine).GetField("currentPlayer", F)!.SetValue(engine, hero);
        long before = hero.HP;
        await (Task)typeof(CombatEngine).GetMethod("ExecuteHeal", F)!
            .Invoke(engine, new object[] { hero, new CombatResult(), true })!;
        hero.Healing.Should().Be(4, "one potion drunk");
        return hero.HP - before;
    }

    [Fact]
    public async Task AQuickCombatPotion_Heals20PercentMore_WithInfirmary2()
    {
        for (int seed = 1; seed <= 10; seed++)
        {
            long plain = await QuickHeal(Drinker(0), seed);
            long boosted = await QuickHeal(Drinker(2), seed);
            plain.Should().BeGreaterThan(100);
            boosted.Should().Be((long)Math.Round(plain * 1.2), "Infirmary 2 multiplies the final heal by 1.2");
        }
    }

    [Fact]
    public async Task LevelsReadForAnotherTeam_GiveNothing()
    {
        var stale = Drinker(2);
        stale.HQLevelsTeam = "Y";
        (await QuickHeal(stale, 3)).Should().Be(await QuickHeal(Drinker(0), 3));
    }

    private static async Task<long> DungeonPotion(Character hero)
    {
        var dungeon = new DungeonLocation();
        typeof(BaseLocation).GetField("terminal", F)!.SetValue(dungeon, new TerminalEmulator(new MemoryStream(), new MemoryStream()));
        long before = hero.HP;
        await (Task)typeof(DungeonLocation).GetMethod("UseHealingPotion", F, null, new[] { typeof(Character) }, null)!
            .Invoke(dungeon, new object[] { hero })!;
        hero.Healing.Should().Be(4, "one potion drunk");
        return hero.HP - before;
    }

    [Fact]
    public async Task ADungeonPotion_Heals20PercentMore_WithInfirmary2()
    {
        long plain = await DungeonPotion(Drinker(0));
        long boosted = await DungeonPotion(Drinker(2));
        plain.Should().Be(25_000, "a quarter of MaxHP");
        boosted.Should().Be(30_000);
    }
}
