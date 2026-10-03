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
using Microsoft.Data.Sqlite;
using UsurperRemake.BBS;
using UsurperRemake.Server;
using UsurperRemake.Systems;
using UsurperRemake.UI;
using Xunit;

namespace UsurperReborn.Tests;

/// <summary>
/// v1.2.5: the MUD server side in each player's language. Chat lines (say, shout, tell, gossip, the
/// mute toggles), /title, /accept and /deny, spectators, groups and their notices, the room and realm
/// departure lines, disconnect and shutdown notices, the gods' notices, the login gate and SSH relay
/// menus and the BBS adapter prompts. A line to one player is in that player's language; a broadcast
/// is rendered for each recipient; the text a player typed is never translated. Chat command words,
/// the AUTH protocol (OK, ERR:) and the group leave reasons stay English. Every changed row fits 79
/// columns in English and Hungarian with a 30-character name.
/// </summary>
[Collection("SharedGameSingletons")]
public class OnlineServer125Tests : IDisposable
{
    private const BindingFlags NP = BindingFlags.NonPublic | BindingFlags.Instance;
    private const BindingFlags SNP = BindingFlags.NonPublic | BindingFlags.Static;
    private const int MaxWidth = 79;

    // GameConfig.MaxNameLength (30) characters.
    private const string LongName = "Aranyszivu Hosszunevu Kalandor";

    private static readonly string[] AllLanguages = { "en", "es", "fr", "hu", "it" };

    private readonly MudServer? _oldServer;
    private readonly GroupSystem? _oldGroups;
    private readonly GuildSystem? _oldGuild;
    private readonly string _oldLang;
    private readonly bool _oldSr;
    private readonly MudServer _server;
    private readonly GroupSystem _groups;

    public OnlineServer125Tests()
    {
        _oldServer = MudServer.Instance;
        _oldGroups = GroupSystem.Instance;
        _oldGuild = GuildSystem.Instance;
        _oldLang = GameConfig.Language;
        _oldSr = GameConfig.ScreenReaderMode;
        GameConfig.Language = "en";
        GameConfig.ScreenReaderMode = false;
        ChatHistoryStore.ResetForTests();
        _server = (MudServer)RuntimeHelpers.GetUninitializedObject(typeof(MudServer));
        typeof(MudServer).GetField("<ActiveSessions>k__BackingField", NP)!
            .SetValue(_server, new ConcurrentDictionary<string, PlayerSession>());
        typeof(MudServer).GetField("_instance", SNP)!.SetValue(null, _server);
        typeof(GuildSystem).GetProperty("Instance")!.SetValue(null, null);
        new RoomRegistry();
        _groups = new GroupSystem();
    }

    public void Dispose()
    {
        typeof(MudServer).GetField("_instance", SNP)!.SetValue(null, _oldServer);
        typeof(GroupSystem).GetProperty("Instance")!.SetValue(null, _oldGroups);
        typeof(GuildSystem).GetProperty("Instance")!.SetValue(null, _oldGuild);
        SessionContext.Current = null;
        GameConfig.Language = _oldLang;
        GameConfig.ScreenReaderMode = _oldSr;
        ChatHistoryStore.ResetForTests();
    }

    // ---------- fixtures ----------

    private PlayerSession Online(string username, string name, string lang, int level = 10, string team = "Wolves")
    {
        var s = (PlayerSession)RuntimeHelpers.GetUninitializedObject(typeof(PlayerSession));
        typeof(PlayerSession).GetField("<Username>k__BackingField", NP)!.SetValue(s, username);
        typeof(PlayerSession).GetField("<IncomingMessages>k__BackingField", NP)!.SetValue(s, new ConcurrentQueue<string>());
        typeof(PlayerSession).GetField("<Spectators>k__BackingField", NP)!.SetValue(s, new List<PlayerSession>());
        typeof(PlayerSession).GetField("<SnoopedBy>k__BackingField", NP)!.SetValue(s, new List<PlayerSession>());
        typeof(PlayerSession).GetField("_server", NP)!.SetValue(s, _server);
        s.ActiveCharacterName = name;
        s.IsInGame = true;

        var ctx = (SessionContext)RuntimeHelpers.GetUninitializedObject(typeof(SessionContext));
        var engine = (GameEngine)RuntimeHelpers.GetUninitializedObject(typeof(GameEngine));
        engine.CurrentPlayer = new Player
        {
            Name1 = name, Name2 = name, Level = level, Team = team,
            MutedChannels = new HashSet<string>(StringComparer.OrdinalIgnoreCase),
        };
        ctx.Engine = engine;
        ctx.Language = lang;
        typeof(SessionContext).GetField("<Username>k__BackingField", NP)!.SetValue(ctx, username);
        ctx.Terminal = new TerminalEmulator(new MemoryStream(), new MemoryStream());
        typeof(PlayerSession).GetField("<Context>k__BackingField", NP)!.SetValue(s, ctx);

        _server.ActiveSessions[username] = s;
        return s;
    }

    private sealed class Screen
    {
        public TerminalEmulator Term = null!;
        public MemoryStream Output = new();
        public string Text => UIHelper.StripAnsi(Encoding.UTF8.GetString(Output.ToArray()));
        public List<string> Rows => Text.Replace("\r", "").Split('\n').Where(r => r.Length > 0).ToList();
    }

    private static Screen NewScreen(string keys = "")
    {
        var s = new Screen();
        s.Term = new TerminalEmulator(new MemoryStream(Encoding.UTF8.GetBytes(keys)), s.Output);
        return s;
    }

