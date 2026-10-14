using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using FluentAssertions;
using FluentAssertions.Execution;
using UsurperRemake;
using UsurperRemake.Data;
using UsurperRemake.Locations;
using UsurperRemake.Systems;
using Xunit;

namespace UsurperReborn.Tests;

/// <summary>
/// 1.3.2: seven world faults.
/// 1. Crown monster bounties stored a doubled reward tier (2, 4 or 6) that the reward code did not know, so the
///    harder ones paid the lowest tier. A Crown bounty now pays twice the gold and XP of its base tier.
/// 2. The royal quest offer showed a reward nothing paid. It now shows the advance paid on accepting and what
///    completion pays.
/// 3. The jailbreak had no limit. It is now once a day (a saved daily flag), and the warning about prison is gone.
/// 4. The Church blessing lines claimed a duration nothing reads. They now name the effects applied.
/// 5. The guard screens printed one cap of 20; each now prints the cap of the action it offers, or both.
/// 6. The monster market showed feeding, HP, Strength and Defence without the terms above level 30.
/// 7. Three druid shrine texts claimed effects with no code.
/// All in memory.
/// </summary>
[Collection("SharedGameSingletons")]
public class WorldFixes132Tests : IDisposable
{
    private const BindingFlags F = BindingFlags.NonPublic | BindingFlags.Instance;
    private const BindingFlags S = BindingFlags.NonPublic | BindingFlags.Static;
    private static readonly string[] Langs = { "en", "es", "fr", "hu", "it" };

    private readonly bool _pacingWas;
    private readonly King? _kingBefore = CastleLocation.GetCurrentKing();
    private readonly string _langWas = GameConfig.Language;
    private readonly bool _srWas = GameConfig.ScreenReaderMode, _compactWas = GameConfig.CompactMode;

    public WorldFixes132Tests()
    {
        _pacingWas = Pacing.Disabled;
        Pacing.Disabled = true;
        GameConfig.ScreenReaderMode = false;
        GameConfig.CompactMode = false;
    }

