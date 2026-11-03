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
using Microsoft.Data.Sqlite;
using UsurperRemake;
using UsurperRemake.Server;
using UsurperRemake.Systems;
using Xunit;

namespace UsurperReborn.Tests;

/// <summary>
/// 1.3.10 small game items: new characters start on Fast combat speed while every saved character keeps its own (a save
/// with no CombatSpeed field reads Normal); the map legend's leftover rows sit in the legend's column (R13); the dungeon
/// room's Client.Menu sends the key the room prints (B10); a Main Street district sends a Room.Info naming it, and the
/// street is named again on the way back (A2); the loot comparison for a two-handed weapon counts the off-hand and names
/// it (B15); an empty Enter at the loot prompt asks again with no "Invalid choice" line (B18).
/// </summary>
[Collection("SharedGameSingletons")]
public class GameSmall1310Tests : IDisposable
{
    private const BindingFlags F = BindingFlags.NonPublic | BindingFlags.Instance;
    private static readonly string[] GlobalKeys = { "%", "*", "~", "0", "?", "!" };

    private readonly string _dbPath = Path.Combine(Path.GetTempPath(), $"usurper-small-1310-{Guid.NewGuid():N}.db");
    private SqlSaveBackend? _db;
    private SqlSaveBackend Db => _db ??= new SqlSaveBackend(_dbPath);

    public void Dispose()
    {
        if (_db == null) return;
        SqliteConnection.ClearAllPools();
        try { File.Delete(_dbPath); } catch { }
    }

    private static object? Invoke(object target, Type type, string method, params object?[] args)
    {
        try { return type.GetMethod(method, F)!.Invoke(target, args); }
        catch (TargetInvocationException ex) when (ex.InnerException != null) { throw ex.InnerException; }
    }

    /// <summary>Text with every run of whitespace (a wrapped row's break and indent) read as one space.</summary>
    private static string Flat(string s) => Regex.Replace(s, "\\s+", " ").Trim();

    private static string Strip(string s) => Regex.Replace(s, "\u001b\\[[0-9;?]*[A-Za-z]", "").Replace("\r", "");

    // ================================================================== Fast for new characters

    private static Character NewCharacter(string name)
    {
        var ccs = new CharacterCreationSystem(new TerminalEmulator(new MemoryStream(), new MemoryStream()));
        return (Character)Invoke(ccs, typeof(CharacterCreationSystem), "CreateBaseCharacter", name)!;
    }

    private static PlayerData Serialize(Character hero) => (PlayerData)Invoke(SaveSystem.Instance, typeof(SaveSystem), "SerializePlayer", hero)!;

    private static Character Restore(PlayerData data) => (Character)Invoke(GameEngine.Instance, typeof(GameEngine), "RestorePlayerFromSaveData", data)!;

    // the save file's own options (FileSaveBackend): camelCase names, fields included
    private static readonly JsonSerializerOptions FileJson = new() { PropertyNamingPolicy = JsonNamingPolicy.CamelCase, IncludeFields = true };

    [Fact]
    public void ANewCharacter_StartsOnFast_AndASinglePlayerSaveKeepsIt()
    {
        var hero = NewCharacter("Newbie");
        hero.CombatSpeed.Should().Be(CombatSpeed.Fast, "a new character starts on Fast");

        string json = JsonSerializer.Serialize(Serialize(hero), FileJson);
        json.Should().Contain("\"combatSpeed\":1", "the save file stores the speed");
        Restore(JsonSerializer.Deserialize<PlayerData>(json, FileJson)!).CombatSpeed.Should().Be(CombatSpeed.Fast);
    }

    [Fact]
    public async Task ANewCharacter_Online_KeepsFastThroughTheDatabaseRow()
    {
        var hero = NewCharacter("onlinenew");
        hero.CombatSpeed.Should().Be(CombatSpeed.Fast);
        (await Db.WriteGameData("onlinenew", new SaveGameData { Version = GameConfig.SaveVersion, Player = Serialize(hero) })).Should().BeTrue();
        var read = await Db.ReadGameData("onlinenew");
        read.Should().NotBeNull();
        Restore(read!.Player).CombatSpeed.Should().Be(CombatSpeed.Fast, "the online row keeps the new character's speed");
    }

