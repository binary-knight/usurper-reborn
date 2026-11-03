using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using FluentAssertions;
using UsurperRemake;
using UsurperRemake.Data;
using UsurperRemake.Locations;
using UsurperRemake.Systems;
using Xunit;

namespace UsurperReborn.Tests;

/// <summary>
/// 1.3.10 audit, piece FS (settlements), his picks P6A and P12A:
/// P12. Outpost and street merchant healing and mana potions cost the town price for the buyer's level x1.25
///      (they cost 25 or 30 x (1 + floor / 10), or 40 + 3 x level, a few percent of the town price late on).
/// L4-11. The Rat King's Market sold "Smoke Bomb (flee aid)", which gives +2 Agility for one fight and no flee help
///      (flight rolls on Dexterity; the real smoke bomb is the Black Market's guaranteed escape). It is renamed to what
///      it does: Smoke Powder (+2 Agility), in every language.
/// P12. A fifth outpost, the Cinder Post on floor 85 (the deep floors had none after 65): floor 65's services and heal
///      factors, with the outposts' own goods and the top herbs; its text is keyed in every language.
/// P6.  The Barracks, voted, funded and built with no service, now serves once Built: a settler militia fighter at the
///      player's level minus 2 joins each of the next 3 monster fights (a timed settlement buff) and leaves at each end.
/// All in memory.
/// </summary>
[Collection("SharedGameSingletons")]
public class Settlement1310Tests : IDisposable
{
    private const BindingFlags F = BindingFlags.NonPublic | BindingFlags.Instance;

    private readonly bool _pacingWas = Pacing.Disabled;
    private readonly bool _srWas = GameConfig.ScreenReaderMode;

    public Settlement1310Tests()
    {
        Pacing.Disabled = true;
        GameConfig.ScreenReaderMode = false;
    }

    public void Dispose()
    {
        Pacing.Disabled = _pacingWas;
        GameConfig.ScreenReaderMode = _srWas;
    }

    // ---------- helpers ----------

    private sealed class Screen
    {
        public TerminalEmulator Term = null!;
        public MemoryStream Output = new();
        public string Text
        {
            get
            {
                Term.StreamWriterInternal?.Flush();
                return Regex.Replace(Encoding.UTF8.GetString(Output.ToArray()), "\u001b\\[[0-9;?]*[A-Za-z]", "");
            }
        }
    }

    private static Screen At(BaseLocation loc, Character hero, params string[] lines)
    {
        var s = new Screen();
        s.Term = new TerminalEmulator(new LineStream(lines.Concat(Enumerable.Repeat("", 30))), s.Output);
        typeof(BaseLocation).GetField("terminal", F)!.SetValue(loc, s.Term);
        typeof(BaseLocation).GetField("currentPlayer", F)!.SetValue(loc, hero);
        return s;
    }

    private static async Task Run(object target, string method, params object?[] args)
    {
        var m = target.GetType().GetMethod(method, F);
        m.Should().NotBeNull(method);
        try
        {
            var r = m!.Invoke(target, args);
            if (r is Task t)
            {
                (await Task.WhenAny(t, Task.Delay(TimeSpan.FromSeconds(60)))).Should().BeSameAs(t, "{0} must return", method);
                await t;
            }
        }
        catch (TargetInvocationException ex) when (ex.InnerException != null) { throw ex.InnerException; }
    }

    private static T Call<T>(object target, string method, params object?[] args)
    {
        var m = target.GetType().GetMethod(method, F);
        m.Should().NotBeNull(method);
        return (T)m!.Invoke(target, args)!;
    }

    private static Character Hero(string name, int level = 30, CharacterClass cls = CharacterClass.Warrior) => new Character
    {
        Name1 = name, Name2 = name, AI = CharacterAI.Human, Class = cls, Race = CharacterRace.Human, Level = level,
        HP = 100000, MaxHP = 100000, Gold = 100_000_000, Experience = 1000000,
        Agility = 1, Dexterity = 1, Constitution = 1, Wisdom = 1, Mental = 50, MentalHintShown = true,
    };

