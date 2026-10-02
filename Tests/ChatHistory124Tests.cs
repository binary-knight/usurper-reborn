using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Runtime.CompilerServices;
using System.Text;
using System.Threading.Tasks;
using FluentAssertions;
using UsurperRemake.Server;
using UsurperRemake.Systems;
using UsurperRemake.UI;
using Xunit;

namespace UsurperReborn.Tests;

/// <summary>
/// 1.2.4: /history keeps the last 50 chat lines each account was shown, in server memory only,
/// for 15 minutes after its session ends so a reconnect keeps them. Lines are recorded where they
/// are delivered, for the recipient and the sender; muted channels and system lines stay out.
/// </summary>
[Collection("SharedGameSingletons")]
public class ChatHistory124Tests : IDisposable
{
    private const BindingFlags NP = BindingFlags.NonPublic | BindingFlags.Instance;
    private const BindingFlags SNP = BindingFlags.NonPublic | BindingFlags.Static;

    private readonly MudServer? _oldServer;
    private readonly GuildSystem? _oldGuild;
    private readonly string _oldLang;
    private readonly bool _oldSr;
    private DateTime _now = new DateTime(2026, 10, 1, 12, 0, 0, DateTimeKind.Utc);
    private readonly MudServer _server;

    public ChatHistory124Tests()
    {
        _oldServer = MudServer.Instance;
        _oldGuild = GuildSystem.Instance;
        _oldLang = GameConfig.Language;
        _oldSr = GameConfig.ScreenReaderMode;
        GameConfig.Language = "en";
        GameConfig.ScreenReaderMode = false;
        ChatHistoryStore.ResetForTests(() => _now);
        _server = NewServer();
        SetServerInstance(_server);
        new RoomRegistry();
    }

    public void Dispose()
    {
        SetServerInstance(_oldServer);
        typeof(GuildSystem).GetProperty("Instance")!.SetValue(null, _oldGuild);
        GameConfig.Language = _oldLang;
        GameConfig.ScreenReaderMode = _oldSr;
        ChatHistoryStore.ResetForTests();
    }

    // ---- fixtures ----

    private static MudServer NewServer()
    {
        var server = (MudServer)RuntimeHelpers.GetUninitializedObject(typeof(MudServer));
        typeof(MudServer).GetField("<ActiveSessions>k__BackingField", NP)!
            .SetValue(server, new ConcurrentDictionary<string, PlayerSession>());
        return server;
    }

    private static void SetServerInstance(MudServer? server) =>
        typeof(MudServer).GetField("_instance", SNP)!.SetValue(null, server);

    private PlayerSession Online(string username, string characterName, params string[] muted)
    {
        var s = (PlayerSession)RuntimeHelpers.GetUninitializedObject(typeof(PlayerSession));
        typeof(PlayerSession).GetField("<Username>k__BackingField", NP)!.SetValue(s, username);
        typeof(PlayerSession).GetField("<IncomingMessages>k__BackingField", NP)!.SetValue(s, new ConcurrentQueue<string>());
        s.ActiveCharacterName = characterName;
        s.IsInGame = true;

        var ctx = (SessionContext)RuntimeHelpers.GetUninitializedObject(typeof(SessionContext));
        var engine = (GameEngine)RuntimeHelpers.GetUninitializedObject(typeof(GameEngine));
        engine.CurrentPlayer = new Player
        {
            Name1 = characterName, Name2 = characterName,
            MutedChannels = new HashSet<string>(muted, StringComparer.OrdinalIgnoreCase),
        };
        ctx.Engine = engine;
        ctx.Terminal = new TerminalEmulator(new MemoryStream(), new MemoryStream());
        typeof(PlayerSession).GetField("<Context>k__BackingField", NP)!.SetValue(s, ctx);

        _server.ActiveSessions[username] = s;
        return s;
    }

    private static (TerminalEmulator term, MemoryStream output) Terminal()
    {
        var output = new MemoryStream();
        return (new TerminalEmulator(new MemoryStream(), output), output);
    }

    private static void Chat(string handler, string username, string text)
    {
        var (term, _) = Terminal();
        var m = typeof(MudChatSystem).GetMethod(handler, SNP)!;
        var r = m.Invoke(null, new object[] { username, text, term });
        if (r is Task t) t.GetAwaiter().GetResult();
    }

    private static string Output(MemoryStream output) => UIHelper.StripAnsi(Encoding.UTF8.GetString(output.ToArray()));

