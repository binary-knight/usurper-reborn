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
using UsurperRemake.Locations;
using UsurperRemake.Systems;
using UsurperRemake.UI;
using Xunit;

namespace UsurperReborn.Tests;

/// <summary>
/// v1.2.5 (data piece 9): the rest of the data tables. GameConfig (message of the day, crafting materials, race and
/// class names, race descriptions and restrictions, prestige descriptions, appearance colours, the creation help
/// screens, god rank titles), the file save backend's load errors, the save repair errors, the update download
/// errors, NPC greetings and the NPC info line, the Level Master descriptions and crystal ball navigation, and the
/// inventory error. What is stored or matched (the default message of the day, save types, appearance numbers,
/// material ids, the English tables the wiki export reads) stays English; what is shown is in the reader's language.
/// </summary>
[Collection("SharedGameSingletons")]
public class DataRest125Tests : IDisposable
{
    private const BindingFlags F = BindingFlags.NonPublic | BindingFlags.Instance;
    private const BindingFlags SNP = BindingFlags.NonPublic | BindingFlags.Static;
    private const int MaxWidth = 79;
    private static readonly string[] AllLanguages = { "en", "es", "fr", "hu", "it" };
    private static readonly string[] OtherLanguages = { "es", "fr", "hu", "it" };

    // GameConfig.MaxNameLength (30) characters, the longest name a player can have.
    private const string LongName = "Bartholomew Thistlewood Grande";

    private readonly string _dir = Path.Combine(Path.GetTempPath(), "drest125-" + Guid.NewGuid().ToString("N"));

    public DataRest125Tests() { Directory.CreateDirectory(_dir); }

    public void Dispose()
    {
        try { Directory.Delete(_dir, true); } catch { }
    }

    // ---------- helpers ----------

    [Fact]
    public void TheLongName_IsTheLongestNameAPlayerCanHave() => LongName.Length.Should().Be(GameConfig.MaxNameLength);

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