    private static (DungeonLocation d, Screen s) Dungeon(Character hero, int floor, params string[] lines)
    {
        var d = new DungeonLocation();
        var s = At(d, hero, lines);
        typeof(DungeonLocation).GetField("currentDungeonLevel", F)!.SetValue(d, floor);
        return (d, s);
    }

    private static List<(string id, string name, long cost)> Goods(Character hero, int floor)
    {
        var (d, _) = Dungeon(hero, floor);
        return Call<List<(string id, string name, long cost)>>(d, "GetSettlementTradeItems", DungeonSettlementData.GetSettlement(floor));
    }

    private static long Cost(List<(string id, string name, long cost)> goods, string id) => goods.Single(g => g.id == id).cost;

    private static List<(string name, long price)> StreetWares(int level, bool shady)
    {
        var list = (IEnumerable)typeof(StreetEncounterSystem).GetMethod("GenerateMerchantItems", F)!
            .Invoke(StreetEncounterSystem.Instance, new object[] { level, shady })!;
        var result = new List<(string, long)>();
        foreach (var item in list)
        {
            var t = item.GetType();
            result.Add(((string)t.GetField("Name")!.GetValue(item)!, (long)t.GetField("Price")!.GetValue(item)!));
        }
        return result;
    }

    // ---------- L4-11: the outpost smoke bomb says what it does ----------

    private static readonly string[] Langs = { "en", "es", "fr", "hu", "it" };

    [Fact]
    public void The_outpost_smoke_powder_names_its_agility_and_promises_no_flight()
    {
        var expected = new Dictionary<string, (string agility, string[] notWords)>
        {
            ["en"] = ("+2 Agility", new[] { "flee", "Bomb" }),
            ["es"] = ("+2 Agilidad", new[] { "huir", "Bomba" }),
            ["fr"] = ("+2 Agilit\u00e9", new[] { "fuir", "Fumig\u00e8ne (" }),
            ["hu"] = ("+2 Mozg\u00e9konys\u00e1g", new[] { "menek\u00fcl", "bomba" }),
            ["it"] = ("+2 Agilit\u00e0", new[] { "fuga", "Fumogeno (" }),
        };
        foreach (var lang in Langs)
        {
            string name = Loc.GetIn(lang, "dungeon.settlement_item_smoke_bomb");
            var (agility, notWords) = expected[lang];
            name.Should().Contain(Regex.Unescape(agility), lang);
            foreach (var w in notWords) name.Should().NotContain(Regex.Unescape(w), lang);
            name.Length.Should().BeLessThanOrEqualTo(29, lang);
        }
        foreach (var lang in new[] { "en", "hu" })
            Loc.GetIn(lang, "dungeon.settlement_item_smoke_bomb").Length.Should().BeLessThanOrEqualTo(25, $"{lang} fits the 25 column name field");
    }

    [Fact]
    public void The_outpost_smoke_powder_still_gives_two_agility_for_one_fight_and_no_smoke_bomb()
    {
        var hero = Hero("Powder Buyer");
        var (d, _) = Dungeon(hero, 20);
        typeof(DungeonLocation).GetMethod("ApplySettlementPurchase", F)!.Invoke(d, new object[] { hero, "smoke_bomb" });
        hero.SmokeBombs.Should().Be(0, "it is not the Black Market's guaranteed escape");
        hero.TimedStatBuffs.Should().ContainSingle(b => b.Source == "settlement_smoke_bomb" && b.Stat == StatKind.Agility && b.Amount == 2 && b.CombatsLeft == 1);
    }

    // ---------- P6: the Barracks militia ----------

    /// <summary>Runs the body with a fresh settlement state that has the given proposed buildings, then puts the old one back.</summary>
    private static async Task WithSettlement(Func<Task> body, params (string id, BuildingTier tier)[] built)
    {
        var saved = SettlementSystem.Instance.State;
        try
        {
            var state = new SettlementState();
            foreach (var (id, tier) in built) state.ProposedBuildings[id] = new ProposedBuildingState { Id = id, Tier = tier };
            SettlementSystem.Instance.State = state;
            await body();
        }
        finally { SettlementSystem.Instance.State = saved; }
    }

