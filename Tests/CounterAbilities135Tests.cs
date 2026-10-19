using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using FluentAssertions;
using UsurperRemake;
using UsurperRemake.Data;
using UsurperRemake.Systems;
using UsurperRemake.UI;
using Xunit;

namespace UsurperReborn.Tests;

/// <summary>
/// 1.3.5 (CT-2): a held counter (Sunbane for Aurelion, Stonebane for Terravok, Godbane for Manwe, on the main or
/// off hand) holds back two of that god's named abilities. A drawn blocked ability is the god's plain strike with
/// one held-back line, never a redraw, so the god's other abilities come no more often (supervisor, ledger
/// 2026-10-19T04:26:01Z). With no counter, or another god's counter, every draw, random call and line is as before.
/// </summary>
[Collection("SharedGameSingletons")]
public class CounterAbilities135Tests : IDisposable
{
    private const BindingFlags F = BindingFlags.NonPublic | BindingFlags.Instance;
    private const BindingFlags S = BindingFlags.NonPublic | BindingFlags.Static;
    private static readonly string[] Langs = { "en", "es", "fr", "hu", "it" };

    internal static readonly OldGodType[] Countered = { OldGodType.Aurelion, OldGodType.Terravok, OldGodType.Manwe };

    internal static readonly Dictionary<OldGodType, string[]> Blocked = new()
    {
        [OldGodType.Aurelion] = new[] { "Light's Embrace", "Sacrifice" },
        [OldGodType.Terravok] = new[] { "Stone Skin", "Entomb" },
        [OldGodType.Manwe] = new[] { "Word of Creation", "Time Stop" },
    };

    private readonly string _prevLanguage = GameConfig.Language;
    private readonly bool _prevManwe = CombatEngine.IsManweBattle;

    public CounterAbilities135Tests() { GameConfig.Language = "en"; }

    public void Dispose()
    {
        GameConfig.Language = _prevLanguage;
        CombatEngine.IsManweBattle = _prevManwe;
    }

    // ---------- helpers ----------

    /// <summary>The shop's own kind code for a tier (MagicShopLocation.GetEnchantKindCode, private).</summary>
    internal static string ShopCode(int tier) =>
        (string)typeof(MagicShopLocation).GetMethod("GetEnchantKindCode", S)!.Invoke(null, new object[] { tier, 0 })!;

    internal static int TierOf(OldGodType god) => god switch
    {
        OldGodType.Aurelion => 15,
        OldGodType.Terravok => 16,
        OldGodType.Manwe => 17,
        _ => throw new ArgumentOutOfRangeException(nameof(god)),
    };

    private static readonly Dictionary<string, int> Registered = new();

    /// <summary>A plain sword in the slot, with the shop's counter code for the god written on it when one is given.</summary>
    private static int Weapon(EquipmentSlot slot, OldGodType? counterOf)
    {
        string key = $"{slot}:{counterOf}";
        if (Registered.TryGetValue(key, out int id)) return id;
        var item = new Equipment
        {
            Name = slot == EquipmentSlot.MainHand ? "Plain Blade" : "Plain Dagger", Slot = slot, WeaponType = WeaponType.Sword,
            WeaponPower = 100, MinLevel = 1, Rarity = EquipmentRarity.Common, Description = "",
        };
        if (counterOf is OldGodType god) item.AddEnchantedKind(ShopCode(TierOf(god)));
        id = EquipmentDatabase.RegisterDynamic(item);
        Registered[key] = id;
        return id;
    }

    /// <summary>The Old God fixture hero with a plain main hand, the counters given, and HP no fight can end.</summary>
    internal static Character Hero(OldGodType? main = null, OldGodType? off = null, bool offHand = false)
    {
        var hero = OldGodBuild134Support.Hero();
        hero.EquippedItems[EquipmentSlot.MainHand] = Weapon(EquipmentSlot.MainHand, main);
        if (offHand || off != null) hero.EquippedItems[EquipmentSlot.OffHand] = Weapon(EquipmentSlot.OffHand, off);
        hero.RecalculateStats();
        hero.MaxHP = 100_000_000;
        hero.HP = hero.MaxHP;
        return hero;
    }

