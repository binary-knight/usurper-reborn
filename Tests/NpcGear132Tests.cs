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
/// 1.3.2: NPC party members keep their gear. The world simulator leaves a party member, an NPC in conversation and
/// the player's spouse or lover out of its activity pass; NPCs in the dungeon party are not attacked by other NPCs;
/// NPC shopping buys only what the class can use and keeps the replaced item; a dead saved ally gets a notice at
/// dungeon entry. All in memory.
/// </summary>
[Collection("SharedGameSingletons")]
public class NpcGear132Tests
{
    private const BindingFlags F = BindingFlags.NonPublic | BindingFlags.Instance;

    private sealed class ZeroRandom : Random
    {
        public override int Next() => 0;
        public override int Next(int maxValue) => 0;
        public override int Next(int minValue, int maxValue) => minValue;
        public override double NextDouble() => 0;
    }

    private static NPC Npc(string id, CharacterClass cls = CharacterClass.Warrior, int level = 20, long gold = 5000, string team = "")
    {
        var npc = new NPC
        {
            ID = id, Name1 = "Gear " + id, Name2 = "Gear " + id, Class = cls, Race = CharacterRace.Human,
            Level = level, HP = 500, MaxHP = 500, BaseMaxHP = 500, Strength = 300, BaseStrength = 300,
            Gold = gold, Team = team, CurrentLocation = "Main Street",
        };
        var profile = PersonalityProfile.GenerateForArchetype("commoner");
        profile.Impulsiveness = 0;
        npc.Personality = profile;
        npc.Brain = new NPCBrain(npc, profile);
        return npc;
    }

    private static WorldSimulator Sim(Random? rng = null)
    {
        var sim = new WorldSimulator();
        typeof(WorldSimulator).GetField("random", F)!.SetValue(sim, rng ?? new ZeroRandom());
        return sim;
    }

    private static int Dispatched(NPC npc) =>
        ((Dictionary<string, DateTime>)typeof(NPCBrain).GetField("lastActivities", F)!.GetValue(npc.Brain)!).Count;

    /// <summary>One activity pass of the live (Brain v2) cohort; true when the NPC ran a verb.</summary>
    private static bool ActivityPass(NPC npc, params NPC[] world)
    {
        int before = Dispatched(npc);
        var all = world.Append(npc).Distinct().ToList();
        typeof(WorldSimulator).GetMethod("BrainV2ProcessActivities", F)!.Invoke(Sim(), new object[] { npc, new WorldState(all) });
        return Dispatched(npc) > before;
    }

    private static void WithPlayerState(Action body)
    {
        var engine = GameEngine.Instance;
        var savedParty = engine.DungeonPartyNPCIds.ToList();
        var romance = RomanceTracker.Instance;
        var savedSpouses = romance.Spouses.ToList();
        var savedLovers = romance.CurrentLovers.ToList();
        try
        {
            engine.SetDungeonPartyNPCs(Array.Empty<string>());
            romance.Spouses.Clear();
            romance.CurrentLovers.Clear();
            body();
        }
        finally
        {
            engine.SetDungeonPartyNPCs(savedParty);
            romance.Spouses.Clear(); romance.Spouses.AddRange(savedSpouses);
            romance.CurrentLovers.Clear(); romance.CurrentLovers.AddRange(savedLovers);
        }
    }

    private static void AssertUntouched(NPC npc, bool ran, long gold)
    {
        ran.Should().BeFalse($"{npc.ID} ran a world sim verb");
        npc.CurrentLocation.Should().Be("Main Street");
        npc.Gold.Should().Be(gold);
        npc.EquippedItems.Should().BeEmpty();
    }

    // ---------- 1. the activity pass skips a party member, an NPC in conversation, a spouse and a lover ----------

    // NG-C: the spouse and lover are in the activity pass (they go home); only their gear is held

    private static NPC Partner(string kind)
    {
        var npc = Npc("ng132_" + kind);
        EquipmentDatabase.Initialize();
        npc.EquippedItems[EquipmentSlot.MainHand] = Weakest(w => w.WeaponType == WeaponType.Sword && w.CanEquip(npc, out _)).Id;
        if (kind == "spouse") RomanceTracker.Instance.Spouses.Add(new Spouse { NPCId = npc.ID, NPCName = npc.Name2 });
        else RomanceTracker.Instance.CurrentLovers.Add(new Lover { NPCId = npc.ID, NPCName = npc.Name2 });
        return npc;
    }

