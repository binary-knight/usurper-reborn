using System;
using System.Linq;
using System.Threading.Tasks;
using FluentAssertions;
using UsurperRemake.Systems;
using Xunit;

namespace UsurperReborn.Tests;

/// <summary>v1.1.13: a guard bonus is bounded before its cost is worked out, so it can never overflow.</summary>
public partial class OwnerProcessConflictTests
{
    [Theory]
    [InlineData(6000000000000000000L)]
    [InlineData(long.MaxValue / 2)]
    [InlineData(long.MaxValue)]
    [InlineData(0L)]
    [InlineData(-5L)]
    public async Task AGuardBonus_ThatWouldOverflowOrIsNotPositive_IsRefused_AndTheTreasuryIsUnchanged(long bonus)
    {
        await WithKing("Kim", 1000, async _ =>
        {
            var dbA = new SqlSaveBackend(_path);
            await dbA.SaveWorldState("royal_court", CourtWith("Kim", 1000, ("Gerald", 50), ("Helena", 60), ("Ivo", 70)));
            var osmA = NewOsm(dbA);
            await osmA.LoadRoyalCourtFromWorldState();
            long version = _db.GetWorldStateVersion("royal_court");

            (await CastleLocation.PayGuardBonusAsync(osmA, bonus)).Should().BeFalse($"a bonus of {bonus} with three guards is refused");

            _db.GetWorldStateVersion("royal_court").Should().Be(version, "nothing was written");
            var stored = await StoredCourt();
            stored.Treasury.Should().Be(1000);
            stored.Guards.Select(g => g.Loyalty).Should().Equal(new[] { 50, 60, 70 });
            CastleLocation.GetCurrentKing()!.Treasury.Should().Be(1000);
        });
    }

    [Fact]
    public void GuardBonusCost_IsBoundedBeforeTheMultiply()
    {
        CastleLocation.GuardBonusCost(6000000000000000000L, 3, 1000).Should().BeNull("the product would overflow to a negative cost");
        CastleLocation.GuardBonusCost(long.MaxValue / 2, 3, long.MaxValue).Should().BeNull("above the cap");
        CastleLocation.GuardBonusCost(CastleLocation.MaxGuardBonus, 3, long.MaxValue).Should().Be(CastleLocation.MaxGuardBonus * 3);
        CastleLocation.GuardBonusCost(200, 3, 600).Should().Be(600);
        CastleLocation.GuardBonusCost(201, 3, 600).Should().BeNull("more than the treasury");
        CastleLocation.GuardBonusCost(200, 0, 600).Should().BeNull("no guards");
    }

    [Fact]
    public async Task AGuardBonus_WithinBounds_StillPaysAndRaisesLoyalty()
    {
        await WithKing("Kim", 1000, async _ =>
        {
            var dbA = new SqlSaveBackend(_path);
            await dbA.SaveWorldState("royal_court", CourtWith("Kim", 1000, ("Gerald", 50), ("Helena", 60), ("Ivo", 70)));
            var osmA = NewOsm(dbA);
            await osmA.LoadRoyalCourtFromWorldState();

            (await CastleLocation.PayGuardBonusAsync(osmA, 300)).Should().BeTrue();
            var stored = await StoredCourt();
            stored.Treasury.Should().Be(100);
            stored.Guards.Select(g => g.Loyalty).Should().Equal(new[] { 53, 63, 73 });
        });
    }
}
