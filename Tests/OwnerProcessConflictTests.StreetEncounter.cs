using System;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text.Json;
using System.Threading.Tasks;
using FluentAssertions;
using UsurperRemake.Systems;
using Xunit;

namespace UsurperReborn.Tests;

/// <summary>v1.1.13: the street throne challenge's court outcomes (unreachable, see the IL test) are guarded court changes.</summary>
public partial class OwnerProcessConflictTests
{
    private sealed class ZeroRandom : Random { public override int Next(int maxValue) => 0; public override int Next(int min, int max) => min; }

    private static async Task StreetThroneChallenge(Character player, NPC challenger, string choice)
    {
        var street = (StreetEncounterSystem)Activator.CreateInstance(typeof(StreetEncounterSystem), nonPublic: true)!;
        typeof(StreetEncounterSystem).GetField("_random", BindingFlags.NonPublic | BindingFlags.Instance)!.SetValue(street, new ZeroRandom());
        var term = new TerminalEmulator(new LineStream(new[] { choice, "", "" }), new MemoryStream());
        await (Task)typeof(StreetEncounterSystem).GetMethod("ExecuteThroneChallenge", BindingFlags.NonPublic | BindingFlags.Instance)!
            .Invoke(street, new object[] { challenger, player, term, new EncounterResult() })!;
    }

    [Fact]
    public async Task TheStreetChallengesAdvisorAndGuardPenalty_SurviveAFollowingDeposit()
    {
        await WithKing("Kim", 1000, async _ => await AsTheWorldSim(async () =>
        {
            await _db.SaveWorldState("royal_court", CourtWith("Kim", 1000, ("Gerald", 60)));
            await NewOsm(_db).LoadRoyalCourtFromWorldState();
            var kim = PlayerKing("Kim");
            kim.Charisma = 50;

            await StreetThroneChallenge(kim, Npc("npc_st_vex", "Vex"), "N");   // negotiated into an advisory seat
            await StreetThroneChallenge(kim, Npc("npc_st_rook", "Rook"), "I");  // imprisoned: the guards' loyalty drops
            (await CastleLocation.CourtChangeAsync(court => { court.Treasury += 100; return true; })).Should().BeTrue();   // a deposit

            var stored = await StoredCourt();
            stored.CourtMembers.Should().Contain(m => m.Name == "Vex" && m.Role == "Advisor");
            stored.Guards.Single().Loyalty.Should().Be(50);
            stored.Treasury.Should().Be(1100);
            CastleLocation.GetCurrentKing()!.CourtMembers.Should().Contain(m => m.Name == "Vex");
        }));
    }
}
