using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Runtime.CompilerServices;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using FluentAssertions;
using Microsoft.Data.Sqlite;
using Spectre.Console;
using Spectre.Console.Rendering;
using UsurperRemake.Locations;
using UsurperRemake.Server;
using UsurperRemake.Systems;
using Xunit;

namespace UsurperReborn.Tests;

/// <summary>
/// v1.2.6: a name a player chooses from now on (character, child, divine, team, guild) may not hold
/// &lt; &gt; &amp; or a double quote, because names reach web pages. Each entry point is driven through
/// its own screen with a refused name. Names already saved that way still load and play.
/// </summary>
[Collection("SharedGameSingletons")]
public class NameChars126Tests : IDisposable
{
    private const BindingFlags NP = BindingFlags.NonPublic | BindingFlags.Instance;
    private const BindingFlags SNP = BindingFlags.NonPublic | BindingFlags.Static;
    private const string Bad = "Bad<b>";

    private readonly string _path = Path.Combine(Path.GetTempPath(), $"usurper-names126-{Guid.NewGuid():N}.db");
    private readonly SqlSaveBackend _db;
    private readonly object? _oldSaveSystem;
    private readonly GuildSystem? _oldGuild;
    private readonly string _oldLang;
    private readonly List<NPC> _rosterBefore;
    private readonly List<ChildData> _childrenBefore;

    private static FieldInfo SaveSystemField => typeof(SaveSystem).GetField("instance", SNP)!;

    public NameChars126Tests()
    {
        _db = new SqlSaveBackend(_path);
        _oldSaveSystem = SaveSystemField.GetValue(null);
        _oldGuild = GuildSystem.Instance;
        _oldLang = GameConfig.Language;
        GameConfig.Language = "en";
        _rosterBefore = NPCSpawnSystem.Instance.ActiveNPCs.ToList();
        _childrenBefore = FamilySystem.Instance.SerializeChildren();
    }

    public void Dispose()
    {
        var roster = NPCSpawnSystem.Instance.ActiveNPCs;
        roster.Clear();
        roster.AddRange(_rosterBefore);
        FamilySystem.Instance.DeserializeChildren(_childrenBefore);
        SaveSystemField.SetValue(null, _oldSaveSystem);
        typeof(GuildSystem).GetProperty("Instance")!.SetValue(null, _oldGuild);
        GameConfig.Language = _oldLang;
        SessionContext.Current = null;
        SqliteConnection.ClearAllPools();
        try { File.Delete(_path); } catch { }
    }

    private static string Message => Loc.Get("creation.name_bad_chars");

    private static string Plain(MemoryStream output) =>
        Regex.Replace(Encoding.UTF8.GetString(output.ToArray()), "\u001b\\[[0-9;]*[A-Za-z]", "");

    private static (TerminalEmulator Term, MemoryStream Output) Term(params string[] lines)
    {
        var output = new MemoryStream();
        return (new TerminalEmulator(new LineStream(lines), output), output);
    }

    private static string Text(TerminalEmulator term, MemoryStream output)
    {
        term.StreamWriterInternal?.Flush();
        return Plain(output);
    }

    // ---------------- the rule ----------------

    [Theory]
    [InlineData("A<b", true)]
    [InlineData("A>b", true)]
    [InlineData("A&b", true)]
    [InlineData("A\"b", true)]
    [InlineData("O'Brien", false)]
    [InlineData("Anne-Marie", false)]
    [InlineData("Zoltan Kovacs", false)]
    [InlineData("", false)]
    [InlineData(null, false)]
    public void TheRule_IsExactlyTheFourCharacters(string? name, bool refused)
    {
        NameRules.MarkupChars.Should().Be("<>&\"");
        NameRules.HasMarkupChars(name).Should().Be(refused);
    }

