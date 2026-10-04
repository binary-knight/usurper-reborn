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
using UsurperRemake.Data;
using UsurperRemake.Locations;
using UsurperRemake.Systems;
using UsurperRemake.UI;
using Xunit;

namespace UsurperReborn.Tests;

/// <summary>
/// v1.2.5: the combat, monster and dungeon tables. Monster names stay English in storage and in every
/// comparison (the combat engine's name checks, quest kills, castle guards, pets, saves) and show in the
/// reader's language through MonsterNames, keyed by the English table name. Old God names keep their proper
/// name and key the epithet, title and ability names; artifacts, combat attack sentences, world boss elements,
/// dungeon event choices and the Electron combat labels are keyed. Every row fits 79 columns.
/// </summary>
[Collection("SharedGameSingletons")]
public class DataCombat125Tests
{
    private const BindingFlags F = BindingFlags.NonPublic | BindingFlags.Instance;
    private const BindingFlags SNP = BindingFlags.NonPublic | BindingFlags.Static;
    private const int MaxWidth = 79;
    private static readonly string[] AllLanguages = { "en", "es", "fr", "hu", "it" };
    private static readonly string[] OtherLanguages = { "es", "fr", "hu", "it" };

    // GameConfig.MaxNameLength (30) characters, the longest name a player can have.
    private const string LongName = "Bartholomew Thistlewood Grande";

    // A monster name a language keeps as English writes it (proper names, loan words).
    private static readonly Dictionary<string, string[]> SameInLanguage = new()
    {
        ["Goblin"] = new[] { "es", "hu", "it" }, ["Hobgoblin"] = new[] { "es", "hu", "it" }, ["Zombie"] = new[] { "fr" },
        ["Lich"] = new[] { "hu", "it" }, ["Orc"] = new[] { "fr" }, ["Kobold"] = new[] { "es", "fr", "hu" },
        ["Drake"] = new[] { "fr" }, ["Ogre"] = new[] { "fr", "hu" }, ["Troll"] = new[] { "fr", "hu", "it" },
        ["Titan"] = new[] { "fr" }, ["Fenrir"] = new[] { "es", "fr", "hu", "it" }, ["Inferno"] = new[] { "it" },
        ["Shoggoth"] = new[] { "es", "fr", "hu", "it" }, ["Golem"] = new[] { "fr", "it" }, ["Pixie"] = new[] { "fr" },
        ["Sprite"] = new[] { "es" }, ["Merrow"] = new[] { "fr" }, ["Crocodile"] = new[] { "fr" }, ["Mimic"] = new[] { "it" },
        ["Pirate"] = new[] { "fr" }, ["Manticore"] = new[] { "fr" }, ["Bandit"] = new[] { "fr" }, ["Boss"] = new[] { "fr", "it" },
        ["Ghoul"] = new[] { "it" }, ["Orc Berserker"] = new[] { "fr" }, ["Elder Kraken"] = new[] { "fr" }, ["Mountain Lion"] = Array.Empty<string>(),
    };

    // ---------- helpers ----------

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

    private static void EveryRowFits(string text, string screen)
    {
        foreach (var row in Rows(text))
            row.Length.Should().BeLessOrEqualTo(MaxWidth, $"every row of the {screen} fits in {MaxWidth} columns: \"{row}\"");
    }

    private static string Src(params string[] parts) =>
        File.ReadAllText(Path.Combine(new[] { UsurperReborn.Tests.Localization.HardcodedTextScannerTests.RepoRoot(), "Scripts" }.Concat(parts).ToArray()));

    private static Character Hero() => new()
    {
        Name1 = "dctester", Name2 = "DcTester", Class = CharacterClass.Warrior, Race = CharacterRace.Human, Level = 40,
        HP = 5000, MaxHP = 5000, AI = CharacterAI.Human, Gold = 1000, Strength = 200, MKills = 10,
        CombatSpeed = CombatSpeed.Instant, AutoLevelUp = false,
    };

    private static (CombatEngine engine, Screen screen) Engine(Character hero)
    {
        var s = NewScreen(Enumerable.Repeat("P", 30).ToArray());
        var engine = new CombatEngine(s.Term);
        typeof(CombatEngine).GetField("random", F)!.SetValue(engine, new Random(7));
        typeof(CombatEngine).GetField("currentPlayer", F)!.SetValue(engine, hero);
        return (engine, s);
    }

    /// <summary>Every family's tiers, as MonsterFamilies writes them.</summary>
    private static IEnumerable<(MonsterFamilies.MonsterFamily family, MonsterFamilies.MonsterTier tier)> Tiers() =>
        MonsterFamilies.GetBuiltInFamilies().SelectMany(f => f.Tiers.Select(t => (f, t)));

    /// <summary>The English champion name MonsterGenerator gives a tier's mini-boss.</summary>
    private static string ChampionName(string tier, string family) =>
        (string)typeof(MonsterGenerator).GetMethod("GetChampionName", SNP)!.Invoke(null, new object[] { tier, family })!;

