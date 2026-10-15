using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using FluentAssertions;
using UsurperRemake;
using UsurperRemake.Locations;
using UsurperRemake.Systems;
using Xunit;

namespace UsurperReborn.Tests;

/// <summary>
/// 1.3.2 live swing. Four attack effects that only the old single-monster path read now apply on the
/// live player swing (ComputePlayerSwingDamageRolled), once: the divine blessing damage
/// (DivineBlessingSystem.CalculateBonusDamage: the temporary blessing, Solarius, Valorian, the Old God
/// echo), Umbrath's critical chance (under the shared crit ceiling), the Blood Price damage cut and
/// BossSlayer against bosses. Also: divine wrath's damage and defence cut holds for that fight at the
/// size the screen states, the Status Blood Price lines name only what applies, and the Sanctum's
/// ward turns an Evil character away. In memory only.
/// </summary>
[Collection("SharedGameSingletons")]
public class LiveSwing132Tests : IDisposable
{
    private const BindingFlags F = BindingFlags.NonPublic | BindingFlags.Instance;
    private const BindingFlags P = BindingFlags.Public | BindingFlags.Instance;
    private static readonly string[] AllLanguages = { "en", "es", "fr", "hu", "it" };

    // the swing rows read damage with fixed draws, so the to-hit roll is off for them
    private readonly bool _savedHitRoll = GameConfig.PlayerHitRollEnabled;
    public LiveSwing132Tests() { GameConfig.PlayerHitRollEnabled = false; }
    public void Dispose() { GameConfig.PlayerHitRollEnabled = _savedHitRoll; }

    // ---------- harness ----------

    /// <summary>Every draw is fixed: Next(min, max) is min, Next(max) is max - 1, and a percent roll
    /// Next(100) is the given value (99 by default, which no crit chance reaches).</summary>
    private sealed class FixedRandom : Random
    {
        private readonly int _percent;
        public FixedRandom(int percent = 99) { _percent = percent; }
        public override int Next() => 0;
        public override int Next(int maxValue) => maxValue == 100 ? _percent : (maxValue <= 0 ? 0 : maxValue - 1);
        public override int Next(int minValue, int maxValue) => minValue;
        public override double NextDouble() => 0.5;
        protected override double Sample() => 0.5;
    }

    private sealed class Recorder : ICombatObserver
    {
        public readonly List<CombatEvent> Events = new();
        public void OnFightStart(CombatFightStart start) { }
        public void OnEvent(CombatEvent e) { if (!e.IsUse && e.Amount > 0) Events.Add(e); }
        public void OnFightEnd(long playerHpEnd) { }
    }

    private static Character Hero(string name = "Tester", CharacterClass cls = CharacterClass.Warrior,
        CharacterAI ai = CharacterAI.Human, int level = 30)
    {
        var c = new Character
        {
            Name1 = name, Name2 = name, AI = ai, Class = cls, Race = CharacterRace.Human, Sex = CharacterSex.Male,
            Age = 25, Level = level, HP = 5000, MaxHP = 5000, BaseMaxHP = 5000,
            Strength = 120, BaseStrength = 120, Charisma = 20, BaseCharisma = 20,
            Dexterity = 20, BaseDexterity = 20, Agility = 10, BaseAgility = 10,
            Constitution = 30, BaseConstitution = 30, Intelligence = 20, BaseIntelligence = 20,
            Wisdom = 20, BaseWisdom = 20, Stamina = 100, BaseStamina = 100, Mental = 80,
            BonusWeapPow = 40, CombatSpeed = CombatSpeed.Instant,
        };
        c.RecalculateStats();
        c.HP = c.MaxHP;
        return c;
    }

    private static Monster Dummy(MonsterClass cls = MonsterClass.Normal) => new Monster
    {
        Name = "Training Dummy", Level = 30, HP = 1_000_000_000L, MaxHP = 1_000_000_000L, Strength = 1, Defence = 0,
        ArmPow = 0, Experience = 1, Gold = 0, IsActive = true, MonsterClass = cls,
    };

