using UsurperRemake.Utils;
using UsurperRemake.Systems;
using UsurperRemake.BBS;
using Godot;
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
            GameLocation.TheInn  // Can return to the Inn
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
        terminal.ClearScreen();

        // Header
        terminal.SetColor("bright_magenta");
        terminal.WriteLine("╔══════════════════════════════════════════════════════════════════════════════╗");
        terminal.WriteLine("║                         ADVENTURERS TEAM CORNER                             ║");
        terminal.WriteLine("║                    'Where gangs forge their destiny'                        ║");
        terminal.WriteLine("╚══════════════════════════════════════════════════════════════════════════════╝");
        terminal.WriteLine("");

        // Atmospheric description
        terminal.SetColor("white");
        terminal.WriteLine("A smoky back room filled with rough-hewn tables. Team banners hang from the");
        terminal.WriteLine("rafters, and the walls are covered with bounties, challenges, and team records.");
        terminal.WriteLine("");

        // Show player's team status
        if (!string.IsNullOrEmpty(currentPlayer.Team))
        {
            terminal.SetColor("bright_cyan");
            terminal.WriteLine($"Your Team: {currentPlayer.Team}");
            terminal.WriteLine($"Turf Control: {(currentPlayer.CTurf ? "YES - You own this town!" : "No")}");
            terminal.WriteLine("");
        }
        else
        {
            terminal.SetColor("yellow");
            terminal.WriteLine("You are not a member of any team. Create or join one to gain power!");
            terminal.WriteLine("");
        }

        // Menu options
        terminal.SetColor("cyan");
        terminal.WriteLine("Team Information:");
        terminal.SetColor("white");
        WriteMenuOption("T", "Team Rankings", "P", "Password Change");
        WriteMenuOption("I", "Info on Teams", "E", "Examine Member");
        WriteMenuOption("Y", "Your Team Status", "", "");
        terminal.WriteLine("");

        terminal.SetColor("cyan");
        terminal.WriteLine("Team Actions:");
        terminal.SetColor("white");
        WriteMenuOption("C", "Create Team", "J", "Join Team");
        WriteMenuOption("Q", "Quit Team", "A", "Apply for Membership");
        WriteMenuOption("N", "Recruit NPC", "2", "Sack Member");
        WriteMenuOption("G", "Equip Member", "", "");
        terminal.WriteLine("");

        terminal.SetColor("cyan");
        terminal.WriteLine("Communication:");
        terminal.SetColor("white");
        WriteMenuOption("M", "Message Teammates", "!", "Resurrect Teammate");
        if (DoorMode.IsOnlineMode)
        {
            WriteMenuOption("W", "Recruit Player Ally", "", "");
            terminal.WriteLine("");
            terminal.SetColor("cyan");
            terminal.WriteLine("Online Features:");
            terminal.SetColor("white");
            WriteMenuOption("B", "Team Battle (War)", "H", "Team Headquarters");
        }
        terminal.WriteLine("");

        terminal.SetColor("yellow");
        terminal.WriteLine("Navigation:");
        terminal.SetColor("white");
        WriteMenuOption("R", "Return to Inn", "S", "Status");
        terminal.WriteLine("");
    }

    private void WriteMenuOption(string key1, string label1, string key2, string label2)
    {
        if (!string.IsNullOrEmpty(key1))
        {
            terminal.SetColor("darkgray");
            terminal.Write("[");
            terminal.SetColor("bright_cyan");
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
            terminal.SetColor("bright_cyan");
            terminal.Write(key2);
            terminal.SetColor("darkgray");
            terminal.Write("] ");
            terminal.SetColor("white");
            terminal.Write(label2);
        }
        terminal.WriteLine("");
    }

    protected override async Task<bool> ProcessChoice(string choice)
    {
        if (string.IsNullOrWhiteSpace(choice))
            return false;

        var upperChoice = choice.ToUpper().Trim();

        // Handle ! locally first (Resurrect) before global handler claims it for bug report
        if (upperChoice == "!")
        {
            await ResurrectTeammate();
            return false;
        }

        // Handle global quick commands
        var (handled, shouldExit) = await TryProcessGlobalCommand(choice);
        if (handled) return shouldExit;

        switch (upperChoice)
        {
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
                await JoinTeam(); // Apply is same as join for now
                return false;

            case "Q":
                await QuitTeam();
                return false;

            case "N":
                await RecruitNPCToTeam();
                return false;

            case "E":
                await ExamineMember();
                return false;

            case "P":
                await ChangeTeamPassword();
                return false;

            case "M":
                await SendTeamMessage();
                return false;

            case "2":
                await SackMember();
                return false;

            case "G":
                await EquipMember();
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

            case "!":
                await ResurrectTeammate();
                return false;

            case "R":
                await NavigateToLocation(GameLocation.TheInn);
                return true;

            case "S":
                await ShowStatus();
                return false;

            case "?":
                // Menu is already displayed
                return false;

            default:
                terminal.WriteLine("Invalid choice! The gang leader shakes his head.", "red");
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
        terminal.SetColor("bright_magenta");
        terminal.WriteLine("╔══════════════════════════════════════════════════════════════════════════════╗");
        terminal.WriteLine("║                             TEAM RANKINGS                                   ║");
        terminal.WriteLine("╚══════════════════════════════════════════════════════════════════════════════╝");
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
                teamGroups.Add(new
                {
                    TeamName = existingTeam.TeamName,
                    MemberCount = totalMembers,
                    TotalPower = totalPower,
                    AverageLevel = (int)(totalPower / totalMembers),
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
            terminal.WriteLine("No teams have been formed yet.");
            terminal.WriteLine("Be the first to create a team!");
        }
        else
        {
            terminal.SetColor("white");
            terminal.WriteLine($"{"Rank",-5} {"Team Name",-24} {"Mbrs",-6} {"Power",-8} {"Avg Lvl",-8} {"Turf",-5}");
            terminal.SetColor("darkgray");
            terminal.WriteLine(new string('─', 60));

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
                string nameDisplay = team.IsPlayerTeam ? $"{team.TeamName} (you)" : team.TeamName;
                terminal.WriteLine($"{rank,-5} {nameDisplay,-24} {team.MemberCount,-6} {team.TotalPower,-8} {team.AverageLevel,-8} {turfMark,-5}");
                rank++;
            }

            terminal.WriteLine("");
            terminal.SetColor("bright_yellow");
            terminal.WriteLine("* = Controls the town turf");
        }

        terminal.WriteLine("");
        terminal.SetColor("darkgray");
        terminal.WriteLine("Press Enter to continue...");
        await terminal.ReadKeyAsync();
    }

    /// <summary>
    /// Show info on a specific team
    /// </summary>
    private async Task ShowTeamInfo()
    {
        terminal.WriteLine("");
        terminal.SetColor("cyan");
        terminal.Write("Which team do you want info on? ");
        terminal.SetColor("white");
        string teamName = await terminal.ReadLineAsync();

        if (string.IsNullOrEmpty(teamName))
            return;

        terminal.ClearScreen();
        terminal.SetColor("bright_cyan");
        terminal.WriteLine($"Team Information: {teamName}");
        terminal.SetColor("darkgray");
        terminal.WriteLine(new string('═', 50));
        terminal.WriteLine("");

        await ShowTeamMembers(teamName, false);

        terminal.WriteLine("");
        terminal.SetColor("darkgray");
        terminal.WriteLine("Press Enter to continue...");
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
            terminal.WriteLine("You don't belong to a team.");
            terminal.WriteLine("");
            await Task.Delay(2000);
            return;
        }

        terminal.ClearScreen();
        terminal.SetColor("bright_cyan");
        terminal.WriteLine($"Team Status: {currentPlayer.Team}");
        terminal.SetColor("darkgray");
        terminal.WriteLine(new string('═', 50));
        terminal.WriteLine("");

        terminal.SetColor("white");
        terminal.WriteLine($"Team Name: {currentPlayer.Team}");

        if (currentPlayer.CTurf)
        {
            terminal.SetColor("bright_yellow");
            terminal.WriteLine("Town Control: YES - Your team owns this town!");
        }
        else
        {
            terminal.SetColor("red");
            terminal.WriteLine("Town Control: NO");
        }

        terminal.SetColor("white");
        terminal.WriteLine($"Team Record: {currentPlayer.TeamRec} days");
        terminal.WriteLine("");

        await ShowTeamMembers(currentPlayer.Team, true);

        terminal.WriteLine("");
        terminal.SetColor("darkgray");
        terminal.WriteLine("Press Enter to continue...");
        await terminal.ReadKeyAsync();
    }

    /// <summary>
    /// Show members of a team
    /// </summary>
    private async Task ShowTeamMembers(string teamName, bool detailed)
    {
        terminal.SetColor("cyan");
        terminal.WriteLine("Team Members:");
        terminal.SetColor("darkgray");
        terminal.WriteLine("─────────────");

        // Get NPCs in this team
        var allNPCs = NPCSpawnSystem.Instance.ActiveNPCs;
        var teamMembers = allNPCs
            .Where(n => n.Team == teamName)
            .OrderByDescending(n => n.Level)
            .ToList();

        // Online mode: also get player members from database
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

        bool hasMembers = teamMembers.Count > 0 || playerMembers.Count > 0;
        if (!hasMembers)
        {
            terminal.SetColor("yellow");
            terminal.WriteLine($"No other members found in team '{teamName}'.");
            if (currentPlayer.Team == teamName)
            {
                terminal.WriteLine("(You are the only member!)");
            }
            return;
        }

        if (detailed)
        {
            terminal.SetColor("white");
            terminal.WriteLine($"{"Name",-20} {"Class",-12} {"Lvl",-5} {"HP",-12} {"Location",-15} {"Status",-8}");
            terminal.SetColor("darkgray");
            terminal.WriteLine(new string('─', 75));

            // Show player members first
            foreach (var pm in playerMembers)
            {
                terminal.SetColor("bright_cyan");
                string className = pm.ClassId >= 0 ? ((CharacterClass)pm.ClassId).ToString() : "?";
                string onlineStatus = pm.IsOnline ? "Online" : "Offline";
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

                string status = member.IsAlive ? "Alive" : "Dead";
                terminal.WriteLine($"{member.DisplayName,-20} {member.Class,-12} {member.Level,-5} {hpDisplay,-12} {location,-15} {status,-8}");
            }

            terminal.WriteLine("");
            terminal.SetColor("cyan");
            int totalCount = teamMembers.Count + playerMembers.Count;
            terminal.WriteLine($"Total: {totalCount} members ({playerMembers.Count} players, {teamMembers.Count} NPCs)");
        }
        else
        {
            // Show player members
            foreach (var pm in playerMembers)
            {
                string className = pm.ClassId >= 0 ? ((CharacterClass)pm.ClassId).ToString() : "?";
                string onlineTag = pm.IsOnline ? " [ONLINE]" : "";
                terminal.SetColor("bright_cyan");
                terminal.WriteLine($"  {pm.DisplayName} - Level {pm.Level} {className}{onlineTag}");
            }

            // Show NPC members
            foreach (var member in teamMembers)
            {
                string status = member.IsAlive ? "" : " (Dead)";
                terminal.SetColor("white");
                terminal.WriteLine($"  {member.DisplayName} - Level {member.Level} {member.Class}{status}");
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
        if (!string.IsNullOrEmpty(currentPlayer.Team))
        {
            terminal.WriteLine("");
            terminal.SetColor("red");
            terminal.WriteLine($"You are already a member of {currentPlayer.Team}!");
            terminal.WriteLine("You must quit your current team first.");
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
            terminal.WriteLine($"Creating a gang costs {creationCost:N0} gold!");
            terminal.WriteLine($"You only have {currentPlayer.Gold:N0} gold.");
            terminal.WriteLine("");
            await Task.Delay(2000);
            return;
        }

        terminal.WriteLine("");
        terminal.SetColor("cyan");
        terminal.WriteLine("Creating a new gang...");
        terminal.SetColor("yellow");
        terminal.WriteLine($"Registration fee: {creationCost:N0} gold");
        terminal.WriteLine("");

        // Get team name
        terminal.SetColor("white");
        terminal.Write("Enter gang name (max 40 chars): ");
        string teamName = await terminal.ReadLineAsync();

        if (string.IsNullOrEmpty(teamName) || teamName.Length > 40)
        {
            terminal.SetColor("red");
            terminal.WriteLine("Invalid team name!");
            await Task.Delay(2000);
            return;
        }

        // Check if team name already exists (NPC teams + player teams)
        var allNPCs = NPCSpawnSystem.Instance.ActiveNPCs;
        if (allNPCs.Any(n => n.Team == teamName))
        {
            terminal.SetColor("red");
            terminal.WriteLine("A team with that name already exists!");
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
                terminal.WriteLine("A player team with that name already exists!");
                await Task.Delay(2000);
                return;
            }
        }

        // Get password
        terminal.Write("Enter gang password (max 20 chars): ");
        string password = await terminal.ReadLineAsync();

        if (string.IsNullOrEmpty(password) || password.Length > 20)
        {
            terminal.SetColor("red");
            terminal.WriteLine("Invalid password!");
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
        terminal.WriteLine($"Gang '{teamName}' created successfully!");
        terminal.WriteLine($"You are now the leader of {teamName}!");
        terminal.SetColor("yellow");
        terminal.WriteLine($"Paid {creationCost:N0} gold in registration fees.");
        terminal.WriteLine("");

        // Generate news
        NewsSystem.Instance.Newsy(true, $"{currentPlayer.DisplayName} formed a new team: '{teamName}'!");
        if (DoorMode.IsOnlineMode)
            UsurperRemake.Systems.OnlineStateManager.Instance?.AddNews(
                $"{currentPlayer.DisplayName} formed a new team: '{teamName}'!", "team");

        terminal.SetColor("darkgray");
        terminal.WriteLine("Press Enter to continue...");
        await terminal.ReadKeyAsync();
    }

    /// <summary>
    /// Join an existing team
    /// </summary>
    private async Task JoinTeam()
    {
        if (!string.IsNullOrEmpty(currentPlayer.Team))
        {
            terminal.WriteLine("");
            terminal.SetColor("red");
            terminal.WriteLine($"You are already a member of {currentPlayer.Team}!");
            terminal.WriteLine("");
            await Task.Delay(2000);
            return;
        }

        terminal.WriteLine("");
        terminal.SetColor("cyan");
        terminal.Write("Which gang would you like to join? ");
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
                terminal.Write("Enter password: ");
                terminal.SetColor("white");
                string password = await terminal.ReadLineAsync();

                var (exists, pwCorrect) = await backend.VerifyPlayerTeam(teamName, password);
                if (exists && pwCorrect)
                {
                    currentPlayer.Team = teamName;
                    currentPlayer.TeamPW = password;
                    currentPlayer.CTurf = false;

                    await backend.UpdatePlayerTeamMemberCount(teamName);

                    terminal.WriteLine("");
                    terminal.SetColor("bright_green");
                    terminal.WriteLine($"Correct! You are now a member of {teamName}!");
                    terminal.WriteLine("");

                    NewsSystem.Instance.Newsy(true, $"{currentPlayer.DisplayName} joined the team '{teamName}'!");
                    if (DoorMode.IsOnlineMode)
                        UsurperRemake.Systems.OnlineStateManager.Instance?.AddNews(
                            $"{currentPlayer.DisplayName} joined the team '{teamName}'!", "team");

                    terminal.SetColor("darkgray");
                    terminal.WriteLine("Press Enter to continue...");
                    await terminal.ReadKeyAsync();
                    return;
                }
                else if (exists)
                {
                    terminal.WriteLine("");
                    terminal.SetColor("red");
                    terminal.WriteLine("Wrong password! Access denied.");
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
            terminal.WriteLine("No active team found with that name!");
            await Task.Delay(2000);
            return;
        }

        terminal.SetColor("cyan");
        terminal.Write("Enter password: ");
        terminal.SetColor("white");
        string npcPassword = await terminal.ReadLineAsync();

        if (npcPassword == teamMember.TeamPW)
        {
            currentPlayer.Team = teamName;
            currentPlayer.TeamPW = npcPassword;
            currentPlayer.CTurf = teamMember.CTurf;

            terminal.WriteLine("");
            terminal.SetColor("bright_green");
            terminal.WriteLine($"Correct! You are now a member of {teamName}!");
            terminal.WriteLine("");

            NewsSystem.Instance.Newsy(true, $"{currentPlayer.DisplayName} joined the team '{teamName}'!");
            if (DoorMode.IsOnlineMode)
                UsurperRemake.Systems.OnlineStateManager.Instance?.AddNews(
                    $"{currentPlayer.DisplayName} joined the team '{teamName}'!", "team");

            terminal.SetColor("darkgray");
            terminal.WriteLine("Press Enter to continue...");
            await terminal.ReadKeyAsync();
        }
        else
        {
            terminal.WriteLine("");
            terminal.SetColor("red");
            terminal.WriteLine("Wrong password! Access denied.");
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
            terminal.WriteLine("You don't belong to a team!");
            terminal.WriteLine("");
            await Task.Delay(2000);
            return;
        }

        terminal.WriteLine("");
        terminal.SetColor("yellow");
        terminal.Write($"Really quit {currentPlayer.Team}? (Y/N): ");
        string response = await terminal.ReadLineAsync();

        if (response?.ToUpper().StartsWith("Y") == true)
        {
            string oldTeam = currentPlayer.Team;
            currentPlayer.Team = "";
            currentPlayer.TeamPW = "";
            currentPlayer.CTurf = false;
            currentPlayer.TeamRec = 0;

            // Online mode: update member count, delete team if empty
            if (DoorMode.IsOnlineMode)
            {
                var backend = SaveSystem.Instance.Backend as SqlSaveBackend;
                if (backend != null)
                {
                    await backend.UpdatePlayerTeamMemberCount(oldTeam);
                }
            }

            terminal.WriteLine("");
            terminal.SetColor("bright_green");
            terminal.WriteLine("You have left the team!");
            terminal.WriteLine("");

            NewsSystem.Instance.Newsy(true, $"{currentPlayer.DisplayName} left the team '{oldTeam}'!");
            if (DoorMode.IsOnlineMode)
                UsurperRemake.Systems.OnlineStateManager.Instance?.AddNews(
                    $"{currentPlayer.DisplayName} left the team '{oldTeam}'!", "team");

            terminal.SetColor("darkgray");
            terminal.WriteLine("Press Enter to continue...");
            await terminal.ReadKeyAsync();
        }
    }

    /// <summary>
    /// Recruit an NPC to join your team
    /// </summary>
    private async Task RecruitNPCToTeam()
    {
        if (string.IsNullOrEmpty(currentPlayer.Team))
        {
            terminal.WriteLine("");
            terminal.SetColor("red");
            terminal.WriteLine("You must be in a team to recruit members!");
            terminal.WriteLine("Create a team first with the (C)reate team option.");
            terminal.WriteLine("");
            await Task.Delay(2000);
            return;
        }

        // Count current team size
        var allNPCs = NPCSpawnSystem.Instance.ActiveNPCs;
        var currentTeamSize = allNPCs.Count(n => n.Team == currentPlayer.Team && n.IsAlive) + 1; // +1 for player

        if (currentTeamSize >= MaxTeamSize)
        {
            terminal.WriteLine("");
            terminal.SetColor("red");
            terminal.WriteLine($"Your team is full! (Max {MaxTeamSize} members)");
            terminal.WriteLine("");
            await Task.Delay(2000);
            return;
        }

        terminal.ClearScreen();
        terminal.SetColor("bright_magenta");
        terminal.WriteLine("╔══════════════════════════════════════════════════════════════════════════════╗");
        terminal.WriteLine("║                             NPC RECRUITMENT                                 ║");
        terminal.WriteLine("╚══════════════════════════════════════════════════════════════════════════════╝");
        terminal.WriteLine("");

        terminal.SetColor("white");
        terminal.WriteLine($"Team: {currentPlayer.Team}");
        terminal.WriteLine($"Current Size: {currentTeamSize}/{MaxTeamSize}");
        terminal.WriteLine("");

        // Find NPCs that are not in any team and are in town locations
        var townLocations = new[] { "Main Street", "Market", "Inn", "Temple", "Church", "Weapon Shop", "Armor Shop", "Castle", "Bank", "Team Corner" };
        var availableNPCs = allNPCs
            .Where(n => n.IsAlive &&
                   string.IsNullOrEmpty(n.Team) &&
                   townLocations.Contains(n.CurrentLocation))
            .OrderByDescending(n => n.Level)
            .Take(10)
            .ToList();

        if (availableNPCs.Count == 0)
        {
            terminal.SetColor("yellow");
            terminal.WriteLine("No NPCs available for recruitment right now.");
            terminal.WriteLine("Try again later - NPCs move around the world!");
            terminal.WriteLine("");
            terminal.SetColor("darkgray");
            terminal.WriteLine("Press Enter to continue...");
            await terminal.ReadKeyAsync();
            return;
        }

        terminal.SetColor("cyan");
        terminal.WriteLine("Available Recruits:");
        terminal.SetColor("white");
        terminal.WriteLine($"{"#",-3} {"Name",-18} {"Class",-12} {"Lvl",-5} {"Location",-14} {"Cost",-10}");
        terminal.SetColor("darkgray");
        terminal.WriteLine(new string('─', 65));

        terminal.SetColor("white");
        for (int i = 0; i < availableNPCs.Count; i++)
        {
            var npc = availableNPCs[i];
            long recruitCost = CalculateRecruitmentCost(npc, currentPlayer);
            string location = npc.CurrentLocation ?? "Unknown";
            if (location.Length > 13) location = location.Substring(0, 13);

            terminal.WriteLine($"{i + 1,-3} {npc.DisplayName,-18} {npc.Class,-12} {npc.Level,-5} {location,-14} {recruitCost:N0}g");
        }

        terminal.WriteLine("");
        terminal.SetColor("bright_yellow");
        terminal.WriteLine($"Your Gold: {currentPlayer.Gold:N0}");
        terminal.WriteLine("");
        terminal.SetColor("cyan");
        terminal.Write("Enter number to recruit (0 to cancel): ");
        terminal.SetColor("white");
        string input = await terminal.ReadLineAsync();

        if (int.TryParse(input, out int choice) && choice >= 1 && choice <= availableNPCs.Count)
        {
            var recruit = availableNPCs[choice - 1];
            long cost = CalculateRecruitmentCost(recruit, currentPlayer);

            if (currentPlayer.Gold < cost)
            {
                terminal.WriteLine("");
                terminal.SetColor("red");
                terminal.WriteLine($"You don't have enough gold to recruit {recruit.DisplayName}!");
                terminal.WriteLine($"You need {cost:N0} gold, but only have {currentPlayer.Gold:N0}.");
            }
            else
            {
                // Recruitment success!
                currentPlayer.Gold -= cost;
                recruit.Team = currentPlayer.Team;
                recruit.TeamPW = currentPlayer.TeamPW;
                recruit.CTurf = currentPlayer.CTurf;

                terminal.WriteLine("");
                terminal.SetColor("bright_green");
                terminal.WriteLine($"{recruit.DisplayName} has joined your team!");
                terminal.WriteLine($"You paid {cost:N0} gold for recruitment.");
                terminal.WriteLine("");
                terminal.SetColor("bright_cyan");
                terminal.WriteLine($"\"{recruit.DisplayName} says: 'I'll fight alongside you, boss!'\"");

                NewsSystem.Instance.Newsy(true, $"{currentPlayer.DisplayName} recruited {recruit.DisplayName} into team '{currentPlayer.Team}'!");
            }
        }
        else if (choice != 0 && !string.IsNullOrEmpty(input))
        {
            terminal.SetColor("red");
            terminal.WriteLine("Invalid choice.");
        }

        terminal.WriteLine("");
        terminal.SetColor("darkgray");
        terminal.WriteLine("Press Enter to continue...");
        await terminal.ReadKeyAsync();
    }

    /// <summary>
    /// Calculate the cost to recruit an NPC
    /// </summary>
    private long CalculateRecruitmentCost(NPC npc, Character recruiter)
    {
        long baseCost = npc.Level * 500;
        baseCost += ((long)npc.Strength + (long)npc.Defence + (long)npc.Agility) * 20;

        if (npc.Level > recruiter.Level)
            baseCost = (long)(baseCost * 1.5);

        if (npc.Level < recruiter.Level - 5)
            baseCost = (long)(baseCost * 0.7);

        return Math.Max(100, baseCost);
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
            terminal.WriteLine("You don't belong to a team.");
            terminal.WriteLine("");
            await Task.Delay(2000);
            return;
        }

        terminal.WriteLine("");
        terminal.SetColor("cyan");
        terminal.WriteLine("Examine which team member? (enter ? to see your team)");
        terminal.Write(": ");
        terminal.SetColor("white");
        string memberName = await terminal.ReadLineAsync();

        if (memberName == "?")
        {
            await ShowTeamMembers(currentPlayer.Team, true);
            terminal.SetColor("darkgray");
            terminal.WriteLine("Press Enter to continue...");
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
            terminal.WriteLine($"No team member named '{memberName}' found.");
            await Task.Delay(2000);
            return;
        }

        // Show detailed stats
        terminal.ClearScreen();
        terminal.SetColor("bright_cyan");
        terminal.WriteLine($"═══════════════════════════════════════");
        terminal.WriteLine($"        {member.DisplayName.ToUpper()}");
        terminal.WriteLine($"═══════════════════════════════════════");
        terminal.WriteLine("");

        terminal.SetColor("white");
        terminal.WriteLine($"Class: {member.Class}");
        terminal.WriteLine($"Race: {member.Race}");
        terminal.WriteLine($"Level: {member.Level}");

        if (member.IsAlive)
        {
            terminal.SetColor("bright_green");
            terminal.WriteLine("Status: Alive");
        }
        else
        {
            terminal.SetColor("red");
            terminal.WriteLine("Status: Dead");
        }
        terminal.WriteLine("");

        terminal.SetColor("white");
        terminal.WriteLine($"HP: {member.HP}/{member.MaxHP}");
        terminal.WriteLine($"Mana: {member.Mana}/{member.MaxMana}");
        terminal.WriteLine($"Gold: {member.Gold:N0}");
        terminal.WriteLine("");

        terminal.WriteLine($"Strength: {member.Strength,-6} Defence: {member.Defence,-6}");
        terminal.WriteLine($"Agility:  {member.Agility,-6} Stamina: {member.Stamina,-6}");
        terminal.WriteLine($"Weapon Power: {member.WeapPow,-6} Armor Power: {member.ArmPow,-6}");
        terminal.WriteLine("");

        terminal.WriteLine($"Location: {member.CurrentLocation ?? "Unknown"}");
        terminal.WriteLine("");

        terminal.SetColor("darkgray");
        terminal.WriteLine("Press Enter to continue...");
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
            terminal.WriteLine("You don't belong to a team.");
            terminal.WriteLine("");
            await Task.Delay(2000);
            return;
        }

        terminal.WriteLine("");
        terminal.SetColor("cyan");
        terminal.Write("Enter current password: ");
        terminal.SetColor("white");
        string currentPassword = await terminal.ReadLineAsync();

        if (currentPassword != currentPlayer.TeamPW)
        {
            terminal.WriteLine("");
            terminal.SetColor("red");
            terminal.WriteLine("Wrong password!");
            terminal.WriteLine("");
            await Task.Delay(2000);
            return;
        }

        terminal.SetColor("cyan");
        terminal.Write("Enter new password: ");
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
            terminal.WriteLine("Password changed successfully!");
            terminal.WriteLine("");
        }
        else
        {
            terminal.SetColor("red");
            terminal.WriteLine("Invalid password!");
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
            terminal.WriteLine("You don't belong to a team.");
            terminal.WriteLine("");
            await Task.Delay(2000);
            return;
        }

        terminal.WriteLine("");
        terminal.SetColor("cyan");
        terminal.WriteLine("Message to team members:");
        terminal.Write(": ");
        terminal.SetColor("white");
        string message = await terminal.ReadLineAsync();

        if (!string.IsNullOrEmpty(message))
        {
            terminal.WriteLine("");
            terminal.SetColor("bright_green");
            terminal.WriteLine("Message sent to team!");
            terminal.SetColor("white");
            terminal.WriteLine($"Your message: \"{message}\"");
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
            terminal.WriteLine("You don't belong to a team.");
            terminal.WriteLine("");
            await Task.Delay(2000);
            return;
        }

        terminal.WriteLine("");
        terminal.SetColor("cyan");
        terminal.WriteLine("Who must be SACKED? (enter ? to see your team)");
        terminal.Write(": ");
        terminal.SetColor("white");
        string memberName = await terminal.ReadLineAsync();

        if (memberName == "?")
        {
            await ShowTeamMembers(currentPlayer.Team, true);
            terminal.SetColor("darkgray");
            terminal.WriteLine("Press Enter to continue...");
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
                terminal.WriteLine($"No team member named '{memberName}' found.");
                await Task.Delay(2000);
                return;
            }

            terminal.SetColor("yellow");
            terminal.Write($"Really sack {member.DisplayName}? (Y/N): ");
            string response = await terminal.ReadLineAsync();

            if (response?.ToUpper().StartsWith("Y") == true)
            {
                member.Team = "";
                member.TeamPW = "";
                member.CTurf = false;

                terminal.WriteLine("");
                terminal.SetColor("bright_green");
                terminal.WriteLine($"{member.DisplayName} has been sacked from the team!");
                terminal.WriteLine("");

                NewsSystem.Instance.Newsy(true, $"{member.DisplayName} was kicked out of team '{currentPlayer.Team}'!");

                terminal.SetColor("darkgray");
                terminal.WriteLine("Press Enter to continue...");
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
            terminal.WriteLine("You don't belong to a team.");
            terminal.WriteLine("");
            await Task.Delay(2000);
            return;
        }

        // Find dead team members
        var allNPCs = NPCSpawnSystem.Instance.ActiveNPCs;
        var deadMembers = allNPCs
            .Where(n => n.Team == currentPlayer.Team && (n.IsDead || !n.IsAlive))
            .ToList();

        if (deadMembers.Count == 0)
        {
            terminal.WriteLine("");
            terminal.SetColor("bright_green");
            terminal.WriteLine("All your team members are alive!");
            terminal.WriteLine("");
            await Task.Delay(2000);
            return;
        }

        terminal.WriteLine("");
        terminal.SetColor("cyan");
        terminal.WriteLine("Dead Team Members:");
        for (int i = 0; i < deadMembers.Count; i++)
        {
            var dead = deadMembers[i];
            long cost = dead.Level * 1000; // Resurrection cost
            terminal.SetColor("white");
            terminal.WriteLine($"{i + 1}. {dead.DisplayName} (Level {dead.Level}) - Cost: {cost:N0} gold");
        }

        terminal.WriteLine("");
        terminal.SetColor("cyan");
        terminal.Write("Enter number to resurrect (0 to cancel): ");
        terminal.SetColor("white");
        string input = await terminal.ReadLineAsync();

        if (int.TryParse(input, out int choice) && choice >= 1 && choice <= deadMembers.Count)
        {
            var toResurrect = deadMembers[choice - 1];
            long cost = toResurrect.Level * 1000;

            if (currentPlayer.Gold < cost)
            {
                terminal.SetColor("red");
                terminal.WriteLine($"You need {cost:N0} gold to resurrect {toResurrect.DisplayName}!");
            }
            else
            {
                currentPlayer.Gold -= cost;
                toResurrect.HP = toResurrect.MaxHP / 2; // Resurrect at half HP
                toResurrect.IsDead = false; // Clear permanent death flag - IsAlive is computed from HP > 0

                terminal.WriteLine("");
                terminal.SetColor("bright_green");
                terminal.WriteLine($"{toResurrect.DisplayName} has been resurrected!");
                terminal.WriteLine($"Cost: {cost:N0} gold");

                NewsSystem.Instance.Newsy(true, $"{toResurrect.DisplayName} was resurrected by their team '{currentPlayer.Team}'!");
            }
        }

        terminal.WriteLine("");
        terminal.SetColor("darkgray");
        terminal.WriteLine("Press Enter to continue...");
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
            terminal.WriteLine("You must be in a team to recruit player allies!");
            terminal.WriteLine("Create or join a team first.");
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
            terminal.WriteLine("No other players found on your team.");
            terminal.WriteLine("Recruit other players to join your team first!");
            terminal.WriteLine("");
            await Task.Delay(2000);
            return;
        }

        terminal.ClearScreen();
        terminal.SetColor("bright_magenta");
        terminal.WriteLine("╔══════════════════════════════════════════════════════════════════════════════╗");
        terminal.WriteLine("║                          RECRUIT PLAYER ALLY                                ║");
        terminal.WriteLine("╚══════════════════════════════════════════════════════════════════════════════╝");
        terminal.WriteLine("");

        terminal.SetColor("white");
        terminal.WriteLine($"Team: {currentPlayer.Team}");
        terminal.WriteLine("");

        terminal.SetColor("cyan");
        terminal.WriteLine("Available Player Allies:");
        terminal.SetColor("white");
        terminal.WriteLine($"{"#",-3} {"Name",-18} {"Class",-12} {"Level",-6} {"Status",-10}");
        terminal.SetColor("darkgray");
        terminal.WriteLine(new string('─', 52));

        terminal.SetColor("white");
        for (int i = 0; i < teammates.Count; i++)
        {
            var tm = teammates[i];
            string className = tm.ClassId >= 0 ? ((CharacterClass)tm.ClassId).ToString() : "Unknown";
            string status = tm.IsOnline ? "Online" : "Offline";
            terminal.WriteLine($"{i + 1,-3} {tm.DisplayName,-18} {className,-12} {tm.Level,-6} {status,-10}");
        }

        terminal.WriteLine("");
        terminal.SetColor("gray");
        terminal.WriteLine("Their echo fights alongside you in the dungeon.");
        terminal.WriteLine("");

        terminal.SetColor("cyan");
        terminal.Write("Select ally (0 to cancel): ");
        terminal.SetColor("white");
        string input = await terminal.ReadLineAsync();

        if (int.TryParse(input, out int choice) && choice >= 1 && choice <= teammates.Count)
        {
            var selected = teammates[choice - 1];

            // Check if already recruited
            var partyNames = GameEngine.Instance?.DungeonPartyPlayerNames ?? new List<string>();
            if (partyNames.Contains(selected.DisplayName, StringComparer.OrdinalIgnoreCase))
            {
                terminal.SetColor("yellow");
                terminal.WriteLine($"{selected.DisplayName}'s echo is already in your party!");
                await Task.Delay(2000);
                return;
            }

            // Add to dungeon party
            var names = new List<string>(partyNames) { selected.DisplayName };
            GameEngine.Instance?.SetDungeonPartyPlayers(names);

            terminal.WriteLine("");
            terminal.SetColor("bright_cyan");
            terminal.WriteLine($"{selected.DisplayName}'s echo will join your next dungeon run!");
            terminal.SetColor("gray");
            terminal.WriteLine("They'll fight as AI-controlled allies with their real stats.");
        }

        terminal.WriteLine("");
        terminal.SetColor("darkgray");
        terminal.WriteLine("Press Enter to continue...");
        await terminal.ReadKeyAsync();
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
            terminal.WriteLine("You don't belong to a team.");
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
            terminal.WriteLine("Your team has no living NPC members to equip.");
            await Task.Delay(2000);
            return;
        }

        terminal.ClearScreen();
        terminal.SetColor("bright_cyan");
        terminal.WriteLine("╔══════════════════════════════════════════════════════════════════════════════╗");
        terminal.WriteLine("║                           EQUIP TEAM MEMBER                                 ║");
        terminal.WriteLine("╚══════════════════════════════════════════════════════════════════════════════╝");
        terminal.WriteLine("");

        // List team members
        terminal.SetColor("white");
        terminal.WriteLine("Team Members:");
        terminal.WriteLine("");

        for (int i = 0; i < teamMembers.Count; i++)
        {
            var member = teamMembers[i];
            terminal.SetColor("bright_yellow");
            terminal.Write($"  {i + 1}. ");
            terminal.SetColor("white");
            terminal.Write($"{member.DisplayName} ");
            terminal.SetColor("gray");
            terminal.WriteLine($"(Lv {member.Level} {member.Class})");
        }

        terminal.WriteLine("");
        terminal.SetColor("cyan");
        terminal.Write("Select member to equip (0 to cancel): ");
        terminal.SetColor("white");

        var input = await terminal.ReadLineAsync();
        if (!int.TryParse(input, out int memberIdx) || memberIdx < 1 || memberIdx > teamMembers.Count)
        {
            terminal.SetColor("gray");
            terminal.WriteLine("Cancelled.");
            await Task.Delay(1000);
            return;
        }

        var selectedMember = teamMembers[memberIdx - 1];
        await ManageCharacterEquipment(selectedMember);

        // Auto-save after equipment changes to persist NPC equipment state
        await SaveSystem.Instance.AutoSave(currentPlayer);
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
            terminal.SetColor("bright_cyan");
            terminal.WriteLine($"═══════════════════════════════════════════════════════════════════════════════");
            terminal.WriteLine($"                    EQUIPMENT: {target.DisplayName.ToUpper()}");
            terminal.WriteLine($"═══════════════════════════════════════════════════════════════════════════════");
            terminal.WriteLine("");

            // Show target's stats
            terminal.SetColor("white");
            terminal.WriteLine($"  Level: {target.Level}  Class: {target.Class}  Race: {target.Race}");
            terminal.WriteLine($"  HP: {target.HP}/{target.MaxHP}  Mana: {target.Mana}/{target.MaxMana}");
            terminal.WriteLine($"  Str: {target.Strength}  Def: {target.Defence}  Agi: {target.Agility}");
            terminal.WriteLine("");

            // Show current equipment
            terminal.SetColor("bright_yellow");
            terminal.WriteLine("Current Equipment:");
            terminal.SetColor("white");

            DisplayEquipmentSlot(target, EquipmentSlot.MainHand, "Main Hand");
            DisplayEquipmentSlot(target, EquipmentSlot.OffHand, "Off Hand");
            DisplayEquipmentSlot(target, EquipmentSlot.Head, "Head");
            DisplayEquipmentSlot(target, EquipmentSlot.Body, "Body");
            DisplayEquipmentSlot(target, EquipmentSlot.Arms, "Arms");
            DisplayEquipmentSlot(target, EquipmentSlot.Hands, "Hands");
            DisplayEquipmentSlot(target, EquipmentSlot.Legs, "Legs");
            DisplayEquipmentSlot(target, EquipmentSlot.Feet, "Feet");
            DisplayEquipmentSlot(target, EquipmentSlot.Cloak, "Cloak");
            DisplayEquipmentSlot(target, EquipmentSlot.Neck, "Neck");
            DisplayEquipmentSlot(target, EquipmentSlot.LFinger, "Left Ring");
            DisplayEquipmentSlot(target, EquipmentSlot.RFinger, "Right Ring");
            terminal.WriteLine("");

            // Show options
            terminal.SetColor("cyan");
            terminal.WriteLine("Options:");
            terminal.SetColor("white");
            terminal.WriteLine("  [E] Equip item from your inventory");
            terminal.WriteLine("  [U] Unequip item from them");
            terminal.WriteLine("  [T] Take all their equipment");
            terminal.WriteLine("  [Q] Done / Return");
            terminal.WriteLine("");

            terminal.SetColor("cyan");
            terminal.Write("Choice: ");
            terminal.SetColor("white");

            var choice = (await terminal.ReadLineAsync()).ToUpper().Trim();

            switch (choice)
            {
                case "E":
                    await EquipItemToCharacter(target);
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
    /// Display an equipment slot with its current item
    /// </summary>
    private void DisplayEquipmentSlot(Character target, EquipmentSlot slot, string label)
    {
        var item = target.GetEquipment(slot);
        terminal.SetColor("gray");
        terminal.Write($"  {label,-12}: ");
        if (item != null)
        {
            terminal.SetColor("bright_green");
            terminal.WriteLine(item.Name);
        }
        else
        {
            terminal.SetColor("darkgray");
            terminal.WriteLine("(empty)");
        }
    }

    /// <summary>
    /// Equip an item from the player's inventory to a character
    /// </summary>
    private async Task EquipItemToCharacter(Character target)
    {
        terminal.ClearScreen();
        terminal.SetColor("bright_cyan");
        terminal.WriteLine($"═══ EQUIP ITEM TO {target.DisplayName.ToUpper()} ═══");
        terminal.WriteLine("");

        // Collect equippable items from player's inventory and equipped items
        var equipmentItems = new List<(Equipment item, bool isEquipped, EquipmentSlot? fromSlot)>();

        // Add items from player's inventory that are Equipment type
        foreach (var invItem in currentPlayer.Inventory)
        {
            // Try to find matching Equipment in database
            var equipment = EquipmentDatabase.GetByName(invItem.Name);
            if (equipment != null)
            {
                equipmentItems.Add((equipment, false, (EquipmentSlot?)null));
            }
        }

        // Add player's currently equipped items
        foreach (EquipmentSlot slot in Enum.GetValues(typeof(EquipmentSlot)))
        {
            if (slot == EquipmentSlot.None) continue;
            var equipped = currentPlayer.GetEquipment(slot);
            if (equipped != null)
            {
                equipmentItems.Add((equipped, true, slot));
            }
        }

        if (equipmentItems.Count == 0)
        {
            terminal.SetColor("yellow");
            terminal.WriteLine("You have no equipment to give.");
            await Task.Delay(2000);
            return;
        }

        // Display available items
        terminal.SetColor("white");
        terminal.WriteLine("Available equipment:");
        terminal.WriteLine("");

        for (int i = 0; i < equipmentItems.Count; i++)
        {
            var (item, isEquipped, fromSlot) = equipmentItems[i];
            terminal.SetColor("bright_yellow");
            terminal.Write($"  {i + 1}. ");
            terminal.SetColor("white");
            terminal.Write($"{item.Name} ");

            // Show item stats
            terminal.SetColor("gray");
            if (item.WeaponPower > 0)
                terminal.Write($"[Atk:{item.WeaponPower}] ");
            if (item.ArmorClass > 0)
                terminal.Write($"[AC:{item.ArmorClass}] ");
            if (item.ShieldBonus > 0)
                terminal.Write($"[Shield:{item.ShieldBonus}] ");

            // Show if currently equipped by player
            if (isEquipped)
            {
                terminal.SetColor("cyan");
                terminal.Write($"(your {fromSlot?.GetDisplayName()})");
            }

            // Check if target can use it
            if (!item.CanEquip(target, out string reason))
            {
                terminal.SetColor("red");
                terminal.Write($" [{reason}]");
            }

            terminal.WriteLine("");
        }

        terminal.WriteLine("");
        terminal.SetColor("cyan");
        terminal.Write("Select item (0 to cancel): ");
        terminal.SetColor("white");

        var input = await terminal.ReadLineAsync();
        if (!int.TryParse(input, out int itemIdx) || itemIdx < 1 || itemIdx > equipmentItems.Count)
        {
            terminal.SetColor("gray");
            terminal.WriteLine("Cancelled.");
            await Task.Delay(1000);
            return;
        }

        var (selectedItem, wasEquipped, sourceSlot) = equipmentItems[itemIdx - 1];

        // Check if target can equip
        if (!selectedItem.CanEquip(target, out string equipReason))
        {
            terminal.SetColor("red");
            terminal.WriteLine($"{target.DisplayName} cannot use this item: {equipReason}");
            await Task.Delay(2000);
            return;
        }

        // For one-handed weapons, ask which hand
        EquipmentSlot? targetSlot = null;
        if (selectedItem.Handedness == WeaponHandedness.OneHanded &&
            (selectedItem.Slot == EquipmentSlot.MainHand || selectedItem.Slot == EquipmentSlot.OffHand))
        {
            terminal.WriteLine("");
            terminal.SetColor("cyan");
            terminal.WriteLine("Which hand? [M]ain hand or [O]ff hand?");
            terminal.Write(": ");
            terminal.SetColor("white");
            var handChoice = (await terminal.ReadLineAsync()).ToUpper().Trim();
            if (handChoice.StartsWith("O"))
                targetSlot = EquipmentSlot.OffHand;
            else
                targetSlot = EquipmentSlot.MainHand;
        }

        // Remove from player
        if (wasEquipped && sourceSlot.HasValue)
        {
            currentPlayer.UnequipSlot(sourceSlot.Value);
            currentPlayer.RecalculateStats();
        }
        else
        {
            // Remove from inventory (find by name)
            var invItem = currentPlayer.Inventory.FirstOrDefault(i => i.Name == selectedItem.Name);
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

            terminal.WriteLine("");
            terminal.SetColor("bright_green");
            terminal.WriteLine($"{target.DisplayName} equipped {selectedItem.Name}!");
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
            terminal.WriteLine($"Failed to equip: {message}");
        }

        await Task.Delay(2000);
    }

    /// <summary>
    /// Unequip an item from a character and add to player's inventory
    /// </summary>
    private async Task UnequipItemFromCharacter(Character target)
    {
        terminal.ClearScreen();
        terminal.SetColor("bright_cyan");
        terminal.WriteLine($"═══ UNEQUIP FROM {target.DisplayName.ToUpper()} ═══");
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
            terminal.WriteLine($"{target.DisplayName} has no equipment to unequip.");
            await Task.Delay(2000);
            return;
        }

        terminal.SetColor("white");
        terminal.WriteLine("Equipped items:");
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
        terminal.Write("Select slot to unequip (0 to cancel): ");
        terminal.SetColor("white");

        var input = await terminal.ReadLineAsync();
        if (!int.TryParse(input, out int slotIdx) || slotIdx < 1 || slotIdx > equippedSlots.Count)
        {
            terminal.SetColor("gray");
            terminal.WriteLine("Cancelled.");
            await Task.Delay(1000);
            return;
        }

        var (selectedSlot, selectedItem) = equippedSlots[slotIdx - 1];

        // Check if cursed
        if (selectedItem.IsCursed)
        {
            terminal.SetColor("red");
            terminal.WriteLine($"The {selectedItem.Name} is cursed and cannot be removed!");
            await Task.Delay(2000);
            return;
        }

        // Unequip and add to player inventory
        var unequipped = target.UnequipSlot(selectedSlot);
        if (unequipped != null)
        {
            target.RecalculateStats();
            var legacyItem = ConvertEquipmentToItem(unequipped);
            currentPlayer.Inventory.Add(legacyItem);

            terminal.WriteLine("");
            terminal.SetColor("bright_green");
            terminal.WriteLine($"Took {unequipped.Name} from {target.DisplayName}.");
            terminal.SetColor("gray");
            terminal.WriteLine("Item added to your inventory.");
        }
        else
        {
            terminal.SetColor("red");
            terminal.WriteLine("Failed to unequip item.");
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
        terminal.WriteLine($"Take ALL equipment from {target.DisplayName}?");
        terminal.Write("This will leave them with nothing. Confirm (Y/N): ");
        terminal.SetColor("white");

        var confirm = await terminal.ReadLineAsync();
        if (!confirm.ToUpper().StartsWith("Y"))
        {
            terminal.SetColor("gray");
            terminal.WriteLine("Cancelled.");
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

        terminal.WriteLine("");
        if (itemsTaken > 0)
        {
            terminal.SetColor("bright_green");
            terminal.WriteLine($"Took {itemsTaken} item{(itemsTaken != 1 ? "s" : "")} from {target.DisplayName}.");
        }
        else
        {
            terminal.SetColor("yellow");
            terminal.WriteLine($"{target.DisplayName} had no equipment to take.");
        }

        if (cursedItems.Count > 0)
        {
            terminal.SetColor("red");
            terminal.WriteLine($"Could not remove cursed items: {string.Join(", ", cursedItems)}");
        }

        await Task.Delay(2000);
    }

    /// <summary>
    /// Convert Equipment to legacy Item for inventory storage
    /// </summary>
    private Item ConvertEquipmentToItem(Equipment equipment)
    {
        return new Item
        {
            Name = equipment.Name,
            Type = SlotToObjType(equipment.Slot),
            Value = equipment.Value,
            Attack = equipment.WeaponPower,
            Armor = equipment.ArmorClass,
            Strength = equipment.StrengthBonus,
            Dexterity = equipment.DexterityBonus,
            HP = equipment.MaxHPBonus,
            Mana = equipment.MaxManaBonus,
            Defence = equipment.DefenceBonus,
            IsCursed = equipment.IsCursed,
            MinLevel = equipment.MinLevel,
            StrengthNeeded = equipment.StrengthRequired,
            RequiresGood = equipment.RequiresGood,
            RequiresEvil = equipment.RequiresEvil,
            ItemID = equipment.Id
        };
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
            terminal.WriteLine("\n  You must be in a team to wage war!");
            await Task.Delay(2000);
            return;
        }

        while (true)
        {
            terminal.ClearScreen();
            terminal.SetColor("bright_red");
            terminal.WriteLine("╔══════════════════════════════════════════════════════════════════════════════╗");
            terminal.WriteLine("║                            TEAM WARS                                       ║");
            terminal.WriteLine("╚══════════════════════════════════════════════════════════════════════════════╝");
            terminal.SetColor("white");
            terminal.WriteLine($"  Your Team: {currentPlayer.Team}");
            terminal.WriteLine("");

            terminal.SetColor("cyan");
            terminal.WriteLine("  [C] Challenge a Team    [H] War History    [Q] Back");
            terminal.SetColor("white");
            terminal.Write("\n  Choice: ");
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
            terminal.WriteLine("\n  Your team already has an active war!");
            await Task.Delay(2000);
            return;
        }

        // Show available teams to challenge
        var allTeams = await backend.GetPlayerTeams();
        var opponents = allTeams.Where(t => t.TeamName != myTeam && t.MemberCount > 0).ToList();
        if (opponents.Count == 0)
        {
            terminal.SetColor("gray");
            terminal.WriteLine("\n  No other teams to challenge.");
            await Task.Delay(2000);
            return;
        }

        terminal.SetColor("bright_yellow");
        terminal.WriteLine("\n  ═══════════ CHOOSE OPPONENT TEAM ═══════════");
        terminal.SetColor("darkgray");
        terminal.WriteLine($"  {"#",-4} {"Team",-25} {"Members",-10}");
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
        terminal.Write("\n  Challenge team #: ");
        string input = (await terminal.ReadLineAsync())?.Trim() ?? "";
        if (!int.TryParse(input, out int choice) || choice < 1 || choice > opponents.Count) return;

        var enemyTeam = opponents[choice - 1];
        long wager = Math.Max(1000, currentPlayer.Level * 200);

        terminal.SetColor("yellow");
        terminal.WriteLine($"\n  War wager: {wager:N0} gold (winner takes all)");
        terminal.Write("  Confirm war against " + enemyTeam.TeamName + "? (Y/N): ");
        string confirm = (await terminal.ReadLineAsync())?.Trim().ToUpper() ?? "";
        if (confirm != "Y") return;

        if (currentPlayer.Gold < wager)
        {
            terminal.SetColor("red");
            terminal.WriteLine("  Not enough gold for the war wager!");
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
            terminal.WriteLine("  One of the teams has no members!");
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
        terminal.SetColor("bright_red");
        terminal.WriteLine("═══════════════════════════════════════════════════════════════");
        terminal.WriteLine($"        TEAM WAR: {myTeam} vs {enemyTeam.TeamName}");
        terminal.WriteLine("═══════════════════════════════════════════════════════════════");
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
            var rng = new Random();
            myPower = (long)(myPower * (0.8 + rng.NextDouble() * 0.4));
            enemyPower = (long)(enemyPower * (0.8 + rng.NextDouble() * 0.4));

            bool myWin = myPower >= enemyPower;
            if (myWin) myWins++; else enemyWins++;

            terminal.SetColor(myWin ? "bright_green" : "bright_red");
            terminal.Write($"  Round {i + 1}: ");
            terminal.SetColor("white");
            terminal.Write($"{mySummary.DisplayName} (Lv{mySummary.Level}) vs {enemySummary.DisplayName} (Lv{enemySummary.Level}) ");
            terminal.SetColor(myWin ? "bright_green" : "bright_red");
            terminal.WriteLine(myWin ? $"- {mySummary.DisplayName} WINS!" : $"- {enemySummary.DisplayName} WINS!");

            await backend.UpdateTeamWarScore(warId, myWin);
            await Task.Delay(800);
        }

        terminal.WriteLine("");
        bool weWon = myWins > enemyWins;
        string result = weWon ? "challenger_won" : "defender_won";
        await backend.CompleteTeamWar(warId, result);

        if (weWon)
        {
            long reward = wager * 2;
            currentPlayer.Gold += reward;
            terminal.SetColor("bright_green");
            terminal.WriteLine($"  ═══ YOUR TEAM WINS! ═══");
            terminal.SetColor("yellow");
            terminal.WriteLine($"  Score: {myWins} - {enemyWins}");
            terminal.WriteLine($"  War spoils: {reward:N0} gold!");
        }
        else
        {
            terminal.SetColor("bright_red");
            terminal.WriteLine($"  ═══ YOUR TEAM LOSES! ═══");
            terminal.SetColor("yellow");
            terminal.WriteLine($"  Score: {myWins} - {enemyWins}");
            terminal.WriteLine($"  Lost {wager:N0} gold in war wager.");
        }

        if (UsurperRemake.Systems.OnlineStateManager.IsActive)
        {
            string winner = weWon ? myTeam : enemyTeam.TeamName;
            _ = UsurperRemake.Systems.OnlineStateManager.Instance!.AddNews(
                $"Team {winner} won the war between {myTeam} and {enemyTeam.TeamName}! ({myWins}-{enemyWins})", "team_war");
        }

        await terminal.PressAnyKey();
    }

    private async Task ShowWarHistory(SqlSaveBackend backend)
    {
        string myTeam = currentPlayer.Team;
        var wars = await backend.GetTeamWarHistory(myTeam);

        terminal.ClearScreen();
        terminal.SetColor("bright_red");
        terminal.WriteLine("\n  ═══════════ WAR HISTORY ═══════════");

        if (wars.Count == 0)
        {
            terminal.SetColor("gray");
            terminal.WriteLine("  No wars fought yet.");
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
                terminal.Write($"  {(weWon ? "WIN" : "LOSS")} ");
                terminal.SetColor("white");
                terminal.Write($"vs {opponent} ");
                terminal.SetColor("gray");
                terminal.WriteLine($"({ourWins}-{theirWins}) {war.GoldWagered:N0}g");
            }
        }

        await terminal.PressAnyKey();
    }

    // ═══════════════════════════════════════════════════════════════════════════
    // Team Headquarters
    // ═══════════════════════════════════════════════════════════════════════════

    private static readonly Dictionary<string, (string Name, string Description, long BaseCost)> UpgradeDefinitions = new()
    {
        ["armory"]   = ("Armory",          "+5% attack per level",       5000),
        ["barracks"] = ("Barracks",        "+5% defense per level",      5000),
        ["training"] = ("Training Grounds", "+5% XP bonus per level",    8000),
        ["vault"]    = ("Vault",           "+50,000 vault capacity/lv",  3000),
        ["infirmary"]= ("Infirmary",       "+10% healing per level",     4000),
    };

    private async Task TeamHeadquartersMenu()
    {
        var backend = SaveSystem.Instance.Backend as SqlSaveBackend;
        if (backend == null) return;

        if (string.IsNullOrEmpty(currentPlayer.Team))
        {
            terminal.SetColor("red");
            terminal.WriteLine("\n  You must be in a team to access headquarters!");
            await Task.Delay(2000);
            return;
        }

        string teamName = currentPlayer.Team;

        while (true)
        {
            terminal.ClearScreen();
            terminal.SetColor("bright_cyan");
            terminal.WriteLine("╔══════════════════════════════════════════════════════════════════════════════╗");
            terminal.WriteLine($"║              TEAM HEADQUARTERS - {teamName,-30}           ║");
            terminal.WriteLine("╚══════════════════════════════════════════════════════════════════════════════╝");
            terminal.WriteLine("");

            // Show upgrades
            var upgrades = await backend.GetTeamUpgrades(teamName);
            long vaultGold = await backend.GetTeamVaultGold(teamName);
            int vaultLevel = backend.GetTeamUpgradeLevel(teamName, "vault");
            long vaultCapacity = 50000 + (vaultLevel * 50000);

            terminal.SetColor("bright_yellow");
            terminal.WriteLine("  ═══ Facilities ═══");
            terminal.WriteLine("");

            int idx = 1;
            foreach (var (key, def) in UpgradeDefinitions)
            {
                var existing = upgrades.FirstOrDefault(u => u.UpgradeType == key);
                int level = existing?.Level ?? 0;
                long nextCost = def.BaseCost * (level + 1);

                terminal.SetColor("bright_yellow");
                terminal.Write($"  {idx}. ");
                terminal.SetColor("white");
                terminal.Write($"{def.Name,-22} ");
                terminal.SetColor(level > 0 ? "bright_green" : "gray");
                terminal.Write($"Lv {level,-4} ");
                terminal.SetColor("gray");
                terminal.WriteLine($"({def.Description})  [Upgrade: {nextCost:N0}g]");
                idx++;
            }

            terminal.WriteLine("");
            terminal.SetColor("bright_green");
            terminal.WriteLine($"  Team Vault: {vaultGold:N0} / {vaultCapacity:N0} gold");
            terminal.WriteLine("");

            terminal.SetColor("cyan");
            terminal.WriteLine("  [U] Upgrade Facility    [D] Deposit Gold    [W] Withdraw Gold    [Q] Back");
            terminal.SetColor("white");
            terminal.Write("\n  Choice: ");
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
        terminal.Write("\n  Upgrade # (1-5): ");
        string input = (await terminal.ReadLineAsync())?.Trim() ?? "";
        if (!int.TryParse(input, out int choice) || choice < 1 || choice > keys.Count) return;

        string key = keys[choice - 1];
        var def = UpgradeDefinitions[key];
        int currentLevel = backend.GetTeamUpgradeLevel(teamName, key);

        if (currentLevel >= 10)
        {
            terminal.SetColor("yellow");
            terminal.WriteLine("  Maximum level reached!");
            await Task.Delay(1500);
            return;
        }

        long cost = def.BaseCost * (currentLevel + 1);

        // Try team vault first, then personal gold
        long vaultGold = await backend.GetTeamVaultGold(teamName);

        terminal.SetColor("yellow");
        terminal.WriteLine($"  Upgrade {def.Name} to Lv {currentLevel + 1} costs {cost:N0} gold.");
        terminal.WriteLine($"  Team vault has {vaultGold:N0}g, you have {currentPlayer.Gold:N0}g.");
        terminal.Write("  Pay from [V]ault or [P]ersonal gold? ");
        string payChoice = (await terminal.ReadLineAsync())?.Trim().ToUpper() ?? "";

        if (payChoice == "V")
        {
            if (vaultGold < cost)
            {
                terminal.SetColor("red");
                terminal.WriteLine("  Not enough gold in vault!");
                await Task.Delay(1500);
                return;
            }
            bool withdrawn = await backend.WithdrawFromTeamVault(teamName, cost);
            if (!withdrawn) { terminal.SetColor("red"); terminal.WriteLine("  Failed!"); await Task.Delay(1500); return; }
        }
        else if (payChoice == "P")
        {
            if (currentPlayer.Gold < cost)
            {
                terminal.SetColor("red");
                terminal.WriteLine("  Not enough personal gold!");
                await Task.Delay(1500);
                return;
            }
            currentPlayer.Gold -= cost;
        }
        else return;

        await backend.UpgradeTeamFacility(teamName, key, cost);
        terminal.SetColor("bright_green");
        terminal.WriteLine($"\n  {def.Name} upgraded to Level {currentLevel + 1}!");
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
            terminal.WriteLine("\n  Vault is full! Upgrade it for more capacity.");
            await Task.Delay(1500);
            return;
        }

        terminal.SetColor("white");
        terminal.Write($"\n  Deposit how much? (max {Math.Min(space, currentPlayer.Gold):N0}): ");
        string input = (await terminal.ReadLineAsync())?.Trim() ?? "";
        if (!long.TryParse(input, out long amount) || amount <= 0) return;

        amount = Math.Min(amount, Math.Min(space, currentPlayer.Gold));
        if (amount <= 0) return;

        currentPlayer.Gold -= amount;
        await backend.DepositToTeamVault(teamName, amount);
        terminal.SetColor("bright_green");
        terminal.WriteLine($"  Deposited {amount:N0} gold into the team vault!");
        await Task.Delay(1500);
    }

    private async Task WithdrawFromVault(SqlSaveBackend backend, string teamName)
    {
        long currentVault = await backend.GetTeamVaultGold(teamName);
        if (currentVault <= 0)
        {
            terminal.SetColor("gray");
            terminal.WriteLine("\n  The vault is empty.");
            await Task.Delay(1500);
            return;
        }

        terminal.SetColor("white");
        terminal.Write($"\n  Withdraw how much? (vault has {currentVault:N0}): ");
        string input = (await terminal.ReadLineAsync())?.Trim() ?? "";
        if (!long.TryParse(input, out long amount) || amount <= 0) return;

        amount = Math.Min(amount, currentVault);
        bool success = await backend.WithdrawFromTeamVault(teamName, amount);
        if (success)
        {
            currentPlayer.Gold += amount;
            terminal.SetColor("bright_green");
            terminal.WriteLine($"  Withdrew {amount:N0} gold from the team vault!");
        }
        else
        {
            terminal.SetColor("red");
            terminal.WriteLine("  Withdrawal failed.");
        }
        await Task.Delay(1500);
    }
}
