using UsurperRemake.Utils;
using UsurperRemake.Systems;
using Godot;
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
    private Random random = new Random();

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
        terminal.ClearScreen();

        // Header
        terminal.SetColor("bright_magenta");
        terminal.WriteLine("╔══════════════════════════════════════════════════════════════════════════════╗");
        terminal.WriteLine("║                    ANCHOR ROAD - Conjunction of Destinies                  ║");
        terminal.WriteLine("╚══════════════════════════════════════════════════════════════════════════════╝");
        terminal.WriteLine("");

        // Atmospheric description
        terminal.SetColor("white");
        terminal.WriteLine("The Red Fields stretch east, where warriors test their mettle.");
        terminal.SetColor("bright_yellow");
        terminal.WriteLine("Blood and glory await those brave enough to enter.");
        terminal.WriteLine("");

        // Show NPCs in location
        ShowNPCsInLocation();

        // Show current status
        ShowChallengeStatus();
        terminal.WriteLine("");

        // Menu - Challenges
        terminal.SetColor("cyan");
        terminal.WriteLine("Challenges:");
        WriteMenuRow("B", "Bounty Board", "G", "Gang War", "T", "The Gauntlet");
        terminal.WriteLine("");

        // Menu - Town Control
        terminal.SetColor("cyan");
        terminal.WriteLine("Town Control:");
        WriteMenuRow("C", "Claim Town", "F", "Flee Town Control", "", "");
        terminal.WriteLine("");

        // Menu - Other
        terminal.SetColor("cyan");
        terminal.WriteLine("Other:");
        WriteMenuRow("P", "Prison Grounds", "S", "Status", "R", "Return to Town");
        terminal.WriteLine("");
    }

    private void WriteMenuRow(string key1, string label1, string key2, string label2, string key3, string label3)
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
            terminal.Write(label1.PadRight(18));
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
            terminal.Write(label2.PadRight(18));
        }

        if (!string.IsNullOrEmpty(key3))
        {
            terminal.SetColor("darkgray");
            terminal.Write("[");
            terminal.SetColor("bright_cyan");
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
        terminal.SetColor("bright_cyan");
        terminal.Write(key);
        terminal.SetColor("darkgray");
        terminal.Write("] ");
        terminal.SetColor("white");
        terminal.WriteLine(label);
    }

    private void ShowChallengeStatus()
    {
        terminal.SetColor("darkgray");
        terminal.WriteLine("─────────────────────────────────────────");

        // Show player fights remaining
        terminal.SetColor("white");
        terminal.Write("Player Fights: ");
        if (currentPlayer.PFights > 0)
        {
            terminal.SetColor("bright_green");
            terminal.WriteLine($"{currentPlayer.PFights}");
        }
        else
        {
            terminal.SetColor("red");
            terminal.WriteLine("0 (exhausted)");
        }

        // Show team fights if in a team
        if (!string.IsNullOrEmpty(currentPlayer.Team))
        {
            terminal.SetColor("white");
            terminal.Write("Team Fights: ");
            if (currentPlayer.TFights > 0)
            {
                terminal.SetColor("bright_green");
                terminal.WriteLine($"{currentPlayer.TFights}");
            }
            else
            {
                terminal.SetColor("red");
                terminal.WriteLine("0 (exhausted)");
            }

            terminal.SetColor("white");
            terminal.Write("Your Team: ");
            terminal.SetColor("bright_cyan");
            terminal.Write(currentPlayer.Team);

            if (currentPlayer.CTurf)
            {
                terminal.SetColor("bright_yellow");
                terminal.WriteLine(" * CONTROLS TOWN");
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
            terminal.Write("Town Controlled By: ");
            terminal.SetColor("bright_yellow");
            terminal.WriteLine(turfController);
        }

        terminal.SetColor("darkgray");
        terminal.WriteLine("─────────────────────────────────────────");
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
                terminal.WriteLine("Invalid choice! Press ? for menu.");
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
        terminal.SetColor("bright_red");
        terminal.WriteLine("╔══════════════════════════════════════════════════════════════════════════════╗");
        terminal.WriteLine("║                              BOUNTY HUNTING                                 ║");
        terminal.WriteLine("╚══════════════════════════════════════════════════════════════════════════════╝");
        terminal.WriteLine("");

        if (currentPlayer.PFights <= 0)
        {
            terminal.SetColor("red");
            terminal.WriteLine("You have no player fights left today!");
            terminal.WriteLine("Come back tomorrow when you're rested.");
            terminal.WriteLine("");
            terminal.SetColor("darkgray");
            terminal.WriteLine("Press Enter to continue...");
            await terminal.ReadKeyAsync();
            return;
        }

        terminal.SetColor("white");
        terminal.WriteLine("You scan the bounty board for wanted criminals...");
        terminal.WriteLine($"Player Fights Remaining: {currentPlayer.PFights}");
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
                terminal.WriteLine("No bounties available at this time.");
                terminal.WriteLine("");
                terminal.SetColor("darkgray");
                terminal.WriteLine("Press Enter to continue...");
                await terminal.ReadKeyAsync();
                return;
            }
        }

        terminal.SetColor("bright_yellow");
        terminal.WriteLine("WANTED - DEAD OR ALIVE:");
        terminal.SetColor("darkgray");
        terminal.WriteLine(new string('─', 60));
        terminal.SetColor("white");
        terminal.WriteLine($"{"#",-3} {"Name",-20} {"Level",-6} {"Bounty",-12} {"Crime",-15}");
        terminal.SetColor("darkgray");
        terminal.WriteLine(new string('─', 60));

        for (int i = 0; i < bountyTargets.Count; i++)
        {
            var target = bountyTargets[i];
            long bounty = target.Level * 100 + (long)target.Darkness;
            string crime = target.Darkness > 500 ? "Murder" :
                          target.Darkness > 200 ? "Assault" : "Troublemaker";

            terminal.SetColor("white");
            terminal.WriteLine($"{i + 1,-3} {target.DisplayName,-20} {target.Level,-6} {bounty:N0}g{"",-5} {crime,-15}");
        }

        terminal.WriteLine("");
        terminal.SetColor("cyan");
        terminal.Write("Hunt which target? (0 to cancel): ");
        terminal.SetColor("white");
        string input = await terminal.ReadLineAsync();

        if (int.TryParse(input, out int choice) && choice >= 1 && choice <= bountyTargets.Count)
        {
            var target = bountyTargets[choice - 1];
            currentPlayer.PFights--;

            terminal.WriteLine("");
            terminal.SetColor("bright_red");
            terminal.WriteLine($"You track down {target.DisplayName}...");
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
                terminal.WriteLine("═══════════════════════════════════════");
                terminal.WriteLine("           BOUNTY COLLECTED!");
                terminal.WriteLine("═══════════════════════════════════════");
                terminal.WriteLine($"Bounty Reward: {bounty:N0} gold");
                terminal.WriteLine($"Experience: {expGain:N0}");

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
                terminal.WriteLine("You fled the fight. The bounty remains uncollected.");
            }
            else
            {
                terminal.SetColor("red");
                terminal.WriteLine("");
                terminal.WriteLine($"{target.DisplayName} bested you! The bounty remains uncollected.");
            }
        }

        terminal.WriteLine("");
        terminal.SetColor("darkgray");
        terminal.WriteLine("Press Enter to continue...");
        await terminal.ReadKeyAsync();
    }

    /// <summary>
    /// Gang war - sequential 1v1 fights against rival team members using real combat
    /// </summary>
    private async Task StartGangWar()
    {
        terminal.ClearScreen();
        terminal.SetColor("bright_red");
        terminal.WriteLine("╔══════════════════════════════════════════════════════════════════════════════╗");
        terminal.WriteLine("║                                GANG WAR                                     ║");
        terminal.WriteLine("╚══════════════════════════════════════════════════════════════════════════════╝");
        terminal.WriteLine("");

        if (string.IsNullOrEmpty(currentPlayer.Team))
        {
            terminal.SetColor("red");
            terminal.WriteLine("You must be in a team to participate in gang wars!");
            terminal.WriteLine("Visit Team Corner at the Inn to create or join a team.");
            terminal.WriteLine("");
            terminal.SetColor("darkgray");
            terminal.WriteLine("Press Enter to continue...");
            await terminal.ReadKeyAsync();
            return;
        }

        if (currentPlayer.TFights <= 0)
        {
            terminal.SetColor("red");
            terminal.WriteLine("You have no team fights left today!");
            terminal.WriteLine("");
            terminal.SetColor("darkgray");
            terminal.WriteLine("Press Enter to continue...");
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
            .OrderByDescending(t => t.TotalPower)
            .ToList();

        if (teams.Count == 0)
        {
            terminal.SetColor("yellow");
            terminal.WriteLine("No rival teams found to challenge!");
            terminal.WriteLine("");
            terminal.SetColor("darkgray");
            terminal.WriteLine("Press Enter to continue...");
            await terminal.ReadKeyAsync();
            return;
        }

        terminal.SetColor("white");
        terminal.WriteLine($"Your Team: {currentPlayer.Team}");
        terminal.WriteLine($"Team Fights Remaining: {currentPlayer.TFights}");
        terminal.WriteLine("");

        terminal.SetColor("cyan");
        terminal.WriteLine("Rival Teams:");
        terminal.SetColor("darkgray");
        terminal.WriteLine(new string('─', 55));
        terminal.SetColor("white");
        terminal.WriteLine($"{"#",-3} {"Team Name",-24} {"Members",-8} {"Power",-8} {"Turf",-5}");
        terminal.SetColor("darkgray");
        terminal.WriteLine(new string('─', 55));

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
        terminal.Write("Challenge which team? (0 to cancel): ");
        terminal.SetColor("white");
        string input = await terminal.ReadLineAsync();

        if (int.TryParse(input, out int choice) && choice >= 1 && choice <= teams.Count)
        {
            var targetTeam = teams[choice - 1];
            currentPlayer.TFights--;

            terminal.WriteLine("");
            terminal.SetColor("bright_red");
            terminal.WriteLine($"Your team challenges {targetTeam.TeamName}!");
            terminal.WriteLine($"You must defeat their members one by one!");
            terminal.WriteLine("");
            await Task.Delay(1500);

            // Get player's NPC team members for turf transfer
            var playerTeamMembers = allNPCs
                .Where(n => n.Team == currentPlayer.Team && n.IsAlive)
                .ToList();

            // Get enemy team members sorted by level (weakest first)
            var enemyTeamMembers = allNPCs
                .Where(n => n.Team == targetTeam.TeamName && n.IsAlive && !n.IsDead)
                .OrderBy(n => n.Level)
                .ToList();

            bool playerWon = true;
            int enemiesDefeated = 0;
            long totalGoldReward = 0;
            long totalXPReward = 0;

            for (int f = 0; f < enemyTeamMembers.Count; f++)
            {
                var enemy = enemyTeamMembers[f];

                terminal.SetColor("bright_magenta");
                terminal.WriteLine($"═══ FIGHT {f + 1}/{enemyTeamMembers.Count} ═══");
                terminal.SetColor("white");
                terminal.WriteLine($"You face {enemy.DisplayName} (Level {enemy.Level} {enemy.Class})!");
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
                        terminal.WriteLine($"You catch your breath and recover {healAmount} HP.");
                        terminal.WriteLine($"HP: {currentPlayer.HP}/{currentPlayer.MaxHP}");
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
                terminal.WriteLine("═══════════════════════════════════════");
                terminal.WriteLine("        GANG WAR VICTORY!");
                terminal.WriteLine("═══════════════════════════════════════");
                terminal.WriteLine($"You defeated all {enemiesDefeated} members of {targetTeam.TeamName}!");
                terminal.WriteLine($"Gold Plundered: {totalGoldReward:N0}");
                terminal.WriteLine($"Experience: {totalXPReward:N0}");

                currentPlayer.Gold += totalGoldReward;
                currentPlayer.Experience += totalXPReward;

                // Handle turf transfer
                if (targetTeam.ControlsTurf)
                {
                    terminal.SetColor("bright_yellow");
                    terminal.WriteLine("");
                    terminal.WriteLine("* YOUR TEAM NOW CONTROLS THE TOWN! *");

                    foreach (var enemy in enemyTeamMembers)
                    {
                        enemy.CTurf = false;
                    }
                    currentPlayer.CTurf = true;
                    foreach (var ally in playerTeamMembers)
                    {
                        ally.CTurf = true;
                    }
                }

                NewsSystem.Instance.Newsy(true, $"Gang War! {currentPlayer.Team} defeated {targetTeam.TeamName}!");
            }
            else
            {
                terminal.SetColor("red");
                terminal.WriteLine("═══════════════════════════════════════");
                terminal.WriteLine("        GANG WAR DEFEAT!");
                terminal.WriteLine("═══════════════════════════════════════");
                terminal.WriteLine($"You were defeated after taking down {enemiesDefeated} opponent{(enemiesDefeated != 1 ? "s" : "")}.");

                if (enemiesDefeated > 0)
                {
                    // Give partial rewards for enemies defeated before losing
                    long partialGold = totalGoldReward / 2;
                    long partialXP = totalXPReward / 2;
                    terminal.SetColor("yellow");
                    terminal.WriteLine($"Partial Rewards: {partialGold:N0} gold, {partialXP:N0} XP");
                    currentPlayer.Gold += partialGold;
                    currentPlayer.Experience += partialXP;
                }

                NewsSystem.Instance.Newsy(true, $"Gang War! {targetTeam.TeamName} repelled {currentPlayer.Team}!");
            }
        }

        terminal.WriteLine("");
        terminal.SetColor("darkgray");
        terminal.WriteLine("Press Enter to continue...");
        await terminal.ReadKeyAsync();
    }

    /// <summary>
    /// The Gauntlet - solo 10-wave endurance challenge against increasingly tough monsters
    /// </summary>
    private async Task StartGauntlet()
    {
        terminal.ClearScreen();
        terminal.SetColor("bright_yellow");
        terminal.WriteLine("╔══════════════════════════════════════════════════════════════════════════════╗");
        terminal.WriteLine("║                             THE GAUNTLET                                    ║");
        terminal.WriteLine("╚══════════════════════════════════════════════════════════════════════════════╝");
        terminal.WriteLine("");

        terminal.SetColor("white");
        terminal.WriteLine("Face 10 waves of increasingly dangerous monsters.");
        terminal.WriteLine("Your health and mana carry over between waves.");
        terminal.WriteLine("Survive all 10 to earn the title of Gauntlet Champion!");
        terminal.WriteLine("");

        if (currentPlayer.Level < GameConfig.GauntletMinLevel)
        {
            terminal.SetColor("red");
            terminal.WriteLine($"You must be at least level {GameConfig.GauntletMinLevel} to enter The Gauntlet.");
            terminal.WriteLine("");
            terminal.SetColor("darkgray");
            terminal.WriteLine("Press Enter to continue...");
            await terminal.ReadKeyAsync();
            return;
        }

        if (currentPlayer.PFights <= 0)
        {
            terminal.SetColor("red");
            terminal.WriteLine("You have no player fights left today!");
            terminal.WriteLine("");
            terminal.SetColor("darkgray");
            terminal.WriteLine("Press Enter to continue...");
            await terminal.ReadKeyAsync();
            return;
        }

        long entryFee = GameConfig.GauntletEntryFeePerLevel * currentPlayer.Level;

        terminal.SetColor("cyan");
        terminal.WriteLine("Gauntlet Details:");
        terminal.SetColor("darkgray");
        terminal.WriteLine("─────────────────────────────────────────");
        terminal.SetColor("white");
        terminal.Write("Entry Fee: ");
        terminal.SetColor("bright_yellow");
        terminal.WriteLine($"{entryFee:N0} gold");
        terminal.SetColor("white");
        terminal.Write("Your Gold: ");
        terminal.SetColor(currentPlayer.Gold >= entryFee ? "bright_green" : "red");
        terminal.WriteLine($"{currentPlayer.Gold:N0}");
        terminal.SetColor("white");
        terminal.Write("Your HP: ");
        terminal.SetColor(currentPlayer.HP > currentPlayer.MaxHP / 2 ? "bright_green" : "red");
        terminal.WriteLine($"{currentPlayer.HP}/{currentPlayer.MaxHP}");
        terminal.SetColor("darkgray");
        terminal.WriteLine("─────────────────────────────────────────");
        terminal.WriteLine("");

        if (currentPlayer.Gold < entryFee)
        {
            terminal.SetColor("red");
            terminal.WriteLine($"You need {entryFee:N0} gold to enter. You only have {currentPlayer.Gold:N0}.");
            terminal.WriteLine("");
            terminal.SetColor("darkgray");
            terminal.WriteLine("Press Enter to continue...");
            await terminal.ReadKeyAsync();
            return;
        }

        terminal.SetColor("cyan");
        terminal.Write($"Enter The Gauntlet for {entryFee:N0} gold? (Y/N): ");
        terminal.SetColor("white");
        string response = await terminal.ReadLineAsync();

        if (response?.ToUpper().StartsWith("Y") != true)
        {
            terminal.SetColor("white");
            terminal.WriteLine("You decide to come back another time.");
            terminal.WriteLine("");
            terminal.SetColor("darkgray");
            terminal.WriteLine("Press Enter to continue...");
            await terminal.ReadKeyAsync();
            return;
        }

        // Deduct entry fee and fight
        currentPlayer.Gold -= entryFee;
        currentPlayer.PFights--;

        terminal.WriteLine("");
        terminal.SetColor("bright_red");
        terminal.WriteLine("The iron gates slam shut behind you.");
        terminal.WriteLine("The crowd roars as you enter the arena...");
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
            terminal.SetColor("bright_yellow");
            terminal.WriteLine($"═══════════════════ WAVE {wave}/{GameConfig.GauntletWaveCount} ═══════════════════");
            terminal.WriteLine("");

            terminal.SetColor("white");
            terminal.Write("Opponent: ");
            if (isBoss)
                terminal.SetColor("bright_red");
            else if (isMiniBoss)
                terminal.SetColor("bright_magenta");
            else
                terminal.SetColor("bright_cyan");
            terminal.WriteLine($"{monster.Name} (Level {monster.Level})");

            terminal.SetColor("white");
            terminal.Write("Your HP: ");
            terminal.SetColor(currentPlayer.HP > currentPlayer.MaxHP / 2 ? "bright_green" : "red");
            terminal.WriteLine($"{currentPlayer.HP}/{currentPlayer.MaxHP}");
            terminal.WriteLine("");
            await Task.Delay(1000);

            // Real combat
            var combatEngine = new CombatEngine(terminal);
            var result = await combatEngine.PlayerVsMonster(currentPlayer, monster, null, false);

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
                terminal.WriteLine($"Wave {wave} complete! +{waveGold:N0} gold, +{waveXP:N0} XP");

                // Wave 10 champion bonus
                if (wave == GameConfig.GauntletWaveCount)
                {
                    long championGold = GameConfig.GauntletChampionGoldPerLevel * currentPlayer.Level;
                    long championXP = GameConfig.GauntletChampionXPPerLevel * currentPlayer.Level;
                    totalGoldEarned += championGold;
                    totalXPEarned += championXP;
                    currentPlayer.Gold += championGold;
                    currentPlayer.Experience += championXP;

                    terminal.SetColor("bright_yellow");
                    terminal.WriteLine("");
                    terminal.WriteLine("═══════════════════════════════════════");
                    terminal.WriteLine("        GAUNTLET CHAMPION!");
                    terminal.WriteLine("═══════════════════════════════════════");
                    terminal.WriteLine($"Champion Bonus: +{championGold:N0} gold, +{championXP:N0} XP!");

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
                    terminal.WriteLine($"You catch your breath... +{healAmount} HP, +{manaRestore} Mana");
                    terminal.WriteLine($"HP: {currentPlayer.HP}/{currentPlayer.MaxHP}  Mana: {currentPlayer.Mana}/{currentPlayer.MaxMana}");
                    terminal.WriteLine("");
                    terminal.SetColor("darkgray");
                    terminal.WriteLine("Press Enter for next wave...");
                    await terminal.ReadKeyAsync();
                }
            }
            else
            {
                // Player died or fled — gauntlet over
                terminal.SetColor("red");
                terminal.WriteLine("");
                if (result.Outcome == CombatOutcome.PlayerEscaped)
                    terminal.WriteLine("You flee the arena in disgrace!");
                else
                    terminal.WriteLine("You collapse in the arena...");
                break;
            }
        }

        // Final summary
        terminal.WriteLine("");
        terminal.SetColor("bright_cyan");
        terminal.WriteLine("═══════════════════════════════════════");
        terminal.WriteLine("         GAUNTLET SUMMARY");
        terminal.WriteLine("═══════════════════════════════════════");
        terminal.SetColor("white");
        terminal.Write("Waves Survived: ");
        if (wavesCompleted >= GameConfig.GauntletWaveCount)
            terminal.SetColor("bright_yellow");
        else if (wavesCompleted >= 5)
            terminal.SetColor("bright_green");
        else
            terminal.SetColor("yellow");
        terminal.WriteLine($"{wavesCompleted}/{GameConfig.GauntletWaveCount}");
        terminal.SetColor("white");
        terminal.WriteLine($"Gold Earned: {totalGoldEarned:N0}");
        terminal.WriteLine($"XP Earned: {totalXPEarned:N0}");

        terminal.WriteLine("");
        terminal.SetColor("darkgray");
        terminal.WriteLine("Press Enter to continue...");
        await terminal.ReadKeyAsync();
    }

    /// <summary>
    /// Claim town for your team
    /// </summary>
    private async Task ClaimTown()
    {
        terminal.ClearScreen();
        terminal.SetColor("bright_yellow");
        terminal.WriteLine("╔══════════════════════════════════════════════════════════════════════════════╗");
        terminal.WriteLine("║                              CLAIM TOWN                                     ║");
        terminal.WriteLine("╚══════════════════════════════════════════════════════════════════════════════╝");
        terminal.WriteLine("");

        if (string.IsNullOrEmpty(currentPlayer.Team))
        {
            terminal.SetColor("red");
            terminal.WriteLine("You must be in a team to claim towns!");
            terminal.WriteLine("");
            terminal.SetColor("darkgray");
            terminal.WriteLine("Press Enter to continue...");
            await terminal.ReadKeyAsync();
            return;
        }

        if (currentPlayer.CTurf)
        {
            terminal.SetColor("yellow");
            terminal.WriteLine("Your team already controls this town!");
            terminal.WriteLine("");
            terminal.SetColor("darkgray");
            terminal.WriteLine("Press Enter to continue...");
            await terminal.ReadKeyAsync();
            return;
        }

        // Check if anyone controls the town
        var turfController = GetTurfControllerName();

        if (string.IsNullOrEmpty(turfController))
        {
            // Nobody controls - easy claim
            terminal.SetColor("white");
            terminal.WriteLine("No team currently controls this town.");
            terminal.WriteLine("");
            terminal.SetColor("cyan");
            terminal.Write("Claim the town for your team? (Y/N): ");
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
                terminal.WriteLine("* YOUR TEAM NOW CONTROLS THE TOWN! *");
                terminal.WriteLine("Rule wisely, for challengers will come...");

                NewsSystem.Instance.Newsy(true, $"{currentPlayer.Team} has taken control of the town!");
            }
        }
        else
        {
            terminal.SetColor("yellow");
            terminal.WriteLine($"The town is currently controlled by: {turfController}");
            terminal.WriteLine("");
            terminal.SetColor("white");
            terminal.WriteLine("To claim the town, you must defeat the controlling team in Gang War!");
            terminal.WriteLine("Use the (G)ang War option to challenge them.");
        }

        terminal.WriteLine("");
        terminal.SetColor("darkgray");
        terminal.WriteLine("Press Enter to continue...");
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
            terminal.WriteLine("Your team doesn't control any town!");
            terminal.WriteLine("");
            await Task.Delay(2000);
            return;
        }

        terminal.WriteLine("");
        terminal.SetColor("yellow");
        terminal.WriteLine("Are you sure you want to abandon town control?");
        terminal.WriteLine("This will leave the town open for other teams to claim.");
        terminal.Write("Abandon control? (Y/N): ");
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
            terminal.WriteLine("Your team has abandoned control of the town.");
            terminal.WriteLine("The town is now free for the taking...");

            NewsSystem.Instance.Newsy(true, $"{currentPlayer.Team} abandoned control of the town!");
        }
        else
        {
            terminal.SetColor("white");
            terminal.WriteLine("Town control maintained.");
        }

        terminal.WriteLine("");
        terminal.SetColor("darkgray");
        terminal.WriteLine("Press Enter to continue...");
        await terminal.ReadKeyAsync();
    }

    /// <summary>
    /// Navigate to prison grounds
    /// </summary>
    private async Task NavigateToPrisonGrounds()
    {
        terminal.ClearScreen();
        terminal.SetColor("darkgray");
        terminal.WriteLine("╔══════════════════════════════════════════════════════════════════════════════╗");
        terminal.WriteLine("║                             PRISON GROUNDS                                  ║");
        terminal.WriteLine("╚══════════════════════════════════════════════════════════════════════════════╝");
        terminal.WriteLine("");

        terminal.SetColor("white");
        terminal.WriteLine("You approach the forbidding prison walls...");
        terminal.WriteLine("Guards patrol the perimeter, watching for trouble.");
        terminal.WriteLine("");

        terminal.SetColor("cyan");
        terminal.WriteLine("Options:");
        terminal.SetColor("white");
        WriteMenuOption("J", "Attempt a Jailbreak (rescue a prisoner)");
        WriteMenuOption("V", "View Prisoners");
        WriteMenuOption("L", "Leave");
        terminal.WriteLine("");

        terminal.SetColor("cyan");
        terminal.Write("Choice: ");
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
        terminal.WriteLine("Jailbreaks are extremely dangerous!");
        terminal.WriteLine("You could end up in prison yourself if caught.");
        terminal.WriteLine("");

        terminal.SetColor("cyan");
        terminal.Write("Proceed with jailbreak? (Y/N): ");
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
                terminal.WriteLine("You sneak past the guards and find a prisoner!");
                terminal.WriteLine("You help them escape through a secret passage.");
                terminal.WriteLine("");
                terminal.WriteLine("The prisoner thanks you and disappears into the night.");

                currentPlayer.Chivalry += 50;
                NewsSystem.Instance.Newsy(true, $"{currentPlayer.DisplayName} orchestrated a daring prison escape!");
            }
            else
            {
                terminal.SetColor("red");
                terminal.WriteLine("");
                terminal.WriteLine("CAUGHT!");
                terminal.WriteLine("The guards spotted you and gave chase!");

                // Damage and possible imprisonment
                long damage = currentPlayer.MaxHP / 5;
                currentPlayer.HP = Math.Max(1, currentPlayer.HP - damage);
                currentPlayer.Darkness += 25;

                terminal.WriteLine($"You barely escaped, losing {damage} HP in the process.");
            }
        }

        terminal.WriteLine("");
        terminal.SetColor("darkgray");
        terminal.WriteLine("Press Enter to continue...");
        await terminal.ReadKeyAsync();
    }

    private async Task ViewPrisoners()
    {
        terminal.WriteLine("");
        terminal.SetColor("white");
        terminal.WriteLine("You peer through the iron bars...");
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
            terminal.WriteLine("The prison cells appear to be empty.");
        }
        else
        {
            terminal.SetColor("cyan");
            terminal.WriteLine("Prisoners:");
            terminal.SetColor("darkgray");
            terminal.WriteLine(new string('─', 40));

            foreach (var prisoner in prisoners)
            {
                terminal.SetColor("white");
                terminal.WriteLine($"  {prisoner.DisplayName} - Level {prisoner.Level} {prisoner.Class}");
            }
        }

        terminal.WriteLine("");
        terminal.SetColor("darkgray");
        terminal.WriteLine("Press Enter to continue...");
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

        return controller?.Team;
    }

    #endregion
}
