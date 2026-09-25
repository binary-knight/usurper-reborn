using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using FluentAssertions;
using UsurperRemake;
using UsurperRemake.Systems;
using Xunit;

namespace UsurperReborn.Tests;

/// <summary>
/// v1.1.10: an Old God's dialogue answers promised effects in the fight and delivered almost none.
/// The player's bonuses were applied before the fight and wiped by its start-of-fight reset;
/// penalties, flat bonuses and crit chance were never applied; and the god's damage multiplier was
/// read by nothing. These pin each part: the hook fires after the reset in a real fight, the
/// player's side (with its floor and crit at the roll site), and the god's side with its cap.
/// </summary>
[Collection("SharedGameSingletons")]
public class OldGodDialogueTests
{
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

    private static readonly BindingFlags F = BindingFlags.NonPublic | BindingFlags.Instance;

    private static Character Hero() => new Character
    {
        Name2 = "Hero", Class = CharacterClass.Warrior, Race = CharacterRace.Human, Level = 25,
        HP = 900, MaxHP = 900, BaseMaxHP = 900,
        Strength = 100, BaseStrength = 100, WeapPow = 100, Defence = 50, BaseDefence = 50, ArmPow = 50,
        Dexterity = 30, BaseDexterity = 30, Agility = 25, BaseAgility = 25, Constitution = 30, BaseConstitution = 30,
        Stamina = 100, CombatSpeed = CombatSpeed.Instant,
    };

    /// <summary>Sets the god system's dialogue modifiers for one test and puts them back afterwards.</summary>
    private static void WithModifiers(Action<object> set, Action body)
    {
        var field = typeof(OldGodBossSystem).GetField("activeCombatModifiers", F)!;
        var mods = field.GetValue(OldGodBossSystem.Instance)!;
        var reset = mods.GetType().GetMethod("Reset")!;
        reset.Invoke(mods, null);
        try { set(mods); body(); }
        finally { reset.Invoke(mods, null); }
    }

    private static void Set(object mods, string prop, object value) => mods.GetType().GetProperty(prop)!.SetValue(mods, value);

    private static void ApplyToPlayer(Character p) =>
        typeof(OldGodBossSystem).GetMethod("ApplyModifiersToPlayer", F)!.Invoke(OldGodBossSystem.Instance, new object[] { p });

    private static void ApplyToMonster(Monster m, Character player) =>
        typeof(OldGodBossSystem).GetMethod("ApplyModifiersToMonster", F)!.Invoke(OldGodBossSystem.Instance, new object[] { m, player });

    [Fact]
    public async Task TheDialogueHook_FiresAfterTheFightStartReset_InARealFight()
    {
        var output = new MemoryStream();
        var term = new TerminalEmulator(new ScriptedStream(string.Concat(Enumerable.Repeat("A\n", 10)) + string.Concat(Enumerable.Repeat("P\n", 6))), output);
        var engine = new CombatEngine(term);
        engine.SeedRandomForTests(1114);   // v1.1.14: the same rolls every run
        var hero = Hero();
        hero.TempAttackBonus = 777;          // leftovers the reset must clear before the hook runs
        hero.HasBloodlust = true;
        int? attackSeen = null; bool? bloodlustSeen = null;
        engine.BossContext = new BossCombatContext
        {
            BossData = UsurperRemake.Data.OldGodsData.GetGodBossData(OldGodType.Maelketh),
            ApplyPlayerModifiers = p => { attackSeen = p.TempAttackBonus; bloodlustSeen = p.HasBloodlust; },
        };
        var rat = new Monster { Name = "Sewer Rat", Level = 1, HP = 1, MaxHP = 1, Strength = 1, Defence = 0, Experience = 5, Gold = 3 };
        try { await engine.PlayerVsMonsters(hero, new List<Monster> { rat }, offerMonkEncounter: false); }
        finally { engine.BossContext = null; }

        attackSeen.Should().Be(0, "the hook runs after the reset, so what it applies survives into the fight");
        bloodlustSeen.Should().BeFalse();
    }

    private static Monster God() => new Monster { Name = "Maelketh", Level = 28, HP = 49_500, MaxHP = 49_500, Strength = 1000, WeapPow = 1000, IsBoss = true };

    [Fact]
    public void TheRecklessAnswer_ShortensTheGod_SharpensItsBlows_AndRaisesCrit()
    {
        // Maelketh's "destroy you" answer: +25% damage, -15% defence, 15% crit. Damage and defence are
        // carried on the god, so every attack in the fight follows them (Codex round 3: a bonus on the
        // player's side missed dozens of damage paths).
        var hero = Hero();
        var god = God();
        int critBefore = StatEffectsSystem.CritChance(hero);
        WithModifiers(m => { Set(m, "DamageMultiplier", 1.25); Set(m, "DefenseMultiplier", 0.85); Set(m, "CriticalChance", 0.15); Set(m, "HasRageBoost", true); },
            () => { ApplyToMonster(god, hero); ApplyToPlayer(hero); });

        god.MaxHP.Should().Be(39_600, "+25% damage is the god having 1/1.25 of its HP");
        god.HP.Should().Be(god.MaxHP);
        god.Strength.Should().Be(1_100, "-15% defence is the god hitting 1/0.85 as hard, capped at 1.10 until the gods are retuned");
        hero.TempCritChanceBonus.Should().Be(10, "15% written against the 5% base");
        StatEffectsSystem.CritChance(hero).Should().BeGreaterThan(critBefore, "the roll site reads the bonus");
        hero.HasBloodlust.Should().BeTrue();
    }

