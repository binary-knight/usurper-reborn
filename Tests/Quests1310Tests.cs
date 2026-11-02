using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Runtime.CompilerServices;
using System.Threading.Tasks;
using FluentAssertions;
using UsurperRemake;
using UsurperRemake.BBS;
using UsurperRemake.Server;
using UsurperRemake.Systems;
using Xunit;

namespace UsurperReborn.Tests;

/// <summary>
/// 1.3.10 audit fixes, quests and contracts (piece FB). Each region names its id. All in memory; the MUD is
/// stood in for by the online flag and a MudServer instance made without its constructor.
/// </summary>
[Collection("SharedGameSingletons")]
public class Quests1310Tests : IDisposable
{
    private const BindingFlags SNP = BindingFlags.NonPublic | BindingFlags.Static;
    private const BindingFlags INP = BindingFlags.NonPublic | BindingFlags.Instance;
    private static readonly FieldInfo OnlineField = typeof(DoorMode).GetField("_onlineMode", SNP)!;
    private static readonly FieldInfo MudField = typeof(MudServer).GetField("_instance", SNP)!;

    private readonly bool _pacingWas;
    private readonly List<Quest> _made = new();

    public Quests1310Tests()
    {
        _pacingWas = Pacing.Disabled;
        Pacing.Disabled = true;
    }

    public void Dispose()
    {
        Pacing.Disabled = _pacingWas;
        foreach (var q in _made) { q.Deleted = true; q.Occupier = ""; q.OfferedTo = ""; }
    }

    /// <summary>The MUD for the length of a using block: online, with a MudServer instance.</summary>
    private sealed class InMud : IDisposable
    {
        private readonly object? _onlineWas = OnlineField.GetValue(null);
        private readonly object? _mudWas = MudField.GetValue(null);
        public InMud()
        {
            OnlineField.SetValue(null, true);
            MudField.SetValue(null, RuntimeHelpers.GetUninitializedObject(typeof(MudServer)));
        }
        public void Dispose()
        {
            OnlineField.SetValue(null, _onlineWas);
            MudField.SetValue(null, _mudWas);
        }
    }

    private static Character Hero(string name, int level = 20) => new Character
    {
        Name1 = name, Name2 = name, AI = CharacterAI.Human, Level = level,
        Class = CharacterClass.Warrior, Race = CharacterRace.Human,
        HP = 300, MaxHP = 300, BaseMaxHP = 300, Mental = 80,
    };

    private Quest Held(string occupier, int days = 0, int limit = 14)
    {
        var q = new Quest { Title = "Fb held", Initiator = "A Patron", Occupier = occupier, OccupiedDays = days, DaysToComplete = limit, Date = DateTime.Now };
        q.Objectives.Add(new QuestObjective(QuestObjectiveType.KillMonsters, "Fb", 5, "", ""));
        QuestSystem.AddQuestToDatabase(q);
        _made.Add(q);
        return q;
    }

    private Quest Track(Quest q) { _made.Add(q); return q; }

    private static async Task CallDaily(Character current, string step)
    {
        var engine = GameEngine.Instance;
        var old = engine.CurrentPlayer;
        engine.CurrentPlayer = current;
        try
        {
            var m = typeof(DailySystemManager).GetMethod(step, INP)!;
            var task = (Task)m.Invoke(DailySystemManager.Instance, null)!;
            (await Task.WhenAny(task, Task.Delay(TimeSpan.FromSeconds(60)))).Should().BeSameAs(task, "{0} must return", step);
            await task;
        }
        catch (TargetInvocationException ex) when (ex.InnerException != null) { throw ex.InnerException; }
        finally { engine.CurrentPlayer = old; }
    }

    // ---------- B1: online, quests age once a day, by the world reset ----------

    [Fact]
    public async Task B1_InTheMud_APlayersDailyReset_AgesNoQuest_TheirsOrAnotherPlayers()
    {
        var alpha = Hero("Fb Alpha");
        var mine = Held("Fb Alpha");
        var theirs = Held("Fb Beta");
        using (new InMud())
            await CallDaily(alpha, "ProcessPlayerDailyEvents");
        theirs.OccupiedDays.Should().Be(0, "another player's reset never ages a quest that is not theirs");
        theirs.Deleted.Should().BeFalse();
        mine.OccupiedDays.Should().Be(0, "the world reset ages it, once a day for everyone");
    }

