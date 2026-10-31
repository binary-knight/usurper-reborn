using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using FluentAssertions;
using UsurperRemake.Server;
using Xunit;

/// <summary>
/// 1.3.9: the login box of the direct connection gate and the SSH relay ("Welcome to Usurper Reborn
/// Online") is drawn in two colours and red: the border and title bright purple, the menu bright
/// green, and the whole Quit entry red. Every border segment carries its own colour code.
/// </summary>
public class LoginColors139Tests
{
    private const string Purple = "1;35", Green = "1;32", Red = "1;31";

    // Each visible character of a row with the SGR code in force when it was drawn.
    private static List<(char ch, string sgr)> Painted(string row)
    {
        var outList = new List<(char, string)>();
        string sgr = "";
        for (int i = 0; i < row.Length; i++)
        {
            var m = Regex.Match(row.Substring(i), "^\u001b\\[([0-9;]*)m");
            if (m.Success) { sgr = m.Groups[1].Value; i += m.Length - 1; continue; }
            if (row[i] == '\u001b') { var n = Regex.Match(row.Substring(i), "^\u001b\\[[0-9;]*[A-Za-z]"); i += n.Length - 1; continue; }
            if (row[i] == '\r' || row[i] == '\n') continue;
            outList.Add((row[i], sgr));
        }
        return outList;
    }

    private static bool IsBorder(char c) => "╔╗╚╝╠╣═║".IndexOf(c) >= 0;

    private static void CheckBox(List<string> rows, string title)
    {
        rows.Should().HaveCountGreaterThan(5);
        foreach (var row in rows)
        {
            row.Should().StartWith($"\u001b[{Purple}m", "every row opens in the box colour, so none inherits the row before");
            var painted = Painted(row);
            painted.Should().HaveCount(79, "the box stays 79 columns");
            painted.Where(p => IsBorder(p.ch)).Should().OnlyContain(p => p.sgr == Purple, "every border segment is purple");
            IsBorder(painted[0].ch).Should().BeTrue();
            IsBorder(painted[^1].ch).Should().BeTrue("the right border is the 79th column");
        }
        // The title row: every character purple.
        var titleRow = rows.Select(Painted).First(p => new string(p.Select(x => x.ch).ToArray()).Contains(title));
        titleRow.Should().OnlyContain(p => p.sgr == Purple);
        // Entry rows: the key and the label are one colour, green, except Quit, which is red.
        foreach (var p in rows.Select(Painted))
        {
            string text = new string(p.Select(x => x.ch).ToArray());
            var m = Regex.Match(text, @"\[([A-Z])\]");
            if (!m.Success) continue;
            string want = m.Groups[1].Value == "Q" ? Red : Green;
            var inner = p.Skip(1).Take(77).Where(x => x.ch != ' ').ToList();
            inner.Should().NotBeEmpty();
            inner.Should().OnlyContain(x => x.sgr == want, $"[{m.Groups[1].Value}] and its label are one colour");
        }
    }

    [Theory]
    [InlineData("en")]
    [InlineData("hu")]
    public void TheRelayWelcomeBox_IsPurpleWithAGreenMenuAndARedQuit(string lang)
    {
        var text = RelayClient.AuthMenuText(lang);
        var rows = text.Where(t => t.Contains('║') || t.Contains('╔') || t.Contains('╚')).ToList();
        CheckBox(rows, UsurperRemake.Systems.Loc.GetIn(lang, "auth.relay_title"));
        string.Concat(rows).Should().Contain("[Q]");
    }

    [Fact]
    public void TheGateBox_UsesTheSameColours_WithForgotPassword()
    {
        string L(string k) => UsurperRemake.Systems.Loc.GetIn("en", k);
        var rows = MudServer.AuthBoxRows(L("auth.title"), new[]
        {
            (MudServer.AuthMenuColor, "L", L("auth.login")), (MudServer.AuthMenuColor, "R", L("auth.register")),
            (MudServer.AuthMenuColor, "F", L("auth.forgot")), (MudServer.AuthMenuColor, "G", L("auth.language") + " (English)"),
            (MudServer.AuthQuitColor, "Q", L("auth.quit")),
        });
        CheckBox(rows, L("auth.title"));
        MudServer.AuthBoxColor.Should().Be(Purple);
        MudServer.AuthMenuColor.Should().Be(Green);
        MudServer.AuthQuitColor.Should().Be(Red);
    }

    [Fact]
    public void TheGateUsesTheMenuColourForEveryEntryButQuit()
    {
        var root = new DirectoryInfo(System.AppContext.BaseDirectory);
        while (root != null && !File.Exists(Path.Combine(root.FullName, "usurper-reloaded.csproj"))) root = root.Parent;
        string src = File.ReadAllText(Path.Combine(root!.FullName, "Scripts", "Server", "MudServer.cs"));
        int at = src.IndexOf("foreach (var row in AuthBoxRows(L(\"auth.title\"), entries))");
        at.Should().BeGreaterThan(0);
        string block = src.Substring(src.LastIndexOf("var entries = new List<(string, string, string)>", at), 600);
        foreach (var key in new[] { "\"L\"", "\"R\"", "\"F\"", "\"G\"" })
            block.Should().Contain($"(AuthMenuColor, {key},");
        block.Should().Contain("(AuthQuitColor, \"Q\",");
        Regex.Matches(block, "\"1;3[0-9]\"").Count.Should().Be(0, "no entry keeps a colour of its own");
    }
}
