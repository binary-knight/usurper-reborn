using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using FluentAssertions;
using UsurperRemake;
using UsurperRemake.Systems;
using Xunit;

namespace UsurperReborn.Tests;

/// <summary>
/// 1.3.0 (F12): under an NPC king, an NPC on a player's team is never picked as a throne challenger, so it no
/// longer leaves the team by itself. Row K1: a hired NPC keeps its team over 2,000 seeded ticks while its
/// unhired twin challenges. Row K2: what the tick draws when a hired NPC would have been picked. Row K3: a
/// world with no player team draws and picks as the code before the fix did, tick for tick, and a registered
/// player team with no candidate on it changes nothing. The scenarios run in a copy of the game of their own.
/// </summary>
[Collection("SharedGameSingletons")]
public class ThroneChallengeHired130Tests
{
    [Fact]
    public void K1_AHiredNpc_KeepsItsTeam_Over2000Ticks_WhileItsUnhiredTwinChallenges() =>
        IsolatedGame.Run(nameof(RecruitPoolRuns.ThroneHiredKeepsTeam), "").Should().Be("ok");

    [Fact]
    public void K2_WhenAHiredNpcWouldHaveBeenPicked_TheNextCandidateMovesIn_OrNoOneAndNoDraw() =>
        IsolatedGame.Run(nameof(RecruitPoolRuns.ThroneHiredDraws), "").Should().Be("ok");

    [Fact]
    public void K3_AWorldWithNoPlayerTeam_DrawsAndPicksAsBeforeTheFix_TickForTick() =>
        IsolatedGame.Run(nameof(RecruitPoolRuns.ThroneNoPlayerTeam), "").Should().Be("ok");

    // 1.3.0 (F12b): the empty throne. A hired NPC is not crowned as the named heir or from the fallback list.

    [Fact]
    public void K4_AHiredNpcNamedHeir_IsNotFound_TheThroneGoesToTheNextRule_AnUnhiredHeirIsCrownedAsBefore() =>
        IsolatedGame.Run(nameof(RecruitPoolRuns.ThroneHiredHeir), "").Should().Be("ok");

    [Fact]
    public void K5_TheFallbackList_LeavesOutAHiredNpc_TheThroneStaysEmptyOrGoesToTheNextInTheList() =>
        IsolatedGame.Run(nameof(RecruitPoolRuns.ThroneHiredFallback), "").Should().Be("ok");
}

internal static partial class RecruitPoolRuns
{
    /// <summary>A seeded random that writes down every call.</summary>
    private sealed class CallsRandom : Random
    {
        public readonly List<string> Calls = new();
        public CallsRandom(int seed) : base(seed) { }
        public override int Next() { int v = base.Next(); Calls.Add($"Next()={v}"); return v; }
        public override int Next(int maxValue) { int v = base.Next(maxValue); Calls.Add($"Next({maxValue})={v}"); return v; }
        public override int Next(int minValue, int maxValue) { int v = base.Next(minValue, maxValue); Calls.Add($"Next({minValue},{maxValue})={v}"); return v; }
        public override double NextDouble() { double v = base.NextDouble(); Calls.Add($"NextDouble()={v:R}"); return v; }
    }

    private const string OwlsTeam = "Night Owls";

    /// <summary>An NPC that the throne tick can pick: level 15 or more, the given Ambition. It is frail, so
    /// its challenge fails against the king below and it goes to prison.</summary>
    private static NPC Ambitious(string name, int level, long strength, string team, float ambition = 0.9f)
    {
        var n = Make(name, CharacterRace.Human, level, team: team);
        n.EnsureSystemsInitialized();
        n.Brain!.Personality!.Ambition = ambition;
        n.Strength = strength; n.WeapPow = 1; n.Defence = 0; n.ArmPow = 0; n.HP = n.MaxHP = n.BaseMaxHP = 1;
        return n;
    }

    /// <summary>An NPC king no challenger can beat, and a challenge system with a seeded, recorded random.</summary>
    private static (ChallengeSystem Challenges, CallsRandom Random) NpcKingAndChallenges(int seed)
    {
        var kingNpc = Make("Old King", CharacterRace.Human, 60);
        kingNpc.Strength = 100_000; kingNpc.Defence = 100_000; kingNpc.WeapPow = 1000; kingNpc.HP = kingNpc.MaxHP = kingNpc.BaseMaxHP = 1_000_000;
        CastleLocation.SetCurrentKing(kingNpc);
        var king = CastleLocation.GetCurrentKing()!;
        king.AI = CharacterAI.Computer;
        king.Guards.Clear(); king.MonsterGuards.Clear();
        var challenges = (ChallengeSystem)Activator.CreateInstance(typeof(ChallengeSystem), nonPublic: true)!;
        var random = new CallsRandom(seed);
        typeof(ChallengeSystem).GetField("random", WardF)!.SetValue(challenges, random);
        return (challenges, random);
    }