    /// <summary>Runs a MudChatSystem handler as `username`, whose own screen reads `lang`.</summary>
    private static Screen Chat(string lang, string handler, string username, string? args = null)
    {
        var screen = NewScreen();
        var prev = GameConfig.Language;
        try
        {
            GameConfig.Language = lang;
            var m = typeof(MudChatSystem).GetMethod(handler, SNP)!;
            var r = m.Invoke(null, args == null ? new object[] { username, screen.Term } : new object[] { username, args, screen.Term });
            if (r is Task t) t.GetAwaiter().GetResult();
        }
        finally { GameConfig.Language = prev; }
        return screen;
    }

    private static List<string> Got(PlayerSession s)
    {
        var rows = new List<string>();
        while (s.IncomingMessages.TryDequeue(out var m)) rows.Add(UIHelper.StripAnsi(m));
        return rows;
    }

    private static string L(string lang, string key, params object[] args) => Loc.GetIn(lang, key, args);

    private static void Fits(IEnumerable<string> rows, string why)
    {
        foreach (var r in rows)
            UIHelper.VisibleLength(r).Should().BeLessThanOrEqualTo(MaxWidth, $"{why}: \"{r}\"");
    }

    private static string Squash(string s) => System.Text.RegularExpressions.Regex.Replace(s, "\\s+", " ").Trim();

    private static void Capture(string name, string text)
    {
        var dir = Environment.GetEnvironmentVariable("USURPER_EVIDENCE_DIR");
        if (!string.IsNullOrEmpty(dir)) File.WriteAllText(Path.Combine(dir, name), text);
    }

    private static string Src(string path)
    {
        var dir = AppContext.BaseDirectory;
        while (dir != null && !File.Exists(Path.Combine(dir, "usurper-reloaded.csproj"))) dir = Path.GetDirectoryName(dir);
        return File.ReadAllText(Path.Combine(dir!, path));
    }

    private static readonly string[] EnglishKeyed =
    {
        "says:", "shouts:", "tells you", "[Gossip]", "has left the realm", "has disconnected",
        "has joined", "group invite", "spectat", "Your group", "Usage:", "SERVER SHUTDOWN", "the gods",
    };

    private static void NoEnglish(IEnumerable<string> rows, string why)
    {
        foreach (var r in rows)
            foreach (var e in EnglishKeyed)
                r.Should().NotContain(e, why);
    }

    [Fact]
    public void LongName_IsTheLongestName()
    {
        LongName.Length.Should().Be(GameConfig.MaxNameLength);
    }

    [Fact]
    public void EveryNewKey_IsInAllFiveLanguages_AndTranslated()
    {
        var keys = new[]
        {
            "chat.says", "chat.shouts", "chat.tells_you", "chat.gossip_line", "chat.tell_muted", "chat.usage_say",
            "chat.mute_again_hint", "chat.unmute_hint", "chat.group_invited_you", "chat.group_invite_howto",
            "chat.group_usage", "chat.group_disbanded", "group.member_left", "group.disbanded_reason",
            "group.reason_leader_fell", "mud.left_realm", "mud.room_disconnected", "mud.shutdown_in_reason",
            "mud.idle_warning_many", "mud.gods_frozen", "mud.kicked", "auth.relay_title", "auth.err_prefix",
            "auth.server_unavailable", "ui.invalid_choice_retry", "ui.number_between", "ui.enter_valid_number",
        };
        foreach (var k in keys)
            foreach (var lang in AllLanguages)
                Loc.HasIn(lang, k).Should().BeTrue($"{k} in {lang}");
        foreach (var k in keys.Where(k => k != "auth.err_prefix" && k != "auth.relay_title"))
            Loc.GetIn("hu", k).Should().NotBe(Loc.GetIn("en", k), $"{k} is translated in hu");
    }

    // ---------- chat lines: each recipient in their own language ----------

    [Fact]
    public void Say_ReachesEachListener_InTheirLanguage_WithTheMessageAsTyped()
    {
        var a = Online("acct1", "Mira", "en");
        var b = Online("acct2", "Tovin", "en");
        var c = Online("acct3", LongName, "hu");
        foreach (var s in new[] { a, b, c }) RoomRegistry.Instance!.PlayerEntered(GameLocation.TheInn, s);

        Chat("en", "HandleSay", "acct1", "hello there");

        Got(b).Should().ContainSingle().Which.Should().Be("  Mira says: hello there");
        var hu = Got(c).Should().ContainSingle().Subject;
        hu.Should().Be("  " + L("hu", "chat.says", "Mira", "hello there")).And.Contain("mondja").And.Contain("hello there");
        NoEnglish(new[] { hu }, "the hu listener");
        ChatHistoryStore.Instance.ReadChatHistory("acct3", "say").Single().Text.Should().Contain("mondja");
    }

    [Fact]
    public void Say_FromAHungarianPlayer_ReachesAnEnglishListener_InEnglish()
    {
        var a = Online("acct1", LongName, "hu");
        var b = Online("acct2", "Tovin", "en");
        RoomRegistry.Instance!.PlayerEntered(GameLocation.TheInn, a);
        RoomRegistry.Instance!.PlayerEntered(GameLocation.TheInn, b);

        var own = Chat("hu", "HandleSay", "acct1", "szia");

        own.Text.Should().Contain(L("hu", "chat.you_say", "szia"));
        var line = Got(b).Single();
        line.Should().Be($"  {LongName} says: szia");
        Fits(new[] { line }, "say frame with a 30 character name");
    }

