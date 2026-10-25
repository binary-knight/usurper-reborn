using System;
using System.Collections.Generic;

namespace UsurperRemake.Systems;

/// <summary>
/// v1.1.3 (council ruling 1): per-teammate tactics. Balanced is exactly the behaviour PR #134
/// shipped, read from the same GameConfig constants; Aggressive and Cautious bracket it.
/// Thresholds are fractions of max HP and are compared strictly below. In every column
/// EmergencySelfPotion &lt; Brace &lt; DefensiveFirst &lt; PotionMostInjured.
/// </summary>
public enum TeammateStance
{
    Balanced = 0,   // the default, and what a missing or unknown value means
    Aggressive = 1,
    Cautious = 2,
}

public readonly record struct TeammatePolicy(
    double EmergencySelfPotion,
    double PotionMostInjured,
    double DefensiveFirst,
    double Brace,
    bool MayTaunt,
    double TargetWeightMultiplier);

public static class TeammateStances
{
    public static TeammatePolicy PolicyFor(TeammateStance stance) => stance switch
    {
        TeammateStance.Aggressive => new TeammatePolicy(0.25, 0.45, 0.35, 0.30, true, 1.00),
        TeammateStance.Cautious => new TeammatePolicy(0.40, 0.60, 0.55, 0.50, false, 0.70),
        _ => new TeammatePolicy(
            GameConfig.TeammateEmergencySelfHealHpPercent,   // 0.30
            0.50,                                            // the potion-on-most-injured threshold PR #134 kept
            GameConfig.TeammateDefensivePriorityHpPercent,   // 0.40
            GameConfig.TeammateDefendHpPercent,              // 0.35
            true, 1.00),
    };

    /// <summary>Stable key for the owner's per-teammate stance: companions by companion id,
    /// everyone else by the same key the ability toggles use.</summary>
    public static string KeyFor(Character teammate) =>
        teammate.IsCompanion && teammate.CompanionId.HasValue
            ? "companion:" + teammate.CompanionId.Value
            : teammate.GetSkillToggleKey();

    public static string KeyForCompanion(CompanionId id) => "companion:" + id;

    /// <summary>Grouped players, echoes and mercenaries take no orders: always Balanced.</summary>
    public static bool TakesOrders(Character teammate) =>
        !teammate.IsGroupedPlayer && !teammate.IsEcho && !teammate.IsMercenary;

    public static TeammateStance Get(Character? owner, Character teammate)
    {
        if (owner == null || !TakesOrders(teammate)) return TeammateStance.Balanced;
        return owner.TeammateStances.TryGetValue(KeyFor(teammate), out int raw) && Enum.IsDefined(typeof(TeammateStance), raw)
            ? (TeammateStance)raw
            : TeammateStance.Balanced;
    }

    public static void Set(Character owner, string key, TeammateStance stance)
    {
        if (stance == TeammateStance.Balanced) owner.TeammateStances.Remove(key);
        else owner.TeammateStances[key] = (int)stance;
    }

    public static string NameKey(TeammateStance stance) => stance switch
    {
        TeammateStance.Aggressive => "party.stance.aggressive",
        TeammateStance.Cautious => "party.stance.cautious",
        _ => "party.stance.balanced",
    };
}
