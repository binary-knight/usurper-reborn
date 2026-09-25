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

/// <summary>
/// v1.1.13: the conflict paths of the shared world records: treasury moves, a purge's roster write, a
/// door's retried save, the owner's marriage registry, and the purges a web delete queues.
/// </summary>
[Collection("SharedGameSingletons")]
public partial class OwnerProcessConflictTests : IDisposable
{
    private readonly string _path = Path.Combine(Path.GetTempPath(), $"usurper-owner3-{Guid.NewGuid():N}.db");
    private readonly SqlSaveBackend _db;
    private readonly List<NPC> _rosterBefore;
    private readonly List<NPCMarriageData> _registryBefore;
    private readonly object? _instanceBefore;

    private static readonly System.Reflection.FieldInfo FallbackInstance =
        typeof(OnlineStateManager).GetField("_fallbackInstance", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Static)!;

    private static readonly JsonSerializerOptions Json = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        IncludeFields = true,
    };

    public OwnerProcessConflictTests()
    {
        _db = new SqlSaveBackend(_path);
        _rosterBefore = NPCSpawnSystem.Instance.ActiveNPCs.ToList();
        NPCSpawnSystem.Instance.ActiveNPCs.Clear();
        _registryBefore = NPCMarriageRegistry.Instance.GetAllMarriages();
        NPCMarriageRegistry.Instance.RestoreMarriages(null);
        _instanceBefore = FallbackInstance.GetValue(null);
        FallbackInstance.SetValue(null, null);
        OnlineStateManager.NoteRosterRestored(null);
    }

    public void Dispose()
    {
        var roster = NPCSpawnSystem.Instance.ActiveNPCs;
        roster.Clear();
        roster.AddRange(_rosterBefore);
        NPCSpawnSystem.Instance.IsRebuilding = false;
        NPCMarriageRegistry.Instance.RestoreMarriages(_registryBefore);
        FallbackInstance.SetValue(null, _instanceBefore);
        WorldEditLog.OwnerOverride = null;
        OnlineStateManager.NoteRosterRestored(null);
        SqliteConnection.ClearAllPools();
        try { File.Delete(_path); } catch { }
    }

    // ─── helpers ───

    private static NPC Npc(string id, string name)
    {
        var npc = new NPC { ID = id, Id = id, Name1 = name, Name2 = name, Level = 10, HP = 100, MaxHP = 100 };
        npc.EnsureSystemsInitialized();
        NPCSpawnSystem.Instance.ActiveNPCs.Add(npc);
        return npc;
    }

    private static void Remember(NPC npc, MemoryType type, string who, DateTime at)
    {
        var m = new MemoryEvent { Type = type, InvolvedCharacter = who, Description = type.ToString(), Importance = 0.9f };
        npc.Brain!.Memory.RecordEvent(m);
        m.Timestamp = at;
    }

    private static NPC? Find(string name) => NPCSpawnSystem.Instance.ActiveNPCs.FirstOrDefault(n => n.Name2 == name);

    private void Exec(string sql)
    {
        using var conn = new SqliteConnection($"Data Source={_path}");
        conn.Open();
        using var cmd = conn.CreateCommand();
        cmd.CommandText = sql;
        cmd.ExecuteNonQuery();
    }

    private string RosterJson() => JsonSerializer.Serialize(OnlineStateManager.SerializeCurrentNPCs(), Json);

    private async Task<List<NPCData>> StoredRoster() =>
        JsonSerializer.Deserialize<List<NPCData>>((await _db.LoadWorldState(OnlineStateManager.KEY_NPCS))!, Json)!;

    private static OnlineStateManager NewOsm(SqlSaveBackend db) =>
        (OnlineStateManager)Activator.CreateInstance(typeof(OnlineStateManager),
            System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance, null, new object[] { db, "door_a" }, null)!;

    /// <summary>Door process A: loads the stored roster as a login does, at its version, and is this process's session.</summary>
    private static async Task<OnlineStateManager> DoorLogin(SqlSaveBackend db)
    {
        var osm = NewOsm(db);
        await GameEngine.Instance.RestoreNPCs((await osm.LoadSharedNPCs())!, osm.NpcsVersion);
        osm.NoteNpcBaseline();
        FallbackInstance.SetValue(null, osm);
        return osm;
    }

    /// <summary>Another process's versioned write of the stored roster after an edit to it.</summary>
    private async Task OtherWriter(Action<List<NPCData>> edit)
    {
        long v = _db.GetWorldStateVersion(OnlineStateManager.KEY_NPCS);
        var roster = await StoredRoster();
        edit(roster);
        (await _db.SaveWorldStateIfVersion(OnlineStateManager.KEY_NPCS, JsonSerializer.Serialize(roster, Json), v)).Should().BeTrue();
    }

    // ─── 1. Treasury moves ───

    private static async Task WithKing(string name, long treasury, Func<King, Task> body)
    {
        var before = CastleLocation.GetCurrentKing();
        var history = CastleLocation.GetMonarchHistory().ToList();
        var version = OnlineStateManager.RoyalCourtVersion;
        bool loaded = CastleLocation.RoyalCourtLoadedFromShared;
        var king = King.CreateNewKing(name, CharacterAI.Human, CharacterSex.Male);
        king.Treasury = treasury;
        CastleLocation.SetKing(king);
        OnlineStateManager.NoteRoyalCourtVersion(null);
        try { await body(king); }
        finally
        {
            CastleLocation.SetKing(before);
            CastleLocation.SetMonarchHistory(history);
            OnlineStateManager.NoteRoyalCourtVersion(version);
            CastleLocation.RoyalCourtLoadedFromShared = loaded;
        }
    }

    private static string Court(string king, long treasury) =>
        JsonSerializer.Serialize(new RoyalCourtSaveData { KingName = king, KingAI = (int)CharacterAI.Human, Treasury = treasury, TaxRate = 7 }, Json);

    private async Task<RoyalCourtSaveData> StoredCourt() =>
        JsonSerializer.Deserialize<RoyalCourtSaveData>((await _db.LoadWorldState("royal_court"))!, new JsonSerializerOptions { PropertyNameCaseInsensitive = true })!;

    /// <summary>Process B writes the court with this treasury under its current version.</summary>
    private async Task OtherCourtWrite(string king, long treasury) =>
        (await _db.SaveWorldStateIfVersion("royal_court", Court(king, treasury), _db.GetWorldStateVersion("royal_court"))).Should().BeTrue();

    [Fact]
    public async Task AWithdrawal_AfterAndDuringConcurrentCourtWrites_NeitherDuplicatesNorLosesGold()
    {
        await WithKing("Kim", 1000, async _ =>
        {
            var dbA = new SqlSaveBackend(_path);
            await dbA.SaveWorldState("royal_court", Court("Kim", 1000));
            var osmA = NewOsm(dbA);
            await osmA.LoadRoyalCourtFromWorldState();   // door A holds the court at 1000
            var player = new Character { Name2 = "Kim", Gold = 0 };

            await OtherCourtWrite("Kim", 1500);   // B deposits 500 after A loaded
            int writes = 0;
            (await CastleLocation.MoveTreasuryGoldAsync(osmA, player, 300, async () =>
            {
                if (writes++ == 0) await OtherCourtWrite("Kim", 2000);   // and B deposits 500 more during A's move
            })).Should().BeTrue();

            writes.Should().Be(2, "the move's first write met B's second deposit and was retried");
            player.Gold.Should().Be(300, "the player is paid once");
            (await StoredCourt()).Treasury.Should().Be(1700, "both of B's deposits stay, and A's withdrawal leaves once");
            CastleLocation.GetCurrentKing()!.Treasury.Should().Be(1700);

            // a deposit, the same way
            player.Gold = 500;
            (await CastleLocation.MoveTreasuryGoldAsync(osmA, player, -200)).Should().BeTrue();
            player.Gold.Should().Be(300);
            (await StoredCourt()).Treasury.Should().Be(1900);
        });
    }

    [Fact]
    public async Task AFailedCourtSave_LeavesThePlayersGoldAndTheTreasuryUnchanged()
    {
        await WithKing("Kim", 1000, async _ =>
        {
            var dbA = new SqlSaveBackend(_path);
            await dbA.SaveWorldState("royal_court", Court("Kim", 1000));
            var osmA = NewOsm(dbA);
            await osmA.LoadRoyalCourtFromWorldState();
            var player = new Character { Name2 = "Kim", Gold = 50 };

            // every write of A's meets another write first
            long b = 1000;
            (await CastleLocation.MoveTreasuryGoldAsync(osmA, player, 300, () => OtherCourtWrite("Kim", b += 10))).Should().BeFalse();

            player.Gold.Should().Be(50, "the player is paid only once the treasury's write holds");
            (await StoredCourt()).Treasury.Should().Be(b, "the stored treasury is B's alone");
            CastleLocation.GetCurrentKing()!.Treasury.Should().Be(b, "and the in-memory court is the stored one");

            // a withdrawal larger than the stored treasury is refused, whatever A's stale copy says
            await OtherCourtWrite("Kim", 100);
            CastleLocation.GetCurrentKing()!.Treasury.Should().Be(b, "A has not seen B's spending");
            (await CastleLocation.MoveTreasuryGoldAsync(osmA, player, 500)).Should().BeFalse();
            player.Gold.Should().Be(50);
            (await StoredCourt()).Treasury.Should().Be(100);
        });
    }

    // ─── v1.1.13 r2: every court change is one guarded read-modify-write ───

    private string CourtWith(string king, long treasury, params (string Name, int Loyalty)[] guards) =>
        JsonSerializer.Serialize(new RoyalCourtSaveData
        {
            KingName = king, KingAI = (int)CharacterAI.Human, Treasury = treasury, TaxRate = 7, MagicBudget = GameConfig.MaxMagicBudget,
            Guards = guards.Select(g => new RoyalGuardSaveData { Name = g.Name, Loyalty = g.Loyalty, DailySalary = 10, IsActive = true }).ToList()
        }, Json);

    [Fact]
    public async Task ALoginReload_WhileTheSimSaves_NeverDuplicatesATreasuryChange()
    {
        // both orders of the reported interleave: the login's stale court is applied after or before the sim's save
        foreach (bool loginFirst in new[] { false, true })
        {
            await WithKing("Kim", 1000, async _ =>
            {
                var dbA = new SqlSaveBackend(_path);
                await dbA.SaveWorldState("royal_court", Court("Kim", 1000));
                var osmA = NewOsm(dbA);
                await osmA.LoadRoyalCourtFromWorldState();
                var sim = new WorldSimService(_db);

                // a login reads the court (1000) before the change lands, and applies it late
                long staleVersion = _db.GetWorldStateVersion("royal_court");
                var stale = JsonSerializer.Deserialize<RoyalCourtSaveData>((await _db.LoadWorldState("royal_court"))!, Json)!;

                var player = new Character { Name2 = "Pat", Gold = 250 };
                (await CastleLocation.MoveTreasuryGoldAsync(osmA, player, -250)).Should().BeTrue();   // +250 into the treasury
                player.Gold.Should().Be(0);

                if (loginFirst) OnlineStateManager.ApplyLoadedCourt(stale, staleVersion);
                await sim.SaveRoyalCourtToWorldState();
                if (!loginFirst) OnlineStateManager.ApplyLoadedCourt(stale, staleVersion);
                await sim.SaveRoyalCourtToWorldState();   // under the stale version: a conflict, reloaded, not written over
                await osmA.SaveRoyalCourtToWorldState();

                (await StoredCourt()).Treasury.Should().Be(1250, $"the +250 is stored exactly once (login first: {loginFirst})");
                CastleLocation.GetCurrentKing()!.Treasury.Should().Be(1250);
            });
        }
    }

    [Fact]
    public async Task AGuardBonus_AfterAConcurrentCourtWrite_KeepsBothTheCostAndTheLoyalty()
    {
        await WithKing("Kim", 1000, async _ =>
        {
            var dbA = new SqlSaveBackend(_path);
            await dbA.SaveWorldState("royal_court", CourtWith("Kim", 1000, ("Gerald", 50), ("Helena", 60)));
            var osmA = NewOsm(dbA);
            await osmA.LoadRoyalCourtFromWorldState();

            int writes = 0;
            (await CastleLocation.PayGuardBonusAsync(osmA, 200, async () =>
            {
                // another process's court write (its income) lands during the bonus
                if (writes++ == 0)
                    (await _db.SaveWorldStateIfVersion("royal_court", CourtWith("Kim", 1500, ("Gerald", 50), ("Helena", 60)),
                        _db.GetWorldStateVersion("royal_court"))).Should().BeTrue();
            })).Should().BeTrue();

            writes.Should().Be(2, "the first write met the other process's and was applied again");
            var stored = await StoredCourt();
            stored.Treasury.Should().Be(1100, "the other write's income stays and the bonus of 2 x 200 is paid once");
            stored.Guards.Select(g => g.Loyalty).Should().Equal(new[] { 52, 62 }, "the loyalty the bonus bought is stored with its cost");
            var king = CastleLocation.GetCurrentKing()!;
            king.Treasury.Should().Be(1100);
            king.Guards.Select(g => g.Loyalty).Should().Equal(new[] { 52, 62 });
        });
    }

    [Fact]
    public async Task TheSimsDailyTick_AndAWithdrawal_Interleaved_LeaveTheSumOfBoth()
    {
        await WithKing("Kim", 100000, async _ =>
        {
            await _db.SaveWorldState("royal_court", CourtWith("Kim", 100000, ("Gerald", 60)));
            var sim = new WorldSimService(_db);
            sim.LoadRoyalCourtFromWorldState();
            var dbA = new SqlSaveBackend(_path);
            var osmA = NewOsm(dbA);
            var player = new Character { Name2 = "Pat", Gold = 0 };

            var court = await StoredCourt();
            long net = King.DailyIncomeOf(court, 0) - King.DailyExpensesOf(court);
            long reign = court.TotalReign;

            int writes = 0;
            (await sim.ProcessCourtDailyAsync(async () =>
            {
                // a session's withdrawal lands between the sim's read and its write
                if (writes++ == 0) (await CastleLocation.MoveTreasuryGoldAsync(osmA, player, 300)).Should().BeTrue();
            })).Should().BeTrue();

            writes.Should().Be(2);
            player.Gold.Should().Be(300);
            var stored = await StoredCourt();
            stored.Treasury.Should().Be(100000 - 300 + net, "the day's income less expenses and the withdrawal both hold, each once");
            stored.TotalReign.Should().Be(reign + 1, "the day is counted once");
            CastleLocation.GetCurrentKing()!.Treasury.Should().Be(stored.Treasury);
        });
    }

    [Fact]
    public async Task TwoCourtChanges_BackToBack_TheSecondReadsTheFirstsVersion()
    {
        await WithKing("Kim", 1000, async _ =>
        {
            var dbA = new SqlSaveBackend(_path);
            await dbA.SaveWorldState("royal_court", Court("Kim", 1000));
            var osmA = NewOsm(dbA);
            await osmA.LoadRoyalCourtFromWorldState();
            var player = new Character { Name2 = "Pat", Gold = 500 };

            for (int i = 1; i <= 2; i++)
            {
                int writes = 0;
                (await CastleLocation.MoveTreasuryGoldAsync(osmA, player, -100, () => { writes++; return Task.CompletedTask; })).Should().BeTrue();
                writes.Should().Be(1, "no conflict: nothing else wrote the court");
                OnlineStateManager.RoyalCourtVersion.Should().Be(_db.GetWorldStateVersion("royal_court"), "the in-memory court is the written copy, at its version");
                CastleLocation.GetCurrentKing()!.Treasury.Should().Be((await StoredCourt()).Treasury);
            }
            (await StoredCourt()).Treasury.Should().Be(1200);
            player.Gold.Should().Be(300);

            // and the ordinary whole-court save that follows is no conflict either
            long before = _db.GetWorldStateVersion("royal_court");
            await osmA.SaveRoyalCourtToWorldState();
            _db.GetWorldStateVersion("royal_court").Should().Be(before + 1);
            (await StoredCourt()).Treasury.Should().Be(1200);
        });
    }

    /// <summary>Source with // and /* */ comments removed, so only code is checked.</summary>
    private static string CodeOnly(string src)
    {
        src = System.Text.RegularExpressions.Regex.Replace(src, @"/\*.*?\*/", "", System.Text.RegularExpressions.RegexOptions.Singleline);
        return string.Join("\n", src.Split('\n').Select(line =>
        {
            int c = line.IndexOf("//", StringComparison.Ordinal);
            return c >= 0 ? line.Substring(0, c) : line;
        }));
    }

    // v1.1.13: the court record's mutable members, and the fields of its entries (guards, monsters, courtiers,
    // orphans, heirs, the spouse, prisoners, plots)
    private const string CourtMembers = @"(?:Treasury|TaxRate|TotalReign|KingTaxPercent|CityTaxPercent|DesignatedHeir|MagicBudget|Spouse|Guards|MonsterGuards|Prisoners|Orphans|CourtMembers|Heirs|ActivePlots|EstablishmentStatus|LastProclamation|LastProclamationDate|CoronationDate|TaxAlignment)";
    private const string CourtLists = @"(?:Guards|MonsterGuards|Prisoners|Orphans|CourtMembers|Heirs|ActivePlots|Spouse)";
    private const string EntryFields = @"(?:Loyalty|LoyaltyToKing|Influence|IsPlotting|Happiness|ClaimStrength|IsDesignated|DaysServed|Sentence|BailAmount|Progress|IsDiscovered|DailySalary|HP)";
    private const string Write = @"(?:[-+*/]?=(?![=>])|\+\+|--)";

    /// <summary>
    /// v1.1.13: every write to a court member or a court entry's field in this code, other than on a guarded
    /// court change's copy. No allowlist: what is left out is left out by its form. The receiver court is a
    /// court change's record and working ApplyKingChangeAsync's King copy (both helpers name them so); a
    /// member assigned from a stored record (royalCourt. or data.RoyalCourt.) is a loader installing it; a King
    /// declared in the same method by new King or King.CreateNewKing is being built; an entry is a court
    /// entry when declared (foreach, var or out var) from a court list, and one from court. is the copy's.
    /// </summary>
    internal static List<string> InMemoryCourtWrites(string file, string code)
    {
        var rx = System.Text.RegularExpressions.RegexOptions.None;
        var assign = new System.Text.RegularExpressions.Regex(@"(\b\w+(?:\(\))?)\s*\.\s*" + CourtMembers + @"\s*" + Write, rx);
        var mutate = new System.Text.RegularExpressions.Regex(@"(\b\w+(?:\(\))?)\s*\.\s*" + CourtMembers + @"\s*(?:\[[^\]]*\]\s*=(?![=>])|\.\s*(?:Add|AddRange|Insert|Remove|RemoveAll|RemoveAt|Clear)\s*\()", rx);
        var chain = new System.Text.RegularExpressions.Regex(@"(\b\w+(?:\(\))?)\s*\.\s*" + CourtLists + @"\b[^;=]*?\.\s*" + EntryFields + @"\s*" + Write, rx);
        var entry = new System.Text.RegularExpressions.Regex(@"\b(\w+)\s*\.\s*" + EntryFields + @"\s*" + Write, rx);
        var copies = new[] { "court", "working" };
        var lines = code.Split('\n');
        var found = new List<string>();
        for (int i = 0; i < lines.Length; i++)
        {
            string line = lines[i];
            foreach (var m in assign.Matches(line).Concat(mutate.Matches(line)).Concat(chain.Matches(line)))
            {
                string receiver = m.Groups[1].Value;
                if (copies.Contains(receiver)) continue;
                if (System.Text.RegularExpressions.Regex.IsMatch(line.Substring(m.Index + m.Length), @"\b(royalCourt|RoyalCourt)\.")) continue;
                bool built = false;
                for (int j = i; j >= Math.Max(0, i - 60) && !built; j--)
                    built = System.Text.RegularExpressions.Regex.IsMatch(lines[j],
                        @"\bvar\s+" + System.Text.RegularExpressions.Regex.Escape(receiver) + @"\s*=\s*(King\.CreateNewKing\(|new King\b)");
                if (built) continue;
                found.Add($"{file}:{i + 1} {line.Trim()}");
            }
            foreach (System.Text.RegularExpressions.Match m in entry.Matches(line))
            {
                string v = System.Text.RegularExpressions.Regex.Escape(m.Groups[1].Value);
                string? from = null;
                for (int j = i; j >= Math.Max(0, i - 80) && from == null; j--)
                {
                    var d = System.Text.RegularExpressions.Regex.Match(lines[j], @"(?:foreach\s*\(\s*var\s+" + v + @"\s+in\s+|var\s+" + v + @"\s*=\s*)(.*)");
                    if (d.Success) { from = d.Groups[1].Value; break; }
                    d = System.Text.RegularExpressions.Regex.Match(lines[j], @"([\w.()!?]*\." + CourtLists + @")\.TryGetValue\([^,]*,\s*out\s+var\s+" + v + @"\b");
                    if (d.Success) from = d.Groups[1].Value;
                }
                if (from == null) continue;
                var list = System.Text.RegularExpressions.Regex.Match(from, @"^\s*\(?([\w.()!?]*?)\." + CourtLists + @"\b");
                if (!list.Success) continue;                                       // not a court entry
                if (copies.Contains(list.Groups[1].Value.Split('.')[0].TrimEnd('!', '?'))) continue;
                found.Add($"{file}:{i + 1} {line.Trim()}");
            }
        }
        return found.Distinct().ToList();   // one entry per line
    }

    [Fact]
    public void NoCourtMemberIsChanged_OutsideAGuardedCourtChange()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir != null && !Directory.Exists(Path.Combine(dir.FullName, "Scripts"))) dir = dir.Parent;
        var root = Path.Combine(dir!.FullName, "Scripts");

        // the check finds each form it looks for
        InMemoryCourtWrites("canary", string.Join("\n", new[]
        {
            "king.Treasury -= 5;", "currentKing.Guards.Remove(g);", "CastleLocation.GetCurrentKing().Orphans.Add(o);",
            "foreach (var guard in king.Guards)", "    guard.Loyalty = 0;", "if (king.Prisoners.TryGetValue(n, out var rec)) rec.BailAmount = 5;",
            "king.Spouse.Happiness += 1;", "king.EstablishmentStatus[key] = false;",
        })).Should().HaveCount(7);
        InMemoryCourtWrites("canary", string.Join("\n", new[]
        {
            "court.Treasury -= 5;", "working.Guards.Add(g);", "foreach (var guard in court.Guards)", "    guard.Loyalty = 0;",
            "king.Treasury = royalCourt.Treasury;", "var k = King.CreateNewKing(n, ai, sex);", "k.Treasury = 5;",
        })).Should().BeEmpty();

        var offenders = new List<string>();
        foreach (var path in Directory.GetFiles(root, "*.cs", SearchOption.AllDirectories))
            offenders.AddRange(InMemoryCourtWrites(Path.GetRelativePath(root, path), CodeOnly(File.ReadAllText(path))));
        offenders.Should().BeEmpty("every change to a court goes through a guarded court change (ApplyCourtChangeAsync, ApplyKingChangeAsync or CrownAsync)");
    }

    [Fact]
    public void TheCarriedTreasuryDelta_IsGone()
    {
        var osm = CodeOnly(Source("Systems", "OnlineStateManager.cs")) + CodeOnly(Source("Systems", "WorldSimService.cs"));
        osm.Should().NotContain("UnsavedTreasury").And.NotContain("TreasuryAfterLoad").And.NotContain("_courtBaselineTreasury")
            .And.NotContain("TryMoveTreasuryAsync");
    }

    [Fact]
    public void TheCastlesTreasuryMoves_PayThePlayerOnlyAfterTheCourtWrite()
    {
        var src = Source("Locations", "CastleLocation.cs");
        foreach (var method in new[] { "private async Task WithdrawFromTreasury()", "private async Task DepositToTreasury()" })
        {
            int at = src.IndexOf(method, StringComparison.Ordinal);
            var body = src.Substring(at, src.IndexOf("await Task.Delay(2000);\n    }", at, StringComparison.Ordinal) - at);
            body.Should().Contain("await MoveTreasuryGoldAsync(TreasuryOsm(), currentPlayer,");
            body.Should().NotContain("currentPlayer.Gold +=").And.NotContain("currentPlayer.Gold -=")
                .And.NotContain("currentKing.Treasury +=").And.NotContain("currentKing.Treasury -=");
        }
    }

    // ─── 2 and 3. A purge's roster write ───

    [Fact]
    public async Task APurge_WritesUnderTheVersionItsRosterWasLoadedAt_KeepingANewerStoredChange()
    {
        var grudger = Npc("npc_p_g", "Grudger");
        Remember(grudger, MemoryType.Attacked, "Bob", DateTime.Now.AddMinutes(-5));
        Npc("npc_p_s", "Smith");
        Npc("npc_p_b", "Baker");
        await _db.SaveWorldState(OnlineStateManager.KEY_NPCS, RosterJson());
        WorldEditLog.OwnerOverride = false;
        var dbA = new SqlSaveBackend(_path);
        var osmA = await DoorLogin(dbA);   // A holds v1
        long v1 = osmA.NpcsVersion!.Value;
        Find("Baker")!.Level = 42;         // A's own unsaved change

        // B takes Smith's gear and saves v2
        await OtherWriter(r => { var s = r.Single(d => d.Name == "Smith"); s.EquippedItems[(int)EquipmentSlot.MainHand] = 4242; s.Gold = 777; });

        int writes = 0;
        await PermadeathHelper.ForgetCharacterInNpcWorldAsync(dbA, new[] { "Bob" }, DateTime.Now, true,
            beforeWrite: () => { writes++; return Task.CompletedTask; });

        writes.Should().Be(2, "the write under v1 failed; the retry over B's roster landed");
        dbA.GetWorldStateVersion(OnlineStateManager.KEY_NPCS).Should().Be(v1 + 2);
        var stored = await StoredRoster();
        stored.Single(d => d.Name == "Smith").EquippedItems.Should().ContainKey((int)EquipmentSlot.MainHand).WhoseValue.Should().Be(4242, "B's change survives");
        stored.Single(d => d.Name == "Smith").Gold.Should().Be(777);
        stored.Single(d => d.Name == "Grudger").Memories.Should().NotContain(m => m.InvolvedCharacter == "Bob", "the clean-up was applied again");
        stored.Single(d => d.Name == "Baker").Level.Should().Be(42, "A's own unsaved change was laid over B's roster");
        osmA.NpcsVersion.Should().Be(v1 + 2);
        OnlineStateManager.LiveRosterVersion.Should().Be(v1 + 2);
    }

    /// <summary>Four NPCs stored and held; the grudge is on the first.</summary>
    private async Task<List<NPC>> FourStored()
    {
        var all = new List<NPC> { Npc("npc_r_1", "Grudger"), Npc("npc_r_2", "Smith"), Npc("npc_r_3", "Baker"), Npc("npc_r_4", "Miller") };
        Remember(all[0], MemoryType.Attacked, "Bob", DateTime.Now.AddMinutes(-5));
        await _db.SaveWorldState(OnlineStateManager.KEY_NPCS, RosterJson());
        OnlineStateManager.NoteRosterRestored(_db.GetWorldStateVersion(OnlineStateManager.KEY_NPCS));
        WorldEditLog.OwnerOverride = true;   // the MUD: its own logins rebuild the roster on other tasks
        return all;
    }

    [Fact]
    public async Task APurge_StartedMidRebuild_WaitsAndWritesTheWholeRoster()
    {
        var all = await FourStored();
        long v = _db.GetWorldStateVersion(OnlineStateManager.KEY_NPCS);
        using var holding = new ManualResetEventSlim();
        using var finish = new ManualResetEventSlim();
        // a login's RestoreNPCs on another thread, half done: it holds the roster lock
        var rebuild = new Thread(() =>
        {
            lock (OnlineStateManager.RosterLock)
            {
                NPCSpawnSystem.Instance.IsRebuilding = true;
                NPCSpawnSystem.Instance.ActiveNPCs.Clear();
                NPCSpawnSystem.Instance.ActiveNPCs.Add(all[0]);
                holding.Set();
                finish.Wait();
                NPCSpawnSystem.Instance.ActiveNPCs.AddRange(all.Skip(1));
                NPCSpawnSystem.Instance.IsRebuilding = false;
            }
        });
        rebuild.IsBackground = true;
        rebuild.Start();
        holding.Wait();

        var purge = Task.Run(() => PermadeathHelper.ForgetCharacterInNpcWorldAsync(_db, new[] { "Bob" }, DateTime.Now, true));
        bool doneEarly;
        long versionMidRebuild;
        try
        {
            await Task.Delay(300);
            doneEarly = purge.IsCompleted;
            versionMidRebuild = _db.GetWorldStateVersion(OnlineStateManager.KEY_NPCS);
        }
        finally
        {
            finish.Set();
            rebuild.Join();
        }
        doneEarly.Should().BeFalse("the purge waits for the rebuild");
        versionMidRebuild.Should().Be(v, "nothing was written from the half-built roster");
        (await purge).Should().BeGreaterThan(0);

        var stored = await StoredRoster();
        stored.Select(d => d.Name).Should().BeEquivalentTo(new[] { "Grudger", "Smith", "Baker", "Miller" }, "the whole roster was written");
        stored.Single(d => d.Name == "Grudger").Memories.Should().NotContain(m => m.InvolvedCharacter == "Bob");
    }

    [Fact]
    public async Task APurge_NeverWritesAPartialRoster_EvenWithoutTheLock()
    {
        var all = await FourStored();
        long v = _db.GetWorldStateVersion(OnlineStateManager.KEY_NPCS);
        // a rebuild marked in flight and a roster at a quarter of the stored size, the lock not held
        NPCSpawnSystem.Instance.IsRebuilding = true;
        NPCSpawnSystem.Instance.ActiveNPCs.RemoveAll(n => n != all[0]);
        var purge = PermadeathHelper.ForgetCharacterInNpcWorldAsync(_db, new[] { "Bob" }, DateTime.Now, true, rosterWaitMs: 2000);
        await Task.Delay(200);
        NPCSpawnSystem.Instance.IsRebuilding = false;   // done, but still partial: the purge keeps waiting, then gives up
        (await purge).Should().Be(0, "the clean-up is left to the world edit log");

        _db.GetWorldStateVersion(OnlineStateManager.KEY_NPCS).Should().Be(v, "a partial roster is never written");
        (await StoredRoster()).Should().HaveCount(4);
        all[0].Brain!.Memory.AllMemories.Should().Contain(m => m.InvolvedCharacter == "Bob", "no clean-up ran on the partial roster");
    }

    // ─── 5 and 6. A door's retried save ───

    [Fact]
    public async Task AnNpcOnlyTheEditTouched_KeepsTheOtherWritersChange()
    {
        var x = Npc("npc_s_x", "Xander");
        Remember(x, MemoryType.Attacked, "Bob", DateTime.Now.AddMinutes(-5));
        Npc("npc_s_s", "Smith");
        await _db.SaveWorldState(OnlineStateManager.KEY_NPCS, RosterJson());
        WorldEditLog.OwnerOverride = false;
        var dbA = new SqlSaveBackend(_path);
        var osmA = await DoorLogin(dbA);
        Find("Smith")!.Level = 42;   // A changed Smith, never Xander

        // B deletes Bob (the edit) and saves Xander with new gold and without the grudge
        WorldEditLog.AppendForgetCharacter(_db, new[] { "Bob" }, "bob_account", DateTime.Now, true);
        await OtherWriter(r => { var d = r.Single(n => n.Name == "Xander"); d.Gold = 777; d.Memories.RemoveAll(m => m.InvolvedCharacter == "Bob"); });

        (await osmA.SaveSharedNPCsVersionedAsync(dbA, OnlineStateManager.SerializeCurrentNPCs())).Should().BeTrue();

        var stored = await StoredRoster();
        stored.Single(d => d.Name == "Xander").Gold.Should().Be(777, "the edit A applied to Xander is no change of A's");
        stored.Single(d => d.Name == "Xander").Memories.Should().NotContain(m => m.InvolvedCharacter == "Bob");
        stored.Single(d => d.Name == "Smith").Level.Should().Be(42);
    }

    [Fact]
    public async Task ANewNpc_SurvivesARetry_AndAnNpcDeletedElsewhere_StaysDeleted()
    {
        Npc("npc_n_s", "Smith");
        Npc("npc_n_b", "Baker");
        await _db.SaveWorldState(OnlineStateManager.KEY_NPCS, RosterJson());
        WorldEditLog.OwnerOverride = false;
        var dbA = new SqlSaveBackend(_path);
        var osmA = await DoorLogin(dbA);
        Npc("npc_n_child", "Graduate");   // a child graduates in A
        Find("Baker")!.Level = 55;        // and A touched Baker too

        await OtherWriter(r => { r.RemoveAll(d => d.Name == "Baker"); r.Single(d => d.Name == "Smith").Gold = 5; });   // B removes Baker

        (await osmA.SaveSharedNPCsVersionedAsync(dbA, OnlineStateManager.SerializeCurrentNPCs())).Should().BeTrue();

        var names = (await StoredRoster()).Select(d => d.Name).ToList();
        names.Should().Contain("Graduate", "an NPC created in this session is appended to the reloaded roster");
        names.Should().NotContain("Baker", "an NPC another writer removed is not brought back");
        (await StoredRoster()).Single(d => d.Name == "Smith").Gold.Should().Be(5);
        Find("Graduate").Should().NotBeNull();
    }

    // ─── 4. The owner's own registry ───

    [Fact]
    public async Task TheOwnersStaleRegistryEntry_IsEnded_ThoughTheReloadedWifeHasNoSpouseName()
    {
        var reg = NPCMarriageRegistry.Instance;
        var wife = Npc("npc_m_w", "Wife");
        wife.SpouseName = "Bob"; wife.Married = true; wife.IsMarried = true;
        Exec("INSERT INTO players (username, display_name, player_data) VALUES ('bob_account', 'Bob', '{\"player\":{\"name2\":\"Bob\",\"id\":\"player_bob_id\"}}');");
        await _db.SaveWorldState(OnlineStateManager.KEY_NPCS, RosterJson());
        await _db.SaveWorldState(OnlineStateManager.KEY_MARRIAGES,
            "{\"marriages\":[{\"npc1Id\":\"player_bob_id\",\"npc2Id\":\"npc_m_w\"},{\"npc1Id\":\"npc_x\",\"npc2Id\":\"npc_y\"}],\"affairs\":[]}");
        OnlineStateManager.NoteRosterRestored(_db.GetWorldStateVersion(OnlineStateManager.KEY_NPCS));

        // the owner process's registry holds the marriage
        reg.RegisterMarriage("player_bob_id", "npc_m_w", "Bob", "Wife");
        var ownerRegistry = reg.GetAllMarriages();

        // a door process, whose registry is its own (doors do not load it), deletes Bob
        reg.RestoreMarriages(null);
        WorldEditLog.OwnerOverride = false;
        await PermadeathHelper.PurgeDeletedCharacterAsync(_db, "bob_account", "Bob");
        var stored = await StoredRoster();
        stored.Single(d => d.Name == "Wife").SpouseName.Should().BeEmpty();
        (await _db.LoadWorldState(OnlineStateManager.KEY_MARRIAGES)).Should().NotContain("npc_m_w").And.Contain("npc_x");

        // the owner, its registry unchanged, reloads the clean roster and re-applies the edit
        reg.RestoreMarriages(ownerRegistry);
        WorldEditLog.OwnerOverride = true;
        await GameEngine.Instance.RestoreNPCs(stored, _db.GetWorldStateVersion(OnlineStateManager.KEY_NPCS));
        Find("Wife")!.SpouseName.Should().BeEmpty("the reloaded wife names no spouse");
        WorldEditLog.Apply(_db, _db.GetWorldEditsToApply()).Should().BeGreaterThan(0);

        reg.GetSpouseId("npc_m_w").Should().BeNull("the edit ended the registry entry by the character's ID");
        reg.GetSpouseId("player_bob_id").Should().BeNull();
        reg.GetAllMarriages().Should().BeEmpty("so the owner republishes no stale marriage");
    }

    // ─── 7 and 8. Purges queued by a web delete ───

    [Fact]
    public async Task AQueuedPurge_CutsOffAtTheDeleteTime_AndKeepsARecreatedCharactersMarriage()
    {
        var grudger = Npc("npc_q_g", "Grudger");
        Remember(grudger, MemoryType.Attacked, "Bob", DateTime.Now.AddMinutes(-20));   // the old Bob
        Remember(grudger, MemoryType.Insulted, "Bob", DateTime.Now.AddMinutes(-3));    // the new Bob
        var wife = Npc("npc_q_w", "Wife");
        wife.SpouseName = "Bob"; wife.Married = true; wife.IsMarried = true;
        NPCMarriageRegistry.Instance.RegisterMarriage("new_bob_id", "npc_q_w", "Bob", "Wife");
        await _db.SaveWorldState(OnlineStateManager.KEY_NPCS, RosterJson());
        OnlineStateManager.NoteRosterRestored(_db.GetWorldStateVersion(OnlineStateManager.KEY_NPCS));
        WorldEditLog.OwnerOverride = true;
        // the web delete, 10 minutes ago, while no other player used the name
        Exec("INSERT INTO pending_purges (username, name2, display_name, deleted_at, player_id, untimed) " +
             "VALUES ('bob_account', 'Bob', 'Bob', datetime('now', '-10 minutes'), 'old_bob_id', 1);");
        // a new Bob, made 5 minutes ago on another account, married Wife
        Exec("INSERT INTO players (username, display_name, player_data, created_at) VALUES ('bob_two', 'Bob', " +
             "'{\"player\":{\"name2\":\"Bob\",\"id\":\"new_bob_id\"}}', datetime('now', '-5 minutes'));");

        (await UsurperRemake.Server.MudServer.DrainPendingPurgesAsync(_db)).Should().Be(1);

        grudger.Brain!.Memory.AllMemories.Where(m => m.InvolvedCharacter == "Bob").Select(m => m.Type)
            .Should().Equal(new[] { MemoryType.Insulted }, "cut off at the delete, not at the drain");
        wife.SpouseName.Should().Be("Bob", "the new Bob's marriage is kept");
        NPCMarriageRegistry.Instance.GetSpouseId("npc_q_w").Should().Be("new_bob_id");

        // and the owner's re-apply of the logged edit keeps them too
        WorldEditLog.Apply(_db, _db.GetWorldEditsToApply());
        grudger.Brain!.Memory.AllMemories.Should().Contain(m => m.InvolvedCharacter == "Bob" && m.Type == MemoryType.Insulted);
        wife.SpouseName.Should().Be("Bob");
        NPCMarriageRegistry.Instance.GetSpouseId("npc_q_w").Should().Be("new_bob_id");
    }

    [Fact]
    public async Task AQueuedPurge_KeepsUntimedFields_WhenTheNameWasInUseAtTheDelete()
    {
        var rival = Npc("npc_q_r", "Rival");
        rival.Enemies.Add("Bob");
        await _db.SaveWorldState(OnlineStateManager.KEY_NPCS, RosterJson());
        OnlineStateManager.NoteRosterRestored(_db.GetWorldStateVersion(OnlineStateManager.KEY_NPCS));
        // another Bob played then (untimed = 0) and has since gone too: no row uses the name at the drain
        Exec("INSERT INTO pending_purges (username, name2, display_name, untimed) VALUES ('bob_account', 'Bob', 'Bob', 0);");

        (await UsurperRemake.Server.MudServer.DrainPendingPurgesAsync(_db)).Should().Be(1);

        rival.Enemies.Should().Contain("Bob", "the delete found the name in use, so untimed entries stay");
    }

    [Fact]
    public async Task AQueuedPurge_PassesTheMarriedDisplayName()
    {
        Npc("npc_q_c", "Commoner");
        await _db.SaveWorldState(OnlineStateManager.KEY_NPCS, RosterJson());
        await WithKing("Bob Smith", 100, async _ =>
        {
            Exec("INSERT INTO pending_purges (username, name2, display_name, untimed) VALUES ('bob_account', 'Bob', 'Bob Smith', 1);");

            (await UsurperRemake.Server.MudServer.DrainPendingPurgesAsync(_db)).Should().Be(1);

            (CastleLocation.GetCurrentKing()?.Name).Should().NotBe("Bob Smith", "the king crowned under the married name was matched");
            using var conn = new SqliteConnection($"Data Source={_path}");
            conn.Open();
            using var cmd = conn.CreateCommand();
            cmd.CommandText = "SELECT payload FROM world_edits WHERE kind = 'vacate_throne';";
            (cmd.ExecuteScalar() as string).Should().Contain("Bob Smith", "and the throne edit was logged");
        });
    }

    [Fact]
    public void TheWebDelete_QueuesEveryNameTheIdAndTheUntimedFinding()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir != null && !Directory.Exists(Path.Combine(dir.FullName, "web"))) dir = dir.Parent;
        var js = File.ReadAllText(Path.Combine(dir!.FullName, "web", "ssh-proxy.js"));
        int start = js.IndexOf("// Fallback: queue the world purge", StringComparison.Ordinal);
        var fallback = js.Substring(start, js.IndexOf("// Kick if online first", start, StringComparison.Ordinal) - start);
        fallback.Should().Contain("json_extract(player_data, '$.player.name2')").And.Contain("json_extract(player_data, '$.player.id')");
        fallback.Should().Contain("INSERT INTO pending_purges (username, name2, display_name, created_by, player_id, untimed)");
        fallback.Should().Contain(".run(playerUsername, who.name2 || null, who.display_name || null, 'admin-web', who.player_id || null,");
        fallback.Should().Contain("ALTER TABLE pending_purges ADD COLUMN");

        var mud = Source("Server", "MudServer.cs");
        mud.Should().Contain("shownName: p.DisplayName, characterId: p.PlayerId, deletedAt: p.DeletedAt, untimedAtDelete: p.Untimed");
    }

    private static string Source(string folder, string file)
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir != null && !Directory.Exists(Path.Combine(dir.FullName, "Scripts"))) dir = dir.Parent;
        return File.ReadAllText(Path.Combine(dir!.FullName, "Scripts", folder, file));
    }
}
