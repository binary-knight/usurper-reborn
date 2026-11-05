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
using UsurperRemake.Locations;
using UsurperRemake.Systems;
using UsurperReborn.Tests.Localization;
using Xunit;

namespace UsurperReborn.Tests;

/// <summary>
/// 1.3.11 small game rows. B7: the Ancient Scroll result waits for Enter. G1: the murder revenge grudge asks again on
/// a key it does not offer. K6: the arrest text says imprisoned only when someone was. K7: the street Smoke Bomb text
/// states what it gives. K8: the outpost torch and lockpick say what they do. K9: the dungeon merchant's healing
/// potion follows the player's level. K10, K14: two unused keys are gone. K12: BanditLordRises is gone and old state
/// naming it still loads. K15: the unread stat effects are gone and old state with them still loads. K16: a dying
/// wish message for someone permanently dead is dropped at the daily reset. K17: a recipient holding a family grudge
/// still receives the message. Members added in 1.3.11 are reached by reflection so the class also runs on the
/// release before it (the repro). All in memory.
/// </summary>
[Collection("SharedGameSingletons")]
public sealed class GameSmall1311Tests : IDisposable
{
    private const BindingFlags F = BindingFlags.NonPublic | BindingFlags.Instance;
    private const BindingFlags PS = BindingFlags.Public | BindingFlags.Static;
    private static readonly string[] Langs = { "en", "es", "fr", "hu", "it" };

    private readonly King? _kingBefore = CastleLocation.GetCurrentKing();
    private readonly List<NPC> _rosterBefore = NPCSpawnSystem.Instance.ActiveNPCs.ToList();
    private readonly bool _pacing = Pacing.Disabled;
    private readonly List<string> _noticesBefore = GameEngine.PendingNotifications.ToList();
    private readonly object? _streetRandomBefore = typeof(StreetEncounterSystem).GetField("_random", F)!.GetValue(StreetEncounterSystem.Instance);

    public GameSmall1311Tests()
    {
        Pacing.Disabled = true;
        NPCSpawnSystem.Instance.ActiveNPCs.Clear();
        GameEngine.PendingNotifications.Clear();
    }

    public void Dispose()
    {
        CastleLocation.SetKing(_kingBefore!);
        var active = NPCSpawnSystem.Instance.ActiveNPCs;
        active.Clear();
        active.AddRange(_rosterBefore);
        Pacing.Disabled = _pacing;
        GameEngine.PendingNotifications.Clear();
        foreach (var n in _noticesBefore) GameEngine.PendingNotifications.Enqueue(n);
        typeof(StreetEncounterSystem).GetField("_random", F)!.SetValue(StreetEncounterSystem.Instance, _streetRandomBefore);
    }

    // ---------- helpers ----------

    /// <summary>Next gives the set value, within the bound asked for; NextDouble gives 0.</summary>
    private sealed class FixedRandom : Random
    {
        private readonly int _value;
        public FixedRandom(int value) { _value = value; }
        public override int Next() => _value;
        public override int Next(int maxValue) => Math.Min(_value, Math.Max(0, maxValue - 1));
        public override int Next(int minValue, int maxValue) => Math.Clamp(_value, minValue, Math.Max(minValue, maxValue - 1));
        public override double NextDouble() => 0;
    }

    private static string RawText(TerminalEmulator term, MemoryStream output)
    {
        term.StreamWriterInternal?.Flush();
        return Encoding.UTF8.GetString(output.ToArray());
    }

    private static string Plain(string raw) => Regex.Replace(raw, "\u001b\\[[0-9;?]*[A-Za-z]", "").Replace("\r", "");

    private static string Plain(TerminalEmulator term, MemoryStream output) => Plain(RawText(term, output));

    private static async Task Run(object target, Type owner, string method, params object?[] args)
    {
        try
        {
            var r = owner.GetMethod(method, F)!.Invoke(target, args);
            if (r is Task t) await t;
        }
        catch (TargetInvocationException ex) when (ex.InnerException != null) { throw ex.InnerException; }
    }

