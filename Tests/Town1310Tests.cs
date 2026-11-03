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
using UsurperRemake.Systems;
using Xunit;
using P = MainStreetLocation.StreetPlace;
using G = MainStreetLocation.StreetGroup;

namespace UsurperReborn.Tests;

/// <summary>
/// 1.3.10 play test, town items: dungeon event results wait for Enter (B7), text slips (B16), the shop tip (B8), the
/// reach rule (B11), the splash (B13), the two-hander comparison (B15), town text (B17), the district return and the
/// one-place district (N3), the auto-buy plan (N4), the start-over entry (N6), inventory key labels (N7), encounter
/// typos (G1), Spanish place names (L1) and unique room names (A4).
/// </summary>
[Collection("SharedGameSingletons")]
public class Town1310Tests
{
    private const BindingFlags F = BindingFlags.NonPublic | BindingFlags.Instance;
    private const BindingFlags SF = BindingFlags.NonPublic | BindingFlags.Static;
    internal static readonly string[] Langs = { "en", "es", "fr", "hu", "it" };

    internal static string Plain(TerminalEmulator term, MemoryStream output)
    {
        term.StreamWriterInternal?.Flush();
        return Regex.Replace(Encoding.UTF8.GetString(output.ToArray()), "\u001b\\[[0-9;?]*[A-Za-z]", "").Replace("\r", "");
    }

    private static async Task Run(object o, string method, params object[] args)
    {
        try { await (Task)o.GetType().GetMethod(method, F)!.Invoke(o, args)!; }
        catch (TargetInvocationException ex) when (ex.InnerException != null) { throw ex.InnerException; }
    }

    private static T Lang<T>(string lang, Func<T> f)
    {
        string prev = GameConfig.Language;
        GameConfig.Language = lang;
        try { return f(); }
        finally { GameConfig.Language = prev; }
    }

    internal static Character Hero(int level = 8) => new Character
    {
        Name1 = "tw", Name2 = "Tw", Class = CharacterClass.Warrior, Level = level, HP = 300, MaxHP = 300, Gold = 1000,
        AI = CharacterAI.Human, Dexterity = 60, Wisdom = 60, Healing = 2,
    };

    private static (DungeonLocation d, TerminalEmulator term, MemoryStream output, Character hero) Dungeon(params string[] lines)
    {
        var output = new MemoryStream();
        var term = new TerminalEmulator(new LineStream(lines), output);
        var hero = Hero();
        var d = new DungeonLocation();
        typeof(BaseLocation).GetField("terminal", F)!.SetValue(d, term);
        typeof(BaseLocation).GetField("currentPlayer", F)!.SetValue(d, hero);
        typeof(DungeonLocation).GetField("currentDungeonLevel", F)!.SetValue(d, 5);
        typeof(DungeonLocation).GetField("dungeonRandom", F)!.SetValue(d, new Random(1310));
        return (d, term, output, hero);
    }

    // ------------------------------------------------------------------ B7: event results wait for Enter

    /// <summary>The events a room's [V] runs (RunRoomEvent and RandomDungeonEvent) that ended on a timed pause.</summary>
    [Theory]
    [InlineData("WoundedManEncounter", "H")]
    [InlineData("PotionCacheEncounter", null)]
    [InlineData("TreasureChestEncounter", "L")]
    [InlineData("MysteriousShrine", "L")]
    [InlineData("GamblingGhostEncounter", "100")]
    public async Task ARoomEvent_EndsOnItsResult_AndAnEnterPrompt_BeforeTheRoomRedraws(string method, string? answer)
    {
        var (d, term, output, _) = Dungeon(answer == null ? new[] { "" } : new[] { answer, "" });
        Pacing.Disabled = true;
        await Run(d, method);
        string shown = Plain(term, output).TrimEnd();
        shown.Should().EndWith(Loc.Get("ui.press_enter").TrimEnd(), "{0} waits for Enter after its result", method);
    }

