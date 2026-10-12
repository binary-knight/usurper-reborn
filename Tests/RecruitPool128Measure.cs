using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text;
using UsurperRemake.Systems;
using Xunit;

namespace UsurperReborn.Tests;

/// <summary>
/// v1.2.8 (H2): the measurements of rows 9 and 12 of the brief (the pool over a long seeded world run). They
/// are not part of the gate: the test returns at once unless H2_MEASURE names "seed,ticks,size,warmup" and
/// H2_MEASURE_OUT a file for the table. Each run is a whole world (every NPC acts, teams form as they do on
/// the server), online rules, the server's x5 experience, with 30 seconds of clock per tick.
/// </summary>
[Collection("SharedGameSingletons")]
public class RecruitPool128Measure
{
    [Fact]
    public void Measure()
    {
        string? arg = Environment.GetEnvironmentVariable("H2_MEASURE");
        string? outFile = Environment.GetEnvironmentVariable("H2_MEASURE_OUT");
        if (string.IsNullOrEmpty(arg) || string.IsNullOrEmpty(outFile)) return;
        File.WriteAllText(outFile, IsolatedGame.Run(nameof(RecruitPoolRuns.MeasureRun), arg));
    }
}

internal static partial class RecruitPoolRuns
{
    private static readonly string[] BandNames = { "1-5", "6-10", "11-15" };

