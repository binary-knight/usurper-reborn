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
using UsurperRemake.Server;
using UsurperRemake.Systems;
using Xunit;

namespace UsurperReborn.Tests;

/// <summary>
/// v1.2.4: group combat in each member's language. A follower's turn runs on the leader's session, so
/// it is drawn inside a Loc.RenderLanguage scope in the follower's language; the scope never writes the
/// leader's session language.
/// </summary>
[Collection("SharedGameSingletons")]
public class GroupCombatLang124Tests
{
    // ---------- 1. the render language scope ----------

    private static void WithLeaderSession(Action<SessionContext> body)
    {
        var saved = SessionContext.Current;
        var ctx = new SessionContext { Username = "leader", Language = "en" };
        SessionContext.Current = ctx;
        try { body(ctx); }
        finally { SessionContext.Current = saved; }
    }

    [Fact]
    public void RenderLanguage_ResolvesInTheScopeLanguage_AndRestoresAfterIt()
    {
        WithLeaderSession(ctx =>
        {
            using (Loc.RenderLanguage("hu"))
            {
                GameConfig.Language.Should().Be("hu");
                Loc.Get("combat.round_label", 3).Should().Be(Loc.GetIn("hu", "combat.round_label", 3));
            }
            GameConfig.Language.Should().Be("en", "the scope restores on exit");
            Loc.RenderLanguageOverride.Should().BeNull();
            Loc.Get("combat.round_label", 3).Should().Be("Round 3");
        });
    }

    [Fact]
    public void RenderLanguage_DoesNotWriteTheLeadersSessionLanguage()
    {
        WithLeaderSession(ctx =>
        {
            using (Loc.RenderLanguage("hu"))
            {
                ctx.Language.Should().Be("en", "the leader's own setting is not touched");
                // the setter inside a scope does nothing: neither the override nor the leader's setting moves
                GameConfig.Language = "fr";
                GameConfig.Language.Should().Be("hu");
                ctx.Language.Should().Be("en");
                CombatEngine.InLanguage("it", () => ctx.Language).Should().Be("en");
            }
            ctx.Language.Should().Be("en");
            GameConfig.Language.Should().Be("en");
            CombatEngine.InLanguage("hu", () => GameConfig.Language + "/" + ctx.Language).Should().Be("hu/en");
            ctx.Language.Should().Be("en");
        });
    }

    [Fact]
    public void RenderLanguage_RestoresAfterAnExceptionInside()
    {
        WithLeaderSession(ctx =>
        {
            Action act = () =>
            {
                using (Loc.RenderLanguage("hu"))
                    throw new InvalidOperationException("boom");
            };
            act.Should().Throw<InvalidOperationException>();
            GameConfig.Language.Should().Be("en");
            Loc.RenderLanguageOverride.Should().BeNull();
            ctx.Language.Should().Be("en");
        });
    }

    [Fact]
    public async Task RenderLanguage_HoldsAcrossAwaits_AndNests()
    {
        var saved = SessionContext.Current;
        var ctx = new SessionContext { Username = "leader", Language = "en" };
        SessionContext.Current = ctx;
        try
        {
            using (Loc.RenderLanguage("hu"))
            {
                await Task.Yield();
                await Task.Delay(5);
                GameConfig.Language.Should().Be("hu");
                using (Loc.RenderLanguage("fr"))
                {
                    await Task.Yield();
                    GameConfig.Language.Should().Be("fr");
                }
                GameConfig.Language.Should().Be("hu", "the inner scope gives back the outer one");
                using (Loc.SessionLanguage())
                    GameConfig.Language.Should().Be("en", "a session scope reads the leader's own language");
                GameConfig.Language.Should().Be("hu");
            }
            GameConfig.Language.Should().Be("en");
            ctx.Language.Should().Be("en");
        }
        finally { SessionContext.Current = saved; }
    }

    [Fact]
    public void Recordings_Nest_SoAnInnerOneDoesNotEndTheOuter()
    {
        var outer = Loc.BeginRecording();
        string first, second;
        try
        {
            first = Loc.Get("combat.round_label", 1);
            var inner = Loc.BeginRecording();
            try { Loc.Get("combat.party_label"); }
            finally { Loc.EndRecording(); }
            second = Loc.Get("combat.round_label", 2);
            inner.Render("Party:", "hu").Should().Be(Loc.GetIn("hu", "combat.party_label"));
        }
        finally { Loc.EndRecording(); }
        outer.Render(first + " " + second + " Party:", "hu").Should().Be(
            $"{Loc.GetIn("hu", "combat.round_label", 1)} {Loc.GetIn("hu", "combat.round_label", 2)} {Loc.GetIn("hu", "combat.party_label")}");
    }