    [Fact]
    public async Task The_barracks_is_a_service_once_built_and_not_before()
    {
        await WithSettlement(() =>
        {
            SettlementSystem.Instance.GetProposedServices().Should().NotContain(s => s.buildingId == "barracks");
            return Task.CompletedTask;
        }, ("barracks", BuildingTier.Foundation));
        await WithSettlement(() =>
        {
            var svc = SettlementSystem.Instance.GetProposedServices();
            svc.Should().ContainSingle(s => s.buildingId == "barracks");
            svc.Single(s => s.buildingId == "barracks").label.Should().Be(Loc.Get("settlement.psvc.barracks"));
            return Task.CompletedTask;
        }, ("barracks", BuildingTier.Built));
        // the services already there keep their keys
        await WithSettlement(() =>
        {
            var svc = SettlementSystem.Instance.GetProposedServices();
            svc.Single(s => s.buildingId == "arena").key.Should().Be("8");
            svc.Single(s => s.buildingId == "barracks").key.Should().Be("9");
            return Task.CompletedTask;
        }, ("arena", BuildingTier.Upgraded), ("barracks", BuildingTier.Upgraded));
    }

    [Fact]
    public async Task The_barracks_service_gives_the_militia_for_three_fights()
    {
        var hero = Hero("Militia Captain", 30);
        var loc = new SettlementLocation();
        var screen = At(loc, hero);
        await Run(loc, "UseBarracksService");
        hero.SettlementBuffType.Should().Be((int)SettlementBuffType.Militia);
        hero.SettlementBuffCombats.Should().Be(3);
        hero.HasSettlementBuff.Should().BeTrue();
        screen.Text.Should().Contain(Loc.Get("settlement.gained_militia", 28, 3));
    }

    [Fact]
    public async Task The_barracks_waits_while_another_settlement_buff_is_held()
    {
        var hero = Hero("Busy Captain", 30);
        hero.SettlementBuffType = (int)SettlementBuffType.XPBonus;
        hero.SettlementBuffCombats = 4;
        hero.SettlementBuffValue = GameConfig.SettlementXPBonus;
        var loc = new SettlementLocation();
        var screen = At(loc, hero);
        await Run(loc, "UseBarracksService");
        hero.SettlementBuffType.Should().Be((int)SettlementBuffType.XPBonus);
        hero.SettlementBuffCombats.Should().Be(4);
        screen.Text.Should().Contain(Loc.Get("ui.settlement_buff_active"));
    }

    [Theory]
    [InlineData(30, 28)]
    [InlineData(2, 1)]
    [InlineData(1, 1)]
    public void The_militia_fighter_is_the_players_level_minus_two_with_the_tank_mercenarys_stats(int level, int expected)
    {
        var m = SettlementMilitia.Build(Hero("Lvl", level));
        m.Level.Should().Be(expected);
        m.IsSettlementMilitia.Should().BeTrue();
        m.IsMercenary.Should().BeFalse("a dead militia fighter is not a lost royal bodyguard");
        m.Class.Should().Be(CharacterClass.Warrior);
        m.AI.Should().Be(CharacterAI.Computer);
        m.DisplayName.Should().Be(Loc.Get("settlement.militia_name"));

        // the royal Tank mercenary at the same level (CastleLocation.GenerateMercenary)
        var castle = new CastleLocation();
        typeof(BaseLocation).GetField("currentPlayer", F)!.SetValue(castle, Hero("King", level));
        var merc = (RoyalMercenary)typeof(CastleLocation).GetMethod("GenerateMercenary", F)!.Invoke(castle, new object[] { "Tank", expected })!;
        (m.MaxHP, m.Strength, m.Defence, m.WeapPow, m.ArmPow).Should().Be((merc.MaxHP, merc.Strength, merc.Defence, merc.WeapPow, merc.ArmPow));
        (m.Agility, m.Dexterity, m.Constitution, m.Wisdom, m.Intelligence).Should().Be((merc.Agility, merc.Dexterity, merc.Constitution, merc.Wisdom, merc.Intelligence));
        (m.BaseMaxHP, m.BaseStrength, m.BaseDefence).Should().Be((merc.MaxHP, merc.Strength, merc.Defence));
    }