    [Theory]
    [InlineData("HandleShout", "shout", "chat.shouts")]
    [InlineData("HandleGossip", "gossip", "chat.gossip_line")]
    public void ShoutAndGossip_ReachEachPlayer_InTheirLanguage_AndHonourMutes(string handler, string channel, string key)
    {
        Online("acct1", LongName, "en");
        var en = Online("acct2", "Tovin", "en");
        var hu = Online("acct3", "Bea", "hu");
        var muted = Online("acct4", "Cal", "hu");
        muted.Context!.Engine!.CurrentPlayer!.MutedChannels.Add(channel);

        Chat("en", handler, "acct1", "hi all");

        Got(en).Single().Should().Be("  " + L("en", key, LongName, "hi all"));
        var huLine = Got(hu).Single();
        huLine.Should().Be("  " + L("hu", key, LongName, "hi all"));
        NoEnglish(new[] { huLine }, "hu " + channel);
        Got(muted).Should().BeEmpty("the channel is muted");
        ChatHistoryStore.Instance.ReadChatHistory("acct3", channel).Single().Text.Should().Be(L("hu", key, LongName, "hi all"));
        Fits(new[] { huLine }, channel + " frame");
    }

    [Fact]
    public void Gossip_InEnglish_ReadsAsBefore()
    {
        Online("acct1", "Mira", "en");
        var b = Online("acct2", "Tovin", "en");
        Chat("en", "HandleGossip", "acct1", "hello realm");
        Got(b).Single().Should().Be("  [Gossip] Mira: hello realm");
    }

    [Fact]
    public void Tell_ReachesTheRecipient_InTheRecipientsLanguage()
    {
        Online("acct1", "Mira", "en");
        var hu = Online("acct2", "Tovin", "hu");

        var own = Chat("en", "HandleTell", "acct1", "Tovin meet me at the inn");

        own.Text.Should().Contain("You tell Tovin: meet me at the inn");
        var line = Got(hu).Single();
        line.Should().Be("  " + L("hu", "chat.tells_you", "Mira", "meet me at the inn"));
        line.Should().Contain("üzeni neked").And.NotContain("tells you");
        ChatHistoryStore.Instance.ReadChatHistory("acct2", "tell").Single().Text.Should().Be(L("hu", "chat.tells_you", "Mira", "meet me at the inn"));
    }

    [Fact]
    public void Tell_FromAHungarianSender_ReachesAnEnglishRecipient_InEnglish()
    {
        Online("acct1", "Mira", "hu");
        var en = Online("acct2", "Tovin", "en");

        var own = Chat("hu", "HandleTell", "acct1", "Tovin szia");

        own.Text.Should().Contain(L("hu", "chat.you_tell", "Tovin", "szia"));
        Got(en).Single().Should().Be("  Mira tells you: szia");
    }

    [Theory]
    [InlineData("en")]
    [InlineData("hu")]
    public void Tell_ToAMutedPlayer_IsInTheSendersLanguage_AndFits(string lang)
    {
        Online("acct1", "Mira", lang);
        var t = Online("acct2", LongName, "en");
        t.Context!.Engine!.CurrentPlayer!.MutedChannels.Add("tell");

        var own = Chat(lang, "HandleTell", "acct1", $"{LongName} hi");

        string joined = string.Join(" ", own.Rows.Select(r => r.Trim()));
        joined.Should().Be(L(lang, "chat.tell_muted", LongName));
        Fits(own.Rows, "tell muted");
        Got(t).Should().BeEmpty();
        Capture($"online-tell-muted-{lang}.txt", own.Text);
    }

    [Theory]
    [InlineData("en", "HandleShout", "/shout")]
    [InlineData("hu", "HandleShout", "/shout")]
    [InlineData("en", "HandleGossip", "/gossip")]
    [InlineData("hu", "HandleGossip", "/gossip")]
    [InlineData("en", "HandleTell", "/tell")]
    [InlineData("hu", "HandleTell", "/tell")]
    public void MuteToggle_IsInThePlayersLanguage_KeepsTheCommand_AndFits(string lang, string handler, string command)
    {
        Online("acct1", LongName, lang);

        var muteScreen = Chat(lang, handler, "acct1", "");
        var unmuteScreen = Chat(lang, handler, "acct1", "");

        foreach (var screen in new[] { muteScreen, unmuteScreen })
        {
            screen.Text.Should().Contain(command);
            Fits(screen.Rows, $"{handler} toggle {lang}");
            if (lang == "hu")
            {
                screen.Text.Should().NotContain("type the command alone").And.NotContain("Usage");
                screen.Text.Should().Contain("Használat");
            }
        }
        string usageKey = handler == "HandleShout" ? "chat.usage_shout" : handler == "HandleGossip" ? "chat.usage_gossip" : "chat.usage_tell";
        Squash(string.Join(" ", muteScreen.Rows)).Should().Contain(Squash(L(lang, "chat.unmute_hint", L(lang, usageKey))));
        Squash(string.Join(" ", unmuteScreen.Rows)).Should().Contain(Squash(L(lang, "chat.mute_again_hint", L(lang, usageKey))));
        Capture($"online-mute-{handler}-{lang}.txt", muteScreen.Text + unmuteScreen.Text);
    }

