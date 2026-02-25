using UsurperRemake.Utils;
using UsurperRemake.Systems;
using UsurperRemake.BBS;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;

/// <summary>
/// Castle Location - Pascal-compatible royal court system
/// Based on CASTLE.PAS with complete King and commoner interfaces
/// </summary>
public class CastleLocation : BaseLocation
{
    private static King currentKing = null;
    private static List<MonarchRecord> monarchHistory = new();
    private const int MaxMonarchHistory = 100;
    private static List<RoyalMailMessage> royalMail = new();
    private const int MaxRoyalMail = 50;
    private bool playerIsKing = false;
    private Random random = new Random();

    public CastleLocation() : base(
        GameLocation.Castle,
        "The Royal Castle",
        "You approach the magnificent royal castle, seat of power in the kingdom."
    ) { }

    protected override void SetupLocation()
    {
        base.SetupLocation();

        playerIsKing = currentPlayer?.King ?? false;

        // Load or create king data
        LoadKingData();
    }

    protected override void DisplayLocation()
    {
        // Refresh king status every time we display (handles login, throne changes, etc.)
        playerIsKing = currentPlayer?.King ?? false;
        LoadKingData();

        terminal.ClearScreen();

        if (DoorMode.IsInDoorMode)
        {
            if (playerIsKing)
                DisplayRoyalCastleInteriorBBS();
            else
                DisplayCastleExteriorBBS();
            return;
        }

        if (playerIsKing)
        {
            DisplayRoyalCastleInterior();
        }
        else
        {
            DisplayCastleExterior();
        }
    }

    /// <summary>
    /// Display castle interior for the reigning monarch
    /// </summary>
    private void DisplayRoyalCastleInterior()
    {
        // Header - standardized format
        terminal.SetColor("bright_cyan");
        terminal.WriteLine("╔═════════════════════════════════════════════════════════════════════════════╗");
        terminal.WriteLine($"║{"THE ROYAL CASTLE".PadLeft((77 + 16) / 2).PadRight(77)}║");
        terminal.WriteLine("╚═════════════════════════════════════════════════════════════════════════════╝");
        terminal.WriteLine("");

        // Royal greeting
        terminal.SetColor("cyan");
        terminal.WriteLine("You have entered the Great Hall. Upon your arrival you are");
        terminal.WriteLine("immediately surrounded by a flock of servants and advisors.");
        terminal.SetColor("white");
        terminal.WriteLine("You greet your staff with a subtle nod.");
        terminal.WriteLine("");

        // Royal treasury status
        terminal.SetColor("bright_green");
        terminal.Write("The Royal Purse");
        terminal.SetColor("white");
        terminal.Write(" has ");
        terminal.SetColor("bright_yellow");
        terminal.Write($"{currentKing.Treasury:N0}");
        terminal.SetColor("white");
        terminal.WriteLine(" gold pieces.");

        // Guard status
        terminal.SetColor("cyan");
        terminal.Write("Royal Guards: ");
        terminal.SetColor("white");
        terminal.WriteLine($"{currentKing.Guards.Count}/{GameConfig.MaxRoyalGuards}");

        // Prisoners
        terminal.SetColor("red");
        terminal.Write("Prisoners: ");
        terminal.SetColor("white");
        terminal.WriteLine($"{currentKing.Prisoners.Count}");

        // Unread mail
        int unreadMail = royalMail.Count(m => !m.IsRead);
        if (unreadMail > 0)
        {
            terminal.SetColor("bright_magenta");
            terminal.WriteLine($"You have {unreadMail} unread messages!");
        }
        terminal.WriteLine("");

        ShowRoyalMenu();
    }

    /// <summary>
    /// Display castle exterior for non-monarchs
    /// </summary>
    private void DisplayCastleExterior()
    {
        // Header - standardized format
        terminal.SetColor("bright_cyan");
        terminal.WriteLine("╔═════════════════════════════════════════════════════════════════════════════╗");
        terminal.WriteLine($"║{"OUTSIDE THE ROYAL CASTLE".PadLeft((77 + 24) / 2).PadRight(77)}║");
        terminal.WriteLine("╚═════════════════════════════════════════════════════════════════════════════╝");
        terminal.WriteLine("");

        // Approach description
        terminal.SetColor("white");
        terminal.WriteLine("You journey the winding road up to the gates of the Castle.");
        terminal.WriteLine("Massive stone walls tower above you, and royal guards");
        terminal.WriteLine("watch your approach with keen interest.");
        terminal.WriteLine("");

        // King status
        if (currentKing != null && currentKing.IsActive)
        {
            terminal.SetColor("white");
            terminal.Write("The mighty ");
            terminal.SetColor("bright_yellow");
            terminal.Write($"{currentKing.GetTitle()} {currentKing.Name}");
            terminal.SetColor("white");
            terminal.WriteLine(" rules from within these walls.");
            terminal.SetColor("cyan");
            terminal.WriteLine($"Reign: {currentKing.TotalReign} days | Treasury: {currentKing.Treasury:N0} gold");
            terminal.WriteLine("");
        }
        else
        {
            terminal.SetColor("bright_red");
            terminal.WriteLine("The kingdom appears to be in disarray!");
            terminal.WriteLine("No monarch sits upon the throne...");
            terminal.WriteLine("");
        }

        ShowNPCsInLocation();

        ShowCastleExteriorMenu();
    }

    /// <summary>
    /// Show royal castle menu for the king
    /// </summary>
    private void ShowRoyalMenu()
    {
        terminal.SetColor("cyan");
        terminal.WriteLine("Royal Commands:");
        terminal.WriteLine("");

        // Row 1
        terminal.SetColor("darkgray");
        terminal.Write(" [");
        terminal.SetColor("bright_yellow");
        terminal.Write("P");
        terminal.SetColor("darkgray");
        terminal.Write("]");
        terminal.SetColor("white");
        terminal.Write("rison Cells       ");

        terminal.SetColor("darkgray");
        terminal.Write("[");
        terminal.SetColor("bright_yellow");
        terminal.Write("O");
        terminal.SetColor("darkgray");
        terminal.Write("]");
        terminal.SetColor("white");
        terminal.Write("rders             ");

        terminal.SetColor("darkgray");
        terminal.Write("[");
        terminal.SetColor("bright_yellow");
        terminal.Write("1");
        terminal.SetColor("darkgray");
        terminal.Write("]");
        terminal.SetColor("white");
        terminal.WriteLine(" Royal Mail");

        // Row 2
        terminal.SetColor("darkgray");
        terminal.Write(" [");
        terminal.SetColor("bright_yellow");
        terminal.Write("G");
        terminal.SetColor("darkgray");
        terminal.Write("]");
        terminal.SetColor("white");
        terminal.Write("o to Sleep        ");

        terminal.SetColor("darkgray");
        terminal.Write("[");
        terminal.SetColor("bright_yellow");
        terminal.Write("C");
        terminal.SetColor("darkgray");
        terminal.Write("]");
        terminal.SetColor("white");
        terminal.Write("heck Security     ");

        terminal.SetColor("darkgray");
        terminal.Write("[");
        terminal.SetColor("bright_yellow");
        terminal.Write("H");
        terminal.SetColor("darkgray");
        terminal.Write("]");
        terminal.SetColor("white");
        terminal.WriteLine("istory of Monarchs");

        // Row 3
        terminal.SetColor("darkgray");
        terminal.Write(" [");
        terminal.SetColor("bright_yellow");
        terminal.Write("A");
        terminal.SetColor("darkgray");
        terminal.Write("]");
        terminal.SetColor("white");
        terminal.Write("bdicate           ");

        terminal.SetColor("darkgray");
        terminal.Write("[");
        terminal.SetColor("bright_yellow");
        terminal.Write("M");
        terminal.SetColor("darkgray");
        terminal.Write("]");
        terminal.SetColor("white");
        terminal.Write("agic              ");

        terminal.SetColor("darkgray");
        terminal.Write("[");
        terminal.SetColor("bright_yellow");
        terminal.Write("F");
        terminal.SetColor("darkgray");
        terminal.Write("]");
        terminal.SetColor("white");
        terminal.WriteLine("iscal Matters");

        // Row 4
        terminal.SetColor("darkgray");
        terminal.Write(" [");
        terminal.SetColor("bright_yellow");
        terminal.Write("S");
        terminal.SetColor("darkgray");
        terminal.Write("]");
        terminal.SetColor("white");
        terminal.Write("tatus             ");

        terminal.SetColor("darkgray");
        terminal.Write("[");
        terminal.SetColor("bright_yellow");
        terminal.Write("Q");
        terminal.SetColor("darkgray");
        terminal.Write("]");
        terminal.SetColor("white");
        terminal.Write("uests             ");

        terminal.SetColor("darkgray");
        terminal.Write("[");
        terminal.SetColor("bright_yellow");
        terminal.Write("T");
        terminal.SetColor("darkgray");
        terminal.Write("]");
        terminal.SetColor("white");
        terminal.WriteLine("he Royal Orphanage");

        // Row 5 - Political systems
        terminal.SetColor("darkgray");
        terminal.Write(" [");
        terminal.SetColor("bright_yellow");
        terminal.Write("W");
        terminal.SetColor("darkgray");
        terminal.Write("]");
        terminal.SetColor("white");
        terminal.Write("edding            ");

        terminal.SetColor("darkgray");
        terminal.Write("[");
        terminal.SetColor("bright_yellow");
        terminal.Write("U");
        terminal.SetColor("darkgray");
        terminal.Write("]");
        terminal.SetColor("white");
        terminal.Write("Court Politics    ");

        terminal.SetColor("darkgray");
        terminal.Write("[");
        terminal.SetColor("bright_yellow");
        terminal.Write("E");
        terminal.SetColor("darkgray");
        terminal.Write("]");
        terminal.SetColor("white");
        terminal.WriteLine("state (Succession)");

        // Row 6 - Royal Bodyguards
        terminal.SetColor("darkgray");
        terminal.Write(" [");
        terminal.SetColor("bright_yellow");
        terminal.Write("B");
        terminal.SetColor("darkgray");
        terminal.Write("]");
        terminal.SetColor("white");
        terminal.WriteLine("odyguards (Dungeon Mercenaries)");

        terminal.WriteLine("");

        // Navigation
        terminal.SetColor("darkgray");
        terminal.Write(" [");
        terminal.SetColor("bright_yellow");
        terminal.Write("R");
        terminal.SetColor("darkgray");
        terminal.Write("]");
        terminal.SetColor("white");
        terminal.WriteLine("eturn to Town");
        terminal.WriteLine("");

        ShowStatusLine();
    }

    /// <summary>
    /// Show castle exterior menu for commoners
    /// </summary>
    private void ShowCastleExteriorMenu()
    {
        terminal.SetColor("cyan");
        terminal.WriteLine("Options:");
        terminal.WriteLine("");

        // Row 1
        terminal.SetColor("darkgray");
        terminal.Write(" [");
        terminal.SetColor("bright_yellow");
        terminal.Write("T");
        terminal.SetColor("darkgray");
        terminal.Write("]");
        terminal.SetColor("white");
        terminal.Write("he Royal Guard    ");

        terminal.SetColor("darkgray");
        terminal.Write("[");
        terminal.SetColor("bright_yellow");
        terminal.Write("P");
        terminal.SetColor("darkgray");
        terminal.Write("]");
        terminal.SetColor("white");
        terminal.Write("rison             ");

        terminal.SetColor("darkgray");
        terminal.Write("[");
        terminal.SetColor("bright_yellow");
        terminal.Write("D");
        terminal.SetColor("darkgray");
        terminal.Write("]");
        terminal.SetColor("white");
        terminal.WriteLine("onate to Royal Purse");

        // Row 2
        terminal.SetColor("darkgray");
        terminal.Write(" [");
        terminal.SetColor("bright_yellow");
        terminal.Write("H");
        terminal.SetColor("darkgray");
        terminal.Write("]");
        terminal.SetColor("white");
        terminal.Write("istory of Monarchs ");

        terminal.SetColor("darkgray");
        terminal.Write("[");
        terminal.SetColor("bright_yellow");
        terminal.Write("S");
        terminal.SetColor("darkgray");
        terminal.Write("]");
        terminal.SetColor("white");
        terminal.Write("eek Audience       ");

        // Apply for Royal Guard option
        terminal.SetColor("darkgray");
        terminal.Write("[");
        terminal.SetColor("bright_yellow");
        terminal.Write("A");
        terminal.SetColor("darkgray");
        terminal.Write("]");
        terminal.SetColor("white");
        terminal.WriteLine("pply for Royal Guard");

        // Throne options - always show one of these
        if (currentKing != null && currentKing.IsActive)
        {
            // There is a king - show infiltrate option
            terminal.SetColor("darkgray");
            terminal.Write(" [");
            terminal.SetColor("bright_yellow");
            terminal.Write("I");
            terminal.SetColor("darkgray");
            terminal.Write("]");

            if (CanChallengeThrone())
            {
                terminal.SetColor("bright_red");
                terminal.WriteLine("nfiltrate Castle (Challenge for Throne)");
            }
            else if (currentPlayer.Level < GameConfig.MinLevelKing)
            {
                terminal.SetColor("gray");
                terminal.WriteLine($"nfiltrate Castle (Requires Level {GameConfig.MinLevelKing})");
            }
            else if (currentKing != null && currentKing.IsActive)
            {
                int kLevel = GetKingLevel();
                if (kLevel > 0 && kLevel - currentPlayer.Level > GameConfig.KingChallengeLevelRange)
                {
                    terminal.SetColor("gray");
                    terminal.WriteLine($"nfiltrate Castle (King Lv{kLevel}, need Lv{Math.Max(GameConfig.MinLevelKing, kLevel - GameConfig.KingChallengeLevelRange)}+)");
                }
                else
                {
                    terminal.SetColor("gray");
                    terminal.WriteLine("nfiltrate Castle (Not Available)");
                }
            }
            else
            {
                terminal.SetColor("gray");
                terminal.WriteLine("nfiltrate Castle (Not Available)");
            }
        }
        else
        {
            // No king - show claim throne option
            terminal.SetColor("darkgray");
            terminal.Write(" [");
            terminal.SetColor("bright_yellow");
            terminal.Write("C");
            terminal.SetColor("darkgray");
            terminal.Write("]");

            if (currentPlayer.Level >= GameConfig.MinLevelKing)
            {
                terminal.SetColor("bright_yellow");
                terminal.WriteLine("laim Empty Throne");
            }
            else
            {
                terminal.SetColor("gray");
                terminal.WriteLine($"laim Empty Throne (Requires Level {GameConfig.MinLevelKing})");
            }
        }

        // Castle Siege option (online mode, team required)
        if (UsurperRemake.BBS.DoorMode.IsOnlineMode && !string.IsNullOrEmpty(currentPlayer.Team))
        {
            terminal.SetColor("darkgray");
            terminal.Write(" [");
            terminal.SetColor("bright_yellow");
            terminal.Write("B");
            terminal.SetColor("darkgray");
            terminal.Write("]");
            terminal.SetColor("bright_red");
            terminal.WriteLine("esiege the Castle (Team Siege)");
        }

        terminal.WriteLine("");

        // The Crown faction option - only show if not already a member
        var factionSystem = UsurperRemake.Systems.FactionSystem.Instance;
        if (factionSystem.PlayerFaction != UsurperRemake.Systems.Faction.TheCrown)
        {
            terminal.SetColor("darkgray");
            terminal.Write(" [");
            terminal.SetColor("bright_yellow");
            terminal.Write("J");
            terminal.SetColor("darkgray");
            terminal.Write("]");
            terminal.SetColor("bright_yellow");
            terminal.Write("oin Royal Service ");
            if (factionSystem.PlayerFaction == null)
            {
                terminal.SetColor("gray");
                terminal.WriteLine("(swear loyalty to The Crown...)");
            }
            else
            {
                terminal.SetColor("dark_red");
                terminal.WriteLine("(you serve another...)");
            }
        }
        else
        {
            terminal.SetColor("bright_green");
            terminal.WriteLine(" You are a loyal servant of The Crown.");
        }

        // Royal Armory (Crown only)
        if (FactionSystem.Instance?.HasCastleAccess() == true)
        {
            terminal.SetColor("darkgray");
            terminal.Write(" [");
            terminal.SetColor("bright_yellow");
            terminal.Write("L");
            terminal.SetColor("darkgray");
            terminal.Write("]");
            terminal.SetColor("yellow");
            terminal.WriteLine(" Royal Armory");
        }

        // Navigation
        terminal.SetColor("darkgray");
        terminal.Write(" [");
        terminal.SetColor("bright_yellow");
        terminal.Write("R");
        terminal.SetColor("darkgray");
        terminal.Write("]");
        terminal.SetColor("white");
        terminal.WriteLine("eturn to Town");
        terminal.WriteLine("");

        ShowStatusLine();
    }

    /// <summary>
    /// BBS compact display for the reigning monarch (80x25 terminal)
    /// </summary>
    private void DisplayRoyalCastleInteriorBBS()
    {
        ShowBBSHeader("THE ROYAL CASTLE");
        // 1-line greeting + treasury
        terminal.SetColor("white");
        terminal.Write(" Your Majesty! Treasury: ");
        terminal.SetColor("bright_yellow");
        terminal.Write($"{currentKing.Treasury:N0}g");
        terminal.SetColor("gray");
        terminal.Write($"  Guards: {currentKing.Guards.Count}/{GameConfig.MaxRoyalGuards}");
        terminal.Write($"  Prisoners: {currentKing.Prisoners.Count}");
        terminal.WriteLine("");
        int unreadMail = royalMail.Count(m => !m.IsRead);
        if (unreadMail > 0)
        {
            terminal.SetColor("bright_magenta");
            terminal.WriteLine($" You have {unreadMail} unread messages!");
        }
        ShowBBSNPCs();
        // Menu rows
        ShowBBSMenuRow(("P", "bright_yellow", "Prison"), ("O", "bright_yellow", "Orders"), ("1", "bright_yellow", "Mail"), ("G", "bright_yellow", "Sleep"));
        ShowBBSMenuRow(("C", "bright_yellow", "Security"), ("H", "bright_yellow", "History"), ("A", "bright_yellow", "Abdicate"), ("M", "bright_yellow", "Magic"));
        ShowBBSMenuRow(("F", "bright_yellow", "Fiscal"), ("Q", "bright_yellow", "Quests"), ("T", "bright_yellow", "Orphanage"), ("W", "bright_yellow", "Wedding"));
        ShowBBSMenuRow(("U", "bright_yellow", "Court"), ("E", "bright_yellow", "Succession"), ("B", "bright_yellow", "Bodyguards"));
        ShowBBSMenuRow(("R", "bright_yellow", "Return"));
        ShowBBSFooter();
    }

    /// <summary>
    /// BBS compact display for non-monarchs (80x25 terminal)
    /// </summary>
    private void DisplayCastleExteriorBBS()
    {
        ShowBBSHeader("OUTSIDE THE ROYAL CASTLE");
        // 1-line king status
        if (currentKing != null && currentKing.IsActive)
        {
            terminal.SetColor("white");
            terminal.Write($" {currentKing.GetTitle()} ");
            terminal.SetColor("bright_yellow");
            terminal.Write(currentKing.Name);
            terminal.SetColor("gray");
            terminal.WriteLine($" rules. Reign: {currentKing.TotalReign}d | Treasury: {currentKing.Treasury:N0}g");
        }
        else
        {
            terminal.SetColor("bright_red");
            terminal.WriteLine(" No monarch sits upon the throne...");
        }
        ShowBBSNPCs();
        // Menu rows
        ShowBBSMenuRow(("T", "bright_yellow", "Royal Guard"), ("P", "bright_yellow", "Prison"), ("D", "bright_yellow", "Donate"));
        ShowBBSMenuRow(("H", "bright_yellow", "History"), ("S", "bright_yellow", "Audience"), ("A", "bright_yellow", "Apply Guard"));
        // Throne challenge / claim
        if (currentKing != null && currentKing.IsActive)
        {
            if (CanChallengeThrone())
                ShowBBSMenuRow(("I", "bright_yellow", "Infiltrate (Challenge Throne)"));
            else
                ShowBBSMenuRow(("I", "gray", $"Infiltrate (Lv{GameConfig.MinLevelKing}+)"));
        }
        else
        {
            if (currentPlayer.Level >= GameConfig.MinLevelKing)
                ShowBBSMenuRow(("C", "bright_yellow", "Claim Empty Throne"));
            else
                ShowBBSMenuRow(("C", "gray", $"Claim Throne (Lv{GameConfig.MinLevelKing}+)"));
        }
        // Siege option
        if (DoorMode.IsOnlineMode && !string.IsNullOrEmpty(currentPlayer.Team))
            ShowBBSMenuRow(("B", "bright_yellow", "Besiege Castle"));
        // Faction
        var factionSystem = FactionSystem.Instance;
        if (factionSystem.PlayerFaction != Faction.TheCrown)
            ShowBBSMenuRow(("J", "bright_yellow", "Join Crown"));
        if (FactionSystem.Instance?.HasCastleAccess() == true)
            ShowBBSMenuRow(("L", "bright_yellow", "Royal Armory"));
        ShowBBSMenuRow(("R", "bright_yellow", "Return"));
        ShowBBSFooter();
    }

    protected override async Task<bool> ProcessChoice(string choice)
    {
        // Handle global quick commands first
        var (handled, shouldExit) = await TryProcessGlobalCommand(choice);
        if (handled) return shouldExit;

        if (string.IsNullOrWhiteSpace(choice))
            return false;

        var upperChoice = choice.ToUpper().Trim();

        if (playerIsKing)
        {
            return await ProcessRoyalChoice(upperChoice);
        }
        else
        {
            return await ProcessCommonerChoice(upperChoice);
        }
    }

    /// <summary>
    /// Process choices for the reigning monarch
    /// </summary>
    private async Task<bool> ProcessRoyalChoice(string choice)
    {
        switch (choice)
        {
            case "P":
                await ManagePrisonCells();
                return false;

            case "O":
                await RoyalOrders();
                return false;

            case "1":
                await ReadRoyalMail();
                return false;

            case "G":
                await RoyalSleep();
                return false;

            case "C":
                await CheckSecurity();
                return false;

            case "H":
                await ShowMonarchHistory();
                return false;

            case "A":
                return await AttemptAbdication();

            case "M":
                await CourtMagician();
                return false;

            case "F":
                await FiscalMatters();
                return false;

            case "S":
                await ShowStatus();
                return false;

            case "Q":
                await RoyalQuests();
                return false;

            case "T":
                await RoyalOrphanage();
                return false;

            case "W":
                await ArrangeRoyalMarriage();
                return false;

            case "U":
                await ViewCourtPolitics();
                return false;

            case "E":
                await ManageSuccession();
                return false;

            case "B":
                await ManageRoyalBodyguards();
                return false;

            case "R":
                await NavigateToLocation(GameLocation.MainStreet);
                return true;

            default:
                terminal.SetColor("red");
                terminal.WriteLine("Invalid choice! Try again.");
                await Task.Delay(1000);
                return false;
        }
    }

    /// <summary>
    /// Process choices for commoners outside the castle
    /// </summary>
    private async Task<bool> ProcessCommonerChoice(string choice)
    {
        switch (choice)
        {
            case "T":
                await ViewRoyalGuard();
                return false;

            case "P":
                await VisitPrison();
                return false;

            case "D":
                await DonateToRoyalPurse();
                return false;

            case "H":
                await ShowMonarchHistory();
                return false;

            case "S":
                await SeekAudience();
                return false;

            case "A":
                await ApplyForRoyalGuard();
                return false;

            case "I":
                if (CanChallengeThrone())
                {
                    return await ChallengeThrone();
                }
                else
                {
                    terminal.SetColor("red");
                    if (currentPlayer.Level < GameConfig.MinLevelKing)
                        terminal.WriteLine($"You must be at least level {GameConfig.MinLevelKing} to challenge for the throne!");
                    else if (currentKing != null && currentKing.IsActive)
                    {
                        int kLevel = GetKingLevel();
                        if (kLevel > 0 && kLevel - currentPlayer.Level > GameConfig.KingChallengeLevelRange)
                            terminal.WriteLine($"The {currentKing.GetTitle()} is level {kLevel}. You must be at least level {kLevel - GameConfig.KingChallengeLevelRange} to challenge!");
                        else
                            terminal.WriteLine("You are not worthy to challenge for the throne!");
                    }
                    else
                        terminal.WriteLine("You are not worthy to challenge for the throne!");
                    await Task.Delay(2000);
                }
                return false;

            case "C":
                if (currentKing == null || !currentKing.IsActive)
                {
                    return await ClaimEmptyThrone();
                }
                else
                {
                    terminal.SetColor("red");
                    terminal.WriteLine("The throne is already occupied!");
                    await Task.Delay(2000);
                }
                return false;

            case "J": // The Crown faction recruitment
                await ShowCrownRecruitment();
                return false;

            case "L": // Royal Armory (Crown only)
                await VisitRoyalArmory();
                return false;

            case "B": // Castle Siege (online mode, team required)
                if (UsurperRemake.BBS.DoorMode.IsOnlineMode)
                    await CastleSiegeMenu();
                else
                {
                    terminal.SetColor("red");
                    terminal.WriteLine("Castle siege is only available in online mode!");
                    await Task.Delay(1500);
                }
                return false;

            case "R":
                await NavigateToLocation(GameLocation.MainStreet);
                return true;

            default:
                terminal.SetColor("red");
                terminal.WriteLine("Invalid choice! Try again.");
                await Task.Delay(1000);
                return false;
        }
    }

    // ═══════════════════════════════════════════════════════════════════════════
    // ROYAL GUARD SYSTEM
    // ═══════════════════════════════════════════════════════════════════════════

