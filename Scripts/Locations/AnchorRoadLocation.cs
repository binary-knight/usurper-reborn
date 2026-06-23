using UsurperRemake.BBS;
using UsurperRemake.Utils;
using UsurperRemake.Systems;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;

/// <summary>
/// Anchor Road Location - Challenge hub with bounty hunting, gang wars, the gauntlet,
/// town control, and prison grounds.
/// </summary>
public class AnchorRoadLocation : BaseLocation
{
    private Random random = Random.Shared;

    public AnchorRoadLocation() : base(GameLocation.AnchorRoad, "Anchor Road", "Conjunction of Destinies")
    {
    }

    protected override void SetupLocation()
    {
        PossibleExits = new List<GameLocation>
        {
            GameLocation.MainStreet
        };

        LocationActions = new List<string>
        {
            "Bounty Hunting",
            "Gang War",
            "The Gauntlet",
            "Claim Town",
            "Flee Town Control",
            "Status",
            "Prison Grounds"
        };
    }
    protected override void DisplayLocation()
    {
        if (IsScreenReader) { DisplayLocationSR(); return; }
        if (IsBBSSession) { DisplayLocationBBS(); return; }

        terminal.ClearScreen();

        // Phase 5: Electron mode emits Anchor Road menu state. Pattern B.
        if (GameConfig.ElectronMode)
        {
            EmitElectronEvents();
            return;
        }

        // Header
        if (IsScreenReader)
        {
            terminal.WriteLine(Loc.Get("anchor_road.header_title"), "bright_magenta");
        }
        else
        {
            terminal.SetColor("bright_magenta");
            terminal.WriteLine("╔══════════════════════════════════════════════════════════════════════════════╗");
            { string t = Loc.Get("anchor_road.header_title"); int l = (78 - t.Length) / 2, r = 78 - t.Length - l; terminal.WriteLine($"║{new string(' ', l)}{t}{new string(' ', r)}║"); }
            terminal.WriteLine("╚══════════════════════════════════════════════════════════════════════════════╝");
        }
        terminal.WriteLine("");

        // Atmospheric description
        terminal.SetColor("white");
        terminal.WriteLine(Loc.Get("anchor_road.desc_red_fields"));
        terminal.SetColor("bright_yellow");
        terminal.WriteLine(Loc.Get("anchor_road.desc_blood_glory"));
        terminal.WriteLine("");

        // Show NPCs in location
        ShowNPCsInLocation();

        // Show current status
        ShowChallengeStatus();
        terminal.WriteLine("");

        // Menu - Challenges
        terminal.SetColor("cyan");
        terminal.WriteLine(Loc.Get("anchor_road.menu_challenges"));
        WriteMenuRow("B", Loc.Get("anchor_road.menu_bounty_board"), "G", Loc.Get("anchor_road.menu_gang_war_label"), "T", Loc.Get("anchor_road.menu_gauntlet_label"));
        // v0.62.x Phase 4: Sellsword Hall (faction-issued freelance contracts; no oath required)
        WriteMenuRow("M", Loc.Get("merc.menu_sellsword_hall"), "", "", "", "");
        terminal.WriteLine("");

        // Menu - Town Control
        terminal.SetColor("cyan");
        terminal.WriteLine(Loc.Get("anchor_road.menu_town_control"));
        WriteMenuRow("C", Loc.Get("anchor_road.menu_claim_town_label"), "F", Loc.Get("anchor_road.menu_flee_control_label"), "", "");
        terminal.WriteLine("");

        // Menu - Other
        terminal.SetColor("cyan");
        terminal.WriteLine(Loc.Get("anchor_road.menu_other"));
        WriteMenuRow("P", Loc.Get("anchor_road.menu_prison_grounds"), "S", Loc.Get("anchor_road.menu_status_label"), "R", Loc.Get("anchor_road.menu_return_town"));
        terminal.WriteLine("");

        ShowStatusLine();
    }

    private void DisplayLocationSR()
    {
        terminal.ClearScreen();
        WriteBoxHeader(Loc.Get("anchor_road.header"), "bright_magenta");
        terminal.WriteLine("");
        terminal.SetColor("white");
        terminal.WriteLine(Loc.Get("anchor_road.desc_red_fields"));
        terminal.WriteLine(Loc.Get("anchor_road.desc_blood_glory"));
        terminal.WriteLine("");
        ShowNPCsInLocation();
        ShowChallengeStatus();
        terminal.WriteLine("");
        terminal.SetColor("cyan");
        terminal.WriteLine(Loc.Get("anchor_road.menu_challenges"));
        WriteSRMenuOption("B", Loc.Get("anchor_road.bounty"));
        WriteSRMenuOption("G", Loc.Get("anchor_road.gang_war"));
        WriteSRMenuOption("T", Loc.Get("anchor_road.gauntlet"));
        // v0.62.x Phase 4: Sellsword Hall
        WriteSRMenuOption("M", Loc.Get("merc.menu_sellsword_hall"));
        terminal.WriteLine("");
        terminal.SetColor("cyan");
        terminal.WriteLine(Loc.Get("anchor_road.menu_town_control"));
        WriteSRMenuOption("C", Loc.Get("anchor_road.claim_town"));
        WriteSRMenuOption("F", Loc.Get("anchor_road.flee_control"));
        terminal.WriteLine("");
        terminal.SetColor("cyan");
        terminal.WriteLine(Loc.Get("anchor_road.menu_other"));
        WriteSRMenuOption("P", Loc.Get("anchor_road.prison"));
        WriteSRMenuOption("S", Loc.Get("anchor_road.status"));
        WriteSRMenuOption("R", Loc.Get("anchor_road.return"));
        terminal.WriteLine("");
        ShowStatusLine();
    }

