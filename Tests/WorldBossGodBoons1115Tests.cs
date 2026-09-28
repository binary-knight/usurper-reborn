using System;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using FluentAssertions;
using UsurperRemake;
using UsurperRemake.Data;
using UsurperRemake.Systems;
using Xunit;

namespace UsurperReborn.Tests;

/// <summary>
/// 1.2.0: the canon gods' combat boons reach the world boss fight as they reach a monster fight:
/// the damage boons (Solarius against the undead boss, Valorian when wounded), Umbrath's extra
/// critical chance, Judicar's defence on the boss's basic hit, and the potion bonuses with the
/// boss as the foe. Worship goes through the shared singleton and is cleared after.
/// </summary>
[Collection("SharedGameSingletons")]
public class WorldBossGodBoons1115Tests
{
    private static readonly BindingFlags F = BindingFlags.NonPublic | BindingFlags.Instance;
    private static WorldBossDefinition Vareth => WorldBossDatabase.GetBossById("lich_king_vareth")!;
    private static WorldBossDefinition Leviathan => WorldBossDatabase.GetBossById("abyssal_leviathan")!;

    private static Character Hero(string name)
    {
        var c = new Character
        {
            Name1 = name, Name2 = name, AI = CharacterAI.Human, Level = 10, HP = 1000, MaxHP = 1000,
            BaseMaxHP = 1000, BaseStrength = 10, BaseDexterity = 10, BaseConstitution = 10, BaseIntelligence = 20,
            BaseWisdom = 20, BaseCharisma = 10, BaseDefence = 5, BaseStamina = 10, BaseAgility = 10,
            Mental = 80, Class = CharacterClass.Warrior, Race = CharacterRace.Human
        };
        c.RecalculateStats();
        return c;
    }

    private static T WithGod<T>(Character c, string? god, int favor, Func<T> body)
    {
        if (god == null) return body();
        GodRegistry.SetWorshippedGod(c, god).Should().BeTrue();
        c.GodFavor = favor;
        try { return body(); }
        finally { GodRegistry.SetWorshippedGod(c, null); }
    }

    /// <summary>A Random whose Next(n) is fixed and NextDouble is 0.5 (variance exactly 1.0).</summary>
    private sealed class FixedRandom : Random
    {
        private readonly int _next;
        public FixedRandom(int next) { _next = next; }
        public override int Next(int maxValue) => Math.Min(_next, maxValue - 1);
        public override double NextDouble() => 0.5;
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

    // ---------------- The boss as a foe ----------------

    [Fact]
    public void AsFoe_TheLichKingIsUndead_NoOtherBossIsUndeadOrDemon()
    {
        DivineBlessingSystem.IsUndeadOrDemon(WorldBossSystem.AsFoe(Vareth)).Should().BeTrue();
        WorldBossSystem.AsFoe(Vareth).MonsterClass.Should().Be(MonsterClass.Undead);
        foreach (var def in WorldBossDatabase.GetAllBosses().Where(b => b.Id != "lich_king_vareth"))
            DivineBlessingSystem.IsUndeadOrDemon(WorldBossSystem.AsFoe(def)).Should().BeFalse(def.Name);
    }

    // ---------------- Damage boons ----------------

    [Fact]
    public void Solarius_BoostsAWorldBossHit_OnTheLichKing_Only()
    {
        var c = Hero("WbSol");
        var rng = new FixedRandom(99);
        WithGod(c, "Solarius", 60, () => WorldBossSystem.ApplyDivineAttack(c, Vareth, 1000, false, rng, null)).Should().Be(1150);
        WithGod(c, "Solarius", 60, () => WorldBossSystem.ApplyDivineAttack(c, Leviathan, 1000, false, rng, null)).Should().Be(1000, "not undead");
        WithGod(c, null, 0, () => WorldBossSystem.ApplyDivineAttack(c, Vareth, 1000, false, rng, null)).Should().Be(1000, "a non-worshipper");
    }

    [Fact]
    public void Solarius_ReachesTheWorldBossAttack_ThroughCalculatePlayerDamage()
    {
        var c = Hero("WbSolAtk");
        c.Strength = 500; c.WeapPow = 100; c.Dexterity = 1;
        var data = new WorldBossRuntimeData { ScaledStrength = 100, ScaledDefence = 200 };
        var m = typeof(WorldBossSystem).GetMethod("CalculatePlayerDamage", F)!;
        var sys = new WorldBossSystem();
        long Hit(WorldBossDefinition def) => (long)m.Invoke(sys, new object?[] { c, def, data, new FixedRandom(99), true, null })!;
        long raw = 500 + 100 - 100;
        WithGod(c, "Solarius", 60, () => Hit(Vareth)).Should().Be(raw + raw * 15 / 100, "Solarius against the Lich King");
        WithGod(c, "Solarius", 60, () => Hit(Leviathan)).Should().Be(raw);
        WithGod(c, null, 0, () => Hit(Vareth)).Should().Be(raw, "a non-worshipper");
    }

    [Fact]
    public void Valorian_BoostsAWorldBossHit_WhenWounded()
    {
        var c = Hero("WbVal");
        var rng = new FixedRandom(99);
        WithGod(c, "Valorian", 60, () => { c.HP = c.MaxHP / 4; return WorldBossSystem.ApplyDivineAttack(c, Leviathan, 1000, false, rng, null); }).Should().Be(1100);
        WithGod(c, "Valorian", 60, () => { c.HP = c.MaxHP; return WorldBossSystem.ApplyDivineAttack(c, Leviathan, 1000, false, rng, null); }).Should().Be(1000, "not wounded");
        WithGod(c, null, 0, () => { c.HP = c.MaxHP / 4; return WorldBossSystem.ApplyDivineAttack(c, Leviathan, 1000, false, rng, null); }).Should().Be(1000, "a non-worshipper");
    }

