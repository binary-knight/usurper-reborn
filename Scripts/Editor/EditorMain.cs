using System;
using System.IO;
using System.Threading.Tasks;

namespace UsurperRemake.Editor;

/// <summary>
/// Standalone editor mode entry point. Launched via <c>UsurperReborn --editor</c>.
/// Presents a menu-driven interface that edits the same files the game reads
/// from: <c>GameData/*.json</c> for moddable content and <c>saves/*.json</c>
/// for per-character saves. Analogous to the old DOS-era <c>USEDIT.EXE</c>
/// that shipped alongside the original Usurper game.
///
/// Intentionally lives entirely outside the MUD/BBS/terminal plumbing. Uses
/// plain <see cref="Console"/> I/O so it's safe to run on any OS without a
/// graphical terminal, and impossible to corrupt the running game's state
/// even if both are launched simultaneously (the editor reads and writes
/// files; it doesn't share memory with a running server).
/// </summary>
public static class EditorMain
{
    public static async Task<int> RunAsync(string[] args)
    {
        EditorIO.Header($"Usurper Reborn Editor  (v{GameConfig.Version})");
        EditorIO.Info("A separate editor tool for modifying game data and save files.");
        EditorIO.Info("Changes take effect next time the game starts.");
        EditorIO.Warn("Exit any running game instances before editing save files.");

        // Initialize game-data systems up front — the editor needs the same
        // built-in references the game uses (equipment lookups, balance
        // defaults, etc.) to present sane editing choices.
        try
        {
            UsurperRemake.Systems.GameDataLoader.Initialize();
            EquipmentDatabase.Initialize();
        }
        catch (Exception ex)
        {
            EditorIO.Error($"Initialization failed: {ex.Message}");
            return 1;
        }

        while (true)
        {
            int choice = EditorIO.Menu("Main Menu", new[]
            {
                "Player Saves            — edit characters (stats, inventory, quests, companions, etc.)",
                "Game Data / Modding     — custom equipment, NPCs, monsters, dreams, achievements, dialogue, balance",
                "Save File Management    — clone, delete, restore saves",
                "Export Defaults         — write built-in data to GameData/ as a starting mod template",
                "File Locations          — where the editor reads / writes files",
            });

            try
            {
                switch (choice)
                {
                    case 0:
                        EditorIO.Info("Exiting editor.");
                        return 0;
                    case 1:
                        await PlayerSaveEditor.RunAsync();
                        break;
                    case 2:
                        RunGameDataMenu();
                        break;
                    case 3:
                        SaveFileManager.Run();
                        break;
                    case 4:
                        ExportDefaults();
                        break;
                    case 5:
                        ShowGameDataFolder();
                        break;
                }
            }
            catch (Exception ex)
            {
                EditorIO.Error($"Editor action failed: {ex.Message}");
                EditorIO.Info(ex.StackTrace ?? "");
                EditorIO.Pause();
            }
        }
    }

    /// <summary>Nested menu for GameData/*.json modding — one entry per data source.</summary>
    private static void RunGameDataMenu()
    {
        while (true)
        {
            int choice = EditorIO.Menu("Game Data / Modding", new[]
            {
                "Equipment        — custom weapons, armor, shields, accessories (equipment.json)",
                "NPCs             — town NPCs (npcs.json)",
                "Monsters         — monster families and tiers (monster_families.json)",
                "Dreams           — narrative dream sequences (dreams.json)",
                "Achievements     — disabled (Steam integrity)",
                "Dialogue         — NPC dialogue lines (dialogue.json)",
                "Balance          — tuning constants (balance.json)",
            });
            switch (choice)
            {
                case 0: return;
                case 1: EquipmentEditor.Run(); break;
                case 2: NPCEditor.Run(); break;
                case 3: MonsterEditor.Run(); break;
                case 4: DreamEditor.Run(); break;
                case 5:
                    // v0.57.4: achievement-definition editing removed. Since the game
                    // loads achievements.json in place of the built-in list (see
                    // AchievementSystem.Initialize), users could otherwise edit a
                    // built-in like "first_steps" to have a 100000-gold reward and
                    // unlock Steam achievements normally via TryUnlock. Modders who
                    // want custom achievements can still write achievements.json by
                    // hand — the editor just doesn't facilitate it.
                    EditorIO.Warn("Achievement definition editing is disabled.");
                    EditorIO.Info("Editing built-in achievement rewards would allow Steam-achievement cheating.");
                    EditorIO.Info("Modders can still edit GameData/achievements.json directly if needed.");
                    EditorIO.Pause();
                    break;
                case 6: DialogueEditor.Run(); break;
                case 7: BalanceEditor.Run(); break;
            }
        }
    }

    private static void ExportDefaults()
    {
        EditorIO.Section("Export Defaults");
        var outputDir = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "GameData");
        EditorIO.Info($"Target directory: {outputDir}");
        if (!EditorIO.Confirm("Write all 7 default JSON files here? Existing files will be OVERWRITTEN"))
        {
            EditorIO.Info("Cancelled.");
            return;
        }
        try
        {
            UsurperRemake.Systems.GameDataLoader.ExportDefaults(outputDir);
            EditorIO.Success("Default game data exported.");
        }
        catch (Exception ex)
        {
            EditorIO.Error($"Export failed: {ex.Message}");
        }
        EditorIO.Pause();
    }

    private static void ShowGameDataFolder()
    {
        EditorIO.Section("File locations");
        var baseDir = AppDomain.CurrentDomain.BaseDirectory;
        EditorIO.Info($"Executable directory : {baseDir}");
        EditorIO.Info($"GameData folder      : {Path.Combine(baseDir, "GameData")}");
        EditorIO.Info($"Saves folder         : {new UsurperRemake.Systems.FileSaveBackend().GetSaveDirectory()}");
        EditorIO.Info($"Localization folder  : {Path.Combine(baseDir, "Localization")}");
        EditorIO.Pause();
    }
}
