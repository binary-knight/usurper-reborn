using UsurperRemake.Utils;
using UsurperRemake.Systems;
using UsurperRemake.BBS;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;

/// <summary>
/// Team Corner Location - Complete implementation based on Pascal TCORNER.PAS
/// "This is the place where the teams make their decisions"
/// Provides team creation, management, communication, and all team-related functions
/// </summary>
public class TeamCornerLocation : BaseLocation
{
    // Pascal constants from TCORNER.PAS
    private const int LocalMaxY = 200; // max number of teams the routines will handle
    private const int MaxTeamSize = 5; // Maximum members per team

    public TeamCornerLocation() : base(
        GameLocation.TeamCorner,
        "Adventurers Team Corner",
        "The place where gangs gather to plan their strategies and make their decisions."
    ) { }

    protected override void SetupLocation()
    {
        // Pascal-compatible exits
        PossibleExits = new List<GameLocation>
        {
            GameLocation.MainStreet  // Can return to Main Street
        };

        // Team Corner actions
        LocationActions = new List<string>
        {
            "Team Rankings",
            "Info on Teams",
            "Your Team Status",
            "Create Team",
            "Join Team",
            "Quit Team",
            "Recruit NPC",
            "Examine Member",
            "Password Change",
            "Send Team Message"
        };
    }
    protected override void DisplayLocation()
    {
        if (IsScreenReader) { DisplayLocationSR(); return; }
        if (IsBBSSession) { DisplayLocationBBS(); return; }

        terminal.ClearScreen();

        // Header
        WriteBoxHeader(Loc.Get("team_corner.header"), "bright_magenta");
        terminal.WriteLine("");

        // Atmospheric description
        terminal.SetColor("white");
        terminal.WriteLine(Loc.Get("team.desc_line1"));
        terminal.WriteLine(Loc.Get("team.desc_line2"));
        terminal.WriteLine("");

        // Show player's team status
        if (!string.IsNullOrEmpty(currentPlayer.Team))
        {
            terminal.SetColor("bright_cyan");
            terminal.WriteLine(Loc.Get("team.your_team", currentPlayer.Team));
            terminal.WriteLine(currentPlayer.CTurf ? Loc.Get("team.turf_control_yes") : Loc.Get("team.turf_control_no"));
            terminal.WriteLine("");
        }
        else
        {
            terminal.SetColor("yellow");
            terminal.WriteLine(Loc.Get("team.no_team_hint"));
            terminal.WriteLine("");
        }

        // v0.62.1 menu cleanup (player report Lv.6 Human Sage on MainStreet):
        // "The option to apply to join a team... does not go away after already
        // joining a team. It's clutter that could probably be removed."
        // Two changes: (1) [A] Apply removed from every menu surface -- it routed
        // to JoinTeam with the comment "Apply is same as join for now", so it was
        // a hidden duplicate of [J] Join. The input handler keeps "A" as an alias
        // for muscle-memory backward compat; only the display is gone. (2) Apply
        // the same "don't show options that don't work" principle to the rest of
        // the menu: gate Create/Join on !inTeam and team-management options on
        // inTeam. Universal options (Rankings/Info/Return/Status) always show.
        bool inTeam = !string.IsNullOrEmpty(currentPlayer.Team);

        // v0.62.1 single-player suppression (player report same Sage session):
        // "I'm not sure there's any purpose to a password for teams in the
        // singleplayer version of the game? Nor a few other features like
        // messaging teammates." Password locks the team against cross-session
        // join attempts -- meaningless when there's no other session, so gate
        // it on online-mode. Send Message has the same shape (it's a stub
        // even in online mode; in single-player it's pure noise with no NPC
        // recipient). Mirrors the existing W/B/H online-only pattern.
        bool showPassword = inTeam && DoorMode.IsOnlineMode;
        bool showMessage = inTeam && DoorMode.IsOnlineMode;

        // INFO section -- Rankings and Info always show; Password (online + in-team),
        // Examine and Your Status (in-team).
        terminal.SetColor("cyan");
        terminal.WriteLine(Loc.Get("team.section_info"));
        terminal.SetColor("white");
        WriteMenuOption("T", Loc.Get("team.menu_rankings"), showPassword ? "P" : "", showPassword ? Loc.Get("team.menu_password") : "");
        WriteMenuOption("I", Loc.Get("team.menu_info"), inTeam ? "E" : "", inTeam ? Loc.Get("team.menu_examine") : "");
        if (inTeam) WriteMenuOption("Y", Loc.Get("team.menu_your_status"), "", "");
        terminal.WriteLine("");

        // ACTIONS section — Create / Join when not in a team; team-management
        // when in a team. Apply removed entirely.
        terminal.SetColor("cyan");
        terminal.WriteLine(Loc.Get("team.section_actions"));
        terminal.SetColor("white");
        if (!inTeam)
        {
            WriteMenuOption("C", Loc.Get("team.menu_create"), "J", Loc.Get("team.menu_join"));
        }
        else
        {
            WriteMenuOption("L", Loc.Get("team.menu_quit"), "N", Loc.Get("team.menu_recruit_npc")); // v0.64.2: Quit Team moved Q -> L (Q = back)
            WriteMenuOption("2", Loc.Get("team.menu_sack"), "G", Loc.Get("team.menu_equip"));
            WriteMenuOption("X", Loc.Get("team.menu_specialize"), "V", Loc.Get("team.menu_view_inventories"));
        }
        terminal.WriteLine("");

        // COMM section -- in-team only. Send Message gated on online-mode (see
        // showMessage comment above). Resurrect Teammate stays available in
        // single-player because NPC teammates can die and players want to
        // revive them. Skip the entire section header when not in a team.
        if (inTeam)
        {
            terminal.SetColor("cyan");
            terminal.WriteLine(Loc.Get("team.section_comm"));
            terminal.SetColor("white");
            WriteMenuOption(showMessage ? "M" : "U", showMessage ? Loc.Get("team.menu_message") : Loc.Get("team.menu_resurrect"), showMessage ? "U" : "", showMessage ? Loc.Get("team.menu_resurrect") : "");
            if (DoorMode.IsOnlineMode)
            {
                WriteMenuOption("W", Loc.Get("team.menu_recruit_player"), "", "");
                terminal.WriteLine("");
                terminal.SetColor("cyan");
                terminal.WriteLine(Loc.Get("team.section_online"));
                terminal.SetColor("white");
                WriteMenuOption("B", Loc.Get("team.menu_battle"), "H", Loc.Get("team.menu_hq"));
            }
            terminal.WriteLine("");
        }

        terminal.SetColor("yellow");
        terminal.WriteLine(Loc.Get("team.section_nav"));
        terminal.SetColor("white");
        WriteMenuOption("R", Loc.Get("team.menu_return"), "S", Loc.Get("team.menu_status"));
        terminal.WriteLine("");
    }

    private void DisplayLocationSR()
    {
        terminal.ClearScreen();
        terminal.SetColor("bright_magenta");
        terminal.WriteLine(Loc.Get("team.sr_title"));
        terminal.WriteLine("");

        // Team status
        if (!string.IsNullOrEmpty(currentPlayer.Team))
        {
            terminal.SetColor("white");
            terminal.WriteLine(Loc.Get("team.your_team", currentPlayer.Team));
            terminal.WriteLine(currentPlayer.CTurf ? Loc.Get("team.sr_turf_yes") : Loc.Get("team.sr_turf_no"));
        }
        else
        {
            terminal.SetColor("yellow");
            terminal.WriteLine(Loc.Get("team.sr_no_team"));
        }
        terminal.WriteLine("");

        // v0.62.1 menu cleanup -- see DisplayLocation() for the rationale on
        // Apply / Create / Join / team-management gating, AND the additional
        // single-player suppression of Password and Send Message.
        bool inTeamSr = !string.IsNullOrEmpty(currentPlayer.Team);
        bool showPasswordSr = inTeamSr && DoorMode.IsOnlineMode;
        bool showMessageSr = inTeamSr && DoorMode.IsOnlineMode;

        terminal.SetColor("cyan");
        terminal.WriteLine(Loc.Get("team.sr_info_section"));
        WriteSRMenuOption("T", Loc.Get("team_corner.rankings"));
        WriteSRMenuOption("I", Loc.Get("team_corner.info"));
        if (inTeamSr)
        {
            WriteSRMenuOption("Y", Loc.Get("team_corner.your_status"));
            if (showPasswordSr) WriteSRMenuOption("P", Loc.Get("team_corner.password"));
            WriteSRMenuOption("E", Loc.Get("team_corner.examine"));
        }
        terminal.WriteLine("");

        terminal.SetColor("cyan");
        terminal.WriteLine(Loc.Get("team.sr_actions_section"));
        if (!inTeamSr)
        {
            WriteSRMenuOption("C", Loc.Get("team_corner.create"));
            WriteSRMenuOption("J", Loc.Get("team_corner.join"));
        }
        else
        {
            WriteSRMenuOption("L", Loc.Get("team_corner.quit")); // v0.64.2: Quit Team moved Q -> L (Q = back)
            WriteSRMenuOption("N", Loc.Get("team_corner.recruit_npc"));
            WriteSRMenuOption("2", Loc.Get("team_corner.sack"));
            WriteSRMenuOption("G", Loc.Get("team_corner.equip"));
            WriteSRMenuOption("X", Loc.Get("team_corner.specialize"));
            WriteSRMenuOption("V", Loc.Get("team.menu_view_inventories").Trim());   // v1.1.12: was missing here
        }
        terminal.WriteLine("");

        if (inTeamSr)
        {
            terminal.SetColor("cyan");
            terminal.WriteLine(Loc.Get("team.sr_comm_section"));
            if (showMessageSr) WriteSRMenuOption("M", Loc.Get("team_corner.message"));
            WriteSRMenuOption("U", Loc.Get("team_corner.resurrect"));
            if (DoorMode.IsOnlineMode)
            {
                WriteSRMenuOption("W", Loc.Get("team_corner.recruit_player"));
                terminal.SetColor("cyan");
                terminal.WriteLine(Loc.Get("team.sr_online_section"));
                WriteSRMenuOption("B", Loc.Get("team_corner.battle"));
                WriteSRMenuOption("H", Loc.Get("team_corner.headquarters"));
            }
            terminal.WriteLine("");
        }

        WriteSRMenuOption("R", Loc.Get("team_corner.return"));
        WriteSRMenuOption("S", Loc.Get("marketplace.status"));
        terminal.WriteLine("");
    }

    private void WriteMenuOption(string key1, string label1, string key2, string label2)
    {
        if (!string.IsNullOrEmpty(key1))
        {
            terminal.SetColor("darkgray");
            terminal.Write("[");
            terminal.SetColor("bright_yellow");
            terminal.Write(key1);
            terminal.SetColor("darkgray");
            terminal.Write("] ");
            terminal.SetColor("white");
            terminal.Write(label1.PadRight(25));
        }

        if (!string.IsNullOrEmpty(key2))
        {
            terminal.SetColor("darkgray");
            terminal.Write("[");
            terminal.SetColor("bright_yellow");
            terminal.Write(key2);
            terminal.SetColor("darkgray");
            terminal.Write("] ");
            terminal.SetColor("white");
            terminal.Write(label2);
        }
        terminal.WriteLine("");
    }

    /// <summary>
    /// Compact BBS display for 80x25 terminals.
    /// </summary>
    private void DisplayLocationBBS()
    {
        terminal.ClearScreen();
        ShowBBSHeader(Loc.Get("team_corner.header"));

        // 1-line team status
        if (!string.IsNullOrEmpty(currentPlayer.Team))
        {
            terminal.SetColor("bright_cyan");
            terminal.Write(Loc.Get("team.bbs_team_label", currentPlayer.Team));
            if (currentPlayer.CTurf)
            {
                terminal.SetColor("bright_yellow");
                terminal.Write(Loc.Get("team.bbs_controls_town"));
            }
            terminal.WriteLine("");
        }
        else
        {
            terminal.SetColor("yellow");
            terminal.WriteLine(Loc.Get("team.bbs_no_team"));
        }
        terminal.WriteLine("");

        // v0.62.1 menu cleanup -- see DisplayLocation() for the rationale on
        // Apply / Create / Join / team-management gating AND on the single-player
        // suppression of Password and Send Message.
        bool inTeamBbs = !string.IsNullOrEmpty(currentPlayer.Team);
        bool showPasswordBbs = inTeamBbs && DoorMode.IsOnlineMode;
        bool showMessageBbs = inTeamBbs && DoorMode.IsOnlineMode;

        // INFO row -- always Rankings + Info; in-team gets Examine and Your Team;
        // Password only when in-team AND online (no purpose in single-player).
        terminal.SetColor("cyan");
        terminal.WriteLine(Loc.Get("team.bbs_info"));
        if (inTeamBbs)
        {
            // Build the info row with Password as an optional cell so single-player
            // sessions get a Rankings / Info / Examine / Your Team row without the
            // dead Password slot.
            var infoCells = new System.Collections.Generic.List<(string, string, string)>
            {
                ("T", "bright_yellow", Loc.Get("team.bbs_rankings"))
            };
            if (showPasswordBbs) infoCells.Add(("P", "bright_yellow", Loc.Get("team.bbs_password")));
            infoCells.Add(("I", "bright_yellow", Loc.Get("team.bbs_info")));
            infoCells.Add(("E", "bright_yellow", Loc.Get("team.bbs_examine")));
            infoCells.Add(("Y", "bright_yellow", Loc.Get("team.bbs_your_team")));
            ShowBBSMenuRow(infoCells.ToArray());
        }
        else
        {
            ShowBBSMenuRow(("T", "bright_yellow", Loc.Get("team.bbs_rankings")), ("I", "bright_yellow", Loc.Get("team.bbs_info")));
        }

        // ACTIONS rows -- Create/Join when not in a team; team-management when in.
        terminal.SetColor("cyan");
        terminal.WriteLine(Loc.Get("team.bbs_actions"));
        if (!inTeamBbs)
        {
            ShowBBSMenuRow(("C", "bright_yellow", Loc.Get("team.bbs_create")), ("J", "bright_yellow", Loc.Get("team.bbs_join")));
        }
        else
        {
            ShowBBSMenuRow(("L", "bright_yellow", Loc.Get("team.bbs_quit_team")), ("N", "bright_yellow", Loc.Get("team.bbs_recruit_npc")), ("2", "bright_yellow", Loc.Get("team.bbs_sack_member")), ("G", "bright_yellow", Loc.Get("team.bbs_equip_mbr")), ("X", "bright_yellow", Loc.Get("team.bbs_specialize")));
            // Message gated on online-mode; Resurrect always shown when in-team. v1.1.12: View Inventories added.
            if (showMessageBbs)
            {
                ShowBBSMenuRow(("M", "bright_yellow", Loc.Get("team.bbs_message")), ("U", "bright_yellow", Loc.Get("team.bbs_resurrect")), ("V", "bright_yellow", Loc.Get("team.bbs_inventories")));
            }
            else
            {
                ShowBBSMenuRow(("U", "bright_yellow", Loc.Get("team.bbs_resurrect")), ("V", "bright_yellow", Loc.Get("team.bbs_inventories")));
            }
            if (DoorMode.IsOnlineMode)
            {
                ShowBBSMenuRow(("W", "bright_yellow", Loc.Get("team.bbs_recruit_ally")), ("B", "bright_yellow", Loc.Get("team.bbs_team_battle")), ("H", "bright_yellow", Loc.Get("team.bbs_hq")));
            }
        }
        ShowBBSMenuRow(("R", "bright_yellow", Loc.Get("team.bbs_main_street")));

        ShowBBSFooter();
    }

    protected override async Task<bool> ProcessChoice(string choice)
    {
        if (string.IsNullOrWhiteSpace(choice))
            return false;

        var upperChoice = choice.ToUpper().Trim();

        // Handle global quick commands first — keeps `!` as the Report Bug shortcut
        // consistent with every other location (v0.57.10 Coosh report: the prior local
        // intercept of `!` for Resurrect clashed with the global bug-report key).
        var (handled, shouldExit) = await TryProcessGlobalCommand(choice);
        if (handled) return shouldExit;

        switch (upperChoice)
        {
            case "U":
                await ResurrectTeammate();
                return false;

            case "T":
                await ShowTeamRankings();
                return false;

            case "I":
                await ShowTeamInfo();
                return false;

            case "Y":
                await ShowYourTeamStatus();
                return false;

            case "C":
                await CreateTeam();
                return false;

            case "J":
                await JoinTeam();
                return false;

            case "A":
                // v0.62.1: [A] Apply is removed from the displayed menu (was
                // redundant with [J] Join -- the code comment that used to live
                // here said "Apply is same as join for now" since at least
                // v0.49). The case is kept as a hidden alias so existing muscle
                // memory and old hotkey docs still route to JoinTeam. JoinTeam
                // itself early-exits cleanly if the player is already in a team.
                await JoinTeam();
                return false;

            case "L":
                // v0.64.2 (player feedback on key consistency): Quit Team moved
                // from [Q] to [L] (Leave Team). Q was a destructive landmine --
                // in most town locations Q backs out to Main Street, so a player
                // reflexively pressing Q then confirming with Y could nuke their
                // team membership while trying to LEAVE THE MENU. Q now backs
                // out like everywhere else (case below).
                await QuitTeam();
                return false;

            case "Q":
                // v0.64.2: Q = back out (standardized). Was QuitTeam (moved to L).
                await NavigateToLocation(GameLocation.MainStreet);
                return true;

            case "N":
                await RecruitNPCToTeam();
                return false;

            case "E":
                await ExamineMember();
                return false;

            case "P":
                // v0.62.1: Password is meaningless in single-player (no cross-session
                // join attempts to lock out). Gated to online mode to match the
                // existing W/B/H online-only pattern; the hidden hotkey silently
                // no-ops in single-player so old muscle memory doesn't get an
                // unexpected "not in team" beat for a feature that shouldn't exist.
                if (DoorMode.IsOnlineMode)
                    await ChangeTeamPassword();
                return false;

            case "M":
                // v0.62.1: Send Message has no NPC recipient in single-player and
                // the handler itself is a stub (prints "message sent!" then drops
                // the text on the floor with a "// Could integrate with mail system
                // here" comment). Gated the same way as Password.
                if (DoorMode.IsOnlineMode)
                    await SendTeamMessage();
                return false;

            case "2":
                await SackMember();
                return false;

            case "G":
                await EquipMember();
                return false;

            case "X":
                await SpecializeMember();
                return false;

            case "V":
                await ViewTeamInventories();
                return false;

            case "W":
                if (DoorMode.IsOnlineMode)
                    await RecruitPlayerAlly();
                return false;

            case "B":
                if (DoorMode.IsOnlineMode)
                    await TeamWarMenu();
                return false;

            case "H":
                if (DoorMode.IsOnlineMode)
                    await TeamHeadquartersMenu();
                return false;

            case "R":
                await NavigateToLocation(GameLocation.MainStreet);
                return true;

            case "S":
                await ShowStatus();
                return false;

            case "?":
                // Menu is already displayed
                return false;

            default:
                terminal.WriteLine(Loc.Get("team.invalid_choice"), "red");
                await Task.Delay(1500);
                return false;
        }
    }

    #region Team Management Functions

    /// <summary>
    /// Show team rankings - all teams sorted by power
    /// </summary>
    private async Task ShowTeamRankings()
    {
        var teamGroups = await LoadTeamRankings();

        if (teamGroups.Count == 0)
        {
            terminal.ClearScreen();
            WriteBoxHeader(Loc.Get("team_corner.rankings_header"), "bright_magenta");
            terminal.WriteLine("");
            terminal.SetColor("yellow");
            terminal.WriteLine(Loc.Get("team.no_teams_yet"));
            terminal.WriteLine(Loc.Get("team.be_first"));
            terminal.WriteLine("");
            terminal.SetColor("darkgray");
            terminal.WriteLine(Loc.Get("ui.press_enter"));
            await terminal.ReadKeyAsync();
            return;
        }

        // v1.1.12: the name column is as wide as the longest name (a long name pushed the columns out of line),
        // and the list pages
        string NameDisplay(TeamRankingRow t) => t.IsPlayerTeam ? $"{t.TeamName} {Loc.Get("team.you_suffix")}" : t.TeamName;
        int nameWidth = Math.Max(24, teamGroups.Max(t => NameDisplay(t).Length));
        await ShowPaged(teamGroups,
            header: () =>
            {
                terminal.ClearScreen();
                WriteBoxHeader(Loc.Get("team_corner.rankings_header"), "bright_magenta");
                terminal.WriteLine("");
                terminal.SetColor("white");
                terminal.WriteLine($"{Loc.Get("team.rank_col_rank"),-5} {Loc.Get("team.rank_col_name").PadRight(nameWidth)} {Loc.Get("team.rank_col_mbrs"),-6} {Loc.Get("team.rank_col_power"),-8} {Loc.Get("team.rank_col_avg_lvl"),-8} {Loc.Get("team.rank_col_turf"),-5}");
                if (!IsScreenReader)
                {
                    terminal.SetColor("darkgray");
                    terminal.WriteLine(new string('─', 36 + nameWidth));
                }
            },
            renderRow: (team, index) =>
            {
                terminal.SetColor(team.ControlsTurf ? "bright_yellow" : team.IsPlayerTeam ? "bright_cyan" : "white");
                string turfMark = team.ControlsTurf ? "*" : "-";
                terminal.WriteLine($"{index + 1,-5} {NameDisplay(team).PadRight(nameWidth)} {team.MemberCount,-6} {team.TotalPower,-8} {team.AverageLevel,-8} {turfMark,-5}");
            },
            footer: () =>
            {
                terminal.WriteLine("");
                terminal.SetColor("bright_yellow");
                terminal.WriteLine(Loc.Get("team.turf_legend"));
            });
    }

    /// <summary>
    /// v1.1.12: the rankings rows (the teams, their members, power and level), read the way the rankings
    /// screen reads them; [I] Info and [J] Join list the same rows.
    /// </summary>
    private async Task<List<TeamRankingRow>> LoadTeamRankings()
    {
        // online, the player side is every player's save in one query (the viewer's own save left out,
        // the in-memory character is added instead); offline it is only the in-memory character
        List<PlayerTeamInfo> playerTeams = new();
        if (DoorMode.IsOnlineMode && SaveSystem.Instance.Backend is SqlSaveBackend backend)
            playerTeams = await backend.GetTeamRankingStats(GameEngine.InheritanceKey(currentPlayer));
        return BuildTeamRankings(NPCSpawnSystem.Instance.ActiveNPCs, playerTeams, currentPlayer);
    }

    /// <summary>v1.1.12: one team as a picker row, with the rankings' numbers.</summary>
    private static string TeamPickRow(TeamRankingRow t) =>
        Loc.Get("team.pick_team_row", t.TeamName, t.MemberCount, t.AverageLevel, t.TotalPower)
        + (t.ControlsTurf ? " " + Loc.Get("team.pick_team_turf") : "")
        + (t.IsPlayerTeam ? " " + Loc.Get("team.you_suffix") : "");

