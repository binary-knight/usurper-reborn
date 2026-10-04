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
using Microsoft.Data.Sqlite;
using System.Text.Json.Nodes;
using UsurperRemake;
using UsurperRemake.Locations;
using UsurperRemake.Systems;
using UsurperReborn.Tests.Localization;
using Xunit;

namespace UsurperReborn.Tests;

/// <summary>
/// v1.2.5 leftovers (D11). Part A, saved text stored in English (or as a key) and shown in each reader's
/// language, with what was saved before kept and shown as before: the three Inn memories, the bounty board
/// comment, the boss quest champion, the special fights' monster names, loot descriptions and the curse line.
/// Part B, display only: stored English shown in the reader's language. Part C, dead code removed.
/// </summary>
[Collection("SharedGameSingletons")]
public class Leftovers125Tests
{
    private const BindingFlags F = BindingFlags.NonPublic | BindingFlags.Instance;
    private const BindingFlags FS = BindingFlags.NonPublic | BindingFlags.Static;
    private const int MaxWidth = 79;

    // GameConfig.MaxNameLength (30) characters.
    private const string LongName = "Aranyszivu Hosszunevu Kalandor";

    private static readonly string[] AllLanguages = { "en", "es", "fr", "hu", "it" };

    // ---------- helpers ----------

    private static T InLang<T>(string lang, Func<T> body)
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

    private static string Src(params string[] path) =>
        File.ReadAllText(Path.Combine(new[] { HardcodedTextScannerTests.RepoRoot(), "Scripts" }.Concat(path).ToArray()));

    private static List<string> Rows(string text) => text.Replace("\r", "").Split('\n').ToList();

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

    private static Screen At(BaseLocation location, Character hero, params string[] lines)
    {
        var s = NewScreen(lines);
        typeof(BaseLocation).GetField("terminal", F)!.SetValue(location, s.Term);
        typeof(BaseLocation).GetField("currentPlayer", F)!.SetValue(location, hero);
        return s;
    }

    private static async Task Run(object target, string method, params object?[] args)
    {
        var r = target.GetType().GetMethod(method, F)!.Invoke(target, args);
        if (r is Task t) await t;
    }

    private static void EveryRowFits(string text, string screen)
    {
        foreach (var row in Rows(text))
            if (row.Length > 0 && "╔║╚╠".IndexOf(row[0]) >= 0)
                row.Length.Should().BeLessOrEqualTo(MaxWidth + 1, $"the {screen} box keeps its width: \"{row}\"");
            else
                row.Length.Should().BeLessOrEqualTo(MaxWidth, $"every row of the {screen} fits in {MaxWidth} columns: \"{row}\"");
    }

    /// <summary>The Hungarian text holds none of the English text of these keys (each literal piece of 5 letters
    /// or more of the English value, unless the Hungarian value has it too).</summary>
    private static void NoEnglishLeft(string huText, IEnumerable<string> keys)
    {
        foreach (var key in keys)
        {
            string en = Loc.GetIn("en", key), hu = Loc.GetIn("hu", key);
            foreach (var piece in Regex.Split(en, @"\{\d+[^}]*\}"))
                foreach (Match w in Regex.Matches(piece, @"[A-Za-z][A-Za-z' ]{4,}[A-Za-z]"))
                    if (!hu.Contains(w.Value)) huText.Should().NotContain(w.Value, $"{key} is shown in Hungarian");
        }
    }

    [Fact]
    public void MaxNameLength_IsThirty() => LongName.Length.Should().Be(GameConfig.MaxNameLength);

    // ====================================================================================================
    // A. bug 7: the three Inn memories are stored in English
    // ====================================================================================================

    [Fact]
    public void InnMemories_AreWrittenInEnglish_WhateverThePlayersLanguage()
    {
        string inn = Src("Locations", "InnLocation.cs");
        foreach (var key in new[] { "inn.memory_defeated_duel", "inn.memory_murdered_sleep", "inn.memory_witnessed_murder" })
        {
            var rows = Rows(inn).Where(r => r.Contains($"\"{key}\"")).ToList();
            rows.Should().ContainSingle(key);
            rows[0].Should().Contain($"Description = Loc.GetIn(\"en\", \"{key}\"", "the memory is stored in English");
        }
        // nothing in the Inn writes a memory in the session's language
        Regex.IsMatch(inn, @"Description = Loc\.Get\(""inn\.memory_").Should().BeFalse();
        // the English text is what a Hungarian session now stores
        InLang("hu", () => Loc.GetIn("en", "inn.memory_murdered_sleep", LongName)).Should().Be($"Murdered in my sleep at the Inn by {LongName}");
    }

    [Fact]
    public void WitnessMatch_StillHitsTheEnglishWitnessText_AndInnMemoriesDoNotDisturbIt()
    {
        var npc = new NPC { Name1 = "Witness", Name2 = "Witness", Archetype = "citizen" };
        npc.EnsureSystemsInitialized();
        npc.Brain.Should().NotBeNull();
        var hero = new Character { Name1 = "Hero", Name2 = "Hero" };
        var method = typeof(DialogueEnhancer).GetMethod("GetWitnessFlavor", FS)!;

        // an Inn memory (SawDeath) is not a witnessed event; alone it gives no witness line
        npc.Brain!.Memory.RecordEvent(new MemoryEvent
        {
            Type = MemoryType.SawDeath, Description = Loc.GetIn("en", "inn.memory_witnessed_murder", "Hero", "Vex"),
            InvolvedCharacter = "Hero", Importance = 0.8f, EmotionalImpact = -0.6f
        });
        InLang("hu", () => method.Invoke(null, new object[] { npc, hero })).Should().BeNull();

        // the English "Saw {actor} ..." witness memory still matches with the Inn memory beside it
        npc.Brain.Memory.RecordEvent(new MemoryEvent
        {
            Type = MemoryType.WitnessedEvent, Description = "Saw Hero murder Vex",
            InvolvedCharacter = "Hero", Importance = 0.9f, EmotionalImpact = -0.6f
        });
        InLang("hu", () => method.Invoke(null, new object[] { npc, hero })).Should().NotBeNull();
    }

