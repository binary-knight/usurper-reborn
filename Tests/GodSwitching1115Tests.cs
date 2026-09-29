using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text.Json;
using System.Text.RegularExpressions;
using FluentAssertions;
using UsurperRemake.Systems;
using Xunit;

namespace UsurperReborn.Tests;

/// <summary>
/// 1.2.0 Temple gods piece 4: switching gods. Leaving a god by the player's own choice costs all
/// Favor with it (the new god starts at 0) and brings its wrath by the Favor lost: a canon god's
/// Divine Wrath level, a player-god's smite. A change made by the game or another player costs the
/// Favor and brings no wrath. Every write of the worshipped god goes through GodSwitchSystem.Switch.
/// </summary>
[Collection("SharedGameSingletons")]
public class GodSwitching1115Tests
{
    private static Character Hero(string name) => new Character
    {
        Name1 = name, Name2 = name, AI = CharacterAI.Human, Level = 10, HP = 100, MaxHP = 100,
        BaseMaxHP = 100, BaseStrength = 10, BaseDexterity = 10, BaseConstitution = 10, BaseIntelligence = 20,
        BaseWisdom = 20, BaseCharisma = 10, BaseDefence = 5, BaseStamina = 10, BaseAgility = 10,
        Mental = 80, Class = CharacterClass.Warrior, Race = CharacterRace.Human
    };

    /// <summary>A hero worshipping god with the given Favor, in its own GodSystem.</summary>
    private static (Character c, GodSystem gods) Worshipper(string name, string god, int favor)
    {
        var gods = new GodSystem();
        var c = Hero(name);
        GodRegistry.SetWorshippedGod(c, god, gods).Should().BeTrue();
        c.GodFavor = favor;
        FavorSystem.Bind(c, gods);
        c.GodFavor.Should().Be(favor);
        return (c, gods);
    }

    private static string RepoRoot()
    {
        var dir = AppContext.BaseDirectory;
        while (dir != null && !File.Exists(Path.Combine(dir, "UsurperReborn.sln"))) dir = Path.GetDirectoryName(dir);
        return dir!;
    }

    private static string Source(string file) => File.ReadAllText(Path.Combine(RepoRoot(), file));

    /// <summary>The text of a method from its signature to the next member at the same indent.</summary>
    private static string Body(string file, string signature)
    {
        string src = Source(file);
        int start = src.IndexOf(signature, StringComparison.Ordinal);
        start.Should().BeGreaterThanOrEqualTo(0, $"{signature} must exist in {file}");
        var next = new Regex(@"\n    (private|public|internal|protected) ").Match(src, start + signature.Length);
        return next.Success ? src.Substring(start, next.Index - start) : src.Substring(start);
    }

    private static int Count(string text, string part) => Regex.Matches(text, Regex.Escape(part)).Count;

    // ---------------- Wrath tiers ----------------

    [Theory]
    [InlineData(0, 0)]
    [InlineData(1, 1)]
    [InlineData(24, 1)]
    [InlineData(25, 2)]
    [InlineData(49, 2)]
    [InlineData(50, 3)]
    [InlineData(100, 3)]
    public void WrathLevel_FollowsTheFavorLost(int favorLost, int level)
    {
        GodSwitchSystem.WrathLevelFor(favorLost).Should().Be(level);
    }

    [Fact]
    public void SmitePercent_IsTheSmiteRange_ScaledByFavorLost()
    {
        GodSwitchSystem.SmitePercentFor(0).Should().Be(0f);
        GodSwitchSystem.SmitePercentFor(GameConfig.GodFavorMax).Should().BeApproximately(GameConfig.GodSmiteMaxPercent, 0.0001f);
        GodSwitchSystem.SmitePercentFor(1).Should().BeGreaterThan(GameConfig.GodSmiteMinPercent);
        GodSwitchSystem.SmitePercentFor(50).Should().BeApproximately(
            GameConfig.GodSmiteMinPercent + (GameConfig.GodSmiteMaxPercent - GameConfig.GodSmiteMinPercent) * 0.5f, 0.0001f);
        GodSwitchSystem.SmiteDamageFor(0, 1000).Should().Be(0);
        GodSwitchSystem.SmiteDamageFor(1, 1).Should().Be(1, "a smite always does at least 1");
    }

