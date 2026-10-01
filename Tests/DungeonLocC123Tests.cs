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
using UsurperRemake.Data;
using UsurperRemake.Systems;
using UsurperReborn.Tests.Localization;
using Xunit;

namespace UsurperReborn.Tests;

/// <summary>
/// v1.2.3: the rest of the dungeon system reads in the player's language: the settlement workshop
/// and watchtower, the wilderness shrine menu and discovery news, the rare encounter XP and HP lines
/// and the arena champion, the settlement news, the puzzle titles and hints. Every screen is
/// rendered in English and Hungarian with worst case inputs and every row must fit in 79 columns.
/// </summary>
[Collection("SharedGameSingletons")]
public class DungeonLocC123Tests
{
    private const BindingFlags F = BindingFlags.NonPublic | BindingFlags.Instance;
    private const BindingFlags FS = BindingFlags.NonPublic | BindingFlags.Static;
    private const int MaxWidth = 79;

    // GameConfig.MaxNameLength (30) characters, the longest name a player can have.
    private const string LongName = "Aranyszivu Hosszunevu Kalandor";
    private const long Big = 123456;

    [Fact]
    public void LongName_IsTheLongestAllowedName() => LongName.Length.Should().Be(GameConfig.MaxNameLength);

    private static (TerminalEmulator term, MemoryStream output) Term(string input = "")
    {
        var output = new MemoryStream();
        return (new TerminalEmulator(new MemoryStream(Encoding.UTF8.GetBytes(input)), output), output);
    }

    // A player's typed answer and Enter end the prompt's row; the scripted input stream does not
    // echo, so the render breaks the row after each prompt the screens here show.
    private static readonly string[] PromptKeys = { "ui.press_enter", "ui.your_choice", "dungeon.scout_floor_prompt", "wilderness.pilgrimage_select" };

    private static string Shown(TerminalEmulator term, MemoryStream output)
    {
        term.StreamWriterInternal?.Flush();
        string text = Strip(Encoding.UTF8.GetString(output.ToArray()));
        foreach (var key in PromptKeys)
        {
            string prompt = Loc.Get(key);
            if (prompt.Length > 0) text = text.Replace(prompt, prompt + "\n");
        }
        return text;
    }

    private static string Strip(string s) => Regex.Replace(s, "\u001b\\[[0-9;?]*[A-Za-z]", "");

    private static IEnumerable<string> Rows(string text) => text.Replace("\r", "").Split('\n');

    /// <summary>Writes a render to USURPER_EVIDENCE_DIR when set, for a by-eye check.</summary>
    private static void Capture(string name, string text)
    {
        var dir = Environment.GetEnvironmentVariable("USURPER_EVIDENCE_DIR");
        if (!string.IsNullOrEmpty(dir)) File.WriteAllText(Path.Combine(dir, name), text);
    }

    private static void EveryRowFits(string text, string screen)
    {
        foreach (var row in Rows(text))
            row.Length.Should().BeLessOrEqualTo(MaxWidth, $"every row of the {screen} fits in {MaxWidth} columns: \"{row}\"");
    }

    /// <summary>The text appears as the rows UIHelper.WordWrap gives it, each with the indent.</summary>
    private static void ShowsWrapped(string text, string expected, string indent, string because)
    {
        var rows = Rows(text).ToList();
        var wrapped = UsurperRemake.UI.UIHelper.WordWrap(expected, UsurperRemake.UI.UIHelper.WrapWidth - indent.Length).Select(l => indent + l).ToList();
        int at = rows.IndexOf(wrapped[0]);
        at.Should().BeGreaterOrEqualTo(0, $"{because}: \"{wrapped[0]}\" is a row");
        rows.Skip(at).Take(wrapped.Count).Should().Equal(wrapped, because);
    }

    private static async Task<T> InLanguage<T>(string lang, Func<Task<T>> body)
    {
        var prev = GameConfig.Language;
        bool sr = GameConfig.ScreenReaderMode;
        try
        {
            GameConfig.Language = lang;
            GameConfig.ScreenReaderMode = false;
            return await body();
        }
        finally { GameConfig.Language = prev; GameConfig.ScreenReaderMode = sr; }
    }

    private static string L(string lang, string key, params object[] args) => Loc.GetIn(lang, key, args);

