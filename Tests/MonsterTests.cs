using Xunit;
using FluentAssertions;

namespace UsurperReborn.Tests;

/// <summary>
/// Unit tests for Monster class
/// Tests IsBoss/IsMiniBoss bonuses for attack, defense, XP, and gold rewards
/// </summary>
public class MonsterTests
{
    #region IsBoss/IsMiniBoss Flag Tests

    [Fact]
    public void Monster_DefaultFlags_AreFalse()
    {
        var monster = CreateTestMonster();

        monster.IsBoss.Should().BeFalse();
        monster.IsMiniBoss.Should().BeFalse();
        monster.IsUnique.Should().BeFalse();
    }

    [Fact]
    public void Monster_IsBoss_CanBeSetTrue()
    {
        var monster = CreateTestMonster();
        monster.IsBoss = true;

        monster.IsBoss.Should().BeTrue();
        monster.IsMiniBoss.Should().BeFalse("IsBoss and IsMiniBoss are mutually exclusive");
    }

    [Fact]
    public void Monster_IsMiniBoss_CanBeSetTrue()
    {
        var monster = CreateTestMonster();
        monster.IsMiniBoss = true;

        monster.IsMiniBoss.Should().BeTrue();
        monster.IsBoss.Should().BeFalse("IsBoss and IsMiniBoss are mutually exclusive");
    }

    #endregion

    #region Attack Power Bonus Tests

    [Fact]
    public void GetAttackPower_Boss_Gets30PercentBonus()
    {
        var normalMonster = CreateTestMonster(strength: 100, weappow: 0, punch: 0);
        var bossMonster = CreateTestMonster(strength: 100, weappow: 0, punch: 0);
        bossMonster.IsBoss = true;

        var normalAttack = normalMonster.GetAttackPower();
        var bossAttack = bossMonster.GetAttackPower();

        bossAttack.Should().Be((long)(normalAttack * 1.3f),
            "Bosses should get 30% attack bonus");
    }

    [Fact]
    public void GetAttackPower_MiniBoss_Gets15PercentBonus()
    {
        // v0.42.2: IsMiniBoss 1.15x multiplier removed from GetAttackPower() to fix
        // double-dipping — mini-boss stats are already boosted 1.5x during generation.
        // GetAttackPower() now returns the same value for mini-bosses and normal monsters.
        var normalMonster = CreateTestMonster(strength: 100, weappow: 0, punch: 0);
        var miniBossMonster = CreateTestMonster(strength: 100, weappow: 0, punch: 0);
        miniBossMonster.IsMiniBoss = true;

        var normalAttack = normalMonster.GetAttackPower();
        var miniBossAttack = miniBossMonster.GetAttackPower();

        miniBossAttack.Should().Be(normalAttack,
            "Mini-boss attack bonus comes from stat generation, not GetAttackPower()");
    }

    [Fact]
    public void GetAttackPower_Boss_StrongerThanMiniBoss()
    {
        var bossMonster = CreateTestMonster(strength: 100, weappow: 0, punch: 0);
        bossMonster.IsBoss = true;

        var miniBossMonster = CreateTestMonster(strength: 100, weappow: 0, punch: 0);
        miniBossMonster.IsMiniBoss = true;

        var bossAttack = bossMonster.GetAttackPower();
        var miniBossAttack = miniBossMonster.GetAttackPower();

        bossAttack.Should().BeGreaterThan(miniBossAttack,
            "Bosses should have higher attack bonus than mini-bosses");
    }

    #endregion

    #region Defense Power Bonus Tests

    [Fact]
    public void GetDefensePower_Boss_Gets20PercentBonus()
    {
        var normalMonster = CreateTestMonster(defence: 100, armpow: 0);
        var bossMonster = CreateTestMonster(defence: 100, armpow: 0);
        bossMonster.IsBoss = true;

        var normalDefense = normalMonster.GetDefensePower();
        var bossDefense = bossMonster.GetDefensePower();

        bossDefense.Should().Be((long)(normalDefense * 1.2f),
            "Bosses should get 20% defense bonus");
    }

    [Fact]
    public void GetDefensePower_MiniBoss_Gets10PercentBonus()
    {
        var normalMonster = CreateTestMonster(defence: 100, armpow: 0);
        var miniBossMonster = CreateTestMonster(defence: 100, armpow: 0);
        miniBossMonster.IsMiniBoss = true;

        var normalDefense = normalMonster.GetDefensePower();
        var miniBossDefense = miniBossMonster.GetDefensePower();

        miniBossDefense.Should().Be((long)(normalDefense * 1.1f),
            "Mini-bosses should get 10% defense bonus");
    }

