using System;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using FluentAssertions;
using Microsoft.Data.Sqlite;
using UsurperRemake;
using UsurperRemake.Data;
using UsurperRemake.Systems;
using Xunit;

namespace UsurperReborn.Tests;

/// <summary>
/// v1.1.4 ruling 4: settle writes one frozen reward row per qualified player and is idempotent;
/// withdrawal pay is a quarter share once per night; delivery by the owning session flips each
/// row once and is the only path that touches a Character.
/// </summary>
[Collection("SharedGameSingletons")]
public class WorldBossSettleTests : IDisposable
{
    private static readonly BindingFlags F = BindingFlags.NonPublic | BindingFlags.Instance;
    private readonly string _path;
    private readonly SqlSaveBackend _db;
    private readonly WorldBossSystem _sys = new WorldBossSystem();

    public WorldBossSettleTests()
    {
        _path = Path.Combine(Path.GetTempPath(), $"usurper-wbs-{Guid.NewGuid():N}.db");
        _db = new SqlSaveBackend(_path);
    }

    public void Dispose()
    {
        SqliteConnection.ClearAllPools();
        try { File.Delete(_path); } catch { }
    }

    private async Task<WorldBossInfo> Spawn(long hp)
    {
        var def = WorldBossDatabase.GetBossById("abyssal_leviathan")!;
        var data = new WorldBossRuntimeData { DefinitionId = def.Id, CurrentPhase = 1, ScaledLevel = 40, ScaledStrength = 140, ScaledDefence = 100, ScaledAgility = 80, AttacksPerRound = 2 };
        int id = await _db.SpawnScheduledWorldBoss(def.Name, 40, hp, 3, System.Text.Json.JsonSerializer.Serialize(data), def.Id, DateTime.UtcNow, 40, 1);
        return (await _db.GetWorldBossById(id))!;
    }

    private static Character Hero(string name, int level = 40) => new Character
    {
        Name2 = name, Class = CharacterClass.Warrior, Race = CharacterRace.Human, Level = level,
        HP = 5000, MaxHP = 5000, Strength = 130, WeapPow = 50, Defence = 100, ArmPow = 40, Dexterity = 30,
    };

    [Fact]
    public async Task SettleKill_PaysByEffort_OncePerPlayer_AndOnlyOnce()
    {
        long budget = WorldBossMath.PerPlayerBudget(40, 100); // 16,200
        var boss = await Spawn(hp: budget + budget / 2 + 100);
        await _db.RecordWorldBossDamage(boss.Id, "Hero", budget, 40);
        await _db.RecordWorldBossDamage(boss.Id, "Mira", budget / 2, 30);
        var (_, kill, _) = await _db.RecordWorldBossDamage(boss.Id, "Tourist", 100, 20);
        kill.Should().BeTrue();
        var defeated = (await _db.GetWorldBossById(boss.Id))!;

        WorldEventSystem.Instance.ClearAllEvents();
        try
        {
            await _sys.SettleKill(_db, defeated);
            await _sys.SettleKill(_db, defeated);
            var hero = _db.GetUndeliveredWorldBossRewards("hero").Single();
            hero.Kind.Should().Be("kill");
            hero.Xp.Should().Be(WorldBossMath.KillXP(40, 1.0, 2), "two qualified contributors; the tourist's 100 is under the line");
            hero.Gold.Should().Be(WorldBossMath.KillGold(40, 1.0, 2));
            hero.Mvp.Should().BeTrue();
            hero.Rarity.Should().Be((int)LootGenerator.ItemRarity.Epic, "the MVP of two is not Legendary");
            hero.Fame.Should().Be(GameConfig.WorldBossFameQualified + GameConfig.WorldBossFameMvpExtra);
            var mira = _db.GetUndeliveredWorldBossRewards("mira").Single();
            mira.Xp.Should().Be(WorldBossMath.KillXP(30, 0.5, 2));
            mira.Rarity.Should().Be((int)LootGenerator.ItemRarity.Rare);
            mira.Mvp.Should().BeFalse();
            _db.GetUndeliveredWorldBossRewards("tourist").Should().BeEmpty("under the qualifying score");
            (await _db.GetWorldBossById(boss.Id))!.Settled.Should().BeTrue();
            _db.GetUnsettledWorldBossIds().Should().BeEmpty();
            WorldEventSystem.Instance.GlobalXPModifier.Should().BeGreaterThanOrEqualTo(1.1f, "the realm celebrates for a day");
        }
        finally { WorldEventSystem.Instance.ClearAllEvents(); }
    }

