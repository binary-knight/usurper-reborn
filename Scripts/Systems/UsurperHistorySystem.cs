using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using UsurperRemake.Systems;
using UsurperRemake.UI;

/// <summary>
/// Displays the history of Usurper and BBS culture from the 1990s.
/// Honors the original creators and explains the context of this remake.
/// v1.2.5: every page in the player's language. Each paragraph is one key; the English values keep their
/// hand made line breaks, and every row is word wrapped to fit 79 columns (inside the frames, to the frame).
/// The names of people and games are not translated.
/// </summary>
public class UsurperHistorySystem
{
    private static UsurperHistorySystem? _instance;
    public static UsurperHistorySystem Instance => _instance ??= new UsurperHistorySystem();

    // Proper names, the same in every language.
    internal const string JakobName = "JAKOB DANGARDEN";
    internal const string RickName = "RICK PARRISH";
    internal const string DanName = "DANIEL ZINGARO";
    internal const string JasonName = "JASON KNIGHT";

    // The page frame is 79 columns; the name boxes are 75 (two columns in), with 69 columns of text.
    private const int PageWidth = 79;
    private const int HeadingWidth = 70;
    private const int BoxInner = 71;
    private const int BoxText = 69;

    public UsurperHistorySystem()
    {
        _instance = this;
    }

    /// <summary>
    /// Display the complete history of Usurper and BBS culture
    /// </summary>
    public async Task ShowHistory(TerminalEmulator terminal)
    {
        await ShowBBSCulturePage(terminal);
        await ShowDoorGamesPage(terminal);
        await ShowUsurperOriginPage(terminal);
        await ShowCreatorsPage(terminal);
        await ShowRemakePage(terminal);
    }

    // ---------- rows ----------

    /// <summary>v1.2.5: text centred in a field of the given width (no padding after it).</summary>
    internal static string Centered(string text, int width)
    {
        int pad = Math.Max(0, (width - UIHelper.VisibleLength(text)) / 2);
        return new string(' ', pad) + text;
    }

    /// <summary>v1.2.5: the rows of a paragraph, two columns in, wrapped at 79.</summary>
    internal static List<string> ParagraphRows(string text)
    {
        var rows = new List<string>();
        foreach (var row in UIHelper.WordWrap(text, PageWidth - 2))
            rows.Add("  " + row);
        return rows;
    }

    /// <summary>v1.2.5: the rows of a list, one item per line of the text, "    - " before each, later rows under the text.</summary>
    internal static List<string> ListRows(string text)
    {
        var rows = new List<string>();
        foreach (var item in text.Replace("\r\n", "\n").Split('\n'))
        {
            var lines = UIHelper.WordWrap(item, PageWidth - 6);
            for (int i = 0; i < lines.Count; i++)
                rows.Add((i == 0 ? "    - " : "      ") + lines[i]);
        }
        return rows;
    }

    /// <summary>v1.2.5: the rows of a paragraph inside a name box: "  |  text   |", 75 columns, at least two spaces before the right edge.</summary>
    internal static List<string> BoxRows(string text)
    {
        var rows = new List<string>();
        foreach (var row in UIHelper.WordWrap(text, BoxText - 2))
            rows.Add("  |  " + row + new string(' ', Math.Max(0, BoxText - UIHelper.VisibleLength(row))) + "|");
        return rows;
    }

    /// <summary>v1.2.5: a centred row inside a name box.</summary>
    internal static string BoxCenteredRow(string text)
    {
        var inner = Centered(text, BoxInner);
        return "  |" + inner + new string(' ', Math.Max(0, BoxInner - UIHelper.VisibleLength(inner))) + "|";
    }

    private static readonly string BoxRule = "  +" + new string('-', BoxInner) + "+";

    private static void Para(TerminalEmulator terminal, string key, string color)
    {
        terminal.SetColor(color);
        foreach (var row in ParagraphRows(Loc.Get(key)))
            terminal.WriteLine(row);
    }

    private static void Bullets(TerminalEmulator terminal, string key, string color)
    {
        terminal.SetColor(color);
        foreach (var row in ListRows(Loc.Get(key)))
            terminal.WriteLine(row);
    }

    private static void Heading(TerminalEmulator terminal, string text, string color)
    {
        terminal.SetColor(color);
        terminal.WriteLine(Centered(text, HeadingWidth));
    }

    /// <summary>v1.2.5: the page title: a 79-column frame, or the plain title for a screen reader.</summary>
    private static void PageTitle(TerminalEmulator terminal)
    {
        string title = Loc.Get("history.title");
        if (GameConfig.ScreenReaderMode)
        {
            terminal.WriteLine(title, "bright_cyan");
        }
        else
        {
            terminal.SetColor("bright_cyan");
            string inner = Centered(title, PageWidth - 2);
            terminal.WriteLine("+" + new string('=', PageWidth - 2) + "+");
            terminal.WriteLine("|" + inner + new string(' ', Math.Max(0, PageWidth - 2 - UIHelper.VisibleLength(inner))) + "|");
            terminal.WriteLine("+" + new string('=', PageWidth - 2) + "+");
        }
        terminal.WriteLine("");
    }

    /// <summary>v1.2.5: a person's name, role and story: plain rows for a screen reader, else a name box.</summary>
    private static void PersonBox(TerminalEmulator terminal, string name, string roleKey, string bioKey, string color)
    {
        string role = Loc.Get(roleKey);
        if (GameConfig.ScreenReaderMode)
        {
            terminal.WriteLine("  " + Loc.Get("history.sr_name_role", name, role), color);
            if (bioKey.Length > 0) Para(terminal, bioKey, "gray");
            return;
        }
        terminal.SetColor(color);
        terminal.WriteLine(BoxRule);
        terminal.WriteLine(BoxCenteredRow(name));
        terminal.WriteLine(BoxCenteredRow(role));
        terminal.WriteLine(BoxRule);
        if (bioKey.Length == 0) return;
        terminal.SetColor("gray");
        foreach (var row in BoxRows(Loc.Get(bioKey)))
            terminal.WriteLine(row);
        terminal.WriteLine(BoxRule);
    }

