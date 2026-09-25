using System.Linq;
using System.Threading.Tasks;
using FluentAssertions;
using UsurperRemake.Systems;
using Xunit;

namespace UsurperReborn.Tests;

/// <summary>
/// v1.1.14: an NPC the street throne challenge imprisons has a court prison record, so the online court upkeep
/// releases it when the sentence ends (the path is unreachable, see StreetEncounterSystemReachabilityTests).
/// </summary>
public partial class OwnerProcessConflictTests
{
    [Fact]
    public async Task AStreetImprisonedNpc_IsReleasedByTheOnlineUpkeep_WhenTheSentenceEnds()
    {
        await WithKing("Kim", 100_000, async _ => await AsTheWorldSim(async () =>
        {
            await _db.SaveWorldState("royal_court", CourtWith("Kim", 100_000, ("Gerald", 60)));
            await NewOsm(_db).LoadRoyalCourtFromWorldState();
            var kim = PlayerKing("Kim");
            var rook = Npc("npc_sp_rook", "Rook");

            await StreetThroneChallenge(kim, rook, "I");
            rook.DaysInPrison.Should().Be(14);
            var stored = await StoredCourt();
            stored.Prisoners.Should().ContainSingle(p => p.CharacterName == "Rook" && p.Sentence == 14);
            stored.Guards.Single().Loyalty.Should().Be(50, "the guards' loyalty loss is in the same write");

            for (int day = 1; day < 14; day++)
                (await King.ProcessDailyActivitiesAsync(_db)).Should().BeTrue();
            rook.DaysInPrison.Should().BeGreaterThan(0, "the sentence has a day to run");

            (await King.ProcessDailyActivitiesAsync(_db)).Should().BeTrue();
            rook.DaysInPrison.Should().Be(0, "the upkeep releases the NPC when the sentence ends");
            (await StoredCourt()).Prisoners.Should().NotContain(p => p.CharacterName == "Rook");
        }));
    }
}
