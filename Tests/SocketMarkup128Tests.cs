using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text;
using System.Text.RegularExpressions;
using FluentAssertions;
using UsurperRemake.BBS;
using UsurperRemake.UI;
using Xunit;
using Xunit.Abstractions;

namespace UsurperReborn.Tests;

/// <summary>
/// 1.2.8, the BBS socket branch (a door session whose comm type is not local) and [colour]...[/] markup.
/// The adapter's socket branch sent the text with its tags in Write and in WriteLine. It now renders a text
/// with a pair through SocketTerminal.WriteMarkupAsync: the runs of TerminalEmulator.MarkupSegments, each
/// through SetColorAsync (the terminal's colour state and its emulation check) and WriteAsync.
/// The bytes are recorded in process: a SocketTerminal that was never connected writes to the console, and
/// the console is captured. No socket is opened.
/// </summary>
[Collection("SharedGameSingletons")]
public class SocketMarkup128Tests
{
    private const string Esc = "\u001b";
    private const string Yellow = Esc + "[0;33m";
    private const string Red = Esc + "[0;31m";
    private const string Green = Esc + "[0;32m";
    private const BindingFlags PrivStatic = BindingFlags.NonPublic | BindingFlags.Static;
    private static readonly Regex Tag = new(@"\[(/|[a-z_]+)\]", RegexOptions.IgnoreCase);
    private static readonly Regex Sgr = new("\u001b\\[([0-9;]*)m");
    private readonly ITestOutputHelper _out;
    public SocketMarkup128Tests(ITestOutputHelper output) { _out = output; }

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

    /// <summary>What a socket session of the given emulation receives for the adapter's writes.</summary>
    private static string OnSocket(TerminalEmulation emulation, Action<BBSTerminalAdapter> write)
    {
        var instance = typeof(BBSTerminalAdapter).GetProperty("Instance", BindingFlags.Public | BindingFlags.Static)!;
        var old = BBSTerminalAdapter.Instance;
        var oldOut = Console.Out;
        var captured = new StringWriter();
        try
        {
            Console.SetOut(captured);
            var session = new BBSSessionInfo { CommType = ConnectionType.Telnet, Emulation = emulation };
            write(new BBSTerminalAdapter(new SocketTerminal(session)));
            return captured.ToString();
        }
        finally
        {
            Console.SetOut(oldOut);
            instance.SetValue(null, old);
        }
    }

    /// <summary>What a socket session receives for writes made on a TerminalEmulator (its socket branch).</summary>
    private static string ThroughTheTerminal(TerminalEmulation emulation, Action<TerminalEmulator> write)
    {
        var door = typeof(DoorMode).GetField("_sessionInfo", PrivStatic)!;
        var oldDoor = door.GetValue(null);
        try
        {
            return OnSocket(emulation, _ =>
            {
                door.SetValue(null, new BBSSessionInfo { CommType = ConnectionType.Telnet, Emulation = emulation });
                DoorMode.IsInDoorMode.Should().BeFalse("no drop file: the page prompt stays off");
                write(new TerminalEmulator());
            });
        }
        finally { door.SetValue(null, oldDoor); }
    }

    /// <summary>What a stream session (online play) receives for the same write.</summary>
    private static string OnStream(Action<TerminalEmulator> write)
    {
        var output = new MemoryStream();
        write(new TerminalEmulator(new NoInput(), output));
        return Encoding.UTF8.GetString(output.ToArray());
    }

    /// <summary>
    /// Each character with the colour it shows in. The stream sends "31" or "91" where the socket terminal
    /// sends "0;31" or "1;31": both are read as one colour. A reset shows as "reset".
    /// </summary>
    private static List<(char Ch, string Color)> Shown(string sent, string start)
    {
        var shown = new List<(char, string)>();
        string color = start;
        int pos = 0;
        foreach (Match m in Sgr.Matches(sent))
        {
            foreach (char c in sent.Substring(pos, m.Index - pos)) shown.Add((c, color));
            color = Colour(m.Groups[1].Value);
            pos = m.Index + m.Length;
        }
        foreach (char c in sent.Substring(pos)) shown.Add((c, color));
        return shown;
    }

