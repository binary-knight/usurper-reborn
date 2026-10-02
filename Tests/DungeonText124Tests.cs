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
using UsurperReborn.Tests.Localization;
using Xunit;

namespace UsurperReborn.Tests;

/// <summary>
/// v1.2.4: the dungeon text the 1.2.3 pieces left in English. The follower room view and status
/// rows, the merchant's stat labels, the reward share labels, the skill toggle names, the descend
/// broadcast's theme, the feature examine broadcast and the monsters' battle cries and gear read
/// in the reader's language. Every row fits 79 columns in English and Hungarian with a 30-character
/// name. The online location value stays English.
/// </summary>
[Collection("SharedGameSingletons")]
public class DungeonText124Tests
{
    private const BindingFlags F = BindingFlags.NonPublic | BindingFlags.Instance;
    private const BindingFlags FS = BindingFlags.NonPublic | BindingFlags.Static;
    private const int MaxWidth = 79;

    // GameConfig.MaxNameLength (30) characters, the longest name a player can have.
    private const string LongName = "Aranyszivu Hosszunevu Kalandor";
    private const long Big = 123456;

    private static readonly string[] AllLanguages = { "en", "es", "fr", "hu", "it" };

    private static (TerminalEmulator term, MemoryStream output) Term(string input = "")
    {
        var output = new MemoryStream();
        return (new TerminalEmulator(new MemoryStream(Encoding.UTF8.GetBytes(input)), output), output);
    }

    private static string Shown(TerminalEmulator term, MemoryStream output)
    {
        term.StreamWriterInternal?.Flush();
        return Strip(Encoding.UTF8.GetString(output.ToArray()));
    }

    private static string Strip(string s) => Regex.Replace(s, "\u001b\\[[0-9;?]*[A-Za-z]", "");

    private static IEnumerable<string> Rows(string text) => text.Replace("\r", "").Split('\n');

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

    private static T InLanguage<T>(string lang, Func<T> body)
    {
        var prev = GameConfig.Language;
        bool sr = GameConfig.ScreenReaderMode;
        try
        {
            GameConfig.Language = lang;
            GameConfig.ScreenReaderMode = false;
            return body();
        }
        finally { GameConfig.Language = prev; GameConfig.ScreenReaderMode = sr; }
    }

    private static string L(string lang, string key, params object[] args) => Loc.GetIn(lang, key, args);

    private static Character Hero() => new()
    {
        Name1 = "tester", Name2 = LongName, Class = CharacterClass.Warrior, Level = 100, HP = Big, MaxHP = Big,
        AI = CharacterAI.Human, Mental = 100, Gold = 1_000_000_000, Healing = 99,
    };

    private static DungeonLocation Dungeon(TerminalEmulator term, Character hero, int level, DungeonFloor? floor = null)
    {
        var d = new DungeonLocation();
        typeof(BaseLocation).GetField("terminal", F)!.SetValue(d, term);
        typeof(BaseLocation).GetField("currentPlayer", F)!.SetValue(d, hero);
        typeof(DungeonLocation).GetField("currentDungeonLevel", F)!.SetValue(d, level);
        if (floor != null) typeof(DungeonLocation).GetField("currentFloor", F)!.SetValue(d, floor);
        return d;
    }

    private static string Src() =>
        File.ReadAllText(Path.Combine(HardcodedTextScannerTests.RepoRoot(), "Scripts", "Locations", "DungeonLocation.cs"));

    private static JsonElement LangJson(string lang) => JsonDocument.Parse(File.ReadAllText(
        Path.Combine(HardcodedTextScannerTests.RepoRoot(), "Localization", lang + ".json"))).RootElement;

    /// <summary>The theme whose short name is longest in this language.</summary>
    private static DungeonTheme LongestTheme(string lang) => Enum.GetValues<DungeonTheme>()
        .OrderByDescending(t => DungeonLocation.GetThemeShortNameIn(t, lang).Length).First();

    /// <summary>The discovery id whose name is longest in this language.</summary>
    private static string LongestDiscovery(string lang) => LangJson(lang).EnumerateObject()
        .Select(p => Regex.Match(p.Name, "^discovery\\.(.+)\\.name$"))
        .Where(m => m.Success).Select(m => m.Groups[1].Value)
        .OrderByDescending(id => L(lang, $"discovery.{id}.name").Length).First();

