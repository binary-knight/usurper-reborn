using System;
using System.Collections;
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
/// v1.2.3: DungeonLocation.cs from MerchantTradeMenu to the end of the file reads in the player's
/// language: the merchant's rare goods, the ally list, the skill toggles, the party status, the
/// potion sounds, the map header and legend, the settlements, the Safe Haven camp and the group
/// broadcasts. Every screen is rendered in English and Hungarian with worst case inputs and every
/// row must fit in 79 columns.
/// </summary>
[Collection("SharedGameSingletons")]
public class DungeonLocB123Tests
{
    private const BindingFlags F = BindingFlags.NonPublic | BindingFlags.Instance;
    private const BindingFlags FS = BindingFlags.NonPublic | BindingFlags.Static;
    private const int MaxWidth = 79;

    // GameConfig.MaxNameLength (30) characters, the longest name a player can have.
    private const string LongName = "Aranyszivu Hosszunevu Kalandor";
    private const long Big = 123456;

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

    /// <summary>Writes a render to USURPER_EVIDENCE_DIR when set, for a by-eye check.</summary>
    private static void Capture(string name, string text)
    {
        var dir = Environment.GetEnvironmentVariable("USURPER_EVIDENCE_DIR");
        if (!string.IsNullOrEmpty(dir)) File.WriteAllText(Path.Combine(dir, name), text);
    }