    [Fact]
    public void AnOldSave_WithNoCombatSpeedField_LoadsNormal_AndAStoredSpeedIsKept()
    {
        // an old single player save file, camelCase, written before the field existed
        var old = JsonSerializer.Deserialize<PlayerData>("{\"name1\":\"Old\",\"name2\":\"Old\",\"level\":7,\"hp\":100,\"maxHP\":100}", FileJson)!;
        Restore(old).CombatSpeed.Should().Be(CombatSpeed.Normal, "a save with no speed is not moved to Fast");
        var pascal = JsonSerializer.Deserialize<PlayerData>("{\"Name1\":\"Old\",\"Name2\":\"Old\",\"Level\":7,\"HP\":100,\"MaxHP\":100}")!;
        Restore(pascal).CombatSpeed.Should().Be(CombatSpeed.Normal);

        foreach (var speed in new[] { CombatSpeed.Normal, CombatSpeed.Fast, CombatSpeed.Instant })
        {
            var saved = JsonSerializer.Deserialize<PlayerData>(
                $"{{\"name1\":\"Kept\",\"name2\":\"Kept\",\"level\":7,\"hp\":100,\"maxHP\":100,\"combatSpeed\":{(int)speed}}}", FileJson)!;
            Restore(saved).CombatSpeed.Should().Be(speed, "an existing character keeps its saved speed ({0})", speed);
        }
    }

    [Fact]
    public async Task AnOnlineRow_WithNoCombatSpeedField_LoadsNormal()
    {
        var hero = NewCharacter("oldonline");
        await Db.WriteGameData("oldonline", new SaveGameData { Version = GameConfig.SaveVersion, Player = Serialize(hero) });

        // the row as an older server wrote it: no combatSpeed in player_data
        string json;
        using (var c = new SqliteConnection($"Data Source={_dbPath}"))
        {
            c.Open();
            using var get = c.CreateCommand();
            get.CommandText = "SELECT player_data FROM players WHERE username = 'oldonline';";
            json = (string)get.ExecuteScalar()!;
            var node = JsonNode.Parse(json)!.AsObject();
            var player = node["player"]!.AsObject();
            player.Remove("combatSpeed").Should().BeTrue("the row carried the field before it was taken out");
            using var put = c.CreateCommand();
            put.CommandText = "UPDATE players SET player_data = @d WHERE username = 'oldonline';";
            put.Parameters.AddWithValue("@d", node.ToJsonString());
            put.ExecuteNonQuery().Should().Be(1);
        }
        var read = await Db.ReadGameData("oldonline");
        read.Should().NotBeNull();
        Restore(read!.Player).CombatSpeed.Should().Be(CombatSpeed.Normal, "an online character with no stored speed stays Normal");
    }

    // ================================================================== R13 the map legend

    /// <summary>A floor of three rooms in a column (five map rows), so the legend runs past the map's last row.</summary>
    private static DungeonFloor ColumnFloor()
    {
        var floor = new DungeonFloor { Level = 6, Theme = DungeonTheme.Catacombs, CurrentRoomId = "r0", EntranceRoomId = "r0" };
        for (int i = 0; i < 3; i++)
            floor.Rooms.Add(new DungeonRoom { Id = $"r{i}", Name = $"Room {i}", Description = "A room.", DangerRating = 1, IsExplored = true, IsCleared = true });
        for (int i = 0; i < 2; i++)
        {
            floor.Rooms[i].Exits[Direction.South] = new RoomExit($"r{i + 1}", "south");
            floor.Rooms[i + 1].Exits[Direction.North] = new RoomExit($"r{i}", "north");
        }
        return floor;
    }