    [Fact]
    public async Task B1_SinglePlayer_TheDailyReset_StillAgesTheQuest()
    {
        var alpha = Hero("Fb Solo");
        var mine = Held("Fb Solo");
        OnlineField.GetValue(null).Should().Be(false);
        await CallDaily(alpha, "ProcessDailyEvents");
        mine.OccupiedDays.Should().Be(1);
    }

    [Fact]
    public void B1_TheWorldReset_AgesEveryClaimedQuestOnce()
    {
        var a = Held("Fb Gamma");
        var b = Held("Fb Delta");
        QuestSystem.ProcessDailyQuestMaintenance();
        a.OccupiedDays.Should().Be(1);
        b.OccupiedDays.Should().Be(1);
        File.ReadAllText(SourcePath("Systems", "WorldSimService.cs")).Should().Contain("QuestSystem.ProcessDailyQuestMaintenance();");
    }

    private static QuestData Saved(Quest q, int days) => new QuestData
    {
        Id = q.Id, Title = q.Title, Initiator = q.Initiator, Occupier = q.Occupier, OccupiedDays = days,
        DaysToComplete = q.DaysToComplete, Status = QuestStatus.Active, StartTime = q.Date, MinLevel = 1, MaxLevel = 9999,
    };

    [Fact]
    public void B1_InTheMud_ALogin_KeepsTheDaysTheWorldCounted()
    {
        var q = Held("Fb Epsilon", days: 5);
        var save = Saved(q, 2);
        using (new InMud())
            QuestSystem.MergePlayerQuests("Fb Epsilon", new List<QuestData> { save });
        var held = QuestSystem.GetPlayerQuests("Fb Epsilon");
        held.Should().ContainSingle(x => x.Id == q.Id);
        Track(held.Single()).OccupiedDays.Should().Be(5, "the days the world reset counted while the player was away stay");
    }

    [Fact]
    public void B1_InTheMud_ALogin_DoesNotHandBackAQuestTheWorldFailed()
    {
        var q = Held("Fb Zeta", days: 15);
        var save = Saved(q, 3);
        q.Deleted = true; q.Occupier = "";   // as ProcessQuestFailure leaves it
        using (new InMud())
            QuestSystem.MergePlayerQuests("Fb Zeta", new List<QuestData> { save });
        QuestSystem.GetPlayerQuests("Fb Zeta").Should().BeEmpty("the failure mail said it failed; the player does not hold it");
    }

    [Fact]
    public void B1_SinglePlayer_ALoad_RestoresTheSaveAsSaved()
    {
        var q = Held("Fb Eta", days: 15);
        var save = Saved(q, 3);
        q.Deleted = true; q.Occupier = "";
        QuestSystem.MergePlayerQuests("Fb Eta", new List<QuestData> { save });
        var held = QuestSystem.GetPlayerQuests("Fb Eta");
        held.Should().ContainSingle(x => x.Id == q.Id);
        Track(held.Single()).OccupiedDays.Should().Be(3, "a single player load is the save, whatever an earlier game left in memory");
    }

    // ---------- B6: beating an NPC does not take another player's bounty on it ----------

    private Quest BountyOn(string target, string occupier = "", string offeredTo = "")
    {
        var q = new Quest
        {
            Title = "Fb wanted", Initiator = QuestSystem.CrownInitiator, QuestTarget = QuestTarget.DefeatNPC,
            TargetNPCName = target, BountyGold = 4000, RewardType = QuestRewardType.Money,
            Occupier = occupier, OfferedTo = offeredTo, Date = DateTime.Now, DaysToComplete = 14, MinLevel = 1, MaxLevel = 9999,
        };
        q.Objectives.Add(new QuestObjective(QuestObjectiveType.DefeatNPC, "Fb", 1, target, target));
        QuestSystem.AddQuestToDatabase(q);
        _made.Add(q);
        return q;
    }

