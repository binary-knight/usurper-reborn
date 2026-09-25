using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Threading.Tasks;
using FluentAssertions;
using Microsoft.Data.Sqlite;
using UsurperRemake.Systems;
using Xunit;

namespace UsurperReborn.Tests;

/// <summary>v1.1.14: the maintainer's decisions on the 1.1.13 leftovers (court, teams, world edits, shared records).</summary>
public partial class OwnerProcessConflictTests
{
    // ─── C6: sales tax that gives up is carried into the next court change that lands ───

    [Fact]
    public async Task SalesTaxThatGivesUp_IsCreditedByTheNextCourtChange_ExactlyOnce()
    {
        await WithKing("Kim", 1000, async _ =>
        {
            OnlineStateManager.PendingSalesTax = 0;
            try
            {
                var dbA = new SqlSaveBackend(_path);
                await dbA.SaveWorldState("royal_court", Court("Kim", 1000));
                var osmA = NewOsm(dbA);
                await osmA.LoadRoyalCourtFromWorldState();

                // every write of the tax meets another process's write first: it gives up
                long b = 1000;
                (await CityControlSystem.AddSalesTaxAsync(50, osmA, () => OtherCourtWrite("Kim", b += 10))).Should().BeFalse();
                OnlineStateManager.PendingSalesTax.Should().Be(50, "the buyer paid; the share waits for the next court change");
                (await StoredCourt()).Treasury.Should().Be(b);

                // the next court change meets one conflict and is retried: the carried share is added once
                var player = new Character { Name2 = "Pat", Gold = 500 };
                int writes = 0;
                (await CastleLocation.MoveTreasuryGoldAsync(osmA, player, -100, async () =>
                {
                    if (writes++ == 0) await OtherCourtWrite("Kim", b);
                })).Should().BeTrue();
                writes.Should().Be(2);
                (await StoredCourt()).Treasury.Should().Be(b + 100 + 50, "the deposit and the carried tax, each once");
                OnlineStateManager.PendingSalesTax.Should().Be(0);

                // and the change after it adds nothing more
                (await CastleLocation.MoveTreasuryGoldAsync(osmA, player, -100)).Should().BeTrue();
                (await StoredCourt()).Treasury.Should().Be(b + 200 + 50, "no second credit");
                CastleLocation.GetCurrentKing()!.Treasury.Should().Be(b + 250);

                // a refused change keeps the carried share for the one after
                OnlineStateManager.CarrySalesTax(30);
                (await CastleLocation.MoveTreasuryGoldAsync(osmA, player, 1_000_000)).Should().BeFalse();
                OnlineStateManager.PendingSalesTax.Should().Be(30);
            }
            finally { OnlineStateManager.PendingSalesTax = 0; }
        });
    }
}
