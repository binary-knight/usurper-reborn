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
            terminal.WriteLine(Loc.Get("sysop_location.error_bbs_only"));
            terminal.SetColor("gray");
            await terminal.GetInputAsync(Loc.Get("ui.press_enter"));
            throw new LocationExitException(GameLocation.MainStreet);
        }

        if (!DoorMode.IsSysOp)
        {
            terminal.SetColor("red");
            terminal.WriteLine(Loc.Get("sysop_location.access_denied"));
            terminal.SetColor("gray");
            await terminal.GetInputAsync(Loc.Get("ui.press_enter"));
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
        WriteBoxHeader(Loc.Get("sysop.console_header"), "bright_red");
        terminal.WriteLine("");

        // Show session info
        terminal.SetColor("yellow");
        if (DoorMode.SessionInfo != null)
        {
            terminal.WriteLine(Loc.Get("sysop_location.logged_in_as", DoorMode.SessionInfo.UserName, DoorMode.SessionInfo.SecurityLevel));
            terminal.WriteLine(Loc.Get("sysop_location.bbs_name", DoorMode.SessionInfo.BBSName));
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
            if (IsScreenReader)
            {
                WriteSectionHeader(Loc.Get("sysop.update_available"), "bright_yellow");
                terminal.SetColor("white");
                terminal.WriteLine(Loc.Get("sysop_location.version_available", _latestVersion, GameConfig.Version));
                terminal.WriteLine(Loc.Get("sysop_location.press_9_update"));
            }
            else
            {
                if (GameConfig.ScreenReaderMode)
                {
                    terminal.SetColor("bright_green");
                    terminal.WriteLine(Loc.Get("sysop_location.update_available_banner", _latestVersion, GameConfig.Version));
                    terminal.SetColor("white");
                    terminal.WriteLine(Loc.Get("sysop_location.press_9_bracket"));
                }
                else
                {
                    terminal.SetColor("bright_yellow");
                    terminal.WriteLine("╔══════════════════════════════════════════════════════════════════════════════╗");
                    terminal.Write("║  ");
                    terminal.SetColor("white");
                    terminal.Write($"{Loc.Get("sysop_location.update_label")} ");
                    terminal.SetColor("bright_green");
                    terminal.Write($"v{_latestVersion}");
                    terminal.SetColor("white");
                    terminal.Write($" ({Loc.Get("sysop_location.current_label")}: {GameConfig.Version})");
                    terminal.SetColor("bright_yellow");
                    // Pad to fit the box
                    int labelLen = Loc.Get("sysop_location.update_label").Length + 1 + _latestVersion.Length + 1 + Loc.Get("sysop_location.current_label").Length + 2 + GameConfig.Version.Length + 1;
                    terminal.WriteLine(new string(' ', Math.Max(0, 74 - labelLen)) + "║");
                    terminal.WriteLine($"║  {Loc.Get("sysop_location.press_9_bracket"),-74}║");
                    terminal.WriteLine("╚══════════════════════════════════════════════════════════════════════════════╝");
                }
            }
            terminal.WriteLine("");
        }

        terminal.SetColor("bright_cyan");
        terminal.WriteLine(GameConfig.ScreenReaderMode ? Loc.Get("sysop_location.section_game_mgmt") : $"═══ {Loc.Get("sysop_location.section_game_mgmt")} ═══");
        terminal.SetColor("white");
        terminal.WriteLine($"  [1] {Loc.Get("sysop_location.menu_view_players")}");
        terminal.WriteLine($"  [2] {Loc.Get("sysop_location.menu_delete_player")}");
        terminal.WriteLine($"  [3] {Loc.Get("sysop_location.menu_reset_game")}");
        terminal.WriteLine("");

        terminal.SetColor("bright_cyan");
        terminal.WriteLine(GameConfig.ScreenReaderMode ? Loc.Get("sysop_location.section_settings") : $"═══ {Loc.Get("sysop_location.section_settings")} ═══");
        terminal.SetColor("white");
        terminal.WriteLine($"  [4] {Loc.Get("sysop_location.menu_config")}");
        terminal.WriteLine($"  [5] {Loc.Get("sysop_location.menu_motd")}");
        terminal.WriteLine("");

        terminal.SetColor("bright_cyan");
        terminal.WriteLine(GameConfig.ScreenReaderMode ? Loc.Get("sysop_location.section_monitoring") : $"═══ {Loc.Get("sysop_location.section_monitoring")} ═══");
        terminal.SetColor("white");
        terminal.WriteLine($"  [6] {Loc.Get("sysop_location.menu_stats")}");
        terminal.WriteLine($"  [7] {Loc.Get("sysop_location.menu_debug_log")}");
        terminal.WriteLine($"  [8] {Loc.Get("sysop_location.menu_npcs")}");
        terminal.WriteLine("");

        terminal.SetColor("bright_cyan");
        terminal.WriteLine(GameConfig.ScreenReaderMode ? Loc.Get("sysop_location.section_maintenance") : $"═══ {Loc.Get("sysop_location.section_maintenance")} ═══");
        if (_updateCheckComplete && _updateAvailable)
        {
            terminal.SetColor("bright_green");
            terminal.WriteLine($"  [9] {Loc.Get("sysop_location.menu_updates")}  ★ {Loc.Get("sysop_location.update_available_tag")} ★");
        }
        else
        {
            terminal.SetColor("white");
            terminal.WriteLine($"  [9] {Loc.Get("sysop_location.menu_updates")}");
        }
        terminal.WriteLine("");

        terminal.SetColor("gray");
        terminal.WriteLine($"  [Q] {Loc.Get("sysop_location.menu_return")}");
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
        terminal.WriteLine(GameConfig.ScreenReaderMode ? Loc.Get("sysop_location.all_players") : $"═══ {Loc.Get("sysop_location.all_players")} ═══");
        terminal.WriteLine("");

        try
        {
            var saveDir = SaveSystem.Instance.GetSaveDirectory();
            if (!Directory.Exists(saveDir))
            {
                terminal.SetColor("gray");
                terminal.WriteLine(Loc.Get("sysop_location.no_save_dir"));
                await terminal.GetInputAsync(Loc.Get("ui.press_enter"));
                return;
            }

            var saveFiles = Directory.GetFiles(saveDir, "*.json")
                .Where(f => !Path.GetFileName(f).Contains("state")).ToArray();

            if (saveFiles.Length == 0)
            {
                terminal.SetColor("gray");
                terminal.WriteLine(Loc.Get("sysop_location.no_saves"));
                await terminal.GetInputAsync(Loc.Get("ui.press_enter"));
                return;
            }

            terminal.SetColor("white");
            terminal.WriteLine(Loc.Get("sysop_location.found_saves", saveFiles.Length));
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
                terminal.WriteLine($" - {Loc.Get("sysop_location.last_played")}: {lastPlayed}, {Loc.Get("sysop_location.size")}: {fileSize}KB");

                index++;
            }

            terminal.WriteLine("");
        }
        catch (Exception ex)
        {
            terminal.SetColor("red");
            terminal.WriteLine(Loc.Get("sysop_location.error_reading_saves", ex.Message));
        }

        terminal.SetColor("gray");
        await terminal.GetInputAsync(Loc.Get("ui.press_enter"));
    }

    private async Task DeletePlayer()
    {
        terminal.ClearScreen();
        terminal.SetColor("bright_red");
        terminal.WriteLine(GameConfig.ScreenReaderMode ? Loc.Get("sysop_location.delete_player") : $"═══ {Loc.Get("sysop_location.delete_player")} ═══");
        terminal.WriteLine("");

        terminal.SetColor("yellow");
        terminal.WriteLine(Loc.Get("sysop_location.delete_warning"));
        terminal.WriteLine("");

        terminal.SetColor("white");
        terminal.Write(Loc.Get("sysop_location.enter_name_delete"));
        var playerName = await terminal.GetInputAsync("");

        if (string.IsNullOrWhiteSpace(playerName))
            return;

        // Check if trying to delete the current player
        var currentPlayer = GameEngine.Instance?.CurrentPlayer;
        if (currentPlayer != null && currentPlayer.Name.Equals(playerName, StringComparison.OrdinalIgnoreCase))
        {
            terminal.SetColor("red");
            terminal.WriteLine(Loc.Get("sysop_location.error_delete_active"));
            terminal.WriteLine(Loc.Get("sysop_location.must_logout_first"));
            await terminal.GetInputAsync(Loc.Get("ui.press_enter"));
            return;
        }

        var saveDir = SaveSystem.Instance.GetSaveDirectory();
        var savePath = Path.Combine(saveDir, $"{playerName}.json");

        if (!File.Exists(savePath))
        {
            terminal.SetColor("red");
            terminal.WriteLine(Loc.Get("sysop_location.player_not_found", playerName));
            await terminal.GetInputAsync(Loc.Get("ui.press_enter"));
            return;
        }

        terminal.SetColor("bright_red");
        terminal.Write(Loc.Get("sysop_location.confirm_delete", playerName));
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
                terminal.WriteLine(Loc.Get("sysop_location.deleted_file", $"{playerName}.json"));

                // Check for and delete any associated files (backup, state, etc.)
                var associatedPatterns = new[] { $"{playerName}_*.json", $"{playerName}.*.json" };
                foreach (var pattern in associatedPatterns)
                {
                    var associatedFiles = Directory.GetFiles(saveDir, pattern);
                    foreach (var file in associatedFiles)
                    {
                        File.Delete(file);
                        filesDeleted++;
                        terminal.WriteLine(Loc.Get("sysop_location.deleted_file", Path.GetFileName(file)));
                    }
                }

                terminal.SetColor("green");
                terminal.WriteLine("");
                terminal.WriteLine(Loc.Get("sysop_location.player_deleted", playerName, filesDeleted));
                DebugLogger.Instance.LogWarning("SYSOP", $"SysOp deleted player: {playerName} ({filesDeleted} files)");
            }
            catch (Exception ex)
            {
                terminal.SetColor("red");
                terminal.WriteLine(Loc.Get("sysop_location.error_deleting", ex.Message));
            }
        }
        else
        {
            terminal.SetColor("gray");
            terminal.WriteLine(Loc.Get("sysop_location.deletion_cancelled"));
        }

        await terminal.GetInputAsync(Loc.Get("ui.press_enter"));
    }

    #endregion

    #region Game Reset

    private async Task ResetGame()
    {
        terminal.ClearScreen();
        WriteBoxHeader(Loc.Get("sysop.danger_reset"), "bright_red");
        terminal.WriteLine("");

        terminal.SetColor("yellow");
        terminal.WriteLine(Loc.Get("sysop_location.reset_will_delete"));
        terminal.WriteLine(Loc.Get("sysop_location.reset_all_saves"));
        terminal.WriteLine(Loc.Get("sysop_location.reset_all_state"));
        terminal.WriteLine(Loc.Get("sysop_location.reset_fresh"));
        terminal.WriteLine("");

        terminal.SetColor("white");
        terminal.WriteLine(Loc.Get("sysop_location.cannot_be_undone"));
        terminal.WriteLine("");

        terminal.SetColor("bright_red");
        terminal.Write(Loc.Get("sysop_location.type_reset_game"));
        var confirm = await terminal.GetInputAsync("");

        if (confirm == "RESET GAME")
        {
            terminal.SetColor("yellow");
            terminal.Write(Loc.Get("sysop_location.final_confirm"));
            var finalConfirm = await terminal.GetInputAsync("");

            if (finalConfirm.ToUpper() == "YES")
            {
                await PerformGameReset();
            }
            else
            {
                terminal.SetColor("gray");
                terminal.WriteLine(Loc.Get("sysop_location.reset_cancelled"));
            }
        }
        else
        {
            terminal.SetColor("gray");
            terminal.WriteLine(Loc.Get("sysop_location.reset_cancelled"));
        }

        await terminal.GetInputAsync(Loc.Get("ui.press_enter"));
    }

    private async Task PerformGameReset()
    {
        terminal.SetColor("yellow");
        terminal.WriteLine("");
        terminal.WriteLine(Loc.Get("sysop_location.resetting_game"));

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
                        terminal.WriteLine(Loc.Get("sysop_location.preserved_file", fileName));
                        continue;
                    }
                    File.Delete(file);
                    terminal.SetColor("gray");
                    terminal.WriteLine(Loc.Get("sysop_location.deleted_file", fileName));
                }
            }

            terminal.SetColor("yellow");
            terminal.WriteLine("");
            terminal.WriteLine(Loc.Get("sysop_location.resetting_systems"));

            // Reset all singleton systems (matches CreateNewGame in GameEngine)
            // Romance and Family systems
            RomanceTracker.Instance.Reset();
            terminal.SetColor("gray");
            terminal.WriteLine(Loc.Get("sysop_location.reset_system", "Romance"));

            FamilySystem.Instance.Reset();
            terminal.WriteLine(Loc.Get("sysop_location.reset_system", "Family"));

            // NPC systems
            NPCSpawnSystem.Instance.ResetNPCs();
            terminal.WriteLine(Loc.Get("sysop_location.reset_system", "NPC spawn"));

            WorldSimulator.Instance?.ClearRespawnQueue();
            terminal.WriteLine(Loc.Get("sysop_location.reset_system", "World simulator queues"));

            NPCMarriageRegistry.Instance.Reset();
            terminal.WriteLine(Loc.Get("sysop_location.reset_system", "NPC marriage registry"));

            // Companion system
            CompanionSystem.Instance.ResetAllCompanions();
            terminal.WriteLine(Loc.Get("sysop_location.reset_system", "Companion"));

            // Story progression systems
            StoryProgressionSystem.Instance.FullReset();
            terminal.WriteLine(Loc.Get("sysop_location.reset_system", "Story progression"));

            OceanPhilosophySystem.Instance.Reset();
            terminal.WriteLine(Loc.Get("sysop_location.reset_system", "Ocean philosophy"));

            TownNPCStorySystem.Instance.Reset();
            terminal.WriteLine(Loc.Get("sysop_location.reset_system", "Town NPC stories"));

            // World and faction systems
            WorldInitializerSystem.Instance.ResetWorld();
            terminal.WriteLine(Loc.Get("sysop_location.reset_system", "World state"));

            FactionSystem.Instance.Reset();
            terminal.WriteLine(Loc.Get("sysop_location.reset_system", "Faction"));

            ArchetypeTracker.Instance.Reset();
            terminal.WriteLine(Loc.Get("sysop_location.reset_system", "Archetype tracker"));

            // Narrative systems
            StrangerEncounterSystem.Instance.Reset();
            terminal.WriteLine(Loc.Get("sysop_location.reset_system", "Stranger encounters"));

            DreamSystem.Instance.Reset();
            terminal.WriteLine(Loc.Get("sysop_location.reset_system", "Dream"));

            GriefSystem.Instance.Reset();
            terminal.WriteLine(Loc.Get("sysop_location.reset_system", "Grief"));

            // Clear dungeon party
            GameEngine.Instance?.ClearDungeonParty();
            terminal.WriteLine(Loc.Get("sysop_location.reset_system", "Dungeon party"));

            DebugLogger.Instance.LogWarning("SYSOP", "Full game reset performed by SysOp - all systems cleared");

            terminal.SetColor("bright_green");
            terminal.WriteLine("");
            terminal.WriteLine(Loc.Get("sysop_location.reset_complete"));
            terminal.WriteLine(Loc.Get("sysop_location.reset_new_chars"));
            terminal.WriteLine(Loc.Get("sysop_location.reset_config_preserved"));
        }
        catch (Exception ex)
        {
            terminal.SetColor("red");
            terminal.WriteLine(Loc.Get("sysop_location.error_reset", ex.Message));
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
            terminal.WriteLine(GameConfig.ScreenReaderMode ? Loc.Get("sysop_location.difficulty_settings") : $"═══ {Loc.Get("sysop_location.difficulty_settings")} ═══");
            terminal.WriteLine("");

            terminal.SetColor("white");
            terminal.WriteLine(Loc.Get("sysop_location.current_settings"));
            terminal.WriteLine($"  {Loc.Get("sysop_location.xp_mult")}: {GameConfig.XPMultiplier:F1}x");
            terminal.WriteLine($"  {Loc.Get("sysop_location.gold_mult")}: {GameConfig.GoldMultiplier:F1}x");
            terminal.WriteLine($"  {Loc.Get("sysop_location.monster_hp_mult")}: {GameConfig.MonsterHPMultiplier:F1}x");
            terminal.WriteLine($"  {Loc.Get("sysop_location.monster_dmg_mult")}: {GameConfig.MonsterDamageMultiplier:F1}x");
            terminal.WriteLine("");

            terminal.SetColor("gray");
            terminal.WriteLine(Loc.Get("sysop_location.values_higher"));
            terminal.WriteLine(Loc.Get("sysop_location.values_lower"));
            terminal.WriteLine("");

            terminal.SetColor("white");
            terminal.WriteLine(Loc.Get("sysop_location.feature_toggles"));
            terminal.Write($"  {Loc.Get("sysop_location.online_multiplayer")}: ");
            if (GameConfig.DisableOnlinePlay)
            {
                terminal.SetColor("red");
                terminal.WriteLine(Loc.Get("sysop_location.disabled"));
            }
            else
            {
                terminal.SetColor("bright_green");
                terminal.WriteLine(Loc.Get("sysop_location.enabled"));
            }
            terminal.WriteLine("");

            terminal.SetColor("cyan");
            terminal.WriteLine(Loc.Get("sysop_location.edit_options"));
            terminal.SetColor("white");
            terminal.WriteLine($"  [1] {Loc.Get("sysop_location.set_xp_mult")}");
            terminal.WriteLine($"  [2] {Loc.Get("sysop_location.set_gold_mult")}");
            terminal.WriteLine($"  [3] {Loc.Get("sysop_location.set_monster_hp_mult")}");
            terminal.WriteLine($"  [4] {Loc.Get("sysop_location.set_monster_dmg_mult")}");
            terminal.WriteLine($"  [5] {Loc.Get("sysop_location.toggle_online")}");
            terminal.WriteLine($"  [Q] {Loc.Get("sysop_location.return_sysop")}");
            terminal.WriteLine("");

            terminal.SetColor("gray");
            terminal.Write(Loc.Get("ui.choice"));
            var choice = await terminal.GetInputAsync("");

            switch (choice.ToUpper())
            {
                case "1":
                    terminal.Write(Loc.Get("sysop_location.new_xp_mult"));
                    var xpInput = await terminal.GetInputAsync("");
                    if (float.TryParse(xpInput, out float xp) && xp >= 0.1f && xp <= 10.0f)
                    {
                        GameConfig.XPMultiplier = xp;
                        SysOpConfigSystem.Instance.SaveConfig();
                        terminal.SetColor("green");
                        terminal.WriteLine(Loc.Get("sysop_location.mult_set", Loc.Get("sysop_location.xp_mult"), $"{xp:F1}"));
                        DebugLogger.Instance.LogInfo("SYSOP", $"XP multiplier changed to {xp:F1}");
                    }
                    else
                    {
                        terminal.SetColor("red");
                        terminal.WriteLine(Loc.Get("sysop_location.invalid_mult", xpInput));
                    }
                    await terminal.GetInputAsync(Loc.Get("ui.press_enter"));
                    break;

                case "2":
                    terminal.Write(Loc.Get("sysop_location.new_gold_mult"));
                    var goldInput = await terminal.GetInputAsync("");
                    if (float.TryParse(goldInput, out float gold) && gold >= 0.1f && gold <= 10.0f)
                    {
                        GameConfig.GoldMultiplier = gold;
                        SysOpConfigSystem.Instance.SaveConfig();
                        terminal.SetColor("green");
                        terminal.WriteLine(Loc.Get("sysop_location.mult_set", Loc.Get("sysop_location.gold_mult"), $"{gold:F1}"));
                        DebugLogger.Instance.LogInfo("SYSOP", $"Gold multiplier changed to {gold:F1}");
                    }
                    else
                    {
                        terminal.SetColor("red");
                        terminal.WriteLine(Loc.Get("sysop_location.invalid_mult", goldInput));
                    }
                    await terminal.GetInputAsync(Loc.Get("ui.press_enter"));
                    break;

                case "3":
                    terminal.Write(Loc.Get("sysop_location.new_monster_hp_mult"));
                    var hpInput = await terminal.GetInputAsync("");
                    if (float.TryParse(hpInput, out float hp) && hp >= 0.1f && hp <= 10.0f)
                    {
                        GameConfig.MonsterHPMultiplier = hp;
                        SysOpConfigSystem.Instance.SaveConfig();
                        terminal.SetColor("green");
                        terminal.WriteLine(Loc.Get("sysop_location.mult_set", Loc.Get("sysop_location.monster_hp_mult"), $"{hp:F1}"));
                        DebugLogger.Instance.LogInfo("SYSOP", $"Monster HP multiplier changed to {hp:F1}");
                    }
                    else
                    {
                        terminal.SetColor("red");
                        terminal.WriteLine(Loc.Get("sysop_location.invalid_mult", hpInput));
                    }
                    await terminal.GetInputAsync(Loc.Get("ui.press_enter"));
                    break;

                case "4":
                    terminal.Write(Loc.Get("sysop_location.new_monster_dmg_mult"));
                    var dmgInput = await terminal.GetInputAsync("");
                    if (float.TryParse(dmgInput, out float dmg) && dmg >= 0.1f && dmg <= 10.0f)
                    {
                        GameConfig.MonsterDamageMultiplier = dmg;
                        SysOpConfigSystem.Instance.SaveConfig();
                        terminal.SetColor("green");
                        terminal.WriteLine(Loc.Get("sysop_location.mult_set", Loc.Get("sysop_location.monster_dmg_mult"), $"{dmg:F1}"));
                        DebugLogger.Instance.LogInfo("SYSOP", $"Monster damage multiplier changed to {dmg:F1}");
                    }
                    else
                    {
                        terminal.SetColor("red");
                        terminal.WriteLine(Loc.Get("sysop_location.invalid_mult", dmgInput));
                    }
                    await terminal.GetInputAsync(Loc.Get("ui.press_enter"));
                    break;

                case "5":
                    GameConfig.DisableOnlinePlay = !GameConfig.DisableOnlinePlay;
                    SysOpConfigSystem.Instance.SaveConfig();
                    terminal.SetColor("green");
                    if (GameConfig.DisableOnlinePlay)
                        terminal.WriteLine(Loc.Get("sysop_location.online_disabled"));
                    else
                        terminal.WriteLine(Loc.Get("sysop_location.online_enabled"));
                    DebugLogger.Instance.LogInfo("SYSOP", $"Online multiplayer {(GameConfig.DisableOnlinePlay ? "disabled" : "enabled")}");
                    await terminal.GetInputAsync(Loc.Get("ui.press_enter"));
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
        terminal.WriteLine(GameConfig.ScreenReaderMode ? Loc.Get("sysop_location.motd_title") : $"═══ {Loc.Get("sysop_location.motd_title")} ═══");
        terminal.WriteLine("");

        var currentMOTD = GameConfig.MessageOfTheDay;

        terminal.SetColor("white");
        terminal.WriteLine(Loc.Get("sysop_location.current_motd"));
        terminal.SetColor("cyan");
        terminal.WriteLine(string.IsNullOrEmpty(currentMOTD) ? $"  ({Loc.Get("sysop_location.no_motd")})" : $"  {currentMOTD}");
        terminal.WriteLine("");

        terminal.SetColor("white");
        terminal.WriteLine(Loc.Get("sysop_location.enter_new_motd"));
        var newMOTD = await terminal.GetInputAsync("");

        GameConfig.MessageOfTheDay = newMOTD;
        SysOpConfigSystem.Instance.SaveConfig();

        terminal.SetColor("green");
        if (string.IsNullOrEmpty(newMOTD))
        {
            terminal.WriteLine(Loc.Get("sysop_location.motd_cleared"));
        }
        else
        {
            terminal.WriteLine(Loc.Get("sysop_location.motd_updated"));
        }

        DebugLogger.Instance.LogInfo("SYSOP", $"MOTD changed to: {newMOTD}");
        await terminal.GetInputAsync(Loc.Get("ui.press_enter"));
    }

    #endregion

    #region Monitoring

    private async Task ViewGameStatistics()
    {
        terminal.ClearScreen();
        terminal.SetColor("bright_yellow");
        terminal.WriteLine(GameConfig.ScreenReaderMode ? Loc.Get("sysop_location.game_statistics") : $"═══ {Loc.Get("sysop_location.game_statistics")} ═══");
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
            terminal.WriteLine($"{Loc.Get("sysop_location.total_players")}: {playerCount}");
            terminal.WriteLine("");

            // NPC Statistics
            terminal.SetColor("cyan");
            terminal.WriteLine(Loc.Get("sysop_location.npc_statistics"));
            terminal.SetColor("white");
            var activeNPCs = NPCSpawnSystem.Instance.ActiveNPCs;
            terminal.WriteLine($"  {Loc.Get("sysop_location.active_npcs")}: {activeNPCs.Count}");
            terminal.WriteLine($"  {Loc.Get("sysop_location.dead_npcs")}: {activeNPCs.Count(n => n.IsDead)}");
            terminal.WriteLine($"  {Loc.Get("sysop_location.married_npcs")}: {activeNPCs.Count(n => n.IsMarried)}");
            terminal.WriteLine("");

            // Story Statistics
            terminal.SetColor("cyan");
            terminal.WriteLine(Loc.Get("sysop_location.story_statistics"));
            terminal.SetColor("white");
            var story = StoryProgressionSystem.Instance;
            terminal.WriteLine($"  {Loc.Get("sysop_location.collected_seals")}: {story.CollectedSeals.Count}/7");
            terminal.WriteLine($"  {Loc.Get("sysop_location.current_chapter")}: {story.CurrentChapter}");

            var ocean = OceanPhilosophySystem.Instance;
            terminal.WriteLine($"  {Loc.Get("sysop_location.awakening_level")}: {ocean.AwakeningLevel}/7");
            terminal.WriteLine($"  {Loc.Get("sysop_location.wave_fragments")}: {ocean.CollectedFragments.Count}/10");
        }
        catch (Exception ex)
        {
            terminal.SetColor("red");
            terminal.WriteLine(Loc.Get("sysop_location.error_stats", ex.Message));
        }

        terminal.WriteLine("");
        terminal.SetColor("gray");
        await terminal.GetInputAsync(Loc.Get("ui.press_enter"));
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
                terminal.WriteLine(Loc.Get("sysop_location.no_debug_log"));
                await terminal.GetInputAsync(Loc.Get("ui.press_enter"));
                return;
            }

            var allLines = File.ReadAllLines(logPath);
            if (allLines.Length == 0)
            {
                terminal.ClearScreen();
                terminal.SetColor("gray");
                terminal.WriteLine(Loc.Get("sysop_location.debug_log_empty"));
                await terminal.GetInputAsync(Loc.Get("ui.press_enter"));
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
                terminal.WriteLine(GameConfig.ScreenReaderMode ? Loc.Get("sysop_location.debug_log_page", page + 1, totalPages, lines.Count) : $"═══ {Loc.Get("sysop_location.debug_log_page", page + 1, totalPages, lines.Count)} ═══");
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
                terminal.WriteLine(Loc.Get("sysop_location.log_nav"));
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
            terminal.WriteLine(Loc.Get("sysop_location.error_log", ex.Message));
            await terminal.GetInputAsync(Loc.Get("ui.press_enter"));
        }
    }

    private async Task ViewActiveNPCs()
    {
        var npcs = NPCSpawnSystem.Instance.ActiveNPCs.OrderBy(n => n.Name).ToList();

        if (npcs.Count == 0)
        {
            terminal.ClearScreen();
            terminal.SetColor("gray");
            terminal.WriteLine(Loc.Get("sysop_location.no_active_npcs"));
            await terminal.GetInputAsync(Loc.Get("ui.press_enter"));
            return;
        }

        int page = 0;
        int pageSize = 15;
        int totalPages = (npcs.Count + pageSize - 1) / pageSize;

        while (true)
        {
            terminal.ClearScreen();
            terminal.SetColor("bright_yellow");
            terminal.WriteLine(GameConfig.ScreenReaderMode ? Loc.Get("sysop_location.active_npcs_page", page + 1, totalPages) : $"═══ {Loc.Get("sysop_location.active_npcs_page", page + 1, totalPages)} ═══");
            terminal.WriteLine("");

            var pageNPCs = npcs.Skip(page * pageSize).Take(pageSize);

            foreach (var npc in pageNPCs)
            {
                string status = npc.IsDead ? $"[{Loc.Get("sysop_location.dead_tag")}]" : $"HP:{npc.HP}/{npc.MaxHP}";
                string married = npc.IsMarried ? $" [{Loc.Get("sysop_location.married_tag")}]" : "";

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
            terminal.WriteLine(Loc.Get("sysop_location.npc_nav"));
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
        terminal.WriteLine(GameConfig.ScreenReaderMode ? Loc.Get("sysop_location.check_updates") : $"═══ {Loc.Get("sysop_location.check_updates")} ═══");
        terminal.WriteLine("");

        terminal.SetColor("white");
        terminal.WriteLine(Loc.Get("sysop_location.current_version", GameConfig.Version));
        terminal.WriteLine("");

        // Check if this is a Steam build
        if (VersionChecker.Instance.IsSteamBuild)
        {
            terminal.SetColor("cyan");
            terminal.WriteLine(Loc.Get("sysop_location.steam_build"));
            terminal.WriteLine(Loc.Get("sysop_location.check_steam"));
            terminal.WriteLine("");
            await terminal.GetInputAsync(Loc.Get("ui.press_enter"));
            return;
        }

        terminal.SetColor("gray");
        terminal.WriteLine(Loc.Get("sysop_location.checking_github"));
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
                terminal.WriteLine(Loc.Get("sysop_location.check_failed"));
                terminal.WriteLine("");
                terminal.SetColor("yellow");
                if (!string.IsNullOrEmpty(checker.CheckFailedReason))
                    terminal.WriteLine($"  {Loc.Get("sysop_location.error_label")}: {checker.CheckFailedReason}");
                terminal.SetColor("gray");
                terminal.WriteLine(Loc.Get("sysop_location.tls_hint1"));
                terminal.WriteLine(Loc.Get("sysop_location.tls_hint2"));
                terminal.WriteLine("");
                terminal.SetColor("white");
                terminal.WriteLine(Loc.Get("sysop_location.manual_download"));
                terminal.SetColor("cyan");
                terminal.WriteLine("  https://github.com/binary-knight/usurper-reborn/releases/latest");
                terminal.WriteLine("");
                await terminal.GetInputAsync(Loc.Get("ui.press_enter"));
                return;
            }

            if (!checker.NewVersionAvailable)
            {
                terminal.SetColor("bright_green");
                terminal.WriteLine(Loc.Get("sysop_location.latest_version"));
                terminal.WriteLine("");
                terminal.SetColor("gray");
                terminal.WriteLine($"{Loc.Get("sysop_location.current_label")}: {checker.CurrentVersion}");
                terminal.WriteLine($"{Loc.Get("sysop_location.latest_label")}:  {checker.LatestVersion}");
                terminal.WriteLine("");
                await terminal.GetInputAsync(Loc.Get("ui.press_enter"));
                return;
            }

            // New version is available
            WriteBoxHeader(Loc.Get("sysop.new_version"), "bright_yellow");
            terminal.WriteLine("");

            terminal.SetColor("white");
            terminal.WriteLine($"  {Loc.Get("sysop_location.current_version_label")}: {checker.CurrentVersion}");
            terminal.SetColor("bright_green");
            terminal.WriteLine($"  {Loc.Get("sysop_location.latest_version_label")}:  {checker.LatestVersion}");
            terminal.WriteLine("");

            // Show release notes if available
            if (!string.IsNullOrEmpty(checker.ReleaseNotes))
            {
                terminal.SetColor("cyan");
                terminal.WriteLine(Loc.Get("sysop_location.release_notes"));
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
                terminal.WriteLine(Loc.Get("sysop_location.auto_update_available"));
                terminal.WriteLine("");

                terminal.SetColor("white");
                terminal.WriteLine(Loc.Get("sysop_location.options"));
                terminal.WriteLine($"  [1] {Loc.Get("sysop_location.download_auto")}");
                terminal.WriteLine($"  [2] {Loc.Get("sysop_location.open_download")}");
                terminal.WriteLine($"  [3] {Loc.Get("sysop_location.skip_for_now")}");
                terminal.WriteLine("");

                terminal.SetColor("gray");
                terminal.Write(Loc.Get("ui.choice"));
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
                        terminal.WriteLine(Loc.Get("sysop_location.opening_browser"));
                        await terminal.GetInputAsync(Loc.Get("ui.press_enter"));
                        break;
                    default:
                        terminal.SetColor("gray");
                        terminal.WriteLine(Loc.Get("sysop_location.update_skipped"));
                        await terminal.GetInputAsync(Loc.Get("ui.press_enter"));
                        break;
                }
            }
            else
            {
                // No auto-update - offer manual download
                terminal.SetColor("white");
                terminal.WriteLine($"{Loc.Get("sysop_location.download_label")}: {checker.ReleaseUrl}");
                terminal.WriteLine("");

                terminal.SetColor("cyan");
                terminal.Write(Loc.Get("sysop_location.open_browser_yn"));
                var response = await terminal.GetInputAsync("");

                if (GameConfig.IsAffirmative(response))
                {
                    checker.OpenDownloadPage();
                    terminal.SetColor("green");
                    terminal.WriteLine("");
                    terminal.WriteLine(Loc.Get("sysop_location.opening_browser"));
                }
                await terminal.GetInputAsync(Loc.Get("ui.press_enter"));
            }
        }
        catch (Exception ex)
        {
            terminal.SetColor("red");
            terminal.WriteLine(Loc.Get("sysop_location.error_updates", ex.Message));
            DebugLogger.Instance.LogError("SYSOP", $"Update check failed: {ex.Message}");
            await terminal.GetInputAsync(Loc.Get("ui.press_enter"));
        }
    }

    private async Task PerformAutoUpdate(VersionChecker checker)
    {
        terminal.WriteLine("");
        terminal.SetColor("yellow");
        terminal.WriteLine(Loc.Get("sysop_location.downloading_update"));
        terminal.WriteLine("");

        terminal.SetColor("white");
        var lastProgress = 0;

        var success = await checker.DownloadAndInstallUpdateAsync(progress =>
        {
            if (progress >= lastProgress + 10 || progress == 100)
            {
                // Create a simple progress bar
                if (GameConfig.ScreenReaderMode)
                {
                    terminal.Write($"\r  {Loc.Get("sysop_location.downloading_pct")}: {progress}%   ");
                }
                else
                {
                    int filled = progress / 5; // 20 chars total
                    int empty = 20 - filled;
                    string bar = new string('█', filled) + new string('░', empty);
                    terminal.Write($"\r  [{bar}] {progress}%   ");
                }
                lastProgress = progress;
            }
        });

        terminal.WriteLine("");
        terminal.WriteLine("");

        if (success)
        {
            WriteBoxHeader(Loc.Get("sysop.update_success"), "bright_green");
            terminal.WriteLine("");
            terminal.SetColor("white");
            terminal.WriteLine(Loc.Get("sysop_location.update_close"));
            terminal.WriteLine(Loc.Get("sysop_location.update_disconnect"));
            terminal.WriteLine("");

            DebugLogger.Instance.LogWarning("SYSOP", $"SysOp initiated auto-update to version {checker.LatestVersion}");

            terminal.SetColor("yellow");
            terminal.Write(Loc.Get("sysop_location.press_enter_update"));
            await terminal.GetInputAsync("");

            // Exit the game to let the updater run
            Environment.Exit(0);
        }
        else
        {
            terminal.SetColor("red");
            terminal.WriteLine(Loc.Get("sysop_location.download_failed", checker.DownloadError));
            terminal.WriteLine("");

            terminal.SetColor("gray");
            terminal.Write(Loc.Get("sysop_location.open_download_yn"));
            var response = await terminal.GetInputAsync("");

            if (GameConfig.IsAffirmative(response))
            {
                checker.OpenDownloadPage();
                terminal.SetColor("green");
                terminal.WriteLine("");
                terminal.WriteLine(Loc.Get("sysop_location.opening_browser"));
            }

            await terminal.GetInputAsync(Loc.Get("ui.press_enter"));
        }
    }

    #endregion
}