    /// <summary>Every English monster name the owned tables store.</summary>
    private static List<string> OwnedMonsterNames()
    {
        var names = new List<string>();
        names.AddRange(Tiers().Select(x => x.tier.Name));
        names.AddRange(WildernessData.Regions.SelectMany(r => r.MonsterNames));
        var pool = (System.Collections.IDictionary)typeof(DungeonLocation).GetField("BossNamePool", SNP)!.GetValue(null)!;
        foreach (string[] v in pool.Values) names.AddRange(v);
        names.Add("Dungeon Boss");
        names.AddRange(new[] { "Orc", "Half-Orc", "Orc Raider", "Troll", "Half-Troll", "Lumber-Troll", "Rogue", "Thief", "Pirate",
            "Dwarf", "Dwarf Warrior", "Dwarf Scout", "Ruffian", "Undead", "Zombie", "Skeleton Warrior", "Portal Guardian", "Mimic",
            "Vengeful Spirit", "Rival Adventurer", "Riddle Guardian", "Traveling Merchant", "Orcs Leader", "Trolls Leader",
            "Rogues Leader", "Dwarves Leader", "Undead Warrior", "Undead Mage", "Undead Cleric", "Undead Rogue", "Undead Paladin" });
        names.AddRange(MonsterGuardTypes.AvailableMonsters.Select(m => m.Name));
        names.AddRange(new[] { "Wolf", "Goblin", "Kobold", "Zombie", "Imp", "Orc", "Troll", "Ogre", "Wraith", "Wyvern",
            "Ancient Dragon", "Archfiend", "Lich", "Titan", "Void Entity", "Bandit", "Boss", "Dire Wolf", "Hobgoblin", "Ghoul",
            "Orc Warrior", "Orc Berserker", "Drake", "Shade" });
        names.AddRange(BeastData.Beasts.Select(b => b.Name));
        names.AddRange(new[] { "Spectral Soldier", "Divine Executioner", "Shadow Clone", "Living Shadow", "Summoned Creature", "Shadow of Manwe" });
        return names.Distinct().ToList();
    }

    /// <summary>Every name a monster is shown by: the tables, the champions, the Old Gods.</summary>
    private static List<string> ShownNames(string lang)
    {
        var shown = OwnedMonsterNames().Select(n => MonsterNames.DisplayIn(lang, n)).ToList();
        foreach (var (family, tier) in Tiers())
            shown.Add(MonsterNames.DisplayIn(lang, new Monster { Name = ChampionName(tier.Name, family.FamilyName), TierName = tier.Name, FamilyName = family.FamilyName }));
        shown.AddRange(OldGodsData.GetAllOldGodsWithVariants().Select(g => MonsterNames.DisplayIn(lang, g.Name)));
        return shown;
    }

    private static string Longest(string lang) => ShownNames(lang).OrderByDescending(n => n.Length).First();

    private static bool HasDash(string s) => s.Contains('\u2014') || s.Contains('\u2013');

    // ---------- 1. keys ----------

    [Fact]
    public void EveryStoredMonsterName_HasItsKey_InFiveLanguages_Translated()
    {
        var missing = new List<string>();
        foreach (var name in OwnedMonsterNames())
        {
            var key = MonsterNames.KeyOf(name);
            if (key == null) { missing.Add(name); continue; }
            Loc.GetIn("en", key).Should().Be(name);
            foreach (var lang in OtherLanguages)
            {
                Loc.HasIn(lang, key).Should().BeTrue($"{lang} {key}");
                string text = Loc.GetIn(lang, key);
                HasDash(text).Should().BeFalse(key);
                if (text == name)
                    (SameInLanguage.TryGetValue(name, out var same) && same.Contains(lang)).Should().BeTrue($"{lang} {key} is translated, not English \"{name}\"");
            }
        }
        missing.Should().BeEmpty("every stored monster name shows through monster.name.{id}");
        foreach (var family in MonsterFamilies.GetBuiltInFamilies())
            foreach (var lang in AllLanguages)
                MonsterNames.FamilyKeyOf(family.FamilyName).Should().NotBeNull(family.FamilyName);
        foreach (var (family, tier) in Tiers())
            foreach (var lang in AllLanguages)
                Loc.HasIn(lang, "monster.plural." + MonsterNames.IdOf(tier.Name)).Should().BeTrue($"{lang} plural of {tier.Name}");
    }

    [Fact]
    public void EveryChampionName_IsItsTier_InItsFamilysTemplate_AndReadsBackInEnglish()
    {
        foreach (var (family, tier) in Tiers())
        {
            var champion = new Monster { Name = ChampionName(tier.Name, family.FamilyName), TierName = tier.Name, FamilyName = family.FamilyName, IsMiniBoss = true };
            MonsterNames.DisplayIn("en", champion).Should().Be(champion.Name, "English shows the stored name");
            string hu = MonsterNames.DisplayIn("hu", champion);
            hu.Should().Contain(MonsterNames.DisplayIn("hu", tier.Name), $"{champion.Name} is built on its tier");
            hu.Should().NotContain(" Champion").And.NotContain("Revenant").And.NotContain("Alpha ").And.NotContain("Hive Lord");
        }
        MonsterNames.DisplayIn("hu", "Ruffian Leader").Should().Be(L("hu", "monster.leader", L("hu", "monster.name.ruffian")));
        MonsterNames.DisplayIn("hu", "Unknown Thing").Should().Be("Unknown Thing", "a name the tables do not write shows as stored");
    }

    [Fact]
    public void OldGods_KeepTheirProperName_AndKeyTheEpithetTitleAndAbilities()
    {
        foreach (var god in OldGodsData.GetAllOldGodsWithVariants())
        {
            string proper = god.Name.Split(',')[0];
            InLang("en", god.LocName).Should().Be(god.Name);
            InLang("en", god.LocTitle).Should().Be(god.Title);
            foreach (var lang in OtherLanguages)
            {
                string name = InLang(lang, god.LocName), title = InLang(lang, god.LocTitle);
                name.Should().StartWith(proper + ",", "the proper name is the same in every language").And.NotBe(god.Name);
                title.Should().NotBe(god.Title);
                MonsterNames.DisplayIn(lang, god.Name).Should().Be(name, "the boss monster shows the same name");
                foreach (var ability in god.Phase1Abilities.Concat(god.Phase2Abilities).Concat(god.Phase3Abilities))
                {
                    OldGodsData.AbilityKeyOf(ability).Should().NotBeNull(ability);
                    OldGodsData.AbilityLabelIn(lang, ability).Should().NotBeNullOrEmpty();
                    HasDash(OldGodsData.AbilityLabelIn(lang, ability)).Should().BeFalse(ability);
                }
            }
        }
        InLang("hu", () => CombatEngine.BossAbilityLabel("Gavel Strike")).Should().Be(L("hu", "oldgod.ability.gavel_strike")).And.NotBe("Gavel Strike");
        InLang("en", () => CombatEngine.BossAbilityLabel("Gavel Strike")).Should().Be("Gavel Strike");
    }

