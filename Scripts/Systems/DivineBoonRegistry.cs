using System;
using System.Collections.Generic;
using System.Linq;

/// <summary>
/// Static registry of all divine boons that immortal player-gods can configure for their followers.
/// Handles budget calculation, config parsing/serialization, effect aggregation, and prose generation.
/// </summary>
public class BoonDefinition
{
    public string Id { get; set; } = "";
    public string Name { get; set; } = "";
    public string Description { get; set; } = "";
    public string[] Alignments { get; set; } = Array.Empty<string>(); // Empty = any alignment
    public int CostPerTier { get; set; }
    public string Category { get; set; } = ""; // "Combat", "Economy", "Utility"

    // Per-tier effect values — index 0 = tier 1, index 1 = tier 2, index 2 = tier 3
    public float[] DamagePercent { get; set; } = { 0, 0, 0 };
    public float[] DefensePercent { get; set; } = { 0, 0, 0 };
    public float[] CritPercent { get; set; } = { 0, 0, 0 };
    public float[] LifestealPercent { get; set; } = { 0, 0, 0 };
    public float[] XPPercent { get; set; } = { 0, 0, 0 };
    public float[] GoldPercent { get; set; } = { 0, 0, 0 };
    public float[] ShopDiscountPercent { get; set; } = { 0, 0, 0 };
    public float[] MaxHPPercent { get; set; } = { 0, 0, 0 };
    public float[] MaxManaPercent { get; set; } = { 0, 0, 0 };
    public float[] FleePercent { get; set; } = { 0, 0, 0 };
    public float[] LuckPercent { get; set; } = { 0, 0, 0 };
    public int[] FlatAttack { get; set; } = { 0, 0, 0 };
    public int[] FlatDefense { get; set; } = { 0, 0, 0 };

    public int MaxTier => 3;

    public bool IsAvailableForAlignment(string alignment)
    {
        if (Alignments.Length == 0) return true; // Any alignment
        return Alignments.Contains(alignment, StringComparer.OrdinalIgnoreCase);
    }

    /// <summary>Get a short effect description for a given tier (1-based)</summary>
    public string GetEffectDescription(int tier)
    {
        int idx = Math.Clamp(tier, 1, 3) - 1;
        var parts = new List<string>();
        if (DamagePercent[idx] > 0) parts.Add($"+{DamagePercent[idx] * 100:0}% damage");
        if (DefensePercent[idx] > 0) parts.Add($"+{DefensePercent[idx] * 100:0}% defense");
        if (CritPercent[idx] > 0) parts.Add($"+{CritPercent[idx] * 100:0}% crit chance");
        if (LifestealPercent[idx] > 0) parts.Add($"+{LifestealPercent[idx] * 100:0}% lifesteal");
        if (XPPercent[idx] > 0) parts.Add($"+{XPPercent[idx] * 100:0}% XP");
        if (GoldPercent[idx] > 0) parts.Add($"+{GoldPercent[idx] * 100:0}% gold");
        if (ShopDiscountPercent[idx] > 0) parts.Add($"{ShopDiscountPercent[idx] * 100:0}% shop discount");
        if (MaxHPPercent[idx] > 0) parts.Add($"+{MaxHPPercent[idx] * 100:0}% max HP");
        if (MaxManaPercent[idx] > 0) parts.Add($"+{MaxManaPercent[idx] * 100:0}% max mana");
        if (FleePercent[idx] > 0) parts.Add($"+{FleePercent[idx] * 100:0}% flee chance");
        if (LuckPercent[idx] > 0) parts.Add($"+{LuckPercent[idx] * 100:0}% luck");
        if (FlatAttack[idx] > 0) parts.Add($"+{FlatAttack[idx]} attack");
        if (FlatDefense[idx] > 0) parts.Add($"+{FlatDefense[idx]} defense");
        return string.Join(", ", parts);
    }
}

/// <summary>Aggregated boon effects cached on a mortal character at runtime</summary>
public class ActiveBoonEffects
{
    public float DamagePercent { get; set; }
    public float DefensePercent { get; set; }
    public float CritPercent { get; set; }
    public float LifestealPercent { get; set; }
    public float XPPercent { get; set; }
    public float GoldPercent { get; set; }
    public float ShopDiscountPercent { get; set; }
    public float MaxHPPercent { get; set; }
    public float MaxManaPercent { get; set; }
    public float FleePercent { get; set; }
    public float LuckPercent { get; set; }
    public int FlatAttack { get; set; }
    public int FlatDefense { get; set; }

