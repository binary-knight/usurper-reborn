using UsurperRemake.Utils;
using UsurperRemake.Systems;
using UsurperRemake.BBS;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;

/// <summary>
/// SysOp Administration Console - BBS door mode administration interface
/// Only accessible in BBS door mode by users with SysOp security level (100+)
///
/// Allows SysOps to manage the game on their BBS including:
/// - Game reset (wipe all saves)
/// - Player management (view/delete players)
/// - View statistics and logs
/// </summary>
public class SysOpLocation : BaseLocation
{
    private Task? _updateCheckTask;
    private bool _updateCheckComplete = false;
    private bool _updateAvailable = false;
    private string _latestVersion = "";

    public SysOpLocation() : base(GameLocation.SysOpConsole, "SysOp Console", "BBS Administration Console")
    {
    }

    protected override void SetupLocation()
    {
        PossibleExits.Add(GameLocation.MainStreet);
    }

    public override async Task EnterLocation(Character player, TerminalEmulator term)
    {
        currentPlayer = player;
        terminal = term;

        // Verify SysOp access - should only be reachable in door mode
        if (!DoorMode.IsInDoorMode)
        {
            terminal.SetColor("red");
            terminal.WriteLine("ERROR: SysOp Console is only available in BBS door mode.");
            terminal.SetColor("gray");
            await terminal.GetInputAsync("Press Enter to return...");
            throw new LocationExitException(GameLocation.MainStreet);
        }

        if (!DoorMode.IsSysOp)
        {
            terminal.SetColor("red");
            terminal.WriteLine("ACCESS DENIED: SysOp privileges required.");
            terminal.SetColor("gray");
            await terminal.GetInputAsync("Press Enter to return...");
            throw new LocationExitException(GameLocation.MainStreet);
        }

        // Start background update check (non-blocking)
        StartBackgroundUpdateCheck();

        await LocationLoop();
    }

