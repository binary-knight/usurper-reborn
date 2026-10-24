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
/// 1.4.0 H: tell an ally to wait in a cleared room. The order is in the dungeon party menu, only in a
/// cleared room and only for an ally that takes orders. A waiting ally keeps its party slot but is in no
/// fight, takes no XP and no potion. Walking into its room or calling it back brings it back; the stairs,
/// leaving the dungeon and a save and load end the wait. Dismissing it drops its mark; a follower's party
/// view tags it.
/// </summary>
[Collection("SharedGameSingletons")]
public class H140WaitHere
{
    private const BindingFlags F = BindingFlags.NonPublic | BindingFlags.Instance;
    private const BindingFlags FS = BindingFlags.NonPublic | BindingFlags.Static;
    private const int MaxWidth = 79;
    private static readonly string[] Langs = { "en", "es", "fr", "hu", "it" };
    private static readonly string[] Keys =
    {
        "dungeon.wait_option", "dungeon.wait_pick", "dungeon.wait_order", "dungeon.wait_call_option", "dungeon.wait_call_any",
        "dungeon.wait_call_pick", "dungeon.wait_called_back", "dungeon.wait_rejoins", "dungeon.wait_catches_up", "dungeon.wait_tag",
    };

    // ---------- the rig ----------

    private sealed class Rig
    {
        public DungeonLocation D = null!;
        public MemoryStream Output = new();
        public TerminalEmulator Term = null!;
        public Character Hero = null!;
        public DungeonFloor Floor = null!;
        public string Text { get { Term.StreamWriterInternal?.Flush(); return Strip(Encoding.UTF8.GetString(Output.ToArray())); } }
    }

    private static string Strip(string s) => Regex.Replace(s, "\u001b\\[[0-9;?]*[A-Za-z]", "");

    private static IEnumerable<string> Rows(string text) => text.Replace("\r", "").Split('\n');

    private static Character Hero() => new()
    {
        Name1 = "tester", Name2 = "Tester", ID = "h140_hero", Class = CharacterClass.Warrior, Level = 20, HP = 900, MaxHP = 900,
        AI = CharacterAI.Human, Mental = 100,
    };

    private static NPC Ally(string id, string name, int level = 20) => new()
    {
        ID = id, Name1 = name, Name2 = name, Level = level, Class = CharacterClass.Warrior, Race = CharacterRace.Human,
        HP = 300, MaxHP = 300, AI = CharacterAI.Computer,
    };

    private static DungeonRoom Room(string id, bool cleared) => new()
    {
        Id = id, Name = "Room " + id, Description = "A room.", DangerRating = 1, IsExplored = true, IsCleared = cleared,
        HasMonsters = !cleared,
    };

    private static Rig NewRig(string input, bool cleared = true, IEnumerable<Character>? party = null)
    {
        var r = new Rig { Hero = Hero() };
        r.Term = new TerminalEmulator(new MemoryStream(Encoding.UTF8.GetBytes(input)), r.Output);
        r.Floor = new DungeonFloor { Level = 6, Theme = DungeonTheme.Catacombs, CurrentRoomId = "r1", EntranceRoomId = "r1" };
        r.Floor.Rooms.Add(Room("r1", cleared));
        r.Floor.Rooms.Add(Room("r2", true));
        r.D = new DungeonLocation();
        typeof(BaseLocation).GetField("terminal", F)!.SetValue(r.D, r.Term);
        typeof(BaseLocation).GetField("currentPlayer", F)!.SetValue(r.D, r.Hero);
        typeof(DungeonLocation).GetField("currentDungeonLevel", F)!.SetValue(r.D, 6);
        typeof(DungeonLocation).GetField("currentFloor", F)!.SetValue(r.D, r.Floor);
        typeof(DungeonLocation).GetField("inRoomMode", F)!.SetValue(r.D, true);
        if (party != null) r.D.teammates.AddRange(party);
        return r;
    }

