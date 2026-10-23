using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Threading.Tasks;
using FluentAssertions;
using Microsoft.Data.Sqlite;
using UsurperRemake;
using UsurperRemake.BBS;
using UsurperRemake.Locations;
using UsurperRemake.Systems;
using Xunit;
using S = UsurperReborn.Tests.ClassTexts136Support;

namespace UsurperReborn.Tests;

/// <summary>
/// 1.3.7 VR (the Voidreaver lift): a Voidreaver level up gains Constitution, 2 on an even level and 1 on an odd one
/// (the level the level up reaches); the death level loss takes back what the lost level gave; the NPC floor expects
/// it; an existing Voidreaver, player or NPC, gets once on load the Constitution its levels so far would have given,
/// guarded by a saved marker; the admin level edit gains it behind the same marker; Unmaking costs 10 percent of
/// current HP (was 25). The new members are reached by name, so this file builds on the base too and shows red there.
/// </summary>
[Collection("SharedGameSingletons")]
public class VR137VoidreaverTests
{
    private const BindingFlags F = BindingFlags.NonPublic | BindingFlags.Instance;
    private const BindingFlags SNP = BindingFlags.NonPublic | BindingFlags.Static;
    private const BindingFlags SP = BindingFlags.Public | BindingFlags.Static;
    private const string Marker = "VoidreaverConstitutionApplied";

    /// <summary>The rule as the brief states it: the sum of g(l) over the levels 2..L reached by a level up.</summary>
    private static long Total(int level) => level <= 1 ? 0 : (level - 1) + level / 2;

    // ---------------- reflection seams (new members, so the file compiles on the base) ----------------

    private static int Gain(int levelReached)
    {
        var m = typeof(LevelMasterLocation).GetMethod("VoidreaverConstitutionGain", SP);
        m.Should().NotBeNull("LevelMasterLocation.VoidreaverConstitutionGain is the one source of the per level gain");
        return (int)m!.Invoke(null, new object[] { levelReached })!;
    }

    private static long Through(int level)
    {
        var m = typeof(LevelMasterLocation).GetMethod("VoidreaverConstitutionThrough", SP);
        m.Should().NotBeNull("LevelMasterLocation.VoidreaverConstitutionThrough sums the gain");
        return (long)m!.Invoke(null, new object[] { level })!;
    }

    private static bool GetMarker(object o)
    {
        var p = o.GetType().GetProperty(Marker);
        p.Should().NotBeNull($"{o.GetType().Name} carries the saved marker {Marker}");
        return (bool)p!.GetValue(o)!;
    }

    private static void SetMarker(object o, bool value)
    {
        var p = o.GetType().GetProperty(Marker);
        p.Should().NotBeNull($"{o.GetType().Name} carries the saved marker {Marker}");
        p!.SetValue(o, value);
    }

    /// <summary>A save written before 1.3.7: the marker is not in the JSON (any casing).</summary>
    private static JsonNode StripMarker(JsonNode node)
    {
        if (node is JsonObject obj)
        {
            foreach (var key in obj.Select(kv => kv.Key).Where(k => string.Equals(k, Marker, StringComparison.OrdinalIgnoreCase)).ToList())
                obj.Remove(key);
            foreach (var kv in obj.ToList()) if (kv.Value != null) StripMarker(kv.Value);
        }
        else if (node is JsonArray arr)
        {
            foreach (var item in arr) if (item != null) StripMarker(item);
        }
        return node;
    }

    private static bool JsonHasMarker(string json) =>
        json.Contains("\"" + Marker + "\"", StringComparison.OrdinalIgnoreCase);

    // ---------------- fixtures ----------------

    private static Character Hero(string name, CharacterClass cls, int level, long con = 50) => new Character
    {
        Name1 = name, Name2 = name, AI = CharacterAI.Human, Level = level, HP = 100, MaxHP = 100,
        BaseMaxHP = 500, BaseStrength = 40, BaseDexterity = 40, BaseConstitution = con, BaseIntelligence = 40,
        BaseWisdom = 20, BaseCharisma = 10, BaseDefence = 20, BaseStamina = 30, BaseAgility = 30, BaseMaxMana = 200,
        Mental = 80, Class = cls, Race = CharacterRace.Human, Experience = GameConfig.GetExperienceForLevel(level)
    };

