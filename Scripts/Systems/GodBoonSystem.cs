using System;
using System.Collections.Generic;
using System.Linq;

namespace UsurperRemake.Systems;

/// <summary>
/// 1.2.0 Temple gods piece 2: the ten boon domains, one per canon god. A player-god's immortal
/// picks one of these for their god (piece 2, commit 2).
/// </summary>
public enum GodDomain
{
    None,
    Light,      // Solarius
    War,        // Valorian
    Love,       // Amara
    Law,        // Judicar
    Shadow,     // Umbrath
    Earth,      // Terran
    Death,      // Mortis
    Magic,      // Arcanus
    Nature,     // Sylvana
    Chaos,      // Discordia
}

/// <summary>1.2.0 Temple gods piece 2: the Mental losses a god's ward softens (Devout and up).</summary>
public enum MentalWard
{
    None,
    Grief,       // Amara: companion, NPC and Depression-stage grief losses halved
    Death,       // Mortis: the Mental death loss halved
    Witness,     // Mortis: witnessing losses halved
    Strain,      // Sylvana: dungeon strain (rooms and fights) reduced
    Boss,        // Solarius: the boss fight loss halved
    OldGod,      // Arcanus: the Old God fight loss halved
    NearDeath,   // Valorian: the near-death loss halved
    Withdrawal,  // Judicar: withdrawal losses halved
    DrugCrash,   // Terran: drug crash and overdose losses halved
    Flee,        // Umbrath: the flee loss halved
    Fear,        // Discordia: the fear chance at combat start halved
}

/// <summary>
/// 1.2.0 Temple gods piece 2: each god's distinct boon, scaled by the follower's Favor tier
/// (Follower 1/3, Devout 2/3, Zealot and Chosen full), and the god's Mental ward at Devout and up.
/// A canon god's boon is fixed. A player-god gives the boon of the domain its immortal picked,
/// scaled by the god's standing (GodBoonSystem.PlayerGodScalePct, cached on the character at login
/// and at the Temple). NPCs never get a boon or a ward.
/// </summary>
public static class GodBoonSystem
{
    private static readonly GodDomain[] CanonDomains =
    {
        GodDomain.Light, GodDomain.War, GodDomain.Love, GodDomain.Law, GodDomain.Shadow,
        GodDomain.Earth, GodDomain.Death, GodDomain.Magic, GodDomain.Nature, GodDomain.Chaos
    };

    /// <summary>The ten domains in Temple order (the order of GameConfig.CanonGodNames).</summary>
    public static IReadOnlyList<GodDomain> AllDomains => CanonDomains;

    /// <summary>The domain of a canon god (any letter case), or None.</summary>
    public static GodDomain DomainOfCanon(string? god)
    {
        if (string.IsNullOrWhiteSpace(god)) return GodDomain.None;
        int i = Array.FindIndex(GameConfig.CanonGodNames, g => g.Equals(god, StringComparison.OrdinalIgnoreCase));
        return i >= 0 && i < CanonDomains.Length ? CanonDomains[i] : GodDomain.None;
    }

    /// <summary>The canon god whose boon a domain is, or "".</summary>
    public static string CanonGodOf(GodDomain d)
    {
        int i = Array.IndexOf(CanonDomains, d);
        return i >= 0 ? GameConfig.CanonGodNames[i] : "";
    }

    /// <summary>A saved domain name read back: one of the ten, or None for blank or unknown text.</summary>
    public static GodDomain ParseDomain(string? saved) =>
        !string.IsNullOrWhiteSpace(saved) && Enum.TryParse<GodDomain>(saved.Trim(), true, out var d)
            && d != GodDomain.None && Enum.IsDefined(typeof(GodDomain), d) ? d : GodDomain.None;

    /// <summary>The ward of each domain.</summary>
    public static MentalWard[] WardsOf(GodDomain d) => d switch
    {
        GodDomain.Light => new[] { MentalWard.Boss },
        GodDomain.War => new[] { MentalWard.NearDeath },
        GodDomain.Love => new[] { MentalWard.Grief },
        GodDomain.Law => new[] { MentalWard.Withdrawal },
        GodDomain.Shadow => new[] { MentalWard.Flee },
        GodDomain.Earth => new[] { MentalWard.DrugCrash },
        GodDomain.Death => new[] { MentalWard.Death, MentalWard.Witness },
        GodDomain.Magic => new[] { MentalWard.OldGod },
        GodDomain.Nature => new[] { MentalWard.Strain },
        GodDomain.Chaos => new[] { MentalWard.Fear },
        _ => Array.Empty<MentalWard>()
    };

    /// <summary>Percent of the full boon for a tier: GodBoon*StrengthPct.</summary>
    public static int TierStrengthPct(GodFavorTier tier) => tier switch
    {
        GodFavorTier.Chosen => GameConfig.GodBoonChosenStrengthPct,
        GodFavorTier.Zealot => GameConfig.GodBoonZealotStrengthPct,
        GodFavorTier.Devout => GameConfig.GodBoonDevoutStrengthPct,
        _ => GameConfig.GodBoonFollowerStrengthPct
    };