    private static async Task Call(object target, string method, params object[] args)
    {
        var m = target.GetType().GetMethod(method, F) ?? throw new MissingMethodException(target.GetType().Name, method);
        var res = m.Invoke(target, args);
        if (res is Task t) await t;
    }

    private static async Task PartyMenu(Rig r) => await Call(r.D, "ManagePartyInDungeon");

    private static void Wait(Rig r, Character ally, string room = "r1") => ally.WaitingAt = new WaitMark(6, room);

    // ---------- 1. the order: a cleared room, an ally that takes orders ----------

    [Fact]
    public async Task Order_InAClearedRoom_MarksTheAlly_WithFloorAndRoom()
    {
        var mira = Ally("h140_mira", "Mira");
        var r = NewRig("W\n1\nQ\n", party: new[] { mira });
        await PartyMenu(r);
        mira.WaitingAt.Should().Be(new WaitMark(6, "r1"), "{0}", r.Text);
        mira.IsWaiting.Should().BeTrue();
        r.D.teammates.Should().Contain(mira, "a waiting ally keeps its party slot");
        r.Text.Should().Contain("  [W] " + Loc.Get("dungeon.wait_option"));
        r.Text.Should().Contain(Loc.Get("dungeon.wait_order", mira.DisplayName));
    }

    [Fact]
    public async Task Order_IsNotOffered_InARoomNotCleared()
    {
        var mira = Ally("h140_mira", "Mira");
        var r = NewRig("W\nQ\n", cleared: false, party: new[] { mira });
        r.D.AlliesWhoMayWait().Should().BeEmpty();
        await PartyMenu(r);
        mira.IsWaiting.Should().BeFalse("the order is only for a cleared room");
        r.Text.Should().NotContain(Loc.Get("dungeon.wait_option"));
    }

    [Fact]
    public void Order_IsOnlyForAnAllyThatTakesOrders_ThePetIncluded()
    {
        var mira = Ally("h140_mira", "Mira");
        var merc = new Character { Name1 = "Merc", Name2 = "Merc", IsMercenary = true, HP = 100, MaxHP = 100, Level = 20 };
        var echo = new Character { Name1 = "Echo", Name2 = "Echo", IsEcho = true, HP = 100, MaxHP = 100, Level = 20 };
        var grouped = new Character { Name1 = "Friend", Name2 = "Friend", HP = 100, MaxHP = 100, Level = 20, AI = CharacterAI.Human };
        grouped.RemoteTerminal = new TerminalEmulator(new MemoryStream(), new MemoryStream());
        var pet = new Character { Name1 = "Wolf", Name2 = "Wolf", IsPet = true, HP = 100, MaxHP = 100, Level = 20 };
        var fallen = Ally("h140_fallen", "Fallen"); fallen.HP = 0;
        var r = NewRig("", party: new Character[] { mira, merc, echo, grouped, pet, fallen });
        r.D.AlliesWhoMayWait().Should().BeEquivalentTo(new[] { mira, pet }, o => o.WithStrictOrdering());
    }

    [Fact]
    public async Task Order_TheMenuListsOnlyTheAlliesWhoMayWait()
    {
        var merc = new Character { Name1 = "Merc", Name2 = "Merc", IsMercenary = true, HP = 100, MaxHP = 100, Level = 20 };
        var mira = Ally("h140_mira", "Mira");
        var r = NewRig("W\n1\nQ\n", party: new Character[] { merc, mira });
        await PartyMenu(r);
        merc.IsWaiting.Should().BeFalse("a mercenary takes no orders, so [1] is Mira");
        mira.IsWaiting.Should().BeTrue("{0}", r.Text);
    }

    // ---------- 2. safe while waiting: no fight, no XP, no potion ----------

    private static Monster Rat()
    {
        var m = PartySurvival133Harness.Mob("Rat", "Goblinoid", hp: 1, str: 1);
        m.Experience = 500;
        return m;
    }

