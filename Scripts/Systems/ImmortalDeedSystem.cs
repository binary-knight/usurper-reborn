using System;
using System.Collections.Generic;

namespace UsurperRemake.Systems;

/// <summary>
/// 1.2.0 Temple gods piece 5b: what a player-god's bless did. Refused is true when the target no
/// longer follows the god (nothing changed). FavorGained is 0 when the day's cap is spent.
/// </summary>
public readonly record struct BlessOutcome(bool Refused, int FavorGained, int FavorNow, float Bonus, int Combats);

/// <summary>
/// 1.2.0 Temple gods piece 5b: what a chastise did. Refused is true when the target no longer
/// follows the god (nothing changed). FavorLost is the Favor taken (0 at Favor 0).
/// </summary>
public readonly record struct ChastiseOutcome(bool Refused, int FavorLost, int FavorNow);

/// <summary>
/// 1.2.0 Temple gods piece 5b: the rules of an immortal's deeds on followers (Pantheon, Divine
/// Deeds). Bless gives the follower GodBlessFavorGain Favor (FavorSource.ImmortalBlessing, at most
/// GodBlessFavorDailyCap a day) and a combat blessing whose bonus follows the follower's Favor tier
/// after that gain (BlessBonusFor). NPCs have no Favor; an NPC follower gets GodBlessBonusNpc (the full
/// 10%, as before tiers existed; user ruling 2026-09-29).
/// Smite never strikes the god's own follower (CanSmite). Three paths reach a follower: an NPC
/// (Bless on the NPC), a player live in another session (Bless with otherSession: Favor is changed
/// in memory and the tier's stat update is left to that session, GodBoonSystem.RequestRecalcForBoon;
/// that session's own save carries it), and a player not online (BlessSaved on the saved data,
/// written by SqlSaveBackend.UpdateFollowerSaveOffline; the load recalculates the stats).
/// </summary>
public static class ImmortalDeedSystem
{
    /// <summary>The bless bonus for a Favor tier: 5% Follower, 7% Devout, 10% Zealot and Chosen.</summary>
    public static float BlessBonusFor(GodFavorTier tier) => tier switch
    {
        GodFavorTier.Follower => GameConfig.GodBlessBonusFollower,
        GodFavorTier.Devout => GameConfig.GodBlessBonusDevout,
        _ => GameConfig.GodBlessBonusZealot,
    };

    /// <summary>True when followerGod names the god (any letter case). Blank names are never a match.</summary>
    public static bool IsOwnFollower(string? godName, string? followerGod) =>
        !string.IsNullOrWhiteSpace(godName) && !string.IsNullOrWhiteSpace(followerGod) &&
        followerGod.Trim().Equals(godName.Trim(), StringComparison.OrdinalIgnoreCase);

    /// <summary>
    /// The key a chastised follower is kept under on the god (Character.ChastisedToday): the
    /// follower's account username, trimmed and in lower case.
    /// </summary>
    public static string ChastiseKey(string? username) => (username ?? "").Trim().ToLowerInvariant();

    /// <summary>
    /// A god chastises each follower at most once a day. The day's list is kept on the god, in the
    /// god's own save (only the god's session writes it), and cleared with the deeds at the god's
    /// daily reset (ClearChastised).
    /// </summary>
    public static bool CanChastise(Character god, string? username)
    {
        string key = ChastiseKey(username);
        return god != null && key.Length > 0 && !(god.ChastisedToday ?? new List<string>()).Contains(key);
    }

    /// <summary>Records a follower as chastised today on the god.</summary>
    public static void MarkChastised(Character god, string? username)
    {
        string key = ChastiseKey(username);
        if (god == null || key.Length == 0) return;
        god.ChastisedToday ??= new List<string>();
        if (!god.ChastisedToday.Contains(key)) god.ChastisedToday.Add(key);
    }

    /// <summary>The god's daily reset (DailySystemManager, with the deeds): every follower can be chastised again.</summary>
    public static void ClearChastised(Character god)
    {
        if (god == null) return;
        god.ChastisedToday = new List<string>();
    }

    /// <summary>
    /// Chastise a player follower live in another session (otherSession, the tier's stat update is
    /// left to that session): GodChastiseFavorLoss Favor through FavorSystem.Change. Refused for an
    /// NPC (no Favor) or a player who no longer follows godName.
    /// </summary>
    public static ChastiseOutcome Chastise(Character follower, string godName, bool otherSession, GodSystem? gods = null)
    {
        if (follower == null || follower.IsNPC) return new ChastiseOutcome(true, 0, 0);
        var god = GodRegistry.GetWorshippedGod(follower, gods);
        if (god == null || !IsOwnFollower(godName, god.Value.Name)) return new ChastiseOutcome(true, 0, 0);
        int applied = FavorSystem.Change(follower, -GameConfig.GodChastiseFavorLoss, gods, deferBoonRecalc: otherSession);
        return new ChastiseOutcome(false, -applied, FavorSystem.GetFavor(follower, gods));
    }

    /// <summary>Chastise a player who is not online, on their saved data: the same rule as Chastise.</summary>
    public static ChastiseOutcome ChastiseSaved(PlayerData p, Dictionary<string, string>? playerGods, string godName)
    {
        if (p == null) return new ChastiseOutcome(true, 0, 0);
        var (current, _) = GodRegistry.StandingEntryFrom(p, playerGods);
        if (!IsOwnFollower(godName, current)) return new ChastiseOutcome(true, 0, 0);
        BindSaved(p, playerGods);
        int before = p.GodFavor;
        p.GodFavor = Math.Clamp(before - GameConfig.GodChastiseFavorLoss, GameConfig.GodFavorMin, GameConfig.GodFavorMax);
        return new ChastiseOutcome(false, before - p.GodFavor, p.GodFavor);
    }

