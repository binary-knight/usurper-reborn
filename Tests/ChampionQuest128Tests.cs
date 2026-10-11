using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using FluentAssertions;
using UsurperRemake;
using UsurperRemake.Systems;
using Xunit;
using Xunit.Abstractions;

namespace UsurperReborn.Tests;

/// <summary>
/// 1.2.8, the board's champion quest names the monster the player fights. The quest stores its target as
/// "{tier} Champion" (quest.title.champion); the dungeon champion is named by MonsterGenerator.GetChampionName
/// ("Zombie Revenant", "Alpha Wolf", ...) and shown by MonsterNames. QuestSystem.ShowMonsterArgs now shows the
/// stored target as that fight name in the reader's language. Nothing stored changes.
/// </summary>
[Collection("SharedGameSingletons")]
public class ChampionQuest128Tests
{
    private static readonly string[] Langs = { "en", "es", "fr", "hu", "it" };
    private readonly ITestOutputHelper _out;
    public ChampionQuest128Tests(ITestOutputHelper output) { _out = output; }

    private static T In<T>(string lang, Func<T> read)
    {
        string saved = GameConfig.Language;
        try { GameConfig.Language = lang; return read(); }
        finally { GameConfig.Language = saved; }
    }

    private static string TierId(string tier) => tier.ToLower().Replace(" ", "_");

    /// <summary>The champion of a tier as the dungeon makes it: the name GetChampionName writes, the tier and the family.</summary>
    private static Monster Champion(string family, string tier) =>
        new() { Name = MonsterGenerator.GetChampionName(tier, family), TierName = tier, FamilyName = family, IsMiniBoss = true };

    /// <summary>A board quest for the tier's champion with the fields creation stores (the stored target in storedLang).</summary>
    private static Quest StoredQuest(string tier, string storedLang = "en")
    {
        string stored = Loc.GetIn(storedLang, "quest.title.champion", tier);
        var quest = new Quest
        {
            QuestTarget = QuestTarget.ClearBoss, Difficulty = 4, Date = DateTime.Now, DaysToComplete = 30,
            Title = Loc.GetIn(storedLang, "quest.title.defeat_boss", stored), TitleKey = "quest.title.defeat_boss", TitleArgs = new List<string> { stored },
        };
        quest.Objectives.Add(new QuestObjective
        {
            ObjectiveType = QuestObjectiveType.KillBoss, DescriptionKey = "quest.objective.defeat_boss", DescriptionArgs = new List<string> { stored },
            Description = Loc.GetIn(storedLang, "quest.objective.defeat_boss", stored), RequiredProgress = 1, TargetId = TierId(tier), TargetName = stored,
        });
        return quest;
    }

    public static IEnumerable<object[]> EveryTierInEveryLanguage() =>
        from family in MonsterFamilies.AllFamilies
        from tier in family.Tiers
        from lang in Langs
        select new object[] { family.FamilyName, tier.Name, lang };

    // ---------------------------------------------------------------- the quest shows the fight name

    [Theory]
    [MemberData(nameof(EveryTierInEveryLanguage))]
    public void TheQuest_ShowsTheNameTheChampionHasInAFight(string family, string tier, string lang)
    {
        string fight = MonsterNames.DisplayIn(lang, Champion(family, tier));
        fight.Should().NotBeNullOrEmpty();
        var quest = StoredQuest(tier);
        In(lang, quest.GetDisplayTitle).Should().Be(Loc.GetIn(lang, "quest.title.defeat_boss", fight));
        In(lang, quest.Objectives[0].GetDisplayDescription).Should().Be(Loc.GetIn(lang, "quest.objective.defeat_boss", fight));
        In(lang, quest.Objectives[0].GetDisplayString).Should().Contain(fight);
    }

    [Fact]
    public void TheQuest_ShowsTheNameOfTheChampionTheDungeonGenerates()
    {
        // champions from the generator itself, floors 1 to 100: the quest for the champion's tier shows its name
        var seen = new HashSet<string>();
        for (int floor = 1; floor <= 100; floor++)
        {
            var champion = MonsterGenerator.GenerateMonster(floor, isBoss: false, isMiniBoss: true, random: new Random(floor));
            champion.Name.Should().Be(MonsterGenerator.GetChampionName(champion.TierName, champion.FamilyName));
            seen.Add(champion.FamilyName);
            foreach (string lang in Langs)
                In(lang, StoredQuest(champion.TierName).GetDisplayTitle).Should().Be(Loc.GetIn(lang, "quest.title.defeat_boss", MonsterNames.DisplayIn(lang, champion)));
        }
        seen.Count.Should().BeGreaterThan(5);
    }