    [Fact]
    public async Task AWaitingAlly_IsNotInTheFight_AndTakesNoXp_AndNoPotion()
    {
        var hero = PartySurvival133Harness.Hero();
        var present = PartySurvival133Harness.Ally("Kade", 100, 100);
        var waiting = PartySurvival133Harness.Ally("Mira", 10, 100);
        waiting.Healing = 5;
        waiting.WaitingAt = new WaitMark(6, "r1");
        long xpBefore = waiting.Experience;
        var output = new MemoryStream();
        var term = new TerminalEmulator(new PartySurvival133Harness.ScriptedStream(string.Concat(Enumerable.Repeat("A\n\n", 30))), output);
        var e = new CombatEngine(term);
        e.SeedRandomForTests(140);
        var party = new List<Character> { present, waiting };
        var task = e.PlayerVsMonsters(hero, new List<Monster> { Rat() }, party, offerMonkEncounter: false);
        var done = await Task.WhenAny(task, Task.Delay(TimeSpan.FromSeconds(60)));
        done.Should().Be(task, "the fight ends");
        var res = await task;
        string text = Encoding.UTF8.GetString(output.ToArray());
        res.Outcome.Should().Be(CombatOutcome.Victory, text);
        res.Teammates.Should().Contain(present).And.NotContain(waiting, "a waiting ally is in no fight");
        party.Should().Contain(waiting, "the caller's party list is not changed");
        waiting.Experience.Should().Be(xpBefore, "no XP share while waiting");
        waiting.Healing.Should().Be(5, "no potion while waiting");
        waiting.HP.Should().Be(10);
        present.Experience.Should().BeGreaterThan(0, "the ally with the party still shares: {0}", text);
    }

    /// <summary>The first Char.Combat.Party frame of a fight (the seed sent with Char.Combat.Start) holds the
    /// fight's list: a MUD client's combat party pane never shows a waiting ally.</summary>
    [Fact]
    public async Task TheFirstGmcpPartyFrame_HoldsNoWaitingAlly()
    {
        var saved = UsurperRemake.Server.SessionContext.Current;
        var gmcp = new MemoryStream();
        var ctx = new UsurperRemake.Server.SessionContext { Username = "h140gmcp", CharacterKey = "h140gmcp", OutputStream = gmcp, GmcpEnabled = true };
        ctx.InitializeSystems();   // the per-session systems a fight reads (grief, companions)
        UsurperRemake.Server.SessionContext.Current = ctx;
        try
        {
            var hero = PartySurvival133Harness.Hero();
            var present = PartySurvival133Harness.Ally("Kadewyn", 100, 100);
            var waiting = PartySurvival133Harness.Ally("Mirabeth", 100, 100);
            waiting.WaitingAt = new WaitMark(6, "r1");
            var output = new MemoryStream();
            var term = new TerminalEmulator(new PartySurvival133Harness.ScriptedStream(string.Concat(Enumerable.Repeat("A\n\n", 30))), output);
            var e = new CombatEngine(term);
            e.SeedRandomForTests(140);
            var task = e.PlayerVsMonsters(hero, new List<Monster> { Rat() }, new List<Character> { present, waiting }, offerMonkEncounter: false);
            (await Task.WhenAny(task, Task.Delay(TimeSpan.FromSeconds(60)))).Should().Be(task, "the fight ends");
            await task;

            string frames = Encoding.UTF8.GetString(gmcp.ToArray());
            int start = frames.IndexOf("Char.Combat.Start", StringComparison.Ordinal);
            int party = frames.IndexOf("Char.Combat.Party", StringComparison.Ordinal);
            start.Should().BeGreaterOrEqualTo(0, "GMCP is on: {0}", frames);
            party.Should().BeGreaterThan(start, "the party seed follows the start frame");
            int next = frames.IndexOf("Char.", party + 5, StringComparison.Ordinal);
            var first = next > party ? frames.Substring(party, next - party) : frames.Substring(party);
            first.Should().Contain("Kadewyn", "the ally with the party is in the pane");
            first.Should().NotContain("Mirabeth", "a waiting ally is not in the fight's pane");
            frames.Should().NotContain("Mirabeth", "no later frame of the fight shows it either");
        }
        finally { UsurperRemake.Server.SessionContext.Current = saved; }
    }

