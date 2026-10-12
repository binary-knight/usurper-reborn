using System;
using System.Collections.Generic;
using System.Linq;
using UsurperRemake.Systems;

/// <summary>
/// v1.2.8 (H3): the ward on hired NPCs and dungeon party members, behind GameConfig.WardEnabled. On screen it
/// is the oath-mark in every row and language ("ward" is the Sage's spell term); the code keeps the name Ward.
/// IsWarded is THE test; nothing else decides who is warded. The ward draws no random number anywhere:
/// the chance that a fall at the player's side is permanent is the roll that was there before 1.2.8.
/// </summary>
public static class WardSystem
{
    /// <summary>The number of last lines a warded NPC can speak when the ward breaks (oath_mark.last_1 to oath_mark.last_3).</summary>
    public const int LastLineCount = 3;

    /// <summary>
    /// THE test for "this character is warded". With the switch on:
    /// (1) an NPC that is a member of a player's team (WorldSimulator.IsOnPlayerTeam, the H2 test), anywhere;
    /// (2) an NPC in a player's dungeon party for the length of that run: the caller at the player's side
    ///     passes the fight's Teammates and GameEngine.DungeonPartyNPCIds, and the NPC is matched by ID.
    /// A caller away from the player (the world simulator) passes no party, so a party-only member (a spouse,
    /// a lover or a friend who came along without being on the team) gets nothing there.
    /// A story companion is never warded: in a fight it is a plain Character with IsCompanion and a
    /// CompanionId (CompanionSystem builds it), not an NPC, and it is refused here by type and by both
    /// marks. Echoes of players, royal mercenaries and grouped players are refused the same way.
    /// </summary>
    public static bool IsWarded(Character? who, IEnumerable<Character>? party = null, IEnumerable<string>? partyNpcIds = null)
    {
        if (!GameConfig.WardEnabled) return false;
        if (who is not NPC npc) return false;
        if (who.IsCompanion || who.CompanionId.HasValue || who.IsEcho || who.IsMercenary || who.IsGroupedPlayer) return false;
        if (WorldSimulator.IsOnPlayerTeam(npc)) return true;
        string id = npc.ID ?? "";
        if (id.Length == 0) return false;
        if (partyNpcIds != null && partyNpcIds.Contains(id)) return true;
        return party != null && party.Any(t => t is NPC mate && !mate.IsCompanion && !mate.CompanionId.HasValue && mate.ID == id);
    }

    /// <summary>
    /// Which of the last lines this NPC speaks: a stable hash (FNV-1a, 32 bit) of its ID, so the pick takes
    /// no draw from the fight's random or from any shared random, and is the same in every process.
    /// </summary>
    public static int LastLineVariant(string? npcId)
    {
        uint h = 2166136261;
        foreach (char c in npcId ?? "")
            unchecked { h = (h ^ c) * 16777619; }
        return (int)(h % LastLineCount) + 1;
    }

    /// <summary>The key of the last line this NPC speaks.</summary>
    public static string LastLineKey(string? npcId) => "oath_mark.last_" + LastLineVariant(npcId);

    /// <summary>The rows shown after a successful hire (Team Corner).</summary>
    public static string[] HireRows(string name) => new[] { Loc.Get("oath_mark.hire_laid", name), Loc.Get("oath_mark.hire_holds") };

    /// <summary>The row of the Team Corner's team view.</summary>
    public static string TeamViewRow() => Loc.Get("oath_mark.team_view");

    /// <summary>The row shown once when an NPC that is not on the team joins the player's dungeon party.</summary>
    public static string PartyJoinRow(string name) => Loc.Get("oath_mark.party_joins", name);

    /// <summary>The row after the fallen banner when the fall is not permanent.</summary>
    public static string CaughtRow(string name) => Loc.Get("oath_mark.caught", name);

    /// <summary>The scene when the fall is permanent: the ward flares and breaks, the last line, the closing row.</summary>
    public static string[] BreakSceneRows(string name, string? npcId) => new[]
    {
        Loc.Get("oath_mark.flares", name),
        Loc.Get("oath_mark.breaks"),
        Loc.Get(LastLineKey(npcId), name),
        Loc.Get("oath_mark.gone", name),
    };

    /// <summary>The text of the lasting record (GriefSystem.AddNpcMemory, saved with the player's grief memories).</summary>
    public static string MemoryText(string name) => Loc.Get("oath_mark.memory", name);

    /// <summary>The rows when an NPC is sacked from the player's team: the protection ends at once, and death
    /// by old age is held for GameConfig.RecruitOldAgeGraceDays more days (H2).</summary>
    public static string[] LiftRows(string name) => new[]
        { Loc.Get("oath_mark.lifts", name), Loc.Get("oath_mark.years_return", GameConfig.RecruitOldAgeGraceDays) };

    /// <summary>The rows when the last player leaves a team that still has hired NPCs.</summary>
    public static string[] LiftTeamRows() => new[]
        { Loc.Get("oath_mark.lifts_team"), Loc.Get("oath_mark.years_return", GameConfig.RecruitOldAgeGraceDays) };
}
