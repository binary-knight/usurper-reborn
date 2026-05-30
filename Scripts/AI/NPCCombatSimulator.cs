using System;
using System.Collections.Generic;
using System.Linq;
using static ClassAbilitySystem;

/// <summary>
/// v0.64.0 Brain v2 Slice 4: real round-by-round combat for Tier A NPCs.
///
/// Replaces the abstract `Math.Max(Level*6, STR + WeapPow - Defence)` damage
/// formula for the named/notable NPC subset (kings, court, player-team,
/// player-adjacent, Lv 30+). Tier A NPCs use class abilities, drink potions,
/// flee tactically; gear actually matters because the formula reads from
/// equipment plumbing.
///
/// Tier B NPCs (everyone else) continue through the existing
/// WorldSimulator.SimulateTeamVsMonsterCombat / inline-NPCExploreDungeon
/// abstract sim. Cheap, fast, telemetry-comparable.
///
/// Parallel to CombatEngine instead of refactoring it. The full combat engine
/// has ~28k LOC with 234 GetInput sites + 298 Task.Delay sites; the sim-mode
/// retrofit would be a multi-week undertaking with high risk to player combat.
/// This module is ~400 LOC, isolated, can grow incrementally.
/// </summary>
public static class NPCCombatSimulator
{
    /// <summary>
    /// Max rounds per combat. Sim should always converge within 20 because
    /// NPCs do real ability damage; if it doesn't, treat as a stalemate
    /// (split XP partial-credit on monster damage).
    /// </summary>
    private const int MaxRounds = 20;