    [Fact]
    public async Task TheTick_SettlesADefeatedBossTheKillerLeftBehind()
    {
        var boss = await Spawn(hp: 20_000);
        await _db.RecordWorldBossDamage(boss.Id, "Hero", 20_000, 40);
        _db.GetUnsettledWorldBossIds().Should().ContainSingle();
        WorldEventSystem.Instance.ClearAllEvents();
        try
        {
            await _sys.Tick(_db);
            _db.GetUnsettledWorldBossIds().Should().BeEmpty();
            _db.GetUndeliveredWorldBossRewards("hero").Should().ContainSingle();
        }
        finally { WorldEventSystem.Instance.ClearAllEvents(); }
    }

    [Fact]
    public async Task WithdrawalPay_IsAQuarterShare_OncePerNight()
    {
        long budget = WorldBossMath.PerPlayerBudget(40, 100);
        var boss = await Spawn(hp: 200_000);
        await _db.RecordWorldBossDamage(boss.Id, "Hero", budget / 2, 40);
        await _db.RecordWorldBossDamage(boss.Id, "Tourist", 10, 20);
        (await _db.WithdrawWorldBoss(boss.Id)).Should().BeTrue();
        var withdrawn = (await _db.GetWorldBossById(boss.Id))!;
        await _sys.PayWithdrawal(_db, withdrawn);
        await _sys.PayWithdrawal(_db, withdrawn);
        var row = _db.GetUndeliveredWorldBossRewards("hero").Single();
        row.Kind.Should().Be("withdraw");
        row.Night.Should().Be(1);
        row.Xp.Should().Be(WorldBossMath.WithdrawalXP(40, 0.5, 1));
        row.Rarity.Should().Be(0, "no item for a withdrawal");
        _db.GetUndeliveredWorldBossRewards("tourist").Should().BeEmpty();
    }

    [Fact]
    public async Task Delivery_AppliesOnce_ByTheOwningSession()
    {
        var boss = await Spawn(hp: 20_000);
        await _db.RecordWorldBossDamage(boss.Id, "Hero", 20_000, 40);
        WorldEventSystem.Instance.ClearAllEvents();
        try { await _sys.SettleKill(_db, (await _db.GetWorldBossById(boss.Id))!); }
        finally { WorldEventSystem.Instance.ClearAllEvents(); }
        var row = _db.GetUndeliveredWorldBossRewards("hero").Single();

        var hero = Hero("Hero");
        hero.DisplayName.Should().Be("Hero");
        var term = new TerminalEmulator(new MemoryStream(), new MemoryStream());
        await _sys.DeliverWorldBossRewards(hero, _db, term);
        // Achievements unlocked by a first kill add their own gold, XP, and fame on top, so the
        // reward is asserted as a floor, and the second delivery as a no-op.
        hero.Experience.Should().BeGreaterThanOrEqualTo(row.Xp);
        hero.Gold.Should().BeGreaterThanOrEqualTo(row.Gold);
        hero.Fame.Should().BeGreaterThanOrEqualTo(row.Fame);
        hero.Inventory.Should().ContainSingle("the item is rolled at delivery with the frozen rarity");
        hero.Statistics.WorldBossesKilled.Should().Be(1);
        hero.Statistics.WorldBossMVPCount.Should().Be(1);
        _db.GetUndeliveredWorldBossRewards("hero").Should().BeEmpty();
        var (xpAfter, goldAfter, fameAfter) = (hero.Experience, hero.Gold, hero.Fame);

        await _sys.DeliverWorldBossRewards(hero, _db, term);
        (hero.Experience, hero.Gold, hero.Fame).Should().Be((xpAfter, goldAfter, fameAfter), "delivered once");
        hero.Inventory.Should().ContainSingle();
        hero.Statistics.WorldBossesKilled.Should().Be(1);
    }

