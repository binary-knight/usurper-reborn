using System;

namespace UsurperRemake.Systems;

/// <summary>Mental health bands (v1.1.15). Display strings live elsewhere.</summary>
public enum MentalBand
{
    Stable,     // 75-100
    Strained,   // 50-74
    Shaken,     // 25-49
    Breaking,   // 1-24
    Broken      // 0
}

/// <summary>
/// v1.1.15 Mental health core: bands, the addiction cap, clamped changes, dungeon strain and the
/// combat penalty. Pure logic, no UI. NPCs are skipped by Change and AddStrain.
/// </summary>
public static class MentalSystem
{
    /// <summary>Strain is kept in hundredths of a per mille, so one Mental point is this many units.</summary>
    public const int StrainUnitsPerPoint = GameConfig.MentalStrainPerPoint * 100;

    public static MentalBand GetBand(int mental)
    {
        if (mental >= GameConfig.MentalStableThreshold) return MentalBand.Stable;
        if (mental >= GameConfig.MentalStrainedThreshold) return MentalBand.Strained;
        if (mental >= GameConfig.MentalShakenThreshold) return MentalBand.Shaken;
        if (mental >= GameConfig.MentalBreakingThreshold) return MentalBand.Breaking;
        return MentalBand.Broken;
    }

    /// <summary>Highest Mental the character can hold: max minus half the addiction, in [0, max].</summary>
    public static int GetCap(Character c) =>
        Math.Clamp(GameConfig.MaxMentalStability - c.Addict / 2, 0, GameConfig.MaxMentalStability);

    /// <summary>
    /// Changes Mental by delta and returns the change actually applied (after minus before).
    /// The result is clamped to [0, GetCap]. A gain never lowers Mental: if Mental is already above
    /// the cap (addiction rose later), a gain applies 0. A loss while above the cap lands at or below
    /// the cap. NPCs are skipped and return 0.
    /// </summary>
    public static int Change(Character c, int delta)
    {
        if (c == null || c.IsNPC) return 0;
        int before = c.Mental;
        int after = Math.Clamp(before + delta, 0, GetCap(c));
        if (delta > 0 && after < before) after = before;
        c.Mental = after;
        return after - before;
    }

    /// <summary>
    /// Strain percentage for class and race. When both qualify the lower (kinder) value is taken,
    /// for example a Troll Cleric takes 80. MysticShaman has no class entry; as it is Troll, Orc or
    /// Gnoll only, it lands on 90 through its race.
    /// </summary>
    public static int GetStrainPct(CharacterClass cls, CharacterRace race)
    {
        int classPct = cls switch
        {
            CharacterClass.Cleric or CharacterClass.Paladin or CharacterClass.Tidesworn => GameConfig.MentalStrainPctDevout,
            CharacterClass.Sage => GameConfig.MentalStrainPctSage,
            CharacterClass.Barbarian => GameConfig.MentalStrainPctHardy,
            _ => 100
        };
        int racePct = race switch
        {
            CharacterRace.Troll or CharacterRace.Orc or CharacterRace.Gnoll => GameConfig.MentalStrainPctHardy,
            _ => 100
        };
        return Math.Min(classPct, racePct);
    }

    /// <summary>Companion cut in percent: 10 per story companion in the party, at most 20.</summary>
    public static int GetCompanionCutPct(int storyCompanionsInParty) =>
        Math.Clamp(storyCompanionsInParty * GameConfig.MentalStrainCompanionCutPct, 0, GameConfig.MentalStrainCompanionCutMaxPct);

    /// <summary>
    /// Adds dungeon strain in per mille and returns the Mental points lost (0 or more).
    /// Rounding: the strain is scaled to hundredths of a per mille as
    /// perMille x strainPct x (100 - companionCutPct) / 100, floored by integer division, and added
    /// to Character.MentalStrainRemainder. Each full 100_000 units (1000 per mille) costs 1 Mental;
    /// the rest carries to the next call. Non-positive strain and NPCs do nothing.
    /// </summary>
    public static int AddStrain(Character c, int perMille, int storyCompanionsInParty)
    {
        if (c == null || c.IsNPC || perMille <= 0) return 0;
        long scaled = (long)perMille * GetStrainPct(c.Class, c.Race) * (100 - GetCompanionCutPct(storyCompanionsInParty)) / 100;
        long total = c.MentalStrainRemainder + scaled;
        int points = (int)(total / StrainUnitsPerPoint);
        c.MentalStrainRemainder = (int)(total % StrainUnitsPerPoint);
        if (points == 0) return 0;
        return -Change(c, -points);
    }

    /// <summary>
    /// Combat penalty from Mental alone, as a positive fraction of damage and defence lost:
    /// 0 for Stable and Strained, 0.05 for Shaken, 0.10 for Breaking and Broken.
    /// Taking the worse of this and Grief, and the -15% Mental plus Fatigue cap, belong to a later
    /// piece (band effects), not here.
    /// </summary>
    public static float GetCombatPenalty(int mental) => GetBand(mental) switch
    {
        MentalBand.Shaken => GameConfig.MentalShakenCombatPenalty,
        MentalBand.Breaking or MentalBand.Broken => GameConfig.MentalBreakingCombatPenalty,
        _ => 0f
    };
}
