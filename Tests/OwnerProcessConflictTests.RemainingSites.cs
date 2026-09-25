using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using System.Threading.Tasks;
using FluentAssertions;
using UsurperRemake.Systems;
using Xunit;

namespace UsurperReborn.Tests;

/// <summary>v1.1.13: the last in-memory court changes (a served sentence's record, the single-player guard pass) are guarded too.</summary>
public partial class OwnerProcessConflictTests
{
    [Fact]
    public async Task AServedSentencesRecord_IsRemovedFromTheStoredCourt_AndStaysRemoved()
    {
        await WithKing("Aldric", 1000, async _ => await AsTheWorldSim(async () =>
        {
            await _db.SaveWorldState("royal_court", NpcCourtHolding("Aldric", "Dorn", withRecord: true));
            await NewOsm(_db).LoadRoyalCourtFromWorldState();
            var dorn = Npc("npc_rs_dorn", "Dorn");
            dorn.DaysInPrison = 1;

            PrisonActivitySystem.Instance.ProcessDailyPrisonCountdown();
            (await CastleLocation.CourtChangeAsync(court => { court.Treasury += 5; return true; })).Should().BeTrue();   // a deposit

            dorn.DaysInPrison.Should().Be(0);
            (await StoredCourt()).Prisoners.Should().NotContain(p => p.CharacterName == "Dorn");
            CastleLocation.GetCurrentKing()!.Prisoners.Should().NotContainKey("Dorn");
        }));
    }

    [Fact]
    public async Task TheGuardUpkeep_OnACourtRecord_WithoutAStore_IsTheInMemoryCourt()
    {
        await WithKing("Kim", 0, async king =>
        {
            king.Guards.Add(new RoyalGuard { Name = "Faithless", Loyalty = 8, DailySalary = 10, RecruitmentDate = DateTime.Now });
            king.Guards.Add(new RoyalGuard { Name = "Steady", Loyalty = 90, DailySalary = 10, RecruitmentDate = DateTime.Now });
            var recruited = king.Guards.ToDictionary(g => g.Name, g => g.RecruitmentDate);
            var news = new List<(bool Important, string Text)>();
            (await King.ProcessDailyActivitiesAsync(null, court => { news = King.ApplyGuardUpkeep(court, recruited, new Random(1)); return true; })).Should().BeTrue();

            var now = CastleLocation.GetCurrentKing()!;
            now.Guards.Select(g => g.Name).Should().Equal(new[] { "Steady" }, "the unpaid, disloyal guard deserts");
            now.Guards.Single().Loyalty.Should().NotBe(90, "the day's loyalty change is the written copy's");
            news.Should().Contain(n => n.Text.Contains("Faithless"));
        });
    }

    [Fact]
    public void TheSinglePlayerDay_RunsTheGuardUpkeepInsideTheCourtChange()
    {
        string src = CodeOnly(Source("Systems", "DailySystemManager.cs"));
        src.Should().Contain("await King.ProcessDailyActivitiesAsync(null, court =>")
            .And.Contain("news = King.ApplyGuardUpkeep(court, recruited, Random.Shared);")
            .And.NotContain("ProcessGuardLoyalty(").And.NotContain("ProcessTreasuryCrisis(");
    }
}
