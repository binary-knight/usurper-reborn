using System;
using System.Collections.Generic;
using UsurperRemake.Systems;

/// <summary>
/// Combat message system - generates varied attack descriptions based on damage
/// Messages scale with damage tiers for more immersive combat
/// </summary>
public static class CombatMessages
{
    /// <summary>
    /// Damage tier thresholds (percentages of max HP)
    /// </summary>
    public enum DamageTier
    {
        Miss = 0,       // 0 damage
        Graze = 1,      // 1-10% max HP
        Light = 2,      // 11-20% max HP
        Moderate = 3,   // 21-35% max HP
        Heavy = 4,      // 36-50% max HP
        Severe = 5,     // 51-70% max HP
        Critical = 6,   // 71-90% max HP
        Devastating = 7 // 91%+ max HP
    }

    /// <summary>
    /// Calculate damage tier based on damage and max HP
    /// </summary>
    public static DamageTier GetDamageTier(long damage, long maxHP)
    {
        if (damage <= 0) return DamageTier.Miss;
        if (maxHP <= 0) return DamageTier.Devastating; // Edge case: treat as massive damage

        float percentage = (float)damage / maxHP * 100f;

        return percentage switch
        {
            <= 10 => DamageTier.Graze,
            <= 20 => DamageTier.Light,
            <= 35 => DamageTier.Moderate,
            <= 50 => DamageTier.Heavy,
            <= 70 => DamageTier.Severe,
            <= 90 => DamageTier.Critical,
            _ => DamageTier.Devastating
        };
    }

    /// <summary>
    /// v1.2.5: how many whole-sentence forms each damage tier has, for the player's, an ally's and a
    /// monster's attack (combat.msg.{player|ally|monster}.{tier}.{i}). One form is drawn per message with
    /// the caller's RNG, one draw from the same count the verb lists had before, so seeded fights draw
    /// the same numbers. Before 1.2.5 the sentences were glued from English verbs.
    /// </summary>
    private static readonly Dictionary<DamageTier, int> AttackForms = new()
    {
        [DamageTier.Miss] = 3,
        [DamageTier.Graze] = 4,
        [DamageTier.Light] = 4,
        [DamageTier.Moderate] = 4,
        [DamageTier.Heavy] = 4,
        [DamageTier.Severe] = 4,
        [DamageTier.Critical] = 4,
        [DamageTier.Devastating] = 3
    };

    private static readonly Dictionary<DamageTier, int> MonsterAttackForms = new()
    {
        [DamageTier.Miss] = 3,
        [DamageTier.Graze] = 3,
        [DamageTier.Light] = 3,
        [DamageTier.Moderate] = 3,
        [DamageTier.Heavy] = 3,
        [DamageTier.Severe] = 3,
        [DamageTier.Critical] = 3,
        [DamageTier.Devastating] = 3
    };

    /// <summary>The number of forms a speaker's tier has, for the tests.</summary>
    internal static int FormCount(string who, DamageTier tier) => who == "monster" ? MonsterAttackForms[tier] : AttackForms[tier];

    /// <summary>The key of one attack sentence.</summary>
    internal static string FormKey(string who, DamageTier tier, int index) =>
        $"combat.msg.{who}.{tier.ToString().ToLowerInvariant()}.{index}";

    /// <summary>
    /// Color for damage tier
    /// </summary>
    private static readonly Dictionary<DamageTier, string> DamageColors = new()
    {
        [DamageTier.Miss] = "gray",
        [DamageTier.Graze] = "white",
        [DamageTier.Light] = "yellow",
        [DamageTier.Moderate] = "bright_yellow",
        [DamageTier.Heavy] = "red",
        [DamageTier.Severe] = "bright_red",
        [DamageTier.Critical] = "bright_magenta",
        [DamageTier.Devastating] = "bright_magenta"
    };

