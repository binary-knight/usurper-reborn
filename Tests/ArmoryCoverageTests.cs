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
/// v1.1.11: the Team HQ Armory (+5% per level) multiplies every hit a player makes on an enemy, PvE
/// and PvP, after every other modifier of that hit and just before it leaves the enemy's HP. There is
/// no single choke point, so every enemy-HP write in CombatEngine must either apply
/// TeamHQBonus.ApplyAttack within the eight lines above it, sit in a method listed below with its
/// reason, or carry an "hq-armory: out (reason)" comment for a derived write (riders, reflect, DoT
/// ticks, a hit that reuses an already boosted number).
/// </summary>
[Collection("SharedGameSingletons")]
public class ArmoryCoverageTests
{
    private static string RepoRoot()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir != null && !(Directory.Exists(Path.Combine(dir.FullName, "Scripts")) && Directory.Exists(Path.Combine(dir.FullName, "Localization"))))
            dir = dir.Parent;
        dir.Should().NotBeNull("the test must run inside the repository");
        return dir!.FullName;
    }

    // "<var>.HP -= ..." or "<var>.HP = Math.Max(0, <var>.HP - ...)"
    private static readonly Regex HpWrite = new(@"\b([A-Za-z_][A-Za-z_0-9]*)\.HP\s*(?:-=|=\s*Math\.Max\(\s*0\s*,\s*\1\.HP\s*-)");

    private static readonly Regex MethodSignature = new(@"^\s*(?:public|private|internal|protected)\b[^=;]*?(\w+)\s*(?:<[^>]*>)?\s*\(");

    // The variables that hold the player's side as the victim: a write to one of them is damage taken.
    // The PvP victims (defender, and opponent in ProcessComputerPlayerAction) are enemies here.
    private static readonly HashSet<string> PlayerSide = new() { "player", "currentPlayer", "companion", "teammate", "tm", "member", "ally", "groupedPlayer" };

    private static readonly Dictionary<string, string> ExcludedMethods = new()
    {
        ["ExecuteSingleAttack"] = "dead: only reached from ProcessPlayerAction, which throws DeadCombatPathGuard",
        ["ExecuteBackstab"] = "dead: only called from ProcessPlayerAction",
        ["ExecuteSoulStrike"] = "dead: only called from ProcessPlayerAction",
        ["ExecutePowerAttack"] = "dead: only called from ProcessPlayerAction",
        ["ExecutePreciseStrike"] = "dead: only called from ProcessPlayerAction",
        ["ExecuteRangedAttack"] = "dead: only called from ProcessPlayerAction",
        ["ExecuteSmite"] = "dead: only called from ProcessPlayerAction",
        ["ExecuteFightToDeath"] = "dead: only called from ProcessPlayerAction",
        ["ApplyAbilityEffects"] = "dead: the single-monster ability path throws DeadCombatPathGuard",
        ["ApplySpellEffects"] = "dead monster branch: every live caller passes a null monster",
        ["ExecuteLearnedAbility"] = "dead: no callers",
        ["ProcessTeammateAction"] = "dead: the single-monster teammate turn has no callers",
        ["CheckElementalEnchantProcsMonster"] = "dead: no callers",
        ["ProcessCorruptionTick"] = "the target is a player-side Character, not an enemy",
        ["ProcessBossAoE"] = "the target is a player-side Character, not an enemy",
    };

    private static string MethodAt(string[] lines, int index)
    {
        for (int j = index; j >= 0; j--)
        {
            var m = MethodSignature.Match(lines[j]);
            if (m.Success) return m.Groups[1].Value;
        }
        return "?";
    }

    private static List<(int line, string method, string text)> EnemyHpWrites(string[] lines)
    {
        var hits = new List<(int, string, string)>();
        for (int i = 0; i < lines.Length; i++)
        {
            string code = lines[i].TrimStart();
            if (code.StartsWith("//") || code.StartsWith("*")) continue;
            var m = HpWrite.Match(lines[i]);
            if (!m.Success || PlayerSide.Contains(m.Groups[1].Value)) continue;
            hits.Add((i + 1, MethodAt(lines, i), lines[i].Trim()));
        }
        return hits;
    }

    [Fact]
    public void EveryEnemyHpWriteInCombatEngine_AppliesTheArmoryOrSaysWhyNot()
    {
        string path = Path.Combine(RepoRoot(), "Scripts", "Systems", "CombatEngine.cs");
        var lines = File.ReadAllLines(path);
        var hits = EnemyHpWrites(lines);
        hits.Count.Should().BeGreaterThan(80, "the scan must find the enemy-HP writes, or it proves nothing");

        var problems = new List<string>();
        foreach (var (line, method, text) in hits)
        {
            if (ExcludedMethods.ContainsKey(method)) continue;
            int from = Math.Max(0, line - 1 - 8);
            bool applied = lines.Skip(from).Take(line - from).Any(l => l.Contains("TeamHQBonus.ApplyAttack"));
            int comment = text.IndexOf("//", StringComparison.Ordinal);
            bool markedOut = comment >= 0 && Regex.IsMatch(text.Substring(comment), @"hq-armory: out \(\S[^)]*\)");
            if (!applied && !markedOut)
                problems.Add($"CombatEngine.cs:{line} in {method}: {text}");
        }
        problems.Should().BeEmpty("each enemy-HP write applies TeamHQBonus.ApplyAttack just before it or is marked out with a reason");
    }

    [Fact]
    public void TheExcludedMethods_StillExist()
    {
        var lines = File.ReadAllLines(Path.Combine(RepoRoot(), "Scripts", "Systems", "CombatEngine.cs"));
        var methods = lines.Select(l => MethodSignature.Match(l)).Where(m => m.Success).Select(m => m.Groups[1].Value).ToHashSet();
        ExcludedMethods.Keys.Where(k => !methods.Contains(k)).Should().BeEmpty("a stale exclusion would hide a new method of the same name");
    }

    [Fact]
    public void TheOldArmoryReaders_AreGone()
    {
        string src = File.ReadAllText(Path.Combine(RepoRoot(), "Scripts", "Systems", "CombatEngine.cs"));
        src.Should().NotContain("TeamHQBonus.Armory(", "Armory added to attack power before armor counted twice with ApplyAttack");
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

    [Theory]
    [InlineData("Scripts/Systems/WorldBossSystem.cs", "RunWorldBossCombat", 1)]
    [InlineData("Scripts/Locations/CastleLocation.cs", "CastleSiegeMenu", 3)]
    [InlineData("Scripts/Locations/PrisonWalkLocation.cs", "BattlePrisonGuards", 1)]
    [InlineData("Scripts/Locations/DungeonLocation.cs", "FightMalachar", 1)]
    public void TheFightsOutsideCombatEngine_ApplyTheArmory(string file, string method, int sites)
    {
        Count(MethodBody(file, method), "TeamHQBonus.ApplyAttack(").Should().BeGreaterThanOrEqualTo(sites);
    }

    [Fact]
    public void TheWorldBoss_AppliesTheArmoryBeforeTheRatioAndCap_AndReadsTheLevels()
    {
        string body = MethodBody("Scripts/Systems/WorldBossSystem.cs", "RunWorldBossCombat");
        int armory = body.IndexOf("TeamHQBonus.ApplyAttack(player, roundDamage)", StringComparison.Ordinal);
        int applied = body.IndexOf("WorldBossMath.Applied(roundDamage", StringComparison.Ordinal);
        armory.Should().BeGreaterThan(0);
        applied.Should().BeGreaterThan(armory);
        body.Should().Contain("TeamHQBonus.RefreshLevels(player)");
    }

    // Behaviour: the real helpers, a hit on a monster with armor 200.
    private static readonly BindingFlags F = BindingFlags.NonPublic | BindingFlags.Instance;

    private static CombatEngine Engine() => new CombatEngine(new TerminalEmulator(new MemoryStream(), new MemoryStream()));

    private static Monster Dummy() => new Monster { Name = "Stone Dummy", Level = 1, HP = 1_000_000, MaxHP = 1_000_000, ArmPow = 200, IsActive = true };

    private static Character Hero(int armory, string levelsTeam = "X") => new Character
    {
        Name1 = "armory", Name2 = "Armory", Class = CharacterClass.Warrior, Level = 10, HP = 500, MaxHP = 500,
        Team = "X", HQLevelsTeam = armory > 0 ? levelsTeam : "", HQArmoryLevel = armory,
    };

    private static async Task<long> SingleHit(Character attacker)
    {
        var target = Dummy();
        long before = target.HP;
        await (Task<bool>)typeof(CombatEngine).GetMethod("ApplySingleMonsterDamage", F)!
            .Invoke(Engine(), new object?[] { target, 1200L, new CombatResult(), "attack", attacker, false })!;
        return before - target.HP;
    }

    [Fact]
    public async Task ASingleHit_GainsFivePercentPerArmoryLevel_AfterArmor()
    {
        long plain = await SingleHit(Hero(0));
        long armory = await SingleHit(Hero(2));
        plain.Should().Be(1000, "1,200 against 200 armor");
        armory.Should().Be(1100, "Armory 2 is +10% of the hit after armor, not of the attack before it");
    }

    [Fact]
    public async Task LevelsReadForAnotherTeam_GiveNothing()
    {
        (await SingleHit(Hero(2, levelsTeam: "Y"))).Should().Be(1000);
    }

    [Fact]
    public async Task ThePlayersOwnAoESpell_UsesTheCurrentPlayersArmory()
    {
        async Task<long> AoE(Character caster)
        {
            var engine = Engine();
            typeof(CombatEngine).GetField("currentPlayer", F)!.SetValue(engine, caster);
            var target = Dummy();
            long before = target.HP;
            await (Task)typeof(CombatEngine).GetMethod("ApplyAoEDamage", F)!
                .Invoke(engine, new object?[] { new List<Monster> { target }, 1200L, new CombatResult(), "spell", true, null })!;
            return before - target.HP;
        }
        (await AoE(Hero(0))).Should().Be(1000);
        (await AoE(Hero(2))).Should().Be(1100);
    }
}
