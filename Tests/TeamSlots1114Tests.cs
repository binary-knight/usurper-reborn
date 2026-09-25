using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using FluentAssertions;
using UsurperRemake;
using UsurperRemake.Systems;
using Xunit;

namespace UsurperReborn.Tests;

/// <summary>
/// v1.1.14: every path that adds a member to a team keeps the five-slot limit: the street gang join, the
/// Team Corner hire, and an NPC joining a team on its own (world sim, world creation, gang maintenance).
/// </summary>
[Collection("SharedGameSingletons")]
public class TeamSlots1114Tests
{
    private const BindingFlags F = BindingFlags.NonPublic | BindingFlags.Instance;

    private static List<NPC> Crew(string prefix, string team, int n, int dead = 0) =>
        Enumerable.Range(1, n).Select(i => TeamCornerRig.Npc($"{prefix}_{i}", $"{prefix} Member {i}", team, dead: i > n - dead)).ToList();

    private static async Task<(string shown, EncounterResult result)> StreetGang(Character hero, string team, params string[] lines)
    {
        var teams = WorldInitializerSystem.Instance.ActiveTeams;
        var saved = teams.ToList();
        teams.Clear();
        teams.Add(new WorldInitializerSystem.TeamRecord { Name = team, MemberNames = new List<string> { "Founder", "Second" } });
        var output = new MemoryStream();
        var term = new TerminalEmulator(new LineStream(lines), output);
        var result = new EncounterResult();
        try
        {
            var m = typeof(StreetEncounterSystem).GetMethod("ProcessGangEncounter", F)!;
            await (Task)m.Invoke(StreetEncounterSystem.Instance, new object[] { hero, result, term })!;
        }
        finally { teams.Clear(); teams.AddRange(saved); }
        term.StreamWriterInternal?.Flush();
        return (Regex.Replace(Encoding.UTF8.GetString(output.ToArray()), "\u001b\\[[0-9;]*[A-Za-z]", ""), result);
    }

    private static async Task WithNpcs(List<NPC> npcs, Func<Task> body)
    {
        NPCSpawnSystem.Instance.ActiveNPCs.AddRange(npcs);
        try { await body(); }
        finally { foreach (var n in npcs) NPCSpawnSystem.Instance.ActiveNPCs.Remove(n); }
    }

    [Fact]
    public async Task StreetGang_AFullTeam_IsRefused_ThoughItsFoundingRecordShowsRoom()
    {
        // four living members and a dead one who holds a slot; the founding record lists two
        await WithNpcs(Crew("tsl_full", "Alley Cats", 5, dead: 1), async () =>
        {
            var hero = TeamCornerRig.Hero(name: "Street Joiner");
            var (shown, _) = await StreetGang(hero, "Alley Cats", "J", "", "", "");
            shown.Should().Contain(Loc.Get("team.join_team_full", "Alley Cats", 5));
            hero.Team.Should().BeEmpty();
        });
    }

    [Fact]
    public async Task StreetGang_AFreeSlot_Joins()
    {
        await WithNpcs(Crew("tsl_room", "Alley Cats", 4), async () =>
        {
            var hero = TeamCornerRig.Hero(name: "Street Joiner");
            var (shown, _) = await StreetGang(hero, "Alley Cats", "J", "", "", "");
            shown.Should().Contain(Loc.Get("street_encounter.gang.welcome", "Alley Cats"));
            hero.Team.Should().Be("Alley Cats");
            WorldSimulator.UnregisterPlayerTeam("Alley Cats");
        });
    }

    [Fact]
    public async Task StreetGang_AKing_IsRefused()
    {
        await WithNpcs(Crew("tsl_king", "Alley Cats", 2), async () =>
        {
            var hero = TeamCornerRig.Hero(name: "Street King");
            hero.King = true;
            var (shown, _) = await StreetGang(hero, "Alley Cats", "J", "", "", "");
            shown.Should().Contain(Loc.Get("team.king_cannot_join"));
            hero.Team.Should().BeEmpty();
        });
    }

