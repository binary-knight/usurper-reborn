using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using FluentAssertions;
using UsurperRemake;
using UsurperRemake.Systems;
using Xunit;

namespace UsurperReborn.Tests;

/// <summary>
/// v1.2.8: the "cannot act" lines name the status with its word in the reader's language. Before, the duel
/// line was filled with the status description ("You are stunned, cannot act and cannot act!") and the
/// prompt lines with the English enum name in every language.
/// Callers: the duel round (attacker and computer opponent), the single prompt and the dungeon prompt.
/// </summary>
[Collection("SharedGameSingletons")]
public class StunLine128Tests
{
    private const BindingFlags F = BindingFlags.NonPublic | BindingFlags.Instance;
    private static readonly string[] Languages = { "en", "es", "fr", "hu", "it" };

    /// <summary>Every status that takes the turn away (StatusEffect.PreventsAction).</summary>
    private static readonly StatusEffect[] Holding =
        Enum.GetValues<StatusEffect>().Where(s => s.PreventsAction()).ToArray();

    public static IEnumerable<object[]> LanguageAndStatus() =>
        from lang in Languages from status in Holding select new object[] { lang, status };

    public static IEnumerable<object[]> AllLanguages() => Languages.Select(l => new object[] { l });

    private sealed class ScriptedStream : Stream
    {
        private readonly byte[] _data;
        private int _pos;
        private Action? _onFirstRead;
        public ScriptedStream(string script, Action? onFirstRead = null) { _data = Encoding.UTF8.GetBytes(script); _onFirstRead = onFirstRead; }
        public override int Read(byte[] buffer, int offset, int count)
        {
            _onFirstRead?.Invoke();   // the first prompt of the duel: the fight has begun
            _onFirstRead = null;
            if (_pos >= _data.Length) throw new IOException("scripted disconnect");
            int n = Math.Min(count, _data.Length - _pos);
            Array.Copy(_data, _pos, buffer, offset, n);
            _pos += n;
            return n;
        }
        public override Task<int> ReadAsync(byte[] buffer, int offset, int count, CancellationToken ct)
            => Task.FromResult(Read(buffer, offset, count));
        public override bool CanRead => true;
        public override bool CanSeek => false;
        public override bool CanWrite => false;
        public override long Length => _data.Length;
        public override long Position { get => _pos; set => throw new NotSupportedException(); }
        public override void Flush() { }
        public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();
        public override void SetLength(long value) => throw new NotSupportedException();
        public override void Write(byte[] buffer, int offset, int count) => throw new NotSupportedException();
    }

    private static Character Duelist(string name) => new()
    {
        Name1 = name, Name2 = name, Class = CharacterClass.Warrior, Race = CharacterRace.Human, Level = 10,
        HP = 100_000, MaxHP = 100_000, BaseMaxHP = 100_000,
        Strength = 40, BaseStrength = 40, Defence = 20, BaseDefence = 20,
        Dexterity = 20, BaseDexterity = 20, Agility = 20, BaseAgility = 20,
        Constitution = 20, BaseConstitution = 20, CombatSpeed = CombatSpeed.Instant,
    };

    private static string Strip(string s) => Regex.Replace(s, "\u001b\\[[0-9;]*[A-Za-z]", "");

    private static async Task<T> WithLanguage<T>(string lang, Func<Task<T>> body)
    {
        var prev = GameConfig.Language;
        try
        {
            GameConfig.Language = lang;
            return await body();
        }
        finally { GameConfig.Language = prev; }
    }

    /// <summary>Runs a duel until the script runs out; the transcript without colour codes.</summary>
    private static async Task<string> Duel(string script, Character attacker, Character defender, Action? atFirstPrompt = null)
    {
        var output = new MemoryStream();
        var engine = new CombatEngine(new TerminalEmulator(new ScriptedStream(script, atFirstPrompt), output));
        engine.SeedRandomForTests(128);
        Exception? error = null;
        try { await engine.PlayerVsPlayer(attacker, defender); }
        catch (Exception ex) { error = ex; }
        string text = Strip(Encoding.UTF8.GetString(output.ToArray()));
        error.Should().BeAssignableTo<IOException>("the duel ends when the script runs out; transcript: {0}", text);
        return text;
    }

