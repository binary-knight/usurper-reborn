using System.Text.Json;
using System.Threading.Tasks;
using FluentAssertions;
using UsurperRemake.Systems;
using Xunit;

namespace UsurperReborn.Tests;

/// <summary>v1.1.13: a landed coronation is the in-memory court at once, before any other court change runs.</summary>
public partial class OwnerProcessConflictTests
{
    private static RoyalCourtSaveData CrownedCourt(string king) =>
        new() { KingName = king, KingAI = (int)CharacterAI.Computer, Treasury = 777, TaxRate = 7 };

    [Fact]
    public async Task ALandedCoronation_IsTheInMemoryCourt_AtTheWrittenVersion_BeforeAnyOtherChange()
    {
        await WithKing("Aldric", 1000, async _ =>
        {
            await _db.SaveWorldState("royal_court", NpcCourt("Aldric", 1000));
            long read = _db.GetWorldStateVersion("royal_court");

            (await OnlineStateManager.CrownAsync(_db, stored => stored?.KingName == "Aldric" ? CrownedCourt("Brenna") : null)).Should().BeTrue();

            var king = CastleLocation.GetCurrentKing()!;
            king.Name.Should().Be("Brenna", "the written court is installed by the coronation itself");
            king.Treasury.Should().Be(777);
            OnlineStateManager.RoyalCourtVersion.Should().Be(read + 1, "the in-memory court is at the version it was written as");
            _db.GetWorldStateVersion("royal_court").Should().Be(read + 1);
        });
    }

    [Fact]
    public async Task ALandedCoronation_WithoutAStore_IsTheInMemoryCourt_AtOnce()
    {
        await WithKing("Aldric", 1000, async _ =>
        {
            (await OnlineStateManager.CrownAsync(null, stored => stored?.KingName == "Aldric" ? CrownedCourt("Brenna") : null)).Should().BeTrue();

            var king = CastleLocation.GetCurrentKing()!;
            king.Name.Should().Be("Brenna");
            king.Treasury.Should().Be(777);
            OnlineStateManager.RoyalCourtVersion.Should().BeNull("no store: the in-memory court has no stored version");
        });
    }
}