    [Theory]
    [InlineData("en")]
    [InlineData("hu")]
    public async Task TheMapLegend_StandsInOneColumn_UnknownWithTheRest(string lang)
    {
        var prev = GameConfig.Language;
        bool sr = GameConfig.ScreenReaderMode, pacing = Pacing.Disabled;
        string text;
        try
        {
            GameConfig.Language = lang; GameConfig.ScreenReaderMode = false; Pacing.Disabled = true;
            var output = new MemoryStream();
            var term = new TerminalEmulator(new MemoryStream(Encoding.UTF8.GetBytes("\n\n\n")), output);
            var d = new DungeonLocation();
            typeof(BaseLocation).GetField("terminal", F)!.SetValue(d, term);
            typeof(BaseLocation).GetField("currentPlayer", F)!.SetValue(d, new Character { Name1 = "mapper", Name2 = "Mapper", Level = 6, HP = 50, MaxHP = 50, AI = CharacterAI.Human });
            typeof(DungeonLocation).GetField("currentDungeonLevel", F)!.SetValue(d, 6);
            typeof(DungeonLocation).GetField("currentFloor", F)!.SetValue(d, ColumnFloor());
            await (Task)Invoke(d, typeof(DungeonLocation), "ShowDungeonMap")!;
            term.StreamWriterInternal?.Flush();
            text = Strip(Encoding.UTF8.GetString(output.ToArray()));
        }
        finally { GameConfig.Language = prev; GameConfig.ScreenReaderMode = sr; Pacing.Disabled = pacing; }

        var rows = text.Split('\n');
        int Col(string mark, string key)
        {
            string label = Loc.GetIn(lang, key);
            var row = rows.Single(r => r.Contains(mark + label));
            return row.IndexOf(mark + label, StringComparison.Ordinal);
        }
        int at = Col("@ ", "dungeon.map_legend_you");
        var columns = new Dictionary<string, int>
        {
            ["title"] = Col("", "dungeon.map_legend_title"),
            ["cleared"] = Col("# ", "dungeon.map_legend_cleared"),
            ["monsters"] = Col("█ ", "dungeon.map_legend_monsters"),
            ["stairs"] = Col("> ", "dungeon.map_legend_stairs"),
            ["boss"] = Col("B ", "dungeon.map_legend_boss"),
            ["safe"] = Col("· ", "dungeon.map_legend_safe"),
            ["unknown"] = Col("? ", "dungeon.map_legend_unknown"),
        };
        foreach (var (name, col) in columns)
            col.Should().Be(at, "the {0} legend line starts where the @ line does ({1})", name, lang);
        at.Should().BeLessThan(20, "the legend sits beside a one-room-wide map, not 50 columns out");
        foreach (var row in rows) row.Length.Should().BeLessOrEqualTo(79, "every map row fits: [{0}]", row);
    }

    // ================================================================== web sessions

    private sealed class WebScope : IDisposable
    {
        private readonly SessionContext? _saved = SessionContext.Current;
        private readonly string _lang = GameConfig.Language;
        private readonly bool _pacing = Pacing.Disabled;
        private readonly bool _sr = GameConfig.ScreenReaderMode;
        public readonly MemoryStream Stream = new();
        public WebScope(Character hero, string kind = "web")
        {
            Pacing.Disabled = true;
            SessionContext.Current = WebFlagOff200Tests.Session(kind, Stream, hero);
            GameConfig.Language = "en";
            GameConfig.ScreenReaderMode = false;
        }
        public TerminalEmulator Term(string input) => new(new MemoryStream(Encoding.UTF8.GetBytes(input)), Stream);
        public byte[] Bytes(TerminalEmulator t) { t.StreamWriterInternal?.Flush(); return Stream.ToArray(); }
        public void Dispose()
        {
            SessionContext.Current = _saved;
            GameConfig.Language = _lang;
            Pacing.Disabled = _pacing;
            GameConfig.ScreenReaderMode = _sr;
        }
    }

    private static List<JsonElement> Of(byte[] bytes, string package) =>
        WebMenu200Tests.Frames(bytes).Where(f => f.Package == package).Select(f => f.Json).ToList();

    // ================================================================== B10 the room menu keys

    [Fact]
    public void TheRoomMenu_SendsTheKeysTheRoomPrints_EachOneTheGameTakes()
    {
        var hero = WebFlagOff200Tests.Hero(false, false);
        using var s = new WebScope(hero);
        var t = s.Term("");
        var (d, floor) = WebFlagOff200Tests.Dungeon(hero, t);
        Invoke(d, typeof(DungeonLocation), "DisplayLocation");
        var bytes = s.Bytes(t);
        var menu = Of(bytes, "Client.Menu").Single();
        var actions = menu.GetProperty("groups").EnumerateArray().Single(g => g.GetProperty("label").GetString() == Loc.Get("web.menu.actions"))
            .GetProperty("items").EnumerateArray().Select(i => (Key: i.GetProperty("key").GetString()!, Label: i.GetProperty("label").GetString()!)).ToList();

        actions.Should().Contain((("*", Loc.Get("dungeon.inventory"))), "Inventory is sent as the * the room prints");
        actions.Should().Contain((("%", Loc.Get("dungeon.status"))), "Status is sent as the % the room prints");
        actions.Select(a => a.Key).Should().NotContain(new[] { "I", "=" }, "the menu no longer sends a key the room does not print");

        // each action key is the one the room text prints beside its label
        string text = Strip(Encoding.UTF8.GetString(bytes));
        var list = ((System.Collections.IEnumerable)Invoke(d, typeof(DungeonLocation), "RoomActions", floor.GetCurrentRoom()!)!).Cast<object>().ToList();
        string P(object a, string p) => (string)a.GetType().GetProperty(p)!.GetValue(a)!;
        actions.Select(a => a.Key).Should().Equal(list.Select(a => P(a, "DrawKey")), "the menu is RoomActions' printed keys, in order");
        foreach (var a in list.Where(a => (bool)a.GetType().GetProperty("Drawn")!.GetValue(a)!))
            text.Should().Contain("[" + P(a, "DrawKey") + "]", "the room prints {0}", P(a, "Label"));

        // and the game takes each one: a case of the room handler or a global key
        var cases = WebMenu200Tests.CaseLabels("Scripts/Locations/DungeonLocation.cs", "ProcessRoomChoice");
        actions.Select(a => a.Key).Should().OnlyContain(k => cases.Contains(k) || GlobalKeys.Contains(k));
        cases.Should().Contain(new[] { "I", "=" }, "the old keys are still taken when typed");
    }