    [Fact]
    public void GetDefensePower_Boss_StrongerThanMiniBoss()
    {
        var bossMonster = CreateTestMonster(defence: 100, armpow: 0);
        bossMonster.IsBoss = true;

        var miniBossMonster = CreateTestMonster(defence: 100, armpow: 0);
        miniBossMonster.IsMiniBoss = true;

        var bossDefense = bossMonster.GetDefensePower();
        var miniBossDefense = miniBossMonster.GetDefensePower();

        bossDefense.Should().BeGreaterThan(miniBossDefense,
            "Bosses should have higher defense bonus than mini-bosses");
    }

    #endregion

    #region XP Reward Bonus Tests

    [Fact]
    public void GetExperienceReward_Boss_Gets3xMultiplier()
    {
        var normalMonster = CreateTestMonster(level: 50);
        var bossMonster = CreateTestMonster(level: 50);
        bossMonster.IsBoss = true;

        var normalXP = normalMonster.GetExperienceReward();
        var bossXP = bossMonster.GetExperienceReward();

        // Boss XP should be approximately 3x normal (allowing for small rounding differences)
        bossXP.Should().BeGreaterThanOrEqualTo(normalXP * 2,
            "Boss XP should be at least 2x normal (targeting 3x)");
        bossXP.Should().BeLessThanOrEqualTo(normalXP * 4,
            "Boss XP should be at most 4x normal (targeting 3x)");
    }

    [Fact]
    public void GetExperienceReward_MiniBoss_Gets1_5xMultiplier()
    {
        var normalMonster = CreateTestMonster(level: 50);
        var miniBossMonster = CreateTestMonster(level: 50);
        miniBossMonster.IsMiniBoss = true;

        var normalXP = normalMonster.GetExperienceReward();
        var miniBossXP = miniBossMonster.GetExperienceReward();

        // Mini-boss XP should be approximately 1.5x normal
        miniBossXP.Should().BeGreaterThanOrEqualTo((long)(normalXP * 1.3),
            "Mini-boss XP should be at least 1.3x normal (targeting 1.5x)");
        miniBossXP.Should().BeLessThanOrEqualTo((long)(normalXP * 1.7),
            "Mini-boss XP should be at most 1.7x normal (targeting 1.5x)");
    }

    [Fact]
    public void GetExperienceReward_Boss_MoreThanMiniBoss()
    {
        var bossMonster = CreateTestMonster(level: 50);
        bossMonster.IsBoss = true;

        var miniBossMonster = CreateTestMonster(level: 50);
        miniBossMonster.IsMiniBoss = true;

        var bossXP = bossMonster.GetExperienceReward();
        var miniBossXP = miniBossMonster.GetExperienceReward();

        bossXP.Should().BeGreaterThan(miniBossXP,
            "Bosses should give more XP than mini-bosses");
    }

    #endregion

    #region Gold Reward Bonus Tests

    [Fact]
    public void GetGoldReward_Boss_Gets3xMultiplier()
    {
        // Run multiple times to average out random component
        long totalNormalGold = 0;
        long totalBossGold = 0;
        int iterations = 100;

        for (int i = 0; i < iterations; i++)
        {
            var normalMonster = CreateTestMonster(level: 50);
            var bossMonster = CreateTestMonster(level: 50);
            bossMonster.IsBoss = true;

            totalNormalGold += normalMonster.GetGoldReward();
            totalBossGold += bossMonster.GetGoldReward();
        }

        var avgNormalGold = totalNormalGold / iterations;
        var avgBossGold = totalBossGold / iterations;

        // Boss gold should be approximately 3x normal
        avgBossGold.Should().BeGreaterThan(avgNormalGold * 2,
            "Boss gold should be greater than 2x normal (targeting 3x)");
    }

    [Fact]
    public void GetGoldReward_MiniBoss_Gets1_5xMultiplier()
    {
        // Run multiple times to average out random component
        long totalNormalGold = 0;
        long totalMiniBossGold = 0;
        int iterations = 100;

        for (int i = 0; i < iterations; i++)
        {
            var normalMonster = CreateTestMonster(level: 50);
            var miniBossMonster = CreateTestMonster(level: 50);
            miniBossMonster.IsMiniBoss = true;

            totalNormalGold += normalMonster.GetGoldReward();
            totalMiniBossGold += miniBossMonster.GetGoldReward();
        }

        var avgNormalGold = totalNormalGold / iterations;
        var avgMiniBossGold = totalMiniBossGold / iterations;

        // Mini-boss gold should be approximately 1.5x normal
        avgMiniBossGold.Should().BeGreaterThan((long)(avgNormalGold * 1.2),
            "Mini-boss gold should be greater than 1.2x normal (targeting 1.5x)");
    }

