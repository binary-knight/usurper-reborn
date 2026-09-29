using System;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using FluentAssertions;
using UsurperRemake.Systems;
using Xunit;

namespace UsurperReborn.Tests;

/// <summary>
/// 1.2.0 Temple gods piece 3, commit 1: devotion at the Temple gives Favor. Daily prayer +3 once a
/// day; gold sacrifice +1 per (Level x 100) gold up to +5 a day; item sacrifice +1 to +4 by value up
/// to +4 a day, and the item is really given up. Canon gods and player-gods alike. Every test
/// uses its own GodSystem, so the shared singleton is never touched.
/// </summary>
[Collection("SharedGameSingletons")]
public class GodsDevotion1115Tests
{
    private static Character Hero(string name, int level = 5) =>
        new Character { Name1 = name, Name2 = name, AI = CharacterAI.Human, Level = level, HP = 50, MaxHP = 50 };

    private static (Character c, GodSystem gods) Worshipper(string name, string god, int favor = 10, int level = 5)
    {
        var gods = new GodSystem();
        var c = Hero(name, level);
        GodRegistry.SetWorshippedGod(c, god, gods).Should().BeTrue();
        c.GodFavor = favor;
        FavorSystem.Bind(c, gods);
        c.GodFavor = favor;
        return (c, gods);
    }

    private static string RepoRoot()
    {
        var dir = AppContext.BaseDirectory;
        while (dir != null && !File.Exists(Path.Combine(dir, "UsurperReborn.sln"))) dir = Path.GetDirectoryName(dir);
        return dir!;
    }

    private static string Source(string file) => File.ReadAllText(Path.Combine(RepoRoot(), file));

    private static string Body(string file, string signature)
    {
        string src = Source(file);
        int start = src.IndexOf(signature, StringComparison.Ordinal);
        start.Should().BeGreaterThanOrEqualTo(0, $"{signature} must exist in {file}");
        var next = new Regex(@"\n    (private|public|internal|protected) ").Match(src, start + signature.Length);
        return next.Success ? src.Substring(start, next.Index - start) : src.Substring(start);
    }

    private const string Temple = "Scripts/Locations/TempleLocation.cs";

    // ---------------- Prayer ----------------

    [Theory]
    [InlineData("Solarius")]
    [InlineData("Zephyrine")]   // a player-god: one god system
    public void Prayer_GivesThreeOnceADay_ThenAgainAfterTheReset(string god)
    {
        var (c, gods) = Worshipper("GdPray" + god, god);
        FavorSystem.Prayer(c, gods).Should().Be(GameConfig.GodFavorPrayerGain);
        c.GodFavor.Should().Be(10 + GameConfig.GodFavorPrayerGain);
        FavorSystem.Prayer(c, gods).Should().Be(0, "one prayer a day");
        c.GodFavor.Should().Be(13);
        FavorSystem.ApplyDailyReset(c, gods);
        FavorSystem.Prayer(c, gods).Should().Be(3);
    }

    [Fact]
    public void Prayer_NoGodOrNpc_GivesNothing()
    {
        var gods = new GodSystem();
        FavorSystem.Prayer(Hero("GdNoGod"), gods).Should().Be(0);
        var npc = new Character { Name1 = "GdNpc", Name2 = "GdNpc", AI = CharacterAI.Computer, WorshippedGod = "Zephyrine", GodFavorGod = "Zephyrine", GodFavor = 5 };
        FavorSystem.Prayer(npc, gods).Should().Be(0);
        npc.GodFavor.Should().Be(5);
    }

    // ---------------- Gold ----------------

    [Theory]
    [InlineData(0, 5, 0)]
    [InlineData(499, 5, 0)]
    [InlineData(500, 5, 1)]
    [InlineData(2499, 5, 4)]
    [InlineData(10000, 5, 20)]
    [InlineData(100, 0, 1)]     // level 0 counts as 1
    [InlineData(-50, 5, 0)]
    public void GoldSacrificeFavor_OnePerLevelTimesHundred(long gold, int level, int expected) =>
        FavorSystem.GoldSacrificeFavor(gold, level).Should().Be(expected);

    [Fact]
    public void GoldSacrifice_CappedAtFiveADay_AcrossSacrifices()
    {
        var (c, gods) = Worshipper("GdGold", "Terran");
        FavorSystem.GoldSacrifice(c, 1000, gods).Should().Be(2);
        FavorSystem.GoldSacrifice(c, 100000, gods).Should().Be(3, "the day's cap is 5 in all");
        FavorSystem.GoldSacrifice(c, 100000, gods).Should().Be(0);
        c.GodFavor.Should().Be(15);
        FavorSystem.ApplyDailyReset(c, gods);
        FavorSystem.GoldSacrifice(c, 100000, gods).Should().Be(GameConfig.GodFavorGoldDailyCap);
    }

