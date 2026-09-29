using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text.Json;
using FluentAssertions;
using UsurperRemake.Systems;
using Xunit;

namespace UsurperReborn.Tests;

/// <summary>
/// 1.2.0 Temple gods piece 1: one god list (canon ten plus ascended player-gods, never Manwe), one
/// worship API over the two existing stores, Favor 0..100 bound to its god, tiers, per-source daily
/// gains, neglect, and the save plumbing with the schema guard for saves from before Favor.
/// Every test uses its own GodSystem, so the shared singleton is never touched.
/// </summary>
[Collection("SharedGameSingletons")]
public class GodsCore1115Tests
{
    private const BindingFlags F = BindingFlags.NonPublic | BindingFlags.Instance;

    private static PlayerData Serialize(Character c)
    {
        var method = typeof(SaveSystem).GetMethod("SerializePlayer", F)!;
        return (PlayerData)method.Invoke(SaveSystem.Instance, new object[] { c })!;
    }

    private static Character Restore(PlayerData data) => MenuKeysNeedEnterPref1115Tests.Restore(data);

    private static Character Hero(string name) => new Character { Name1 = name, Name2 = name, AI = CharacterAI.Human, Level = 5, HP = 50, MaxHP = 50 };

    private static (Character c, GodSystem gods) Worshipper(string name, string god, int favor)
    {
        var gods = new GodSystem();
        var c = Hero(name);
        GodRegistry.SetWorshippedGod(c, god, gods).Should().BeTrue();
        c.GodFavor = favor;
        return (c, gods);
    }

    // ---------------- Tiers ----------------

    [Theory]
    [InlineData(-5, GodFavorTier.Follower)]
    [InlineData(0, GodFavorTier.Follower)]
    [InlineData(24, GodFavorTier.Follower)]
    [InlineData(25, GodFavorTier.Devout)]
    [InlineData(49, GodFavorTier.Devout)]
    [InlineData(50, GodFavorTier.Zealot)]
    [InlineData(74, GodFavorTier.Zealot)]
    [InlineData(75, GodFavorTier.Chosen)]
    [InlineData(100, GodFavorTier.Chosen)]
    [InlineData(150, GodFavorTier.Chosen)]
    public void GetTier_Boundaries(int favor, GodFavorTier expected) =>
        FavorSystem.GetTier(favor).Should().Be(expected);

    // ---------------- Change and clamp ----------------

    [Fact]
    public void Change_ClampsToZeroAndOneHundred()
    {
        var (c, gods) = Worshipper("GcClamp", "Solarius", 95);
        FavorSystem.Change(c, 20, gods).Should().Be(5);
        c.GodFavor.Should().Be(GameConfig.GodFavorMax);
        FavorSystem.Change(c, -500, gods).Should().Be(-100);
        c.GodFavor.Should().Be(GameConfig.GodFavorMin);
        FavorSystem.Change(c, int.MaxValue, gods).Should().Be(100, "an absurd delta cannot wrap");
        FavorSystem.Change(c, int.MinValue, gods).Should().Be(-100);
    }

    [Fact]
    public void Change_SkipsNpcs_AndCharactersWithNoGod()
    {
        var gods = new GodSystem();
        var npc = new Character { Name1 = "GcNpc", Name2 = "GcNpc", AI = CharacterAI.Computer, WorshippedGod = "Somegod", GodFavor = 7, GodFavorGod = "Somegod" };
        FavorSystem.Change(npc, 10, gods).Should().Be(0);
        npc.GodFavor.Should().Be(7);

        var none = Hero("GcNone");
        FavorSystem.Change(none, 10, gods).Should().Be(0);
        none.GodFavor.Should().Be(0);
    }

    // ---------------- One god list and the worship API ----------------

    [Fact]
    public void AllGods_IsTheCanonTenThenPlayerGods_AndNeverManwe()
    {
        var list = GodRegistry.AllGods(new[] { "Zephyrine", "Manwe", "manwe", "", "  ", "solarius", "Zephyrine", "Korvath" });
        list.Take(10).Select(g => g.Name).Should().Equal(GameConfig.CanonGodNames);
        list.Take(10).Should().OnlyContain(g => g.IsCanon);
        list.Skip(10).Should().Equal(new GodListEntry("Zephyrine", false), new GodListEntry("Korvath", false));
        list.Should().NotContain(g => g.Name.Equals(GameConfig.SupremeCreatorName, StringComparison.OrdinalIgnoreCase));
        GodRegistry.AllGods(null).Should().HaveCount(10);
    }

