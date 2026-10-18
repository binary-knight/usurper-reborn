using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using FluentAssertions;
using Xunit;

namespace UsurperReborn.Tests;

/// <summary>
/// 1.3.5: Death's Embrace (Voidreaver) said "revive with 1 HP and become invulnerable for 1 round. Lasts 3 rounds." The cast
/// regenerates for 3 rounds and dodges the next attack; the revive is not timed, it fires the next time HP reaches 0 in that
/// fight, at 15 percent of max HP, and dodges the next attack (every revive block in CombatEngine.cs). The text now says so.
/// </summary>
public class DeathsEmbraceText135Tests
{
    private const string Key = "ability.deaths_embrace.desc";

    private const string English = "Cheat death. Regenerate for 3 rounds and dodge the next attack. The next time your HP " +
        "reaches 0 in this fight, revive with 15% of max HP and dodge the next attack.";

    // the one-HP revive and the invulnerable round each locale said before 1.3.5
    private static readonly Dictionary<string, string[]> OldWords = new()
    {
        ["en"] = new[] { "1 HP", "invulnerable" },
        ["es"] = new[] { "1 PV", "invulnerable" },
        ["fr"] = new[] { "1 PV", "invulnérable" },
        ["hu"] = new[] { "1 ÉP", "sebezhetetlen" },
        ["it"] = new[] { "1 PV", "invulnerabile" },
    };

    private static string RepoRoot()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir != null && !File.Exists(Path.Combine(dir.FullName, "Localization", "en.json")))
            dir = dir.Parent;
        dir.Should().NotBeNull("the test must run inside the repository");
        return dir!.FullName;
    }

    private static string Desc(string lang)
    {
        var doc = JsonSerializer.Deserialize<Dictionary<string, JsonElement>>(
            File.ReadAllText(Path.Combine(RepoRoot(), "Localization", lang + ".json")))!;
        return doc[Key].GetString() ?? "";
    }

    [Fact]
    public void DeathsEmbraceDesc_English_IsExact()
    {
        Desc("en").Should().Be(English);
    }

    [Fact]
    public void DeathsEmbraceDescription_InCode_MatchesEnglish()
    {
        ClassAbilitySystem.GetAbility("deaths_embrace")!.Description.Should().Be(English);
    }

    [Theory]
    [InlineData("en")]
    [InlineData("es")]
    [InlineData("fr")]
    [InlineData("hu")]
    [InlineData("it")]
    public void DeathsEmbraceDesc_SaysFifteenPercent_InEveryLanguage(string lang)
    {
        string desc = Desc(lang);
        desc.Should().Contain("15", $"{lang}: the revive is 15 percent of max HP").And.Contain("3", $"{lang}: 3 rounds of regeneration");
        foreach (string old in OldWords[lang])
            desc.Should().NotContain(old, $"{lang}: the revive is not at 1 HP and gives no invulnerable round");
        desc.Should().NotContain("\u2014").And.NotContain("\u2013").And.NotContain(" - ");
    }

    [Fact]
    public void DeathsEmbraceRevive_EveryBlock_IsFifteenPercentAndADodge()
    {
        string[] lines = File.ReadAllLines(Path.Combine(RepoRoot(), "Scripts", "Systems", "CombatEngine.cs"));
        var blocks = lines.Select((l, i) => (l, i)).Where(x => x.l.Contains("player.HP <= 0 && player.DeathsEmbraceActive")).ToList();
        blocks.Should().HaveCount(4, "three revive blocks and ApplyManweDeathsEmbrace");
        foreach (var (_, i) in blocks)
        {
            string body = string.Join("\n", lines.Skip(i).Take(6));
            body.Should().Contain("player.MaxHP * 0.15").And.Contain("player.DodgeNextAttack = true");
        }
    }
}
