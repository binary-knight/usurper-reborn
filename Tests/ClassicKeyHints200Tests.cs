using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using FluentAssertions;
using UsurperRemake.Systems;
using Xunit;
using P = MainStreetLocation.StreetPlace;
using G = MainStreetLocation.StreetGroup;

namespace UsurperReborn.Tests;

/// <summary>
/// 2.0: text that names Main Street keys follows the player's layout. Each key path text (Guild Row [G], Level Master
/// [V] and the like) has a ".classic" variant with the classic keys, picked by MainStreetLocation.StreetTextKey.
/// The tables below say which places each text names; every bracketed key must resolve to that place in the layout
/// the text is for, in all five languages.
/// </summary>
[Collection("SharedGameSingletons")]
public class ClassicKeyHints200Tests
{
    private const BindingFlags F = BindingFlags.NonPublic | BindingFlags.Instance;
    private static readonly string[] Langs = { "en", "es", "fr", "hu", "it" };

    /// <summary>
    /// One thing a text names: a place, a district by itself, or a key that is not a Main Street key. NameOnly: the
    /// text names the place (and its district) in words with no bracketed key, so it must carry no key for it.
    /// </summary>
    internal sealed record Step(P? Place = null, G? District = null, string? Inside = null, bool NameOnly = false);
    private static Step At(P p) => new(Place: p);
    private static Step Named(P p) => new(Place: p, NameOnly: true); // named in words, no key
    private static Step Row(G g) => new(District: g);
    private static Step In(string key) => new(Inside: key); // a key inside a place or in combat, not on Main Street

    /// <summary>The district texts: the places they name, each reached by its district key path.</summary>
    internal static readonly (string Key, Step[] Steps)[] DistrictTexts =
    {
        ("church.marriage_step1", new[] { At(P.LoveStreet) }),
        ("church.marriage_step3", new[] { At(P.MagicShop) }),
        ("journal.next_heal", new[] { At(P.Healer) }),
        ("journal.next_level_up", new[] { At(P.LevelMaster) }),
        ("journal.next_turn_in", new[] { At(P.QuestHall) }),
        ("journal.next_merc_turn_in", new[] { At(P.Challenges), In("M") }),
        ("journal.next_training", new[] { At(P.LevelMaster) }),
        ("journal.line_training_points", new[] { At(P.LevelMaster) }),
        ("journal.line_level_ready", new[] { At(P.LevelMaster) }),
        ("journal.line_free_blessing", new[] { At(P.Temple) }),
        ("base.training_points_hint", new[] { At(P.LevelMaster) }),
        ("base.train_how", new[] { At(P.LevelMaster) }),
        ("base.stat_awakening_hint", new[] { At(P.Progress) }),
        ("hint.awakening.msg", new[] { At(P.Progress) }),
        ("hint.low_hp.msg", new[] { At(P.Healer), In("H") }),
        ("hint.first_level_up.msg", new[] { At(P.LevelMaster) }),
        ("hint.level_master.msg", new[] { At(P.LevelMaster) }),
        ("hint.mana_spells.msg", new[] { At(P.MagicShop) }),
        ("hint.quest_system.msg", new[] { At(P.QuestHall) }),
        ("hint.getting_started.msg", new[] { Row(G.MerchantRow), At(P.Dungeons), Row(G.GuildRow) }),
        ("castle.visit_quest_hall", new[] { Named(P.QuestHall) }),
    };

