using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using FluentAssertions;
using UsurperRemake;
using UsurperRemake.Systems;
using Xunit;

namespace UsurperReborn.Tests;

/// <summary>
/// 1.3.4: three Cyclebreaker spells do what their text says. Probability Shift takes the target's critical
/// hits away and adds 30 points of miss chance to its attacks for 3 rounds (it no longer confuses and slows);
/// Echo of Tomorrow ignores half the target's armor instead of adding half the armor as flat damage; the text
/// of Paradox Collapse names its bonus of 10 percent of the damage dealt this fight. Every other spell's
/// damage step is unchanged (a fixture taken on 04720f16).
/// </summary>
[Collection("SharedGameSingletons")]
public class CyclebreakerFix134Tests
{
    private const BindingFlags F = BindingFlags.NonPublic | BindingFlags.Instance;
    private const BindingFlags SF = BindingFlags.NonPublic | BindingFlags.Static;
    internal const string OtherSpellsFixture = "Tests/Fixtures/cyclebreaker134-other-spells.txt";

    // ---------- helpers ----------

    private static (CombatEngine engine, MemoryStream output) Engine(Character player, Random rng)
    {
        var output = new MemoryStream();
        var engine = new CombatEngine(new TerminalEmulator(new MemoryStream(), output));
        typeof(CombatEngine).GetField("random", F)!.SetValue(engine, rng);
        typeof(CombatEngine).GetField("currentPlayer", F)!.SetValue(engine, player);
        return (engine, output);
    }

    private static string Text(CombatEngine engine, MemoryStream output)
    {
        var term = (TerminalEmulator)typeof(CombatEngine).GetField("terminal", F)!.GetValue(engine)!;
        term.StreamWriterInternal?.Flush();
        return Regex.Replace(Encoding.UTF8.GetString(output.ToArray()), "\u001b\\[[0-9;]*[A-Za-z]", "");
    }

    /// <summary>Invokes a private engine method, filling any optional parameter past the given ones with its default.</summary>
    private static object? Call(CombatEngine engine, string name, params object?[] args)
    {
        var m = typeof(CombatEngine).GetMethod(name, F)!;
        var all = m.GetParameters().Select((p, i) => i < args.Length ? args[i] : Type.Missing).ToArray();
        try { return m.Invoke(engine, all); }
        catch (TargetInvocationException ex) when (ex.InnerException != null) { throw ex.InnerException; }
    }

    private static readonly (CharacterClass cls, int slots)[] SpellClasses =
    {
        (CharacterClass.Cleric, 25), (CharacterClass.Magician, 25), (CharacterClass.Sage, 25),
        (CharacterClass.Tidesworn, 5), (CharacterClass.Wavecaller, 5), (CharacterClass.Cyclebreaker, 5),
        (CharacterClass.Abysswarden, 5), (CharacterClass.Voidreaver, 5),
    };

    /// <summary>The two spells this piece changes; every other spell must match the fixture.</summary>
    private static bool Changed(CharacterClass cls, int slot) =>
        cls == CharacterClass.Cyclebreaker && (slot == 2 || slot == 3);

    private static Character Caster(CharacterClass cls) => new Character
    {
        Name1 = "cb134", Name2 = "Caster", Class = cls, Race = CharacterRace.Human, Level = 60,
        Intelligence = 60, Wisdom = 60, Strength = 40, HP = 10_000, MaxHP = 10_000,
        Mana = 100_000, MaxMana = 100_000, AI = CharacterAI.Human, CombatSpeed = CombatSpeed.Instant,
    };

    private static Monster FixtureMonster() => new Monster
    {
        Name = "Ogre", Level = 50, HP = 1_000_000, MaxHP = 1_000_000, ArmPow = 60, Strength = 100, Defence = 30,
    };

    /// <summary>The monster's public state after a hit, as one sorted line (the new Probability Shift state left out).</summary>
    private static string Snapshot(Monster m) => string.Join(" ", typeof(Monster)
        .GetProperties(BindingFlags.Public | BindingFlags.Instance)
        .Where(p => p.CanRead && p.GetIndexParameters().Length == 0 && !p.Name.StartsWith("ProbabilityShift", StringComparison.Ordinal))
        .Where(p => p.PropertyType.IsPrimitive || p.PropertyType == typeof(string) || p.PropertyType.IsEnum)
        .OrderBy(p => p.Name, StringComparer.Ordinal)
        .Select(p => { object? v; try { v = p.GetValue(m); } catch { v = "?"; } return p.Name + "=" + v; }));

