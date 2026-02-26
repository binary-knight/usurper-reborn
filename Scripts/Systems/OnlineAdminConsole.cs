using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using System.Threading.Tasks;
using UsurperRemake.BBS;
using UsurperRemake.Server;
using UsurperRemake.Utils;

namespace UsurperRemake.Systems
{
    /// <summary>
    /// Online Admin Console for managing the online multiplayer server.
    /// Only accessible to admin users (Rage, fastfinge) from the character selection screen.
    /// Operates on SqlSaveBackend for all data operations.
    /// </summary>
    public class OnlineAdminConsole
    {
        private readonly TerminalEmulator terminal;
        private readonly SqlSaveBackend backend;

        // Class names indexed by CharacterClass enum value (alphabetical base + prestige)
        private static readonly string[] ClassNames = {
            "Alchemist", "Assassin", "Barbarian", "Bard", "Cleric",
            "Jester", "Magician", "Paladin", "Ranger", "Sage", "Warrior",
            "Tidesworn", "Wavecaller", "Cyclebreaker", "Abysswarden", "Voidreaver"
        };

        public OnlineAdminConsole(TerminalEmulator term, SqlSaveBackend sqlBackend)
        {
            terminal = term;
            backend = sqlBackend;
        }

        /// <summary>
        /// Sanitize input by stripping non-printable characters.
        /// SSH/xterm.js terminal response sequences (from ANSI escape codes) can
        /// buffer into stdin and appear as garbage in the next ReadLine.
        /// </summary>
        private static string Sanitize(string input)
        {
            if (string.IsNullOrEmpty(input)) return "";
            var clean = new char[input.Length];
            int len = 0;
            for (int i = 0; i < input.Length; i++)
            {
                char c = input[i];
                if (c >= 32 && c < 127) // printable ASCII only
                    clean[len++] = c;
            }
            return new string(clean, 0, len).Trim();
        }

        /// <summary>
        /// Drain any buffered bytes from stdin (terminal ANSI responses, escape sequences).
        /// Must be called before Console.ReadLine() to prevent garbage in input.
        /// </summary>
        private void DrainPendingInput()
        {
            try
            {
                while (Console.KeyAvailable)
                {
                    Console.ReadKey(true); // consume without displaying
                }
            }
            catch (InvalidOperationException)
            {
                // Console.KeyAvailable not supported when stdin is fully redirected
            }
        }

        /// <summary>
        /// Read sanitized input from the terminal.
        /// Drains any buffered escape sequences from stdin before reading.
        /// </summary>
        private async Task<string> ReadInput(string prompt)
        {
            await Task.Delay(50);
            DrainPendingInput();
            return Sanitize(await terminal.GetInputAsync(prompt));
        }

        public async Task Run()
        {
            bool done = false;
            while (!done)
            {
                DisplayMenu();

                var choice = await ReadInput("Choice: ");

                switch (choice.ToUpper())
                {
                    case "1":
                        await ListAndEditPlayers();
                        break;
                    case "2":
                        await BanPlayer();
                        break;
                    case "3":
                        await UnbanPlayer();
                        break;
                    case "4":
                        await DeletePlayer();
                        break;
                    case "5":
                        await EditDifficultySettings();
                        break;
                    case "6":
                        await SetMOTD();
                        break;
                    case "7":
                        await ViewOnlinePlayers();
                        break;
                    case "8":
                        await ClearNews();
                        break;
                    case "9":
                        await BroadcastMessage();
                        break;
                    case "P":
                        await ResetPlayerPassword();
                        break;
                    case "I":
                        await ImmortalizePlayer();
                        break;
                    case "W":
                        await FullGameReset();
                        break;
                    case "Q":
                        done = true;
                        break;
                }
            }
        }

        private void DisplayMenu()
        {
            terminal.ClearScreen();
            terminal.SetColor("bright_red");
            terminal.WriteLine("╔══════════════════════════════════════════════════════════════════════════════╗");
            terminal.WriteLine("║                    O N L I N E   A D M I N   C O N S O L E                 ║");
            terminal.WriteLine("╚══════════════════════════════════════════════════════════════════════════════╝");
            terminal.WriteLine("");

            terminal.SetColor("yellow");
            terminal.WriteLine($"  Logged in as: {DoorMode.OnlineUsername ?? "Unknown"} (Admin)");
            terminal.WriteLine("");

            terminal.SetColor("bright_cyan");
            terminal.WriteLine("═══ PLAYER MANAGEMENT ═══");
            terminal.SetColor("white");
            terminal.WriteLine("  [1] List/Edit Players");
            terminal.WriteLine("  [2] Ban Player        [3] Unban Player");
            terminal.WriteLine("  [4] Delete Player     [P] Reset Password");
            terminal.WriteLine("");

            terminal.SetColor("bright_cyan");
            terminal.WriteLine("═══ GAME SETTINGS ═══");
            terminal.SetColor("white");
            terminal.WriteLine("  [5] Difficulty Settings");
            terminal.WriteLine("  [6] Set Message of the Day (MOTD)");
            terminal.WriteLine("");

            terminal.SetColor("bright_cyan");
            terminal.WriteLine("═══ WORLD MANAGEMENT ═══");
            terminal.SetColor("white");
            terminal.WriteLine("  [7] View Online Players");
            terminal.WriteLine("  [8] Clear News Feed   [9] Broadcast Message");
            terminal.WriteLine("");

            terminal.SetColor("bright_cyan");
            terminal.WriteLine("═══ GOD MANAGEMENT ═══");
            terminal.SetColor("white");
            terminal.WriteLine("  [I] Immortalize Player (Grant Godhood)");
            terminal.WriteLine("");

            terminal.SetColor("bright_red");
            terminal.WriteLine("═══ DANGER ZONE ═══");
            terminal.SetColor("red");
            terminal.WriteLine("  [W] Full Game Wipe/Reset");
            terminal.WriteLine("");

            terminal.SetColor("white");
            terminal.WriteLine("  [Q] Return to Character Selection");
            terminal.WriteLine("");
        }

        private string GetClassName(int classId)
        {
            if (classId >= 0 && classId < ClassNames.Length)
                return ClassNames[classId];
            return "Unknown";
        }

        private string GetPlayerStatus(AdminPlayerInfo p)
        {
            if (p.IsBanned) return "BANNED";
            if (p.IsOnline) return "ONLINE";
            return "Off";
        }

        private string GetStatusColor(AdminPlayerInfo p)
        {
            if (p.IsBanned) return "red";
            if (p.IsOnline) return "bright_green";
            return "gray";
        }

        // =====================================================================
        // Reusable numbered player list for single-select operations
        // =====================================================================

