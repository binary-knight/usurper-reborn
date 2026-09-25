using System;
using System.Linq;
using System.Threading.Tasks;
using FluentAssertions;
using UsurperRemake.Systems;
using Xunit;

namespace UsurperReborn.Tests;

/// <summary>v1.1.13: a sale's tax reaches the treasury once, at the sale; the daily reset only reports it.</summary>
public partial class OwnerProcessConflictTests
{
    [Fact]
    public async Task ASalesTax_ThenTheDailyReset_CreditsTheTaxExactlyOnce()
    {
        await WithKing("Kim", 100000, async _ =>
        {
            bool online = (bool)OnlineModeField.GetValue(null)!;
            OnlineModeField.SetValue(null, true);
            try
            {
                await _db.SaveWorldState("royal_court", CourtWith("Kim", 100000, ("Gerald", 60)));   // a full magic budget: no top-up
                var dbA = new SqlSaveBackend(_path);
                var osmA = NewOsm(dbA);
                FallbackInstance.SetValue(null, osmA);   // this process's session
                await osmA.LoadRoyalCourtFromWorldState();
                var king = CastleLocation.GetCurrentKing()!;
                king.KingTaxPercent.Should().Be(5);
                king.DailyTaxRevenue = 0;

                var court = await StoredCourt();
                long net = King.DailyIncomeOf(court, 0) - King.DailyExpensesOf(court);

                // a 10,000 gold sale: the king's 5% is 500
                CityControlSystem.Instance.ProcessSaleTax(10000);
                for (int i = 0; i < 100 && (await StoredCourt()).Treasury == 100000; i++) await Task.Delay(20);
                (await StoredCourt()).Treasury.Should().Be(100500, "the sale pays its tax into the stored treasury at once");
                CastleLocation.GetCurrentKing()!.DailyTaxRevenue.Should().Be(500, "and the day's takings are counted for the reports");

                (await King.ProcessDailyActivitiesAsync(dbA)).Should().BeTrue();

                var stored = await StoredCourt();
                (stored.Treasury - 100000 - net).Should().Be(500, "the 500 is credited exactly once across the sale and the reset");
                CastleLocation.GetCurrentKing()!.Treasury.Should().Be(stored.Treasury);
                CastleLocation.GetCurrentKing()!.DailyTaxRevenue.Should().Be(0, "the next day's count starts at zero");
            }
            finally { OnlineModeField.SetValue(null, online); }
        });
    }
}