    /// <summary>
    /// Generate player attack message. `targetName` is the name as shown (MonsterNames.Display).
    /// </summary>
    public static string GetPlayerAttackMessage(string targetName, long damage, long targetMaxHP, Random? random = null)
    {
        random ??= Random.Shared;
        var tier = GetDamageTier(damage, targetMaxHP);
        int form = random.Next(AttackForms[tier]);
        return tier == DamageTier.Miss
            ? Loc.Get(FormKey("player", tier, form), targetName)
            : Loc.Get(FormKey("player", tier, form), targetName, DamageColors[tier], damage);
    }

    /// <summary>
    /// Generate ally/teammate/companion attack message. v1.2.5: whole sentences per language; the English
    /// third person forms the old verb gluing got wrong ("whiff ates", "strike hards", "DEMOLISHs") read right.
    /// </summary>
    public static string GetAllyAttackMessage(string allyName, string targetName, long damage, long targetMaxHP, Random? random = null)
    {
        random ??= Random.Shared;
        var tier = GetDamageTier(damage, targetMaxHP);
        int form = random.Next(AttackForms[tier]);
        return tier == DamageTier.Miss
            ? Loc.Get(FormKey("ally", tier, form), allyName, targetName)
            : Loc.Get(FormKey("ally", tier, form), allyName, targetName, DamageColors[tier], damage);
    }

    /// <summary>
    /// Generate monster attack message. The raw damage is not shown here: the damage after armor is shown
    /// separately (showing both confused players into thinking they were hit twice).
    /// </summary>
    public static string GetMonsterAttackMessage(string monsterName, string monsterColor, long damage, long playerMaxHP, Random? random = null)
    {
        random ??= Random.Shared;
        var tier = GetDamageTier(damage, playerMaxHP);
        int form = random.Next(MonsterAttackForms[tier]);
        return Loc.Get(FormKey("monster", tier, form), monsterColor, monsterName);
    }

    /// <summary>
    /// v1.2.5: an attack message as rows of at most `width` visible columns (colour markup does not count). A
    /// message that fits is one row, unchanged. A colour open at a break is closed at the end of the row and
    /// opened again at the start of the next, so each row is whole markup.
    /// </summary>
    internal static List<string> Rows(string message, int width = 79)
    {
        var rows = new List<string>();
        var row = new System.Text.StringBuilder();
        int used = 0;
        string? open = null;
        foreach (var word in message.Split(' '))
        {
            int w = System.Text.RegularExpressions.Regex.Replace(word, @"\[/?[a-z_]*\]", "").Length;
            if (used > 0 && used + 1 + w > width)
            {
                if (open != null) row.Append("[/]");
                rows.Add(row.ToString());
                row.Clear();
                used = 0;
                if (open != null) row.Append('[').Append(open).Append(']');
            }
            else if (row.Length > 0) { row.Append(' '); used++; }
            row.Append(word);
            used += w;
            foreach (System.Text.RegularExpressions.Match tag in System.Text.RegularExpressions.Regex.Matches(word, @"\[(/?)([a-z_]*)\]"))
                open = tag.Groups[1].Value == "/" ? null : tag.Groups[2].Value;
        }
        rows.Add(row.ToString());
        return rows;
    }

    /// <summary>
    /// Get death message
    /// </summary>
    public static string GetDeathMessage(string name, string color = "white", Random? random = null)
    {
        int idx = (random ?? Random.Shared).Next(4) + 1;   // v1.1.14: the caller's RNG when given (a seeded test)
        return Loc.Get($"combat.death_msg_{idx}", color, name);
    }

    /// <summary>
    /// Get victory message
    /// </summary>
    public static string GetVictoryMessage(int monstersDefeated)
    {
        if (monstersDefeated == 1)
        {
            return Loc.Get("combat.victory_solo");
        }

        return monstersDefeated switch
        {
            2 => Loc.Get("combat.victory_double"),
            3 => Loc.Get("combat.victory_triple"),
            4 => Loc.Get("combat.victory_quadra"),
            5 => Loc.Get("combat.victory_penta"),
            _ => Loc.Get("combat.victory_many", monstersDefeated)
        };
    }
}