    [Fact]
    public void B6_BeatingAnNpc_DoesNotPayOrTakeAnotherPlayersClaimedBounty()
    {
        var alpha = Hero("Fb Sheriff");
        var bounty = BountyOn("Fb Rogue One", occupier: "Fb Claimant");
        QuestSystem.AutoCompleteBountyForNPC(alpha, "Fb Rogue One").Should().Be(0);
        alpha.Gold.Should().Be(0);
        bounty.Deleted.Should().BeFalse("the other player keeps the quest");
        bounty.Occupier.Should().Be("Fb Claimant");
    }

    [Fact]
    public void B6_BeatingAnNpc_DoesNotTakeAQuestOfferedToAnotherPlayer()
    {
        var alpha = Hero("Fb Sheriff");
        var quest = BountyOn("Fb Rogue Two", offeredTo: "Fb Offeree");
        QuestSystem.AutoCompleteBountyForNPC(alpha, "Fb Rogue Two").Should().Be(0);
        quest.Deleted.Should().BeFalse();
    }

    [Fact]
    public void B6_AnOpenBounty_AndTheWinnersOwn_StillPay()
    {
        var alpha = Hero("Fb Sheriff");
        var open = BountyOn("Fb Rogue Three");
        var own = BountyOn("Fb Rogue Four", occupier: "Fb Sheriff");
        QuestSystem.AutoCompleteBountyForNPC(alpha, "Fb Rogue Three").Should().Be(4000);
        QuestSystem.AutoCompleteBountyForNPC(alpha, "Fb Rogue Four").Should().Be(4000);
        open.Deleted.Should().BeTrue();
        own.Deleted.Should().BeTrue();
        alpha.Gold.Should().Be(8000);
    }

    // ---------- B7: one Sellsword contract, one claim ----------

    private Quest Contract(string occupier = "", int minLevel = 1, int maxLevel = 99)
    {
        var q = new Quest
        {
            Id = "merc_fb_" + Guid.NewGuid().ToString("N").Substring(0, 8), Title = "Fb contract", Initiator = QuestSystem.MERC_INITIATOR_CROWN,
            IsMercContract = true, IssuingFaction = Faction.TheCrown, MercContractTier = 1, BountyGold = 1500,
            RewardType = QuestRewardType.Money, Reward = GameConfig.QuestRewardMedium, Penalty = 2,
            Occupier = occupier, MinLevel = minLevel, MaxLevel = maxLevel, DaysToComplete = 7, Date = DateTime.Now,
        };
        q.Objectives.Add(new QuestObjective(QuestObjectiveType.KillMonsters, "Fb", 5, "", ""));
        QuestSystem.AddQuestToDatabase(q);
        _made.Add(q);
        return q;
    }

    private static async Task<string> ContractCard(Character hero, Quest quest, params string[] input)
    {
        var road = new AnchorRoadLocation();
        var s = At(road, hero, input);
        await Run(road, "ShowMercContractDetails", quest);
        return s.Text;
    }

    [Fact]
    public async Task B7_AClaimOnAContractAnotherPlayerHolds_IsRefused_AndTheHolderKeepsIt()
    {
        GameConfig.Language = "en";
        var quest = Contract(occupier: "Fb First");
        var second = Hero("Fb Second");
        string text = await ContractCard(second, quest, "Y");
        quest.Occupier.Should().Be("Fb First");
        second.MercContractsClaimedToday.Should().Be(0, "a refused claim does not use a daily slot");
        text.Should().Contain(Loc.Get("merc.contract_taken"));
    }

    [Fact]
    public async Task B7_AClaimOutsideTheLevelBand_IsRefused()
    {
        GameConfig.Language = "en";
        var quest = Contract(minLevel: 60, maxLevel: 80);
        var low = Hero("Fb Low", level: 20);
        await ContractCard(low, quest, "Y");
        quest.Occupier.Should().BeEmpty();
    }

    [Fact]
    public async Task B7_AFreeContractInBand_IsClaimed()
    {
        GameConfig.Language = "en";
        var quest = Contract();
        var hero = Hero("Fb Taker");
        string text = await ContractCard(hero, quest, "Y");
        quest.Occupier.Should().Be("Fb Taker");
        hero.MercContractsClaimedToday.Should().Be(1);
        text.Should().Contain(Loc.Get("merc.contract_claimed_msg"));
    }

    // ---------- B9: a board bounty an NPC finishes leaves the board ----------

