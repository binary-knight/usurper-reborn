using System;
using System.Collections.Generic;

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
                result.DamageMultiplier = 0; // Heal only — no damage to target
                result.SkipNormalAttack = false; // Can still attack normally
                result.IsSelfOnly = true; // No "attacks!" message needed
                result.Message = $"{monster.Name} regenerates {healAmount} HP!";
                result.MessageColor = "bright_green";
                break;

            case AbilityType.Thorns:
                result.DamageMultiplier = 0; // Passive reflect — no direct damage
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
                result.DamageMultiplier = 0; // Buff only — no damage
                result.SkipNormalAttack = true;
                break;

            case AbilityType.Vanish:
                result.DamageMultiplier = 0; // Evasion buff — no damage
                result.EvasionBonus = 30;
                result.Message = $"{monster.Name} fades into the shadows!";
                result.MessageColor = "darkgray";
                break;

            case AbilityType.Phase:
                result.DamageMultiplier = 0; // Damage avoidance — no damage
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
                result.IsSelfOnly = true;
                result.Message = $"{monster.Name} heals for {bigHeal} HP!";
                result.MessageColor = "bright_green";
                break;

            case AbilityType.Flee:
                result.MonsterFlees = true;
                result.SkipNormalAttack = true;
                result.Message = $"{monster.Name} attempts to flee!";
                result.MessageColor = "yellow";
                break;

            // --- Monster Family Abilities ---

            // Goblinoid
            case AbilityType.CriticalStrike:
                result.DamageMultiplier = 2.0f;
                result.Message = $"{monster.Name} lands a critical strike!";
                result.MessageColor = "bright_red";
                break;

            case AbilityType.Rally:
                if (!monster.HasEnraged)
                {
                    monster.Strength += monster.Level / 3;
                    monster.HasEnraged = true;
                    result.Message = $"{monster.Name} rallies, surging with renewed vigor!";
                }
                else
                {
                    result.Message = $"{monster.Name} shouts a battle cry!";
                }
                result.DamageMultiplier = 1.3f;
                result.MessageColor = "yellow";
                break;

            case AbilityType.CommandArmy:
                result.SummonMonsters = true;
                result.SummonCount = _rnd.Next(2, 4);
                result.SkipNormalAttack = true;
                result.Message = $"{monster.Name} commands its army to attack!";
                result.MessageColor = "bright_yellow";
                break;

            // Undead
            case AbilityType.Paralyze:
                result.InflictStatus = StatusEffect.Stunned;
                result.StatusDuration = 2;
                result.StatusChance = 35;
                result.SkipNormalAttack = true;
                result.Message = $"{monster.Name}'s touch paralyzes you!";
                result.MessageColor = "cyan";
                break;

            case AbilityType.Incorporeal:
                result.AvoidAllDamage = _rnd.Next(100) < 30;
                result.DamageMultiplier = 0;
                if (result.AvoidAllDamage)
                    result.Message = $"{monster.Name} becomes incorporeal — attacks pass through!";
                else
                    result.Message = $"{monster.Name} flickers between planes!";
                result.MessageColor = "bright_cyan";
                break;

            case AbilityType.Spellcasting:
                result.DirectDamage = CalculateBreathDamage(monster, 1.3f);
                result.InflictStatus = _rnd.Next(3) switch { 0 => StatusEffect.Cursed, 1 => StatusEffect.Weakened, _ => StatusEffect.Silenced };
                result.StatusDuration = 3;
                result.StatusChance = 40;
                result.SkipNormalAttack = true;
                result.Message = $"{monster.Name} casts a dark spell!";
                result.MessageColor = "bright_magenta";
                break;

            case AbilityType.Phylactery:
                if (monster.HP < monster.MaxHP / 4)
                {
                    var phylHeal = monster.MaxHP / 3;
                    monster.HP = Math.Min(monster.HP + phylHeal, monster.MaxHP);
                    result.SkipNormalAttack = true;
                    result.IsSelfOnly = true;
                    result.Message = $"{monster.Name}'s phylactery pulses — it regenerates {phylHeal} HP!";
                    result.MessageColor = "bright_magenta";
                }
                else
                {
                    result.DamageMultiplier = 1.2f;
                    result.Message = $"{monster.Name} channels dark energy!";
                    result.MessageColor = "magenta";
                }
                break;

            // Orc
            case AbilityType.Rage:
                if (!monster.HasEnraged)
                {
                    monster.Strength += 5;
                    monster.HasEnraged = true;
                    result.Message = $"{monster.Name} flies into a rage!";
                }
                else
                {
                    result.Message = $"{monster.Name} attacks with furious rage!";
                }
                result.DamageMultiplier = 1.5f;
                result.MessageColor = "bright_red";
                break;

            case AbilityType.Frenzy:
                result.DamageMultiplier = 1.3f;
                result.ExtraAttacks = _rnd.Next(1, 3);
                result.Message = $"{monster.Name} enters a wild frenzy!";
                result.MessageColor = "bright_red";
                break;

            case AbilityType.Warcry:
                result.InflictStatus = StatusEffect.Feared;
                result.StatusDuration = 2;
                result.StatusChance = 45;
                result.SkipNormalAttack = true;
                result.Message = $"{monster.Name} unleashes a terrifying warcry!";
                result.MessageColor = "bright_yellow";
                break;

            case AbilityType.Cleave:
                result.DamageMultiplier = 2.2f;
                result.Message = $"{monster.Name} cleaves with devastating force!";
                result.MessageColor = "bright_red";
                break;

            // Dragon
            case AbilityType.Flight:
                result.EvasionBonus = 25;
                result.DamageMultiplier = 0;
                result.Message = $"{monster.Name} takes flight, becoming harder to hit!";
                result.MessageColor = "bright_cyan";
                break;

            case AbilityType.DragonFear:
                result.InflictStatus = StatusEffect.Feared;
                result.StatusDuration = 3;
                result.StatusChance = 50;
                result.SkipNormalAttack = true;
                result.Message = $"{monster.Name}'s draconic presence fills you with dread!";
                result.MessageColor = "bright_yellow";
                break;

            case AbilityType.AncientMagic:
                result.DirectDamage = CalculateBreathDamage(monster, 2.0f);
                result.SkipNormalAttack = true;
                result.Message = $"{monster.Name} unleashes ancient draconic magic!";
                result.MessageColor = "bright_magenta";
                break;

            // Demon
            case AbilityType.Invisibility:
                result.EvasionBonus = 35;
                result.DamageMultiplier = 0;
                result.Message = $"{monster.Name} fades from sight!";
                result.MessageColor = "gray";
                break;

            case AbilityType.Teleport:
                result.EvasionBonus = 40;
                result.DamageMultiplier = 0;
                result.SkipNormalAttack = true;
                result.Message = $"{monster.Name} teleports behind you!";
                result.MessageColor = "bright_magenta";
                break;

            case AbilityType.Hellfire:
                result.DirectDamage = CalculateBreathDamage(monster, 1.8f);
                result.InflictStatus = StatusEffect.Burning;
                result.StatusDuration = 4;
                result.StatusChance = 60;
                result.SkipNormalAttack = true;
                result.Message = $"{monster.Name} engulfs you in hellfire!";
                result.MessageColor = "bright_red";
                break;

            case AbilityType.Corruption:
                result.InflictStatus = StatusEffect.Cursed;
                result.StatusDuration = 5;
                result.StatusChance = 50;
                result.DamageMultiplier = 1.2f;
                result.Message = $"{monster.Name} corrupts your very essence!";
                result.MessageColor = "magenta";
                break;

            case AbilityType.Dominate:
                result.InflictStatus = StatusEffect.Stunned;
                result.StatusDuration = 2;
                result.StatusChance = 30;
                result.SkipNormalAttack = true;
                result.Message = $"{monster.Name} tries to dominate your will!";
                result.MessageColor = "bright_magenta";
                break;

            // Giant
            case AbilityType.Boulder:
                result.DirectDamage = CalculateBreathDamage(monster, 1.5f);
                result.InflictStatus = StatusEffect.Stunned;
                result.StatusDuration = 1;
                result.StatusChance = 30;
                result.SkipNormalAttack = true;
                result.Message = $"{monster.Name} hurls a massive boulder!";
                result.MessageColor = "gray";
                break;

            case AbilityType.Stoneskin:
                if (!monster.HasHardenedArmor)
                {
                    monster.ArmPow += monster.Level;
                    monster.HasHardenedArmor = true;
                    result.Message = $"{monster.Name}'s skin turns to stone!";
                }
                else
                {
                    result.Message = $"{monster.Name}'s stone armor holds firm!";
                }
                result.DamageMultiplier = 0;
                result.SkipNormalAttack = true;
                result.MessageColor = "gray";
                break;

            case AbilityType.Lightning:
                result.DirectDamage = CalculateBreathDamage(monster, 1.7f);
                result.InflictStatus = StatusEffect.Stunned;
                result.StatusDuration = 1;
                result.StatusChance = 40;
                result.SkipNormalAttack = true;
                result.Message = $"{monster.Name} calls down lightning!";
                result.MessageColor = "bright_yellow";
                break;

            case AbilityType.Earthquake:
                result.DirectDamage = CalculateBreathDamage(monster, 1.4f);
                result.InflictStatus = StatusEffect.Stunned;
                result.StatusDuration = 1;
                result.StatusChance = 50;
                result.SkipNormalAttack = true;
                result.Message = $"{monster.Name} causes the earth to shake violently!";
                result.MessageColor = "bright_yellow";
                break;

            // Beast/Wolf
            case AbilityType.PackTactics:
                result.ExtraAttacks = 1;
                result.DamageMultiplier = 1.1f;
                result.Message = $"{monster.Name} coordinates with the pack!";
                result.MessageColor = "white";
                break;

            case AbilityType.Bite:
                result.DamageMultiplier = 1.2f;
                result.InflictStatus = StatusEffect.Bleeding;
                result.StatusDuration = 3;
                result.StatusChance = 40;
                result.Message = $"{monster.Name} bites down hard!";
                result.MessageColor = "red";
                break;

            case AbilityType.Lycanthropy:
                result.DamageMultiplier = 1.3f;
                result.InflictStatus = StatusEffect.Cursed;
                result.StatusDuration = 4;
                result.StatusChance = 25;
                result.Message = $"{monster.Name} attacks with supernatural ferocity!";
                result.MessageColor = "bright_white";
                break;

            case AbilityType.Howl:
                result.InflictStatus = StatusEffect.Feared;
                result.StatusDuration = 2;
                result.StatusChance = 40;
                result.SkipNormalAttack = true;
                result.Message = $"{monster.Name} lets loose a bone-chilling howl!";
                result.MessageColor = "bright_cyan";
                break;

            case AbilityType.Moonlight:
                var moonHeal = Math.Max(5, monster.MaxHP / 8);
                monster.HP = Math.Min(monster.HP + moonHeal, monster.MaxHP);
                result.DamageMultiplier = 1.3f;
                result.Message = $"{monster.Name} bathes in moonlight, healing {moonHeal} HP!";
                result.MessageColor = "bright_white";
                break;

            // Fire Elemental
            case AbilityType.Burn:
                result.DamageMultiplier = 1.0f;
                result.InflictStatus = StatusEffect.Burning;
                result.StatusDuration = 3;
                result.StatusChance = 60;
                result.Message = $"{monster.Name} scorches you with fire!";
                result.MessageColor = "bright_red";
                break;

            case AbilityType.Immolate:
                result.DirectDamage = CalculateBreathDamage(monster, 1.6f);
                result.InflictStatus = StatusEffect.Burning;
                result.StatusDuration = 4;
                result.StatusChance = 70;
                result.SkipNormalAttack = true;
                result.Message = $"{monster.Name} immolates you in flames!";
                result.MessageColor = "bright_red";
                break;

            case AbilityType.Fireball:
                result.DirectDamage = CalculateBreathDamage(monster, 1.5f);
                result.SkipNormalAttack = true;
                result.Message = $"{monster.Name} hurls a fireball!";
                result.MessageColor = "bright_red";
                break;

            case AbilityType.Rebirth:
                if (monster.HP < monster.MaxHP / 5 && !monster.HasEnraged) // Use HasEnraged as "used rebirth" flag
                {
                    monster.HP = monster.MaxHP;
                    monster.HasEnraged = true;
                    result.SkipNormalAttack = true;
                    result.IsSelfOnly = true;
                    result.Message = $"{monster.Name} erupts in flame and is reborn!";
                    result.MessageColor = "bright_yellow";
                }
                else
                {
                    result.DamageMultiplier = 1.5f;
                    result.Message = $"{monster.Name} attacks with blazing fury!";
                    result.MessageColor = "bright_red";
                }
                break;

            case AbilityType.Inferno:
                result.DirectDamage = CalculateBreathDamage(monster, 2.0f);
                result.InflictStatus = StatusEffect.Burning;
                result.StatusDuration = 5;
                result.StatusChance = 80;
                result.SkipNormalAttack = true;
                result.Message = $"{monster.Name} unleashes an inferno!";
                result.MessageColor = "bright_red";
                break;

            // Ooze/Slime
            case AbilityType.Corrosion:
                result.InflictStatus = StatusEffect.Weakened;
                result.StatusDuration = 4;
                result.StatusChance = 55;
                result.DamageMultiplier = 0.8f;
                result.Message = $"{monster.Name}'s acidic touch corrodes your equipment!";
                result.MessageColor = "green";
                break;

            case AbilityType.Split:
                result.SummonMonsters = true;
                result.SummonCount = 1;
                result.SkipNormalAttack = true;
                result.Message = $"{monster.Name} splits into two!";
                result.MessageColor = "bright_green";
                break;

            case AbilityType.Engulf:
                result.DamageMultiplier = 1.5f;
                result.InflictStatus = StatusEffect.Stunned;
                result.StatusDuration = 1;
                result.StatusChance = 45;
                result.Message = $"{monster.Name} engulfs you in its mass!";
                result.MessageColor = "bright_green";
                break;

            case AbilityType.Absorb:
                result.DamageMultiplier = 1.0f;
                result.LifeStealPercent = 75;
                result.Message = $"{monster.Name} absorbs your life force!";
                result.MessageColor = "bright_green";
                break;

            case AbilityType.ShapeShift:
                monster.Strength += _rnd.Next(-3, 8);
                monster.ArmPow += _rnd.Next(-3, 8);
                result.DamageMultiplier = 1.2f;
                result.Message = $"{monster.Name} shifts into a new horrifying form!";
                result.MessageColor = "bright_magenta";
                break;

            case AbilityType.Madness:
                result.InflictStatus = StatusEffect.Stunned;
                result.StatusDuration = 2;
                result.StatusChance = 35;
                result.DirectDamage = CalculateBreathDamage(monster, 0.8f);
                result.SkipNormalAttack = true;
                result.Message = $"{monster.Name}'s alien form drives you to the brink of madness!";
                result.MessageColor = "bright_magenta";
                break;

            // Spider/Insect
            case AbilityType.WebTrap:
                result.InflictStatus = StatusEffect.Stunned;
                result.StatusDuration = 2;
                result.StatusChance = 45;
                result.SkipNormalAttack = true;
                result.Message = $"{monster.Name} traps you in sticky webbing!";
                result.MessageColor = "white";
                break;

            case AbilityType.PhaseShift:
                result.AvoidAllDamage = _rnd.Next(100) < 30;
                result.DamageMultiplier = 0;
                result.Message = result.AvoidAllDamage
                    ? $"{monster.Name} phase shifts out of reality!"
                    : $"{monster.Name} flickers between dimensions!";
                result.MessageColor = "bright_cyan";
                break;

            case AbilityType.Poison:
                result.DamageMultiplier = 0.7f;
                result.InflictStatus = StatusEffect.Poisoned;
                result.StatusDuration = 5;
                result.StatusChance = 65;
                result.Message = $"{monster.Name} injects you with poison!";
                result.MessageColor = "green";
                break;

            case AbilityType.SummonSpiders:
                result.SummonMonsters = true;
                result.SummonCount = _rnd.Next(2, 4);
                result.SkipNormalAttack = true;
                result.Message = $"{monster.Name} summons a swarm of spiders!";
                result.MessageColor = "white";
                break;

            case AbilityType.DeadlyVenom:
                result.DamageMultiplier = 1.3f;
                result.InflictStatus = StatusEffect.Poisoned;
                result.StatusDuration = 6;
                result.StatusChance = 80;
                result.Message = $"{monster.Name} strikes with deadly venom!";
                result.MessageColor = "bright_green";
                break;

            case AbilityType.Swarm:
                result.ExtraAttacks = _rnd.Next(2, 5);
                result.DamageMultiplier = 0.6f;
                result.Message = $"{monster.Name} sends a swarm of spiderlings!";
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
                result.SkipNormalAttack = true;
                result.IsSelfOnly = true;
                result.Message = $"{monster.Name} wraps itself in a cocoon, healing {cocoonHeal} HP!";
                result.MessageColor = "white";
                break;

            // Construct/Golem
            case AbilityType.ImmuneMagic:
                // Passive resistance — represented as armor boost
                if (!monster.HasHardenedArmor)
                {
                    monster.ArmPow += monster.Level / 2;
                    monster.HasHardenedArmor = true;
                    result.Message = $"{monster.Name}'s magical resistance flares!";
                }
                else
                {
                    result.Message = $"{monster.Name} shrugs off magical energy!";
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
                result.Message = $"{monster.Name} releases a cloud of toxic gas!";
                result.MessageColor = "green";
                break;

            case AbilityType.Indestructible:
                if (!monster.HasHardenedArmor)
                {
                    monster.ArmPow += monster.Level;
                    monster.HasHardenedArmor = true;
                    result.Message = $"{monster.Name}'s body becomes nearly indestructible!";
                }
                else
                {
                    result.Message = $"{monster.Name}'s armor holds strong!";
                }
                result.DamageMultiplier = 0;
                result.SkipNormalAttack = true;
                result.MessageColor = "bright_white";
                break;

            case AbilityType.SelfRepair:
                var repairAmount = Math.Max(15, monster.MaxHP / 5);
                monster.HP = Math.Min(monster.HP + repairAmount, monster.MaxHP);
                result.SkipNormalAttack = true;
                result.IsSelfOnly = true;
                result.Message = $"{monster.Name} repairs itself for {repairAmount} HP!";
                result.MessageColor = "bright_cyan";
                break;

            case AbilityType.Overload:
                result.DirectDamage = CalculateBreathDamage(monster, 2.5f);
                monster.HP = Math.Max(1, monster.HP - monster.MaxHP / 5); // Self-damage
                result.SkipNormalAttack = true;
                result.Message = $"{monster.Name} overloads its core, unleashing devastating energy!";
                result.MessageColor = "bright_yellow";
                break;

            // Fey
            case AbilityType.Sleep:
                result.InflictStatus = StatusEffect.Stunned;
                result.StatusDuration = 2;
                result.StatusChance = 40;
                result.SkipNormalAttack = true;
                result.Message = $"{monster.Name} sprinkles sleep dust over you!";
                result.MessageColor = "bright_cyan";
                break;

            case AbilityType.TreeMeld:
                result.AvoidAllDamage = _rnd.Next(100) < 35;
                result.DamageMultiplier = 0;
                result.Message = result.AvoidAllDamage
                    ? $"{monster.Name} melds with a nearby tree, becoming untouchable!"
                    : $"{monster.Name} partially melds with nature!";
                result.MessageColor = "green";
                break;

            case AbilityType.Charm:
                result.InflictStatus = StatusEffect.Stunned;
                result.StatusDuration = 2;
                result.StatusChance = 35;
                result.SkipNormalAttack = true;
                result.Message = $"{monster.Name} tries to charm you with fey magic!";
                result.MessageColor = "bright_magenta";
                break;

            case AbilityType.AnimateTrees:
                result.SummonMonsters = true;
                result.SummonCount = _rnd.Next(1, 3);
                result.SkipNormalAttack = true;
                result.Message = $"{monster.Name} animates the surrounding trees!";
                result.MessageColor = "bright_green";
                break;

            case AbilityType.RootEntangle:
                result.DirectDamage = CalculateBreathDamage(monster, 0.8f);
                result.InflictStatus = StatusEffect.Stunned;
                result.StatusDuration = 2;
                result.StatusChance = 50;
                result.SkipNormalAttack = true;
                result.Message = $"{monster.Name} entangles you in grasping roots!";
                result.MessageColor = "green";
                break;

            case AbilityType.TimeStop:
                result.ExtraAttacks = _rnd.Next(2, 4);
                result.DamageMultiplier = 1.0f;
                result.Message = $"{monster.Name} stops time and strikes repeatedly!";
                result.MessageColor = "bright_magenta";
                break;

            case AbilityType.WildShape:
                monster.Strength += monster.Level / 4;
                var wsHeal = monster.MaxHP / 6;
                monster.HP = Math.Min(monster.HP + wsHeal, monster.MaxHP);
                result.DamageMultiplier = 1.4f;
                result.Message = $"{monster.Name} wild shapes into a monstrous form!";
                result.MessageColor = "bright_green";
                break;

            // Sea Creature
            case AbilityType.TentacleGrab:
                result.ExtraAttacks = _rnd.Next(1, 3);
                result.DamageMultiplier = 0.9f;
                result.InflictStatus = StatusEffect.Stunned;
                result.StatusDuration = 1;
                result.StatusChance = 25;
                result.Message = $"{monster.Name} lashes out with writhing tentacles!";
                result.MessageColor = "bright_blue";
                break;

            case AbilityType.InkCloud:
                result.InflictStatus = StatusEffect.Blinded;
                result.StatusDuration = 3;
                result.StatusChance = 55;
                result.EvasionBonus = 30;
                result.SkipNormalAttack = true;
                result.Message = $"{monster.Name} releases a cloud of ink!";
                result.MessageColor = "gray";
                break;

            case AbilityType.Whirlpool:
                result.DirectDamage = CalculateBreathDamage(monster, 1.5f);
                result.InflictStatus = StatusEffect.Stunned;
                result.StatusDuration = 1;
                result.StatusChance = 40;
                result.SkipNormalAttack = true;
                result.Message = $"{monster.Name} creates a devastating whirlpool!";
                result.MessageColor = "bright_blue";
                break;

            case AbilityType.TidalWave:
                result.DirectDamage = CalculateBreathDamage(monster, 2.0f);
                result.SkipNormalAttack = true;
                result.Message = $"{monster.Name} summons a massive tidal wave!";
                result.MessageColor = "bright_blue";
                break;

            // Celestial
            case AbilityType.HolySmite:
                result.DirectDamage = CalculateBreathDamage(monster, 1.6f);
                result.SkipNormalAttack = true;
                result.Message = $"{monster.Name} calls down holy smite!";
                result.MessageColor = "bright_yellow";
                break;

            case AbilityType.Purify:
                // Weaken the player's buffs by applying debuffs
                result.InflictStatus = StatusEffect.Weakened;
                result.StatusDuration = 3;
                result.StatusChance = 60;
                result.SkipNormalAttack = true;
                result.Message = $"{monster.Name} purifies the battlefield with divine light!";
                result.MessageColor = "bright_white";
                break;

            case AbilityType.DivineJudgment:
                result.DirectDamage = CalculateBreathDamage(monster, 2.2f);
                result.SkipNormalAttack = true;
                result.Message = $"{monster.Name} passes divine judgment upon you!";
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
                result.SkipNormalAttack = true;
                result.IsSelfOnly = true;
                result.Message = $"{monster.Name} creates a sanctuary, healing {sancHeal} HP!";
                result.MessageColor = "bright_white";
                break;

            case AbilityType.Resurrection:
                // In practice this is like SummonMinions for multi-monster
                result.SummonMonsters = true;
                result.SummonCount = 1;
                result.SkipNormalAttack = true;
                result.Message = $"{monster.Name} resurrects a fallen ally!";
                result.MessageColor = "bright_white";
                break;

            // Shadow
            case AbilityType.StrengthDrain:
                result.InflictStatus = StatusEffect.Weakened;
                result.StatusDuration = 4;
                result.StatusChance = 55;
                result.DamageMultiplier = 0.8f;
                result.Message = $"{monster.Name} drains your strength!";
                result.MessageColor = "gray";
                break;

            case AbilityType.Terror:
                result.DirectDamage = CalculateBreathDamage(monster, 1.0f);
                result.InflictStatus = StatusEffect.Feared;
                result.StatusDuration = 3;
                result.StatusChance = 45;
                result.SkipNormalAttack = true;
                result.Message = $"{monster.Name} fills your mind with visions of terror!";
                result.MessageColor = "magenta";
                break;

            case AbilityType.Possess:
                // Self-damage effect
                result.DirectDamage = (int)Math.Max(1, target.Strength / 2);
                result.SkipNormalAttack = true;
                result.Message = $"{monster.Name} possesses you — you strike yourself!";
                result.MessageColor = "bright_magenta";
                break;

            case AbilityType.Nightmare:
                result.DirectDamage = CalculateBreathDamage(monster, 1.3f);
                result.InflictStatus = StatusEffect.Feared;
                result.StatusDuration = 2;
                result.StatusChance = 50;
                result.SkipNormalAttack = true;
                result.Message = $"{monster.Name} subjects you to a waking nightmare!";
                result.MessageColor = "magenta";
                break;

            case AbilityType.DevourSoul:
                if (_rnd.Next(100) < 5)
                {
                    result.DirectDamage = (int)target.HP;
                    result.Message = $"{monster.Name} devours your soul!";
                    result.MessageColor = "bright_red";
                }
                else
                {
                    result.DamageMultiplier = 1.5f;
                    result.LifeStealPercent = 40;
                    result.Message = $"{monster.Name} tears at your soul!";
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
                result.Message = $"{monster.Name} tears a hole in reality!";
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