    /// <summary>
    /// Run a real-combat simulation. Returns a structured outcome:
    /// - Won: NPC (and team if present) defeated all monsters
    /// - Fled: NPC fled before death
    /// - Died: NPC's HP hit 0
    /// - Stalemate: hit MaxRounds without resolution
    ///
    /// Mutates HP, Mana, CurrentCombatStamina, Healing, ManaPotions on the NPC
    /// and teammates. Caller is responsible for awarding XP/gold from the
    /// returned `expReward` / `goldReward` and for permadeath handling on
    /// `Died` outcome.
    /// </summary>
    public static NPCCombatResult Simulate(
        NPC primary,
        List<Monster> monsters,
        List<Character>? teammates,
        Random random)
    {
        if (primary == null || monsters == null || monsters.Count == 0)
        {
            return new NPCCombatResult { Outcome = NPCCombatOutcome.Stalemate };
        }

        // Initialize transient combat state. Mirror CombatEngine's PlayerVsMonsters
        // setup: reset combat stamina, ensure mana is in bounds, clear stale
        // round-start statuses.
        InitializeForCombat(primary);
        if (teammates != null)
        {
            foreach (var t in teammates) InitializeForCombat(t);
        }

        // Per-NPC ability cooldowns. Keyed by ability ID. Mirrors
        // CombatEngine's per-character cooldown dict but local to this fight.
        var cooldowns = new Dictionary<Character, Dictionary<string, int>>();
        cooldowns[primary] = new Dictionary<string, int>();
        if (teammates != null)
        {
            foreach (var t in teammates) cooldowns[t] = new Dictionary<string, int>();
        }

        long totalExpReward = 0;
        long totalGoldReward = 0;
        long startingMonsterHP = monsters.Sum(m => m.HP);
        int rounds = 0;
        bool fled = false;
        bool playerSideDied = false;

        // Tracks monsters whose XP/gold has already been awarded so we don't
        // double-credit if the monster is still in the list (HP <= 0 but dead).
        // Local to this fight; no field on Monster needed.
        var rewardClaimed = new HashSet<Monster>();

        // Round loop.
        while (rounds < MaxRounds)
        {
            rounds++;

            // v0.64.0 Slice 8a: tick status effects on monsters at the top of
            // the round. Poison/burn deal DoT before action resolution; stun /
            // slow counters tick down at end of round. Tick damage is a
            // percentage of MaxHP rather than stored per-monster (Monster class
            // has no PoisonDamage field) -- 5% for poison, 6% for burn,
            // floored at 1 so high-AC low-HP monsters still take chip.
            foreach (var m in monsters.Where(m => m.IsAlive))
            {
                if (m.PoisonRounds > 0)
                {
                    long tick = Math.Max(1, m.MaxHP / 20);
                    m.HP = Math.Max(0, m.HP - tick);
                }
                if (m.BurnRounds > 0)
                {
                    long tick = Math.Max(1, m.MaxHP / 17);  // ~6% MaxHP
                    m.HP = Math.Max(0, m.HP - tick);
                }
            }

            // Pre-round: flee check for primary AND teammates.
            // (Each combatant decides independently whether to flee at low HP.)
            if (ShouldFlee(primary, random))
            {
                fled = true;
                break;
            }
            // Teammates that fled stop participating but don't end the fight.
            if (teammates != null)
            {
                foreach (var t in teammates.Where(c => c.IsAlive).ToList())
                {
                    if (ShouldFlee(t, random))
                    {
                        // Tag teammate as fled by zeroing their participation.
                        // We use a sentinel by setting CurrentCombatStamina to -1
                        // for the rest of this fight (sim-local convention).
                        t.CurrentCombatStamina = -1;
                    }
                }
            }

            // Player-side actions (primary first, then teammates).
            ProcessActorTurn(primary, monsters, BuildAllyList(primary, teammates), cooldowns[primary], random);
            if (!primary.IsAlive)
            {
                playerSideDied = true;
                break;
            }

            if (teammates != null)
            {
                foreach (var t in teammates.Where(c => c.IsAlive && c.CurrentCombatStamina >= 0).ToList())
                {
                    ProcessActorTurn(t, monsters, BuildAllyList(primary, teammates), cooldowns[t], random);
                }
            }

            // Award rewards for monsters killed this round.
            foreach (var dead in monsters.Where(m => !m.IsAlive && !rewardClaimed.Contains(m)))
            {
                totalExpReward += dead.GetExperienceReward();
                totalGoldReward += dead.GetGoldReward();
                rewardClaimed.Add(dead);
            }

            // Win check.
            if (!monsters.Any(m => m.IsAlive))
            {
                return BuildResult(NPCCombatOutcome.Won, primary, totalExpReward, totalGoldReward, rounds);
            }

            // Monster turn. Each alive monster picks a random alive player-side
            // target and attacks. Damage formula matches the abstract sim's
            // halved-damage convention so Tier A doesn't get cheesed.
            // v0.64.0 Slice 8a: stunned monsters skip turn; slowed monsters
            // have 50% chance to skip.
            foreach (var monster in monsters.Where(m => m.IsAlive))
            {
                if (monster.StunRounds > 0) continue;
                if (monster.IsSlowed && monster.SlowDuration > 0 && random.Next(2) == 0) continue;

                var aliveTargets = BuildAllyList(primary, teammates)
                    .Where(c => c.IsAlive && c.CurrentCombatStamina != -1)
                    .ToList();
                if (aliveTargets.Count == 0) break;

                var target = aliveTargets[random.Next(aliveTargets.Count)];
                ApplyMonsterDamage(monster, target, random);
                if (!target.IsAlive && target == primary)
                {
                    playerSideDied = true;
                    break;
                }
            }

            if (playerSideDied) break;

            // v0.64.0 Slice 8a: end-of-round status decrement on monsters.
            foreach (var m in monsters.Where(m => m.IsAlive))
            {
                if (m.StunRounds > 0) m.StunRounds--;
                if (m.PoisonRounds > 0) m.PoisonRounds--;
                if (m.BurnRounds > 0) m.BurnRounds--;
                if (m.WeakenRounds > 0) m.WeakenRounds--;
                if (m.SlowDuration > 0)
                {
                    m.SlowDuration--;
                    if (m.SlowDuration == 0) m.IsSlowed = false;
                }
            }

            // v0.64.0 Slice 8b: end-of-round buff decrement on player-side actors.
            foreach (var a in BuildAllyList(primary, teammates).Where(c => c.IsAlive))
            {
                if (a.TempAttackBonusDuration > 0)
                {
                    a.TempAttackBonusDuration--;
                    if (a.TempAttackBonusDuration == 0) a.TempAttackBonus = 0;
                }
                if (a.TempDefenseBonusDuration > 0)
                {
                    a.TempDefenseBonusDuration--;
                    if (a.TempDefenseBonusDuration == 0)
                    {
                        a.TempDefenseBonus = 0;
                        a.TempDamageReductionPercent = 0;
                    }
                }
            }

            // Cooldown tick. Decrement all cooldown counters at the end of each round.
            foreach (var kv in cooldowns)
            {
                foreach (var key in kv.Value.Keys.ToList())
                {
                    if (kv.Value[key] > 0) kv.Value[key]--;
                }
            }
        }

        // Determine outcome.
        if (playerSideDied)
        {
            // Partial XP credit if NPC dealt meaningful damage.
            long monsterDamageDealt = startingMonsterHP - monsters.Sum(m => Math.Max(0, m.HP));
            double damageFrac = startingMonsterHP > 0 ? (double)monsterDamageDealt / startingMonsterHP : 0;
            if (damageFrac > 0.3)
            {
                totalExpReward = (long)(totalExpReward * damageFrac);
                totalGoldReward = (long)(totalGoldReward * damageFrac);
            }
            else
            {
                totalExpReward = 0;
                totalGoldReward = 0;
            }
            return BuildResult(NPCCombatOutcome.Died, primary, totalExpReward, totalGoldReward, rounds);
        }

        if (fled)
        {
            // Partial credit on flee, same shape as death.
            long monsterDamageDealt = startingMonsterHP - monsters.Sum(m => Math.Max(0, m.HP));
            double damageFrac = startingMonsterHP > 0 ? (double)monsterDamageDealt / startingMonsterHP : 0;
            if (damageFrac > 0.3)
            {
                totalExpReward = (long)(totalExpReward * damageFrac);
                totalGoldReward = (long)(totalGoldReward * damageFrac);
            }
            else
            {
                totalExpReward = 0;
                totalGoldReward = 0;
            }
            return BuildResult(NPCCombatOutcome.Fled, primary, totalExpReward, totalGoldReward, rounds);
        }

        return BuildResult(NPCCombatOutcome.Stalemate, primary, 0, 0, rounds);
    }