    private static void ThroneTick(ChallengeSystem challenges) =>
        typeof(ChallengeSystem).GetMethod("ProcessThroneChallenge", WardF)!.Invoke(challenges, null);

    /// <summary>The candidates of the NPC-king branch as the code before the fix listed them (a705b716,
    /// ChallengeSystem.cs :217 to :226): no question about a player's team.</summary>
    private static List<NPC> CandidatesOfBefore(King king) =>
        NPCSpawnSystem.Instance.ActiveNPCs
            .Where(n => n.IsAlive && n.Level >= 15 && n.DaysInPrison <= 0 && !n.King
                && !string.Equals(n.Name, king.Name, StringComparison.OrdinalIgnoreCase) && !n.IsStoryNPC
                && n.Brain?.Personality?.Ambition > 0.6f)
            .OrderByDescending(n => n.Level + n.Strength).Take(5).ToList();

    /// <summary>Row K1.</summary>
    public static string ThroneHiredKeepsTeam(string _)
    {
        var news = new List<string>();
        NewsSystem.Instance.SetCatchUpBuffer(news);
        GameConfig.Language = "en";
        using var w = new RecruitPoolWorld(41, classic: false);
        WorldSimulator.RegisterPlayerTeam(PlayerTeam);
        try
        {
            var (challenges, _) = NpcKingAndChallenges(41);
            var king = CastleLocation.GetCurrentKing()!;
            var hired = Ambitious("Hired Hand", 40, 500, PlayerTeam);
            var twin = Ambitious("Free Twin", 40, 500, OwlsTeam);
            WorldSimulator.IsOnPlayerTeam(hired).Should().BeTrue();
            WorldSimulator.IsOnPlayerTeam(twin).Should().BeFalse();
            // the code before the fix would list both, the hired one first
            CandidatesOfBefore(king).Should().Equal(hired, twin);

            string Challenged(NPC n) => Loc.Get("challenge.news_challenged", n.Name, king.TitleLabel(), king.Name);
            int twinChallenges = 0;
            for (int tick = 0; tick < 2000; tick++)
            {
                // the whole tick of the challenge system, as the world simulator calls it
                challenges.ProcessMaintenanceChallenges();
                hired.Team.Should().Be(PlayerTeam, "tick {0}", tick);
                hired.DaysInPrison.Should().Be(0, "tick {0}", tick);
                if (twin.DaysInPrison > 0) { twinChallenges++; twin.DaysInPrison = 0; twin.CurrentLocation = "Main Street"; }   // its sentence passes
            }
            twinChallenges.Should().BeGreaterThan(10, "the unhired twin challenged again and again");
            twin.Team.Should().BeEmpty("it left its team to challenge");
            news.Count(n => n.Contains(Challenged(twin))).Should().Be(twinChallenges);
            string hiredChallenged = Challenged(hired), hiredLeft = Loc.Get("city.news_left_team", hired.Name2, PlayerTeam);
            string twinLeft = Loc.Get("city.news_left_team", twin.Name2, OwlsTeam);
            news.Count(n => n.Contains(twinLeft)).Should().Be(1);
            news.Count(n => n.Contains(hiredChallenged)).Should().Be(0);
            news.Count(n => n.Contains(hiredLeft)).Should().Be(0);
            CastleLocation.GetCurrentKing()!.Name.Should().Be("Old King");

            // sacked, the same NPC is a candidate like any other
            hired.Team = "";
            for (int tick = 0; tick < 2000 && hired.DaysInPrison == 0; tick++) challenges.ProcessMaintenanceChallenges();
            hired.DaysInPrison.Should().BeGreaterThan(0, "off the player's team it challenges as before");
            return "ok";
        }
        finally
        {
            WorldSimulator.UnregisterPlayerTeam(PlayerTeam);
            NewsSystem.Instance.ClearCatchUpBuffer();
            CastleLocation.SetCurrentKing(null!);
        }
    }

