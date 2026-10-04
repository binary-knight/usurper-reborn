using System;
using System.Collections.Concurrent;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text.Json;
using System.Threading.Tasks;
using FluentAssertions;
using Microsoft.Data.Sqlite;
using UsurperRemake;
using UsurperRemake.Locations;
using UsurperRemake.Systems;
using Xunit;

namespace UsurperReborn.Tests;

/// <summary>
/// 1.2.5: what reaches the online mailbox. The nightly world boss notice is no longer mailed and the old
/// ones are purged once; an NPC's throne threat is not mailed, only its outcome; the sacrifice, throne and
/// Auction House mails are in the recipient's language; NPC purchases make one Auction House mail per
/// seller per world-sim day; and mail from "System" older than the cap is deleted, never player mail.
/// </summary>
[Collection("SharedGameSingletons")]
public class MailSpam125Tests : IDisposable
{
    private const BindingFlags Priv = BindingFlags.NonPublic | BindingFlags.Instance;
    private const BindingFlags PrivStatic = BindingFlags.NonPublic | BindingFlags.Static;
    private static readonly string[] Langs = { "en", "es", "fr", "hu", "it" };
    private static readonly string[] NewKeys =
        { "mail.throne_lost", "mail.throne_defended", "mail.sacrifice", "mail.auction_sold", "mail.auction_sold_today" };

    /// <summary>A name as long as the game allows.</summary>
    private static readonly string LongName = new string('W', 30);

    private readonly string _path = Path.Combine(Path.GetTempPath(), $"usurper-mailspam-{Guid.NewGuid():N}.db");
    private readonly SqlSaveBackend _db;

    public MailSpam125Tests() { _db = new SqlSaveBackend(_path); }

    public void Dispose()
    {
        SqliteConnection.ClearAllPools();
        try { File.Delete(_path); } catch { }
    }

    // ---- helpers ----

    private void Exec(string sql, params (string Name, object Value)[] args)
    {
        using var c = new SqliteConnection($"Data Source={_path}"); c.Open();
        using var cmd = c.CreateCommand();
        cmd.CommandText = sql;
        foreach (var (n, v) in args) cmd.Parameters.AddWithValue(n, v);
        cmd.ExecuteNonQuery();
    }

    private long Count(string sql, params (string Name, object Value)[] args)
    {
        using var c = new SqliteConnection($"Data Source={_path}"); c.Open();
        using var cmd = c.CreateCommand();
        cmd.CommandText = sql;
        foreach (var (n, v) in args) cmd.Parameters.AddWithValue(n, v);
        return Convert.ToInt64(cmd.ExecuteScalar());
    }

    private string? Text(string sql)
    {
        using var c = new SqliteConnection($"Data Source={_path}"); c.Open();
        using var cmd = c.CreateCommand();
        cmd.CommandText = sql;
        return cmd.ExecuteScalar() as string;
    }

    private long Mail() => Count("SELECT COUNT(*) FROM messages;");

    /// <summary>A player who logged in just now, in <paramref name="lang"/>.</summary>
    private void Player(string key, string display, string lang = "en") =>
        Exec("INSERT INTO players (username, display_name, player_data, language, last_login) VALUES (@u, @d, @p, @l, datetime('now'));",
            ("@u", key), ("@d", display), ("@p", $"{{\"player\":{{\"name2\":\"{display}\"}}}}"), ("@l", lang));

    private void Row(string from, string to, string type, int daysOld) =>
        Exec("INSERT INTO messages (from_player, to_player, message_type, message, created_at) VALUES (@f, @t, @y, 'hello', datetime('now', @age));",
            ("@f", from), ("@t", to), ("@y", type), ("@age", $"-{daysOld} days"));

    private static readonly FieldInfo FallbackOsm = typeof(OnlineStateManager).GetField("_fallbackInstance", PrivStatic)!;

