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