    [Fact]
    public async Task StreetGang_Online_TakesTheSlotThroughTheSaves()
    {
        var saved = UsurperRemake.Server.SessionContext.Current;
        try
        {
            await WithNpcs(Crew("tsl_onl", "Alley Cats", 3), async () =>
            {
                await TeamCornerRig.Online(async (db, path) =>
                {
                    TeamCornerRig.PlayerRow(path, "catone", "Alley Cats");
                    TeamCornerRig.PlayerRow(path, "streeta", "");
                    TeamCornerRig.PlayerRow(path, "streetb", "");
                    async Task<Character> Join(string key)
                    {
                        UsurperRemake.Server.SessionContext.Current = new UsurperRemake.Server.SessionContext
                            { InputStream = Stream.Null, OutputStream = Stream.Null, Username = key, CharacterKey = key };
                        var hero = TeamCornerRig.Hero(name: char.ToUpper(key[0]) + key.Substring(1));
                        await StreetGang(hero, "Alley Cats", "J", "", "", "");
                        return hero;
                    }
                    (await Join("streeta")).Team.Should().Be("Alley Cats", "three NPCs and one player leave a slot");
                    Convert.ToString(TeamCornerRig.Scalar(path, "SELECT json_extract(player_data, '$.player.team') FROM players WHERE username = 'streeta';"))
                        .Should().Be("Alley Cats", "the claim writes the membership into the save");
                    (await Join("streetb")).Team.Should().BeEmpty("the first joiner took the last slot, before any full save");
                    WorldSimulator.UnregisterPlayerTeam("Alley Cats");
                });
            });
        }
        finally { UsurperRemake.Server.SessionContext.Current = saved; }
    }

    [Fact]
    public async Task Hire_WaitsForTheGate_AndCountsAgainUnderIt()
    {
        var crew = Crew("tsl_hire", "Hire Hall", 3);
        var recruit = TeamCornerRig.Npc("tsl_hire_new", "Free Sword", "");
        recruit.TeamPW = "";
        var late = TeamCornerRig.Npc("tsl_hire_late", "Late Sword", "Hire Hall");
        crew.Add(recruit);
        await WithNpcs(crew, async () =>
        {
            var hero = TeamCornerRig.Hero(name: "Hall Boss", team: "Hire Hall", gold: 1_000_000);
            var rig = new TeamCornerRig(hero, new[] { "Y", "", "", "" });
            var mi = typeof(TeamCornerLocation).GetMethod("ConfirmAndRecruit", F)!;
            Task hire;
            await TeamCornerLocation.TeamMembershipGate.WaitAsync();
            try
            {
                hire = (Task)mi.Invoke(rig.Loc, new object[] { recruit, TeamSystem.RecruitmentBand.Neutral, 1.0, 100L })!;
                // the hire passes its early check (four of five) and stops at the gate
                for (int i = 0; i < 100 && !hire.IsCompleted; i++) await Task.Delay(20);
                hire.IsCompleted.Should().BeFalse("the hire waits for the gate; transcript: " + rig.Shown());
                // a fifth member joins while the hire waits
                NPCSpawnSystem.Instance.ActiveNPCs.Add(late);
            }
            finally { TeamCornerLocation.TeamMembershipGate.Release(); }
            try
            {
                await hire;
                rig.Shown().Should().Contain(Loc.Get("team.team_full", 5));
                recruit.Team.Should().BeEmpty("the team filled while the hire waited");
                hero.Gold.Should().Be(1_000_000, "no fee for a hire that did not happen");
            }
            finally { NPCSpawnSystem.Instance.ActiveNPCs.Remove(late); }
        });
    }