    [Fact]
    public void GoldSacrifice_IsDevotion_ResetsNeglect()
    {
        var (c, gods) = Worshipper("GdGoldDev", "Zephyrine");
        c.DaysSinceDevotion = 6;
        FavorSystem.GoldSacrifice(c, 500, gods);
        c.DaysSinceDevotion.Should().Be(0);
    }

    // ---------------- Items ----------------

    [Theory]
    [InlineData(0, 5, 0)]
    [InlineData(10, 5, 1)]        // worth little: still +1
    [InlineData(1000, 5, 1)]      // resale 500 = one gold unit
    [InlineData(3000, 5, 3)]      // resale 1500
    [InlineData(4000, 5, 4)]
    [InlineData(1000000, 5, 4)]   // at most 4
    public void ItemSacrificeFavor_OneToFourByValue(long value, int level, int expected) =>
        FavorSystem.ItemSacrificeFavor(value, level).Should().Be(expected);

    [Fact]
    public void ItemSacrifice_CappedAtFourADay()
    {
        var (c, gods) = Worshipper("GdItemCap", "Arcanus");
        FavorSystem.ItemSacrifice(c, 3000, gods).Should().Be(3);
        FavorSystem.ItemSacrifice(c, 3000, gods).Should().Be(1);
        FavorSystem.ItemSacrifice(c, 3000, gods).Should().Be(0);
        FavorSystem.GainedToday(c, FavorSource.ItemSacrifice).Should().Be(GameConfig.GodFavorItemDailyCap);
    }

    private static Equipment Blade(string name, int power, long value, bool cursed = false, bool unique = false)
    {
        var e = new Equipment
        {
            Name = name, Slot = EquipmentSlot.MainHand, WeaponPower = power, Value = value, MinLevel = 1,
            Handedness = WeaponHandedness.OneHanded, WeaponType = WeaponType.Sword, IsCursed = cursed, IsUnique = unique
        };
        EquipmentDatabase.RegisterDynamic(e);
        return e;
    }

    private static void Wear(Character c, Equipment e, EquipmentSlot slot)
    {
        c.EquippedItems[slot] = e.Id;
        c.RecalculateStats();
    }

    [Fact]
    public void SacrificeEquipped_TheItemIsGone_TheStatsDrop_AndFavorComes()
    {
        var (c, gods) = Worshipper("GdBlade", "Valorian");
        c.DaysSinceDevotion = 5;
        long bare = c.WeapPow;
        var blade = Blade("Test Offering Blade", 40, 3000);
        Wear(c, blade, EquipmentSlot.MainHand);
        c.WeapPow.Should().Be(bare + 40);

        var (outcome, item, favor) = FavorSystem.SacrificeEquipped(c, EquipmentSlot.MainHand, gods);

        outcome.Should().Be(ItemSacrificeOutcome.Done);
        item.Should().BeSameAs(blade);
        favor.Should().Be(3);
        c.GodFavor.Should().Be(13);
        c.DaysSinceDevotion.Should().Be(0, "an offering is devotion");
        c.EquippedItems.ContainsKey(EquipmentSlot.MainHand).Should().BeFalse("the item is given up");
        c.GetEquipment(EquipmentSlot.MainHand).Should().BeNull();
        c.WeapPow.Should().Be(bare);
        c.RecalculateStats();
        c.WeapPow.Should().Be(bare, "a recalculation does not bring it back");
    }

    [Fact]
    public void SacrificeEquipped_EmptySlot_NothingHappens()
    {
        var (c, gods) = Worshipper("GdEmpty", "Valorian");
        var (outcome, item, favor) = FavorSystem.SacrificeEquipped(c, EquipmentSlot.Body, gods);
        outcome.Should().Be(ItemSacrificeOutcome.NoItem);
        item.Should().BeNull();
        favor.Should().Be(0);
        c.GodFavor.Should().Be(10);
    }

    [Theory]
    [InlineData(true, false)]
    [InlineData(false, true)]
    public void SacrificeEquipped_CursedOrUnique_IsRefused_AndStaysWorn(bool cursed, bool unique)
    {
        var (c, gods) = Worshipper("GdRefuse" + cursed, "Solarius");
        var blade = Blade("Test Refused Blade", 30, 3000, cursed, unique);
        Wear(c, blade, EquipmentSlot.MainHand);
        long worn = c.WeapPow;

        var (outcome, _, favor) = FavorSystem.SacrificeEquipped(c, EquipmentSlot.MainHand, gods);

        outcome.Should().Be(ItemSacrificeOutcome.Refused);
        favor.Should().Be(0);
        c.GodFavor.Should().Be(10);
        c.EquippedItems[EquipmentSlot.MainHand].Should().Be(blade.Id);
        c.WeapPow.Should().Be(worn);
    }