    private static PlayerData Serialize(Character c)
    {
        var ser = typeof(SaveSystem).GetMethod("SerializePlayer", F)!;
        try { return (PlayerData)ser.Invoke(SaveSystem.Instance, new object[] { c })!; }
        catch (TargetInvocationException ex) when (ex.InnerException != null) { throw ex.InnerException; }
    }

    /// <summary>The player's save through JSON; old = written before 1.3.7 (no marker).</summary>
    private static PlayerData ThroughJson(PlayerData data, bool old)
    {
        var node = JsonNode.Parse(JsonSerializer.Serialize(data))!;
        if (old) StripMarker(node);
        return JsonSerializer.Deserialize<PlayerData>(node.ToJsonString())!;
    }

    /// <summary>A player load: the restore, then the one-time login migrations (GameEngine.cs, both load paths).</summary>
    private static Character Load(PlayerData data)
    {
        var c = MenuKeysNeedEnterPref1115Tests.Restore(data);
        GameEngine.RunStatRewardMigrations(c, null);
        return c;
    }

    private static Character Twin(Character c, long extraCon)
    {
        var t = Hero(c.Name2 + " twin", c.Class, c.Level, c.BaseConstitution + extraCon);
        t.BaseMaxHP = c.BaseMaxHP; t.BaseStrength = c.BaseStrength; t.BaseDexterity = c.BaseDexterity;
        t.BaseIntelligence = c.BaseIntelligence; t.BaseWisdom = c.BaseWisdom; t.BaseCharisma = c.BaseCharisma;
        t.BaseDefence = c.BaseDefence; t.BaseStamina = c.BaseStamina; t.BaseAgility = c.BaseAgility; t.BaseMaxMana = c.BaseMaxMana;
        t.RecalculateStats();
        return t;
    }

    // ---------------- the rule ----------------

    [Fact]
    public void VR137Voidreaver_TheGainIsTwoOnEvenLevelsAndOneOnOdd_AndTheTotalIsTheFormula()
    {
        for (int l = 2; l <= 100; l++)
            Gain(l).Should().Be(l % 2 == 0 ? 2 : 1, $"level {l}");
        Through(1).Should().Be(0, "level 1 is reached by no level up");
        for (int level = 1; level <= 100; level++)
        {
            long sum = 0;
            for (int l = 2; l <= level; l++) sum += Gain(l);
            Through(level).Should().Be(sum, $"the total to {level} is the sum of the gains");
            Through(level).Should().Be(Total(level), $"(L - 1) + floor(L / 2) at {level}");
        }
        Through(80).Should().Be(119, "L 80: 79 + 40");
    }

    // ---------------- level up and level loss ----------------

    [Fact]
    public void VR137Voidreaver_ANewVoidreaverLevelled1To80_GainsTheTotal_ThroughTheAutoLevelUp()
    {
        var c = Hero("Vr137 Climber", CharacterClass.Voidreaver, 1, con: 10);
        c.Experience = GameConfig.GetExperienceForLevel(80);
        LevelMasterLocation.CheckAutoLevelUp(c).Should().Be(79);
        c.Level.Should().Be(80);
        c.BaseConstitution.Should().Be(10 + Total(80), "79 level ups at 2 on an even level and 1 on an odd one, neutral master");
        c.Constitution.Should().Be(c.BaseConstitution);
    }

    [Fact]
    public void VR137Voidreaver_AnNpcVoidreaverLevelled1To80_GainsTheTotal_ThroughTheTeammateOrder()
    {
        // the NPC and teammate level ups (CombatEngine.cs, GameEngine.cs, LevelMasterLocation.cs) raise Level first
        var n = new NPC { Name1 = "Vr137 Npc Climber", Name2 = "Vr137 Npc Climber", Class = CharacterClass.Voidreaver, Level = 1, BaseConstitution = 10, BaseMaxHP = 100 };
        while (n.Level < 80)
        {
            n.Level++;
            LevelMasterLocation.ApplyClassStatIncreases(n);
        }
        n.BaseConstitution.Should().Be(10 + Total(80));
    }

    [Fact]
    public void VR137Voidreaver_ANonVoidreaverLevelUp_GainsNoNewConstitution()
    {
        // an Assassin's level up gives no Constitution, before and after the lift
        var c = Hero("Vr137 Assassin", CharacterClass.Assassin, 1, con: 10);
        c.Experience = GameConfig.GetExperienceForLevel(80);
        LevelMasterLocation.CheckAutoLevelUp(c);
        c.BaseConstitution.Should().Be(10);
    }