    private sealed class Scripted : Stream
    {
        private readonly byte[] _data; private int _pos;
        public Scripted(string script) { _data = Encoding.UTF8.GetBytes(script); }
        public override int Read(byte[] buffer, int offset, int count)
        {
            int n = Math.Min(count, _data.Length - _pos);
            if (n <= 0) return 0;
            Array.Copy(_data, _pos, buffer, offset, n); _pos += n; return n;
        }
        public override bool CanRead => true; public override bool CanSeek => false; public override bool CanWrite => false;
        public override long Length => _data.Length;
        public override long Position { get => _pos; set => throw new NotSupportedException(); }
        public override void Flush() { }
        public override long Seek(long o, SeekOrigin so) => throw new NotSupportedException();
        public override void SetLength(long v) => throw new NotSupportedException();
        public override void Write(byte[] b, int o, int c) => throw new NotSupportedException();
    }

    private static Character Fighter(string name) => new Character
    {
        Name2 = name, Class = CharacterClass.Warrior, Race = CharacterRace.Human, Level = 10,
        HP = 500, MaxHP = 500, BaseMaxHP = 500, Strength = 80, BaseStrength = 80, Defence = 40, BaseDefence = 40,
        Dexterity = 30, BaseDexterity = 30, Agility = 25, BaseAgility = 25, Constitution = 30, BaseConstitution = 30,
        Gold = 100, Stamina = 100, CombatSpeed = CombatSpeed.Instant, Mental = 80,
    };

    private static async Task<(CombatResult result, string text, List<Character> seen)> MilitiaFight(Character hero, List<Character>? party = null)
    {
        var output = new MemoryStream();
        var engine = new CombatEngine(new TerminalEmulator(new Scripted(string.Concat(Enumerable.Repeat("A\n", 10)) + string.Concat(Enumerable.Repeat("P\n", 6))), output));
        engine.SeedRandomForTests(1310);
        var rat = new Monster { Name = "Sewer Rat", Level = 1, HP = 1, MaxHP = 1, Strength = 1, Defence = 0, Experience = 5, Gold = 3 };
        var result = await engine.PlayerVsMonsters(hero, new List<Monster> { rat }, party, offerMonkEncounter: false);
        string text = Regex.Replace(Encoding.UTF8.GetString(output.ToArray()), "\u001b\\[[0-9;?]*[A-Za-z]", "");
        return (result, text, party ?? new List<Character>());
    }

    [Fact]
    public async Task The_militia_joins_each_fight_while_held_leaves_at_its_end_and_counts_three_fights()
    {
        var hero = Fighter("Militia Owner");
        hero.SettlementBuffType = (int)SettlementBuffType.Militia;
        hero.SettlementBuffCombats = GameConfig.SettlementMilitiaFights;
        var party = new List<Character>();
        for (int fight = 1; fight <= 3; fight++)
        {
            var (result, text, _) = await MilitiaFight(hero, party);
            result.Outcome.Should().Be(CombatOutcome.Victory, text);
            text.Should().Contain(Loc.Get("settlement.militia_joins", Loc.Get("settlement.militia_name"), 8), $"fight {fight}");
            result.Teammates.Should().NotContain(t => t.IsSettlementMilitia, "it leaves at the fight's end");
            party.Should().BeEmpty("it never joins the dungeon party");
            hero.SettlementBuffCombats.Should().Be(3 - fight);
        }
        hero.SettlementBuffType.Should().Be(0, "spent after three fights");
        var (_, after, _) = await MilitiaFight(hero, party);
        after.Should().NotContain(Loc.Get("settlement.militia_name"), "no militia once the three fights are spent");
    }

    [Fact]
    public async Task No_militia_joins_without_the_barracks_service()
    {
        var hero = Fighter("No Militia");
        hero.SettlementBuffType = (int)SettlementBuffType.XPBonus;
        hero.SettlementBuffCombats = 5;
        hero.SettlementBuffValue = GameConfig.SettlementXPBonus;
        var (result, text, _) = await MilitiaFight(hero);
        result.Outcome.Should().Be(CombatOutcome.Victory, text);
        text.Should().NotContain(Loc.Get("settlement.militia_name"));
    }

