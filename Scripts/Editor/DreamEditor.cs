using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using UsurperRemake.Systems;

namespace UsurperRemake.Editor;

/// <summary>
/// Edit <c>GameData/dreams.json</c>. Each dream has a title, unlock conditions
/// (level range, awakening level, cycle, floor, god states, alignment, etc.)
/// and a Content array that's the actual narrative text shown line-by-line.
/// The list tends to have ~50 entries so this is a filter-driven editor.
/// </summary>
internal static class DreamEditor
{
    public static void Run()
    {
        var dreams = LoadOrSeed();
        bool dirty = false;
        while (true)
        {
            EditorIO.Section($"Dreams  —  {dreams.Count}{(dirty ? "  [UNSAVED]" : "")}");
            int choice = EditorIO.Menu("Choose:", new[]
            {
                "List / filter dreams",
                "Edit a dream",
                "Add new dream",
                "Delete a dream",
                "Edit content (narrative text) of a dream",
                dirty ? "Save (writes dreams.json)  *UNSAVED CHANGES*" : "Save (writes dreams.json)",
                "Reset to built-in defaults",
                "Reload from disk (discard unsaved changes)",
            });
            switch (choice)
            {
                case 0:
                    if (dirty && !EditorIO.Confirm("Discard unsaved changes and exit?")) continue;
                    return;
                case 1: ListDreams(dreams); break;
                case 2: if (EditDream(dreams)) dirty = true; break;
                case 3: if (AddDream(dreams)) dirty = true; break;
                case 4: if (DeleteDream(dreams)) dirty = true; break;
                case 5: if (EditContent(dreams)) dirty = true; break;
                case 6: if (Save(dreams)) dirty = false; break;
                case 7:
                    if (EditorIO.Confirm("Overwrite dreams.json with built-in defaults?"))
                    {
                        dreams = DreamSystem.GetBuiltInDreams();
                        if (Save(dreams)) dirty = false;
                    }
                    break;
                case 8:
                    if (dirty && !EditorIO.Confirm("Discard unsaved changes?")) break;
                    dreams = LoadOrSeed();
                    dirty = false;
                    EditorIO.Info("Reloaded from disk.");
                    EditorIO.Pause();
                    break;
            }
        }
    }