    [Fact]
    public void AWaitingAlly_IsLeftOutOfThePotionMenu_TheShares_AndTheBossCount()
    {
        var mira = Ally("h140_mira", "Mira");
        var kade = Ally("h140_kade", "Kade");
        mira.HP = 30;
        var r = NewRig("", party: new[] { mira, kade });
        Wait(r, mira);
        var members = (List<Character>)typeof(DungeonLocation).GetMethod("GetAllPartyMembers", F)!.Invoke(r.D, null)!;
        members.Should().Contain(kade).And.NotContain(mira, "the potion menu reaches only the allies with you");
        r.D.PresentTeammates().Should().Equal(kade);
        DungeonLocation.LowHealthAllies(r.D.teammates).Should().BeEmpty("the low health warning is for a fight, which a waiting ally is not in");
        OldGodBossSystem.CountLivingFighters(r.Hero, r.D.teammates).Should().Be(2, "the god is scaled for the player and Kade");

        long gold = mira.Gold, xp = mira.Experience, kadeGold = kade.Gold;
        var friend = new Character { Name1 = "Friend", Name2 = "Friend", HP = 100, MaxHP = 100, Level = 20, AI = CharacterAI.Human };
        friend.RemoteTerminal = new TerminalEmulator(new MemoryStream(), new MemoryStream());
        r.D.teammates.Add(friend);
        typeof(DungeonLocation).GetMethod("SplitPartyRewards", F)!.Invoke(r.D, new object[] { 1000L, 900L, "dungeon.bbs_treasure", false });
        mira.Gold.Should().Be(gold, "no share of the party's gold while waiting");
        mira.Experience.Should().Be(xp, "no share of the party's XP while waiting");
        (kade.Gold - kadeGold).Should().Be(300, "the 900 gold splits over the player, Kade and the friend");
    }

    // ---------- 3. walking in and the call back ----------

    [Fact]
    public async Task WalkingIntoItsRoom_BringsItBack_WithALine()
    {
        var mira = Ally("h140_mira", "Mira");
        var r = NewRig("", party: new[] { mira });
        Wait(r, mira, "r2");
        await Call(r.D, "MoveToRoom", "r2");
        mira.IsWaiting.Should().BeFalse("{0}", r.Text);
        r.Text.Should().Contain(Loc.Get("dungeon.wait_rejoins", mira.DisplayName));
    }

    [Fact]
    public async Task WalkingIntoAnotherRoom_LeavesItWaiting()
    {
        var mira = Ally("h140_mira", "Mira");
        var r = NewRig("", party: new[] { mira });
        r.Floor.CurrentRoomId = "r2";
        Wait(r, mira, "r1");
        await Call(r.D, "MoveToRoom", "r2");
        mira.IsWaiting.Should().BeTrue();
        r.D.PresentTeammates().Should().BeEmpty();
    }

    [Fact]
    public async Task TheCallBack_BringsItBack_WithALine()
    {
        var mira = Ally("h140_mira", "Mira");
        var r = NewRig("C\nQ\n", party: new[] { mira });
        r.Floor.CurrentRoomId = "r2";
        Wait(r, mira, "r1");
        await PartyMenu(r);
        mira.IsWaiting.Should().BeFalse("{0}", r.Text);
        r.Text.Should().Contain("  [C] " + Loc.Get("dungeon.wait_call_option", mira.DisplayName));
        r.Text.Should().Contain(Loc.Get("dungeon.wait_called_back", mira.DisplayName));
        r.Text.Should().Contain(Loc.Get("dungeon.wait_tag"), "the party rows tag a waiting ally");
    }

    [Fact]
    public async Task TheCallBack_WithTwoWaiting_AsksWhich()
    {
        var mira = Ally("h140_mira", "Mira");
        var kade = Ally("h140_kade", "Kade");
        var r = NewRig("C\n2\nQ\n", party: new[] { mira, kade });
        r.Floor.CurrentRoomId = "r2";
        Wait(r, mira, "r1"); Wait(r, kade, "r1");
        await PartyMenu(r);
        r.Text.Should().Contain("  [C] " + Loc.Get("dungeon.wait_call_any"));
        kade.IsWaiting.Should().BeFalse();
        mira.IsWaiting.Should().BeTrue();
    }