    [Fact]
    public async Task The_status_sheet_shows_the_militia_as_an_ally_not_a_percent()
    {
        var hero = Hero("Status Captain", 30);
        hero.SettlementBuffType = (int)SettlementBuffType.Militia;
        hero.SettlementBuffCombats = 2;
        var loc = new MainStreetLocation();
        var screen = At(loc, hero);
        await (Task)typeof(BaseLocation).GetMethod("ShowStatus", F)!.Invoke(loc, Array.Empty<object>())!;
        screen.Text.Should().Contain(Loc.Get("base.buff_settlement_militia", 2).Trim());
        screen.Text.Should().NotContain(Loc.Get("base.buff_settlement_default"));
    }

    [Fact]
    public void The_militia_rows_exist_in_every_language_and_fit_79_columns()
    {
        string[] keys = { "settlement.psvc.barracks", "settlement.barracks_desc", "settlement.militia_name" };
        foreach (var lang in Langs)
        {
            foreach (var k in keys.Concat(new[] { "settlement.gained_militia", "settlement.militia_joins", "base.buff_settlement_militia" }))
                Loc.GetIn(lang, k).Should().NotBe(k, lang);
            if (lang == "en" || lang == "hu")
            {
                foreach (var k in keys) Loc.GetIn(lang, k).Length.Should().BeLessThanOrEqualTo(79 - 6, $"{lang} {k}");
                Loc.GetIn(lang, "settlement.gained_militia", 98, 3).Length.Should().BeLessThanOrEqualTo(79, lang);
                Loc.GetIn(lang, "settlement.militia_joins", Loc.GetIn(lang, "settlement.militia_name"), 98).Length.Should().BeLessThanOrEqualTo(79, lang);
                Loc.GetIn(lang, "base.buff_settlement_militia", 3).Length.Should().BeLessThanOrEqualTo(79, lang);
            }
        }
    }

    // ---------- P12: the floor 85 outpost ----------

    [Fact]
    public void Floor_85_has_the_cinder_post_room_and_keeps_its_boss_room()
    {
        DungeonSettlementData.IsSettlementFloor(85).Should().BeTrue();
        var floor = DungeonGenerator.GenerateFloor(85);
        var outposts = floor.Rooms.Where(r => r.Type == RoomType.Settlement).ToList();
        outposts.Should().ContainSingle();
        outposts[0].Name.Should().Be("The Cinder Post");
        outposts[0].EventType.Should().Be(DungeonEventType.Settlement);
        outposts[0].HasMonsters.Should().BeFalse();
        outposts[0].HasTrap.Should().BeFalse();
        floor.Rooms.Should().Contain(r => r.Type == RoomType.BossAntechamber, "the Old God floor keeps its boss approach");
        floor.Rooms.Should().Contain(r => r.IsBossRoom);
        foreach (int other in new[] { 84, 86 })
            DungeonGenerator.GenerateFloor(other).Rooms.Should().NotContain(r => r.Type == RoomType.Settlement, $"floor {other}");
    }