    private static Character Hero(int level = 100) => new()
    {
        Name1 = "tester", Name2 = LongName, Class = CharacterClass.Warrior, Level = level, HP = Big, MaxHP = Big,
        AI = CharacterAI.Human, Mental = 100, Gold = 1_000_000_000,
    };

    private static T At<T>(T location, TerminalEmulator term, Character hero) where T : BaseLocation
    {
        typeof(BaseLocation).GetField("terminal", F)!.SetValue(location, term);
        typeof(BaseLocation).GetField("currentPlayer", F)!.SetValue(location, hero);
        return location;
    }

    private static async Task Run(object target, string method, params object[] args) =>
        await (Task)target.GetType().GetMethod(method, F)!.Invoke(target, args)!;

    private static string Src(params string[] path) =>
        File.ReadAllText(Path.Combine(new[] { HardcodedTextScannerTests.RepoRoot(), "Scripts" }.Concat(path).ToArray()));

    /// <summary>A Random that always rolls the lowest value, so every "unlucky" branch is taken.</summary>
    private sealed class LowRandom : Random
    {
        public override double NextDouble() => 0.0;
        public override int Next(int maxValue) => 0;
        public override int Next(int minValue, int maxValue) => minValue;
    }

    // ---------- 1. the settlement workshop ----------

    [Theory]
    [InlineData("en")]
    [InlineData("hu")]
    public async Task Workshop_SharpenAndUsedToday_RenderInLanguage_AndFit(string lang)
    {
        string text = await InLanguage(lang, async () =>
        {
            var (term, output) = Term("\n\n");
            var hero = Hero();
            var s = At(new SettlementLocation(), term, hero);
            await Run(s, "UseWorkshopService");
            hero.SettlementWorkshopUsedToday.Should().BeTrue();
            await Run(s, "UseWorkshopService");
            return Shown(term, output);
        });
        Capture($"dungeon-c-workshop-{lang}.txt", text);
        foreach (var key in new[] { "dungeon.workshop_hone", "dungeon.workshop_sharpened", "dungeon.workshop_used_today" })
            ShowsWrapped(text, L(lang, key), "  ", key);
        if (lang == "hu") text.Should().NotContain("smiths").And.NotContain("sharpened");
        EveryRowFits(text, "settlement workshop");
    }

    // ---------- 2. the watchtower scout report, every floor ----------

    [Theory]
    [InlineData("en")]
    [InlineData("hu")]
    public async Task Watchtower_EveryFloor_RendersInLanguage_AndFits(string lang)
    {
        var all = new StringBuilder();
        for (int floor = 1; floor <= 100; floor++)
        {
            int f = floor;
            string text = await InLanguage(lang, async () =>
            {
                var (term, output) = Term($"{f}\n\n");
                var s = At(new SettlementLocation(), term, Hero());
                await Run(s, "UseWatchtowerService");
                return Shown(term, output);
            });
            text.Should().Contain(L(lang, "dungeon.scout_report_header", f));
            text.Should().Contain(L(lang, "dungeon.scout_report_footer"));
            EveryRowFits(text, $"watchtower report for floor {f}");
            all.Append(text);
        }
        string every = all.ToString();
        Capture($"dungeon-c-watchtower-{lang}.txt", every);
        every.Should().Contain(L(lang, "dungeon.scout_floor_prompt"));
        every.Should().Contain(L(lang, "dungeon.scout_old_god_warning"));
        every.Should().Contain(L(lang, "dungeon.scout_seal_floor"));
        every.Should().Contain(L(lang, "dungeon.scout_tag_monsters"));
        every.Should().Contain(L(lang, "dungeon.scout_icon_boss"));
        every.Should().Contain(L(lang, "dungeon.scout_tag_stairs"));
        every.Should().MatchRegex(Regex.Escape(L(lang, "dungeon.scout_floor_summary", "T", 1, 2))
            .Replace("T", ".+").Replace("1", "[0-9]+").Replace("2", "[0-9]+"));
        if (lang == "hu")
            every.Should().NotContain("WATCHTOWER").And.NotContain(" Monsters").And.NotContain("Danger:").And.NotContain("↓Stairs")
                .And.NotContain("Scout report");
    }

    // ---------- 3. the wilderness shrine menu ----------