    [Fact]
    public void GetGoldReward_Boss_MoreThanMiniBoss()
    {
        // Run multiple times to average out random component
        long totalBossGold = 0;
        long totalMiniBossGold = 0;
        int iterations = 100;

        for (int i = 0; i < iterations; i++)
        {
            var bossMonster = CreateTestMonster(level: 50);
            bossMonster.IsBoss = true;

            var miniBossMonster = CreateTestMonster(level: 50);
            miniBossMonster.IsMiniBoss = true;

            totalBossGold += bossMonster.GetGoldReward();
            totalMiniBossGold += miniBossMonster.GetGoldReward();
        }

        var avgBossGold = totalBossGold / iterations;
        var avgMiniBossGold = totalMiniBossGold / iterations;

        avgBossGold.Should().BeGreaterThan(avgMiniBossGold,
            "Bosses should give more gold than mini-bosses");
    }

    #endregion

    #region Display Info Tests

    [Fact]
    public void GetDisplayInfo_Boss_ShowsBossTag()
    {
        var monster = CreateTestMonster();
        monster.IsBoss = true;

        var displayInfo = monster.GetDisplayInfo();

        displayInfo.Should().Contain("[BOSS]");
        displayInfo.Should().NotContain("[CHAMPION]");
    }

    [Fact]
    public void GetDisplayInfo_MiniBoss_ShowsChampionTag()
    {
        var monster = CreateTestMonster();
        monster.IsMiniBoss = true;

        var displayInfo = monster.GetDisplayInfo();

        displayInfo.Should().Contain("[CHAMPION]");
        displayInfo.Should().NotContain("[BOSS]");
    }

    [Fact]
    public void GetDisplayInfo_Normal_NoSpecialTags()
    {
        var monster = CreateTestMonster();

        var displayInfo = monster.GetDisplayInfo();

        displayInfo.Should().NotContain("[BOSS]");
        displayInfo.Should().NotContain("[CHAMPION]");
    }

    #endregion

    #region Non-Boss-Room Monsters Should Use IsMiniBoss

    [Fact]
    public void GeneratedMonster_WithBossInName_ShouldBeMiniBoss_NotBoss()
    {
        // This tests that monsters with "Boss" or "Lord" in their name
        // are marked as IsMiniBoss, not IsBoss (since IsBoss is reserved for floor bosses)
        var monster = Monster.CreateMonster(
            nr: 50,
            name: "Goblin Boss",
            hps: 100,
            strength: 50,
            defence: 30,
            phrase: "Attack!",
            grabweap: false,
            grabarm: false,
            weapon: "Sword",
            armor: "Leather",
            poisoned: false,
            disease: false,
            punch: 10,
            armpow: 5,
            weappow: 10
        );

        monster.IsMiniBoss.Should().BeTrue(
            "Monsters with 'Boss' in name should be IsMiniBoss (not IsBoss, which is for floor bosses)");
        monster.IsBoss.Should().BeFalse(
            "IsBoss should only be set for actual floor bosses in boss rooms");
    }

    [Fact]
    public void GeneratedMonster_WithLordInName_ShouldBeMiniBoss_NotBoss()
    {
        var monster = Monster.CreateMonster(
            nr: 50,
            name: "Demon Lord",
            hps: 100,
            strength: 50,
            defence: 30,
            phrase: "Attack!",
            grabweap: false,
            grabarm: false,
            weapon: "Sword",
            armor: "Leather",
            poisoned: false,
            disease: false,
            punch: 10,
            armpow: 5,
            weappow: 10
        );

        monster.IsMiniBoss.Should().BeTrue(
            "Monsters with 'Lord' in name should be IsMiniBoss");
        monster.IsBoss.Should().BeFalse(
            "IsBoss should only be set for actual floor bosses");
    }

    [Fact]
    public void GeneratedMonster_DeathKnight_ShouldBeUnique_AndMiniBoss()
    {
        var monster = Monster.CreateMonster(
            nr: 50,
            name: "Death Knight",
            hps: 100,
            strength: 50,
            defence: 30,
            phrase: "Attack!",
            grabweap: false,
            grabarm: false,
            weapon: "Sword",
            armor: "Plate",
            poisoned: false,
            disease: false,
            punch: 10,
            armpow: 5,
            weappow: 10
        );

        monster.IsUnique.Should().BeTrue(
            "Death Knight should be marked as Unique");
        monster.IsMiniBoss.Should().BeTrue(
            "Death Knight should be IsMiniBoss (not IsBoss)");
        monster.IsBoss.Should().BeFalse(
            "IsBoss should only be set for actual floor bosses");
    }

    #endregion

    #region Helper Methods

    /// <summary>
    /// Create a test monster with specified stats
    /// </summary>
    private Monster CreateTestMonster(
        int level = 50,
        int strength = 50,
        int defence = 30,
        int weappow = 10,
        int armpow = 5,
        int punch = 10)
    {
        return Monster.CreateMonster(
            nr: level,
            name: "Test Monster",
            hps: 100,
            strength: strength,
            defence: defence,
            phrase: "Test!",
            grabweap: false,
            grabarm: false,
            weapon: "Sword",
            armor: "Leather",
            poisoned: false,
            disease: false,
            punch: punch,
            armpow: armpow,
            weappow: weappow
        );
    }

    #endregion
}