    private static CombatEngine NewEngine(Character player, Random rng, List<Character>? teammates = null, int wrathPct = 0)
    {
        var engine = new CombatEngine(new TerminalEmulator(new MemoryStream(), new MemoryStream()));
        typeof(CombatEngine).GetField("random", F)!.SetValue(engine, rng);
        typeof(CombatEngine).GetField("currentPlayer", F)!.SetValue(engine, player);
        typeof(CombatEngine).GetField("currentTeammates", F)!.SetValue(engine, teammates ?? new List<Character>());
        if (wrathPct != 0) SetWrath(engine, wrathPct);
        return engine;
    }

    /// <summary>The fight's divine wrath cut, as the dungeon sets it on the engine before the fight.</summary>
    private static void SetWrath(CombatEngine engine, int pct)
    {
        var prop = typeof(CombatEngine).GetProperty("DivineWrathPct", P);
        prop.Should().NotBeNull("the engine carries the fight's divine wrath cut");
        prop!.SetValue(engine, pct);
    }

    /// <summary>One live main-hand swing of the player, on a fresh engine with fixed draws.</summary>
    private static long Swing(Character player, Monster? target = null, int percent = 99, int wrathPct = 0) =>
        (long)typeof(CombatEngine).GetMethod("ComputePlayerSwingDamage", F)!
            .Invoke(NewEngine(player, new FixedRandom(percent), wrathPct: wrathPct), new object[] { player, target ?? Dummy(), false, 0, 1 })!;

    /// <summary>Worship a Temple god through the shared singleton (the swing passes no GodSystem), cleared after.</summary>
    private static T WithGod<T>(Character c, string god, int favor, Func<T> body)
    {
        GodRegistry.SetWorshippedGod(c, god).Should().BeTrue();
        c.GodFavor = favor;
        try { return body(); }
        finally { GodRegistry.SetWorshippedGod(c, null); c.GodFavor = 0; }
    }

    private static string RepoRoot()
    {
        var dir = AppContext.BaseDirectory;
        while (dir != null && !File.Exists(Path.Combine(dir, "UsurperReborn.sln"))) dir = Path.GetDirectoryName(dir);
        return dir!;
    }

    private static string Source(string file) => File.ReadAllText(Path.Combine(RepoRoot(), file));

    private static string MethodBody(string src, string signature)
    {
        int start = src.IndexOf(signature, StringComparison.Ordinal);
        start.Should().BeGreaterThan(0, signature);
        int brace = src.IndexOf('{', start);
        int depth = 0;
        for (int i = brace; i < src.Length; i++)
        {
            if (src[i] == '{') depth++;
            else if (src[i] == '}' && --depth == 0) return src.Substring(brace, i - brace + 1);
        }
        return src.Substring(brace);
    }

    private static int Count(string text, string what) => Regex.Matches(text, Regex.Escape(what)).Count;

    // ---------- 1. divine blessing damage on the live swing ----------

    [Fact]
    public void Solarius_LiveSwingOnUndead_AddsTheBoonOnce()
    {
        long plain = Swing(Hero(), Dummy(MonsterClass.Undead));
        var c = Hero("Sol");
        WithGod(c, "Solarius", 60, () =>
        {
            long bonus = DivineBlessingSystem.Instance.CalculateBonusDamage(c, Dummy(MonsterClass.Undead), (int)plain);
            bonus.Should().BeGreaterThan(0, "a Zealot of Solarius has the undead boon");
            Swing(c, Dummy(MonsterClass.Undead)).Should().Be(plain + bonus, "the boon adds once on the live swing");
            Swing(c, Dummy()).Should().Be(Swing(Hero(), Dummy()), "a living foe takes no Solarius bonus");
            return 0;
        });
    }