    /// <summary>
    /// v1.1.12: a read-only list shown a page at a time (N/P page, Enter leaves). header draws the screen
    /// top, renderRow one row by its index in the whole list, footer what follows the rows.
    /// </summary>
    private async Task ShowPaged<T>(IReadOnlyList<T> rows, Action header, Action<T, int> renderRow, Action? footer = null, int pageSize = 15)
    {
        int pageIndex = 0;
        while (true)
        {
            int totalPages = Math.Max(1, (rows.Count + pageSize - 1) / pageSize);
            pageIndex = Math.Clamp(pageIndex, 0, totalPages - 1);
            header();
            int first = pageIndex * pageSize;
            var page = rows.Skip(first).Take(pageSize).ToList();
            for (int i = 0; i < page.Count; i++) renderRow(page[i], first + i);
            footer?.Invoke();
            terminal.WriteLine("");
            if (totalPages <= 1)
            {
                terminal.SetColor("darkgray");
                terminal.WriteLine(Loc.Get("ui.press_enter"));
                await terminal.ReadKeyAsync();
                return;
            }
            terminal.SetColor("darkgray");
            terminal.WriteLine(Loc.Get("team.recruit_page_footer", first + 1, first + page.Count, rows.Count, pageIndex + 1, totalPages));
            string input = ((await terminal.GetInput(Loc.Get("team.page_nav"))) ?? "").Trim().ToUpperInvariant();
            if (input == "N") { pageIndex++; continue; }
            if (input == "P") { pageIndex--; continue; }
            return;
        }
    }

    internal sealed record TeamRankingRow(string TeamName, int MemberCount, long TotalPower, int AverageLevel, bool ControlsTurf, bool IsPlayerTeam);

    /// <summary>
    /// v1.1.12: one row per team: living NPC members, the player members from the saves (playerTeams, which
    /// must not include the viewer's save) and the viewer from memory, each counted once. Power is
    /// level + strength + defence summed; a team with no members is left out. Sorted by power.
    /// </summary>
    internal static List<TeamRankingRow> BuildTeamRankings(IEnumerable<NPC> npcs, IEnumerable<PlayerTeamInfo> playerTeams, Character? viewer)
    {
        // v1.1.12: grouped by the exact name; older case variants are separate teams (a join checks the exact name)
        var acc = new Dictionary<string, (int Members, long LevelSum, long Power, bool Turf)>(StringComparer.Ordinal);
        var order = new List<string>();
        void Add(string team, int members, long levels, long power, bool turf)
        {
            if (!acc.TryGetValue(team, out var a)) { order.Add(team); a = default; }
            acc[team] = (a.Members + members, a.LevelSum + levels, a.Power + power, a.Turf || turf);
        }

        foreach (var n in npcs)
            if (!string.IsNullOrEmpty(n.Team) && n.IsAlive)
                Add(n.Team, 1, n.Level, n.Level + (long)n.Strength + (long)n.Defence, n.CTurf);
        if (viewer != null && !string.IsNullOrEmpty(viewer.Team))
            Add(viewer.Team, 1, viewer.Level, viewer.Level + (long)viewer.Strength + (long)viewer.Defence, viewer.CTurf);
        foreach (var pt in playerTeams)
            if (!string.IsNullOrEmpty(pt.TeamName))
                Add(pt.TeamName, pt.MemberCount, pt.LevelSum, pt.PowerSum, pt.ControlsTurf);

        return order
            .Where(t => acc[t].Members > 0)
            .Select(t => new TeamRankingRow(t, acc[t].Members, acc[t].Power, (int)(acc[t].LevelSum / acc[t].Members), acc[t].Turf,
                viewer != null && string.Equals(t, viewer.Team, StringComparison.Ordinal)))
            .OrderByDescending(r => r.TotalPower)
            .ToList();
    }

    /// <summary>
    /// Show info on a specific team
    /// </summary>
    private async Task ShowTeamInfo()
    {
        // v1.1.12: the teams are listed (the rankings' rows) instead of asking for an exact name
        var teams = await LoadTeamRankings();
        if (teams.Count == 0)
        {
            terminal.WriteLine("");
            terminal.SetColor("yellow");
            terminal.WriteLine(Loc.Get("team.no_teams_yet"));
            await Task.Delay(2000);
            return;
        }
        var picked = await PickFromList(teams, TeamPickRow, t => t.TeamName, "team.pick_team_info_title",
            t => t.ControlsTurf ? "bright_yellow" : t.IsPlayerTeam ? "bright_cyan" : "white");
        if (picked == null) return;
        string teamName = picked.TeamName;

        terminal.ClearScreen();
        WriteSectionHeader(Loc.Get("team.info_header", teamName), "bright_cyan");
        terminal.WriteLine("");

        await ShowTeamMembers(teamName, false);

        terminal.WriteLine("");
        terminal.SetColor("darkgray");
        terminal.WriteLine(Loc.Get("ui.press_enter"));
        await terminal.ReadKeyAsync();
    }

    /// <summary>
    /// Show your team's status
    /// </summary>
    private async Task ShowYourTeamStatus()
    {
        if (string.IsNullOrEmpty(currentPlayer.Team))
        {
            terminal.WriteLine("");
            terminal.SetColor("red");
            terminal.WriteLine(Loc.Get("team.not_in_team"));
            terminal.WriteLine("");
            await Task.Delay(2000);
            return;
        }

        terminal.ClearScreen();
        WriteSectionHeader(Loc.Get("team.status_header", currentPlayer.Team), "bright_cyan");
        terminal.WriteLine("");

        terminal.SetColor("white");
        terminal.WriteLine(Loc.Get("team.team_name_label", currentPlayer.Team));

        if (currentPlayer.CTurf)
        {
            terminal.SetColor("bright_yellow");
            terminal.WriteLine(Loc.Get("team.town_control_yes"));
        }
        else
        {
            terminal.SetColor("red");
            terminal.WriteLine(Loc.Get("team.town_control_no"));
        }

        terminal.SetColor("white");
        terminal.WriteLine(Loc.Get("team.team_record", currentPlayer.TeamRec));
        terminal.WriteLine("");

        await ShowTeamMembers(currentPlayer.Team, true);

        terminal.WriteLine("");
        terminal.SetColor("darkgray");
        terminal.WriteLine(Loc.Get("ui.press_enter"));
        await terminal.ReadKeyAsync();
    }

    /// <summary>
    /// Show members of a team
    /// </summary>
    /// <summary>
    /// Persists a just-changed team membership to the player's save row immediately.
    ///
    /// Player report: "friend says he sees me as part of the team, when I check it
    /// says I'm the only one in the team." Team membership for players lives in the
    /// player's own save blob (player_data.player.team), and the roster screen builds
    /// its list by querying that column across all players. Joining or creating a team
    /// only set Character.Team in memory, so until the joiner's next autosave (or
    /// logout) every OTHER player's roster query still read their old value -- each
    /// side saw a different team. Remaking the team did not help, because the remake
    /// had exactly the same problem.
    ///
    /// Single-player needs nothing here: the roster is built from the in-memory NPC
    /// list, and there is no second client reading a stale row.
    /// </summary>
    private async Task PersistTeamMembershipChange()
    {
        if (!DoorMode.IsOnlineMode) return;
        try
        {
            await GameEngine.Instance.SaveCurrentGame();
        }
        catch (Exception ex)
        {
            DebugLogger.Instance.LogError("TEAM",
                $"Failed to persist team membership change: {ex.Message}");
        }
    }

    /// <summary>v1.1.12: online, a save that must land now (gold or items moved against a shared row); AutoSave's
    /// throttle would otherwise skip it for up to a minute. Single-player keeps the player and the NPCs in
    /// one save, so there is no second row to keep in step; true there. True when written.</summary>
    private async Task<bool> ForcePlayerSave()
    {
        if (!DoorMode.IsOnlineMode) return true;
        try { return await SaveSystem.Instance.AutoSave(currentPlayer, force: true); }
        catch (Exception ex)
        {
            DebugLogger.Instance.LogError("TEAM", $"Forced save failed: {ex.Message}");
            return false;
        }
    }

    /// <summary>v1.1.12: one member of the viewer's team for the pickers: an NPC, or a player (the viewer or a save).</summary>
    internal sealed record TeamMemberEntry(NPC? Npc, PlayerSummary? Player, bool IsViewer = false)
    {
        public string Name => Npc?.DisplayName ?? Player?.DisplayName ?? "";
    }

    /// <summary>v1.1.12: every member of the viewer's team: the viewer (if asked), the player members from
    /// the saves (online), then the NPCs, the dead included.</summary>
    private async Task<List<TeamMemberEntry>> GetTeamMemberEntries(bool includeViewer)
    {
        var list = new List<TeamMemberEntry>();
        string team = currentPlayer.Team;
        if (includeViewer)
            list.Add(new TeamMemberEntry(null, new PlayerSummary
            {
                Username = GameEngine.InheritanceKey(currentPlayer),
                DisplayName = currentPlayer.DisplayName,
                Level = currentPlayer.Level,
                ClassId = (int)currentPlayer.Class,
                IsOnline = true,
            }, IsViewer: true));
        if (DoorMode.IsOnlineMode && SaveSystem.Instance.Backend is SqlSaveBackend backend)
            foreach (var pm in await backend.GetPlayerTeamMembers(team, excludeDisplayName: currentPlayer.DisplayName))
                list.Add(new TeamMemberEntry(null, pm));
        foreach (var npc in NPCSpawnSystem.Instance.ActiveNPCs.Where(n => n.Team == team).OrderByDescending(n => n.Level))
            list.Add(new TeamMemberEntry(npc, null));
        return list;
    }

    private static string SpecTag(NPC npc) => npc.Specialization == ClassSpecialization.None ? ""
        : $" [{UsurperRemake.Data.SpecializationData.GetSpec(npc.Specialization)?.Name ?? npc.Specialization.ToString()}]";

    private static string MemberPickRow(TeamMemberEntry e)
    {
        if (e.Npc != null)
        {
            var n = e.Npc;
            string state = n.IsAlive ? "" : n.IsPermaDead || n.IsAgedDeath ? " " + Loc.Get("team.pick_gone_tag") : Loc.Get("team.member_dead_tag");
            return $"{n.DisplayName} - {Loc.Get("inn.npc_level_class", n.Level, GameConfig.GetLocalizedClassName(n.Class))}{SpecTag(n)}{state}";
        }
        var p = e.Player!;
        string tag = e.IsViewer ? " " + Loc.Get("team.you_suffix") : " " + Loc.Get("team.pick_player_tag") + (p.IsOnline ? Loc.Get("team.online_tag") : "");
        return $"{p.DisplayName} - {Loc.Get("inn.npc_level_class", p.Level, GameConfig.GetLocalizedClassName(p.ClassId))}{tag}";
    }

    private static string MemberPickColor(TeamMemberEntry e) => e.Npc == null ? "bright_cyan" : e.Npc.IsAlive ? "white" : "red";

    /// <summary>
    /// v1.1.12: the live copy of an NPC picked from a list. A world_state reload while the player sat at a
    /// prompt replaces every NPC object, and a change to the old one is lost (the Recruit fix, v0.57.x,
    /// ConfirmAndRecruit). Null when that NPC is gone.
    /// </summary>
    internal static NPC? LiveTeamNpc(NPC captured)
    {
        var active = NPCSpawnSystem.Instance.ActiveNPCs;
        if (string.IsNullOrEmpty(captured.ID)) return active.Contains(captured) ? captured : null;
        return active.FirstOrDefault(n => n.ID == captured.ID);
    }

    /// <summary>
    /// v1.1.12: the slots a team uses, of MaxTeamSize: its NPCs, the dead included (a dead member holds the
    /// slot until sacked; one who can never come back does not), plus its players.
    /// </summary>
    internal static int CountTeamSlots(IEnumerable<NPC> npcs, string team, int playerMembers) =>
        npcs.Count(n => n.Team == team && !n.IsPermaDead && !n.IsAgedDeath) + playerMembers;

    /// <summary>v1.1.12: CountTeamSlots for a team, with its player members from the saves online (the viewer
    /// counted from memory when on it) and only the viewer offline.</summary>
    private async Task<int> TeamSlotsUsed(string team)
    {
        int players = team == currentPlayer.Team ? 1 : 0;
        if (DoorMode.IsOnlineMode && SaveSystem.Instance.Backend is SqlSaveBackend backend)
            players += (await backend.GetPlayerTeamMembers(team, excludeDisplayName: currentPlayer.DisplayName)).Count;
        return CountTeamSlots(NPCSpawnSystem.Instance.ActiveNPCs, team, players);
    }

    private void WriteMemberStatLines(Character c)
    {
        terminal.WriteLine(Loc.Get("team.examine_stats1", c.Strength, c.Dexterity, c.Agility, c.Constitution));
        terminal.WriteLine(Loc.Get("team.examine_stats2", c.Intelligence, c.Wisdom, c.Charisma, c.Defence));
        terminal.WriteLine(Loc.Get("team.examine_stats3", c.Stamina, c.WeapPow, c.ArmPow));
    }

    /// <summary>v1.1.12: a player member's card: the viewer from memory, anyone else from their save.</summary>
    private async Task ExaminePlayerMember(TeamMemberEntry entry)
    {
        var summary = entry.Player!;
        Character? c = entry.IsViewer ? currentPlayer : null;
        if (c == null && SaveSystem.Instance.Backend is SqlSaveBackend backend)
        {
            try
            {
                string key = !string.IsNullOrEmpty(summary.Username) ? summary.Username : backend.ResolvePlayerUsername(summary.DisplayName) ?? summary.DisplayName;
                var data = await backend.ReadGameData(key);
                if (data?.Player != null)
                {
                    c = PlayerCharacterLoader.CreateFromSaveData(data.Player, summary.DisplayName);
                    // v1.1.12: the combat loader starts at full HP and mana and has no age; show the saved ones.
                    // The saved maxima too: the loader's lack the member's awakening boons.
                    if (data.Player.MaxHP > 0) c.MaxHP = data.Player.MaxHP;
                    if (data.Player.MaxMana > 0) c.MaxMana = data.Player.MaxMana;
                    c.HP = Math.Min(data.Player.HP, c.MaxHP);
                    c.Mana = Math.Min(data.Player.Mana, c.MaxMana);
                    c.Age = data.Player.Age;
                }
            }
            catch (Exception ex) { DebugLogger.Instance.LogWarning("TEAM", $"Could not load the save of {summary.DisplayName}: {ex.Message}"); }
        }

        terminal.ClearScreen();
        WriteSectionHeader(summary.DisplayName.ToUpper(), "bright_cyan");
        terminal.WriteLine("");
        terminal.SetColor("bright_white");
        terminal.WriteLine(Loc.Get("team.examine_section_identity"));
        terminal.SetColor("white");
        terminal.WriteLine($"  {Loc.Get("status.class")}: {GameConfig.GetLocalizedClassName(summary.ClassId)}");
        if (c != null)
        {
            terminal.WriteLine($"  {Loc.Get("status.race")}: {GameConfig.GetLocalizedRaceName(c.Race)}");
            terminal.WriteLine($"  {Loc.Get("team.examine_sex")}: {GameConfig.GetLocalizedSexName(c.Sex)}");
            terminal.WriteLine($"  {Loc.Get("team.examine_age")}: {c.Age}");
        }
        terminal.WriteLine($"  {Loc.Get("ui.level")}: {c?.Level ?? summary.Level}");
        terminal.SetColor(summary.IsOnline ? "bright_green" : "gray");
        terminal.WriteLine($"  {(summary.IsOnline ? Loc.Get("team.status_online") : Loc.Get("team.status_offline"))}");
        terminal.WriteLine("");
        if (c != null)
        {
            terminal.SetColor("bright_white");
            terminal.WriteLine(Loc.Get("team.examine_section_combat"));
            terminal.SetColor("white");
            terminal.WriteLine($"  {Loc.Get("combat.bar_hp")}: {c.HP}/{c.MaxHP}");
            terminal.WriteLine($"  {Loc.Get("ui.mana_label")}: {c.Mana}/{c.MaxMana}");
            terminal.WriteLine("");
            WriteMemberStatLines(c);
            terminal.WriteLine("");
        }
        terminal.SetColor("darkgray");
        terminal.WriteLine(Loc.Get("ui.press_enter"));
        await terminal.ReadKeyAsync();
    }

    private async Task ShowTeamMembers(string teamName, bool detailed)
    {
        WriteSectionHeader(Loc.Get("team_corner.members"), "cyan");

        // Get NPCs in this team
        var allNPCs = NPCSpawnSystem.Instance.ActiveNPCs;
        var teamMembers = allNPCs
            .Where(n => n.Team == teamName)
            .OrderByDescending(n => n.Level)
            .ToList();

        // Online mode: also get player members from database.
        List<PlayerSummary> playerMembers = new();
        if (DoorMode.IsOnlineMode)
        {
            var backend = SaveSystem.Instance.Backend as SqlSaveBackend;
            if (backend != null)
            {
                string myUsername = currentPlayer.DisplayName.ToLower();
                playerMembers = await backend.GetPlayerTeamMembers(teamName, myUsername);
            }
        }

        // v0.57.11 (spudman report: "your character is not listed, and is not
        // calculated in the total members"). The DB query above deliberately
        // excludes the viewing player to avoid duplication when a caller wants
        // "other members." For team-status / rankings / info screens we DO want
        // the viewer included in the roster, so synthesize a PlayerSummary for
        // the current player and prepend it when the viewer is on this team.
        if (teamName == currentPlayer.Team)
        {
            playerMembers.Insert(0, new PlayerSummary
            {
                Username = currentPlayer.DisplayName.ToLower(),
                DisplayName = currentPlayer.DisplayName,
                Level = currentPlayer.Level,
                ClassId = (int)currentPlayer.Class,
                Experience = currentPlayer.Experience,
                IsOnline = true, // the viewer is necessarily online to see this screen
                NobleTitle = currentPlayer.NobleTitle,
            });
        }

        bool hasMembers = teamMembers.Count > 0 || playerMembers.Count > 0;
        if (!hasMembers)
        {
            terminal.SetColor("yellow");
            terminal.WriteLine(Loc.Get("team.no_other_members", teamName));
            if (currentPlayer.Team == teamName)
            {
                terminal.WriteLine(Loc.Get("team.only_member"));
            }
            return;
        }

        if (detailed)
        {
            terminal.SetColor("white");
            terminal.WriteLine($"{Loc.Get("team.detail_col_name"),-20} {Loc.Get("team.detail_col_class"),-12} {Loc.Get("team.detail_col_lvl"),-5} {Loc.Get("team.detail_col_hp"),-12} {Loc.Get("team.detail_col_location"),-15} {Loc.Get("team.detail_col_status"),-8}");
            if (!IsScreenReader)
            {
                terminal.SetColor("darkgray");
                terminal.WriteLine(new string('─', 75));
            }

            // Show player members first
            foreach (var pm in playerMembers)
            {
                terminal.SetColor("bright_cyan");
                string className = GameConfig.GetLocalizedClassName(pm.ClassId);   // v1.1.12: localized, was the enum name
                string onlineStatus = pm.IsOnline ? Loc.Get("team.status_online") : Loc.Get("team.status_offline");
                terminal.WriteLine($"{pm.DisplayName,-20} {className,-12} {pm.Level,-5} {"?",-12} {"?",-15} {onlineStatus,-8}");
            }

            // Show NPC members
            foreach (var member in teamMembers)
            {
                string hpDisplay = $"{member.HP}/{member.MaxHP}";
                string location = member.CurrentLocation ?? Loc.Get("team.unknown");
                if (location.Length > 14) location = location.Substring(0, 14);

                if (member.IsAlive)
                    terminal.SetColor("white");
                else
                    terminal.SetColor("red");

                string status = member.IsAlive ? Loc.Get("team.status_alive") : (member.IsPermaDead ? Loc.Get("team.status_gone") : Loc.Get("team.status_dead_label"));
                string classCol = GameConfig.GetLocalizedClassName(member.Class) + SpecTag(member);   // v1.1.12: localized class
                terminal.WriteLine($"{member.DisplayName,-20} {classCol,-12} {member.Level,-5} {hpDisplay,-12} {location,-15} {status,-8}");
            }

            terminal.WriteLine("");
            terminal.SetColor("cyan");
            int totalCount = teamMembers.Count + playerMembers.Count;
            terminal.WriteLine(Loc.Get("team.total_members", totalCount, playerMembers.Count, teamMembers.Count));
        }
        else
        {
            // Show player members
            foreach (var pm in playerMembers)
            {
                string className = GameConfig.GetLocalizedClassName(pm.ClassId);
                string onlineTag = pm.IsOnline ? Loc.Get("team.online_tag") : "";
                terminal.SetColor("bright_cyan");
                terminal.WriteLine($"  {pm.DisplayName} - {Loc.Get("inn.npc_level_class", pm.Level, className)}{onlineTag}");
            }

            // Show NPC members
            foreach (var member in teamMembers)
            {
                string status = member.IsAlive ? "" : Loc.Get("team.member_dead_tag");
                terminal.SetColor("white");
                terminal.WriteLine($"  {member.DisplayName} - {Loc.Get("inn.npc_level_class", member.Level, GameConfig.GetLocalizedClassName(member.Class))}{SpecTag(member)}{status}");
            }
        }
    }

    /// <summary>
    /// Calculate the cost to create a new team
    /// Scales with player level to remain a meaningful investment
    /// </summary>
    private long GetTeamCreationCost()
    {
        return Math.Max(2000, currentPlayer.Level * 500);
    }

