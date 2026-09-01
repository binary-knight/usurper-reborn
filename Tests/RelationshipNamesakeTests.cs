using System.Collections.Generic;
using Xunit;
using FluentAssertions;
using UsurperRemake.Systems;

namespace UsurperReborn.Tests;

/// <summary>
/// v1.0.4 regression tests. Relationship records are keyed by display name, so
/// a newcomer who took a departed NPC's name used to inherit that NPC's whole
/// record. Reported live: a player's wife permadied in the dungeon and later
/// "another NPC appeared with the same name and the same level of love".
/// Records now also check the stored NPC id (IdTag1/IdTag2), so a namesake
/// starts as a stranger while the same NPC keeps its history.
/// </summary>
[Collection("SharedGameSingletons")]
public class RelationshipNamesakeTests
{
    private static Character MakePlayer(string id = "thorulf")
        => new Character { Name1 = "Thorulf", Name2 = "Thorulf", ID = id, Level = 48 };

    private static NPC MakeNpc(string id, string name)
        => new NPC { ID = id, Name1 = name, Name2 = name, Level = 20, HP = 100, MaxHP = 100 };

    private static void SetScore(Character a, Character b, int score)
    {
        var rec = RelationshipSystem.GetOrCreateRelationship(a, b);
        if (rec.Name1 == a.Name)
            rec.Relation1 = score;
        else
            rec.Relation2 = score;
    }

    [Fact]
    public void NamesakeWithDifferentId_StartsAsStranger()
    {
        RelationshipSystem.Instance.Reset();
        var player = MakePlayer();
        var wife = MakeNpc("npc_imm_jocelyn_1a2b3c4d", "Jocelyn Holloway");
        SetScore(player, wife, GameConfig.RelationLove);

        var namesake = MakeNpc("npc_imm_jocelyn_9f8e7d6c", "Jocelyn Holloway");

        RelationshipSystem.GetRelationshipStatus(player, namesake).Should().Be(GameConfig.RelationNormal);
    }

    [Fact]
    public void NamesakeWithDifferentId_GetsAFreshRecordCarryingItsOwnId()
    {
        RelationshipSystem.Instance.Reset();
        var player = MakePlayer();
        var wife = MakeNpc("npc_imm_jocelyn_1a2b3c4d", "Jocelyn Holloway");
        SetScore(player, wife, GameConfig.RelationLove);

        var namesake = MakeNpc("npc_imm_jocelyn_9f8e7d6c", "Jocelyn Holloway");
        var rec = RelationshipSystem.GetOrCreateRelationship(player, namesake);

        rec.IdTag2.Should().Be(namesake.ID);
        rec.Relation1.Should().Be(GameConfig.RelationNormal);
        RelationshipSystem.GetRelationshipStatus(player, namesake).Should().Be(GameConfig.RelationNormal);
    }

    [Fact]
    public void SameNpc_KeepsRelationship()
    {
        RelationshipSystem.Instance.Reset();
        var player = MakePlayer();
        var wife = MakeNpc("npc_imm_jocelyn_1a2b3c4d", "Jocelyn Holloway");
        SetScore(player, wife, GameConfig.RelationLove);

        RelationshipSystem.GetRelationshipStatus(player, wife).Should().Be(GameConfig.RelationLove);
    }

    [Fact]
    public void RecordStoredInReverseOrder_StillRejectsNamesake()
    {
        RelationshipSystem.Instance.Reset();
        var player = MakePlayer();
        var wife = MakeNpc("npc_imm_jocelyn_1a2b3c4d", "Jocelyn Holloway");
        // Record created as (npc, player), read back as (player, npc)
        SetScore(wife, player, GameConfig.RelationLove);
        var wifeToPlayer = RelationshipSystem.GetRelationshipStatus(wife, player);
        wifeToPlayer.Should().Be(GameConfig.RelationLove);

        var namesake = MakeNpc("npc_imm_jocelyn_9f8e7d6c", "Jocelyn Holloway");

        RelationshipSystem.GetRelationshipStatus(namesake, player).Should().Be(GameConfig.RelationNormal);
        RelationshipSystem.GetRelationshipStatus(player, namesake).Should().Be(GameConfig.RelationNormal);
    }

    [Fact]
    public void LegacyRecordWithoutIds_IsTrustedAndStampedOnWrite()
    {
        RelationshipSystem.Instance.Reset();
        var player = MakePlayer();
        var wife = MakeNpc("npc_imm_jocelyn_1a2b3c4d", "Jocelyn Holloway");
        SetScore(player, wife, GameConfig.RelationLove);
        var legacy = RelationshipSystem.GetOrCreateRelationship(player, wife);
        legacy.IdTag1 = "";
        legacy.IdTag2 = "";

        RelationshipSystem.GetRelationshipStatus(player, wife).Should().Be(GameConfig.RelationLove);

        var stamped = RelationshipSystem.GetOrCreateRelationship(player, wife);
        stamped.Should().BeSameAs(legacy);
        stamped.IdTag1.Should().Be(player.ID);
        stamped.IdTag2.Should().Be(wife.ID);
    }