    [Fact]
    public void AnOldNonEnglishInnMemory_IsSavedAndLoadedAsStored()
    {
        string hu = L("hu", "inn.memory_murdered_sleep", LongName);
        hu.Should().NotBe(L("en", "inn.memory_murdered_sleep", LongName));
        var npc = new NPC { Name1 = "Sleeper", Name2 = "Sleeper", Archetype = "citizen" };
        npc.EnsureSystemsInitialized();
        npc.Brain!.Memory.RecordEvent(new MemoryEvent { Type = MemoryType.Murdered, Description = hu, InvolvedCharacter = LongName, Importance = 1f, EmotionalImpact = -1f });

        var serialize = typeof(SaveSystem).GetMethod("SerializeMemories", F)!;
        var data = (List<MemoryData>)serialize.Invoke(SaveSystem.Instance, new object?[] { npc.Brain.Memory })!;
        string json = JsonSerializer.Serialize(data);
        var loaded = JsonSerializer.Deserialize<List<MemoryData>>(json)!;
        loaded.Single(m => m.Type == "Murdered").Description.Should().Be(hu, "an old memory keeps its text");
    }

    // ====================================================================================================
    // A. bug 10: the bounty board comment is stored as a key and shown per reader
    // ====================================================================================================

    [Fact]
    public void BountyComment_IsStoredAsAKey_AndShownInEachReadersLanguage()
    {
        var q = InLang("hu", () => QuestSystem.CreateDungeonQuest(QuestTarget.ReachFloor, 1, null, 10, 5));
        try
        {
            q.CommentKey.Should().Be("quest.dungeon_quest_comment");
            q.CommentArgs.Should().Equal("loc:quest.dungeon_name");
            q.Comment.Should().Be(L("en", "quest.dungeon_quest_comment", L("en", "quest.dungeon_name")), "the stored text is English");
            foreach (var lang in AllLanguages)
                InLang(lang, q.GetDisplayComment).Should().Be(L(lang, "quest.dungeon_quest_comment", L(lang, "quest.dungeon_name")), lang);

            // the board's own refresh passes the default name the same way
            Src("Systems", "QuestSystem.cs").Should().Contain("CreateDungeonQuest(questType, difficulty, null, playerLevel, deepestFloor);");
            // a dungeon named by the caller is kept as given
            var named = InLang("fr", () => QuestSystem.CreateDungeonQuest(QuestTarget.ReachFloor, 1, "Old Mine", 10, 5));
            named.CommentArgs.Should().Equal("Old Mine");
            InLang("hu", named.GetDisplayComment).Should().Be(L("hu", "quest.dungeon_quest_comment", "Old Mine"));
            named.Deleted = true;
        }
        finally { q.Deleted = true; }
    }

    [Fact]
    public void ABountyQuestSavedBeforeTheFix_ShowsItsCommentAsSaved()
    {
        string huComment = L("hu", "quest.dungeon_quest_comment", L("hu", "quest.dungeon_name"));
        var saved = new QuestData
        {
            Id = "D11OLDBOUNTY", Title = "x", Initiator = QuestSystem.BountyBoardInitiator, Comment = huComment,
            QuestTarget = (int)QuestTarget.ReachFloor, Difficulty = 1, MinLevel = 1, MaxLevel = 99
        };
        var keep = QuestSystem.GetAllQuests(true).ToList();
        try
        {
            QuestSystem.RestoreFromSaveData(new List<QuestData> { saved });
            var q = QuestSystem.GetAllQuests(true).Single(x => x.Id == "D11OLDBOUNTY");
            q.CommentKey.Should().BeEmpty();
            InLang("en", q.GetDisplayComment).Should().Be(huComment);
            InLang("hu", q.GetDisplayComment).Should().Be(huComment);
        }
        finally { RestoreQuests(keep); }
    }

    private static void RestoreQuests(List<Quest> quests)
    {
        QuestSystem.ClearAllQuests();
        foreach (var q in quests) QuestSystem.AddQuestToDatabase(q);
    }

    // ====================================================================================================
    // A. bug 12: the boss quest champion is stored in English
    // ====================================================================================================

    [Fact]
    public void BossQuestChampion_IsStoredInEnglish_AndShownInTheReadersLanguage()
    {
        var boss = InLang("hu", () => QuestSystem.CreateDungeonQuest(QuestTarget.ClearBoss, 2, null, 20));
        try
        {
            var kill = boss.Objectives.Single(o => o.ObjectiveType == QuestObjectiveType.KillBoss);
            string tier = TierOf(kill.TargetId);
            string english = L("en", "quest.title.champion", tier);
            kill.TargetName.Should().Be(english);
            kill.DescriptionArgs.Should().Equal(english);
            boss.TitleKey.Should().Be("quest.title.defeat_boss");
            boss.TitleArgs.Should().Equal(english);
            boss.Title.Should().Be(L("en", "quest.title.defeat_boss", english));
            InLang("hu", boss.GetDisplayTitle).Should().Be(L("hu", "quest.title.defeat_boss", L("hu", "quest.title.champion", MonsterNames.DisplayIn("hu", tier))));
            InLang("en", boss.GetDisplayTitle).Should().Be(L("en", "quest.title.defeat_boss", english));
            InLang("hu", kill.GetDisplayDescription).Should().Be(L("hu", "quest.objective.defeat_boss", L("hu", "quest.title.champion", MonsterNames.DisplayIn("hu", tier))));
        }
        finally { boss.Deleted = true; }
    }