    [Fact]
    public void Valorian_LiveSwingBelowHalfHp_AddsTheBoonOnce()
    {
        var hurt = Hero();
        hurt.HP = hurt.MaxHP / 4;
        long plain = Swing(hurt);
        var c = Hero("Val");
        c.HP = c.MaxHP / 4;
        WithGod(c, "Valorian", 60, () =>
        {
            long bonus = DivineBlessingSystem.Instance.CalculateBonusDamage(c, Dummy(), (int)plain);
            bonus.Should().BeGreaterThan(0);
            Swing(c).Should().Be(plain + bonus);
            c.HP = c.MaxHP;
            Swing(c).Should().Be(Swing(Hero()), "at full HP Valorian adds nothing");
            return 0;
        });
    }

    [Fact]
    public void OldGodEcho_LiveSwing_AddsOnceAgainstTheEchoedOldGod()
    {
        Monster OldGod() { var m = Dummy(); m.Name = "Maelketh"; m.OldGod = OldGodType.Maelketh; return m; }
        long plain = Swing(Hero(), OldGod());
        var c = Hero("Echo");
        WithGod(c, "Valorian", 60, () =>
        {
            long echo = OldGodEchoSystem.BonusDamage(c, OldGod(), plain);
            echo.Should().BeGreaterThan(0, "Valorian echoes Maelketh for a Zealot");
            Swing(c, OldGod()).Should().Be(plain + echo, "the echo counts once on a weapon swing");
            return 0;
        });
    }

    [Fact]
    public void TemporaryBlessing_LiveSwing_AddsItsDamageBonus()
    {
        long plain = Swing(Hero());
        var c = Hero("Fury");
        var dict = (System.Collections.IDictionary)typeof(DivineBlessingSystem).GetField("temporaryBlessings", F)!
            .GetValue(DivineBlessingSystem.Instance)!;
        WithGod(c, "Umbrath", 0, () =>
        {
            dict[c.Name2] = new TemporaryBlessing
            {
                PlayerName = c.Name2, GodName = "Umbrath", Name = "Fury", DamageBonus = 12,
                GrantedAt = DateTime.Now, ExpiresAt = DateTime.Now.AddMinutes(30),
            };
            try
            {
                Swing(c).Should().Be(plain + (int)(plain * 12 / 100f), "a sacrifice blessing's damage bonus reads on the live swing");
            }
            finally { dict.Remove(c.Name2); }
            Swing(c).Should().Be(plain, "without the blessing the swing is plain");
            return 0;
        });
    }

    // ---------- 2. Umbrath's crit on the live swing ----------

    [Fact]
    public void Umbrath_LiveSwing_AddsCritChanceInTheCritRoll()
    {
        var plainHero = Hero();
        int baseChance = StatEffectsSystem.CritChance(plainHero);
        long noCrit = Swing(plainHero, percent: 99);
        long crit = Swing(plainHero, percent: 0);
        crit.Should().BeGreaterThan(noCrit, "a percent roll of 0 is a Dexterity crit");
        Swing(plainHero, percent: baseChance).Should().Be(noCrit, "the roll at the base chance misses without Umbrath");

        var c = Hero("Umb");
        WithGod(c, "Umbrath", 60, () =>
        {
            int umbrath = DivineBlessingSystem.Instance.GetCriticalHitBonus(c);
            umbrath.Should().BeGreaterThan(0);
            Swing(c, percent: baseChance).Should().Be(crit, "Umbrath's points make that roll a crit");
            Swing(c, percent: baseChance + umbrath - 1).Should().Be(crit);
            Swing(c, percent: baseChance + umbrath).Should().Be(noCrit, "the chance rises by exactly Umbrath's points");
            Swing(c, percent: 0).Should().Be(crit, "a crit is not multiplied a second time");
            return 0;
        });
    }