    // ---------- 2. the follower's turn ----------

    private const BindingFlags F = BindingFlags.NonPublic | BindingFlags.Instance;

    /// <summary>A 30 character name, the longest a player can take.</summary>
    private const string LongName = "Eszterhazy Kalandor Bajnoka Ur";

    private static string Strip(string s) => Regex.Replace(s, "\u001b\\[[0-9;?]*[A-Za-z]", "");

    private static IEnumerable<string> Rows(string text) => text.Replace("\r", "").Split('\n');

    private static string Shown(TerminalEmulator term, MemoryStream output)
    {
        term.StreamWriterInternal?.Flush();
        return Strip(Encoding.UTF8.GetString(output.ToArray()));
    }

    /// <summary>Writes a render to USURPER_EVIDENCE_DIR when set, for a by-eye check.</summary>
    private static void Capture(string name, string text)
    {
        var dir = Environment.GetEnvironmentVariable("USURPER_EVIDENCE_DIR");
        if (!string.IsNullOrEmpty(dir)) File.WriteAllText(Path.Combine(dir, name), text);
    }

    private static void AllRowsFit(string text, string what)
    {
        foreach (var row in Rows(text))
            row.Length.Should().BeLessThanOrEqualTo(79, $"{what}: \"{row}\"");
    }

    private static Character Member(string name, string? username, TerminalEmulator? remote) => new Character
    {
        Name1 = name, Name2 = name, AI = CharacterAI.Human, Level = 20, HP = 300, MaxHP = 400, BaseMaxHP = 400,
        BaseStrength = 30, BaseDexterity = 20, BaseConstitution = 20, BaseIntelligence = 20, BaseWisdom = 20,
        BaseCharisma = 10, BaseDefence = 5, BaseStamina = 10, BaseAgility = 10, Mental = 80,
        Class = CharacterClass.Warrior, Race = CharacterRace.Human, CombatSpeed = CombatSpeed.Instant,
        GroupPlayerUsername = username, RemoteTerminal = remote,
        CombatInputChannel = remote == null ? null : System.Threading.Channels.Channel.CreateBounded<string>(1),
    };

    private static Monster Foe() => new Monster { Name = "Ogre", Level = 20, HP = 3000, MaxHP = 5000, Strength = 50, Defence = 20, IsActive = true };

    private sealed class Turn
    {
        public string Leader = "", Follower = "";
        public List<(string? exclude, Func<string, string> build)> Broadcasts = new();
    }

    /// <summary>
    /// Runs one grouped follower turn (the follower defends) with the leader in English, the follower in
    /// `followerLang` and a second follower in French. Returns both screens and the group broadcasts.
    /// </summary>
    private static async Task<Turn> FollowerTurn(string followerLang)
    {
        var prevLangOf = CombatEngine.LanguageOf;
        var prevSink = CombatEngine.GroupBroadcastSink;
        var prevLang = GameConfig.Language;
        var turn = new Turn();
        try
        {
            GameConfig.Language = "en";   // the leader's session
            var leaderOut = new MemoryStream();
            var leaderTerm = new TerminalEmulator(new MemoryStream(), leaderOut);
            var followerOut = new MemoryStream();
            var followerTerm = new TerminalEmulator(new MemoryStream(), followerOut);
            var leader = Member("Leader", null, null);
            var follower = Member(LongName, "gcl124f1", followerTerm);
            var other = Member("Other", "gcl124f2", new TerminalEmulator(new MemoryStream(), new MemoryStream()));
            CombatEngine.LanguageOf = c => ReferenceEquals(c, follower) ? followerLang : ReferenceEquals(c, other) ? "fr" : "en";
            CombatEngine.GroupBroadcastSink = (exclude, build) => turn.Broadcasts.Add((exclude, build));

            var engine = new CombatEngine(leaderTerm);
            var mates = new List<Character> { follower, other };
            typeof(CombatEngine).GetField("currentPlayer", F)!.SetValue(engine, leader);
            typeof(CombatEngine).GetField("currentTeammates", F)!.SetValue(engine, mates);
            var result = new CombatResult { Player = leader, Monsters = new List<Monster> { Foe() }, Teammates = mates };

            var task = (Task)typeof(CombatEngine).GetMethod("ProcessGroupedPlayerTurn", F)!
                .Invoke(engine, new object[] { follower, leader, result.Monsters, result })!;
            var waited = System.Diagnostics.Stopwatch.StartNew();
            while (!follower.IsAwaitingCombatInput && !task.IsCompleted && waited.ElapsedMilliseconds < 10000)
                await Task.Delay(5);
            follower.IsAwaitingCombatInput.Should().BeTrue("the follower's turn waits for their input");
            await follower.CombatInputChannel!.Writer.WriteAsync("D");
            await task;

            GameConfig.Language.Should().Be("en", "the follower's scope has ended");
            Loc.RenderLanguageOverride.Should().BeNull();
            turn.Leader = Shown(leaderTerm, leaderOut);
            turn.Follower = Shown(followerTerm, followerOut);
            return turn;
        }
        finally
        {
            CombatEngine.LanguageOf = prevLangOf;
            CombatEngine.GroupBroadcastSink = prevSink;
            GameConfig.Language = prevLang;
        }
    }

