using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using FluentAssertions;
using UsurperRemake;
using UsurperRemake.BBS;
using UsurperRemake.Systems;
using UsurperRemake.Utils;
using UsurperRemake.Locations;
using Xunit;

namespace UsurperReborn.Tests;

/// <summary>
/// 1.3.10 economy audit (piece FD): online team wages per player (D1), one shop price (D2), the carry cap before the
/// charge (D3), the offline auction withdraw and expiry (D4), discount and ability texts (D5), no hidden bank interest
/// (P10), the Fountain of Vitality as 10 percent (P11), Good Deeds, the bank ban and the Reinforced Door (P13), and the
/// removed counters (P14).
/// </summary>
[Collection("SharedGameSingletons")]
public class Economy1310Tests : IDisposable
{
    private const BindingFlags F = BindingFlags.NonPublic | BindingFlags.Instance;
    private const BindingFlags SF = BindingFlags.NonPublic | BindingFlags.Static;
    private static readonly FieldInfo OnlineField = typeof(DoorMode).GetField("_onlineMode", SF)!;
    internal static readonly string[] Langs = { "en", "es", "fr", "hu", "it" };

    private readonly bool _prevOnline;
    private readonly Character? _prevPlayer;
    private readonly TerminalEmulator? _prevTerminal;
    private readonly List<NPC> _addedNpcs = new();

    public Economy1310Tests()
    {
        Pacing.Disabled = true;
        _prevOnline = (bool)OnlineField.GetValue(null)!;
        _prevPlayer = GameEngine.Instance.CurrentPlayer;
        _prevTerminal = (TerminalEmulator?)typeof(TerminalEmulator).GetField("_fallbackInstance", SF)!.GetValue(null);
        MailSystem.ClearAllMail();
    }

    public void Dispose()
    {
        OnlineField.SetValue(null, _prevOnline);
        GameEngine.Instance.CurrentPlayer = _prevPlayer;
        typeof(TerminalEmulator).GetField("_fallbackInstance", SF)!.SetValue(null, _prevTerminal);
        foreach (var n in _addedNpcs) NPCSpawnSystem.Instance.ActiveNPCs.Remove(n);
        MailSystem.ClearAllMail();
    }

    // ------------------------------------------------------------------ helpers

    internal static string Plain(TerminalEmulator term, MemoryStream output)
    {
        term.StreamWriterInternal?.Flush();
        return Regex.Replace(Encoding.UTF8.GetString(output.ToArray()), "\u001b\\[[0-9;?]*[A-Za-z]", "").Replace("\r", "");
    }

    private static async Task Call(object o, string method, params object[] args)
    {
        try { await (Task)o.GetType().GetMethod(method, F)!.Invoke(o, args)!; }
        catch (TargetInvocationException ex) when (ex.InnerException != null) { throw ex.InnerException; }
    }

    private static (TerminalEmulator term, MemoryStream output) Term(params string[] lines)
    {
        var output = new MemoryStream();
        var term = new TerminalEmulator(new LineStream(lines.Concat(Enumerable.Repeat("", 20))), output);
        return (term, output);
    }

    private static void Wire(BaseLocation loc, TerminalEmulator term, Character hero)
    {
        typeof(BaseLocation).GetField("terminal", F)!.SetValue(loc, term);
        typeof(BaseLocation).GetField("currentPlayer", F)!.SetValue(loc, hero);
    }

    internal static Character Hero(string name = "Ec", int level = 10) => new Character
    {
        Name1 = name, Name2 = name, Class = CharacterClass.Warrior, Race = CharacterRace.Human, Level = level,
        HP = 500, MaxHP = 500, BaseMaxHP = 500, Gold = 10_000, AI = CharacterAI.Human, Strength = 30, BaseStrength = 30,
        Constitution = 20, BaseConstitution = 20, Stamina = 20, BaseStamina = 20,
    };

    private static PlayerData Save(Character c)
    {
        var data = (PlayerData)typeof(SaveSystem).GetMethod("SerializePlayer", F)!.Invoke(SaveSystem.Instance, new object[] { c })!;
        return JsonSerializer.Deserialize<PlayerData>(JsonSerializer.Serialize(data))!;
    }

    private static Character Load(PlayerData data)
    {
        try { return (Character)typeof(GameEngine).GetMethod("RestorePlayerFromSaveData", F)!.Invoke(GameEngine.Instance, new object[] { data })!; }
        catch (TargetInvocationException ex) when (ex.InnerException != null) { throw ex.InnerException; }
    }

    /// <summary>The single player save file's own JSON options (camel case), so an old file reads as the game reads it.</summary>
    private static JsonSerializerOptions FileOptions() =>
        (JsonSerializerOptions)typeof(FileSaveBackend).GetField("jsonOptions", F)!.GetValue(new FileSaveBackend())!;

