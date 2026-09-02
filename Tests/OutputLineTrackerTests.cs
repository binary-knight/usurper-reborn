using System.IO;
using System.Text;
using Xunit;
using FluentAssertions;
using UsurperRemake.Server;

namespace UsurperReborn.Tests;

/// <summary>
/// v1.0.6 input-stomping fix: the tracked output line is what a mid-input redraw
/// restores, so it must hold exactly the visible prompt (text plus colour codes)
/// and nothing that would move the cursor or replay stale text.
/// </summary>
public class OutputLineTrackerTests
{
    [Fact]
    public void Tracks_text_since_last_newline_and_keeps_colour_codes()
    {
        var t = new OutputLineTracker();
        t.Track("Welcome\r\n\x1b[37m[\x1b[92m27hp\x1b[37m] Main Street > ");
        t.CurrentLine.Should().Be("\x1b[37m[\x1b[92m27hp\x1b[37m] Main Street > ");
    }

    [Theory]
    [InlineData("\n")]
    [InlineData("\r")]
    [InlineData("\x1b[2J")]
    [InlineData("\x1b[2K")]
    public void Resets_on_newline_carriage_return_and_clears(string reset)
    {
        var t = new OutputLineTracker();
        t.Track("old prompt > ");
        t.Track(reset);
        t.CurrentLine.Should().BeEmpty();
    }

    [Fact]
    public void Drops_cursor_movement_but_not_the_text_around_it()
    {
        var t = new OutputLineTracker();
        t.Track("\x1b[2J\x1b[H\x1b[1;36mTitle\x1b[0m");
        t.CurrentLine.Should().Be("\x1b[1;36mTitle\x1b[0m", "ESC[H must not be replayed on redraw");
    }

    [Fact]
    public void Suppress_ignores_writes_such_as_keystroke_echo()
    {
        var t = new OutputLineTracker();
        t.Track("prompt > ");
        t.Suppress = true;
        t.Track("hello");
        t.Suppress = false;
        t.CurrentLine.Should().Be("prompt > ");
    }

    [Fact]
    public void Incomplete_escape_sequence_is_not_exposed()
    {
        var t = new OutputLineTracker();
        t.Track("abc\x1b[3");
        t.CurrentLine.Should().Be("abc");
        t.Track("2m");
        t.CurrentLine.Should().Be("abc\x1b[32m");
    }

    [Fact]
    public void Tracking_writer_sees_every_write_path_once()
    {
        var tracker = new OutputLineTracker();
        using var ms = new MemoryStream();
        using var w = new LineTrackingStreamWriter(ms, new UTF8Encoding(false), tracker) { AutoFlush = true };
        w.Write('[');
        w.Write("12hp");
        w.Write(new[] { ']', ' ' }, 0, 2);
        w.Write("Inn > ".AsSpan());
        tracker.CurrentLine.Should().Be("[12hp] Inn > ");
        w.WriteLine("gone");
        tracker.CurrentLine.Should().BeEmpty();
        Encoding.UTF8.GetString(ms.ToArray()).Should().Be("[12hp] Inn > gone" + w.NewLine);
    }

    [Theory]
    [InlineData("MUD", "MUD", false)]
    [InlineData("SSH;echo=1", "SSH", true)]
    [InlineData("SSH;echo=0", "SSH", false)]
    [InlineData("Steam;version=2", "Steam", false)]
    [InlineData(" Web ; echo = 1 ", "Web", true)]
    [InlineData("", "", false)]
    [InlineData(null, "", false)]
    public void Connection_type_parameters_are_stripped_and_echo_flag_read(string? input, string expectedType, bool expectedEcho)
    {
        var type = MudServer.ParseConnectionTypeParams(input, out bool echo);
        type.Should().Be(expectedType);
        echo.Should().Be(expectedEcho);
    }
}