    // ---------------- The cost at each kind of switch ----------------

    [Theory]
    [InlineData("Solarius", null)]          // W: leave a canon god
    [InlineData("Solarius", "Mortis")]      // W: leave, then choose (same rule in one step)
    [InlineData("Solarius", "Zephyrine")]   // J: a canon god for a player-god
    public void LeavingACanonGod_ByChoice_CostsAllFavor_AndRecordsItsWrath(string from, string? to)
    {
        var (c, gods) = Worshipper("GswCanon" + from + (to ?? "none"), from, 40);
        long hpBefore = c.HP;
        var cost = GodSwitchSystem.Switch(c, to, GodChangeBy.Player, gods);

        cost.Should().NotBeNull();
        cost!.Value.OldGod.Should().Be(from);
        cost.Value.FavorLost.Should().Be(40);
        cost.Value.WrathLevel.Should().Be(2);
        cost.Value.SmiteDamage.Should().Be(0, "a canon god's wrath is Divine Wrath, not a smite");
        FavorSystem.GetFavor(c, gods).Should().Be(0, "the new god starts at 0");
        c.GodFavorGod.Should().Be(to ?? "");
        c.DivineWrathPending.Should().BeTrue();
        c.DivineWrathLevel.Should().Be(2);
        c.AngeredGodName.Should().Be(from);
        c.BetrayedForGodName.Should().Be(to ?? "");
        c.LastGodSwitchDay.Should().Be(GodSwitchSystem.Today());
        c.HP.Should().Be(Math.Min(hpBefore, c.MaxHP), "no smite from a canon god");
    }

    [Theory]
    [InlineData("Zephyrine", null)]          // L, or W's player-god branch
    [InlineData("Zephyrine", "Terran")]      // W: a player-god for a canon god
    [InlineData("Zephyrine", "Korvessa")]    // J: one player-god for another
    public void LeavingAPlayerGod_ByChoice_CostsAllFavor_AndItsSmiteStrikes(string from, string? to)
    {
        var (c, gods) = Worshipper("GswPlayer" + (to ?? "none"), from, 60);
        long maxBefore = c.MaxHP, hpBefore = c.HP;
        long expected = GodSwitchSystem.SmiteDamageFor(60, maxBefore);
        var preview = GodSwitchSystem.Preview(c, to, gods);
        preview.SmiteDamage.Should().Be(expected);

        var cost = GodSwitchSystem.Switch(c, to, GodChangeBy.Player, gods);

        cost.Should().Be(preview, "the preview is the cost applied");
        cost!.Value.FavorLost.Should().Be(60);
        cost.Value.WrathLevel.Should().Be(0, "a player-god's wrath is its smite");
        expected.Should().BeGreaterThan(0);
        c.HP.Should().Be(Math.Max(1, Math.Min(hpBefore, c.MaxHP) - expected));
        c.DivineWrathPending.Should().BeFalse();
        FavorSystem.GetFavor(c, gods).Should().Be(0);
        c.LastGodSwitchDay.Should().Be(GodSwitchSystem.Today());
    }

    [Fact]
    public void TheSmite_NeverTakesHpBelowOne()
    {
        var (c, gods) = Worshipper("GswFloor", "Zephyrine", 100);
        c.HP = 2;
        GodSwitchSystem.Switch(c, null, GodChangeBy.Player, gods);
        c.HP.Should().Be(1);
    }

    [Fact]
    public void LeavingWithNoFavor_BringsNoWrath()
    {
        var (c, gods) = Worshipper("GswZero", "Umbrath", 0);
        var cost = GodSwitchSystem.Switch(c, "Amara", GodChangeBy.Player, gods)!.Value;
        cost.FavorLost.Should().Be(0);
        cost.WrathLevel.Should().Be(0);
        c.DivineWrathPending.Should().BeFalse();
        c.LastGodSwitchDay.Should().Be(GodSwitchSystem.Today(), "it was still a switch by choice");
    }