    // ------------------------------------------------------------------ P10: no hidden bank interest

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task P10_TheDailyMaintenance_PaysNoHiddenInterest_TheBankPaysItsStatedRate(bool online)
    {
        OnlineField.SetValue(null, online);
        var hero = Hero();
        hero.BankGold = 1_000_000;
        GameEngine.Instance.CurrentPlayer = hero;
        var maint = new MaintenanceSystem(new TerminalUI());
        await Call(maint, "ProcessEconomicSystems", new MaintenanceConfig());
        hero.BankGold.Should().Be(1_000_000, "no 3 percent on top of the bank's rate");
        hero.Interest.Should().Be(0);
        MailSystem.MailFor(hero.Name2).Should().BeEmpty("no bank interest mail");

        BankLocation.ProcessDailyMaintenance(hero);
        hero.BankGold.Should().Be(1_001_000, "the bank pays the 0.1 percent it states");
        Loc.GetIn("en", "bank.interest_savings_rate").Should().Contain("0.1%");
    }

    // ------------------------------------------------------------------ P14: removed counters

    [Fact]
    public void P14_TheAliveBonus_TheTownPot_AndItemDurability_AreGone()
    {
        typeof(Character).GetProperty("AliveBonus").Should().BeNull();
        typeof(Item).GetProperty("Durability").Should().BeNull();
        typeof(Item).GetProperty("MaxDurability").Should().BeNull();
        typeof(Item).GetMethod("Repair").Should().BeNull();
        typeof(Item).GetMethod("TakeDamage").Should().BeNull();
        typeof(MaintenanceConfig).GetProperty("TownPotValue").Should().BeNull();
        typeof(MaintenanceConfig).GetProperty("BankInterest").Should().BeNull();
        typeof(GameConfig).GetField("AliveBonus").Should().BeNull();
        typeof(GameConfig).GetField("DefaultTownPot").Should().BeNull();
    }

    [Fact]
    public async Task P14_TheMaintenanceScreen_PrintsNoAliveBonus_NoTownPot_AndNoInterestRow()
    {
        var output = new MemoryStream();
        var term = new TerminalEmulator(new LineStream(Enumerable.Repeat("", 10)), output);
        typeof(TerminalEmulator).GetField("_fallbackInstance", SF)!.SetValue(null, term);
        var hero = Hero();
        hero.BankGold = 1_000_000;
        GameEngine.Instance.CurrentPlayer = hero;
        var maint = new MaintenanceSystem(new TerminalUI());
        typeof(MaintenanceSystem).GetField("silentMode", F)!.SetValue(maint, false);
        await Call(maint, "ProcessPlayerDailyMaintenance", hero, new MaintenanceConfig(), false);
        typeof(MaintenanceSystem).GetField("silentMode", F)!.SetValue(maint, false);
        await Call(maint, "ProcessEconomicSystems", new MaintenanceConfig());
        string shown = Plain(term, output);
        shown.Should().Contain(Loc.Get("maint.economy_complete"), "the screen ran");
        shown.Should().NotContain(Loc.Get("maint.alive_bonus", hero.Name2, hero.Level * 100), "the row the alive bonus printed");
        shown.Should().NotContain(Loc.Get("maint.town_pot", 5000), "the row the town pot printed");
        shown.Should().NotContain(Loc.Get("maint.bank_interest_row", 30000), "the row the 3 percent printed");
    }

    [Fact]
    public void P14_AnItemShowsNoConditionLabel()
    {
        var item = new Item { Name = "Ring of Plain Things" };
        item.GetDisplayName().Should().Be(ItemNames.Display("Ring of Plain Things"));
        foreach (var k in new[] { "item.condition.good", "item.condition.fair", "item.condition.poor", "item.condition.bad", "item.condition.broken" })
            item.GetDisplayName().Should().NotContain($"[{Loc.Get(k)}]");
    }

    [Fact]
    public void P14_AnOldSave_WithTheRemovedFields_Loads()
    {
        var hero = Hero("OldP14");
        hero.Inventory.Add(new Item { Name = "Worn Ring", Value = 10 });
        var opts = FileOptions();
        var node = JsonNode.Parse(JsonSerializer.Serialize(Save(hero), opts))!.AsObject();
        node["aliveBonus"] = 123_456;   // never saved by name, but a hand-edited or older file may carry it
        foreach (var it in node["inventory"]!.AsArray()) { it!["durability"] = 40; it["maxDurability"] = 100; }
        var data = JsonSerializer.Deserialize<PlayerData>(node.ToJsonString(), opts)!;
        var loaded = Load(data);
        loaded.Name2.Should().Be("OldP14");
        loaded.Inventory.Should().ContainSingle(i => i.Name == "Worn Ring");
        loaded.Inventory.Single().GetDisplayName().Should().Be(ItemNames.Display("Worn Ring"));

        var world = JsonNode.Parse("{\"bankInterestRate\":3,\"townPotValue\":5000,\"currentRuler\":null}")!.ToJsonString();
        var w = JsonSerializer.Deserialize<WorldStateData>(world, opts)!;
        w.TownPotValue.Should().Be(5000, "an old world save still reads; nothing uses the value");
    }

    // ------------------------------------------------------------------ P11: the Fountain of Vitality