    [Fact]
    public void TheFightName_IsTranslated_ForEveryFamily()
    {
        // every family's template and the two shared ones exist in the five languages, and the English one is the stored name
        foreach (var family in MonsterFamilies.AllFamilies)
            foreach (var tier in family.Tiers)
            {
                var champion = Champion(family.FamilyName, tier.Name);
                MonsterNames.DisplayIn("en", champion).Should().Be(champion.Name);
                MonsterNames.ChampionOfTierIn("en", tier.Name, family.FamilyName).Should().Be(champion.Name);
                foreach (string lang in Langs)
                    MonsterNames.ChampionOfTierIn(lang, tier.Name, family.FamilyName).Should().Be(MonsterNames.DisplayIn(lang, champion));
                Langs.Skip(1).Count(lang => MonsterNames.DisplayIn(lang, champion) != champion.Name).Should().BeGreaterThanOrEqualTo(3, "{0} is translated", champion.Name);
            }
    }

    [Fact]
    public void ATierName_BelongsToOneFamily()
    {
        var tiers = MonsterFamilies.AllFamilies.SelectMany(f => f.Tiers.Select(t => TierId(t.Name))).ToList();
        tiers.Should().OnlyHaveUniqueItems("the quest finds the family from the tier in TargetId");
    }

    // ---------------------------------------------------------------- what is stored does not change

    [Fact]
    public void Creation_StoresWhatItStored()
    {
        var generate = typeof(QuestSystem).GetMethod("GenerateDungeonQuestObjectives", BindingFlags.NonPublic | BindingFlags.Static)!;
        foreach (string lang in new[] { "en", "hu" })
            for (int level = 1; level <= 100; level += 9)
            {
                var quest = new Quest { QuestTarget = QuestTarget.ClearBoss, Difficulty = 2, Date = DateTime.Now, DaysToComplete = 30 };
                In(lang, () => generate.Invoke(null, new object[] { quest, level, 0 }));
                var kill = quest.Objectives.Single();
                string tier = MonsterFamilies.AllFamilies.SelectMany(f => f.Tiers).First(t => TierId(t.Name) == kill.TargetId).Name;
                string stored = tier + " Champion";
                kill.ObjectiveType.Should().Be(QuestObjectiveType.KillBoss);
                kill.TargetName.Should().Be(stored);
                kill.DescriptionKey.Should().Be("quest.objective.defeat_boss");
                kill.DescriptionArgs.Should().Equal(stored);
                kill.Description.Should().Be(Loc.GetIn(lang, "quest.objective.defeat_boss", stored));
                kill.RequiredProgress.Should().Be(1);
                quest.Title.Should().Be("Slay the " + stored);
                quest.TitleKey.Should().Be("quest.title.defeat_boss");
                quest.TitleArgs.Should().Equal(stored);
            }
    }

    [Fact]
    public void ShowingAQuest_WritesNothingBack()
    {
        var quest = StoredQuest("Zombie");
        foreach (string lang in Langs) { In(lang, quest.GetDisplayTitle); In(lang, quest.Objectives[0].GetDisplayDescription); }
        quest.Title.Should().Be("Slay the Zombie Champion");
        quest.TitleArgs.Should().Equal("Zombie Champion");
        quest.Objectives[0].TargetName.Should().Be("Zombie Champion");
        quest.Objectives[0].DescriptionArgs.Should().Equal("Zombie Champion");
        quest.Objectives[0].TargetId.Should().Be("zombie");
    }

    [Fact]
    public void TheReadersOfTheStoredTitle_ReadItAsBefore()
    {
        string root = AppContext.BaseDirectory;
        while (!File.Exists(Path.Combine(root, "usurper-reloaded.csproj"))) root = Path.GetDirectoryName(root)!;
        string quests = File.ReadAllText(Path.Combine(root, "Scripts", "Systems", "QuestSystem.cs"));
        quests.Should().Contain(".GroupBy(q => q.Title)");
        quests.Should().Contain("internal static string BountyClaimKey(Quest q) => BountyClaimKey(q.Id, q.Initiator, q.TargetNPCName, q.Title);");
        quests.Should().Contain("var bossName = Loc.GetIn(\"en\", \"quest.title.champion\", tier.Name);");
        File.ReadAllText(Path.Combine(root, "Scripts", "Systems", "OnlineStateManager.cs"))
            .Should().Contain("private static string QuestKey(QuestData q) => !string.IsNullOrEmpty(q.Id) ? q.Id : \"title:\" + q.Title + \"|\" + q.Initiator;");
        // two quests for one tier's champion carry one stored title, the key the board groups by
        StoredQuest("Wolf").Title.Should().Be(StoredQuest("Wolf").Title).And.Be("Slay the Wolf Champion");
        QuestSystem.BountyClaimKey("", "board", "", StoredQuest("Wolf").Title).Should().Be(QuestSystem.BountyClaimKey("", "board", "", "Slay the Wolf Champion"));
    }