    public void Dispose()
    {
        Pacing.Disabled = _pacingWas;
        CastleLocation.SetKing(_kingBefore!);
        GameConfig.Language = _langWas;
        GameConfig.ScreenReaderMode = _srWas;
        GameConfig.CompactMode = _compactWas;
    }

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
        s.Term = new TerminalEmulator(new LineStream(lines.Concat(Enumerable.Repeat("", 20)), _ =>
        {
            s.Term.StreamWriterInternal?.Flush();
            s.Output.WriteByte((byte)'\n');
        }), s.Output);
        return s;
    }

    private static Screen At(BaseLocation loc, Character hero, params string[] lines)
    {
        var s = NewScreen(lines);
        typeof(BaseLocation).GetField("terminal", F)!.SetValue(loc, s.Term);
        typeof(BaseLocation).GetField("currentPlayer", F)!.SetValue(loc, hero);
        return s;
    }

    private static async Task Run(object target, string method, params object?[] args)
    {
        var m = target.GetType().GetMethod(method, F);
        m.Should().NotBeNull(method);
        try
        {
            var r = m!.Invoke(target, args);
            if (r is Task t)
            {
                (await Task.WhenAny(t, Task.Delay(TimeSpan.FromSeconds(60)))).Should().BeSameAs(t, "{0} must return", method);
                await t;
            }
        }
        catch (TargetInvocationException ex) when (ex.InnerException != null) { throw ex.InnerException; }
        catch (IOException) { }   // the scripted input ran out
    }

    private static List<string> Rows(string text) => text.Replace("\r", "").Split('\n').ToList();

    private static string RepoRoot()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir != null && !File.Exists(Path.Combine(dir.FullName, "usurper-reloaded.csproj"))) dir = dir.Parent;
        return dir!.FullName;
    }

    private static string Src(params string[] path) =>
        File.ReadAllText(Path.Combine(new[] { RepoRoot(), "Scripts" }.Concat(path).ToArray()));

    private static Dictionary<string, string> LocFile(string lang) =>
        JsonSerializer.Deserialize<Dictionary<string, string>>(File.ReadAllText(Path.Combine(RepoRoot(), "Localization", lang + ".json")))!;

    private static Character Hero(int level = 20) => new Character
    {
        Name1 = "world_tester", Name2 = "World Tester", AI = CharacterAI.Human,
        Class = CharacterClass.Warrior, Race = CharacterRace.Human, Level = level,
        HP = 5000, MaxHP = 5000, BaseMaxHP = 5000, Strength = 100, BaseStrength = 100,
        Defence = 50, BaseDefence = 50, Agility = 50, BaseAgility = 50, Dexterity = 50, BaseDexterity = 50,
        Mental = 100, Gold = 0, Chivalry = 0, Darkness = 0,
    };

    private static List<Quest> QuestDb() => (List<Quest>)typeof(QuestSystem).GetField("questDatabase", S)!.GetValue(null)!;

    // ================= 1. Crown bounties =================

    /// <summary>The base tier SetDefaultRewards gives a difficulty: 1, 2, 3, 3.</summary>
    private static int BaseTier(int difficulty) => difficulty switch { 1 => 1, 2 => 2, _ => 3 };

    private static long TierGold(int tier, int level) => tier switch { 1 => level * 150L, 2 => level * 500L, _ => level * 1000L };
    private static long TierXp(int tier, int level) => tier switch { 1 => level * 100L, 2 => level * 500L, _ => level * 1000L };

    /// <summary>One Crown monster bounty of each difficulty, made by the game's own CreateGenericBounty.</summary>
    private static Dictionary<int, Quest> CrownBountiesByDifficulty()
    {
        var create = typeof(QuestSystem).GetMethod("CreateGenericBounty", S)!;
        var found = new Dictionary<int, Quest>();
        for (int i = 0; i < 400 && found.Count < 4; i++)
        {
            var q = (Quest)create.Invoke(null, new object[] { "Test Crown" })!;
            found.TryAdd(q.Difficulty, q);
        }
        found.Keys.Should().BeEquivalentTo(new[] { 1, 2, 3, 4 }, "the generic bounty rolls difficulty 1 to 4");
        return found;
    }

    private static long PaidBy(Quest quest, Character hero)
    {
        var apply = typeof(QuestSystem).GetMethod("ApplyQuestReward", S)!;
        var s = NewScreen();
        long gold = hero.Gold, xp = hero.Experience;
        apply.Invoke(null, new object[] { hero, quest, quest.CalculateReward(hero.Level), s.Term });
        return quest.RewardType == QuestRewardType.Money ? hero.Gold - gold : hero.Experience - xp;
    }

    [Theory]
    [InlineData(QuestRewardType.Money)]
    [InlineData(QuestRewardType.Experience)]
    public void CrownBounty_PaysTwiceItsBaseTier_AndNeverLessForAHarderOne(QuestRewardType type)
    {
        var bounties = CrownBountiesByDifficulty();
        long previous = 0;
        using var scope = new AssertionScope();
        foreach (var d in new[] { 1, 2, 3, 4 })
        {
            var q = bounties[d];
            q.Initiator.Should().Be("The Crown");
            q.QuestTarget.Should().Be(QuestTarget.Monster);
            q.RewardType = type;
            var hero = Hero(20);
            long expected = 2 * (type == QuestRewardType.Money ? TierGold(BaseTier(d), 20) : TierXp(BaseTier(d), 20));
            q.CalculateReward(20).Should().Be(expected, $"a difficulty {d} Crown bounty pays twice its base tier");
            long paid = PaidBy(q, hero);
            paid.Should().Be(expected, $"the difficulty {d} bounty is paid what it shows");
            paid.Should().BeGreaterOrEqualTo(previous, $"difficulty {d} pays no less than the easier ones");
            previous = paid;
        }
    }

    [Theory]
    [InlineData(4, 2)]
    [InlineData(6, 3)]
    [InlineData(2, 1)]
    public void ASavedDoubledTier_IsHalvedBackToItsBase_AndPaidDouble(int saved, int baseTier)
    {
        var q = new Quest { Initiator = "The Crown", QuestTarget = QuestTarget.Monster, Reward = (byte)saved, RewardType = QuestRewardType.Money };
        q.CalculateReward(30).Should().Be(2 * TierGold(baseTier, 30));
        PaidBy(q, Hero(30)).Should().Be(2 * TierGold(baseTier, 30));
        q.GetRewardDescription().Should().NotContain(Loc.Get("quest.reward.unknown"), "the board names the tier of a saved Crown bounty");
        var xp = new Quest { Initiator = "The Crown", QuestTarget = QuestTarget.Monster, Reward = (byte)saved, RewardType = QuestRewardType.Experience };
        xp.CalculateReward(30).Should().Be(2 * TierXp(baseTier, 30));
    }

    [Fact]
    public void OtherQuests_KeepTheirPay()
    {
        new Quest { Initiator = QuestSystem.BountyBoardInitiator, QuestTarget = QuestTarget.Monster, Reward = 2, RewardType = QuestRewardType.Money }
            .CalculateReward(10).Should().Be(5000, "a board quest of tier 2 pays the tier");
        new Quest { Initiator = "The Crown", QuestTarget = QuestTarget.DefeatNPC, Reward = 60, RewardType = QuestRewardType.Money }
            .CalculateReward(10).Should().Be(0, "a Crown bounty on an NPC keeps its own pay (BountyReward)");
        QuestSystem.BountyReward(new Quest { Initiator = "The Crown", QuestTarget = QuestTarget.DefeatNPC, Reward = 60 })
            .Should().Be(6000);
        new Quest { Initiator = "Queen Test", QuestTarget = QuestTarget.Monster, Reward = 3, RewardType = QuestRewardType.Money }
            .CalculateReward(10).Should().Be(10000, "a royal quest pays its tier");
        new Quest { Initiator = "The Crown", QuestTarget = QuestTarget.Monster, Reward = 4, RewardType = QuestRewardType.Chivalry }
            .CalculateReward(10).Should().Be(75, "only gold and XP are doubled");
    }

    // ================= 2. the royal quest offer =================

    private static King TestKing()
    {
        var king = King.CreateNewKing("Queen Test", CharacterAI.Computer, CharacterSex.Female);
        king.Treasury = 1_000_000;
        return king;
    }

    [Theory]
    [InlineData("en", 20)]
    [InlineData("en", 100)]
    [InlineData("hu", 100)]
    public async Task RoyalOffer_ShowsTheAdvanceAndTheCompletionPay(string lang, int level)
    {
        CastleLocation.SetKing(TestKing());
        GameConfig.Language = lang;
        var hero = Hero(level);
        hero.Name2 = $"Royal Offer {lang} {level}";
        hero.Chivalry = 500;
        hero.Fame = 500;
        long gold = hero.Gold, xp = hero.Experience;
        int before = QuestDb().Count;
        var castle = new CastleLocation();
        var s = At(castle, hero, "Y");
        typeof(CastleLocation).GetField("playerIsKing", F)!.SetValue(castle, false);
        Quest? quest = null;
        try
        {
            await Run(castle, "AudienceRequestQuest");
            quest = QuestDb().Skip(before).FirstOrDefault(q => q.Occupier == hero.Name2);
            quest.Should().NotBeNull("the offer was accepted");
            string text = s.Text;
            string prompt = Loc.Get("castle.quest_accept_yn").Trim();
            int at = text.IndexOf(prompt, StringComparison.Ordinal);
            at.Should().BeGreaterThan(0);
            string offer = text.Substring(0, at);

            long advanceGold = hero.Gold - gold, advanceXp = hero.Experience - xp;
            long completion = quest!.CalculateReward(hero.Level);
            using var scope = new AssertionScope();
            advanceGold.Should().BeGreaterThan(0);
            offer.Should().Contain(advanceGold.ToString("N0"), "the offer shows the advance gold paid on accepting");
            offer.Should().Contain(advanceXp.ToString("N0"), "the offer shows the advance XP paid on accepting");
            offer.Should().Contain(completion.ToString("N0"), "the offer shows what completion pays");
            foreach (var row in Rows(offer))
                row.Length.Should().BeLessOrEqualTo(79, $"\"{row}\"");
        }
        finally
        {
            if (quest != null) { QuestDb().Remove(quest); hero.ActiveQuests.Remove(quest); }
        }
    }

    // ================= 3. the jailbreak =================

    private const string OldWarning = "You could end up in prison yourself if caught.";

    private static Character Breaker()
    {
        var hero = Hero(80);   // 30 + level + Agility / 5 is above 100: the attempt always succeeds
        hero.Name2 = "Jail Breaker";
        return hero;
    }

    private static async Task<string> Jailbreak(Character hero)
    {
        var road = new AnchorRoadLocation();
        var s = At(road, hero, "Y");
        await Run(road, "AttemptJailbreak");
        return s.Text;
    }

    private static async Task NextDay(Character hero)
    {
        var engine = GameEngine.Instance;
        var old = engine.CurrentPlayer;
        engine.CurrentPlayer = hero;
        try { await Run(DailySystemManager.Instance, "RunBasicDailyReset"); }
        finally { engine.CurrentPlayer = old; }
    }

    private static PlayerData Serialize(Character player) =>
        (PlayerData)typeof(SaveSystem).GetMethod("SerializePlayer", F)!.Invoke(SaveSystem.Instance, new object[] { player })!;

    private static Character Restore(PlayerData data)
    {
        try { return (Character)typeof(GameEngine).GetMethod("RestorePlayerFromSaveData", F)!.Invoke(GameEngine.Instance, new object[] { data })!; }
        catch (TargetInvocationException ex) when (ex.InnerException != null) { throw ex.InnerException; }
    }

    [Fact]
    public async Task Jailbreak_IsOnceADay_AndTheNextDayItWorks()
    {
        GameConfig.Language = "en";
        var hero = Breaker();
        string first = await Jailbreak(hero);
        hero.Chivalry.Should().Be(50, "the first jailbreak of the day succeeds");
        first.Should().NotContain(OldWarning, "the prison warning is cut: a jailbreak never leads to prison");

        string second = await Jailbreak(hero);
        using (new AssertionScope())
        {
            hero.Chivalry.Should().Be(50, "a second jailbreak the same day is refused and gives no Chivalry");
            second.Should().NotContain(Loc.Get("anchor_road.proceed_jailbreak").Trim(), "the attempt is not offered again today");
            second.Should().Contain(Loc.Get("anchor_road.jailbreak_done_today"));
        }

        await NextDay(hero);
        await Jailbreak(hero);
        hero.Chivalry.Should().BeGreaterThan(50, "the next day the jailbreak works again");
    }

    [Fact]
    public async Task TheDaysJailbreak_SurvivesASaveAndLoad()
    {
        GameConfig.Language = "en";
        var hero = Breaker();
        await Jailbreak(hero);
        hero.Chivalry.Should().Be(50);

        var restored = Restore(JsonSerializer.Deserialize<PlayerData>(JsonSerializer.Serialize(Serialize(hero)))!);
        long chivalry = restored.Chivalry;
        await Jailbreak(restored);
        restored.Chivalry.Should().Be(chivalry, "the day's attempt is saved, so a reload does not reopen it");
    }

    [Fact]
    public async Task ASaveFromBeforeTheField_LoadsWithTheJailbreakOpen_OnceThatDay()
    {
        GameConfig.Language = "en";
        var hero = Breaker();
        var json = JsonNode.Parse(JsonSerializer.Serialize(Serialize(hero)))!.AsObject();
        var keys = json.Select(p => p.Key).Where(k => k.Contains("Jailbreak")).ToList();
        keys.Should().NotBeEmpty("the save holds the day's jailbreak");
        foreach (var k in keys) json.Remove(k);   // a save written before the field existed

        var old = Restore(JsonSerializer.Deserialize<PlayerData>(json.ToJsonString())!);
        long chivalry = old.Chivalry;
        await Jailbreak(old);
        old.Chivalry.Should().Be(chivalry + 50, "an old save may break someone out that day");
        await Jailbreak(old);
        old.Chivalry.Should().Be(chivalry + 50, "and only once");
    }

    [Fact]
    public void TheJailbreakTexts_WarningCut_RefusalTranslatedAndFits()
    {
        string en = Loc.GetIn("en", "anchor_road.jailbreak_done_today");
        using var scope = new AssertionScope();
        foreach (var lang in Langs)
        {
            LocFile(lang).Should().NotContainKey("anchor_road.end_up_in_prison", $"{lang}: the prison warning is cut");
            string text = Loc.GetIn(lang, "anchor_road.jailbreak_done_today");
            text.Should().NotBe("anchor_road.jailbreak_done_today", lang);
            if (lang != "en") text.Should().NotBe(en, $"{lang} has its own text");
            text.Should().NotContainAny(new[] { "\u2013", "\u2014" });
        }
        foreach (var lang in new[] { "en", "hu" })
            Loc.GetIn(lang, "anchor_road.jailbreak_done_today").Length.Should().BeLessOrEqualTo(79, lang);
        Src("Locations", "AnchorRoadLocation.cs").Should().NotContain("anchor_road.end_up_in_prison");
    }

    // ================= 4. the Church blessing =================

    private static readonly Dictionary<string, string> DayWord = new()
    {
        ["en"] = "day", ["es"] = "d\u00eda", ["fr"] = "jour", ["it"] = "giorn", ["hu"] = "nap",
    };

    private static readonly string[] BlessingKeys = { "church.blessing_days", "church.bishop_blessing_days", "renown.free_blessing_granted" };

    [Fact]
    public void TheBlessingLines_NameNoDuration()
    {
        using var scope = new AssertionScope();
        foreach (var lang in Langs)
            foreach (var key in BlessingKeys)
            {
                string text = Loc.GetIn(lang, key);
                text.Should().NotBe(key);
                text.Any(char.IsDigit).Should().BeFalse($"{lang} {key} names no number of days: \"{text}\"");
                text.ToLowerInvariant().Should().NotContain(DayWord[lang], $"{lang} {key} names no duration");
                text.Should().NotContainAny(new[] { "\u2013", "\u2014" });
            }
        foreach (var lang in new[] { "en", "hu" })
            foreach (var key in BlessingKeys)
                Loc.GetIn(lang, key).Length.Should().BeLessOrEqualTo(79, $"{lang} {key}");
    }

    [Fact]
    public async Task APurchasedBlessing_AppliesWhatItsLineNames()
    {
        GameConfig.Language = "en";
        Loc.GetIn("en", "church.blessing_days").Should().ContainAll("Chivalry", "Darkness");
        var hero = Hero();
        hero.Gold = 10_000;
        hero.Darkness = 100;
        hero.Chivalry = 0;
        var church = new ChurchLocation();
        var s = At(church, hero, "1000", "Y");
        await Run(church, "ProcessBlessingPurchase");
        using var scope = new AssertionScope();
        s.Text.Should().Contain(Loc.Get("church.blessing_days"));
        hero.Gold.Should().Be(9_000);
        hero.Chivalry.Should().BeGreaterThan(0, "the blessing raises Chivalry");
        hero.Darkness.Should().BeLessThan(100, "the blessing lowers Darkness");
    }

    [Fact]
    public async Task TheBishopsBlessing_AppliesWhatItsLineNames()
    {
        GameConfig.Language = "en";
        Loc.GetIn("en", "church.bishop_blessing_days").Should().Contain("Chivalry");
        var hero = Hero();
        hero.Chivalry = 100;
        hero.Darkness = 0;
        for (int i = 0; i < 200; i++)
        {
            long before = hero.Chivalry;
            var church = new ChurchLocation();
            var s = At(church, hero);
            await Run(church, "SpeakWithBishop");
            if (!s.Text.Contains(Loc.Get("church.bishop_blessing_days"))) continue;
            hero.Chivalry.Should().BeGreaterThan(before, "the Bishop's blessing raises Chivalry");
            return;
        }
        throw new Xunit.Sdk.XunitException("the Bishop's blessing (25 percent) did not come in 200 audiences");
    }

    // ================= 5. the guard caps =================

    private static King GuardedKing(int guards)
    {
        var king = TestKing();
        for (int i = 0; i < guards; i++) king.Guards.Add(new RoyalGuard { Name = $"Guard {i}", AI = CharacterAI.Computer });
        return king;
    }

    private static IEnumerable<int> Numbers(string row) => Regex.Matches(row, @"\d+").Select(m => int.Parse(m.Value));

    private static string RowWith(string text, string prefix)
    {
        var row = Rows(text).FirstOrDefault(r => r.Contains(prefix.Trim()));
        row.Should().NotBeNull($"a row with \"{prefix.Trim()}\"");
        return row!;
    }

    private static string Prefix(string lang, string key) => Loc.GetIn(lang, key).Split("{0}")[0];

    [Theory]
    [InlineData("en")]
    [InlineData("hu")]
    public async Task ListOnlyScreens_PrintBothCaps(string lang)
    {
        GameConfig.Language = lang;
        CastleLocation.SetKing(GuardedKing(7));
        var hero = Hero(100);

        var castle = new CastleLocation();
        var s = At(castle, hero);
        typeof(CastleLocation).GetField("playerIsKing", F)!.SetValue(castle, true);
        await Run(castle, "DisplayRoyalCastleInterior");
        string interior = RowWith(s.Text, Loc.GetIn(lang, "castle.guards_label"));

        GameConfig.CompactMode = true;
        castle = new CastleLocation();
        s = At(castle, hero);
        typeof(CastleLocation).GetField("playerIsKing", F)!.SetValue(castle, true);
        await Run(castle, "DisplayRoyalCastleInteriorBBS");
        string bbsText = s.Text;
        string bbs = RowWith(bbsText, Prefix(lang, "castle.bbs_guards"));
        GameConfig.CompactMode = false;

        castle = new CastleLocation();
        s = At(castle, hero);
        typeof(CastleLocation).GetField("playerIsKing", F)!.SetValue(castle, false);
        await Run(castle, "ViewRoyalGuard");
        string roster = RowWith(s.Text, Prefix(lang, "castle.total_guards"));

        using var scope = new AssertionScope();
        foreach (var (name, row) in new[] { ("throne room", interior), ("BBS throne room", bbs), ("Royal Guard roster", roster) })
        {
            Numbers(row).Should().Contain(new[] { 7, King.MaxNPCGuards, GameConfig.MaxRoyalGuards },
                $"the {name} shows the count, the hiring cap and the joining cap: \"{row}\"");
            row.Length.Should().BeLessOrEqualTo(79, $"\"{row}\"");
        }
        foreach (var row in Rows(bbsText))   // a framed box row (WriteBoxHeader) is 80 wide, every other row fits 79
            row.Length.Should().BeLessOrEqualTo(row.Length > 0 && "╔║╚".IndexOf(row[0]) >= 0 ? 80 : 79, $"\"{row}\"");
    }

    [Fact]
    public async Task ActionScreens_PrintTheCapThatStopsTheirAction()
    {
        GameConfig.Language = "en";
        CastleLocation.SetKing(GuardedKing(3));
        var hero = Hero(100);

        var castle = new CastleLocation();
        var s = At(castle, hero, "");
        typeof(CastleLocation).GetField("playerIsKing", F)!.SetValue(castle, true);
        await Run(castle, "CheckSecurity");
        string security = RowWith(s.Text, Prefix("en", "castle.npc_guards"));

        castle = new CastleLocation();
        s = At(castle, hero, "N");
        typeof(CastleLocation).GetField("playerIsKing", F)!.SetValue(castle, false);
        await Run(castle, "ApplyForRoyalGuard");
        string apply = RowWith(s.Text, Prefix("en", "castle.guard_current_count"));

        using var scope = new AssertionScope();
        Numbers(security).Should().Equal(3, King.MaxNPCGuards);
        Numbers(apply).Should().Equal(3, GameConfig.MaxRoyalGuards);
    }

    // ================= 6. the monster market =================

    [Theory]
    [InlineData("Wyvern", 20)]
    [InlineData("Elder Drake", 40)]
    public async Task MonsterMarket_ShowsWhatAMonsterGuardCosts_AndIs(string monster, int level)
    {
        GameConfig.Language = "en";
        CastleLocation.SetKing(TestKing());
        var castle = new CastleLocation();
        var s = At(castle, Hero(100), "");
        typeof(CastleLocation).GetField("playerIsKing", F)!.SetValue(castle, true);
        await Run(castle, "HireMonsterGuard");

        var entry = MonsterGuardTypes.AvailableMonsters.Single(m => m.Name == monster);
        entry.Level.Should().Be(level);
        var court = new RoyalCourtSaveData { Treasury = 100_000_000 };
        King.AddMonsterGuard(court, entry.Name, entry.Level, entry.Cost).Should().BeTrue();
        var bought = court.MonsterGuards.Single();

        string shownName = MonsterNames.Display(monster);
        string row = Rows(s.Text).Single(r => Regex.IsMatch(r, @"^\d+\s+" + Regex.Escape(shownName) + @"\s"));
        var cells = Regex.Split(row.Substring(row.IndexOf(shownName, StringComparison.Ordinal) + shownName.Length).Trim(), @"\s+");
        using var scope = new AssertionScope();
        cells.Should().HaveCount(6, $"level, HP, Strength, Defence, price and feeding: \"{row}\"");
        cells[0].Should().Be(level.ToString());
        cells[1].Should().Be(bought.MaxHP.ToString(), "HP");
        cells[2].Should().Be(bought.Strength.ToString(), "Strength");
        cells[3].Should().Be(bought.Defence.ToString(), "Defence");
        cells[4].Should().Be(bought.PurchaseCost.ToString("N0"), "price");
        cells[5].Should().Be(bought.DailyFeedingCost.ToString("N0"), "feeding a day");
        row.Length.Should().BeLessOrEqualTo(79);
    }

    // ================= 7. the druid shrine texts =================

    private static string Squeeze(string s) => s.Replace(" ", "").Replace("\u00a0", "");

    [Fact]
    public void ShrineTexts_ClaimOnlyTheEffectsTheCodeApplies()
    {
        var texts = Langs.Select(l => (l, t: Loc.GetIn(l, "shrine.terravok.passive"), m: Loc.GetIn(l, "shrine.maelketh.passive"), n: Loc.GetIn(l, "shrine.noctura.passive")))
            .Append(("fallback",
                DruidShrineData.Shrines.Single(x => x.Id == "terravok").PassiveSummary,
                DruidShrineData.Shrines.Single(x => x.Id == "maelketh").PassiveSummary,
                DruidShrineData.Shrines.Single(x => x.Id == "noctura").PassiveSummary));
        using var scope = new AssertionScope();
        foreach (var (lang, t, m, n) in texts)
        {
            Squeeze(t).Should().NotContain("10%", $"{lang}: Terravok gives no Max HP bonus");
            Squeeze(t).Should().Contain("+5", $"{lang}: Terravok keeps its HP a round");
            Squeeze(m).Should().NotContain("5%", $"{lang}: Maelketh gives no rage bonus");
            Squeeze(m).Should().Contain("8%", $"{lang}: Maelketh keeps its melee damage");
            Squeeze(n).Should().NotContain("10%", $"{lang}: Noctura gives no backstab bonus");
            Squeeze(n).Should().Contain("5%", $"{lang}: Noctura keeps its crit chance");
            foreach (var x in new[] { t, m, n }) x.Should().NotContainAny(new[] { "\u2013", "\u2014" });
        }
        GameConfig.ShrineTerravokHpRegenPerRound.Should().Be(5.0f);
        GameConfig.ShrineMaelkethMeleeBonus.Should().Be(0.08f);
        GameConfig.ShrineNocturaCritChanceBonus.Should().Be(5.0f);
        string combat = Src("Systems", "CombatEngine.cs"), stats = Src("Systems", "StatEffectsSystem.cs");
        combat.Should().Contain("IsAttunedTo(\"terravok\")").And.Contain("GameConfig.ShrineTerravokHpRegenPerRound");
        combat.Should().Contain("IsAttunedTo(\"maelketh\")").And.Contain("GameConfig.ShrineMaelkethMeleeBonus");
        stats.Should().Contain("IsAttunedTo(\"noctura\")").And.Contain("GameConfig.ShrineNocturaCritChanceBonus");
    }
}
