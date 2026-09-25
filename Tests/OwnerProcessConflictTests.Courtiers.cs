using System.Linq;
using System.Reflection;
using System.Text.Json;
using System.Threading.Tasks;
using FluentAssertions;
using UsurperRemake.Systems;
using Xunit;

namespace UsurperReborn.Tests;

/// <summary>v1.1.13: dismissing or promoting a courtier changes the selected entry, never a namesake.</summary>
public partial class OwnerProcessConflictTests
{
    private string CourtWithNamesakes() =>
        JsonSerializer.Serialize(new RoyalCourtSaveData
        {
            KingName = "Kim", KingAI = (int)CharacterAI.Human, Treasury = 5000, TaxRate = 7,
            CourtMembers =
            {
                new CourtMemberSaveData { Name = "Lord Blackwood", Role = "Chancellor", Faction = (int)CourtFaction.Loyalists, Influence = 50, LoyaltyToKing = 60 },
                new CourtMemberSaveData { Name = "Lord Blackwood", Role = "Treasurer", Faction = (int)CourtFaction.Merchants, Influence = 40, LoyaltyToKing = 50 },
                new CourtMemberSaveData { Name = "Lady Ashe", Role = "Advisor", Faction = (int)CourtFaction.Reformists, Influence = 30, LoyaltyToKing = 70 },
            }
        }, Json);

    private async Task CourtAction(string method, string answer)
    {
        var castle = Castle(PlayerKing("Kim"), new[] { answer });
        await (Task)typeof(CastleLocation).GetMethod(method, BindingFlags.NonPublic | BindingFlags.Instance)!.Invoke(castle, null)!;
    }

    [Fact]
    public async Task DismissingOneOfTwoNamesakes_RemovesOnlyTheSelectedOne()
    {
        await WithKing("Kim", 5000, async _ => await AsTheWorldSim(async () =>
        {
            await _db.SaveWorldState("royal_court", CourtWithNamesakes());
            await NewOsm(_db).LoadRoyalCourtFromWorldState();

            await CourtAction("CourtAction_Dismiss", "2");   // the Treasurer

            var members = (await StoredCourt()).CourtMembers;
            members.Select(m => (m.Name, m.Role)).Should().Equal(("Lord Blackwood", "Chancellor"), ("Lady Ashe", "Advisor"));
            CastleLocation.GetCurrentKing()!.CourtMembers.Select(m => m.Role).Should().Equal("Chancellor", "Advisor");
        }));
    }

    [Fact]
    public async Task PromotingTheSecondNamesake_ImprovesTheSecond()
    {
        await WithKing("Kim", 5000, async _ => await AsTheWorldSim(async () =>
        {
            await _db.SaveWorldState("royal_court", CourtWithNamesakes());
            await NewOsm(_db).LoadRoyalCourtFromWorldState();

            await CourtAction("CourtAction_Promote", "2");   // the Treasurer

            var stored = await StoredCourt();
            stored.Treasury.Should().Be(5000 - GameConfig.PromoteCost);
            stored.CourtMembers[0].Influence.Should().Be(50, "the Chancellor namesake is untouched");
            stored.CourtMembers[0].LoyaltyToKing.Should().Be(60);
            stored.CourtMembers[1].Influence.Should().Be(45, "the selected Treasurer is promoted");
            stored.CourtMembers[1].LoyaltyToKing.Should().Be(50 + GameConfig.PromoteLoyaltyGain);
        }));
    }
}