    [Fact]
    public async Task TheWoundedStranger_ShowsTheGoldAndTheChivalry_ThenWaits_AndItsIntroOnce()
    {
        var (d, term, output, hero) = Dungeon("H", "");
        long gold = hero.Gold;
        await Run(d, "WoundedManEncounter");
        string shown = Plain(term, output);
        hero.Gold.Should().BeGreaterThan(gold);
        int reward = shown.IndexOf(Loc.Get("dungeon.wounded_reward", hero.Gold - gold), StringComparison.Ordinal);
        reward.Should().BeGreaterThan(0);
        shown.IndexOf(Loc.Get("ui.press_enter").TrimEnd(), reward, StringComparison.Ordinal).Should().BeGreaterThan(reward);
        Regex.Matches(shown, Regex.Escape(Loc.Get("dungeon.wounded_find"))).Count.Should().Be(1, "the intro line is printed once (B16)");
        foreach (var lang in Langs)
            Loc.GetIn(lang, "dungeon.wounded_begs").Should().NotBe(Loc.GetIn(lang, "dungeon.wounded_find"), lang);
    }

    // ------------------------------------------------------------------ B16: Healer rows

    [Theory]
    [InlineData("en")] [InlineData("es")] [InlineData("fr")] [InlineData("hu")] [InlineData("it")]
    public void TheHealerMenu_NeverRunsALabelIntoTheNextKey_AndFits79(string lang)
    {
        var healer = new HealerLocation();
        var output = new MemoryStream();
        var term = new TerminalEmulator(new LineStream(Array.Empty<string>()), output);
        typeof(BaseLocation).GetField("terminal", F)!.SetValue(healer, term);
        typeof(BaseLocation).GetField("currentPlayer", F)!.SetValue(healer, Hero());
        string text = Lang(lang, () =>
        {
            typeof(HealerLocation).GetMethod("ShowMenu", F)!.Invoke(healer, null);
            return Plain(term, output);
        });
        var rows = text.Split('\n').Where(r => Regex.IsMatch(r, @"\[[A-Z]\]")).ToList();
        string keys = string.Concat(rows.SelectMany(r => Regex.Matches(r, @"\[([A-Z])\]").Select(m => m.Groups[1].Value)));
        keys.Should().Contain("H").And.Contain("D").And.Contain("N").And.Contain("R");
        rows.Should().NotContain(r => Regex.IsMatch(r, @"\S\[[A-Z]\]"), "a label touching the next key reads as one word ({0})", lang);
        rows.Should().OnlyContain(r => r.TrimEnd().Length <= 79, lang);
    }

    // ------------------------------------------------------------------ B8: the shop tip

    [Fact]
    public void TheFirstShopTip_NamesOnlyKeysBothShopsTake()
    {
        foreach (var lang in Langs)
        {
            var quoted = Regex.Matches(Loc.GetIn(lang, "hint.first_shop.msg"), @"'([A-Z])'").Select(m => m.Groups[1].Value).ToList();
            quoted.Should().NotBeEmpty(lang);
            quoted.Should().OnlyContain(k => k == "S" || k == "A", "the shops take a number, S and A; B is not a shop key ({0})", lang);
        }
    }

    // ------------------------------------------------------------------ B11: the reach rule

    [Fact]
    public void TheReachTable_IsTheRuleTheGameUses()
    {
        var allowance = typeof(DungeonLocation).GetMethod("GetFloorReachAllowance", SF)!;
        int Allow(int level) => (int)allowance.Invoke(null, new object[] { level })!;
        Allow(1).Should().Be(3); Allow(5).Should().Be(3); Allow(6).Should().Be(5); Allow(15).Should().Be(5);
        Allow(16).Should().Be(8); Allow(30).Should().Be(8); Allow(31).Should().Be(10); Allow(100).Should().Be(10);
    }

    [Fact]
    public async Task TheTutorial_AndTheLevelChange_StateTheSameReachRule()
    {
        var output = new MemoryStream();
        var term = new TerminalEmulator(new LineStream(new[] { "Y", "", "", "", "", "", "", "", "", "" }), output);
        var hero = Hero(4);
        var loc = new DungeonLocation();
        await (Task<bool>)typeof(DungeonLocation).GetMethod("RunDungeonTutorial", F)!.Invoke(loc, new object[] { hero, term })!;
        string tutorial = Plain(term, output);
        tutorial.Should().NotContain("10 levels", "the tutorial said floors within 10 levels of yours for every level");
        var rule = DungeonLocation.FloorReachRuleLines(73);
        foreach (var line in rule) tutorial.Should().Contain(line);

        var (d, t2, o2, h2) = Dungeon("");
        h2.Level = 4;
        typeof(DungeonLocation).GetField("currentDungeonLevel", F)!.SetValue(d, 2);
        try { await Run(d, "ChangeDungeonLevel"); } catch (IOException) { }
        string change = Plain(t2, o2);
        change.Should().Contain(Loc.Get("dungeon.deepest_accessible", 7, 3));
        foreach (var line in DungeonLocation.FloorReachRuleLines(77)) change.Should().Contain(line);
        foreach (var lang in Langs)
            Loc.GetIn(lang, "dungeon.reach_rule", 3, 5, 5, 15, 8, 30, 10).Should().Contain("3").And.Contain("15").And.Contain("30", lang);
        foreach (var lang in new[] { "en", "hu" })
            Lang(lang, () => DungeonLocation.FloorReachRuleLines(73)).Should().OnlyContain(l => l.Length + 4 <= 79, lang);
    }