    private static string History(string username, string args = "", string keys = "B\r\nB\r\nB\r\n")
    {
        // The pager reads B (back) when a history runs over one page.
        var output = new MemoryStream();
        var term = new TerminalEmulator(new MemoryStream(Encoding.UTF8.GetBytes(keys)), output);
        var m = typeof(MudChatSystem).GetMethod("HandleHistory", SNP)!;
        ((Task)m.Invoke(null, new object[] { username, args, term })!).GetAwaiter().GetResult();
        return Output(output);
    }

    private static IReadOnlyList<ChatHistoryEntry> Ring(string key, string? channel = null) =>
        ChatHistoryStore.Instance.ReadChatHistory(key, channel);

    private void InGuild(string guild, params string[] members)
    {
        var g = (GuildSystem)RuntimeHelpers.GetUninitializedObject(typeof(GuildSystem));
        var cache = new ConcurrentDictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        foreach (var m in members) cache[m] = guild;
        typeof(GuildSystem).GetField("membershipCache", NP)!.SetValue(g, cache);
        typeof(GuildSystem).GetProperty("Instance")!.SetValue(null, g);
    }

    // ---- each channel, sender and recipient ----

    [Theory]
    [InlineData("HandleSay", "say", "hello there")]
    [InlineData("HandleEmote", "emote", "waves hello")]
    [InlineData("HandleGossip", "gossip", "hello realm")]
    [InlineData("HandleShout", "shout", "hello realm")]
    public void EachChannel_IsRecorded_ForTheSenderAndTheRecipient(string handler, string channel, string text)
    {
        Online("acct1", "Mira");
        Online("acct2", "Tovin");
        RoomRegistry.Instance!.PlayerEntered(GameLocation.TheInn, _server.ActiveSessions["acct1"]);
        RoomRegistry.Instance!.PlayerEntered(GameLocation.TheInn, _server.ActiveSessions["acct2"]);

        Chat(handler, "acct1", text);

        Ring("acct1").Should().ContainSingle().Which.Should().Match<ChatHistoryEntry>(e => e.Channel == channel && e.Text.Contains(text));
        var got = Ring("acct2").Should().ContainSingle().Subject;
        got.Channel.Should().Be(channel);
        got.Text.Should().Contain(text).And.Contain("Mira").And.NotContain("\u001b");
        got.TimeUtc.Should().Be(_now);
    }

    [Fact]
    public void Tell_IsRecorded_ForTheSenderAndTheRecipient()
    {
        Online("acct1", "Mira");
        Online("acct2", "Tovin");

        Chat("HandleTell", "acct1", "Tovin meet me at the inn");

        Ring("acct1", "tell").Should().ContainSingle().Which.Text.Should().Contain("Tovin").And.Contain("meet me at the inn");
        Ring("acct2", "tell").Should().ContainSingle().Which.Text.Should().Be("Mira tells you: meet me at the inn");
    }

    [Fact]
    public void GuildChat_IsRecorded_ForTheSenderAndTheMembers()
    {
        Online("acct1", "Mira");
        Online("acct2", "Tovin");
        Online("acct3", "Bea");
        InGuild("wolves", "acct1", "acct2");

        Chat("HandleGuildChat", "acct1", "raid tonight");

        Ring("acct1", "guild").Should().ContainSingle().Which.Text.Should().Contain("raid tonight");
        Ring("acct2", "guild").Should().ContainSingle().Which.Text.Should().Contain("Mira").And.Contain("raid tonight");
        Ring("acct3").Should().BeEmpty();
    }

    [Fact]
    public void SystemLines_AreNotRecorded()
    {
        var a = Online("acct1", "Mira");
        var b = Online("acct2", "Tovin");
        RoomRegistry.Instance!.PlayerEntered(GameLocation.TheInn, a);
        RoomRegistry.Instance!.PlayerEntered(GameLocation.TheInn, b);

        _server.BroadcastToAll("Mira has left the realm.", excludeUsername: "acct1");
        RoomRegistry.Instance!.BroadcastToRoom(GameLocation.TheInn, "A bard begins to play.");
        RoomRegistry.Instance!.PlayerDisconnected(a);

        b.IncomingMessages.Should().HaveCount(3, "the lines were delivered");
        Ring("acct1").Should().BeEmpty();
        Ring("acct2").Should().BeEmpty();
    }

    [Fact]
    public void SayAtHome_IsRecordedOnlyForTheSpeaker()
    {
        var a = Online("acct1", "Mira");
        var b = Online("acct2", "Tovin");
        RoomRegistry.Instance!.PlayerEntered(GameLocation.Home, a);
        RoomRegistry.Instance!.PlayerEntered(GameLocation.Home, b);

        Chat("HandleSay", "acct1", "hello there");

        Ring("acct1").Should().ContainSingle();
        Ring("acct2").Should().BeEmpty("a line not delivered is not recorded");
    }