    [Fact]
    public void LongName_IsTheLongestPlayerName()
    {
        LongName.Length.Should().Be(GameConfig.MaxNameLength);
    }

    // ---------- 1. the follower room view ----------

    private static (string text, DungeonTheme theme, string featureId) FollowerRoom(string lang, bool boss)
    {
        var theme = LongestTheme(lang);
        string featureId = LongestDiscovery(lang);
        var room = new DungeonRoom
        {
            Id = "r1", Name = new string('R', 52), Description = "A room.", DangerRating = 3, IsExplored = true,
            HasMonsters = true, IsBossRoom = boss, HasTreasure = true, HasEvent = true,
            EventType = DungeonEventType.Settlement, HasStairsDown = true,
        };
        room.Features.Add(new RoomFeature("English Feature", "desc", FeatureInteraction.Examine) { DiscoveryId = featureId });
        var cleared = new DungeonRoom { Id = "c1", Name = "Crypt", IsExplored = true, IsCleared = true };
        var explored = new DungeonRoom { Id = "e1", Name = "Hall", IsExplored = true };
        var unknown = new DungeonRoom { Id = "u1", Name = "Pit" };
        room.Exits[Direction.North] = new RoomExit("c1", "north");
        room.Exits[Direction.South] = new RoomExit("e1", "south");
        room.Exits[Direction.East] = new RoomExit("u1", "east");
        var floor = new DungeonFloor { Level = 100, Theme = theme, CurrentRoomId = "r1" };
        floor.Rooms.AddRange(new[] { room, cleared, explored, unknown });
        var (term, _) = Term();
        // The leader plays in English; the view is built for the follower's language.
        var d = InLanguage("en", () => Dungeon(term, Hero(), 100, floor));
        var build = typeof(DungeonLocation).GetMethod("BuildRoomAnsi", F)!;
        string text = InLanguage("en", () => Strip((string)build.Invoke(d, new object[] { room, "leader", lang })!));
        return (text, theme, featureId);
    }

    [Theory]
    [InlineData("en")]
    [InlineData("hu")]
    public void FollowerRoomView_RendersInTheFollowersLanguage_AndFits(string lang)
    {
        var (text, theme, featureId) = FollowerRoom(lang, boss: false);
        var (bossText, _, _) = FollowerRoom(lang, boss: true);
        Capture($"dungeon-text-follower-room-{lang}.txt", text + "\n" + bossText);
        var rows = Rows(text).ToList();

        text.Should().Contain($"  {L(lang, "dungeon.floor", 100)} | {DungeonLocation.GetThemeShortNameIn(theme, lang)} | ***");
        text.Should().Contain("  " + L(lang, "dungeon.follower_hint_hostile"));
        text.Should().Contain("  " + L(lang, "dungeon.follower_hint_treasure"));
        text.Should().Contain("  " + L(lang, "dungeon.room_stairs_down"));
        text.Should().Contain("  >> " + L(lang, "dungeon.event_hint_settlement").Replace(">>", "").Replace("<<", "").Trim() + " <<");
        text.Should().Contain("  " + L(lang, "dungeon.you_notice"));
        text.Should().Contain("    - " + L(lang, $"discovery.{featureId}.name"));
        text.Should().Contain($"  {L(lang, "dungeon.exits")} [N {L(lang, "dungeon.exit_tag_cleared")}] [S {L(lang, "dungeon.exit_tag_explored")}] [E ?]");
        bossText.Should().Contain("  " + L(lang, "dungeon.room_boss_presence"));

        if (lang == "en")
        {
            rows.Should().Contain("  >> Hostile creatures lurk in the shadows <<", "the English hint is unchanged");
            rows.Should().Contain("  >> Something valuable glints in the darkness <<");
            rows.Should().Contain("  >> Stairs lead down to a deeper level <<");
            rows.Should().Contain("  You notice:");
            rows.Should().Contain("  Exits: [N clr] [S exp] [E ?]");
            bossText.Should().Contain("  >> A powerful presence dominates this room! <<");
        }
        if (lang == "hu")
        {
            foreach (var english in new[] { "Floor ", "Hostile creatures", "Something valuable", "Stairs lead", "powerful presence",
                         "You notice", "Exits:", " clr]", " exp]", "English Feature", theme.ToString(),
                         DungeonLocation.GetThemeShortNameIn(theme, "en") })
            {
                text.Should().NotContain(english);
                bossText.Should().NotContain(english);
            }
        }
        EveryRowFits(text, "follower room view");
        EveryRowFits(bossText, "follower boss room view");
    }