    [Fact]
    public void TheCautiousAnswer_SoftensTheGodsBlows()
    {
        // Maelketh's "teach me" answer: +20% defence and the god doing 15% less damage: 0.85 / 1.2
        var hero = Hero();
        var god = God();
        WithModifiers(m => { Set(m, "DefenseMultiplier", 1.20); Set(m, "BossDamageMultiplier", 0.85); }, () => ApplyToMonster(god, hero));
        god.Strength.Should().Be((long)(1000 * 0.85 / 1.20));
        god.MaxHP.Should().Be(49_500, "no damage answer, no HP change");
    }

    [Fact]
    public void WhenAnAnswerChangesTheGodsHP_TheFightSaysWhatItEntersWith()
    {
        var data = UsurperRemake.Data.OldGodsData.GetGodBossData(OldGodType.Maelketh);
        var god = (Monster)typeof(OldGodBossSystem).GetMethod("CreateBossMonster", F)!.Invoke(OldGodBossSystem.Instance, new object[] { data })!;
        WithModifiers(m => Set(m, "DamageMultiplier", 1.25), () => ApplyToMonster(god, Hero()));
        var output = new MemoryStream();
        var term = new TerminalEmulator(new MemoryStream(), output);
        ((bool)typeof(OldGodBossSystem).GetMethod("AnnounceFightHP", BindingFlags.NonPublic | BindingFlags.Static)!.Invoke(null, new object[] { god, data, term })!)
            .Should().BeTrue("the caller pauses on it before the fight clears the screen");
        term.StreamWriterInternal!.Flush();
        Encoding.UTF8.GetString(output.ToArray()).Should().Contain(god.MaxHP.ToString("N0"));
    }

    [Fact]
    public void TheGodsFixedAoEAndChannelDamage_FollowTheAnswerToo()
    {
        // Manwe's Creation's End and Unmake Reality deal fixed damage set for the fight, not damage from
        // his stats (Codex round 4). A defence answer of +20% and a -15% god answer: 0.85 / 1.2.
        var ctx = new BossCombatContext { AoEDamage = 1500, ChannelDamage = 3000, CorruptionDamagePerStack = 70 };
        WithModifiers(m => { Set(m, "DefenseMultiplier", 1.20); Set(m, "BossDamageMultiplier", 0.85); },
            () => typeof(OldGodBossSystem).GetMethod("ApplyDialogueToFixedBossDamage", F)!.Invoke(OldGodBossSystem.Instance, new object[] { ctx, Hero() }));
        ctx.AoEDamage.Should().Be((int)Math.Round(1500 * 0.85 / 1.20));
        ctx.ChannelDamage.Should().Be((int)Math.Round(3000 * 0.85 / 1.20));
        ctx.CorruptionDamagePerStack.Should().Be((int)Math.Round(70 * 0.85 / 1.20), "corruption is fixed damage too (Codex round 5)");
    }

    [Fact]
    public void WhatTheGodSummons_HitsAsTheAnswerSays()
    {
        // Manwe's shadow, the spectral soldiers and other summons get stats of their own when they appear,
        // so the answer's factor is carried on the context and applied to them (Codex round 6).
        var ctx = new BossCombatContext { AoEDamage = 1500 };
        WithModifiers(m => { Set(m, "DefenseMultiplier", 1.20); Set(m, "BossDamageMultiplier", 0.85); },
            () => typeof(OldGodBossSystem).GetMethod("ApplyDialogueToFixedBossDamage", F)!.Invoke(OldGodBossSystem.Instance, new object[] { ctx, Hero() }));
        ctx.DialogueDamageFactor.Should().BeApproximately(0.85 / 1.20, 1e-9);

        var engine = new CombatEngine(new TerminalEmulator(new MemoryStream(), new MemoryStream())) { BossContext = ctx };
        var soldiers = (List<Monster>)typeof(CombatEngine).GetMethod("CreateSpectralSoldiers", F)!.Invoke(engine, new object[] { 1, 50 })!;
        soldiers[0].Strength.Should().Be((long)((10 + 50 * 2) * ctx.DialogueDamageFactor));
        soldiers[0].WeapPow.Should().Be((long)((5 + 50) * ctx.DialogueDamageFactor));

        var unscaled = (List<Monster>)typeof(CombatEngine).GetMethod("CreateSpectralSoldiers", F)!
            .Invoke(new CombatEngine(new TerminalEmulator(new MemoryStream(), new MemoryStream())), new object[] { 1, 50 })!;
        unscaled[0].Strength.Should().Be(110, "outside a god fight nothing changes");
    }

    [Fact]
    public void AFlatAnswer_IsConvertedAgainstTheStatsTheFightUses()
    {
        // A flat +50 damage answer is converted against Strength + WeapPow. A shrine's blessing that the
        // fight's own recalculation will drop must not count (Codex round 4), so the stats are recalculated first.
        var hero = Hero();
        hero.Strength = 205;   // a blessing on top of BaseStrength 100
        var god = God();
        WithModifiers(m => Set(m, "BonusDamage", 50), () => ApplyToMonster(god, hero));
        hero.Strength.Should().NotBe(205, "recalculated before the conversion");
        god.MaxHP.Should().Be((long)Math.Round(49_500 / (1.0 + 50.0 / (hero.Strength + hero.WeapPow))));
    }

    [Theory]
    [InlineData(0.85, 0.85)]   // a softer answer, applied in full
    [InlineData(0.50, 0.50)]   // the softest written, not clamped
    [InlineData(1.25, 1.10)]   // a harsher answer, capped until the gods are retuned
    public void TheGodsDamage_FollowsTheAnswer_AndTheHarshSideIsCapped(double answer, double applied)
    {
        var god = God();
        WithModifiers(m => Set(m, "BossDamageMultiplier", answer), () => ApplyToMonster(god, Hero()));
        god.Strength.Should().Be((long)(1000 * applied));
        god.WeapPow.Should().Be((long)(1000 * applied));
    }
}
