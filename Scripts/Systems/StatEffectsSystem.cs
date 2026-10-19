using System;
using UsurperRemake.Systems;

/// <summary>
/// Centralized stat effects system - defines how each attribute affects gameplay.
/// All stats now have meaningful mechanical effects like a modern RPG.
/// </summary>
public static class StatEffectsSystem
{
    private static Random _random = Random.Shared;

    // =====================================================
    // STRENGTH - Physical power, melee damage
    // =====================================================

    /// <summary>
    /// Bonus melee damage from Strength
    /// Formula: Strength / 4 (so 20 STR = +5 damage)
    /// </summary>
    public static int GetStrengthDamageBonus(long strength)
    {
        return (int)(strength / 4);
    }

    // =====================================================
    // DEXTERITY - Accuracy, ranged damage, critical hits
    // =====================================================

    /// <summary>
    /// Critical hit chance from Dexterity + equipment bonuses.
    ///
    /// Base formula: 5 + (Dexterity / 10) + equipment crit bonus.
    ///
    /// v0.57.11 — DEX-scaling cap + Creator's Eye rebalance.
    ///
    /// Before v0.57.11 the cap was a flat 50% for every character and the
    /// Creator's Eye artifact applied a pre-clamp 1.5× multiplier. Two
    /// feedback problems surfaced from that: (a) DEX-primary classes
    /// (Assassin / Ranger / Jester / Abysswarden / Wayfarer) got nothing
    /// out of stat investment past DEX ~450 because the cap pinned them,
    /// and (b) the artifact was so strong it instantly floored any build
    /// at 50% crit, making further DEX investment meaningless for every
    /// other class too.
    ///
    /// New behavior:
    ///   - Cap scales from 50 to 75 based on DEX: <c>cap = 50 + min(25, DEX/30)</c>.
    ///     A DEX 750 Jester / Abysswarden hits the 75% ceiling. A DEX 100
    ///     Warrior still caps at ~53%. DEX investment matters all the way up.
    ///   - Creator's Eye drops from a 1.5× pre-clamp multiplier to a flat
    ///     +10 crit chance. Low-DEX builds still benefit (free 10 points).
    ///     High-DEX builds still benefit (it stacks under their raised cap).
    ///     But it no longer trivializes the subsystem — the artifact is a
    ///     strong complement to a crit build, not a replacement for one.
    ///   - Manwe/Void Key bonus preserved: the artifact's flat bonus doubles
    ///     (+20) during the Manwe battle with the Void Key, matching the
    ///     prior "empowered artifact" narrative.
    ///
    /// Maximum crit ceiling with every source: DEX 750 (cap raised to 75)
    /// + equipment bonuses already rolled into baseChance → 75%. A Wavecaller
    /// with Ocean's Voice active still gets a sequential backup roll on top
    /// (separate mechanic), bringing effective rate to ~80% in niche cases.
    /// Guaranteed-crit abilities (Hidden, Backstab, Umbral Step, Temporal
    /// Feint, Vanish) remain a separate 100%-for-one-hit mechanic.
    /// </summary>
    public static int GetCriticalHitChance(long dexterity, int equipmentCritBonus = 0)
    {
        int baseChance = 5 + (int)(dexterity / 10) + equipmentCritBonus;

        // Creator's Eye: flat crit chance bonus. Doubles during Manwe fight
        // with the Void Key to preserve the empowered-artifact moment.
        if (ArtifactSystem.Instance.HasCreatorsEye())
        {
            int eyeBonus = GameConfig.CreatorsEyeCritBonus;
            if (ArtifactSystem.Instance.HasVoidKey() && CombatEngine.IsManweBattle)
                eyeBonus *= 2;
            baseChance += eyeBonus;
        }

        // DEX-scaling cap: 50 baseline, +1 per 30 DEX, cap at +25 (75% total).
        // Keeps a hard ceiling (no 100% crit walkthroughs) while rewarding
        // deep DEX investment for DEX-primary classes.
        int cap = 50 + (int)Math.Min(25, dexterity / 30);
        return Math.Clamp(baseChance, 5, cap);
    }