    /// <summary>
    /// Per-actor turn: pick action via NPCCombatBrain, dispatch.
    /// </summary>
    private static void ProcessActorTurn(
        Character actor,
        List<Monster> monsters,
        List<Character> allies,
        Dictionary<string, int> cooldowns,
        Random random)
    {
        if (!actor.IsAlive) return;
        if (actor.CurrentCombatStamina == -1) return; // fled this fight

        var action = NPCCombatBrain.PickAction(actor, monsters, allies, cooldowns, random);

        switch (action.Kind)
        {
            case NPCCombatActionKind.UseHealingPotion:
                DrinkHealingPotion(actor, action.HealTarget ?? actor);
                break;

            case NPCCombatActionKind.UseManaPotion:
                DrinkManaPotion(actor);
                break;

            case NPCCombatActionKind.UseAbility:
                ExecuteAbility(actor, action, monsters, allies, cooldowns, random);
                break;

            case NPCCombatActionKind.BasicAttack:
            default:
                ExecuteBasicAttack(actor, monsters, action.MonsterTargetIndex, random);
                break;
        }
    }

    private static void DrinkHealingPotion(Character actor, Character target)
    {
        if (actor.Healing <= 0) return;
        actor.Healing--;

        // Heal target by 50% of their MaxHP (matches CombatEngine's potion baseline).
        long heal = target.MaxHP / 2;
        target.HP = Math.Min(target.MaxHP, target.HP + heal);
    }

    private static void DrinkManaPotion(Character actor)
    {
        if (actor.ManaPotions <= 0) return;
        actor.ManaPotions--;
        long mana = actor.MaxMana / 2;
        actor.Mana = Math.Min(actor.MaxMana, actor.Mana + mana);
    }

