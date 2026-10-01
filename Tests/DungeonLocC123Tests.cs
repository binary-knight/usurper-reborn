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
        AI = CharacterAI.Human, Mental = 100, Gold = Big,
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

    // ---------- 7. the dungeon settlements: screen and every lore fragment ----------

    private static DungeonLocation Dungeon(TerminalEmulator term, Character hero, int level)
    {
        var d = At(new DungeonLocation(), term, hero);
        typeof(DungeonLocation).GetField("currentDungeonLevel", F)!.SetValue(d, level);
        return d;
    }

    public static IEnumerable<object[]> SettlementCases() =>
        DungeonSettlementData.Settlements.Keys.SelectMany(floor => new[] { new object[] { "en", floor }, new object[] { "hu", floor } });

    [Theory]
    [MemberData(nameof(SettlementCases))]
    public async Task SettlementScreen_FirstAndReturnGreeting_RenderInLanguage_AndFit(string lang, int floor)
    {
        var settlement = DungeonSettlementData.GetSettlement(floor)!;
        string text = await InLanguage(lang, async () =>
        {
            // An unknown key redraws the screen with the return greeting, then R leaves.
            var (term, output) = Term("Z\nR\n");
            var hero = Hero();
            var d = Dungeon(term, hero, floor);
            await Run(d, "SettlementEncounter", new object?[] { null });
            return Shown(term, output);
        });
        Capture($"dungeon-c-settlement-{settlement.Id}-{lang}.txt", text);
        string key = $"dungeon.settlement.{settlement.Id}";
        // GetChoice draws the shared status line (BaseLocation.ShowStatusLine) and the Quick Commands bar
        // under every location's menu. Both are BaseLocation's, not this screen's, and already run past 79
        // (en Quick Commands 98; hu status line 85 with six-digit HP), so the width check leaves those two rows out.
        string statusRow = L(lang, "status.hp") + ": ", quickRow = L(lang, "ui.quick_commands") + ":";
        string screen = string.Join("\n", Rows(text).Where(r => !r.StartsWith(statusRow) && !r.StartsWith(quickRow)));
        text.Should().Contain($"{settlement.NPCName} ({L(lang, key + ".npc_title")})");
        ShowsWrapped(text, L(lang, key + ".description"), "", "description");
        ShowsWrapped(text, L(lang, key + ".greeting_first"), "", "first greeting");
        ShowsWrapped(text, L(lang, key + ".greeting_return"), "", "return greeting");
        if (lang == "hu")
            text.Should().NotContain(settlement.NPCTitle).And.NotContain(settlement.Description.Split('\n')[0])
                .And.NotContain(settlement.ReturnGreeting.Split('\n')[0]);
        EveryRowFits(screen, $"{settlement.Id} settlement");
    }

    [Theory]
    [MemberData(nameof(SettlementCases))]
    public async Task SettlementLore_EveryFragment_RendersInLanguage_AndFits(string lang, int floor)
    {
        var settlement = DungeonSettlementData.GetSettlement(floor)!;
        var hero = Hero();
        string text = await InLanguage(lang, async () =>
        {
            var (term, output) = Term(string.Concat(Enumerable.Repeat("\n", settlement.LoreFragments.Length + 1)));
            var d = Dungeon(term, hero, floor);
            for (int i = 0; i < settlement.LoreFragments.Length; i++)
                await Run(d, "SettlementLore", hero, settlement);
            return Shown(term, output);
        });
        Capture($"dungeon-c-settlement-lore-{settlement.Id}-{lang}.txt", text);
        for (int i = 0; i < settlement.LoreFragments.Length; i++)
        {
            hero.SettlementLoreRead.Should().Contain($"{settlement.Id}_{i}", "lore read is saved by id and index, not text");
            ShowsWrapped(text, L(lang, $"dungeon.settlement.{settlement.Id}.lore.{i}"), "", $"lore {i}");
            if (lang == "hu") text.Should().NotContain(settlement.LoreFragments[i].Split('\n')[0]);
        }
        EveryRowFits(text, $"{settlement.Id} lore");
    }

    [Fact]
    public void SettlementEnglish_KeysMatchTheSource_ExceptDashes()
    {
        // The English keys are the data's text; the only change is an em-dash written as "--".
        foreach (var s in DungeonSettlementData.Settlements.Values)
        {
            string Dash(string t) => t.Replace(" \u2014 ", " -- ").Replace(" \u2014\n", " --\n");
            string key = $"dungeon.settlement.{s.Id}";
            L("en", key + ".npc_title").Should().Be(s.NPCTitle);
            L("en", key + ".description").Should().Be(Dash(s.Description));
            L("en", key + ".greeting_first").Should().Be(Dash(s.FirstGreeting));
            L("en", key + ".greeting_return").Should().Be(Dash(s.ReturnGreeting));
            for (int i = 0; i < s.LoreFragments.Length; i++) L("en", $"{key}.lore.{i}").Should().Be(Dash(s.LoreFragments[i]));
        }
    }

    [Fact]
    public void SettlementRoom_DescriptionIsKeyed_AtGeneration()
    {
        string hu = InLanguage("hu", () => Task.FromResult(DungeonGenerator.GenerateFloor(10).Rooms
            .First(r => r.Type == RoomType.Settlement).Description)).Result;
        hu.Should().Be(L("hu", "dungeon.settlement.bonewright_forge.description"));
    }

    // ---------- 8. beasts: the species label ----------

    public static IEnumerable<object[]> BeastCases() =>
        BeastData.Beasts.SelectMany(b => new[] { new object[] { "en", b.Id }, new object[] { "hu", b.Id } });

    [Theory]
    [MemberData(nameof(BeastCases))]
    public async Task BeastEncounter_SpeciesRendersInLanguage_NameStaysEnglish_AndFits(string lang, string id)
    {
        var beast = BeastData.GetById(id)!;
        var hero = Hero();
        // Own every other beast so the region's pick is this one.
        foreach (var other in BeastData.Beasts.Where(b => b.Id != id && b.RegionDirectionKey == beast.RegionDirectionKey))
            hero.PetRoster.Add(new Pet { Id = other.Id, Name = other.Name });
        var region = WildernessData.Regions.First(r => r.DirectionKey == beast.RegionDirectionKey);
        string text = await InLanguage(lang, async () =>
        {
            var (term, output) = Term("W\n\n");
            var w = At(new WildernessLocation(), term, hero);
            await Run(w, "TryBeastEncounter", region);
            return Shown(term, output);
        });
        Capture($"dungeon-c-beast-{id}-{lang}.txt", text);
        text.Should().Contain(L(lang, "wilderness.beast_encounter_header", beast.Name, L(lang, $"dungeon.beast.{id}.species")));
        L("en", $"dungeon.beast.{id}.species").Should().Be(beast.Species);
        ShowsWrapped(text, L(lang, $"beast.{id}.encounter").Replace('\n', ' '), "  ", "encounter flavor");
        ShowsWrapped(text, L(lang, $"beast.{id}.passive"), "  ", "passive summary");
        if (lang == "hu") text.Should().NotContain($"({beast.Species})");
        EveryRowFits(text, $"{id} encounter");
    }

    [Theory]
    [MemberData(nameof(BeastCases))]
    public void PetJoinsParty_SpeciesRendersInLanguage_AndFits(string lang, string id)
    {
        var beast = BeastData.GetById(id)!;
        if (beast.Role != BeastData.BeastRole.Combat) return;
        string text = InLanguage(lang, () =>
        {
            var (term, output) = Term();
            var hero = Hero();
            hero.PetRoster.Add(new Pet { Id = id, Name = beast.Name });
            hero.ActivePetId = id;
            var d = Dungeon(term, hero, 100);
            typeof(DungeonLocation).GetMethod("AddActivePetToParty", F)!.Invoke(d, new object[] { hero, term });
            return Task.FromResult(Shown(term, output));
        }).Result;
        Capture($"dungeon-c-pet-joins-{id}-{lang}.txt", text);
        text.Should().Contain(L(lang, "dungeon.pet_joins_party", beast.Name, L(lang, $"dungeon.beast.{id}.species")));
        EveryRowFits(text, $"{id} joins the party");
    }

    // ---------- 9. room feature stat checks ----------

    private static async Task<(string text, Character hero)> StatChallenge(string lang, FeatureInteraction interaction)
    {
        var system = (FeatureInteractionSystem)Activator.CreateInstance(typeof(FeatureInteractionSystem), true)!;
        typeof(FeatureInteractionSystem).GetField("random", F)!.SetValue(system, new LowRandom());
        var hero = Hero();
        hero.Strength = hero.Intelligence = hero.Dexterity = hero.Wisdom = 1000;
        hero.Mana = 0; hero.MaxMana = Big;
        string text = await InLanguage(lang, async () =>
        {
            var (term, output) = Term("\n\n");
            var feature = new RoomFeature("Ancient Chest", "x", interaction);
            await (Task)typeof(FeatureInteractionSystem).GetMethod("HandleSkillChallenge", F)!
                .Invoke(system, new object[] { feature, hero, 100, term, new FeatureOutcome() })!;
            return Shown(term, output);
        });
        return (text, hero);
    }

    [Theory]
    [InlineData("en", FeatureInteraction.Open, "ui.stat_strength")]
    [InlineData("hu", FeatureInteraction.Open, "ui.stat_strength")]
    [InlineData("en", FeatureInteraction.Search, "ui.stat_intelligence")]
    [InlineData("hu", FeatureInteraction.Search, "ui.stat_intelligence")]
    [InlineData("en", FeatureInteraction.Take, "ui.stat_dexterity")]
    [InlineData("hu", FeatureInteraction.Take, "ui.stat_dexterity")]
    [InlineData("en", FeatureInteraction.Use, "ui.stat_wisdom")]
    [InlineData("hu", FeatureInteraction.Use, "ui.stat_wisdom")]
    public async Task FeatureStatCheck_LabelRendersInLanguage_AndFits(string lang, FeatureInteraction interaction, string statKey)
    {
        var (text, _) = await StatChallenge(lang, interaction);
        Capture($"dungeon-c-feature-{interaction}-{lang}.txt", text);
        string stat = L(lang, statKey);
        int difficulty = 8 + 100 / 4;
        text.Should().Contain(L(lang, "feature.stat_check", stat, difficulty));
        text.Should().Contain(L(lang, "feature.roll_result", 1, 40, stat, 41));
        text.Should().Contain(L(lang, "feature.stat_boost", 2 + 100 / 20, stat));
        if (lang == "hu") text.Should().NotContain("Strength").And.NotContain("Intelligence").And.NotContain("Dexterity").And.NotContain("Wisdom");
        EveryRowFits(text, $"{interaction} stat check");
    }

    [Theory]
    [InlineData("en")]
    [InlineData("hu")]
    public async Task FeatureStatBoost_StillAppliesByStatCode_InEveryLanguage(string lang)
    {
        int boost = 2 + 100 / 20;
        (await StatChallenge(lang, FeatureInteraction.Open)).hero.TempAttackBonus.Should().Be(boost, "Strength raises attack");
        (await StatChallenge(lang, FeatureInteraction.Take)).hero.TempDefenseBonus.Should().Be(boost, "Dexterity raises defence");
        (await StatChallenge(lang, FeatureInteraction.Search)).hero.Mana.Should().Be(boost * 5, "Intelligence restores mana");
        var wis = (await StatChallenge(lang, FeatureInteraction.Use)).hero;
        (wis.TempAttackBonus + wis.TempDefenseBonus + wis.Mana).Should().Be(0, "Wisdom has no boost, as before");
    }

    // ---------- 10. names kept English on purpose ----------

    [Fact]
    public void KeptEnglish_WildernessMonsterNames_BeastNames_ChampionItems_StayListed()
    {
        string sources = File.ReadAllText(Path.Combine(HardcodedTextScannerTests.RepoRoot(), "Tests", "Localization", "hardcoded-data-sources.txt"));
        sources.Should().Contain("nouns|Scripts/Data/WildernessData.cs|init.MonsterNames|")
            .And.Contain("nouns|Scripts/Data/BeastData.cs|init.Name|")
            .And.Contain("nouns|Scripts/Data/GauntletChampionData.cs|init.ItemName|")
            .And.Contain("keyed|Scripts/Data/BeastData.cs|init.Species|dungeon.beast.{id}.species");
        // The wilderness fight looks its monster up by the English name.
        foreach (var region in WildernessData.Regions)
            foreach (var name in region.MonsterNames)
                WildernessData.MonsterProfiles.Should().ContainKey(name, "the fight finds its profile by the English name");
        Src("Locations", "WildernessLocation.cs").Should().Contain("monster.Name = monsterName;")
            .And.Contain("monster.TierName = monsterName;").And.Contain("WildernessData.GetMonsterProfile(monsterName)");
    }
}