    [Fact]
    public void B9_AnNpcThatWinsABoardBounty_TakesItOffTheBoard()
    {
        var board = new Quest
        {
            Title = "Fb board", Initiator = QuestSystem.BountyBoardInitiator, QuestTarget = QuestTarget.ReachFloor,
            Difficulty = 1, Reward = 1, RewardType = QuestRewardType.Money, MinLevel = 500, MaxLevel = 510,
            Date = DateTime.Now, DaysToComplete = 7,
        };
        QuestSystem.AddQuestToDatabase(board);
        _made.Add(board);
        var npc = new NPC { Name1 = "Fb Hunter", Name2 = "Fb Hunter", Level = 505, HP = 500, MaxHP = 500, Gold = 0 };
        var simBefore = typeof(WorldSimulator).GetField("_instance", SNP)!.GetValue(null);
        try
        {
            var sim = new WorldSimulator();
            var take = typeof(WorldSimulator).GetMethod("NPCTakeBountyQuest", INP)!;
            for (int i = 0; i < 60 && npc.Gold == 0; i++)
                take.Invoke(sim, new object[] { npc });
        }
        finally { typeof(WorldSimulator).GetField("_instance", SNP)!.SetValue(null, simBefore); }

        npc.Gold.Should().BeGreaterThan(0, "the hunter won one of sixty tries at 55 percent or better");
        board.Deleted.Should().BeTrue("the news says the hunt is done");
        QuestSystem.GetBountyBoardQuests(Hero("Fb Viewer", level: 505)).Should().NotContain(board);
    }

    // ---------- B2: Sellsword contracts only at the Sellsword Hall ----------

    [Fact]
    public void B2_TheQuestHall_DoesNotOfferASellswordContract()
    {
        var hero = Hero("Fb Hall Viewer");
        var contract = Contract();
        QuestSystem.GetAvailableQuests(hero).Should().NotContain(contract);
    }

    [Fact]
    public async Task B2_TheQuestHall_DoesNotTurnInOrPayASellswordContract()
    {
        GameConfig.Language = "en";
        var hero = Hero("Fb Hall Turnin");
        var contract = Contract(occupier: "Fb Hall Turnin");
        contract.Objectives[0].CurrentProgress = contract.Objectives[0].RequiredProgress;   // done
        var hall = new QuestHallLocation(null!);
        At(hall, hero, "1");
        await Run(hall, "TurnInQuest");
        hero.Gold.Should().Be(0, "a contract is paid at the Sellsword Hall, at its own rate");
        contract.Deleted.Should().BeFalse();
        contract.Occupier.Should().Be("Fb Hall Turnin", "the player can still turn it in at the Sellsword Hall");
        QuestSystem.CompleteMercContract(hero, contract).ok.Should().BeTrue();
    }

    // ---------- B3: a low level board is not left empty by a high level visit ----------

    [Fact]
    public void B3_TheSellswordBoard_FillsForALowLevelPlayer_AfterAHighLevelPlayerFilledIt()
    {
        var high = Hero("Fb Board High", level: 400);
        var low = Hero("Fb Board Low", level: 300);
        try
        {
            QuestSystem.RefreshMercBoard(high);
            QuestSystem.RefreshMercBoard(low);
            QuestSystem.GetAvailableMercContracts(low).Should().HaveCountGreaterOrEqualTo(3, "one per faction at least");
            QuestSystem.GetAvailableMercContracts(high).Should().HaveCountGreaterOrEqualTo(3, "the high level contracts stay");
        }
        finally
        {
            _made.AddRange(QuestSystem.GetAvailableMercContracts(high));
            _made.AddRange(QuestSystem.GetAvailableMercContracts(low));
        }
    }

    [Fact]
    public void B3_TheSellswordBoard_DoesNotRefillWhenThePlayerAlreadyHasContracts()
    {
        var hero = Hero("Fb Board Again", level: 350);
        try
        {
            QuestSystem.RefreshMercBoard(hero);
            int first = QuestSystem.GetAvailableMercContracts(hero).Count;
            QuestSystem.RefreshMercBoard(hero);
            QuestSystem.GetAvailableMercContracts(hero).Count.Should().Be(first);
        }
        finally { _made.AddRange(QuestSystem.GetAvailableMercContracts(hero)); }
    }