    private async Task ViewRoyalGuard()
    {
        terminal.ClearScreen();
        terminal.SetColor("bright_cyan");
        terminal.WriteLine("╔══════════════════════════════════════════════════════════════════════════════╗");
        terminal.WriteLine("║                           THE ROYAL GUARD                                   ║");
        terminal.WriteLine("╚══════════════════════════════════════════════════════════════════════════════╝");
        terminal.WriteLine("");

        if (currentKing == null || !currentKing.IsActive)
        {
            terminal.SetColor("gray");
            terminal.WriteLine("With no monarch on the throne, the Royal Guard has disbanded.");
            terminal.WriteLine("The castle stands largely undefended...");
        }
        else if (currentKing.Guards.Count == 0)
        {
            terminal.SetColor("yellow");
            terminal.WriteLine($"{currentKing.GetTitle()} {currentKing.Name} has no Royal Guards!");
            terminal.WriteLine("The castle relies only on its walls for defense.");
        }
        else
        {
            terminal.SetColor("white");
            terminal.WriteLine($"The Royal Guard of {currentKing.GetTitle()} {currentKing.Name}:");
            terminal.WriteLine("");

            terminal.SetColor("cyan");
            terminal.WriteLine($"{"#",-3} {"Name",-20} {"Rank",-15} {"Loyalty",-10}");
            terminal.SetColor("darkgray");
            terminal.WriteLine(new string('─', 50));

            int i = 1;
            foreach (var guard in currentKing.Guards)
            {
                string loyaltyColor = guard.Loyalty > 80 ? "bright_green" :
                                     guard.Loyalty > 50 ? "yellow" : "red";

                terminal.SetColor("white");
                terminal.Write($"{i,-3} {guard.Name,-20} ");
                terminal.SetColor("cyan");
                terminal.Write($"{"Guard",-15} ");
                terminal.SetColor(loyaltyColor);
                terminal.WriteLine($"{guard.Loyalty}%");
                i++;
            }

            terminal.WriteLine("");
            terminal.SetColor("white");
            terminal.WriteLine($"Total Guards: {currentKing.Guards.Count}/{GameConfig.MaxRoyalGuards}");
        }

        terminal.WriteLine("");
        terminal.SetColor("darkgray");
        terminal.WriteLine("Press Enter to continue...");
        await terminal.ReadKeyAsync();
    }

    // ═══════════════════════════════════════════════════════════════════════════
    // PRISON SYSTEM
    // ═══════════════════════════════════════════════════════════════════════════

    private async Task VisitPrison()
    {
        terminal.ClearScreen();
        terminal.SetColor("red");
        terminal.WriteLine("╔══════════════════════════════════════════════════════════════════════════════╗");
        terminal.WriteLine("║                           THE ROYAL PRISON                                  ║");
        terminal.WriteLine("╚══════════════════════════════════════════════════════════════════════════════╝");
        terminal.WriteLine("");

        terminal.SetColor("gray");
        terminal.WriteLine("You peer through the iron bars at the gloomy prison cells.");
        terminal.WriteLine("The smell of damp stone fills your nostrils...");
        terminal.WriteLine("");

        if (currentKing == null || currentKing.Prisoners.Count == 0)
        {
            terminal.SetColor("white");
            terminal.WriteLine("The prison cells are empty.");
        }
        else
        {
            terminal.SetColor("cyan");
            terminal.WriteLine($"{"Name",-20} {"Crime",-20} {"Days Left",-10}");
            terminal.SetColor("darkgray");
            terminal.WriteLine(new string('─', 55));

            foreach (var prisoner in currentKing.Prisoners)
            {
                int daysLeft = Math.Max(0, prisoner.Value.Sentence - prisoner.Value.DaysServed);
                terminal.SetColor("white");
                terminal.WriteLine($"{prisoner.Value.CharacterName,-20} {prisoner.Value.Crime,-20} {daysLeft,-10}");
            }
        }

        terminal.WriteLine("");
        terminal.SetColor("darkgray");
        terminal.WriteLine("Press Enter to continue...");
        await terminal.ReadKeyAsync();
    }

    private async Task ManagePrisonCells()
    {
        bool done = false;
        while (!done)
        {
            terminal.ClearScreen();
            terminal.SetColor("bright_red");
            terminal.WriteLine("╔══════════════════════════════════════════════════════════════════════════════╗");
            terminal.WriteLine("║                        PRISON CELL MANAGEMENT                               ║");
            terminal.WriteLine("╚══════════════════════════════════════════════════════════════════════════════╝");
            terminal.WriteLine("");

            if (currentKing.Prisoners.Count == 0)
            {
                terminal.SetColor("gray");
                terminal.WriteLine("The dungeons are empty. Justice reigns in your kingdom!");
                terminal.WriteLine("");
            }
            else
            {
                terminal.SetColor("cyan");
                terminal.WriteLine($"{"#",-3} {"Name",-18} {"Crime",-18} {"Sentence",-10} {"Served",-8} {"Bail",-10}");
                terminal.SetColor("darkgray");
                terminal.WriteLine(new string('─', 75));

                int i = 1;
                foreach (var prisoner in currentKing.Prisoners)
                {
                    var p = prisoner.Value;
                    string bailStr = p.BailAmount > 0 ? $"{p.BailAmount:N0}g" : "None";
                    terminal.SetColor("white");
                    terminal.WriteLine($"{i,-3} {p.CharacterName,-18} {p.Crime,-18} {p.Sentence,-10} {p.DaysServed,-8} {bailStr,-10}");
                    i++;
                }
                terminal.WriteLine("");
            }

            terminal.SetColor("cyan");
            terminal.WriteLine("Commands:");
            terminal.SetColor("white");
            terminal.WriteLine("(I)mprison someone  (P)ardon prisoner  (E)xecute prisoner");
            terminal.WriteLine("(S)et bail amount   (R)eturn");
            terminal.WriteLine("");

            terminal.SetColor("cyan");
            terminal.Write("Your decree: ");
            terminal.SetColor("white");
            string input = await terminal.ReadLineAsync();

            if (string.IsNullOrEmpty(input)) continue;

            switch (char.ToUpper(input[0]))
            {
                case 'I':
                    await ImprisonSomeone();
                    break;
                case 'P':
                    await PardonPrisoner();
                    break;
                case 'E':
                    await ExecutePrisoner();
                    break;
                case 'S':
                    await SetBailAmount();
                    break;
                case 'R':
                    done = true;
                    break;
            }
        }
    }

    private async Task ImprisonSomeone()
    {
        terminal.WriteLine("");

        // Build a list of potential targets (NPCs + online players)
        var targets = new List<(string Name, string Type, bool IsNPC)>();

        // Add living NPCs (not already imprisoned, not the king)
        var npcs = NPCSpawnSystem.Instance?.ActiveNPCs?
            .Where(n => !n.IsDead && n.DaysInPrison <= 0 && n.Name2 != currentKing.Name)
            .OrderBy(n => n.Name2)
            .ToList() ?? new List<NPC>();
        foreach (var npc in npcs)
            targets.Add((npc.Name2, $"Lv{npc.Level} {npc.Class}", true));

        // In online mode, add other players
        if (DoorMode.IsOnlineMode)
        {
            var backend = SaveSystem.Instance.Backend as SqlSaveBackend;
            if (backend != null)
            {
                var playerNames = backend.GetAllPlayerNames()
                    .Where(p => !p.Equals(currentPlayer.Name, StringComparison.OrdinalIgnoreCase)
                             && !p.Equals(currentKing.Name, StringComparison.OrdinalIgnoreCase))
                    .OrderBy(p => p)
                    .ToList();
                foreach (var pn in playerNames)
                    targets.Add((pn, "Player", false));
            }
        }

        if (targets.Count == 0)
        {
            terminal.SetColor("yellow");
            terminal.WriteLine("There is no one to imprison.");
            await Task.Delay(1500);
            return;
        }

        // Show numbered list
        terminal.SetColor("cyan");
        terminal.WriteLine($"{"#",-4} {"Name",-20} {"Type",-20}");
        terminal.SetColor("darkgray");
        terminal.WriteLine(new string('─', 44));

        for (int i = 0; i < targets.Count; i++)
        {
            terminal.SetColor(targets[i].IsNPC ? "white" : "bright_yellow");
            terminal.WriteLine($"{i + 1,-4} {targets[i].Name,-20} {targets[i].Type,-20}");
        }

        terminal.WriteLine("");
        terminal.SetColor("cyan");
        terminal.Write("# to imprison (or 0 to cancel): ");
        terminal.SetColor("white");
        string input = await terminal.ReadLineAsync();

        if (!int.TryParse(input, out int idx) || idx < 1 || idx > targets.Count)
            return;

        var target = targets[idx - 1];

        terminal.SetColor("cyan");
        terminal.Write("Crime committed: ");
        terminal.SetColor("white");
        string crime = await terminal.ReadLineAsync();
        if (string.IsNullOrEmpty(crime)) crime = "General Misconduct";

        terminal.SetColor("cyan");
        terminal.Write("Sentence (days): ");
        terminal.SetColor("white");
        string sentenceStr = await terminal.ReadLineAsync();

        int sentence = 7;
        if (int.TryParse(sentenceStr, out int s) && s > 0)
            sentence = Math.Min(s, 365);

        // Add to king's prison records
        currentKing.ImprisonCharacter(target.Name, sentence, crime);

        // Actually enforce the imprisonment
        if (target.IsNPC)
        {
            var npc = NPCSpawnSystem.Instance?.GetNPCByName(target.Name);
            if (npc != null)
            {
                npc.DaysInPrison = (byte)Math.Min(255, sentence);
                npc.CurrentLocation = "Prison";
                npc.CellDoorOpen = false;
            }
        }
        else if (DoorMode.IsOnlineMode)
        {
            // Set DaysInPrison on the player's save data
            var backend = SaveSystem.Instance.Backend as SqlSaveBackend;
            if (backend != null)
            {
                await backend.ImprisonPlayer(target.Name, sentence);
                // Send them a message
                try
                {
                    await backend.SendMessage("System", target.Name, "system",
                        $"You have been imprisoned by {currentKing.GetTitle()} {currentKing.Name} for {sentence} days! Crime: {crime}");
                }
                catch { /* notification failed */ }
            }
        }

        // Persist royal court changes in online mode
        if (DoorMode.IsOnlineMode)
            PersistRoyalCourtToWorldState();

        terminal.SetColor("bright_green");
        terminal.WriteLine($"{target.Name} has been imprisoned for {sentence} days!");
        NewsSystem.Instance.Newsy(true, $"{currentKing.GetTitle()} {currentKing.Name} imprisoned {target.Name} for {crime}!");

        await Task.Delay(2000);
    }

    private async Task PardonPrisoner()
    {
        if (currentKing.Prisoners.Count == 0)
        {
            terminal.SetColor("yellow");
            terminal.WriteLine("There are no prisoners to pardon.");
            await Task.Delay(1500);
            return;
        }

        terminal.WriteLine("");
        terminal.SetColor("cyan");
        terminal.Write("Prisoner # to pardon: ");
        terminal.SetColor("white");
        string input = await terminal.ReadLineAsync();

        var keys = currentKing.Prisoners.Keys.ToList();
        if (!int.TryParse(input, out int idx) || idx < 1 || idx > keys.Count)
        {
            terminal.SetColor("red");
            terminal.WriteLine("Invalid selection.");
            await Task.Delay(1500);
            return;
        }

        string name = keys[idx - 1];
        if (currentKing.ReleaseCharacter(name))
        {
            // Actually release the NPC
            var npc = NPCSpawnSystem.Instance?.GetNPCByName(name);
            if (npc != null)
            {
                npc.DaysInPrison = 0;
                npc.CurrentLocation = "MainStreet";
            }

            // Release player in online mode
            if (DoorMode.IsOnlineMode)
            {
                var backend = SaveSystem.Instance.Backend as SqlSaveBackend;
                if (backend != null)
                {
                    await backend.ImprisonPlayer(name, 0);
                    try
                    {
                        await backend.SendMessage("System", name, "system",
                            $"You have been pardoned by {currentKing.GetTitle()} {currentKing.Name}! You are free!");
                    }
                    catch { /* notification failed */ }
                }
                PersistRoyalCourtToWorldState();
            }

            terminal.SetColor("bright_green");
            terminal.WriteLine($"{name} has been pardoned and released!");
            NewsSystem.Instance.Newsy(true, $"{currentKing.GetTitle()} {currentKing.Name} pardoned {name}!");
        }
        else
        {
            terminal.SetColor("red");
            terminal.WriteLine("That person is not in the dungeon.");
        }

        await Task.Delay(2000);
    }

    private async Task ExecutePrisoner()
    {
        if (currentKing.Prisoners.Count == 0)
        {
            terminal.SetColor("yellow");
            terminal.WriteLine("There are no prisoners to execute.");
            await Task.Delay(1500);
            return;
        }

        terminal.WriteLine("");
        terminal.SetColor("bright_red");
        terminal.WriteLine("WARNING: Executions greatly increase your Darkness!");
        terminal.SetColor("cyan");
        terminal.Write("Prisoner # to execute: ");
        terminal.SetColor("white");
        string input = await terminal.ReadLineAsync();

        var keys = currentKing.Prisoners.Keys.ToList();
        if (!int.TryParse(input, out int idx) || idx < 1 || idx > keys.Count)
        {
            terminal.SetColor("red");
            terminal.WriteLine("Invalid selection.");
            await Task.Delay(1500);
            return;
        }

        string name = keys[idx - 1];
        terminal.SetColor("cyan");
        terminal.Write($"Execute {name}? Are you SURE? (Y/N): ");
        terminal.SetColor("white");
        string confirm = await terminal.ReadLineAsync();

        if (confirm?.ToUpper() == "Y")
        {
            currentKing.Prisoners.Remove(name);
            currentPlayer.Darkness += 100;

            // Permadeath the NPC
            var npc = NPCSpawnSystem.Instance?.GetNPCByName(name);
            if (npc != null)
            {
                npc.IsDead = true;
                npc.IsPermaDead = true;
                npc.DaysInPrison = 0;
                npc.HP = 0;
            }

            // Release player from prison (execution just frees them with a penalty)
            if (DoorMode.IsOnlineMode)
            {
                var backend = SaveSystem.Instance.Backend as SqlSaveBackend;
                if (backend != null)
                {
                    await backend.ImprisonPlayer(name, 0);
                    try
                    {
                        await backend.SendMessage("System", name, "system",
                            $"You were sentenced to execution by {currentKing.GetTitle()} {currentKing.Name}! You narrowly escaped with your life but lost 10% of your gold.");
                    }
                    catch { /* notification failed */ }
                    // Deduct 10% gold as execution penalty
                    await backend.DeductGoldFromPlayer(name, 1000);
                }
                PersistRoyalCourtToWorldState();
            }

            terminal.SetColor("red");
            terminal.WriteLine($"{name} has been executed!");
            terminal.WriteLine("Your darkness increases significantly...");
            NewsSystem.Instance.Newsy(true, $"{currentKing.GetTitle()} {currentKing.Name} executed {name}!");
        }
        else
        {
            terminal.SetColor("gray");
            terminal.WriteLine("Execution cancelled.");
        }

        await Task.Delay(2000);
    }

    private async Task SetBailAmount()
    {
        if (currentKing.Prisoners.Count == 0)
        {
            terminal.SetColor("yellow");
            terminal.WriteLine("There are no prisoners.");
            await Task.Delay(1500);
            return;
        }

        terminal.WriteLine("");
        terminal.SetColor("cyan");
        terminal.Write("Prisoner # to set bail: ");
        terminal.SetColor("white");
        string input = await terminal.ReadLineAsync();

        var keys = currentKing.Prisoners.Keys.ToList();
        if (!int.TryParse(input, out int idx) || idx < 1 || idx > keys.Count)
        {
            terminal.SetColor("red");
            terminal.WriteLine("Invalid selection.");
            await Task.Delay(1500);
            return;
        }

        string name = keys[idx - 1];
        terminal.SetColor("cyan");
        terminal.Write($"Bail amount for {name} (0 for no bail): ");
        terminal.SetColor("white");
        string amountStr = await terminal.ReadLineAsync();

        if (long.TryParse(amountStr, out long amount) && amount >= 0)
        {
            currentKing.Prisoners[name].BailAmount = amount;
            terminal.SetColor("bright_green");
            if (amount > 0)
                terminal.WriteLine($"Bail set to {amount:N0} gold for {name}.");
            else
                terminal.WriteLine($"No bail allowed for {name}.");
        }

        await Task.Delay(2000);
    }

    // ═══════════════════════════════════════════════════════════════════════════
    // ROYAL MAIL SYSTEM
    // ═══════════════════════════════════════════════════════════════════════════

    private async Task ReadRoyalMail()
    {
        terminal.ClearScreen();
        terminal.SetColor("bright_magenta");
        terminal.WriteLine("╔══════════════════════════════════════════════════════════════════════════════╗");
        terminal.WriteLine("║                             ROYAL MAIL                                      ║");
        terminal.WriteLine("╚══════════════════════════════════════════════════════════════════════════════╝");
        terminal.WriteLine("");

        // Generate some random mail if there's none
        if (royalMail.Count == 0)
        {
            GenerateRandomMail();
        }

        if (royalMail.Count == 0)
        {
            terminal.SetColor("gray");
            terminal.WriteLine("Your inbox is empty.");
        }
        else
        {
            terminal.SetColor("cyan");
            terminal.WriteLine($"{"#",-3} {"From",-20} {"Subject",-35} {"Status",-10}");
            terminal.SetColor("darkgray");
            terminal.WriteLine(new string('─', 70));

            for (int i = 0; i < royalMail.Count; i++)
            {
                var mail = royalMail[i];
                string status = mail.IsRead ? "Read" : "NEW";
                string statusColor = mail.IsRead ? "gray" : "bright_yellow";

                terminal.SetColor("white");
                terminal.Write($"{i + 1,-3} {mail.Sender,-20} {mail.Subject,-35} ");
                terminal.SetColor(statusColor);
                terminal.WriteLine(status);
            }
        }

        terminal.WriteLine("");
        terminal.SetColor("cyan");
        terminal.Write("Read message # (0 to return): ");
        terminal.SetColor("white");
        string input = await terminal.ReadLineAsync();

        if (int.TryParse(input, out int choice) && choice >= 1 && choice <= royalMail.Count)
        {
            var mail = royalMail[choice - 1];
            mail.IsRead = true;

            terminal.ClearScreen();
            terminal.SetColor("bright_cyan");
            terminal.WriteLine($"From: {mail.Sender}");
            terminal.WriteLine($"Subject: {mail.Subject}");
            terminal.SetColor("darkgray");
            terminal.WriteLine(new string('─', 60));
            terminal.SetColor("white");
            terminal.WriteLine("");
            terminal.WriteLine(mail.Body);
            terminal.WriteLine("");

            terminal.SetColor("darkgray");
            terminal.WriteLine("Press Enter to continue...");
            await terminal.ReadKeyAsync();
        }
    }

    private void GenerateRandomMail()
    {
        string[] senders = { "Royal Advisor", "Castle Steward", "High Priest", "Guild Master", "Merchant Lord", "Town Elder" };
        string[] subjects = { "Treasury Report", "Kingdom Affairs", "Request for Audience", "Trade Proposal", "Security Concerns", "Festival Planning" };
        string[] bodies = {
            "Your Majesty,\n\nThe treasury report for this quarter shows steady growth.\nOur financial situation remains stable.\n\nYour humble servant.",
            "Your Highness,\n\nThe people speak highly of your rule.\nMorale in the kingdom is good.\n\nLong live the Crown!",
            "Most Noble Sovereign,\n\nA delegation from a distant land requests an audience.\nThey bring gifts and proposals of trade.\n\nAwaiting your decision.",
            "My Liege,\n\nThe castle walls require maintenance.\nI recommend allocating funds for repairs.\n\nYour faithful steward.",
            "Your Royal Majesty,\n\nAll is well in the realm.\nThe guards report no incidents.\n\nMay your reign be long and prosperous."
        };

        // Add 1-3 random messages
        int numMessages = random.Next(1, 4);
        for (int i = 0; i < numMessages; i++)
        {
            royalMail.Add(new RoyalMailMessage
            {
                Sender = senders[random.Next(senders.Length)],
                Subject = subjects[random.Next(subjects.Length)],
                Body = bodies[random.Next(bodies.Length)],
                IsRead = false,
                Date = DateTime.Now.AddDays(-random.Next(1, 7))
            });
        }
        // Cap royal mail to prevent unbounded growth
        while (royalMail.Count > MaxRoyalMail)
            royalMail.RemoveAt(0);
    }

    // ═══════════════════════════════════════════════════════════════════════════
    // ROYAL SLEEP
    // ═══════════════════════════════════════════════════════════════════════════

    private async Task RoyalSleep()
    {
        terminal.ClearScreen();
        terminal.SetColor("bright_blue");
        terminal.WriteLine("╔══════════════════════════════════════════════════════════════════════════════╗");
        terminal.WriteLine("║                          THE ROYAL CHAMBERS                                 ║");
        terminal.WriteLine("╚══════════════════════════════════════════════════════════════════════════════╝");
        terminal.WriteLine("");

        terminal.SetColor("white");
        terminal.WriteLine("You retire to your opulent royal chambers.");
        terminal.WriteLine("Servants draw a warm bath and prepare your bed.");
        terminal.WriteLine("");

        await Task.Delay(1500);

        // Full heal
        currentPlayer.HP = currentPlayer.MaxHP;
        currentPlayer.Mana = currentPlayer.MaxMana;
        currentPlayer.Stamina = Math.Max(currentPlayer.Stamina, currentPlayer.Constitution * 2);

        // Remove Groggo's Shadow Blessing on rest
        if (currentPlayer.GroggoShadowBlessingDex > 0)
        {
            currentPlayer.Dexterity = Math.Max(1, currentPlayer.Dexterity - currentPlayer.GroggoShadowBlessingDex);
            terminal.WriteLine("The Blessing of Shadows fades as you rest...", "gray");
            currentPlayer.GroggoShadowBlessingDex = 0;
        }

        if (DoorMode.IsOnlineMode)
        {
            // Online mode: King sleeps in the castle (protected by royal guards) and logs out
            // Day does NOT increment — only the 7 PM ET maintenance reset does that
            terminal.SetColor("bright_green");
            terminal.WriteLine("You rest peacefully in the royal chambers...");
            terminal.WriteLine("");
            terminal.WriteLine($"HP restored to {currentPlayer.MaxHP}!");
            terminal.WriteLine($"Mana restored to {currentPlayer.MaxMana}!");
            terminal.WriteLine("");

            // Show kingdom status
            terminal.SetColor("cyan");
            terminal.WriteLine("=== Kingdom Status ===");
            terminal.SetColor("white");
            terminal.WriteLine($"Treasury Balance: {currentKing.Treasury:N0} gold");
            terminal.WriteLine($"Days of Reign: {currentKing.TotalReign}");
            terminal.WriteLine("");

            // Save game
            await GameEngine.Instance.SaveCurrentGame();

            // Register as sleeping at castle (protected by royal guards)
            var backend = SaveSystem.Instance.Backend as SqlSaveBackend;
            if (backend != null)
            {
                var username = DoorMode.OnlineUsername ?? currentPlayer.Name2;
                // Royal guards protect the king — register with castle guards
                int guardCount = currentKing.Guards?.Count ?? 0;
                var guardsJson = "[]";
                if (guardCount > 0)
                {
                    var guardsList = (currentKing.Guards ?? new List<RoyalGuard>())
                        .Where(g => g != null)
                        .Select(g => new { type = "royal_guard", hp = 500, maxHp = 500 })
                        .ToList();
                    guardsJson = System.Text.Json.JsonSerializer.Serialize(guardsList);
                }
                await backend.RegisterSleepingPlayer(username, "castle", guardsJson, 0);
            }

            terminal.SetColor("gray");
            terminal.WriteLine("Your royal guards stand watch outside the chambers... (logging out)");
            await Task.Delay(2000);

            throw new LocationExitException(GameLocation.NoWhere);
        }
        else
        {
            // Single-player mode: process daily activities and continue
            currentKing.ProcessDailyActivities();

            terminal.SetColor("bright_green");
            terminal.WriteLine("You rest peacefully through the night...");
            terminal.WriteLine("");
            terminal.WriteLine($"HP restored to {currentPlayer.MaxHP}!");
            terminal.WriteLine($"Mana restored to {currentPlayer.MaxMana}!");
            terminal.WriteLine("");

            // Show daily report
            terminal.SetColor("cyan");
            terminal.WriteLine("=== Daily Kingdom Report ===");
            terminal.SetColor("white");
            terminal.WriteLine($"Daily Income: {currentKing.CalculateDailyIncome():N0} gold");
            terminal.WriteLine($"Daily Expenses: {currentKing.CalculateDailyExpenses():N0} gold");
            terminal.WriteLine($"Treasury Balance: {currentKing.Treasury:N0} gold");
            terminal.WriteLine($"Days of Reign: {currentKing.TotalReign}");

            terminal.WriteLine("");
            terminal.SetColor("darkgray");
            terminal.WriteLine("Press Enter to continue...");
            await terminal.ReadKeyAsync();
        }
    }

    // ═══════════════════════════════════════════════════════════════════════════
    // SECURITY CHECK
    // ═══════════════════════════════════════════════════════════════════════════

    private async Task CheckSecurity()
    {
        terminal.ClearScreen();
        terminal.SetColor("bright_cyan");
        terminal.WriteLine("╔══════════════════════════════════════════════════════════════════════════════╗");
        terminal.WriteLine("║                          SECURITY REPORT                                    ║");
        terminal.WriteLine("╚══════════════════════════════════════════════════════════════════════════════╝");
        terminal.WriteLine("");

        // NPC Guard summary
        terminal.SetColor("cyan");
        terminal.WriteLine("=== Royal Guard Status ===");
        terminal.SetColor("white");
        terminal.WriteLine($"NPC Guards: {currentKing.Guards.Count}/{King.MaxNPCGuards}");

        if (currentKing.Guards.Count > 0)
        {
            int avgLoyalty = (int)currentKing.Guards.Average(g => g.Loyalty);
            string loyaltyStatus = avgLoyalty > 80 ? "Excellent" : avgLoyalty > 60 ? "Good" : avgLoyalty > 40 ? "Fair" : "Poor";
            terminal.WriteLine($"Average Loyalty: {avgLoyalty}% ({loyaltyStatus})");
            terminal.WriteLine($"Daily Guard Costs: {currentKing.Guards.Sum(g => g.DailySalary):N0} gold");
            terminal.WriteLine("");

            // Individual guard listing
            terminal.SetColor("cyan");
            terminal.WriteLine($"{"Name",-20} {"Loyalty",-12} {"Salary",-12} {"Status",-10}");
            terminal.SetColor("darkgray");
            terminal.WriteLine(new string('─', 55));

            foreach (var guard in currentKing.Guards)
            {
                string loyaltyColor = guard.Loyalty > 80 ? "bright_green" :
                                     guard.Loyalty > 50 ? "yellow" : "red";
                string status = guard.IsActive ? "Active" : "Inactive";

                terminal.SetColor("white");
                terminal.Write($"{guard.Name,-20} ");
                terminal.SetColor(loyaltyColor);
                terminal.Write($"{guard.Loyalty}%{"",-9} ");
                terminal.SetColor("white");
                terminal.WriteLine($"{guard.DailySalary,-12} {status,-10}");
            }
        }
        else
        {
            terminal.SetColor("yellow");
            terminal.WriteLine("No NPC guards currently employed.");
        }

        terminal.WriteLine("");

        // Monster Guard summary
        terminal.SetColor("bright_red");
        terminal.WriteLine("=== Monster Guards ===");
        terminal.SetColor("white");
        terminal.WriteLine($"Monster Guards: {currentKing.MonsterGuards.Count}/{King.MaxMonsterGuards}");

        if (currentKing.MonsterGuards.Count > 0)
        {
            terminal.WriteLine($"Daily Feeding Costs: {currentKing.MonsterGuards.Sum(m => m.DailyFeedingCost):N0} gold");
            terminal.WriteLine("");

            terminal.SetColor("cyan");
            terminal.WriteLine($"{"Name",-20} {"Level",-8} {"HP",-12} {"Strength",-10}");
            terminal.SetColor("darkgray");
            terminal.WriteLine(new string('─', 55));

            foreach (var monster in currentKing.MonsterGuards)
            {
                terminal.SetColor("red");
                terminal.WriteLine($"{monster.Name,-20} {monster.Level,-8} {monster.HP}/{monster.MaxHP,-8} {monster.Strength,-10}");
            }
        }
        else
        {
            terminal.SetColor("gray");
            terminal.WriteLine("No monster guards currently protecting the castle.");
        }

        terminal.WriteLine("");

        // Security assessment
        terminal.SetColor("cyan");
        terminal.WriteLine("=== Security Assessment ===");
        int securityLevel = CalculateSecurityLevel();
        string securityColor = securityLevel > 80 ? "bright_green" : securityLevel > 50 ? "yellow" : "red";
        terminal.SetColor(securityColor);
        terminal.WriteLine($"Overall Security: {securityLevel}%");
        terminal.SetColor("white");
        terminal.WriteLine($"Total Guards: {currentKing.TotalGuardCount} (Challengers fight monsters first, then NPC guards)");

        if (securityLevel < 50)
        {
            terminal.SetColor("red");
            terminal.WriteLine("Your castle is vulnerable to attack!");
        }

        terminal.WriteLine("");
        terminal.SetColor("cyan");
        terminal.WriteLine("Guard Commands:");
        terminal.SetColor("white");
        terminal.WriteLine("(H)ire NPC guard    (M)onster guard    (F)ire guard");
        terminal.WriteLine("(P)ay bonus         (D)ismiss monster  (R)eturn");
        terminal.WriteLine("");

        terminal.SetColor("cyan");
        terminal.Write("Command: ");
        terminal.SetColor("white");
        string input = await terminal.ReadLineAsync();

        if (!string.IsNullOrEmpty(input))
        {
            switch (char.ToUpper(input[0]))
            {
                case 'H':
                    await HireGuard();
                    break;
                case 'M':
                    await HireMonsterGuard();
                    break;
                case 'F':
                    await FireGuard();
                    break;
                case 'D':
                    await DismissMonsterGuard();
                    break;
                case 'P':
                    await PayGuardBonus();
                    break;
            }
        }
    }

    private int CalculateSecurityLevel()
    {
        int totalGuards = currentKing.TotalGuardCount;
        if (totalGuards == 0) return 10;

        // NPC guards contribute based on loyalty and count
        int npcContribution = 0;
        if (currentKing.Guards.Count > 0)
        {
            int avgLoyalty = (int)currentKing.Guards.Average(g => g.Loyalty);
            npcContribution = (currentKing.Guards.Count * 10) + (avgLoyalty / 5);
        }

        // Monster guards contribute based on level and count
        int monsterContribution = 0;
        if (currentKing.MonsterGuards.Count > 0)
        {
            int avgLevel = (int)currentKing.MonsterGuards.Average(m => m.Level);
            monsterContribution = (currentKing.MonsterGuards.Count * 8) + (avgLevel / 2);
        }

        return Math.Min(100, npcContribution + monsterContribution + 10);
    }

    private async Task HireMonsterGuard()
    {
        if (currentKing.MonsterGuards.Count >= King.MaxMonsterGuards)
        {
            terminal.SetColor("yellow");
            terminal.WriteLine("You already have the maximum number of monster guards!");
            await Task.Delay(2000);
            return;
        }

        terminal.ClearScreen();
        terminal.SetColor("bright_red");
        terminal.WriteLine("╔══════════════════════════════════════════════════════════════════════════════╗");
        terminal.WriteLine("║                      MONSTER GUARD MARKET                                   ║");
        terminal.WriteLine("╚══════════════════════════════════════════════════════════════════════════════╝");
        terminal.WriteLine("");

        terminal.SetColor("white");
        terminal.WriteLine("The Beast Master presents fearsome creatures for your moat defense.");
        terminal.WriteLine("Challengers must defeat ALL monster guards before facing your NPC guards!");
        terminal.WriteLine("");
        terminal.SetColor("yellow");
        terminal.WriteLine($"Treasury: {currentKing.Treasury:N0} gold");
        terminal.WriteLine($"Current Monsters: {currentKing.MonsterGuards.Count}/{King.MaxMonsterGuards}");
        terminal.WriteLine("");

        terminal.SetColor("cyan");
        terminal.WriteLine($"{"#",-3} {"Name",-15} {"Lvl",-5} {"HP",-7} {"STR",-6} {"DEF",-6} {"Cost",-10} {"Feed/Day",-10}");
        terminal.SetColor("darkgray");
        terminal.WriteLine(new string('─', 75));

        // Sort by level for easier selection
        var sortedMonsters = MonsterGuardTypes.AvailableMonsters.OrderBy(m => m.Level).ToArray();

        int i = 1;
        foreach (var (name, level, cost) in sortedMonsters)
        {
            long actualCost = cost + (currentKing.MonsterGuards.Count * 500);
            long feedingCost = 50 + (level * 10);
            // Calculate stats using the same formula as AddMonsterGuard in King.cs
            long hp = 200 + (level * 50);
            long str = 30 + (level * 5);
            long def = 20 + (level * 3);

            bool canAfford = currentKing.Treasury >= actualCost;
            terminal.SetColor(canAfford ? "white" : "darkgray");
            terminal.WriteLine($"{i,-3} {name,-15} {level,-5} {hp,-7} {str,-6} {def,-6} {actualCost:N0,-10} {feedingCost:N0,-10}");
            i++;
        }

        terminal.WriteLine("");
        terminal.SetColor("gray");
        terminal.WriteLine("Note: Each additional monster costs +500g more than base price.");
        terminal.WriteLine("");
        terminal.SetColor("cyan");
        terminal.Write("Purchase which monster? (0 to cancel): ");
        terminal.SetColor("white");
        string input = await terminal.ReadLineAsync();

        if (int.TryParse(input, out int choice) && choice >= 1 && choice <= sortedMonsters.Length)
        {
            var (name, level, cost) = sortedMonsters[choice - 1];

            if (currentKing.AddMonsterGuard(name, level, cost))
            {
                terminal.SetColor("bright_green");
                terminal.WriteLine($"A {name} has been added to your castle defenses!");
                terminal.WriteLine($"The beast now lurks in the moat, awaiting intruders...");
                NewsSystem.Instance.Newsy(true, $"{currentKing.GetTitle()} {currentKing.Name} acquired a fearsome {name} to guard the castle!");
                PersistRoyalCourtToWorldState();
            }
            else
            {
                terminal.SetColor("red");
                terminal.WriteLine("Insufficient funds in the treasury!");
            }
        }

        await Task.Delay(2500);
    }

    private async Task DismissMonsterGuard()
    {
        if (currentKing.MonsterGuards.Count == 0)
        {
            terminal.SetColor("yellow");
            terminal.WriteLine("You have no monster guards to dismiss.");
            await Task.Delay(2000);
            return;
        }

        terminal.SetColor("cyan");
        terminal.Write("Name of monster to dismiss: ");
        terminal.SetColor("white");
        string name = await terminal.ReadLineAsync();

        if (currentKing.RemoveMonsterGuard(name))
        {
            terminal.SetColor("yellow");
            terminal.WriteLine($"The {name} has been released from service.");
            PersistRoyalCourtToWorldState();
        }
        else
        {
            terminal.SetColor("red");
            terminal.WriteLine("No monster by that name found.");
        }

        await Task.Delay(2000);
    }

    private async Task HireGuard()
    {
        if (currentKing.Guards.Count >= King.MaxNPCGuards)
        {
            terminal.SetColor("yellow");
            terminal.WriteLine("You already have the maximum number of guards!");
            await Task.Delay(2000);
            return;
        }

        if (currentKing.Treasury < GameConfig.GuardRecruitmentCost)
        {
            terminal.SetColor("red");
            terminal.WriteLine($"Insufficient funds! Need {GameConfig.GuardRecruitmentCost:N0} gold.");
            await Task.Delay(2000);
            return;
        }

        // Generate random guard name
        string[] firstNames = { "Sir Marcus", "Sir Gerald", "Lady Helena", "Sir Roland", "Dame Elise", "Sir Bartholomew", "Lady Catherine", "Sir Edmund" };
        string guardName = firstNames[random.Next(firstNames.Length)];

        terminal.SetColor("cyan");
        terminal.Write($"Hire {guardName}? Cost: {GameConfig.GuardRecruitmentCost:N0} gold (Y/N): ");
        terminal.SetColor("white");
        string confirm = await terminal.ReadLineAsync();

        if (confirm?.ToUpper() == "Y")
        {
            CharacterSex sex = guardName.StartsWith("Lady") || guardName.StartsWith("Dame") ? CharacterSex.Female : CharacterSex.Male;
            // Scale guard salary with king level (guards hired by stronger kings demand more pay)
            int kLevel = GetKingLevel();
            long guardSalary = GameConfig.BaseGuardSalary + (kLevel * GameConfig.GuardSalaryPerGuardLevel);
            if (currentKing.AddGuard(guardName, CharacterAI.Computer, sex, guardSalary))
            {
                terminal.SetColor("bright_green");
                terminal.WriteLine($"{guardName} has joined the Royal Guard!");
                PersistRoyalCourtToWorldState();
            }
        }

        await Task.Delay(2000);
    }

    private async Task FireGuard()
    {
        if (currentKing.Guards.Count == 0)
        {
            terminal.SetColor("yellow");
            terminal.WriteLine("You have no guards to dismiss.");
            await Task.Delay(2000);
            return;
        }

        terminal.SetColor("cyan");
        terminal.Write("Name of guard to dismiss: ");
        terminal.SetColor("white");
        string name = await terminal.ReadLineAsync();

        if (currentKing.RemoveGuard(name))
        {
            terminal.SetColor("yellow");
            terminal.WriteLine($"{name} has been dismissed from the Royal Guard.");
            PersistRoyalCourtToWorldState();
        }
        else
        {
            terminal.SetColor("red");
            terminal.WriteLine("No guard by that name found.");
        }

        await Task.Delay(2000);
    }

    private async Task PayGuardBonus()
    {
        if (currentKing.Guards.Count == 0)
        {
            terminal.SetColor("yellow");
            terminal.WriteLine("You have no guards to pay.");
            await Task.Delay(2000);
            return;
        }

        terminal.SetColor("cyan");
        terminal.Write("Bonus amount per guard: ");
        terminal.SetColor("white");
        string input = await terminal.ReadLineAsync();

        if (long.TryParse(input, out long bonus) && bonus > 0)
        {
            long totalCost = bonus * currentKing.Guards.Count;
            if (totalCost > currentKing.Treasury)
            {
                terminal.SetColor("red");
                terminal.WriteLine("Insufficient funds in treasury!");
            }
            else
            {
                currentKing.Treasury -= totalCost;
                foreach (var guard in currentKing.Guards)
                {
                    guard.Loyalty = Math.Min(100, guard.Loyalty + (int)(bonus / 100));
                }
                terminal.SetColor("bright_green");
                terminal.WriteLine($"Paid {bonus:N0} gold bonus to each guard!");
                terminal.WriteLine("Guard loyalty has increased!");
                PersistRoyalCourtToWorldState();
            }
        }

        await Task.Delay(2000);
    }

    // ═══════════════════════════════════════════════════════════════════════════
    // MONARCH HISTORY
    // ═══════════════════════════════════════════════════════════════════════════

    private async Task ShowMonarchHistory()
    {
        terminal.ClearScreen();
        terminal.SetColor("bright_yellow");
        terminal.WriteLine("╔══════════════════════════════════════════════════════════════════════════════╗");
        terminal.WriteLine("║                         HISTORY OF MONARCHS                                 ║");
        terminal.WriteLine("╚══════════════════════════════════════════════════════════════════════════════╝");
        terminal.WriteLine("");

        if (monarchHistory.Count == 0)
        {
            terminal.SetColor("gray");
            terminal.WriteLine("The history books are empty. Your reign may be the first!");
            terminal.WriteLine("");
        }
        else
        {
            terminal.SetColor("cyan");
            terminal.WriteLine($"{"#",-3} {"Name",-25} {"Title",-8} {"Reign",-12} {"End",-15}");
            terminal.SetColor("darkgray");
            terminal.WriteLine(new string('─', 65));

            int i = 1;
            foreach (var monarch in monarchHistory.OrderByDescending(m => m.CoronationDate))
            {
                terminal.SetColor("white");
                terminal.WriteLine($"{i,-3} {monarch.Name,-25} {monarch.Title,-8} {monarch.DaysReigned,-12} {monarch.EndReason,-15}");
                i++;
            }
            terminal.WriteLine("");
        }

        // Current monarch
        if (currentKing != null && currentKing.IsActive)
        {
            terminal.SetColor("bright_yellow");
            terminal.WriteLine("=== CURRENT MONARCH ===");
            terminal.SetColor("white");
            terminal.WriteLine($"{currentKing.GetTitle()} {currentKing.Name}");
            terminal.WriteLine($"Reign: {currentKing.TotalReign} days");
            terminal.WriteLine($"Coronation: {currentKing.CoronationDate:d}");
        }

        terminal.WriteLine("");
        terminal.SetColor("darkgray");
        terminal.WriteLine("Press Enter to continue...");
        await terminal.ReadKeyAsync();
    }

    // ═══════════════════════════════════════════════════════════════════════════
    // COURT MAGICIAN
    // ═══════════════════════════════════════════════════════════════════════════

    private async Task CourtMagician()
    {
        terminal.ClearScreen();
        terminal.SetColor("bright_magenta");
        terminal.WriteLine("╔══════════════════════════════════════════════════════════════════════════════╗");
        terminal.WriteLine("║                          THE COURT MAGICIAN                                 ║");
        terminal.WriteLine("╚══════════════════════════════════════════════════════════════════════════════╝");
        terminal.WriteLine("");

        terminal.SetColor("cyan");
        terminal.WriteLine("An elderly wizard in flowing robes approaches you.");
        terminal.SetColor("white");
        terminal.WriteLine("\"Greetings, Your Majesty. How may I serve the Crown?\"");
        terminal.WriteLine("");

        terminal.SetColor("yellow");
        terminal.WriteLine($"Magic Budget: {currentKing.MagicBudget:N0} gold");
        terminal.WriteLine("");

        terminal.SetColor("cyan");
        terminal.WriteLine("Available Services:");
        terminal.SetColor("white");
        terminal.WriteLine("(B)less the Kingdom   - 1,000 gold - Increase kingdom morale");
        terminal.WriteLine("(D)etect Threats      - 500 gold   - Reveal potential dangers");
        terminal.WriteLine("(P)rotection Spell    - 2,000 gold - Boost castle defenses");
        terminal.WriteLine("(S)cry on Enemy       - 1,500 gold - Learn about rivals");
        terminal.WriteLine("(R)eturn");
        terminal.WriteLine("");

        terminal.SetColor("cyan");
        terminal.Write("Your wish: ");
        terminal.SetColor("white");
        string input = await terminal.ReadLineAsync();

        if (!string.IsNullOrEmpty(input))
        {
            switch (char.ToUpper(input[0]))
            {
                case 'B':
                    await CastBlessKingdom();
                    break;
                case 'D':
                    await CastDetectThreats();
                    break;
                case 'P':
                    await CastProtection();
                    break;
                case 'S':
                    await CastScry();
                    break;
            }
        }
    }

    private async Task CastBlessKingdom()
    {
        if (currentKing.MagicBudget < 1000)
        {
            terminal.SetColor("red");
            terminal.WriteLine("Insufficient magic budget!");
            await Task.Delay(2000);
            return;
        }

        currentKing.MagicBudget -= 1000;
        currentPlayer.Chivalry += 25;

        terminal.SetColor("bright_yellow");
        terminal.WriteLine("");
        terminal.WriteLine("The wizard raises his staff and incants ancient words...");
        await Task.Delay(1500);
        terminal.SetColor("bright_green");
        terminal.WriteLine("A golden light spreads across the kingdom!");
        terminal.WriteLine("The people feel blessed! Your chivalry increases!");

        NewsSystem.Instance.Newsy(true, $"{currentKing.GetTitle()} {currentKing.Name} blessed the kingdom with powerful magic!");

        await Task.Delay(2500);
    }

    private async Task CastDetectThreats()
    {
        if (currentKing.MagicBudget < 500)
        {
            terminal.SetColor("red");
            terminal.WriteLine("Insufficient magic budget!");
            await Task.Delay(2000);
            return;
        }

        currentKing.MagicBudget -= 500;

        terminal.SetColor("bright_blue");
        terminal.WriteLine("");
        terminal.WriteLine("The wizard gazes into a crystal ball...");
        await Task.Delay(1500);

        // Get potential threats (high darkness NPCs)
        var threats = NPCSpawnSystem.Instance.ActiveNPCs
            .Where(n => n.IsAlive && n.Darkness > 300)
            .Take(3)
            .ToList();

        if (threats.Count == 0)
        {
            terminal.SetColor("bright_green");
            terminal.WriteLine("\"I sense no significant threats to your realm, Your Majesty.\"");
        }
        else
        {
            terminal.SetColor("yellow");
            terminal.WriteLine("\"I sense darkness in these individuals...\"");
            terminal.WriteLine("");
            foreach (var threat in threats)
            {
                terminal.SetColor("red");
                terminal.WriteLine($"  - {threat.Name} (Level {threat.Level}) - Darkness: {threat.Darkness}");
            }
        }

        await Task.Delay(3000);
    }

    private async Task CastProtection()
    {
        if (currentKing.MagicBudget < 2000)
        {
            terminal.SetColor("red");
            terminal.WriteLine("Insufficient magic budget!");
            await Task.Delay(2000);
            return;
        }

        currentKing.MagicBudget -= 2000;

        // Boost all guards
        foreach (var guard in currentKing.Guards)
        {
            guard.Loyalty = Math.Min(100, guard.Loyalty + 10);
        }

        terminal.SetColor("bright_cyan");
        terminal.WriteLine("");
        terminal.WriteLine("The wizard weaves protective enchantments...");
        await Task.Delay(1500);
        terminal.SetColor("bright_green");
        terminal.WriteLine("The castle glows with magical protection!");
        terminal.WriteLine("Guard loyalty increased!");

        await Task.Delay(2500);
    }

    private async Task CastScry()
    {
        if (currentKing.MagicBudget < 1500)
        {
            terminal.SetColor("red");
            terminal.WriteLine("Insufficient magic budget!");
            await Task.Delay(2000);
            return;
        }

        currentKing.MagicBudget -= 1500;

        terminal.SetColor("bright_blue");
        terminal.WriteLine("");
        terminal.WriteLine("The wizard peers through the mists of time...");
        await Task.Delay(1500);

        // Show info about a random powerful NPC
        var targets = NPCSpawnSystem.Instance.ActiveNPCs
            .Where(n => n.IsAlive && n.Level > 5)
            .ToList();

        if (targets.Count > 0)
        {
            var target = targets[random.Next(targets.Count)];
            terminal.SetColor("cyan");
            terminal.WriteLine($"\"I see {target.Name}...\"");
            terminal.SetColor("white");
            terminal.WriteLine($"  Level: {target.Level}");
            terminal.WriteLine($"  Location: {target.CurrentLocation}");
            terminal.WriteLine($"  Team: {(string.IsNullOrEmpty(target.Team) ? "None" : target.Team)}");
            terminal.WriteLine($"  Gold: ~{target.Gold:N0}");
        }
        else
        {
            terminal.SetColor("gray");
            terminal.WriteLine("\"The mists reveal nothing of note, Your Majesty.\"");
        }

        await Task.Delay(3000);
    }

    // ═══════════════════════════════════════════════════════════════════════════
    // FISCAL MATTERS
    // ═══════════════════════════════════════════════════════════════════════════

    private async Task FiscalMatters()
    {
        bool done = false;
        while (!done)
        {
            terminal.ClearScreen();
            terminal.SetColor("bright_yellow");
            terminal.WriteLine("╔══════════════════════════════════════════════════════════════════════════════╗");
            terminal.WriteLine("║                           FISCAL MATTERS                                    ║");
            terminal.WriteLine("╚══════════════════════════════════════════════════════════════════════════════╝");
            terminal.WriteLine("");

            terminal.SetColor("white");
            terminal.WriteLine($"Royal Treasury: {currentKing.Treasury:N0} gold");
            terminal.WriteLine($"Daily Citizen Tax: {currentKing.TaxRate} gold per citizen");
            terminal.WriteLine($"Tax Alignment: {currentKing.TaxAlignment}");
            terminal.SetColor("yellow");
            terminal.WriteLine($"King's Sales Tax: {currentKing.KingTaxPercent}% of all sales");
            terminal.WriteLine($"City Team Tax:    {currentKing.CityTaxPercent}% of all sales");
            terminal.SetColor("gray");
            terminal.WriteLine($"Sales Tax Revenue Today: {currentKing.DailyTaxRevenue:N0} gold");
            terminal.WriteLine("");

            terminal.SetColor("cyan");
            terminal.WriteLine("=== Financial Summary ===");
            terminal.SetColor("bright_green");
            terminal.WriteLine($"Daily Income:    {currentKing.CalculateDailyIncome():N0} gold");
            terminal.SetColor("red");
            terminal.WriteLine($"Daily Expenses:  {currentKing.CalculateDailyExpenses():N0} gold");
            terminal.SetColor("white");
            long netIncome = currentKing.CalculateDailyIncome() - currentKing.CalculateDailyExpenses();
            string netColor = netIncome >= 0 ? "bright_green" : "red";
            terminal.SetColor(netColor);
            terminal.WriteLine($"Net Daily:       {netIncome:N0} gold");
            terminal.WriteLine("");

            terminal.SetColor("cyan");
            terminal.WriteLine("Options:");
            terminal.SetColor("white");
            terminal.WriteLine("(T)ax Policy        (B)udget Details    (W)ithdraw from Treasury");
            terminal.WriteLine("(D)eposit to Treasury                   (R)eturn");
            terminal.WriteLine("");

            terminal.SetColor("cyan");
            terminal.Write("Decision: ");
            terminal.SetColor("white");
            string input = await terminal.ReadLineAsync();

            if (string.IsNullOrEmpty(input)) continue;

            switch (char.ToUpper(input[0]))
            {
                case 'T':
                    await SetTaxPolicy();
                    break;
                case 'B':
                    await ShowBudgetDetails();
                    break;
                case 'W':
                    await WithdrawFromTreasury();
                    break;
                case 'D':
                    await DepositToTreasury();
                    break;
                case 'R':
                    done = true;
                    break;
            }
        }
    }

    private async Task SetTaxPolicy()
    {
        terminal.WriteLine("");
        terminal.SetColor("cyan");
        terminal.WriteLine("Tax Alignments:");
        terminal.SetColor("white");
        terminal.WriteLine("1. All citizens");
        terminal.WriteLine("2. Good-aligned only");
        terminal.WriteLine("3. Evil-aligned only");
        terminal.WriteLine("4. Neutrals only");
        terminal.WriteLine("");

        terminal.SetColor("cyan");
        terminal.Write("New tax alignment (1-4): ");
        terminal.SetColor("white");
        string alignInput = await terminal.ReadLineAsync();

        if (int.TryParse(alignInput, out int alignChoice) && alignChoice >= 1 && alignChoice <= 4)
        {
            currentKing.TaxAlignment = alignChoice switch
            {
                1 => GameConfig.TaxAlignment.All,
                2 => GameConfig.TaxAlignment.Good,
                3 => GameConfig.TaxAlignment.Evil,
                4 => GameConfig.TaxAlignment.Neutral,
                _ => GameConfig.TaxAlignment.All
            };
        }

        terminal.SetColor("cyan");
        terminal.Write("New daily citizen tax (gold per citizen, 0-1000): ");
        terminal.SetColor("white");
        string rateInput = await terminal.ReadLineAsync();

        if (long.TryParse(rateInput, out long rate) && rate >= 0)
        {
            currentKing.TaxRate = Math.Min(rate, 1000);
            terminal.SetColor("bright_green");
            terminal.WriteLine($"Citizen tax set to {currentKing.TaxRate} gold per citizen.");

            if (rate > 100)
            {
                terminal.SetColor("yellow");
                terminal.WriteLine("Warning: High taxes may cause unrest!");
            }
        }

        terminal.WriteLine("");
        terminal.SetColor("cyan");
        terminal.Write($"King's sales tax (0-25%, currently {currentKing.KingTaxPercent}%): ");
        terminal.SetColor("white");
        string kingTaxInput = await terminal.ReadLineAsync();

        if (int.TryParse(kingTaxInput, out int kingTax) && kingTax >= 0)
        {
            currentKing.KingTaxPercent = Math.Min(kingTax, 25);
            terminal.SetColor("bright_green");
            terminal.WriteLine($"King's sales tax set to {currentKing.KingTaxPercent}%.");

            if (kingTax > 15)
            {
                terminal.SetColor("yellow");
                terminal.WriteLine("Warning: High sales taxes may discourage trade!");
            }
        }

        terminal.SetColor("cyan");
        terminal.Write($"City team tax (0-10%, currently {currentKing.CityTaxPercent}%): ");
        terminal.SetColor("white");
        string cityTaxInput = await terminal.ReadLineAsync();

        if (int.TryParse(cityTaxInput, out int cityTax) && cityTax >= 0)
        {
            currentKing.CityTaxPercent = Math.Min(cityTax, 10);
            terminal.SetColor("bright_green");
            terminal.WriteLine($"City team tax set to {currentKing.CityTaxPercent}%.");
        }

        // Persist tax changes to world_state
        PersistRoyalCourtToWorldState();

        await Task.Delay(2000);
    }

    private async Task ShowBudgetDetails()
    {
        terminal.WriteLine("");
        terminal.SetColor("cyan");
        terminal.WriteLine("=== Income Breakdown ===");
        terminal.SetColor("white");

        int npcCount = UsurperRemake.Systems.NPCSpawnSystem.Instance?.ActiveNPCs?.Count ?? 10;
        long citizenTaxIncome = currentKing.TaxRate * Math.Max(10, npcCount);
        long salesTaxIncome = currentKing.DailyTaxRevenue;

        terminal.WriteLine($"Citizen Tax:       {citizenTaxIncome:N0} gold ({currentKing.TaxRate} x {Math.Max(10, npcCount)} citizens)");
        terminal.WriteLine($"Sales Tax Revenue: {salesTaxIncome:N0} gold ({currentKing.KingTaxPercent}% of sales)");
        terminal.SetColor("darkgray");
        terminal.WriteLine(new string('─', 40));
        terminal.SetColor("bright_green");
        terminal.WriteLine($"Total Income:      {citizenTaxIncome + salesTaxIncome:N0} gold");

        terminal.WriteLine("");
        terminal.SetColor("cyan");
        terminal.WriteLine("=== Expense Breakdown ===");
        terminal.SetColor("white");

        long guardCosts = currentKing.Guards.Sum(g => g.DailySalary);
        long orphanCosts = currentKing.Orphans.Count * GameConfig.OrphanCareCost;
        long baseCosts = 1000;

        terminal.WriteLine($"Guard Salaries:    {guardCosts:N0} gold ({currentKing.Guards.Count} guards)");
        terminal.WriteLine($"Orphanage Costs:   {orphanCosts:N0} gold ({currentKing.Orphans.Count} orphans)");
        terminal.WriteLine($"Castle Maintenance: {baseCosts:N0} gold");
        terminal.SetColor("darkgray");
        terminal.WriteLine(new string('─', 40));
        terminal.SetColor("red");
        terminal.WriteLine($"Total Expenses:    {guardCosts + orphanCosts + baseCosts:N0} gold");

        terminal.WriteLine("");
        terminal.SetColor("darkgray");
        terminal.WriteLine("Press Enter to continue...");
        await terminal.ReadKeyAsync();
    }

    private async Task WithdrawFromTreasury()
    {
        terminal.WriteLine("");
        terminal.SetColor("cyan");
        terminal.Write($"Withdraw how much? (Max: {currentKing.Treasury:N0}): ");
        terminal.SetColor("white");
        string input = await terminal.ReadLineAsync();

        if (long.TryParse(input, out long amount) && amount > 0)
        {
            if (amount > currentKing.Treasury)
            {
                terminal.SetColor("red");
                terminal.WriteLine("The treasury doesn't have that much!");
            }
            else
            {
                currentKing.Treasury -= amount;
                currentPlayer.Gold += amount;
                terminal.SetColor("bright_green");
                terminal.WriteLine($"Withdrew {amount:N0} gold from the treasury.");
                PersistRoyalCourtToWorldState();
            }
        }

        await Task.Delay(2000);
    }

    private async Task DepositToTreasury()
    {
        terminal.WriteLine("");
        terminal.SetColor("cyan");
        terminal.Write($"Deposit how much? (You have: {currentPlayer.Gold:N0}): ");
        terminal.SetColor("white");
        string input = await terminal.ReadLineAsync();

        if (long.TryParse(input, out long amount) && amount > 0)
        {
            if (amount > currentPlayer.Gold)
            {
                terminal.SetColor("red");
                terminal.WriteLine("You don't have that much gold!");
            }
            else
            {
                currentPlayer.Gold -= amount;
                currentKing.Treasury += amount;
                terminal.SetColor("bright_green");
                terminal.WriteLine($"Deposited {amount:N0} gold to the treasury.");
                PersistRoyalCourtToWorldState();
            }
        }

        await Task.Delay(2000);
    }

    // ═══════════════════════════════════════════════════════════════════════════
    // ROYAL ORDERS
    // ═══════════════════════════════════════════════════════════════════════════

    private async Task RoyalOrders()
    {
        bool done = false;
        while (!done)
        {
            terminal.ClearScreen();
            terminal.SetColor("bright_yellow");
            terminal.WriteLine("╔══════════════════════════════════════════════════════════════════════════════╗");
            terminal.WriteLine("║                             ROYAL ORDERS                                    ║");
            terminal.WriteLine("╚══════════════════════════════════════════════════════════════════════════════╝");
            terminal.WriteLine("");

            terminal.SetColor("white");
            terminal.WriteLine("Issue decrees and manage the kingdom.");
            terminal.WriteLine("");

            if (!string.IsNullOrEmpty(currentKing.LastProclamation))
            {
                terminal.SetColor("cyan");
                terminal.WriteLine("Last Proclamation:");
                terminal.SetColor("gray");
                terminal.WriteLine($"\"{currentKing.LastProclamation}\"");
                terminal.WriteLine("");
            }

            terminal.SetColor("cyan");
            terminal.WriteLine("Commands:");
            terminal.SetColor("white");
            terminal.WriteLine("(E)stablishments    (P)roclamation     (B)ounty on someone");
            terminal.WriteLine("(R)eturn");
            terminal.WriteLine("");

            terminal.SetColor("cyan");
            terminal.Write("Orders: ");
            terminal.SetColor("white");
            string input = await terminal.ReadLineAsync();

            if (string.IsNullOrEmpty(input)) continue;

            switch (char.ToUpper(input[0]))
            {
                case 'E':
                    await ManageEstablishments();
                    break;
                case 'P':
                    await IssueProclamation();
                    break;
                case 'B':
                    await PlaceBounty();
                    break;
                case 'R':
                    done = true;
                    break;
            }
        }
    }

    private async Task ManageEstablishments()
    {
        terminal.ClearScreen();
        terminal.SetColor("cyan");
        terminal.WriteLine("=== Establishment Status ===");
        terminal.WriteLine("");

        int i = 1;
        var establishments = currentKing.EstablishmentStatus.ToList();
        foreach (var est in establishments)
        {
            string status = est.Value ? "OPEN" : "CLOSED";
            string statusColor = est.Value ? "bright_green" : "red";

            terminal.SetColor("white");
            terminal.Write($"{i}. {est.Key,-20} ");
            terminal.SetColor(statusColor);
            terminal.WriteLine(status);
            i++;
        }

        terminal.WriteLine("");
        terminal.SetColor("cyan");
        terminal.Write("Toggle which establishment? (0 to return): ");
        terminal.SetColor("white");
        string input = await terminal.ReadLineAsync();

        if (int.TryParse(input, out int choice) && choice >= 1 && choice <= establishments.Count)
        {
            var key = establishments[choice - 1].Key;
            currentKing.EstablishmentStatus[key] = !currentKing.EstablishmentStatus[key];

            string newStatus = currentKing.EstablishmentStatus[key] ? "opened" : "closed";
            terminal.SetColor("bright_green");
            terminal.WriteLine($"The {key} is now {newStatus}!");

            NewsSystem.Instance.Newsy(true, $"{currentKing.GetTitle()} {currentKing.Name} has {newStatus} the {key}!");

            await Task.Delay(2000);
        }
    }

    private async Task IssueProclamation()
    {
        terminal.WriteLine("");
        terminal.SetColor("cyan");
        terminal.WriteLine("Issue a Royal Proclamation to all citizens.");
        terminal.Write("Your decree: ");
        terminal.SetColor("white");
        string proclamation = await terminal.ReadLineAsync();

        if (!string.IsNullOrEmpty(proclamation))
        {
            currentKing.LastProclamation = proclamation;
            currentKing.LastProclamationDate = DateTime.Now;

            terminal.SetColor("bright_yellow");
            terminal.WriteLine("");
            terminal.WriteLine("HEAR YE, HEAR YE!");
            terminal.SetColor("white");
            terminal.WriteLine($"By royal decree of {currentKing.GetTitle()} {currentKing.Name}:");
            terminal.WriteLine($"\"{proclamation}\"");

            NewsSystem.Instance.Newsy(true, $"Royal Proclamation: \"{proclamation}\" - {currentKing.GetTitle()} {currentKing.Name}");

            await Task.Delay(3000);
        }
    }

    private async Task PlaceBounty()
    {
        terminal.WriteLine("");
        terminal.SetColor("cyan");
        terminal.Write("Name of the criminal: ");
        terminal.SetColor("white");
        string name = await terminal.ReadLineAsync();

        if (string.IsNullOrEmpty(name)) return;

        terminal.SetColor("cyan");
        terminal.Write("Bounty amount: ");
        terminal.SetColor("white");
        string amountStr = await terminal.ReadLineAsync();

        if (long.TryParse(amountStr, out long amount) && amount > 0)
        {
            if (amount > currentKing.Treasury)
            {
                terminal.SetColor("red");
                terminal.WriteLine("Insufficient funds in treasury!");
            }
            else
            {
                currentKing.Treasury -= amount;
                terminal.SetColor("bright_red");
                terminal.WriteLine($"A bounty of {amount:N0} gold has been placed on {name}!");

                NewsSystem.Instance.Newsy(true, $"BOUNTY: {amount:N0} gold on {name} by order of {currentKing.GetTitle()} {currentKing.Name}!");

                // Wire into QuestSystem so the bounty is trackable
                QuestSystem.PostBountyOnPlayer(name, "Royal decree", (int)Math.Min(amount, int.MaxValue));
            }
        }

        await Task.Delay(2500);
    }


    // ═══════════════════════════════════════════════════════════════════════════
    // ROYAL ORPHANAGE
    // ═══════════════════════════════════════════════════════════════════════════

    private static readonly string[] OrphanNamesMale = {
        "Tommy", "Billy", "Jack", "Oliver", "Henry", "Arthur", "Finn", "Leo",
        "Marcus", "Theo", "Cedric", "Robin", "Edmund", "Gareth", "Pip", "Rory"
    };
    private static readonly string[] OrphanNamesFemale = {
        "Sarah", "Emma", "Lily", "Sophie", "Rose", "Clara", "Ivy", "Hazel",
        "Nora", "Wren", "Elise", "Mabel", "Ada", "Greta", "Tess", "Fern"
    };
    private static readonly string[] OrphanBackstories = {
        "Found wandering the streets after a bandit raid.",
        "Parents lost to dungeon creatures.",
        "Left at the castle gates wrapped in a tattered blanket.",
        "Orphaned by plague in the outer villages.",
        "Sole survivor of a merchant caravan attack.",
        "Abandoned at the church doorstep as an infant.",
        "Parents died in a mining collapse.",
        "Found hiding in the ruins of a burned farmstead.",
        "Ran away from a cruel master in a distant town.",
        "Family lost at sea during a storm."
    };

    private async Task RoyalOrphanage()
    {
        while (true)
        {
            terminal.ClearScreen();
            terminal.SetColor("bright_cyan");
            terminal.WriteLine("╔══════════════════════════════════════════════════════════════════════════════╗");
            terminal.WriteLine("║                          THE ROYAL ORPHANAGE                                ║");
            terminal.WriteLine("╚══════════════════════════════════════════════════════════════════════════════╝");
            terminal.WriteLine("");

            // Update ages for real orphans
            foreach (var o in currentKing.Orphans.Where(o => o.IsRealOrphan))
                o.Age = o.ComputedAge;

            terminal.SetColor("white");
            terminal.WriteLine("You visit the children under royal protection.");
            long dailyCost = currentKing.Orphans.Count * GameConfig.OrphanCareCost;
            terminal.WriteLine($"Orphans: {currentKing.Orphans.Count}/{GameConfig.MaxRoyalOrphans}   Daily care cost: {dailyCost:N0} gold ({GameConfig.OrphanCareCost} per child)");
            terminal.WriteLine("");

            if (currentKing.Orphans.Count == 0)
            {
                terminal.SetColor("gray");
                terminal.WriteLine("The orphanage is empty. Perhaps you could take in a child in need.");
            }
            else
            {
                terminal.SetColor("cyan");
                terminal.WriteLine($"{"#",-4} {"Name",-20} {"Age",-6} {"Sex",-8} {"Race",-10} {"Type",-10} {"Happy",-6}");
                terminal.SetColor("darkgray");
                terminal.WriteLine(new string('─', 66));

                for (int i = 0; i < currentKing.Orphans.Count; i++)
                {
                    var orphan = currentKing.Orphans[i];
                    string happyColor = orphan.Happiness > 70 ? "bright_green" :
                                       orphan.Happiness > 40 ? "yellow" : "red";
                    string sexStr = orphan.Sex == CharacterSex.Male ? "Boy" : "Girl";
                    string typeStr = orphan.IsRealOrphan ? "Orphaned" : "Adopted";
                    string typeColor = orphan.IsRealOrphan ? "bright_magenta" : "cyan";
                    string raceStr = orphan.IsRealOrphan ? orphan.Race.ToString() : "-";

                    terminal.SetColor("gray");
                    terminal.Write($"{i + 1,-4} ");
                    terminal.SetColor("white");
                    terminal.Write($"{orphan.Name,-20} {orphan.Age,-6} {sexStr,-8} {raceStr,-10} ");
                    terminal.SetColor(typeColor);
                    terminal.Write($"{typeStr,-10} ");
                    terminal.SetColor(happyColor);
                    terminal.Write($"{orphan.Happiness}%");

                    // Coming-of-age indicator
                    if (orphan.IsRealOrphan && orphan.Age >= GameConfig.OrphanCommissionAge)
                    {
                        terminal.SetColor("bright_yellow");
                        terminal.Write(orphan.Age >= 18 ? "  ADULT" : "  Ready!");
                    }
                    terminal.WriteLine("");
                }
            }

            terminal.WriteLine("");
            terminal.SetColor("cyan");
            terminal.WriteLine("Commands:");
            long adoptCost = 500 + (currentKing.Orphans.Count * 100);
            terminal.SetColor("bright_yellow");
            terminal.Write("  [A] "); terminal.SetColor("white"); terminal.WriteLine($"Adopt new orphan ({adoptCost:N0} gold from treasury)");
            terminal.SetColor("bright_yellow");
            terminal.Write("  [V] "); terminal.SetColor("white"); terminal.WriteLine("View orphan details");

            // Show commission option if any real orphans are old enough
            bool hasCommissionable = currentKing.Orphans.Any(o => o.IsRealOrphan && o.Age >= GameConfig.OrphanCommissionAge);
            if (hasCommissionable)
            {
                terminal.SetColor("bright_yellow");
                terminal.Write("  [C] "); terminal.SetColor("white"); terminal.WriteLine($"Commission orphan (recruit age {GameConfig.OrphanCommissionAge}+, {GameConfig.OrphanCommissionCost:N0} gold)");
            }

            terminal.SetColor("bright_yellow");
            terminal.Write("  [G] "); terminal.SetColor("white"); terminal.WriteLine("Give gifts (increase happiness)");
            terminal.SetColor("bright_yellow");
            terminal.Write("  [R] "); terminal.SetColor("white"); terminal.WriteLine("Return to Royal Menu");
            terminal.WriteLine("");

            terminal.SetColor("cyan");
            terminal.Write("Action: ");
            terminal.SetColor("white");
            string input = await terminal.ReadLineAsync();

            if (string.IsNullOrEmpty(input) || char.ToUpper(input[0]) == 'R')
                break;

            switch (char.ToUpper(input[0]))
            {
                case 'A':
                    await AdoptOrphan();
                    break;
                case 'V':
                    await ViewOrphanDetails();
                    break;
                case 'C':
                    if (hasCommissionable)
                        await CommissionOrphan();
                    break;
                case 'G':
                    await GiveGiftsToOrphans();
                    break;
            }
        }
    }

    private async Task ViewOrphanDetails()
    {
        if (currentKing.Orphans.Count == 0)
        {
            terminal.SetColor("yellow");
            terminal.WriteLine("No orphans to view.");
            await Task.Delay(1500);
            return;
        }

        terminal.SetColor("cyan");
        terminal.Write("Orphan number: ");
        terminal.SetColor("white");
        string input = await terminal.ReadLineAsync();

        if (!int.TryParse(input, out int idx) || idx < 1 || idx > currentKing.Orphans.Count)
        {
            terminal.SetColor("red");
            terminal.WriteLine("Invalid selection.");
            await Task.Delay(1000);
            return;
        }

        var orphan = currentKing.Orphans[idx - 1];
        terminal.WriteLine("");
        terminal.SetColor("bright_cyan");
        terminal.WriteLine($"  === {orphan.Name} ===");
        terminal.SetColor("white");
        terminal.WriteLine($"  Age: {orphan.Age}   Sex: {(orphan.Sex == CharacterSex.Male ? "Boy" : "Girl")}   Happiness: {orphan.Happiness}%");

        if (orphan.IsRealOrphan)
        {
            terminal.SetColor("bright_magenta");
            terminal.WriteLine("  Type: Orphaned (both parents deceased)");
            terminal.SetColor("white");
            terminal.WriteLine($"  Mother: {orphan.MotherName ?? "Unknown"}");
            terminal.WriteLine($"  Father: {orphan.FatherName ?? "Unknown"}");
            terminal.WriteLine($"  Race: {orphan.Race}");
            string soulDesc = orphan.Soul > 200 ? "Pure-hearted" :
                              orphan.Soul > 100 ? "Good-natured" :
                              orphan.Soul < -200 ? "Dark-souled" :
                              orphan.Soul < -100 ? "Troubled" : "Neutral";
            terminal.WriteLine($"  Temperament: {soulDesc} ({orphan.Soul:+0;-0;0})");

            if (orphan.Age >= GameConfig.OrphanCommissionAge)
            {
                terminal.SetColor("bright_yellow");
                terminal.WriteLine($"  Status: Ready for commission (age {GameConfig.OrphanCommissionAge}+)");
            }
        }
        else
        {
            terminal.SetColor("cyan");
            terminal.WriteLine("  Type: Adopted (taken in by the crown)");
        }

        terminal.SetColor("gray");
        terminal.WriteLine($"  \"{orphan.BackgroundStory}\"");
        terminal.SetColor("white");
        terminal.WriteLine($"  Arrived: {orphan.ArrivalDate:yyyy-MM-dd}");

        terminal.WriteLine("");
        await terminal.PressAnyKey();
    }

    private async Task CommissionOrphan()
    {
        var eligible = currentKing.Orphans
            .Select((o, i) => (orphan: o, index: i))
            .Where(x => x.orphan.IsRealOrphan && x.orphan.Age >= GameConfig.OrphanCommissionAge)
            .ToList();

        if (eligible.Count == 0)
        {
            terminal.SetColor("yellow");
            terminal.WriteLine($"No real orphans age {GameConfig.OrphanCommissionAge}+ available for commission.");
            await Task.Delay(1500);
            return;
        }

        if (currentKing.Treasury < GameConfig.OrphanCommissionCost)
        {
            terminal.SetColor("red");
            terminal.WriteLine($"Insufficient treasury! Need {GameConfig.OrphanCommissionCost:N0} gold for training costs.");
            await Task.Delay(1500);
            return;
        }

        terminal.SetColor("cyan");
        terminal.WriteLine("Eligible orphans for commission:");
        foreach (var (orphan, _) in eligible)
        {
            int displayIdx = currentKing.Orphans.IndexOf(orphan) + 1;
            terminal.SetColor("white");
            terminal.WriteLine($"  {displayIdx}. {orphan.Name} (Age {orphan.Age}, {orphan.Race}, {(orphan.Sex == CharacterSex.Male ? "M" : "F")})");
        }

        terminal.SetColor("cyan");
        terminal.Write("Select orphan number: ");
        terminal.SetColor("white");
        string input = await terminal.ReadLineAsync();

        if (!int.TryParse(input, out int idx) || idx < 1 || idx > currentKing.Orphans.Count)
        {
            terminal.SetColor("red");
            terminal.WriteLine("Invalid selection.");
            await Task.Delay(1000);
            return;
        }

        var selected = currentKing.Orphans[idx - 1];
        if (!selected.IsRealOrphan || selected.Age < GameConfig.OrphanCommissionAge)
        {
            terminal.SetColor("red");
            terminal.WriteLine("That orphan is not eligible for commission.");
            await Task.Delay(1000);
            return;
        }

        terminal.WriteLine("");
        terminal.SetColor("cyan");
        terminal.WriteLine($"Commission {selected.Name} as:");
        terminal.SetColor("bright_yellow");
        terminal.Write("  [G] "); terminal.SetColor("white"); terminal.WriteLine("Royal Guard (free guard slot, high loyalty)");
        terminal.SetColor("bright_yellow");
        terminal.Write("  [M] "); terminal.SetColor("white"); terminal.WriteLine("Royal Mercenary (dungeon bodyguard, half cost)");
        terminal.SetColor("bright_yellow");
        terminal.Write("  [N] "); terminal.SetColor("white"); terminal.WriteLine("Release as NPC citizen");
        terminal.SetColor("bright_yellow");
        terminal.Write("  [X] "); terminal.SetColor("white"); terminal.WriteLine("Cancel");
        terminal.WriteLine("");

        terminal.SetColor("cyan");
        terminal.Write("Choice: ");
        terminal.SetColor("white");
        string choice = await terminal.ReadLineAsync();

        if (string.IsNullOrEmpty(choice)) return;

        switch (char.ToUpper(choice[0]))
        {
            case 'G':
                await CommissionAsGuard(selected);
                break;
            case 'M':
                await CommissionAsMercenary(selected);
                break;
            case 'N':
                await CommissionAsNPC(selected);
                break;
            default:
                return;
        }
    }

    private async Task CommissionAsGuard(RoyalOrphan orphan)
    {
        if (currentKing.Guards.Count >= King.MaxNPCGuards)
        {
            terminal.SetColor("red");
            terminal.WriteLine("No guard slots available!");
            await Task.Delay(1500);
            return;
        }

        currentKing.Treasury -= GameConfig.OrphanCommissionCost;
        currentKing.Orphans.Remove(orphan);

        // Mark underlying Child as deleted
        MarkOrphanChildDeleted(orphan);

        // Create the NPC entity first (for combat stats)
        WorldSimulator.Instance?.OrphanBecomesNPC(orphan);

        // Create guard record with high loyalty (raised by the crown)
        var guard = new RoyalGuard
        {
            Name = orphan.Name,
            AI = CharacterAI.Computer,
            Sex = orphan.Sex,
            DailySalary = GameConfig.BaseGuardSalary,
            RecruitmentDate = DateTime.Now,
            Loyalty = 90 // Very high — raised by the crown
        };
        currentKing.Guards.Add(guard);

        terminal.SetColor("bright_green");
        terminal.WriteLine($"{orphan.Name} has been commissioned as a Royal Guard!");
        terminal.SetColor("white");
        terminal.WriteLine("Loyalty: 90 (raised by the crown)");
        terminal.SetColor("yellow");
        terminal.WriteLine($"Treasury: -{GameConfig.OrphanCommissionCost:N0} gold");

        currentPlayer.Chivalry += 10;
        PersistRoyalCourtToWorldState();
        await Task.Delay(2500);
    }

    private async Task CommissionAsMercenary(RoyalOrphan orphan)
    {
        if (currentPlayer.RoyalMercenaries.Count >= GameConfig.MaxRoyalMercenaries)
        {
            terminal.SetColor("red");
            terminal.WriteLine("No mercenary slots available!");
            await Task.Delay(1500);
            return;
        }

        // Half the normal mercenary cost (orphan raised by the crown)
        long mercCost = GameConfig.OrphanCommissionCost + (GameConfig.MercenaryBaseCost + currentPlayer.Level * GameConfig.MercenaryCostPerLevel) / 2;
        if (currentKing.Treasury < mercCost)
        {
            terminal.SetColor("red");
            terminal.WriteLine($"Insufficient treasury! Need {mercCost:N0} gold.");
            await Task.Delay(1500);
            return;
        }

        currentKing.Treasury -= mercCost;
        currentKing.Orphans.Remove(orphan);
        MarkOrphanChildDeleted(orphan);

        // Pick a mercenary role based on soul
        string role = orphan.Soul > 100 ? "Support" :
                      orphan.Soul < -100 ? "DPS" :
                      "Tank";
        var merc = GenerateMercenary(role, currentPlayer.Level);
        merc.Name = orphan.Name; // Use their actual name
        merc.Sex = orphan.Sex;
        currentPlayer.RoyalMercenaries.Add(merc);

        terminal.SetColor("bright_green");
        terminal.WriteLine($"{orphan.Name} has been commissioned as a Royal Mercenary ({role})!");
        terminal.SetColor("white");
        terminal.WriteLine($"Level: {merc.Level}  HP: {merc.MaxHP}  STR: {merc.Strength}  DEF: {merc.Defence}");
        terminal.SetColor("yellow");
        terminal.WriteLine($"Treasury: -{mercCost:N0} gold");

        currentPlayer.Chivalry += 10;
        PersistRoyalCourtToWorldState();
        await Task.Delay(2500);
    }

    private async Task CommissionAsNPC(RoyalOrphan orphan)
    {
        currentKing.Treasury -= GameConfig.OrphanCommissionCost;
        currentKing.Orphans.Remove(orphan);
        MarkOrphanChildDeleted(orphan);

        WorldSimulator.Instance?.OrphanBecomesNPC(orphan);

        terminal.SetColor("bright_green");
        terminal.WriteLine($"{orphan.Name} has been released into the world as a citizen!");
        terminal.SetColor("white");
        terminal.WriteLine("They will make their own way, grateful for the crown's care.");
        terminal.SetColor("yellow");
        terminal.WriteLine($"Treasury: -{GameConfig.OrphanCommissionCost:N0} gold");

        currentPlayer.Chivalry += 5;
        PersistRoyalCourtToWorldState();
        await Task.Delay(2500);
    }

    private void MarkOrphanChildDeleted(RoyalOrphan orphan)
    {
        if (!orphan.IsRealOrphan) return;
        var child = FamilySystem.Instance?.AllChildren
            .FirstOrDefault(c => c.Name == orphan.Name && !c.Deleted &&
                                 c.Location == GameConfig.ChildLocationOrphanage);
        if (child != null)
            child.Deleted = true;
    }

    private async Task AdoptOrphan()
    {
        if (currentKing.Orphans.Count >= GameConfig.MaxRoyalOrphans)
        {
            terminal.SetColor("yellow");
            terminal.WriteLine($"The orphanage is full ({GameConfig.MaxRoyalOrphans} children).");
            await Task.Delay(1500);
            return;
        }

        long adoptCost = 500 + (currentKing.Orphans.Count * 100);
        if (currentKing.Treasury < adoptCost)
        {
            terminal.SetColor("red");
            terminal.WriteLine($"Insufficient treasury funds! Need {adoptCost:N0} gold to cover intake costs.");
            await Task.Delay(1500);
            return;
        }

        // Pick a name that isn't already used
        var sex = random.Next(2) == 0 ? CharacterSex.Male : CharacterSex.Female;
        var namePool = sex == CharacterSex.Male ? OrphanNamesMale : OrphanNamesFemale;
        var usedNames = new HashSet<string>(currentKing.Orphans.Select(o => o.Name));
        var availableNames = namePool.Where(n => !usedNames.Contains(n)).ToArray();
        if (availableNames.Length == 0)
            availableNames = namePool; // All used — allow duplicates as fallback

        string name = availableNames[random.Next(availableNames.Length)];

        var orphan = new RoyalOrphan
        {
            Name = name,
            Age = random.Next(3, 13),
            Sex = sex,
            Happiness = 50,
            BackgroundStory = OrphanBackstories[random.Next(OrphanBackstories.Length)]
        };

        currentKing.Orphans.Add(orphan);
        currentKing.Treasury -= adoptCost;

        terminal.SetColor("bright_green");
        string sexStr = sex == CharacterSex.Male ? "boy" : "girl";
        terminal.WriteLine($"{name}, a {orphan.Age}-year-old {sexStr}, has been taken into the Royal Orphanage.");
        terminal.SetColor("gray");
        terminal.WriteLine($"  \"{orphan.BackgroundStory}\"");
        terminal.SetColor("bright_green");
        terminal.WriteLine("Your compassion increases your standing with the people!");
        terminal.SetColor("yellow");
        terminal.WriteLine($"Treasury: -{adoptCost:N0} gold");

        currentPlayer.Chivalry += 15;
        PersistRoyalCourtToWorldState();

        await Task.Delay(2500);
    }

    private async Task GiveGiftsToOrphans()
    {
        if (currentKing.Orphans.Count == 0)
        {
            terminal.SetColor("yellow");
            terminal.WriteLine("There are no orphans to gift.");
            await Task.Delay(1500);
            return;
        }

        terminal.SetColor("white");
        terminal.WriteLine($"Treasury: {currentKing.Treasury:N0} gold");
        terminal.SetColor("cyan");
        terminal.Write("Gift amount (gold): ");
        terminal.SetColor("white");
        string input = await terminal.ReadLineAsync();

        if (long.TryParse(input, out long amount) && amount > 0)
        {
            if (amount > currentKing.Treasury)
            {
                terminal.SetColor("red");
                terminal.WriteLine("Insufficient funds in treasury!");
            }
            else
            {
                currentKing.Treasury -= amount;
                int happinessBoost = (int)Math.Min(30, amount / (currentKing.Orphans.Count * 50));
                if (happinessBoost < 1) happinessBoost = 1;

                foreach (var orphan in currentKing.Orphans)
                {
                    orphan.Happiness = Math.Min(100, orphan.Happiness + happinessBoost);
                }

                terminal.SetColor("bright_green");
                terminal.WriteLine("The children are delighted with your generosity!");
                terminal.WriteLine($"Orphan happiness increased by {happinessBoost}%!");
                terminal.SetColor("yellow");
                terminal.WriteLine($"Treasury: -{amount:N0} gold");

                currentPlayer.Chivalry += (int)Math.Min(50, amount / 200);
                PersistRoyalCourtToWorldState();
            }
        }

        await Task.Delay(2000);
    }

    // ═══════════════════════════════════════════════════════════════════════════
    // ROYAL MARRIAGE
    // ═══════════════════════════════════════════════════════════════════════════

    private async Task ArrangeRoyalMarriage()
    {
        terminal.ClearScreen();
        terminal.SetColor("bright_magenta");
        terminal.WriteLine("╔══════════════════════════════════════════════════════════════════════════════╗");
        terminal.WriteLine("║                         ROYAL MARRIAGE                                       ║");
        terminal.WriteLine("╚══════════════════════════════════════════════════════════════════════════════╝");
        terminal.WriteLine("");

        // Check if already married
        if (currentKing.Spouse != null)
        {
            terminal.SetColor("cyan");
            terminal.WriteLine($"You are already married to {currentKing.Spouse.Name}.");
            terminal.WriteLine($"Married on: {currentKing.Spouse.MarriageDate:d}");
            terminal.WriteLine($"Spouse's happiness: {currentKing.Spouse.Happiness}%");
            terminal.WriteLine($"Original faction: {currentKing.Spouse.OriginalFaction}");
            terminal.WriteLine("");

            terminal.SetColor("yellow");
            terminal.WriteLine("Options:");
            terminal.Write(" [");
            terminal.SetColor("bright_yellow");
            terminal.Write("G");
            terminal.SetColor("yellow");
            terminal.WriteLine("]ift to spouse (improve happiness)");
            terminal.Write(" [");
            terminal.SetColor("bright_yellow");
            terminal.Write("D");
            terminal.SetColor("yellow");
            terminal.WriteLine("]ivorce (political consequences)");
            terminal.Write(" [");
            terminal.SetColor("bright_yellow");
            terminal.Write("R");
            terminal.SetColor("yellow");
            terminal.WriteLine("]eturn");
            terminal.WriteLine("");

            terminal.SetColor("cyan");
            terminal.Write("Your choice: ");
            terminal.SetColor("white");
            string input = await terminal.ReadLineAsync();

            switch (input?.ToUpper())
            {
                case "G":
                    await GiftToSpouse();
                    break;
                case "D":
                    await DivorceSpouse();
                    break;
            }

            await terminal.PressAnyKey();
            return;
        }

        terminal.SetColor("white");
        terminal.WriteLine("A royal marriage can secure alliances, fill the treasury with dowry,");
        terminal.WriteLine("and produce heirs to continue your dynasty.");
        terminal.WriteLine("");

        // Find eligible NPCs for marriage
        var eligibleNPCs = NPCSpawnSystem.Instance?.ActiveNPCs?
            .Where(n => n.IsAlive &&
                   !n.IsDead &&
                   !n.IsMarried &&
                   !n.Married &&
                   n.Level >= 10 &&
                   !n.King &&
                   !n.IsStoryNPC &&
                   string.IsNullOrEmpty(n.Team))  // Can't marry team members
            .OrderByDescending(n => n.Level)
            .Take(5)
            .ToList();

        if (eligibleNPCs == null || eligibleNPCs.Count == 0)
        {
            terminal.SetColor("yellow");
            terminal.WriteLine("There are no suitable marriage candidates at this time.");
            terminal.WriteLine("Candidates must be level 10+, not on a team, and not a story NPC.");
            await terminal.PressAnyKey();
            return;
        }

        terminal.SetColor("cyan");
        terminal.WriteLine("Potential Marriage Candidates:");
        terminal.WriteLine("");

        terminal.SetColor("white");
        terminal.WriteLine($"{"#",-3} {"Name",-20} {"Level",-8} {"Dowry",-12} {"Faction Benefit"}");
        terminal.SetColor("darkgray");
        terminal.WriteLine(new string('─', 70));

        int i = 1;
        foreach (var npc in eligibleNPCs)
        {
            // Calculate dowry based on level and charisma
            long dowry = (npc.Level * 1000) + (npc.Charisma * 100);
            var faction = DetermineFactionForNPC(npc);

            terminal.SetColor("white");
            terminal.WriteLine($"{i,-3} {npc.Name,-20} {npc.Level,-8} {dowry:N0,-12} +20 {faction} relations");
            i++;
        }

        terminal.WriteLine("");
        terminal.SetColor("cyan");
        terminal.Write("Propose to which candidate? (0 to cancel): ");
        terminal.SetColor("white");
        string choice = await terminal.ReadLineAsync();

        if (int.TryParse(choice, out int selection) && selection >= 1 && selection <= eligibleNPCs.Count)
        {
            var candidate = eligibleNPCs[selection - 1];
            await ProposeMarriage(candidate);
        }

        await terminal.PressAnyKey();
    }

    private CourtFaction DetermineFactionForNPC(NPC npc)
    {
        // Determine faction based on personality
        if (npc.Brain?.Personality == null)
            return CourtFaction.None;

        var p = npc.Brain.Personality;
        if (p.Aggression > 0.7f) return CourtFaction.Militarists;
        if (p.Greed > 0.7f) return CourtFaction.Merchants;
        if (p.Loyalty > 0.7f) return CourtFaction.Loyalists;
        if (p.Mysticism > 0.5f) return CourtFaction.Faithful;
        return CourtFaction.Reformists;
    }

    private async Task ProposeMarriage(NPC candidate)
    {
        terminal.WriteLine("");
        terminal.SetColor("bright_magenta");
        terminal.WriteLine($"You send a royal proposal to {candidate.Name}...");
        await Task.Delay(1500);

        // Acceptance chance based on player reputation and NPC personality
        int acceptChance = 50 + (int)(currentPlayer.Chivalry / 10) + (currentPlayer.Level - candidate.Level) * 2;

        if (candidate.Brain?.Personality?.Ambition > 0.5f)
            acceptChance += 20;  // Ambitious NPCs want royal power

        acceptChance = Math.Clamp(acceptChance, 20, 95);

        if (random.Next(100) < acceptChance)
        {
            // Marriage accepted!
            long dowry = (candidate.Level * 1000) + (candidate.Charisma * 100);
            var faction = DetermineFactionForNPC(candidate);

            currentKing.Spouse = new RoyalSpouse
            {
                Name = candidate.Name,
                Sex = candidate.Sex,
                OriginalFaction = faction,
                Dowry = dowry,
                MarriageDate = DateTime.Now,
                Happiness = 70 + random.Next(30)
            };

            currentKing.Treasury += dowry;

            // Register the actual marriage on both characters
            currentPlayer.Married = true;
            currentPlayer.IsMarried = true;
            currentPlayer.SpouseName = candidate.Name;
            currentPlayer.MarriedTimes++;

            candidate.Married = true;
            candidate.IsMarried = true;
            candidate.SpouseName = currentPlayer.Name;
            candidate.MarriedTimes++;

            // Register with marriage registry and romance tracker
            NPCMarriageRegistry.Instance.RegisterMarriage(currentPlayer.ID, candidate.ID, currentPlayer.Name, candidate.Name);
            RomanceTracker.Instance?.AddSpouse(candidate.ID);

            // Create/update the relationship record to married
            var relation = RelationshipSystem.GetOrCreateRelationship(currentPlayer, candidate);
            if (relation != null)
            {
                relation.Relation1 = GameConfig.RelationMarried;
                relation.Relation2 = GameConfig.RelationMarried;
                relation.MarriedDays = 0;
                relation.MarriedTimes++;
                RelationshipSystem.SaveRelationship(relation);
            }

            // Boost loyalty of that faction
            foreach (var member in currentKing.CourtMembers.Where(m => m.Faction == faction))
            {
                member.LoyaltyToKing = Math.Min(100, member.LoyaltyToKing + 20);
            }

            terminal.SetColor("bright_green");
            terminal.WriteLine($"{candidate.Name} accepts your proposal!");
            terminal.WriteLine("");
            terminal.SetColor("yellow");
            terminal.WriteLine($"Dowry received: {dowry:N0} gold!");
            terminal.WriteLine($"The {faction} faction's loyalty has increased!");
            terminal.WriteLine("");

            NewsSystem.Instance?.Newsy(true,
                $"ROYAL WEDDING! {currentKing.GetTitle()} {currentKing.Name} has married {candidate.Name}!");
            NewsSystem.Instance?.WriteMarriageNews(currentPlayer.Name, candidate.Name, "Castle");

            DebugLogger.Instance.LogMarriage(currentPlayer.Name, candidate.Name);
        }
        else
        {
            terminal.SetColor("red");
            terminal.WriteLine($"{candidate.Name} politely declines your proposal.");
            terminal.WriteLine("Perhaps try again when you are more renowned...");
        }
    }

    private async Task GiftToSpouse()
    {
        terminal.SetColor("cyan");
        terminal.Write("Gift amount (gold): ");
        terminal.SetColor("white");
        string input = await terminal.ReadLineAsync();

        if (long.TryParse(input, out long amount) && amount > 0)
        {
            if (amount > currentKing.Treasury)
            {
                terminal.SetColor("red");
                terminal.WriteLine("Insufficient funds!");
                return;
            }

            currentKing.Treasury -= amount;
            int happinessBoost = (int)Math.Min(30, amount / 500);
            currentKing.Spouse!.Happiness = Math.Min(100, currentKing.Spouse.Happiness + happinessBoost);

            terminal.SetColor("bright_green");
            terminal.WriteLine($"Your spouse is pleased! Happiness +{happinessBoost}%");
        }
    }

    private async Task DivorceSpouse()
    {
        terminal.SetColor("bright_red");
        terminal.WriteLine("WARNING: Divorce will have serious political consequences!");
        terminal.WriteLine($"The {currentKing.Spouse!.OriginalFaction} faction will turn against you.");
        terminal.WriteLine("");

        terminal.SetColor("yellow");
        terminal.Write("Are you sure you want to divorce? (Y/N): ");
        terminal.SetColor("white");
        string confirm = await terminal.ReadLineAsync();

        if (confirm?.ToUpper() == "Y")
        {
            var faction = currentKing.Spouse.OriginalFaction;
            var spouseName = currentKing.Spouse.Name;

            // Severe loyalty penalty to that faction
            foreach (var member in currentKing.CourtMembers.Where(m => m.Faction == faction))
            {
                member.LoyaltyToKing = Math.Max(0, member.LoyaltyToKing - 40);
            }

            // Clear marriage state on player
            currentPlayer.Married = false;
            currentPlayer.IsMarried = false;
            currentPlayer.SpouseName = "";

            // Clear marriage state on the NPC spouse
            var spouseNPC = NPCSpawnSystem.Instance?.ActiveNPCs?.FirstOrDefault(n => n.Name == spouseName);
            if (spouseNPC != null)
            {
                spouseNPC.Married = false;
                spouseNPC.IsMarried = false;
                spouseNPC.SpouseName = "";
            }

            // Clean up registries
            NPCMarriageRegistry.Instance.EndMarriage(currentPlayer.ID);
            if (spouseNPC != null)
                RomanceTracker.Instance?.Divorce(spouseNPC.ID, "Royal divorce", playerInitiated: true);

            terminal.SetColor("red");
            terminal.WriteLine($"You have divorced {spouseName}.");
            terminal.WriteLine($"The {faction} faction is furious!");

            NewsSystem.Instance?.Newsy(true,
                $"SCANDAL! {currentKing.GetTitle()} {currentKing.Name} has divorced {spouseName}!");

            currentKing.Spouse = null;
        }
        else
        {
            terminal.SetColor("gray");
            terminal.WriteLine("Divorce cancelled.");
        }
    }

    // ═══════════════════════════════════════════════════════════════════════════
    // COURT POLITICS
    // ═══════════════════════════════════════════════════════════════════════════

    private async Task ViewCourtPolitics()
    {
        terminal.ClearScreen();
        terminal.SetColor("bright_cyan");
        terminal.WriteLine("╔══════════════════════════════════════════════════════════════════════════════╗");
        terminal.WriteLine("║                         ROYAL COURT                                          ║");
        terminal.WriteLine("╚══════════════════════════════════════════════════════════════════════════════╝");
        terminal.WriteLine("");

        // Show court members
        terminal.SetColor("yellow");
        terminal.WriteLine("COURT MEMBERS:");
        terminal.WriteLine("");

        if (currentKing.CourtMembers.Count == 0)
        {
            terminal.SetColor("gray");
            terminal.WriteLine("The court has not yet been established.");
            terminal.WriteLine("(Court members will appear as the kingdom develops)");
        }
        else
        {
            terminal.SetColor("white");
            terminal.WriteLine($"{"Role",-18} {"Name",-25} {"Faction",-15} {"Loyalty",-10} {"Influence"}");
            terminal.SetColor("darkgray");
            terminal.WriteLine(new string('─', 78));

            foreach (var member in currentKing.CourtMembers)
            {
                string loyaltyColor = member.LoyaltyToKing >= 70 ? "bright_green" :
                                      member.LoyaltyToKing >= 40 ? "yellow" : "red";
                string plottingMark = member.IsPlotting ? " *" : "";

                terminal.SetColor("white");
                terminal.Write($"{member.Role,-18} ");
                terminal.SetColor("cyan");
                terminal.Write($"{member.Name,-25} ");
                terminal.SetColor("gray");
                terminal.Write($"{member.Faction,-15} ");
                terminal.SetColor(loyaltyColor);
                terminal.Write($"{member.LoyaltyToKing}%{plottingMark,-7} ");
                terminal.SetColor("white");
                terminal.WriteLine($"{member.Influence}");
            }

            if (currentKing.CourtMembers.Any(m => m.IsPlotting))
            {
                terminal.SetColor("red");
                terminal.WriteLine("");
                terminal.WriteLine("* Some courtiers seem to be scheming...");
            }
        }

        terminal.WriteLine("");

        // Show active plots (if detected)
        terminal.SetColor("yellow");
        terminal.WriteLine("INTELLIGENCE REPORTS:");
        terminal.WriteLine("");

        var discoveredPlots = currentKing.ActivePlots.Where(p => p.IsDiscovered).ToList();
        if (discoveredPlots.Count > 0)
        {
            foreach (var plot in discoveredPlots)
            {
                terminal.SetColor("red");
                terminal.WriteLine($"FOILED: {plot.PlotType} plot by {string.Join(", ", plot.Conspirators)}");
            }
        }

        // Hint about undiscovered plots
        var secretPlots = currentKing.ActivePlots.Where(p => !p.IsDiscovered).ToList();
        if (secretPlots.Count > 0)
        {
            terminal.SetColor("gray");
            terminal.WriteLine($"Your spies report {secretPlots.Count} rumor(s) of intrigue...");
            terminal.WriteLine("Use the Court Magician's 'Detect Threats' spell to investigate.");
        }
        else if (discoveredPlots.Count == 0)
        {
            terminal.SetColor("green");
            terminal.WriteLine("No plots detected. The court appears stable.");
        }

        terminal.WriteLine("");

        // Show faction standing
        terminal.SetColor("yellow");
        terminal.WriteLine("FACTION RELATIONS:");
        terminal.WriteLine("");

        var factionGroups = currentKing.CourtMembers
            .GroupBy(m => m.Faction)
            .Where(g => g.Key != CourtFaction.None);

        foreach (var group in factionGroups)
        {
            int avgLoyalty = (int)group.Average(m => m.LoyaltyToKing);
            string status = avgLoyalty >= 70 ? "Loyal" :
                           avgLoyalty >= 40 ? "Neutral" : "Hostile";
            string color = avgLoyalty >= 70 ? "bright_green" :
                          avgLoyalty >= 40 ? "yellow" : "red";

            terminal.SetColor("white");
            terminal.Write($"  {group.Key,-15}: ");
            terminal.SetColor(color);
            terminal.WriteLine($"{status} ({avgLoyalty}%)");
        }

        terminal.WriteLine("");

        // Interactive menu — only for the king
        if (currentPlayer.King && currentKing.CourtMembers.Count > 0)
        {
            bool courtLoop = true;
            while (courtLoop)
            {
                terminal.SetColor("cyan");
                terminal.WriteLine("[D]ismiss  [A]rrest Plotter  [B]ribe  [P]romote  [Q]uit");
                terminal.SetColor("white");
                terminal.Write("Court action: ");
                string courtChoice = (await terminal.ReadLineAsync())?.ToUpper() ?? "";

                switch (courtChoice)
                {
                    case "D":
                        await CourtAction_Dismiss();
                        break;
                    case "A":
                        await CourtAction_Arrest();
                        break;
                    case "B":
                        await CourtAction_Bribe();
                        break;
                    case "P":
                        await CourtAction_Promote();
                        break;
                    default:
                        courtLoop = false;
                        break;
                }
            }
        }
        else
        {
            await terminal.PressAnyKey();
        }
    }

    private async Task CourtAction_Dismiss()
    {
        terminal.SetColor("yellow");
        terminal.WriteLine("");
        terminal.WriteLine("Dismiss which court member?");
        for (int i = 0; i < currentKing.CourtMembers.Count; i++)
        {
            var m = currentKing.CourtMembers[i];
            terminal.SetColor("white");
            terminal.WriteLine($"  {i + 1}. {m.Name} ({m.Role}, {m.Faction})");
        }
        terminal.SetColor("cyan");
        terminal.Write("Number (0 to cancel): ");
        string input = await terminal.ReadLineAsync();
        if (int.TryParse(input, out int idx) && idx > 0 && idx <= currentKing.CourtMembers.Count)
        {
            var member = currentKing.CourtMembers[idx - 1];
            currentKing.CourtMembers.RemoveAt(idx - 1);

            // Faction loyalty hit
            foreach (var cm in currentKing.CourtMembers.Where(c => c.Faction == member.Faction))
                cm.LoyaltyToKing = Math.Max(0, cm.LoyaltyToKing - GameConfig.DismissLoyaltyCost);

            // Make the NPC hostile
            var npc = NPCSpawnSystem.Instance?.GetNPCByName(member.Name);
            if (npc?.Brain != null)
                npc.Brain.Memory.CharacterImpressions[currentPlayer.DisplayName] = -0.5f;

            terminal.SetColor("red");
            terminal.WriteLine($"{member.Name} has been dismissed from the court!");
            NewsSystem.Instance?.Newsy(false, $"{member.Name} has been dismissed from the royal court by {currentKing.GetTitle()} {currentKing.Name}.");
        }
        terminal.WriteLine("");
    }

    private async Task CourtAction_Arrest()
    {
        var discoveredPlots = currentKing.ActivePlots.Where(p => p.IsDiscovered).ToList();
        if (discoveredPlots.Count == 0)
        {
            terminal.SetColor("gray");
            terminal.WriteLine("No discovered plots. Use the Court Magician's 'Detect Threats' to find plotters.");
            terminal.WriteLine("");
            return;
        }

        terminal.SetColor("yellow");
        terminal.WriteLine("");
        terminal.WriteLine("Arrest plotters from which conspiracy?");
        for (int i = 0; i < discoveredPlots.Count; i++)
        {
            var plot = discoveredPlots[i];
            terminal.SetColor("red");
            terminal.WriteLine($"  {i + 1}. {plot.PlotType} by {string.Join(", ", plot.Conspirators)} (Trial cost: {GameConfig.ArrestTrialCost:N0}g)");
        }
        terminal.SetColor("cyan");
        terminal.Write("Number (0 to cancel): ");
        string input = await terminal.ReadLineAsync();
        if (int.TryParse(input, out int idx) && idx > 0 && idx <= discoveredPlots.Count)
        {
            if (currentKing.Treasury < GameConfig.ArrestTrialCost)
            {
                terminal.SetColor("red");
                terminal.WriteLine("Insufficient treasury funds for the trial!");
                terminal.WriteLine("");
                return;
            }

            var plot = discoveredPlots[idx - 1];
            currentKing.Treasury -= GameConfig.ArrestTrialCost;

            // Arrest each conspirator
            foreach (var conspirator in plot.Conspirators)
            {
                // Remove from court
                var courtMember = currentKing.CourtMembers.FirstOrDefault(c => c.Name == conspirator);
                if (courtMember != null)
                {
                    // Faction loyalty hit
                    foreach (var cm in currentKing.CourtMembers.Where(c => c.Faction == courtMember.Faction))
                        cm.LoyaltyToKing = Math.Max(0, cm.LoyaltyToKing - GameConfig.ArrestFactionLoyaltyCost);
                    currentKing.CourtMembers.Remove(courtMember);
                }

                // Imprison
                var npc = NPCSpawnSystem.Instance?.GetNPCByName(conspirator);
                if (npc != null)
                    currentKing.ImprisonCharacter(conspirator, 14, $"{plot.PlotType} conspiracy");
            }

            // Remove the plot
            currentKing.ActivePlots.Remove(plot);

            terminal.SetColor("bright_green");
            terminal.WriteLine($"The conspirators have been arrested and imprisoned! (-{GameConfig.ArrestTrialCost:N0}g)");
            NewsSystem.Instance?.Newsy(true, $"{currentKing.GetTitle()} {currentKing.Name} has crushed a {plot.PlotType} conspiracy!");
        }
        terminal.WriteLine("");
    }

    private async Task CourtAction_Bribe()
    {
        terminal.SetColor("yellow");
        terminal.WriteLine("");
        terminal.WriteLine("Bribe which court member to increase loyalty?");
        for (int i = 0; i < currentKing.CourtMembers.Count; i++)
        {
            var m = currentKing.CourtMembers[i];
            long cost = GameConfig.BribeBaseCost + (100 - m.LoyaltyToKing) * 50;
            string loyColor = m.LoyaltyToKing >= 70 ? "bright_green" : m.LoyaltyToKing >= 40 ? "yellow" : "red";
            terminal.SetColor("white");
            terminal.Write($"  {i + 1}. {m.Name,-20} ");
            terminal.SetColor(loyColor);
            terminal.Write($"Loyalty: {m.LoyaltyToKing}% ");
            terminal.SetColor("gray");
            terminal.WriteLine($"(Cost: {cost:N0}g)");
        }
        terminal.SetColor("cyan");
        terminal.Write("Number (0 to cancel): ");
        string input = await terminal.ReadLineAsync();
        if (int.TryParse(input, out int idx) && idx > 0 && idx <= currentKing.CourtMembers.Count)
        {
            var member = currentKing.CourtMembers[idx - 1];
            long cost = GameConfig.BribeBaseCost + (100 - member.LoyaltyToKing) * 50;

            if (currentKing.Treasury < cost)
            {
                terminal.SetColor("red");
                terminal.WriteLine("Insufficient treasury funds!");
                terminal.WriteLine("");
                return;
            }

            currentKing.Treasury -= cost;
            var random = new Random();
            int loyaltyGain = 15 + random.Next(11); // 15-25
            member.LoyaltyToKing = Math.Min(100, member.LoyaltyToKing + loyaltyGain);

            // If plotting and loyalty now above 60, abandon plot
            if (member.IsPlotting && member.LoyaltyToKing > 60)
            {
                member.IsPlotting = false;
                var plotToRemove = currentKing.ActivePlots.FirstOrDefault(p => p.Conspirators.Contains(member.Name));
                if (plotToRemove != null)
                {
                    plotToRemove.Conspirators.Remove(member.Name);
                    if (plotToRemove.Conspirators.Count == 0)
                        currentKing.ActivePlots.Remove(plotToRemove);
                }
                terminal.SetColor("bright_green");
                terminal.WriteLine($"{member.Name} has abandoned their scheming! (+{loyaltyGain} loyalty, -{cost:N0}g)");
            }
            else
            {
                terminal.SetColor("green");
                terminal.WriteLine($"{member.Name}'s loyalty increased to {member.LoyaltyToKing}%. (+{loyaltyGain} loyalty, -{cost:N0}g)");
            }
        }
        terminal.WriteLine("");
    }

    private async Task CourtAction_Promote()
    {
        terminal.SetColor("yellow");
        terminal.WriteLine("");
        terminal.WriteLine($"Promote which court member? (Cost: {GameConfig.PromoteCost:N0}g from treasury)");
        for (int i = 0; i < currentKing.CourtMembers.Count; i++)
        {
            var m = currentKing.CourtMembers[i];
            terminal.SetColor("white");
            terminal.WriteLine($"  {i + 1}. {m.Name} ({m.Role}, Influence: {m.Influence})");
        }
        terminal.SetColor("cyan");
        terminal.Write("Number (0 to cancel): ");
        string input = await terminal.ReadLineAsync();
        if (int.TryParse(input, out int idx) && idx > 0 && idx <= currentKing.CourtMembers.Count)
        {
            if (currentKing.Treasury < GameConfig.PromoteCost)
            {
                terminal.SetColor("red");
                terminal.WriteLine("Insufficient treasury funds!");
                terminal.WriteLine("");
                return;
            }

            var member = currentKing.CourtMembers[idx - 1];
            currentKing.Treasury -= GameConfig.PromoteCost;
            member.LoyaltyToKing = Math.Min(100, member.LoyaltyToKing + GameConfig.PromoteLoyaltyGain);
            member.Influence = Math.Min(100, member.Influence + 5);

            terminal.SetColor("bright_green");
            terminal.WriteLine($"{member.Name} has been promoted! (+{GameConfig.PromoteLoyaltyGain} loyalty, +5 influence, -{GameConfig.PromoteCost:N0}g)");
            NewsSystem.Instance?.Newsy(false, $"{member.Name} has been promoted in the royal court by {currentKing.GetTitle()} {currentKing.Name}.");
        }
        terminal.WriteLine("");
    }

    // ═══════════════════════════════════════════════════════════════════════════
    // SUCCESSION MANAGEMENT
    // ═══════════════════════════════════════════════════════════════════════════

    private async Task ManageSuccession()
    {
        terminal.ClearScreen();
        terminal.SetColor("bright_green");
        terminal.WriteLine("╔══════════════════════════════════════════════════════════════════════════════╗");
        terminal.WriteLine("║                         ROYAL SUCCESSION                                     ║");
        terminal.WriteLine("╚══════════════════════════════════════════════════════════════════════════════╝");
        terminal.WriteLine("");

        // Show current heirs
        terminal.SetColor("yellow");
        terminal.WriteLine("HEIRS TO THE THRONE:");
        terminal.WriteLine("");

        if (currentKing.Heirs.Count == 0)
        {
            terminal.SetColor("gray");
            terminal.WriteLine("You have no heirs.");
            if (currentKing.Spouse == null)
            {
                terminal.WriteLine("Consider arranging a royal marriage to produce heirs.");
            }
            else
            {
                terminal.WriteLine("A child may be born to your marriage in time.");
            }
        }
        else
        {
            terminal.SetColor("white");
            terminal.WriteLine($"{"#",-3} {"Name",-20} {"Age",-6} {"Sex",-8} {"Claim",-10} {"Status"}");
            terminal.SetColor("darkgray");
            terminal.WriteLine(new string('─', 60));

            int i = 1;
            foreach (var heir in currentKing.Heirs.OrderByDescending(h => h.ClaimStrength))
            {
                string status = heir.IsDesignated ? "DESIGNATED" :
                               heir.IsAdult ? "Adult" : "Minor";
                string statusColor = heir.IsDesignated ? "bright_green" :
                                    heir.IsAdult ? "white" : "gray";

                terminal.SetColor("white");
                terminal.Write($"{i,-3} {heir.Name,-20} {heir.Age,-6} {heir.Sex,-8} {heir.ClaimStrength}%     ");
                terminal.SetColor(statusColor);
                terminal.WriteLine(status);
                i++;
            }
        }

        terminal.WriteLine("");

        // Show designated heir
        if (!string.IsNullOrEmpty(currentKing.DesignatedHeir))
        {
            terminal.SetColor("bright_green");
            terminal.WriteLine($"Current designated heir: {currentKing.DesignatedHeir}");
        }
        else
        {
            terminal.SetColor("yellow");
            terminal.WriteLine("No heir has been officially designated.");
        }

        terminal.WriteLine("");

        // Options
        terminal.SetColor("cyan");
        terminal.WriteLine("Options:");
        terminal.Write(" [");
        terminal.SetColor("bright_yellow");
        terminal.Write("D");
        terminal.SetColor("cyan");
        terminal.WriteLine("]esignate an heir");
        terminal.Write(" [");
        terminal.SetColor("bright_yellow");
        terminal.Write("N");
        terminal.SetColor("cyan");
        terminal.WriteLine("]ame a new heir (adopt/legitimize)");
        terminal.Write(" [");
        terminal.SetColor("bright_yellow");
        terminal.Write("R");
        terminal.SetColor("cyan");
        terminal.WriteLine("]eturn");
        terminal.WriteLine("");

        terminal.Write("Your choice: ");
        terminal.SetColor("white");
        string choice = await terminal.ReadLineAsync();

        switch (choice?.ToUpper())
        {
            case "D":
                await DesignateHeir();
                break;
            case "N":
                await CreateNewHeir();
                break;
        }
    }

    private async Task DesignateHeir()
    {
        if (currentKing.Heirs.Count == 0)
        {
            terminal.SetColor("red");
            terminal.WriteLine("You have no heirs to designate!");
            await Task.Delay(2000);
            return;
        }

        terminal.SetColor("cyan");
        terminal.Write("Designate which heir? (number): ");
        terminal.SetColor("white");
        string input = await terminal.ReadLineAsync();

        var sortedHeirs = currentKing.Heirs.OrderByDescending(h => h.ClaimStrength).ToList();

        if (int.TryParse(input, out int selection) && selection >= 1 && selection <= sortedHeirs.Count)
        {
            var heir = sortedHeirs[selection - 1];

            // Clear old designation
            foreach (var h in currentKing.Heirs)
            {
                h.IsDesignated = false;
            }

            heir.IsDesignated = true;
            heir.ClaimStrength = Math.Min(100, heir.ClaimStrength + 20);
            currentKing.DesignatedHeir = heir.Name;

            terminal.SetColor("bright_green");
            terminal.WriteLine($"{heir.Name} has been officially designated as your heir!");
            terminal.WriteLine("Their claim to the throne has been strengthened.");

            NewsSystem.Instance?.Newsy(false,
                $"{currentKing.GetTitle()} {currentKing.Name} has named {heir.Name} as the royal heir!");
        }

        await Task.Delay(2500);
    }

    private async Task CreateNewHeir()
    {
        terminal.SetColor("yellow");
        terminal.WriteLine("You may legitimize a bastard child or adopt a ward as heir.");
        terminal.WriteLine("This costs 5,000 gold and reduces claim strength of existing heirs.");
        terminal.WriteLine("");

        if (currentKing.Treasury < 5000)
        {
            terminal.SetColor("red");
            terminal.WriteLine("Insufficient funds! (Need 5,000 gold)");
            await Task.Delay(2000);
            return;
        }

        terminal.SetColor("cyan");
        terminal.Write("Name of the new heir: ");
        terminal.SetColor("white");
        string name = await terminal.ReadLineAsync();

        if (string.IsNullOrWhiteSpace(name))
        {
            return;
        }

        // Create new heir
        var newHeir = new RoyalHeir
        {
            Name = name,
            Age = 12 + random.Next(10),  // 12-21 years old
            ClaimStrength = 30 + random.Next(20),  // Lower initial claim
            ParentName = currentKing.Name,
            Sex = random.Next(2) == 0 ? CharacterSex.Male : CharacterSex.Female,
            BirthDate = DateTime.Now.AddYears(-12 - random.Next(10))
        };

        currentKing.Heirs.Add(newHeir);
        currentKing.Treasury -= 5000;

        // Reduce other heirs' claims slightly (jealousy)
        foreach (var heir in currentKing.Heirs.Where(h => h.Name != name))
        {
            heir.ClaimStrength = Math.Max(10, heir.ClaimStrength - 10);
        }

        terminal.SetColor("bright_green");
        terminal.WriteLine($"{name} has been added to the line of succession!");

        NewsSystem.Instance?.Newsy(false,
            $"{currentKing.GetTitle()} {currentKing.Name} has added {name} to the royal succession!");

        await Task.Delay(2500);
    }

    // ═══════════════════════════════════════════════════════════════════════════
    // ROYAL BODYGUARDS (Dungeon Mercenaries)
    // ═══════════════════════════════════════════════════════════════════════════

    private static readonly Dictionary<string, string[]> MercenaryNames = new()
    {
        ["Tank"] = new[] { "Sir Harken", "Dame Ironheart", "Ulric the Wall", "Brenna Shieldmaiden" },
        ["Healer"] = new[] { "Brother Cedric", "Sister Alara", "Wynne the Mender", "Father Osric" },
        ["DPS"] = new[] { "Kael Swiftblade", "Sera the Hawk", "Grimjaw the Sharp", "Nyx Shadowstrike" },
        ["Support"] = new[] { "Theron the Stalwart", "Lady Maren", "Jorund Brightshield", "Elara the Wise" }
    };

    private RoyalMercenary GenerateMercenary(string role, int level)
    {
        var random = new Random();

        // Pick a name not already in use
        var existingNames = currentPlayer.RoyalMercenaries.Select(m => m.Name).ToHashSet();
        var namePool = MercenaryNames[role].Where(n => !existingNames.Contains(n)).ToArray();
        string name = namePool.Length > 0
            ? namePool[random.Next(namePool.Length)]
            : $"{MercenaryNames[role][0]} II"; // Fallback if all names used

        var merc = new RoyalMercenary
        {
            Name = name,
            Role = role,
            Level = level,
            Sex = random.Next(2) == 0 ? CharacterSex.Male : CharacterSex.Female
        };

        // Stats scale with level — includes built-in gear (WeapPow/ArmPow represent equipment)
        // Quadratic HP component ensures bodyguards scale with high-level monster damage
        // At level 10: +3 HP (negligible). At level 50: +83 HP. At level 90: +270 HP.
        int quadHP = (level * level) / 30;
        switch (role)
        {
            case "Tank":
                merc.Class = CharacterClass.Warrior;
                merc.HP = merc.MaxHP = 120 + level * 45 + quadHP * 2;
                merc.Strength = 12 + level * 4;
                merc.Defence = 12 + level * 5;
                merc.WeapPow = 8 + level * 3;
                merc.ArmPow = 10 + level * 5;
                merc.Agility = 5 + level * 2;
                merc.Dexterity = 5 + level * 2;
                merc.Constitution = 10 + level * 4;
                merc.Wisdom = 3 + level;
                merc.Intelligence = 3 + level;
                merc.Healing = 3;
                break;

            case "Healer":
                merc.Class = CharacterClass.Cleric;
                merc.HP = merc.MaxHP = 80 + level * 30 + quadHP;
                merc.Mana = merc.MaxMana = 50 + level * 10;
                merc.Strength = 5 + level * 2;
                merc.Defence = 8 + level * 3;
                merc.WeapPow = 3 + level * 2;
                merc.ArmPow = 6 + level * 4;
                merc.Agility = 4 + level * 2;
                merc.Dexterity = 4 + level * 2;
                merc.Constitution = 6 + level * 2;
                merc.Wisdom = 10 + level * 5;
                merc.Intelligence = 8 + level * 4;
                merc.Healing = 5;
                break;

            case "DPS":
                merc.Class = CharacterClass.Ranger;
                merc.HP = merc.MaxHP = 100 + level * 35 + quadHP;
                merc.Strength = 14 + level * 5;
                merc.Defence = 6 + level * 3;
                merc.WeapPow = 10 + level * 4;
                merc.ArmPow = 4 + level * 3;
                merc.Agility = 10 + level * 4;
                merc.Dexterity = 10 + level * 4;
                merc.Constitution = 6 + level * 2;
                merc.Wisdom = 3 + level;
                merc.Intelligence = 3 + level;
                merc.Healing = 3;
                break;

            case "Support":
                merc.Class = CharacterClass.Paladin;
                merc.HP = merc.MaxHP = 100 + level * 35 + quadHP;
                merc.Mana = merc.MaxMana = 30 + level * 8;
                merc.Strength = 8 + level * 3;
                merc.Defence = 10 + level * 4;
                merc.WeapPow = 6 + level * 3;
                merc.ArmPow = 8 + level * 4;
                merc.Agility = 6 + level * 3;
                merc.Dexterity = 6 + level * 3;
                merc.Constitution = 8 + level * 3;
                merc.Wisdom = 8 + level * 3;
                merc.Intelligence = 6 + level * 2;
                merc.Healing = 4;
                break;
        }

        return merc;
    }

    private async Task ManageRoyalBodyguards()
    {
        while (true)
        {
            terminal.ClearScreen();
            terminal.SetColor("bright_cyan");
            terminal.WriteLine("╔══════════════════════════════════════════════════════════════════════════════╗");
            terminal.WriteLine("║                          ROYAL BODYGUARDS                                   ║");
            terminal.WriteLine("╚══════════════════════════════════════════════════════════════════════════════╝");
            terminal.WriteLine("");

            terminal.SetColor("gray");
            terminal.WriteLine("Hire elite mercenaries to accompany you in the dungeon.");
            terminal.WriteLine($"Maximum bodyguards: {GameConfig.MaxRoyalMercenaries}");
            terminal.WriteLine("");

            // Show current mercenaries
            if (currentPlayer.RoyalMercenaries.Count == 0)
            {
                terminal.SetColor("darkgray");
                terminal.WriteLine("You have no bodyguards in your employ.");
            }
            else
            {
                terminal.SetColor("bright_yellow");
                terminal.WriteLine("Current Bodyguards:");
                terminal.SetColor("gray");

                for (int i = 0; i < currentPlayer.RoyalMercenaries.Count; i++)
                {
                    var merc = currentPlayer.RoyalMercenaries[i];
                    string hpColor = merc.HP >= merc.MaxHP ? "bright_green" :
                                     merc.HP > merc.MaxHP / 2 ? "yellow" : "red";

                    terminal.SetColor("white");
                    terminal.Write($"  {i + 1}. {merc.Name}");
                    terminal.SetColor("gray");
                    terminal.Write($" - Level {merc.Level} {merc.Class} ({merc.Role})");
                    terminal.SetColor(hpColor);
                    terminal.Write($" - HP: {merc.HP}/{merc.MaxHP}");
                    if (merc.MaxMana > 0)
                    {
                        terminal.SetColor("bright_cyan");
                        terminal.Write($" MP: {merc.Mana}/{merc.MaxMana}");
                    }
                    terminal.WriteLine("");
                }
            }

            terminal.WriteLine("");

            long hireCost = GameConfig.MercenaryBaseCost + (currentPlayer.Level * GameConfig.MercenaryCostPerLevel);

            // Menu
            terminal.SetColor("darkgray");
            terminal.Write(" [");
            terminal.SetColor("bright_yellow");
            terminal.Write("H");
            terminal.SetColor("darkgray");
            terminal.Write("]");
            terminal.SetColor("white");
            terminal.Write($"ire Mercenary ({hireCost:N0}g)   ");

            terminal.SetColor("darkgray");
            terminal.Write("[");
            terminal.SetColor("bright_yellow");
            terminal.Write("D");
            terminal.SetColor("darkgray");
            terminal.Write("]");
            terminal.SetColor("white");
            terminal.Write("ismiss   ");

            terminal.SetColor("darkgray");
            terminal.Write("[");
            terminal.SetColor("bright_yellow");
            terminal.Write("R");
            terminal.SetColor("darkgray");
            terminal.Write("]");
            terminal.SetColor("white");
            terminal.WriteLine("eturn");
            terminal.WriteLine("");

            string input = await terminal.ReadLineAsync();
            if (string.IsNullOrEmpty(input)) continue;

            switch (char.ToUpper(input[0]))
            {
                case 'H':
                    await HireMercenary(hireCost);
                    break;
                case 'D':
                    await DismissMercenary();
                    break;
                case 'R':
                    return;
            }
        }
    }

    private async Task HireMercenary(long cost)
    {
        if (currentPlayer.RoyalMercenaries.Count >= GameConfig.MaxRoyalMercenaries)
        {
            terminal.SetColor("red");
            terminal.WriteLine($"You already have {GameConfig.MaxRoyalMercenaries} bodyguards — the maximum allowed.");
            await terminal.PressAnyKey();
            return;
        }

        if (currentPlayer.Gold < cost)
        {
            terminal.SetColor("red");
            terminal.WriteLine($"You need {cost:N0} gold to hire a mercenary. You have {currentPlayer.Gold:N0}g.");
            await terminal.PressAnyKey();
            return;
        }

        terminal.WriteLine("");
        terminal.SetColor("bright_yellow");
        terminal.WriteLine("Choose a mercenary role:");
        terminal.WriteLine("");

        terminal.SetColor("white");
        terminal.Write("  1. ");
        terminal.SetColor("bright_red");
        terminal.Write("Tank");
        terminal.SetColor("gray");
        terminal.WriteLine("     - Warrior. High HP and defense, draws enemy attacks.");

        terminal.SetColor("white");
        terminal.Write("  2. ");
        terminal.SetColor("bright_green");
        terminal.Write("Healer");
        terminal.SetColor("gray");
        terminal.WriteLine("   - Cleric. Heals wounded party members, casts holy spells.");

        terminal.SetColor("white");
        terminal.Write("  3. ");
        terminal.SetColor("bright_magenta");
        terminal.Write("DPS");
        terminal.SetColor("gray");
        terminal.WriteLine("      - Ranger. High damage output with fast attacks.");

        terminal.SetColor("white");
        terminal.Write("  4. ");
        terminal.SetColor("bright_cyan");
        terminal.Write("Support");
        terminal.SetColor("gray");
        terminal.WriteLine("  - Paladin. Balanced fighter with healing and combat spells.");

        terminal.WriteLine("");
        terminal.SetColor("white");
        terminal.Write("Choose (1-4, or R to cancel): ");
        string roleInput = await terminal.ReadLineAsync();

        string? role = roleInput?.Trim() switch
        {
            "1" => "Tank",
            "2" => "Healer",
            "3" => "DPS",
            "4" => "Support",
            _ => null
        };

        if (role == null) return;

        var merc = GenerateMercenary(role, currentPlayer.Level);

        currentPlayer.Gold -= cost;
        currentPlayer.RoyalMercenaries.Add(merc);

        terminal.WriteLine("");
        terminal.SetColor("bright_green");
        terminal.WriteLine($"{merc.Name} has been hired as your royal bodyguard!");
        terminal.SetColor("gray");
        terminal.WriteLine($"  Role: {merc.Role} ({merc.Class})  Level: {merc.Level}");
        terminal.WriteLine($"  HP: {merc.MaxHP}  STR: {merc.Strength}  DEF: {merc.Defence}");
        terminal.WriteLine($"  Weapon Power: {merc.WeapPow}  Armor Power: {merc.ArmPow}");
        if (merc.MaxMana > 0)
            terminal.WriteLine($"  Mana: {merc.MaxMana}  Potions: {merc.Healing}");
        terminal.SetColor("yellow");
        terminal.WriteLine($"  Cost: {cost:N0} gold. Remaining: {currentPlayer.Gold:N0}g");
        await terminal.PressAnyKey();
    }

    private async Task DismissMercenary()
    {
        if (currentPlayer.RoyalMercenaries.Count == 0)
        {
            terminal.SetColor("red");
            terminal.WriteLine("You have no bodyguards to dismiss.");
            await terminal.PressAnyKey();
            return;
        }

        terminal.WriteLine("");
        terminal.SetColor("yellow");
        terminal.WriteLine("Dismiss which bodyguard?");
        for (int i = 0; i < currentPlayer.RoyalMercenaries.Count; i++)
        {
            var merc = currentPlayer.RoyalMercenaries[i];
            terminal.SetColor("white");
            terminal.WriteLine($"  {i + 1}. {merc.Name} (Level {merc.Level} {merc.Role})");
        }
        terminal.SetColor("gray");
        terminal.Write("Choose (or R to cancel): ");
        string dismissInput = await terminal.ReadLineAsync();

        if (int.TryParse(dismissInput, out int idx) && idx >= 1 && idx <= currentPlayer.RoyalMercenaries.Count)
        {
            var dismissed = currentPlayer.RoyalMercenaries[idx - 1];
            currentPlayer.RoyalMercenaries.RemoveAt(idx - 1);

            terminal.SetColor("yellow");
            terminal.WriteLine($"{dismissed.Name} has been dismissed from your service.");
            await terminal.PressAnyKey();
        }
    }

    // ═══════════════════════════════════════════════════════════════════════════
    // ROYAL QUESTS
    // ═══════════════════════════════════════════════════════════════════════════

    private async Task RoyalQuests()
    {
        terminal.ClearScreen();
        terminal.SetColor("bright_red");
        terminal.WriteLine("╔══════════════════════════════════════════════════════════════════════════════╗");
        terminal.WriteLine("║                         ROYAL BOUNTY BOARD                                  ║");
        terminal.WriteLine("╚══════════════════════════════════════════════════════════════════════════════╝");
        terminal.WriteLine("");

        terminal.SetColor("white");
        terminal.WriteLine("The King's personal bounty board displays wanted criminals and threats.");
        terminal.WriteLine("");

        // Get bounties posted by the King
        var bounties = QuestSystem.GetKingBounties();

        if (bounties.Count == 0)
        {
            terminal.SetColor("gray");
            terminal.WriteLine("No bounties have been posted by the Crown.");
            terminal.WriteLine("");
            terminal.SetColor("yellow");
            terminal.WriteLine("The realm is at peace... for now.");
        }
        else
        {
            terminal.SetColor("bright_red");
            terminal.WriteLine("WANTED:");
            terminal.SetColor("darkgray");
            terminal.WriteLine(new string('─', 60));

            foreach (var bounty in bounties)
            {
                terminal.SetColor("red");
                terminal.Write("  ▪ ");
                terminal.SetColor("white");
                terminal.WriteLine(bounty.Title ?? bounty.Comment ?? "Unknown Target");
                terminal.SetColor("yellow");
                terminal.WriteLine($"    Reward: {bounty.GetRewardDescription()}");
                terminal.SetColor("gray");
                terminal.WriteLine($"    {bounty.Comment}");
                terminal.WriteLine("");
            }
        }

        terminal.WriteLine("");
        terminal.SetColor("cyan");
        terminal.WriteLine("Visit the Quest Hall on Main Street to claim bounties.");
        terminal.WriteLine("");
        terminal.SetColor("darkgray");
        terminal.WriteLine("Press Enter to continue...");
        await terminal.ReadKeyAsync();
    }

    // ═══════════════════════════════════════════════════════════════════════════
    // THRONE MECHANICS
    // ═══════════════════════════════════════════════════════════════════════════

    private bool CanChallengeThrone()
    {
        if (currentPlayer.Level < GameConfig.MinLevelKing)
            return false;

        if (currentPlayer.King)
            return false;

        if (currentKing == null || !currentKing.IsActive)
            return false;

        // Don't let the player fight themselves (stale king data from before abdication)
        if (currentKing.Name == currentPlayer.DisplayName || currentKing.Name == currentPlayer.Name2)
            return false;

        // Level proximity check — challenger must not be too far below king's level (higher level always allowed)
        int kingLevel = GetKingLevel();
        if (kingLevel > 0 && kingLevel - currentPlayer.Level > GameConfig.KingChallengeLevelRange)
            return false;

        return true;
    }

    /// <summary>
    /// Get the current king's level by looking up their NPC or player data
    /// </summary>
    private int GetKingLevel()
    {
        if (currentKing == null || !currentKing.IsActive) return 0;

        // Check if king is an NPC
        var kingNpc = NPCSpawnSystem.Instance?.GetNPCByName(currentKing.Name);
        if (kingNpc != null) return kingNpc.Level;

        // Check if king is the current player
        if (currentPlayer != null &&
            (currentKing.Name == currentPlayer.DisplayName || currentKing.Name == currentPlayer.Name2))
            return currentPlayer.Level;

        // For offline player kings, try to load from database
        if (currentKing.AI == CharacterAI.Human && DoorMode.IsOnlineMode)
        {
            try
            {
                var backend = UsurperRemake.Systems.SaveSystem.Instance?.Backend as UsurperRemake.Systems.SqlSaveBackend;
                if (backend != null)
                {
                    var kingSave = backend.ReadGameData(currentKing.Name.ToLowerInvariant()).GetAwaiter().GetResult();
                    if (kingSave?.Player != null)
                        return kingSave.Player.Level;
                }
            }
            catch { /* Fall through */ }
        }

        // Fallback — estimate from reign length
        return Math.Max(20, 20 + (int)(currentKing.TotalReign / 5));
    }

    private async Task<bool> ChallengeThrone()
    {
        terminal.ClearScreen();
        terminal.SetColor("bright_red");
        terminal.WriteLine("╔══════════════════════════════════════════════════════════════════════════════╗");
        terminal.WriteLine("║                          THRONE CHALLENGE                                   ║");
        terminal.WriteLine("╚══════════════════════════════════════════════════════════════════════════════╝");
        terminal.WriteLine("");

        // RULE: King cannot be on a team - must leave team first
        if (!string.IsNullOrEmpty(currentPlayer.Team))
        {
            terminal.SetColor("yellow");
            terminal.WriteLine("To challenge for the throne, you must abandon your team.");
            terminal.WriteLine($"You are currently a member of '{currentPlayer.Team}'.");
            terminal.WriteLine("");
            terminal.SetColor("cyan");
            terminal.Write("Leave your team to pursue the crown? (Y/N): ");
            terminal.SetColor("white");
            string leaveConfirm = await terminal.ReadLineAsync();

            if (leaveConfirm?.ToUpper() != "Y")
            {
                terminal.SetColor("gray");
                terminal.WriteLine("You decide to remain loyal to your team.");
                await Task.Delay(2000);
                return false;
            }

            // Force leave team and clear dungeon party
            string oldTeam = currentPlayer.Team;
            CityControlSystem.Instance.ForceLeaveTeam(currentPlayer);
            GameEngine.Instance?.ClearDungeonParty(); // Clear NPC teammates from dungeon
            terminal.SetColor("yellow");
            terminal.WriteLine($"You have left '{oldTeam}' to pursue the crown.");
            terminal.WriteLine("");
        }

        terminal.SetColor("white");
        terminal.WriteLine("You have chosen to challenge for the throne!");
        terminal.WriteLine($"You must defeat {currentKing.GetTitle()} {currentKing.Name} in combat.");
        terminal.WriteLine("");

        // Show monster guard warning
        if (currentKing.MonsterGuards.Count > 0)
        {
            terminal.SetColor("bright_red");
            terminal.WriteLine($"WARNING: You must first defeat {currentKing.MonsterGuards.Count} Monster Guards!");
        }

        // Show NPC guard warning
        if (currentKing.Guards.Count > 0)
        {
            terminal.SetColor("yellow");
            terminal.WriteLine($"WARNING: You must also defeat {currentKing.Guards.Count} Royal Guards!");
        }

        if (currentKing.TotalGuardCount > 0)
        {
            terminal.WriteLine("");
        }

        terminal.SetColor("cyan");
        terminal.Write("Do you wish to proceed? (Y/N): ");
        terminal.SetColor("white");
        string confirm = await terminal.ReadLineAsync();

        if (confirm?.ToUpper() != "Y")
        {
            terminal.SetColor("gray");
            terminal.WriteLine("You reconsider and step back.");
            await Task.Delay(2000);
            return false;
        }

        long runningPlayerHP = currentPlayer.HP;

        // PHASE 1: Fight through monster guards first
        foreach (var monster in currentKing.MonsterGuards.ToList())
        {
            terminal.ClearScreen();
            terminal.SetColor("bright_red");
            terminal.WriteLine($"=== Fighting Monster Guard: {monster.Name} (Level {monster.Level}) ===");
            terminal.WriteLine("");

            long monsterHP = monster.HP;

            while (monsterHP > 0 && runningPlayerHP > 0)
            {
                // Player attacks
                long playerDamage = Math.Max(1, currentPlayer.Strength + currentPlayer.WeapPow - monster.Defence);
                playerDamage += random.Next(1, (int)Math.Max(2, currentPlayer.WeapPow / 3));
                monsterHP -= playerDamage;

                terminal.SetColor("bright_green");
                terminal.WriteLine($"You strike the {monster.Name} for {playerDamage} damage! (Monster HP: {Math.Max(0, monsterHP)})");

                if (monsterHP <= 0) break;

                // Monster attacks
                long monsterDamage = Math.Max(1, monster.Strength + monster.WeapPow - currentPlayer.Defence - currentPlayer.ArmPow);
                monsterDamage += random.Next(1, (int)Math.Max(2, monster.WeapPow / 3));
                runningPlayerHP -= monsterDamage;

                terminal.SetColor("red");
                terminal.WriteLine($"The {monster.Name} claws you for {monsterDamage} damage! (Your HP: {Math.Max(0, runningPlayerHP)})");

                await Task.Delay(300);
            }

            if (runningPlayerHP <= 0)
            {
                terminal.SetColor("red");
                terminal.WriteLine($"You were slain by the {monster.Name}!");
                currentPlayer.HP = 1;
                terminal.WriteLine("Your challenge has failed. You barely escape with your life.");
                await Task.Delay(2500);
                return false;
            }
            else
            {
                terminal.SetColor("bright_green");
                terminal.WriteLine($"You defeated the {monster.Name}!");
                currentKing.MonsterGuards.Remove(monster);
                NewsSystem.Instance?.Newsy(true, $"{currentPlayer.DisplayName} slew the monster guard {monster.Name}!");
            }

            await Task.Delay(1500);
        }

        // PHASE 2: Fight through NPC guards
        foreach (var guard in currentKing.Guards.ToList())
        {
            // Skip if this guard is the player - they don't fight themselves!
            if (guard.Name.Equals(currentPlayer.DisplayName, StringComparison.OrdinalIgnoreCase) ||
                guard.Name.Equals(currentPlayer.Name2, StringComparison.OrdinalIgnoreCase))
            {
                terminal.SetColor("yellow");
                terminal.WriteLine($"As a former guard, you slip past your own post...");
                terminal.WriteLine("Your betrayal of the crown is noted.");
                currentKing.Guards.Remove(guard);
                NewsSystem.Instance?.Newsy(true, $"Guard {guard.Name} has betrayed the crown to challenge the throne!");
                await Task.Delay(1500);
                continue;
            }

            terminal.ClearScreen();
            terminal.SetColor("bright_red");
            terminal.WriteLine($"=== Fighting Royal Guard: {guard.Name} ===");
            terminal.WriteLine("");

            // Look up actual NPC stats if available, otherwise scale fallback with king level
            var actualNpc = NPCSpawnSystem.Instance?.GetNPCByName(guard.Name);
            int estKingLevel = currentKing != null ? Math.Max(20, (int)(currentKing.TotalReign / 5) + 20) : 20;
            long guardStr = actualNpc?.Strength ?? (30 + estKingLevel * 5);
            long guardHP = actualNpc?.HP ?? (100 + estKingLevel * 30);
            long guardDef = actualNpc?.Defence ?? (10 + estKingLevel * 3);
            int guardLevel = actualNpc?.Level ?? Math.Max(10, estKingLevel - 5);

            // Apply loyalty modifier to guard effectiveness
            float loyaltyMod = guard.Loyalty / 100f;
            guardStr = (long)(guardStr * loyaltyMod);

            // Low loyalty guards might flee before combat
            if (guard.Loyalty < 30 && random.Next(100) < 30)
            {
                terminal.SetColor("yellow");
                terminal.WriteLine($"{guard.Name} sees your strength and flees from combat!");
                currentKing.Guards.Remove(guard);
                NewsSystem.Instance?.Newsy(false, $"Cowardly guard {guard.Name} fled from {currentPlayer.DisplayName}!");
                await Task.Delay(1500);
                continue;
            }

            if (actualNpc != null)
            {
                terminal.SetColor("gray");
                terminal.WriteLine($"Level {guardLevel} | STR: {guardStr} | DEF: {guardDef} | Loyalty: {guard.Loyalty}%");
                terminal.WriteLine("");
            }

            while (guardHP > 0 && runningPlayerHP > 0)
            {
                // Player attacks
                long playerDamage = Math.Max(1, currentPlayer.Strength + currentPlayer.WeapPow - guardDef);
                guardHP -= playerDamage;

                terminal.SetColor("bright_green");
                terminal.WriteLine($"You strike {guard.Name} for {playerDamage} damage! (Guard HP: {Math.Max(0, guardHP)})");

                if (guardHP <= 0) break;

                // Guard attacks - effectiveness reduced by low loyalty
                long guardDamage = Math.Max(1, guardStr - currentPlayer.Defence);
                runningPlayerHP -= guardDamage;

                terminal.SetColor("red");
                terminal.WriteLine($"{guard.Name} strikes you for {guardDamage} damage! (Your HP: {Math.Max(0, runningPlayerHP)})");

                await Task.Delay(300);
            }

            if (runningPlayerHP <= 0)
            {
                terminal.SetColor("red");
                terminal.WriteLine($"You were defeated by {guard.Name}!");
                currentPlayer.HP = 1;
                terminal.WriteLine("Your challenge has failed. You barely escape with your life.");
                await Task.Delay(2500);
                return false;
            }
            else
            {
                terminal.SetColor("bright_green");
                terminal.WriteLine($"You defeated {guard.Name}!");
                currentKing.Guards.Remove(guard);
                NewsSystem.Instance?.Newsy(true, $"{currentPlayer.DisplayName} defeated guard {guard.Name}!");
            }

            await Task.Delay(1500);
        }

        // Update player HP from the guard battles
        currentPlayer.HP = runningPlayerHP;

        // Fight the king — use real stats with defender bonus
        terminal.ClearScreen();
        terminal.SetColor("bright_red");
        terminal.WriteLine($"=== Final Battle: {currentKing.GetTitle()} {currentKing.Name} ===");
        terminal.WriteLine("");

        // Look up real king stats: NPC king, player king (offline), or scaled fallback
        long kingStr, kingDef, kingWeapPow, kingArmPow;
        long kingHP;
        int kingLevel;
        var kingNpc = NPCSpawnSystem.Instance?.GetNPCByName(currentKing.Name);

        if (kingNpc != null)
        {
            // NPC King — use actual stats + defender bonus
            kingStr = kingNpc.Strength;
            kingDef = (long)(kingNpc.Defence * GameConfig.KingDefenderDefBonus);
            kingWeapPow = kingNpc.WeapPow;
            kingArmPow = kingNpc.ArmPow;
            kingHP = (long)(kingNpc.MaxHP * GameConfig.KingDefenderHPBonus);
            kingLevel = kingNpc.Level;
        }
        else if (currentKing.AI == CharacterAI.Human && DoorMode.IsOnlineMode)
        {
            // Player King (offline) — try loading from database
            long pStr = 100, pDef = 50, pWP = 50, pAP = 30, pHP = 500;
            int pLevel = 20;
            try
            {
                var backend = SaveSystem.Instance?.Backend as SqlSaveBackend;
                if (backend != null)
                {
                    var kingSave = await backend.ReadGameData(currentKing.Name.ToLowerInvariant());
                    if (kingSave?.Player != null)
                    {
                        pStr = kingSave.Player.Strength;
                        pDef = kingSave.Player.Defence;
                        pWP = kingSave.Player.WeapPow;
                        pAP = kingSave.Player.ArmPow;
                        pHP = kingSave.Player.MaxHP > 0 ? kingSave.Player.MaxHP : kingSave.Player.HP;
                        pLevel = kingSave.Player.Level;
                    }
                }
            }
            catch { /* Fall through to values above */ }

            // Apply defender bonus
            kingStr = pStr;
            kingDef = (long)(pDef * GameConfig.KingDefenderDefBonus);
            kingWeapPow = pWP;
            kingArmPow = pAP;
            kingHP = (long)(pHP * GameConfig.KingDefenderHPBonus);
            kingLevel = pLevel;
        }
        else
        {
            // Fallback — scale with estimated king level
            kingLevel = Math.Max(20, currentKing.TotalReign > 0 ? 20 + (int)(currentKing.TotalReign / 5) : 20);
            kingStr = 50 + kingLevel * 8;
            kingDef = (long)((10 + kingLevel * 4) * GameConfig.KingDefenderDefBonus);
            kingWeapPow = 20 + kingLevel * 3;
            kingArmPow = 10 + kingLevel * 2;
            kingHP = (long)((200 + kingLevel * 40) * GameConfig.KingDefenderHPBonus);
        }

        terminal.SetColor("gray");
        terminal.WriteLine($"Level {kingLevel} | STR: {kingStr} | DEF: {kingDef} | HP: {kingHP}");
        terminal.WriteLine("");

        long finalPlayerHP = currentPlayer.HP;

        int round = 0;
        while (kingHP > 0 && finalPlayerHP > 0)
        {
            round++;
            terminal.SetColor("cyan");
            terminal.WriteLine($"--- Round {round} ---");

            // Player attacks — armor reduces damage
            long playerDmg = Math.Max(1, currentPlayer.Strength + currentPlayer.WeapPow - kingDef - kingArmPow);
            playerDmg += random.Next(Math.Max(1, (int)currentPlayer.WeapPow / 2));
            kingHP -= playerDmg;

            terminal.SetColor("bright_green");
            terminal.WriteLine($"You strike for {playerDmg} damage! (King HP: {Math.Max(0, kingHP)})");

            if (kingHP <= 0) break;

            // King attacks — uses full combat stats
            long kingDmg = Math.Max(1, kingStr + kingWeapPow - currentPlayer.Defence - currentPlayer.ArmPow);
            kingDmg += random.Next(Math.Max(1, (int)kingWeapPow / 3));
            finalPlayerHP -= kingDmg;

            terminal.SetColor("red");
            terminal.WriteLine($"{currentKing.Name} strikes for {kingDmg} damage! (Your HP: {Math.Max(0, finalPlayerHP)})");

            await Task.Delay(500);
        }

        currentPlayer.HP = Math.Max(1, finalPlayerHP);

        if (kingHP <= 0)
        {
            // Player wins!
            terminal.WriteLine("");
            terminal.SetColor("bright_yellow");
            terminal.WriteLine("════════════════════════════════════════════════");
            terminal.WriteLine("              VICTORY!");
            terminal.WriteLine("════════════════════════════════════════════════");
            terminal.WriteLine($"You have defeated {currentKing.GetTitle()} {currentKing.Name}!");
            terminal.WriteLine("The throne is yours!");

            // Record old monarch
            monarchHistory.Add(new MonarchRecord
            {
                Name = currentKing.Name,
                Title = currentKing.GetTitle(),
                DaysReigned = (int)currentKing.TotalReign,
                CoronationDate = currentKing.CoronationDate,
                EndReason = $"Defeated by {currentPlayer.DisplayName}"
            });
            while (monarchHistory.Count > MaxMonarchHistory)
                monarchHistory.RemoveAt(0);

            // Crown new monarch — inherit the previous king's treasury and orphans
            long inheritedTreasury = currentKing.Treasury;
            var inheritedOrphans = currentKing?.Orphans?.ToList();
            string oldKingName = currentKing.Name;
            bool oldKingWasHuman = currentKing.AI == CharacterAI.Human;
            ClearRoyalMarriage(currentKing); // Clear old king's royal spouse before replacing
            currentPlayer.King = true;
            currentKing = King.CreateNewKing(currentPlayer.DisplayName, CharacterAI.Human, currentPlayer.Sex, inheritedOrphans);
            currentKing.Treasury = inheritedTreasury;
            playerIsKing = true;

            currentPlayer.PKills++;

            // Track archetype - Major Ruler moment
            UsurperRemake.Systems.ArchetypeTracker.Instance.RecordBecameKing();

            NewsSystem.Instance.Newsy(true, $"{currentPlayer.DisplayName} has seized the throne! Long live the new {currentKing.GetTitle()}!");

            // Notify the dethroned player
            if (oldKingWasHuman)
                NotifyDethronedPlayer(oldKingName, currentPlayer.DisplayName, "defeated you in combat");

            // Persist to world_state so world sim and other players see the new king immediately
            PersistRoyalCourtToWorldState();

            await Task.Delay(4000);
            return false; // Stay in castle as new king
        }
        else
        {
            // Player lost
            terminal.WriteLine("");
            terminal.SetColor("red");
            terminal.WriteLine("You have been defeated!");
            terminal.WriteLine("The guards drag you from the castle...");

            await Task.Delay(3000);
            await NavigateToLocation(GameLocation.MainStreet);
            return true; // Exit castle
        }
    }

    private async Task<bool> ClaimEmptyThrone()
    {
        terminal.ClearScreen();
        terminal.SetColor("bright_yellow");
        terminal.WriteLine("╔══════════════════════════════════════════════════════════════════════════════╗");
        terminal.WriteLine("║                         CLAIMING THE THRONE                                 ║");
        terminal.WriteLine("╚══════════════════════════════════════════════════════════════════════════════╝");
        terminal.WriteLine("");

        // Check level requirement
        if (currentPlayer.Level < GameConfig.MinLevelKing)
        {
            terminal.SetColor("red");
            terminal.WriteLine($"You must be at least level {GameConfig.MinLevelKing} to claim the throne!");
            terminal.WriteLine($"Your current level: {currentPlayer.Level}");
            await Task.Delay(2500);
            return false;
        }

        terminal.SetColor("white");
        terminal.WriteLine("The Castle seems to be in disarray!");
        terminal.WriteLine("No King or Queen is to be found anywhere. People are just");
        terminal.WriteLine("running around in a disorganized manner.");
        terminal.WriteLine("");

        // RULE: King cannot be on a team - must leave team first
        if (!string.IsNullOrEmpty(currentPlayer.Team))
        {
            terminal.SetColor("yellow");
            terminal.WriteLine("To claim the throne, you must abandon your team.");
            terminal.WriteLine($"You are currently a member of '{currentPlayer.Team}'.");
            terminal.WriteLine("");
            terminal.SetColor("cyan");
            terminal.Write("Leave your team to claim the crown? (Y/N): ");
            terminal.SetColor("white");
            string leaveConfirm = await terminal.ReadLineAsync();

            if (leaveConfirm?.ToUpper() != "Y")
            {
                terminal.SetColor("gray");
                terminal.WriteLine("You decide to remain loyal to your team.");
                await Task.Delay(2000);
                return false;
            }

            // Force leave team and clear dungeon party
            string oldTeam = currentPlayer.Team;
            CityControlSystem.Instance.ForceLeaveTeam(currentPlayer);
            GameEngine.Instance?.ClearDungeonParty(); // Clear NPC teammates from dungeon
            terminal.SetColor("yellow");
            terminal.WriteLine($"You have left '{oldTeam}' to claim the crown.");
            terminal.WriteLine("");
        }

        var title = currentPlayer.Sex == CharacterSex.Male ? "KING" : "QUEEN";
        terminal.SetColor("cyan");
        terminal.Write($"Proclaim yourself {title}? (Y/N): ");
        terminal.SetColor("white");
        string confirm = await terminal.ReadLineAsync();

        if (confirm?.ToUpper() != "Y")
        {
            terminal.SetColor("gray");
            terminal.WriteLine("You decide not to claim the throne.");
            await Task.Delay(2000);
            return false;
        }

        // Crown the new monarch — inherit any orphans from previous reign
        var inheritedOrphans = currentKing?.Orphans?.ToList();
        ClearRoyalMarriage(currentKing); // Clear old king's royal spouse if any
        currentPlayer.King = true;
        currentKing = King.CreateNewKing(currentPlayer.DisplayName, CharacterAI.Human, currentPlayer.Sex, inheritedOrphans);
        playerIsKing = true;

        // Track archetype - Major Ruler moment
        UsurperRemake.Systems.ArchetypeTracker.Instance.RecordBecameKing();

        terminal.WriteLine("");
        terminal.SetColor("bright_yellow");
        terminal.WriteLine("═══════════════════════════════════════════════════════════════");
        terminal.WriteLine("        Congratulations, The Castle is Yours!");
        terminal.WriteLine($"  The InterRegnum is over, long live the {title}!");
        terminal.WriteLine("═══════════════════════════════════════════════════════════════");
        terminal.WriteLine("");

        NewsSystem.Instance.Newsy(true, $"{currentPlayer.DisplayName} has claimed the empty throne! Long live the {title}!");

        // Persist to world_state so world sim and other players see the new king immediately
        PersistRoyalCourtToWorldState();

        await Task.Delay(4000);
        return false; // Stay in castle as new king
    }

    private async Task<bool> AttemptAbdication()
    {
        terminal.ClearScreen();
        terminal.SetColor("bright_red");
        terminal.WriteLine("╔══════════════════════════════════════════════════════════════════════════════╗");
        terminal.WriteLine("║                            ABDICATION                                       ║");
        terminal.WriteLine("╚══════════════════════════════════════════════════════════════════════════════╝");
        terminal.WriteLine("");

        terminal.SetColor("white");
        terminal.WriteLine("Are you sure you want to abdicate the throne?");
        terminal.WriteLine("This action cannot be undone!");
        terminal.WriteLine("");
        terminal.SetColor("yellow");
        terminal.WriteLine("You will lose all royal privileges and the kingdom");
        terminal.WriteLine("will be thrown into chaos until a new ruler emerges.");
        terminal.WriteLine("");

        terminal.SetColor("cyan");
        terminal.Write("Abdicate the throne? (type 'yes' to confirm): ");
        terminal.SetColor("white");
        string confirm = await terminal.ReadLineAsync();

        if (confirm?.ToLower() == "yes")
        {
            // Record old monarch
            monarchHistory.Add(new MonarchRecord
            {
                Name = currentKing.Name,
                Title = currentKing.GetTitle(),
                DaysReigned = (int)currentKing.TotalReign,
                CoronationDate = currentKing.CoronationDate,
                EndReason = "Abdicated"
            });
            while (monarchHistory.Count > MaxMonarchHistory)
                monarchHistory.RemoveAt(0);

            terminal.WriteLine("");
            terminal.SetColor("gray");
            terminal.WriteLine("You pack your few personal belongings and leave your Crown");
            terminal.WriteLine("to the royal treasurer. You dress yourself in simple clothes");
            terminal.WriteLine("and walk out from the Castle, never to return (?).");
            terminal.WriteLine("");

            currentPlayer.King = false;
            currentPlayer.RoyalMercenaries?.Clear(); // Dismiss bodyguards on abdication
            currentPlayer.RecalculateStats(); // Remove Royal Authority HP bonus
            ClearRoyalMarriage(currentKing); // Clear royal spouse before abdication
            currentKing.IsActive = false;
            currentKing = null;
            playerIsKing = false;

            terminal.SetColor("bright_yellow");
            terminal.WriteLine($"The {(currentPlayer.Sex == CharacterSex.Male ? "King" : "Queen")} has ABDICATED!");
            terminal.SetColor("red");
            terminal.WriteLine("The land is in disarray! Who will claim the Throne?");

            NewsSystem.Instance.Newsy(true, $"{currentPlayer.DisplayName} has abdicated the throne! The kingdom is in chaos!");

            // Trigger immediate NPC succession so the throne doesn't stay empty
            TriggerNPCSuccession();

            // Persist the new king (or empty throne) to world_state for online mode
            PersistRoyalCourtToWorldState();

            await Task.Delay(4000);
            await NavigateToLocation(GameLocation.MainStreet);
            return true; // Exit castle
        }
        else
        {
            terminal.SetColor("bright_green");
            terminal.WriteLine("Phew! The kingdom breathes a sigh of relief.");
            await Task.Delay(2000);
            return false;
        }
    }

    // ═══════════════════════════════════════════════════════════════════════════
    // DONATION
    // ═══════════════════════════════════════════════════════════════════════════

    private async Task SeekAudience()
    {
        if (currentKing == null || !currentKing.IsActive)
        {
            terminal.ClearScreen();
            terminal.SetColor("bright_cyan");
            terminal.WriteLine("╔══════════════════════════════════════════════════════════════════════════════╗");
            terminal.WriteLine("║                           SEEK AN AUDIENCE                                  ║");
            terminal.WriteLine("╚══════════════════════════════════════════════════════════════════════════════╝");
            terminal.WriteLine("");
            terminal.SetColor("gray");
            terminal.WriteLine("With no monarch on the throne, there is no one to grant you an audience.");
            terminal.WriteLine("The throne room stands empty and echoing...");
            terminal.WriteLine("");
            terminal.SetColor("darkgray");
            terminal.WriteLine("Press Enter to continue...");
            await terminal.ReadKeyAsync();
            return;
        }

        // Check reputation for access
        long reputation = (currentPlayer.Chivalry + currentPlayer.Fame) / 2;

        if (reputation < 25)
        {
            terminal.ClearScreen();
            terminal.SetColor("bright_cyan");
            terminal.WriteLine("╔══════════════════════════════════════════════════════════════════════════════╗");
            terminal.WriteLine("║                           SEEK AN AUDIENCE                                  ║");
            terminal.WriteLine("╚══════════════════════════════════════════════════════════════════════════════╝");
            terminal.WriteLine("");
            terminal.SetColor("red");
            terminal.WriteLine("The guards block your path!");
            terminal.WriteLine("");
            terminal.SetColor("yellow");
            terminal.WriteLine($"\"Begone, {currentPlayer.DisplayName}! The {currentKing.GetTitle()} has no time");
            terminal.WriteLine("for the likes of you. Prove your worth to the realm first!\"");
            terminal.WriteLine("");
            terminal.SetColor("gray");
            terminal.WriteLine("(Increase your Chivalry and Fame to gain an audience)");
            terminal.WriteLine("");
            terminal.SetColor("darkgray");
            terminal.WriteLine("Press Enter to continue...");
            await terminal.ReadKeyAsync();
            return;
        }

        // Audience granted - show menu
        while (true)
        {
            terminal.ClearScreen();
            terminal.SetColor("bright_cyan");
            terminal.WriteLine("╔══════════════════════════════════════════════════════════════════════════════╗");
            terminal.WriteLine("║                        AUDIENCE WITH THE CROWN                              ║");
            terminal.WriteLine("╚══════════════════════════════════════════════════════════════════════════════╝");
            terminal.WriteLine("");

            // Display monarch and player status
            terminal.SetColor("bright_yellow");
            terminal.WriteLine($"{currentKing.GetTitle()} {currentKing.Name} sits upon the throne.");
            terminal.SetColor("gray");
            terminal.WriteLine($"Reign: {currentKing.TotalReign} days | Treasury: {currentKing.Treasury:N0} gold");
            terminal.WriteLine("");

            terminal.SetColor("white");
            terminal.WriteLine($"Your Standing: {GetReputationTitle(reputation)} (Rep: {reputation})");
            terminal.WriteLine($"Your Gold: {currentPlayer.Gold:N0} | Chivalry: {currentPlayer.Chivalry} | Darkness: {currentPlayer.Darkness}");
            terminal.WriteLine("");

            // Show greeting based on reputation
            if (reputation >= 150)
            {
                terminal.SetColor("bright_green");
                terminal.WriteLine($"\"Welcome, noble {currentPlayer.DisplayName}! The realm is honored by your presence.\"");
            }
            else if (reputation >= 100)
            {
                terminal.SetColor("cyan");
                terminal.WriteLine($"\"Ah, {currentPlayer.DisplayName}. Your service to the crown is noted.\"");
            }
            else
            {
                terminal.SetColor("white");
                terminal.WriteLine($"\"State your business, {currentPlayer.DisplayName}.\"");
            }
            terminal.WriteLine("");

            // Menu options
            terminal.SetColor("bright_yellow");
            terminal.WriteLine("What do you wish to petition the Crown for?");
            terminal.WriteLine("");

            terminal.SetColor("darkgray");
            terminal.Write(" [");
            terminal.SetColor("bright_cyan");
            terminal.Write("Q");
            terminal.SetColor("darkgray");
            terminal.Write("]");
            terminal.SetColor("white");
            terminal.WriteLine(" Request a Royal Quest");

            terminal.SetColor("darkgray");
            terminal.Write(" [");
            terminal.SetColor("bright_yellow");
            terminal.Write("K");
            terminal.SetColor("darkgray");
            terminal.Write("]");
            terminal.SetColor("white");
            terminal.Write(" Request Knighthood");
            terminal.SetColor("gray");
            terminal.WriteLine($" (Requires Chivalry 200+, Fame 100+)");

            terminal.SetColor("darkgray");
            terminal.Write(" [");
            terminal.SetColor("bright_green");
            terminal.Write("P");
            terminal.SetColor("darkgray");
            terminal.Write("]");
            terminal.SetColor("white");
            terminal.Write(" Request a Pardon");
            terminal.SetColor("gray");
            terminal.WriteLine($" (Clear Darkness for gold)");

            terminal.SetColor("darkgray");
            terminal.Write(" [");
            terminal.SetColor("bright_magenta");
            terminal.Write("L");
            terminal.SetColor("darkgray");
            terminal.Write("]");
            terminal.SetColor("white");
            terminal.Write(" Request a Loan");
            terminal.SetColor("gray");
            terminal.WriteLine($" (Borrow from Treasury)");

            terminal.SetColor("darkgray");
            terminal.Write(" [");
            terminal.SetColor("bright_red");
            terminal.Write("C");
            terminal.SetColor("darkgray");
            terminal.Write("]");
            terminal.SetColor("white");
            terminal.WriteLine(" Report a Crime (Place Bounty)");

            terminal.SetColor("darkgray");
            terminal.Write(" [");
            terminal.SetColor("cyan");
            terminal.Write("B");
            terminal.SetColor("darkgray");
            terminal.Write("]");
            terminal.SetColor("white");
            terminal.Write(" Request a Blessing");
            terminal.SetColor("gray");
            terminal.WriteLine($" (Temporary stat boost)");

            terminal.SetColor("darkgray");
            terminal.Write(" [");
            terminal.SetColor("yellow");
            terminal.Write("T");
            terminal.SetColor("darkgray");
            terminal.Write("]");
            terminal.SetColor("white");
            terminal.Write(" Petition for Tax Relief");
            terminal.SetColor("gray");
            terminal.WriteLine($" (Reduce kingdom taxes)");

            terminal.WriteLine("");
            terminal.SetColor("darkgray");
            terminal.Write(" [");
            terminal.SetColor("red");
            terminal.Write("R");
            terminal.SetColor("darkgray");
            terminal.Write("]");
            terminal.SetColor("white");
            terminal.WriteLine(" Return to Castle");
            terminal.WriteLine("");

            string choice = (await terminal.GetKeyInput()).ToUpperInvariant();

            switch (choice)
            {
                case "Q":
                    await AudienceRequestQuest();
                    break;
                case "K":
                    await AudienceRequestKnighthood();
                    break;
                case "P":
                    await AudienceRequestPardon();
                    break;
                case "L":
                    await AudienceRequestLoan();
                    break;
                case "C":
                    await AudienceReportCrime();
                    break;
                case "B":
                    await AudienceRequestBlessing();
                    break;
                case "T":
                    await AudiencePetitionTaxRelief();
                    break;
                case "R":
                case "ESCAPE":
                    return;
            }
        }
    }

    private string GetReputationTitle(long reputation)
    {
        if (reputation >= 500) return "Legendary Hero";
        if (reputation >= 300) return "Champion of the Realm";
        if (reputation >= 200) return "Honored Noble";
        if (reputation >= 150) return "Trusted Ally";
        if (reputation >= 100) return "Respected Citizen";
        if (reputation >= 50) return "Known Adventurer";
        if (reputation >= 25) return "Common Petitioner";
        return "Unknown";
    }

    /// <summary>
    /// Request a special royal quest from the king
    /// </summary>
    private async Task AudienceRequestQuest()
    {
        terminal.ClearScreen();
        terminal.SetColor("bright_cyan");
        terminal.WriteLine("═══ REQUEST A ROYAL QUEST ═══");
        terminal.WriteLine("");

        long reputation = (currentPlayer.Chivalry + currentPlayer.Fame) / 2;

        if (reputation < 50)
        {
            terminal.SetColor("yellow");
            terminal.WriteLine($"{currentKing.GetTitle()} {currentKing.Name} waves dismissively.");
            terminal.WriteLine("\"You have not yet proven yourself worthy of a royal commission.\"");
            terminal.WriteLine("");
            terminal.SetColor("gray");
            terminal.WriteLine("(Requires reputation of 50+)");
        }
        else
        {
            // Check if player already has an active royal quest
            var existingRoyalQuest = QuestSystem.GetActiveQuestsForPlayer(currentPlayer.Name2)
                .FirstOrDefault(q => q.Initiator == currentKing.Name && q.Title.StartsWith("Royal Commission"));

            if (existingRoyalQuest != null)
            {
                terminal.SetColor("yellow");
                terminal.WriteLine($"{currentKing.GetTitle()} {currentKing.Name} raises an eyebrow.");
                terminal.WriteLine("\"You already have a royal commission to complete.\"");
                terminal.WriteLine("");
                terminal.SetColor("white");
                terminal.WriteLine($"Active Quest: {existingRoyalQuest.Title}");
                terminal.WriteLine($"Days Remaining: {existingRoyalQuest.DaysRemaining}");
                terminal.WriteLine("");
                terminal.SetColor("gray");
                terminal.WriteLine("Complete or abandon your current quest first.");
            }
            else
            {
                terminal.SetColor("bright_green");
                terminal.WriteLine($"{currentKing.GetTitle()} {currentKing.Name} considers your request...");
                terminal.WriteLine("");

                // Generate a special royal quest with better rewards
                var random = new Random();
                int difficulty = Math.Min(4, 1 + currentPlayer.Level / 15);
                long goldReward = (500 + random.Next(500)) * currentPlayer.Level * (difficulty + 1);
                long xpReward = (100 + random.Next(100)) * currentPlayer.Level * (difficulty + 1);

                string[] questTypes = {
                    "Eliminate dangerous monsters threatening our roads",
                    "Recover a stolen royal artifact from the dungeon depths",
                    "Clear a dungeon floor of all hostile creatures",
                    "Investigate strange occurrences in the lower dungeons",
                    "Hunt down a notorious criminal hiding in the shadows"
                };

                string questDesc = questTypes[random.Next(questTypes.Length)];

                terminal.SetColor("bright_yellow");
                terminal.WriteLine("\"I have a task worthy of your skills...\"");
                terminal.WriteLine("");
                terminal.SetColor("white");
                terminal.WriteLine($"Quest: {questDesc}");
                terminal.WriteLine($"Difficulty: {new string('*', difficulty + 1)}");
                terminal.WriteLine($"Reward: {goldReward:N0} gold + {xpReward:N0} XP (upon completion)");
                terminal.WriteLine($"Time Limit: {7 + difficulty * 2} days");
                terminal.WriteLine("");

                terminal.SetColor("cyan");
                terminal.Write("Accept this royal quest? (Y/N): ");
                string response = await terminal.ReadLineAsync();

                if (response?.ToUpper() == "Y")
                {
                    // Create the actual quest in the QuestSystem
                    var quest = QuestSystem.CreateRoyalAudienceQuest(
                        currentPlayer,
                        currentKing.Name,
                        difficulty,
                        goldReward,
                        xpReward,
                        questDesc);

                    // Give advance payment (25% of reward)
                    long advanceGold = goldReward / 4;
                    long advanceXP = xpReward / 4;
                    currentPlayer.Gold += advanceGold;
                    currentPlayer.Experience += advanceXP;

                    terminal.WriteLine("");
                    terminal.SetColor("bright_green");
                    terminal.WriteLine("\"Excellent! The Crown places its faith in you.\"");
                    terminal.WriteLine($"You receive {advanceGold:N0} gold as an advance.");
                    terminal.WriteLine($"You gain {advanceXP:N0} experience for this honor.");
                    terminal.WriteLine("");
                    terminal.SetColor("yellow");
                    terminal.WriteLine("Quest added to your active quests!");
                    terminal.WriteLine("Check the Quest Hall to view progress and turn in when complete.");

                    // Show objective
                    if (quest.Objectives.Count > 0)
                    {
                        terminal.WriteLine("");
                        terminal.SetColor("cyan");
                        terminal.WriteLine($"Objective: {quest.Objectives[0].Description}");
                        if (quest.Objectives[0].RequiredProgress > 1)
                        {
                            terminal.WriteLine($"Target: {quest.Objectives[0].TargetName} x{quest.Objectives[0].RequiredProgress}");
                        }
                    }

                    NewsSystem.Instance?.Newsy(true, $"{currentPlayer.DisplayName} accepted a royal quest from {currentKing.GetTitle()} {currentKing.Name}!");
                }
                else
                {
                    terminal.WriteLine("");
                    terminal.SetColor("gray");
                    terminal.WriteLine("\"Perhaps another time, then.\"");
                }
            }
        }

        terminal.WriteLine("");
        terminal.SetColor("darkgray");
        terminal.WriteLine("Press Enter to continue...");
        await terminal.ReadKeyAsync();
    }

    /// <summary>
    /// Request knighthood/title from the king
    /// </summary>
    private async Task AudienceRequestKnighthood()
    {
        terminal.ClearScreen();
        terminal.SetColor("bright_yellow");
        terminal.WriteLine("═══ REQUEST KNIGHTHOOD ═══");
        terminal.WriteLine("");

        // Check requirements
        bool hasChivalry = currentPlayer.Chivalry >= 200;
        bool hasFame = currentPlayer.Fame >= 100;
        bool hasLevel = currentPlayer.Level >= 10;
        bool notEvil = currentPlayer.Darkness < currentPlayer.Chivalry;

        // Check if already knighted (using a title marker)
        bool alreadyKnighted = currentPlayer.NobleTitle?.Contains("Sir") == true ||
                               currentPlayer.NobleTitle?.Contains("Dame") == true ||
                               currentPlayer.NobleTitle?.Contains("Lord") == true ||
                               currentPlayer.NobleTitle?.Contains("Lady") == true;

        if (alreadyKnighted)
        {
            terminal.SetColor("cyan");
            terminal.WriteLine($"{currentKing.GetTitle()} {currentKing.Name} smiles.");
            terminal.WriteLine($"\"You already bear a noble title, {currentPlayer.NobleTitle} {currentPlayer.DisplayName}.\"");
            terminal.WriteLine("\"Continue to serve the realm with honor.\"");
        }
        else if (!hasChivalry || !hasFame || !hasLevel || !notEvil)
        {
            terminal.SetColor("yellow");
            terminal.WriteLine($"{currentKing.GetTitle()} {currentKing.Name} shakes their head.");
            terminal.WriteLine("\"You are not yet ready for such an honor.\"");
            terminal.WriteLine("");
            terminal.SetColor("gray");
            terminal.WriteLine("Requirements for Knighthood:");
            terminal.WriteLine($"  Chivalry 200+: {(hasChivalry ? "Yes" : $"No ({currentPlayer.Chivalry}/200)")}");
            terminal.WriteLine($"  Fame 100+: {(hasFame ? "Yes" : $"No ({currentPlayer.Fame}/100)")}");
            terminal.WriteLine($"  Level 10+: {(hasLevel ? "Yes" : $"No ({currentPlayer.Level}/10)")}");
            terminal.WriteLine($"  Honorable (Chivalry > Darkness): {(notEvil ? "Yes" : "No")}");
        }
        else
        {
            terminal.SetColor("bright_green");
            terminal.WriteLine($"{currentKing.GetTitle()} {currentKing.Name} rises from the throne!");
            terminal.WriteLine("");
            terminal.SetColor("bright_yellow");
            terminal.WriteLine("\"Kneel before me, brave warrior.\"");
            terminal.WriteLine("");

            await Task.Delay(1500);

            string title = currentPlayer.Sex == CharacterSex.Male ? "Sir" : "Dame";
            currentPlayer.NobleTitle = title;

            terminal.SetColor("bright_cyan");
            terminal.WriteLine($"*The {currentKing.GetTitle()} draws the Royal Sword*");
            terminal.WriteLine("");
            terminal.WriteLine("\"By the power vested in me by the realm and the gods,\"");
            terminal.WriteLine($"\"I dub thee {title} {currentPlayer.DisplayName}!\"");
            terminal.WriteLine("");
            terminal.WriteLine("\"Rise, and serve the kingdom with honor!\"");
            terminal.WriteLine("");

            // Bonuses for knighthood
            currentPlayer.Chivalry += 50;
            currentPlayer.Fame += 25;

            terminal.SetColor("bright_green");
            terminal.WriteLine($"You are now {title} {currentPlayer.DisplayName}!");
            terminal.WriteLine("+50 Chivalry, +25 Fame");

            NewsSystem.Instance?.Newsy(true, $"{currentPlayer.DisplayName} was knighted by {currentKing.GetTitle()} {currentKing.Name}!");
        }

        terminal.WriteLine("");
        terminal.SetColor("darkgray");
        terminal.WriteLine("Press Enter to continue...");
        await terminal.ReadKeyAsync();
    }

    /// <summary>
    /// Request a pardon to reduce darkness
    /// </summary>
    private async Task AudienceRequestPardon()
    {
        terminal.ClearScreen();
        terminal.SetColor("bright_green");
        terminal.WriteLine("═══ REQUEST A PARDON ═══");
        terminal.WriteLine("");

        if (currentPlayer.Darkness <= 0)
        {
            terminal.SetColor("cyan");
            terminal.WriteLine($"{currentKing.GetTitle()} {currentKing.Name} looks puzzled.");
            terminal.WriteLine("\"You have no sins to pardon, noble soul. Your record is clean.\"");
        }
        else
        {
            // Cost scales with darkness level
            long pardonCost = currentPlayer.Darkness * 50 * (1 + currentPlayer.Level / 10);
            long reputation = (currentPlayer.Chivalry + currentPlayer.Fame) / 2;

            // Discount for high reputation
            if (reputation >= 200)
                pardonCost = (long)(pardonCost * 0.5);
            else if (reputation >= 100)
                pardonCost = (long)(pardonCost * 0.75);

            terminal.SetColor("white");
            terminal.WriteLine($"Your Current Darkness: {currentPlayer.Darkness}");
            terminal.WriteLine("");

            terminal.SetColor("yellow");
            terminal.WriteLine($"{currentKing.GetTitle()} {currentKing.Name} considers your request...");
            terminal.WriteLine("");
            terminal.SetColor("white");
            terminal.WriteLine("\"The Crown can offer absolution, but such mercy has a price.\"");
            terminal.WriteLine("");
            terminal.WriteLine($"Cost for Full Pardon: {pardonCost:N0} gold");
            terminal.WriteLine($"Your Gold: {currentPlayer.Gold:N0}");
            terminal.WriteLine("");

            if (currentPlayer.Gold < pardonCost)
            {
                terminal.SetColor("red");
                terminal.WriteLine("You cannot afford a full pardon.");
                terminal.WriteLine("");

                // Offer partial pardon
                long partialCost = pardonCost / 4;
                long partialReduction = currentPlayer.Darkness / 4;

                if (currentPlayer.Gold >= partialCost && partialReduction > 0)
                {
                    terminal.SetColor("cyan");
                    terminal.Write($"Accept partial pardon? (-{partialReduction} Darkness for {partialCost:N0} gold) (Y/N): ");
                    string partial = await terminal.ReadLineAsync();

                    if (partial?.ToUpper() == "Y")
                    {
                        currentPlayer.Gold -= partialCost;
                        currentPlayer.Darkness -= partialReduction;
                        currentKing.Treasury += partialCost;

                        terminal.WriteLine("");
                        terminal.SetColor("bright_green");
                        terminal.WriteLine("\"Some of your sins are forgiven. Go and sin no more.\"");
                        terminal.WriteLine($"Darkness reduced by {partialReduction}. New Darkness: {currentPlayer.Darkness}");
                    }
                }
            }
            else
            {
                terminal.SetColor("cyan");
                terminal.Write($"Pay {pardonCost:N0} gold for a full pardon? (Y/N): ");
                string response = await terminal.ReadLineAsync();

                if (response?.ToUpper() == "Y")
                {
                    currentPlayer.Gold -= pardonCost;
                    long oldDarkness = currentPlayer.Darkness;
                    currentPlayer.Darkness = 0;
                    currentKing.Treasury += pardonCost;

                    terminal.WriteLine("");
                    terminal.SetColor("bright_green");
                    terminal.WriteLine($"{currentKing.GetTitle()} {currentKing.Name} raises a hand in blessing.");
                    terminal.WriteLine("\"Your past sins are forgiven. Your slate is wiped clean.\"");
                    terminal.WriteLine("");
                    terminal.WriteLine($"Darkness reduced from {oldDarkness} to 0!");

                    NewsSystem.Instance?.Newsy(false, $"{currentPlayer.DisplayName} received a royal pardon from {currentKing.GetTitle()} {currentKing.Name}.");
                }
            }
        }

        terminal.WriteLine("");
        terminal.SetColor("darkgray");
        terminal.WriteLine("Press Enter to continue...");
        await terminal.ReadKeyAsync();
    }

    /// <summary>
    /// Request a loan from the royal treasury
    /// </summary>
    private async Task AudienceRequestLoan()
    {
        terminal.ClearScreen();
        terminal.SetColor("bright_magenta");
        terminal.WriteLine("═══ REQUEST A LOAN ═══");
        terminal.WriteLine("");

        long reputation = (currentPlayer.Chivalry + currentPlayer.Fame) / 2;

        // Check if player has an outstanding loan
        if (currentPlayer.RoyalLoanAmount > 0)
        {
            // Show loan status and offer repayment
            long totalOwed = (long)(currentPlayer.RoyalLoanAmount * 1.10); // 10% interest
            int daysRemaining = currentPlayer.RoyalLoanDueDay - DailySystemManager.Instance.CurrentDay;

            terminal.SetColor("yellow");
            terminal.WriteLine("You have an outstanding loan from the Crown.");
            terminal.WriteLine("");
            terminal.SetColor("white");
            terminal.WriteLine($"Principal: {currentPlayer.RoyalLoanAmount:N0} gold");
            terminal.WriteLine($"With Interest (10%): {totalOwed:N0} gold");
            terminal.WriteLine($"Days Remaining: {Math.Max(0, daysRemaining)}");
            terminal.WriteLine($"Your Gold: {currentPlayer.Gold:N0}");
            terminal.WriteLine("");

            if (daysRemaining < 0)
            {
                terminal.SetColor("red");
                terminal.WriteLine("YOUR LOAN IS OVERDUE!");
                terminal.WriteLine("The Crown demands immediate repayment!");
            }

            if (currentPlayer.Gold >= totalOwed)
            {
                terminal.SetColor("cyan");
                terminal.Write($"Repay the loan of {totalOwed:N0} gold? (Y/N): ");
                string response = await terminal.ReadLineAsync();

                if (response?.ToUpper() == "Y")
                {
                    currentPlayer.Gold -= totalOwed;
                    currentKing.Treasury += totalOwed;
                    currentPlayer.RoyalLoanAmount = 0;
                    currentPlayer.RoyalLoanDueDay = 0;

                    terminal.WriteLine("");
                    terminal.SetColor("bright_green");
                    terminal.WriteLine($"{currentKing.GetTitle()} {currentKing.Name} nods approvingly.");
                    terminal.WriteLine("\"Your debt is paid. The Crown is pleased.\"");
                    terminal.WriteLine("");
                    terminal.WriteLine("+10 Chivalry for honoring your obligations.");
                    currentPlayer.Chivalry += 10;

                    NewsSystem.Instance?.Newsy(false, $"{currentPlayer.DisplayName} repaid their royal loan.");
                }
            }
            else
            {
                terminal.SetColor("gray");
                terminal.WriteLine("You don't have enough gold to repay the full amount.");
                terminal.WriteLine("Gather more funds and return when you can pay.");
            }
        }
        else if (reputation < 75)
        {
            terminal.SetColor("yellow");
            terminal.WriteLine($"{currentKing.GetTitle()} {currentKing.Name} looks skeptical.");
            terminal.WriteLine("\"The Crown does not lend to those of... uncertain reputation.\"");
            terminal.WriteLine("");
            terminal.SetColor("gray");
            terminal.WriteLine("(Requires reputation of 75+)");
        }
        else if (currentKing.Treasury < 1000)
        {
            terminal.SetColor("red");
            terminal.WriteLine($"{currentKing.GetTitle()} {currentKing.Name} sighs heavily.");
            terminal.WriteLine("\"The royal coffers are nearly empty. We have nothing to lend.\"");
        }
        else
        {
            // Max loan based on reputation and level
            long maxLoan = Math.Min(currentKing.Treasury / 2, reputation * 10 * currentPlayer.Level);
            maxLoan = Math.Max(500, maxLoan); // Minimum loan

            terminal.SetColor("white");
            terminal.WriteLine($"Royal Treasury: {currentKing.Treasury:N0} gold");
            terminal.WriteLine($"Maximum Loan Available: {maxLoan:N0} gold");
            terminal.WriteLine("");
            terminal.SetColor("yellow");
            terminal.WriteLine("Loan Terms: 10% interest, due in 30 days");
            terminal.WriteLine("(Failure to repay will damage your reputation severely)");
            terminal.WriteLine("");

            terminal.SetColor("cyan");
            terminal.Write($"How much would you like to borrow? (0-{maxLoan:N0}): ");
            string input = await terminal.ReadLineAsync();

            if (long.TryParse(input, out long amount) && amount > 0 && amount <= maxLoan)
            {
                currentPlayer.Gold += amount;
                currentKing.Treasury -= amount;

                // Track the loan for repayment
                currentPlayer.RoyalLoanAmount = amount;
                currentPlayer.RoyalLoanDueDay = DailySystemManager.Instance.CurrentDay + 30;

                terminal.WriteLine("");
                terminal.SetColor("bright_green");
                terminal.WriteLine($"{currentKing.GetTitle()} {currentKing.Name} nods.");
                terminal.WriteLine($"\"The Crown grants you a loan of {amount:N0} gold.\"");
                terminal.WriteLine($"\"You have 30 days to repay {(long)(amount * 1.10):N0} gold (with interest).\"");
                terminal.WriteLine("");
                terminal.WriteLine($"You received {amount:N0} gold.");

                NewsSystem.Instance?.Newsy(false, $"{currentPlayer.DisplayName} received a loan of {amount:N0} gold from the Crown.");
            }
            else if (amount == 0 || string.IsNullOrEmpty(input))
            {
                terminal.WriteLine("");
                terminal.SetColor("gray");
                terminal.WriteLine("\"Very well. The offer stands should you need it.\"");
            }
            else
            {
                terminal.WriteLine("");
                terminal.SetColor("red");
                terminal.WriteLine("\"That is not a valid amount.\"");
            }
        }

        terminal.WriteLine("");
        terminal.SetColor("darkgray");
        terminal.WriteLine("Press Enter to continue...");
        await terminal.ReadKeyAsync();
    }

    /// <summary>
    /// Report a crime and place a bounty on an NPC
    /// </summary>
    private async Task AudienceReportCrime()
    {
        terminal.ClearScreen();
        terminal.SetColor("bright_red");
        terminal.WriteLine("═══ REPORT A CRIME ═══");
        terminal.WriteLine("");

        terminal.SetColor("white");
        terminal.WriteLine($"{currentKing.GetTitle()} {currentKing.Name} listens intently.");
        terminal.WriteLine("\"Who has wronged you or the realm?\"");
        terminal.WriteLine("");

        // Get list of NPCs that don't already have bounties
        var npcs = NPCSpawnSystem.Instance.ActiveNPCs?
            .Where(n => n.IsAlive && n.Darkness > 50)
            .OrderByDescending(n => n.Darkness)
            .Take(10)
            .ToList() ?? new List<NPC>();

        if (npcs.Count == 0)
        {
            terminal.SetColor("yellow");
            terminal.WriteLine("\"There are no known criminals at large at this time.\"");
        }
        else
        {
            terminal.SetColor("gray");
            terminal.WriteLine("Known suspicious characters:");
            terminal.WriteLine("");

            for (int i = 0; i < npcs.Count; i++)
            {
                var npc = npcs[i];
                terminal.SetColor("white");
                terminal.Write($"  {i + 1}. {npc.Name,-20}");
                terminal.SetColor("gray");
                terminal.Write($" Lv{npc.Level}");
                terminal.SetColor("red");
                terminal.WriteLine($" (Darkness: {npc.Darkness})");
            }

            terminal.WriteLine("");
            terminal.SetColor("cyan");
            terminal.Write("Enter number to report (or 0 to cancel): ");
            string input = await terminal.ReadLineAsync();

            if (int.TryParse(input, out int choice) && choice > 0 && choice <= npcs.Count)
            {
                var target = npcs[choice - 1];
                long bountyCost = 100 + target.Level * 50;

                terminal.WriteLine("");
                terminal.SetColor("yellow");
                terminal.WriteLine($"To place a bounty on {target.Name}, you must contribute {bountyCost:N0} gold.");
                terminal.WriteLine($"Your Gold: {currentPlayer.Gold:N0}");
                terminal.WriteLine("");

                if (currentPlayer.Gold >= bountyCost)
                {
                    terminal.SetColor("cyan");
                    terminal.Write($"Pay {bountyCost:N0} gold to place a bounty? (Y/N): ");
                    string confirm = await terminal.ReadLineAsync();

                    if (confirm?.ToUpper() == "Y")
                    {
                        currentPlayer.Gold -= bountyCost;
                        currentKing.Treasury += bountyCost / 2; // Half goes to treasury

                        terminal.WriteLine("");
                        terminal.SetColor("bright_green");
                        terminal.WriteLine($"{currentKing.GetTitle()} {currentKing.Name} nods grimly.");
                        terminal.WriteLine($"\"A bounty has been placed on {target.Name}.\"");
                        terminal.WriteLine("\"Justice will be served.\"");

                        // Increase the target's darkness (they're now wanted)
                        target.Darkness += 25;

                        NewsSystem.Instance?.Newsy(true, $"A bounty has been placed on {target.Name} by royal decree!");

                        // Wire into QuestSystem so the bounty is trackable
                        QuestSystem.PostBountyOnPlayer(target.Name, "Criminal activity", (int)Math.Min(bountyCost, int.MaxValue));

                        // Small chivalry boost for reporting
                        currentPlayer.Chivalry += 5;
                    }
                }
                else
                {
                    terminal.SetColor("red");
                    terminal.WriteLine("You cannot afford to post this bounty.");
                }
            }
        }

        terminal.WriteLine("");
        terminal.SetColor("darkgray");
        terminal.WriteLine("Press Enter to continue...");
        await terminal.ReadKeyAsync();
    }

    /// <summary>
    /// Request a temporary blessing/buff from the king
    /// </summary>
    private async Task AudienceRequestBlessing()
    {
        terminal.ClearScreen();
        terminal.SetColor("cyan");
        terminal.WriteLine("═══ REQUEST A BLESSING ═══");
        terminal.WriteLine("");

        long reputation = (currentPlayer.Chivalry + currentPlayer.Fame) / 2;

        if (reputation < 100)
        {
            terminal.SetColor("yellow");
            terminal.WriteLine($"{currentKing.GetTitle()} {currentKing.Name} considers your request.");
            terminal.WriteLine("\"You have not yet earned such a boon from the Crown.\"");
            terminal.WriteLine("");
            terminal.SetColor("gray");
            terminal.WriteLine("(Requires reputation of 100+)");
        }
        else
        {
            // Cost for blessing
            long blessingCost = 500 * currentPlayer.Level;

            // Discount for very high reputation
            if (reputation >= 300)
            {
                blessingCost = 0;
                terminal.SetColor("bright_green");
                terminal.WriteLine($"{currentKing.GetTitle()} {currentKing.Name} smiles warmly.");
                terminal.WriteLine("\"For a champion of your standing, this blessing is freely given.\"");
            }
            else if (reputation >= 200)
            {
                blessingCost /= 2;
                terminal.SetColor("white");
                terminal.WriteLine($"{currentKing.GetTitle()} {currentKing.Name} nods approvingly.");
                terminal.WriteLine($"\"For {blessingCost:N0} gold, the Crown will bestow its blessing upon you.\"");
            }
            else
            {
                terminal.SetColor("white");
                terminal.WriteLine($"{currentKing.GetTitle()} {currentKing.Name} considers.");
                terminal.WriteLine($"\"Such a blessing requires a donation of {blessingCost:N0} gold to the realm.\"");
            }

            terminal.WriteLine("");
            terminal.SetColor("bright_yellow");
            terminal.WriteLine("The Royal Blessing grants:");
            terminal.WriteLine("  +10% to attack and defense in combat");
            terminal.WriteLine("  Improved accuracy against enemies");
            terminal.WriteLine("");

            bool canAfford = currentPlayer.Gold >= blessingCost || blessingCost == 0;

            if (!canAfford)
            {
                terminal.SetColor("red");
                terminal.WriteLine($"You need {blessingCost:N0} gold. You have {currentPlayer.Gold:N0}.");
            }
            else
            {
                terminal.SetColor("cyan");
                string prompt = blessingCost > 0
                    ? $"Pay {blessingCost:N0} gold for the Royal Blessing? (Y/N): "
                    : "Accept the Royal Blessing? (Y/N): ";
                terminal.Write(prompt);
                string response = await terminal.ReadLineAsync();

                if (response?.ToUpper() == "Y")
                {
                    if (blessingCost > 0)
                    {
                        currentPlayer.Gold -= blessingCost;
                        currentKing.Treasury += blessingCost;
                    }

                    // Apply the Royal Blessing status effect (lasts several combats)
                    currentPlayer.ApplyStatus(StatusEffect.RoyalBlessing, GameConfig.RoyalBlessingDuration);

                    // Also give a small immediate HP/Mana restoration
                    long hpRestore = Math.Min(25, currentPlayer.MaxHP - currentPlayer.HP);
                    long manaRestore = Math.Min(25, currentPlayer.MaxMana - currentPlayer.Mana);
                    currentPlayer.HP += hpRestore;
                    currentPlayer.Mana += manaRestore;

                    terminal.WriteLine("");
                    terminal.SetColor("bright_cyan");
                    terminal.WriteLine($"{currentKing.GetTitle()} {currentKing.Name} raises a hand in blessing.");
                    terminal.WriteLine("");
                    terminal.SetColor("bright_yellow");
                    terminal.WriteLine("*A warm golden light surrounds you*");
                    terminal.WriteLine("");
                    terminal.SetColor("bright_green");
                    terminal.WriteLine("You feel strengthened by the Crown's favor!");
                    terminal.WriteLine("  +10% to combat stats (attack/defense)");
                    if (hpRestore > 0 || manaRestore > 0)
                    {
                        terminal.WriteLine($"  Restored {hpRestore} HP and {manaRestore} Mana");
                    }
                    terminal.WriteLine("  (Blessing lasts for several combats)");

                    NewsSystem.Instance?.Newsy(false, $"{currentPlayer.DisplayName} received the Royal Blessing from {currentKing.GetTitle()} {currentKing.Name}.");
                }
            }
        }

        terminal.WriteLine("");
        terminal.SetColor("darkgray");
        terminal.WriteLine("Press Enter to continue...");
        await terminal.ReadKeyAsync();
    }

    /// <summary>
    /// Petition for reduced taxes in the kingdom
    /// </summary>
    private async Task AudiencePetitionTaxRelief()
    {
        terminal.ClearScreen();
        terminal.SetColor("yellow");
        terminal.WriteLine("═══ PETITION FOR TAX RELIEF ═══");
        terminal.WriteLine("");

        long reputation = (currentPlayer.Chivalry + currentPlayer.Fame) / 2;

        terminal.SetColor("white");
        terminal.WriteLine($"Current Kingdom Tax Rate: {currentKing.TaxRate}%");
        terminal.WriteLine("");

        if (reputation < 150)
        {
            terminal.SetColor("yellow");
            terminal.WriteLine($"{currentKing.GetTitle()} {currentKing.Name} looks amused.");
            terminal.WriteLine("\"And who are you to petition on matters of state?\"");
            terminal.WriteLine("");
            terminal.SetColor("gray");
            terminal.WriteLine("(Requires reputation of 150+ to influence tax policy)");
        }
        else if (currentKing.TaxRate <= 5)
        {
            terminal.SetColor("cyan");
            terminal.WriteLine($"{currentKing.GetTitle()} {currentKing.Name} spreads their hands.");
            terminal.WriteLine("\"The taxes are already as low as they can reasonably be.\"");
            terminal.WriteLine("\"The realm still needs gold to function.\"");
        }
        else
        {
            // Cost to petition scales with current tax rate and reputation
            long petitionCost = currentKing.TaxRate * 100 * currentPlayer.Level;
            if (reputation >= 300)
                petitionCost /= 2;

            terminal.SetColor("white");
            terminal.WriteLine($"{currentKing.GetTitle()} {currentKing.Name} considers your words.");
            terminal.WriteLine("");
            terminal.WriteLine($"\"A tax reduction could be arranged... with proper compensation.\"");
            terminal.WriteLine("");
            terminal.WriteLine($"Cost to reduce tax by 5%: {petitionCost:N0} gold");
            terminal.WriteLine($"Your Gold: {currentPlayer.Gold:N0}");
            terminal.WriteLine("");

            if (currentPlayer.Gold >= petitionCost)
            {
                terminal.SetColor("cyan");
                terminal.Write($"Pay {petitionCost:N0} gold to reduce taxes? (Y/N): ");
                string response = await terminal.ReadLineAsync();

                if (response?.ToUpper() == "Y")
                {
                    currentPlayer.Gold -= petitionCost;
                    currentKing.Treasury += petitionCost;
                    long oldRate = currentKing.TaxRate;
                    currentKing.TaxRate = Math.Max(5, currentKing.TaxRate - 5);

                    terminal.WriteLine("");
                    terminal.SetColor("bright_green");
                    terminal.WriteLine($"{currentKing.GetTitle()} {currentKing.Name} nods solemnly.");
                    terminal.WriteLine("\"Very well. Let it be known that taxes are hereby reduced.\"");
                    terminal.WriteLine("");
                    terminal.WriteLine($"Kingdom tax rate reduced from {oldRate}% to {currentKing.TaxRate}%!");

                    // Fame boost for helping the people
                    currentPlayer.Fame += 20;
                    currentPlayer.Chivalry += 10;
                    terminal.WriteLine("+20 Fame, +10 Chivalry for aiding the common folk.");

                    NewsSystem.Instance?.Newsy(true, $"{currentPlayer.DisplayName} petitioned {currentKing.GetTitle()} {currentKing.Name} for tax relief! Kingdom taxes reduced to {currentKing.TaxRate}%.");
                }
            }
            else
            {
                terminal.SetColor("red");
                terminal.WriteLine("You cannot afford to make this petition.");
            }
        }

        terminal.WriteLine("");
        terminal.SetColor("darkgray");
        terminal.WriteLine("Press Enter to continue...");
        await terminal.ReadKeyAsync();
    }

    private async Task DonateToRoyalPurse()
    {
        if (currentKing == null || !currentKing.IsActive)
        {
            terminal.SetColor("yellow");
            terminal.WriteLine("There is no monarch to receive your donation!");
            await Task.Delay(2000);
            return;
        }

        terminal.SetColor("white");
        terminal.WriteLine($"The Royal Purse currently contains {currentKing.Treasury:N0} gold.");
        terminal.WriteLine($"You have {currentPlayer.Gold:N0} gold.");
        terminal.WriteLine("");

        terminal.SetColor("cyan");
        terminal.Write("How much gold do you wish to donate? ");
        terminal.SetColor("white");
        string input = await terminal.ReadLineAsync();

        if (long.TryParse(input, out long amount))
        {
            if (amount <= 0)
            {
                terminal.SetColor("gray");
                terminal.WriteLine("Donation cancelled.");
            }
            else if (amount > currentPlayer.Gold)
            {
                terminal.SetColor("red");
                terminal.WriteLine("You don't have that much gold!");
            }
            else
            {
                currentPlayer.Gold -= amount;
                currentKing.Treasury += amount;
                int chivalryGain = (int)Math.Min(50, amount / 100);
                currentPlayer.Chivalry += chivalryGain;

                terminal.SetColor("bright_green");
                terminal.WriteLine($"You donate {amount:N0} gold to the Royal Purse.");
                terminal.WriteLine($"Your chivalry increases by {chivalryGain} for this noble deed!");

                // Increase Crown standing (+1 per 50 gold donated)
                int crownStandingGain = (int)(amount / 50);
                if (crownStandingGain > 0)
                {
                    UsurperRemake.Systems.FactionSystem.Instance.ModifyReputation(UsurperRemake.Systems.Faction.TheCrown, crownStandingGain);
                    terminal.SetColor("bright_yellow");
                    terminal.WriteLine($"The Crown appreciates your loyalty. (+{crownStandingGain} Crown standing)");
                }
            }
        }
        else
        {
            terminal.SetColor("gray");
            terminal.WriteLine("Invalid amount.");
        }

        await Task.Delay(2500);
    }

    /// <summary>
    /// Player applies for a position in the Royal Guard
    /// </summary>
    private async Task ApplyForRoyalGuard()
    {
        terminal.ClearScreen();
        terminal.SetColor("bright_cyan");
        terminal.WriteLine("╔══════════════════════════════════════════════════════════════════════════════╗");
        terminal.WriteLine("║                      ROYAL GUARD RECRUITMENT                                 ║");
        terminal.WriteLine("╚══════════════════════════════════════════════════════════════════════════════╝");
        terminal.WriteLine("");

        // Check if there's a king
        if (currentKing == null || !currentKing.IsActive)
        {
            terminal.SetColor("yellow");
            terminal.WriteLine("With no monarch on the throne, the Royal Guard has disbanded.");
            terminal.WriteLine("There is no one to accept your application.");
            terminal.WriteLine("");
            terminal.SetColor("darkgray");
            terminal.WriteLine("Press Enter to continue...");
            await terminal.ReadKeyAsync();
            return;
        }

        // Check if guard slots are full
        if (currentKing.Guards.Count >= GameConfig.MaxRoyalGuards)
        {
            terminal.SetColor("yellow");
            terminal.WriteLine("The Royal Guard is at full capacity.");
            terminal.WriteLine("There are no positions available at this time.");
            terminal.WriteLine("");
            terminal.SetColor("darkgray");
            terminal.WriteLine("Press Enter to continue...");
            await terminal.ReadKeyAsync();
            return;
        }

        // Check if player is already a guard
        if (currentKing.Guards.Any(g => g.Name == currentPlayer.DisplayName || g.Name == currentPlayer.Name2))
        {
            terminal.SetColor("yellow");
            terminal.WriteLine("You are already serving in the Royal Guard!");
            terminal.WriteLine("");
            terminal.SetColor("darkgray");
            terminal.WriteLine("Press Enter to continue...");
            await terminal.ReadKeyAsync();
            return;
        }

        // Minimum level requirement
        int minLevel = 5;
        if (currentPlayer.Level < minLevel)
        {
            terminal.SetColor("red");
            terminal.WriteLine($"The captain of the guard looks you over...");
            terminal.WriteLine("");
            terminal.SetColor("yellow");
            terminal.WriteLine($"\"Come back when you've proven yourself, adventurer.\"");
            terminal.WriteLine($"\"We require guards of at least level {minLevel}.\"");
            terminal.WriteLine("");
            terminal.SetColor("darkgray");
            terminal.WriteLine("Press Enter to continue...");
            await terminal.ReadKeyAsync();
            return;
        }

        // Check reputation (chivalry vs darkness)
        if (currentPlayer.Darkness > currentPlayer.Chivalry + 50)
        {
            terminal.SetColor("red");
            terminal.WriteLine("The captain of the guard narrows his eyes...");
            terminal.WriteLine("");
            terminal.SetColor("yellow");
            terminal.WriteLine("\"Your reputation precedes you, and not in a good way.\"");
            terminal.WriteLine("\"The Royal Guard serves the realm with honor. Seek redemption first.\"");
            terminal.WriteLine("");
            terminal.SetColor("darkgray");
            terminal.WriteLine("Press Enter to continue...");
            await terminal.ReadKeyAsync();
            return;
        }

        // Display offer
        long salary = GameConfig.BaseGuardSalary + (currentPlayer.Level * 20);
        terminal.SetColor("white");
        terminal.WriteLine($"The captain of the guard reviews your credentials...");
        terminal.WriteLine("");
        terminal.SetColor("bright_green");
        terminal.WriteLine($"\"Impressive, {currentPlayer.DisplayName}! Level {currentPlayer.Level}, with notable deeds.\"");
        terminal.WriteLine("");
        terminal.SetColor("cyan");
        terminal.WriteLine($"Position: Royal Guard");
        terminal.WriteLine($"Daily Salary: {salary:N0} gold");
        terminal.WriteLine($"Current Guards: {currentKing.Guards.Count}/{GameConfig.MaxRoyalGuards}");
        terminal.WriteLine("");
        terminal.SetColor("yellow");
        terminal.WriteLine("As a Royal Guard, you will:");
        terminal.WriteLine("  - Defend the throne against usurpers");
        terminal.WriteLine("  - Receive a daily salary from the treasury");
        terminal.WriteLine("  - Gain prestige and the king's favor");
        terminal.WriteLine("");

        terminal.SetColor("bright_cyan");
        terminal.Write("Do you wish to join the Royal Guard? (Y/N): ");
        terminal.SetColor("white");
        string response = await terminal.ReadLineAsync();

        if (response?.ToUpper() == "Y")
        {
            // Add player as a guard
            var guard = new RoyalGuard
            {
                Name = currentPlayer.DisplayName,
                AI = CharacterAI.Human,
                Sex = currentPlayer.Sex,
                DailySalary = salary,
                RecruitmentDate = DateTime.Now,
                Loyalty = 100,
                IsActive = true
            };
            currentKing.Guards.Add(guard);

            terminal.WriteLine("");
            terminal.SetColor("bright_green");
            terminal.WriteLine("The captain smiles and extends his hand.");
            terminal.WriteLine($"\"Welcome to the Royal Guard, {currentPlayer.DisplayName}!\"");
            terminal.WriteLine("");
            terminal.SetColor("bright_yellow");
            terminal.WriteLine($"You are now a member of {currentKing.GetTitle()} {currentKing.Name}'s Royal Guard!");

            // News announcement
            NewsSystem.Instance?.Newsy(true, $"{currentPlayer.DisplayName} has joined the Royal Guard!");

            // Small chivalry boost
            currentPlayer.Chivalry += 10;
        }
        else
        {
            terminal.WriteLine("");
            terminal.SetColor("gray");
            terminal.WriteLine("The captain nods understandingly.");
            terminal.WriteLine("\"The offer stands, should you change your mind.\"");
        }

        terminal.WriteLine("");
        terminal.SetColor("darkgray");
        terminal.WriteLine("Press Enter to continue...");
        await terminal.ReadKeyAsync();
    }

    // ═══════════════════════════════════════════════════════════════════════════
    // UTILITY
    // ═══════════════════════════════════════════════════════════════════════════

    /// <summary>
    /// Persist current royal court state to world_state (online mode only).
    /// Called after any action that modifies king state: throne challenges,
    /// tax policy changes, treasury deposits/withdrawals, etc.
    /// This ensures the world sim and other player sessions see the change immediately.
    /// </summary>
    private void PersistRoyalCourtToWorldState()
    {
        if (!UsurperRemake.BBS.DoorMode.IsOnlineMode) return;
        var osm = OnlineStateManager.Instance;
        if (osm == null) return;

        // Fire-and-forget - don't block the UI for a DB write
        _ = Task.Run(async () =>
        {
            try { await osm.SaveRoyalCourtToWorldState(); }
            catch (Exception ex)
            {
                DebugLogger.Instance.LogError("CASTLE", $"Failed to persist royal court: {ex.Message}");
            }
        });
    }

    private void LoadKingData()
    {
        // If player is king but no king data exists, create it
        if (currentKing == null && playerIsKing)
        {
            currentKing = King.CreateNewKing(currentPlayer.DisplayName, CharacterAI.Human, currentPlayer.Sex);
            return;
        }

        // If no king exists at all, trigger NPC succession
        if (currentKing == null || !currentKing.IsActive)
        {
            TriggerNPCSuccession();
        }
    }

    /// <summary>
    /// When throne is empty, find the strongest/most worthy NPC to claim it
    /// </summary>
    private void TriggerNPCSuccession()
    {
        var npcs = NPCSpawnSystem.Instance.ActiveNPCs;
        if (npcs == null || npcs.Count == 0)
            return;

        // Check designated heir first
        var designatedHeirName = currentKing?.DesignatedHeir;
        if (!string.IsNullOrEmpty(designatedHeirName))
        {
            var heir = npcs.FirstOrDefault(n => n.Name == designatedHeirName &&
                n.IsAlive && !n.IsDead && n.DaysInPrison <= 0 && n.Level >= GameConfig.MinLevelKing);
            if (heir != null)
            {
                CrownNPC(heir);
                NewsSystem.Instance?.Newsy(true, $"The designated heir {heir.DisplayName} has ascended to the throne as {(heir.Sex == CharacterSex.Male ? "King" : "Queen")}!");
                return;
            }
            else
            {
                NewsSystem.Instance?.Newsy(false, $"The designated heir {designatedHeirName} could not be found or is not eligible for the throne.");
            }
        }

        // Fallback: Find the most worthy NPC based on level, alignment (good preferred), and class
        var candidates = npcs
            .Where(npc => npc.IsAlive && npc.Level >= GameConfig.MinLevelKing)
            .OrderByDescending(npc => CalculateSuccessionScore(npc))
            .ToList();

        if (candidates.Count == 0)
            return;

        // Crown the highest scoring NPC
        var newMonarch = candidates.First();
        CrownNPC(newMonarch);

        // Announce succession
        NewsSystem.Instance.Newsy(true, $"{newMonarch.DisplayName} has claimed the throne and been crowned {(newMonarch.Sex == CharacterSex.Male ? "King" : "Queen")}!");
    }

    /// <summary>
    /// Calculate an NPC's worthiness to become monarch
    /// </summary>
    private int CalculateSuccessionScore(NPC npc)
    {
        int score = npc.Level * 10;

        // Paladins and Clerics are more worthy
        if (npc.Class == CharacterClass.Paladin) score += 50;
        if (npc.Class == CharacterClass.Cleric) score += 30;
        if (npc.Class == CharacterClass.Warrior) score += 20;

        // Higher charisma = better leader
        score += (int)(npc.Charisma / 2);

        // Good alignment preferred (Chivalry - Darkness)
        long alignment = npc.Chivalry - npc.Darkness;
        if (alignment > 0) score += (int)Math.Min(alignment, 100);

        // Wealth helps
        score += (int)(npc.Gold / 10000);

        return score;
    }

    /// <summary>
    /// Crown an NPC as the new monarch
    /// </summary>
    private void CrownNPC(NPC npc)
    {
        ClearRoyalMarriage(currentKing); // Clear old king's royal spouse before crowning NPC
        currentKing = new King
        {
            Name = npc.DisplayName,
            AI = CharacterAI.Computer,
            Sex = npc.Sex,
            IsActive = true,
            CoronationDate = DateTime.Now,
            Treasury = Math.Max(10000, npc.Gold / 2), // NPC donates half their gold to treasury
            TotalReign = 0
        };

        // Record the coronation
        monarchHistory.Add(new MonarchRecord
        {
            Name = npc.DisplayName,
            Title = npc.Sex == CharacterSex.Male ? "King" : "Queen",
            CoronationDate = DateTime.Now,
            DaysReigned = 0,
            EndReason = ""
        });
        while (monarchHistory.Count > MaxMonarchHistory)
            monarchHistory.RemoveAt(0);

        // Persist to world_state so world sim picks up the new NPC king
        PersistRoyalCourtToWorldState();
    }

    /// <summary>
    /// Get the current king (for external access)
    /// </summary>
    public static King GetCurrentKing() => currentKing;

    /// <summary>
    /// Get the monarch history for serialization
    /// </summary>
    public static List<MonarchRecord> GetMonarchHistory() => monarchHistory;

    /// <summary>
    /// Set the monarch history from deserialized data
    /// </summary>
    public static void SetMonarchHistory(List<MonarchRecord> history)
    {
        monarchHistory = history ?? new List<MonarchRecord>();
    }

    /// <summary>
    /// Notify a dethroned player via system message.
    /// Their King flag will sync from world_state on next login or castle entry.
    /// </summary>
    private void NotifyDethronedPlayer(string oldKingName, string newKingName, string reason)
    {
        if (!UsurperRemake.BBS.DoorMode.IsOnlineMode) return;

        _ = Task.Run(async () =>
        {
            try
            {
                var backend = SaveSystem.Instance.Backend as SqlSaveBackend;
                if (backend == null) return;

                await backend.SendMessage("System", oldKingName, "system",
                    $"You have been DETHRONED! {newKingName} {reason} and now sits on the throne. " +
                    $"Your reign has ended.");
            }
            catch (Exception ex)
            {
                DebugLogger.Instance.LogError("CASTLE", $"Failed to notify dethroned player {oldKingName}: {ex.Message}");
            }
        });
    }

    /// <summary>
    /// Clear the outgoing king's royal marriage state (spouse NPC + marriage registry).
    /// Must be called BEFORE overwriting currentKing when the throne changes hands.
    /// </summary>
    private static void ClearRoyalMarriage(King outgoingKing)
    {
        if (outgoingKing?.Spouse == null) return;

        var spouseName = outgoingKing.Spouse.Name;
        outgoingKing.Spouse = null;

        // Clear marriage state on the NPC
        var spouseNPC = NPCSpawnSystem.Instance?.ActiveNPCs?.FirstOrDefault(n => n.Name == spouseName);
        if (spouseNPC != null)
        {
            spouseNPC.Married = false;
            spouseNPC.IsMarried = false;
            spouseNPC.SpouseName = "";
        }

        // Clear marriage registry entry for the old king
        NPCMarriageRegistry.Instance?.EndMarriage(outgoingKing.Name);

        DebugLogger.Instance.LogInfo("CASTLE", $"Cleared royal marriage to {spouseName} (throne changed)");
    }

    /// <summary>
    /// Set the king (for save/load)
    /// </summary>
    public static void SetKing(King king) => currentKing = king;

    /// <summary>
    /// Set an NPC as the current king (for save restoration)
    /// </summary>
    public static void SetCurrentKing(NPC npc)
    {
        if (npc == null)
        {
            currentKing = null;
            return;
        }

        currentKing = new King
        {
            Name = npc.Name,
            AI = CharacterAI.Computer,
            Sex = npc.Sex,
            IsActive = true,
            CoronationDate = DateTime.Now
        };

        // GD.Print($"[Castle] {npc.Name} has been restored as monarch");
    }

    /// <summary>
    /// Called when the current king dies. Vacates the throne and posts news.
    /// Static so it can be called from WorldSimulator without a CastleLocation instance.
    /// </summary>
    /// <summary>
    /// Automatically abdicate the throne for a player who is ascending to godhood or rerolling.
    /// Static so it can be called from EndingsSystem and PantheonLocation.
    /// </summary>
    public static void AbdicatePlayerThrone(Character player, string reason)
    {
        if (player == null || !player.King) return;

        var king = GetCurrentKing();

        // Record monarch history
        if (king != null)
        {
            monarchHistory.Add(new MonarchRecord
            {
                Name = king.Name,
                Title = king.GetTitle(),
                DaysReigned = (int)king.TotalReign,
                CoronationDate = king.CoronationDate,
                EndReason = reason
            });
            while (monarchHistory.Count > MaxMonarchHistory)
                monarchHistory.RemoveAt(0);

            ClearRoyalMarriage(king);
            king.IsActive = false;
        }

        // Clear player state
        player.King = false;
        player.RoyalMercenaries?.Clear();
        player.RecalculateStats();

        currentKing = null;

        // News
        NewsSystem.Instance?.Newsy(true, $"{player.DisplayName} has {reason}! The kingdom is in chaos!");

        // Trigger NPC succession (uses only static fields + singletons)
        var npcs = NPCSpawnSystem.Instance?.ActiveNPCs;
        if (npcs != null && npcs.Count > 0)
        {
            var candidates = npcs
                .Where(npc => npc.IsAlive && npc.Level >= GameConfig.MinLevelKing)
                .OrderByDescending(npc => npc.Level * 10 + (int)(npc.Charisma / 2))
                .ToList();

            if (candidates.Count > 0)
            {
                var newMonarch = candidates.First();
                currentKing = new King
                {
                    Name = newMonarch.DisplayName,
                    AI = CharacterAI.Computer,
                    Sex = newMonarch.Sex,
                    IsActive = true,
                    CoronationDate = DateTime.Now,
                    Treasury = Math.Max(10000, newMonarch.Gold / 2),
                    TotalReign = 0
                };
                NewsSystem.Instance?.Newsy(true, $"{newMonarch.DisplayName} has claimed the throne!");
            }
        }

        // Persist to world_state in online mode
        if (UsurperRemake.BBS.DoorMode.IsOnlineMode)
        {
            var osm = OnlineStateManager.Instance;
            if (osm != null)
            {
                _ = Task.Run(async () =>
                {
                    try { await osm.SaveRoyalCourtToWorldState(); }
                    catch (Exception ex)
                    {
                        DebugLogger.Instance.LogError("CASTLE", $"Failed to persist abdication: {ex.Message}");
                    }
                });
            }
        }
    }

    public static void VacateThrone(string reason)
    {
        var king = GetCurrentKing();
        if (king == null || !king.IsActive) return;

        string kingName = king.Name;
        king.IsActive = false;

        // Post news
        NewsSystem.Instance?.Newsy(true, $"{kingName} is no longer ruler! The throne stands vacant. {reason}");

        // Persist to world_state in online mode
        if (UsurperRemake.BBS.DoorMode.IsOnlineMode)
        {
            var osm = OnlineStateManager.Instance;
            if (osm != null)
            {
                _ = Task.Run(async () =>
                {
                    try { await osm.SaveRoyalCourtToWorldState(); }
                    catch (Exception ex)
                    {
                        DebugLogger.Instance.LogError("CASTLE", $"Failed to persist throne vacancy: {ex.Message}");
                    }
                });
            }
        }
    }

    #region The Crown Faction Recruitment

    /// <summary>
    /// Show The Crown faction recruitment UI
    /// Meet the Royal Chancellor and potentially join The Crown
    /// </summary>
    // ═══════════════════════════════════════════════════════════════════════════
    // ROYAL ARMORY (Crown faction exclusive)
    // ═══════════════════════════════════════════════════════════════════════════

    private async Task VisitRoyalArmory()
    {
        if (FactionSystem.Instance?.HasCastleAccess() != true)
        {
            terminal.SetColor("red");
            terminal.WriteLine("\n  The Royal Armory is restricted to Crown members.");
            await Task.Delay(2000);
            return;
        }

        terminal.ClearScreen();
        terminal.SetColor("bright_yellow");
        terminal.WriteLine("╔══════════════════════════════════════════════════════════════════╗");
        terminal.WriteLine("║                       THE ROYAL ARMORY                            ║");
        terminal.WriteLine("╚══════════════════════════════════════════════════════════════════╝");
        terminal.WriteLine("");

        terminal.SetColor("gray");
        terminal.WriteLine("  Racks of polished weapons and gleaming armor line the walls.");
        terminal.WriteLine("  A Royal Quartermaster stands at attention.");
        terminal.SetColor("yellow");
        terminal.WriteLine($"\n  Gold: {currentPlayer.Gold:N0}");
        terminal.WriteLine("");

        int level = currentPlayer.Level;

        // Define armory items with level-scaled prices and stats
        long crownBladePrice = 50000 + level * 500;
        long royalPlatePrice = 45000 + level * 400;
        long crownShieldPrice = 30000 + level * 300;
        long signetRingPrice = 25000 + level * 200;

        int bladeWeapPow = 150 + level * 2;
        int plateArmPow = 120 + level * 2;
        int shieldArmPow = 80 + level;

        terminal.SetColor("bright_white");
        terminal.WriteLine("  #  Item                  Price        Stats");
        terminal.SetColor("darkgray");
        terminal.WriteLine("  ─────────────────────────────────────────────────────────");

        string bladeColor = currentPlayer.Gold >= crownBladePrice ? "white" : "darkgray";
        terminal.SetColor("darkgray");
        terminal.Write("  [");
        terminal.SetColor("bright_yellow");
        terminal.Write("1");
        terminal.SetColor("darkgray");
        terminal.Write("] ");
        terminal.SetColor(bladeColor);
        terminal.WriteLine($"Crown Blade         {crownBladePrice,10:N0}g   WeapPow {bladeWeapPow}");

        string plateColor = currentPlayer.Gold >= royalPlatePrice ? "white" : "darkgray";
        terminal.SetColor("darkgray");
        terminal.Write("  [");
        terminal.SetColor("bright_yellow");
        terminal.Write("2");
        terminal.SetColor("darkgray");
        terminal.Write("] ");
        terminal.SetColor(plateColor);
        terminal.WriteLine($"Royal Guard Plate   {royalPlatePrice,10:N0}g   ArmPow {plateArmPow}");

        string shieldColor = currentPlayer.Gold >= crownShieldPrice ? "white" : "darkgray";
        terminal.SetColor("darkgray");
        terminal.Write("  [");
        terminal.SetColor("bright_yellow");
        terminal.Write("3");
        terminal.SetColor("darkgray");
        terminal.Write("] ");
        terminal.SetColor(shieldColor);
        terminal.WriteLine($"Crown Shield        {crownShieldPrice,10:N0}g   ArmPow {shieldArmPow}");

        string ringColor = currentPlayer.Gold >= signetRingPrice ? "white" : "darkgray";
        terminal.SetColor("darkgray");
        terminal.Write("  [");
        terminal.SetColor("bright_yellow");
        terminal.Write("4");
        terminal.SetColor("darkgray");
        terminal.Write("] ");
        terminal.SetColor(ringColor);
        terminal.WriteLine($"Signet Ring         {signetRingPrice,10:N0}g   +5 CHA, +5 STR");

        terminal.SetColor("darkgray");
        terminal.Write("  [");
        terminal.SetColor("bright_yellow");
        terminal.Write("0");
        terminal.SetColor("darkgray");
        terminal.Write("] ");
        terminal.SetColor("white");
        terminal.WriteLine("Leave");
        terminal.WriteLine("");

        var input = await terminal.GetInput("  Purchase? ");

        Equipment item = null;
        long price = 0;
        string itemName = "";

        switch (input.Trim())
        {
            case "1":
                price = crownBladePrice;
                itemName = "Crown Blade";
                item = new Equipment
                {
                    Name = "Crown Blade",
                    Slot = EquipmentSlot.MainHand,
                    Handedness = WeaponHandedness.OneHanded,
                    WeaponType = WeaponType.Sword,
                    WeaponPower = bladeWeapPow,
                    Value = price,
                    Rarity = EquipmentRarity.Legendary,
                    Description = "A blade forged in the royal foundry, reserved for Crown elite.",
                    MinLevel = Math.Max(1, level - 5)
                };
                break;
            case "2":
                price = royalPlatePrice;
                itemName = "Royal Guard Plate";
                item = new Equipment
                {
                    Name = "Royal Guard Plate",
                    Slot = EquipmentSlot.Body,
                    ArmorType = ArmorType.Plate,
                    ArmorClass = plateArmPow,
                    Value = price,
                    Rarity = EquipmentRarity.Legendary,
                    Description = "Full plate armor bearing the royal crest.",
                    MinLevel = Math.Max(1, level - 5)
                };
                break;
            case "3":
                price = crownShieldPrice;
                itemName = "Crown Shield";
                item = new Equipment
                {
                    Name = "Crown Shield",
                    Slot = EquipmentSlot.OffHand,
                    ShieldBonus = shieldArmPow,
                    BlockChance = 20,
                    Value = price,
                    Rarity = EquipmentRarity.Epic,
                    Description = "A tower shield emblazoned with the royal seal.",
                    MinLevel = Math.Max(1, level - 5)
                };
                break;
            case "4":
                price = signetRingPrice;
                itemName = "Signet Ring";
                item = new Equipment
                {
                    Name = "Royal Signet Ring",
                    Slot = EquipmentSlot.LFinger,
                    CharismaBonus = 5,
                    StrengthBonus = 5,
                    Value = price,
                    Rarity = EquipmentRarity.Epic,
                    Description = "A heavy gold ring bearing the royal seal.",
                    MinLevel = Math.Max(1, level - 5)
                };
                break;
            default:
                return;
        }

        if (currentPlayer.Gold < price)
        {
            terminal.SetColor("red");
            terminal.WriteLine($"\n  You cant afford the {itemName}.");
            await Task.Delay(2000);
            return;
        }

        currentPlayer.Gold -= price;
        currentPlayer.Statistics?.RecordPurchase(price);
        currentPlayer.Statistics?.RecordGoldSpent(price);

        // Register as dynamic equipment so it gets a valid ID for EquippedItems lookup
        EquipmentDatabase.RegisterDynamic(item);

        // Equip directly
        if (currentPlayer.EquipItem(item, null, out string equipMsg))
        {
            if (!string.IsNullOrEmpty(equipMsg))
            {
                terminal.SetColor("gray");
                terminal.WriteLine($"  {equipMsg}");
            }
        }

        terminal.SetColor("bright_green");
        terminal.WriteLine($"\n  The Quartermaster presents you with the {itemName}.");
        terminal.SetColor("yellow");
        terminal.WriteLine("  \"Wear it with honor.\"");
        terminal.WriteLine("");

        await terminal.PressAnyKey();
    }

    private async Task ShowCrownRecruitment()
    {
        var factionSystem = UsurperRemake.Systems.FactionSystem.Instance;

        terminal.ClearScreen();
        terminal.SetColor("bright_yellow");
        terminal.WriteLine("╔══════════════════════════════════════════════════════════════════════════════╗");
        terminal.WriteLine("║                              THE CROWN                                       ║");
        terminal.WriteLine("╚══════════════════════════════════════════════════════════════════════════════╝");
        terminal.WriteLine("");

        terminal.SetColor("white");
        terminal.WriteLine("A royal herald leads you through gilded corridors to an ornate");
        terminal.WriteLine("chamber where the Royal Chancellor awaits at a great oak desk.");
        terminal.WriteLine("");
        await Task.Delay(1500);

        terminal.SetColor("bright_cyan");
        terminal.WriteLine("\"Ah, a prospective servant of the realm,\" the Chancellor says,");
        terminal.WriteLine("adjusting his monocle to study you carefully.");
        terminal.WriteLine("");
        await Task.Delay(1500);

        // Check if already in a faction
        if (factionSystem.PlayerFaction != null)
        {
            terminal.SetColor("yellow");
            terminal.WriteLine("The Chancellor's expression hardens.");
            terminal.WriteLine("");
            terminal.SetColor("bright_cyan");
            terminal.WriteLine($"\"Our records show you have sworn allegiance to {UsurperRemake.Systems.FactionSystem.Factions[factionSystem.PlayerFaction.Value].Name}.\"");
            terminal.WriteLine("\"The Crown does not accept those with divided loyalties.\"");
            terminal.WriteLine("\"Should you ever renounce your current masters, return here.\"");
            terminal.WriteLine("");
            await terminal.GetInputAsync("Press Enter to continue...");
            return;
        }

        terminal.SetColor("white");
        terminal.WriteLine("The Chancellor gestures to tapestries depicting the kingdom's history.");
        terminal.WriteLine("");
        terminal.SetColor("bright_cyan");
        terminal.WriteLine("\"The Crown represents order, stability, and justice.\"");
        terminal.WriteLine("\"When the Old Gods fell to corruption, it was The Crown\"");
        terminal.WriteLine("\"that held the kingdom together. Law. Structure. Purpose.\"");
        terminal.WriteLine("");
        await Task.Delay(2000);

        terminal.SetColor("cyan");
        terminal.WriteLine("\"We do not kneel to gods who abandoned us.\"");
        terminal.WriteLine("\"We do not skulk in shadows like common thieves.\"");
        terminal.WriteLine("\"We BUILD. We PROTECT. We GOVERN.\"");
        terminal.WriteLine("");
        await Task.Delay(1500);

        // Show faction benefits
        terminal.SetColor("bright_yellow");
        terminal.WriteLine("═══ Benefits of The Crown ═══");
        terminal.SetColor("white");
        terminal.WriteLine("• 10% discount at all legitimate shops");
        terminal.WriteLine("• Access to Royal Guard positions and military resources");
        terminal.WriteLine("• Friendly treatment from guards and noble NPCs");
        terminal.WriteLine("• Priority audience with the ruling monarch");
        terminal.WriteLine("");

        // Check requirements
        var (canJoin, reason) = factionSystem.CanJoinFaction(UsurperRemake.Systems.Faction.TheCrown, currentPlayer);

        if (!canJoin)
        {
            terminal.SetColor("red");
            terminal.WriteLine("═══ Requirements Not Met ═══");
            terminal.SetColor("yellow");
            terminal.WriteLine(reason);
            terminal.WriteLine("");
            terminal.SetColor("gray");
            terminal.WriteLine("The Crown requires:");
            terminal.WriteLine("• Level 10 or higher");
            terminal.WriteLine("• Chivalry 500+ (prove your honor through noble deeds)");
            terminal.WriteLine("• Darkness below 500 (no criminal record)");
            terminal.WriteLine($"  Your Chivalry: {currentPlayer.Chivalry}");
            terminal.WriteLine($"  Your Darkness: {currentPlayer.Darkness}");
            terminal.WriteLine("");
            terminal.SetColor("bright_cyan");
            terminal.WriteLine("\"Your reputation precedes you,\" the Chancellor says coolly.");
            if (currentPlayer.Darkness > 500)
            {
                terminal.WriteLine("\"The Crown does not associate with criminals.\"");
            }
            else
            {
                terminal.WriteLine("\"Prove your worth. Perform honorable deeds. Then return.\"");
            }
            await terminal.GetInputAsync("Press Enter to continue...");
            return;
        }

        // Can join - offer the choice
        terminal.SetColor("bright_green");
        terminal.WriteLine("═══ Requirements Met ═══");
        terminal.SetColor("white");
        terminal.WriteLine("The Chancellor rises and extends a scroll sealed with the royal crest.");
        terminal.WriteLine("");
        terminal.SetColor("bright_cyan");
        terminal.WriteLine("\"Your record speaks well of you. Honorable. Disciplined.\"");
        terminal.WriteLine("\"Will you swear the Oath of Service and join The Crown?\"");
        terminal.WriteLine("");
        terminal.SetColor("yellow");
        terminal.WriteLine("WARNING: Joining The Crown will:");
        terminal.WriteLine("• Lock you out of The Faith and The Shadows");
        terminal.WriteLine("• Decrease standing with rival factions by 100");
        terminal.WriteLine("");

        var choice = await terminal.GetInputAsync("Join The Crown? (Y/N) ");

        if (choice.ToUpper() == "Y")
        {
            await PerformCrownOath(factionSystem);
        }
        else
        {
            terminal.WriteLine("");
            terminal.SetColor("cyan");
            terminal.WriteLine("The Chancellor nods curtly.");
            terminal.SetColor("bright_cyan");
            terminal.WriteLine("\"A wise soul takes time to consider such oaths.\"");
            terminal.WriteLine("\"The Crown's gates remain open to those of noble heart.\"");
        }

        await terminal.GetInputAsync("Press Enter to continue...");
    }

    /// <summary>
    /// Perform the oath ceremony to join The Crown
    /// </summary>
    private async Task PerformCrownOath(UsurperRemake.Systems.FactionSystem factionSystem)
    {
        terminal.ClearScreen();
        terminal.SetColor("bright_yellow");
        terminal.WriteLine("╔══════════════════════════════════════════════════════════════════════════════╗");
        terminal.WriteLine("║                         THE OATH OF SERVICE                                  ║");
        terminal.WriteLine("╚══════════════════════════════════════════════════════════════════════════════╝");
        terminal.WriteLine("");

        terminal.SetColor("white");
        terminal.WriteLine("You are led to the throne room, where officials have gathered.");
        terminal.WriteLine("The royal banner hangs above - a golden crown on crimson field.");
        terminal.WriteLine("");
        await Task.Delay(1500);

        terminal.SetColor("cyan");
        terminal.WriteLine("You kneel before the great seal of the kingdom.");
        terminal.WriteLine("The Chancellor stands before you, scroll in hand.");
        terminal.WriteLine("");
        await Task.Delay(1500);

        terminal.SetColor("bright_cyan");
        terminal.WriteLine("\"Repeat the Oath of Service:\"");
        terminal.WriteLine("");
        await Task.Delay(1000);

        terminal.SetColor("yellow");
        terminal.WriteLine("\"I pledge my sword, my honor, and my life to The Crown.\"");
        await Task.Delay(1200);
        terminal.WriteLine("\"I will uphold law and order in all my dealings.\"");
        await Task.Delay(1200);
        terminal.WriteLine("\"I will defend the realm against chaos and corruption.\"");
        await Task.Delay(1200);
        terminal.WriteLine("\"In service to the kingdom, I shall not falter.\"");
        terminal.WriteLine("");
        await Task.Delay(1500);

        terminal.SetColor("white");
        terminal.WriteLine("The Chancellor places the royal seal upon your oath.");
        terminal.WriteLine("Trumpets sound. The gathered officials applaud.");
        terminal.WriteLine("");
        await Task.Delay(1500);

        // Actually join the faction
        factionSystem.JoinFaction(UsurperRemake.Systems.Faction.TheCrown, currentPlayer);

        terminal.SetColor("bright_green");
        terminal.WriteLine("╔══════════════════════════════════════════════════════════════════════════════╗");
        terminal.WriteLine("║              YOU HAVE JOINED THE CROWN                                       ║");
        terminal.WriteLine("╚══════════════════════════════════════════════════════════════════════════════╝");
        terminal.WriteLine("");

        terminal.SetColor("bright_cyan");
        terminal.WriteLine("\"Rise, servant of the Crown,\" the Chancellor declares.");
        terminal.WriteLine("\"Your loyalty will be rewarded. Your service, remembered.\"");
        terminal.WriteLine("");

        terminal.SetColor("white");
        terminal.WriteLine("As a member of The Crown, you will receive:");
        terminal.SetColor("bright_green");
        terminal.WriteLine("• 10% discount at all shops in the kingdom");
        terminal.WriteLine("• Recognition from guards and royal officials");
        terminal.WriteLine("• Access to Crown-only opportunities and resources");
        terminal.WriteLine("");

        // Generate news
        NewsSystem.Instance.Newsy(true, $"{currentPlayer.Name2} has sworn the Oath of Service and joined The Crown!");

        // Log to debug
        UsurperRemake.Systems.DebugLogger.Instance.LogInfo("FACTION", $"{currentPlayer.Name2} joined The Crown");
    }

    #endregion

    // ═══════════════════════════════════════════════════════════════════════
    // Castle Siege (Online Mode — Team-Based Assault)
    // ═══════════════════════════════════════════════════════════════════════

    private async Task CastleSiegeMenu()
    {
        terminal.ClearScreen();
        terminal.SetColor("bright_red");
        terminal.WriteLine("╔══════════════════════════════════════════════════════════════════════════════╗");
        terminal.WriteLine("║                           CASTLE SIEGE                                      ║");
        terminal.WriteLine("╚══════════════════════════════════════════════════════════════════════════════╝");
        terminal.WriteLine("");

        var backend = SaveSystem.Instance?.Backend as SqlSaveBackend;
        if (backend == null)
        {
            terminal.SetColor("red");
            terminal.WriteLine("Online backend not available.");
            await terminal.PressAnyKey();
            return;
        }

        // Must be on a team
        if (string.IsNullOrEmpty(currentPlayer.Team))
        {
            terminal.SetColor("yellow");
            terminal.WriteLine("You must be on a team to siege the castle!");
            terminal.WriteLine("Visit the Team Corner to create or join a team.");
            await terminal.PressAnyKey();
            return;
        }

        // Must have a king to siege
        if (currentKing == null || !currentKing.IsActive)
        {
            terminal.SetColor("yellow");
            terminal.WriteLine("There is no king to overthrow! The throne sits empty.");
            terminal.WriteLine("Use [C]laim Empty Throne instead.");
            await terminal.PressAnyKey();
            return;
        }

        // Can't siege your own throne
        if (currentKing.Name.Equals(currentPlayer.DisplayName, StringComparison.OrdinalIgnoreCase))
        {
            terminal.SetColor("yellow");
            terminal.WriteLine("You cannot siege your own castle!");
            await terminal.PressAnyKey();
            return;
        }

        // 24h cooldown
        if (!backend.CanTeamSiege(currentPlayer.Team))
        {
            terminal.SetColor("yellow");
            terminal.WriteLine("Your team has already attempted a siege in the last 24 hours.");
            terminal.WriteLine("The castle defenses are on high alert. Try again later.");
            await terminal.PressAnyKey();
            return;
        }

        // Minimum level
        if (currentPlayer.Level < GameConfig.MinLevelKing)
        {
            terminal.SetColor("yellow");
            terminal.WriteLine($"You must be at least level {GameConfig.MinLevelKing} to lead a siege.");
            await terminal.PressAnyKey();
            return;
        }

        // Show siege info
        terminal.SetColor("white");
        terminal.WriteLine($"Your team '{currentPlayer.Team}' prepares to storm the castle!");
        terminal.WriteLine("");

        int totalGuards = currentKing.TotalGuardCount;
        terminal.SetColor("cyan");
        terminal.WriteLine($"  King: {currentKing.GetTitle()} {currentKing.Name}");
        terminal.SetColor("red");
        terminal.WriteLine($"  Monster Guards: {currentKing.MonsterGuards.Count}");
        terminal.SetColor("yellow");
        terminal.WriteLine($"  Royal Guards: {currentKing.Guards.Count}");
        terminal.SetColor("white");
        terminal.WriteLine($"  Total Defenders: {totalGuards}");
        terminal.WriteLine("");

        // Load team members for the assault
        var teamMembers = await backend.GetPlayerTeamMembers(currentPlayer.Team, currentPlayer.DisplayName);
        terminal.SetColor("bright_cyan");
        terminal.WriteLine("Your siege force:");
        terminal.SetColor("bright_green");
        terminal.WriteLine($"  YOU — {currentPlayer.DisplayName} (Lv {currentPlayer.Level} {currentPlayer.Class})");
        foreach (var member in teamMembers)
        {
            terminal.SetColor("cyan");
            terminal.WriteLine($"  {member.DisplayName} (Lv {member.Level})");
        }
        terminal.WriteLine("");

        terminal.SetColor("bright_red");
        terminal.Write("Launch the siege? (Y/N): ");
        terminal.SetColor("white");
        string confirm = await terminal.ReadLineAsync();

        if (confirm?.ToUpper() != "Y")
        {
            terminal.SetColor("gray");
            terminal.WriteLine("Your team stands down. Perhaps another day...");
            await Task.Delay(2000);
            return;
        }

        // Start siege in DB
        int siegeId = await backend.StartCastleSiege(currentPlayer.Team, Math.Max(1, totalGuards));

        terminal.ClearScreen();
        terminal.SetColor("bright_red");
        terminal.WriteLine("═══════════════════════════════════════════════════════════════");
        terminal.WriteLine("           THE SIEGE BEGINS!");
        terminal.WriteLine("═══════════════════════════════════════════════════════════════");
        terminal.WriteLine("");

        // Calculate team power
        long teamPower = currentPlayer.Strength + currentPlayer.WeapPow + currentPlayer.Level * 5;
        long teamDefense = currentPlayer.Defence + currentPlayer.ArmPow;
        long teamHP = currentPlayer.HP;
        int memberCount = 1 + teamMembers.Count;

        foreach (var member in teamMembers)
        {
            // Each member adds combat power
            teamPower += member.Level * 8;
            teamDefense += member.Level * 3;
            teamHP += member.Level * 20;
        }

        int guardsDefeated = 0;
        bool siegeFailed = false;

        // PHASE 1: Monster Guards
        foreach (var monster in currentKing.MonsterGuards.ToList())
        {
            terminal.SetColor("bright_red");
            terminal.WriteLine($">>> Monster Guard: {monster.Name} (Level {monster.Level}) blocks the path! <<<");
            terminal.WriteLine("");

            long monsterHP = monster.HP;
            long monsterStr = monster.Strength + monster.WeapPow;
            long monsterDef = monster.Defence;

            int rounds = 0;
            while (monsterHP > 0 && teamHP > 0 && rounds < 20)
            {
                rounds++;

                // Team attacks (combined)
                long teamDmg = Math.Max(1, teamPower - monsterDef);
                teamDmg = (long)(teamDmg * (0.8 + random.NextDouble() * 0.4));
                monsterHP -= teamDmg;

                terminal.SetColor("bright_green");
                terminal.WriteLine($"  Your team strikes {monster.Name} for {teamDmg} damage! (HP: {Math.Max(0, monsterHP)})");

                if (monsterHP <= 0) break;

                // Monster retaliates
                long monsterDmg = Math.Max(1, monsterStr - teamDefense / memberCount);
                monsterDmg = (long)(monsterDmg * (0.8 + random.NextDouble() * 0.4));
                teamHP -= monsterDmg;

                terminal.SetColor("red");
                terminal.WriteLine($"  {monster.Name} strikes back for {monsterDmg}! (Team HP: {Math.Max(0, teamHP)})");

                await Task.Delay(250);
            }

            if (teamHP <= 0)
            {
                terminal.SetColor("red");
                terminal.WriteLine($"Your siege force was overwhelmed by {monster.Name}!");
                siegeFailed = true;
                break;
            }
            else
            {
                guardsDefeated++;
                currentKing.MonsterGuards.Remove(monster);
                terminal.SetColor("bright_green");
                terminal.WriteLine($"  {monster.Name} has been defeated!");
                terminal.WriteLine("");
                await Task.Delay(500);
            }
        }

        // PHASE 2: NPC Guards
        if (!siegeFailed)
        {
            foreach (var guard in currentKing.Guards.ToList())
            {
                terminal.SetColor("yellow");
                terminal.WriteLine($">>> Royal Guard: {guard.Name} (Loyalty: {guard.Loyalty}%) stands firm! <<<");
                terminal.WriteLine("");

                // Low loyalty guards may surrender during siege
                if (guard.Loyalty < 25 && random.Next(100) < 40)
                {
                    terminal.SetColor("bright_yellow");
                    terminal.WriteLine($"  {guard.Name} throws down their weapon and surrenders!");
                    guardsDefeated++;
                    currentKing.Guards.Remove(guard);
                    await Task.Delay(500);
                    continue;
                }

                var actualNpc = NPCSpawnSystem.Instance?.GetNPCByName(guard.Name);
                long guardStr = actualNpc?.Strength ?? (50 + random.Next(50));
                long guardHP = actualNpc?.HP ?? (200 + random.Next(200));
                long guardDef = actualNpc?.Defence ?? 20;
                float loyaltyMod = guard.Loyalty / 100f;
                guardStr = (long)(guardStr * loyaltyMod);

                int rounds = 0;
                while (guardHP > 0 && teamHP > 0 && rounds < 20)
                {
                    rounds++;

                    long teamDmg = Math.Max(1, teamPower - guardDef);
                    teamDmg = (long)(teamDmg * (0.8 + random.NextDouble() * 0.4));
                    guardHP -= teamDmg;

                    terminal.SetColor("bright_green");
                    terminal.WriteLine($"  Your team strikes {guard.Name} for {teamDmg}! (HP: {Math.Max(0, guardHP)})");

                    if (guardHP <= 0) break;

                    long guardDmg = Math.Max(1, guardStr - teamDefense / memberCount);
                    guardDmg = (long)(guardDmg * (0.8 + random.NextDouble() * 0.4));
                    teamHP -= guardDmg;

                    terminal.SetColor("red");
                    terminal.WriteLine($"  {guard.Name} fights back for {guardDmg}! (Team HP: {Math.Max(0, teamHP)})");

                    await Task.Delay(250);
                }

                if (teamHP <= 0)
                {
                    terminal.SetColor("red");
                    terminal.WriteLine($"Your siege force was defeated by {guard.Name}!");
                    siegeFailed = true;
                    break;
                }
                else
                {
                    guardsDefeated++;
                    currentKing.Guards.Remove(guard);
                    terminal.SetColor("bright_green");
                    terminal.WriteLine($"  {guard.Name} has been defeated!");
                    terminal.WriteLine("");
                    await Task.Delay(500);
                }
            }
        }

        await backend.UpdateSiegeProgress(siegeId, guardsDefeated);

        if (siegeFailed)
        {
            await backend.CompleteSiege(siegeId, "failed");
            terminal.WriteLine("");
            terminal.SetColor("red");
            terminal.WriteLine("═══════════════════════════════════════════════════════════════");
            terminal.WriteLine("          THE SIEGE HAS FAILED!");
            terminal.WriteLine("═══════════════════════════════════════════════════════════════");
            terminal.WriteLine("");
            terminal.SetColor("yellow");
            terminal.WriteLine("Your battered team retreats from the castle walls.");
            terminal.SetColor("gray");
            terminal.WriteLine($"Guards defeated: {guardsDefeated}/{Math.Max(1, totalGuards)}");
            currentPlayer.HP = Math.Max(1, currentPlayer.HP / 2);
            terminal.SetColor("red");
            terminal.WriteLine("You lost half your HP in the failed assault.");

            UsurperRemake.Systems.OnlineStateManager.Instance?.AddNews(
                $"{currentPlayer.Team} attempted to siege the castle but was repelled! ({guardsDefeated}/{totalGuards} guards defeated)",
                "siege");

            await terminal.PressAnyKey();
            return;
        }

        // PHASE 3: Challenge the King!
        terminal.ClearScreen();
        terminal.SetColor("bright_yellow");
        terminal.WriteLine("═══════════════════════════════════════════════════════════════");
        terminal.WriteLine("          ALL GUARDS DEFEATED — THE THRONE ROOM AWAITS!");
        terminal.WriteLine("═══════════════════════════════════════════════════════════════");
        terminal.WriteLine("");

        terminal.SetColor("white");
        terminal.WriteLine($"Your team storms the throne room. {currentKing.GetTitle()} {currentKing.Name}");
        terminal.WriteLine("rises from the throne, drawing their weapon...");
        terminal.WriteLine("");

        terminal.SetColor("cyan");
        terminal.WriteLine("As siege leader, you must face the king in single combat.");
        terminal.SetColor("gray");
        terminal.WriteLine("(Your team holds back the remaining servants.)");
        terminal.WriteLine("");

        // Apply damage taken during siege to player
        long hpLost = currentPlayer.HP - Math.Max(1, (long)(currentPlayer.HP * (teamHP / (double)Math.Max(1, currentPlayer.HP + teamMembers.Count * 100))));
        currentPlayer.HP = Math.Max(currentPlayer.HP / 2, currentPlayer.HP - hpLost); // At least keep 50% HP

        terminal.SetColor("yellow");
        terminal.WriteLine($"Your HP after the siege assault: {currentPlayer.HP}/{currentPlayer.MaxHP}");
        terminal.WriteLine("");

        terminal.SetColor("bright_red");
        terminal.Write("Face the king? (Y/N): ");
        terminal.SetColor("white");
        string faceKing = await terminal.ReadLineAsync();

        if (faceKing?.ToUpper() != "Y")
        {
            await backend.CompleteSiege(siegeId, "retreated");
            terminal.SetColor("gray");
            terminal.WriteLine("Your team secured the guards but you declined to face the king.");
            terminal.WriteLine("The king's remaining loyalists rally and your team retreats.");

            UsurperRemake.Systems.OnlineStateManager.Instance?.AddNews(
                $"{currentPlayer.Team} breached the castle defenses but retreated before facing the king!",
                "siege");

            await terminal.PressAnyKey();
            return;
        }

        // Fight the king — use real stats with defender bonus (same pattern as solo throne challenge)
        long siegeKingStr, siegeKingDef, siegeKingWeapPow, siegeKingArmPow;
        long kingHP;
        int siegeKingLevel;
        var siegeKingNpc = NPCSpawnSystem.Instance?.GetNPCByName(currentKing.Name);

        if (siegeKingNpc != null)
        {
            // NPC King — use actual stats + defender bonus
            siegeKingStr = siegeKingNpc.Strength;
            siegeKingDef = (long)(siegeKingNpc.Defence * GameConfig.KingDefenderDefBonus);
            siegeKingWeapPow = siegeKingNpc.WeapPow;
            siegeKingArmPow = siegeKingNpc.ArmPow;
            kingHP = (long)(siegeKingNpc.MaxHP * GameConfig.KingDefenderHPBonus);
            siegeKingLevel = siegeKingNpc.Level;
        }
        else if (currentKing.AI == CharacterAI.Human && DoorMode.IsOnlineMode)
        {
            // Player King (offline) — try loading from database
            long pStr = 100, pDef = 50, pWP = 50, pAP = 30, pHP = 500;
            int pLevel = 20;
            try
            {
                var sqlBackend = SaveSystem.Instance?.Backend as SqlSaveBackend;
                if (sqlBackend != null)
                {
                    var kingSave = await sqlBackend.ReadGameData(currentKing.Name.ToLowerInvariant());
                    if (kingSave?.Player != null)
                    {
                        pStr = kingSave.Player.Strength;
                        pDef = kingSave.Player.Defence;
                        pWP = kingSave.Player.WeapPow;
                        pAP = kingSave.Player.ArmPow;
                        pHP = kingSave.Player.MaxHP > 0 ? kingSave.Player.MaxHP : kingSave.Player.HP;
                        pLevel = kingSave.Player.Level;
                    }
                }
            }
            catch { /* Fall through to values above */ }

            siegeKingStr = pStr;
            siegeKingDef = (long)(pDef * GameConfig.KingDefenderDefBonus);
            siegeKingWeapPow = pWP;
            siegeKingArmPow = pAP;
            kingHP = (long)(pHP * GameConfig.KingDefenderHPBonus);
            siegeKingLevel = pLevel;
        }
        else
        {
            // Fallback — scale with estimated king level
            siegeKingLevel = Math.Max(20, currentKing.TotalReign > 0 ? 20 + (int)(currentKing.TotalReign / 5) : 20);
            siegeKingStr = 50 + siegeKingLevel * 8;
            siegeKingDef = (long)((10 + siegeKingLevel * 4) * GameConfig.KingDefenderDefBonus);
            siegeKingWeapPow = 20 + siegeKingLevel * 3;
            siegeKingArmPow = 10 + siegeKingLevel * 2;
            kingHP = (long)((200 + siegeKingLevel * 40) * GameConfig.KingDefenderHPBonus);
        }

        long playerHP = currentPlayer.HP;

        terminal.ClearScreen();
        terminal.SetColor("bright_red");
        terminal.WriteLine($"=== {currentKing.GetTitle()} {currentKing.Name} vs {currentPlayer.DisplayName} ===");
        terminal.WriteLine("");
        terminal.SetColor("gray");
        terminal.WriteLine($"Level {siegeKingLevel} | STR: {siegeKingStr} | DEF: {siegeKingDef} | HP: {kingHP}");
        terminal.WriteLine("");

        int kingRounds = 0;
        while (kingHP > 0 && playerHP > 0 && kingRounds < 30)
        {
            kingRounds++;

            // Player attacks (accounts for weapon and armor power)
            long playerDamage = Math.Max(1, currentPlayer.Strength + currentPlayer.WeapPow - siegeKingDef - siegeKingArmPow);
            playerDamage = (long)(playerDamage * (0.8 + random.NextDouble() * 0.4));
            kingHP -= playerDamage;

            terminal.SetColor("bright_green");
            terminal.WriteLine($"You strike {currentKing.Name} for {playerDamage} damage! (King HP: {Math.Max(0, kingHP)})");

            if (kingHP <= 0) break;

            // King attacks (accounts for weapon and armor power)
            long kingDamage = Math.Max(1, siegeKingStr + siegeKingWeapPow - currentPlayer.Defence - currentPlayer.ArmPow);
            kingDamage = (long)(kingDamage * (0.8 + random.NextDouble() * 0.4));
            playerHP -= kingDamage;

            terminal.SetColor("red");
            terminal.WriteLine($"{currentKing.Name} strikes you for {kingDamage}! (Your HP: {Math.Max(0, playerHP)})");

            await Task.Delay(300);
        }

        if (playerHP <= 0 || (kingHP > 0 && playerHP > 0))
        {
            // King wins or stalemate — siege fails
            await backend.CompleteSiege(siegeId, "king_won");
            terminal.WriteLine("");
            terminal.SetColor("red");
            if (playerHP <= 0)
            {
                terminal.WriteLine($"{currentKing.Name} has defeated you!");
            }
            else
            {
                terminal.WriteLine($"The battle reaches a stalemate! {currentKing.Name} holds firm.");
            }
            terminal.WriteLine("The siege has failed at the final hurdle.");
            currentPlayer.HP = Math.Max(1, playerHP);

            UsurperRemake.Systems.OnlineStateManager.Instance?.AddNews(
                $"{currentPlayer.Team} sieged the castle but {currentPlayer.DisplayName} fell to {currentKing.Name} in combat!",
                "siege");

            await terminal.PressAnyKey();
            return;
        }

        // VICTORY — Player takes the throne!
        await backend.CompleteSiege(siegeId, "victory");

        terminal.ClearScreen();
        terminal.SetColor("bright_yellow");
        terminal.WriteLine("╔══════════════════════════════════════════════════════════════════════════════╗");
        terminal.WriteLine("║                    THE SIEGE IS VICTORIOUS!                                  ║");
        terminal.WriteLine("╚══════════════════════════════════════════════════════════════════════════════╝");
        terminal.WriteLine("");

        terminal.SetColor("white");
        terminal.WriteLine($"{currentKing.Name} falls to the ground, defeated.");
        terminal.WriteLine("The crown tumbles from their head and rolls to your feet...");
        terminal.WriteLine("");
        await Task.Delay(2000);

        // Must leave team to become king
        string siegeTeam = currentPlayer.Team;
        CityControlSystem.Instance.ForceLeaveTeam(currentPlayer);
        GameEngine.Instance?.ClearDungeonParty();

        terminal.SetColor("yellow");
        terminal.WriteLine($"You leave '{siegeTeam}' to take the crown.");
        terminal.WriteLine("");
        await Task.Delay(1500);

        // Record old monarch in history
        string oldKingName = currentKing.Name;
        monarchHistory.Add(new MonarchRecord
        {
            Name = currentKing.Name,
            Title = currentKing.GetTitle(),
            DaysReigned = (int)currentKing.TotalReign,
            CoronationDate = currentKing.CoronationDate,
            EndReason = $"Overthrown by {siegeTeam} siege"
        });
        while (monarchHistory.Count > MaxMonarchHistory)
            monarchHistory.RemoveAt(0);

        // Crown new monarch — inherit the previous king's treasury and orphans
        long inheritedTreasury = currentKing.Treasury;
        var inheritedOrphans = currentKing?.Orphans?.ToList();
        bool oldKingWasHuman = currentKing.AI == CharacterAI.Human;
        ClearRoyalMarriage(currentKing); // Clear old king's royal spouse before replacing
        currentPlayer.King = true;
        currentKing = King.CreateNewKing(currentPlayer.DisplayName, CharacterAI.Human, currentPlayer.Sex, inheritedOrphans);
        currentKing.Treasury = inheritedTreasury;
        playerIsKing = true;
        currentPlayer.PKills++;
        UsurperRemake.Systems.ArchetypeTracker.Instance.RecordBecameKing();

        currentPlayer.HP = playerHP;

        terminal.SetColor("bright_yellow");
        terminal.WriteLine($"ALL HAIL {currentPlayer.DisplayName.ToUpper()}, THE NEW RULER!");
        terminal.WriteLine("");
        terminal.SetColor("cyan");
        terminal.WriteLine("Your team's siege will be remembered in the annals of history.");

        UsurperRemake.Systems.OnlineStateManager.Instance?.AddNews(
            $"{siegeTeam} has conquered the castle! {currentPlayer.DisplayName} overthrew {oldKingName} and claims the throne!",
            "siege");

        // Notify the dethroned player
        if (oldKingWasHuman)
            NotifyDethronedPlayer(oldKingName, currentPlayer.DisplayName, $"was overthrown by {siegeTeam}'s siege");

        // Persist royal court changes
        if (UsurperRemake.BBS.DoorMode.IsOnlineMode)
            PersistRoyalCourtToWorldState();

        await terminal.PressAnyKey();
    }
}

// ═══════════════════════════════════════════════════════════════════════════
// SUPPORTING CLASSES
// ═══════════════════════════════════════════════════════════════════════════

public class RoyalMailMessage
{
    public string Sender { get; set; } = "";
    public string Subject { get; set; } = "";
    public string Body { get; set; } = "";
    public bool IsRead { get; set; } = false;
    public DateTime Date { get; set; } = DateTime.Now;
}

public class MonarchRecord
{
    public string Name { get; set; } = "";
    public string Title { get; set; } = "";
    public int DaysReigned { get; set; } = 0;
    public DateTime CoronationDate { get; set; } = DateTime.Now;
    public string EndReason { get; set; } = "";
}
