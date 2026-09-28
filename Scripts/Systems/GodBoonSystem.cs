using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;

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
    public static GodDomain ParseDomain(string? saved)
    {
        if (string.IsNullOrWhiteSpace(saved)) return GodDomain.None;
        string s = saved.Trim();
        if (!char.IsLetter(s[0])) return GodDomain.None;   // Enum.TryParse would take "3"
        return Enum.TryParse<GodDomain>(s, true, out var d) && Enum.IsDefined(typeof(GodDomain), d) ? d : GodDomain.None;
    }

    /// <summary>The schema guard for the saved domain: one of the ten names, or "" (not chosen).</summary>
    public static string StoredDomain(string? saved)
    {
        var d = ParseDomain(saved);
        return d == GodDomain.None ? "" : d.ToString();
    }

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
        if (god.Value.IsCanon) return DomainOfCanon(god.Value.Name);
        return PlayerGodCacheFits(c, god.Value.Name) ? c.PlayerGodBoonDomain : GodDomain.None;
    }

    private static bool PlayerGodCacheFits(Character c, string god) =>
        !string.IsNullOrEmpty(c.PlayerGodBoonGod) && c.PlayerGodBoonGod.Equals(god, StringComparison.OrdinalIgnoreCase);

    /// <summary>The god's scale of its boon: 100 for a canon god (fixed), the cached standing scale for a player-god.</summary>
    public static int GetScalePct(Character c, GodSystem? gods = null)
    {
        if (c == null || c.IsNPC) return 0;
        var god = GodRegistry.GetWorshippedGod(c, gods);
        if (god == null) return 0;
        if (god.Value.IsCanon) return 100;
        return PlayerGodCacheFits(c, god.Value.Name) ? Math.Max(0, c.PlayerGodBoonScalePct) : 0;
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

    // ---------------- Player-gods ----------------

    /// <summary>
    /// A player-god's boon scale in percent of the canon boon at the same tier: its standing over
    /// the strongest canon god's, clamped to GodPlayerBoonFloorPct..GodPlayerBoonCapPct. With no canon
    /// standing, a god with followers' Favor is at the cap and one without at the floor. An immortal
    /// away more than GodPlayerInactiveDays loses GodPlayerInactiveDecayPctPerDay a day past that,
    /// never below the floor.
    /// </summary>
    public static int PlayerGodScalePct(long standing, long strongestCanon, int daysInactive)
    {
        int floor = GameConfig.GodPlayerBoonFloorPct, cap = GameConfig.GodPlayerBoonCapPct;
        long ratio = strongestCanon > 0 ? Math.Max(0, standing) * 100 / strongestCanon : (standing > 0 ? cap : floor);
        int scale = (int)Math.Clamp(ratio, floor, cap);
        if (daysInactive > GameConfig.GodPlayerInactiveDays)
        {
            long decay = (long)(daysInactive - GameConfig.GodPlayerInactiveDays) * GameConfig.GodPlayerInactiveDecayPctPerDay;
            scale = (int)Math.Max(floor, scale - decay);
        }
        return scale;
    }

    /// <summary>Whole days since the immortal's last login; 0 while online or with no record.</summary>
    public static int DaysInactive(bool isOnline, DateTime? lastLoginUtc, DateTime nowUtc)
    {
        if (isOnline || lastLoginUtc == null) return 0;
        double days = (nowUtc - lastLoginUtc.Value).TotalDays;
        return days <= 0 ? 0 : (int)Math.Min(Math.Floor(days), int.MaxValue);
    }

    /// <summary>The highest standing among the ten canon gods (0 when none has followers).</summary>
    public static long StrongestCanon(IReadOnlyDictionary<string, GodStanding> standings) =>
        standings == null ? 0 : standings.Where(kv => GodRegistry.IsCanon(kv.Key)).Select(kv => kv.Value.Standing).DefaultIfEmpty(0).Max();

    private static long StandingOf(IReadOnlyDictionary<string, GodStanding> standings, string god) =>
        standings?.FirstOrDefault(kv => kv.Key.Equals(god, StringComparison.OrdinalIgnoreCase)).Value.Standing ?? 0;

    /// <summary>
    /// Online: a player-god's domain and boon scale from the saved world, the immortal's row (domain,
    /// last login, online now) and every saved character's standing. (None, 0) for an unknown god or
    /// an immortal who has not chosen a domain.
    /// </summary>
    public static async Task<(GodDomain Domain, int ScalePct)> PlayerGodBoonAsync(string god, SqlSaveBackend backend, DateTime nowUtc)
    {
        if (string.IsNullOrWhiteSpace(god) || backend == null) return (GodDomain.None, 0);
        var info = (await backend.GetImmortalPlayers())
            .FirstOrDefault(i => string.Equals(i.DivineName, god, StringComparison.OrdinalIgnoreCase));
        if (info == null) return (GodDomain.None, 0);
        var domain = ParseDomain(info.DivineDomain);
        if (domain == GodDomain.None) return (GodDomain.None, 0);
        var standings = await Task.Run(() => backend.GetGodStandings());
        int scale = PlayerGodScalePct(StandingOf(standings, god), StrongestCanon(standings), DaysInactive(info.IsOnline, info.LastLogin, nowUtc));
        return (domain, scale);
    }

    /// <summary>Caches a player-god's boon on a follower (runtime only).</summary>
    public static void SetPlayerGodBoon(Character c, string god, GodDomain domain, int scalePct)
    {
        if (c == null) return;
        c.PlayerGodBoonGod = god ?? "";
        c.PlayerGodBoonDomain = domain;
        c.PlayerGodBoonScalePct = Math.Max(0, scalePct);
    }

    /// <summary>
    /// Refreshes the cached player-god boon of a character (at login and at the Temple). A canon
    /// worshipper, the godless and NPCs clear it. Online it reads the saved world
    /// (PlayerGodBoonAsync). Single-player, a player-god's followers are NPCs, so no player boon.
    /// A read that fails keeps the previous cache. The stats are recalculated after, so the boon's
    /// max HP follows the refreshed god, domain and scale.
    /// </summary>
    public static async Task RefreshPlayerGodBoonAsync(Character c)
    {
        if (c == null) return;
        var god = c.IsNPC ? null : GodRegistry.GetWorshippedGod(c);
        if (god == null || god.Value.IsCanon || !UsurperRemake.BBS.DoorMode.IsOnlineMode
            || SaveSystem.Instance?.Backend is not SqlSaveBackend backend)
        {
            SetPlayerGodBoon(c, "", GodDomain.None, 0);
        }
        else
        {
            try
            {
                var (domain, scale) = await PlayerGodBoonAsync(god.Value.Name, backend, DateTime.UtcNow);
                SetPlayerGodBoon(c, god.Value.Name, domain, scale);
            }
            catch (Exception ex)
            {
                DebugLogger.Instance.LogWarning("FAITH", $"Player-god boon unavailable for {c.Name2}: {ex.Message}");
            }
        }
        RecalculateForBoon(c);
    }

    /// <summary>
    /// Updates a player's max HP and max mana after an input of a god boon changed (the god, the
    /// Favor tier, a player-god's domain, scale or configured boons), so the boons follow at once.
    /// Only the boons' share of MaxHP and MaxMana is updated (Character.RecalculateBoonShare); every
    /// other stat is left alone. HP and mana are only clamped to the new max, never raised. NPCs are
    /// skipped, and so is a GodSystem other than the shared one, since the stats read the shared
    /// registry. A player who follows no player-god loses the configured boons (CachedBoonEffects)
    /// first, so their max HP and mana go with the god. For the acting session's own character only;
    /// another session's character gets RequestRecalcForBoon.
    /// </summary>
    public static void RecalculateForBoon(Character c, GodSystem? gods = null)
    {
        if (c == null || c.IsNPC) return;
        if (string.IsNullOrWhiteSpace(c.WorshippedGod)) c.CachedBoonEffects = null;
        if (gods != null && !ReferenceEquals(gods, UsurperRemake.GodSystemSingleton.Instance)) return;
        c.RecalculateBoonShare();
    }

    /// <summary>
    /// A character played in another session had its god boon caches changed by this session: the
    /// update is left to its own session (GodBoonRecalcPending), which applies it at its next safe
    /// point (ApplyPendingBoonRecalc), so a hit or a heal written there at the same moment is not
    /// lost. The caller writes the caches first, then this sets the flag.
    /// </summary>
    public static void RequestRecalcForBoon(Character c)
    {
        if (c == null || c.IsNPC) return;
        c.GodBoonRecalcPending = true;
    }

    /// <summary>
    /// The session's own character, at a safe point (the top of the location loop, the end of a
    /// fight): applies a boon update another session left pending. The flag is cleared before the
    /// update, so caches written during it stay pending for the next safe point.
    /// </summary>
    public static void ApplyPendingBoonRecalc(Character c)
    {
        if (c == null || !c.GodBoonRecalcPending) return;
        c.GodBoonRecalcPending = false;
        RecalculateForBoon(c);
    }

    /// <summary>
    /// A player-god's configured boons (Pantheon) reach a follower in another session: the cache is
    /// set from the config and the follower's own session updates max HP and mana at its next safe
    /// point (RequestRecalcForBoon).
    /// </summary>
    public static void SetConfiguredBoons(Character c, string config)
    {
        if (c == null || c.IsNPC) return;
        c.CachedBoonEffects = DivineBoonRegistry.CalculateEffects(config);
        RequestRecalcForBoon(c);
    }

    /// <summary>
    /// A player in another session was recruited by an immortal (Pantheon): the follower takes the
    /// god's configured boons and the god's domain at the given scale, and the follower's own
    /// session updates the stats at its next safe point. With no scale
    /// (the standings could not be read) the domain cache is kept, as RefreshPlayerGodBoonAsync
    /// keeps it when its read fails; a cache for another god gives no boon (PlayerGodCacheFits).
    /// </summary>
    public static void ApplyRecruit(Character immortal, Character follower, int? scalePct)
    {
        if (immortal == null || follower == null || follower.IsNPC || string.IsNullOrWhiteSpace(immortal.DivineName)) return;
        SetConfiguredBoons(follower, immortal.DivineBoonConfig);
        if (scalePct is not int scale) return;
        ApplyDomainChange(immortal.DivineName, ParseDomain(immortal.DivineDomain), scale, new[] { follower });
    }

    /// <summary>
    /// Online: a player recruited by an immortal gets the god's boons at their own session's next
    /// safe point (ApplyRecruit leaves the update pending), at the god's current
    /// scale (the immortal is online, so no idle decay). The domain comes from the immortal in
    /// memory, like ApplyDomainChangeAsync. A standings read that fails keeps the domain cache, as
    /// the login refresh does, until the next refresh (login or Temple).
    /// </summary>
    public static async Task ApplyRecruitAsync(Character immortal, Character follower)
    {
        if (immortal == null || follower == null || string.IsNullOrWhiteSpace(immortal.DivineName)) return;
        int? scale = null;
        if (UsurperRemake.BBS.DoorMode.IsOnlineMode && SaveSystem.Instance?.Backend is SqlSaveBackend backend)
        {
            try
            {
                var standings = await Task.Run(() => backend.GetGodStandings());
                scale = PlayerGodScalePct(StandingOf(standings, immortal.DivineName), StrongestCanon(standings), 0);
            }
            catch (Exception ex)
            {
                DebugLogger.Instance.LogWarning("FAITH", $"Player-god scale unavailable for the recruit of {immortal.DivineName}: {ex.Message}");
            }
        }
        ApplyRecruit(immortal, follower, scale);
    }

    /// <summary>
    /// A player-god's domain was chosen: each follower of that god (players only, played in other
    /// sessions) caches the new domain at the given scale, and each follower's own session updates
    /// the stats at its next safe point.
    /// </summary>
    public static void ApplyDomainChange(string god, GodDomain domain, int scalePct, IEnumerable<Character> followers)
    {
        if (string.IsNullOrWhiteSpace(god) || followers == null) return;
        foreach (var f in followers)
        {
            if (f == null || f.IsNPC) continue;
            var worshipped = GodRegistry.GetWorshippedGod(f);
            if (worshipped == null || worshipped.Value.IsCanon
                || !worshipped.Value.Name.Equals(god, StringComparison.OrdinalIgnoreCase)) continue;
            SetPlayerGodBoon(f, worshipped.Value.Name, domain, scalePct);
            RequestRecalcForBoon(f);
        }
    }

    /// <summary>
    /// Online: after an immortal picks their domain, the followers playing now get the domain boon
    /// at once, at the god's current scale (the immortal is online, so no idle decay). The domain is
    /// not saved yet, so PlayerGodBoonAsync cannot read it. Single-player has no player followers.
    /// </summary>
    public static async Task ApplyDomainChangeAsync(Character immortal)
    {
        if (immortal == null || string.IsNullOrWhiteSpace(immortal.DivineName)) return;
        if (!UsurperRemake.BBS.DoorMode.IsOnlineMode || SaveSystem.Instance?.Backend is not SqlSaveBackend backend) return;
        var server = UsurperRemake.Server.MudServer.Instance;
        if (server == null) return;
        try
        {
            string god = immortal.DivineName;
            var standings = await Task.Run(() => backend.GetGodStandings());
            int scale = PlayerGodScalePct(StandingOf(standings, god), StrongestCanon(standings), 0);
            var followers = server.ActiveSessions.Values
                .Select(s => s.Context?.Engine?.CurrentPlayer)
                .Where(p => p != null && !ReferenceEquals(p, immortal))
                .Cast<Character>()
                .ToList();
            ApplyDomainChange(god, ParseDomain(immortal.DivineDomain), scale, followers);
        }
        catch (Exception ex)
        {
            DebugLogger.Instance.LogWarning("FAITH", $"Player-god domain change not applied to followers of {immortal.DivineName}: {ex.Message}");
        }
    }
}
