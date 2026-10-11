using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text;
using System.Text.RegularExpressions;
using FluentAssertions;
using UsurperRemake;
using UsurperRemake.BBS;
using UsurperRemake.Systems;
using UsurperRemake.UI;
using Xunit;
using Xunit.Abstractions;

namespace UsurperReborn.Tests;

/// <summary>
/// 1.2.8, terminal.Write and [colour]...[/] markup. Write sent the text as it is on the stream path (online
/// play), on the door path and on the console fallback, so a marked text showed its tags; only WriteLine
/// rendered them. Write now renders them with the parsers WriteLine uses, with no line end, and goes back
/// to the colour of the Write after a pair. A text without a pair is sent exactly as before.
/// Row 1 stream, row 2 plain text, row 3 door and console, row 4 a text without a pair, row 5 the colour
/// after a pair, row 6 the wandering encounter line; then the capture buffer and the BBS adapter's local
/// branches (its socket branch: SocketMarkup128Tests).
/// </summary>
[Collection("SharedGameSingletons")]
public class WriteMarkup128Tests
{
    private const string Esc = "\u001b";
    private const string Yellow = Esc + "[33m";
    private const string Red = Esc + "[31m";
    private const string White = Esc + "[37m";
    private const string Reset = Esc + "[0m";
    private const BindingFlags PrivStatic = BindingFlags.NonPublic | BindingFlags.Static;
    private static readonly Regex Tag = new(@"\[(/|[a-z_]+)\]", RegexOptions.IgnoreCase);
    private readonly ITestOutputHelper _out;
    public WriteMarkup128Tests(ITestOutputHelper output) { _out = output; }

    /// <summary>Texts with no [colour]...[/] pair: menu keys, lone brackets, a tag that is never closed.</summary>
    public static readonly string[] NoPair =
    {
        "[A]ttack", "[1] Sword", "[Enter]", "[", "]", "a [ b ] c", "[red]never closed", "[zzz] unknown name",
        "[N]ext  [P]rev  [Q]uit", " [12] ", "[####....] 50%", "plain text", "[/color]", "/]", "[ /]", "[/ ]",
    };

    public static IEnumerable<object[]> NoPairCases() => NoPair.Select(t => new object[] { t });

    private sealed class NoInput : Stream
    {
        public override int Read(byte[] buffer, int offset, int count) => 0;
        public override bool CanRead => true;
        public override bool CanSeek => false;
        public override bool CanWrite => false;
        public override long Length => 0;
        public override long Position { get => 0; set => throw new NotSupportedException(); }
        public override void Flush() { }
        public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();
        public override void SetLength(long value) => throw new NotSupportedException();
        public override void Write(byte[] buffer, int offset, int count) => throw new NotSupportedException();
    }

    /// <summary>What a stream session (online play) receives for the given writes.</summary>
    private static string Sent(Action<TerminalEmulator> write, Action<TerminalEmulator>? setup = null)
    {
        var output = new MemoryStream();
        var terminal = new TerminalEmulator(new NoInput(), output);
        setup?.Invoke(terminal);
        write(terminal);
        return Encoding.UTF8.GetString(output.ToArray());
    }

    /// <summary>What the console receives from a terminal with no stream: the door path (ANSI codes) when
    /// online is true, the console fallback when it is false.</summary>
    private static string OnConsole(bool online, Action<TerminalEmulator> write)
    {
        BBSTerminalAdapter.Instance.Should().BeNull("these paths are the ones with no BBS adapter");
        DoorMode.IsInDoorMode.Should().BeFalse("no drop file session in the tests");
        var field = typeof(DoorMode).GetField("_onlineMode", PrivStatic)!;
        bool oldOnline = (bool)field.GetValue(null)!;
        var oldOut = Console.Out;
        var captured = new StringWriter();
        try
        {
            field.SetValue(null, online);
            Console.SetOut(captured);
            write(new TerminalEmulator());
            return captured.ToString();
        }
        finally
        {
            Console.SetOut(oldOut);
            field.SetValue(null, oldOnline);
        }
    }

