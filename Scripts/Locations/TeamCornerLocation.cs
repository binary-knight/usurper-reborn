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
            // Message gated on online-mode; Resurrect always shown when in-team.
            if (showMessageBbs)
            {
                ShowBBSMenuRow(("M", "bright_yellow", Loc.Get("team.bbs_message")), ("U", "bright_yellow", Loc.Get("team.bbs_resurrect")));
            }
            else
            {
                ShowBBSMenuRow(("U", "bright_yellow", Loc.Get("team.bbs_resurrect")));
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
        terminal.ClearScreen();
        WriteBoxHeader(Loc.Get("team_corner.rankings_header"), "bright_magenta");
        terminal.WriteLine("");

        // Get all teams from NPCs, then merge in the player's team
        var allNPCs = NPCSpawnSystem.Instance.ActiveNPCs;
        var teamGroups = allNPCs
            .Where(n => !string.IsNullOrEmpty(n.Team) && n.IsAlive)
            .GroupBy(n => n.Team)
            .Select(g => new
            {
                TeamName = g.Key,
                MemberCount = g.Count(),
                TotalPower = (long)g.Sum(m => m.Level + (int)m.Strength + (int)m.Defence),
                AverageLevel = (int)g.Average(m => m.Level),
                ControlsTurf = g.Any(m => m.CTurf),
                IsPlayerTeam = false
            })
            .ToList();

        // Merge the player into the team list
        if (!string.IsNullOrEmpty(currentPlayer.Team))
        {
            long playerPower = currentPlayer.Level + (long)currentPlayer.Strength + (long)currentPlayer.Defence;
            var existingTeam = teamGroups.FirstOrDefault(t => t.TeamName == currentPlayer.Team);
            if (existingTeam != null)
            {
                // Player's team has NPC members too - add the player's stats
                teamGroups.Remove(existingTeam);
                int totalMembers = existingTeam.MemberCount + 1;
                long totalPower = existingTeam.TotalPower + playerPower;
                int totalLevels = existingTeam.AverageLevel * existingTeam.MemberCount + currentPlayer.Level;
                teamGroups.Add(new
                {
                    TeamName = existingTeam.TeamName,
                    MemberCount = totalMembers,
                    TotalPower = totalPower,
                    AverageLevel = totalLevels / totalMembers,
                    ControlsTurf = existingTeam.ControlsTurf || currentPlayer.CTurf,
                    IsPlayerTeam = true
                });
            }
            else
            {
                // Player-only team (no NPC members)
                teamGroups.Add(new
                {
                    TeamName = currentPlayer.Team,
                    MemberCount = 1,
                    TotalPower = playerPower,
                    AverageLevel = currentPlayer.Level,
                    ControlsTurf = currentPlayer.CTurf,
                    IsPlayerTeam = true
                });
            }
        }

        // Online mode: merge player teams from database
        if (DoorMode.IsOnlineMode)
        {
            var backend = SaveSystem.Instance.Backend as SqlSaveBackend;
            if (backend != null)
            {
                var playerTeams = await backend.GetPlayerTeams();
                foreach (var pt in playerTeams)
                {
                    // Skip if this team is already in the list (NPC team or player's own team)
                    if (teamGroups.Any(t => t.TeamName == pt.TeamName))
                        continue;

                    teamGroups.Add(new
                    {
                        TeamName = pt.TeamName,
                        MemberCount = pt.MemberCount,
                        TotalPower = (long)(pt.MemberCount * 50), // Estimate power from member count
                        AverageLevel = 0,
                        ControlsTurf = pt.ControlsTurf,
                        IsPlayerTeam = false
                    });
                }
            }
        }

        // Sort by power descending
        teamGroups = teamGroups.OrderByDescending(t => t.TotalPower).ToList();

        if (teamGroups.Count == 0)
        {
            terminal.SetColor("yellow");
            terminal.WriteLine(Loc.Get("team.no_teams_yet"));
            terminal.WriteLine(Loc.Get("team.be_first"));
        }
        else
        {
            terminal.SetColor("white");
            terminal.WriteLine($"{Loc.Get("team.rank_col_rank"),-5} {Loc.Get("team.rank_col_name"),-24} {Loc.Get("team.rank_col_mbrs"),-6} {Loc.Get("team.rank_col_power"),-8} {Loc.Get("team.rank_col_avg_lvl"),-8} {Loc.Get("team.rank_col_turf"),-5}");
            if (!IsScreenReader)
            {
                terminal.SetColor("darkgray");
                terminal.WriteLine(new string('─', 60));
            }

            int rank = 1;
            foreach (var team in teamGroups)
            {
                if (team.ControlsTurf)
                    terminal.SetColor("bright_yellow");
                else if (team.IsPlayerTeam)
                    terminal.SetColor("bright_cyan");
                else
                    terminal.SetColor("white");

                string turfMark = team.ControlsTurf ? "*" : "-";
                string nameDisplay = team.IsPlayerTeam ? $"{team.TeamName} {Loc.Get("team.you_suffix")}" : team.TeamName;
                terminal.WriteLine($"{rank,-5} {nameDisplay,-24} {team.MemberCount,-6} {team.TotalPower,-8} {team.AverageLevel,-8} {turfMark,-5}");
                rank++;
            }

            terminal.WriteLine("");
            terminal.SetColor("bright_yellow");
            terminal.WriteLine(Loc.Get("team.turf_legend"));
        }

        terminal.WriteLine("");
        terminal.SetColor("darkgray");
        terminal.WriteLine(Loc.Get("ui.press_enter"));
        await terminal.ReadKeyAsync();
    }

    /// <summary>
    /// Show info on a specific team
    /// </summary>
    private async Task ShowTeamInfo()
    {
        terminal.WriteLine("");
        terminal.SetColor("cyan");
        terminal.Write(Loc.Get("team.which_team_info"));
        terminal.SetColor("white");
        string teamName = await terminal.ReadLineAsync();

        if (string.IsNullOrEmpty(teamName))
            return;

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
                string className = pm.ClassId >= 0 ? ((CharacterClass)pm.ClassId).ToString() : "?";
                string onlineStatus = pm.IsOnline ? Loc.Get("team.status_online") : Loc.Get("team.status_offline");
                terminal.WriteLine($"{pm.DisplayName,-20} {className,-12} {pm.Level,-5} {"?",-12} {"?",-15} {onlineStatus,-8}");
            }

            // Show NPC members
            foreach (var member in teamMembers)
            {
                string hpDisplay = $"{member.HP}/{member.MaxHP}";
                string location = member.CurrentLocation ?? "Unknown";
                if (location.Length > 14) location = location.Substring(0, 14);

                if (member.IsAlive)
                    terminal.SetColor("white");
                else
                    terminal.SetColor("red");

                string specTag = member.Specialization != ClassSpecialization.None ? $" [{member.Specialization}]" : "";
                string status = member.IsAlive ? Loc.Get("team.status_alive") : (member.IsPermaDead ? Loc.Get("team.status_gone") : Loc.Get("team.status_dead_label"));
                terminal.WriteLine($"{member.DisplayName,-20} {member.Class}{specTag,-12} {member.Level,-5} {hpDisplay,-12} {location,-15} {status,-8}");
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
                string className = pm.ClassId >= 0 ? ((CharacterClass)pm.ClassId).ToString() : "?";
                string onlineTag = pm.IsOnline ? Loc.Get("team.online_tag") : "";
                terminal.SetColor("bright_cyan");
                terminal.WriteLine($"  {pm.DisplayName} - Level {pm.Level} {className}{onlineTag}");
            }

            // Show NPC members
            foreach (var member in teamMembers)
            {
                string specTag = member.Specialization != ClassSpecialization.None ? $" [{member.Specialization}]" : "";
                string status = member.IsAlive ? "" : Loc.Get("team.member_dead_tag");
                terminal.SetColor("white");
                terminal.WriteLine($"  {member.DisplayName} - Level {member.Level} {member.ClassName}{specTag}{status}");
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

        // Get team name
        terminal.SetColor("white");
        terminal.Write(Loc.Get("team.enter_gang_name"));
        string teamName = await terminal.ReadLineAsync();

        if (string.IsNullOrEmpty(teamName) || teamName.Length > 40)
        {
            terminal.SetColor("red");
            terminal.WriteLine(Loc.Get("team.invalid_team_name"));
            await Task.Delay(2000);
            return;
        }

        // Check if team name already exists (NPC teams + player teams)
        var allNPCs = NPCSpawnSystem.Instance.ActiveNPCs;
        if (allNPCs.Any(n => n.Team == teamName))
        {
            terminal.SetColor("red");
            terminal.WriteLine(Loc.Get("team.team_name_exists"));
            await Task.Delay(2000);
            return;
        }

        // Online mode: also check player_teams table
        if (DoorMode.IsOnlineMode)
        {
            var backend = SaveSystem.Instance.Backend as SqlSaveBackend;
            if (backend != null && backend.IsTeamNameTaken(teamName))
            {
                terminal.SetColor("red");
                terminal.WriteLine(Loc.Get("team.player_team_exists"));
                await Task.Delay(2000);
                return;
            }
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

        // Deduct the creation cost
        currentPlayer.Gold -= creationCost;

        // Create team
        currentPlayer.Team = teamName;
        currentPlayer.TeamPW = password;
        currentPlayer.CTurf = false;
        currentPlayer.TeamRec = 0;

        // Register so WorldSimulator protects this team from NPC AI
        WorldSimulator.RegisterPlayerTeam(teamName);

        // Online mode: register in player_teams table
        if (DoorMode.IsOnlineMode)
        {
            var backend = SaveSystem.Instance.Backend as SqlSaveBackend;
            if (backend != null)
            {
                string hashedPW = SqlSaveBackend.HashTeamPassword(password);
                string username = currentPlayer.DisplayName.ToLower();
                await backend.CreatePlayerTeam(teamName, hashedPW, username);
            }
        }

        terminal.WriteLine("");
        terminal.SetColor("bright_green");
        terminal.WriteLine(Loc.Get("team.gang_created", teamName));
        terminal.WriteLine(Loc.Get("team.now_leader", teamName));
        terminal.SetColor("yellow");
        terminal.WriteLine(Loc.Get("team.paid_registration", $"{creationCost:N0}"));
        terminal.WriteLine("");

        // Generate news
        NewsSystem.Instance.Newsy(true, $"{currentPlayer.DisplayName} formed a new team: '{teamName}'!");
        if (DoorMode.IsOnlineMode)
            UsurperRemake.Systems.OnlineStateManager.Instance?.AddNews(
                $"{currentPlayer.DisplayName} formed a new team: '{teamName}'!", "team");

        terminal.SetColor("darkgray");
        terminal.WriteLine(Loc.Get("ui.press_enter"));
        await terminal.ReadKeyAsync();
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

        terminal.WriteLine("");
        terminal.SetColor("cyan");
        terminal.Write(Loc.Get("team.which_gang_join"));
        terminal.SetColor("white");
        string teamName = await terminal.ReadLineAsync();

        if (string.IsNullOrEmpty(teamName))
            return;

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

                var (exists, pwCorrect) = await backend.VerifyPlayerTeam(teamName, password);
                if (exists && pwCorrect)
                {
                    currentPlayer.Team = teamName;
                    currentPlayer.TeamPW = password;
                    currentPlayer.CTurf = false;

                    WorldSimulator.RegisterPlayerTeam(teamName);
                    await backend.UpdatePlayerTeamMemberCount(teamName);

                    terminal.WriteLine("");
                    terminal.SetColor("bright_green");
                    terminal.WriteLine(Loc.Get("team.joined_team", teamName));
                    terminal.WriteLine("");

                    NewsSystem.Instance.Newsy(true, $"{currentPlayer.DisplayName} joined the team '{teamName}'!");
                    if (DoorMode.IsOnlineMode)
                        UsurperRemake.Systems.OnlineStateManager.Instance?.AddNews(
                            $"{currentPlayer.DisplayName} joined the team '{teamName}'!", "team");

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
            currentPlayer.Team = teamName;
            currentPlayer.TeamPW = npcPassword;
            currentPlayer.CTurf = teamMember.CTurf;

            WorldSimulator.RegisterPlayerTeam(teamName);

            terminal.WriteLine("");
            terminal.SetColor("bright_green");
            terminal.WriteLine(Loc.Get("team.joined_team", teamName));
            terminal.WriteLine("");

            NewsSystem.Instance.Newsy(true, $"{currentPlayer.DisplayName} joined the team '{teamName}'!");
            if (DoorMode.IsOnlineMode)
                UsurperRemake.Systems.OnlineStateManager.Instance?.AddNews(
                    $"{currentPlayer.DisplayName} joined the team '{teamName}'!", "team");

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

        if (response?.ToUpper().StartsWith("Y") == true)
        {
            string oldTeam = currentPlayer.Team;
            currentPlayer.Team = "";
            currentPlayer.TeamPW = "";
            currentPlayer.CTurf = false;
            currentPlayer.TeamRec = 0;

            WorldSimulator.UnregisterPlayerTeam(oldTeam);

            // Online mode: save player data FIRST so the DB reflects the team change,
            // THEN update member count. Without this, the SQL count still finds the
            // quitting player in the team (stale player_data) and never reaches 0.
            if (DoorMode.IsOnlineMode)
            {
                SaveSystem.Instance.ResetAutoSaveThrottle();
                await SaveSystem.Instance.AutoSave(currentPlayer);

                var backend = SaveSystem.Instance.Backend as SqlSaveBackend;
                if (backend != null)
                {
                    await backend.UpdatePlayerTeamMemberCount(oldTeam);

                    // If team is now empty (no players AND no NPCs), delete it
                    var remainingPlayers = await backend.GetPlayerTeamMembers(oldTeam);
                    var remainingNPCs = NPCSpawnSystem.Instance.ActiveNPCs
                        .Count(n => n.Team == oldTeam && !n.IsDead && !n.IsPermaDead);
                    if (remainingPlayers.Count == 0 && remainingNPCs == 0)
                    {
                        await backend.DeletePlayerTeam(oldTeam);
                        DebugLogger.Instance.LogInfo("TEAM", $"Team '{oldTeam}' dissolved — no members remaining");
                    }
                }
            }

            terminal.WriteLine("");
            terminal.SetColor("bright_green");
            terminal.WriteLine(Loc.Get("team.left_team"));
            terminal.WriteLine("");

            NewsSystem.Instance.Newsy(true, $"{currentPlayer.DisplayName} left the team '{oldTeam}'!");
            if (DoorMode.IsOnlineMode)
                UsurperRemake.Systems.OnlineStateManager.Instance?.AddNews(
                    $"{currentPlayer.DisplayName} left the team '{oldTeam}'!", "team");

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
    /// <c>DOCS/RELEASE_NOTES_0.57.11.md</c> for the design rationale.
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

        // Count current team size (include dead members — they still occupy a slot until dismissed or permadead)
        var allNPCs = NPCSpawnSystem.Instance.ActiveNPCs;
        var currentTeamSize = allNPCs.Count(n => n.Team == currentPlayer.Team && !n.IsDead) + 1; // +1 for player

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
                terminal.WriteLine($"  {i + 1}. {disamb[i].DisplayName} ({disamb[i].ClassName}, Lv.{disamb[i].Level})");
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
        if (response != "Y" && response != "YES")
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

        NewsSystem.Instance.Newsy(true, $"{currentPlayer.DisplayName} recruited {recruit.DisplayName} into team '{currentPlayer.Team}'!");

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

        terminal.WriteLine("");
        terminal.SetColor("cyan");
        terminal.WriteLine(Loc.Get("team.examine_prompt"));
        terminal.Write(": ");
        terminal.SetColor("white");
        string memberName = await terminal.ReadLineAsync();

        if (memberName == "?")
        {
            await ShowTeamMembers(currentPlayer.Team, true);
            terminal.SetColor("darkgray");
            terminal.WriteLine(Loc.Get("ui.press_enter"));
            await terminal.ReadKeyAsync();
            return;
        }

        // Find the member
        var allNPCs = NPCSpawnSystem.Instance.ActiveNPCs;
        var member = allNPCs.FirstOrDefault(n =>
            n.Team == currentPlayer.Team &&
            n.DisplayName.Equals(memberName, StringComparison.OrdinalIgnoreCase));

        if (member == null)
        {
            terminal.SetColor("red");
            terminal.WriteLine(Loc.Get("team.member_not_found", memberName));
            await Task.Delay(2000);
            return;
        }

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

        // v0.64.0 Brain v2 Slice 9b: First Impression block. When LLM is
        // configured, generates a 2-3 sentence character impression keyed
        // to the NPC's personality + archetype + class. Cached on the NPC
        // after first generation so subsequent examines are free. When LLM
        // is disabled or fails, falls back to a templated string driven by
        // the NPC's strongest personality traits. The Task is awaited
        // because the player is waiting on the UI; bounded by the LLM
        // timeout (default 3s).
        try
        {
            string impression = await UsurperRemake.Systems.LLMMoments
                .GeneratePersonalitySummaryAsync(member, System.Threading.CancellationToken.None);
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
            terminal.WriteLine($"  {Loc.Get("spec.label")}: {specDef?.Name ?? member.Specialization.ToString()} ({specDef?.Role})");
            terminal.SetColor("white");
        }
        terminal.WriteLine($"  {Loc.Get("status.race")}: {member.Race}");
        terminal.WriteLine($"  {Loc.Get("team.examine_sex")}: {member.Sex}");
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
            terminal.WriteLine($"  {Loc.Get("team.examine_faction")}: {member.NPCFaction.Value}");
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
        terminal.WriteLine($"  STR: {member.Strength}  DEX: {member.Dexterity}  AGI: {member.Agility}  CON: {member.Constitution}");
        terminal.WriteLine($"  INT: {member.Intelligence}  WIS: {member.Wisdom}  CHA: {member.Charisma}  DEF: {member.Defence}");
        terminal.WriteLine($"  STA: {member.Stamina}  WeapPow: {member.WeapPow}  ArmPow: {member.ArmPow}");
        terminal.WriteLine("");

        terminal.WriteLine($"{Loc.Get("ui.location")}: {member.CurrentLocation ?? "Unknown"}");
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

        terminal.WriteLine("");
        terminal.SetColor("cyan");
        terminal.Write(Loc.Get("team.enter_current_password"));
        terminal.SetColor("white");
        string currentPassword = await terminal.ReadLineAsync();

        if (currentPassword != currentPlayer.TeamPW)
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
            string oldPassword = currentPlayer.TeamPW;
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
            terminal.WriteLine("");
            terminal.SetColor("bright_green");
            terminal.WriteLine(Loc.Get("team.message_sent"));
            terminal.SetColor("white");
            terminal.WriteLine(Loc.Get("team.your_message", message));
            terminal.WriteLine("");

            // Could integrate with mail system here
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

        terminal.WriteLine("");
        terminal.SetColor("cyan");
        terminal.WriteLine(Loc.Get("team.sack_prompt"));
        terminal.Write(": ");
        terminal.SetColor("white");
        string memberName = await terminal.ReadLineAsync();

        if (memberName == "?")
        {
            await ShowTeamMembers(currentPlayer.Team, true);
            terminal.SetColor("darkgray");
            terminal.WriteLine(Loc.Get("ui.press_enter"));
            await terminal.ReadKeyAsync();
            return;
        }

        if (!string.IsNullOrEmpty(memberName))
        {
            var allNPCs = NPCSpawnSystem.Instance.ActiveNPCs;
            var member = allNPCs.FirstOrDefault(n =>
                n.Team == currentPlayer.Team &&
                n.DisplayName.Equals(memberName, StringComparison.OrdinalIgnoreCase));

            if (member == null)
            {
                terminal.SetColor("red");
                terminal.WriteLine(Loc.Get("team.member_not_found", memberName));
                await Task.Delay(2000);
                return;
            }

            terminal.SetColor("yellow");
            terminal.Write(Loc.Get("team.confirm_sack", member.DisplayName));
            string response = await terminal.ReadLineAsync();

            if (response?.ToUpper().StartsWith("Y") == true)
            {
                member.Team = "";
                member.TeamPW = "";
                member.CTurf = false;

                terminal.WriteLine("");
                terminal.SetColor("bright_green");
                terminal.WriteLine(Loc.Get("team.member_sacked", member.DisplayName));
                terminal.WriteLine("");

                NewsSystem.Instance.Newsy(true, $"{member.DisplayName} was kicked out of team '{currentPlayer.Team}'!");

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

                terminal.SetColor("darkgray");
                terminal.WriteLine(Loc.Get("ui.press_enter"));
                await terminal.ReadKeyAsync();
            }
        }
    }

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
                    terminal.WriteLine($"  {pd.DisplayName} — {reason}");
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

        terminal.WriteLine("");
        terminal.SetColor("cyan");
        terminal.WriteLine(Loc.Get("team.dead_members_header"));
        for (int i = 0; i < deadMembers.Count; i++)
        {
            var dead = deadMembers[i];
            long cost = dead.Level * 1000; // Resurrection cost
            terminal.SetColor("white");
            terminal.WriteLine($"{i + 1}. {dead.DisplayName} (Level {dead.Level}) - Cost: {cost:N0} gold");
        }

        terminal.WriteLine("");
        terminal.SetColor("cyan");
        terminal.Write(Loc.Get("team.enter_number_resurrect"));
        terminal.SetColor("white");
        string input = await terminal.ReadLineAsync();

        if (int.TryParse(input, out int choice) && choice >= 1 && choice <= deadMembers.Count)
        {
            var toResurrect = deadMembers[choice - 1];
            long cost = toResurrect.Level * 1000;

            if (currentPlayer.Gold < cost)
            {
                terminal.SetColor("red");
                terminal.WriteLine(Loc.Get("team.need_gold_resurrect", $"{cost:N0}", toResurrect.DisplayName));
            }
            else
            {
                currentPlayer.Gold -= cost;
                toResurrect.HP = toResurrect.MaxHP / 2; // Resurrect at half HP
                toResurrect.IsDead = false; // Clear permanent death flag - IsAlive is computed from HP > 0

                terminal.WriteLine("");
                terminal.SetColor("bright_green");
                terminal.WriteLine(Loc.Get("team.member_resurrected", toResurrect.DisplayName));
                terminal.WriteLine(Loc.Get("team.resurrect_cost", $"{cost:N0}"));

                NewsSystem.Instance.Newsy(true, $"{toResurrect.DisplayName} was resurrected by their team '{currentPlayer.Team}'!");
            }
        }

        terminal.WriteLine("");
        terminal.SetColor("darkgray");
        terminal.WriteLine(Loc.Get("ui.press_enter"));
        await terminal.ReadKeyAsync();
    }

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
            string className = tm.ClassId >= 0 ? ((CharacterClass)tm.ClassId).ToString() : "Unknown";
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

        terminal.ClearScreen();
        WriteBoxHeader(Loc.Get("team_corner.equip_header"), "bright_cyan");
        terminal.WriteLine("");

        // List team members
        terminal.SetColor("white");
        terminal.WriteLine(Loc.Get("team.team_members_label"));
        terminal.WriteLine("");

        for (int i = 0; i < teamMembers.Count; i++)
        {
            var member = teamMembers[i];
            terminal.SetColor("bright_yellow");
            terminal.Write($"  {i + 1}. ");
            terminal.SetColor("white");
            terminal.Write($"{member.DisplayName} ");
            terminal.SetColor("gray");
            terminal.WriteLine($"(Lv {member.Level} {member.ClassName})");
        }

        terminal.WriteLine("");
        terminal.SetColor("cyan");
        terminal.Write(Loc.Get("team.select_member_equip"));
        terminal.SetColor("white");

        var input = await terminal.ReadLineAsync();
        if (!int.TryParse(input, out int memberIdx) || memberIdx < 1 || memberIdx > teamMembers.Count)
        {
            terminal.SetColor("gray");
            terminal.WriteLine(Loc.Get("ui.cancelled"));
            await Task.Delay(1000);
            return;
        }

        var selectedMember = teamMembers[memberIdx - 1];
        await ManageCharacterEquipment(selectedMember);

        // Sync equipment changes to canonical NPC in ActiveNPCs (handles orphaned references)
        CombatEngine.SyncNPCTeammateToActiveNPCs(selectedMember);

        // Auto-save after equipment changes to persist NPC equipment state
        await SaveSystem.Instance.AutoSave(currentPlayer);

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
            terminal.WriteLine(Loc.Get("team.examine_level", target.Level, GameConfig.GetLocalizedClassName(target.Class), target.Race));
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
                terminal.WriteLine("  No items available for this slot.");
                await Task.Delay(2000);
                continue;
            }

            // Step 3: Show current item in slot
            terminal.WriteLine("");
            var currentItem = target.GetEquipment(selectedSlot.Value);
            terminal.SetColor("white");
            terminal.Write($"  Current: ");
            if (currentItem != null)
            {
                terminal.SetColor(currentItem.IsIdentified ? currentItem.GetRarityColor() : "magenta");
                terminal.Write(currentItem.IsIdentified ? currentItem.Name : "Unidentified");
                if (currentItem.IsIdentified) WriteEquipmentStatSummary(currentItem);
                terminal.WriteLine("");
            }
            else
            {
                terminal.SetColor("darkgray");
                terminal.WriteLine("Empty");
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
                terminal.WriteLine("  Must identify the item first.");
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

            // Remove from player
            if (wasEquipped && sourceSlot.HasValue)
            {
                currentPlayer.UnequipSlot(sourceSlot.Value);
                currentPlayer.RecalculateStats();
            }
            else
            {
                // Remove from inventory (find by name)
                // Two-pass match: first try Name+Attack+ArmorClass for precision, then fallback to name-only
                var invItem = currentPlayer.Inventory.FirstOrDefault(i =>
                    i.Name == selectedItem.Name && i.Attack == selectedItem.WeaponPower && i.Armor == selectedItem.ArmorClass)
                    ?? currentPlayer.Inventory.FirstOrDefault(i => i.Name == selectedItem.Name);
                if (invItem != null)
                {
                    currentPlayer.Inventory.Remove(invItem);
                }
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
                terminal.Write(" (CURSED)");
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
    private async Task TakeAllEquipment(Character target)
    {
        terminal.WriteLine("");
        terminal.SetColor("yellow");
        terminal.WriteLine(Loc.Get("team.take_all_confirm", target.DisplayName));
        terminal.Write(Loc.Get("team.take_all_warning"));
        terminal.SetColor("white");

        var confirm = await terminal.ReadLineAsync();
        if (!confirm.ToUpper().StartsWith("Y"))
        {
            terminal.SetColor("gray");
            terminal.WriteLine(Loc.Get("ui.cancelled"));
            await Task.Delay(1000);
            return;
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
        var opponents = allTeams.Where(t => t.TeamName != myTeam && t.MemberCount > 0).ToList();
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
            w.StartedAt > cooldownCutoff &&
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
        if (confirm != "Y") return;

        if (currentPlayer.Gold < wager)
        {
            terminal.SetColor("red");
            terminal.WriteLine(Loc.Get("team.not_enough_gold_wager"));
            await Task.Delay(1500);
            return;
        }

        currentPlayer.Gold -= wager;

        // Load both teams' members for combat
        var myMembers = await backend.GetPlayerTeamMembers(myTeam);
        var enemyMembers = await backend.GetPlayerTeamMembers(enemyTeam.TeamName);

        if (myMembers.Count == 0 || enemyMembers.Count == 0)
        {
            terminal.SetColor("red");
            terminal.WriteLine(Loc.Get("team.no_members_war"));
            currentPlayer.Gold += wager; // refund
            await Task.Delay(1500);
            return;
        }

        int warId = await backend.CreateTeamWar(myTeam, enemyTeam.TeamName, wager);
        if (warId < 0)
        {
            currentPlayer.Gold += wager; // refund
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
            var myData = await backend.ReadGameData(mySummary.DisplayName);
            var enemyData = await backend.ReadGameData(enemySummary.DisplayName);
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
            terminal.Write($"{mySummary.DisplayName} (Lv{mySummary.Level}) vs {enemySummary.DisplayName} (Lv{enemySummary.Level}) ");
            terminal.SetColor(myWin ? "bright_green" : "bright_red");
            terminal.WriteLine(myWin ? Loc.Get("team.fighter_wins", mySummary.DisplayName) : Loc.Get("team.fighter_wins", enemySummary.DisplayName));

            await backend.UpdateTeamWarScore(warId, myWin);
            await Task.Delay(800);
        }

        terminal.WriteLine("");
        bool weWon = myWins > enemyWins;
        string result = weWon ? "challenger_won" : "defender_won";
        await backend.CompleteTeamWar(warId, result);

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
        var wars = await backend.GetTeamWarHistory(myTeam);

        terminal.ClearScreen();
        terminal.WriteLine("");
        WriteSectionHeader(Loc.Get("team_corner.war_history_header"), "bright_red");

        if (wars.Count == 0)
        {
            terminal.SetColor("gray");
            terminal.WriteLine(Loc.Get("team.no_wars_yet"));
        }
        else
        {
            foreach (var war in wars)
            {
                bool weChallenger = war.ChallengerTeam == myTeam;
                string opponent = weChallenger ? war.DefenderTeam : war.ChallengerTeam;
                int ourWins = weChallenger ? war.ChallengerWins : war.DefenderWins;
                int theirWins = weChallenger ? war.DefenderWins : war.ChallengerWins;
                bool weWon = ourWins > theirWins;

                terminal.SetColor(weWon ? "bright_green" : "bright_red");
                terminal.Write($"  {(weWon ? Loc.Get("team.war_win") : Loc.Get("team.war_loss"))} ");
                terminal.SetColor("white");
                terminal.Write(Loc.Get("team.war_vs", opponent));
                terminal.SetColor("gray");
                terminal.WriteLine(Loc.Get("team.war_record", ourWins, theirWins, $"{war.GoldWagered:N0}"));
            }
        }

        await terminal.PressAnyKey();
    }

    // ═══════════════════════════════════════════════════════════════════════════
    // Team Headquarters
    // ═══════════════════════════════════════════════════════════════════════════

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
            long vaultCapacity = 50000 + (vaultLevel * 50000);

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
                long nextCost = def.BaseCost * (level + 1) * (level + 1);

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

        if (currentLevel >= 10)
        {
            terminal.SetColor("yellow");
            terminal.WriteLine(Loc.Get("team.max_level_reached"));
            await Task.Delay(1500);
            return;
        }

        long cost = def.BaseCost * (currentLevel + 1);

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

        if (payChoice == "V")
        {
            if (vaultGold < cost)
            {
                terminal.SetColor("red");
                terminal.WriteLine(Loc.Get("team.vault_not_enough"));
                await Task.Delay(1500);
                return;
            }
            bool withdrawn = await backend.WithdrawFromTeamVault(teamName, cost);
            if (!withdrawn) { terminal.SetColor("red"); terminal.WriteLine(Loc.Get("team.failed_generic")); await Task.Delay(1500); return; }
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
        }
        else return;

        await backend.UpgradeTeamFacility(teamName, key, cost);
        terminal.SetColor("bright_green");
        terminal.WriteLine(Loc.Get("team.facility_upgraded", Loc.Get(def.NameKey), currentLevel + 1));

        // Refresh cached HQ upgrade levels on the player
        currentPlayer.HQArmoryLevel = backend.GetTeamUpgradeLevel(teamName, "armory");
        currentPlayer.HQBarracksLevel = backend.GetTeamUpgradeLevel(teamName, "barracks");
        currentPlayer.HQTrainingLevel = backend.GetTeamUpgradeLevel(teamName, "training");
        currentPlayer.HQInfirmaryLevel = backend.GetTeamUpgradeLevel(teamName, "infirmary");

        await Task.Delay(2000);
    }

    private async Task DepositToVault(SqlSaveBackend backend, string teamName)
    {
        int vaultLevel = backend.GetTeamUpgradeLevel(teamName, "vault");
        long vaultCapacity = 50000 + (vaultLevel * 50000);
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

        currentPlayer.Gold -= amount;
        await backend.DepositToTeamVault(teamName, amount);
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
        bool success = await backend.WithdrawFromTeamVault(teamName, amount);
        if (success)
        {
            currentPlayer.Gold += amount;
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

        // List team members with current spec
        terminal.ClearScreen();
        terminal.SetColor("bright_cyan");
        terminal.WriteLine(Loc.Get("spec.title"));
        terminal.WriteLine("");

        for (int i = 0; i < teamMembers.Count; i++)
        {
            var member = teamMembers[i];
            string specLabel = member.Specialization != ClassSpecialization.None
                ? $"[{member.Specialization}]"
                : Loc.Get("spec.none_label");

            terminal.SetColor("bright_yellow");
            terminal.Write($"  {i + 1}. ");
            terminal.SetColor("white");
            terminal.Write($"{member.DisplayName} ");
            terminal.SetColor("gray");
            terminal.Write($"(Lv {member.Level} {member.ClassName}) ");
            terminal.SetColor("cyan");
            terminal.WriteLine(specLabel);
        }

        terminal.WriteLine("");
        terminal.SetColor("yellow");
        string input = await terminal.GetInput(Loc.Get("spec.choose_member"));
        if (string.IsNullOrWhiteSpace(input)) return;

        if (!int.TryParse(input, out int memberIndex) || memberIndex < 1 || memberIndex > teamMembers.Count)
        {
            terminal.SetColor("red");
            terminal.WriteLine(Loc.Get("spec.invalid_choice"));
            await Task.Delay(1500);
            return;
        }

        var selectedNPC = teamMembers[memberIndex - 1];
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
            ? npc.Specialization.ToString()
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
            terminal.Write($"({spec.Role}) ");
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
            if (spec.BonusStrength > 0) bonuses.Add($"+{spec.BonusStrength} STR");
            if (spec.BonusConstitution > 0) bonuses.Add($"+{spec.BonusConstitution} CON");
            if (spec.BonusMaxHP > 0) bonuses.Add($"+{spec.BonusMaxHP} HP");
            if (spec.BonusDefence > 0) bonuses.Add($"+{spec.BonusDefence} DEF");
            if (spec.BonusIntelligence > 0) bonuses.Add($"+{spec.BonusIntelligence} INT");
            if (spec.BonusWisdom > 0) bonuses.Add($"+{spec.BonusWisdom} WIS");
            if (spec.BonusCharisma > 0) bonuses.Add($"+{spec.BonusCharisma} CHA");
            if (spec.BonusMaxMana > 0) bonuses.Add($"+{spec.BonusMaxMana} Mana");
            if (spec.BonusDexterity > 0) bonuses.Add($"+{spec.BonusDexterity} DEX");
            if (spec.BonusAgility > 0) bonuses.Add($"+{spec.BonusAgility} AGI");
            if (spec.BonusStamina > 0) bonuses.Add($"+{spec.BonusStamina} STA");

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

        // Apply specialization
        var oldSpec = npc.Specialization;
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
            terminal.WriteLine(Loc.Get("spec.set", npc.DisplayName, specDef?.Name ?? newSpec.ToString(), specDef?.Role.ToString() ?? ""));
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
