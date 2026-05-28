using System;
using System.Collections.Generic;
using UsurperRemake.Systems;

/// <summary>
/// Monster special ability definitions and execution logic
/// Provides unique combat behaviors for different monster types
/// </summary>
public static class MonsterAbilities
{
    private static Random _rnd = Random.Shared;

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
        Heal,               // Heal self significantly

        // --- Monster Family Abilities (from MonsterFamilies.cs) ---

        // Goblinoid
        CriticalStrike,     // 2x damage single hit
        Rally,              // Buff self: temporary strength boost
        CommandArmy,        // Summon goblin reinforcements

        // Undead
        Paralyze,           // Chance to stun (like PetrifyingGaze)
        Incorporeal,        // Phase-like: chance to avoid damage
        Spellcasting,       // Cast random offensive spell
        Phylactery,         // Self-heal when low HP

        // Orc
        Rage,               // Damage boost (like Enrage)
        Frenzy,             // Multi-attack + damage boost
        Warcry,             // Fear effect on player
        Cleave,             // High damage attack

        // Dragon
        Flight,             // Evasion bonus (like Vanish)
        DragonFear,         // Fear effect (like HorrifyingScream)
        AncientMagic,       // High direct damage magical attack

        // Demon
        Invisibility,       // Evasion bonus (like Vanish)
        Teleport,           // Skip attack, gain evasion next round
        Hellfire,           // High fire damage + burn
        Corruption,         // Curse + weaken
        Dominate,           // Charm: player may skip turn

        // Giant
        Boulder,            // High direct damage ranged attack
        Stoneskin,          // Armor harden (like ArmorHarden)
        Lightning,          // High direct damage + stun chance
        Earthquake,         // Direct damage + stun chance

        // Beast/Wolf
        PackTactics,        // Extra attack (like Multiattack but 1 extra)
        Bite,               // Attack + bleed
        Lycanthropy,        // Curse + bleed
        Howl,               // Fear effect (like HorrifyingScream)
        Moonlight,          // Regeneration + damage boost

        // Fire Elemental
        Burn,               // Attack + burn DoT
        Immolate,           // High fire damage + burn
        Fireball,           // Direct fire damage
        Rebirth,            // Self-heal to full when low HP (once)
        Inferno,            // Massive fire damage

        // Ooze/Slime
        Corrosion,          // Reduce player defense
        Split,              // Summon copy of self
        Engulf,             // High damage + stun
        Absorb,             // Damage + heal self
        ShapeShift,         // Random stat changes
        Madness,            // Confusion effect

        // Spider/Insect
        WebTrap,            // Stun/slow effect
        PhaseShift,         // Phase-like dodge
        Poison,             // Apply poison DoT
        SummonSpiders,      // Summon minions
        DeadlyVenom,        // Strong poison + damage
        Swarm,              // Multi-attack swarm
        Cocoon,             // Heal self + armor boost

        // Construct/Golem
        ImmuneMagic,        // Resist magic (passive, reduces spell damage)
        PoisonGas,          // AoE poison (like PoisonCloud)
        Indestructible,     // Massive armor boost
        SelfRepair,         // Heal self (like Heal)
        Overload,           // Massive damage, self-damage

        // Fey
        Sleep,              // Put player to sleep (stun)
        TreeMeld,           // Phase-like evasion
        Charm,              // Player may skip turn
        AnimateTrees,       // Summon minions
        RootEntangle,       // Stun + damage
        TimeStop,           // Extra attacks
        WildShape,          // Stat boost + heal

        // Sea Creature
        TentacleGrab,       // Multi-attack + stun chance
        InkCloud,           // Blind + evasion boost
        Whirlpool,          // Direct damage + stun
        TidalWave,          // High direct damage

        // Celestial
        HolySmite,          // High direct damage
        Purify,             // Remove player buffs
        DivineJudgment,     // Massive damage on evil-aligned
        Sanctuary,          // Heal + armor boost
        Resurrection,       // Revive dead allies in multi-monster

        // Shadow
        StrengthDrain,      // Reduce player strength
        Terror,             // Fear + damage
        Possess,            // Player attacks self
        Nightmare,          // Direct damage + fear
        DevourSoul,         // Soul reap variant
        RealityBreak        // Direct damage + random debuff
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

        // Target pronouns: player target reads as "you"/"your"; companions and NPC teammates use their name.
        bool isPlayerTarget = target is Player;
        string you = isPlayerTarget ? "you" : target.Name;
        string your = isPlayerTarget ? "your" : $"{target.Name}'s";