    // ------------------------------------------------------------------ B13: the splash

    [Fact]
    public async Task TheSplash_SaysEnter_WhereItReadsALine_AndWhatWasTypedThereNeverAnswersTheNextPrompt()
    {
        var output = new MemoryStream();
        var term = new TerminalEmulator(new LineStream(new[] { "x", "1" }), output);
        bool sr = GameConfig.ScreenReaderMode;
        try
        {
            GameConfig.ScreenReaderMode = true;   // the plain title, no art
            await UsurperRemake.UI.SplashScreen.Show(term);
        }
        finally { GameConfig.ScreenReaderMode = sr; }
        term.PauseNeedsEnter.Should().BeTrue("a stream session reads a line at a pause");
        string shown = Plain(term, output);
        shown.Should().Contain(Loc.Get("splash.press_enter")).And.NotContain(Loc.Get("splash.press_any_key"));
        (await term.GetInput("> ")).Should().Be("1", "the x typed at the splash is not the account menu's answer");
        foreach (var lang in Langs) Loc.GetIn(lang, "splash.press_enter").Should().NotBe(Loc.GetIn(lang, "splash.press_any_key"), lang);
    }

    // ------------------------------------------------------------------ B15: the two-hander comparison

    [Fact]
    public void ATwoHander_IsComparedAgainstBothHands_AndNamesTheOffHandItTakesOff()
    {
        var hero = Hero();
        var main = new Equipment { Name = "Short Sword", Slot = EquipmentSlot.MainHand, WeaponPower = 8, Handedness = WeaponHandedness.OneHanded };
        var off = new Equipment { Name = "Dagger", Slot = EquipmentSlot.OffHand, WeaponPower = 16, Handedness = WeaponHandedness.OneHanded };
        EquipmentDatabase.RegisterDynamic(main);
        EquipmentDatabase.RegisterDynamic(off);
        hero.EquippedItems[EquipmentSlot.MainHand] = main.Id;
        hero.EquippedItems[EquipmentSlot.OffHand] = off.Id;
        var (power, removed) = InventorySystem.WeaponComparison(hero, main, newIsTwoHanded: true);
        power.Should().Be(24, "a 12 WP two-hander against 8 + 16 is a downgrade, not +4");
        removed.Should().BeSameAs(off);
        InventorySystem.WeaponComparison(hero, main, newIsTwoHanded: false).Should().Be((8, (Equipment?)null));
        foreach (var lang in Langs) Loc.GetIn(lang, "inventory.offhand_comes_off", "Dagger", 16).Should().Contain("Dagger").And.Contain("16");
    }

    // ------------------------------------------------------------------ B17: town text

    [Fact]
    public void GuildRow_NamesOnlyWhatIsOpen_UntilTheTempleAndBankOpen()
    {
        var guild = MainStreetLocation.Districts.Single(d => d.Group == G.GuildRow);
        MainStreetLocation.DistrictDescKey(guild, new MainStreetLocation.StreetView(1, false, false)).Should().Be(guild.DescKey + ".early");
        MainStreetLocation.DistrictDescKey(guild, new MainStreetLocation.StreetView(3, false, false)).Should().Be(guild.DescKey);
        Loc.GetIn("en", guild.DescKey + ".early").Should().NotContainAny("priest", "banker");
        foreach (var lang in Langs) Loc.HasIn(lang, guild.DescKey + ".early").Should().BeTrue(lang);
        var merchant = MainStreetLocation.Districts.Single(d => d.Group == G.MerchantRow);
        MainStreetLocation.DistrictDescKey(merchant, new MainStreetLocation.StreetView(1, false, false)).Should().Be(merchant.DescKey, "no early text, the plain one");
    }