        /// <summary>
        /// Show a numbered list of players and let the admin select one.
        /// Returns the selected player or null on cancel.
        /// </summary>
        private async Task<AdminPlayerInfo?> ShowPlayerList(string title,
            List<AdminPlayerInfo> players)
        {
            if (players.Count == 0)
            {
                terminal.ClearScreen();
                terminal.SetColor("yellow");
                terminal.WriteLine("No players found.");
                await ReadInput("Press Enter to continue...");
                return null;
            }

            terminal.ClearScreen();
            terminal.SetColor("bright_cyan");
            terminal.WriteLine($"═══ {title} ═══");
            terminal.WriteLine("");

            terminal.SetColor("yellow");
            terminal.WriteLine($"  {"#",-4} {"Name",-16} {"Lvl",4} {"Class",-14} {"Gold",10} {"Status",-8}");
            terminal.SetColor("gray");
            terminal.WriteLine("  " + new string('─', 60));

            for (int i = 0; i < players.Count; i++)
            {
                var p = players[i];
                terminal.SetColor(GetStatusColor(p));
                terminal.WriteLine($"  {i + 1,-4} {p.DisplayName,-16} {p.Level,4} {GetClassName(p.ClassId),-14} {p.Gold,10:N0} {GetPlayerStatus(p),-8}");
            }

            terminal.WriteLine("");
            terminal.SetColor("white");
            terminal.WriteLine("  Enter # to select, [Q]uit");
            terminal.WriteLine("");

            var input = await ReadInput("Choice: ");
            if (string.IsNullOrWhiteSpace(input) || input.ToUpper() == "Q")
                return null;

            if (int.TryParse(input, out int sel) && sel >= 1 && sel <= players.Count)
                return players[sel - 1];

            terminal.SetColor("red");
            terminal.WriteLine("Invalid selection.");
            await Task.Delay(1000);
            return null;
        }

        // =====================================================================
        // Player Management
        // =====================================================================

        /// <summary>
        /// Paginated player list with number selection to edit.
        /// </summary>
        internal async Task ListAndEditPlayers()
        {
            var players = await backend.GetAllPlayersDetailed();

            if (players.Count == 0)
            {
                terminal.ClearScreen();
                terminal.SetColor("yellow");
                terminal.WriteLine("No players found.");
                await ReadInput("Press Enter to continue...");
                return;
            }

            int pageSize = 15;
            int page = 0;
            int totalPages = (players.Count + pageSize - 1) / pageSize;

            while (true)
            {
                terminal.ClearScreen();
                terminal.SetColor("bright_cyan");
                terminal.WriteLine($"═══ ALL PLAYERS (Page {page + 1}/{totalPages}, {players.Count} total) ═══");
                terminal.WriteLine("");

                // Header
                terminal.SetColor("yellow");
                terminal.WriteLine($"  {"#",-4} {"Name",-16} {"Lvl",4} {"Class",-14} {"Gold",10} {"Status",-8} {"Last Login",-12}");
                terminal.SetColor("gray");
                terminal.WriteLine("  " + new string('─', 72));

                var pageItems = players.Skip(page * pageSize).Take(pageSize).ToList();
                for (int i = 0; i < pageItems.Count; i++)
                {
                    var p = pageItems[i];
                    int num = page * pageSize + i + 1;
                    string lastLogin = p.LastLogin != null ? p.LastLogin.Substring(0, Math.Min(10, p.LastLogin.Length)) : "Never";

                    terminal.SetColor(GetStatusColor(p));
                    terminal.WriteLine($"  {num,-4} {p.DisplayName,-16} {p.Level,4} {GetClassName(p.ClassId),-14} {p.Gold,10:N0} {GetPlayerStatus(p),-8} {lastLogin,-12}");
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
                terminal.WriteLine($"  {nav}");
                terminal.WriteLine("");

                var choice = await ReadInput("Choice: ");
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
                            await EditPlayer(selected.Username);
                            // Refresh list after edit
                            players = await backend.GetAllPlayersDetailed();
                            totalPages = (players.Count + pageSize - 1) / pageSize;
                            if (page >= totalPages) page = Math.Max(0, totalPages - 1);
                        }
                        break;
                }
            }
        }

        internal async Task BanPlayer()
        {
            var players = (await backend.GetAllPlayersDetailed())
                .Where(p => !p.IsBanned)
                .OrderBy(p => p.DisplayName)
                .ToList();

            var target = await ShowPlayerList("BAN PLAYER", players);
            if (target == null) return;

            // Self-ban protection
            if (string.Equals(target.Username, DoorMode.OnlineUsername, StringComparison.OrdinalIgnoreCase))
            {
                terminal.SetColor("red");
                terminal.WriteLine("You cannot ban yourself!");
                await ReadInput("Press Enter to continue...");
                return;
            }

            terminal.SetColor("gray");
            terminal.WriteLine($"Player: {target.DisplayName} (Level {target.Level} {GetClassName(target.ClassId)})");
            terminal.WriteLine($"Status: {(target.IsOnline ? "ONLINE" : "Offline")}");
            terminal.WriteLine("");

            terminal.SetColor("white");
            var reason = await ReadInput("Enter ban reason: ");
            if (string.IsNullOrWhiteSpace(reason))
                reason = "No reason given";

            terminal.WriteLine("");
            terminal.SetColor("bright_red");
            var confirm = await ReadInput($"Ban '{target.DisplayName}'? (Y/N): ");
            if (confirm.ToUpper() != "Y")
            {
                terminal.SetColor("yellow");
                terminal.WriteLine("Ban cancelled.");
                await ReadInput("Press Enter to continue...");
                return;
            }

            await backend.BanPlayer(target.Username, reason);
            terminal.SetColor("green");
            terminal.WriteLine($"Player '{target.DisplayName}' has been banned.");
            terminal.WriteLine($"Reason: {reason}");
            if (target.IsOnline)
            {
                terminal.SetColor("yellow");
                terminal.WriteLine("Note: Player is currently online. They will be blocked on their next login.");
            }
            DebugLogger.Instance.LogInfo("ADMIN", $"Player '{target.DisplayName}' banned by {DoorMode.OnlineUsername}: {reason}");
            await ReadInput("Press Enter to continue...");
        }

        internal async Task UnbanPlayer()
        {
            terminal.ClearScreen();
            terminal.SetColor("bright_cyan");
            terminal.WriteLine("═══ UNBAN PLAYER ═══");
            terminal.WriteLine("");

            var banned = await backend.GetBannedPlayers();
            if (banned.Count == 0)
            {
                terminal.SetColor("green");
                terminal.WriteLine("No banned players found.");
                await ReadInput("Press Enter to continue...");
                return;
            }

            terminal.SetColor("white");
            terminal.WriteLine("Currently banned players:");
            terminal.WriteLine("");
            for (int i = 0; i < banned.Count; i++)
            {
                var (username, displayName, banReason) = banned[i];
                terminal.SetColor("red");
                terminal.Write($"  [{i + 1}] {displayName}");
                terminal.SetColor("gray");
                terminal.WriteLine($" - Reason: {banReason ?? "No reason given"}");
            }

            terminal.WriteLine("");
            terminal.SetColor("white");
            var input = await ReadInput("Enter number to unban (or blank to cancel): ");
            if (string.IsNullOrWhiteSpace(input) || !int.TryParse(input, out int selection) || selection < 1 || selection > banned.Count)
            {
                if (!string.IsNullOrWhiteSpace(input))
                {
                    terminal.SetColor("red");
                    terminal.WriteLine("Invalid selection.");
                    await ReadInput("Press Enter to continue...");
                }
                return;
            }

            var target = banned[selection - 1];
            terminal.SetColor("yellow");
            var confirm = await ReadInput($"Unban '{target.displayName}'? (Y/N): ");
            if (confirm.ToUpper() != "Y")
            {
                terminal.SetColor("yellow");
                terminal.WriteLine("Unban cancelled.");
                await ReadInput("Press Enter to continue...");
                return;
            }

            await backend.UnbanPlayer(target.username);
            terminal.SetColor("green");
            terminal.WriteLine($"Player '{target.displayName}' has been unbanned.");
            DebugLogger.Instance.LogInfo("ADMIN", $"Player '{target.displayName}' unbanned by {DoorMode.OnlineUsername}");
            await ReadInput("Press Enter to continue...");
        }

        internal async Task DeletePlayer()
        {
            var players = (await backend.GetAllPlayersDetailed())
                .OrderBy(p => p.DisplayName)
                .ToList();

            var target = await ShowPlayerList("DELETE PLAYER", players);
            if (target == null) return;

            // Self-delete protection
            if (string.Equals(target.Username, DoorMode.OnlineUsername, StringComparison.OrdinalIgnoreCase))
            {
                terminal.SetColor("red");
                terminal.WriteLine("You cannot delete your own account!");
                await ReadInput("Press Enter to continue...");
                return;
            }

            terminal.SetColor("gray");
            terminal.WriteLine($"Player: {target.DisplayName} (Level {target.Level} {GetClassName(target.ClassId)}, {target.Gold:N0} gold)");
            if (target.IsOnline)
            {
                terminal.SetColor("bright_yellow");
                terminal.WriteLine("WARNING: This player is currently ONLINE!");
            }
            terminal.WriteLine("");

            terminal.SetColor("bright_red");
            var confirm1 = await ReadInput("Type 'DELETE' to confirm: ");
            if (confirm1 != "DELETE")
            {
                terminal.SetColor("yellow");
                terminal.WriteLine("Deletion cancelled.");
                await ReadInput("Press Enter to continue...");
                return;
            }

            var confirm2 = await ReadInput("Final confirmation - Type 'YES' to proceed: ");
            if (confirm2 != "YES")
            {
                terminal.SetColor("yellow");
                terminal.WriteLine("Deletion cancelled.");
                await ReadInput("Press Enter to continue...");
                return;
            }

            backend.DeleteGameData(target.Username);
            terminal.SetColor("green");
            terminal.WriteLine($"Player '{target.DisplayName}' has been permanently deleted.");
            DebugLogger.Instance.LogWarning("ADMIN", $"Player '{target.DisplayName}' deleted by {DoorMode.OnlineUsername}");
            await ReadInput("Press Enter to continue...");
        }

        internal async Task ResetPlayerPassword()
        {
            var players = (await backend.GetAllPlayersDetailed())
                .OrderBy(p => p.DisplayName)
                .ToList();

            var target = await ShowPlayerList("RESET PLAYER PASSWORD", players);
            if (target == null) return;

            terminal.SetColor("yellow");
            terminal.WriteLine($"Setting new password for '{target.DisplayName}'.");
            terminal.WriteLine("The player will use this password to log in.");
            terminal.WriteLine("");

            terminal.SetColor("white");
            var newPassword = await ReadInput("Enter new password (min 4 chars): ");
            if (string.IsNullOrWhiteSpace(newPassword) || newPassword.Length < 4)
            {
                terminal.SetColor("red");
                terminal.WriteLine("Password must be at least 4 characters. Cancelled.");
                await ReadInput("Press Enter to continue...");
                return;
            }

            var (success, message) = backend.AdminResetPassword(target.Username, newPassword);
            terminal.SetColor(success ? "green" : "red");
            terminal.WriteLine(message);

            if (success)
                DebugLogger.Instance.LogWarning("ADMIN", $"Password reset for '{target.DisplayName}' by {DoorMode.OnlineUsername}");

            await ReadInput("Press Enter to continue...");
        }

        internal async Task EditPlayer(string username)
        {
            // Load the save data
            var saveData = await backend.ReadGameData(username);
            if (saveData?.Player == null)
            {
                terminal.SetColor("red");
                terminal.WriteLine($"No save data found for '{username}'.");
                await ReadInput("Press Enter to continue...");
                return;
            }

            // Check if online
            bool isOnline = await backend.IsPlayerOnline(username);
            if (isOnline)
            {
                terminal.SetColor("bright_yellow");
                terminal.WriteLine("WARNING: This player is currently ONLINE!");
                terminal.WriteLine("Changes may be overwritten when they save.");
                terminal.WriteLine("");
            }

            var player = saveData.Player;
            bool modified = false;

            while (true)
            {
                terminal.ClearScreen();
                terminal.SetColor("bright_cyan");
                terminal.WriteLine($"═══ EDITING: {player.Name2} ═══");
                if (modified)
                {
                    terminal.SetColor("bright_yellow");
                    terminal.WriteLine("  (unsaved changes)");
                }
                terminal.WriteLine("");

                // Display current stats
                terminal.SetColor("white");
                terminal.WriteLine($"  Level: {player.Level,-15} Experience: {player.Experience:N0}");
                terminal.WriteLine($"  HP: {player.HP}/{player.MaxHP,-12} Mana: {player.Mana}/{player.MaxMana}");
                terminal.WriteLine($"  Gold: {player.Gold:N0,-15} Bank: {player.BankGold:N0}");
                terminal.WriteLine($"  Class: {GetClassName((int)player.Class),-14} Race: {player.Race}");
                terminal.WriteLine("");
                terminal.SetColor("gray");
                terminal.WriteLine($"  STR: {player.Strength,-5} DEF: {player.Defence,-5} STA: {player.Stamina,-5} AGI: {player.Agility,-5} CHA: {player.Charisma}");
                terminal.WriteLine($"  DEX: {player.Dexterity,-5} WIS: {player.Wisdom,-5} INT: {player.Intelligence,-5} CON: {player.Constitution}");
                terminal.WriteLine("");

                terminal.SetColor("white");
                terminal.WriteLine("  [1] Level          [2] Experience     [3] Gold");
                terminal.WriteLine("  [4] Bank Gold      [5] HP/MaxHP       [6] Mana/MaxMana");
                terminal.WriteLine("  [7] Strength       [8] Defence        [9] Stamina");
                terminal.WriteLine("  [A] Agility        [B] Charisma       [C] Dexterity");
                terminal.WriteLine("  [D] Wisdom         [E] Intelligence   [F] Constitution");
                terminal.WriteLine("");
                terminal.SetColor("cyan");
                terminal.WriteLine("  [G] Manage Companions              [H] Manage Old Gods");
                terminal.WriteLine("");
                terminal.SetColor("green");
                terminal.WriteLine("  [S] Save Changes");
                terminal.SetColor("red");
                terminal.WriteLine("  [Q] Cancel (discard changes)");
                terminal.WriteLine("");

                var choice = await ReadInput("Choice: ");

                switch (choice.ToUpper())
                {
                    case "1":
                        player.Level = (int)await PromptNumericEdit("Level", player.Level, 1, 100);
                        modified = true;
                        break;
                    case "2":
                        player.Experience = await PromptNumericEdit("Experience", player.Experience, 0, long.MaxValue);
                        modified = true;
                        break;
                    case "3":
                        player.Gold = await PromptNumericEdit("Gold", player.Gold, 0, long.MaxValue);
                        modified = true;
                        break;
                    case "4":
                        player.BankGold = await PromptNumericEdit("Bank Gold", player.BankGold, 0, long.MaxValue);
                        modified = true;
                        break;
                    case "5":
                        player.MaxHP = await PromptNumericEdit("Max HP", player.MaxHP, 1, long.MaxValue);
                        player.BaseMaxHP = player.MaxHP;
                        player.HP = player.MaxHP;
                        modified = true;
                        break;
                    case "6":
                        player.MaxMana = await PromptNumericEdit("Max Mana", player.MaxMana, 0, long.MaxValue);
                        player.BaseMaxMana = player.MaxMana;
                        player.Mana = player.MaxMana;
                        modified = true;
                        break;
                    case "7":
                        player.Strength = await PromptNumericEdit("Strength", player.Strength, 1, 9999);
                        player.BaseStrength = player.Strength;
                        modified = true;
                        break;
                    case "8":
                        player.Defence = await PromptNumericEdit("Defence", player.Defence, 1, 9999);
                        player.BaseDefence = player.Defence;
                        modified = true;
                        break;
                    case "9":
                        player.Stamina = await PromptNumericEdit("Stamina", player.Stamina, 1, 9999);
                        player.BaseStamina = player.Stamina;
                        modified = true;
                        break;
                    case "A":
                        player.Agility = await PromptNumericEdit("Agility", player.Agility, 1, 9999);
                        player.BaseAgility = player.Agility;
                        modified = true;
                        break;
                    case "B":
                        player.Charisma = await PromptNumericEdit("Charisma", player.Charisma, 1, 9999);
                        player.BaseCharisma = player.Charisma;
                        modified = true;
                        break;
                    case "C":
                        player.Dexterity = await PromptNumericEdit("Dexterity", player.Dexterity, 1, 9999);
                        player.BaseDexterity = player.Dexterity;
                        modified = true;
                        break;
                    case "D":
                        player.Wisdom = await PromptNumericEdit("Wisdom", player.Wisdom, 1, 9999);
                        player.BaseWisdom = player.Wisdom;
                        modified = true;
                        break;
                    case "E":
                        player.Intelligence = await PromptNumericEdit("Intelligence", player.Intelligence, 1, 9999);
                        player.BaseIntelligence = player.Intelligence;
                        modified = true;
                        break;
                    case "F":
                        player.Constitution = await PromptNumericEdit("Constitution", player.Constitution, 1, 9999);
                        player.BaseConstitution = player.Constitution;
                        modified = true;
                        break;
                    case "G":
                        if (await EditCompanions(saveData))
                            modified = true;
                        break;
                    case "H":
                        if (await EditOldGods(saveData))
                            modified = true;
                        break;
                    case "S":
                        if (!modified)
                        {
                            terminal.SetColor("yellow");
                            terminal.WriteLine("No changes to save.");
                            await Task.Delay(1000);
                            break;
                        }
                        saveData.Player = player;
                        var success = await backend.WriteGameData(username.ToLower(), saveData);
                        if (success)
                        {
                            terminal.SetColor("green");
                            terminal.WriteLine($"Changes saved for '{player.Name2}'!");
                            DebugLogger.Instance.LogInfo("ADMIN", $"Player '{player.Name2}' edited by {DoorMode.OnlineUsername}");

                            // Apply changes to live in-memory session if player is online
                            ApplyEditsToLiveSession(username, player);
                            ApplyStoryEditsToLiveSession(username, saveData);
                        }
                        else
                        {
                            terminal.SetColor("red");
                            terminal.WriteLine("Failed to save changes!");
                        }
                        await ReadInput("Press Enter to continue...");
                        return;
                    case "Q":
                        if (modified)
                        {
                            terminal.SetColor("yellow");
                            var discard = await ReadInput("Discard unsaved changes? (Y/N): ");
                            if (discard.ToUpper() != "Y")
                                break;
                        }
                        return;
                }
            }
        }

        /// <summary>
        /// Apply admin edits to a live in-memory player session.
        /// Without this, the player's next autosave would overwrite DB changes.
        /// </summary>
        private void ApplyEditsToLiveSession(string username, PlayerData edited)
        {
            var server = MudServer.Instance;
            if (server == null) return;

            var key = username.ToLowerInvariant();
            if (!server.ActiveSessions.TryGetValue(key, out var session)) return;

            var livePlayer = session.Context?.Engine?.CurrentPlayer;
            if (livePlayer == null) return;

            livePlayer.Level = edited.Level;
            livePlayer.Experience = edited.Experience;
            livePlayer.Gold = edited.Gold;
            livePlayer.BankGold = edited.BankGold;
            livePlayer.BaseMaxHP = edited.MaxHP;
            livePlayer.HP = edited.HP;
            livePlayer.BaseMaxMana = edited.MaxMana;
            livePlayer.Mana = edited.Mana;
            livePlayer.BaseStrength = edited.Strength;
            livePlayer.BaseDefence = edited.Defence;
            livePlayer.Stamina = edited.Stamina;
            livePlayer.BaseAgility = edited.Agility;
            livePlayer.Charisma = edited.Charisma;
            livePlayer.BaseDexterity = edited.Dexterity;
            livePlayer.Wisdom = edited.Wisdom;
            livePlayer.Intelligence = edited.Intelligence;
            livePlayer.Constitution = edited.Constitution;
            livePlayer.RecalculateStats();

            terminal.SetColor("cyan");
            terminal.WriteLine("  (Live session updated)");
        }

        // ──────────────────────────────────────────────────────────────
        //  Companion & Old God Admin Editors
        // ──────────────────────────────────────────────────────────────

        private static readonly string[] CompanionNames = { "Lyris", "Aldric", "Mira", "Vex" };

        private static readonly string[] GodNames =
        {
            "Maelketh (War, Floor 25)",
            "Veloura (Love, Floor 40)",
            "Thorgrim (Law, Floor 55)",
            "Noctura (Shadow, Floor 70)",
            "Aurelion (Light, Floor 85)",
            "Terravok (Earth, Floor 95)",
            "Manwe (Creation, Floor 100)"
        };

        private static readonly string[] GodStatusNames =
        {
            "Unknown", "Imprisoned", "Dormant", "Dying", "Corrupted",
            "Neutral", "Awakened", "Hostile", "Allied", "Saved", "Defeated", "Consumed"
        };

        /// <summary>
        /// Submenu for managing a player's companion states (resurrect, kill, un-recruit).
        /// </summary>
        private async Task<bool> EditCompanions(SaveGameData saveData)
        {
            var ss = saveData.StorySystems;
            bool changed = false;

            while (true)
            {
                terminal.ClearScreen();
                terminal.SetColor("bright_cyan");
                terminal.WriteLine($"═══ COMPANIONS: {saveData.Player.Name2} ═══");
                terminal.WriteLine("");

                // Display each companion's status
                for (int i = 0; i < CompanionNames.Length; i++)
                {
                    var comp = ss.Companions?.FirstOrDefault(c => c.Id == i);
                    string status;
                    string color;

                    if (comp == null)
                    {
                        status = "No data";
                        color = "dark_gray";
                    }
                    else if (comp.IsDead)
                    {
                        var deathInfo = ss.FallenCompanions?.FirstOrDefault(f => f.CompanionId == i);
                        string deathDetail = deathInfo != null ? $": \"{deathInfo.Circumstance}\"" : "";
                        status = $"DEAD{deathDetail}";
                        color = "dark_red";
                    }
                    else if (comp.IsRecruited && comp.IsActive)
                    {
                        status = $"Active, Level {comp.Level}";
                        color = "bright_green";
                    }
                    else if (comp.IsRecruited)
                    {
                        status = $"Recruited (at Inn), Level {comp.Level}";
                        color = "yellow";
                    }
                    else
                    {
                        status = "Not Recruited";
                        color = "gray";
                    }

                    terminal.SetColor("white");
                    terminal.Write($"  [{i}] {CompanionNames[i],-10}— ");
                    terminal.SetColor(color);
                    terminal.WriteLine(status);
                }

                // Show grief info if any
                if (ss.ActiveGriefs != null && ss.ActiveGriefs.Count > 0)
                {
                    terminal.WriteLine("");
                    terminal.SetColor("dark_red");
                    terminal.WriteLine("  Active Griefs:");
                    foreach (var g in ss.ActiveGriefs)
                    {
                        terminal.SetColor("gray");
                        terminal.WriteLine($"    {g.CompanionName} — Stage {g.CurrentStage}");
                    }
                }

                terminal.WriteLine("");
                terminal.SetColor("white");
                terminal.WriteLine("  [R] Resurrect a companion");
                terminal.WriteLine("  [K] Kill a companion");
                terminal.WriteLine("  [U] Un-recruit (reset to factory)");
                terminal.SetColor("gray");
                terminal.WriteLine("  [Q] Back");
                terminal.WriteLine("");

                var choice = await ReadInput("Choice: ");

                switch (choice.ToUpper())
                {
                    case "R":
                    {
                        var idx = await PromptCompanionIndex("Resurrect which companion?");
                        if (idx < 0) break;

                        var comp = ss.Companions?.FirstOrDefault(c => c.Id == idx);
                        if (comp == null)
                        {
                            terminal.SetColor("red");
                            terminal.WriteLine("  No companion data found.");
                            await Task.Delay(1000);
                            break;
                        }
                        if (!comp.IsDead)
                        {
                            terminal.SetColor("yellow");
                            terminal.WriteLine($"  {CompanionNames[idx]} is not dead.");
                            await Task.Delay(1000);
                            break;
                        }

                        // Resurrect
                        comp.IsDead = false;
                        comp.IsRecruited = true; // keep recruited

                        // Remove from fallen companions
                        ss.FallenCompanions?.RemoveAll(f => f.CompanionId == idx);

                        // Clear grief entries
                        ss.ActiveGriefs?.RemoveAll(g => g.CompanionId == idx);
                        ss.GriefMemories?.RemoveAll(m => m.CompanionId == idx);

                        // Clear legacy grief fields if they reference this companion
                        if (ss.GriefCompanionName == CompanionNames[idx])
                        {
                            ss.GriefStage = 0;
                            ss.GriefDaysRemaining = 0;
                            ss.GriefCompanionName = "";
                        }

                        terminal.SetColor("bright_green");
                        terminal.WriteLine($"  {CompanionNames[idx]} has been resurrected!");
                        terminal.SetColor("gray");
                        terminal.WriteLine("  (Cleared: IsDead, FallenCompanions, ActiveGriefs, GriefMemories)");
                        changed = true;
                        await Task.Delay(1500);
                        break;
                    }
                    case "K":
                    {
                        var idx = await PromptCompanionIndex("Kill which companion?");
                        if (idx < 0) break;

                        var comp = ss.Companions?.FirstOrDefault(c => c.Id == idx);
                        if (comp == null)
                        {
                            terminal.SetColor("red");
                            terminal.WriteLine("  No companion data found.");
                            await Task.Delay(1000);
                            break;
                        }
                        if (comp.IsDead)
                        {
                            terminal.SetColor("yellow");
                            terminal.WriteLine($"  {CompanionNames[idx]} is already dead.");
                            await Task.Delay(1000);
                            break;
                        }

                        comp.IsDead = true;
                        comp.IsActive = false;
                        ss.ActiveCompanionIds?.Remove(idx);

                        // Add to fallen if not already there
                        if (ss.FallenCompanions == null)
                            ss.FallenCompanions = new List<CompanionDeathInfo>();
                        if (!ss.FallenCompanions.Any(f => f.CompanionId == idx))
                        {
                            ss.FallenCompanions.Add(new CompanionDeathInfo
                            {
                                CompanionId = idx,
                                DeathType = 0, // Combat
                                Circumstance = "Admin kill",
                                LastWords = "",
                                DeathDay = saveData.CurrentDay
                            });
                        }

                        terminal.SetColor("dark_red");
                        terminal.WriteLine($"  {CompanionNames[idx]} has been killed.");
                        changed = true;
                        await Task.Delay(1500);
                        break;
                    }
                    case "U":
                    {
                        var idx = await PromptCompanionIndex("Un-recruit which companion?");
                        if (idx < 0) break;

                        var comp = ss.Companions?.FirstOrDefault(c => c.Id == idx);
                        if (comp == null)
                        {
                            terminal.SetColor("red");
                            terminal.WriteLine("  No companion data found.");
                            await Task.Delay(1000);
                            break;
                        }
                        if (!comp.IsRecruited && !comp.IsDead)
                        {
                            terminal.SetColor("yellow");
                            terminal.WriteLine($"  {CompanionNames[idx]} is already in factory state.");
                            await Task.Delay(1000);
                            break;
                        }

                        // Reset to factory
                        comp.IsRecruited = false;
                        comp.IsActive = false;
                        comp.IsDead = false;
                        comp.PersonalQuestStarted = false;
                        comp.PersonalQuestCompleted = false;
                        ss.ActiveCompanionIds?.Remove(idx);
                        ss.FallenCompanions?.RemoveAll(f => f.CompanionId == idx);
                        ss.ActiveGriefs?.RemoveAll(g => g.CompanionId == idx);
                        ss.GriefMemories?.RemoveAll(m => m.CompanionId == idx);

                        if (ss.GriefCompanionName == CompanionNames[idx])
                        {
                            ss.GriefStage = 0;
                            ss.GriefDaysRemaining = 0;
                            ss.GriefCompanionName = "";
                        }

                        terminal.SetColor("cyan");
                        terminal.WriteLine($"  {CompanionNames[idx]} has been reset to factory state.");
                        terminal.SetColor("gray");
                        terminal.WriteLine("  They can now be re-encountered and recruited.");
                        changed = true;
                        await Task.Delay(1500);
                        break;
                    }
                    case "Q":
                    case "":
                        return changed;
                }
            }
        }

        private async Task<int> PromptCompanionIndex(string prompt)
        {
            terminal.SetColor("white");
            var input = await ReadInput($"  {prompt} (0-3, or Q): ");
            if (string.IsNullOrWhiteSpace(input) || input.ToUpper() == "Q")
                return -1;
            if (int.TryParse(input, out int idx) && idx >= 0 && idx < CompanionNames.Length)
                return idx;
            terminal.SetColor("red");
            terminal.WriteLine("  Invalid selection.");
            await Task.Delay(800);
            return -1;
        }

        /// <summary>
        /// Submenu for managing a player's Old God encounter states.
        /// </summary>
        private async Task<bool> EditOldGods(SaveGameData saveData)
        {
            var ss = saveData.StorySystems;
            if (ss.OldGodStates == null)
                ss.OldGodStates = new Dictionary<int, int>();
            bool changed = false;

            while (true)
            {
                terminal.ClearScreen();
                terminal.SetColor("bright_cyan");
                terminal.WriteLine($"═══ OLD GODS: {saveData.Player.Name2} ═══");
                terminal.WriteLine("");

                for (int i = 0; i < GodNames.Length; i++)
                {
                    ss.OldGodStates.TryGetValue(i, out int statusInt);
                    string statusName = statusInt >= 0 && statusInt < GodStatusNames.Length
                        ? GodStatusNames[statusInt] : $"({statusInt})";

                    string color = statusInt switch
                    {
                        8 => "bright_green",  // Allied
                        9 => "cyan",          // Saved
                        10 => "dark_red",     // Defeated
                        11 => "dark_gray",    // Consumed
                        7 => "red",           // Hostile
                        0 => "gray",          // Unknown
                        _ => "yellow"
                    };

                    terminal.SetColor("white");
                    terminal.Write($"  [{i}] {GodNames[i],-32}— ");
                    terminal.SetColor(color);
                    terminal.WriteLine(statusName);
                }

                terminal.WriteLine("");
                terminal.SetColor("white");
                terminal.WriteLine("  Select god to edit (0-6), or Q to go back");
                terminal.WriteLine("");

                var choice = await ReadInput("Choice: ");
                if (string.IsNullOrWhiteSpace(choice) || choice.ToUpper() == "Q")
                    return changed;

                if (!int.TryParse(choice, out int godIdx) || godIdx < 0 || godIdx >= GodNames.Length)
                {
                    terminal.SetColor("red");
                    terminal.WriteLine("  Invalid selection.");
                    await Task.Delay(800);
                    continue;
                }

                // Show status options
                ss.OldGodStates.TryGetValue(godIdx, out int currentStatus);
                string currentName = currentStatus >= 0 && currentStatus < GodStatusNames.Length
                    ? GodStatusNames[currentStatus] : $"({currentStatus})";

                terminal.WriteLine("");
                terminal.SetColor("white");
                terminal.WriteLine($"  Current: {currentName}");
                terminal.WriteLine("");
                terminal.WriteLine("  [0]  Unknown      [1]  Imprisoned   [2]  Dormant");
                terminal.WriteLine("  [3]  Dying        [4]  Corrupted    [5]  Neutral");
                terminal.WriteLine("  [6]  Awakened     [7]  Hostile      [8]  Allied");
                terminal.WriteLine("  [9]  Saved        [10] Defeated     [11] Consumed");
                terminal.WriteLine("");

                var statusInput = await ReadInput("  New status: ");
                if (string.IsNullOrWhiteSpace(statusInput))
                    continue;

                if (int.TryParse(statusInput, out int newStatus) && newStatus >= 0 && newStatus < GodStatusNames.Length)
                {
                    ss.OldGodStates[godIdx] = newStatus;
                    terminal.SetColor("green");
                    terminal.WriteLine($"  {GodNames[godIdx].Split('(')[0].Trim()}: {currentName} → {GodStatusNames[newStatus]}");
                    changed = true;
                    await Task.Delay(1500);
                }
                else
                {
                    terminal.SetColor("red");
                    terminal.WriteLine("  Invalid status.");
                    await Task.Delay(800);
                }
            }
        }

        /// <summary>
        /// Apply admin edits to a live session's story systems (companions, Old Gods, grief).
        /// Mirrors the conversion logic in SaveSystem.RestoreFromSaveData.
        /// </summary>
        private void ApplyStoryEditsToLiveSession(string username, SaveGameData saveData)
        {
            var server = MudServer.Instance;
            if (server == null) return;

            var key = username.ToLowerInvariant();
            if (!server.ActiveSessions.TryGetValue(key, out var session)) return;

            var ctx = session.Context;
            if (ctx == null) return;

            var ss = saveData.StorySystems;

            // --- Sync companions ---
            try
            {
                var companionSystemData = new CompanionSystemData
                {
                    CompanionStates = ss.Companions?.Select(c => new CompanionSaveData
                    {
                        Id = (CompanionId)c.Id,
                        IsRecruited = c.IsRecruited,
                        IsActive = c.IsActive,
                        IsDead = c.IsDead,
                        LoyaltyLevel = c.LoyaltyLevel,
                        TrustLevel = c.TrustLevel,
                        RomanceLevel = c.RomanceLevel,
                        PersonalQuestStarted = c.PersonalQuestStarted,
                        PersonalQuestCompleted = c.PersonalQuestCompleted,
                        RecruitedDay = c.RecruitedDay,
                        Level = c.Level,
                        Experience = c.Experience,
                        BaseStatsHP = c.BaseStatsHP,
                        BaseStatsAttack = c.BaseStatsAttack,
                        BaseStatsDefense = c.BaseStatsDefense,
                        BaseStatsMagicPower = c.BaseStatsMagicPower,
                        BaseStatsSpeed = c.BaseStatsSpeed,
                        BaseStatsHealingPower = c.BaseStatsHealingPower,
                        EquippedItemsSave = c.EquippedItemsSave ?? new Dictionary<int, int>(),
                        DisabledAbilities = c.DisabledAbilities ?? new List<string>()
                    }).ToList() ?? new List<CompanionSaveData>(),

                    ActiveCompanions = ss.ActiveCompanionIds?.Select(id => (CompanionId)id).ToList()
                        ?? new List<CompanionId>(),

                    FallenCompanions = ss.FallenCompanions?.Select(d => new CompanionDeath
                    {
                        CompanionId = (CompanionId)d.CompanionId,
                        Type = (DeathType)d.DeathType,
                        Circumstance = d.Circumstance,
                        LastWords = d.LastWords,
                        DeathDay = d.DeathDay
                    }).ToList() ?? new List<CompanionDeath>()
                };

                ctx.Companions.Deserialize(companionSystemData);
            }
            catch (Exception ex)
            {
                DebugLogger.Instance.LogError("ADMIN", $"Failed to sync companions: {ex.Message}");
            }

            // --- Sync grief ---
            try
            {
                var griefData = new GriefSystemData
                {
                    ActiveGrief = ss.ActiveGriefs?.Select(g => new GriefState
                    {
                        CompanionId = (CompanionId)g.CompanionId,
                        NpcId = g.NpcId,
                        CompanionName = g.CompanionName,
                        DeathType = (DeathType)g.DeathType,
                        CurrentStage = (GriefStage)g.CurrentStage,
                        StageStartDay = g.StageStartDay,
                        GriefStartDay = g.GriefStartDay,
                        ResurrectionAttempts = g.ResurrectionAttempts,
                        IsComplete = g.IsComplete
                    }).ToList() ?? new List<GriefState>(),

                    ActiveNpcGrief = ss.ActiveNpcGriefs?.Select(g => new GriefState
                    {
                        CompanionId = (CompanionId)g.CompanionId,
                        NpcId = g.NpcId,
                        CompanionName = g.CompanionName,
                        DeathType = (DeathType)g.DeathType,
                        CurrentStage = (GriefStage)g.CurrentStage,
                        StageStartDay = g.StageStartDay,
                        GriefStartDay = g.GriefStartDay,
                        ResurrectionAttempts = g.ResurrectionAttempts,
                        IsComplete = g.IsComplete
                    }).ToList() ?? new List<GriefState>(),

                    Memories = ss.GriefMemories?.Select(m => new CompanionMemory
                    {
                        CompanionId = (CompanionId)m.CompanionId,
                        NpcId = m.NpcId,
                        CompanionName = m.CompanionName,
                        MemoryText = m.MemoryText,
                        CreatedDay = m.CreatedDay
                    }).ToList() ?? new List<CompanionMemory>()
                };

                ctx.Grief.Deserialize(griefData);
            }
            catch (Exception ex)
            {
                DebugLogger.Instance.LogError("ADMIN", $"Failed to sync grief: {ex.Message}");
            }

            // --- Sync Old God states ---
            try
            {
                if (ss.OldGodStates != null)
                {
                    foreach (var kvp in ss.OldGodStates)
                    {
                        var godType = (OldGodType)kvp.Key;
                        var newStatus = (GodStatus)kvp.Value;
                        if (ctx.Story.OldGodStates.TryGetValue(godType, out var state))
                        {
                            state.Status = newStatus;
                            if (newStatus != GodStatus.Unknown)
                                state.HasBeenEncountered = true;
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                DebugLogger.Instance.LogError("ADMIN", $"Failed to sync Old God states: {ex.Message}");
            }

            terminal.SetColor("cyan");
            terminal.WriteLine("  (Live story systems updated)");
        }

        internal async Task ImmortalizePlayer()
        {
            var allPlayers = await backend.GetAllPlayersDetailed();

            // Filter: show only non-alt players who aren't already immortal
            var eligible = new List<AdminPlayerInfo>();
            foreach (var p in allPlayers.OrderBy(p => p.DisplayName))
            {
                if (SqlSaveBackend.IsAltCharacter(p.Username))
                    continue;

                // Check if already immortal by loading save
                var saveData = await backend.ReadGameData(p.Username);
                if (saveData?.Player?.IsImmortal == true)
                    continue;

                eligible.Add(p);
            }

            var target = await ShowPlayerList("IMMORTALIZE PLAYER", eligible);
            if (target == null) return;

            var targetSave = await backend.ReadGameData(target.Username);
            if (targetSave?.Player == null)
            {
                terminal.SetColor("red");
                terminal.WriteLine($"  No save data found for '{target.Username}'.");
                await ReadInput("Press Enter to continue...");
                return;
            }

            var player = targetSave.Player;

            terminal.SetColor("white");
            terminal.WriteLine($"  Player: {player.Name2} — Level {player.Level} {GetClassName((int)player.Class)}");
            terminal.WriteLine("");

            // Get divine name
            string divineName = "";
            while (true)
            {
                divineName = await ReadInput("  Divine Name (3-30 chars): ");
                if (divineName.Length >= 3 && divineName.Length <= 30)
                    break;
                terminal.SetColor("red");
                terminal.WriteLine("  Name must be 3-30 characters.");
                terminal.SetColor("white");
            }

            // Get alignment
            terminal.WriteLine("");
            terminal.SetColor("white");
            terminal.WriteLine("  [1] Light    [2] Dark    [3] Balance");
            var alignChoice = await ReadInput("  Alignment: ");
            string alignment = alignChoice switch
            {
                "1" => "Light",
                "2" => "Dark",
                _ => "Balance"
            };

            // Confirm
            terminal.WriteLine("");
            terminal.SetColor("bright_yellow");
            terminal.WriteLine($"  Ascend {player.Name2} as '{divineName}', {alignment} god?");
            var confirm = await ReadInput("  Type 'YES' to confirm: ");
            if (confirm != "YES")
            {
                terminal.SetColor("yellow");
                terminal.WriteLine("  Cancelled.");
                await Task.Delay(1500);
                return;
            }

            // Apply immortality
            player.IsImmortal = true;
            player.DivineName = divineName;
            player.GodLevel = 1;
            player.GodExperience = 0;
            player.DeedsLeft = GameConfig.GodDeedsPerDay[0];
            player.GodAlignment = alignment;
            player.AscensionDate = DateTime.UtcNow;
            player.HasEarnedAltSlot = true;

            targetSave.Player = player;
            var success = await backend.WriteGameData(target.Username, targetSave);
            if (success)
            {
                terminal.SetColor("bright_green");
                terminal.WriteLine($"  {player.Name2} has ascended as {divineName}, Lesser Spirit of {alignment}!");
                terminal.WriteLine("  Alt slot has been granted.");
                DebugLogger.Instance.LogInfo("ADMIN", $"Player '{player.Name2}' immortalized as '{divineName}' by {DoorMode.OnlineUsername}");

                // Apply to live session if online
                var server = MudServer.Instance;
                if (server != null && server.ActiveSessions.TryGetValue(target.Username, out var session))
                {
                    var livePlayer = session.Context?.Engine?.CurrentPlayer;
                    if (livePlayer != null)
                    {
                        livePlayer.IsImmortal = true;
                        livePlayer.DivineName = divineName;
                        livePlayer.GodLevel = 1;
                        livePlayer.GodExperience = 0;
                        livePlayer.DeedsLeft = GameConfig.GodDeedsPerDay[0];
                        livePlayer.GodAlignment = alignment;
                        livePlayer.AscensionDate = DateTime.UtcNow;
                        livePlayer.HasEarnedAltSlot = true;
                        terminal.SetColor("cyan");
                        terminal.WriteLine("  (Live session updated)");
                    }
                }
            }
            else
            {
                terminal.SetColor("red");
                terminal.WriteLine("  Failed to save changes!");
            }
            await ReadInput("Press Enter to continue...");
        }

        private async Task<long> PromptNumericEdit(string fieldName, long currentValue, long min, long max)
        {
            terminal.SetColor("white");
            var input = await ReadInput($"New {fieldName} (current: {currentValue:N0}): ");
            if (string.IsNullOrWhiteSpace(input))
                return currentValue;

            if (long.TryParse(input, out long newValue))
            {
                newValue = Math.Clamp(newValue, min, max);
                terminal.SetColor("green");
                terminal.WriteLine($"{fieldName} changed: {currentValue:N0} -> {newValue:N0}");
                await Task.Delay(500);
                return newValue;
            }

            terminal.SetColor("red");
            terminal.WriteLine("Invalid number. No change made.");
            await Task.Delay(500);
            return currentValue;
        }

        // =====================================================================
        // Game Settings
        // =====================================================================

        internal async Task EditDifficultySettings()
        {
            while (true)
            {
                terminal.ClearScreen();
                terminal.SetColor("bright_cyan");
                terminal.WriteLine("═══ DIFFICULTY SETTINGS ═══");
                terminal.WriteLine("");

                terminal.SetColor("white");
                terminal.WriteLine($"  [1] XP Multiplier:             {GameConfig.XPMultiplier:F1}x");
                terminal.WriteLine($"  [2] Gold Multiplier:           {GameConfig.GoldMultiplier:F1}x");
                terminal.WriteLine($"  [3] Monster HP Multiplier:     {GameConfig.MonsterHPMultiplier:F1}x");
                terminal.WriteLine($"  [4] Monster Damage Multiplier: {GameConfig.MonsterDamageMultiplier:F1}x");
                terminal.WriteLine("");
                terminal.SetColor("gray");
                terminal.WriteLine("  Valid range: 0.1 to 10.0 (1.0 = default)");
                terminal.WriteLine("");
                terminal.SetColor("white");
                terminal.WriteLine("  [Q] Back");
                terminal.WriteLine("");

                var choice = await ReadInput("Choice: ");

                string settingName;
                float currentValue;

                switch (choice.ToUpper())
                {
                    case "1":
                        settingName = "XP Multiplier";
                        currentValue = GameConfig.XPMultiplier;
                        break;
                    case "2":
                        settingName = "Gold Multiplier";
                        currentValue = GameConfig.GoldMultiplier;
                        break;
                    case "3":
                        settingName = "Monster HP Multiplier";
                        currentValue = GameConfig.MonsterHPMultiplier;
                        break;
                    case "4":
                        settingName = "Monster Damage Multiplier";
                        currentValue = GameConfig.MonsterDamageMultiplier;
                        break;
                    case "Q":
                    case "":
                        return;
                    default:
                        continue;
                }

                terminal.SetColor("white");
                var input = await ReadInput($"New {settingName} (current: {currentValue:F1}, range 0.1-10.0): ");
                if (string.IsNullOrWhiteSpace(input))
                    continue;

                if (float.TryParse(input, out float newValue))
                {
                    newValue = Math.Clamp(newValue, 0.1f, 10.0f);

                    switch (choice)
                    {
                        case "1": GameConfig.XPMultiplier = newValue; break;
                        case "2": GameConfig.GoldMultiplier = newValue; break;
                        case "3": GameConfig.MonsterHPMultiplier = newValue; break;
                        case "4": GameConfig.MonsterDamageMultiplier = newValue; break;
                    }

                    SysOpConfigSystem.Instance.SaveConfig();
                    terminal.SetColor("green");
                    terminal.WriteLine($"{settingName} changed: {currentValue:F1}x -> {newValue:F1}x");
                    DebugLogger.Instance.LogInfo("ADMIN", $"{settingName} changed to {newValue:F1}x by {DoorMode.OnlineUsername}");
                    await Task.Delay(1000);
                }
                else
                {
                    terminal.SetColor("red");
                    terminal.WriteLine("Invalid number.");
                    await Task.Delay(1000);
                }
            }
        }

        internal async Task SetMOTD()
        {
            terminal.ClearScreen();
            terminal.SetColor("bright_cyan");
            terminal.WriteLine("═══ SET MESSAGE OF THE DAY ═══");
            terminal.WriteLine("");

            if (!string.IsNullOrEmpty(GameConfig.MessageOfTheDay))
            {
                terminal.SetColor("yellow");
                terminal.WriteLine($"Current MOTD: {GameConfig.MessageOfTheDay}");
            }
            else
            {
                terminal.SetColor("gray");
                terminal.WriteLine("No MOTD currently set.");
            }
            terminal.WriteLine("");

            terminal.SetColor("white");
            terminal.WriteLine("Enter new MOTD (blank to clear):");
            var motd = await ReadInput("> ");

            GameConfig.MessageOfTheDay = motd;
            SysOpConfigSystem.Instance.SaveConfig();

            if (string.IsNullOrEmpty(motd))
            {
                terminal.SetColor("yellow");
                terminal.WriteLine("MOTD cleared.");
            }
            else
            {
                terminal.SetColor("green");
                terminal.WriteLine($"MOTD set to: {motd}");
            }
            DebugLogger.Instance.LogInfo("ADMIN", $"MOTD changed by {DoorMode.OnlineUsername}: '{motd}'");
            await ReadInput("Press Enter to continue...");
        }

        // =====================================================================
        // World Management
        // =====================================================================

        internal async Task ViewOnlinePlayers()
        {
            terminal.ClearScreen();
            terminal.SetColor("bright_cyan");
            terminal.WriteLine("═══ ONLINE PLAYERS ═══");
            terminal.WriteLine("");

            var online = await backend.GetOnlinePlayers();
            if (online.Count == 0)
            {
                terminal.SetColor("gray");
                terminal.WriteLine("No players currently online.");
                await ReadInput("Press Enter to continue...");
                return;
            }

            terminal.SetColor("yellow");
            terminal.WriteLine($"  {"Name",-18} {"Location",-20} {"Connection",-10} {"Connected At",-20}");
            terminal.SetColor("gray");
            terminal.WriteLine("  " + new string('─', 70));

            foreach (var p in online)
            {
                terminal.SetColor("bright_green");
                terminal.Write($"  {p.DisplayName,-18}");
                terminal.SetColor("white");
                terminal.Write($" {p.Location,-20}");
                terminal.SetColor("cyan");
                terminal.Write($" {p.ConnectionType,-10}");
                terminal.SetColor("gray");
                terminal.WriteLine($" {p.ConnectedAt:yyyy-MM-dd HH:mm}");
            }

            terminal.WriteLine("");
            terminal.SetColor("white");
            terminal.WriteLine($"  {online.Count} player(s) online");
            terminal.WriteLine("");
            await ReadInput("Press Enter to continue...");
        }

        internal async Task ClearNews()
        {
            terminal.ClearScreen();
            terminal.SetColor("bright_cyan");
            terminal.WriteLine("═══ CLEAR NEWS FEED ═══");
            terminal.WriteLine("");

            terminal.SetColor("yellow");
            terminal.WriteLine("This will delete ALL news entries.");
            terminal.WriteLine("");

            terminal.SetColor("white");
            var confirm = await ReadInput("Are you sure? (Y/N): ");
            if (confirm.ToUpper() != "Y")
            {
                terminal.SetColor("yellow");
                terminal.WriteLine("Cancelled.");
                await ReadInput("Press Enter to continue...");
                return;
            }

            await backend.ClearAllNews();
            terminal.SetColor("green");
            terminal.WriteLine("News feed cleared.");
            DebugLogger.Instance.LogInfo("ADMIN", $"News feed cleared by {DoorMode.OnlineUsername}");
            await ReadInput("Press Enter to continue...");
        }

        internal async Task BroadcastMessage()
        {
            terminal.ClearScreen();
            terminal.SetColor("bright_cyan");
            terminal.WriteLine("═══ BROADCAST SYSTEM MESSAGE ═══");
            terminal.WriteLine("");

            // Show current broadcast if active
            var current = UsurperRemake.Server.MudServer.ActiveBroadcast;
            if (!string.IsNullOrEmpty(current))
            {
                terminal.SetColor("bright_yellow");
                terminal.WriteLine($"  Current: *** {current} ***");
                terminal.WriteLine("");
            }

            terminal.SetColor("white");
            terminal.WriteLine("Enter message (or blank to clear current broadcast):");
            var message = await ReadInput("> ");

            if (string.IsNullOrWhiteSpace(message))
            {
                if (string.IsNullOrEmpty(current))
                    return; // Nothing to clear, just go back

                UsurperRemake.Server.MudServer.ActiveBroadcast = null;
                var server = UsurperRemake.Server.MudServer.Instance;
                server?.BroadcastToAll($"\u001b[1;31m  *** SYSTEM MESSAGE: Broadcast cleared ***\u001b[0m");
                terminal.SetColor("green");
                terminal.WriteLine("Broadcast cleared.");
                DebugLogger.Instance.LogInfo("ADMIN", $"Broadcast cleared by {DoorMode.OnlineUsername}");
                await ReadInput("Press Enter to continue...");
                return;
            }

            terminal.WriteLine("");
            terminal.SetColor("yellow");
            terminal.WriteLine($"Message: *** {message} ***");
            var confirm = await ReadInput("Set as persistent broadcast? (Y/N): ");
            if (confirm.ToUpper() != "Y")
            {
                terminal.SetColor("yellow");
                terminal.WriteLine("Broadcast cancelled.");
                await ReadInput("Press Enter to continue...");
                return;
            }

            UsurperRemake.Server.MudServer.ActiveBroadcast = message;
            var srv = UsurperRemake.Server.MudServer.Instance;
            srv?.BroadcastToAll($"\u001b[1;31m  *** SYSTEM MESSAGE: {message} ***\u001b[0m");
            terminal.SetColor("green");
            terminal.WriteLine("Persistent broadcast set for all players.");
            DebugLogger.Instance.LogInfo("ADMIN", $"Broadcast set by {DoorMode.OnlineUsername}: '{message}'");
            await ReadInput("Press Enter to continue...");
        }

        // =====================================================================
        // Game Reset
        // =====================================================================

        internal async Task FullGameReset()
        {
            terminal.ClearScreen();
            terminal.SetColor("bright_red");
            terminal.WriteLine("╔══════════════════════════════════════════════════════════════════════════════╗");
            terminal.WriteLine("║                     !!! DANGER: FULL GAME WIPE !!!                         ║");
            terminal.WriteLine("╚══════════════════════════════════════════════════════════════════════════════╝");
            terminal.WriteLine("");

            terminal.SetColor("red");
            terminal.WriteLine("This will PERMANENTLY DELETE:");
            terminal.SetColor("white");
            terminal.WriteLine("  - ALL player save data (accounts remain, characters reset)");
            terminal.WriteLine("  - ALL world state (king, events, quests)");
            terminal.WriteLine("  - ALL news entries");
            terminal.WriteLine("  - ALL messages");
            terminal.WriteLine("  - ALL online player tracking");
            terminal.WriteLine("");
            terminal.SetColor("yellow");
            terminal.WriteLine("Player accounts and passwords will be preserved.");
            terminal.SetColor("red");
            terminal.WriteLine("This action CANNOT be undone!");
            terminal.WriteLine("");

            // Check online players
            var online = await backend.GetOnlinePlayers();
            if (online.Count > 1) // > 1 because admin is online too
            {
                terminal.SetColor("bright_yellow");
                terminal.WriteLine($"WARNING: {online.Count} player(s) currently online will be disrupted!");
                terminal.WriteLine("");
            }

            terminal.SetColor("bright_red");
            var confirm1 = await ReadInput("Type 'WIPE EVERYTHING' to confirm: ");
            if (confirm1 != "WIPE EVERYTHING")
            {
                terminal.SetColor("yellow");
                terminal.WriteLine("Game wipe cancelled.");
                await ReadInput("Press Enter to continue...");
                return;
            }

            var confirm2 = await ReadInput("Final confirmation - Type 'YES' to proceed: ");
            if (confirm2 != "YES")
            {
                terminal.SetColor("yellow");
                terminal.WriteLine("Game wipe cancelled.");
                await ReadInput("Press Enter to continue...");
                return;
            }

            terminal.SetColor("yellow");
            terminal.WriteLine("");
            terminal.WriteLine("Performing full game wipe...");

            try
            {
                await backend.FullGameReset();
                terminal.SetColor("green");
                terminal.WriteLine("  Cleared: All player save data");
                terminal.WriteLine("  Cleared: World state");
                terminal.WriteLine("  Cleared: News");
                terminal.WriteLine("  Cleared: Messages");
                terminal.WriteLine("  Cleared: Online player tracking");
                terminal.WriteLine("");
                terminal.WriteLine("Full game wipe complete!");
                terminal.WriteLine("All players will need to create new characters.");
                DebugLogger.Instance.LogWarning("ADMIN", $"Full game wipe performed by {DoorMode.OnlineUsername}");
            }
            catch (Exception ex)
            {
                terminal.SetColor("red");
                terminal.WriteLine($"ERROR: Game wipe failed! {ex.Message}");
                DebugLogger.Instance.LogError("ADMIN", $"Game wipe failed: {ex.Message}");
            }

            await ReadInput("Press Enter to continue...");
        }
    }
}
