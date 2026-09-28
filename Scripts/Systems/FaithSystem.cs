using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;

namespace UsurperRemake.Systems;

/// <summary>1.2.0 Temple gods: the Favor tier of a follower with their god (GameConfig.GodFavorTier*Min).</summary>
public enum GodFavorTier
{
    Follower,   // 0-24
    Devout,     // 25-49
    Zealot,     // 50-74
    Chosen      // 75-100
}

/// <summary>
/// 1.2.0 Temple gods: a source of Favor that has its own daily cap. The amount gained today from
/// each source is saved on Character.GodFavorDayGains (keyed by the enum name) and cleared by
/// FavorSystem.ApplyDailyReset. Later pieces add the call sites.
/// </summary>
public enum FavorSource
{
    Prayer,
    GoldSacrifice,
    ItemSacrifice,
    Deed,
}

/// <summary>The god a character worships: a canon god or an ascended player-god.</summary>
public readonly record struct WorshippedGod(string Name, bool IsCanon);

/// <summary>One god in the unified list (canon ten plus ascended player-gods).</summary>
public readonly record struct GodListEntry(string Name, bool IsCanon);

/// <summary>A god's standing: the sum of its followers' Favor, and how many followers that is.</summary>
public readonly record struct GodStanding(string God, long Standing, int Followers);

/// <summary>
/// 1.2.0 Temple gods piece 1: one god system. Canon gods and player-gods are one list, and a
/// character worships exactly one of them. Until the Temple rework (piece 7) the choice stays in
/// the two stores the game already saves (a canon god in GodSystem's worship dictionary, saved as
/// StorySystemsData.PlayerGods; a player-god in Character.WorshippedGod); this class is the one API
/// over both, and keeps them mutually exclusive. Manwe is never worshippable and never listed.
/// </summary>
public static class GodRegistry
{
    private static GodSystem Gods(GodSystem? gods) => gods ?? UsurperRemake.GodSystemSingleton.Instance;

    /// <summary>True for one of the ten canon gods (any letter case).</summary>
    public static bool IsCanon(string? name) =>
        !string.IsNullOrWhiteSpace(name) &&
        GameConfig.CanonGodNames.Any(g => g.Equals(name, StringComparison.OrdinalIgnoreCase));

    /// <summary>The canon spelling of a canon god's name, or null.</summary>
    public static string? CanonName(string? name) =>
        string.IsNullOrWhiteSpace(name) ? null
            : GameConfig.CanonGodNames.FirstOrDefault(g => g.Equals(name, StringComparison.OrdinalIgnoreCase));

    /// <summary>True for Manwe (the Supreme Creator), who is never worshipped or listed.</summary>
    public static bool IsManwe(string? name) =>
        !string.IsNullOrWhiteSpace(name) && name.Equals(GameConfig.SupremeCreatorName, StringComparison.OrdinalIgnoreCase);

    /// <summary>
    /// The unified god list: the ten canon gods first, then each ascended player-god once. Blank
    /// names, Manwe and player-god names that repeat a canon name are left out.
    /// </summary>
    public static List<GodListEntry> AllGods(IEnumerable<string>? playerGodNames)
    {
        var list = GameConfig.CanonGodNames.Select(n => new GodListEntry(n, true)).ToList();
        var seen = new HashSet<string>(GameConfig.CanonGodNames, StringComparer.OrdinalIgnoreCase);
        foreach (var name in playerGodNames ?? Enumerable.Empty<string>())
        {
            if (string.IsNullOrWhiteSpace(name) || IsManwe(name)) continue;
            string trimmed = name.Trim();
            if (seen.Add(trimmed)) list.Add(new GodListEntry(trimmed, false));
        }
        return list;
    }

    /// <summary>
    /// The unified god list with the ascended player-gods of this world: online, every immortal in
    /// the database; single-player, the current character if they have ascended.
    /// </summary>
    public static async Task<List<GodListEntry>> AllGodsAsync()
    {
        var names = new List<string>();
        try
        {
            if (UsurperRemake.BBS.DoorMode.IsOnlineMode && SaveSystem.Instance?.Backend is SqlSaveBackend backend)
                names.AddRange((await backend.GetImmortalPlayers()).Select(i => i.DivineName));
            else
            {
                var player = GameEngine.Instance?.CurrentPlayer;
                if (player != null && player.IsImmortal && !string.IsNullOrEmpty(player.DivineName))
                    names.Add(player.DivineName);
            }
        }
        catch (Exception ex) { DebugLogger.Instance.LogWarning("FAITH", $"Player-god list unavailable: {ex.Message}"); }
        return AllGods(names);
    }

