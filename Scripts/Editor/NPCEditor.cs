using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using UsurperRemake.Data;
using UsurperRemake.Systems;

namespace UsurperRemake.Editor;

/// <summary>
/// CRUD editor for town NPCs in <c>GameData/npcs.json</c>. If the JSON file
/// does not yet exist, the editor seeds it with the 60 built-in town NPCs
/// so modders start from a known-good baseline rather than an empty list.
/// </summary>
internal static class NPCEditor
{
    public static void Run()
    {
        // Load ONCE before the menu loop. Previous revision re-loaded every
        // iteration, which silently discarded any in-memory edits whenever
        // the user navigated the menu without explicitly picking Save first.
        var npcs = LoadOrSeed();
        bool dirty = false;

        // v0.57.4: in online/MUD mode NPC state lives in the world_state SQLite
        // table and is restored from OnlineStateManager.LoadSharedNPCs at login
        // (see GameEngine.LoadSaveByFileName), which OVERRIDES whatever
        // npcs.json contains. Edits here are only visible in single-player /
        // Steam builds. Warn once at entry so users don't think the editor is
        // broken when their MUD-server NPC edits seem to revert.
        EditorIO.Warn("NPC edits apply to SINGLE-PLAYER / Steam builds only.");
        EditorIO.Info("In online / MUD mode, NPC state lives in the server's world_state database");
        EditorIO.Info("and is restored at login — edits to npcs.json are overridden by the world sim.");
        EditorIO.Pause();

        while (true)
        {
            EditorIO.Section($"NPCs  —  {npcs.Count} total{(dirty ? "  [UNSAVED]" : "")}");
            int choice = EditorIO.Menu("Choose:", new[]
            {
                "List all NPCs",
                "Add new NPC",
                "Edit existing NPC",
                "Delete NPC",
                dirty ? "Save (writes npcs.json)  *UNSAVED CHANGES*" : "Save (writes npcs.json)",
                "Reset to built-in defaults",
                "Reload from disk (discard unsaved changes)",
            });
            switch (choice)
            {
                case 0:
                    if (dirty && !EditorIO.Confirm("Discard unsaved changes and exit?")) continue;
                    return;
                case 1: ListAll(npcs); break;
                case 2: if (AddNpc(npcs)) dirty = true; break;
                case 3: if (EditNpc(npcs)) dirty = true; break;
                case 4: if (DeleteNpc(npcs)) dirty = true; break;
                case 5:
                    if (Save(npcs)) dirty = false;
                    break;
                case 6:
                    if (EditorIO.Confirm("Overwrite npcs.json with built-in defaults?"))
                    {
                        npcs = ClassicNPCs.GetBuiltInNPCs();
                        if (Save(npcs)) dirty = false;
                    }
                    break;
                case 7:
                    if (dirty && !EditorIO.Confirm("Discard unsaved changes?")) break;
                    npcs = LoadOrSeed();
                    dirty = false;
                    EditorIO.Info("Reloaded from disk.");
                    EditorIO.Pause();
                    break;
            }
        }
    }

