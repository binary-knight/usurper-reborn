using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using UsurperRemake.Systems;

namespace UsurperRemake.Editor;

/// <summary>
/// Edit <c>GameData/monster_families.json</c>. Each family has a name, description,
/// color, attack type, and a list of tiers (Goblin -> Hobgoblin -> Bugbear -> ...)
/// where each tier owns its own level range, color, power multiplier, and a list
/// of special ability IDs. Seeds with the 16 built-in families on first run.
/// </summary>
internal static class MonsterEditor
{
    public static void Run()
    {
        var families = LoadOrSeed();
        bool dirty = false;
        while (true)
        {
            EditorIO.Section($"Monster families  —  {families.Count}{(dirty ? "  [UNSAVED]" : "")}");
            int choice = EditorIO.Menu("Choose:", new[]
            {
                "List all families",
                "Edit a family",
                "Add new family",
                "Delete a family",
                "Edit tiers of a family",
                dirty ? "Save (writes monster_families.json)  *UNSAVED CHANGES*" : "Save (writes monster_families.json)",
                "Reset to built-in defaults",
                "Reload from disk (discard unsaved changes)",
            });
            switch (choice)
            {
                case 0:
                    if (dirty && !EditorIO.Confirm("Discard unsaved changes and exit?")) continue;
                    return;
                case 1: ListAll(families); break;
                case 2: if (EditFamily(families)) dirty = true; break;
                case 3: if (AddFamily(families)) dirty = true; break;
                case 4: if (DeleteFamily(families)) dirty = true; break;
                case 5: if (EditFamilyTiers(families)) dirty = true; break;
                case 6: if (Save(families)) dirty = false; break;
                case 7:
                    if (EditorIO.Confirm("Overwrite monster_families.json with built-in defaults?"))
                    {
                        families = MonsterFamilies.GetBuiltInFamilies();
                        if (Save(families)) dirty = false;
                    }
                    break;
                case 8:
                    if (dirty && !EditorIO.Confirm("Discard unsaved changes?")) break;
                    families = LoadOrSeed();
                    dirty = false;
                    EditorIO.Info("Reloaded from disk.");
                    EditorIO.Pause();
                    break;
            }
        }
    }

