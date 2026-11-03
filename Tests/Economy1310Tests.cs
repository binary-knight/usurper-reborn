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
}