    // ---------------------------------------------------------------- a quest saved before the change

    [Fact]
    public void AQuestSavedBeforeTheChange_ShowsTheFightName()
    {
        // the stored fields as a 1.2.7 save holds them, written out
        var quest = new Quest
        {
            QuestTarget = QuestTarget.ClearBoss, Difficulty = 2, Title = "Slay the Zombie Champion",
            TitleKey = "quest.title.defeat_boss", TitleArgs = new List<string> { "Zombie Champion" },
        };
        quest.Objectives.Add(new QuestObjective
        {
            ObjectiveType = QuestObjectiveType.KillBoss, DescriptionKey = "quest.objective.defeat_boss", DescriptionArgs = new List<string> { "Zombie Champion" },
            Description = "Defeat the Zombie Champion", RequiredProgress = 1, TargetId = "zombie", TargetName = "Zombie Champion",
        });
        In("en", quest.GetDisplayTitle).Should().Be("Slay the Zombie Revenant");
        In("en", quest.Objectives[0].GetDisplayDescription).Should().Be("Defeat the Zombie Revenant");
        In("hu", quest.GetDisplayTitle).Should().Be(Loc.GetIn("hu", "quest.title.defeat_boss", MonsterNames.DisplayIn("hu", Champion("Undead", "Zombie"))));
        In("hu", quest.GetDisplayTitle).Should().NotContain("Bajnok");
    }

    [Theory]
    [InlineData("es")]
    [InlineData("fr")]
    [InlineData("hu")]
    [InlineData("it")]
    public void AQuestStoredInItsCreatorsLanguage_ShowsTheFightName(string creator)
    {
        // before 1.2.5 the target was stored in the creator's language around the English tier
        var quest = StoredQuest("Wolf", creator);
        foreach (string lang in Langs)
            In(lang, quest.GetDisplayTitle).Should().Be(Loc.GetIn(lang, "quest.title.defeat_boss", MonsterNames.DisplayIn(lang, Champion("Beast", "Wolf"))));
        In("en", quest.GetDisplayTitle).Should().Be("Slay the Alpha Wolf");
    }

    [Fact]
    public void AQuestWhoseTierIsNotKnown_OrWhoseTargetIsOtherText_ShowsWhatItStores()
    {
        var unknown = StoredQuest("Zombie");
        unknown.Objectives[0].TargetId = "no_such_tier";
        In("hu", unknown.GetDisplayTitle).Should().Be(Loc.GetIn("hu", "quest.title.defeat_boss", "Zombie Champion"));
        var other = StoredQuest("Zombie");
        other.TitleArgs = new List<string> { "The Bone King" };
        other.Objectives[0].DescriptionArgs = new List<string> { "The Bone King" };
        other.Objectives[0].TargetName = "The Bone King";
        In("hu", other.GetDisplayTitle).Should().Be(Loc.GetIn("hu", "quest.title.defeat_boss", "The Bone King"));
        In("en", other.Objectives[0].GetDisplayDescription).Should().Be("Defeat the The Bone King");
    }

    // ---------------------------------------------------------------- 79 columns