    public bool HasAnyEffect =>
        DamagePercent > 0 || DefensePercent > 0 || CritPercent > 0 || LifestealPercent > 0 ||
        XPPercent > 0 || GoldPercent > 0 || ShopDiscountPercent > 0 ||
        MaxHPPercent > 0 || MaxManaPercent > 0 || FleePercent > 0 || LuckPercent > 0 ||
        FlatAttack > 0 || FlatDefense > 0;

    /// <summary>Create a copy with all values multiplied (for prayer bonus)</summary>
    public ActiveBoonEffects Multiply(float multiplier) => new ActiveBoonEffects
    {
        DamagePercent = DamagePercent * multiplier,
        DefensePercent = DefensePercent * multiplier,
        CritPercent = CritPercent * multiplier,
        LifestealPercent = LifestealPercent * multiplier,
        XPPercent = XPPercent * multiplier,
        GoldPercent = GoldPercent * multiplier,
        ShopDiscountPercent = ShopDiscountPercent * multiplier,
        MaxHPPercent = MaxHPPercent * multiplier,
        MaxManaPercent = MaxManaPercent * multiplier,
        FleePercent = FleePercent * multiplier,
        LuckPercent = LuckPercent * multiplier,
        FlatAttack = (int)(FlatAttack * multiplier),
        FlatDefense = (int)(FlatDefense * multiplier)
    };
}

public static class DivineBoonRegistry
{
    public static readonly List<BoonDefinition> AllBoons = new()
    {
        // ── Combat ──
        new BoonDefinition
        {
            Id = "warrior_fury", Name = "Warrior's Fury", Category = "Combat",
            Description = "Increases combat damage",
            Alignments = new[] { "Light", "Balance" }, CostPerTier = 8,
            DamagePercent = new[] { 0.05f, 0.10f, 0.15f }
        },
        new BoonDefinition
        {
            Id = "shadow_strike", Name = "Shadow Strike", Category = "Combat",
            Description = "Increases critical hit chance",
            Alignments = new[] { "Dark" }, CostPerTier = 8,
            CritPercent = new[] { 0.05f, 0.10f, 0.15f }
        },
        new BoonDefinition
        {
            Id = "divine_shield", Name = "Divine Shield", Category = "Combat",
            Description = "Increases damage reduction",
            Alignments = new[] { "Light" }, CostPerTier = 10,
            DefensePercent = new[] { 0.05f, 0.10f, 0.15f }
        },
        new BoonDefinition
        {
            Id = "lifedrain", Name = "Lifedrain", Category = "Combat",
            Description = "Heals a portion of damage dealt",
            Alignments = new[] { "Dark", "Balance" }, CostPerTier = 10,
            LifestealPercent = new[] { 0.03f, 0.06f, 0.10f }
        },
        new BoonDefinition
        {
            Id = "battle_rage", Name = "Battle Rage", Category = "Combat",
            Description = "Flat bonus to attack power",
            Alignments = Array.Empty<string>(), CostPerTier = 6,
            FlatAttack = new[] { 3, 6, 10 }
        },

        // ── Economy ──
        new BoonDefinition
        {
            Id = "golden_touch", Name = "Golden Touch", Category = "Economy",
            Description = "Increases gold from combat",
            Alignments = Array.Empty<string>(), CostPerTier = 6,
            GoldPercent = new[] { 0.05f, 0.10f, 0.15f }
        },
        new BoonDefinition
        {
            Id = "merchants_favor", Name = "Merchant's Favor", Category = "Economy",
            Description = "Reduces shop prices",
            Alignments = new[] { "Light", "Balance" }, CostPerTier = 8,
            ShopDiscountPercent = new[] { 0.03f, 0.06f, 0.10f }
        },
        new BoonDefinition
        {
            Id = "scholars_wisdom", Name = "Scholar's Wisdom", Category = "Economy",
            Description = "Increases experience gained",
            Alignments = Array.Empty<string>(), CostPerTier = 10,
            XPPercent = new[] { 0.05f, 0.10f, 0.15f }
        },

        // ── Utility ──
        new BoonDefinition
        {
            Id = "divine_vitality", Name = "Divine Vitality", Category = "Utility",
            Description = "Increases maximum hit points",
            Alignments = new[] { "Light" }, CostPerTier = 8,
            MaxHPPercent = new[] { 0.05f, 0.10f, 0.15f }
        },
        new BoonDefinition
        {
            Id = "shadow_veil", Name = "Shadow Veil", Category = "Utility",
            Description = "Increases flee chance in combat",
            Alignments = new[] { "Dark" }, CostPerTier = 6,
            FleePercent = new[] { 0.05f, 0.10f, 0.15f }
        },
        new BoonDefinition
        {
            Id = "fortunes_smile", Name = "Fortune's Smile", Category = "Utility",
            Description = "Increases luck and rare loot chance",
            Alignments = Array.Empty<string>(), CostPerTier = 6,
            LuckPercent = new[] { 0.05f, 0.10f, 0.15f }
        },
        new BoonDefinition
        {
            Id = "ironhide", Name = "Ironhide", Category = "Utility",
            Description = "Flat bonus to defense",
            Alignments = new[] { "Balance" }, CostPerTier = 6,
            FlatDefense = new[] { 3, 6, 10 }
        },
        new BoonDefinition
        {
            Id = "mana_well", Name = "Mana Well", Category = "Utility",
            Description = "Increases maximum mana",
            Alignments = Array.Empty<string>(), CostPerTier = 8,
            MaxManaPercent = new[] { 0.05f, 0.10f, 0.15f }
        },
    };