    [Fact]
    public void CanonGods_MatchTheGodSystemPantheon_AndExcludeManwe()
    {
        var pantheon = new GodSystem().GetAllGods()
            .Where(g => g.Properties.TryGetValue("IsPantheonGod", out var v) && v is bool b && b)
            .Select(g => g.Name).ToList();
        pantheon.Should().BeEquivalentTo(GameConfig.CanonGodNames);
        GameConfig.CanonGodNames.Should().NotContain(GameConfig.SupremeCreatorName);
        GodRegistry.IsCanon("Manwe").Should().BeFalse();
        GodRegistry.IsCanon("amara").Should().BeTrue();
        GodRegistry.IsCanon("Zephyrine").Should().BeFalse();
    }

    [Fact]
    public void SetWorshippedGod_Canon_ClearsThePlayerGod_AndTheReverse()
    {
        var gods = new GodSystem();
        var c = Hero("GcSwap");
        GodRegistry.SetWorshippedGod(c, "Zephyrine", gods);
        GodRegistry.GetWorshippedGod(c, gods).Should().Be(new WorshippedGod("Zephyrine", false));
        gods.GetPlayerGod("GcSwap").Should().BeEmpty();

        GodRegistry.SetWorshippedGod(c, "valorian", gods);
        GodRegistry.GetWorshippedGod(c, gods).Should().Be(new WorshippedGod("Valorian", true));
        c.WorshippedGod.Should().BeEmpty("worshipping a canon god leaves the player-god");

        GodRegistry.SetWorshippedGod(c, "Zephyrine", gods);
        gods.GetPlayerGod("GcSwap").Should().BeEmpty("worshipping a player-god leaves the canon god");
        c.WorshippedGod.Should().Be("Zephyrine");

        GodRegistry.SetWorshippedGod(c, null, gods);
        GodRegistry.GetWorshippedGod(c, gods).Should().BeNull();
    }

    [Fact]
    public void SetWorshippedGod_RefusesManwe()
    {
        var gods = new GodSystem();
        var c = Hero("GcManwe");
        GodRegistry.SetWorshippedGod(c, "Solarius", gods);
        GodRegistry.SetWorshippedGod(c, "Manwe", gods).Should().BeFalse();
        GodRegistry.GetWorshippedGod(c, gods)!.Value.Name.Should().Be("Solarius");
    }

    [Fact]
    public void Favor_BelongsToItsGod_ANewGodStartsAtZero_EvenThroughTheOldPaths()
    {
        var (c, gods) = Worshipper("GcBind", "Solarius", 40);
        FavorSystem.GetFavor(c, gods).Should().Be(40);

        // an old Temple path switches the store directly, bypassing GodRegistry
        gods.SetPlayerGod("GcBind", "Mortis");
        FavorSystem.GetFavor(c, gods).Should().Be(0, "the stored Favor belongs to Solarius");
        FavorSystem.Change(c, 3, gods).Should().Be(3);
        c.GodFavorGod.Should().Be("Mortis");
        c.GodFavor.Should().Be(3);

        GodRegistry.SetWorshippedGod(c, "Zephyrine", gods);
        FavorSystem.GetFavor(c, gods).Should().Be(0);
        c.GodFavorGod.Should().Be("Zephyrine");
    }

    // ---------------- Daily gains and neglect ----------------

    [Fact]
    public void GainCapped_StopsAtTheSourcesDailyCap_AndSourcesAreIndependent()
    {
        var (c, gods) = Worshipper("GcCap", "Arcanus", 10);
        FavorSystem.GainCapped(c, FavorSource.GoldSacrifice, 3, 5, gods).Should().Be(3);
        FavorSystem.GainCapped(c, FavorSource.GoldSacrifice, 3, 5, gods).Should().Be(2);
        FavorSystem.GainCapped(c, FavorSource.GoldSacrifice, 3, 5, gods).Should().Be(0);
        FavorSystem.GainCapped(c, FavorSource.Prayer, 3, 3, gods).Should().Be(3);
        FavorSystem.GainedToday(c, FavorSource.GoldSacrifice).Should().Be(5);
        FavorSystem.GainedToday(c, FavorSource.Prayer).Should().Be(3);
        FavorSystem.GainedToday(c, FavorSource.Deed).Should().Be(0);
        c.GodFavor.Should().Be(18);
    }

