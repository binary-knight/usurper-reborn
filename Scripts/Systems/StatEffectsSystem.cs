using System;
using UsurperRemake.Systems;

/// <summary>
/// Centralized stat effects system - defines how each attribute affects gameplay.
/// All stats now have meaningful mechanical effects like a modern RPG.
/// </summary>
public static class StatEffectsSystem
{
    private static Random _random = new Random();

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

    /// <summary>
    /// Carry capacity bonus (for future inventory weight system)
    /// </summary>
    public static int GetCarryCapacity(long strength)
    {
        return (int)(50 + strength * 2);
    }

    // =====================================================
    // DEXTERITY - Accuracy, ranged damage, critical hits
    // =====================================================

    /// <summary>
    /// Hit chance bonus from Dexterity
    /// Formula: (Dexterity - 10) * 2 percent (so 20 DEX = +20% hit chance)
    /// </summary>
    public static int GetDexterityHitBonus(long dexterity)
    {
        return (int)((dexterity - 10) * 2);
    }

    /// <summary>
    /// Critical hit chance from Dexterity + equipment bonuses
    /// Base 5% + (Dexterity / 10)% + equipment crit bonus, clamped to 5-50%
    /// Creator's Eye artifact: +50% crit chance (multiplicative), doubled during Manwe fight
    /// </summary>
    public static int GetCriticalHitChance(long dexterity, int equipmentCritBonus = 0)
    {
        int baseChance = 5 + (int)(dexterity / 10) + equipmentCritBonus;

        // Creator's Eye: +50% critical hit chance (multiplicative)
        if (ArtifactSystem.Instance.HasCreatorsEye())
        {
            float mult = ArtifactSystem.Instance.HasVoidKey() && CombatEngine.IsManweBattle ? 2.0f : 1.5f;
            baseChance = (int)(baseChance * mult);
        }

        return Math.Clamp(baseChance, 5, 50);
    }

    /// <summary>
    /// Critical hit damage multiplier
    /// Base 1.5x + 0.02x per Dexterity above 10 (so 20 DEX = 1.7x)
    /// Equipment crit damage bonus adds directly (e.g., +10% = +0.1x)
    /// </summary>
    public static float GetCriticalDamageMultiplier(long dexterity, int equipmentCritDamageBonus = 0)
    {
        return 1.5f + Math.Max(0, (dexterity - 10) * 0.02f) + (equipmentCritDamageBonus / 100f);
    }

    /// <summary>
    /// Ranged attack bonus damage
    /// </summary>
    public static int GetRangedDamageBonus(long dexterity)
    {
        return (int)(dexterity / 5);
    }

    // =====================================================
    // CONSTITUTION - HP, poison/disease resistance, stamina regen
    // =====================================================

    /// <summary>
    /// Bonus HP from Constitution
    /// Formula: (Constitution - 10) * 3 + Level * (Constitution / 10)
    /// </summary>
    public static long GetConstitutionHPBonus(long constitution, int level)
    {
        return (constitution - 10) * 3 + level * (constitution / 10);
    }

    /// <summary>
    /// Poison resistance percentage
    /// Formula: Constitution * 2 (so 20 CON = 40% resist)
    /// </summary>
    public static int GetPoisonResistance(long constitution)
    {
        return Math.Min(75, (int)(constitution * 2));
    }

    /// <summary>
    /// Disease resistance percentage
    /// </summary>
    public static int GetDiseaseResistance(long constitution)
    {
        return Math.Min(75, (int)(constitution * 2));
    }

    /// <summary>
    /// HP regeneration per rest (at inn, etc.)
    /// Formula: Level + Constitution / 5
    /// </summary>
    public static int GetHPRegenPerRest(long constitution, int level)
    {
        return level + (int)(constitution / 5);
    }

