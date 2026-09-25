using System.Text.Json;
using System.Threading.Tasks;
using FluentAssertions;
using UsurperRemake.Systems;
using Xunit;

namespace UsurperReborn.Tests;

/// <summary>v1.1.13: a sales tax set to 0 stays 0; the 5% and 2% defaults are only for a record without the fields.</summary>
public partial class OwnerProcessConflictTests
{
    private static void ChargesNoSalesTax()
    {
        var (kingTax, cityTax, total) = CityControlSystem.CalculateTaxedPrice(10_000);
        kingTax.Should().Be(0);
        cityTax.Should().Be(0);
        total.Should().Be(10_000, "a purchase charges no tax");
    }

    [Fact]
    public async Task ZeroSalesTaxes_StayZero_ThroughTheStoredCourtAndTheNextCourtChange()
    {
        await WithKing("Kim", 1000, async _ => await AsTheWorldSim(async () =>
        {
            var court = JsonSerializer.Deserialize<RoyalCourtSaveData>(Court("Kim", 1000), Json)!;
            court.KingTaxPercent = 0;
            court.CityTaxPercent = 0;
            await _db.SaveWorldState("royal_court", JsonSerializer.Serialize(court, Json));
            await NewOsm(_db).LoadRoyalCourtFromWorldState();
            CastleLocation.GetCurrentKing()!.KingTaxPercent.Should().Be(0);
            CastleLocation.GetCurrentKing()!.CityTaxPercent.Should().Be(0);

            (await CastleLocation.CourtChangeAsync(c => { c.Treasury += 10; return true; })).Should().BeTrue();
            CastleLocation.GetCurrentKing()!.KingTaxPercent.Should().Be(0, "a court change's written copy keeps the 0");
            CastleLocation.GetCurrentKing()!.CityTaxPercent.Should().Be(0);
            (await StoredCourt()).KingTaxPercent.Should().Be(0);
            ChargesNoSalesTax();

            // a record without the fields (an older save) takes the defaults
            await _db.SaveWorldState("royal_court", "{\"kingName\":\"Kim\",\"kingAI\":0,\"treasury\":1000}");
            await NewOsm(_db).LoadRoyalCourtFromWorldState();
            CastleLocation.GetCurrentKing()!.KingTaxPercent.Should().Be(5);
            CastleLocation.GetCurrentKing()!.CityTaxPercent.Should().Be(2);
        }));
    }

    [Fact]
    public async Task ZeroSalesTaxes_StayZero_InSinglePlayer()
    {
        await WithKing("Kim", 1000, async _ =>
        {
            // the tax policy's court change with no store
            (await CastleLocation.CourtChangeAsync(null, c => { c.KingTaxPercent = 0; c.CityTaxPercent = 0; return true; })).Should().BeTrue();
            CastleLocation.GetCurrentKing()!.KingTaxPercent.Should().Be(0);
            CastleLocation.GetCurrentKing()!.CityTaxPercent.Should().Be(0);
            ChargesNoSalesTax();

            // a single-player save that holds 0 and 0 loads as 0 and 0; one without the fields takes the defaults
            SaveSystem.Instance.RestoreStorySystems(JsonSerializer.Deserialize<StorySystemsData>(
                "{\"RoyalCourt\":{\"KingName\":\"Kim\",\"Treasury\":1000,\"KingTaxPercent\":0,\"CityTaxPercent\":0}}")!);
            CastleLocation.GetCurrentKing()!.KingTaxPercent.Should().Be(0);
            CastleLocation.GetCurrentKing()!.CityTaxPercent.Should().Be(0);
            ChargesNoSalesTax();

            SaveSystem.Instance.RestoreStorySystems(JsonSerializer.Deserialize<StorySystemsData>(
                "{\"RoyalCourt\":{\"KingName\":\"Kim\",\"Treasury\":1000}}")!);
            CastleLocation.GetCurrentKing()!.KingTaxPercent.Should().Be(5);
            CastleLocation.GetCurrentKing()!.CityTaxPercent.Should().Be(2);
        });
    }
}