    [Theory]
    [InlineData(80)]
    [InlineData(79)]
    public void VR137Voidreaver_LevelLossTakesBackWhatTheLevelGave_AndRegainRoundTrips(int top)
    {
        var c = Hero("Vr137 Mourner", CharacterClass.Voidreaver, 1, con: 10);
        c.Experience = GameConfig.GetExperienceForLevel(top);
        LevelMasterLocation.CheckAutoLevelUp(c);
        c.BaseConstitution.Should().Be(10 + Total(top));

        // the death penalty (CombatEngine.cs and GameEngine.cs): reverse, then lower the level, five times
        for (int i = 0; i < 5; i++)
        {
            LevelMasterLocation.ReverseClassStatIncrease(c);
            c.Level--;
            c.BaseConstitution.Should().Be(10 + Total(c.Level), $"after losing down to {c.Level}");
        }
        c.Experience = GameConfig.GetExperienceForLevel(top);
        LevelMasterLocation.CheckAutoLevelUp(c);
        c.Level.Should().Be(top);
        c.BaseConstitution.Should().Be(10 + Total(top), "the regained levels give what the lost ones took");
    }

    // ---------------- the one-time top-up, a player ----------------

    [Fact]
    public void VR137Voidreaver_AnOldSaveOfAnL80Voidreaver_GainsTheTopUpOnce_AndASaveAfterItNone()
    {
        var hero = Hero("Vr137 Elder", CharacterClass.Voidreaver, 80, con: 50);
        hero.RecalculateStats();
        var old = ThroughJson(Serialize(hero), old: true);
        GetMarker(old).Should().BeFalse("a save written before 1.3.7 has no marker, read as false");

        var loaded = Load(old);
        loaded.BaseConstitution.Should().Be(50 + 119, "once, the formula at L 80");
        loaded.Constitution.Should().Be(50 + 119, "the game's own recalculation ran");
        loaded.MaxHP.Should().Be(Twin(hero, 119).MaxHP, "Max HP follows the new Constitution");
        GetMarker(loaded).Should().BeTrue();

        // a second run of the login migrations in the same session adds none
        GameEngine.RunStatRewardMigrations(loaded, null);
        loaded.BaseConstitution.Should().Be(169);

        // a save written after the top-up carries the marker and loads with none
        var after = Serialize(loaded);
        GetMarker(after).Should().BeTrue("the player save writes the marker");
        JsonHasMarker(JsonSerializer.Serialize(after)).Should().BeTrue();
        var again = Load(ThroughJson(after, old: false));
        again.BaseConstitution.Should().Be(169, "the guard set: no second top-up");
        GetMarker(again).Should().BeTrue();
    }

    [Fact]
    public void VR137Voidreaver_TheToppedUpVoidreaver_EqualsANewOneLevelled1To80()
    {
        var fresh = Hero("Vr137 Fresh", CharacterClass.Voidreaver, 1, con: 10);
        fresh.Experience = GameConfig.GetExperienceForLevel(80);
        LevelMasterLocation.CheckAutoLevelUp(fresh);

        var elder = Hero("Vr137 Elder Two", CharacterClass.Voidreaver, 80, con: 10);
        var loaded = Load(ThroughJson(Serialize(elder), old: true));
        loaded.BaseConstitution.Should().Be(fresh.BaseConstitution, "the top-up gives what the new rule's level ups give");
    }

    [Fact]
    public void VR137Voidreaver_ANewCharacter_StartsWithTheMarkerSet_SoItIsNeverToppedUp()
    {
        var c = Hero("Vr137 Newborn", CharacterClass.Voidreaver, 80, con: 50);
        GetMarker(c).Should().BeTrue("a character made on 1.3.7 levels under the new rule and needs no top-up");
        var loaded = Load(ThroughJson(Serialize(c), old: false));
        loaded.BaseConstitution.Should().Be(50);
    }

    [Theory]
    [InlineData(CharacterClass.Warrior)]
    [InlineData(CharacterClass.Abysswarden)]
    [InlineData(CharacterClass.Assassin)]
    public void VR137Voidreaver_ANonVoidreaverOldSave_GetsNoTopUp(CharacterClass cls)
    {
        var hero = Hero("Vr137 Other " + cls, cls, 80, con: 50);
        var loaded = Load(ThroughJson(Serialize(hero), old: true));
        loaded.BaseConstitution.Should().Be(50, $"{cls} gets nothing");
        GetMarker(loaded).Should().BeTrue("the check ran once for every class");
    }