    private static Character Hero(int level = 8) => new Character
    {
        Name1 = "gs", Name2 = "Gs Hero", Class = CharacterClass.Warrior, Race = CharacterRace.Human, Level = level,
        HP = 300, MaxHP = 300, BaseMaxHP = 300, Gold = 1_000_000, Mental = 100, AI = CharacterAI.Human, Dexterity = 60, Wisdom = 60,
        Healing = 2, Chivalry = 500,
    };

    private static NPC Npc(string id, string name, int level = 8)
    {
        var npc = new NPC
        {
            ID = id, Name1 = name, Name2 = name, Class = CharacterClass.Warrior, Race = CharacterRace.Human,
            Level = level, HP = 500, MaxHP = 500, BaseMaxHP = 500, Strength = 300, BaseStrength = 300,
            Gold = 5000, CurrentLocation = "Main Street", Sex = CharacterSex.Female, BirthDate = DateTime.Now,
        };
        var profile = PersonalityProfile.GenerateForArchetype("commoner");
        npc.Personality = profile;
        npc.Brain = new NPCBrain(npc, profile);
        npc.Memory = npc.Brain.Memory;
        return npc;
    }

    private static (DungeonLocation d, TerminalEmulator term, MemoryStream output, Character hero) Dungeon(Random random, int level, int floor, params string[] lines)
    {
        var output = new MemoryStream();
        var term = new TerminalEmulator(new LineStream(lines), output);
        var hero = Hero(level);
        var d = new DungeonLocation();
        typeof(BaseLocation).GetField("terminal", F)!.SetValue(d, term);
        typeof(BaseLocation).GetField("currentPlayer", F)!.SetValue(d, hero);
        typeof(DungeonLocation).GetField("currentDungeonLevel", F)!.SetValue(d, floor);
        typeof(DungeonLocation).GetField("dungeonRandom", F)!.SetValue(d, random);
        return (d, term, output, hero);
    }

    private static int Count(string text, string part) => Regex.Matches(text, Regex.Escape(part)).Count;