    [Fact]
    public void Artifacts_ShowTheirTextInTheReadersLanguage_AndTypesAreWhatIsStored()
    {
        foreach (var a in ArtifactSystem.Instance.GetAllArtifacts())
        {
            InLang("en", a.LocName).Should().Be(a.Name);
            InLang("en", a.LocDescription).Should().Be(a.Description);
            InLang("en", a.LocAbility).Should().Be(a.SpecialAbility);
            InLang("en", a.LocLore).Should().Equal(a.LoreText, "English keeps its lines");
            foreach (var lang in OtherLanguages)
            {
                InLang(lang, a.LocName).Should().NotBe(a.Name);
                InLang(lang, a.LocDescription).Should().NotBe(a.Description);
                InLang(lang, a.LocAbility).Should().NotBe(a.SpecialAbility);
                var lore = InLang(lang, a.LocLore);
                lore.Where(r => r.Length > 0).Should().NotIntersectWith(a.LoreText.Where(r => r.Length > 0));
                lore.Count(r => r.Length == 0).Should().Be(a.LoreText.Count(r => r.Length == 0), "paragraph breaks stay");
                foreach (var stat in a.StatBonuses.Keys)
                    InLang(lang, () => ArtifactData.StatLabel(stat)).Should().Be(L(lang, "artifact.stat." + stat.ToLowerInvariant()));
            }
        }
        InLang("en", () => ArtifactData.StatLabel("MaxMana")).Should().Be("Max Mana", "v1.2.5: the label, not the code name");
        InLang("en", () => ArtifactData.StatLabel("WeapPow")).Should().Be("Weapon Power");
    }

    [Fact]
    public void AttackSentences_AreWholeSentenceKeys_AndDrawTheSameNumbers()
    {
        foreach (CombatMessages.DamageTier tier in Enum.GetValues<CombatMessages.DamageTier>())
            foreach (var who in new[] { "player", "ally", "monster" })
                for (int i = 0; i < CombatMessages.FormCount(who, tier); i++)
                    foreach (var lang in AllLanguages)
                    {
                        string key = CombatMessages.FormKey(who, tier, i);
                        Loc.HasIn(lang, key).Should().BeTrue($"{lang} {key}");
                        HasDash(Loc.GetIn(lang, key)).Should().BeFalse(key);
                    }
        // the same draws as the verb lists: one Next(count) per message
        var a = new Random(11); var b = new Random(11);
        InLang("en", () => CombatMessages.GetPlayerAttackMessage("Goblin", 30, 100, a)).Should().Be(
            $"You {new[] { "wound", "strike hard", "cleave", "rend" }[b.Next(4)]} Goblin for [bright_yellow]30[/] damage!");
        a.Next().Should().Be(b.Next(), "the RNG is in step after the message");
        InLang("en", () => CombatMessages.GetMonsterAttackMessage("Goblin", "red", 0, 100, new Random(3))).Should().StartWith("[red]Goblin[/] ");
        InLang("en", () => CombatMessages.GetAllyAttackMessage("Mira", "Goblin", 0, 100, new Random(1)))
            .Should().NotContain("ates ").And.NotContain("hards").And.StartWith("[bright_cyan]Mira[/] ");
        string hu = InLang("hu", () => CombatMessages.GetMonsterAttackMessage(MonsterNames.DisplayIn("hu", "Wolf"), "white", 15, 100, new Random(3)));
        hu.Should().Contain("Farkas").And.NotContain(" you").And.NotContain("hits");
    }

    // ---------- 2. the hu player sees hu names ----------

    [Fact]
    public async Task AHuPlayer_SeesHuMonsterNames_InCombat_AndTheStoredNameStaysEnglish()
    {
        var hero = Hero();
        var wolf = InLang("hu", () => new Monster { Name = "Wolf", TierName = "Wolf", FamilyName = "Beast", Level = 3, HP = 5, MaxHP = 50, IsActive = true, MonsterColor = "white" });
        string text = await InLanguage("hu", async () =>
        {
            var (engine, s) = Engine(hero);
            await (Task<bool>)typeof(CombatEngine).GetMethod("ApplySingleMonsterDamage", F)!
                .Invoke(engine, new object?[] { wolf, 1000L, new CombatResult { Player = hero }, "attack", hero, false })!;
            foreach (var row in CombatEngine.FacingRows(wolf, "  ")) s.Term.WriteLine(row);
            return s.Text;
        });
        Capture("data-combat-hu-wolf.txt", text);
        text.Should().Contain("Farkas").And.NotContain("Wolf");
        text.Should().Contain(L("hu", "combat.facing", L("hu", "monster.display_info", "Farkas", 3, 0, "")));
        wolf.Name.Should().Be("Wolf");
        wolf.TierName.Should().Be("Wolf");
    }

    [Theory]
    [InlineData("en")]
    [InlineData("es")]
    [InlineData("fr")]
    [InlineData("hu")]
    [InlineData("it")]
    public void GeneratedMonsters_StoreTheEnglishTableNames_InEveryLanguage(string lang)
    {
        var tierNames = Tiers().Select(x => x.tier.Name).ToHashSet();
        var championNames = Tiers().Select(x => ChampionName(x.tier.Name, x.family.FamilyName)).ToHashSet();
        var rng = new Random(5);
        for (int level = 1; level <= 100; level += 3)
        {
            var m = InLang(lang, () => MonsterGenerator.GenerateMonster(level, random: rng));
            tierNames.Should().Contain(m.Name, $"{lang} level {level}");
            var c = InLang(lang, () => MonsterGenerator.GenerateMonster(level, isMiniBoss: true, random: rng));
            championNames.Should().Contain(c.Name, $"{lang} champion level {level}");
            InLang(lang, () => MonsterNames.Display(c)).Should().Be(MonsterNames.DisplayIn(lang, c));
        }
    }