    /// <summary>The god the character worships, or null. A canon choice wins over a player-god one.</summary>
    public static WorshippedGod? GetWorshippedGod(Character c, GodSystem? gods = null)
    {
        if (c == null) return null;
        string key = KeyOf(c);
        if (!string.IsNullOrEmpty(key))
        {
            string canon = Gods(gods).GetPlayerGod(key);
            if (!string.IsNullOrEmpty(canon) && !IsManwe(canon))
                return new WorshippedGod(CanonName(canon) ?? canon, IsCanon(canon));
        }
        string playerGod = c.WorshippedGod ?? "";
        if (!string.IsNullOrWhiteSpace(playerGod) && !IsManwe(playerGod))
            return new WorshippedGod(playerGod, false);
        return null;
    }

    /// <summary>
    /// Worship a god from the unified list, or none (null or blank). A canon god goes to the
    /// GodSystem store and clears the player-god; a player-god clears the canon entry. A different
    /// god starts at Favor 0. Manwe is refused. Returns false only when refused.
    /// </summary>
    public static bool SetWorshippedGod(Character c, string? name, GodSystem? gods = null)
    {
        if (c == null || IsManwe(name)) return false;
        var godSystem = Gods(gods);
        string key = KeyOf(c);
        if (string.IsNullOrWhiteSpace(name))
        {
            if (!string.IsNullOrEmpty(key)) godSystem.SetPlayerGod(key, "");
            c.WorshippedGod = "";
        }
        else if (IsCanon(name))
        {
            if (!string.IsNullOrEmpty(key)) godSystem.SetPlayerGod(key, CanonName(name)!);
            c.WorshippedGod = "";
        }
        else
        {
            if (!string.IsNullOrEmpty(key) && !string.IsNullOrEmpty(godSystem.GetPlayerGod(key)))
                godSystem.SetPlayerGod(key, "");
            c.WorshippedGod = name.Trim();
        }
        FavorSystem.Bind(c, godSystem);
        return true;
    }

    /// <summary>
    /// One god only: a Manwe entry is cleared, and when both stores hold a god the canon one wins
    /// and the player-god is cleared (the rule the load has always applied).
    /// </summary>
    public static void EnforceSingleGod(Character c, GodSystem? gods = null)
    {
        if (c == null) return;
        var godSystem = Gods(gods);
        string key = KeyOf(c);
        string canon = string.IsNullOrEmpty(key) ? "" : godSystem.GetPlayerGod(key);
        if (IsManwe(canon))
        {
            DebugLogger.Instance.LogWarning("WORSHIP", $"Cleaned up invalid Manwe worship for {key}");
            godSystem.SetPlayerGod(key, "");
            canon = "";
        }
        if (IsManwe(c.WorshippedGod)) c.WorshippedGod = "";
        if (!string.IsNullOrEmpty(canon) && !string.IsNullOrEmpty(c.WorshippedGod))
        {
            DebugLogger.Instance.LogWarning("WORSHIP", $"Dual worship detected for {key}: canon god '{canon}' + player-god '{c.WorshippedGod}'. Clearing the player-god.");
            c.WorshippedGod = "";
        }
    }

    /// <summary>
    /// Load step, run after the save's worship entry is back in GodSystem (RestoreStorySystems).
    /// Applies EnforceSingleGod, then the Favor schema guard: a save from before Favor starts at
    /// GodFavorLegacyStart with its current god, or 0 with none; then binds Favor to the god.
    /// </summary>
    public static void ApplyLoad(Character c, GodSystem? gods = null)
    {
        if (c == null || c.IsNPC) return;
        var godSystem = Gods(gods);
        EnforceSingleGod(c, godSystem);
        if (c.GodFavorSchema < GameConfig.GodFavorSchemaCurrent)
        {
            var god = GetWorshippedGod(c, godSystem);
            c.GodFavor = god != null ? GameConfig.GodFavorLegacyStart : 0;
            c.GodFavorGod = god?.Name ?? "";
            c.DaysSinceDevotion = 0;
            c.GodFavorDayGains.Clear();
            c.GodFavorSchema = GameConfig.GodFavorSchemaCurrent;
        }
        FavorSystem.Bind(c, godSystem);
    }

    /// <summary>The worship dictionary key for a character (as GameEngine.GodRestoreFilterFor).</summary>
    private static string KeyOf(Character c) =>
        !string.IsNullOrEmpty(c.Name2) ? c.Name2 : (c.Name1 ?? "");