    // ---------- 2. the follower's own status rows ----------

    private static string FollowerStatus(string lang, Character follower, string leaderName) =>
        Strip(DungeonLocation.BuildFollowerStatusAnsi(follower, leaderName, lang));

    [Theory]
    [InlineData("en")]
    [InlineData("hu")]
    public void FollowerStatus_RendersInTheFollowersLanguage_AndFits(string lang)
    {
        var follower = Hero();
        follower.MaxPotions.Should().BeGreaterThan(0);
        string text = FollowerStatus(lang, follower, LongName);
        Capture($"dungeon-text-follower-status-{lang}.txt", text);
        text.Should().Contain($"{L(lang, "dungeon.hp_label")}: [");
        text.Should().Contain(L(lang, "dungeon.follower_bar_gold", "1,000,000,000"));
        text.Should().Contain(L(lang, "dungeon.potion_cache_count", 99, follower.MaxPotions));
        text.Should().Contain(L(lang, "dungeon.follower_bar_following", LongName));
        text.Should().Contain(L(lang, "dungeon.follower_bar_keys") + " | /party /help /say");
        if (lang == "hu")
            foreach (var english in new[] { "Gold:", "Potions:", "Following", "Inv  [P]ot", "Status", "Leave" })
                text.Should().NotContain(english);
        EveryRowFits(text, "follower status");
    }

    [Fact]
    public void FollowerStatus_English_StaysOnTheOldRows_WhenTheyFit()
    {
        var follower = new Character { Name2 = "Bob", HP = 50, MaxHP = 100, Gold = 1234, Healing = 3 };
        string text = FollowerStatus("en", follower, "Ann");
        var rows = Rows(text).Where(r => r.Length > 0).ToList();
        rows.Should().HaveCount(3);
        rows[1].Should().Be($"  HP: [##########..........] 50/100  Gold: 1,234  Potions: 3/{follower.MaxPotions}");
        rows[2].Should().Be("  Following Ann | [*] Inv  [P]ot  [%] Status  [Q] Leave | /party /help /say");

        // A 30-character leader name breaks the footer in two: the old single row would be 105 wide.
        var longRows = Rows(FollowerStatus("en", follower, LongName)).Where(r => r.Length > 0).ToList();
        longRows.Should().Contain($"  Following {LongName}");
        longRows.Should().Contain("  [*] Inv  [P]ot  [%] Status  [Q] Leave | /party /help /say");
    }

    [Fact]
    public void FollowerStatus_IsBuiltPerFollowerLanguage_AtEveryPush()
    {
        string src = Src();
        src.Should().Contain("PushRoomToSingleFollower(mate, roomAnsi, currentPlayer?.DisplayName ?? ctx.Username, lang);");
        Regex.Matches(src, "PushRoomToSingleFollower\\([^;]*GameConfig\\.Language\\);").Count
            .Should().Be(2, "the join and re-push paths run in the follower's own session");
        src.Should().NotContain("Following \\u001b[97m").And.NotContain("Potions: {follower.Healing}");
    }

    // ---------- 3. the descend broadcast and the theme name ----------

    [Theory]
    [InlineData("en")]
    [InlineData("hu")]
    public void Descends_ShowsTheShortThemeNameInTheFollowersLanguage_AndFits(string lang)
    {
        var theme = LongestTheme(lang);
        string sent = Strip(DungeonLocation.FollowerMessage(l =>
            $"\u001b[34m  {L(l, "dungeon.bc_descends", 100, DungeonLocation.GetThemeShortNameIn(theme, l))}\u001b[0m")(lang));
        Capture($"dungeon-text-descends-{lang}.txt", sent);
        sent.Should().Contain(DungeonLocation.GetThemeShortNameIn(theme, lang));
        if (theme.ToString() != DungeonLocation.GetThemeShortNameIn(theme, lang))
            sent.Should().NotContain(theme.ToString(), "the raw enum name is no longer shown");
        EveryRowFits(sent, "descend broadcast");
        Src().Should().Contain("Loc.GetIn(lang, \"dungeon.bc_descends\", currentDungeonLevel, GetThemeShortNameIn(currentFloor.Theme, lang))");
    }