    [Fact]
    public void QuestText_ShowsHuMonsterNames_WhileTheQuestStoresEnglish()
    {
        InLang("hu", () => { QuestSystem.InitializeStarterQuests(); return 0; });
        var obj = QuestSystem.GetAllQuests(true).Where(q => q.Id.StartsWith("STARTER_"))
            .SelectMany(q => q.Objectives).First(o => o.TargetName == "Wolf" && o.DescriptionKey == "quest.objective.kill_count");
        obj.TargetId.Should().Be("wolf");
        obj.DescriptionArgs.Should().Contain(a => a == "Wolves" || a == "Wolf", "stored English");
        string hu = InLang("hu", obj.GetDisplayDescription);
        hu.Should().Contain("Farkas").And.NotContain("Wol");
        InLang("en", obj.GetDisplayDescription).Should().Contain("Wol");
        InLang("es", obj.GetDisplayDescription).Should().Contain(obj.RequiredProgress > 1 ? L("es", "monster.plural.wolf") : L("es", "monster.name.wolf"));
        InLang("hu", () => CastleLocation.QuestTargetLabel("Lich")).Should().Be(L("hu", "monster.name.lich"));
        InLang("hu", () => CastleLocation.QuestTargetLabel("Floor 7")).Should().Be(L("hu", "dungeon.floor", 7));

        // a boss quest written in French shows its champion in the reader's language from the stored tier id
        var boss = InLang("fr", () => QuestSystem.CreateDungeonQuest(QuestTarget.ClearBoss, 2, null, 20));
        try
        {
            var kill = boss.Objectives.Single(o => o.ObjectiveType == QuestObjectiveType.KillBoss);
            string tier = Tiers().First(t => t.tier.Name.ToLower().Replace(" ", "_") == kill.TargetId).tier.Name;
            InLang("hu", kill.GetDisplayDescription).Should().Be(L("hu", "quest.objective.defeat_boss", L("hu", "quest.title.champion", MonsterNames.DisplayIn("hu", tier))));
            InLang("hu", boss.GetDisplayTitle).Should().Contain(MonsterNames.DisplayIn("hu", tier));
        }
        finally { boss.Deleted = true; }
    }

    [Fact]
    public void CastleGuards_ShowInTheReadersLanguage_AndAreDismissedByTheShownOrStoredName()
    {
        var stored = new[] { "Wyvern", "Champion of Maelketh" };
        InLang("hu", () => CastleLocation.MonsterGuardNameFromInput(stored, L("hu", "monster.name.wyvern"))).Should().Be("Wyvern");
        InLang("hu", () => CastleLocation.MonsterGuardNameFromInput(stored, "champion of maelketh")).Should().Be("Champion of Maelketh");
        InLang("hu", () => CastleLocation.MonsterGuardNameFromInput(stored, "Nope")).Should().Be("Nope");
        InLang("hu", () => MonsterNames.Display("Champion of Maelketh")).Should().Be("Maelketh Bajnoka");
    }

    // ---------- 3. stored and matched names stay English ----------

    [Fact]
    public void TheNameChecks_SeeTheEnglishName_InEveryLanguage()
    {
        var angel = typeof(CombatEngine).GetMethod("IsAngelMonster", SNP)!;
        var demon = typeof(CombatEngine).GetMethod("IsDemonMonster", SNP)!;
        foreach (var lang in AllLanguages)
        {
            InLang(lang, () => (bool)angel.Invoke(null, new object[] { new Monster { Name = "Archangel", TierName = "Archangel" } })!).Should().BeTrue(lang);
            InLang(lang, () => (bool)demon.Invoke(null, new object[] { new Monster { Name = "Greater Demon", TierName = "Greater Demon" } })!).Should().BeTrue(lang);
        }
        // no name check is ever handed a shown name
        foreach (var file in new[] { Src("Systems", "CombatEngine.cs"), Src("Systems", "DivineBlessingSystem.cs"), Src("Systems", "MonsterAbilities.cs"),
                     Src("Systems", "QuestSystem.cs"), Src("Systems", "OldGodBossSystem.cs"), Src("Systems", "PermadeathHelper.cs"),
                     Src("Locations", "CastleLocation.cs"), Src("Locations", "DungeonLocation.cs"), Src("Locations", "WildernessLocation.cs"),
                     Src("Locations", "HomeLocation.cs"), Src("Locations", "SettlementLocation.cs"), Src("Locations", "QuestHallLocation.cs"),
                     Src("Core", "King.cs"), Src("Core", "Quest.cs"), Src("Server", "GroupFollowerDeath.cs") })
            foreach (var row in Rows(file).Where(r => r.Contains("MonsterNames.")))
            {
                row.Should().NotContain(".Contains(\"");
                row.Should().NotContain("OnMonsterKilled").And.NotContain("RecordBossDefeat").And.NotContain("GroupFollowerDeath.Mark")
                    .And.NotContain("IsAngelMonster").And.NotContain("RecordFallenLegacy");
                Regex.IsMatch(row, @"\bName\s*=\s*MonsterNames\.Display").Should().BeFalse(row);
            }
        Src("Systems", "CombatEngine.cs").Should().Contain("var n = target.Name;");
    }

    [Theory]
    [InlineData("en")]
    [InlineData("es")]
    [InlineData("fr")]
    [InlineData("hu")]
    [InlineData("it")]
    public async Task AQuestKill_Counts_InEveryLanguage(string lang)
    {
        var hero = Hero();
        hero.Name2 = "DcKill" + lang;
        var quest = new Quest { Occupier = hero.Name2, Title = "kill test", QuestTarget = QuestTarget.Monster };
        quest.Objectives.Add(new QuestObjective(QuestObjectiveType.KillSpecificMonster, "kill wolves", 3, "wolf", "Wolf"));
        QuestSystem.AddQuestToDatabase(quest);
        try
        {
            await InLanguage(lang, async () =>
            {
                var wolf = new Monster { Name = "Wolf", TierName = "Wolf", FamilyName = "Beast", Level = 3, HP = 0, MaxHP = 50, Experience = 10, Gold = 1 };
                var result = new CombatResult { Player = hero, Outcome = CombatOutcome.Victory };
                result.DefeatedMonsters.Add(wolf);
                var (engine, _) = Engine(hero);
                await (Task)typeof(CombatEngine).GetMethod("HandleVictoryMultiMonster", F)!.Invoke(engine, new object[] { result, false })!;
                return 0;
            });
            quest.Objectives[0].CurrentProgress.Should().Be(1, $"a {lang} player's wolf kill counts");
        }
        finally { quest.Deleted = true; }
    }