    [Fact]
    public async Task FollowerTurn_TheFollowersScreenIsInTheirLanguage_TheLeadersStaysEnglish()
    {
        LongName.Length.Should().Be(GameConfig.MaxNameLength);
        var turn = await FollowerTurn("hu");
        Capture("group-lang-follower-hu.txt", turn.Follower);
        Capture("group-lang-leader-en.txt", turn.Leader);

        foreach (var key in new[] { "combat.combat_status_header", "combat.choose_action", "combat.defend_stance" })
        {
            Loc.GetIn("hu", key).Should().NotBe(Loc.GetIn("en", key), key);
            turn.Follower.Should().Contain(Loc.GetIn("hu", key).Trim(), key);
            turn.Follower.Should().NotContain(Loc.GetIn("en", key).Trim(), key);
            turn.Leader.Should().NotContain(Loc.GetIn("hu", key).Trim(), key);
        }
        turn.Leader.Should().Contain(Loc.GetIn("en", "combat.group_leader_turn", LongName));
        turn.Leader.Should().NotContain(Loc.GetIn("hu", "combat.group_leader_turn", LongName));
    }

    [Theory]
    [InlineData("en")]
    [InlineData("hu")]
    public async Task FollowerTurn_ScreenFits79Columns_WithA30CharacterName(string lang)
    {
        LongName.Length.Should().Be(GameConfig.MaxNameLength);
        var turn = await FollowerTurn(lang);
        Capture($"group-lang-follower-fit-{lang}.txt", turn.Follower);
        turn.Follower.Should().Contain(LongName);
        AllRowsFit(turn.Follower, $"follower screen in {lang}");
        foreach (var lineLang in new[] { "en", "hu" })
            foreach (var (_, build) in turn.Broadcasts)
                AllRowsFit(Strip(build(lineLang)), $"broadcast in {lineLang}");
    }

    [Theory]
    [InlineData("en")]
    [InlineData("hu")]
    [InlineData("es")]
    [InlineData("fr")]
    [InlineData("it")]
    public async Task FollowerTurn_TheActionMenuBoxIsAligned(string lang)
    {
        var turn = await FollowerTurn(lang);
        var rows = Rows(turn.Follower).ToList();
        int top = rows.FindIndex(r => r.StartsWith("╔") && r.EndsWith("╗") && rows.IndexOf(r) + 1 < rows.Count
            && rows[rows.IndexOf(r) + 1].Contains(Loc.GetIn(lang, "combat.choose_action").Trim()));
        top.Should().BeGreaterThanOrEqualTo(0, "the action menu box is drawn");
        int bottom = rows.FindIndex(top, r => r.StartsWith("╚"));
        bottom.Should().BeGreaterThan(top);
        var box = rows.GetRange(top, bottom - top + 1);
        box.Should().Contain(r => r.Contains(Loc.GetIn(lang, "combat.aid_ally_none_short")), "the no-means aid row is in the box");
        foreach (var row in box)
        {
            row.Length.Should().Be(rows[top].Length, $"every row's right border is in the same column: \"{row}\"");
            "║╗╣╝".Should().Contain(row[^1].ToString(), $"the row ends in the border: \"{row}\"");
        }
        rows[top].Length.Should().BeLessThanOrEqualTo(79);
    }