    private static string GetNpcsJsonPath()
        => Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "GameData", "npcs.json");

    private static List<NPCTemplate> LoadOrSeed()
    {
        var path = GetNpcsJsonPath();
        if (!File.Exists(path)) return ClassicNPCs.GetBuiltInNPCs();
        try
        {
            var json = File.ReadAllText(path);
            return JsonSerializer.Deserialize<List<NPCTemplate>>(json, GameDataLoader.JsonOptions)
                   ?? ClassicNPCs.GetBuiltInNPCs();
        }
        catch (Exception ex)
        {
            EditorIO.Error($"Failed to read {path}: {ex.Message}");
            EditorIO.Info("Falling back to built-in defaults for this session.");
            return ClassicNPCs.GetBuiltInNPCs();
        }
    }

    private static bool Save(List<NPCTemplate> npcs)
    {
        var path = GetNpcsJsonPath();
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(path)!);
            File.WriteAllText(path, JsonSerializer.Serialize(npcs, GameDataLoader.JsonOptions));
            EditorIO.Success($"Wrote {npcs.Count} NPC(s) to {path}");
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

    private static void ListAll(List<NPCTemplate> npcs)
    {
        EditorIO.Section($"NPCs ({npcs.Count})");
        string filter = EditorIO.Prompt("Filter by name (blank for all, 'q' to cancel)");
        if (filter == "q") return;
        var shown = string.IsNullOrWhiteSpace(filter)
            ? npcs
            : npcs.Where(n => n.Name.Contains(filter, StringComparison.OrdinalIgnoreCase)).ToList();
        int page = 0, pageSize = 25;
        while (page * pageSize < shown.Count)
        {
            foreach (var n in shown.Skip(page * pageSize).Take(pageSize))
                EditorIO.Info($"  {n.Name,-28} L{n.StartLevel,-3} {n.Race,-10} {n.Class,-13} {n.Alignment,-7} {n.Personality}");
            page++;
            if (page * pageSize >= shown.Count) break;
            if (!EditorIO.Confirm($"Shown {page * pageSize}/{shown.Count}. More?")) break;
        }
        EditorIO.Pause();
    }

    private static bool AddNpc(List<NPCTemplate> npcs)
    {
        var n = new NPCTemplate { Name = "New NPC" };
        EditFields(n);
        if (string.IsNullOrWhiteSpace(n.Name))
        {
            EditorIO.Warn("Blank name — not added.");
            return false;
        }
        npcs.Add(n);
        EditorIO.Success($"Added \"{n.Name}\". Use 'Save' when done.");
        EditorIO.Pause();
        return true;
    }

    private static bool EditNpc(List<NPCTemplate> npcs)
    {
        if (npcs.Count == 0) { EditorIO.Warn("No NPCs."); EditorIO.Pause(); return false; }
        // Pick from a scrollable list so the user doesn't have to type exact names.
        var labels = npcs.Select(x => $"{x.Name,-28} L{x.StartLevel,-3} {x.Race,-10} {x.Class,-13} {x.Alignment}").ToList();
        int pick = EditorIO.Menu("NPC to edit", labels);
        if (pick == 0) return false;
        EditFields(npcs[pick - 1]);
        EditorIO.Success("NPC updated. Use 'Save' when done.");
        EditorIO.Pause();
        return true;
    }

    private static bool DeleteNpc(List<NPCTemplate> npcs)
    {
        if (npcs.Count == 0) { EditorIO.Warn("No NPCs."); EditorIO.Pause(); return false; }
        var labels = npcs.Select(x => $"{x.Name,-28} L{x.StartLevel,-3} {x.Race,-10} {x.Class,-13} {x.Alignment}").ToList();
        int pick = EditorIO.Menu("NPC to delete", labels);
        if (pick == 0) return false;
        var n = npcs[pick - 1];
        if (!EditorIO.Confirm($"Delete \"{n.Name}\"?")) return false;
        npcs.RemoveAt(pick - 1);
        EditorIO.Success("Deleted. Use 'Save' when done.");
        EditorIO.Pause();
        return true;
    }

    private static void EditFields(NPCTemplate n)
    {
        EditorIO.Info("Press Enter to keep current value.");
        n.Name = EditorIO.PromptString("Name", n.Name);
        n.Class = EditorIO.PromptEnum("Class", n.Class);
        n.Race = EditorIO.PromptEnum("Race", n.Race);
        n.Personality = EditorIO.PromptChoice("Personality", EditorVocab.NpcPersonalities, n.Personality);
        n.Alignment = EditorIO.PromptChoice("Alignment", EditorVocab.Alignments, n.Alignment, allowCustom: false);
        n.StartLevel = EditorIO.PromptInt("StartLevel", n.StartLevel, min: 1, max: 100);
        n.Gender = EditorIO.PromptEnum("Gender", n.Gender);
        n.Orientation = EditorIO.PromptEnum("Orientation", n.Orientation);
        n.IntimateStyle = EditorIO.PromptEnum("IntimateStyle", n.IntimateStyle);
        n.RelationshipPref = EditorIO.PromptEnum("RelationshipPref", n.RelationshipPref);
        // StoryRole is an optional tag that hooks the NPC into the faction system
        // (HighPriest -> Faith, ShadowAgent -> Shadows, FallenPaladin -> Crown, etc.)
        // Pick a known role, "Custom..." for a new tag, or "(none)" for a regular NPC.
        const string noneSentinel = "(none - regular NPC)";
        var storyRoleChoices = new List<string> { noneSentinel };
        storyRoleChoices.AddRange(EditorVocab.StoryRoles);
        var srPick = EditorIO.PromptChoice("StoryRole (links NPC to a faction)", storyRoleChoices, n.StoryRole ?? noneSentinel);
        n.StoryRole = (srPick == noneSentinel || string.IsNullOrWhiteSpace(srPick)) ? null : srPick;

        // LoreNote is free-form narrative flavor describing the NPC's role in the world.
        // Shown in certain dialogue contexts and by some story systems. One or two
        // sentences, player-readable. Leave blank for ordinary citizens.
        n.LoreNote = EditorIO.PromptString("LoreNote (one-line description of this NPC's lore role; blank if generic)", n.LoreNote ?? "");
        if (string.IsNullOrWhiteSpace(n.LoreNote)) n.LoreNote = null;
    }
}