    // ================================================================== A2 districts

    [Fact]
    public async Task ADistrict_SendsARoomInfoNamingIt_AndLeavingNamesMainStreetAgain()
    {
        var hero = WebFlagOff200Tests.Hero(false, false);
        hero.Level = 12;
        var district = MainStreetLocation.Districts.Single(x => x.Group == MainStreetLocation.StreetGroup.MerchantRow);
        using var s = new WebScope(hero);
        var t = s.Term("R\n");
        var street = new MainStreetLocation();
        typeof(BaseLocation).GetField("terminal", F)!.SetValue(street, t);
        typeof(BaseLocation).GetField("currentPlayer", F)!.SetValue(street, hero);
        bool left = await (Task<bool>)Invoke(street, typeof(MainStreetLocation), "EnterDistrict", district)!;
        left.Should().BeFalse("R goes back to the street");

        var infos = Of(s.Bytes(t), "Room.Info");
        infos.Should().HaveCount(2, "one for the district, one for the street on the way back");
        var at = infos[0];
        at.GetProperty("name").GetString().Should().Be(Loc.Get("main_street.district_merchant_row"));
        at.GetProperty("num").GetString().Should().Be("district:MerchantRow", "a text id, never the street's number 1");
        at.GetProperty("area").GetString().Should().Be("Usurper Reborn");
        var exits = at.GetProperty("exits").EnumerateObject().ToDictionary(p => p.Name, p => p.Value.GetInt32());
        exits.Should().Contain("mainstreet", (int)GameLocation.MainStreet);
        exits.Should().Contain("weaponshop", (int)GameLocation.WeaponShop);

        var back = infos[1];
        back.GetProperty("num").GetInt32().Should().Be((int)GameLocation.MainStreet);
        back.GetProperty("name").GetString().Should().NotBe(Loc.Get("main_street.district_merchant_row"));
    }

    [Fact]
    public async Task ADistrict_SendsNoFrame_WithoutGmcp()
    {
        var hero = WebFlagOff200Tests.Hero(false, false);
        hero.Level = 12;
        var district = MainStreetLocation.Districts.Single(x => x.Group == MainStreetLocation.StreetGroup.MerchantRow);
        using var s = new WebScope(hero, "ssh");
        var t = s.Term("R\n");
        var street = new MainStreetLocation();
        typeof(BaseLocation).GetField("terminal", F)!.SetValue(street, t);
        typeof(BaseLocation).GetField("currentPlayer", F)!.SetValue(street, hero);
        await (Task<bool>)Invoke(street, typeof(MainStreetLocation), "EnterDistrict", district)!;
        WebMenu200Tests.Frames(s.Bytes(t)).Should().BeEmpty("no GMCP was negotiated");
    }

    // ================================================================== B15 loot comparison for a two-hander

    private static int Weapon(string name, WeaponType type, WeaponHandedness hands, int power, EquipmentSlot slot) =>
        EquipmentDatabase.RegisterDynamic(new Equipment { Name = name, Slot = slot, WeaponType = type, Handedness = hands, WeaponPower = power, MinLevel = 1 });

    private static string Compare(Character hero, Item loot, string lang)
    {
        var prev = GameConfig.Language;
        try
        {
            GameConfig.Language = lang;
            var output = new MemoryStream();
            var term = new TerminalEmulator(new MemoryStream(), output);
            CombatEngine.ShowEquipmentComparison(term, loot, hero);
            term.StreamWriterInternal?.Flush();
            string text = Strip(Encoding.UTF8.GetString(output.ToArray()));
            foreach (var row in text.Split('\n')) row.Length.Should().BeLessOrEqualTo(79, "{0} row fits: [{1}]", lang, row);
            return Flat(text);
        }
        finally { GameConfig.Language = prev; }
    }

