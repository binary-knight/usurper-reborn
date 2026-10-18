using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using FluentAssertions;
using UsurperRemake.Data;
using UsurperRemake.Systems;
using Xunit;

namespace UsurperReborn.Tests;

/// <summary>
/// 1.3.5 (B1): Maelketh's fight HP times s(n), n the living fighters (the player and every teammate,
/// companion or NPC ally) counted once when the fight is built: s(1) = 1.0, s(2) = 1 - 3k, s(3) = 1 - 2k,
/// s(4) = 1 - k, s(5 or more) = 1.0, k = GameConfig.MaelkethPartyHpScaleK (0.25). Every other god is
/// unchanged. Today's build is the committed fixture (Tests/Fixtures/oldgod-build-7ef83c36.txt).
/// </summary>
[Collection("SharedGameSingletons")]
public class MaelkethPartyHp135Tests
{
    private static BossDialogue Dialogue((string Field, object Value)[] fields)
    {
        var d = new BossDialogue();
        foreach (var (field, value) in fields) typeof(BossDialogue).GetProperty(field)!.SetValue(d, value);
        return d;
    }

    /// <summary>Builds one fixture case at n fighters, the hero recalculated with the case's artifacts held.</summary>
    private static string[] Build(OldGodBossData data, int artifacts, string dialogue, int fighters, out BossFight fight)
    {
        var before = OldGodBuild134Support.SetArtifacts(artifacts);
        try
        {
            var hero = OldGodBuild134Support.Hero();
            hero.RecalculateStats();
            var fields = OldGodBuild134Support.Dialogues.Single(d => d.Name == dialogue).Fields;
            fight = OldGodBossSystem.BuildBossFight(data, artifacts, Dialogue(fields), hero, fighters);
            return OldGodBuild134Support.Describe(fight.Monster, fight.Context, hero);
        }
        finally { OldGodBuild134Support.RestoreArtifacts(before); }
    }

    private static OldGodBossData Maelketh =>
        OldGodBuild134Support.Rows().Single(r => r.Row == "Maelketh").Data;

    private static long FixtureMaxHp(string[] lines) =>
        long.Parse(lines.Single(l => l.StartsWith("monster.MaxHP=")).Split('=')[1]);

    private static string[] WithoutHp(string[] lines) =>
        lines.Where(l => !l.StartsWith("monster.MaxHP=") && !l.StartsWith("monster.HP=")).ToArray();

    [Fact]
    public void TheStep_IsOneConstant_AndNamesMaelketh()
    {
        GameConfig.MaelkethPartyHpScaleK.Should().Be(0.25);
        GameConfig.MaelkethPartyHpScaleGod.Should().Be(OldGodType.Maelketh);
    }

    [Fact]
    public void FiveAndSixFighters_BuildMaelkethAsTheFixture()
    {
        var fixture = OldGodBuild134Support.ReadFixture();
        foreach (var n in new[] { 5, 6 })
        {
            OldGodBossSystem.PartyHpScale(OldGodType.Maelketh, n).Should().Be(1.0, $"n = {n}");
            foreach (var artifacts in OldGodBuild134Support.ArtifactCounts)
                foreach (var (name, _) in OldGodBuild134Support.Dialogues)
                {
                    var key = OldGodBuild134Support.CaseKey("Maelketh", artifacts, name);
                    Build(Maelketh, artifacts, name, n, out _).Should().Equal(fixture[key], $"{key}, n = {n}: the full party's fight as today");
                }
        }
    }

    [Theory]
    [InlineData(3, 0.5)]
    [InlineData(4, 0.75)]
    [InlineData(2, 0.25)]
    public void FewerThanFiveFighters_ScaleMaelkethsHp(int fighters, double scale)
    {
        var fixture = OldGodBuild134Support.ReadFixture();
        OldGodBossSystem.PartyHpScale(OldGodType.Maelketh, fighters).Should().Be(scale, $"s({fighters})");
        foreach (var artifacts in OldGodBuild134Support.ArtifactCounts)
        {
            var key = OldGodBuild134Support.CaseKey("Maelketh", artifacts, "none");
            var lines = Build(Maelketh, artifacts, "none", fighters, out var fight);
            long want = (long)(FixtureMaxHp(fixture[key]) * scale);
            fight.Monster.MaxHP.Should().Be(want, $"{key}, n = {fighters}: HP times {scale}");
            fight.Monster.HP.Should().Be(want, $"{key}, n = {fighters}: the god starts at its full scaled HP");
            WithoutHp(lines).Should().Equal(WithoutHp(fixture[key]), $"{key}, n = {fighters}: only the HP changes");
        }
    }