    [Fact]
    public void MainStreet_NamesTheTownTheOpeningNames()
    {
        var street = new MainStreetLocation();
        string town = (string)typeof(MainStreetLocation).GetMethod("GetTownName", F)!.Invoke(street, null)!;
        foreach (var lang in Langs) Loc.GetIn(lang, "opening_story.goal_dorashire").Should().Contain(town, lang);
    }

    [Fact]
    public void TheNextStep_ForAClassicPlayer_GivesTheClassicKeys()
    {
        // fixed by 1.3.9 (#211, 458e84d4); kept here as the regression for B17's second slip
        var classic = Hero();
        classic.DistrictsMainStreet = false;
        string text = new JournalSystem.JournalNextStep { LocKey = "journal.next_level_up" }.Text(classic);
        text.Should().NotContain("[G]").And.Contain("[V]");
    }

    // ------------------------------------------------------------------ L1: one Spanish pair

    [Fact]
    public void Spanish_UsesOneNameForTheQuestHall_AndOneForMainStreet()
    {
        string es = File.ReadAllText(Path.Combine(MainStreetDistricts1113Tests.RepoRoot(), "Localization/es.json"));
        es.Should().NotContain("Sala de Misiones").And.NotContain("Calle Mayor");
        Loc.GetIn("es", "location.quest_hall").Should().Be("Salón de Misiones");
        Loc.GetIn("es", "location.main_street").Should().Be("Calle Principal");
        Loc.GetIn("es", "castle.visit_quest_hall.classic").Should().Contain("Salón de Misiones").And.Contain("Calle Principal");
        Loc.GetIn("es", "castle.visit_quest_hall").Should().Contain("Salón de Misiones").And.Contain("Calle Principal");
    }

    // ------------------------------------------------------------------ N3: back to the district

    private static (MainStreetLocation Street, TerminalEmulator Term, MemoryStream Output) Street(Character hero, params string[] lines)
    {
        var street = new MainStreetLocation();
        var output = new MemoryStream();
        var term = new TerminalEmulator(new LineStream(lines), output);
        typeof(BaseLocation).GetField("terminal", F)!.SetValue(street, term);
        typeof(BaseLocation).GetField("currentPlayer", F)!.SetValue(street, hero);
        return (street, term, output);
    }

    private static Character DistrictHero(int level)
    {
        var h = Hero(level);
        h.DistrictsMainStreet = true;
        return h;
    }

    [Fact]
    public async Task AShopEnteredFromADistrict_LeadsBackToThatDistrict()
    {
        Pacing.Disabled = true;
        var hero = DistrictHero(8);
        var (street, term, output) = Street(hero, "W", "R");
        var merchant = MainStreetLocation.Districts.Single(d => d.Group == G.MerchantRow);
        var go = async () => await (Task<bool>)typeof(MainStreetLocation).GetMethod("EnterDistrict", F)!.Invoke(street, new object[] { merchant })!;
        (await go.Should().ThrowAsync<LocationExitException>()).Which.DestinationLocation.Should().Be(GameLocation.WeaponShop);

        term.StreamWriterInternal?.Flush();
        int before = Plain(term, output).Length;
        (await street.ShowReturnDistrict(GameLocation.WeaponShop)).Should().BeFalse("R leaves the district to Main Street");
        string back = Plain(term, output).Substring(before);
        back.Should().Contain(Loc.Get(merchant.NameKey), "R in the Weapon Shop comes back to Merchant Row");
        street.TakeReturnDistrict(GameLocation.WeaponShop).Should().BeNull("it is taken once");
    }

    [Fact]
    public async Task TheReturn_IsOnlyStraightBack_AndOnlyOnTheDistrictsLayout()
    {
        Pacing.Disabled = true;
        var merchant = MainStreetLocation.Districts.Single(d => d.Group == G.MerchantRow);
        async Task<MainStreetLocation> VisitedWeaponShop(Character hero)
        {
            var (street, _, _) = Street(hero, "W");
            try { await (Task<bool>)typeof(MainStreetLocation).GetMethod("EnterDistrict", F)!.Invoke(street, new object[] { merchant })!; }
            catch (LocationExitException) { }
            return street;
        }
        (await VisitedWeaponShop(DistrictHero(8))).TakeReturnDistrict(GameLocation.Dungeons).Should().BeNull("came back from somewhere else");
        var classic = DistrictHero(8);
        var s2 = await VisitedWeaponShop(classic);
        classic.DistrictsMainStreet = false;
        s2.TakeReturnDistrict(GameLocation.WeaponShop).Should().BeNull("the classic layout has no districts");
        (await VisitedWeaponShop(DistrictHero(8))).TakeReturnDistrict(GameLocation.WeaponShop).Should().Be(merchant);
    }