    [Fact]
    public void P11_TheFountain_IsTenPercentOfMaxHP_AtEveryLevel()
    {
        var c = Hero("Fv");
        c.RecalculateStats();
        long plain = c.MaxHP;
        c.HasVitalityFountain = true;
        c.RecalculateStats();
        c.MaxHP.Should().Be((long)(plain * 1.10));
        c.BonusMaxHP.Should().Be(0, "no flat snapshot is stored");

        // levels later
        c.Level = 60;
        c.BaseMaxHP = 9_000;
        c.HasVitalityFountain = false;
        c.RecalculateStats();
        long plainLater = c.MaxHP;
        c.HasVitalityFountain = true;
        c.RecalculateStats();
        c.MaxHP.Should().Be((long)(plainLater * 1.10), "still 10 percent after levelling");
    }

    [Fact]
    public void P11_BuyingTheFountain_AddsTenPercent_AndSaysHowMuch()
    {
        var hero = Hero("FvBuy");
        hero.RecalculateStats();
        long plain = hero.MaxHP;
        var (term, output) = Term();
        var home = new HomeLocation();
        Wire(home, term, hero);
        hero.HasVitalityFountain = true;
        typeof(HomeLocation).GetMethod("ApplyFountainBonus", F)!.Invoke(home, null);
        long gain = hero.MaxHP - plain;
        gain.Should().Be((long)(plain * 1.10) - plain);
        hero.BonusMaxHP.Should().Be(0);
        Plain(term, output).Should().Contain(Loc.Get("home.fountain_hp_bonus", gain));
    }

    [Fact]
    public void P11_AnOldSave_WithTheFlatSnapshot_LoadsToTenPercent_NotBoth()
    {
        var hero = Hero("FvOld");
        hero.HasVitalityFountain = true;
        hero.RecalculateStats();
        var clean = Save(hero);
        var old = Save(hero);
        old.BonusMaxHP = 137;   // a fountain bought before 1.3.10 stored its 10 percent as this flat number
        old.HasVitalityFountain.Should().BeTrue();

        var expected = Load(clean).MaxHP;
        var loaded = Load(old);
        loaded.BonusMaxHP.Should().Be(0, "the old snapshot is dropped on load");
        loaded.MaxHP.Should().Be(expected, "10 percent, not 10 percent plus the old flat number");

        // the online defender load (PvP and the world sim) reads the save the same way
        var snap = PlayerCharacterLoader.CreateFromSaveData(old, "FvOld");
        snap.HasVitalityFountain.Should().BeTrue();
        snap.BonusMaxHP.Should().Be(0);
        PlayerCharacterLoader.CreateFromSaveData(clean, "FvOld").MaxHP.Should().Be(snap.MaxHP);

    }

    // ------------------------------------------------------------------ D1: online team wages per player

    private NPC TeamMate(string team, int level)
    {
        var npc = new NPC { Name1 = "Wage" + team, Name2 = "Wage" + team, Level = level, Team = team, HP = 100, MaxHP = 100, AI = CharacterAI.Computer };
        NPCSpawnSystem.Instance.ActiveNPCs.Add(npc);
        _addedNpcs.Add(npc);
        return npc;
    }

    [Fact]
    public async Task D1_Online_TheSharedMaintenancePass_ChargesNoWages_OfflineItStillDoes()
    {
        var hero = Hero("WgPass");
        hero.Team = "EcWagePass";
        var npc = TeamMate(hero.Team, 30);
        long wage = TeamSystem.GetDailyWage(npc);
        wage.Should().BeGreaterThan(0);
        var maint = new MaintenanceSystem(new TerminalUI());

        OnlineField.SetValue(null, true);
        await Call(maint, "ProcessPlayerDailyMaintenance", hero, new MaintenanceConfig(), true);
        hero.Gold.Should().Be(10_000, "online the wage is charged at the player's own reset, not by the shared pass");

        OnlineField.SetValue(null, false);
        await Call(maint, "ProcessPlayerDailyMaintenance", hero, new MaintenanceConfig(), true);
        hero.Gold.Should().Be(10_000 - wage, "offline the maintenance pass still charges it");
    }

    [Fact]
    public async Task D1_Online_TwoPlayersOnOneServerDay_EachPaysTheirWage_AtTheirOwnReset()
    {
        OnlineField.SetValue(null, true);
        var dsm = DailySystemManager.Instance;
        typeof(DailySystemManager).GetField("autoSaveEnabled", F)!.SetValue(dsm, false);
        try
        {
            var first = Hero("WgFirst");
            first.Team = "EcWageA";
            var second = Hero("WgSecond");
            second.Team = "EcWageB";
            long wageA = TeamSystem.GetDailyWage(TeamMate(first.Team, 30));
            long wageB = TeamSystem.GetDailyWage(TeamMate(second.Team, 40));

            foreach (var p in new[] { first, second })
            {
                GameEngine.Instance.CurrentPlayer = p;
                await Call(dsm, "PerformDailyReset", false);
            }
            first.Gold.Should().Be(10_000 - wageA, "the first player of the day pays once");
            second.Gold.Should().Be(10_000 - wageB, "the second player of the same server day pays too");
        }
        finally { typeof(DailySystemManager).GetField("autoSaveEnabled", F)!.SetValue(dsm, true); }
    }

    // ------------------------------------------------------------------ D3: the carry limit before the charge

    private static DungeonLocation Dungeon(TerminalEmulator term, Character hero, int floor)
    {
        var d = new DungeonLocation();
        Wire(d, term, hero);
        typeof(DungeonLocation).GetField("currentDungeonLevel", F)!.SetValue(d, floor);
        typeof(DungeonLocation).GetField("dungeonRandom", F)!.SetValue(d, new Random(1310));
        return d;
    }

