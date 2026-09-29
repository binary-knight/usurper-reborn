using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;

namespace UsurperRemake.Systems;

/// <summary>The side a god or a character stands on: good and evil oppose each other, neutral opposes none.</summary>
public enum FaithSide
{
    Neutral,
    Good,
    Evil,
}

/// <summary>What an NPC's faith meant for a first talk of the day with the player.</summary>
public enum FaithMeetingKind
{
    None,
    Shared,
    Opposed,
}

/// <summary>
/// The faith part of a talk with an NPC: Kind (shared god, opposed gods, or neither), the NPC's
/// god, the player's god, whether the player is Chosen (the mark draws a remark), and Applied, true
/// only on the first talk with that NPC this game day (the relationship change and the lines).
/// </summary>
public readonly record struct FaithMeeting(FaithMeetingKind Kind, string NpcGod, string PlayerGod, bool PlayerChosen, bool Applied);

/// <summary>
/// 1.2.0 Temple gods piece 6: NPC townsfolk worship a god from the unified list. The god is the
/// NPC's WorshippedGod, the field the NPC save path already carries (the same field holds a
/// player-god a follower was recruited to, so there is one NPC faith field). An NPC entering the
/// roster with no god is given a canon god (EnsureAssigned, from NPCSpawnSystem.AddRestoredNPC and
/// InitializeClassicNPCs); the pick is deterministic from the NPC's name, so it is the same on every
/// load and in every process, and a god the NPC already has is never changed. Player-gods gain NPC
/// followers by recruiting them (the Pantheon), not from this pick.
/// </summary>
public static class NpcFaithSystem
{
    /// <summary>The side of each canon god, from its goodness and darkness in GodSystem.InitializeDefaultPantheon.</summary>
    private static readonly Dictionary<string, FaithSide> CanonSides = new(StringComparer.OrdinalIgnoreCase)
    {
        ["Solarius"] = FaithSide.Good,
        ["Valorian"] = FaithSide.Good,
        ["Amara"] = FaithSide.Good,
        ["Judicar"] = FaithSide.Good,
        ["Umbrath"] = FaithSide.Evil,
        ["Terran"] = FaithSide.Good,
        ["Mortis"] = FaithSide.Evil,
        ["Arcanus"] = FaithSide.Neutral,
        ["Sylvana"] = FaithSide.Good,
        ["Discordia"] = FaithSide.Evil,
    };

    /// <summary>The canon god a class leans to (added NpcFaithClassWeight), or none.</summary>
    public static string ClassGod(CharacterClass cls) => cls switch
    {
        CharacterClass.Warrior or CharacterClass.Barbarian => "Valorian",
        CharacterClass.Paladin or CharacterClass.Cleric or CharacterClass.Tidesworn => "Solarius",
        CharacterClass.Bard or CharacterClass.Wavecaller => "Amara",
        CharacterClass.Magician or CharacterClass.Sage or CharacterClass.Alchemist or CharacterClass.Cyclebreaker => "Arcanus",
        CharacterClass.Assassin or CharacterClass.Abysswarden => "Umbrath",
        CharacterClass.Ranger => "Sylvana",
        CharacterClass.Jester or CharacterClass.Voidreaver => "Discordia",
        CharacterClass.MysticShaman => "Terran",
        _ => ""
    };

    /// <summary>The side of a canon god (Neutral for any other name).</summary>
    public static FaithSide CanonSide(string? god) =>
        !string.IsNullOrWhiteSpace(god) && CanonSides.TryGetValue(god.Trim(), out var side) ? side : FaithSide.Neutral;

    /// <summary>A character's side from its alignment: Good when Chivalry is higher, Evil when Darkness is, else Neutral.</summary>
    public static FaithSide SideOf(Character c)
    {
        if (c == null || c.Chivalry == c.Darkness) return FaithSide.Neutral;
        return c.Chivalry > c.Darkness ? FaithSide.Good : FaithSide.Evil;
    }

    /// <summary>True when the two sides oppose (good against evil).</summary>
    public static bool Opposed(FaithSide a, FaithSide b) =>
        (a == FaithSide.Good && b == FaithSide.Evil) || (a == FaithSide.Evil && b == FaithSide.Good);