    [Fact]
    public void ApplyDailyReset_ClearsTheDailyGains()
    {
        var (c, gods) = Worshipper("GcReset", "Terran", 10);
        FavorSystem.GainCapped(c, FavorSource.Deed, 4, 4, gods);
        FavorSystem.ApplyDailyReset(c, gods);
        c.GodFavorDayGains.Should().BeEmpty();
        FavorSystem.GainCapped(c, FavorSource.Deed, 4, 4, gods).Should().Be(4, "a new day, a new cap");
    }

    [Theory]
    [InlineData(0, 0)]
    [InlineData(3, 0)]
    [InlineData(4, 1)]
    [InlineData(40, 1)]
    public void NeglectLoss_StartsAfterTheGraceDays(int days, int loss) =>
        FavorSystem.NeglectLoss(days).Should().Be(loss);

    [Fact]
    public void ApplyDailyReset_Neglect_OneADayAfterThreeIdleDays_AndDevotionStartsOver()
    {
        var (c, gods) = Worshipper("GcNeglect", "Sylvana", 20);
        c.DaysSinceDevotion = 0;
        for (int d = 0; d < GameConfig.GodNeglectGraceDays; d++)
            FavorSystem.ApplyDailyReset(c, gods).Should().Be(0);
        c.GodFavor.Should().Be(20);
        FavorSystem.ApplyDailyReset(c, gods).Should().Be(-1);
        FavorSystem.ApplyDailyReset(c, gods).Should().Be(-1);
        c.GodFavor.Should().Be(18);

        FavorSystem.MarkDevotion(c);
        FavorSystem.ApplyDailyReset(c, gods).Should().Be(0);
        c.GodFavor.Should().Be(18);
    }

    [Fact]
    public void ApplyDailyReset_SkipsNpcs_AndLeavesTheGodless()
    {
        var gods = new GodSystem();
        var npc = new Character { Name1 = "GcN", Name2 = "GcN", AI = CharacterAI.Computer, WorshippedGod = "Zephyrine", GodFavor = 5, GodFavorGod = "Zephyrine", DaysSinceDevotion = 10 };
        npc.GodFavorDayGains["Deed"] = 2;
        FavorSystem.ApplyDailyReset(npc, gods).Should().Be(0);
        npc.GodFavor.Should().Be(5);
        npc.GodFavorDayGains.Should().ContainKey("Deed");

        var none = Hero("GcGodless");
        none.DaysSinceDevotion = 10;
        FavorSystem.ApplyDailyReset(none, gods).Should().Be(0);
        none.DaysSinceDevotion.Should().Be(10, "no god, no neglect count");
    }

    // ---------------- Saves ----------------

    [Fact]
    public void AllFavorFields_RoundTripThroughSerializeAndRestore()
    {
        var c = Hero("GcRound");
        c.WorshippedGod = "Zephyrine";
        c.GodFavor = 63;
        c.GodFavorGod = "Zephyrine";
        c.GodFavorSchema = GameConfig.GodFavorSchemaCurrent;
        c.GodFavorDayGains["Prayer"] = 3;
        c.GodFavorDayGains["Deed"] = 4;
        c.DaysSinceDevotion = 2;

        var data = Serialize(c);
        data.GodFavor.Should().Be(63);
        data.GodFavorGod.Should().Be("Zephyrine");
        data.GodFavorSchema.Should().Be(GameConfig.GodFavorSchemaCurrent);
        data.GodFavorDayGains.Should().Equal(new Dictionary<string, int> { ["Prayer"] = 3, ["Deed"] = 4 });
        data.DaysSinceDevotion.Should().Be(2);

        var back = JsonSerializer.Deserialize<PlayerData>(JsonSerializer.Serialize(data))!;
        var restored = Restore(back);
        GodRegistry.ApplyLoad(restored, new GodSystem());

        restored.GodFavor.Should().Be(63);
        restored.GodFavorGod.Should().Be("Zephyrine");
        restored.GodFavorSchema.Should().Be(GameConfig.GodFavorSchemaCurrent);
        restored.GodFavorDayGains.Should().Equal(new Dictionary<string, int> { ["Prayer"] = 3, ["Deed"] = 4 });
        restored.DaysSinceDevotion.Should().Be(2);
    }

