using System;
using System.IO;
using System.Linq;
using System.Reflection;
using FluentAssertions;
using UsurperRemake.Data;
using UsurperRemake.Systems;
using Xunit;

namespace UsurperReborn.Tests;

/// <summary>
/// 1.3.4: Doom starts at Aurelion, as the Old God design says. The phase 3 Doom roll fires whenever the
/// context's DoomRounds is above 0 and the context's default is 3, so every god before Aurelion and
/// Noctura's betrayal fight set 0; Aurelion and Terravok set 3, Manwe 2.
/// </summary>
[Collection("SharedGameSingletons")]
public class DoomFix134Tests
{
    private static int DoomRoundsOf(string row)
    {
        var data = OldGodBuild134Support.Rows().Single(r => r.Row == row).Data;
        var hero = OldGodBuild134Support.Hero();
        hero.RecalculateStats();
        return OldGodBossSystem.BuildBossFight(data, 0, BossDialogue.None, hero).Context.DoomRounds;
    }

    [Theory]
    [InlineData("Maelketh", 0)]
    [InlineData("Veloura", 0)]
    [InlineData("Thorgrim", 0)]
    [InlineData("Noctura", 0)]
    [InlineData("Aurelion", 3)]
    [InlineData("Terravok", 3)]
    [InlineData("Manwe", 2)]
    [InlineData("NocturaBetrayal", 0)]
    public void EachBossRow_BuildsItsDesignedDoomRounds(string row, int expected)
    {
        DoomRoundsOf(row).Should().Be(expected, $"{row}: Doom starts at Aurelion");
    }

    [Fact]
    public void TheContextDefault_StaysThree()
    {
        // the fix is in the gods' setup, not the engine's default
        new BossCombatContext().DoomRounds.Should().Be(3);
    }

    [Fact]
    public void TheBetrayalFightContext_HasNoDoom()
    {
        var ctx = OldGodBossSystem.BetrayalContext(OldGodsData.GetNocturaBetrayal());
        ctx.DoomRounds.Should().Be(0, "the betrayal fight is Noctura's, before Aurelion");
        ctx.GodType.Should().Be(OldGodType.Noctura);
        ctx.AttacksPerRound.Should().Be(1);
        ctx.CanSave.Should().BeFalse();
    }

    [Fact]
    public void HandleNocturaBetrayal_FightsWithTheBetrayalContext()
    {
        var src = File.ReadAllText(Path.Combine(OldGodBuild134Support.Root(), "Scripts", "Systems", "OldGodBossSystem.cs"));
        var start = src.IndexOf("private async Task<bool> HandleNocturaBetrayal(", StringComparison.Ordinal);
        start.Should().BeGreaterThan(0);
        var end = src.IndexOf("internal static BossCombatContext BetrayalContext(", start, StringComparison.Ordinal);
        end.Should().BeGreaterThan(start, "the betrayal context sits after the betrayal fight");
        var body = src[start..end];
        body.Should().Contain("combatEngine.BossContext = BetrayalContext(betrayalData);");
        body.Should().NotContain("new BossCombatContext");
    }

    [Theory]
    [InlineData(OldGodType.Maelketh, 0)]
    [InlineData(OldGodType.Veloura, 0)]
    [InlineData(OldGodType.Thorgrim, 0)]
    [InlineData(OldGodType.Noctura, 0)]
    [InlineData(OldGodType.Aurelion, 3)]
    [InlineData(OldGodType.Terravok, 3)]
    [InlineData(OldGodType.Manwe, 2)]
    public void TheWikiExportPath_SetsTheSameDoomRounds(OldGodType god, int expected)
    {
        // the export reads a fresh context through the instance ConfigureBossPartyMechanics
        var ctx = new BossCombatContext();
        var sys = (OldGodBossSystem)System.Runtime.CompilerServices.RuntimeHelpers.GetUninitializedObject(typeof(OldGodBossSystem));
        typeof(OldGodBossSystem).GetMethod("ConfigureBossPartyMechanics", BindingFlags.NonPublic | BindingFlags.Instance)!
            .Invoke(sys, new object[] { ctx, god });
        ctx.DoomRounds.Should().Be(expected, god.ToString());
    }
}