    /// <summary>Row K2.</summary>
    public static string ThroneHiredDraws(string _)
    {
        SilenceNews();
        GameConfig.Language = "en";
        using var w = new RecruitPoolWorld(42, classic: false);
        WorldSimulator.RegisterPlayerTeam(PlayerTeam);
        try
        {
            // (a) the hired NPC is the only candidate: no one is picked and the tick draws nothing (the code
            // before the fix drew one Next(1) and took it off the team)
            var (challenges, random) = NpcKingAndChallenges(42);
            var king = CastleLocation.GetCurrentKing()!;
            var hired = Ambitious("Hired Hand", 40, 900, PlayerTeam);
            CandidatesOfBefore(king).Should().Equal(hired);
            for (int tick = 0; tick < 200; tick++) ThroneTick(challenges);
            random.Calls.Should().BeEmpty();
            hired.Team.Should().Be(PlayerTeam);

            // (b) six candidates by rank, the hired one first: the five the tick picks among are ranks 2 to 6
            // (the sixth moves in), with one draw, Next(5)
            var others = Enumerable.Range(2, 5).Select(rank => Ambitious("Rank " + rank, 40, 900 - rank * 10, "")).ToList();
            CandidatesOfBefore(king).Should().Equal(new[] { hired }.Concat(others.Take(4)), "before the fix the sixth was out and the hired one in");
            var picked = new HashSet<string>();
            for (int tick = 0; tick < 400; tick++)
            {
                random.Calls.Clear();
                ThroneTick(challenges);
                random.Calls.Should().NotBeEmpty();
                random.Calls[0].Should().StartWith("Next(5)=", "one pick among five");
                var jailed = others.Where(n => n.DaysInPrison > 0).ToList();
                jailed.Should().ContainSingle("the picked challenger failed and was imprisoned");
                jailed[0].Should().BeSameAs(others[int.Parse(random.Calls[0].Split('=')[1])], "the pick indexes the five that are left, by rank");
                picked.Add(jailed[0].Name2);
                jailed[0].DaysInPrison = 0; jailed[0].CurrentLocation = "Main Street";
                hired.Team.Should().Be(PlayerTeam);
                hired.DaysInPrison.Should().Be(0);
            }
            picked.Should().BeEquivalentTo(others.Select(n => n.Name2), "every one of the five is picked, the sixth by rank among them");
            return "ok";
        }
        finally { WorldSimulator.UnregisterPlayerTeam(PlayerTeam); CastleLocation.SetCurrentKing(null!); }
    }

    /// <summary>
    /// Row K3. Eight candidates on NPC teams and on none, no hired NPC. One run with no player team at all,
    /// one with a player team registered that none of them is on: every tick is checked against the candidate
    /// list of before the fix (the count drawn among, the NPC picked, whether it stays with its team), and the
    /// two runs give the same calls and the same roster.
    /// </summary>
    public static string ThroneNoPlayerTeam(string _)
    {
        SilenceNews();
        GameConfig.Language = "en";
        (List<string> Calls, string Roster) Run(bool registerAnEmptyPlayerTeam)
        {
            using var w = new RecruitPoolWorld(43, classic: false);
            if (registerAnEmptyPlayerTeam) WorldSimulator.RegisterPlayerTeam(PlayerTeam);
            try
            {
                var (challenges, random) = NpcKingAndChallenges(43);
                var king = CastleLocation.GetCurrentKing()!;
                var all = new List<NPC>();
                for (int i = 0; i < 8; i++)
                    all.Add(Ambitious("Rival " + i, 20 + i * 3, 300 + (i * 37) % 90, i % 3 == 0 ? "" : i % 3 == 1 ? OwlsTeam : "Grey Company", i % 4 == 3 ? 0.7f : 0.9f));
                all.Should().OnlyContain(n => !WorldSimulator.IsOnPlayerTeam(n));
                var calls = new List<string>();
                int picks = 0, stayed = 0;
                for (int tick = 0; tick < 2000; tick++)
                {
                    if (tick % 7 == 0) foreach (var n in all) { n.DaysInPrison = 0; n.CurrentLocation = "Main Street"; }   // sentences pass
                    if (tick % 50 == 0) for (int i = 0; i < all.Count; i++) if (i % 3 != 0 && all[i].Team == "") all[i].Team = i % 3 == 1 ? OwlsTeam : "Grey Company";   // and they join teams again
                    var before = CandidatesOfBefore(king);
                    var teams = all.ToDictionary(n => n, n => n.Team);
                    random.Calls.Clear();
                    ThroneTick(challenges);
                    calls.AddRange(random.Calls);
                    if (before.Count == 0) { random.Calls.Should().BeEmpty("tick {0}: no candidate, no draw", tick); continue; }
                    random.Calls[0].Should().StartWith($"Next({before.Count})=", "tick {0}: one pick among the candidates of before", tick);
                    var picked = before[int.Parse(random.Calls[0].Split('=')[1])];
                    picks++;
                    if (teams[picked] != "" && picked.Brain!.Personality!.Ambition < 0.8f)
                    {
                        stayed++;
                        random.Calls.Should().HaveCount(1, "tick {0}: it stays with its team, nothing more is drawn", tick);
                        picked.Team.Should().Be(teams[picked]);
                        picked.DaysInPrison.Should().Be(0);
                    }
                    else
                    {
                        picked.Team.Should().BeEmpty("tick {0}: the challenger left its team", tick);
                        picked.DaysInPrison.Should().BeGreaterThan(0, "tick {0}: its challenge failed", tick);
                    }
                    foreach (var n in all.Where(n => n != picked)) n.Team.Should().Be(teams[n], "tick {0}: no one else is touched", tick);
                }
                picks.Should().BeGreaterThan(1500);
                stayed.Should().BeGreaterThan(20);
                return (calls, w.Roster());
            }
            finally { if (registerAnEmptyPlayerTeam) WorldSimulator.UnregisterPlayerTeam(PlayerTeam); CastleLocation.SetCurrentKing(null!); }
        }
        var none = Run(false);
        var empty = Run(true);
        empty.Calls.Should().Equal(none.Calls);
        empty.Roster.Should().Be(none.Roster);
        return "ok";
    }

