using System;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using FluentAssertions;
using Microsoft.Data.Sqlite;
using UsurperRemake;
using UsurperRemake.Locations;
using UsurperRemake.Systems;
using Xunit;

namespace UsurperReborn.Tests;

/// <summary>
/// v1.1.12 Team Corner audit fixes: the password a join checks, dissolving a team with its upgrades and
/// vault, keeping the protection while players remain, creating a team (insert first, trimmed, case),
/// team wars left active or with no rounds, the vault and upgrades, equipping, and the team size.
/// </summary>
[Collection("SharedGameSingletons")]
public class TeamCornerFixes1112Tests : IDisposable
{
    private readonly string _path = Path.Combine(Path.GetTempPath(), $"usurper-tcf-{Guid.NewGuid():N}.db");
    private readonly SqlSaveBackend _db;

    public TeamCornerFixes1112Tests() { _db = new SqlSaveBackend(_path); }

    public void Dispose()
    {
        SqliteConnection.ClearAllPools();
        try { File.Delete(_path); } catch { }
    }

    private void Exec(string sql) => TeamCornerRig.Exec(_path, sql);
    private object? Scalar(string sql) => TeamCornerRig.Scalar(_path, sql);
    private long Long(string sql) => Convert.ToInt64(Scalar(sql) ?? 0L);

