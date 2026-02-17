using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using UsurperRemake.BBS;
using UsurperRemake.Systems;

namespace UsurperRemake.Locations;

/// <summary>
/// PvP Arena -- online mode only.
/// Players can attack other players' saved characters (async PvP).
/// Defender is AI-controlled, loaded from their save at full HP.
/// Attacker's damage persists. Winner steals gold from the loser.
/// </summary>
public class ArenaLocation : BaseLocation
{
    private static readonly string[] ClassNames = {
        "Alchemist", "Assassin", "Barbarian", "Bard", "Cleric",
        "Jester", "Magician", "Paladin", "Ranger", "Sage", "Warrior"
    };

    public ArenaLocation() : base(GameLocation.Arena,
        "The Arena",
        "A blood-stained combat pit where warriors settle scores.")
    {
    }

    protected override void SetupLocation()
    {
        PossibleExits.Add(GameLocation.MainStreet);
    }

    protected override void DisplayLocation()
    {
        terminal.ClearScreen();
        ShowArenaBanner();
        ShowArenaMenu();
        ShowStatusLine();
    }

    protected override async Task<bool> ProcessChoice(string choice)
    {
        var (handled, shouldExit) = await TryProcessGlobalCommand(choice);
        if (handled) return shouldExit;

        if (string.IsNullOrWhiteSpace(choice)) return false;
        char ch = char.ToUpperInvariant(choice.Trim()[0]);

        switch (ch)
        {
            case 'A':
                await ChooseOpponent();
                return false;
            case 'L':
                await ShowPvPLeaderboard();
                return false;
            case 'H':
                await ShowRecentFights();
                return false;
            case 'S':
                await ShowPvPStats();
                return false;
            case 'R':
                throw new LocationExitException(GameLocation.MainStreet);
            default:
                return false;
        }
    }

    // =====================================================================
    // Banner and Menu
    // =====================================================================

    private void ShowArenaBanner()
    {
        terminal.SetColor("bright_red");
        terminal.WriteLine("╔══════════════════════════════════════════════════════════════════════════════╗");
        terminal.SetColor("bright_yellow");
        terminal.WriteLine("║                            -= THE ARENA =-                                 ║");
        terminal.SetColor("bright_red");
        terminal.WriteLine("╚══════════════════════════════════════════════════════════════════════════════╝");
        terminal.SetColor("gray");
        terminal.WriteLine("  Torches flicker along the walls of the combat pit. The crowd roars");
        terminal.WriteLine("  as another challenger steps onto the blood-stained sand.");
        terminal.WriteLine("");
    }

    private void ShowArenaMenu()
    {
        terminal.SetColor("darkgray");
        terminal.Write("  [");
        terminal.SetColor("bright_red");
        terminal.Write("A");
        terminal.SetColor("darkgray");
        terminal.Write("]");
        terminal.SetColor("white");
        terminal.Write("ttack Player   ");

        terminal.SetColor("darkgray");
        terminal.Write("[");
        terminal.SetColor("bright_yellow");
        terminal.Write("L");
        terminal.SetColor("darkgray");
        terminal.Write("]");
        terminal.SetColor("white");
        terminal.WriteLine("eaderboard");

        terminal.SetColor("darkgray");
        terminal.Write("  [");
        terminal.SetColor("bright_cyan");
        terminal.Write("H");
        terminal.SetColor("darkgray");
        terminal.Write("]");
        terminal.SetColor("white");
        terminal.Write("istory         ");

        terminal.SetColor("darkgray");
        terminal.Write("[");
        terminal.SetColor("cyan");
        terminal.Write("S");
        terminal.SetColor("darkgray");
        terminal.Write("]");
        terminal.SetColor("white");
        terminal.Write("tats   ");

        terminal.SetColor("darkgray");
        terminal.Write("[");
        terminal.SetColor("gray");
        terminal.Write("R");
        terminal.SetColor("darkgray");
        terminal.Write("]");
        terminal.SetColor("white");
        terminal.WriteLine("eturn");
        terminal.WriteLine("");
    }

    // =====================================================================
    // Attack Player (Core PvP Flow)
    // =====================================================================

