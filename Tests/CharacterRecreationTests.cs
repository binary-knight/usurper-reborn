using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using FluentAssertions;
using Microsoft.Data.Sqlite;
using UsurperRemake;
using UsurperRemake.Systems;
using Xunit;

namespace UsurperReborn.Tests;

/// <summary>
/// v1.1.11: player report: "I deleted my old character and created a new one using the exact same
/// name. The new character retained the list of unfinished quests (from the quest board) and the
/// deity I had chosen for the previous character." Worship and quests are keyed by display name in
/// shared systems, and only permadeath purged them. Now every delete path runs the permadeath purge,
/// and character creation clears whatever is still held under the new name.
/// </summary>
[Collection("SharedGameSingletons")]
public class CharacterRecreationTests : IDisposable
{
    private const string God = "Solarius";   // a default pantheon god, so VerifyGodExists holds
    private readonly string _path = Path.Combine(Path.GetTempPath(), $"usurper-recreate-{Guid.NewGuid():N}.db");
    private readonly List<Quest> _added = new();
    private SqlSaveBackend? _db;

    public void Dispose()
    {
        // GetAllQuests is a copy; remove the seeds through the database's own removers.
        QuestSystem.RemovePlayerQuests("Bob", "Alice");
        QuestSystem.RemoveBountiesOnPlayer("Bob");
        QuestSystem.RemoveBountiesOnPlayer("Aldric");
        foreach (var name in new[] { "Bob", "bob", "Alice" })
            GodSystemSingleton.Instance.SetPlayerGod(name, "");
        if (_db != null)
        {
            SqliteConnection.ClearAllPools();
            try { File.Delete(_path); } catch { }
        }
    }

    private Quest Add(Quest q)
    {
        QuestSystem.AddQuestToDatabase(q);
        _added.Add(q);
        return q;
    }

    private static Quest Wanted(string target) => new Quest
    {
        Title = "WANTED: " + target, Initiator = "The Crown", QuestTarget = QuestTarget.DefeatNPC,
        TargetNPCName = target, BountyGold = 5000, Date = DateTime.Now, DaysToComplete = 30
    };

    private static bool InDatabase(Quest q) => QuestSystem.GetAllQuests(includeCompleted: true).Contains(q);

    [Fact]
    public void ANewCharacterWithADeletedCharactersName_HasNoGodAndNoQuests()
    {
        var gods = GodSystemSingleton.Instance;
        gods.SetPlayerGod("Bob", God);
        gods.PlayerHasGod("Bob").Should().BeTrue("the seed must be a real worship entry, or the test proves nothing");
        var claimed = Add(new Quest { Title = "Old claim", Occupier = "Bob", Date = DateTime.Now, DaysToComplete = 7 });
        var offered = Add(new Quest { Title = "Old offer", OfferedTo = "bob", Date = DateTime.Now, DaysToComplete = 7 });
        var someoneElses = Add(new Quest { Title = "Alice's claim", Occupier = "Alice", Date = DateTime.Now, DaysToComplete = 7 });

        GameEngine.ClearLeftoversForNewCharacter("Bob").Should().Be(2);

        gods.PlayerHasGod("Bob").Should().BeFalse("the deleted character's god must not carry over");
        gods.GetPlayerGod("Bob").Should().BeEmpty();
        InDatabase(claimed).Should().BeFalse();
        InDatabase(offered).Should().BeFalse("OfferedTo matches in any letter case");
        InDatabase(someoneElses).Should().BeTrue("another character's quest is untouched");
    }

    [Fact]
    public void TheGodEntryIsClearedInAnyLetterCase()
    {
        var gods = GodSystemSingleton.Instance;
        gods.SetPlayerGod("bob", God);
        gods.SetPlayerGod("Alice", God);

        gods.ClearPlayerGodAnyCase("Bob").Should().Be(1);

        gods.PlayerHasGod("bob").Should().BeFalse();
        gods.PlayerHasGod("Alice").Should().BeTrue();
    }

    [Fact]
    public async System.Threading.Tasks.Task AWantedBountyOnTheName_IsRemoved_AtCreationAndByThePurge()
    {
        var onBob = Add(Wanted("Bob"));
        var onNpc = Add(Wanted("Aldric"));
        GameEngine.ClearLeftoversForNewCharacter("Bob").Should().Be(1);
        InDatabase(onBob).Should().BeFalse("the King's bounty on the old character must not hang over the new one");
        InDatabase(onNpc).Should().BeTrue();

        var again = Add(Wanted("bob"));
        await PermadeathHelper.PurgeDeletedCharacterAsync(null, "bob_account", "Bob");
        InDatabase(again).Should().BeFalse("the delete purge removes it too");
        InDatabase(onNpc).Should().BeTrue();
    }