    [Theory]
    [InlineData(3)] [InlineData(4)]
    public async Task ADistrictWithOneOpenPlace_OpensThatPlace_WithoutAOneChoiceScreen(int level)
    {
        Pacing.Disabled = true;
        var castle = MainStreetLocation.Districts.Single(d => d.Group == G.CastleGrounds);
        var view = new MainStreetLocation.StreetView(MainStreetLocation.TierForLevel(level), false, false);
        MainStreetLocation.PlacesIn(G.CastleGrounds, view).Should().ContainSingle();
        var (street, term, output) = Street(DistrictHero(level), "R");
        var go = async () => await (Task<bool>)typeof(MainStreetLocation).GetMethod("EnterDistrict", F)!.Invoke(street, new object[] { castle })!;
        (await go.Should().ThrowAsync<LocationExitException>()).Which.DestinationLocation.Should().Be(GameLocation.Castle);
        Plain(term, output).Should().NotContain(Loc.Get(castle.DescKey));
    }

    // ------------------------------------------------------------------ N4: auto-buy asks once

    [Fact]
    public async Task AutoBuy_ShowsEveryPieceAndTheTotal_ThenAsksOnce()
    {
        Pacing.Disabled = true;
        var hero = Hero(10);
        hero.Gold = 4000;
        var shop = new ArmorShopLocation();
        var output = new MemoryStream();
        var term = new TerminalEmulator(new LineStream(new[] { "Y", "" }), output);
        typeof(BaseLocation).GetField("terminal", F)!.SetValue(shop, term);
        typeof(BaseLocation).GetField("currentPlayer", F)!.SetValue(shop, hero);
        var plan = shop.PlanAutoBuy();
        plan.Count.Should().BeGreaterThan(1, "the test needs more than one slot to buy");
        long total = plan.Sum(p => p.Total);
        total.Should().BeLessThanOrEqualTo(4000);

        try { await Run(shop, "AutoBuyBestArmor"); } catch (IOException) { }
        string shown = Plain(term, output);
        int totalLine = shown.IndexOf(Loc.Get("armor_shop.autobuy_plan_total", total.ToString("N0"), plan.Count, (4000 - total).ToString("N0"), "4,000"), StringComparison.Ordinal);
        totalLine.Should().BeGreaterThan(0, "the total is shown");
        int firstBuy = shown.IndexOf(Loc.Get("armor_shop.autobuy_purchased", ItemNames.Display(plan[0].Armor)), StringComparison.Ordinal);
        firstBuy.Should().BeGreaterThan(totalLine, "nothing is bought before the total is shown");
        Regex.Matches(shown, Regex.Escape(Loc.Get("ui.your_choice"))).Count.Should().Be(1, "one question for the whole list");
        hero.Gold.Should().Be(4000 - total);
        foreach (var p in plan) hero.GetEquipment(p.Slot)!.Name.Should().Be(p.Armor.Name);
        foreach (var lang in new[] { "en", "hu" })
            foreach (var p in plan)
                UsurperRemake.UI.UIHelper.MessageRows(Loc.GetIn(lang, "armor_shop.autobuy_plan_row", p.Slot, p.Armor.Name, 99, 99, "99,999")).Should().OnlyContain(r => r.Length <= 79);
    }

    [Fact]
    public async Task AutoBuy_PKeepsThePerSlotPath_AndCCancelsWithNothingBought()
    {
        Pacing.Disabled = true;
        var hero = Hero(10);
        hero.Gold = 4000;
        var shop = new ArmorShopLocation();
        var output = new MemoryStream();
        var term = new TerminalEmulator(new LineStream(new[] { "C", "" }), output);
        typeof(BaseLocation).GetField("terminal", F)!.SetValue(shop, term);
        typeof(BaseLocation).GetField("currentPlayer", F)!.SetValue(shop, hero);
        await Run(shop, "AutoBuyBestArmor");
        hero.Gold.Should().Be(4000);
        Plain(term, output).Should().Contain(Loc.Get("armor_shop.autobuy_cancelled"));

        var output2 = new MemoryStream();
        var term2 = new TerminalEmulator(new LineStream(new[] { "P", "C", "" }), output2);
        typeof(BaseLocation).GetField("terminal", F)!.SetValue(shop, term2);
        await Run(shop, "AutoBuyBestArmor");
        Plain(term2, output2).Should().Contain(Loc.Get("armor_shop.autobuy_opt_buy").Trim(), "P asks slot by slot as before");
        hero.Gold.Should().Be(4000);
    }

