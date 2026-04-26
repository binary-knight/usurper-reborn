using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Threading.Tasks;
using UsurperRemake.Data;
using UsurperRemake.Systems;

namespace UsurperRemake.Editor;

/// <summary>
/// Comprehensive player-save editor. Loads a save JSON, deserializes to
/// <see cref="SaveGameData"/>, lets the user modify nearly any field
/// through nested category menus, then serializes back to disk.
///
/// This class is deliberately long. Every top-level menu item below has
/// its own region so you can navigate by category; each region owns a
/// small set of methods that edit one concern. All editing happens on
/// the in-memory <see cref="SaveGameData"/> graph — the only I/O is the
/// initial load, the backup-on-save, and the final serialize. No game
/// systems are loaded, no network is opened, and a running game server
/// is unaffected unless the sysop is editing the same save file that
/// server has open (in which case the last writer wins — the intro
/// banner warns about that).
///
/// Design notes:
///   - Every menu choice is non-destructive until the user picks "Save changes".
///   - Before overwriting a save file we copy it to <c>.bak</c> alongside itself.
///   - Power users can still hand-edit the JSON for anything not exposed here —
///     this editor is a convenience over the file, not a replacement for it.
/// </summary>
internal static class PlayerSaveEditor
{
    /// <summary>
    /// JSON options that EXACTLY match the game's <see cref="FileSaveBackend"/>.
    /// Critical: the game serializes with <c>camelCase</c> + <c>IncludeFields</c>,
    /// and deserializes with the same policy (no case-insensitive fallback). If
    /// the editor writes with default PascalCase names, the game re-loads the
    /// file with every field missing — fields default to 0/null — and the next
    /// autosave wipes the edit. v0.57.3 shipped with mismatched options; this
    /// pins them to the same shape so edits survive.
    /// </summary>
    private static readonly JsonSerializerOptions GameSaveJsonOptions = new()
    {
        WriteIndented = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        IncludeFields = true,
    };

