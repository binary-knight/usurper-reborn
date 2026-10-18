using System;
using System.Collections.Generic;
using System.IO;
using System.Text.Json;
using FluentAssertions;
using Xunit;

namespace UsurperReborn.Tests;

/// <summary>
/// 1.3.5: no fight path reads a monster ability's ExtraAttacks, so Multiattack and Time Stop strike once. Their messages said
/// "attacks in a flurry of blows!" and "strikes repeatedly!" in every language; they now announce the one attack.
/// </summary>
public class MonsterSwingText135Tests
{
    // the words for more than one strike each locale used before 1.3.5
    private static readonly Dictionary<string, string[]> OldWords = new()
    {
        ["en"] = new[] { "flurry of blows", "repeatedly" },
        ["es"] = new[] { "ráfaga de golpes", "sin cesar" },
        ["fr"] = new[] { "déluge de coups", "sans relâche" },
        ["hu"] = new[] { "csapások özönével", "többször" },
        ["it"] = new[] { "raffica di colpi", "ripetutamente" },
    };

    private static string RepoRoot()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir != null && !(Directory.Exists(Path.Combine(dir.FullName, "Scripts")) && Directory.Exists(Path.Combine(dir.FullName, "Localization"))))
            dir = dir.Parent;
        dir.Should().NotBeNull("the test must run inside the repository");
        return dir!.FullName;
    }

    private static string Line(string lang, string key)
    {
        var doc = JsonSerializer.Deserialize<Dictionary<string, JsonElement>>(
            File.ReadAllText(Path.Combine(RepoRoot(), "Localization", lang + ".json")))!;
        return doc[key].GetString() ?? "";
    }

    [Theory]
    [InlineData("en")]
    [InlineData("es")]
    [InlineData("fr")]
    [InlineData("hu")]
    [InlineData("it")]
    public void MultiattackAndTimeStop_PromiseNoExtraStrikes_InEveryLanguage(string lang)
    {
        foreach (string key in new[] { "mability.multiattack", "mability.stops_time" })
        {
            string line = Line(lang, key);
            line.Should().Contain("{0}", $"{lang} {key}: the monster's name");
            foreach (string old in OldWords[lang])
                line.Should().NotContain(old, $"{lang} {key}: the monster strikes once");
        }
    }

    [Fact]
    public void MultiattackAndTimeStop_English_AreExact()
    {
        Line("en", "mability.multiattack").Should().Be("{0} attacks in a sudden blur!");
        Line("en", "mability.stops_time").Should().Be("{0} stops time and strikes!");
    }
}