    /// <summary>Runs <paramref name="body"/> with an online state manager on this database, so news is written.</summary>
    private async Task WithNews(Func<Task> body)
    {
        var before = FallbackOsm.GetValue(null);
        var osm = (OnlineStateManager)Activator.CreateInstance(typeof(OnlineStateManager), Priv, null, new object[] { _db, "mailspam" }, null)!;
        FallbackOsm.SetValue(null, osm);
        try { await body(); }
        finally { FallbackOsm.SetValue(null, before); }
    }

    private sealed class LogCapture : IDisposable
    {
        private static readonly FieldInfo InstanceField = typeof(DebugLogger).GetField("_instance", PrivStatic)!;
        private readonly object? _previous;
        private readonly ConcurrentQueue<string> _queue = new();

        public LogCapture()
        {
            _previous = InstanceField.GetValue(null);
            var logger = (DebugLogger)System.Runtime.CompilerServices.RuntimeHelpers.GetUninitializedObject(typeof(DebugLogger));
            typeof(DebugLogger).GetField("logQueue", Priv)!.SetValue(logger, _queue);
            typeof(DebugLogger).GetField("isEnabled", Priv)!.SetValue(logger, true);
            typeof(DebugLogger).GetField("minimumLevel", Priv)!.SetValue(logger, DebugLogger.LogLevel.Debug);
            InstanceField.SetValue(null, logger);
        }

        public string[] Lines(string text) => _queue.ToArray().Where(l => l.Contains(text)).ToArray();

        public void Dispose() => InstanceField.SetValue(null, _previous);
    }

    // ---- 1. the world boss notice is not mailed ----

    private static readonly JsonSerializerOptions BossJson = new() { PropertyNamingPolicy = JsonNamingPolicy.CamelCase };

    private async Task<WorldBossSchedule> Schedule() =>
        JsonSerializer.Deserialize<WorldBossSchedule>((await _db.LoadWorldState(WorldBossSystem.ScheduleKey))!, BossJson)!;

    private async Task ForceSpawnHour()
    {
        var s = await Schedule();
        s.SpawnUtc = DateTime.UtcNow.AddMinutes(-1);
        await _db.SaveWorldState(WorldBossSystem.ScheduleKey, JsonSerializer.Serialize(s, BossJson));
    }

    private void EndNight(int bossId) =>
        Exec("UPDATE world_bosses SET expires_at = datetime('now', '-1 minute') WHERE id = @id;", ("@id", bossId));

    [Fact]
    public async Task ABossSchedule_AndThreeNights_MailNobody_AndTheNewsKeepsItsLine()
    {
        Player("ranger_key", "Ranger");
        Player("mage_key", "Mage", "hu");
        _db.GetRecentActivePlayers(7).Should().HaveCount(2, "both players are in the notice cohort the old mail went to");
        var sys = new WorldBossSystem();

        await WithNews(async () =>
        {
            await sys.Tick(_db);   // the first schedule
            Count("SELECT COUNT(*) FROM messages WHERE message_type = 'world_boss';").Should().Be(0);
            long news = Count("SELECT COUNT(*) FROM news WHERE category = 'world_boss';");
            news.Should().Be(1, "the scheduled notice still goes to the news");

            for (int night = 1; night <= 3; night++)
            {
                await ForceSpawnHour();
                await sys.Tick(_db);
                var boss = await _db.GetActiveWorldBoss();
                boss.Should().NotBeNull($"night {night} spawned");
                EndNight(boss!.Id);
                await sys.Tick(_db);   // the night ends and the next one is scheduled
                Count("SELECT COUNT(*) FROM messages WHERE message_type = 'world_boss';").Should().Be(0, $"no notice is mailed after night {night}");
                Count("SELECT COUNT(*) FROM news WHERE category = 'world_boss';").Should().BeGreaterThan(news, $"night {night}'s end and the next schedule are in the news");
                news = Count("SELECT COUNT(*) FROM news WHERE category = 'world_boss';");
            }
        });

        var def = UsurperRemake.Data.WorldBossDatabase.GetBossById((await Schedule()).DefinitionId)!;
        Count("SELECT COUNT(*) FROM news WHERE category = 'world_boss' AND message LIKE @n;", ("@n", $"%{def.Name}%"))
            .Should().BeGreaterThan(0, "the notice line names the boss");
        Mail().Should().Be(0, "no mail of any kind");
    }

