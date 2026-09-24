using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using FluentAssertions;
using Microsoft.Data.Sqlite;
using UsurperRemake;
using UsurperRemake.Locations;
using UsurperRemake.Systems;
using Xunit;

namespace UsurperReborn.Tests;

/// <summary>
/// v1.1.12: a scripted input stream that hands the terminal one line per read, and runs a hook before
/// serving a given line, so a test can change the world between two prompts (a world_state reload).
/// </summary>
internal sealed class LineStream : Stream
{
    private readonly List<string> _lines;
    private readonly Action<int>? _beforeLine;
    private byte[] _pending = Array.Empty<byte>();
    private int _pendingPos, _next;
    public LineStream(IEnumerable<string> lines, Action<int>? beforeLine = null) { _lines = lines.ToList(); _beforeLine = beforeLine; }
    public override int Read(byte[] buffer, int offset, int count)
    {
        if (_pendingPos >= _pending.Length)
        {
            if (_next >= _lines.Count) return 0;
            _beforeLine?.Invoke(_next);
            _pending = Encoding.UTF8.GetBytes(_lines[_next++] + "\n");
            _pendingPos = 0;
        }
        int n = Math.Min(count, _pending.Length - _pendingPos);
        Array.Copy(_pending, _pendingPos, buffer, offset, n);
        _pendingPos += n;
        return n;
    }
    public override Task<int> ReadAsync(byte[] buffer, int offset, int count, CancellationToken ct) => Task.FromResult(Read(buffer, offset, count));
    public override ValueTask<int> ReadAsync(Memory<byte> buffer, CancellationToken ct = default)
    {
        var tmp = new byte[buffer.Length];
        int n = Read(tmp, 0, tmp.Length);
        tmp.AsMemory(0, n).CopyTo(buffer);
        return ValueTask.FromResult(n);
    }
    public override bool CanRead => true; public override bool CanSeek => false; public override bool CanWrite => false;
    public override long Length => throw new NotSupportedException();
    public override long Position { get => 0; set => throw new NotSupportedException(); }
    public override void Flush() { }
    public override long Seek(long o, SeekOrigin s) => throw new NotSupportedException();
    public override void SetLength(long v) => throw new NotSupportedException();
    public override void Write(byte[] b, int o, int c) => throw new NotSupportedException();
}

/// <summary>v1.1.12: drives Team Corner screens on a script and returns what they printed.</summary>
internal sealed class TeamCornerRig
{
    private const BindingFlags F = BindingFlags.NonPublic | BindingFlags.Instance;
    public readonly TeamCornerLocation Loc = new();
    public readonly TerminalEmulator Term;
    private readonly MemoryStream _out = new();

    public TeamCornerRig(Character hero, IEnumerable<string> lines, Action<int>? beforeLine = null)
    {
        Term = new TerminalEmulator(new LineStream(lines, beforeLine), _out);
        typeof(BaseLocation).GetField("terminal", F)!.SetValue(Loc, Term);
        typeof(BaseLocation).GetField("currentPlayer", F)!.SetValue(Loc, hero);
    }

    public string Shown()
    {
        Term.StreamWriterInternal!.Flush();
        return Regex.Replace(Encoding.UTF8.GetString(_out.ToArray()), "\u001b\\[[0-9;]*[A-Za-z]", "");
    }

    public async Task<string> Run(string method, params object[] args)
    {
        var mi = typeof(TeamCornerLocation).GetMethod(method, F)!;
        await (Task)mi.Invoke(Loc, args.Length == 0 ? null : args)!;
        return Shown();
    }

    public static Character Hero(string name = "Rig Hero", string team = "", long gold = 10000) =>
        new Character { Name1 = name, Name2 = name, Class = CharacterClass.Warrior, Level = 20, HP = 300, MaxHP = 300, Gold = gold, Team = team };

    public static NPC Npc(string id, string name, string team, int level = 10, bool dead = false) =>
        new NPC { ID = id, Name1 = name, Name2 = name, Team = team, TeamPW = "pw", Class = CharacterClass.Warrior, Level = level,
                  HP = dead ? 0 : 100, MaxHP = 100, IsDead = dead, CurrentLocation = "Main Street" };