    [Fact]
    public async Task FollowerTurn_TheCapturedActionReachesEachOtherMemberInTheirLanguage()
    {
        var turn = await FollowerTurn("hu");
        var action = turn.Broadcasts.Where(b => b.exclude == "gcl124f1")
            .Select(b => b.build).Where(b => Strip(b("en")).Contains("defensive stance")).ToList();
        action.Should().HaveCount(1, "the follower's action is sent once, built per reader");
        Strip(action[0]("hu")).Should().Contain(Loc.GetIn("hu", "combat.defend_stance_third", LongName), "a Hungarian reader other than the actor reads the third person")
            .And.NotContain(Loc.GetIn("hu", "combat.defend_stance"));
        Capture("group-lang-action.txt", action[0]("hu") + "\n----\n" + action[0]("en") + "\n----\n" + action[0]("fr"));
        Strip(action[0]("en")).Should().Contain($"{LongName} takes a defensive stance!", "the leader reads English, in the third person");
        Strip(action[0]("fr")).Should().Contain(Loc.GetIn("fr", "combat.defend_stance_third", LongName), "the other follower reads French, in the third person");
        Strip(action[0]("fr")).Should().NotContain(Loc.GetIn("fr", "combat.defend_stance")).And.NotContain("Vous");
        Strip(action[0]("fr")).Should().NotContain(Loc.GetIn("hu", "combat.defend_stance"));
        Loc.GetIn("fr", "combat.defend_stance").Should().NotBe(Loc.GetIn("en", "combat.defend_stance"));
    }

    /// <summary>The leader's captured action, recorded in English as the combat loop records it.</summary>
    private static (LocRecording rec, string captured) LeaderAction()
    {
        var prev = GameConfig.Language;
        GameConfig.Language = "en";
        var term = new TerminalEmulator(new MemoryStream(), new MemoryStream());
        term.StartCapture();
        var rec = Loc.BeginRecording();
        try
        {
            term.WriteLine(Loc.Get("combat.you_attack_target", "Ogre"), "white");                    // has a third person key
            term.WriteLine(Loc.Get("combat.miss", "Ogre"), "white");                                  // no person
            term.WriteLine(Loc.Get("combat.power_attack_action"), "white");                          // second person, no third person key
        }
        finally { Loc.EndRecording(rec); GameConfig.Language = prev; }
        return (rec, term.StopCapture()!);
    }

    [Theory]
    [InlineData("fr", "Vous")]
    [InlineData("hu", "Megtámadod")]
    [InlineData("hu", "készülsz")]
    public void LeadersCapturedAction_NeverReachesAnotherReaderInTheSecondPerson(string lang, string youMarker)
    {
        var (rec, captured) = LeaderAction();
        (Loc.GetIn(lang, "combat.you_attack_target", "Ogre") + Loc.GetIn(lang, "combat.power_attack_action"))
            .Should().Contain(youMarker, "the marker is the reader language's own second person form");
        string shown = Strip(CombatEngine.CapturedInLanguage(rec, captured, lang, "Rage"));
        Capture($"group-lang-leader-action-{lang}.txt", shown);
        shown.Should().NotContain(youMarker);
        shown.Should().NotContain(Loc.GetIn(lang, "combat.you_attack_target", "Ogre")).And.NotContain(Loc.GetIn(lang, "combat.power_attack_action"));
        shown.Should().Contain(Loc.GetIn(lang, "combat.you_attack_target_third", "Rage", "Ogre"), "a line with a third person key is in the reader's language");
        shown.Should().Contain(Loc.GetIn(lang, "combat.miss", "Ogre"), "a line with no person is in the reader's language");
        shown.Should().Contain("Rage unleashes a powerful strike!", "a second person line with no third person key falls back to English in the third person");
    }

    [Fact]
    public void LeadersCapturedAction_ForAnEnglishReaderIsUnchanged()
    {
        var (rec, captured) = LeaderAction();
        string shown = CombatEngine.CapturedInLanguage(rec, captured, "en", "Rage");
        shown.Should().Be(captured.Replace("You attack", "Rage attacks").Replace("You unleash", "Rage unleashes"),
            "English reads as it did before: the English line put in the third person");
        Strip(shown).Should().Contain("Rage attacks Ogre!").And.Contain("Rage unleashes a powerful strike!").And.Contain("The Ogre misses!");
    }