    // ---------- 4. the ends: the stairs, leaving the dungeon, a save and load ----------

    [Fact]
    public async Task TheStairs_EndTheWait_WithTheCatchUpLine()
    {
        var mira = Ally("h140_mira", "Mira");
        var r = NewRig("", party: new[] { mira });
        r.Floor.Rooms.Add(Room("r3", cleared: false));   // not every room is cleared: no floor bonus on the way down
        Wait(r, mira, "r1");
        await Call(r.D, "DescendStairs");
        ((int)typeof(DungeonLocation).GetField("currentDungeonLevel", F)!.GetValue(r.D)!).Should().Be(7, "{0}", r.Text);
        mira.IsWaiting.Should().BeFalse("{0}", r.Text);
        r.Text.Should().Contain(Loc.Get("dungeon.wait_catches_up", mira.DisplayName));
        r.D.teammates.Should().Contain(mira);
    }

    [Fact]
    public void AnyOtherFloorChange_EndsTheWait_AtTheNextScreen()
    {
        var mira = Ally("h140_mira", "Mira");
        var r = NewRig("", party: new[] { mira });
        Wait(r, mira, "r1");
        typeof(DungeonLocation).GetField("currentDungeonLevel", F)!.SetValue(r.D, 9);
        r.D.SweepWaitMarks();
        mira.IsWaiting.Should().BeFalse();
        r.Text.Should().Contain(Loc.Get("dungeon.wait_catches_up", mira.DisplayName));

        var kade = Ally("h140_kade", "Kade");
        r.D.teammates.Add(kade);
        kade.WaitingAt = new WaitMark(9, "r1");
        r.D.SweepWaitMarks();
        kade.IsWaiting.Should().BeTrue("a wait on this floor stays");
    }

    [Fact]
    public void LeavingTheDungeon_EndsEveryWait_AndTheExitRunsIt()
    {
        var mira = Ally("h140_mira", "Mira");
        var kade = Ally("h140_kade", "Kade");
        var r = NewRig("", party: new[] { mira, kade });
        Wait(r, mira, "r1"); Wait(r, kade, "r2");
        r.D.EndVisitWaits();
        mira.IsWaiting.Should().BeFalse();
        kade.IsWaiting.Should().BeFalse();
        r.Text.Should().Contain(Loc.Get("dungeon.wait_catches_up", mira.DisplayName)).And.Contain(Loc.Get("dungeon.wait_catches_up", kade.DisplayName));

        // every exit (town, death, teleport, rescue, log out, disconnect) leaves EnterLocation through its finally
        var src = File.ReadAllText(Path.Combine(HardcodedTextScannerTests.RepoRoot(), "Scripts", "Locations", "DungeonLocation.cs"));
        int enter = src.IndexOf("public override async Task EnterLocation(Character player, TerminalEmulator term)", StringComparison.Ordinal);
        int fin = src.IndexOf("        finally\n        {", enter, StringComparison.Ordinal);
        int release = src.IndexOf("// Release NPC teammates when leaving the dungeon.", fin, StringComparison.Ordinal);
        enter.Should().BeGreaterThan(0); fin.Should().BeGreaterThan(enter); release.Should().BeGreaterThan(fin);
        src.Substring(fin, release - fin).Should().Contain("EndVisitWaits();", "the exit's finally ends every wait");
    }

    [Fact]
    public void EnteringTheDungeon_ClearsAnOldMark_AndTheFinallyIsBeforeTheLoopEnds()
    {
        var src = File.ReadAllText(Path.Combine(HardcodedTextScannerTests.RepoRoot(), "Scripts", "Locations", "DungeonLocation.cs"));
        int clear = src.IndexOf("        teammates.Clear();\n", StringComparison.Ordinal);
        src.Substring(0, clear).TrimEnd().Should().EndWith("ForgetWaitMarks();   // 1.4.0 H: a wait never outlives the visit");
        int dedupe = src.IndexOf("        DeduplicateTeammates();\n", clear, StringComparison.Ordinal);
        src.Substring(dedupe, 200).Should().Contain("ForgetWaitMarks();", "a party member brought in from an earlier session has no mark");
    }