    [Fact]
    public void VR137Voidreaver_LevelLossAfterTheTopUp_TakesBackWhatTheLevelGave()
    {
        var loaded = Load(ThroughJson(Serialize(Hero("Vr137 Fallen", CharacterClass.Voidreaver, 80, con: 10)), old: true));
        loaded.BaseConstitution.Should().Be(10 + Total(80));
        for (int i = 0; i < 5; i++) { LevelMasterLocation.ReverseClassStatIncrease(loaded); loaded.Level--; }
        loaded.BaseConstitution.Should().Be(10 + Total(75));
        loaded.Experience = GameConfig.GetExperienceForLevel(80);
        LevelMasterLocation.CheckAutoLevelUp(loaded);
        loaded.BaseConstitution.Should().Be(10 + Total(80));
    }

    [Fact]
    public void VR137Voidreaver_ThePvpCopyLoader_CarriesTheSavedMarker()
    {
        var oldData = ThroughJson(Serialize(Hero("Vr137 Copy", CharacterClass.Voidreaver, 80)), old: true);
        GetMarker(PlayerCharacterLoader.CreateFromSaveData(oldData, "Vr137 Copy")).Should().BeFalse();
        SetMarker(oldData, true);
        GetMarker(PlayerCharacterLoader.CreateFromSaveData(oldData, "Vr137 Copy")).Should().BeTrue();
    }

    // ---------------- the one-time top-up, an NPC ----------------

    private sealed class Roster : IDisposable
    {
        private readonly List<NPC> _before = NPCSpawnSystem.Instance.ActiveNPCs.ToList();
        private readonly FieldInfo _init = typeof(NPCSpawnSystem).GetField("npcsInitialized", F)!;
        private readonly object? _initBefore;
        private readonly bool _onlineBefore = DoorMode.IsOnlineMode;
        private static readonly FieldInfo OnlineField = typeof(DoorMode).GetField("_onlineMode", BindingFlags.Static | BindingFlags.NonPublic)!;
        public Roster(bool online)
        {
            _initBefore = _init.GetValue(NPCSpawnSystem.Instance);
            OnlineField.SetValue(null, online);
            NPCSpawnSystem.Instance.ActiveNPCs.Clear();
        }
        public void Dispose()
        {
            NPCSpawnSystem.Instance.IsRebuilding = false;
            NPCSpawnSystem.Instance.ActiveNPCs.Clear();
            NPCSpawnSystem.Instance.ActiveNPCs.AddRange(_before);
            _init.SetValue(NPCSpawnSystem.Instance, _initBefore);
            OnlineField.SetValue(null, _onlineBefore);
        }
    }

    private static int _made;

    private static NPC Npc(string name, CharacterClass cls, int level, long con)
    {
        _made++;
        var n = new NPC
        {
            ID = "npc_vr137_" + _made, Id = "vr137-" + _made, Name1 = name, Name2 = name, Race = CharacterRace.Human,
            Sex = CharacterSex.Male, Class = cls, Level = level, HP = 900, MaxHP = 900, BaseMaxHP = 900,
            BaseStrength = 60, BaseDefence = 40, BaseDexterity = 60, BaseAgility = 60, BaseStamina = 60,
            BaseConstitution = con, BaseIntelligence = 60, BaseWisdom = 30, BaseCharisma = 20, BaseMaxMana = 300,
            Experience = GameConfig.GetExperienceForLevel(level), AI = CharacterAI.Computer, Age = 30,
            CurrentLocation = "Main Street", BirthDate = DateTime.Now.AddDays(-30),
        };
        n.RecalculateStats();
        return n;
    }

    private static List<NPCData> NpcJson(List<NPCData> data, bool old)
    {
        var opts = new JsonSerializerOptions { PropertyNamingPolicy = JsonNamingPolicy.CamelCase, IncludeFields = true };
        var node = JsonNode.Parse(JsonSerializer.Serialize(data, opts))!;
        if (old) StripMarker(node);
        return JsonSerializer.Deserialize<List<NPCData>>(node.ToJsonString(), opts)!;
    }

    private static NPC Find(string name) => NPCSpawnSystem.Instance.ActiveNPCs.Single(n => n.Name2 == name);