    internal static BossCombatContext Context(OldGodType god, Character hero) =>
        OldGodBossSystem.BuildBossFight(god, 0, BossDialogue.None, hero).Context;

    private static CombatEngine Engine(int seed)
    {
        var engine = new CombatEngine(new TerminalEmulator(new MemoryStream(), new MemoryStream()));
        engine.SeedRandomForTests(seed);
        return engine;
    }

    // what a pick of null means: the god makes its normal attack (the caller's null branch)
    private const string PlainStrike = "(plain strike)";

    /// <summary>One SelectBossAbility call: the name (or the plain strike) and the held-back name.</summary>
    private static (string Pick, string? HeldBack) Pick(CombatEngine engine, BossCombatContext ctx)
    {
        var args = new object?[] { ctx, null };
        var name = (string?)typeof(CombatEngine).GetMethod("SelectBossAbility", F)!.Invoke(engine, args);
        return (name ?? PlainStrike, (string?)args[1]);
    }

    private static List<string> Picks(BossCombatContext ctx, int seed, int count)
    {
        var engine = Engine(seed);
        return Enumerable.Range(0, count).Select(_ => Pick(engine, ctx).Pick).ToList();
    }

    private static List<string> PhasePool(BossCombatContext ctx, int phase) =>
        ctx.BossData.Abilities.Where(a => a.Phase <= phase).Select(a => a.Name).ToList();

    /// <summary>A seeded Random that writes down every draw the engine asks of it.</summary>
    private sealed class RecordingRandom : Random
    {
        public readonly List<string> Calls = new();
        public RecordingRandom(int seed) : base(seed) { }
        public override int Next() { int v = base.Next(); Calls.Add($"Next()={v}"); return v; }
        public override int Next(int maxValue) { int v = base.Next(maxValue); Calls.Add($"Next({maxValue})={v}"); return v; }
        public override int Next(int minValue, int maxValue) { int v = base.Next(minValue, maxValue); Calls.Add($"Next({minValue},{maxValue})={v}"); return v; }
        public override double NextDouble() { double v = base.NextDouble(); Calls.Add($"NextDouble()={v:R}"); return v; }
    }

    private sealed record Turn(bool Used, string Shown, List<string> Calls, List<string> Log, string GodName);

    /// <summary>One god turn through TryMonsterSpecialAbility on a fresh fight and a fresh engine seeded with seed.</summary>
    private static Turn GodTurn(OldGodType god, int phase, Character hero, int seed)
    {
        var fight = OldGodBossSystem.BuildBossFight(god, 0, BossDialogue.None, hero);
        fight.Context.CurrentPhase = phase;
        var output = new MemoryStream();
        // Manwe's The Offer asks yes or no: every turn declines it
        var input = new MemoryStream(Encoding.UTF8.GetBytes(string.Concat(Enumerable.Repeat("n\n", 20))));
        var term = new TerminalEmulator(input, output);
        var engine = new CombatEngine(term);
        var random = new RecordingRandom(seed);
        typeof(CombatEngine).GetField("random", F)!.SetValue(engine, random);
        typeof(CombatEngine).GetField("_combatOwner", F)!.SetValue(engine, hero);
        typeof(CombatEngine).GetField("currentPlayer", F)!.SetValue(engine, hero);
        engine.BossContext = fight.Context;
        CombatEngine.IsManweBattle = god == OldGodType.Manwe;
        engine.ResetManweBossFlags();
        var result = new CombatResult { Player = hero, Teammates = new List<Character>(), CombatLog = new List<string>(), CurrentRound = 2 };
        bool used = ((Task<bool>)typeof(CombatEngine).GetMethod("TryMonsterSpecialAbility", F)!
            .Invoke(engine, new object?[] { fight.Monster, hero, result, new List<Monster> { fight.Monster } })!).GetAwaiter().GetResult();
        term.StreamWriterInternal?.Flush();
        string shown = Regex.Replace(Encoding.UTF8.GetString(output.ToArray()), "\u001b\\[[0-9;?]*[A-Za-z]", "").Replace("\r", "");
        return new Turn(used, shown, random.Calls, result.CombatLog, MonsterNames.Display(fight.Monster));
    }

