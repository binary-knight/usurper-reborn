using System;
using System.IO;
using System.Linq;
using UsurperRemake.Systems;

namespace UsurperRemake.Editor;

/// <summary>
/// Utility menus for managing save files on disk: list, copy (clone a character),
/// delete, restore from the automatic <c>.bak</c> that the editor creates on
/// every Save operation. All operations run against the on-disk save directory,
/// never against a live session.
/// </summary>
internal static class SaveFileManager
{
    public static void Run()
    {
        var backend = new FileSaveBackend();
        while (true)
        {
            var saveDir = backend.GetSaveDirectory();
            EditorIO.Section($"Save File Management  —  {saveDir}");
            int choice = EditorIO.Menu("Choose:", new[]
            {
                "List all save files (with sizes)",
                "Copy a save to a new character name (clone)",
                "Delete a save (permanent)",
                "Restore a save from its .bak backup",
                "Open the save directory in explorer / finder",
            });
            switch (choice)
            {
                case 0: return;
                case 1: ListSaves(saveDir); break;
                case 2: CloneSave(saveDir, backend); break;
                case 3: DeleteSave(saveDir, backend); break;
                case 4: RestoreBackup(saveDir); break;
                case 5: OpenFolder(saveDir); break;
            }
        }
    }

    private static void ListSaves(string saveDir)
    {
        if (!Directory.Exists(saveDir))
        { EditorIO.Warn($"Save directory does not exist: {saveDir}"); EditorIO.Pause(); return; }
        var files = new DirectoryInfo(saveDir).GetFiles("*.json")
            .OrderByDescending(f => f.LastWriteTime)
            .ToList();
        if (files.Count == 0) { EditorIO.Info("(no saves)"); EditorIO.Pause(); return; }
        EditorIO.Info($"Found {files.Count} saves:");
        foreach (var f in files)
        {
            var bakExists = File.Exists(f.FullName + ".bak") ? " [.bak]" : "";
            EditorIO.Info($"  {f.Name,-28} {f.Length,10:N0} bytes  {f.LastWriteTime:yyyy-MM-dd HH:mm}{bakExists}");
        }
        EditorIO.Pause();
    }

    private static void CloneSave(string saveDir, FileSaveBackend backend)
    {
        EditorIO.Section("Clone save");
        var saves = backend.GetAllSaves();
        if (saves.Count == 0) { EditorIO.Warn("No saves."); EditorIO.Pause(); return; }
        var ordered = saves.OrderByDescending(s => s.SaveTime).ToList();
        var labels = ordered.Select(s => $"{s.PlayerName,-24} L{s.Level} {s.ClassName}").ToList();
        int pick = EditorIO.Menu("Source save:", labels);
        if (pick == 0) return;
        var src = ordered[pick - 1];
        // v0.57.4 (pass 4): trust the on-disk filename from SaveInfo.FileName
        // instead of re-deriving it through the editor's sanitizer. Editor
        // used char.IsLetterOrDigit while backend uses Path.GetInvalidFileNameChars,
        // so names with hyphens / apostrophes mismatched and File.Exists failed.
        // Same fix pattern used in PlayerSaveEditor earlier in v0.57.4.
        var srcFile = !string.IsNullOrEmpty(src.FileName)
            ? Path.Combine(saveDir, src.FileName)
            : Path.Combine(saveDir, SanitizeFileName(src.PlayerName) + ".json");
        if (!File.Exists(srcFile)) { EditorIO.Error($"Source file missing: {srcFile}"); EditorIO.Pause(); return; }

        string newName = EditorIO.Prompt("New character name for the clone");
        if (string.IsNullOrWhiteSpace(newName)) { EditorIO.Warn("Cancelled."); EditorIO.Pause(); return; }
        var dstFile = Path.Combine(saveDir, SanitizeFileName(newName) + ".json");
        if (File.Exists(dstFile))
        {
            if (!EditorIO.Confirm($"{Path.GetFileName(dstFile)} already exists — overwrite?")) return;
        }
        try
        {
            File.Copy(srcFile, dstFile, overwrite: true);
            EditorIO.Success($"Cloned to {dstFile}.");
            EditorIO.Warn("NOTE: the clone's internal name fields still match the source. Edit the character's Name1/Name2 via Player Saves to rename it in-game.");
            EditorIO.Pause();
        }
        catch (Exception ex)
        {
            EditorIO.Error($"Copy failed: {ex.Message}");
            EditorIO.Pause();
        }
    }

