using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using FluentAssertions;
using UsurperRemake.Systems;
using UsurperRemake.UI;
using Xunit;

namespace UsurperReborn.Tests;

/// <summary>
/// Column measurement for wide scripts: East Asian characters, fullwidth forms, emoji and combining
/// marks. A terminal draws these differently from their UTF-16 length, so boxes, padding and wraps
/// must measure display columns. Chinese prose also has no spaces, so a row has to be breakable on a
/// column boundary.
/// </summary>
public class TextWidthWideTests
{
    [Theory]
    [InlineData("", 0)]
    [InlineData("abc", 3)]
    [InlineData("café", 4)]              // precomposed accent is one column
    [InlineData("é", 1)]           // combining acute draws on the previous column
    [InlineData("中文", 4)]
    [InlineData("こんにちは", 10)]
    [InlineData("한글", 4)]
    [InlineData("ＡＢ", 4)]              // fullwidth Latin
    [InlineData("😀", 2)]                // surrogate pair, one wide code point
    [InlineData("a😀b", 4)]
    [InlineData("a​b", 2)]          // zero width space
    public void Width_IsTerminalColumns(string text, int expected) =>
        TextWidth.Width(text).Should().Be(expected);

    [Fact]
    public void HasWide_FindsOnlyDoubleColumnText()
    {
        TextWidth.HasWide("plain ASCII").Should().BeFalse();
        TextWidth.HasWide("中文").Should().BeTrue();
        TextWidth.HasWide("Ａ").Should().BeTrue();
        TextWidth.HasWide("😀").Should().BeTrue();
    }

    [Fact]
    public void PadRight_And_PadLeft_CountColumns()
    {
        TextWidth.PadRight("中", 3).Should().Be("中 ");
        TextWidth.PadLeft("中", 3).Should().Be(" 中");
        TextWidth.PadRight("中文", 4).Should().Be("中文");
        TextWidth.Width(TextWidth.PadRight("中文", 6)).Should().Be(6);
        TextWidth.Width(TextWidth.PadLeft("中文", 6)).Should().Be(6);
    }

    [Fact]
    public void Truncate_NeverSplitsAWideCharacter()
    {
        TextWidth.Truncate("中文字", 4).Should().Be("中文");
        TextWidth.Truncate("中文字", 5, "...").Should().Be("中...");
        TextWidth.Truncate("abcdef", 4, "..").Should().Be("ab..");
        TextWidth.Truncate("abc", 10).Should().Be("abc");
    }

    [Fact]
    public void SplitToFit_BreaksWideTextOnColumnBoundaries()
    {
        var pieces = TextWidth.SplitToFit("中文字", 4);
        pieces.Should().Equal("中文", "字");
        pieces.Should().OnlyContain(piece => TextWidth.Width(piece) <= 4);
        string.Concat(pieces).Should().Be("中文字");
    }

    [Fact]
    public void LastSpaceWithin_CountsColumnsNotChars()
    {
        TextWidth.LastSpaceWithin("ab cd", 4).Should().Be(2);
        // The wide characters before the space use four columns already, so the space is out of reach.
        TextWidth.LastSpaceWithin("中文 ab", 4).Should().Be(-1);
        TextWidth.LastSpaceWithin("中文 ab", 5).Should().Be(2);
    }

    [Fact]
    public void BreakAt_NeverReturnsAnEmptyPrefix()
    {
        string prefix = TextWidth.BreakAt("中文字", 3, out string rest);
        prefix.Should().Be("中");
        rest.Should().Be("文字");
        TextWidth.Width(prefix).Should().BeLessOrEqualTo(3);

        // A single character wider than the limit is still taken, so a loop makes progress.
        TextWidth.BreakAt("中", 1, out string none).Should().Be("中");
        none.Should().BeEmpty();
    }