    private void StartBackgroundUpdateCheck()
    {
        // Skip if already checking, Steam build, or online server mode
        if (_updateCheckTask != null || VersionChecker.Instance.IsSteamBuild || DoorMode.IsOnlineMode)
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
                // Silently ignore errors - this is a background check
            }
            finally
            {
                _updateCheckComplete = true;
            }
        });
    }

    protected override void DisplayLocation()
    {
        terminal.ClearScreen();
        terminal.SetColor("bright_red");
        terminal.WriteLine("╔══════════════════════════════════════════════════════════════════════════════╗");
        terminal.WriteLine("║                        S Y S O P   C O N S O L E                             ║");
        terminal.WriteLine("╚══════════════════════════════════════════════════════════════════════════════╝");
        terminal.WriteLine("");

        // Show session info
        terminal.SetColor("yellow");
        if (DoorMode.SessionInfo != null)
        {
            terminal.WriteLine($"  Logged in as: {DoorMode.SessionInfo.UserName} (Security Level: {DoorMode.SessionInfo.SecurityLevel})");
            terminal.WriteLine($"  BBS: {DoorMode.SessionInfo.BBSName}");
        }
        terminal.WriteLine("");

        // Show menu
        ShowSysOpMenu();
    }

    private void ShowSysOpMenu()
    {
        // Show update notification if available
        if (_updateCheckComplete && _updateAvailable)
        {
            terminal.SetColor("bright_yellow");
            terminal.WriteLine("╔══════════════════════════════════════════════════════════════════════════════╗");
            terminal.Write("║  ");
            terminal.SetColor("white");
            terminal.Write("UPDATE AVAILABLE: ");
            terminal.SetColor("bright_green");
            terminal.Write($"v{_latestVersion}");
            terminal.SetColor("white");
            terminal.Write($" (current: {GameConfig.Version})");
            terminal.SetColor("bright_yellow");
            // Pad to fit the box
            int contentLen = 18 + _latestVersion.Length + 11 + GameConfig.Version.Length + 1;
            terminal.WriteLine(new string(' ', Math.Max(0, 74 - contentLen)) + "║");
            terminal.WriteLine("║  Press [9] to download and install the update                               ║");
            terminal.WriteLine("╚══════════════════════════════════════════════════════════════════════════════╝");
            terminal.WriteLine("");
        }

        terminal.SetColor("bright_cyan");
        terminal.WriteLine("═══ GAME MANAGEMENT ═══");
        terminal.SetColor("white");
        terminal.WriteLine("  [1] View All Players");
        terminal.WriteLine("  [2] Delete Player");
        terminal.WriteLine("  [3] Reset Game (Wipe All Data)");
        terminal.WriteLine("");

        terminal.SetColor("bright_cyan");
        terminal.WriteLine("═══ GAME SETTINGS ═══");
        terminal.SetColor("white");
        terminal.WriteLine("  [4] View/Edit Game Configuration");
        terminal.WriteLine("  [5] Set Message of the Day (MOTD)");
        terminal.WriteLine("");

        terminal.SetColor("bright_cyan");
        terminal.WriteLine("═══ MONITORING ═══");
        terminal.SetColor("white");
        terminal.WriteLine("  [6] View Game Statistics");
        terminal.WriteLine("  [7] View Debug Log");
        terminal.WriteLine("  [8] View Active NPCs");
        terminal.WriteLine("");

        terminal.SetColor("bright_cyan");
        terminal.WriteLine("═══ SYSTEM MAINTENANCE ═══");
        if (_updateCheckComplete && _updateAvailable)
        {
            terminal.SetColor("bright_green");
            terminal.WriteLine("  [9] Check for Updates  ★ UPDATE AVAILABLE ★");
        }
        else
        {
            terminal.SetColor("white");
            terminal.WriteLine("  [9] Check for Updates");
        }
        terminal.WriteLine("");

        terminal.SetColor("gray");
        terminal.WriteLine("  [Q] Return to Main Street");
        terminal.WriteLine("");
    }

    protected override async Task<bool> ProcessChoice(string choice)
    {
        switch (choice.ToUpper())
        {
            case "1":
                await ViewAllPlayers();
                return false;

            case "2":
                await DeletePlayer();
                return false;

            case "3":
                await ResetGame();
                return false;

            case "4":
                await ViewEditConfig();
                return false;

            case "5":
                await SetMOTD();
                return false;

            case "6":
                await ViewGameStatistics();
                return false;

            case "7":
                await ViewDebugLog();
                return false;

            case "8":
                await ViewActiveNPCs();
                return false;

            case "9":
                await CheckForUpdates();
                return false;

            case "Q":
                throw new LocationExitException(GameLocation.MainStreet);

            default:
                return false;
        }
    }

    #region Player Management

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
                .Where(f => !Path.GetFileName(f).Contains("state")).ToArray();

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
                var fileSize = fileInfo.Length / 1024; // KB

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

        // Check if trying to delete the current player
        var currentPlayer = GameEngine.Instance?.CurrentPlayer;
        if (currentPlayer != null && currentPlayer.Name.Equals(playerName, StringComparison.OrdinalIgnoreCase))
        {
            terminal.SetColor("red");
            terminal.WriteLine("ERROR: Cannot delete the currently active player.");
            terminal.WriteLine("The player must log out first.");
            await terminal.GetInputAsync("Press Enter to continue...");
            return;
        }

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

                // Delete main save file
                File.Delete(savePath);
                filesDeleted++;
                terminal.SetColor("gray");
                terminal.WriteLine($"  Deleted: {playerName}.json");

                // Check for and delete any associated files (backup, state, etc.)
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

            // Delete all save files (except sysop_config.json which contains SysOp settings)
            if (Directory.Exists(saveDir))
            {
                var files = Directory.GetFiles(saveDir, "*.json");
                foreach (var file in files)
                {
                    var fileName = Path.GetFileName(file);
                    // Preserve SysOp configuration
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

            // Reset all singleton systems (matches CreateNewGame in GameEngine)
            // Romance and Family systems
            RomanceTracker.Instance.Reset();
            terminal.SetColor("gray");
            terminal.WriteLine("  Reset: Romance system");

            FamilySystem.Instance.Reset();
            terminal.WriteLine("  Reset: Family system");

            // NPC systems
            NPCSpawnSystem.Instance.ResetNPCs();
            terminal.WriteLine("  Reset: NPC spawn system");

            WorldSimulator.Instance?.ClearRespawnQueue();
            terminal.WriteLine("  Reset: World simulator queues");

            NPCMarriageRegistry.Instance.Reset();
            terminal.WriteLine("  Reset: NPC marriage registry");

            // Companion system
            CompanionSystem.Instance.ResetAllCompanions();
            terminal.WriteLine("  Reset: Companion system");

            // Story progression systems
            StoryProgressionSystem.Instance.FullReset();
            terminal.WriteLine("  Reset: Story progression");

            OceanPhilosophySystem.Instance.Reset();
            terminal.WriteLine("  Reset: Ocean philosophy");

            TownNPCStorySystem.Instance.Reset();
            terminal.WriteLine("  Reset: Town NPC stories");

            // World and faction systems
            WorldInitializerSystem.Instance.ResetWorld();
            terminal.WriteLine("  Reset: World state");

            FactionSystem.Instance.Reset();
            terminal.WriteLine("  Reset: Faction system");

            ArchetypeTracker.Instance.Reset();
            terminal.WriteLine("  Reset: Archetype tracker");

            // Narrative systems
            StrangerEncounterSystem.Instance.Reset();
            terminal.WriteLine("  Reset: Stranger encounters");

            DreamSystem.Instance.Reset();
            terminal.WriteLine("  Reset: Dream system");

            GriefSystem.Instance.Reset();
            terminal.WriteLine("  Reset: Grief system");

            // Clear dungeon party
            GameEngine.Instance?.ClearDungeonParty();
            terminal.WriteLine("  Reset: Dungeon party");

            DebugLogger.Instance.LogWarning("SYSOP", "Full game reset performed by SysOp - all systems cleared");

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

                default:
                    // Invalid menu choice - just redisplay the menu
                    break;
            }
        }
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

    private async Task ViewGameStatistics()
    {
        terminal.ClearScreen();
        terminal.SetColor("bright_yellow");
        terminal.WriteLine("═══ GAME STATISTICS ═══");
        terminal.WriteLine("");

        try
        {
            var saveDir = SaveSystem.Instance.GetSaveDirectory();
            int playerCount = 0;

            if (Directory.Exists(saveDir))
            {
                var saveFiles = Directory.GetFiles(saveDir, "*.json")
                    .Where(f => !Path.GetFileName(f).Contains("state")).ToList();
                playerCount = saveFiles.Count;
            }

            terminal.SetColor("white");
            terminal.WriteLine($"Total Players: {playerCount}");
            terminal.WriteLine("");

            // NPC Statistics
            terminal.SetColor("cyan");
            terminal.WriteLine("NPC Statistics:");
            terminal.SetColor("white");
            var activeNPCs = NPCSpawnSystem.Instance.ActiveNPCs;
            terminal.WriteLine($"  Active NPCs: {activeNPCs.Count}");
            terminal.WriteLine($"  Dead NPCs: {activeNPCs.Count(n => n.IsDead)}");
            terminal.WriteLine($"  Married NPCs: {activeNPCs.Count(n => n.IsMarried)}");
            terminal.WriteLine("");

            // Story Statistics
            terminal.SetColor("cyan");
            terminal.WriteLine("Story Statistics:");
            terminal.SetColor("white");
            var story = StoryProgressionSystem.Instance;
            terminal.WriteLine($"  Collected Seals: {story.CollectedSeals.Count}/7");
            terminal.WriteLine($"  Current Chapter: {story.CurrentChapter}");

            var ocean = OceanPhilosophySystem.Instance;
            terminal.WriteLine($"  Awakening Level: {ocean.AwakeningLevel}/7");
            terminal.WriteLine($"  Wave Fragments: {ocean.CollectedFragments.Count}/10");
        }
        catch (Exception ex)
        {
            terminal.SetColor("red");
            terminal.WriteLine($"Error gathering statistics: {ex.Message}");
        }

        terminal.WriteLine("");
        terminal.SetColor("gray");
        await terminal.GetInputAsync("Press Enter to continue...");
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

            // Reverse so newest entries are first
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
                    // Color code based on log level
                    if (line.Contains("[ERROR]"))
                        terminal.SetColor("red");
                    else if (line.Contains("[WARNING]"))
                        terminal.SetColor("yellow");
                    else if (line.Contains("[DEBUG]"))
                        terminal.SetColor("gray");
                    else
                        terminal.SetColor("white");

                    // Truncate long lines
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

        // Check if this is a Steam build
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

        // Force a fresh check by directly calling the API
        try
        {
            // Reset the checker state to force a fresh check
            var checker = VersionChecker.Instance;

            // Perform the update check
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

            // New version is available
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

            // Show release notes if available
            if (!string.IsNullOrEmpty(checker.ReleaseNotes))
            {
                terminal.SetColor("cyan");
                terminal.WriteLine("Release Notes:");
                terminal.SetColor("gray");
                var notes = checker.ReleaseNotes.Length > 300
                    ? checker.ReleaseNotes.Substring(0, 300) + "..."
                    : checker.ReleaseNotes;
                // Clean up markdown
                notes = notes.Replace("#", "").Replace("*", "").Replace("\r", "");
                var lines = notes.Split('\n');
                foreach (var line in lines.Take(8))
                {
                    if (!string.IsNullOrWhiteSpace(line))
                        terminal.WriteLine($"  {line.Trim()}");
                }
                terminal.WriteLine("");
            }

            // Check if auto-update is available
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
                // No auto-update - offer manual download
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
                // Create a simple progress bar
                int filled = progress / 5; // 20 chars total
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

            // Exit the game to let the updater run
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
