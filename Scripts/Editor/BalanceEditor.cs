using System;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text.Json;
using UsurperRemake.Data;
using UsurperRemake.Systems;

namespace UsurperRemake.Editor;

/// <summary>
/// Edit <c>GameData/balance.json</c> — the game-balance constant overrides.
/// Uses reflection over <see cref="BalanceConfig"/> so newly-added balance
/// properties become editable without editor code changes. Falls back to a
/// fresh <see cref="BalanceConfig"/> (all built-in defaults) when the JSON
/// file is missing.
/// </summary>
internal static class BalanceEditor
{
    public static void Run()
    {
        var config = Load();
        bool dirty = false;
        while (true)
        {
            EditorIO.Section($"Balance — game tuning constants{(dirty ? "  [UNSAVED]" : "")}");
            int choice = EditorIO.Menu("Choose:", new[]
            {
                "List all balance values",
                "Edit a balance value",
                dirty ? "Save (writes balance.json)  *UNSAVED CHANGES*" : "Save (writes balance.json)",
                "Reset to built-in defaults (overwrites)",
                "Reload from disk (discard unsaved changes)",
            });
            switch (choice)
            {
                case 0:
                    if (dirty && !EditorIO.Confirm("Discard unsaved changes and exit?")) continue;
                    return;
                case 1: ListAll(config); break;
                case 2: if (EditOne(config)) dirty = true; break;
                case 3: if (Save(config)) dirty = false; break;
                case 4:
                    if (EditorIO.Confirm("Overwrite balance.json with built-in defaults?"))
                    {
                        config = new BalanceConfig();
                        if (Save(config)) dirty = false;
                    }
                    break;
                case 5:
                    if (dirty && !EditorIO.Confirm("Discard unsaved changes?")) break;
                    config = Load();
                    dirty = false;
                    EditorIO.Info("Reloaded from disk.");
                    EditorIO.Pause();
                    break;
            }
        }
    }

    private static string GetBalanceJsonPath()
        => Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "GameData", "balance.json");

    private static BalanceConfig Load()
    {
        var path = GetBalanceJsonPath();
        if (!File.Exists(path)) return new BalanceConfig();
        try
        {
            var json = File.ReadAllText(path);
            return JsonSerializer.Deserialize<BalanceConfig>(json, GameDataLoader.JsonOptions) ?? new BalanceConfig();
        }
        catch (Exception ex)
        {
            EditorIO.Error($"Failed to read {path}: {ex.Message}");
            return new BalanceConfig();
        }
    }

    private static bool Save(BalanceConfig config)
    {
        var path = GetBalanceJsonPath();
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(path)!);
            File.WriteAllText(path, JsonSerializer.Serialize(config, GameDataLoader.JsonOptions));
            EditorIO.Success($"Wrote balance.json to {path}");
            EditorIO.Info("Restart the game to pick up the changes.");
            EditorIO.Pause();
            return true;
        }
        catch (Exception ex)
        {
            EditorIO.Error($"Failed to write {path}: {ex.Message}");
            EditorIO.Pause();
            return false;
        }
    }

    private static PropertyInfo[] EditableProps()
        => typeof(BalanceConfig).GetProperties(BindingFlags.Public | BindingFlags.Instance)
            .Where(p => p.CanRead && p.CanWrite)
            .OrderBy(p => p.Name)
            .ToArray();

    private static void ListAll(BalanceConfig config)
    {
        EditorIO.Section("Balance values");
        foreach (var prop in EditableProps())
        {
            var val = prop.GetValue(config);
            EditorIO.Info($"  {prop.Name,-45} = {val}   ({prop.PropertyType.Name})");
        }
        EditorIO.Pause();
    }

    private static bool EditOne(BalanceConfig config)
    {
        var props = EditableProps();
        // Present every balance property as an arrow-key list so users don't
        // have to know exact names. Shows current value alongside each entry.
        var labels = props.Select(p => $"{p.Name,-45} = {p.GetValue(config)}   ({p.PropertyType.Name})").ToList();
        int pick = EditorIO.Menu("Property to edit", labels);
        if (pick == 0) return false;
        var match = props[pick - 1];

        var current = match.GetValue(config);
        EditorIO.Info($"Editing {match.Name} ({match.PropertyType.Name}). Current: {current}");

        object? newVal = current;
        if (match.PropertyType == typeof(int))
            newVal = EditorIO.PromptInt("New value", current == null ? 0 : (int)current);
        else if (match.PropertyType == typeof(long))
            newVal = EditorIO.PromptLong("New value", current == null ? 0L : (long)current);
        else if (match.PropertyType == typeof(float))
        {
            var fs = EditorIO.PromptString("New value", current?.ToString() ?? "0");
            if (float.TryParse(fs, System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out var f))
                newVal = f;
            else EditorIO.Warn("Not a float; keeping current.");
        }
        else if (match.PropertyType == typeof(double))
        {
            var ds = EditorIO.PromptString("New value", current?.ToString() ?? "0");
            if (double.TryParse(ds, System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out var d))
                newVal = d;
            else EditorIO.Warn("Not a double; keeping current.");
        }
        else if (match.PropertyType == typeof(bool))
            newVal = EditorIO.PromptBool("New value", current == null ? false : (bool)current);
        else
        {
            EditorIO.Warn($"Type {match.PropertyType.Name} not supported by the editor. Edit balance.json directly.");
            EditorIO.Pause();
            return false;
        }

        match.SetValue(config, newVal);
        EditorIO.Success($"{match.Name} = {newVal}. Use 'Save' to persist.");
        EditorIO.Pause();
        return true;
    }
}
