using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Runtime.CompilerServices;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using FluentAssertions;
using Microsoft.Data.Sqlite;
using UsurperRemake.Server;
using UsurperRemake.Systems;
using UsurperRemake.UI;
using Xunit;

namespace UsurperReborn.Tests;

/// <summary>
/// 1.2.4 immortal bugs: (1) the Pantheon Status shows the believer experience the daily reset pays;
/// (2) a renounce clears every follower, NPC and player (in the game and in saved games), and tells
/// each player once; (3) an alt refused at ascension keeps its throne, team and guild, while a main
/// still abdicates and leaves; (4) the alt slot earned by ascending survives the renounce's
/// delete-and-recreate of the character.
/// </summary>
[Collection("SharedGameSingletons")]
public class ImmortalBugs124Tests : IDisposable
{
    private const BindingFlags NP = BindingFlags.NonPublic | BindingFlags.Instance;
    private const BindingFlags SNP = BindingFlags.NonPublic | BindingFlags.Static;
    private const string God = "Ibvessa";

    private readonly string _path = Path.Combine(Path.GetTempPath(), $"usurper-immortal124-{Guid.NewGuid():N}.db");
    private readonly SqlSaveBackend _db;
    private readonly object? _oldSaveSystem;
    private readonly MudServer? _oldServer;
    private readonly GuildSystem? _oldGuild;
    private readonly King? _oldKing;
    private readonly List<MonarchRecord> _oldHistory;
    private readonly List<NPC> _addedNpcs = new();
    private readonly string _oldLang;

    private static FieldInfo SaveSystemField => typeof(SaveSystem).GetField("instance", SNP)!;

    public ImmortalBugs124Tests()
    {
        _db = new SqlSaveBackend(_path);
        _oldSaveSystem = SaveSystemField.GetValue(null);
        _oldServer = MudServer.Instance;
        _oldGuild = GuildSystem.Instance;
        _oldKing = CastleLocation.GetCurrentKing();
        _oldHistory = CastleLocation.GetMonarchHistory().ToList();
        _oldLang = GameConfig.Language;
        GameConfig.Language = "en";
    }

    public void Dispose()
    {
        foreach (var n in _addedNpcs) NPCSpawnSystem.Instance.ActiveNPCs.Remove(n);
        SaveSystemField.SetValue(null, _oldSaveSystem);
        typeof(MudServer).GetField("_instance", SNP)!.SetValue(null, _oldServer);
        typeof(GuildSystem).GetProperty("Instance")!.SetValue(null, _oldGuild);
        CastleLocation.SetKing(_oldKing);
        CastleLocation.SetMonarchHistory(_oldHistory);
        GameConfig.Language = _oldLang;
        SessionContext.Current = null;
        SqliteConnection.ClearAllPools();
        try { File.Delete(_path); } catch { }
    }

    // ---------------- fixtures ----------------

    private NPC Believer(string id, string god)
    {
        var npc = new NPC { ID = id, Name1 = id, Name2 = id, Level = 10, WorshippedGod = god };
        NPCSpawnSystem.Instance.ActiveNPCs.Add(npc);
        _addedNpcs.Add(npc);
        return npc;
    }

    private static Character Immortal(string name, int level) => new Character
    {
        Name1 = name, Name2 = name, Level = 60, HP = 500, MaxHP = 500, AI = CharacterAI.Human,
        IsImmortal = true, DivineName = God, GodLevel = level, GodExperience = 0, GodAlignment = "Light",
    };

    private static string Plain(MemoryStream output) =>
        Regex.Replace(Encoding.UTF8.GetString(output.ToArray()), "\u001b\\[[0-9;]*[A-Za-z]", "");

    private static (PantheonLocation Pantheon, MemoryStream Output) Pantheon(Character player, params string[] lines)
    {
        var pantheon = new PantheonLocation();
        var output = new MemoryStream();
        var term = new TerminalEmulator(new LineStream(lines), output);
        typeof(BaseLocation).GetField("terminal", NP)!.SetValue(pantheon, term);
        typeof(BaseLocation).GetField("currentPlayer", NP)!.SetValue(pantheon, player);
        return (pantheon, output);
    }

    private Task Save(string key, PlayerData p) =>
        _db.WriteGameData(key, new SaveGameData
        {
            Version = GameConfig.SaveVersion,
            Player = p,
            StorySystems = new StorySystemsData { PlayerGods = new Dictionary<string, string>() }
        });

    private static PlayerData Mortal(string name, string god, string lang) =>
        new PlayerData { Name1 = name, Name2 = name, Level = 12, WorshippedGod = god, Language = lang };

