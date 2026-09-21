using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;

namespace UsurperRemake.Systems
{
    /// <summary>The kind of piece a template or an item is, for the slot-agreement guard.</summary>
    public enum GearSetSlotKind { None, Weapon, Shield, Head, Body, Arms, Hands, Legs, Feet, Waist, Face, Cloak, Ring, Neck }

    /// <summary>
    /// v1.1.9: which gear set family a piece belongs to.
    ///
    /// The Family stored on an item is authoritative whenever it is present; every item generated
    /// since v1.1.0 carries one. Items that predate v1.1.0, and hand-authored items that never had
    /// one, carry none, and before this they could never count toward a set: on the live server
    /// that was 94 percent of worn gear, so two identical Leather Caps counted or not depending on
    /// when they dropped (player report). For those items only, the family is inferred from the
    /// name. The maintainer approved inferring it on 2026-09-21; the alternative was that pieces
    /// dropped before v1.1.0 never count.
    ///
    /// The hand-authored items that share a set template's exact English name (Leather Cap,
    /// Chain Coif, Steel Helm and the like, which NPCs wear and players only get second-hand)
    /// count too: same name, same set, whoever wears it (maintainer decision, 2026-09-21).
    ///
    /// Nothing inferred is ever written back into a save: a guess stored as Family would become
    /// indistinguishable from a rolled one, and reading it at the point of use keeps the change
    /// reversible by reverting code.
    ///
    /// Inference uses three guards, each closing a different kind of wrong match:
    ///  - Every template is a candidate, set and non-set, from the loot tables, the hand-authored
    ///    equipment and the NPC tables, and the longest match wins. A loot template's candidates
    ///    are every full name the generator can give it, so "Steel Vambraces of Power" is read as
    ///    Steel Vambraces and not as the hand-authored "Vambraces of Power". A family is assigned only when
    ///    the winner is a set template. So "Studded Leather Cap", a built-in that is not a set
    ///    piece, is not read as the Leather Cap it contains.
    ///  - The template must appear as whole words, never inside a longer word.
    ///  - The item's kind of slot must agree with the template's, so a match can never cross from
    ///    a helmet to a pair of boots, in any of the five languages.
    /// Names are matched in every language, because a loot name is localized when it drops. On a
    /// tie between a set and a non-set template of equal length, the non-set one wins: a missing
    /// bonus is a smaller wrong than an unearned one. The one such tie in the shipped tables is
    /// French, where Shadow Cloak and Cloak of Shadows are both "Cape des Ombres": an old French
    /// Shadow Cloak without a stored family is not counted, because its name cannot say which it is.
    /// </summary>
    public static class GearSetFamilyResolver
    {
        private sealed record Candidate(string English, string Form, GearSetSlotKind Kind, bool IsSet);

        private static readonly Lazy<Dictionary<GearSetSlotKind, List<Candidate>>> Catalog = new(BuildCatalog);

        // Most stored names are exactly one generated form, and a candidate equal to the whole name is
        // necessarily the longest match, so an exact lookup settles them without a scan. Ties between
        // equal forms are settled here by the same rule the scan uses.
        private static readonly Lazy<Dictionary<(string, GearSetSlotKind), Candidate>> Exact = new(() =>
        {
            var exact = new Dictionary<(string, GearSetSlotKind), Candidate>(new NameKindComparer());
            foreach (var (kind, list) in Catalog.Value)
                foreach (var c in list)
                    if (!exact.TryGetValue((c.Form, kind), out var had) || (had.IsSet && !c.IsSet))
                        exact[(c.Form, kind)] = c;
            return exact;
        });

        private sealed class NameKindComparer : IEqualityComparer<(string, GearSetSlotKind)>
        {
            public bool Equals((string, GearSetSlotKind) a, (string, GearSetSlotKind) b) =>
                a.Item2 == b.Item2 && string.Equals(a.Item1, b.Item1, StringComparison.OrdinalIgnoreCase);
            public int GetHashCode((string, GearSetSlotKind) k) =>
                HashCode.Combine(StringComparer.OrdinalIgnoreCase.GetHashCode(k.Item1), k.Item2);
        }
        private static readonly ConcurrentDictionary<(string, GearSetSlotKind), string?> Cache = new();
        private const int MaxCached = 20_000;

