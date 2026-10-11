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
using Xunit;

namespace UsurperReborn.Tests;

/// <summary>
/// 1.2.8: an honor duel the player wins (issued by the player in town or by an NPC in the street) leaves
/// the NPC with the HP it had before the duel, so it stays in the room, is not queued for a respawn, keeps its gold and makes no "returned from the dead"
/// news; a kill contract on it is neither paid nor ticked, while a bounty to defeat it is, as in the Inn's
/// duel. Every fight here is a real fight through StreetEncounterSystem on a scripted terminal.
/// </summary>
[Collection("SharedGameSingletons")]
public class DuelQuest128Tests
{
    private const BindingFlags F = BindingFlags.NonPublic | BindingFlags.Instance;
    private const long BigBounty = 1_000_000;

    private sealed class ZeroRandom : Random { public override int Next(int maxValue) => 0; public override int Next(int min, int max) => min; }

    /// <summary>A hero who wins in one exchange and already holds every achievement, so no unlock adds to a reward.</summary>
    private static Character Hero(int level = 18)
    {
        var hero = BareHero(level);
        AchievementSystem.Initialize();
        foreach (var a in AchievementSystem.GetAllAchievements()) hero.Achievements.Unlock(a.Id);
        return hero;
    }

    private static Character BareHero(int level) => new Character
    {
        Name1 = "duel_tester", Name2 = "Duel Tester", Class = CharacterClass.Warrior, Race = CharacterRace.Human,
        Level = level, AI = CharacterAI.Human, HP = 50000, MaxHP = 50000, BaseMaxHP = 50000,
        Strength = 5000, BaseStrength = 5000, Defence = 2000, BaseDefence = 2000, WeapPow = 5000,
        Dexterity = 200, BaseDexterity = 200, Agility = 200, BaseAgility = 200, Constitution = 200, BaseConstitution = 200,
        Stamina = 200, BaseStamina = 200, CombatSpeed = CombatSpeed.Instant,
        MKills = 1,
    };

    private static NPC Rival(string name, long hp = 137, long gold = 4000) => new NPC
    {
        ID = "npc_" + name.ToLowerInvariant().Replace(' ', '_'), Name1 = name, Name2 = name, Level = 18,
        HP = hp, MaxHP = 300, BaseMaxHP = 300, Strength = 20, BaseStrength = 20, Defence = 5, BaseDefence = 5,
        Gold = gold, CurrentLocation = "Main Street", Memory = new MemorySystem(),
    };

    private static StreetEncounterSystem Street()
    {
        var street = (StreetEncounterSystem)Activator.CreateInstance(typeof(StreetEncounterSystem), nonPublic: true)!;
        typeof(StreetEncounterSystem).GetField("_random", F)!.SetValue(street, new ZeroRandom());
        return street;
    }

    private static TerminalEmulator Script(MemoryStream output, params string[] lead) =>
        new TerminalEmulator(new LineStream(lead.Concat(Enumerable.Repeat("A", 12))
            .Concat(Enumerable.Range(0, 60).Select(i => i % 2 == 0 ? "P" : "N"))), output);   // P passes on a loot drop, N turns an offer away

    private static string Plain(MemoryStream output, TerminalEmulator term)
    {
        term.StreamWriterInternal?.Flush();
        return Regex.Replace(Encoding.UTF8.GetString(output.ToArray()), "\u001b\\[[0-9;]*[A-Za-z]", "");
    }

    /// <summary>The roster holds only the given NPCs while the body runs, and news goes to the returned list.</summary>
    private static async Task<List<string>> InTown(IEnumerable<NPC> town, Func<Task> body)
    {
        var roster = NPCSpawnSystem.Instance.ActiveNPCs;
        var saved = roster.ToList();
        var news = new List<string>();
        roster.Clear();
        roster.AddRange(town);
        NewsSystem.Instance.SetCatchUpBuffer(news);
        try { await body(); }
        finally
        {
            NewsSystem.Instance.ClearCatchUpBuffer();
            roster.Clear();
            roster.AddRange(saved);
        }
        return news;
    }