    [Fact]
    public void B3_TheBountyBoard_FillsForALowLevelPlayer_AfterAHighLevelPlayerFilledIt()
    {
        var low = Hero("Fb Bounty Low", level: 200);
        var high = Hero("Fb Bounty High", level: 260);
        try
        {
            QuestSystem.RefreshBountyBoard(260);
            QuestSystem.RefreshBountyBoard(200);
            QuestSystem.GetBountyBoardQuests(low).Should().HaveCount(5);
            QuestSystem.GetBountyBoardQuests(high).Should().HaveCount(5);
            QuestSystem.RefreshBountyBoard(200);
            QuestSystem.GetBountyBoardQuests(low).Should().HaveCount(5, "a full board for this level is not filled again");
        }
        finally
        {
            _made.AddRange(QuestSystem.GetBountyBoardQuests(low));
            _made.AddRange(QuestSystem.GetBountyBoardQuests(high));
        }
    }

    // ---------- B4: "Survive N floors" counts floors descended after the claim ----------

    private Quest Survive(string occupier)
    {
        var q = QuestSystem.CreateDungeonQuest(QuestTarget.SurviveDungeon, 2, null, 30);
        q.Occupier = occupier;
        _made.Add(q);
        return q;
    }

    [Fact]
    public void B4_ADeepPlayer_DoesNotMeetSurviveFloors_ByEnteringTheDungeon()
    {
        var hero = Hero("Fb Deep", level: 60);
        var q = Survive("Fb Deep");
        var floors = q.Objectives.Single(o => o.ObjectiveType == QuestObjectiveType.ReachDungeonFloor);
        floors.RequiredProgress.Should().BeInRange(8, 10);
        QuestSystem.OnDungeonFloorReached(hero, 60);
        floors.IsComplete.Should().BeFalse("standing on a deep floor is not going down floors");
        floors.CurrentProgress.Should().Be(0);
    }

    [Fact]
    public void B4_SurviveFloors_CountsEachFloorDescended_AfterTheClaim()
    {
        var hero = Hero("Fb Descender", level: 60);
        var q = Survive("Fb Descender");
        var floors = q.Objectives.Single(o => o.ObjectiveType == QuestObjectiveType.ReachDungeonFloor);
        QuestSystem.OnDungeonFloorReached(hero, 40);   // entering: where the count starts
        QuestSystem.OnDungeonFloorReached(hero, 41);
        QuestSystem.OnDungeonFloorReached(hero, 42);
        floors.CurrentProgress.Should().Be(2);
        QuestSystem.OnDungeonFloorReached(hero, 41);   // going up counts nothing
        floors.CurrentProgress.Should().Be(2);
        QuestSystem.OnDungeonFloorReached(hero, 41);   // the same floor again counts nothing
        floors.CurrentProgress.Should().Be(2);
        QuestSystem.OnDungeonFloorReached(hero, 44);   // three floors down at once
        floors.CurrentProgress.Should().Be(5);
        QuestSystem.OnDungeonFloorReached(hero, 60);
        floors.IsComplete.Should().BeTrue();
        floors.CurrentProgress.Should().Be(floors.RequiredProgress);
    }

    [Fact]
    public void B4_AReachFloorQuest_IsStillMetByReachingTheFloor()
    {
        var hero = Hero("Fb Reacher", level: 30);
        var q = QuestSystem.CreateDungeonQuest(QuestTarget.ReachFloor, 2, null, 30);
        q.Occupier = "Fb Reacher";
        _made.Add(q);
        var floor = q.Objectives.First(o => o.ObjectiveType == QuestObjectiveType.ReachDungeonFloor);
        QuestSystem.OnDungeonFloorReached(hero, 100);
        floor.IsComplete.Should().BeTrue();
    }

    // ---------- B5: the royal "hunt a criminal" says it is a dungeon boss hunt ----------