    private static string GearOf(NPC npc) => string.Join(",", npc.EquippedItems.OrderBy(k => k.Key).Select(k => $"{k.Key}={k.Value}"));

    [Theory]
    [InlineData("spouse")]
    [InlineData("lover")]
    public void ActivityPass_TheSpouseAndALover_RunTheirVerbs_AndKeepTheirGear(string kind) => WithPlayerState(() =>
    {
        var partner = Partner(kind);
        string gear = GearOf(partner);
        ActivityPass(partner).Should().BeTrue($"the {kind} is in the activity pass");
        GearOf(partner).Should().Be(gear);
    });

    [Theory]
    [InlineData("spouse")]
    [InlineData("lover")]
    public void GoHome_TheSpouseAndALover_GoHome(string kind) => WithPlayerState(() =>
    {
        var partner = Partner(kind);
        Sim().DispatchVerb(partner, "go_home");
        partner.CurrentLocation.Should().Be("Home");
    });

    [Theory]
    [InlineData("spouse", "shop")]
    [InlineData("lover", "shop")]
    [InlineData("spouse", "marketplace")]
    [InlineData("lover", "marketplace")]
    public void GearVerbs_TheSpouseAndALover_AreNotMovedToAShop_AndKeepTheirGear(string kind, string verb) => WithPlayerState(() =>
    {
        var partner = Partner(kind);
        partner.Gold = 10_000_000;
        string gear = GearOf(partner);
        Sim().DispatchVerb(partner, verb);
        partner.CurrentLocation.Should().Be("Main Street");
        GearOf(partner).Should().Be(gear);
        partner.Gold.Should().Be(10_000_000);
    });

    [Fact]
    public void ActivityPass_SkipsAnNpcInTheDungeonParty_ThoughNotOnATeam() => WithPlayerState(() =>
    {
        var ally = Npc("ng132_party");
        GameEngine.Instance.SetDungeonPartyNPCs(new[] { ally.ID });
        AssertUntouched(ally, ActivityPass(ally), 5000);
    });

    [Fact]
    public void ActivityPass_SkipsAnNpcInConversation() => WithPlayerState(() =>
    {
        var engaged = Npc("ng132_engaged");
        engaged.IsInConversation = true;
        AssertUntouched(engaged, ActivityPass(engaged), 5000);
    });

    [Fact]
    public void ActivityPass_AFreeNpc_IsNotSkipped() => WithPlayerState(() =>
    {
        var free = Npc("ng132_free");
        // the same NPC is skipped once it is in the party: the free run below is not a quiet pass
        ActivityPass(free).Should().BeTrue();
    });

    // ---------- 1 (NG-A). no NPC attacks a party member; MarkNPCDead heals a party member ----------

    private static void Attack(NPC attacker, NPC target) =>
        typeof(WorldSimulator).GetMethod("ExecuteAttack", F)!.Invoke(Sim(new Random(132)),
            new object[] { attacker, target.Id, new WorldState(new List<NPC> { attacker, target }) });

    private static (NPC attacker, NPC target) Brawlers(string targetId)
    {
        var attacker = Npc("ng132_brawler", level: 10);
        attacker.CurrentLocation = "Inn";
        var target = Npc(targetId, level: 10, gold: 4000);
        target.CurrentLocation = "Inn";
        target.HP = target.MaxHP = 100_000;   // the brawl runs its rounds and the target lives
        return (attacker, target);
    }

    [Fact]
    public void Attack_ADungeonPartyNpcInTown_IsNotAttacked_AndKeepsItsGold() => WithPlayerState(() =>
    {
        var (attacker, target) = Brawlers("ng132_party_target");
        GameEngine.Instance.SetDungeonPartyNPCs(new[] { target.ID });
        Attack(attacker, target);
        target.HP.Should().Be(100_000);
        target.Gold.Should().Be(4000);
        target.Enemies.Should().BeEmpty();
    });

    [Fact]
    public void Attack_ADungeonPartyNpc_StartsNoBrawl() => WithPlayerState(() =>
    {
        var (attacker, target) = Brawlers("ng132_free_target");
        GameEngine.Instance.SetDungeonPartyNPCs(new[] { attacker.ID });
        Attack(attacker, target);
        target.HP.Should().Be(100_000);
        attacker.Enemies.Should().BeEmpty();
    });