    [Fact]
    public void PlayerIdChange_DoesNotInvalidateRecord()
    {
        RelationshipSystem.Instance.Reset();
        var player = MakePlayer("thorulf-guid");
        var wife = MakeNpc("npc_imm_jocelyn_1a2b3c4d", "Jocelyn Holloway");
        SetScore(player, wife, GameConfig.RelationLove);

        // GameEngine falls back to Name2 when a save carries no player id
        var reloaded = MakePlayer("Thorulf");

        RelationshipSystem.GetRelationshipStatus(reloaded, wife).Should().Be(GameConfig.RelationLove);
    }

    [Fact]
    public void NamesakeAtLove_IsNotTreatedAsLover()
    {
        RelationshipSystem.Instance.Reset();
        var player = MakePlayer();
        var wife = MakeNpc("npc_imm_jocelyn_1a2b3c4d", "Jocelyn Holloway");
        SetScore(player, wife, GameConfig.RelationLove);
        SetScore(wife, player, GameConfig.RelationLove);
        RelationshipSystem.IsMarriedOrLover(wife, player.Name).Should().BeTrue();

        var namesake = MakeNpc("npc_imm_jocelyn_9f8e7d6c", "Jocelyn Holloway");

        RelationshipSystem.IsMarriedOrLover(namesake, player.Name).Should().BeFalse();
        // The name-only overload cannot tell them apart; that is why the sleeping-attack checks moved off it
        RelationshipSystem.IsMarriedOrLover(namesake.Name, player.Name).Should().BeTrue();
    }

    /// <summary>
    /// Runs SyncDeadSpouseState against the live NPC roster. The roster must be
    /// plausibly complete (see NPCSpawnSystem.IsCountPlausible) for a missing or
    /// mismatched spouse to count as gone, so filler NPCs are added and removed.
    /// </summary>
    private static int SyncAgainstRoster(Character player, params NPC[] roster)
    {
        var spawner = NPCSpawnSystem.Instance;
        var added = new List<NPC>(roster);
        for (int i = added.Count; i < 60; i++)
            added.Add(MakeNpc($"npc_filler_{i}", $"Filler {i}"));
        foreach (var n in added) spawner.ActiveNPCs.Add(n);
        try
        {
            spawner.IsRosterTrustworthy.Should().BeTrue();
            return RelationshipSystem.SyncDeadSpouseState(player);
        }
        finally
        {
            foreach (var n in added) spawner.ActiveNPCs.Remove(n);
        }
    }

    [Fact]
    public void StaleMarriedRecord_WithLiveNamesake_IsRetiredOnLogin()
    {
        RelationshipSystem.Instance.Reset();
        var player = MakePlayer();
        var wife = MakeNpc("npc_imm_jocelyn_1a2b3c4d", "Jocelyn Holloway");
        SetScore(player, wife, GameConfig.RelationMarried);
        SetScore(wife, player, GameConfig.RelationMarried);

        // The corpse has been pruned; only the namesake carries the name now
        var namesake = MakeNpc("npc_imm_jocelyn_9f8e7d6c", "Jocelyn Holloway");

        SyncAgainstRoster(player, namesake).Should().Be(1);
        RelationshipSystem.GetRelationshipStatus(player, namesake).Should().Be(GameConfig.RelationNormal);
    }

    [Fact]
    public void MarriedRecord_WithSpouseStillInRoster_IsLeftAlone()
    {
        RelationshipSystem.Instance.Reset();
        var player = MakePlayer();
        var wife = MakeNpc("npc_imm_jocelyn_1a2b3c4d", "Jocelyn Holloway");
        SetScore(player, wife, GameConfig.RelationMarried);
        SetScore(wife, player, GameConfig.RelationMarried);

        SyncAgainstRoster(player, wife).Should().Be(0);
        RelationshipSystem.GetRelationshipStatus(player, wife).Should().Be(GameConfig.RelationMarried);
    }

    [Fact]
    public void MarriedRecord_WithSpouseTemporarilyDead_IsLeftAlone()
    {
        RelationshipSystem.Instance.Reset();
        var player = MakePlayer();
        var wife = MakeNpc("npc_imm_jocelyn_1a2b3c4d", "Jocelyn Holloway");
        SetScore(player, wife, GameConfig.RelationMarried);
        SetScore(wife, player, GameConfig.RelationMarried);
        wife.IsDead = true; // world-sim knockdown, respawns in ~10 minutes

        SyncAgainstRoster(player, wife).Should().Be(0);
        RelationshipSystem.GetRelationshipStatus(player, wife).Should().Be(GameConfig.RelationMarried);
    }
}