    [Fact]
    public void ABossQuestSavedBeforeTheFix_LoadsUnchanged_AndIsStillRebuiltFromItsTier()
    {
        string tier = "Wolf";
        string frChampion = L("fr", "quest.title.champion", tier);
        frChampion.Should().NotBe(L("en", "quest.title.champion", tier));
        var saved = new QuestData
        {
            Id = "D11OLDBOSS", Title = L("fr", "quest.title.defeat_boss", frChampion), Initiator = QuestSystem.BountyBoardInitiator,
            Comment = "c", TitleKey = "quest.title.defeat_boss", TitleArgs = new List<string> { frChampion },
            QuestTarget = (int)QuestTarget.ClearBoss, Difficulty = 2, MinLevel = 1, MaxLevel = 99,
            Objectives = new List<QuestObjectiveData>
            {
                new() { Id = "o", ObjectiveType = (int)QuestObjectiveType.KillBoss, DescriptionKey = "quest.objective.defeat_boss",
                        DescriptionArgs = new List<string> { frChampion }, Description = "d", RequiredProgress = 1,
                        TargetId = "wolf", TargetName = frChampion }
            }
        };
        var keep = QuestSystem.GetAllQuests(true).ToList();
        try
        {
            QuestSystem.RestoreFromSaveData(new List<QuestData> { saved });
            var q = QuestSystem.GetAllQuests(true).Single(x => x.Id == "D11OLDBOSS");
            q.TitleArgs.Should().Equal(frChampion);
            q.Objectives.Single().TargetName.Should().Be(frChampion, "an old quest is not rewritten");
            InLang("hu", q.GetDisplayTitle).Should().Be(L("hu", "quest.title.defeat_boss", L("hu", "quest.title.champion", MonsterNames.DisplayIn("hu", tier))));
            InLang("en", q.GetDisplayTitle).Should().Be(L("en", "quest.title.defeat_boss", L("en", "quest.title.champion", tier)));
        }
        finally { RestoreQuests(keep); }
    }

    private static string TierOf(string targetId) =>
        MonsterFamilies.GetBuiltInFamilies().SelectMany(f => f.Tiers).First(t => t.Name.ToLower().Replace(" ", "_") == targetId).Name;

    // ====================================================================================================
    // A. bug 13: the special fights' monster names are stored in English, shown per reader
    // ====================================================================================================

    private static Monster KeyedMonster()
    {
        var m = new Monster { Name = "Wolf", TierName = "Wolf", FamilyName = "Beast", Level = 5, HP = 50, MaxHP = 50 };
        MonsterNames.KeyOf("Wolf").Should().NotBeNull();
        return m;
    }

    [Fact]
    public void SpecialFightNames_AreStoredInEnglish_AtEverySite()
    {
        Src("Locations", "DarkAlleyLocation.cs").Should().Contain("monster.Name = MonsterNames.FromKey(\"dark_alley.pit_monster_name\", monster.Name);")
            .And.Contain("mugger.Name = MonsterNames.FromKey(\"dark_alley.mugger_name\");")
            .And.Contain("GameConfig.ArticulateForLanguage(MonsterNames.Display(monster))");
        Src("Systems", "RareEncounters.cs").Should().Contain("champion.Name = MonsterNames.FromKey(\"dungeon.arena_champion_name\", champion.Name);")
            .And.Contain("Loc.Get(\"encounter.arena.opponent\", MonsterNames.Display(champion))");
        Src("Locations", "SanctumLocation.cs").Should().Contain("monster.Name = champion.StoredName();");
        foreach (var file in new[] { Src("Locations", "DarkAlleyLocation.cs"), Src("Systems", "RareEncounters.cs"), Src("Locations", "SanctumLocation.cs") })
            Regex.IsMatch(file, @"\b(monster|mugger|champion)\.Name = (Loc\.Get|champion\.LocName)\(").Should().BeFalse("no ruled monster name is stored in the session's language");
    }

    [Theory]
    [InlineData("dark_alley.pit_monster_name")]
    [InlineData("dungeon.arena_champion_name")]
    public void AWrappedFightName_IsEnglishInside_AndShownWhollyInTheReadersLanguage(string key)
    {
        var m = KeyedMonster();
        m.Name = InLang("hu", () => MonsterNames.FromKey(key, m.Name));
        m.Name.Should().Be(L("en", key, "Wolf"), "stored English in a Hungarian session");
        foreach (var lang in AllLanguages)
            MonsterNames.DisplayIn(lang, m).Should().Be(L(lang, key, L(lang, "monster.name.wolf")), lang);
        MonsterNames.DisplayIn("hu", m).Should().NotContain("Wolf", "the inner name is Hungarian too");
    }

    [Fact]
    public void TheMugger_AndTheSanctumChampions_AreStoredInEnglish_AndShownPerReader()
    {
        var mugger = new Monster { Name = InLang("hu", () => MonsterNames.FromKey("dark_alley.mugger_name")) };
        mugger.Name.Should().Be("Dark Alley Mugger");
        foreach (var lang in AllLanguages) MonsterNames.DisplayIn(lang, mugger).Should().Be(L(lang, "dark_alley.mugger_name"), lang);

        foreach (var champ in UsurperRemake.Data.HonorTournamentData.Champions)
        {
            string stored = InLang("hu", champ.StoredName);
            stored.Should().Be(champ.Name, "the English name");
            foreach (var lang in AllLanguages)
                MonsterNames.DisplayIn(lang, new Monster { Name = stored }).Should().Be(InLang(lang, champ.LocName), $"{champ.Id} {lang}");
        }
    }