    [Fact]
    public void Umbrath_AtTheCritCeiling_StaysUnderIt()
    {
        var c = Hero("UmbCap");
        c.TempCritChanceBonus = 200;
        int ceiling = StatEffectsSystem.CritChance(c);
        ceiling.Should().Be(50, "Dexterity 20 has a ceiling of 50");
        long noCrit = Swing(Hero(), percent: 99);
        WithGod(c, "Umbrath", 60, () =>
        {
            Swing(c, percent: ceiling - 1).Should().BeGreaterThan(noCrit, "below the ceiling the roll crits");
            Swing(c, percent: ceiling).Should().Be(noCrit, "Umbrath does not lift the chance past the ceiling");
            return 0;
        });
    }

    [Fact]
    public void WorldBossCritChance_DoesNotCountUmbrath()
    {
        var c = Hero("UmbWb");
        int plain = StatEffectsSystem.CritChance(c);
        WithGod(c, "Umbrath", 60, () =>
        {
            StatEffectsSystem.CritChance(c).Should().Be(plain, "the world boss adds Umbrath on its own roll (ApplyDivineAttack)");
            return 0;
        });
    }

    // ---------- 3. the Blood Price ----------

    [Fact]
    public void BloodPrice_LiveSwing_CutsDamageByTier()
    {
        long plain = Swing(Hero());
        var c = Hero("Blood");
        c.MurderWeight = GameConfig.MurderWeightTier2Threshold;
        Swing(c).Should().Be(plain - (long)(plain * GameConfig.MurderWeightTier2CombatPenalty));
        c.MurderWeight = GameConfig.MurderWeightTier3Threshold;
        Swing(c).Should().Be(plain - (long)(plain * GameConfig.MurderWeightTier3CombatPenalty));
        c.MurderWeight = GameConfig.MurderWeightTier2Threshold - 0.5f;
        Swing(c).Should().Be(plain, "below the second tier there is no cut");
    }

    // ---------- 4. BossSlayer ----------

    [Fact]
    public void BossSlayer_LiveSwing_AddsTenPercentAgainstBosses()
    {
        var shield = EquipmentDatabase.GetShields().First();
        bool before = shield.HasBossSlayer;
        try
        {
            var c = Hero("Slayer");
            c.EquippedItems[EquipmentSlot.OffHand] = shield.Id;
            var boss = Dummy(); boss.IsBoss = true;
            var elite = Dummy(); elite.IsMiniBoss = true;

            long vsPlain = Swing(c, Dummy());
            Swing(c, boss).Should().Be(vsPlain, "without the effect a boss takes the same");

            shield.HasBossSlayer = true;
            Swing(c, Dummy()).Should().Be(vsPlain, "an ordinary monster takes no bonus");
            Swing(c, boss).Should().Be(vsPlain + (long)(vsPlain * 0.10));
            Swing(c, elite).Should().Be(vsPlain + (long)(vsPlain * 0.10), "a mini-boss counts as the old path counted it");
        }
        finally { shield.HasBossSlayer = before; }
    }

    // ---------- 5. divine wrath ----------

    [Fact]
    public void DivineWrath_LiveSwing_DealsTheStatedPercentLess()
    {
        long plain = Swing(Hero());
        foreach (int pct in new[] { 10, 20, 30 })
            Swing(Hero(), wrathPct: pct).Should().Be(plain - plain * pct / 100, $"wrath of {pct} percent");
    }

    private static async Task<long> MonsterHitOn(Character player, int wrathPct, int seed)
    {
        var monster = new Monster
        {
            Name = "Brute", Level = 30, HP = 100_000, MaxHP = 100_000, Strength = 3000, Defence = 0,
            WeapPow = 0, IsActive = true,
        };
        var engine = NewEngine(player, new Random(seed), wrathPct: wrathPct);
        var result = new CombatResult { Player = player, Teammates = new List<Character>() };
        long hp = player.HP;
        await (Task)typeof(CombatEngine).GetMethod("ProcessMonsterAction", F)!
            .Invoke(engine, new object?[] { monster, player, result, new List<Monster> { monster } })!;
        return hp - player.HP;
    }

