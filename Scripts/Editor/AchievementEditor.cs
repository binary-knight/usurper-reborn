using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using UsurperRemake.Systems;

namespace UsurperRemake.Editor;

/// <summary>
/// Edit <c>GameData/achievements.json</c>. ~75 entries by default. Each has
/// an Id, name, description, category, tier, secret flag, point value, and
/// gold/XP reward on unlock. Custom achievements go in the same file —
/// modders can add entries, not just modify.
/// </summary>
internal static class AchievementEditor
{
    public static void Run()
    {
        var list = LoadOrSeed();
        bool dirty = false;
        while (true)
        {
            EditorIO.Section($"Achievements  —  {list.Count}{(dirty ? "  [UNSAVED]" : "")}");
            int choice = EditorIO.Menu("Choose:", new[]
            {
                "List / filter achievements",
                "Edit an achievement",
                "Add new achievement",
                "Delete an achievement",
                dirty ? "Save (writes achievements.json)  *UNSAVED CHANGES*" : "Save (writes achievements.json)",
                "Reset to built-in defaults",
                "Reload from disk (discard unsaved changes)",
            });
            switch (choice)
            {
                case 0:
                    if (dirty && !EditorIO.Confirm("Discard unsaved changes and exit?")) continue;
                    return;
                case 1: ListAchievements(list); break;
                case 2: if (EditAchievement(list)) dirty = true; break;
                case 3: if (AddAchievement(list)) dirty = true; break;
                case 4: if (DeleteAchievement(list)) dirty = true; break;
                case 5: if (Save(list)) dirty = false; break;
                case 6:
                    if (EditorIO.Confirm("Overwrite achievements.json with built-in defaults?"))
                    {
                        list = AchievementSystem.GetBuiltInAchievements();
                        if (Save(list)) dirty = false;
                    }
                    break;
                case 7:
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
        => Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "GameData", "achievements.json");

    private static List<Achievement> LoadOrSeed()
    {
        var path = GetJsonPath();
        if (!File.Exists(path)) return AchievementSystem.GetBuiltInAchievements();
        try
        {
            var json = File.ReadAllText(path);
            return JsonSerializer.Deserialize<List<Achievement>>(json, GameDataLoader.JsonOptions)
                   ?? AchievementSystem.GetBuiltInAchievements();
        }
        catch (Exception ex)
        {
            EditorIO.Error($"Failed to read {path}: {ex.Message}");
            return AchievementSystem.GetBuiltInAchievements();
        }
    }

    private static bool Save(List<Achievement> list)
    {
        var path = GetJsonPath();
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(path)!);
            File.WriteAllText(path, JsonSerializer.Serialize(list, GameDataLoader.JsonOptions));
            EditorIO.Success($"Wrote {list.Count} achievements to {path}");
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

    private static void ListAchievements(List<Achievement> list)
    {
        string filter = EditorIO.Prompt("Filter by ID or name (blank for all, 'q' to cancel)");
        if (filter == "q") return;
        var shown = string.IsNullOrWhiteSpace(filter)
            ? list
            : list.Where(a => a.Id.Contains(filter, StringComparison.OrdinalIgnoreCase)
                           || a.Name.Contains(filter, StringComparison.OrdinalIgnoreCase)).ToList();
        int page = 0, pageSize = 25;
        while (page * pageSize < shown.Count)
        {
            foreach (var a in shown.Skip(page * pageSize).Take(pageSize))
                EditorIO.Info($"  {a.Id,-30} tier={a.Tier,-8} cat={a.Category,-14} pts={a.PointValue,-4} {(a.IsSecret ? "[SECRET] " : "")}\"{a.Name}\"");
            page++;
            if (page * pageSize >= shown.Count) break;
            if (!EditorIO.Confirm($"Shown {page * pageSize}/{shown.Count}. More?")) break;
        }
        EditorIO.Pause();
    }

    private static Achievement? PickAchievement(List<Achievement> list, string title)
    {
        if (list.Count == 0) { EditorIO.Warn("No achievements."); EditorIO.Pause(); return null; }
        var labels = list.OrderBy(a => a.Id).Select(a => $"[{a.Tier}] {a.Name,-40} ({a.Id})").ToList();
        int pick = EditorIO.Menu(title, labels);
        if (pick == 0) return null;
        return list.OrderBy(a => a.Id).ElementAt(pick - 1);
    }

    private static bool EditAchievement(List<Achievement> list)
    {
        var a = PickAchievement(list, "Achievement to edit");
        if (a == null) return false;
        EditFields(a);
        EditorIO.Success("Updated. Use 'Save' when done.");
        EditorIO.Pause();
        return true;
    }

    private static bool AddAchievement(List<Achievement> list)
    {
        var a = new Achievement { Id = "new_achievement_" + DateTime.UtcNow.Ticks, Name = "New Achievement" };
        EditFields(a);
        list.Add(a);
        EditorIO.Success("Added. Use 'Save' when done.");
        EditorIO.Pause();
        return true;
    }

    private static bool DeleteAchievement(List<Achievement> list)
    {
        var a = PickAchievement(list, "Achievement to delete");
        if (a == null) return false;
        if (!EditorIO.Confirm($"Delete \"{a.Name}\" ({a.Id})?")) return false;
        list.Remove(a);
        EditorIO.Success("Deleted.");
        EditorIO.Pause();
        return true;
    }

    private static void EditFields(Achievement a)
    {
        a.Id = EditorIO.PromptString("Id (unique)", a.Id);
        a.Name = EditorIO.PromptString("Name", a.Name);
        a.Description = EditorIO.PromptString("Description", a.Description);
        a.SecretHint = EditorIO.PromptString("SecretHint (shown instead of description if secret)", a.SecretHint);
        a.Category = EditorIO.PromptEnum("Category", a.Category);
        a.Tier = EditorIO.PromptEnum("Tier", a.Tier);
        a.IsSecret = EditorIO.PromptBool("IsSecret", a.IsSecret);
        a.PointValue = EditorIO.PromptInt("PointValue", a.PointValue, min: 0);
        a.GoldReward = EditorIO.PromptLong("GoldReward", a.GoldReward, min: 0);
        a.ExperienceReward = EditorIO.PromptLong("ExperienceReward", a.ExperienceReward, min: 0);
        a.UnlockMessage = EditorIO.PromptString("UnlockMessage (blank if none)", a.UnlockMessage ?? "");
        if (string.IsNullOrWhiteSpace(a.UnlockMessage)) a.UnlockMessage = null;
    }
}
