using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;

namespace UsurperRemake.Systems
{
    /// <summary>
    /// v1.2.5: the one place an item's name is turned into the reader's language for display.
    ///
    /// An item's Name is stored, saved, traded and matched (the gear set resolver, the enchant strip, the
    /// enchant base record, EquipmentDatabase.GetByName), so it stays exactly as it was written. The
    /// hand-authored tables write it in English: the built-in equipment (EquipmentData), the classic
    /// weapons and armour (ItemManager), the NPC loot and merchant tables (NPCItemGenerator), and the
    /// shop catalogue, whose names are the loot templates' English names. Each of those English
    /// template names is the item's stable id, and its key is LootGenerator.TemplateLocKey of it
    /// ("Long Sword" is item.long_sword); a built-in piece is found by its EquipmentData id first.
    ///
    /// Display reads the stored name as: an optional quality prefix from NPCItemGenerator's table, an
    /// English template name, then the Magic Shop's enchant parts (" +4 Dex", " (Blessed)"). The template
    /// is shown through its key, the prefix through its key, and the enchant parts are appended as
    /// stored, the way the Magic Shop writes them (English, matched by the gear set resolver and the
    /// enchant removal). A stored name that is not an English template name underneath (rolled loot,
    /// whose name was localized when it dropped, a modded item, any other name) is shown as stored.
    ///
    /// The result is for the screen only: never assign it to Name or to anything saved.
    /// </summary>
    public static class ItemNames
    {
        /// <summary>The NPC table's quality prefixes, English as stored, and the key each one shows
        /// through. Fine, Superior and Cursed are the loot rarity words, so an NPC's Fine Long Sword
        /// reads like a Fine Long Sword dropped in the reader's language; the rest are templates.</summary>
        private static readonly (string Stored, string Key, bool Template)[] QualityPrefixKeys =
        {
            ("Fine ", "item.rarity.fine", false),
            ("Superior ", "item.rarity.superior", false),
            ("Cursed ", "item.rarity.cursed", false),
            ("Quality ", "item.quality.quality", true),
            ("Masterwork ", "item.quality.masterwork", true),
            ("Rusted ", "item.quality.rusted", true),
            ("Worn ", "item.quality.worn", true),
            ("Battered ", "item.quality.battered", true),
            ("Enchanted ", "item.quality.enchanted", true),
            ("Blessed ", "item.quality.blessed", true),
        };

        /// <summary>The stored prefixes the display maps, for the tests.</summary>
        internal static IEnumerable<string> MappedPrefixes => QualityPrefixKeys.Select(p => p.Stored);

        // The Magic Shop's enchant parts, as GearSetFamilyResolver.TrailingEnchant peels them: a tag in
        // parentheses, or " +N" with an optional three-letter stat code. No template holds either.
        private static readonly Regex TrailingEnchant = new(@"\s+(?:\([^()]*\)|\+\d+(?:\s+\p{L}{3})?)$", RegexOptions.Compiled);

        private static readonly Lazy<Dictionary<string, string>> Templates = new(BuildTemplates);

        /// <summary>Every English template name that has its key, mapped to the key. A name whose key is
        /// missing, or whose key's English text is not the name, is left out and shows as stored.</summary>
        private static Dictionary<string, string> BuildTemplates()
        {
            var names = new List<string>();
            names.AddRange(EquipmentDatabase.GetBuiltInTemplates().Select(e => e.Name));
            names.AddRange(ItemManager.ClassicTemplateNames);
            names.AddRange(NPCItemGenerator.WeaponTemplateNames);
            names.AddRange(NPCItemGenerator.ArmorTemplateNames);
            names.AddRange(LootGenerator.GetWeaponTemplates().Select(t => t.Name));
            names.AddRange(LootGenerator.GetBodyArmorTemplates().Select(t => t.Name));
            names.AddRange(LootGenerator.GetHeadArmorTemplates().Select(t => t.Name));
            names.AddRange(LootGenerator.GetArmsArmorTemplates().Select(t => t.Name));
            names.AddRange(LootGenerator.GetHandsArmorTemplates().Select(t => t.Name));
            names.AddRange(LootGenerator.GetLegsArmorTemplates().Select(t => t.Name));
            names.AddRange(LootGenerator.GetFeetArmorTemplates().Select(t => t.Name));
            names.AddRange(LootGenerator.GetWaistArmorTemplates().Select(t => t.Name));
            names.AddRange(LootGenerator.GetFaceArmorTemplates().Select(t => t.Name));
            names.AddRange(LootGenerator.GetCloakArmorTemplates().Select(t => t.Name));
            names.AddRange(LootGenerator.GetShieldTemplates().Select(t => t.Name));
            names.AddRange(LootGenerator.GetRingTemplates().Select(t => t.Name));
            names.AddRange(LootGenerator.GetNecklaceTemplates().Select(t => t.Name));

            var map = new Dictionary<string, string>(StringComparer.Ordinal);
            foreach (var name in names)
            {
                if (string.IsNullOrEmpty(name) || map.ContainsKey(name)) continue;
                string key = LootGenerator.TemplateLocKey(name);
                if (Loc.HasIn("en", key) && Loc.GetIn("en", key) == name) map[name] = key;
            }
            return map;
        }

