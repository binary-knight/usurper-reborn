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

/// <summary>v1.1.13: a queued purge and a character made again after the delete.</summary>
public partial class OwnerProcessConflictTests
{
    // ─── v1.1.13 r2: a queued purge leaves a recreated character's rows alone ───

    private static readonly System.Reflection.FieldInfo HighWater =
        typeof(NPCSpawnSystem).GetField("_rosterHighWaterMark", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance)!;
    private int _highWaterBefore;

    private void RestoreHighWater()
    {
        QuestSystem.RemovePlayerQuests("Quillon");
        HighWater.SetValue(NPCSpawnSystem.Instance, _highWaterBefore);
    }

    private void QueuedPurgeWorld(bool recreated, out Quest quest)
    {
        var grudger = Npc("npc_r_g", "Grudger");
        Remember(grudger, MemoryType.Attacked, "Quillon", DateTime.Now.AddMinutes(-20));   // before the delete
        Remember(grudger, MemoryType.Insulted, "Quillon", DateTime.Now.AddMinutes(-3));    // after it
        Exec("CREATE TABLE IF NOT EXISTS guild_members (username TEXT PRIMARY KEY COLLATE NOCASE, guild_name TEXT NOT NULL, rank TEXT NOT NULL DEFAULT 'Member', joined_at TEXT DEFAULT (datetime('now')));");
        Exec("INSERT INTO pending_purges (username, name2, display_name, deleted_at, player_id, untimed) " +
             "VALUES ('quillon_acct', 'Quillon', 'Quillon', datetime('now', '-10 minutes'), 'old_q_id', 1);");
        if (recreated)
            Exec("INSERT INTO players (username, display_name, player_data, created_at, last_login) VALUES ('quillon_acct', 'Quillon', " +
                 "'{\"player\":{\"name2\":\"Quillon\",\"id\":\"new_q_id\"}}', datetime('now', '-5 minutes'), datetime('now', '-1 minutes'));");
        Exec("INSERT INTO guild_members (username, guild_name) VALUES ('quillon_acct', 'Knights');");
        Exec("INSERT INTO auction_listings (seller, item_name, item_json, price, expires_at) VALUES ('quillon', 'Sword', '{}', 10, datetime('now', '+1 day'));");
        quest = new Quest { Title = "Deliver", Initiator = "Mayor", Occupier = "Quillon", Date = DateTime.Now, DaysToComplete = 5 };
        // a roster plausibly whole, so the auction purge can rule out an NPC seller of that name (the
        // high-water mark is put back afterwards, see RestoreHighWater)
        _highWaterBefore = (int)HighWater.GetValue(NPCSpawnSystem.Instance)!;
        HighWater.SetValue(NPCSpawnSystem.Instance, 0);
        for (int i = 0; i < 60; i++) Npc($"npc_r_filler_{i}", $"Filler {i}");
        NPCSpawnSystem.Instance.IsRosterTrustworthy.Should().BeTrue();
        QuestSystem.AddQuestToDatabase(quest);
    }

    private long Count(string sql)
    {
        using var conn = new SqliteConnection($"Data Source={_path}");
        conn.Open();
        using var cmd = conn.CreateCommand();
        cmd.CommandText = sql;
        return Convert.ToInt64(cmd.ExecuteScalar());
    }

    [Fact]
    public async Task AQueuedPurge_LeavesARecreatedCharactersGuildAuctionAndQuest()
    {
        Quest quest = null!;
        try
        {
            QueuedPurgeWorld(recreated: true, out quest);
            await _db.SaveWorldState(OnlineStateManager.KEY_NPCS, RosterJson());
            OnlineStateManager.NoteRosterRestored(_db.GetWorldStateVersion(OnlineStateManager.KEY_NPCS));
            WorldEditLog.OwnerOverride = true;

            (await UsurperRemake.Server.MudServer.DrainPendingPurgesAsync(_db)).Should().Be(1);

            Count("SELECT COUNT(*) FROM guild_members WHERE username = 'quillon_acct'").Should().Be(1, "the new character's guild membership stays");
            Count("SELECT COUNT(*) FROM auction_listings WHERE seller = 'quillon'").Should().Be(1, "and its auction");
            QuestSystem.GetAllQuests(includeCompleted: true).Should().Contain(quest, "and its quest");
            Find("Grudger")!.Brain!.Memory.AllMemories.Where(m => m.InvolvedCharacter == "Quillon").Select(m => m.Type)
                .Should().Equal(new[] { MemoryType.Insulted }, "the NPC memories up to the delete are still cleared");
        }
        finally { RestoreHighWater(); }
    }

    [Fact]
    public async Task AQueuedPurge_OfACharacterNotMadeAgain_ClearsItsRows()
    {
        Quest quest = null!;
        try
        {
            QueuedPurgeWorld(recreated: false, out quest);
            await _db.SaveWorldState(OnlineStateManager.KEY_NPCS, RosterJson());
            OnlineStateManager.NoteRosterRestored(_db.GetWorldStateVersion(OnlineStateManager.KEY_NPCS));
            WorldEditLog.OwnerOverride = true;

            (await UsurperRemake.Server.MudServer.DrainPendingPurgesAsync(_db)).Should().Be(1);

            Count("SELECT COUNT(*) FROM guild_members WHERE username = 'quillon_acct'").Should().Be(0);
            Count("SELECT COUNT(*) FROM auction_listings WHERE seller = 'quillon'").Should().Be(0);
            QuestSystem.GetAllQuests(includeCompleted: true).Should().NotContain(quest);
        }
        finally { RestoreHighWater(); }
    }
}
