using System;
using System.Collections.Generic;
using System.Linq;
using FluentAssertions;
using UsurperRemake;
using UsurperRemake.Systems;
using Xunit;

namespace UsurperReborn.Tests;

/// <summary>
/// 1.3.5 (F12 rest): an NPC threatens a player ruler, and the challenge runs when the warning ticks run out.
/// T1: a player hires the NPC inside the warning ticks; the challenge does not run and the NPC keeps its team,
/// and the pending challenge is cleared with no line. T2: the NPC on no team challenges as before. T3: the NPC
/// joins an NPC team inside the ticks; it is taken off that team and challenges as before. Each scenario runs in
/// a copy of the game of its own.
/// </summary>
[Collection("SharedGameSingletons")]
public class ThroneChallengeF12Rest135Tests
{
    [Fact]
    public void T1_AChallengerHiredInsideTheWarningTicks_KeepsItsTeam_NoChallengeRuns_ThePendingChallengeIsCleared() =>
        IsolatedGame.Run(nameof(RecruitPoolRuns.F12RestHired), "").Should().Be("ok");

    [Fact]
    public void T2_AChallengerOnNoTeam_StormsTheCastle_AndTheChallengeRunsAsBefore() =>
        IsolatedGame.Run(nameof(RecruitPoolRuns.F12RestFree), "").Should().Be("ok");

    [Fact]
    public void T3_AChallengerThatJoinedAnNpcTeam_IsTakenOffIt_AndTheChallengeRunsAsBefore() =>
        IsolatedGame.Run(nameof(RecruitPoolRuns.F12RestNpcTeam), "").Should().Be("ok");
}

internal static partial class RecruitPoolRuns
{
    private const string F12RestRival = "Bold Rival";

    /// <summary>
    /// A player ruler (no NPC of that name, so the fight reads the default player stats; no save backend, so
    /// the ruler's level is taken as 20) and one NPC that the player-ruler branch picks: level 32, ambitious,
    /// frail. The first tick picks it and sets the pending challenge.
    /// </summary>
    private static (ChallengeSystem Challenges, King King, NPC Rival) F12RestThreatened(int seed)
    {
        var rulerSource = new NPC { Name1 = "Queen Ilse", Name2 = "Queen Ilse", Sex = CharacterSex.Female, Level = 20 };
        CastleLocation.SetCurrentKing(rulerSource);
        var king = CastleLocation.GetCurrentKing()!;
        king.AI = CharacterAI.Human;
        king.Guards.Clear(); king.MonsterGuards.Clear();
        NPCSpawnSystem.Instance.GetNPCByName("Queen Ilse").Should().BeNull("the ruler is a player");

        var rival = Ambitious(F12RestRival, 32, 100, "");
        var challenges = (ChallengeSystem)Activator.CreateInstance(typeof(ChallengeSystem), nonPublic: true)!;
        typeof(ChallengeSystem).GetField("random", WardF)!.SetValue(challenges, new CallsRandom(seed));

        ThroneTick(challenges);
        F12RestPending(challenges).Should().Be((F12RestRival, GameConfig.NPCChallengeWarningTicks), "the first tick sets the pending challenge");
        rival.Team.Should().BeEmpty();
        return (challenges, king, rival);
    }

    /// <summary>The pending challenge as (challenger name, ticks left), or null when there is none.</summary>
    private static (string Name, int Ticks)? F12RestPending(ChallengeSystem challenges)
    {
        var pending = typeof(ChallengeSystem).GetField("_pendingChallenge", WardF)!.GetValue(challenges);
        if (pending == null) return null;
        var t = pending.GetType();
        return ((string)t.GetProperty("ChallengerName")!.GetValue(pending)!, (int)t.GetProperty("TicksRemaining")!.GetValue(pending)!);
    }

    /// <summary>Runs one scenario with its news kept, the player's team registered, and every static put back.</summary>
    private static string F12RestScenario(int seed, Action<List<string>> body)
    {
        var news = new List<string>();
        NewsSystem.Instance.SetCatchUpBuffer(news);
        string languageBefore = GameConfig.Language;
        GameConfig.Language = "en";
        using var w = new RecruitPoolWorld(seed, classic: false);
        WorldSimulator.RegisterPlayerTeam(PlayerTeam);
        try
        {
            body(news);
            return "ok";
        }
        finally
        {
            WorldSimulator.UnregisterPlayerTeam(PlayerTeam);
            NewsSystem.Instance.ClearCatchUpBuffer();
            CastleLocation.SetCurrentKing(null!);
            GameConfig.Language = languageBefore;
        }
    }