    /// <summary>One line per spell: its own result on a fixed seed, then the engine's damage step and special
    /// effect on a fixed monster with a fixed engine seed.</summary>
    private static async Task<List<string>> OtherSpellLines()
    {
        var lines = new List<string>();
        foreach (var (cls, slots) in SpellClasses)
        {
            var exec = typeof(SpellSystem).GetMethod("Execute" + cls + "Spell", SF)!;
            for (int slot = 1; slot <= slots; slot++)
            {
                if (Changed(cls, slot)) continue;
                var caster = Caster(cls);
                var r = new SpellSystem.SpellResult { Success = true };
                string line = $"{cls}:{slot}";
                try
                {
                    exec.Invoke(null, new object?[] { caster, slot, null, null, r, new Random(1000 + slot), 1.0f });
                }
                catch (TargetInvocationException ex)
                {
                    lines.Add(line + " cast-throws " + ex.InnerException?.GetType().Name);
                    continue;
                }
                var (engine, _) = Engine(caster, new Random(7));
                var m = FixtureMonster();
                var result = new CombatResult { Player = caster, TotalDamageDealt = 5000 };
                result.Monsters.Add(m);
                string step = "";
                try
                {
                    if (r.Damage > 0)
                        await (Task<bool>)Call(engine, "ApplySingleMonsterDamage", m, r.Damage, result, "fixture", caster, true)!;
                    if (!string.IsNullOrEmpty(r.SpecialEffect) && r.SpecialEffect != "fizzle" && r.SpecialEffect != "fail")
                        Call(engine, "HandleSpecialSpellEffectOnMonster", m, r.SpecialEffect, r.Duration, caster, r.Damage, result);
                }
                catch (Exception ex) { step = " step-throws " + ex.GetType().Name; }
                lines.Add($"{line} dmg={r.Damage} eff={r.SpecialEffect} dur={r.Duration}{step} | {Snapshot(m)}");
            }
        }
        return lines;
    }

    // ---------- Probability Shift ----------

    /// <summary>A Random whose d20 always shows a fixed face and whose other draws come from a seeded Random.</summary>
    private sealed class FixedD20Random : Random
    {
        private readonly Random _inner;
        private readonly int _face;
        public FixedD20Random(int face, int seed) { _face = face; _inner = new Random(seed); }
        public override int Next() => _inner.Next();
        public override int Next(int maxValue) => _inner.Next(maxValue);
        public override int Next(int minValue, int maxValue) => minValue == 1 && maxValue == 21 ? _face : _inner.Next(minValue, maxValue);
        public override double NextDouble() => _inner.NextDouble();
        protected override double Sample() => _inner.NextDouble();
    }

    private static Character Fighter() => new Character
    {
        Name1 = "cb134", Name2 = "Fighter", Class = CharacterClass.Warrior, Race = CharacterRace.Human, Level = 30,
        HP = 100_000, MaxHP = 100_000, Agility = 10, Dexterity = 10, AI = CharacterAI.Human, CombatSpeed = CombatSpeed.Instant,
    };

    private static Monster Brute(bool boss) => new Monster
    {
        Name = "Ogre", Level = 30, HP = 1_000_000, MaxHP = 1_000_000, Strength = 2000, Defence = 20, IsBoss = boss,
    };

    private static void Shift(CombatEngine engine, Monster m, Character caster) =>
        Call(engine, "HandleSpecialSpellEffectOnMonster", m, "probability_shift", 3, caster, 0L, new CombatResult { Player = caster });

    private static int Count(string text, string needle)
    {
        int n = 0, i = 0;
        while ((i = text.IndexOf(needle, i, StringComparison.Ordinal)) >= 0) { n++; i += needle.Length; }
        return n;
    }

    private static string MissLine(Monster m) => Loc.Get("combat.probability_shift_miss", MonsterNames.Display(m));

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void ProbabilityShift_SetsItsOwnStateForThreeRounds_AndNoLongerConfusesOrSlows(bool boss)
    {
        var caster = Caster(CharacterClass.Cyclebreaker);
        var (engine, _) = Engine(caster, new Random(5));
        var m = Brute(boss);
        Shift(engine, m, caster);
        m.IsConfused.Should().BeFalse("Probability Shift no longer confuses");
        m.ConfusedDuration.Should().Be(0);
        m.IsSlowed.Should().BeFalse("Probability Shift no longer slows");
        m.SlowDuration.Should().Be(0);
        m.ProbabilityShiftRounds.Should().Be(3, boss ? "a boss takes it as any monster does" : "the text says 3 rounds");
        m.ProbabilityShiftActive.Should().BeTrue();
    }