    [Theory]
    [InlineData("en")]
    [InlineData("es")]
    [InlineData("fr")]
    [InlineData("hu")]
    [InlineData("it")]
    public void TheMessage_IsTranslated_NamesTheCharacters_AndFits(string lang)
    {
        string msg = Loc.GetIn(lang, "creation.name_bad_chars");
        msg.Should().NotBe("creation.name_bad_chars");
        if (lang != "en") msg.Should().NotBe(Loc.GetIn("en", "creation.name_bad_chars"), "a real translation");
        foreach (char c in NameRules.MarkupChars) msg.Should().Contain(c.ToString());
        ("  " + msg).Length.Should().BeLessThanOrEqualTo(79, "the widest site indents it by two");
    }

    // ---------------- 1. character creation (door, alt, online, NG+ and local first run all call it) ----------------

    private static async Task<(string? Name, string Text)> SelectName(bool allowEmpty, params string[] lines)
    {
        var (term, output) = Term(lines);
        var ccs = new CharacterCreationSystem(term);
        var mi = typeof(CharacterCreationSystem).GetMethod("SelectCharacterName", NP)!;
        string? name = null;
        try { name = await (Task<string>)mi.Invoke(ccs, new object[] { allowEmpty })!; }
        catch (Exception) { /* the scripted input ran out */ }
        return (name, Text(term, output));
    }

    [Theory]
    [InlineData("Ann<a", false)]
    [InlineData("Ann>a", false)]
    [InlineData("Ann&a", true)]
    [InlineData("Ann\"a", true)]
    public async Task CharacterCreation_RefusesTheName_AndAsksAgain(string bad, bool allowEmpty)
    {
        var (name, text) = await SelectName(allowEmpty, bad, "Annabel", "Y");
        text.Should().Contain(Message);
        name.Should().Be("Annabel", "the next name is asked for and taken");
    }

    [Fact]
    public async Task CharacterCreation_AnOrdinaryName_IsTakenAsBefore()
    {
        var (name, text) = await SelectName(false, "O'Brien", "Y");
        name.Should().Be("O'Brien");
        text.Should().NotContain(Message);
    }

    [Fact]
    public async Task NewGamePlus_CreatesThroughTheSameNameScreen()
    {
        // CreateNewGame("") (first run and the NG+ restart) reaches SelectCharacterName with no player name
        var (term, output) = Term(Bad);
        var ccs = new CharacterCreationSystem(term);
        try { await ccs.CreateNewCharacter(""); } catch (Exception) { /* the scripted input ran out */ }
        Text(term, output).Should().Contain(Message);
    }

    // ---------------- 2. local save slot name (becomes Name1 and Name2 without the name screen) ----------------

    [Fact]
    public async Task LocalNewSaveName_IsRefused_AndNoCharacterIsCreated()
    {
        SaveSystem.InitializeWithBackend(_db);
        var existing = new Character { Name1 = "keeper", Name2 = "Keeper", Level = 1, HP = 10, MaxHP = 10, AI = CharacterAI.Human };
        (await SaveSystem.Instance.SaveGame("keeper", existing)).Should().BeTrue();
        var before = SaveSystem.Instance.GetAllPlayerNames().ToList();

        var engine = (GameEngine)RuntimeHelpers.GetUninitializedObject(typeof(GameEngine));
        var (term, output) = Term("N", "Kid & Co", "B");
        typeof(GameEngine).GetField("terminal", NP)!.SetValue(engine, term);
        await (Task)typeof(GameEngine).GetMethod("EnterGame", NP)!.Invoke(engine, null)!;

        Text(term, output).Should().Contain(Message);
        SaveSystem.Instance.GetAllPlayerNames().Should().BeEquivalentTo(before);
    }

    // ---------------- 3. naming a newborn ----------------

    [Fact]
    public async Task Newborn_RefusedName_KeepsTheGeneratedName()
    {
        FamilySystem.Instance.DeserializeChildren(new List<ChildData>());
        var mother = new Character { Name1 = "mum126", Name2 = "Mum126", Sex = CharacterSex.Female, Level = 5, HP = 50, MaxHP = 50, AI = CharacterAI.Human };
        var father = new NPC { ID = "npc_dad126", Id = "npc_dad126", Name1 = "Dad126", Name2 = "Dad126", Sex = CharacterSex.Male, Level = 5, HP = 50, MaxHP = 50 };

        var (term, output) = Term("Kid\"x", "", "", "");
        var intimacy = IntimacySystem.Instance;
        typeof(IntimacySystem).GetField("terminal", NP)!.SetValue(intimacy, term);
        typeof(IntimacySystem).GetField("player", NP)!.SetValue(intimacy, mother);
        try { await (Task)typeof(IntimacySystem).GetMethod("AnnouncePregnancy", NP)!.Invoke(intimacy, new object[] { father, false })!; }
        catch (Exception) { /* the scripted input ran out at the closing key */ }

        Text(term, output).Should().Contain(Message);
        var child = FamilySystem.Instance.GetChildrenOf(mother).Single();
        child.Name.Should().NotContain("Kid");
        NameRules.HasMarkupChars(child.Name).Should().BeFalse();
    }