    private static DungeonSettlement Settlement() => new DungeonSettlement
    {
        Id = "ec", Floor = 10, Name = "Ec Camp", NPCName = "Ec Trader", HasTrading = true,
        TradeItems = new[] { "Healing Potion", "Mana Potion", "Antidote", "Healing Herb", "Starbloom Essence", "Firebloom Petal" },
    };

    [Theory]
    [InlineData("1", "heal")]
    [InlineData("2", "mana")]
    [InlineData("3", "antidote")]
    [InlineData("4", "herb")]
    [InlineData("5", "starbloom")]
    [InlineData("6", "firebloom")]
    public async Task D3_ASettlementItemAtTheCarryLimit_IsRefused_AndNoGoldIsTaken(string pick, string what)
    {
        var hero = Hero("CapSet");
        hero.Healing = hero.MaxPotions;
        hero.ManaPotions = hero.MaxManaPotions;
        hero.Antidotes = hero.MaxAntidotes;
        hero.HerbHealing = GameConfig.HerbMaxCarry[(int)HerbType.HealingHerb];
        hero.HerbStarbloom = GameConfig.HerbMaxCarry[(int)HerbType.StarbloomEssence];
        hero.HerbFirebloom = GameConfig.HerbMaxCarry[(int)HerbType.FirebloomPetal];
        var (term, output) = Term(pick, "", "0");
        await Call(Dungeon(term, hero, 10), "SettlementTrade", hero, Settlement());
        hero.Gold.Should().Be(10_000, what + ": nothing was given, so nothing is taken");
        string shown = Plain(term, output);
        string line = what switch
        {
            "heal" => Loc.Get("healer.max_potions", hero.MaxPotions),
            "mana" => Loc.Get("healer.max_mana_potions"),
            "antidote" => Loc.Get("healer.max_antidotes", hero.MaxAntidotes),
            _ => Loc.Get("wilderness.herb_pouch_full"),
        };
        shown.Should().Contain(line);
        shown.Should().NotContain(Loc.Get("dungeon.settlement_purchased", "").Trim().TrimEnd('!'), "no purchase line");
    }

    [Fact]
    public async Task D3_ASettlementItemBelowTheLimit_IsStillSold()
    {
        var hero = Hero("CapSet2");
        hero.Healing = hero.MaxPotions - 1;
        var (term, output) = Term("1", "", "0");
        await Call(Dungeon(term, hero, 10), "SettlementTrade", hero, Settlement());
        hero.Healing.Should().Be(hero.MaxPotions);
        hero.Gold.Should().Be(10_000 - 25 * 2, "the floor 10 price");
    }

    private static async Task<(Character hero, string shown)> Merchant(long healing, string pick)
    {
        var hero = Hero("CapStreet");
        hero.Healing = healing;
        var (term, output) = Term(pick, "", "");
        var m = typeof(StreetEncounterSystem).GetMethod("ProcessMerchantEncounter", F)!;
        try { await (Task)m.Invoke(StreetEncounterSystem.Instance, new object[] { hero, GameLocation.MainStreet, new EncounterResult(), term })!; }
        catch (TargetInvocationException ex) when (ex.InnerException != null) { throw ex.InnerException; }
        return (hero, Plain(term, output));
    }

    [Theory]
    [InlineData("1")]
    [InlineData("2")]
    public async Task D3_TheStreetMerchant_RefusesPotionsAtTheCarryLimit_AndTakesNoGold(string pick)
    {
        var probe = Hero("CapStreet");
        var (hero, shown) = await Merchant(probe.MaxPotions, pick);
        hero.Gold.Should().Be(10_000);
        hero.Healing.Should().Be(hero.MaxPotions);
        shown.Should().Contain(Loc.Get("healer.max_potions", hero.MaxPotions));

        var (below, _) = await Merchant(probe.MaxPotions - 1, "1");
        below.Healing.Should().Be(below.MaxPotions, "below the limit the potion is sold");
        below.Gold.Should().Be(10_000 - (40 + 3 * below.Level));
    }

    // ------------------------------------------------------------------ P13: Good Deeds, the bank ban, the Reinforced Door

    [Theory]
    [InlineData(1, "visual")] [InlineData(3, "visual")]
    [InlineData(1, "screenreader")] [InlineData(3, "screenreader")]
    [InlineData(1, "bbs")] [InlineData(3, "bbs")]
    public void P13_TheClassicMainStreet_ShowsGoodDeeds(int tier, string mode)
    {
        string label = Loc.Get("main_street.good_deeds_title");
        string menu = ClassicMainStreetLayout1114Tests.RenderClassic(tier == 3 ? 10 : 1, false, mode);
        menu.Should().Contain(mode == "screenreader" ? $"  G - {label}" : $"[G]{label}");
        foreach (string row in menu.Split('\n'))
            if (row.Contains("[G]") || row.Contains("G - ")) row.Length.Should().BeLessThanOrEqualTo(79, row);
    }

