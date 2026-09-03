using System;
using System.Collections.Generic;
using System.Linq;

namespace UsurperRemake.Systems
{
    /// <summary>Character fields a gear set bonus can add to. Only fields that
    /// Equipment.ApplyToCharacter already touches; crit, life steal and regen live in
    /// the lazy equipment getters and are left for a later slice.</summary>
    public enum GearSetStat { MaxHP, MaxMana, Strength, Dexterity, Constitution, Intelligence, Wisdom, Charisma, Defence, Agility, ArmPow, WeapPow }

    public readonly record struct GearSetBonus(GearSetStat Stat, int Amount);

    public sealed class GearSetTier
    {
        public int Pieces { get; }
        public IReadOnlyList<GearSetBonus> Bonuses { get; }
        public GearSetTier(int pieces, params GearSetBonus[] bonuses) { Pieces = pieces; Bonuses = bonuses; }
    }

    public sealed class GearSet
    {
        public string Id { get; }
        /// <summary>English template names (Item.Family / Equipment.Family) that belong to the set.</summary>
        public IReadOnlyList<string> Families { get; }
        public IReadOnlyList<GearSetTier> Tiers { get; }
        public int MaxPieces => Tiers[^1].Pieces;
        public string NameKey => $"item.set.{Id}.name";
        public GearSet(string id, string[] families, params GearSetTier[] tiers) { Id = id; Families = families; Tiers = tiers; }
    }

    /// <summary>
    /// v1.1 gear set bonuses. Membership keys on the English template family stored on
    /// each generated item at creation (never on the localized display name, and never
    /// on a prefix parse: "Forged-Thread Cape" and "Cloak of Shadows" are not set pieces).
    /// Bonuses are applied in Character.RecalculateStats after the equipped-item loop,
    /// for every character: NPCs, companions, echoes and PvP snapshots wear the same
    /// families and get the same bonuses (maintainer decision, 2026-09-03).
    /// Numbers are a first pass, tuned per level band, and are meant to be revisited
    /// with live data: modest at two pieces, meaningful at four, build-defining at six.
    /// The block runs before the Constitution-to-HP line, so a Constitution bonus also
    /// raises MaxHP through that line; the MaxHP figures below are the direct part only.
    /// Sets not yet covered: Runed, Plate, Titan's, Dragon, Holy (levels 50-100).
    /// </summary>
    public static class GearSetRegistry
    {
        private static GearSetBonus B(GearSetStat s, int a) => new(s, a);

        public static readonly IReadOnlyList<GearSet> Sets = new List<GearSet>
        {
            new GearSet("leather", new[] { "Leather Armor", "Leather Belt", "Leather Boots", "Leather Bracers", "Leather Cap", "Leather Cord", "Leather Face Guard", "Leather Gloves", "Leather Leggings", "Leather Shield" },
                new GearSetTier(2, B(GearSetStat.MaxHP, 10)),
                new GearSetTier(4, B(GearSetStat.Dexterity, 2), B(GearSetStat.Agility, 2)),
                new GearSetTier(6, B(GearSetStat.Defence, 4), B(GearSetStat.MaxHP, 25))),
            new GearSet("chain", new[] { "Chain Belt", "Chain Boots", "Chain Coif", "Chain Gauntlets", "Chain Leggings", "Chain Mail", "Chain Shirt", "Chain Sleeves" },
                new GearSetTier(2, B(GearSetStat.ArmPow, 2)),
                new GearSetTier(4, B(GearSetStat.Constitution, 3), B(GearSetStat.MaxHP, 20)),
                new GearSetTier(6, B(GearSetStat.ArmPow, 5), B(GearSetStat.Strength, 3))),
            new GearSet("silk", new[] { "Silk Arm Wraps", "Silk Handwraps", "Silk Slippers", "Silk Trousers", "Silk Vestments" },
                new GearSetTier(2, B(GearSetStat.MaxMana, 15)),
                new GearSetTier(4, B(GearSetStat.Intelligence, 3), B(GearSetStat.Wisdom, 3)),
                new GearSetTier(5, B(GearSetStat.MaxMana, 40), B(GearSetStat.Defence, 2))),
            new GearSet("shadow", new[] { "Shadow Bow", "Shadow Bracers", "Shadow Cloak", "Shadow Fang", "Shadow Handwraps", "Shadow Hood", "Shadow Leather", "Shadow Leggings", "Shadow Mask", "Shadow Treads" },
                new GearSetTier(2, B(GearSetStat.Dexterity, 3)),
                new GearSetTier(4, B(GearSetStat.Agility, 3), B(GearSetStat.WeapPow, 2)),
                new GearSetTier(6, B(GearSetStat.Dexterity, 5), B(GearSetStat.Agility, 5), B(GearSetStat.WeapPow, 4))),
            new GearSet("steel", new[] { "Steel Buckler", "Steel Faceplate", "Steel Gauntlets", "Steel Girdle", "Steel Greatsword", "Steel Greaves", "Steel Helm", "Steel Sabatons", "Steel Shield", "Steel Vambraces" },
                new GearSetTier(2, B(GearSetStat.ArmPow, 3)),
                new GearSetTier(4, B(GearSetStat.Strength, 4)),
                new GearSetTier(6, B(GearSetStat.ArmPow, 6), B(GearSetStat.MaxHP, 40))),
            new GearSet("reinforced", new[] { "Reinforced Belt", "Reinforced Boots", "Reinforced Bracers", "Reinforced Buckler", "Reinforced Chain", "Reinforced Cloak", "Reinforced Gi", "Reinforced Gloves", "Reinforced Helm", "Reinforced Leather", "Reinforced Leggings" },
                new GearSetTier(2, B(GearSetStat.ArmPow, 3)),
                new GearSetTier(4, B(GearSetStat.MaxHP, 30), B(GearSetStat.Constitution, 2)),
                new GearSetTier(6, B(GearSetStat.Defence, 6), B(GearSetStat.ArmPow, 5))),
            new GearSet("forged", new[] { "Forged Armguards", "Forged Boots", "Forged Brigandine", "Forged Buckler", "Forged Gauntlets", "Forged Girdle", "Forged Greaves", "Forged Helm", "Forged Mace", "Forged Visor" },
                new GearSetTier(2, B(GearSetStat.WeapPow, 3)),
                new GearSetTier(4, B(GearSetStat.Strength, 4), B(GearSetStat.Dexterity, 2)),
                new GearSetTier(6, B(GearSetStat.WeapPow, 6), B(GearSetStat.Defence, 4))),
            new GearSet("mithril", new[] { "Mithril Armguards", "Mithril Belt", "Mithril Boots", "Mithril Gloves", "Mithril Helm", "Mithril Legguards", "Mithril Ring", "Mithril Torc", "Mithril Visor", "Mithril Weave Cloak" },
                new GearSetTier(2, B(GearSetStat.ArmPow, 4), B(GearSetStat.MaxHP, 20)),
                new GearSetTier(4, B(GearSetStat.Strength, 3), B(GearSetStat.Dexterity, 3), B(GearSetStat.Constitution, 3)),
                new GearSetTier(6, B(GearSetStat.ArmPow, 8), B(GearSetStat.Defence, 8), B(GearSetStat.MaxHP, 60))),
        };