    [Fact]
    public void Attack_ASpouseOutsideTheParty_CanStillBeAttacked() => WithPlayerState(() =>
    {
        var (attacker, target) = Brawlers("ng132_spouse_target");
        RomanceTracker.Instance.Spouses.Add(new Spouse { NPCId = target.ID, NPCName = target.Name2 });
        Attack(attacker, target);
        target.HP.Should().BeLessThan(100_000);
    });

    [Fact]
    public void Attack_AFreeNpc_CanBeAttacked() => WithPlayerState(() =>
    {
        var (attacker, target) = Brawlers("ng132_plain_target");
        Attack(attacker, target);
        target.HP.Should().BeLessThan(100_000);
        target.Enemies.Should().Contain(attacker.Id);
    });

    [Fact]
    public void MarkNPCDead_ADungeonPartyNpc_IsHealedInstead() => WithPlayerState(() =>
    {
        var ally = Npc("ng132_party_dead", level: 1);
        ally.HP = 0;
        GameEngine.Instance.SetDungeonPartyNPCs(new[] { ally.ID });
        Sim(new Random(132)).MarkNPCDead(ally, 0f, "A Killer", "Inn").Should().BeFalse();
        ally.IsDead.Should().BeFalse();
        ally.HP.Should().Be(125);
    });

    [Fact]
    public void MarkNPCDead_AtThePlayersSide_ADungeonPartyNpcStillDies() => WithPlayerState(() =>
    {
        // CombatEngine.HandleNpcTeammateDeath calls with the ID still in the party list: a real death stays one
        var ally = Npc("ng132_party_falls", level: 1);
        ally.HP = 0;
        GameEngine.Instance.SetDungeonPartyNPCs(new[] { ally.ID });
        Sim(new Random(132)).MarkNPCDead(ally, 0f, "A Monster", "the Dungeon", atPlayersSide: true);
        ally.IsDead.Should().BeTrue();
    });

    // ---------- 2. NPC shopping: the class rule, an unresolved slot, the replaced item ----------

    private static void Shop(NPC npc) =>
        typeof(WorldSimulator).GetMethod("NPCGoShopping", F)!.Invoke(Sim(), new object[] { npc });

    private static Equipment Weakest(Func<Equipment, bool> pick) =>
        EquipmentDatabase.GetWeaponsByHandedness(WeaponHandedness.OneHanded)
            .Concat(EquipmentDatabase.GetWeaponsByHandedness(WeaponHandedness.TwoHanded))
            .Where(pick).Where(w => w.WeaponPower > 0).OrderBy(w => w.WeaponPower).First();

    private static Equipment? MainHand(NPC npc) =>
        npc.EquippedItems.TryGetValue(EquipmentSlot.MainHand, out int id) ? EquipmentDatabase.GetById(id) : null;

    [Fact]
    public void Shopping_ARangerWithABow_BuysABetterBow_AndKeepsTheOldOneForSale()
    {
        EquipmentDatabase.Initialize();
        var ranger = Npc("ng132_ranger", CharacterClass.Ranger, level: 60, gold: 10_000_000);
        var oldBow = Weakest(w => w.WeaponType == WeaponType.Bow);
        ranger.EquippedItems[EquipmentSlot.MainHand] = oldBow.Id;
        Shop(ranger);
        var now = MainHand(ranger)!;
        now.WeaponType.Should().Be(WeaponType.Bow, $"a Ranger bought {now.Name}");
        now.WeaponPower.Should().BeGreaterThan(oldBow.WeaponPower);
        now.CanEquip(ranger, out _).Should().BeTrue();
        ranger.MarketInventory.Should().Contain(i => i.Name == oldBow.Name && i.Type == ObjType.Weapon);
    }

    [Fact]
    public void Shopping_ARangerOnASmallBudget_NeverBuysTheSmithingHammer()
    {
        EquipmentDatabase.Initialize();
        // 30 percent of 220 is 66: the Smithing Hammer (60 gold, power 5) is the best weapon of any class in reach
        var ranger = Npc("ng132_ranger_poor", CharacterClass.Ranger, level: 5, gold: 220);
        Shop(ranger);
        var now = MainHand(ranger);
        if (now != null)
        {
            now.Name.Should().NotBe("Smithing Hammer");
            now.WeaponType.Should().Be(WeaponType.Bow);
        }
    }