    /// <summary>The status as the game's own word list has it in `lang`, lower case.</summary>
    private static string Word(string lang, StatusEffect status)
    {
        string key = "status." + status.ToString().ToLowerInvariant();
        Loc.HasIn(lang, key).Should().BeTrue("{0} has its own word for {1}", lang, status);
        return Loc.GetIn(lang, key).ToLower();
    }

    /// <summary>The checks every "cannot act" row has to pass: the status word of the language, once, in 79 columns.</summary>
    private static void RowReadsRight(string row, string lang, StatusEffect status)
    {
        string word = Word(lang, status);
        row.Should().Contain(word);
        row.Length.Should().BeLessThanOrEqualTo(79, "the row fits the screen: {0}", row);
        char.IsUpper(row.TrimStart('¡')[0]).Should().BeTrue("the sentence starts with a capital: {0}", row);
        Regex.Matches(row, "cannot act").Count.Should().BeLessThanOrEqualTo(1, "the row says it once: {0}", row);
        if (lang != "en")
        {
            word.Should().NotBe(status.ToString().ToLower(), "{0} does not show the English enum name", lang);
            row.ToLower().Should().NotContain(status.ToString().ToLower(), "no English enum name in {0}: {1}", lang, row);
            row.ToLower().Should().NotContain(status.GetDescription().ToLower(), "no English description in {0}: {1}", lang, row);
        }
    }

    // ---------- caller 1: the duel round, the player's turn (CombatEngine.PlayerVsPlayer) ----------

    [Theory]
    [MemberData(nameof(LanguageAndStatus))]
    public async Task Duel_ThePlayerHeld_ReadsTheStatusWordOnce(string lang, StatusEffect status)
    {
        string text = await WithLanguage(lang, async () =>
        {
            var attacker = Duelist("Tester");
            var defender = Duelist("Rival");
            defender.AI = CharacterAI.Computer;
            attacker.ApplyStatus(status, 1);
            return await Duel("A\n", attacker, defender);
        });

        string row = Loc.GetIn(lang, "combat.you_status_prevented", Word(lang, status));
        text.Split('\n').Select(l => l.Trim()).Should().Contain(row, "transcript: {0}", text);
        RowReadsRight(row, lang, status);
        text.Should().NotContain("cannot act and cannot act");
    }

    [Fact]
    public async Task Duel_TheStunLine_InEnglish_IsTheOneSentence()
    {
        var attacker = Duelist("Tester");
        var defender = Duelist("Rival");
        defender.AI = CharacterAI.Computer;
        attacker.ApplyStatus(StatusEffect.Stunned, 1);
        string text = await WithLanguage("en", () => Duel("A\n", attacker, defender));

        text.Split('\n').Select(l => l.Trim()).Should().Contain("You are stunned and cannot act!", "transcript: {0}", text);
        text.Should().NotContain("stunned, cannot act");
    }

    // ---------- caller 2: the duel round, the computer opponent's turn ----------

    [Theory]
    [MemberData(nameof(LanguageAndStatus))]
    public async Task Duel_TheOpponentHeld_ReadsTheStatusWord(string lang, StatusEffect status)
    {
        string text = await WithLanguage(lang, async () =>
        {
            var attacker = Duelist("Tester");
            var defender = Duelist("Rival");
            defender.AI = CharacterAI.Computer;
            // the duel clears the opponent's holds when it starts, so the hold lands at the player's first
            // prompt; the player defends, so no blow wakes a sleeper before the opponent's turn
            return await Duel("D\n", attacker, defender, () => defender.ApplyStatus(status, 2));
        });

        string row = Loc.GetIn(lang, "combat.npc_status_prevented", "Rival", Word(lang, status));
        text.Split('\n').Select(l => l.Trim()).Should().Contain(row, "transcript: {0}", text);
        RowReadsRight(row, lang, status);
    }

    // ---------- callers 3 and 4: the action prompts (single and dungeon) ----------