    [Theory]
    [InlineData("en")]
    [InlineData("hu")]
    public async Task PilgrimageMenu_FavorAndCancel_RenderInLanguage_AndFit(string lang)
    {
        string text = await InLanguage(lang, async () =>
        {
            var (term, output) = Term("0\n");
            var hero = Hero();
            foreach (var shrine in DruidShrineData.Shrines) hero.ShrineFavor[shrine.Id] = (int)Big;
            var w = At(new WildernessLocation(), term, hero);
            await Run(w, "ShowPilgrimageMenu");
            return Shown(term, output);
        });
        Capture($"dungeon-c-pilgrimage-{lang}.txt", text);
        text.Should().Contain(L(lang, "dungeon.wild_shrine_favor", Big));
        text.Should().Contain(L(lang, "settlement.cancel"));
        if (lang == "hu") text.Should().NotContain("favor:").And.NotContain("Cancel");
        EveryRowFits(text, "pilgrimage menu");
    }

    [Fact]
    public void WildernessDiscoveryNews_IsKeyed_AndReadsInHungarian()
    {
        Src("Locations", "WildernessLocation.cs").Should()
            .Contain("Newsy(Loc.Get(\"dungeon.wild_discovery_news\", currentPlayer.Name, WildernessData.GetDiscoveryName(discovery), WildernessData.GetRegionName(region)))")
            .And.NotContain("discovered {discovery.Name}");
        // The longest discovery and region names the line can carry.
        var region = WildernessData.Regions.OrderByDescending(r => r.Name.Length).First();
        var discovery = WildernessData.Regions.SelectMany(r => r.Discoveries).OrderByDescending(d => d.Name.Length).First();
        string en = L("en", "dungeon.wild_discovery_news", LongName, discovery.Name, region.Name);
        en.Should().Be($"☆ {LongName} discovered {discovery.Name} in the {region.Name}!");
        string hu = InLanguage("hu", () => Task.FromResult(L("hu", "dungeon.wild_discovery_news", LongName,
            WildernessData.GetDiscoveryName(discovery), WildernessData.GetRegionName(region)))).Result;
        hu.Should().Contain("felfedezte").And.NotContain("discovered");
    }

    // ---------- 4. rare encounters: XP and HP lines, the arena champion ----------

    private static async Task<string> Encounter(string lang, string method, string input)
    {
        var field = typeof(RareEncounters).GetField("random", FS)!;
        var prev = field.GetValue(null);
        field.SetValue(null, new LowRandom());
        try
        {
            return await InLanguage(lang, async () =>
            {
                var (term, output) = Term(input);
                var m = typeof(RareEncounters).GetMethod(method, FS)!;
                await (Task)m.Invoke(null, new object[] { term, Hero(), 100 })!;
                return Shown(term, output);
            });
        }
        finally { field.SetValue(null, prev); }
    }

    [Theory]
    [InlineData("en")]
    [InlineData("hu")]
    public async Task MinstrelListen_XpLine_RendersInLanguage_AndFits(string lang)
    {
        string text = await Encounter(lang, "WanderingMinstrelEncounter", "9\n\n");
        Capture($"dungeon-c-minstrel-{lang}.txt", text);
        text.Should().Contain(L(lang, "dungeon.encounter_xp_gain", 100 * 10));
        if (lang == "hu") text.Should().NotContain(" XP)");
        EveryRowFits(text, "minstrel encounter");
    }

    [Theory]
    [InlineData("en", "AncientTombEncounter", "O\n\n", 3)]
    [InlineData("hu", "AncientTombEncounter", "O\n\n", 3)]
    [InlineData("en", "LostChildEncounter", "C\n\n", 5)]
    [InlineData("hu", "LostChildEncounter", "C\n\n", 5)]
    [InlineData("en", "TreasureHoardEncounter", "T\n\n", 4)]
    [InlineData("hu", "TreasureHoardEncounter", "T\n\n", 4)]
    public async Task EncounterTrap_HpLine_RendersInLanguage_AndFits(string lang, string method, string input, int divisor)
    {
        string text = await Encounter(lang, method, input);
        Capture($"dungeon-c-{method}-{lang}.txt", text);
        text.Should().Contain(L(lang, "feature.minus_hp", (int)(Big / divisor)));
        if (lang == "hu") text.Should().NotContain(" HP!");
        EveryRowFits(text, method);
    }