    /// <summary>
    /// Page 1: What was a BBS?
    /// </summary>
    private async Task ShowBBSCulturePage(TerminalEmulator terminal)
    {
        terminal.ClearScreen();
        PageTitle(terminal);

        Heading(terminal, Loc.Get("history.bbs_heading"), "bright_yellow");
        Heading(terminal, "(1978 - 1999)", "bright_yellow");
        terminal.WriteLine("");

        Para(terminal, "history.bbs_p1", "white");
        terminal.WriteLine("");
        Para(terminal, "history.bbs_p2", "gray");
        terminal.WriteLine("");
        Para(terminal, "history.bbs_p3", "white");
        terminal.WriteLine("");
        Para(terminal, "history.bbs_p4", "cyan");
        terminal.WriteLine("");

        terminal.SetColor("yellow");
        await terminal.WaitForKey();
    }

    /// <summary>
    /// Page 2: Door Games
    /// </summary>
    private async Task ShowDoorGamesPage(TerminalEmulator terminal)
    {
        terminal.ClearScreen();
        PageTitle(terminal);

        Heading(terminal, Loc.Get("history.doors_heading"), "bright_green");
        terminal.WriteLine("");

        Para(terminal, "history.doors_p1", "white");
        terminal.WriteLine("");
        Para(terminal, "history.doors_p2", "gray");
        terminal.WriteLine("");
        Bullets(terminal, "history.doors_list", "cyan");
        terminal.WriteLine("");
        Para(terminal, "history.doors_p3", "white");
        terminal.WriteLine("");
        Para(terminal, "history.doors_p4", "bright_yellow");
        terminal.WriteLine("");
        Para(terminal, "history.doors_p5", "gray");
        terminal.WriteLine("");

        terminal.SetColor("yellow");
        await terminal.WaitForKey();
    }

    /// <summary>
    /// Page 3: The Birth of Usurper
    /// </summary>
    private async Task ShowUsurperOriginPage(TerminalEmulator terminal)
    {
        terminal.ClearScreen();
        PageTitle(terminal);

        Heading(terminal, Loc.Get("history.origin_heading"), "bright_red");
        terminal.WriteLine("");

        Para(terminal, "history.origin_p1", "white");
        terminal.WriteLine("");
        Para(terminal, "history.origin_p2", "gray");
        terminal.WriteLine("");
        Bullets(terminal, "history.origin_list", "cyan");
        terminal.WriteLine("");
        Para(terminal, "history.origin_p3", "white");
        terminal.WriteLine("");
        Para(terminal, "history.origin_p4", "bright_yellow");
        terminal.WriteLine("");

        terminal.SetColor("yellow");
        await terminal.WaitForKey();
    }

    /// <summary>
    /// Page 4: The People Who Made It Possible
    /// </summary>
    private async Task ShowCreatorsPage(TerminalEmulator terminal)
    {
        terminal.ClearScreen();
        PageTitle(terminal);

        Heading(terminal, Loc.Get("history.creators_heading"), "bright_magenta");
        terminal.WriteLine("");

        Para(terminal, "history.creators_intro", "white");
        terminal.WriteLine("");

        PersonBox(terminal, JakobName, "history.jakob_role", "history.jakob_bio", "bright_yellow");
        terminal.WriteLine("");

        PersonBox(terminal, RickName, "history.rick_role", "history.rick_bio", "bright_green");
        terminal.WriteLine("");

        terminal.SetColor("yellow");
        await terminal.WaitForKey();

        // Continue with Dan Zingaro
        terminal.ClearScreen();
        PageTitle(terminal);

        Heading(terminal, Loc.Get("history.creators_heading"), "bright_magenta");
        terminal.WriteLine("");

        PersonBox(terminal, DanName, "history.dan_role", "history.dan_bio", "bright_cyan");
        terminal.WriteLine("");

        Para(terminal, "history.creators_together", "white");
        terminal.WriteLine("");
        Para(terminal, "history.creators_honor", "bright_white");
        terminal.WriteLine("");

        terminal.SetColor("yellow");
        await terminal.WaitForKey();
    }

    /// <summary>
    /// Page 5: This Remake
    /// </summary>
    private async Task ShowRemakePage(TerminalEmulator terminal)
    {
        terminal.ClearScreen();
        PageTitle(terminal);

        Heading(terminal, Loc.Get("history.remake_heading"), "bright_white");
        Heading(terminal, Loc.Get("history.remake_years"), "bright_white");
        terminal.WriteLine("");

        PersonBox(terminal, JasonName, "history.jason_role", "", "bright_green");
        terminal.WriteLine("");

        Para(terminal, "history.remake_p1", "white");
        terminal.WriteLine("");
        Para(terminal, "history.remake_p2", "gray");
        terminal.WriteLine("");
        Para(terminal, "history.remake_features", "cyan");
        terminal.WriteLine("");
        Bullets(terminal, "history.remake_list", "white");
        terminal.WriteLine("");
        Para(terminal, "history.remake_p3", "bright_yellow");
        terminal.WriteLine("");
        Para(terminal, "history.remake_welcome", "bright_green");
        terminal.WriteLine("");

        terminal.SetColor("yellow");
        terminal.WriteLine(Centered(Loc.Get("engine.press_enter_return"), PageWidth));
        await terminal.WaitForKey();
    }
}