    [Fact]
    public void SayUsage_AndTitle_AreInThePlayersLanguage()
    {
        Online("acct1", "Mira", "hu");
        RoomRegistry.Instance!.PlayerEntered(GameLocation.TheInn, _server.ActiveSessions["acct1"]);

        Chat("hu", "HandleSay", "acct1", "").Text.Should().Contain(L("hu", "chat.usage_say")).And.Contain("/say").And.NotContain("Say what");
        Chat("hu", "HandleTitle", "acct1", "the Bold").Text.Should().Contain(L("hu", "chat.title_set").Trim()).And.Contain("the Bold");
        Chat("hu", "HandleTitle", "acct1", "").Text.Should().Contain(L("hu", "chat.title_cleared"));
    }

    // ---------- accept, deny, spectators ----------

    [Theory]
    [InlineData("en")]
    [InlineData("hu")]
    public void AcceptAndDeny_AreInThePlayersLanguage_AndFit(string lang)
    {
        var inviter = Online("acct1", LongName, "en");
        var me = Online("acct2", "Tovin", lang);
        var rows = new List<string>();

        me.PendingGroupInvite = new GroupInvite { Inviter = inviter };
        rows.AddRange(Chat(lang, "HandleAccept", "acct2").Rows);
        me.PendingGroupInvite = new GroupInvite { Inviter = inviter };
        rows.AddRange(Chat(lang, "HandleDeny", "acct2").Rows);
        me.PendingSpectateRequest = new SpectateRequest { Requester = inviter };
        rows.AddRange(Chat(lang, "HandleAccept", "acct2").Rows);
        me.PendingSpectateRequest = new SpectateRequest { Requester = inviter };
        rows.AddRange(Chat(lang, "HandleDeny", "acct2").Rows);

        string all = string.Join(" ", rows.Select(r => r.Trim()));
        foreach (var k in new[] { "chat.accepted_group_invite", "chat.denied_group_invite", "chat.accepted_spectate", "chat.denied_spectate" })
            all.Should().Contain(L(lang, k, LongName));
        Fits(rows, "accept and deny");
        if (lang == "hu") NoEnglish(rows, "hu accept/deny");
    }

    [Fact]
    public void Spectators_ListAndRemoval_AreInEachPlayersLanguage()
    {
        var owner = Online("acct1", "Mira", "hu");
        var specEn = Online("acct2", "Tovin", "en");
        var specHu = Online("acct3", LongName, "hu");

        Chat("hu", "HandleListSpectators", "acct1").Text.Should().Contain(L("hu", "chat.no_spectators"));
        owner.Spectators.Add(specEn);
        owner.Spectators.Add(specHu);
        var list = Chat("hu", "HandleListSpectators", "acct1");
        list.Text.Should().Contain(L("hu", "chat.spectators_header")).And.Contain(LongName);

        var kick = Chat("hu", "HandleKickAllSpectators", "acct1");
        kick.Text.Should().Contain(L("hu", "chat.spectators_removed"));
        Got(specEn).Single().Should().Be("  * Mira has ended the spectator session.");
        var hu = Got(specHu).Single();
        hu.Should().Be("  * " + L("hu", "chat.spectator_session_ended", "Mira"));
        NoEnglish(new[] { hu }, "hu spectator");
    }

    // ---------- groups ----------

    [Fact]
    public void GroupInfo_IsInThePlayersLanguage()
    {
        Online("acct1", LongName, "hu");
        var none = Chat("hu", "HandleGroup", "acct1", "");
        none.Text.Should().Contain(L("hu", "chat.group_usage")).And.Contain("/group").And.Contain(L("hu", "chat.group_same_team"));
        Fits(none.Rows, "group usage hu");
        Fits(Chat("en", "HandleGroup", "acct1", "").Rows, "group usage en");

        Online("acct2", "Tovin", "en", level: 42);
        var g = _groups.CreateGroup("acct1");
        _groups.AddMember(g, "acct2");
        g.IsInDungeon = true;
        g.CurrentFloor = 7;
        var info = Chat("hu", "HandleGroup", "acct1", "");
        info.Text.Should().Contain(L("hu", "chat.group_header")).And.Contain(L("hu", "chat.group_leader_tag"))
            .And.Contain(L("hu", "chat.group_member_level", 42)).And.Contain(L("hu", "chat.group_members", 2, GameConfig.GroupMaxSize))
            .And.Contain(L("hu", "chat.group_in_dungeon", 7));
        info.Text.Should().NotContain("[Leader]").And.NotContain("members").And.NotContain("Status:");
        Fits(info.Rows, "group info hu");
        Capture("online-group-info-hu.txt", info.Text);
    }

    public static IEnumerable<object[]> Refusals()
    {
        foreach (var lang in new[] { "en", "hu" })
        {
            yield return new object[] { lang, "lowlevel" };
            yield return new object[] { lang, "offline" };
            yield return new object[] { lang, "spectating" };
            yield return new object[] { lang, "ingroup" };
            yield return new object[] { lang, "noteam" };
            yield return new object[] { lang, "otherteam" };
            yield return new object[] { lang, "targetlow" };
            yield return new object[] { lang, "full" };
            yield return new object[] { lang, "dungeon" };
            yield return new object[] { lang, "pending" };
            yield return new object[] { lang, "follower" };
        }
    }