    private void WriteMenuRow(string key1, string label1, string key2, string label2, string key3, string label3)
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
            terminal.Write(label1.PadRight(18));
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
            terminal.Write(label2.PadRight(18));
        }

        if (!string.IsNullOrEmpty(key3))
        {
            terminal.SetColor("darkgray");
            terminal.Write("[");
            terminal.SetColor("bright_yellow");
            terminal.Write(key3);
            terminal.SetColor("darkgray");
            terminal.Write("] ");
            terminal.SetColor("white");
            terminal.Write(label3);
        }
        terminal.WriteLine("");
    }

    private void WriteMenuOption(string key, string label)
    {
        terminal.SetColor("darkgray");
        terminal.Write("[");
        terminal.SetColor("bright_yellow");
        terminal.Write(key);
        terminal.SetColor("darkgray");
        terminal.Write("] ");
        terminal.SetColor("white");
        terminal.WriteLine(label);
    }

    private void ShowChallengeStatus()
    {
        if (!IsScreenReader)
        {
            terminal.SetColor("darkgray");
            terminal.WriteLine("─────────────────────────────────────────");
        }

        // Show player fights remaining
        terminal.SetColor("white");
        terminal.Write(Loc.Get("anchor_road.player_fights"));
        if (currentPlayer.PFights > 0)
        {
            terminal.SetColor("bright_green");
            terminal.WriteLine($"{currentPlayer.PFights}");
        }
        else
        {
            terminal.SetColor("red");
            terminal.WriteLine(Loc.Get("anchor_road.fights_exhausted"));
        }

        // Show team fights if in a team
        if (!string.IsNullOrEmpty(currentPlayer.Team))
        {
            terminal.SetColor("white");
            terminal.Write(Loc.Get("anchor_road.team_fights"));
            if (currentPlayer.TFights > 0)
            {
                terminal.SetColor("bright_green");
                terminal.WriteLine($"{currentPlayer.TFights}");
            }
            else
            {
                terminal.SetColor("red");
                terminal.WriteLine(Loc.Get("anchor_road.fights_exhausted"));
            }

            terminal.SetColor("white");
            terminal.Write(Loc.Get("anchor_road.your_team"));
            terminal.SetColor("bright_cyan");
            terminal.Write(currentPlayer.Team);

            if (currentPlayer.CTurf)
            {
                terminal.SetColor("bright_yellow");
                terminal.WriteLine(Loc.Get("anchor_road.controls_town_tag"));
            }
            else
            {
                terminal.WriteLine("");
            }
        }

        // Show who controls the town
        var turfController = GetTurfControllerName();
        if (!string.IsNullOrEmpty(turfController))
        {
            terminal.SetColor("white");
            terminal.Write(Loc.Get("anchor_road.town_controlled_by"));
            terminal.SetColor("bright_yellow");
            terminal.WriteLine(turfController);
        }

        if (!IsScreenReader)
        {
            terminal.SetColor("darkgray");
            terminal.WriteLine("─────────────────────────────────────────");
        }
    }

    /// <summary>
    /// Compact BBS display for 80x25 terminals.
    /// </summary>
    private void DisplayLocationBBS()
    {
        terminal.ClearScreen();
        ShowBBSHeader(Loc.Get("anchor_road.header"));

        // 1-line description
        terminal.SetColor("white");
        terminal.WriteLine(Loc.Get("anchor_road.bbs_desc"));

        ShowBBSNPCs();

        // Compact challenge status (1-2 lines)
        terminal.SetColor("gray");
        terminal.Write(Loc.Get("anchor_road.bbs_fights"));
        terminal.SetColor(currentPlayer.PFights > 0 ? "bright_green" : "red");
        terminal.Write($"{currentPlayer.PFights}");
        if (!string.IsNullOrEmpty(currentPlayer.Team))
        {
            terminal.SetColor("gray");
            terminal.Write(Loc.Get("anchor_road.bbs_team"));
            terminal.SetColor("cyan");
            terminal.Write($"{currentPlayer.Team}");
            terminal.SetColor("gray");
            terminal.Write(Loc.Get("anchor_road.bbs_tfights"));
            terminal.SetColor(currentPlayer.TFights > 0 ? "bright_green" : "red");
            terminal.Write($"{currentPlayer.TFights}");
            if (currentPlayer.CTurf)
            {
                terminal.SetColor("bright_yellow");
                terminal.Write(Loc.Get("anchor_road.bbs_turf"));
            }
        }
        var turfController = GetTurfControllerName();
        if (!string.IsNullOrEmpty(turfController))
        {
            terminal.SetColor("gray");
            terminal.Write(Loc.Get("anchor_road.bbs_town"));
            terminal.SetColor("bright_yellow");
            terminal.Write(turfController);
        }
        terminal.WriteLine("");
        terminal.WriteLine("");

        // Menu rows
        terminal.SetColor("cyan");
        terminal.WriteLine(Loc.Get("anchor_road.bbs_challenges"));
        ShowBBSMenuRow(("B", "bright_yellow", Loc.Get("anchor_road.bbs_bounty")), ("G", "bright_yellow", Loc.Get("anchor_road.bbs_gang_war")), ("T", "bright_yellow", Loc.Get("anchor_road.bbs_gauntlet")));
        // v0.62.x Phase 4: Sellsword Hall
        ShowBBSMenuRow(("M", "bright_yellow", Loc.Get("merc.menu_sellsword_hall")), ("", "", ""), ("", "", ""));
        terminal.SetColor("cyan");
        terminal.WriteLine(Loc.Get("anchor_road.bbs_town_control"));
        ShowBBSMenuRow(("C", "bright_yellow", Loc.Get("anchor_road.bbs_claim_town")), ("F", "bright_yellow", Loc.Get("anchor_road.bbs_flee_town")), ("P", "bright_yellow", Loc.Get("anchor_road.bbs_prison")), ("R", "bright_yellow", Loc.Get("anchor_road.bbs_return")));

        ShowBBSFooter();
    }

    protected override async Task<bool> ProcessChoice(string choice)
    {
        // Handle global quick commands first
        var (handled, shouldExit) = await TryProcessGlobalCommand(choice);
        if (handled) return shouldExit;

        if (string.IsNullOrWhiteSpace(choice))
            return false;

        char ch = char.ToUpperInvariant(choice.Trim()[0]);

        switch (ch)
        {
            case 'B':
                await StartBountyHunting();
                return false;

            case 'G':
                await StartGangWar();
                return false;

            case 'T':
                await StartGauntlet();
                return false;

            case 'M':
                await ShowSellswordHall();
                return false;

            case 'C':
                await ClaimTown();
                return false;

            case 'F':
                await FleeTownControl();
                return false;

            case 'S':
                await ShowStatus();
                return false;

            case 'P':
                await NavigateToPrisonGrounds();
                return false;

            case 'R':
                await NavigateToLocation(GameLocation.MainStreet);
                return true;

            case '?':
                return false;

            default:
                terminal.SetColor("red");
                terminal.WriteLine(Loc.Get("anchor_road.invalid_choice"));
                await Task.Delay(1500);
                return false;
        }
    }

    #region Challenge Implementations

    /// <summary>
    /// Bounty hunting - hunt for criminal NPCs using real combat
    /// </summary>
    private async Task StartBountyHunting()
    {
        terminal.ClearScreen();
        WriteBoxHeader(Loc.Get("anchor_road.bounty_header"), "bright_red");
        terminal.WriteLine("");

        if (currentPlayer.PFights <= 0)
        {
            terminal.SetColor("red");
            terminal.WriteLine(Loc.Get("anchor_road.no_fights_left"));
            terminal.WriteLine(Loc.Get("anchor_road.come_back_tomorrow"));
            terminal.WriteLine("");
            terminal.SetColor("darkgray");
            terminal.WriteLine(Loc.Get("ui.press_enter"));
            await terminal.ReadKeyAsync();
            return;
        }

        terminal.SetColor("white");
        terminal.WriteLine(Loc.Get("anchor_road.scan_bounty_board"));
        terminal.WriteLine(Loc.Get("anchor_road.fights_remaining", currentPlayer.PFights));
        terminal.WriteLine("");

        // Get NPCs with high darkness (evil NPCs) as bounty targets
        var allNPCs = NPCSpawnSystem.Instance.ActiveNPCs;
        var bountyTargets = allNPCs
            .Where(n => n.IsAlive && !n.IsDead && n.Darkness > 200)
            .OrderByDescending(n => n.Darkness * 10)
            .Take(5)
            .ToList();

        if (bountyTargets.Count == 0)
        {
            // Fallback to random level-appropriate NPCs
            bountyTargets = allNPCs
                .Where(n => n.IsAlive && !n.IsDead && n.Level <= currentPlayer.Level + 5)
                .OrderBy(_ => random.Next())
                .Take(3)
                .ToList();

            if (bountyTargets.Count == 0)
            {
                terminal.SetColor("yellow");
                terminal.WriteLine(Loc.Get("anchor_road.no_bounties"));
                terminal.WriteLine("");
                terminal.SetColor("darkgray");
                terminal.WriteLine(Loc.Get("ui.press_enter"));
                await terminal.ReadKeyAsync();
                return;
            }
        }

        terminal.SetColor("bright_yellow");
        terminal.WriteLine(Loc.Get("anchor_road.wanted_header"));
        if (!IsScreenReader)
        {
            terminal.SetColor("darkgray");
            terminal.WriteLine(new string('─', 60));
        }
        terminal.SetColor("white");
        terminal.WriteLine($"{Loc.Get("anchor_road.bounty_col_num"),-3} {Loc.Get("anchor_road.bounty_col_name"),-20} {Loc.Get("anchor_road.bounty_col_level"),-6} {Loc.Get("anchor_road.bounty_col_bounty"),-12} {Loc.Get("anchor_road.bounty_col_crime"),-15}");
        if (!IsScreenReader)
        {
            terminal.SetColor("darkgray");
            terminal.WriteLine(new string('─', 60));
        }

        for (int i = 0; i < bountyTargets.Count; i++)
        {
            var target = bountyTargets[i];
            long bounty = target.Level * 100 + (long)target.Darkness;
            string crime = target.Darkness > 500 ? Loc.Get("anchor_road.crime_murder") :
                          target.Darkness > 200 ? Loc.Get("anchor_road.crime_assault") : Loc.Get("anchor_road.crime_troublemaker");

            terminal.SetColor("white");
            terminal.WriteLine($"{i + 1,-3} {target.DisplayName,-20} {target.Level,-6} {bounty:N0}g{"",-5} {crime,-15}");
        }

        terminal.WriteLine("");
        terminal.SetColor("cyan");
        terminal.Write(Loc.Get("anchor_road.hunt_prompt"));
        terminal.SetColor("white");
        string input = await terminal.ReadLineAsync();

        if (int.TryParse(input, out int choice) && choice >= 1 && choice <= bountyTargets.Count)
        {
            var target = bountyTargets[choice - 1];
            currentPlayer.PFights--;

            terminal.WriteLine("");
            terminal.SetColor("bright_red");
            terminal.WriteLine(Loc.Get("anchor_road.tracking_target", target.DisplayName));
            await Task.Delay(1000);

            // Real combat using CombatEngine
            var combatEngine = new CombatEngine(terminal);
            var result = await combatEngine.PlayerVsPlayer(currentPlayer, target, allowSurrender: false); // v0.64.1: bounty hunt treats non-Victory as loss

            if (result.Outcome == CombatOutcome.Victory)
            {
                long bounty = target.Level * 100 + (long)target.Darkness;
                long expGain = target.Level * 50;

                terminal.SetColor("bright_green");
                terminal.WriteLine("");
                WriteSectionHeader(Loc.Get("anchor_road.bounty_collected"), "bright_green");
                terminal.WriteLine(Loc.Get("ui.bounty_reward", $"{bounty:N0}"));
                terminal.WriteLine($"{Loc.Get("ui.experience")}: {expGain:N0}");

                currentPlayer.Gold += bounty;
                currentPlayer.Experience += expGain;
                currentPlayer.PKills++;
                target.HP = 0;

                NewsSystem.Instance.Newsy(true, $"{currentPlayer.DisplayName} collected the bounty on {target.DisplayName}!");
            }
            else if (result.Outcome == CombatOutcome.PlayerEscaped)
            {
                terminal.SetColor("yellow");
                terminal.WriteLine("");
                terminal.WriteLine(Loc.Get("anchor_road.fled_bounty"));
            }
            else
            {
                terminal.SetColor("red");
                terminal.WriteLine("");
                terminal.WriteLine(Loc.Get("anchor_road.bested_by_target", target.DisplayName));
            }
        }

        terminal.WriteLine("");
        terminal.SetColor("darkgray");
        terminal.WriteLine(Loc.Get("ui.press_enter"));
        await terminal.ReadKeyAsync();
    }

    /// <summary>
    /// Gang war - sequential 1v1 fights against rival team members using real combat
    /// </summary>
    private async Task StartGangWar()
    {
        terminal.ClearScreen();
        WriteBoxHeader(Loc.Get("anchor_road.gang_war_header"), "bright_red");
        terminal.WriteLine("");

        if (string.IsNullOrEmpty(currentPlayer.Team))
        {
            terminal.SetColor("red");
            terminal.WriteLine(Loc.Get("anchor_road.no_team_gang_war"));
            terminal.WriteLine(Loc.Get("anchor_road.visit_team_corner"));
            terminal.WriteLine("");
            terminal.SetColor("darkgray");
            terminal.WriteLine(Loc.Get("ui.press_enter"));
            await terminal.ReadKeyAsync();
            return;
        }

        if (currentPlayer.TFights <= 0)
        {
            terminal.SetColor("red");
            terminal.WriteLine(Loc.Get("anchor_road.no_team_fights"));
            terminal.WriteLine("");
            terminal.SetColor("darkgray");
            terminal.WriteLine(Loc.Get("ui.press_enter"));
            await terminal.ReadKeyAsync();
            return;
        }

        // Get all active teams
        var allNPCs = NPCSpawnSystem.Instance.ActiveNPCs;
        var teams = allNPCs
            .Where(n => !string.IsNullOrEmpty(n.Team) && n.IsAlive && !n.IsDead && n.Team != currentPlayer.Team)
            .GroupBy(n => n.Team)
            .Select(g => new
            {
                TeamName = g.Key,
                MemberCount = g.Count(),
                TotalPower = g.Sum(m => m.Level + (int)m.Strength + (int)m.Defence),
                ControlsTurf = g.Any(m => m.CTurf)
            })
            .ToList();

        // v0.57.9 (Coosh report: "I can't challenge the team watchers even though they own
        // the town turf"): teams that hold CTurf but have zero alive members (all dead /
        // permadead) got filtered out above, making the turf unreachable via Gang War.
        // Pascal's GANGWARS.PAS had EasyTownTakeover for this; the C# inline port missed it.
        // Surface them here as "ghost" controllers with 0 members — the fight loop below
        // short-circuits to an unopposed takeover when selected.
        var ghostControllers = allNPCs
            .Where(n => !string.IsNullOrEmpty(n.Team) && n.CTurf && n.Team != currentPlayer.Team)
            .GroupBy(n => n.Team)
            .Where(g => !teams.Any(t => t.TeamName == g.Key))
            .Select(g => new
            {
                TeamName = g.Key,
                MemberCount = 0,
                TotalPower = 0,
                ControlsTurf = true
            });
        teams.AddRange(ghostControllers);
        teams = teams.OrderByDescending(t => t.TotalPower).ToList();

        if (teams.Count == 0)
        {
            terminal.SetColor("yellow");
            terminal.WriteLine(Loc.Get("anchor_road.no_rival_teams"));
            terminal.WriteLine("");
            terminal.SetColor("darkgray");
            terminal.WriteLine(Loc.Get("ui.press_enter"));
            await terminal.ReadKeyAsync();
            return;
        }

        terminal.SetColor("white");
        terminal.WriteLine(Loc.Get("anchor_road.your_team_label", currentPlayer.Team));
        terminal.WriteLine(Loc.Get("anchor_road.team_fights_remaining", currentPlayer.TFights));
        terminal.WriteLine("");

        terminal.SetColor("cyan");
        terminal.WriteLine(Loc.Get("anchor_road.rival_teams"));
        if (!IsScreenReader)
        {
            terminal.SetColor("darkgray");
            terminal.WriteLine(new string('─', 55));
        }
        terminal.SetColor("white");
        terminal.WriteLine($"{Loc.Get("anchor_road.rival_col_num"),-3} {Loc.Get("anchor_road.rival_col_team"),-24} {Loc.Get("anchor_road.rival_col_members"),-8} {Loc.Get("anchor_road.rival_col_power"),-8} {Loc.Get("anchor_road.rival_col_turf"),-5}");
        if (!IsScreenReader)
        {
            terminal.SetColor("darkgray");
            terminal.WriteLine(new string('─', 55));
        }

        for (int i = 0; i < teams.Count; i++)
        {
            var team = teams[i];
            string turfMark = team.ControlsTurf ? "*" : "-";

            if (team.ControlsTurf)
                terminal.SetColor("bright_yellow");
            else
                terminal.SetColor("white");

            terminal.WriteLine($"{i + 1,-3} {team.TeamName,-24} {team.MemberCount,-8} {team.TotalPower,-8} {turfMark,-5}");
        }

        terminal.WriteLine("");
        terminal.SetColor("cyan");
        terminal.Write(Loc.Get("anchor_road.challenge_prompt"));
        terminal.SetColor("white");
        string input = await terminal.ReadLineAsync();

        if (int.TryParse(input, out int choice) && choice >= 1 && choice <= teams.Count)
        {
            var targetTeam = teams[choice - 1];
            currentPlayer.TFights--;

            terminal.WriteLine("");
            terminal.SetColor("bright_red");
            terminal.WriteLine(Loc.Get("anchor_road.team_challenges", targetTeam.TeamName));
            terminal.WriteLine(Loc.Get("anchor_road.defeat_one_by_one"));
            terminal.WriteLine("");
            await Task.Delay(1500);

            // Get player's NPC team members.
            // `playerTeamFighters` (alive only) is used for combat participation.
            // `allPlayerTeamMembers` (all, including dead) is used when setting the
            // CTurf flag on turf transfer — v0.57.10 (Coosh report): dead teammates
            // who respawn later need to read as controllers too, otherwise the
            // "who controls the town" query flips back to an NPC team on the first
            // respawn tick.
            var playerTeamFighters = allNPCs
                .Where(n => n.Team == currentPlayer.Team && n.IsAlive)
                .ToList();
            var allPlayerTeamMembers = allNPCs
                .Where(n => n.Team == currentPlayer.Team)
                .ToList();

            // Get enemy team members sorted by level (weakest first)
            var enemyTeamMembers = allNPCs
                .Where(n => n.Team == targetTeam.TeamName && n.IsAlive && !n.IsDead)
                .OrderBy(n => n.Level)
                .ToList();

            // v0.57.9: "ghost controller" short-circuit — team holds turf but has zero alive
            // members, so there's nobody to fight. Unopposed takeover, turf transfers clean.
            if (enemyTeamMembers.Count == 0 && targetTeam.ControlsTurf)
            {
                terminal.SetColor("bright_yellow");
                WriteSectionHeader(Loc.Get("anchor_road.gang_war_victory"), "bright_green");
                terminal.SetColor("white");
                terminal.WriteLine(Loc.Get("anchor_road.ghost_takeover", targetTeam.TeamName));
                terminal.WriteLine("");

                // Strip turf from any remaining flagged NPCs on the ghost team (even dead ones)
                foreach (var npc in allNPCs.Where(n => n.Team == targetTeam.TeamName))
                    npc.CTurf = false;
                currentPlayer.CTurf = true;
                foreach (var ally in allPlayerTeamMembers)
                    ally.CTurf = true;

                terminal.SetColor("bright_yellow");
                terminal.WriteLine(Loc.Get("anchor_road.team_controls_town"));
                NewsSystem.Instance.Newsy(true, $"Gang War! {currentPlayer.Team} took the town unopposed — {targetTeam.TeamName} had no living members left.");

                // v0.57.10 (Coosh report): persist the turf transfer to world_state
                // immediately. Without this, a relog before the next auto-save tick
                // reloads the stale `Watchers` NPC snapshot with its old CTurf=true
                // flag and the "who controls town" query returns the old owner.
                await PersistTurfTransfer();

                terminal.WriteLine("");
                terminal.SetColor("darkgray");
                terminal.WriteLine(Loc.Get("ui.press_enter"));
                await terminal.ReadKeyAsync();
                return;
            }

            bool playerWon = true;
            int enemiesDefeated = 0;
            long totalGoldReward = 0;
            long totalXPReward = 0;

            for (int f = 0; f < enemyTeamMembers.Count; f++)
            {
                var enemy = enemyTeamMembers[f];

                WriteSectionHeader(Loc.Get("anchor_road.fight_header", f + 1, enemyTeamMembers.Count), "bright_magenta");
                terminal.SetColor("white");
                terminal.WriteLine(Loc.Get("anchor_road.face_opponent", enemy.DisplayName, enemy.Level, GameConfig.GetLocalizedClassName(enemy.Class)));
                terminal.WriteLine("");
                await Task.Delay(1000);

                var combatEngine = new CombatEngine(terminal);
                var result = await combatEngine.PlayerVsPlayer(currentPlayer, enemy, allowSurrender: false); // v0.64.1: gang war chain treats non-Victory as forfeit

                if (result.Outcome == CombatOutcome.Victory)
                {
                    enemiesDefeated++;
                    totalGoldReward += enemy.Level * 50;
                    totalXPReward += enemy.Level * 25;

                    if (f < enemyTeamMembers.Count - 1)
                    {
                        // Heal between fights
                        long healAmount = currentPlayer.MaxHP / 7;
                        currentPlayer.HP = Math.Min(currentPlayer.MaxHP, currentPlayer.HP + healAmount);

                        terminal.SetColor("bright_green");
                        terminal.WriteLine(Loc.Get("anchor_road.breath_recover", healAmount));
                        terminal.WriteLine($"{Loc.Get("combat.bar_hp")}: {currentPlayer.HP}/{currentPlayer.MaxHP}");
                        terminal.WriteLine("");
                        await Task.Delay(1000);
                    }
                }
                else
                {
                    // Player lost or fled — gang war over
                    playerWon = false;
                    break;
                }
            }

            terminal.WriteLine("");

            if (playerWon)
            {
                terminal.SetColor("bright_green");
                WriteSectionHeader(Loc.Get("anchor_road.gang_war_victory"), "bright_green");
                terminal.WriteLine(Loc.Get("ui.defeated_all_members", enemiesDefeated, targetTeam.TeamName));
                terminal.WriteLine(Loc.Get("ui.gold_plundered", $"{totalGoldReward:N0}"));
                terminal.WriteLine($"{Loc.Get("ui.experience")}: {totalXPReward:N0}");

                currentPlayer.Gold += totalGoldReward;
                currentPlayer.Experience += totalXPReward;

                // Handle turf transfer
                if (targetTeam.ControlsTurf)
                {
                    terminal.SetColor("bright_yellow");
                    terminal.WriteLine("");
                    terminal.WriteLine(Loc.Get("anchor_road.team_controls_town"));

                    // v0.57.10: strip CTurf from EVERY NPC on the losing team, not
                    // just the alive ones who participated in the fight — dead /
                    // permadead members on the losing team would otherwise keep
                    // CTurf=true and reappear as controllers the moment they
                    // respawn. Matches the ghost-takeover strip above.
                    foreach (var enemyNpc in allNPCs.Where(n => n.Team == targetTeam.TeamName))
                        enemyNpc.CTurf = false;
                    currentPlayer.CTurf = true;
                    foreach (var ally in allPlayerTeamMembers)
                        ally.CTurf = true;
                }

                NewsSystem.Instance.Newsy(true, $"Gang War! {currentPlayer.Team} defeated {targetTeam.TeamName}!");

                // v0.57.10 (Coosh report): sync the transfer to world_state before
                // the player can log out. Without this, `OnlineStateManager.LoadSharedNPCs`
                // on the next login reloads the stale NPC snapshot and the old
                // controller (often the Watchers default team) reclaims the CTurf
                // flag silently.
                if (targetTeam.ControlsTurf)
                    await PersistTurfTransfer();
            }
            else
            {
                terminal.SetColor("red");
                WriteSectionHeader(Loc.Get("anchor_road.gang_war_defeat"), "red");
                terminal.WriteLine(Loc.Get("anchor_road.defeated_after", enemiesDefeated, enemiesDefeated != 1 ? "s" : ""));

                if (enemiesDefeated > 0)
                {
                    // Give partial rewards for enemies defeated before losing
                    long partialGold = totalGoldReward / 2;
                    long partialXP = totalXPReward / 2;
                    terminal.SetColor("yellow");
                    terminal.WriteLine(Loc.Get("anchor_road.partial_rewards", $"{partialGold:N0}", $"{partialXP:N0}"));
                    currentPlayer.Gold += partialGold;
                    currentPlayer.Experience += partialXP;
                }

                NewsSystem.Instance.Newsy(true, $"Gang War! {targetTeam.TeamName} repelled {currentPlayer.Team}!");
            }
        }

        terminal.WriteLine("");
        terminal.SetColor("darkgray");
        terminal.WriteLine(Loc.Get("ui.press_enter"));
        await terminal.ReadKeyAsync();
    }

    /// <summary>
    /// The Gauntlet - solo 10-wave endurance challenge against increasingly tough monsters
    /// </summary>
    private async Task StartGauntlet()
    {
        terminal.ClearScreen();
        WriteBoxHeader(Loc.Get("anchor_road.gauntlet_header"), "bright_yellow");
        terminal.WriteLine("");

        terminal.SetColor("white");
        terminal.WriteLine(Loc.Get("anchor_road.gauntlet_desc_1"));
        terminal.WriteLine(Loc.Get("anchor_road.gauntlet_desc_2"));
        terminal.WriteLine(Loc.Get("anchor_road.gauntlet_desc_3"));
        terminal.SetColor("dark_gray");
        terminal.WriteLine(Loc.Get("anchor_road.gauntlet_desc_safety"));
        terminal.WriteLine("");

        if (currentPlayer.Level < GameConfig.GauntletMinLevel)
        {
            terminal.SetColor("red");
            terminal.WriteLine(Loc.Get("anchor_road.gauntlet_min_level", GameConfig.GauntletMinLevel));
            terminal.WriteLine("");
            terminal.SetColor("darkgray");
            terminal.WriteLine(Loc.Get("ui.press_enter"));
            await terminal.ReadKeyAsync();
            return;
        }

        if (currentPlayer.PFights <= 0)
        {
            terminal.SetColor("red");
            terminal.WriteLine(Loc.Get("anchor_road.no_fights_left"));
            terminal.WriteLine("");
            terminal.SetColor("darkgray");
            terminal.WriteLine(Loc.Get("ui.press_enter"));
            await terminal.ReadKeyAsync();
            return;
        }

        // v0.60.11 hotfix: daily run cap. Stops endgame players from farming the gauntlet
        // for stacks of mid-tier loot in a single session. Matches the cadence of the
        // other daily caps (Love Street, drinking games, team wars).
        if (currentPlayer.GauntletRunsToday >= GameConfig.MaxGauntletRunsPerDay)
        {
            terminal.SetColor("red");
            terminal.WriteLine(Loc.Get("anchor_road.gauntlet_daily_cap_reached", GameConfig.MaxGauntletRunsPerDay));
            terminal.WriteLine("");
            terminal.SetColor("darkgray");
            terminal.WriteLine(Loc.Get("ui.press_enter"));
            await terminal.ReadKeyAsync();
            return;
        }

        // v0.60.11: quadratic entry fee scaling. Pre-fix `100 * level` was trivial at endgame.
        long entryFee = (long)GameConfig.GauntletEntryFeeQuadraticCoefficient * currentPlayer.Level * currentPlayer.Level;

        terminal.SetColor("cyan");
        terminal.WriteLine(Loc.Get("anchor_road.gauntlet_details"));
        if (!IsScreenReader)
        {
            terminal.SetColor("darkgray");
            terminal.WriteLine("─────────────────────────────────────────");
        }
        terminal.SetColor("white");
        terminal.Write(Loc.Get("anchor_road.entry_fee"));
        terminal.SetColor("bright_yellow");
        terminal.WriteLine(Loc.Get("anchor_road.gold_amount", entryFee.ToString("N0")));
        terminal.SetColor("white");
        terminal.Write(Loc.Get("anchor_road.your_gold"));
        terminal.SetColor(currentPlayer.Gold >= entryFee ? "bright_green" : "red");
        terminal.WriteLine($"{currentPlayer.Gold:N0}");
        terminal.SetColor("white");
        terminal.Write(Loc.Get("anchor_road.your_hp"));
        terminal.SetColor(currentPlayer.HP > currentPlayer.MaxHP / 2 ? "bright_green" : "red");
        terminal.WriteLine($"{currentPlayer.HP}/{currentPlayer.MaxHP}");
        // v0.60.11 hotfix: surface the daily-cap state so the player sees their
        // remaining runs before committing to the entry fee.
        int runsRemaining = System.Math.Max(0, GameConfig.MaxGauntletRunsPerDay - currentPlayer.GauntletRunsToday);
        terminal.SetColor("white");
        terminal.Write(Loc.Get("anchor_road.gauntlet_runs_today"));
        terminal.SetColor(runsRemaining > 0 ? "bright_green" : "red");
        terminal.WriteLine($"{runsRemaining}/{GameConfig.MaxGauntletRunsPerDay}");
        if (!IsScreenReader)
        {
            terminal.SetColor("darkgray");
            terminal.WriteLine("─────────────────────────────────────────");
        }
        terminal.WriteLine("");

        if (currentPlayer.Gold < entryFee)
        {
            terminal.SetColor("red");
            terminal.WriteLine(Loc.Get("anchor_road.need_gold", $"{entryFee:N0}", $"{currentPlayer.Gold:N0}"));
            terminal.WriteLine("");
            terminal.SetColor("darkgray");
            terminal.WriteLine(Loc.Get("ui.press_enter"));
            await terminal.ReadKeyAsync();
            return;
        }

        terminal.SetColor("cyan");
        terminal.Write(Loc.Get("anchor_road.enter_gauntlet_prompt", $"{entryFee:N0}"));
        terminal.SetColor("white");
        string response = await terminal.ReadLineAsync();

        if (!GameConfig.IsAffirmative(response))
        {
            terminal.SetColor("white");
            terminal.WriteLine(Loc.Get("anchor_road.come_back_later"));
            terminal.WriteLine("");
            terminal.SetColor("darkgray");
            terminal.WriteLine(Loc.Get("ui.press_enter"));
            await terminal.ReadKeyAsync();
            return;
        }

        // Deduct entry fee and fight
        currentPlayer.Gold -= entryFee;
        currentPlayer.PFights--;
        // v0.60.11 hotfix: count this run against the daily cap. Increments on commit
        // (regardless of outcome -- whether the player wins, loses, surrenders, or
        // gets dragged out, the daily slot is spent).
        currentPlayer.GauntletRunsToday++;

        terminal.WriteLine("");
        terminal.SetColor("bright_red");
        terminal.WriteLine(Loc.Get("anchor_road.gates_slam"));
        terminal.WriteLine(Loc.Get("anchor_road.crowd_roars"));
        terminal.WriteLine("");
        await Task.Delay(2000);

        int wavesCompleted = 0;
        long totalGoldEarned = 0;
        long totalXPEarned = 0;
        int fameEarnedThisRun = 0; // v0.60.11: tracked so we can forfeit it on a loss

        // v0.60.11 hotfix: pre-roll the "showpiece" champion for this run -- exactly ONE
        // of the 7 champions drops at the player's earned top rarity (Artifact at Lv 80+,
        // Rare at Lv 22, etc.); the other 6 drop one or two notches below. Weighted so the
        // Tyrant (wave 10, the final fight) has a higher chance of being the showpiece --
        // payoff-on-hardest-fight matches loot-game expectations -- but any champion can
        // roll into it so the whole run feels meaningful.
        //
        // Wave-to-champion-index mapping: wave - 4 (waves 4-10 -> champion 0-6, champion 6
        // is the Tyrant). Weights: Tyrant 35%, each other champion ~10.83%.
        int showpieceChampionIdx;
        {
            int roll = random.Next(100);
            // Tyrant gets 35%. Remaining 65% split evenly across 6 champions = 10.83% each.
            if (roll < 35) showpieceChampionIdx = 6;       // Tyrant
            else if (roll < 46) showpieceChampionIdx = 0;  // Vargash
            else if (roll < 57) showpieceChampionIdx = 1;  // Selithea
            else if (roll < 68) showpieceChampionIdx = 2;  // Korr
            else if (roll < 79) showpieceChampionIdx = 3;  // Black Twin
            else if (roll < 89) showpieceChampionIdx = 4;  // Aedric
            else                 showpieceChampionIdx = 5; // Grok
        }

        for (int wave = 1; wave <= GameConfig.GauntletWaveCount; wave++)
        {
            // v0.60.11: pre-roll the death die per wave. If this wave's loss would be a
            // "real death" (25% chance), don't set IsExhibitionCombat -- the combat engine
            // will then run the normal death path (consume resurrection, possibly permadeath).
            // If it would be a "drag-out" (75% chance), set the flag so combat protects HP=1
            // and we apply the lighter XP / Fame penalty post-combat. Only one of these dice
            // matters per run because the player only LOSES a single wave (the one that
            // ends their run); the rest are wasted rolls.
            // v0.60.11: between fights -- offer surrender (clean exit, no penalty) and
            // print a crowd-ambiance line for atmosphere. First wave skips this since the
            // player just paid the entry fee and committed.
            if (wave > 1)
            {
                if (await OfferGauntletSurrender(wave, wavesCompleted, totalGoldEarned, totalXPEarned, fameEarnedThisRun))
                {
                    terminal.SetColor("yellow");
                    terminal.WriteLine("");
                    terminal.WriteLine(Loc.Get("anchor_road.gauntlet_surrender_taken"));
                    return;
                }
                PrintGauntletCrowdAmbiance();
            }

            bool deathRollThisWave = random.Next(100) < GameConfig.GauntletDeathChancePercent;

            // v0.60.11: split spawning by wave kind.
            //   Waves 1-3: themed warmup (random low-tier monster with arena flavor wrapper).
            //   Waves 4-10: the 7 Old God champions in canonical order, hand-crafted stats
            //   and announced entrance theater.
            Monster monster;
            UsurperRemake.Data.GauntletChampionData.GauntletChampion? championData = null;
            bool isWarmupWave = wave <= 3;

            terminal.ClearScreen();
            WriteSectionHeader(Loc.Get("anchor_road.wave_header", wave, GameConfig.GauntletWaveCount), "bright_yellow");
            terminal.WriteLine("");

            if (isWarmupWave)
            {
                int monsterLevel = Math.Max(1, Math.Min(100, currentPlayer.Level - 3 + wave));
                monster = MonsterGenerator.GenerateMonster(monsterLevel, false, false, random);
                AnnounceWarmupWave(monster);
            }
            else
            {
                championData = UsurperRemake.Data.GauntletChampionData.Champions[wave - 4];
                monster = SpawnChampionMonster(championData, currentPlayer.Level);
                await AnnounceChampionEntrance(championData, monster);
            }

            terminal.WriteLine("");
            terminal.SetColor("white");
            terminal.Write(Loc.Get("anchor_road.your_hp"));
            terminal.SetColor(currentPlayer.HP > currentPlayer.MaxHP / 2 ? "bright_green" : "red");
            terminal.WriteLine($"{currentPlayer.HP}/{currentPlayer.MaxHP}");
            terminal.WriteLine("");
            await Task.Delay(1000);

            // Real combat. v0.60.8: mark as exhibition so a wave loss doesn't
            // burn a resurrection -- the entry fee + daily PFight slot are
            // already the cost of admission. HP=1 restore happens inside
            // HandlePlayerDeath; the wave loop ends in the defeat branch below.
            // v0.60.11: only mark as exhibition when the death-roll says
            // "drag-out." When the roll said "real death," leave the flag
            // false and let the combat engine's normal death path consume
            // a resurrection (or trigger permadeath in online mode).
            var combatEngine = new CombatEngine(terminal);
            CombatResult result;
            currentPlayer.IsExhibitionCombat = !deathRollThisWave;
            try
            {
                result = await combatEngine.PlayerVsMonster(currentPlayer, monster, null, false);
            }
            finally
            {
                currentPlayer.IsExhibitionCombat = false;
            }

            if (result.Outcome == CombatOutcome.Victory)
            {
                wavesCompleted++;

                // Wave rewards
                long waveGold = GameConfig.GauntletGoldPerWavePerLevel * currentPlayer.Level;
                long waveXP = GameConfig.GauntletXPPerWave * wave * currentPlayer.Level;
                totalGoldEarned += waveGold;
                totalXPEarned += waveXP;
                currentPlayer.Gold += waveGold;
                currentPlayer.Experience += waveXP;

                // v0.60.11: Fame per wave. Curve matches the difficulty tiers:
                // waves 1-3 (warmup, sub-level monsters) give +1 each, waves 4-6
                // (at/over level) give +2 each, waves 7-9 (mini-boss tier) give
                // +3 each, wave 10 (boss) gives +5. Champion bonus below adds
                // another +20. Full clear = 1+1+1+2+2+2+3+3+3+5+20 = 43 Fame.
                // Partial-clear progress earns scaling Fame so even reaching
                // wave 5-6 is a meaningful renown bump (~7 Fame).
                int waveFame = wave switch
                {
                    <= 3 => 1,
                    <= 6 => 2,
                    <= 9 => 3,
                    _    => 5
                };
                currentPlayer.Fame += waveFame;
                fameEarnedThisRun += waveFame;

                terminal.SetColor("bright_green");
                terminal.WriteLine(Loc.Get("anchor_road.wave_complete", wave, $"{waveGold:N0}", $"{waveXP:N0}"));
                terminal.SetColor("bright_yellow");
                terminal.WriteLine(Loc.Get("anchor_road.wave_fame", waveFame));

                // v0.60.11: drop the champion's themed equipment on defeat. Stats scaled
                // to player level so the drop is always relevant. Warmup waves don't drop
                // champion gear -- they're opening acts, gold/XP only.
                // Hotfix: only ONE champion per run drops at the player's top rarity tier
                // (the "showpiece"); the rest drop one or two notches below so a clear
                // doesn't produce a full set of top-tier gear.
                if (championData != null)
                {
                    int championIdx = wave - 4;
                    bool isShowpiece = championIdx == showpieceChampionIdx;
                    GenerateAndAwardChampionDrop(championData, currentPlayer.Level, isShowpiece);
                }

                // v0.60.11: full-clear (wave 10) tier-based reward. Tier is gated on player
                // level at this moment: Hopeful (5-19), Veteran (20-39), Master (40-59),
                // Champion (60-79), GrandChampion (80+). Each tier scales gold/XP/Fame
                // dramatically and grants a tier-keyed achievement. Player's stored tier
                // upgrades only -- a Lv 80 re-clear from a Lv 25 Veteran becomes Grand
                // Champion permanently; a Lv 25 re-clear from a Lv 80 Grand Champion does
                // NOT downgrade (the higher honor sticks).
                if (wave == GameConfig.GauntletWaveCount)
                {
                    var earnedTier = UsurperRemake.Data.GauntletChampionData.GetTierForLevel(currentPlayer.Level);
                    var rewards = UsurperRemake.Data.GauntletChampionData.GetTierRewards(earnedTier);
                    string tierTitle = UsurperRemake.Data.GauntletChampionData.GetTierTitle(earnedTier);
                    string tierAchievementId = UsurperRemake.Data.GauntletChampionData.GetTierAchievementId(earnedTier);

                    long tierGold = (long)rewards.GoldMultiplierPerLevel * currentPlayer.Level;
                    long tierXP = (long)rewards.XpMultiplierPerLevel * currentPlayer.Level;
                    int tierFame = rewards.FameBonus;

                    totalGoldEarned += tierGold;
                    totalXPEarned += tierXP;
                    currentPlayer.Gold += tierGold;
                    currentPlayer.Experience += tierXP;
                    currentPlayer.Fame += tierFame;
                    fameEarnedThisRun += tierFame;

                    // Upgrade tier only -- never downgrade on a lower-level re-clear.
                    bool newHighWaterMark = (int)earnedTier > currentPlayer.ArenaChampionTier;
                    if (newHighWaterMark)
                    {
                        currentPlayer.ArenaChampionTier = (int)earnedTier;
                        // Auto-set NobleTitle on tier upgrade (same UX as the knighting
                        // ceremony's auto-assign). Player can change it or remove it via
                        // Preferences > Title at any time. Persist to MetaProgressionSystem
                        // so the title survives NG+ cycles.
                        currentPlayer.NobleTitle = tierTitle;
                        try
                        {
                            MetaProgressionSystem.Instance.UnlockedTitles.Add(tierTitle);
                            MetaProgressionSystem.Instance.SaveData();
                        }
                        catch { /* best-effort meta-persist */ }
                    }

                    terminal.WriteLine("");
                    WriteSectionHeader(Loc.Get("anchor_road.gauntlet_full_clear", tierTitle), "bright_yellow");
                    terminal.SetColor("bright_yellow");
                    terminal.WriteLine(Loc.Get("anchor_road.tier_reward", $"{tierGold:N0}", $"{tierXP:N0}", tierFame));
                    if (newHighWaterMark)
                    {
                        terminal.SetColor("bright_magenta");
                        terminal.WriteLine(Loc.Get("anchor_road.tier_title_earned", tierTitle));
                        terminal.SetColor("dark_gray");
                        terminal.WriteLine(Loc.Get("anchor_road.tier_title_change_hint"));
                        if (earnedTier == UsurperRemake.Data.GauntletChampionData.ArenaTier.GrandChampion)
                        {
                            terminal.SetColor("bright_cyan");
                            terminal.WriteLine(Loc.Get("anchor_road.grand_champion_passive",
                                (int)(GameConfig.GrandChampionDamageBonus * 100),
                                (int)(GameConfig.GrandChampionDefenseBonus * 100)));
                        }
                    }
                    else
                    {
                        terminal.SetColor("dark_gray");
                        terminal.WriteLine(Loc.Get("anchor_road.tier_title_already_held",
                            UsurperRemake.Data.GauntletChampionData.GetTierTitle((UsurperRemake.Data.GauntletChampionData.ArenaTier)currentPlayer.ArenaChampionTier)));
                    }

                    AchievementSystem.TryUnlock(currentPlayer, tierAchievementId);
                    NewsSystem.Instance.Newsy(true, $"{tierTitle} {currentPlayer.DisplayName} has conquered the Anchor Road Gauntlet!");
                    try
                    {
                        if (UsurperRemake.BBS.DoorMode.IsOnlineMode)
                            UsurperRemake.Server.MudServer.Instance?.BroadcastToAll(
                                $"[1;33m*** {tierTitle} {currentPlayer.DisplayName} has conquered the Anchor Road Gauntlet! ***[0m");
                    }
                    catch { /* broadcast best-effort */ }
                }
                else
                {
                    // Heal between waves
                    long healAmount = (long)(currentPlayer.MaxHP * GameConfig.GauntletHealBetweenWaves);
                    long manaRestore = (long)(currentPlayer.MaxMana * GameConfig.GauntletManaRestoreBetweenWaves);
                    currentPlayer.HP = Math.Min(currentPlayer.MaxHP, currentPlayer.HP + healAmount);
                    currentPlayer.Mana = Math.Min(currentPlayer.MaxMana, currentPlayer.Mana + manaRestore);

                    terminal.SetColor("cyan");
                    terminal.WriteLine(Loc.Get("anchor_road.catch_breath", healAmount, manaRestore));
                    terminal.WriteLine($"{Loc.Get("combat.bar_hp")}: {currentPlayer.HP}/{currentPlayer.MaxHP}  {Loc.Get("ui.mana_label")}: {currentPlayer.Mana}/{currentPlayer.MaxMana}");
                    terminal.WriteLine("");
                    terminal.SetColor("darkgray");
                    terminal.WriteLine(Loc.Get("anchor_road.next_wave"));
                    await terminal.ReadKeyAsync();
                }
            }
            else
            {
                // Player went down or fled -- gauntlet over.
                terminal.SetColor("red");
                terminal.WriteLine("");

                if (result.Outcome == CombatOutcome.PlayerEscaped)
                {
                    // Flee. No real death, no drag-out penalty -- the flee itself
                    // already cost them the entry fee + daily fight slot.
                    terminal.WriteLine(Loc.Get("anchor_road.flee_disgrace"));
                }
                else if (deathRollThisWave)
                {
                    // v0.60.11: real death. Combat already ran the normal death
                    // path (consumed a resurrection in online mode, possibly
                    // triggered permadeath if Resurrections was 0). HandlePlayerDeath
                    // already printed the appropriate death narrative. We just need
                    // a gauntlet-specific flavor line and to forfeit any Fame
                    // earned this run (the run is voided since they "died").
                    terminal.SetColor("bright_red");
                    terminal.WriteLine(Loc.Get("anchor_road.gauntlet_claims_you"));
                    if (fameEarnedThisRun > 0)
                    {
                        currentPlayer.Fame = Math.Max(0, currentPlayer.Fame - fameEarnedThisRun);
                        terminal.SetColor("dark_gray");
                        terminal.WriteLine(Loc.Get("anchor_road.gauntlet_fame_forfeit_dead", fameEarnedThisRun));
                    }
                }
                else
                {
                    // v0.60.11: drag-out. Exhibition flag protected the player from
                    // resurrection consumption (HP=1 restore in HandlePlayerDeath).
                    // Apply the lighter penalty: 5% of current XP + forfeit Fame
                    // earned this run + extra -5 Fame for the loss itself.
                    long xpLost = (long)(currentPlayer.Experience * GameConfig.GauntletDragoutXPLossPercent);
                    int fameLost = fameEarnedThisRun + GameConfig.GauntletDragoutFameLossPenalty;
                    currentPlayer.Experience = Math.Max(0, currentPlayer.Experience - xpLost);
                    currentPlayer.Fame = Math.Max(0, currentPlayer.Fame - fameLost);

                    terminal.WriteLine(Loc.Get("anchor_road.collapse_arena"));
                    terminal.SetColor("dark_gray");
                    terminal.WriteLine(Loc.Get("anchor_road.gauntlet_recovered"));
                    if (xpLost > 0)
                    {
                        terminal.SetColor("yellow");
                        terminal.WriteLine(Loc.Get("anchor_road.gauntlet_xp_loss", $"{xpLost:N0}"));
                    }
                    if (fameLost > 0)
                    {
                        terminal.SetColor("yellow");
                        terminal.WriteLine(Loc.Get("anchor_road.gauntlet_fame_loss", fameLost));
                    }
                }
                break;
            }
        }

        // Final summary
        terminal.WriteLine("");
        WriteSectionHeader(Loc.Get("anchor_road.gauntlet_summary"), "bright_cyan");
        terminal.SetColor("white");
        terminal.Write(Loc.Get("anchor_road.waves_survived"));
        if (wavesCompleted >= GameConfig.GauntletWaveCount)
            terminal.SetColor("bright_yellow");
        else if (wavesCompleted >= 5)
            terminal.SetColor("bright_green");
        else
            terminal.SetColor("yellow");
        terminal.WriteLine($"{wavesCompleted}/{GameConfig.GauntletWaveCount}");
        terminal.SetColor("white");
        terminal.WriteLine(Loc.Get("anchor_road.gold_earned", $"{totalGoldEarned:N0}"));
        terminal.WriteLine(Loc.Get("anchor_road.xp_earned", $"{totalXPEarned:N0}"));

        terminal.WriteLine("");
        terminal.SetColor("darkgray");
        terminal.WriteLine(Loc.Get("ui.press_enter"));
        await terminal.ReadKeyAsync();
    }

    // ====================================================================================
    // v0.60.11 Anchor Road Gauntlet helpers -- champion encounters, ambiance, surrender.
    // ====================================================================================

    /// <summary>Themed warmup-wave announcement. Random pick from the WarmupWaveFlavor pool
    /// in GauntletChampionData. Waves 1-3 use this; the monster is still a real
    /// MonsterGenerator spawn but framed as a condemned criminal / escaped slave / dire
    /// beast / drunk gladiator / etc., delivering the "opening acts" feel.</summary>
    private void AnnounceWarmupWave(Monster monster)
    {
        // Warmup flavor lives in loc keys gauntlet.warmup.0..N so it renders in the session
        // language (the English array is the source/fallback). Player report: warmup entry
        // texts ("a dire beast is released", "an unnamed sellsword") showed in English.
        int flavorCount = UsurperRemake.Data.GauntletChampionData.WarmupWaveFlavor.Length;
        string line = Loc.Get($"gauntlet.warmup.{random.Next(flavorCount)}");

        terminal.SetColor("white");
        terminal.WriteLine(line);
        terminal.WriteLine("");
        terminal.SetColor("bright_cyan");
        terminal.Write(Loc.Get("anchor_road.opponent_label"));
        terminal.WriteLine($"{monster.Name} {Loc.Get("gauntlet.level_suffix", monster.Level)}");
    }

    /// <summary>Multi-line champion entrance theater with timing. Reads the herald's
    /// announcement, prints the god-lore reveal in italics, and shows the crowd's reaction.
    /// 600ms beat between each line so the player can read.</summary>
    private async Task AnnounceChampionEntrance(
        UsurperRemake.Data.GauntletChampionData.GauntletChampion champion, Monster monster)
    {
        terminal.SetColor("bright_yellow");
        foreach (var line in champion.LocEntrance())
        {
            terminal.WriteLine(line);
            await Task.Delay(600);
        }
        terminal.WriteLine("");

        // Lore line: italicized via dark color, leading two-space indent for emphasis.
        terminal.SetColor("dark_gray");
        terminal.WriteLine($"  {champion.LocLore()}");
        terminal.WriteLine("");
        await Task.Delay(800);

        // Crowd reaction (sets the mood for this specific fight).
        terminal.SetColor("white");
        terminal.WriteLine(champion.LocCrowd());
        terminal.WriteLine("");
        await Task.Delay(600);

        // Stat banner: the champion's title, the patron god, the level.
        terminal.SetColor("bright_red");
        terminal.WriteLine($"  {champion.Name}, {champion.Title} {Loc.Get("gauntlet.level_suffix", monster.Level)}");
        terminal.SetColor("dark_gray");
        terminal.WriteLine($"  {Loc.Get("gauntlet.patron_label")} {champion.GodPatron}");
    }

    /// <summary>Build a real Monster from a champion data entry, scaled to the player's
    /// current level. Champion's effective level = playerLevel + champion.LevelBonus;
    /// stats scale via MonsterGenerator at that level, then we multiply HP/ATK/DEF by the
    /// champion's role-specific multipliers and override the name/title/abilities.</summary>
    private Monster SpawnChampionMonster(
        UsurperRemake.Data.GauntletChampionData.GauntletChampion champion, long playerLevel)
    {
        int effLevel = (int)Math.Max(1, Math.Min(100, playerLevel + champion.LevelBonus));
        // v0.61.6: generate as a NON-boss base. The champion's own HpMultiplier /
        // AttackMultiplier / DefenseMultiplier ARE the boss-tier scaling (per the
        // v0.60.11 design: effective level + role-specific multipliers). Generating
        // with isBoss:true previously stacked the 2.8x boss-tier HP multiplier on
        // top of the champion's, so Vargash (1.8x) became base * 2.8 * 1.8 = ~5x and
        // the Nameless Tyrant (4.0x) became ~11.2x -- a 13-round attrition slog that
        // killed even a Lv.55 Barbarian who takes zero damage from everything else.
        // We set monster.IsBoss = true AFTER generation so the boss combat behaviors
        // (phase transitions, last-stand cap, boss AI) still fire without the HP
        // double-multiply. Player report: Quent, Lv.55 Barbarian, lost to the first
        // and weakest champion despite one-shotting all regular content.
        var monster = MonsterGenerator.GenerateMonster(effLevel, isBoss: false, isMiniBoss: false, random);

        monster.Name = champion.Name;
        monster.MonsterColor = "bright_yellow";
        monster.HP = (long)(monster.HP * champion.HpMultiplier);
        monster.MaxHP = (long)(monster.MaxHP * champion.HpMultiplier);
        monster.Strength = (int)(monster.Strength * champion.AttackMultiplier);
        monster.Defence = (int)(monster.Defence * champion.DefenseMultiplier);
        monster.ArmPow = (int)(monster.ArmPow * champion.DefenseMultiplier);

        // Flag as boss so phase transitions, the last-stand cap, and boss-targeted
        // combat logic still treat the champion as a boss -- just without the
        // boss-tier stat multipliers that the champion multipliers already supply.
        monster.IsBoss = true;

        // Override the random ability roll with the champion's themed kit.
        if (champion.SpecialAbilities != null && champion.SpecialAbilities.Length > 0)
        {
            monster.SpecialAbilities.Clear();
            foreach (var ab in champion.SpecialAbilities)
                monster.SpecialAbilities.Add(ab);
        }

        return monster;
    }

    /// <summary>Print one random crowd-ambiance line between fights, in dark gray italics.
    /// Keeps the arena feeling alive between confrontations.</summary>
    private void PrintGauntletCrowdAmbiance()
    {
        int count = UsurperRemake.Data.GauntletChampionData.CrowdAmbiance.Length;
        terminal.SetColor("dark_gray");
        terminal.WriteLine("");
        terminal.WriteLine($"  {UsurperRemake.Data.GauntletChampionData.GetLocalizedCrowdAmbiance(random.Next(count))}");
        terminal.WriteLine("");
    }

    /// <summary>Prompt the player to surrender between fights. Surrender is a clean exit:
    /// no real-death roll, no XP/Fame penalty, no resurrection consumed. The player keeps
    /// whatever gold/XP/Fame they earned in waves already completed. Returns true if
    /// the player chose to yield.</summary>
    private async Task<bool> OfferGauntletSurrender(int nextWave, int wavesCompleted, long totalGold, long totalXp, int fameEarned)
    {
        terminal.WriteLine("");
        terminal.SetColor("yellow");
        terminal.WriteLine(Loc.Get("anchor_road.surrender_prompt_header", nextWave - 1, GameConfig.GauntletWaveCount));
        terminal.SetColor("dark_gray");
        terminal.WriteLine(Loc.Get("anchor_road.surrender_prompt_keep", $"{totalGold:N0}", $"{totalXp:N0}", fameEarned));
        terminal.WriteLine("");
        terminal.SetColor("white");
        terminal.Write(Loc.Get("anchor_road.surrender_prompt_question"));
        string? response = await terminal.ReadLineAsync();
        return response != null && GameConfig.IsAffirmative(response);
    }

    /// <summary>Generate and award the champion's themed equipment drop. Stats scale to
    /// the player's current level so a Lv 10 player gets a Lv 10 version, a Lv 80 player
    /// gets a Lv 80 version. Same item name and slot regardless of tier; only stats differ.</summary>
    private void GenerateAndAwardChampionDrop(
        UsurperRemake.Data.GauntletChampionData.GauntletChampion champion, long playerLevel, bool isShowpiece)
    {
        // Map slot hint to EquipmentSlot for the drop.
        EquipmentSlot slot = champion.Drop.SlotHint switch
        {
            "MainHand" => EquipmentSlot.MainHand,
            "OffHand"  => EquipmentSlot.OffHand,
            "Body"     => EquipmentSlot.Body,
            "Head"     => EquipmentSlot.Head,
            "Legs"     => EquipmentSlot.Legs,
            "Feet"     => EquipmentSlot.Feet,
            "Arms"     => EquipmentSlot.Arms,
            "Hands"    => EquipmentSlot.Hands,
            "Waist"    => EquipmentSlot.Waist,
            "Cloak"    => EquipmentSlot.Cloak,
            "Face"     => EquipmentSlot.Face,
            "Neck"     => EquipmentSlot.Neck,
            "LFinger"  => EquipmentSlot.LFinger,
            "RFinger"  => EquipmentSlot.RFinger,
            _          => EquipmentSlot.MainHand
        };

        bool isWeapon = slot == EquipmentSlot.MainHand || slot == EquipmentSlot.OffHand;
        bool isAccessory = slot == EquipmentSlot.Neck || slot == EquipmentSlot.LFinger || slot == EquipmentSlot.RFinger;

        // Top-tier rarity earned at this player level. The "showpiece" champion drop
        // (one random pick per run) lands at this rarity; the other six drops land one
        // or two notches below it -- so a full clear gives one chase-tier piece and six
        // mixed mid-tier pieces, not seven top-tier pieces.
        EquipmentRarity topRarity = playerLevel >= 80 ? EquipmentRarity.Artifact
                                  : playerLevel >= 60 ? EquipmentRarity.Legendary
                                  : playerLevel >= 40 ? EquipmentRarity.Epic
                                  : playerLevel >= 20 ? EquipmentRarity.Rare
                                  : EquipmentRarity.Uncommon;

        EquipmentRarity rarity;
        if (isShowpiece)
        {
            rarity = topRarity;
        }
        else
        {
            // Roll between one and two notches below the top tier (50/50). Floors at
            // Common so even a Lv 5 Hopeful run's filler drops have a defined rarity.
            var rarityScale = new[]
            {
                EquipmentRarity.Common,
                EquipmentRarity.Uncommon,
                EquipmentRarity.Rare,
                EquipmentRarity.Epic,
                EquipmentRarity.Legendary,
                EquipmentRarity.Artifact
            };
            int topIdx = System.Array.IndexOf(rarityScale, topRarity);
            int notchesDown = random.Next(1, 3); // 1 or 2
            int rolledIdx = System.Math.Max(0, topIdx - notchesDown);
            rarity = rarityScale[rolledIdx];
        }

        var drop = new Equipment
        {
            Name = champion.Drop.ItemName,
            Slot = slot,
            Rarity = rarity,
            MinLevel = (int)Math.Max(1, playerLevel - 2),
            Description = champion.LocDropFlavor()
        };

        // Stat power matches the rarity label, using the same `basePower * levelScale *
        // rarityMult` curve LootGenerator applies to regular dungeon drops. Pre-fix the
        // formula was `playerLevel * 10` flat (Lv 22 = WP:198 / AP:198), which delivered
        // Artifact-tier stats regardless of the rarity label -- 2-4x too powerful at
        // every level. Now an arena drop reads roughly like a top-rarity equivalent of
        // a typical dungeon template for the same slot.
        float rarityMult = rarity switch
        {
            EquipmentRarity.Uncommon  => 1.3f,
            EquipmentRarity.Rare      => 1.7f,
            EquipmentRarity.Epic      => 2.2f,
            EquipmentRarity.Legendary => 3.0f,
            EquipmentRarity.Artifact  => 4.0f,
            _ => 1.0f
        };
        float levelScale = 1.0f + (playerLevel / 80.0f);

        // Slot-appropriate base power, calibrated against representative LootGenerator
        // templates so arena drops live in the same value space as dungeon loot.
        int slotBasePower = slot switch
        {
            EquipmentSlot.MainHand or EquipmentSlot.OffHand           => 35,   // ~Broadsword
            EquipmentSlot.Body                                        => 28,
            EquipmentSlot.Head                                        => 18,
            EquipmentSlot.Face                                        => 14,
            EquipmentSlot.Cloak or EquipmentSlot.Waist                => 10,
            EquipmentSlot.Arms  or EquipmentSlot.Legs
              or EquipmentSlot.Hands or EquipmentSlot.Feet            => 14,
            EquipmentSlot.Neck  or EquipmentSlot.LFinger
              or EquipmentSlot.RFinger                                =>  8,
            _ => 15
        };
        int power = (int)Math.Max(5, slotBasePower * levelScale * rarityMult);

        // Per-slot stat distribution. Mirrors LootGenerator proportions roughly.
        if (isWeapon)
        {
            drop.WeaponPower   = power;
            drop.StrengthBonus = Math.Max(1, power / 6);
        }
        else if (isAccessory)
        {
            drop.StrengthBonus     = Math.Max(1, power / 2);
            drop.ConstitutionBonus = Math.Max(1, power / 2);
        }
        else // Armor
        {
            drop.ArmorClass        = power;
            drop.DefenceBonus      = Math.Max(1, power / 4);
            drop.ConstitutionBonus = Math.Max(1, power / 8);
        }

        drop.Id = EquipmentDatabase.RegisterDynamic(drop);

        // Hand it to the player. Combat already showed the champion's death; this fires
        // immediately after the wave-complete reward print, in cyan to stand out.
        currentPlayer.Inventory.Add(currentPlayer.ConvertEquipmentToLegacyItem(drop));
        terminal.SetColor("bright_cyan");
        terminal.WriteLine(Loc.Get("anchor_road.champion_drop", drop.Name));
        terminal.SetColor("dark_gray");
        terminal.WriteLine($"  {drop.Description}");
    }

    /// <summary>
    /// Claim town for your team
    /// </summary>
    /// <summary>
    /// v0.57.10: shared helper to persist a turf-control change to world_state
    /// in online mode. Called immediately after any mutation of CTurf flags
    /// (Gang War win, ghost takeover, Claim Town, Abandon Town) so the shared
    /// NPC snapshot in `world_state` reflects the new controller before the
    /// player can log out. Without this, the authoritative `LoadSharedNPCs`
    /// call on the next login would overwrite the player's new CTurf flag
    /// with the stale NPC team's.
    /// </summary>
    private async Task PersistTurfTransfer()
    {
        if (!UsurperRemake.BBS.DoorMode.IsOnlineMode) return;
        if (OnlineStateManager.Instance == null) return;
        try
        {
            await OnlineStateManager.Instance.SaveAllSharedState();
        }
        catch (Exception ex)
        {
            DebugLogger.Instance.LogError("TURF", $"SaveAllSharedState after turf transfer failed: {ex.Message}");
        }
    }

    private async Task ClaimTown()
    {
        terminal.ClearScreen();
        WriteBoxHeader(Loc.Get("anchor_road.claim_town_header"), "bright_yellow");
        terminal.WriteLine("");

        if (string.IsNullOrEmpty(currentPlayer.Team))
        {
            terminal.SetColor("red");
            terminal.WriteLine(Loc.Get("anchor_road.no_team_claim"));
            terminal.WriteLine("");
            terminal.SetColor("darkgray");
            terminal.WriteLine(Loc.Get("ui.press_enter"));
            await terminal.ReadKeyAsync();
            return;
        }

        if (currentPlayer.CTurf)
        {
            terminal.SetColor("yellow");
            terminal.WriteLine(Loc.Get("anchor_road.already_controls"));
            terminal.WriteLine("");
            terminal.SetColor("darkgray");
            terminal.WriteLine(Loc.Get("ui.press_enter"));
            await terminal.ReadKeyAsync();
            return;
        }

        // Check if anyone controls the town
        var turfController = GetTurfControllerName();

        if (string.IsNullOrEmpty(turfController))
        {
            // Nobody controls - easy claim
            terminal.SetColor("white");
            terminal.WriteLine(Loc.Get("anchor_road.no_controller"));
            terminal.WriteLine("");
            terminal.SetColor("cyan");
            terminal.Write(Loc.Get("anchor_road.claim_prompt"));
            terminal.SetColor("white");
            string claimResponse = await terminal.ReadLineAsync();

            if (GameConfig.IsAffirmative(claimResponse))
            {
                currentPlayer.CTurf = true;
                currentPlayer.TeamRec = 0;

                // Set for all team NPCs too
                var allNPCs = NPCSpawnSystem.Instance.ActiveNPCs;
                foreach (var npc in allNPCs.Where(n => n.Team == currentPlayer.Team))
                {
                    npc.CTurf = true;
                }

                terminal.SetColor("bright_green");
                terminal.WriteLine("");
                terminal.WriteLine(Loc.Get("anchor_road.town_claimed"));
                terminal.WriteLine(Loc.Get("anchor_road.rule_wisely"));

                NewsSystem.Instance.Newsy(true, $"{currentPlayer.Team} has taken control of the town!");

                // v0.57.10: persist unopposed claim to world_state immediately
                await PersistTurfTransfer();
            }
        }
        else
        {
            terminal.SetColor("yellow");
            terminal.WriteLine(Loc.Get("anchor_road.town_controlled_info", turfController));
            terminal.WriteLine("");
            terminal.SetColor("white");
            terminal.WriteLine(Loc.Get("anchor_road.must_defeat_gang_war"));
            terminal.WriteLine(Loc.Get("anchor_road.use_gang_war"));
        }

        terminal.WriteLine("");
        terminal.SetColor("darkgray");
        terminal.WriteLine(Loc.Get("ui.press_enter"));
        await terminal.ReadKeyAsync();
    }

    /// <summary>
    /// Flee/abandon town control
    /// </summary>
    private async Task FleeTownControl()
    {
        if (!currentPlayer.CTurf)
        {
            terminal.SetColor("red");
            terminal.WriteLine("");
            terminal.WriteLine(Loc.Get("anchor_road.no_town_control"));
            terminal.WriteLine("");
            await Task.Delay(2000);
            return;
        }

        terminal.WriteLine("");
        terminal.SetColor("yellow");
        terminal.WriteLine(Loc.Get("ui.confirm_abandon_control"));
        terminal.WriteLine(Loc.Get("anchor_road.leave_town_open"));
        terminal.Write(Loc.Get("anchor_road.abandon_prompt"));
        terminal.SetColor("white");
        string response = await terminal.ReadLineAsync();

        if (GameConfig.IsAffirmative(response))
        {
            // Remove turf control from all team members
            currentPlayer.CTurf = false;

            var allNPCs = NPCSpawnSystem.Instance.ActiveNPCs;
            foreach (var npc in allNPCs.Where(n => n.Team == currentPlayer.Team))
            {
                npc.CTurf = false;
            }

            terminal.SetColor("bright_green");
            terminal.WriteLine("");
            terminal.WriteLine(Loc.Get("anchor_road.abandoned_control"));
            terminal.WriteLine(Loc.Get("anchor_road.town_free"));

            NewsSystem.Instance.Newsy(true, $"{currentPlayer.Team} abandoned control of the town!");

            // v0.57.10: persist abandonment to world_state immediately
            await PersistTurfTransfer();
        }
        else
        {
            terminal.SetColor("white");
            terminal.WriteLine(Loc.Get("anchor_road.control_maintained"));
        }

        terminal.WriteLine("");
        terminal.SetColor("darkgray");
        terminal.WriteLine(Loc.Get("ui.press_enter"));
        await terminal.ReadKeyAsync();
    }

    /// <summary>
    /// Navigate to prison grounds
    /// </summary>
    private async Task NavigateToPrisonGrounds()
    {
        terminal.ClearScreen();
        WriteBoxHeader(Loc.Get("anchor_road.prison_header"), "darkgray");
        terminal.WriteLine("");

        terminal.SetColor("white");
        terminal.WriteLine(Loc.Get("anchor_road.approach_prison"));
        terminal.WriteLine(Loc.Get("anchor_road.guards_patrol"));
        terminal.WriteLine("");

        terminal.SetColor("cyan");
        terminal.WriteLine(Loc.Get("anchor_road.prison_options"));
        if (IsScreenReader)
        {
            WriteSRMenuOption("J", Loc.Get("anchor_road.jailbreak"));
            WriteSRMenuOption("V", Loc.Get("anchor_road.view_prisoners"));
            WriteSRMenuOption("L", Loc.Get("ui.leave"));
        }
        else
        {
            WriteMenuOption("J", Loc.Get("anchor_road.menu_jailbreak"));
            WriteMenuOption("V", Loc.Get("anchor_road.menu_view_prisoners"));
            WriteMenuOption("L", Loc.Get("ui.leave"));
        }
        terminal.WriteLine("");

        terminal.SetColor("cyan");
        terminal.Write(Loc.Get("ui.choice"));
        terminal.SetColor("white");
        string input = await terminal.ReadLineAsync();

        if (!string.IsNullOrEmpty(input))
        {
            char prisonChoice = char.ToUpperInvariant(input[0]);
            switch (prisonChoice)
            {
                case 'J':
                    await AttemptJailbreak();
                    break;

                case 'V':
                    await ViewPrisoners();
                    break;
            }
        }
    }

    private async Task AttemptJailbreak()
    {
        terminal.WriteLine("");
        terminal.SetColor("red");
        terminal.WriteLine(Loc.Get("anchor_road.jailbreak_dangerous"));
        terminal.WriteLine(Loc.Get("anchor_road.end_up_in_prison"));
        terminal.WriteLine("");

        terminal.SetColor("cyan");
        terminal.Write(Loc.Get("anchor_road.proceed_jailbreak"));
        terminal.SetColor("white");
        string response = await terminal.ReadLineAsync();

        if (GameConfig.IsAffirmative(response))
        {
            int successChance = 30 + currentPlayer.Level + (int)(currentPlayer.Agility / 5);
            bool success = random.Next(100) < successChance;

            if (success)
            {
                terminal.SetColor("bright_green");
                terminal.WriteLine("");
                terminal.WriteLine(Loc.Get("anchor_road.sneak_past_guards"));
                terminal.WriteLine(Loc.Get("anchor_road.help_escape"));
                terminal.WriteLine("");
                terminal.WriteLine(Loc.Get("anchor_road.prisoner_thanks"));

                AlignmentSystem.Instance.ChangeAlignment(currentPlayer, 50, isGood: true, "anchor_road.prison_escape"); // v0.57.12: paired movement
                NewsSystem.Instance.Newsy(true, $"{currentPlayer.DisplayName} orchestrated a daring prison escape!");
            }
            else
            {
                terminal.SetColor("red");
                terminal.WriteLine("");
                terminal.WriteLine(Loc.Get("anchor_road.caught"));
                terminal.WriteLine(Loc.Get("anchor_road.guards_spotted"));

                // Damage and possible imprisonment
                long damage = currentPlayer.MaxHP / 5;
                currentPlayer.HP = Math.Max(1, currentPlayer.HP - damage);
                AlignmentSystem.Instance.ChangeAlignment(currentPlayer, 25, isGood: false, "anchor_road.escape_caught"); // v0.57.12: paired movement

                terminal.WriteLine(Loc.Get("anchor_road.barely_escaped", damage));
            }
        }

        terminal.WriteLine("");
        terminal.SetColor("darkgray");
        terminal.WriteLine(Loc.Get("ui.press_enter"));
        await terminal.ReadKeyAsync();
    }

    private async Task ViewPrisoners()
    {
        terminal.WriteLine("");
        terminal.SetColor("white");
        terminal.WriteLine(Loc.Get("anchor_road.peer_through_bars"));
        terminal.WriteLine("");

        // Get imprisoned NPCs (those with prison status)
        var allNPCs = NPCSpawnSystem.Instance.ActiveNPCs;
        var prisoners = allNPCs
            .Where(n => n.CurrentLocation == "Prison" || n.PrisonsLeft > 0)
            .Take(5)
            .ToList();

        if (prisoners.Count == 0)
        {
            terminal.SetColor("yellow");
            terminal.WriteLine(Loc.Get("anchor_road.cells_empty"));
        }
        else
        {
            terminal.SetColor("cyan");
            terminal.WriteLine(Loc.Get("anchor_road.prisoners_label"));
            if (!IsScreenReader)
            {
                terminal.SetColor("darkgray");
                terminal.WriteLine(new string('─', 40));
            }

            foreach (var prisoner in prisoners)
            {
                terminal.SetColor("white");
                terminal.WriteLine(Loc.Get("anchor_road.prisoner_info", prisoner.DisplayName, prisoner.Level, GameConfig.GetLocalizedClassName(prisoner.Class)));
            }
        }

        terminal.WriteLine("");
        terminal.SetColor("darkgray");
        terminal.WriteLine(Loc.Get("ui.press_enter"));
        await terminal.ReadKeyAsync();
    }

    /// <summary>
    /// v0.62.x "Light and Dark" Phase 4 (Mercenary/Sellsword job board). The freelance contract
    /// surface: factions post jobs, the player claims and completes them without swearing an
    /// oath. Faction standing climbs naturally through the existing cascade so a long-time
    /// freelancer eventually becomes eligible for full membership if they want it. No alignment
    /// gate -- this is the explicit yin/yang centerline for the alignment rework.
    /// </summary>
    private async Task ShowSellswordHall()
    {
        // Per-player on-demand board refresh: if we haven't refreshed today, do it now. Mirrors
        // the LastMercBoardRefreshUtc check pattern; default DateTime.MinValue forces a refresh
        // on first-ever visit so a v0.62.x save loaded for the first time gets a populated board.
        if (currentPlayer.LastMercBoardRefreshUtc.Date < DateTime.UtcNow.Date)
        {
            QuestSystem.RefreshMercBoard(currentPlayer);
        }

        bool stayInHall = true;
        while (stayInHall)
        {
            terminal.ClearScreen();
            WriteBoxHeader(Loc.Get("merc.hall_header"), "bright_magenta");
            terminal.WriteLine("");
            terminal.SetColor("white");
            terminal.WriteLine(Loc.Get("merc.hall_desc_1"));
            terminal.WriteLine(Loc.Get("merc.hall_desc_2"));
            terminal.WriteLine("");

            // Show player's current merc rank + counts.
            var rank = UsurperRemake.Systems.AlignmentSystem.Instance.GetMercRank(currentPlayer);
            string rankName = rank == UsurperRemake.Systems.AlignmentSystem.MercRank.None
                ? Loc.Get("merc.rank_none")
                : Loc.Get($"merc.rank_{rank.ToString().ToLowerInvariant()}");
            terminal.SetColor("cyan");
            terminal.WriteLine(Loc.Get("merc.hall_your_rank", rankName, currentPlayer.MercContractsCompleted));
            int remainingToday = Math.Max(0, GameConfig.MaxMercContractsPerDay - currentPlayer.MercContractsClaimedToday);
            terminal.SetColor("gray");
            terminal.WriteLine(Loc.Get("merc.hall_daily_remaining", remainingToday, GameConfig.MaxMercContractsPerDay));
            terminal.WriteLine("");

            // Build the available list (numbered across all factions).
            var available = QuestSystem.GetAvailableMercContracts(currentPlayer, null);

            // Available contracts list.
            terminal.SetColor("cyan");
            terminal.WriteLine(Loc.Get("merc.board_available_header"));
            terminal.SetColor("white");
            if (available.Count == 0)
            {
                terminal.SetColor("gray");
                terminal.WriteLine($"  {Loc.Get("merc.board_empty")}");
            }
            else
            {
                for (int i = 0; i < available.Count; i++)
                {
                    var q = available[i];
                    string factionTag = FormatMercFactionTag(q.IssuingFaction);
                    string title = q.GetDisplayTitle();
                    terminal.SetColor("darkgray");
                    terminal.Write("  [");
                    terminal.SetColor("bright_yellow");
                    terminal.Write((i + 1).ToString());
                    terminal.SetColor("darkgray");
                    terminal.Write("] ");
                    terminal.SetColor("bright_white");
                    terminal.Write(factionTag);
                    terminal.SetColor("white");
                    terminal.WriteLine(" " + title);
                }
            }
            terminal.WriteLine("");

            // Claimed (in-progress) contracts.
            var claimed = QuestSystem.GetClaimedMercContracts(currentPlayer);
            if (claimed.Count > 0)
            {
                terminal.SetColor("cyan");
                terminal.WriteLine(Loc.Get("merc.board_claimed_header"));
                terminal.SetColor("white");
                foreach (var q in claimed)
                {
                    string factionTag = FormatMercFactionTag(q.IssuingFaction);
                    string title = q.GetDisplayTitle();
                    // Progress summary from the first objective (slice 1 contracts are single-objective).
                    string progress = "";
                    if (q.Objectives != null && q.Objectives.Count > 0)
                    {
                        var o = q.Objectives[0];
                        bool done = o.CurrentProgress >= o.RequiredProgress;
                        progress = done
                            ? Loc.Get("merc.board_claimed_ready")
                            : Loc.Get("merc.board_claimed_progress", o.CurrentProgress, o.RequiredProgress);
                    }
                    terminal.SetColor("gray");
                    terminal.WriteLine($"    {factionTag} {title}  {progress}");
                }
                terminal.WriteLine("");
            }

            // Action prompt.
            terminal.SetColor("white");
            terminal.WriteLine(Loc.Get("merc.hall_prompt_options"));
            string input = (await terminal.GetInput(Loc.Get("ui.your_choice"))).Trim().ToUpperInvariant();

            if (input == "Q" || input == "")
            {
                stayInHall = false;
                continue;
            }

            if (input == "T")
            {
                await TurnInReadyMercContracts(claimed);
                continue;
            }

            if (int.TryParse(input, out int contractNum) && contractNum >= 1 && contractNum <= available.Count)
            {
                await ShowMercContractDetails(available[contractNum - 1]);
                continue;
            }

            terminal.SetColor("red");
            terminal.WriteLine(Loc.Get("merc.hall_invalid_choice"));
            await Task.Delay(900);
        }
    }

    private string FormatMercFactionTag(UsurperRemake.Systems.Faction? faction)
    {
        if (!faction.HasValue) return "";
        string key = faction.Value switch
        {
            UsurperRemake.Systems.Faction.TheCrown => "merc.tag_crown",
            UsurperRemake.Systems.Faction.TheShadows => "merc.tag_shadows",
            UsurperRemake.Systems.Faction.TheFaith => "merc.tag_faith",
            _ => "merc.tag_unknown"
        };
        return Loc.Get(key);
    }

    private async Task ShowMercContractDetails(Quest quest)
    {
        terminal.ClearScreen();
        WriteBoxHeader(Loc.Get("merc.contract_details_header"), "bright_magenta");
        terminal.WriteLine("");

        terminal.SetColor("bright_white");
        terminal.WriteLine($"  {FormatMercFactionTag(quest.IssuingFaction)} {quest.GetDisplayTitle()}");
        terminal.WriteLine("");
        terminal.SetColor("gray");
        terminal.WriteLine($"  {Loc.Get("merc.contract_issued_by", quest.GetDisplayInitiator())}");
        terminal.WriteLine("");
        terminal.SetColor("white");
        terminal.WriteLine($"  {quest.GetDisplayComment()}");
        terminal.WriteLine("");

        if (quest.Objectives != null && quest.Objectives.Count > 0)
        {
            terminal.SetColor("cyan");
            terminal.WriteLine(Loc.Get("merc.contract_objective_header"));
            terminal.SetColor("white");
            foreach (var o in quest.Objectives)
            {
                terminal.WriteLine($"    - {o.GetDisplayDescription()}");
            }
            terminal.WriteLine("");
        }

        // Payout preview (rank-adjusted).
        int rankIdx = (int)UsurperRemake.Systems.AlignmentSystem.Instance.GetMercRank(currentPlayer);
        float rankMul = rankIdx >= 0 && rankIdx < GameConfig.MercRankPayMultiplier.Length
            ? GameConfig.MercRankPayMultiplier[rankIdx]
            : 1.0f;
        long previewGold = (long)(quest.BountyGold * rankMul);
        terminal.SetColor("yellow");
        terminal.WriteLine(Loc.Get("merc.contract_reward_line", previewGold));
        terminal.SetColor("gray");
        terminal.WriteLine(Loc.Get("merc.contract_penalty_line", quest.Penalty));
        terminal.WriteLine("");

        // Daily-cap check.
        if (currentPlayer.MercContractsClaimedToday >= GameConfig.MaxMercContractsPerDay)
        {
            terminal.SetColor("bright_red");
            terminal.WriteLine($"  {Loc.Get("merc.contract_cap_reached", GameConfig.MaxMercContractsPerDay)}");
            terminal.WriteLine("");
            terminal.SetColor("gray");
            terminal.WriteLine(Loc.Get("ui.press_enter"));
            await terminal.ReadKeyAsync();
            return;
        }

        terminal.SetColor("white");
        string choice = (await terminal.GetInput(Loc.Get("merc.contract_claim_prompt"))).Trim().ToUpperInvariant();
        if (GameConfig.IsAffirmative(choice))
        {
            quest.Occupier = currentPlayer.Name2;
            quest.OccupierRace = currentPlayer.Race;
            quest.OccupierSex = (byte)currentPlayer.Sex;
            quest.OccupiedDays = 0;
            currentPlayer.MercContractsClaimedToday++;

            terminal.SetColor("bright_green");
            terminal.WriteLine($"  {Loc.Get("merc.contract_claimed_msg")}");
            await Task.Delay(1500);
        }
    }

    private async Task TurnInReadyMercContracts(List<Quest> claimedContracts)
    {
        // Filter to contracts that meet objective completion.
        var ready = claimedContracts.Where(q => q.Objectives != null
            && q.Objectives.Count > 0
            && q.Objectives.All(o => o.IsOptional || o.CurrentProgress >= o.RequiredProgress)).ToList();

        if (ready.Count == 0)
        {
            terminal.SetColor("yellow");
            terminal.WriteLine($"  {Loc.Get("merc.turnin_nothing_ready")}");
            await Task.Delay(1500);
            return;
        }

        // Pick one: numbered list if more than 1, otherwise auto.
        Quest pick = ready[0];
        if (ready.Count > 1)
        {
            terminal.ClearScreen();
            WriteBoxHeader(Loc.Get("merc.turnin_header"), "bright_magenta");
            terminal.WriteLine("");
            terminal.SetColor("white");
            terminal.WriteLine(Loc.Get("merc.turnin_choose"));
            terminal.WriteLine("");
            for (int i = 0; i < ready.Count; i++)
            {
                terminal.WriteLine($"  [{i + 1}] {FormatMercFactionTag(ready[i].IssuingFaction)} {ready[i].GetDisplayTitle()}");
            }
            terminal.WriteLine("");
            string input = (await terminal.GetInput(Loc.Get("ui.your_choice"))).Trim();
            if (!int.TryParse(input, out int idx) || idx < 1 || idx > ready.Count)
            {
                return;
            }
            pick = ready[idx - 1];
        }

        var (ok, gold, standing, reason) = QuestSystem.CompleteMercContract(currentPlayer, pick);
        if (!ok)
        {
            terminal.SetColor("red");
            terminal.WriteLine($"  {Loc.Get("merc.turnin_failed", reason)}");
            await Task.Delay(1800);
            return;
        }

        terminal.ClearScreen();
        WriteBoxHeader(Loc.Get("merc.turnin_complete_header"), "bright_green");
        terminal.WriteLine("");
        terminal.SetColor("bright_yellow");
        terminal.WriteLine($"  {Loc.Get("merc.turnin_gold_awarded", gold)}");
        if (standing > 0)
        {
            terminal.SetColor("cyan");
            terminal.WriteLine($"  {Loc.Get("merc.turnin_standing_awarded", standing, FormatMercFactionTag(pick.IssuingFaction))}");
        }
        terminal.SetColor("gray");
        terminal.WriteLine($"  {Loc.Get("merc.turnin_contracts_completed", currentPlayer.MercContractsCompleted)}");

        // Rank-up flavor on threshold crossings.
        int newRank = (int)UsurperRemake.Systems.AlignmentSystem.Instance.GetMercRank(currentPlayer);
        var thresholds = GameConfig.MercRankContractsRequired;
        if (newRank > 0 && currentPlayer.MercContractsCompleted == thresholds[newRank])
        {
            string newRankName = Loc.Get($"merc.rank_{((UsurperRemake.Systems.AlignmentSystem.MercRank)newRank).ToString().ToLowerInvariant()}");
            terminal.WriteLine("");
            terminal.SetColor("bright_magenta");
            terminal.WriteLine($"  {Loc.Get("merc.rank_up", newRankName)}");
        }

        terminal.WriteLine("");
        terminal.SetColor("gray");
        terminal.WriteLine(Loc.Get("ui.press_enter"));
        await terminal.ReadKeyAsync();
    }

    #endregion

    #region Utility Methods

    private string GetTurfControllerName()
    {
        // Check if player controls
        if (currentPlayer.CTurf && !string.IsNullOrEmpty(currentPlayer.Team))
        {
            return currentPlayer.Team;
        }

        // Check NPCs
        var allNPCs = NPCSpawnSystem.Instance.ActiveNPCs;
        var controller = allNPCs.FirstOrDefault(n => n.CTurf && !string.IsNullOrEmpty(n.Team));

        return controller?.Team ?? "";
    }

    #endregion

    /// <summary>
    /// Phase 5: emit Anchor Road (challenge hub) menu for the Electron client. Pattern B.
    /// </summary>
    private void EmitElectronEvents()
    {
        var player = GetCurrentPlayer();
        if (player == null) return;

        ElectronBridge.EmitLocation(
            name: Loc.Get("anchor_road.header_title"),
            description: "",
            timeOfDay: "");

        bool isManaClass = player is Player p && p.IsManaClass;
        ElectronBridge.EmitStats(
            hp: player.HP, maxHp: player.MaxHP,
            mana: isManaClass ? player.Mana : 0, maxMana: isManaClass ? player.MaxMana : 0,
            stamina: isManaClass ? 0 : player.Stamina, maxStamina: isManaClass ? 0 : player.BaseStamina,
            gold: player.Gold, level: player.Level,
            className: player.ClassName, raceName: player.Race.ToString(),
            playerName: player.DisplayName);

        // Labels reuse the existing localized SR/visual menu keys so the Electron client renders
        // in the player's language (was hardcoded English).
        var menu = new List<ElectronBridge.MenuItemData>
        {
            new() { Key = "B", Label = Loc.Get("anchor_road.bounty"), Category = "combat", Icon = "bounty" },
            new() { Key = "G", Label = Loc.Get("anchor_road.gang_war"), Category = "combat", Icon = "gang" },
            new() { Key = "T", Label = Loc.Get("anchor_road.gauntlet"), Category = "combat", Icon = "gauntlet" },
            new() { Key = "C", Label = Loc.Get("anchor_road.claim_town"), Category = "team", Icon = "control" },
            new() { Key = "F", Label = Loc.Get("anchor_road.flee_control"), Category = "team", Icon = "flee" },
            new() { Key = "S", Label = Loc.Get("anchor_road.status"), Category = "info", Icon = "info" },
            new() { Key = "P", Label = Loc.Get("anchor_road.prison"), Category = "navigate", Icon = "prison" },
            new() { Key = "R", Label = Loc.Get("ui.return"), Category = "navigate", Icon = "back" },
        };
        ElectronBridge.EmitMenu(menu);

        EmitNPCsInLocationToElectron();
    }
}
