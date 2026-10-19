using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;
using FluentAssertions;
using UsurperRemake;
using UsurperRemake.Systems;
using Xunit;

namespace UsurperReborn.Tests;

/// <summary>1.3.5: a story companion's team XP line shows its progress to the next level, as an NPC
/// teammate's line does. The numbers are the Companion's own, read after the award; the award is unchanged.
/// Synthetic party only.</summary>
[Collection("SharedGameSingletons")]
public class CompanionXpLine135Tests
{
    private static string Strip(string s) => Regex.Replace(s, "\u001b\\[[0-9;]*[A-Za-z]", "");

    /// <summary>A fresh recruited companion at the given level and experience, its wrapper at the same level.</summary>
    private static (Companion comp, Character wrapper) NewCompanion(CompanionId id, int level, long experience)
    {
        var wrapper = PartySurvival133Harness.FreshCompanion(id);
        var comp = CompanionSystem.Instance.GetCompanion(id)!;
        comp.Level = level;
        comp.Experience = experience;
        wrapper.Level = level;
        return (comp, wrapper);
    }

    private static Character NpcMate(int level)
    {
        var mate = PartySurvival133Harness.Ally("Synthmate");
        mate.Level = level;
        mate.Experience = GameConfig.GetExperienceForLevel(level);
        return mate;
    }

    /// <summary>Runs one slot award and returns the shown text and the shares used.</summary>
    private static (string text, int[] shares) Award(Character hero, List<Character> team, long pot)
    {
        var rig = PartySurvival133Harness.NewRig(hero, team);
        var shares = CombatEngine.ResolveTeamXPShares(hero, team);
        PartySurvival133Harness.Call(rig.Engine, "DistributeTeamSlotXP", hero, team, pot, rig.Term, shares);
        rig.Term.StreamWriterInternal?.Flush();
        return (Strip(rig.Text), shares);
    }

    /// <summary>The one shown row that starts with the teammate's name and share.</summary>
    private static string Row(string text, string name)
        => text.Split('\n').Select(r => r.TrimEnd('\r').Trim()).Single(r => r.StartsWith(name + " ("));

    private static string Line(string lang, string name, int percent, long slotXP, long exp, long need, string catchUp = "")
        => Loc.GetIn(lang, "combat.teammate_xp_share_progress", name, percent, slotXP, exp.ToString("N0"), need.ToString("N0"), catchUp);

    [Fact]
    public void CompanionLine_ShowsTheCompanionsOwnProgress_AndTheNpcLineIsAsToday()
    {
        var prevLang = GameConfig.Language;
        try
        {
            GameConfig.Language = "en";
            var (comp, wrapper) = NewCompanion(CompanionId.Aldric, 10, GameConfig.GetExperienceForLevel(10));
            wrapper.Experience = 0;   // the wrapper's figure is not the companion's
            var mate = NpcMate(10);
            long mateBefore = mate.Experience;
            var hero = PartySurvival133Harness.Hero();   // level 10: no catch-up
            const long pot = 3000;

            var (text, shares) = Award(hero, new List<Character> { wrapper, mate }, pot);

            long compSlot = (long)(pot * shares[1] / 100.0);
            long mateSlot = (long)(pot * shares[2] / 100.0);
            comp.Level.Should().Be(10, "the slot is too small to cross a level");
            string companionLine = Line("en", "Aldric", shares[1], compSlot, comp.Experience, GameConfig.GetExperienceForLevel(11));
            Row(text, "Aldric").Should().Be(companionLine, "the companion's line carries its own Experience and next level need after the award");
            Row(text, "Aldric").Should().EndWith($"({comp.Experience:N0}/{GameConfig.GetExperienceForLevel(11):N0})");
            Row(text, "Synthmate").Should().Be(Line("en", "Synthmate", shares[2], mateSlot, mateBefore + mateSlot, GameConfig.GetExperienceForLevel(11)),
                "the NPC line is as today");
        }
        finally { GameConfig.Language = prevLang; PartySurvival133Harness.RestoreSilence(); }
    }

