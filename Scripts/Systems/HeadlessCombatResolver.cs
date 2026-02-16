using System;

namespace UsurperRemake.Systems;

/// <summary>
/// Resolves combat without terminal output — used by WorldSimulator for
/// NPC attacks on sleeping players (background thread, no UI).
/// </summary>
public static class HeadlessCombatResolver
{
    public enum HeadlessOutcome { AttackerWins, DefenderWins, Draw }

    public record HeadlessCombatResult(
        HeadlessOutcome Outcome,
        long DamageToAttacker,
        long DamageToDefender,
        int Rounds
    );

    /// <summary>
    /// Run a simple combat loop between attacker and defender.
    /// Both characters' HP is modified in-place (supports gauntlet chaining).
    /// </summary>
    public static HeadlessCombatResult Resolve(Character attacker, Character defender, Random rng, int maxRounds = 30)
    {
        long attackerStartHp = attacker.HP;
        long defenderStartHp = defender.HP;
        int rounds = 0;

        while (attacker.IsAlive && defender.IsAlive && rounds < maxRounds)
        {
            rounds++;

            // Attacker strikes
            long atkDmg = CalculateDamage(attacker, defender, rng);
            defender.HP -= atkDmg;

            if (!defender.IsAlive) break;

            // Defender strikes back
            long defDmg = CalculateDamage(defender, attacker, rng);
            attacker.HP -= defDmg;
        }

        var outcome = !defender.IsAlive ? HeadlessOutcome.AttackerWins
                    : !attacker.IsAlive ? HeadlessOutcome.DefenderWins
                    : HeadlessOutcome.Draw;

        return new HeadlessCombatResult(
            outcome,
            DamageToAttacker: attackerStartHp - attacker.HP,
            DamageToDefender: defenderStartHp - defender.HP,
            Rounds: rounds
        );
    }

    /// <summary>
    /// Calculate damage for one strike: STR + WeapPow/2 ± 20% variance - target DEF/3.
    /// Minimum 1 damage per hit.
    /// </summary>
    private static long CalculateDamage(Character striker, Character target, Random rng)
    {
        long baseDmg = striker.Strength + striker.WeapPow / 2;
        double variance = 0.8 + rng.NextDouble() * 0.4; // 0.8x to 1.2x
        long damage = (long)(baseDmg * variance);

        long defense = target.Defence / 3 + target.ArmPow / 4;
        damage -= defense;

        // Critical hit: 10% chance for 1.5x damage
        if (rng.NextDouble() < 0.10)
            damage = (long)(damage * 1.5);

        return Math.Max(1, damage);
    }

    /// <summary>
    /// Create a Character representing a hired guard for combat.
    /// Stats scale with the player's level.
    /// </summary>
    public static Character CreateGuardCharacter(string guardType, int guardHp, int playerLevel, Random rng)
    {
        // Base stat multipliers by guard type
        float strMult, defMult, agiMult;
        string guardName;

        switch (guardType)
        {
            case "rookie_npc":
                strMult = 0.6f; defMult = 0.5f; agiMult = 0.5f;
                guardName = "Rookie Guard";
                break;
            case "veteran_npc":
                strMult = 0.8f; defMult = 0.8f; agiMult = 0.6f;
                guardName = "Veteran Guard";
                break;
            case "elite_npc":
                strMult = 1.0f; defMult = 1.0f; agiMult = 0.8f;
                guardName = "Elite Guard";
                break;
            case "hound":
                strMult = 0.5f; defMult = 0.3f; agiMult = 1.2f;
                guardName = "Guard Hound";
                break;
            case "troll":
                strMult = 1.0f; defMult = 1.2f; agiMult = 0.3f;
                guardName = "Guard Troll";
                break;
            case "drake":
                strMult = 1.3f; defMult = 1.0f; agiMult = 0.5f;
                guardName = "Guard Drake";
                break;
            default:
                strMult = 0.6f; defMult = 0.5f; agiMult = 0.5f;
                guardName = "Guard";
                break;
        }

        // Scale stats based on player level
        long baseStr = (long)(playerLevel * 3 * strMult);
        long baseDef = (long)(playerLevel * 3 * defMult);
        long baseAgi = (long)(playerLevel * 2 * agiMult);
        long weapPow = (long)(playerLevel * 2 * strMult);
        long armPow = (long)(playerLevel * 2 * defMult);

        return new Character
        {
            Name2 = guardName,
            Level = Math.Max(1, playerLevel / 2),
            HP = guardHp,
            MaxHP = guardHp,
            Strength = baseStr,
            Defence = baseDef,
            Agility = baseAgi,
            WeapPow = weapPow,
            ArmPow = armPow,
            Dexterity = baseAgi,
            Constitution = (long)(playerLevel * 2),
            AI = CharacterAI.Computer
        };
    }

    /// <summary>
    /// Calculate guard HP based on guard type and player level.
    /// </summary>
    public static int GetGuardBaseHp(string guardType, int playerLevel)
    {
        int baseHp = guardType switch
        {
            "rookie_npc" => 80,
            "veteran_npc" => 150,
            "elite_npc" => 250,
            "hound" => 60,
            "troll" => 200,
            "drake" => 300,
            _ => 80
        };
        return (int)(baseHp * (1.0 + playerLevel / 10.0));
    }
}
