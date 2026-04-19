using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using UsurperRemake.Data;
using UsurperRemake.Systems;

namespace UsurperRemake.Editor;

/// <summary>
/// Edit <c>GameData/dialogue.json</c>. ~500 built-in lines keyed by NPC
/// personality, category (greeting/farewell/smalltalk/etc.), relationship
/// tier, emotion, and context. Modders add lines to flesh out specific
/// personalities or NPCs. Filter-driven editing by category/personality.
/// </summary>
internal static class DialogueEditor
{
    public static void Run()
    {
        var list = LoadOrSeed();
        bool dirty = false;
        while (true)
        {
            EditorIO.Section($"Dialogue lines  —  {list.Count}{(dirty ? "  [UNSAVED]" : "")}");
            int choice = EditorIO.Menu("Choose:", new[]
            {
                "List / filter lines",
                "Edit a line",
                "Add new line",
                "Delete a line",
                "Show unique personality types / categories in the set",
                dirty ? "Save (writes dialogue.json)  *UNSAVED CHANGES*" : "Save (writes dialogue.json)",
                "Reset to built-in defaults",
                "Reload from disk (discard unsaved changes)",
            });
            switch (choice)
            {
                case 0:
                    if (dirty && !EditorIO.Confirm("Discard unsaved changes and exit?")) continue;
                    return;
                case 1: ListLines(list); break;
                case 2: if (EditLine(list)) dirty = true; break;
                case 3: if (AddLine(list)) dirty = true; break;
                case 4: if (DeleteLine(list)) dirty = true; break;
                case 5: ShowVocab(list); break;
                case 6: if (Save(list)) dirty = false; break;
                case 7:
                    if (EditorIO.Confirm("Overwrite dialogue.json with built-in defaults?"))
                    {
                        list = NPCDialogueDatabase.GetAllBuiltInLines();
                        if (Save(list)) dirty = false;
                    }
                    break;
                case 8:
                    if (dirty && !EditorIO.Confirm("Discard unsaved changes?")) break;
                    list = LoadOrSeed();
                    dirty = false;
                    EditorIO.Info("Reloaded from disk.");
                    EditorIO.Pause();
                    break;
            }
        }
    }

