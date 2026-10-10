using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using FluentAssertions;
using Microsoft.Data.Sqlite;
using UsurperRemake.Systems;
using Xunit;

namespace UsurperReborn.Tests;

/// <summary>
/// v1.2.8 (F5), row 7: the refresh of the player team list draws from no random. A seeded world of the classic
/// roster with one player team runs 2,000 ticks twice: once with the team registered by a session only (what
/// the base commit does when its list is complete) and once with the list also refreshed from a store before
/// every tick. The two runs must agree draw for draw. This file names nothing the base commit lacks (the
/// refresh is reached by reflection), so the same file produces the stored golden on the base commit.
/// </summary>
[Collection("SharedGameSingletons")]
public class PlayerTeams128GoldenTests
{
    [Fact]
    public void Row7_TwinWorlds_TheRefreshedWorld_EqualsTheSessionOnlyWorld_DrawForDraw()
    {
        string plain = "", refreshed = "";
        for (int attempt = 0; attempt < 3; attempt++)
        {
            plain = IsolatedGame.Run(nameof(RecruitPoolRuns.TeamWorld), "4321,2000,session");
            refreshed = IsolatedGame.Run(nameof(RecruitPoolRuns.TeamWorld), "4321,2000,refresh");
            // the world reads the clock hour: a pair that crossed 06, 12, 18 or 23 is run again
            if (!plain.StartsWith("straddle") && plain.Split(' ')[0] == refreshed.Split(' ')[0]) break;
        }
        refreshed.Should().Contain("refreshes=2000", "the refresh ran before every tick");
        plain.Should().Contain("refreshes=0");
        refreshed.Should().Contain("hall=True");
        refreshed.Replace("refreshes=2000", "refreshes=0").Should().Be(plain);
    }

    /// <summary>
    /// The opt-in golden (F5_GOLDEN set): the refreshed world against the digests the BASE commit b07b3596 gave
    /// for the session-only world, in each part of the day. It reads the wall clock, so it is not in the
    /// always-run suite; the four parts are taken by running it under four time zones.
    /// </summary>
    [Fact]
    public void Row7_Golden_TheRefreshedWorld_EqualsTheBaseCommit()
    {
        var golden = new Dictionary<string, string>
        {
            ["morning"] = "shared=b8e137655c94b9f0 npcs=116 alive=101 roster=f890ef442aa80e6b simDraws=420497 simHash=dfa9bfefd2b23c58 spawnDraws=3608 spawnHash=ab248d028dbb2a48 hall=True",
            ["afternoon"] = "shared=9ac337551b91ec62 npcs=109 alive=93 roster=388969b2426d7b75 simDraws=400098 simHash=d7f19da093d368cd spawnDraws=3495 spawnHash=5a835b77b04917b6 hall=True",
            ["evening"] = "shared=58a0cc864a77d4e3 npcs=105 alive=90 roster=569b8cd794bb4591 simDraws=382081 simHash=9cd50544c915589e spawnDraws=3436 spawnHash=d70861556191b21f hall=True",
            ["night"] = "shared=de5b2b052c1d2868 npcs=108 alive=92 roster=83dbb0f08f6ca22e simDraws=403703 simHash=618f75572bd1574e spawnDraws=3481 spawnHash=0976207238d5930e hall=True",
        };
        if (string.IsNullOrEmpty(Environment.GetEnvironmentVariable("F5_GOLDEN"))) return;
        string mode = Environment.GetEnvironmentVariable("F5_GOLDEN_MODE") ?? "refresh";
        string got = "";
        for (int attempt = 0; attempt < 3; attempt++)
        {
            got = IsolatedGame.Run(nameof(RecruitPoolRuns.TeamWorld), "4321,2000," + mode);
            if (!got.StartsWith("straddle")) break;
        }
        if (Environment.GetEnvironmentVariable("F5_GOLDEN_OUT") is string receipt && receipt.Length > 0) File.AppendAllText(receipt, got + "\n");
        if (mode != "refresh") return;   // the base commit only writes its line
        string part = got.Split(' ')[0];
        golden.Should().ContainKey(part);
        got.Replace(" refreshes=2000", "").Should().Be(part + " " + golden[part]);
    }
}

internal static partial class RecruitPoolRuns
{
    /// <summary>
    /// "seed,ticks,mode": the classic world with the player team "Twin Hall" (two hires taken by name order).
    /// mode "session": the team is registered as a session does, and nothing else. mode "refresh": the same,
    /// and a store that names the team is read before every tick (WorldSimService.RefreshPlayerTeamNames).
    /// </summary>
    public static string TeamWorld(string arg)
    {
        var a = arg.Split(',');
        int seed = int.Parse(a[0]), ticks = int.Parse(a[1]);
        bool refresh = a[2] == "refresh";
        SilenceNews();
        int h0 = DateTime.Now.Hour;
        string path = Path.Combine(Path.GetTempPath(), $"usurper-f5g-{Guid.NewGuid():N}.db");
        int refreshes = 0;
        try
        {
            using var w = new RecruitPoolWorld(seed);
            foreach (var hire in w.Npcs.Where(n => string.IsNullOrEmpty(n.Team) && n.IsAlive && !n.King)
                         .OrderBy(n => n.Name2, StringComparer.Ordinal).Take(2))
            {
                hire.Team = "Twin Hall";
                hire.TeamPW = "pw";
            }
            WorldSimulator.RegisterPlayerTeam("Twin Hall");
            Func<bool>? read = null;
            if (refresh)
            {
                var db = new SqlSaveBackend(path);
                using (var conn = new SqliteConnection($"Data Source={path}"))
                {
                    conn.Open();
                    using var cmd = conn.CreateCommand();
                    cmd.CommandText = "INSERT INTO players (username, display_name, player_data) VALUES ('twin player', 'Twin Player', '{\"player\":{\"level\":10,\"team\":\"Twin Hall\"}}');";
                    cmd.ExecuteNonQuery();
                }
                var service = new WorldSimService(db);
                var method = typeof(WorldSimService).GetMethod("RefreshPlayerTeamNames", BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public)!;
                read = () => (bool)method.Invoke(service, null)!;
            }
            for (int i = 0; i < ticks; i++)
            {
                if (read != null) { read().Should().BeTrue(); refreshes++; }
                w.Step();
            }
            int h1 = DateTime.Now.Hour;
            string mark = DayPart(h0) != DayPart(h1) ? "straddle" : DayPart(h0);
            return $"{mark} {w.Digest()} hall={WorldSimulator.IsPlayerTeam("Twin Hall")} refreshes={refreshes}";
        }
        finally
        {
            SqliteConnection.ClearAllPools();
            try { File.Delete(path); } catch { }
        }
    }
}