    /// <summary>A player-god smites only a target that does not follow it.</summary>
    public static bool CanSmite(string? godName, string? targetGod) => !IsOwnFollower(godName, targetGod);

    /// <summary>
    /// The combat blessing: lasts at least GodBlessCombatDuration fights; a stronger blessing still
    /// running keeps its bonus (a bless never weakens one), else the new bonus applies.
    /// </summary>
    public static (int Combats, float Bonus) MergeBlessing(int combats, float bonus, float newBonus)
    {
        float merged = combats > 0 ? Math.Max(bonus, newBonus) : newBonus;
        return (Math.Max(combats, GameConfig.GodBlessCombatDuration), merged);
    }

    /// <summary>
    /// Bless a live character: an NPC, or a player in another session (otherSession, the tier's stat
    /// update is left to that session). A player who no longer follows godName is refused.
    /// </summary>
    public static BlessOutcome Bless(Character follower, string godName, bool otherSession, GodSystem? gods = null)
    {
        if (follower == null) return new BlessOutcome(true, 0, 0, 0f, 0);
        int gained = 0;
        if (!follower.IsNPC)
        {
            var god = GodRegistry.GetWorshippedGod(follower, gods);
            if (god == null || !IsOwnFollower(godName, god.Value.Name)) return new BlessOutcome(true, 0, 0, 0f, 0);
            gained = FavorSystem.GainCapped(follower, FavorSource.ImmortalBlessing, GameConfig.GodBlessFavorGain,
                GameConfig.GodBlessFavorDailyCap, gods, deferBoonRecalc: otherSession);
        }
        int favor = FavorSystem.GetFavor(follower, gods);
        float bonus = follower.IsNPC ? GameConfig.GodBlessBonusNpc : BlessBonusFor(FavorSystem.GetTier(favor));
        var (combats, merged) = MergeBlessing(follower.DivineBlessingCombats, follower.DivineBlessingBonus, bonus);
        follower.DivineBlessingCombats = combats;
        follower.DivineBlessingBonus = merged;
        return new BlessOutcome(false, gained, favor, bonus, combats);
    }

    /// <summary>
    /// The saved data brought to the Favor state its load would give it (GodRegistry.ApplyLoad and
    /// FavorSystem.Bind): a save from before Favor starts at GodFavorLegacyStart with its god, and
    /// Favor stored for another god restarts at 0. Returns the effective god, or "" with none
    /// (playerGods: the save's canon worship dictionary, which wins over the player-god).
    /// </summary>
    public static string BindSaved(PlayerData p, Dictionary<string, string>? playerGods)
    {
        var (god, _) = GodRegistry.StandingEntryFrom(p, playerGods);
        if (p.GodFavorSchema < GameConfig.GodFavorSchemaCurrent)
        {
            p.GodFavor = god.Length > 0 ? GameConfig.GodFavorLegacyStart : 0;
            p.GodFavorGod = god;
            p.DaysSinceDevotion = 0;
            p.GodFavorDayGains = new Dictionary<string, int>();
            p.GodFavorSchema = GameConfig.GodFavorSchemaCurrent;
        }
        p.GodFavorDayGains ??= new Dictionary<string, int>();
        if (god.Length == 0) return "";
        if (!god.Equals(p.GodFavorGod ?? "", StringComparison.OrdinalIgnoreCase))
        {
            p.GodFavor = 0;
            p.GodFavorGod = god;
            p.DaysSinceDevotion = 0;
        }
        p.GodFavor = Math.Clamp(p.GodFavor, GameConfig.GodFavorMin, GameConfig.GodFavorMax);
        return god;
    }

    /// <summary>
    /// Bless a player who is not online, on their saved data: the same rule as Bless (Favor with the
    /// daily cap, the tier's bonus after it, a stronger blessing kept). Refused, with nothing changed,
    /// when the save's god is not godName.
    /// </summary>
    public static BlessOutcome BlessSaved(PlayerData p, Dictionary<string, string>? playerGods, string godName)
    {
        if (p == null) return new BlessOutcome(true, 0, 0, 0f, 0);
        var (current, _) = GodRegistry.StandingEntryFrom(p, playerGods);
        if (!IsOwnFollower(godName, current)) return new BlessOutcome(true, 0, 0, 0f, 0);
        BindSaved(p, playerGods);
        string key = FavorSource.ImmortalBlessing.ToString();
        int today = p.GodFavorDayGains.TryGetValue(key, out int v) ? v : 0;
        int room = Math.Max(0, GameConfig.GodBlessFavorDailyCap - today);
        int before = p.GodFavor;
        p.GodFavor = Math.Clamp(before + Math.Min(GameConfig.GodBlessFavorGain, room), GameConfig.GodFavorMin, GameConfig.GodFavorMax);
        int gained = p.GodFavor - before;
        if (gained > 0) p.GodFavorDayGains[key] = today + gained;
        float bonus = BlessBonusFor(FavorSystem.GetTier(p.GodFavor));
        var (combats, merged) = MergeBlessing(p.DivineBlessingCombats, p.DivineBlessingBonus, bonus);
        p.DivineBlessingCombats = combats;
        p.DivineBlessingBonus = merged;
        return new BlessOutcome(false, gained, p.GodFavor, bonus, combats);
    }
}