    [Fact]
    public void Shopping_AMainHandIdThatDoesNotResolve_IsLeftInPlace()
    {
        EquipmentDatabase.Initialize();
        var ranger = Npc("ng132_ranger_lost", CharacterClass.Ranger, level: 60, gold: 10_000_000);
        EquipmentDatabase.GetById(20000).Should().BeNull();   // between the built-ins and the shop range
        ranger.EquippedItems[EquipmentSlot.MainHand] = 20000;
        Shop(ranger);
        ranger.EquippedItems[EquipmentSlot.MainHand].Should().Be(20000);
    }

    [Fact]
    public void Shopping_AMagician_NeverWearsArmorItCannotEquip()
    {
        EquipmentDatabase.Initialize();
        // the main hand does not resolve, so the visit goes on to the armor shop (body slot with this random)
        var mage = Npc("ng132_mage", CharacterClass.Magician, level: 60, gold: 10_000_000);
        mage.EquippedItems[EquipmentSlot.MainHand] = 20000;
        Shop(mage);
        mage.EquippedItems.TryGetValue(EquipmentSlot.Body, out int bodyId).Should().BeTrue("the visit buys body armor");
        var body = EquipmentDatabase.GetById(bodyId)!;
        body.CanEquip(mage, out var why).Should().BeTrue($"{body.Name}: {why}");
    }

    [Fact]
    public void Shopping_AWarrior_StillUpgrades()
    {
        EquipmentDatabase.Initialize();
        var warrior = Npc("ng132_warrior", CharacterClass.Warrior, level: 30, gold: 1_000_000);
        var old = Weakest(w => w.WeaponType == WeaponType.Sword && w.CanEquip(warrior, out _));
        warrior.EquippedItems[EquipmentSlot.MainHand] = old.Id;
        Shop(warrior);
        MainHand(warrior)!.WeaponPower.Should().BeGreaterThan(old.WeaponPower);
    }

    /// <summary>A loot bow with a poison effect, as a dungeon drop is registered.</summary>
    internal static Equipment LootBow(int power, long value) => new Equipment
    {
        Name = "Venomous Longbow", Slot = EquipmentSlot.MainHand, Handedness = WeaponHandedness.TwoHanded,
        WeaponType = WeaponType.Bow, WeaponPower = power, Value = value, Rarity = EquipmentRarity.Rare, PoisonDamage = 12,
    };

    [Fact]
    public void Shopping_ALootBowOfSlightlyLowerPower_IsKeptAgainstACheaperShopBow()
    {
        EquipmentDatabase.Initialize();
        var ranger = Npc("ng132_ranger_loot", CharacterClass.Ranger, level: 60, gold: 10_000_000);
        long budget = ranger.Gold * 3 / 10;
        var reachable = EquipmentDatabase.GetWeaponsByHandedness(WeaponHandedness.OneHanded)
            .Concat(EquipmentDatabase.GetWeaponsByHandedness(WeaponHandedness.TwoHanded))
            .Where(w => w.WeaponType == WeaponType.Bow && w.Value <= budget && w.CanEquip(ranger, out _)).ToList();
        int top = reachable.Max(w => w.WeaponPower);
        long dearest = reachable.Max(w => w.Value);
        // one below the best bow in reach, worth more than any of them (a Rare drop's value carries its effects)
        var loot = LootBow(top - 1, dearest + 1);
        int lootId = EquipmentDatabase.RegisterDynamic(loot);
        ranger.EquippedItems[EquipmentSlot.MainHand] = lootId;
        Shop(ranger);
        ranger.EquippedItems[EquipmentSlot.MainHand].Should().Be(lootId);
    }

    [Fact]
    public void CatchUp_TheTeamThePartyAndTheSpouse_KeepTheirGear_AndAFreeNpcStillUpgrades() =>
        IsolatedGame.Run(nameof(RecruitPoolRuns.NgCatchUpGear), "132").Should().Be("team=kept party=kept spouse=kept lover=kept free=upgraded");

    // ---------- NG-B 1. the hard check inside the gear changes, whatever path reaches them ----------