    [Fact]
    public async System.Threading.Tasks.Task ThePurge_RemovesQueuedDeliveriesForTheKey()
    {
        _db = new SqlSaveBackend(_path);
        _db.QueueInheritance("bob", "Aldric", "{\"name\":\"sword\"}").Should().BeTrue();
        _db.QueueInheritance("carol", "Aldric", "{\"name\":\"shield\"}").Should().BeTrue();
        _db.QueueGoldTransfer("bob", "Alice", 500).Should().BeTrue();
        _db.QueueGoldTransfer("carol", "Alice", 700).Should().BeTrue();
        (await _db.InsertWorldBossReward(new WorldBossReward { BossId = 1, BossName = "Leviathan", PlayerName = "bob", Night = 1, Gold = 100 })).Should().BeTrue();
        (await _db.InsertWorldBossReward(new WorldBossReward { BossId = 1, BossName = "Leviathan", PlayerName = "bob", Night = 2, Gold = 200 })).Should().BeTrue();
        (await _db.InsertWorldBossReward(new WorldBossReward { BossId = 1, BossName = "Leviathan", PlayerName = "carol", Night = 1, Gold = 300 })).Should().BeTrue();
        var delivered = _db.GetUndeliveredWorldBossRewards("bob").First(r => r.Night == 2);
        (await _db.MarkWorldBossRewardDelivered(delivered.Id)).Should().BeTrue();

        _db.PurgePlayerWorldState("Bob", "Bob");

        _db.GetPendingInheritance("bob").Should().BeEmpty();
        _db.GetPendingGoldTransfers("bob").Should().BeEmpty();
        _db.GetUndeliveredWorldBossRewards("bob").Should().BeEmpty();
        _db.GetPendingInheritance("carol").Should().ContainSingle("another character's queue is untouched");
        _db.GetPendingGoldTransfers("carol").Should().ContainSingle();
        _db.GetUndeliveredWorldBossRewards("carol").Should().ContainSingle();

        using var conn = new SqliteConnection($"Data Source={_path}");
        conn.Open();
        using var cmd = conn.CreateCommand();
        cmd.CommandText = "SELECT COUNT(*) FROM world_boss_rewards WHERE player_name = 'bob' AND delivered = 1;";
        Convert.ToInt64(cmd.ExecuteScalar()).Should().Be(1, "a delivered reward is history and stays");
    }

    [Fact]
    public void TheGodRestoreOnLoad_KeepsOnlyTheLoadingPlayersEntry()
    {
        var gods = new GodSystem();
        var saved = new Dictionary<string, string> { ["Bob"] = God, ["Alice"] = "Valorian" };

        SaveSystem.RestorePlayerGods(gods, saved, GameEngine.GodRestoreFilterFor(new Character { Name1 = "alice", Name2 = "Alice" }));

        gods.GetPlayerGod("Alice").Should().Be("Valorian");
        gods.PlayerHasGod("Bob").Should().BeFalse("an old entry for another name in the save is not re-injected");

        var none = new GodSystem();
        SaveSystem.RestorePlayerGods(none, saved, GameEngine.GodRestoreFilterFor(null));
        none.PlayerHasGod("Bob").Should().BeFalse("with no name, no one's entry is restored");
        none.PlayerHasGod("Alice").Should().BeFalse();
    }

    private static string Source(params string[] parts)
    {
        var dir = new DirectoryInfo(Directory.GetCurrentDirectory());
        while (dir != null && !File.Exists(Path.Combine(dir.FullName, "usurper-reloaded.csproj")))
            dir = dir.Parent;
        dir.Should().NotBeNull("the test must be able to find the repo root");
        return File.ReadAllText(Path.Combine(new[] { dir!.FullName, "Scripts" }.Concat(parts).ToArray()));
    }

    private static string Between(string source, string from, string to)
    {
        int a = source.IndexOf(from, StringComparison.Ordinal);
        a.Should().BeGreaterThanOrEqualTo(0, $"'{from}' must be in the source");
        int b = source.IndexOf(to, a, StringComparison.Ordinal);
        b.Should().BeGreaterThan(a, $"'{to}' must follow '{from}'");
        return source.Substring(a, b - a);
    }