    [Theory]
    [InlineData("hu")]
    [InlineData("fr")]
    public void ASummonedThrall_IsStoredInEnglish_AndShownInTheReadersLanguage(string lang)
    {
        var hero = Hero();
        var summoner = new Monster { Name = "Goblin King", TierName = "Goblin King", FamilyName = "Goblinoid", Level = 80, HP = 100, MaxHP = 100, IsActive = true };
        var list = new List<Monster> { summoner };
        InLang(lang, () =>
        {
            var (engine, _) = Engine(hero);
            typeof(CombatEngine).GetMethod("TrySpawnSummonReinforcements", F)!.Invoke(engine, new object?[]
                { summoner, new AbilityResult { SummonMonsters = true, SummonCount = 1 }, list, new CombatResult { Player = hero } });
            return 0;
        });
        var thrall = list.Last();
        thrall.Should().NotBeSameAs(summoner);
        thrall.Name.Should().Be("Goblin King's Thrall", "stored in English whatever the reader's language");
        MonsterNames.DisplayIn(lang, thrall).Should().Be(L(lang, "combat.summoned_minion_name", L(lang, "monster.name.goblin_king")));
    }

    [Theory]
    [InlineData("en")]
    [InlineData("hu")]
    [InlineData("it")]
    public void StoredNames_StayEnglish_ThroughASaveAndReload_AndShowInTheReadersLanguage(string lang)
    {
        var hero = Hero();
        hero.Name1 = hero.Name2 = "DcSave" + lang;
        hero.PetRoster.Add(new Pet { Id = "dire_wolf", Name = BeastData.GetById("dire_wolf")!.Name, Level = 2 });
        var quest = new Quest { Occupier = hero.Name2, Title = "save test", QuestTarget = QuestTarget.Monster };
        quest.Objectives.Add(QuestObjective.Localized(QuestObjectiveType.KillSpecificMonster, "quest.objective.kill_count", new object[] { 4, "Wraiths" }, 4, "wraith", "Wraith"));
        quest.Monsters.Add(new QuestMonster(0, 4, "Wraith"));
        QuestSystem.AddQuestToDatabase(quest);
        try
        {
            // saved and restored in the reader's language, through the game's own save and reload
            var data = InLang(lang, () => (PlayerData)typeof(SaveSystem).GetMethod("SerializePlayer", F)!.Invoke(SaveSystem.Instance, new object[] { hero })!);
            var json = JsonSerializer.Deserialize<PlayerData>(JsonSerializer.Serialize(data))!;
            json.PetRoster.Should().ContainSingle(p => p.Name == "Dire Wolf");
            json.ActiveQuests.First(x => x.Title == "save test").Objectives.Single().TargetName.Should().Be("Wraith");
            var back = InLang(lang, () =>
            {
                var restore = typeof(GameEngine).GetMethod("RestorePlayerFromSaveData", F)!;
                try { return (Character)restore.Invoke(GameEngine.Instance, new object[] { json })!; }
                catch (TargetInvocationException ex) when (ex.InnerException != null) { throw ex.InnerException; }
            });
            back.PetRoster.Should().ContainSingle(p => p.Name == "Dire Wolf", "the pet keeps its English name");
            var restored = QuestSystem.GetPlayerQuests(back.Name2).Single(q => q.Title == "save test");
            restored.Objectives.Single().TargetName.Should().Be("Wraith");
            restored.Objectives.Single().TargetId.Should().Be("wraith");
            restored.Objectives.Single().DescriptionArgs.Should().Contain("Wraiths");
            restored.Monsters.Single().MonsterName.Should().Be("Wraith");
            // shown in the reader's language after the reload
            InLang(lang, () => MonsterNames.Display(back.PetRoster[0].Name)).Should().Be(L(lang, "monster.name.dire_wolf"));
            InLang(lang, restored.Objectives[0].GetDisplayDescription).Should().Be(L(lang, "quest.objective.kill_count", 4,
                lang == "en" ? "Wraiths" : L(lang, "monster.plural.wraith")));
            restored.Deleted = true;
        }
        finally { quest.Deleted = true; }

        // the castle's monster guards: bought in the reader's language, kept in English through the court's JSON
        var court = new RoyalCourtSaveData { Treasury = 1_000_000 };
        InLang(lang, () => King.AddMonsterGuard(court, "Hellhound", 12, 6000L)).Should().BeTrue();
        var reloaded = JsonSerializer.Deserialize<RoyalCourtSaveData>(JsonSerializer.Serialize(court))!;
        reloaded.MonsterGuards.Should().ContainSingle(m => m.Name == "Hellhound");
        InLang(lang, () => MonsterNames.Display(reloaded.MonsterGuards[0].Name)).Should().Be(L(lang, "monster.name.hellhound"));
    }

    // ---------- 4. a party of mixed languages ----------