    private static string RepoFile(params string[] parts)
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir != null && !File.Exists(Path.Combine(dir.FullName, "usurper-reloaded.csproj"))) dir = dir.Parent;
        dir.Should().NotBeNull();
        return Path.Combine(new[] { dir!.FullName }.Concat(parts).ToArray());
    }

    // ---------- B7: the Ancient Scroll result waits for Enter ----------

    [Fact]
    public async Task B7_DecliningTheScroll_ShowsTheResult_ThenOneEnterPrompt()
    {
        var (d, term, output, _) = Dungeon(new FixedRandom(0), 8, 5, "N", "");
        await Run(d, typeof(DungeonLocation), "AncientScrollEncounter");
        string shown = Plain(term, output).TrimEnd();
        shown.Should().Contain(Loc.Get("dungeon.scroll_stored"));
        shown.Should().EndWith(Loc.Get("ui.press_enter").TrimEnd(), "the result stays until Enter");
        Count(shown, Loc.Get("ui.press_enter").TrimEnd()).Should().Be(1, "one prompt, not two");
    }

    [Theory]
    [InlineData(0, "dungeon.scroll_divine_light")]
    [InlineData(2, "dungeon.scroll_hidden_passage")]
    public async Task B7_RecitingTheScroll_ShowsItsEffect_ThenOneEnterPrompt(int scrollType, string key)
    {
        var (d, term, output, _) = Dungeon(new FixedRandom(scrollType), 8, 5, "Y", "");
        await Run(d, typeof(DungeonLocation), "AncientScrollEncounter");
        string shown = Plain(term, output).TrimEnd();
        shown.Should().Contain(Loc.Get(key));
        shown.Should().EndWith(Loc.Get("ui.press_enter").TrimEnd());
        Count(shown, Loc.Get("ui.press_enter").TrimEnd()).Should().Be(1);
    }

    // ---------- G1: the murder revenge grudge asks again on a key it does not offer ----------

    private static NPC MurderGrudge(Character hero)
    {
        var npc = Npc("gs_grudge", "Gs Grudge", hero.Level);
        npc.Memory!.RecordEvent(new MemoryEvent { Type = MemoryType.Murdered, InvolvedCharacter = hero.Name2, Description = "murdered", Importance = 1f });
        return npc;
    }

    private static async Task Grudge(NPC npc, Character hero, TerminalEmulator term, EncounterResult result)
    {
        typeof(StreetEncounterSystem).GetField("_random", F)!.SetValue(StreetEncounterSystem.Instance, new FixedRandom(0));
        var run = (Task)typeof(StreetEncounterSystem).GetMethod("ExecuteGrudgeConfrontation", F)!
            .Invoke(StreetEncounterSystem.Instance, new object[] { npc, hero, term, result })!;
        await run.WaitAsync(TimeSpan.FromSeconds(60));
    }

    [Fact]
    public async Task G1_AnInvalidKey_AsksAgain_AndRStillRuns()
    {
        var hero = Hero();
        var npc = MurderGrudge(hero);
        var output = new MemoryStream();
        var term = new TerminalEmulator(new LineStream(new[] { "Z", "R" }.Concat(Enumerable.Repeat("", 8))), output);
        var result = new EncounterResult();
        await Grudge(npc, hero, term, result);
        string shown = Plain(term, output);
        shown.Should().Contain(Loc.Get("ui.invalid_choice_choose", "F, R"), "Z is not an option");
        shown.Should().Contain(Loc.Get("street_encounter.grudge.barely_escape", npc.Name2), "the second answer is taken");
    }

    [Fact]
    public async Task G1_ThreeInvalidKeys_AreTheFight_AsTheOtherEncountersFallBack()
    {
        var hero = Hero();
        var npc = MurderGrudge(hero);
        var output = new MemoryStream();
        var term = new TerminalEmulator(new LineStream(new[] { "Z", "Y", "X" }), output);
        var result = new EncounterResult();
        try { await Grudge(npc, hero, term, result); }
        catch (Exception) { /* the fight reads past the end of the input */ }
        string shown = Plain(term, output);
        Count(shown, Loc.Get("ui.invalid_choice_choose", "F, R")).Should().Be(3);
        shown.Should().NotContain(Loc.Get("street_encounter.grudge.barely_escape", npc.Name2));
    }

    // ---------- K6: the arrest text ----------

    private static King PlotKing(params string[] conspirators)
    {
        var king = King.CreateNewKing("Gs Hero", CharacterAI.Human, CharacterSex.Male);
        king.Treasury = 1_000_000;
        king.CourtMembers.Clear();
        king.ActivePlots.Clear();
        foreach (var n in conspirators)
            king.CourtMembers.Add(new CourtMember { Name = n, LoyaltyToKing = 50 });
        king.ActivePlots.Add(new CourtIntrigue { PlotType = "Coup", Conspirators = conspirators.ToList(), Target = king.Name, Progress = 20, IsDiscovered = true });
        CastleLocation.SetKing(king);
        return king;
    }

    private static async Task<string> Arrest()
    {
        var castle = new CastleLocation();
        var output = new MemoryStream();
        var term = new TerminalEmulator(new LineStream(new[] { "1" }.Concat(Enumerable.Repeat("", 6))), output);
        typeof(BaseLocation).GetField("terminal", F)!.SetValue(castle, term);
        typeof(BaseLocation).GetField("currentPlayer", F)!.SetValue(castle, Hero());
        typeof(CastleLocation).GetField("playerIsKing", F)!.SetValue(castle, true);
        await Run(castle, typeof(CastleLocation), "CourtAction_Arrest");
        return Plain(term, output);
    }

    [Fact]
    public async Task K6_APlotOfCourtTitlesOnly_IsNotCalledImprisoned()
    {
        PlotKing("Lord Invented", "Lady Invented");
        string shown = await Arrest();
        string cost = GameConfig.ArrestTrialCost.ToString("N0");
        CastleLocation.GetCurrentKing().Prisoners.Should().BeEmpty("no real NPC to jail");
        CastleLocation.GetCurrentKing().ActivePlots.Should().BeEmpty();
        shown.Should().NotContain(Loc.Get("castle.arrested", cost));
        shown.Should().Contain(Loc.Get("castle.arrested_dismissed", cost));
    }

    [Theory]
    [InlineData(1)]
    [InlineData(2)]
    public async Task K6_APlotWithARealNpc_SaysImprisoned(int invented)
    {
        var real = Npc("gs_plotter", "Gs Plotter");
        NPCSpawnSystem.Instance.ActiveNPCs.Add(real);
        PlotKing(new[] { real.Name2 }.Concat(Enumerable.Range(0, invented - 1).Select(i => $"Lord Invented {i}")).ToArray());
        string shown = await Arrest();
        string cost = GameConfig.ArrestTrialCost.ToString("N0");
        CastleLocation.GetCurrentKing().Prisoners.Keys.Should().Contain(real.Name2);
        shown.Should().Contain(Loc.Get("castle.arrested", cost));
        shown.Should().NotContain(Loc.Get("castle.arrested_dismissed", cost));
    }

    [Fact]
    public void K6_TheNewText_FitsAndIsTranslated()
    {
        foreach (var lang in Langs)
        {
            Loc.HasIn(lang, "castle.arrested_dismissed").Should().BeTrue(lang);
            Loc.GetIn(lang, "castle.arrested_dismissed").Should().Contain("{0}", lang);
        }
        foreach (var lang in new[] { "en", "hu" })
            string.Format(Loc.GetIn(lang, "castle.arrested_dismissed"), "1,000,000").Length.Should().BeLessOrEqualTo(79, lang);
    }

    // ---------- K7: the street merchant's Smoke Bomb text ----------

    [Fact]
    public void K7_TheSmokeBombText_StatesItsEffect_InEveryLanguage()
    {
        const string key = "street_encounter.merchant.item.smoke_bomb_desc";
        Loc.GetIn("en", key).Should().Contain("Darkness +2").And.NotContain("Guaranteed");
        Loc.GetIn("es", key).Should().Contain("Oscuridad +2").And.NotContain("garantizada");
        Loc.GetIn("fr", key).Should().Contain("Ténèbres +2").And.NotContain("garantie");
        Loc.GetIn("hu", key).Should().Contain("Sötétség +2").And.NotContain("Garantált");
        Loc.GetIn("it", key).Should().Contain("Oscurità +2").And.NotContain("garantita");
        foreach (var lang in new[] { "en", "hu" })
            ("      " + Loc.GetIn(lang, key)).Length.Should().BeLessOrEqualTo(79, lang);
    }

    [Fact]
    public void K7_TheSmokeBomb_StillGivesDarknessTwo_AndNothingForFleeing()
    {
        var hero = Hero();
        hero.Darkness = 5;
        int bombs = hero.SmokeBombs;
        var itemType = typeof(StreetEncounterSystem).GetNestedType("MerchantItem", BindingFlags.NonPublic)!;
        object item = Activator.CreateInstance(itemType)!;
        itemType.GetField("Name")!.SetValue(item, "Smoke Bomb");
        typeof(StreetEncounterSystem).GetMethod("ApplyMerchantItem", F)!.Invoke(StreetEncounterSystem.Instance, new object[] { hero, item });
        hero.Darkness.Should().Be(7);
        hero.SmokeBombs.Should().Be(bombs);
    }

    // ---------- K8: the outpost torch and lockpick ----------

    [Theory]
    [InlineData("en", "HP", "Dexterity")]
    [InlineData("es", "PV", "Destreza")]
    [InlineData("fr", "PV", "Dextérité")]
    [InlineData("hu", "ÉP", "Ügyesség")]
    [InlineData("it", "PV", "Destrezza")]
    public void K8_TheNames_SayWhatEachDoes(string lang, string hp, string dex)
    {
        Loc.GetIn(lang, "dungeon.settlement_item_torch").Should().Contain($"+10 {hp}");
        Loc.GetIn(lang, "dungeon.settlement_item_lockpick").Should().Contain($"+2 {dex}");
    }

    [Fact]
    public void K8_TheEffects_AreWhatTheNamesSay()
    {
        var (d, _, _, hero) = Dungeon(new FixedRandom(0), 8, 5);
        hero.HP = 100;
        var apply = typeof(DungeonLocation).GetMethod("ApplySettlementPurchase", F)!;
        apply.Invoke(d, new object[] { hero, "torch" });
        hero.HP.Should().Be(110);
        apply.Invoke(d, new object[] { hero, "lockpick" });
        hero.TimedStatBuffs.Should().Contain(b => b.Source == "settlement_lockpick" && b.Stat == StatKind.Dexterity && b.Amount == 2);
    }

    [Theory]
    [InlineData("en")] [InlineData("hu")] [InlineData("it")]
    public void K8_ThePriceColumn_StaysLinedUp_AndTheRowsFit(string lang)
    {
        var (d, term, output, hero) = Dungeon(new FixedRandom(0), 8, 5, "0");
        string prev = GameConfig.Language;
        GameConfig.Language = lang;
        try
        {
            var settlement = Enumerable.Range(1, 100).Select(DungeonSettlementData.GetSettlement).First(s => s != null && s.TradeItems.Contains("Lockpick"))!;
            var r = typeof(DungeonLocation).GetMethod("SettlementTrade", F)!.Invoke(d, new object[] { hero, settlement });
            if (r is Task t) t.GetAwaiter().GetResult();
        }
        finally { GameConfig.Language = prev; }
        var rows = Plain(term, output).Split('\n').Where(r => Regex.IsMatch(r, @"^  \[\d+\] ") && r.TrimEnd().EndsWith("g")).ToList();
        rows.Should().HaveCountGreaterThan(1);
        rows.Select(r => Regex.Match(r, @" +[\d,.]+g\s*$").Index + Regex.Match(r, @" +[\d,.]+g\s*$").Length - Regex.Match(r, @"[\d,.]+g\s*$").Length)
            .Distinct().Count().Should().BeLessOrEqualTo(1, "the prices start in one column");
        rows.Should().OnlyContain(r => r.TrimEnd().Length <= 79);
    }

    // ---------- K9: the dungeon merchant's healing potion follows the player's level ----------

    [Theory]
    [InlineData(1, 1, 85)]
    [InlineData(50, 50, 1335)]
    [InlineData(100, 100, 5085)]
    [InlineData(50, 10, 1335)]
    [InlineData(100, 20, 5085)]
    [InlineData(50, 60, 1335)]
    public async Task K9_TheHealingPotion_IsTheTownPriceAtYourLevel_MinusFifteen(int level, int floor, int price)
    {
        var (d, term, output, hero) = Dungeon(new FixedRandom(0), level, floor, "1", "L");
        hero.Healing = 0;
        long gold = hero.Gold;
        await Run(d, typeof(DungeonLocation), "MerchantTradeMenu", hero);
        (gold - hero.Gold).Should().Be(price);
        hero.Healing.Should().Be(1);
        string shown = Plain(term, output);
        shown.Should().Contain(Loc.Get("dungeon.merchant_healing_potion", price));
    }

    [Fact]
    public async Task K9_TheMegaPotion_StaysByFloor()
    {
        var (d, term, output, hero) = Dungeon(new FixedRandom(0), 50, 10, "2", "L");
        long gold = hero.Gold;
        await Run(d, typeof(DungeonLocation), "MerchantTradeMenu", hero);
        (gold - hero.Gold).Should().Be(1000, "floor 10 x 100");
        Plain(term, output).Should().Contain(Loc.Get("dungeon.merchant_mega_potion", 1000));
    }

    [Fact]
    public void K9_TheRareItems_StayByFloor()
    {
        File.ReadAllText(RepoFile("Scripts", "Locations", "DungeonLocation.cs"))
            .Should().Contain("var rareItems = GenerateMerchantRareItems(currentDungeonLevel);");
    }

    // ---------- K10, K14: two unused keys ----------

    [Theory]
    [InlineData("settlement.market_coming_soon")]
    [InlineData("petition.dying.someone_special")]
    public void K10_K14_TheUnusedKeys_AreGone(string key)
    {
        foreach (var lang in Langs)
            Loc.HasIn(lang, key).Should().BeFalse(lang);
    }

    // ---------- K16: a dying wish message whose recipient has died permanently ----------

    private static DyingMessageSaveData Message(string recipient) => new() { Recipient = recipient, Elder = "Gs Elder", Gold = 500 };

    private static int Drop(Character hero)
    {
        var drop = typeof(NPCPetitionSystem).GetMethod("DropMessagesToTheDead", PS);
        drop.Should().NotBeNull();
        return (int)drop!.Invoke(null, new object[] { hero })!;
    }

    [Theory]
    [InlineData(true, false)]
    [InlineData(false, true)]
    public void K16_APermanentlyDeadRecipient_DropsTheMessage_WithNoReward_AndSaysSo(bool perma, bool aged)
    {
        var dead = Npc("gs_dead", "Gs Dead");
        dead.IsDead = true; dead.HP = 0;
        dead.IsPermaDead = perma; dead.IsAgedDeath = aged;
        NPCSpawnSystem.Instance.ActiveNPCs.Add(dead);
        var hero = Hero();
        long gold = hero.Gold, chivalry = hero.Chivalry;
        hero.DyingMessages.Add(Message(dead.Name2));
        Drop(hero).Should().Be(1);
        hero.DyingMessages.Should().BeEmpty();
        hero.Gold.Should().Be(gold, "no reward");
        hero.Chivalry.Should().Be(chivalry);
        GameEngine.PendingNotifications.Should().ContainSingle().Which.Should().Be(Loc.Get("petition.dying.undeliverable", dead.Name2, "Gs Elder"));
    }

    [Fact]
    public void K16_ARecipientPrunedFromTheRoster_DropsTheMessage()
    {
        NPCSpawnSystem.Instance.ActiveNPCs.Add(Npc("gs_other", "Gs Other"));
        var hero = Hero();
        hero.DyingMessages.Add(Message("Gs Long Gone"));
        Drop(hero).Should().Be(1);
        hero.DyingMessages.Should().BeEmpty();
    }

    [Fact]
    public void K16_AnEmptyRoster_DropsNothing()
    {
        var hero = Hero();
        hero.DyingMessages.Add(Message("Gs Anyone"));
        Drop(hero).Should().Be(0);
        hero.DyingMessages.Should().HaveCount(1);
        GameEngine.PendingNotifications.Should().BeEmpty();
    }

    [Fact]
    public void K16_ATemporaryDeath_OrALivingRecipient_KeepsTheMessage()
    {
        var down = Npc("gs_down", "Gs Down");
        down.IsDead = true; down.HP = 0;
        var alive = Npc("gs_alive", "Gs Alive");
        NPCSpawnSystem.Instance.ActiveNPCs.AddRange(new[] { down, alive });
        var hero = Hero();
        hero.DyingMessages.Add(Message(down.Name2));
        hero.DyingMessages.Add(Message(alive.Name2));
        Drop(hero).Should().Be(0);
        hero.DyingMessages.Should().HaveCount(2);
        GameEngine.PendingNotifications.Should().BeEmpty();
    }

    [Fact]
    public void K16_TheDailyReset_RunsTheDrop_AndTheLineFits()
    {
        string daily = File.ReadAllText(RepoFile("Scripts", "Systems", "DailySystemManager.cs"));
        int basic = daily.IndexOf("private async Task RunBasicDailyReset()", StringComparison.Ordinal);
        int next = daily.IndexOf("public void RunCatchUpDailyReset()", StringComparison.Ordinal);
        basic.Should().BeGreaterThan(0);
        daily.IndexOf("NPCPetitionSystem.DropMessagesToTheDead(player)", basic, StringComparison.Ordinal).Should().BeInRange(basic, next);
        foreach (var lang in new[] { "en", "hu" })
            string.Format(Loc.GetIn(lang, "petition.dying.undeliverable"), "Fcii Twenty Chars Nm", "Fcii Twenty Chars Nm").Length.Should().BeLessOrEqualTo(79, lang);
    }

    // ---------- K17: a recipient holding a family grudge ----------

    private static (HealerLocation place, TerminalEmulator term, MemoryStream output, Character hero, NPC npc) GrudgeTalk(params string[] lines)
    {
        var hero = Hero();
        var npc = Npc("gs_son", "Gs Son");
        npc.Memory!.RecordEvent(new MemoryEvent { Type = MemoryType.KilledMyParent, InvolvedCharacter = hero.Name2, Description = "killed", Importance = 1f });
        FamilySystem.HasGrudgeAgainst(npc, hero).Should().BeTrue();
        var place = new HealerLocation();
        var output = new MemoryStream();
        var term = new TerminalEmulator(new LineStream(lines), output);
        typeof(BaseLocation).GetField("terminal", F)!.SetValue(place, term);
        typeof(BaseLocation).GetField("currentPlayer", F)!.SetValue(place, hero);
        return (place, term, output, hero, npc);
    }

    private static async Task Talk(HealerLocation place, NPC npc)
    {
        var talk = typeof(BaseLocation).GetMethod("InteractWithNPC", F)!;
        await ((Task)talk.Invoke(place, new object[] { npc })!).WaitAsync(TimeSpan.FromSeconds(60));
    }

    [Fact]
    public async Task K17_TheGrudgeHolder_ReceivesTheMessage_AndTheRefusalStands_BothOnOneScreen_UntilEnter()
    {
        var (place, term, output, hero, npc) = GrudgeTalk("");
        long gold = hero.Gold;
        hero.DyingMessages.Add(Message(npc.Name2));
        await Talk(place, npc);

        hero.DyingMessages.Should().BeEmpty();
        hero.Gold.Should().Be(gold + 500);
        npc.Memory!.HasMemoryOfEvent(MemoryType.Helped, hero.Name2, hoursAgo: 24).Should().BeTrue();

        string raw = RawText(term, output);
        int clear = raw.LastIndexOf("\u001b[2J", StringComparison.Ordinal);
        clear.Should().BeGreaterOrEqualTo(0);
        string lastScreen = Plain(raw.Substring(clear));
        lastScreen.Should().Contain(Loc.Get("petition.dying.deliver", npc.Name2, "Gs Elder"), "no clear follows the delivery");
        lastScreen.Should().Contain(Loc.Get("family.grudge_refuse_talk", npc.Name2));
        lastScreen.TrimEnd().Should().EndWith(Loc.Get("ui.press_enter").TrimEnd(), "both stay until Enter");
        lastScreen.Should().NotContain(Loc.Get("base.npc_says", npc.Name2), "the refusal stands: no talk follows");
    }

    [Fact]
    public async Task K17_WithNoMessage_TheRefusal_IsAsBefore()
    {
        var (place, term, output, hero, npc) = GrudgeTalk();
        long gold = hero.Gold;
        await Talk(place, npc);
        hero.Gold.Should().Be(gold);
        string shown = Plain(term, output);
        shown.Should().Contain(Loc.Get("family.grudge_refuse_talk", npc.Name2));
        shown.Should().NotContain(Loc.Get("ui.press_enter").TrimEnd());
    }

    [Fact]
    public async Task K17_AMessageForSomeoneElse_StaysWithThePlayer()
    {
        var (place, _, _, hero, npc) = GrudgeTalk();
        hero.DyingMessages.Add(Message("Gs Someone Else"));
        long gold = hero.Gold;
        await Talk(place, npc);
        hero.DyingMessages.Should().HaveCount(1);
        hero.Gold.Should().Be(gold);
    }
}