    [Theory]
    [MemberData(nameof(Refusals))]
    public void GroupInviteRefusals_AreInTheLeadersLanguage_AndFit(string lang, string case_)
    {
        var me = Online("acct1", "Mira", lang, level: case_ == "lowlevel" ? 2 : 10, team: case_ == "noteam" ? "" : "Wolves");
        var target = Online("acct2", LongName, "en", level: case_ == "targetlow" ? 2 : 10, team: case_ == "otherteam" ? "Ravens" : "Wolves");
        string expect = "";
        switch (case_)
        {
            case "lowlevel": expect = L(lang, "chat.group_min_level", GameConfig.GroupMinLevel); break;
            case "offline": expect = L(lang, "chat.group_target_offline", "Nobody"); break;
            case "spectating": target.IsSpectating = true; expect = L(lang, "chat.group_target_spectating", LongName); break;
            case "ingroup": var other = Online("acct3", "Cal", "en"); _groups.AddMember(_groups.CreateGroup("acct3"), "acct2"); expect = L(lang, "chat.group_target_in_group", LongName); break;
            case "noteam": expect = L(lang, "chat.group_need_team"); break;
            case "otherteam": expect = L(lang, "chat.group_not_your_team", LongName, "Wolves"); break;
            case "targetlow": expect = L(lang, "chat.group_target_min_level", LongName, GameConfig.GroupMinLevel); break;
            case "full":
                var g = _groups.CreateGroup("acct1");
                for (int i = 0; i < GameConfig.GroupMaxSize - 1; i++) g.MemberUsernames.Add("x" + i);
                expect = L(lang, "chat.group_full", GameConfig.GroupMaxSize, GameConfig.GroupMaxSize); break;
            case "dungeon": _groups.CreateGroup("acct1").IsInDungeon = true; expect = L(lang, "chat.group_no_invite_in_dungeon"); break;
            case "pending": target.PendingGroupInvite = new GroupInvite { Inviter = target }; expect = L(lang, "chat.group_target_pending", LongName); break;
            case "follower": me.IsGroupFollower = true; expect = L(lang, "chat.group_only_leader_invites"); break;
        }

        var screen = Chat(lang, "HandleGroup", "acct1", case_ == "offline" ? "Nobody" : LongName);

        string.Join(" ", screen.Rows.Select(r => r.Trim())).Should().Be(expect);
        Fits(screen.Rows, $"group refusal {case_} {lang}");
        if (lang == "hu") NoEnglish(screen.Rows, "hu refusal");
    }

    [Fact]
    public async Task GroupInvite_EachPlayerReadsTheirOwnLanguage_AndAcceptStillParses()
    {
        var leader = Online("acct1", LongName, "en");
        var target = Online("acct2", "Tovin", "hu");
        var member = Online("acct3", "Bea", "hu");
        var g = _groups.CreateGroup("acct1");
        _groups.AddMember(g, "acct3");

        var sent = Chat("en", "HandleGroup", "acct1", "Tovin");

        string.Join(" ", sent.Rows.Select(r => r.Trim())).Should().Be($"Group invite sent to Tovin. ({GameConfig.GroupInviteTimeoutSeconds}s to respond)");
        var invite = Got(target);
        string inv = string.Join(" ", invite.Select(r => r.Trim()));
        inv.Should().Contain(L("hu", "chat.group_invited_you", LongName)).And.Contain(L("hu", "chat.group_invite_howto", GameConfig.GroupInviteTimeoutSeconds));
        inv.Should().Contain("/accept").And.Contain("/deny");
        NoEnglish(invite, "hu invite");
        Fits(invite, "invite rows");

        Chat("hu", "HandleAccept", "acct2").Text.Should().Contain(L("hu", "chat.accepted_group_invite", LongName));
        for (int i = 0; i < 100 && leader.IncomingMessages.IsEmpty; i++) await Task.Delay(50);

        Got(leader).Should().ContainSingle().Which.Should().Be("  * Tovin has joined your group!");
        Got(member).Should().ContainSingle().Which.Should().Be("  * " + L("hu", "chat.group_joined", "Tovin"));
    }

    [Fact]
    public async Task GroupInviteDenied_ReachesTheLeader_InTheLeadersLanguage()
    {
        var leader = Online("acct1", "Mira", "hu");
        var target = Online("acct2", LongName, "en");
        Chat("hu", "HandleGroup", "acct1", LongName);
        Got(target).Should().NotBeEmpty();

        Chat("en", "HandleDeny", "acct2").Text.Should().Contain($"You denied Mira's group invite.");
        for (int i = 0; i < 100 && leader.IncomingMessages.IsEmpty; i++) await Task.Delay(50);

        var rows = Got(leader);
        string.Join(" ", rows.Select(r => r.Trim())).Should().StartWith("* " + L("hu", "chat.group_invite_denied", LongName));
        Fits(rows, "denied notice");
        NoEnglish(rows, "hu leader");
    }