    private async Task ChooseOpponent()
    {
        if (!DoorMode.IsOnlineMode)
        {
            terminal.SetColor("gray");
            terminal.WriteLine("  The Arena is only available in online mode.");
            await terminal.PressAnyKey();
            return;
        }

        if (currentPlayer.Level < GameConfig.MinPvPLevel)
        {
            terminal.SetColor("yellow");
            terminal.WriteLine($"  You must be at least level {GameConfig.MinPvPLevel} to enter the pit.");
            terminal.SetColor("gray");
            terminal.WriteLine("  The arena master waves you away.");
            await terminal.PressAnyKey();
            return;
        }

        var backend = SaveSystem.Instance.Backend as SqlSaveBackend;
        if (backend == null) return;

        string myName = currentPlayer.Name2 ?? currentPlayer.Name1;
        string myUsername = myName.ToLower();
        int attacksToday = backend.GetPvPAttacksToday(myUsername);

        if (attacksToday >= GameConfig.MaxPvPAttacksPerDay)
        {
            terminal.SetColor("yellow");
            terminal.WriteLine($"  You have already fought {GameConfig.MaxPvPAttacksPerDay} times today.");
            terminal.SetColor("gray");
            terminal.WriteLine("  The arena master says: \"Come back tomorrow, fighter.\"");
            await terminal.PressAnyKey();
            return;
        }

        terminal.ClearScreen();
        terminal.SetColor("bright_red");
        terminal.WriteLine("  ═══ CHOOSE YOUR OPPONENT ═══");
        terminal.WriteLine("");
        terminal.SetColor("bright_cyan");
        terminal.WriteLine($"  Attacks remaining today: {GameConfig.MaxPvPAttacksPerDay - attacksToday}");
        terminal.WriteLine("");

        // Get eligible opponents
        var allPlayers = await backend.GetAllPlayerSummaries();
        var eligible = allPlayers
            .Where(p => !string.Equals(p.DisplayName, myName, StringComparison.OrdinalIgnoreCase))
            .Where(p => Math.Abs(p.Level - currentPlayer.Level) <= GameConfig.PvPLevelRangeLimit)
            .Where(p => p.Level >= GameConfig.MinPvPLevel)
            .Where(p => !backend.HasAttackedPlayerToday(myUsername, p.DisplayName.ToLower()))
            .OrderByDescending(p => p.Level)
            .ToList();

        if (eligible.Count == 0)
        {
            terminal.SetColor("gray");
            terminal.WriteLine("  There are no eligible opponents right now.");
            terminal.SetColor("darkgray");
            terminal.WriteLine("  (Must be within " + GameConfig.PvPLevelRangeLimit + " levels, not already fought today)");
            await terminal.PressAnyKey();
            return;
        }

        // Display opponent list
        terminal.SetColor("yellow");
        terminal.WriteLine($"  {"#",-4} {"Name",-20} {"Level",-8} {"Class",-12} {"Status"}");
        terminal.SetColor("darkgray");
        terminal.WriteLine("  " + new string('─', 52));

        for (int i = 0; i < eligible.Count; i++)
        {
            var p = eligible[i];
            string className = GetClassName(p.ClassId);
            terminal.SetColor("darkgray");
            terminal.Write($"  {i + 1,2}. ");
            terminal.SetColor("white");
            terminal.Write($"{p.DisplayName,-20}");
            terminal.SetColor("cyan");
            terminal.Write($"Lv {p.Level,-5}");
            terminal.SetColor("gray");
            terminal.Write($"{className,-12}");
            if (p.IsOnline)
            {
                terminal.SetColor("bright_green");
                terminal.Write("[ONLINE]");
            }
            terminal.WriteLine("");
        }

        terminal.WriteLine("");
        terminal.SetColor("white");
        var input = await terminal.GetInput("  Choose opponent (number, or Q to cancel): ");

        if (string.IsNullOrWhiteSpace(input) || input.Trim().ToUpper() == "Q")
            return;

        if (!int.TryParse(input.Trim(), out int selection) || selection < 1 || selection > eligible.Count)
        {
            terminal.SetColor("red");
            terminal.WriteLine("  Invalid selection.");
            await Task.Delay(1000);
            return;
        }

        var target = eligible[selection - 1];

        // Confirm attack
        terminal.WriteLine("");
        terminal.SetColor("bright_red");
        terminal.WriteLine($"  You are about to attack {target.DisplayName} (Level {target.Level} {GetClassName(target.ClassId)})!");
        terminal.SetColor("yellow");
        terminal.WriteLine("  WARNING: Any damage YOU take will persist after the fight.");
        terminal.WriteLine($"  Winner steals {GameConfig.PvPGoldStealPercent * 100:0}% of the loser's gold on hand.");
        terminal.WriteLine("");
        var confirm = await terminal.GetInput("  Are you sure? (Y/N): ");

        if (confirm?.Trim().ToUpper() != "Y")
        {
            terminal.SetColor("gray");
            terminal.WriteLine("  You step back from the pit.");
            await Task.Delay(1000);
            return;
        }

        // Load opponent's save data
        var opponentSave = await backend.ReadGameData(target.DisplayName);
        if (opponentSave?.Player == null)
        {
            terminal.SetColor("red");
            terminal.WriteLine("  Could not load opponent's data. They may have deleted their character.");
            await terminal.PressAnyKey();
            return;
        }

        // Shadows members resting at the Safe House are hidden from PvP attacks
        if (opponentSave.Player.SafeHouseResting)
        {
            terminal.SetColor("dark_magenta");
            terminal.WriteLine($"  {target.DisplayName} is hiding in the Dark Alley Safe House.");
            terminal.SetColor("gray");
            terminal.WriteLine("  The Shadows protect their own. You cannot find them.");
            await terminal.PressAnyKey();
            return;
        }

        var opponent = CreateCombatCharacterFromSave(opponentSave.Player, target.DisplayName);

        // Save defender's actual gold for 10% steal calculation, then zero
        // to prevent CombatEngine from applying its own 50% gold steal
        long defenderGold = opponent.Gold;
        opponent.Gold = 0;

        // Execute PvP combat
        terminal.ClearScreen();
        terminal.SetColor("bright_red");
        terminal.WriteLine("");
        terminal.WriteLine($"  {myName} challenges {target.DisplayName} in the Arena!");
        terminal.WriteLine("");
        await Task.Delay(1500);

        var combatEngine = new CombatEngine(terminal);
        var result = await combatEngine.PlayerVsPlayer(currentPlayer, opponent);

        // Process result
        await ProcessPvPResult(result, target, backend, defenderGold);
    }

    /// <summary>
    /// Create a combat-ready Character from saved PlayerData.
    /// Delegates to the shared PlayerCharacterLoader utility.
    /// </summary>
    private Character CreateCombatCharacterFromSave(PlayerData playerData, string displayName)
    {
        return PlayerCharacterLoader.CreateFromSaveData(playerData, displayName);
    }

    /// <summary>
    /// Process PvP combat results: rewards, penalties, news, logging.
    /// </summary>
    private async Task ProcessPvPResult(CombatResult result, PlayerSummary target, SqlSaveBackend backend, long defenderGold)
    {
        string myName = currentPlayer.Name2 ?? currentPlayer.Name1;
        string myUsername = myName.ToLower();
        string defenderUsername = target.DisplayName.ToLower();
        bool attackerWon = result.Outcome == CombatOutcome.Victory;
        bool attackerFled = result.Outcome == CombatOutcome.PlayerEscaped;
        string winnerUsername = attackerWon ? myUsername : defenderUsername;

        long goldStolen = 0;
        // XP and kill tracking are handled by CombatEngine.DeterminePvPOutcome()
        long xpGained = result.ExperienceGained;

        if (attackerWon)
        {
            // Calculate gold theft (10% of defender's ACTUAL gold from save)
            goldStolen = (long)(defenderGold * GameConfig.PvPGoldStealPercent);
            goldStolen = Math.Max(0, goldStolen);

            // Apply gold reward (XP and kill tracking already handled by CombatEngine)
            currentPlayer.Gold += goldStolen;
            currentPlayer.Statistics?.RecordGoldChange(currentPlayer.Gold);

            // Deduct gold from defender's save atomically
            if (goldStolen > 0)
                await backend.DeductGoldFromPlayer(defenderUsername, goldStolen);

            // Claim any bounties on the defeated player
            long bountyReward = await backend.ClaimBounties(defenderUsername, myUsername);

            // Display victory
            terminal.WriteLine("");
            terminal.SetColor("bright_green");
            terminal.WriteLine("  ═══ VICTORY! ═══");
            if (goldStolen > 0)
            {
                terminal.SetColor("yellow");
                terminal.WriteLine($"  Gold stolen from {target.DisplayName}: {goldStolen:N0}");
            }
            if (bountyReward > 0)
            {
                currentPlayer.Gold += bountyReward;
                terminal.SetColor("bright_magenta");
                terminal.WriteLine($"  BOUNTY COLLECTED: {bountyReward:N0} gold!");
            }
        }
        else if (result.Outcome == CombatOutcome.PlayerDied)
        {
            // Defender wins - steal 10% of attacker's gold
            goldStolen = (long)(currentPlayer.Gold * GameConfig.PvPGoldStealPercent);
            goldStolen = Math.Max(0, goldStolen);

            // Deduct stolen gold from attacker (on top of death penalty)
            currentPlayer.Gold = Math.Max(0, currentPlayer.Gold - goldStolen);

            // Add stolen gold to defender's save atomically
            if (goldStolen > 0)
                await backend.AddGoldToPlayer(defenderUsername, goldStolen);

            // Apply PvP death penalties and resurrect player here
            // to prevent the standard HandleDeath() from also triggering
            // (which would show "Monster defeats" and double-penalize)
            long expLoss = currentPlayer.Experience / 10;  // 10% XP loss
            long goldLoss = currentPlayer.Gold / 4;        // 25% gold loss
            currentPlayer.Experience = Math.Max(0, currentPlayer.Experience - expLoss);
            currentPlayer.Gold = Math.Max(0, currentPlayer.Gold - goldLoss);

            // Resurrect at Inn with half HP (prevents standard death handler)
            currentPlayer.HP = Math.Max(1, currentPlayer.MaxHP / 2);
            currentPlayer.Poison = 0;

            terminal.WriteLine("");
            terminal.SetColor("bright_red");
            terminal.WriteLine("  ═══ DEFEAT! ═══");
            terminal.SetColor("gray");
            terminal.WriteLine($"  You were defeated by {target.DisplayName}'s shadow in the Arena.");
            terminal.WriteLine("");
            terminal.SetColor("yellow");
            if (goldStolen > 0)
                terminal.WriteLine($"  {target.DisplayName} stole {goldStolen:N0} of your gold!");
            if (expLoss > 0)
                terminal.WriteLine($"  Lost {expLoss:N0} experience points");
            if (goldLoss > 0)
                terminal.WriteLine($"  Lost {goldLoss:N0} gold (death penalty)");
            terminal.SetColor("cyan");
            terminal.WriteLine($"  Healed to {currentPlayer.HP}/{currentPlayer.MaxHP} HP at the Inn.");
        }
        else if (attackerFled)
        {
            terminal.WriteLine("");
            terminal.SetColor("yellow");
            terminal.WriteLine("  You fled from the Arena. No rewards, no penalties.");
        }

        // Log to pvp_log table (skip if fled)
        if (!attackerFled)
        {
            int rounds = result.CombatLog?.Count ?? 0;
            await backend.LogPvPCombat(
                myUsername, defenderUsername,
                currentPlayer.Level, target.Level,
                winnerUsername, goldStolen, xpGained,
                (long)currentPlayer.HP, rounds
            );
        }

        // Post news (skip if fled)
        if (!attackerFled && OnlineStateManager.IsActive)
        {
            string newsMsg;
            if (attackerWon)
                newsMsg = goldStolen > 0
                    ? $"{myName} defeated {target.DisplayName} in the Arena and stole {goldStolen:N0} gold!"
                    : $"{myName} defeated {target.DisplayName} in the Arena!";
            else
                newsMsg = goldStolen > 0
                    ? $"{target.DisplayName}'s shadow struck down {myName} in the Arena and took {goldStolen:N0} gold!"
                    : $"{target.DisplayName}'s shadow struck down {myName} in the Arena!";
            await OnlineStateManager.Instance!.AddNews(newsMsg, "pvp");
        }

        // Send notification to defender (skip if fled)
        if (!attackerFled)
        {
            string notifyMsg = attackerWon
                ? $"[Arena] {myName} attacked you in the Arena and won! They stole {goldStolen:N0} of your gold."
                : $"[Arena] {myName} attacked you in the Arena but your shadow defeated them! You gained {goldStolen:N0} gold.";
            await backend.SendMessage(myUsername, defenderUsername, "pvp", notifyMsg);
            UsurperRemake.Server.MudServer.Instance?.SendToPlayer(defenderUsername,
                $"\u001b[91m  {notifyMsg}\u001b[0m");
        }

        // Autosave (attacker damage persists)
        await SaveSystem.Instance.AutoSave(currentPlayer as Player);

        DebugLogger.Instance.LogInfo("PVP",
            $"{myName} vs {target.DisplayName}: {(attackerWon ? "WIN" : attackerFled ? "FLED" : "LOSS")} " +
            $"Gold:{goldStolen} XP:{xpGained}");

        await terminal.PressAnyKey();
    }

    // =====================================================================
    // Leaderboard
    // =====================================================================

    private async Task ShowPvPLeaderboard()
    {
        var backend = SaveSystem.Instance.Backend as SqlSaveBackend;
        if (backend == null) return;

        var leaderboard = await backend.GetPvPLeaderboard();

        terminal.ClearScreen();
        terminal.SetColor("bright_yellow");
        terminal.WriteLine("  ═══ ARENA LEADERBOARD ═══");
        terminal.WriteLine("");

        if (leaderboard.Count == 0)
        {
            terminal.SetColor("gray");
            terminal.WriteLine("  No PvP combat has occurred yet. Be the first to fight!");
        }
        else
        {
            terminal.SetColor("darkgray");
            terminal.WriteLine($"  {"#",-4} {"Name",-20} {"W/L",-10} {"Level",-8} {"Gold Stolen"}");
            terminal.SetColor("darkgray");
            terminal.WriteLine("  " + new string('─', 56));

            foreach (var entry in leaderboard)
            {
                string rankColor = entry.Rank switch
                {
                    1 => "bright_yellow",
                    2 => "white",
                    3 => "yellow",
                    _ => "gray"
                };
                terminal.SetColor(rankColor);
                terminal.WriteLine(
                    $"  {entry.Rank,-4} {entry.DisplayName,-20} " +
                    $"{entry.Wins}W/{entry.Losses}L    Lv{entry.Level,-5} {entry.TotalGoldStolen:N0}g");
            }
        }

        terminal.WriteLine("");
        await terminal.PressAnyKey();
    }

    // =====================================================================
    // Fight History
    // =====================================================================

    private async Task ShowRecentFights()
    {
        var backend = SaveSystem.Instance.Backend as SqlSaveBackend;
        if (backend == null) return;

        var fights = await backend.GetRecentPvPFights();

        terminal.ClearScreen();
        terminal.SetColor("bright_red");
        terminal.WriteLine("  ═══ RECENT ARENA FIGHTS ═══");
        terminal.WriteLine("");

        if (fights.Count == 0)
        {
            terminal.SetColor("gray");
            terminal.WriteLine("  The arena sand is undisturbed. No fights yet.");
        }
        else
        {
            foreach (var fight in fights)
            {
                bool attackerWon = string.Equals(fight.WinnerUsername, fight.AttackerName, StringComparison.OrdinalIgnoreCase);
                terminal.SetColor("darkgray");
                terminal.Write($"  {fight.CreatedAt:MM/dd HH:mm}  ");
                terminal.SetColor(attackerWon ? "bright_green" : "red");
                terminal.Write($"{fight.AttackerName}");
                terminal.SetColor("white");
                terminal.Write($" (Lv{fight.AttackerLevel}) vs ");
                terminal.SetColor(!attackerWon ? "bright_green" : "red");
                terminal.Write($"{fight.DefenderName}");
                terminal.SetColor("white");
                terminal.Write($" (Lv{fight.DefenderLevel})");
                if (fight.GoldStolen > 0)
                {
                    terminal.SetColor("yellow");
                    terminal.Write($" [{fight.GoldStolen:N0}g]");
                }
                terminal.WriteLine("");
            }
        }

        terminal.WriteLine("");
        await terminal.PressAnyKey();
    }

    // =====================================================================
    // Personal PvP Stats
    // =====================================================================

    private async Task ShowPvPStats()
    {
        terminal.ClearScreen();
        terminal.SetColor("bright_cyan");
        terminal.WriteLine("  ═══ YOUR PVP RECORD ═══");
        terminal.WriteLine("");

        var stats = currentPlayer.Statistics;
        if (stats == null)
        {
            terminal.SetColor("gray");
            terminal.WriteLine("  No statistics available.");
        }
        else
        {
            terminal.SetColor("white");
            terminal.WriteLine($"  PvP Kills:  {stats.TotalPlayerKills}");
            terminal.WriteLine($"  PvP Deaths: {stats.TotalPlayerDeaths}");

            double totalFights = stats.TotalPlayerKills + stats.TotalPlayerDeaths;
            double winRate = totalFights > 0
                ? (double)stats.TotalPlayerKills / totalFights * 100
                : 0;

            terminal.SetColor("yellow");
            terminal.WriteLine($"  Win Rate:   {winRate:F1}%");
            terminal.WriteLine($"  Total Fights: {totalFights:N0}");
        }

        terminal.WriteLine("");
        await terminal.PressAnyKey();
    }

    // =====================================================================
    // Helpers
    // =====================================================================

    private string GetClassName(int classId)
    {
        return classId >= 0 && classId < ClassNames.Length ? ClassNames[classId] : "Unknown";
    }
}
