using System;
using System.Collections.Generic;

/// <summary>
/// Monster special ability definitions and execution logic
/// Provides unique combat behaviors for different monster types
/// </summary>
public static class MonsterAbilities
{
    private static Random _rnd = new Random();

    /// <summary>
    /// All available monster abilities
    /// </summary>
    public enum AbilityType
    {
        None,

        // Attack Modifiers
        Multiattack,        // Attack 2-3 times per round
        CrushingBlow,       // High damage single attack, chance to stun
        VenomousBite,       // Attack + poison
        BleedingWound,      // Attack + bleed
        FireBreath,         // AoE fire damage + burn
        FrostBreath,        // AoE cold damage + freeze
        PoisonCloud,        // AoE poison
        LifeDrain,          // Damage + heal self
        ManaDrain,          // Drain mana from target

        // Defensive Abilities
        Regeneration,       // Heal HP each round
        Thorns,             // Reflect damage to attackers
        ArmorHarden,        // Temporarily boost defense
        Vanish,             // Become harder to hit
        Phase,              // 25% chance to avoid all damage

        // Status Effects
        PetrifyingGaze,     // Chance to stun
        HorrifyingScream,   // Fear effect, reduce damage dealt
        BlindingFlash,      // Blind the player
        Curse,              // Apply curse debuff
        Silence,            // Prevent spell casting
        Enfeeble,           // Reduce player strength

        // Special Attacks
        Devour,             // Instant kill attempt on low HP targets
        Berserk,            // When low HP, go berserk
        SummonMinions,      // Call additional monsters
        Explosion,          // Suicide attack on death
        SoulReap,           // Chance to instantly kill
        Backstab,           // Extra damage from ambush

        // Utility
        Flee,               // Attempt to escape
        CallForHelp,        // Alert nearby monsters
        Enrage,             // Buff self when damaged
        Heal                // Heal self significantly
    }