    /// <summary>The fight AttackCharacter runs: an honor duel (the town challenge) or an attack (Main Street).</summary>
    private static async Task<EncounterResult> Attack(Character hero, Character target, bool honorDuel)
    {
        var output = new MemoryStream();
        var term = Script(output);
        var result = await Street().AttackCharacter(hero, target, term, isHonorDuel: honorDuel);
        result.Victory.Should().BeTrue("the scripted hero wins: " + Plain(output, term));
        return result;
    }

    /// <summary>The street challenge encounter with the given answer (A accept, I insult), fought to a win.</summary>
    private static async Task<EncounterResult> NpcIssuedChallenge(Character hero, string answer)
    {
        var output = new MemoryStream();
        var term = Script(output, answer);
        var result = new EncounterResult();
        await (Task)typeof(StreetEncounterSystem).GetMethod("ProcessChallengeEncounter", F)!
            .Invoke(Street(), new object[] { hero, GameLocation.MainStreet, result, term })!;
        result.Victory.Should().BeTrue("the scripted hero wins: " + Plain(output, term));
        return result;
    }

    private static Quest Contract(Character hero, NPC target, QuestTarget kind, bool claimed)
    {
        var quest = new Quest
        {
            Title = "Mark: " + target.Name, Initiator = "A Patron", QuestTarget = kind, TargetNPCName = target.Name,
            BountyGold = BigBounty, Occupier = claimed ? hero.Name2 : "", Date = DateTime.Now, DaysToComplete = 30,
        };
        quest.Objectives.Add(new QuestObjective(QuestObjectiveType.DefeatNPC, "Defeat " + target.Name, 1, target.Name, target.Name));
        QuestSystem.AddQuestToDatabase(quest);
        return quest;
    }

    private static Dictionary<string, int> RespawnQueue(WorldSimulator sim) =>
        (Dictionary<string, int>)typeof(WorldSimulator).GetField("deadNPCRespawnTimers", F)!.GetValue(sim)!;

    /// <summary>The simulator's readers of a dead NPC: the queue after a load, then enough ticks to respawn it.</summary>
    private static bool QueuedThenRespawned(NPC npc)
    {
        var sim = new WorldSimulator();
        sim.ProcessDeadNPCsOnLoad();
        bool queued = RespawnQueue(sim).ContainsKey(npc.Id ?? npc.Name);
        for (int i = 0; i < 3; i++) typeof(WorldSimulator).GetMethod("ProcessNPCRespawns", F)!.Invoke(sim, null);
        return queued;
    }

    private sealed class TownProbe : BaseLocation
    {
        public TownProbe() : base(GameLocation.MainStreet, "Duel Probe") { }
        protected override bool ShouldNPCAcceptDuel(NPC npc) => true;
        public List<NPC> Listed() => GetLiveNPCsAtLocation();
    }

    private static TownProbe Place(Character hero, TerminalEmulator term)
    {
        var probe = new TownProbe();
        typeof(BaseLocation).GetField("terminal", F)!.SetValue(probe, term);
        typeof(BaseLocation).GetField("currentPlayer", F)!.SetValue(probe, hero);
        return probe;
    }

    // ---------- the honor duel the player issues ----------

    [Fact]
    public async Task AWonHonorDuel_LeavesTheNpcsHpAsItWasBeforeTheFight()
    {
        var hero = Hero();
        var npc = Rival("Spared Rival One");
        await InTown(new[] { npc }, () => Attack(hero, npc, honorDuel: true));

        npc.HP.Should().Be(137, "the NPC keeps the HP it had before the duel, neither 0 nor healed to MaxHP");
        npc.IsAlive.Should().BeTrue();
        npc.IsDead.Should().BeFalse();
    }

