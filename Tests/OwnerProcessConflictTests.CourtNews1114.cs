using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using System.Threading.Tasks;
using FluentAssertions;
using UsurperRemake.Systems;
using Xunit;

namespace UsurperReborn.Tests;

/// <summary>
/// v1.1.14: court politics run inside the retried court change; their news is collected there and posted
/// once, after the write lands, never per attempt and never for a change that gave up.
/// </summary>
public partial class OwnerProcessConflictTests
{
    private string PlotCourt(string king) =>
        JsonSerializer.Serialize(new RoyalCourtSaveData
        {
            KingName = king, KingAI = (int)CharacterAI.Computer, Treasury = 50_000, TaxRate = 7, CityTaxPercent = 3,
            // a plot this close to done ends this tick either way: discovered, or carried out; one news line
            ActivePlots = new List<CourtIntrigueSaveData>
            {
                new() { PlotType = "Scandal", Conspirators = new List<string> { "Lord Nobody" }, Target = king, Progress = 99 }
            }
        }, Json);

    /// <summary>Runs one court politics tick with conflicts forced before the first writes; returns the news posted about king.</summary>
    private async Task<List<string>> CourtPoliticsNews(string king, int conflicts)
    {
        var posted = new List<string>();
        int attempts = 0;
        WorldSimulator.CourtPoliticsBeforeWrite = async () =>
        {
            if (attempts++ < conflicts)   // another process writes the court first
                (await _db.SaveWorldStateIfVersion("royal_court", PlotCourt(king), _db.GetWorldStateVersion("royal_court"))).Should().BeTrue();
        };
        NewsSystem.Instance.SetCatchUpBuffer(posted);
        try { RunCourtPolitics(); }
        finally
        {
            NewsSystem.Instance.ClearCatchUpBuffer();
            WorldSimulator.CourtPoliticsBeforeWrite = null;
        }
        attempts.Should().Be(Math.Min(conflicts + 1, 5));
        return posted.Where(n => n.Contains(king)).ToList();
    }

    [Fact]
    public async Task ACourtPoliticsTick_RetriedAfterAConflict_PostsItsNewsOnce()
    {
        await WithKing("Newsworthy King", 50_000, async _ => await AsTheWorldSim(async () =>
        {
            await _db.SaveWorldState("royal_court", PlotCourt("Newsworthy King"));
            new WorldSimService(_db).LoadRoyalCourtFromWorldState();

            var news = await CourtPoliticsNews("Newsworthy King", conflicts: 1);

            news.Should().ContainSingle("the delegate ran twice, but only the written run's news is posted");
            (await StoredCourt()).ActivePlots.Should().BeEmpty("the plot ended in the written court");
        }));
    }

    [Fact]
    public async Task ACourtPoliticsTick_ThatGivesUp_PostsNoNews()
    {
        await WithKing("Unlucky King", 50_000, async _ => await AsTheWorldSim(async () =>
        {
            await _db.SaveWorldState("royal_court", PlotCourt("Unlucky King"));
            new WorldSimService(_db).LoadRoyalCourtFromWorldState();

            var news = await CourtPoliticsNews("Unlucky King", conflicts: 5);

            news.Should().BeEmpty("nothing was written, so nothing happened");
            (await StoredCourt()).ActivePlots.Should().ContainSingle("the court politics' change never landed");
        }));
    }

    [Fact]
    public void CourtPolitics_PostNoNewsInsideTheDelegate()
    {
        var sim = Source("Systems", "WorldSimulator.cs").Replace("\r\n", "\n");
        foreach (var method in new[] { "private List<(bool Important, string Text)> CourtPoliticsTick(", "private void ProcessNPCGuardRecruitment(",
                                       "private void ProcessCourtIntrigue(", "private void AdvancePlot(", "private void ExecutePlot(" })
        {
            int start = sim.IndexOf(method, StringComparison.Ordinal);
            start.Should().BeGreaterThan(0, method);
            int end = sim.IndexOf("\n    }\n", start, StringComparison.Ordinal);
            sim.Substring(start, end - start).Should().NotContain("Newsy", $"{method} runs inside the retried court change");
        }
    }
}