    private static Character Tank()
    {
        var c = Hero("Tank");
        c.HP = c.MaxHP = 100_000;
        c.Defence = 1000;
        c.ArmPow = 0;
        return c;
    }

    [Fact]
    public async Task DivineWrath_MonsterHit_TakesTheStatedPercentMore()
    {
        // the same seed draws the same rolls; the first seed whose hit lands is the one compared
        int seed = 1;
        long plain = 0;
        for (; seed < 200 && plain == 0; seed++) plain = await MonsterHitOn(Tank(), 0, seed);
        seed--;
        plain.Should().BeGreaterThan(0, "a hit lands");
        (await MonsterHitOn(Tank(), 0, seed)).Should().Be(plain, "without wrath the hit is unchanged");
        (await MonsterHitOn(Tank(), 30, seed)).Should().Be(plain + 300, "30 percent of 1000 defence is gone");
        (await MonsterHitOn(Tank(), 10, seed)).Should().Be(plain + 100);
    }

    [Fact]
    public void DivineWrath_Dungeon_SetsTheScreensPercentOnTheEngine()
    {
        var src = Source("Scripts/Locations/DungeonLocation.cs");
        src.Should().NotContain("TempAttackBonus -= Math.Abs(damageModifier)", "the fight clears TempAttackBonus as it starts");
        Count(src, "combatEngine.DivineWrathPct = ").Should().Be(2, "both dungeon fights hand the cut to the engine");
        Count(src, "CheckDivinePunishment(").Should().Be(3);
    }

    // ---------- 6. the Status Blood Price lines ----------

    private static readonly Dictionary<string, string[]> NotDone = new()
    {
        ["en"] = new[] { "shop", "healer" },
        ["es"] = new[] { "tienda", "sanador" },
        ["fr"] = new[] { "boutique", "guérison" },
        ["it"] = new[] { "negozi", "guaritore" },
        ["hu"] = new[] { "bolti", "gyógyító" },
    };

    [Fact]
    public void StatusBloodPrice_NamesNoShopOrHealerCost_InAnyLanguage()
    {
        foreach (var lang in AllLanguages)
        {
            var rows = new[]
            {
                Loc.GetIn(lang, "base.buff_blood_price_1", 8),
                Loc.GetIn(lang, "base.buff_blood_price_2", 10),
                Loc.GetIn(lang, "base.buff_blood_price_3", 20),
            };
            foreach (var row in rows)
                foreach (var word in NotDone[lang])
                    row.Should().NotContainEquivalentOf(word, $"{lang}: the Blood Price raises no shop or healer price");
            rows[1].Should().Contain("10", $"{lang}: the tier 2 damage cut stays");
            rows[2].Should().Contain("20", $"{lang}: the tier 3 damage cut stays");
            rows[0].Should().Contain("8", $"{lang}: tier 1 names the weight where the cut starts");
            foreach (var row in rows.Where(_ => lang is "en" or "hu"))
                row.Length.Should().BeLessOrEqualTo(79, $"{lang}: \"{row}\"");
        }
        var src = Source("Scripts/Locations/BaseLocation.cs");
        src.Should().NotContain("ShopMarkup * 100", "the Status screen passes no markup");
        src.Should().NotContain("HealPenalty * 100");
    }

    // ---------- 7. the Sanctum's ward ----------

    private static Character Aligned(string name, long chivalry, long darkness)
    {
        var c = Hero(name);
        c.Chivalry = chivalry; c.Darkness = darkness;
        return c;
    }