    // A box header drawn by WriteBoxHeader is at most 80 wide by design only on the BBS screens; the
    // screens here draw narrower boxes, so every row must fit in 79.
    private static void EveryRowFits(string text, string screen)
    {
        foreach (var row in Rows(text))
            row.Length.Should().BeLessOrEqualTo(MaxWidth, $"every row of the {screen} fits in {MaxWidth} columns: \"{row}\"");
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
    private static string Hu(string key, params object[] args) => Loc.GetIn("hu", key, args);

    private static Character Hero(int level = 100) => new()
    {
        Name1 = "tester", Name2 = LongName, Class = CharacterClass.Warrior, Level = level, HP = 900, MaxHP = Big,
        AI = CharacterAI.Human, Mental = 100, Gold = 1_000_000_000,
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

    private static async Task Run(object target, string method, params object[] args) =>
        await (Task)target.GetType().GetMethod(method, F)!.Invoke(target, args)!;

    private static string Src() =>
        File.ReadAllText(Path.Combine(HardcodedTextScannerTests.RepoRoot(), "Scripts", "Locations", "DungeonLocation.cs"));

    // ---------- 1. the merchant's rare goods ----------

    [Theory]
    [InlineData("en")]
    [InlineData("hu")]
    public async Task MerchantRareItem_DescriptionAndAcquired_RenderInLanguage_AndFit(string lang)
    {
        string text = await InLanguage(lang, async () =>
        {
            var (term, output) = Term("Y\n");
            var hero = Hero();
            var d = Dungeon(term, hero, 100);
            var gen = typeof(DungeonLocation).GetMethod("GenerateMerchantRareItems", F)!;
            // The longest weapon name the merchant offered over 40 visits at floor 100.
            object? longest = null;
            string longestName = "";
            for (int visit = 0; visit < 40; visit++)
            {
                var items = (IList)gen.Invoke(d, new object[] { 100 })!;
                var weapon = items[0]!;
                var loot = (Item)weapon.GetType().GetProperty("LootItem")!.GetValue(weapon)!;
                string desc = (string)weapon.GetType().GetProperty("Description")!.GetValue(weapon)!;
                desc.Should().StartWith(L(lang, "dungeon.merchant_stat_atk", loot.Attack), "the weapon's attack is described in the player's language");
                string name = (string)weapon.GetType().GetProperty("Name")!.GetValue(weapon)!;
                if (name.Length > longestName.Length) { longest = weapon; longestName = name; }
                var armor = items[1]!;
                var armorLoot = (Item)armor.GetType().GetProperty("LootItem")!.GetValue(armor)!;
                ((string)armor.GetType().GetProperty("Description")!.GetValue(armor)!)
                    .Should().StartWith(L(lang, "dungeon.merchant_stat_ac", armorLoot.Armor));
            }
            // Skip the comparison table (combat code, not part of this screen's keys).
            longest!.GetType().GetProperty("LootItem")!.SetValue(longest, null);
            await Run(d, "PurchaseRareItem", hero, longest);
            return Shown(term, output);
        });
        Capture($"dungeon-b-merchant-acquired-{lang}.txt", text);
        text.Should().Contain(L(lang, "dungeon.merchant_acquired", "").Trim());
        if (lang == "hu") text.Should().NotContain("ACQUIRED:").And.NotContain("Atk +");
        EveryRowFits(text, "merchant purchase");
    }

    // ---------- 2. the ally list in party management ----------

    [Theory]
    [InlineData("en")]
    [InlineData("hu")]
    public async Task AllyList_LongestNpcNameAndSixDigitHp_Fits(string lang)
    {
        // The list holds team NPCs; the longest built-in NPC name, with the longest class name.
        string npcName = ClassicNPCs.GetClassicNPCs().Select(n => n.Name).OrderByDescending(n => n.Length).First();
        var cls = await InLanguage(lang, () => Task.FromResult(Enum.GetValues<CharacterClass>()
            .OrderByDescending(c => GameConfig.GetLocalizedClassName(c).Length).First()));
        var npc = new NPC { Name1 = npcName, Name2 = npcName, ID = "dungeon-b-ally", Team = "Dungeon B Team", Level = 100, Class = cls,
            HP = Big, MaxHP = Big, AI = CharacterAI.Computer };
        var spawner = NPCSpawnSystem.Instance;
        spawner.ActiveNPCs.Add(npc);
        try
        {
            string text = await InLanguage(lang, async () =>
            {
                var (term, output) = Term("\n\n\n");
                var hero = Hero();
                hero.Team = "Dungeon B Team";
                var d = Dungeon(term, hero, 100);
                await Run(d, "ManageTeam");
                return Shown(term, output);
            });
            Capture($"dungeon-b-ally-list-{lang}.txt", text);
            text.Should().Contain($"] {npcName} - {L(lang, "dungeon.level_label")} 100");
            text.Should().Contain($"      {L(lang, "dungeon.hp_label")}: {Big}/{Big}");
            EveryRowFits(text, "ally list");
        }
        finally { spawner.ActiveNPCs.Remove(npc); }
    }

    // ---------- 3. the skill toggle rows ----------

    [Theory]
    [InlineData("en")]
    [InlineData("hu")]
    public async Task SkillToggleRows_RenderInLanguage_AndFit(string lang)
    {
        string text = await InLanguage(lang, () =>
        {
            // The longest ability or spell name the screen can list.
            string name = ClassAbilitySystem.GetAllAbilities().Select(a => a.Name)
                .Concat(Enum.GetValues<CharacterClass>().SelectMany(c => SpellSystem.GetAllSpellsForClass(c) ?? new List<SpellSystem.SpellInfo>()).Select(s => s.DisplayName))
                .OrderByDescending(n => n.Length).First();
            var (term, output) = Term();
            var d = Dungeon(term, Hero(), 100);
            var row = typeof(DungeonLocation).GetMethod("WriteSkillToggleRow", F)!;
            row.Invoke(d, new object[] { 10, name, new string('x', 60), false });
            row.Invoke(d, new object[] { 11, name, new string('x', 60), true });
            return Task.FromResult(Shown(term, output));
        });
        Capture($"dungeon-b-skill-toggles-{lang}.txt", text);
        var rows = Rows(text).Where(r => r.Length > 0).ToList();
        rows[0].Should().Contain("] " + L(lang, "dungeon.skill_on") + " ");
        rows[1].Should().Contain("] " + L(lang, "dungeon.skill_off") + " ");
        rows[0].IndexOf(L(lang, "dungeon.skill_on")).Should().Be(rows[1].IndexOf(L(lang, "dungeon.skill_off")));
        (rows[0].Length).Should().Be(rows[1].Length, "the name column starts at the same place for on and off");
        if (lang == "en") rows[0].Should().StartWith("  [10] [ON]  ", "the English layout is unchanged");
        if (lang == "hu") text.Should().NotContain("[ON]").And.NotContain("[OFF]");
        EveryRowFits(text, "skill toggles");
    }

    // ---------- 4. the party member equipment screen ----------

    [Fact]
    public void PartyMemberEquipment_PassesNoEnglishSlotLabels()
    {
        // BaseLocation.DisplayEquipmentSlotWithStats shows GameConfig.GetLocalizedSlotName(slot);
        // the label was never shown, so the dungeon passes none.
        string src = Src();
        src.Should().NotMatchRegex("DisplayEquipmentSlotWithStats\\(target, EquipmentSlot\\.\\w+, \"");
        src.Should().Contain("DisplayEquipmentSlotWithStats(target, EquipmentSlot.RFinger);");
    }

    // ---------- 5. the party status in the potion menu ----------

    [Theory]
    [InlineData("en")]
    [InlineData("hu")]
    public async Task PotionMenuPartyStatus_FullTag_RendersInLanguage_AndFits(string lang)
    {
        string text = await InLanguage(lang, async () =>
        {
            var (term, output) = Term("Q\n\n\n");
            var hero = Hero();
            var d = Dungeon(term, hero, 100);
            // A grouped player can have the longest name; full HP and six-digit HP and mana.
            var mate = new Character { Name1 = LongName, Name2 = LongName, Class = CharacterClass.Magician, Level = 100,
                HP = Big, MaxHP = Big, Mana = Big, MaxMana = Big, AI = CharacterAI.Human };
            var mates = (List<Character>)typeof(DungeonLocation).GetField("teammates", F)!.GetValue(d)!;
            mates.Add(mate);
            await Run(d, "UsePotions");
            return Shown(term, output);
        });
        Capture($"dungeon-b-potion-party-{lang}.txt", text);
        text.Should().Contain($"{Big}/{Big}{L(lang, "combat.full_status")}");
        Rows(text).Should().Contain($"    {L(lang, "dungeon.mp_label")}:{Big}/{Big}", "the mana moves to its own row when the row would pass 79 columns");
        if (lang == "hu") text.Should().NotContain("(Full)");
        EveryRowFits(text, "potion menu");
    }

    // ---------- 6. the potion sounds ----------

    [Theory]
    [InlineData("en")]
    [InlineData("hu")]
    public async Task PotionGlug_RendersInLanguage_AndFits(string lang)
    {
        foreach (var (method, key) in new[] { ("UseHealingPotion", "dungeon.potion_glug"), ("HealToFull", "dungeon.potions_glug") })
        {
            string text = await InLanguage(lang, async () =>
            {
                var (term, output) = Term("Y\n\n\n");
                var hero = Hero();
                hero.Healing = 20; hero.HP = 1;
                var d = Dungeon(term, hero, 100);
                await Run(d, method, hero);
                return Shown(term, output);
            });
            Capture($"dungeon-b-{method}-{lang}.txt", text);
            Rows(text).Should().Contain(L(lang, key), method);
            if (lang == "hu") text.Should().NotContain("glug");
            EveryRowFits(text, method);
        }
    }

    // ---------- 7. the map header and legend ----------

    private static DungeonFloor BigFloor(DungeonTheme theme)
    {
        var floor = new DungeonFloor { Level = 100, Theme = theme, CurrentRoomId = "r0", EntranceRoomId = "r0" };
        for (int i = 0; i < 99; i++)
            floor.Rooms.Add(new DungeonRoom { Id = $"r{i}", Name = $"Room {i}", Description = "A room.", DangerRating = 1, IsExplored = true, IsCleared = true });
        for (int i = 0; i < 4; i++)
        {
            floor.Rooms[i].Exits[Direction.East] = new RoomExit($"r{i + 1}", "east");
            floor.Rooms[i + 1].Exits[Direction.West] = new RoomExit($"r{i}", "west");
        }
        return floor;
    }

    [Theory]
    [InlineData("en")]
    [InlineData("hu")]
    public async Task MapHeaderAndLegend_LongestTheme_RenderInLanguage_AndFit(string lang)
    {
        // The theme whose short name is longest in this language, and two-digit room counts.
        var themes = Enum.GetValues<DungeonTheme>().ToList();
        string themeName = "";
        string text = await InLanguage(lang, async () =>
        {
            var theme = themes.OrderByDescending(t => ((string)typeof(DungeonLocation).GetMethod("GetThemeShortName", FS)!.Invoke(null, new object[] { t })!).Length).First();
            themeName = (string)typeof(DungeonLocation).GetMethod("GetThemeShortName", FS)!.Invoke(null, new object[] { theme })!;
            var longest = Enum.GetValues<DungeonTheme>()
                .Select(t => (string)typeof(DungeonLocation).GetMethod("GetThemeShortName", FS)!.Invoke(null, new object[] { t })!)
                .Max(n => n.Length);
            themeName.Length.Should().Be(longest);
            var (term, output) = Term("\n\n");
            var floor = BigFloor(theme);
            var d = Dungeon(term, Hero(), 100, floor);
            await Run(d, "ShowDungeonMap");
            return Shown(term, output);
        });
        Capture($"dungeon-b-map-{lang}.txt", text);
        text.Should().Contain(" " + L(lang, "dungeon.map_header", 100, themeName, 99, 99, 99));
        text.Should().Contain("  " + L(lang, "dungeon.map_legend_title"));
        if (lang == "hu") text.Should().NotContain("DUNGEON MAP").And.NotContain("Legend:");
        if (lang == "en") L("en", "dungeon.map_header", 100, "X", 1, 2, 3).Should().Be("DUNGEON MAP ── Level 100 (X)  [1/2 explored, 3/2 cleared]");
        EveryRowFits(text, "dungeon map");
    }

    [Fact]
    public void MapLegend_IsARowCount_NotEnglishText()
    {
        string src = Src();
        src.Should().Contain("const int legendRows = 11;");
        src.Should().NotContain("\"  Legend:\"").And.NotContain("\\u001b[92m#\\u001b[0m Cleared");
    }

    // ---------- 8. the settlements ----------

    private static DungeonSettlement Settlement()
    {
        // The longest settlement and NPC names from the data, with every trade good on sale.
        var all = DungeonSettlementData.Settlements.Values.ToList();
        return new DungeonSettlement
        {
            Id = "dungeon_b_test", Name = all.OrderByDescending(s => s.Name.Length).First().Name,
            NPCName = all.OrderByDescending(s => s.NPCName.Length).First().NPCName, HasHealing = true, HasTrading = true,
            HealEffectiveness = 1f,
            TradeItems = new[] { "Healing Potion", "Mana Potion", "Antidote", "Healing Herb", "Starbloom Essence", "Firebloom Petal", "Torch", "Lockpick", "Smoke Bomb" },
        };
    }

    private static readonly string[] TradeKeys =
    {
        "street_encounter.merchant.item.healing_potion", "world_boss.mana_potion", "street_encounter.merchant.item.antidote",
        "herb.healing_herb.name", "herb.starbloom_essence.name", "herb.firebloom_petal.name",
        "dungeon.settlement_item_torch", "dungeon.settlement_item_lockpick", "dungeon.settlement_item_smoke_bomb",
    };

    [Theory]
    [InlineData("en")]
    [InlineData("hu")]
    public async Task SettlementTrade_RendersInLanguage_AndFits(string lang)
    {
        var settlement = Settlement();
        string text = await InLanguage(lang, async () =>
        {
            var (term, output) = Term("9\n0\n");
            var hero = Hero();
            var d = Dungeon(term, hero, 100);
            await Run(d, "SettlementTrade", hero, settlement);
            return Shown(term, output);
        });
        Capture($"dungeon-b-settlement-trade-{lang}.txt", text);
        text.Should().Contain(L(lang, "dungeon.settlement_wares", settlement.NPCName));
        foreach (var key in TradeKeys) text.Should().Contain("] " + L(lang, key), key);
        text.Should().Contain("[0] " + L(lang, "ui.done_shopping"));
        text.Should().Contain(L(lang, "dungeon.settlement_buy_prompt"));
        text.Should().Contain(L(lang, "church.blood_gold_cost", 35 * 11));
        if (lang == "hu") text.Should().NotContain("Wares").And.NotContain("Buy:").And.NotContain("Enchanted Torch").And.NotContain(" gold)");
        EveryRowFits(text, "settlement trade");
    }

    [Theory]
    [InlineData("en")]
    [InlineData("hu")]
    public async Task SettlementHeal_CostRendersInLanguage_AndFits(string lang)
    {
        var settlement = Settlement();
        string text = await InLanguage(lang, async () =>
        {
            var (term, output) = Term();
            var hero = Hero();
            hero.HP = 1;
            var d = Dungeon(term, hero, 100);
            await Run(d, "SettlementHeal", hero, settlement);
            return Shown(term, output);
        });
        Capture($"dungeon-b-settlement-heal-{lang}.txt", text);
        text.Should().MatchRegex(Regex.Escape(L(lang, "church.blood_gold_cost", "X")).Replace("X", "[0-9]+"));
        if (lang == "hu") text.Should().NotContain(" gold)");
        EveryRowFits(text, "settlement heal");
    }

    // ---------- 9. group broadcasts and the Safe Haven camp ----------

    private static readonly (string key, object[] args)[] Broadcasts =
    {
        ("dungeon.bc_pixie_blessing", new object[] { LongName }),
        ("dungeon.bc_pixie_cursed", new object[] { LongName }),
        ("dungeon.bc_map_revealed", new object[] { LongName }),
        ("dungeon.bc_explorer_robbed", new object[] { LongName, Big }),
        ("dungeon.bc_explorer_rescued", new object[] { LongName, Big }),
        ("dungeon.bc_settlement_arrive", new object[] { "The Bonewright's Forge" }),
        ("dungeon.bc_settlement_healed", new object[] { LongName, "The Bonewright's Forge" }),
        ("dungeon.bc_settlement_lore", new object[] { "Durgan Bonewright" }),
        ("dungeon.bc_safe_haven_rest", Array.Empty<object>()),
        ("dungeon.bc_vision_floor", Array.Empty<object>()),
        ("dungeon.bc_time_warp", new object[] { LongName, Big }),
        ("dungeon.bc_gold_rain", new object[] { LongName, Big }),
    };

    [Fact]
    public void EventBroadcasts_AreHungarian_AndBuiltPerFollower()
    {
        LongName.Length.Should().Be(GameConfig.MaxNameLength, "the check uses the longest player name");
        var sb = new StringBuilder();
        foreach (var (key, args) in Broadcasts)
        {
            Loc.HasIn("hu", key).Should().BeTrue(key);
            string line = "  " + Hu(key, args);
            line.Should().NotBe("  " + L("en", key, args), key);
            sb.AppendLine(line);
        }
        Capture("dungeon-b-broadcasts-hu.txt", sb.ToString());

        string src = Src();
        foreach (var (key, _) in Broadcasts)
            src.Should().Contain($"BroadcastDungeonEvent(lang => $\"\\u001b[", key).And.Contain($"Loc.GetIn(lang, \"{key}\"", key);
        foreach (var english in new[] { "catches a pixie", "angers a pixie", "wounded adventurer", "robs a lost explorer",
                     "rescues a lost explorer", "The party arrives at", "was healed at", "shares knowledge of the depths",
                     "rests in a safe haven", "A vision reveals", "Reality warps!", "Gold coins rain" })
            src.Should().NotContain(english);
        L("en", "dungeon.bc_map_revealed", "X").Should().Be("X receives a map from a wounded adventurer -- dungeon layout revealed!");
    }

    [Fact]
    public void SafeHavenCamp_IsBuiltInTheMatesLanguage_AndFits()
    {
        string src = Src();
        src.Should().Contain("string campLang = session?.Context?.Language ?? \"en\";");
        src.Should().Contain("Loc.GetIn(campLang, \"dungeon.camp_header\")").And.NotContain("Safe Haven Camp");
        foreach (var lang in new[] { "en", "hu" })
        {
            string text = $"  ═══ {L(lang, "dungeon.camp_header")} ═══\n" +
                $"  +{Big} {L(lang, "dungeon.hp_label")}  +{Big} {L(lang, "dungeon.mp_label")}  +{Big} {L(lang, "stats.sta")}";
            Capture($"dungeon-b-camp-{lang}.txt", text);
            EveryRowFits(text, "safe haven camp");
        }
        L("en", "dungeon.camp_header").Should().Be("Safe Haven Camp");
        Hu("dungeon.camp_header").Should().NotBe("Safe Haven Camp");
    }

    // ---------- 10. the Seal news line ----------

    [Theory]
    [InlineData("en")]
    [InlineData("hu")]
    public void SealNews_IsInLanguage_AndFits(string lang)
    {
        string line = L(lang, "dungeon.news_seal_found", LongName, 7);
        Capture($"dungeon-b-seal-news-{lang}.txt", line);
        line.Length.Should().BeLessOrEqualTo(MaxWidth, line);
        if (lang == "hu") line.Should().NotContain("has discovered");
        Src().Should().Contain("Loc.Get(\"dungeon.news_seal_found\", sealFinderName, sealCount)").And.NotContain("has discovered an ancient Seal");
    }

    // ---------- 11. the divine punishment's combat penalties ----------

    [Theory]
    [InlineData("en")]
    [InlineData("hu")]
    public async Task DivinePenalties_RenderInLanguage_AndFit(string lang)
    {
        string text = "";
        for (int seed = 0; seed < 50 && !text.Contains(L(lang, "dungeon.divine_combat_penalties", -30, -30)); seed++)
        {
            text = await InLanguage(lang, async () =>
            {
                var (term, output) = Term("\n\n\n\n");
                var hero = Hero();
                hero.DivineWrathPending = true;
                hero.DivineWrathLevel = 3;
                var d = Dungeon(term, hero, 100);
                typeof(DungeonLocation).GetField("dungeonRandom", F)!.SetValue(d, new Random(seed));
                await (Task)typeof(DungeonLocation).GetMethod("CheckDivinePunishment", F)!.Invoke(d, new object[] { hero })!;
                return Shown(term, output);
            });
        }
        Capture($"dungeon-b-divine-penalties-{lang}.txt", text);
        text.Should().Contain(L(lang, "dungeon.divine_combat_penalties", -30, -30));
        if (lang == "hu") text.Should().NotContain("Combat penalties");
        EveryRowFits(text, "divine punishment");
    }

    // ---------- 12. the reward share notices ----------

    [Theory]
    [InlineData("en")]
    [InlineData("hu")]
    public void RewardShares_AreInTheMatesLanguage_AndFit(string lang)
    {
        string source = "Lost Explorer Rescue";
        string text = string.Join("\n",
            $"  ═══ {L(lang, "dungeon.feature_your_share", source)} ═══",
            $"  {L(lang, "feature.reward_gold_plus", $"{Big:N0}")}  {L(lang, "feature.plus_xp", $"{Big:N0}")}",
            $"  {L(lang, "dungeon.share_gold_xp", $"{Big:N0}", $"{Big:N0}", " (50%)")}",
            L(lang, "dungeon.gold_split", 5, Big));
        Capture($"dungeon-b-shares-{lang}.txt", text);
        EveryRowFits(text, "reward share");
        if (lang == "hu") text.Should().NotContain("Your Share").And.NotContain("Gold split").And.NotContain("Gold:");
        if (lang == "en") L("en", "dungeon.share_gold_xp", "1", "2", " (50%)").Should().Be("Gold: +1  XP: +2 (50%)");

        string src = Src();
        src.Should().NotContain("(Your Share)").And.NotContain("Gold split").And.NotContain("  Gold: +{goldPerMember");
        src.Should().Contain("string shareLang = session?.Context?.Language ?? \"en\";")
            .And.Contain("string shareLang = session.Context?.Language ?? \"en\";");
    }

    // ---------- 13. the follower room view status tags ----------

    [Theory]
    [InlineData("en")]
    [InlineData("hu")]
    public void FollowerRoomView_TagsInTheFollowersLanguage_AndFit(string lang)
    {
        var boss = new DungeonRoom { Id = "b1", Name = new string('B', 52), Description = "A throne room.", DangerRating = 3, IsBossRoom = true, HasMonsters = true, IsExplored = true };
        var cleared = new DungeonRoom { Id = "c1", Name = "Crypt", Description = "A crypt.", DangerRating = 3, IsExplored = true, IsCleared = true };
        var danger = new DungeonRoom { Id = "d1", Name = "Pit", Description = "A pit.", DangerRating = 3, IsExplored = true, HasMonsters = true };
        var floor = new DungeonFloor { Level = 100, Theme = DungeonTheme.AbyssalVoid, CurrentRoomId = "b1" };
        floor.Rooms.AddRange(new[] { boss, cleared, danger });
        var (term, _) = Term();
        var d = Dungeon(term, Hero(), 100, floor);
        var build = typeof(DungeonLocation).GetMethod("BuildRoomAnsi", F)!;
        var views = new[] { boss, cleared, danger }.Select(r => Strip((string)build.Invoke(d, new object[] { r, "leader", lang })!)).ToList();
        string text = string.Join("\n", views);
        Capture($"dungeon-b-follower-room-{lang}.txt", text);
        views[0].Should().Contain(L(lang, "dungeon.tag_boss"));
        views[1].Should().Contain(" " + L(lang, "dungeon.tag_cleared"));
        views[2].Should().Contain(" " + L(lang, "dungeon.tag_danger"));
        if (lang == "en") views[0].Should().Contain("*** [BOSS]", "the English row is unchanged");
        if (lang == "hu") text.Should().NotContain("[BOSS]").And.NotContain("[CLEARED]").And.NotContain("[DANGER]");
        EveryRowFits(text, "follower room view");
        Src().Should().Contain("roomAnsiByLang[lang] = roomAnsi = BuildRoomAnsi(room, ctx.Username, lang);");
    }

    // ---------- 14. the group notices, built per member and wrapped ----------

    private static readonly (string key, object[] args, string color)[] Notices =
    {
        ("dungeon.grp_leader_entered", new object[] { LongName, 100 }, "\u001b[1;33m"),
        ("dungeon.grp_go_join", Array.Empty<object>(), "\u001b[1;33m"),
        ("dungeon.grp_joined_your", new object[] { LongName }, "\u001b[1;32m"),
        ("dungeon.grp_entered_with", new object[] { LongName }, "\u001b[1;32m"),
        ("dungeon.grp_member_left", new object[] { LongName }, "\u001b[1;33m"),
        ("dungeon.grp_leader_left", Array.Empty<object>(), "\u001b[1;33m"),
        ("dungeon.grp_run_over", new object[] { LongName }, "\u001b[1;33m"),
        ("group.follower_left_dead", new object[] { LongName }, "\u001b[1;31m"),
    };

    [Theory]
    [InlineData("en")]
    [InlineData("hu")]
    public void GroupNotices_RenderInLanguage_AndFitWhenWrapped(string lang)
    {
        var sb = new StringBuilder();
        foreach (var (key, args, color) in Notices)
            sb.AppendLine(Strip(DungeonLocation.FollowerMessage(l => $"{color}  {L(l, key, args)}\u001b[0m")(lang)));
        string text = sb.ToString();
        Capture($"dungeon-b-group-notices-{lang}.txt", text);
        EveryRowFits(text, "group notice");
        if (lang == "hu") text.Should().NotContain("has left the dungeon").And.NotContain("Your group leader");

        string src = Src();
        foreach (var key in new[] { "dungeon.grp_leader_entered", "dungeon.grp_go_join", "dungeon.grp_entered_with", "dungeon.grp_run_over" })
            src.Should().Contain($"Loc.GetIn(lang, \"{key}\"", key);
        src.Should().Contain("Loc.GetIn(leaderLang, \"dungeon.grp_member_left\"").And.Contain("Loc.GetIn(leaderLang, \"group.follower_left_dead\"");
        src.Should().Contain("(leaderSession.Context?.Language ?? \"en\")");
        src.Should().Contain("Loc.GetIn(followerSession.Context?.Language ?? \"en\", \"dungeon.grp_leader_left\")");
    }

    [Theory]
    [InlineData("en")]
    [InlineData("hu")]
    public void FollowerTerminalLines_AreInLanguage_AndFit(string lang)
    {
        var lines = new[]
        {
            "  " + L(lang, "dungeon.grp_stays_behind", "Seraphina Lightbringer"),
            "  " + L(lang, "dungeon.grp_no_connect"),
            "  " + L(lang, "dungeon.grp_leader_not_in"),
            "  " + L(lang, "dungeon.grp_party_full"),
            "  " + L(lang, "dungeon.grp_you_join", LongName),
            "  " + L(lang, "dungeon.follower_unknown_cmd", "abcdefghijklmnop"),
        };
        string text = string.Join("\n", lines);
        Capture($"dungeon-b-follower-lines-{lang}.txt", text);
        EveryRowFits(text, "follower lines");
        if (lang == "hu") text.Should().NotContain("group leader").And.NotContain("Unknown command");
    }

    // ---------- 15. the follower help, quests, inventory and potions ----------

    private static Character Follower()
    {
        var p = new Character { Name1 = LongName, Name2 = LongName, Class = CharacterClass.Magician, Level = 100, HP = 10, MaxHP = Big,
            Mana = 10, MaxMana = Big, Gold = 999_999_999, AI = CharacterAI.Human };
        p.Healing = 20; p.ManaPotions = 20;
        return p;
    }

    private static async Task<string> FollowerCommand(string lang, string command, Character player)
    {
        return await InLanguage(lang, async () =>
        {
            var (term, output) = Term();
            var leader = Dungeon(Term().term, Hero(), 100);
            var m = typeof(DungeonLocation).GetMethod("ProcessFollowerSlashCommand", FS)!;
            bool handled = await (Task<bool>)m.Invoke(null, new object[] { command, player, term, leader })!;
            handled.Should().BeTrue(command);
            return Shown(term, output);
        });
    }

    [Theory]
    [InlineData("en")]
    [InlineData("hu")]
    public async Task FollowerHelp_RendersInLanguage_AndFits(string lang)
    {
        string text = await FollowerCommand(lang, "/help", Follower());
        Capture($"dungeon-b-follower-help-{lang}.txt", text);
        text.Should().Contain($"═══ {L(lang, "dungeon.follower_help_title")} ═══");
        foreach (var key in new[] { "dungeon.follower_help_inventory", "dungeon.follower_help_potion_any", "dungeon.follower_help_status",
                     "dungeon.leave_dungeon", "dungeon.follower_help_attack", "combat.defend", "dungeon.follower_help_potion", "combat.bbs_retreat" })
            text.Should().Contain("  - " + L(lang, key), key);
        text.Should().Contain(" - " + L(lang, "dungeon.follower_help_quickbar"));
        text.Should().Contain("  " + L(lang, "dungeon.follower_help_between")).And.Contain("  " + L(lang, "dungeon.follower_help_in_combat"));
        text.Should().Contain("    /party  /stats  /health  /gold  /quests", "slash command names are what the player types");
        if (lang == "hu") text.Should().NotContain("FOLLOWER COMMANDS").And.NotContain("Between combats").And.NotContain("Retreat");
        EveryRowFits(text, "follower help");
    }

    [Theory]
    [InlineData("en")]
    [InlineData("hu")]
    public async Task FollowerQuests_RenderInLanguage_AndFit(string lang)
    {
        string text = await FollowerCommand(lang, "/quests", Follower());
        Capture($"dungeon-b-follower-quests-{lang}.txt", text);
        text.Should().Contain("  " + L(lang, "dungeon.follower_no_quests"));
        string more = $"  {L(lang, "quest_hall.active")} ({Big}):\n  {L(lang, "shop.filter_more", Big)}";
        EveryRowFits(text + "\n" + more, "follower quests");
        if (lang == "hu") text.Should().NotContain("No active quests");
        L("en", "shop.filter_more", 3).Should().Be("  ... and 3 more");
    }

    // Spanish too: its "Mano secundaria" is longer than ten columns, so the slot column widens.
    [Theory]
    [InlineData("en")]
    [InlineData("hu")]
    [InlineData("es")]
    public async Task FollowerInventory_RendersInLanguage_AndFits(string lang)
    {
        // Every slot holds the equipment with the longest name; the backpack holds it too.
        var longest = EquipmentDatabase.GetAll().OrderByDescending(e => e.Name.Length).First();
        var player = Follower();
        foreach (var slot in new[] { EquipmentSlot.MainHand, EquipmentSlot.OffHand, EquipmentSlot.Head, EquipmentSlot.Body, EquipmentSlot.Arms,
                     EquipmentSlot.Hands, EquipmentSlot.Legs, EquipmentSlot.Feet, EquipmentSlot.Cloak, EquipmentSlot.Waist, EquipmentSlot.Neck,
                     EquipmentSlot.LFinger, EquipmentSlot.RFinger })
            player.EquippedItems[slot] = longest.Id;
        player.Inventory.Add(new Item { Name = longest.Name });
        string text = await InLanguage(lang, async () =>
        {
            var (term, output) = Term("U\n\n\n");
            await (Task)typeof(DungeonLocation).GetMethod("ShowFollowerInventory", FS)!.Invoke(null, new object[] { player, term })!;
            return Shown(term, output);
        });
        Capture($"dungeon-b-follower-inventory-{lang}.txt", text);
        text.Should().Contain($"═══ {L(lang, "inventory.title")} ═══");
        text.Should().Contain("  " + L(lang, "magic_shop.equipped_label")).And.Contain("  " + L(lang, "dungeon.follower_backpack"));
        foreach (var key in new[] { "inn.equip_slot_weapon", "inn.equip_slot_off_hand", "ui.waist", "dungeon.slot_l_ring", "dungeon.slot_r_ring" })
            text.Should().Contain("    " + L(lang, key), key);
        text.Should().Contain("  " + L(lang, "dungeon.follower_inv_totals", "999,999,999", 20, 20));
        text.Should().Contain("  " + L(lang, "dungeon.follower_choose_unequip")).And.Contain("  " + L(lang, "dungeon.follower_unequip_prompt"));
        // The slot column: at least ten wide, and wider where this language's longest slot name needs it.
        var slotNames = new[] { "inn.equip_slot_weapon", "inn.equip_slot_off_hand", "ui.head", "ui.body", "ui.arms", "ui.hands", "ui.legs",
            "ui.feet", "ui.cloak", "ui.waist", "ui.neck", "dungeon.slot_l_ring", "dungeon.slot_r_ring" }.Select(k => L(lang, k)).ToList();
        int width = Math.Max(10, slotNames.Max(n => n.Length) + 1);
        if (lang == "es") width.Should().BeGreaterThan(10, "a Spanish slot name runs past ten columns");
        // v1.2.5: the item shows in the reader's language (ItemNames), its stored name stays English
        string shown = Loc.GetIn(lang, LootGenerator.TemplateLocKey(longest.Name));
        if (lang != "en") shown.Should().NotBe(longest.Name);
        text.Should().Contain("    " + L(lang, "inn.equip_slot_weapon").PadRight(width) + shown);
        text.Should().Contain("    1. " + shown);
        if (lang == "en") text.Should().Contain("    Weapon    " + longest.Name, "the English column is ten wide as before");
        if (lang == "hu") text.Should().NotContain("Equipped:").And.NotContain("Backpack:").And.NotContain("L.Ring").And.NotContain("HP Potions");
        EveryRowFits(text, "follower inventory");

        var other = new[]
        {
            "    " + L(lang, "dungeon.follower_nothing_equipped"),
            "  " + L(lang, "inventory.backpack_empty"),
            "  " + L(lang, "inventory.cannot_equip", "You need level 100 to use this."),
            "  " + L(lang, "dungeon.follower_cannot_be_equipped", shown),
            "  " + L(lang, "dungeon.follower_unequipped", shown),
            "  " + L(lang, "dungeon.follower_cannot_unequip"),
        };
        EveryRowFits(string.Join("\n", other), "follower inventory messages");
    }

    [Theory]
    [InlineData("en")]
    [InlineData("hu")]
    public async Task FollowerPotions_RenderInLanguage_AndFit(string lang)
    {
        var m = typeof(DungeonLocation).GetMethod("UseFollowerPotion", FS)!;
        string both = await InLanguage(lang, async () =>
        {
            var (term, output) = Term("M\n");
            await (Task)m.Invoke(null, new object[] { Follower(), term })!;
            return Shown(term, output);
        });
        string none = await InLanguage(lang, async () =>
        {
            var (term, output) = Term();
            var p = Follower(); p.Healing = 0; p.ManaPotions = 0;
            await (Task)m.Invoke(null, new object[] { p, term })!;
            return Shown(term, output);
        });
        string full = await InLanguage(lang, async () =>
        {
            var (term, output) = Term();
            var p = Follower(); p.HP = p.MaxHP; p.Mana = p.MaxMana;
            await (Task)m.Invoke(null, new object[] { p, term })!;
            return Shown(term, output);
        });
        string text = both + "\n" + none + "\n" + full;
        Capture($"dungeon-b-follower-potions-{lang}.txt", text);
        text.Should().Contain("  " + L(lang, "dungeon.follower_potion_choice", 20, 20));
        text.Should().Contain("  " + L(lang, "dungeon.choose"));
        text.Should().MatchRegex(Regex.Escape("  " + L(lang, "combat.drink_mana_potion", "X")).Replace("X", "[0-9,]+"));
        text.Should().MatchRegex(Regex.Escape("  " + L(lang, "dungeon.follower_mana_line", "X", Big, 19)).Replace("X", "[0-9]+"), "the mana line");
        text.Should().Contain("  " + L(lang, "dungeon.follower_no_potions")).And.Contain("  " + L(lang, "dungeon.follower_hp_mana_full"));
        if (lang == "hu") text.Should().NotContain("Healing potion").And.NotContain("You have no potions").And.NotContain("Mana Potions:");
        EveryRowFits(text + "\n  " + L(lang, "dungeon.follower_mana_line", Big, Big, Big), "follower potions");
    }

    // ---------- 16. every dungeon broadcast wraps to 79 columns in every language ----------

    private static readonly Dictionary<string, object[]> BroadcastArgs = new()
    {
        ["dungeon.bc_trap"] = Array.Empty<object>(),
        ["dungeon.bc_trap_evaded"] = new object[] { LongName },
        ["dungeon.bc_trap_pit"] = new object[] { LongName, Big },
        ["dungeon.bc_trap_darts"] = new object[] { LongName, Big },
        ["dungeon.bc_trap_fire"] = new object[] { LongName, Big },
        ["dungeon.bc_trap_acid"] = new object[] { LongName, Big },
        ["dungeon.bc_trap_curse_resisted"] = new object[] { LongName },
        ["dungeon.bc_trap_curse_drain"] = new object[] { LongName, Big },
        ["dungeon.bc_trap_salvage"] = new object[] { LongName, Big },
        ["dungeon.bc_boss_encounter"] = new object[] { "3 Skeletons, Bone Lord" },
        ["dungeon.bc_combat"] = new object[] { "3 Skeletons, 2 Goblins" },
        ["dungeon.bc_party_treasure"] = Array.Empty<object>(),
        ["dungeon.bc_descends"] = new object[] { 100, DungeonTheme.AncientRuins },
        ["dungeon.bc_chest_opened"] = Array.Empty<object>(),
        ["dungeon.bc_chest_trapped"] = Array.Empty<object>(),
        ["dungeon.bc_chest_mimic"] = Array.Empty<object>(),
        ["dungeon.bc_shrine_healed"] = new object[] { LongName },
        ["dungeon.bc_shrine_strength"] = new object[] { LongName, Big },
        ["dungeon.bc_shrine_exp"] = new object[] { LongName, Big },
        ["dungeon.bc_shrine_nothing"] = new object[] { LongName },
        ["dungeon.bc_shrine_hp"] = new object[] { LongName, Big },
        ["dungeon.bc_shrine_gold"] = new object[] { LongName, Big },
        ["dungeon.bc_pixie_blessing"] = new object[] { LongName },
        ["dungeon.bc_pixie_cursed"] = new object[] { LongName },
        ["dungeon.bc_map_revealed"] = new object[] { LongName },
        ["dungeon.bc_explorer_robbed"] = new object[] { LongName, Big },
        ["dungeon.bc_explorer_rescued"] = new object[] { LongName, Big },
        ["dungeon.bc_settlement_arrive"] = new object[] { "The Bonewright's Forge" },
        ["dungeon.bc_settlement_healed"] = new object[] { LongName, "The Bonewright's Forge" },
        ["dungeon.bc_settlement_lore"] = new object[] { "Durgan Bonewright" },
        ["dungeon.bc_safe_haven_rest"] = Array.Empty<object>(),
        ["dungeon.bc_vision_floor"] = Array.Empty<object>(),
        ["dungeon.bc_time_warp"] = new object[] { LongName, Big },
        ["dungeon.bc_gold_rain"] = new object[] { LongName, Big },
    };

    public static IEnumerable<object[]> Languages() => new[] { "en", "es", "fr", "hu", "it" }.Select(l => new object[] { l });

    [Theory]
    [MemberData(nameof(Languages))]
    public void EveryDungeonBroadcast_WrapsTo79_AndKeepsItsText(string lang)
    {
        LongName.Length.Should().Be(GameConfig.MaxNameLength, "the check uses the longest player name");
        var enJson = System.Text.Json.JsonDocument.Parse(File.ReadAllText(
            Path.Combine(HardcodedTextScannerTests.RepoRoot(), "Localization", "en.json")));
        var bcKeys = enJson.RootElement.EnumerateObject().Select(p => p.Name).Where(k => k.StartsWith("dungeon.bc_")).ToList();
        bcKeys.Should().HaveCountGreaterOrEqualTo(34);
        bcKeys.Should().BeSubsetOf(BroadcastArgs.Keys, "every dungeon broadcast key has worst case arguments here");

        var sb = new StringBuilder();
        int wrapped = 0;
        foreach (var key in bcKeys)
        {
            string raw = $"\u001b[33m  {L(lang, key, BroadcastArgs[key])}\u001b[0m";
            string sent = DungeonLocation.FollowerMessage(l => $"\u001b[33m  {L(l, key, BroadcastArgs[key])}\u001b[0m")(lang);
            var rows = sent.Split('\n');
            if (rows.Length > 1) wrapped++;
            foreach (var row in rows)
            {
                row.Should().StartWith("\u001b[33m  ", $"{key} keeps its color and indent on every row");
                Strip(row).Length.Should().BeLessOrEqualTo(MaxWidth, $"{key} in {lang}: \"{Strip(row)}\"");
                sb.AppendLine(Strip(row));
            }
            string.Join(" ", rows.Select(r => Strip(r).Substring(2))).Should().Be(Strip(raw).Substring(2), $"{key} keeps its text");
        }
        Capture($"dungeon-b-broadcasts-wrapped-{lang}.txt", sb.ToString());
        if (lang == "en") wrapped.Should().BeGreaterThan(0, "at the longest name some English broadcasts need a second row");
    }

    [Fact]
    public void BroadcastOverload_SendsTheWrappedMessage()
    {
        Src().Should().Contain("BroadcastToAllGroupSessionsLocalized(group, FollowerMessage(buildMessage),");
        // A row that fits is sent as it is, double spaces and all.
        string fits = "\u001b[32m  +5 HP  +3 MP\u001b[0m";
        DungeonLocation.WrapBroadcast(fits).Should().Be(fits);
        // A long row wraps; the color active at the break carries to the next row.
        string mixed = "\u001b[33m  " + string.Join(" ", Enumerable.Repeat("gold", 12)) + " \u001b[36m" + string.Join(" ", Enumerable.Repeat("cyan", 12)) + "\u001b[0m";
        var rows = DungeonLocation.WrapBroadcast(mixed).Split('\n');
        rows.Should().HaveCount(2);
        rows[1].Should().StartWith("\u001b[36m  cyan");
        rows[0].Should().EndWith("\u001b[0m");
    }
}