    /// <summary>
    /// The domain of the god the character worships: a canon god's own, or the player-god's cached
    /// domain (only while the cache names the god worshipped now). None for NPCs and the godless.
    /// </summary>
    public static GodDomain GetDomain(Character c, GodSystem? gods = null)
    {
        if (c == null || c.IsNPC) return GodDomain.None;
        var god = GodRegistry.GetWorshippedGod(c, gods);
        if (god == null) return GodDomain.None;
        return god.Value.IsCanon ? DomainOfCanon(god.Value.Name) : GodDomain.None;
    }

    /// <summary>The god's scale of its boon: 100 for a canon god.</summary>
    public static int GetScalePct(Character c, GodSystem? gods = null)
    {
        if (c == null || c.IsNPC) return 0;
        var god = GodRegistry.GetWorshippedGod(c, gods);
        return god != null && god.Value.IsCanon ? 100 : 0;
    }

    /// <summary>Percent of the full canon boon the character gets now: tier strength times the god's scale.</summary>
    public static int GetStrengthPct(Character c, GodSystem? gods = null)
    {
        if (GetDomain(c, gods) == GodDomain.None) return 0;
        int tier = TierStrengthPct(FavorSystem.GetTier(FavorSystem.GetFavor(c, gods)));
        return (int)((long)tier * GetScalePct(c, gods) / 100);
    }

    /// <summary>
    /// The part of a full boon value the character gets from domain d, as a percent (fractional):
    /// fullPct x strength / 100, or 0 when the character's god is not of that domain.
    /// </summary>
    public static double Pct(Character c, GodDomain d, int fullPct, GodSystem? gods = null)
    {
        if (fullPct <= 0 || c == null || c.IsNPC) return 0;
        if (GetDomain(c, gods) != d) return 0;
        return fullPct * GetStrengthPct(c, gods) / 100.0;
    }

    /// <summary>
    /// The rounding rule for whole-number boons: value x pct / 100 rounded half up, and at least 1
    /// while the boon is active on a positive value. Returns the bonus alone.
    /// </summary>
    public static long Bonus(long value, double pct)
    {
        if (value <= 0 || pct <= 0) return 0;
        long b = (long)Math.Floor(value * pct / 100.0 + 0.5);
        return Math.Max(1, b);
    }

    /// <summary>value plus the domain's boon on it (Bonus rounding).</summary>
    public static long Apply(Character c, GodDomain d, int fullPct, long value, GodSystem? gods = null) =>
        value + Bonus(value, Pct(c, d, fullPct, gods));

    // ---------------- Display ----------------

    /// <summary>A boon value at a strength, for display: up to one decimal.</summary>
    private static string At(int fullPct, int strengthPct) =>
        (fullPct * strengthPct / 100.0).ToString("0.#", System.Globalization.CultureInfo.InvariantCulture);

    /// <summary>The boon of a domain at a strength (percent of the full canon boon), in the player's language.</summary>
    public static string DescribeBoon(GodDomain d, int strengthPct) => d switch
    {
        GodDomain.Light => Loc.Get("god.boon.light", At(GameConfig.GodBoonSolariusUndeadDamagePct, strengthPct)),
        GodDomain.War => Loc.Get("god.boon.war", At(GameConfig.GodBoonValorianLowHpDamagePct, strengthPct)),
        GodDomain.Love => Loc.Get("god.boon.love", At(GameConfig.GodBoonAmaraHealPct, strengthPct)),
        GodDomain.Law => Loc.Get("god.boon.law", At(GameConfig.GodBoonJudicarDefencePct, strengthPct), At(GameConfig.GodBoonJudicarBountyPct, strengthPct)),
        GodDomain.Shadow => Loc.Get("god.boon.shadow", At(GameConfig.GodBoonUmbrathCritPct, strengthPct), At(GameConfig.GodBoonUmbrathTheftPct, strengthPct)),
        GodDomain.Earth => Loc.Get("god.boon.earth", At(GameConfig.GodBoonTerranMaxHpPct, strengthPct), At(GameConfig.GodBoonTerranYieldPct, strengthPct)),
        GodDomain.Death => Loc.Get("god.boon.death", At(GameConfig.GodBoonMortisDeathGoldCutPct, strengthPct)),
        GodDomain.Magic => Loc.Get("god.boon.magic", At(GameConfig.GodBoonArcanusSpellPct, strengthPct), At(GameConfig.GodBoonArcanusManaRegenPct, strengthPct)),
        GodDomain.Nature => Loc.Get("god.boon.nature", At(GameConfig.GodBoonSylvanaWildernessPct, strengthPct)),
        GodDomain.Chaos => Loc.Get("god.boon.chaos", At(GameConfig.GodBoonDiscordiaPvpDamagePct, strengthPct), At(GameConfig.GodBoonDiscordiaFirstActionFailPct, strengthPct)),
        _ => ""
    };

    /// <summary>The Mental ward of a domain (Devout and up), in the player's language.</summary>
    public static string DescribeWard(GodDomain d)
    {
        var wards = WardsOf(d);
        if (wards.Length == 0) return "";
        return Loc.Get("god.ward." + d.ToString().ToLowerInvariant(), WardCutPct(wards[0]));
    }