    private static Screen NewScreen(params string[] lines)
    {
        var s = new Screen();
        s.Term = new TerminalEmulator(new LineStream(lines.Concat(Enumerable.Repeat("", 12)), _ =>
        {
            s.Term.StreamWriterInternal?.Flush();
            s.Output.WriteByte((byte)'\n');
        }), s.Output);
        return s;
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

    private static T InLang<T>(string lang, Func<T> body) =>
        InLanguage(lang, () => Task.FromResult(body())).GetAwaiter().GetResult();

    private static string L(string lang, string key, params object[] args) => Loc.GetIn(lang, key, args);

    private static List<string> Rows(string text) => text.Replace("\r", "").Split('\n').ToList();

    private static void Capture(string name, string text)
    {
        var dir = Environment.GetEnvironmentVariable("USURPER_EVIDENCE_DIR");
        if (!string.IsNullOrEmpty(dir)) File.WriteAllText(Path.Combine(dir, name), text);
    }

    /// <summary>A framed box row is at most 80 wide; every other row fits in 79.</summary>
    private static void EveryRowFits(IEnumerable<string> rows, string screen)
    {
        foreach (var row in rows)
            row.Length.Should().BeLessOrEqualTo(row.Length > 0 && "╔║╚╠".IndexOf(row[0]) >= 0 ? MaxWidth + 1 : MaxWidth,
                $"every row of the {screen} fits in {MaxWidth} columns: \"{row}\"");
    }

    private static List<string> EnglishKeys(params string[] prefixes)
    {
        string path = Path.Combine(UsurperReborn.Tests.Localization.HardcodedTextScannerTests.RepoRoot(), "Localization", "en.json");
        using var doc = JsonDocument.Parse(File.ReadAllText(path));
        return doc.RootElement.EnumerateObject().Select(p => p.Name).Where(k => prefixes.Any(k.StartsWith)).ToList();
    }

    private static readonly string[] NewKeyPrefixes =
    {
        "motd.default", "creation.race_desc.", "creation.race_restriction.", "creation.prestige_desc.", "appearance.",
        "creation.help.", "material.", "npc.seth_greeting.", "npc.special_greeting.", "npc.display_info",
        "level_master.desc_", "level_master.crystal_nav_prev", "level_master.crystal_nav_next", "version.download_",
        "save_repair.", "save.load_error_",
    };

    // Same as the English by design: French "Blond" is the French word.
    private static readonly HashSet<string> SameByDesign = new() { "fr:appearance.hair.4" };

    // ---------- 1. keys ----------

    [Fact]
    public void EveryNewKey_IsInFiveLanguages_AndTranslated()
    {
        var keys = EnglishKeys(NewKeyPrefixes);
        keys.Remove("version.download_at");   // an older key under the same prefix
        keys.Should().HaveCount(131, "the keys this piece added");
        foreach (var key in keys)
            foreach (var lang in OtherLanguages)
            {
                Loc.HasIn(lang, key).Should().BeTrue($"{key} is in {lang}");
                if (SameByDesign.Contains(lang + ":" + key)) continue;
                L(lang, key).Should().NotBe(L("en", key), $"{lang} {key} is translated");
                Regex.Matches(L(lang, key), @"\{\d\}").Select(m => m.Value).OrderBy(x => x)
                    .Should().Equal(Regex.Matches(L("en", key), @"\{\d\}").Select(m => m.Value).OrderBy(x => x), $"{lang} {key} keeps the placeholders");
            }
    }

    [Fact]
    public void TheEnglishKeys_AreTheEnglishOfTheTables_SoEnglishScreensAreUnchanged()
    {
        foreach (CharacterRace race in Enum.GetValues(typeof(CharacterRace)))
        {
            InLang("en", () => GameConfig.GetLocalizedRaceDescription(race)).Should().Be(GameConfig.RaceDescriptions[race]);
            InLang("en", () => GameConfig.GetLocalizedRaceRestriction(race)).Should().Be(GameConfig.RaceRestrictionReasons[race]);
            InLang("en", () => GameConfig.GetLocalizedRaceName(race)).Should().Be(GameConfig.RaceNames[(int)race], "the wiki export reads RaceNames");
        }
        foreach (var (cls, desc) in GameConfig.PrestigeClassDescriptions)
            InLang("en", () => GameConfig.GetLocalizedPrestigeDescription(cls)).Should().Be(desc);
        for (int i = 1; i < GameConfig.EyeColors.Length; i++) InLang("en", () => GameConfig.GetLocalizedEyeColor(i)).Should().Be(GameConfig.EyeColors[i]);
        for (int i = 1; i < GameConfig.HairColors.Length; i++) InLang("en", () => GameConfig.GetLocalizedHairColor(i)).Should().Be(GameConfig.HairColors[i]);
        for (int i = 1; i < GameConfig.SkinColors.Length; i++) InLang("en", () => GameConfig.GetLocalizedSkinColor(i)).Should().Be(GameConfig.SkinColors[i]);
        GameConfig.GetLocalizedEyeColor(0).Should().Be("", "0 is no colour, as the table's empty first entry");
        foreach (var m in GameConfig.CraftingMaterials)
        {
            InLang("en", () => m.LocName).Should().Be(m.Name);
            InLang("en", () => m.LocDescription).Should().Be(m.Description);
        }
        L("en", "motd.default").Should().Be(GameConfig.DefaultMessageOfTheDay);
        foreach (var field in new[] { "GoodMaster", "NeutralMaster", "EvilMaster" })
        {
            var master = (MasterInfo)typeof(LevelMasterLocation).GetField(field, SNP)!.GetValue(null)!;
            InLang("en", () => master.LocDescription).Should().Be(master.Description);
        }
        Enumerable.Range(1, 5).Select(i => L("en", "npc.seth_greeting." + i)).Should().Equal(
            "You lookin' at me funny?!", "*hiccup* Want to fight?", "I can take anyone in this place!", "*burp* You think you're tough?", "Another pretty boy... pfft!");
        InLang("en", () => Loc.Get("creation.race_restricted", GameConfig.GetLocalizedRaceName(CharacterRace.Hobbit),
            GameConfig.ArticulateForLanguage(GameConfig.GetLocalizedClassName(CharacterClass.Assassin), capitalize: false)))
            .Should().Be("Sorry, Hobbit cannot be an Assassin!", "the English line is as before");
    }

    private const string OldRaceHelp = @"
Race determines your basic physical and mental characteristics:

Human     - Balanced in all areas. Can be any class.
Hobbit    - Small but agile. Good rangers, rogues, bards. Too small for heavy combat.
Elf       - Graceful and magical. Excellent mages and clerics. Dislike brute force.
Half-Elf  - Versatile like humans. Can be any class.
Dwarf     - Strong and tough. Great warriors. Distrust arcane magic.
Troll     - Massive brutes with natural regeneration. Warriors, barbarians, rangers only.
Orc       - Aggressive fighters. Warriors, assassins, rangers. Limited magic.
Gnome     - Small and clever. Great mages, alchemists. Poor heavy fighters.
Gnoll     - Pack hunters. Warriors, rangers, assassins. Limited intellect.
Mutant    - Chaotic and unpredictable. Can be any class.
";

    private const string OldClassHelp = @"
Class determines your profession and abilities:

=== MELEE FIGHTERS ===
Warrior   - Strong fighters, masters of weapons. Balanced and reliable.
Barbarian - Savage fighters with incredible strength. Requires brute force races.
Paladin   - Holy warriors of virtue. Restricted to honorable races.

=== HYBRID CLASSES ===
Ranger    - Woodsmen and trackers. Balanced fighters with survival skills.
Assassin  - Deadly killers, masters of stealth. Requires cunning and dexterity.
Bard      - Musicians and storytellers. Social skills and light combat.
Jester    - Entertainers and tricksters. Very agile and unpredictable.

=== MAGIC USERS ===
Magician  - Powerful spellcasters with low health. Requires high intellect.
Sage      - Scholars and wise magic users. Requires wisdom and study.
Cleric    - Healers and holy magic users. Requires devotion and wisdom.
Alchemist - Potion makers and researchers. Requires intellect and patience.

=== PRESTIGE CLASSES (NG+) ===
Tidesworn    - Ocean's divine shield. Tank/healer hybrid. Requires Holy alignment ending.
Wavecaller   - Ocean's harmonics. Support/buffer specialist. Requires Savior ending.
Cyclebreaker - Reality manipulator. Balanced versatility. Requires Defiant ending.
Abysswarden  - Old God prison warden. Drain/debuff striker. Requires Usurper ending.
Voidreaver   - Void consumer. Extreme glass cannon. Requires Usurper ending.

=== RACE-LOCKED CLASSES ===
Mystic Shaman - Tribal caster who summons totems and enchants weapons. Troll/Orc/Gnoll only.
";

    /// <summary>The help entries' words, the text after "- " on each old row.</summary>
    private static List<string> HelpEntryTexts(string help) =>
        Rows(help).Where(r => r.Contains(" - ")).Select(r => r.Substring(r.IndexOf(" - ") + 3)).ToList();

    [Fact]
    public void TheHelpScreens_InEnglish_SayWhatTheOldTextSaid()
    {
        string Joined(List<string> rows) => string.Join(" ", rows.Select(r => r.Trim()));
        string Squash(string s) => Regex.Replace(s, @"\s+", " ").Trim();
        Squash(Joined(InLang("en", CharacterCreationSystem.RaceHelpRows))).Should().Be(Squash(OldRaceHelp));
        Squash(Joined(InLang("en", CharacterCreationSystem.ClassHelpRows))).Should().Be(Squash(OldClassHelp));
    }

    [Fact]
    public void TheHelpScreens_InHungarian_HaveNoEnglishEntryLeft()
    {
        var race = string.Join("\n", InLang("hu", CharacterCreationSystem.RaceHelpRows));
        var cls = string.Join("\n", InLang("hu", CharacterCreationSystem.ClassHelpRows));
        Capture("help-race-hu.txt", race);
        Capture("help-class-hu.txt", cls);
        foreach (var english in HelpEntryTexts(OldRaceHelp)) race.Should().NotContain(english);
        foreach (var english in HelpEntryTexts(OldClassHelp)) cls.Should().NotContain(english);
        race.Should().Contain(L("hu", "creation.help.race_intro")).And.Contain(L("hu", "race.half_elf")).And.Contain(L("hu", "creation.help.race.troll").Split(' ')[0]);
        cls.Should().Contain($"=== {L("hu", "creation.help.class_group.melee")} ===").And.Contain(L("hu", "class.mystic_shaman"))
            .And.NotContain("MELEE FIGHTERS").And.NotContain("Mystic Shaman");
    }

    // ---------- 2. character creation ----------

    private static Character CreationHero(string name) => new Character
    {
        Name1 = name, Name2 = name, Race = CharacterRace.HalfElf, Class = CharacterClass.Paladin, Sex = CharacterSex.Female,
        Age = 20, Level = 1, HP = 20, MaxHP = 20, Eyes = 4, Hair = 7, Skin = 10, Height = 180, Weight = 80, Gold = 100,
        AI = CharacterAI.Human,
    };

    [Theory]
    [InlineData("en")] [InlineData("es")] [InlineData("fr")] [InlineData("hu")] [InlineData("it")]
    public async Task TheCreationSummary_ShowsRaceAndAppearance_InTheReadersLanguage_AndFits(string lang)
    {
        var s = NewScreen();
        var creation = new CharacterCreationSystem(s.Term);
        var hero = CreationHero(LongName);
        await InLanguage(lang, async () =>
        {
            await (Task)typeof(CharacterCreationSystem).GetMethod("ShowCharacterSummary", F)!.Invoke(creation, new object[] { hero })!;
            return 0;
        });
        string text = s.Text;
        Capture($"creation-summary-{lang}.txt", text);
        text.Should().Contain(L(lang, "race.half_elf")).And.Contain(L(lang, "appearance.eye.4")).And.Contain(L(lang, "appearance.hair.7"))
            .And.Contain(L(lang, "appearance.skin.10"));
        if (lang != "en")
            text.Should().NotContain("Half-Elf").And.NotContain("Hazel").And.NotContain("Auburn").And.NotContain("Very Fair");
        EveryRowFits(Rows(text), $"creation summary ({lang})");
    }

    private static async Task<string> ClassSelection(string lang, CharacterRace race, params string[] input)
    {
        var s = NewScreen(input);
        var creation = new CharacterCreationSystem(s.Term);
        await InLanguage(lang, async () =>
        {
            try
            {
                await (Task<CharacterClass>)typeof(CharacterCreationSystem).GetMethod("SelectClass", F)!.Invoke(creation, new object[] { race })!;
            }
            catch (OperationCanceledException) { }
            return 0;
        });
        return s.Text;
    }

    [Fact]
    public async Task TheClassSelection_ForAHobbit_NamesTheRaceClassAndReason_InHungarian()
    {
        // 10 is the Barbarian, which a Hobbit cannot be; A aborts and Y confirms.
        string hu = await ClassSelection("hu", CharacterRace.Hobbit, "10", "A", "Y");
        Capture("class-select-hu.txt", hu);
        hu.Should().Contain(L("hu", "creation.choose_class", L("hu", "race.hobbit")));
        hu.Should().Contain(L("hu", "creation.race_restricted", L("hu", "race.hobbit"), L("hu", "class.barbarian")));
        hu.Should().Contain(L("hu", "creation.race_restriction.hobbit").Substring(0, 30));
        hu.Should().Contain(L("hu", "class.tidesworn"), "the prestige rows show the class name, not the enum");
        hu.Should().NotContain("Hobbits are too small").And.NotContain("Tidesworn ").And.NotContain("cannot be");
        string en = await ClassSelection("en", CharacterRace.Hobbit, "10", "A", "Y");
        en.Should().Contain("Sorry, Hobbit cannot be a Barbarian!").And.Contain("Hobbits are too small for the berserker's");
    }

    [Theory]
    [InlineData("en")] [InlineData("es")] [InlineData("fr")] [InlineData("hu")] [InlineData("it")]
    public void TheRaceDescription_FitsTheConfirmPrompt(string lang)
    {
        foreach (CharacterRace race in Enum.GetValues(typeof(CharacterRace)))
        {
            InLang(lang, () => $" {Loc.Get("creation.preview.be_race", GameConfig.GetLocalizedRaceDescription(race))}").Length
                .Should().BeLessOrEqualTo(77, "the prompt sits inside the 79 column card");
            InLang(lang, () => $"  {Loc.Get("creation.preview.be_race_yn", GameConfig.GetLocalizedRaceDescription(race))}").Length
                .Should().BeLessOrEqualTo(MaxWidth);
            InLang(lang, () => Loc.Get("creation.you_are_now", GameConfig.GetLocalizedRaceDescription(race))).Length.Should().BeLessOrEqualTo(MaxWidth);
        }
    }

    /// <summary>A creation screen rendered in a language: a race or class preview (card, portrait or screen reader).</summary>
    private static async Task<string> CreationScreen(string lang, string method, object arg, bool art, bool screenReader)
    {
        var s = NewScreen("N", "N", "N");
        var creation = new CharacterCreationSystem(s.Term);
        bool artWas = GameConfig.DisableCharacterMonsterArt;
        await InLanguage(lang, async () =>
        {
            GameConfig.ScreenReaderMode = screenReader;
            GameConfig.DisableCharacterMonsterArt = !art;
            try
            {
                var m = typeof(CharacterCreationSystem).GetMethod(method, F)!;
                object?[] args = m.GetParameters().Length == 1 ? new[] { arg } : new object?[] { arg, "Zz", CharacterSex.Male };
                if (m.GetParameters().Length == 2) args = new[] { arg, (object)CharacterRace.Human };
                await (Task<bool>)m.Invoke(creation, args)!;
            }
            finally { GameConfig.DisableCharacterMonsterArt = artWas; }
            return 0;
        });
        return s.Text;
    }

    [Theory]
    [InlineData("en")] [InlineData("es")] [InlineData("fr")] [InlineData("hu")] [InlineData("it")]
    public async Task EveryRaceAndClassPreview_FitsIn79Columns(string lang)
    {
        var bad = new List<string>();
        foreach (CharacterRace race in Enum.GetValues(typeof(CharacterRace)))
            foreach (var (art, sr) in new[] { (true, false), (false, false), (false, true) })
            {
                string text = await CreationScreen(lang, "ShowRacePreview", race, art, sr);
                bad.AddRange(Rows(text).Where(r => r.Length > MaxWidth).Select(r => $"{race} art={art} sr={sr}: {r.Length} {r}"));
            }
        foreach (CharacterClass cls in Enum.GetValues(typeof(CharacterClass)))
            foreach (var (art, sr) in new[] { (true, false), (false, false), (false, true) })
            {
                string text = await CreationScreen(lang, "ShowClassPreview", cls, art, sr);
                bad.AddRange(Rows(text).Where(r => r.Length > MaxWidth).Select(r => $"{cls} art={art} sr={sr}: {r.Length} {r}"));
            }
        bad.Should().BeEmpty($"every {lang} race and class preview row fits");
    }

    [Fact]
    public async Task TheClassConfirmPrompt_HasNoEnglishArticle_OutsideEnglish()
    {
        foreach (var (art, sr) in new[] { (false, false), (false, true) })
        {
            string en = await CreationScreen("en", "ShowClassPreview", CharacterClass.Assassin, art, sr);
            en.Should().Contain("Be an Assassin?", "English keeps its article");
            foreach (var lang in OtherLanguages)
            {
                string text = await CreationScreen(lang, "ShowClassPreview", CharacterClass.Assassin, art, sr);
                string name = L(lang, "class.assassin");
                text.Should().Contain(L(lang, sr ? "creation.preview.be_class_yn" : "creation.preview.be_class", name).Trim().Split(' ')[0] + " " + name);
                text.Should().NotContain(" an " + name).And.NotContain(" a " + name);
            }
        }
        L("hu", "creation.preview.be_class_yn", "x").Should().NotContain("Yes");
    }

    [Theory]
    [InlineData("en")] [InlineData("es")] [InlineData("fr")] [InlineData("hu")] [InlineData("it")]
    public async Task TheClassSelection_FitsIn79Columns_ForEveryRace(string lang)
    {
        foreach (CharacterRace race in Enum.GetValues(typeof(CharacterRace)))
        {
            var restricted = GameConfig.InvalidCombinations.TryGetValue(race, out var r) ? r : Array.Empty<CharacterClass>();
            // pick a class the race cannot be (by its menu number) so the refusal and the reason are shown too
            var menu = new[] { CharacterClass.Warrior, CharacterClass.Paladin, CharacterClass.Ranger, CharacterClass.Assassin, CharacterClass.Bard,
                CharacterClass.Jester, CharacterClass.Alchemist, CharacterClass.Magician, CharacterClass.Cleric, CharacterClass.Sage,
                CharacterClass.Barbarian, CharacterClass.MysticShaman };
            int pick = Array.FindIndex(menu, c => restricted.Contains(c));
            string text = await ClassSelection(lang, race, pick >= 0 ? pick.ToString() : "?", "A", "Y");
            EveryRowFits(Rows(text), $"class selection for {race} ({lang})");
        }
    }

    [Theory]
    [InlineData("en")] [InlineData("es")] [InlineData("fr")] [InlineData("hu")] [InlineData("it")]
    public async Task TheUnlockedPrestigeRows_ShowTheirDescriptions_AndFit(string lang)
    {
        var story = StoryProgressionSystem.Instance;
        int cycleWas = story.CurrentCycle;
        bool had = story.CompletedEndings.Contains(EndingType.TrueEnding);
        try
        {
            story.CompletedEndings.Add(EndingType.TrueEnding);
            typeof(StoryProgressionSystem).GetProperty("CurrentCycle")!.SetValue(story, 2);
            string text = await ClassSelection(lang, CharacterRace.Human, "A", "Y");
            Capture($"prestige-{lang}.txt", text);
            foreach (var cls in GameConfig.PrestigeClassDescriptions.Keys)
            {
                text.Should().Contain(L(lang, "class." + GameConfig.ClassKeyPart(cls)));
                string first = L(lang, "creation.prestige_desc." + cls.ToString().ToLowerInvariant()).Split(' ')[0];
                text.Should().Contain(first);
            }
            if (lang != "en") text.Should().NotContain("The Ocean's divine shield");
            EveryRowFits(Rows(text), $"prestige rows ({lang})");
        }
        finally
        {
            if (!had) story.CompletedEndings.Remove(EndingType.TrueEnding);
            typeof(StoryProgressionSystem).GetProperty("CurrentCycle")!.SetValue(story, cycleWas);
        }
    }

    [Theory]
    [InlineData("en")] [InlineData("es")] [InlineData("fr")] [InlineData("hu")] [InlineData("it")]
    public void TheHelpScreens_FitIn79Columns(string lang)
    {
        EveryRowFits(InLang(lang, CharacterCreationSystem.RaceHelpRows), $"race help ({lang})");
        EveryRowFits(InLang(lang, CharacterCreationSystem.ClassHelpRows), $"class help ({lang})");
    }

    [Theory]
    [InlineData("en")] [InlineData("es")] [InlineData("fr")] [InlineData("hu")] [InlineData("it")]
    public async Task TheMaterialsScreen_FitsIn79Columns(string lang)
    {
        var s = NewScreen();
        var hero = new Character { Name1 = "zzmats", Name2 = "ZzMats", Level = 50, HP = 300, MaxHP = 300, AI = CharacterAI.Human };
        foreach (var m in GameConfig.CraftingMaterials) hero.AddMaterial(m.Id, 999);
        var street = new MainStreetLocation();
        typeof(BaseLocation).GetField("terminal", F)!.SetValue(street, s.Term);
        typeof(BaseLocation).GetField("currentPlayer", F)!.SetValue(street, hero);
        await InLanguage(lang, async () => { await (Task)typeof(BaseLocation).GetMethod("ShowMaterials", F)!.Invoke(street, null)!; return 0; });
        string text = s.Text;
        Capture($"materials-{lang}.txt", text);
        foreach (var m in GameConfig.CraftingMaterials)
            text.Should().Contain(L(lang, $"material.{m.Id}.name"));
        EveryRowFits(Rows(text), $"materials screen ({lang})");
    }

    [Theory]
    [InlineData("en")] [InlineData("es")] [InlineData("fr")] [InlineData("hu")] [InlineData("it")]
    public async Task TheInnTrainingScreen_WithMaterials_FitsIn79Columns(string lang)
    {
        var s = NewScreen("");
        var hero = new Character { Name1 = "zztrain", Name2 = "ZzTrain", Level = 100, HP = 900, MaxHP = 900, AI = CharacterAI.Human,
            Strength = 99999, Dexterity = 99999, Gold = 1_000_000_000 };
        hero.StatTrainingCounts["STR"] = 3;
        hero.StatTrainingCounts["DEX"] = 4;
        var inn = new InnLocation();
        typeof(BaseLocation).GetField("terminal", F)!.SetValue(inn, s.Term);
        typeof(BaseLocation).GetField("currentPlayer", F)!.SetValue(inn, hero);
        await InLanguage(lang, async () => { await (Task)typeof(InnLocation).GetMethod("HandleStatTraining", F)!.Invoke(inn, null)!; return 0; });
        string text = s.Text;
        Capture($"inn-training-{lang}.txt", text);
        text.Should().Contain(L(lang, "material.eye_of_manwe.name")).And.Contain(L(lang, "material.heart_of_the_ocean.name"));
        // the stat rows and the material rows (the trainer's own lines are another piece's keys)
        var tableRows = Rows(text).Where(r => Regex.IsMatch(r, @"^\d ") || r.Contains(L(lang, "material.heart_of_the_ocean.name"))).ToList();
        tableRows.Should().HaveCountGreaterOrEqualTo(8);
        EveryRowFits(tableRows, $"inn training ({lang})");
    }

    [Fact]
    public void AppearanceRaceAndMaterials_AreSavedAsNumbersAndIds_InEveryLanguage()
    {
        foreach (var lang in AllLanguages)
        {
            var hero = CreationHero("ZzAppear" + lang);
            hero.AddMaterial("heart_of_the_ocean", 2);
            var data = InLang(lang, () => (PlayerData)typeof(SaveSystem).GetMethod("SerializePlayer", F)!.Invoke(SaveSystem.Instance, new object[] { hero })!);
            var json = JsonSerializer.Serialize(data);
            var back = JsonSerializer.Deserialize<PlayerData>(json)!;
            (back.Eyes, back.Hair, back.Skin).Should().Be((4, 7, 10), "the character stores the colour numbers");
            back.Race.Should().Be(CharacterRace.HalfElf);
            back.CraftingMaterials.Should().ContainKey("heart_of_the_ocean").WhoseValue.Should().Be(2);
            if (lang != "en")
                json.Should().NotContain(L(lang, "appearance.hair.7")).And.NotContain(L(lang, "material.heart_of_the_ocean.name"));
            InLang(lang, () => GameConfig.GetLocalizedHairColor(back.Hair)).Should().Be(L(lang, "appearance.hair.7"));
        }
    }

    // ---------- 3. crafting materials ----------

    [Theory]
    [InlineData("en")] [InlineData("es")] [InlineData("fr")] [InlineData("hu")] [InlineData("it")]
    public void Materials_ShowInTheReadersLanguage_AndTheirRowsFit(string lang)
    {
        string boss = InLang(lang, () => L(lang, "oldgod.terravok.name"));
        foreach (var m in GameConfig.CraftingMaterials)
        {
            InLang(lang, () => m.LocName).Should().Be(L(lang, $"material.{m.Id}.name"));
            InLang(lang, () => m.LocDescription).Should().Be(L(lang, $"material.{m.Id}.desc"));
            foreach (var indent in new[] { "", "  ", "    " })
            {
                var rows = InLang(lang, () => m.QuotedDescriptionRows(indent));
                EveryRowFits(rows, $"{m.Id} description ({lang})");
                string.Join(" ", rows.Select(r => r.Trim())).Should().Be("\"" + L(lang, $"material.{m.Id}.desc") + "\"", "only line breaks are added");
            }
            InLang(lang, () => $"    {m.LocName} x99").Length.Should().BeLessOrEqualTo(MaxWidth);
            InLang(lang, () => Loc.Get("dungeon.chest_discover_material", m.LocName)).Length.Should().BeLessOrEqualTo(MaxWidth);
            // the Old God drop lines are written wrapped (OldGodBossSystem, UIHelper.WriteWrapped)
            UIHelper.WordWrap(InLang(lang, () => Loc.Get("old_god.material_left_behind", boss, m.LocName, 2))).Should().OnlyContain(r => r.Length <= MaxWidth);
            if (lang != "en") InLang(lang, () => m.LocName).Should().NotBe(m.Name);
        }
        GameConfig.CraftingMaterials.Select(m => m.Name).Should().Contain("Heart of the Ocean", "the English table is unchanged");
    }

    [Fact]
    public void TheMaterialReaders_ShowTheKeyedNames()
    {
        string Src(params string[] p) => File.ReadAllText(Path.Combine(new[] { UsurperReborn.Tests.Localization.HardcodedTextScannerTests.RepoRoot(), "Scripts" }.Concat(p).ToArray()));
        foreach (var src in new[] { Src("Locations", "InnLocation.cs"), Src("Locations", "MagicShopLocation.cs"), Src("Locations", "BaseLocation.cs"),
                     Src("Locations", "DungeonLocation.cs"), Src("Systems", "OldGodBossSystem.cs") })
            Regex.IsMatch(src, @"\b(mat\??|matDef|material|thematicMaterial)\.(Name|Description)\b").Should().BeFalse("materials are shown through LocName and LocDescription");
        Regex.Matches(Src("Systems", "OldGodBossSystem.cs"), @"WriteWrapped\(terminal, Loc\.Get\(""old_god\.(material_left_behind|defeated_crystallizes)""").Count
            .Should().Be(2, "the drop lines wrap: in French a god's name and the material can pass 79 columns");
    }

    // ---------- 4. message of the day ----------

    [Fact]
    public void TheMessageOfTheDay_IsStoredInEnglish_AndShownInTheReadersLanguage()
    {
        var was = GameConfig.MessageOfTheDay;
        try
        {
            GameConfig.MessageOfTheDay = GameConfig.DefaultMessageOfTheDay;
            var motd = ServerSettingsRegistry.Get("motd")!;
            foreach (var lang in AllLanguages)
            {
                InLang(lang, () => GameConfig.MessageOfTheDayText()).Should().Be(L(lang, "motd.default"));
                InLang(lang, () => motd.CurrentValue()).Should().Be(GameConfig.DefaultMessageOfTheDay, "the server settings store the English default");
                ($"  {L(lang, "motd.default")}").Length.Should().BeLessOrEqualTo(MaxWidth, $"the {lang} message of the day fits");
            }
            motd.DefaultValue.Should().Be(GameConfig.DefaultMessageOfTheDay);
            new SysOpConfig().MessageOfTheDay.Should().Be(GameConfig.DefaultMessageOfTheDay, "the sysop config default is the same English");
            GameConfig.MessageOfTheDay = "Server restart at noon.";
            InLang("hu", () => GameConfig.MessageOfTheDayText()).Should().Be("Server restart at noon.", "a message the sysop set is shown as set");
        }
        finally { GameConfig.MessageOfTheDay = was; }
    }

    // ---------- 5. god rank titles ----------

    [Fact]
    public void TheGodRankLine_ShowsTheTitleInTheGodsLanguage()
    {
        for (int level = 1; level <= GameConfig.GodTitles.Length; level++)
        {
            CombatEngine.GodRankTitleLine("en", level).Should().Be(CombatEngine.GodRankLine("en", GameConfig.GodTitles[level - 1]), "English is as before");
            string hu = CombatEngine.GodRankTitleLine("hu", level);
            hu.Should().Contain(L("hu", "god.title." + level));
            if (L("hu", "god.title." + level) != GameConfig.GodTitles[level - 1])
                hu.Should().NotContain(GameConfig.GodTitles[level - 1]);
        }
    }

    // ---------- 6. NPCs ----------

    [Theory]
    [InlineData("en")] [InlineData("es")] [InlineData("fr")] [InlineData("hu")] [InlineData("it")]
    public void TheNpcInfoLine_IsInTheReadersLanguage_AndFits(string lang)
    {
        var npc = new NPC(LongName, "commoner", CharacterClass.Warrior, 100) { IsHostile = true };
        string line = InLang(lang, () => npc.GetDisplayInfo());
        line.Should().Be(L(lang, "npc.display_info_hostile", npc.IsNPC ? GameConfig.NpcMark : "", npc.DisplayName, "commoner", 100));
        line.Length.Should().BeLessOrEqualTo(MaxWidth);
        if (lang != "en") line.Should().NotContain("Level");
        npc.IsHostile = false;
        InLang(lang, () => npc.GetDisplayInfo()).Should().Be(L(lang, "npc.display_info", npc.IsNPC ? GameConfig.NpcMark : "", npc.DisplayName, "commoner", 100));
    }

    [Fact]
    public void TheSpecialGreetings_AreInTheReadersLanguage()
    {
        var hero = new Character { Name1 = "zzgreet", Name2 = "ZzGreet", Level = 5, HP = 30, MaxHP = 30, AI = CharacterAI.Human };
        var seth = new NPC("Seth Able", "drunk", CharacterClass.Warrior, 10) { IsSpecialNPC = true, SpecialScript = "drunk_fighter" };
        var huSeth = Enumerable.Range(1, NPC.SethGreetingCount).Select(i => L("hu", "npc.seth_greeting." + i)).ToHashSet();
        for (int i = 0; i < 20; i++) huSeth.Should().Contain(InLang("hu", () => seth.GetGreeting(hero)));
        var guard = new NPC("Gate Guard", "guard", CharacterClass.Warrior, 10) { IsSpecialNPC = true, SpecialScript = "castle_guard" };
        InLang("hu", () => guard.GetGreeting(hero)).Should().Be(L("hu", "npc.special_greeting.castle_guard"));
        InLang("en", () => guard.GetGreeting(hero)).Should().Be("Halt! State your business in the castle!");
        foreach (var lang in AllLanguages)
            foreach (var key in EnglishKeys("npc.seth_greeting.", "npc.special_greeting."))
                NPCDialogueGenerator.QuotedRows(L(lang, key)).Should().OnlyContain(r => r.Length <= MaxWidth);
    }

    // ---------- 7. Level Master ----------

    [Theory]
    [InlineData("en")] [InlineData("es")] [InlineData("fr")] [InlineData("hu")] [InlineData("it")]
    public void TheLevelMaster_DescriptionAndCrystalNavigation_AreInTheReadersLanguage_AndFit(string lang)
    {
        foreach (var field in new[] { "GoodMaster", "NeutralMaster", "EvilMaster" })
        {
            var master = (MasterInfo)typeof(LevelMasterLocation).GetField(field, SNP)!.GetValue(null)!;
            string desc = InLang(lang, () => master.LocDescription);
            desc.Should().Be(L(lang, "level_master.desc_" + master.Alignment.ToString().ToLowerInvariant()));
            if (lang != "en") desc.Should().NotBe(master.Description);
            UIHelper.WordWrap(desc).Should().OnlyContain(r => r.Length <= MaxWidth);
        }
        L(lang, "level_master.crystal_nav_prev").Should().StartWith("[P]", "the crystal ball reads P in every language");
        L(lang, "level_master.crystal_nav_next").Should().StartWith("[N]", "the crystal ball reads N in every language");
        string nav = L(lang, "level_master.crystal_nav", L(lang, "level_master.crystal_nav_prev") + "  " + L(lang, "level_master.crystal_nav_next") + "  ");
        nav.Length.Should().BeLessOrEqualTo(MaxWidth);
    }

    // ---------- 8. save files, repair, update ----------

    private FileSaveBackend FileBackend()
    {
        var backend = new FileSaveBackend();
        typeof(FileSaveBackend).GetField("baseSaveDirectory", F)!.SetValue(backend, _dir);
        return backend;
    }

    [Fact]
    public async Task TheLoadErrors_AreInTheReadersLanguage()
    {
        var backend = FileBackend();
        var (data, error, tooLarge) = await InLanguage("hu", () => backend.ReadGameDataByFileNameWithError("nobody.json"));
        data.Should().BeNull();
        tooLarge.Should().BeFalse();
        error.Should().Be(L("hu", "save.load_error_not_found", Path.Combine(_dir, "nobody.json")));
        File.WriteAllText(Path.Combine(_dir, "broken.json"), "{ \"player\": ");
        (data, error, tooLarge) = await InLanguage("hu", () => backend.ReadGameDataByFileNameWithError("broken.json"));
        error.Should().StartWith(L("hu", "save.load_error_json", 0, 0, "", "").Substring(0, 20));
        error.Should().NotContain("malformed JSON");
        tooLarge.Should().BeFalse();
        (_, error, _) = await InLanguage("en", () => backend.ReadGameDataByFileNameWithError("nobody.json"));
        error.Should().Be($"Save file not found on disk: {Path.Combine(_dir, "nobody.json")}", "English is as before");
    }

    [Fact]
    public void TheSaveTypes_StayEnglishInTheListing_AndAreShownInTheReadersLanguage()
    {
        var backend = FileBackend();
        string save = "{ \"player\": { \"name2\": \"ZzRest\", \"level\": 3 }, \"version\": " + GameConfig.MinSaveVersion + " }";
        File.WriteAllText(Path.Combine(_dir, "zzrest.json"), save);
        File.WriteAllText(Path.Combine(_dir, "zzrest_autosave_20260101.json"), save);
        File.WriteAllText(Path.Combine(_dir, "zzrest_backup.json"), "{ not json");
        var saves = InLang("hu", () => backend.GetPlayerSaves("zzrest"));
        saves.Select(x => x.FileName).Should().BeEquivalentTo(new[] { "zzrest.json", "zzrest_autosave_20260101.json", "zzrest_backup.json" },
            "the save file names are not localized");
        saves.Select(x => x.SaveType).Should().BeEquivalentTo(new[] { "Manual Save", "Autosave", "Backup" }, "the slot labels stay English: SaveTypeLabel matches them");
        InLang("hu", () => GameEngine.SaveTypeLabel("Autosave")).Should().Be(L("hu", "engine.save_type_autosave"));
        InLang("hu", () => GameEngine.SaveTypeLabel("Manual Save")).Should().Be(L("hu", "engine.save_type_manual"));
    }

    [Fact]
    public void TheRepairErrors_AreInTheReadersLanguage()
    {
        string missing = Path.Combine(_dir, "missing.json");
        var result = InLang("hu", () => SaveFileRepair.RepairInPlace(missing, new JsonSerializerOptions()));
        result.Success.Should().BeFalse();
        result.ErrorMessage.Should().Be(L("hu", "save_repair.not_found", missing));
        InLang("en", () => SaveFileRepair.RepairInPlace(missing, new JsonSerializerOptions())).ErrorMessage.Should().Be($"Save file not found: {missing}");
        foreach (var lang in AllLanguages)
            UIHelper.WordWrap(L(lang, "engine.rp_failed", L(lang, "save_repair.too_large", 2048))).Should().OnlyContain(r => r.Length <= MaxWidth);
    }

    [Fact]
    public async Task TheUpdateDownloadError_IsInTheReadersLanguage()
    {
        var checker = new VersionChecker();
        typeof(VersionChecker).GetField("releaseAssets", F)!.SetValue(checker, null);
        (await InLanguage("hu", () => checker.DownloadAndInstallUpdateAsync())).Should().BeFalse();
        checker.DownloadError.Should().Be(L("hu", "version.download_no_package"));
        foreach (var lang in AllLanguages)
            foreach (var key in new[] { "version.download_no_package", "version.download_no_updater" })
                UIHelper.WordWrap(L(lang, key), MaxWidth - 2).Should().OnlyContain(r => r.Length + 2 <= MaxWidth);
    }
}