    /// <summary>
    /// Create a new team
    /// </summary>
    private async Task CreateTeam()
    {
        // v0.57.13: kings are forced out of their team on throne ascension; this block prevents
        // them from rejoining or forming a new one while wearing the crown. Previously an ex-team-
        // member king could just walk back to Team Corner and re-form, making the ascension-eviction
        // pointless.
        if (currentPlayer.King)
        {
            terminal.WriteLine("");
            terminal.SetColor("red");
            terminal.WriteLine(Loc.Get("team.king_cannot_join"));
            terminal.WriteLine("");
            await Task.Delay(2000);
            return;
        }

        if (!string.IsNullOrEmpty(currentPlayer.Team))
        {
            terminal.WriteLine("");
            terminal.SetColor("red");
            terminal.WriteLine(Loc.Get("team.already_in_team", currentPlayer.Team));
            terminal.WriteLine(Loc.Get("team.quit_current_first"));
            terminal.WriteLine("");
            await Task.Delay(2000);
            return;
        }

        // Check if player can afford to create a team
        long creationCost = GetTeamCreationCost();
        if (currentPlayer.Gold < creationCost)
        {
            terminal.WriteLine("");
            terminal.SetColor("red");
            terminal.WriteLine(Loc.Get("team.creation_cost", $"{creationCost:N0}"));
            terminal.WriteLine(Loc.Get("team.you_only_have", $"{currentPlayer.Gold:N0}"));
            terminal.WriteLine("");
            await Task.Delay(2000);
            return;
        }

        terminal.WriteLine("");
        terminal.SetColor("cyan");
        terminal.WriteLine(Loc.Get("team.creating_gang"));
        terminal.SetColor("yellow");
        terminal.WriteLine(Loc.Get("team.registration_fee", $"{creationCost:N0}"));
        terminal.WriteLine("");

        // Get team name. v1.1.12: trimmed; a blank name is refused
        terminal.SetColor("white");
        terminal.Write(Loc.Get("team.enter_gang_name"));
        string teamName = ((await terminal.ReadLineAsync()) ?? "").Trim();

        if (string.IsNullOrEmpty(teamName) || teamName.Length > 40)
        {
            terminal.SetColor("red");
            terminal.WriteLine(Loc.Get("team.invalid_team_name"));
            await Task.Delay(2000);
            return;
        }

        // Check if team name already exists (NPC teams + player teams). v1.1.12: ignoring case, as the
        // protection list does (WorldSimulator._playerTeamNames)
        if (IsNpcTeamName(teamName))
        {
            terminal.SetColor("red");
            terminal.WriteLine(Loc.Get("team.team_name_exists"));
            await Task.Delay(2000);
            return;
        }

        SqlSaveBackend? backend = DoorMode.IsOnlineMode ? SaveSystem.Instance.Backend as SqlSaveBackend : null;
        if (backend != null && backend.IsTeamNameTaken(teamName))
        {
            terminal.SetColor("red");
            terminal.WriteLine(Loc.Get("team.player_team_exists"));
            await Task.Delay(2000);
            return;
        }

        // Get password
        terminal.Write(Loc.Get("team.enter_gang_password"));
        string password = await terminal.ReadLineAsync();

        if (string.IsNullOrEmpty(password) || password.Length > 20)
        {
            terminal.SetColor("red");
            terminal.WriteLine(Loc.Get("team.invalid_password"));
            await Task.Delay(2000);
            return;
        }

        // v1.1.12: the gold can have gone while the prompts were up
        if (currentPlayer.Gold < creationCost)
        {
            terminal.SetColor("red");
            terminal.WriteLine(Loc.Get("team.you_only_have", $"{currentPlayer.Gold:N0}"));
            await Task.Delay(2000);
            return;
        }

        // Online: register in player_teams FIRST. v1.1.12: the fee was taken before this insert and its
        // result ignored, so a name taken a moment earlier by another session (the check above is not a
        // lock) cost the fee and left a member of a team with no row. The insert is guarded; only a
        // created row is paid for.
        if (backend != null)
        {
            string hashedPW = SqlSaveBackend.HashTeamPassword(password);
            // v1.1.10: the team's leader key is the save key that queued bequests are delivered
            // under (GameEngine.InheritanceKey), not the display name, which differs for an alt
            // and changes with a marriage. created_by is read only to find where a dying NPC
            // member's belongings go (WorldSimulator.BequeathItemsToTeamLeader).
            string username = GameEngine.InheritanceKey(currentPlayer);
            if (!await backend.CreatePlayerTeam(teamName, hashedPW, username))
            {
                terminal.SetColor("red");
                terminal.WriteLine(Loc.Get("team.player_team_exists"));
                await Task.Delay(2000);
                return;
            }
        }

        // Deduct the creation cost
        currentPlayer.Gold -= creationCost;

        // Create team
        currentPlayer.Team = teamName;
        currentPlayer.TeamPW = password;
        currentPlayer.CTurf = false;
        TeamHQBonus.RefreshLevels(currentPlayer);   // v1.1.11
        currentPlayer.TeamRec = 0;
        await PersistTeamMembershipChange();

        // Register so WorldSimulator protects this team from NPC AI
        WorldSimulator.RegisterPlayerTeam(teamName);

        terminal.WriteLine("");
        terminal.SetColor("bright_green");
        terminal.WriteLine(Loc.Get("team.gang_created", teamName));
        terminal.WriteLine(Loc.Get("team.now_leader", teamName));
        terminal.SetColor("yellow");
        terminal.WriteLine(Loc.Get("team.paid_registration", $"{creationCost:N0}"));
        terminal.WriteLine("");

        // Generate news
        NewsSystem.Instance.Newsy(true, Loc.Get("team.news_formed", currentPlayer.DisplayName, teamName));
        if (DoorMode.IsOnlineMode)
            UsurperRemake.Systems.OnlineStateManager.Instance?.AddNews(
                Loc.Get("team.news_formed", currentPlayer.DisplayName, teamName), "team");

        terminal.SetColor("darkgray");
        terminal.WriteLine(Loc.Get("ui.press_enter"));
        await terminal.ReadKeyAsync();
    }

    /// <summary>v1.1.12: an NPC team goes by this name, in any case.</summary>
    private static bool IsNpcTeamName(string teamName) =>
        NPCSpawnSystem.Instance.ActiveNPCs.Any(n => !string.IsNullOrEmpty(n.Team) && string.Equals(n.Team, teamName, StringComparison.OrdinalIgnoreCase));

    /// <summary>
    /// v1.1.12: true (and says so) when the team has no free slot. Join checks before the password and
    /// again after it, as another player can join while the prompt is up.
    /// </summary>
    private async Task<bool> RefuseJoinIfFull(string teamName)
    {
        if (await TeamSlotsUsed(teamName) < MaxTeamSize) return false;
        terminal.WriteLine("");
        terminal.SetColor("red");
        terminal.WriteLine(Loc.Get("team.join_team_full", teamName, MaxTeamSize));
        terminal.WriteLine("");
        await Task.Delay(2000);
        return true;
    }

    /// <summary>
    /// Join an existing team
    /// </summary>
    private async Task JoinTeam()
    {
        // v0.57.13: kings cannot join teams — see CreateTeam for the rationale.
        if (currentPlayer.King)
        {
            terminal.WriteLine("");
            terminal.SetColor("red");
            terminal.WriteLine(Loc.Get("team.king_cannot_join"));
            terminal.WriteLine("");
            await Task.Delay(2000);
            return;
        }

        if (!string.IsNullOrEmpty(currentPlayer.Team))
        {
            terminal.WriteLine("");
            terminal.SetColor("red");
            terminal.WriteLine(Loc.Get("team.already_in_team", currentPlayer.Team));
            terminal.WriteLine("");
            await Task.Delay(2000);
            return;
        }

        // v1.1.12: the teams are listed (the rankings' rows) instead of asking for an exact name
        var teams = await LoadTeamRankings();
        if (teams.Count == 0)
        {
            terminal.WriteLine("");
            terminal.SetColor("yellow");
            terminal.WriteLine(Loc.Get("team.no_teams_yet"));
            await Task.Delay(2000);
            return;
        }
        var picked = await PickFromList(teams, TeamPickRow, t => t.TeamName, "team.pick_team_join_title",
            t => t.ControlsTurf ? "bright_yellow" : "white");
        if (picked == null) return;
        string teamName = picked.TeamName;

        // v1.1.12: five members at most, players and NPCs together, the dead holding their slot. Checked
        // before the password, whose check stamps the team's last join.
        if (await RefuseJoinIfFull(teamName)) return;

        // Online mode: check player_teams table first
        if (DoorMode.IsOnlineMode)
        {
            var backend = SaveSystem.Instance.Backend as SqlSaveBackend;
            if (backend != null)
            {
                terminal.SetColor("cyan");
                terminal.Write(Loc.Get("team.enter_password"));
                terminal.SetColor("white");
                string password = await terminal.ReadLineAsync();

                // v1.1.11: a successful check stamps the team's last join, so the empty-team cleanup leaves it
                // alone until this membership is saved (SqlSaveBackend.VerifyPlayerTeam / DeleteEmptyTeam)
                var (exists, pwCorrect) = await backend.VerifyPlayerTeam(teamName, password);
                if (exists && pwCorrect)
                {
                    if (await RefuseJoinIfFull(teamName)) return;
                    currentPlayer.Team = teamName;
                    currentPlayer.TeamPW = password;
                    currentPlayer.CTurf = false;
                    TeamHQBonus.RefreshLevels(currentPlayer);   // v1.1.11: the team's upgrades count from joining
                    await PersistTeamMembershipChange();

                    WorldSimulator.RegisterPlayerTeam(teamName);
                    await backend.UpdatePlayerTeamMemberCount(teamName);

                    terminal.WriteLine("");
                    terminal.SetColor("bright_green");
                    terminal.WriteLine(Loc.Get("team.joined_team", teamName));
                    terminal.WriteLine("");

                    NewsSystem.Instance.Newsy(true, Loc.Get("team.news_joined", currentPlayer.DisplayName, teamName));
                    if (DoorMode.IsOnlineMode)
                        UsurperRemake.Systems.OnlineStateManager.Instance?.AddNews(
                            Loc.Get("team.news_joined", currentPlayer.DisplayName, teamName), "team");

                    terminal.SetColor("darkgray");
                    terminal.WriteLine(Loc.Get("ui.press_enter"));
                    await terminal.ReadKeyAsync();
                    return;
                }
                else if (exists)
                {
                    terminal.WriteLine("");
                    terminal.SetColor("red");
                    terminal.WriteLine(Loc.Get("team.wrong_password"));
                    terminal.WriteLine("");
                    await Task.Delay(2000);
                    return;
                }
                // If not found in player_teams, fall through to NPC team search
            }
        }

        // Find a team member to get the password from (NPC teams)
        var allNPCs = NPCSpawnSystem.Instance.ActiveNPCs;
        var teamMember = allNPCs.FirstOrDefault(n => n.Team == teamName && n.IsAlive);

        if (teamMember == null)
        {
            terminal.SetColor("red");
            terminal.WriteLine(Loc.Get("team.no_active_team"));
            await Task.Delay(2000);
            return;
        }

        terminal.SetColor("cyan");
        terminal.Write(Loc.Get("team.enter_password"));
        terminal.SetColor("white");
        string npcPassword = await terminal.ReadLineAsync();

        if (npcPassword == teamMember.TeamPW)
        {
            if (await RefuseJoinIfFull(teamName)) return;
            currentPlayer.Team = teamName;
            currentPlayer.TeamPW = npcPassword;
            currentPlayer.CTurf = teamMember.CTurf;
            TeamHQBonus.RefreshLevels(currentPlayer);   // v1.1.11
            await PersistTeamMembershipChange();

            WorldSimulator.RegisterPlayerTeam(teamName);

            terminal.WriteLine("");
            terminal.SetColor("bright_green");
            terminal.WriteLine(Loc.Get("team.joined_team", teamName));
            terminal.WriteLine("");

            NewsSystem.Instance.Newsy(true, Loc.Get("team.news_joined", currentPlayer.DisplayName, teamName));
            if (DoorMode.IsOnlineMode)
                UsurperRemake.Systems.OnlineStateManager.Instance?.AddNews(
                    Loc.Get("team.news_joined", currentPlayer.DisplayName, teamName), "team");

            terminal.SetColor("darkgray");
            terminal.WriteLine(Loc.Get("ui.press_enter"));
            await terminal.ReadKeyAsync();
        }
        else
        {
            terminal.WriteLine("");
            terminal.SetColor("red");
            terminal.WriteLine(Loc.Get("team.wrong_password"));
            terminal.WriteLine("");
            await Task.Delay(2000);
        }
    }

    /// <summary>
    /// Quit your current team
    /// </summary>
    private async Task QuitTeam()
    {
        if (string.IsNullOrEmpty(currentPlayer.Team))
        {
            terminal.WriteLine("");
            terminal.SetColor("red");
            terminal.WriteLine(Loc.Get("team.not_in_team_excl"));
            terminal.WriteLine("");
            await Task.Delay(2000);
            return;
        }

        terminal.WriteLine("");
        terminal.SetColor("yellow");
        terminal.Write(Loc.Get("team.confirm_quit", currentPlayer.Team));
        string response = await terminal.ReadLineAsync();

        if (GameConfig.IsAffirmative(response))
        {
            string oldTeam = currentPlayer.Team;
            currentPlayer.Team = "";
            currentPlayer.TeamPW = "";
            currentPlayer.CTurf = false;
            currentPlayer.TeamRec = 0;
            // Online, this save lands before the member count below, or the SQL count still finds the
            // quitting player in the team. v1.1.12: the second, throttle-reset AutoSave that followed was
            // the same write again and is gone.
            await PersistTeamMembershipChange();

            var backend = DoorMode.IsOnlineMode ? SaveSystem.Instance.Backend as SqlSaveBackend : null;
            if (backend == null)
            {
                // single-player: no other player can be on the team
                WorldSimulator.UnregisterPlayerTeam(oldTeam);
            }
            else
            {
                await backend.UpdatePlayerTeamMemberCount(oldTeam);

                // v1.1.11: a leader who quits passes the team to the highest-level player left in it
                string myKey = GameEngine.InheritanceKey(currentPlayer);
                if (string.Equals(await backend.GetTeamLeaderUsername(oldTeam), myKey, StringComparison.Ordinal))
                    backend.TryPassTeamLeadership(oldTeam, myKey, myKey, requireOldLeaderGone: true, out _);

                var remainingPlayers = await backend.GetPlayerTeamMembers(oldTeam);
                var remainingNPCs = NPCSpawnSystem.Instance.ActiveNPCs
                    .Count(n => n.Team == oldTeam && !n.IsDead && !n.IsPermaDead);

                // v1.1.12: the protection is kept while a player is still on the team; it was dropped on
                // every quit, which left the members still in it open to the NPC AI
                if (remainingPlayers.Count == 0)
                    WorldSimulator.UnregisterPlayerTeam(oldTeam);

                // If team is now empty (no players AND no NPCs), delete it. v1.1.12: with its upgrades and
                // vault (a later team of the same name inherited them), and not within the join grace; a
                // team left then is the empty-team sweep's to remove
                if (remainingPlayers.Count == 0 && remainingNPCs == 0 && backend.DeleteEmptyTeam(oldTeam))
                    DebugLogger.Instance.LogInfo("TEAM", $"Team '{oldTeam}' dissolved, no members remaining");
            }

            terminal.WriteLine("");
            terminal.SetColor("bright_green");
            terminal.WriteLine(Loc.Get("team.left_team"));
            terminal.WriteLine("");

            NewsSystem.Instance.Newsy(true, Loc.Get("team.news_left", currentPlayer.DisplayName, oldTeam));
            if (DoorMode.IsOnlineMode)
                UsurperRemake.Systems.OnlineStateManager.Instance?.AddNews(
                    Loc.Get("team.news_left", currentPlayer.DisplayName, oldTeam), "team");

            terminal.SetColor("darkgray");
            terminal.WriteLine(Loc.Get("ui.press_enter"));
            await terminal.ReadKeyAsync();
        }
    }

    /// <summary>
    /// Recruit an NPC to join your team
    /// </summary>
    /// <summary>
    /// v0.57.11: relationship-aware NPC recruitment UI. Replaces the old
    /// "top 10 by level" flat list with a paginated, social-standing-sorted
    /// list plus a partial-name search and a role filter. See
    /// <c>DOCS/release-notes/RELEASE_NOTES_0.57.11.md</c> for the design rationale.
    ///
    /// Recruitment flow:
    ///   1. Check team-exists and team-not-full gates (unchanged from v0.57.10).
    ///   2. Build candidate pool via <see cref="TeamSystem.IsRecruitable"/>.
    ///      Filters out lovers / spouses / FWB / exes via RomanceTracker,
    ///      scripted NPCs, prisoners, dead NPCs, NPCs already on other teams.
    ///   3. Sort by relationship band (Friend > Neutral > Rival > Refused),
    ///      then level descending within each band.
    ///   4. Render page of 12 rows with relationship tags, price multipliers,
    ///      and [S]earch / [F]ilter / [N]ext / [P]rev / [Q]uit footer.
    ///   5. On digit select, show price-breakdown confirmation before charging.
    /// </summary>
    private async Task RecruitNPCToTeam()
    {
        if (string.IsNullOrEmpty(currentPlayer.Team))
        {
            terminal.WriteLine("");
            terminal.SetColor("red");
            terminal.WriteLine(Loc.Get("team.must_be_in_team_recruit"));
            terminal.WriteLine(Loc.Get("team.create_first_hint"));
            terminal.WriteLine("");
            await Task.Delay(2000);
            return;
        }

        // Count current team size. v1.1.12: the comment said the dead hold a slot but the code left them out,
        // and player members were not counted at all; both count now (TeamSlotsUsed)
        var allNPCs = NPCSpawnSystem.Instance.ActiveNPCs;
        var currentTeamSize = await TeamSlotsUsed(currentPlayer.Team);

        if (currentTeamSize >= MaxTeamSize)
        {
            terminal.WriteLine("");
            terminal.SetColor("red");
            terminal.WriteLine(Loc.Get("team.team_full", MaxTeamSize));
            terminal.WriteLine("");
            await Task.Delay(2000);
            return;
        }

        // Flow loop — role filter and page state persist across re-renders
        // within one recruit session. Exits via [Q], empty input, or a
        // successful/failed recruit.
        UsurperRemake.Data.SpecRole? roleFilter = null;
        int pageIndex = 0;

        while (true)
        {
            var candidates = BuildRecruitmentCandidates(allNPCs, roleFilter);

            terminal.ClearScreen();
            WriteBoxHeader(Loc.Get("team_corner.npc_recruit_header"), "bright_magenta");
            terminal.WriteLine("");
            terminal.SetColor("white");
            terminal.WriteLine(Loc.Get("team.team_label", currentPlayer.Team));
            terminal.WriteLine(Loc.Get("team.current_size", currentTeamSize, MaxTeamSize));
            if (roleFilter != null)
            {
                terminal.SetColor("bright_cyan");
                terminal.WriteLine(Loc.Get("team.recruit_active_filter", GetRoleFilterLabel(roleFilter.Value)));
            }
            terminal.WriteLine("");

            if (candidates.Count == 0)
            {
                terminal.SetColor("yellow");
                terminal.WriteLine(roleFilter != null
                    ? Loc.Get("team.recruit_filter_none_match", GetRoleFilterLabel(roleFilter.Value))
                    : Loc.Get("team.no_npcs_available"));
                terminal.WriteLine(Loc.Get("team.try_again_later"));
                terminal.WriteLine("");
                terminal.SetColor("cyan");
                terminal.Write(Loc.Get("team.recruit_nav_nofilter"));
                terminal.SetColor("white");
                string emptyInput = (await terminal.ReadLineAsync())?.Trim().ToUpperInvariant() ?? "";
                if (emptyInput == "S") { await SearchAndRecruitByName(allNPCs); return; }
                if (emptyInput == "F") { roleFilter = await PromptRoleFilter(roleFilter); pageIndex = 0; continue; }
                return;
            }

            const int pageSize = 12;
            int totalPages = (candidates.Count + pageSize - 1) / pageSize;
            pageIndex = Math.Clamp(pageIndex, 0, totalPages - 1);
            var pageRows = candidates.Skip(pageIndex * pageSize).Take(pageSize).ToList();

            RenderRecruitmentHeader();
            for (int i = 0; i < pageRows.Count; i++)
            {
                var row = pageRows[i];
                RenderRecruitmentRow(pageIndex * pageSize + i + 1, row);
            }

            terminal.WriteLine("");
            terminal.SetColor("darkgray");
            terminal.WriteLine(Loc.Get("team.recruit_page_footer", pageIndex * pageSize + 1, pageIndex * pageSize + pageRows.Count, candidates.Count, pageIndex + 1, totalPages));
            terminal.SetColor("bright_yellow");
            terminal.WriteLine(Loc.Get("team.your_gold", $"{currentPlayer.Gold:N0}"));
            terminal.WriteLine("");
            terminal.SetColor("cyan");
            terminal.Write(Loc.Get("team.recruit_page_nav"));
            terminal.SetColor("white");
            string input = (await terminal.ReadLineAsync())?.Trim() ?? "";
            if (string.IsNullOrEmpty(input)) return;

            string upper = input.ToUpperInvariant();
            switch (upper)
            {
                case "Q": return;
                case "N":
                    if (pageIndex + 1 < totalPages) pageIndex++;
                    continue;
                case "P":
                    if (pageIndex > 0) pageIndex--;
                    continue;
                case "S":
                    await SearchAndRecruitByName(allNPCs);
                    return;
                case "F":
                    roleFilter = await PromptRoleFilter(roleFilter);
                    pageIndex = 0;
                    continue;
            }

            // Numeric selection — interpret against the full (cross-page) index
            if (int.TryParse(input, out int choice) && choice >= 1 && choice <= candidates.Count)
            {
                var picked = candidates[choice - 1];
                await ConfirmAndRecruit(picked.Npc, picked.Band, picked.Multiplier, picked.Cost);
                return;
            }

            terminal.SetColor("red");
            terminal.WriteLine(Loc.Get("team.invalid_choice_generic"));
            await Task.Delay(1200);
        }
    }

    /// <summary>
    /// v0.57.11: a single recruitment list row pre-computed for rendering.
    /// Captures the band + multiplier + final cost so the list render doesn't
    /// have to re-query the relationship system per frame.
    /// </summary>
    private readonly struct RecruitmentRow
    {
        public NPC Npc { get; init; }
        public TeamSystem.RecruitmentBand Band { get; init; }
        public double Multiplier { get; init; }
        public long Cost { get; init; }
        public long DailyWage { get; init; }
    }

    /// <summary>
    /// v0.57.11: build the candidate list filtered and sorted for presentation.
    /// Lovers / spouses / FWB / exes / dead / prisoners / scripted NPCs / NPCs
    /// already on other teams are excluded. Enemy / Hate tier NPCs are
    /// INCLUDED (with band = Refused) so the player sees they exist and can't
    /// be recruited — narrative signal. Sort: band asc (Friend first, Refused
    /// last), then level desc within band.
    /// </summary>
    private List<RecruitmentRow> BuildRecruitmentCandidates(List<NPC> allNPCs, UsurperRemake.Data.SpecRole? roleFilter)
    {
        var townLocations = new[] { "Main Street", "Auction House", "Inn", "Temple", "Church", "Weapon Shop", "Armor Shop", "Castle", "Bank", "Team Corner" };
        var rows = new List<RecruitmentRow>();

        foreach (var npc in allNPCs)
        {
            if (!townLocations.Contains(npc.CurrentLocation)) continue;
            if (!TeamSystem.IsRecruitable(currentPlayer, npc, out var band)) continue;

            if (roleFilter != null)
            {
                var roles = UsurperRemake.Data.SpecializationData.GetDefaultRolesForClass(npc.Class);
                if (!roles.Contains(roleFilter.Value)) continue;
            }

            var (_, mult) = TeamSystem.GetRecruitmentBand(currentPlayer, npc);
            long cost = band == TeamSystem.RecruitmentBand.Refused
                ? -1
                : Math.Max(100, (long)(TeamSystem.GetRecruitmentBaseCost(npc, currentPlayer) * mult));

            rows.Add(new RecruitmentRow
            {
                Npc = npc,
                Band = band,
                Multiplier = mult,
                Cost = cost,
                DailyWage = npc.Level * GameConfig.NpcDailyWagePerLevel,
            });
        }

        // Sort: Family (0, top) → Friend (1) → Neutral (2) → Rival (3) → Refused (4), then level desc.
        // v0.63.0 slice 3b C3: adult children sort to the top because the player
        // overwhelmingly wants to find their kids fast in the recruit list.
        int BandOrder(TeamSystem.RecruitmentBand b) => b switch
        {
            TeamSystem.RecruitmentBand.Family   => 0,
            TeamSystem.RecruitmentBand.Friend   => 1,
            TeamSystem.RecruitmentBand.Neutral  => 2,
            TeamSystem.RecruitmentBand.Rival    => 3,
            TeamSystem.RecruitmentBand.Refused  => 4,
            _ => 99, // Hidden shouldn't appear — IsRecruitable filters it out
        };

        return rows
            .OrderBy(r => BandOrder(r.Band))
            .ThenByDescending(r => r.Npc.Level)
            .ToList();
    }