    /// <summary>
    /// The pick weight of a canon god for an NPC. By alignment: a god of the NPC's own side
    /// NpcFaithAlignedWeight, an opposed god 0, any other NpcFaithNeutralWeight (a neutral NPC gives
    /// every god that, and the neutral god Arcanus NpcFaithAlignedWeight). The god of the NPC's
    /// class adds NpcFaithClassWeight, unless the god is opposed (weight 0 stays 0).
    /// </summary>
    public static int Weight(Character npc, string canonGod)
    {
        var npcSide = SideOf(npc);
        var godSide = CanonSide(canonGod);
        int w;
        if (Opposed(npcSide, godSide)) return 0;
        if (npcSide == godSide) w = GameConfig.NpcFaithAlignedWeight;
        else w = GameConfig.NpcFaithNeutralWeight;
        if (ClassGod(npc.Class).Equals(canonGod, StringComparison.OrdinalIgnoreCase))
            w += GameConfig.NpcFaithClassWeight;
        return w;
    }

    /// <summary>A hash of text that is the same in every process and run (FNV-1a over the lower-case text).</summary>
    public static uint StableHash(string? text)
    {
        uint h = 2166136261;
        foreach (char ch in (text ?? "").ToLowerInvariant())
        {
            h ^= ch;
            h *= 16777619;
        }
        return h;
    }

    /// <summary>
    /// The canon god an NPC is given: a weighted pick over the ten (Weight), the roll taken from
    /// StableHash of the NPC's name (its ID when nameless), so one NPC always gets the same god.
    /// </summary>
    public static string PickGod(Character npc)
    {
        if (npc == null) return "";
        var weights = GameConfig.CanonGodNames.Select(g => (God: g, W: Weight(npc, g))).ToList();
        int total = weights.Sum(x => x.W);
        if (total <= 0) return GameConfig.CanonGodNames[0];
        string seed = !string.IsNullOrWhiteSpace(npc.Name2) ? npc.Name2 : (npc is NPC n ? n.Id : npc.ID);
        int roll = (int)(StableHash(seed) % (uint)total);
        foreach (var (god, w) in weights)
        {
            if (roll < w) return god;
            roll -= w;
        }
        return weights.Last(x => x.W > 0).God;
    }

    /// <summary>
    /// True when an NPC follows a canon god that is a weak fit for it: the god's pick weight for the
    /// NPC's alignment and class (Weight) is below NpcFaithAlignedWeight, so it is neither a god of
    /// the NPC's own side nor the god of its class (a god now opposed after the NPC's alignment
    /// moved counts too, at weight 0). False with no god and for a follower of a player-god. A
    /// player-god recruits such an NPC as it recruits a pagan (PantheonLocation).
    /// </summary>
    public static bool IsLooselyDevout(Character npc)
    {
        if (npc == null) return false;
        string god = GodOf(npc);
        if (god.Length == 0 || !GodRegistry.IsCanon(god)) return false;
        return Weight(npc, god) < GameConfig.NpcFaithAlignedWeight;
    }

    /// <summary>
    /// An NPC with no god is given PickGod; an NPC with a god keeps it (a player-god follower stays
    /// one). Manwe is cleared first. Returns true when a god was given.
    /// </summary>
    public static bool EnsureAssigned(Character npc)
    {
        if (npc == null) return false;
        if (GodRegistry.IsManwe(npc.WorshippedGod)) npc.WorshippedGod = "";
        if (!string.IsNullOrWhiteSpace(npc.WorshippedGod)) return false;
        npc.WorshippedGod = PickGod(npc);
        return npc.WorshippedGod.Length > 0;
    }

    /// <summary>The god an NPC follows (canon spelling for a canon god), or "".</summary>
    public static string GodOf(Character npc)
    {
        string g = npc?.WorshippedGod ?? "";
        if (string.IsNullOrWhiteSpace(g) || GodRegistry.IsManwe(g)) return "";
        return GodRegistry.CanonName(g) ?? g.Trim();
    }

    /// <summary>
    /// Living NPC followers per god, from a roster (canon names in canon spelling). The standing a
    /// god gets from them is the count times GodNpcFollowerStanding (GodRegistry.AddNpcFollowers).
    /// </summary>
    public static Dictionary<string, int> CountFollowers(IEnumerable<NPC>? npcs)
    {
        var counts = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
        foreach (var n in npcs ?? Enumerable.Empty<NPC>())
        {
            if (n == null || n.IsDead) continue;
            string g = GodOf(n);
            if (g.Length == 0) continue;
            counts[g] = (counts.TryGetValue(g, out int v) ? v : 0) + 1;
        }
        return counts;
    }