    private static async Task<string> Prompt(string lang, StatusEffect status, bool dungeon)
    {
        return await WithLanguage(lang, async () =>
        {
            var output = new MemoryStream();
            var term = new TerminalEmulator(new MemoryStream(), output);
            var engine = new CombatEngine(term);
            var player = Duelist("Tester");
            player.ApplyStatus(status, 1);
            var result = new CombatResult();
            if (dungeon)
            {
                var m = typeof(CombatEngine).GetMethod("GetPlayerActionMultiMonster", F)!;
                await (Task<(CombatAction action, bool enableAutoCombat)>)m.Invoke(engine, new object[] { player, new List<Monster>(), result })!;
            }
            else
            {
                var m = typeof(CombatEngine).GetMethod("GetPlayerAction", F)!;
                await (Task<CombatAction>)m.Invoke(engine, new object?[] { player, null, result, Duelist("Rival") })!;
            }
            term.StreamWriterInternal?.Flush();
            return Strip(Encoding.UTF8.GetString(output.ToArray()));
        });
    }

    [Theory]
    [MemberData(nameof(LanguageAndStatus))]
    public async Task SinglePrompt_ThePlayerHeld_ReadsTheStatusWord(string lang, StatusEffect status)
    {
        string text = await Prompt(lang, status, dungeon: false);
        string row = Loc.GetIn(lang, "combat.you_status_prevented", Word(lang, status));
        text.Split('\n').Select(l => l.Trim()).Should().Contain(row, "screen: {0}", text);
        RowReadsRight(row, lang, status);
    }

    [Theory]
    [MemberData(nameof(LanguageAndStatus))]
    public async Task DungeonPrompt_ThePlayerHeld_ReadsTheStatusWord(string lang, StatusEffect status)
    {
        string text = await Prompt(lang, status, dungeon: true);
        string row = Loc.GetIn(lang, "combat.cannot_act", Word(lang, status));
        text.Split('\n').Select(l => l.Trim()).Should().Contain(row, "screen: {0}", text);
        RowReadsRight(row, lang, status);
    }

    // ---------- a group member who reads another language (the recorded Loc path) ----------

    public static IEnumerable<object[]> FollowerRows() =>
        from key in new[] { "combat.you_status_prevented", "combat.cannot_act" }
        from pair in new[] { ("en", "hu"), ("en", "fr"), ("it", "hu"), ("hu", "es") }
        from status in Holding
        select new object[] { key, pair.Item1, pair.Item2, status };

    [Theory]
    [MemberData(nameof(FollowerRows))]
    public async Task AFollower_ReadsTheLine_WithTheirOwnStatusWord(string key, string leaderLang, string followerLang, StatusEffect status)
    {
        var (rec, captured) = await WithLanguage(leaderLang, () =>
        {
            var recording = Loc.BeginRecording();
            string line;
            try { line = Loc.Get(key, CombatEngine.StatusWord(status)); }
            finally { Loc.EndRecording(recording); }
            return Task.FromResult((recording, line + "\n"));
        });

        string shown = CombatEngine.CapturedInLanguage(rec, captured, followerLang, "Tester");

        string row = Loc.GetIn(followerLang, "combat.teammate_status_prevented", "Tester", Word(followerLang, status));
        shown.Should().Be(row + "\n", "the follower reads the third person line in {0}", followerLang);
        row.Length.Should().BeLessThanOrEqualTo(79);
        shown.Should().NotContain(Word(leaderLang, status), "the leader's word does not stay in the follower's sentence");
    }

    [Fact]
    public void NoGroupCapture_EnclosesTheCallers()
    {
        // The callers of the two "You are ... and cannot act!" keys write before the leader's capture
        // starts (the dungeon prompt) or in a duel, which has no group; the follower rows above are the
        // recorded path they would take.
        string src = File.ReadAllText(Path.Combine(RepoRoot(), "Scripts", "Systems", "CombatEngine.cs"));
        Regex.Matches(src, "Loc\\.Get\\(\"combat\\.you_status_prevented\"").Count.Should().Be(2);
        Regex.Matches(src, "Loc\\.Get\\(\"combat\\.cannot_act\"").Count.Should().Be(1);
        Regex.Matches(src, "Loc\\.Get\\(\"combat\\.(you_status_prevented|cannot_act|npc_status_prevented)\", [^\\n]*(GetDescription|ToString)\\(")
            .Count.Should().Be(0, "no caller fills the status with a description or an enum name");
    }

    private static string RepoRoot()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir != null && !File.Exists(Path.Combine(dir.FullName, "usurper-reloaded.csproj"))) dir = dir.Parent;
        return dir!.FullName;
    }
}