        /// <summary>The stored family if the item has one; otherwise the family inferred from its name, or null.</summary>
        public static string? FamilyOf(string? storedFamily, string? name, GearSetSlotKind kind)
        {
            if (!string.IsNullOrEmpty(storedFamily)) return storedFamily;
            if (string.IsNullOrWhiteSpace(name) || kind == GearSetSlotKind.None) return null;
            if (Cache.TryGetValue((name, kind), out var known)) return known;
            var family = Infer(name, kind);
            if (Cache.Count < MaxCached) Cache.TryAdd((name, kind), family);   // bounded: names are player-shaped
            return family;
        }

        public static string? FamilyOf(Equipment? e) => e == null ? null : FamilyOf(e.Family, e.Name, KindOf(e));

        public static string? FamilyOf(global::Item? i) => i == null ? null : FamilyOf(i.Family, i.Name, KindOf(i));

        public static GearSetSlotKind KindOf(Equipment e)
        {
            bool shield = e.WeaponType == WeaponType.Shield || e.WeaponType == WeaponType.Buckler
                          || e.WeaponType == WeaponType.TowerShield || e.Handedness == WeaponHandedness.OffHandOnly;
            if (shield) return GearSetSlotKind.Shield;
            return e.Slot switch
            {
                EquipmentSlot.MainHand or EquipmentSlot.OffHand => GearSetSlotKind.Weapon,
                EquipmentSlot.Head => GearSetSlotKind.Head,
                EquipmentSlot.Body => GearSetSlotKind.Body,
                EquipmentSlot.Arms => GearSetSlotKind.Arms,
                EquipmentSlot.Hands => GearSetSlotKind.Hands,
                EquipmentSlot.Legs => GearSetSlotKind.Legs,
                EquipmentSlot.Feet => GearSetSlotKind.Feet,
                EquipmentSlot.Waist => GearSetSlotKind.Waist,
                EquipmentSlot.Face => GearSetSlotKind.Face,
                EquipmentSlot.Cloak => GearSetSlotKind.Cloak,
                EquipmentSlot.LFinger or EquipmentSlot.RFinger => GearSetSlotKind.Ring,
                EquipmentSlot.Neck or EquipmentSlot.Neck2 => GearSetSlotKind.Neck,
                _ => GearSetSlotKind.None,
            };
        }

        public static GearSetSlotKind KindOf(global::Item i) => i.Type switch
        {
            ObjType.Weapon => GearSetSlotKind.Weapon,
            ObjType.Shield => GearSetSlotKind.Shield,
            ObjType.Head => GearSetSlotKind.Head,
            ObjType.Body => GearSetSlotKind.Body,
            ObjType.Arms => GearSetSlotKind.Arms,
            ObjType.Hands => GearSetSlotKind.Hands,
            ObjType.Legs => GearSetSlotKind.Legs,
            ObjType.Feet => GearSetSlotKind.Feet,
            ObjType.Waist => GearSetSlotKind.Waist,
            ObjType.Face => GearSetSlotKind.Face,
            ObjType.Abody => GearSetSlotKind.Cloak,
            ObjType.Fingers => GearSetSlotKind.Ring,
            ObjType.Neck => GearSetSlotKind.Neck,
            ObjType.Magic => (int)i.MagicType switch
            {
                5 => GearSetSlotKind.Ring,
                10 => GearSetSlotKind.Neck,
                9 => GearSetSlotKind.Waist,
                _ => GearSetSlotKind.None,
            },
            _ => GearSetSlotKind.None,
        };