    [Fact]
    public void LeaveAndDisband_AreInThePlayersLanguage_AndTheReasonInEachMembersLanguage()
    {
        var leader = Online("acct1", "Mira", "hu");
        var en = Online("acct2", LongName, "en");
        var hu = Online("acct3", "Bea", "hu");
        var g = _groups.CreateGroup("acct1");
        _groups.AddMember(g, "acct2");
        _groups.AddMember(g, "acct3");

        Chat("hu", "HandleLeaveGroup", "acct1").Text.Should().Contain(L("hu", "chat.group_leader_cant_leave")).And.Contain("/disband");
        Chat("hu", "HandleDisbandGroup", "acct3").Text.Should().Contain(L("hu", "chat.group_only_leader_disbands"));
        Chat("hu", "HandleLeaveGroup", "acct3").Text.Should().Contain(L("hu", "chat.group_left"));

        Got(en).Single().Should().Be("  * Bea has left the group (left voluntarily).");
        var huLeader = Got(leader).Single();
        huLeader.Should().Be("  * " + L("hu", "group.member_left", "Bea", L("hu", "group.reason_left_voluntarily")));
        NoEnglish(new[] { huLeader }, "hu member left");

        Chat("hu", "HandleDisbandGroup", "acct1").Text.Should().Contain(L("hu", "chat.group_disbanded"));
        string.Join(" ", Got(en).Select(r => r.Trim())).Should().Be($"* Your group has been disbanded (leader disbanded the group).");
        Got(leader).Single().Should().Be("  * " + L("hu", "group.disbanded_reason", L("hu", "group.reason_leader_disbanded")));
    }

    [Fact]
    public void GroupReasons_StayEnglishAsPassed_AndAreMappedOnlyForDisplay()
    {
        // The reasons the callers pass, including CombatEngine's (not changed by 1.2.5).
        Src("Scripts/Systems/CombatEngine.cs").Should().Contain("DisbandGroup(ctx.Username, \"leader fell in combat\")");
        Src("Scripts/Server/MudChatSystem.cs").Should().Contain("RemoveMember(username, \"left voluntarily\")");
        Src("Scripts/Server/PlayerSession.cs").Should().Contain("RemoveMember(Username, \"disconnected\")");

        var map = new Dictionary<string, string>
        {
            ["left voluntarily"] = "group.reason_left_voluntarily", ["disconnected"] = "group.reason_disconnected",
            ["leader disconnected"] = "group.reason_leader_disconnected", ["no members joined"] = "group.reason_no_members",
            ["leader disbanded the group"] = "group.reason_leader_disbanded", ["leader fell in combat"] = "group.reason_leader_fell",
            ["group too small"] = "group.reason_too_small",
        };
        foreach (var (reason, key) in map)
        {
            GroupSystem.ReasonLabel("en", reason).Should().Be(reason);
            GroupSystem.ReasonLabel("hu", reason).Should().Be(L("hu", key)).And.NotBe(reason);
        }
        GroupSystem.ReasonLabel("hu", "something else").Should().Be("something else");

        var hu = Online("acct2", LongName, "hu");
        var g = _groups.CreateGroup("acct1");
        _groups.AddMember(g, "acct2");
        _groups.DisbandGroup("acct1", "leader fell in combat");
        var rows = Got(hu);
        string.Join(" ", rows.Select(r => r.Trim())).Should().Be("* " + L("hu", "group.disbanded_reason", L("hu", "group.reason_leader_fell")));
        Fits(rows, "disband notice");
    }

    // ---------- room, realm, spectators on disconnect ----------

    [Fact]
    public void RoomDisconnect_AndLeftTheRealm_ReachEachPlayer_InTheirLanguage()
    {
        var gone = Online("acct1", LongName, "en");
        var en = Online("acct2", "Tovin", "en");
        var hu = Online("acct3", "Bea", "hu");
        foreach (var s in new[] { gone, en, hu }) RoomRegistry.Instance!.PlayerEntered(GameLocation.TheInn, s);

        RoomRegistry.Instance!.PlayerDisconnected(gone);
        gone.AnnounceLeftRealm();

        Got(en).Should().Equal($"{LongName} has disconnected.", $"  {LongName} has left the realm.");
        var rows = Got(hu);
        rows.Should().Equal(L("hu", "mud.room_disconnected", LongName), "  " + L("hu", "mud.left_realm", LongName));
        NoEnglish(rows, "hu departure");
        Fits(rows, "departure rows");
    }

    [Fact]
    public void EndOfSpectating_TellsEachSide_InTheirLanguage()
    {
        var watched = Online("acct1", LongName, "en");
        var me = Online("acct2", "Tovin", "hu");
        var specHu = Online("acct3", "Bea", "hu");
        me.Spectators.Add(specHu);
        me.SpectatingSession = watched;
        watched.Spectators.Add(me);

        me.EndSpectatingOnDisconnect(null);

        var hu = Got(specHu).Single();
        hu.Should().Be("  * " + Loc.GetIn("hu", "engine.spectator_disconnected").Trim());
        NoEnglish(new[] { hu }, "hu spectator");
        Got(watched).Single().Should().Be("  * acct2 stopped watching your session.");
    }

    [Theory]
    [InlineData("en")]
    [InlineData("hu")]
    public void DisconnectLines_AreInThePlayersLanguage(string lang)
    {
        var screen = NewScreen();
        PlayerSession.WriteDisconnectLines(screen.Term, lang, l => Loc.GetIn(l, "mud.disconnect_idle", 30));
        PlayerSession.WriteDisconnectLines(screen.Term, lang, l => Loc.GetIn(l, "mud.disconnect_other_session"));
        PlayerSession.WriteDisconnectLines(screen.Term, lang, l => Loc.GetIn(l, "mud.kicked", Loc.GetIn(l, "mud.kick_by_admin")));
        screen.Term.StreamWriterInternal?.Flush();
        var text = screen.Text;
        text.Should().Contain($"*** {L(lang, "mud.disconnect_idle", 30)} ***").And.Contain(L(lang, "mud.auto_saved"));
        if (lang == "en")
            text.Should().Contain("*** Disconnected: logged in from another session ***").And.Contain("Your game has been auto-saved.")
                .And.Contain("*** Kicked: Kicked by admin ***");
        else
            text.Should().NotContain("Disconnected").And.NotContain("auto-saved").And.NotContain("Kicked");
        Fits(screen.Rows, "disconnect lines");
    }