    [Fact]
    public void NpcJoin_TheDeadHoldTheirSlots()
    {
        var npcs = Crew("tsl_dead", "Bone Club", 5, dead: 2);
        var joiner = TeamCornerRig.Npc("tsl_dead_j", "Joiner", "");
        npcs.Add(joiner);
        TeamCornerLocation.TryNpcJoin(npcs, "Bone Club", () => joiner.Team = "Bone Club").Should().BeFalse("three alive and two dead fill five slots");
        joiner.Team.Should().BeEmpty();
        npcs.RemoveAt(4);
        TeamCornerLocation.TryNpcJoin(npcs, "Bone Club", () => joiner.Team = "Bone Club").Should().BeTrue();
        joiner.Team.Should().Be("Bone Club");
    }

    [Fact]
    public void NpcJoin_LeavesAPlayerTeamAlone_AndNeverWaitsForTheGate()
    {
        var npcs = Crew("tsl_pt", "Player Hall", 1);
        bool ran = false;
        WorldSimulator.RegisterPlayerTeam("Player Hall");
        try { TeamCornerLocation.TryNpcJoin(npcs, "Player Hall", () => ran = true).Should().BeFalse(); }
        finally { WorldSimulator.UnregisterPlayerTeam("Player Hall"); }
        TeamCornerLocation.TeamMembershipGate.Wait();
        try { TeamCornerLocation.TryNpcJoin(npcs, "Player Hall", () => ran = true).Should().BeFalse("a join or a hire holds the gate"); }
        finally { TeamCornerLocation.TeamMembershipGate.Release(); }
        ran.Should().BeFalse();
        TeamCornerLocation.TryNpcJoin(npcs, "Player Hall", () => ran = true).Should().BeTrue();
    }

    [Fact]
    public async Task NpcJoin_Online_CountsThePlayerMembersInTheSaves()
    {
        var npcs = Crew("tsl_db", "Mixed Hall", 3);
        await TeamCornerRig.Online(async (db, path) =>
        {
            TeamCornerRig.PlayerRow(path, "mixa", "Mixed Hall");
            TeamCornerRig.PlayerRow(path, "mixb", "Mixed Hall");
            db.CountPlayerTeamMembers("Mixed Hall").Should().Be(2);
            TeamCornerLocation.TryNpcJoin(npcs, "Mixed Hall", () => { }).Should().BeFalse("three NPCs and two players fill five slots");
            TeamCornerRig.Exec(path, "UPDATE players SET player_data = json_set(player_data, '$.player.team', '') WHERE username = 'mixb';");
            TeamCornerLocation.TryNpcJoin(npcs, "Mixed Hall", () => { }).Should().BeTrue();
            await Task.CompletedTask;
        });
    }

    [Theory]
    [InlineData("Scripts/Systems/WorldSimulator.cs", "private void NPCTryJoinOrFormTeam(", "npc.Team = teamLeader.Team;")]
    [InlineData("Scripts/Systems/WorldSimulator.cs", "private void NPCTryRecruitForTeam(", "candidate.Team = npc.Team;")]
    [InlineData("Scripts/Systems/WorldInitializerSystem.cs", "private void SimulateTeamActivity(", "candidate.Team = npc.Team;")]
    [InlineData("Scripts/AI/EnhancedNPCBehaviors.cs", "private static void RecruitGangMembers(", "candidate.Team = gangName;")]
    public void EveryNpcJoinOfAnExistingTeam_GoesThroughTryNpcJoin(string file, string method, string assignment)
    {
        var src = File.ReadAllText(Path.Combine(Leftovers1114BTests.RepoRoot(), file));
        int start = src.IndexOf(method, StringComparison.Ordinal);
        start.Should().BeGreaterThan(0, method);
        int at = src.IndexOf(assignment, start, StringComparison.Ordinal);
        at.Should().BeGreaterThan(start, assignment);
        int call = src.LastIndexOf("TeamCornerLocation.TryNpcJoin(", at, StringComparison.Ordinal);
        call.Should().BeGreaterThan(start, $"{method} must add the member inside TryNpcJoin");
        src.Substring(call, at - call).Should().NotContain(";\n", "the assignment is inside the join callback")
            .And.Contain("() =>");
    }
}