    /// <summary>The plain text of the stream path, by the terminal's own unchanged helper.</summary>
    private static string PlainOf(string text) =>
        (string)typeof(TerminalEmulator).GetMethod("ToPlainText", PrivStatic)!.Invoke(null, new object[] { text })!;

    // ---------------------------------------------------------------- row 1: the stream path

    [Fact]
    public void Row1_Stream_AMarkedWrite_SendsTheColour_TheText_AndNoTag()
    {
        string sent = Sent(t => t.Write("You encounter [red]1 Wolf[/]", "yellow"));
        sent.Should().Be(Yellow + "You encounter " + Red + "1 Wolf" + Reset + Yellow);
        Tag.IsMatch(sent).Should().BeFalse("no tag character reaches the player");
        UIHelper.StripAnsi(sent).Should().Be("You encounter 1 Wolf");
        sent.Should().NotContain("\n", "Write ends no line");
    }

    [Fact]
    public void Row1_Stream_WriteWithoutAColour_UsesTheCurrentColour()
    {
        // SetColor sends the colour itself, then the Write sends it again, as before
        Sent(t => { t.SetColor("yellow"); t.Write("[red]1 Wolf[/]"); }).Should().Be(Yellow + Yellow + Red + "1 Wolf" + Reset + Yellow);
    }

    [Fact]
    public void Row1_Stream_Cp437_SendsTheSameRender()
    {
        string sent = Sent(t => t.Write("You encounter [red]1 Wolf[/]", "yellow"), t => t.UseCp437 = true);
        sent.Should().Be(Yellow + "You encounter " + Red + "1 Wolf" + Reset + Yellow);
    }

    [Fact]
    public void Row1_Stream_SpectatorsReceiveTheSameRender()
    {
        var watched = new MemoryStream();
        var player = new TerminalEmulator(new NoInput(), new MemoryStream());
        player.AddSpectatorStream(new TerminalEmulator(new NoInput(), watched));
        player.Write("You encounter [red]1 Wolf[/]", "yellow");
        Encoding.UTF8.GetString(watched.ToArray()).Should().Be(Yellow + "You encounter " + Red + "1 Wolf" + Reset + Yellow);
    }

    [Fact]
    public void Row1_Stream_NestedPairs_AreRenderedByTheSameParserAsWriteLine()
    {
        const string text = "[red]a[green]b[/]c[/]d";
        string line = Sent(t => t.WriteLine(text, "yellow"));
        line.Should().Be(Yellow + Red + "a" + Esc + "[32mb" + Reset + "c" + Reset + "d\r\n" + Reset);
        // Write: the same parse, the colour of the Write after each reset, no line end
        Sent(t => t.Write(text, "yellow")).Should().Be(Yellow + Red + "a" + Esc + "[32mb" + Reset + Yellow + "c" + Reset + Yellow + "d");
    }

    // ---------------------------------------------------------------- row 2: plain text (screen reader clients)

    [Fact]
    public void Row2_PlainText_AMarkedWrite_IsTextOnly()
    {
        string sent = Sent(t => t.Write("You encounter [red]1 Wolf[/]", "yellow"), t => t.IsPlainText = true);
        sent.Should().Be("You encounter 1 Wolf");
        sent.Should().NotContain(Esc);
    }

    [Fact]
    public void Row2_PlainText_ScreenReaderMode_IsTextOnlyToo()
    {
        bool old = GameConfig.ScreenReaderMode;
        try
        {
            GameConfig.ScreenReaderMode = true;
            Sent(t => t.Write("You encounter [red]1 Wolf[/]", "yellow"), t => t.IsPlainText = true).Should().Be("You encounter 1 Wolf");
        }
        finally { GameConfig.ScreenReaderMode = old; }
    }

    // ---------------------------------------------------------------- row 3: the door path and the console fallback

    [Fact]
    public void Row3_Door_AMarkedWrite_SendsTheColour_TheText_AndNoTag()
    {
        string sent = OnConsole(true, t => t.Write("You encounter [red]1 Wolf[/]", "yellow"));
        sent.Should().Be(Yellow + "You encounter " + Red + "1 Wolf" + Reset + Yellow);
        Tag.IsMatch(sent).Should().BeFalse();
    }