    /// <summary>The ability the god draws on this seed and phase without a counter, or null when it rolls no ability
    /// (the caller's 70 percent roll, then the phase pool draw).</summary>
    private static string? Drawn(OldGodType god, int phase, int seed)
    {
        var pool = PhasePool(Context(god, Hero()), phase);
        var r = new Random(seed);
        return r.Next(100) >= 70 ? null : pool[r.Next(pool.Count)];
    }

    private static string Norm(string s) => Regex.Replace(s, @"\s+", " ").Trim();

    private static string HeldLine(OldGodType god, string godName, string ability) =>
        Loc.Get("combat.counter_blocked", godName, CombatEngine.BossAbilityLabel(ability), OldGodBossSystem.CounterTierName(god));

    // ---------- the codes and the lists ----------

    [Fact]
    public void CounterAbilities135_CounterKindCode_EqualsTheShopsCode_BothWays()
    {
        MagicShopLocation.CounterTiers.Should().Equal(15, 16, 17);
        foreach (var god in Countered)
        {
            OldGodBossSystem.CounterKindCode(god).Should().Be(ShopCode(TierOf(god)), god.ToString());
            OldGodBossSystem.CounterKindCode(god).Should().NotBeEmpty(god.ToString());
        }
        foreach (int tier in MagicShopLocation.CounterTiers)
            Countered.Count(g => OldGodBossSystem.CounterKindCode(g) == ShopCode(tier)).Should().Be(1, $"tier {tier} counters one god");
        foreach (OldGodType god in Enum.GetValues(typeof(OldGodType)))
            if (!Countered.Contains(god))
            {
                OldGodBossSystem.CounterKindCode(god).Should().BeEmpty(god.ToString());
                OldGodBossSystem.CounterBlockedAbilities(god).Should().BeEmpty(god.ToString());
                OldGodBossSystem.CounterTierName(god).Should().BeEmpty(god.ToString());
            }
    }

    [Fact]
    public void CounterAbilities135_BlockedLists_AreTheTwoNamedAbilities_InTheGodsPool()
    {
        foreach (var god in Countered)
        {
            OldGodBossSystem.CounterBlockedAbilities(god).Should().BeEquivalentTo(Blocked[god], god.ToString());
            PhasePool(Context(god, Hero()), 3).Should().Contain(Blocked[god], $"{god} draws both by phase 3");
            OldGodBossSystem.CounterTierName(god).Should().Be(MagicShopLocation.TierName(TierOf(god) - 1), god.ToString());
        }
    }

    [Fact]
    public void CounterAbilities135_HoldsCounter_MainHandOrOffHand_OwnGodOnly()
    {
        foreach (var god in Countered)
        {
            OldGodBossSystem.HoldsCounter(god, Hero(main: god)).Should().BeTrue($"{god} main hand");
            OldGodBossSystem.HoldsCounter(god, Hero(off: god)).Should().BeTrue($"{god} off hand");
            OldGodBossSystem.HoldsCounter(god, Hero()).Should().BeFalse($"{god} none");
            OldGodBossSystem.HoldsCounter(god, Hero(offHand: true)).Should().BeFalse($"{god} plain off hand");
            foreach (var other in Countered.Where(o => o != god))
            {
                OldGodBossSystem.HoldsCounter(god, Hero(main: other)).Should().BeFalse($"{god} with {other}'s counter");
                OldGodBossSystem.HoldsCounter(god, Hero(off: other)).Should().BeFalse($"{god} with {other}'s counter off hand");
            }
        }
        foreach (OldGodType god in Enum.GetValues(typeof(OldGodType)))
            if (!Countered.Contains(god))
                foreach (var c in Countered)
                    OldGodBossSystem.HoldsCounter(god, Hero(main: c)).Should().BeFalse($"{god} has no counter");
    }