    // ---------------- Umbrath's critical chance ----------------

    [Fact]
    public void Umbrath_AddsACriticalChance_ToAWorldBossHit()
    {
        var c = Hero("WbUmb");
        var always = new FixedRandom(0);
        WithGod(c, "Umbrath", 60, () => WorldBossSystem.ApplyDivineAttack(c, Leviathan, 1000, true, always, null)).Should().Be(1500);
        WithGod(c, "Umbrath", 60, () => WorldBossSystem.ApplyDivineAttack(c, Leviathan, 1000, false, always, null)).Should().Be(1000, "already a crit");
        WithGod(c, "Umbrath", 60, () => WorldBossSystem.ApplyDivineAttack(c, Leviathan, 1000, true, new FixedRandom(10), null)).Should().Be(1000, "a roll of 10 misses a 10 percent chance");
        WithGod(c, null, 0, () => WorldBossSystem.ApplyDivineAttack(c, Leviathan, 1000, true, always, null)).Should().Be(1000, "a non-worshipper");
    }

    // ---------------- Judicar's defence ----------------

    private static long BossHitLoss(Character c)
    {
        c.HP = c.MaxHP; c.Defence = 0; c.ArmPow = 0;
        var data = new WorldBossRuntimeData { CurrentPhase = 1, ScaledStrength = 500 };
        var term = new TerminalEmulator(new ScriptedStream(""), new MemoryStream());
        var m = typeof(WorldBossSystem).GetMethod("ProcessBossActions", F)!;
        m.Invoke(new WorldBossSystem(), new object[] { Leviathan, data, c, term, new FixedRandom(99), new WorldBossCombatState(), false });
        return c.MaxHP - c.HP;
    }

    [Fact]
    public void Judicar_CutsTheBossBasicHit()
    {
        var c = Hero("WbJud");
        long plain = WithGod(c, null, 0, () => BossHitLoss(c));
        plain.Should().BeGreaterThan(100);
        WithGod(c, "Judicar", 60, () => BossHitLoss(c)).Should().Be(plain - GodBoonSystem.Bonus(plain, 10), "Judicar's 10 percent defence");
        WithGod(c, "Amara", 60, () => BossHitLoss(c)).Should().Be(plain, "another god's follower");
    }

    // ---------------- Discordia's first action fail ----------------

    private sealed class ZeroDouble : Random { public override double NextDouble() => 0.0; }

    [Fact]
    public void Discordia_TheBossSkipsItsFirstActionOnce_ForAFollower_Only()
    {
        var c = Hero("WbDisc");
        WithGod(c, "Discordia", 60, () => GodBoonSystem.DiscordiaStrikes(c, new ZeroDouble())).Should().BeTrue("a forced roll");
        WithGod(c, null, 0, () => GodBoonSystem.DiscordiaStrikes(c, new ZeroDouble())).Should().BeFalse("a non-worshipper");

        c.HP = c.MaxHP; c.Defence = 0; c.ArmPow = 0;
        var data = new WorldBossRuntimeData { CurrentPhase = 1, ScaledStrength = 500 };
        var output = new MemoryStream();
        var term = new TerminalEmulator(new ScriptedStream(""), output);
        var m = typeof(WorldBossSystem).GetMethod("ProcessBossActions", F)!;
        var state = new WorldBossCombatState { DiscordStruck = true };
        m.Invoke(new WorldBossSystem(), new object[] { Leviathan, data, c, term, new FixedRandom(99), state, false });
        c.HP.Should().Be(c.MaxHP, "the first action fails");
        term.StreamWriterInternal!.Flush();
        Encoding.UTF8.GetString(output.ToArray()).Should().Contain(Loc.Get("combat.discordia_first_action_fails", Leviathan.Name));
        m.Invoke(new WorldBossSystem(), new object[] { Leviathan, data, c, term, new FixedRandom(99), state, false });
        c.HP.Should().BeLessThan(c.MaxHP, "only once");
    }

    [Fact]
    public void Discordia_IsRolledAtTheStartOfTheWorldBossFight()
    {
        var dir = AppContext.BaseDirectory;
        while (dir != null && !File.Exists(Path.Combine(dir, "UsurperReborn.sln"))) dir = Path.GetDirectoryName(dir);
        File.ReadAllText(Path.Combine(dir!, "Scripts/Systems/WorldBossSystem.cs"))
            .Should().Contain("state.DiscordStruck = GodBoonSystem.DiscordiaStrikes(player, rng);");
    }

    // ---------------- Potions ----------------

    private static async Task<long> DrinkOne(Character c, WorldBossDefinition def)
    {
        c.HP = 100; c.Healing = 5;
        var term = new TerminalEmulator(new ScriptedStream("1\n"), new MemoryStream());
        var m = typeof(WorldBossSystem).GetMethod("DrinkHealingPotions", F)!;
        await (Task)m.Invoke(new WorldBossSystem(), new object[] { c, term, def })!;
        c.Healing.Should().Be(4);
        return c.HP - 100;
    }

    [Fact]
    public void Solarius_BoostsAWorldBossPotion_AgainstTheLichKing()
    {
        var c = Hero("WbPot");
        c.MaxHP.Should().Be(1000);
        WithGod(c, "Solarius", 60, () => DrinkOne(c, Vareth).GetAwaiter().GetResult()).Should().Be(345, "300 plus Solarius's 15 percent");
        WithGod(c, "Solarius", 60, () => DrinkOne(c, Leviathan).GetAwaiter().GetResult()).Should().Be(300, "not undead");
        WithGod(c, null, 0, () => DrinkOne(c, Vareth).GetAwaiter().GetResult()).Should().Be(300, "a non-worshipper");
    }
}