    /// <summary>A copy of an NPC as a world_state reload would build it: same ID, a new object.</summary>
    public static NPC Reloaded(NPC n) => new NPC
    {
        ID = n.ID, Name1 = n.Name1, Name2 = n.Name2, Team = n.Team, TeamPW = n.TeamPW, Class = n.Class, Level = n.Level,
        HP = n.HP, MaxHP = n.MaxHP, IsDead = n.IsDead, CurrentLocation = n.CurrentLocation, Specialization = n.Specialization,
    };

    /// <summary>Replaces the NPC in the live list with its reloaded copy; returns the copy.</summary>
    public static NPC Reload(NPC n)
    {
        var list = NPCSpawnSystem.Instance.ActiveNPCs;
        var copy = Reloaded(n);
        int i = list.IndexOf(n);
        list[i] = copy;
        return copy;
    }

    public static void SetOnline(bool on) =>
        typeof(UsurperRemake.BBS.DoorMode).GetField("_onlineMode", BindingFlags.NonPublic | BindingFlags.Static)!.SetValue(null, on);

    /// <summary>Runs body online against a fresh SQLite database, restoring the save system after.</summary>
    public static async Task Online(Func<SqlSaveBackend, string, Task> body)
    {
        string path = Path.Combine(Path.GetTempPath(), $"usurper-tc-{Guid.NewGuid():N}.db");
        var field = typeof(SaveSystem).GetField("instance", BindingFlags.NonPublic | BindingFlags.Static)!;
        var before = field.GetValue(null);
        bool wasOnline = UsurperRemake.BBS.DoorMode.IsOnlineMode;
        var db = new SqlSaveBackend(path);
        SaveSystem.InitializeWithBackend(db);
        SetOnline(true);
        try { await body(db, path); }
        finally
        {
            SetOnline(wasOnline);
            field.SetValue(null, before);
            SqliteConnection.ClearAllPools();
            try { File.Delete(path); } catch { }
        }
    }

    public static void Exec(string path, string sql)
    {
        using var conn = new SqliteConnection($"Data Source={path}");
        conn.Open();
        using var cmd = conn.CreateCommand();
        cmd.CommandText = sql;
        cmd.ExecuteNonQuery();
    }

    public static object? Scalar(string path, string sql)
    {
        using var conn = new SqliteConnection($"Data Source={path}");
        conn.Open();
        using var cmd = conn.CreateCommand();
        cmd.CommandText = sql;
        return cmd.ExecuteScalar();
    }

    public static void PlayerRow(string path, string key, string team, int level = 12, int str = 55, string? rawPlayer = null) =>
        Exec(path, $"INSERT INTO players (username, display_name, player_data) VALUES ('{key}', '{char.ToUpper(key[0]) + key.Substring(1)}', " +
                   $"'{{\"player\":{rawPlayer ?? $"{{\"name2\":\"{char.ToUpper(key[0]) + key.Substring(1)}\",\"team\":\"{team}\",\"level\":{level},\"class\":0,\"strength\":{str},\"hp\":90,\"maxHP\":90}}"}}}');");
}

/// <summary>
/// v1.1.12: the shared list picker (BaseLocation.PickFromList) and the Team Corner screens that use it:
/// [I] Info, [J] Join, [E] Examine, [2] Sack, [G] Equip, [X] Specialize, [U] Resurrect.
/// </summary>
[Collection("SharedGameSingletons")]
public class TeamCornerPickerTests
{
    private static List<string> Names(int n) => Enumerable.Range(1, n).Select(i => $"Name {i:00}").ToList();

    private static async Task<(string? picked, string shown)> Pick(IReadOnlyList<string> items, params string[] lines)
    {
        var rig = new TeamCornerRig(TeamCornerRig.Hero(), lines);
        var picked = await rig.Loc.PickFromList(items, s => s, s => s, "team.pick_examine_title");
        return (picked, rig.Shown());
    }

    // ---------- the picker ----------