    [Fact]
    public void The_cinder_post_has_floor_65s_services_and_deeper_stock()
    {
        var hearth = DungeonSettlementData.GetSettlement(65)!;
        var post = DungeonSettlementData.GetSettlement(85)!;
        post.Id.Should().Be("cinder_post");
        post.Floor.Should().Be(85);
        (post.HasHealing, post.HasTrading, post.HasLore).Should().Be((hearth.HasHealing, hearth.HasTrading, hearth.HasLore));
        post.HealCostMultiplier.Should().Be(hearth.HealCostMultiplier);
        post.HealEffectiveness.Should().Be(hearth.HealEffectiveness);
        post.TradeItems.Should().Contain(hearth.TradeItems, "everything the Last Hearth sells");
        post.TradeItems.Should().Contain(new[] { "Healing Herb", "Starbloom Essence" });

        var hero = Hero("Deep Buyer", 90);
        var goods = Goods(hero, 85);
        goods.Select(g => g.id).Should().Equal("heal_potion", "mana_potion", "antidote", "healing_herb", "firebloom", "starbloom");
        Cost(goods, "heal_potion").Should().Be((long)(GameConfig.GetHealingPotionCost(90) * 1.25));
        Cost(goods, "mana_potion").Should().Be((long)(GameConfig.GetManaPotionCost(90) * 1.25));
        Cost(goods, "antidote").Should().Be(20 * 9);
        Cost(goods, "healing_herb").Should().Be(40 * 9);
        Cost(goods, "firebloom").Should().Be(60 * 9);
        Cost(goods, "starbloom").Should().Be(80 * 9);
        var (d, _) = Dungeon(hero, 85);
        Call<long>(d, "CalculateSettlementHealCost", hero, post).Should().Be((long)((10 + 85 * 5) * 0.6f));
    }

    [Fact]
    public void The_cinder_post_text_is_keyed_in_every_language()
    {
        var post = DungeonSettlementData.GetSettlement(85)!;
        var fields = new[] { "npc_title", "description", "greeting_first", "greeting_return" }
            .Concat(Enumerable.Range(0, post.LoreFragments.Length).Select(i => $"lore.{i}")).ToList();
        fields.Should().HaveCount(8);
        foreach (var lang in Langs)
            foreach (var f in fields)
            {
                string key = $"dungeon.settlement.cinder_post.{f}";
                string v = Loc.GetIn(lang, key);
                v.Should().NotBe(key, $"{lang} {f}");
                v.Should().NotContain("\u2014").And.NotContain("\u2013");
                if (lang != "en") v.Should().NotBe(Loc.GetIn("en", key), $"{lang} {f} is translated");
            }
    }

    // ---------- P12: potion prices away from town ----------

    [Theory]
    [InlineData(65, 80)]
    [InlineData(10, 5)]
    [InlineData(35, 100)]
    public void Outpost_potions_cost_the_town_price_for_the_buyers_level_times_one_and_a_quarter(int floor, int level)
    {
        var goods = Goods(Hero("Buyer", level), floor);
        Cost(goods, "heal_potion").Should().Be((long)(GameConfig.GetHealingPotionCost(level) * 1.25));
        Cost(goods, "mana_potion").Should().Be((long)(GameConfig.GetManaPotionCost(level) * 1.25));
    }

    [Fact]
    public void A_level_80_buyer_at_the_last_hearth_pays_4125_for_a_healing_potion_not_175()
    {
        var goods = Goods(Hero("Late Buyer", 80), 65);
        Cost(goods, "heal_potion").Should().Be(4125);
        Cost(goods, "mana_potion").Should().Be(4187);
    }

    [Fact]
    public void Outpost_antidotes_herbs_and_other_goods_keep_their_floor_price()
    {
        var goods = Goods(Hero("Herb Buyer", 80), 65);
        Cost(goods, "antidote").Should().Be(20 * 7);
        Cost(goods, "firebloom").Should().Be(60 * 7);
        var forge = Goods(Hero("Forge Buyer", 80), 10);
        Cost(forge, "torch").Should().Be(15 * 2);
    }

    [Theory]
    [InlineData(1)]
    [InlineData(40)]
    [InlineData(80)]
    public void Street_merchant_potions_cost_the_town_price_times_one_and_a_quarter_and_the_bundles_follow(int level)
    {
        long one = (long)(GameConfig.GetHealingPotionCost(level) * 1.25);
        var normal = StreetWares(level, false);
        normal.Single(w => w.name == "Healing Potion").price.Should().Be(one);
        normal.Single(w => w.name == "Healing Potions (x5)").price.Should().Be((long)(one * 4.5));
        normal.Single(w => w.name == "Antidote").price.Should().Be(50 + level * 2, "only potions move to the town curve");
        var shady = StreetWares(level, true);
        shady.Single(w => w.name == "Healing Potions (x3)").price.Should().Be(one * 3);
    }
}