    // ---- a tell reaches only its two parties ----

    [Fact]
    public void Tell_LandsOnlyInTheTwoPartiesRings()
    {
        Online("acct1", "Mira");
        Online("acct2", "Tovin");
        Online("acct3", "Bea");
        Online("acct4", "Cal");

        Chat("HandleTell", "acct1", "Tovin a secret");

        Ring("acct1").Should().ContainSingle();
        Ring("acct2").Should().ContainSingle();
        Ring("acct3").Should().BeEmpty();
        Ring("acct4").Should().BeEmpty();
    }

    // ---- muted channels ----

    [Theory]
    [InlineData("HandleGossip", "gossip", "hello realm")]
    [InlineData("HandleShout", "shout", "hello realm")]
    [InlineData("HandleTell", "tell", "Tovin hello")]
    public void AMutedChannel_IsNotRecorded_ForThePlayerWhoMutedIt(string handler, string channel, string text)
    {
        Online("acct1", "Mira");
        Online("acct2", "Tovin", channel);
        Online("acct3", "Bea");

        Chat(handler, "acct1", text);

        Ring("acct2").Should().BeEmpty();
        if (channel != "tell") Ring("acct3").Should().ContainSingle("a player who did not mute it still records it");
    }

    [Fact]
    public void GuildChat_Muted_IsNotRecorded_ForTheMutingMember()
    {
        Online("acct1", "Mira");
        Online("acct2", "Tovin", "guild");
        InGuild("wolves", "acct1", "acct2");

        Chat("HandleGuildChat", "acct1", "raid tonight");

        Ring("acct2").Should().BeEmpty();
    }

    [Fact]
    public void TheSendersOwnLine_OnAChannelTheyMuted_IsNotRecorded()
    {
        Online("acct1", "Mira", "gossip");
        Online("acct2", "Tovin");

        Chat("HandleGossip", "acct1", "hello realm");

        Ring("acct1").Should().BeEmpty();
        Ring("acct2").Should().ContainSingle();
    }

    // ---- the cap ----

    [Fact]
    public void The51stLine_DropsTheOldest()
    {
        for (int i = 1; i <= 51; i++)
            ChatHistoryStore.Instance.RecordChatLine("acct1", "gossip", $"line {i}", incoming: true);

        var ring = Ring("acct1");
        ring.Should().HaveCount(ChatHistoryStore.Capacity).And.HaveCount(50);
        ring[0].Text.Should().Be("line 2");
        ring[^1].Text.Should().Be("line 51");
    }

    // ---- the expiry ----

    private async Task<PlayerSession> Connect(string key)
    {
        var s = (PlayerSession)RuntimeHelpers.GetUninitializedObject(typeof(PlayerSession));
        typeof(PlayerSession).GetField("<Username>k__BackingField", NP)!.SetValue(s, key);
        (await _server.RegisterSessionAsync(key, s)).Should().BeTrue();
        return s;
    }

    [Fact]
    public async Task AReconnectWithin15Minutes_KeepsTheRing()
    {
        var first = await Connect("acct1");
        ChatHistoryStore.Instance.RecordChatLine("acct1", "tell", "Tovin tells you: hi", incoming: true);
        _server.EndConnection("acct1", first);

        _now = _now.AddMinutes(14).AddSeconds(59);
        await Connect("acct1");

        Ring("acct1").Should().ContainSingle().Which.Text.Should().Be("Tovin tells you: hi");
    }

    [Fact]
    public async Task AReconnectAfter15Minutes_StartsEmpty()
    {
        var first = await Connect("acct1");
        ChatHistoryStore.Instance.RecordChatLine("acct1", "tell", "Tovin tells you: hi", incoming: true);
        _server.EndConnection("acct1", first);

        _now = _now.AddMinutes(15);
        await Connect("acct1");

        Ring("acct1").Should().BeEmpty();
    }

    [Fact]
    public async Task AnExpiredRing_IsDroppedWhenAnotherAccountConnects()
    {
        var first = await Connect("acct1");
        ChatHistoryStore.Instance.RecordChatLine("acct1", "tell", "Tovin tells you: hi", incoming: true);
        _server.EndConnection("acct1", first);

        _now = _now.AddMinutes(16);
        await Connect("acct2");

        Ring("acct1").Should().BeEmpty();
    }