    /// <summary>
    /// v0.57.11: column header for the recruitment list.
    /// </summary>
    private void RenderRecruitmentHeader()
    {
        terminal.SetColor("cyan");
        terminal.WriteLine(Loc.Get("team.available_recruits"));
        terminal.SetColor("white");
        terminal.WriteLine($"{Loc.Get("team.col_num"),-3} {Loc.Get("team.col_name"),-20} {Loc.Get("team.col_class"),-12} {Loc.Get("team.col_level"),-4} {Loc.Get("team.recruit_col_rel"),-10} {Loc.Get("team.col_cost"),-13} {Loc.Get("team.col_wage"),-10}");
        if (!IsScreenReader)
        {
            terminal.SetColor("darkgray");
            terminal.WriteLine(new string('─', 78));
        }
    }

    /// <summary>
    /// v0.57.11: one row of the recruitment list, colored by band.
    /// </summary>
    private void RenderRecruitmentRow(int displayIndex, RecruitmentRow row)
    {
        string color = row.Band switch
        {
            TeamSystem.RecruitmentBand.Family  => "bright_magenta",  // v0.63.0 slice 3b: family pop
            TeamSystem.RecruitmentBand.Friend  => "bright_green",
            TeamSystem.RecruitmentBand.Neutral => "white",
            TeamSystem.RecruitmentBand.Rival   => "yellow",
            TeamSystem.RecruitmentBand.Refused => "red",
            _ => "white",
        };
        terminal.SetColor(color);

        string relTag = GetBandTag(row.Band);
        string costCol = row.Band == TeamSystem.RecruitmentBand.Refused
            ? Loc.Get("team.recruit_wont_join")
            : $"{row.Cost:N0}g";

        terminal.WriteLine($"{displayIndex,-3} {TruncateName(row.Npc.DisplayName, 20),-20} {row.Npc.ClassName,-12} {row.Npc.Level,-4} {relTag,-10} {costCol,-13} {row.DailyWage:N0}g");
    }

    private static string TruncateName(string name, int max)
    {
        if (string.IsNullOrEmpty(name)) return "";
        return name.Length <= max ? name : name.Substring(0, max - 1) + "…";
    }

    private static string GetBandTag(TeamSystem.RecruitmentBand band) => band switch
    {
        TeamSystem.RecruitmentBand.Family   => Loc.Get("team.recruit_rel_family"),
        TeamSystem.RecruitmentBand.Friend   => Loc.Get("team.recruit_rel_friend"),
        TeamSystem.RecruitmentBand.Neutral  => Loc.Get("team.recruit_rel_neutral"),
        TeamSystem.RecruitmentBand.Rival    => Loc.Get("team.recruit_rel_rival"),
        TeamSystem.RecruitmentBand.Refused  => Loc.Get("team.recruit_rel_enemy"),
        _ => "",
    };

    /// <summary>
    /// v0.57.11: localized label for a SpecRole — used in the role-filter
    /// menu and the "active filter" banner.
    /// </summary>
    private static string GetRoleFilterLabel(UsurperRemake.Data.SpecRole role) => role switch
    {
        UsurperRemake.Data.SpecRole.Tank    => Loc.Get("team.recruit_filter_tank"),
        UsurperRemake.Data.SpecRole.DPS     => Loc.Get("team.recruit_filter_dps"),
        UsurperRemake.Data.SpecRole.Healer  => Loc.Get("team.recruit_filter_healer"),
        UsurperRemake.Data.SpecRole.Utility => Loc.Get("team.recruit_filter_utility"),
        UsurperRemake.Data.SpecRole.Debuff  => Loc.Get("team.recruit_filter_debuff"),
        _ => role.ToString(),
    };

    /// <summary>v1.1.12: one specialization role, named (the raw enum name was shown).</summary>
    private static string SpecRoleName(UsurperRemake.Data.SpecRole role) => role switch
    {
        UsurperRemake.Data.SpecRole.Tank    => Loc.Get("team.spec_role_tank"),
        UsurperRemake.Data.SpecRole.DPS     => Loc.Get("team.spec_role_dps"),
        UsurperRemake.Data.SpecRole.Healer  => Loc.Get("team.spec_role_healer"),
        UsurperRemake.Data.SpecRole.Utility => Loc.Get("team.spec_role_utility"),
        UsurperRemake.Data.SpecRole.Debuff  => Loc.Get("team.spec_role_debuff"),
        _ => role.ToString(),
    };

    /// <summary>
    /// v0.57.11: prompts the player to pick a role to filter by, or clear the
    /// filter. Returns the new filter value (null = "all roles").
    /// </summary>
    private async Task<UsurperRemake.Data.SpecRole?> PromptRoleFilter(UsurperRemake.Data.SpecRole? current)
    {
        terminal.WriteLine("");
        terminal.SetColor("cyan");
        terminal.WriteLine(Loc.Get("team.recruit_filter_prompt"));
        terminal.SetColor("white");
        terminal.WriteLine($"  0. {Loc.Get("team.recruit_filter_all")}");
        terminal.WriteLine($"  1. {Loc.Get("team.recruit_filter_tank")}");
        terminal.WriteLine($"  2. {Loc.Get("team.recruit_filter_dps")}");
        terminal.WriteLine($"  3. {Loc.Get("team.recruit_filter_healer")}");
        terminal.WriteLine($"  4. {Loc.Get("team.recruit_filter_utility")}");
        terminal.WriteLine($"  5. {Loc.Get("team.recruit_filter_debuff")}");
        terminal.WriteLine("");
        terminal.SetColor("bright_white");
        string input = (await terminal.ReadLineAsync())?.Trim() ?? "";
        return input switch
        {
            "0" => null,
            "1" => UsurperRemake.Data.SpecRole.Tank,
            "2" => UsurperRemake.Data.SpecRole.DPS,
            "3" => UsurperRemake.Data.SpecRole.Healer,
            "4" => UsurperRemake.Data.SpecRole.Utility,
            "5" => UsurperRemake.Data.SpecRole.Debuff,
            _   => current, // unknown input keeps previous filter
        };
    }

    /// <summary>
    /// v0.57.11: partial-name search for a specific NPC. Cloned-in-shape from
    /// <c>TempleLocation.SelectGod</c>: empty → cancel, zero matches →
    /// "nobody by that name", one match → straight to confirmation, multiple
    /// matches with a preferable StartsWith candidate → auto-resolve,
    /// otherwise → disambiguation sub-list.
    ///
    /// Searches also consider NPCs that are filtered out of the paginated
    /// list (lovers, spouses, exes, hate-tier enemies) — but instead of
    /// silently returning no match, the search prints a dedicated refusal
    /// flavor so the player knows why they can't recruit that person.
    /// </summary>
    private async Task SearchAndRecruitByName(List<NPC> allNPCs)
    {
        terminal.WriteLine("");
        terminal.SetColor("cyan");
        terminal.Write(Loc.Get("team.recruit_search_prompt"));
        terminal.SetColor("white");
        string raw = (await terminal.ReadLineAsync())?.Trim() ?? "";
        if (string.IsNullOrEmpty(raw)) return;

        // Search the full NPC population, not just the eligible list, so we can
        // tell the player "no, that's your spouse" instead of "no match found"
        // when they type a specific name.
        var lookup = raw;
        var townLocations = new[] { "Main Street", "Auction House", "Inn", "Temple", "Church", "Weapon Shop", "Armor Shop", "Castle", "Bank", "Team Corner" };
        var pool = allNPCs.Where(n => n.IsAlive && !n.IsDead && string.IsNullOrEmpty(n.Team)
                                   && townLocations.Contains(n.CurrentLocation)
                                   && !n.IsSpecialNPC
                                   && n.DaysInPrison == 0).ToList();

        var matches = pool.Where(n => n.DisplayName.Contains(lookup, StringComparison.OrdinalIgnoreCase)
                                   || n.Name2.Contains(lookup, StringComparison.OrdinalIgnoreCase)).ToList();

        // Prefer StartsWith hits (TempleLocation pattern).
        var startsWith = matches.Where(n => n.DisplayName.StartsWith(lookup, StringComparison.OrdinalIgnoreCase)
                                         || n.Name2.StartsWith(lookup, StringComparison.OrdinalIgnoreCase)).ToList();

        NPC? chosen = null;
        if (startsWith.Count == 1) chosen = startsWith[0];
        else if (matches.Count == 1) chosen = matches[0];
        else if (startsWith.Count > 1 || matches.Count > 1)
        {
            // Disambiguation sub-list
            var disamb = (startsWith.Count > 1 ? startsWith : matches).OrderBy(n => n.DisplayName).Take(12).ToList();
            terminal.WriteLine("");
            terminal.SetColor("bright_cyan");
            terminal.WriteLine(Loc.Get("team.recruit_multiple_matches_header"));
            terminal.SetColor("white");
            for (int i = 0; i < disamb.Count; i++)
                terminal.WriteLine($"  {i + 1}. {disamb[i].DisplayName} ({Loc.Get("inn.npc_level_class", disamb[i].Level, disamb[i].ClassName)})");
            terminal.WriteLine("");
            terminal.SetColor("cyan");
            terminal.Write(Loc.Get("team.recruit_pick_match"));
            terminal.SetColor("white");
            string pickRaw = (await terminal.ReadLineAsync())?.Trim() ?? "";
            if (int.TryParse(pickRaw, out int pickIdx) && pickIdx >= 1 && pickIdx <= disamb.Count)
                chosen = disamb[pickIdx - 1];
        }

        if (chosen == null)
        {
            terminal.WriteLine("");
            terminal.SetColor("yellow");
            terminal.WriteLine(Loc.Get("team.recruit_no_match", lookup));
            terminal.WriteLine("");
            terminal.SetColor("darkgray");
            terminal.WriteLine(Loc.Get("ui.press_enter"));
            await terminal.ReadKeyAsync();
            return;
        }

        // Now classify. If the match is a lover / spouse / FWB / ex, print a
        // dedicated refusal; if Refused (Hate / Enemy), print the hate-tier
        // refusal; otherwise go to confirmation.
        var (band, mult) = TeamSystem.GetRecruitmentBand(currentPlayer, chosen);
        if (band == TeamSystem.RecruitmentBand.Hidden)
        {
            var romanceType = RomanceTracker.Instance.GetRelationType(chosen.ID);
            terminal.WriteLine("");
            terminal.SetColor("magenta");
            string key = romanceType switch
            {
                RomanceRelationType.Spouse => "team.recruit_refuse_spouse_search",
                RomanceRelationType.Lover  => "team.recruit_refuse_lover_search",
                RomanceRelationType.FWB    => "team.recruit_refuse_fwb_search",
                RomanceRelationType.Ex     => "team.recruit_refuse_ex_search",
                _ => "team.recruit_refuse_spouse_search",
            };
            terminal.WriteLine(Loc.Get(key, chosen.DisplayName));
            terminal.WriteLine("");
            terminal.SetColor("darkgray");
            terminal.WriteLine(Loc.Get("ui.press_enter"));
            await terminal.ReadKeyAsync();
            return;
        }
        if (band == TeamSystem.RecruitmentBand.Refused)
        {
            int idx = Random.Shared.Next(5); // 5 flavor variants: team.recruit_refuse_hate_1..5
            terminal.WriteLine("");
            terminal.SetColor("red");
            terminal.WriteLine(Loc.Get($"team.recruit_refuse_hate_{idx + 1}", chosen.DisplayName));
            terminal.WriteLine("");
            terminal.SetColor("darkgray");
            terminal.WriteLine(Loc.Get("ui.press_enter"));
            await terminal.ReadKeyAsync();
            return;
        }

        long cost = Math.Max(100, (long)(TeamSystem.GetRecruitmentBaseCost(chosen, currentPlayer) * mult));
        await ConfirmAndRecruit(chosen, band, mult, cost);
    }

    /// <summary>
    /// v0.57.11: shared confirmation + execution path. Shows the price
    /// breakdown (base × multiplier = final), re-checks recruitability
    /// immediately before deducting gold (closes the race window where an
    /// NPC could die or join someone else between list render and confirm),
    /// and fires the news + world-state save on success.
    /// </summary>
    private async Task ConfirmAndRecruit(NPC recruit, TeamSystem.RecruitmentBand band, double multiplier, long cost)
    {
        terminal.ClearScreen();
        WriteBoxHeader(Loc.Get("team.recruit_confirm_header"), "bright_magenta");
        terminal.WriteLine("");
        terminal.SetColor("white");
        terminal.WriteLine(Loc.Get("team.recruit_confirm_name", recruit.DisplayName, recruit.ClassName, recruit.Level));
        terminal.SetColor("darkgray");
        terminal.WriteLine(Loc.Get("team.recruit_confirm_relationship", GetBandTag(band)));
        terminal.WriteLine("");

        long baseCost = TeamSystem.GetRecruitmentBaseCost(recruit, currentPlayer);
        terminal.SetColor("white");
        terminal.WriteLine(Loc.Get("team.recruit_confirm_base_cost", $"{baseCost:N0}"));
        if (Math.Abs(multiplier - 1.0) > 0.001)
        {
            string tone = multiplier < 1.0 ? "bright_green" : "yellow";
            terminal.SetColor(tone);
            terminal.WriteLine(Loc.Get("team.recruit_confirm_multiplier", $"{multiplier:0.00}", GetBandTag(band)));
        }
        terminal.SetColor("bright_yellow");
        terminal.WriteLine(Loc.Get("team.recruit_confirm_final_cost", $"{cost:N0}"));
        terminal.WriteLine("");
        terminal.SetColor("bright_yellow");
        terminal.WriteLine(Loc.Get("team.your_gold", $"{currentPlayer.Gold:N0}"));
        terminal.WriteLine("");
        terminal.SetColor("cyan");
        terminal.Write(Loc.Get("team.recruit_confirm_prompt", recruit.DisplayName, $"{cost:N0}"));
        terminal.SetColor("white");
        string response = (await terminal.ReadLineAsync())?.Trim().ToUpperInvariant() ?? "";
        if (!GameConfig.IsAffirmative(response) && response != "YES")
        {
            terminal.SetColor("gray");
            terminal.WriteLine(Loc.Get("team.recruit_cancelled"));
            await Task.Delay(800);
            return;
        }

        // Player report (Lv.32 Abysswarden, online): "I am hiring NPCs for my
        // dungeon team, but it is still listing them as available for hire. I
        // can still hire the same person and lose my gold even though he is
        // currently in my team." Root cause: `RecruitNPCToTeam` captures
        // `allNPCs = NPCSpawnSystem.Instance.ActiveNPCs` at menu entry and
        // passes references through to `ConfirmAndRecruit`. If
        // `WorldSimService.LoadWorldState` fires between menu and Y-confirm
        // (triggered by another online player's save bumping the world_state
        // version), `ClearAllNPCs` destroys the in-memory NPC objects and
        // builds new ones from DB. The `recruit` parameter is now an orphan
        // reference — mutating its Team field writes to a discarded object,
        // and the next `SaveAllSharedState` serializes the live list (where
        // the new NPC still has the pre-recruit Team value). Result: gold
        // gets deducted on every retry, NPC's live Team flag stays empty,
        // recruit list re-shows the NPC. Defense: re-resolve `recruit` to a
        // live ActiveNPCs reference by ID before any mutation. If the live
        // NPC is gone (rare — permadied or evicted by another path), refuse.
        var liveRecruit = NPCSpawnSystem.Instance.ActiveNPCs
            .FirstOrDefault(n => !string.IsNullOrEmpty(n.ID) && n.ID == recruit.ID);
        if (liveRecruit == null)
        {
            DebugLogger.Instance.LogWarning("RECRUIT",
                $"Player '{currentPlayer.DisplayName}' attempted to recruit '{recruit.DisplayName}' " +
                $"(ID={recruit.ID}) but no live NPC with that ID exists. Likely a stale orphan " +
                $"reference from a world-sim reload between menu and confirm.");
            terminal.SetColor("red");
            terminal.WriteLine(Loc.Get("team.recruit_unavailable_now", recruit.DisplayName));
            terminal.WriteLine("");
            terminal.SetColor("darkgray");
            terminal.WriteLine(Loc.Get("ui.press_enter"));
            await terminal.ReadKeyAsync();
            return;
        }
        if (!ReferenceEquals(liveRecruit, recruit))
        {
            DebugLogger.Instance.LogInfo("RECRUIT",
                $"Re-resolved recruit '{recruit.DisplayName}' (ID={recruit.ID}) to live ActiveNPCs " +
                $"reference. Previous reference was a stale orphan (world-sim reload mid-recruit).");
            recruit = liveRecruit;
        }

        // Live re-check (closes the race window): an NPC could have died or
        // joined another team between list render and confirm, or the player's
        // relationship could have changed (e.g. a companion quest fired between
        // screens and shifted the NPC to Hate).
        if (!TeamSystem.IsRecruitable(currentPlayer, recruit, out var liveBand))
        {
            // Defense-in-depth: surface a specific "already on your team"
            // refusal so the player learns the recruit succeeded (probably
            // an earlier attempt during the same session) instead of a vague
            // "unavailable" message. Doesn't deduct gold.
            if (!string.IsNullOrEmpty(recruit.Team) &&
                string.Equals(recruit.Team, currentPlayer.Team, StringComparison.OrdinalIgnoreCase))
            {
                terminal.SetColor("yellow");
                terminal.WriteLine(Loc.Get("team.recruit_already_on_team", recruit.DisplayName));
                terminal.WriteLine("");
                terminal.SetColor("darkgray");
                terminal.WriteLine(Loc.Get("ui.press_enter"));
                await terminal.ReadKeyAsync();
                return;
            }
            terminal.SetColor("red");
            terminal.WriteLine(Loc.Get("team.recruit_unavailable_now", recruit.DisplayName));
            terminal.WriteLine("");
            terminal.SetColor("darkgray");
            terminal.WriteLine(Loc.Get("ui.press_enter"));
            await terminal.ReadKeyAsync();
            return;
        }
        if (liveBand == TeamSystem.RecruitmentBand.Refused)
        {
            terminal.SetColor("red");
            terminal.WriteLine(Loc.Get("team.recruit_refuse_hate_1", recruit.DisplayName));
            terminal.WriteLine("");
            terminal.SetColor("darkgray");
            terminal.WriteLine(Loc.Get("ui.press_enter"));
            await terminal.ReadKeyAsync();
            return;
        }

        // v1.1.12: the team can have filled while the list was up
        if (await TeamSlotsUsed(currentPlayer.Team) >= MaxTeamSize)
        {
            terminal.SetColor("red");
            terminal.WriteLine(Loc.Get("team.team_full", MaxTeamSize));
            terminal.WriteLine("");
            terminal.SetColor("darkgray");
            terminal.WriteLine(Loc.Get("ui.press_enter"));
            await terminal.ReadKeyAsync();
            return;
        }

        long liveCost = Math.Max(100, (long)(TeamSystem.GetRecruitmentBaseCost(recruit, currentPlayer) *
                                             TeamSystem.GetRecruitmentBand(currentPlayer, recruit).multiplier));
        if (currentPlayer.Gold < liveCost)
        {
            terminal.WriteLine("");
            terminal.SetColor("red");
            terminal.WriteLine(Loc.Get("ui.not_enough_gold_recruit", recruit.DisplayName));
            terminal.WriteLine(Loc.Get("team.need_gold_recruit", $"{liveCost:N0}", $"{currentPlayer.Gold:N0}"));
            terminal.WriteLine("");
            terminal.SetColor("darkgray");
            terminal.WriteLine(Loc.Get("ui.press_enter"));
            await terminal.ReadKeyAsync();
            return;
        }

        // Recruitment success
        currentPlayer.Gold -= liveCost;
        recruit.Team = currentPlayer.Team;
        recruit.TeamPW = currentPlayer.TeamPW;
        recruit.CTurf = currentPlayer.CTurf;

        terminal.WriteLine("");
        terminal.SetColor("bright_green");
        terminal.WriteLine(Loc.Get("team.npc_joined", recruit.DisplayName));
        terminal.WriteLine(Loc.Get("team.paid_recruitment", $"{liveCost:N0}"));
        long wage = recruit.Level * GameConfig.NpcDailyWagePerLevel;
        terminal.SetColor("yellow");
        terminal.WriteLine(Loc.Get("team.daily_wage", $"{wage:N0}"));
        terminal.WriteLine("");
        terminal.SetColor("bright_cyan");
        terminal.WriteLine(Loc.Get("team.recruit_quote", recruit.DisplayName));

        NewsSystem.Instance.Newsy(true, Loc.Get("team.news_recruited", currentPlayer.DisplayName, recruit.DisplayName, currentPlayer.Team));

        if (DoorMode.IsOnlineMode && OnlineStateManager.Instance != null)
        {
            try { await OnlineStateManager.Instance.SaveAllSharedState(); }
            catch (Exception ex) { DebugLogger.Instance.LogError("RECRUIT", $"SaveAllSharedState failed: {ex.Message}"); }
        }

        terminal.WriteLine("");
        terminal.SetColor("darkgray");
        terminal.WriteLine(Loc.Get("ui.press_enter"));
        await terminal.ReadKeyAsync();
    }