    [Theory]
    [InlineData("en")] [InlineData("es")] [InlineData("fr")] [InlineData("hu")] [InlineData("it")]
    public void P13_TheClassicGoodDeedsRows_Fit79_InEveryLanguage(string lang)
    {
        string prev = GameConfig.Language;
        GameConfig.Language = lang;
        try
        {
            foreach (var mode in new[] { "visual", "bbs" })
                foreach (string row in ClassicMainStreetLayout1114Tests.RenderClassic(10, false, mode).Split('\n'))
                    if (row.Contains("[G]")) row.Length.Should().BeLessThanOrEqualTo(79, $"{lang} {mode}: {row}");
            ("  " + Loc.GetIn(lang, "main_street.deed_give_gold", 999_999.ToString("N0"))).Length.Should().BeLessThanOrEqualTo(79);
        }
        finally { GameConfig.Language = prev; }
    }

    private async Task<(Character hero, string shown)> Deeds(long gold, params string[] lines)
    {
        var hero = Hero("Deeds");
        hero.Gold = gold;
        hero.ChivNr = 3;
        var (term, output) = Term(lines);
        var street = new MainStreetLocation();
        Wire(street, term, hero);
        await Call(street, "ShowGoodDeeds");
        return (hero, Plain(term, output));
    }

    [Fact]
    public async Task P13_GivingGoldToThePoor_CostsLevelTimes20_AndShowsThePrice()
    {
        var (hero, shown) = await Deeds(1_000, "1", "");
        hero.Gold.Should().Be(1_000 - 10 * 20);
        hero.ChivNr.Should().Be(2);
        hero.Chivalry.Should().BeGreaterThan(0);
        shown.Should().Contain(Loc.Get("main_street.deed_give_gold", "200"));
    }

    [Fact]
    public async Task P13_GivingGoldToThePoor_WithoutTheGold_IsRefused_AndTheDeedIsKept()
    {
        var (hero, shown) = await Deeds(199, "1", "");
        hero.Gold.Should().Be(199);
        hero.ChivNr.Should().Be(3, "the deed is not spent");
        hero.Chivalry.Should().Be(0);
        shown.Should().Contain(Loc.Get("ui.not_enough_gold"));

        var (helper, _) = await Deeds(0, "2", "");
        helper.ChivNr.Should().Be(2, "the other deeds stay free");
        helper.Gold.Should().Be(0);
    }

    private static (BankLocation bank, TerminalEmulator term, MemoryStream output, Character hero) Bank(params string[] lines)
    {
        var hero = Hero("Banned");
        hero.Loan = 150_000;
        hero.Gold = 200_000;
        hero.BankGold = 0;
        var (term, output) = Term(lines);
        var bank = new BankLocation();
        Wire(bank, term, hero);
        return (bank, term, output, hero);
    }

    private static async Task<bool> Choose(BaseLocation loc, string key)
    {
        try { return await (Task<bool>)loc.GetType().GetMethod("ProcessChoice", F)!.Invoke(loc, new object[] { key })!; }
        catch (TargetInvocationException ex) when (ex.InnerException != null) { throw ex.InnerException; }
    }

    [Theory]
    [InlineData("D")] [InlineData("W")] [InlineData("T")] [InlineData("G")] [InlineData("O")] [InlineData("I")]
    public async Task P13_ABannedPlayer_GetsNoBankService(string key)
    {
        var (bank, term, output, hero) = Bank("100", "100", "Y", "", "", "");
        (await Choose(bank, key)).Should().BeFalse();
        hero.Gold.Should().Be(200_000, key);
        hero.BankGold.Should().Be(0, key);
        hero.BankGuard.Should().BeFalse(key);
        Plain(term, output).Should().Contain(Loc.Get("bank.banned_message"));
    }

    [Fact]
    public async Task P13_ABannedPlayer_CanRepay_WithL_AndIsThenServed()
    {
        var (bank, term, output, hero) = Bank("1", "", "", "");
        typeof(BankLocation).GetMethod("DisplayLocation", F)!.Invoke(bank, null);
        string screen = Plain(term, output);
        screen.Should().Contain($"[L] {Loc.Get("bank.repay")}").And.Contain($"[Q] {Loc.Get("bank.banned_leave")}");
        foreach (var lang in Langs) Loc.GetIn(lang, "bank.banned_leave").Should().NotContain("(", lang);

        (await Choose(bank, "L")).Should().BeFalse();
        hero.Loan.Should().Be(0);
        hero.Gold.Should().Be(50_000);
        BankLocation.IsPlayerBannedFromBank(hero).Should().BeFalse();
    }

    private static async Task<(Character hero, string shown)> Upgrades(bool online, params string[] lines)
    {
        OnlineField.SetValue(null, online);
        var hero = Hero("Door");
        hero.Gold = 3_000_000;
        var (term, output) = Term(lines);
        var home = new HomeLocation();
        Wire(home, term, hero);
        await Call(home, "ShowHomeUpgrades");
        return (hero, Plain(term, output));
    }