    [Fact]
    public void CounterAbilities135_Build_RecordsTheBlockedNames_MainHandAndOffHand()
    {
        foreach (var god in Countered)
        {
            Context(god, Hero(main: god)).CounterBlockedAbilities.Should().BeEquivalentTo(Blocked[god], $"{god} main hand");
            Context(god, Hero(off: god)).CounterBlockedAbilities.Should().BeEquivalentTo(Blocked[god], $"{god} off hand");
            Context(god, Hero()).CounterBlockedAbilities.Should().BeNull($"{god} none");
            foreach (var other in Countered.Where(o => o != god))
                Context(god, Hero(main: other)).CounterBlockedAbilities.Should().BeNull($"{god} with {other}'s counter");
        }
    }

    [Fact]
    public void CounterAbilities135_BlockedList_IsNotOnTheContextSnapshot()
    {
        typeof(BossCombatContext).GetProperties(BindingFlags.Public | BindingFlags.Instance)
            .Select(p => p.Name).Should().NotContain("CounterBlockedAbilities");
        typeof(BossCombatContext).GetProperty("CounterBlockedAbilities", F).Should().NotBeNull("it is internal");
        foreach (var god in Countered)
        {
            var hero = Hero(main: god);
            var fight = OldGodBossSystem.BuildBossFight(god, 0, BossDialogue.None, hero);
            fight.Context.CounterBlockedAbilities.Should().NotBeNull();
            OldGodBuild134Support.Describe(fight.Monster, fight.Context, hero).Should().NotContain(l => l.Contains("CounterBlocked"), god.ToString());
        }
    }

    // ---------- the draw ----------

    [Fact]
    public void CounterAbilities135_Draw_WithTheCounter_BlockedTurnsArePlainStrikes_OthersAsOften()
    {
        foreach (var god in Countered)
            foreach (bool offHand in new[] { false, true })
                for (int phase = 1; phase <= 3; phase++)
                {
                    var held = Context(god, offHand ? Hero(off: god) : Hero(main: god));
                    held.CurrentPhase = phase;
                    var none = Context(god, Hero());
                    none.CurrentPhase = phase;
                    var withEngine = Engine(1350 + phase);
                    var withCounter = Enumerable.Range(0, 1000).Select(_ => Pick(withEngine, held)).ToList();
                    var without = Picks(none, 1350 + phase, 1000);
                    string label = $"{god} phase {phase} {(offHand ? "off" : "main")} hand";
                    withCounter.Select(p => p.Pick).Should().NotContain(Blocked[god], label);
                    withCounter.Select(p => p.Pick).Should().Equal(without.Select(n => Blocked[god].Contains(n) ? PlainStrike : n),
                        $"{label}: a blocked turn is a plain strike and the others come as often");
                    withCounter.Select(p => p.HeldBack).Should().Equal(without.Select(n => Blocked[god].Contains(n) ? n : null),
                        $"{label}: the held-back name is the blocked one drawn");
                    if (phase == 3) withCounter.Count(p => p.Pick == PlainStrike).Should().BeGreaterThan(0, label);
                }
    }