    [Fact]
    public async Task TheKickedConnectionsLateCleanup_DoesNotStartTheWindow_ForTheLiveSession()
    {
        var old = await Connect("acct1");
        await _server.KickStaleSessionAsync("acct1", old, "reconnect");
        await Connect("acct1");
        ChatHistoryStore.Instance.RecordChatLine("acct1", "tell", "Tovin tells you: hi", incoming: true);
        _server.EndConnection("acct1", old);   // the old connection's finally runs after the new login

        _now = _now.AddMinutes(30);
        await Connect("acct2");

        Ring("acct1").Should().ContainSingle("acct1 is still online");
    }

    // ---- the filter ----

    [Fact]
    public void TheFilter_ShowsOnlyThatChannel_OldestFirst()
    {
        var s = ChatHistoryStore.Instance;
        s.RecordChatLine("acct1", "gossip", "[Gossip] Tovin: alpha", incoming: true);
        s.RecordChatLine("acct1", "tell", "Tovin tells you: bravo", incoming: true);
        s.RecordChatLine("acct1", "gossip", "[Gossip] Bea: charlie", incoming: true);

        var all = History("acct1");
        all.IndexOf("alpha", StringComparison.Ordinal).Should().BeLessThan(all.IndexOf("bravo", StringComparison.Ordinal));
        all.IndexOf("bravo", StringComparison.Ordinal).Should().BeLessThan(all.IndexOf("charlie", StringComparison.Ordinal));

        var gossip = History("acct1", "gossip");
        gossip.Should().Contain("alpha").And.Contain("charlie").And.NotContain("bravo");
        History("acct1", "gos").Should().Contain("charlie").And.NotContain("bravo");

        var tells = History("acct1", "tell");
        tells.Should().Contain("bravo").And.NotContain("alpha").And.NotContain("charlie");

        History("acct1", "say").Should().Contain(Loc.Get("chat.history_empty"));
        History("acct1", "nonsense").Should().Contain(Loc.Get("chat.history_usage")).And.NotContain("alpha");
    }

    [Fact]
    public void AnEmptyHistory_ShowsTheNoMessagesLine()
    {
        History("acct9").Should().Contain(Loc.Get("chat.history_empty"));
    }

    // ---- width ----

    [Theory]
    [InlineData("en")]
    [InlineData("hu")]
    public void HistoryOutput_FitsIn79Columns_WithALongNameAndALongMessage(string lang)
    {
        GameConfig.Language = lang;
        string name = new string('W', 30);
        name.Length.Should().Be(GameConfig.MaxNameLength);
        string longMessage = string.Join(' ', Enumerable.Repeat("the quick brown fox jumps over the lazy dog", 6))
            + " " + new string('x', 140);
        var s = ChatHistoryStore.Instance;
        s.RecordChatLine("acct1", "gossip", $"{Loc.Get("chat.gossip_you", longMessage)}", incoming: false);
        s.RecordChatLine("acct1", "tell", $"{name} tells you: {longMessage}", incoming: true);
        s.RecordChatLine("acct1", "tell", Loc.Get("chat.you_tell", name, longMessage), incoming: false);
        s.RecordChatLine("acct1", "guild", $"{Loc.Get("guild.chat_label")} {name}: {longMessage}", incoming: true);

        var paged = History("acct1", "", "N\r\nN\r\nN\r\nB\r\n");
        paged.Should().Contain(Loc.Get("base.quest_pager_nav")).And.Contain(Loc.Get("guild.chat_label") + " " + name, "the pager reaches the last page");
        var text = paged + "\n" + History("acct1", "gossip") + "\n" + History("acct1", "say")
            + "\n" + History("acct1", "bogus");
        var lines = text.Replace("\r", "").Split('\n');
        lines.Should().OnlyContain(l => l.Length <= 79, "every /history row fits 79 columns");
        text.Should().Contain(new string('x', 20)).And.Contain(name);

        foreach (var key in new[] { "chat.history_header", "chat.history_empty", "chat.history_usage", "chat.history_hint_one" })
            ("  " + Loc.Get(key)).Length.Should().BeLessThanOrEqualTo(79, key);
        ("  " + Loc.Get("chat.history_header_channel", "gossip")).Length.Should().BeLessThanOrEqualTo(79);
        ("  " + Loc.Get("chat.history_hint_many", 50)).Length.Should().BeLessThanOrEqualTo(79);
        (" " + Loc.Get("base.help_history_cmd").PadRight(20) + " " + Loc.Get("base.help_history")).Length.Should().BeLessThanOrEqualTo(78);
    }