    [Fact]
    public void VR137Voidreaver_AnOldNpcVoidreaver_GainsTheTopUpOnce_InTheSinglePlayerSaveAndLoad()
    {
        using var roster = new Roster(online: false);
        string name = "Vr137 Hermit " + Guid.NewGuid().ToString("N")[..6];
        string other = "Vr137 Smith " + Guid.NewGuid().ToString("N")[..6];
        NPCSpawnSystem.Instance.ActiveNPCs.Add(Npc(name, CharacterClass.Voidreaver, 80, 50));
        NPCSpawnSystem.Instance.ActiveNPCs.Add(Npc(other, CharacterClass.Warrior, 80, 50));

        var task = (Task<List<NPCData>>)typeof(SaveSystem).GetMethod("SerializeNPCs", F)!.Invoke(SaveSystem.Instance, null)!;
        var old = NpcJson(task.GetAwaiter().GetResult(), old: true);
        GetMarker(old.Single(d => d.Name == name)).Should().BeFalse();

        GameEngine.Instance.RestoreNPCs(old).GetAwaiter().GetResult();
        Find(name).BaseConstitution.Should().Be(10 + 119, "the single-player NPC loader raises an NPC below the floor to it, once");
        Find(name).Constitution.Should().Be(10 + 119);
        Find(other).BaseConstitution.Should().Be(50, "a non Voidreaver NPC gets nothing");

        var task2 = (Task<List<NPCData>>)typeof(SaveSystem).GetMethod("SerializeNPCs", F)!.Invoke(SaveSystem.Instance, null)!;
        var after = task2.GetAwaiter().GetResult();
        GetMarker(after.Single(d => d.Name == name)).Should().BeTrue("the single-player NPC save writes the marker");
        GameEngine.Instance.RestoreNPCs(NpcJson(after, old: false)).GetAwaiter().GetResult();
        Find(name).BaseConstitution.Should().Be(129, "a save after the top-up loads with none");
    }

    [Fact]
    public void VR137Voidreaver_AnOldNpcVoidreaver_GainsTheTopUpOnce_InTheOnlineSaveAndBothLoaders()
    {
        using var roster = new Roster(online: true);
        string name = "Vr137 Wanderer " + Guid.NewGuid().ToString("N")[..6];
        NPCSpawnSystem.Instance.ActiveNPCs.Add(Npc(name, CharacterClass.Voidreaver, 80, 50));
        string path = Path.Combine(Path.GetTempPath(), $"usurper-vr137-{Guid.NewGuid():N}.db");
        try
        {
            var old = NpcJson(OnlineStateManager.SerializeCurrentNPCs(), old: true);   // the online writer
            GetMarker(old.Single(d => d.Name == name)).Should().BeFalse();

            var service = new WorldSimService(new SqlSaveBackend(path));
            typeof(WorldSimService).GetMethod("RestoreNPCsFromData", F)!.Invoke(service, new object[] { old });   // the world simulator's loader
            Find(name).BaseConstitution.Should().Be(129, "the world simulator's NPC loader raises it to the floor once");
            Find(name).Constitution.Should().Be(129);

            var written = OnlineStateManager.SerializeCurrentNPCs();
            GetMarker(written.Single(d => d.Name == name)).Should().BeTrue("the online NPC save writes the marker");
            typeof(WorldSimService).GetMethod("RestoreNPCsFromData", F)!.Invoke(service, new object[] { NpcJson(written, old: false) });
            Find(name).BaseConstitution.Should().Be(129, "the world simulator loads a topped-up NPC with none");

            GameEngine.Instance.RestoreNPCs(NpcJson(old, old: true)).GetAwaiter().GetResult();   // a player session's loader, old record
            Find(name).BaseConstitution.Should().Be(129, "a session loading the old record tops up once, from the same saved value");
            GameEngine.Instance.RestoreNPCs(NpcJson(written, old: false)).GetAwaiter().GetResult();
            Find(name).BaseConstitution.Should().Be(129, "and none from the topped-up record");
        }
        finally
        {
            SqliteConnection.ClearAllPools();
            try { File.Delete(path); } catch { }
        }
    }