    private static string GetJsonPath()
        => Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "GameData", "monster_families.json");

    private static List<MonsterFamilies.MonsterFamily> LoadOrSeed()
    {
        var path = GetJsonPath();
        if (!File.Exists(path)) return MonsterFamilies.GetBuiltInFamilies();
        try
        {
            var json = File.ReadAllText(path);
            return JsonSerializer.Deserialize<List<MonsterFamilies.MonsterFamily>>(json, GameDataLoader.JsonOptions)
                   ?? MonsterFamilies.GetBuiltInFamilies();
        }
        catch (Exception ex)
        {
            EditorIO.Error($"Failed to read {path}: {ex.Message}");
            return MonsterFamilies.GetBuiltInFamilies();
        }
    }

    private static bool Save(List<MonsterFamilies.MonsterFamily> fams)
    {
        var path = GetJsonPath();
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(path)!);
            File.WriteAllText(path, JsonSerializer.Serialize(fams, GameDataLoader.JsonOptions));
            EditorIO.Success($"Wrote {fams.Count} families to {path}");
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

    private static void ListAll(List<MonsterFamilies.MonsterFamily> fams)
    {
        EditorIO.Section($"Monster families ({fams.Count})");
        foreach (var f in fams)
            EditorIO.Info($"  {f.FamilyName,-18} attack={f.AttackType,-10} color={f.BaseColor,-10} tiers={f.Tiers?.Count ?? 0}");
        EditorIO.Pause();
    }

    private static bool EditFamily(List<MonsterFamilies.MonsterFamily> fams)
    {
        var f = PickFamily(fams, "Family to edit");
        if (f == null) return false;
        f.FamilyName = EditorIO.PromptString("Name", f.FamilyName);
        f.Description = EditorIO.PromptString("Description", f.Description);
        f.BaseColor = EditorIO.PromptChoice("BaseColor", EditorVocab.AnsiColorNames, f.BaseColor);
        f.AttackType = EditorIO.PromptChoice("AttackType", EditorVocab.MonsterAttackTypes, f.AttackType);
        EditorIO.Success("Family updated. Use 'Save' when done.");
        EditorIO.Pause();
        return true;
    }

    private static bool AddFamily(List<MonsterFamilies.MonsterFamily> fams)
    {
        var f = new MonsterFamilies.MonsterFamily { FamilyName = "New Family" };
        f.FamilyName = EditorIO.PromptString("Name", f.FamilyName);
        f.Description = EditorIO.PromptString("Description", f.Description);
        f.BaseColor = EditorIO.PromptChoice("BaseColor", EditorVocab.AnsiColorNames, "white");
        f.AttackType = EditorIO.PromptChoice("AttackType", EditorVocab.MonsterAttackTypes, "physical");
        fams.Add(f);
        EditorIO.Success("Added. Edit tiers via 'Edit tiers of a family' next.");
        EditorIO.Pause();
        return true;
    }

    private static bool DeleteFamily(List<MonsterFamilies.MonsterFamily> fams)
    {
        var f = PickFamily(fams, "Family to delete");
        if (f == null) return false;
        if (!EditorIO.Confirm($"Delete family \"{f.FamilyName}\" and its {f.Tiers?.Count ?? 0} tiers?")) return false;
        fams.Remove(f);
        EditorIO.Success("Deleted.");
        EditorIO.Pause();
        return true;
    }

    /// <summary>Select a family via the shared arrow-key menu instead of exact name typing.</summary>
    private static MonsterFamilies.MonsterFamily? PickFamily(List<MonsterFamilies.MonsterFamily> fams, string title)
    {
        if (fams.Count == 0) { EditorIO.Warn("No families."); EditorIO.Pause(); return null; }
        var labels = fams.Select(f => $"{f.FamilyName,-18} attack={f.AttackType,-10} tiers={f.Tiers?.Count ?? 0}").ToList();
        int pick = EditorIO.Menu(title, labels);
        return pick == 0 ? null : fams[pick - 1];
    }

    private static bool EditFamilyTiers(List<MonsterFamilies.MonsterFamily> fams)
    {
        var f = PickFamily(fams, "Family to edit tiers of");
        if (f == null) return false;
        bool anyChange = false;
        f.Tiers ??= new List<MonsterFamilies.MonsterTier>();
        while (true)
        {
            EditorIO.Section($"{f.FamilyName} tiers ({f.Tiers.Count})");
            for (int i = 0; i < f.Tiers.Count; i++)
            {
                var t = f.Tiers[i];
                EditorIO.Info($"  [{i + 1}] {t.Name,-20} L{t.MinLevel}-{t.MaxLevel}  x{t.PowerMultiplier:F2}  color={t.Color}  abilities={t.SpecialAbilities?.Count ?? 0}");
            }
            int choice = EditorIO.Menu("Tiers:", new[]
            {
                "Add new tier",
                "Edit tier",
                "Delete tier",
                "Add ability to tier",
                "Remove ability from tier",
            });
            switch (choice)
            {
                case 0: return anyChange;
                case 1:
                    {
                        var t = new MonsterFamilies.MonsterTier { Name = "New Tier" };
                        EditTierFields(t);
                        f.Tiers.Add(t);
                        anyChange = true;
                        break;
                    }
                case 2:
                    {
                        if (f.Tiers.Count == 0) { EditorIO.Warn("No tiers."); EditorIO.Pause(); break; }
                        var tierLabels = f.Tiers.Select(t => $"{t.Name,-20} L{t.MinLevel}-{t.MaxLevel}  x{t.PowerMultiplier:F2}").ToList();
                        int pick = EditorIO.Menu("Tier to edit", tierLabels);
                        if (pick == 0) break;
                        EditTierFields(f.Tiers[pick - 1]);
                        anyChange = true;
                        break;
                    }
                case 3:
                    {
                        if (f.Tiers.Count == 0) { EditorIO.Warn("No tiers."); EditorIO.Pause(); break; }
                        var tierLabels = f.Tiers.Select(t => $"{t.Name,-20} L{t.MinLevel}-{t.MaxLevel}").ToList();
                        int pick = EditorIO.Menu("Tier to delete", tierLabels);
                        if (pick == 0) break;
                        if (EditorIO.Confirm($"Delete tier \"{f.Tiers[pick - 1].Name}\"?"))
                        { f.Tiers.RemoveAt(pick - 1); anyChange = true; }
                        break;
                    }
                case 4:
                    {
                        if (f.Tiers.Count == 0) { EditorIO.Warn("No tiers."); EditorIO.Pause(); break; }
                        var tierLabels = f.Tiers.Select(t => $"{t.Name,-20} abilities={t.SpecialAbilities?.Count ?? 0}").ToList();
                        int pick = EditorIO.Menu("Tier to add ability to", tierLabels);
                        if (pick == 0) break;
                        // v0.57.4 (pass 4): picker over the real MonsterAbilities.AbilityType
                        // enum. The old free-text prompt ("poison_bite, power_strike") let
                        // users type anything — typos silently fell through the
                        // Enum.TryParse in CombatEngine.TryMonsterAbility and produced no
                        // effect at runtime. The picker means invalid values are
                        // structurally impossible.
                        var abilityNames = Enum.GetNames<global::MonsterAbilities.AbilityType>()
                            .Where(n => n != "None")
                            .OrderBy(n => n)
                            .ToList();
                        int aPick = EditorIO.Menu("Ability to add", abilityNames);
                        if (aPick == 0) break;
                        string a = abilityNames[aPick - 1];
                        f.Tiers[pick - 1].SpecialAbilities ??= new List<string>();
                        if (!f.Tiers[pick - 1].SpecialAbilities.Contains(a))
                        { f.Tiers[pick - 1].SpecialAbilities.Add(a); anyChange = true; EditorIO.Success($"Added {a}."); }
                        else
                            EditorIO.Info($"{a} already on this tier.");
                        EditorIO.Pause();
                        break;
                    }
                case 5:
                    {
                        if (f.Tiers.Count == 0) { EditorIO.Warn("No tiers."); EditorIO.Pause(); break; }
                        var tierLabels = f.Tiers.Select(t => $"{t.Name,-20} abilities={t.SpecialAbilities?.Count ?? 0}").ToList();
                        int pick = EditorIO.Menu("Tier to remove ability from", tierLabels);
                        if (pick == 0) break;
                        var tier = f.Tiers[pick - 1];
                        if (tier.SpecialAbilities == null || tier.SpecialAbilities.Count == 0)
                        { EditorIO.Warn("No abilities."); EditorIO.Pause(); break; }
                        int aPick = EditorIO.Menu("Ability to remove", tier.SpecialAbilities.ToList());
                        if (aPick == 0) break;
                        tier.SpecialAbilities.RemoveAt(aPick - 1);
                        anyChange = true;
                        break;
                    }
            }
        }
    }

    private static void EditTierFields(MonsterFamilies.MonsterTier t)
    {
        t.Name = EditorIO.PromptString("Tier name", t.Name);
        t.MinLevel = EditorIO.PromptInt("MinLevel", t.MinLevel, min: 1, max: 100);
        t.MaxLevel = EditorIO.PromptInt("MaxLevel", t.MaxLevel, min: t.MinLevel, max: 100);
        t.Color = EditorIO.PromptChoice("Color", EditorVocab.AnsiColorNames, t.Color);
        var ps = EditorIO.PromptString("PowerMultiplier", t.PowerMultiplier.ToString("F2"));
        if (float.TryParse(ps, System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out var p))
            t.PowerMultiplier = p;
    }
}