    private static void DeleteSave(string saveDir, FileSaveBackend backend)
    {
        EditorIO.Section("Delete save");
        var saves = backend.GetAllSaves();
        if (saves.Count == 0) { EditorIO.Warn("No saves."); EditorIO.Pause(); return; }
        var ordered = saves.OrderByDescending(s => s.SaveTime).ToList();
        var labels = ordered.Select(s => $"{s.PlayerName,-24} L{s.Level} {s.ClassName}  last {s.SaveTime:yyyy-MM-dd}").ToList();
        int pick = EditorIO.Menu("Save to delete:", labels);
        if (pick == 0) return;
        var tgt = ordered[pick - 1];
        // v0.57.4 (pass 4): trust SaveInfo.FileName over re-sanitizing the name.
        var file = !string.IsNullOrEmpty(tgt.FileName)
            ? Path.Combine(saveDir, tgt.FileName)
            : Path.Combine(saveDir, SanitizeFileName(tgt.PlayerName) + ".json");

        EditorIO.Warn($"This will permanently delete: {file}");
        if (!EditorIO.Confirm($"Delete {tgt.PlayerName} (L{tgt.Level} {tgt.ClassName})?")) return;
        if (!EditorIO.Confirm("Are you really sure? This cannot be undone from the editor.")) return;
        try
        {
            File.Delete(file);
            // Also clean up the .bak if one exists
            if (File.Exists(file + ".bak")) File.Delete(file + ".bak");
            EditorIO.Success($"Deleted {Path.GetFileName(file)}.");
        }
        catch (Exception ex) { EditorIO.Error($"Delete failed: {ex.Message}"); }
        EditorIO.Pause();
    }

    private static void RestoreBackup(string saveDir)
    {
        EditorIO.Section("Restore from .bak");
        var baks = Directory.Exists(saveDir)
            ? new DirectoryInfo(saveDir).GetFiles("*.json.bak").OrderByDescending(f => f.LastWriteTime).ToList()
            : new();
        if (baks.Count == 0) { EditorIO.Info("No .bak files found — the editor creates one on every Save."); EditorIO.Pause(); return; }
        var labels = baks.Select(f => $"{f.Name,-34}  {f.LastWriteTime:yyyy-MM-dd HH:mm}").ToList();
        int pick = EditorIO.Menu("Pick a backup to restore:", labels);
        if (pick == 0) return;
        var bak = baks[pick - 1];
        var target = bak.FullName.Substring(0, bak.FullName.Length - 4); // strip .bak
        if (File.Exists(target))
        {
            if (!EditorIO.Confirm($"Overwrite existing {Path.GetFileName(target)} with backup?")) return;
        }
        try
        {
            File.Copy(bak.FullName, target, overwrite: true);
            EditorIO.Success($"Restored {Path.GetFileName(target)} from backup.");
        }
        catch (Exception ex) { EditorIO.Error($"Restore failed: {ex.Message}"); }
        EditorIO.Pause();
    }

    private static void OpenFolder(string saveDir)
    {
        try
        {
            if (!Directory.Exists(saveDir)) Directory.CreateDirectory(saveDir);
            // Cross-platform: on Windows use explorer; on Linux/macOS print the path.
            if (OperatingSystem.IsWindows())
                System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo { FileName = saveDir, UseShellExecute = true });
            else
                EditorIO.Info($"Folder path: {saveDir}");
        }
        catch (Exception ex) { EditorIO.Error($"Open failed: {ex.Message}"); EditorIO.Info($"Path: {saveDir}"); }
        EditorIO.Pause();
    }

    // v0.57.4 (pass 4): mirror FileSaveBackend.GetSaveFileName's sanitizer
    // exactly — Path.GetInvalidFileNameChars, not char.IsLetterOrDigit — so
    // cloned destination filenames land where the game expects them on load.
    // The old letter-or-digit algorithm replaced valid filename chars
    // (hyphens, apostrophes, periods) with underscores, producing paths that
    // didn't match what FileSaveBackend would look for when reading back.
    private static string SanitizeFileName(string name)
        => string.Join("_", name.Split(Path.GetInvalidFileNameChars()));
}