    /// <summary>
    /// Critical hit damage multiplier
    /// Dexterity part: 1.5x + 0.02x per Dexterity above 10 (so 20 DEX = 1.7x), capped at 3.0x.
    /// Equipment crit damage bonus adds on top of that part and is not capped (e.g., +10% = +0.1x)
    /// </summary>
    public static float GetCriticalDamageMultiplier(long dexterity, int equipmentCritDamageBonus = 0)
    {
        float dexPart = Math.Min(1.5f + Math.Max(0, (dexterity - 10) * 0.02f), 3.0f); // Cap at 3x holds the Dexterity part only
        return dexPart + (equipmentCritDamageBonus / 100f);
    }

    // =====================================================
    // CONSTITUTION - HP, poison/disease resistance, stamina regen
    // =====================================================

    /// <summary>
    /// Bonus HP from Constitution (v0.41.4: reduced quadratic growth from CON/10 to CON/15)
    /// Formula: (Constitution - 10) * 3 + Level * (Constitution / 15)
    /// </summary>
    public static long GetConstitutionHPBonus(long constitution, int level)
    {
        return (constitution - 10) * 3 + level * (constitution / 15);
    }

    // =====================================================
    // INTELLIGENCE - Spell power, mana pool, learning
    // =====================================================

    /// <summary>
    /// Bonus mana from Intelligence
    /// Formula: (Intelligence - 10) * 5 + Level * (Intelligence / 8)
    /// </summary>
    public static long GetIntelligenceManaBonus(long intelligence, int level)
    {
        return Math.Max(0, (intelligence - 10) * 5 + level * (intelligence / 8));
    }

    /// <summary>
    /// Spell damage multiplier from Intelligence
    /// Uses soft cap with diminishing returns so Magicians scale into endgame
    /// without uncapped exponential damage:
    /// - Below INT 85: full scaling at 0.05 per point (reaches 4.75x)
    /// - Above INT 85: diminished scaling at 0.015 per point
    /// - Hard ceiling at 8.0x
    /// </summary>
    public static float GetSpellDamageMultiplier(long intelligence)
    {
        const float fullScalingRate = 0.05f;
        const float diminishedRate = 0.015f;
        const long softCapThreshold = 85;
        const float hardCeiling = 8.0f;

        float multiplier;
        if (intelligence <= softCapThreshold)
        {
            multiplier = 1.0f + Math.Max(0, (intelligence - 10) * fullScalingRate);
        }
        else
        {
            // Full scaling up to threshold, then diminished returns beyond
            float baseMultiplier = 1.0f + (softCapThreshold - 10) * fullScalingRate; // 4.0x at threshold
            float bonusMultiplier = (intelligence - softCapThreshold) * diminishedRate;
            multiplier = baseMultiplier + bonusMultiplier;
        }

        return Math.Min(multiplier, hardCeiling);
    }

    /// <summary>
    /// Spell critical chance
    /// Formula: (Intelligence - 10) / 2 percent
    /// </summary>
    public static int GetSpellCriticalChance(long intelligence)
    {
        return Math.Clamp((int)((intelligence - 10) / 2), 0, 50); // Cap at 50% like physical crits
    }

    // =====================================================
    // WISDOM - Mana cost reduction, spell resistance, healing
    // =====================================================

    /// <summary>
    /// Mana cost reduction percentage
    /// Formula: (Wisdom - 10) * 1.5 percent (so 20 WIS = 15% reduction)
    /// </summary>
    public static int GetManaCostReduction(long wisdom)
    {
        return Math.Max(0, Math.Min(50, (int)((wisdom - 10) * 1.5)));
    }

    /// <summary>
    /// Bonus mana from Wisdom (for hybrid casters)
    /// Formula: (Wisdom - 10) * 3
    /// </summary>
    public static long GetWisdomManaBonus(long wisdom)
    {
        return Math.Max(0, (wisdom - 10) * 3);
    }

    /// <summary>
    /// Healing effectiveness multiplier
    /// Formula: 1.0 + (Wisdom - 10) * 0.015 (so 20 WIS = 1.15x, 30 WIS = 1.30x healing)
    /// Balanced to give meaningful healing bonuses from Wisdom investment.
    /// </summary>
    public static float GetHealingMultiplier(long wisdom)
    {
        float multiplier = 1.0f + Math.Max(0, (wisdom - 10) * 0.015f);
        return Math.Min(multiplier, 3.0f); // Cap at 3x to prevent excessive healing at high WIS
    }