    /// <summary>The name of a domain, in the player's language.</summary>
    public static string DomainName(GodDomain d) =>
        d == GodDomain.None ? "" : Loc.Get("god.domain." + d.ToString().ToLowerInvariant());

    // ---------------- Each boon where it belongs ----------------

    /// <summary>Arcanus: spell damage (SpellSystem.ScaleSpellEffect).</summary>
    public static long SpellDamage(Character caster, long damage, GodSystem? gods = null) =>
        Apply(caster, GodDomain.Magic, GameConfig.GodBoonArcanusSpellPct, damage, gods);

    /// <summary>Arcanus: mana regenerated each combat round.</summary>
    public static long ManaRegen(Character c, long regen, GodSystem? gods = null) =>
        Apply(c, GodDomain.Magic, GameConfig.GodBoonArcanusManaRegenPct, regen, gods);

    /// <summary>Amara: a heal the follower casts (spells and abilities, on anyone in the party).</summary>
    public static long PartyHeal(Character caster, long heal, GodSystem? gods = null) =>
        Apply(caster, GodDomain.Love, GameConfig.GodBoonAmaraHealPct, heal, gods);

    /// <summary>Amara: the strength of a party ward the follower raises.</summary>
    public static long PartyWard(Character caster, long ward, GodSystem? gods = null) =>
        Apply(caster, GodDomain.Love, GameConfig.GodBoonAmaraHealPct, ward, gods);

    /// <summary>Judicar: a bounty reward paid to the follower.</summary>
    public static long BountyReward(Character c, long reward, GodSystem? gods = null) =>
        Apply(c, GodDomain.Law, GameConfig.GodBoonJudicarBountyPct, reward, gods);

    /// <summary>Umbrath: percentage points added to a theft's success chance (and its cap).</summary>
    public static double TheftChanceBonusPct(Character c, GodSystem? gods = null) =>
        Pct(c, GodDomain.Shadow, GameConfig.GodBoonUmbrathTheftPct, gods);

    /// <summary>Terran: max HP bonus on a computed max HP (Character.RecalculateStats).</summary>
    public static long MaxHpBonus(Character c, long maxHp, GodSystem? gods = null) =>
        Bonus(maxHp, Pct(c, GodDomain.Earth, GameConfig.GodBoonTerranMaxHpPct, gods));

    /// <summary>Terran: herb and settlement yields (garden herbs a day, the council share).</summary>
    public static long EarthYield(Character c, long yield, GodSystem? gods = null) =>
        Apply(c, GodDomain.Earth, GameConfig.GodBoonTerranYieldPct, yield, gods);

    /// <summary>Mortis: the gold a death penalty takes, reduced.</summary>
    public static long DeathGoldLoss(Character c, long loss, GodSystem? gods = null) =>
        Math.Max(0, loss - Bonus(loss, Pct(c, GodDomain.Death, GameConfig.GodBoonMortisDeathGoldCutPct, gods)));

    /// <summary>Sylvana: wilderness gold and XP.</summary>
    public static long WildernessGain(Character c, long gain, GodSystem? gods = null) =>
        Apply(c, GodDomain.Nature, GameConfig.GodBoonSylvanaWildernessPct, gain, gods);

    /// <summary>Discordia: PvP damage.</summary>
    public static long PvpDamage(Character attacker, long damage, GodSystem? gods = null) =>
        Apply(attacker, GodDomain.Chaos, GameConfig.GodBoonDiscordiaPvpDamagePct, damage, gods);

    /// <summary>Discordia: chance in percent that each foe's first action fails.</summary>
    public static double DiscordiaFirstActionFailPct(Character c, GodSystem? gods = null) =>
        Pct(c, GodDomain.Chaos, GameConfig.GodBoonDiscordiaFirstActionFailPct, gods);

    /// <summary>True when the character's god gives this ward and the character is Devout or higher.</summary>
    public static bool HasWard(Character c, MentalWard ward, GodSystem? gods = null)
    {
        if (c == null || c.IsNPC || ward == MentalWard.None) return false;
        var domain = GetDomain(c, gods);
        if (domain == GodDomain.None || Array.IndexOf(WardsOf(domain), ward) < 0) return false;
        return FavorSystem.GetTier(FavorSystem.GetFavor(c, gods)) >= GodFavorTier.Devout;
    }

    /// <summary>The percent a ward cuts: GodWardSylvanaStrainCutPct for strain, GodWardLossCutPct for the rest.</summary>
    public static int WardCutPct(MentalWard ward) =>
        ward == MentalWard.Strain ? GameConfig.GodWardSylvanaStrainCutPct : GameConfig.GodWardLossCutPct;

    /// <summary>A loss (positive points) after the character's ward: reduced by WardCutPct, rounded down, when warded.</summary>
    public static long Warded(Character c, MentalWard ward, long loss, GodSystem? gods = null)
    {
        if (loss <= 0 || !HasWard(c, ward, gods)) return loss;
        return loss - loss * WardCutPct(ward) / 100;
    }
}