    public static async Task RunAsync()
    {
        EditorIO.Section("Player Saves");
        var backend = new FileSaveBackend();
        var saveDir = backend.GetSaveDirectory();
        EditorIO.Info($"Save directory: {saveDir}");

        var saves = backend.GetAllSaves();
        if (saves.Count == 0)
        {
            EditorIO.Warn("No save files found in that directory.");
            EditorIO.Pause();
            return;
        }

        var ordered = saves.OrderByDescending(s => s.SaveTime).ToList();
        var labels = ordered
            .Select(s => $"{s.PlayerName,-24} L{s.Level,-3} {s.ClassName,-12} last saved {s.SaveTime:yyyy-MM-dd HH:mm}")
            .ToList();
        int pick = EditorIO.Menu("Pick a save to edit (most recent first):", labels);
        if (pick == 0) return;

        var chosen = ordered[pick - 1];
        // v0.57.4: trust the on-disk filename reported by FileSaveBackend.GetAllSaves,
        // rather than reconstructing it from PlayerName. Previous code used a different
        // sanitizer than the backend uses (char.IsLetterOrDigit vs Path.GetInvalidFileNameChars),
        // so names containing hyphens / apostrophes silently failed to locate the file.
        var path = !string.IsNullOrEmpty(chosen.FileName)
            ? Path.Combine(saveDir, chosen.FileName)
            : Path.Combine(saveDir, SanitizeFileName(chosen.PlayerName) + ".json");
        if (!File.Exists(path))
        {
            EditorIO.Error($"Expected save file not found: {path}");
            EditorIO.Pause();
            return;
        }

        SaveGameData? data;
        try
        {
            var json = await File.ReadAllTextAsync(path);
            // v0.57.4: use the same JSON options as FileSaveBackend so we read
            // the same fields the game reads (camelCase policy + IncludeFields).
            data = JsonSerializer.Deserialize<SaveGameData>(json, GameSaveJsonOptions);
        }
        catch (Exception ex)
        {
            EditorIO.Error($"Failed to parse save JSON: {ex.Message}");
            EditorIO.Pause();
            return;
        }
        if (data == null || data.Player == null)
        {
            EditorIO.Error("Save file produced null data. Aborting.");
            EditorIO.Pause();
            return;
        }

        // v0.57.4 (pass 3): register dynamic equipment from the save into
        // EquipmentDatabase so ResolveItemName can display names for dungeon
        // loot / enchanted drops (IDs above 10000 but below the custom-mod
        // range). Previously equipped loot showed as "<unknown>" in the
        // editor's equip views even though the save had full stat data for
        // them. Mirror GameEngine.LoadSaveByFileName's single-player path.
        RegisterDynamicEquipmentForEditor(data.Player);

        bool dirty = false;
        while (true)
        {
            var p = data.Player;
            EditorIO.Section($"Editing: {p.Name2} (L{p.Level} {p.Class}, {p.Race}){(dirty ? "  [UNSAVED CHANGES]" : "")}");
            int choice = EditorIO.Menu("Choose category:", new[]
            {
                "Character Info           — name, class, race, alignment, fame, knighthood",
                "Stats & Progression      — level, XP, core stats, HP/Mana caps, resurrections",
                "Gold & Economy           — gold, bank, loan, team wages",
                "Inventory & Equipment    — items, equipped slots, curses, potions",
                "Spells & Abilities       — learned spells, class abilities, quickbar",
                "Companions               — recruit, revive, loyalty, romance",
                "Quests                   — active, complete, reset, grant",
                "Achievements             — disabled (Steam integrity)",
                "Old Gods & Story         — god states, cycle, seals, artifacts",
                "Relationships & Family   — NPC relationships, marriages, children",
                "Status & Cleanup         — diseases, divine wrath, daily limits, poison",
                "Appearance & Flavor      — height, weight, eyes/hair/skin, phrases, description",
                "Skills & Training        — proficiencies, stat training counts, crafting materials",
                "Team / Guild / Factions  — team info, guild, faction standings",
                "Settings & Preferences   — auto-heal, combat speed, color theme, language",
                "World State              — current king, bank interest, town pot, economy",
                "Show full summary",
                dirty ? "SAVE CHANGES to disk" : "(no changes yet)",
                "Discard changes and exit",
            });
            if (choice == 0 || choice == 19)
            {
                if (dirty && !EditorIO.Confirm("Discard unsaved changes?")) continue;
                return;
            }
            try
            {
                switch (choice)
                {
                    case 1: EditCharacterInfo(p); dirty = true; break;
                    case 2: EditStats(p); dirty = true; break;
                    case 3: EditGold(p); dirty = true; break;
                    case 4: EditInventoryAndEquipment(p); dirty = true; break;
                    case 5: EditSpellsAndAbilities(p); dirty = true; break;
                    case 6: EditCompanions(data); dirty = true; break;
                    case 7: EditQuests(p); dirty = true; break;
                    case 8:
                        // v0.57.4: achievement editing removed — granting an achievement
                        // via the editor would also unlock the corresponding Steam
                        // achievement at runtime, which is straight-up cheating on the
                        // Steam leaderboards. Hard-disabled on every platform for
                        // consistency; players who want to grind can still grind.
                        EditorIO.Warn("Achievement editing is disabled.");
                        EditorIO.Info("Granting achievements via the editor would also unlock Steam achievements,");
                        EditorIO.Info("which would be cheating on leaderboards. To earn achievements, play the game.");
                        EditorIO.Pause();
                        break;
                    case 9: EditStoryAndGods(data); dirty = true; break;
                    case 10: EditRelationshipsAndFamily(data); dirty = true; break;
                    case 11: EditStatusAndCleanup(p); dirty = true; break;
                    case 12: EditAppearance(p); dirty = true; break;
                    case 13: EditSkillsAndTraining(p); dirty = true; break;
                    case 14: EditTeamAndGuild(p); dirty = true; break;
                    case 15: EditSettings(p); dirty = true; break;
                    case 16: EditWorldState(data); dirty = true; break;
                    case 17: ShowSummary(p); break;
                    case 18:
                        if (!dirty) { EditorIO.Info("Nothing to save."); EditorIO.Pause(); break; }
                        if (SaveBack(path, data)) dirty = false;
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

    // v0.57.4 (pass 4): mirror FileSaveBackend.GetSaveFileName's sanitizer
    // EXACTLY — Path.GetInvalidFileNameChars, not char.IsLetterOrDigit. The
    // old implementation replaced hyphens / apostrophes / periods with
    // underscores; the backend keeps those chars. This is the fallback path
    // when SaveInfo.FileName isn't available; the main path already uses
    // the backend's on-disk filename directly (see RunAsync).
    private static string SanitizeFileName(string name)
        => string.Join("_", name.Split(Path.GetInvalidFileNameChars()));

    private static bool SaveBack(string path, SaveGameData data)
    {
        try
        {
            var backupPath = path + ".bak";
            File.Copy(path, backupPath, overwrite: true);
            // v0.57.4: must match the game's FileSaveBackend options (camelCase +
            // IncludeFields), or the game won't see the edited fields when it reloads.
            var json = JsonSerializer.Serialize(data, GameSaveJsonOptions);
            File.WriteAllText(path, json);
            EditorIO.Success($"Saved. Backup at: {backupPath}");
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

    #region Character Info

    private static void EditCharacterInfo(PlayerData p)
    {
        EditorIO.Section("Character Info");
        p.Name2 = EditorIO.PromptString("Display name", p.Name2);
        p.Name1 = EditorIO.PromptString("Internal name (rarely used — match display name if unsure)", p.Name1);
        p.RealName = EditorIO.PromptString("Real name (narrative, can be blank)", p.RealName);

        EditorIO.Info("Class and race are USUALLY risky to change — stats tied to class-per-level won't re-apply.");
        EditorIO.Info("Change only if you know what you're doing or are willing to tweak stats manually after.");
        if (EditorIO.Confirm("Change class?"))
            p.Class = EditorIO.PromptEnum("Class", p.Class);
        if (EditorIO.Confirm("Change race?"))
            p.Race = EditorIO.PromptEnum("Race", p.Race);

        var sexChoice = EditorIO.PromptChoice("Sex", new[] { "M", "F" }, p.Sex.ToString(), allowCustom: false);
        if (!string.IsNullOrEmpty(sexChoice)) p.Sex = sexChoice[0];
        p.Age = EditorIO.PromptInt("Age", p.Age, min: 1, max: 2000);

        EditorIO.Info("— Alignment —");
        // v0.57.4 (pass 4): actually enforce the 0-1000 cap. Game's
        // AlignmentSystem.ChangeAlignment clamps to this range on every
        // modification, but a raw file edit lets values above 1000 into the
        // save. They load fine but clamp back to 1000 the first time the
        // value is changed in-game. Write the cap at the boundary so the
        // editor value matches what the game will end up with.
        p.Chivalry = EditorIO.PromptLong("Chivalry (0-1000)", p.Chivalry, min: 0, max: 1000);
        p.Darkness = EditorIO.PromptLong("Darkness (0-1000)", p.Darkness, min: 0, max: 1000);

        EditorIO.Info("— Social standing —");
        p.Fame = EditorIO.PromptInt("Fame", p.Fame, min: 0);
        p.IsKnighted = EditorIO.PromptBool("Knighted (Sir/Dame prefix)", p.IsKnighted);
        p.NobleTitle = EditorIO.PromptString("Noble title (blank = auto)", p.NobleTitle ?? "");
        if (string.IsNullOrWhiteSpace(p.NobleTitle)) p.NobleTitle = null;
        // v0.57.4: King flag is authoritative in online mode via world_state's
        // royal_court entry. Editing here affects the single-player save's
        // notion of kingship only; on a server the throne is whatever the
        // shared state says. Immortal ascension is similar — the online
        // pantheon tracks immortals in a separate data store; flipping this
        // bit won't register a local character with the server pantheon.
        EditorIO.Info("(King / Immortal flags only apply to the single-player save being edited.)");
        p.King = EditorIO.PromptBool("Is the current king?", p.King);
        p.Immortal = EditorIO.PromptBool("Immortal (ascended, pantheon)", p.Immortal);

        EditorIO.Info("— Difficulty —");
        p.Difficulty = EditorIO.PromptEnum("Difficulty", p.Difficulty);
    }

    #endregion

    #region Stats & Progression

    private static void EditStats(PlayerData p)
    {
        EditorIO.Section("Stats & Progression");
        EditorIO.Warn("Changing level doesn't retroactively grant per-class stat gains.");
        p.Level = EditorIO.PromptInt("Level", p.Level, min: 1, max: 100);
        p.Experience = EditorIO.PromptLong("Experience", p.Experience, min: 0);

        // v0.57.4 — the editor MUST write to Base* fields, not the derived
        // Strength/Dexterity/MaxHP/etc. The game's load path calls
        // Character.RecalculateStats() after deserializing, which resets each
        // derived stat to its Base* counterpart and then layers equipment
        // bonuses on top. Earlier v0.57.4 edited the derived fields, which
        // were then wiped on load — player saw their edits disappear. Writing
        // Base* (and mirroring to the derived field so in-editor summary /
        // preview makes sense) makes the edits survive.
        EditorIO.Info("— Core stats (base values; equipment bonuses apply on top at runtime) —");
        p.BaseStrength = EditorIO.PromptLong("STR", p.BaseStrength > 0 ? p.BaseStrength : p.Strength, min: 0);
        p.Strength = p.BaseStrength;
        p.BaseDexterity = EditorIO.PromptLong("DEX", p.BaseDexterity > 0 ? p.BaseDexterity : p.Dexterity, min: 0);
        p.Dexterity = p.BaseDexterity;
        p.BaseConstitution = EditorIO.PromptLong("CON", p.BaseConstitution > 0 ? p.BaseConstitution : p.Constitution, min: 0);
        p.Constitution = p.BaseConstitution;
        p.BaseIntelligence = EditorIO.PromptLong("INT", p.BaseIntelligence > 0 ? p.BaseIntelligence : p.Intelligence, min: 0);
        p.Intelligence = p.BaseIntelligence;
        p.BaseWisdom = EditorIO.PromptLong("WIS", p.BaseWisdom > 0 ? p.BaseWisdom : p.Wisdom, min: 0);
        p.Wisdom = p.BaseWisdom;
        p.BaseCharisma = EditorIO.PromptLong("CHA", p.BaseCharisma > 0 ? p.BaseCharisma : p.Charisma, min: 0);
        p.Charisma = p.BaseCharisma;
        p.BaseDefence = EditorIO.PromptLong("DEF", p.BaseDefence > 0 ? p.BaseDefence : p.Defence, min: 0);
        p.Defence = p.BaseDefence;
        p.BaseAgility = EditorIO.PromptLong("AGI", p.BaseAgility > 0 ? p.BaseAgility : p.Agility, min: 0);
        p.Agility = p.BaseAgility;
        p.BaseStamina = EditorIO.PromptLong("STA", p.BaseStamina > 0 ? p.BaseStamina : p.Stamina, min: 0);
        p.Stamina = p.BaseStamina;

        EditorIO.Info("— HP / Mana (base; CON bonus adds on top at runtime) —");
        p.BaseMaxHP = EditorIO.PromptLong("MaxHP (base)", p.BaseMaxHP > 0 ? p.BaseMaxHP : p.MaxHP, min: 1);
        p.MaxHP = p.BaseMaxHP;
        p.HP = EditorIO.PromptLong("HP (current; will be clamped to final MaxHP on load)", p.HP, min: 0);
        p.BaseMaxMana = EditorIO.PromptLong("MaxMana (base)", p.BaseMaxMana > 0 ? p.BaseMaxMana : p.MaxMana, min: 0);
        p.MaxMana = p.BaseMaxMana;
        p.Mana = EditorIO.PromptLong("Mana (current; clamped on load)", p.Mana, min: 0);

        EditorIO.Info("— Combat power —");
        EditorIO.Warn("WeapPow/ArmPow are derived from equipped items at runtime — editing them directly has no effect.");
        EditorIO.Warn("To change combat power, edit your equipment (Inventory & Equipment menu) or give yourself better gear.");

        EditorIO.Info("— Resurrections —");
        p.Resurrections = EditorIO.PromptInt("Resurrections available", p.Resurrections, min: 0);
        p.MaxResurrections = EditorIO.PromptInt("Max resurrections", p.MaxResurrections, min: 0);
        p.ResurrectionsUsed = EditorIO.PromptInt("Resurrections used (lifetime)", p.ResurrectionsUsed, min: 0);

        EditorIO.Info("— Training —");
        p.Trains = EditorIO.PromptInt("Unspent training sessions", p.Trains, min: 0);
        p.TrainingPoints = EditorIO.PromptInt("Training points", p.TrainingPoints, min: 0);
    }

    #endregion

    #region Gold & Economy

    private static void EditGold(PlayerData p)
    {
        EditorIO.Section("Gold & Economy");
        // v0.57.4 (pass 3): min: 0 on every field. Gold is `long` so nothing
        // stops negative values, but the game's shops / bank / loan checks
        // don't handle them gracefully (negative gold fails affordability
        // checks, negative bank loan produces weird interest, etc.). No
        // legitimate reason to enter a negative value here.
        p.Gold = EditorIO.PromptLong("Gold on hand", p.Gold, min: 0);
        p.BankGold = EditorIO.PromptLong("Bank gold", p.BankGold, min: 0);
        p.BankLoan = EditorIO.PromptLong("Bank loan owed", p.BankLoan, min: 0);
        p.BankInterest = EditorIO.PromptLong("Bank interest earned", p.BankInterest, min: 0);
        p.BankWage = EditorIO.PromptLong("Bank wage", p.BankWage, min: 0);
        p.RoyalLoanAmount = EditorIO.PromptLong("Royal loan amount", p.RoyalLoanAmount, min: 0);
        p.RoyTaxPaid = EditorIO.PromptLong("Royal tax paid (lifetime)", p.RoyTaxPaid, min: 0);
    }

    #endregion

    #region Inventory & Equipment

    private static void EditInventoryAndEquipment(PlayerData p)
    {
        while (true)
        {
            int choice = EditorIO.Menu("Inventory & Equipment", new[]
            {
                $"View inventory ({p.Inventory?.Count ?? 0} items)",
                "Add item to inventory (by ID)",
                "Remove item from inventory",
                "Clear entire inventory",
                $"View equipped slots ({p.EquippedItems?.Count ?? 0} equipped)",
                "Equip item in slot (by ID)",
                "Unequip a slot",
                "Clear all equipped items",
                "Uncurse equipped items (weapon/armor/shield flags)",
                $"Potions (heal: {p.Healing}, mana: {p.ManaPotions}, antidote: {p.Antidotes})",
            });
            if (choice == 0) return;
            switch (choice)
            {
                case 1: ListInventory(p); break;
                case 2: AddInventoryItem(p); break;
                case 3: RemoveInventoryItem(p); break;
                case 4:
                    if (EditorIO.Confirm("Clear ALL inventory items?"))
                    { p.Inventory?.Clear(); EditorIO.Success("Inventory cleared."); EditorIO.Pause(); }
                    break;
                case 5: ListEquipped(p); break;
                case 6: EquipItemInSlot(p); break;
                case 7: UnequipSlot(p); break;
                case 8:
                    if (EditorIO.Confirm("Unequip every slot?"))
                    { p.EquippedItems?.Clear(); EditorIO.Success("All slots empty."); EditorIO.Pause(); }
                    break;
                case 9:
                    p.WeaponCursed = false; p.ArmorCursed = false; p.ShieldCursed = false;
                    EditorIO.Success("Curse flags cleared on weapon/armor/shield.");
                    EditorIO.Pause();
                    break;
                case 10: EditPotions(p); break;
            }
        }
    }

    private static void ListInventory(PlayerData p)
    {
        EditorIO.Section($"Inventory ({p.Inventory?.Count ?? 0})");
        if (p.Inventory == null || p.Inventory.Count == 0)
        {
            EditorIO.Info("  (empty)");
            EditorIO.Pause();
            return;
        }
        // Inventory stores full item copies (legacy Pascal Item model), not
        // references to EquipmentDatabase IDs. Show the fields users actually
        // care about.
        for (int i = 0; i < p.Inventory.Count && i < 200; i++)
        {
            var inv = p.Inventory[i];
            EditorIO.Info($"  [{i + 1,3}] {inv.Name,-38} type={inv.Type,-10} val={inv.Value,-7} cursed={inv.IsCursed} identified={inv.IsIdentified}");
        }
        if (p.Inventory.Count > 200) EditorIO.Info($"  ...and {p.Inventory.Count - 200} more (use the JSON for full audit).");
        EditorIO.Pause();
    }

    private static void AddInventoryItem(PlayerData p)
    {
        // The inventory uses the legacy Pascal Item model — each entry is a full
        // copy of an item's stats, not a reference to EquipmentDatabase. So
        // "add by ID" here means: look the ID up in EquipmentDatabase, copy its
        // stats into a new InventoryItemData. The resulting inventory entry is
        // a standalone stat block that doesn't depend on the database existing
        // on load, which also means deleting a mod won't orphan the save.
        //
        // v0.57.4: replaced raw-ID prompt with an arrow-key picker. The old UX
        // required users to know an internal integer ID, which nobody does.
        // Slot filter narrows huge catalogs (hundreds of items) into a picker
        // that fits in a viewport.
        var all = EquipmentDatabase.GetAll().OrderBy(e => e.Slot).ThenBy(e => e.Id).ToList();
        if (all.Count == 0) { EditorIO.Warn("No equipment registered."); EditorIO.Pause(); return; }

        // Optional slot filter
        var slotOptions = new List<string> { "(any slot)" };
        slotOptions.AddRange(Enum.GetNames<EquipmentSlot>());
        int slotPick = EditorIO.Menu("Filter by slot (speeds up search on big catalogs)", slotOptions);
        if (slotPick == 0) return;
        IEnumerable<Equipment> filtered = all;
        if (slotPick > 1 && Enum.TryParse<EquipmentSlot>(slotOptions[slotPick - 1], out var slot))
            filtered = filtered.Where(e => e.Slot == slot);

        // Optional name substring filter — critical when the slot has hundreds
        // of items (e.g. MainHand). Blank = show all.
        string nameFilter = EditorIO.Prompt("Filter by name (blank = all, 'q' to cancel)");
        if (nameFilter == "q") return;
        if (!string.IsNullOrWhiteSpace(nameFilter))
            filtered = filtered.Where(e => e.Name.Contains(nameFilter, StringComparison.OrdinalIgnoreCase));

        var shown = filtered.ToList();
        if (shown.Count == 0) { EditorIO.Warn("No matches."); EditorIO.Pause(); return; }
        if (shown.Count > 400) { EditorIO.Warn($"{shown.Count} matches — tighten the filter."); EditorIO.Pause(); return; }

        var labels = shown.Select(e =>
            $"#{e.Id,-7} [{e.Slot,-10}] [{e.Rarity,-8}] {e.Name,-38} MinL{e.MinLevel,-3} {e.Value,8:N0}g"
        ).ToList();
        int pick = EditorIO.Menu("Item to copy into inventory", labels);
        if (pick == 0) return;
        var eq = shown[pick - 1];

        p.Inventory ??= new List<InventoryItemData>();
        p.Inventory.Add(new InventoryItemData
        {
            Name = eq.Name,
            Value = eq.Value,
            Attack = eq.WeaponPower,
            Armor = eq.ArmorClass,
            Strength = eq.StrengthBonus,
            Dexterity = eq.DexterityBonus,
            Wisdom = eq.WisdomBonus,
            Defence = eq.DefenceBonus,
            BlockChance = eq.BlockChance,
            ShieldBonus = eq.ShieldBonus,
            HP = eq.MaxHPBonus,
            Mana = eq.MaxManaBonus,
            Charisma = eq.CharismaBonus,
            Agility = eq.AgilityBonus,
            Stamina = eq.StaminaBonus,
            MinLevel = eq.MinLevel,
            IsCursed = eq.IsCursed,
            IsIdentified = true,
        });
        EditorIO.Success($"Added \"{eq.Name}\" to inventory.");
        EditorIO.Pause();
    }

    private static void RemoveInventoryItem(PlayerData p)
    {
        if (p.Inventory == null || p.Inventory.Count == 0)
        { EditorIO.Warn("Inventory is empty."); EditorIO.Pause(); return; }
        // v0.57.4: picker over inventory contents instead of "enter the index
        // number" — the picker scrolls with the viewport so long inventories
        // stay navigable and users can't off-by-one themselves.
        var labels = p.Inventory.Select((inv, i) =>
            $"{inv.Name,-38} type={inv.Type,-10} val={inv.Value,-7}{(inv.IsCursed ? " [cursed]" : "")}{(inv.IsIdentified ? "" : " [unid]")}"
        ).ToList();
        int pick = EditorIO.Menu("Item to remove", labels);
        if (pick == 0) return;
        var match = p.Inventory[pick - 1];
        p.Inventory.RemoveAt(pick - 1);
        EditorIO.Success($"Removed \"{match.Name}\".");
        EditorIO.Pause();
    }

    private static void ListEquipped(PlayerData p)
    {
        EditorIO.Section($"Equipped slots ({p.EquippedItems?.Count ?? 0})");
        if (p.EquippedItems == null || p.EquippedItems.Count == 0)
        { EditorIO.Info("  (nothing equipped)"); EditorIO.Pause(); return; }
        foreach (var kv in p.EquippedItems)
        {
            string slotName = ((EquipmentSlot)kv.Key).ToString();
            string itemName = ResolveItemName(kv.Value) ?? "<unknown>";
            EditorIO.Info($"  {slotName,-12} ID:{kv.Value,-7} {itemName}");
        }
        EditorIO.Info($"  WeaponCursed={p.WeaponCursed} ArmorCursed={p.ArmorCursed} ShieldCursed={p.ShieldCursed}");
        EditorIO.Pause();
    }

    private static void EquipItemInSlot(PlayerData p)
    {
        // v0.57.4: picker-based — list every item registered for the chosen
        // slot. Raw ID prompt was broken UX (nobody knows IDs) and allowed
        // entering non-existent IDs that load as "ghost" equips (RecalculateStats
        // silently skips them, leaving the slot visually filled but contributing
        // nothing).
        var slot = EditorIO.PromptEnum<EquipmentSlot>("Slot to equip", EquipmentSlot.MainHand);
        var candidates = EquipmentDatabase.GetAll()
            .Where(e => e.Slot == slot)
            .OrderBy(e => e.MinLevel)
            .ThenBy(e => e.Id)
            .ToList();
        if (candidates.Count == 0)
        {
            EditorIO.Warn($"No equipment registered for slot {slot}.");
            EditorIO.Pause();
            return;
        }

        string nameFilter = EditorIO.Prompt("Filter by name (blank = all, 'q' to cancel)");
        if (nameFilter == "q") return;
        var shown = string.IsNullOrWhiteSpace(nameFilter)
            ? candidates
            : candidates.Where(e => e.Name.Contains(nameFilter, StringComparison.OrdinalIgnoreCase)).ToList();
        if (shown.Count == 0) { EditorIO.Warn("No matches."); EditorIO.Pause(); return; }
        if (shown.Count > 400) { EditorIO.Warn($"{shown.Count} matches — tighten the filter."); EditorIO.Pause(); return; }

        var labels = shown.Select(e =>
            $"#{e.Id,-7} [{e.Rarity,-8}] {e.Name,-38} MinL{e.MinLevel,-3} Pow={e.WeaponPower}/{e.ArmorClass} {e.Value,8:N0}g"
        ).ToList();
        int pick = EditorIO.Menu($"Item to equip in {slot}", labels);
        if (pick == 0) return;
        var eq = shown[pick - 1];
        p.EquippedItems ??= new Dictionary<int, int>();
        p.EquippedItems[(int)slot] = eq.Id;
        EditorIO.Success($"{slot} = #{eq.Id} ({eq.Name})");
        // Class/level restrictions are enforced at runtime (Character.CanEquip),
        // not at load — so equipping out-of-class gear via editor writes fine
        // but the game will block actual use (e.g. Warrior can't swing a Staff's
        // magic bonuses). Let the user know rather than silently "allowing" it.
        if (eq.MinLevel > p.Level)
            EditorIO.Warn($"Warning: {eq.Name} has MinLevel={eq.MinLevel}, player is L{p.Level}. Item works on load but gameplay may restrict.");
        EditorIO.Pause();
    }

    private static void UnequipSlot(PlayerData p)
    {
        if (p.EquippedItems == null || p.EquippedItems.Count == 0)
        { EditorIO.Warn("Nothing equipped."); EditorIO.Pause(); return; }
        // v0.57.4: picker over only the slots that are actually filled, so the
        // user never picks an already-empty slot and gets a "slot was empty"
        // warning. Also shows the item name for context.
        var filled = p.EquippedItems.Where(kv => kv.Value > 0).ToList();
        var labels = filled.Select(kv =>
            $"{(EquipmentSlot)kv.Key,-12} #{kv.Value,-7} {ResolveItemName(kv.Value) ?? "<unknown>"}"
        ).ToList();
        int pick = EditorIO.Menu("Slot to clear", labels);
        if (pick == 0) return;
        int slotKey = filled[pick - 1].Key;
        p.EquippedItems.Remove(slotKey);
        EditorIO.Success($"{(EquipmentSlot)slotKey} cleared.");
        EditorIO.Pause();
    }

    private static void EditPotions(PlayerData p)
    {
        p.Healing = EditorIO.PromptLong("Healing potions", p.Healing, min: 0);
        p.ManaPotions = EditorIO.PromptLong("Mana potions", p.ManaPotions, min: 0);
        p.Antidotes = EditorIO.PromptInt("Antidotes", p.Antidotes, min: 0);
    }

    private static string? ResolveItemName(int id)
    {
        var eq = EquipmentDatabase.GetById(id);
        return eq?.Name;
    }

    /// <summary>
    /// Register saved dynamic equipment (dungeon loot drops, IDs 10000+) into
    /// <see cref="EquipmentDatabase"/> so the editor's equip displays can
    /// resolve names. Mirrors the single-player path in
    /// <see cref="GameEngine.LoadSaveByFileName"/>. Without this, equipped
    /// loot IDs show as &lt;unknown&gt; because <c>EquipmentDatabase</c> only
    /// contains built-ins plus custom-mod items at editor start.
    /// </summary>
    private static void RegisterDynamicEquipmentForEditor(PlayerData playerData)
    {
        if (playerData.DynamicEquipment == null || playerData.DynamicEquipment.Count == 0) return;
        foreach (var equipData in playerData.DynamicEquipment)
        {
            try
            {
                // Skip if already registered (shouldn't happen but defensive)
                if (EquipmentDatabase.GetById(equipData.Id) != null) continue;

                var equipment = new Equipment
                {
                    Name = equipData.Name,
                    Description = equipData.Description ?? "",
                    Slot = (EquipmentSlot)equipData.Slot,
                    WeaponPower = equipData.WeaponPower,
                    ArmorClass = equipData.ArmorClass,
                    ShieldBonus = equipData.ShieldBonus,
                    BlockChance = equipData.BlockChance,
                    StrengthBonus = equipData.StrengthBonus,
                    DexterityBonus = equipData.DexterityBonus,
                    ConstitutionBonus = equipData.ConstitutionBonus,
                    IntelligenceBonus = equipData.IntelligenceBonus,
                    WisdomBonus = equipData.WisdomBonus,
                    CharismaBonus = equipData.CharismaBonus,
                    MaxHPBonus = equipData.MaxHPBonus,
                    MaxManaBonus = equipData.MaxManaBonus,
                    DefenceBonus = equipData.DefenceBonus,
                    MinLevel = equipData.MinLevel,
                    Value = equipData.Value,
                    IsCursed = equipData.IsCursed,
                    Rarity = (EquipmentRarity)equipData.Rarity,
                    WeaponType = (WeaponType)equipData.WeaponType,
                    Handedness = (WeaponHandedness)equipData.Handedness,
                    ArmorType = (ArmorType)equipData.ArmorType,
                    IsIdentified = equipData.IsIdentified,
                };
                EquipmentDatabase.RegisterDynamicWithId(equipment, equipData.Id);
            }
            catch (Exception ex)
            {
                // Silent — one bad entry shouldn't block editing the rest.
                EditorIO.Warn($"Skipped dynamic equipment #{equipData.Id}: {ex.Message}");
            }
        }
    }

    #endregion

    #region Spells & Abilities

    private static void EditSpellsAndAbilities(PlayerData p)
    {
        while (true)
        {
            int choice = EditorIO.Menu("Spells & Abilities", new[]
            {
                $"View learned spells ({p.Spells?.Count(s => s != null && s.Count > 0 && s[0]) ?? 0} known)",
                "Learn ALL spells (known, not mastered)",
                "Master ALL spells",
                "Clear all spells",
                $"View learned abilities ({p.LearnedAbilities?.Count ?? 0})",
                "Add ability by ID",
                "Remove ability",
                "Clear all abilities",
                $"View quickbar ({p.Quickbar?.Count ?? 0} slots)",
                "Clear quickbar",
            });
            if (choice == 0) return;
            switch (choice)
            {
                case 1:
                    ListSpells(p); break;
                case 2:
                    GrantAllSpells(p, mastered: false); break;
                case 3:
                    GrantAllSpells(p, mastered: true); break;
                case 4:
                    if (EditorIO.Confirm("Forget ALL spells?")) { p.Spells?.Clear(); EditorIO.Success("Cleared."); EditorIO.Pause(); }
                    break;
                case 5:
                    if (p.LearnedAbilities == null || p.LearnedAbilities.Count == 0)
                        EditorIO.Info("(none)");
                    else
                        foreach (var a in p.LearnedAbilities) EditorIO.Info($"  {a}");
                    EditorIO.Pause();
                    break;
                case 6:
                    {
                        // Pick from the live ClassAbilitySystem registry so the user sees
                        // ability names (not raw IDs) and can't grant something that
                        // doesn't exist.
                        var all = ClassAbilitySystem.GetAllAbilities();
                        var labels = all.Select(x => $"{x.Id,-24} ({x.Name}, L{x.LevelRequired})").ToList();
                        int pick = EditorIO.Menu("Ability to grant", labels);
                        if (pick == 0) break;
                        var chosen = all[pick - 1];
                        p.LearnedAbilities ??= new List<string>();
                        if (!p.LearnedAbilities.Contains(chosen.Id))
                        { p.LearnedAbilities.Add(chosen.Id); EditorIO.Success($"Granted {chosen.Name}."); }
                        else
                            EditorIO.Info($"{chosen.Name} already known.");
                        EditorIO.Pause();
                        break;
                    }
                case 7:
                    {
                        if (p.LearnedAbilities == null || p.LearnedAbilities.Count == 0)
                        { EditorIO.Warn("No abilities to remove."); EditorIO.Pause(); break; }
                        int pick = EditorIO.Menu("Ability to remove", p.LearnedAbilities.ToList());
                        if (pick == 0) break;
                        var removed = p.LearnedAbilities[pick - 1];
                        p.LearnedAbilities.RemoveAt(pick - 1);
                        EditorIO.Success($"Removed {removed}.");
                        EditorIO.Pause();
                        break;
                    }
                case 8:
                    if (EditorIO.Confirm("Clear all abilities?"))
                    { p.LearnedAbilities?.Clear(); EditorIO.Success("Cleared."); EditorIO.Pause(); }
                    break;
                case 9:
                    if (p.Quickbar == null || p.Quickbar.Count == 0)
                        EditorIO.Info("(empty)");
                    else
                        for (int i = 0; i < p.Quickbar.Count; i++) EditorIO.Info($"  [{i + 1}] {p.Quickbar[i] ?? "(empty)"}");
                    EditorIO.Pause();
                    break;
                case 10:
                    if (EditorIO.Confirm("Clear quickbar?")) { p.Quickbar?.Clear(); EditorIO.Success("Cleared."); EditorIO.Pause(); }
                    break;
            }
        }
    }

    private static void ListSpells(PlayerData p)
    {
        if (p.Spells == null || p.Spells.Count == 0) { EditorIO.Info("(no spell data)"); EditorIO.Pause(); return; }
        int known = 0, mastered = 0;
        for (int i = 0; i < p.Spells.Count; i++)
        {
            var row = p.Spells[i];
            if (row == null || row.Count == 0) continue;
            if (row[0]) { known++; EditorIO.Info($"  Spell[{i}] known{(row.Count > 1 && row[1] ? ", mastered" : "")}"); }
            if (row.Count > 1 && row[1]) mastered++;
        }
        EditorIO.Info($"  Total known: {known}, mastered: {mastered}");
        EditorIO.Pause();
    }

    private static void GrantAllSpells(PlayerData p, bool mastered)
    {
        // v0.57.4: warn for non-caster classes. Spell matrix is per-level and
        // RecalculateStats zeros MaxMana for IsManaClass == false, so a Warrior
        // / Barbarian / Ranger / Jester granted "all spells" gets the matrix
        // written but can never cast anything. This used to look like a bug to
        // users ("I granted all spells but my Warrior can't cast!"); it's
        // working as designed — the class just has no mana pool. Warn so the
        // user understands the edit is effectively a no-op for them.
        if (!IsCasterClass(p.Class))
        {
            EditorIO.Warn($"{p.Class} is not a caster class. Spells require a mana pool; granted spells won't be castable.");
            EditorIO.Info("Caster classes: Cleric, Magician, Sage, MysticShaman, and all 5 prestige classes.");
            EditorIO.Info("(Paladin, Bard, Alchemist use stamina, not mana — their class abilities live under 'Abilities' instead.)");
            if (!EditorIO.Confirm("Write the spell entries anyway?")) return;
        }
        // Fill spell matrix with [known=true, mastered=mastered] for a reasonable spell count.
        // Actual spell count is class-dependent and not trivially discoverable from save data,
        // so we set a healthy ceiling of 60 which covers every current caster class's spell list.
        p.Spells ??= new List<List<bool>>();
        while (p.Spells.Count < 60) p.Spells.Add(new List<bool> { false, false });
        for (int i = 0; i < p.Spells.Count; i++)
        {
            var row = p.Spells[i] ?? new List<bool>();
            while (row.Count < 2) row.Add(false);
            row[0] = true;
            row[1] = mastered;
            p.Spells[i] = row;
        }
        EditorIO.Success($"All spells {(mastered ? "mastered" : "learned")}.");
        EditorIO.Pause();
    }

    /// <summary>
    /// Classes that have a mana pool and can cast spells. Matches
    /// <see cref="UsurperRemake.Character.IsManaClass"/>'s list. Used by the
    /// spell-grant flow to warn when the edit will be a no-op.
    /// </summary>
    // Mirrors Character.IsManaClass (see Scripts/Core/Character.cs). Paladin /
    // Bard / Alchemist were moved to stamina in v0.49.5 and are deliberately
    // excluded — their class toolkits live under LearnedAbilities, not Spells.
    private static bool IsCasterClass(CharacterClass c) =>
        c == CharacterClass.Cleric ||
        c == CharacterClass.Magician ||
        c == CharacterClass.Sage ||
        c == CharacterClass.MysticShaman ||
        c == CharacterClass.Tidesworn ||
        c == CharacterClass.Wavecaller ||
        c == CharacterClass.Cyclebreaker ||
        c == CharacterClass.Abysswarden ||
        c == CharacterClass.Voidreaver;

    #endregion

    #region Companions

    private static void EditCompanions(SaveGameData data)
    {
        data.StorySystems ??= new StorySystemsData();
        data.StorySystems.Companions ??= new List<CompanionSaveInfo>();
        data.StorySystems.ActiveCompanionIds ??= new List<int>();
        data.StorySystems.FallenCompanions ??= new List<CompanionDeathInfo>();
        while (true)
        {
            int choice = EditorIO.Menu("Companions", new[]
            {
                $"List companions ({data.StorySystems.Companions.Count})",
                "Revive a fallen companion",
                "Set loyalty / trust / romance level",
                "Recruit a companion by ID",
                "Dismiss (un-recruit) a companion",
                "Restore full HP + potions on all active companions (not directly, but set IsDead=false)",
            });
            if (choice == 0) return;
            switch (choice)
            {
                case 1:
                    foreach (var c in data.StorySystems.Companions)
                        EditorIO.Info($"  ID:{c.Id}  recruited:{c.IsRecruited} active:{c.IsActive} dead:{c.IsDead}  L{c.Level}  loyalty:{c.LoyaltyLevel} trust:{c.TrustLevel} romance:{c.RomanceLevel}");
                    EditorIO.Pause();
                    break;
                case 2:
                    ReviveCompanion(data);
                    break;
                case 3:
                    SetCompanionRelationship(data.StorySystems.Companions);
                    break;
                case 4:
                    {
                        var picked = EditorIO.PromptEnum("Companion to recruit", UsurperRemake.Systems.CompanionId.Aldric);
                        int id = (int)picked;
                        // v0.57.4 (pass 3): the editor must update BOTH the per-companion
                        // IsActive flag AND the top-level ActiveCompanionIds list —
                        // CompanionSystem.GetActiveCompanions() iterates the top-level
                        // list, not the bool, so the bool-only edit in the previous
                        // revision produced a "recruited but not actually in party"
                        // ghost state. Also remove from FallenCompanions if revive-via-
                        // recruit.
                        var ex = data.StorySystems.Companions.FirstOrDefault(c => c.Id == id);
                        if (ex == null) { ex = new CompanionSaveInfo { Id = id }; data.StorySystems.Companions.Add(ex); }
                        ex.IsRecruited = true; ex.IsDead = false; ex.IsActive = true;
                        if (!data.StorySystems.ActiveCompanionIds.Contains(id))
                        {
                            if (data.StorySystems.ActiveCompanionIds.Count >= 4)
                            {
                                EditorIO.Warn("Already at the 4-active-companion cap; not adding to active party.");
                                EditorIO.Info("Dismiss one first, or the new companion will be recruited but inactive.");
                                ex.IsActive = false;
                            }
                            else
                            {
                                data.StorySystems.ActiveCompanionIds.Add(id);
                            }
                        }
                        data.StorySystems.FallenCompanions.RemoveAll(f => f.CompanionId == id);
                        EditorIO.Success($"{picked} set to recruited{(ex.IsActive ? "+active" : "")}.");
                        EditorIO.Pause();
                        break;
                    }
                case 5:
                    {
                        var picked = EditorIO.PromptEnum("Companion to dismiss", UsurperRemake.Systems.CompanionId.Aldric);
                        int id = (int)picked;
                        var ex = data.StorySystems.Companions.FirstOrDefault(c => c.Id == id);
                        if (ex == null) { EditorIO.Warn("Not found."); EditorIO.Pause(); break; }
                        ex.IsRecruited = false; ex.IsActive = false;
                        data.StorySystems.ActiveCompanionIds.Remove(id);
                        EditorIO.Success($"{picked} dismissed.");
                        EditorIO.Pause();
                        break;
                    }
                case 6:
                    // Mark every companion alive AND drop them from FallenCompanions so
                    // CompanionSystem.Deserialize doesn't rebuild the fallen-companions
                    // dict from stale entries. Leaves IsRecruited/IsActive alone — this
                    // is a revive-only operation, not a recruit.
                    // v0.57.9 (Grug report): also clear ActiveGriefs and GriefMemories
                    // for the revived companions, matching OnlineAdminConsole behavior.
                    // Otherwise the player keeps seeing grief flashbacks for someone who
                    // is now standing right next to them in the party.
                    var revivedIds = data.StorySystems.Companions.Where(c => c.IsDead).Select(c => c.Id).ToList();
                    foreach (var c in data.StorySystems.Companions.Where(c => c.IsDead))
                        c.IsDead = false;
                    data.StorySystems.FallenCompanions.Clear();
                    if (data.StorySystems.ActiveGriefs != null)
                        data.StorySystems.ActiveGriefs.RemoveAll(g => revivedIds.Contains(g.CompanionId));
                    if (data.StorySystems.GriefMemories != null)
                        data.StorySystems.GriefMemories.RemoveAll(m => revivedIds.Contains(m.CompanionId));
                    EditorIO.Success("All companions marked alive. (Use Recruit to re-add to active party.)");
                    EditorIO.Pause();
                    break;
            }
        }
    }

    private static void ReviveCompanion(SaveGameData data)
    {
        var companions = data.StorySystems.Companions;
        var fallen = data.StorySystems.FallenCompanions;
        var picked = EditorIO.PromptEnum("Companion to revive", UsurperRemake.Systems.CompanionId.Aldric);
        int id = (int)picked;
        var c = companions.FirstOrDefault(x => x.Id == id);
        if (c == null) { EditorIO.Warn($"{picked} is not in the save. Use 'Recruit' first."); EditorIO.Pause(); return; }
        if (!c.IsDead && !fallen.Any(f => f.CompanionId == id))
        { EditorIO.Info($"{picked} is not dead."); EditorIO.Pause(); return; }
        c.IsDead = false;
        c.IsRecruited = true;
        // v0.57.4 (pass 3): remove from FallenCompanions too — CompanionSystem
        // rebuilds fallenCompanions dict from this list on Deserialize, so a
        // stale entry keeps the companion "permanently dead" in the in-game
        // UI/logic even after IsDead = false.
        fallen.RemoveAll(f => f.CompanionId == id);
        // v0.57.9 (Grug report): also strip the matching grief entries.
        // OnlineAdminConsole's revive path already does this; the local editor
        // didn't, so revived companions kept appearing in /health "grieving for"
        // lists and triggered combat-start grief reminders despite being alive
        // and in the party.
        if (data.StorySystems.ActiveGriefs != null)
            data.StorySystems.ActiveGriefs.RemoveAll(g => g.CompanionId == id);
        if (data.StorySystems.GriefMemories != null)
            data.StorySystems.GriefMemories.RemoveAll(m => m.CompanionId == id);
        EditorIO.Success($"{picked} revived. Use Recruit to re-add them to the active party.");
        EditorIO.Pause();
    }

    private static void SetCompanionRelationship(List<CompanionSaveInfo> companions)
    {
        var picked = EditorIO.PromptEnum("Companion", UsurperRemake.Systems.CompanionId.Aldric);
        int id = (int)picked;
        var c = companions.FirstOrDefault(x => x.Id == id);
        if (c == null) { EditorIO.Warn($"{picked} is not in the save. Recruit them first."); EditorIO.Pause(); return; }
        c.LoyaltyLevel = EditorIO.PromptInt("Loyalty (0-100)", c.LoyaltyLevel, min: 0, max: 100);
        c.TrustLevel = EditorIO.PromptInt("Trust (0-100)", c.TrustLevel, min: 0, max: 100);
        c.RomanceLevel = EditorIO.PromptInt("Romance (0-100)", c.RomanceLevel, min: 0, max: 100);
        EditorIO.Success("Updated.");
        EditorIO.Pause();
    }

    #endregion

    #region Quests

    private static void EditQuests(PlayerData p)
    {
        p.ActiveQuests ??= new List<QuestData>();
        // v0.57.4: QuestSystem.RestoreFromSaveData accepts whatever status the
        // save says without re-validating objectives, so marking a quest
        // Completed here doesn't also mark its objectives complete — reward
        // fire-on-complete may miss, and the quest board may still show it as
        // active depending on the quest type. Use Gold & Economy + Character
        // Info menus for rewards instead of relying on quest-complete payouts.
        EditorIO.Warn("Quest status edits are not re-validated by the game.");
        EditorIO.Info("Marking 'Completed' does NOT re-fire rewards or objective hooks.");
        EditorIO.Info("Use Gold & Economy / Achievements menus if you want rewards; treat quest edits as cosmetic.");
        while (true)
        {
            int choice = EditorIO.Menu("Quests", new[]
            {
                $"List quests ({p.ActiveQuests.Count})",
                "Mark a quest complete (sets Status)",
                "Cancel a quest",
                "Clear ALL quests",
            });
            if (choice == 0) return;
            switch (choice)
            {
                case 1:
                    if (p.ActiveQuests.Count == 0) { EditorIO.Info("(none)"); EditorIO.Pause(); break; }
                    foreach (var q in p.ActiveQuests)
                        EditorIO.Info($"  [{q.Id}] {q.Title}  status={q.Status}  reward={q.Reward}");
                    EditorIO.Pause();
                    break;
                case 2:
                    {
                        if (p.ActiveQuests.Count == 0) { EditorIO.Warn("No quests to complete."); EditorIO.Pause(); break; }
                        var q = PickQuest(p.ActiveQuests, "Quest to mark complete");
                        if (q == null) break;
                        q.Status = QuestStatus.Completed;
                        EditorIO.Success($"Marked \"{q.Title}\" complete.");
                        EditorIO.Pause();
                        break;
                    }
                case 3:
                    {
                        if (p.ActiveQuests.Count == 0) { EditorIO.Warn("No quests to cancel."); EditorIO.Pause(); break; }
                        var q = PickQuest(p.ActiveQuests, "Quest to cancel");
                        if (q == null) break;
                        q.Status = QuestStatus.Abandoned;
                        EditorIO.Success($"Cancelled \"{q.Title}\".");
                        EditorIO.Pause();
                        break;
                    }
                case 4:
                    if (EditorIO.Confirm("Remove ALL quests from the save?"))
                    { p.ActiveQuests.Clear(); EditorIO.Success("Cleared."); EditorIO.Pause(); }
                    break;
            }
        }
    }

    /// <summary>
    /// Pick a specific quest from the player's active list via an arrow-key
    /// selector. Shows title + status so users can tell them apart when a
    /// character has many quests. Returns null if the user backs out.
    /// </summary>
    private static QuestData? PickQuest(List<QuestData> quests, string title)
    {
        var labels = quests.Select(q => $"[{q.Status}] {q.Title}").ToList();
        int pick = EditorIO.Menu(title, labels);
        if (pick == 0) return null;
        return quests[pick - 1];
    }

    #endregion

    #region Old Gods & Story

    private static void EditStoryAndGods(SaveGameData data)
    {
        data.StorySystems ??= new StorySystemsData();
        var s = data.StorySystems;
        // v0.57.4: Old God states load without prerequisite validation — setting
        // Manwe Defeated while Maelketh is Dormant produces a technically-valid
        // save, but the progression flow (dungeon events, dialogue, endings)
        // assumes gods fall in floor order (Maelketh 25 → Veloura 40 →
        // Thorgrim 55 → Noctura 70 → Aurelion 85 → Terravok 95 → Manwe 100).
        // Out-of-order states can soft-lock story events. Warn once.
        EditorIO.Warn("Old God states load as-is — the game does NOT enforce prerequisites.");
        EditorIO.Info("Floor order: Maelketh(25) → Veloura(40) → Thorgrim(55) → Noctura(70) → Aurelion(85) → Terravok(95) → Manwe(100).");
        EditorIO.Info("Skipping gods may soft-lock dungeon events and endings. Use at your own risk.");
        while (true)
        {
            int choice = EditorIO.Menu("Story & Old Gods", new[]
            {
                $"Old God states ({s.OldGodStates?.Count ?? 0} tracked)",
                "Set Old God status by ID",
                $"Collected seals ({s.CollectedSeals?.Count ?? 0}/7)",
                "Grant all seven seals",
                "Clear seals",
                $"Collected artifacts ({s.CollectedArtifacts?.Count ?? 0})",
                "Grant artifact by ID",
                $"NG+ cycle: {s.CurrentCycle}",
                "Set NG+ cycle",
                $"Completed endings: [{string.Join(",", s.CompletedEndings ?? new())}]",
                "Clear story flags",
            });
            if (choice == 0) return;
            switch (choice)
            {
                case 1:
                    foreach (var kv in s.OldGodStates ?? new())
                        EditorIO.Info($"  God#{kv.Key}  status={kv.Value}");
                    EditorIO.Pause();
                    break;
                case 2:
                    SetOldGodStatus(s);
                    break;
                case 3:
                    foreach (var id in s.CollectedSeals ?? new()) EditorIO.Info($"  Seal #{id}");
                    EditorIO.Pause();
                    break;
                case 4:
                    s.CollectedSeals = new List<int> { 1, 2, 3, 4, 5, 6, 7 };
                    EditorIO.Success("All 7 seals granted.");
                    EditorIO.Pause();
                    break;
                case 5:
                    s.CollectedSeals = new List<int>();
                    EditorIO.Success("Seals cleared.");
                    EditorIO.Pause();
                    break;
                case 6:
                    foreach (var id in s.CollectedArtifacts ?? new()) EditorIO.Info($"  Artifact #{id}");
                    EditorIO.Pause();
                    break;
                case 7:
                    {
                        s.CollectedArtifacts ??= new List<int>();
                        var artifact = EditorIO.PromptEnum("Artifact", ArtifactType.CreatorsEye);
                        if (!s.CollectedArtifacts.Contains((int)artifact)) s.CollectedArtifacts.Add((int)artifact);
                        EditorIO.Success($"Granted {artifact}.");
                        EditorIO.Pause();
                        break;
                    }
                case 8:
                    break; // display-only
                case 9:
                    // v0.57.4 (pass 3): CurrentCycle controls NG+ difficulty modifiers
                    // (monster HP / gold multipliers via GameConfig.GetNGPlus*Multiplier)
                    // AND the player's CycleExpMultiplier / CycleBonuses. Those are
                    // normally applied in OpeningSequence.ApplyCycleBonusesToNewCharacter
                    // at NG+ start. Setting the cycle number here updates the counter
                    // but does NOT grant the matching XP multiplier / starting bonuses —
                    // you'll face harder cycle-N monsters without the cycle-N perks.
                    EditorIO.Warn("Setting NG+ cycle changes monster scaling but NOT XP multiplier or starting bonuses.");
                    EditorIO.Info("Cycle bonuses normally apply at NG+ start (OpeningSequence). Editing this mid-character creates imbalance.");
                    s.CurrentCycle = EditorIO.PromptInt("NG+ cycle", s.CurrentCycle, min: 1);
                    // Keep CycleExpMultiplier vaguely sane: match the new cycle
                    // number linearly (cycle 1 = 1.0x, cycle 2 = 1.25x, etc.) so
                    // the player isn't completely stuck at 1.0x if they bump it up.
                    if (s.CurrentCycle > 1)
                    {
                        float suggested = 1.0f + (s.CurrentCycle - 1) * 0.25f;
                        var p = data.Player;
                        if (EditorIO.Confirm($"Also set CycleExpMultiplier to {suggested:F2}x (suggested for cycle {s.CurrentCycle})?"))
                        {
                            p.CycleExpMultiplier = suggested;
                            EditorIO.Success($"CycleExpMultiplier = {suggested:F2}x");
                        }
                    }
                    break;
                case 10:
                    break;
                case 11:
                    if (EditorIO.Confirm("Clear StoryFlags (risky — may unstick or re-stick story)?"))
                    { s.StoryFlags?.Clear(); EditorIO.Success("Cleared."); EditorIO.Pause(); }
                    break;
            }
        }
    }

    private static void SetOldGodStatus(StorySystemsData s)
    {
        s.OldGodStates ??= new Dictionary<int, int>();
        // Use the existing enum names as the picker vocabulary — no more "which
        // integer is Manwe again?" guessing for the user.
        var god = EditorIO.PromptEnum("Old God", OldGodType.Maelketh);
        var status = EditorIO.PromptEnum("Status", GodStatus.Dormant);
        s.OldGodStates[(int)god] = (int)status;
        EditorIO.Success($"{god} = {status}.");
        EditorIO.Pause();
    }

    #endregion

    #region Relationships & Family

    private static void EditRelationshipsAndFamily(SaveGameData data)
    {
        var p = data.Player;
        p.Relationships ??= new Dictionary<string, float>();
        // v0.57.4: the per-NPC Relationship score is independent from marriage /
        // lover / ex status, which live in RomanceTracker's Spouses / CurrentLovers
        // / Exes collections (loaded separately at GameEngine.cs:4348). Setting
        // Relationship["Ivy"] = -500 won't divorce Ivy — it only affects generic
        // dialogue tone. For marriage / romance changes, the editor doesn't yet
        // expose RomanceTracker; edit via in-game temple / church actions.
        EditorIO.Info("Note: relationship score edits affect dialogue tone only.");
        EditorIO.Info("Marriages / lovers / exes are tracked separately (RomanceTracker) and NOT edited here.");
        while (true)
        {
            int choice = EditorIO.Menu("Relationships & Family", new[]
            {
                $"List relationships ({p.Relationships.Count})",
                "Set a relationship score by NPC name",
                "Clear all relationships",
                $"Kids: {p.Kids}",
                "Set kid count (simple counter; full Children list editable via JSON)",
                $"Divine wrath level: {p.DivineWrathLevel}",
                "Clear divine wrath (forgive the betrayed god)",
            });
            if (choice == 0) return;
            switch (choice)
            {
                case 1:
                    foreach (var kv in p.Relationships.OrderByDescending(k => k.Value))
                        EditorIO.Info($"  {kv.Key,-25} {kv.Value:F1}");
                    EditorIO.Pause();
                    break;
                case 2:
                    {
                        string name = EditorIO.Prompt("NPC name (exact)");
                        if (string.IsNullOrWhiteSpace(name)) break;
                        float cur = p.Relationships.TryGetValue(name, out var v) ? v : 0;
                        var s = EditorIO.PromptString("Score (-100..100 typical)", cur.ToString("F1"));
                        if (float.TryParse(s, System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out var f))
                        { p.Relationships[name] = f; EditorIO.Success($"{name} = {f}"); }
                        else EditorIO.Warn("Not a number.");
                        EditorIO.Pause();
                        break;
                    }
                case 3:
                    if (EditorIO.Confirm("Clear all relationships?"))
                    { p.Relationships.Clear(); EditorIO.Success("Cleared."); EditorIO.Pause(); }
                    break;
                case 4: break; // display
                case 5:
                    // v0.57.4 (pass 3): p.Kids is only a counter. The actual
                    // Children list (full ChildData objects with names, ages,
                    // classes, parent links) lives on StorySystemsData.Children
                    // and the FamilySystem uses THAT list for gameplay — reading
                    // Kids count produces weird states (e.g. "you have 5 kids"
                    // but Home Location shows 0 when spending time with a child).
                    EditorIO.Warn("This only edits the Kids counter, NOT the actual Children list.");
                    EditorIO.Info("FamilySystem reads the full Children list (names, ages) — setting Kids high produces a ghost counter.");
                    EditorIO.Info("For real children, have them naturally via marriage or edit GameData directly.");
                    p.Kids = EditorIO.PromptInt("Kid count", p.Kids, min: 0);
                    break;
                case 6: break;
                case 7:
                    p.DivineWrathLevel = 0;
                    p.DivineWrathPending = false;
                    p.DivineWrathTurnsRemaining = 0;
                    p.AngeredGodName = "";
                    p.BetrayedForGodName = "";
                    EditorIO.Success("Divine wrath cleared.");
                    EditorIO.Pause();
                    break;
            }
        }
    }

    #endregion

    #region Status & Cleanup

    private static void EditStatusAndCleanup(PlayerData p)
    {
        while (true)
        {
            int choice = EditorIO.Menu("Status & Cleanup", new[]
            {
                $"Cure diseases (Blind={p.Blind} Plague={p.Plague} Smallpox={p.Smallpox} Measles={p.Measles} Leprosy={p.Leprosy} LoversBane={p.LoversBane})",
                $"Clear poison (Poison={p.Poison}, turns={p.PoisonTurns})",
                "Clear drug addiction / steroid effects",
                "Reset ALL daily counters (fights, brawls, thievery, etc.)",
                "Clear all active status effects",
                "Release from prison",
                "Clear wanted level",
                "Clear murder weight / perma-kill log",
            });
            if (choice == 0) return;
            switch (choice)
            {
                case 1:
                    p.Blind = p.Plague = p.Smallpox = p.Measles = p.Leprosy = p.LoversBane = false;
                    EditorIO.Success("All diseases cured.");
                    EditorIO.Pause();
                    break;
                case 2:
                    p.Poison = 0; p.PoisonTurns = 0; p.GnollP = 0;
                    EditorIO.Success("Poison cleared.");
                    EditorIO.Pause();
                    break;
                case 3:
                    p.Addict = 0; p.SteroidDays = 0; p.DrugEffectDays = 0; p.ActiveDrug = 0;
                    EditorIO.Success("Drug effects cleared.");
                    EditorIO.Pause();
                    break;
                case 4:
                    ResetDailyCounters(p);
                    break;
                case 5:
                    p.ActiveStatuses?.Clear();
                    EditorIO.Success("Status effects cleared.");
                    EditorIO.Pause();
                    break;
                case 6:
                    p.DaysInPrison = 0;
                    p.IsMurderConvict = false;
                    p.CellDoorOpen = false;
                    p.PrisonEscapes = 3;
                    EditorIO.Success("Released from prison.");
                    EditorIO.Pause();
                    break;
                case 7:
                    p.WantedLvl = 0;
                    EditorIO.Success("Wanted level = 0.");
                    EditorIO.Pause();
                    break;
                case 8:
                    p.MurderWeight = 0;
                    p.PermakillLog?.Clear();
                    EditorIO.Success("Murder weight cleared.");
                    EditorIO.Pause();
                    break;
            }
        }
    }

    private static void ResetDailyCounters(PlayerData p)
    {
        EditorIO.Info("Resetting all daily limits to fresh values...");
        p.Fights = GameConfig.DefaultDungeonFights;
        p.PFights = GameConfig.DefaultPlayerFights;
        p.TFights = GameConfig.DefaultTeamFights;
        p.Thiefs = GameConfig.DefaultThiefAttempts;
        p.Brawls = GameConfig.DefaultBrawls;
        p.Assa = GameConfig.DefaultAssassinAttempts;
        p.DarkNr = 0;
        p.ChivNr = 0;
        p.ThroneChallengedToday = false;
        p.ExecutionsToday = 0;
        p.NPCsImprisonedToday = 0;
        p.PlayerImprisonedToday = false;
        p.BankRobberyAttempts = 0;
        p.TempleResurrectionsUsed = 0;
        p.MurdersToday = 0;
        p.TeamWarsToday = 0;
        p.DrinkingGamesToday = 0;
        EditorIO.Success("Daily counters reset. Play as if a new day began.");
        EditorIO.Pause();
    }

    #endregion

    #region Summary

    private static void ShowSummary(PlayerData p)
    {
        EditorIO.Section($"{p.Name2}  —  L{p.Level} {p.Race} {p.Class}");
        EditorIO.Info($"  HP:   {p.HP} / {p.MaxHP}   Mana: {p.Mana} / {p.MaxMana}");
        EditorIO.Info($"  XP:   {p.Experience}   Fame: {p.Fame}{(p.IsKnighted ? " (knighted)" : "")}{(p.King ? " — CURRENT KING" : "")}");
        EditorIO.Info($"  Gold: {p.Gold} (bank: {p.BankGold}, loan: {p.BankLoan})");
        EditorIO.Info($"  STR {p.Strength}  DEX {p.Dexterity}  CON {p.Constitution}  INT {p.Intelligence}  WIS {p.Wisdom}  CHA {p.Charisma}");
        EditorIO.Info($"  DEF {p.Defence}  AGI {p.Agility}  STA {p.Stamina}   WeapPow {p.WeapPow}  ArmPow {p.ArmPow}");
        EditorIO.Info($"  Chivalry {p.Chivalry}  Darkness {p.Darkness}");
        EditorIO.Info($"  Resurrections: {p.Resurrections}/{p.MaxResurrections} (used {p.ResurrectionsUsed})");
        EditorIO.Info($"  Potions: heal={p.Healing}  mana={p.ManaPotions}  antidote={p.Antidotes}");
        EditorIO.Info($"  Inventory: {p.Inventory?.Count ?? 0} items   Equipped slots: {p.EquippedItems?.Count ?? 0}");
        EditorIO.Info($"  Abilities: {p.LearnedAbilities?.Count ?? 0}   Quests: {p.ActiveQuests?.Count ?? 0}   Achievements: {p.Achievements?.Count(kv => kv.Value) ?? 0}");
        bool anyDisease = p.Blind || p.Plague || p.Smallpox || p.Measles || p.Leprosy || p.LoversBane;
        if (anyDisease) EditorIO.Warn($"  Has diseases: Blind={p.Blind} Plague={p.Plague} Smallpox={p.Smallpox} Measles={p.Measles} Leprosy={p.Leprosy} LoversBane={p.LoversBane}");
        if (p.Poison > 0) EditorIO.Warn($"  Poisoned ({p.PoisonTurns} turns remain)");
        if (p.DaysInPrison > 0) EditorIO.Warn($"  In prison ({p.DaysInPrison} days)");
        if (p.WantedLvl > 0) EditorIO.Warn($"  Wanted level: {p.WantedLvl}");
        EditorIO.Pause();
    }

    #endregion

    #region Appearance & Flavor

    private static void EditAppearance(PlayerData p)
    {
        EditorIO.Section("Appearance & Flavor");
        p.Height = EditorIO.PromptInt("Height (inches / cosmetic)", p.Height, min: 0, max: 400);
        p.Weight = EditorIO.PromptInt("Weight", p.Weight, min: 0, max: 1000);
        p.Eyes = EditorIO.PromptInt("Eyes (index into eye-color table)", p.Eyes, min: 0);
        p.Hair = EditorIO.PromptInt("Hair (index)", p.Hair, min: 0);
        p.Skin = EditorIO.PromptInt("Skin (index)", p.Skin, min: 0);

        EditorIO.Info("— Combat phrases (6 lines; shown in some victory/taunt events) —");
        p.Phrases ??= new List<string>();
        while (p.Phrases.Count < 6) p.Phrases.Add("");
        for (int i = 0; i < 6; i++)
            p.Phrases[i] = EditorIO.PromptString($"Phrase {i + 1}", p.Phrases[i]);

        EditorIO.Info("— Character description (4 lines; shown on character sheets) —");
        p.Description ??= new List<string>();
        while (p.Description.Count < 4) p.Description.Add("");
        for (int i = 0; i < 4; i++)
            p.Description[i] = EditorIO.PromptString($"Desc line {i + 1}", p.Description[i]);

        p.BattleCry = EditorIO.PromptString("Battle cry (short slogan)", p.BattleCry);
    }

    #endregion

    #region Skills & Training

    private static void EditSkillsAndTraining(PlayerData p)
    {
        while (true)
        {
            p.SkillProficiencies ??= new Dictionary<string, int>();
            p.StatTrainingCounts ??= new Dictionary<string, int>();
            p.CraftingMaterials ??= new Dictionary<string, int>();
            int choice = EditorIO.Menu("Skills & Training", new[]
            {
                $"Unspent training sessions: {p.Trains}",
                $"Training points: {p.TrainingPoints}",
                $"Skill proficiencies ({p.SkillProficiencies.Count})",
                $"Gold-based stat training counts ({p.StatTrainingCounts.Count})",
                $"Crafting materials ({p.CraftingMaterials.Count})",
                "Set a skill proficiency level",
                "Clear all skill proficiencies",
                "Set stat training count (resets the 'too expensive' progression)",
                "Add crafting material by name",
            });
            switch (choice)
            {
                case 0: return;
                case 1: p.Trains = EditorIO.PromptInt("Training sessions", p.Trains, min: 0); break;
                case 2: p.TrainingPoints = EditorIO.PromptInt("Training points", p.TrainingPoints, min: 0); break;
                case 3:
                    foreach (var kv in p.SkillProficiencies.OrderBy(k => k.Key))
                        EditorIO.Info($"  {kv.Key,-20} level {kv.Value}");
                    EditorIO.Pause();
                    break;
                case 4:
                    foreach (var kv in p.StatTrainingCounts.OrderBy(k => k.Key))
                        EditorIO.Info($"  {kv.Key,-20} trained {kv.Value} times");
                    EditorIO.Pause();
                    break;
                case 5:
                    foreach (var kv in p.CraftingMaterials.OrderBy(k => k.Key))
                        EditorIO.Info($"  {kv.Key,-25} x{kv.Value}");
                    EditorIO.Pause();
                    break;
                case 6:
                    {
                        // v0.57.4 (pass 3): the old picker used EditorVocab.CombatSkillNames
                        // ("sword", "axe", "mace", ...) which has no relationship to the
                        // actual skill IDs the game stores in SkillProficiencies. Real
                        // skill IDs are "basic_attack", class ability IDs from
                        // ClassAbilitySystem (e.g. "backstab", "power_attack"), and spell
                        // IDs like "cleric_spell_3". Setting e.g. "sword" = 5 wrote a
                        // dead key that nothing in the game reads.
                        //
                        // Build the picker from real registries: basic_attack + every
                        // registered class ability. Spell proficiency ids depend on class
                        // and spell level, so we offer spell-levels 1..12 for the player's
                        // class when it's a caster.
                        var skillIds = new List<string> { "basic_attack" };
                        skillIds.AddRange(ClassAbilitySystem.GetAllAbilities().Select(a => a.Id));
                        if (IsCasterClass(p.Class))
                        {
                            string classPrefix = p.Class switch
                            {
                                CharacterClass.Cleric => "cleric",
                                CharacterClass.Magician => "magician",
                                CharacterClass.Sage => "sage",
                                _ => "spell",
                            };
                            for (int lvl = 1; lvl <= 12; lvl++)
                                skillIds.Add($"{classPrefix}_spell_{lvl}");
                        }
                        var labels = skillIds.Select(s =>
                        {
                            int cur = p.SkillProficiencies.TryGetValue(s, out var v) ? v : 0;
                            return $"{s,-30}  current level: {cur}";
                        }).ToList();
                        int pick = EditorIO.Menu("Skill", labels);
                        if (pick == 0) break;
                        string skillId = skillIds[pick - 1];
                        int prev = p.SkillProficiencies.TryGetValue(skillId, out var pv) ? pv : 0;
                        // ProficiencyLevel enum: 0 Untrained, 1 Poor, 2 Average, 3 Good,
                        // 4 Skilled, 5 Expert, 6 Superb, 7 Master, 8 Legendary (max).
                        p.SkillProficiencies[skillId] = EditorIO.PromptInt("Level (0 Untrained .. 8 Legendary)", prev, min: 0, max: 8);
                        EditorIO.Success($"{skillId} = {p.SkillProficiencies[skillId]}");
                        EditorIO.Pause();
                        break;
                    }
                case 7:
                    if (EditorIO.Confirm("Forget all skill proficiencies?"))
                    { p.SkillProficiencies.Clear(); EditorIO.Success("Cleared."); EditorIO.Pause(); }
                    break;
                case 8:
                    {
                        string stat = EditorIO.PromptChoice("Stat", EditorVocab.CoreStatNames, "", allowCustom: false);
                        if (!string.IsNullOrWhiteSpace(stat))
                        {
                            int cur = p.StatTrainingCounts.TryGetValue(stat, out var v) ? v : 0;
                            p.StatTrainingCounts[stat] = EditorIO.PromptInt("Training count", cur, min: 0);
                            EditorIO.Success("Set.");
                            EditorIO.Pause();
                        }
                        break;
                    }
                case 9:
                    {
                        string mat = EditorIO.Prompt("Material name");
                        if (!string.IsNullOrWhiteSpace(mat))
                        {
                            int cur = p.CraftingMaterials.TryGetValue(mat, out var v) ? v : 0;
                            p.CraftingMaterials[mat] = EditorIO.PromptInt("Quantity", cur, min: 0);
                            EditorIO.Success("Set.");
                            EditorIO.Pause();
                        }
                        break;
                    }
            }
        }
    }

    #endregion

    #region Team / Guild / Factions

    private static void EditTeamAndGuild(PlayerData p)
    {
        EditorIO.Section("Team / Guild");
        // v0.57.4: Team and Guild are both backed by SQLite in online / MUD
        // mode (teams table; guilds + guild_members tables via GuildSystem).
        // Editing the local save's Team / IsTeamLeader fields does NOT update
        // the server database — the player will still appear in the same
        // server-side team / guild they were in when the save was taken.
        // Single-player saves use these fields directly and edits work there.
        EditorIO.Info("(Team / Guild fields are authoritative in SINGLE-PLAYER saves only.)");
        EditorIO.Info("Online mode stores team and guild membership in the server's SQLite — edits here won't sync.");
        p.Team = EditorIO.PromptString("Team name (blank = no team)", p.Team);
        p.TeamPassword = EditorIO.PromptString("Team password", p.TeamPassword);
        p.IsTeamLeader = EditorIO.PromptBool("Is team leader", p.IsTeamLeader);
        p.TeamRec = EditorIO.PromptInt("Team record (days held turf)", p.TeamRec, min: 0);
        p.BGuard = EditorIO.PromptInt("Door guard type", p.BGuard, min: 0);
        p.BGuardNr = EditorIO.PromptInt("Number of door guards", p.BGuardNr, min: 0);

        EditorIO.Info("— Unpaid NPC team wages —");
        p.UnpaidWageDays ??= new Dictionary<string, int>();
        if (p.UnpaidWageDays.Count == 0)
            EditorIO.Info("  (none)");
        else
            foreach (var kv in p.UnpaidWageDays)
                EditorIO.Info($"  {kv.Key,-20} {kv.Value} days unpaid");
        if (p.UnpaidWageDays.Count > 0 && EditorIO.Confirm("Clear all unpaid wages?"))
        {
            p.UnpaidWageDays.Clear();
            EditorIO.Success("Cleared.");
        }
    }

    #endregion

    #region Settings & Preferences

    private static void EditSettings(PlayerData p)
    {
        EditorIO.Section("Settings & Preferences");
        p.AutoHeal = EditorIO.PromptBool("AutoHeal in battle", p.AutoHeal);
        p.CombatSpeed = EditorIO.PromptEnum("CombatSpeed", p.CombatSpeed);
        p.SkipIntimateScenes = EditorIO.PromptBool("Skip intimate scenes (fade to black)", p.SkipIntimateScenes);
        p.ScreenReaderMode = EditorIO.PromptBool("Screen reader mode", p.ScreenReaderMode);
        p.CompactMode = EditorIO.PromptBool("Compact mode (mobile/small-screen menus)", p.CompactMode);
        // v0.57.4: use live Loc.AvailableLanguages for the picker so the editor
        // always offers exactly the languages actually installed — a typo'd
        // language code used to silently fall back to English at runtime.
        var availableLangs = UsurperRemake.Systems.Loc.AvailableLanguages;
        if (availableLangs.Length > 0)
        {
            var langCodes = availableLangs.Select(l => l.Code).ToList();
            var langLabels = availableLangs.Select(l => $"{l.Code}  —  {l.Name}").ToList();
            int pick = EditorIO.Menu($"Language (current: {p.Language ?? "en"})", langLabels);
            if (pick > 0) p.Language = langCodes[pick - 1];
        }
        else
        {
            // Fallback: free text if somehow no languages are registered (should
            // never happen in a correctly-packaged build).
            p.Language = EditorIO.PromptString("Language code", p.Language);
        }
        p.ColorTheme = EditorIO.PromptEnum("ColorTheme", p.ColorTheme);
        p.AutoLevelUp = EditorIO.PromptBool("AutoLevelUp on XP threshold", p.AutoLevelUp);
        p.AutoEquipDisabled = EditorIO.PromptBool("AutoEquipDisabled (shop purchases go to inventory)", p.AutoEquipDisabled);
        p.DateFormatPreference = EditorIO.PromptInt("DateFormat (0=MM/DD, 1=DD/MM, 2=YYYY-MM-DD)", p.DateFormatPreference, min: 0, max: 2);
        p.AutoRedistributeXP = EditorIO.PromptBool("Auto-redistribute XP when teammates die", p.AutoRedistributeXP);
    }

    #endregion

    #region World State

    private static void EditWorldState(SaveGameData data)
    {
        data.WorldState ??= new WorldStateData();
        var w = data.WorldState;
        EditorIO.Section("World State");
        // v0.57.4: in online / MUD mode the shared world (king, treasury, town
        // pot, day, events, news) lives in the server's world_state SQLite and
        // is restored at login from OnlineStateManager (overriding any
        // WorldState block in a local save). This editor only touches local
        // save files, so in practice these edits ARE single-player only — but
        // users who dual-use (local save + server character) could be confused
        // when their local edits don't reflect on the server.
        EditorIO.Warn("These fields control the SINGLE-PLAYER world only.");
        EditorIO.Info("In online / MUD mode, the shared world lives in the server's world_state database.");
        EditorIO.Pause();
        w.CurrentRuler = EditorIO.PromptString("Current ruler name (blank = no king)", w.CurrentRuler ?? "");
        if (string.IsNullOrWhiteSpace(w.CurrentRuler)) w.CurrentRuler = null;
        w.BankInterestRate = EditorIO.PromptInt("BankInterestRate (percent)", w.BankInterestRate, min: 0, max: 100);
        w.TownPotValue = EditorIO.PromptInt("TownPot value (gold)", w.TownPotValue, min: 0);

        EditorIO.Info("— Day / calendar —");
        data.CurrentDay = EditorIO.PromptInt("CurrentDay", data.CurrentDay, min: 1);
        data.Player.TurnCount = EditorIO.PromptInt("TurnCount (world sim counter)", data.Player.TurnCount, min: 0);
        data.Player.GameTimeMinutes = EditorIO.PromptInt("GameTimeMinutes (0-1439)", data.Player.GameTimeMinutes, min: 0, max: 1439);

        EditorIO.Info("— Clean-up options —");
        if (w.ActiveEvents?.Count > 0 && EditorIO.Confirm($"Clear {w.ActiveEvents.Count} active world events?"))
        {
            w.ActiveEvents.Clear();
            EditorIO.Success("Cleared.");
        }
        if (w.RecentNews?.Count > 0 && EditorIO.Confirm($"Clear {w.RecentNews.Count} news entries?"))
        {
            w.RecentNews.Clear();
            EditorIO.Success("Cleared.");
        }
    }

    #endregion
}