    private static readonly Dictionary<string, BoonDefinition> _boonLookup =
        AllBoons.ToDictionary(b => b.Id, StringComparer.OrdinalIgnoreCase);

    /// <summary>Look up a boon by ID</summary>
    public static BoonDefinition GetBoon(string id) =>
        _boonLookup.TryGetValue(id, out var boon) ? boon : null;

    /// <summary>Parse a boon config string like "warrior_fury:2,scholars_wisdom:1"</summary>
    public static List<(string boonId, int tier)> ParseConfig(string config)
    {
        var result = new List<(string, int)>();
        if (string.IsNullOrWhiteSpace(config)) return result;

        foreach (var part in config.Split(',', StringSplitOptions.RemoveEmptyEntries))
        {
            var pair = part.Trim().Split(':');
            if (pair.Length == 2 && int.TryParse(pair[1], out int tier) && tier >= 1 && tier <= 3)
            {
                if (_boonLookup.ContainsKey(pair[0].Trim()))
                    result.Add((pair[0].Trim(), tier));
            }
        }
        return result;
    }

    /// <summary>Serialize a boon list back to config string</summary>
    public static string SerializeConfig(List<(string boonId, int tier)> boons)
    {
        if (boons == null || boons.Count == 0) return "";
        return string.Join(",", boons.Select(b => $"{b.boonId}:{b.tier}"));
    }

    /// <summary>Calculate total points spent on a config</summary>
    public static int CalculateSpent(string config)
    {
        int total = 0;
        foreach (var (boonId, tier) in ParseConfig(config))
        {
            var boon = GetBoon(boonId);
            if (boon != null) total += boon.CostPerTier * tier;
        }
        return total;
    }

    /// <summary>Calculate total budget for a god</summary>
    public static int CalculateBudget(int godLevel, int believerCount)
    {
        int baseBudget = Math.Max(1, godLevel) * GameConfig.GodBoonBudgetPerLevel;
        int concentration = Math.Max(0, GameConfig.GodBoonConcentrationMax - believerCount * GameConfig.GodBoonConcentrationPerBeliever);
        return baseBudget + concentration;
    }