    // ------------------------------------------------------------------ N6: start over, apart

    private static string Source(string rel) => File.ReadAllText(Path.Combine(MainStreetDistricts1113Tests.RepoRoot(), rel));

    [Fact]
    public void StartingOver_IsNotBesideThePlayKey_ItHasItsOwnRedSection_AndStillAsksForDelete()
    {
        string src = Source("Scripts/Core/GameEngine.cs");
        int body = src.IndexOf("private async Task RunBBSDoorMode()", StringComparison.Ordinal);
        int play = src.IndexOf("\"engine.section_play_visual\"", body, StringComparison.Ordinal);
        int info = src.IndexOf("\"engine.section_info_visual\"", play, StringComparison.Ordinal);
        src.Substring(play, info - play).Should().NotContain("engine.menu_new_overwrite", "the PLAY section has only keys that play");
        int apart = src.IndexOf("\"engine.section_start_over_visual\"", info, StringComparison.Ordinal);
        apart.Should().BeGreaterThan(info);
        src.Substring(apart, 400).Should().Contain("WriteDangerKey(\"N\", Loc.Get(\"engine.menu_new_overwrite_full\", mainSave.PlayerName))");
        int compactQ = src.IndexOf("WriteMenuKey(\"Q\", Loc.Get(\"engine.menu_quit\"));", body, StringComparison.Ordinal);
        src.Substring(compactQ - 300, 300).Should().Contain("WriteDangerKey(\"N\", Loc.Get(\"engine.menu_new_overwrite\", mainSave.PlayerName))");
        int n = src.IndexOf("Loc.Get(\"engine.delete_main_warning\")", body, StringComparison.Ordinal);
        src.Substring(n, 1200).Should().Contain("confirm == \"DELETE\"", "N still asks for DELETE first");
        foreach (var lang in Langs)
        {
            Loc.GetIn(lang, "engine.menu_new_overwrite_full", "Pyra").Should().Contain("Pyra", lang);
            Loc.GetIn(lang, "engine.menu_new_overwrite", "Pyra").Should().Contain("Pyra", lang);
            (Loc.GetIn(lang, "engine.section_start_over_visual").Length + 2 + Loc.GetIn(lang, "engine.menu_new_overwrite_full", "Abcdefghijklmnop").Length).Should().BeLessThan(160);
        }
        foreach (var lang in new[] { "en", "hu" })
            ("  [N] " + Loc.GetIn(lang, "engine.menu_new_overwrite_full", new string('W', 20))).Length.Should().BeLessThanOrEqualTo(79, lang);
    }

    // ------------------------------------------------------------------ N7: inventory key labels

    /// <summary>The web client's reading of a key and its label (web/play/index.html scanCommands).</summary>
    internal static List<(string Key, string Label)> WebKeys(string row)
    {
        var list = new List<(string, string)>();
        foreach (Match m in Regex.Matches(row, @"\[([^\[\]\s]{1,5})\]([a-z]*)([^\[\]║│]*)"))
        {
            string rest = Regex.Split(m.Groups[3].Value.TrimStart(), @"\s{2,}")[0];
            rest = Regex.Replace(rest, @"[:>]\s*$", "").Trim();
            string label = m.Groups[2].Value.Length > 0 ? m.Groups[1].Value + m.Groups[2].Value + (rest.Length > 0 ? " " + rest : "") : rest;
            if (!Regex.IsMatch(label, @"[A-Za-z0-9À-ɏ]")) continue;
            list.Add((m.Groups[1].Value, label));
        }
        return list;
    }