    [Fact]
    public void DeathNews_AndTheEulogy_NameTheFoeInTheirReadersLanguage_AndOldNewsStaysAsStored()
    {
        var m = KeyedMonster();
        m.Name = MonsterNames.FromKey("dark_alley.mugger_name");
        var buffer = new List<string>();
        var news = NewsSystem.Instance;
        news.SetCatchUpBuffer(buffer);
        try
        {
            InLang("hu", () => { news.WriteDeathNews(LongName, MonsterNames.Display(m), "Utca"); return 0; });
            InLang("en", () => { news.WriteDeathNews(LongName, MonsterNames.Display(m), "Street"); return 0; });
        }
        finally { news.ClearCatchUpBuffer(); }
        buffer[0].Should().Contain(L("hu", "dark_alley.mugger_name")).And.NotContain("Dark Alley Mugger");
        buffer[1].Should().Contain("Dark Alley Mugger");

        // the permadeath eulogy is built for each reader with the foe through MonsterNames.DisplayIn
        Src("Systems", "CombatEngine.cs").Should().Contain("string KillerIn(string lang) => killerMonster != null ? MonsterNames.DisplayIn(lang, killerMonster) : killerName;");
        foreach (var lang in AllLanguages)
            Loc.GetIn(lang, "permadeath.eulogy", LongName, 50, "Warrior", MonsterNames.DisplayIn(lang, m)).Should().Contain(L(lang, "dark_alley.mugger_name"), lang);

        // a news row written before the fix with a Hungarian foe is shown as it was stored
        string oldRow = "[12:00] " + L("hu", "news.death", LongName, L("hu", "dark_alley.mugger_name"), "Utca");
        var cache = (List<string>)typeof(NewsSystem).GetField("_todaysNews", F)!.GetValue(news)!;
        cache.Add(oldRow);
        try { InLang("en", news.GetTodaysNews).Should().Contain(oldRow); }
        finally { cache.Remove(oldRow); }
    }

    // ====================================================================================================
    // A. bug 14: loot descriptions are stored in English, the curse is found by its flag
    // ====================================================================================================

    private static Item CursedDrop(string lang)
    {
        var item = new Item { Name = "Long Sword", Type = ObjType.Weapon, Attack = 100, IsCursed = true, Cursed = true };
        var effects = new List<(LootGenerator.SpecialEffect, int)> { (LootGenerator.SpecialEffect.FireDamage, 5), (LootGenerator.SpecialEffect.LifeSteal, 3) };
        InLang(lang, () =>
        {
            typeof(LootGenerator).GetMethod("ApplyEffectsToItem", FS)!.Invoke(null, new object[] { item, effects, true });
            typeof(LootGenerator).GetMethod("ApplyCursePenalties", FS)!.Invoke(null, new object[] { item });
            return 0;
        });
        return item;
    }

    private static string EffectLine(string lang) =>
        $"{L(lang, "item.effect.fire_damage.name")} +5, {L(lang, "item.effect.life_steal.name")} +3";

    [Fact]
    public void ANewDrop_StoresItsDescriptionInEnglish_AndShowsItInEachReadersLanguage()
    {
        var item = CursedDrop("hu");
        item.Description[0].Should().Be(EffectLine("en"), "stored in English in a Hungarian session");
        item.Description[1].Should().Be("This item is CURSED! Visit the Magic Shop to remove the curse.");
        foreach (var lang in AllLanguages)
        {
            LootGenerator.DescriptionLineIn(lang, item.Description[0]).Should().Be(EffectLine(lang), lang);
            LootGenerator.DescriptionLineIn(lang, item.Description[1]).Should().Be(L(lang, "item.desc_cursed"), lang);
            LootGenerator.DescriptionLineIn(lang, LootGenerator.PurifiedLine).Should().Be(L(lang, "item.desc_purified"), lang);
        }
        // saved, auctioned and banked as the whole item: the English goes with it
        var copy = JsonSerializer.Deserialize<Item>(JsonSerializer.Serialize(item))!;
        copy.Description[0].Should().Be(EffectLine("en"));
        InLang("hu", copy.GetFullDescription).Should().Contain(EffectLine("hu")).And.Contain(L("hu", "item.desc_cursed")).And.NotContain("CURSED");
    }

    [Fact]
    public void AnOldDropsDescription_ShowsAsStored_InTheSaveTheAuctionAndTheGuildBank()
    {
        string huEffects = EffectLine("hu");
        string huCurse = L("hu", "item.desc_cursed");
        var old = new Item { Name = "Long Sword", Type = ObjType.Weapon, IsCursed = true, Cursed = true };
        old.Description[0] = huEffects;
        old.Description[1] = huCurse;
        // the auction listing and the guild bank keep the item as JSON (BaseLocation, MudChatSystem)
        var listed = JsonSerializer.Deserialize<Item>(JsonSerializer.Serialize(old))!;
        listed.Description[0].Should().Be(huEffects);
        listed.Description[1].Should().Be(huCurse);
        foreach (var lang in AllLanguages)
        {
            LootGenerator.DescriptionLineIn(lang, listed.Description[0]).Should().Be(huEffects, lang);
            LootGenerator.DescriptionLineIn(lang, listed.Description[1]).Should().Be(huCurse, lang);
        }
        // the save keeps it too (InventoryItemData, SaveDataStructures.cs)
        var saved = JsonSerializer.Deserialize<InventoryItemData>(JsonSerializer.Serialize(InventoryItemData.FromItem(old)))!;
        var loaded = saved.ToItem();
        loaded.Description[0].Should().Be(huEffects, "not rewritten on load");
        loaded.Description[1].Should().Be(huCurse, "not rewritten on load");
    }