    [Fact]
    public void Serialize_WritesTheCharactersOwnSchema_SoASkippedMigrationRepeats()
    {
        var c = Hero("GcStale");
        c.GodFavorSchema = 0;
        Serialize(c).GodFavorSchema.Should().Be(0);
    }

    [Fact]
    public void AnEmptySave_ReadsAsLegacy()
    {
        var empty = JsonSerializer.Deserialize<PlayerData>("{}")!;
        empty.GodFavorSchema.Should().Be(0);
        empty.GodFavor.Should().Be(0);
        empty.GodFavorDayGains.Should().BeEmpty();
    }

    /// <summary>The load in order: the character, then the save's worship dictionary, then ApplyLoad.</summary>
    private static (Character c, GodSystem gods) LoadOld(string name, string worshippedGod, Dictionary<string, string> playerGods)
    {
        var data = new PlayerData { Name1 = name, Name2 = name, WorshippedGod = worshippedGod, Level = 5, HP = 50, MaxHP = 50 };
        var back = JsonSerializer.Deserialize<PlayerData>(JsonSerializer.Serialize(data))!;
        back.GodFavorSchema.Should().Be(0, "a save written before Favor");
        var c = Restore(back);
        var gods = new GodSystem();
        SaveSystem.RestorePlayerGods(gods, playerGods, GameEngine.GodRestoreFilterFor(c));
        GodRegistry.ApplyLoad(c, gods);
        return (c, gods);
    }

    [Fact]
    public void OldSave_WithACanonGod_StartsAtLegacyFavorWithThatGod()
    {
        var (c, gods) = LoadOld("GcOldCanon", "", new Dictionary<string, string> { ["GcOldCanon"] = "Judicar", ["Other"] = "Amara" });
        GodRegistry.GetWorshippedGod(c, gods).Should().Be(new WorshippedGod("Judicar", true));
        c.GodFavor.Should().Be(GameConfig.GodFavorLegacyStart);
        c.GodFavorGod.Should().Be("Judicar");
        c.GodFavorSchema.Should().Be(GameConfig.GodFavorSchemaCurrent);
        FavorSystem.GetFavor(c, gods).Should().Be(GameConfig.GodFavorLegacyStart);
    }

    [Fact]
    public void OldSave_WithAPlayerGod_StartsAtLegacyFavorWithThatGod()
    {
        var (c, gods) = LoadOld("GcOldPlayerGod", "Zephyrine", new Dictionary<string, string>());
        GodRegistry.GetWorshippedGod(c, gods).Should().Be(new WorshippedGod("Zephyrine", false));
        c.GodFavor.Should().Be(GameConfig.GodFavorLegacyStart);
        c.GodFavorGod.Should().Be("Zephyrine");
        c.GodFavorSchema.Should().Be(GameConfig.GodFavorSchemaCurrent);
    }

    [Fact]
    public void OldSave_WithNoGod_StartsAtZero()
    {
        var (c, gods) = LoadOld("GcOldNone", "", new Dictionary<string, string>());
        GodRegistry.GetWorshippedGod(c, gods).Should().BeNull();
        c.GodFavor.Should().Be(0);
        c.GodFavorGod.Should().BeEmpty();
        c.GodFavorSchema.Should().Be(GameConfig.GodFavorSchemaCurrent);
    }

    [Fact]
    public void OldSave_WithBothKinds_KeepsTheCanonGod_AndItsFavor()
    {
        var (c, gods) = LoadOld("GcOldDual", "Zephyrine", new Dictionary<string, string> { ["GcOldDual"] = "Umbrath" });
        c.WorshippedGod.Should().BeEmpty("mutual exclusion: the canon god wins");
        GodRegistry.GetWorshippedGod(c, gods).Should().Be(new WorshippedGod("Umbrath", true));
        c.GodFavorGod.Should().Be("Umbrath");
        c.GodFavor.Should().Be(GameConfig.GodFavorLegacyStart);
    }

    [Fact]
    public void OldSave_WorshippingManwe_IsCleared_AndStartsAtZero()
    {
        var (c, gods) = LoadOld("GcOldManwe", "", new Dictionary<string, string> { ["GcOldManwe"] = GameConfig.SupremeCreatorName });
        gods.GetPlayerGod("GcOldManwe").Should().BeEmpty();
        GodRegistry.GetWorshippedGod(c, gods).Should().BeNull();
        c.GodFavor.Should().Be(0);
    }