    [Fact]
    public void Row3_ConsoleFallback_AMarkedWrite_SendsTheTextAndNoTag()
    {
        // the colours of this path are Console.ForegroundColor, which a captured Console.Out does not carry
        OnConsole(false, t => t.Write("You encounter [red]1 Wolf[/]", "yellow")).Should().Be("You encounter 1 Wolf");
    }

    // ---------------------------------------------------------------- row 4: a text without a pair is unchanged

    [Theory]
    [MemberData(nameof(NoPairCases))]
    public void Row4_ATextWithoutAPair_IsSentAsBefore_OnEveryPath(string text)
    {
        // the bytes of 3a8dd9dc: the colour code, then the text as it is
        Sent(t => t.Write(text, "yellow")).Should().Be(Yellow + text, "stream");
        Sent(t => t.Write(text, "yellow"), t => t.UseCp437 = true).Should().Be(Yellow + text, "stream, CP437");
        OnConsole(true, t => t.Write(text, "yellow")).Should().Be(Yellow + text, "door");
        OnConsole(false, t => t.Write(text, "yellow")).Should().Be(text, "console fallback");
        // plain text: the terminal's own helper, as before (it has always taken "[A]" for a tag)
        Sent(t => t.Write(text, "yellow"), t => t.IsPlainText = true).Should().Be(PlainOf(text), "plain text");
        var capture = new TerminalEmulator(new NoInput(), new MemoryStream());
        capture.StartCapture();
        capture.Write(text, "yellow");
        capture.StopCapture().Should().Be(Yellow + text, "capture buffer");
    }

    [Fact]
    public void Row4_MenuKeys_KeepTheirBrackets()
    {
        Sent(t => { t.Write("[", "white"); t.Write("A", "bright_yellow"); t.Write("]ttack ", "white"); t.Write("[1] Sword", "cyan"); })
            .Should().Be(White + "[" + Esc + "[93mA" + White + "]ttack " + Esc + "[36m[1] Sword");
        UIHelper.StripAnsi(OnConsole(true, t => { t.Write("[Enter]", "yellow"); t.Write(" [A]ll", "yellow"); })).Should().Be("[Enter] [A]ll");
    }

    [Fact]
    public void Row4_TheRuleIsWriteLines_AClosingTagAlone_OrAnUnknownName_IsReadAsWriteLineReadsIt()
    {
        // WriteLine's rule: the text holds "[" and "[/]"; then any [name] opens a pair and a lone [/] is dropped.
        Sent(t => t.WriteLine("x[/]y", "yellow")).Should().Be(Yellow + "xy\r\n" + Reset);
        Sent(t => t.Write("x[/]y", "yellow")).Should().Be(Yellow + "xy");
        Sent(t => t.WriteLine("[zzz]x[/]y", "yellow")).Should().Be(Yellow + White + "x" + Reset + "y\r\n" + Reset);
        Sent(t => t.Write("[zzz]x[/]y", "yellow")).Should().Be(Yellow + White + "x" + Reset + Yellow + "y");
    }

    // ---------------------------------------------------------------- row 5: the colour after a pair

    [Fact]
    public void Row5_AfterAPair_TheColourOfTheWriteComesBack()
    {
        const string text = "[red]5 Wolves[/] from the Canine family!";
        Sent(t => t.Write(text, "yellow")).Should().Be(Yellow + Red + "5 Wolves" + Reset + Yellow + " from the Canine family!", "stream");
        OnConsole(true, t => t.Write(text, "yellow")).Should().Be(Yellow + Red + "5 Wolves" + Reset + Yellow + " from the Canine family!", "door");
        // the piece that follows sends its own colour, as before
        Sent(t => { t.Write("[red]1 Wolf[/]", "yellow"); t.Write(", ", "yellow"); })
            .Should().Be(Yellow + Red + "1 Wolf" + Reset + Yellow + Yellow + ", ");
    }

