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
using UsurperRemake.Locations;
using UsurperRemake.Server;
using UsurperRemake.Systems;
using UsurperReborn.Tests.Localization;
using Xunit;

namespace UsurperReborn.Tests;

/// <summary>
/// 1.3.11 K11 (his pick, Faith only): members of The Faith pay less at the Healer by FactionSystem.GetHealingPriceModifier,
/// 15 to 35 percent by rank, on every price the Healer charges and every menu line that states one, before tax. Nobody
/// else's price changes, a reader outside the Healer keeps today's price, Blessed Rain stays free. The alignment and
/// world event price lines, which nothing applied, are gone. Runs with the Healer's tax on. All in memory.
/// </summary>
[Collection("SharedGameSingletons")]
public sealed class HealerFaith1311Tests : IDisposable
{
    private const BindingFlags F = BindingFlags.NonPublic | BindingFlags.Instance;
    private const BindingFlags SF = BindingFlags.NonPublic | BindingFlags.Static;
    private static readonly string[] Langs = { "en", "es", "fr", "hu", "it" };
    private const int Level = 20;

    private readonly King? _kingBefore = CastleLocation.GetCurrentKing();
    private readonly bool _pacing = Pacing.Disabled;
    private readonly FactionSaveData _factionsBefore = FactionSystem.Instance.Serialize();
    private readonly List<WorldEventSystem.WorldEvent> _eventsBefore = WorldEventSystem.Instance.GetActiveEvents();

    public HealerFaith1311Tests()
    {
        Pacing.Disabled = true;
        Events().Clear();
        var king = King.CreateNewKing("Gs Ruler", CharacterAI.Human, CharacterSex.Male);
        king.KingTaxPercent = 5;
        king.CityTaxPercent = 2;
        CastleLocation.SetKing(king);
    }

    public void Dispose()
    {
        CastleLocation.SetKing(_kingBefore!);
        Pacing.Disabled = _pacing;
        FactionSystem.Instance.Reset();
        FactionSystem.Instance.Deserialize(_factionsBefore);
        Events().Clear();
        Events().AddRange(_eventsBefore);
        typeof(WorldEventSystem).GetMethod("RecalculateGlobalModifiers", F)!.Invoke(WorldEventSystem.Instance, null);
    }

    private static List<WorldEventSystem.WorldEvent> Events() =>
        (List<WorldEventSystem.WorldEvent>)typeof(WorldEventSystem).GetField("_activeEvents", F)!.GetValue(WorldEventSystem.Instance)!;

    private static void Member(Faction? faction, int rank)
    {
        FactionSystem.Instance.Reset();
        var data = FactionSystem.Instance.Serialize();
        data.PlayerFaction = faction.HasValue ? (int)faction.Value : -1;
        data.FactionRank = rank;
        FactionSystem.Instance.Deserialize(data);
    }

    /// <summary>member: none, crown8 (another faction), or faith0, faith1, faith8 (the rank). Returns the share a member pays.</summary>
    private static decimal Join(string member)
    {
        switch (member)
        {
            case "none": Member(null, 0); return 1m;
            case "crown8": Member(Faction.TheCrown, 8); return 1m;
            case "faith0": Member(Faction.TheFaith, 0); return 0.85m;
            case "faith1": Member(Faction.TheFaith, 1); return 0.825m;
            case "faith8": Member(Faction.TheFaith, 8); return 0.65m;
            default: throw new ArgumentException(member);
        }
    }

    private static long Pay(long basePrice, decimal share) => (long)Math.Round(basePrice * share, MidpointRounding.AwayFromZero);
    private static long Cure(int baseCost) => (long)(baseCost * (1 + Math.Pow(Level, 0.6)));
    private static long Taxed(long price) => CityControlSystem.CalculateHealingTaxedPrice(price).total;

    private static Character Hero() => new()
    {
        Name1 = "gs", Name2 = "Gs Hero", Class = CharacterClass.Warrior, Race = CharacterRace.Human, Level = Level,
        HP = 500, MaxHP = 1000, BaseMaxHP = 1000, Mental = 50, Gold = 10_000_000, AI = CharacterAI.Human, Healing = 0, ManaPotions = 0, Antidotes = 0,
    };