    // ---------------- 4. renaming a child at home ----------------

    [Fact]
    public async Task ChildRename_RefusedName_KeepsTheName()
    {
        var parent = new Character { Name1 = "par126", Name2 = "Par126", ID = "par126", Level = 5, HP = 50, MaxHP = 50, AI = CharacterAI.Human };
        FamilySystem.Instance.DeserializeChildren(new List<ChildData>
        {
            new ChildData { Name = "Pip Vale", Mother = "Par126", MotherID = "par126", Father = "Fa Vale", FatherID = "other126",
                            Sex = (int)CharacterSex.Male, Age = 5, Location = GameConfig.ChildLocationHome, Named = true,
                            BirthDate = DateTime.Now.AddDays(-5) },
        });

        var home = new HomeLocation();
        var (term, output) = Term("N", "Pip>", "", "");
        typeof(BaseLocation).GetField("terminal", NP)!.SetValue(home, term);
        typeof(BaseLocation).GetField("currentPlayer", NP)!.SetValue(home, parent);
        try { await (Task)typeof(HomeLocation).GetMethod("InteractWithChild", NP)!.Invoke(home, null)!; }
        catch (Exception) { /* the scripted input ran out */ }

        Text(term, output).Should().Contain(Message);
        FamilySystem.Instance.GetChildrenOf(parent).Single().Name.Should().Be("Pip Vale");
    }

    // ---------------- 5. the player's divine name at ascension ----------------

    [Fact]
    public async Task Ascension_RefusedDivineName_IsAskedAgain()
    {
        new GuildSystem(_path);   // registers as the instance (restored in Dispose)
        var player = new Character { Name1 = "asc126", Name2 = "Asc126", Level = 60, HP = 500, MaxHP = 500, AI = CharacterAI.Human };
        var ctx = new SessionContext { Username = "asc126", CharacterKey = "asc126" };
        ctx.InitializeSystems();
        ctx.Engine = (GameEngine)RuntimeHelpers.GetUninitializedObject(typeof(GameEngine));
        ctx.Player = player;
        SessionContext.Current = ctx;

        var (term, output) = Term("Y", "Lord&Lady", "Ascgod", "", "", "", "");
        var offer = typeof(EndingsSystem).GetMethod("OfferImmortality", NP)!;
        bool ascended = await (Task<bool>)offer.Invoke(EndingsSystem.Instance, new object[] { player, EndingType.Savior, term })!;

        Text(term, output).Should().Contain(Message);
        ascended.Should().BeTrue();
        player.DivineName.Should().Be("Ascgod");
    }

    // ---------------- 6. the admin console's divine name ----------------

    [Fact]
    public async Task AdminImmortalize_RefusedDivineName_IsAskedAgain()
    {
        (await _db.RegisterPlayer("adm126", "secret1")).success.Should().BeTrue();
        var pd = new PlayerData { Name1 = "adm126", Name2 = "Adm126", Level = 30, HP = 100, MaxHP = 100 };
        (await _db.WriteGameData("adm126", new SaveGameData { Version = GameConfig.SaveVersion, Player = pd })).Should().BeTrue();

        var (term, output) = Term("1", "Dark\"One", "Admgod", "1", "YES", "");
        var console = new OnlineAdminConsole(term, _db);
        try { await console.ImmortalizePlayer(); } catch (Exception) { /* the scripted input ran out */ }

        Text(term, output).Should().Contain(Message);
        (await _db.ReadGameData("adm126"))!.Player.DivineName.Should().Be("Admgod");
    }