        private static readonly Dictionary<string, GearSet> ByFamily = Sets
            .SelectMany(s => s.Families.Select(f => (f, s)))
            .ToDictionary(p => p.f, p => p.s, StringComparer.OrdinalIgnoreCase);

        public static GearSet? ForFamily(string? family)
            => string.IsNullOrEmpty(family) ? null : ByFamily.TryGetValue(family, out var s) ? s : null;

        /// <summary>Equipped piece count per set. Counts slots, so two Mithril rings count twice; the 2/4/6 thresholds absorb that.</summary>
        public static List<(GearSet Set, int Count)> CountEquipped(Character c)
        {
            var counts = new Dictionary<string, int>();
            foreach (var kvp in c.EquippedItems)
            {
                if (kvp.Value <= 0) continue;
                var set = ForFamily(EquipmentDatabase.GetById(kvp.Value)?.Family);
                if (set == null) continue;
                counts[set.Id] = counts.GetValueOrDefault(set.Id) + 1;
            }
            return Sets.Where(s => counts.ContainsKey(s.Id)).Select(s => (s, counts[s.Id])).ToList();
        }

        /// <summary>Apply every reached tier. Called from Character.RecalculateStats after stats were reset to base and equipment applied.</summary>
        public static void Apply(Character c)
        {
            foreach (var (set, count) in CountEquipped(c))
                foreach (var tier in set.Tiers)
                    if (count >= tier.Pieces)
                        foreach (var b in tier.Bonuses) AddStat(c, b);
        }

        private static void AddStat(Character c, GearSetBonus b)
        {
            switch (b.Stat)
            {
                case GearSetStat.MaxHP: c.MaxHP += b.Amount; break;
                case GearSetStat.MaxMana: c.MaxMana += b.Amount; break;
                case GearSetStat.Strength: c.Strength += b.Amount; break;
                case GearSetStat.Dexterity: c.Dexterity += b.Amount; break;
                case GearSetStat.Constitution: c.Constitution += b.Amount; break;
                case GearSetStat.Intelligence: c.Intelligence += b.Amount; break;
                case GearSetStat.Wisdom: c.Wisdom += b.Amount; break;
                case GearSetStat.Charisma: c.Charisma += b.Amount; break;
                case GearSetStat.Defence: c.Defence += b.Amount; break;
                case GearSetStat.Agility: c.Agility += b.Amount; break;
                case GearSetStat.ArmPow: c.ArmPow += b.Amount; break;
                case GearSetStat.WeapPow: c.WeapPow += b.Amount; break;
            }
        }

        public static string SetName(GearSet set) => Loc.Get(set.NameKey);

        public static string DescribeTier(GearSetTier tier)
            => string.Join(", ", tier.Bonuses.Select(b => Loc.Get($"item.set.bonus.{b.Stat.ToString().ToLowerInvariant()}", b.Amount)));

        /// <summary>Lines for the equipment overview and /gear: one per set the character wears, with every tier marked active or not.</summary>
        public static List<(string Text, bool Active)> DescribeActive(Character c)
        {
            var lines = new List<(string, bool)>();
            foreach (var (set, count) in CountEquipped(c))
            {
                lines.Add((Loc.Get("item.set.progress", SetName(set), count, set.MaxPieces), true));
                foreach (var tier in set.Tiers)
                    lines.Add((Loc.Get("item.set.tier", tier.Pieces, DescribeTier(tier)), count >= tier.Pieces));
            }
            return lines;
        }
    }
}