    [Fact]
    public void ChoosingAGod_WithNoGod_CostsNothing()
    {
        var gods = new GodSystem();
        var c = Hero("GswFresh");
        var cost = GodSwitchSystem.Switch(c, "Arcanus", GodChangeBy.Player, gods)!.Value;
        cost.LeavesAGod.Should().BeFalse();
        c.DivineWrathPending.Should().BeFalse();
        c.LastGodSwitchDay.Should().Be(-1, "nothing was left");
        FavorSystem.GetFavor(c, gods).Should().Be(0);
        gods.GetPlayerGod(c.Name2).Should().Be("Arcanus");
    }

    [Fact]
    public void ChoosingTheSameGod_CostsNothing_AndKeepsFavor()
    {
        var (c, gods) = Worshipper("GswSame", "Valorian", 45);
        var cost = GodSwitchSystem.Switch(c, "valorian", GodChangeBy.Player, gods)!.Value;
        cost.FavorLost.Should().Be(0);
        FavorSystem.GetFavor(c, gods).Should().Be(45);
        c.DivineWrathPending.Should().BeFalse();
        c.LastGodSwitchDay.Should().Be(-1);
    }

    [Fact]
    public void Manwe_IsRefused_AndNothingChanges()
    {
        var (c, gods) = Worshipper("GswManwe", "Sylvana", 30);
        GodSwitchSystem.Switch(c, "Manwe", GodChangeBy.Player, gods).Should().BeNull();
        FavorSystem.GetFavor(c, gods).Should().Be(30);
        c.DivineWrathPending.Should().BeFalse();
    }

    // ---------------- Changes not made by the worshipper ----------------

    [Theory]
    [InlineData("Judicar", null)]            // a god gone (Temple VerifyPlayerGodExists)
    [InlineData("Judicar", "Zephyrine")]     // a player-god's recruit (Pantheon)
    [InlineData("Zephyrine", "Korvessa")]    // a player-god's recruit of another's follower
    public void AChange_ByTheGameOrAnotherPlayer_CostsTheFavor_ButBringsNoWrath(string from, string? to)
    {
        var (c, gods) = Worshipper("GswOther" + from + (to ?? "none"), from, 80);
        long hpBefore = c.HP;
        var cost = GodSwitchSystem.Switch(c, to, GodChangeBy.Other, gods)!.Value;
        cost.WrathLevel.Should().Be(0);
        cost.SmiteDamage.Should().Be(0);
        c.DivineWrathPending.Should().BeFalse();
        c.DivineWrathLevel.Should().Be(0);
        c.HP.Should().Be(Math.Min(hpBefore, c.MaxHP));
        c.LastGodSwitchDay.Should().Be(-1, "not the worshipper's switch");
        FavorSystem.GetFavor(c, gods).Should().Be(0, "Favor stays with the old god");
    }

    // ---------------- Preview is read-only (a refused prompt changes nothing) ----------------

    [Fact]
    public void Preview_ChangesNothing()
    {
        var (c, gods) = Worshipper("GswPreview", "Discordia", 70);
        var before = (c.GodFavor, c.GodFavorGod, c.HP, c.MaxHP, c.DivineWrathLevel, c.DivineWrathPending, c.LastGodSwitchDay, gods.GetPlayerGod(c.Name2), c.WorshippedGod);
        var cost = GodSwitchSystem.Preview(c, "Amara", gods);
        cost.FavorLost.Should().Be(70);
        cost.WrathLevel.Should().Be(3);
        (c.GodFavor, c.GodFavorGod, c.HP, c.MaxHP, c.DivineWrathLevel, c.DivineWrathPending, c.LastGodSwitchDay, gods.GetPlayerGod(c.Name2), c.WorshippedGod)
            .Should().Be(before);
    }