    /// <summary>
    /// Examine a team member in detail
    /// </summary>
    private async Task ExamineMember()
    {
        if (string.IsNullOrEmpty(currentPlayer.Team))
        {
            terminal.WriteLine("");
            terminal.SetColor("red");
            terminal.WriteLine(Loc.Get("team.not_in_team"));
            terminal.WriteLine("");
            await Task.Delay(2000);
            return;
        }

        // v1.1.12: every member is listed, players too (the name prompt found NPCs only, so a player member
        // could not be examined)
        var entries = await GetTeamMemberEntries(includeViewer: true);
        var pickedEntry = await PickFromList(entries, MemberPickRow, e => e.Name, "team.pick_examine_title", MemberPickColor);
        if (pickedEntry == null) return;
        if (pickedEntry.Npc == null)
        {
            await ExaminePlayerMember(pickedEntry);
            return;
        }
        var member = pickedEntry.Npc;

        // Show detailed stats. Expanded v0.61.5: identity (age/sex/race/class),
        // alignment, attitude (NPC dominant emotion + impression of player),
        // personality traits (top 3 deviated from neutral), social context
        // (marriage, faction, worship), and combat stats. Player report:
        // "Need a way to view stats about each of their team mates."
        // v0.63.0 slice 1: prepend "(your daughter / son / child)" if this
        // team member is the player's adult child.
        terminal.ClearScreen();
        string familyTag = UsurperRemake.Systems.FamilySystem.Instance?.GetChildTagFor(member, currentPlayer) ?? "";
        string examineHeader = string.IsNullOrEmpty(familyTag)
            ? member.DisplayName.ToUpper()
            : $"{member.DisplayName.ToUpper()} {familyTag}";
        WriteSectionHeader(examineHeader, "bright_cyan");
        terminal.WriteLine("");

        // configured, generates a 2-3 sentence character impression keyed
        // to the NPC's personality + archetype + class. Cached on the NPC
        // is disabled or fails, falls back to a templated string driven by
        // the NPC's strongest personality traits. The Task is awaited
        // timeout (default 3s).
        try
        {
            string impression = UsurperRemake.Systems.NPCImpressionText.Build(member);
            if (!string.IsNullOrWhiteSpace(impression))
            {
                terminal.SetColor("bright_white");
                terminal.WriteLine(Loc.Get("team.examine_section_impression"));
                terminal.SetColor("white");
                terminal.WriteLine($"  {impression}");
                terminal.WriteLine("");
            }
        }
        catch
        {
            // First-impression is decorative; never block the examine screen.
        }

        // --- Identity ---
        terminal.SetColor("bright_white");
        terminal.WriteLine(Loc.Get("team.examine_section_identity"));
        terminal.SetColor("white");
        terminal.WriteLine($"  {Loc.Get("status.class")}: {member.ClassName}");
        if (member.Specialization != ClassSpecialization.None)
        {
            var specDef = UsurperRemake.Data.SpecializationData.GetSpec(member.Specialization);
            terminal.SetColor("cyan");
            terminal.WriteLine($"  {Loc.Get("spec.label")}: {specDef?.Name ?? member.Specialization.ToString()} ({(specDef != null ? SpecRoleName(specDef.Role) : "")})");
            terminal.SetColor("white");
        }
        terminal.WriteLine($"  {Loc.Get("status.race")}: {GameConfig.GetLocalizedRaceName(member.Race)}");   // v1.1.12: localized
        terminal.WriteLine($"  {Loc.Get("team.examine_sex")}: {GameConfig.GetLocalizedSexName(member.Sex)}");
        terminal.WriteLine($"  {Loc.Get("team.examine_age")}: {member.Age}");
        terminal.WriteLine($"  {Loc.Get("ui.level")}: {member.Level}");

        if (member.IsAlive)
        {
            terminal.SetColor("bright_green");
            terminal.WriteLine($"  {Loc.Get("team.status_label_alive")}");
        }
        else
        {
            terminal.SetColor("red");
            terminal.WriteLine($"  {Loc.Get("team.status_label_dead")}");
        }
        terminal.WriteLine("");

        // --- Alignment & Faction ---
        terminal.SetColor("bright_white");
        terminal.WriteLine(Loc.Get("team.examine_section_alignment"));
        terminal.SetColor("white");
        var (alignLabel, alignColor) = AlignmentSystem.Instance.GetAlignmentDisplay(member);
        terminal.Write($"  {Loc.Get("team.examine_alignment")}: ");
        terminal.SetColor(alignColor);
        terminal.WriteLine(alignLabel);
        terminal.SetColor("white");
        terminal.WriteLine($"  {Loc.Get("team.examine_chivalry")}: {member.Chivalry}   {Loc.Get("team.examine_darkness")}: {member.Darkness}");
        if (member.NPCFaction.HasValue)
        {
            string facName = member.NPCFaction.Value switch   // v1.1.12: localized, was the enum name
            {
                Faction.TheCrown => Loc.Get("faction.name_crown"),
                Faction.TheShadows => Loc.Get("faction.name_shadows"),
                Faction.TheFaith => Loc.Get("faction.name_faith"),
                _ => member.NPCFaction.Value.ToString()
            };
            terminal.WriteLine($"  {Loc.Get("team.examine_faction")}: {facName}");
        }
        if (!string.IsNullOrEmpty(member.WorshippedGod))
        {
            terminal.WriteLine($"  {Loc.Get("team.examine_worships")}: {member.WorshippedGod}");
        }
        terminal.WriteLine("");

        // --- Family / Lineage block (v0.63.0 slice E1) ---
        bool hasParents = !string.IsNullOrEmpty(member.MotherName)
            || !string.IsNullOrEmpty(member.FatherName);
        bool hasSpouse = !string.IsNullOrEmpty(member.SpouseName);
        // Children of this NPC: walk Child registry + GetAdultChildrenOf
        var family = UsurperRemake.Systems.FamilySystem.Instance;
        int npcChildCount = family != null ? family.GetChildrenOf(member).Count
            + family.GetAdultChildrenOf(member).Count : 0;
        if (hasParents || hasSpouse || npcChildCount > 0)
        {
            terminal.SetColor("bright_white");
            terminal.WriteLine(Loc.Get("team.examine_section_family"));
            terminal.SetColor("white");
            if (!string.IsNullOrEmpty(member.MotherName))
                terminal.WriteLine($"  {Loc.Get("team.examine_mother")}: {member.MotherName}");
            if (!string.IsNullOrEmpty(member.FatherName))
                terminal.WriteLine($"  {Loc.Get("team.examine_father")}: {member.FatherName}");
            if (hasSpouse)
                terminal.WriteLine($"  {Loc.Get("team.examine_spouse")}: {member.SpouseName}");
            if (npcChildCount > 0)
                terminal.WriteLine($"  {Loc.Get("team.examine_children", npcChildCount)}");
            terminal.WriteLine("");
        }

        // --- Attitude (toward player + dominant emotion) ---
        terminal.SetColor("bright_white");
        terminal.WriteLine(Loc.Get("team.examine_section_attitude"));
        terminal.SetColor("white");
        if (member.EmotionalState != null)
        {
            var dom = member.EmotionalState.GetDominantEmotion();
            string moodLabel = dom.HasValue ? dom.Value.ToString() : Loc.Get("team.examine_mood_neutral");
            terminal.WriteLine($"  {Loc.Get("team.examine_current_mood")}: {moodLabel}");
        }
        if (member.Brain?.Memory != null && currentPlayer.Name2 != null)
        {
            float impression = member.Brain.Memory.GetCharacterImpression(currentPlayer.Name2);
            string impressionLabel = impression >= 0.5f ? Loc.Get("team.examine_impression_loyal")
                : impression >= 0.2f ? Loc.Get("team.examine_impression_friendly")
                : impression >= -0.2f ? Loc.Get("team.examine_impression_neutral")
                : impression >= -0.5f ? Loc.Get("team.examine_impression_wary")
                : Loc.Get("team.examine_impression_hostile");
            string impressionColor = impression >= 0.2f ? "bright_green"
                : impression >= -0.2f ? "gray"
                : "red";
            terminal.Write($"  {Loc.Get("team.examine_impression_label")}: ");
            terminal.SetColor(impressionColor);
            terminal.WriteLine($"{impressionLabel} ({impression:F2})");
            terminal.SetColor("white");
        }
        terminal.WriteLine("");

        // --- Personality traits (top 3 strongly-deviated from neutral 0.5) ---
        if (member.Personality != null)
        {
            terminal.SetColor("bright_white");
            terminal.WriteLine(Loc.Get("team.examine_section_personality"));
            terminal.SetColor("white");
            var p = member.Personality;
            var traits = new (string label, float deviation)[]
            {
                (Loc.Get("team.examine_trait_aggression"), Math.Abs(p.Aggression - 0.5f) * Math.Sign(p.Aggression - 0.5f)),
                (Loc.Get("team.examine_trait_greed"), Math.Abs(p.Greed - 0.5f) * Math.Sign(p.Greed - 0.5f)),
                (Loc.Get("team.examine_trait_courage"), Math.Abs(p.Courage - 0.5f) * Math.Sign(p.Courage - 0.5f)),
                (Loc.Get("team.examine_trait_loyalty"), Math.Abs(p.Loyalty - 0.5f) * Math.Sign(p.Loyalty - 0.5f)),
                (Loc.Get("team.examine_trait_vengefulness"), Math.Abs(p.Vengefulness - 0.5f) * Math.Sign(p.Vengefulness - 0.5f)),
                (Loc.Get("team.examine_trait_sociability"), Math.Abs(p.Sociability - 0.5f) * Math.Sign(p.Sociability - 0.5f)),
                (Loc.Get("team.examine_trait_ambition"), Math.Abs(p.Ambition - 0.5f) * Math.Sign(p.Ambition - 0.5f)),
                (Loc.Get("team.examine_trait_trustworthy"), Math.Abs(p.Trustworthiness - 0.5f) * Math.Sign(p.Trustworthiness - 0.5f)),
                (Loc.Get("team.examine_trait_caution"), Math.Abs(p.Caution - 0.5f) * Math.Sign(p.Caution - 0.5f)),
            };
            // Top 3 by absolute deviation
            var topTraits = traits.OrderByDescending(t => Math.Abs(t.deviation)).Take(3).ToList();
            foreach (var (label, deviation) in topTraits)
            {
                string strength = Math.Abs(deviation) >= 0.30f ? Loc.Get("team.examine_trait_very")
                    : Math.Abs(deviation) >= 0.15f ? Loc.Get("team.examine_trait_somewhat")
                    : Loc.Get("team.examine_trait_mildly");
                string direction = deviation >= 0 ? "+" : "-";
                terminal.WriteLine($"  {direction} {strength} {label}");
            }
            terminal.WriteLine("");
        }

        // --- Social ---
        terminal.SetColor("bright_white");
        terminal.WriteLine(Loc.Get("team.examine_section_social"));
        terminal.SetColor("white");
        if (member.IsMarried && !string.IsNullOrEmpty(member.SpouseName))
        {
            terminal.WriteLine($"  {Loc.Get("team.examine_married_to", member.SpouseName)}");
        }
        else
        {
            terminal.WriteLine($"  {Loc.Get("team.examine_unmarried")}");
        }
        if (member.Kids > 0)
        {
            terminal.WriteLine($"  {Loc.Get("team.examine_children", member.Kids)}");
        }
        terminal.WriteLine("");

        // --- Combat stats ---
        terminal.SetColor("bright_white");
        terminal.WriteLine(Loc.Get("team.examine_section_combat"));
        terminal.SetColor("white");
        terminal.WriteLine($"  {Loc.Get("combat.bar_hp")}: {member.HP}/{member.MaxHP}");
        terminal.WriteLine($"  {Loc.Get("ui.mana_label")}: {member.Mana}/{member.MaxMana}");
        terminal.WriteLine($"  {Loc.Get("ui.gold")}: {member.Gold:N0}");
        terminal.WriteLine("");
        WriteMemberStatLines(member);
        terminal.WriteLine("");

        terminal.WriteLine($"{Loc.Get("ui.location")}: {member.CurrentLocation ?? Loc.Get("team.unknown")}");
        terminal.WriteLine("");

        terminal.SetColor("darkgray");
        terminal.WriteLine(Loc.Get("ui.press_enter"));
        await terminal.ReadKeyAsync();
    }

    /// <summary>
    /// Change team password
    /// </summary>
    private async Task ChangeTeamPassword()
    {
        if (string.IsNullOrEmpty(currentPlayer.Team))
        {
            terminal.WriteLine("");
            terminal.SetColor("red");
            terminal.WriteLine(Loc.Get("team.not_in_team"));
            terminal.WriteLine("");
            await Task.Delay(2000);
            return;
        }

        // v1.1.12: online, the password a join checks is player_teams.password_hash, and only the leader may
        // change it; the change used to set only the in-memory copies, so the new password was refused and
        // the old one kept working
        var backend = DoorMode.IsOnlineMode ? SaveSystem.Instance.Backend as SqlSaveBackend : null;
        string myKey = GameEngine.InheritanceKey(currentPlayer);
        // v1.1.12: a team an NPC founded has no player_teams row; its password is the one its NPCs hold, which
        // a join checks, so it is changed as before (the old password checked against that one)
        bool? hasRow = backend?.HasPlayerTeamRow(currentPlayer.Team);
        if (backend != null && hasRow == null)
        {
            terminal.WriteLine("");
            terminal.SetColor("red");
            terminal.WriteLine(Loc.Get("team.failed_generic"));
            await Task.Delay(2000);
            return;
        }
        bool npcTeam = hasRow == false;
        if (npcTeam) backend = null;
        if (backend != null && !string.Equals(await backend.GetTeamLeaderUsername(currentPlayer.Team), myKey, StringComparison.Ordinal))
        {
            terminal.WriteLine("");
            terminal.SetColor("red");
            terminal.WriteLine(Loc.Get("team.password_leader_only"));
            terminal.WriteLine("");
            await Task.Delay(2000);
            return;
        }

        terminal.WriteLine("");
        terminal.SetColor("cyan");
        terminal.Write(Loc.Get("team.enter_current_password"));
        terminal.SetColor("white");
        string currentPassword = await terminal.ReadLineAsync();

        // offline the in-memory copy is the password; an NPC team's is the one its NPCs hold (checked if set);
        // a player team's stored hash is checked below
        string heldPassword = npcTeam
            ? NPCSpawnSystem.Instance.ActiveNPCs.FirstOrDefault(n => n.Team == currentPlayer.Team && n.IsAlive)?.TeamPW ?? currentPlayer.TeamPW ?? ""
            : currentPlayer.TeamPW;
        if (backend == null && (!npcTeam || !string.IsNullOrEmpty(heldPassword)) && currentPassword != heldPassword)
        {
            terminal.WriteLine("");
            terminal.SetColor("red");
            terminal.WriteLine(Loc.Get("team.wrong_password_short"));
            terminal.WriteLine("");
            await Task.Delay(2000);
            return;
        }

        terminal.SetColor("cyan");
        terminal.Write(Loc.Get("team.enter_new_password"));
        terminal.SetColor("white");
        string newPassword = await terminal.ReadLineAsync();

        if (!string.IsNullOrEmpty(newPassword) && newPassword.Length <= 20)
        {
            if (backend != null && !backend.ChangeTeamPassword(currentPlayer.Team, myKey, currentPassword, newPassword))
            {
                terminal.WriteLine("");
                terminal.SetColor("red");
                terminal.WriteLine(Loc.Get("team.wrong_password_short"));
                terminal.WriteLine("");
                await Task.Delay(2000);
                return;
            }

            currentPlayer.TeamPW = newPassword;

            // Update all team members' passwords
            var allNPCs = NPCSpawnSystem.Instance.ActiveNPCs;
            foreach (var npc in allNPCs.Where(n => n.Team == currentPlayer.Team))
            {
                npc.TeamPW = newPassword;
            }

            terminal.WriteLine("");
            terminal.SetColor("bright_green");
            terminal.WriteLine(Loc.Get("team.password_changed"));
            terminal.WriteLine("");

            // Persist NPC password changes immediately. v0.57.11: changed from
            // fire-and-forget `_ = Task.Run(...)` to `await` — if another
            // session-driven world_state reload fires before the Task completes,
            // the password change is silently lost. Same class of bug as the
            // Coosh turf-reverts-on-relog problem fixed in v0.57.10.
            if (DoorMode.IsOnlineMode && OnlineStateManager.Instance != null)
            {
                try { await OnlineStateManager.Instance.SaveAllSharedState(); }
                catch (Exception ex) { DebugLogger.Instance.LogError("TEAM", $"SaveAllSharedState failed after password change: {ex.Message}"); }
            }
        }
        else
        {
            terminal.SetColor("red");
            terminal.WriteLine(Loc.Get("team.invalid_password"));
        }

        await Task.Delay(2000);
    }

    /// <summary>
    /// Send message to team members
    /// </summary>
    private async Task SendTeamMessage()
    {
        if (string.IsNullOrEmpty(currentPlayer.Team))
        {
            terminal.WriteLine("");
            terminal.SetColor("red");
            terminal.WriteLine(Loc.Get("team.not_in_team"));
            terminal.WriteLine("");
            await Task.Delay(2000);
            return;
        }

        terminal.WriteLine("");
        terminal.SetColor("cyan");
        terminal.WriteLine(Loc.Get("team.message_prompt"));
        terminal.Write(": ");
        terminal.SetColor("white");
        string message = await terminal.ReadLineAsync();

        if (!string.IsNullOrEmpty(message))
        {
            // v1.0.5: this printed "message sent!" and dropped the text. It now goes to
            // every other team member's mailbox (online-only menu entry, so the backend
            // is the SQL one), under the mailbox's daily send cap.
            var backend = SaveSystem.Instance.Backend as SqlSaveBackend;
            var members = backend != null
                ? await backend.GetPlayerTeamMembers(currentPlayer.Team, excludeDisplayName: currentPlayer.DisplayName)
                : new List<PlayerSummary>();

            terminal.WriteLine("");
            if (members.Count == 0)
            {
                terminal.SetColor("yellow");
                terminal.WriteLine(Loc.Get("team.message_no_recipients"));
            }
            else if (backend!.GetMailsSentToday(currentPlayer.DisplayName) + members.Count > 20)
            {
                // One row per member counts against the cap, same as sending each by hand.
                terminal.SetColor("red");
                terminal.WriteLine(Loc.Get("base.mail_daily_limit"));
            }
            else
            {
                if (message.Length > 200) message = message.Substring(0, 200);
                string body = $"[{currentPlayer.Team}] {message}";
                foreach (var member in members)
                {
                    await backend.SendMessage(currentPlayer.DisplayName, member.DisplayName, "mail", body);
                    // Live push for online members. Sessions are keyed by login
                    // username, not character name, so resolve first.
                    var memberUser = member.IsOnline ? backend.ResolvePlayerUsername(member.DisplayName) : null;
                    if (memberUser != null)
                        UsurperRemake.Server.MudServer.Instance?.SendToPlayer(memberUser,
                            $"\u001b[35m  {Loc.Get("team.mail_push_tag")} {currentPlayer.DisplayName}: {body}\u001b[0m");
                }
                terminal.SetColor("bright_green");
                terminal.WriteLine(Loc.Get("team.message_mailed", members.Count));
                terminal.SetColor("white");
                terminal.WriteLine(Loc.Get("team.your_message", message));
            }
            terminal.WriteLine("");
        }

        await Task.Delay(2000);
    }

    /// <summary>
    /// Sack a team member
    /// </summary>
    private async Task SackMember()
    {
        if (string.IsNullOrEmpty(currentPlayer.Team))
        {
            terminal.WriteLine("");
            terminal.SetColor("red");
            terminal.WriteLine(Loc.Get("team.not_in_team"));
            terminal.WriteLine("");
            await Task.Delay(2000);
            return;
        }

        // v1.1.12: every member is listed, players too. A player cannot be sacked: their membership is in
        // their own save, which only their session writes (no cross-session save writes), so they leave
        // with [L] themselves.
        var entries = await GetTeamMemberEntries(includeViewer: false);
        if (entries.Count == 0)
        {
            terminal.WriteLine("");
            terminal.SetColor("yellow");
            terminal.WriteLine(Loc.Get("team.only_member"));
            await Task.Delay(2000);
            return;
        }
        var picked = await PickFromList(entries, MemberPickRow, e => e.Name, "team.pick_sack_title", MemberPickColor);
        if (picked == null) return;
        if (picked.Npc == null)
        {
            terminal.WriteLine("");
            terminal.SetColor("yellow");
            terminal.WriteLine(Loc.Get("team.sack_player_refused", picked.Name));
            await Task.Delay(2500);
            return;
        }
        var member = picked.Npc;

        terminal.WriteLine("");
        terminal.SetColor("yellow");
        terminal.Write(Loc.Get("team.confirm_sack", member.DisplayName));
        string response = await terminal.ReadLineAsync();
        if (!GameConfig.IsAffirmative(response)) return;

        // v1.1.12: the NPC keeps what they wear when they go; offer to take it first
        bool tookGear = false;
        if (HasRemovableEquipment(member))
        {
            terminal.SetColor("yellow");
            terminal.WriteLine(Loc.Get("team.sack_gear_warning", member.DisplayName));
            terminal.Write(Loc.Get("team.sack_take_gear_prompt"));
            if (GameConfig.IsAffirmative(await terminal.ReadLineAsync()))
            {
                // v1.1.12: the live copy, checked still on this team before any gear comes off; a reload or
                // a move while the prompts were up means nothing is taken
                var liveForGear = LiveTeamNpc(member);
                if (liveForGear == null || liveForGear.Team != currentPlayer.Team)
                {
                    terminal.SetColor("red");
                    terminal.WriteLine(Loc.Get("team.sack_gear_gone", member.DisplayName));
                    await Task.Delay(2000);
                    return;
                }
                member = liveForGear;
                tookGear = await TakeAllEquipment(member, confirm: false) > 0;
            }
        }

        // v1.1.12: the NPC is looked up again by ID; a world_state reload while the prompts were up replaced
        // the object picked from the list, and clearing its Team changed nothing
        var live = LiveTeamNpc(member);
        if (live == null || live.Team != currentPlayer.Team)
        {
            terminal.SetColor("red");
            terminal.WriteLine(Loc.Get("team.member_gone_now", member.DisplayName));
            await Task.Delay(2000);
            return;
        }
        live.Team = "";
        live.TeamPW = "";
        live.CTurf = false;

        terminal.WriteLine("");
        terminal.SetColor("bright_green");
        terminal.WriteLine(Loc.Get("team.member_sacked", live.DisplayName));
        terminal.WriteLine("");

        NewsSystem.Instance.Newsy(true, Loc.Get("team.news_sacked", live.DisplayName, currentPlayer.Team));

        // Persist NPC team removal immediately. v0.57.11 (spudman
        // report: "Sacking members that died didn't seem to have any
        // effects other than history log"). Changed from fire-and-forget
        // `_ = Task.Run(...)` to `await` because if another online
        // session triggers a world_state reload between the `Team = ""`
        // mutation and the Task completing, the cleared Team field
        // gets overwritten from the stale snapshot and the sack is
        // silently undone. Same class of bug as the v0.57.10 Coosh
        // turf-reverts-on-relog issue.
        if (DoorMode.IsOnlineMode && OnlineStateManager.Instance != null)
        {
            try { await OnlineStateManager.Instance.SaveAllSharedState(); }
            catch (Exception ex) { DebugLogger.Instance.LogError("TEAM", $"SaveAllSharedState failed after sack: {ex.Message}"); }
        }
        // v1.1.12: the gear taken is in the player's pack; saved after the NPC's side (a crash between loses
        // it rather than copying it)
        if (tookGear) await ForcePlayerSave();

        terminal.SetColor("darkgray");
        terminal.WriteLine(Loc.Get("ui.press_enter"));
        await terminal.ReadKeyAsync();
    }

    /// <summary>v1.1.12: the character wears something that can be taken off (not cursed).</summary>
    private static bool HasRemovableEquipment(Character c) =>
        Enum.GetValues(typeof(EquipmentSlot)).Cast<EquipmentSlot>()
            .Where(slot => slot != EquipmentSlot.None)
            .Select(c.GetEquipment)
            .Any(item => item != null && !item.IsCursed);

