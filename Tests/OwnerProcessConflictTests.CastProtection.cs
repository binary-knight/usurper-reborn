using System;
using System.Linq;
using System.Threading.Tasks;
using FluentAssertions;
using UsurperRemake.Systems;
using Xunit;

namespace UsurperReborn.Tests;

/// <summary>v1.1.13: Cast Protection's cost and its loyalty benefit are one court change.</summary>
public partial class OwnerProcessConflictTests
{
    [Fact]
    public async Task TwoProtectionCasts_KeepBothTheCostAndTheLoyalty_ThroughADepositAConflictAndAReload()
    {
        await WithKing("Kim", 1000, async _ =>
        {
            var dbA = new SqlSaveBackend(_path);
            await dbA.SaveWorldState("royal_court", CourtWith("Kim", 1000, ("Gerald", 50)));
            var osmA = NewOsm(dbA);
            await osmA.LoadRoyalCourtFromWorldState();
            long budget = (await StoredCourt()).MagicBudget;

            (await CastleLocation.CastProtectionAsync(osmA)).Should().BeTrue();
            (await CastleLocation.CastProtectionAsync(osmA)).Should().BeTrue();

            var stored = await StoredCourt();
            stored.MagicBudget.Should().Be(budget - 4000, "two casts cost 4,000");
            stored.Guards.Single().Loyalty.Should().Be(70, "and raise loyalty 50 -> 70");
            CastleLocation.GetCurrentKing()!.Guards.Single().Loyalty.Should().Be(70);

            // a following deposit, whose first write meets another process's income
            var player = new Character { Name2 = "Pat", Gold = 500 };
            int writes = 0;
            (await CastleLocation.MoveTreasuryGoldAsync(osmA, player, -100, async () =>
            {
                if (writes++ == 0)
                {
                    var other = await StoredCourt();
                    other.Treasury += 50;
                    (await _db.SaveWorldStateIfVersion("royal_court", System.Text.Json.JsonSerializer.Serialize(other, Json),
                        _db.GetWorldStateVersion("royal_court"))).Should().BeTrue();
                }
            })).Should().BeTrue();

            stored = await StoredCourt();
            stored.Treasury.Should().Be(1150);
            stored.Guards.Single().Loyalty.Should().Be(70, "the deposit keeps the loyalty the casts bought");
            stored.MagicBudget.Should().Be(budget - 4000);
            CastleLocation.GetCurrentKing()!.Guards.Single().Loyalty.Should().Be(70);

            await osmA.LoadRoyalCourtFromWorldState();
            CastleLocation.GetCurrentKing()!.Guards.Single().Loyalty.Should().Be(70, "a reload keeps it too");
        });
    }

    [Fact]
    public async Task AProtectionCast_WithoutTheBudget_ChangesNothing()
    {
        await WithKing("Kim", 1000, async _ =>
        {
            var dbA = new SqlSaveBackend(_path);
            var court = System.Text.Json.JsonSerializer.Deserialize<RoyalCourtSaveData>(CourtWith("Kim", 1000, ("Gerald", 50)), Json)!;
            court.MagicBudget = 1999;
            await dbA.SaveWorldState("royal_court", System.Text.Json.JsonSerializer.Serialize(court, Json));
            var osmA = NewOsm(dbA);
            await osmA.LoadRoyalCourtFromWorldState();

            (await CastleLocation.CastProtectionAsync(osmA)).Should().BeFalse();
            var stored = await StoredCourt();
            stored.MagicBudget.Should().Be(1999);
            stored.Guards.Single().Loyalty.Should().Be(50);
        });
    }
}
