using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text.Json;
using System.Threading.Tasks;
using FluentAssertions;
using Microsoft.Data.Sqlite;
using UsurperRemake;
using UsurperRemake.Systems;
using Xunit;

namespace UsurperReborn.Tests;

/// <summary>
/// 1.3.5: every hand copy between Equipment and DynamicEquipmentData keeps every property. Each write path
/// (SaveSystem player and NPC, OnlineStateManager NPC state) and each read path (GameEngine player and NPC load,
/// WorldSimService NPC load, PlayerCharacterLoader) takes an item with every property set, found by reflection,
/// and gives every property back. A property that a path drops, today or added later, fails here.
/// </summary>
[Collection("SharedGameSingletons")]
public class SaveGuards135Tests : IDisposable
{
    private const BindingFlags F = BindingFlags.NonPublic | BindingFlags.Instance;

    private static readonly JsonSerializerOptions Json = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        IncludeFields = true,
    };

    private readonly List<NPC> _rosterBefore;
    private readonly RomanceTrackerData _romanceBefore;
    private readonly string _dbPath = Path.Combine(Path.GetTempPath(), $"usurper-saveguards135-{Guid.NewGuid():N}.db");
    private SqlSaveBackend? _db;

    public SaveGuards135Tests()
    {
        _rosterBefore = NPCSpawnSystem.Instance.ActiveNPCs.ToList();
        _romanceBefore = RomanceTracker.Instance.ToSaveData();
        OnlineStateManager.NoteRosterRestored(null);
    }

    public void Dispose()
    {
        var roster = NPCSpawnSystem.Instance.ActiveNPCs;
        roster.Clear();
        roster.AddRange(_rosterBefore);
        NPCSpawnSystem.Instance.InvalidateSnapshot();
        OnlineStateManager.NoteRosterRestored(null);
        RomanceTracker.Instance.LoadFromSaveData(_romanceBefore);
        if (_db != null)
        {
            SqliteConnection.ClearAllPools();
            try { File.Delete(_dbPath); } catch { }
        }
    }

    // ---------- the properties and the filled item ----------

    private static PropertyInfo[] Props(Type t) => t.GetProperties(BindingFlags.Public | BindingFlags.Instance)
        .Where(p => p.CanRead && p.SetMethod != null && p.SetMethod.IsPublic && p.GetIndexParameters().Length == 0)
        .OrderBy(p => p.Name, StringComparer.Ordinal).ToArray();

    /// <summary>A value for one property that differs from the default of a fresh object of the same type.</summary>
    private static object FillValue(PropertyInfo p, object? fresh, ref int n)
    {
        n++;
        var t = p.PropertyType;
        if (t == typeof(int)) return n;
        if (t == typeof(long)) return (long)(1000 + n);
        if (t == typeof(bool)) return !(bool)fresh!;
        if (t == typeof(string)) return $"g{n}";
        if (t.IsEnum)
            return Enum.GetValues(t).Cast<object>().First(v => !v.Equals(fresh) && Convert.ToInt64(v) != 0);
        throw new InvalidOperationException(
            $"SaveGuards135: no fill value for {p.Name} ({t.Name}); add one so the new property is checked on every save path");
    }

    /// <summary>An Equipment with every settable property set to a value no fresh object has (Id is set on registration).</summary>
    private static Equipment Filled(string name)
    {
        var item = new Equipment();
        var fresh = new Equipment();
        int n = 1;
        foreach (var p in Props(typeof(Equipment)))
        {
            if (p.Name == nameof(Equipment.Id)) continue;
            object value = p.Name switch
            {
                nameof(Equipment.Name) => name,
                // a body slot: every loader re-infers the weapon type of a hand slot item from its name
                nameof(Equipment.Slot) => EquipmentSlot.Body,
                nameof(Equipment.ClassRestrictions) => new List<CharacterClass> { CharacterClass.Warrior, CharacterClass.Paladin },
                _ => FillValue(p, p.GetValue(fresh), ref n),
            };
            p.SetValue(item, value);
        }
        return item;
    }

    /// <summary>The save record of an item, built by reflection (not by any game path), for the read paths.</summary>
    private static DynamicEquipmentData Record(Equipment item, int id)
    {
        var data = new DynamicEquipmentData();
        foreach (var p in Props(typeof(DynamicEquipmentData)))
        {
            var source = typeof(Equipment).GetProperty(p.Name)!.GetValue(item);
            object? value = source switch
            {
                Enum e => Convert.ToInt32(e),
                IEnumerable list and not string => list.Cast<object>().Select(o => Convert.ToInt32(o)).ToList(),
                _ => source,
            };
            p.SetValue(data, value);
        }
        data.Id = id;
        return data;
    }

    /// <summary>One comparable form: enums and numbers as long, lists joined, null as empty.</summary>
    private static string Norm(object? v) => v switch
    {
        null => "",
        string s => s,
        bool b => b ? "true" : "false",
        Enum e => Convert.ToInt64(e).ToString(),
        IEnumerable list => "[" + string.Join(",", list.Cast<object>().Select(Norm)) + "]",
        _ => Convert.ToInt64(v).ToString(),
    };

    /// <summary>Every property of `want` that `got` does not carry, as "Name: want X, got Y".</summary>
    private static List<string> Dropped(object want, object got, params string[] byDesign)
    {
        var lost = new List<string>();
        foreach (var p in Props(want.GetType()))
        {
            if (byDesign.Contains(p.Name)) continue;
            var q = got.GetType().GetProperty(p.Name);
            if (q == null) { lost.Add($"{p.Name}: no such property on {got.GetType().Name}"); continue; }
            string w = Norm(p.GetValue(want)), g = Norm(q.GetValue(got));
            if (w != g) lost.Add($"{p.Name}: want {w}, got {g}");
        }
        return lost;
    }

    // ---------- the characters and the paths ----------

    private static Character Hero(string name) => new()
    {
        Name1 = name, Name2 = name, Class = CharacterClass.Warrior, Race = CharacterRace.Human, Level = 60,
        HP = 900, MaxHP = 900, BaseMaxHP = 900, Mana = 50, MaxMana = 50, BaseMaxMana = 50,
        BaseStrength = 60, BaseDexterity = 40, BaseConstitution = 40, BaseIntelligence = 20, BaseWisdom = 20,
        BaseCharisma = 20, BaseAgility = 30, BaseStamina = 30, BaseDefence = 10, AI = CharacterAI.Human,
        AutoEquipDisabled = true,
    };

    private static NPC Npc(string name)
    {
        var npc = new NPC
        {
            ID = "npc_sg135_" + name.Replace(' ', '_'), Id = "npc_sg135_" + name.Replace(' ', '_'), Name1 = name, Name2 = name,
            Class = CharacterClass.Warrior, Race = CharacterRace.Human, Level = 30, HP = 400, MaxHP = 400, BaseMaxHP = 400,
            BaseStrength = 40, BaseDexterity = 30, BaseConstitution = 30, BaseIntelligence = 20, BaseWisdom = 20,
            BaseCharisma = 20, BaseAgility = 20, BaseStamina = 20, BaseDefence = 10,
        };
        npc.EnsureSystemsInitialized();
        return npc;
    }

    private static T ThroughJson<T>(T value) => JsonSerializer.Deserialize<T>(JsonSerializer.Serialize(value, Json), Json)!;

    private static PlayerData SavePlayer(Character c) =>
        ThroughJson((PlayerData)typeof(SaveSystem).GetMethod("SerializePlayer", F)!.Invoke(SaveSystem.Instance, new object[] { c })!);

    private static void Roster(params NPC[] npcs)
    {
        var roster = NPCSpawnSystem.Instance.ActiveNPCs;
        roster.Clear();
        roster.AddRange(npcs);
        NPCSpawnSystem.Instance.InvalidateSnapshot();
    }

    /// <summary>Puts one save record on the character's record and wears it on the body.</summary>
    private static void Carry(List<DynamicEquipmentData> into, Dictionary<int, int> worn, DynamicEquipmentData record)
    {
        into.Clear();
        into.Add(record);
        worn.Clear();
        worn[(int)EquipmentSlot.Body] = record.Id;
    }

    private static Equipment Registered(string name) =>
        EquipmentDatabase.GetByName(name) ?? throw new InvalidOperationException($"the path registered no item named {name}");

    // ---------- the guard: the two types, and the filled item ----------

    [Fact]
    public void SaveGuards135_EquipmentAndItsSaveRecord_HaveTheSameProperties()
    {
        var item = Props(typeof(Equipment)).Select(p => p.Name).ToList();
        var record = Props(typeof(DynamicEquipmentData)).Select(p => p.Name).ToList();
        record.Should().Equal(item, "every Equipment property has a save field of the same name, and no other");
        item.Should().HaveCount(53, "the property count the save paths were checked against (EN-4); a new one is checked on every path below");
    }

    [Fact]
    public void SaveGuards135_TheFilledItemAndRecord_DifferFromAFreshOneInEveryProperty()
    {
        var item = Filled("Guard135 Fill");
        Dropped(item, new Equipment(), nameof(Equipment.Id)).Should().HaveCount(Props(typeof(Equipment)).Length - 1,
            "a dropped property would come back at its default, so no filled value may equal it");
        var record = Record(item, 135_135);
        Dropped(record, new DynamicEquipmentData()).Should().HaveCount(Props(typeof(DynamicEquipmentData)).Length,
            "the same for the save record");
        Dropped(item, record, nameof(Equipment.Id)).Should().BeEmpty("the record carries the item");
    }

    // ---------- the write paths ----------

    [Fact]
    public void SaveGuards135_Write_SaveSystemPlayer_KeepsEveryProperty()
    {
        var hero = Hero("sg135 writer");
        var item = Filled("Guard135 W Player");
        hero.EquippedItems[EquipmentSlot.Body] = EquipmentDatabase.RegisterDynamic(item);

        var saved = SavePlayer(hero).DynamicEquipment.Single(d => d.Name == item.Name);

        Dropped(item, saved).Should().BeEmpty("SaveSystem.SerializePlayer writes every property of a worn item");
    }

    [Fact]
    public void SaveGuards135_Write_SaveSystemNpc_KeepsEveryProperty()
    {
        UsurperRemake.BBS.DoorMode.IsOnlineMode.Should().BeFalse("the single-player NPC writer runs offline");
        var npc = Npc("Guard Npc Writer");
        var item = Filled("Guard135 W Npc");
        npc.EquippedItems[EquipmentSlot.Body] = EquipmentDatabase.RegisterDynamic(item);
        Roster(npc);

        var task = (Task<List<NPCData>>)typeof(SaveSystem).GetMethod("SerializeNPCs", F)!.Invoke(SaveSystem.Instance, null)!;
        var saved = ThroughJson(task.GetAwaiter().GetResult()).Single(d => d.Name == npc.Name2)
            .DynamicEquipment.Single(d => d.Name == item.Name);

        Dropped(item, saved).Should().BeEmpty("SaveSystem.SerializeNPCs writes every property of a worn item");
    }

    [Fact]
    public void SaveGuards135_Write_OnlineStateManagerNpc_KeepsEveryProperty()
    {
        var npc = Npc("Guard Npc State");
        var item = Filled("Guard135 W State");
        npc.EquippedItems[EquipmentSlot.Body] = EquipmentDatabase.RegisterDynamic(item);
        Roster(npc);

        var saved = ThroughJson(OnlineStateManager.SerializeCurrentNPCs()).Single(d => d.Name == npc.Name2)
            .DynamicEquipment.Single(d => d.Name == item.Name);

        Dropped(item, saved).Should().BeEmpty("OnlineStateManager.SerializeCurrentNPCs writes every property of a worn item");
    }

    // ---------- the read paths ----------

    private const int SavedId = 135_135;

    [Fact]
    public void SaveGuards135_Read_GameEnginePlayer_KeepsEveryProperty()
    {
        UsurperRemake.BBS.DoorMode.IsOnlineMode.Should().BeFalse("the single-player load keeps the saved id");
        var item = Filled("Guard135 R Player");
        var data = SavePlayer(Hero("sg135 player load"));
        Carry(data.DynamicEquipment, data.EquippedItems, Record(item, SavedId));

        Character loaded;
        try { loaded = (Character)typeof(GameEngine).GetMethod("RestorePlayerFromSaveData", F)!.Invoke(GameEngine.Instance, new object[] { data })!; }
        catch (TargetInvocationException ex) when (ex.InnerException != null) { throw ex.InnerException; }

        loaded.EquippedItems[EquipmentSlot.Body].Should().Be(SavedId, "the load wears the saved item");
        // Id: the load registers the item again (here under its saved id); by design
        Dropped(item, Registered(item.Name), nameof(Equipment.Id)).Should().BeEmpty("GameEngine's player load reads every property");
    }

    [Fact]
    public async Task SaveGuards135_Read_GameEngineNpc_KeepsEveryProperty()
    {
        var item = Filled("Guard135 R Npc");
        Roster(Npc("Guard Npc Load"));
        var data = ThroughJson(OnlineStateManager.SerializeCurrentNPCs());
        var rec = data.Single(d => d.Name == "Guard Npc Load");
        Carry(rec.DynamicEquipment, rec.EquippedItems, Record(item, SavedId));
        NPCSpawnSystem.Instance.ActiveNPCs.Clear();

        await GameEngine.Instance.RestoreNPCs(data);

        NPCSpawnSystem.Instance.ActiveNPCs.Should().ContainSingle(n => n.Name2 == "Guard Npc Load", "the NPC loads");
        // Id: the load registers the item again under a fresh id; by design
        Dropped(item, Registered(item.Name), nameof(Equipment.Id)).Should().BeEmpty("GameEngine's NPC load reads every property");
    }

    [Fact]
    public void SaveGuards135_Read_WorldSimServiceNpc_KeepsEveryProperty()
    {
        var item = Filled("Guard135 R WorldSim");
        Roster(Npc("Guard Npc Sim"));
        var data = ThroughJson(OnlineStateManager.SerializeCurrentNPCs());
        var rec = data.Single(d => d.Name == "Guard Npc Sim");
        Carry(rec.DynamicEquipment, rec.EquippedItems, Record(item, SavedId));
        NPCSpawnSystem.Instance.ActiveNPCs.Clear();

        _db = new SqlSaveBackend(_dbPath);
        var sim = new WorldSimService(_db);
        try { typeof(WorldSimService).GetMethod("RestoreNPCsFromData", F)!.Invoke(sim, new object[] { data }); }
        catch (TargetInvocationException ex) when (ex.InnerException != null) { throw ex.InnerException; }

        NPCSpawnSystem.Instance.ActiveNPCs.Should().ContainSingle(n => n.Name2 == "Guard Npc Sim", "the NPC loads");
        // Id: the load registers the item again under a fresh id; by design
        Dropped(item, Registered(item.Name), nameof(Equipment.Id)).Should().BeEmpty("WorldSimService's NPC load reads every property");
    }

    [Fact]
    public void SaveGuards135_Read_PlayerCharacterLoader_KeepsEveryProperty()
    {
        var item = Filled("Guard135 R Loader");
        var data = SavePlayer(Hero("sg135 loader"));
        Carry(data.DynamicEquipment, data.EquippedItems, Record(item, SavedId));

        var loaded = PlayerCharacterLoader.CreateFromSaveData(data, "sg135 loader");

        var worn = EquipmentDatabase.GetById(loaded.EquippedItems[EquipmentSlot.Body]);
        worn.Should().NotBeNull("the loaded character wears the saved item");
        // Id: the loader registers the item again under a fresh id; by design
        Dropped(item, worn!, nameof(Equipment.Id)).Should().BeEmpty("PlayerCharacterLoader reads every property");
    }

    // ---------- (b): an unidentified item through PlayerCharacterLoader ----------

    [Fact]
    public void SaveGuards135_PlayerCharacterLoader_AnUnidentifiedItem_LoadsUnidentified()
    {
        var hero = Hero("sg135 veiled");
        var item = new Equipment { Name = "Guard135 Veiled Vest", Slot = EquipmentSlot.Body, ArmorClass = 5, Value = 300, IsIdentified = false };
        hero.EquippedItems[EquipmentSlot.Body] = EquipmentDatabase.RegisterDynamic(item);

        var data = SavePlayer(hero);
        data.DynamicEquipment.Single(d => d.Name == item.Name).IsIdentified.Should().BeFalse("the save keeps the item unidentified");

        var loaded = PlayerCharacterLoader.CreateFromSaveData(data, "sg135 veiled");

        var worn = loaded.GetEquipment(EquipmentSlot.Body);
        worn.Should().NotBeNull("the loaded character wears the vest");
        worn!.Name.Should().Be(item.Name);
        worn.IsIdentified.Should().BeFalse("an item saved unidentified loads unidentified through PlayerCharacterLoader");
    }

    // ---------- (c): the save editor's display registration ----------

    [Fact]
    public void SaveGuards135_SaveEditor_RegistersEveryProperty()
    {
        const int id = 1_350_012;   // an id no other test registers
        EquipmentDatabase.GetById(id).Should().BeNull("the editor skips an id that is already registered");
        var item = Filled("Guard135 Editor");
        var data = new PlayerData { DynamicEquipment = new List<DynamicEquipmentData> { Record(item, id) } };

        var editor = typeof(GameEngine).Assembly.GetType("UsurperRemake.Editor.PlayerSaveEditor", throwOnError: true)!;
        editor.GetMethod("RegisterDynamicEquipmentForEditor", BindingFlags.NonPublic | BindingFlags.Static)!
            .Invoke(null, new object[] { data });

        var shown = EquipmentDatabase.GetById(id);
        shown.Should().NotBeNull("the editor registered the saved item (it skips one that throws)");
        Dropped(item, shown!, nameof(Equipment.Id)).Should().BeEmpty("the editor's display registration copies every property");
        shown!.Id.Should().Be(id, "the editor registers the item under its saved id");
    }
}