    // ---- 2. the one-time purge ----

    [Fact]
    public void ThePurge_DeletesWorldBossMailOnly_AndASecondStartDeletesNothing()
    {
        // the constructor is a start: on a fresh database the purge already ran (and found nothing)
        Text($"SELECT value FROM world_state WHERE key = '{SqlSaveBackend.WorldBossMailPurgeMarker}';").Should().Be("0");

        // a database from 1.2.4: the marker is not there yet, and the mailbox holds the nightly notices
        Exec($"DELETE FROM world_state WHERE key = '{SqlSaveBackend.WorldBossMailPurgeMarker}';");
        string[] kept = { "mail", "divine", "auction", "throne_lost", "throne_defended", "team_renamed", "trade", "world_boss_reward", "spouse" };
        for (int day = 0; day < 10; day++) Row("System", "Ranger", "world_boss", day);
        foreach (var type in kept) Row("System", "Ranger", type, 1);
        Row("Mage", "Ranger", "mail", 1);

        using (var log = new LogCapture())
        {
            new SqlSaveBackend(_path);   // the first 1.2.5 start
            Count("SELECT COUNT(*) FROM messages WHERE message_type = 'world_boss';").Should().Be(0);
            Mail().Should().Be(kept.Length + 1, "every other type stays");
            Text($"SELECT value FROM world_state WHERE key = '{SqlSaveBackend.WorldBossMailPurgeMarker}';").Should().Be("10");
            log.Lines("World boss mail purge deleted 10 mail rows").Should().HaveCount(1);

            // a world_boss row written after the purge (an older process still running) survives the next start
            Row("System", "Ranger", "world_boss", 0);
            new SqlSaveBackend(_path);
            Count("SELECT COUNT(*) FROM messages WHERE message_type = 'world_boss';").Should().Be(1, "the purge runs once");
            Mail().Should().Be(kept.Length + 2);
            log.Lines("World boss mail purge ran already").Should().NotBeEmpty("the second start says it ran already");
            _db.PurgeWorldBossMailOnce().Should().Be(-1);
        }
    }

    // ---- 3. throne challenge mail ----

    private static readonly FieldInfo OnlineModeField =
        typeof(UsurperRemake.BBS.DoorMode).GetField("_onlineMode", PrivStatic)!;
    private static readonly FieldInfo SimulatorInstance =
        typeof(WorldSimulator).GetField("_instance", PrivStatic)!;

    /// <summary>A human king of this name on the throne, the world sim's court store on this database.</summary>
    private async Task WithHumanKing(string name, Func<King, Task> body)
    {
        var before = CastleLocation.GetCurrentKing();
        var history = CastleLocation.GetMonarchHistory().ToList();
        var version = OnlineStateManager.RoyalCourtVersion;
        bool loaded = CastleLocation.RoyalCourtLoadedFromShared;
        bool online = (bool)OnlineModeField.GetValue(null)!;
        var simStore = OnlineStateManager.SimCourtStore;
        var simulator = SimulatorInstance.GetValue(null);
        var roster = NPCSpawnSystem.Instance.ActiveNPCs.ToList();
        NPCSpawnSystem.Instance.ActiveNPCs.Clear();
        var king = King.CreateNewKing(name, CharacterAI.Human, CharacterSex.Male);
        king.Treasury = 1000;
        CastleLocation.SetKing(king);
        OnlineStateManager.NoteRoyalCourtVersion(null);
        OnlineModeField.SetValue(null, true);
        OnlineStateManager.SimCourtStore = _db;
        await _db.SaveWorldState("royal_court", JsonSerializer.Serialize(new RoyalCourtSaveData
            { KingName = name, KingAI = (int)CharacterAI.Human, Treasury = 1000, TaxRate = 7 }));
        try { await body(king); }
        finally
        {
            CastleLocation.SetKing(before);
            CastleLocation.SetMonarchHistory(history);
            OnlineStateManager.NoteRoyalCourtVersion(version);
            CastleLocation.RoyalCourtLoadedFromShared = loaded;
            OnlineModeField.SetValue(null, online);
            OnlineStateManager.SimCourtStore = simStore;
            SimulatorInstance.SetValue(null, simulator);
            NPCSpawnSystem.Instance.ActiveNPCs.Clear();
            NPCSpawnSystem.Instance.ActiveNPCs.AddRange(roster);
        }
    }