        /// <summary>The key a template name shows through, or null when it has none.</summary>
        internal static string? KeyOf(string englishTemplate) =>
            Templates.Value.TryGetValue(englishTemplate, out var key) ? key : null;

        /// <summary>Every template name the display knows, for the tests.</summary>
        internal static IReadOnlyCollection<string> KnownTemplates => Templates.Value.Keys;

        /// <summary>The item's name in the reader's language (the session's, or an open RenderLanguage scope).</summary>
        public static string Display(Equipment? equipment) =>
            equipment == null ? "" : DisplayIn(GameConfig.Language, equipment);

        /// <summary>The item's name in the reader's language.</summary>
        public static string Display(global::Item? item) =>
            item == null ? "" : DisplayIn(GameConfig.Language, item.Name);

        /// <summary>A stored item name in the reader's language.</summary>
        public static string Display(string? storedName) => DisplayIn(GameConfig.Language, storedName);

        /// <summary>A built-in piece is looked up by its EquipmentData id: the template's own name is the
        /// base, whatever else the stored name carries is kept. Any other piece reads its stored name.</summary>
        public static string DisplayIn(string lang, Equipment equipment)
        {
            if (equipment.Id > 0 && !EquipmentDatabase.IsDynamic(equipment.Id) && !EquipmentDatabase.IsShopGenerated(equipment.Id))
            {
                var template = EquipmentDatabase.GetById(equipment.Id);
                if (template != null && template.Name == equipment.Name && KeyOf(template.Name) is string key)
                    return Loc.GetIn(lang, key);
            }
            return DisplayIn(lang, equipment.Name);
        }

        /// <summary>A stored item name in the given language.</summary>
        public static string DisplayIn(string lang, string? storedName)
        {
            if (string.IsNullOrEmpty(storedName)) return storedName ?? "";

            // Peel the enchant parts off the end, keeping them as stored.
            string core = storedName;
            string tail = "";
            while (!Templates.Value.ContainsKey(StripPrefix(core).Base))
            {
                var m = TrailingEnchant.Match(core);
                if (!m.Success || m.Index == 0) return storedName;
                tail = core.Substring(m.Index) + tail;
                core = core.Substring(0, m.Index);
            }

            var (prefix, baseName) = StripPrefix(core);
            string shown = Loc.GetIn(lang, Templates.Value[baseName]);
            if (prefix != null)
                shown = prefix.Value.Template
                    ? Loc.GetIn(lang, prefix.Value.Key, shown)
                    : $"{Loc.GetIn(lang, prefix.Value.Key)} {shown}";
            return shown + tail;
        }

        /// <summary>An item's shown name in a fixed-width list column, padded to the width. A translation
        /// longer than both the column and the stored name is cut with a closing period, so the columns
        /// after it stay where the stored name would leave them. The stored name itself is never cut, so an
        /// English row is exactly what it was.</summary>
        public static string Column(Equipment equipment, int width) => Fit(Display(equipment), equipment.Name, width);

        /// <summary>As Column(Equipment, int), for an inventory item.</summary>
        public static string Column(global::Item item, int width) => Fit(Display(item), item.Name, width);

        internal static string Fit(string shown, string stored, int width)
        {
            int limit = Math.Max(width, stored?.Length ?? 0);
            if (shown.Length > limit) shown = shown.Substring(0, limit - 1).TrimEnd() + ".";
            return shown.PadRight(width);
        }

        private static ((string Stored, string Key, bool Template)? Prefix, string Base) StripPrefix(string name)
        {
            if (Templates.Value.ContainsKey(name)) return (null, name);
            foreach (var p in QualityPrefixKeys)
                if (name.StartsWith(p.Stored, StringComparison.Ordinal) && Templates.Value.ContainsKey(name.Substring(p.Stored.Length)))
                    return (p, name.Substring(p.Stored.Length));
            return (null, name);
        }
    }
}
