using System;
using System.IO;
using System.Linq;
using System.Text.Json;
using FluentAssertions;
using Xunit;

namespace UsurperReborn.Tests;

/// <summary>
/// 1.4.0 RT: the English values say "Defense", as the rest of the game does; the six keys that carry the old
/// spelling in their names are unchanged; the NPC Voidreaver Constitution floor is in the NPC guide.
/// </summary>
public class RT140Rows
{
    private static string RepoRoot()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir != null && !(Directory.Exists(Path.Combine(dir.FullName, "Scripts")) && Directory.Exists(Path.Combine(dir.FullName, "Localization"))))
            dir = dir.Parent;
        dir.Should().NotBeNull("the test must run inside the repository");
        return dir!.FullName;
    }

    /// <summary>The wiki armor page quotes this value and three wiki tests pin "Defence" in it (1.3.7 spelling test).</summary>
    private const string PinnedByWikiTests = "item.set.bonus.defence";

    private static readonly string[] KeysKept =
    {
        "combat.status_defence_label", "temple.blessing_defence", "artifact.stat.defence",
        "opening.bonus_defence", "encounter.minstrel.plus_defence", "item.set.bonus.defence",
    };

    private static System.Collections.Generic.Dictionary<string, string> En()
    {
        var text = File.ReadAllText(Path.Combine(RepoRoot(), "Localization", "en.json"));
        return JsonSerializer.Deserialize<System.Collections.Generic.Dictionary<string, string>>(text)!;
    }

    [Fact]
    public void RT140_NoEnglishValueSaysDefence_ExceptTheOnePinnedByWikiTests()
    {
        var bad = En().Where(kv => kv.Key != PinnedByWikiTests
            && kv.Value.IndexOf("defence", StringComparison.OrdinalIgnoreCase) >= 0).Select(kv => kv.Key).ToList();
        bad.Should().BeEmpty("the English text spells it Defense");
    }

    [Fact]
    public void RT140_TheSixKeysWithTheOldSpellingInTheirNameAreUnchanged()
    {
        var en = En();
        foreach (var k in KeysKept) en.ContainsKey(k).Should().BeTrue(k + " is looked up by name and keeps its name");
        en.Keys.Count(k => k.Contains("defence", StringComparison.OrdinalIgnoreCase)).Should().Be(KeysKept.Length);
    }

    [Fact]
    public void RT140_TheNpcGuideStatesTheVoidreaverConstitutionFloor()
    {
        var guide = File.ReadAllText(Path.Combine(RepoRoot(), "DOCS", "wiki", "online", "npcs.md"));
        guide.Should().Contain("An NPC Voidreaver is raised once, when it first loads in 1.3.7 or later, to a Constitution of at least 10 plus the total its levels have given the class");
    }

    [Fact]
    public void RT140_TheDungeonFloorSummarySaysOneHour()
    {
        var src = File.ReadAllText(Path.Combine(RepoRoot(), "Scripts", "Systems", "DungeonGenerator.cs"));
        src.Should().Contain("Regular floors respawn after 1 hour,").And.NotContain("respawn after 24 hours");
        src.Should().Contain("RESPAWN_HOURS = 1;");
    }
}
