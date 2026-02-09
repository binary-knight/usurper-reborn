using UsurperRemake.Utils;
using UsurperRemake.Systems;
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

        // Get all teams from NPCs
        var allNPCs = NPCSpawnSystem.Instance.ActiveNPCs;
        var teamGroups = allNPCs
            .Where(n => !string.IsNullOrEmpty(n.Team) && n.IsAlive)
            .GroupBy(n => n.Team)
            .Select(g => new
            {
                TeamName = g.Key,
                MemberCount = g.Count(),
                TotalPower = g.Sum(m => m.Level + (int)m.Strength + (int)m.Defence),
                AverageLevel = (int)g.Average(m => m.Level),
                ControlsTurf = g.Any(m => m.CTurf)
            })
            .OrderByDescending(t => t.TotalPower)
            .ToList();

        // Add player's team if exists and has no NPC members yet
        if (!string.IsNullOrEmpty(currentPlayer.Team))
        {
            var playerTeamExists = teamGroups.Any(t => t.TeamName == currentPlayer.Team);
            if (!playerTeamExists)
            {
                terminal.SetColor("bright_cyan");
                terminal.WriteLine($"Your Team: {currentPlayer.Team}");
                var playerPower = currentPlayer.Level + (int)currentPlayer.Strength + (int)currentPlayer.Defence;
                terminal.WriteLine($"  Members: 1 (just you), Power: {playerPower}, Turf: {(currentPlayer.CTurf ? "Yes" : "No")}");
                terminal.WriteLine("");
            }
        }

        if (teamGroups.Count == 0 && string.IsNullOrEmpty(currentPlayer.Team))
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
                else if (team.TeamName == currentPlayer.Team)
                    terminal.SetColor("bright_cyan");
                else
                    terminal.SetColor("white");

                string turfMark = team.ControlsTurf ? "*" : "-";
                terminal.WriteLine($"{rank,-5} {team.TeamName,-24} {team.MemberCount,-6} {team.TotalPower,-8} {team.AverageLevel,-8} {turfMark,-5}");
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

        if (teamMembers.Count == 0)
        {
            terminal.SetColor("yellow");
            terminal.WriteLine($"No NPC members found in team '{teamName}'.");
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
            terminal.WriteLine($"Total: {teamMembers.Count} NPC members, {teamMembers.Count(m => m.IsAlive)} alive");
        }
        else
        {
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

        // Check if team name already exists
        var allNPCs = NPCSpawnSystem.Instance.ActiveNPCs;
        if (allNPCs.Any(n => n.Team == teamName))
        {
            terminal.SetColor("red");
            terminal.WriteLine("A team with that name already exists!");
            await Task.Delay(2000);
            return;
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

        terminal.WriteLine("");
        terminal.SetColor("bright_green");
        terminal.WriteLine($"Gang '{teamName}' created successfully!");
        terminal.WriteLine($"You are now the leader of {teamName}!");
        terminal.SetColor("yellow");
        terminal.WriteLine($"Paid {creationCost:N0} gold in registration fees.");
        terminal.WriteLine("");

        // Generate news
        NewsSystem.Instance.Newsy(true, $"{currentPlayer.DisplayName} formed a new team: '{teamName}'!");

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

        // Find a team member to get the password from
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
        string password = await terminal.ReadLineAsync();

        if (password == teamMember.TeamPW)
        {
            currentPlayer.Team = teamName;
            currentPlayer.TeamPW = password;
            currentPlayer.CTurf = teamMember.CTurf;

            terminal.WriteLine("");
            terminal.SetColor("bright_green");
            terminal.WriteLine($"Correct! You are now a member of {teamName}!");
            terminal.WriteLine("");

            NewsSystem.Instance.Newsy(true, $"{currentPlayer.DisplayName} joined the team '{teamName}'!");

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

            terminal.WriteLine("");
            terminal.SetColor("bright_green");
            terminal.WriteLine("You have left the team!");
            terminal.WriteLine("");

            NewsSystem.Instance.Newsy(true, $"{currentPlayer.DisplayName} left the team '{oldTeam}'!");

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
}
