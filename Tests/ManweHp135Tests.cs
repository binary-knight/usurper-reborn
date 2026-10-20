using System.Linq;
using FluentAssertions;
using UsurperRemake.Data;
using UsurperRemake.Systems;
using Xunit;

namespace UsurperReborn.Tests;

/// <summary>1.3.5: Manwe's HP is 0.6 of 900000, set in the data row alone.</summary>
[Collection("SharedGameSingletons")]
public class ManweHp135Tests
{
    [Fact]
    public void ManweRow_HoldsFiveHundredFortyThousand()
    {
        var manwe = OldGodsData.GetGodBossData(OldGodType.Manwe);
        manwe.HP.Should().Be(540000, "Manwe's HP is 0.6 of 900000");
        manwe.MaxHP.Should().Be(540000, "MaxHP follows HP");
    }

    [Fact]
    public void ManweFight_BuildsFromTheRowThroughFightHP()
    {
        var data = OldGodsData.GetGodBossData(OldGodType.Manwe);
        var hero = OldGodBuild134Support.Hero();
        hero.RecalculateStats();
        var fight = OldGodBossSystem.BuildBossFight(OldGodType.Manwe, 0, BossDialogue.None, hero);
        long expected = (long)(540000 * 1.0f * GameConfig.BaseMonsterDifficultyScale);
        OldGodBossSystem.FightHP(data, 0).Should().Be(expected);
        fight.Monster.MaxHP.Should().Be(OldGodBossSystem.FightHP(data, 0));
        fight.Monster.MaxHP.Should().Be(expected);
    }

    [Fact]
    public void EveryOtherGod_KeepsItsHP()
    {
        var hp = OldGodBuild134Support.Rows().ToDictionary(r => r.Row, r => r.Data.HP);
        hp["Maelketh"].Should().Be(22000);
        hp["Veloura"].Should().Be(120000);
        hp["Thorgrim"].Should().Be(250000);
        hp["Noctura"].Should().Be(380000);
        hp["Aurelion"].Should().Be(500000);
        hp["Terravok"].Should().Be(650000);
        hp["NocturaBetrayal"].Should().Be(300000);
    }
}
