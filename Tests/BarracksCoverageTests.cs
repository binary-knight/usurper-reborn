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
/// v1.1.11: the Team HQ Barracks divides every combat hit an enemy lands on a player by 1 + 5% per
/// level, PvE and PvP, as the last modifier of that hit, just before it leaves the player's HP. A
/// minimum-damage floor earlier in the chain is re-applied after it, so the anti-tank minimum holds.
/// There is no single choke point, so every player-side HP write in CombatEngine must either apply
/// TeamHQBonus.ApplyDefense within the eight lines above it (or call a helper that does), sit in a
/// method listed below with its reason, or carry an "hq-barracks: out (reason)" comment.
/// </summary>
[Collection("SharedGameSingletons")]
public class BarracksCoverageTests
{
    private static string RepoRoot()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir != null && !(Directory.Exists(Path.Combine(dir.FullName, "Scripts")) && Directory.Exists(Path.Combine(dir.FullName, "Localization"))))
            dir = dir.Parent;
        dir.Should().NotBeNull("the test must run inside the repository");
        return dir!.FullName;
    }

    private static string[] CombatEngineLines() => File.ReadAllLines(Path.Combine(RepoRoot(), "Scripts", "Systems", "CombatEngine.cs"));

    // "<var>.HP -= ..." or "<var>.HP = Math.Max(0, <var>.HP - ...)"
    private static readonly Regex HpWrite = new(@"\b([A-Za-z_][A-Za-z_0-9]*)\.HP\s*(?:-=|=\s*Math\.Max\(\s*0\s*,\s*\1\.HP\s*-)");

    private static readonly Regex MethodSignature = new(@"^\s*(?:public|private|internal|protected)\b[^=;]*?(\w+)\s*(?:<[^>]*>)?\s*\(");

    // The variables that only ever hold an enemy. Every other name is treated as the player's side,
    // so a write through a new variable name fails until someone looks at it. "target" is an enemy
    // only where the method declares "Monster target" or is listed in MonsterTargetMethods.
    private static readonly HashSet<string> EnemySide = new() { "monster", "m", "corrodeTarget" };

    private static readonly Dictionary<string, string> MonsterTargetMethods = new()
    {
        ["TryTeammateOffensiveSpell"] = "its target is picked from the living monsters",
    };

    private static readonly Dictionary<string, string> ExcludedMethods = new()
    {
        ["ExecuteFightToDeath"] = "dead: only called from ProcessPlayerAction, which throws DeadCombatPathGuard",
    };

    // Helpers that apply the Barracks inside them; a call within the window counts as applying it.
    private static readonly string[] ApplyingHelpers = { "MitigateCompanionAbilityHit(" };

    // Writes whose Barracks sits further up than the window, each pinned by its own ordering test.
    private static readonly Dictionary<string, string> FarApplications = new()
    {
        ["ProcessMonsterAction"] = "applied after the flee grace and before divine intervention and the companion sacrifice; see TheBasicAttack_AppliesTheBarracks_BeforeTheSurviveAtOneLogic",
    };

    private static string MethodAt(string[] lines, int index, out int start)
    {
        for (int j = index; j >= 0; j--)
        {
            var m = MethodSignature.Match(lines[j]);
            if (m.Success) { start = j; return m.Groups[1].Value; }
        }
        start = 0;
        return "?";
    }

    private static List<(int line, string method, string text)> PlayerHpWrites(string[] lines)
    {
        var hits = new List<(int, string, string)>();
        for (int i = 0; i < lines.Length; i++)
        {
            string code = lines[i].TrimStart();
            if (code.StartsWith("//") || code.StartsWith("*")) continue;
            var m = HpWrite.Match(lines[i]);
            if (!m.Success) continue;
            string variable = m.Groups[1].Value;
            if (EnemySide.Contains(variable)) continue;
            string method = MethodAt(lines, i, out int start);
            if (variable == "target" && (MonsterTargetMethods.ContainsKey(method) || Regex.IsMatch(lines[start], @"\bMonster\??\s+target\b")))
                continue;
            hits.Add((i + 1, method, lines[i].Trim()));
        }
        return hits;
    }

    [Fact]
    public void EveryPlayerHpWriteInCombatEngine_AppliesTheBarracksOrSaysWhyNot()
    {
        var lines = CombatEngineLines();
        var hits = PlayerHpWrites(lines);
        hits.Count.Should().BeGreaterThan(30, "the scan must find the player-side HP writes, or it proves nothing");

        var problems = new List<string>();
        foreach (var (line, method, text) in hits)
        {
            if (ExcludedMethods.ContainsKey(method) || FarApplications.ContainsKey(method)) continue;
            int from = Math.Max(0, line - 1 - 8);
            bool applied = lines.Skip(from).Take(line - from)
                .Any(l => l.Contains("TeamHQBonus.ApplyDefense") || ApplyingHelpers.Any(h => l.Contains(h)));
            int comment = text.IndexOf("//", StringComparison.Ordinal);
            bool markedOut = comment >= 0 && Regex.IsMatch(text.Substring(comment), @"hq-barracks: out \(\S[^)]*\)");
            if (!applied && !markedOut)
                problems.Add($"CombatEngine.cs:{line} in {method}: {text}");
        }
        problems.Should().BeEmpty("each player-side HP write applies TeamHQBonus.ApplyDefense just before it or is marked out with a reason");
    }

    [Fact]
    public void TheScan_SeesThePvPAndTeammateWrites()
    {
        // A guard on the variable rules: these are the writes most easily lost by a narrower filter.
        var methods = PlayerHpWrites(CombatEngineLines()).Select(h => h.method).ToHashSet();
        methods.Should().Contain(new[] { "ExecutePvPSingleHit", "ProcessComputerPlayerAction", "MonsterAttacksCompanion", "ProcessBossChannel", "ProcessBossAoE", "ProcessCorruptionTick", "TryManweBossAbility" });
    }

    [Fact]
    public void TheListedMethods_StillExist()
    {
        var methods = CombatEngineLines().Select(l => MethodSignature.Match(l)).Where(m => m.Success).Select(m => m.Groups[1].Value).ToHashSet();
        ExcludedMethods.Keys.Concat(MonsterTargetMethods.Keys).Concat(FarApplications.Keys)
            .Where(k => !methods.Contains(k)).Should().BeEmpty("a stale entry would hide a new method of the same name");
    }

    [Fact]
    public void TheOldBarracksReader_IsGone()
    {
        string src = File.ReadAllText(Path.Combine(RepoRoot(), "Scripts", "Systems", "CombatEngine.cs"));
        src.Should().NotContain("TeamHQBonus.Barracks(", "Barracks added to defence before the floor counted twice with ApplyDefense");
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

    [Fact]
    public void TheBasicAttack_AppliesTheBarracks_BeforeTheSurviveAtOneLogic()
    {
        string body = MethodBody("Scripts/Systems/CombatEngine.cs", "ProcessMonsterAction");
        int grace = body.IndexOf("ApplyFleeGrace(player, actualDamage)", StringComparison.Ordinal);
        int barracks = body.IndexOf("TeamHQBonus.ApplyDefense(player, actualDamage)", StringComparison.Ordinal);
        int divine = body.IndexOf("CheckDivineIntervention(", StringComparison.Ordinal);
        int sacrifice = body.IndexOf("CheckCompanionSacrifice(", StringComparison.Ordinal);
        int write = body.IndexOf("player.HP = Math.Max(0, player.HP - actualDamage)", StringComparison.Ordinal);
        grace.Should().BeGreaterThan(0);
        barracks.Should().BeGreaterThan(grace, "the Barracks is the last modifier, after the flee grace");
        divine.Should().BeGreaterThan(barracks, "survive-at-1 must see the final number, not have it divided");
        sacrifice.Should().BeGreaterThan(barracks);
        write.Should().BeGreaterThan(barracks);
        Count(body, "TeamHQBonus.ApplyDefense(").Should().Be(1);
    }

    [Fact]
    public void TheCompanionAbilityHelper_AppliesTheBarracks()
    {
        MethodBody("Scripts/Systems/CombatEngine.cs", "MitigateCompanionAbilityHit").Should().Contain("TeamHQBonus.ApplyDefense(companion, damage)");
    }

    [Fact]
    public void ShadowIncarnate_ReducesTheHitBeforeTheHealReadsIt()
    {
        string body = MethodBody("Scripts/Systems/CombatEngine.cs", "TryManweBossAbility");
        Count(body, "TeamHQBonus.ApplyDefense(player,").Should().Be(12, "every named Manwe hit");
        int shadow = body.IndexOf("\"Shadow Incarnate\"", StringComparison.Ordinal);
        int barracks = body.IndexOf("TeamHQBonus.ApplyDefense", shadow, StringComparison.Ordinal);
        int heal = body.IndexOf("long healAmt = damage", shadow, StringComparison.Ordinal);
        barracks.Should().BeGreaterThan(shadow);
        heal.Should().BeGreaterThan(barracks);
    }

    [Theory]
    [InlineData("Scripts/Systems/WorldBossSystem.cs", "ProcessBossActions", 1)]
    [InlineData("Scripts/Locations/PrisonWalkLocation.cs", "BattlePrisonGuards", 1)]
    [InlineData("Scripts/Locations/CastleLocation.cs", "CastleSiegeMenu", 2)]
    public void TheFightsOutsideCombatEngine_ApplyTheBarracks(string file, string method, int sites)
    {
        Count(MethodBody(file, method), "TeamHQBonus.ApplyDefense(").Should().BeGreaterThanOrEqualTo(sites);
    }

    [Fact]
    public void TheWorldBoss_ReappliesItsStrengthMinimum_AfterTheBarracks()
    {
        string body = MethodBody("Scripts/Systems/WorldBossSystem.cs", "ProcessBossActions");
        body.Should().Contain("BossMinimumDamage(bossData)");
        body.IndexOf("TeamHQBonus.ApplyDefense(player,", StringComparison.Ordinal)
            .Should().BeLessThan(body.IndexOf("player.HP = Math.Max(0, player.HP - bossDmg)", StringComparison.Ordinal));
    }

    [Fact]
    public void TheHelper_NeverTakesOneToZero_AtTheTopLevel()
    {
        var c = new Character { Team = "X", HQLevelsTeam = "X", HQBarracksLevel = 10 };
        TeamHQBonus.ApplyDefense(c, 1).Should().Be(1, "every Math.Max(1) floor before it therefore still holds");
    }

    // Behaviour: the real monster basic attack, same dice with and without the Barracks.
    private static readonly BindingFlags F = BindingFlags.NonPublic | BindingFlags.Instance;

    private static Monster Brute() => new Monster { Name = "Brute", Level = 20, HP = 1_000_000, MaxHP = 1_000_000, Strength = 1000, WeapPow = 200, IsActive = true };

    private static Character Hero(int barracks, long defence = 0) => new Character
    {
        Name1 = "barracks", Name2 = "Barracks", Class = CharacterClass.Warrior, Level = 20, HP = 100_000, MaxHP = 100_000,
        Defence = defence, CombatSpeed = CombatSpeed.Instant,
        Team = "X", HQLevelsTeam = barracks > 0 ? "X" : "", HQBarracksLevel = barracks,
    };

    // The damage of one monster swing with the engine's dice seeded, or -1 when it missed or was dodged.
    private static async Task<long> Swing(Character hero, int seed)
    {
        var engine = new CombatEngine(new TerminalEmulator(new MemoryStream(), new MemoryStream()));
        typeof(CombatEngine).GetField("random", F)!.SetValue(engine, new Random(seed));
        typeof(CombatEngine).GetField("currentPlayer", F)!.SetValue(engine, hero);
        long before = hero.HP;
        await (Task)typeof(CombatEngine).GetMethod("ProcessMonsterAction", F)!
            .Invoke(engine, new object?[] { Brute(), hero, new CombatResult(), null })!;
        long taken = before - hero.HP;
        return taken > 0 ? taken : -1;
    }

    private static async Task<List<(long plain, long barracks)>> Pairs(long defence)
    {
        var pairs = new List<(long, long)>();
        for (int seed = 1; seed <= 200 && pairs.Count < 20; seed++)
        {
            long plain = await Swing(Hero(0, defence), seed);
            long barracks = await Swing(Hero(2, defence), seed);
            if (plain > 0 && barracks > 0) pairs.Add((plain, barracks));
        }
        pairs.Should().NotBeEmpty("some swings must land for the comparison to mean anything");
        return pairs;
    }

    [Fact]
    public async Task AMonsterBasicAttack_IsDividedByTheBarracks()
    {
        foreach (var (plain, barracks) in await Pairs(defence: 0))
        {
            plain.Should().BeGreaterThan(300, "the hit sits well above the anti-tank minimum");
            barracks.Should().Be((long)Math.Round(plain / 1.1), "Barracks 2 divides the final hit by 1.1");
        }
    }

    [Fact]
    public async Task TheAntiTankMinimum_StillHolds_AfterTheBarracks()
    {
        // Defence far above the attack: the hit is the minimum, 0.25% of 100,000 MaxHP or 5% of the attack.
        foreach (var (plain, barracks) in await Pairs(defence: 10_000_000))
        {
            plain.Should().BeLessThan(1000, "the hit is the floor");
            barracks.Should().Be(plain, "the Barracks never takes a hit below the minimum it had already reached");
        }
    }

    [Fact]
    public async Task LevelsReadForAnotherTeam_GiveNothing()
    {
        var other = Hero(2);
        other.HQLevelsTeam = "Y";
        long plain = await Swing(Hero(0), 7);
        long stale = await Swing(other, 7);
        stale.Should().Be(plain);
    }
}