    /// <summary>
    /// Resurrect a dead teammate
    /// </summary>
    private async Task ResurrectTeammate()
    {
        if (string.IsNullOrEmpty(currentPlayer.Team))
        {
            terminal.WriteLine("");
            terminal.SetColor("red");
            terminal.WriteLine(Loc.Get("team.not_in_team"));
            terminal.WriteLine("");
            await Task.Delay(2000);
            return;
        }

        // Find dead team members
        var allNPCs = NPCSpawnSystem.Instance.ActiveNPCs;
        var deadMembers = allNPCs
            .Where(n => n.Team == currentPlayer.Team && (n.IsDead || !n.IsAlive) && !n.IsAgedDeath && !n.IsPermaDead)
            .ToList();

        if (deadMembers.Count == 0)
        {
            // Check if there are permanently dead members that can't be resurrected
            var permadeadMembers = allNPCs
                .Where(n => n.Team == currentPlayer.Team && (n.IsDead || !n.IsAlive) && (n.IsPermaDead || n.IsAgedDeath))
                .ToList();

            terminal.WriteLine("");
            if (permadeadMembers.Count > 0)
            {
                terminal.SetColor("red");
                terminal.WriteLine(Loc.Get("team.no_resurrectables"));
                terminal.SetColor("darkgray");
                foreach (var pd in permadeadMembers)
                {
                    string reason = pd.IsAgedDeath ? Loc.Get("team.permadead_reason_age") : Loc.Get("team.permadead_reason_slain");
                    terminal.WriteLine($"  {pd.DisplayName} - {reason}");
                }
            }
            else
            {
                terminal.SetColor("bright_green");
                terminal.WriteLine(Loc.Get("team.all_alive"));
            }
            terminal.WriteLine("");
            await Task.Delay(2000);
            return;
        }

        // v1.1.12: listed in the shared picker, and the cost confirmed before it is paid
        var toResurrect = await PickFromList(deadMembers,
            d => Loc.Get("team.pick_resurrect_row", d.DisplayName, Loc.Get("inn.npc_level_class", d.Level, GameConfig.GetLocalizedClassName(d.Class)), $"{ResurrectionCost(d):N0}"),
            d => d.DisplayName, "team.dead_members_header", _ => "white");
        if (toResurrect == null) return;

        long cost = ResurrectionCost(toResurrect);
        terminal.WriteLine("");
        if (currentPlayer.Gold < cost)
        {
            terminal.SetColor("red");
            terminal.WriteLine(Loc.Get("team.need_gold_resurrect", $"{cost:N0}", toResurrect.DisplayName));
            await Task.Delay(2000);
            return;
        }
        terminal.SetColor("yellow");
        terminal.Write(Loc.Get("team.confirm_resurrect", toResurrect.DisplayName, $"{cost:N0}"));
        if (!GameConfig.IsAffirmative(await terminal.ReadLineAsync())) return;

        // v1.1.12: the NPC is looked up again by ID; a world_state reload while the prompts were up replaced
        // the object picked from the list, and the gold paid revived no one
        var live = LiveTeamNpc(toResurrect);
        if (live == null || live.Team != currentPlayer.Team || (live.IsAlive && !live.IsDead) || live.IsPermaDead || live.IsAgedDeath)
        {
            terminal.SetColor("red");
            terminal.WriteLine(Loc.Get("team.member_gone_now", toResurrect.DisplayName));
            await Task.Delay(2000);
            return;
        }

        // v1.1.12: a dead member holds a slot, so a revival adds no slot; it may bring the living up to
        // MaxTeamSize and no further (a team from before the cap, with more, keeps its members)
        int deadHere = NPCSpawnSystem.Instance.ActiveNPCs.Count(n => n.Team == currentPlayer.Team && (n.IsDead || !n.IsAlive) && !n.IsPermaDead && !n.IsAgedDeath);
        if (await TeamSlotsUsed(currentPlayer.Team) - deadHere >= MaxTeamSize)
        {
            terminal.SetColor("red");
            terminal.WriteLine(Loc.Get("team.team_full", MaxTeamSize));
            await Task.Delay(2000);
            return;
        }

        cost = ResurrectionCost(live);
        if (currentPlayer.Gold < cost)
        {
            terminal.SetColor("red");
            terminal.WriteLine(Loc.Get("team.need_gold_resurrect", $"{cost:N0}", live.DisplayName));
            await Task.Delay(2000);
            return;
        }

        currentPlayer.Gold -= cost;
        live.HP = live.MaxHP / 2; // Resurrect at half HP
        live.IsDead = false; // Clear permanent death flag - IsAlive is computed from HP > 0

        terminal.WriteLine("");
        terminal.SetColor("bright_green");
        terminal.WriteLine(Loc.Get("team.member_resurrected", live.DisplayName));
        terminal.WriteLine(Loc.Get("team.resurrect_cost", $"{cost:N0}"));

        NewsSystem.Instance.Newsy(true, Loc.Get("team.news_resurrected", live.DisplayName, currentPlayer.Team));

        // v1.1.12: saved at once, as Sack and Specialize do (the revival was lost to the next reload), and
        // the gold with it
        if (DoorMode.IsOnlineMode && OnlineStateManager.Instance != null)
        {
            try { await OnlineStateManager.Instance.SaveAllSharedState(); }
            catch (Exception ex) { DebugLogger.Instance.LogError("TEAM", $"SaveAllSharedState failed after resurrect: {ex.Message}"); }
        }
        await ForcePlayerSave();

        terminal.WriteLine("");
        terminal.SetColor("darkgray");
        terminal.WriteLine(Loc.Get("ui.press_enter"));
        await terminal.ReadKeyAsync();
    }

    private static long ResurrectionCost(NPC npc) => npc.Level * 1000L;

    /// <summary>
    /// Recruit a player's echo as a dungeon ally (online mode only).
    /// Their character will be loaded from the database and fight as AI.
    /// </summary>
    private async Task RecruitPlayerAlly()
    {
        if (string.IsNullOrEmpty(currentPlayer.Team))
        {
            terminal.WriteLine("");
            terminal.SetColor("red");
            terminal.WriteLine(Loc.Get("team.must_be_in_team_allies"));
            terminal.WriteLine(Loc.Get("team.create_join_first"));
            terminal.WriteLine("");
            await Task.Delay(2000);
            return;
        }

        var backend = SaveSystem.Instance.Backend as SqlSaveBackend;
        if (backend == null) return;

        string myUsername = currentPlayer.DisplayName.ToLower();
        var teammates = await backend.GetPlayerTeamMembers(currentPlayer.Team, myUsername);

        if (teammates.Count == 0)
        {
            terminal.WriteLine("");
            terminal.SetColor("yellow");
            terminal.WriteLine(Loc.Get("team.no_other_players"));
            terminal.WriteLine(Loc.Get("team.recruit_players_first"));
            terminal.WriteLine("");
            await Task.Delay(2000);
            return;
        }

        terminal.ClearScreen();
        WriteBoxHeader(Loc.Get("team_corner.player_recruit_header"), "bright_magenta");
        terminal.WriteLine("");

        terminal.SetColor("white");
        terminal.WriteLine(Loc.Get("team.team_label", currentPlayer.Team));
        terminal.WriteLine("");

        // Show currently-recruited echoes up front so the player can see the
        // state of their party at Team Corner instead of finding out at
        // dungeon entry. Bug report: player thought their recruit hadn't
        // landed (echo never appeared in dungeon) and re-tried, was told
        // "already in party" with no way to verify or recover.
        var currentRecruits = GameEngine.Instance?.DungeonPartyPlayerNames ?? new List<string>();
        if (currentRecruits.Count > 0)
        {
            terminal.SetColor("bright_cyan");
            terminal.WriteLine(Loc.Get("team.echo_currently_recruited", string.Join(", ", currentRecruits)));
            terminal.SetColor("gray");
            terminal.WriteLine(Loc.Get("team.echo_unrecruit_hint"));
            terminal.WriteLine("");
        }

        terminal.SetColor("cyan");
        terminal.WriteLine(Loc.Get("team.available_player_allies"));
        terminal.SetColor("white");
        terminal.WriteLine($"{Loc.Get("team.col_num"),-3} {Loc.Get("team.col_name"),-18} {Loc.Get("team.col_class"),-12} {Loc.Get("team.col_level_full"),-6} {Loc.Get("team.col_status"),-10}");
        if (!IsScreenReader)
        {
            terminal.SetColor("darkgray");
            terminal.WriteLine(new string('─', 52));
        }

        terminal.SetColor("white");
        for (int i = 0; i < teammates.Count; i++)
        {
            var tm = teammates[i];
            string className = GameConfig.GetLocalizedClassName(tm.ClassId);   // v1.1.12: localized
            string status = tm.IsOnline ? Loc.Get("team.status_online") : Loc.Get("team.status_offline");
            // Mark teammates already recruited with a [recruited] tag so the
            // player can see at a glance which slots are filled.
            bool alreadyRecruited = currentRecruits.Contains(tm.DisplayName, StringComparer.OrdinalIgnoreCase);
            string tag = alreadyRecruited ? " " + Loc.Get("team.echo_recruited_tag") : "";
            terminal.WriteLine($"{i + 1,-3} {tm.DisplayName,-18} {className,-12} {tm.Level,-6} {status,-10}{tag}");
        }

        terminal.WriteLine("");
        terminal.SetColor("gray");
        terminal.WriteLine(Loc.Get("team.echo_description"));
        terminal.WriteLine("");

        terminal.SetColor("cyan");
        terminal.Write(Loc.Get("team.select_ally"));
        terminal.SetColor("white");
        string input = await terminal.ReadLineAsync();

        // [U] Un-recruit flow: lets the player dismiss a stuck or
        // changed-their-mind echo without entering the dungeon. Recovery
        // path for the v0.61.5 "echo recruited but never materialized" bug.
        if (!string.IsNullOrEmpty(input) && input.Trim().Equals("U", StringComparison.OrdinalIgnoreCase))
        {
            await UnrecruitPlayerAlly(currentRecruits);
            return;
        }

        if (int.TryParse(input, out int choice) && choice >= 1 && choice <= teammates.Count)
        {
            var selected = teammates[choice - 1];

            // Check if already recruited
            var partyNames = GameEngine.Instance?.DungeonPartyPlayerNames ?? new List<string>();
            if (partyNames.Contains(selected.DisplayName, StringComparer.OrdinalIgnoreCase))
            {
                terminal.SetColor("yellow");
                terminal.WriteLine(Loc.Get("team.echo_already_in_party", selected.DisplayName));
                terminal.SetColor("gray");
                terminal.WriteLine(Loc.Get("team.echo_already_in_party_hint"));
                await Task.Delay(2500);
                return;
            }

            // Add to dungeon party
            var names = new List<string>(partyNames) { selected.DisplayName };
            GameEngine.Instance?.SetDungeonPartyPlayers(names);

            terminal.WriteLine("");
            terminal.SetColor("bright_cyan");
            terminal.WriteLine(Loc.Get("team.echo_will_join", selected.DisplayName));
            terminal.SetColor("gray");
            terminal.WriteLine(Loc.Get("team.echo_ai_note"));

            // Player report (Lv.68 Voidreaver): echoes silently failed to load
            // into the dungeon party because companions had already filled the
            // 4-slot cap. Warn at recruit time so the player can plan ahead
            // (companion dismiss, NPC un-recruit) instead of finding out only
            // after dungeon entry.
            int companionCount = UsurperRemake.Systems.CompanionSystem.Instance?
                .GetCompanionsAsCharacters()?.Count(c => c.IsAlive) ?? 0;
            int npcCount = GameEngine.Instance?.DungeonPartyNPCIds?.Count ?? 0;
            int echoCount = names.Count;
            int total = companionCount + npcCount + echoCount;
            const int maxPartySize = 4;
            if (total > maxPartySize)
            {
                terminal.WriteLine("");
                terminal.SetColor("yellow");
                terminal.WriteLine(Loc.Get("team.recruit_party_overflow", total, maxPartySize));
                terminal.SetColor("gray");
                terminal.WriteLine(Loc.Get("team.recruit_party_overflow_hint"));
            }
        }

        terminal.WriteLine("");
        terminal.SetColor("darkgray");
        terminal.WriteLine(Loc.Get("ui.press_enter"));
        await terminal.ReadKeyAsync();
    }

    /// <summary>
    /// Manual un-recruit flow for player echoes. Lets the player dismiss an
    /// echo from `DungeonPartyPlayerNames` without entering the dungeon.
    /// Recovery path for the case where an echo was recruited but failed to
    /// materialize (save deleted, off-team, load error) -- and also lets
    /// players who change their mind un-recruit cleanly.
    /// </summary>
    private async Task UnrecruitPlayerAlly(List<string> currentRecruits)
    {
        if (currentRecruits.Count == 0)
        {
            terminal.SetColor("yellow");
            terminal.WriteLine(Loc.Get("team.echo_unrecruit_none"));
            await Task.Delay(2000);
            return;
        }

        terminal.WriteLine("");
        terminal.SetColor("cyan");
        terminal.WriteLine(Loc.Get("team.echo_unrecruit_header"));
        terminal.SetColor("white");
        for (int i = 0; i < currentRecruits.Count; i++)
        {
            terminal.WriteLine($"  [{i + 1}] {currentRecruits[i]}");
        }
        terminal.WriteLine("");
        terminal.SetColor("cyan");
        terminal.Write(Loc.Get("team.echo_unrecruit_prompt"));
        terminal.SetColor("white");
        string input = await terminal.ReadLineAsync();

        if (int.TryParse(input, out int choice) && choice >= 1 && choice <= currentRecruits.Count)
        {
            string toRemove = currentRecruits[choice - 1];
            var cleaned = currentRecruits.Where(n => !n.Equals(toRemove, StringComparison.OrdinalIgnoreCase)).ToList();
            GameEngine.Instance?.SetDungeonPartyPlayers(cleaned);
            terminal.SetColor("bright_yellow");
            terminal.WriteLine(Loc.Get("team.echo_unrecruit_done", toRemove));
            await Task.Delay(2000);
        }
    }

    #endregion

    #region Equipment Management

    /// <summary>
    /// Equip a team member with items from your inventory
    /// </summary>
    private async Task EquipMember()
    {
        if (string.IsNullOrEmpty(currentPlayer.Team))
        {
            terminal.WriteLine("");
            terminal.SetColor("red");
            terminal.WriteLine(Loc.Get("team.not_in_team"));
            terminal.WriteLine("");
            await Task.Delay(2000);
            return;
        }

        // Get team members
        var allNPCs = NPCSpawnSystem.Instance.ActiveNPCs;
        var teamMembers = allNPCs
            .Where(n => n.Team == currentPlayer.Team && n.IsAlive)
            .ToList();

        if (teamMembers.Count == 0)
        {
            terminal.WriteLine("");
            terminal.SetColor("yellow");
            terminal.WriteLine(Loc.Get("team.no_living_members"));
            await Task.Delay(2000);
            return;
        }

        // v1.1.12: the shared picker (a number, or a name or the start of one)
        var selectedMember = await PickFromList(teamMembers,
            m => $"{m.DisplayName} ({Loc.Get("inn.npc_level_class", m.Level, GameConfig.GetLocalizedClassName(m.Class))})",
            m => m.DisplayName, "team_corner.equip_header");
        if (selectedMember == null) return;

        await ManageCharacterEquipment(selectedMember);

        // Sync equipment changes to canonical NPC in ActiveNPCs (handles orphaned references)
        CombatEngine.SyncNPCTeammateToActiveNPCs(selectedMember);

        // v1.1.12: online, the player's side is written now (the throttled AutoSave could skip it for a minute
        // while the NPC side below is written at once, so a crash between them could copy an item), then the NPCs
        if (DoorMode.IsOnlineMode) await ForcePlayerSave();
        else await SaveSystem.Instance.AutoSave(currentPlayer);

        // Force NPC world_state save so equipment survives world-sim reload cycles
        if (DoorMode.IsOnlineMode && OnlineStateManager.Instance != null)
        {
            try { await OnlineStateManager.Instance.SaveAllSharedState(); }
            catch (Exception ex) { DebugLogger.Instance.LogError("TEAM", $"SaveAllSharedState failed after equipment change: {ex.Message}"); }
        }
    }

    /// <summary>
    /// Manage equipment for a specific character (NPC teammate, spouse, or lover)
    /// This is a shared method that can be called from Team Corner or Home
    /// </summary>
    private async Task ManageCharacterEquipment(Character target)
    {
        while (true)
        {
            terminal.ClearScreen();
            WriteSectionHeader(Loc.Get("team.equip_header_label", target.DisplayName.ToUpper()), "bright_cyan");
            terminal.WriteLine("");

            // Show target's stats
            terminal.SetColor("white");
            terminal.WriteLine(Loc.Get("team.examine_level", target.Level, GameConfig.GetLocalizedClassName(target.Class), GameConfig.GetLocalizedRaceName(target.Race)));
            terminal.WriteLine(Loc.Get("team.examine_hp", target.HP, target.MaxHP, target.Mana, target.MaxMana));
            terminal.WriteLine(Loc.Get("team.examine_stats1", target.Strength, target.Dexterity, target.Agility, target.Constitution));
            terminal.WriteLine(Loc.Get("team.examine_stats2", target.Intelligence, target.Wisdom, target.Charisma, target.Defence));
            terminal.WriteLine("");

            // Show current equipment
            terminal.SetColor("bright_yellow");
            terminal.WriteLine(Loc.Get("team.current_equipment"));
            terminal.SetColor("white");

            DisplayEquipmentSlot(target, EquipmentSlot.MainHand, Loc.Get("team.slot_main_hand"));
            DisplayEquipmentSlot(target, EquipmentSlot.OffHand, Loc.Get("team.slot_off_hand"));
            DisplayEquipmentSlot(target, EquipmentSlot.Head, Loc.Get("team.slot_head"));
            DisplayEquipmentSlot(target, EquipmentSlot.Body, Loc.Get("team.slot_body"));
            DisplayEquipmentSlot(target, EquipmentSlot.Arms, Loc.Get("team.slot_arms"));
            DisplayEquipmentSlot(target, EquipmentSlot.Hands, Loc.Get("team.slot_hands"));
            DisplayEquipmentSlot(target, EquipmentSlot.Legs, Loc.Get("team.slot_legs"));
            DisplayEquipmentSlot(target, EquipmentSlot.Feet, Loc.Get("team.slot_feet"));
            DisplayEquipmentSlot(target, EquipmentSlot.Waist, Loc.Get("team.slot_waist"));
            DisplayEquipmentSlot(target, EquipmentSlot.Face, Loc.Get("team.slot_face"));
            DisplayEquipmentSlot(target, EquipmentSlot.Cloak, Loc.Get("team.slot_cloak"));
            DisplayEquipmentSlot(target, EquipmentSlot.Neck, Loc.Get("team.slot_neck"));
            DisplayEquipmentSlot(target, EquipmentSlot.LFinger, Loc.Get("team.slot_left_ring"));
            DisplayEquipmentSlot(target, EquipmentSlot.RFinger, Loc.Get("team.slot_right_ring"));
            terminal.WriteLine("");

            // Show options
            terminal.SetColor("cyan");
            terminal.WriteLine(Loc.Get("team.options_label"));
            WriteSRMenuOption("E", Loc.Get("team_corner.equip_item"));
            WriteSRMenuOption("B", Loc.Get("inn.equip_best"));
            WriteSRMenuOption("U", Loc.Get("team_corner.unequip_item"));
            WriteSRMenuOption("T", Loc.Get("team_corner.take_all"));
            WriteSRMenuOption("Q", Loc.Get("team_corner.done"));
            terminal.WriteLine("");

            terminal.SetColor("cyan");
            terminal.Write(Loc.Get("ui.choice"));
            terminal.SetColor("white");

            var choice = (await terminal.ReadLineAsync()).ToUpper().Trim();

            switch (choice)
            {
                case "E":
                    await EquipItemToCharacter(target);
                    break;
                case "B":
                    // v0.64.2 (player request): auto-equip best applicable gear
                    // from the player's backpack across all slots, instead of
                    // forcing slot-by-slot outfitting of a naked recruit.
                    await RunEquipBestGear(target);
                    break;
                case "U":
                    await UnequipItemFromCharacter(target);
                    break;
                case "T":
                    await TakeAllEquipment(target);
                    break;
                case "Q":
                case "":
                    return;
            }
        }
    }

    /// <summary>
    /// Display an equipment slot with its current item and stats
    /// </summary>
    private void DisplayEquipmentSlot(Character target, EquipmentSlot slot, string label)
    {
        DisplayEquipmentSlotWithStats(target, slot, label);
    }

    /// <summary>
    /// Equip an item from the player's inventory to a character (slot-based flow)
    /// </summary>
    private async Task EquipItemToCharacter(Character target)
    {
        // v0.64.2 (player request): loop back to the SLOT PICKER after each
        // equip instead of kicking out to the parent menu -- outfitting a
        // naked recruit means many equips in a row. Cancel at the slot picker
        // (Q / 0 / invalid) exits the whole flow.
        while (true)
        {

            terminal.ClearScreen();
            WriteSectionHeader(Loc.Get("team.equip_to_header", target.DisplayName.ToUpper()), "bright_cyan");
            terminal.WriteLine("");

            // Step 1: Pick a slot
            var selectedSlot = await PromptForEquipmentSlot(target);
            if (selectedSlot == null) return; // exit: cancel at slot picker

            // Step 2: Get items that match this slot
            var equipmentItems = GetItemsForSlot(selectedSlot.Value);

            if (equipmentItems.Count == 0)
            {
                terminal.WriteLine("");
                terminal.SetColor("yellow");
                terminal.WriteLine(Loc.Get("team.equip_no_items_for_slot"));
                await Task.Delay(2000);
                continue;
            }

            // Step 3: Show current item in slot
            terminal.WriteLine("");
            var currentItem = target.GetEquipment(selectedSlot.Value);
            terminal.SetColor("white");
            terminal.Write($"  {Loc.Get("weapon_shop.current_prefix")}");
            if (currentItem != null)
            {
                terminal.SetColor(currentItem.IsIdentified ? currentItem.GetRarityColor() : "magenta");
                terminal.Write(currentItem.IsIdentified ? currentItem.Name : Loc.Get("ui.unidentified"));
                if (currentItem.IsIdentified) WriteEquipmentStatSummary(currentItem);
                terminal.WriteLine("");
            }
            else
            {
                terminal.SetColor("darkgray");
                terminal.WriteLine(Loc.Get("ui.empty"));
            }
            terminal.WriteLine("");

            // Step 4: Display matching items with full stats
            terminal.SetColor("white");
            terminal.WriteLine(Loc.Get("team.available_equipment"));
            terminal.WriteLine("");
            DisplayEquipmentItemList(equipmentItems, target);

            terminal.WriteLine("");
            terminal.SetColor("cyan");
            terminal.Write(Loc.Get("team.select_item"));
            terminal.SetColor("white");

            var input = await terminal.ReadLineAsync();
            if (!int.TryParse(input, out int itemIdx) || itemIdx < 1 || itemIdx > equipmentItems.Count)
            {
                terminal.SetColor("gray");
                terminal.WriteLine(Loc.Get("ui.cancelled"));
                await Task.Delay(1000);
                continue;
            }

            var (selectedItem, wasEquipped, sourceSlot) = equipmentItems[itemIdx - 1];

            // Block unidentified items
            if (!selectedItem.IsIdentified)
            {
                terminal.SetColor("yellow");
                terminal.WriteLine(Loc.Get("team.equip_identify_first"));
                await Task.Delay(2000);
                continue;
            }

            // Check if target can equip
            if (!selectedItem.CanEquip(target, out string equipReason))
            {
                terminal.SetColor("red");
                terminal.WriteLine(Loc.Get("team.cannot_use_item", target.DisplayName, equipReason));
                await Task.Delay(2000);
                continue;
            }

            // Use the slot the player already picked (no need to ask which hand)
            EquipmentSlot? targetSlot = selectedSlot.Value;

            // Remove from player. v1.1.12: if nothing came off the player, nothing is equipped (the item
            // was equipped anyway, a copy)
            if (!TakeFromPlayerForEquip(selectedItem, wasEquipped, sourceSlot))
            {
                terminal.SetColor("red");
                terminal.WriteLine(Loc.Get("team.equip_item_gone", selectedItem.Name));
                await Task.Delay(2000);
                continue;
            }

            // Track items in target's inventory BEFORE equipping, so we can move displaced items to player
            var targetInventoryBefore = target.Inventory.Count;

            // Equip to target - EquipItem adds displaced items to target's inventory
            var result = target.EquipItem(selectedItem, targetSlot, out string message);
            target.RecalculateStats();

            if (result)
            {
                // Move any items that were added to target's inventory (displaced equipment) to player's inventory
                if (target.Inventory.Count > targetInventoryBefore)
                {
                    var displacedItems = target.Inventory.Skip(targetInventoryBefore).ToList();
                    foreach (var displaced in displacedItems)
                    {
                        target.Inventory.Remove(displaced);
                        currentPlayer.Inventory.Add(displaced);
                    }
                }

                // v0.57.7 (Hesperos report): `target` is a WRAPPER Character built fresh by
                // CompanionSystem.GetCompanionsAsCharacters() — edits to wrapper.EquippedItems
                // don't mutate the underlying Companion unless we explicitly sync. Without this
                // call Lyris reverted to her EquipStartingGear set on next wrapper regeneration.
                // Safe no-op for non-companion targets (team NPC).
                if (target.IsCompanion)
                    CompanionSystem.Instance?.SyncCompanionEquipment(target);

                terminal.WriteLine("");
                terminal.SetColor("bright_green");
                terminal.WriteLine(Loc.Get("team.equipped_success", target.DisplayName, selectedItem.Name));
                if (!string.IsNullOrEmpty(message))
                {
                    terminal.SetColor("yellow");
                    terminal.WriteLine(message);
                }
            }
            else
            {
                // Failed - return item to player
                var legacyItem = ConvertEquipmentToItem(selectedItem);
                currentPlayer.Inventory.Add(legacyItem);
                terminal.SetColor("red");
                terminal.WriteLine(Loc.Get("team.equip_failed", message));
            }

            await Task.Delay(2000);
        }
    }