    [Fact]
    public async Task Picker_PagesOfTen_AndANumberCountsAcrossPages()
    {
        var (picked, shown) = await Pick(Names(23), "N", "15");
        picked.Should().Be("Name 15");
        shown.Should().Contain("[10] Name 10").And.Contain("[11] Name 11").And.Contain("[20] Name 20");
        shown.Should().Contain(Loc.Get("team.recruit_page_footer", 1, 10, 23, 1, 3));
        shown.Should().Contain(Loc.Get("team.recruit_page_footer", 11, 20, 23, 2, 3));
        shown.Substring(0, shown.IndexOf("[10] Name 10")).Should().NotContain("[11]", "the first page stops at ten");
    }

    [Fact]
    public async Task Picker_ANumberOnALaterPage_CanBeTypedFromTheFirst()
    {
        var (picked, _) = await Pick(Names(23), "23");
        picked.Should().Be("Name 23");
    }

    [Fact]
    public async Task Picker_PreviousPage_GoesBack()
    {
        var (picked, shown) = await Pick(Names(23), "N", "N", "P", "0");
        picked.Should().BeNull();
        shown.Should().Contain(Loc.Get("team.recruit_page_footer", 21, 23, 23, 3, 3));
        shown.LastIndexOf(Loc.Get("team.recruit_page_footer", 11, 20, 23, 2, 3)).Should().BeGreaterThan(shown.IndexOf(Loc.Get("team.recruit_page_footer", 21, 23, 23, 3, 3)));
    }

    [Fact]
    public async Task Picker_AUniqueStartOfAName_Chooses()
    {
        var (picked, _) = await Pick(new[] { "Aldric", "Bram", "Brom", "Cedric" }, "ald");
        picked.Should().Be("Aldric");
    }

    [Fact]
    public async Task Picker_AnExactName_WinsOverALongerOne()
    {
        var (picked, _) = await Pick(new[] { "Bobby", "Bob" }, "bob");
        picked.Should().Be("Bob");
    }

    [Fact]
    public async Task Picker_AStartThatFitsSeveral_ListsOnlyThem_ThenANumberChoosesAmongThem()
    {
        var (picked, shown) = await Pick(new[] { "Aldric", "Bram", "Brom", "Cedric" }, "br", "2");
        picked.Should().Be("Brom");
        shown.Should().Contain(Loc.Get("base.pick_matching", "br"));
    }

    [Fact]
    public async Task Picker_EnterOrZero_Cancels()
    {
        (await Pick(Names(5), "")).picked.Should().BeNull();
        (await Pick(Names(5), "0")).picked.Should().BeNull();
        // with a filter on, the first Enter brings the full list back and the second leaves
        var (picked, shown) = await Pick(new[] { "Bram", "Brom", "Cedric" }, "br", "", "");
        picked.Should().BeNull();
        shown.LastIndexOf("[3] Cedric").Should().BeGreaterThan(shown.IndexOf(Loc.Get("base.pick_matching", "br")));
    }

    [Fact]
    public async Task Picker_ANumberPastTheEnd_OrANameNotThere_SaysSo()
    {
        var (_, shown) = await Pick(Names(12), "40", "", "zzz", "", "0");
        shown.Should().Contain(Loc.Get("base.pick_no_number", 40, 12));
        shown.Should().Contain(Loc.Get("base.pick_no_match", "zzz"));
    }

    [Fact]
    public async Task Picker_ScreenReaderRows_AreNumberDotText()
    {
        var hero = TeamCornerRig.Hero();
        hero.ScreenReaderMode = true;
        var rig = new TeamCornerRig(hero, new[] { "0" });
        await rig.Loc.PickFromList(Names(3), s => s, s => s, "team.pick_examine_title");
        string shown = rig.Shown();
        shown.Should().Contain("  3. Name 03").And.Contain($"0. {Loc.Get("ui.cancel")}").And.NotContain("[3]");
    }

    [Fact]
    public async Task Picker_AFewEntries_ShowNoPaging()
    {
        var (_, shown) = await Pick(Names(4), "0");
        shown.Should().Contain("[4] Name 04").And.NotContain("Page 1/");
    }

    // ---------- the call sites ----------