    private static string Colour(string code)
    {
        if (code == "0" || code == "") return "reset";
        if (code.StartsWith("0;")) return code.Substring(2);
        if (code.StartsWith("1;3")) return "9" + code.Substring(3);
        return code;
    }

    private static string Text(List<(char Ch, string Color)> shown) => new(shown.Select(s => s.Ch).ToArray());

    // ---------------------------------------------------------------- the recorded bytes, Write

    [Fact]
    public void Write_OnePair_SendsTheText_ThePairInItsColour_TheRestInTheBaseColour_AndNoTag()
    {
        string sent = OnSocket(TerminalEmulation.ANSI, a => a.Write("You encounter [red]1 Wolf[/]!", "yellow"));
        sent.Should().Be(Yellow + "You encounter " + Red + "1 Wolf" + Yellow + "!");
        Tag.IsMatch(sent).Should().BeFalse("no tag reaches the player");
        sent.Should().NotContain("\n", "Write ends no line");
    }

    [Fact]
    public void Write_TwoPairs_EachInItsColour()
    {
        OnSocket(TerminalEmulation.ANSI, a => a.Write("[red]2 Wolves[/] and [green]1 Sprite[/] wait", "yellow"))
            .Should().Be(Red + "2 Wolves" + Yellow + " and " + Green + "1 Sprite" + Yellow + " wait");
    }

    [Fact]
    public void Write_APairAtTheEnd_LeavesTheBaseColour()
    {
        OnSocket(TerminalEmulation.ANSI, a => a.Write("You encounter [red]1 Wolf[/]", "yellow"))
            .Should().Be(Yellow + "You encounter " + Red + "1 Wolf" + Yellow);
    }

    [Fact]
    public void Write_AFollowingPlainWrite_GetsItsOwnColour()
    {
        // the terminal's colour state follows the pair: a write in the pair's colour after it sends that colour again
        OnSocket(TerminalEmulation.ANSI, a => { a.Write("a [red]b[/] c", "yellow"); a.Write("d", "red"); a.Write("e", "red"); a.Write("f", "yellow"); })
            .Should().Be(Yellow + "a " + Red + "b" + Yellow + " c" + Red + "de" + Yellow + "f");
    }

    [Fact]
    public void Write_AsciiEmulation_SendsTheTextAlone()
    {
        string sent = OnSocket(TerminalEmulation.ASCII, a => { a.Write("You encounter [red]1 Wolf[/]!", "yellow"); a.Write(" Run", "red"); });
        sent.Should().Be("You encounter 1 Wolf! Run");
        sent.Should().NotContain(Esc);
    }

    // ---------------------------------------------------------------- the recorded bytes, WriteLine

    [Fact]
    public void WriteLine_OnePair_SendsTheText_ThePairInItsColour_TheRestInTheBaseColour_AndNoTag()
    {
        string sent = OnSocket(TerminalEmulation.ANSI, a => a.WriteLine("You encounter [red]1 Wolf[/]!", "yellow"));
        sent.Should().Be(Yellow + "You encounter " + Red + "1 Wolf" + Yellow + "!\r\n");
        Tag.IsMatch(sent).Should().BeFalse("no tag reaches the player");
    }

    [Fact]
    public void WriteLine_TwoPairs_EachInItsColour()
    {
        OnSocket(TerminalEmulation.ANSI, a => a.WriteLine("[red]2 Wolves[/] and [green]1 Sprite[/] wait", "yellow"))
            .Should().Be(Red + "2 Wolves" + Yellow + " and " + Green + "1 Sprite" + Yellow + " wait\r\n");
    }

    [Fact]
    public void WriteLine_APairAtTheEnd_LeavesTheBaseColour_BeforeTheLineEnd()
    {
        OnSocket(TerminalEmulation.ANSI, a => a.WriteLine("You encounter [red]1 Wolf[/]", "yellow"))
            .Should().Be(Yellow + "You encounter " + Red + "1 Wolf" + Yellow + "\r\n");
    }