    // ---------------- 7. team names ----------------

    [Fact]
    public async Task TeamCreation_RefusedName_CreatesNoTeam()
    {
        var player = new Character { Name1 = "tm126", Name2 = "Tm126", Level = 5, HP = 50, MaxHP = 50, Gold = 1_000_000, AI = CharacterAI.Human };
        var corner = new TeamCornerLocation();
        var (term, output) = Term("Wolves<", "pw", "", "", "");
        typeof(BaseLocation).GetField("terminal", NP)!.SetValue(corner, term);
        typeof(BaseLocation).GetField("currentPlayer", NP)!.SetValue(corner, player);
        try { await (Task)typeof(TeamCornerLocation).GetMethod("CreateTeam", NP)!.Invoke(corner, null)!; }
        catch (Exception) { /* the scripted input ran out */ }

        Text(term, output).Should().Contain(Message);
        player.Team.Should().BeEmpty();
        player.Gold.Should().Be(1_000_000);
    }

    // ---------------- 8. guild names ----------------

    [Fact]
    public void GuildCreation_RefusedName_CreatesNoGuild()
    {
        var guilds = new GuildSystem(_path);
        guilds.CreateGuild("gl126", "Hall & Co", "Hall & Co").Should().Be(Message);
        guilds.GetPlayerGuild("gl126").Should().BeNull();
        guilds.CreateGuild("gl126", "Hall Co", "Hall Co").Should().BeNull("an ordinary name is created as before");
        guilds.GetPlayerGuild("gl126").Should().BeEquivalentTo("Hall Co", "the guild key is kept lower case");
    }

    // ---------------- 9. the offline save editor ----------------

    private sealed class ScriptedKeys : IAnsiConsoleInput
    {
        private readonly Queue<char> _keys;
        public ScriptedKeys(string text) { _keys = new Queue<char>(text); }
        public bool IsKeyAvailable() => _keys.Count > 0;
        public ConsoleKeyInfo? ReadKey(bool intercept)
        {
            if (_keys.Count == 0) throw new InvalidOperationException("scripted input ran out");
            char c = _keys.Dequeue();
            return c == '\n' ? new ConsoleKeyInfo('\r', ConsoleKey.Enter, false, false, false)
                             : new ConsoleKeyInfo(c, ConsoleKey.A, false, false, false);
        }
        public Task<ConsoleKeyInfo?> ReadKeyAsync(bool intercept, CancellationToken cancellationToken) => Task.FromResult(ReadKey(intercept));
    }

    private sealed class ScriptedConsole : IAnsiConsole
    {
        private readonly IAnsiConsole _inner;
        public ScriptedConsole(IAnsiConsole inner, IAnsiConsoleInput input) { _inner = inner; Input = input; }
        public Profile Profile => _inner.Profile;
        public IAnsiConsoleCursor Cursor => _inner.Cursor;
        public IAnsiConsoleInput Input { get; }
        public IExclusivityMode ExclusivityMode => _inner.ExclusivityMode;
        public RenderPipeline Pipeline => _inner.Pipeline;
        public void Clear(bool home) => _inner.Clear(home);
        public void Write(IRenderable renderable) => _inner.Write(renderable);
    }

    [Fact]
    public void SaveEditor_RefusedDisplayName_KeepsTheName()
    {
        var writer = new StringWriter();
        var inner = AnsiConsole.Create(new AnsiConsoleSettings
        {
            Out = new AnsiConsoleOutput(writer), Interactive = InteractionSupport.Yes, Ansi = AnsiSupport.No, ColorSystem = ColorSystemSupport.NoColors,
        });
        var old = AnsiConsole.Console;
        AnsiConsole.Console = new ScriptedConsole(inner, new ScriptedKeys("Ed<i>\nedkey\n"));
        var p = new PlayerData { Name1 = "edold", Name2 = "Edold" };
        try
        {
            var editor = typeof(GameEngine).Assembly.GetType("UsurperRemake.Editor.PlayerSaveEditor")!;
            try { editor.GetMethod("EditCharacterInfo", SNP)!.Invoke(null, new object[] { p }); }
            catch (TargetInvocationException) { /* the scripted input ran out at the next prompt */ }
        }
        finally { AnsiConsole.Console = old; }

        writer.ToString().Should().Contain(Message);
        p.Name2.Should().Be("Edold");
        p.Name1.Should().Be("edkey", "an ordinary name is taken as before");
    }