    [Fact]
    public void CompanionLine_AfterALevelCrossed_ShowsTheNewLevelsNeed()
    {
        var prevLang = GameConfig.Language;
        try
        {
            GameConfig.Language = "en";
            var (comp, wrapper) = NewCompanion(CompanionId.Mira, 10, GameConfig.GetExperienceForLevel(11) - 1);
            var hero = PartySurvival133Harness.Hero();
            const long pot = 5000;

            var (text, shares) = Award(hero, new List<Character> { wrapper }, pot);

            long slot = (long)(pot * shares[1] / 100.0);
            comp.Level.Should().Be(11, "the award crosses one level");
            Row(text, "Mira").Should().Be(Line("en", "Mira", shares[1], slot, comp.Experience, GameConfig.GetExperienceForLevel(12)),
                "a level gained in the same award shows the new level's need");
            text.Should().NotContain($"/{GameConfig.GetExperienceForLevel(11):N0})");
        }
        finally { GameConfig.Language = prevLang; PartySurvival133Harness.RestoreSilence(); }
    }

    [Fact]
    public void Award_MovesTheSameXpAsToday()
    {
        var prevLang = GameConfig.Language;
        try
        {
            GameConfig.Language = "en";
            long start = GameConfig.GetExperienceForLevel(10) + 7;
            var (comp, wrapper) = NewCompanion(CompanionId.Vex, 10, start);
            wrapper.Experience = 123;
            var hero = PartySurvival133Harness.Hero();
            long heroBefore = hero.Experience;
            const long pot = 2000;

            var (_, shares) = Award(hero, new List<Character> { wrapper }, pot);

            comp.Experience.Should().Be(start + (long)(pot * shares[1] / 100.0), "the slot award to the companion is unchanged");
            wrapper.Experience.Should().Be(123, "the display reads the companion and writes nothing to the wrapper");
            hero.Experience.Should().Be(heroBefore, "the slot split pays no one else");
        }
        finally { GameConfig.Language = prevLang; PartySurvival133Harness.RestoreSilence(); }
    }

    [Fact]
    public void CompanionAtTheCap_KeepsThePlainLine()
    {
        var prevLang = GameConfig.Language;
        try
        {
            GameConfig.Language = "en";
            // the wrapper below the cap, the companion at it: the award is refused and the plain line is kept
            var (comp, wrapper) = NewCompanion(CompanionId.Lyris, 100, GameConfig.GetExperienceForLevel(100));
            wrapper.Level = 10;
            var hero = PartySurvival133Harness.Hero();
            const long pot = 2000;

            var (text, shares) = Award(hero, new List<Character> { wrapper }, pot);

            long slot = (long)(pot * shares[1] / 100.0);
            Row(text, "Lyris").Should().Be(Loc.GetIn("en", "combat.teammate_xp_share", "Lyris", shares[1], slot, ""));
            text.Should().NotContain($"/{GameConfig.GetExperienceForLevel(101):N0})");
        }
        finally { GameConfig.Language = prevLang; PartySurvival133Harness.RestoreSilence(); }
    }

    [Theory]
    [InlineData("en")]
    [InlineData("hu")]
    public void CompanionLine_NearTheCap_Fits79Columns(string lang)
    {
        var prevLang = GameConfig.Language;
        try
        {
            GameConfig.Language = lang;
            var (comp, wrapper) = NewCompanion(CompanionId.Melodia, 97, GameConfig.GetExperienceForLevel(97));
            var hero = PartySurvival133Harness.Hero();
            hero.Level = 99;   // two levels ahead: a catch-up label on the line
            const long pot = 1_000_000;   // below one level's need at 97, so the line keeps its progress

            var (text, shares) = Award(hero, new List<Character> { wrapper }, pot);

            comp.Level.Should().Be(97);
            string row = text.Split('\n').Select(r => r.TrimEnd('\r')).Single(r => r.TrimStart().StartsWith("Melodia ("));
            row.Should().Contain($"({comp.Experience:N0}/{GameConfig.GetExperienceForLevel(98):N0})");
            row.Should().EndWith(Loc.GetIn(lang, "combat.catch_up_label", 1.2.ToString("F1")));
            row.Length.Should().BeLessThanOrEqualTo(79, $"the companion row fits 79 columns in {lang}: [{row}]");
        }
        finally { GameConfig.Language = prevLang; PartySurvival133Harness.RestoreSilence(); }
    }
}