    private static async Task WithNpcs(IEnumerable<NPC> npcs, Func<Task> body)
    {
        var list = NPCSpawnSystem.Instance.ActiveNPCs;
        var added = npcs.ToList();
        list.AddRange(added);
        try { await body(); }
        finally { list.RemoveAll(n => added.Any(a => a.ID == n.ID)); }
    }

    [Fact]
    public async Task Info_ListsTheTeamsWithTheRankingsNumbers_AndShowsTheOneChosen()
    {
        var a = TeamCornerRig.Npc("tc_info_1", "Info One", "Info Wolves", level: 10);
        var b = TeamCornerRig.Npc("tc_info_2", "Info Two", "Info Wolves", level: 20);
        await WithNpcs(new[] { a, b }, async () =>
        {
            var rig = new TeamCornerRig(TeamCornerRig.Hero(), new[] { "info wolves", "" });
            string shown = await rig.Run("ShowTeamInfo");
            long power = 10 + a.Strength + a.Defence + 20 + b.Strength + b.Defence;
            shown.Should().Contain(Loc.Get("team.pick_team_row", "Info Wolves", 2, 15, power));
            shown.Should().Contain(Loc.Get("team.info_header", "Info Wolves"));
            shown.Should().Contain("Info Two");
        });
    }

    [Fact]
    public async Task Join_ListsTheTeams_ANameInAnyCaseChooses_AndThePasswordIsStillAsked()
    {
        var a = TeamCornerRig.Npc("tc_join_1", "Hawk One", "Join Hawks");
        await WithNpcs(new[] { a }, async () =>
        {
            var hero = TeamCornerRig.Hero();
            var wrong = new TeamCornerRig(hero, new[] { "join hawks", "nope", "" });
            string shown = await wrong.Run("JoinTeam");
            shown.Should().Contain(Loc.Get("team.pick_team_join_title"));
            shown.Should().Contain(Loc.Get("team.wrong_password"));
            hero.Team.Should().BeEmpty();

            var right = new TeamCornerRig(hero, new[] { "join hawks", "pw", "" });
            await right.Run("JoinTeam");
            hero.Team.Should().Be("Join Hawks");
        });
    }

    [Fact]
    public async Task Examine_ListsPlayerMembers_AndShowsOne()
    {
        var npc = TeamCornerRig.Npc("tc_exam_1", "Exam Npc", "Exam Band");
        await WithNpcs(new[] { npc }, () => TeamCornerRig.Online(async (db, path) =>
        {
            await db.WriteGameData("tomas", new SaveGameData
            {
                Version = GameConfig.SaveVersion,
                Player = new PlayerData { Name1 = "tomas", Name2 = "Tomas", Team = "Exam Band", Level = 12, Strength = 55, BaseStrength = 55, HP = 90, MaxHP = 90 }
            });
            var hero = TeamCornerRig.Hero(team: "Exam Band");
            var rig = new TeamCornerRig(hero, new[] { "tomas", "" });
            string shown = await rig.Run("ExamineMember");
            shown.Should().Contain("Tomas - ").And.Contain("Exam Npc - ").And.Contain(hero.DisplayName);
            shown.Should().Contain("TOMAS");
            shown.Should().Contain("STR: 55", "the player's strength comes from their save");
        }));
    }

    [Fact]
    public async Task Examine_AnNpc_StillShowsTheNpcCard()
    {
        var npc = TeamCornerRig.Npc("tc_exam_2", "Card Npc", "Card Band");
        await WithNpcs(new[] { npc }, async () =>
        {
            var rig = new TeamCornerRig(TeamCornerRig.Hero(team: "Card Band"), new[] { "card", "" });
            string shown = await rig.Run("ExamineMember");
            shown.Should().Contain("CARD NPC").And.Contain(Loc.Get("team.examine_section_identity"));
            shown.Should().Contain(GameConfig.GetLocalizedRaceName(npc.Race));
        });
    }