    private sealed class Screen
    {
        public TerminalEmulator Term = null!;
        public MemoryStream Output = new();
        public string Text
        {
            get
            {
                Term.StreamWriterInternal?.Flush();
                return Regex.Replace(Encoding.UTF8.GetString(Output.ToArray()), "\u001b\\[[0-9;?]*[A-Za-z]", "").Replace("\r", "");
            }
        }
    }

    private static Screen At(BaseLocation place, Character hero, params string[] lines)
    {
        var s = new Screen();
        s.Term = new TerminalEmulator(new LineStream(lines.Concat(Enumerable.Repeat("", 12))), s.Output);
        typeof(BaseLocation).GetField("terminal", F)!.SetValue(place, s.Term);
        typeof(BaseLocation).GetField("currentPlayer", F)!.SetValue(place, hero);
        return s;
    }

    private static async Task Run(object target, string method)
    {
        try
        {
            var r = target.GetType().GetMethod(method, F)!.Invoke(target, null);
            if (r is Task t) await t;
        }
        catch (TargetInvocationException ex) when (ex.InnerException != null) { throw ex.InnerException; }
    }

    private static IEnumerable<string> Members => new[] { "none", "crown8", "faith0", "faith1", "faith8" };

    public static IEnumerable<object[]> Each(params string[] services) =>
        from s in services from m in Members select new object[] { s, m };

    public static IEnumerable<object[]> AllServices => Each("heal_hp", "full_heal", "potions", "mana", "antidotes", "poison",
        "disease_one", "disease_all", "decurse", "rehab", "therapy", "willow");

    // ---------- every path: what is charged, before tax, and what the path's own quote says ----------

    [Theory]
    [MemberData(nameof(AllServices))]
    public async Task EveryPrice_IsTheDiscountedBase_ThenTax_AndTheQuoteSaysSo(string service, string member)
    {
        decimal share = Join(member);
        var hero = Hero();
        var healer = new HealerLocation();
        string method;
        string[] lines;
        long charge;
        var quotes = new List<string>();
        switch (service)
        {
            case "heal_hp":
                method = "HealHP"; lines = new[] { "100" }; charge = Taxed(Pay(200, share));
                quotes.Add(Loc.Get("healer.base_cost_info", (2 * share).ToString("0.##")));
                quotes.Add(Loc.Get("shop.healing_cost", 500, $"{Taxed(Pay(1000, share)):N0}"));
                break;
            case "full_heal":
                method = "FullHeal"; lines = new[] { "Y" }; charge = Taxed(Pay(1000, share));
                quotes.Add(Loc.Get("healer.full_restore_cost", $"{charge:N0}", "Jadu"));
                break;
            case "potions":
                method = "BuyPotions"; lines = new[] { "3" };
                long potion = Pay(GameConfig.GetHealingPotionCost(Level), share);
                charge = Taxed(3 * potion);
                quotes.Add(Loc.Get("healer.potions_price", Taxed(potion), "Jadu"));
                break;
            case "mana":
                method = "BuyManaPotions"; lines = new[] { "3" };
                long mana = Pay(GameConfig.GetManaPotionCost(Level), share);
                charge = Taxed(3 * mana);
                quotes.Add(Loc.Get("healer.mana_price", Taxed(mana), 30 + Level * 5, "Jadu"));
                break;
            case "antidotes":
                method = "BuyAntidotes"; lines = new[] { "2" };
                long antidote = Pay(75, share);
                charge = Taxed(2 * antidote);
                quotes.Add(Loc.Get("healer.antidote_price", Taxed(antidote)));
                break;
            case "poison":
                method = "CurePoison"; lines = new[] { "Y" }; hero.Poison = 5; hero.PoisonTurns = 5;
                charge = Taxed(Pay(Cure(200), share));
                quotes.Add(Loc.Get("healer.purge_cost", $"{Pay(Cure(200), share):N0}"));
                break;
            case "disease_one":
                method = "CureDisease"; lines = new[] { "B", "Y" }; hero.Blind = true; hero.Plague = true;
                charge = Taxed(Pay(Cure(1500), share));
                quotes.Add($"{Pay(Cure(1500), share):N0} {Loc.Get("healer.disease_gold")}");
                quotes.Add(Loc.Get("healer.cure_all", $"{Pay(Cure(1500), share) + Pay(Cure(2000), share):N0}"));
                break;
            case "disease_all":
                method = "CureDisease"; lines = new[] { "C", "Y" }; hero.Blind = true; hero.Plague = true;
                charge = Taxed(Pay(Cure(1500), share) + Pay(Cure(2000), share));
                quotes.Add(Loc.Get("healer.cure_all", $"{Pay(Cure(1500), share) + Pay(Cure(2000), share):N0}"));
                break;
            case "decurse":
                method = "RemoveCursedItem"; lines = new[] { "Y" }; hero.RHand = 1; hero.WeaponCursed = true;
                charge = Taxed(Pay(Cure(500), share));
                quotes.Add(Loc.Get("healer.curse_cost", $"{Pay(Cure(500), share):N0}"));
                break;
            case "rehab":
                method = "CureAddiction"; lines = new[] { "Y" }; hero.Addict = 30;
                long rehab = Pay(GameConfig.RehabBaseCost, share), surcharge = Pay(30 * GameConfig.RehabPerAddictionCost, share);
                charge = Taxed(rehab + surcharge);
                quotes.Add(Loc.Get("healer.treatment_cost_quote", $"{rehab + surcharge:N0}"));
                quotes.Add(Loc.Get("healer.base_cost_label", $"{rehab:N0}"));
                quotes.Add(Loc.Get("healer.surcharge", $"{surcharge:N0}"));
                break;
            case "therapy":
                method = "TalkTherapy"; lines = new[] { "Y" };
                charge = Taxed(Pay(MentalSystem.TherapyCost(hero), share));
                quotes.Add(Loc.Get("healer.therapy_quote", hero.Mental, GameConfig.MaxMentalStability, $"{charge:N0}"));
                break;
            case "willow":
                method = "BuyWillowDraught"; lines = new[] { "Y" };
                charge = Taxed(Pay(MentalSystem.WillowDraughtPrice(Level), share));
                quotes.Add(Loc.Get("healer.willow_price", $"{charge:N0}", 0, GameConfig.MaxWillowDraughts));
                break;
            default: throw new ArgumentException(service);
        }
        var s = At(healer, hero, lines);
        long gold = hero.Gold;
        await Run(healer, method);
        (gold - hero.Gold).Should().Be(charge, "{0} for {1}", service, member);
        foreach (var quote in quotes) s.Text.Should().Contain(quote, "{0} for {1}", service, member);
    }