    private static string GetJsonPath()
        => Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "GameData", "dreams.json");

    private static List<NarrativeDreamData> LoadOrSeed()
    {
        var path = GetJsonPath();
        if (!File.Exists(path)) return DreamSystem.GetBuiltInDreams();
        try
        {
            var json = File.ReadAllText(path);
            return JsonSerializer.Deserialize<List<NarrativeDreamData>>(json, GameDataLoader.JsonOptions)
                   ?? DreamSystem.GetBuiltInDreams();
        }
        catch (Exception ex)
        {
            EditorIO.Error($"Failed to read {path}: {ex.Message}");
            return DreamSystem.GetBuiltInDreams();
        }
    }

    private static bool Save(List<NarrativeDreamData> list)
    {
        var path = GetJsonPath();
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(path)!);
            File.WriteAllText(path, JsonSerializer.Serialize(list, GameDataLoader.JsonOptions));
            EditorIO.Success($"Wrote {list.Count} dreams to {path}");
            EditorIO.Info("Restart the game to pick up the changes.");
            EditorIO.Pause();
            return true;
        }
        catch (Exception ex)
        {
            EditorIO.Error($"Write failed: {ex.Message}");
            EditorIO.Pause();
            return false;
        }
    }

    private static void ListDreams(List<NarrativeDreamData> dreams)
    {
        string filter = EditorIO.Prompt("Filter by ID or title (blank for all, 'q' to cancel)");
        if (filter == "q") return;
        var shown = string.IsNullOrWhiteSpace(filter)
            ? dreams
            : dreams.Where(d => d.Id.Contains(filter, StringComparison.OrdinalIgnoreCase)
                             || d.Title.Contains(filter, StringComparison.OrdinalIgnoreCase)).ToList();
        int page = 0, pageSize = 25;
        while (page * pageSize < shown.Count)
        {
            foreach (var d in shown.Skip(page * pageSize).Take(pageSize))
                EditorIO.Info($"  {d.Id,-30} L{d.MinLevel}-{d.MaxLevel,-3} awk{d.MinAwakening}-{d.MaxAwakening}  pri:{d.Priority,-3}  \"{d.Title}\"");
            page++;
            if (page * pageSize >= shown.Count) break;
            if (!EditorIO.Confirm($"Shown {page * pageSize}/{shown.Count}. More?")) break;
        }
        EditorIO.Pause();
    }

    private static NarrativeDreamData? PickDream(List<NarrativeDreamData> dreams, string title)
    {
        if (dreams.Count == 0) { EditorIO.Warn("No dreams."); EditorIO.Pause(); return null; }
        var labels = dreams.OrderBy(d => d.Id).Select(d => $"{d.Id,-30} L{d.MinLevel}-{d.MaxLevel,-3}  {d.Title}").ToList();
        int pick = EditorIO.Menu(title, labels);
        if (pick == 0) return null;
        return dreams.OrderBy(d => d.Id).ElementAt(pick - 1);
    }

    private static bool EditDream(List<NarrativeDreamData> dreams)
    {
        var d = PickDream(dreams, "Dream to edit");
        if (d == null) return false;
        EditCommonFields(d);
        EditorIO.Success("Updated. Use 'Save' when done.");
        EditorIO.Pause();
        return true;
    }

    private static bool AddDream(List<NarrativeDreamData> dreams)
    {
        var d = new NarrativeDreamData { Id = "new_dream_" + DateTime.UtcNow.Ticks, Title = "New Dream", Content = Array.Empty<string>() };
        EditCommonFields(d);
        dreams.Add(d);
        EditorIO.Success("Added. Edit its content via 'Edit content of a dream'.");
        EditorIO.Pause();
        return true;
    }

    private static bool DeleteDream(List<NarrativeDreamData> dreams)
    {
        var d = PickDream(dreams, "Dream to delete");
        if (d == null) return false;
        if (!EditorIO.Confirm($"Delete \"{d.Title}\" ({d.Id})?")) return false;
        dreams.Remove(d);
        EditorIO.Success("Deleted.");
        EditorIO.Pause();
        return true;
    }

    private static void EditCommonFields(NarrativeDreamData d)
    {
        d.Id = EditorIO.PromptString("Id (unique identifier)", d.Id);
        d.Title = EditorIO.PromptString("Title", d.Title);
        d.MinLevel = EditorIO.PromptInt("MinLevel", d.MinLevel, min: 0, max: 100);
        d.MaxLevel = EditorIO.PromptInt("MaxLevel", d.MaxLevel, min: d.MinLevel, max: 100);
        d.MinAwakening = EditorIO.PromptInt("MinAwakening (0-7)", d.MinAwakening, min: 0, max: 7);
        d.MaxAwakening = EditorIO.PromptInt("MaxAwakening", d.MaxAwakening, min: d.MinAwakening, max: 7);
        d.MinCycle = EditorIO.PromptInt("MinCycle", d.MinCycle, min: 1);
        d.RequiredFloor = EditorIO.PromptInt("RequiredFloor (0 = any)", d.RequiredFloor, min: 0);
        d.MaxFloor = EditorIO.PromptInt("MaxFloor (0 = any)", d.MaxFloor, min: 0);
        d.Priority = EditorIO.PromptInt("Priority (higher = picked first)", d.Priority);
        d.AwakeningGain = EditorIO.PromptInt("AwakeningGain (added on trigger)", d.AwakeningGain, min: 0);
        d.MinChivalry = EditorIO.PromptInt("MinChivalry", d.MinChivalry, min: 0);
        d.MinDarkness = EditorIO.PromptInt("MinDarkness", d.MinDarkness, min: 0);
        d.MinKills = EditorIO.PromptInt("MinKills", d.MinKills, min: 0);
        d.MinDeepestFloor = EditorIO.PromptInt("MinDeepestFloor", d.MinDeepestFloor, min: 0);
        d.RequiresIsKing = EditorIO.PromptBool("RequiresIsKing", d.RequiresIsKing);
        d.RequiresMarriage = EditorIO.PromptBool("RequiresMarriage", d.RequiresMarriage);
        d.RequiresAllSeals = EditorIO.PromptBool("RequiresAllSeals", d.RequiresAllSeals);
        d.RequiresAnyCompanionQuestDone = EditorIO.PromptBool("RequiresAnyCompanionQuestDone", d.RequiresAnyCompanionQuestDone);
        d.PhilosophicalHint = EditorIO.PromptString("PhilosophicalHint (optional subtitle)", d.PhilosophicalHint);
    }

    private static bool EditContent(List<NarrativeDreamData> dreams)
    {
        var d = PickDream(dreams, "Dream to edit content of");
        if (d == null) return false;
        var lines = (d.Content ?? Array.Empty<string>()).ToList();
        bool anyChange = false;
        while (true)
        {
            EditorIO.Section($"Content of \"{d.Title}\" ({lines.Count} lines)");
            for (int i = 0; i < lines.Count; i++)
                EditorIO.Info($"  [{i + 1,2}] {lines[i]}");
            int choice = EditorIO.Menu("Content:", new[]
            {
                "Append line",
                "Edit line",
                "Delete line",
                "Clear all lines",
                "Commit content back to the dream",
            });
            switch (choice)
            {
                case 0: return anyChange;
                case 1: lines.Add(EditorIO.Prompt("New line")); anyChange = true; break;
                case 2:
                    {
                        if (lines.Count == 0) { EditorIO.Warn("No lines."); EditorIO.Pause(); break; }
                        int idx = EditorIO.PromptInt($"Index (1..{lines.Count})", 1, min: 1, max: lines.Count);
                        lines[idx - 1] = EditorIO.PromptString("Line", lines[idx - 1]);
                        anyChange = true;
                        break;
                    }
                case 3:
                    {
                        if (lines.Count == 0) break;
                        int idx = EditorIO.PromptInt($"Index to delete (1..{lines.Count})", 1, min: 1, max: lines.Count);
                        lines.RemoveAt(idx - 1);
                        anyChange = true;
                        break;
                    }
                case 4:
                    if (EditorIO.Confirm("Clear all lines?")) { lines.Clear(); anyChange = true; }
                    break;
                case 5:
                    d.Content = lines.ToArray();
                    EditorIO.Success("Content committed to dream (still need to 'Save' from the main Dreams menu).");
                    EditorIO.Pause();
                    return true;
            }
        }
    }
}