    /// <summary>The classic variants: the places they name, each by its key on the classic Main Street.</summary>
    internal static readonly (string Key, Step[] Steps)[] ClassicTexts =
    {
        ("church.marriage_step1.classic", new[] { At(P.LoveStreet) }),
        ("church.marriage_step3.classic", new[] { At(P.MagicShop) }),
        ("journal.next_heal.classic", new[] { At(P.Healer) }),
        ("journal.next_level_up.classic", new[] { At(P.LevelMaster) }),
        ("journal.next_turn_in.classic", new[] { At(P.QuestHall) }),
        ("journal.next_merc_turn_in.classic", new[] { At(P.Challenges), In("M") }),
        ("journal.next_training.classic", new[] { At(P.LevelMaster) }),
        ("journal.line_training_points.classic", new[] { At(P.LevelMaster) }),
        ("journal.line_level_ready.classic", new[] { At(P.LevelMaster) }),
        ("journal.line_free_blessing.classic", new[] { At(P.Temple) }),
        ("base.training_points_hint.classic", new[] { At(P.LevelMaster) }),
        ("base.train_how.classic", new[] { At(P.LevelMaster) }),
        ("base.stat_awakening_hint.classic", new[] { At(P.Progress) }),
        ("hint.awakening.msg.classic", new[] { At(P.Progress) }),
        ("hint.low_hp.msg.classic", new[] { At(P.Healer), In("H") }),
        ("hint.first_level_up.msg.classic", new[] { At(P.LevelMaster) }),
        ("hint.level_master.msg.classic", new[] { At(P.LevelMaster) }),
        ("hint.mana_spells.msg.classic", new[] { At(P.MagicShop) }),
        ("hint.quest_system.msg.classic", new[] { At(P.QuestHall) }),
        ("hint.getting_started.msg.classic", new[] { At(P.WeaponShop), At(P.ArmorShop), At(P.Dungeons), At(P.QuestHall), At(P.LevelMaster) }),
        ("castle.visit_quest_hall.classic", new[] { At(P.QuestHall) }),
    };

    /// <summary>Texts whose keys are the same on both layouts, so they need no variant.</summary>
    internal static readonly (string Key, Step[] Steps)[] BothLayoutTexts =
    {
        ("journal.next_first_fight", new[] { At(P.Dungeons) }),
        ("journal.next_god", new[] { At(P.Dungeons) }),
        ("journal.next_seal", new[] { At(P.Dungeons) }),
        ("journal.next_delve", new[] { At(P.Dungeons) }),
        ("journal.next_delve_resume", new[] { At(P.Dungeons) }),
    };

    private static Dictionary<string, string> Table(string lang) =>
        JsonSerializer.Deserialize<Dictionary<string, JsonElement>>(File.ReadAllText(Path.Combine(MainStreetDistricts1113Tests.RepoRoot(), $"Localization/{lang}.json")))!
            .Where(kv => kv.Value.ValueKind == JsonValueKind.String).ToDictionary(kv => kv.Key, kv => kv.Value.GetString()!);

    internal static List<string> BracketKeys(string text) =>
        Regex.Matches(text, @"\[([^\]\s]{1,2})\]").Select(m => m.Groups[1].Value).ToList();

    /// <summary>The key a classic player presses for a place: from the classic menu rows, the one key that row draws.</summary>
    internal static string ClassicKey(P place)
    {
        var keys = MainStreetLocation.ClassicVisualRows.SelectMany(r => r).Where(i => i.Place == place).Select(i => i.Key).Distinct().ToList();
        keys.Should().ContainSingle($"the classic rows draw one key for {place}");
        return keys[0];
    }

    /// <summary>The keys a district player presses for a place: its district key, then its key there (none for Main Street itself).</summary>
    internal static IEnumerable<string> DistrictPath(P place)
    {
        var e = MainStreetLocation.StreetEntries.Single(x => x.Place == place);
        if (e.Group != G.MainStreet) yield return MainStreetLocation.Districts.Single(d => d.Group == e.Group).Key;
        yield return e.Key;
    }

    internal static List<string> Expected(Step[] steps, bool classic)
    {
        var keys = new List<string>();
        foreach (var s in steps)
        {
            if (s.NameOnly) continue; // named in words only: no key may stand for it
            if (s.Inside != null) keys.Add(s.Inside);
            else if (s.District != null)
            {
                classic.Should().BeFalse("the classic Main Street has no districts to name");
                keys.Add(MainStreetLocation.Districts.Single(d => d.Group == s.District).Key);
            }
            else if (classic) keys.Add(ClassicKey(s.Place!.Value));
            else keys.AddRange(DistrictPath(s.Place!.Value));
        }
        return keys;
    }