    // ---------- server notices ----------

    [Fact]
    public async Task Shutdown_ReachesEachPlayer_InTheirLanguage_AndAnAdminReasonAsTyped()
    {
        var en = Online("acct1", "Mira", "en");
        var hu = Online("acct2", LongName, "hu");

        await _server.InitiateShutdown(30);
        Got(en).Single().Should().Be("  *** SERVER SHUTDOWN in 30 seconds: Server shutting down ***");
        var huLine = Got(hu).Single();
        huLine.Should().Be($"  *** {L("hu", "mud.shutdown_in_reason", 30, L("hu", "mud.shutdown_default_reason"))} ***");
        NoEnglish(new[] { huLine }, "hu shutdown");
        Fits(new[] { huLine }, "shutdown");

        typeof(MudServer).GetProperty("ShutdownCountdownSeconds")!.SetValue(_server, null);
        await _server.InitiateShutdown(30, "patch day");
        Got(hu).Single().Should().Contain("patch day").And.Contain("SZERVERLEÁLLÁS");
    }

    [Theory]
    [InlineData("en")]
    [InlineData("hu")]
    public void IdleWarning_IsInThePlayersLanguage_AndFits(string lang)
    {
        foreach (int minutes in new[] { 1, 5 })
        {
            var rows = MudServer.IdleWarningRows(lang, minutes);
            Fits(rows, $"idle warning {lang}");
            string joined = string.Join(" ", rows.Select(r => r.Trim()));
            joined.Should().Be($"*** {L(lang, minutes == 1 ? "mud.idle_warning_one" : "mud.idle_warning_many", minutes)} ***");
        }
        if (lang == "en")
            string.Join(" ", MudServer.IdleWarningRows("en", 5).Select(r => r.Trim()))
                .Should().Be("*** WARNING: You will be disconnected in ~5 minutes due to inactivity! Press any key. ***");
    }

    [Fact]
    public async Task GodsNotices_ReachThePlayer_InTheirLanguage()
    {
        var path = Path.Combine(Path.GetTempPath(), $"usurper-online-{Guid.NewGuid():N}.db");
        try
        {
            typeof(MudServer).GetField("_sqlBackend", NP)!.SetValue(_server, new SqlSaveBackend(path));
            var hu = Online("acct1", LongName, "hu");
            var en = Online("acct2", "Tovin", "en");
            var exec = typeof(MudServer).GetMethod("RunClaimedAdminCommand", NP)!;
            async Task Run(string cmd, string target, string? args = null) =>
                await (Task)exec.Invoke(_server, new object[] { new AdminCommand { Id = 1, Command = cmd, TargetUsername = target, Args = args } })!;

            var keys = new[] { ("freeze", "mud.gods_frozen"), ("thaw", "mud.gods_thawed"), ("mute", "mud.gods_silenced"), ("unmute", "mud.gods_voice_restored"), ("slay", "mud.gods_struck_down") };
            foreach (var (cmd, key) in keys)
            {
                await Run(cmd, "acct1");
                Got(hu).Single().Should().Be($"  *** {L("hu", key)} ***");
                await Run(cmd, "acct2");
                Got(en).Single().Should().Be($"  *** {L("en", key)} ***");
            }
            Got(en).Should().BeEmpty();
            await Run("message", "acct1", "{\"message\":\"be kind\"}");
            Got(hu).Single().Should().Be("  " + L("hu", "mud.admin_message", "be kind"));
            await Run("freeze", "acct1");
            Got(hu).Single().Should().NotContain("the gods");
        }
        finally
        {
            SqliteConnection.ClearAllPools();
            try { File.Delete(path); } catch { }
        }
    }

    // ---------- login gate, relay, BBS adapter ----------

    [Theory]
    [InlineData("en")]
    [InlineData("es")]
    [InlineData("fr")]
    [InlineData("hu")]
    [InlineData("it")]
    public void RelayMenu_IsInTheChosenLanguage_AndTheBoxIs79Wide(string lang)
    {
        var text = RelayClient.AuthMenuText(lang);
        var rows = string.Concat(text).Replace("\r", "").Split('\n').Select(UIHelper.StripAnsi).Where(r => r.Length > 0).ToList();
        rows.Where(r => r.StartsWith("╔") || r.StartsWith("║") || r.StartsWith("╚"))
            .Should().OnlyContain(r => r.Length == MaxWidth, "every box row is 79 columns");
        string all = string.Join("\n", rows);
        all.Should().Contain(L(lang, "auth.relay_title")).And.Contain("[L] " + L(lang, "auth.login"))
            .And.Contain("[R] " + L(lang, "auth.register")).And.Contain("[G] " + L(lang, "auth.language"))
            .And.Contain("[Q] " + L(lang, "auth.quit")).And.Contain(L(lang, "auth.choice"));
        if (lang == "hu") all.Should().NotContain("Login to existing account").And.NotContain("Register new account");
        if (lang == "en") all.Should().Contain("Welcome to Usurper Reborn Online").And.Contain("Login to existing account");
        Capture($"online-relay-menu-{lang}.txt", all);
    }