    // ---------------- 10. the marriage surname (unchanged: its allow list already drops the four) ----------------

    [Fact]
    public void MarriageSurname_AlreadyDropsTheFourCharacters()
    {
        var sanitize = typeof(MarriageSurnameHelper).GetMethod("SanitizeSurname", SNP)!;
        ((string)sanitize.Invoke(null, new object[] { "Va<l>e&\"s" })!).Should().Be("Vales");
    }

    // ---------------- existing names are kept ----------------

    private const string Old = "Old <b>&\"Name";

    [Fact]
    public async Task AnExistingCharacter_WithTheCharacters_LoadsEntersALocation_AndSaves()
    {
        SaveSystem.InitializeWithBackend(_db);
        var hero = new Character { Name1 = "oldkey126", Name2 = Old, Level = 3, HP = 40, MaxHP = 40, AI = CharacterAI.Human };
        (await SaveSystem.Instance.SaveGame("oldkey126", hero)).Should().BeTrue();

        var loaded = MenuKeysNeedEnterPref1115Tests.Restore((await _db.ReadGameData("oldkey126"))!.Player);
        loaded.Name2.Should().Be(Old);

        var street = new MainStreetLocation();
        var (term, output) = Term("", "", "");
        typeof(BaseLocation).GetField("terminal", NP)!.SetValue(street, term);
        typeof(BaseLocation).GetField("currentPlayer", NP)!.SetValue(street, loaded);
        typeof(MainStreetLocation).GetMethod("DisplayLocation", NP)!.Invoke(street, null);
        Text(term, output).Should().NotBeEmpty();

        (await SaveSystem.Instance.SaveGame("oldkey126", loaded)).Should().BeTrue();
        (await _db.ReadGameData("oldkey126"))!.Player.Name2.Should().Be(Old);
    }

    [Fact]
    public void AnExistingChild_WithTheCharacters_Loads()
    {
        string name = "Kid<i>&\" Vale";   // the surname follows the father, as a load expects
        FamilySystem.Instance.DeserializeChildren(new List<ChildData>
        {
            new ChildData { Name = name, Mother = "M", MotherID = "m126", Father = "Fa Vale", FatherID = "f126", Sex = (int)CharacterSex.Female,
                            Age = 4, Location = GameConfig.ChildLocationHome, Named = true, BirthDate = DateTime.Now.AddDays(-4) },
        });
        var back = JsonSerializer.Deserialize<List<ChildData>>(JsonSerializer.Serialize(FamilySystem.Instance.SerializeChildren()))!;
        FamilySystem.Instance.DeserializeChildren(back);
        FamilySystem.Instance.AllChildren.Single().Name.Should().Be(name);
    }

    [Fact]
    public async Task AnExistingNpc_WithTheCharacters_Loads()
    {
        string name = "Npc <x>&\"Oak";
        NPCSpawnSystem.Instance.ActiveNPCs.Clear();
        var npc = new NPC { ID = "npc_old126", Id = "npc_old126", Name1 = name, Name2 = name, Level = 10, HP = 100, MaxHP = 100 };
        npc.EnsureSystemsInitialized();
        NPCSpawnSystem.Instance.ActiveNPCs.Add(npc);
        var json = new JsonSerializerOptions { PropertyNamingPolicy = JsonNamingPolicy.CamelCase, IncludeFields = true };
        var data = JsonSerializer.Deserialize<List<NPCData>>(JsonSerializer.Serialize(OnlineStateManager.SerializeCurrentNPCs(), json), json)!;

        NPCSpawnSystem.Instance.ActiveNPCs.Clear();
        await GameEngine.Instance.RestoreNPCs(data);
        NPCSpawnSystem.Instance.ActiveNPCs.Should().ContainSingle(n => n.Name2 == name);
    }
}