    /// <summary>SelectBossAbility at f22e7ec7 (before CT-2), seeds 1350 + phase, 20 picks, no counter.</summary>
    private static readonly Dictionary<(OldGodType, int), string> Golden = new()
    {
        [(OldGodType.Aurelion, 1)] = "Truth Revealed | Purifying Light | Purifying Light | Blinding Flash | Purifying Light | Purifying Light | Purifying Light | Purifying Light | Purifying Light | Truth Revealed | Truth Revealed | Purifying Light | Truth Revealed | Truth Revealed | Truth Revealed | Blinding Flash | Blinding Flash | Truth Revealed | Purifying Light | Truth Revealed",
        [(OldGodType.Aurelion, 2)] = "Purifying Light | Solar Flare | Divine Judgment | Purifying Light | Solar Flare | Purifying Light | Divine Judgment | Divine Judgment | Truth Revealed | Divine Judgment | Blinding Flash | Light's Embrace | Purifying Light | Light's Embrace | Blinding Flash | Blinding Flash | Truth Revealed | Truth Revealed | Truth Revealed | Divine Judgment",
        [(OldGodType.Aurelion, 3)] = "Light's Embrace | Sacrifice | Divine Judgment | Solar Flare | Purifying Light | Final Truth | Truth Revealed | Truth Revealed | Solar Flare | Truth Revealed | Purifying Light | Last Light | Last Light | Solar Flare | Purifying Light | Purifying Light | Light's Embrace | Sacrifice | Blinding Flash | Truth Revealed",
        [(OldGodType.Terravok, 1)] = "Stone Skin | Earthquake | Earthquake | Mountain's Weight | Earthquake | Earthquake | Earthquake | Earthquake | Earthquake | Stone Skin | Stone Skin | Earthquake | Stone Skin | Stone Skin | Stone Skin | Mountain's Weight | Mountain's Weight | Stone Skin | Earthquake | Stone Skin",
        [(OldGodType.Terravok, 2)] = "Earthquake | Entomb | Magma Surge | Earthquake | Entomb | Earthquake | Magma Surge | Magma Surge | Stone Skin | Magma Surge | Mountain's Weight | Geological Shift | Earthquake | Geological Shift | Mountain's Weight | Mountain's Weight | Stone Skin | Stone Skin | Stone Skin | Magma Surge",
        [(OldGodType.Terravok, 3)] = "Geological Shift | Core Awakening | Magma Surge | Entomb | Earthquake | The Long Sleep | Stone Skin | Stone Skin | Entomb | Stone Skin | Earthquake | World Breaker | World Breaker | Entomb | Earthquake | Earthquake | Geological Shift | Core Awakening | Mountain's Weight | Stone Skin",
        [(OldGodType.Manwe, 1)] = "Divine Judgment | Unmake | Word of Creation | Reality Warp | Word of Creation | Word of Creation | Unmake | Unmake | Word of Creation | Divine Judgment | Time Stop | Unmake | Unmake | Unmake | Time Stop | Time Stop | Time Stop | Time Stop | Unmake | Divine Judgment",
        [(OldGodType.Manwe, 2)] = "Word of Creation | Reality Warp | Shadow Incarnate | Unmake | Split Form | Word of Creation | Light Incarnate | Light Incarnate | Divine Judgment | Shadow Incarnate | Time Stop | The Question | Word of Creation | Shadow Incarnate | Time Stop | Time Stop | Unmake | Divine Judgment | Divine Judgment | Shadow Incarnate",
        [(OldGodType.Manwe, 3)] = "Light Incarnate | Final Word | Split Form | Reality Warp | Word of Creation | The Offer | Unmake | Divine Judgment | Reality Warp | Divine Judgment | Unmake | Final Word | Final Word | Reality Warp | Word of Creation | Word of Creation | Shadow Incarnate | Creation's End | Time Stop | Divine Judgment",
    };

    [Fact]
    public void CounterAbilities135_Draw_NoCounterOrAnotherGodsCounter_EqualsTheGoldenListFromBefore()
    {
        foreach (var god in Countered)
            for (int phase = 1; phase <= 3; phase++)
            {
                var heroes = new List<(string, Character)> { ("none", Hero()) };
                foreach (var other in Countered.Where(o => o != god))
                {
                    heroes.Add(($"{other}'s counter", Hero(main: other)));
                    heroes.Add(($"{other}'s counter off hand", Hero(off: other)));
                }
                foreach (var (label, hero) in heroes)
                {
                    var ctx = Context(god, hero);
                    ctx.CurrentPhase = phase;
                    var engine = Engine(1350 + phase);
                    var picks = Enumerable.Range(0, 20).Select(_ => Pick(engine, ctx)).ToList();
                    string.Join(" | ", picks.Select(p => p.Pick)).Should().Be(Golden[(god, phase)], $"{god} phase {phase} {label}");
                    picks.Should().OnlyContain(p => p.HeldBack == null, $"{god} phase {phase} {label}");
                }
            }
    }