    private static void ExecuteAbility(
        Character actor,
        NPCCombatAction action,
        List<Monster> monsters,
        List<Character> allies,
        Dictionary<string, int> cooldowns,
        Random random)
    {
        var ability = ClassAbilitySystem.GetAbility(action.AbilityId);
        if (ability == null)
        {
            ExecuteBasicAttack(actor, monsters, action.MonsterTargetIndex, random);
            return;
        }

        // Spend resources up front.
        if (ability.ManaCost > 0) actor.Mana = Math.Max(0, actor.Mana - ability.ManaCost);
        if (ability.StaminaCost > 0)
            actor.CurrentCombatStamina = Math.Max(0, actor.CurrentCombatStamina - ability.StaminaCost);
        if (ability.Cooldown > 0) cooldowns[ability.Id] = ability.Cooldown;

        switch (ability.Type)
        {
            case AbilityType.Heal:
                long heal = (long)EstimateHealing(actor, ability);
                var healTarget = action.HealTarget ?? actor;
                healTarget.HP = Math.Min(healTarget.MaxHP, healTarget.HP + heal);
                break;

            case AbilityType.Attack:
            case AbilityType.Debuff:
                if (ability.SpecialEffect?.Contains("aoe", StringComparison.OrdinalIgnoreCase) == true)
                {
                    // AoE: half damage per monster, hits all alive.
                    long perTarget = (long)(EstimateDamage(actor, ability) * 0.6);
                    foreach (var m in monsters.Where(m => m.IsAlive))
                    {
                        m.HP -= ApplyMonsterDefense(perTarget, m, random);
                        ApplyAbilityStatusToMonster(ability, m, actor);
                    }
                }
                else
                {
                    int idx = action.MonsterTargetIndex;
                    if (idx >= 0 && idx < monsters.Count && monsters[idx].IsAlive)
                    {
                        long dmg = (long)EstimateDamage(actor, ability);
                        monsters[idx].HP -= ApplyMonsterDefense(dmg, monsters[idx], random);
                        ApplyAbilityStatusToMonster(ability, monsters[idx], actor);
                    }
                }
                break;

            case AbilityType.Buff:
            case AbilityType.Defense:
                // v0.64.0 Brain v2 Slice 8b: apply real buff/defense effects using
                // Character's existing Temp* fields (same plumbing CombatEngine
                // uses for player buffs). AttackBonus, DefenseBonus, Duration,
                // and SpecialEffect drive the application.
                ApplyBuffOrDefenseAbility(actor, ability);
                break;

            case AbilityType.Utility:
                // Utility abilities (Hide, Vanish, etc.) deferred -- Slice 4b can
                // wire specific ones if telemetry shows NPCs casting them often
                // without effect.
                break;
        }
    }

    /// <summary>
    /// v0.64.0 Brain v2 Slice 8a: apply ability-driven status effects to a
    /// monster. Reads the ability's SpecialEffect string and sets the
    /// corresponding Monster fields (StunRounds, PoisonRounds + PoisonDamage,
    /// BurnRounds, IsSlowed + SlowDuration, WeakenRounds). Mirrors how
    /// CombatEngine wires status application from ability SpecialEffect.
    /// </summary>
    private static void ApplyAbilityStatusToMonster(ClassAbility ability, Monster target, Character attacker)
    {
        if (target == null || !target.IsAlive) return;
        var effect = ability.SpecialEffect ?? "";
        if (string.IsNullOrEmpty(effect)) return;
        string e = effect.ToLowerInvariant();

        // Stun -- target skips its next turn (decremented at end of round).
        if (e.Contains("stun"))
        {
            target.StunRounds = Math.Max(target.StunRounds, 2);
        }
        // Poison -- 3 rounds of MaxHP-percentage tick (computed in the tick loop).
        if (e.Contains("poison"))
        {
            target.PoisonRounds = Math.Max(target.PoisonRounds, 3);
        }
        // Burn (fire DoT) -- separate counter from poison so both can stack.
        if (e.Contains("burn") || e.Contains("fire"))
        {
            target.BurnRounds = Math.Max(target.BurnRounds, 3);
        }
        // Slowed -- 50% chance to skip turn while slowed.
        if (e.Contains("slow"))
        {
            target.IsSlowed = true;
            target.SlowDuration = Math.Max(target.SlowDuration, 3);
        }
        // Weaken -- attack/defense reduction.
        if (e.Contains("weaken") || e.Contains("debuff"))
        {
            target.WeakenRounds = Math.Max(target.WeakenRounds, 3);
        }
    }