    /// <summary>
    /// Bonus stamina regen per round from Constitution
    /// </summary>
    public static int GetStaminaRegenBonus(long constitution)
    {
        return (int)(constitution / 15);
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
    /// Formula: 1.0 + (Intelligence - 10) * 0.04 (so 20 INT = 1.4x, 30 INT = 1.8x spell damage)
    /// This is doubled from the original to make Intelligence investment more impactful
    /// for spellcasters, similar to how Strength benefits physical damage dealers.
    /// </summary>
    public static float GetSpellDamageMultiplier(long intelligence)
    {
        return 1.0f + Math.Max(0, (intelligence - 10) * 0.04f);
    }

    /// <summary>
    /// Spell critical chance
    /// Formula: (Intelligence - 10) / 2 percent
    /// </summary>
    public static int GetSpellCriticalChance(long intelligence)
    {
        return Math.Max(0, (int)((intelligence - 10) / 2));
    }

    /// <summary>
    /// Bonus XP from learning (percentage bonus)
    /// Formula: Intelligence / 10 percent bonus
    /// </summary>
    public static int GetXPBonusPercent(long intelligence)
    {
        return (int)(intelligence / 10);
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
    /// Magic resistance percentage
    /// Formula: Wisdom / 3 + equipment magic resist bonus, capped at 50%
    /// </summary>
    public static int GetMagicResistance(long wisdom, int equipmentMagicResist = 0)
    {
        return Math.Min(50, (int)(wisdom / 3) + equipmentMagicResist);
    }

    /// <summary>
    /// Healing effectiveness multiplier
    /// Formula: 1.0 + (Wisdom - 10) * 0.015 (so 20 WIS = 1.15x, 30 WIS = 1.30x healing)
    /// Balanced to give meaningful healing bonuses from Wisdom investment.
    /// </summary>
    public static float GetHealingMultiplier(long wisdom)
    {
        return 1.0f + Math.Max(0, (wisdom - 10) * 0.015f);
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

    /// <summary>
    /// Shop price modifier (percentage reduction)
    /// Formula: Charisma / 5 percent discount (so 20 CHA = 4% cheaper)
    /// </summary>
    public static int GetShopDiscount(long charisma)
    {
        return Math.Min(25, (int)(charisma / 5));
    }

    /// <summary>
    /// NPC reaction bonus (for future dialogue system)
    /// </summary>
    public static int GetNPCReactionBonus(long charisma)
    {
        return (int)(charisma - 10);
    }

    /// <summary>
    /// Mercy/conversion chance bonus
    /// </summary>
    public static int GetMercyChanceBonus(long charisma)
    {
        return (int)(charisma * 2);
    }

    /// <summary>
    /// Team recruitment bonus (how many members you can have)
    /// </summary>
    public static int GetMaxTeamMembers(long charisma)
    {
        return 2 + (int)(charisma / 8);
    }

    // =====================================================
    // STAMINA - Combat stamina pool, physical endurance
    // =====================================================

    /// <summary>
    /// Max combat stamina (already implemented in Character.cs)
    /// Formula: 50 + (Stamina * 2) + (Level * 3)
    /// </summary>
    public static long GetMaxCombatStamina(long stamina, int level)
    {
        return 50 + (stamina * 2) + (level * 3);
    }

    /// <summary>
    /// Stamina regeneration per round
    /// Formula: 5 + (Stamina / 10)
    /// </summary>
    public static int GetStaminaRegenPerRound(long stamina)
    {
        return 5 + (int)(stamina / 10);
    }

    /// <summary>
    /// Extra dungeon fights from Stamina
    /// </summary>
    public static int GetBonusDungeonFights(long stamina)
    {
        return (int)(stamina / 15);
    }

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
    /// Initiative bonus (determines who attacks first)
    /// Formula: Agility / 2 + Level / 4
    /// </summary>
    public static int GetInitiativeBonus(long agility, int level)
    {
        return (int)(agility / 2 + level / 4);
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
    /// Escape bonus (added to escape chance)
    /// </summary>
    public static int GetEscapeBonus(long agility)
    {
        return (int)(agility / 3);
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
    /// Calculate total attack bonus from all relevant stats
    /// </summary>
    public static int GetTotalAttackBonus(Character character)
    {
        int bonus = 0;
        bonus += GetStrengthDamageBonus(character.Strength);
        bonus += GetDexterityHitBonus(character.Dexterity) / 5; // Partial contribution
        bonus += character.Level / 2;
        return bonus;
    }

    /// <summary>
    /// Calculate total defense from all relevant stats
    /// </summary>
    public static int GetTotalDefenseBonus(Character character)
    {
        int bonus = 0;
        bonus += (int)(character.Defence / 2);
        bonus += GetAgilityDefenseBonus(character.Agility);
        bonus += character.Level / 3;
        return bonus;
    }

    /// <summary>
    /// Roll for a critical hit
    /// </summary>
    public static bool RollCriticalHit(Character attacker)
    {
        // Apply drug DexterityBonus to effective DEX for crit calculation
        var drugEffects = DrugSystem.GetDrugEffects(attacker);
        long effectiveDex = attacker.Dexterity + drugEffects.DexterityBonus;
        int critChance = GetCriticalHitChance(effectiveDex, attacker.GetEquipmentCritChanceBonus());

        // Apply drug CritBonus directly to crit chance (e.g., QuickSilver: +15)
        if (drugEffects.CritBonus > 0)
            critChance = Math.Clamp(critChance + drugEffects.CritBonus, 5, 50);

        return _random.Next(100) < critChance;
    }

    /// <summary>
    /// Roll for dodge
    /// </summary>
    public static bool RollDodge(Character defender)
    {
        // Apply drug AgilityBonus/AgilityPenalty to effective agility for dodge
        var drugEffects = DrugSystem.GetDrugEffects(defender);
        long effectiveAgility = defender.Agility + drugEffects.AgilityBonus - drugEffects.AgilityPenalty;
        effectiveAgility = Math.Max(0, effectiveAgility);
        int dodgeChance = GetDodgeChance(effectiveAgility);
        return _random.Next(100) < dodgeChance;
    }

    /// <summary>
    /// Roll for poison resistance
    /// </summary>
    public static bool RollPoisonResist(Character character)
    {
        // Drug ConstitutionBonus adds to effective constitution for poison resistance (e.g., Ironhide: +25)
        var drugEffects = DrugSystem.GetDrugEffects(character);
        long effectiveCon = character.Constitution + drugEffects.ConstitutionBonus;
        int resist = GetPoisonResistance(effectiveCon);
        return _random.Next(100) < resist;
    }

    /// <summary>
    /// Roll for magic resistance
    /// </summary>
    public static bool RollMagicResist(Character character)
    {
        int resist = GetMagicResistance(character.Wisdom, character.GetEquipmentMagicResistance());
        return _random.Next(100) < resist;
    }

    /// <summary>
    /// Roll for extra attack this round
    /// </summary>
    public static bool RollExtraAttack(Character attacker)
    {
        int chance = GetExtraAttackChance(attacker.Agility);
        return _random.Next(100) < chance;
    }

    /// <summary>
    /// Get descriptive tooltip for a stat
    /// </summary>
    public static string GetStatDescription(string statName, Character character)
    {
        return statName.ToLower() switch
        {
            "strength" => $"Strength ({character.Strength})\n" +
                          $"  +{GetStrengthDamageBonus(character.Strength)} melee damage\n" +
                          $"  Affects: Physical attacks, carrying capacity",

            "dexterity" => $"Dexterity ({character.Dexterity})\n" +
                           $"  +{GetDexterityHitBonus(character.Dexterity)}% hit chance\n" +
                           $"  {GetCriticalHitChance(character.Dexterity, character.GetEquipmentCritChanceBonus())}% critical chance\n" +
                           $"  Affects: Accuracy, crits, ranged attacks, escaping",

            "constitution" => $"Constitution ({character.Constitution})\n" +
                              $"  +{GetConstitutionHPBonus(character.Constitution, character.Level)} max HP\n" +
                              $"  {GetPoisonResistance(character.Constitution)}% poison resist\n" +
                              $"  Affects: Health, resistances, stamina regen",

            "intelligence" => $"Intelligence ({character.Intelligence})\n" +
                              $"  +{GetIntelligenceManaBonus(character.Intelligence, character.Level)} max mana\n" +
                              $"  {GetSpellDamageMultiplier(character.Intelligence):P0} spell damage\n" +
                              $"  +{GetXPBonusPercent(character.Intelligence)}% XP bonus\n" +
                              $"  Affects: Spell power, mana pool, learning",

            "wisdom" => $"Wisdom ({character.Wisdom})\n" +
                        $"  -{GetManaCostReduction(character.Wisdom)}% mana costs\n" +
                        $"  {GetMagicResistance(character.Wisdom, character.GetEquipmentMagicResistance())}% magic resist\n" +
                        $"  {GetHealingMultiplier(character.Wisdom):P0} healing power\n" +
                        $"  Affects: Spell efficiency, resistance, healing",

            "charisma" => $"Charisma ({character.Charisma})\n" +
                          $"  {GetShopDiscount(character.Charisma)}% shop discount\n" +
                          $"  Max team: {GetMaxTeamMembers(character.Charisma)} members\n" +
                          $"  Affects: Prices, NPC reactions, mercy",

            "stamina" => $"Stamina ({character.Stamina})\n" +
                         $"  {GetMaxCombatStamina(character.Stamina, character.Level)} combat stamina\n" +
                         $"  +{GetStaminaRegenPerRound(character.Stamina)} stamina/round\n" +
                         $"  +{GetBonusDungeonFights(character.Stamina)} dungeon fights\n" +
                         $"  Affects: Ability usage, endurance",

            "agility" => $"Agility ({character.Agility})\n" +
                         $"  {GetDodgeChance(character.Agility)}% dodge chance\n" +
                         $"  +{GetInitiativeBonus(character.Agility, character.Level)} initiative\n" +
                         $"  {GetExtraAttackChance(character.Agility)}% extra attack\n" +
                         $"  Affects: Evasion, speed, extra attacks",

            _ => $"{statName}: No description available"
        };
    }
}