    /// <summary>
    /// The side of the god a player worships: a canon god's own side, a player-god's by the canon god
    /// of its domain (Neutral before a domain is chosen). Neutral with no god.
    /// </summary>
    public static FaithSide PlayerGodSide(Character player, GodSystem? gods = null)
    {
        var god = player == null ? null : GodRegistry.GetWorshippedGod(player, gods);
        if (god == null) return FaithSide.Neutral;
        if (god.Value.IsCanon) return CanonSide(god.Value.Name);
        return CanonSide(GodBoonSystem.CanonGodOf(GodBoonSystem.GetDomain(player, gods)));
    }

    /// <summary>
    /// How an NPC's god meets the player's: Shared when they follow the same god, Opposed when the
    /// NPC's canon god and the player's god stand on opposed sides (an NPC following a player-god
    /// counts as neutral), else None.
    /// </summary>
    public static FaithMeetingKind MeetingKind(Character player, Character npc, GodSystem? gods = null)
    {
        var mine = player == null ? null : GodRegistry.GetWorshippedGod(player, gods);
        string theirs = GodOf(npc);
        if (mine == null || theirs.Length == 0) return FaithMeetingKind.None;
        if (mine.Value.Name.Equals(theirs, StringComparison.OrdinalIgnoreCase)) return FaithMeetingKind.Shared;
        return Opposed(PlayerGodSide(player!, gods), CanonSide(theirs)) ? FaithMeetingKind.Opposed : FaithMeetingKind.None;
    }

    /// <summary>The (player, NPC) pairs already met this game day. Run time only: a new session starts over.</summary>
    private static readonly ConcurrentDictionary<string, int> MetOnDay = new(StringComparer.OrdinalIgnoreCase);

    /// <summary>
    /// A talk with an NPC begins. On the first talk with that NPC this game day the faith effect is
    /// applied once, through RelationshipSystem.UpdateRelationship (the NPC's feeling for the player):
    /// a shared god NpcFaithSharedRelationSteps warmer (under the daily relationship gain cap), opposed
    /// gods NpcFaithOpposedRelationSteps cooler. Later talks that day return Applied false and change
    /// nothing.
    /// </summary>
    public static FaithMeeting OnTalk(Character player, Character npc, int day, GodSystem? gods = null)
    {
        if (player == null || npc == null || player.IsNPC)
            return new FaithMeeting(FaithMeetingKind.None, "", "", false, false);
        var kind = MeetingKind(player, npc, gods);
        string playerGod = GodRegistry.GetWorshippedGod(player, gods)?.Name ?? "";
        bool chosen = playerGod.Length > 0 && FavorSystem.GetTier(FavorSystem.GetFavor(player, gods)) == GodFavorTier.Chosen;
        string key = $"{player.Name2}|{(npc is NPC n ? n.Id : npc.ID)}|{npc.Name2}";
        bool first = !MetOnDay.TryGetValue(key, out int last) || last != day;
        if (first) MetOnDay[key] = day;
        if (first)
        {
            if (kind == FaithMeetingKind.Shared)
                RelationshipSystem.UpdateRelationship(npc, player, 1, GameConfig.NpcFaithSharedRelationSteps);
            else if (kind == FaithMeetingKind.Opposed)
                RelationshipSystem.UpdateRelationship(npc, player, -1, GameConfig.NpcFaithOpposedRelationSteps);
        }
        return new FaithMeeting(kind, GodOf(npc), playerGod, chosen, first);
    }

    /// <summary>
    /// The lines a first talk of the day shows (none on later talks): the shared god or the cool
    /// look, then the remark on a Chosen player's mark.
    /// </summary>
    public static List<string> Lines(FaithMeeting m, string npcName)
    {
        var lines = new List<string>();
        if (!m.Applied) return lines;
        if (m.Kind == FaithMeetingKind.Shared) lines.Add(Loc.Get("faith.npc_shared", npcName, m.NpcGod));
        else if (m.Kind == FaithMeetingKind.Opposed) lines.Add(Loc.Get("faith.npc_opposed", npcName, m.NpcGod, m.PlayerGod));
        if (m.PlayerChosen)
            lines.Add(Loc.Get(m.Kind == FaithMeetingKind.Shared ? "faith.npc_mark_shared" : "faith.npc_mark_other", npcName, m.PlayerGod));
        return lines;
    }

    /// <summary>Test seam: forgets the talks met today.</summary>
    internal static void ResetMeetingsForTests() => MetOnDay.Clear();
}