    private static (NPC npc, string gear, long weap, long arm) Held(string kind)
    {
        EquipmentDatabase.Initialize();
        var npc = Npc("ng132_held_" + kind, CharacterClass.Warrior, level: 30, gold: 1_000_000);
        npc.Experience = 1L << 40;   // enough for a level at the Level Master
        npc.EquippedItems[EquipmentSlot.MainHand] = Weakest(w => w.WeaponType == WeaponType.Sword && w.CanEquip(npc, out _)).Id;
        npc.RecalculateStats();
        switch (kind)
        {
            case "team": npc.Team = "Gear Hard Check"; WorldSimulator.RegisterPlayerTeam(npc.Team); break;
            case "party": GameEngine.Instance.SetDungeonPartyNPCs(new[] { npc.ID }); break;
            case "engaged": npc.IsInConversation = true; break;
            case "spouse": RomanceTracker.Instance.Spouses.Add(new Spouse { NPCId = npc.ID, NPCName = npc.Name2 }); break;
            case "lover": RomanceTracker.Instance.CurrentLovers.Add(new Lover { NPCId = npc.ID, NPCName = npc.Name2 }); break;
        }
        string gear = string.Join(",", npc.EquippedItems.OrderBy(k => k.Key).Select(k => $"{k.Key}={k.Value}"));
        return (npc, gear, npc.WeapPow, npc.ArmPow);
    }

    [Theory]
    [InlineData("team")]
    [InlineData("party")]
    [InlineData("engaged")]
    [InlineData("spouse")]
    [InlineData("lover")]
    public void HardCheck_ShoppingAndTheLevelMaster_LeaveAHeldNpcsGearAlone(string kind) => WithPlayerState(() =>
    {
        var (npc, gear, weap, arm) = Held(kind);
        try
        {
            Shop(npc);
            typeof(WorldSimulator).GetMethod("NPCVisitMaster", F)!.Invoke(Sim(), new object[] { npc });
            string.Join(",", npc.EquippedItems.OrderBy(k => k.Key).Select(k => $"{k.Key}={k.Value}")).Should().Be(gear);
            npc.Gold.Should().Be(1_000_000);
            npc.WeapPow.Should().Be(weap);
            npc.ArmPow.Should().Be(arm);
        }
        finally { WorldSimulator.UnregisterPlayerTeam("Gear Hard Check"); }
    });

    [Fact]
    public void HardCheck_AFreeNpc_StillShopsAndGainsGearPowerAtTheLevelMaster() => WithPlayerState(() =>
    {
        var (npc, gear, weap, _) = Held("free");
        Shop(npc);
        string.Join(",", npc.EquippedItems.OrderBy(k => k.Key).Select(k => $"{k.Key}={k.Value}")).Should().NotBe(gear);
        long afterShop = npc.WeapPow;
        typeof(WorldSimulator).GetMethod("NPCVisitMaster", F)!.Invoke(Sim(), new object[] { npc });
        npc.WeapPow.Should().Be(afterShop + 5);
    });

    // ---------- 3. a street gang join registers the team ----------

    [Fact]
    public async Task StreetGangJoin_RegistersTheTeam_SoItsMembersSkipTheActivityPass()
    {
        const string gang = "Gear Lane Crew";
        var crew = new List<NPC> { Npc("ng132_gang_1", team: gang), Npc("ng132_gang_2", team: gang) };
        foreach (var n in crew) n.TeamPW = "pw";
        var teams = WorldInitializerSystem.Instance.ActiveTeams;
        var savedTeams = teams.ToList();
        teams.Clear();
        teams.Add(new WorldInitializerSystem.TeamRecord { Name = gang, MemberNames = crew.Select(n => n.Name2).ToList() });
        NPCSpawnSystem.Instance.ActiveNPCs.AddRange(crew);
        WorldSimulator.UnregisterPlayerTeam(gang);
        try
        {
            var hero = new Character { Name1 = "Gear Joiner", Name2 = "Gear Joiner", Class = CharacterClass.Warrior, Level = 20, HP = 300, MaxHP = 300 };
            var term = new TerminalEmulator(new LineStream(new[] { "J", "", "", "" }), new MemoryStream());
            await (Task)typeof(StreetEncounterSystem).GetMethod("ProcessGangEncounter", F)!
                .Invoke(StreetEncounterSystem.Instance, new object[] { hero, new EncounterResult(), term })!;
            hero.Team.Should().Be(gang);
            WorldSimulator.IsPlayerTeam(gang).Should().BeTrue();
            WithPlayerState(() => AssertUntouched(crew[0], ActivityPass(crew[0]), 5000));
        }
        finally
        {
            WorldSimulator.UnregisterPlayerTeam(gang);
            foreach (var n in crew) NPCSpawnSystem.Instance.ActiveNPCs.Remove(n);
            teams.Clear(); teams.AddRange(savedTeams);
        }
    }