    private static string En(string labelKey) => Loc.GetIn("en", labelKey);

    /// <summary>What the English sentence must name for each step: the place's label and, on the districts, its district.</summary>
    private static IEnumerable<string> EnglishNames(Step[] steps, bool classic)
    {
        foreach (var s in steps)
        {
            if (s.District != null) yield return En(MainStreetLocation.Districts.Single(d => d.Group == s.District).NameKey);
            if (s.Place == null) continue;
            var e = MainStreetLocation.StreetEntries.Single(x => x.Place == s.Place);
            yield return En(e.LabelKey);
            if (!classic && e.Group != G.MainStreet) yield return En(MainStreetLocation.Districts.Single(d => d.Group == e.Group).NameKey);
        }
    }

    internal static List<string> Problems((string Key, Step[] Steps)[] texts, bool classic, Func<string, Dictionary<string, string>> table)
    {
        var problems = new List<string>();
        foreach (string lang in Langs)
        {
            var t = table(lang);
            foreach (var (key, steps) in texts)
            {
                if (!t.TryGetValue(key, out string? text)) { problems.Add($"{lang} {key}: missing"); continue; }
                var want = Expected(steps, classic);
                var got = BracketKeys(text);
                if (!got.SequenceEqual(want)) problems.Add($"{lang} {key}: keys [{string.Join(",", got)}], the {(classic ? "classic" : "district")} path is [{string.Join(",", want)}]");
                if (lang == "en")
                    foreach (string name in EnglishNames(steps, classic))
                        if (!text.Contains(name)) problems.Add($"en {key}: does not name {name}");
            }
        }
        return problems;
    }

    [Fact]
    public void EveryClassicText_PointsAtTheClassicKeys_ForThePlacesItNames()
    {
        Problems(ClassicTexts, classic: true, Table).Should().BeEmpty();
    }

    [Fact]
    public void EveryDistrictText_PointsAtTheDistrictKeyPath_ForThePlacesItNames()
    {
        Problems(DistrictTexts, classic: false, Table).Should().BeEmpty();
    }

    [Fact]
    public void TextsWithoutAVariant_AreRightOnBothLayouts()
    {
        Problems(BothLayoutTexts, classic: true, Table).Should().BeEmpty();
        Problems(BothLayoutTexts, classic: false, Table).Should().BeEmpty();
        BothLayoutTexts.Select(x => x.Key).Should().NotIntersectWith(MainStreetLocation.ClassicTextKeys);
    }

    [Fact]
    public void TheTables_TheHelperSet_AndTheLanguageFiles_Agree()
    {
        var set = MainStreetLocation.ClassicTextKeys;
        DistrictTexts.Select(x => x.Key).Should().BeEquivalentTo(set);
        ClassicTexts.Select(x => x.Key).Should().BeEquivalentTo(set.Select(k => k + ".classic"));
        foreach (string lang in Langs)
        {
            var t = Table(lang);
            t.Keys.Where(k => k.EndsWith(".classic")).Should().BeEquivalentTo(set.Select(k => k + ".classic"), lang);
        }
    }

    [Fact]
    public void TheNegatives_EachHalfCatchesAWrongKey()
    {
        Dictionary<string, string> Broken(string lang, string key, string from, string to)
        {
            var t = Table(lang);
            t[key] = t[key].Replace(from, to);
            return t;
        }
        // a classic text pointing at the district key for the Healer
        Problems(ClassicTexts, true, l => Broken(l, "journal.next_heal.classic", "[1]", "[H]")).Should().NotBeEmpty();
        // a district text pointing at the classic key for the Healer
        Problems(DistrictTexts, false, l => Broken(l, "journal.next_heal", "[H]", "[1]")).Should().NotBeEmpty();
        // a district text that drops its district step
        Problems(DistrictTexts, false, l => Broken(l, "base.train_how", "[G]", "")).Should().NotBeEmpty();
        // the castle line's classic variant pointing at the district key for the Quest Hall
        Problems(ClassicTexts, true, l => Broken(l, "castle.visit_quest_hall.classic", "[2]", "[U]")).Should().NotBeEmpty();
        // the castle line's classic variant with its key dropped
        Problems(ClassicTexts, true, l => Broken(l, "castle.visit_quest_hall.classic", "[2]", "")).Should().NotBeEmpty();
        // the castle line's district text gaining a key it is listed without
        Problems(DistrictTexts, false, l => Broken(l, "castle.visit_quest_hall", "Quest Hall", "Quest Hall [2]")).Should().NotBeEmpty();
        // the castle line's district text no longer naming Guild Row
        Problems(DistrictTexts, false, l => Broken(l, "castle.visit_quest_hall", "Guild Row", "Merchant Row")).Should().NotBeEmpty();
    }

