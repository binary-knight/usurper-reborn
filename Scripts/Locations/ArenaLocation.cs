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
        "Jester", "Magician", "Paladin", "Ranger", "Sage", "Warrior",
        "Tidesworn", "Wavecaller", "Cyclebreaker", "Abysswarden", "Voidreaver",
        "Mystic Shaman"
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
        WriteBoxHeader(Loc.Get("arena.header"), "bright_red");
        terminal.SetColor("gray");
        terminal.WriteLine($"  {Loc.Get("arena.desc1")}");
        terminal.WriteLine($"  {Loc.Get("arena.desc2")}");
        terminal.WriteLine("");
    }

    private void ShowArenaMenu()
    {
        terminal.SetColor("darkgray");
        terminal.Write("  [");
        terminal.SetColor("bright_yellow");
        terminal.Write("A");
        terminal.SetColor("darkgray");
        terminal.Write("]");
        terminal.SetColor("white");
        terminal.Write(Loc.Get("arena.attack_player"));

        terminal.SetColor("darkgray");
        terminal.Write("[");
        terminal.SetColor("bright_yellow");
        terminal.Write("L");
        terminal.SetColor("darkgray");
        terminal.Write("]");
        terminal.SetColor("white");
        terminal.WriteLine(Loc.Get("arena.leaderboard_menu"));

        terminal.SetColor("darkgray");
        terminal.Write("  [");
        terminal.SetColor("bright_yellow");
        terminal.Write("H");
        terminal.SetColor("darkgray");
        terminal.Write("]");
        terminal.SetColor("white");
        terminal.Write(Loc.Get("arena.history_menu"));

        terminal.SetColor("darkgray");
        terminal.Write("[");
        terminal.SetColor("bright_yellow");
        terminal.Write("S");
        terminal.SetColor("darkgray");
        terminal.Write("]");
        terminal.SetColor("white");
        terminal.Write(Loc.Get("arena.stats_menu"));

        terminal.SetColor("darkgray");
        terminal.Write("[");
        terminal.SetColor("bright_yellow");
        terminal.Write("R");
        terminal.SetColor("darkgray");
        terminal.Write("]");
        terminal.SetColor("white");
        terminal.WriteLine(Loc.Get("arena.return_menu"));
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
            terminal.WriteLine($"  {Loc.Get("arena.online_only")}");
            await terminal.PressAnyKey();
            return;
        }

        if (currentPlayer.Level < GameConfig.MinPvPLevel)
        {
            terminal.SetColor("yellow");
            terminal.WriteLine($"  {Loc.Get("arena.min_level", GameConfig.MinPvPLevel)}");
            terminal.SetColor("gray");
            terminal.WriteLine($"  {Loc.Get("arena.master_waves")}");
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
            terminal.WriteLine($"  {Loc.Get("arena.max_fights", GameConfig.MaxPvPAttacksPerDay)}");
            terminal.SetColor("gray");
            terminal.WriteLine($"  {Loc.Get("arena.come_back")}");
            await terminal.PressAnyKey();
            return;
        }

        terminal.ClearScreen();
        terminal.SetColor("bright_red");
        if (IsScreenReader)
            terminal.WriteLine($"  {Loc.Get("arena.choose_opponent")}");
        else
            terminal.WriteLine($"  {Loc.Get("arena.choose_opponent_box")}");
        terminal.WriteLine("");
        terminal.SetColor("bright_cyan");
        terminal.WriteLine($"  {Loc.Get("arena.attacks_remaining", GameConfig.MaxPvPAttacksPerDay - attacksToday)}");
        terminal.WriteLine("");

        // Get eligible opponents
        var allPlayers = await backend.GetAllPlayerSummaries();
        // Determine this account's base username (strip __alt suffix if present)
        string myAccount = SqlSaveBackend.GetAccountUsername(myUsername);
        var eligible = allPlayers
            .Where(p => !string.Equals(p.Username, myUsername, StringComparison.OrdinalIgnoreCase))
            // Block same-account PvP (main vs alt)
            .Where(p => !string.Equals(SqlSaveBackend.GetAccountUsername(p.Username), myAccount, StringComparison.OrdinalIgnoreCase))
            .Where(p => Math.Abs(p.Level - currentPlayer.Level) <= GameConfig.PvPLevelRangeLimit)
            .Where(p => p.Level >= GameConfig.MinPvPLevel)
            .Where(p => !backend.HasAttackedPlayerToday(myUsername, p.Username))
            .OrderByDescending(p => p.Level)
            .ToList();

        if (eligible.Count == 0)
        {
            terminal.SetColor("gray");
            terminal.WriteLine($"  {Loc.Get("arena.no_opponents")}");
            terminal.SetColor("darkgray");
            terminal.WriteLine($"  {Loc.Get("arena.level_range_note", GameConfig.PvPLevelRangeLimit)}");
            await terminal.PressAnyKey();
            return;
        }

        // Display opponent list
        terminal.SetColor("yellow");
        terminal.WriteLine($"  {Loc.Get("arena.col_num"),-4} {Loc.Get("arena.col_name"),-20} {Loc.Get("arena.col_level"),-8} {Loc.Get("arena.col_class"),-12} {Loc.Get("arena.col_status")}");
        if (!IsScreenReader)
        {
            terminal.SetColor("darkgray");
            terminal.WriteLine("  " + new string('─', 52));
        }

        for (int i = 0; i < eligible.Count; i++)
        {
            var p = eligible[i];
            string className = GetClassName(p.ClassId);
            terminal.SetColor("darkgray");
            terminal.Write($"  {i + 1,2}. ");
            terminal.SetColor("white");
            terminal.Write($"{p.DisplayName,-20}");
            terminal.SetColor("cyan");
            terminal.Write($"{Loc.Get("arena.lv_prefix")}{p.Level,-5}");
            terminal.SetColor("gray");
            terminal.Write($"{className,-12}");
            if (p.IsOnline)
            {
                terminal.SetColor("bright_green");
                terminal.Write(Loc.Get("arena.online_badge"));
            }
            terminal.WriteLine("");
        }

        terminal.WriteLine("");
        terminal.SetColor("white");
        var input = await terminal.GetInput($"  {Loc.Get("arena.choose_prompt")}");

        if (string.IsNullOrWhiteSpace(input) || input.Trim().ToUpper() == "Q")
            return;

        if (!int.TryParse(input.Trim(), out int selection) || selection < 1 || selection > eligible.Count)
        {
            terminal.SetColor("red");
            terminal.WriteLine($"  {Loc.Get("arena.invalid_selection")}");
            await Task.Delay(1000);
            return;
        }

        var target = eligible[selection - 1];

        // Confirm attack
        terminal.WriteLine("");
        terminal.SetColor("bright_red");
        terminal.WriteLine($"  {Loc.Get("arena.confirm_attack", target.DisplayName, target.Level, GetClassName(target.ClassId))}");
        terminal.SetColor("yellow");
        terminal.WriteLine($"  {Loc.Get("arena.warning_damage")}");
        terminal.WriteLine($"  {Loc.Get("arena.gold_steal_rule", GameConfig.PvPGoldStealPercent * 100)}");
        terminal.WriteLine("");
        var confirm = await terminal.GetInput($"  {Loc.Get("arena.confirm_prompt")}");

        if (confirm?.Trim().ToUpper() != "Y")
        {
            terminal.SetColor("gray");
            terminal.WriteLine($"  {Loc.Get("arena.step_back")}");
            await Task.Delay(1000);
            return;
        }

        // Load opponent's save data (use username, not display name)
        var opponentSave = await backend.ReadGameData(target.Username);
        if (opponentSave?.Player == null)
        {
            terminal.SetColor("red");
            terminal.WriteLine($"  {Loc.Get("arena.load_failed")}");
            await terminal.PressAnyKey();
            return;
        }

        // Shadows members resting at the Safe House are hidden from PvP attacks
        if (opponentSave.Player.SafeHouseResting)
        {
            terminal.SetColor("dark_magenta");
            terminal.WriteLine($"  {Loc.Get("arena.safe_house_hiding", target.DisplayName)}");
            terminal.SetColor("gray");
            terminal.WriteLine($"  {Loc.Get("arena.shadows_protect")}");
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
        terminal.WriteLine($"  {Loc.Get("arena.challenges", myName, target.DisplayName)}");
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
            DebugLogger.Instance.LogInfo("GOLD", $"ARENA VICTORY: {currentPlayer.DisplayName} stole {goldStolen:N0}g from {target.DisplayName} (gold now {currentPlayer.Gold:N0})");

            // Deduct gold from defender's save atomically
            if (goldStolen > 0)
                await backend.DeductGoldFromPlayer(defenderUsername, goldStolen);

            // Claim any bounties on the defeated player
            long bountyReward = await backend.ClaimBounties(defenderUsername, myUsername);

            // Fame from PvP victory
            currentPlayer.Fame += 10;

            // Display victory
            terminal.WriteLine("");
            terminal.SetColor("bright_green");
            if (IsScreenReader)
                terminal.WriteLine($"  {Loc.Get("arena.victory")}");
            else
                terminal.WriteLine($"  {Loc.Get("arena.victory_box")}");
            if (goldStolen > 0)
            {
                terminal.SetColor("yellow");
                terminal.WriteLine($"  {Loc.Get("arena.gold_stolen_from", target.DisplayName, $"{goldStolen:N0}")}");
            }
            if (bountyReward > 0)
            {
                currentPlayer.Gold += bountyReward;
                terminal.SetColor("bright_magenta");
                terminal.WriteLine($"  {Loc.Get("arena.bounty_collected", $"{bountyReward:N0}")}");
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
            currentPlayer.PoisonTurns = 0;

            // Fame loss from PvP defeat
            currentPlayer.Fame = Math.Max(0, currentPlayer.Fame - 5);

            terminal.WriteLine("");
            terminal.SetColor("bright_red");
            if (IsScreenReader)
                terminal.WriteLine($"  {Loc.Get("arena.defeat")}");
            else
                terminal.WriteLine($"  {Loc.Get("arena.defeat_box")}");
            terminal.SetColor("gray");
            terminal.WriteLine($"  {Loc.Get("arena.defeated_by", target.DisplayName)}");
            terminal.WriteLine("");
            terminal.SetColor("yellow");
            if (goldStolen > 0)
                terminal.WriteLine($"  {Loc.Get("arena.they_stole", target.DisplayName, $"{goldStolen:N0}")}");
            if (expLoss > 0)
                terminal.WriteLine($"  {Loc.Get("arena.lost_xp", $"{expLoss:N0}")}");
            if (goldLoss > 0)
                terminal.WriteLine($"  {Loc.Get("arena.lost_gold", $"{goldLoss:N0}")}");
            terminal.SetColor("cyan");
            terminal.WriteLine($"  {Loc.Get("arena.healed_at_inn", currentPlayer.HP, currentPlayer.MaxHP)}");
        }
        else if (attackerFled)
        {
            terminal.WriteLine("");
            terminal.SetColor("yellow");
            terminal.WriteLine($"  {Loc.Get("arena.fled")}");
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
                ? Loc.Get("arena.notify_attack_won", myName, $"{goldStolen:N0}")
                : Loc.Get("arena.notify_attack_lost", myName, $"{goldStolen:N0}");
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
        if (IsScreenReader)
            terminal.WriteLine($"  {Loc.Get("arena.leaderboard_header")}");
        else
            terminal.WriteLine($"  {Loc.Get("arena.leaderboard_header_box")}");
        terminal.WriteLine("");

        if (leaderboard.Count == 0)
        {
            terminal.SetColor("gray");
            terminal.WriteLine($"  {Loc.Get("arena.no_fights_yet")}");
        }
        else
        {
            terminal.SetColor("darkgray");
            terminal.WriteLine($"  {Loc.Get("arena.lb_col_num"),-4} {Loc.Get("arena.lb_col_name"),-20} {Loc.Get("arena.lb_col_wl"),-10} {Loc.Get("arena.lb_col_level"),-8} {Loc.Get("arena.lb_col_gold")}");
            if (!IsScreenReader)
            {
                terminal.SetColor("darkgray");
                terminal.WriteLine("  " + new string('─', 56));
            }

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
                    $"{Loc.Get("arena.lb_wl_format", entry.Wins, entry.Losses)}    {Loc.Get("arena.lv_prefix")}{entry.Level,-5} {entry.TotalGoldStolen:N0}g");
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
        if (IsScreenReader)
            terminal.WriteLine($"  {Loc.Get("arena.recent_header")}");
        else
            terminal.WriteLine($"  {Loc.Get("arena.recent_header_box")}");
        terminal.WriteLine("");

        if (fights.Count == 0)
        {
            terminal.SetColor("gray");
            terminal.WriteLine($"  {Loc.Get("arena.sand_undisturbed")}");
        }
        else
        {
            foreach (var fight in fights)
            {
                bool attackerWon = string.Equals(fight.WinnerUsername, fight.AttackerName, StringComparison.OrdinalIgnoreCase);
                terminal.SetColor("darkgray");
                terminal.Write($"  {GameConfig.FormatDate(fight.CreatedAt, currentPlayer.DateFormatPreference, includeTime: true)}  ");
                terminal.SetColor(attackerWon ? "bright_green" : "red");
                terminal.Write($"{fight.AttackerName}");
                terminal.SetColor("white");
                terminal.Write($" ({Loc.Get("arena.lv_prefix")}{fight.AttackerLevel}) {Loc.Get("arena.vs")} ");
                terminal.SetColor(!attackerWon ? "bright_green" : "red");
                terminal.Write($"{fight.DefenderName}");
                terminal.SetColor("white");
                terminal.Write($" ({Loc.Get("arena.lv_prefix")}{fight.DefenderLevel})");
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
        if (IsScreenReader)
            terminal.WriteLine($"  {Loc.Get("arena.pvp_record")}");
        else
            terminal.WriteLine($"  {Loc.Get("arena.pvp_record_box")}");
        terminal.WriteLine("");

        var stats = currentPlayer.Statistics;
        if (stats == null)
        {
            terminal.SetColor("gray");
            terminal.WriteLine($"  {Loc.Get("arena.no_stats")}");
        }
        else
        {
            terminal.SetColor("white");
            terminal.WriteLine($"  {Loc.Get("arena.pvp_kills", stats.TotalPlayerKills)}");
            terminal.WriteLine($"  {Loc.Get("arena.pvp_deaths", stats.TotalPlayerDeaths)}");

            double totalFights = stats.TotalPlayerKills + stats.TotalPlayerDeaths;
            double winRate = totalFights > 0
                ? (double)stats.TotalPlayerKills / totalFights * 100
                : 0;

            terminal.SetColor("yellow");
            terminal.WriteLine($"  {Loc.Get("arena.win_rate", $"{winRate:F1}")}");
            terminal.WriteLine($"  {Loc.Get("arena.total_fights", $"{totalFights:N0}")}");
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