    [Fact]
    public void CounterAbilities135_Draw_WholePoolBlocked_EveryTurnIsAPlainStrike()
    {
        var data = new OldGodBossData { Type = OldGodType.Terravok, Phase1Abilities = new[] { "Stone Skin", "Entomb" } };
        var ctx = new BossCombatContext { BossData = data, GodType = OldGodType.Terravok, CurrentPhase = 3, CounterBlockedAbilities = Blocked[OldGodType.Terravok] };
        Picks(ctx, 135, 100).Should().OnlyContain(n => n == PlainStrike);
    }

    // ---------- the god's turn (TryMonsterSpecialAbility) ----------

    private const int Seeds = 160;

    [Fact]
    public void CounterAbilities135_Turn_WithTheCounter_BlockedDrawIsAPlainStrikeWithTheHeldBackLine_OtherTurnsAsBefore()
    {
        foreach (var god in Countered)
        {
            var seen = new HashSet<string>();
            foreach (bool offHand in new[] { false, true })
                for (int phase = 1; phase <= 3; phase++)
                    for (int seed = 0; seed < Seeds; seed++)
                    {
                        string label = $"{god} phase {phase} {(offHand ? "off" : "main")} hand seed {seed}";
                        string? drawn = Drawn(god, phase, seed);
                        var without = GodTurn(god, phase, Hero(), seed);
                        var with = GodTurn(god, phase, offHand ? Hero(off: god) : Hero(main: god), seed);
                        without.Calls.Should().NotBeEmpty(label);
                        without.Calls[0].Should().StartWith("Next(100)=", $"{label}: the caller's roll comes first (Drawn)");
                        if (drawn != null && Blocked[god].Contains(drawn))
                        {
                            seen.Add(drawn);
                            with.Used.Should().BeFalse($"{label}: the caller makes the normal attack");
                            with.Calls.Should().Equal(without.Calls.Take(2), $"{label}: no redraw and no other random call");
                            var rows = with.Shown.Split('\n').Where(r => r.Trim().Length > 0).ToList();
                            Norm(string.Join(" ", rows)).Should().Be(Norm(HeldLine(god, with.GodName, drawn)), $"{label}: one held-back line");
                            with.Log.Should().NotContain(l => Blocked[god].Any(b => l.Contains(b)), label);
                            without.Used.Should().BeTrue($"{label}: without the counter it runs");
                        }
                        else
                        {
                            with.Used.Should().Be(without.Used, label);
                            with.Shown.Should().Be(without.Shown, label);
                            with.Calls.Should().Equal(without.Calls, label);
                            with.Log.Should().Equal(without.Log, label);
                            foreach (var b in Blocked[god])
                                Norm(with.Shown).Should().NotContain(Norm(HeldLine(god, with.GodName, b)), label);
                        }
                    }
            seen.Should().BeEquivalentTo(Blocked[god], $"{god}: both blocked names were drawn and held back");
        }
    }

    [Fact]
    public void CounterAbilities135_Turn_AnotherGodsCounter_EqualsNoCounter_SeedForSeed()
    {
        foreach (var god in Countered)
            foreach (var other in Countered.Where(o => o != god))
                for (int phase = 1; phase <= 3; phase++)
                    for (int seed = 0; seed < Seeds / 2; seed++)
                    {
                        string label = $"{god} with {other}'s counter phase {phase} seed {seed}";
                        var without = GodTurn(god, phase, Hero(), seed);
                        foreach (var hero in new[] { Hero(main: other), Hero(off: other) })
                        {
                            var with = GodTurn(god, phase, hero, seed);
                            with.Used.Should().Be(without.Used, label);
                            with.Shown.Should().Be(without.Shown, label);
                            with.Calls.Should().Equal(without.Calls, label);
                            with.Log.Should().Equal(without.Log, label);
                        }
                    }
    }