    // ---- the missed-message hint ----

    [Fact]
    public void TheHint_ShowsOnce_WithTheRightCount_Plural()
    {
        Online("acct1", "Mira");
        Online("acct2", "Tovin");
        Chat("HandleGossip", "acct1", "one");
        Chat("HandleGossip", "acct1", "two");
        Chat("HandleGossip", "acct1", "three");

        MudChatSystem.TakeRedrawHint("acct2").Should().Be(Loc.Get("chat.history_hint_many", 3));
        MudChatSystem.TakeRedrawHint("acct2").Should().BeNull("the hint shows once");
        MudChatSystem.TakeRedrawHint("acct1").Should().BeNull("the sender's own lines are not new to them");
    }

    [Fact]
    public void TheHint_Singular_AndClearedByReadingHistory()
    {
        Online("acct1", "Mira");
        Online("acct2", "Tovin");
        Chat("HandleTell", "acct1", "Tovin hi");

        MudChatSystem.TakeRedrawHint("acct2").Should().Be(Loc.Get("chat.history_hint_one"));

        Chat("HandleTell", "acct1", "Tovin again");
        History("acct2");
        MudChatSystem.TakeRedrawHint("acct2").Should().BeNull("reading /history clears the count");
    }

    // ---- screen reader ----

    [Fact]
    public void ScreenReaderOutput_IsPlain_OneLinePerMessage()
    {
        GameConfig.ScreenReaderMode = true;
        string longMessage = string.Join(' ', Enumerable.Repeat("the quick brown fox jumps over the lazy dog", 4));
        ChatHistoryStore.Instance.RecordChatLine("acct1", "tell", $"Tovin tells you: {longMessage}", incoming: true);
        ChatHistoryStore.Instance.RecordChatLine("acct1", "gossip", "[Gossip] Bea: hi", incoming: true);

        var rows = MudChatSystem.RenderHistoryRows(Ring("acct1"), screenReader: true);
        rows.Should().HaveCount(2);
        rows[0].Text.Should().Be($"12:00 Tovin tells you: {longMessage}");
        rows[1].Text.Should().Be("12:00 [Gossip] Bea: hi");

        var shown = History("acct1").Replace("\r", "").Split('\n').Where(l => l.Length > 0).ToList();
        shown.Should().Equal(Loc.Get("chat.history_header"), rows[0].Text, rows[1].Text);
        shown.Should().OnlyContain(l => !l.StartsWith(" ") && !l.Contains('['  + "12"));
    }

    // ---- privacy: nothing saves, logs or forwards the ring ----

    internal static readonly string[] PrivacyFiles =
    {
        "Scripts/Systems/SaveSystem.cs",
        "Scripts/Systems/SqlSaveBackend.cs",
        "Scripts/Systems/DebugLogger.cs",
        "Scripts/Systems/DiscordBridge.cs",
    };

    private static string RepoRoot()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir != null && !File.Exists(Path.Combine(dir.FullName, "usurper-reloaded.csproj")))
            dir = dir.Parent;
        dir.Should().NotBeNull("the tests run inside the repository");
        return dir!.FullName;
    }

    [Fact]
    public void Privacy_NoSaveLogOrDiscordCode_ReferencesTheChatHistory()
    {
        var names = new List<string> { nameof(ChatHistoryStore), nameof(ChatHistoryEntry) };
        var members = new[]
        {
            nameof(ChatHistoryStore.RecordChatLine), nameof(ChatHistoryStore.ReadChatHistory),
            nameof(ChatHistoryStore.MarkChatHistoryViewed), nameof(ChatHistoryStore.TakeUnseenChatCount),
            nameof(ChatHistoryStore.ChatSessionStarted), nameof(ChatHistoryStore.ChatSessionEnded),
            nameof(MudChatSystem.RecordDelivered), nameof(MudChatSystem.RenderHistoryRows),
        };
        names.AddRange(members);

        string root = RepoRoot();
        var hits = new List<string>();
        foreach (var rel in PrivacyFiles)
        {
            var path = Path.Combine(root, rel);
            File.Exists(path).Should().BeTrue(rel);
            var lines = File.ReadAllLines(path);
            for (int i = 0; i < lines.Length; i++)
                foreach (var n in names)
                    if (lines[i].Contains(n, StringComparison.Ordinal))
                        hits.Add($"{rel}:{i + 1}: {n}");
        }
        hits.Should().BeEmpty("the chat history lives in memory only");
    }
}