    private static Character DualWielder()
    {
        var hero = new Character { Name1 = "loot", Name2 = "Looter", Class = CharacterClass.Warrior, Level = 20, HP = 500, MaxHP = 500, AI = CharacterAI.Human };
        hero.EquippedItems[EquipmentSlot.MainHand] = Weapon("Plain Longsword", WeaponType.Sword, WeaponHandedness.OneHanded, 20, EquipmentSlot.MainHand);
        hero.EquippedItems[EquipmentSlot.OffHand] = Weapon("Plain Offhand Sword", WeaponType.Sword, WeaponHandedness.OneHanded, 16, EquipmentSlot.OffHand);
        return hero;
    }

    [Theory]
    [InlineData("en")]
    [InlineData("hu")]
    public void ATwoHanderFromLoot_IsComparedWithBothHands_AndNamesTheOffHand(string lang)
    {
        var hero = DualWielder();
        var loot = new Item { Name = "Blazing Claymore", Type = ObjType.Weapon, Attack = 24, Value = 500 };
        string text = Compare(hero, loot, lang);
        var offHand = hero.GetEquipment(EquipmentSlot.OffHand)!;
        text.Should().Contain(Flat(Loc.GetIn(lang, "combat.compare_attack", 36, 24)), "main hand 20 plus off-hand 16");
        text.Should().Contain(Flat(Loc.GetIn(lang, "combat.downgrade", -12)));
        text.Should().Contain(Flat(Loc.GetIn(lang, "inventory.offhand_comes_off", ItemNames.Display(offHand), 16)));
        text.Should().NotContain(Flat(Loc.GetIn(lang, "combat.upgrade", 4)), "not +4 against the main hand alone");
    }

    [Fact]
    public void AOneHanderFromLoot_IsComparedWithTheMainHandAlone()
    {
        var hero = DualWielder();
        var loot = new Item { Name = "Blazing Longsword", Type = ObjType.Weapon, Attack = 24, Value = 500 };
        string text = Compare(hero, loot, "en");
        text.Should().Contain(Flat(Loc.Get("combat.compare_attack", 20, 24))).And.Contain(Flat(Loc.Get("combat.upgrade", 4)));
        text.Should().NotContain(Flat(Loc.Get("inventory.offhand_comes_off", ItemNames.Display(hero.GetEquipment(EquipmentSlot.OffHand)!), 16)));
    }

    // ================================================================== B18 empty Enter at the loot prompt

    private static async Task<(string choice, string text)> LootPrompt(string input)
    {
        var hero = new Character { Name1 = "loot", Name2 = "Looter", Class = CharacterClass.Warrior, Level = 20, HP = 500, MaxHP = 500, AI = CharacterAI.Human };
        var output = new MemoryStream();
        var term = new TerminalEmulator(new MemoryStream(Encoding.UTF8.GetBytes(input)), output);
        var engine = new CombatEngine(term);
        typeof(CombatEngine).GetField("currentPlayer", F)!.SetValue(engine, hero);
        typeof(CombatEngine).GetField("currentTeammates", F)!.SetValue(engine, new List<Character>());
        var loot = new Item { Name = "Plain Club", Type = ObjType.Weapon, Attack = 5, Value = 10, IsIdentified = true };
        var task = (Task<(string choice, Character selectedCharacter)>)Invoke(engine, typeof(CombatEngine), "GetEquipmentDropInput",
            loot, WebFlagOff200Tests.Foe("Ghoul"), hero, new CombatEngine.LocalizedLines())!;
        var result = await task;
        term.StreamWriterInternal?.Flush();
        return (result.choice, Strip(Encoding.UTF8.GetString(output.ToArray())));
    }

    [Fact]
    public async Task AnEmptyEnter_AtTheLootPrompt_AsksAgainQuietly()
    {
        var (choice, text) = await LootPrompt("\n\n \nT\n");
        choice.Should().Be("T", "the first key typed is taken");
        string invalid = Loc.Get("combat.loot_invalid_choice", "");
        text.Should().NotContain(invalid.Trim().Split('\'')[0], "an empty Enter is not called an invalid choice");
        Regex.Matches(text, Regex.Escape(Loc.Get("ui.your_choice"))).Count.Should().Be(4, "the prompt is asked again for each empty Enter");
        Regex.Matches(text, Regex.Escape(Loc.Get("combat.loot_pass_option"))).Count.Should().Be(1, "the options are not drawn again");
    }

    [Fact]
    public async Task AWrongKey_AtTheLootPrompt_IsStillCalledInvalid()
    {
        var (choice, text) = await LootPrompt("Z\nP\n");
        choice.Should().Be("P");
        text.Should().Contain(Loc.Get("combat.loot_invalid_choice", "Z"));
    }
}