    private object? Scalar(string sql)
    {
        using var conn = new SqliteConnection($"Data Source={_path}");
        conn.Open();
        using var cmd = conn.CreateCommand();
        cmd.CommandText = sql;
        return cmd.ExecuteScalar();
    }

    private List<(string To, string Text)> DivineMail()
    {
        using var conn = new SqliteConnection($"Data Source={_path}");
        conn.Open();
        using var cmd = conn.CreateCommand();
        cmd.CommandText = "SELECT to_player, message FROM messages WHERE message_type = 'divine' ORDER BY id;";
        using var r = cmd.ExecuteReader();
        var list = new List<(string, string)>();
        while (r.Read()) list.Add((r.GetString(0), r.GetString(1)));
        return list;
    }

    private MudServer Server()
    {
        var server = (MudServer)RuntimeHelpers.GetUninitializedObject(typeof(MudServer));
        typeof(MudServer).GetField("<ActiveSessions>k__BackingField", NP)!
            .SetValue(server, new ConcurrentDictionary<string, PlayerSession>());
        typeof(MudServer).GetField("_instance", SNP)!.SetValue(null, server);
        return server;
    }

    private static PlayerSession Online(MudServer server, string username, Character player, string lang)
    {
        var s = (PlayerSession)RuntimeHelpers.GetUninitializedObject(typeof(PlayerSession));
        typeof(PlayerSession).GetField("<Username>k__BackingField", NP)!.SetValue(s, username);
        typeof(PlayerSession).GetField("<IncomingMessages>k__BackingField", NP)!.SetValue(s, new ConcurrentQueue<string>());
        s.ActiveCharacterName = player.Name2;
        s.IsInGame = true;
        var ctx = new SessionContext { Username = username, CharacterKey = username, Language = lang };
        var engine = (GameEngine)RuntimeHelpers.GetUninitializedObject(typeof(GameEngine));
        engine.CurrentPlayer = player;
        ctx.Engine = engine;
        typeof(PlayerSession).GetField("<Context>k__BackingField", NP)!.SetValue(s, ctx);
        server.ActiveSessions[username] = s;
        return s;
    }

    private static string[] Messages(PlayerSession s) =>
        s.IncomingMessages.Select(m => Regex.Replace(m, "\u001b\\[[0-9;]*[A-Za-z]", "")).ToArray();

    // ---------------- 1. believer experience: Status equals the payout ----------------

    [Theory]
    [InlineData(4, 3)]
    [InlineData(7, 1)]
    public async Task TheStatusScreen_ShowsTheBelieverExperienceTheDailyResetPays(int believers, int godLevel)
    {
        var god = Immortal("Ibstatus", godLevel);
        for (int i = 0; i < believers; i++) Believer($"npc_ib_status_{godLevel}_{i}", God);

        var daily = typeof(DailySystemManager).GetMethod("ProcessGodDailyMaintenance", NP)!;
        daily.Invoke(DailySystemManager.Instance, new object?[] { god, null });
        long paid = god.GodExperience;
        paid.Should().Be((long)believers * godLevel * 2, "the payout is believers x level x 2; no balance change");

        var (pantheon, output) = Pantheon(Immortal("Ibstatus", godLevel), "");
        await (Task)typeof(PantheonLocation).GetMethod("ShowDivineStatus", NP)!.Invoke(pantheon, null)!;
        string text = Plain(output);
        var row = text.Split('\n').Single(l => l.Contains(Loc.Get("pantheon.daily_exp_label").Trim()));
        var shown = Regex.Match(row, @"\+(\d+)").Groups[1].Value;
        long.Parse(shown).Should().Be(paid, "the Status row reads the same rule as the payout");
        (long.Parse(shown) / ((long)believers * godLevel)).Should().Be(2, "the multiplier shown is the x2 paid");
    }

    // ---------------- 2. renounce clears every follower ----------------