    [Fact]
    public void VR137Voidreaver_TheNpcFloorAndTheTopUp_NeverAddTwice()
    {
        // the floor (EnsureClassStatsForLevel, called from DungeonLocation.cs) is a Math.Max with the new expectation
        var fresh = Npc("Vr137 Floor Fresh", CharacterClass.Voidreaver, 80, 10);
        LevelMasterLocation.EnsureClassStatsForLevel(fresh);
        fresh.BaseConstitution.Should().Be(10 + Total(80), "the floor expects the lifted Constitution");
        LevelMasterLocation.EnsureClassStatsForLevel(fresh);
        fresh.BaseConstitution.Should().Be(10 + Total(80), "a second floor adds none");

        // an old NPC topped up on load, then floored: the floor adds none on top
        var topped = Npc("Vr137 Floor Topped", CharacterClass.Voidreaver, 80, 50);
        SetMarker(topped, false);
        NpcTopUp(topped);
        topped.BaseConstitution.Should().Be(129, "an NPC top-up is the floor");
        topped.BaseConstitution = 140;   // trained above the floor after the top-up
        LevelMasterLocation.EnsureClassStatsForLevel(topped);
        topped.BaseConstitution.Should().Be(140, "the floor is a Max, under the value");

        // an old NPC floored before any top-up: the floor runs the guarded top-up first, so a later top-up adds none
        var floored = Npc("Vr137 Floor First", CharacterClass.Voidreaver, 80, 10);
        SetMarker(floored, false);
        LevelMasterLocation.EnsureClassStatsForLevel(floored);
        floored.BaseConstitution.Should().Be(10 + Total(80));
        GetMarker(floored).Should().BeTrue();
        NpcTopUp(floored);
        floored.BaseConstitution.Should().Be(10 + Total(80), "never twice");
    }

    private static long NpcTopUp(Character c)
    {
        var m = typeof(LevelMasterLocation).GetMethod("ApplyVoidreaverConstitutionTopUp", SP)!;
        m.GetParameters().Length.Should().Be(2, "the top-up is told whether it serves an NPC");
        return (long)m.Invoke(null, new object[] { c, true })!;
    }

    [Fact]
    public void VR137Voidreaver_AnOldSpawnShapedNpc_AboveTheFloor_StaysUnchanged_AndOneBelowIsRaisedOnce()
    {
        // NPCSpawnSystem gave every NPC 10 + 2 x level Constitution (plus or minus 3), whatever its class:
        // at L 80 about 170, above the floor 10 + 119, so the old NPC keeps what it has
        using var roster = new Roster(online: false);
        string spawned = "Vr137 Spawned " + Guid.NewGuid().ToString("N")[..6];
        string low = "Vr137 Low " + Guid.NewGuid().ToString("N")[..6];
        NPCSpawnSystem.Instance.ActiveNPCs.Add(Npc(spawned, CharacterClass.Voidreaver, 80, 10 + 80 * 2 - 3));
        NPCSpawnSystem.Instance.ActiveNPCs.Add(Npc(low, CharacterClass.Voidreaver, 80, 100));
        var task = (Task<List<NPCData>>)typeof(SaveSystem).GetMethod("SerializeNPCs", F)!.Invoke(SaveSystem.Instance, null)!;
        var old = NpcJson(task.GetAwaiter().GetResult(), old: true);
        GameEngine.Instance.RestoreNPCs(old).GetAwaiter().GetResult();
        Find(spawned).BaseConstitution.Should().Be(167, "above the floor: no top-up added on top of the spawn Constitution");
        GetMarker(Find(spawned)).Should().BeTrue();
        Find(low).BaseConstitution.Should().Be(129, "below the floor: raised to 10 + 119");
        NpcTopUp(Find(low)).Should().Be(0, "once only");
        Find(low).BaseConstitution.Should().Be(129);

        // a player is not floored: the top-up adds (his answer), even above 10 + 119
        var hero = Hero("Vr137 Strong Elder", CharacterClass.Voidreaver, 80, con: 170);
        Load(ThroughJson(Serialize(hero), old: true)).BaseConstitution.Should().Be(170 + 119);
    }

    // ---------------- the wiki export ----------------

    [Fact]
    public void VR137Voidreaver_TheWikiExport_ShowsTheVoidreaversConstitutionGrowthAs1Point5()
    {
        string dir = Path.Combine(Path.GetTempPath(), $"usurper-vr137-wiki-{Guid.NewGuid():N}");
        try
        {
            WikiDataExporter.Export(dir);
            using var doc = JsonDocument.Parse(File.ReadAllText(Path.Combine(dir, "classes.json")));
            JsonElement Growth(string id) => doc.RootElement.GetProperty("data").EnumerateArray()
                .Single(c => c.GetProperty("id").GetString() == id).GetProperty("growthPerLevel");
            Growth("Voidreaver").GetProperty("constitution").GetDouble().Should().Be(1.5, "2 on an even level, 1 on an odd one");
            Growth("Voidreaver").GetProperty("strength").GetInt64().Should().Be(4, "a gain that does not alternate stays whole");
            Growth("Warrior").GetProperty("constitution").GetInt64().Should().Be(3);
            Growth("Warrior").GetProperty("maxHP").GetInt64().Should().Be(17);
        }
        finally { try { Directory.Delete(dir, true); } catch { } }
    }

