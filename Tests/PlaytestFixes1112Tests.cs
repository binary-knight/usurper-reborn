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
using UsurperRemake.Systems;
using Xunit;

namespace UsurperReborn.Tests;

/// <summary>
/// v1.1.12 playtest fixes: the world event bonus line, cursed item hints and tags, one rest per
/// floor, the lives hint, the ST bar for casters, the training and auto-combat texts, the world
/// events key on Main Street, and the gear comparison for dungeon finds.
/// </summary>
[Collection("SharedGameSingletons")]
public class PlaytestFixes1112Tests
{
    private const BindingFlags F = BindingFlags.NonPublic | BindingFlags.Instance;

    private static string RepoRoot()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir != null && !(Directory.Exists(Path.Combine(dir.FullName, "Scripts")) && Directory.Exists(Path.Combine(dir.FullName, "Localization"))))
            dir = dir.Parent;
        dir.Should().NotBeNull("the test must run inside the repository");
        return dir!.FullName;
    }

    private static string Source(string relative) => File.ReadAllText(Path.Combine(RepoRoot(), relative));

    /// <summary>The body of the first method named <paramref name="method"/>, by brace matching.</summary>
    private static string MethodBody(string relative, string method)
    {
        string src = Source(relative);
        var m = Regex.Match(src, @"(?:private|internal|public|protected)[^\n;=]*\b" + Regex.Escape(method) + @"\s*\(");
        m.Success.Should().BeTrue($"{method} must exist in {relative}");
        int open = src.IndexOf('{', m.Index);
        int depth = 0;
        for (int i = open; i < src.Length; i++)
        {
            if (src[i] == '{') depth++;
            else if (src[i] == '}' && --depth == 0) return src.Substring(open, i - open + 1);
        }
        throw new InvalidOperationException("unbalanced braces");
    }

    private static (TerminalEmulator term, MemoryStream output) Terminal(string input = "")
    {
        var output = new MemoryStream();
        return (new TerminalEmulator(new MemoryStream(Encoding.UTF8.GetBytes(input)), output), output);
    }

    private static string Shown(TerminalEmulator term, MemoryStream output)
    {
        term.StreamWriterInternal?.Flush();
        return Encoding.UTF8.GetString(output.ToArray());
    }

    private static Character Hero(CharacterClass cls = CharacterClass.Warrior) => new()
    {
        Name1 = "fix", Name2 = "Fix", Class = cls, Level = 8, HP = 500, MaxHP = 500,
        AI = CharacterAI.Human
    };

    // ---------- 1. world event bonus line ----------

    [Fact]
    public void WorldEventShare_IsZero_AndNothingPrints_WithNoEventRunning()
    {
        WorldEventSystem.Instance.ClearAllEvents();
        // a level 8 kill: the early-game 2x and difficulty used to count as "world event"
        long baseXp = 1000;
        long boosted = (long)(baseXp * GameConfig.GetEarlyGameXPMultiplier(8));
        boosted.Should().BeGreaterThan(baseXp, "the early-game multiplier is what the old check picked up");
        long xp = WorldEventSystem.Instance.GetWorldEventXPBonus(baseXp);
        long gold = WorldEventSystem.Instance.GetWorldEventGoldBonus(500);
        (xp, gold).Should().Be((0L, 0L));

        var (term, output) = Terminal();
        CombatEngine.ShowWorldEventBonus(term, xp, gold);
        Shown(term, output).Should().BeEmpty();
    }

    [Fact]
    public void WithAnEventRunning_TheLineShowsTheEventsShareOnly()
    {
        WorldEventSystem.Instance.ClearAllEvents();
        try
        {
            WorldEventSystem.Instance.ForceEvent(WorldEventSystem.EventType.KingWarDeclaration, 1);
            long expected = (long)(1000 * WorldEventSystem.Instance.GlobalXPModifier) - 1000;
            expected.Should().BeGreaterThan(0);
            long xp = WorldEventSystem.Instance.GetWorldEventXPBonus(1000);
            xp.Should().Be(expected);

            var (term, output) = Terminal();
            CombatEngine.ShowWorldEventBonus(term, xp, 0);
            string shown = Shown(term, output);
            shown.Should().Contain(Loc.Get("combat.world_event_xp", expected.ToString()));
            shown.Should().NotContain(Loc.Get("combat.world_event_gold", "0"));
        }
        finally { WorldEventSystem.Instance.ClearAllEvents(); }
    }

    [Fact]
    public void EveryRewardPath_PrintsTheWorldEventLine_OnlyThroughTheHelper()
    {
        string src = Source("Scripts/Systems/CombatEngine.cs");
        Regex.Matches(src, "\"combat.world_event_xp\"").Count.Should().Be(1, "only ShowWorldEventBonus prints it");
        Regex.Matches(src, "\"combat.world_event_gold\"").Count.Should().Be(1);
        Regex.Matches(src, @"ShowWorldEventBonus\(terminal, worldEventXP, worldEventGold\)").Count.Should().Be(3);
    }

    // ---------- 2. cursed items ----------

    [Fact]
    public void TheCursedDropHint_NamesTheMagicShop()
    {
        Loc.Get("inventory.visit_healer_curse").Should().Contain("Magic Shop").And.NotContain("Healer");
        // the Healer only scans equipped gear; the Magic Shop scans the backpack
        MethodBody("Scripts/Locations/MagicShopLocation.cs", "RemoveCurse").Should().Contain("player.Inventory.Where(i => i.IsCursed)");
        MethodBody("Scripts/Locations/HealerLocation.cs", "RemoveCursedItem").Should().NotContain("Inventory");
    }

    private static string RenderBackpack(Item item)
    {
        var hero = Hero();
        hero.Inventory.Add(item);
        var (term, output) = Terminal();
        var inv = new InventorySystem(term, hero);
        typeof(InventorySystem).GetMethod("DisplayBackpack", F)!.Invoke(inv, new object?[] { null });
        return Shown(term, output);
    }

    [Fact]
    public void TheBackpack_TagsAnIdentifiedCursedItem()
    {
        RenderBackpack(new Item { Name = "Grim Blade", Type = ObjType.Weapon, Attack = 20, Value = 100, IsCursed = true })
            .Should().Contain("Grim Blade").And.Contain(Loc.Get("shop.cursed_tag").Trim());
        RenderBackpack(new Item { Name = "Fair Blade", Type = ObjType.Weapon, Attack = 20, Value = 100 })
            .Should().NotContain(Loc.Get("shop.cursed_tag").Trim());
    }

    [Fact]
    public void TheBackpack_DoesNotRevealTheCurseOfAnUnidentifiedItem()
    {
        RenderBackpack(new Item { Name = "Grim Blade", Type = ObjType.Weapon, Attack = 20, Value = 100, IsCursed = true, IsIdentified = false })
            .Should().NotContain(Loc.Get("shop.cursed_tag").Trim());
    }

    [Fact]
    public void TheItemView_TagsACursedItem()
    {
        MethodBody("Scripts/Systems/InventorySystem.cs", "ManageBackpackItem").Should().Contain("shop.cursed_tag");
    }

    [Fact]
    public async Task EquippingACursedItem_WarnsItCannotBeRemoved()
    {
        var hero = Hero();
        hero.Inventory.Add(new Item { Name = "Grim Plate", Type = ObjType.Body, Armor = 10, Value = 100, IsCursed = true });
        var (term, output) = Terminal("y\n\n");
        var inv = new InventorySystem(term, hero);
        await (Task)typeof(InventorySystem).GetMethod("EquipFromBackpack", F)!.Invoke(inv, new object?[] { 0, null })!;
        Shown(term, output).Should().Contain(Loc.Get("inventory.cursed_equip_warning")).And.Contain(Loc.Get("inventory.equip_confirm").Trim(), "asked into an empty slot");
        hero.GetEquipment(EquipmentSlot.Body)!.IsCursed.Should().BeTrue();
        hero.UnequipSlot(EquipmentSlot.Body).Should().BeNull("a cursed item cannot be removed, as the warning says");
    }

    [Fact]
    public async Task ACursedItem_IntoAnEmptySlot_IsConfirmed_AndNoLeavesTheSlotEmpty()
    {
        var hero = Hero();
        hero.GetEquipment(EquipmentSlot.Body).Should().BeNull("the slot starts empty");
        hero.Inventory.Add(new Item { Name = "Grim Plate", Type = ObjType.Body, Armor = 10, Value = 100, IsCursed = true });
        var (term, output) = Terminal("n\n\n");
        var inv = new InventorySystem(term, hero);
        await (Task)typeof(InventorySystem).GetMethod("EquipFromBackpack", F)!.Invoke(inv, new object?[] { 0, null })!;
        string shown = Shown(term, output);
        shown.Should().Contain(Loc.Get("inventory.equip_confirm").Trim());
        shown.Should().Contain(Loc.Get("ui.cancelled"));
        hero.GetEquipment(EquipmentSlot.Body).Should().BeNull("declined");
        hero.Inventory.Should().ContainSingle(i => i.Name == "Grim Plate");
    }

    // ---------- 3. one rest per floor ----------

    private static async Task<string> SanctuaryWith(bool alreadyRested)
    {
        var hero = Hero();
        hero.HP = 100;
        var dungeon = new DungeonLocation();
        var (term, output) = Terminal("\n\n\n\n");
        typeof(BaseLocation).GetField("terminal", F)!.SetValue(dungeon, term);
        typeof(BaseLocation).GetField("currentPlayer", F)!.SetValue(dungeon, hero);
        typeof(DungeonLocation).GetField("hasCampedThisFloor", F)!.SetValue(dungeon, alreadyRested);
        await (Task)typeof(DungeonLocation).GetMethod("RestSpotEncounter", F)!.Invoke(dungeon, null)!;
        return Shown(term, output);
    }

    [Fact]
    public async Task ASanctuaryAfterCamping_SaysWhyItRefuses()
    {
        string shown = await SanctuaryWith(alreadyRested: true);
        shown.Should().Contain(Loc.Get("dungeon.sanctuary_already_rested"));
        shown.Should().Contain(Loc.Get("dungeon.rest_once_per_floor"));
    }

    [Fact]
    public void TheFirstRest_AndTheCamp_BothStateTheRule()
    {
        MethodBody("Scripts/Locations/DungeonLocation.cs", "RestInRoom").Should().Contain("dungeon.rest_once_per_floor");
        Regex.Matches(MethodBody("Scripts/Locations/DungeonLocation.cs", "RestSpotEncounter"), "dungeon.rest_once_per_floor")
            .Count.Should().Be(2, "once on the first rest, once in the refusal");
    }

    // ---------- 4. lives hint ----------

    private static void SetOnline(bool on) =>
        typeof(UsurperRemake.BBS.DoorMode).GetField("_onlineMode", BindingFlags.NonPublic | BindingFlags.Static)!.SetValue(null, on);

    [Fact]
    public void TheLivesHint_IsRegistered_AndShownOnce()
    {
        bool wasOnline = UsurperRemake.BBS.DoorMode.IsOnlineMode;
        bool permadeath = GameConfig.OnlinePermadeathEnabled;
        try
        {
            SetOnline(true);
            GameConfig.OnlinePermadeathEnabled = true;
            var hero = Hero();
            hero.Resurrections = 2;
            hero.MaxResurrections = 3;

            var (term, output) = Terminal();
            HintSystem.Instance.TryShowLivesHint(hero, term).Should().BeTrue();
            HintSystem.Instance.TryShowLivesHint(hero, term).Should().BeFalse("once per character");
            hero.HintsShown.Should().Contain(HintSystem.HINT_LIVES);
            string shown = Shown(term, output);
            shown.Should().Contain(Loc.Get("hint.lives.title"));
            shown.Should().Contain("2 of 3");
        }
        finally
        {
            SetOnline(wasOnline);
            GameConfig.OnlinePermadeathEnabled = permadeath;
        }
    }

    [Fact]
    public void TheLivesHint_IsNotShown_WhereThereAreNoLives()
    {
        bool wasOnline = UsurperRemake.BBS.DoorMode.IsOnlineMode;
        try
        {
            SetOnline(false);
            var hero = Hero();
            var (term, _) = Terminal();
            HintSystem.Instance.TryShowLivesHint(hero, term).Should().BeFalse("single-player death never reads the lives counter");
            hero.HintsShown.Should().NotContain(HintSystem.HINT_LIVES);
        }
        finally { SetOnline(wasOnline); }
    }

    [Fact]
    public void TheLivesHint_IsOfferedOnDungeonEntry()
    {
        Source("Scripts/Locations/DungeonLocation.cs").Should().Contain("HintSystem.Instance.TryShowLivesHint(player, term)");
    }

    // ---------- 5. ST bar ----------

    [Fact]
    public void TheStaminaBar_ShowsForFighters_AndCastersWithAStaminaAbility_Only()
    {
        CombatEngine.ShowsStaminaBar(Hero(CharacterClass.Warrior)).Should().BeTrue();

        var mage = Hero(CharacterClass.Magician);
        mage.Quickbar = new List<string?>(new string?[9]) { [0] = "spell:1", [1] = "spell:2" };
        mage.CurrentCombatStamina = mage.MaxCombatStamina;
        CombatEngine.ShowsStaminaBar(mage).Should().BeFalse("a caster's quickbar of spells never spends ST");
        mage.CurrentCombatStamina = mage.MaxCombatStamina - 15;
        CombatEngine.ShowsStaminaBar(mage).Should().BeTrue("Power Attack is open to every class and spends ST");
        mage.CurrentCombatStamina = mage.MaxCombatStamina;

        ClassAbilitySystem.GetAbility("power_strike")!.StaminaCost.Should().BeGreaterThan(0);
        mage.Quickbar[2] = "power_strike";
        CombatEngine.ShowsStaminaBar(mage).Should().BeTrue("a slotted stamina ability does spend ST");
    }

    [Fact]
    public void TheStatusBar_DrawsST_OnlyThroughTheCheck()
    {
        string src = Source("Scripts/Systems/CombatEngine.cs");
        int st = src.IndexOf("terminal.Write($\" {Loc.Get(\"combat.bar_st\")}:\");", StringComparison.Ordinal);
        st.Should().BeGreaterThan(0);
        src.Substring(Math.Max(0, st - 400), 400).Should().Contain("if (ShowsStaminaBar(player))");
    }

    // ---------- 6 and 7. texts ----------

    [Fact]
    public void TheTrainingPointsHint_NamesTheLevelMastersKey()
    {
        Loc.Get("base.training_points_hint").Should().Contain("[V]").And.Contain("Main Street").And.Contain("Guild Row [G]"); // v1.1.13
        // v1.1.13: [V] is the Level Master inside Guild Row
        MainStreetLocation.StreetEntries.Should().ContainSingle(e => e.Place == MainStreetLocation.StreetPlace.LevelMaster)
            .Which.Should().Match<MainStreetLocation.StreetEntry>(e => e.Key == "V" && e.Group == MainStreetLocation.StreetGroup.GuildRow);
        MainStreetLocation.DestinationOf(MainStreetLocation.StreetPlace.LevelMaster, false).Should().Be(GameLocation.Master);
    }

    [Fact]
    public void TheAutoCombatNotice_SaysWhatItDoes()
    {
        string text = Loc.Get("combat.auto_combat_on");
        text.Should().Contain("basic attack").And.Contain("healing potion").And.Contain("No spells");
    }

    // ---------- 8. world events key ----------

    [Fact]
    public void TheWorldEventsKey_IsDrawnInEveryMainStreetMenu()
    {
        // v1.1.13: World Events is [W] on the Notice Board, which every renderer draws from the one table
        const string file = "Scripts/Locations/MainStreetLocation.cs";
        MainStreetLocation.StreetEntries.Should().Contain(e => e.Place == MainStreetLocation.StreetPlace.WorldEvents
            && e.Key == "W" && e.Group == MainStreetLocation.StreetGroup.NoticeBoard);
        MethodBody(file, "DisplayLocationBBS").Should().Contain("MainStreetLines(");
        MethodBody(file, "ShowClassicMenu").Should().Contain("MainStreetLines(");
        MethodBody(file, "ShowScreenReaderMenu").Should().Contain("MainStreetLines(");
    }

    // ---------- 9. comparison for dungeon finds ----------

    private static Character HeroWithASword()
    {
        var hero = Hero();
        var sword = new Equipment { Name = "Old Sword", Slot = EquipmentSlot.MainHand, WeaponPower = 10, MinLevel = 1, Handedness = WeaponHandedness.OneHanded, WeaponType = WeaponType.Sword };
        hero.EquippedItems[EquipmentSlot.MainHand] = EquipmentDatabase.RegisterDynamic(sword);
        return hero;
    }

    [Fact]
    public void AFoundWeapon_IsComparedWithTheEquippedOne()
    {
        var hero = HeroWithASword();
        var (term, output) = Terminal();
        DiscoverySystem.ShowFoundGear(term, hero, new Item { Name = "Vault Sword", Type = ObjType.Weapon, Attack = 25, Value = 500 });
        string shown = Shown(term, output);
        shown.Should().Contain(Loc.Get("combat.currently_equipped", "Old Sword"));
        shown.Should().Contain(Loc.Get("combat.compare_attack", 10, 25));
        shown.Should().NotContain(Loc.Get("combat.loot_cursed_warning"));
    }

    [Fact]
    public void ACursedFind_CarriesTheWarning_AndAPotionShowsNothing()
    {
        var hero = HeroWithASword();
        var (term, output) = Terminal();
        DiscoverySystem.ShowFoundGear(term, hero, new Item { Name = "Grim Helm", Type = ObjType.Head, Armor = 5, Value = 50, IsCursed = true });
        Shown(term, output).Should().Contain(Loc.Get("combat.loot_cursed_warning"));

        var (term2, output2) = Terminal();
        DiscoverySystem.ShowFoundGear(term2, hero, new Item { Name = "Tonic", Type = ObjType.Potion, Value = 5 });
        DiscoverySystem.ShowFoundGear(term2, hero, new Item { Name = "Hidden Sword", Type = ObjType.Weapon, Attack = 30, IsIdentified = false });
        Shown(term2, output2).Should().BeEmpty("a potion is not gear, and an unidentified find keeps its stats hidden");
    }

    [Fact]
    public void GrantLoot_AndTheMerchant_UseTheSharedComparison()
    {
        MethodBody("Scripts/Systems/DiscoverySystem.cs", "GrantLoot").Should().Contain("ShowFoundGear(terminal, player, item)");
        var merchant = MethodBody("Scripts/Locations/DungeonLocation.cs", "PurchaseRareItem");
        merchant.Should().Contain("CombatEngine.ShowEquipmentComparison(terminal, item.LootItem, player)");
        merchant.Should().NotContain("merchant_current_weapon");
    }
}