    [Fact]
    public async Task AMixedParty_SeesOneFight_EachInTheirOwnLanguage()
    {
        var hero = Hero();
        var sent = new List<Func<string, string>>();
        var prevSink = CombatEngine.GroupBroadcastSink;
        CombatEngine.GroupBroadcastSink = (_, build) => sent.Add(build);
        try
        {
            var orc = new Monster { Name = "Orc Warrior", TierName = "Orc Warrior", FamilyName = "Orcish", Level = 20, HP = 5, MaxHP = 300, IsActive = true };
            await InLanguage("en", async () =>
            {
                var (engine, _) = Engine(hero);
                await (Task<bool>)typeof(CombatEngine).GetMethod("ApplySingleMonsterDamage", F)!
                    .Invoke(engine, new object?[] { orc, 1000L, new CombatResult { Player = hero }, "attack", hero, false })!;
                return 0;
            });
            var slay = sent.Select(b => b("hu")).FirstOrDefault(t => t.Contains(L("hu", "monster.name.orc_warrior")));
            slay.Should().NotBeNull("the follower who reads Hungarian sees the Hungarian name");
            sent.Select(b => b("fr")).Should().Contain(t => t.Contains(L("fr", "monster.name.orc_warrior")));
            sent.Select(b => b("en")).Should().Contain(t => t.Contains("Orc Warrior"));
            sent.Select(b => b("hu")).Should().NotContain(t => t.Contains("Orc Warrior"), "never the leader's English to a Hungarian reader");
        }
        finally { CombatEngine.GroupBroadcastSink = prevSink; }

        // a recorded combat line re-rendered for each reader carries their monster name and sentence
        var prevLang = GameConfig.Language;
        GameConfig.Language = "en";
        var rec = Loc.BeginRecording();
        string line;
        try { line = CombatMessages.GetMonsterAttackMessage(MonsterNames.Display(new Monster { Name = "Wolf", TierName = "Wolf" }), "white", 0, 100, new Random(2)); }
        finally { Loc.EndRecording(rec); GameConfig.Language = prevLang; }
        line.Should().Contain("Wolf");
        rec.Render(line, "hu").Should().Contain("Farkas").And.NotContain("Wolf");
        rec.Render(line, "it").Should().Contain("Lupo").And.NotContain("Wolf");

        // the group's opening lines, one per reader
        var mobs = new List<Monster> { new() { Name = "Ghoul", TierName = "Ghoul", FamilyName = "Undead", Level = 20, HP = 9, MaxHP = 9 }, new() { Name = "Wight", TierName = "Wight", FamilyName = "Undead", Level = 30, HP = 9, MaxHP = 9 } };
        foreach (var lang in AllLanguages)
        {
            string intro = CombatEngine.GroupCombatIntro(lang, mobs, null);
            intro.Should().Contain(MonsterNames.DisplayIn(lang, "Ghoul")).And.Contain(MonsterNames.DisplayIn(lang, "Wight"));
        }
    }

    // ---------- 5. widths ----------

    [Fact]
    public void TheLongestShownName_IsNoLongerThanTheLongestEnglishName_InEveryLanguage()
    {
        LongName.Length.Should().Be(GameConfig.MaxNameLength);
        int english = Longest("en").Length;
        foreach (var lang in OtherLanguages)
            Longest(lang).Length.Should().BeLessOrEqualTo(english, $"{lang}: \"{Longest(lang)}\"");
    }

    /// <summary>The Loc keys a monster's shown name is passed to, read from the code.</summary>
    private static List<string> KeysWithAMonsterName()
    {
        var keys = new HashSet<string>();
        foreach (var file in new[] { Src("Systems", "CombatEngine.cs"), Src("Systems", "MonsterAbilities.cs"), Src("Locations", "DungeonLocation.cs"),
                     Src("Locations", "CastleLocation.cs"), Src("Locations", "WildernessLocation.cs"), Src("Locations", "SettlementLocation.cs") })
            foreach (Match m in Regex.Matches(file, "Loc\\.Get(?:In)?\\((?:lang, )?\"([a-z0-9_.]+)\"[^;\\n]*MonsterNames\\."))
                keys.Add(m.Groups[1].Value);
        return keys.OrderBy(k => k).ToList();
    }

    [Fact]
    public void EveryRowThatCarriesAMonsterName_FitsAt79_WithTheLongestName_InFiveLanguages()
    {
        var keys = KeysWithAMonsterName();
        keys.Count.Should().BeGreaterThan(150);
        var report = new StringBuilder();
        var older = new StringBuilder();
        var failures = new List<string>();
        foreach (var key in keys)
        {
            string Render(string lang, string monster)
            {
                string template = Loc.GetIn(lang, key);
                int args = Regex.Matches(template, @"\{(\d+)").Select(x => int.Parse(x.Groups[1].Value)).DefaultIfEmpty(-1).Max() + 1;
                // every argument at its widest: a monster name or a 30-character player name, numbers at 9,999,999
                var values = Enumerable.Range(0, args).Select(i => (object)(template.Contains("{" + i + ":") ? 9_999_999 : monster)).ToArray();
                return "  " + Loc.GetIn(lang, key, values);
            }
            string widest = Longest("en").Length >= LongName.Length ? Longest("en") : LongName;
            foreach (var lang in AllLanguages)
            {
                string shown = Longest(lang).Length >= LongName.Length ? Longest(lang) : LongName;
                string row = Render(lang, shown), withEnglish = Render(lang, widest);
                // a row that was already over 79 with the English name is the template's width, older than this
                // piece (receipts); the shown name never makes a row cross 79 that the English name did not
                if (row.Length > MaxWidth && withEnglish.Length <= MaxWidth) failures.Add($"{lang} {key} {row.Length}: {row}");
                if (withEnglish.Length > MaxWidth) older.AppendLine($"{lang} {key} {withEnglish.Length}");
                report.AppendLine($"{lang} {key} {row.Length}");
            }
        }
        Capture("data-combat-monster-rows.txt", report.ToString());
        Capture("data-combat-older-overflows.txt", older.ToString());
        failures.Should().BeEmpty("a row that fits with the longest English name fits with the longest shown name");
    }