    [Fact]
    public async Task ASaveAndLoad_HasNoWait_TheMarkIsNeverSaved()
    {
        var json = new JsonSerializerOptions { PropertyNamingPolicy = JsonNamingPolicy.CamelCase, IncludeFields = true };
        var keep = NPCSpawnSystem.Instance.ActiveNPCs.ToList();
        try
        {
            NPCSpawnSystem.Instance.ActiveNPCs.Clear();
            var mira = Ally("h140_save_mira", "H140Mira");
            NPCSpawnSystem.Instance.AddRestoredNPC(mira);
            var live = NPCSpawnSystem.Instance.ActiveNPCs.Single(n => n.ID == "h140_save_mira");
            live.WaitingAt = new WaitMark(6, "r1");

            var stored = JsonSerializer.Serialize(OnlineStateManager.SerializeCurrentNPCs(), json);
            stored.Should().NotContain("waitingAt").And.NotContain("WaitingAt");
            NPCSpawnSystem.Instance.ActiveNPCs.Clear();
            await GameEngine.Instance.RestoreNPCs(JsonSerializer.Deserialize<List<NPCData>>(stored, json)!);
            var loaded = NPCSpawnSystem.Instance.ActiveNPCs.Single(n => n.ID == "h140_save_mira");
            loaded.IsWaiting.Should().BeFalse("a load has no wait");

            JsonSerializer.Serialize<Character>(live, json).Should().NotContain("waitingAt", "the Character itself never writes it");
            var structures = File.ReadAllText(Path.Combine(HardcodedTextScannerTests.RepoRoot(), "Scripts", "Systems", "SaveDataStructures.cs"));
            structures.Should().NotContain("WaitMark").And.NotContain("WaitingAt");
        }
        finally
        {
            NPCSpawnSystem.Instance.ActiveNPCs.Clear();
            foreach (var n in keep) NPCSpawnSystem.Instance.ActiveNPCs.Add(n);
        }
    }

    // ---------- 5. the cap of 4, the saved id list ----------

    [Fact]
    public async Task TheCapOfFour_CountsAWaitingAlly_AndTheSavedIdsHoldIt()
    {
        var keepIds = GameEngine.Instance.DungeonPartyNPCIds.ToList();
        var keepNpcs = NPCSpawnSystem.Instance.ActiveNPCs.ToList();
        try
        {
            var party = Enumerable.Range(1, 4).Select(i => Ally($"h140_cap{i}", $"Cap{i}")).ToList();
            var fifth = Ally("h140_cap5", "Cap5");
            NPCSpawnSystem.Instance.ActiveNPCs.Clear();
            foreach (var n in party.Append(fifth)) NPCSpawnSystem.Instance.ActiveNPCs.Add(n);
            var r = NewRig("", party: party);
            Wait(r, party[0], "r1");

            typeof(DungeonLocation).GetMethod("SyncNPCTeammatesToGameEngine", F)!.Invoke(r.D, null);
            GameEngine.Instance.DungeonPartyNPCIds.Should().Contain("h140_cap1", "the save's id list still holds a waiting ally");
            party[0].IsWaiting.Should().BeTrue("a sync keeps the mark of an ally still in the party");
            ((NPC)party[0]).IsInConversation.Should().BeTrue("the world sim protection still covers it");

            GameEngine.Instance.SetDungeonPartyNPCs(new[] { "h140_cap5" });
            await Call(r.D, "RestoreNPCTeammates", r.Term);
            r.D.teammates.Should().HaveCount(4).And.NotContain(fifth, "the waiting ally holds one of the four slots");
        }
        finally
        {
            GameEngine.Instance.SetDungeonPartyNPCs(keepIds);
            NPCSpawnSystem.Instance.ActiveNPCs.Clear();
            foreach (var n in keepNpcs) NPCSpawnSystem.Instance.ActiveNPCs.Add(n);
        }
    }