    /// <summary>Sum of Favor and follower count per god, from (god, Favor) entries. Blank gods and Manwe are skipped.</summary>
    public static Dictionary<string, GodStanding> ComputeStandings(IEnumerable<(string God, int Favor)> entries)
    {
        var result = new Dictionary<string, GodStanding>(StringComparer.OrdinalIgnoreCase);
        foreach (var (god, favor) in entries ?? Enumerable.Empty<(string, int)>())
        {
            if (string.IsNullOrWhiteSpace(god) || IsManwe(god)) continue;
            string name = CanonName(god) ?? god.Trim();
            result.TryGetValue(name, out var s);
            result[name] = new GodStanding(name, s.Standing + Math.Clamp(favor, GameConfig.GodFavorMin, GameConfig.GodFavorMax), s.Followers + 1);
        }
        return result;
    }

    /// <summary>
    /// A save's (god, Favor) entry for god standing, read from the saved data alone: the canon god
    /// from the worship dictionary under the character's key, else the player-god. A save from
    /// before Favor counts GodFavorLegacyStart, as its first load will give it. ("", 0) with no god.
    /// </summary>
    public static (string God, int Favor) StandingEntryFrom(PlayerData? p, Dictionary<string, string>? playerGods)
    {
        if (p == null) return ("", 0);
        string key = !string.IsNullOrEmpty(p.Name2) ? p.Name2 : (p.Name1 ?? "");
        string canon = "";
        if (playerGods != null && !string.IsNullOrEmpty(key))
            canon = playerGods.FirstOrDefault(kv => kv.Key.Equals(key, StringComparison.OrdinalIgnoreCase)).Value ?? "";
        string god = !string.IsNullOrWhiteSpace(canon) && !IsManwe(canon) ? (CanonName(canon) ?? canon.Trim())
            : !string.IsNullOrWhiteSpace(p.WorshippedGod) && !IsManwe(p.WorshippedGod) ? p.WorshippedGod.Trim() : "";
        if (god.Length == 0) return ("", 0);
        if (p.GodFavorSchema < GameConfig.GodFavorSchemaCurrent) return (god, GameConfig.GodFavorLegacyStart);
        int favor = god.Equals(p.GodFavorGod ?? "", StringComparison.OrdinalIgnoreCase)
            ? Math.Clamp(p.GodFavor, GameConfig.GodFavorMin, GameConfig.GodFavorMax) : 0;
        return (god, favor);
    }

    /// <summary>
    /// Every god's standing. Online: the saved rows of every character (SqlSaveBackend.GetGodStandings).
    /// Single-player: the current character (NPC worshippers join in a later piece).
    /// </summary>
    public static async Task<Dictionary<string, GodStanding>> GetStandingsAsync(Character? current)
    {
        if (UsurperRemake.BBS.DoorMode.IsOnlineMode && SaveSystem.Instance?.Backend is SqlSaveBackend backend)
            return await Task.Run(() => backend.GetGodStandings());
        return SinglePlayerStandings(current);
    }

    /// <summary>Single-player standing: the character's own god and Favor.</summary>
    public static Dictionary<string, GodStanding> SinglePlayerStandings(Character? c, GodSystem? gods = null)
    {
        var god = c == null ? null : GetWorshippedGod(c, gods);
        var entries = god == null ? Array.Empty<(string, int)>() : new[] { (god.Value.Name, FavorSystem.GetFavor(c!, gods)) };
        return ComputeStandings(entries);
    }

    /// <summary>Living NPCs following a god (today only player-gods have NPC followers).</summary>
    public static int CountNpcFollowers(string god)
    {
        if (string.IsNullOrWhiteSpace(god)) return 0;
        return NPCSpawnSystem.Instance?.ActiveNPCs?
            .Count(n => !n.IsDead && string.Equals(n.WorshippedGod, god, StringComparison.OrdinalIgnoreCase)) ?? 0;
    }
}

/// <summary>
/// 1.2.0 Temple gods piece 1: Favor 0..100 with the god a character worships. Favor is stored with
/// the name of the god it belongs to (Character.GodFavorGod), so a god switch through any path
/// starts the new god at 0. NPCs are skipped.
/// </summary>
public static class FavorSystem
{
    /// <summary>The tier for a Favor value (values outside 0..100 are clamped first).</summary>
    public static GodFavorTier GetTier(int favor)
    {
        int f = Math.Clamp(favor, GameConfig.GodFavorMin, GameConfig.GodFavorMax);
        if (f >= GameConfig.GodFavorTierChosenMin) return GodFavorTier.Chosen;
        if (f >= GameConfig.GodFavorTierZealotMin) return GodFavorTier.Zealot;
        if (f >= GameConfig.GodFavorTierDevoutMin) return GodFavorTier.Devout;
        return GodFavorTier.Follower;
    }