    private static string GetJsonPath()
        => Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "GameData", "dialogue.json");

    private static List<NPCDialogueDatabase.DialogueLine> LoadOrSeed()
    {
        var path = GetJsonPath();
        if (!File.Exists(path)) return NPCDialogueDatabase.GetAllBuiltInLines();
        try
        {
            var json = File.ReadAllText(path);
            return JsonSerializer.Deserialize<List<NPCDialogueDatabase.DialogueLine>>(json, GameDataLoader.JsonOptions)
                   ?? NPCDialogueDatabase.GetAllBuiltInLines();
        }
        catch (Exception ex)
        {
            EditorIO.Error($"Failed to read {path}: {ex.Message}");
            return NPCDialogueDatabase.GetAllBuiltInLines();
        }
    }

    private static bool Save(List<NPCDialogueDatabase.DialogueLine> list)
    {
        var path = GetJsonPath();
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(path)!);
            File.WriteAllText(path, JsonSerializer.Serialize(list, GameDataLoader.JsonOptions));
            EditorIO.Success($"Wrote {list.Count} lines to {path}");
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

    private static void ListLines(List<NPCDialogueDatabase.DialogueLine> list)
    {
        string category = EditorIO.Prompt("Filter by category (blank = any, 'q' to cancel)");
        if (category == "q") return;
        string personality = EditorIO.Prompt("Filter by personality (blank = any)");
        string textQuery = EditorIO.Prompt("Filter by text substring (blank = any)");

        var shown = list.AsEnumerable();
        if (!string.IsNullOrWhiteSpace(category))
            shown = shown.Where(d => d.Category.Contains(category, StringComparison.OrdinalIgnoreCase));
        if (!string.IsNullOrWhiteSpace(personality))
            shown = shown.Where(d => (d.PersonalityType ?? "").Contains(personality, StringComparison.OrdinalIgnoreCase));
        if (!string.IsNullOrWhiteSpace(textQuery))
            shown = shown.Where(d => d.Text.Contains(textQuery, StringComparison.OrdinalIgnoreCase));

        var results = shown.ToList();
        EditorIO.Info($"Matches: {results.Count}");
        int page = 0, pageSize = 25;
        while (page * pageSize < results.Count)
        {
            foreach (var d in results.Skip(page * pageSize).Take(pageSize))
                EditorIO.Info($"  {d.Id,-14} [{d.Category,-10}] [{d.PersonalityType ?? "any",-10}] tier={d.RelationshipTier,-2}  \"{d.Text}\"");
            page++;
            if (page * pageSize >= results.Count) break;
            if (!EditorIO.Confirm($"Shown {page * pageSize}/{results.Count}. More?")) break;
        }
        EditorIO.Pause();
    }

    /// <summary>
    /// With ~500 built-in lines, present the pick as a filtered arrow menu so
    /// users don't need to know or type line IDs. If the working set is too
    /// big (Spectre handles ~100 fine but thousands lag), we prompt for a
    /// substring filter first and only show matches in the picker.
    /// </summary>
    private static NPCDialogueDatabase.DialogueLine? PickLine(List<NPCDialogueDatabase.DialogueLine> list, string title)
    {
        if (list.Count == 0) { EditorIO.Warn("No dialogue lines."); EditorIO.Pause(); return null; }

        IEnumerable<NPCDialogueDatabase.DialogueLine> subset = list;
        if (list.Count > 80)
        {
            var filter = EditorIO.Prompt("Filter by ID, category, or text substring (blank = all)");
            if (!string.IsNullOrWhiteSpace(filter))
            {
                subset = list.Where(d =>
                    d.Id.Contains(filter, StringComparison.OrdinalIgnoreCase) ||
                    d.Category.Contains(filter, StringComparison.OrdinalIgnoreCase) ||
                    d.Text.Contains(filter, StringComparison.OrdinalIgnoreCase));
            }
        }
        var ordered = subset.OrderBy(d => d.Category).ThenBy(d => d.Id).ToList();
        if (ordered.Count == 0) { EditorIO.Warn("No matches."); EditorIO.Pause(); return null; }
        if (ordered.Count > 300)
        {
            EditorIO.Warn($"{ordered.Count} matches — tighten the filter.");
            EditorIO.Pause();
            return null;
        }
        var labels = ordered.Select(d =>
        {
            string snippet = d.Text.Length > 50 ? d.Text.Substring(0, 47) + "..." : d.Text;
            return $"{d.Id,-12} [{d.Category,-10}] \"{snippet}\"";
        }).ToList();
        int pick = EditorIO.Menu(title, labels);
        if (pick == 0) return null;
        return ordered[pick - 1];
    }

    private static bool EditLine(List<NPCDialogueDatabase.DialogueLine> list)
    {
        var d = PickLine(list, "Line to edit");
        if (d == null) return false;
        EditFields(d);
        EditorIO.Success("Updated. Use 'Save' when done.");
        EditorIO.Pause();
        return true;
    }

    private static bool AddLine(List<NPCDialogueDatabase.DialogueLine> list)
    {
        var d = new NPCDialogueDatabase.DialogueLine { Id = "mod_" + DateTime.UtcNow.Ticks, Category = "greeting" };
        EditFields(d);
        list.Add(d);
        EditorIO.Success("Added. Use 'Save' when done.");
        EditorIO.Pause();
        return true;
    }

    private static bool DeleteLine(List<NPCDialogueDatabase.DialogueLine> list)
    {
        var d = PickLine(list, "Line to delete");
        if (d == null) return false;
        if (!EditorIO.Confirm($"Delete \"{d.Text}\" ({d.Id})?")) return false;
        list.Remove(d);
        EditorIO.Success("Deleted.");
        EditorIO.Pause();
        return true;
    }

    private static void ShowVocab(List<NPCDialogueDatabase.DialogueLine> list)
    {
        var categories = list.Select(d => d.Category).Where(s => !string.IsNullOrEmpty(s)).Distinct().OrderBy(s => s);
        var personalities = list.Select(d => d.PersonalityType ?? "").Where(s => !string.IsNullOrEmpty(s)).Distinct().OrderBy(s => s);
        var emotions = list.Select(d => d.Emotion ?? "").Where(s => !string.IsNullOrEmpty(s)).Distinct().OrderBy(s => s);
        var contexts = list.Select(d => d.Context ?? "").Where(s => !string.IsNullOrEmpty(s)).Distinct().OrderBy(s => s);
        EditorIO.Info("Categories:   " + string.Join(", ", categories));
        EditorIO.Info("Personalities:" + string.Join(", ", personalities));
        EditorIO.Info("Emotions:     " + string.Join(", ", emotions));
        EditorIO.Info("Contexts:     " + string.Join(", ", contexts));
        EditorIO.Pause();
    }

    private static void EditFields(NPCDialogueDatabase.DialogueLine d)
    {
        d.Id = EditorIO.PromptString("Id (unique)", d.Id);
        d.Text = EditorIO.PromptString("Text", d.Text);
        d.Category = EditorIO.PromptChoice("Category", EditorVocab.DialogueCategories, d.Category);
        d.NpcName = EditorIO.PromptString("NpcName (blank = generic)", d.NpcName ?? "");
        if (string.IsNullOrWhiteSpace(d.NpcName)) d.NpcName = null;
        d.PersonalityType = EditorIO.PromptChoice("PersonalityType (blank = any)", EditorVocab.DialoguePersonalityTypes, d.PersonalityType ?? "");
        if (string.IsNullOrWhiteSpace(d.PersonalityType)) d.PersonalityType = null;
        d.RelationshipTier = EditorIO.PromptInt("RelationshipTier (0 = any)", d.RelationshipTier);
        d.Emotion = EditorIO.PromptChoice("Emotion (blank = any)", EditorVocab.DialogueEmotions, d.Emotion ?? "");
        if (string.IsNullOrWhiteSpace(d.Emotion)) d.Emotion = null;
        d.Context = EditorIO.PromptChoice("Context (blank = any)", EditorVocab.DialogueContexts, d.Context ?? "");
        if (string.IsNullOrWhiteSpace(d.Context)) d.Context = null;
        d.MemoryType = EditorIO.PromptChoice("MemoryType (blank = any)", EditorVocab.DialogueMemoryTypes, d.MemoryType ?? "");
        if (string.IsNullOrWhiteSpace(d.MemoryType)) d.MemoryType = null;
        d.EventType = EditorIO.PromptChoice("EventType (blank = any)", EditorVocab.DialogueEventTypes, d.EventType ?? "");
        if (string.IsNullOrWhiteSpace(d.EventType)) d.EventType = null;
    }
}