    private static Character ShopHero() => new()
    {
        Name1 = "tester", Name2 = LongName, Class = CharacterClass.Warrior, Race = CharacterRace.Human, Level = 50,
        HP = 500, MaxHP = 500, BaseMaxHP = 500, AI = CharacterAI.Human, Gold = 9_000_000_000, AutoEquipDisabled = true,
    };

    private static async Task<string> Decurse(string lang, Character hero, Item item)
    {
        return await InLanguage(lang, async () =>
        {
            var shop = new MagicShopLocation();
            var s = At(shop, hero, "Y");
            await Run(shop, "RemoveCurseFromPlayerItem", hero, item);
            return s.Text;
        });
    }

    [Theory]
    [InlineData("en")]
    [InlineData("hu")]
    public async Task CurseRemoval_FindsTheCurseByItsFlag_AndStoresTheEnglishPurifiedLine(string lang)
    {
        var hero = ShopHero();
        var item = CursedDrop(lang);
        string shown = await Decurse(lang, hero, item);
        item.IsCursed.Should().BeFalse();
        item.Description[1].Should().Be(LootGenerator.PurifiedLine, "stored English");
        item.Description[0].Should().Be(EffectLine("en"), "the effect line is left as it was");
        EveryRowFits(shown, $"{lang} curse removal");
    }

    [Fact]
    public async Task AnOldItemWhoseCurseLineIsNotEnglish_IsStillPurified_ByItsFlag()
    {
        var hero = ShopHero();
        var item = new Item { Name = "Long Sword", Type = ObjType.Weapon, Attack = 100, IsCursed = true, Cursed = true, Value = 1000 };
        item.Description[0] = EffectLine("hu");
        item.Description[1] = L("hu", "item.desc_cursed");
        item.Description[1].Should().NotContain("CURSED");
        await Decurse("hu", hero, item);
        item.IsCursed.Should().BeFalse();
        item.Description[1].Should().Be(LootGenerator.PurifiedLine, "the curse line goes, whatever its language");
        item.Description[0].Should().Be(EffectLine("hu"), "the old effect line is kept as stored");
        InLang("hu", () => LootGenerator.DescriptionLine(item.Description[1])).Should().Be(L("hu", "item.desc_purified"));
        // an item without a curse line keeps its empty slot
        var plain = new Item { Name = "Long Sword", Type = ObjType.Weapon, Attack = 100, IsCursed = true, Cursed = true, Value = 1000 };
        await Decurse("en", hero, plain);
        plain.Description[1].Should().BeNullOrEmpty();
    }

    [Fact]
    public void TheLoreRow_ShowsTheDescriptionInTheReadersLanguage_AndFits_WithTheFourLongestEffects()
    {
        var all = Enum.GetValues(typeof(LootGenerator.SpecialEffect)).Cast<LootGenerator.SpecialEffect>().Where(e => e != LootGenerator.SpecialEffect.None).ToList();
        foreach (var lang in AllLanguages)
        {
            string Key(LootGenerator.SpecialEffect e) => LootGenerator.EffectWordKey(e, "name");
            var longest = all.OrderByDescending(e => L(lang, Key(e)).Length).Take(4).ToList();
            var item = new Item { Name = "Long Sword", Type = ObjType.Weapon, IsCursed = true, Strength = -5 };
            item.Description[0] = string.Join(", ", longest.Select(e => $"{L("en", Key(e))} +999"));
            string shown = InLang(lang, () =>
            {
                var shop = new MagicShopLocation();
                var s = At(shop, ShopHero());
                typeof(MagicShopLocation).GetMethod("DisplayCurseDetails", F)!.Invoke(shop, new object[] { item });
                return s.Text;
            });
            string flat = Regex.Replace(shown, @"\s+", " ");
            foreach (var e in longest) flat.Should().Contain(L(lang, Key(e)), lang);
            EveryRowFits(shown, $"{lang} curse details");
            if (lang == "hu") NoEnglishLeft(shown, longest.Select(Key));
        }
    }

    [Fact]
    public void TheCurseAndPurifiedLines_AreShownOnlyThroughFullDescription_WhichWrapsNothing()
    {
        // Description[1] has no row of its own on any screen: the lore row (DisplayCurseDetails) shows Description[0],
        // and Item.GetFullDescription (no caller) joins the lines with new lines.
        Regex.Matches(Src("Locations", "MagicShopLocation.cs"), @"Description\[1\]").Count.Should().Be(2, "the purify write and its guard only");
        foreach (var lang in AllLanguages)
        {
            L(lang, "item.desc_cursed").Should().NotBeEmpty();
            L(lang, "item.desc_purified").Should().NotBeEmpty();
        }
    }

    // ====================================================================================================
    // B. display only: stored English shown in the reader's language
    // ====================================================================================================

    private static readonly string[] ArchetypeIds =
    {
        "commoner", "citizen", "merchant", "drunk_fighter", "assassin", "bard", "craftsman", "fighter", "guard", "healer",
        "knight", "mystic", "noble", "priest", "sailor", "scholar", "scout", "thug", "worker",
    };