    [Fact]
    public async Task RowsAreKeyedByTheLoginName_SoAltsAndMarriedPlayersAreFound()
    {
        // An alt: login name "rage__alt", shown as "Rage". Married after the kill, so the display
        // name changes before delivery; and a full pack, so the item goes to the inheritance queue.
        var boss = await Spawn(hp: 20_000);
        var alt = Hero("Rage"); alt.Name1 = "rage__alt";
        WorldBossSystem.RowKey(alt).Should().Be("rage__alt");
        var output = new MemoryStream();
        var term = new TerminalEmulator(new ScriptedStream("A\nR\n\n"), output);
        _sys.SaveHook = _ => Task.FromResult(true);
        var m = typeof(WorldBossSystem).GetMethod("RunWorldBossCombat", F)!;
        await (Task)m.Invoke(_sys, new object[] { alt, term, _db, boss })!;
        var row = (await _db.GetWorldBossDamageLeaderboard(boss.Id, 5)).Single();
        row.PlayerName.Should().Be("rage__alt", "the login name is the key");
        row.DisplayName.Should().Be("Rage", "the display name rides beside it");
        _db.GetWorldBossCooldownSeconds(boss.Id, "rage__alt").Should().BeGreaterThan(0);

        await _db.RecordWorldBossDamage(boss.Id, "rage__alt", 20_000, 40, "Rage");
        WorldEventSystem.Instance.ClearAllEvents();
        try { await _sys.SettleKill(_db, (await _db.GetWorldBossById(boss.Id))!); }
        finally { WorldEventSystem.Instance.ClearAllEvents(); }
        _db.GetUndeliveredWorldBossRewards("rage__alt").Should().ContainSingle();

        alt.FamilySurname = "Stormborn";
        alt.DisplayName.Should().NotBe("Rage", "a marriage changed the display name");
        for (int i = 0; i < 50; i++) alt.Inventory.Add(new Item { Name = $"junk {i}" });
        await _sys.DeliverWorldBossRewards(alt, _db, new TerminalEmulator(new MemoryStream(), new MemoryStream()));
        alt.Experience.Should().BeGreaterThan(0, "found by the login name after the marriage");
        _db.GetUndeliveredWorldBossRewards("rage__alt").Should().BeEmpty();
        alt.Inventory.Count.Should().Be(50, "the pack was full");
        _db.GetPendingInheritance("rage__alt").Should().ContainSingle("the item waits under the login name, where login delivery looks");
    }

    [Fact]
    public async Task AFailedQueueWrite_PutsTheItemInThePack_RatherThanLosingIt()
    {
        // v1.1.10: the reward row is marked delivered before the item is rolled, so when a full pack
        // sent the item to the inheritance queue and that write failed, the item was gone.
        var boss = await Spawn(hp: 20_000);
        await _db.RecordWorldBossDamage(boss.Id, "Hero", 20_000, 40);
        WorldEventSystem.Instance.ClearAllEvents();
        try { await _sys.SettleKill(_db, (await _db.GetWorldBossById(boss.Id))!); }
        finally { WorldEventSystem.Instance.ClearAllEvents(); }

        var hero = Hero("Hero");
        for (int i = 0; i < 50; i++) hero.Inventory.Add(new Item { Name = $"junk {i}" });
        int saves = 0;
        _sys.SaveHook = _ => { saves++; return Task.FromResult(true); };
        _sys.QueueHook = (_, _, _, _) => false;
        try
        {
            await _sys.DeliverWorldBossRewards(hero, _db, new TerminalEmulator(new MemoryStream(), new MemoryStream()));
        }
        finally { _sys.QueueHook = (db, key, source, json) => db.QueueInheritance(key, source, json); }

        hero.Inventory.Count.Should().Be(51, "the item went into the pack past the soft limit");
        _db.GetPendingInheritance("hero").Should().BeEmpty("nothing was queued");
        _db.GetUndeliveredWorldBossRewards("hero").Should().BeEmpty();
        saves.Should().Be(1, "the save after delivery keeps the item");
    }