    private static string RepoRoot()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir != null && !(Directory.Exists(Path.Combine(dir.FullName, "Scripts")) && Directory.Exists(Path.Combine(dir.FullName, "Localization"))))
            dir = dir.Parent;
        return dir!.FullName;
    }

    private static string MethodBody(string method)
    {
        string src = File.ReadAllText(Path.Combine(RepoRoot(), "Scripts/Locations/TeamCornerLocation.cs"));
        var m = Regex.Match(src, @"(?:private|internal|public|protected)[^\n;=]*\b" + Regex.Escape(method) + @"\s*\(");
        m.Success.Should().BeTrue($"{method} must exist");
        int open = src.IndexOf('{', m.Index);
        int depth = 0;
        for (int i = open; i < src.Length; i++)
        {
            if (src[i] == '{') depth++;
            else if (src[i] == '}' && --depth == 0) return src.Substring(open, i - open + 1);
        }
        throw new InvalidOperationException("unbalanced braces");
    }

    // ---------- 1. the team password ----------

    [Fact]
    public async Task PasswordChange_UpdatesTheHashAJoinChecks_AndOnlyForTheLeader()
    {
        await _db.CreatePlayerTeam("Keyholders", SqlSaveBackend.HashTeamPassword("old"), "boss");
        _db.ChangeTeamPassword("Keyholders", "someone", "old", "new").Should().BeFalse("only the leader");
        _db.ChangeTeamPassword("Keyholders", "boss", "wrong", "new").Should().BeFalse("the old password must match");
        _db.ChangeTeamPassword("Keyholders", "boss", "old", "new").Should().BeTrue();
        (await _db.VerifyPlayerTeam("Keyholders", "new")).passwordCorrect.Should().BeTrue();
        (await _db.VerifyPlayerTeam("Keyholders", "old")).passwordCorrect.Should().BeFalse();
    }

    [Fact]
    public async Task PasswordScreen_ChangesTheStoredPassword_ForTheLeader_AndRefusesAMember()
    {
        await TeamCornerRig.Online(async (db, path) =>
        {
            var hero = TeamCornerRig.Hero(name: "Pw Leader", team: "Lockers");
            await db.CreatePlayerTeam("Lockers", SqlSaveBackend.HashTeamPassword("old"), GameEngine.InheritanceKey(hero));
            var rig = new TeamCornerRig(hero, new[] { "old", "fresh" });
            await rig.Run("ChangeTeamPassword");
            (await db.VerifyPlayerTeam("Lockers", "fresh")).passwordCorrect.Should().BeTrue("a join checks the stored hash");
            (await db.VerifyPlayerTeam("Lockers", "old")).passwordCorrect.Should().BeFalse();

            // a team led by someone else
            await db.CreatePlayerTeam("Led Elsewhere", SqlSaveBackend.HashTeamPassword("theirs"), "someone_else");
            var member = TeamCornerRig.Hero(name: "Pw Member", team: "Led Elsewhere");
            var refused = new TeamCornerRig(member, new[] { "theirs", "mine" });
            string shown = await refused.Run("ChangeTeamPassword");
            shown.Should().Contain(Loc.Get("team.password_leader_only"));
            (await db.VerifyPlayerTeam("Led Elsewhere", "theirs")).passwordCorrect.Should().BeTrue();
        });
    }

    // ---------- 2 and 3. quitting ----------

    [Fact]
    public async Task Quit_KeepsTheProtection_WhileAPlayerIsStillOnTheTeam()
    {
        await TeamCornerRig.Online(async (db, path) =>
        {
            TeamCornerRig.PlayerRow(path, "tomas", "Stayers");
            await db.CreatePlayerTeam("Stayers", "x", "tomas");
            WorldSimulator.RegisterPlayerTeam("Stayers");
            try
            {
                var hero = TeamCornerRig.Hero(name: "Quit Hero", team: "Stayers");
                await new TeamCornerRig(hero, new[] { "y", "" }).Run("QuitTeam");
                hero.Team.Should().BeEmpty();
                WorldSimulator.IsPlayerTeam("Stayers").Should().BeTrue("a player is still on it");
            }
            finally { WorldSimulator.UnregisterPlayerTeam("Stayers"); }
        });
    }

    [Fact]
    public async Task Quit_TheLastMember_DissolvesTheTeam_WithItsUpgradesAndVault()
    {
        await TeamCornerRig.Online(async (db, path) =>
        {
            TeamCornerRig.Exec(path, "INSERT INTO player_teams (team_name, password_hash, created_by) VALUES ('Leavers', 'x', 'quit hero');");
            TeamCornerRig.Exec(path, "INSERT INTO team_upgrades (team_name, upgrade_type, level) VALUES ('Leavers', 'vault', 3);");
            TeamCornerRig.Exec(path, "INSERT INTO team_vault (team_name, gold) VALUES ('Leavers', 5000);");
            WorldSimulator.RegisterPlayerTeam("Leavers");
            var hero = TeamCornerRig.Hero(name: "Quit Hero", team: "Leavers");
            await new TeamCornerRig(hero, new[] { "y", "" }).Run("QuitTeam");
            WorldSimulator.IsPlayerTeam("Leavers").Should().BeFalse("no player is left");
            Convert.ToInt64(TeamCornerRig.Scalar(path, "SELECT COUNT(*) FROM player_teams WHERE team_name = 'Leavers'")).Should().Be(0);
            Convert.ToInt64(TeamCornerRig.Scalar(path, "SELECT COUNT(*) FROM team_upgrades WHERE team_name = 'Leavers'")).Should().Be(0, "a later team of the name must not inherit them");
            Convert.ToInt64(TeamCornerRig.Scalar(path, "SELECT COUNT(*) FROM team_vault WHERE team_name = 'Leavers'")).Should().Be(0);
        });
    }

    [Fact]
    public void Quit_SavesOnce()
    {
        string body = MethodBody("QuitTeam");
        body.Should().Contain("PersistTeamMembershipChange()");
        body.Should().NotContain("ResetAutoSaveThrottle").And.NotContain("AutoSave(");
    }

    // ---------- 4. creating a team ----------

    [Fact]
    public async Task CreateInsert_IsGuarded_AndIgnoresCase_AndStampsTheJoin()
    {
        (await _db.CreatePlayerTeam("Ravens", "x", "a")).Should().BeTrue();
        (await _db.CreatePlayerTeam("Ravens", "x", "b")).Should().BeFalse("the name is taken");
        (await _db.CreatePlayerTeam("RAVENS", "x", "b")).Should().BeFalse("a name that differs only in case is taken");
        _db.IsTeamNameTaken("ravens").Should().BeTrue();
        Scalar("SELECT last_join_at FROM player_teams WHERE team_name = 'Ravens'").Should().NotBeNull();
    }

    [Fact]
    public async Task Create_ANameTakenInAnotherCase_IsRefused_AndNothingIsCharged()
    {
        await TeamCornerRig.Online(async (db, path) =>
        {
            await db.CreatePlayerTeam("Ravens", "x", "someone");
            var hero = TeamCornerRig.Hero(name: "Create Hero", gold: 50000);
            string shown = await new TeamCornerRig(hero, new[] { "  RAVENS ", "pw" }).Run("CreateTeam");
            shown.Should().Contain(Loc.Get("team.player_team_exists"));
            hero.Gold.Should().Be(50000);
            hero.Team.Should().BeEmpty();
        });
    }

    [Fact]
    public async Task Create_ANameAnNpcTeamHasInAnotherCase_IsRefused_AndABlankNameToo()
    {
        var npc = TeamCornerRig.Npc("tc_create_1", "Crow Npc", "Night Crows");
        NPCSpawnSystem.Instance.ActiveNPCs.Add(npc);
        try
        {
            var hero = TeamCornerRig.Hero(name: "Create Hero", gold: 50000);
            string shown = await new TeamCornerRig(hero, new[] { " night crows ", "pw" }).Run("CreateTeam");
            shown.Should().Contain(Loc.Get("team.team_name_exists"));
            hero.Team.Should().BeEmpty();
            hero.Gold.Should().Be(50000);

            string blank = await new TeamCornerRig(hero, new[] { "    ", "pw" }).Run("CreateTeam");
            blank.Should().Contain(Loc.Get("team.invalid_team_name"));
            hero.Team.Should().BeEmpty();
        }
        finally { NPCSpawnSystem.Instance.ActiveNPCs.Remove(npc); }
    }

    [Fact]
    public async Task Create_Online_InsertsTheRowThenCharges()
    {
        await TeamCornerRig.Online(async (db, path) =>
        {
            var hero = TeamCornerRig.Hero(name: "Create Hero", gold: 50000);
            await new TeamCornerRig(hero, new[] { " Brand New ", "pw", "" }).Run("CreateTeam");
            hero.Team.Should().Be("Brand New", "the name is trimmed");
            hero.Gold.Should().BeLessThan(50000);
            db.IsTeamNameTaken("Brand New").Should().BeTrue();
            WorldSimulator.UnregisterPlayerTeam("Brand New");
        });
        string body = MethodBody("CreateTeam");
        body.IndexOf("CreatePlayerTeam(").Should().BeLessThan(body.IndexOf("currentPlayer.Gold -= creationCost"), "the fee is taken only for a created row");
    }

    [Fact]
    public void Rankings_GroupNamesIgnoringCase()
    {
        var a = TeamCornerRig.Npc("tc_rank_1", "Rank One", "Grey Band", level: 10);
        var b = TeamCornerRig.Npc("tc_rank_2", "Rank Two", "grey band", level: 20);
        var rows = TeamCornerLocation.BuildTeamRankings(new[] { a, b }, Array.Empty<PlayerTeamInfo>(), null);
        rows.Should().ContainSingle().Which.MemberCount.Should().Be(2);
    }

    // ---------- 5. the live NPC ----------

    [Fact]
    public void LiveTeamNpc_FindsTheReloadedCopy_ByID()
    {
        var npc = TeamCornerRig.Npc("tc_live_1", "Live Npc", "Live Band");
        NPCSpawnSystem.Instance.ActiveNPCs.Add(npc);
        try
        {
            var copy = TeamCornerRig.Reload(npc);
            TeamCornerLocation.LiveTeamNpc(npc).Should().BeSameAs(copy);
        }
        finally { NPCSpawnSystem.Instance.ActiveNPCs.RemoveAll(n => n.ID == "tc_live_1"); }
        TeamCornerLocation.LiveTeamNpc(npc).Should().BeNull("gone from the live list");
    }

    [Fact]
    public void Resurrect_SavesTheSharedState_AndSack_OffersTheGearFirst()
    {
        MethodBody("ResurrectTeammate").Should().Contain("SaveAllSharedState()");
        string sack = MethodBody("SackMember");
        sack.Should().Contain("team.sack_gear_warning").And.Contain("TakeAllEquipment(member, confirm: false)");
        sack.IndexOf("TakeAllEquipment(").Should().BeLessThan(sack.IndexOf("live.Team = \"\""), "the gear is offered before the NPC goes");
    }

    // ---------- 6. team wars ----------

    private int War(string challenger, string defender, int minutesAgo, int cWins, int dWins, string key = "bran", long wager = 500)
    {
        Exec($"INSERT INTO team_wars (challenger_team, defender_team, status, challenger_wins, defender_wins, gold_wagered, started_at, challenger_key) " +
             $"VALUES ('{challenger}', '{defender}', 'active', {cWins}, {dWins}, {wager}, datetime('now', '-{minutesAgo} minutes'), '{key}');");
        return Convert.ToInt32(Scalar("SELECT MAX(id) FROM team_wars"));
    }

    [Fact]
    public void AStaleWar_IsAbandoned_AndNoLongerBlocks_AndAWarWithNoRoundIsRefundedOnce()
    {
        int id = War("Reds", "Blues", minutesAgo: 20, 0, 0);
        _db.HasActiveTeamWar("Reds").Should().BeFalse("a war left by a lost session must not block for ever");
        _db.HasActiveTeamWar("Blues").Should().BeFalse();
        Scalar($"SELECT status FROM team_wars WHERE id = {id}").Should().Be("abandoned");
        Long("SELECT COUNT(*) FROM pending_gold_transfers WHERE recipient_username = 'bran' AND amount = 500").Should().Be(1);
        _db.ExpireStaleTeamWars().Should().Be(0);
        Long("SELECT COUNT(*) FROM pending_gold_transfers").Should().Be(1, "refunded once");
    }

    [Fact]
    public void AStaleWarWithRounds_IsAbandoned_WithoutARefund_AndAFreshWarStillBlocks()
    {
        int played = War("Reds", "Blues", minutesAgo: 20, 1, 0);
        War("Greens", "Golds", minutesAgo: 1, 0, 0);
        _db.HasActiveTeamWar("Reds").Should().BeFalse();
        Scalar($"SELECT status FROM team_wars WHERE id = {played}").Should().Be("abandoned");
        Long("SELECT COUNT(*) FROM pending_gold_transfers").Should().Be(0, "leaving a war under way must not pay");
        _db.HasActiveTeamWar("Greens").Should().BeTrue("a war under way is not stale");
    }

    [Fact]
    public async Task CreateTeamWar_IsGuarded_AndKeepsThePayer()
    {
        int first = await _db.CreateTeamWar("Reds", "Blues", 500, "Bran");
        first.Should().BeGreaterThan(0);
        (await _db.CreateTeamWar("Blues", "Greens", 500, "tomas")).Should().Be(-1, "Blues is already at war");
        Scalar($"SELECT challenger_key FROM team_wars WHERE id = {first}").Should().Be("bran");
    }

    [Fact]
    public async Task AWarWithNoRoundFought_IsAbandoned_TheWagerReturned_AndNoDailyWarUsed()
    {
        await TeamCornerRig.Online(async (db, path) =>
        {
            await db.CreatePlayerTeam("Home Side", "x", "war hero");
            await db.CreatePlayerTeam("Away Side", "x", "ghost");
            // saves that cannot be loaded: every round is skipped
            TeamCornerRig.PlayerRow(path, "mate", "Home Side", rawPlayer: "{\"team\":\"Home Side\",\"level\":\"bad\"}");
            TeamCornerRig.PlayerRow(path, "ghost", "Away Side", rawPlayer: "{\"team\":\"Away Side\",\"level\":\"bad\"}");
            var hero = TeamCornerRig.Hero(name: "War Hero", team: "Home Side", gold: 5000);
            hero.Level = 1;
            var rig = new TeamCornerRig(hero, new[] { "1", "y", "", "" });
            string shown = await rig.Run("ChallengeTeamWar", db);
            shown.Should().Contain(Loc.Get("team.war_no_rounds", $"{1000:N0}"));
            hero.Gold.Should().Be(5000);
            hero.TeamWarsToday.Should().Be(0);
            TeamCornerRig.Scalar(path, "SELECT status FROM team_wars").Should().Be("abandoned");
            // v1.1.12: the settled war is not refunded again by the stale cleanup
            TeamCornerRig.Exec(path, "UPDATE team_wars SET started_at = datetime('now', '-20 minutes');");
            db.ExpireStaleTeamWars().Should().Be(0);
            Convert.ToInt64(TeamCornerRig.Scalar(path, "SELECT COUNT(*) FROM pending_gold_transfers")).Should().Be(0, "refunded once, at the war");
        });
    }

    [Fact]
    public async Task AWarWithNoRound_WhoseSettlementFails_IsNotRefundedAtOnce_AndTheCleanupRefundsItOnce()
    {
        await TeamCornerRig.Online(async (db, path) =>
        {
            await db.CreatePlayerTeam("Home Side", "x", "war hero");
            await db.CreatePlayerTeam("Away Side", "x", "ghost");
            TeamCornerRig.PlayerRow(path, "mate", "Home Side", rawPlayer: "{\"team\":\"Home Side\",\"level\":\"bad\"}");
            TeamCornerRig.PlayerRow(path, "ghost", "Away Side", rawPlayer: "{\"team\":\"Away Side\",\"level\":\"bad\"}");
            // the settlement write fails (as a DB error would), leaving the war active
            TeamCornerRig.Exec(path, "CREATE TRIGGER no_settle BEFORE UPDATE OF status ON team_wars WHEN NEW.status = 'abandoned' BEGIN SELECT RAISE(ABORT, 'test'); END;");
            var hero = TeamCornerRig.Hero(name: "War Hero", team: "Home Side", gold: 5000);
            hero.Level = 1;
            string shown = await new TeamCornerRig(hero, new[] { "1", "y", "", "" }).Run("ChallengeTeamWar", db);
            shown.Should().Contain(Loc.Get("team.war_no_rounds_pending", $"{1000:N0}"));
            hero.Gold.Should().Be(4000, "not refunded while the war is unsettled");
            TeamCornerRig.Scalar(path, "SELECT status FROM team_wars").Should().Be("active");

            TeamCornerRig.Exec(path, "DROP TRIGGER no_settle; UPDATE team_wars SET started_at = datetime('now', '-20 minutes');");
            db.ExpireStaleTeamWars().Should().Be(1);
            db.ExpireStaleTeamWars().Should().Be(0);
            Convert.ToInt64(TeamCornerRig.Scalar(path, "SELECT COUNT(*) FROM pending_gold_transfers WHERE amount = 1000")).Should().Be(1, "one refund in all");
            Convert.ToInt64(TeamCornerRig.Scalar(path, "SELECT COUNT(*) FROM pending_gold_transfers")).Should().Be(1);
        });
    }

    [Fact]
    public async Task CompleteTeamWar_OnAWarNoLongerActive_DoesNotLand()
    {
        int id = War("Reds", "Blues", minutesAgo: 20, 0, 0);
        _db.ExpireStaleTeamWars().Should().Be(1);
        (await _db.CompleteTeamWar(id, "abandoned")).Should().BeFalse("the cleanup settled it first");
        (await _db.CompleteTeamWar(id, "challenger_won")).Should().BeFalse();
        Scalar($"SELECT status FROM team_wars WHERE id = {id}").Should().Be("abandoned");
        int live = War("Greens", "Golds", minutesAgo: 1, 1, 0);
        (await _db.CompleteTeamWar(live, "challenger_won")).Should().BeTrue();
        (await _db.CompleteTeamWar(live, "abandoned")).Should().BeFalse("settled once");
    }

    [Fact]
    public void TheWager_IsSavedBeforeTheWarRow_AndTheResultAfter()
    {
        string body = MethodBody("ChallengeTeamWar");
        int deduct = body.IndexOf("currentPlayer.Gold -= wager");
        int save = body.IndexOf("ForcePlayerSave()", deduct);
        body.IndexOf("CreateTeamWar(").Should().BeGreaterThan(save).And.BeGreaterThan(0);
        body.IndexOf("ForcePlayerSave()", body.IndexOf("currentPlayer.TeamWarsToday++")).Should().BeGreaterThan(0, "the daily count and gold are saved");
    }

    // ---------- 7. the vault and upgrades ----------

    [Fact]
    public void Upgrade_IsGuardedOnTheLevel_AndTheCap()
    {
        _db.TryUpgradeTeamFacility("Smiths", "armory", 0, 100, payFromVault: false).Should().BeTrue();
        _db.TryUpgradeTeamFacility("Smiths", "armory", 0, 100, payFromVault: false).Should().BeFalse("someone else raised it first");
        _db.TryUpgradeTeamFacility("Smiths", "armory", 1, 100, payFromVault: false).Should().BeTrue();
        _db.GetTeamUpgradeLevel("Smiths", "armory").Should().Be(2);
        Exec("UPDATE team_upgrades SET level = 10 WHERE team_name = 'Smiths';");
        _db.TryUpgradeTeamFacility("Smiths", "armory", 10, 100, payFromVault: false).Should().BeFalse("the cap");
        _db.GetTeamUpgradeLevel("Smiths", "armory").Should().Be(10);
    }

    [Fact]
    public async Task Upgrade_PaidFromTheVault_IsOneTransaction()
    {
        (await _db.DepositToTeamVault("Smiths", 1000)).Should().BeTrue();
        _db.TryUpgradeTeamFacility("Smiths", "vault", 0, 5000, payFromVault: true).Should().BeFalse("the vault is short");
        (await _db.GetTeamVaultGold("Smiths")).Should().Be(1000);
        _db.GetTeamUpgradeLevel("Smiths", "vault").Should().Be(0);

        _db.TryUpgradeTeamFacility("Smiths", "vault", 0, 400, payFromVault: true).Should().BeTrue();
        (await _db.GetTeamVaultGold("Smiths")).Should().Be(600);
        _db.TryUpgradeTeamFacility("Smiths", "vault", 0, 400, payFromVault: true).Should().BeFalse("the level moved");
        (await _db.GetTeamVaultGold("Smiths")).Should().Be(600, "a failed upgrade takes nothing");
    }

    [Fact]
    public async Task Deposit_EnforcesTheCapacity_InTheSql()
    {
        (await _db.DepositToTeamVault("Misers", 50000)).Should().BeTrue();
        (await _db.DepositToTeamVault("Misers", 1)).Should().BeFalse("the vault holds 50,000 at level 0");
        (await _db.GetTeamVaultGold("Misers")).Should().Be(50000);
        _db.TryUpgradeTeamFacility("Misers", "vault", 0, 1, payFromVault: false).Should().BeTrue();
        (await _db.DepositToTeamVault("Misers", 50000)).Should().BeTrue("level 1 holds 100,000");
        (await _db.DepositToTeamVault("Newcomers", 60000)).Should().BeFalse("a first deposit is capped too");
    }

    [Fact]
    public async Task VaultDeposit_AndWithdraw_SaveThePlayerAtOnce_AndAWithdrawIsConfirmed()
    {
        await TeamCornerRig.Online(async (db, path) =>
        {
            var hero = TeamCornerRig.Hero(name: "Vault Hero", team: "Savers", gold: 1000);
            string key = UsurperRemake.BBS.DoorMode.GetPlayerName().ToLowerInvariant();   // the save key AutoSave uses here
            await new TeamCornerRig(hero, new[] { "600" }).Run("DepositToVault", db, "Savers");
            (await db.GetTeamVaultGold("Savers")).Should().Be(600);
            hero.Gold.Should().Be(400);
            Convert.ToInt64(TeamCornerRig.Scalar(path, $"SELECT json_extract(player_data, '$.player.gold') FROM players WHERE username = '{key}'")).Should().Be(400, "the gold left the player on disk too");

            await new TeamCornerRig(hero, new[] { "100", "n" }).Run("WithdrawFromVault", db, "Savers");
            (await db.GetTeamVaultGold("Savers")).Should().Be(600, "the withdraw was declined");

            await new TeamCornerRig(hero, new[] { "100", "y" }).Run("WithdrawFromVault", db, "Savers");
            (await db.GetTeamVaultGold("Savers")).Should().Be(500);
            Convert.ToInt64(TeamCornerRig.Scalar(path, $"SELECT json_extract(player_data, '$.player.gold') FROM players WHERE username = '{key}'")).Should().Be(500);
        });
    }

    // ---------- 8. equipping ----------

    [Fact]
    public void Equip_NothingTakenFromThePlayer_MeansNothingEquipped()
    {
        var hero = TeamCornerRig.Hero();
        var rig = new TeamCornerRig(hero, Array.Empty<string>());
        var item = new Equipment { Name = "Phantom Blade", WeaponPower = 5 };
        rig.Loc.TakeFromPlayerForEquip(item, wasEquipped: false, sourceSlot: null).Should().BeFalse();
        hero.Inventory.Add(new Item { Name = "Phantom Blade", Attack = 5 });
        rig.Loc.TakeFromPlayerForEquip(item, wasEquipped: false, sourceSlot: null).Should().BeTrue();
        hero.Inventory.Should().BeEmpty();

        string body = MethodBody("EquipItemToCharacter");
        body.IndexOf("if (!TakeFromPlayerForEquip(").Should().BeGreaterThan(0).And.BeLessThan(body.IndexOf("target.EquipItem("));
    }

    [Fact]
    public void Equip_ForcesThePlayerSave_BeforeTheSharedState()
    {
        string body = MethodBody("EquipMember");
        body.Should().Contain("ForcePlayerSave()");
        body.IndexOf("ForcePlayerSave()").Should().BeLessThan(body.IndexOf("SaveAllSharedState"));
    }

    // ---------- 9. team size ----------

    [Fact]
    public void Slots_CountTheDead_ButNotTheGoneForGood()
    {
        var npcs = new[]
        {
            TeamCornerRig.Npc("s1", "S1", "Sizers"),
            TeamCornerRig.Npc("s2", "S2", "Sizers", dead: true),
            new NPC { ID = "s3", Name2 = "S3", Team = "Sizers", IsPermaDead = true },
            TeamCornerRig.Npc("s4", "S4", "Others"),
        };
        TeamCornerLocation.CountTeamSlots(npcs, "Sizers", playerMembers: 2).Should().Be(4);
    }

    [Fact]
    public async Task Recruit_ATeamWhoseDeadFillIt_IsFull()
    {
        var npcs = new[]
        {
            TeamCornerRig.Npc("tc_full_1", "Full One", "Full Crew"),
            TeamCornerRig.Npc("tc_full_2", "Full Two", "Full Crew"),
            TeamCornerRig.Npc("tc_full_3", "Full Three", "Full Crew"),
            TeamCornerRig.Npc("tc_full_4", "Full Four", "Full Crew", dead: true),
        };
        NPCSpawnSystem.Instance.ActiveNPCs.AddRange(npcs);
        try
        {
            string shown = await new TeamCornerRig(TeamCornerRig.Hero(team: "Full Crew"), new[] { "" }).Run("RecruitNPCToTeam");
            shown.Should().Contain(Loc.Get("team.team_full", 5), "the dead member holds a slot");
        }
        finally { NPCSpawnSystem.Instance.ActiveNPCs.RemoveAll(n => n.ID.StartsWith("tc_full_")); }
    }

    [Fact]
    public async Task Join_AFullTeam_IsRefused()
    {
        var npcs = Enumerable.Range(1, 5).Select(i => TeamCornerRig.Npc($"tc_jfull_{i}", $"Packed {i}", "Packed House", dead: i == 5)).ToList();
        NPCSpawnSystem.Instance.ActiveNPCs.AddRange(npcs);
        try
        {
            var hero = TeamCornerRig.Hero();
            string shown = await new TeamCornerRig(hero, new[] { "packed house", "pw", "" }).Run("JoinTeam");
            shown.Should().Contain(Loc.Get("team.join_team_full", "Packed House", 5));
            hero.Team.Should().BeEmpty();
        }
        finally { NPCSpawnSystem.Instance.ActiveNPCs.RemoveAll(n => n.ID.StartsWith("tc_jfull_")); }
    }

    [Fact]
    public async Task Join_ATeamThatFillsDuringThePassword_IsRefused()
    {
        var npcs = Enumerable.Range(1, 4).Select(i => TeamCornerRig.Npc($"tc_jlate_{i}", $"Late {i}", "Late House")).ToList();
        NPCSpawnSystem.Instance.ActiveNPCs.AddRange(npcs);
        try
        {
            var hero = TeamCornerRig.Hero();
            // line 1 is the password; a fifth member joins while it is asked
            var rig = new TeamCornerRig(hero, new[] { "late house", "pw", "" },
                i => { if (i == 1) NPCSpawnSystem.Instance.ActiveNPCs.Add(TeamCornerRig.Npc("tc_jlate_5", "Late 5", "Late House")); });
            string shown = await rig.Run("JoinTeam");
            shown.Should().Contain(Loc.Get("team.join_team_full", "Late House", 5));
            shown.Should().NotContain(Loc.Get("team.joined_team", "Late House"));
            hero.Team.Should().BeEmpty();
        }
        finally { NPCSpawnSystem.Instance.ActiveNPCs.RemoveAll(n => n.ID.StartsWith("tc_jlate_")); }
    }

    [Fact]
    public async Task Join_APlayerTeamThatFillsDuringThePassword_IsRefused()
    {
        var npcs = Enumerable.Range(1, 4).Select(i => TeamCornerRig.Npc($"tc_jlp_{i}", $"Lodge {i}", "Late Lodge")).ToList();
        NPCSpawnSystem.Instance.ActiveNPCs.AddRange(npcs);
        try
        {
            await TeamCornerRig.Online(async (db, path) =>
            {
                (await db.CreatePlayerTeam("Late Lodge", SqlSaveBackend.HashTeamPassword("secret"), "boss")).Should().BeTrue();
                var hero = TeamCornerRig.Hero();
                // a player joins the team while the password is asked
                var rig = new TeamCornerRig(hero, new[] { "late lodge", "secret", "" },
                    i => { if (i == 1) TeamCornerRig.PlayerRow(path, "tomas", "Late Lodge"); });
                string shown = await rig.Run("JoinTeam");
                shown.Should().Contain(Loc.Get("team.join_team_full", "Late Lodge", 5));
                hero.Team.Should().BeEmpty();
            });
        }
        finally { NPCSpawnSystem.Instance.ActiveNPCs.RemoveAll(n => n.ID.StartsWith("tc_jlp_")); }
    }

    [Fact]
    public async Task Sack_AnNpcThatLeftTheTeamAtTheGearPrompt_KeepsItsGear()
    {
        int shield = EquipmentDatabase.GetShields().First().Id;
        var npc = TeamCornerRig.Npc("tc_sgear_1", "Geared Npc", "Gear Crew");
        npc.EquippedItems[EquipmentSlot.OffHand] = shield;
        NPCSpawnSystem.Instance.ActiveNPCs.Add(npc);
        try
        {
            var hero = TeamCornerRig.Hero(team: "Gear Crew");
            int packBefore = hero.Inventory.Count;
            NPC? live = null;
            // line 2 answers the take-gear prompt; before it the NPC is reloaded onto another team
            var rig = new TeamCornerRig(hero, new[] { "geared", "y", "y", "" }, i =>
            {
                if (i != 2) return;
                live = TeamCornerRig.Reload(npc);
                live.EquippedItems[EquipmentSlot.OffHand] = shield;
                live.Team = "Other Crew";
            });
            string shown = await rig.Run("SackMember");
            live.Should().NotBeNull();
            shown.Should().Contain(Loc.Get("team.sack_gear_gone", "Geared Npc"));
            live!.GetEquipment(EquipmentSlot.OffHand).Should().NotBeNull("nothing is taken from an NPC off the team");
            hero.Inventory.Count.Should().Be(packBefore);
            live.Team.Should().Be("Other Crew");
        }
        finally { NPCSpawnSystem.Instance.ActiveNPCs.RemoveAll(n => n.ID == "tc_sgear_1"); }
    }

    [Fact]
    public async Task Resurrect_ATeamWithFiveLiving_CannotReviveASixth()
    {
        var npcs = Enumerable.Range(1, 4).Select(i => TeamCornerRig.Npc($"tc_rfull_{i}", $"Living {i}", "Crowded")).ToList();
        var dead = TeamCornerRig.Npc("tc_rfull_dead", "Fallen One", "Crowded", level: 1, dead: true);
        npcs.Add(dead);
        NPCSpawnSystem.Instance.ActiveNPCs.AddRange(npcs);
        try
        {
            var hero = TeamCornerRig.Hero(team: "Crowded", gold: 10000);
            string shown = await new TeamCornerRig(hero, new[] { "fallen", "y", "" }).Run("ResurrectTeammate");
            shown.Should().Contain(Loc.Get("team.team_full", 5));
            dead.IsAlive.Should().BeFalse();
            hero.Gold.Should().Be(10000);
        }
        finally { NPCSpawnSystem.Instance.ActiveNPCs.RemoveAll(n => n.ID.StartsWith("tc_rfull_")); }
    }

    // ---------- the menus ----------

    [Fact]
    public void ViewInventories_IsOnTheScreenReaderAndBbsMenus()
    {
        MethodBody("DisplayLocationSR").Should().Contain("WriteSRMenuOption(\"V\"");
        MethodBody("DisplayLocationBBS").Should().Contain("(\"V\", \"bright_yellow\"");
    }
}