    [Theory]
    [InlineData("en")]
    [InlineData("es")]
    [InlineData("fr")]
    [InlineData("hu")]
    [InlineData("it")]
    public void B5_TheRoyalCriminalHunt_SaysItIsADungeonBossKill_InEveryLanguage(string lang)
    {
        var old = GameConfig.Language;
        try
        {
            GameConfig.Language = lang;
            string offer = Loc.Get("castle.quest_type_4");
            offer.Should().NotBe(Loc.GetIn(lang, "castle.quest_type_0"));
            var boss = new Dictionary<string, string> { ["en"] = "boss", ["es"] = "jefe", ["fr"] = "boss", ["hu"] = "főellenség", ["it"] = "boss" };
            offer.ToLowerInvariant().Should().Contain(boss[lang], "the offer names what completes it");
            offer.Should().NotContain("shadows").And.NotContain("sombras").And.NotContain("ombre").And.NotContain("árnyék");

            var hero = Hero("Fb Royal " + lang, level: 30);
            var quest = Track(QuestSystem.CreateRoyalAudienceQuest(hero, "Fb King", 2, 1000, 1000, CastleLocation.RoyalQuestTypes[4]));
            quest.Objectives[0].ObjectiveType.Should().Be(QuestObjectiveType.KillBoss, "still completed by a floor boss kill");
            quest.Objectives[0].GetDisplayDescription().Should().Be(offer, "the journal shows the same wording as the offer");
        }
        finally { GameConfig.Language = old; }
    }

    // ---------- B8: the bounty board shows the gold a bounty pays ----------

    [Fact]
    public void B8_AnNpcBounty_ShowsTheGoldItPays()
    {
        GameConfig.Language = "en";
        var q = new Quest { Initiator = QuestSystem.CrownInitiator, QuestTarget = QuestTarget.DefeatNPC, TargetNPCName = "Fb Mark", Reward = 120, RewardType = QuestRewardType.Money };
        q.GetRewardDescription().Should().Be(Loc.Get("quest.reward.gold_sum", "12,000"));
    }

    [Fact]
    public void B8_APlayerBounty_ShowsItsPostedGold()
    {
        GameConfig.Language = "en";
        var q = new Quest { Initiator = QuestSystem.CrownInitiator, QuestTarget = QuestTarget.DefeatNPC, TargetNPCName = "Fb Outlaw", BountyGold = 7500, IsPlayerBounty = true, RewardType = QuestRewardType.Money };
        q.GetRewardDescription().Should().Be(Loc.Get("quest.reward.gold_sum", "7,500"));
    }

    [Fact]
    public void B8_TheShownGold_IsWhatBeatingTheTargetPays()
    {
        var alpha = Hero("Fb Payee");
        var q = BountyOn("Fb Rogue Paid");
        q.BountyGold = 0; q.Reward = 37;
        GameConfig.Language = "en";
        q.GetRewardDescription().Should().Be(Loc.Get("quest.reward.gold_sum", "3,700"));
        QuestSystem.AutoCompleteBountyForNPC(alpha, "Fb Rogue Paid").Should().Be(3700);
    }

    [Fact]
    public void B8_ATieredQuest_KeepsItsTierText()
    {
        GameConfig.Language = "en";
        var q = new Quest { Initiator = QuestSystem.CrownInitiator, QuestTarget = QuestTarget.Monster, Reward = 2, RewardType = QuestRewardType.Experience };
        q.GetRewardDescription().Should().Be($"{Loc.Get("quest.reward.low")} {Loc.Get("quest.reward_type.experience")}");
    }

    // ---------- B10: contract and royal texts say what happens ----------

    [Theory]
    [InlineData("en")]
    [InlineData("hu")]
    public async Task B10_TheContractCard_ShowsTheDays_AndTheReworded_FailureLine_InRowsOf79(string lang)
    {
        var old = GameConfig.Language;
        try
        {
            GameConfig.Language = lang;
            var quest = Contract();
            quest.DaysToComplete = 14;
            quest.CommentKey = "merc.contract.crown_warband_breaker.comment";
            quest.CommentArgs = new List<string> { "5" };
            var hero = Hero("Fb Card " + lang);
            string text = await ContractCard(hero, quest, "N");
            text.Should().Contain(Loc.Get("merc.contract_days_line", 14));
            text.Should().Contain(Loc.Get("merc.contract_penalty_line"));
            // the rows this card writes (the box header is drawn by WriteBoxHeader and is not part of this fix)
            foreach (var row in text.Split('\n').Select(r => r.TrimEnd('\r')).Where(r => !"╔║╚".Contains(r.TrimStart().FirstOrDefault())))
                row.Length.Should().BeLessOrEqualTo(79, "row: " + row);
            Loc.Get("merc.contract_taken").Length.Should().BeLessOrEqualTo(77);
        }
        finally { GameConfig.Language = old; }
    }