    [Fact]
    public async Task AWonHonorDuel_TheNpcIsStillListedInTheRoom()
    {
        var hero = Hero();
        var npc = Rival("Spared Rival Two");
        var listed = new List<NPC>();
        await InTown(new[] { npc }, async () =>
        {
            await Attack(hero, npc, honorDuel: true);
            listed = Place(hero, new TerminalEmulator(new MemoryStream(), new MemoryStream())).Listed();
        });
        listed.Should().Contain(npc);
    }

    [Fact]
    public async Task AWonHonorDuel_QueuesNoRespawn_TakesNoGold_AndMakesNoNews()
    {
        var hero = Hero();
        var npc = Rival("Spared Rival Three", gold: 4000);
        bool queued = true;
        var news = await InTown(new[] { npc }, async () =>
        {
            await Attack(hero, npc, honorDuel: true);
            queued = QueuedThenRespawned(npc);
        });

        queued.Should().BeFalse("a living NPC is not queued for a respawn");
        npc.Gold.Should().Be(4000, "no respawn, so no half of its gold lost");
        npc.HP.Should().Be(137, "no respawn, so no heal to MaxHP");
        npc.CurrentLocation.Should().Be("Main Street");
        news.Should().NotContain(n => n.Contains(Loc.Get("worldsim.news_respawned", npc.Name)));
    }

    [Fact]
    public async Task AnAttackThatIsNotADuel_StillDropsTheNpc_AndTheSimulatorRespawnsIt()
    {
        var hero = Hero();
        var npc = Rival("Slain Rival One", gold: 4000);
        bool queued = false;
        long hpAfterFight = -1;
        var news = await InTown(new[] { npc }, async () =>
        {
            await Attack(hero, npc, honorDuel: false);
            hpAfterFight = npc.HP;
            queued = QueuedThenRespawned(npc);
        });

        hpAfterFight.Should().Be(0, "an attack leaves the NPC at 0 HP");
        queued.Should().BeTrue("the simulator queues it for a respawn");
        npc.Gold.Should().Be(2000, "the respawn costs it half its gold");
        news.Should().Contain(n => n.Contains(Loc.Get("worldsim.news_respawned", npc.Name)));
        hero.Darkness.Should().BeGreaterThan(0, "an attack is a dark deed");
    }

    [Fact]
    public async Task AnNpcAtZeroHpBeforeTheDuel_StaysAtZero()
    {
        var hero = Hero();
        var npc = Rival("Fallen Rival", hp: 0);
        await InTown(new[] { npc }, () => Attack(hero, npc, honorDuel: true));
        npc.HP.Should().Be(0, "the duel does not raise an NPC that was already down");
    }

    [Fact]
    public async Task AWonHonorDuel_KeepsFame_TheDefeatedMemory_AndNoMurderCount()
    {
        var hero = Hero();
        long fame0 = hero.Fame;
        var npc = Rival("Spared Rival Four");
        await InTown(new[] { npc }, () => Attack(hero, npc, honorDuel: true));

        (hero.Fame - fame0).Should().Be(15);
        npc.Memory!.GetMemoriesOfType(MemoryType.Defeated).Should().ContainSingle(m => m.InvolvedCharacter == hero.Name2);
        hero.MurdersToday.Should().Be(0);
        hero.Darkness.Should().Be(0);
    }

    [Fact]
    public async Task TheTownChallenge_Won_CountsTowardTheCap_GivesAlignment_AndSparesTheNpc()
    {
        var hero = Hero();
        long chivalry0 = hero.Chivalry, fame0 = hero.Fame, kills0 = hero.PKills;
        var npc = Rival("Spared Rival Five");
        var output = new MemoryStream();
        var term = Script(output);
        var probe = Place(hero, term);
        await InTown(new[] { npc }, async () =>
        {
            try { await (Task)typeof(BaseLocation).GetMethod("ChallengeNPC", F)!.Invoke(probe, new object[] { npc })!; }
            catch (IOException) { }
        });
        string screen = Plain(output, term);

        screen.Should().Contain(Loc.Get("base.duel_victory", npc.Name2));
        npc.HP.Should().Be(137, "the player's challenge spares the NPC");
        hero.InnDuelsToday.Should().Be(1, "a won duel counts toward the shared daily cap");
        hero.PKills.Should().Be(kills0 + 1);
        hero.Chivalry.Should().BeGreaterThan(chivalry0, "a won duel still gives its alignment");
        (hero.Fame - fame0).Should().Be(15);
    }