    [Fact]
    public void ThemeShortName_InHungarian_IsNeverTheEnglishName()
    {
        foreach (var theme in Enum.GetValues<DungeonTheme>())
        {
            string hu = DungeonLocation.GetThemeShortNameIn(theme, "hu");
            hu.Should().NotBe(theme.ToString());
            hu.Should().NotBe(DungeonLocation.GetThemeShortNameIn(theme, "en"));
            InLanguage("hu", () => DungeonLocation.GetThemeShortName(theme)).Should().Be(hu, "the leader's overload reads the session language");
        }
    }

    // ---------- 4. the reward share labels ----------

    private static List<string> RewardSourceKeys()
    {
        string src = Src();
        var keys = Regex.Matches(src, "(?:AwardDungeonReward|ShareEventRewardsWithGroup)\\([^;]*?\"([^\"]+)\"(?:, fromCombat: true)?\\);")
            .Select(m => m.Groups[1].Value).Distinct().ToList();
        return keys;
    }

    [Fact]
    public void RewardSources_AreKeys_InEveryLanguage()
    {
        var keys = RewardSourceKeys();
        keys.Should().HaveCount(14, "four AwardDungeonReward and ten ShareEventRewardsWithGroup labels");
        foreach (var key in keys)
        {
            key.Should().Contain(".", $"{key} is a Loc key, not an English label");
            foreach (var lang in AllLanguages)
                Loc.HasIn(lang, key).Should().BeTrue($"{key} is in {lang}");
            L("hu", key).Should().NotBe(L("en", key), $"{key} is translated in Hungarian");
        }
        Src().Should().Contain("Loc.GetIn(shareLang, \"dungeon.feature_your_share\", Loc.GetIn(shareLang, sourceKey))");
    }

    [Theory]
    [InlineData("en")]
    [InlineData("hu")]
    public void RewardShareRows_RenderInTheMatesLanguage_AndFit(string lang)
    {
        var sb = new StringBuilder();
        foreach (var key in RewardSourceKeys())
            sb.AppendLine($"  ═══ {L(lang, "dungeon.feature_your_share", L(lang, key))} ═══");
        string text = sb.ToString();
        Capture($"dungeon-text-reward-shares-{lang}.txt", text);
        if (lang == "en") text.Should().Contain("  ═══ Lost Explorer Rescue (Your Share) ═══", "the English label is unchanged");
        if (lang == "hu") text.Should().NotContain("Rescue").And.NotContain("Solved").And.NotContain("Treasure").And.NotContain("Shrine");
        EveryRowFits(text, "reward share");
    }

    // ---------- 5. the merchant's stat labels ----------