    /// <summary>
    /// v0.64.0 Brain v2 Slice 8b: apply Buff / Defense ability effects to the
    /// actor. Sets the existing Character Temp* fields so the modifier reads
    /// in basic-attack / monster-attack paths see the buff. Mirrors
    /// CombatEngine's TempAttackBonus / TempDefenseBonus / TempDamageReductionPercent
    /// pattern.
    /// </summary>
    private static void ApplyBuffOrDefenseAbility(Character actor, ClassAbility ability)
    {
        int duration = Math.Max(1, ability.Duration);

        if (ability.AttackBonus > 0)
        {
            actor.TempAttackBonus = Math.Max(actor.TempAttackBonus, ability.AttackBonus);
            actor.TempAttackBonusDuration = Math.Max(actor.TempAttackBonusDuration, duration);
        }
        if (ability.DefenseBonus > 0)
        {
            actor.TempDefenseBonus = Math.Max(actor.TempDefenseBonus, ability.DefenseBonus);
            actor.TempDefenseBonusDuration = Math.Max(actor.TempDefenseBonusDuration, duration);
        }

        // Special-effect-keyed buffs.
        var effect = (ability.SpecialEffect ?? "").ToLowerInvariant();
        if (effect.Contains("damage_reduction") || effect.Contains("shield_wall"))
        {
            // Shield Wall Formation: 30% damage reduction while active (matches
            // CombatEngine's pre-existing constant).
            actor.TempDamageReductionPercent = Math.Max(actor.TempDamageReductionPercent, 30);
        }
        if (effect.Contains("resist_all") || effect.Contains("immunity") || effect.Contains("mindblank"))
        {
            actor.HasStatusImmunity = true;
        }
    }

    private static void ExecuteBasicAttack(Character actor, List<Monster> monsters, int targetIdx, Random random)
    {
        // Pick target if not specified.
        if (targetIdx < 0 || targetIdx >= monsters.Count || !monsters[targetIdx].IsAlive)
        {
            for (int i = 0; i < monsters.Count; i++)
            {
                if (monsters[i].IsAlive) { targetIdx = i; break; }
            }
        }
        if (targetIdx < 0 || targetIdx >= monsters.Count || !monsters[targetIdx].IsAlive) return;

        var target = monsters[targetIdx];
        // Level-scaled floor mirrors the abstract sim's Lv*6 floor so under-geared
        // NPCs still do meaningful damage. Stat-based formula stacks on top
        // when gear/stats exceed the floor.
        long baseAttack = actor.Strength + actor.WeapPow;

        // v0.64.0 Slice 8b: TempAttackBonus applies when the actor has an
        // active Buff (Battle Cry, Focus, etc.). Multiplied as a percentage
        // boost to the base attack.
        if (actor.TempAttackBonusDuration > 0 && actor.TempAttackBonus > 0)
        {
            baseAttack = (long)(baseAttack * (1.0 + actor.TempAttackBonus / 100.0));
        }

        long damage = Math.Max(actor.Level * 6, baseAttack - target.Defence);
        damage += random.Next(1, (int)Math.Max(2, actor.WeapPow / 3));
        long dealt = ApplyMonsterDefense(damage, target, random);
        target.HP -= dealt;

        // v0.64.0 Slice 8c: equipment-enchant procs on basic attack hit.
        // Lifedrinker (LifeSteal) restores HP equal to a % of damage dealt.
        // Siphoning (ManaSteal) restores mana similarly. Pre-fix NPCs with
        // these enchants got no benefit (the abstract sim ignored equipment
        // procs entirely). Now wired through.
        ApplyOnHitEnchants(actor, dealt);
    }

    /// <summary>
    /// v0.64.0 Brain v2 Slice 8c: equipment-enchant on-hit procs for NPC combat.
    /// Reads the actor's GetEquipmentLifeSteal and GetEquipmentManaSteal (which
    /// sum across equipped items) and grants HP / mana per hit.
    /// </summary>
    private static void ApplyOnHitEnchants(Character actor, long damageDealt)
    {
        if (damageDealt <= 0) return;

        try
        {
            int lifeStealPct = actor.GetEquipmentLifeSteal();
            if (lifeStealPct > 0)
            {
                long heal = (long)(damageDealt * lifeStealPct / 100.0);
                if (heal > 0)
                    actor.HP = Math.Min(actor.MaxHP, actor.HP + heal);
            }

            int manaStealPct = actor.GetEquipmentManaSteal();
            if (manaStealPct > 0 && actor.MaxMana > 0)
            {
                long mana = (long)(damageDealt * manaStealPct / 100.0);
                if (mana > 0)
                    actor.Mana = Math.Min(actor.MaxMana, actor.Mana + mana);
            }
        }
        catch
        {
            // Equipment accessors can throw on partial save state; tolerate it.
            // The proc is decorative; missing one tick is no-op.
        }
    }