    /// <summary>
    /// Get abilities for a monster based on its family and tier
    /// </summary>
    public static List<AbilityType> GetAbilitiesForMonster(string family, int tier, bool isBoss)
    {
        var abilities = new List<AbilityType>();

        // Base abilities by family
        switch (family.ToLower())
        {
            case "goblinoid":
                if (tier >= 2) abilities.Add(AbilityType.CallForHelp);
                if (tier >= 3) abilities.Add(AbilityType.Backstab);
                if (tier >= 4) abilities.Add(AbilityType.Enfeeble);
                break;

            case "undead":
                abilities.Add(AbilityType.LifeDrain);
                if (tier >= 2) abilities.Add(AbilityType.Curse);
                if (tier >= 3) abilities.Add(AbilityType.SoulReap);
                if (tier >= 4) abilities.Add(AbilityType.PetrifyingGaze);
                break;

            case "beast":
                abilities.Add(AbilityType.Multiattack);
                if (tier >= 2) abilities.Add(AbilityType.BleedingWound);
                if (tier >= 3) abilities.Add(AbilityType.VenomousBite);
                if (tier >= 4) abilities.Add(AbilityType.Berserk);
                break;

            case "reptilian":
                abilities.Add(AbilityType.VenomousBite);
                if (tier >= 2) abilities.Add(AbilityType.ArmorHarden);
                if (tier >= 3) abilities.Add(AbilityType.Regeneration);
                if (tier >= 4) abilities.Add(AbilityType.PoisonCloud);
                break;

            case "dragon":
                abilities.Add(AbilityType.FireBreath);
                abilities.Add(AbilityType.ArmorHarden);
                if (tier >= 2) abilities.Add(AbilityType.HorrifyingScream);
                if (tier >= 3) abilities.Add(AbilityType.Multiattack);
                if (tier >= 4) abilities.Add(AbilityType.Phase);
                break;

            case "demon":
                abilities.Add(AbilityType.LifeDrain);
                abilities.Add(AbilityType.Curse);
                if (tier >= 2) abilities.Add(AbilityType.FireBreath);
                if (tier >= 3) abilities.Add(AbilityType.SummonMinions);
                if (tier >= 4) abilities.Add(AbilityType.SoulReap);
                break;

            case "elemental":
                if (tier >= 2) abilities.Add(AbilityType.Phase);
                if (tier >= 3) abilities.Add(AbilityType.Explosion);
                if (tier >= 4) abilities.Add(AbilityType.Regeneration);
                break;

            case "humanoid":
                if (tier >= 2) abilities.Add(AbilityType.Backstab);
                if (tier >= 3) abilities.Add(AbilityType.CrushingBlow);
                if (tier >= 4) abilities.Add(AbilityType.Enrage);
                break;

            case "insect":
                abilities.Add(AbilityType.VenomousBite);
                if (tier >= 2) abilities.Add(AbilityType.PoisonCloud);
                if (tier >= 3) abilities.Add(AbilityType.CallForHelp);
                if (tier >= 4) abilities.Add(AbilityType.Multiattack);
                break;

            case "giant":
                abilities.Add(AbilityType.CrushingBlow);
                if (tier >= 2) abilities.Add(AbilityType.HorrifyingScream);
                if (tier >= 3) abilities.Add(AbilityType.Enrage);
                if (tier >= 4) abilities.Add(AbilityType.Devour);
                break;

            case "arcane":
                abilities.Add(AbilityType.ManaDrain);
                abilities.Add(AbilityType.Silence);
                if (tier >= 2) abilities.Add(AbilityType.BlindingFlash);
                if (tier >= 3) abilities.Add(AbilityType.Phase);
                if (tier >= 4) abilities.Add(AbilityType.SoulReap);
                break;

            default:
                // Generic monsters get basic abilities based on tier
                if (tier >= 2) abilities.Add(AbilityType.Multiattack);
                if (tier >= 3) abilities.Add(AbilityType.Enrage);
                break;
        }

        // Boss monsters get additional abilities
        if (isBoss)
        {
            abilities.Add(AbilityType.Enrage);
            abilities.Add(AbilityType.Regeneration);
            if (!abilities.Contains(AbilityType.Multiattack))
                abilities.Add(AbilityType.Multiattack);
        }

        return abilities;
    }