    [Theory]
    [InlineData("none", 1000)]
    [InlineData("faith0", 850)]
    [InlineData("faith1", 825)]
    [InlineData("faith8", 650)]
    public async Task TheNumbers_Rank0Is15Off_Rank8Is35Off_NoOneElse(string member, long paidBeforeTax)
    {
        Join(member);
        CastleLocation.SetKing(null!);
        var hero = Hero();
        var healer = new HealerLocation();
        At(healer, hero, "Y");
        long gold = hero.Gold;
        await Run(healer, "FullHeal");
        (gold - hero.Gold).Should().Be(paidBeforeTax);
    }

    // ---------- the full menu states what is charged ----------

    [Theory]
    [InlineData("none")] [InlineData("crown8")] [InlineData("faith0")] [InlineData("faith1")] [InlineData("faith8")]
    public void TheFullMenu_StatesTheSamePrices(string member)
    {
        decimal share = Join(member);
        var hero = Hero();
        hero.Addict = 30;
        var healer = new HealerLocation();
        var s = At(healer, hero);
        typeof(HealerLocation).GetMethod("ShowFullMenu", F)!.Invoke(healer, null);
        string text = s.Text;
        foreach (var (key, args) in new (string, object[])[]
        {
            ("healer.menu_heal_hp", new object[] { (2 * share).ToString("0.##") }),
            ("healer.menu_buy_potions", new object[] { Pay(GameConfig.GetHealingPotionCost(Level), share) }),
            ("healer.menu_mana_potions", new object[] { Pay(GameConfig.GetManaPotionCost(Level), share) }),
            ("healer.menu_poison_cure", new object[] { $"{Pay(Cure(200), share):N0}" }),
            ("healer.menu_blindness", new object[] { $"{Pay(Cure(1500), share):N0}" }),
            ("healer.menu_plague", new object[] { $"{Pay(Cure(2000), share):N0}" }),
            ("healer.menu_smallpox", new object[] { $"{Pay(Cure(2500), share):N0}" }),
            ("healer.menu_measles", new object[] { $"{Pay(Cure(3000), share):N0}" }),
            ("healer.menu_leprosy", new object[] { $"{Pay(Cure(3500), share):N0}" }),
            ("healer.menu_lovers_bane", new object[] { $"{Pay(Cure(1000), share):N0}" }),
            ("healer.menu_decurse", new object[] { $"{Pay(Cure(500), share):N0}" }),
            ("healer.menu_addiction", new object[] { $"{Pay(GameConfig.RehabBaseCost, share) + Pay(30 * GameConfig.RehabPerAddictionCost, share):N0}" }),
            ("healer.menu_therapy", new object[] { $"{Taxed(Pay(MentalSystem.TherapyCost(hero), share)):N0}" }),
            ("healer.menu_willow", new object[] { $"{Taxed(Pay(MentalSystem.WillowDraughtPrice(Level), share)):N0}", GameConfig.MentalWillowDraughtGain, GameConfig.MaxWillowDraughts }),
        })
            text.Should().Contain(Loc.Get(key, args), "{0} for {1}", key, member);
    }