    // ---------- 6. dismissing a waiting ally drops its mark ----------

    [Fact]
    public async Task Dismissing_AWaitingAlly_DropsItsMark()
    {
        var keepIds = GameEngine.Instance.DungeonPartyNPCIds.ToList();
        try
        {
            var mira = Ally("h140_mira", "Mira");
            var kade = Ally("h140_kade", "Kade");
            // through the menu: the order, then the dismissal (party number 2 is Mira)
            var r2 = NewRig("W\n1\nQ\n", party: new[] { mira, kade });
            await PartyMenu(r2);
            mira.IsWaiting.Should().BeTrue();
            typeof(BaseLocation).GetField("terminal", F)!.SetValue(r2.D, new TerminalEmulator(new MemoryStream(Encoding.UTF8.GetBytes("2\n")), new MemoryStream()));
            await Call(r2.D, "RemoveTeammateFromParty");
            r2.D.teammates.Should().NotContain(mira);
            mira.IsWaiting.Should().BeFalse("an ally who leaves the party while waiting loses its mark");
        }
        finally { GameEngine.Instance.SetDungeonPartyNPCs(keepIds); }
    }

    [Fact]
    public void AnAllyGoneFromTheList_LosesItsMark_AtTheNextScreen()
    {
        var mira = Ally("h140_mira", "Mira");
        var r = NewRig("W\n1\nQ\n", party: new[] { mira });
        PartyMenu(r).GetAwaiter().GetResult();
        mira.IsWaiting.Should().BeTrue();
        r.D.teammates.Remove(mira);   // any path that takes it out of the list (the group cap, a dedupe)
        r.D.SweepWaitMarks();
        mira.IsWaiting.Should().BeFalse();
        r.Text.Should().NotContain(Loc.Get("dungeon.wait_catches_up", mira.DisplayName), "it left; it does not catch up");
    }

    // ---------- 7. a follower's party view ----------

    [Fact]
    public void AFollowersPartyView_TagsAWaitingAlly()
    {
        var mira = Ally("h140_mira", "Mira");
        var kade = Ally("h140_kade", "Kade");
        mira.WaitingAt = new WaitMark(6, "r1");
        var output = new MemoryStream();
        var term = new TerminalEmulator(new MemoryStream(), output);
        var leader = Hero();
        var follower = new Character { Name1 = "Friend", Name2 = "Friend", HP = 100, MaxHP = 100, Level = 20, AI = CharacterAI.Human };
        typeof(DungeonLocation).GetMethod("RenderPartyStatus", FS)!.Invoke(null, new object?[] { term, leader, new List<Character> { mira, kade }, follower });
        term.StreamWriterInternal?.Flush();
        var rows = Rows(Strip(Encoding.UTF8.GetString(output.ToArray()))).ToList();
        rows.Single(x => x.Contains("Mira")).Should().Contain(Loc.Get("dungeon.wait_tag"));
        rows.Single(x => x.Contains("Kade")).Should().NotContain(Loc.Get("dungeon.wait_tag"));
    }

    // ---------- 8. the texts: five languages, 79 columns in en and hu ----------

    [Fact]
    public void TheTexts_AreInFiveLanguages_AndTranslated()
    {
        foreach (var key in Keys)
            foreach (var lang in Langs)
            {
                var v = Loc.GetIn(lang, key, "Mira");
                v.Should().NotBeNullOrEmpty().And.NotBe(key, $"{lang} {key}");
                if (lang != "en") v.Should().NotBe(Loc.GetIn("en", key, "Mira"), $"{lang} {key} is translated");
            }
    }