    [Theory]
    [InlineData("en")] [InlineData("es")] [InlineData("fr")] [InlineData("hu")] [InlineData("it")]
    public void CounterAbilities135_HeldBackLine_IsShownInTheLanguage_AndEveryRowFits(string lang)
    {
        GameConfig.Language = lang;
        foreach (var god in Countered)
        {
            var seen = new HashSet<string>();
            for (int seed = 0; seed < Seeds && seen.Count < 2; seed++)
            {
                string? drawn = Drawn(god, 3, seed);
                if (drawn == null || !Blocked[god].Contains(drawn) || seen.Contains(drawn)) continue;
                seen.Add(drawn);
                var with = GodTurn(god, 3, Hero(main: god), seed);
                string expected = HeldLine(god, with.GodName, drawn);
                expected.Should().Contain(with.GodName).And.Contain(CombatEngine.BossAbilityLabel(drawn))
                    .And.Contain(OldGodBossSystem.CounterTierName(god), $"{lang} {god} {drawn}");
                Norm(with.Shown).Should().Be(Norm(expected), $"{lang} {god} {drawn}");
                foreach (var row in with.Shown.Split('\n'))
                    row.TrimEnd().Length.Should().BeLessOrEqualTo(79, $"{lang} row fits: [{row}]");
            }
            seen.Should().HaveCount(2, $"{lang} {god}");
        }
    }

    // ---------- the text ----------

    private static string RepoRoot()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir != null && !(Directory.Exists(Path.Combine(dir.FullName, "Scripts")) && Directory.Exists(Path.Combine(dir.FullName, "Localization"))))
            dir = dir.Parent;
        dir.Should().NotBeNull("the test must run inside the repository");
        return dir!.FullName;
    }

    [Fact]
    public void CounterAbilities135_NewKey_IsInEveryLanguage_FilledAndTranslated()
    {
        const string key = "combat.counter_blocked";
        var files = Langs.ToDictionary(l => l, l =>
            JsonSerializer.Deserialize<Dictionary<string, JsonElement>>(File.ReadAllText(Path.Combine(RepoRoot(), "Localization", l + ".json")))!);
        string en = files["en"][key].GetString()!;
        en.Should().Be("{0}'s {1} is held back by your {2}.");
        foreach (var lang in Langs)
        {
            files[lang].Should().ContainKey(key, lang);
            string text = files[lang][key].GetString()!;
            text.Should().NotBeNullOrWhiteSpace(lang);
            foreach (var hole in new[] { "{0}", "{1}", "{2}" }) text.Should().Contain(hole, $"{lang} {hole}");
            if (lang != "en") text.Should().NotBe(en, $"{lang} is translated");
        }
    }
}

/// <summary>
/// 1.3.5 (CT-2): the companion path pin, carried from the probe. MonsterAttacksCompanion draws from the god's full
/// SpecialAbilities list, unfiltered (supervisor, ledger 2026-10-19T02:20:52Z); a drawn name runs there only when it
/// parses as a MonsterAbilities.AbilityType, and none of the six blocked names does.
/// </summary>
[Collection("SharedGameSingletons")]
public class CounterAbilities135CompanionTests
{
    private const BindingFlags F = BindingFlags.NonPublic | BindingFlags.Instance;

    private static readonly string[] AllBlocked = { "Light's Embrace", "Sacrifice", "Stone Skin", "Entomb", "Word of Creation", "Time Stop" };

    [Fact]
    public void CounterAbilities135_Companion_NoBlockedName_ParsesAsAnAbilityType()
    {
        AllBlocked.Should().BeEquivalentTo(CounterAbilities135Tests.Countered.SelectMany(OldGodBossSystem.CounterBlockedAbilities));
        foreach (var name in AllBlocked)
            Enum.TryParse<MonsterAbilities.AbilityType>(name, true, out _).Should().BeFalse(name);
    }

