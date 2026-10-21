using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using FluentAssertions;
using Xunit;

namespace UsurperReborn.Tests;

/// <summary>
/// 1.3.6 G4 (condition b): a source scan. No file under Scripts writes the four slot fields (TempAttackBonus,
/// TempAttackBonusDuration, TempDefenseBonus, TempDefenseBonusDuration) directly with =, +=, -=, ++ or --; every write
/// goes through Character.AddSlotBonus, ClearSlotBonus, ClearSlotBonuses or TickSlotBonuses, which keep the entries and
/// the shown value in step. The named exceptions below each carry their reason.
/// </summary>
public class SlotWrites136Tests
{
    private const string Field = "(TempAttackBonusDuration|TempAttackBonus|TempDefenseBonusDuration|TempDefenseBonus)\\b";
    private static readonly Regex PostWrite = new(Field + "\\s*(\\+\\+|--|\\+=|-=|=(?!=))");
    private static readonly Regex PreWrite = new("(\\+\\+|--)\\s*[\\w\\.\\[\\]\\(\\)]*?\\." + Field);

    /// <summary>The helpers' own writes (Character.RecomputeSlot, the one place that sets the shown value and rounds).</summary>
    private static readonly HashSet<string> HelperLines = new()
    {
        "if (slot == BonusSlot.Attack) { TempAttackBonus = value; TempAttackBonusDuration = rounds; }",
        "else { TempDefenseBonus = value; TempDefenseBonusDuration = rounds; }",
    };
    private const string HelperFile = "Scripts/Core/Character.cs";

    /// <summary>Files allowed to write the fields, with the reason.</summary>
    private static readonly Dictionary<string, string> Exceptions = new()
    {
        ["Scripts/AI/NPCCombatSimulator.cs"] =
            "the off screen NPC fights: their own bonus apply (ApplyBuffAbility) and their own round end tick, and no call into " +
            "CombatEngine's tick or any recompute, so their raw writes never meet the entries (rule 3)",
    };

    private static string Root()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir != null && !File.Exists(Path.Combine(dir.FullName, "usurper-reloaded.csproj"))) dir = dir.Parent;
        dir.Should().NotBeNull("the repository root holds usurper-reloaded.csproj");
        return dir!.FullName;
    }

    internal static List<string> DirectWrites(string root)
    {
        var hits = new List<string>();
        foreach (var path in Directory.EnumerateFiles(Path.Combine(root, "Scripts"), "*.cs", SearchOption.AllDirectories))
        {
            string rel = Path.GetRelativePath(root, path).Replace('\\', '/');
            if (Exceptions.ContainsKey(rel)) continue;
            var lines = File.ReadAllLines(path);
            for (int i = 0; i < lines.Length; i++)
            {
                string code = lines[i];
                int comment = code.IndexOf("//", StringComparison.Ordinal);
                if (comment >= 0) code = code.Substring(0, comment);
                if (rel == HelperFile && HelperLines.Contains(lines[i].Trim())) continue;
                if (PostWrite.IsMatch(code) || PreWrite.IsMatch(code))
                    hits.Add($"{rel}:{i + 1}: {lines[i].Trim()}");
            }
        }
        return hits;
    }

    [Fact]
    public void SlotWrites136_NoDirectWriteOfTheSlotFields_OutsideTheHelpers()
    {
        DirectWrites(Root()).Should().BeEmpty("every write of the four slot fields goes through the Character helpers");
    }

    [Fact]
    public void SlotWrites136_TheScan_SeesEachKindOfWrite()
    {
        foreach (var line in new[]
        {
            "p.TempAttackBonus = 5;", "p.TempAttackBonus += 5;", "p.TempDefenseBonus -= 20;", "p.TempAttackBonusDuration++;",
            "p.TempDefenseBonusDuration--;", "if (p.TempAttackBonusDuration > 0 && --p.TempAttackBonusDuration <= 0)",
        })
            (PostWrite.IsMatch(line) || PreWrite.IsMatch(line)).Should().BeTrue(line);
        foreach (var line in new[] { "if (p.TempAttackBonus == 0)", "x = p.TempDefenseBonus;", "if (p.TempAttackBonusDuration <= 0)", "p.TempAttackBonus > 0" })
            (PostWrite.IsMatch(line) || PreWrite.IsMatch(line)).Should().BeFalse(line);
    }

    [Fact]
    public void SlotWrites136_EachException_StillWritesTheFields()
    {
        // an exception that no longer writes is stale and goes
        string root = Root();
        var helper = File.ReadAllLines(Path.Combine(root, HelperFile)).Select(l => l.Trim()).ToHashSet();
        foreach (var line in HelperLines) helper.Should().Contain(line, "the helper's own write is still there");
        foreach (var rel in Exceptions.Keys)
            File.ReadAllLines(Path.Combine(root, rel)).Any(l => PostWrite.IsMatch(l) || PreWrite.IsMatch(l)).Should().BeTrue(rel);
    }
}