    [Fact]
    public async Task P13_TheReinforcedDoor_IsNotSoldOffline_AndTheNumbersAfterItStillBuyTheirUpgrade()
    {
        var (hero, shown) = await Upgrades(false, "10", "Y", "", "");
        shown.Should().NotContain(Loc.Get("home.special_door"));
        shown.Should().Contain($"[10] {Loc.Get("home.special_armory")}").And.Contain($"[11] {Loc.Get("home.special_fountain")}");
        hero.HasReinforcedDoor.Should().BeFalse();
        hero.HasLegendaryArmory.Should().BeTrue("10 offline is the Armory");

        var (online, onlineShown) = await Upgrades(true, "10", "Y", "", "");
        onlineShown.Should().Contain($"[10] {Loc.Get("home.special_door")}");
        online.HasReinforcedDoor.Should().BeTrue("online the door is sold at 10, as before");
        online.HasLegendaryArmory.Should().BeFalse();
    }

    // ------------------------------------------------------------------ D2: one shop price

    private static Character Aligned(string name, bool evil, long gold)
    {
        var h = Hero(name);
        if (evil) { h.Darkness = 1000; h.Chivalry = 0; } else { h.Chivalry = 1000; h.Darkness = 0; }
        h.Gold = gold;
        return h;
    }

    private static float Mod(Character h) => AlignmentSystem.Instance.GetPriceModifier(h, isShadyShop: false);
    private static long Taxed(long pre) => CityControlSystem.CalculateTaxedPrice(pre).Item3;

    /// <summary>The direct buy's price, figured here from the shop's own modifiers (only alignment is not 1 in these tests).</summary>
    private static long DirectPrice(Character h, Equipment e) => (long)(e.Value * Mod(h));

    private static void NeutralWorld(Character h)
    {
        WorldEventSystem.Instance.GlobalPriceModifier.Should().Be(1.0f, "the test sets only alignment");
        FactionSystem.Instance.GetShopPriceModifier().Should().Be(1.0f);
        DifficultySystem.GetShopPriceMultiplier().Should().Be(1.0f);
        CityControlSystem.Instance.ApplyDiscount(1000, h).Should().Be(1000);
    }

    private static Equipment BestWeapon(Character h) =>
        EquipmentDatabase.GetShopWeapons(WeaponHandedness.OneHanded).Concat(EquipmentDatabase.GetShopWeapons(WeaponHandedness.TwoHanded))
            .Where(w => w.WeaponPower > 0).Where(w => w.CanEquip(h, out _))
            .Where(w => !w.RequiresGood || h.Chivalry > h.Darkness).Where(w => !w.RequiresEvil || h.Darkness > h.Chivalry)
            .OrderByDescending(w => w.WeaponPower).ThenBy(w => w.Value).First();

    private static async Task<Character> AutoBuyWeapon(Character h)
    {
        var (term, _) = Term("Y", "M", "", "");
        var shop = new WeaponShopLocation();
        Wire(shop, term, h);
        await Call(shop, "AutoBuyBestWeapon");
        return h;
    }

    [Fact]
    public async Task D2_WeaponAutoBuy_ChargesTheDirectBuyPrice_WithTheAlignmentMarkup()
    {
        var h = Aligned("AbEvil", evil: true, 0);
        Mod(h).Should().Be(1.4f);
        NeutralWorld(h);
        var t = BestWeapon(h);
        h.Gold = Taxed(DirectPrice(h, t));
        await AutoBuyWeapon(h);
        h.GetEquipment(EquipmentSlot.MainHand)?.Name.Should().Be(t.Name);
        h.Gold.Should().Be(0, "auto-buy charges what buying it directly charges, the 40 percent Evil markup included");
    }

    [Fact]
    public async Task D2_WeaponAutoBuy_OffersWhatTheDiscountedPriceAffords()
    {
        var h = Aligned("AbHoly", evil: false, 0);
        Mod(h).Should().BeLessThan(1.0f);
        NeutralWorld(h);
        var t = BestWeapon(h);
        h.Gold = Taxed(DirectPrice(h, t));
        t.Value.Should().BeGreaterThan(h.Gold, "the raw value is out of reach, the Holy price is not");
        await AutoBuyWeapon(h);
        h.GetEquipment(EquipmentSlot.MainHand)?.Name.Should().Be(t.Name);
        h.Gold.Should().Be(0);
    }

    [Fact]
    public void D2_ArmorAutoBuyPlan_PricesEveryPieceAsTheDirectBuy()
    {
        var h = Aligned("AbArmor", evil: true, 2_000_000);
        NeutralWorld(h);
        var shop = new ArmorShopLocation();
        var (term, _) = Term();
        Wire(shop, term, h);
        var plan = shop.PlanAutoBuy();
        plan.Should().NotBeEmpty();
        foreach (var p in plan)
        {
            p.Price.Should().Be(DirectPrice(h, p.Armor), p.Armor.Name);
            p.Total.Should().Be(Taxed(p.Price));
        }
    }

    private static EquipmentSlot FirstArmorSlot() =>
        ((EquipmentSlot[])typeof(ArmorShopLocation).GetField("ArmorSlots", SF)!.GetValue(null)!)[0];

    private static Equipment BestArmor(Character h, EquipmentSlot slot) =>
        EquipmentDatabase.GetShopArmor(slot).Where(i => i.ArmorClass > 0).Where(i => i.CanEquip(h, out _))
            .Where(i => !i.RequiresGood || h.Chivalry > h.Darkness).Where(i => !i.RequiresEvil || h.Darkness > h.Chivalry)
            .OrderByDescending(i => i.ArmorClass).ThenBy(i => i.Value).First();