    /// <summary>The party rows that carry the waiting tag: the party management list, the remove list and a
    /// follower's party view, with a 16 letter name, a caster and five digit HP and mana.</summary>
    [Theory]
    [InlineData("en")]
    [InlineData("hu")]
    public async Task TheTaggedPartyRows_Fit79Columns(string lang)
    {
        var prev = GameConfig.Language;
        try
        {
            GameConfig.Language = lang;
            string name = new string('W', 16);
            var ally = Ally("h140_tagged", name, level: 100);
            ally.Class = CharacterClass.Magician;
            ally.HP = 12345; ally.MaxHP = 12345; ally.MaxMana = 12345; ally.Mana = 12345;
            ally.WaitingAt = new WaitMark(6, "r1");
            string tag = Loc.Get("dungeon.wait_tag");

            var r = NewRig("B\n", party: new[] { ally });
            await Call(r.D, "ManageTeam");
            typeof(BaseLocation).GetField("terminal", F)!.SetValue(r.D, r.Term = new TerminalEmulator(new MemoryStream(Encoding.UTF8.GetBytes("\n")), r.Output));
            await Call(r.D, "RemoveTeammateFromParty");
            var dungeonRows = Rows(r.Text).Where(x => x.Contains(name)).ToList();
            dungeonRows.Should().HaveCount(2, "the party management row and the remove row: {0}", r.Text);

            var output = new MemoryStream();
            var term = new TerminalEmulator(new MemoryStream(), output);
            typeof(DungeonLocation).GetMethod("RenderPartyStatus", FS)!.Invoke(null, new object?[] { term, Hero(), new List<Character> { ally }, null });
            term.StreamWriterInternal?.Flush();
            var followerRows = Rows(Strip(Encoding.UTF8.GetString(output.ToArray()))).Where(x => x.Contains(name)).ToList();
            followerRows.Should().HaveCount(1);

            foreach (var row in dungeonRows.Concat(followerRows))
            {
                row.Should().Contain(tag);
                row.Length.Should().BeLessOrEqualTo(MaxWidth, $"{lang}: \"{row}\"");
            }
        }
        finally { GameConfig.Language = prev; }
    }

    [Theory]
    [InlineData("en")]
    [InlineData("hu")]
    public async Task TheRows_Fit79Columns(string lang)
    {
        var prev = GameConfig.Language;
        try
        {
            GameConfig.Language = lang;
            string longName = new string('M', 24);
            var mira = Ally("h140_mira", longName);
            mira.HP = 99999; mira.MaxHP = 99999;
            var kade = Ally("h140_kade", new string('K', 24));
            var r = NewRig("W\n1\nC\nQ\n", party: new[] { mira, kade });
            await PartyMenu(r);
            Wait(r, kade, "r2");
            await Call(r.D, "MoveToRoom", "r2");
            var mine = new[]
            {
                Loc.Get("dungeon.wait_option"), Loc.Get("dungeon.wait_pick"), Loc.Get("dungeon.wait_order", longName),
                Loc.Get("dungeon.wait_call_option", longName), Loc.Get("dungeon.wait_call_any"), Loc.Get("dungeon.wait_called_back", longName),
                Loc.Get("dungeon.wait_tag"), Loc.Get("dungeon.wait_rejoins", kade.DisplayName),
            };
            var text = r.Text;
            text.Should().Contain(Loc.Get("dungeon.wait_order", longName)).And.Contain(Loc.Get("dungeon.wait_called_back", longName));
            // the rows this piece adds or changes: its texts, and a party row that carries the waiting tag
            // a prompt row ends where the player types; the next line starts on its own row on a real terminal
            string prompt = Loc.Get("ui.choice");
            foreach (var row in Rows(text).Select(x => x.StartsWith(prompt) ? x.Substring(prompt.Length) : x).Where(x => mine.Any(m => x.Contains(m))))
                row.Length.Should().BeLessOrEqualTo(MaxWidth, $"{lang}: \"{row}\"");
            foreach (var catchUp in new[] { Loc.Get("dungeon.wait_catches_up", longName), Loc.Get("dungeon.wait_call_pick") })
                catchUp.Length.Should().BeLessOrEqualTo(MaxWidth - 2, $"{lang}: \"{catchUp}\"");
        }
        finally { GameConfig.Language = prev; }
    }
}