    [Fact]
    public void VisibleLength_StripsAnsiAndCountsWideCharacters()
    {
        UIHelper.VisibleLength("\u001b[31m中文\u001b[0m").Should().Be(4);
        UIHelper.VisibleLength("plain").Should().Be(5);
    }

    [Fact]
    public void WordWrap_BreaksChineseProseThatHasNoSpaces()
    {
        var lines = UIHelper.WordWrap("中文中文中文", 5);

        lines.Should().NotBeEmpty();
        lines.Should().OnlyContain(line => UIHelper.VisibleLength(line) <= 5);
        string.Concat(lines).Should().Be("中文中文中文");
    }

    [Fact]
    public void WordWrap_KeepsSpacesBetweenWideWords()
    {
        UIHelper.WordWrap("中文 中文", 4).Should().Equal("中文", "中文");
    }

    [Fact]
    public void WordWrap_RowsWithWideTextFitTheBox()
    {
        const int width = 20;
        string text = "这是一段没有任何空格的中文，用来测试自动换行是否按显示列计算。";
        var lines = UIHelper.WordWrap(text, width);

        lines.Should().NotBeEmpty();
        lines.Should().OnlyContain(line => UIHelper.VisibleLength(line) <= width);
        string.Concat(lines).Should().Be(text);
    }

    [Fact]
    public void HintWordWrap_UsesTheSharedColumnWrapper()
    {
        const int width = 30;
        string text = "提示框里的中文通常没有空格，也要按显示列换行，不能整行溢出。";
        var lines = HintSystem.WordWrap(text, width);

        lines.Should().NotBeEmpty();
        lines.Should().OnlyContain(line => TextWidth.Width(line) <= width);
        string.Concat(lines).Should().Be(text);
    }

    [Fact]
    public void MailboxCells_PadByColumns()
    {
        BaseLocation.Cell("中文", 6).Should().Be("中文  ");
        TextWidth.Width(BaseLocation.Cell("很长的列标题", 6)).Should().Be(6);

        string row = BaseLocation.MailboxRow("", 1, "中文名字", "2026-01-01", "一段中文预览");
        TextWidth.Width(row).Should().BeLessOrEqualTo(79);
    }

    [Fact]
    public void DrawStatBar_WritesTheStatsOnce()
    {
        var output = new MemoryStream();
        var terminal = new TerminalEmulator(new MemoryStream(), output);
        UIHelper.DrawStatBar(terminal, "生命", 5, 10);
        terminal.StreamWriterInternal?.Flush();

        string text = Regex.Replace(Encoding.UTF8.GetString(output.ToArray()),
                                    "\u001b\\[[0-9;?]*[A-Za-z]", "");
        Regex.Matches(text, "5/10").Count.Should().Be(1);
    }

    private static string StatBarRow(string label)
    {
        var output = new MemoryStream();
        var terminal = new TerminalEmulator(new MemoryStream(), output);
        UIHelper.DrawStatBar(terminal, label, 5, 10);
        terminal.StreamWriterInternal?.Flush();
        return Regex.Replace(Encoding.UTF8.GetString(output.ToArray()), "\u001b\\[[0-9;?]*[A-Za-z]", "");
    }

    [Theory]
    [InlineData("HP")]
    [InlineData("生命")]
    public void DrawStatBar_EndsWithItsRightBorder_AndALineEnd(string label)
    {
        string row = StatBarRow(label);
        row.Should().EndWith("\n");
        string line = row.TrimEnd('\r', '\n');
        line.Should().StartWith("║").And.EndWith("║");
        line.Count(c => c == '\n').Should().Be(0, "one row");
    }

    [Fact]
    public void DrawStatBar_IsAsWideForAWideScriptLabel_AsForALatinOne()
    {
        string latin = StatBarRow("HPHP").TrimEnd('\r', '\n');
        string wide = StatBarRow("生命").TrimEnd('\r', '\n');
        TextWidth.Width(wide).Should().Be(TextWidth.Width(latin));
    }
}