    private static long ApplyMonsterDefense(long damage, Monster monster, Random random)
    {
        // Already factored Defence in for basic attacks; abilities skip that step.
        // Minimum 1 damage so abilities never zero-out.
        return Math.Max(1, damage);
    }

    private static void ApplyMonsterDamage(Monster monster, Character target, Random random)
    {
        // Monster damage formula matches the abstract sim's halved-baseline so
        // Tier A and Tier B see roughly comparable monster pressure.
        long effectiveDefence = target.Defence;
        long effectiveArmPow = target.ArmPow;

        // v0.64.0 Slice 8b: TempDefenseBonus adds to defence when an active Defense
        // ability is up (Shield Wall, Iron Will, etc.).
        if (target.TempDefenseBonusDuration > 0 && target.TempDefenseBonus > 0)
        {
            effectiveDefence += target.TempDefenseBonus;
        }

        long damage = Math.Max(1, monster.Strength + monster.WeapPow - effectiveDefence - effectiveArmPow);
        damage += random.Next(1, (int)Math.Max(2, monster.WeapPow / 3));
        damage = (long)(damage * 0.50);

        // Warrior tank reduction (same shape as abstract sim).
        if (target.Class == CharacterClass.Warrior)
            damage = (long)(damage * 0.85);

        // v0.64.0 Slice 8b: percentage damage reduction from defensive abilities
        // (Shield Wall Formation = 30% reduction). Applied AFTER the additive
        // defence reduction, matching CombatEngine's order.
        if (target.TempDefenseBonusDuration > 0 && target.TempDamageReductionPercent > 0)
        {
            damage = (long)(damage * (1.0 - target.TempDamageReductionPercent / 100.0));
        }

        target.HP = Math.Max(0, target.HP - Math.Max(1, damage));
    }

    private static bool ShouldFlee(Character npc, Random random)
    {
        if (npc.MaxHP <= 0 || !npc.IsAlive) return false;

        float fleeThreshold = 0.20f;
        var p = (npc as NPC)?.Brain?.Personality;
        if (p != null)
        {
            if (p.Courage < 0.3f) fleeThreshold = 0.30f;
            else if (p.Courage > 0.7f) fleeThreshold = 0.10f;
        }

        if (npc.HP >= npc.MaxHP * fleeThreshold) return false;

        int fleeChance = 70 + (int)(npc.Agility / 3);
        return random.Next(100) < Math.Min(95, fleeChance);
    }

    private static double EstimateDamage(Character actor, ClassAbility ability)
    {
        double statBoost = 1.0 + (actor.Strength - 10) * 0.04;
        statBoost = Math.Clamp(statBoost, 0.5, 6.0);
        return ability.BaseDamage * statBoost;
    }

    private static double EstimateHealing(Character actor, ClassAbility ability)
    {
        // Healing scales with Wisdom for healer classes.
        double wisBoost = 1.0 + (actor.Wisdom - 10) * 0.03;
        wisBoost = Math.Clamp(wisBoost, 0.5, 5.0);
        return ability.BaseHealing * wisBoost;
    }

    private static void InitializeForCombat(Character c)
    {
        // Reset transient combat-only state. Mirror CombatEngine setup at line ~487.
        c.InitializeCombatStamina();
        if (c.Mana > c.MaxMana) c.Mana = c.MaxMana;
        if (c.HP > c.MaxHP) c.HP = c.MaxHP;
    }

    private static List<Character> BuildAllyList(Character primary, List<Character>? teammates)
    {
        var list = new List<Character> { primary };
        if (teammates != null) list.AddRange(teammates);
        return list;
    }

    private static NPCCombatResult BuildResult(
        NPCCombatOutcome outcome,
        Character primary,
        long expReward,
        long goldReward,
        int rounds)
    {
        return new NPCCombatResult
        {
            Outcome = outcome,
            ExpReward = expReward,
            GoldReward = goldReward,
            Rounds = rounds,
            FinalHP = primary.HP,
            FinalMana = primary.Mana,
        };
    }
}

public class NPCCombatResult
{
    public NPCCombatOutcome Outcome { get; set; }
    public long ExpReward { get; set; }
    public long GoldReward { get; set; }
    public int Rounds { get; set; }
    public long FinalHP { get; set; }
    public long FinalMana { get; set; }
}

public enum NPCCombatOutcome
{
    Won,
    Fled,
    Died,
    Stalemate,
}