    [Fact]
    public void Row5_WriteLine_IsNotChanged_ItLeavesTheResetAfterAPair()
    {
        const string text = "[red]5 Wolves[/] from the Canine family!";
        Sent(t => t.WriteLine(text, "yellow")).Should().Be(Yellow + Red + "5 Wolves" + Reset + " from the Canine family!\r\n" + Reset);
        OnConsole(true, t => t.WriteLine(text, "yellow")).Should().Be(Yellow + Red + "5 Wolves" + Reset + " from the Canine family!" + Environment.NewLine + Reset);
    }

    // ---------------------------------------------------------------- row 6: the wandering encounter line

    private static readonly Dictionary<string, string> Codes = new()
    {
        ["bright_red"] = "91", ["green"] = "32", ["cyan"] = "36", ["magenta"] = "35", ["bright_blue"] = "94",
    };

    [Theory]
    [InlineData("en")]
    [InlineData("hu")]
    public void Row6_TheWanderingEncounterLine_OnTheStream_ShowsNoTag_AndNoRowPasses79(string lang)
    {
        var group = EncounterLine128Tests.Coloured(EncounterLine128Tests.WorstGroup(lang));
        string sent = EncounterLine128Tests.In(lang, () => Sent(t =>
        {
            t.SetColor("yellow");
            DungeonLocation.WriteWanderingEncounterLine(t, group, EncounterLine128Tests.Plural);
            t.WriteLine("");
        }));
        Tag.IsMatch(sent).Should().BeFalse("{0}: no tag character reaches the player: {1}", lang, UIHelper.StripAnsi(sent));
        var rows = UIHelper.StripAnsi(sent).Replace("\r\n", "\n").TrimEnd('\n').Split('\n');
        rows.Length.Should().BeGreaterThan(1, "the worst case wraps");
        foreach (string row in rows)
        {
            _out.WriteLine($"{lang} {row.Length,3} {row}");
            row.Length.Should().BeLessThanOrEqualTo(79, "{0}: [{1}]", lang, row);
            row.Should().NotContain("[").And.NotContain("]");
        }
        string.Join(" ", rows).Should().Be(EncounterLine128Tests.WanderingText(lang, group));
        // each member is sent in its own colour, and the row goes back to yellow after it
        foreach (var m in group)
        {
            string name = EncounterLine128Tests.In(lang, () => MonsterNames.Count(1, m, EncounterLine128Tests.Plural));
            sent.Should().Contain(Esc + "[" + Codes[m.MonsterColor] + "m1", "{0}: {1}", lang, name);
            sent.Should().Contain(name.Split(' ').Last() + Reset + Yellow, "{0}: {1}", lang, name);
        }
    }

    [Theory]
    [InlineData("en")]
    [InlineData("hu")]
    public void Row6_TheFamilyLine_OnTheStream_ShowsNoTag_AndItsTailStaysYellow(string lang)
    {
        string text = EncounterLine128Tests.In(lang, () => Loc.Get("dungeon.encounter_group_family", "[red]5 Wolves[/]", "Canine"));
        string sent = Sent(t => { t.SetColor("yellow"); t.Write(text); t.WriteLine(""); });
        Tag.IsMatch(sent).Should().BeFalse();
        string tail = text.Substring(text.IndexOf("[/]", StringComparison.Ordinal) + 3);
        tail.Should().NotBeEmpty();
        sent.Should().Contain(Red + "5 Wolves" + Reset + Yellow + tail);
        UIHelper.StripAnsi(sent).TrimEnd('\r', '\n').Length.Should().BeLessThanOrEqualTo(79);
    }

    // ---------------------------------------------------------------- the capture buffer (group combat broadcast)

    [Fact]
    public void TheCaptureBuffer_RendersAMarkedWrite_AsBefore()
    {
        var terminal = new TerminalEmulator(new NoInput(), new MemoryStream());
        terminal.StartCapture();
        terminal.Write("[red]5 Wolves[/] from the Canine family!", "yellow");
        // as at 3a8dd9dc: rendered, and the reset is left after the pair (not changed here)
        terminal.StopCapture().Should().Be(Yellow + Red + "5 Wolves" + Reset + " from the Canine family!");
    }

    // ---------------------------------------------------------------- the column count