    [Fact]
    public void ThreeFighters_HalveMaelkethsHp()
    {
        // the measured case (B1-REPORT, k 0.25): 49,500 at floor 25 with no artifact becomes 24,750
        var fixture = OldGodBuild134Support.ReadFixture();
        var key = OldGodBuild134Support.CaseKey("Maelketh", 0, "none");
        Build(Maelketh, 0, "none", 3, out var fight);
        fight.Monster.MaxHP.Should().Be(FixtureMaxHp(fixture[key]) / 2, "three fighters face half his HP");
        fight.Monster.MaxHP.Should().Be(24750);
    }

    [Fact]
    public void Solo_BuildsMaelkethAsTheFixture_AndTheSoloRuleAsToday()
    {
        var fixture = OldGodBuild134Support.ReadFixture();
        OldGodBossSystem.PartyHpScale(OldGodType.Maelketh, 1).Should().Be(1.0);
        foreach (var artifacts in OldGodBuild134Support.ArtifactCounts)
            foreach (var (name, _) in OldGodBuild134Support.Dialogues)
            {
                var key = OldGodBuild134Support.CaseKey("Maelketh", artifacts, name);
                Build(Maelketh, artifacts, name, 1, out _).Should().Equal(fixture[key], $"{key}, n = 1: solo as today");
            }
        // the solo rule (the god deals more and the player takes less alone) is the combat engine's, from
        // the fight's teammates; its constants stand and the engine never reads the party scale
        GameConfig.OldGodSoloDamageBonus.Should().Be(0.15f);
        GameConfig.OldGodSoloPlayerDamageReduction.Should().Be(0.20f);
        var engine = File.ReadAllText(Path.Combine(OldGodBuild134Support.Root(), "Scripts", "Systems", "CombatEngine.cs"));
        engine.Should().NotContain("PartyHpScale", "the fight never reads the scale");
        engine.Should().NotContain("CountLivingFighters", "the fight never recounts the fighters");
    }

    [Fact]
    public void EveryOtherGod_Veloura_Included_UnchangedAtEveryN()
    {
        var fixture = OldGodBuild134Support.ReadFixture();
        var others = OldGodBuild134Support.Rows().Where(r => r.Row != "Maelketh").ToList();
        others.Select(r => r.Row).Should().Contain("Veloura");
        foreach (var (row, data) in others)
            for (int n = 1; n <= 6; n++)
            {
                OldGodBossSystem.PartyHpScale(data.Type, n).Should().Be(1.0, $"{row}, n = {n}");
                foreach (var artifacts in OldGodBuild134Support.ArtifactCounts)
                    foreach (var (name, _) in OldGodBuild134Support.Dialogues)
                    {
                        var key = OldGodBuild134Support.CaseKey(row, artifacts, name);
                        Build(data, artifacts, name, n, out _).Should().Equal(fixture[key], $"{key}, n = {n}: another god is unchanged");
                    }
            }
    }

    [Fact]
    public void ATeammateDying_AfterTheBuild_DoesNotChangeMaelkethsHp()
    {
        // n is counted once, when the fight is built; a fall after that is not recounted
        var hero = OldGodBuild134Support.Hero();
        var team = new List<Character> { OldGodBuild134Support.Hero(), OldGodBuild134Support.Hero() };
        int n = OldGodBossSystem.CountLivingFighters(hero, team);
        n.Should().Be(3);
        Build(Maelketh, 0, "none", n, out var fight);
        long maxHp = fight.Monster.MaxHP;
        maxHp.Should().Be(24750);

        team[0].HP = 0;   // a teammate falls
        OldGodBossSystem.CountLivingFighters(hero, team).Should().Be(2);
        fight.Monster.MaxHP.Should().Be(maxHp, "the god's HP is not recounted");
        fight.Monster.HP.Should().Be(maxHp);
    }

    [Fact]
    public void CountLivingFighters_ThePlayerAndEveryLivingTeammate()
    {
        var hero = OldGodBuild134Support.Hero();
        OldGodBossSystem.CountLivingFighters(hero, null).Should().Be(1);
        OldGodBossSystem.CountLivingFighters(hero, new List<Character>()).Should().Be(1);
        var team = Enumerable.Range(0, 4).Select(_ => OldGodBuild134Support.Hero()).ToList();
        OldGodBossSystem.CountLivingFighters(hero, team).Should().Be(5);
        team[1].HP = 0;
        OldGodBossSystem.CountLivingFighters(hero, team).Should().Be(4, "a fallen teammate is not a fighter");
    }
}
