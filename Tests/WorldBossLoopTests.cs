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
/// v1.1.4 ruling 3 in the loop itself: no lock, cooldowns on the row, the boss meets a low-level
/// player at their level, applied damage under the cap, no ability above the ceiling, gear read
/// from equipped slots. The fight is driven through a scripted terminal against real SQLite.
/// </summary>
[Collection("SharedGameSingletons")]
public class WorldBossLoopTests : IDisposable
{
    private static readonly BindingFlags F = BindingFlags.NonPublic | BindingFlags.Instance;
    private readonly string _path;
    private readonly SqlSaveBackend _db;
    private readonly WorldBossSystem _sys = new WorldBossSystem();

    public WorldBossLoopTests()
    {
        _path = Path.Combine(Path.GetTempPath(), $"usurper-wbl-{Guid.NewGuid():N}.db");
        _db = new SqlSaveBackend(_path);
    }

    public void Dispose()
    {
        SqliteConnection.ClearAllPools();
        try { File.Delete(_path); } catch { }
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

    private static Character Hero(int level, long hp, long str = 130, long weap = 50) => new Character
    {
        Name2 = "Hero", Class = CharacterClass.Warrior, Race = CharacterRace.Human, Level = level,
        HP = hp, MaxHP = hp, Strength = str, WeapPow = weap, Defence = 100, ArmPow = 40, Dexterity = 30, Mana = 0, MaxMana = 0,
    };

    private async Task<WorldBossInfo> Spawn(long hp, int level = 40, long strength = 140)
    {
        var def = WorldBossDatabase.GetBossById("abyssal_leviathan")!;
        var data = new WorldBossRuntimeData { DefinitionId = def.Id, CurrentPhase = 1, ScaledLevel = level, ScaledStrength = strength, ScaledDefence = 100, ScaledAgility = 80, AttacksPerRound = 2 };
        int id = await _db.SpawnScheduledWorldBoss(def.Name, level, hp, 3, System.Text.Json.JsonSerializer.Serialize(data), def.Id, DateTime.UtcNow, level, 1);
        return (await _db.GetWorldBossById(id))!;
    }

    private static readonly System.Text.RegularExpressions.Regex Ansi = new("\\[[0-9;]*[A-Za-z]");

    private async Task<string> Fight(Character hero, WorldBossInfo boss, string script)
    {
        var output = new MemoryStream();
        var term = new TerminalEmulator(new ScriptedStream(script), output);
        var m = typeof(WorldBossSystem).GetMethod("RunWorldBossCombat", F)!;
        await (Task)m.Invoke(_sys, new object[] { hero, term, _db, boss })!;
        term.StreamWriterInternal!.Flush();
        return Ansi.Replace(Encoding.UTF8.GetString(output.ToArray()), "");
    }

    [Fact]
    public async Task Retreat_RecordsTheSession_AndTheCooldownGuardsReentry_NoLock()
    {
        var boss = await Spawn(hp: 200_000);
        var hero = Hero(40, 5000);
        var text = await Fight(hero, boss, "A\nA\nA\nR\n\n");
        text.Should().Contain(Loc.Get("world_boss.retreat"));
        text.Should().NotContain("already faced this god", "the per-spawn lock is gone");
        var row = (await _db.GetWorldBossDamageLeaderboard(boss.Id, 5)).Single();
        row.DamageDealt.Should().BeGreaterThan(0).And.BeLessThanOrEqualTo(3 * WorldBossMath.RoundCap(boss.MaxHP), "three attacking rounds under the cap");
        row.Sessions.Should().Be(1);
        row.Rounds.Should().BeGreaterThanOrEqualTo(4, "three attacks and a retreat; a status that stops the hero acting adds rounds that read no input");
        row.PlayerLevel.Should().Be(40);
        _db.GetWorldBossCooldownSeconds(boss.Id, "hero").Should().BeInRange(100, GameConfig.WorldBossRetreatCooldownSeconds);

        var again = await Fight(hero, boss, "A\nR\n\n");
        again.Should().Contain("seconds", "the cooldown line");
        (await _db.GetWorldBossDamageLeaderboard(boss.Id, 5)).Single().Sessions.Should().Be(1, "no fight ran");
    }

    [Fact]
    public async Task Fall_IsNonLethal_WithTheLongerCooldown_AndNoAura()
    {
        var boss = await Spawn(hp: 200_000, strength: 50_000);
        var hero = Hero(40, 400);
        var text = await Fight(hero, boss, "A\nA\nA\nA\nA\nA\n\n");
        text.Should().Contain(Loc.Get("world_boss.struck_you_down", "The Abyssal Leviathan"));
        text.Should().NotContain("presence aura", "the aura is gone");
        hero.HP.Should().Be(100, "healers' safety: a quarter of max HP");
        var row = (await _db.GetWorldBossDamageLeaderboard(boss.Id, 5)).Single();
        row.Sessions.Should().Be(1);
        _db.GetWorldBossCooldownSeconds(boss.Id, "hero").Should().BeInRange(280, GameConfig.WorldBossFallCooldownSeconds);
        using var c = new SqliteConnection($"Data Source={_path};Pooling=true"); c.Open();
        using var cmd = c.CreateCommand(); cmd.CommandText = "SELECT deaths FROM world_boss_damage WHERE boss_id = @id;"; cmd.Parameters.AddWithValue("@id", boss.Id);
        Convert.ToInt32(cmd.ExecuteScalar()).Should().Be(1);
    }

    [Fact]
    public async Task LowLevelHero_MeetsTheBossAtTheirLevel_AndLandsAFullWound()
    {
        var boss = await Spawn(hp: 200_000);
        var lowbie = Hero(15, 300, str: 60, weap: 25);
        var text = await Fight(lowbie, boss, "A\nR\n\n");
        text.Should().Contain(Loc.Get("world_boss.meets_at_level"));
        var row = (await _db.GetWorldBossDamageLeaderboard(boss.Id, 5)).Single();
        // native: 85 minus half of the boss's defence scaled by r (100 x 0.37 / 2 = 18) = 67, x0.7..1.3 (x1.5 crit), divided by r = 0.37
        row.DamageDealt.Should().BeGreaterThanOrEqualTo((long)(67 * 0.7 / 0.38), "a level 15 lands like an at-level fighter");
        row.DamageDealt.Should().BeLessThanOrEqualTo(WorldBossMath.RoundCap(boss.MaxHP));
        lowbie.HP.Should().BeGreaterThan(0, "the boss's strength met them at their level too");
    }

    [Fact]
    public void NoLandedTelegraph_TakesMoreThanTheCeiling_AndAHealChannelTakesNothingFromThePlayer()
    {
        // v1.1.5: the abilities are telegraphs; a landing is a share of the player's max HP, never more than the 30 percent ceiling
        var hero = Hero(40, 1000);
        var def = WorldBossDatabase.GetBossById("shadowlord_malachar")!;
        var mine = new WorldBossPlayerTelegraphState(1, 0, 0, "", 0);
        var term = new TerminalEmulator(new MemoryStream(), new MemoryStream());
        _sys.ApplyLandedTelegraph(new WorldBossTelegraphOutcome(1, "Soul Drain", "strike", "landed"), mine, def, hero, term, new Random(1));
        hero.HP.Should().Be(700, "30 percent of max HP is the ceiling and the unanswered cost");
        _sys.ApplyLandedTelegraph(new WorldBossTelegraphOutcome(2, "Dark Pact", "channel", "landed"), mine, def, hero, term, new Random(1));
        hero.HP.Should().Be(700, "a heal channel that lands heals the pool in the tick and costs the player nothing");
        GameConfig.WorldBossTelegraphUnansweredPercent.Should().BeLessThanOrEqualTo(0.30);
    }

    [Fact]
    public void BossSlayer_CountsOnlyWhenEquipped()
    {
        var hero = Hero(40, 1000);
        var bagBlade = new Item { Name = "Bag Blade", LootEffects = new System.Collections.Generic.List<(int, int)> { ((int)LootGenerator.SpecialEffect.BossSlayer, 10) } };
        hero.Inventory.Add(bagBlade);
        WorldBossSystem.HasSpecialEffect(hero, LootGenerator.SpecialEffect.BossSlayer).Should().BeFalse("in the bag, not in a hand");
        var shield = EquipmentDatabase.GetShields().First();
        bool before = shield.HasBossSlayer;
        try
        {
            shield.HasBossSlayer = true;
            hero.EquippedItems[EquipmentSlot.OffHand] = shield.Id;
            WorldBossSystem.HasSpecialEffect(hero, LootGenerator.SpecialEffect.BossSlayer).Should().BeTrue();
        }
        finally { shield.HasBossSlayer = before; }
    }

    [Fact]
    public void PreciseStrike_RollsOneCrit()
    {
        var hero = Hero(40, 1000); hero.Dexterity = 500;
        var def = WorldBossDatabase.GetBossById("abyssal_leviathan")!;
        var data = new WorldBossRuntimeData { DefinitionId = def.Id, ScaledStrength = 140, ScaledDefence = 100 };
        var m = typeof(WorldBossSystem).GetMethod("CalculatePlayerDamage", F)!;
        long raw = 130 + 50 - 50;
        var rng = new Random(3);
        for (int i = 0; i < 300; i++)
        {
            long d = (long)m.Invoke(_sys, new object?[] { hero, def, data, rng, false, null })!;
            d.Should().BeLessThanOrEqualTo((long)(raw * 1.3) + 1, "no crit when the base roll is asked not to crit");
        }
    }
}
