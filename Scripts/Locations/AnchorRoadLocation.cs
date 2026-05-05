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

    protected override string GetMudPromptName() => "Anchor Road";

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
            var result = await combatEngine.PlayerVsPlayer(currentPlayer, target);

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
                terminal.WriteLine(Loc.Get("anchor_road.face_opponent", enemy.DisplayName, enemy.Level, enemy.Class));
                terminal.WriteLine("");
                await Task.Delay(1000);

                var combatEngine = new CombatEngine(terminal);
                var result = await combatEngine.PlayerVsPlayer(currentPlayer, enemy);

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

        long entryFee = GameConfig.GauntletEntryFeePerLevel * currentPlayer.Level;

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

        if (response?.ToUpper().StartsWith("Y") != true)
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

        terminal.WriteLine("");
        terminal.SetColor("bright_red");
        terminal.WriteLine(Loc.Get("anchor_road.gates_slam"));
        terminal.WriteLine(Loc.Get("anchor_road.crowd_roars"));
        terminal.WriteLine("");
        await Task.Delay(2000);

        int wavesCompleted = 0;
        long totalGoldEarned = 0;
        long totalXPEarned = 0;

        for (int wave = 1; wave <= GameConfig.GauntletWaveCount; wave++)
        {
            // Determine monster level and type
            int monsterLevel;
            bool isBoss = false;
            bool isMiniBoss = false;

            if (wave <= 3)
            {
                monsterLevel = Math.Max(1, currentPlayer.Level - 3 + wave);
            }
            else if (wave <= 6)
            {
                monsterLevel = currentPlayer.Level + wave - 2;
            }
            else if (wave <= 9)
            {
                monsterLevel = currentPlayer.Level + wave;
                isMiniBoss = true;
            }
            else
            {
                monsterLevel = currentPlayer.Level + 10;
                isBoss = true;
            }

            monsterLevel = Math.Max(1, Math.Min(100, monsterLevel));

            var monster = MonsterGenerator.GenerateMonster(monsterLevel, isBoss, isMiniBoss, random);

            terminal.ClearScreen();
            WriteSectionHeader(Loc.Get("anchor_road.wave_header", wave, GameConfig.GauntletWaveCount), "bright_yellow");
            terminal.WriteLine("");

            terminal.SetColor("white");
            terminal.Write(Loc.Get("anchor_road.opponent_label"));
            if (isBoss)
                terminal.SetColor("bright_red");
            else if (isMiniBoss)
                terminal.SetColor("bright_magenta");
            else
                terminal.SetColor("bright_cyan");
            terminal.WriteLine($"{monster.Name} (Level {monster.Level})");

            terminal.SetColor("white");
            terminal.Write(Loc.Get("anchor_road.your_hp"));
            terminal.SetColor(currentPlayer.HP > currentPlayer.MaxHP / 2 ? "bright_green" : "red");
            terminal.WriteLine($"{currentPlayer.HP}/{currentPlayer.MaxHP}");
            terminal.WriteLine("");
            await Task.Delay(1000);

            // Real combat. v0.60.8: mark as exhibition so a wave loss doesn't
            // burn a resurrection -- the entry fee + daily PFight slot are
            // already the cost of admission. HP=1 restore happens inside
            // HandlePlayerDeath; the wave loop ends in the defeat branch
            // below.
            var combatEngine = new CombatEngine(terminal);
            CombatResult result;
            currentPlayer.IsExhibitionCombat = true;
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

                terminal.SetColor("bright_green");
                terminal.WriteLine(Loc.Get("anchor_road.wave_complete", wave, $"{waveGold:N0}", $"{waveXP:N0}"));

                // Wave 10 champion bonus
                if (wave == GameConfig.GauntletWaveCount)
                {
                    long championGold = GameConfig.GauntletChampionGoldPerLevel * currentPlayer.Level;
                    long championXP = GameConfig.GauntletChampionXPPerLevel * currentPlayer.Level;
                    totalGoldEarned += championGold;
                    totalXPEarned += championXP;
                    currentPlayer.Gold += championGold;
                    currentPlayer.Experience += championXP;

                    terminal.WriteLine("");
                    WriteSectionHeader(Loc.Get("anchor_road.gauntlet_champion"), "bright_yellow");
                    terminal.SetColor("bright_yellow");
                    terminal.WriteLine(Loc.Get("anchor_road.champion_bonus", $"{championGold:N0}", $"{championXP:N0}"));

                    AchievementSystem.TryUnlock(currentPlayer, "gauntlet_champion");
                    NewsSystem.Instance.Newsy(true, $"{currentPlayer.DisplayName} conquered The Gauntlet!");
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
                // Player went down or fled -- gauntlet over. v0.60.8:
                // exhibition flag in HandlePlayerDeath already restored HP=1;
                // surface a recovery line so the player understands no
                // resurrection was consumed.
                terminal.SetColor("red");
                terminal.WriteLine("");
                if (result.Outcome == CombatOutcome.PlayerEscaped)
                    terminal.WriteLine(Loc.Get("anchor_road.flee_disgrace"));
                else
                {
                    terminal.WriteLine(Loc.Get("anchor_road.collapse_arena"));
                    terminal.SetColor("dark_gray");
                    terminal.WriteLine(Loc.Get("anchor_road.gauntlet_recovered"));
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

            if (claimResponse?.ToUpper().StartsWith("Y") == true)
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

        if (response?.ToUpper().StartsWith("Y") == true)
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

        if (response?.ToUpper().StartsWith("Y") == true)
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
                terminal.WriteLine(Loc.Get("anchor_road.prisoner_info", prisoner.DisplayName, prisoner.Level, prisoner.Class));
            }
        }

        terminal.WriteLine("");
        terminal.SetColor("darkgray");
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

        var menu = new List<ElectronBridge.MenuItemData>
        {
            new() { Key = "B", Label = "Bounty Hunting", Category = "combat", Icon = "bounty" },
            new() { Key = "G", Label = "Gang War", Category = "combat", Icon = "gang" },
            new() { Key = "T", Label = "The Gauntlet", Category = "combat", Icon = "gauntlet" },
            new() { Key = "C", Label = "Claim Town Control", Category = "team", Icon = "control" },
            new() { Key = "F", Label = "Flee Town Control", Category = "team", Icon = "flee" },
            new() { Key = "S", Label = "Challenge Status", Category = "info", Icon = "info" },
            new() { Key = "P", Label = "Prison Grounds", Category = "navigate", Icon = "prison" },
            new() { Key = "R", Label = Loc.Get("ui.return"), Category = "navigate", Icon = "back" },
        };
        ElectronBridge.EmitMenu(menu);

        EmitNPCsInLocationToElectron();
    }
}