    // ---------- contracts and bounties ----------

    [Fact]
    public async Task AWonHonorDuel_DoesNotPayAKillContractOnTheNpc()
    {
        var hero = Hero();
        var npc = Rival("Contract Rival One");
        var contract = Contract(hero, npc, QuestTarget.Assassin, claimed: false);
        long gold0 = hero.Gold;
        try
        {
            var news = await InTown(new[] { npc }, () => Attack(hero, npc, honorDuel: true));
            contract.Deleted.Should().BeFalse("the contract is for a kill and the NPC lives");
            (hero.Gold - gold0).Should().BeLessThan(BigBounty, "no contract gold was paid");
            news.Should().NotContain(n => n.Contains(Loc.Get("quest.bounty_collected_news", hero.Name2, npc.Name, BigBounty)));
        }
        finally { contract.Deleted = true; }
    }

    [Fact]
    public async Task AWonHonorDuel_DoesNotTickAClaimedKillContract()
    {
        var hero = Hero();
        var npc = Rival("Contract Rival Two");
        var contract = Contract(hero, npc, QuestTarget.Assassin, claimed: true);
        contract.TargetNPCName = "";   // found through the player's claimed quests only, not through the bounty payout
        try
        {
            await InTown(new[] { npc }, () => Attack(hero, npc, honorDuel: true));
            contract.Objectives[0].CurrentProgress.Should().Be(0, "a duel the NPC walks away from is not the kill");
        }
        finally { contract.Deleted = true; }
    }

    [Fact]
    public async Task AnAttackThatIsNotADuel_PaysTheKillContract_AndTicksAClaimedOne()
    {
        var hero = Hero();
        var npc = Rival("Contract Rival Three");
        var open = Contract(hero, npc, QuestTarget.Assassin, claimed: false);
        var claimed = Contract(hero, npc, QuestTarget.Assassin, claimed: true);
        claimed.TargetNPCName = "";
        long gold0 = hero.Gold;
        try
        {
            await InTown(new[] { npc }, () => Attack(hero, npc, honorDuel: false));
            open.Deleted.Should().BeTrue("a kill pays the contract");
            (hero.Gold - gold0).Should().BeGreaterThanOrEqualTo(BigBounty);
            claimed.Objectives[0].IsComplete.Should().BeTrue();
        }
        finally { open.Deleted = true; claimed.Deleted = true; }
    }

    [Fact]
    public async Task AWonHonorDuel_StillPaysABountyToDefeatTheNpc_AndTicksItsObjective()
    {
        var hero = Hero();
        var npc = Rival("Wanted Rival");
        var bounty = Contract(hero, npc, QuestTarget.DefeatNPC, claimed: false);
        var quest = Contract(hero, npc, QuestTarget.DefeatNPC, claimed: true);
        quest.TargetNPCName = "";
        long gold0 = hero.Gold;
        try
        {
            await InTown(new[] { npc }, () => Attack(hero, npc, honorDuel: true));
            bounty.Deleted.Should().BeTrue("a bounty to defeat the NPC is met by a duel, as at the Inn");
            (hero.Gold - gold0).Should().BeGreaterThanOrEqualTo(BigBounty);
            quest.Objectives[0].IsComplete.Should().BeTrue("a Defeat objective is met by a duel, as at the Inn");
            npc.HP.Should().Be(137);
        }
        finally { bounty.Deleted = true; quest.Deleted = true; }
    }