    /// <summary>
    /// v1.1.12: takes the item being given from the player: off the slot it is worn in, or out of the pack
    /// (matched on name and power first, then on name). False when nothing was removed, and then nothing
    /// may be equipped.
    /// </summary>
    internal bool TakeFromPlayerForEquip(Equipment selectedItem, bool wasEquipped, EquipmentSlot? sourceSlot)
    {
        if (wasEquipped && sourceSlot.HasValue)
        {
            if (currentPlayer.UnequipSlot(sourceSlot.Value) == null) return false;
            currentPlayer.RecalculateStats();
            return true;
        }
        var invItem = currentPlayer.Inventory.FirstOrDefault(i =>
            i.Name == selectedItem.Name && i.Attack == selectedItem.WeaponPower && i.Armor == selectedItem.ArmorClass)
            ?? currentPlayer.Inventory.FirstOrDefault(i => i.Name == selectedItem.Name);
        return invItem != null && currentPlayer.Inventory.Remove(invItem);
    }

    /// <summary>
    /// Unequip an item from a character and add to player's inventory
    /// </summary>
    private async Task UnequipItemFromCharacter(Character target)
    {
        terminal.ClearScreen();
        WriteSectionHeader(Loc.Get("team.unequip_header", target.DisplayName.ToUpper()), "bright_cyan");
        terminal.WriteLine("");

        // Get all equipped slots
        var equippedSlots = new List<(EquipmentSlot slot, Equipment item)>();
        foreach (EquipmentSlot slot in Enum.GetValues(typeof(EquipmentSlot)))
        {
            if (slot == EquipmentSlot.None) continue;
            var item = target.GetEquipment(slot);
            if (item != null)
            {
                equippedSlots.Add((slot, item));
            }
        }

        if (equippedSlots.Count == 0)
        {
            terminal.SetColor("yellow");
            terminal.WriteLine(Loc.Get("team.no_equipment_unequip", target.DisplayName));
            await Task.Delay(2000);
            return;
        }

        terminal.SetColor("white");
        terminal.WriteLine(Loc.Get("team.equipped_items"));
        terminal.WriteLine("");

        for (int i = 0; i < equippedSlots.Count; i++)
        {
            var (slot, item) = equippedSlots[i];
            terminal.SetColor("bright_yellow");
            terminal.Write($"  {i + 1}. ");
            terminal.SetColor("gray");
            terminal.Write($"[{slot.GetDisplayName(),-12}] ");
            terminal.SetColor("white");
            terminal.Write($"{item.Name}");
            if (item.IsCursed)
            {
                terminal.SetColor("red");
                terminal.Write(Loc.Get("inn.cursed_label"));
            }
            terminal.WriteLine("");
        }

        terminal.WriteLine("");
        terminal.SetColor("cyan");
        terminal.Write(Loc.Get("team.select_slot_unequip"));
        terminal.SetColor("white");

        var input = await terminal.ReadLineAsync();
        if (!int.TryParse(input, out int slotIdx) || slotIdx < 1 || slotIdx > equippedSlots.Count)
        {
            terminal.SetColor("gray");
            terminal.WriteLine(Loc.Get("ui.cancelled"));
            await Task.Delay(1000);
            return;
        }

        var (selectedSlot, selectedItem) = equippedSlots[slotIdx - 1];

        // Check if cursed
        if (selectedItem.IsCursed)
        {
            terminal.SetColor("red");
            terminal.WriteLine(Loc.Get("team.cursed_cannot_remove", selectedItem.Name));
            await Task.Delay(2000);
            return;
        }

        // Unequip and add to player inventory
        var unequipped = target.UnequipSlot(selectedSlot);
        if (unequipped != null)
        {
            target.RecalculateStats();
            // v0.57.7 — sync wrapper unequip back to Companion (Hesperos report)
            if (target.IsCompanion)
                CompanionSystem.Instance?.SyncCompanionEquipment(target);
            var legacyItem = ConvertEquipmentToItem(unequipped);
            currentPlayer.Inventory.Add(legacyItem);

            terminal.WriteLine("");
            terminal.SetColor("bright_green");
            terminal.WriteLine(Loc.Get("team.took_item", unequipped.Name, target.DisplayName));
            terminal.SetColor("gray");
            terminal.WriteLine(Loc.Get("team.item_added_inventory"));
        }
        else
        {
            terminal.SetColor("red");
            terminal.WriteLine(Loc.Get("team.unequip_failed"));
        }

        await Task.Delay(2000);
    }

    /// <summary>
    /// Take all equipment from a character
    /// </summary>
    /// <remarks>v1.1.12: confirm false skips the question (Sack asks its own); returns the items taken.</remarks>
    private async Task<int> TakeAllEquipment(Character target, bool confirm = true)
    {
        if (confirm)
        {
            terminal.WriteLine("");
            terminal.SetColor("yellow");
            terminal.WriteLine(Loc.Get("team.take_all_confirm", target.DisplayName));
            terminal.Write(Loc.Get("team.take_all_warning"));
            terminal.SetColor("white");

            var answer = await terminal.ReadLineAsync();
            if (!GameConfig.IsAffirmative(answer))
            {
                terminal.SetColor("gray");
                terminal.WriteLine(Loc.Get("ui.cancelled"));
                await Task.Delay(1000);
                return 0;
            }
        }

        int itemsTaken = 0;
        var cursedItems = new List<string>();

        foreach (EquipmentSlot slot in Enum.GetValues(typeof(EquipmentSlot)))
        {
            if (slot == EquipmentSlot.None) continue;
            var item = target.GetEquipment(slot);
            if (item != null)
            {
                if (item.IsCursed)
                {
                    cursedItems.Add(item.Name);
                    continue;
                }

                var unequipped = target.UnequipSlot(slot);
                if (unequipped != null)
                {
                    var legacyItem = ConvertEquipmentToItem(unequipped);
                    currentPlayer.Inventory.Add(legacyItem);
                    itemsTaken++;
                }
            }
        }

        target.RecalculateStats();
        // v0.57.7 — sync wrapper take-all back to Companion (Hesperos report)
        if (itemsTaken > 0 && target.IsCompanion)
            CompanionSystem.Instance?.SyncCompanionEquipment(target);

        terminal.WriteLine("");
        if (itemsTaken > 0)
        {
            terminal.SetColor("bright_green");
            terminal.WriteLine(Loc.Get("team.took_items_count", itemsTaken, target.DisplayName));
        }
        else
        {
            terminal.SetColor("yellow");
            terminal.WriteLine(Loc.Get("team.no_equipment_take", target.DisplayName));
        }

        if (cursedItems.Count > 0)
        {
            terminal.SetColor("red");
            terminal.WriteLine(Loc.Get("team.cursed_not_removed", string.Join(", ", cursedItems)));
        }

        await Task.Delay(2000);
        return itemsTaken;
    }

    /// <summary>
    /// Convert Equipment to legacy Item for inventory storage
    /// </summary>
    private Item ConvertEquipmentToItem(Equipment equipment)
    {
        // Delegate to the canonical implementation that preserves LootEffects
        // (INT, CON, enchantments, proc effects)
        return currentPlayer.ConvertEquipmentToLegacyItem(equipment);
    }

    /// <summary>
    /// Convert EquipmentSlot to ObjType for legacy item system
    /// </summary>
    private ObjType SlotToObjType(EquipmentSlot slot) => slot switch
    {
        EquipmentSlot.Head => ObjType.Head,
        EquipmentSlot.Body => ObjType.Body,
        EquipmentSlot.Arms => ObjType.Arms,
        EquipmentSlot.Hands => ObjType.Hands,
        EquipmentSlot.Legs => ObjType.Legs,
        EquipmentSlot.Feet => ObjType.Feet,
        EquipmentSlot.MainHand => ObjType.Weapon,
        EquipmentSlot.OffHand => ObjType.Shield,
        EquipmentSlot.Neck => ObjType.Neck,
        EquipmentSlot.Neck2 => ObjType.Neck,
        EquipmentSlot.LFinger => ObjType.Fingers,
        EquipmentSlot.RFinger => ObjType.Fingers,
        EquipmentSlot.Cloak => ObjType.Abody,
        EquipmentSlot.Waist => ObjType.Waist,
        _ => ObjType.Magic
    };

    #endregion

    // ═══════════════════════════════════════════════════════════════════════════
    // Team Wars
    // ═══════════════════════════════════════════════════════════════════════════

    private async Task TeamWarMenu()
    {
        var backend = SaveSystem.Instance.Backend as SqlSaveBackend;
        if (backend == null) return;

        if (string.IsNullOrEmpty(currentPlayer.Team))
        {
            terminal.SetColor("red");
            terminal.WriteLine($"\n  {Loc.Get("team.war_must_be_in_team")}");
            await Task.Delay(2000);
            return;
        }

        while (true)
        {
            terminal.ClearScreen();
            WriteBoxHeader(Loc.Get("team_corner.wars_header"), "bright_red");
            terminal.SetColor("white");
            terminal.WriteLine(Loc.Get("team.your_team_war_label", currentPlayer.Team));
            int warsLeft = Math.Max(0, GameConfig.MaxTeamWarsPerDay - currentPlayer.TeamWarsToday);
            terminal.SetColor(warsLeft > 0 ? "gray" : "red");
            terminal.WriteLine($"  {Loc.Get("team.war_daily_remaining", warsLeft, GameConfig.MaxTeamWarsPerDay)}");
            terminal.WriteLine("");

            WriteSRMenuOption("C", Loc.Get("team_corner.challenge"));
            WriteSRMenuOption("H", Loc.Get("team_corner.war_history"));
            WriteSRMenuOption("Q", Loc.Get("ui.cancel"));
            terminal.SetColor("white");
            terminal.Write(Loc.Get("team.choice_label"));
            string input = (await terminal.ReadLineAsync())?.Trim().ToUpper() ?? "";

            if (input == "Q" || input == "") break;
            if (input == "C") await ChallengeTeamWar(backend);
            if (input == "H") await ShowWarHistory(backend);
        }
    }

    private async Task ChallengeTeamWar(SqlSaveBackend backend)
    {
        string myTeam = currentPlayer.Team;

        if (backend.HasActiveTeamWar(myTeam))
        {
            terminal.SetColor("red");
            terminal.WriteLine($"\n  {Loc.Get("team.active_war_exists")}");
            await Task.Delay(2000);
            return;
        }

        // v0.57.17 — daily cap on team-war challenges. Player report: at Lv.100 the
        // wager is 20k and the win pays 40k (wager * 2 from thin air). With no daily
        // cap and no opponent cooldown the system became a free-money printer the
        // moment a challenger identified a beatable team.
        if (currentPlayer.TeamWarsToday >= GameConfig.MaxTeamWarsPerDay)
        {
            terminal.SetColor("red");
            terminal.WriteLine($"\n  {Loc.Get("team.war_daily_cap", GameConfig.MaxTeamWarsPerDay)}");
            await Task.Delay(2500);
            return;
        }

        // Show available teams to challenge
        var allTeams = await backend.GetPlayerTeams();
        var opponents = allTeams.Where(t => !string.Equals(t.TeamName, myTeam, StringComparison.OrdinalIgnoreCase) && t.MemberCount > 0).ToList();
        if (opponents.Count == 0)
        {
            terminal.SetColor("gray");
            terminal.WriteLine($"\n  {Loc.Get("team.no_other_teams")}");
            await Task.Delay(2000);
            return;
        }

        terminal.WriteLine("");
        WriteSectionHeader(Loc.Get("team_corner.choose_opponent"), "bright_yellow");
        terminal.SetColor("darkgray");
        terminal.WriteLine($"  {Loc.Get("team.col_num"),-4} {Loc.Get("team.col_team"),-25} {Loc.Get("team.col_members"),-10}");
        if (!IsScreenReader)
            terminal.WriteLine("  " + new string('─', 40));

        for (int i = 0; i < opponents.Count; i++)
        {
            terminal.SetColor("bright_yellow");
            terminal.Write($"  {i + 1,-4} ");
            terminal.SetColor("white");
            terminal.Write($"{opponents[i].TeamName,-25} ");
            terminal.SetColor("cyan");
            terminal.WriteLine($"{opponents[i].MemberCount}");
        }

        terminal.SetColor("white");
        terminal.Write(Loc.Get("team.challenge_team_num"));
        string input = (await terminal.ReadLineAsync())?.Trim() ?? "";
        if (!int.TryParse(input, out int choice) || choice < 1 || choice > opponents.Count) return;

        var enemyTeam = opponents[choice - 1];

        // v0.57.17 — per-opponent cooldown. Stops "find a beatable team, farm it
        // every minute" by enforcing a wait between consecutive challenges against
        // the same defender. Reads from the team_wars history rather than tracking
        // separate state — any war (won or lost, by any challenger from our team)
        // counts toward the cooldown.
        var recentHistory = await backend.GetTeamWarHistory(myTeam, limit: 20);
        var cooldownCutoff = DateTime.UtcNow.AddHours(-GameConfig.TeamWarOpponentCooldownHours);
        var recentVsThisOpponent = recentHistory.FirstOrDefault(w =>
            w.StartedAt > cooldownCutoff && w.Status != "abandoned" &&   // v1.1.12: a war that never ran does not count
            ((w.ChallengerTeam == myTeam && w.DefenderTeam == enemyTeam.TeamName) ||
             (w.DefenderTeam == myTeam && w.ChallengerTeam == enemyTeam.TeamName)));
        if (recentVsThisOpponent != null)
        {
            var hoursLeft = Math.Max(1, (int)Math.Ceiling((recentVsThisOpponent.StartedAt - cooldownCutoff).TotalHours));
            terminal.SetColor("red");
            terminal.WriteLine($"\n  {Loc.Get("team.war_opponent_cooldown", enemyTeam.TeamName, hoursLeft)}");
            await Task.Delay(2500);
            return;
        }

        long wager = Math.Max(1000, currentPlayer.Level * 200);

        terminal.SetColor("yellow");
        terminal.WriteLine(Loc.Get("team.war_wager", $"{wager:N0}"));
        terminal.SetColor("gray");
        terminal.WriteLine($"  {Loc.Get("team.war_daily_remaining", GameConfig.MaxTeamWarsPerDay - currentPlayer.TeamWarsToday, GameConfig.MaxTeamWarsPerDay)}");
        terminal.SetColor("yellow");
        terminal.Write(Loc.Get("team.confirm_war", enemyTeam.TeamName));
        string confirm = (await terminal.ReadLineAsync())?.Trim().ToUpper() ?? "";
        if (!GameConfig.IsAffirmative(confirm)) return;

        if (currentPlayer.Gold < wager)
        {
            terminal.SetColor("red");
            terminal.WriteLine(Loc.Get("team.not_enough_gold_wager"));
            await Task.Delay(1500);
            return;
        }

        // Load both teams' members for combat
        var myMembers = await backend.GetPlayerTeamMembers(myTeam);
        var enemyMembers = await backend.GetPlayerTeamMembers(enemyTeam.TeamName);

        if (myMembers.Count == 0 || enemyMembers.Count == 0)
        {
            terminal.SetColor("red");
            terminal.WriteLine(Loc.Get("team.no_members_war"));
            await Task.Delay(1500);
            return;
        }

        // v1.1.12: the wager is taken and SAVED before the war row exists. A war a lost session leaves
        // active is refunded from the row (SqlSaveBackend.ExpireStaleTeamWars), so the payment must be on
        // disk first, or the refund would pay a wager that was never taken.
        currentPlayer.Gold -= wager;
        if (!await ForcePlayerSave())
        {
            currentPlayer.Gold += wager;
            terminal.SetColor("red");
            terminal.WriteLine(Loc.Get("team.failed_generic"));
            await Task.Delay(1500);
            return;
        }

        int warId = await backend.CreateTeamWar(myTeam, enemyTeam.TeamName, wager, GameEngine.GoldTransferKey(currentPlayer));
        if (warId < 0)
        {
            currentPlayer.Gold += wager; // refund
            await ForcePlayerSave();
            terminal.SetColor("red");
            terminal.WriteLine($"  {Loc.Get("team.active_war_exists")}");
            await Task.Delay(2000);
            return;
        }

        terminal.ClearScreen();
        WriteSectionHeader(Loc.Get("team.war_title", myTeam, enemyTeam.TeamName), "bright_red");
        terminal.WriteLine("");

        int myWins = 0, enemyWins = 0;
        int rounds = Math.Min(myMembers.Count, enemyMembers.Count);

        for (int i = 0; i < rounds; i++)
        {
            var mySummary = myMembers[i];
            var enemySummary = enemyMembers[i];

            // Load characters from save data
            // v1.1.1: saves are keyed by login username; a display name that differed from it
            // loaded nothing, every round was skipped, and the challenger lost the wager on a
            // war that never ran.
            var myData = await backend.ReadGameData((string.IsNullOrEmpty(mySummary.Username) ? backend.ResolvePlayerUsername(mySummary.DisplayName) : mySummary.Username) ?? mySummary.DisplayName);
            var enemyData = await backend.ReadGameData((string.IsNullOrEmpty(enemySummary.Username) ? backend.ResolvePlayerUsername(enemySummary.DisplayName) : enemySummary.Username) ?? enemySummary.DisplayName);
            if (myData?.Player == null || enemyData?.Player == null) continue;

            var myFighter = PlayerCharacterLoader.CreateFromSaveData(myData.Player, mySummary.DisplayName);
            var enemyFighter = PlayerCharacterLoader.CreateFromSaveData(enemyData.Player, enemySummary.DisplayName);

            // Quick auto-resolved combat (no UI, just determine winner by stats)
            long myPower = myFighter.Level * 10 + myFighter.Strength + myFighter.WeapPow + myFighter.Dexterity;
            long enemyPower = enemyFighter.Level * 10 + enemyFighter.Strength + enemyFighter.WeapPow + enemyFighter.Dexterity;
            // Add randomness (±20%)
            myPower = (long)(myPower * (0.8 + Random.Shared.NextDouble() * 0.4));
            enemyPower = (long)(enemyPower * (0.8 + Random.Shared.NextDouble() * 0.4));

            bool myWin = myPower >= enemyPower;
            if (myWin) myWins++; else enemyWins++;

            terminal.SetColor(myWin ? "bright_green" : "bright_red");
            terminal.Write(Loc.Get("team.round_label", i + 1));
            terminal.SetColor("white");
            terminal.Write(Loc.Get("team.war_round_line", mySummary.DisplayName, mySummary.Level, enemySummary.DisplayName, enemySummary.Level));
            terminal.SetColor(myWin ? "bright_green" : "bright_red");
            terminal.WriteLine(myWin ? Loc.Get("team.fighter_wins", mySummary.DisplayName) : Loc.Get("team.fighter_wins", enemySummary.DisplayName));

            await backend.UpdateTeamWarScore(warId, myWin);
            await Task.Delay(800);
        }

        terminal.WriteLine("");

        // v1.1.12: no round could be fought (no save loaded); this counted as a defender win, the wager lost
        // on a war that never ran. Now it is abandoned, the wager returned, and no daily war used.
        if (myWins + enemyWins == 0)
        {
            // v1.1.12: refunded here only if this guarded flip landed; otherwise the war is still active
            // and the stale cleanup refunds it once (ExpireStaleTeamWars), so it is never paid twice
            if (await backend.CompleteTeamWar(warId, "abandoned"))
            {
                currentPlayer.Gold += wager;
                await ForcePlayerSave();
                terminal.SetColor("yellow");
                terminal.WriteLine(Loc.Get("team.war_no_rounds", $"{wager:N0}"));
            }
            else
            {
                terminal.SetColor("yellow");
                terminal.WriteLine(Loc.Get("team.war_no_rounds_pending", $"{wager:N0}"));
            }
            await terminal.PressAnyKey();
            return;
        }

        bool weWon = myWins > enemyWins;
        string result = weWon ? "challenger_won" : "defender_won";
        // v1.1.12: paid or charged only if this guarded flip landed, as in the no-round path. Otherwise nothing
        // changes hands here: the stale cleanup (ExpireStaleTeamWars) closes the war once, refunding the wager
        // only if no round was recorded, so a won war can never pay twice.
        if (!await backend.CompleteTeamWar(warId, result))
        {
            string? status = await backend.GetTeamWarStatus(warId);
            bool closed = status != null && status != "active";
            terminal.SetColor("yellow");
            terminal.WriteLine(Loc.Get("team.war_score", myWins, enemyWins));
            terminal.WriteLine(closed ? Loc.Get("team.war_already_closed") : Loc.Get("team.war_result_pending", $"{wager:N0}"));
            await terminal.PressAnyKey();
            return;
        }

        // v0.57.17 — increment daily counter regardless of outcome (ran a war = burned a slot)
        currentPlayer.TeamWarsToday++;

        if (weWon)
        {
            // v0.57.17 — reduced from wager*2 (net +100% per win) to wager*1.5 (net +50%
            // per win) so even within the daily cap each win is less of a printer.
            long reward = (long)(wager * GameConfig.TeamWarRewardMultiplier);
            currentPlayer.Gold += reward;
            WriteSectionHeader(Loc.Get("team_corner.your_team_wins"), "bright_green");
            terminal.SetColor("yellow");
            terminal.WriteLine(Loc.Get("team.war_score", myWins, enemyWins));
            terminal.WriteLine(Loc.Get("team.war_spoils", $"{reward:N0}"));
        }
        else
        {
            WriteSectionHeader(Loc.Get("team_corner.your_team_loses"), "bright_red");
            terminal.SetColor("yellow");
            terminal.WriteLine(Loc.Get("team.war_score", myWins, enemyWins));
            terminal.WriteLine(Loc.Get("team.war_lost_gold", $"{wager:N0}"));
        }

        // v1.1.12: the daily count and the spoils are saved now, not at the next throttled autosave
        await ForcePlayerSave();

        if (UsurperRemake.Systems.OnlineStateManager.IsActive)
        {
            string winner = weWon ? myTeam : enemyTeam.TeamName;
            _ = UsurperRemake.Systems.OnlineStateManager.Instance!.AddNews(
                Loc.Get("team.news_war_result", winner, myTeam, enemyTeam.TeamName, myWins, enemyWins), "team_war");
        }

        await terminal.PressAnyKey();
    }