        // A magic shop enchant appends, in English whatever the language, " +N Abc" (the first three
        // letters of the stat), a tag in parentheses such as " (Blessed)" or " (Lifedrinker)", or on
        // the legacy flow a bare " +N"; enchants stack (MagicShopLocation). No template name holds a
        // parenthesis or a plus, so peeling these off the end can never cut into a template.
        private static readonly Lazy<IReadOnlyList<string>> BossPrefixes = new(() => LootGenerator.WorldBossNamePrefixes);

        private static readonly Regex TrailingEnchant = new(@"\s+(?:\([^()]*\)|\+\d+(?:\s+\p{L}{3})?)$", RegexOptions.Compiled);

        private static string? Infer(string name, GearSetSlotKind kind)
        {
            // Exact first, then again after peeling each trailing enchant and a leading world boss
            // prefix, so a decorated name is read as exactly the generated name underneath it; only a
            // name that never reaches one is scanned. The whole name is always tried before a peel,
            // so a template that itself begins with an element word is never cut.
            string probe = name.Trim();
            while (true)
            {
                if (Exact.Value.TryGetValue((probe, kind), out var exact)) return exact.IsSet ? exact.English : null;
                foreach (var prefix in BossPrefixes.Value)
                    if (probe.Length > prefix.Length + 1 && probe.StartsWith(prefix + " ", StringComparison.OrdinalIgnoreCase)
                        && Exact.Value.TryGetValue((probe.Substring(prefix.Length + 1).TrimStart(), kind), out var underBoss))
                        return underBoss.IsSet ? underBoss.English : null;
                var m = TrailingEnchant.Match(probe);
                if (!m.Success || m.Index == 0) break;
                probe = probe.Substring(0, m.Index);
            }
            if (!Catalog.Value.TryGetValue(kind, out var candidates)) return null;
            Candidate? best = null;
            foreach (var c in candidates)
            {
                if (!ContainsWholeWords(probe, c.Form)) continue;
                if (best == null || c.Form.Length > best.Form.Length
                    || (c.Form.Length == best.Form.Length && best.IsSet && !c.IsSet))
                    best = c;
            }
            return best != null && best.IsSet ? best.English : null;
        }

        /// <summary>True when needle occurs in haystack with no letter or digit touching either end.</summary>
        internal static bool ContainsWholeWords(string haystack, string needle)
        {
            if (string.IsNullOrEmpty(needle)) return false;
            int from = 0;
            while (true)
            {
                int at = haystack.IndexOf(needle, from, StringComparison.OrdinalIgnoreCase);
                if (at < 0) return false;
                int end = at + needle.Length;
                bool leftOk = at == 0 || !char.IsLetterOrDigit(haystack[at - 1]);
                bool rightOk = end == haystack.Length || !char.IsLetterOrDigit(haystack[end]);
                if (leftOk && rightOk) return true;
                from = at + 1;
            }
        }

        /// <summary>Every template, from every source, in every language, grouped by the kind of slot it fills.</summary>
        internal static IReadOnlyDictionary<GearSetSlotKind, List<(string English, string Form, bool IsSet)>> DescribeCatalog() =>
            Catalog.Value.ToDictionary(kv => kv.Key, kv => kv.Value.Select(c => (c.English, c.Form, c.IsSet)).ToList());