    [Fact]
    public async Task AWonHonorDuelAgainstACharacterThatIsNotAnNpc_PaysNoKillContractOnThatName()
    {
        var hero = Hero();
        var target = new Character { Name1 = "Plain Rival", Name2 = "Plain Rival", Level = 18, HP = 137, MaxHP = 300, Strength = 20, Defence = 5 };
        var named = Rival("Plain Rival");
        var contract = Contract(hero, named, QuestTarget.Assassin, claimed: false);
        try
        {
            await InTown(Array.Empty<NPC>(), () => Attack(hero, target, honorDuel: true));
            contract.Deleted.Should().BeFalse();
        }
        finally { contract.Deleted = true; }
    }

    // ---------- a street fight that is not an honor duel ----------

    [Fact]
    public async Task AnInsultedChallenger_Beaten_DropsToZero()
    {
        var hero = Hero();
        var npc = Rival("Street Rival One");
        await InTown(new[] { npc }, () => NpcIssuedChallenge(hero, "I"));
        npc.HP.Should().Be(0, "a street fight that is not a duel leaves the NPC at 0 HP");
    }

    // ---------- the challenge an NPC issues in the street ----------

    [Fact]
    public async Task TheNpcIssuedChallenge_Accepted_SparesTheNpc()
    {
        var hero = Hero();
        long fame0 = hero.Fame;
        var npc = Rival("Street Rival Two");
        await InTown(new[] { npc }, () => NpcIssuedChallenge(hero, "A"));

        npc.HP.Should().Be(137, "the challenge an NPC issues is an honor duel too: the NPC keeps its HP");
        npc.IsAlive.Should().BeTrue();
        (hero.Fame - fame0).Should().Be(15);
        hero.InnDuelsToday.Should().Be(0, "this path does not count toward the daily duel cap");
        hero.MurdersToday.Should().Be(0);
    }

    [Fact]
    public async Task TheNpcIssuedChallenge_Won_QueuesNoRespawn_TakesNoGold_AndMakesNoNews()
    {
        var hero = Hero();
        var npc = Rival("Street Rival Three", gold: 4000);
        bool queued = true;
        var news = await InTown(new[] { npc }, async () =>
        {
            await NpcIssuedChallenge(hero, "A");
            queued = QueuedThenRespawned(npc);
        });

        queued.Should().BeFalse("a living NPC is not queued for a respawn");
        npc.Gold.Should().Be(4000);
        npc.HP.Should().Be(137);
        news.Should().NotContain(n => n.Contains(Loc.Get("worldsim.news_respawned", npc.Name)));
    }

    [Fact]
    public async Task TheNpcIssuedChallenge_Won_DoesNotPayAKillContractOnTheNpc()
    {
        var hero = Hero();
        var npc = Rival("Street Rival Four");
        var contract = Contract(hero, npc, QuestTarget.Assassin, claimed: false);
        long gold0 = hero.Gold;
        try
        {
            await InTown(new[] { npc }, () => NpcIssuedChallenge(hero, "A"));
            contract.Deleted.Should().BeFalse("the contract is for a kill and the NPC lives");
            (hero.Gold - gold0).Should().BeLessThan(BigBounty, "no contract gold was paid");
        }
        finally { contract.Deleted = true; }
    }

    [Fact]
    public async Task TheNpcIssuedChallenge_Won_DoesNotTickAClaimedKillContract()
    {
        var hero = Hero();
        var npc = Rival("Street Rival Five");
        var contract = Contract(hero, npc, QuestTarget.Assassin, claimed: true);
        contract.TargetNPCName = "";   // found through the player's claimed quests only, not through the bounty payout
        try
        {
            await InTown(new[] { npc }, () => NpcIssuedChallenge(hero, "A"));
            contract.Objectives[0].CurrentProgress.Should().Be(0, "a duel the NPC walks away from is not the kill");
        }
        finally { contract.Deleted = true; }
    }
}