    [Fact]
    public void CurrentSave_IsNotMigrated_AndKeepsItsFavor()
    {
        var data = new PlayerData { Name1 = "GcCurrent", Name2 = "GcCurrent", WorshippedGod = "", Level = 5, HP = 50, MaxHP = 50,
            GodFavor = 55, GodFavorGod = "Amara", GodFavorSchema = GameConfig.GodFavorSchemaCurrent, DaysSinceDevotion = 2 };
        var c = Restore(data);
        var gods = new GodSystem();
        SaveSystem.RestorePlayerGods(gods, new Dictionary<string, string> { ["GcCurrent"] = "Amara" }, "GcCurrent");
        GodRegistry.ApplyLoad(c, gods);
        c.GodFavor.Should().Be(55);
        c.DaysSinceDevotion.Should().Be(2);
    }

    [Fact]
    public void NewCharacter_StartsAtTheCurrentFavorSchema()
    {
        var term = new TerminalEmulator(new MemoryStream(), new MemoryStream());
        var ccs = new CharacterCreationSystem(term);
        var method = typeof(CharacterCreationSystem).GetMethod("CreateBaseCharacter", F)!;
        var c = (Character)method.Invoke(ccs, new object[] { "GcNewbie" })!;
        c.GodFavorSchema.Should().Be(GameConfig.GodFavorSchemaCurrent);
        c.GodFavor.Should().Be(0);
    }

    // ---------------- Wiring (source) ----------------

    private static string Source(params string[] parts)
    {
        var dir = new DirectoryInfo(Directory.GetCurrentDirectory());
        while (dir != null && !File.Exists(Path.Combine(dir.FullName, "usurper-reloaded.csproj")))
            dir = dir.Parent;
        dir.Should().NotBeNull("the test must be able to find the repo root");
        return File.ReadAllText(Path.Combine(new[] { dir!.FullName, "Scripts" }.Concat(parts).ToArray()));
    }

    [Fact]
    public void Wiring_BothLoadPathsRunApplyLoad_TheDailyResetAndPrayerAreHooked()
    {
        var engine = Source("Core", "GameEngine.cs");
        System.Text.RegularExpressions.Regex.Matches(engine,
            @"RestoreStorySystems\(saveData\.StorySystems, \w+\);\s*\n\s*GodRegistry\.ApplyLoad\(currentPlayer\);")
            .Count.Should().Be(2, "both load paths run the Favor load step right after the worship entry is restored");

        var daily = Source("Systems", "DailySystemManager.cs");
        int basic = daily.IndexOf("private async Task RunBasicDailyReset()", StringComparison.Ordinal);
        int call = daily.IndexOf("FavorSystem.ApplyDailyReset(player);", StringComparison.Ordinal);
        basic.Should().BeGreaterThan(0);
        call.Should().BeGreaterThan(daily.IndexOf("MentalSystem.ApplyDailyReset(player);", StringComparison.Ordinal),
            "the Favor reset runs in RunBasicDailyReset after the Mental reset");
        daily.IndexOf("FavorSystem.ApplyDailyReset", call + 1, StringComparison.Ordinal).Should().Be(-1, "one production caller");

        Source("Locations", "TempleLocation.cs").Should().Contain("FavorSystem.MarkDevotion(currentPlayer);");
    }

    [Fact]
    public void Editor_HasEveryFavorField()
    {
        var src = Source("Editor", "PlayerSaveEditor.cs");
        src.Should().Contain("p.GodFavorSchema = EditorIO.PromptInt(");
        src.Should().Contain("p.GodFavor = EditorIO.PromptInt($\"Favor with the worshipped god ({GameConfig.GodFavorMin}-{GameConfig.GodFavorMax})\", p.GodFavor, min: GameConfig.GodFavorMin, max: GameConfig.GodFavorMax);");
        src.Should().Contain("p.GodFavorGod = EditorIO.PromptString(");
        src.Should().Contain("p.DaysSinceDevotion = EditorIO.PromptInt(");
        src.Should().Contain("p.GodFavorDayGains = new Dictionary<string, int>();");
    }
}