    // ---------------------------------------------------------------- F12b: the empty throne

    /// <summary>A challenge system with a seeded, recorded random and an empty throne.</summary>
    private static (ChallengeSystem Challenges, CallsRandom Random) EmptyThrone(int seed, string? heir = null)
    {
        CastleLocation.SetCurrentKing(null!);
        var challenges = (ChallengeSystem)Activator.CreateInstance(typeof(ChallengeSystem), nonPublic: true)!;
        var random = new CallsRandom(seed);
        typeof(ChallengeSystem).GetField("random", WardF)!.SetValue(challenges, random);
        typeof(ChallengeSystem).GetField("_lastDesignatedHeir", WardF)!.SetValue(challenges, heir);
        return (challenges, random);
    }

    private static List<string> CallNames(CallsRandom random) => random.Calls.Select(c => c.Split('=')[0]).ToList();

    /// <summary>Row K4: the named heir.</summary>
    public static string ThroneHiredHeir(string _)
    {
        GameConfig.Language = "en";
        // One world: the heir on `heirTeam`, a free claimant of a higher level, and whether the player's team
        // is in the simulator's list. Returns who reigns, the heir's team, the calls and the news.
        (string King, string HeirTeam, bool HeirKing, List<string> Calls, List<string> News) Run(string heirTeam, bool registerPlayerTeam)
        {
            var news = new List<string>();
            NewsSystem.Instance.SetCatchUpBuffer(news);
            using var w = new RecruitPoolWorld(44, classic: false);
            if (registerPlayerTeam) WorldSimulator.RegisterPlayerTeam(PlayerTeam);
            try
            {
                var (challenges, random) = EmptyThrone(44, "Named Heir");
                var heir = Ambitious("Named Heir", 25, 100, heirTeam, 0.1f);
                Ambitious("Free Claimant", 30, 100, "", 0.1f);
                challenges.ClaimEmptyThroneIfVacant();
                return (CastleLocation.GetCurrentKing()?.Name ?? "", heir.Team, heir.King, CallNames(random), news.ToList());
            }
            finally
            {
                if (registerPlayerTeam) WorldSimulator.UnregisterPlayerTeam(PlayerTeam);
                NewsSystem.Instance.ClearCatchUpBuffer();
                CastleLocation.SetCurrentKing(null!);
            }
        }
        string missing = Loc.Get("challenge.news_heir_missing", "Named Heir");

        // hired: not found as heir; the existing "missing" line; the next rule crowns the free claimant
        var hired = Run(PlayerTeam, true);
        hired.King.Should().Be("Free Claimant");
        hired.HeirTeam.Should().Be(PlayerTeam, "the hired heir keeps its team");
        hired.HeirKing.Should().BeFalse();
        hired.News.Count(n => n.Contains(missing)).Should().Be(1);
        hired.News.Should().NotContain(n => n.Contains("Named Heir") && !n.Contains(missing), "no line says it left or claimed");
        hired.Calls.Should().Equal(new[] { "Next(5000,20000)", "Next(10,30)" }, "the draws of the next rule: treasury and tax");

        // the same heir on an NPC team, with the player's team registered: crowned as before, off its team
        var unhired = Run(OwlsTeam, true);
        unhired.King.Should().Be("Named Heir");
        unhired.HeirTeam.Should().BeEmpty();
        unhired.HeirKing.Should().BeTrue();
        unhired.News.Should().NotContain(n => n.Contains(missing));
        unhired.Calls.Should().Equal(new[] { "Next(5000,20000)" }, "the heir's one draw: the treasury");

        // no player team in the world at all: the same calls, the same king, the same news as that run;
        // and a team of that NAME that no player holds is an NPC team like any other
        var none = Run(OwlsTeam, false);
        (none.King, none.HeirTeam, none.HeirKing).Should().Be((unhired.King, unhired.HeirTeam, unhired.HeirKing));
        none.Calls.Should().Equal(unhired.Calls);
        none.News.Should().Equal(unhired.News);
        var sameName = Run(PlayerTeam, false);
        sameName.King.Should().Be("Named Heir");
        sameName.Calls.Should().Equal(unhired.Calls);
        return "ok";
    }