    [Theory]
    [InlineData("en")]
    [InlineData("hu")]
    public async Task MerchantStatLabels_AreInLanguage_AndFit(string lang)
    {
        var item = new Item
        {
            Name = "Ring", Attack = 12, Armor = 9, Strength = 5, Defence = 4, Dexterity = 3, Wisdom = 2, Agility = 1,
            Charisma = -1, HP = 40, Mana = 30, Stamina = 6,
        };
        item.LootEffects.Add(((int)LootGenerator.SpecialEffect.Constitution, 7));
        item.LootEffects.Add(((int)LootGenerator.SpecialEffect.Intelligence, 8));
        var acc = typeof(DungeonLocation).GetMethod("FormatAccessoryStats", FS)!;
        var bonus = typeof(DungeonLocation).GetMethod("FormatItemBonuses", FS)!;
        string full = InLanguage(lang, () => (string)acc.Invoke(null, new object[] { item })!);
        string bare = InLanguage(lang, () => (string)acc.Invoke(null, new object[] { new Item { Name = "Plain" } })!);
        string weaponTail = InLanguage(lang, () => (string)bonus.Invoke(null, new object[] { item })!);
        Capture($"dungeon-text-merchant-stats-{lang}.txt", full + "\n" + bare + "\n" + weaponTail);

        foreach (var (key, value) in new[] { ("stats.str", "+5"), ("stats.def", "+4"), ("stats.dex", "+3"), ("stats.wis", "+2"),
                     ("stats.agi", "+1"), ("stats.cha", "-1"), ("stats.con", "+7"), ("stats.int", "+8"), ("ui.stat_hp", "+40"),
                     ("ui.stat_mana", "+30"), ("stats.sta", "+6") })
        {
            full.Should().Contain($"{L(lang, key)} {value}");
            weaponTail.Should().Contain($"{L(lang, key)} {value}");
        }
        full.Should().StartWith($"{L(lang, "dungeon.merchant_stat_atk", 12)}, {L(lang, "dungeon.merchant_stat_ac", 9)}, ");
        bare.Should().Be(L(lang, "dungeon.merchant_accessory"));
        if (lang == "en")
        {
            full.Should().Be("Atk +12, AC +9, STR +5, DEF +4, DEX +3, WIS +2, AGI +1, CHA -1, CON +7, INT +8, HP +40, Mana +30, STA +6",
                "the English labels are unchanged");
            bare.Should().Be("Accessory");
        }
        if (lang == "hu")
            foreach (var english in new[] { "STR ", "DEF ", "DEX ", "WIS ", "AGI ", "CHA ", "CON ", "STA ", "Atk ", "AC ", "Accessory" })
                (full + bare).Should().NotContain(english);

        // The merchant shows the description on its own rows; the longest generated one wraps to fit.
        var (term, output) = Term("Y\n");
        var hero = Hero();
        var d = InLanguage(lang, () => Dungeon(term, hero, 100));
        var gen = typeof(DungeonLocation).GetMethod("GenerateMerchantRareItems", F)!;
        object? longest = null;
        string longestDesc = "";
        for (int visit = 0; visit < 40; visit++)
        {
            var items = InLanguage(lang, () => (System.Collections.IList)gen.Invoke(d, new object[] { 100 })!);
            foreach (var it in items)
            {
                string desc = (string)it!.GetType().GetProperty("Description")!.GetValue(it)!;
                if (lang == "hu") desc.Should().NotContain("Accessory").And.NotContain("STR ").And.NotContain("AC +");
                if (desc.Length > longestDesc.Length) { longest = it; longestDesc = desc; }
            }
        }
        // Skip the comparison table (combat code, not part of this screen's keys).
        longest!.GetType().GetProperty("LootItem")!.SetValue(longest, null);
        string shown = await InLanguageAsync(lang, async () =>
        {
            await (Task)typeof(DungeonLocation).GetMethod("PurchaseRareItem", F)!.Invoke(d, new object[] { hero, longest })!;
            return Shown(term, output);
        });
        Capture($"dungeon-text-merchant-purchase-{lang}.txt", shown);
        string joined = string.Join(" ", Rows(shown).Select(r => r.Trim()));
        joined.Should().Contain(longestDesc, "the wrapped rows keep the whole description");
        EveryRowFits(shown, "merchant purchase");
    }

    private static async Task<T> InLanguageAsync<T>(string lang, Func<Task<T>> body)
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

    // ---------- 6. the skill toggles ----------

    [Theory]
    [InlineData("en")]
    [InlineData("hu")]
    public void SkillToggles_ShowTheLocalizedAbilityName_AndFit(string lang)
    {
        string text = InLanguage(lang, () =>
        {
            var longest = ClassAbilitySystem.GetAllAbilities().OrderByDescending(a => a.DisplayName.Length).First();
            var (term, output) = Term();
            var d = Dungeon(term, Hero(), 100);
            var row = typeof(DungeonLocation).GetMethod("WriteSkillToggleRow", F)!;
            row.Invoke(d, new object[] { 10, longest.DisplayName, longest.DisplayDescription + new string('x', 60), false });
            row.Invoke(d, new object[] { 11, longest.DisplayName, longest.DisplayDescription + new string('x', 60), true });
            string shown = Shown(term, output);
            shown.Should().Contain(longest.DisplayName);
            if (lang == "hu" && longest.Name != longest.DisplayName) shown.Should().NotContain(longest.Name);
            return shown;
        });
        Capture($"dungeon-text-skill-toggles-{lang}.txt", text);
        EveryRowFits(text, "skill toggles");

        var any = ClassAbilitySystem.GetAllAbilities().First(a => Loc.HasIn("hu", $"ability.{a.Id}.name"));
        InLanguage("hu", () => any.DisplayName).Should().Be(L("hu", $"ability.{any.Id}.name"));
        Src().Should().Contain("WriteSkillToggleRow(row, ab.DisplayName, ab.DisplayDescription, disabledAbilities.Contains(ab.Id));");
    }

