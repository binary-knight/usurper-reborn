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

/// <summary>Temple gods piece 3: how an item sacrifice went (FavorSystem.SacrificeEquipped).</summary>
public enum ItemSacrificeOutcome
{
    Done,
    NoItem,
    Refused,
}

/// <summary>The god a character worships: a canon god or an ascended player-god.</summary>
public readonly record struct WorshippedGod(string Name, bool IsCanon);

/// <summary>One god in the unified list (canon ten plus ascended player-gods).</summary>
public readonly record struct GodListEntry(string Name, bool IsCanon);

/// <summary>
/// A god's standing: the sum of its followers' Favor, and how many followers that is. Followers
/// counts characters (players); NpcFollowers counts the living NPCs following the god, each adding
/// GameConfig.GodNpcFollowerStanding to Standing (piece 6).
/// </summary>
public readonly record struct GodStanding(string God, long Standing, int Followers)
{
    /// <summary>Living NPC followers counted in Standing.</summary>
    public int NpcFollowers { get; init; }

    /// <summary>Every follower: characters and NPCs.</summary>
    public int AllFollowers => Followers + NpcFollowers;
}

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
    /// god starts at Favor 0. Manwe is refused. Returns false only when refused. The boons' max HP
    /// and mana follow at once; for a character played in another session (otherSession) its own
    /// session applies them at its next safe point (GodBoonSystem.RequestRecalcForBoon).
    /// </summary>
    public static bool SetWorshippedGod(Character c, string? name, GodSystem? gods = null, bool otherSession = false)
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
        if (otherSession) GodBoonSystem.RequestRecalcForBoon(c);   // 1.2.0: its own session updates it
        else GodBoonSystem.RecalculateForBoon(c, gods);   // 1.2.0: a god boon on max HP follows the new god
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
    /// Adds NPC followers to standings: each god's count times GodNpcFollowerStanding joins its
    /// Standing and the count its NpcFollowers; a god with NPC followers only gets an entry. Blank
    /// gods, Manwe and counts below 1 are skipped. Applied before the desecration penalties.
    /// </summary>
    public static Dictionary<string, GodStanding> AddNpcFollowers(Dictionary<string, GodStanding> standings, IEnumerable<KeyValuePair<string, int>>? npcCounts)
    {
        standings ??= new Dictionary<string, GodStanding>(StringComparer.OrdinalIgnoreCase);
        foreach (var (god, count) in npcCounts ?? Enumerable.Empty<KeyValuePair<string, int>>())
        {
            if (count <= 0 || string.IsNullOrWhiteSpace(god) || IsManwe(god)) continue;
            string name = CanonName(god) ?? god.Trim();
            standings.TryGetValue(name, out var s);
            standings[name] = new GodStanding(name, s.Standing + (long)count * GameConfig.GodNpcFollowerStanding, s.Followers)
            {
                NpcFollowers = s.NpcFollowers + count
            };
        }
        return standings;
    }

    /// <summary>
    /// Every god's standing. Online: the saved rows of every character and the world's NPCs
    /// (SqlSaveBackend.GetGodStandings). Single-player: the current character and the NPC roster.
    /// </summary>
    public static async Task<Dictionary<string, GodStanding>> GetStandingsAsync(Character? current)
    {
        if (UsurperRemake.BBS.DoorMode.IsOnlineMode && SaveSystem.Instance?.Backend is SqlSaveBackend backend)
            return await Task.Run(() => backend.GetGodStandings());
        return SinglePlayerStandings(current);
    }

    /// <summary>
    /// Single-player standing: the character's own god and Favor, plus the NPC followers of the
    /// roster (npcs; null reads NPCSpawnSystem.ActiveNPCs), less this week's desecration penalties
    /// kept in the save (GodStandingPenalty).
    /// </summary>
    public static Dictionary<string, GodStanding> SinglePlayerStandings(Character? c, GodSystem? gods = null, IEnumerable<NPC>? npcs = null) =>
        SinglePlayerStandings(c, GodStandingPenalty.CurrentWeek(), gods, npcs);

    /// <summary>SinglePlayerStandings with the desecration penalties of a given week.</summary>
    public static Dictionary<string, GodStanding> SinglePlayerStandings(Character? c, int penaltyWeek, GodSystem? gods, IEnumerable<NPC>? npcs)
    {
        var god = c == null ? null : GetWorshippedGod(c, gods);
        var entries = god == null ? Array.Empty<(string, int)>() : new[] { (god.Value.Name, FavorSystem.GetFavor(c!, gods)) };
        var roster = npcs ?? (IEnumerable<NPC>?)NPCSpawnSystem.Instance?.ActiveNPCs ?? Enumerable.Empty<NPC>();
        var standings = AddNpcFollowers(ComputeStandings(entries), NpcFaithSystem.CountFollowers(roster));
        return GodStandingPenalty.Apply(standings, GodStandingPenalty.LocalPenalties(c, penaltyWeek));
    }

    /// <summary>Living NPCs of the roster following a god (canon gods and player-gods).</summary>
    public static int CountNpcFollowers(string god)
    {
        if (string.IsNullOrWhiteSpace(god)) return 0;
        return NPCSpawnSystem.Instance?.ActiveNPCs?
            .Count(n => !n.IsDead && string.Equals(NpcFaithSystem.GodOf(n), god.Trim(), StringComparison.OrdinalIgnoreCase)) ?? 0;
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

    /// <summary>
    /// Changes Favor with the current god by delta, clamped to 0..100. NPCs and characters with no
    /// god get 0. A change that crosses a tier recalculates the stats (a boon's strength follows the
    /// tier). With deferBoonRecalc (a character played in another session, such as a grouped
    /// follower in the leader's fight) the recalculation is left to that character's own session
    /// (GodBoonSystem.RequestRecalcForBoon). Returns the change applied.
    /// </summary>
    public static int Change(Character c, int delta, GodSystem? gods = null, bool deferBoonRecalc = false)
    {
        if (c == null || c.IsNPC) return 0;
        var tierBefore = GetTier(GetFavor(c, gods));
        Bind(c, gods);
        if (string.IsNullOrEmpty(c.GodFavorGod)) return 0;
        int before = c.GodFavor;
        int after = (int)Math.Clamp((long)before + delta, GameConfig.GodFavorMin, GameConfig.GodFavorMax);
        c.GodFavor = after;
        if (GetTier(after) != tierBefore)
        {
            if (deferBoonRecalc) GodBoonSystem.RequestRecalcForBoon(c);
            else GodBoonSystem.RecalculateForBoon(c, gods);
        }
        return after - before;
    }

    /// <summary>Favor gained today from a source.</summary>
    public static int GainedToday(Character c, FavorSource source) =>
        c?.GodFavorDayGains != null && c.GodFavorDayGains.TryGetValue(source.ToString(), out int v) ? v : 0;

    /// <summary>
    /// Gains up to amount from a source, never past dailyCap for that source today, and counts it
    /// toward today's total. Returns the Favor actually gained (0 for NPCs, no god, or a spent cap).
    /// deferBoonRecalc as in Change.
    /// </summary>
    public static int GainCapped(Character c, FavorSource source, int amount, int dailyCap, GodSystem? gods = null, bool deferBoonRecalc = false)
    {
        if (c == null || c.IsNPC || amount <= 0) return 0;
        int room = dailyCap - GainedToday(c, source);
        if (room <= 0) return 0;
        int applied = Change(c, Math.Min(amount, room), gods, deferBoonRecalc);
        if (applied > 0)
            c.GodFavorDayGains[source.ToString()] = GainedToday(c, source) + applied;
        return applied;
    }

    /// <summary>Temple gods piece 3: daily prayer, GodFavorPrayerGain once a day. Returns the Favor gained.</summary>
    public static int Prayer(Character c, GodSystem? gods = null) =>
        GainCapped(c, FavorSource.Prayer, GameConfig.GodFavorPrayerGain, GameConfig.GodFavorPrayerGain, gods);

    /// <summary>Favor a gold sacrifice is worth before the daily cap: +1 per (Level x GodFavorGoldPerLevel) gold.</summary>
    public static int GoldSacrificeFavor(long gold, int level)
    {
        if (gold <= 0) return 0;
        long unit = (long)Math.Max(1, level) * GameConfig.GodFavorGoldPerLevel;
        return (int)Math.Min(gold / unit, int.MaxValue);
    }

    /// <summary>A gold sacrifice to the character's own god (devotion): GoldSacrificeFavor, at most GodFavorGoldDailyCap a day. Returns the Favor gained.</summary>
    public static int GoldSacrifice(Character c, long gold, GodSystem? gods = null)
    {
        if (c == null) return 0;
        MarkDevotion(c);
        return GainCapped(c, FavorSource.GoldSacrifice, GoldSacrificeFavor(gold, c.Level), GameConfig.GodFavorGoldDailyCap, gods);
    }

    /// <summary>
    /// Favor an item sacrifice is worth before the daily cap: the item's resale value (half its
    /// price) at the gold sacrifice rate, from GodFavorItemMin to GodFavorItemMax. 0 for a worthless item.
    /// </summary>
    public static int ItemSacrificeFavor(long value, int level)
    {
        if (value <= 0) return 0;
        return Math.Clamp(GoldSacrificeFavor(value / 2, level), GameConfig.GodFavorItemMin, GameConfig.GodFavorItemMax);
    }

    /// <summary>An item sacrifice to the character's own god (devotion): ItemSacrificeFavor, at most GodFavorItemDailyCap a day. Returns the Favor gained.</summary>
    public static int ItemSacrifice(Character c, long value, GodSystem? gods = null)
    {
        if (c == null) return 0;
        MarkDevotion(c);
        return GainCapped(c, FavorSource.ItemSacrifice, ItemSacrificeFavor(value, c.Level), GameConfig.GodFavorItemDailyCap, gods);
    }

    /// <summary>
    /// Temple gods piece 3: offers the item worn in a slot to the character's god. The item is
    /// unequipped and dropped (nothing keeps it: the save writes only equipped and carried items),
    /// the stats are recalculated without it, and ItemSacrifice Favor is gained; the offering is
    /// devotion. A cursed or unique item is refused and stays worn. Returns the outcome, the item
    /// (null when the slot is empty) and the Favor gained.
    /// </summary>
    public static (ItemSacrificeOutcome Outcome, Equipment? Item, int Favor) SacrificeEquipped(Character c, EquipmentSlot slot, GodSystem? gods = null)
    {
        var item = c?.GetEquipment(slot);
        if (c == null || item == null) return (ItemSacrificeOutcome.NoItem, null, 0);
        if (item.IsCursed || item.IsUnique) return (ItemSacrificeOutcome.Refused, item, 0);
        if (c.UnequipSlot(slot) == null) return (ItemSacrificeOutcome.Refused, item, 0);
        c.RecalculateStats();
        int favor = ItemSacrifice(c, item.Value, gods);
        return (ItemSacrificeOutcome.Done, item, favor);
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

/// <summary>Temple gods piece 4: who changed a character's god.</summary>
public enum GodChangeBy
{
    /// <summary>The worshipper chose it at the Temple (W, J, L): leaving costs Favor and brings wrath.</summary>
    Player,
    /// <summary>The game or another player did it (a god gone, a player-god's recruit): Favor goes with the god, no wrath.</summary>
    Other,
}

/// <summary>
/// Temple gods piece 4: what leaving the current god costs. FavorLost is all Favor with it.
/// WrathLevel is the DivineWrath level a canon god records (0 none); SmiteDamage is the HP a
/// player-god strikes at once (0 none). OldGod is "" when there is no god to leave.
/// </summary>
public readonly record struct GodSwitchCost(string OldGod, bool OldIsCanon, int FavorLost, int WrathLevel, long SmiteDamage)
{
    public bool LeavesAGod => OldGod.Length > 0;
}

/// <summary>
/// Temple gods piece 4: the one switching rule. Every change of the god a character worships goes
/// through Switch (a source test holds the game to it). Leaving a god costs all Favor with it and
/// the new god starts at 0 (FavorSystem.Bind); when the worshipper chose it (GodChangeBy.Player)
/// the left god's wrath follows at once, by the Favor lost: a canon god records Divine Wrath
/// (Character.RecordDivineWrath, delivered in the dungeon), a player-god strikes with its smite.
/// A change made by the game or another player (GodChangeBy.Other) brings no wrath. The wrath is
/// applied once, in memory, with the switch; the Temple saves the character at once after it, so
/// the god, the Favor, the wrath and LastGodSwitchDay reach the save together.
/// </summary>
public static class GodSwitchSystem
{
    /// <summary>The DivineWrath level for the Favor lost: 0 none, 1 below Devout, 2 below Zealot, 3 from Zealot.</summary>
    public static int WrathLevelFor(int favorLost)
    {
        if (favorLost <= 0) return 0;
        if (favorLost >= GameConfig.GodFavorTierZealotMin) return 3;
        if (favorLost >= GameConfig.GodFavorTierDevoutMin) return 2;
        return 1;
    }

    /// <summary>The share of max HP a left player-god smites for the Favor lost: 0 at none, the smite range scaled by Favor lost / 100.</summary>
    public static float SmitePercentFor(int favorLost)
    {
        if (favorLost <= 0) return 0f;
        float scale = Math.Clamp(favorLost, GameConfig.GodFavorMin, GameConfig.GodFavorMax) / (float)GameConfig.GodFavorMax;
        return GameConfig.GodSmiteMinPercent + (GameConfig.GodSmiteMaxPercent - GameConfig.GodSmiteMinPercent) * scale;
    }

    /// <summary>The smite damage for the Favor lost against a max HP (at least 1 when there is a smite).</summary>
    public static long SmiteDamageFor(int favorLost, long maxHp)
    {
        float pct = SmitePercentFor(favorLost);
        return pct <= 0f ? 0 : Math.Max(1, (long)(Math.Max(0, maxHp) * pct));
    }

    /// <summary>
    /// What switching to newGod (null or blank for none) would cost, read-only. Nothing when there
    /// is no god to leave or newGod is the current god.
    /// </summary>
    public static GodSwitchCost Preview(Character c, string? newGod, GodSystem? gods = null)
    {
        var old = c == null ? null : GodRegistry.GetWorshippedGod(c, gods);
        if (c == null || old == null) return new GodSwitchCost("", false, 0, 0, 0);
        string name = old.Value.Name;
        if (!string.IsNullOrWhiteSpace(newGod) && name.Equals(newGod.Trim(), StringComparison.OrdinalIgnoreCase))
            return new GodSwitchCost(name, old.Value.IsCanon, 0, 0, 0);
        int lost = FavorSystem.GetFavor(c, gods);
        return old.Value.IsCanon
            ? new GodSwitchCost(name, true, lost, WrathLevelFor(lost), 0)
            : new GodSwitchCost(name, false, lost, 0, SmiteDamageFor(lost, c.MaxHP));
    }

    /// <summary>
    /// Switches the character to newGod (null or blank for none) through GodRegistry.SetWorshippedGod.
    /// By the player: the Preview cost is applied (the wrath recorded or the smite struck, HP never
    /// below 1) and LastGodSwitchDay is set to today. By another: Favor goes with the god, nothing
    /// else. otherSession as in SetWorshippedGod. Returns the cost applied, or null when refused (Manwe).
    /// </summary>
    public static GodSwitchCost? Switch(Character c, string? newGod, GodChangeBy by, GodSystem? gods = null, bool otherSession = false)
    {
        if (c == null) return null;
        var cost = Preview(c, newGod, gods);
        if (!GodRegistry.SetWorshippedGod(c, newGod, gods, otherSession)) return null;
        if (by != GodChangeBy.Player) return cost with { WrathLevel = 0, SmiteDamage = 0 };
        bool left = cost.LeavesAGod && !cost.OldGod.Equals(newGod?.Trim() ?? "", StringComparison.OrdinalIgnoreCase);
        if (!left) return cost;
        c.LastGodSwitchDay = Today();
        if (cost.WrathLevel > 0)
            c.RecordDivineWrath(cost.OldGod, string.IsNullOrWhiteSpace(newGod) ? "" : newGod.Trim(), cost.WrathLevel);
        if (cost.SmiteDamage > 0)
            c.HP = Math.Max(1, c.HP - cost.SmiteDamage);
        return cost;
    }

    /// <summary>Today's game day (DailySystemManager).</summary>
    public static int Today() => DailySystemManager.Instance.CurrentDay;

    /// <summary>
    /// Temple W: a god chosen right after leaving one in the same run. The leave recorded its wrath
    /// with no god it was for; this names the chosen god in BetrayedForGodName. The wrath level is
    /// not changed. Nothing when the leave recorded no wrath, the recorded wrath is from another god,
    /// it already names a god, or the chosen god is the angered one.
    /// </summary>
    public static void NameBetrayedFor(Character c, GodSwitchCost? leave, string? chosenGod)
    {
        if (c == null || leave is not { } l || l.WrathLevel <= 0 || string.IsNullOrWhiteSpace(chosenGod)) return;
        string chosen = chosenGod.Trim();
        if (string.IsNullOrEmpty(c.AngeredGodName) || !string.IsNullOrEmpty(c.BetrayedForGodName)) return;
        if (!c.AngeredGodName.Equals(l.OldGod, StringComparison.OrdinalIgnoreCase)) return;
        if (c.AngeredGodName.Equals(chosen, StringComparison.OrdinalIgnoreCase)) return;
        c.BetrayedForGodName = chosen;
    }
}

/// <summary>
/// Temple gods piece 4: a desecrated altar lowers that god's standing by
/// GodDesecrationStandingPenalty until the next weekly reset (CurrentWeek: online the world week,
/// DailySystemManager.WorldWeek, the same for every session; single-player game day /
/// GodStandingWeekDays). The penalty is stored, not a change to any
/// follower's Favor, and it is applied where the standing is read: online in SQL
/// (SqlSaveBackend.AddGodStandingPenalty, read inside GetGodStandings), single-player in the save
/// (Character.GodStandingPenalties). A penalty from an earlier week no longer counts. Standing
/// never goes below 0.
/// </summary>
public static class GodStandingPenalty
{
    /// <summary>The week a game day belongs to.</summary>
    public static int WeekOf(int day) => Math.Max(0, day) / GameConfig.GodStandingWeekDays;

    /// <summary>This week: online the world week, single-player the game day's week.</summary>
    public static int CurrentWeek() =>
        CurrentWeek(UsurperRemake.BBS.DoorMode.IsOnlineMode, DailySystemManager.Instance.CurrentDay, DateTime.UtcNow);

    /// <summary>
    /// The week for a mode, game day and time. Online it is DailySystemManager.WorldWeekAt(utcNow),
    /// because online the game day is whichever save loaded last and differs between sessions.
    /// Single-player it is WeekOf(gameDay), the one clock there is.
    /// </summary>
    public static int CurrentWeek(bool online, int gameDay, DateTime utcNow) =>
        online ? DailySystemManager.WorldWeekAt(utcNow) : WeekOf(gameDay);

    /// <summary>Standings less the penalties (any letter case), never below 0. Gods without standing stay out.</summary>
    public static Dictionary<string, GodStanding> Apply(Dictionary<string, GodStanding> standings, IReadOnlyDictionary<string, int>? penalties)
    {
        if (standings == null) return new Dictionary<string, GodStanding>(StringComparer.OrdinalIgnoreCase);
        if (penalties == null || penalties.Count == 0) return standings;
        foreach (var (god, points) in penalties)
        {
            if (points <= 0 || string.IsNullOrWhiteSpace(god)) continue;
            string name = GodRegistry.CanonName(god) ?? god.Trim();
            if (standings.TryGetValue(name, out var s))
                standings[name] = s with { Standing = Math.Max(0, s.Standing - points) };
        }
        return standings;
    }

    /// <summary>The single-player penalties for a week (empty when they belong to another week).</summary>
    public static IReadOnlyDictionary<string, int> LocalPenalties(Character? c, int week)
    {
        if (c?.GodStandingPenalties == null || c.GodStandingPenaltyWeek != week)
            return new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
        return new Dictionary<string, int>(c.GodStandingPenalties, StringComparer.OrdinalIgnoreCase);
    }

    /// <summary>Single-player: adds a penalty to a god for a week; penalties of an earlier week are dropped first.</summary>
    public static void AddLocal(Character c, string god, int week, int points = GameConfig.GodDesecrationStandingPenalty)
    {
        if (c == null || string.IsNullOrWhiteSpace(god) || points <= 0) return;
        if (c.GodStandingPenaltyWeek != week || c.GodStandingPenalties == null)
        {
            c.GodStandingPenalties = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
            c.GodStandingPenaltyWeek = week;
        }
        string name = GodRegistry.CanonName(god) ?? god.Trim();
        string key = c.GodStandingPenalties.Keys.FirstOrDefault(k => k.Equals(name, StringComparison.OrdinalIgnoreCase)) ?? name;
        c.GodStandingPenalties[key] = (c.GodStandingPenalties.TryGetValue(key, out int v) ? v : 0) + points;
    }

    /// <summary>An altar of god desecrated by c: the penalty for this week, online in SQL, else in c's save.</summary>
    public static void RecordDesecration(Character c, string god)
    {
        if (c == null || string.IsNullOrWhiteSpace(god)) return;
        int week = CurrentWeek();
        if (UsurperRemake.BBS.DoorMode.IsOnlineMode && SaveSystem.Instance?.Backend is SqlSaveBackend backend)
            backend.AddGodStandingPenalty(GodRegistry.CanonName(god) ?? god.Trim(), week, GameConfig.GodDesecrationStandingPenalty);
        else
            AddLocal(c, god, week);
    }
}