    [Fact]
    public void NpcInfoLine_ShowsTheArchetypeInTheReadersLanguage_AndKeepsTheStoredId()
    {
        foreach (var id in ArchetypeIds)
        {
            L("en", "npc.archetype." + id).Should().Be(id, "English shows the id as before");
            var npc = new NPC { Name1 = LongName, Name2 = LongName, Archetype = id, Level = 100 };
            foreach (var lang in AllLanguages)
            {
                string line = InLang(lang, npc.GetDisplayInfo);
                line.Should().Contain(L(lang, "npc.archetype." + id), $"{id} {lang}");
                line.Length.Should().BeLessOrEqualTo(MaxWidth, $"{id} {lang}");
            }
            npc.Archetype.Should().Be(id, "the stored id is not changed");
            if (id != "noble") InLang("hu", npc.GetDisplayInfo).Should().NotContain(id, id);
        }
        InLang("hu", () => NPC.ArchetypeLabel("Balanced")).Should().Be("Balanced", "an id without a key shows as stored");
    }

    [Fact]
    public async Task TheOnlineSaveFallbackError_IsInThePlayersLanguage()
    {
        string path = Path.Combine(Path.GetTempPath(), $"usurper-d11-{Guid.NewGuid():N}.db");
        try
        {
            var system = new SaveSystem(new SqlSaveBackend(path));
            var (data, error, tooLarge) = await InLanguage("hu", () => system.LoadSaveByFileNameWithError("missing_save"));
            data.Should().BeNull();
            tooLarge.Should().BeFalse();
            error.Should().Be(L("hu", "save.load_error_unreadable", "missing_save")).And.NotContain("Could not read");
            (await InLanguage("en", () => system.LoadSaveByFileNameWithError("missing_save"))).Error.Should().Be("Could not read save: missing_save");
        }
        finally { SqliteConnection.ClearAllPools(); try { File.Delete(path); } catch { } }
    }

    [Fact]
    public void CompanionCombatDeaths_AreStoredAsBefore_AndShownInTheReadersLanguage()
    {
        // the stored English is the text written before 1.2.5
        L("en", "companion.death_sacrifice", LongName).Should().Be($"Sacrificed themselves to save {LongName} from a killing blow");
        L("en", "companion.death_slain", "Wolf").Should().Be("Slain by Wolf in combat");
        foreach (var lang in AllLanguages)
        {
            InLang(lang, () => CompanionSystem.CircumstanceLabel($"Sacrificed themselves to save {LongName} from a killing blow"))
                .Should().Be(L(lang, "companion.death_sacrifice", LongName), lang);
            InLang(lang, () => CompanionSystem.CircumstanceLabel("Slain by Wolf in combat"))
                .Should().Be(L(lang, "companion.death_slain", lang == "en" ? "Wolf" : L(lang, "monster.name.wolf")), lang);
        }
        // anything else shows as stored
        InLang("hu", () => CompanionSystem.CircumstanceLabel("Slain by")).Should().Be("Slain by");
        Src("Systems", "CombatEngine.cs").Should().Contain("Loc.GetIn(\"en\", \"companion.death_sacrifice\", player.DisplayName)")
            .And.Contain("Loc.GetIn(\"en\", \"companion.death_slain\", killerName)");
    }

    [Fact]
    public void TheHealerCursedWeapon_IsShownThroughItemNames()
    {
        // HealerLocation's cursed list keeps the stored name and shows each entry through ItemNames.Display
        string healer = Src("Locations", "HealerLocation.cs");
        healer.Should().Contain("cursedItems.Add((player.WeaponName ?? Loc.Get(\"base.item_type_weapon\"), \"weapon\",")
            .And.Contain("Loc.Get(\"shop.cursed_item_healer\", ItemNames.Display(item.Name))");
        InLang("hu", () => ItemNames.Display("Long Sword")).Should().Be(L("hu", "item.long_sword"));
    }

    [Fact]
    public void SleepMurderMails_NameTheItemInTheVictimsLanguage()
    {
        InnLocation.SleepMurderMail("hu", LongName, 1234, "Long Sword").Should().Be(L("hu", "inn.mail_sleep_murder_item", LongName, $"{1234:N0}", L("hu", "item.long_sword")));
        DormitoryLocation.SleepMurderMail("hu", LongName, 1234, "Long Sword").Should().Be(L("hu", "dormitory.mail_sleep_murder_item", LongName, $"{1234:N0}", L("hu", "item.long_sword")));
        InnLocation.SleepMurderMail("en", "Bo", 5, "Long Sword").Should().Be(L("en", "inn.mail_sleep_murder_item", "Bo", "5", "Long Sword"));
        // an unknown stored name shows as stored
        InnLocation.SleepMurderMail("hu", "Bo", 5, "Zzyzx Blade").Should().Contain("Zzyzx Blade");
        Src("Locations", "InnLocation.cs").Should().Contain("SleepMurderMail(lang, murderer, stolenGold, stolenItemName, stolenItemFamily)");
        Src("Locations", "DormitoryLocation.cs").Should().Contain("SleepMurderMail(lang, murderer, stolenGold, stolenItemName, stolenEquipment?.Family)");
    }

    /// <summary>An entry as WorldSimulator.ProcessNPCAttacksOnSleepers writes it.</summary>
    private static JsonNode WorldSimEntry(string result, params string[] details) => JsonNode.Parse(JsonSerializer.Serialize(new
    {
        attacker = "Grimbold", type = "npc", result, goldStolen = 120L, itemStolen = "Long Sword", xpLost = 33L, details,
    }))!;

