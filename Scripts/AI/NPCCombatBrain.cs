using System;
using System.Collections.Generic;
using System.Linq;
using static ClassAbilitySystem;

/// <summary>
/// v0.64.0 Brain v2 Slice 4: per-round combat action picker for NPCs running
/// real combat via NPCCombatSimulator.
///
/// Decides each round whether the NPC should: drink a healing potion, drink a
/// mana potion, cast a heal ability, cast an offensive ability/spell, or fall
/// through to a basic attack. Picks targets when relevant. Pure function over
/// (npc, monsters, teammates, cooldowns) -> CombatAction.
///
/// Triage rules:
///   1. HP below threshold + has potion -> potion (cheap, no mana).
///   2. HP below threshold + has heal ability with mana/stamina -> heal cast.
///   3. Mana below 30% + has mana potion + spellcasting class -> mana potion.
///   4. Offensive ability available + cost affordable + base damage > weapon damage -> ability.
///   5. Otherwise -> basic attack on weakest alive monster.
///
/// Targets: heal targets the most-injured alive ally (including self).
/// Offensive targets the lowest-HP alive monster.
/// </summary>
public static class NPCCombatBrain
{
    /// <summary>
    /// Heal threshold by class. Healer classes (Cleric/Paladin) heal earlier;
    /// pure-damage classes (Warrior/Barbarian) tough it out longer.
    /// </summary>
    private static double GetHealThreshold(Character npc)
    {
        switch (npc.Class)
        {
            case CharacterClass.Cleric:
            case CharacterClass.Paladin:
            case CharacterClass.Alchemist:
            case CharacterClass.Sage:
                return 0.70;  // heal at 70% for healer classes
            case CharacterClass.Warrior:
            case CharacterClass.Barbarian:
                return 0.35;  // tough it out
            default:
                return 0.50;
        }
    }

    /// <summary>
    /// Picks the combat action for this NPC this round. Returns the action,
    /// target index (for offensive attacks), and target (Character for heals).
    /// </summary>
    public static NPCCombatAction PickAction(
        Character npc,
        List<Monster> monsters,
        List<Character> allies,
        Dictionary<string, int> cooldowns,
        Random random)
    {
        double hpFrac = npc.MaxHP > 0 ? (double)npc.HP / npc.MaxHP : 1.0;
        double healThresh = GetHealThreshold(npc);

        // 1. Potion (healing) -- cheap, no resource cost beyond the potion itself.
        // Self-heal when hurt below threshold.
        if (hpFrac < healThresh && npc.Healing > 0)
        {
            return new NPCCombatAction
            {
                Kind = NPCCombatActionKind.UseHealingPotion,
                HealTarget = npc,
            };
        }

        // 2. Heal teammate via potion if any ally is critically wounded.
        if (npc.Healing > 0 && allies != null)
        {
            var hurtAlly = allies
                .Where(a => a != npc && a.IsAlive && a.MaxHP > 0 && (double)a.HP / a.MaxHP < 0.30)
                .OrderBy(a => (double)a.HP / a.MaxHP)
                .FirstOrDefault();
            if (hurtAlly != null)
            {
                return new NPCCombatAction
                {
                    Kind = NPCCombatActionKind.UseHealingPotion,
                    HealTarget = hurtAlly,
                };
            }
        }

        // 3. Heal ability for healer classes when wounded.
        var available = ClassAbilitySystem.GetAvailableAbilities(npc);
        if (hpFrac < healThresh)
        {
            var healAbility = available
                .Where(a => a.Type == AbilityType.Heal)
                .Where(a => CanAffordAbility(npc, a) && !IsOnCooldown(cooldowns, a))
                .OrderByDescending(a => a.BaseHealing)
                .FirstOrDefault();
            if (healAbility != null)
            {
                return new NPCCombatAction
                {
                    Kind = NPCCombatActionKind.UseAbility,
                    AbilityId = healAbility.Id,
                    HealTarget = npc,
                };
            }
        }

        // 4. Mana potion when low on mana for spellcasters.
        bool isCaster = npc.Class == CharacterClass.Magician
            || npc.Class == CharacterClass.Sage
            || npc.Class == CharacterClass.Cleric
            || npc.Class == CharacterClass.MysticShaman;
        if (isCaster && npc.MaxMana > 0 && (double)npc.Mana / npc.MaxMana < 0.30 && npc.ManaPotions > 0)
        {
            return new NPCCombatAction
            {
                Kind = NPCCombatActionKind.UseManaPotion,
            };
        }

        // 5. Best offensive ability. Picks the highest-base-damage Attack ability
        // we can afford AND that beats the weapon-power baseline. Multi-target
        // (AoE) preferred when 3+ monsters alive.
        var aliveMonsters = monsters?.Where(m => m.IsAlive).ToList() ?? new List<Monster>();
        if (aliveMonsters.Count > 0)
        {
            var offensiveAbilities = available
                .Where(a => a.Type == AbilityType.Attack || a.Type == AbilityType.Debuff)
                .Where(a => CanAffordAbility(npc, a) && !IsOnCooldown(cooldowns, a))
                .Where(a => a.BaseDamage > 0)
                .OrderByDescending(a => EstimatedAbilityDamage(npc, a))
                .ToList();

            // Prefer AoE for big groups.
            if (aliveMonsters.Count >= 3)
            {
                var aoe = offensiveAbilities.FirstOrDefault(a =>
                    a.SpecialEffect?.Contains("aoe", StringComparison.OrdinalIgnoreCase) == true);
                if (aoe != null && EstimatedAbilityDamage(npc, aoe) > BasicAttackEstimate(npc))
                {
                    return new NPCCombatAction
                    {
                        Kind = NPCCombatActionKind.UseAbility,
                        AbilityId = aoe.Id,
                    };
                }
            }

            // Single-target best ability if it beats a basic swing.
            var bestAbility = offensiveAbilities.FirstOrDefault();
            if (bestAbility != null
                && EstimatedAbilityDamage(npc, bestAbility) > BasicAttackEstimate(npc))
            {
                int targetIdx = PickWeakestMonsterIndex(monsters);
                return new NPCCombatAction
                {
                    Kind = NPCCombatActionKind.UseAbility,
                    AbilityId = bestAbility.Id,
                    MonsterTargetIndex = targetIdx,
                };
            }
        }

        // 6. Default: basic attack on weakest alive monster.
        return new NPCCombatAction
        {
            Kind = NPCCombatActionKind.BasicAttack,
            MonsterTargetIndex = PickWeakestMonsterIndex(monsters),
        };
    }