    // ---------- 4. a dead saved ally at dungeon entry ----------

    private static async Task<string> Entry(string lang, params NPC[] saved)
    {
        var prevLang = GameConfig.Language;
        var engine = GameEngine.Instance;
        var savedParty = engine.DungeonPartyNPCIds.ToList();
        NPCSpawnSystem.Instance.ActiveNPCs.AddRange(saved);
        try
        {
            GameConfig.Language = lang;
            engine.SetDungeonPartyNPCs(saved.Select(n => n.ID));
            var output = new MemoryStream();
            var term = new TerminalEmulator(new MemoryStream(), output);
            var dungeon = new DungeonLocation();
            typeof(BaseLocation).GetField("terminal", F)!.SetValue(dungeon, term);
            await (Task)typeof(DungeonLocation).GetMethod("RestoreNPCTeammates", F)!.Invoke(dungeon, new object[] { term })!;
            term.StreamWriterInternal?.Flush();
            return Regex.Replace(Encoding.UTF8.GetString(output.ToArray()), "\u001b\\[[0-9;?]*[A-Za-z]", "");
        }
        finally
        {
            GameConfig.Language = prevLang;
            engine.SetDungeonPartyNPCs(savedParty);
            foreach (var n in saved) NPCSpawnSystem.Instance.ActiveNPCs.Remove(n);
        }
    }

    private const string LongName = "Bartholomew Thistlewood";

    [Theory]
    [InlineData("en")]
    [InlineData("hu")]
    public async Task DungeonEntry_ADeadSavedAlly_GetsANotice_ThatFits(string lang)
    {
        var dead = Npc("ng132_dead_ally");
        dead.Name2 = LongName;
        dead.HP = 0;
        dead.IsDead = true;
        string shown = await Entry(lang, dead);
        string line = Loc.GetIn(lang, "dungeon.teammate_dead", dead.DisplayName);
        line.Should().NotBe("dungeon.teammate_dead");
        shown.Should().Contain(line);
        foreach (var row in shown.Replace("\r", "").Split('\n'))
            row.Length.Should().BeLessOrEqualTo(79, row);
    }

    [Fact]
    public async Task DungeonEntry_AnArrestedSavedAlly_GetsTodaysLine()
    {
        var jailed = Npc("ng132_jailed_ally");
        jailed.DaysInPrison = 3;
        string shown = await Entry("en", jailed);
        shown.Should().Contain(Loc.GetIn("en", "dungeon.teammate_arrested", jailed.DisplayName));
        shown.Should().NotContain(Loc.GetIn("en", "dungeon.teammate_dead", jailed.DisplayName));
    }
}

/// <summary>1.3.2: the single-player catch-up with a geared team, a party member and a spouse (NpcGear132Tests).</summary>
internal static partial class RecruitPoolRuns
{
    private static Equipment WeakestFor(NPC npc, Func<Equipment, bool> pick) =>
        EquipmentDatabase.GetAll().Where(e => EquipmentDatabase.IsShopGenerated(e.Id) && pick(e) && e.CanEquip(npc, out _))
            .OrderBy(e => e.Slot == EquipmentSlot.MainHand ? e.WeaponPower : e.ArmorClass).ThenBy(e => e.Value).First();

    /// <summary>Weak but real gear in the main hand and on the body, and gold to shop with.</summary>
    private static void GearUp(NPC npc)
    {
        npc.Gold = 2_000_000;
        npc.Strength = npc.BaseStrength = Math.Max(npc.BaseStrength, 200);
        if (!npc.EquippedItems.ContainsKey(EquipmentSlot.MainHand))
            npc.EquippedItems[EquipmentSlot.MainHand] = WeakestFor(npc, e => e.Slot == EquipmentSlot.MainHand && e.WeaponPower > 0 && e.WeaponType != WeaponType.Shield).Id;
        npc.EquippedItems[EquipmentSlot.Body] = WeakestFor(npc, e => e.Slot == EquipmentSlot.Body && e.ArmorClass > 0).Id;
        npc.RecalculateStats();
    }

    private static string Gear(NPC n) => string.Join(",", n.EquippedItems.OrderBy(k => k.Key).Select(k => $"{k.Key}={k.Value}"));