    [Fact]
    public async Task Sack_APlayerMember_IsRefused()
    {
        await TeamCornerRig.Online(async (db, path) =>
        {
            TeamCornerRig.PlayerRow(path, "tomas", "Sack Band");
            var rig = new TeamCornerRig(TeamCornerRig.Hero(team: "Sack Band"), new[] { "tomas", "" });
            string shown = await rig.Run("SackMember");
            shown.Should().Contain(Loc.Get("team.sack_player_refused", "Tomas"));
            TeamCornerRig.Scalar(path, "SELECT json_extract(player_data, '$.player.team') FROM players WHERE username = 'tomas'").Should().Be("Sack Band");
        });
    }

    [Fact]
    public async Task Sack_AnNpc_ChangesTheLiveNpc_EvenAfterAReloadAtTheConfirm()
    {
        var npc = TeamCornerRig.Npc("tc_sack_1", "Sack Npc", "Sack Crew");
        await WithNpcs(new[] { npc }, async () =>
        {
            NPC? live = null;
            // line 0 picks, line 1 confirms; the reload lands between them
            var rig = new TeamCornerRig(TeamCornerRig.Hero(team: "Sack Crew"), new[] { "sack npc", "y", "" },
                i => { if (i == 1) live = TeamCornerRig.Reload(npc); });
            await rig.Run("SackMember");
            live.Should().NotBeNull();
            live!.Team.Should().BeEmpty("the sack must land on the NPC in the live list");
        });
    }

    [Fact]
    public async Task Resurrect_ListsTheDead_AsksForTheCost_AndChangesTheLiveNpc()
    {
        var dead = TeamCornerRig.Npc("tc_res_1", "Res Npc", "Res Crew", level: 3, dead: true);
        await WithNpcs(new[] { dead }, async () =>
        {
            var hero = TeamCornerRig.Hero(team: "Res Crew", gold: 10000);
            var no = new TeamCornerRig(hero, new[] { "res", "n", "" });
            string shown = await no.Run("ResurrectTeammate");
            shown.Should().Contain(Loc.Get("team.confirm_resurrect", "Res Npc", $"{3000:N0}").TrimEnd());
            hero.Gold.Should().Be(10000);
            dead.IsAlive.Should().BeFalse();

            NPC? live = null;
            var yes = new TeamCornerRig(hero, new[] { "res", "y", "" }, i => { if (i == 1) live = TeamCornerRig.Reload(dead); });
            await yes.Run("ResurrectTeammate");
            live.Should().NotBeNull();
            live!.IsAlive.Should().BeTrue("the revival must land on the NPC in the live list");
            live.IsDead.Should().BeFalse();
            hero.Gold.Should().Be(7000);
        });
    }

    [Fact]
    public async Task Specialize_ChangesTheLiveNpc_EvenAfterAReloadAtTheSpecMenu()
    {
        var npc = TeamCornerRig.Npc("tc_spec_1", "Spec Npc", "Spec Crew");
        var specs = UsurperRemake.Data.SpecializationData.GetSpecsForClass(npc.Class);
        specs.Should().NotBeEmpty();
        await WithNpcs(new[] { npc }, async () =>
        {
            NPC? live = null;
            var rig = new TeamCornerRig(TeamCornerRig.Hero(team: "Spec Crew"), new[] { "spec npc", "1", "" },
                i => { if (i == 1) live = TeamCornerRig.Reload(npc); });
            await rig.Run("SpecializeMember");
            live.Should().NotBeNull();
            live!.Specialization.Should().Be(specs[0].Spec, "the choice must land on the NPC in the live list");
        });
    }

    [Fact]
    public async Task Equip_ListsTheLivingMembers_InThePicker()
    {
        var npc = TeamCornerRig.Npc("tc_eq_1", "Equip Npc", "Equip Crew");
        var gone = TeamCornerRig.Npc("tc_eq_2", "Fallen Npc", "Equip Crew", dead: true);
        await WithNpcs(new[] { npc, gone }, async () =>
        {
            var rig = new TeamCornerRig(TeamCornerRig.Hero(team: "Equip Crew"), new[] { "0" });
            string shown = await rig.Run("EquipMember");
            shown.Should().Contain("[1] Equip Npc").And.NotContain("Fallen Npc");
            shown.Should().Contain(Loc.Get("base.pick_nav").TrimEnd());
        });
    }
}
