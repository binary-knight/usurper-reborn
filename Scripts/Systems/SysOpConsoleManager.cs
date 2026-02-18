using UsurperRemake.Utils;
using UsurperRemake.Systems;
using UsurperRemake.BBS;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;

namespace UsurperRemake.Systems
{
    /// <summary>
    /// Standalone SysOp Administration Console - runs from main menu before any save is loaded.
    /// This allows SysOps to manage the game, check for updates, and reset data without
    /// affecting any player state.
    /// </summary>
    public class SysOpConsoleManager
    {
        private readonly TerminalEmulator terminal;
        private Task? _updateCheckTask;
        private bool _updateCheckComplete = false;
        private bool _updateAvailable = false;
        private string _latestVersion = "";
        private bool _isOnlineMode;
        private OnlineAdminConsole? _onlineAdmin;

        public SysOpConsoleManager(TerminalEmulator term)
        {
            terminal = term;
        }

        private OnlineAdminConsole? GetOnlineAdmin()
        {
            if (_onlineAdmin != null) return _onlineAdmin;
            var sqlBackend = SaveSystem.Instance.Backend as SqlSaveBackend;
            if (sqlBackend == null) return null;
            _onlineAdmin = new OnlineAdminConsole(terminal, sqlBackend);
            return _onlineAdmin;
        }

        public async Task Run()
        {
            // Verify SysOp access - allow BBS sysops (security level) or online admins in BBS mode
            if (!DoorMode.IsInDoorMode || (!DoorMode.IsSysOp && !DoorMode.IsOnlineMode))
            {
                terminal.SetColor("red");
                terminal.WriteLine("ACCESS DENIED: SysOp privileges required.");
                await terminal.GetInputAsync("Press Enter to continue...");
                return;
            }

            _isOnlineMode = DoorMode.IsOnlineMode;

            // Start background update check
            StartBackgroundUpdateCheck();

            bool done = false;
            while (!done)
            {
                if (_isOnlineMode)
                {
                    DisplayOnlineConsole();

                    terminal.SetColor("gray");
                    terminal.Write("Choice: ");
                    var choice = await terminal.GetInputAsync("");
                    done = await ProcessOnlineChoice(choice);
                }
                else
                {
                    DisplayConsole();

                    terminal.SetColor("gray");
                    terminal.Write("Choice: ");
                    var choice = await terminal.GetInputAsync("");
                    done = await ProcessLocalChoice(choice);
                }
            }
        }

        private async Task<bool> ProcessLocalChoice(string choice)
        {
            switch (choice.ToUpper())
            {
                case "1":
                    await ViewAllPlayers();
                    break;
                case "2":
                    await DeletePlayer();
                    break;
                case "3":
                    await ResetGame();
                    break;
                case "4":
                    await ViewEditConfig();
                    break;
                case "5":
                    await SetMOTD();
                    break;
                case "6":
                    await ViewGameStatistics();
                    break;
                case "7":
                    await ViewDebugLog();
                    break;
                case "8":
                    await ViewActiveNPCs();
                    break;
                case "9":
                    await CheckForUpdates();
                    break;
                case "P":
                    await PardonPlayer();
                    break;
                case "I":
                    await SetIdleTimeout();
                    break;
                case "T":
                    await SetDefaultColorTheme();
                    break;
                case "Q":
                    return true;
            }
            return false;
        }

        private async Task<bool> ProcessOnlineChoice(string choice)
        {
            var admin = GetOnlineAdmin();
            if (admin == null)
            {
                terminal.SetColor("red");
                terminal.WriteLine("Error: SQL backend not available for online mode.");
                await terminal.GetInputAsync("Press Enter to continue...");
                return false;
            }

            var sqlBackend = SaveSystem.Instance.Backend as SqlSaveBackend;
            if (sqlBackend == null) return false;

            switch (choice.ToUpper())
            {
                case "1":
                    await ListAndEditPlayers(admin);
                    break;
                case "2":
                    await BanPlayerOnline(sqlBackend);
                    break;
                case "3":
                    await UnbanPlayerOnline(sqlBackend);
                    break;
                case "4":
                    await DeletePlayerOnline(sqlBackend);
                    break;
                case "P":
                    await PardonPlayerOnline(sqlBackend);
                    break;
                case "5":
                    await admin.EditDifficultySettings();
                    break;
                case "6":
                    await admin.SetMOTD();
                    break;
                case "7":
                    await admin.ViewOnlinePlayers();
                    break;
                case "8":
                    await ViewGameStatistics();
                    break;
                case "D":
                    await ViewDebugLog();
                    break;
                case "N":
                    await ViewActiveNPCs();
                    break;
                case "K":
                    await KickOnlinePlayer(sqlBackend);
                    break;
                case "V":
                    await ViewRecentNews(sqlBackend);
                    break;
                case "C":
                    await admin.ClearNews();
                    break;
                case "B":
                    await admin.BroadcastMessage();
                    break;
                case "U":
                    await CheckForUpdates();
                    break;
                case "W":
                    await admin.FullGameReset();
                    break;
                case "I":
                    await SetIdleTimeout();
                    break;
                case "T":
                    await SetDefaultColorTheme();
                    break;
                case "Q":
                    return true;
            }
            return false;
        }

        private void StartBackgroundUpdateCheck()
        {
            if (_updateCheckTask != null || VersionChecker.Instance.IsSteamBuild)
                return;

            _updateCheckComplete = false;
            _updateAvailable = false;
            _latestVersion = "";

            _updateCheckTask = Task.Run(async () =>
            {
                try
                {
                    await VersionChecker.Instance.CheckForUpdatesAsync();
                    if (!VersionChecker.Instance.CheckFailed)
                    {
                        _updateAvailable = VersionChecker.Instance.NewVersionAvailable;
                        _latestVersion = VersionChecker.Instance.LatestVersion;
                    }
                }
                catch
                {
                    // Silently ignore errors
                }
                finally
                {
                    _updateCheckComplete = true;
                }
            });
        }

        private void DisplayConsole()
        {
            terminal.ClearScreen();
            terminal.SetColor("bright_red");
            terminal.WriteLine(" ═══ SysOp Console ═══");
            terminal.SetColor("yellow");
            if (DoorMode.SessionInfo != null)
            {
                terminal.WriteLine($" {DoorMode.SessionInfo.UserName} (Lv {DoorMode.SessionInfo.SecurityLevel})  BBS: {DoorMode.SessionInfo.BBSName}");
            }

            if (_updateCheckComplete && _updateAvailable)
            {
                terminal.SetColor("bright_green");
                terminal.WriteLine($" ★ UPDATE: v{_latestVersion} available (current: {GameConfig.Version})");
            }

            terminal.SetColor("dark_gray");
            terminal.WriteLine(" ──────────────────────────────────────");
            terminal.SetColor("bright_cyan");
            terminal.WriteLine(" Players");
            terminal.SetColor("white");
            terminal.WriteLine("  [1] View All Players   [2] Delete Player");
            terminal.WriteLine("  [3] Reset Game         [P] Pardon Player");
            terminal.SetColor("dark_gray");
            terminal.WriteLine(" ──────────────────────────────────────");
            terminal.SetColor("bright_cyan");
            terminal.WriteLine(" Settings");
            terminal.SetColor("white");
            terminal.WriteLine("  [4] Difficulty          [5] Set MOTD");
            terminal.WriteLine($"  [I] Idle Timeout: {DoorMode.IdleTimeoutMinutes} min");
            terminal.WriteLine($"  [T] Color Theme: {ColorTheme.GetThemeName(GameConfig.DefaultColorTheme)}");
            terminal.SetColor("dark_gray");
            terminal.WriteLine(" ──────────────────────────────────────");
            terminal.SetColor("bright_cyan");
            terminal.WriteLine(" Monitoring");
            terminal.SetColor("white");
            terminal.WriteLine("  [6] Statistics          [7] Debug Log");
            terminal.WriteLine("  [8] Active NPCs");
            terminal.SetColor("dark_gray");
            terminal.WriteLine(" ──────────────────────────────────────");
            if (_updateCheckComplete && _updateAvailable)
            {
                terminal.SetColor("bright_green");
                terminal.WriteLine("  [9] Updates ★ NEW       [Q] Quit");
            }
            else
            {
                terminal.SetColor("white");
                terminal.WriteLine("  [9] Check for Updates   [Q] Quit");
            }
            terminal.WriteLine("");
        }