    [Fact]
    public void GateBox_Is79Wide_WithEveryLabel()
    {
        foreach (var lang in AllLanguages)
        {
            var rows = MudServer.AuthBoxRows(L(lang, "auth.title"), new[]
            {
                ("1;36", "L", L(lang, "auth.login")), ("1;32", "R", L(lang, "auth.register")),
                ("1;35", "G", $"{L(lang, "auth.language")} (Magyar (AI Translated))"), ("1;31", "Q", L(lang, "auth.quit")),
            }).Select(UIHelper.StripAnsi).ToList();
            rows.Should().OnlyContain(r => r.Length == MaxWidth, lang);
            rows.Should().Contain(r => r.Contains(L(lang, "auth.login")));
        }
        Src("Scripts/Server/MudServer.cs").Should().Contain("AuthBoxRows(L(\"auth.title\")");
    }

    [Theory]
    [InlineData("en")]
    [InlineData("hu")]
    public async Task BbsAdapterPrompts_AreInThePlayersLanguage(string lang)
    {
        var adapter = (BBSTerminalAdapter)RuntimeHelpers.GetUninitializedObject(typeof(BBSTerminalAdapter));
        typeof(BBSTerminalAdapter).GetField("_sessionInfo", NP)!.SetValue(adapter, new BBSSessionInfo());
        typeof(BBSTerminalAdapter).GetField("_useAnsiForLocal", NP)!.SetValue(adapter, true);
        typeof(BBSTerminalAdapter).GetField("_currentColor", NP)!.SetValue(adapter, "white");
        var oldIn = Console.In;
        var oldOut = Console.Out;
        var output = new StringWriter();
        var prev = GameConfig.Language;
        try
        {
            GameConfig.Language = lang;
            Console.SetIn(new StringReader("abc\n99\n3\nzz\nB\n"));
            Console.SetOut(output);
            (await adapter.GetNumberInput("", 1, 5)).Should().Be(3);
            (await adapter.GetMenuChoice(new List<UsurperRemake.BBS.MenuOption> { new() { Key = "A", Text = "a" }, new() { Key = "B", Text = "b" } })).Should().Be(1);
        }
        finally
        {
            GameConfig.Language = prev;
            Console.SetIn(oldIn);
            Console.SetOut(oldOut);
        }
        var text = UIHelper.StripAnsi(output.ToString());
        text.Should().Contain(L(lang, "ui.enter_valid_number")).And.Contain(L(lang, "ui.number_between", 1, 5)).And.Contain(L(lang, "ui.invalid_choice_retry"));
        if (lang == "hu") text.Should().NotContain("Please").And.NotContain("Invalid choice");
    }

    // ---------- typed commands and protocol strings ----------

    [Theory]
    [InlineData("en")]
    [InlineData("es")]
    [InlineData("fr")]
    [InlineData("hu")]
    [InlineData("it")]
    public async Task TypedCommands_StillParse_InEveryLanguage(string lang)
    {
        var me = Online("acct1", "Mira", lang);
        var other = Online("acct2", "Tovin", "en");
        RoomRegistry.Instance!.PlayerEntered(GameLocation.TheInn, me);
        RoomRegistry.Instance!.PlayerEntered(GameLocation.TheInn, other);
        var screen = NewScreen();
        try
        {
            SessionContext.Current = me.Context;
            (await MudChatSystem.TryProcessCommand("/say hello", screen.Term)).Should().BeTrue();
            (await MudChatSystem.TryProcessCommand("/tell Tovin psst", screen.Term)).Should().BeTrue();
            (await MudChatSystem.TryProcessCommand("/gos hey", screen.Term)).Should().BeTrue();
            (await MudChatSystem.TryProcessCommand("/group Tovin", screen.Term)).Should().BeTrue();
            (await MudChatSystem.TryProcessCommand("/leave", screen.Term)).Should().BeTrue();
            (await MudChatSystem.TryProcessCommand("/mondja hello", screen.Term)).Should().BeFalse("only the English command words parse");
        }
        finally { SessionContext.Current = null; }

        var got = Got(other);
        got.Should().Contain("  Mira says: hello").And.Contain("  Mira tells you: psst").And.Contain("  [Gossip] Mira: hey");
        got.Should().Contain(r => r.Contains("has invited you to join their dungeon group"));
    }

    [Fact]
    public void ProtocolStrings_StayEnglish()
    {
        var server = Src("Scripts/Server/MudServer.cs");
        server.Should().Contain("await WriteLineAsync(stream, \"OK\");");
        server.Should().Contain("\"ERR:Invalid auth format. Expected AUTH:username:connectionType\"");
        server.Should().Contain("$\"ERR:{message}\"").And.Contain("$\"ERR:{regMessage}\"");
        server.Should().Contain("$\"ERR:Too many failed logins. Try again in {throttleWait} seconds.\"");
        var relay = Src("Scripts/Server/RelayClient.cs");
        relay.Should().Contain("response.StartsWith(\"ERR:\")").And.Contain("if (response != \"OK\")").And.Contain("await WriteAnsi(stdout, \"OK\\r\\n\");");
        relay.Should().Contain("$\"AUTH:{username}:{password}:{connectionType}\\n\"");
        // The group invite and chat channels are ids: unchanged in the history and mute keys.
        Src("Scripts/Server/MudChatSystem.cs").Should().Contain("channelKey: \"gossip\"").And.Contain("historyChannel: \"say\"");
    }
}
