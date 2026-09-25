using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using FluentAssertions;
using Microsoft.Data.Sqlite;
using UsurperRemake.Systems;
using Xunit;

namespace UsurperReborn.Tests;

/// <summary>v1.1.13: a door's retried NPC save across repeated conflicts.</summary>
public partial class OwnerProcessConflictTests
{
    // ─── v1.1.13 r2: a removed NPC stays removed across repeated conflicts ───

    [Fact]
    public async Task ThreeConflictsInARow_NeverResurrectAnNpcAnotherWriterRemoved()
    {
        Npc("npc_t_x", "Xavier");
        Npc("npc_t_y", "Yorick");
        Npc("npc_t_z", "Zelda");
        await _db.SaveWorldState(OnlineStateManager.KEY_NPCS, RosterJson());
        WorldEditLog.OwnerOverride = false;
        var dbA = new SqlSaveBackend(_path);
        var osmA = await DoorLogin(dbA);
        Find("Xavier")!.Level = 33;        // A changed Xavier
        Npc("npc_t_new", "Newcomer");      // and made Newcomer

        int attempt = 0;
        (await osmA.SaveSharedNPCsVersionedAsync(dbA, OnlineStateManager.SerializeCurrentNPCs(), beforeWrite: async () =>
        {
            switch (attempt++)
            {
                case 0: await OtherWriter(r => r.Single(d => d.Name == "Yorick").Gold = 11); break;   // conflict 1
                case 1: await OtherWriter(r => r.RemoveAll(d => d.Name == "Xavier")); break;          // conflict 2: Xavier removed
                case 2: await OtherWriter(r => r.Single(d => d.Name == "Zelda").Gold = 22); break;    // conflict 3
            }
        })).Should().BeTrue();

        attempt.Should().Be(4, "three conflicts, then the write");
        var stored = await StoredRoster();
        stored.Select(d => d.Name).Should().NotContain("Xavier", "an NPC another writer removed is never appended back");
        stored.Select(d => d.Name).Should().Contain("Newcomer", "an NPC this session created is kept through every retry");
        stored.Single(d => d.Name == "Yorick").Gold.Should().Be(11);
        stored.Single(d => d.Name == "Zelda").Gold.Should().Be(22);
        Find("Xavier").Should().BeNull();
    }
}
