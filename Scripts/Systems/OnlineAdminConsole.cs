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
    /// Only accessible to admin users (hardcoded to "Rage") from the character selection screen.
    /// Operates on SqlSaveBackend for all data operations.
    /// </summary>
    public class OnlineAdminConsole
    {
        private readonly TerminalEmulator terminal;
        private readonly SqlSaveBackend backend;

        private static readonly string[] ClassNames = {
            "Alchemist", "Assassin", "Barbarian", "Bard", "Cleric",
            "Jester", "Magician", "Paladin", "Ranger", "Sage", "Warrior"
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
                // (online mode with PTY should support it, but catch just in case)
            }
        }

        /// <summary>
        /// Read sanitized input from the terminal.
        /// Drains any buffered escape sequences from stdin before reading,
        /// preventing terminal response garbage from ANSI codes and special keys.
        /// </summary>
        private async Task<string> ReadInput(string prompt)
        {
            // Let any pending terminal response bytes arrive in stdin buffer
            await Task.Delay(50);

            // Drain buffered bytes (ANSI responses from ClearScreen/SetColor, stale escape sequences)
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
                        await ListAllPlayers();
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
                        await EditPlayer();
                        break;
                    case "6":
                        await EditDifficultySettings();
                        break;
                    case "7":
                        await SetMOTD();
                        break;
                    case "8":
                        await ViewOnlinePlayers();
                        break;
                    case "9":
                        await ClearNews();
                        break;
                    case "A":
                        await BroadcastMessage();
                        break;
                    case "P":
                        await ResetPlayerPassword();
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
            terminal.WriteLine("  [1] List All Players");
            terminal.WriteLine("  [2] Ban Player");
            terminal.WriteLine("  [3] Unban Player");
            terminal.WriteLine("  [4] Delete Player");
            terminal.WriteLine("  [5] Edit Player");
            terminal.WriteLine("  [P] Reset Player Password");
            terminal.WriteLine("");

            terminal.SetColor("bright_cyan");
            terminal.WriteLine("═══ GAME SETTINGS ═══");
            terminal.SetColor("white");
            terminal.WriteLine("  [6] Difficulty Settings (XP/Gold/Monster multipliers)");
            terminal.WriteLine("  [7] Set Message of the Day (MOTD)");
            terminal.WriteLine("");

            terminal.SetColor("bright_cyan");
            terminal.WriteLine("═══ WORLD MANAGEMENT ═══");
            terminal.SetColor("white");
            terminal.WriteLine("  [8] View Online Players");
            terminal.WriteLine("  [9] Clear News Feed");
            terminal.WriteLine("  [A] Broadcast System Message");
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
            return "Offline";
        }

        private string GetStatusColor(AdminPlayerInfo p)
        {
            if (p.IsBanned) return "red";
            if (p.IsOnline) return "bright_green";
            return "gray";
        }

        // =====================================================================
        // Player Management
        // =====================================================================

        internal async Task ListAllPlayers()
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
                terminal.WriteLine($"  {"#",-4} {"Name",-16} {"Lvl",4} {"Class",-12} {"Gold",10} {"Status",-8} {"Last Login",-12}");
                terminal.SetColor("gray");
                terminal.WriteLine("  " + new string('─', 70));

                var pageItems = players.Skip(page * pageSize).Take(pageSize).ToList();
                for (int i = 0; i < pageItems.Count; i++)
                {
                    var p = pageItems[i];
                    int num = page * pageSize + i + 1;
                    string status = GetPlayerStatus(p);
                    string lastLogin = p.LastLogin != null ? p.LastLogin.Substring(0, Math.Min(10, p.LastLogin.Length)) : "Never";

                    terminal.SetColor(GetStatusColor(p));
                    terminal.WriteLine($"  {num,-4} {p.DisplayName,-16} {p.Level,4} {GetClassName(p.ClassId),-12} {p.Gold,10:N0} {status,-8} {lastLogin,-12}");
                }

                terminal.WriteLine("");
                terminal.SetColor("white");
                string nav = "";
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
                }
            }
        }

        internal async Task BanPlayer()
        {
            terminal.ClearScreen();
            terminal.SetColor("bright_red");
            terminal.WriteLine("═══ BAN PLAYER ═══");
            terminal.WriteLine("");

            terminal.SetColor("white");
            var username = await ReadInput("Enter username to ban (or blank to cancel): ");
            if (string.IsNullOrWhiteSpace(username))
                return;

            // Self-ban protection
            if (string.Equals(username, DoorMode.OnlineUsername, StringComparison.OrdinalIgnoreCase))
            {
                terminal.SetColor("red");
                terminal.WriteLine("You cannot ban yourself!");
                await ReadInput("Press Enter to continue...");
                return;
            }

            // Check if player exists
            var players = await backend.GetAllPlayersDetailed();
            var target = players.FirstOrDefault(p => string.Equals(p.Username, username, StringComparison.OrdinalIgnoreCase));
            if (target == null)
            {
                terminal.SetColor("red");
                terminal.WriteLine($"Player '{username}' not found.");
                await ReadInput("Press Enter to continue...");
                return;
            }

            if (target.IsBanned)
            {
                terminal.SetColor("yellow");
                terminal.WriteLine($"Player '{target.DisplayName}' is already banned.");
                terminal.WriteLine($"Reason: {target.BanReason ?? "No reason given"}");
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
            terminal.ClearScreen();
            terminal.SetColor("bright_red");
            terminal.WriteLine("═══ DELETE PLAYER ═══");
            terminal.SetColor("red");
            terminal.WriteLine("WARNING: This permanently deletes a player's account and all their data!");
            terminal.WriteLine("");

            terminal.SetColor("white");
            var username = await ReadInput("Enter username to delete (or blank to cancel): ");
            if (string.IsNullOrWhiteSpace(username))
                return;

            // Self-delete protection
            if (string.Equals(username, DoorMode.OnlineUsername, StringComparison.OrdinalIgnoreCase))
            {
                terminal.SetColor("red");
                terminal.WriteLine("You cannot delete your own account!");
                await ReadInput("Press Enter to continue...");
                return;
            }

            // Check if player exists
            var players = await backend.GetAllPlayersDetailed();
            var target = players.FirstOrDefault(p => string.Equals(p.Username, username, StringComparison.OrdinalIgnoreCase));
            if (target == null)
            {
                terminal.SetColor("red");
                terminal.WriteLine($"Player '{username}' not found.");
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
            terminal.ClearScreen();
            terminal.SetColor("bright_cyan");
            terminal.WriteLine("═══ RESET PLAYER PASSWORD ═══");
            terminal.WriteLine("");

            terminal.SetColor("white");
            var username = await ReadInput("Enter username (or blank to cancel): ");
            if (string.IsNullOrWhiteSpace(username))
                return;

            terminal.SetColor("yellow");
            terminal.WriteLine($"Setting new password for '{username}'.");
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

            var (success, message) = backend.AdminResetPassword(username, newPassword);
            terminal.SetColor(success ? "green" : "red");
            terminal.WriteLine(message);

            if (success)
                DebugLogger.Instance.LogWarning("ADMIN", $"Password reset for '{username}' by {DoorMode.OnlineUsername}");

            await ReadInput("Press Enter to continue...");
        }

        internal async Task EditPlayer()
        {
            terminal.ClearScreen();
            terminal.SetColor("bright_cyan");
            terminal.WriteLine("═══ EDIT PLAYER ═══");
            terminal.WriteLine("");

            terminal.SetColor("white");
            var username = await ReadInput("Enter username to edit (or blank to cancel): ");
            if (string.IsNullOrWhiteSpace(username))
                return;

            await EditPlayer(username);
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
                        player.HP = player.MaxHP;
                        modified = true;
                        break;
                    case "6":
                        player.MaxMana = await PromptNumericEdit("Max Mana", player.MaxMana, 0, long.MaxValue);
                        player.Mana = player.MaxMana;
                        modified = true;
                        break;
                    case "7":
                        player.Strength = await PromptNumericEdit("Strength", player.Strength, 1, 9999);
                        modified = true;
                        break;
                    case "8":
                        player.Defence = await PromptNumericEdit("Defence", player.Defence, 1, 9999);
                        modified = true;
                        break;
                    case "9":
                        player.Stamina = await PromptNumericEdit("Stamina", player.Stamina, 1, 9999);
                        modified = true;
                        break;
                    case "A":
                        player.Agility = await PromptNumericEdit("Agility", player.Agility, 1, 9999);
                        modified = true;
                        break;
                    case "B":
                        player.Charisma = await PromptNumericEdit("Charisma", player.Charisma, 1, 9999);
                        modified = true;
                        break;
                    case "C":
                        player.Dexterity = await PromptNumericEdit("Dexterity", player.Dexterity, 1, 9999);
                        modified = true;
                        break;
                    case "D":
                        player.Wisdom = await PromptNumericEdit("Wisdom", player.Wisdom, 1, 9999);
                        modified = true;
                        break;
                    case "E":
                        player.Intelligence = await PromptNumericEdit("Intelligence", player.Intelligence, 1, 9999);
                        modified = true;
                        break;
                    case "F":
                        player.Constitution = await PromptNumericEdit("Constitution", player.Constitution, 1, 9999);
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
                            // Without this, their next autosave would overwrite our DB changes
                            ApplyEditsToLiveSession(username, player);
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