        private void DisplayOnlineConsole()
        {
            terminal.ClearScreen();
            terminal.SetColor("bright_red");
            terminal.WriteLine(" ═══ SysOp Console (Online) ═══");
            terminal.SetColor("yellow");
            if (DoorMode.SessionInfo != null)
            {
                terminal.WriteLine($" {DoorMode.SessionInfo.UserName} (Lv {DoorMode.SessionInfo.SecurityLevel})  BBS: {DoorMode.SessionInfo.BBSName}");
            }

            if (_updateCheckComplete && _updateAvailable)
            {
                terminal.SetColor("bright_green");
                terminal.WriteLine($" ★ UPDATE: v{_latestVersion} available (current: {GameConfig.Version})");
            }

            terminal.SetColor("dark_gray");
            terminal.WriteLine(" ──────────────────────────────────────");
            terminal.SetColor("bright_cyan");
            terminal.WriteLine(" Players");
            terminal.SetColor("white");
            terminal.WriteLine("  [1] List/Edit Players  [2] Ban Player");
            terminal.WriteLine("  [3] Unban Player       [4] Delete Player");
            terminal.WriteLine("  [P] Pardon Player");
            terminal.SetColor("dark_gray");
            terminal.WriteLine(" ──────────────────────────────────────");
            terminal.SetColor("bright_cyan");
            terminal.WriteLine(" Settings");
            terminal.SetColor("white");
            terminal.WriteLine("  [5] Difficulty          [6] Set MOTD");
            terminal.WriteLine($"  [I] Idle Timeout: {DoorMode.IdleTimeoutMinutes} min");
            terminal.WriteLine($"  [T] Color Theme: {ColorTheme.GetThemeName(GameConfig.DefaultColorTheme)}");
            terminal.SetColor("dark_gray");
            terminal.WriteLine(" ──────────────────────────────────────");
            terminal.SetColor("bright_cyan");
            terminal.WriteLine(" Monitoring");
            terminal.SetColor("white");
            terminal.WriteLine("  [7] Online Players      [8] Statistics");
            terminal.WriteLine("  [K] Kick Player         [D] Debug Log");
            terminal.WriteLine("  [N] Active NPCs");
            terminal.SetColor("dark_gray");
            terminal.WriteLine(" ──────────────────────────────────────");
            terminal.SetColor("bright_cyan");
            terminal.WriteLine(" World");
            terminal.SetColor("white");
            terminal.WriteLine("  [V] View News           [C] Clear News");
            terminal.WriteLine("  [B] Broadcast");
            terminal.SetColor("dark_gray");
            terminal.WriteLine(" ──────────────────────────────────────");
            if (_updateCheckComplete && _updateAvailable)
            {
                terminal.SetColor("bright_green");
                terminal.Write("  [U] Updates ★ NEW");
            }
            else
            {
                terminal.SetColor("white");
                terminal.Write("  [U] Updates");
            }
            terminal.SetColor("red");
            terminal.Write("             [W] Wipe");
            terminal.SetColor("gray");
            terminal.WriteLine("   [Q] Quit");
            terminal.WriteLine("");
        }

        #region Player Management

        private static readonly string[] ClassNames = {
            "Alchemist", "Assassin", "Barbarian", "Bard", "Cleric",
            "Jester", "Magician", "Paladin", "Ranger", "Sage", "Warrior"
        };