    [Fact]
    public void TheInventoryOptions_NameRealKeys_WithBothPageKeys_AndNoPlaceholder()
    {
        var accepted = new HashSet<string> { "D", "U", "Q", "<", ">" };
        foreach (var lang in Langs)
        {
            foreach (var key in new[] { "inventory.options_line1", "inventory.options_line2", "inventory.slot.equip", "inventory.slot.options_line2" })
            {
                string row = Loc.GetIn(lang, key);
                row.Should().NotContain("B#", "{0} {1}", lang, key);
                row.Length.Should().BeLessThanOrEqualTo(key.StartsWith("inventory.slot.") ? 77 : 79, "{0} {1} (the slot rows are indented 2)", lang, key);
                foreach (Match m in Regex.Matches(row, @"\[([^\[\]\s]{1,5})\]"))
                    accepted.Should().Contain(m.Groups[1].Value, "{0} {1} names a key the inventory takes", lang, key);
            }
            var paging = WebKeys(Loc.GetIn(lang, "inventory.options_line2")).Select(k => k.Key).ToList();
            paging.Should().Contain("<").And.Contain(">", lang);
        }
    }

    [Fact]
    public void ASlotRow_ReadsAsTheSlotAndItsItem_AfterTheKey()
    {
        foreach (var lang in Langs)
        {
            string head = Lang(lang, () => InventorySystem.SlotRowHead("1", Loc.Get("inventory.slot_main_hand")));
            var keys = WebKeys(head + "Short Sword (WP:8)");
            keys.Should().ContainSingle();
            keys[0].Label.Should().Be(Loc.GetIn(lang, "inventory.slot_main_hand") + ": Short Sword (WP:8)", lang);
            string head2 = Lang(lang, () => InventorySystem.SlotRowHead("N", Loc.Get("inventory.slot_neck")));
            head2.IndexOf(':').Should().Be(head.IndexOf(':'), "the colons line up ({0})", lang);
        }
    }

    // ------------------------------------------------------------------ G1: encounter typos

    [Fact]
    public async Task AnInvalidKey_AtAHostileEncounter_AsksAgain_InsteadOfStartingTheFight()
    {
        Pacing.Disabled = true;
        var output = new MemoryStream();
        var term = new TerminalEmulator(new LineStream(new[] { "Z" }), output);
        var hero = Hero();
        var result = new EncounterResult();
        try
        {
            await (Task)typeof(StreetEncounterSystem).GetMethod("ProcessHostileNPCEncounter", F)!
                .Invoke(StreetEncounterSystem.Instance, new object[] { hero, GameLocation.MainStreet, result, term })!;
        }
        catch (Exception) { /* the input ends at the second ask */ }
        Plain(term, output).Should().Contain(Loc.Get("ui.invalid_choice_choose", "F, R, B, T"), "Z is not an option; it is asked again");
    }

    [Fact]
    public async Task TheEncounterKeyHelper_TakesTheFirstValidKey_AndFallsBackAfterThree()
    {
        var t1 = new TerminalEmulator(new LineStream(new[] { "z", "t" }), new MemoryStream());
        (await StreetEncounterSystem.ReadEncounterKey(t1, new[] { "F", "T" }, "F")).Should().Be("T");
        var t2 = new TerminalEmulator(new LineStream(new[] { "z", "y", "x", "t" }), new MemoryStream());
        (await StreetEncounterSystem.ReadEncounterKey(t2, new[] { "F", "T" }, "F")).Should().Be("F", "three invalid keys give the old answer");
    }

    // ------------------------------------------------------------------ A4: unique room names

    [Fact]
    public void EveryFloor_HasUniqueRoomNames_AndTheSameLayoutEachTime()
    {
        for (int level = 1; level <= 100; level++)
        {
            var floor = DungeonGenerator.GenerateFloor(level);
            var names = floor.Rooms.Select(r => r.Name).ToList();
            names.Should().OnlyHaveUniqueItems("floor {0} names each room once", level);
            var again = DungeonGenerator.GenerateFloor(level);
            again.Rooms.Select(r => (r.Id, r.Name, r.Type, r.HasMonsters, r.HasEvent, string.Join(",", r.Exits.Keys)))
                .Should().Equal(floor.Rooms.Select(r => (r.Id, r.Name, r.Type, r.HasMonsters, r.HasEvent, string.Join(",", r.Exits.Keys))), "floor {0}", level);
        }
        DungeonGenerator.RoomNumeral(2).Should().Be("II");
        DungeonGenerator.RoomNumeral(4).Should().Be("IV");
    }

