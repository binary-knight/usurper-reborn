using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using UsurperRemake.Systems;

namespace UsurperRemake.Editor;

/// <summary>
/// CRUD editor for modder-added custom equipment. Reads/writes
/// <c>GameData/equipment.json</c>. Built-in items (IDs &lt; 200000) are shown
/// for reference but are read-only — they're defined in code and preserve
/// save compatibility across game versions. Modders can only add/edit/delete
/// entries at IDs 200000+.
/// </summary>
internal static class EquipmentEditor
{
    public static void Run()
    {
        // Load once; preserve in-memory edits across menu navigations until
        // the user explicitly saves or reloads.
        var custom = LoadCustomItems();
        bool dirty = false;
        while (true)
        {
            int builtInCount = EquipmentDatabase.GetAll().Count(e => e.Id < GameDataLoader.ModdedEquipmentIdStart);
            EditorIO.Section($"Equipment  —  {builtInCount} built-in (read-only), {custom.Count} custom{(dirty ? "  [UNSAVED]" : "")}");
            int choice = EditorIO.Menu("Choose:", new[]
            {
                "List custom items",
                "Add new custom item",
                "Edit existing custom item",
                "Delete custom item",
                "Browse built-in items (read-only reference)",
                dirty ? "Save (writes equipment.json)  *UNSAVED CHANGES*" : "Save (writes equipment.json)",
                "Reload from disk (discard unsaved changes)",
            });
            switch (choice)
            {
                case 0:
                    if (dirty && !EditorIO.Confirm("Discard unsaved changes and exit?")) continue;
                    return;
                case 1: ListCustomItems(custom); break;
                case 2: if (AddItem(custom)) dirty = true; break;
                case 3: if (EditItem(custom)) dirty = true; break;
                case 4: if (DeleteItem(custom)) dirty = true; break;
                case 5: BrowseBuiltIns(); break;
                case 6: if (SaveCustomItems(custom)) dirty = false; break;
                case 7:
                    if (dirty && !EditorIO.Confirm("Discard unsaved changes?")) break;
                    custom = LoadCustomItems();
                    dirty = false;
                    EditorIO.Info("Reloaded from disk.");
                    EditorIO.Pause();
                    break;
            }
        }
    }

    private static List<Equipment> LoadCustomItems()
    {
        // Read fresh from disk each time so edits between menu navigations aren't lost.
        var path = GetEquipmentJsonPath();
        if (!File.Exists(path)) return new List<Equipment>();
        try
        {
            var json = File.ReadAllText(path);
            return JsonSerializer.Deserialize<List<Equipment>>(json, GameDataLoader.JsonOptions) ?? new List<Equipment>();
        }
        catch (Exception ex)
        {
            EditorIO.Error($"Failed to read {path}: {ex.Message}");
            return new List<Equipment>();
        }
    }

    private static bool SaveCustomItems(List<Equipment> items)
    {
        // v0.57.4: validate every item is in the custom ID range BEFORE writing.
        // Built-in items (< 200000) get silently dropped by EquipmentData.Initialize,
        // so saving a file that includes them looks successful but the game ignores
        // those entries. Most commonly this happens when a user hand-edits the JSON
        // between editor sessions. Duplicate IDs within the file have the same
        // problem — second occurrence is dropped.
        var offenders = items.Where(i => i.Id < GameDataLoader.ModdedEquipmentIdStart).ToList();
        if (offenders.Count > 0)
        {
            EditorIO.Error($"{offenders.Count} item(s) use IDs below {GameDataLoader.ModdedEquipmentIdStart} (reserved for built-in items).");
            foreach (var o in offenders.Take(10))
                EditorIO.Info($"  #{o.Id} {o.Name}");
            if (offenders.Count > 10) EditorIO.Info($"  ... and {offenders.Count - 10} more");
            EditorIO.Info("Custom IDs must be >= 200000. Fix these entries before saving.");
            EditorIO.Pause();
            return false;
        }
        var dupIds = items.GroupBy(i => i.Id).Where(g => g.Count() > 1).Select(g => g.Key).ToList();
        if (dupIds.Count > 0)
        {
            EditorIO.Error($"Duplicate IDs found: {string.Join(", ", dupIds.Take(10))}");
            EditorIO.Info("Every item needs a unique ID. Edit the duplicates before saving.");
            EditorIO.Pause();
            return false;
        }

        var path = GetEquipmentJsonPath();
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(path)!);
            var json = JsonSerializer.Serialize(items, GameDataLoader.JsonOptions);
            File.WriteAllText(path, json);
            EditorIO.Success($"Wrote {items.Count} item(s) to {path}");
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