    [Fact]
    public void TheSleepReport_ReadsTheWorldSimulationsEntries_InTheReadersLanguage()
    {
        var killed = WorldSimEntry("killed", "Your Hound was defeated by Grimbold.", "Grimbold attacked you in your sleep and killed you!");
        GameEngine.SleepResult(killed).Should().Be("attacker_won");
        var fights = GameEngine.SleepGuardFights(killed, "Grimbold")!;
        fights.Should().ContainSingle();
        fights[0]!["guard"]!.GetValue<string>().Should().Be("Hound");
        fights[0]!["result"]!.GetValue<string>().Should().Be("guard_lost");

        var byGuards = WorldSimEntry("repelled", "Your Old Tom fought off Grimbold!");
        GameEngine.SleepResult(byGuards).Should().Be("guards_repelled");
        GameEngine.SleepGuardFights(byGuards, "Grimbold")![0]!["result"]!.GetValue<string>().Should().Be("guard_won");
        GameEngine.SleepResult(WorldSimEntry("repelled", "You fought off Grimbold in your sleep!")).Should().Be("defender_won");

        // the Inn and the Dormitory entries read as before
        GameEngine.SleepResult(JsonNode.Parse("{\"attacker\":\"X\",\"result\":\"attacker_won\"}")!).Should().Be("attacker_won");
        GameEngine.SleepGuardFights(JsonNode.Parse("{\"attacker\":\"X\",\"result\":\"guards_repelled\"}")!, "X").Should().BeNull();

        string engine = Src("Core", "GameEngine.cs");
        engine.Should().Contain("string result = SleepResult(entry);")
            .And.Contain("entry[\"guard_fights\"] ?? SleepGuardFights(entry, attacker)")
            .And.Contain("(entry[\"gold_stolen\"] ?? entry[\"goldStolen\"])")
            .And.Contain("Loc.Get(\"engine.item_stolen\", ItemNames.Display(itemStolen))");
    }

    [Fact]
    public void ResurrectionChoices_AreInThePlayersLanguage_AndFit()
    {
        L("en", "death.temple_desc", $"{1234567:N0}", 2).Should().Be($"Pay the temple 50% of your gold ({1234567:N0}g) for resurrection (2 remaining)");
        L("en", "death.accept_desc").Should().Be("WARNING: Lose 5 levels, 75% of your gold, and a random equipped item");
        foreach (var lang in AllLanguages)
            foreach (var text in new[] { L(lang, "death.temple_desc", $"{9_999_999_999L:N0}", 3), L(lang, "death.accept_desc"), L(lang, "death.divine_desc", "99") })
                foreach (var row in UsurperRemake.UI.UIHelper.WrapAfterPrefix("    ", text, 79))
                    ("    " + row).Length.Should().BeLessOrEqualTo(MaxWidth, $"{lang}: {row}");
        NoEnglishLeft(L("hu", "death.temple_desc", "1", 1) + L("hu", "death.accept_desc"), new[] { "death.temple_desc", "death.accept_desc" });
        string combat = Src("Systems", "CombatEngine.cs");
        combat.Should().Contain("Description = Loc.Get(\"death.temple_desc\", templeCost.ToString(\"N0\"), remaining),")
            .And.Contain("Description = Loc.Get(\"death.accept_desc\"),")
            .And.Contain("UsurperRemake.UI.UIHelper.WrapAfterPrefix(\"    \", choice.Description, 79)");
    }

    [Fact]
    public async Task TheAuctionSaleMail_NamesTheItemInTheSellersLanguage()
    {
        string path = Path.Combine(Path.GetTempPath(), $"usurper-d11-{Guid.NewGuid():N}.db");
        try
        {
            var db = new SqlSaveBackend(path);
            using (var c = new SqliteConnection($"Data Source={path}"))
            {
                c.Open();
                using var cmd = c.CreateCommand();
                cmd.CommandText = "INSERT INTO players (username, display_name, player_data, language, last_login) VALUES ('seller_hu', 'Seller', '{\"player\":{\"name2\":\"Seller\"}}', 'hu', datetime('now'));";
                cmd.ExecuteNonQuery();
            }
            long id = await db.MailAuctionSale("Seller", "Long Sword", "Brenna", 100, DateTime.UtcNow, "Long Sword");
            id.Should().BeGreaterThan(0);
            using var r = new SqliteConnection($"Data Source={path}");
            r.Open();
            using var q = r.CreateCommand();
            q.CommandText = $"SELECT message FROM messages WHERE id = {id};";
            ((string)q.ExecuteScalar()!).Should().Be(L("hu", "mail.auction_sold", L("hu", "item.long_sword"), "Brenna", $"{100:N0}"));
        }
        finally { SqliteConnection.ClearAllPools(); try { File.Delete(path); } catch { } }
        Src("Systems", "WorldSimulator.cs").Should().Contain("MailAuctionSale(chosen.Seller, item.Name, npc.Name, chosen.Price, itemFamily: item.Family)");
    }

    [Fact]
    public void ATamedPet_IsNamedInTheReadersLanguage_AndKeepsItsStoredName()
    {
        var def = UsurperRemake.Data.BeastData.Beasts.First(b => MonsterNames.KeyOf(b.Name) != null);
        var pet = UsurperRemake.Data.BeastData.BuildCombatWrapper(def, 5, def.Name);
        pet.Name2.Should().Be(def.Name);
        foreach (var lang in AllLanguages)
            InLang(lang, () => pet.DisplayName).Should().Be(MonsterNames.DisplayIn(lang, def.Name), lang);
        // a renamed pet and any other character show as before
        InLang("hu", () => UsurperRemake.Data.BeastData.BuildCombatWrapper(def, 5, "Fluffy").DisplayName).Should().Be("Fluffy");
        InLang("hu", () => new Character { Name2 = "Wolf" }.DisplayName).Should().Be("Wolf");
    }