    /// <summary>Aggregate all boon effects from a config string</summary>
    public static ActiveBoonEffects CalculateEffects(string config)
    {
        var effects = new ActiveBoonEffects();
        if (string.IsNullOrWhiteSpace(config)) return effects;

        foreach (var (boonId, tier) in ParseConfig(config))
        {
            var boon = GetBoon(boonId);
            if (boon == null) continue;
            int idx = tier - 1; // 0-based index

            effects.DamagePercent += boon.DamagePercent[idx];
            effects.DefensePercent += boon.DefensePercent[idx];
            effects.CritPercent += boon.CritPercent[idx];
            effects.LifestealPercent += boon.LifestealPercent[idx];
            effects.XPPercent += boon.XPPercent[idx];
            effects.GoldPercent += boon.GoldPercent[idx];
            effects.ShopDiscountPercent += boon.ShopDiscountPercent[idx];
            effects.MaxHPPercent += boon.MaxHPPercent[idx];
            effects.MaxManaPercent += boon.MaxManaPercent[idx];
            effects.FleePercent += boon.FleePercent[idx];
            effects.LuckPercent += boon.LuckPercent[idx];
            effects.FlatAttack += boon.FlatAttack[idx];
            effects.FlatDefense += boon.FlatDefense[idx];
        }
        return effects;
    }

    /// <summary>Get boons available for a given alignment</summary>
    public static List<BoonDefinition> GetAvailableBoons(string alignment)
    {
        return AllBoons.Where(b => b.IsAvailableForAlignment(alignment)).ToList();
    }

    /// <summary>Generate a prose description of a god's boon offering</summary>
    public static string GenerateDescription(string config, string alignment)
    {
        var boons = ParseConfig(config);
        if (boons.Count == 0)
            return GetAlignmentFlavor(alignment) + " This deity has not yet configured their divine favors.";

        // Group by category
        var combat = new List<string>();
        var economy = new List<string>();
        var utility = new List<string>();

        foreach (var (boonId, tier) in boons)
        {
            var boon = GetBoon(boonId);
            if (boon == null) continue;
            string tierLabel = tier switch { 1 => "minor", 2 => "moderate", 3 => "powerful", _ => "minor" };
            string phrase = boon.Id switch
            {
                "warrior_fury" => $"{tierLabel} battle fury",
                "shadow_strike" => $"{tierLabel} deadly precision",
                "divine_shield" => $"{tierLabel} divine protection",
                "lifedrain" => $"{tierLabel} life-draining power",
                "battle_rage" => $"{tierLabel} combat prowess",
                "golden_touch" => $"{tierLabel} golden fortune",
                "merchants_favor" => $"{tierLabel} merchant connections",
                "scholars_wisdom" => $"{tierLabel} scholarly insight",
                "divine_vitality" => $"{tierLabel} divine vitality",
                "shadow_veil" => $"{tierLabel} shadow concealment",
                "fortunes_smile" => $"{tierLabel} lucky fortune",
                "ironhide" => $"{tierLabel} iron resilience",
                "mana_well" => $"{tierLabel} arcane reserves",
                _ => $"{tierLabel} blessing"
            };

            switch (boon.Category)
            {
                case "Combat": combat.Add(phrase); break;
                case "Economy": economy.Add(phrase); break;
                case "Utility": utility.Add(phrase); break;
            }
        }

        var parts = new List<string>();
        if (combat.Count > 0) parts.Add("empowers followers with " + string.Join(" and ", combat));
        if (economy.Count > 0) parts.Add("bestows " + string.Join(" and ", economy));
        if (utility.Count > 0) parts.Add("grants " + string.Join(" and ", utility));

        string flavor = GetAlignmentFlavor(alignment);
        string body = string.Join(", and ", parts);
        // Capitalize first letter of body
        if (body.Length > 0) body = char.ToUpper(body[0]) + body.Substring(1);
        return $"{flavor} {body}.";
    }

    /// <summary>Get a short flavor phrase for a god's alignment</summary>
    private static string GetAlignmentFlavor(string alignment)
    {
        return alignment?.ToLower() switch
        {
            "light" => "A radiant spirit of virtue and protection.",
            "dark" => "A shadow spirit of cunning and power.",
            "balance" => "A spirit of harmony who walks between light and dark.",
            _ => "A mysterious divine presence."
        };
    }

    /// <summary>Generate a summary of active boon effects for display</summary>
    public static List<string> GetEffectSummaryLines(string config)
    {
        var lines = new List<string>();
        foreach (var (boonId, tier) in ParseConfig(config))
        {
            var boon = GetBoon(boonId);
            if (boon == null) continue;
            string tierStr = tier switch { 1 => "I", 2 => "II", 3 => "III", _ => "" };
            lines.Add($"{boon.Name} {tierStr} — {boon.GetEffectDescription(tier)}");
        }
        return lines;
    }
}