    private static string GetEquipmentJsonPath()
        => Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "GameData", "equipment.json");

    private static void ListCustomItems(List<Equipment> items)
    {
        EditorIO.Section($"Custom items ({items.Count})");
        if (items.Count == 0)
        {
            EditorIO.Info("  (none yet — use 'Add new custom item' to create one)");
        }
        else
        {
            foreach (var e in items.OrderBy(x => x.Id))
                EditorIO.Info($"  #{e.Id,-7} {e.Name,-40} slot={e.Slot} rarity={e.Rarity} value={e.Value}");
        }
        EditorIO.Pause();
    }

    private static void BrowseBuiltIns()
    {
        var all = EquipmentDatabase.GetAll()
            .Where(e => e.Id < GameDataLoader.ModdedEquipmentIdStart)
            .OrderBy(e => e.Id)
            .ToList();
        EditorIO.Section($"Built-in equipment ({all.Count} items, read-only)");
        string filter = EditorIO.Prompt("Filter by name (blank for all, 'q' to cancel)");
        if (filter == "q") return;
        var shown = string.IsNullOrWhiteSpace(filter)
            ? all
            : all.Where(e => e.Name.Contains(filter, StringComparison.OrdinalIgnoreCase)).ToList();
        int page = 0, pageSize = 20;
        while (page * pageSize < shown.Count)
        {
            foreach (var e in shown.Skip(page * pageSize).Take(pageSize))
                EditorIO.Info($"  #{e.Id,-7} {e.Name,-40} slot={e.Slot} rarity={e.Rarity}");
            page++;
            if (page * pageSize >= shown.Count) break;
            if (!EditorIO.Confirm($"Shown {page * pageSize}/{shown.Count}. More?")) break;
        }
        EditorIO.Pause();
    }

    private static bool AddItem(List<Equipment> items)
    {
        EditorIO.Section("Add new custom item");
        int nextId = items.Count == 0
            ? GameDataLoader.ModdedEquipmentIdStart + 1
            : items.Max(i => i.Id) + 1;
        var item = new Equipment { Id = nextId, MinLevel = 1 };
        EditItemFields(item);
        items.Add(item);
        EditorIO.Success($"Added #{item.Id} {item.Name}. Use 'Save' when done.");
        EditorIO.Pause();
        return true;
    }

    private static bool EditItem(List<Equipment> items)
    {
        if (items.Count == 0) { EditorIO.Warn("No custom items yet."); EditorIO.Pause(); return false; }
        var labels = items.OrderBy(x => x.Id).Select(x => $"#{x.Id}  {x.Name}  ({x.Slot})").ToList();
        int pick = EditorIO.Menu("Custom item to edit", labels);
        if (pick == 0) return false;
        var ordered = items.OrderBy(x => x.Id).ToList();
        EditItemFields(ordered[pick - 1]);
        EditorIO.Success("Item updated. Use 'Save' when done.");
        EditorIO.Pause();
        return true;
    }

    private static bool DeleteItem(List<Equipment> items)
    {
        if (items.Count == 0) { EditorIO.Warn("No custom items."); EditorIO.Pause(); return false; }
        var labels = items.OrderBy(x => x.Id).Select(x => $"#{x.Id}  {x.Name}  ({x.Slot})").ToList();
        int pick = EditorIO.Menu("Custom item to delete", labels);
        if (pick == 0) return false;
        var ordered = items.OrderBy(x => x.Id).ToList();
        var item = ordered[pick - 1];
        if (!EditorIO.Confirm($"Delete #{item.Id} \"{item.Name}\"?")) return false;
        items.Remove(item);
        EditorIO.Success("Deleted. Use 'Save' when done.");
        EditorIO.Pause();
        return true;
    }

    private static void EditItemFields(Equipment e)
    {
        EditorIO.Info($"Editing item #{e.Id}. Press Enter to keep current value.");
        e.Name = EditorIO.PromptString("Name", e.Name);
        e.Description = EditorIO.PromptString("Description", e.Description);
        e.Slot = EditorIO.PromptEnum("Slot", e.Slot);
        // v0.57.4 (pass 4): bound-check the raw-stat fields where negatives
        // produce nonsense (weapon deals negative damage, armor reduces AC,
        // shield blocks -50%, item "costs" -200 gold). Stat-bonus fields
        // (STR+/DEX+/etc.) stay unbounded — cursed items legitimately carry
        // negative stat bonuses and modders might want -2 STR penalties on
        // an iron-heavy cuirass, etc.
        if (e.Slot == EquipmentSlot.MainHand || e.Slot == EquipmentSlot.OffHand)
        {
            e.Handedness = EditorIO.PromptEnum("Handedness", e.Handedness);
            e.WeaponType = EditorIO.PromptEnum("WeaponType", e.WeaponType);
            e.WeaponPower = EditorIO.PromptInt("WeaponPower", e.WeaponPower, min: 0);
            if (e.WeaponType == WeaponType.Shield || e.WeaponType == WeaponType.Buckler || e.WeaponType == WeaponType.TowerShield)
            {
                e.ShieldBonus = EditorIO.PromptInt("ShieldBonus", e.ShieldBonus, min: 0);
                e.BlockChance = EditorIO.PromptInt("BlockChance (percent)", e.BlockChance, min: 0, max: 100);
            }
        }
        else
        {
            e.ArmorType = EditorIO.PromptEnum("ArmorType", e.ArmorType);
            e.WeightClass = EditorIO.PromptEnum("WeightClass", e.WeightClass);
            e.ArmorClass = EditorIO.PromptInt("ArmorClass", e.ArmorClass, min: 0);
        }

        EditorIO.Info("— Primary stat bonuses (negative allowed for cursed items) —");
        e.StrengthBonus = EditorIO.PromptInt("STR", e.StrengthBonus);
        e.DexterityBonus = EditorIO.PromptInt("DEX", e.DexterityBonus);
        e.ConstitutionBonus = EditorIO.PromptInt("CON", e.ConstitutionBonus);
        e.IntelligenceBonus = EditorIO.PromptInt("INT", e.IntelligenceBonus);
        e.WisdomBonus = EditorIO.PromptInt("WIS", e.WisdomBonus);
        e.CharismaBonus = EditorIO.PromptInt("CHA", e.CharismaBonus);

        EditorIO.Info("— Secondary bonuses —");
        e.MaxHPBonus = EditorIO.PromptInt("MaxHP+", e.MaxHPBonus);
        e.MaxManaBonus = EditorIO.PromptInt("MaxMana+", e.MaxManaBonus);
        e.DefenceBonus = EditorIO.PromptInt("Defence+", e.DefenceBonus);

        EditorIO.Info("— Restrictions & economy —");
        e.MinLevel = EditorIO.PromptInt("MinLevel", e.MinLevel, min: 1, max: 100);
        e.Value = EditorIO.PromptLong("Value (gold, 0 = unsellable)", e.Value, min: 0);
        e.Rarity = EditorIO.PromptEnum("Rarity", e.Rarity);
    }
}