    [Fact]
    public async Task Sanctum_EvilCharacter_IsTurnedAway()
    {
        var evil = Aligned("Evil", 50, 800);
        AlignmentSystem.Instance.GetAlignment(evil).Should().Be(AlignmentSystem.AlignmentType.Evil);
        var term = new TerminalEmulator(new LineStream(Enumerable.Repeat("", 20)), new MemoryStream());
        var enter = new SanctumLocation().EnterLocation(evil, term);
        var done = await Task.WhenAny(enter, Task.Delay(5000));
        done.Should().BeSameAs(enter, "the ward answers at the door");
        enter.IsFaulted.Should().BeTrue();
        var ex = enter.Exception!.InnerExceptions.Single();
        ex.Should().BeOfType<LocationExitException>();
        ((LocationExitException)ex).DestinationLocation.Should().Be(GameLocation.MainStreet);
    }

    [Fact]
    public void Sanctum_GoodAndNeutral_PassTheWard()
    {
        foreach (var c in new[] { Aligned("Good", 800, 50), Aligned("Neutral", 0, 0) })
        {
            AlignmentSystem.Instance.GetAlignment(c).Should().NotBe(AlignmentSystem.AlignmentType.Evil);
            AlignmentSystem.Instance.CanAccessLocation(c, GameLocation.Sanctum).canAccess.Should().BeTrue(c.Name2);
        }
        var src = Source("Scripts/Locations/SanctumLocation.cs");
        MethodBody(src, "public override async Task EnterLocation(").Should()
            .Contain("CanAccessLocation(player, GameLocation.Sanctum)");
    }

    // ---------- pins: teammate swing and spells unchanged ----------

    private static async Task<List<long>> TeammateSwings(bool marked)
    {
        var leader = Hero("Leader");
        var mate = Hero("Mate", CharacterClass.Warrior, CharacterAI.Computer, level: 9);
        if (marked) mate.MurderWeight = GameConfig.MurderWeightTier3Threshold;
        leader.TeammateDisabledAbilities[mate.GetSkillToggleKey()] =
            ClassAbilitySystem.GetAvailableAbilities(mate).Select(a => a.Id).ToList();
        var mates = new List<Character> { mate };
        var engine = NewEngine(leader, new Random(1322), mates, wrathPct: marked ? 30 : 0);
        var result = new CombatResult { Player = leader, Teammates = mates };
        typeof(CombatEngine).GetMethod("BeginFightEvents", F)!.Invoke(engine, new object[] { result });
        var rec = new Recorder();
        engine.Observer = rec;
        var boss = Dummy(MonsterClass.Undead); boss.IsBoss = true;
        await (Task)typeof(CombatEngine).GetMethod("ProcessTeammateActionMultiMonster", F)!
            .Invoke(engine, new object[] { mate, new List<Monster> { boss }, result })!;
        return rec.Events.Where(e => e.Actor == CombatSide.Team && e.Target == CombatSide.Monster).Select(e => e.Amount).ToList();
    }

    [Fact]
    public async Task TeammateSwing_IsUnchanged()
    {
        var plain = await TeammateSwings(marked: false);
        plain.Should().NotBeEmpty("the teammate swings");
        (await TeammateSwings(marked: true)).Should().Equal(plain, "the effects are the player's own swing only");
    }

    [Fact]
    public void SpellPath_IsUnchanged()
    {
        var src = Source("Scripts/Systems/CombatEngine.cs");
        var swing = MethodBody(src, "private long ComputePlayerSwingDamageRolled(");
        Count(swing, "DivineBlessingSystem.Instance.CalculateBonusDamage(").Should().Be(1, "the swing reads the divine damage once");
        Count(swing, "OldGodEchoSystem.BonusDamage(").Should().Be(0, "the echo comes inside CalculateBonusDamage");
        Count(src, "OldGodEchoSystem.BonusDamage(").Should().Be(2, "the two spell reads stay as they were");
        var spell = MethodBody(src, "private async Task ExecuteSpellMultiMonster(");
        spell.Should().NotContain("CalculateBonusDamage").And.NotContain("BossSlayer").And.NotContain("MurderWeight")
            .And.NotContain("DivineWrathPct");
    }
}