    [Fact]
    public void WriteLine_AFollowingPlainLine_GetsItsOwnColour()
    {
        OnSocket(TerminalEmulation.ANSI, a => { a.WriteLine("a [red]b[/] c", "yellow"); a.WriteLine("d", "red"); a.WriteLine("e", "yellow"); })
            .Should().Be(Yellow + "a " + Red + "b" + Yellow + " c\r\n" + Red + "d\r\n" + Yellow + "e\r\n");
    }

    [Fact]
    public void WriteLine_AsciiEmulation_SendsTheTextAlone()
    {
        string sent = OnSocket(TerminalEmulation.ASCII, a => { a.WriteLine("You encounter [red]1 Wolf[/]!", "yellow"); a.WriteLine("Run", "red"); });
        sent.Should().Be("You encounter 1 Wolf!\r\nRun\r\n");
        sent.Should().NotContain(Esc);
    }

    // ---------------------------------------------------------------- a text without a pair is sent as before

    public static IEnumerable<object[]> NoPairCases() => WriteMarkup128Tests.NoPair.Select(t => new object[] { t });

    [Theory]
    [MemberData(nameof(NoPairCases))]
    public void ATextWithoutAPair_IsSentAsItIs_InWriteAndInWriteLine(string text)
    {
        OnSocket(TerminalEmulation.ANSI, a => a.Write(text, "yellow")).Should().Be(Yellow + text);
        OnSocket(TerminalEmulation.ANSI, a => a.WriteLine(text, "yellow")).Should().Be(Yellow + text + "\r\n");
        OnSocket(TerminalEmulation.ASCII, a => a.Write(text, "yellow")).Should().Be(text);
        OnSocket(TerminalEmulation.ASCII, a => a.WriteLine(text, "yellow")).Should().Be(text + "\r\n");
    }

    [Fact]
    public void AnEmptyLine_IsALineEnd()
    {
        OnSocket(TerminalEmulation.ANSI, a => a.WriteLine()).Should().Be("\r\n");
        OnSocket(TerminalEmulation.ANSI, a => a.WriteLine("", "yellow")).Should().Be(Yellow + "\r\n");
    }

    // ---------------------------------------------------------------- the same render as the stream branch

    /// <summary>Texts with a pair: nested and unknown tags, a "[" with no pair, a "[word]" that is no colour,
    /// a "[/]" with no opening, upper case, an empty pair, a pair only.</summary>
    public static readonly string[] Marked =
    {
        "You encounter [red]1 Wolf[/]!",
        "[red]2 Wolves[/] and [green]1 Sprite[/] wait",
        "[red]a[blue]b[/]c[/]d",
        "[red]a[blue]b[green]c[/][/][/] d",
        "[zzz]unknown name[/] then text",
        "a [ b [red]c[/] d",
        "[1] Sword [red]cursed[/] ]",
        "[Enter] to go on, [red]or flee[/]",
        "[A]ttack the [red]Wolf[/]",
        "a[/]b [red]c[/]",
        "[RED]upper[/] case",
        "[red][/]empty pair",
        "[red]only[/]",
        "[bright_red]bright[/] and [dark_green]dark[/]",
        "[/]",
    };

    public static IEnumerable<object[]> MarkedCases() => Marked.Select(t => new object[] { t });

    [Theory]
    [MemberData(nameof(MarkedCases))]
    public void Write_AMarkedText_ShowsWhatTheStreamShows_CharacterByCharacter(string text)
    {
        var stream = Shown(OnStream(t => t.Write(text, "yellow")), "33");
        var socket = Shown(OnSocket(TerminalEmulation.ANSI, a => a.Write(text, "yellow")), "37");
        _out.WriteLine("[{0}] shows [{1}]", text, Text(stream));
        Text(socket).Should().Be(Text(stream));
        socket.Should().Equal(stream, "each character has the colour the stream gives it");
        Tag.IsMatch(Text(socket)).Should().Be(Tag.IsMatch(Text(stream)));
        OnSocket(TerminalEmulation.ASCII, a => a.Write(text, "yellow")).Should().Be(Text(stream), "an ASCII session gets the text alone");
    }