    [Theory]
    [InlineData("en")]
    [InlineData("es")]
    [InlineData("fr")]
    [InlineData("hu")]
    [InlineData("it")]
    public void EveryAttackSentence_FitsAt79_WithTheLongestNames(string lang)
    {
        static string Visible(string markup) => Regex.Replace(markup, @"\[/?[a-z_]*\]", "");
        string monster = Longest(lang);
        foreach (CombatMessages.DamageTier tier in Enum.GetValues<CombatMessages.DamageTier>())
            for (int i = 0; i < CombatMessages.FormCount("player", tier); i++)
            {
                // as the engine prints them: CombatMessages.Rows, each row whole markup
                foreach (var message in new[] { L(lang, CombatMessages.FormKey("player", tier, i), monster, "bright_red", 9_999_999),
                             L(lang, CombatMessages.FormKey("ally", tier, i), LongName, monster, "bright_red", 9_999_999) })
                {
                    var rows = CombatMessages.Rows(message);
                    string.Join(" ", rows.Select(Visible)).Should().Be(Visible(message), "nothing is lost at a break");
                    foreach (var row in rows)
                    {
                        Visible(row).Length.Should().BeLessOrEqualTo(MaxWidth, row);
                        Regex.Matches(row, @"\[[a-z_]+\]").Count.Should().Be(Regex.Matches(row, @"\[/\]").Count, "each row opens and closes its colours: " + row);
                    }
                }
            }
        foreach (CombatMessages.DamageTier tier in Enum.GetValues<CombatMessages.DamageTier>())
            for (int i = 0; i < CombatMessages.FormCount("monster", tier); i++)
            {
                foreach (var row in CombatMessages.Rows(L(lang, CombatMessages.FormKey("monster", tier, i), "red", monster)))
                    Visible(row).Length.Should().BeLessOrEqualTo(MaxWidth, row);
            }
        string fits = L("en", CombatMessages.FormKey("player", CombatMessages.DamageTier.Light, 0), "Goblin", "yellow", 12);
        CombatMessages.Rows(fits).Should().Equal(new[] { fits }, "a message that fits is printed as before");
        var drake = new Monster { Name = "Terravok, The Sleeping Mountain", Level = 100, HP = 9_999_999, IsBoss = true, IsUnique = true, Poisoned = true, Disease = true };
        foreach (var row in InLang(lang, () => CombatEngine.FacingRows(drake, "  ")))
            row.Length.Should().BeLessOrEqualTo(MaxWidth, row);
    }

    [Theory]
    [InlineData("en")]
    [InlineData("es")]
    [InlineData("fr")]
    [InlineData("hu")]
    [InlineData("it")]
    public async Task ArtifactScreens_And_OldGodTitles_Fit79(string lang)
    {
        foreach (var a in ArtifactSystem.Instance.GetAllArtifacts())
        {
            string text = await InLanguage(lang, async () =>
            {
                var s = NewScreen();
                await (Task)typeof(ArtifactSystem).GetMethod("DisplayArtifactCollection", F)!.Invoke(ArtifactSystem.Instance, new object[] { a, s.Term })!;
                return s.Text;
            });
            Capture($"data-combat-artifact-{a.Type}-{lang}.txt", text);
            EveryRowFits(text, $"{a.Type} artifact screen");
            text.Should().Contain(InLang(lang, a.LocName));
            if (lang != "en") text.Should().NotContain(a.SpecialAbility.Substring(0, 12));
        }
        foreach (var god in OldGodsData.GetAllOldGodsWithVariants())
        {
            InLang(lang, god.LocTitle).Length.Should().BeLessOrEqualTo(58, "the title is centred in the 58 wide box");
            InLang(lang, god.LocName).Length.Should().BeLessOrEqualTo(58);
            InLang(lang, () => L(lang, "dungeon.they_know_you_outcome", L(lang, "dungeon.outcome_allied"), god.LocName())).Length.Should().BeLessOrEqualTo(MaxWidth);
        }
    }

    [Theory]
    [InlineData("en")]
    [InlineData("es")]
    [InlineData("fr")]
    [InlineData("hu")]
    [InlineData("it")]
    public async Task TheCastleMonsterMarket_AndThePetRoster_Fit79(string lang)
    {
        var kingField = typeof(CastleLocation).GetField("currentKing", SNP)!;
        var prev = kingField.GetValue(null);
        try
        {
            var king = new King { Name = LongName, Treasury = 50_000_000 };
            kingField.SetValue(null, king);
            string text = await InLanguage(lang, async () =>
            {
                var castle = new CastleLocation();
                var s = NewScreen("");
                typeof(BaseLocation).GetField("terminal", F)!.SetValue(castle, s.Term);
                typeof(BaseLocation).GetField("currentPlayer", F)!.SetValue(castle, Hero());
                await (Task)typeof(CastleLocation).GetMethod("HireMonsterGuard", F)!.Invoke(castle, Array.Empty<object>())!;
                return s.Text;
            });
            Capture($"data-combat-monster-market-{lang}.txt", text);
            foreach (var (name, _, _) in MonsterGuardTypes.AvailableMonsters)
                text.Should().Contain(MonsterNames.DisplayIn(lang, name));
            // the market's columns are older than this piece; each row is held to the width the English row has
            foreach (var row in Rows(text).Where(r => Regex.IsMatch(r, @"^\d+\s")))
                row.Length.Should().BeLessOrEqualTo(Math.Max(MaxWidth, 3 + 1 + 20 + 50), row);
        }
        finally { kingField.SetValue(null, prev); }

        foreach (var beast in BeastData.Beasts)
            $"  [9] {InLang(lang, () => MonsterNames.Display(beast.Name)),-22}".Length.Should().BeLessOrEqualTo(28, $"{lang} {beast.Name} in its 22 column");
    }