    [Fact]
    public void SkillToggle_English_KeepsItsLayout()
    {
        string text = InLanguage("en", () =>
        {
            var (term, output) = Term();
            var d = Dungeon(term, Hero(), 100);
            typeof(DungeonLocation).GetMethod("WriteSkillToggleRow", F)!
                .Invoke(d, new object[] { 3, "Power Strike", new string('d', 60), false });
            return Shown(term, output);
        });
        Rows(text).First().Should().Be("  [ 3] [ON]  " + "Power Strike".PadRight(22) + " " + new string('d', 34),
            "a name that fits the 22 column keeps the 34 character description");
    }

    // ---------- 7. the online location ----------

    [Fact]
    public void OnlineLocation_StaysEnglish_ForEveryReader()
    {
        string src = Src();
        src.Should().Contain("ctx.OnlineState?.UpdateLocation($\"Dungeon (Group: {leaderName})\");",
            "online_players.location is shared by every viewer and stays English like the other location names");
        Regex.Matches(src, "OnlineState\\?\\.UpdateLocation\\(").Count.Should().Be(1, "the group follower string is the dungeon's only online location");
        // The one reader that compares the value, the co-presence line, never runs in the dungeon.
        UsurperRemake.Server.RoomRegistry.ShowsCoPresence(GameLocation.Dungeons).Should().BeFalse();
        var others = BaseLocation.CoPresenceOthers(new[]
        {
            new OnlinePlayerInfo { Username = "other", Location = "Dungeon (Group: Ann)" },
        }, GameLocation.Dungeons, "Dungeon (Group: Ann)", "me");
        others.Should().BeEmpty();
    }

    // ---------- 8. the battle cries, gear and the enraged duelist ----------

    private static readonly string[] CryAndGearKeys =
    {
        "dungeon.cry.duelist_insulted", "dungeon.cry.duelist_different", "dungeon.cry.duelist_cannot_defeat",
        "dungeon.cry.duelist_honorable", "dungeon.cry.die_enraged", "dungeon.cry.join_me", "dungeon.cry.not_be_here",
        "dungeon.cry.fooled_you", "dungeon.cry.no_gold_death", "dungeon.cry.mind_business", "dungeon.cry.sacrilege",
        "dungeon.cry.ignorance_doom", "dungeon.cry.regret_this", "dungeon.gear.rusty_blade", "dungeon.gear.tattered_armor",
        "dungeon.gear.void_blade", "dungeon.gear.dimensional_armor", "dungeon.gear.duelist_garb", "dungeon.gear.teeth",
        "dungeon.gear.wooden_shell", "dungeon.gear.knife", "dungeon.gear.rags", "dungeon.gear.spectral_claws",
        "dungeon.gear.ethereal_form", "dungeon.gear.steel_sword", "dungeon.gear.stone_fist", "dungeon.gear.merchant_blade",
        "dungeon.gear.rusty_sword", "dungeon.duelist_enraged_name",
    };

    [Fact]
    public void BattleCriesAndGear_AreKeyed_InEveryLanguage()
    {
        foreach (var key in CryAndGearKeys.Concat(new[] { "dungeon.phrase_default_2", "dungeon.phrase_default_3",
                     "item.slot.weapon", "item.slot.armor", "item.chain_mail", "item.leather_armor" }))
        {
            foreach (var lang in AllLanguages)
            {
                Loc.HasIn(lang, key).Should().BeTrue($"{key} is in {lang}");
                L(lang, key, LongName).Should().NotBeNullOrWhiteSpace("a monster's gear must not be empty: WUser/AUser read it");
            }
            L("hu", key, "x").Should().NotBe(L("en", key, "x"), $"{key} is translated in Hungarian");
        }

        string src = Src();
        foreach (var english in new[] { "\"Duelist's Garb\"", "\" (Enraged)\"", "\"DIE!\"", "\"Attack!\"", "\"Die!\"", "\"Weapon\", \"Armor\"",
                     "\"An honorable fight!\"", "\"You will pay for your insults!\"", "\"Fooled you!\"", "\"...\", false, false, \"Rusty Sword\"",
                     "\"Merchant's Blade\"", "\"Steel Sword\"", "\"Chain Mail\"", "\"Stone Fist\"", "\"You will join me...\"" })
            src.Should().NotContain(english);
        src.Should().Contain("\"Ancient Stone\", // English on purpose", "the riddle guardian's armor can be picked up and saved as an item name");
    }