    // ---------- the helper, and every call site through it ----------

    [Fact]
    public void StreetTextKey_PicksTheVariant_FromTheLayoutPreference()
    {
        var classic = new Character();
        var districts = new Character { DistrictsMainStreet = true };
        MainStreetLocation.StreetTextKey("base.train_how", classic).Should().Be("base.train_how.classic");
        MainStreetLocation.StreetTextKey("base.train_how", null).Should().Be("base.train_how.classic", "no player is the classic default");
        MainStreetLocation.StreetTextKey("base.train_how", districts).Should().Be("base.train_how");
        MainStreetLocation.StreetTextKey("journal.next_delve", classic).Should().Be("journal.next_delve", "a text without a variant is unchanged");
    }

    private static string Source(string relative) => File.ReadAllText(Path.Combine(MainStreetDistricts1113Tests.RepoRoot(), relative));

    [Fact]
    public void EveryCallSite_GoesThroughTheHelper()
    {
        var root = Path.Combine(MainStreetDistricts1113Tests.RepoRoot(), "Scripts");
        var problems = new List<string>();
        var hintIds = MainStreetLocation.ClassicTextKeys.Where(k => k.StartsWith("hint.") && k.EndsWith(".msg"))
            .Select(k => k.Substring(5, k.Length - 9)).ToList();
        var hintConsts = typeof(HintSystem).GetFields(BindingFlags.Public | BindingFlags.Static)
            .Where(f => f.IsLiteral && hintIds.Contains((string)f.GetRawConstantValue()!)).Select(f => f.Name).ToList();
        hintConsts.Should().HaveCount(hintIds.Count, "every hint with a variant has a HintSystem id");
        foreach (string file in Directory.GetFiles(root, "*.cs", SearchOption.AllDirectories))
        {
            string src = File.ReadAllText(file);
            string name = Path.GetFileName(file);
            foreach (string key in MainStreetLocation.ClassicTextKeys.Where(k => !k.StartsWith("hint.")))
                if (src.Contains($"Loc.Get(\"{key}\"")) problems.Add($"{name}: Loc.Get(\"{key}\") without StreetTextKey");
            if (Regex.IsMatch(src, @"Loc\.Get\(\w+\.LocKey")) problems.Add($"{name}: a journal step read by its LocKey, not Text(player)");
            foreach (string c in hintConsts)
                foreach (Match m in Regex.Matches(src, $@"TryShowHint\(HintSystem\.{c},\s*\w+,\s*([^,)]+)"))
                    if (m.Groups[1].Value.Contains("HintsShown")) problems.Add($"{name}: {c} shown without the player");
        }
        problems.Should().BeEmpty();
        Source("Scripts/Systems/HintSystem.cs").Should().Contain("MainStreetLocation.StreetTextKey($\"hint.{hintId}.msg\", player)");
    }

    // ---------- what each player sees ----------

    private static string Collapse(string s) => Regex.Replace(s.Replace("│", " "), @"\s+", " ").Trim();