    /// <summary>Runs the monster's turn against the player n times (each a new round) on a d20 that always hits
    /// without a natural 20. Returns the shift-miss lines and the turns that took HP.</summary>
    private static async Task<(int shiftMisses, int hits, int n)> PlayerTurns(bool shifted, bool boss, int n = 1200)
    {
        var player = Fighter();
        var (engine, output) = Engine(player, new FixedD20Random(15, 1234));
        var m = Brute(boss);
        if (shifted) Shift(engine, m, Caster(CharacterClass.Cyclebreaker));
        int hits = 0;
        for (int i = 0; i < n; i++)
        {
            if (shifted) m.ProbabilityShiftRounds = 3;
            m.StatusTickedThisRound = false;
            long before = player.HP;
            await (Task)Call(engine, "ProcessMonsterAction", m, player, new CombatResult { Player = player }, null)!;
            if (player.HP < before) hits++;
            player.HP = player.MaxHP;
        }
        return (Count(Text(engine, output), MissLine(m)), hits, n);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task ProbabilityShift_PlayerPath_MissesThirtyPointsMore(bool boss)
    {
        var plain = await PlayerTurns(shifted: false, boss);
        var shift = await PlayerTurns(shifted: true, boss);
        plain.shiftMisses.Should().Be(0, "no shift, no shift misses");
        double missRate = (double)shift.shiftMisses / shift.n;
        missRate.Should().BeInRange(0.25, 0.35, "30 points of every roll that hit miss under Probability Shift");
        double hitRatio = (double)shift.hits / plain.hits;
        hitRatio.Should().BeInRange(0.62, 0.78, "the attacks that land are about 70 percent of the unshifted ones");
    }

    private static async Task<(string text, long lost)> NaturalTwentyTurn(bool shifted)
    {
        var player = Fighter();
        // the d20 shows 20 and every percent draw is 99: no shift miss, no dodge, no ability
        var (engine, output) = Engine(player, new AlwaysHighRandom());
        var m = Brute(false);
        if (shifted) Shift(engine, m, Caster(CharacterClass.Cyclebreaker));
        m.StatusTickedThisRound = false;
        await (Task)Call(engine, "ProcessMonsterAction", m, player, new CombatResult { Player = player }, null)!;
        return (Text(engine, output), player.MaxHP - player.HP);
    }

    /// <summary>A Random whose d20 comes up 20 and whose chance rolls all fail (no dodge, no ability, no shift miss).</summary>
    private sealed class AlwaysHighRandom : Random
    {
        public override int Next() => int.MaxValue - 1;
        public override int Next(int maxValue) => Math.Max(0, maxValue - 1);
        public override int Next(int minValue, int maxValue) => maxValue - 1;
        public override double NextDouble() => 0.999;
        protected override double Sample() => 0.999;
    }

    [Fact]
    public async Task ProbabilityShift_ANaturalTwenty_IsAPlainHit_NotACritical()
    {
        var m = Brute(false);
        string crit = Loc.Get("combat.monster_critical", MonsterNames.Display(m));
        var plain = await NaturalTwentyTurn(shifted: false);
        plain.text.Should().Contain(crit, "without the shift a natural 20 is a critical hit");
        var shift = await NaturalTwentyTurn(shifted: true);
        shift.text.Should().NotContain(crit, "under Probability Shift the critical chance is 0");
        shift.lost.Should().BeGreaterThan(0, "the natural 20 still hits");
        shift.lost.Should().BeLessThan(plain.lost, "a plain hit does less than the double damage of a critical");
    }

    private static async Task<(int shiftMisses, int hits, int n)> CompanionTurns(bool shifted, int n = 1200)
    {
        var leader = Fighter();
        var (engine, output) = Engine(leader, new Random(4321));
        var m = Brute(false);
        if (shifted) Shift(engine, m, Caster(CharacterClass.Cyclebreaker));
        int hits = 0;
        for (int i = 0; i < n; i++)
        {
            var ally = Fighter();
            ally.Name2 = "Ally";
            long before = ally.HP;
            await (Task)Call(engine, "MonsterAttacksCompanion", m, ally, new CombatResult { Player = leader }, null)!;
            if (ally.HP < before) hits++;
        }
        return (Count(Text(engine, output), MissLine(m)), hits, n);
    }

    [Fact]
    public async Task ProbabilityShift_CompanionPath_MissesThirtyPercent()
    {
        var plain = await CompanionTurns(shifted: false);
        plain.shiftMisses.Should().Be(0);
        plain.hits.Should().Be(plain.n, "an unshifted monster's normal attack on an ally always lands here");
        var shift = await CompanionTurns(shifted: true);
        double missRate = (double)shift.shiftMisses / shift.n;
        missRate.Should().BeInRange(0.25, 0.35, "Probability Shift makes 30 percent of the attacks on an ally miss");
        (shift.hits + shift.shiftMisses).Should().Be(shift.n);
    }

    [Fact]
    public async Task ProbabilityShift_EndsAfterThreeRounds()
    {
        var player = Fighter();
        var (engine, _) = Engine(player, new Random(77));
        var m = Brute(false);
        Shift(engine, m, Caster(CharacterClass.Cyclebreaker));
        string Status() => (string)Call(engine, "GetMonsterStatusString", m)!;
        Status().Should().Contain("PSH(3)");
        var active = new List<bool>();
        for (int round = 1; round <= 5; round++)
        {
            m.StatusTickedThisRound = false;
            await (Task)Call(engine, "ProcessMonsterAction", m, player, new CombatResult { Player = player }, null)!;
            active.Add(m.ProbabilityShiftActive);
            player.HP = player.MaxHP;
            if (round < 3) Status().Should().Contain($"PSH({3 - round})");
            else Status().Should().NotContain("PSH");
        }
        active.Should().Equal(true, true, true, false, false);
    }

    /// <summary>Wave Echo doubles against a debuffed target; a target under Probability Shift counted as one before
    /// (through the confusion and slow) and still does.</summary>
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task ProbabilityShift_StillCountsAsADebuff_ForWaveEcho(bool shifted)
    {
        var caster = Caster(CharacterClass.Wavecaller);
        var (engine, output) = Engine(caster, new Random(13));
        var m = new Monster { Name = "Golem", Level = 50, HP = 10_000_000, MaxHP = 10_000_000 };
        if (shifted) Shift(engine, m, Caster(CharacterClass.Cyclebreaker));
        var ability = ClassAbilitySystem.GetAbility("wave_echo")!;
        var r = new ClassAbilityResult { Success = true, AbilityUsed = ability, Damage = 1000, SpecialEffect = "double_vs_debuffed" };
        var result = new CombatResult { Player = caster };
        result.Monsters.Add(m);
        await (Task)Call(engine, "ApplyAbilityEffectsMultiMonster", caster, m, new List<Monster> { m }, r, result)!;
        string resonates = Loc.Get("combat.wave_echo_resonates", MonsterNames.Display(m));
        if (shifted) Text(engine, output).Should().Contain(resonates);
        else Text(engine, output).Should().NotContain(resonates);
    }

    [Theory]
    [InlineData("en")]
    [InlineData("hu")]
    public void ProbabilityShift_MissRow_FitsSeventyNineColumns(string lang)
    {
        var prev = GameConfig.Language;
        try
        {
            GameConfig.Language = lang;
            var player = Fighter();
            var (engine, output) = Engine(player, new Random(1));
            var m = new Monster { Name = "Ancient Dracolich of the Shattered Abyss", Level = 90 };
            Call(engine, "WriteProbabilityShiftMiss", m, player, new CombatResult { Player = player });
            var rows = Text(engine, output).Split('\n').Select(r => r.TrimEnd('\r')).Where(r => r.Length > 0).ToList();
            rows.Should().NotBeEmpty();
            rows.Should().OnlyContain(r => r.Length <= 79);
            string.Join(" ", rows.Select(r => r.Trim())).Should().Contain(MonsterNames.Display(m));
        }
        finally { GameConfig.Language = prev; }
    }

    [Theory]
    [InlineData("es")]
    [InlineData("fr")]
    [InlineData("hu")]
    [InlineData("it")]
    public void ProbabilityShift_MissLine_IsTranslated(string lang)
    {
        var path = Path.Combine(Leftovers1114BTests.RepoRoot(), "Localization", lang + ".json");
        var en = Path.Combine(Leftovers1114BTests.RepoRoot(), "Localization", "en.json");
        string Value(string file) => System.Text.Json.JsonDocument.Parse(File.ReadAllText(file))
            .RootElement.GetProperty("combat.probability_shift_miss").GetString()!;
        Value(path).Should().Contain("{0}").And.NotBe(Value(en));
    }

    // ---------- Echo of Tomorrow ----------

    /// <summary>The HP the target loses from Echo of Tomorrow's damage step for a spell damage d against armor a.</summary>
    private static async Task<long> EchoStep(long d, long a)
    {
        var caster = Caster(CharacterClass.Cyclebreaker);
        var (engine, _) = Engine(caster, new Random(3));
        var m = new Monster { Name = "Golem", Level = 50, HP = 10_000_000, MaxHP = 10_000_000, ArmPow = a };
        var result = new CombatResult { Player = caster };
        await (Task<bool>)Call(engine, "ApplySpellDamageIgnoringArmor", m, d, result, "Echo of Tomorrow", caster, 50)!;
        Call(engine, "HandleSpecialSpellEffectOnMonster", m, "ignore_half_defense", 1, caster, d, result);
        return m.MaxHP - m.HP;
    }

    [Theory]
    [InlineData(100, 1000)]
    [InlineData(100, 5000)]
    [InlineData(300, 401)]
    [InlineData(100, 150)]
    [InlineData(200, 401)]
    public async Task EchoOfTomorrow_AgainstArmorAboveItsDamage_DealsDamageLessHalfTheArmor(long d, long a)
    {
        long lost = await EchoStep(d, a);
        lost.Should().Be(Math.Max(1, d - (a - a / 2)), "half the armor is ignored and the other half still counts");
        long before = Math.Max(1, d - a) + Math.Max(1, a / 2);   // 04720f16: full armor off, then half the armor added
        lost.Should().BeLessThan(before, "the flat bonus of half the armor is gone");
    }

    [Theory]
    [InlineData(500, 2)]
    [InlineData(500, 3)]
    [InlineData(500, 60)]
    [InlineData(500, 61)]
    [InlineData(1000, 999)]
    [InlineData(5000, 4000)]
    public async Task EchoOfTomorrow_AgainstArmorBelowItsDamage_IsUnchangedFrom04720f16(long d, long a)
    {
        long lost = await EchoStep(d, a);
        long before = Math.Max(1, d - a) + Math.Max(1, a / 2);   // 04720f16
        lost.Should().Be(before);
    }

    private static void ArmSpellWeapon(Character c)
    {
        if (SpellSystem.HasRequiredSpellWeapon(c)) return;
        foreach (var w in EquipmentDatabase.GetAll().Where(e => e.Slot == EquipmentSlot.MainHand))
        {
            c.EquippedItems[EquipmentSlot.MainHand] = w.Id;
            if (SpellSystem.HasRequiredSpellWeapon(c)) return;
        }
    }

    /// <summary>Casts Echo of Tomorrow through the combat engine until the cast lands; returns the HP lost and the screen.</summary>
    private static async Task<(long lost, string text)> CastEcho(long armor)
    {
        for (int attempt = 0; attempt < 60; attempt++)
        {
            var caster = Caster(CharacterClass.Cyclebreaker);
            ArmSpellWeapon(caster);
            var (engine, output) = Engine(caster, new Random(11));
            var m = new Monster { Name = "Golem", Level = 50, HP = 100_000_000, MaxHP = 100_000_000, ArmPow = armor };
            var monsters = new List<Monster> { m };
            var result = new CombatResult { Player = caster };
            result.Monsters.Add(m);
            var action = new CombatAction { Type = CombatActionType.CastSpell, SpellIndex = 3, TargetIndex = 0 };
            await (Task)Call(engine, "ExecuteSpellMultiMonster", caster, monsters, action, result)!;
            if (m.HP < m.MaxHP) return (m.MaxHP - m.HP, Text(engine, output));
        }
        throw new InvalidOperationException("Echo of Tomorrow never landed in 60 casts");
    }

    [Fact]
    public async Task EchoOfTomorrow_Cast_AgainstHugeArmor_DealsOne()
    {
        var (lost, text) = await CastEcho(10_000_000);
        lost.Should().Be(1, "80-110 scaled damage less half of ten million armor is the floor of 1; it was 1 + 5,000,000");
        var golem = new Monster { Name = "Golem", Level = 50 };
        text.Should().NotContain(Loc.Get("combat.future_echo", MonsterNames.Display(golem), 5_000_000), "no flat bonus line");
    }

    [Fact]
    public async Task EchoOfTomorrow_Cast_ShowsHalfTheArmorAsTheDefence()
    {
        var (lost, text) = await CastEcho(40);
        var match = Regex.Match(text, @"\[(\d+) damage vs (\d+) defense\]");
        match.Success.Should().BeTrue("the hit shows its damage against the defence that counts; screen:\n" + text);
        long d = long.Parse(match.Groups[1].Value);
        long shown = long.Parse(match.Groups[2].Value);
        shown.Should().Be(20, "half of 40 armor is ignored");
        lost.Should().Be(d - 20);
        lost.Should().Be(Math.Max(1, d - 40) + 20, "the same as 04720f16 while the armor is below the damage");
    }

    // ---------- Paradox Collapse ----------

    [Theory]
    [InlineData("en", "plus 10% of all damage you have dealt this fight")]
    [InlineData("es", "más el 10% de todo el daño que has infligido en este combate")]
    [InlineData("fr", "plus 10 % de tous les dégâts que vous avez infligés pendant ce combat")]
    [InlineData("hu", "az ebben a harcban általad okozott összes sebzés 10%-a")]
    [InlineData("it", "più il 10% di tutti i danni che hai inflitto in questo combattimento")]
    public void ParadoxCollapse_TextNamesItsBonus(string lang, string bonus)
    {
        var path = Path.Combine(Leftovers1114BTests.RepoRoot(), "Localization", lang + ".json");
        var text = System.Text.Json.JsonDocument.Parse(File.ReadAllText(path))
            .RootElement.GetProperty("spell.cyclebreaker.5.desc").GetString()!;
        text.Should().Contain(bonus);
        text.Should().Contain("250-350");
        var prev = GameConfig.Language;
        try
        {
            GameConfig.Language = lang;
            SpellSystem.GetSpellInfo(CharacterClass.Cyclebreaker, 5)!.DisplayDescription.Should().Contain(bonus, "the player sees it");
        }
        finally { GameConfig.Language = prev; }
        if (lang == "en")
            SpellSystem.GetSpellInfo(CharacterClass.Cyclebreaker, 5)!.Description.Should().Be(text, "the built-in text matches the English key");
    }

    [Theory]
    [InlineData(0, 0)]
    [InlineData(5000, 500)]
    [InlineData(12345, 1234)]
    public void ParadoxCollapse_Bonus_IsTenPercentOfTheFightsDamage_AsBefore(long dealt, long bonus)
    {
        var caster = Caster(CharacterClass.Cyclebreaker);
        var (engine, _) = Engine(caster, new Random(9));
        var m = new Monster { Name = "Golem", Level = 50, HP = 1_000_000, MaxHP = 1_000_000 };
        var result = new CombatResult { Player = caster, TotalDamageDealt = dealt };
        Call(engine, "HandleSpecialSpellEffectOnMonster", m, "paradox_collapse", 0, caster, 300L, result);
        (m.MaxHP - m.HP).Should().Be(bonus);
    }

    // The spell's own damage (250-350 scaled) and its bonus on a fixed seed are in the other-spells fixture
    // (Cyclebreaker:5), which equals 04720f16.

    // ---------- other spells untouched ----------

    [Fact]
    public async Task OtherSpells_DamageStep_EqualsTheFixtureFrom04720f16()
    {
        var lines = await OtherSpellLines();
        var write = Environment.GetEnvironmentVariable("CB134_WRITE_FIXTURE");
        if (!string.IsNullOrEmpty(write))
        {
            File.WriteAllText(write, string.Join("\n", lines) + "\n");
            return;
        }
        var again = await OtherSpellLines();
        again.Should().Equal(lines, "the fixture lines are deterministic");
        var expected = File.ReadAllText(Path.Combine(Leftovers1114BTests.RepoRoot(), OtherSpellsFixture))
            .Replace("\r\n", "\n").TrimEnd('\n').Split('\n');
        lines.Should().HaveCount(expected.Length);
        for (int i = 0; i < expected.Length; i++)
            lines[i].Should().Be(expected[i], $"spell line {i} is unchanged from 04720f16");
    }
}