    [Fact]
    public void DuelistBattleCry_IsInThePlayersLanguage()
    {
        var type = typeof(DungeonLocation).GetNestedType("RecurringDuelist", BindingFlags.NonPublic)!;
        var cry = type.GetMethod("GetBattleCry")!;
        var cases = new (bool insulted, int wins, int losses, string key)[]
        {
            (true, 0, 0, "dungeon.cry.duelist_insulted"),
            (false, 5, 0, "dungeon.cry.duelist_different"),
            (false, 0, 5, "dungeon.cry.duelist_cannot_defeat"),
            (false, 1, 1, "dungeon.cry.duelist_honorable"),
        };
        foreach (var (insulted, wins, losses, key) in cases)
        {
            var d = Activator.CreateInstance(type, true)!;
            type.GetProperty("WasInsulted")!.SetValue(d, insulted);
            type.GetProperty("PlayerWins")!.SetValue(d, wins);
            type.GetProperty("PlayerLosses")!.SetValue(d, losses);
            InLanguage("hu", () => (string)cry.Invoke(d, null)!).Should().Be(L("hu", key));
            InLanguage("en", () => (string)cry.Invoke(d, null)!).Should().Be(L("en", key));
        }
        L("en", "dungeon.cry.duelist_honorable").Should().Be("An honorable fight!", "the English cry is unchanged");
    }

    [Fact]
    public void EnragedDuelistName_NeverReadsAsANamedMonster()
    {
        // CombatEngine raises the drop chance for a name containing Boss, Chief, Lord or King.
        foreach (var lang in AllLanguages)
        {
            string suffix = L(lang, "dungeon.duelist_enraged_name", "");
            foreach (var word in new[] { "Boss", "Chief", "Lord", "King" })
                suffix.Should().NotContain(word, $"the {lang} enraged suffix must not change the drop chance");
            L(lang, "dungeon.duelist_enraged_name", LongName).Should().StartWith(LongName);
        }
        L("en", "dungeon.duelist_enraged_name", "Kira").Should().Be("Kira (Enraged)", "the English name is unchanged");
    }

    // ---------- 9. the feature examine broadcast ----------

    [Theory]
    [InlineData("en")]
    [InlineData("hu")]
    public void FeatureExamineBroadcast_IsInTheFollowersLanguage_AndFits(string lang)
    {
        string id = LongestDiscovery(lang);
        var feature = new RoomFeature("English Feature", "desc", FeatureInteraction.Examine) { DiscoveryId = id };
        string sent = Strip(DungeonLocation.FollowerMessage(l =>
            DungeonLocation.FeatureExamineBroadcast(l, LongName, feature, true, Big, true))(lang));
        Capture($"dungeon-text-feature-examine-{lang}.txt", sent);
        sent.Should().Contain(L(lang, "dungeon.examine_bc_lore"));
        sent.Should().Contain(L(lang, "dungeon.examine_bc_insight"));
        string.Join(" ", Rows(sent).Select(r => r.Trim())).Should()
            .Contain(L(lang, "dungeon.examine_bc_examines", LongName, L(lang, $"discovery.{id}.name")))
            .And.Contain(L(lang, "dungeon.examine_bc_damage", LongName, Big));
        if (lang == "en")
        {
            var plain = new RoomFeature("Odd Statue", "desc", FeatureInteraction.Examine);
            Strip(DungeonLocation.FeatureExamineBroadcast("en", "Ann", plain, true, 12, true)).Replace("\r", "").Should().Be(
                "  Ann examines Odd Statue...\n  Ancient lore discovered!\n  It was dangerous! Ann took 12 damage.\n  A moment of spiritual insight...\n",
                "the English broadcast is unchanged");
        }
        if (lang == "hu")
            foreach (var english in new[] { "examines", "Ancient lore", "dangerous", "took", "spiritual", "English Feature" })
                sent.Should().NotContain(english);
        EveryRowFits(sent, "feature examine broadcast");
        Src().Should().Contain("BroadcastDungeonEvent(lang => FeatureExamineBroadcast(lang, examiner, feature,");
    }

    // ---------- 10. French dungeon hints ----------

    [Fact]
    public void FrenchStrings_HaveNoHtmlEntities()
    {
        foreach (var lang in AllLanguages)
            foreach (var p in LangJson(lang).EnumerateObject())
            {
                string v = p.Value.GetString() ?? "";
                v.Should().NotContain("&gt;", $"{lang} {p.Name} prints raw in a terminal");
                v.Should().NotContain("&lt;", $"{lang} {p.Name} prints raw in a terminal");
            }
        L("fr", "dungeon.room_stairs_down").Should().StartWith(">> ");
    }
}