    [Theory]
    [InlineData("private async Task ProcessWorship(", "ShowSwitchCost(null);", "Loc.Get(\"temple.abandon_for_elder\"", "await SwitchGodAsync(null);")]
    [InlineData("private async Task ProcessWorship(", "ShowSwitchCost(null);   // 1.2.0 Temple gods piece 4: the cost before the choice\n\n", "Loc.Get(\"temple.lost_faith\"", "await SwitchGodAsync(null);\n\n                // In Pascal")]
    [InlineData("private async Task WorshipImmortalGod(", "ShowSwitchCost(chosen.DivineName);   // 1.2.0", "Loc.Get(\"temple.abandon_prompt\")", "await SwitchGodAsync(chosen.DivineName);")]
    [InlineData("private async Task WorshipImmortalGod(", "ShowSwitchCost(chosen.DivineName);\n", "Loc.Get(\"temple.abandon_elder_prompt\"", "await SwitchGodAsync(chosen.DivineName);")]
    [InlineData("private async Task LeaveImmortalFaith(", "ShowSwitchCost(null);", "Loc.Get(\"temple.abandon_faith_prompt\"", "await SwitchGodAsync(null);")]
    public void EachTemplePath_ShowsTheCost_AsksStrictly_AndSwitchesOnlyAfterYes(string method, string preview, string prompt, string switchCall)
    {
        var body = Body("Scripts/Locations/TempleLocation.cs", method);
        int p = body.IndexOf(preview, StringComparison.Ordinal);
        int a = body.IndexOf(prompt, StringComparison.Ordinal);
        int s = body.IndexOf(switchCall, a < 0 ? 0 : a, StringComparison.Ordinal);
        p.Should().BeGreaterThanOrEqualTo(0, "the cost is shown");
        a.Should().BeGreaterThan(p, "the cost is shown before the question");
        body.Substring(0, a).Should().EndWith("terminal.AskYesNoAsync(", "the strict yes/no prompt");
        s.Should().BeGreaterThan(a, "the switch comes after the answer");
        body.Substring(p, a - p).Should().NotContain("SwitchGodAsync(", "nothing switches between the cost and the question");
    }

    [Fact]
    public void TheJPath_NoLongerRollsItsOwnSmite()
    {
        var body = Body("Scripts/Locations/TempleLocation.cs", "private async Task WorshipImmortalGod(");
        body.Should().NotContain("temple.elder_strikes");
        body.Should().NotContain("NextDouble()");
    }

    // ---------------- One helper for every write ----------------