    private static NPC Challenger(string name, bool strong)
    {
        var npc = new NPC { ID = "npc_" + name, Id = "npc_" + name, Name1 = name, Name2 = name, Level = 30 };
        npc.EnsureSystemsInitialized();
        npc.Brain!.Personality!.Ambition = 0.95f;
        if (strong) { npc.Strength = 100_000; npc.WeapPow = 10_000; npc.Defence = 100_000; npc.MaxHP = 1_000_000; npc.HP = 1_000_000; }
        else { npc.Strength = 1; npc.WeapPow = 1; npc.Defence = 0; npc.MaxHP = 1; npc.HP = 1; }
        NPCSpawnSystem.Instance.ActiveNPCs.Add(npc);
        return npc;
    }

    private static ChallengeSystem Challenges(SqlSaveBackend db)
    {
        var c = (ChallengeSystem)Activator.CreateInstance(typeof(ChallengeSystem), nonPublic: true)!;
        c.SqlBackend = db;
        return c;
    }

    [Fact]
    public async Task AThroneThreat_MailsTheKingNothing()
    {
        Player("king_key", "Rex", "hu");
        await WithHumanKing("Rex", async _ =>
        {
            Challenger("Dorn", strong: true);
            var challenges = Challenges(_db);
            typeof(ChallengeSystem).GetMethod("ProcessThroneChallenge", Priv)!.Invoke(challenges, null);
            typeof(ChallengeSystem).GetField("_pendingChallenge", Priv)!.GetValue(challenges)
                .Should().NotBeNull("the threat was declared (the path that used to mail)");
            Mail().Should().Be(0, "the threat is in the news only");
            await Task.CompletedTask;
        });
    }

    [Fact]
    public async Task ALostThrone_MailsTheKingOnce_InTheKingsLanguage()
    {
        Player("king_key", "Rex", "hu");
        await WithHumanKing("Rex", async king =>
        {
            var dorn = Challenger("Dorn", strong: true);
            typeof(ChallengeSystem).GetMethod("ExecuteNPCThroneChallenge", Priv)!.Invoke(Challenges(_db), new object[] { dorn, king });
            Mail().Should().Be(1);
            Count("SELECT COUNT(*) FROM messages WHERE message_type = 'throne_lost' AND from_player = 'System' AND to_player = 'Rex';").Should().Be(1);
            Text("SELECT message FROM messages;").Should().Be(Loc.GetIn("hu", "mail.throne_lost", "Dorn", 30));
            await Task.CompletedTask;
        });
    }

    [Fact]
    public async Task ADefendedThrone_MailsTheKingOnce_InTheKingsLanguage()
    {
        Player("king_key", "Rex", "hu");
        await WithHumanKing("Rex", async king =>
        {
            var dorn = Challenger("Dorn", strong: false);
            typeof(ChallengeSystem).GetMethod("ExecuteNPCThroneChallenge", Priv)!.Invoke(Challenges(_db), new object[] { dorn, king });
            Mail().Should().Be(1);
            Count("SELECT COUNT(*) FROM messages WHERE message_type = 'throne_defended' AND from_player = 'System' AND to_player = 'Rex';").Should().Be(1);
            Text("SELECT message FROM messages;").Should().Be(Loc.GetIn("hu", "mail.throne_defended", "Dorn", 30));
            await Task.CompletedTask;
        });
    }

    // ---- 4. sacrifice and auction mail ----

    [Fact]
    public async Task TheSacrificeMail_IsInTheGodsLanguage()
    {
        Player("god_key", "Divine", "hu");
        await _db.SendMessageToKeyLocalized("Temple", "god_key", "divine", lang => Loc.GetIn(lang, "mail.sacrifice", "Ranger", "1,000", 5));
        Text("SELECT message FROM messages WHERE to_player = 'Divine';").Should().Be(Loc.GetIn("hu", "mail.sacrifice", "Ranger", "1,000", 5));
    }