    // ---------- 3. the round status ----------

    [Fact]
    public void RoundStatus_ReachesTwoFollowersEachInTheirLanguage()
    {
        var prevSink = CombatEngine.GroupBroadcastSink;
        var prevLang = GameConfig.Language;
        try
        {
            GameConfig.Language = "en";   // the leader
            var sent = new List<Func<string, string>>();
            CombatEngine.GroupBroadcastSink = (_, build) => sent.Add(build);
            var engine = new CombatEngine(new TerminalEmulator(new MemoryStream(), new MemoryStream()));
            var leader = Member("Leader", null, null);
            var mates = new List<Character> { Member("Hu", "gcl124hu", null), Member("Fr", "gcl124fr", null) };
            var result = new CombatResult { Player = leader, Teammates = mates };
            typeof(CombatEngine).GetMethod("BroadcastRoundStatus", F)!
                .Invoke(engine, new object[] { result, 4, new List<Monster> { Foe() }, leader });
            sent.Should().HaveCount(1);
            foreach (var lang in new[] { "hu", "fr" })
            {
                string text = Strip(sent[0](lang));
                text.Should().Contain(Loc.GetIn(lang, "combat.round_label", 4)).And.Contain(Loc.GetIn(lang, "combat.party_label"));
                text.Should().NotContain("Round 4").And.NotContain("Party:");
            }
        }
        finally { CombatEngine.GroupBroadcastSink = prevSink; GameConfig.Language = prevLang; }
    }

    [Theory]
    [InlineData("en")]
    [InlineData("hu")]
    [InlineData("fr")]
    public void RoundStatus_Fits79Columns_WithFourLongNames(string lang)
    {
        LongName.Length.Should().Be(GameConfig.MaxNameLength);
        var leader = Member(LongName, null, null);
        leader.HP = 99999; leader.MaxHP = 99999;
        var mates = Enumerable.Range(0, 3).Select(i =>
        {
            var m = Member(LongName.Substring(0, 29) + i, "gcl124m" + i, null);
            m.HP = 99999; m.MaxHP = 99999;
            return m;
        }).ToList();
        string text = Strip(CombatEngine.RoundStatusText(lang, 12, new List<Monster> { Foe() }, leader, mates));
        Capture($"group-lang-round-{lang}.txt", text);
        AllRowsFit(text, $"round status in {lang}");
        foreach (var c in mates.Prepend(leader))
            text.Should().Contain($"{c.DisplayName} 99999/99999", "every member is listed");
    }

    // ---------- 4. shared state keeps the session's language ----------

    [Fact]
    public void ABeastSummonedInAFollowersScope_IsNamedInTheSessionsLanguage()
    {
        var prevLang = GameConfig.Language;
        try
        {
            GameConfig.Language = "en";   // the leader's session
            var actor = Member("GclBeast", null, null);
            actor.Level = 20; actor.MaxHP = 400; actor.HP = 400;
            GodRegistry.SetWorshippedGod(actor, "Sylvana").Should().BeTrue();
            actor.GodFavor = 80;
            FavorSystem.Bind(actor);
            actor.MiracleUsedToday = false;
            var output = new MemoryStream();
            var engine = new CombatEngine(new TerminalEmulator(new MemoryStream(), output));
            typeof(CombatEngine).GetField("currentPlayer", F)!.SetValue(engine, actor);
            var result = new CombatResult { Player = actor, Monsters = new List<Monster> { Foe() }, Teammates = new List<Character>() };
            using (Loc.RenderLanguage("hu"))
            {
                ((Task)typeof(CombatEngine).GetMethod("ExecuteMiracle", F)!
                    .Invoke(engine, new object[] { actor, result.Monsters, new CombatAction { Type = CombatActionType.Miracle }, result })!)
                    .GetAwaiter().GetResult();
            }
            var beast = result.Teammates.Single(t => t.IsMiracleAlly);
            beast.DisplayName.Should().Be(Loc.GetIn("en", "miracle.beast_name"), "the name can reach the news, which keeps the session's language");
            Loc.GetIn("hu", "miracle.beast_name").Should().NotBe(Loc.GetIn("en", "miracle.beast_name"));
            var term = (TerminalEmulator)typeof(CombatEngine).GetField("terminal", F)!.GetValue(engine)!;
            Shown(term, output).Should().Contain(Loc.GetIn("hu", "miracle.beast", beast.DisplayName), "the screen line itself is the reader's");
        }
        finally { GameConfig.Language = prevLang; }
    }