    [Fact]
    public async Task D2_ArmorAutoBuy_SlotBySlot_OffersAndChargesTheDiscountedPrice()
    {
        var h = Aligned("AbSlot", evil: false, 0);
        NeutralWorld(h);
        var slot = FirstArmorSlot();
        var t = BestArmor(h, slot);
        h.Gold = Taxed(DirectPrice(h, t));
        t.Value.Should().BeGreaterThan(h.Gold);
        var (term, _) = Term("P", "Y", "C", "", "");
        var shop = new ArmorShopLocation();
        Wire(shop, term, h);
        await Call(shop, "AutoBuyBestArmor");
        h.GetEquipment(slot)?.Name.Should().Be(t.Name, "the slot by slot path offers what the Holy price affords");
        h.Gold.Should().Be(0);
    }

    [Fact]
    public void D2_TheArmorList_DrawsAnItemAffordable_AtItsDiscountedPrice()
    {
        var h = Aligned("AbList", evil: false, 0);
        NeutralWorld(h);
        var slot = FirstArmorSlot();
        var shop = new ArmorShopLocation();
        var items = (List<Equipment>)typeof(ArmorShopLocation).GetMethod("GetShopArmorForSlot", F)!.Invoke(shop, new object[] { slot })!;
        var t = items.Take(5).Where(i => i.MinLevel <= h.Level && ArmorShopLocation.MeetsClass(h.Class, i) && i.Value >= 100).First();
        h.Gold = Taxed(DirectPrice(h, t));
        t.Value.Should().BeGreaterThan(h.Gold);
        var output = new MemoryStream();
        var term = new TerminalEmulator(new LineStream(Enumerable.Repeat("", 5)), output);
        Wire(shop, term, h);
        typeof(ArmorShopLocation).GetMethod("ShowSlotItems", F)!.Invoke(shop, new object[] { slot });
        term.StreamWriterInternal?.Flush();
        string raw = Encoding.UTF8.GetString(output.ToArray());
        raw.Should().Contain("\u001b[37m" + ItemNames.Column(t, 26), "drawn as one the player can buy, not greyed out");
    }

    [Fact]
    public void D2_TheWeaponList_DrawsAnItemAffordable_AtItsDiscountedPrice()
    {
        var h = Aligned("WbList", evil: false, 0);
        NeutralWorld(h);
        var shop = new WeaponShopLocation();
        var cat = Enum.Parse(typeof(WeaponShopLocation).GetNestedType("WeaponCategory", BindingFlags.NonPublic)!, "OneHanded");
        var items = (List<Equipment>)typeof(WeaponShopLocation).GetMethod("GetShopItemsForCategory", F)!.Invoke(shop, new[] { cat })!;
        var t = items.Take(5).Where(i => i.MinLevel <= h.Level && i.Value >= 100 && i.CanEquip(h, out _)).First();
        h.Gold = Taxed(DirectPrice(h, t));
        t.Value.Should().BeGreaterThan(h.Gold);
        var output = new MemoryStream();
        var term = new TerminalEmulator(new LineStream(Enumerable.Repeat("", 5)), output);
        Wire(shop, term, h);
        typeof(WeaponShopLocation).GetMethod("ShowCategoryItems", F)!.Invoke(shop, new[] { cat });
        term.StreamWriterInternal?.Flush();
        string raw = Encoding.UTF8.GetString(output.ToArray());
        raw.Should().Contain("\u001b[37m" + ItemNames.Column(t, 26), "drawn as one the player can buy, not greyed out");
    }

    // ------------------------------------------------------------------ D4: the offline Auction House

    private static (MarketplaceLocation market, TerminalEmulator term, MemoryStream output) Market(Character hero, params string[] lines)
    {
        var (term, output) = Term(lines);
        var market = new MarketplaceLocation();
        Wire(market, term, hero);
        return (market, term, output);
    }

    private static MarketplaceSystem.MarketListing ListByPlayer(Character hero, string itemName, int daysAgo)
    {
        var item = new Item { Name = itemName, Value = 50 };
        MarketplaceSystem.Instance.ListItem(hero.DisplayName, item, 75);
        var listing = MarketplaceSystem.Instance.Listings.Last();
        listing.Posted = DateTime.Now.AddDays(-daysAgo);
        return listing;
    }

    [Fact]
    public async Task D4_APlayer_CanWithdrawTheirListing_AndTheItemComesBack()
    {
        MarketplaceSystem.Instance.ClearAllListings();
        try
        {
            var hero = Hero("Seller");
            ListByPlayer(hero, "Withdrawn Ring", 2);
            var other = new NPC { Name1 = "Vendor", Name2 = "Vendor", Level = 5 };
            MarketplaceSystem.Instance.NPCListItem(other, new Item { Name = "Vendor Ring", Value = 10 });
            var (market, term, output) = Market(hero, "1", "", "");
            (await Choose(market, "W")).Should().BeFalse();
            hero.Inventory.Should().ContainSingle(i => i.Name == "Withdrawn Ring");
            MarketplaceSystem.Instance.Listings.Should().ContainSingle(l => l.IsNPCSeller, "only the player's own listing is withdrawn");
            Plain(term, output).Should().Contain(Loc.Get("marketplace.withdrawn", ItemNames.Display("Withdrawn Ring")));
        }
        finally { MarketplaceSystem.Instance.ClearAllListings(); }
    }