    /// <summary>A moment of the world-sim day that starts at a 7 PM Eastern reset, <paramref name="minutes"/> from it.</summary>
    private static DateTime FromReset(int minutes)
    {
        var reset = DailySystemManager.ResetBoundaryAt(new DateTime(2026, 10, 5, 12, 0, 0, DateTimeKind.Utc));
        return reset.AddMinutes(minutes);
    }

    [Fact]
    public async Task TwoSalesInOneDay_AreOneMail_AndTheNextDayStartsAnother()
    {
        Player("seller_key", "Seller");
        long first = await _db.MailAuctionSale("Seller", "Iron Sword", "Brenna", 100, FromReset(60));
        long second = await _db.MailAuctionSale("seller_key", "Oak Shield", "Dorn", 250, FromReset(120));
        second.Should().Be(first, "the second sale rewrites the day's mail");
        Count("SELECT COUNT(*) FROM messages WHERE message_type = 'auction';").Should().Be(1);
        Text("SELECT message FROM messages;").Should().Be(Loc.GetIn("en", "mail.auction_sold_today", 2, $"{350:N0}", "Oak Shield", "Dorn", $"{250:N0}"));
        Text("SELECT to_player FROM messages;").Should().Be("Seller");

        // read, then a third sale the same day: the same row, unread again
        Exec("UPDATE messages SET is_read = 1;");
        (await _db.MailAuctionSale("Seller", "Bow", "Brenna", 50, FromReset(23 * 60))).Should().Be(first);
        Count("SELECT COUNT(*) FROM messages WHERE is_read = 0;").Should().Be(1);
        Text("SELECT message FROM messages;").Should().Contain("3").And.Contain($"{400:N0}");

        // the next world-sim day: a second mail, a single sale again
        long next = await _db.MailAuctionSale("Seller", "Axe", "Dorn", 70, FromReset(24 * 60 + 1));
        next.Should().NotBe(first);
        Count("SELECT COUNT(*) FROM messages WHERE message_type = 'auction';").Should().Be(2);
        Text($"SELECT message FROM messages WHERE id = {next};").Should().Be(Loc.GetIn("en", "mail.auction_sold", "Axe", "Dorn", $"{70:N0}"));
    }

    [Fact]
    public async Task TheAuctionDay_TurnsAtTheWorldSimReset()
    {
        Player("seller_key", "Seller", "hu");
        SqlSaveBackend.AuctionMailDay(FromReset(-1)).Should().NotBe(SqlSaveBackend.AuctionMailDay(FromReset(1)));
        SqlSaveBackend.AuctionMailDay(FromReset(1)).Should().Be(SqlSaveBackend.AuctionMailDay(FromReset(24 * 60 - 1)));

        long before = await _db.MailAuctionSale("Seller", "Iron Sword", "Brenna", 100, FromReset(-1));
        long after = await _db.MailAuctionSale("Seller", "Oak Shield", "Dorn", 250, FromReset(1));
        after.Should().NotBe(before, "a minute after the reset is the next day");
        long same = await _db.MailAuctionSale("Seller", "Bow", "Dorn", 50, FromReset(24 * 60 - 1));
        same.Should().Be(after, "a minute before the next reset is still the same day");
        Count("SELECT COUNT(*) FROM messages;").Should().Be(2);
        Text($"SELECT message FROM messages WHERE id = {before};").Should().Be(Loc.GetIn("hu", "mail.auction_sold", "Iron Sword", "Brenna", $"{100:N0}"));
        Text($"SELECT message FROM messages WHERE id = {after};").Should().Be(Loc.GetIn("hu", "mail.auction_sold_today", 2, $"{300:N0}", "Bow", "Dorn", $"{50:N0}"));
    }

    // ---- 5. the System mail cap ----