    /// <summary>
    /// "seed": a single-player world; a registered team of three (a Ranger with a loot bow among them), a dungeon
    /// party NPC off the team, a spouse, a lover, and three free NPCs, all geared weakly with gold to shop; then the
    /// catch-up as GameEngine.RunWorldSimCatchUp runs it, for the most it runs (CatchUpMaxTicks, a daily reset
    /// every CatchUpTicksPerDay). Returns which groups kept their gear.
    /// </summary>
    public static string NgCatchUpGear(string arg)
    {
        int seed = int.Parse(arg);
        SilenceNews();
        using var w = new RecruitPoolWorld(seed, online: false);
        // With no player, the simulator reads the wall clock's hour (DailySystemManager.GetCurrentGameHour), and from
        // 23:00 to 05:00 shopping is weighted down (WorldSimulator.ApplyTimeOfDayWeights). A player at a fixed afternoon
        // hour pins the game hour, so the result does not depend on when the test runs.
        var savedPlayer = GameEngine.Instance.CurrentPlayer;
        GameEngine.Instance.CurrentPlayer = new Character { Name2 = "ng132_clock", GameTimeMinutes = NgCatchUpHour * 60 };
        try
        {
        EquipmentDatabase.Initialize();
        var pool = w.Npcs.Where(n => n.IsAlive && !n.IsDead && string.IsNullOrEmpty(n.Team) && !n.IsSpecialNPC && !n.IsStoryNPC && !n.King)
            .OrderBy(n => n.Name2, StringComparer.Ordinal).Take(9).ToList();
        if (pool.Count < 9) return "not enough NPCs";
        const string team = "Gear Keepers";
        var members = pool.Take(3).ToList();
        members[0].Class = CharacterClass.Ranger;
        members[0].Level = Math.Max(members[0].Level, 30);
        members[0].EquippedItems[EquipmentSlot.MainHand] = EquipmentDatabase.RegisterDynamic(NpcGear132Tests.LootBow(20, 1800));
        foreach (var m in members) { m.Team = team; m.TeamPW = "pw"; GearUp(m); }
        WorldSimulator.RegisterPlayerTeam(team);

        var partyNpc = pool[3];
        GearUp(partyNpc);
        GameEngine.Instance.SetDungeonPartyNPCs(new[] { partyNpc.ID });

        var spouse = pool[4];
        GearUp(spouse);
        RomanceTracker.Instance.Spouses.Clear();
        RomanceTracker.Instance.Spouses.Add(new Spouse { NPCId = spouse.ID, NPCName = spouse.Name2 });

        var lover = pool[5];
        GearUp(lover);
        RomanceTracker.Instance.CurrentLovers.Clear();
        RomanceTracker.Instance.CurrentLovers.Add(new Lover { NPCId = lover.ID, NPCName = lover.Name2 });

        var free = pool.Skip(6).ToList();
        foreach (var f in free) GearUp(f);

        var before = pool.ToDictionary(n => n, Gear);
        w.Sim.IsCatchUpMode = true;
        for (int tick = 0; tick <= GameConfig.CatchUpMaxTicks; tick++)
        {
            if (tick % 60 == 0) w.Step(); else w.Sim.SimulateStep();
            if (tick > 0 && tick % GameConfig.CatchUpTicksPerDay == 0) DailySystemManager.Instance.RunCatchUpDailyReset();
        }
        w.Sim.IsCatchUpMode = false;
        WorldSimulator.UnregisterPlayerTeam(team);

        string Kept(IEnumerable<NPC> group) => group.All(n => Gear(n) == before[n]) ? "kept"
            : "changed:" + string.Join(";", group.Where(n => Gear(n) != before[n]).Select(n => $"{n.Name2} {before[n]} -> {Gear(n)}"));
        string freeResult = free.Any(n => Gear(n) != before[n]) ? "upgraded" : "none";
        return $"team={Kept(members)} party={Kept(new[] { partyNpc })} spouse={Kept(new[] { spouse })} lover={Kept(new[] { lover })} free={freeResult}";
        }
        finally { GameEngine.Instance.CurrentPlayer = savedPlayer; }
    }

    /// <summary>The game hour NgCatchUpGear runs at: an afternoon hour, outside the night weighting.</summary>
    internal const int NgCatchUpHour = 14;
}
