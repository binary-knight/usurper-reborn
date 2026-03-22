using System;
using System.Collections.Generic;

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
    /// Player attack messages
    /// </summary>
    private static readonly Dictionary<DamageTier, List<string>> PlayerAttackVerbs = new()
    {
        [DamageTier.Miss] = new List<string> { "miss", "whiff at", "swing wildly at" },
        [DamageTier.Graze] = new List<string> { "graze", "scratch", "nick", "clip" },
        [DamageTier.Light] = new List<string> { "hit", "strike", "slash", "cut" },
        [DamageTier.Moderate] = new List<string> { "wound", "strike hard", "cleave", "rend" },
        [DamageTier.Heavy] = new List<string> { "smash", "crush", "savage", "maul" },
        [DamageTier.Severe] = new List<string> { "devastate", "shatter", "pulverize", "obliterate" },
        [DamageTier.Critical] = new List<string> { "DEMOLISH", "EVISCERATE", "ANNIHILATE", "DESTROY" },
        [DamageTier.Devastating] = new List<string> { "***OBLITERATE***", "***DEVASTATE***", "***ANNIHILATE***" }
    };

    /// <summary>
    /// Monster attack messages
    /// </summary>
    private static readonly Dictionary<DamageTier, List<string>> MonsterAttackVerbs = new()
    {
        [DamageTier.Miss] = new List<string> { "misses you", "swings wildly", "attacks but misses" },
        [DamageTier.Graze] = new List<string> { "grazes you", "scratches you", "barely touches you" },
        [DamageTier.Light] = new List<string> { "hits you", "strikes you", "attacks you" },
        [DamageTier.Moderate] = new List<string> { "wounds you", "strikes you hard", "slashes you deeply" },
        [DamageTier.Heavy] = new List<string> { "smashes you", "crushes you", "mauls you" },
        [DamageTier.Severe] = new List<string> { "devastates you", "savages you", "rips into you" },
        [DamageTier.Critical] = new List<string> { "DEMOLISHES you", "EVISCERATES you", "RIPS you apart" },
        [DamageTier.Devastating] = new List<string> { "***OBLITERATES you***", "***DESTROYS you***", "***ANNIHILATES you***" }
    };

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
    /// Generate player attack message
    /// </summary>
    public static string GetPlayerAttackMessage(string targetName, long damage, long targetMaxHP, Random? random = null)
    {
        random ??= Random.Shared;
        var tier = GetDamageTier(damage, targetMaxHP);

        if (tier == DamageTier.Miss)
        {
            var verb = PlayerAttackVerbs[tier][random.Next(PlayerAttackVerbs[tier].Count)];
            return $"You {verb} {targetName}!";
        }

        var attackVerb = PlayerAttackVerbs[tier][random.Next(PlayerAttackVerbs[tier].Count)];
        var color = DamageColors[tier];

        return $"You {attackVerb} {targetName} for [{color}]{damage}[/] damage!";
    }

    /// <summary>
    /// Generate ally/teammate/companion attack message
    /// </summary>
    public static string GetAllyAttackMessage(string allyName, string targetName, long damage, long targetMaxHP, Random? random = null)
    {
        random ??= Random.Shared;
        var tier = GetDamageTier(damage, targetMaxHP);

        if (tier == DamageTier.Miss)
        {
            var verb = PlayerAttackVerbs[tier][random.Next(PlayerAttackVerbs[tier].Count)];
            return $"[bright_cyan]{allyName}[/] {verb}es {targetName}!";
        }

        var attackVerb = PlayerAttackVerbs[tier][random.Next(PlayerAttackVerbs[tier].Count)];
        var color = DamageColors[tier];

        // Conjugate verb for third person (add s/es)
        string thirdPersonVerb = attackVerb;
        if (!attackVerb.Contains("*")) // Don't modify emphasis verbs like ***ANNIHILATE***
        {
            if (attackVerb.EndsWith("sh") || attackVerb.EndsWith("ch"))
                thirdPersonVerb = attackVerb + "es";
            else if (attackVerb.EndsWith("e"))
                thirdPersonVerb = attackVerb + "s";
            else
                thirdPersonVerb = attackVerb + "s";
        }

        return $"[bright_cyan]{allyName}[/] {thirdPersonVerb} {targetName} for [{color}]{damage}[/] damage!";
    }

    /// <summary>
    /// Generate monster attack message
    /// </summary>
    public static string GetMonsterAttackMessage(string monsterName, string monsterColor, long damage, long playerMaxHP, Random? random = null)
    {
        random ??= Random.Shared;
        var tier = GetDamageTier(damage, playerMaxHP);

        if (tier == DamageTier.Miss)
        {
            var verb = MonsterAttackVerbs[tier][random.Next(MonsterAttackVerbs[tier].Count)];
            return $"[{monsterColor}]{monsterName}[/] {verb}!";
        }

        var attackVerb = MonsterAttackVerbs[tier][random.Next(MonsterAttackVerbs[tier].Count)];

        // Don't show raw damage here — the actual damage (after armor) is shown separately
        // Showing both confused players into thinking they were hit twice
        return $"[{monsterColor}]{monsterName}[/] {attackVerb}!";
    }

    /// <summary>
    /// Get spell cast message with appropriate color
    /// </summary>
    public static string GetSpellCastMessage(string casterName, string spellName, string casterColor = "white")
    {
        return $"[{casterColor}]{casterName}[/] casts [bright_magenta]{spellName}[/]!";
    }

    /// <summary>
    /// Get death message
    /// </summary>
    public static string GetDeathMessage(string name, string color = "white")
    {
        var messages = new List<string>
        {
            $"[{color}]{name}[/] has been [bright_red]slain[/]!",
            $"[{color}]{name}[/] [bright_red]collapses[/] in defeat!",
            $"[{color}]{name}[/] has been [bright_red]defeated[/]!",
            $"[{color}]{name}[/] [bright_red]falls[/] in combat!"
        };

        return messages[Random.Shared.Next(messages.Count)];
    }

    /// <summary>
    /// Get victory message
    /// </summary>
    public static string GetVictoryMessage(int monstersDefeated)
    {
        if (monstersDefeated == 1)
        {
            return "Victory!";
        }

        return monstersDefeated switch
        {
            2 => "Double kill!",
            3 => "Triple kill!",
            4 => "Quadra kill!",
            5 => "PENTA KILL!",
            _ => $"{monstersDefeated} MONSTERS SLAIN!"
        };
    }
}