        private static Dictionary<GearSetSlotKind, List<Candidate>> BuildCatalog()
        {
            // AvailableLanguages reads an array that stays empty until localization loads; if this ran
            // first, the catalog would silently hold English names only and miss every other player.
            if (Loc.AvailableLanguages.Length == 0) Loc.Initialize();
            var languages = Loc.AvailableLanguages.Select(l => l.Code).ToList();

            var catalog = new Dictionary<GearSetSlotKind, List<Candidate>>();
            var seen = new HashSet<(string, GearSetSlotKind)>();
            void Add(string english, GearSetSlotKind kind, IEnumerable<string> forms)
            {
                if (string.IsNullOrWhiteSpace(english) || kind == GearSetSlotKind.None) return;
                if (!seen.Add((english, kind))) return;
                bool isSet = GearSetRegistry.ForFamily(english) != null;
                if (!catalog.TryGetValue(kind, out var list)) catalog[kind] = list = new List<Candidate>();
                foreach (var f in forms.Where(f => !string.IsNullOrWhiteSpace(f)).Distinct(StringComparer.OrdinalIgnoreCase))
                    list.Add(new Candidate(english, f, kind, isSet));
            }

            // The loot tables, first, so a hand-authored item with the same English name merges into
            // the loot template. A loot name is localized when it drops and can carry a rarity, curse,
            // effect or element word, so every name the generator can give a template, in every
            // language, is a candidate. Matching the whole generated name, not only the template,
            // is what keeps "Steel Vambraces of Power" (Steel Vambraces with the Power suffix) from
            // being read as the hand-authored "Vambraces of Power" it ends with.
            void AddLoot(IEnumerable<string> names, GearSetSlotKind kind)
            {
                foreach (var n in names)
                    Add(n, kind, languages.SelectMany(lang => LootGenerator.AllNameFormsFor(n, lang)).Prepend(n));
            }
            AddLoot(LootGenerator.GetWeaponTemplates().Select(t => t.Name), GearSetSlotKind.Weapon);
            AddLoot(LootGenerator.GetBodyArmorTemplates().Select(t => t.Name), GearSetSlotKind.Body);
            AddLoot(LootGenerator.GetHeadArmorTemplates().Select(t => t.Name), GearSetSlotKind.Head);
            AddLoot(LootGenerator.GetArmsArmorTemplates().Select(t => t.Name), GearSetSlotKind.Arms);
            AddLoot(LootGenerator.GetHandsArmorTemplates().Select(t => t.Name), GearSetSlotKind.Hands);
            AddLoot(LootGenerator.GetLegsArmorTemplates().Select(t => t.Name), GearSetSlotKind.Legs);
            AddLoot(LootGenerator.GetFeetArmorTemplates().Select(t => t.Name), GearSetSlotKind.Feet);
            AddLoot(LootGenerator.GetWaistArmorTemplates().Select(t => t.Name), GearSetSlotKind.Waist);
            AddLoot(LootGenerator.GetFaceArmorTemplates().Select(t => t.Name), GearSetSlotKind.Face);
            AddLoot(LootGenerator.GetCloakArmorTemplates().Select(t => t.Name), GearSetSlotKind.Cloak);
            AddLoot(LootGenerator.GetShieldTemplates().Select(t => t.Name), GearSetSlotKind.Shield);
            AddLoot(LootGenerator.GetRingTemplates().Select(t => t.Name), GearSetSlotKind.Ring);
            AddLoot(LootGenerator.GetNecklaceTemplates().Select(t => t.Name), GearSetSlotKind.Neck);

            // The NPC tables and the hand-authored equipment are stored under their English names
            // only, so that is their one candidate form. Shop-generated items are left out on
            // purpose: they are instances with affixes baked into their names, and as candidates
            // they would out-match an old loot drop of the same name and take its family away.
            // They carry Family anyway.
            foreach (var n in NPCItemGenerator.WeaponTemplateNames) Add(n, GearSetSlotKind.Weapon, new[] { n });
            foreach (var n in NPCItemGenerator.ArmorTemplateNames) Add(n, GearSetSlotKind.Body, new[] { n });
            foreach (var e in EquipmentDatabase.GetAll())
            {
                if (EquipmentDatabase.IsDynamic(e.Id) || EquipmentDatabase.IsShopGenerated(e.Id)) continue;
                Add(e.Name, KindOf(e), new[] { e.Name });
            }
            // Modded equipment from GameData/equipment.json is hand-authored too, but its IDs sit in the
            // dynamic range (200000 and up), so the filter above skips it; without it, a modded
            // "Padded Leather Cap" would be read as the Leather Cap it contains.
            foreach (var e in UsurperRemake.Systems.GameDataLoader.CustomEquipment ?? new List<Equipment>())
                if (e != null && !string.IsNullOrWhiteSpace(e.Name)) Add(e.Name, KindOf(e), new[] { e.Name });
            return catalog;
        }
    }
}