    [Theory]
    [InlineData("faith0")] [InlineData("faith8")]
    public async Task TheStatusScreen_StatesTheDiscountedRehab(string member)
    {
        decimal share = Join(member);
        var hero = Hero();
        hero.Addict = 30;
        var healer = new HealerLocation();
        var s = At(healer, hero);
        await Run(healer, "DisplayPlayerStatus");
        s.Text.Should().Contain(Loc.Get("healer.rehab_available", $"{Pay(GameConfig.RehabBaseCost, share) + Pay(30 * GameConfig.RehabPerAddictionCost, share):N0}"));
    }

    [Theory]
    [InlineData("faith0", 100)]
    [InlineData("faith1", 137)]
    [InlineData("faith8", 100)]
    public async Task TheHpYouCanAfford_IsWhatTheGoldPaysFor(string member, long gold)
    {
        Join(member);
        var hero = Hero();
        hero.Gold = gold;
        var healer = new HealerLocation();
        var s = At(healer, hero, "500");
        await Run(healer, "HealHP");
        hero.Gold.Should().Be(gold, "too dear: refused");
        var m = Regex.Match(s.Text, Regex.Escape(Loc.Get("healer.can_afford_up_to", 987654)).Replace("987654", @"(\d+)"));
        m.Success.Should().BeTrue();
        long hp = long.Parse(m.Groups[1].Value);
        hp.Should().BeGreaterThan(50, "a member's gold goes further than the base rate of 2");

        // that many is exactly what the gold pays for: one more is too dear
        var again = Hero();
        again.Gold = gold;
        At(healer, again, hp.ToString());
        await Run(healer, "HealHP");
        again.HP.Should().Be(500 + (int)hp);
        var over = Hero();
        over.Gold = gold;
        At(healer, over, (hp + 1).ToString());
        await Run(healer, "HealHP");
        over.Gold.Should().Be(gold);
    }

    // ---------- Blessed Rain stays free ----------

    [Theory]
    [InlineData("HealHP", "100")]
    [InlineData("FullHeal", "Y")]
    public async Task BlessedRain_StaysFree_ForAMember(string method, string line)
    {
        Join("faith8");
        Events().Add(new WorldEventSystem.WorldEvent { Type = WorldEventSystem.EventType.BlessedRain, Title = "Blessed Rain", Description = "", DaysRemaining = 2 });
        var hero = Hero();
        long gold = hero.Gold;
        var healer = new HealerLocation();
        At(healer, hero, line);
        await Run(healer, method);
        hero.Gold.Should().Be(gold);
        hero.HP.Should().BeGreaterThan(500);
    }

    // ---------- readers outside the Healer keep today's price ----------

    [Fact]
    public async Task TheMarketStall_HasNoFaithDiscount()
    {
        Join("faith8");
        var hero = Hero();
        long gold = hero.Gold;
        var stall = new SettlementLocation();
        At(stall, hero, "1", "", "0");
        await Run(stall, "UseMarketService");
        (gold - hero.Gold).Should().Be(Taxed(GameConfig.GetHealingPotionCost(Level)));
    }