    [Theory]
    [InlineData("en")]
    [InlineData("hu")]
    public void TheQuestHallList_AndTheJournal_FitIn79Columns_WithTheLongestName(string lang)
    {
        var all = MonsterFamilies.AllFamilies.SelectMany(f => f.Tiers.Select(t => (Family: f.FamilyName, Tier: t.Name))).ToList();
        var longest = all.OrderByDescending(t => MonsterNames.DisplayIn(lang, Champion(t.Family, t.Tier)).Length).First();
        string name = MonsterNames.DisplayIn(lang, Champion(longest.Family, longest.Tier));
        _out.WriteLine("{0}: longest champion name {1} columns [{2}]", lang, name.Length, name);
        var quest = StoredQuest(longest.Tier);
        quest.Objectives[0].CurrentProgress = 0;
        var rows = In(lang, () => new List<string>
        {
            // QuestHallLocation: the list row and its screen reader form
            $" [20] [{quest.GetDifficultyString()}] {quest.GetDisplayTitle()}",
            $"20. {quest.GetDifficultyString()} - {quest.GetDisplayTitle()}",
            // QuestHallLocation: the objective row of the quest's details; the objective's own status form
            $"    [ ] {quest.Objectives[0].GetDisplayDescription()} (0/1)",
            quest.Objectives[0].GetDisplayString(),
            // JournalSystem: the quest row, its objective row, the ready row
            $"{quest.GetDisplayTitle()} ({Loc.Get("base.quest_days_left", 30)})",
            $"  [ ] {quest.Objectives[0].GetDisplayDescription()} (0/1)",
            Loc.Get("journal.line_quest_ready", quest.GetDisplayTitle()),
            // BaseLocation and DungeonLocation: the objective row of the quest log
            $"    [ ] {quest.Objectives[0].GetDisplayDescription()}",
        });
        foreach (string row in rows)
        {
            row.Should().Contain(name);
            row.Length.Should().BeLessThanOrEqualTo(79, "{0}: [{1}]", lang, row);
        }
    }

    // ---------------------------------------------------------------- es, fr and it: no article before the name

    private static readonly string[] Reworded = { "es", "fr", "it" };

    /// <summary>Words that stand as an article (or a preposition joined to one) before a noun in es, fr and it.</summary>
    private static readonly string[] Articles =
    {
        "al", "el", "la", "los", "las", "lo", "un", "una", "del", "le", "les", "du", "des", "au", "aux", "une",
        "il", "i", "gli", "uno", "dello", "della", "allo", "alla", "de", "di", "a",
    };

    public static IEnumerable<object[]> EveryTierInTheRewordedLanguages() =>
        from family in MonsterFamilies.AllFamilies
        from tier in family.Tiers
        from lang in Reworded
        select new object[] { family.FamilyName, tier.Name, lang };

    [Theory]
    [MemberData(nameof(EveryTierInTheRewordedLanguages))]
    public void TheTitle_AndTheObjectiveLine_HoldTheFightName_WithNoArticleBeforeIt(string family, string tier, string lang)
    {
        string fight = MonsterNames.DisplayIn(lang, Champion(family, tier));
        var quest = StoredQuest(tier);
        foreach (string text in new[] { In(lang, quest.GetDisplayTitle), In(lang, quest.Objectives[0].GetDisplayDescription) })
        {
            text.Should().EndWith(fight);
            string lead = text.Substring(0, text.Length - fight.Length);
            lead.Should().EndWith(": ", "{0}: the name follows a colon", lang);
            string before = lead.TrimEnd(' ', ':').Split(' ').Last().ToLowerInvariant();
            before.Should().NotBeEmpty();
            Articles.Should().NotContain(before, "{0}: [{1}]", lang, text);
            before.Should().NotContain("'", "no elided article");
        }
    }

    public static readonly object[][] FirstTierTitles =
    {
        // family, the title of its first tier's quest in es, fr, it
        new object[] { "Goblinoid", "Destruye: Campeón Goblin", "Tuer : Champion Gobelin", "Sconfiggi: Campione Goblin" },
        new object[] { "Undead", "Destruye: Zombi Aparecido", "Tuer : Zombie Revenant", "Sconfiggi: Zombi Redivivo" },
        new object[] { "Orcish", "Destruye: Orco Jefe de Guerra", "Tuer : Orc Chef de Guerre", "Sconfiggi: Orco Capoguerra" },
        new object[] { "Draconic", "Destruye: Kobold Anciano", "Tuer : Kobold Aîné", "Sconfiggi: Coboldo Anziano" },
        new object[] { "Demonic", "Destruye: Diablillo Soberano", "Tuer : Diablotin Suzerain", "Sconfiggi: Diavoletto Sovrano" },
        new object[] { "Giant", "Destruye: Ogro Titánico", "Tuer : Ogre Titanesque", "Sconfiggi: Orco Gigante Titanico" },
        new object[] { "Beast", "Destruye: Lobo Alfa", "Tuer : Loup Alpha", "Sconfiggi: Lupo Alfa" },
        new object[] { "Elemental", "Destruye: Chispa Primordial", "Tuer : Étincelle Primordial", "Sconfiggi: Scintilla Primo" },
        new object[] { "Aberration", "Destruye: Cieno Abominable", "Tuer : Limon Abominable", "Sconfiggi: Melma Abominevole" },
        new object[] { "Insectoid", "Destruye: Araña Gigante Rey Colmena", "Tuer : Araignée Géante Roi-Ruche", "Sconfiggi: Ragno Gigante Re Alveare" },
        new object[] { "Construct", "Destruye: Armadura Animada Primario", "Tuer : Armure Animée Primaire", "Sconfiggi: Armatura Animata Primario" },
        new object[] { "Fey", "Destruye: Duendecillo Archihada", "Tuer : Pixie Archifée", "Sconfiggi: Folletto Arcifata" },
        new object[] { "Aquatic", "Destruye: Tritón Leviatán", "Tuer : Merrow Léviathan", "Sconfiggi: Tritone Leviatano" },
        new object[] { "Celestial", "Destruye: Ángel Serafín", "Tuer : Ange Séraphin", "Sconfiggi: Angelo Serafino" },
        new object[] { "Shadow", "Destruye: Sombra Señor Nocturno", "Tuer : Ombre Nocturne", "Sconfiggi: Ombra Notturno" },
    };