        private async Task ListAndEditPlayers(OnlineAdminConsole admin)
        {
            var sqlBackend = SaveSystem.Instance.Backend as SqlSaveBackend;
            if (sqlBackend == null) return;

            var players = await sqlBackend.GetAllPlayersDetailed();

            if (players.Count == 0)
            {
                terminal.ClearScreen();
                terminal.SetColor("yellow");
                terminal.WriteLine("No players found.");
                await terminal.GetInputAsync("Press Enter to continue...");
                return;
            }

            int pageSize = 15;
            int page = 0;
            int totalPages = (players.Count + pageSize - 1) / pageSize;

            while (true)
            {
                terminal.ClearScreen();
                terminal.SetColor("bright_cyan");
                terminal.WriteLine($" ═══ Players ({page + 1}/{totalPages}, {players.Count} total) ═══");
                terminal.SetColor("yellow");
                terminal.WriteLine($" {"#",-4}{"Name",-16}{"Lvl",4} {"Class",-11}{"Gold",10} {"Status",-8}");
                terminal.SetColor("dark_gray");
                terminal.WriteLine(" " + new string('─', 55));

                var pageItems = players.Skip(page * pageSize).Take(pageSize).ToList();
                for (int i = 0; i < pageItems.Count; i++)
                {
                    var p = pageItems[i];
                    int num = page * pageSize + i + 1;
                    string status = p.IsBanned ? "BANNED" : p.IsOnline ? "ONLINE" : "Off";
                    string cls = p.ClassId >= 0 && p.ClassId < ClassNames.Length ? ClassNames[p.ClassId] : "Unknown";
                    string color = p.IsBanned ? "red" : p.IsOnline ? "bright_green" : "gray";

                    terminal.SetColor(color);
                    terminal.WriteLine($" {num,-4}{p.DisplayName,-16}{p.Level,4} {cls,-11}{p.Gold,10:N0} {status,-8}");
                }

                terminal.WriteLine("");
                terminal.SetColor("white");
                string nav = "#=Edit  ";
                if (totalPages > 1)
                {
                    if (page > 0) nav += "[P]rev  ";
                    if (page < totalPages - 1) nav += "[N]ext  ";
                }
                nav += "[Q]uit";
                terminal.WriteLine($" {nav}");

                terminal.SetColor("gray");
                var choice = await terminal.GetInputAsync(" Choice: ");
                switch (choice.ToUpper())
                {
                    case "N":
                        if (page < totalPages - 1) page++;
                        break;
                    case "P":
                        if (page > 0) page--;
                        break;
                    case "Q":
                    case "":
                        return;
                    default:
                        // Try to parse as a player number
                        if (int.TryParse(choice, out int playerNum) && playerNum >= 1 && playerNum <= players.Count)
                        {
                            var selected = players[playerNum - 1];
                            await admin.EditPlayer(selected.Username);
                            // Refresh list after edit
                            players = await sqlBackend.GetAllPlayersDetailed();
                            totalPages = (players.Count + pageSize - 1) / pageSize;
                            if (page >= totalPages) page = Math.Max(0, totalPages - 1);
                        }
                        break;
                }
            }
        }

        private async Task BanPlayerOnline(SqlSaveBackend sqlBackend)
        {
            var players = (await sqlBackend.GetAllPlayersDetailed())
                .Where(p => !p.IsBanned)
                .OrderBy(p => p.DisplayName)
                .ToList();

            if (players.Count == 0)
            {
                terminal.ClearScreen();
                terminal.SetColor("green");
                terminal.WriteLine("No players available to ban.");
                await terminal.GetInputAsync("Press Enter...");
                return;
            }

            terminal.ClearScreen();
            terminal.SetColor("bright_red");
            terminal.WriteLine(" ═══ Ban Player ═══");
            terminal.SetColor("yellow");
            terminal.WriteLine($" {"#",-4}{"Name",-16}{"Lvl",4} {"Class",-11}{"Status",-8}");
            terminal.SetColor("dark_gray");
            terminal.WriteLine(" " + new string('─', 45));

            for (int i = 0; i < players.Count; i++)
            {
                var p = players[i];
                string cls = p.ClassId >= 0 && p.ClassId < ClassNames.Length ? ClassNames[p.ClassId] : "Unknown";
                terminal.SetColor(p.IsOnline ? "bright_green" : "gray");
                terminal.WriteLine($" {i + 1,-4}{p.DisplayName,-16}{p.Level,4} {cls,-11}{(p.IsOnline ? "ONLINE" : "Off"),-8}");
            }

            terminal.WriteLine("");
            terminal.SetColor("white");
            terminal.WriteLine(" Enter # to ban, [Q]uit");
            terminal.SetColor("gray");
            var input = await terminal.GetInputAsync(" Choice: ");

            if (string.IsNullOrWhiteSpace(input) || input.ToUpper() == "Q") return;
            if (!int.TryParse(input, out int sel) || sel < 1 || sel > players.Count) return;

            var target = players[sel - 1];
            if (string.Equals(target.Username, DoorMode.OnlineUsername, StringComparison.OrdinalIgnoreCase))
            {
                terminal.SetColor("red");
                terminal.WriteLine(" You cannot ban yourself!");
                await terminal.GetInputAsync(" Press Enter...");
                return;
            }

            terminal.SetColor("white");
            var reason = await terminal.GetInputAsync(" Ban reason: ");
            if (string.IsNullOrWhiteSpace(reason)) reason = "No reason given";

            terminal.SetColor("bright_red");
            var confirm = await terminal.GetInputAsync($" Ban '{target.DisplayName}'? (Y/N): ");
            if (confirm.ToUpper() != "Y") return;

            await sqlBackend.BanPlayer(target.Username, reason);
            terminal.SetColor("green");
            terminal.WriteLine($" {target.DisplayName} has been banned.");
            DebugLogger.Instance.LogInfo("SYSOP", $"Banned '{target.DisplayName}': {reason}");
            await terminal.GetInputAsync(" Press Enter...");
        }

        private async Task UnbanPlayerOnline(SqlSaveBackend sqlBackend)
        {
            var banned = await sqlBackend.GetBannedPlayers();

            terminal.ClearScreen();
            terminal.SetColor("bright_cyan");
            terminal.WriteLine(" ═══ Unban Player ═══");

            if (banned.Count == 0)
            {
                terminal.SetColor("green");
                terminal.WriteLine(" No banned players.");
                await terminal.GetInputAsync(" Press Enter...");
                return;
            }

            terminal.SetColor("dark_gray");
            terminal.WriteLine(" " + new string('─', 50));
            for (int i = 0; i < banned.Count; i++)
            {
                var (username, displayName, banReason) = banned[i];
                terminal.SetColor("red");
                terminal.Write($" {i + 1,-4}{displayName,-16}");
                terminal.SetColor("gray");
                terminal.WriteLine($" {banReason ?? "No reason"}");
            }

            terminal.WriteLine("");
            terminal.SetColor("white");
            terminal.WriteLine(" Enter # to unban, [Q]uit");
            terminal.SetColor("gray");
            var input = await terminal.GetInputAsync(" Choice: ");

            if (string.IsNullOrWhiteSpace(input) || input.ToUpper() == "Q") return;
            if (!int.TryParse(input, out int sel) || sel < 1 || sel > banned.Count) return;

            var target = banned[sel - 1];
            terminal.SetColor("yellow");
            var confirm = await terminal.GetInputAsync($" Unban '{target.displayName}'? (Y/N): ");
            if (confirm.ToUpper() != "Y") return;

            await sqlBackend.UnbanPlayer(target.username);
            terminal.SetColor("green");
            terminal.WriteLine($" {target.displayName} has been unbanned.");
            DebugLogger.Instance.LogInfo("SYSOP", $"Unbanned '{target.displayName}'");
            await terminal.GetInputAsync(" Press Enter...");
        }

        private async Task DeletePlayerOnline(SqlSaveBackend sqlBackend)
        {
            var players = (await sqlBackend.GetAllPlayersDetailed())
                .OrderBy(p => p.DisplayName)
                .ToList();

            if (players.Count == 0)
            {
                terminal.ClearScreen();
                terminal.SetColor("yellow");
                terminal.WriteLine("No players found.");
                await terminal.GetInputAsync("Press Enter...");
                return;
            }

            terminal.ClearScreen();
            terminal.SetColor("bright_red");
            terminal.WriteLine(" ═══ Delete Player ═══");
            terminal.SetColor("yellow");
            terminal.WriteLine($" {"#",-4}{"Name",-16}{"Lvl",4} {"Class",-11}{"Status",-8}");
            terminal.SetColor("dark_gray");
            terminal.WriteLine(" " + new string('─', 45));

            for (int i = 0; i < players.Count; i++)
            {
                var p = players[i];
                string cls = p.ClassId >= 0 && p.ClassId < ClassNames.Length ? ClassNames[p.ClassId] : "Unknown";
                string color = p.IsBanned ? "red" : p.IsOnline ? "bright_green" : "gray";
                string status = p.IsBanned ? "BANNED" : p.IsOnline ? "ONLINE" : "Off";
                terminal.SetColor(color);
                terminal.WriteLine($" {i + 1,-4}{p.DisplayName,-16}{p.Level,4} {cls,-11}{status,-8}");
            }

            terminal.WriteLine("");
            terminal.SetColor("white");
            terminal.WriteLine(" Enter # to delete, [Q]uit");
            terminal.SetColor("gray");
            var input = await terminal.GetInputAsync(" Choice: ");

            if (string.IsNullOrWhiteSpace(input) || input.ToUpper() == "Q") return;
            if (!int.TryParse(input, out int sel) || sel < 1 || sel > players.Count) return;

            var target = players[sel - 1];
            if (string.Equals(target.Username, DoorMode.OnlineUsername, StringComparison.OrdinalIgnoreCase))
            {
                terminal.SetColor("red");
                terminal.WriteLine(" You cannot delete your own account!");
                await terminal.GetInputAsync(" Press Enter...");
                return;
            }

            terminal.SetColor("bright_red");
            terminal.WriteLine($" Player: {target.DisplayName} (Lv {target.Level}, {target.Gold:N0} gold)");
            var confirm1 = await terminal.GetInputAsync(" Type DELETE to confirm: ");
            if (confirm1 != "DELETE") { terminal.SetColor("gray"); terminal.WriteLine(" Cancelled."); await terminal.GetInputAsync(" Press Enter..."); return; }

            var confirm2 = await terminal.GetInputAsync(" Type YES for final confirmation: ");
            if (confirm2 != "YES") { terminal.SetColor("gray"); terminal.WriteLine(" Cancelled."); await terminal.GetInputAsync(" Press Enter..."); return; }

            sqlBackend.DeleteGameData(target.Username);
            terminal.SetColor("green");
            terminal.WriteLine($" {target.DisplayName} has been permanently deleted.");
            DebugLogger.Instance.LogWarning("SYSOP", $"Deleted player '{target.DisplayName}'");
            await terminal.GetInputAsync(" Press Enter...");
        }

        private async Task PardonPlayerOnline(SqlSaveBackend sqlBackend)
        {
            var allPlayers = await sqlBackend.GetAllPlayersDetailed();
            var imprisoned = new List<(AdminPlayerInfo info, int daysInPrison, long darkness)>();

            foreach (var p in allPlayers)
            {
                var saveData = await sqlBackend.ReadGameData(p.Username);
                if (saveData?.Player == null) continue;
                if (saveData.Player.DaysInPrison > 0 || saveData.Player.Darkness > 100)
                {
                    imprisoned.Add((p, saveData.Player.DaysInPrison, saveData.Player.Darkness));
                }
            }

            terminal.ClearScreen();
            terminal.SetColor("bright_yellow");
            terminal.WriteLine(" ═══ Pardon Player ═══");

            if (imprisoned.Count == 0)
            {
                terminal.SetColor("green");
                terminal.WriteLine(" No players in prison or wanted.");
                await terminal.GetInputAsync(" Press Enter...");
                return;
            }

            terminal.SetColor("yellow");
            terminal.WriteLine($" {"#",-4}{"Name",-16}{"Lvl",4}  {"Prison",-10}{"Darkness",-10}");
            terminal.SetColor("dark_gray");
            terminal.WriteLine(" " + new string('─', 48));

            for (int i = 0; i < imprisoned.Count; i++)
            {
                var (info, days, dark) = imprisoned[i];
                terminal.SetColor(days > 0 ? "red" : "yellow");
                string prison = days > 0 ? $"{days} day(s)" : "-";
                string darkness = dark > 100 ? $"{dark} WANTED" : $"{dark}";
                terminal.WriteLine($" {i + 1,-4}{info.DisplayName,-16}{info.Level,4}  {prison,-10}{darkness,-10}");
            }

            terminal.WriteLine("");
            terminal.SetColor("white");
            terminal.WriteLine(" Enter # to pardon, [Q]uit");
            terminal.SetColor("gray");
            var input = await terminal.GetInputAsync(" Choice: ");

            if (string.IsNullOrWhiteSpace(input) || input.ToUpper() == "Q") return;
            if (!int.TryParse(input, out int sel) || sel < 1 || sel > imprisoned.Count) return;

            var target = imprisoned[sel - 1];
            terminal.SetColor("white");
            terminal.WriteLine($" {target.info.DisplayName}: Prison={target.daysInPrison}d, Darkness={target.darkness}");
            terminal.WriteLine("  [1] Release from prison");
            terminal.WriteLine("  [2] Clear Darkness");
            terminal.WriteLine("  [3] Full pardon (both)");
            terminal.WriteLine("  [Q] Cancel");
            var action = await terminal.GetInputAsync(" Choice: ");

            var saveData2 = await sqlBackend.ReadGameData(target.info.Username);
            if (saveData2?.Player == null) return;

            bool modified = false;
            switch (action)
            {
                case "1":
                    saveData2.Player.DaysInPrison = 0;
                    modified = true;
                    terminal.SetColor("green");
                    terminal.WriteLine(" Prison sentence cleared.");
                    break;
                case "2":
                    saveData2.Player.Darkness = 0;
                    modified = true;
                    terminal.SetColor("green");
                    terminal.WriteLine(" Darkness cleared.");
                    break;
                case "3":
                    saveData2.Player.DaysInPrison = 0;
                    saveData2.Player.Darkness = 0;
                    modified = true;
                    terminal.SetColor("green");
                    terminal.WriteLine(" Full pardon — prison and Darkness cleared.");
                    break;
                default:
                    return;
            }

            if (modified)
            {
                await sqlBackend.WriteGameData(target.info.Username, saveData2);
                DebugLogger.Instance.LogWarning("SYSOP", $"Pardoned '{target.info.DisplayName}' (Prison={saveData2.Player.DaysInPrison}, Darkness={saveData2.Player.Darkness})");
            }

            await terminal.GetInputAsync(" Press Enter...");
        }

        private async Task ViewAllPlayers()
        {
            terminal.ClearScreen();
            terminal.SetColor("bright_yellow");
            terminal.WriteLine("═══ ALL PLAYERS ═══");
            terminal.WriteLine("");

            try
            {
                var saveDir = SaveSystem.Instance.GetSaveDirectory();
                if (!Directory.Exists(saveDir))
                {
                    terminal.SetColor("gray");
                    terminal.WriteLine("No save directory found.");
                    await terminal.GetInputAsync("Press Enter to continue...");
                    return;
                }

                var saveFiles = Directory.GetFiles(saveDir, "*.json")
                    .Where(f => !Path.GetFileName(f).Contains("state") &&
                               !Path.GetFileName(f).Equals("sysop_config.json", StringComparison.OrdinalIgnoreCase))
                    .ToArray();

                if (saveFiles.Length == 0)
                {
                    terminal.SetColor("gray");
                    terminal.WriteLine("No player saves found.");
                    await terminal.GetInputAsync("Press Enter to continue...");
                    return;
                }

                terminal.SetColor("white");
                terminal.WriteLine($"Found {saveFiles.Length} save file(s):");
                terminal.WriteLine("");

                int index = 1;
                foreach (var file in saveFiles)
                {
                    var fileName = Path.GetFileNameWithoutExtension(file);
                    var fileInfo = new FileInfo(file);
                    var lastPlayed = fileInfo.LastWriteTime.ToString("yyyy-MM-dd HH:mm");
                    var fileSize = fileInfo.Length / 1024;

                    terminal.SetColor("cyan");
                    terminal.Write($"  [{index}] ");
                    terminal.SetColor("white");
                    terminal.Write($"{fileName}");
                    terminal.SetColor("gray");
                    terminal.WriteLine($" - Last played: {lastPlayed}, Size: {fileSize}KB");
                    index++;
                }

                terminal.WriteLine("");
            }
            catch (Exception ex)
            {
                terminal.SetColor("red");
                terminal.WriteLine($"Error reading saves: {ex.Message}");
            }

            terminal.SetColor("gray");
            await terminal.GetInputAsync("Press Enter to continue...");
        }

        private async Task DeletePlayer()
        {
            terminal.ClearScreen();
            terminal.SetColor("bright_red");
            terminal.WriteLine("═══ DELETE PLAYER ═══");
            terminal.WriteLine("");

            terminal.SetColor("yellow");
            terminal.WriteLine("WARNING: This will permanently delete a player's save file!");
            terminal.WriteLine("");

            terminal.SetColor("white");
            terminal.Write("Enter player name to delete (or blank to cancel): ");
            var playerName = await terminal.GetInputAsync("");

            if (string.IsNullOrWhiteSpace(playerName))
                return;

            var saveDir = SaveSystem.Instance.GetSaveDirectory();
            var savePath = Path.Combine(saveDir, $"{playerName}.json");

            if (!File.Exists(savePath))
            {
                terminal.SetColor("red");
                terminal.WriteLine($"Player '{playerName}' not found.");
                await terminal.GetInputAsync("Press Enter to continue...");
                return;
            }

            terminal.SetColor("bright_red");
            terminal.Write($"Are you SURE you want to delete '{playerName}'? Type YES to confirm: ");
            var confirm = await terminal.GetInputAsync("");

            if (confirm.ToUpper() == "YES")
            {
                try
                {
                    int filesDeleted = 0;
                    File.Delete(savePath);
                    filesDeleted++;
                    terminal.SetColor("gray");
                    terminal.WriteLine($"  Deleted: {playerName}.json");

                    var associatedPatterns = new[] { $"{playerName}_*.json", $"{playerName}.*.json" };
                    foreach (var pattern in associatedPatterns)
                    {
                        var associatedFiles = Directory.GetFiles(saveDir, pattern);
                        foreach (var file in associatedFiles)
                        {
                            File.Delete(file);
                            filesDeleted++;
                            terminal.WriteLine($"  Deleted: {Path.GetFileName(file)}");
                        }
                    }

                    terminal.SetColor("green");
                    terminal.WriteLine("");
                    terminal.WriteLine($"Player '{playerName}' deleted ({filesDeleted} file(s) removed).");
                    DebugLogger.Instance.LogWarning("SYSOP", $"SysOp deleted player: {playerName} ({filesDeleted} files)");
                }
                catch (Exception ex)
                {
                    terminal.SetColor("red");
                    terminal.WriteLine($"Error deleting player: {ex.Message}");
                }
            }
            else
            {
                terminal.SetColor("gray");
                terminal.WriteLine("Deletion cancelled.");
            }

            await terminal.GetInputAsync("Press Enter to continue...");
        }

        private async Task PardonPlayer()
        {
            terminal.ClearScreen();
            terminal.SetColor("bright_yellow");
            terminal.WriteLine("═══ PARDON PLAYER ═══");
            terminal.WriteLine("");

            terminal.SetColor("white");
            terminal.WriteLine("This releases a player from prison and/or reduces their Darkness.");
            terminal.WriteLine("");

            terminal.Write("Enter player name to pardon (or blank to cancel): ");
            var playerName = await terminal.GetInputAsync("");

            if (string.IsNullOrWhiteSpace(playerName))
                return;

            try
            {
                var saveBackend = SaveSystem.Instance.Backend;
                var saveData = await saveBackend.ReadGameData(playerName);

                if (saveData == null)
                {
                    terminal.SetColor("red");
                    terminal.WriteLine($"Player '{playerName}' not found.");
                    await terminal.GetInputAsync("Press Enter to continue...");
                    return;
                }

                // Show current status
                terminal.SetColor("white");
                terminal.WriteLine($"  Player: {saveData.Player.Name2 ?? playerName}");
                terminal.WriteLine($"  Level:  {saveData.Player.Level}");
                terminal.SetColor(saveData.Player.DaysInPrison > 0 ? "red" : "green");
                terminal.WriteLine($"  Prison: {saveData.Player.DaysInPrison} day(s)");
                terminal.SetColor(saveData.Player.Darkness > 100 ? "red" : "green");
                terminal.WriteLine($"  Darkness: {saveData.Player.Darkness}{(saveData.Player.Darkness > 100 ? " (WANTED)" : "")}");
                terminal.WriteLine("");

                // Offer options
                terminal.SetColor("white");
                terminal.WriteLine("  [1] Release from prison (set days to 0)");
                terminal.WriteLine("  [2] Clear Darkness (set to 0)");
                terminal.WriteLine("  [3] Full pardon (release + clear Darkness)");
                terminal.WriteLine("  [Q] Cancel");
                terminal.Write("Choice: ");
                var choice = await terminal.GetInputAsync("");

                bool modified = false;
                switch (choice.ToUpper())
                {
                    case "1":
                        saveData.Player.DaysInPrison = 0;
                        modified = true;
                        terminal.SetColor("green");
                        terminal.WriteLine("Prison sentence cleared.");
                        break;
                    case "2":
                        saveData.Player.Darkness = 0;
                        modified = true;
                        terminal.SetColor("green");
                        terminal.WriteLine("Darkness cleared.");
                        break;
                    case "3":
                        saveData.Player.DaysInPrison = 0;
                        saveData.Player.Darkness = 0;
                        modified = true;
                        terminal.SetColor("green");
                        terminal.WriteLine("Full pardon granted — prison and Darkness cleared.");
                        break;
                    default:
                        terminal.SetColor("gray");
                        terminal.WriteLine("Cancelled.");
                        await terminal.GetInputAsync("Press Enter to continue...");
                        return;
                }

                if (modified)
                {
                    await saveBackend.WriteGameData(playerName, saveData);

                    DebugLogger.Instance.LogWarning("SYSOP", $"SysOp pardoned player: {playerName} (Prison={saveData.Player.DaysInPrison}, Darkness={saveData.Player.Darkness})");
                    terminal.SetColor("bright_green");
                    terminal.WriteLine($"Player '{playerName}' has been pardoned and save updated.");
                }
            }
            catch (Exception ex)
            {
                terminal.SetColor("red");
                terminal.WriteLine($"Error pardoning player: {ex.Message}");
            }

            await terminal.GetInputAsync("Press Enter to continue...");
        }

        #endregion

        #region Game Reset

        private async Task ResetGame()
        {
            terminal.ClearScreen();
            terminal.SetColor("bright_red");
            terminal.WriteLine("╔══════════════════════════════════════════════════════════════════════════════╗");
            terminal.WriteLine("║                     !!! DANGER: GAME RESET !!!                               ║");
            terminal.WriteLine("╚══════════════════════════════════════════════════════════════════════════════╝");
            terminal.WriteLine("");

            terminal.SetColor("yellow");
            terminal.WriteLine("This will PERMANENTLY DELETE:");
            terminal.WriteLine("  - ALL player saves");
            terminal.WriteLine("  - ALL game state data");
            terminal.WriteLine("  - The game will start fresh as if newly installed");
            terminal.WriteLine("");

            terminal.SetColor("white");
            terminal.WriteLine("This action CANNOT be undone!");
            terminal.WriteLine("");

            terminal.SetColor("bright_red");
            terminal.Write("Type 'RESET GAME' to confirm: ");
            var confirm = await terminal.GetInputAsync("");

            if (confirm == "RESET GAME")
            {
                terminal.SetColor("yellow");
                terminal.Write("Final confirmation - Type 'YES' to proceed: ");
                var finalConfirm = await terminal.GetInputAsync("");

                if (finalConfirm.ToUpper() == "YES")
                {
                    await PerformGameReset();
                }
                else
                {
                    terminal.SetColor("gray");
                    terminal.WriteLine("Game reset cancelled.");
                }
            }
            else
            {
                terminal.SetColor("gray");
                terminal.WriteLine("Game reset cancelled.");
            }

            await terminal.GetInputAsync("Press Enter to continue...");
        }

        private async Task PerformGameReset()
        {
            terminal.SetColor("yellow");
            terminal.WriteLine("");
            terminal.WriteLine("Resetting game...");

            try
            {
                var saveDir = SaveSystem.Instance.GetSaveDirectory();

                if (Directory.Exists(saveDir))
                {
                    var files = Directory.GetFiles(saveDir, "*.json");
                    foreach (var file in files)
                    {
                        var fileName = Path.GetFileName(file);
                        if (fileName.Equals("sysop_config.json", StringComparison.OrdinalIgnoreCase))
                        {
                            terminal.SetColor("cyan");
                            terminal.WriteLine($"  Preserved: {fileName} (SysOp settings)");
                            continue;
                        }
                        File.Delete(file);
                        terminal.SetColor("gray");
                        terminal.WriteLine($"  Deleted: {fileName}");
                    }
                }

                terminal.SetColor("yellow");
                terminal.WriteLine("");
                terminal.WriteLine("Resetting game systems...");

                RomanceTracker.Instance.Reset();
                terminal.SetColor("gray");
                terminal.WriteLine("  Reset: Romance system");

                FamilySystem.Instance.Reset();
                terminal.WriteLine("  Reset: Family system");

                NPCSpawnSystem.Instance.ResetNPCs();
                terminal.WriteLine("  Reset: NPC spawn system");

                WorldSimulator.Instance?.ClearRespawnQueue();
                terminal.WriteLine("  Reset: World simulator queues");

                NPCMarriageRegistry.Instance.Reset();
                terminal.WriteLine("  Reset: NPC marriage registry");

                CompanionSystem.Instance.ResetAllCompanions();
                terminal.WriteLine("  Reset: Companion system");

                StoryProgressionSystem.Instance.FullReset();
                terminal.WriteLine("  Reset: Story progression");

                OceanPhilosophySystem.Instance.Reset();
                terminal.WriteLine("  Reset: Ocean philosophy");

                TownNPCStorySystem.Instance.Reset();
                terminal.WriteLine("  Reset: Town NPC stories");

                WorldInitializerSystem.Instance.ResetWorld();
                terminal.WriteLine("  Reset: World state");

                FactionSystem.Instance.Reset();
                terminal.WriteLine("  Reset: Faction system");

                ArchetypeTracker.Instance.Reset();
                terminal.WriteLine("  Reset: Archetype tracker");

                StrangerEncounterSystem.Instance.Reset();
                terminal.WriteLine("  Reset: Stranger encounters");

                DreamSystem.Instance.Reset();
                terminal.WriteLine("  Reset: Dream system");

                GriefSystem.Instance.Reset();
                terminal.WriteLine("  Reset: Grief system");

                GameEngine.Instance?.ClearDungeonParty();
                terminal.WriteLine("  Reset: Dungeon party");

                DebugLogger.Instance.LogWarning("SYSOP", "Full game reset performed by SysOp");

                terminal.SetColor("bright_green");
                terminal.WriteLine("");
                terminal.WriteLine("Game has been fully reset!");
                terminal.WriteLine("All players will need to create new characters.");
                terminal.WriteLine("SysOp configuration has been preserved.");
            }
            catch (Exception ex)
            {
                terminal.SetColor("red");
                terminal.WriteLine($"Error during reset: {ex.Message}");
                DebugLogger.Instance.LogError("SYSOP", $"Game reset failed: {ex.Message}");
            }

            await Task.CompletedTask;
        }

        #endregion

        #region Game Settings

        private async Task ViewEditConfig()
        {
            while (true)
            {
                terminal.ClearScreen();
                terminal.SetColor("bright_yellow");
                terminal.WriteLine("═══ GAME DIFFICULTY SETTINGS ═══");
                terminal.WriteLine("");

                terminal.SetColor("white");
                terminal.WriteLine("Current Settings:");
                terminal.WriteLine($"  XP Multiplier: {GameConfig.XPMultiplier:F1}x");
                terminal.WriteLine($"  Gold Multiplier: {GameConfig.GoldMultiplier:F1}x");
                terminal.WriteLine($"  Monster HP Multiplier: {GameConfig.MonsterHPMultiplier:F1}x");
                terminal.WriteLine($"  Monster Damage Multiplier: {GameConfig.MonsterDamageMultiplier:F1}x");
                terminal.WriteLine("");

                terminal.SetColor("gray");
                terminal.WriteLine("  (Values > 1.0 make the game easier/more rewarding)");
                terminal.WriteLine("  (Values < 1.0 make the game harder/less rewarding)");
                terminal.WriteLine("");

                terminal.SetColor("cyan");
                terminal.WriteLine("Edit Options:");
                terminal.SetColor("white");
                terminal.WriteLine("  [1] Set XP Multiplier");
                terminal.WriteLine("  [2] Set Gold Multiplier");
                terminal.WriteLine("  [3] Set Monster HP Multiplier");
                terminal.WriteLine("  [4] Set Monster Damage Multiplier");
                terminal.WriteLine("  [Q] Return to SysOp Menu");
                terminal.WriteLine("");

                terminal.SetColor("gray");
                terminal.Write("Choice: ");
                var choice = await terminal.GetInputAsync("");

                switch (choice.ToUpper())
                {
                    case "1":
                        terminal.Write("New XP multiplier (0.1-10.0): ");
                        var xpInput = await terminal.GetInputAsync("");
                        if (float.TryParse(xpInput, out float xp) && xp >= 0.1f && xp <= 10.0f)
                        {
                            GameConfig.XPMultiplier = xp;
                            SysOpConfigSystem.Instance.SaveConfig();
                            terminal.SetColor("green");
                            terminal.WriteLine($"XP multiplier set to {xp:F1}x (saved)");
                            DebugLogger.Instance.LogInfo("SYSOP", $"XP multiplier changed to {xp:F1}");
                        }
                        else
                        {
                            terminal.SetColor("red");
                            terminal.WriteLine($"Invalid input '{xpInput}'. Please enter a number between 0.1 and 10.0.");
                        }
                        await terminal.GetInputAsync("Press Enter to continue...");
                        break;

                    case "2":
                        terminal.Write("New gold multiplier (0.1-10.0): ");
                        var goldInput = await terminal.GetInputAsync("");
                        if (float.TryParse(goldInput, out float gold) && gold >= 0.1f && gold <= 10.0f)
                        {
                            GameConfig.GoldMultiplier = gold;
                            SysOpConfigSystem.Instance.SaveConfig();
                            terminal.SetColor("green");
                            terminal.WriteLine($"Gold multiplier set to {gold:F1}x (saved)");
                            DebugLogger.Instance.LogInfo("SYSOP", $"Gold multiplier changed to {gold:F1}");
                        }
                        else
                        {
                            terminal.SetColor("red");
                            terminal.WriteLine($"Invalid input '{goldInput}'. Please enter a number between 0.1 and 10.0.");
                        }
                        await terminal.GetInputAsync("Press Enter to continue...");
                        break;

                    case "3":
                        terminal.Write("New monster HP multiplier (0.1-10.0): ");
                        var hpInput = await terminal.GetInputAsync("");
                        if (float.TryParse(hpInput, out float hp) && hp >= 0.1f && hp <= 10.0f)
                        {
                            GameConfig.MonsterHPMultiplier = hp;
                            SysOpConfigSystem.Instance.SaveConfig();
                            terminal.SetColor("green");
                            terminal.WriteLine($"Monster HP multiplier set to {hp:F1}x (saved)");
                            DebugLogger.Instance.LogInfo("SYSOP", $"Monster HP multiplier changed to {hp:F1}");
                        }
                        else
                        {
                            terminal.SetColor("red");
                            terminal.WriteLine($"Invalid input '{hpInput}'. Please enter a number between 0.1 and 10.0.");
                        }
                        await terminal.GetInputAsync("Press Enter to continue...");
                        break;

                    case "4":
                        terminal.Write("New monster damage multiplier (0.1-10.0): ");
                        var dmgInput = await terminal.GetInputAsync("");
                        if (float.TryParse(dmgInput, out float dmg) && dmg >= 0.1f && dmg <= 10.0f)
                        {
                            GameConfig.MonsterDamageMultiplier = dmg;
                            SysOpConfigSystem.Instance.SaveConfig();
                            terminal.SetColor("green");
                            terminal.WriteLine($"Monster damage multiplier set to {dmg:F1}x (saved)");
                            DebugLogger.Instance.LogInfo("SYSOP", $"Monster damage multiplier changed to {dmg:F1}");
                        }
                        else
                        {
                            terminal.SetColor("red");
                            terminal.WriteLine($"Invalid input '{dmgInput}'. Please enter a number between 0.1 and 10.0.");
                        }
                        await terminal.GetInputAsync("Press Enter to continue...");
                        break;

                    case "Q":
                        return;
                }
            }
        }

        private async Task SetIdleTimeout()
        {
            terminal.ClearScreen();
            terminal.SetColor("bright_yellow");
            terminal.WriteLine("═══ IDLE TIMEOUT SETTINGS ═══");
            terminal.WriteLine("");

            terminal.SetColor("white");
            terminal.WriteLine($"Current idle timeout: {DoorMode.IdleTimeoutMinutes} minutes");
            terminal.WriteLine("");

            terminal.SetColor("gray");
            terminal.WriteLine("Players with no input for this many minutes will be");
            terminal.WriteLine("auto-saved and disconnected. Applies to BBS door and online modes.");
            terminal.WriteLine($"Valid range: {GameConfig.MinBBSIdleTimeoutMinutes}-{GameConfig.MaxBBSIdleTimeoutMinutes} minutes.");
            terminal.WriteLine("");

            terminal.SetColor("white");
            terminal.Write($"New idle timeout ({GameConfig.MinBBSIdleTimeoutMinutes}-{GameConfig.MaxBBSIdleTimeoutMinutes} minutes, or Q to cancel): ");
            var input = await terminal.GetInputAsync("");

            if (input.Trim().ToUpper() == "Q") return;

            if (int.TryParse(input, out int timeout) && timeout >= GameConfig.MinBBSIdleTimeoutMinutes && timeout <= GameConfig.MaxBBSIdleTimeoutMinutes)
            {
                DoorMode.IdleTimeoutMinutes = timeout;
                SysOpConfigSystem.Instance.SaveConfig();
                terminal.SetColor("green");
                terminal.WriteLine($"Idle timeout set to {timeout} minutes (saved).");
                DebugLogger.Instance.LogInfo("SYSOP", $"Idle timeout changed to {timeout} minutes");
            }
            else
            {
                terminal.SetColor("red");
                terminal.WriteLine($"Invalid input. Please enter a number between {GameConfig.MinBBSIdleTimeoutMinutes} and {GameConfig.MaxBBSIdleTimeoutMinutes}.");
            }
            await terminal.GetInputAsync("Press Enter to continue...");
        }

        private async Task SetDefaultColorTheme()
        {
            terminal.ClearScreen();
            terminal.SetColor("bright_yellow");
            terminal.WriteLine("═══ DEFAULT COLOR THEME ═══");
            terminal.WriteLine("");

            terminal.SetColor("white");
            terminal.WriteLine($"Current default: {ColorTheme.GetThemeName(GameConfig.DefaultColorTheme)}");
            terminal.SetColor("gray");
            terminal.WriteLine("New players will start with this theme.");
            terminal.WriteLine("Players can change their own theme in preferences.");
            terminal.WriteLine("");

            // List all themes
            var themes = new[] { ColorThemeType.Default, ColorThemeType.ClassicDark, ColorThemeType.AmberRetro, ColorThemeType.GreenPhosphor, ColorThemeType.HighContrast };
            for (int i = 0; i < themes.Length; i++)
            {
                var marker = themes[i] == GameConfig.DefaultColorTheme ? " ◄" : "";
                terminal.SetColor("white");
                terminal.Write($"  [{i + 1}] ");
                terminal.SetColor("cyan");
                terminal.Write($"{ColorTheme.GetThemeName(themes[i]),-20}");
                terminal.SetColor("gray");
                terminal.WriteLine($"{ColorTheme.GetThemeDescription(themes[i])}{marker}");
            }
            terminal.WriteLine("");

            terminal.SetColor("white");
            terminal.Write("Choose theme (1-5, or Q to cancel): ");
            var input = await terminal.GetInputAsync("");

            if (input.Trim().ToUpper() == "Q") return;

            if (int.TryParse(input, out int choice) && choice >= 1 && choice <= 5)
            {
                GameConfig.DefaultColorTheme = themes[choice - 1];
                SysOpConfigSystem.Instance.SaveConfig();
                terminal.SetColor("green");
                terminal.WriteLine($"Default theme set to: {ColorTheme.GetThemeName(GameConfig.DefaultColorTheme)} (saved).");
                DebugLogger.Instance.LogInfo("SYSOP", $"Default color theme changed to {GameConfig.DefaultColorTheme}");
            }
            else
            {
                terminal.SetColor("red");
                terminal.WriteLine("Invalid choice.");
            }
            await terminal.GetInputAsync("Press Enter to continue...");
        }

        private async Task SetMOTD()
        {
            terminal.ClearScreen();
            terminal.SetColor("bright_yellow");
            terminal.WriteLine("═══ MESSAGE OF THE DAY ═══");
            terminal.WriteLine("");

            var currentMOTD = GameConfig.MessageOfTheDay;

            terminal.SetColor("white");
            terminal.WriteLine("Current MOTD:");
            terminal.SetColor("cyan");
            terminal.WriteLine(string.IsNullOrEmpty(currentMOTD) ? "  (No MOTD set)" : $"  {currentMOTD}");
            terminal.WriteLine("");

            terminal.SetColor("white");
            terminal.WriteLine("Enter new MOTD (or blank to clear):");
            var newMOTD = await terminal.GetInputAsync("");

            GameConfig.MessageOfTheDay = newMOTD;
            SysOpConfigSystem.Instance.SaveConfig();

            terminal.SetColor("green");
            if (string.IsNullOrEmpty(newMOTD))
            {
                terminal.WriteLine("MOTD has been cleared (saved).");
            }
            else
            {
                terminal.WriteLine("MOTD has been updated (saved).");
            }

            DebugLogger.Instance.LogInfo("SYSOP", $"MOTD changed to: {newMOTD}");
            await terminal.GetInputAsync("Press Enter to continue...");
        }

        #endregion

        #region Monitoring

        private async Task ViewRecentNews(SqlSaveBackend sqlBackend)
        {
            terminal.ClearScreen();
            terminal.SetColor("bright_yellow");
            terminal.WriteLine("═══ RECENT NEWS ═══");
            terminal.WriteLine("");

            try
            {
                var news = await sqlBackend.GetRecentNews(50);
                if (news.Count == 0)
                {
                    terminal.SetColor("gray");
                    terminal.WriteLine("  No news entries.");
                }
                else
                {
                    terminal.SetColor("gray");
                    terminal.WriteLine($"  Showing {news.Count} most recent entries");
                    terminal.WriteLine("");

                    int page = 0;
                    int perPage = 15;
                    int totalPages = (news.Count + perPage - 1) / perPage;

                    while (true)
                    {
                        if (page > 0)
                        {
                            terminal.ClearScreen();
                            terminal.SetColor("bright_yellow");
                            terminal.WriteLine("═══ RECENT NEWS ═══");
                            terminal.WriteLine("");
                        }

                        int start = page * perPage;
                        int end = Math.Min(start + perPage, news.Count);

                        for (int i = start; i < end; i++)
                        {
                            var entry = news[i];
                            string timeAgo = FormatTimeAgo(entry.CreatedAt);
                            string cat = !string.IsNullOrEmpty(entry.Category) ? $"[{entry.Category}]" : "";
                            terminal.SetColor("dark_gray");
                            terminal.Write($"  {timeAgo} ");
                            terminal.SetColor("cyan");
                            terminal.Write(cat);
                            terminal.SetColor("white");
                            terminal.WriteLine($" {entry.Message}");
                        }

                        terminal.WriteLine("");
                        terminal.SetColor("gray");
                        if (totalPages > 1)
                        {
                            terminal.WriteLine($"  Page {page + 1}/{totalPages}");
                            string prompt = page < totalPages - 1 ? "[N]ext  [Q]uit: " : "[P]rev  [Q]uit: ";
                            if (page > 0 && page < totalPages - 1)
                                prompt = "[N]ext  [P]rev  [Q]uit: ";
                            var nav = await terminal.GetInputAsync(prompt);
                            if (nav.Equals("N", StringComparison.OrdinalIgnoreCase) && page < totalPages - 1)
                            {
                                page++;
                                continue;
                            }
                            else if (nav.Equals("P", StringComparison.OrdinalIgnoreCase) && page > 0)
                            {
                                page--;
                                continue;
                            }
                        }
                        else
                        {
                            await terminal.GetInputAsync("Press Enter to continue...");
                        }
                        break;
                    }
                }
            }
            catch (Exception ex)
            {
                terminal.SetColor("red");
                terminal.WriteLine($"Error: {ex.Message}");
                await terminal.GetInputAsync("Press Enter to continue...");
            }
        }

        private static string FormatTimeAgo(DateTime dt)
        {
            var span = DateTime.UtcNow - dt.ToUniversalTime();
            if (span.TotalMinutes < 1) return "just now";
            if (span.TotalMinutes < 60) return $"{(int)span.TotalMinutes}m ago";
            if (span.TotalHours < 24) return $"{(int)span.TotalHours}h ago";
            if (span.TotalDays < 7) return $"{(int)span.TotalDays}d ago";
            return dt.ToString("MM/dd");
        }

        private async Task KickOnlinePlayer(SqlSaveBackend sqlBackend)
        {
            terminal.ClearScreen();
            terminal.SetColor("bright_yellow");
            terminal.WriteLine("═══ KICK ONLINE PLAYER ═══");
            terminal.WriteLine("");

            try
            {
                var onlinePlayers = await sqlBackend.GetOnlinePlayers();
                if (onlinePlayers.Count == 0)
                {
                    terminal.SetColor("gray");
                    terminal.WriteLine("  No players currently online.");
                    terminal.WriteLine("");
                    await terminal.GetInputAsync("Press Enter to continue...");
                    return;
                }

                // Filter out self
                var myName = UsurperRemake.BBS.DoorMode.OnlineUsername ?? "";
                var kickable = onlinePlayers.Where(p =>
                    !p.Username.Equals(myName, StringComparison.OrdinalIgnoreCase)).ToList();

                if (kickable.Count == 0)
                {
                    terminal.SetColor("gray");
                    terminal.WriteLine("  No other players online to kick.");
                    terminal.WriteLine("");
                    await terminal.GetInputAsync("Press Enter to continue...");
                    return;
                }

                terminal.SetColor("white");
                for (int i = 0; i < kickable.Count; i++)
                {
                    var p = kickable[i];
                    string connTime = (DateTime.UtcNow - p.ConnectedAt.ToUniversalTime()).TotalMinutes < 60
                        ? $"{(int)(DateTime.UtcNow - p.ConnectedAt.ToUniversalTime()).TotalMinutes}m"
                        : $"{(int)(DateTime.UtcNow - p.ConnectedAt.ToUniversalTime()).TotalHours}h";
                    terminal.WriteLine($"  [{i + 1}] {p.DisplayName,-20} {p.Location,-15} {p.ConnectionType,-5} {connTime}");
                }

                terminal.WriteLine("");
                terminal.SetColor("gray");
                var input = await terminal.GetInputAsync("Player # to kick (Q to cancel): ");
                if (input.Equals("Q", StringComparison.OrdinalIgnoreCase) || string.IsNullOrEmpty(input))
                    return;

                if (int.TryParse(input, out int idx) && idx >= 1 && idx <= kickable.Count)
                {
                    var target = kickable[idx - 1];
                    terminal.SetColor("bright_yellow");
                    terminal.WriteLine($"  Kick {target.DisplayName}?");
                    var confirm = await terminal.GetInputAsync("  Type Y to confirm: ");
                    if (confirm.Equals("Y", StringComparison.OrdinalIgnoreCase))
                    {
                        await sqlBackend.UnregisterOnline(target.Username);
                        terminal.SetColor("bright_green");
                        terminal.WriteLine($"  {target.DisplayName} has been kicked.");
                        DebugLogger.Instance.LogInfo("SYSOP", $"Kicked player: {target.DisplayName}");
                    }
                    else
                    {
                        terminal.SetColor("gray");
                        terminal.WriteLine("  Cancelled.");
                    }
                }

                terminal.WriteLine("");
                await terminal.GetInputAsync("Press Enter to continue...");
            }
            catch (Exception ex)
            {
                terminal.SetColor("red");
                terminal.WriteLine($"Error: {ex.Message}");
                await terminal.GetInputAsync("Press Enter to continue...");
            }
        }

        private async Task ViewGameStatistics()
        {
            terminal.ClearScreen();
            terminal.SetColor("bright_yellow");
            terminal.WriteLine("═══ GAME STATISTICS ═══");

            try
            {
                if (_isOnlineMode)
                {
                    var sqlBackend = SaveSystem.Instance.Backend as SqlSaveBackend;
                    if (sqlBackend != null)
                    {
                        await ViewOnlineStatistics(sqlBackend);
                    }
                    else
                    {
                        terminal.SetColor("red");
                        terminal.WriteLine("  SQL backend not available.");
                    }
                }
                else
                {
                    await ViewLocalStatistics();
                }
            }
            catch (Exception ex)
            {
                terminal.SetColor("red");
                terminal.WriteLine($"Error: {ex.Message}");
            }

            terminal.SetColor("gray");
            await terminal.GetInputAsync("Press Enter to continue...");
        }

        private async Task ViewOnlineStatistics(SqlSaveBackend sqlBackend)
        {
            var s = await sqlBackend.GetGameStatistics();
            string className(int id) => id >= 0 && id <= 10 ? ((CharacterClass)id).ToString() : "?";

            // Players section
            terminal.SetColor("cyan");
            terminal.WriteLine("── Players ──");
            terminal.SetColor("white");
            terminal.WriteLine($"  Total: {s.TotalPlayers}    Active: {s.ActivePlayers}    Online: {s.OnlinePlayers}    Banned: {s.BannedPlayers}");
            terminal.WriteLine($"  Avg Level: {s.AverageLevel:F1}    Highest: {s.TopPlayerName} (Lv {s.TopPlayerLevel} {className(s.TopPlayerClassId)})");
            string popClass = s.MostPopularClassId >= 0 ? $"{className(s.MostPopularClassId)} ({s.MostPopularClassCount})" : "N/A";
            string newest = !string.IsNullOrEmpty(s.NewestPlayerName) ? s.NewestPlayerName : "N/A";
            terminal.WriteLine($"  Popular Class: {popClass}    Newest: {newest}");

            // Economy section
            terminal.SetColor("cyan");
            terminal.WriteLine("── Economy ──");
            terminal.SetColor("white");
            terminal.WriteLine($"  Gold in Circulation: {s.TotalGoldOnHand:N0}    Bank: {s.TotalBankGold:N0}");
            terminal.WriteLine($"  Total Earned: {s.TotalGoldEarned:N0}    Spent: {s.TotalGoldSpent:N0}");
            terminal.WriteLine($"  Items Bought: {s.TotalItemsBought:N0}    Sold: {s.TotalItemsSold:N0}");
            terminal.WriteLine($"  Bounties: {s.ActiveBounties}    Auctions: {s.ActiveAuctions}");

            // Combat section
            terminal.SetColor("cyan");
            terminal.WriteLine("── Combat ──");
            terminal.SetColor("white");
            terminal.WriteLine($"  Monsters Killed: {s.TotalMonstersKilled:N0}    Bosses: {s.TotalBossesKilled:N0}");
            terminal.WriteLine($"  PvP Fights: {s.TotalPvPFights}    PvP Kills: {s.TotalPvPKills}    PvE Deaths: {s.TotalPvEDeaths}");
            terminal.WriteLine($"  Total Damage Dealt: {s.TotalDamageDealt:N0}    Deepest Floor: {s.DeepestDungeon}");

            // World section
            terminal.SetColor("cyan");
            terminal.WriteLine("── World ──");
            terminal.SetColor("white");
            terminal.WriteLine($"  Active Teams: {s.ActiveTeams}    News: {s.NewsEntries}    Messages: {s.TotalMessages}");
            long hours = s.TotalPlaytimeMinutes / 60;
            string dbSize = s.DatabaseSizeBytes > 0 ? $"{s.DatabaseSizeBytes / 1024.0 / 1024.0:F1} MB" : "N/A";
            terminal.WriteLine($"  Total Playtime: {hours:N0}h    Database: {dbSize}");
            terminal.WriteLine("");
        }

        private async Task ViewLocalStatistics()
        {
            int playerCount = 0;
            var saveDir = SaveSystem.Instance.GetSaveDirectory();
            if (Directory.Exists(saveDir))
            {
                var saveFiles = Directory.GetFiles(saveDir, "*.json")
                    .Where(f => !Path.GetFileName(f).Contains("state") &&
                                !Path.GetFileName(f).Equals("sysop_config.json", StringComparison.OrdinalIgnoreCase))
                    .ToList();
                playerCount = saveFiles.Count;
            }

            terminal.SetColor("white");
            terminal.WriteLine($"  Total Players: {playerCount}");
            terminal.WriteLine("");

            terminal.SetColor("cyan");
            terminal.WriteLine("── NPCs ──");
            terminal.SetColor("white");
            var activeNPCs = NPCSpawnSystem.Instance.ActiveNPCs;
            terminal.WriteLine($"  Active: {activeNPCs.Count}    Dead: {activeNPCs.Count(n => n.IsDead)}    Married: {activeNPCs.Count(n => n.IsMarried)}");
            terminal.WriteLine("");

            terminal.SetColor("cyan");
            terminal.WriteLine("── Story ──");
            terminal.SetColor("white");
            var story = StoryProgressionSystem.Instance;
            terminal.WriteLine($"  Seals: {story.CollectedSeals.Count}/7    Chapter: {story.CurrentChapter}");
            var ocean = OceanPhilosophySystem.Instance;
            terminal.WriteLine($"  Awakening: {ocean.AwakeningLevel}/7    Wave Fragments: {ocean.CollectedFragments.Count}/10");
            terminal.WriteLine("");
        }

        private async Task ViewDebugLog()
        {
            try
            {
                var logPath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "logs", "debug.log");

                if (!File.Exists(logPath))
                {
                    terminal.ClearScreen();
                    terminal.SetColor("gray");
                    terminal.WriteLine("No debug log found.");
                    await terminal.GetInputAsync("Press Enter to continue...");
                    return;
                }

                var allLines = File.ReadAllLines(logPath);
                if (allLines.Length == 0)
                {
                    terminal.ClearScreen();
                    terminal.SetColor("gray");
                    terminal.WriteLine("Debug log is empty.");
                    await terminal.GetInputAsync("Press Enter to continue...");
                    return;
                }

                var lines = allLines.Reverse().ToList();
                int page = 0;
                int pageSize = 20;
                int totalPages = (lines.Count + pageSize - 1) / pageSize;

                while (true)
                {
                    terminal.ClearScreen();
                    terminal.SetColor("bright_yellow");
                    terminal.WriteLine($"═══ DEBUG LOG (Page {page + 1}/{totalPages}, {lines.Count} total lines) ═══");
                    terminal.WriteLine("");

                    var pageLines = lines.Skip(page * pageSize).Take(pageSize);

                    foreach (var line in pageLines)
                    {
                        if (line.Contains("[ERROR]"))
                            terminal.SetColor("red");
                        else if (line.Contains("[WARNING]"))
                            terminal.SetColor("yellow");
                        else if (line.Contains("[DEBUG]"))
                            terminal.SetColor("gray");
                        else
                            terminal.SetColor("white");

                        var displayLine = line.Length > 78 ? line.Substring(0, 75) + "..." : line;
                        terminal.WriteLine(displayLine);
                    }

                    terminal.WriteLine("");
                    terminal.SetColor("cyan");
                    terminal.WriteLine("[N]ewer entries, [O]lder entries, [Q]uit");
                    terminal.SetColor("gray");
                    var choice = await terminal.GetInputAsync("");

                    switch (choice.ToUpper())
                    {
                        case "N":
                            if (page > 0) page--;
                            break;
                        case "O":
                            if (page < totalPages - 1) page++;
                            break;
                        case "Q":
                            return;
                    }
                }
            }
            catch (Exception ex)
            {
                terminal.ClearScreen();
                terminal.SetColor("red");
                terminal.WriteLine($"Error reading log: {ex.Message}");
                await terminal.GetInputAsync("Press Enter to continue...");
            }
        }

        private async Task ViewActiveNPCs()
        {
            var npcs = NPCSpawnSystem.Instance.ActiveNPCs.OrderBy(n => n.Name).ToList();

            if (npcs.Count == 0)
            {
                terminal.ClearScreen();
                terminal.SetColor("gray");
                terminal.WriteLine("No active NPCs found.");
                await terminal.GetInputAsync("Press Enter to continue...");
                return;
            }

            int page = 0;
            int pageSize = 15;
            int totalPages = (npcs.Count + pageSize - 1) / pageSize;

            while (true)
            {
                terminal.ClearScreen();
                terminal.SetColor("bright_yellow");
                terminal.WriteLine($"═══ ACTIVE NPCs (Page {page + 1}/{totalPages}) ═══");
                terminal.WriteLine("");

                var pageNPCs = npcs.Skip(page * pageSize).Take(pageSize);

                foreach (var npc in pageNPCs)
                {
                    string status = npc.IsDead ? "[DEAD]" : $"HP:{npc.HP}/{npc.MaxHP}";
                    string married = npc.IsMarried ? " [MARRIED]" : "";

                    if (npc.IsDead)
                        terminal.SetColor("red");
                    else if (npc.HP < npc.MaxHP / 2)
                        terminal.SetColor("yellow");
                    else
                        terminal.SetColor("green");

                    terminal.WriteLine($"  {npc.Name} Lv{npc.Level} - {status}{married}");
                }

                terminal.WriteLine("");
                terminal.SetColor("gray");
                terminal.WriteLine("[N]ext page, [P]rev page, [Q]uit");
                var choice = await terminal.GetInputAsync("");

                if (choice.ToUpper() == "N" && page < totalPages - 1)
                    page++;
                else if (choice.ToUpper() == "P" && page > 0)
                    page--;
                else if (choice.ToUpper() == "Q")
                    break;
            }
        }

        #endregion

        #region System Maintenance

        private async Task CheckForUpdates()
        {
            terminal.ClearScreen();
            terminal.SetColor("bright_yellow");
            terminal.WriteLine("═══ CHECK FOR UPDATES ═══");
            terminal.WriteLine("");

            terminal.SetColor("white");
            terminal.WriteLine($"Current Version: {GameConfig.Version}");
            terminal.WriteLine("");

            if (VersionChecker.Instance.IsSteamBuild)
            {
                terminal.SetColor("cyan");
                terminal.WriteLine("This is a Steam build. Updates are handled automatically by Steam.");
                terminal.WriteLine("Please check Steam for available updates.");
                terminal.WriteLine("");
                await terminal.GetInputAsync("Press Enter to continue...");
                return;
            }

            terminal.SetColor("gray");
            terminal.WriteLine("Checking GitHub for updates...");
            terminal.WriteLine("");

            try
            {
                var checker = VersionChecker.Instance;
                await checker.CheckForUpdatesAsync();

                if (checker.CheckFailed)
                {
                    terminal.SetColor("red");
                    terminal.WriteLine("Failed to check for updates.");
                    terminal.WriteLine("Check your internet connection and try again.");
                    terminal.WriteLine("");
                    await terminal.GetInputAsync("Press Enter to continue...");
                    return;
                }

                if (!checker.NewVersionAvailable)
                {
                    terminal.SetColor("bright_green");
                    terminal.WriteLine("You are running the latest version!");
                    terminal.WriteLine("");
                    terminal.SetColor("gray");
                    terminal.WriteLine($"Current: {checker.CurrentVersion}");
                    terminal.WriteLine($"Latest:  {checker.LatestVersion}");
                    terminal.WriteLine("");
                    await terminal.GetInputAsync("Press Enter to continue...");
                    return;
                }

                // Update local state
                _updateAvailable = true;
                _latestVersion = checker.LatestVersion;
                _updateCheckComplete = true;

                terminal.SetColor("bright_yellow");
                terminal.WriteLine("╔══════════════════════════════════════════════════════════════════════════════╗");
                terminal.WriteLine("║                         NEW VERSION AVAILABLE                                ║");
                terminal.WriteLine("╚══════════════════════════════════════════════════════════════════════════════╝");
                terminal.WriteLine("");

                terminal.SetColor("white");
                terminal.WriteLine($"  Current version: {checker.CurrentVersion}");
                terminal.SetColor("bright_green");
                terminal.WriteLine($"  Latest version:  {checker.LatestVersion}");
                terminal.WriteLine("");

                if (!string.IsNullOrEmpty(checker.ReleaseNotes))
                {
                    terminal.SetColor("cyan");
                    terminal.WriteLine("Release Notes:");
                    terminal.SetColor("gray");
                    var notes = checker.ReleaseNotes.Length > 300
                        ? checker.ReleaseNotes.Substring(0, 300) + "..."
                        : checker.ReleaseNotes;
                    notes = notes.Replace("#", "").Replace("*", "").Replace("\r", "");
                    var lines = notes.Split('\n');
                    foreach (var line in lines.Take(8))
                    {
                        if (!string.IsNullOrWhiteSpace(line))
                            terminal.WriteLine($"  {line.Trim()}");
                    }
                    terminal.WriteLine("");
                }

                if (checker.CanAutoUpdate())
                {
                    terminal.SetColor("cyan");
                    terminal.WriteLine("Auto-update is available for your platform!");
                    terminal.WriteLine("");

                    terminal.SetColor("white");
                    terminal.WriteLine("Options:");
                    terminal.WriteLine("  [1] Download and install automatically");
                    terminal.WriteLine("  [2] Open download page in browser");
                    terminal.WriteLine("  [3] Skip for now");
                    terminal.WriteLine("");

                    terminal.SetColor("gray");
                    terminal.Write("Choice: ");
                    var choice = await terminal.GetInputAsync("");

                    switch (choice)
                    {
                        case "1":
                            await PerformAutoUpdate(checker);
                            break;
                        case "2":
                            checker.OpenDownloadPage();
                            terminal.SetColor("green");
                            terminal.WriteLine("");
                            terminal.WriteLine("Opening download page in browser...");
                            await terminal.GetInputAsync("Press Enter to continue...");
                            break;
                        default:
                            terminal.SetColor("gray");
                            terminal.WriteLine("Update skipped.");
                            await terminal.GetInputAsync("Press Enter to continue...");
                            break;
                    }
                }
                else
                {
                    terminal.SetColor("white");
                    terminal.WriteLine($"Download: {checker.ReleaseUrl}");
                    terminal.WriteLine("");

                    terminal.SetColor("cyan");
                    terminal.Write("Open download page in browser? (Y/N): ");
                    var response = await terminal.GetInputAsync("");

                    if (response.Trim().ToUpper() == "Y")
                    {
                        checker.OpenDownloadPage();
                        terminal.SetColor("green");
                        terminal.WriteLine("");
                        terminal.WriteLine("Opening download page in browser...");
                    }
                    await terminal.GetInputAsync("Press Enter to continue...");
                }
            }
            catch (Exception ex)
            {
                terminal.SetColor("red");
                terminal.WriteLine($"Error checking for updates: {ex.Message}");
                DebugLogger.Instance.LogError("SYSOP", $"Update check failed: {ex.Message}");
                await terminal.GetInputAsync("Press Enter to continue...");
            }
        }

        private async Task PerformAutoUpdate(VersionChecker checker)
        {
            terminal.WriteLine("");
            terminal.SetColor("yellow");
            terminal.WriteLine("Downloading update...");
            terminal.WriteLine("");

            terminal.SetColor("white");
            var lastProgress = 0;

            var success = await checker.DownloadAndInstallUpdateAsync(progress =>
            {
                if (progress >= lastProgress + 10 || progress == 100)
                {
                    int filled = progress / 5;
                    int empty = 20 - filled;
                    string bar = new string('█', filled) + new string('░', empty);
                    terminal.Write($"\r  [{bar}] {progress}%   ");
                    lastProgress = progress;
                }
            });

            terminal.WriteLine("");
            terminal.WriteLine("");

            if (success)
            {
                terminal.SetColor("bright_green");
                terminal.WriteLine("╔══════════════════════════════════════════════════════════════════════════════╗");
                terminal.WriteLine("║                     UPDATE DOWNLOADED SUCCESSFULLY                          ║");
                terminal.WriteLine("╚══════════════════════════════════════════════════════════════════════════════╝");
                terminal.WriteLine("");
                terminal.SetColor("white");
                terminal.WriteLine("The game will now close and update automatically.");
                terminal.WriteLine("All players will be disconnected briefly during the update.");
                terminal.WriteLine("");

                DebugLogger.Instance.LogWarning("SYSOP", $"SysOp initiated auto-update to version {checker.LatestVersion}");

                terminal.SetColor("yellow");
                terminal.Write("Press Enter to close the game and apply the update...");
                await terminal.GetInputAsync("");

                Environment.Exit(0);
            }
            else
            {
                terminal.SetColor("red");
                terminal.WriteLine($"Download failed: {checker.DownloadError}");
                terminal.WriteLine("");

                terminal.SetColor("gray");
                terminal.Write("Would you like to open the download page instead? (Y/N): ");
                var response = await terminal.GetInputAsync("");

                if (response.Trim().ToUpper() == "Y")
                {
                    checker.OpenDownloadPage();
                    terminal.SetColor("green");
                    terminal.WriteLine("");
                    terminal.WriteLine("Opening download page in browser...");
                }

                await terminal.GetInputAsync("Press Enter to continue...");
            }
        }

        #endregion
    }
}