    // ---------------- the admin level edit ----------------

    private static void AdminRaise(PlayerData p, int oldLevel, int newLevel)
    {
        var m = typeof(OnlineAdminConsole).GetMethod("ApplyClassStatIncreasesToSaveData", SNP)!;
        m.GetParameters().Length.Should().Be(3, "the admin copy takes the old level, so each level reached is known");
        p.Level = newLevel;   // the edit sets the level first (OnlineAdminConsole.cs)
        m.Invoke(null, new object[] { p, newLevel - oldLevel, oldLevel });
    }

    [Theory]
    [InlineData(40, 80)]
    [InlineData(41, 80)]
    [InlineData(1, 79)]
    public void VR137Voidreaver_TheAdminLevelEdit_GainsTheConstitutionOfEachLevelReached_BehindTheMarker(int from, int to)
    {
        var p = Serialize(Hero("Vr137 Admin", CharacterClass.Voidreaver, from, con: 100));
        GetMarker(p).Should().BeTrue();
        long str = p.BaseStrength;
        AdminRaise(p, from, to);
        p.BaseConstitution.Should().Be(100 + Total(to) - Total(from), "g(l) for each level reached, oldLevel + i + 1");
        p.BaseStrength.Should().Be(str + 5L * (to - from), "the copy's other rows are unchanged");

        // a save without the marker gains none here: its next load gives every level's Constitution at once
        var old = ThroughJson(Serialize(Hero("Vr137 Admin Old", CharacterClass.Voidreaver, from, con: 100)), old: true);
        AdminRaise(old, from, to);
        old.BaseConstitution.Should().Be(100);
        Load(ThroughJson(old, old: false)).BaseConstitution.Should().Be(100 + Total(to), "the top-up covers the raised levels once");
    }

    [Fact]
    public void VR137Voidreaver_TheAdminLevelEdit_GivesANonVoidreaverNoNewConstitution()
    {
        var p = Serialize(Hero("Vr137 Admin Assassin", CharacterClass.Assassin, 10, con: 100));
        AdminRaise(p, 10, 50);
        p.BaseConstitution.Should().Be(100);
    }

    // ---------------- Unmaking ----------------

    [Theory]
    [InlineData(5000, 500)]
    [InlineData(999, 99)]
    public void VR137Voidreaver_UnmakingCostsTenPercentOfCurrentHp(long hp, long cost)
    {
        var caster = new Character
        {
            Name1 = "Vr137 Caster", Name2 = "Vr137 Caster", Class = CharacterClass.Voidreaver, Level = 100, HP = hp, MaxHP = 9000,
            Mana = 9000, MaxMana = 9000, Intelligence = 200, Wisdom = 200, AI = CharacterAI.Human,
        };
        var r = new SpellSystem.SpellResult { Success = true, ProficiencyMultiplier = 1.0f };
        typeof(SpellSystem).GetMethod("ExecuteVoidreaverSpell", SNP)!.Invoke(null, new object?[] { caster, 5, null, null, r, new Random(7), 1.0f });
        caster.HP.Should().Be(hp - cost, "10 percent of current HP, rounded down");
        r.Message.Should().Contain(Loc.Get("combat.spell_unmaking_cast", caster.Name2, r.Damage, cost));
    }

    [Fact]
    public void VR137Voidreaver_UnmakingsTextSaysTenPercent_InFiveLanguages()
    {
        const string key = "spell.voidreaver.5.desc";
        S.FiveLanguages(new[] { key });
        S.SpellDescIsCode(CharacterClass.Voidreaver, 5);
        S.DescLength(key);
        foreach (var lang in S.Langs)
        {
            string t = Loc.GetIn(lang, key);
            t.Should().MatchRegex(@"\b10\s?%", $"{lang}: \"{t}\"");
            t.Should().NotContain("25", $"{lang}: the old figure is gone");
        }
    }
}