    /// <summary>
    /// Every write of the worshipped god goes through GodSwitchSystem.Switch: GodRegistry's writes
    /// are called only from FaithSystem; the only other writes are the game's own (a deleted, purged,
    /// new or NG+ character, the load restoring the save, GodSystem's own cleanup) and NPC worship.
    /// </summary>
    [Fact]
    public void EveryWriteOfTheWorshippedGod_GoesThroughTheSwitchHelper()
    {
        var files = Directory.GetFiles(Path.Combine(RepoRoot(), "Scripts"), "*.cs", SearchOption.AllDirectories);
        var found = new List<string>();
        var member = new Regex(@"([\w\.\]\)]+)\.WorshippedGod\s*=[^=]");
        foreach (var path in files)
        {
            string rel = Path.GetRelativePath(RepoRoot(), path).Replace('\\', '/');
            var lines = File.ReadAllLines(path);
            for (int i = 0; i < lines.Length; i++)
            {
                string line = lines[i];
                if (line.TrimStart().StartsWith("//")) continue;
                if (line.Contains("SetWorshippedGod(")) found.Add($"{rel}|SetWorshippedGod");
                if (Regex.IsMatch(line, @"\bSetPlayerGod\(")) found.Add($"{rel}|SetPlayerGod");
                if (line.Contains("ClearPlayerGodAnyCase(")) found.Add($"{rel}|ClearPlayerGodAnyCase");
                if (line.Contains("SetPlayerWorshippedGod(")) found.Add($"{rel}|SetPlayerWorshippedGod");
                var m = member.Match(line);
                if (m.Success) found.Add($"{rel}|{m.Groups[1].Value}.WorshippedGod=");
            }
        }
        var counts = found.GroupBy(f => f).ToDictionary(g => g.Key, g => g.Count());
        var expected = new Dictionary<string, int>
        {
            // GodRegistry itself (definition, its writes, and the one call from GodSwitchSystem.Switch)
            ["Scripts/Systems/FaithSystem.cs|SetWorshippedGod"] = 2,
            ["Scripts/Systems/FaithSystem.cs|SetPlayerGod"] = 4,
            ["Scripts/Systems/FaithSystem.cs|c.WorshippedGod="] = 5,
            // GodSystem's own definitions and cleanup (a god removed; the any-case clear)
            ["Scripts/Systems/GodSystem.cs|SetPlayerGod"] = 3,
            ["Scripts/Systems/GodSystem.cs|ClearPlayerGodAnyCase"] = 1,
            // The game's own changes: a deleted save, the load restoring the save's entries
            ["Scripts/Systems/SaveSystem.cs|SetPlayerGod"] = 2,
            ["Scripts/Systems/SaveSystem.cs|ClearPlayerGodAnyCase"] = 2,
            // An account purge; a new character; an NG+ cycle
            ["Scripts/Server/MudServer.cs|SetPlayerGod"] = 1,
            ["Scripts/Server/MudServer.cs|ClearPlayerGodAnyCase"] = 1,
            ["Scripts/Core/GameEngine.cs|SetPlayerGod"] = 1,
            ["Scripts/Core/GameEngine.cs|ClearPlayerGodAnyCase"] = 1,
            // The offline recruit by a player-god (another player's act; the definition and the one call)
            ["Scripts/Systems/IOnlineSaveBackend.cs|SetPlayerWorshippedGod"] = 1,
            ["Scripts/Systems/SqlSaveBackend.cs|SetPlayerWorshippedGod"] = 1,
            ["Scripts/Locations/PantheonLocation.cs|SetPlayerWorshippedGod"] = 1,
            // NPC worship (NPCs have no Favor)
            ["Scripts/Locations/PantheonLocation.cs|target.NpcRef.WorshippedGod="] = 2,
            ["Scripts/Locations/PantheonLocation.cs|npc.WorshippedGod="] = 1,
            ["Scripts/Systems/WorldSimService.cs|npc.WorshippedGod="] = 1,
            // Piece 6: NPC townsfolk faith (the pick and the Manwe clear), and the NPC faith behaviours
            ["Scripts/Systems/NpcFaithSystem.cs|npc.WorshippedGod="] = 2,
            ["Scripts/AI/EnhancedNPCBehaviorSystem.cs|npc.WorshippedGod="] = 1,
            ["Scripts/AI/EnhancedNPCBehaviors.cs|npc.WorshippedGod="] = 1,
        };
        counts.Should().BeEquivalentTo(expected, "a new write of the worshipped god must go through GodSwitchSystem.Switch");
        Count(Source("Scripts/Systems/FaithSystem.cs"), "GodRegistry.SetWorshippedGod(c, newGod, gods, otherSession)").Should().Be(1);
    }

    [Fact]
    public void ThePlayersOwnSwitch_IsSavedAtOnce_InOneWrite()
    {
        var helper = Body("Scripts/Locations/TempleLocation.cs", "private async Task<GodSwitchCost?> SwitchGodAsync(");
        helper.Should().Contain("GodSwitchSystem.Switch(currentPlayer, newGod, GodChangeBy.Player);");
        helper.Should().Contain("await SaveSystem.Instance.AutoSave(currentPlayer, force: true);");
        helper.IndexOf("AutoSave", StringComparison.Ordinal).Should().BeGreaterThan(helper.IndexOf("GodSwitchSystem.Switch", StringComparison.Ordinal));
        Source("Scripts/Locations/TempleLocation.cs").Should().NotContain("SetPlayerWorshippedGod(", "no partial write of the god without its cost");
        Count(Source("Scripts/Locations/TempleLocation.cs"), "GodChangeBy.Player").Should().Be(1, "only the helper switches by the player");
        Body("Scripts/Locations/PantheonLocation.cs", "private async Task ApplyRecruitToPlayer(")
            .Should().Contain("GodSwitchSystem.Switch(player, godName, GodChangeBy.Other, otherSession: true);");
    }