    /// <summary>Row T1: hired after the threat, before the ticks run out.</summary>
    public static string F12RestHired(string _) => F12RestScenario(135, news =>
    {
        var (challenges, king, rival) = F12RestThreatened(135);
        news.Should().Contain(n => n.Contains(Loc.Get("challenge.news_threat", F12RestRival, 32)));

        // a player hires it inside the warning ticks
        rival.Team = PlayerTeam; rival.TeamPW = "oath"; rival.CTurf = true;
        WorldSimulator.IsPlayerTeam(rival.Team).Should().BeTrue();

        ThroneTick(challenges);
        F12RestPending(challenges).Should().Be((F12RestRival, 1), "one warning tick passes");
        ThroneTick(challenges);

        string storms = Loc.Get("challenge.news_storms_castle", F12RestRival, king.TitleLabel(), king.Name);
        string left = Loc.Get("city.news_left_team", F12RestRival, PlayerTeam);
        rival.Team.Should().Be(PlayerTeam, "a hired challenger keeps its team");
        rival.TeamPW.Should().Be("oath");
        rival.CTurf.Should().BeTrue();
        rival.DaysInPrison.Should().Be(0, "no challenge ran, so it was not imprisoned");
        rival.King.Should().BeFalse();
        news.Count(n => n.Contains(storms)).Should().Be(0, "no challenge ran");
        news.Count(n => n.Contains(left)).Should().Be(0, "it was not taken off the player's team");
        news.Should().NotContain(n => n.Contains(Loc.Get("challenge.news_lost_to_king", F12RestRival, king.TitleLabel(), king.Name)));
        var ruler = CastleLocation.GetCurrentKing()!;
        ruler.Name.Should().Be("Queen Ilse", "the ruler is unchanged");
        ruler.AI.Should().Be(CharacterAI.Human);
        F12RestPending(challenges).Should().BeNull("the pending challenge is cleared");

        // and nothing more comes of it on later ticks that day
        int lines = news.Count;
        for (int tick = 0; tick < 5; tick++) ThroneTick(challenges);
        rival.Team.Should().Be(PlayerTeam);
        news.Count.Should().Be(lines, "the challenge is dropped silently");
    });

    /// <summary>Row T2: on no team when the ticks run out.</summary>
    public static string F12RestFree(string _) => F12RestScenario(136, news =>
    {
        var (challenges, king, rival) = F12RestThreatened(136);
        ThroneTick(challenges);
        ThroneTick(challenges);

        string storms = Loc.Get("challenge.news_storms_castle", F12RestRival, king.TitleLabel(), king.Name);
        news.Count(n => n.Contains(storms)).Should().Be(1, "the challenge runs as before");
        news.Count(n => n.Contains(Loc.Get("challenge.news_lost_to_king", F12RestRival, king.TitleLabel(), king.Name)))
            .Should().Be(1, "the fight was fought");
        rival.DaysInPrison.Should().BeGreaterThan(0, "the failed challenger went to prison");
        CastleLocation.GetCurrentKing()!.Name.Should().Be("Queen Ilse");
        F12RestPending(challenges).Should().BeNull();
    });

    /// <summary>Row T3: joined an NPC team inside the ticks.</summary>
    public static string F12RestNpcTeam(string _) => F12RestScenario(137, news =>
    {
        var (challenges, king, rival) = F12RestThreatened(137);
        rival.Team = OwlsTeam; rival.TeamPW = "hoot";
        WorldSimulator.IsPlayerTeam(rival.Team).Should().BeFalse();
        ThroneTick(challenges);
        ThroneTick(challenges);

        news.Count(n => n.Contains(Loc.Get("challenge.news_storms_castle", F12RestRival, king.TitleLabel(), king.Name)))
            .Should().Be(1, "the challenge runs as before");
        news.Count(n => n.Contains(Loc.Get("city.news_left_team", F12RestRival, OwlsTeam))).Should().Be(1, "it left the NPC team to challenge");
        rival.Team.Should().BeEmpty();
        rival.TeamPW.Should().BeEmpty();
        rival.DaysInPrison.Should().BeGreaterThan(0);
        F12RestPending(challenges).Should().BeNull();
    });
}