    private async Task ShowWarHistory(SqlSaveBackend backend)
    {
        string myTeam = currentPlayer.Team;
        // v1.1.12: more than the last ten, a page at a time; a war still running is left out, and one that
        // was abandoned says so instead of counting as a loss
        var wars = (await backend.GetTeamWarHistory(myTeam, limit: 100)).Where(w => w.Status != "active").ToList();

        void Header()
        {
            terminal.ClearScreen();
            terminal.WriteLine("");
            WriteSectionHeader(Loc.Get("team_corner.war_history_header"), "bright_red");
        }

        if (wars.Count == 0)
        {
            Header();
            terminal.SetColor("gray");
            terminal.WriteLine(Loc.Get("team.no_wars_yet"));
            await terminal.PressAnyKey();
            return;
        }

        await ShowPaged(wars, Header, (war, _) =>
        {
            bool weChallenger = string.Equals(war.ChallengerTeam, myTeam, StringComparison.OrdinalIgnoreCase);
            string opponent = weChallenger ? war.DefenderTeam : war.ChallengerTeam;
            int ourWins = weChallenger ? war.ChallengerWins : war.DefenderWins;
            int theirWins = weChallenger ? war.DefenderWins : war.ChallengerWins;
            bool abandoned = war.Status == "abandoned";
            bool weWon = ourWins > theirWins;

            terminal.SetColor(abandoned ? "gray" : weWon ? "bright_green" : "bright_red");
            terminal.Write($"  {(abandoned ? Loc.Get("team.war_abandoned") : weWon ? Loc.Get("team.war_win") : Loc.Get("team.war_loss"))} ");
            terminal.SetColor("white");
            terminal.Write(Loc.Get("team.war_vs", opponent));
            terminal.SetColor("gray");
            terminal.WriteLine(Loc.Get("team.war_record", ourWins, theirWins, $"{war.GoldWagered:N0}"));
        });
    }

    // ═══════════════════════════════════════════════════════════════════════════
    // Team Headquarters
    // ═══════════════════════════════════════════════════════════════════════════

    /// <summary>
    /// v1.1.10: the price of a facility's next level: its base cost times the next level squared.
    /// The menu showed this, but the purchase charged the base cost times the next level, far less at
    /// high levels (maintainer: the shown price is the intended one). Both read this now.
    /// </summary>
    internal static long UpgradeCost(long baseCost, int currentLevel) => baseCost * (currentLevel + 1) * (currentLevel + 1);

    private static readonly Dictionary<string, (string NameKey, string DescKey, long BaseCost)> UpgradeDefinitions = new()
    {
        ["armory"]   = ("team.upgrade_armory",    "team.upgrade_armory_desc",    5000),
        ["barracks"] = ("team.upgrade_barracks",  "team.upgrade_barracks_desc",  5000),
        ["training"] = ("team.upgrade_training",  "team.upgrade_training_desc",  8000),
        ["vault"]    = ("team.upgrade_vault",     "team.upgrade_vault_desc",     3000),
        ["infirmary"]= ("team.upgrade_infirmary", "team.upgrade_infirmary_desc", 4000),
    };

    private async Task TeamHeadquartersMenu()
    {
        var backend = SaveSystem.Instance.Backend as SqlSaveBackend;
        if (backend == null) return;

        if (string.IsNullOrEmpty(currentPlayer.Team))
        {
            terminal.SetColor("red");
            terminal.WriteLine($"\n  {Loc.Get("team.hq_must_be_in_team")}");
            await Task.Delay(2000);
            return;
        }

        string teamName = currentPlayer.Team;

        while (true)
        {
            terminal.ClearScreen();
            WriteBoxHeader(Loc.Get("team.hq_title", teamName), "bright_cyan");
            terminal.WriteLine("");

            // Show upgrades
            var upgrades = await backend.GetTeamUpgrades(teamName);
            long vaultGold = await backend.GetTeamVaultGold(teamName);
            int vaultLevel = backend.GetTeamUpgradeLevel(teamName, "vault");
            long vaultCapacity = SqlSaveBackend.TeamVaultCapacity(vaultLevel);

            WriteSectionHeader(Loc.Get("team_corner.facilities"), "bright_yellow");
            terminal.WriteLine("");

            int idx = 1;
            foreach (var (key, def) in UpgradeDefinitions)
            {
                var existing = upgrades.FirstOrDefault(u => u.UpgradeType == key);
                int level = existing?.Level ?? 0;
                // Quadratic scaling: tier 5 costs 25x tier 1 instead of 5x. Pre-tune,
                // a full 5-facility HQ upgrade was ~375k gold (achievable solo in a
                // day at mid-level). Now ~1.5M, requiring guild-pooled grinding.
                long nextCost = UpgradeCost(def.BaseCost, level);

                terminal.SetColor("bright_yellow");
                terminal.Write($"  {idx}. ");
                terminal.SetColor("white");
                terminal.Write($"{Loc.Get(def.NameKey),-22} ");
                terminal.SetColor(level > 0 ? "bright_green" : "gray");
                terminal.Write($"{Loc.Get("team.facility_lv", level),-7} ");
                terminal.SetColor("gray");
                terminal.WriteLine($"({Loc.Get(def.DescKey)})  {Loc.Get("team.facility_upgrade_cost", $"{nextCost:N0}")}");
                idx++;
            }

            terminal.WriteLine("");
            terminal.SetColor("bright_green");
            terminal.WriteLine(Loc.Get("team.vault_display", $"{vaultGold:N0}", $"{vaultCapacity:N0}"));
            terminal.WriteLine("");

            WriteSRMenuOption("U", Loc.Get("team_corner.upgrade"));
            WriteSRMenuOption("D", Loc.Get("team_corner.deposit"));
            WriteSRMenuOption("W", Loc.Get("team_corner.withdraw"));
            WriteSRMenuOption("Q", Loc.Get("ui.cancel"));
            terminal.SetColor("white");
            terminal.Write(Loc.Get("team.choice_label"));
            string input = (await terminal.ReadLineAsync())?.Trim().ToUpper() ?? "";

            if (input == "Q" || input == "") break;

            switch (input)
            {
                case "U": await UpgradeFacility(backend, teamName); break;
                case "D": await DepositToVault(backend, teamName); break;
                case "W": await WithdrawFromVault(backend, teamName); break;
            }
        }
    }

    private async Task UpgradeFacility(SqlSaveBackend backend, string teamName)
    {
        var keys = UpgradeDefinitions.Keys.ToList();

        terminal.SetColor("white");
        terminal.Write(Loc.Get("team.upgrade_num_prompt"));
        string input = (await terminal.ReadLineAsync())?.Trim() ?? "";
        if (!int.TryParse(input, out int choice) || choice < 1 || choice > keys.Count) return;

        string key = keys[choice - 1];
        var def = UpgradeDefinitions[key];
        int currentLevel = backend.GetTeamUpgradeLevel(teamName, key);

        if (currentLevel >= GameConfig.MaxTeamFacilityLevel)
        {
            terminal.SetColor("yellow");
            terminal.WriteLine(Loc.Get("team.max_level_reached"));
            await Task.Delay(1500);
            return;
        }

        long cost = UpgradeCost(def.BaseCost, currentLevel);

        // Try team vault first, then personal gold
        long vaultGold = await backend.GetTeamVaultGold(teamName);

        terminal.SetColor("yellow");
        terminal.WriteLine(Loc.Get("team.upgrade_cost_info", Loc.Get(def.NameKey), currentLevel + 1, $"{cost:N0}"));
        terminal.WriteLine(Loc.Get("team.vault_and_gold", $"{vaultGold:N0}", $"{currentPlayer.Gold:N0}"));
        terminal.Write(Loc.Get("team.pay_from_prompt"));
        terminal.SetColor("bright_yellow");
        terminal.Write("V");
        terminal.SetColor("yellow");
        terminal.Write(Loc.Get("team.pay_vault"));
        terminal.SetColor("bright_yellow");
        terminal.Write("P");
        terminal.SetColor("yellow");
        terminal.Write(Loc.Get("team.pay_personal"));
        string payChoice = (await terminal.ReadLineAsync())?.Trim().ToUpper() ?? "";

        // v1.1.12: the level rises only if it is still the one priced (and below the cap), so two members
        // upgrading at once cannot both pay and both raise it. Paid from the vault, the payment and the
        // level change are one transaction. Paid personally, the gold is taken and saved first and given
        // back if the upgrade does not land (the bool was ignored, the gold kept).
        bool landed;
        if (payChoice == "V")
        {
            if (vaultGold < cost)
            {
                terminal.SetColor("red");
                terminal.WriteLine(Loc.Get("team.vault_not_enough"));
                await Task.Delay(1500);
                return;
            }
            landed = backend.TryUpgradeTeamFacility(teamName, key, currentLevel, cost, payFromVault: true);
        }
        else if (payChoice == "P")
        {
            if (currentPlayer.Gold < cost)
            {
                terminal.SetColor("red");
                terminal.WriteLine(Loc.Get("team.personal_not_enough"));
                await Task.Delay(1500);
                return;
            }
            currentPlayer.Gold -= cost;
            if (!await ForcePlayerSave())
            {
                currentPlayer.Gold += cost;
                landed = false;
            }
            else
            {
                landed = backend.TryUpgradeTeamFacility(teamName, key, currentLevel, cost, payFromVault: false);
                if (!landed)
                {
                    currentPlayer.Gold += cost;
                    await ForcePlayerSave();
                }
            }
        }
        else return;

        if (!landed)
        {
            terminal.SetColor("red");
            terminal.WriteLine(Loc.Get("team.upgrade_not_landed"));
            await Task.Delay(2000);
            return;
        }

        terminal.SetColor("bright_green");
        terminal.WriteLine(Loc.Get("team.facility_upgraded", Loc.Get(def.NameKey), currentLevel + 1));

        // Refresh cached HQ upgrade levels on the player
        TeamHQBonus.RefreshLevels(currentPlayer, backend);

        await Task.Delay(2000);
    }

    private async Task DepositToVault(SqlSaveBackend backend, string teamName)
    {
        int vaultLevel = backend.GetTeamUpgradeLevel(teamName, "vault");
        long vaultCapacity = SqlSaveBackend.TeamVaultCapacity(vaultLevel);
        long currentVault = await backend.GetTeamVaultGold(teamName);
        long space = vaultCapacity - currentVault;

        if (space <= 0)
        {
            terminal.SetColor("red");
            terminal.WriteLine(Loc.Get("team.vault_full"));
            await Task.Delay(1500);
            return;
        }

        terminal.SetColor("white");
        terminal.Write(Loc.Get("team.deposit_prompt", $"{Math.Min(space, currentPlayer.Gold):N0}"));
        string input = (await terminal.ReadLineAsync())?.Trim() ?? "";
        if (!long.TryParse(input, out long amount) || amount <= 0) return;

        amount = Math.Min(amount, Math.Min(space, currentPlayer.Gold));
        if (amount <= 0) return;

        // v1.1.12: the vault row was credited at once but the gold left the player only in memory, so a
        // crash before the next autosave kept both. Now the gold is taken and saved first, then the vault
        // is credited (capacity checked in the SQL); if the credit fails the gold comes back.
        currentPlayer.Gold -= amount;
        bool deposited = await ForcePlayerSave() && await backend.DepositToTeamVault(teamName, amount);
        if (!deposited)
        {
            currentPlayer.Gold += amount;
            await ForcePlayerSave();
            terminal.SetColor("red");
            terminal.WriteLine(Loc.Get("team.vault_full"));
            await Task.Delay(1500);
            return;
        }
        terminal.SetColor("bright_green");
        terminal.WriteLine(Loc.Get("team.deposited", $"{amount:N0}"));
        await Task.Delay(1500);
    }

    private async Task WithdrawFromVault(SqlSaveBackend backend, string teamName)
    {
        long currentVault = await backend.GetTeamVaultGold(teamName);
        if (currentVault <= 0)
        {
            terminal.SetColor("gray");
            terminal.WriteLine(Loc.Get("team.vault_empty"));
            await Task.Delay(1500);
            return;
        }

        terminal.SetColor("white");
        terminal.Write(Loc.Get("team.withdraw_prompt", $"{currentVault:N0}"));
        string input = (await terminal.ReadLineAsync())?.Trim() ?? "";
        if (!long.TryParse(input, out long amount) || amount <= 0) return;

        amount = Math.Min(amount, currentVault);

        // v1.1.12: asked first, the vault is the whole team's
        terminal.SetColor("yellow");
        terminal.Write(Loc.Get("team.confirm_withdraw", $"{amount:N0}"));
        if (!GameConfig.IsAffirmative(await terminal.ReadLineAsync())) return;

        bool success = await backend.WithdrawFromTeamVault(teamName, amount);
        if (success)
        {
            // v1.1.12: saved at once; the vault was debited at once while the gold waited for the next
            // throttled autosave, and a crash between lost it
            currentPlayer.Gold += amount;
            await ForcePlayerSave();
            terminal.SetColor("bright_green");
            terminal.WriteLine(Loc.Get("team.withdrew", $"{amount:N0}"));
        }
        else
        {
            terminal.SetColor("red");
            terminal.WriteLine(Loc.Get("team.withdrawal_failed"));
        }
        await Task.Delay(1500);
    }

    /// <summary>
    /// v0.57.2 — show the party inventory viewer for team NPCs and active companions.
    /// Lets the player see what loot items have accumulated on each party member and take
    /// items back to their own inventory. Covers the "my item vanished into an NPC's bag"
    /// confusion reported by Lumina and Aura.
    /// </summary>
    private async Task ViewTeamInventories()
    {
        var party = new List<Character>();

        // Team NPCs (only if the player is on a team)
        if (!string.IsNullOrEmpty(currentPlayer.Team) && NPCSpawnSystem.Instance?.ActiveNPCs != null)
        {
            var teamMembers = NPCSpawnSystem.Instance.ActiveNPCs
                .Where(n => n.Team == currentPlayer.Team && n.IsAlive && !n.IsDead)
                .Cast<Character>()
                .ToList();
            party.AddRange(teamMembers);
        }

        // Active companions (always include regardless of team status)
        if (CompanionSystem.Instance != null)
        {
            foreach (var comp in CompanionSystem.Instance.GetCompanionsAsCharacters())
            {
                if (comp != null && !party.Contains(comp))
                    party.Add(comp);
            }
        }

        await ShowPartyInventoryViewer(party);
    }

    private async Task SpecializeMember()
    {
        if (string.IsNullOrEmpty(currentPlayer.Team))
        {
            terminal.WriteLine("");
            terminal.SetColor("red");
            terminal.WriteLine(Loc.Get("team.not_in_team"));
            terminal.WriteLine("");
            await Task.Delay(2000);
            return;
        }

        // Get NPC team members (not companions, not the player)
        var allNPCs = NPCSpawnSystem.Instance.ActiveNPCs;
        var teamMembers = allNPCs
            .Where(n => n.Team == currentPlayer.Team && n.IsAlive && !n.IsDead)
            .ToList();

        if (teamMembers.Count == 0)
        {
            terminal.WriteLine("");
            terminal.SetColor("yellow");
            terminal.WriteLine(Loc.Get("spec.no_team_members"));
            terminal.WriteLine("");
            await terminal.PressAnyKey();
            return;
        }

        // v1.1.12: the shared picker
        var selectedNPC = await PickFromList(teamMembers,
            m => $"{m.DisplayName} ({Loc.Get("inn.npc_level_class", m.Level, GameConfig.GetLocalizedClassName(m.Class))}) "
                 + (m.Specialization != ClassSpecialization.None ? SpecTag(m).Trim() : Loc.Get("spec.none_label")),
            m => m.DisplayName, "spec.title");
        if (selectedNPC == null) return;
        await ShowSpecOptions(selectedNPC);
    }

    private async Task ShowSpecOptions(NPC npc)
    {
        var specs = UsurperRemake.Data.SpecializationData.GetSpecsForClass(npc.Class);

        if (specs.Count == 0)
        {
            terminal.WriteLine("");
            terminal.SetColor("yellow");
            terminal.WriteLine(Loc.Get("spec.no_specs_available", npc.ClassName));
            terminal.WriteLine("");
            await terminal.PressAnyKey();
            return;
        }

        terminal.ClearScreen();
        terminal.SetColor("bright_cyan");
        terminal.WriteLine(Loc.Get("spec.choose_spec_title", npc.DisplayName, npc.ClassName));
        terminal.WriteLine("");

        // Show current spec
        string currentSpecName = npc.Specialization != ClassSpecialization.None
            ? (UsurperRemake.Data.SpecializationData.GetSpec(npc.Specialization)?.Name ?? npc.Specialization.ToString())
            : Loc.Get("spec.unspecialized");
        terminal.SetColor("white");
        terminal.WriteLine(Loc.Get("spec.current_spec", currentSpecName));
        terminal.WriteLine("");

        // Option 0: Remove specialization
        terminal.SetColor("bright_yellow");
        terminal.Write("  0. ");
        terminal.SetColor("gray");
        terminal.WriteLine(Loc.Get("spec.remove_spec"));
        terminal.WriteLine("");

        // Show available specs with stat growth comparison
        for (int i = 0; i < specs.Count; i++)
        {
            var spec = specs[i];
            bool isCurrentSpec = npc.Specialization == spec.Spec;

            terminal.SetColor(isCurrentSpec ? "bright_green" : "bright_yellow");
            terminal.Write($"  {i + 1}. ");
            terminal.SetColor(isCurrentSpec ? "bright_green" : "white");
            terminal.Write($"{spec.Name} ");
            terminal.SetColor("cyan");
            terminal.Write($"({SpecRoleName(spec.Role)}) ");
            if (isCurrentSpec)
            {
                terminal.SetColor("bright_green");
                terminal.Write(Loc.Get("spec.active_marker"));
            }
            terminal.WriteLine("");

            // Description
            terminal.SetColor("gray");
            terminal.WriteLine($"    {Loc.Get(spec.DescriptionKey)}");

            // Stat growth bonuses
            var bonuses = new List<string>();
            if (spec.BonusStrength > 0) bonuses.Add($"+{spec.BonusStrength} {Loc.Get("stats.str")}");
            if (spec.BonusConstitution > 0) bonuses.Add($"+{spec.BonusConstitution} {Loc.Get("stats.con")}");
            if (spec.BonusMaxHP > 0) bonuses.Add($"+{spec.BonusMaxHP} {Loc.Get("ui.stat_hp")}");
            if (spec.BonusDefence > 0) bonuses.Add($"+{spec.BonusDefence} {Loc.Get("stats.def")}");
            if (spec.BonusIntelligence > 0) bonuses.Add($"+{spec.BonusIntelligence} {Loc.Get("stats.int")}");
            if (spec.BonusWisdom > 0) bonuses.Add($"+{spec.BonusWisdom} {Loc.Get("stats.wis")}");
            if (spec.BonusCharisma > 0) bonuses.Add($"+{spec.BonusCharisma} {Loc.Get("stats.cha")}");
            if (spec.BonusMaxMana > 0) bonuses.Add($"+{spec.BonusMaxMana} {Loc.Get("ui.stat_mana")}");
            if (spec.BonusDexterity > 0) bonuses.Add($"+{spec.BonusDexterity} {Loc.Get("stats.dex")}");
            if (spec.BonusAgility > 0) bonuses.Add($"+{spec.BonusAgility} {Loc.Get("stats.agi")}");
            if (spec.BonusStamina > 0) bonuses.Add($"+{spec.BonusStamina} {Loc.Get("stats.sta")}");

            if (bonuses.Count > 0)
            {
                terminal.SetColor("bright_green");
                terminal.WriteLine($"    {Loc.Get("spec.per_level")}: {string.Join(", ", bonuses)}");
            }

            // Healing threshold info for healers
            if (spec.Role == UsurperRemake.Data.SpecRole.Healer)
            {
                terminal.SetColor("bright_magenta");
                terminal.WriteLine($"    {Loc.Get("spec.heal_threshold", (int)(spec.HealThreshold * 100))}");
            }

            terminal.WriteLine("");
        }

        terminal.SetColor("yellow");
        string choice = await terminal.GetInput(Loc.Get("spec.choose_option"));
        if (string.IsNullOrWhiteSpace(choice)) return;

        if (!int.TryParse(choice, out int specIndex))
        {
            terminal.SetColor("red");
            terminal.WriteLine(Loc.Get("spec.invalid_choice"));
            await Task.Delay(1500);
            return;
        }

        ClassSpecialization newSpec;
        if (specIndex == 0)
        {
            newSpec = ClassSpecialization.None;
        }
        else if (specIndex >= 1 && specIndex <= specs.Count)
        {
            newSpec = specs[specIndex - 1].Spec;
        }
        else
        {
            terminal.SetColor("red");
            terminal.WriteLine(Loc.Get("spec.invalid_choice"));
            await Task.Delay(1500);
            return;
        }

        // Apply specialization. v1.1.12: to the live NPC, looked up again by ID; a world_state reload while
        // the menu was up replaced the object listed, and the change was lost
        var live = LiveTeamNpc(npc);
        if (live == null || live.Team != currentPlayer.Team)
        {
            terminal.SetColor("red");
            terminal.WriteLine(Loc.Get("team.member_gone_now", npc.DisplayName));
            await Task.Delay(2000);
            return;
        }
        npc = live;
        npc.Specialization = newSpec;

        terminal.WriteLine("");
        if (newSpec == ClassSpecialization.None)
        {
            terminal.SetColor("bright_yellow");
            terminal.WriteLine(Loc.Get("spec.removed", npc.DisplayName));
        }
        else
        {
            var specDef = UsurperRemake.Data.SpecializationData.GetSpec(newSpec);
            terminal.SetColor("bright_green");
            terminal.WriteLine(Loc.Get("spec.set", npc.DisplayName, specDef?.Name ?? newSpec.ToString(), specDef != null ? SpecRoleName(specDef.Role) : ""));
        }

        terminal.SetColor("gray");
        terminal.WriteLine(Loc.Get("spec.future_levels_note"));

        // Save state
        if (DoorMode.IsOnlineMode && OnlineStateManager.Instance != null)
        {
            try { await OnlineStateManager.Instance.SaveAllSharedState(); }
            catch (Exception ex) { DebugLogger.Instance.LogError("SPEC", $"SaveAllSharedState failed after spec change: {ex.Message}"); }
        }

        terminal.WriteLine("");
        await terminal.PressAnyKey();
    }
}