        switch (ability)
        {
            case AbilityType.Multiattack:
                result.ExtraAttacks = _rnd.Next(1, 3); // 1-2 extra attacks
                result.Message = Loc.Get("mability.multiattack", monster.Name);
                result.MessageColor = "yellow";
                break;

            case AbilityType.CrushingBlow:
                result.DamageMultiplier = 2.0f;
                result.InflictStatus = StatusEffect.Stunned;
                result.StatusDuration = 2; // +1 for ProcessStatusEffects off-by-one = 1 effective round of stun
                result.StatusChance = 25;
                result.Message = Loc.Get("mability.crushing_blow", monster.Name);
                result.MessageColor = "bright_red";
                break;

            case AbilityType.VenomousBite:
                result.DamageMultiplier = 0.8f;
                result.InflictStatus = StatusEffect.Poisoned;
                result.StatusDuration = 5;
                result.StatusChance = 60;
                result.Message = Loc.Get("mability.venomous_bite", monster.Name);
                result.MessageColor = "green";
                break;

            case AbilityType.BleedingWound:
                result.DamageMultiplier = 1.0f;
                result.InflictStatus = StatusEffect.Bleeding;
                result.StatusDuration = 4;
                result.StatusChance = 50;
                result.Message = Loc.Get("mability.bleeding_wound", monster.Name);
                result.MessageColor = "red";
                break;

            case AbilityType.FireBreath:
                result.DirectDamage = CalculateBreathDamage(monster, 1.5f);
                result.InflictStatus = StatusEffect.Burning;
                result.StatusDuration = 3;
                result.StatusChance = 40;
                result.SkipNormalAttack = true;
                result.Message = Loc.Get("mability.fire_breath", monster.Name);
                result.MessageColor = "bright_red";
                break;

            case AbilityType.FrostBreath:
                result.DirectDamage = CalculateBreathDamage(monster, 1.2f);
                result.InflictStatus = StatusEffect.Frozen;
                result.StatusDuration = 2;
                result.StatusChance = 50;
                result.SkipNormalAttack = true;
                result.Message = Loc.Get("mability.frost_breath", monster.Name);
                result.MessageColor = "bright_cyan";
                break;

            case AbilityType.PoisonCloud:
                result.DirectDamage = CalculateBreathDamage(monster, 0.8f);
                result.InflictStatus = StatusEffect.Poisoned;
                result.StatusDuration = 6;
                result.StatusChance = 70;
                result.SkipNormalAttack = true;
                result.Message = Loc.Get("mability.poison_cloud", monster.Name);
                result.MessageColor = "green";
                break;

            case AbilityType.LifeDrain:
                result.DamageMultiplier = 0.7f;
                result.LifeStealPercent = 50;
                result.Message = (isPlayerTarget ? Loc.Get("mability.life_drain.you", monster.Name) : Loc.Get("mability.life_drain.ally", monster.Name, target.Name));
                result.MessageColor = "magenta";
                break;

            case AbilityType.ManaDrain:
                result.ManaDrain = Math.Min(target.Mana, monster.Level * 5 + _rnd.Next(5, 15));
                result.SkipNormalAttack = true;
                result.Message = Loc.Get("mability.mana_drain", monster.Name, result.ManaDrain);
                result.MessageColor = "bright_blue";
                break;

            case AbilityType.Regeneration:
                var healAmount = Math.Max(5, monster.MaxHP / 10);
                monster.HP = Math.Min(monster.HP + healAmount, monster.MaxHP);
                result.DamageMultiplier = 0; // Heal only — no damage to target
                result.SkipNormalAttack = false; // Can still attack normally
                result.IsSelfOnly = true; // No "attacks!" message needed
                result.Message = Loc.Get("mability.regeneration", monster.Name, healAmount);
                result.MessageColor = "bright_green";
                break;

            case AbilityType.Thorns:
                result.DamageMultiplier = 0; // Passive reflect — no direct damage
                result.ReflectDamagePercent = 25;
                result.Message = Loc.Get("mability.thorns", monster.Name);
                result.MessageColor = "yellow";
                break;

            case AbilityType.ArmorHarden:
                // Prevent infinite stacking - can only harden once per combat
                if (!monster.HasHardenedArmor)
                {
                    monster.ArmPow += monster.Level / 2;
                    monster.HasHardenedArmor = true;
                    result.Message = Loc.Get("mability.armor_harden", monster.Name);
                    result.MessageColor = "gray";
                }
                else
                {
                    result.Message = Loc.Get("mability.armor_harden_already", monster.Name);
                    result.MessageColor = "darkgray";
                }
                result.DamageMultiplier = 0; // Buff only — no damage
                result.SkipNormalAttack = true;
                break;

            case AbilityType.Vanish:
                result.DamageMultiplier = 0; // Evasion buff — no damage
                result.EvasionBonus = 30;
                // v0.61.2: persist evasion onto the monster so player attacks
                // during the buff window actually have a chance to miss.
                monster.EvasionRounds = 2;
                monster.EvasionMissChance = 30;
                result.Message = Loc.Get("mability.vanish", monster.Name);
                result.MessageColor = "darkgray";
                break;

            case AbilityType.Phase:
                result.DamageMultiplier = 0; // Damage avoidance — no damage
                if (_rnd.Next(100) < 25)
                {
                    result.AvoidAllDamage = true;
                    monster.EvasionRounds = 2;
                    monster.EvasionMissChance = 50;
                    result.Message = Loc.Get("mability.phase", monster.Name);
                    result.MessageColor = "bright_cyan";
                }
                break;

            case AbilityType.PetrifyingGaze:
                result.InflictStatus = StatusEffect.Stunned;
                result.StatusDuration = 2;
                result.StatusChance = 30;
                result.SkipNormalAttack = true;
                result.Message = (isPlayerTarget ? Loc.Get("mability.petrifying_gaze.you", monster.Name) : Loc.Get("mability.petrifying_gaze.ally", monster.Name, target.Name));
                result.MessageColor = "gray";
                break;

            case AbilityType.HorrifyingScream:
                result.InflictStatus = StatusEffect.Feared;
                result.StatusDuration = 3;
                result.StatusChance = 40;
                result.SkipNormalAttack = true;
                result.Message = Loc.Get("mability.horrifying_scream", monster.Name);
                result.MessageColor = "magenta";
                break;

            case AbilityType.BlindingFlash:
                result.InflictStatus = StatusEffect.Blinded;
                result.StatusDuration = 3;
                result.StatusChance = 50;
                result.SkipNormalAttack = true;
                result.Message = Loc.Get("mability.blinding_flash", monster.Name);
                result.MessageColor = "bright_yellow";
                break;

            case AbilityType.Curse:
                result.InflictStatus = StatusEffect.Cursed;
                result.StatusDuration = 5;
                result.StatusChance = 45;
                result.SkipNormalAttack = true;
                result.Message = Loc.Get("mability.curse", monster.Name);
                result.MessageColor = "magenta";
                break;

            case AbilityType.Silence:
                result.InflictStatus = StatusEffect.Silenced;
                result.StatusDuration = 4;
                result.StatusChance = 40;
                result.SkipNormalAttack = true;
                result.Message = (isPlayerTarget ? Loc.Get("mability.silence.you", monster.Name) : Loc.Get("mability.silence.ally", monster.Name, target.Name));
                result.MessageColor = "bright_blue";
                break;

            case AbilityType.Enfeeble:
                result.InflictStatus = StatusEffect.Weakened;
                result.StatusDuration = 4;
                result.StatusChance = 50;
                result.SkipNormalAttack = true;
                result.Message = (isPlayerTarget ? Loc.Get("mability.enfeeble.you", monster.Name) : Loc.Get("mability.enfeeble.ally", monster.Name, target.Name));
                result.MessageColor = "yellow";
                break;

            case AbilityType.Devour:
                // Only works on targets below 20% HP
                if (target.HP < target.MaxHP / 5)
                {
                    result.DirectDamage = (int)target.HP; // Instant kill
                    result.Message = (isPlayerTarget ? Loc.Get("mability.devour_kill.you", monster.Name) : Loc.Get("mability.devour_kill.ally", monster.Name, target.Name));
                    result.MessageColor = "bright_red";
                }
                else
                {
                    result.DamageMultiplier = 1.3f;
                    result.Message = (isPlayerTarget ? Loc.Get("mability.devour_try.you", monster.Name) : Loc.Get("mability.devour_try.ally", monster.Name, target.Name));
                    result.MessageColor = "red";
                }
                break;

            case AbilityType.Berserk:
                // Triggers when monster is low HP
                if (monster.HP < monster.MaxHP / 3)
                {
                    result.DamageMultiplier = 2.0f;
                    result.ExtraAttacks = 1;
                    result.Message = Loc.Get("mability.berserk", monster.Name);
                    result.MessageColor = "bright_red";
                }
                break;

            case AbilityType.SummonMinions:
                result.SummonMonsters = true;
                result.SummonCount = _rnd.Next(1, 3);
                result.SkipNormalAttack = true;
                result.Message = Loc.Get("mability.summon_minions", monster.Name);
                result.MessageColor = "yellow";
                break;

            case AbilityType.Explosion:
                // Death explosion - handled when monster dies
                result.OnDeathDamage = (int)(monster.MaxHP / 2);
                result.Message = Loc.Get("mability.explosion", monster.Name);
                result.MessageColor = "bright_red";
                break;

            case AbilityType.SoulReap:
                // Small chance of instant kill
                if (_rnd.Next(100) < 5) // 5% chance
                {
                    result.DirectDamage = (int)target.HP;
                    result.Message = (isPlayerTarget ? Loc.Get("mability.soul_reap_kill.you", monster.Name) : Loc.Get("mability.soul_reap_kill.ally", monster.Name, target.Name));
                    result.MessageColor = "bright_red";
                }
                else
                {
                    result.DamageMultiplier = 1.5f;
                    result.Message = (isPlayerTarget ? Loc.Get("mability.soul_reap_reach.you", monster.Name) : Loc.Get("mability.soul_reap_reach.ally", monster.Name, target.Name));
                    result.MessageColor = "magenta";
                }
                break;

            case AbilityType.Backstab:
                // Extra damage only on first round (surprise attack)
                if (!monster.HasUsedBackstab && monster.CombatRound <= 1)
                {
                    monster.HasUsedBackstab = true;
                    result.DamageMultiplier = 2.5f;
                    result.Message = Loc.Get("mability.backstab_shadows", monster.Name);
                    result.MessageColor = "darkgray";
                }
                else
                {
                    // Normal attack after the element of surprise is gone
                    result.DamageMultiplier = 1.0f;
                    result.Message = Loc.Get("mability.backstab_plain", monster.Name);
                    result.MessageColor = "white";
                }
                break;

            case AbilityType.CallForHelp:
                result.SummonMonsters = true;
                result.SummonCount = 1;
                result.SkipNormalAttack = true;
                result.Message = Loc.Get("mability.call_for_help", monster.Name);
                result.MessageColor = "yellow";
                break;

            case AbilityType.Enrage:
                result.DamageMultiplier = 1.5f;
                // Prevent infinite stacking - permanent strength boost only once per combat
                if (!monster.HasEnraged)
                {
                    monster.Strength += 5;
                    monster.HasEnraged = true;
                    result.Message = Loc.Get("mability.enrage_becomes", monster.Name);
                }
                else
                {
                    result.Message = Loc.Get("mability.enrage_rages", monster.Name);
                }
                result.MessageColor = "red";
                break;

            case AbilityType.Heal:
                var bigHeal = Math.Max(20, monster.MaxHP / 4);
                monster.HP = Math.Min(monster.HP + bigHeal, monster.MaxHP);
                result.DamageMultiplier = 0; // Heal only — no damage to target
                result.SkipNormalAttack = true;
                result.IsSelfOnly = true;
                result.Message = Loc.Get("mability.heal", monster.Name, bigHeal);
                result.MessageColor = "bright_green";
                break;

            case AbilityType.Flee:
                result.MonsterFlees = true;
                result.SkipNormalAttack = true;
                result.Message = Loc.Get("mability.flee", monster.Name);
                result.MessageColor = "yellow";
                break;

            // --- Monster Family Abilities ---

            // Goblinoid
            case AbilityType.CriticalStrike:
                result.DamageMultiplier = 2.0f;
                result.Message = Loc.Get("mability.critical_strike", monster.Name);
                result.MessageColor = "bright_red";
                break;

            case AbilityType.Rally:
                if (!monster.HasEnraged)
                {
                    monster.Strength += monster.Level / 3;
                    monster.HasEnraged = true;
                    result.Message = Loc.Get("mability.rally", monster.Name);
                }
                else
                {
                    result.Message = Loc.Get("mability.rally_alt", monster.Name);
                }
                result.DamageMultiplier = 1.3f;
                result.MessageColor = "yellow";
                break;

            case AbilityType.CommandArmy:
                result.SummonMonsters = true;
                result.SummonCount = _rnd.Next(2, 4);
                result.SkipNormalAttack = true;
                result.Message = Loc.Get("mability.command_army", monster.Name);
                result.MessageColor = "bright_yellow";
                break;

            // Undead
            case AbilityType.Paralyze:
                result.InflictStatus = StatusEffect.Stunned;
                result.StatusDuration = 2;
                result.StatusChance = 35;
                result.SkipNormalAttack = true;
                result.Message = (isPlayerTarget ? Loc.Get("mability.paralyze.you", monster.Name) : Loc.Get("mability.paralyze.ally", monster.Name, target.Name));
                result.MessageColor = "cyan";
                break;

            case AbilityType.Incorporeal:
                result.AvoidAllDamage = _rnd.Next(100) < 30;
                result.DamageMultiplier = 0;
                if (result.AvoidAllDamage)
                {
                    // v0.61.2: persist evasion onto the monster (2 rounds at 50% miss chance)
                    // so the player's next 2 swings can actually pass through.
                    monster.EvasionRounds = 2;
                    monster.EvasionMissChance = 50;
                    result.Message = Loc.Get("mability.incorporeal", monster.Name);
                }
                else
                    result.Message = Loc.Get("mability.incorporeal_alt", monster.Name);
                result.MessageColor = "bright_cyan";
                break;

            case AbilityType.Spellcasting:
                result.DirectDamage = CalculateBreathDamage(monster, 1.3f);
                result.InflictStatus = _rnd.Next(3) switch { 0 => StatusEffect.Cursed, 1 => StatusEffect.Weakened, _ => StatusEffect.Silenced };
                result.StatusDuration = 3;
                result.StatusChance = 40;
                result.SkipNormalAttack = true;
                result.Message = Loc.Get("mability.spellcasting", monster.Name);
                result.MessageColor = "bright_magenta";
                break;

            case AbilityType.Phylactery:
                if (monster.HP < monster.MaxHP / 4)
                {
                    var phylHeal = monster.MaxHP / 3;
                    monster.HP = Math.Min(monster.HP + phylHeal, monster.MaxHP);
                    result.DamageMultiplier = 0;
                    result.SkipNormalAttack = true;
                    result.IsSelfOnly = true;
                    result.Message = Loc.Get("mability.phylactery", monster.Name, phylHeal);
                    result.MessageColor = "bright_magenta";
                }
                else
                {
                    result.DamageMultiplier = 1.2f;
                    result.Message = Loc.Get("mability.phylactery_alt", monster.Name);
                    result.MessageColor = "magenta";
                }
                break;

            // Orc
            case AbilityType.Rage:
                if (!monster.HasEnraged)
                {
                    monster.Strength += 5;
                    monster.HasEnraged = true;
                    result.Message = Loc.Get("mability.rage_flies", monster.Name);
                }
                else
                {
                    result.Message = Loc.Get("mability.rage_attacks", monster.Name);
                }
                result.DamageMultiplier = 1.5f;
                result.MessageColor = "bright_red";
                break;

            case AbilityType.Frenzy:
                result.DamageMultiplier = 1.3f;
                result.ExtraAttacks = _rnd.Next(1, 3);
                result.Message = Loc.Get("mability.frenzy", monster.Name);
                result.MessageColor = "bright_red";
                break;

            case AbilityType.Warcry:
                result.InflictStatus = StatusEffect.Feared;
                result.StatusDuration = 2;
                result.StatusChance = 45;
                result.SkipNormalAttack = true;
                result.Message = Loc.Get("mability.warcry", monster.Name);
                result.MessageColor = "bright_yellow";
                break;

            case AbilityType.Cleave:
                result.DamageMultiplier = 2.2f;
                result.Message = Loc.Get("mability.cleave", monster.Name);
                result.MessageColor = "bright_red";
                break;

            // Dragon
            case AbilityType.Flight:
                result.EvasionBonus = 25;
                result.DamageMultiplier = 0;
                // v0.61.2: persist evasion onto the monster (2 rounds at 25% miss chance).
                monster.EvasionRounds = 2;
                monster.EvasionMissChance = 25;
                result.Message = Loc.Get("mability.flight", monster.Name);
                result.MessageColor = "bright_cyan";
                break;

            case AbilityType.DragonFear:
                result.InflictStatus = StatusEffect.Feared;
                result.StatusDuration = 3;
                result.StatusChance = 50;
                result.SkipNormalAttack = true;
                result.Message = (isPlayerTarget ? Loc.Get("mability.dragon_fear.you", monster.Name) : Loc.Get("mability.dragon_fear.ally", monster.Name, target.Name));
                result.MessageColor = "bright_yellow";
                break;

            case AbilityType.AncientMagic:
                result.DirectDamage = CalculateBreathDamage(monster, 2.0f);
                result.SkipNormalAttack = true;
                result.Message = Loc.Get("mability.dragon_magic", monster.Name);
                result.MessageColor = "bright_magenta";
                break;

            // Demon
            case AbilityType.Invisibility:
                result.EvasionBonus = 35;
                result.DamageMultiplier = 0;
                // v0.61.2: persist evasion onto the monster (2 rounds at 35% miss chance).
                monster.EvasionRounds = 2;
                monster.EvasionMissChance = 35;
                result.Message = Loc.Get("mability.fades_from_sight", monster.Name);
                result.MessageColor = "gray";
                break;

            case AbilityType.Teleport:
                result.EvasionBonus = 40;
                result.DamageMultiplier = 0;
                result.SkipNormalAttack = true;
                // v0.61.2: persist evasion onto the monster (2 rounds at 40% miss chance).
                monster.EvasionRounds = 2;
                monster.EvasionMissChance = 40;
                result.Message = (isPlayerTarget ? Loc.Get("mability.teleport.you", monster.Name) : Loc.Get("mability.teleport.ally", monster.Name, target.Name));
                result.MessageColor = "bright_magenta";
                break;

            case AbilityType.Hellfire:
                result.DirectDamage = CalculateBreathDamage(monster, 1.8f);
                result.InflictStatus = StatusEffect.Burning;
                result.StatusDuration = 4;
                result.StatusChance = 60;
                result.SkipNormalAttack = true;
                result.Message = (isPlayerTarget ? Loc.Get("mability.hellfire.you", monster.Name) : Loc.Get("mability.hellfire.ally", monster.Name, target.Name));
                result.MessageColor = "bright_red";
                break;

            case AbilityType.Corruption:
                result.InflictStatus = StatusEffect.Cursed;
                result.StatusDuration = 5;
                result.StatusChance = 50;
                result.DamageMultiplier = 1.2f;
                result.Message = (isPlayerTarget ? Loc.Get("mability.corruption.you", monster.Name) : Loc.Get("mability.corruption.ally", monster.Name, target.Name));
                result.MessageColor = "magenta";
                break;

            case AbilityType.Dominate:
                result.InflictStatus = StatusEffect.Stunned;
                result.StatusDuration = 2;
                result.StatusChance = 30;
                result.SkipNormalAttack = true;
                result.Message = (isPlayerTarget ? Loc.Get("mability.dominate.you", monster.Name) : Loc.Get("mability.dominate.ally", monster.Name, target.Name));
                result.MessageColor = "bright_magenta";
                break;

            // Giant
            case AbilityType.Boulder:
                result.DirectDamage = CalculateBreathDamage(monster, 1.5f);
                result.InflictStatus = StatusEffect.Stunned;
                result.StatusDuration = 2; // +1 for ProcessStatusEffects off-by-one = 1 effective round of stun
                result.StatusChance = 30;
                result.SkipNormalAttack = true;
                result.Message = Loc.Get("mability.boulder", monster.Name);
                result.MessageColor = "gray";
                break;

            case AbilityType.Stoneskin:
                if (!monster.HasHardenedArmor)
                {
                    monster.ArmPow += monster.Level;
                    monster.HasHardenedArmor = true;
                    result.Message = Loc.Get("mability.stoneskin", monster.Name);
                }
                else
                {
                    result.Message = Loc.Get("mability.stoneskin_holds", monster.Name);
                }
                result.DamageMultiplier = 0;
                result.SkipNormalAttack = true;
                result.MessageColor = "gray";
                break;

            case AbilityType.Lightning:
                result.DirectDamage = CalculateBreathDamage(monster, 1.7f);
                result.InflictStatus = StatusEffect.Stunned;
                result.StatusDuration = 2; // +1 for ProcessStatusEffects off-by-one = 1 effective round of stun
                result.StatusChance = 40;
                result.SkipNormalAttack = true;
                result.Message = Loc.Get("mability.lightning", monster.Name);
                result.MessageColor = "bright_yellow";
                break;

            case AbilityType.Earthquake:
                result.DirectDamage = CalculateBreathDamage(monster, 1.4f);
                result.InflictStatus = StatusEffect.Stunned;
                result.StatusDuration = 2; // +1 for ProcessStatusEffects off-by-one = 1 effective round of stun
                result.StatusChance = 50;
                result.SkipNormalAttack = true;
                result.Message = Loc.Get("mability.earthquake", monster.Name);
                result.MessageColor = "bright_yellow";
                break;

            // Beast/Wolf
            case AbilityType.PackTactics:
                result.ExtraAttacks = 1;
                result.DamageMultiplier = 1.1f;
                result.Message = Loc.Get("mability.pack_coordinate", monster.Name);
                result.MessageColor = "white";
                break;

            case AbilityType.Bite:
                result.DamageMultiplier = 1.2f;
                result.InflictStatus = StatusEffect.Bleeding;
                result.StatusDuration = 3;
                result.StatusChance = 40;
                result.Message = Loc.Get("mability.bite_hard", monster.Name);
                result.MessageColor = "red";
                break;

            case AbilityType.Lycanthropy:
                result.DamageMultiplier = 1.3f;
                result.InflictStatus = StatusEffect.Cursed;
                result.StatusDuration = 4;
                result.StatusChance = 25;
                result.Message = Loc.Get("mability.ferocity", monster.Name);
                result.MessageColor = "bright_white";
                break;

            case AbilityType.Howl:
                result.InflictStatus = StatusEffect.Feared;
                result.StatusDuration = 2;
                result.StatusChance = 40;
                result.SkipNormalAttack = true;
                result.Message = Loc.Get("mability.howl", monster.Name);
                result.MessageColor = "bright_cyan";
                break;

            case AbilityType.Moonlight:
                var moonHeal = Math.Max(5, monster.MaxHP / 8);
                monster.HP = Math.Min(monster.HP + moonHeal, monster.MaxHP);
                result.DamageMultiplier = 1.3f;
                result.Message = Loc.Get("mability.moonlight_heal", monster.Name, moonHeal);
                result.MessageColor = "bright_white";
                break;

            // Fire Elemental
            case AbilityType.Burn:
                result.DamageMultiplier = 1.0f;
                result.InflictStatus = StatusEffect.Burning;
                result.StatusDuration = 3;
                result.StatusChance = 60;
                result.Message = (isPlayerTarget ? Loc.Get("mability.burn_scorch.you", monster.Name) : Loc.Get("mability.burn_scorch.ally", monster.Name, target.Name));
                result.MessageColor = "bright_red";
                break;

            case AbilityType.Immolate:
                result.DirectDamage = CalculateBreathDamage(monster, 1.6f);
                result.InflictStatus = StatusEffect.Burning;
                result.StatusDuration = 4;
                result.StatusChance = 70;
                result.SkipNormalAttack = true;
                result.Message = (isPlayerTarget ? Loc.Get("mability.immolate.you", monster.Name) : Loc.Get("mability.immolate.ally", monster.Name, target.Name));
                result.MessageColor = "bright_red";
                break;

            case AbilityType.Fireball:
                result.DirectDamage = CalculateBreathDamage(monster, 1.5f);
                result.SkipNormalAttack = true;
                result.Message = Loc.Get("mability.fireball", monster.Name);
                result.MessageColor = "bright_red";
                break;

            case AbilityType.Rebirth:
                if (monster.HP < monster.MaxHP / 5 && !monster.HasEnraged) // Use HasEnraged as "used rebirth" flag
                {
                    monster.HP = monster.MaxHP;
                    monster.HasEnraged = true;
                    result.DamageMultiplier = 0;
                    result.SkipNormalAttack = true;
                    result.IsSelfOnly = true;
                    result.Message = Loc.Get("mability.phoenix_rebirth", monster.Name);
                    result.MessageColor = "bright_yellow";
                }
                else
                {
                    result.DamageMultiplier = 1.5f;
                    result.Message = Loc.Get("mability.blazing_fury", monster.Name);
                    result.MessageColor = "bright_red";
                }
                break;

            case AbilityType.Inferno:
                result.DirectDamage = CalculateBreathDamage(monster, 2.0f);
                result.InflictStatus = StatusEffect.Burning;
                result.StatusDuration = 5;
                result.StatusChance = 80;
                result.SkipNormalAttack = true;
                result.Message = Loc.Get("mability.inferno", monster.Name);
                result.MessageColor = "bright_red";
                break;

            // Ooze/Slime
            case AbilityType.Corrosion:
                result.InflictStatus = StatusEffect.Weakened;
                result.StatusDuration = 4;
                result.StatusChance = 55;
                result.DamageMultiplier = 0.8f;
                result.Message = (isPlayerTarget ? Loc.Get("mability.corrosion.you", monster.Name) : Loc.Get("mability.corrosion.ally", monster.Name, target.Name));
                result.MessageColor = "green";
                break;

            case AbilityType.Split:
                result.SummonMonsters = true;
                result.SummonCount = 1;
                result.SkipNormalAttack = true;
                result.Message = Loc.Get("mability.splits", monster.Name);
                result.MessageColor = "bright_green";
                break;

            case AbilityType.Engulf:
                result.DamageMultiplier = 1.5f;
                result.InflictStatus = StatusEffect.Stunned;
                result.StatusDuration = 2; // +1 for ProcessStatusEffects off-by-one = 1 effective round of stun
                result.StatusChance = 45;
                result.Message = (isPlayerTarget ? Loc.Get("mability.engulf.you", monster.Name) : Loc.Get("mability.engulf.ally", monster.Name, target.Name));
                result.MessageColor = "bright_green";
                break;

            case AbilityType.Absorb:
                result.DamageMultiplier = 1.0f;
                result.LifeStealPercent = 75;
                result.Message = (isPlayerTarget ? Loc.Get("mability.absorb.you", monster.Name) : Loc.Get("mability.absorb.ally", monster.Name, target.Name));
                result.MessageColor = "bright_green";
                break;

            case AbilityType.ShapeShift:
                monster.Strength += _rnd.Next(-3, 8);
                monster.ArmPow += _rnd.Next(-3, 8);
                result.DamageMultiplier = 1.2f;
                result.Message = Loc.Get("mability.shifts_form", monster.Name);
                result.MessageColor = "bright_magenta";
                break;

            case AbilityType.Madness:
                result.InflictStatus = StatusEffect.Stunned;
                result.StatusDuration = 2;
                result.StatusChance = 35;
                result.DirectDamage = CalculateBreathDamage(monster, 0.8f);
                result.SkipNormalAttack = true;
                result.Message = (isPlayerTarget ? Loc.Get("mability.madness.you", monster.Name) : Loc.Get("mability.madness.ally", monster.Name, target.Name));
                result.MessageColor = "bright_magenta";
                break;

            // Spider/Insect
            case AbilityType.WebTrap:
                result.InflictStatus = StatusEffect.Stunned;
                result.StatusDuration = 2;
                // v0.61.5 balance pass: production telemetry showed Floor 8 Giant Spider
                // at 60% death rate (3 of 5 encounters) vs Floor 5 Giant Spider 0/5. The
                // difference was WebTrap's 45% stun chance giving the spider two free
                // hits against underleveled players. Reduced 45 -> 30 to lower the
                // free-hit frequency without removing the threat entirely.
                result.StatusChance = 30;
                result.SkipNormalAttack = true;
                result.Message = (isPlayerTarget ? Loc.Get("mability.web_trap.you", monster.Name) : Loc.Get("mability.web_trap.ally", monster.Name, target.Name));
                result.MessageColor = "white";
                break;

            case AbilityType.PhaseShift:
                result.AvoidAllDamage = _rnd.Next(100) < 30;
                result.DamageMultiplier = 0;
                if (result.AvoidAllDamage)
                {
                    // v0.61.2: persist evasion onto the monster (2 rounds at 50% miss chance).
                    monster.EvasionRounds = 2;
                    monster.EvasionMissChance = 50;
                }
                result.Message = result.AvoidAllDamage
                    ? Loc.Get("mability.phase_shift_avoid", monster.Name)
                    : Loc.Get("mability.phase_shift_flicker", monster.Name);
                result.MessageColor = "bright_cyan";
                break;

            case AbilityType.Poison:
                result.DamageMultiplier = 0.7f;
                result.InflictStatus = StatusEffect.Poisoned;
                result.StatusDuration = 5;
                result.StatusChance = 65;
                result.Message = (isPlayerTarget ? Loc.Get("mability.poison_inject.you", monster.Name) : Loc.Get("mability.poison_inject.ally", monster.Name, target.Name));
                result.MessageColor = "green";
                break;

            case AbilityType.SummonSpiders:
                result.SummonMonsters = true;
                result.SummonCount = _rnd.Next(2, 4);
                result.SkipNormalAttack = true;
                result.Message = Loc.Get("mability.summon_spiders", monster.Name);
                result.MessageColor = "white";
                break;

            case AbilityType.DeadlyVenom:
                result.DamageMultiplier = 1.3f;
                result.InflictStatus = StatusEffect.Poisoned;
                result.StatusDuration = 6;
                result.StatusChance = 80;
                result.Message = Loc.Get("mability.deadly_venom", monster.Name);
                result.MessageColor = "bright_green";
                break;

            case AbilityType.Swarm:
                result.ExtraAttacks = _rnd.Next(2, 5);
                result.DamageMultiplier = 0.6f;
                result.Message = Loc.Get("mability.spiderlings", monster.Name);
                result.MessageColor = "white";
                break;

            case AbilityType.Cocoon:
                var cocoonHeal = Math.Max(10, monster.MaxHP / 5);
                monster.HP = Math.Min(monster.HP + cocoonHeal, monster.MaxHP);
                if (!monster.HasHardenedArmor)
                {
                    monster.ArmPow += monster.Level / 3;
                    monster.HasHardenedArmor = true;
                }
                result.DamageMultiplier = 0;
                result.SkipNormalAttack = true;
                result.IsSelfOnly = true;
                result.Message = Loc.Get("mability.cocoon", monster.Name, cocoonHeal);
                result.MessageColor = "white";
                break;

            // Construct/Golem
            case AbilityType.ImmuneMagic:
                // Passive resistance — represented as armor boost
                if (!monster.HasHardenedArmor)
                {
                    monster.ArmPow += monster.Level / 2;
                    monster.HasHardenedArmor = true;
                    result.Message = Loc.Get("mability.magic_resist", monster.Name);
                }
                else
                {
                    result.Message = Loc.Get("mability.magic_resist_alt", monster.Name);
                }
                result.DamageMultiplier = 0;
                result.SkipNormalAttack = true;
                result.MessageColor = "bright_cyan";
                break;

            case AbilityType.PoisonGas:
                result.DirectDamage = CalculateBreathDamage(monster, 0.9f);
                result.InflictStatus = StatusEffect.Poisoned;
                result.StatusDuration = 5;
                result.StatusChance = 65;
                result.SkipNormalAttack = true;
                result.Message = Loc.Get("mability.toxic_gas", monster.Name);
                result.MessageColor = "green";
                break;

            case AbilityType.Indestructible:
                if (!monster.HasHardenedArmor)
                {
                    monster.ArmPow += monster.Level;
                    monster.HasHardenedArmor = true;
                    result.Message = Loc.Get("mability.indestructible", monster.Name);
                }
                else
                {
                    result.Message = Loc.Get("mability.armor_holds", monster.Name);
                }
                result.DamageMultiplier = 0;
                result.SkipNormalAttack = true;
                result.MessageColor = "bright_white";
                break;

            case AbilityType.SelfRepair:
                var repairAmount = Math.Max(15, monster.MaxHP / 5);
                monster.HP = Math.Min(monster.HP + repairAmount, monster.MaxHP);
                result.DamageMultiplier = 0;
                result.SkipNormalAttack = true;
                result.IsSelfOnly = true;
                result.Message = Loc.Get("mability.self_repair", monster.Name, repairAmount);
                result.MessageColor = "bright_cyan";
                break;

            case AbilityType.Overload:
                result.DirectDamage = CalculateBreathDamage(monster, 2.5f);
                monster.HP = Math.Max(1, monster.HP - monster.MaxHP / 5); // Self-damage
                result.SkipNormalAttack = true;
                result.Message = Loc.Get("mability.overload", monster.Name);
                result.MessageColor = "bright_yellow";
                break;

            // Fey
            case AbilityType.Sleep:
                result.InflictStatus = StatusEffect.Stunned;
                result.StatusDuration = 2;
                result.StatusChance = 40;
                result.SkipNormalAttack = true;
                result.Message = (isPlayerTarget ? Loc.Get("mability.sleep_dust.you", monster.Name) : Loc.Get("mability.sleep_dust.ally", monster.Name, target.Name));
                result.MessageColor = "bright_cyan";
                break;

            case AbilityType.TreeMeld:
                result.AvoidAllDamage = _rnd.Next(100) < 35;
                result.DamageMultiplier = 0;
                if (result.AvoidAllDamage)
                {
                    // v0.61.2: persist evasion onto the monster (2 rounds at 55% miss chance —
                    // higher than Phase/Incorporeal because "untouchable" reads stronger).
                    monster.EvasionRounds = 2;
                    monster.EvasionMissChance = 55;
                }
                result.Message = result.AvoidAllDamage
                    ? Loc.Get("mability.tree_meld_avoid", monster.Name)
                    : Loc.Get("mability.tree_meld_partial", monster.Name);
                result.MessageColor = "green";
                break;

            case AbilityType.Charm:
                result.InflictStatus = StatusEffect.Stunned;
                result.StatusDuration = 2;
                result.StatusChance = 35;
                result.SkipNormalAttack = true;
                result.Message = (isPlayerTarget ? Loc.Get("mability.charm.you", monster.Name) : Loc.Get("mability.charm.ally", monster.Name, target.Name));
                result.MessageColor = "bright_magenta";
                break;

            case AbilityType.AnimateTrees:
                result.SummonMonsters = true;
                result.SummonCount = _rnd.Next(1, 3);
                result.SkipNormalAttack = true;
                result.Message = Loc.Get("mability.animate_trees", monster.Name);
                result.MessageColor = "bright_green";
                break;

            case AbilityType.RootEntangle:
                result.DirectDamage = CalculateBreathDamage(monster, 0.8f);
                result.InflictStatus = StatusEffect.Stunned;
                result.StatusDuration = 2;
                result.StatusChance = 50;
                result.SkipNormalAttack = true;
                result.Message = (isPlayerTarget ? Loc.Get("mability.root_entangle.you", monster.Name) : Loc.Get("mability.root_entangle.ally", monster.Name, target.Name));
                result.MessageColor = "green";
                break;

            case AbilityType.TimeStop:
                result.ExtraAttacks = _rnd.Next(2, 4);
                result.DamageMultiplier = 1.0f;
                result.Message = Loc.Get("mability.stops_time", monster.Name);
                result.MessageColor = "bright_magenta";
                break;

            case AbilityType.WildShape:
                monster.Strength += monster.Level / 4;
                var wsHeal = monster.MaxHP / 6;
                monster.HP = Math.Min(monster.HP + wsHeal, monster.MaxHP);
                result.DamageMultiplier = 1.4f;
                result.Message = Loc.Get("mability.wild_shape", monster.Name);
                result.MessageColor = "bright_green";
                break;

            // Sea Creature
            case AbilityType.TentacleGrab:
                result.ExtraAttacks = _rnd.Next(1, 3);
                result.DamageMultiplier = 0.9f;
                result.InflictStatus = StatusEffect.Stunned;
                result.StatusDuration = 2; // +1 for ProcessStatusEffects off-by-one = 1 effective round of stun
                result.StatusChance = 25;
                result.Message = Loc.Get("mability.tentacles", monster.Name);
                result.MessageColor = "bright_blue";
                break;

            case AbilityType.InkCloud:
                result.InflictStatus = StatusEffect.Blinded;
                result.StatusDuration = 3;
                result.StatusChance = 55;
                result.EvasionBonus = 30;
                result.SkipNormalAttack = true;
                // v0.61.2: persist evasion onto the monster (3 rounds at 30% miss chance,
                // matching the 3-round Blinded duration the ability applies).
                monster.EvasionRounds = 3;
                monster.EvasionMissChance = 30;
                result.Message = Loc.Get("mability.ink_cloud", monster.Name);
                result.MessageColor = "gray";
                break;

            case AbilityType.Whirlpool:
                result.DirectDamage = CalculateBreathDamage(monster, 1.5f);
                result.InflictStatus = StatusEffect.Stunned;
                result.StatusDuration = 2; // +1 for ProcessStatusEffects off-by-one = 1 effective round of stun
                result.StatusChance = 40;
                result.SkipNormalAttack = true;
                result.Message = Loc.Get("mability.whirlpool", monster.Name);
                result.MessageColor = "bright_blue";
                break;

            case AbilityType.TidalWave:
                result.DirectDamage = CalculateBreathDamage(monster, 2.0f);
                result.SkipNormalAttack = true;
                result.Message = Loc.Get("mability.tidal_wave", monster.Name);
                result.MessageColor = "bright_blue";
                break;

            // Celestial
            case AbilityType.HolySmite:
                result.DirectDamage = CalculateBreathDamage(monster, 1.6f);
                result.SkipNormalAttack = true;
                result.Message = Loc.Get("mability.holy_smite", monster.Name);
                result.MessageColor = "bright_yellow";
                break;

            case AbilityType.Purify:
                // Weaken the player's buffs by applying debuffs
                result.InflictStatus = StatusEffect.Weakened;
                result.StatusDuration = 3;
                result.StatusChance = 60;
                result.SkipNormalAttack = true;
                result.Message = Loc.Get("mability.purify", monster.Name);
                result.MessageColor = "bright_white";
                break;

            case AbilityType.DivineJudgment:
                result.DirectDamage = CalculateBreathDamage(monster, 2.2f);
                result.SkipNormalAttack = true;
                result.Message = (isPlayerTarget ? Loc.Get("mability.divine_judgment.you", monster.Name) : Loc.Get("mability.divine_judgment.ally", monster.Name, target.Name));
                result.MessageColor = "bright_yellow";
                break;

            case AbilityType.Sanctuary:
                var sancHeal = Math.Max(15, monster.MaxHP / 4);
                monster.HP = Math.Min(monster.HP + sancHeal, monster.MaxHP);
                if (!monster.HasHardenedArmor)
                {
                    monster.ArmPow += monster.Level / 3;
                    monster.HasHardenedArmor = true;
                }
                result.DamageMultiplier = 0;
                result.SkipNormalAttack = true;
                result.IsSelfOnly = true;
                result.Message = Loc.Get("mability.sanctuary", monster.Name, sancHeal);
                result.MessageColor = "bright_white";
                break;

            case AbilityType.Resurrection:
                // In practice this is like SummonMinions for multi-monster
                result.SummonMonsters = true;
                result.SummonCount = 1;
                result.SkipNormalAttack = true;
                result.Message = Loc.Get("mability.resurrect", monster.Name);
                result.MessageColor = "bright_white";
                break;

            // Shadow
            case AbilityType.StrengthDrain:
                result.InflictStatus = StatusEffect.Weakened;
                result.StatusDuration = 4;
                result.StatusChance = 55;
                result.DamageMultiplier = 0.8f;
                result.Message = (isPlayerTarget ? Loc.Get("mability.strength_drain.you", monster.Name) : Loc.Get("mability.strength_drain.ally", monster.Name, target.Name));
                result.MessageColor = "gray";
                break;

            case AbilityType.Terror:
                result.DirectDamage = CalculateBreathDamage(monster, 1.0f);
                result.InflictStatus = StatusEffect.Feared;
                result.StatusDuration = 3;
                result.StatusChance = 45;
                result.SkipNormalAttack = true;
                result.Message = (isPlayerTarget ? Loc.Get("mability.terror.you", monster.Name) : Loc.Get("mability.terror.ally", monster.Name, target.Name));
                result.MessageColor = "magenta";
                break;

            case AbilityType.Possess:
                // Self-damage effect
                result.DirectDamage = (int)Math.Max(1, target.Strength / 2);
                result.SkipNormalAttack = true;
                result.Message = isPlayerTarget
                    ? Loc.Get("mability.possess.you", monster.Name)
                    : Loc.Get("mability.possess.ally", monster.Name, target.Name);
                result.MessageColor = "bright_magenta";
                break;

            case AbilityType.Nightmare:
                result.DirectDamage = CalculateBreathDamage(monster, 1.3f);
                result.InflictStatus = StatusEffect.Feared;
                result.StatusDuration = 2;
                result.StatusChance = 50;
                result.SkipNormalAttack = true;
                result.Message = (isPlayerTarget ? Loc.Get("mability.nightmare.you", monster.Name) : Loc.Get("mability.nightmare.ally", monster.Name, target.Name));
                result.MessageColor = "magenta";
                break;

            case AbilityType.DevourSoul:
                if (_rnd.Next(100) < 5)
                {
                    result.DirectDamage = (int)target.HP;
                    result.Message = (isPlayerTarget ? Loc.Get("mability.devour_soul_kill.you", monster.Name) : Loc.Get("mability.devour_soul_kill.ally", monster.Name, target.Name));
                    result.MessageColor = "bright_red";
                }
                else
                {
                    result.DamageMultiplier = 1.5f;
                    result.LifeStealPercent = 40;
                    result.Message = (isPlayerTarget ? Loc.Get("mability.devour_soul_tear.you", monster.Name) : Loc.Get("mability.devour_soul_tear.ally", monster.Name, target.Name));
                    result.MessageColor = "magenta";
                }
                break;

            case AbilityType.RealityBreak:
                result.DirectDamage = CalculateBreathDamage(monster, 1.8f);
                result.InflictStatus = _rnd.Next(4) switch
                {
                    0 => StatusEffect.Stunned,
                    1 => StatusEffect.Feared,
                    2 => StatusEffect.Cursed,
                    _ => StatusEffect.Weakened
                };
                result.StatusDuration = 3;
                result.StatusChance = 50;
                result.SkipNormalAttack = true;
                result.Message = Loc.Get("mability.reality_break", monster.Name);
                result.MessageColor = "bright_magenta";
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

    // Self-targeting (no "attacks!" message needed)
    public bool IsSelfOnly { get; set; } = false;

    // Summon/flee
    public bool SummonMonsters { get; set; } = false;
    public int SummonCount { get; set; } = 0;
    public bool MonsterFlees { get; set; } = false;

    // Death effects
    public int OnDeathDamage { get; set; } = 0;
}