    /// <summary>
    /// Execute a monster ability and return the result
    /// </summary>
    public static AbilityResult ExecuteAbility(AbilityType ability, Monster monster, Character target)
    {
        var result = new AbilityResult { AbilityUsed = ability };

        switch (ability)
        {
            case AbilityType.Multiattack:
                result.ExtraAttacks = _rnd.Next(1, 3); // 1-2 extra attacks
                result.Message = $"{monster.Name} attacks in a flurry of blows!";
                result.MessageColor = "yellow";
                break;

            case AbilityType.CrushingBlow:
                result.DamageMultiplier = 2.0f;
                result.InflictStatus = StatusEffect.Stunned;
                result.StatusDuration = 1;
                result.StatusChance = 25;
                result.Message = $"{monster.Name} delivers a CRUSHING BLOW!";
                result.MessageColor = "bright_red";
                break;

            case AbilityType.VenomousBite:
                result.DamageMultiplier = 0.8f;
                result.InflictStatus = StatusEffect.Poisoned;
                result.StatusDuration = 5;
                result.StatusChance = 60;
                result.Message = $"{monster.Name} bites with venomous fangs!";
                result.MessageColor = "green";
                break;

            case AbilityType.BleedingWound:
                result.DamageMultiplier = 1.0f;
                result.InflictStatus = StatusEffect.Bleeding;
                result.StatusDuration = 4;
                result.StatusChance = 50;
                result.Message = $"{monster.Name} tears a gaping wound!";
                result.MessageColor = "red";
                break;

            case AbilityType.FireBreath:
                result.DirectDamage = CalculateBreathDamage(monster, 1.5f);
                result.InflictStatus = StatusEffect.Burning;
                result.StatusDuration = 3;
                result.StatusChance = 40;
                result.SkipNormalAttack = true;
                result.Message = $"{monster.Name} breathes a cone of fire!";
                result.MessageColor = "bright_red";
                break;

            case AbilityType.FrostBreath:
                result.DirectDamage = CalculateBreathDamage(monster, 1.2f);
                result.InflictStatus = StatusEffect.Frozen;
                result.StatusDuration = 2;
                result.StatusChance = 50;
                result.SkipNormalAttack = true;
                result.Message = $"{monster.Name} exhales a blast of frost!";
                result.MessageColor = "bright_cyan";
                break;

            case AbilityType.PoisonCloud:
                result.DirectDamage = CalculateBreathDamage(monster, 0.8f);
                result.InflictStatus = StatusEffect.Poisoned;
                result.StatusDuration = 6;
                result.StatusChance = 70;
                result.SkipNormalAttack = true;
                result.Message = $"{monster.Name} releases a cloud of poison!";
                result.MessageColor = "green";
                break;

            case AbilityType.LifeDrain:
                result.DamageMultiplier = 0.7f;
                result.LifeStealPercent = 50;
                result.Message = $"{monster.Name} drains your life force!";
                result.MessageColor = "magenta";
                break;

            case AbilityType.ManaDrain:
                result.ManaDrain = Math.Min(target.Mana, monster.Level * 5 + _rnd.Next(5, 15));
                result.SkipNormalAttack = true;
                result.Message = $"{monster.Name} drains {result.ManaDrain} mana!";
                result.MessageColor = "bright_blue";
                break;

            case AbilityType.Regeneration:
                var healAmount = Math.Max(5, monster.MaxHP / 10);
                monster.HP = Math.Min(monster.HP + healAmount, monster.MaxHP);
                result.SkipNormalAttack = false; // Can still attack
                result.Message = $"{monster.Name} regenerates {healAmount} HP!";
                result.MessageColor = "bright_green";
                break;

            case AbilityType.Thorns:
                result.ReflectDamagePercent = 25;
                result.Message = $"{monster.Name}'s thorny hide wounds attackers!";
                result.MessageColor = "yellow";
                break;

            case AbilityType.ArmorHarden:
                // Prevent infinite stacking - can only harden once per combat
                if (!monster.HasHardenedArmor)
                {
                    monster.ArmPow += monster.Level / 2;
                    monster.HasHardenedArmor = true;
                    result.Message = $"{monster.Name}'s armor hardens!";
                    result.MessageColor = "gray";
                }
                else
                {
                    result.Message = $"{monster.Name}'s armor is already hardened!";
                    result.MessageColor = "darkgray";
                }
                result.SkipNormalAttack = true;
                break;

            case AbilityType.Vanish:
                result.EvasionBonus = 30;
                result.Message = $"{monster.Name} fades into the shadows!";
                result.MessageColor = "darkgray";
                break;

            case AbilityType.Phase:
                if (_rnd.Next(100) < 25)
                {
                    result.AvoidAllDamage = true;
                    result.Message = $"{monster.Name} phases out of reality!";
                    result.MessageColor = "bright_cyan";
                }
                break;

            case AbilityType.PetrifyingGaze:
                result.InflictStatus = StatusEffect.Stunned;
                result.StatusDuration = 2;
                result.StatusChance = 30;
                result.SkipNormalAttack = true;
                result.Message = $"{monster.Name}'s gaze freezes you in place!";
                result.MessageColor = "gray";
                break;

            case AbilityType.HorrifyingScream:
                result.InflictStatus = StatusEffect.Feared;
                result.StatusDuration = 3;
                result.StatusChance = 40;
                result.SkipNormalAttack = true;
                result.Message = $"{monster.Name} unleashes a horrifying scream!";
                result.MessageColor = "magenta";
                break;

            case AbilityType.BlindingFlash:
                result.InflictStatus = StatusEffect.Blinded;
                result.StatusDuration = 3;
                result.StatusChance = 50;
                result.SkipNormalAttack = true;
                result.Message = $"{monster.Name} creates a blinding flash!";
                result.MessageColor = "bright_yellow";
                break;

            case AbilityType.Curse:
                result.InflictStatus = StatusEffect.Cursed;
                result.StatusDuration = 5;
                result.StatusChance = 45;
                result.SkipNormalAttack = true;
                result.Message = $"{monster.Name} casts a dark curse!";
                result.MessageColor = "magenta";
                break;

            case AbilityType.Silence:
                result.InflictStatus = StatusEffect.Silenced;
                result.StatusDuration = 4;
                result.StatusChance = 40;
                result.SkipNormalAttack = true;
                result.Message = $"{monster.Name} silences your magic!";
                result.MessageColor = "bright_blue";
                break;

            case AbilityType.Enfeeble:
                result.InflictStatus = StatusEffect.Weakened;
                result.StatusDuration = 4;
                result.StatusChance = 50;
                result.SkipNormalAttack = true;
                result.Message = $"{monster.Name} saps your strength!";
                result.MessageColor = "yellow";
                break;

            case AbilityType.Devour:
                // Only works on targets below 20% HP
                if (target.HP < target.MaxHP / 5)
                {
                    result.DirectDamage = (int)target.HP; // Instant kill
                    result.Message = $"{monster.Name} DEVOURS you whole!";
                    result.MessageColor = "bright_red";
                }
                else
                {
                    result.DamageMultiplier = 1.3f;
                    result.Message = $"{monster.Name} tries to devour you!";
                    result.MessageColor = "red";
                }
                break;

            case AbilityType.Berserk:
                // Triggers when monster is low HP
                if (monster.HP < monster.MaxHP / 3)
                {
                    result.DamageMultiplier = 2.0f;
                    result.ExtraAttacks = 1;
                    result.Message = $"{monster.Name} goes BERSERK!";
                    result.MessageColor = "bright_red";
                }
                break;

            case AbilityType.SummonMinions:
                result.SummonMonsters = true;
                result.SummonCount = _rnd.Next(1, 3);
                result.SkipNormalAttack = true;
                result.Message = $"{monster.Name} calls for reinforcements!";
                result.MessageColor = "yellow";
                break;

            case AbilityType.Explosion:
                // Death explosion - handled when monster dies
                result.OnDeathDamage = (int)(monster.MaxHP / 2);
                result.Message = $"{monster.Name} explodes violently!";
                result.MessageColor = "bright_red";
                break;

            case AbilityType.SoulReap:
                // Small chance of instant kill
                if (_rnd.Next(100) < 5) // 5% chance
                {
                    result.DirectDamage = (int)target.HP;
                    result.Message = $"{monster.Name} reaps your soul!";
                    result.MessageColor = "bright_red";
                }
                else
                {
                    result.DamageMultiplier = 1.5f;
                    result.Message = $"{monster.Name} reaches for your soul!";
                    result.MessageColor = "magenta";
                }
                break;

            case AbilityType.Backstab:
                // Extra damage only on first round (surprise attack)
                if (!monster.HasUsedBackstab && monster.CombatRound <= 1)
                {
                    monster.HasUsedBackstab = true;
                    result.DamageMultiplier = 2.5f;
                    result.Message = $"{monster.Name} strikes from the shadows!";
                    result.MessageColor = "darkgray";
                }
                else
                {
                    // Normal attack after the element of surprise is gone
                    result.DamageMultiplier = 1.0f;
                    result.Message = $"{monster.Name} attacks!";
                    result.MessageColor = "white";
                }
                break;

            case AbilityType.CallForHelp:
                result.SummonMonsters = true;
                result.SummonCount = 1;
                result.SkipNormalAttack = true;
                result.Message = $"{monster.Name} calls for help!";
                result.MessageColor = "yellow";
                break;

            case AbilityType.Enrage:
                result.DamageMultiplier = 1.5f;
                // Prevent infinite stacking - permanent strength boost only once per combat
                if (!monster.HasEnraged)
                {
                    monster.Strength += 5;
                    monster.HasEnraged = true;
                    result.Message = $"{monster.Name} becomes enraged!";
                }
                else
                {
                    result.Message = $"{monster.Name} rages furiously!";
                }
                result.MessageColor = "red";
                break;

            case AbilityType.Heal:
                var bigHeal = Math.Max(20, monster.MaxHP / 4);
                monster.HP = Math.Min(monster.HP + bigHeal, monster.MaxHP);
                result.SkipNormalAttack = true;
                result.Message = $"{monster.Name} heals for {bigHeal} HP!";
                result.MessageColor = "bright_green";
                break;

            case AbilityType.Flee:
                result.MonsterFlees = true;
                result.SkipNormalAttack = true;
                result.Message = $"{monster.Name} attempts to flee!";
                result.MessageColor = "yellow";
                break;
        }

        return result;
    }

