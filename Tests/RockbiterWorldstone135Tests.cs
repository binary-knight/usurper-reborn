using System;
using System.Linq;
using System.Text.RegularExpressions;
using FluentAssertions;
using UsurperRemake;
using UsurperRemake.Systems;
using Xunit;

namespace UsurperReborn.Tests;

/// <summary>
/// 1.3.5: the Rockbiter and Worldstone texts say what the code does. Rockbiter gives a flat Defence
/// bonus that grows with level (no percent); the Worldstone cuts damage 50 percent (75 percent against
/// Manwe with the Void Key) and turns Creation's End into heavy damage that can still kill.
/// </summary>
[Collection("SharedGameSingletons")]
public class RockbiterWorldstone135Tests
{
    private static readonly string[] Langs = { "en", "es", "fr", "hu", "it" };
    private static readonly string[] Others = { "es", "fr", "hu", "it" };

    // Whole-word match: the word must not touch a letter on either side, so "live" does not match
    // "deliver" and "vie" does not match "vient". Case-insensitive.
    private static bool HasWord(string text, string word) =>
        Regex.IsMatch(text, @"(?<!\p{L})" + Regex.Escape(word) + @"(?!\p{L})", RegexOptions.IgnoreCase);

    private static ArtifactData Worldstone() =>
        ArtifactSystem.Instance.GetAllArtifacts().Single(a => a.Type == ArtifactType.Worldstone);

    private static string WorldstoneText(string lang) =>
        Loc.GetIn(lang, "artifact.worldstone.ability");

    [Fact]
    public void T1_RockbiterText_MatchesTable_AndHasNoPercent()
    {
        var ability = ClassAbilitySystem.GetAbility("rockbiter")!;
        Loc.GetIn("en", "ability.rockbiter.desc").Should().Be(ability.Description, "the English key matches the table");
        foreach (var l in Langs)
        {
            var t = Loc.GetIn(l, "ability.rockbiter.desc");
            t.Should().NotContain("15", $"{l}: the old armor percent is gone");
            t.Should().NotContain("%", $"{l}: no percent claim");
        }
    }

    [Theory]
    [InlineData(10)]
    [InlineData(50)]
    [InlineData(100)]
    public void T2_Rockbiter_GivesFlatDefenceThatGrowsWithLevel(int level)
    {
        var shaman = new Character { Name1 = "tester", Name2 = "Tester", Class = CharacterClass.MysticShaman, Level = level, HP = 500, MaxHP = 500, AI = CharacterAI.Human };
        var result = ClassAbilitySystem.UseAbility(shaman, "rockbiter", new Random(1));
        result.Success.Should().BeTrue();
        result.DefenseBonus.Should().Be((int)(50 * (1.0 + level * 0.03)), $"flat Defence at level {level}");
        result.Duration.Should().Be(5);
    }

    [Fact]
    public void T2_Rockbiter_DefenceIsHigherAtHigherLevel()
    {
        int At(int level) => ClassAbilitySystem.UseAbility(
            new Character { Name1 = "tester", Name2 = "Tester", Class = CharacterClass.MysticShaman, Level = level, HP = 500, MaxHP = 500, AI = CharacterAI.Human },
            "rockbiter", new Random(1)).DefenseBonus;
        At(50).Should().BeGreaterThan(At(10), "the bonus grows with level");
    }

    [Fact]
    public void T3_WorldstoneText_EnglishMatchesTable_OthersDiffer_NoFalseEffects()
    {
        var a = Worldstone();
        Loc.GetIn("en", "artifact.worldstone.ability").Should().Be(a.SpecialAbility, "the English key matches the table");
        foreach (var l in Others)
            WorldstoneText(l).Should().NotBe(a.SpecialAbility, $"{l} is translated");

        var banned = new System.Collections.Generic.Dictionary<string, string[]>
        {
            ["en"] = new[] { "stun", "knockback", "Earthquake" },
            ["es"] = new[] { "aturdimientos", "aturdimiento", "derribos", "Terremoto" },
            ["fr"] = new[] { "étourdissement", "recul", "Séisme" },
            ["hu"] = new[] { "kábítás", "kábításra", "hátralökés", "hátralökésre", "Földrengés" },
            ["it"] = new[] { "stordimenti", "stordimento", "respinte", "Terremoto" },
        };
        foreach (var l in Langs)
            foreach (var w in banned[l])
                HasWord(WorldstoneText(l), w).Should().BeFalse($"{l} must not name '{w}'");
    }

    [Fact]
    public void T4_WorldstoneText_NamesBothEffects_AndNeverPromisesSurvival()
    {
        var must = new System.Collections.Generic.Dictionary<string, string[]>
        {
            ["en"] = new[] { "50%", "75%" },
            ["es"] = new[] { "50%", "75%" },
            ["fr"] = new[] { "50 %", "75 %" },
            ["hu"] = new[] { "50%", "75%" },
            ["it"] = new[] { "50%", "75%" },
        };
        var heavy = new System.Collections.Generic.Dictionary<string, string>
        {
            ["en"] = "heavy damage", ["es"] = "daño grave", ["fr"] = "lourds dégâts", ["hu"] = "súlyos sebzést", ["it"] = "danni gravi",
        };
        var never = new System.Collections.Generic.Dictionary<string, string[]>
        {
            ["en"] = new[] { "save", "saves", "saved", "survive", "survives", "live", "lives", "alive" },
            ["es"] = new[] { "salva", "salvarte", "sobrevives", "sobrevivir", "sobrevive", "vives", "vivo", "viva" },
            ["fr"] = new[] { "sauve", "sauver", "sauvé", "survivre", "survivez", "survie", "vivant", "vivez" },
            ["hu"] = new[] { "megment", "megmenti", "túlél", "túléled", "túlélni", "életben" },
            ["it"] = new[] { "salva", "salvarti", "sopravvivi", "sopravvivere", "sopravvive", "vivo", "vivi", "vivrai" },
        };
        foreach (var l in Langs)
        {
            var t = WorldstoneText(l);
            foreach (var n in must[l]) t.Should().Contain(n, $"{l} names the cut {n}");
            t.Should().Contain(Loc.GetIn(l, "old_god.ability.creations_end"), $"{l} names Creation's End as the file writes it");
            t.Should().Contain(heavy[l], $"{l} says heavy damage");
            foreach (var w in never[l]) HasWord(t, w).Should().BeFalse($"{l} must not say '{w}'");
        }
    }

    [Fact]
    public void WholeWordMatch_DoesNotTripOnLongerWords()
    {
        HasWord("deliver the goods", "live").Should().BeFalse();
        HasWord("you live on", "live").Should().BeTrue();
        HasWord("Save.", "save").Should().BeTrue();
    }
}