    private static string HintText(Character hero, string hintId)
    {
        var output = new MemoryStream();
        var term = new TerminalEmulator(new LineStream(Array.Empty<string>()), output);
        hero.HintsShown.Remove(hintId);
        HintSystem.Instance.TryShowHint(hintId, term, hero).Should().BeTrue();
        term.StreamWriterInternal?.Flush();
        return Collapse(Regex.Replace(Encoding.UTF8.GetString(output.ToArray()), "\u001b\\[[0-9;?]*[A-Za-z]", ""));
    }

    private static string TrainHow(Character hero)
    {
        var inn = new InnLocation();
        var output = new MemoryStream();
        var term = new TerminalEmulator(new LineStream(new[] { "", "", "" }), output);
        typeof(BaseLocation).GetField("terminal", F)!.SetValue(inn, term);
        typeof(BaseLocation).GetField("currentPlayer", F)!.SetValue(inn, hero);
        var task = (System.Threading.Tasks.Task<(bool, bool)>)typeof(BaseLocation).GetMethod("ProcessSlashCommand", F)!.Invoke(inn, new object[] { "train" })!;
        task.GetAwaiter().GetResult();
        term.StreamWriterInternal?.Flush();
        return Encoding.UTF8.GetString(output.ToArray());
    }

    private static Character LevelReady(bool districts) => new()
    {
        Name1 = "Ready", Name2 = "Ready", Level = 5, MKills = 20, HP = 100, MaxHP = 100, Healing = 3, AutoLevelUp = false,
        Experience = GameConfig.GetExperienceForLevel(6), DistrictsMainStreet = districts, TrainingPoints = 5,
    };

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void APlayer_SeesTheKeysOfTheirLayout_InGettingStarted_TrainHow_AndTheNextStep(bool districts)
    {
        var hero = LevelReady(districts);
        string suffix = districts ? "" : ".classic";
        string other = districts ? ".classic" : "";

        string started = HintText(hero, HintSystem.HINT_GETTING_STARTED);
        started.Should().Contain(Collapse(Loc.Get("hint.getting_started.msg" + suffix)))
            .And.NotContain(Collapse(Loc.Get("hint.getting_started.msg" + other)));
        if (districts) started.Should().Contain("Merchant Row [M]").And.NotContain("Quest Hall [2]");
        else started.Should().Contain("Quest Hall [2]").And.Contain("Weapon Shop [W]").And.NotContain("Guild Row [G]");

        string how = TrainHow(hero);
        how.Should().Contain(Loc.Get("base.train_how" + suffix)).And.NotContain(Loc.Get("base.train_how" + other));
        if (districts) how.Should().Contain("[G] and [V]"); else how.Should().Contain("press [V]").And.NotContain("[G]");

        var step = JournalSystem.GetNextStep(hero);
        step.LocKey.Should().Be("journal.next_level_up");
        string next = step.Text(hero);
        next.Should().Be(Loc.Get("journal.next_level_up" + suffix));
        if (districts) next.Should().Contain("Guild Row [G], Level Master [V]"); else next.Should().Contain("Level Master [V] on Main Street").And.NotContain("[G]");

        string claim = string.Join("\n", JournalSystem.BuildClaimLines(hero).Select(l => l.Text));
        claim.Should().Contain(Loc.Get("journal.line_level_ready" + suffix)).And.Contain(Loc.Get("journal.line_training_points" + suffix, 5));
    }

    // ---------- the wiki guides: the classic path first, then the district path ----------

    /// <summary>The guides that give a Main Street path, and the place each one opens.</summary>
    internal static readonly (string Guide, P Place)[] GuidePaths =
    {
        ("online/arena.md", P.Arena), ("online/world-boss.md", P.WorldBoss), ("world/castle.md", P.Castle),
        ("world/anchor-road.md", P.Challenges), ("world/sanctum.md", P.Sanctum), ("world/home.md", P.Home), ("world/story.md", P.Progress),
    };

    /// <summary>The classic key for a place: its menu row key, or its key on the online row.</summary>
    private static string ClassicGuideKey(P place) =>
        MainStreetLocation.ClassicOnlineItems.Any(i => i.Place == place)
            ? MainStreetLocation.ClassicOnlineItems.Single(i => i.Place == place).Key
            : ClassicKey(place);