    [Fact]
    public void TheCap_DeletesSystemMailPast30Days_AndNeverPlayerMail()
    {
        GameConfig.SystemMailKeepDays.Should().Be(30);
        Row("System", "Ranger", "throne_lost", 31);
        Row("System", "Ranger", "throne_defended", 29);
        foreach (var lookalike in new[] { "system", "System ", "SYSTEM", " System" })
            Row(lookalike, "Ranger", "mail", 400);
        Row("Mage", "Ranger", "mail", 31);
        Row("Mage", "Ranger", "mail", 400);
        Row("Temple", "Ranger", "divine", 400);

        _db.PruneOldSystemMail(GameConfig.SystemMailKeepDays).Should().Be(1);
        Count("SELECT COUNT(*) FROM messages WHERE from_player = 'System';").Should().Be(1, "the 29 day old one stays");
        Count("SELECT COUNT(*) FROM messages WHERE message_type = 'throne_defended';").Should().Be(1);
        Count("SELECT COUNT(*) FROM messages WHERE from_player != 'System';").Should().Be(7, "no mail from anyone else, a lookalike name included, is deleted");

        // and at startup
        Row("System", "Ranger", "throne_lost", 45);
        new SqlSaveBackend(_path);
        Count("SELECT COUNT(*) FROM messages;").Should().Be(8);
    }

    [Fact]
    public void ADayResetWithNothingHappening_AddsNoMail_AndRunsTheCap()
    {
        Player("ranger_key", "Ranger");
        Row("System", "Ranger", "throne_lost", 40);
        Row("Mage", "Ranger", "mail", 40);
        long before = Mail();
        typeof(WorldSimService).GetMethod("ProcessWorldDailyReset", Priv)!.Invoke(new WorldSimService(_db), null);
        Count("SELECT COUNT(*) FROM messages WHERE from_player = 'System';").Should().Be(0, "the daily reset runs the cap");
        Mail().Should().Be(before - 1, "nothing new was mailed");
        Count("SELECT COUNT(*) FROM messages WHERE from_player = 'Mage';").Should().Be(1);
    }

    // ---- 6. widths and languages ----

    [Fact]
    public void EveryNewMail_FitsTheListAndTheReadView_InEnglishAndHungarian()
    {
        LongName.Length.Should().Be(GameConfig.MaxNameLength);
        string gold = $"{2_000_000_000L:N0}";
        foreach (var lang in new[] { "en", "hu" })
        {
            var rendered = new (string From, string Type, string Text)[]
            {
                ("System", "throne_lost", Loc.GetIn(lang, "mail.throne_lost", LongName, 100)),
                ("System", "throne_defended", Loc.GetIn(lang, "mail.throne_defended", LongName, 100)),
                ("Temple", "divine", Loc.GetIn(lang, "mail.sacrifice", LongName, gold, 999_999)),
                ("Auction House", "auction", Loc.GetIn(lang, "mail.auction_sold", LongName, LongName, gold)),
                ("Auction House", "auction", Loc.GetIn(lang, "mail.auction_sold_today", 999, gold, LongName, LongName, gold)),
            };
            foreach (var (from, type, text) in rendered)
            {
                text.Should().NotContain("{", $"{lang} is rendered");
                // the read view prints the message as it is stored, under its header lines
                var readView = text.Split('\n')
                    .Append(Loc.GetIn(lang, "base.mail_message_from_box", from.ToUpper()))
                    .Append(Loc.GetIn(lang, "base.mail_date", "2026-10-02 23:59:59"))
                    .Append(Loc.GetIn(lang, "base.mail_type", type));
                foreach (var line in readView)
                    line.Length.Should().BeLessThanOrEqualTo(79, $"{lang} read view: {line}");
                string row = BaseLocation.MailboxRow("*", 10, from, "10/02", BaseLocation.MailPreview(text));
                row.Should().NotContain("\n");
                row.Length.Should().BeLessThanOrEqualTo(79, $"{lang} mail list: {row}");
            }
        }
    }

    [Fact]
    public void EveryNewKey_IsTranslated()
    {
        foreach (var key in NewKeys)
            foreach (var lang in Langs.Where(l => l != "en"))
                Loc.GetIn(lang, key).Should().NotBe(Loc.GetIn("en", key), $"{key} in {lang}");
    }
}