    private static Character Companion() => new Character
    {
        Name1 = "mate", Name2 = "Mate", Class = CharacterClass.Warrior, Race = CharacterRace.Human, Level = 60,
        HP = 100_000_000, MaxHP = 100_000_000, ArmPow = 20, Defence = 10, CombatSpeed = CombatSpeed.Instant,
    };

    /// <summary>One MonsterAttacksCompanion on a fresh seeded engine; returns the combat log.</summary>
    private static List<string> HitCompanion(Monster god, Character hero, BossCombatContext? ctx, int seed)
    {
        var engine = new CombatEngine(new TerminalEmulator(new MemoryStream(), new MemoryStream()));
        engine.SeedRandomForTests(seed);
        typeof(CombatEngine).GetField("_combatOwner", F)!.SetValue(engine, hero);
        typeof(CombatEngine).GetField("currentPlayer", F)!.SetValue(engine, hero);
        engine.BossContext = ctx;
        var companion = Companion();
        var result = new CombatResult { Player = hero, Teammates = new List<Character> { companion }, CombatLog = new List<string>(), CurrentRound = 2 };
        ((Task)typeof(CombatEngine).GetMethod("MonsterAttacksCompanion", F)!
            .Invoke(engine, new object?[] { god, companion, result, new List<Monster> { god } })!).GetAwaiter().GetResult();
        return result.CombatLog;
    }

    [Fact]
    public void CounterAbilities135_Companion_WithTheCounter_NoBlockedAbilityRunsOnACompanion_TacticsOffAndOn()
    {
        bool savedTactics = GameConfig.MonsterTacticsEnabled;
        bool savedManwe = CombatEngine.IsManweBattle;
        try
        {
            bool earthquakeSeen = false;
            foreach (var god in CounterAbilities135Tests.Countered)
            {
                CombatEngine.IsManweBattle = god == OldGodType.Manwe;
                var hero = CounterAbilities135Tests.Hero(main: god);
                var fight = OldGodBossSystem.BuildBossFight(god, 0, BossDialogue.None, hero);
                fight.Context.CounterBlockedAbilities.Should().NotBeNull(god.ToString());
                // an Old God fight with tactics off and on (BossContext set, so the raw SpecialAbilities draw runs
                // either way), and the MonsterTactics.Choose path (no context)
                var configs = new (bool Tactics, BossCombatContext? Ctx, string Label)[]
                {
                    (false, fight.Context, "tactics off"), (true, fight.Context, "tactics on, god fight"), (true, null, "tactics on, Choose"),
                };
                for (int phase = 1; phase <= 3; phase++)
                {
                    fight.Context.CurrentPhase = phase;
                    // the pool PlayBossPhaseTransition writes for this phase, which holds the blocked names
                    var pool = fight.Context.BossData.Abilities.Where(a => a.Phase <= phase).Select(a => a.Name).ToList();
                    foreach (var (tactics, ctx, label) in configs)
                    {
                        GameConfig.MonsterTacticsEnabled = tactics;
                        for (int seed = 0; seed < 200; seed++)
                        {
                            fight.Monster.SpecialAbilities = new List<string>(pool);
                            fight.Monster.HP = fight.Monster.MaxHP;
                            var log = HitCompanion(fight.Monster, hero, ctx, 13500 + seed);
                            foreach (var line in log)
                                AllBlocked.Should().NotContain(b => line.Contains(b), $"{god} phase {phase} {label} seed {seed}: {line}");
                            if (god == OldGodType.Terravok && log.Any(l => l.Contains("Earthquake"))) earthquakeSeen = true;
                        }
                    }
                }
            }
            // the control: a god ability that parses (Terravok's Earthquake) does run on a companion and is logged
            earthquakeSeen.Should().BeTrue();
        }
        finally
        {
            GameConfig.MonsterTacticsEnabled = savedTactics;
            CombatEngine.IsManweBattle = savedManwe;
        }
    }
}