    [Fact]
    public void TheColumn_CountsNoTagOfAPair()
    {
        var terminal = new TerminalEmulator(new NoInput(), new MemoryStream());
        terminal.Write("[red]ab[/]", "yellow");
        // neither "[red]" nor "[/]" takes a column: the two letters
        terminal.Column.Should().Be(2);
        terminal.Write("[red]cd[/] and [green]ef[/]", "yellow");
        terminal.Column.Should().Be(2 + 9);
    }

    [Theory]
    [InlineData("[/]", 0)]
    [InlineData("a[/]b", 2)]
    [InlineData("[red]", 0)]
    [InlineData("[bright_red]ab", 2)]
    [InlineData("[/red]", 0)]
    [InlineData("[A]ttack", 8)]
    [InlineData("[RED]", 5)]
    [InlineData("[1] Sword", 9)]
    [InlineData("[", 1)]
    [InlineData("[/ ]", 4)]
    [InlineData("[//]", 4)]
    [InlineData("\u001b[31mab\u001b[0m", 2)]
    [InlineData("plain text", 10)]
    public void TheColumn_OfAWrite_CountsTheClosingTagAsNothing_AndEveryOtherTextAsBefore(string text, int columns)
    {
        // every row but the first two had this count before the closing tag was added to the pattern
        var terminal = new TerminalEmulator(new NoInput(), new MemoryStream());
        terminal.Write(text, "yellow");
        terminal.Column.Should().Be(columns);
    }

    [Fact]
    public void ATailAfterAMarkedWrite_StartsAtTheColumnThePlayerSees()
    {
        // 60 + 2 columns written, then a tail of 17: 79 in all, one row. With "[/]" counted as 3 columns the
        // tail was laid out from column 65 and wrapped.
        GameConfig.ScreenReaderMode.Should().BeFalse();
        string head = new string('x', 60);
        string tail = " " + new string('y', 16);
        var terminal = new TerminalEmulator(new NoInput(), new MemoryStream());
        terminal.Write(head + "[red]ab[/]", "yellow");
        terminal.Column.Should().Be(62);
        UIHelper.TailRows(terminal.Column, 4, new (string?, string)[] { ("gray", tail) }).Count.Should().Be(1);
        string sent = Sent(t =>
        {
            t.Write(head + "[red]ab[/]", "yellow");
            UIHelper.WriteTail(t, 4, ("gray", tail));
            t.WriteLine("");
        });
        var rows = UIHelper.StripAnsi(sent).Split("\r\n");
        rows.Should().Equal(head + "ab" + tail, "");
        rows[0].Length.Should().Be(79);
    }

    // ---------------------------------------------------------------- the BBS adapter

    private static string ThroughAdapter(ConnectionType comm, bool ansiForLocal, Action<BBSTerminalAdapter> write)
    {
        var instance = typeof(BBSTerminalAdapter).GetProperty("Instance", BindingFlags.Public | BindingFlags.Static)!;
        var old = BBSTerminalAdapter.Instance;
        var oldOut = Console.Out;
        var captured = new StringWriter();
        try
        {
            Console.SetOut(captured);
            // a terminal that was never connected: its writes fall to the console
            var adapter = new BBSTerminalAdapter(new SocketTerminal(new BBSSessionInfo { CommType = comm }), ansiForLocal);
            write(adapter);
            return captured.ToString();
        }
        finally
        {
            Console.SetOut(oldOut);
            instance.SetValue(null, old);
        }
    }

    [Fact]
    public void TheBbsAdapter_LocalBranches_RenderAMarkedWrite()
    {
        string ansi = ThroughAdapter(ConnectionType.Local, true, a => a.Write("You encounter [red]1 Wolf[/]!", "yellow"));
        Tag.IsMatch(ansi).Should().BeFalse();
        UIHelper.StripAnsi(ansi).Should().Be("You encounter 1 Wolf!");
        ansi.Should().Contain(Esc + "[");
        ThroughAdapter(ConnectionType.Local, false, a => a.Write("You encounter [red]1 Wolf[/]!", "yellow")).Should().Be("You encounter 1 Wolf!");
    }
}