    [Fact]
    public void TheCombatLoopBroadcastsAreBuiltPerReader()
    {
        string src = File.ReadAllText(Path.Combine(RepoRoot(), "Scripts", "Systems", "CombatEngine.cs"));
        foreach (var site in new[]
                 {
                     "BroadcastGroupLocalized(result, lang => CapturedInLanguage(ambushRecording, ambushOutput, lang, player.DisplayName));",
                     "BroadcastGroupLocalized(result, lang => CapturedInLanguage(leaderRecording, leaderOutput, lang, player.DisplayName));",
                     "BroadcastGroupLocalized(result, lang => CapturedInLanguage(npcRecording, npcOutput, lang, player.DisplayName));",
                     "BroadcastGroupLocalized(result, lang => CapturedInLanguage(monsterRecording, monsterOutput, lang, player.DisplayName));",
                     "BroadcastGroupLocalized(result, lang => GroupCombatIntro(lang, monsters, result.Teammates));",
                     "BroadcastGroupLocalized(result, lang => GroupVictoryText(lang, result.DefeatedMonsters));",
                     "BroadcastRoundStatus(result, roundNumber, monsters, player);",
                     "using var followerLanguage = Loc.RenderLanguage(remoteLang);",
                 })
            src.Should().Contain(site);
        foreach (var english in new[] { "retreats from combat!\\u001b", "tries to retreat but fails!", "Fighting alongside you:\\u001b", "Defeated: {", "── Round {roundNumber} ──" })
            src.Should().NotContain(english);
    }

    [Theory]
    [InlineData("en")]
    [InlineData("hu")]
    [InlineData("fr")]
    public void IntroAndVictory_AreInTheReadersLanguage_AndFit(string lang)
    {
        var mates = new List<Character> { Member(LongName, "gcl124a", null) };
        var ogre = Foe();
        string intro = Strip(CombatEngine.GroupCombatIntro(lang, new List<Monster> { ogre }, mates));
        string victory = Strip(CombatEngine.GroupVictoryText(lang, new List<Monster> { ogre }));
        Capture($"group-lang-intro-{lang}.txt", intro + victory);
        intro.Should().Contain(Loc.GetIn(lang, "combat.header")).And.Contain(Loc.GetIn(lang, "combat.fighting_alongside"));
        intro.Should().Contain(Loc.GetIn(lang, "combat.facing", CombatEngine.InLanguage(lang, ogre.GetDisplayInfo)));   // v1.2.5: the line in the reader's language
        victory.Should().Contain(Loc.GetIn(lang, "combat.group_defeated_one", "Ogre")).And.Contain(Loc.GetIn(lang, "combat.victory_solo"));
        Strip(CombatEngine.GroupVictoryText(lang, new List<Monster> { ogre, Foe() })).Should().Contain(Loc.GetIn(lang, "combat.defeated_count", 2));
        AllRowsFit(intro, "intro");
        AllRowsFit(victory, "victory");
        foreach (var key in new[] { "combat.group_member_retreats", "combat.group_member_retreat_fails", "miracle.vanish_other",
                     "combat.boss_unleashed", "miracle.mortis_fires_other", "combat.deaths_door_broadcast", "combat.last_stand_broadcast" })
        {
            Loc.HasIn(lang, key).Should().BeTrue(key);
            string line = Strip(CombatEngine.GroupLineWrapped(lang, "", key, LongName));
            line.Replace("\n  ", " ").Should().Contain(LongName).And.Contain(Loc.GetIn(lang, key, LongName).Split(' ').Last());
            AllRowsFit(line, key);
        }
        AllRowsFit($"  ═══ {Loc.GetIn(lang, "combat.npc_fallen_battle_banner", LongName)} ═══", "npc fallen banner");
    }

    private static string RepoRoot()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir != null && !File.Exists(Path.Combine(dir.FullName, "usurper-reloaded.csproj"))) dir = dir.Parent;
        return dir?.FullName ?? throw new InvalidOperationException("repo root not found");
    }
}