    /// <summary>
    /// Calculate breath weapon damage
    /// </summary>
    private static int CalculateBreathDamage(Monster monster, float multiplier)
    {
        int baseDamage = (int)(monster.Level * 3 + monster.Strength / 2);
        return (int)(baseDamage * multiplier) + _rnd.Next(5, 15);
    }

    /// <summary>
    /// Decide which ability the monster should use this turn
    /// </summary>
    public static AbilityType DecideAbility(Monster monster, Character target, int combatRound, List<AbilityType> availableAbilities)
    {
        if (availableAbilities == null || availableAbilities.Count == 0)
            return AbilityType.None;

        // Low HP triggers certain abilities
        bool isLowHP = monster.HP < monster.MaxHP / 3;
        bool isVeryLowHP = monster.HP < monster.MaxHP / 5;

        // Priority 1: Healing when low
        if (isLowHP && availableAbilities.Contains(AbilityType.Heal) && _rnd.Next(100) < 60)
            return AbilityType.Heal;

        if (isLowHP && availableAbilities.Contains(AbilityType.Regeneration) && _rnd.Next(100) < 40)
            return AbilityType.Regeneration;

        // Priority 2: Berserk when low
        if (isLowHP && availableAbilities.Contains(AbilityType.Berserk))
            return AbilityType.Berserk;

        // Priority 3: Flee when very low (cowardly monsters)
        if (isVeryLowHP && availableAbilities.Contains(AbilityType.Flee) && _rnd.Next(100) < 30)
            return AbilityType.Flee;

        // Priority 4: Devour low HP targets
        if (target.HP < target.MaxHP / 5 && availableAbilities.Contains(AbilityType.Devour))
            return AbilityType.Devour;

        // Priority 5: First round specials
        if (combatRound == 1)
        {
            if (availableAbilities.Contains(AbilityType.Backstab) && _rnd.Next(100) < 70)
                return AbilityType.Backstab;
            if (availableAbilities.Contains(AbilityType.HorrifyingScream) && _rnd.Next(100) < 50)
                return AbilityType.HorrifyingScream;
        }

        // Priority 6: Summon when outnumbered or hurt
        if (isLowHP && availableAbilities.Contains(AbilityType.SummonMinions) && _rnd.Next(100) < 40)
            return AbilityType.SummonMinions;

        if (availableAbilities.Contains(AbilityType.CallForHelp) && combatRound <= 2 && _rnd.Next(100) < 25)
            return AbilityType.CallForHelp;

        // Priority 7: Crowd control abilities
        if (!target.HasStatus(StatusEffect.Stunned) && availableAbilities.Contains(AbilityType.PetrifyingGaze) && _rnd.Next(100) < 25)
            return AbilityType.PetrifyingGaze;

        if (!target.HasStatus(StatusEffect.Silenced) && availableAbilities.Contains(AbilityType.Silence) && target.Mana > 0 && _rnd.Next(100) < 35)
            return AbilityType.Silence;

        if (!target.HasStatus(StatusEffect.Blinded) && availableAbilities.Contains(AbilityType.BlindingFlash) && _rnd.Next(100) < 30)
            return AbilityType.BlindingFlash;

        // Priority 8: DoT abilities if target doesn't have them
        if (!target.HasStatus(StatusEffect.Poisoned) && availableAbilities.Contains(AbilityType.VenomousBite) && _rnd.Next(100) < 40)
            return AbilityType.VenomousBite;

        if (!target.HasStatus(StatusEffect.Bleeding) && availableAbilities.Contains(AbilityType.BleedingWound) && _rnd.Next(100) < 35)
            return AbilityType.BleedingWound;

        if (!target.HasStatus(StatusEffect.Burning) && availableAbilities.Contains(AbilityType.FireBreath) && _rnd.Next(100) < 30)
            return AbilityType.FireBreath;

        // Priority 9: Random special attacks
        var offensiveAbilities = new List<AbilityType>();
        foreach (var ability in availableAbilities)
        {
            if (ability == AbilityType.CrushingBlow || ability == AbilityType.LifeDrain ||
                ability == AbilityType.ManaDrain || ability == AbilityType.Multiattack ||
                ability == AbilityType.SoulReap || ability == AbilityType.Enrage)
            {
                offensiveAbilities.Add(ability);
            }
        }

        if (offensiveAbilities.Count > 0 && _rnd.Next(100) < 30)
        {
            return offensiveAbilities[_rnd.Next(offensiveAbilities.Count)];
        }

        // Default: 60% chance to use no ability (normal attack)
        if (_rnd.Next(100) < 60)
            return AbilityType.None;

        // Otherwise pick a random ability
        return availableAbilities[_rnd.Next(availableAbilities.Count)];
    }

