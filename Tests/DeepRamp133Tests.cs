using System.Linq;
using System.Reflection;
using FluentAssertions;
using UsurperRemake.Data;
using UsurperRemake.Systems;
using Xunit;

namespace UsurperReborn.Tests;

/// <summary>
/// 1.3.3: from floor 25 dungeon monsters grow stronger with depth through two ramps (HP; Strength, punch and weapon
/// power), 1.0 up to level 25 and never decreasing, so the deep floors from 25 are built for a full team of five.
/// The Old Gods and the world bosses are built elsewhere and do not change.
/// </summary>
public class DeepRamp133Tests
{
    [Fact]
    public void BothRamps_AreOne_UpToLevel25_AndNeverDecrease()
    {
        for (int level = 1; level <= 25; level++)
        {
            MonsterGenerator.DeepMonsterHpFactor(level).Should().Be(1.0, $"level {level}");
            MonsterGenerator.DeepMonsterDamageFactor(level).Should().Be(1.0, $"level {level}");
        }
        for (int level = 26; level <= 100; level++)
        {
            MonsterGenerator.DeepMonsterHpFactor(level).Should().BeGreaterThanOrEqualTo(MonsterGenerator.DeepMonsterHpFactor(level - 1), $"level {level}");
            MonsterGenerator.DeepMonsterDamageFactor(level).Should().BeGreaterThanOrEqualTo(MonsterGenerator.DeepMonsterDamageFactor(level - 1), $"level {level}");
        }
    }

    [Theory]
    [InlineData(30, 1.22, 1.22)]
    [InlineData(35, 1.44, 1.44)]
    [InlineData(42, 1.517, 1.517)]
    [InlineData(50, 1.605, 1.605)]
    [InlineData(75, 1.6325, 1.715)]
    [InlineData(87, 1.6457, 2.089)]   // 1.3.7: damage was 1.8998
    [InlineData(100, 1.66, 2.31)]     // 1.3.7: damage was 2.1
    public void Ramps_FollowTheirWaypoints(int level, double hp, double damage)
    {
        MonsterGenerator.DeepMonsterHpFactor(level).Should().BeApproximately(hp, 1e-9);
        MonsterGenerator.DeepMonsterDamageFactor(level).Should().BeApproximately(damage, 1e-9);
    }

    // GetWikiStats (power 1.0, Normal, no sysop multiplier): HP, Strength, Defence, Punch, WeaponPower, ArmorPower
    [Theory]
    [InlineData(1, 73, 3, 1, 1, 2, 0)]
    [InlineData(20, 3478, 166, 67, 67, 119, 24)]
    [InlineData(25, 4416, 211, 85, 85, 148, 31)]
    public void MonsterStats_UpToLevel25_AreUnchanged(int level, long hp, long str, long def, long punch, long wp, long ap)
        => MonsterGenerator.GetWikiStats(level, 1.0f).Should().Be((hp, str, def, punch, wp, ap));

    [Theory]
    [InlineData(30, 6554, 308, 103, 126, 218, 36)]
    [InlineData(42, 11706, 542, 144, 218, 384, 51)]
    [InlineData(60, 18328, 852, 207, 339, 603, 76)]
    [InlineData(80, 25362, 1368, 276, 544, 969, 103)]    // 1.3.7: was str 1244, punch 495, weapon 882
    [InlineData(100, 32744, 2016, 346, 798, 1428, 128)]  // 1.3.7: was str 1831, punch 726, weapon 1298
    public void MonsterStats_FromLevel26_FollowTheRamps_DefenceAndArmourUnchanged(int level, long hp, long str, long def, long punch, long wp, long ap)
        => MonsterGenerator.GetWikiStats(level, 1.0f).Should().Be((hp, str, def, punch, wp, ap));

    // name, MaxHP, Strength, Defence, WeapPow, ArmPow, as built before 1.3.3's ramp
    private static readonly (string Name, long Hp, long Str, long Def, long Wp, long Ap)[] OldGodsBefore =
    {
        ("Maelketh, The Broken Blade", 49500, 298, 202, 298, 202),
        ("Veloura, The Withered Heart", 270000, 450, 247, 450, 247),
        ("Thorgrim, The Hollow Judge", 562500, 956, 506, 956, 506),
        ("Noctura, The Shadow Weaver", 855000, 1125, 360, 1125, 360),
        ("Aurelion, The Fading Light", 1125000, 1293, 337, 1293, 337),
        ("Terravok, The Sleeping Mountain", 1462500, 1462, 427, 1462, 427),
        // 1.3.5 set his HP to 0.6 of 900000 (540000 x 2.25)
        ("Manwe, The Weary Creator", 1215000, 1912, 562, 1912, 562),
        ("Noctura, The Shadow Ascendant", 675000, 270, 202, 270, 202),
    };

    private static readonly (string Name, long Hp, long Str, long Def, long Wp, long Ap)[] WorldBossesBefore =
    {
        ("The Abyssal Leviathan", 1, 0, 0, 0, 0),
        ("Void Colossus", 1, 0, 0, 0, 0),
        ("Shadowlord Malachar", 1, 0, 0, 0, 0),
        ("The Crimson Wyrm", 1, 0, 0, 0, 0),
        ("Lich King Vareth", 1, 0, 0, 0, 0),
        ("The Iron Titan", 1, 0, 0, 0, 0),
        ("Dread Serpent Nidhogg", 1, 0, 0, 0, 0),
        ("The Nameless Horror", 1, 0, 0, 0, 0),
    };

    [Fact]
    public void OldGods_AreBuiltAsBefore()
    {
        var create = typeof(OldGodBossSystem).GetMethod("CreateBossMonster", BindingFlags.NonPublic | BindingFlags.Instance)!;
        var now = OldGodsData.GetAllOldGodsWithVariants()
            .Select(g => (Monster)create.Invoke(OldGodBossSystem.Instance, new object[] { g })!)
            .Select(m => (m.Name, m.MaxHP, m.Strength, (long)m.Defence, m.WeapPow, m.ArmPow)).ToArray();
        now.Should().Equal(OldGodsBefore);
    }

    [Fact]
    public void WorldBosses_AreBuiltAsBefore()
    {
        var now = WorldBossDatabase.GetAllBosses().Select(WorldBossSystem.AsFoe)
            .Select(m => (m.Name, m.MaxHP, m.Strength, (long)m.Defence, m.WeapPow, m.ArmPow)).ToArray();
        now.Should().Equal(WorldBossesBefore);
    }
}