    [Fact]
    public async Task ARenounce_ClearsNpcAndPlayerFollowers_InTheGameAndInSavedGames_AndTellsEachPlayerOnce()
    {
        SaveSystem.InitializeWithBackend(_db);
        var server = Server();

        var npc = Believer("npc_ib_follower", God);
        var other = Believer("npc_ib_other", "Elsewyn");

        // a player not online: only the saved game
        await Save("ib_offline", Mortal("IbOffline", God, "hu"));
        // a player online: the session in memory and its saved game
        await Save("ib_online", Mortal("IbOnline", God, "en"));
        var live = new Character { Name1 = "ib_online", Name2 = "IbOnline", Level = 12, AI = CharacterAI.Human, WorshippedGod = God };
        var session = Online(server, "ib_online", live, "en");
        // a player of another god, and an immortal (gods are not followers)
        await Save("ib_elsewhere", Mortal("IbElsewhere", "Elsewyn", "en"));

        int cleared = await PantheonLocation.ClearFollowersOfAsync(God);

        cleared.Should().Be(2);
        npc.WorshippedGod.Should().BeEmpty();
        other.WorshippedGod.Should().Be("Elsewyn");
        live.WorshippedGod.Should().BeEmpty();
        GodRegistry.GetWorshippedGod(live).Should().BeNull();
        (await _db.ReadGameData("ib_offline"))!.Player.WorshippedGod.Should().BeEmpty("the saved game is cleared too");
        (await _db.ReadGameData("ib_online"))!.Player.WorshippedGod.Should().BeEmpty();
        (await _db.ReadGameData("ib_elsewhere"))!.Player.WorshippedGod.Should().Be("Elsewyn");
        (await _db.ReadGameData("ib_offline"))!.Player.Level.Should().Be(12, "only the god field is written");

        // told once each: the online player in the session, the offline one by mail in their language
        Messages(session).Should().Equal(
            "  " + Loc.GetIn("en", "pantheon.follower_god_renounced", God),
            "  " + Loc.GetIn("en", "pantheon.follower_now_godless"));
        var mail = DivineMail();
        mail.Should().ContainSingle("only the player who was not online gets mail");
        mail[0].To.Should().Be(_db.MailAddressForKey("ib_offline"));
        mail[0].Text.Should().Be(Loc.GetIn("hu", "pantheon.follower_god_renounced", God) + " " + Loc.GetIn("hu", "pantheon.follower_now_godless"));
    }

    [Fact]
    public void TheRenounce_ClearsTheFollowers_WhileTheDivineNameIsStillSet()
    {
        string body = Body(Source("Locations", "PantheonLocation.cs"), "private async Task<bool> RenounceImmortality()");
        int clear = body.IndexOf("await ClearFollowersOfAsync(currentPlayer.DivineName);", StringComparison.Ordinal);
        clear.Should().BeGreaterThan(0);
        body.IndexOf("currentPlayer.DivineName = \"\";", StringComparison.Ordinal).Should().BeGreaterThan(clear);
    }

    [Theory]
    [InlineData("en")]
    [InlineData("hu")]
    public void TheFollowerNotice_FitsTheScreen(string lang)
    {
        string longest = new string('W', 30);   // a divine name is at most 30 characters
        ("  " + Loc.GetIn(lang, "pantheon.follower_god_renounced", longest)).Length.Should().BeLessThanOrEqualTo(79);
        ("  " + Loc.GetIn(lang, "pantheon.follower_now_godless")).Length.Should().BeLessThanOrEqualTo(79);
    }

    // ---------------- 3. ascension: the alt check comes first ----------------

    private async Task<(Character Player, bool Ascended, string Text)> Ascend(string account, string characterKey, params string[] lines)
    {
        var guilds = new GuildSystem(_path);   // registers as the instance (restored in Dispose)
        guilds.CreateGuild(account, "ibhall", "Ib Hall").Should().BeNull();

        var player = new Character
        {
            Name1 = account, Name2 = "Ibking", Level = 60, HP = 500, MaxHP = 500, AI = CharacterAI.Human,
            King = true, NobleTitle = "King", Team = "Ib Wolves", TeamPW = "pw",
        };
        CastleLocation.SetKing(King.CreateNewKing("Ibking", CharacterAI.Human, CharacterSex.Male));

        var ctx = new SessionContext { Username = account, CharacterKey = characterKey };
        ctx.InitializeSystems();
        ctx.Engine = (GameEngine)RuntimeHelpers.GetUninitializedObject(typeof(GameEngine));
        ctx.Player = player;
        SessionContext.Current = ctx;

        var output = new MemoryStream();
        var term = new TerminalEmulator(new LineStream(lines), output);
        var offer = typeof(EndingsSystem).GetMethod("OfferImmortality", NP)!;
        bool ascended = await (Task<bool>)offer.Invoke(EndingsSystem.Instance, new object[] { player, EndingType.Savior, term })!;
        return (player, ascended, Plain(output));
    }

    [Fact]
    public async Task ARefusedAlt_KeepsItsThrone_ItsTeam_AndItsGuild()
    {
        var (alt, ascended, text) = await Ascend("ibacct", "ibacct" + GameConfig.AltCharacterSuffix, "Y", "Altgodname", "", "", "");
        try
        {
            ascended.Should().BeFalse();
            text.Should().Contain(Loc.Get("ending.immortal_alt_blocked"));
            alt.IsImmortal.Should().BeFalse();
            alt.King.Should().BeTrue("the refusal comes before the abdication");
            alt.NobleTitle.Should().Be("King");
            CastleLocation.GetCurrentKing()!.Name.Should().Be("Ibking");
            alt.Team.Should().Be("Ib Wolves", "the refusal comes before the team quit");
            GuildSystem.Instance!.GetPlayerGuild("ibacct").Should().Be("ibhall", "the refusal comes before the guild removal");
            text.Should().NotContain(Loc.Get("ending.immortal_choose_name"), "an alt is refused before it names a god");
        }
        finally { SessionContext.Current = null; }
    }