    [Fact]
    public void TheNumbering_KeepsTheFirstName_AndNumbersTheRepeats_InRoomOrder()
    {
        var floor = new DungeonFloor();
        foreach (var (id, name) in new[] { ("room_0", "Hall"), ("room_1", "Crypt"), ("room_2", "Hall"), ("room_3", "Hall") })
            floor.Rooms.Add(new DungeonRoom { Id = id, Name = name });
        DungeonGenerator.NumberRepeatedRoomNames(floor);
        floor.Rooms.Select(r => r.Name).Should().Equal("Hall", "Crypt", Loc.Get("dg.room_name_repeat", "Hall", "II"), Loc.Get("dg.room_name_repeat", "Hall", "III"));
    }
}

/// <summary>1.3.10 (play test G4): a new life (NG+, the renounce restart) keeps the old character's preferences from its first save.</summary>
[Collection("SharedGameSingletons")]
public class NewLifePrefs1310Tests : IDisposable
{
    private const BindingFlags NP = BindingFlags.NonPublic | BindingFlags.Instance;
    private const BindingFlags SNP = BindingFlags.NonPublic | BindingFlags.Static;
    private readonly string _path = Path.Combine(Path.GetTempPath(), $"usurper-newlife1310-{Guid.NewGuid():N}.db");
    private readonly SqlSaveBackend _db;
    private readonly object? _oldSaveSystem;
    private readonly string _oldLang = GameConfig.Language;
    private readonly bool _oldSr = GameConfig.ScreenReaderMode;
    private readonly bool _oldKeys = GameConfig.MenuKeysNeedEnter;
    private static FieldInfo SaveSystemField => typeof(SaveSystem).GetField("instance", SNP)!;

    public NewLifePrefs1310Tests()
    {
        _db = new SqlSaveBackend(_path);
        _oldSaveSystem = SaveSystemField.GetValue(null);
        GameConfig.Language = "en";
        GameConfig.ScreenReaderMode = false;
        GameConfig.MenuKeysNeedEnter = true;
    }

    public void Dispose()
    {
        SaveSystemField.SetValue(null, _oldSaveSystem);
        GameConfig.Language = _oldLang;
        GameConfig.ScreenReaderMode = _oldSr;
        GameConfig.MenuKeysNeedEnter = _oldKeys;
        UsurperRemake.Server.SessionContext.Current = null;
        Microsoft.Data.Sqlite.SqliteConnection.ClearAllPools();
        try { File.Delete(_path); } catch { }
    }

    [Fact]
    public async Task ANewLife_IsSavedWithTheOldCharactersScreenReaderMenuKeysAndOrientation()
    {
        SaveSystem.InitializeWithBackend(_db);
        const string key = "nlprefs";
        var ctx = new UsurperRemake.Server.SessionContext { Username = key, CharacterKey = key };
        ctx.InitializeSystems();
        var engine = new GameEngine();
        ctx.Engine = engine;
        UsurperRemake.Server.SessionContext.Current = ctx;
        var old = new Character
        {
            Name1 = key, Name2 = "Nlprefs", Level = 30, HP = 300, MaxHP = 300, AI = CharacterAI.Human,
            ScreenReaderMode = true, MenuKeysNeedEnter = false, WebMenuKeysNeedEnter = true, Orientation = SexualOrientation.Bisexual,
        };
        engine.CurrentPlayer = old;
        ctx.Player = old;
        (await SaveSystem.Instance.SaveGame(key, old)).Should().BeTrue();

        var output = new MemoryStream();
        typeof(GameEngine).GetField("terminal", NP)!.SetValue(engine, new TerminalEmulator(new LineStream(new[] { "Q", "1", "1", "" }), output));
        try { await engine.BeginNewLifeAsync(key); }
        catch (Exception) { /* the scripted input ends during the opening story, after the first save */ }

        engine.CurrentPlayer.Should().NotBeSameAs(old, "a new character was created");
        var saved = (await _db.ReadGameData(key))!.Player;
        saved.Level.Should().Be(1, "the save is the new life");
        saved.ScreenReaderMode.Should().BeTrue();
        saved.MenuKeysNeedEnter.Should().BeFalse();
        saved.WebMenuKeysNeedEnter.Should().BeTrue();
        ((SexualOrientation)saved.Orientation).Should().Be(SexualOrientation.Bisexual);
        engine.CurrentPlayer!.ScreenReaderMode.Should().BeTrue("the new life plays with them too, not only after it ends");
    }
}