    [Fact]
    public void ArenaChampionName_IsKeyed_AndKeepsTheEnglishMonsterName()
    {
        Src("Systems", "RareEncounters.cs").Should()
            .Contain("champion.Name = Loc.Get(\"dungeon.arena_champion_name\", champion.Name);").And.NotContain("$\"Arena {champion.Name}\"");
        L("en", "dungeon.arena_champion_name", "Zombie").Should().Be("Arena Zombie");
        L("hu", "dungeon.arena_champion_name", "Zombie").Should().Be("Aréna Zombie");
        // CombatEngine's undead check and the kill quests read the English monster name inside it.
        foreach (var lang in new[] { "en", "es", "fr", "hu", "it" })
            L(lang, "dungeon.arena_champion_name", "Zombie").Should().Contain("Zombie", lang);
    }

    // ---------- 5. settlement news ----------

    [Fact]
    public void SettlementNews_IsKeyed_AndReadsInHungarian()
    {
        string src = Src("Systems", "SettlementSystem.cs");
        src.Should().Contain("Newsy(true, Loc.Get(\"dungeon.settlement_founded_news\"))")
            .And.Contain("Newsy(true, Loc.Get(newsKey, template?.LocName ?? buildingName))")
            .And.NotContain("\"foundation has been laid\"").And.NotContain("$\"The settlement's {buildingName} {tierName}!\"");
        L("en", "dungeon.settlement_founded_news").Should().Be("A settlement has been founded beyond the city gates!");
        L("hu", "dungeon.settlement_founded_news").Should().Be("Település alakult a városkapukon túl!");
    }

    // ---------- 6. the puzzle titles and hints ----------

    private static readonly PuzzleType[] TitledPuzzles =
    {
        PuzzleType.SymbolAlignment, PuzzleType.PressurePlates, PuzzleType.NumberGrid, PuzzleType.MemoryMatch,
        PuzzleType.LightDarkness, PuzzleType.ItemCombination, PuzzleType.EnvironmentChange, PuzzleType.ReflectionPuzzle,
    };

    [Theory]
    [InlineData("en")]
    [InlineData("hu")]
    public async Task PuzzleHeaderAndHint_EveryTitledPuzzle_RendersInLanguage_AndFits(string lang)
    {
        var all = new StringBuilder();
        foreach (var type in TitledPuzzles)
        {
            string text = await InLanguage(lang, () =>
            {
                var (term, output) = Term();
                var puzzle = PuzzleSystem.Instance.GeneratePuzzle(type, 5, DungeonTheme.AncientRuins);
                typeof(PuzzleSystem).GetMethod("DisplayPuzzleHeader", F)!.Invoke(PuzzleSystem.Instance, new object[] { puzzle, term });
                if (puzzle.Hints.Count > 0)
                    typeof(PuzzleSystem).GetMethod("ShowHint", F)!.Invoke(PuzzleSystem.Instance, new object[] { puzzle, term });
                if (type == PuzzleType.NumberGrid) puzzle.Hints.Should().Equal(L(lang, "dungeon.puzzle_number_hint", puzzle.TargetNumber));
                return Task.FromResult(Shown(term, output));
            });
            EveryRowFits(text, $"{type} puzzle header");
            all.Append(text);
        }
        string every = all.ToString();
        Capture($"dungeon-c-puzzles-{lang}.txt", every);
        foreach (var key in new[] { "symbol", "pressure", "number", "memory", "light", "alchemy", "elemental", "mirror" })
            every.Should().Contain($"║  {L(lang, $"dungeon.puzzle_title_{key}").PadRight(62)}║", key);
        every.Should().Contain(L(lang, "dungeon.puzzle_hint_banner"));
        if (lang == "hu") every.Should().NotContain("Hall of Mirrors").And.NotContain("HINT").And.NotContain("The answer is");
    }

    [Fact]
    public void PuzzleTitles_FitTheHeaderBox_InEveryLanguage()
    {
        // DisplayPuzzleHeader pads the title to 62 inside the box; a longer title breaks the frame.
        foreach (var lang in new[] { "en", "es", "fr", "hu", "it" })
            foreach (var key in new[] { "symbol", "pressure", "number", "memory", "light", "alchemy", "elemental", "mirror" })
                L(lang, $"dungeon.puzzle_title_{key}").Length.Should().BeLessOrEqualTo(62, $"{lang} {key}");
    }
}