    [Fact]
    public async Task Delivery_SavesThePlayerAfterTheFlagFlips_Once()
    {
        var boss = await Spawn(hp: 20_000);
        await _db.RecordWorldBossDamage(boss.Id, "Hero", 20_000, 40, "Hero");
        WorldEventSystem.Instance.ClearAllEvents();
        try { await _sys.SettleKill(_db, (await _db.GetWorldBossById(boss.Id))!); }
        finally { WorldEventSystem.Instance.ClearAllEvents(); }
        var hero = Hero("Hero");
        long xpSeenBySave = -1; int saves = 0;
        _sys.SaveHook = p => { saves++; xpSeenBySave = p.Experience; return Task.FromResult(true); };
        var term = new TerminalEmulator(new MemoryStream(), new MemoryStream());
        await _sys.DeliverWorldBossRewards(hero, _db, term);
        saves.Should().Be(1, "the save follows the delivery in the same method");
        xpSeenBySave.Should().Be(hero.Experience).And.BeGreaterThan(0, "the save sees the reward already applied");
        await _sys.DeliverWorldBossRewards(hero, _db, term);
        saves.Should().Be(1, "nothing delivered, nothing saved");
    }

    private sealed class ScriptedStream : Stream
    {
        private readonly byte[] _data; private int _pos;
        public ScriptedStream(string script) { _data = Encoding.UTF8.GetBytes(script); }
        public override int Read(byte[] buffer, int offset, int count)
        {
            if (_pos >= _data.Length) return 0;
            int n = Math.Min(count, _data.Length - _pos); Array.Copy(_data, _pos, buffer, offset, n); _pos += n; return n;
        }
        public override Task<int> ReadAsync(byte[] buffer, int offset, int count, CancellationToken ct) => Task.FromResult(Read(buffer, offset, count));
        public override bool CanRead => true; public override bool CanSeek => false; public override bool CanWrite => false;
        public override long Length => _data.Length; public override long Position { get => _pos; set => throw new NotSupportedException(); }
        public override void Flush() { }
        public override long Seek(long o, SeekOrigin s) => throw new NotSupportedException();
        public override void SetLength(long v) => throw new NotSupportedException();
        public override void Write(byte[] b, int o, int c) => throw new NotSupportedException();
    }

    [Fact]
    public async Task TheKillingBlow_SettlesAndDeliversInTheSameSession()
    {
        // A real budget: the boss is nearly dead from earlier blows, and the hero's own earlier
        // damage puts them over the qualifying line. One blow (at most the 291 cap) finishes it.
        var boss = await Spawn(hp: WorldBossMath.MaxHP(40, 100));
        await _db.RecordWorldBossDamage(boss.Id, "Other", boss.MaxHP - 5_600, 40);
        await _db.RecordWorldBossDamage(boss.Id, "Hero", 5_550, 40);
        (await _db.GetWorldBossById(boss.Id))!.CurrentHP.Should().Be(50);
        var hero = Hero("Hero");
        var output = new MemoryStream();
        var term = new TerminalEmulator(new ScriptedStream("A\n\n"), output);
        _sys.SaveHook = _ => Task.FromResult(true);
        WorldEventSystem.Instance.ClearAllEvents();
        try
        {
            var m = typeof(WorldBossSystem).GetMethod("RunWorldBossCombat", F)!;
            await (Task)m.Invoke(_sys, new object[] { hero, term, _db, boss })!;
        }
        finally { WorldEventSystem.Instance.ClearAllEvents(); }
        (await _db.GetWorldBossById(boss.Id))!.Status.Should().Be("defeated");
        hero.Experience.Should().BeGreaterThan(0, "settled and delivered in the killer's own session");
        _db.GetUndeliveredWorldBossRewards("hero").Should().BeEmpty();
        _db.GetWorldBossCooldownSeconds(boss.Id, "hero").Should().Be(0, "a kill sets no cooldown");
    }
}
