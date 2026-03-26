using System.Text.Json;
using System.Text.Json.Serialization;

/// <summary>
/// Emits structured JSON events to the Electron graphical client via OSC escape sequences.
/// Events are invisible to regular terminals (they ignore unrecognized OSC sequences).
/// Format: ESC ] 1337 ; usurper:{json} BEL
/// </summary>
public static class ElectronBridge
{
    private static readonly JsonSerializerOptions _jsonOpts = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
        WriteIndented = false
    };

    /// <summary>
    /// Emit a JSON event to the Electron client. No-op if not in Electron mode.
    /// </summary>
    public static void Emit(string eventType, object data)
    {
        if (!GameConfig.ElectronMode) return;

        try
        {
            var envelope = new { e = eventType, d = data };
            var json = JsonSerializer.Serialize(envelope, _jsonOpts);
            // OSC 1337 ; usurper:{json} BEL
            Console.Write($"\x1b]1337;usurper:{json}\x07");
        }
        catch
        {
            // Never crash the game for a client event
        }
    }

    // ─── Location Events ─────────────────────

    public static void EmitLocation(string name, string description, string timeOfDay)
    {
        Emit("location", new { name, description, timeOfDay });
    }

    public static void EmitMenu(List<MenuItemData> items)
    {
        Emit("menu", new { items });
    }

    public static void EmitNPCList(List<NPCPresenceData> npcs)
    {
        Emit("npcs", new { npcs });
    }

    // ─── Player Stats ─────────────────────────

    public static void EmitStats(long hp, long maxHp, long mana, long maxMana,
        long stamina, long maxStamina, long gold, int level, string className, string raceName,
        string? playerName = null)
    {
        Emit("stats", new
        {
            hp, maxHp, mana, maxMana, stamina, maxStamina,
            gold, level, className, raceName, playerName
        });
    }

    // ─── Combat Events ────────────────────────

    public static void EmitCombatStart(string monsterName, int monsterLevel, long monsterHp, long monsterMaxHp, bool isBoss)
    {
        Emit("combat_start", new { monsterName, monsterLevel, monsterHp, monsterMaxHp, isBoss });
    }

    public static void EmitCombatAction(string actor, string action, string target, long damage, long targetHp, long targetMaxHp)
    {
        Emit("combat_action", new { actor, action, target, damage, targetHp, targetMaxHp });
    }

    public static void EmitCombatEnd(string outcome, long xpGained, long goldGained, string? lootName = null)
    {
        Emit("combat_end", new { outcome, xpGained, goldGained, lootName });
    }

    // ─── Shop Events ──────────────────────────

    public static void EmitShopInventory(string shopName, List<ShopItemData> items)
    {
        Emit("shop", new { shopName, items });
    }

    // ─── Narrative ────────────────────────────

    public static void EmitNarration(string text, string style = "normal")
    {
        Emit("narration", new { text, style });
    }

    public static void EmitPrompt(string prompt, string[] options)
    {
        Emit("prompt", new { prompt, options });
    }

    // ─── Dungeon Events ──────────────────────

    /// <summary>
    /// Emit a choice prompt with labeled options for the Electron client.
    /// The client renders these as clickable buttons.
    /// </summary>
    public static void EmitChoicePrompt(string context, string title, List<ChoiceOption> options)
    {
        Emit("choice", new { context, title, options });
    }

    /// <summary>Emit an event encounter with choices</summary>
    public static void EmitEventEncounter(string eventType, string title, string description, List<ChoiceOption> options)
    {
        Emit("event_encounter", new { eventType, title, description, options });
    }

    /// <summary>Emit loot item for pickup</summary>
    public static void EmitLootItem(string itemName, string itemType, int attack, int armor,
        Dictionary<string, int>? bonusStats, string rarity, bool isIdentified, List<ChoiceOption> options)
    {
        Emit("loot_item", new { itemName, itemType, attack, armor, bonusStats, rarity, isIdentified, options });
    }

    /// <summary>Emit combat target selection</summary>
    public static void EmitTargetSelection(string action, List<TargetOption> targets)
    {
        Emit("target_select", new { action, targets });
    }

    /// <summary>Emit floor overview</summary>
    public static void EmitFloorOverview(int floor, string theme, int totalRooms, int clearedRooms,
        bool hasStairs, bool hasBoss, List<ChoiceOption> options)
    {
        Emit("floor_overview", new { floor, theme, totalRooms, clearedRooms, hasStairs, hasBoss, options });
    }

    /// <summary>Emit a "press any key" signal</summary>
    public static void EmitPressAnyKey()
    {
        Emit("press_any_key", new { });
    }

    /// <summary>Emit confirmation prompt (Y/N)</summary>
    public static void EmitConfirm(string question)
    {
        Emit("confirm", new { question });
    }

    // ─── Data Types ───────────────────────────

    public class ChoiceOption
    {
        public string Key { get; set; } = "";
        public string Label { get; set; } = "";
        public string? Style { get; set; }  // "danger", "treasure", "info", etc.
    }

    public class TargetOption
    {
        public int Index { get; set; }
        public string Name { get; set; } = "";
        public long Hp { get; set; }
        public long MaxHp { get; set; }
        public string? Status { get; set; }
    }

    public class MenuItemData
    {
        public string Key { get; set; } = "";
        public string Label { get; set; } = "";
        public string? Category { get; set; }
        public string? Icon { get; set; }
    }

    public class NPCPresenceData
    {
        public string Name { get; set; } = "";
        public string? Activity { get; set; }
        public string? Class { get; set; }
        public int? Level { get; set; }
    }

    public class ShopItemData
    {
        public string Name { get; set; } = "";
        public int Price { get; set; }
        public string Slot { get; set; } = "";
        public int Power { get; set; }
        public string? Rarity { get; set; }
    }
}