    [Theory]
    [MemberData(nameof(FirstTierTitles))]
    public void TheFirstTierOfEachFamily_ReadsLikeThis(string family, string es, string fr, string it)
    {
        string tier = MonsterFamilies.AllFamilies.Single(f => f.FamilyName == family).Tiers[0].Name;
        var quest = StoredQuest(tier);
        In("es", quest.GetDisplayTitle).Should().Be(es);
        In("fr", quest.GetDisplayTitle).Should().Be(fr);
        In("it", quest.GetDisplayTitle).Should().Be(it);
    }

    [Fact]
    public void TheBossQuestKeys_InEnglishAndHungarian_AreWhatTheyWere_AndTheOthersTakeAColon()
    {
        Loc.GetIn("en", "quest.title.defeat_boss", "X").Should().Be("Slay the X");
        Loc.GetIn("en", "quest.objective.defeat_boss", "X").Should().Be("Defeat the X");
        Loc.GetIn("hu", "quest.title.defeat_boss", "X").Should().Be("V\u00e9gezd ki: X");
        Loc.GetIn("hu", "quest.objective.defeat_boss", "X").Should().Be("Gy\u0151zd le: X");
        Loc.GetIn("es", "quest.title.defeat_boss", "X").Should().Be("Destruye: X");
        Loc.GetIn("es", "quest.objective.defeat_boss", "X").Should().Be("Derrota: X");
        Loc.GetIn("fr", "quest.title.defeat_boss", "X").Should().Be("Tuer : X");
        Loc.GetIn("fr", "quest.objective.defeat_boss", "X").Should().Be("Vaincre : X");
        Loc.GetIn("it", "quest.title.defeat_boss", "X").Should().Be("Sconfiggi: X");
        Loc.GetIn("it", "quest.objective.defeat_boss", "X").Should().Be("Sconfiggi: X");
    }

    [Theory]
    [InlineData("es")]
    [InlineData("fr")]
    [InlineData("it")]
    public void TheLongestRows_InTheRewordedLanguages_AreMeasured(string lang)
    {
        // 79 columns is not asserted in these languages; the widths are written out
        var all = MonsterFamilies.AllFamilies.SelectMany(f => f.Tiers.Select(t => (Family: f.FamilyName, Tier: t.Name))).ToList();
        var longest = all.OrderByDescending(t => MonsterNames.DisplayIn(lang, Champion(t.Family, t.Tier)).Length).First();
        var quest = StoredQuest(longest.Tier);
        string hall = In(lang, () => $" [20] [{quest.GetDifficultyString()}] {quest.GetDisplayTitle()}");
        string journal = In(lang, () => $"{quest.GetDisplayTitle()} ({Loc.Get("base.quest_days_left", 30)})");
        string objective = In(lang, () => $"  [ ] {quest.Objectives[0].GetDisplayDescription()} (0/1)");
        _out.WriteLine("LONGEST {0} hall {1} [{2}]", lang, hall.Length, hall);
        _out.WriteLine("LONGEST {0} journal {1} [{2}]", lang, journal.Length, journal);
        _out.WriteLine("LONGEST {0} journal objective {1} [{2}]", lang, objective.Length, objective);
        hall.Should().Contain(": ");
    }
}