    /// <summary>
    /// Mana regeneration per round
    /// Formula: 2 + Wisdom / 10, capped at 15 mana per round
    ///
    /// BALANCE: Spells cost 15-135 mana, so regen should cover cheap spells
    /// over several rounds but not keep up with sustained high-level casting.
    /// Mana potions are needed for heavy spell use.
    /// </summary>
    public static int GetManaRegenPerRound(long wisdom)
    {
        int regen = 2 + (int)(wisdom / 10);
        return Math.Min(15, regen); // Cap at 15 mana per round
    }

    // =====================================================
    // CHARISMA - Prices, NPC reactions, conversion/mercy
    // =====================================================

    // =====================================================
    // STAMINA - Combat stamina pool, physical endurance
    // =====================================================

    // =====================================================
    // AGILITY - Dodge, initiative, multiple attacks
    // =====================================================

    /// <summary>
    /// Dodge chance percentage
    /// Formula: 5 + (Agility - 10) / 2 (so 20 AGI = 10% dodge)
    /// </summary>
    public static int GetDodgeChance(long agility)
    {
        return Math.Max(0, Math.Min(35, 5 + (int)((agility - 10) / 2)));
    }

    /// <summary>
    /// Chance for extra attack per round
    /// Formula: (Agility - 15) * 2 percent (requires 15+ AGI)
    /// </summary>
    public static int GetExtraAttackChance(long agility)
    {
        return Math.Max(0, Math.Min(40, (int)((agility - 15) * 2)));
    }

    /// <summary>
    /// Defense bonus from Agility (evasion component)
    /// </summary>
    public static int GetAgilityDefenseBonus(long agility)
    {
        return (int)((agility - 10) / 3);
    }

    // =====================================================
    // COMBINED CALCULATIONS
    // =====================================================

    /// <summary>
    /// Roll for a critical hit
    /// </summary>
    /// v1.1.14: rng is the combat engine's own (seeded in tests); Random.Shared otherwise.
    public static bool RollCriticalHit(Character attacker, Random? rng = null) => (rng ?? _random).Next(100) < CritChance(attacker);

    /// <summary>
    /// 1.3.2: the live basic swing's crit roll, with Umbrath's crit points (DivineBlessingSystem.GetCriticalHitBonus)
    /// added under the same ceiling as every other bonus. Only that roll passes them: a world boss hit rolls
    /// Umbrath's crit on its own (WorldBossSystem.ApplyDivineAttack), so CritChance(attacker) leaves it out.
    /// </summary>
    public static bool RollCriticalHit(Character attacker, Random? rng, int divineCritPoints) =>
        (rng ?? _random).Next(100) < CritChance(attacker, divineCritPoints);

    /// <summary>1.3.2: CritChance plus divine crit points, held to the Dexterity ceiling; never lower than CritChance.</summary>
    internal static int CritChance(Character attacker, int divineCritPoints)
    {
        int critChance = CritChance(attacker);
        if (divineCritPoints <= 0) return critChance;
        long effectiveDex = attacker.Dexterity + DrugSystem.GetDrugEffects(attacker).DexterityBonus;
        return Math.Max(critChance, Math.Min(critChance + divineCritPoints, GetCriticalHitChanceCapped(effectiveDex)));
    }