    /// <summary>Row K5: no heir and no free NPC of level 10 or more, so the fallback list is read.</summary>
    public static string ThroneHiredFallback(string _)
    {
        GameConfig.Language = "en";
        (string King, string HiredTeam, string OtherTeam, List<string> Calls, List<string> News) Run(bool withAnother, string hiredTeam, bool registerPlayerTeam)
        {
            var news = new List<string>();
            NewsSystem.Instance.SetCatchUpBuffer(news);
            using var w = new RecruitPoolWorld(45, classic: false);
            if (registerPlayerTeam) WorldSimulator.RegisterPlayerTeam(PlayerTeam);
            try
            {
                var (challenges, random) = EmptyThrone(45);
                var hired = Ambitious("Hired Hand", 30, 100, hiredTeam);                       // first of the list by level
                var other = withAnother ? Ambitious("Owl Captain", 20, 100, OwlsTeam) : null;  // second
                Ambitious("Young One", 5, 100, "");                                            // free, but below level 10
                challenges.ClaimEmptyThroneIfVacant();
                return (CastleLocation.GetCurrentKing()?.Name ?? "", hired.Team, other?.Team ?? "", CallNames(random), news.ToList());
            }
            finally
            {
                if (registerPlayerTeam) WorldSimulator.UnregisterPlayerTeam(PlayerTeam);
                NewsSystem.Instance.ClearCatchUpBuffer();
                CastleLocation.SetCurrentKing(null!);
            }
        }

        // only the hired NPC is able: the throne stays empty, nothing is drawn, nothing is said
        var alone = Run(false, PlayerTeam, true);
        alone.King.Should().BeEmpty();
        alone.HiredTeam.Should().Be(PlayerTeam);
        alone.Calls.Should().BeEmpty();
        alone.News.Should().BeEmpty();

        // with another able NPC on an NPC team: that one is crowned, though the hired one is first by level
        var two = Run(true, PlayerTeam, true);
        two.King.Should().Be("Owl Captain");
        two.HiredTeam.Should().Be(PlayerTeam);
        two.OtherTeam.Should().BeEmpty("the new monarch left its NPC team, as before");
        two.Calls.Should().Equal(new[] { "Next(5000,20000)", "Next(10,30)" });
        two.News.Should().NotContain(n => n.Contains("Hired Hand"));

        // no player team in the world: the first of the list by level is crowned as before, with the same
        // two draws; a registered player team that none of them is on changes nothing
        var none = Run(true, "Grey Company", false);
        none.King.Should().Be("Hired Hand");
        none.HiredTeam.Should().BeEmpty();
        none.Calls.Should().Equal(new[] { "Next(5000,20000)", "Next(10,30)" });
        var empty = Run(true, "Grey Company", true);
        (empty.King, empty.HiredTeam, empty.OtherTeam).Should().Be((none.King, none.HiredTeam, none.OtherTeam));
        empty.Calls.Should().Equal(none.Calls);
        empty.News.Should().Equal(none.News);
        return "ok";
    }
}