    /// <summary>Favor with the character's current god: 0 with no god, or when the stored Favor belongs to another god.</summary>
    public static int GetFavor(Character c, GodSystem? gods = null)
    {
        if (c == null) return 0;
        var god = GodRegistry.GetWorshippedGod(c, gods);
        if (god == null || !god.Value.Name.Equals(c.GodFavorGod ?? "", StringComparison.OrdinalIgnoreCase)) return 0;
        return Math.Clamp(c.GodFavor, GameConfig.GodFavorMin, GameConfig.GodFavorMax);
    }

    /// <summary>
    /// Ties the stored Favor to the current god: with no god, Favor 0 and no god; with a god other
    /// than the stored one, Favor 0 with the new god and the devotion count restarted.
    /// </summary>
    public static void Bind(Character c, GodSystem? gods = null)
    {
        if (c == null || c.IsNPC) return;
        var god = GodRegistry.GetWorshippedGod(c, gods);
        if (god == null)
        {
            c.GodFavor = 0;
            c.GodFavorGod = "";
            return;
        }
        if (!god.Value.Name.Equals(c.GodFavorGod ?? "", StringComparison.OrdinalIgnoreCase))
        {
            c.GodFavor = 0;
            c.GodFavorGod = god.Value.Name;
            c.DaysSinceDevotion = 0;
        }
        c.GodFavor = Math.Clamp(c.GodFavor, GameConfig.GodFavorMin, GameConfig.GodFavorMax);
    }

    /// <summary>Changes Favor with the current god by delta, clamped to 0..100. NPCs and characters with no god get 0. Returns the change applied.</summary>
    public static int Change(Character c, int delta, GodSystem? gods = null)
    {
        if (c == null || c.IsNPC) return 0;
        Bind(c, gods);
        if (string.IsNullOrEmpty(c.GodFavorGod)) return 0;
        int before = c.GodFavor;
        int after = (int)Math.Clamp((long)before + delta, GameConfig.GodFavorMin, GameConfig.GodFavorMax);
        c.GodFavor = after;
        return after - before;
    }

    /// <summary>Favor gained today from a source.</summary>
    public static int GainedToday(Character c, FavorSource source) =>
        c?.GodFavorDayGains != null && c.GodFavorDayGains.TryGetValue(source.ToString(), out int v) ? v : 0;

    /// <summary>
    /// Gains up to amount from a source, never past dailyCap for that source today, and counts it
    /// toward today's total. Returns the Favor actually gained (0 for NPCs, no god, or a spent cap).
    /// </summary>
    public static int GainCapped(Character c, FavorSource source, int amount, int dailyCap, GodSystem? gods = null)
    {
        if (c == null || c.IsNPC || amount <= 0) return 0;
        int room = dailyCap - GainedToday(c, source);
        if (room <= 0) return 0;
        int applied = Change(c, Math.Min(amount, room), gods);
        if (applied > 0)
            c.GodFavorDayGains[source.ToString()] = GainedToday(c, source) + applied;
        return applied;
    }

    /// <summary>A devotion today (prayer, later a fitting deed): the neglect count starts over.</summary>
    public static void MarkDevotion(Character c)
    {
        if (c == null || c.IsNPC) return;
        c.DaysSinceDevotion = 0;
    }

    /// <summary>The neglect loss for a daily reset at this many days without devotion.</summary>
    public static int NeglectLoss(int daysSinceDevotion) =>
        daysSinceDevotion > GameConfig.GodNeglectGraceDays ? GameConfig.GodNeglectDailyLoss : 0;

    /// <summary>
    /// Daily reset: clears today's per-source gains, counts one more day without devotion, and past
    /// GodNeglectGraceDays takes GodNeglectDailyLoss Favor. NPCs are skipped; a character with no
    /// god only has the counters cleared. Returns the Favor change applied. Called once per day from
    /// DailySystemManager.RunBasicDailyReset, after MentalSystem.ApplyDailyReset.
    /// </summary>
    public static int ApplyDailyReset(Character c, GodSystem? gods = null)
    {
        if (c == null || c.IsNPC) return 0;
        c.GodFavorDayGains.Clear();
        Bind(c, gods);
        if (string.IsNullOrEmpty(c.GodFavorGod)) return 0;
        if (c.DaysSinceDevotion < int.MaxValue) c.DaysSinceDevotion++;
        int loss = NeglectLoss(c.DaysSinceDevotion);
        return loss > 0 ? Change(c, -loss, gods) : 0;
    }
}