    private static bool CanAffordAbility(Character npc, ClassAbility a)
    {
        if (a.ManaCost > 0 && npc.Mana < a.ManaCost) return false;
        if (a.StaminaCost > 0 && npc.CurrentCombatStamina < a.StaminaCost) return false;
        return true;
    }

    private static bool IsOnCooldown(Dictionary<string, int> cooldowns, ClassAbility a)
    {
        if (cooldowns == null) return false;
        return cooldowns.TryGetValue(a.Id, out int remaining) && remaining > 0;
    }

    /// <summary>
    /// Estimated damage of an ability: base + stat scaling. Used to compare
    /// abilities against basic-attack damage so we don't waste mana on
    /// abilities that hit weaker than just swinging.
    /// </summary>
    private static double EstimatedAbilityDamage(Character npc, ClassAbility a)
    {
        // Stat-driven multiplier roughly matches CombatEngine's ability scaling.
        double statBoost = 1.0 + (npc.Strength - 10) * 0.04;
        statBoost = Math.Clamp(statBoost, 0.5, 6.0);
        return a.BaseDamage * statBoost;
    }

    private static double BasicAttackEstimate(Character npc)
    {
        // Approximation of basic attack damage: STR + WeapPow + level floor.
        long floor = npc.Level * 6;
        long stat = npc.Strength + npc.WeapPow;
        return Math.Max(floor, stat) * 1.0;
    }

    private static int PickWeakestMonsterIndex(List<Monster> monsters)
    {
        if (monsters == null) return -1;
        int bestIdx = -1;
        long bestHp = long.MaxValue;
        for (int i = 0; i < monsters.Count; i++)
        {
            if (!monsters[i].IsAlive) continue;
            if (monsters[i].HP < bestHp)
            {
                bestHp = monsters[i].HP;
                bestIdx = i;
            }
        }
        return bestIdx;
    }
}

/// <summary>
/// Per-round combat action emitted by NPCCombatBrain and consumed by
/// NPCCombatSimulator.
/// </summary>
public class NPCCombatAction
{
    public NPCCombatActionKind Kind { get; set; }
    public string AbilityId { get; set; } = "";
    public int MonsterTargetIndex { get; set; } = -1;
    public Character? HealTarget { get; set; }
}

public enum NPCCombatActionKind
{
    BasicAttack,
    UseAbility,
    UseHealingPotion,
    UseManaPotion,
}