    internal static List<string> GuideProblems(Func<string, string> read)
    {
        var problems = new List<string>();
        foreach (var (guide, place) in GuidePaths)
        {
            string text = read(guide);
            var e = MainStreetLocation.StreetEntries.Single(x => x.Place == place);
            var d = MainStreetLocation.Districts.Single(x => x.Group == e.Group);
            string classic = $"From Main Street press {ClassicGuideKey(place)} for ";
            string district = $"With the district layout, open the {En(d.NameKey).Replace("&", "and")} district with {d.Key}, then press {e.Key}.";
            int c = text.IndexOf(classic, StringComparison.Ordinal), k = text.IndexOf(district, StringComparison.Ordinal);
            if (c < 0) problems.Add($"{guide}: no classic path \"{classic}\"");
            if (k < 0) problems.Add($"{guide}: no district path \"{district}\"");
            if (c >= 0 && k >= 0 && c > k) problems.Add($"{guide}: the district path comes before the classic path");
        }
        return problems;
    }

    private static string Guide(string relative) => File.ReadAllText(Path.Combine(MainStreetDistricts1113Tests.RepoRoot(), "DOCS/wiki", relative));

    [Fact]
    public void EveryGuidePath_GivesTheClassicKeyFirst_ThenTheDistrictPath()
    {
        GuideProblems(Guide).Should().BeEmpty();
    }

    [Fact]
    public void TheGuideNegatives_CatchAWrongKey_AMissingPath_AndTheWrongOrder()
    {
        string Swap(string g, string from, string to) { string t = Guide(g); t.Should().Contain(from); return t.Replace(from, to); }
        // the Castle guide giving the district key as the classic key
        GuideProblems(g => g == "world/castle.md" ? Swap(g, "press K for the Castle", "press C for the Castle") : Guide(g)).Should().NotBeEmpty();
        // the Arena guide giving the district key for the online row
        GuideProblems(g => g == "online/arena.md" ? Swap(g, "press 6 for", "press A for") : Guide(g)).Should().NotBeEmpty();
        // the story guide without its district path
        GuideProblems(g => g == "world/story.md" ? Swap(g, " With the district layout, open the Status district with S, then press P.", "") : Guide(g)).Should().NotBeEmpty();
        // the Home guide with the district path first
        GuideProblems(g =>
        {
            if (g != "world/home.md") return Guide(g);
            const string classic = "From Main Street press H for Home.", district = "With the district layout, open the Home and Hearth district with H, then press H.";
            return Swap(g, classic + " " + district, district + " " + classic);
        }).Should().NotBeEmpty();
    }

    private static string RoyalBounties(Character hero)
    {
        var castle = new CastleLocation();
        var output = new MemoryStream();
        var term = new TerminalEmulator(new LineStream(new[] { "", "", "" }), output);
        typeof(BaseLocation).GetField("terminal", F)!.SetValue(castle, term);
        typeof(BaseLocation).GetField("currentPlayer", F)!.SetValue(castle, hero);
        ((System.Threading.Tasks.Task)typeof(CastleLocation).GetMethod("RoyalQuests", F)!.Invoke(castle, null)!).GetAwaiter().GetResult();
        term.StreamWriterInternal?.Flush();
        return Encoding.UTF8.GetString(output.ToArray());
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void TheCastleBountyBoard_SendsThePlayerToTheQuestHall_ByTheirLayout(bool districts)
    {
        string board = RoyalBounties(new Character { Name1 = "Board", Name2 = "Board", DistrictsMainStreet = districts });
        string suffix = districts ? "" : ".classic";
        string other = districts ? ".classic" : "";
        board.Should().Contain(Loc.Get("castle.visit_quest_hall" + suffix)).And.NotContain(Loc.Get("castle.visit_quest_hall" + other));
        if (districts) board.Should().Contain("Guild Row").And.NotContain("Quest Hall [2]");
        else board.Should().Contain("Quest Hall [2] on Main Street").And.NotContain("Guild Row");
    }
}