    [Fact]
    public async Task AMain_StillAbdicates_LeavesItsTeamAndGuild_AndAscends()
    {
        var (main, ascended, _) = await Ascend("ibmain", "ibmain", "Y", "Ibmaingod", "", "", "", "");
        try
        {
            ascended.Should().BeTrue();
            main.IsImmortal.Should().BeTrue();
            main.DivineName.Should().Be("Ibmaingod");
            main.HasEarnedAltSlot.Should().BeTrue();
            main.King.Should().BeFalse("a main still abdicates");
            CastleLocation.GetCurrentKing()?.Name.Should().NotBe("Ibking");
            main.Team.Should().BeEmpty("a main still leaves its team");
            GuildSystem.Instance!.GetPlayerGuild("ibmain").Should().BeNull("a main still leaves its guild");
        }
        finally { SessionContext.Current = null; }
    }

    // ---------------- 4. the alt slot survives the renounce ----------------

    [Fact]
    public async Task AfterAscendThenRenounce_TheNewCharacter_HasTheAltSlot()
    {
        SaveSystem.InitializeWithBackend(_db);
        const string key = "ibrenounce";
        var ctx = new SessionContext { Username = key, CharacterKey = key };
        ctx.InitializeSystems();
        var engine = new GameEngine();
        ctx.Engine = engine;
        SessionContext.Current = ctx;
        try
        {
            // ascended (the ascension sets the slot), saved as the main
            var god = Immortal(key, 2);
            god.HasEarnedAltSlot = true;
            engine.CurrentPlayer = god;
            ctx.Player = god;
            ctx.Story.CurrentCycle = 2;
            (await SaveSystem.Instance.SaveGame(key, god)).Should().BeTrue();

            // the renounce, as the Pantheon runs it
            var (pantheon, _) = Pantheon(god, "YES", God, "");
            bool renounced = await (Task<bool>)typeof(PantheonLocation).GetMethod("RenounceImmortality", NP)!.Invoke(pantheon, null)!;
            renounced.Should().BeTrue();
            engine.PendingNewGamePlus.Should().BeTrue();

            // the restart: the saves are deleted and a new character is created (Quick Start)
            var output = new MemoryStream();
            typeof(GameEngine).GetField("terminal", NP)!.SetValue(engine, new TerminalEmulator(new LineStream(new[] { "Q", "1", "1", "" }), output));
            try { await engine.BeginNewLifeAsync(key); }
            catch (Exception) { /* the scripted input ends during the opening story, after the first save */ }

            engine.CurrentPlayer.Should().NotBeSameAs(god, "a new character was created");
            engine.CurrentPlayer!.IsImmortal.Should().BeFalse();
            var saved = (await _db.ReadGameData(key))!.Player;
            saved.IsImmortal.Should().BeFalse("the old save was replaced by the new life");
            saved.HasEarnedAltSlot.Should().BeTrue("the slot earned by ascending persists through the renounce");
            GameEngine.AltSlotUnlocked(saved.IsImmortal, saved.HasEarnedAltSlot, saved.Level).Should().BeTrue("the alt slot stays open");
        }
        finally { SessionContext.Current = null; }
    }

    [Fact]
    public void AnAltSlot_IsOpenOnlyAfterAnAscensionOrTheLevel()
    {
        GameEngine.AltSlotUnlocked(false, false, 1).Should().BeFalse();
        GameEngine.AltSlotUnlocked(false, true, 1).Should().BeTrue("ascended once, then renounced");
        GameEngine.AltSlotUnlocked(true, false, 1).Should().BeTrue();
    }

    // ---------------- helpers ----------------

    private static string Source(string folder, string file)
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir != null && !Directory.Exists(Path.Combine(dir.FullName, "Scripts"))) dir = dir.Parent;
        return File.ReadAllText(Path.Combine(dir!.FullName, "Scripts", folder, file));
    }

    private static string Body(string src, string signature)
    {
        int start = src.IndexOf(signature, StringComparison.Ordinal);
        start.Should().BeGreaterThanOrEqualTo(0, $"{signature} must exist");
        var next = new Regex(@"\n    (private|public|internal|protected) ").Match(src, start + signature.Length);
        return next.Success ? src.Substring(start, next.Index - start) : src.Substring(start);
    }
}
