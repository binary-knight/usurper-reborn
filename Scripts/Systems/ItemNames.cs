using System;
using System.Collections.Concurrent;
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
    /// enchant removal).
    ///
    /// Rolled loot (LootGenerator.BuildItemName) is stored in English since 1.2.5: the English template name
    /// with at most one English word, a rarity or curse word in front, an effect word in front or behind, and a
    /// world boss drop's element word before all of it. Each part is shown through its key and joined as the
    /// generator joins them, so a reader sees exactly the name the drop would have had in their language. The
    /// item's stored Family (its English template) settles which template a name is built on; a bare name
    /// is read with the whole template winning over a split. A world boss element word stays English, as the
    /// generator has always written it.
    ///
    /// A stored name that is not English underneath (loot rolled in another language before 1.2.5, a modded
    /// item, any other name) is shown as stored: translating it back is not lossless.
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
            // v1.2.5: the arena and tournament champions' themed drops (stored under these English names)
            names.AddRange(UsurperRemake.Data.GauntletChampionData.Champions.Select(c => c.Drop.ItemName));
            names.AddRange(UsurperRemake.Data.HonorTournamentData.Champions.Select(c => c.Drop.ItemName));
            // v1.2.5: the starting weapons character creation gives (stored under these English names)
            names.AddRange(Enum.GetValues<CharacterClass>().Select(c => CharacterCreationSystem.StarterWeapon(c).name));

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
            equipment == null ? "" : Recorded(DisplayIn(GameConfig.Language, equipment), lang => DisplayIn(lang, equipment));

        /// <summary>The item's name in the reader's language.</summary>
        public static string Display(global::Item? item) =>
            item == null ? "" : Recorded(DisplayIn(GameConfig.Language, item), lang => DisplayIn(lang, item));

        /// <summary>A stored item name in the reader's language.</summary>
        public static string Display(string? storedName) =>
            Recorded(DisplayIn(GameConfig.Language, storedName), lang => DisplayIn(lang, storedName));

        /// <summary>v1.2.5: a shown name written while combat output is recorded for group members who read
        /// another language (Loc.BeginRecording) is noted with how to show it in theirs, so a recorded line that
        /// carries it as an argument is re-rendered with their own item name. It is used only as an argument,
        /// never replaced as free text.</summary>
        private static string Recorded(string shown, Func<string, string> renderIn)
        {
            Loc.RecordArgument(shown, renderIn);
            return shown;
        }

        /// <summary>The item's name in the given language.</summary>
        public static string DisplayIn(string lang, global::Item item) => DisplayIn(lang, item.Name, item.Family);

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
            return DisplayIn(lang, equipment.Name, equipment.Family);
        }

        /// <summary>A stored item name in the given language. `family` is the item's stored English
        /// template (Item.Family, Equipment.Family), when it has one.</summary>
        public static string DisplayIn(string lang, string? storedName, string? family = null)
        {
            if (string.IsNullOrEmpty(storedName)) return storedName ?? "";

            // Peel the enchant parts off the end, keeping them as stored.
            string core = storedName;
            string tail = "";
            while (true)
            {
                var shown = ShowCore(lang, core, family);
                if (shown != null) return shown + tail;
                var m = TrailingEnchant.Match(core);
                if (!m.Success || m.Index == 0) return storedName;
                tail = core.Substring(m.Index) + tail;
                core = core.Substring(0, m.Index);
            }
        }

        /// <summary>The name without enchant parts in the given language, or null when it is not a name the
        /// tables or the loot generator write in English.</summary>
        private static string? ShowCore(string lang, string core, string? family)
        {
            // A drop built on its stored template, read against that template's forms only.
            if (!string.IsNullOrEmpty(family) && KeyOf(family) is string familyKey
                && FormsOfBase(familyKey).TryGetValue(core, out var onFamily))
                return Render(lang, onFamily);

            // A table name, with an NPC quality prefix or none.
            var (prefix, baseName) = StripPrefix(core);
            if (Templates.Value.TryGetValue(baseName, out var key))
            {
                string shown = Loc.GetIn(lang, key);
                if (prefix != null)
                    shown = prefix.Value.Template
                        ? Loc.GetIn(lang, prefix.Value.Key, shown)
                        : $"{Loc.GetIn(lang, prefix.Value.Key)} {shown}";
                return shown;
            }

            // Any loot form, the whole template winning over a split.
            if (LootForms.Value.TryGetValue(core, out var form)) return Render(lang, form);

            // A world boss drop: its element word, English as written, before a loot form.
            foreach (var element in LootGenerator.WorldBossNamePrefixes)
            {
                if (core.Length <= element.Length + 1 || !core.StartsWith(element + " ", StringComparison.Ordinal)) continue;
                string rest = core.Substring(element.Length + 1);
                if (!string.IsNullOrEmpty(family) && KeyOf(family) is string fk && FormsOfBase(fk).TryGetValue(rest, out var bossOnFamily))
                    return $"{element} {Render(lang, bossOnFamily)}";
                if (LootForms.Value.TryGetValue(rest, out var bossForm))
                    return $"{element} {Render(lang, bossForm)}";
            }
            return null;
        }

        // ---------- rolled loot ----------

        internal enum WordAt { None, Before, After }

        /// <summary>One way the loot generator builds a name: a base key and at most one word key around it.</summary>
        internal readonly record struct LootForm(string BaseKey, string? WordKey, WordAt At);

        private static string Render(string lang, LootForm form)
        {
            string b = Loc.GetIn(lang, form.BaseKey);
            return form.At switch
            {
                WordAt.Before => $"{Loc.GetIn(lang, form.WordKey!)} {b}",
                WordAt.After => $"{b} {Loc.GetIn(lang, form.WordKey!)}",
                _ => b,
            };
        }

        /// <summary>The words a drop's name can start with for its rarity or curse (LootGenerator).</summary>
        private static IEnumerable<string> RarityWordKeys() =>
            Enum.GetValues(typeof(LootGenerator.ItemRarity)).Cast<LootGenerator.ItemRarity>()
                .Select(LootGenerator.RarityPrefixKey).OfType<string>()
                .Prepend("item.rarity.cursed").Distinct();

        /// <summary>The slot words a drop with no template is named by (LootGenerator.CreateBasicWeapon/Armor).</summary>
        private static readonly string[] SlotWordKeys =
        {
            "item.slot.weapon", "item.slot.helm", "item.slot.armguards", "item.slot.gauntlets", "item.slot.greaves",
            "item.slot.boots", "item.slot.belt", "item.slot.mask", "item.slot.cloak", "item.slot.armor",
        };

        /// <summary>Every form the generator can write on one base, by its English text, as BuildItemName
        /// builds them: bare, a rarity or curse word in front, an effect word in front or behind.</summary>
        private static IEnumerable<(string English, LootForm Form)> FormsOn(string baseKey, bool effects)
        {
            string b = Loc.GetIn("en", baseKey);
            yield return (b, new LootForm(baseKey, null, WordAt.None));
            foreach (var w in RarityWordKeys())
                yield return ($"{Loc.GetIn("en", w)} {b}", new LootForm(baseKey, w, WordAt.Before));
            if (!effects) yield break;
            foreach (LootGenerator.SpecialEffect e in Enum.GetValues(typeof(LootGenerator.SpecialEffect)))
            {
                if (e == LootGenerator.SpecialEffect.None) continue;
                string pre = LootGenerator.EffectWordKey(e, "prefix"), suf = LootGenerator.EffectWordKey(e, "suffix");
                yield return ($"{Loc.GetIn("en", pre)} {b}", new LootForm(baseKey, pre, WordAt.Before));
                yield return ($"{b} {Loc.GetIn("en", suf)}", new LootForm(baseKey, suf, WordAt.After));
            }
        }

        private static readonly ConcurrentDictionary<string, Dictionary<string, LootForm>> FamilyForms = new();

        private static Dictionary<string, LootForm> FormsOfBase(string baseKey) =>
            FamilyForms.GetOrAdd(baseKey, k =>
            {
                var map = new Dictionary<string, LootForm>(StringComparer.Ordinal);
                foreach (var (english, form) in FormsOn(k, effects: true)) map.TryAdd(english, form);
                return map;
            });

        /// <summary>The loot templates that have their key, English as stored.</summary>
        internal static IEnumerable<string> LootTemplates() =>
            LootGenerator.GetWeaponTemplates().Select(t => t.Name)
                .Concat(LootGenerator.GetBodyArmorTemplates().Select(t => t.Name))
                .Concat(LootGenerator.GetHeadArmorTemplates().Select(t => t.Name))
                .Concat(LootGenerator.GetArmsArmorTemplates().Select(t => t.Name))
                .Concat(LootGenerator.GetHandsArmorTemplates().Select(t => t.Name))
                .Concat(LootGenerator.GetLegsArmorTemplates().Select(t => t.Name))
                .Concat(LootGenerator.GetFeetArmorTemplates().Select(t => t.Name))
                .Concat(LootGenerator.GetWaistArmorTemplates().Select(t => t.Name))
                .Concat(LootGenerator.GetFaceArmorTemplates().Select(t => t.Name))
                .Concat(LootGenerator.GetCloakArmorTemplates().Select(t => t.Name))
                .Concat(LootGenerator.GetShieldTemplates().Select(t => t.Name))
                .Concat(LootGenerator.GetRingTemplates().Select(t => t.Name))
                .Concat(LootGenerator.GetNecklaceTemplates().Select(t => t.Name))
                .Where(n => KeyOf(n) != null)
                .Distinct(StringComparer.Ordinal);

        private static readonly Lazy<Dictionary<string, LootForm>> LootForms = new(() =>
        {
            // Bare templates first, so a name that is a whole template is read as that template.
            var map = new Dictionary<string, LootForm>(StringComparer.Ordinal);
            var keys = LootTemplates().Select(n => KeyOf(n)!).ToList();
            foreach (var k in keys) map.TryAdd(Loc.GetIn("en", k), new LootForm(k, null, WordAt.None));
            foreach (var k in keys)
                foreach (var (english, form) in FormsOn(k, effects: true)) map.TryAdd(english, form);
            foreach (var k in SlotWordKeys)
                foreach (var (english, form) in FormsOn(k, effects: false)) map.TryAdd(english, form);
            return map;
        });

        /// <summary>For the tests: what an English loot form is read as without a stored Family.</summary>
        internal static bool TryLootForm(string english, out LootForm form) => LootForms.Value.TryGetValue(english, out form);

        /// <summary>For the tests: what an English loot form is read as on a stored Family.</summary>
        internal static bool TryLootFormOn(string family, string english, out LootForm form)
        {
            form = default;
            return KeyOf(family) is string k && FormsOfBase(k).TryGetValue(english, out form);
        }

        /// <summary>
        /// v1.2.5: the name a cleansed cursed item gets at the Magic Shop. The curse word is read in any of the
        /// five languages (an item rolled in another language before 1.2.5 carries that language's word) and
        /// replaced by the same language's purified word, so the name stays in one language; a name that does
        /// not start with a curse word is kept.
        /// </summary>
        public static string PurifiedName(string name)
        {
            if (string.IsNullOrEmpty(name)) return name ?? "";
            foreach (var lang in new[] { "en", "es", "fr", "hu", "it" })
            {
                string cursed = Loc.GetIn(lang, "item.rarity.cursed") + " ";
                if (name.StartsWith(cursed, StringComparison.Ordinal) && name.Length > cursed.Length)
                    return Loc.GetIn(lang, LootGenerator.EffectWordKey(LootGenerator.SpecialEffect.PoisonResist, "prefix"))
                           + " " + name.Substring(cursed.Length);
            }
            return name;
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