    [Fact]
    public void ThePriceMethodsOutsideTheHealer_DoNotMove_ForAMember()
    {
        Join("none");
        var hero = Hero();
        long therapy = MentalSystem.TherapyCost(hero), willow = MentalSystem.WillowDraughtPrice(Level), potion = GameConfig.GetHealingPotionCost(Level);
        Join("faith8");
        MentalSystem.TherapyCost(hero).Should().Be(therapy);
        MentalSystem.WillowDraughtPrice(Level).Should().Be(willow);
        GameConfig.GetHealingPotionCost(Level).Should().Be(potion);
    }

    // ---------- the discount reads the session's own faction online ----------

    [Fact]
    public void Online_TheDiscount_ReadsThisSessionsFaction_NotAnother()
    {
        var fallback = typeof(FactionSystem).GetField("_fallbackInstance", SF)!;
        var before = fallback.GetValue(null);
        var price = typeof(HealerLocation).GetMethod("FaithPrice", SF);
        price.Should().NotBeNull();
        try
        {
            Member(null, 0);
            var mine = new FactionSystem();
            var data = mine.Serialize();
            data.PlayerFaction = (int)Faction.TheFaith;
            data.FactionRank = 8;
            mine.Deserialize(data);
            var other = new FactionSystem();
            SessionContext.Current = new SessionContext { Factions = mine };
            ((long)price!.Invoke(null, new object[] { 100L })!).Should().Be(65, "this session's player is in The Faith");
            SessionContext.Current = new SessionContext { Factions = other };
            ((long)price.Invoke(null, new object[] { 100L })!).Should().Be(100, "this session's player is not");
        }
        finally
        {
            SessionContext.Current = null;
            fallback.SetValue(null, before);
        }
    }

    // ---------- the false lines are gone, the true text is in ----------

    [Fact]
    public void TheUncalledMethod_IsGone()
    {
        typeof(HealerLocation).GetMethod("GetAdjustedPrice", F).Should().BeNull();
    }

    [Fact]
    public void TheMenu_ShowsNoAlignmentOrWorldEventPriceLine_AndBlessedRainStays()
    {
        var price = typeof(WorldEventSystem).GetProperty("GlobalPriceModifier")!;
        float before = WorldEventSystem.Instance.GlobalPriceModifier;
        try
        {
            price.SetValue(WorldEventSystem.Instance, 0.8f);
            foreach (var key in new[] { "healer.alignment_discount", "healer.alignment_markup", "healer.world_discount", "healer.world_markup" })
                foreach (var lang in Langs) Loc.HasIn(lang, key).Should().BeFalse("{0} {1}", lang, key);
            var hero = Hero();
            hero.Chivalry = 10_000;
            var healer = new HealerLocation();
            var s = At(healer, hero);
            typeof(HealerLocation).GetMethod("ShowMenu", F)!.Invoke(healer, null);
            s.Text.Should().NotContain("alignment").And.NotContain("World Events").And.NotContain("discount").And.NotContain("markup");
            Events().Add(new WorldEventSystem.WorldEvent { Type = WorldEventSystem.EventType.BlessedRain, Title = "Blessed Rain", Description = "", DaysRemaining = 2 });
            var rain = At(healer, hero);
            typeof(HealerLocation).GetMethod("ShowMenu", F)!.Invoke(healer, null);
            rain.Text.Should().Contain(Loc.Get("healer.blessed_rain_free"));
        }
        finally { price.SetValue(WorldEventSystem.Instance, before); }
    }

    [Theory]
    [InlineData("base.faction_faith_bonus")]
    [InlineData("temple.benefit_healing")]
    [InlineData("temple.receive_discount")]
    public void TheFaithText_StatesTheRealRange(string key)
    {
        foreach (var lang in Langs)
        {
            string text = Loc.GetIn(lang, key);
            text.Should().Contain("15").And.Contain("35").And.NotContain("25", "{0} {1}", lang, key);
        }
        foreach (var lang in new[] { "en", "hu" })
            Loc.GetIn(lang, key).Length.Should().BeLessOrEqualTo(79, lang);
    }
}