    [Fact]
    public void TheMonsterNamedFoes_AreStoredInEnglish_AndShownInTheReadersLanguage()
    {
        // street muggers, bank guards, the Main Street royal guards, the castle's royal guards
        foreach (var key in StreetEncounterSystem.MuggerNameKeys.Concat(new[] { "bank.guard_captain_name", "bank.guard_name", "bank.war_hound_name", "base.royal_guard" }))
        {
            var m = new Monster { Name = InLang("hu", () => MonsterNames.FromKey(key)) };
            m.Name.Should().Be(L("en", key), key);
            // a name the monster tables also hold ("War Hound") shows through that table's key
            string shownKey = MonsterNames.KeyOf(m.Name) ?? key;
            foreach (var lang in AllLanguages) MonsterNames.DisplayIn(lang, m).Should().Be(L(lang, shownKey), $"{key} {lang}");
            MonsterNames.DisplayIn("hu", m).Should().NotBe(m.Name, key);
        }
        var guard = new Monster { Name = InLang("hu", () => MonsterNames.FromKey("castle.royal_guard_monster", LongName)) };
        guard.Name.Should().Be(L("en", "castle.royal_guard_monster", LongName));
        MonsterNames.DisplayIn("hu", guard).Should().Be(L("hu", "castle.royal_guard_monster", LongName));

        Src("Systems", "StreetEncounterSystem.cs").Should().Contain("return MonsterNames.FromKey(MuggerNameKeys[index % MuggerNameKeys.Length]);");
        string bank = Src("Locations", "BankLocation.cs");
        foreach (var key in new[] { "bank.guard_captain_name", "bank.guard_name", "bank.war_hound_name" })
            bank.Should().Contain($"Name = MonsterNames.FromKey(\"{key}\"),");
        Src("Locations", "BaseLocation.cs").Should().Contain("Name = MonsterNames.FromKey(\"base.royal_guard\"),");
        Src("Locations", "CastleLocation.cs").Should().Contain("Name = MonsterNames.FromKey(\"castle.royal_guard_monster\", guard.Name),");

        // no English name check sees a word in these names
        foreach (var name in StreetEncounterSystem.MuggerNameKeys.Select(k => L("en", k)).Concat(new[] { "Captain of the Guard", "Bank Guard", "War Hound", "Royal Guard" }))
            foreach (var word in new[] { "Boss", "Chief", "Lord", "King", "Undead", "Skeleton", "Zombie", "Ghost", "Demon", "Imp", "Angel", "God" })
                name.Should().NotContain(word, name);
    }

    [Fact]
    public void SecretBosses_AreNamedAndTitledInTheReadersLanguage_AndStoredInEnglish()
    {
        foreach (var type in Enum.GetValues<SecretBossType>())
        {
            var boss = UsurperRemake.Data.SecretBossManager.Instance.GetBoss(type);
            if (boss == null) continue;
            MonsterNames.KeyOf(boss.Name).Should().NotBeNull(boss.Name);
            foreach (var lang in AllLanguages)
            {
                MonsterNames.DisplayIn(lang, boss.Name).Should().Be(L(lang, MonsterNames.KeyOf(boss.Name)!), lang);
                InLang(lang, boss.LocTitle).Should().Be(L(lang, $"secretboss.{boss.LocKey}.title"), lang);
            }
            L("en", $"secretboss.{boss.LocKey}.title").Should().Be(boss.Title);
            InLang("hu", boss.LocTitle).Should().NotBe(boss.Title);
            var monster = (Monster)typeof(UsurperRemake.Data.SecretBossManager).GetMethod("CreateBossMonster", F | BindingFlags.Public)!
                .Invoke(UsurperRemake.Data.SecretBossManager.Instance, BossArgs(typeof(UsurperRemake.Data.SecretBossManager).GetMethod("CreateBossMonster", F | BindingFlags.Public)!, type))!;
            monster.Name.Should().Be(boss.Name, "the fight's monster keeps the English name");
        }
        string src = Src("Data", "SecretBosses.cs");
        src.Should().Contain("terminal.WriteLine($\"  {MonsterNames.Display(boss.Name)}\", \"bright_red\");")
            .And.Contain("terminal.WriteLine($\"  \\\"{boss.LocTitle()}\\\"\", \"red\");")
            .And.Contain("await DisplayDialogue(boss.LocPreDialogue(), MonsterNames.Display(boss.Name), terminal);");
    }

    private static object?[] BossArgs(MethodInfo m, SecretBossType type) =>
        m.GetParameters().Select(p => p.ParameterType == typeof(SecretBossType) ? type
            : p.ParameterType == typeof(int) ? 50 : p.ParameterType == typeof(float) ? 1f : p.ParameterType == typeof(double) ? 1.0
            : p.HasDefaultValue ? p.DefaultValue : null).ToArray();

    // ====================================================================================================
    // C. dead code removed (no reader in Scripts, Tests, tools or electron-client)
    // ====================================================================================================

    [Fact]
    public void TheDeadMonsterSpellAndTerrainCode_IsGone()
    {
        typeof(Monster).GetMethod("CastSpell", BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance).Should().BeNull();
        typeof(Monster).GetMethod("CastSpellByIndex", F).Should().BeNull();
        typeof(Monster).Assembly.GetType("MonsterSpellResult").Should().BeNull();
        typeof(CombatMessages).GetMethod("GetSpellCastMessage").Should().BeNull();
        foreach (var name in new[] { "CreateDungeonMonster", "GetMonsterNamesForTerrain", "GetWeaponArmorForTerrain", "GetLeaderName" })
            typeof(DungeonLocation).GetMethod(name, F).Should().BeNull(name);
        typeof(DungeonLocation).GetMethods(F).Where(m => m.Name == "GetMonsterPhrase").Should().BeEmpty();
    }
}