    [Theory]
    [InlineData("en")]
    [InlineData("es")]
    [InlineData("fr")]
    [InlineData("hu")]
    [InlineData("it")]
    public void B10_TheFailureLine_PromisesNoPenalty_AndTheContractsPromiseNoPilgrimOrHealing(string lang)
    {
        Loc.GetIn(lang, "merc.contract_penalty_line").Should().NotContain("{0}");
        Loc.GetIn("en", "merc.contract_penalty_line").Should().NotContain("abandon");
        Loc.GetIn("en", "merc.contract.faith_escort_pilgrim.objective").Should().NotContain("pilgrim");
        Loc.GetIn("en", "merc.contract.faith_purge_undead.comment").Should().NotContain("healed");
        foreach (var k in new[] { "faith_purge_undead", "crown_warband_breaker", "shadows_message_job", "faith_ghoul_cleansing" })
            Loc.GetIn("en", $"merc.contract.{k}.objective").Should().NotContain("dungeon", "a wilderness kill counts too");
        Loc.GetIn(lang, "castle.quest_target_any", 17).Should().Contain("17");
    }

    [Fact]
    public void B10_TheRoyalMonsterQuest_CountsAnyMonster_AsTheTargetLineSays()
    {
        var hero = Hero("Fb Royal Hunt", level: 40);
        var quest = Track(QuestSystem.CreateRoyalAudienceQuest(hero, "Fb King", 2, 1000, 1000, CastleLocation.RoyalQuestTypes[0]));
        var o = quest.Objectives[0];
        o.ObjectiveType.Should().Be(QuestObjectiveType.KillMonsters);
        QuestSystem.OnMonsterKilled(hero, "Goblin", tierName: "Goblin");
        o.CurrentProgress.Should().Be(1, "any monster counts, whatever name the offer drew");
        File.ReadAllText(SourcePath("Locations", "CastleLocation.cs")).Should().Contain("Loc.Get(\"castle.quest_target_any\", quest.Objectives[0].RequiredProgress)");
    }

    // ---------- the swamp's Mire Zombie counts for the Zombie contract ----------

    [Fact]
    public void MireZombie_CountsForAZombieTarget()
    {
        var hero = Hero("Fb Swamp");
        var q = Held("Fb Swamp");
        q.Objectives.Clear();
        q.Objectives.Add(new QuestObjective(QuestObjectiveType.KillSpecificMonster, "Fb", 3, "Zombie", "Zombie"));
        QuestSystem.OnMonsterKilled(hero, "Mire Zombie", tierName: "Mire Zombie");   // as WildernessLocation names it
        q.Objectives[0].CurrentProgress.Should().Be(1);
        QuestSystem.OnMonsterKilled(hero, "Bog Lurker", tierName: "Bog Lurker");
        q.Objectives[0].CurrentProgress.Should().Be(1);
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
                return System.Text.RegularExpressions.Regex.Replace(System.Text.Encoding.UTF8.GetString(Output.ToArray()), "\u001b\\[[0-9;?]*[A-Za-z]", "");
            }
        }
    }

    private static Screen At(BaseLocation loc, Character hero, params string[] lines)
    {
        var s = new Screen();
        s.Term = new TerminalEmulator(new LineStream(lines.Concat(Enumerable.Repeat("", 20)), _ =>
        {
            s.Term.StreamWriterInternal?.Flush();
            s.Output.WriteByte((byte)'\n');
        }), s.Output);
        typeof(BaseLocation).GetField("terminal", INP)!.SetValue(loc, s.Term);
        typeof(BaseLocation).GetField("currentPlayer", INP)!.SetValue(loc, hero);
        return s;
    }

    private static async Task Run(object target, string method, params object?[] args)
    {
        var m = target.GetType().GetMethod(method, INP);
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

    private static string SourcePath(params string[] parts)
    {
        var dir = AppContext.BaseDirectory;
        while (dir != null && !File.Exists(Path.Combine(dir, "usurper-reloaded.csproj")))
            dir = Path.GetDirectoryName(dir);
        dir.Should().NotBeNull();
        return Path.Combine(new[] { dir!, "Scripts" }.Concat(parts).ToArray());
    }
}