    /// <summary>
    /// The crit chance RollCriticalHit rolls against, with every bonus stacked. v1.1.10: split out so
    /// it can be read, and it now carries the fight's TempCritChanceBonus (an Old God's dialogue).
    /// </summary>
    internal static int CritChance(Character attacker)
    {
        // Apply drug DexterityBonus to effective DEX for crit calculation
        var drugEffects = DrugSystem.GetDrugEffects(attacker);
        long effectiveDex = attacker.Dexterity + drugEffects.DexterityBonus;
        int critChance = GetCriticalHitChance(effectiveDex, attacker.GetEquipmentCritChanceBonus());

        // v1.3.1: one ceiling for every bonus below, from the same Dexterity the base uses.
        int ceiling = GetCriticalHitChanceCapped(effectiveDex);

        // Apply drug CritBonus directly to crit chance (e.g., QuickSilver: +15)
        if (drugEffects.CritBonus > 0)
            critChance = Math.Clamp(critChance + drugEffects.CritBonus, 5, ceiling);

        // Divine Boon crit bonus (from worshipped player-god)
        if (attacker.CachedBoonEffects?.CritPercent > 0)
            critChance = Math.Clamp(critChance + (int)(attacker.CachedBoonEffects.CritPercent * 100), 5, ceiling);

        // v0.61.0 Druid's Shrines: Noctura attunement = +5% crit chance for 24h.
        // Same clamp as the other bonuses so the cap stays sane.
        if (attacker.IsAttunedTo("noctura"))
            critChance = Math.Clamp(critChance + (int)GameConfig.ShrineNocturaCritChanceBonus, 5, ceiling);

        // v0.61.0 Beast Taming: Cave Spider active pet = +5% crit chance.
        // Stacks with Noctura attunement, capped via the same DEX-scaled limit.
        if (attacker.HasActivePet("cave_spider"))
            critChance = Math.Clamp(critChance + 5, 5, ceiling);

        // Alignment passive — Shadow Strike (Dark/Evil) / Righteous Fury (Good/Holy):
        // flat crit bonus for committing to a path. Same DEX-scaled cap so it can't
        // push past the ceiling. (NPC allies with strong alignment benefit too -- intended.)
        int alignCrit = UsurperRemake.Systems.AlignmentSystem.Instance.GetAlignmentCritBonus(attacker);
        if (alignCrit > 0)
            critChance = Math.Clamp(critChance + alignCrit, 5, ceiling);

        // v1.1.10: this fight's bonus or penalty (an Old God's dialogue), under the same cap
        if (attacker.TempCritChanceBonus != 0)
            critChance = Math.Clamp(critChance + attacker.TempCritChanceBonus, 5, ceiling);

        return critChance;
    }

    /// <summary>v0.61.0: helper to keep crit-cap logic in one place. Returns the DEX-scaled
    /// cap (50% base, +25% scaling with DEX). Used by RollCriticalHit when stacking buffs.</summary>
    private static int GetCriticalHitChanceCapped(long dexterity)
    {
        // Mirror the cap from GetCriticalHitChance: 50 + min(25, dex/30).
        int dexBonus = Math.Min(25, (int)(dexterity / 30));
        return 50 + dexBonus;
    }

    /// <summary>
    /// Roll for dodge
    /// </summary>
    public static bool RollDodge(Character defender, Random? rng = null)   // v1.1.14: rng as in RollCriticalHit
    {
        // Apply drug AgilityBonus/AgilityPenalty to effective agility for dodge
        var drugEffects = DrugSystem.GetDrugEffects(defender);
        long effectiveAgility = defender.Agility + drugEffects.AgilityBonus - drugEffects.AgilityPenalty;
        effectiveAgility = Math.Max(0, effectiveAgility);
        int dodgeChance = GetDodgeChance(effectiveAgility);

        // Armor weight dodge bonus (Light = +10%, Medium = +5%, Heavy = +0%)
        float armorDodgeBonus = defender.GetArmorWeightTier() switch
        {
            ArmorWeightClass.Light => GameConfig.LightArmorDodgeBonus,
            ArmorWeightClass.Medium => GameConfig.MediumArmorDodgeBonus,
            _ => GameConfig.HeavyArmorDodgeBonus
        };
        dodgeChance += (int)(armorDodgeBonus * 100);
        dodgeChance = Math.Min(dodgeChance, 45); // Cap at 45% with armor bonus (base cap was 35%)

        return (rng ?? _random).Next(100) < dodgeChance;
    }

    /// <summary>
    /// Roll for extra attack this round
    /// </summary>
    public static bool RollExtraAttack(Character attacker, Random? rng = null)   // v1.1.14: rng as in RollCriticalHit
    {
        int chance = GetExtraAttackChance(attacker.Agility);
        return (rng ?? _random).Next(100) < chance;
    }
}