    /// <summary>
    /// Get a list of ability type from string names
    /// </summary>
    public static List<AbilityType> ParseAbilityStrings(List<string> abilityNames)
    {
        var abilities = new List<AbilityType>();
        foreach (var name in abilityNames)
        {
            if (Enum.TryParse<AbilityType>(name, true, out var ability))
            {
                abilities.Add(ability);
            }
        }
        return abilities;
    }
}

/// <summary>
/// Result of executing a monster ability
/// </summary>
public class AbilityResult
{
    public MonsterAbilities.AbilityType AbilityUsed { get; set; }
    public string Message { get; set; } = "";
    public string MessageColor { get; set; } = "white";

    // Damage modifiers
    public float DamageMultiplier { get; set; } = 1.0f;
    public int DirectDamage { get; set; } = 0;
    public int ExtraAttacks { get; set; } = 0;
    public bool SkipNormalAttack { get; set; } = false;

    // Status effects
    public StatusEffect InflictStatus { get; set; } = StatusEffect.None;
    public int StatusDuration { get; set; } = 0;
    public int StatusChance { get; set; } = 100; // Percent chance to apply

    // Life/mana drain
    public int LifeStealPercent { get; set; } = 0;
    public long ManaDrain { get; set; } = 0;

    // Defensive abilities
    public int ReflectDamagePercent { get; set; } = 0;
    public int EvasionBonus { get; set; } = 0;
    public bool AvoidAllDamage { get; set; } = false;

    // Summon/flee
    public bool SummonMonsters { get; set; } = false;
    public int SummonCount { get; set; } = 0;
    public bool MonsterFlees { get; set; } = false;

    // Death effects
    public int OnDeathDamage { get; set; } = 0;
}