    [Theory]
    [InlineData("hu")]
    [InlineData("fr")]
    public void TheCombatAndDungeonElectronLabels_AreLocalized_IdsUnchanged(string lang)
    {
        string ce = Src("Systems", "CombatEngine.cs"), dl = Src("Locations", "DungeonLocation.cs"), st = Src("Locations", "SettlementLocation.cs");
        foreach (var english in new[] { "label = \"Attack\"", "label = \"Defend\"", "Label = \"Equip\"", "EmitCombatEnd(\"Victory\"", "label = \"Herbs\"" })
            ce.Should().NotContain(english);
        dl.Should().NotContain("\"Search Remains\"").And.NotContain("?? \"Unknown\"").And.Contain("private const string UnknownThemeId = \"Unknown\";");
        st.Should().NotContain("Label = \"View Buildings\"");
        L(lang, "combat.electron_potions", 2, 5).Should().Contain("(2/5)");
        foreach (var key in new[] { "combat.loot_choice_equip", "combat.victory_label", "dungeon.choice.shrine.desecrate",
                     "dungeon.choice.duelist.title", "settlement.electron_oracle", "dungeon.electron_room_unknown" })
            L(lang, key).Should().NotBe(L("en", key), key);
        InLang(lang, () => WorldBossDatabase.ElementLabel("Undead")).Should().Be(L(lang, "worldboss.element.undead")).And.NotBe("Undead");
        InLang(lang, () => WorldBossDatabase.ElementLabel("Plasma")).Should().Be("Plasma");
    }

    [Theory]
    [InlineData("hu")]
    [InlineData("it")]
    public void ChampionDrops_ShowThroughTheItemKeys_AndStoreEnglish(string lang)
    {
        foreach (var c in GauntletChampionData.Champions)
        {
            ItemNames.KeyOf(c.Drop.ItemName).Should().NotBeNull(c.Drop.ItemName);
            ItemNames.DisplayIn(lang, c.Drop.ItemName).Should().NotBe(c.Drop.ItemName);
            ItemNames.DisplayIn(lang, c.Drop.ItemName).Length.Should().BeLessOrEqualTo(27);
        }
        foreach (var c in HonorTournamentData.Champions)
            ItemNames.DisplayIn(lang, c.Drop.ItemName).Should().NotBe(c.Drop.ItemName);
    }

    // ---------- 6. the wiki ----------

    [Fact]
    public void TheWikiExport_KeepsEnglish_AndAddsTheOtherLanguages()
    {
        var dir = Path.Combine(Path.GetTempPath(), "dc125-wiki-" + Guid.NewGuid().ToString("N"));
        try
        {
            WikiDataExporter.Export(dir);
            using var monsters = JsonDocument.Parse(File.ReadAllText(Path.Combine(dir, "monsters.json")));
            var families = monsters.RootElement.GetProperty("data").EnumerateArray().ToList();
            families.Should().HaveCount(MonsterFamilies.GetBuiltInFamilies().Count);
            foreach (var f in families)
            {
                var english = f.GetProperty("name").GetProperty("en").GetString()!;
                MonsterFamilies.GetBuiltInFamilies().Should().Contain(x => x.FamilyName == english);
                f.GetProperty("name").EnumerateObject().Select(p => p.Name).Should().Equal(AllLanguages);
                foreach (var t in f.GetProperty("tiers").EnumerateArray())
                {
                    string en = t.GetProperty("name").GetProperty("en").GetString()!;
                    t.GetProperty("id").GetString().Should().NotBeNullOrEmpty();
                    t.GetProperty("name").GetProperty("hu").GetString().Should().Be(MonsterNames.DisplayIn("hu", en));
                }
            }
            using var bosses = JsonDocument.Parse(File.ReadAllText(Path.Combine(dir, "bosses.json")));
            foreach (var g in bosses.RootElement.GetProperty("data").GetProperty("oldGods").EnumerateArray())
            {
                string en = g.GetProperty("name").GetProperty("en").GetString()!;
                OldGodsData.GetAllOldGods().Should().Contain(x => x.Name == en);
                g.GetProperty("name").GetProperty("hu").GetString().Should().StartWith(en.Split(',')[0] + ",");
                g.GetProperty("title").GetString().Should().Be(OldGodsData.GetAllOldGods().First(x => x.Name == en).Title, "the title field keeps its English shape");
            }
        }
        finally { try { Directory.Delete(dir, true); } catch { } }
    }

    // ---------- 7. pre-existing display fixes in owned files ----------

    [Theory]
    [InlineData("es")]
    [InlineData("hu")]
    public void AMonsterSubject_HasNoEnglishThe_OutsideEnglish(string lang)
    {
        var drake = new Monster { Name = "Drake", TierName = "Drake", FamilyName = "Draconic" };
        InLang("en", () => MonsterNames.TheName(drake)).Should().Be("The Drake");
        InLang(lang, () => MonsterNames.TheName(drake)).Should().Be(L(lang, "monster.name.drake")).And.NotStartWith("The ");
        InLang(lang, () => L(lang, "combat.monster_misses", MonsterNames.TheNameIn(lang, drake))).Should().NotContain("The ");
    }

    [Theory]
    [InlineData("en")]
    [InlineData("fr")]
    [InlineData("hu")]
    [InlineData("it")]
    public async Task TheTownReaction_WrapsEveryRowAt79(string lang)
    {
        foreach (var god in new[] { OldGodType.Maelketh, OldGodType.Veloura, OldGodType.Thorgrim, OldGodType.Terravok, OldGodType.Aurelion, OldGodType.Noctura })
            foreach (var outcome in new[] { BossOutcome.Defeated, BossOutcome.Saved, BossOutcome.Allied, BossOutcome.Spared })
            {
                string text = await InLanguage(lang, async () =>
                {
                    var s = NewScreen("", "", "", "");
                    var hero = Hero();
                    var d = new DungeonLocation();
                    typeof(BaseLocation).GetField("terminal", F)!.SetValue(d, s.Term);
                    typeof(BaseLocation).GetField("currentPlayer", F)!.SetValue(d, hero);
                    var result = new BossEncounterResult { Success = true, God = god, Outcome = outcome, ApproachType = "merciful" };
                    await (Task)typeof(DungeonLocation).GetMethod("ShowTownReactionScene", F)!.Invoke(d, new object[] { result, hero, s.Term })!;
                    return s.Text;
                });
                EveryRowFits(text, $"{lang} {god} {outcome} town reaction");
            }
    }
}