    // ---------------- Temple wiring ----------------

    [Fact]
    public void Prayer_TempleCallsThePrayerGain_AndReportsIt_ForBothKindsOfGod()
    {
        string body = Body(Temple, "private async Task ProcessDailyPrayer(");
        body.Should().Contain("int prayerFavor = FavorSystem.Prayer(currentPlayer, godSystem);");
        Regex.Matches(body, @"FavorUi\.ReportGain\(terminal, currentPlayer, prayerFavor, godSystem\)").Count.Should().Be(2);
    }

    [Fact]
    public void GoldSacrifice_CanonPath_OnlyForYourOwnGod()
    {
        string body = Body(Temple, "private async Task ProcessGoldSacrifice(");
        body.Should().Contain("if (!wrongGod && playerGod == god.Name)\n            FavorUi.ReportGain(terminal, currentPlayer, FavorSystem.GoldSacrifice(currentPlayer, goldAmount, godSystem), godSystem);");
    }

    [Fact]
    public void GoldSacrifice_PlayerGodPath_GivesFavor()
    {
        Body(Temple, "private async Task SacrificeToImmortalGod(")
            .Should().Contain("FavorUi.ReportGain(terminal, currentPlayer, FavorSystem.GoldSacrifice(currentPlayer, amount, godSystem), godSystem);");
    }

    [Fact]
    public void ItemSacrifice_TempleUsesTheRealSacrifice_ForAnyGod()
    {
        string menu = Body(Temple, "private async Task ProcessItemSacrifice(");
        menu.Should().Contain("GodRegistry.GetWorshippedGod(currentPlayer, godSystem)");
        menu.Should().Contain("SacrificeEquippedItem(currentGod, isCanon, EquipmentSlot.MainHand)");
        menu.Should().Contain("SacrificeEquippedItem(currentGod, isCanon, EquipmentSlot.Body)");
        string item = Body(Temple, "private async Task SacrificeEquippedItem(");
        item.Should().Contain("FavorSystem.SacrificeEquipped(currentPlayer, slot, godSystem)");
        item.Should().Contain("FavorUi.ReportGain(terminal, currentPlayer, favor, godSystem)");
        string potions = Body(Temple, "private async Task SacrificePotions(");
        potions.Should().Contain("FavorSystem.ItemSacrifice(currentPlayer, GameConfig.GetHealingPotionCost(currentPlayer.Level) * amount, godSystem)");
        potions.Should().Contain("FavorUi.ReportGain(terminal, currentPlayer, favor, godSystem)");
    }

    [Fact]
    public void ItemSacrifice_TheStatNoOpsAreGone()
    {
        string src = Source(Temple);
        src.Should().NotContain("currentPlayer.WeapPow = 0");
        src.Should().NotContain("currentPlayer.ArmPow = 0");
        src.Should().NotContain("currentPlayer.Strength += blessingBonus");
        src.Should().NotContain("currentPlayer.Defence += blessingBonus");
    }

    // ---------------- The gain line ----------------

    [Fact]
    public void ReportGain_PrintsTheGodAndTheGain_OnlyWhenAboveZero()
    {
        var (c, gods) = Worshipper("GdLine", "Amara", 20);
        var output = new MemoryStream();
        var term = new TerminalEmulator(new LineStream(Enumerable.Repeat("", 5)), output);
        FavorUi.ReportGain(term, c, 0, gods);
        term.StreamWriterInternal!.Flush();
        output.Length.Should().Be(0);
        FavorUi.ReportGain(term, c, 3, gods);
        term.StreamWriterInternal!.Flush();
        string text = Encoding.UTF8.GetString(output.ToArray());
        text.Should().Contain("Amara").And.Contain("+3").And.Contain("20");
    }

    [Theory]
    [InlineData("favor.gain", 3)]
    [InlineData("temple.sacrifice_refused_item", 2)]
    public void Loc_KeysInAllFiveLanguages_WithTheirPlaceholders(string key, int placeholders)
    {
        foreach (var lang in new[] { "en", "es", "fr", "it", "hu" })
        {
            using var doc = JsonDocument.Parse(File.ReadAllText(Path.Combine(RepoRoot(), "Localization", lang + ".json")));
            doc.RootElement.TryGetProperty(key, out var v).Should().BeTrue($"{lang} has {key}");
            string s = v.GetString()!;
            for (int i = 0; i < placeholders; i++) s.Should().Contain("{" + i + "}", $"{lang} {key}");
        }
    }
}
