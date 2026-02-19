using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using System.IO;

/// <summary>
/// News Location - Simple news display
/// Shows all world events in a single unified view
/// </summary>
public class NewsLocation : BaseLocation
{
    private const string NewsStandName = "Town News Board";
    private readonly NewsSystem _newsSystem;
    private const int PageSize = 20;

    public NewsLocation() : base(
        GameLocation.MainStreet,
        NewsStandName,
        "The town crier has posted the latest news on the board."
    )
    {
        _newsSystem = NewsSystem.Instance;
    }

    protected override void SetupLocation()
    {
        PossibleExits = new List<GameLocation>
        {
            GameLocation.MainStreet
        };
    }

    protected override void DisplayLocation()
    {
        // Don't display anything here - we'll show news directly
    }

    public override async Task EnterLocation(Character player, TerminalEmulator term)
    {
        // Set up the location
        currentPlayer = (player as Player)!;
        terminal = term;

        await ShowNewsBoard();
    }

    private async Task ShowNewsBoard()
    {
        var news = _newsSystem.ReadNews();

        terminal.ClearScreen();
        terminal.WriteLine("");

        terminal.SetColor("bright_cyan");
        terminal.WriteLine("╔════════════════════════════════════════════════════════════════════════════╗");
        terminal.WriteLine("║                            TOWN NEWS BOARD                                 ║");
        terminal.WriteLine("╚════════════════════════════════════════════════════════════════════════════╝");
        terminal.WriteLine("");

        if (news == null || news.Count == 0)
        {
            terminal.SetColor("gray");
            terminal.WriteLine("  The news board is empty. Nothing to report.");
            terminal.WriteLine("");
            await terminal.PressAnyKey();
            return;
        }

        // Filter out header/empty lines
        var displayLines = new List<string>();
        foreach (var line in news)
        {
            if (!string.IsNullOrWhiteSpace(line) && !line.StartsWith("═══ Usurper"))
            {
                displayLines.Add(line);
            }
        }

        if (displayLines.Count == 0)
        {
            terminal.SetColor("gray");
            terminal.WriteLine("  The news board is empty. Nothing to report.");
            terminal.WriteLine("");
            await terminal.PressAnyKey();
            return;
        }

        // Show most recent news first (reverse order), paginated
        displayLines.Reverse();

        if (displayLines.Count > PageSize)
        {
            await DisplayPaginatedNews(displayLines);
        }
        else
        {
            terminal.SetColor("white");
            foreach (string line in displayLines)
            {
                DisplayNewsLine(line);
            }
            terminal.WriteLine("");
            terminal.SetColor("gray");
            terminal.WriteLine($"  {displayLines.Count} news items");
            terminal.WriteLine("");
            await terminal.PressAnyKey();
        }
    }

    private void DisplayNewsLine(string line)
    {
        // Color-code based on content
        if (line.Contains("†") || line.Contains("slain") || line.Contains("killed"))
        {
            terminal.SetColor("red");
        }
        else if (line.Contains("♥") || line.Contains("married") || line.Contains("parents"))
        {
            terminal.SetColor("magenta");
        }
        else if (line.Contains("♔") || line.Contains("King") || line.Contains("Royal") || line.Contains("proclaims"))
        {
            terminal.SetColor("bright_yellow");
        }
        else if (line.Contains("✝") || line.Contains("divine") || line.Contains("Temple"))
        {
            terminal.SetColor("cyan");
        }
        else if (line.Contains("Level") || line.Contains("achieved"))
        {
            terminal.SetColor("bright_green");
        }
        else if (line.Contains("Team") || line.Contains("⚑"))
        {
            terminal.SetColor("bright_blue");
        }
        else if (line.Contains("═══"))
        {
            terminal.SetColor("gray");
        }
        else
        {
            terminal.SetColor("white");
        }

        terminal.WriteLine($"  {line}");
    }

    private async Task DisplayPaginatedNews(List<string> newsLines)
    {
        int totalPages = (int)Math.Ceiling((double)newsLines.Count / PageSize);
        int currentPage = 1;

        while (currentPage <= totalPages)
        {
            terminal.ClearScreen();
            terminal.WriteLine("");

            terminal.SetColor("bright_cyan");
            terminal.WriteLine("╔════════════════════════════════════════════════════════════════════════════╗");
            terminal.WriteLine("║                            TOWN NEWS BOARD                                 ║");
            terminal.WriteLine("╚════════════════════════════════════════════════════════════════════════════╝");
            terminal.WriteLine("");

            terminal.SetColor("gray");
            terminal.WriteLine($"  Page {currentPage} of {totalPages} ({newsLines.Count} items)");
            terminal.WriteLine("");

            int startIndex = (currentPage - 1) * PageSize;
            int endIndex = Math.Min(startIndex + PageSize, newsLines.Count);

            for (int i = startIndex; i < endIndex; i++)
            {
                DisplayNewsLine(newsLines[i]);
            }
            terminal.WriteLine("");

            if (currentPage < totalPages)
            {
                terminal.SetColor("yellow");
                terminal.Write("  [ENTER] Next page, [R] Return: ");

                string input = await terminal.GetInput("");

                if (!string.IsNullOrWhiteSpace(input) && (input.Trim().ToUpper() == "R" || input.Trim().ToUpper() == "Q"))
                {
                    break;
                }
                currentPage++;
            }
            else
            {
                await terminal.PressAnyKey();
                break;
            }
        }
    }

    protected override async Task<string> GetUserChoice()
    {
        // Not used - we override EnterLocation
        return await Task.FromResult("");
    }

    protected override async Task<bool> ProcessChoice(string choice)
    {
        // Not used - we override EnterLocation
        return await Task.FromResult(true);
    }

    protected override string GetBreadcrumbPath()
    {
        return "Main Street > News Board";
    }
}
