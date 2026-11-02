using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using FluentAssertions;
using UsurperReborn.Tests.Localization;
using Xunit;

namespace UsurperReborn.Tests;

/// <summary>
/// 1.3.9: the WezTerm game window opens at the size the player last left it at. wezterm.lua reads
/// window-size.txt (beside it, like font-choice.txt) before it sets initial_cols and initial_rows, keeps 80x50
/// for a missing, unreadable or out of range file, and saves the size on window-resized (never full screen,
/// only an in range size that changed, every read and write under pcall). The file is never on the config
/// reload watch list and never shipped. No WezTerm runs in CI, so these checks read the config text.
/// </summary>
public class WeztermWindowSize139Tests
{
    private static string Read(params string[] parts) =>
        File.ReadAllText(Path.Combine(new[] { HardcodedTextScannerTests.RepoRoot() }.Concat(parts).ToArray()));

    private static string Lua => Read("wezterm.lua");

    private static string Handler()
    {
        string lua = Lua;
        int start = lua.IndexOf("wezterm.on('window-resized'", System.StringComparison.Ordinal);
        start.Should().BeGreaterThan(0, "the resize handler is registered");
        int end = lua.IndexOf("\nend)", start, System.StringComparison.Ordinal);
        return lua.Substring(start, end - start);
    }

    [Fact]
    public void TheSavedSize_IsReadBeforeTheWindowSizeIsSet_AndTheDefaultStays80By50()
    {
        string lua = Lua;
        int read = lua.IndexOf("local saved_cols, saved_rows = read_window_size()", System.StringComparison.Ordinal);
        int cols = lua.IndexOf("config.initial_cols = saved_cols or 80", System.StringComparison.Ordinal);
        int rows = lua.IndexOf("config.initial_rows = saved_rows or 50", System.StringComparison.Ordinal);
        read.Should().BeGreaterThan(0);
        cols.Should().BeGreaterThan(read, "the saved size is read first");
        rows.Should().BeGreaterThan(read);
        Regex.Matches(lua, @"^\s*config\.initial_cols\s*=", RegexOptions.Multiline).Count.Should().Be(1, "no later line puts 80 back");
        Regex.Matches(lua, @"^\s*config\.initial_rows\s*=", RegexOptions.Multiline).Count.Should().Be(1);
    }

    [Fact]
    public void TheSizeFile_SitsBesideTheConfig_AndIsReadUnderPcall_WithBounds()
    {
        string lua = Lua;
        lua.Should().Contain("local SIZE_FILE = config_dir .. sep .. 'window-size.txt'");
        lua.Should().Contain("local MIN_COLS, MAX_COLS, MIN_ROWS, MAX_ROWS = 80, 400, 24, 200");
        string reader = lua.Substring(lua.IndexOf("local function read_window_size()", System.StringComparison.Ordinal));
        reader = reader.Substring(0, reader.IndexOf("\nend", System.StringComparison.Ordinal));
        reader.Should().Contain("pcall(function()", "a bad file never breaks startup");
        reader.Should().Contain("if ok and size_in_bounds(cols, rows) then return cols, rows end", "only an in range size is used");
        lua.Should().Contain("cols >= MIN_COLS and cols <= MAX_COLS and rows >= MIN_ROWS and rows <= MAX_ROWS");
    }

    [Fact]
    public void TheResizeHandler_SkipsFullScreen_SavesOnlyAChangedInRangeSize_UnderPcall()
    {
        string h = Handler();
        h.Should().Contain("pcall(function()");
        h.Should().Contain("if window:get_dimensions().is_full_screen then return end");
        h.Should().Contain("local cols, rows = dims.cols, dims.viewport_rows");
        h.Should().Contain("if not size_in_bounds(cols, rows) then return end");
        h.Should().Contain("if cols == last_saved.cols and rows == last_saved.rows then return end");
        h.IndexOf("io.open(SIZE_FILE, 'w')", System.StringComparison.Ordinal)
            .Should().BeGreaterThan(h.IndexOf("last_saved.rows then return end", System.StringComparison.Ordinal), "the write comes after every check");
    }

    [Fact]
    public void TheSizeFile_IsNeverWatched_AndNeverShipped()
    {
        Lua.Should().NotContain("add_to_config_reload_watch_list", "a resize must not reload the config");
        Lua.Should().NotContain("require 'window-size", "the file is read with io.open");
        Read(".github", "workflows", "ci-cd.yml").Should().NotContain("window-size.txt", "the release never carries a size, so an update keeps the player's");
    }
}