    [Fact]
    public void EveryOnlineDeletePath_RunsThePurgeBeforeTheDelete()
    {
        var engine = Source("Core", "GameEngine.cs");
        var n = Between(engine, "Loc.Get(\"engine.delete_main_warning\")", "await CreateNewGame(accountName);");
        n.Should().Contain("PermadeathHelper.PurgeDeletedCharacterAsync(");
        n.IndexOf("PurgeDeletedCharacterAsync", StringComparison.Ordinal).Should().BeLessThan(n.IndexOf("DeleteSave(", StringComparison.Ordinal));

        var d = Between(engine, "Loc.Get(\"engine.delete_alt_warning\"", "Loc.Get(\"engine.delete_alt_done\"");
        d.Should().Contain("PermadeathHelper.PurgeDeletedCharacterAsync(");
        d.IndexOf("PurgeDeletedCharacterAsync", StringComparison.Ordinal).Should().BeLessThan(d.IndexOf("DeleteSave(", StringComparison.Ordinal));

        foreach (var (file, marker) in new[] { ("OnlineAdminConsole.cs", "DeleteGameData(target.Username)"), ("SysOpConsoleManager.cs", "DeleteGameData(target.Username)") })
        {
            var src = Source("Systems", file);
            int del = src.IndexOf(marker, StringComparison.Ordinal);
            del.Should().BeGreaterThan(0, file);
            var before = src.Substring(Math.Max(0, del - 600), 600);
            before.Should().Contain("PermadeathHelper.PurgeDeletedCharacterAsync(", $"{file} purges before it deletes");
        }

        Source("Systems", "PermadeathHelper.cs").Should().Contain("await PurgeDeletedCharacterAsync(", "permadeath shares the same purge");
    }

    [Fact]
    public void SinglePlayer_SlotDeleteAndLoad_GoThroughTheNameAwarePaths()
    {
        var engine = Source("Core", "GameEngine.cs");
        var slotDelete = Between(engine, "Loc.Get(\"engine.delete_saves_confirm\"", "Loc.Get(\"engine.all_saves_deleted\")");
        slotDelete.Should().Contain("SaveSystem.Instance.DeleteSaves(");
        slotDelete.Should().NotContain("File.Delete(", "the raw delete skipped the god clear");

        engine.Should().NotContain("IsOnlineMode && currentPlayer != null)\n                ? currentPlayer.Name2 : null");
        System.Text.RegularExpressions.Regex.Matches(engine, @"RestoreStorySystems\(saveData\.StorySystems, god\w*\)").Count.Should().Be(2);
        System.Text.RegularExpressions.Regex.Matches(engine, @"string\? god\w* = GodRestoreFilterFor\(currentPlayer\)").Count.Should().Be(2);
    }

    [Fact]
    public void EveryCharacterDelete_RunsThePurgeFirst_OrSaysWhyNot()
    {
        // Each call that deletes a character's data must purge the world state keyed by it first, or a
        // same-name character inherits it (player report: quests and deity carried over).
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir != null && !Directory.Exists(Path.Combine(dir.FullName, "Scripts"))) dir = dir.Parent;
        var allowed = new System.Collections.Generic.Dictionary<string, string>
        {
            ["SaveSystem.cs"] = "DeleteSave: the backend call itself; its online callers purge first (N, D)",
            ["CastleLocation.cs"] = "rebellion coin flip: deferred for 1.1.12 (it also passes the wrong key)",
        };
        var problems = new System.Collections.Generic.List<string>();
        foreach (var file in Directory.EnumerateFiles(Path.Combine(dir!.FullName, "Scripts"), "*.cs", SearchOption.AllDirectories))
        {
            string src = File.ReadAllText(file);
            string name = Path.GetFileName(file);
            foreach (System.Text.RegularExpressions.Match m in System.Text.RegularExpressions.Regex.Matches(src, @"\.DeleteGameData\("))
            {
                if (allowed.ContainsKey(name)) continue;
                int methodStart = Math.Max(src.LastIndexOf("\n    private ", m.Index, StringComparison.Ordinal), Math.Max(src.LastIndexOf("\n    public ", m.Index, StringComparison.Ordinal), src.LastIndexOf("\n        public ", m.Index, StringComparison.Ordinal)));
                string before = src.Substring(Math.Max(0, methodStart), m.Index - Math.Max(0, methodStart));
                if (!before.Contains("PurgeDeletedCharacterAsync("))
                    problems.Add($"{name}:{src.Take(m.Index).Count(c => c == '\n') + 1}");
            }
        }
        problems.Should().BeEmpty();
    }

    [Fact]
    public void CreatingANewGame_RunsTheBackstop()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir != null && !Directory.Exists(Path.Combine(dir.FullName, "Scripts"))) dir = dir.Parent;
        string src = File.ReadAllText(Path.Combine(dir!.FullName, "Scripts", "Core", "GameEngine.cs"));
        int start = src.IndexOf("async Task CreateNewGame(", StringComparison.Ordinal);
        start.Should().BeGreaterThan(0);
        int end = src.IndexOf("\n    private ", start + 10, StringComparison.Ordinal);
        src.Substring(start, end - start).Should().Contain("ClearLeftoversForNewCharacter(");
    }
}