    [Theory]
    [MemberData(nameof(MarkedCases))]
    public void WriteLine_AMarkedText_ShowsWhatTheStreamShows_CharacterByCharacter(string text)
    {
        // the stream's Write is the reference for the colours (its WriteLine leaves the text after a pair in the reset colour)
        var stream = Shown(OnStream(t => t.Write(text, "yellow")), "33");
        var socket = Shown(OnSocket(TerminalEmulation.ANSI, a => a.WriteLine(text, "yellow")), "37");
        Text(socket).Should().Be(Text(stream) + "\r\n");
        socket.Take(stream.Count).Should().Equal(stream, "each character has the colour the stream gives it");
        UIHelper.StripAnsi(OnStream(t => t.WriteLine(text, "yellow"))).Should().Be(Text(stream) + "\r\n", "the stream's WriteLine sends the same text");
        OnSocket(TerminalEmulation.ASCII, a => a.WriteLine(text, "yellow")).Should().Be(Text(stream) + "\r\n", "an ASCII session gets the text alone");
    }

    // ---------------------------------------------------------------- text a player supplied

    /// <summary>A name or a chat line holding "[red]" and a lone "[": with no "[/]" (sent as it is on every
    /// branch) and with one (rendered on every branch).</summary>
    public static readonly string[] Supplied =
    {
        "Rogue[red] says: look [ here",
        "Rogue[red] says: look [/] [ here",
        "[red][ [/]",
    };

    public static IEnumerable<object[]> SuppliedCases() => Supplied.Select(t => new object[] { t });

    [Theory]
    [MemberData(nameof(SuppliedCases))]
    public void Write_ATextAPlayerSupplied_IsSentAsTheStreamSendsIt_AndAnAsciiSessionGetsNoEscapeByte(string text)
    {
        var stream = Shown(OnStream(t => t.Write(text, "cyan")), "36");
        var socket = Shown(OnSocket(TerminalEmulation.ANSI, a => a.Write(text, "cyan")), "37");
        _out.WriteLine("[{0}] shows [{1}]", text, Text(stream));
        socket.Should().Equal(stream);
        string ascii = OnSocket(TerminalEmulation.ASCII, a => a.Write(text, "cyan"));
        ascii.Should().Be(Text(stream));
        ascii.Should().NotContain(Esc);
    }

    [Theory]
    [MemberData(nameof(SuppliedCases))]
    public void WriteLine_ATextAPlayerSupplied_IsSentAsTheStreamSendsIt_AndAnAsciiSessionGetsNoEscapeByte(string text)
    {
        var stream = Shown(OnStream(t => t.Write(text, "cyan")), "36");
        var socket = Shown(OnSocket(TerminalEmulation.ANSI, a => a.WriteLine(text, "cyan")), "37");
        socket.Take(stream.Count).Should().Equal(stream);
        string ascii = OnSocket(TerminalEmulation.ASCII, a => a.WriteLine(text, "cyan"));
        ascii.Should().Be(Text(stream) + "\r\n");
        ascii.Should().NotContain(Esc);
    }

    // ---------------------------------------------------------------- the terminal's socket branch

    [Fact]
    public void TheTerminal_OnASocketSession_RendersAMarkedWrite()
    {
        string sent = ThroughTheTerminal(TerminalEmulation.ANSI, t => { t.Write("You encounter [red]1 Wolf[/]", "yellow"); t.Write("!", "yellow"); });
        sent.Should().Be(Yellow + "You encounter " + Red + "1 Wolf" + Yellow + "!");
        ThroughTheTerminal(TerminalEmulation.ASCII, t => t.Write("You encounter [red]1 Wolf[/]!", "yellow")).Should().Be("You encounter 1 Wolf!");
    }

    [Fact]
    public void TheTerminal_OnASocketSession_RendersAMarkedLine()
    {
        string sent = ThroughTheTerminal(TerminalEmulation.ANSI, t => t.WriteLine("You encounter [red]1 Wolf[/]!", "yellow"));
        sent.Should().Be(Yellow + "You encounter " + Red + "1 Wolf" + Yellow + "!\r\n");
        ThroughTheTerminal(TerminalEmulation.ASCII, t => t.WriteLine("You encounter [red]1 Wolf[/]!", "yellow")).Should().Be("You encounter 1 Wolf!\r\n");
    }
}