    [Fact]
    public async Task D4_AnExpiredPlayerListing_ComesBackToTheSeller_NotDestroyed()
    {
        MarketplaceSystem.Instance.ClearAllListings();
        try
        {
            var hero = Hero("Expired");
            ListByPlayer(hero, "Unsold Ring", 31);
            ListByPlayer(hero, "Fresh Ring", 3);
            var (market, term, output) = Market(hero, "", "", "");
            (await Choose(market, "C")).Should().BeFalse();
            hero.Inventory.Should().ContainSingle(i => i.Name == "Unsold Ring");
            MarketplaceSystem.Instance.Listings.Should().ContainSingle(l => l.Item.Name == "Fresh Ring");
            Plain(term, output).Should().Contain(Loc.Get("marketplace.expired_returned", ItemNames.Display("Unsold Ring")));
        }
        finally { MarketplaceSystem.Instance.ClearAllListings(); }
    }

    [Fact]
    public async Task D4_WithAFullPack_TheExpiredListingStays_UntilThereIsRoom()
    {
        MarketplaceSystem.Instance.ClearAllListings();
        try
        {
            var hero = Hero("FullPack");
            for (int i = 0; i < GameConfig.MaxInventoryItems; i++) hero.Inventory.Add(new Item { Name = "Stone", Value = 1 });
            ListByPlayer(hero, "Held Ring", 40);
            var (market, term, output) = Market(hero, "", "", "");
            await Choose(market, "C");
            MarketplaceSystem.Instance.Listings.Should().ContainSingle(l => l.Item.Name == "Held Ring", "kept, not destroyed");
            Plain(term, output).Should().Contain(Loc.Get("marketplace.expired_pack_full"));

            hero.Inventory.RemoveAt(0);
            var (market2, _, _) = Market(hero, "", "", "");
            await Choose(market2, "C");
            hero.Inventory.Should().Contain(i => i.Name == "Held Ring");
            MarketplaceSystem.Instance.Listings.Should().BeEmpty();
        }
        finally { MarketplaceSystem.Instance.ClearAllListings(); }
    }

    [Fact]
    public void D4_AnExpiredNpcListing_GoesBackToTheNpcsStock()
    {
        MarketplaceSystem.Instance.ClearAllListings();
        try
        {
            var npc = new NPC { Name1 = "Stocker", Name2 = "Stocker", Level = 5, HP = 10, MaxHP = 10 };
            NPCSpawnSystem.Instance.ActiveNPCs.Add(npc);
            _addedNpcs.Add(npc);
            var item = new Item { Name = "Stock Ring", Value = 10 };
            MarketplaceSystem.Instance.NPCListItem(npc, item);
            MarketplaceSystem.Instance.Listings.Single().Posted = DateTime.Now.AddDays(-31);
            MarketplaceSystem.Instance.CleanupExpiredListings();
            MarketplaceSystem.Instance.Listings.Should().BeEmpty();
            npc.MarketInventory.Should().Contain(item);
        }
        finally { MarketplaceSystem.Instance.ClearAllListings(); }
    }

    [Theory]
    [InlineData("visual")] [InlineData("bbs")] [InlineData("screenreader")]
    public void D4_EveryAuctionMenu_ShowsTheWithdrawKey(string mode)
    {
        var hero = Hero("MenuW");
        var (market, term, output) = Market(hero);
        string method = mode switch { "bbs" => "DisplayLocationBBS", "screenreader" => "DisplayLocationSR", _ => "DisplayLocation" };
        bool sr = GameConfig.ScreenReaderMode;
        try
        {
            typeof(MarketplaceLocation).GetMethod(method, F)!.Invoke(market, null);
        }
        finally { GameConfig.ScreenReaderMode = sr; }
        string shown = Plain(term, output);
        if (mode == "screenreader") shown.Should().Contain(Loc.Get("marketplace.withdraw"));
        else if (mode == "bbs") shown.Should().Contain("W").And.Contain(Loc.Get("marketplace.bbs_withdraw"));
        else shown.Should().Contain("[W]" + Loc.Get("marketplace.menu_withdraw"));
        foreach (var lang in new[] { "en", "hu" })
        {
            string row = " [S]" + Loc.GetIn(lang, "marketplace.menu_status") + "[W]" + Loc.GetIn(lang, "marketplace.menu_withdraw") + "[R]" + Loc.GetIn(lang, "marketplace.menu_return");
            row.Length.Should().BeLessThanOrEqualTo(79, lang);
            foreach (var k in new[] { "marketplace.expired_returned", "marketplace.withdrawn" })
                Loc.GetIn(lang, k, new string('W', 30)).Length.Should().BeLessThanOrEqualTo(79, k);
            foreach (var k in new[] { "marketplace.expired_pack_full", "marketplace.withdraw_pack_full", "marketplace.withdraw_prompt" })
                Loc.GetIn(lang, k).Length.Should().BeLessThanOrEqualTo(79, k);
        }
    }
}