    // ---------------- Once over save and load ----------------

    [Fact]
    public void TheWrath_IsAppliedOnce_AndSurvivesSaveAndLoad()
    {
        var (c, gods) = Worshipper("GswSave", "Mortis", 55);
        GodSwitchSystem.Switch(c, "Amara", GodChangeBy.Player, gods);
        c.DivineWrathLevel.Should().Be(3);
        int day = c.LastGodSwitchDay;
        day.Should().Be(GodSwitchSystem.Today());

        var method = typeof(SaveSystem).GetMethod("SerializePlayer", BindingFlags.NonPublic | BindingFlags.Instance)!;
        var data = (PlayerData)method.Invoke(SaveSystem.Instance, new object[] { c })!;
        data.LastGodSwitchDay.Should().Be(day);
        var back = JsonSerializer.Deserialize<PlayerData>(JsonSerializer.Serialize(data))!;
        var restored = MenuKeysNeedEnterPref1115Tests.Restore(back);
        var loadGods = new GodSystem();
        loadGods.SetPlayerGod(restored.Name2, "Amara");
        GodRegistry.ApplyLoad(restored, loadGods);

        restored.LastGodSwitchDay.Should().Be(day);
        restored.DivineWrathLevel.Should().Be(3, "the load neither repeats nor drops the wrath");
        restored.DivineWrathPending.Should().BeTrue();
        restored.AngeredGodName.Should().Be("Mortis");
        FavorSystem.GetFavor(restored, loadGods).Should().Be(0);
        restored.GodFavorGod.Should().Be("Amara");

        // Asking again for the god already worshipped costs nothing more
        GodSwitchSystem.Switch(restored, "Amara", GodChangeBy.Player, loadGods)!.Value.FavorLost.Should().Be(0);
        restored.DivineWrathLevel.Should().Be(3);
    }

    [Fact]
    public void LastGodSwitchDay_OldSavesReadNever_AndTheEditorShowsIt()
    {
        var data = JsonSerializer.Deserialize<PlayerData>("{}")!;
        data.LastGodSwitchDay.Should().Be(-1);
        MenuKeysNeedEnterPref1115Tests.Restore(data).LastGodSwitchDay.Should().Be(-1);
        Source("Scripts/Editor/PlayerSaveEditor.cs").Should().Contain("p.LastGodSwitchDay = EditorIO.PromptInt(");
    }

    // ---------------- Text ----------------

    [Theory]
    [InlineData("god.switch_cost_favor", 2)]
    [InlineData("god.switch_cost_no_favor", 1)]
    [InlineData("god.switch_cost_wrath", 2)]
    [InlineData("god.switch_cost_smite", 2)]
    [InlineData("god.switch_cost_new_zero", 0)]
    [InlineData("god.switch_wrath_now", 2)]
    [InlineData("god.switch_smite_now", 4)]
    [InlineData("base.wrath_by_leaving", 0)]
    public void SwitchText_IsInAllFiveLanguages(string key, int placeholders)
    {
        foreach (var lang in new[] { "en", "es", "fr", "hu", "it" })
        {
            using var doc = JsonDocument.Parse(Source($"Localization/{lang}.json"));
            doc.RootElement.TryGetProperty(key, out var v).Should().BeTrue($"{lang} {key}");
            string s = v.GetString()!;
            s.Should().NotBeNullOrWhiteSpace();
            for (int i = 0; i < placeholders; i++) s.Should().Contain("{" + i + "}", $"{lang} {key}");
            s.Should().NotContain("{" + placeholders + "}", $"{lang} {key}");
            s.Should().NotContain("\u2014").And.NotContain("\u2013");
        }
    }

    [Fact]
    public void TheStatusScreen_NamesAWrathLeftForNoGod()
    {
        Source("Scripts/Locations/BaseLocation.cs").Should().Contain("Loc.Get(\"base.wrath_by_leaving\")");
    }
}