    /// <summary>
    /// "seed,ticks,size,warmup[,off]": the classic roster, filled with ordinary immigrants to `size` living NPCs
    /// (0: the classic roster alone), `warmup` ticks with the switch off so teams exist as on a running server,
    /// then `ticks` ticks measured with the switch on (or off, for the comparison).
    /// </summary>
    public static string MeasureRun(string arg)
    {
        var a = arg.Split(',');
        int seed = int.Parse(a[0]), ticks = int.Parse(a[1]), size = int.Parse(a[2]), warmup = int.Parse(a[3]);
        // the variant: off (the switch off), neither (the pool without the cap count), count (the build)
        string variant = a.Length > 4 ? a[4] : "count";
        // "daily": once every 2,880 ticks (24 hours) the prison day of the world's daily reset is run, as the
        // server does (PrisonActivitySystem.ProcessDailyPrisonCountdown); without it nobody ever leaves prison
        bool daily = a.Length > 5 && a[5] == "daily";
        // "server": the model keeps the server's day. A store is given to the court (a temp SQLite file, as the
        // world simulator has), and once every 2,880 ticks (24 hours at 30 seconds) the world daily reset of the
        // server runs (WorldSimService.ProcessWorldDailyReset, the body of CheckWorldDailyReset), then the prison
        // day that a player's daily reset runs (PrisonActivitySystem.ProcessDailyPrisonCountdown), and the
        // challenge system's real-clock stamps are moved with the model's clock.
        // "server" counts NPC sentences down once a day; "serverN" N times a day: on the server that countdown
        // runs in every player's own daily reset (DailySystemManager.PerformDailyReset), so N is the number of
        // players whose day turns over, which the model cannot know.
        bool server = a.Length > 5 && a[5].StartsWith("server");
        int countdownsPerDay = server && a[5].Length > 6 ? int.Parse(a[5].Substring(6)) : 1;
        string? dbPath = null; SqlSaveBackend? db = null; WorldSimService? service = null;
        var dailyErrors = new List<string>(); int daysRun = 0;
        var challenge = ChallengeSystem.Instance;
        var dethroned = typeof(ChallengeSystem).GetField("_lastDethronementTime", BindingFlags.Instance | BindingFlags.NonPublic)!;
        var challengeDay = typeof(ChallengeSystem).GetField("_lastChallengeDay", BindingFlags.Instance | BindingFlags.NonPublic)!;
        if (server)
        {
            dbPath = Path.Combine(Path.GetTempPath(), $"usurper-h2-model-{Guid.NewGuid():N}.db");
            db = new SqlSaveBackend(dbPath);
            service = new WorldSimService(db, npcXpMultiplier: 5.0f);
            SaveSystem.InitializeWithBackend(db);
            OnlineStateManager.SimCourtStore = db;
            challenge.SqlBackend = db;
        }
        void ServerClock(TimeSpan span)
        {
            if (!server) return;
            if ((DateTime)dethroned.GetValue(challenge)! > DateTime.MinValue.AddYears(1))
                dethroned.SetValue(challenge, (DateTime)dethroned.GetValue(challenge)! - span);
        }
        int modelTick = 0;
        void ServerDay()
        {
            modelTick++;
            if (!server) return;
            if (modelTick % (2880 / countdownsPerDay) == 0) PrisonActivitySystem.Instance.ProcessDailyPrisonCountdown();
            if (modelTick % 2880 != 0) return;
            daysRun++;
            try { typeof(WorldSimService).GetMethod("ProcessWorldDailyReset", BindingFlags.Instance | BindingFlags.NonPublic)!.Invoke(service, null); }
            catch (Exception e) { dailyErrors.Add((e.InnerException ?? e).Message); }
            challengeDay.SetValue(challenge, -1);
        }
        bool on = variant != "off";
        // variants: off (the base behaviour), count (the build)
        WorldSimulator.PoolCapCountsWaiting = variant == "count";
        var tick30 = TimeSpan.FromSeconds(30);
        // the news is kept for one tick at a time: a leave from a team that no line names is a release
        var newsOfTheTick = new List<string>();
        NewsSystem.Instance.SetCatchUpBuffer(newsOfTheTick);
        SetSwitch(false);
        using var w = new RecruitPoolWorld(seed);
        string sb0 = "";
        int made = 0;
        while (w.Alive().Count < size)
        {
            int avg = (int)w.Alive().Average(n => n.Level);
            var npc = NPCSpawnSystem.Instance.GenerateImmigrantNPC((CharacterRace)(made % 10), made % 2 == 0 ? CharacterSex.Male : CharacterSex.Female, avg);
            if (npc != null) NPCSpawnSystem.Instance.AddRestoredNPC(npc);
            made++;
        }
        for (int i = 0; i < warmup; i++) { w.Elapse(tick30); ServerClock(tick30); w.Step(); ServerDay(); }
        sb0 = $"in prison after the warm-up: {w.Alive().Count(n => n.DaysInPrison > 0)}";

        var sb = new StringBuilder();
        sb.AppendLine(sb0);
        var start = w.Alive();
        sb.AppendLine($"run seed={seed} ticks={ticks} size={size} warmup={warmup} variant={variant} model={(server ? $"server day, sentences counted down {countdownsPerDay} a day" : daily ? "prison day only" : "no day")} hours={ticks / 120.0:F1} gameYears={ticks / 120.0 / 9.6:F1}");
        sb.AppendLine($"start: alive={start.Count} teamed={start.Count(n => !string.IsNullOrEmpty(n.Team))} avgLevel={start.Average(n => n.Level):F1} below16={start.Count(n => n.Level <= 15)} pool={start.Count(WorldSimulator.IsRecruitPoolMember)} below16OnNpcTeams={start.Count(n => n.Level <= 15 && !string.IsNullOrEmpty(n.Team))}");
        SetSwitch(on);

        var arrivalField = typeof(WorldSimulator).GetField("_lastRecruitPoolArrivalTick", BindingFlags.Instance | BindingFlags.NonPublic)!;
        var wasMember = new HashSet<NPC>(w.Npcs.Where(WorldSimulator.IsRecruitPoolMember));
        var everKnown = new HashSet<NPC>(w.Npcs);
        var away = new HashSet<NPC>();   // members waiting for a respawn or in prison: they come back
        long poolSum = 0, aliveSum = 0; var bandSum = new long[3];
        int poolMin = int.MaxValue, poolMax = 0, aliveMin = int.MaxValue, aliveMax = 0, below12Ticks = 0;
        int over218Ticks = 0, over200Ticks = 0, withWaitingMax = 0, waitingAtAliveMax = 0, arrivalsAt200Up = 0; long waitingSum = 0;
        int arrivals = 0, absorbed = 0, levelled = 0, oldAge = 0, permaDead = 0, pruned = 0, otherIn = 0, backIn = 0, wentAway = 0, hired = 0;
        var oldAgeByRace = new SortedDictionary<string, int>();
        var oldAgeAllByRace = new SortedDictionary<string, int>();
        var lowTeamedAtStart = start.Where(n => n.Level <= 15 && !string.IsNullOrEmpty(n.Team)).ToList();
        var childrenSeen = new HashSet<Child>(FamilySystem.Instance.AllChildren);
        int releasedTotal = 0, releasedFirstTick = 0, lowBandBelow3Ticks = 0;
        // by quarter of the run (a game decade in row 12)
        var qPoolMin = new int[] { int.MaxValue, int.MaxValue, int.MaxValue, int.MaxValue }; var qBelow12 = new int[4]; var qLowBand = new int[4];
        var qBand = new long[4, 3]; var qAliveMax = new int[4]; var qAt190 = new int[4]; var qPerma = new int[4];
        var qTeams = new string[4]; int permaSeenBefore = 0;
        var qTicks = new int[4]; var qAt200 = new int[4]; var qAlive = new long[4]; var qPool = new long[4];
        var qPreg = new int[4]; var qBirths = new int[4]; var qOfAge = new int[4]; var qOrphans = new int[4];
        var qArrivals = new int[4]; var qAbsorbed = new int[4]; var qParkedAtEnd = new int[4]; var qCrossings = new int[4];
        bool wasAt200 = false; int longestAt200 = 0, runAt200 = 0;
        int at200Ticks = 0, at190Ticks = 0, pregnancies = 0, childrenOfAge = 0, permaDeaths = 0, oldAgeDeaths = 0;
        // pregnancies by the living population when they started: below 120, 120 to 159, 160 to 199 (the rule's bands), 200 and more
        var bandTicks = new long[4]; var bandPregnancies = new int[4]; var bandWomen = new long[4];
        int BandOfTown(int living) => living < 120 ? 0 : living < 160 ? 1 : living < 200 ? 2 : 3;
        var pregnantBefore = new HashSet<NPC>(w.Npcs.Where(n => n.PregnancyDueDate.HasValue));
        var convertedSeen = new HashSet<Child>(FamilySystem.Instance.AllChildren.Where(c => c.Deleted));
        var goneSeen = new HashSet<NPC>(w.Npcs.Where(n => n.IsPermaDead || n.IsAgedDeath));
        var jailedBySentence = new SortedDictionary<int, int>(); int jailedLow = 0, jailedTeamed = 0, kingChanges = 0; long prisonSum = 0;
        int births = 0, ordinaryArrivals = 0, cameOfAge = 0, ticksOver218WithWaiting = 0;
        var agedSeen = new HashSet<NPC>(w.Npcs.Where(n => n.IsAgedDeath));
        var series = new List<string>();
        int every = Math.Max(1, ticks / 20);

        for (int t = 0; t < ticks; t++)
        {
            int tickBefore = w.Tick;
            w.Elapse(tick30);
            int aliveBefore = w.Alive().Count;
            var before = new HashSet<NPC>(w.Npcs);
            var lowTeamedBefore = w.Npcs.Where(n => n.Level <= 15 && !string.IsNullOrEmpty(n.Team) && !WorldSimulator.IsPlayerTeam(n.Team)).ToList();
            newsOfTheTick.Clear();
            if (daily && t % 2880 == 2879) PrisonActivitySystem.Instance.ProcessDailyPrisonCountdown();
            var freeBefore = w.Npcs.Where(n => n.DaysInPrison <= 0).ToList();
            string kingBefore = CastleLocation.GetCurrentKing()?.Name ?? "";
            ServerClock(tick30);
            w.Step();
            ServerDay();
            foreach (var n in freeBefore.Where(n => n.DaysInPrison > 0))
            {
                jailedBySentence[n.DaysInPrison] = jailedBySentence.GetValueOrDefault(n.DaysInPrison) + 1;
                if (n.Level <= 15) jailedLow++;
                if (!string.IsNullOrEmpty(n.Team)) jailedTeamed++;
            }
            string kingNow = CastleLocation.GetCurrentKing()?.Name ?? "";
            if (kingNow != kingBefore) kingChanges++;
            prisonSum += w.Alive().Count(n => n.DaysInPrison > 0);
            int releasedNow = lowTeamedBefore.Count(n => string.IsNullOrEmpty(n.Team) && !newsOfTheTick.Any(l => l.Contains(n.Name2)));
            releasedTotal += releasedNow;
            if (t == 0) releasedFirstTick = releasedNow;
            if (t == 0)
            {
                var aliveNow = w.Alive();
                var npcTeamsNow = aliveNow.Where(n => !string.IsNullOrEmpty(n.Team) && !WorldSimulator.IsPlayerTeam(n.Team)).GroupBy(n => n.Team).ToList();
                sb.AppendLine($"after the first tick (as run): released={releasedFirstTick} of {lowTeamedAtStart.Count} low-level members of NPC teams, pool={aliveNow.Count(WorldSimulator.IsRecruitPoolMember)}, npcTeams={npcTeamsNow.Count}, below16StillOnNpcTeams={aliveNow.Count(n => n.Level <= 15 && !string.IsNullOrEmpty(n.Team))}");
            }
            bool poolArrival = (int)arrivalField.GetValue(w.Sim)! == tickBefore;
            if (poolArrival) { arrivals++; if (aliveBefore >= 200) arrivalsAt200Up++; }
            // every other newcomer: a child or orphan come of age (level 1, not an immigrant id), or an ordinary immigrant
            var newcomers = w.Npcs.Where(n => !before.Contains(n)).ToList();
            int immigrants = newcomers.Count(n => (n.ID ?? "").StartsWith("npc_imm_"));
            ordinaryArrivals += immigrants - (poolArrival ? 1 : 0);
            cameOfAge += newcomers.Count - immigrants;
            int birthsAtTickStart = births;
            foreach (var c in FamilySystem.Instance.AllChildren) if (childrenSeen.Add(c)) births++;
            int birthsThisTick = births - birthsAtTickStart;
            int cameOfAgeThisTick = newcomers.Count - immigrants;
            int absorbedAtTickStart = absorbed;

            var members = new HashSet<NPC>(w.Npcs.Where(WorldSimulator.IsRecruitPoolMember));
            foreach (var n in wasMember.Where(n => !members.Contains(n)))
            {
                if (n.IsAgedDeath) { oldAge++; oldAgeByRace[n.Race.ToString()] = oldAgeByRace.GetValueOrDefault(n.Race.ToString()) + 1; }
                else if (n.IsPermaDead) permaDead++;
                else if (!w.Npcs.Contains(n)) pruned++;
                else if (!string.IsNullOrEmpty(n.Team)) { if (WorldSimulator.IsPlayerTeam(n.Team)) hired++; else absorbed++; }
                else if (n.Level > 15) levelled++;
                else { wentAway++; away.Add(n); }   // dead and waiting for the respawn, or in prison
            }
            foreach (var n in members.Where(n => !wasMember.Contains(n)))
            {
                if (away.Remove(n)) backIn++;
                else if (everKnown.Add(n)) { /* a newcomer: a pool arrival (counted above), or see below */ }
                else otherIn++;   // left a team, or came out of prison
            }
            int absorbedThisTick = absorbed - absorbedAtTickStart;
            foreach (var n in w.Npcs) everKnown.Add(n);
            foreach (var n in w.Npcs.Where(n => n.IsAgedDeath && agedSeen.Add(n)))
                oldAgeAllByRace[n.Race.ToString()] = oldAgeAllByRace.GetValueOrDefault(n.Race.ToString()) + 1;
            wasMember = members;

            int alive = w.Alive().Count, pool = members.Count;
            var bands = new int[3];
            foreach (var n in members) bands[WorldSimulator.RecruitPoolBandOf(n.Level)]++;
            poolSum += pool; aliveSum += alive;
            for (int b = 0; b < 3; b++) bandSum[b] += bands[b];
            if (pool < 12) below12Ticks++;
            if (bands[0] < 3) lowBandBelow3Ticks++;
            int waiting = w.Npcs.Count(n => (!n.IsAlive || n.IsDead) && !n.IsAgedDeath && !n.IsPermaDead);   // dead, respawn due
            waitingSum += waiting;
            if (alive > aliveMax) waitingAtAliveMax = waiting;   // aliveMax is raised below, after this line
            if (alive + waiting > 218) ticksOver218WithWaiting++;
            int q = Math.Min(3, (int)((long)t * 4 / ticks));
            qTicks[q]++; qAlive[q] += alive; qPool[q] += pool;
            if (alive >= 200) { qAt200[q]++; runAt200++; longestAt200 = Math.Max(longestAt200, runAt200); } else runAt200 = 0;
            if ((alive >= 200) != wasAt200) { qCrossings[q]++; wasAt200 = alive >= 200; }
            qPoolMin[q] = Math.Min(qPoolMin[q], pool); if (pool < 12) qBelow12[q]++; if (bands[0] < 3) qLowBand[q]++;
            for (int b = 0; b < 3; b++) qBand[q, b] += bands[b];
            qAliveMax[q] = Math.Max(qAliveMax[q], alive); if (alive >= 190) qAt190[q]++;
            int birthsBeforeQ = births, ofAgeBeforeQ = childrenOfAge;
            if (alive >= 200) at200Ticks++;
            if (alive >= 190) at190Ticks++;
            var pregnantNow = new HashSet<NPC>(w.Npcs.Where(n => n.PregnancyDueDate.HasValue));
            int started = pregnantNow.Count(n => !pregnantBefore.Contains(n));
            pregnancies += started;
            qPreg[q] += started;
            int townBand = BandOfTown(aliveBefore);
            bandTicks[townBand]++; bandPregnancies[townBand] += started;
            bandWomen[townBand] += w.Npcs.Count(n => n.IsAlive && !n.IsDead && n.Sex == CharacterSex.Female && n.Age >= 18 && n.Age <= 45);
            pregnantBefore = pregnantNow;
            foreach (var c in FamilySystem.Instance.AllChildren) if (c.Deleted && convertedSeen.Add(c)) childrenOfAge++;
            qOfAge[q] += childrenOfAge - ofAgeBeforeQ;
            qBirths[q] += birthsThisTick;
            qOrphans[q] += Math.Max(0, cameOfAgeThisTick - (childrenOfAge - ofAgeBeforeQ));
            if (poolArrival) qArrivals[q]++;
            qAbsorbed[q] += absorbedThisTick;
            qPerma[q] += permaDeaths - permaSeenBefore; permaSeenBefore = permaDeaths;
            if (t == (long)(q + 1) * ticks / 4 - 1)
            {
                var living = w.Alive();
                var tg = living.Where(n => !string.IsNullOrEmpty(n.Team)).GroupBy(n => n.Team).ToList();
                int teamMembers = tg.Sum(g => g.Count()), lowMembers = living.Count(n => n.Level <= 15 && !string.IsNullOrEmpty(n.Team));
                int freeLow = living.Count(n => n.Level <= 15 && string.IsNullOrEmpty(n.Team));
                qTeams[q] = $"npcTeams={tg.Count} avgSize={(tg.Count == 0 ? 0 : (double)teamMembers / tg.Count):F2} membersBelow16={lowMembers} of {teamMembers} ({(teamMembers == 0 ? 0 : 100.0 * lowMembers / teamMembers):F1}%) freeBelow16={freeLow} poolEnd={living.Count(WorldSimulator.IsRecruitPoolMember)} below16={living.Count(n => n.Level <= 15)}";
            }
            if (t == (long)(q + 1) * ticks / 4 - 1) qParkedAtEnd[q] = FamilySystem.Instance.AllChildren.Count(c => !c.Deleted && c.Location == GameConfig.ChildLocationAway);
            foreach (var n in w.Npcs) if ((n.IsPermaDead || n.IsAgedDeath) && goneSeen.Add(n)) { if (n.IsAgedDeath) oldAgeDeaths++; else permaDeaths++; }
            if (alive > 218) over218Ticks++;
            if (alive > 200) over200Ticks++;
            withWaitingMax = Math.Max(withWaitingMax, alive + waiting);
            poolMin = Math.Min(poolMin, pool); poolMax = Math.Max(poolMax, pool);
            aliveMin = Math.Min(aliveMin, alive); aliveMax = Math.Max(aliveMax, alive);
            if ((t + 1) % every == 0)
                series.Add($"| {t + 1} | {(t + 1) / 120.0 / 9.6:F1} | {pool} | {bands[0]} | {bands[1]} | {bands[2]} | {alive} | {w.Alive().Count(n => !string.IsNullOrEmpty(n.Team))} | {w.Npcs.Count(n => n.IsAgedDeath || n.IsPermaDead)} | {FamilySystem.Instance.AllChildren.Count} |");
        }

        var end = w.Alive();
        var endBands = new int[3];
        foreach (var n in wasMember) endBands[WorldSimulator.RecruitPoolBandOf(n.Level)]++;
        sb.AppendLine($"pool: min={poolMin} mean={(double)poolSum / ticks:F2} max={poolMax} final={wasMember.Count} ticksBelow12={below12Ticks} ({100.0 * below12Ticks / ticks:F1}%)");
        sb.AppendLine($"band 1-5 below 3: {lowBandBelow3Ticks} ticks ({100.0 * lowBandBelow3Ticks / ticks:F1}%)");
        sb.AppendLine($"pool per band (mean; final): {string.Join(", ", Enumerable.Range(0, 3).Select(b => $"{BandNames[b]}: {(double)bandSum[b] / ticks:F2}; {endBands[b]}"))}");
        sb.AppendLine($"alive: min={aliveMin} mean={(double)aliveSum / ticks:F1} max={aliveMax} final={end.Count}");
        sb.AppendLine($"alive above 218: {over218Ticks} ticks ({100.0 * over218Ticks / ticks:F1}%); above 200: {over200Ticks} ticks ({100.0 * over200Ticks / ticks:F1}%)");
        sb.AppendLine($"dead and waiting for the respawn (not counted as alive by any cap): mean={(double)waitingSum / ticks:F1}, at the alive maximum={waitingAtAliveMax}; alive plus waiting, max={withWaitingMax}; ticks with alive plus waiting above 218: {ticksOver218WithWaiting} ({100.0 * ticksOver218WithWaiting / ticks:F1}%)");
        sb.AppendLine($"ordinary immigrants={ordinaryArrivals} births={births} cameOfAge(children, orphans)={cameOfAge}");
        var kids = FamilySystem.Instance.AllChildren.Where(c => !c.Deleted).ToList();
        var kingNow2 = CastleLocation.GetCurrentKing();
        string[] townBands = { "below 120", "120 to 159", "160 to 199", "200 and more" };
        sb.AppendLine("pregnancy rate by living population: " + string.Join("; ", Enumerable.Range(0, 4).Select(b => $"{townBands[b]}: ticks={bandTicks[b]} started={bandPregnancies[b]} per 1,000 ticks={(bandTicks[b] == 0 ? 0 : 1000.0 * bandPregnancies[b] / bandTicks[b]):F2} women of 18 to 45 (mean)={(bandTicks[b] == 0 ? 0 : (double)bandWomen[b] / bandTicks[b]):F1}")));
        for (int q = 0; q < 4; q++)
            sb.AppendLine($"quarterB {q + 1}: poolMin={qPoolMin[q]} bandMean={(double)qBand[q, 0] / qTicks[q]:F2},{(double)qBand[q, 1] / qTicks[q]:F2},{(double)qBand[q, 2] / qTicks[q]:F2} below12={100.0 * qBelow12[q] / qTicks[q]:F1}% lowBandBelow3={100.0 * qLowBand[q] / qTicks[q]:F1}% livingMax={qAliveMax[q]} at190={100.0 * qAt190[q] / qTicks[q]:F1}% permaDeaths={qPerma[q]} {qTeams[q]}");
        for (int q = 0; q < 4; q++)
            sb.AppendLine($"quarter {q + 1}: ticks={qTicks[q]} at200={100.0 * qAt200[q] / qTicks[q]:F1}% crossings={qCrossings[q]} livingMean={(double)qAlive[q] / qTicks[q]:F1} pregnancies={qPreg[q]} births={qBirths[q]} childrenOfAge={qOfAge[q]} orphansOfAge={qOrphans[q]} parkedAtEnd={qParkedAtEnd[q]} poolMean={(double)qPool[q] / qTicks[q]:F2} arrivals={qArrivals[q]} absorbed={qAbsorbed[q]}");
        sb.AppendLine($"at 200 or more: longest unbroken stretch={longestAt200} ticks ({longestAt200 / 120.0 / 9.6:F1} game years); crossings of 200 in all={qCrossings.Sum()}");
        sb.AppendLine($"town: living mean={(double)aliveSum / ticks:F1}; ticks with living at or above 190 = {at190Ticks} ({100.0 * at190Ticks / ticks:F1}%)");
        sb.AppendLine($"gates: ticks with living at or above 200 = {at200Ticks} ({100.0 * at200Ticks / ticks:F1}%); pregnancies started={pregnancies}; births={births}; children come of age={childrenOfAge}; orphans come of age={Math.Max(0, cameOfAge - childrenOfAge)}; children parked away at the end={kids.Count(c => c.Location == GameConfig.ChildLocationAway)}; children not yet 18 at the end={kids.Count(c => c.Age < 18)}; orphans in the orphanage at the end={kingNow2?.Orphans.Count ?? 0}; ordinary immigrants={ordinaryArrivals}; permanent deaths={permaDeaths}; old age deaths={oldAgeDeaths}; living at the end={w.Alive().Count}; mean age at the end={w.Alive().Average(n => (double)n.Age):F1}");
        sb.AppendLine($"pool arrivals={arrivals} (most possible {ticks / 20}), of them with 200 or more alive={arrivalsAt200Up}");
        sb.AppendLine($"left the pool: absorbedByNpcTeams={absorbed} hiredByPlayers={hired} levelledPast15={levelled} diedOfOldAge={oldAge} diedPermanently={permaDead} prunedCorpse={pruned}");
        sb.AppendLine($"away and back: wentAway(respawn or prison)={wentAway} cameBack={backIn} otherEntries(left a team, level set)={otherIn}");
        sb.AppendLine($"old age among pool members by race: {(oldAgeByRace.Count == 0 ? "none" : string.Join(", ", oldAgeByRace.Select(kv => $"{kv.Key} {kv.Value}")))}");
        sb.AppendLine($"old age among all NPCs by race: {(oldAgeAllByRace.Count == 0 ? "none" : string.Join(", ", oldAgeAllByRace.Select(kv => $"{kv.Key} {kv.Value}")))}");
        sb.AppendLine($"end: alive={end.Count} teamed={end.Count(n => !string.IsNullOrEmpty(n.Team))} avgLevel={end.Average(n => n.Level):F1} below16={end.Count(n => n.Level <= 15)} npcRecords={w.Npcs.Count}");
        var teams = end.Where(n => !string.IsNullOrEmpty(n.Team)).GroupBy(n => n.Team).ToList();
        var lowEnd = end.Where(n => n.Level <= 15).ToList();
        sb.AppendLine($"below 16 at the end, by state: poolMembers={lowEnd.Count(WorldSimulator.IsRecruitPoolMember)} onATeam={lowEnd.Count(n => !string.IsNullOrEmpty(n.Team))} inPrisonUnteamed={lowEnd.Count(n => string.IsNullOrEmpty(n.Team) && n.DaysInPrison > 0)} inPrisonAll={end.Count(n => n.DaysInPrison > 0)} (every level) special={lowEnd.Count(n => string.IsNullOrEmpty(n.Team) && n.IsSpecialNPC)} immortal={lowEnd.Count(n => string.IsNullOrEmpty(n.Team) && n.IsImmortal)} levelZeroOrLess={lowEnd.Count(n => n.Level < 1)}");
        int jailedAll = jailedBySentence.Values.Sum();
        double years = ticks / 120.0 / 9.6, days = ticks / 2880.0;
        sb.AppendLine($"prison: mean in prison={(double)prisonSum / ticks:F1}; jailed over the run={jailedAll} ({jailedAll / days:F1} a server day, {jailedAll / years:F0} a game year), by sentence in days: {string.Join(", ", jailedBySentence.Select(kv => $"{kv.Key}d x{kv.Value}"))}; of them below level 16: {jailedLow}, on a team when jailed: {jailedTeamed}");
        var kingEnd = CastleLocation.GetCurrentKing();
        sb.AppendLine($"court: king at the end={(kingEnd == null ? "none" : kingEnd.Name)} changes of king={kingChanges} court prisoner records at the end={kingEnd?.Prisoners.Count ?? 0}; server days run={daysRun}; daily reset errors={dailyErrors.Count}{(dailyErrors.Count > 0 ? " (" + dailyErrors[0] + ")" : "")}");
        int teamedEnd = end.Count(n => !string.IsNullOrEmpty(n.Team));
        int teamedLowEnd = end.Count(n => n.Level <= 15 && !string.IsNullOrEmpty(n.Team));
        sb.AppendLine($"released over the run={releasedTotal}; NPC team members below 16 at the end: {teamedLowEnd} of {teamedEnd} ({(teamedEnd == 0 ? 0 : 100.0 * teamedLowEnd / teamedEnd):F1}%)");
        sb.AppendLine($"NPC teams at the end: {teams.Count}, average size {(teams.Count == 0 ? 0 : teams.Average(g => g.Count())):F2} (living members); below 16 on NPC teams at the end: {end.Count(n => n.Level <= 15 && !string.IsNullOrEmpty(n.Team))}");
        sb.AppendLine($"the {lowTeamedAtStart.Count} below 16 on NPC teams at the start: still teamed and below 16={lowTeamedAtStart.Count(n => w.Alive().Contains(n) && n.Level <= 15 && !string.IsNullOrEmpty(n.Team))}, levelled to 16 or more={lowTeamedAtStart.Count(n => n.Level > 15)}, left the team={lowTeamedAtStart.Count(n => string.IsNullOrEmpty(n.Team) && !n.IsAgedDeath && !n.IsPermaDead)}, dead for good={lowTeamedAtStart.Count(n => n.IsAgedDeath || n.IsPermaDead)}");
        sb.AppendLine("| tick | game years | pool | 1-5 | 6-10 | 11-15 | alive | teamed | corpses kept | children |");
        sb.AppendLine("|---|---|---|---|---|---|---|---|---|---|");
        foreach (var line in series) sb.AppendLine(line);
        sb.AppendLine($"digest: {w.Digest()}");
        if (server)
        {
            OnlineStateManager.SimCourtStore = null;
            Microsoft.Data.Sqlite.SqliteConnection.ClearAllPools();
            try { File.Delete(dbPath!); } catch { }
        }
        return sb.ToString();
    }
}
