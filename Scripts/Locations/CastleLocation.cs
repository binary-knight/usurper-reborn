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
    private Random random = Random.Shared;

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

        if (IsBBSSession)
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
        WriteBoxHeader(Loc.Get("castle.header"), "bright_cyan");
        terminal.WriteLine("");

        // Royal greeting
        terminal.SetColor("cyan");
        terminal.WriteLine(Loc.Get("castle.royal_greeting_1"));
        terminal.WriteLine(Loc.Get("castle.royal_greeting_2"));
        terminal.SetColor("white");
        terminal.WriteLine(Loc.Get("castle.royal_greeting_3"));
        terminal.WriteLine("");

        // Royal treasury status
        terminal.SetColor("bright_green");
        terminal.Write(Loc.Get("castle.treasury_label"));
        terminal.SetColor("white");
        terminal.Write(Loc.Get("castle.treasury_has"));
        terminal.SetColor("bright_yellow");
        terminal.Write($"{currentKing.Treasury:N0}");
        terminal.SetColor("white");
        terminal.WriteLine(Loc.Get("castle.treasury_gold"));

        // Guard status
        terminal.SetColor("cyan");
        terminal.Write(Loc.Get("castle.guards_label"));
        terminal.SetColor("white");
        terminal.WriteLine($"{currentKing.Guards.Count}/{GameConfig.MaxRoyalGuards}");

        // Prisoners
        terminal.SetColor("red");
        terminal.Write(Loc.Get("castle.prisoners_label"));
        terminal.SetColor("white");
        terminal.WriteLine($"{currentKing.Prisoners.Count}");

        // Unread mail
        int unreadMail = royalMail.Count(m => !m.IsRead);
        if (unreadMail > 0)
        {
            terminal.SetColor("bright_magenta");
            terminal.WriteLine(Loc.Get("castle.unread_mail", unreadMail));
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
        WriteBoxHeader(Loc.Get("castle.header_outside"), "bright_cyan");
        terminal.WriteLine("");

        // Approach description
        terminal.SetColor("white");
        terminal.WriteLine(Loc.Get("castle.approach_desc_1"));
        terminal.WriteLine(Loc.Get("castle.approach_desc_2"));
        terminal.WriteLine(Loc.Get("castle.approach_desc_3"));
        terminal.WriteLine("");

        // King status
        if (currentKing != null && currentKing.IsActive)
        {
            terminal.SetColor("white");
            terminal.Write(Loc.Get("castle.the_mighty"));
            terminal.SetColor("bright_yellow");
            terminal.Write($"{currentKing.GetTitle()} {currentKing.Name}");
            terminal.SetColor("white");
            terminal.WriteLine(Loc.Get("castle.rules_from"));
            terminal.SetColor("cyan");
            terminal.WriteLine(Loc.Get("castle.reign_treasury", currentKing.TotalReign, $"{currentKing.Treasury:N0}"));
            terminal.WriteLine("");
        }
        else
        {
            terminal.SetColor("bright_red");
            terminal.WriteLine(Loc.Get("castle.kingdom_disarray"));
            terminal.WriteLine(Loc.Get("castle.no_monarch"));
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
        terminal.WriteLine(Loc.Get("castle.royal_commands"));
        terminal.WriteLine("");

        if (IsScreenReader)
        {
            WriteSRMenuOption("P", Loc.Get("castle.prison_cells"));
            WriteSRMenuOption("O", Loc.Get("castle.orders"));
            WriteSRMenuOption("1", Loc.Get("castle.royal_mail"));
            WriteSRMenuOption("G", Loc.Get("castle.sleep"));
            WriteSRMenuOption("C", Loc.Get("castle.security"));
            WriteSRMenuOption("H", Loc.Get("castle.history"));
            WriteSRMenuOption("A", Loc.Get("castle.abdicate"));
            WriteSRMenuOption("M", Loc.Get("castle.magic"));
            WriteSRMenuOption("F", Loc.Get("castle.fiscal"));
            WriteSRMenuOption("S", Loc.Get("castle.status"));
            WriteSRMenuOption("Q", Loc.Get("castle.quests"));
            WriteSRMenuOption("T", Loc.Get("castle.orphanage"));
            WriteSRMenuOption("W", Loc.Get("castle.wedding"));
            WriteSRMenuOption("U", Loc.Get("castle.court"));
            WriteSRMenuOption("E", Loc.Get("castle.estate"));
            WriteSRMenuOption("B", Loc.Get("castle.bodyguards"));
            terminal.WriteLine("");
            WriteSRMenuOption("R", Loc.Get("castle.return"));
            terminal.WriteLine("");
        }
        else
        {
            // Row 1
            terminal.SetColor("darkgray");
            terminal.Write(" [");
            terminal.SetColor("bright_yellow");
            terminal.Write("P");
            terminal.SetColor("darkgray");
            terminal.Write("]");
            terminal.SetColor("white");
            terminal.Write(Loc.Get("castle.menu_prison_cells"));

            terminal.SetColor("darkgray");
            terminal.Write("[");
            terminal.SetColor("bright_yellow");
            terminal.Write("O");
            terminal.SetColor("darkgray");
            terminal.Write("]");
            terminal.SetColor("white");
            terminal.Write(Loc.Get("castle.menu_orders"));

            terminal.SetColor("darkgray");
            terminal.Write("[");
            terminal.SetColor("bright_yellow");
            terminal.Write("1");
            terminal.SetColor("darkgray");
            terminal.Write("]");
            terminal.SetColor("white");
            terminal.WriteLine(Loc.Get("castle.menu_royal_mail"));

            // Row 2
            terminal.SetColor("darkgray");
            terminal.Write(" [");
            terminal.SetColor("bright_yellow");
            terminal.Write("G");
            terminal.SetColor("darkgray");
            terminal.Write("]");
            terminal.SetColor("white");
            terminal.Write(Loc.Get("castle.menu_go_sleep"));

            terminal.SetColor("darkgray");
            terminal.Write("[");
            terminal.SetColor("bright_yellow");
            terminal.Write("C");
            terminal.SetColor("darkgray");
            terminal.Write("]");
            terminal.SetColor("white");
            terminal.Write(Loc.Get("castle.menu_check_security"));

            terminal.SetColor("darkgray");
            terminal.Write("[");
            terminal.SetColor("bright_yellow");
            terminal.Write("H");
            terminal.SetColor("darkgray");
            terminal.Write("]");
            terminal.SetColor("white");
            terminal.WriteLine(Loc.Get("castle.menu_history"));

            // Row 3
            terminal.SetColor("darkgray");
            terminal.Write(" [");
            terminal.SetColor("bright_yellow");
            terminal.Write("A");
            terminal.SetColor("darkgray");
            terminal.Write("]");
            terminal.SetColor("white");
            terminal.Write(Loc.Get("castle.menu_abdicate"));

            terminal.SetColor("darkgray");
            terminal.Write("[");
            terminal.SetColor("bright_yellow");
            terminal.Write("M");
            terminal.SetColor("darkgray");
            terminal.Write("]");
            terminal.SetColor("white");
            terminal.Write(Loc.Get("castle.menu_magic"));

            terminal.SetColor("darkgray");
            terminal.Write("[");
            terminal.SetColor("bright_yellow");
            terminal.Write("F");
            terminal.SetColor("darkgray");
            terminal.Write("]");
            terminal.SetColor("white");
            terminal.WriteLine(Loc.Get("castle.menu_fiscal"));

            // Row 4
            terminal.SetColor("darkgray");
            terminal.Write(" [");
            terminal.SetColor("bright_yellow");
            terminal.Write("S");
            terminal.SetColor("darkgray");
            terminal.Write("]");
            terminal.SetColor("white");
            terminal.Write(Loc.Get("castle.menu_status"));

            terminal.SetColor("darkgray");
            terminal.Write("[");
            terminal.SetColor("bright_yellow");
            terminal.Write("Q");
            terminal.SetColor("darkgray");
            terminal.Write("]");
            terminal.SetColor("white");
            terminal.Write(Loc.Get("castle.menu_quests"));

            terminal.SetColor("darkgray");
            terminal.Write("[");
            terminal.SetColor("bright_yellow");
            terminal.Write("T");
            terminal.SetColor("darkgray");
            terminal.Write("]");
            terminal.SetColor("white");
            terminal.WriteLine(Loc.Get("castle.menu_orphanage"));

            // Row 5 - Political systems
            terminal.SetColor("darkgray");
            terminal.Write(" [");
            terminal.SetColor("bright_yellow");
            terminal.Write("W");
            terminal.SetColor("darkgray");
            terminal.Write("]");
            terminal.SetColor("white");
            terminal.Write(Loc.Get("castle.menu_wedding"));

            terminal.SetColor("darkgray");
            terminal.Write("[");
            terminal.SetColor("bright_yellow");
            terminal.Write("U");
            terminal.SetColor("darkgray");
            terminal.Write("]");
            terminal.SetColor("white");
            terminal.Write(Loc.Get("castle.menu_court"));

            terminal.SetColor("darkgray");
            terminal.Write("[");
            terminal.SetColor("bright_yellow");
            terminal.Write("E");
            terminal.SetColor("darkgray");
            terminal.Write("]");
            terminal.SetColor("white");
            terminal.WriteLine(Loc.Get("castle.menu_estate"));

            // Row 6 - Royal Bodyguards
            terminal.SetColor("darkgray");
            terminal.Write(" [");
            terminal.SetColor("bright_yellow");
            terminal.Write("B");
            terminal.SetColor("darkgray");
            terminal.Write("]");
            terminal.SetColor("white");
            terminal.WriteLine(Loc.Get("castle.menu_bodyguards"));

            terminal.WriteLine("");

            // Navigation
            terminal.SetColor("darkgray");
            terminal.Write(" [");
            terminal.SetColor("bright_yellow");
            terminal.Write("R");
            terminal.SetColor("darkgray");
            terminal.Write("]");
            terminal.SetColor("white");
            terminal.WriteLine(Loc.Get("castle.menu_return_town"));
            terminal.WriteLine("");
        }

        ShowStatusLine();
    }

    /// <summary>
    /// Show castle exterior menu for commoners
    /// </summary>
    private void ShowCastleExteriorMenu()
    {
        terminal.SetColor("cyan");
        terminal.WriteLine(Loc.Get("castle.options_label"));
        terminal.WriteLine("");

        if (IsScreenReader)
        {
            WriteSRMenuOption("T", Loc.Get("castle.royal_guard"));
            WriteSRMenuOption("P", Loc.Get("castle.prison"));
            WriteSRMenuOption("D", Loc.Get("castle.donate"));
            WriteSRMenuOption("H", Loc.Get("castle.history"));
            WriteSRMenuOption("S", Loc.Get("castle.seek_audience"));
            WriteSRMenuOption("A", Loc.Get("castle.apply_guard"));

            if (currentKing != null && currentKing.IsActive)
            {
                bool canChallenge = CanChallengeThrone();
                WriteSRMenuOption("I", canChallenge ? Loc.Get("castle.infiltrate") : Loc.Get("castle.infiltrate_unavailable"), canChallenge);
            }
            else
            {
                bool canClaim = currentPlayer.Level >= GameConfig.MinLevelKing;
                WriteSRMenuOption("C", canClaim ? Loc.Get("castle.claim_empty_throne_sr") : Loc.Get("castle.claim_throne_requires_sr", GameConfig.MinLevelKing), canClaim);
            }

            if (UsurperRemake.BBS.DoorMode.IsOnlineMode && !string.IsNullOrEmpty(currentPlayer.Team))
                WriteSRMenuOption("B", Loc.Get("castle.besiege"));

            terminal.WriteLine("");

            var factionSystem = UsurperRemake.Systems.FactionSystem.Instance;
            if (factionSystem.PlayerFaction != UsurperRemake.Systems.Faction.TheCrown)
                WriteSRMenuOption("J", Loc.Get("castle.join_service"));
            else
                terminal.WriteLine(Loc.Get("castle.loyal_servant"));

            if (FactionSystem.Instance?.HasCastleAccess() == true)
                WriteSRMenuOption("L", Loc.Get("castle.armory"));

            WriteSRMenuOption("R", Loc.Get("castle.return"));
            terminal.WriteLine("");
        }
        else
        {
            // Row 1
            terminal.SetColor("darkgray");
            terminal.Write(" [");
            terminal.SetColor("bright_yellow");
            terminal.Write("T");
            terminal.SetColor("darkgray");
            terminal.Write("]");
            terminal.SetColor("white");
            terminal.Write(Loc.Get("castle.menu_ext_royal_guard"));

            terminal.SetColor("darkgray");
            terminal.Write("[");
            terminal.SetColor("bright_yellow");
            terminal.Write("P");
            terminal.SetColor("darkgray");
            terminal.Write("]");
            terminal.SetColor("white");
            terminal.Write(Loc.Get("castle.menu_ext_prison"));

            terminal.SetColor("darkgray");
            terminal.Write("[");
            terminal.SetColor("bright_yellow");
            terminal.Write("D");
            terminal.SetColor("darkgray");
            terminal.Write("]");
            terminal.SetColor("white");
            terminal.WriteLine(Loc.Get("castle.menu_ext_donate"));

            // Row 2
            terminal.SetColor("darkgray");
            terminal.Write(" [");
            terminal.SetColor("bright_yellow");
            terminal.Write("H");
            terminal.SetColor("darkgray");
            terminal.Write("]");
            terminal.SetColor("white");
            terminal.Write(Loc.Get("castle.menu_ext_history"));

            terminal.SetColor("darkgray");
            terminal.Write("[");
            terminal.SetColor("bright_yellow");
            terminal.Write("S");
            terminal.SetColor("darkgray");
            terminal.Write("]");
            terminal.SetColor("white");
            terminal.Write(Loc.Get("castle.menu_ext_audience"));

            // Apply for Royal Guard option
            terminal.SetColor("darkgray");
            terminal.Write("[");
            terminal.SetColor("bright_yellow");
            terminal.Write("A");
            terminal.SetColor("darkgray");
            terminal.Write("]");
            terminal.SetColor("white");
            terminal.WriteLine(Loc.Get("castle.menu_ext_apply_guard"));

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
                    terminal.WriteLine(Loc.Get("castle.menu_infiltrate_challenge"));
                }
                else if (currentPlayer.Level < GameConfig.MinLevelKing)
                {
                    terminal.SetColor("gray");
                    terminal.WriteLine(Loc.Get("castle.menu_infiltrate_level_req", GameConfig.MinLevelKing));
                }
                else if (currentKing != null && currentKing.IsActive)
                {
                    int kLevel = GetKingLevel();
                    if (kLevel > 0 && kLevel - currentPlayer.Level > GameConfig.KingChallengeLevelRange)
                    {
                        terminal.SetColor("gray");
                        terminal.WriteLine(Loc.Get("castle.menu_infiltrate_king_level", kLevel, Math.Max(GameConfig.MinLevelKing, kLevel - GameConfig.KingChallengeLevelRange)));
                    }
                    else
                    {
                        terminal.SetColor("gray");
                        terminal.WriteLine(Loc.Get("castle.menu_infiltrate_unavailable"));
                    }
                }
                else
                {
                    terminal.SetColor("gray");
                    terminal.WriteLine(Loc.Get("castle.menu_infiltrate_unavailable"));
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
                    terminal.WriteLine(Loc.Get("castle.menu_claim_throne"));
                }
                else
                {
                    terminal.SetColor("gray");
                    terminal.WriteLine(Loc.Get("castle.menu_claim_throne_level", GameConfig.MinLevelKing));
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
                terminal.WriteLine(Loc.Get("castle.menu_besiege"));
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
                terminal.Write(Loc.Get("castle.menu_join_service"));
                if (factionSystem.PlayerFaction == null)
                {
                    terminal.SetColor("gray");
                    terminal.WriteLine(Loc.Get("castle.swear_loyalty"));
                }
                else
                {
                    terminal.SetColor("dark_red");
                    terminal.WriteLine(Loc.Get("castle.serve_another"));
                }
            }
            else
            {
                terminal.SetColor("bright_green");
                terminal.WriteLine(Loc.Get("castle.loyal_servant"));
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
                terminal.WriteLine(Loc.Get("castle.menu_royal_armory"));
            }

            // Navigation
            terminal.SetColor("darkgray");
            terminal.Write(" [");
            terminal.SetColor("bright_yellow");
            terminal.Write("R");
            terminal.SetColor("darkgray");
            terminal.Write("]");
            terminal.SetColor("white");
            terminal.WriteLine(Loc.Get("castle.menu_return_town"));
            terminal.WriteLine("");
        }

        ShowStatusLine();
    }

    /// <summary>
    /// BBS compact display for the reigning monarch (80x25 terminal)
    /// </summary>
    private void DisplayRoyalCastleInteriorBBS()
    {
        ShowBBSHeader(Loc.Get("castle.header"));
        // 1-line greeting + treasury
        terminal.SetColor("white");
        terminal.Write(Loc.Get("castle.bbs_majesty_treasury"));
        terminal.SetColor("bright_yellow");
        terminal.Write($"{currentKing.Treasury:N0}g");
        terminal.SetColor("gray");
        terminal.Write(Loc.Get("castle.bbs_guards", currentKing.Guards.Count, GameConfig.MaxRoyalGuards));
        terminal.Write(Loc.Get("castle.bbs_prisoners", currentKing.Prisoners.Count));
        terminal.WriteLine("");
        int unreadMail = royalMail.Count(m => !m.IsRead);
        if (unreadMail > 0)
        {
            terminal.SetColor("bright_magenta");
            terminal.WriteLine(Loc.Get("castle.unread_mail", unreadMail));
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
        ShowBBSHeader(Loc.Get("castle.header_outside"));
        // 1-line king status
        if (currentKing != null && currentKing.IsActive)
        {
            terminal.SetColor("white");
            terminal.Write($" {currentKing.GetTitle()} ");
            terminal.SetColor("bright_yellow");
            terminal.Write(currentKing.Name);
            terminal.SetColor("gray");
            terminal.WriteLine(Loc.Get("castle.bbs_rules", currentKing.TotalReign, $"{currentKing.Treasury:N0}"));
        }
        else
        {
            terminal.SetColor("bright_red");
            terminal.WriteLine(" " + Loc.Get("castle.no_monarch"));
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
                terminal.WriteLine(Loc.Get("castle.invalid_choice"));
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
                        terminal.WriteLine(Loc.Get("castle.level_req_throne", GameConfig.MinLevelKing));
                    else if (currentKing != null && currentKing.IsActive)
                    {
                        int kLevel = GetKingLevel();
                        if (kLevel > 0 && kLevel - currentPlayer.Level > GameConfig.KingChallengeLevelRange)
                            terminal.WriteLine(Loc.Get("castle.king_level_challenge", currentKing.GetTitle(), kLevel, kLevel - GameConfig.KingChallengeLevelRange));
                        else
                            terminal.WriteLine(Loc.Get("castle.not_worthy"));
                    }
                    else
                        terminal.WriteLine(Loc.Get("castle.not_worthy"));
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
                    terminal.WriteLine(Loc.Get("castle.throne_occupied"));
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
                    terminal.WriteLine(Loc.Get("castle.siege_online_only"));
                    await Task.Delay(1500);
                }
                return false;

            case "R":
                await NavigateToLocation(GameLocation.MainStreet);
                return true;

            default:
                terminal.SetColor("red");
                terminal.WriteLine(Loc.Get("castle.invalid_choice"));
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
        WriteBoxHeader(Loc.Get("castle.royal_guard"), "bright_cyan");
        terminal.WriteLine("");

        if (currentKing == null || !currentKing.IsActive)
        {
            terminal.SetColor("gray");
            terminal.WriteLine(Loc.Get("castle.guard_disbanded"));
            terminal.WriteLine(Loc.Get("castle.castle_undefended"));
        }
        else if (currentKing.Guards.Count == 0)
        {
            terminal.SetColor("yellow");
            terminal.WriteLine(Loc.Get("castle.no_royal_guards", currentKing.GetTitle(), currentKing.Name));
            terminal.WriteLine(Loc.Get("castle.walls_only"));
        }
        else
        {
            terminal.SetColor("white");
            terminal.WriteLine(Loc.Get("castle.guard_of", currentKing.GetTitle(), currentKing.Name));
            terminal.WriteLine("");

            terminal.SetColor("cyan");
            terminal.WriteLine($"{Loc.Get("castle.header_num"),-3} {Loc.Get("castle.header_name"),-20} {Loc.Get("castle.header_rank"),-15} {Loc.Get("castle.header_loyalty"),-10}");
            WriteDivider(50);

            int i = 1;
            foreach (var guard in currentKing.Guards)
            {
                string loyaltyColor = guard.Loyalty > 80 ? "bright_green" :
                                     guard.Loyalty > 50 ? "yellow" : "red";

                terminal.SetColor("white");
                terminal.Write($"{i,-3} {guard.Name,-20} ");
                terminal.SetColor("cyan");
                terminal.Write($"{Loc.Get("castle.guard_rank"),-15} ");
                terminal.SetColor(loyaltyColor);
                terminal.WriteLine($"{guard.Loyalty}%");
                i++;
            }

            terminal.WriteLine("");
            terminal.SetColor("white");
            terminal.WriteLine(Loc.Get("castle.total_guards", currentKing.Guards.Count, GameConfig.MaxRoyalGuards));
        }

        terminal.WriteLine("");
        terminal.SetColor("darkgray");
        terminal.WriteLine(Loc.Get("ui.press_enter"));
        await terminal.ReadKeyAsync();
    }

    // ═══════════════════════════════════════════════════════════════════════════
    // PRISON SYSTEM
    // ═══════════════════════════════════════════════════════════════════════════

    private async Task VisitPrison()
    {
        terminal.ClearScreen();
        WriteBoxHeader(Loc.Get("castle.royal_prison"), "red");
        terminal.WriteLine("");

        terminal.SetColor("gray");
        terminal.WriteLine(Loc.Get("castle.prison_bars"));
        terminal.WriteLine(Loc.Get("castle.prison_smell"));
        terminal.WriteLine("");

        if (currentKing == null || currentKing.Prisoners.Count == 0)
        {
            terminal.SetColor("white");
            terminal.WriteLine(Loc.Get("castle.prison_empty"));
        }
        else
        {
            terminal.SetColor("cyan");
            terminal.WriteLine($"{Loc.Get("castle.header_name"),-20} {Loc.Get("castle.header_crime"),-20} {Loc.Get("castle.header_days_left"),-10}");
            WriteDivider(55);

            foreach (var prisoner in currentKing.Prisoners)
            {
                int daysLeft = Math.Max(0, prisoner.Value.Sentence - prisoner.Value.DaysServed);
                terminal.SetColor("white");
                terminal.WriteLine($"{prisoner.Value.CharacterName,-20} {prisoner.Value.Crime,-20} {daysLeft,-10}");
            }
        }

        terminal.WriteLine("");
        terminal.SetColor("darkgray");
        terminal.WriteLine(Loc.Get("ui.press_enter"));
        await terminal.ReadKeyAsync();
    }

    private async Task ManagePrisonCells()
    {
        if (currentKing == null)
        {
            terminal.SetColor("gray");
            terminal.WriteLine("  The throne is vacant. No one has authority over the prison.");
            await Task.Delay(2000);
            return;
        }

        bool done = false;
        while (!done)
        {
            terminal.ClearScreen();
            WriteBoxHeader(Loc.Get("castle.prison_mgmt"), "bright_red");
            terminal.WriteLine("");

            if (currentKing.Prisoners.Count == 0)
            {
                terminal.SetColor("gray");
                terminal.WriteLine(Loc.Get("castle.dungeons_empty"));
                terminal.WriteLine("");
            }
            else
            {
                int i = 1;
                if (IsScreenReader)
                {
                    foreach (var prisoner in currentKing.Prisoners)
                    {
                        var p = prisoner.Value;
                        string bailStr = p.BailAmount > 0 ? $"{p.BailAmount:N0}g" : Loc.Get("ui.none");
                        terminal.SetColor("white");
                        terminal.WriteLine($"{i}. {p.CharacterName} - {Loc.Get("castle.header_crime")}: {p.Crime}, {Loc.Get("castle.header_sentence")}: {p.Sentence} days, {Loc.Get("castle.header_served")}: {p.DaysServed}, {Loc.Get("castle.header_bail")}: {bailStr}");
                        i++;
                    }
                }
                else
                {
                    terminal.SetColor("cyan");
                    terminal.WriteLine($"  {Loc.Get("castle.header_num"),-3} {Loc.Get("castle.header_name"),-18} {Loc.Get("castle.header_crime"),-18} {Loc.Get("castle.header_sentence"),-10} {Loc.Get("castle.header_served"),-8} {Loc.Get("castle.header_bail"),-10}");
                    WriteDivider(75);

                    foreach (var prisoner in currentKing.Prisoners)
                    {
                        var p = prisoner.Value;
                        string bailStr = p.BailAmount > 0 ? $"{p.BailAmount:N0}g" : Loc.Get("ui.none");
                        terminal.SetColor("white");
                        terminal.WriteLine($"  {i,-3} {p.CharacterName,-18} {p.Crime,-18} {p.Sentence,-10} {p.DaysServed,-8} {bailStr,-10}");
                        i++;
                    }
                }
                terminal.WriteLine("");
            }

            if (IsScreenReader)
            {
                WriteSRMenuOption("I", Loc.Get("castle.imprison"));
                WriteSRMenuOption("P", Loc.Get("castle.pardon"));
                WriteSRMenuOption("E", Loc.Get("castle.execute"));
                WriteSRMenuOption("S", Loc.Get("castle.set_bail"));
                WriteSRMenuOption("R", Loc.Get("castle.return"));
            }
            else if (IsBBSSession)
            {
                terminal.SetColor("bright_yellow");
                terminal.Write(" [I]");
                terminal.SetColor("white");
                terminal.Write($"{Loc.Get("castle.imprison")}  ");
                terminal.SetColor("bright_yellow");
                terminal.Write("[P]");
                terminal.SetColor("white");
                terminal.Write($"{Loc.Get("castle.pardon")}  ");
                terminal.SetColor("bright_yellow");
                terminal.Write("[E]");
                terminal.SetColor("white");
                terminal.WriteLine($"{Loc.Get("castle.execute")}");
                terminal.SetColor("bright_yellow");
                terminal.Write(" [S]");
                terminal.SetColor("white");
                terminal.Write($"{Loc.Get("castle.set_bail")}  ");
                terminal.SetColor("bright_yellow");
                terminal.Write("[R]");
                terminal.SetColor("gray");
                terminal.WriteLine($"{Loc.Get("castle.return")}");
            }
            else
            {
                terminal.WriteLine("");
                terminal.Write("  [", "white");
                terminal.Write("I", "bright_yellow");
                terminal.Write($"] {Loc.Get("castle.imprison")}   ", "white");
                terminal.Write("[", "white");
                terminal.Write("P", "bright_yellow");
                terminal.Write($"] {Loc.Get("castle.pardon")}   ", "white");
                terminal.Write("[", "white");
                terminal.Write("E", "bright_yellow");
                terminal.WriteLine($"] {Loc.Get("castle.execute")}", "white");

                terminal.Write("  [", "white");
                terminal.Write("S", "bright_yellow");
                terminal.Write($"] {Loc.Get("castle.set_bail")}   ", "white");
                terminal.Write("[", "white");
                terminal.Write("R", "bright_yellow");
                terminal.WriteLine($"] {Loc.Get("castle.return")}", "gray");
            }
            terminal.WriteLine("");

            terminal.SetColor("cyan");
            string input = await terminal.GetInput(Loc.Get("castle.your_decree"));

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

        // Add living NPCs (not already imprisoned, not the king, not dead)
        string kingName = currentKing?.Name ?? "";
        var npcs = NPCSpawnSystem.Instance?.ActiveNPCs?
            .Where(n => n.IsAlive && !n.IsDead && !n.IsPermaDead && n.DaysInPrison <= 0 && n.Name2 != kingName)
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
            terminal.WriteLine(Loc.Get("castle.no_one_imprison"));
            await Task.Delay(1500);
            return;
        }

        // Show numbered list
        terminal.SetColor("cyan");
        terminal.WriteLine($"{Loc.Get("castle.header_num"),-4} {Loc.Get("castle.header_name"),-20} {Loc.Get("castle.header_type"),-20}");
        WriteDivider(44);

        for (int i = 0; i < targets.Count; i++)
        {
            terminal.SetColor(targets[i].IsNPC ? "white" : "bright_yellow");
            terminal.WriteLine($"{i + 1,-4} {targets[i].Name,-20} {targets[i].Type,-20}");
        }

        terminal.WriteLine("");
        terminal.SetColor("cyan");
        terminal.Write(Loc.Get("castle.imprison_prompt"));
        terminal.SetColor("white");
        string input = await terminal.ReadLineAsync();

        if (!int.TryParse(input, out int idx) || idx < 1 || idx > targets.Count)
            return;

        var target = targets[idx - 1];

        // Check if already imprisoned (prevent double-imprisonment)
        if (currentKing.Prisoners.ContainsKey(target.Name))
        {
            terminal.SetColor("yellow");
            terminal.WriteLine($"  {target.Name} is already imprisoned!");
            await Task.Delay(1500);
            return;
        }

        // Limit: 1 player imprisonment per day, 5 NPCs per day
        if (!target.IsNPC && currentPlayer.PlayerImprisonedToday)
        {
            terminal.SetColor("yellow");
            terminal.WriteLine("  You have already imprisoned a player today.");
            terminal.WriteLine("  The guards need time to process new prisoners.");
            await Task.Delay(2000);
            return;
        }
        if (target.IsNPC && currentPlayer.NPCsImprisonedToday >= 5)
        {
            terminal.SetColor("yellow");
            terminal.WriteLine("  The dungeons are overwhelmed. No more NPC arrests today.");
            await Task.Delay(2000);
            return;
        }

        terminal.SetColor("cyan");
        terminal.Write(Loc.Get("castle.crime_committed"));
        terminal.SetColor("white");
        string crime = await terminal.ReadLineAsync();
        if (string.IsNullOrEmpty(crime)) crime = Loc.Get("castle.general_misconduct");

        terminal.SetColor("cyan");
        terminal.Write(Loc.Get("castle.sentence_days"));
        terminal.SetColor("white");
        string sentenceStr = await terminal.ReadLineAsync();

        int sentence = 7;
        if (int.TryParse(sentenceStr, out int s) && s > 0)
            sentence = target.IsNPC ? Math.Min(s, 30) : Math.Min(s, 1); // Players: max 1 day; NPCs: max 30

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

            // If the player is currently online, immediately send guards to arrest them
            var mudServer = UsurperRemake.Server.MudServer.Instance;
            if (mudServer != null)
            {
                // Find their session by display name or username
                UsurperRemake.Server.PlayerSession? targetSession = null;
                foreach (var kvp in mudServer.ActiveSessions)
                {
                    var sessPlayer = kvp.Value.Context?.Engine?.CurrentPlayer;
                    if (sessPlayer != null &&
                        (sessPlayer.DisplayName.Equals(target.Name, StringComparison.OrdinalIgnoreCase) ||
                         sessPlayer.Name2?.Equals(target.Name, StringComparison.OrdinalIgnoreCase) == true))
                    {
                        targetSession = kvp.Value;
                        break;
                    }
                }

                if (targetSession != null)
                {
                    var sessPlayer = targetSession!.Context?.Engine?.CurrentPlayer;
                    if (sessPlayer != null)
                    {
                        sessPlayer.DaysInPrison = (byte)sentence;
                        // Send them a dramatic arrest message
                        targetSession!.IncomingMessages.Enqueue(
                            $"\u001b[1;31m\n  *** ROYAL GUARDS SEIZE YOU! ***\n" +
                            $"  By order of {currentKing.GetTitle()} {currentKing.Name}, you are under arrest!\n" +
                            $"  Crime: {crime}\n" +
                            $"  Sentence: {sentence} day{(sentence == 1 ? "" : "s")}\n" +
                            $"  You will be sent to prison on your next action.\u001b[0m");
                    }
                }
            }
        }

        // Persist royal court changes in online mode
        if (DoorMode.IsOnlineMode)
            PersistRoyalCourtToWorldState();

        terminal.SetColor("bright_green");
        terminal.WriteLine(Loc.Get("castle.imprisoned_confirm", target.Name, sentence));
        NewsSystem.Instance.Newsy(true, $"{currentKing.GetTitle()} {currentKing.Name} imprisoned {target.Name} for {crime}!");

        // Track daily imprisonment limits
        if (!target.IsNPC)
            currentPlayer.PlayerImprisonedToday = true;
        else
            currentPlayer.NPCsImprisonedToday++;

        await Task.Delay(2000);
    }

    private async Task PardonPrisoner()
    {
        if (currentKing.Prisoners.Count == 0)
        {
            terminal.SetColor("yellow");
            terminal.WriteLine(Loc.Get("castle.no_prisoners_pardon"));
            await Task.Delay(1500);
            return;
        }

        terminal.WriteLine("");
        terminal.SetColor("cyan");
        terminal.Write(Loc.Get("castle.pardon_prompt"));
        terminal.SetColor("white");
        string input = await terminal.ReadLineAsync();

        var keys = currentKing.Prisoners.Keys.ToList();
        if (!int.TryParse(input, out int idx) || idx < 1 || idx > keys.Count)
        {
            terminal.SetColor("red");
            terminal.WriteLine(Loc.Get("ui.invalid_selection"));
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
            terminal.WriteLine(Loc.Get("castle.pardoned_confirm", name));
            NewsSystem.Instance.Newsy(true, $"{currentKing.GetTitle()} {currentKing.Name} pardoned {name}!");
        }
        else
        {
            terminal.SetColor("red");
            terminal.WriteLine(Loc.Get("castle.not_in_dungeon"));
        }

        await Task.Delay(2000);
    }

    private async Task ExecutePrisoner()
    {
        if (currentKing.Prisoners.Count == 0)
        {
            terminal.SetColor("yellow");
            terminal.WriteLine(Loc.Get("castle.no_prisoners_execute"));
            await Task.Delay(1500);
            return;
        }

        terminal.WriteLine("");
        terminal.SetColor("bright_red");
        terminal.WriteLine(Loc.Get("castle.execution_warning"));
        terminal.SetColor("cyan");
        terminal.Write(Loc.Get("castle.execute_prompt"));
        terminal.SetColor("white");
        string input = await terminal.ReadLineAsync();

        var keys = currentKing.Prisoners.Keys.ToList();
        if (!int.TryParse(input, out int idx) || idx < 1 || idx > keys.Count)
        {
            terminal.SetColor("red");
            terminal.WriteLine(Loc.Get("ui.invalid_selection"));
            await Task.Delay(1500);
            return;
        }

        string name = keys[idx - 1];
        terminal.SetColor("cyan");
        terminal.Write(Loc.Get("castle.execute_confirm_prompt", name));
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
                    await backend.DeductGoldByPercentage(name, 10);
                }
                PersistRoyalCourtToWorldState();
            }

            terminal.SetColor("red");
            terminal.WriteLine(Loc.Get("castle.executed_confirm", name));
            terminal.WriteLine(Loc.Get("castle.darkness_increases"));
            NewsSystem.Instance.Newsy(true, $"{currentKing.GetTitle()} {currentKing.Name} executed {name}!");

            // Server-wide broadcast of execution
            if (DoorMode.IsOnlineMode)
            {
                UsurperRemake.Server.MudServer.Instance?.BroadcastToAll(
                    $"\u001b[1;31m  *** {currentKing.GetTitle()} {currentKing.Name} has executed {name}! ***\u001b[0m");
            }

            // Track executions
            currentPlayer.ExecutionsToday++;
            currentPlayer.TotalExecutions++;

            // Rebellion trigger: 5+ total executions = the people rise up
            if (currentPlayer.TotalExecutions >= 5)
            {
                await TriggerRebellion();
                return;
            }
            else if (currentPlayer.TotalExecutions >= 4)
            {
                terminal.SetColor("bright_yellow");
                terminal.WriteLine("");
                terminal.WriteLine("  Whispers of rebellion spread through the streets...");
                terminal.WriteLine("  The people will not tolerate this tyranny much longer.");
            }
            else if (currentPlayer.TotalExecutions >= 3)
            {
                terminal.SetColor("yellow");
                terminal.WriteLine("");
                terminal.WriteLine("  The townspeople watch in fearful silence.");
            }

            // Prevent save cheesing — persist negative outcomes immediately
            if (DoorMode.IsOnlineMode)
                _ = GameEngine.Instance.SaveCurrentGame();
        }
        else
        {
            terminal.SetColor("gray");
            terminal.WriteLine(Loc.Get("castle.execution_cancelled"));
        }

        await Task.Delay(2000);
    }

    /// <summary>
    /// Rebellion triggered by excessive executions. The king is overthrown,
    /// imprisoned, and faces the executioner. A coin flip determines their fate:
    /// Heads = character permanently deleted. Tails = stripped, beaten, humiliated, but alive.
    /// </summary>
    private async Task TriggerRebellion()
    {
        string kingName = currentPlayer.DisplayName;
        string title = currentPlayer.Sex == CharacterSex.Female ? "Queen" : "King";

        // === ACT 1: THE UPRISING ===
        terminal.ClearScreen();
        terminal.SetColor("bright_red");
        terminal.WriteLine("");
        terminal.WriteLine("  ╔═══════════════════════════════════════════════════╗");
        terminal.WriteLine("  ║              THE PEOPLE HAVE RISEN!              ║");
        terminal.WriteLine("  ╚═══════════════════════════════════════════════════╝");
        terminal.WriteLine("");
        await Task.Delay(2000);

        terminal.SetColor("white");
        terminal.WriteLine("  You hear it before you see it.");
        terminal.WriteLine("  A low rumble, like distant thunder.");
        terminal.WriteLine("  But it's not thunder. It's voices. Hundreds of them.");
        terminal.WriteLine("");
        await Task.Delay(2500);

        terminal.SetColor("yellow");
        terminal.WriteLine("  The castle doors EXPLODE inward.");
        terminal.WriteLine("");
        await Task.Delay(1500);

        terminal.SetColor("white");
        terminal.WriteLine("  A tide of citizens floods the throne room — farmers,");
        terminal.WriteLine("  merchants, mothers clutching children, old soldiers");
        terminal.WriteLine("  with rusty swords. Their eyes burn with fury.");
        terminal.WriteLine("");
        await Task.Delay(2500);

        terminal.SetColor("bright_yellow");
        terminal.WriteLine($"  A woman at the front points at you.");
        terminal.WriteLine($"  \"THAT is the one! {currentPlayer.TotalExecutions} lives! {currentPlayer.TotalExecutions} of our");
        terminal.WriteLine($"   people fed to the executioner's blade!\"");
        terminal.WriteLine("");
        await Task.Delay(2500);

        terminal.SetColor("gray");
        terminal.WriteLine("  You look to your Royal Guard for protection.");
        terminal.WriteLine("  They stand motionless. Then, one by one, they");
        terminal.WriteLine("  lay down their weapons and step aside.");
        terminal.WriteLine("");
        await Task.Delay(2500);

        terminal.SetColor("white");
        terminal.WriteLine("  The captain of the guard removes his helm.");
        terminal.SetColor("cyan");
        terminal.WriteLine($"  \"I swore an oath to the crown, not to a butcher.\"");
        terminal.WriteLine("");
        await terminal.PressAnyKey();

        // === ACT 2: THE CAPTURE ===
        terminal.ClearScreen();
        terminal.SetColor("bright_red");
        terminal.WriteLine("");
        terminal.WriteLine("  ╔═══════════════════════════════════════════════════╗");
        terminal.WriteLine("  ║                   OVERTHROWN                     ║");
        terminal.WriteLine("  ╚═══════════════════════════════════════════════════╝");
        terminal.WriteLine("");
        await Task.Delay(1500);

        terminal.SetColor("white");
        terminal.WriteLine("  Rough hands seize you from the throne.");
        terminal.WriteLine("  Your crown is torn from your head and thrown");
        terminal.WriteLine("  to the marble floor, where it rings hollow.");
        terminal.WriteLine("");
        await Task.Delay(2000);

        terminal.SetColor("gray");
        terminal.WriteLine("  They drag you through the halls you once ruled.");
        terminal.WriteLine("  Servants you ordered around avert their eyes.");
        terminal.WriteLine("  Some spit as you pass.");
        terminal.WriteLine("");
        await Task.Delay(2000);

        terminal.SetColor("white");
        terminal.WriteLine("  Down the spiral stairs. Past the armory.");
        terminal.WriteLine("  Into the cold, dripping darkness of the dungeon.");
        terminal.WriteLine("");
        await Task.Delay(2000);

        terminal.SetColor("dark_red");
        terminal.WriteLine("  The iron door slams shut behind you.");
        terminal.WriteLine("  The lock turns with terrible finality.");
        terminal.WriteLine("");
        await Task.Delay(2000);

        terminal.SetColor("gray");
        terminal.WriteLine("  You sit in the dark.");
        terminal.WriteLine("  Water drips somewhere.");
        terminal.WriteLine("  Hours pass. Or days. You can't tell.");
        terminal.WriteLine("");
        await terminal.PressAnyKey();

        // === ACT 3: THE TRIAL ===
        terminal.ClearScreen();
        terminal.SetColor("bright_yellow");
        terminal.WriteLine("");
        terminal.WriteLine("  ╔═══════════════════════════════════════════════════╗");
        terminal.WriteLine("  ║              THE PEOPLE'S COURT                  ║");
        terminal.WriteLine("  ╚═══════════════════════════════════════════════════╝");
        terminal.WriteLine("");
        await Task.Delay(1500);

        terminal.SetColor("white");
        terminal.WriteLine("  They drag you into the town square.");
        terminal.WriteLine("  The entire population has gathered.");
        terminal.WriteLine("  A makeshift judge's bench. No defense. No mercy.");
        terminal.WriteLine("");
        await Task.Delay(2000);

        terminal.SetColor("cyan");
        terminal.WriteLine("  The judge — an elderly woman whose son you executed —");
        terminal.WriteLine("  reads the charges in a steady voice:");
        terminal.WriteLine("");
        await Task.Delay(1500);

        terminal.SetColor("bright_white");
        terminal.WriteLine($"  \"{title} {kingName}, you stand accused of the murder");
        terminal.WriteLine($"   of {currentPlayer.TotalExecutions} citizens under color of royal authority.\"");
        terminal.WriteLine("");
        terminal.WriteLine("  \"The law of this land is clear: a tyrant who rules");
        terminal.WriteLine("   through execution forfeits their right to rule —\"");
        terminal.WriteLine("");
        terminal.SetColor("bright_red");
        terminal.WriteLine("  \"— and perhaps their right to LIVE.\"");
        terminal.WriteLine("");
        await Task.Delay(2500);

        terminal.SetColor("white");
        terminal.WriteLine("  The crowd roars.");
        terminal.WriteLine("");
        await terminal.PressAnyKey();

        // === ACT 4: THE COIN ===
        terminal.ClearScreen();
        terminal.SetColor("bright_yellow");
        terminal.WriteLine("");
        terminal.WriteLine("  ╔═══════════════════════════════════════════════════╗");
        terminal.WriteLine("  ║              THE COIN OF FATE                    ║");
        terminal.WriteLine("  ╚═══════════════════════════════════════════════════╝");
        terminal.WriteLine("");
        await Task.Delay(1500);

        terminal.SetColor("white");
        terminal.WriteLine("  The judge reaches into her robe and produces");
        terminal.WriteLine("  an ancient coin — tarnished, heavy, inscribed");
        terminal.WriteLine("  with symbols older than the kingdom itself.");
        terminal.WriteLine("");
        await Task.Delay(2000);

        terminal.SetColor("cyan");
        terminal.WriteLine("  \"The gods will decide your fate.\"");
        terminal.WriteLine("");
        terminal.SetColor("white");
        terminal.WriteLine("  \"Heads — the executioner takes your life.\"");
        terminal.SetColor("gray");
        terminal.WriteLine("  \"Tails — you walk. Stripped of everything, but alive.\"");
        terminal.WriteLine("");
        await Task.Delay(2500);

        terminal.SetColor("bright_yellow");
        terminal.WriteLine("  She flips the coin.");
        terminal.WriteLine("");
        await Task.Delay(1500);

        terminal.SetColor("white");
        terminal.WriteLine("  It spins in the air...");
        await Task.Delay(1000);
        terminal.WriteLine("  catching the sunlight...");
        await Task.Delay(1000);
        terminal.WriteLine("  tumbling end over end...");
        await Task.Delay(1000);
        terminal.WriteLine("  the crowd holds its breath...");
        await Task.Delay(1500);
        terminal.WriteLine("");

        // THE COIN FLIP
        bool heads = Random.Shared.Next(2) == 0;

        // Release all prisoners before removing king (prevents orphaned prisoners)
        if (currentKing?.Prisoners != null)
        {
            foreach (var prisonerName in currentKing.Prisoners.Keys.ToList())
            {
                var npc = NPCSpawnSystem.Instance?.GetNPCByName(prisonerName, includeDead: true);
                if (npc != null)
                {
                    npc.DaysInPrison = 0;
                    npc.CurrentLocation = "MainStreet";
                }
                // Release online players too
                if (DoorMode.IsOnlineMode)
                {
                    var backend = SaveSystem.Instance.Backend as SqlSaveBackend;
                    if (backend != null)
                        _ = backend.ImprisonPlayer(prisonerName, 0);
                }
            }
            currentKing.Prisoners.Clear();
        }

        // Remove from throne (regardless of outcome)
        currentPlayer.King = false;
        if (currentPlayer.NobleTitle == "King" || currentPlayer.NobleTitle == "Queen")
            currentPlayer.NobleTitle = null;
        currentKing = null;

        if (heads)
        {
            // === HEADS: EXECUTION ===
            terminal.SetColor("bright_red");
            terminal.WriteLine("  ╔═══════════════════════════════╗");
            terminal.WriteLine("  ║           H E A D S           ║");
            terminal.WriteLine("  ╚═══════════════════════════════╝");
            terminal.WriteLine("");
            await Task.Delay(2000);

            terminal.SetColor("white");
            terminal.WriteLine("  The crowd erupts. Not in celebration.");
            terminal.WriteLine("  In grim satisfaction.");
            terminal.WriteLine("");
            await Task.Delay(2000);

            terminal.SetColor("gray");
            terminal.WriteLine("  They lead you to the executioner's block.");
            terminal.WriteLine("  The hooded figure tests the blade's edge");
            terminal.WriteLine("  with his thumb. A thin line of red appears.");
            terminal.WriteLine("  He nods.");
            terminal.WriteLine("");
            await Task.Delay(2500);

            terminal.SetColor("white");
            terminal.WriteLine("  You kneel.");
            terminal.WriteLine("  The wood is stained dark from years of use.");
            terminal.WriteLine("  You wonder how many of these stains are");
            terminal.WriteLine("  from the people YOU sent here.");
            terminal.WriteLine("");
            await Task.Delay(2500);

            terminal.SetColor("cyan");
            terminal.WriteLine("  The judge speaks one final time:");
            terminal.WriteLine($"  \"{kingName}, may the gods have more mercy");
            terminal.WriteLine("   on your soul than you had on theirs.\"");
            terminal.WriteLine("");
            await Task.Delay(2000);

            terminal.SetColor("gray");
            terminal.WriteLine("  The executioner raises the axe.");
            terminal.WriteLine("");
            await Task.Delay(2000);

            terminal.SetColor("dark_red");
            terminal.WriteLine("  ...");
            await Task.Delay(2000);

            terminal.SetColor("bright_red");
            terminal.WriteLine("  ╔═══════════════════════════════════════════════════╗");
            terminal.WriteLine("  ║          YOUR STORY ENDS HERE.                   ║");
            terminal.WriteLine("  ║                                                  ║");
            terminal.WriteLine("  ║   Your character has been permanently deleted.    ║");
            terminal.WriteLine("  ║   The kingdom remembers you as a cautionary       ║");
            terminal.WriteLine("  ║   tale about the price of tyranny.               ║");
            terminal.WriteLine("  ╚═══════════════════════════════════════════════════╝");
            terminal.WriteLine("");

            NewsSystem.Instance.Newsy(true, $"REBELLION! The tyrant {kingName} was overthrown and EXECUTED by the people's court!");

            // Server-wide broadcast
            if (DoorMode.IsOnlineMode)
            {
                UsurperRemake.Server.MudServer.Instance?.BroadcastToAll(
                    $"\u001b[1;31m  *** REBELLION! The tyrant {kingName} has been EXECUTED by the people! The kingdom is free! ***\u001b[0m");
            }

            // Delete the character and suppress disconnect save
            if (DoorMode.IsOnlineMode)
            {
                // Suppress the emergency save on disconnect so it doesn't overwrite the deletion
                string sessionUser = currentPlayer.Name2?.ToLowerInvariant() ?? "";
                if (UsurperRemake.Server.MudServer.Instance?.ActiveSessions.TryGetValue(sessionUser, out var session) == true)
                    session.SuppressDisconnectSave = true;

                var backend = SaveSystem.Instance.Backend as SqlSaveBackend;
                if (backend != null)
                {
                    string username = currentPlayer.Name2?.ToLowerInvariant() ?? currentPlayer.Name1?.ToLowerInvariant() ?? "";
                    if (!string.IsNullOrEmpty(username))
                    {
                        try
                        {
                            backend.DeleteGameData(username);
                            DebugLogger.Instance.LogInfo("REBELLION", $"Character '{kingName}' permanently deleted — coin was HEADS");
                        }
                        catch (Exception ex)
                        {
                            DebugLogger.Instance.Log(DebugLogger.LogLevel.Debug, "REBELLION", $"Failed to delete: {ex.Message}");
                        }
                    }
                }
            }
            else
            {
                SaveSystem.Instance.DeleteSave(currentPlayer.Name2 ?? currentPlayer.Name1 ?? "");
            }

            // Clear king from world state before disconnecting
            PersistRoyalCourtToWorldState();

            await terminal.PressAnyKey();
            throw new Exception("CHARACTER_DELETED_REBELLION");
        }
        else
        {
            // === TAILS: THE WALK OF SHAME ===
            terminal.SetColor("bright_green");
            terminal.WriteLine("  ╔═══════════════════════════════╗");
            terminal.WriteLine("  ║           T A I L S           ║");
            terminal.WriteLine("  ╚═══════════════════════════════╝");
            terminal.WriteLine("");
            await Task.Delay(2000);

            terminal.SetColor("white");
            terminal.WriteLine("  A murmur ripples through the crowd.");
            terminal.WriteLine("  Some cry out in protest. The judge raises her hand.");
            terminal.WriteLine("");
            await Task.Delay(2000);

            terminal.SetColor("cyan");
            terminal.WriteLine("  \"The gods have spoken. We are not the tyrant.\"");
            terminal.WriteLine("  \"We will not become what we sought to destroy.\"");
            terminal.WriteLine("");
            await Task.Delay(2000);

            terminal.SetColor("bright_yellow");
            terminal.WriteLine("  \"But mercy is not forgiveness.\"");
            terminal.WriteLine("");
            await terminal.PressAnyKey();

            // THE WALK OF SHAME
            terminal.ClearScreen();
            terminal.SetColor("bright_red");
            terminal.WriteLine("");
            terminal.WriteLine("  ╔═══════════════════════════════════════════════════╗");
            terminal.WriteLine("  ║              THE WALK OF SHAME                   ║");
            terminal.WriteLine("  ╚═══════════════════════════════════════════════════╝");
            terminal.WriteLine("");
            await Task.Delay(1500);

            terminal.SetColor("white");
            terminal.WriteLine("  They strip you of your royal garments.");
            terminal.WriteLine("  Your armor. Your weapons. Your rings.");
            terminal.WriteLine("  Everything you own, taken piece by piece");
            terminal.WriteLine("  and thrown to the crowd as trophies.");
            terminal.WriteLine("");
            await Task.Delay(2500);

            // Strip all equipment
            var allSlots = new[] {
                EquipmentSlot.MainHand, EquipmentSlot.OffHand, EquipmentSlot.Head,
                EquipmentSlot.Body, EquipmentSlot.Arms, EquipmentSlot.Hands,
                EquipmentSlot.Legs, EquipmentSlot.Feet, EquipmentSlot.Waist,
                EquipmentSlot.Face, EquipmentSlot.Cloak, EquipmentSlot.Neck,
                EquipmentSlot.LFinger, EquipmentSlot.RFinger
            };
            foreach (var slot in allSlots)
            {
                if (currentPlayer.GetEquipment(slot) != null)
                    currentPlayer.UnequipSlot(slot);
            }
            currentPlayer.Inventory.Clear(); // Everything confiscated

            terminal.SetColor("gray");
            terminal.WriteLine("  Dressed in nothing but rags, barefoot,");
            terminal.WriteLine("  they tie your hands behind your back");
            terminal.WriteLine("  and push you into the street.");
            terminal.WriteLine("");
            await Task.Delay(2000);

            terminal.SetColor("white");
            terminal.WriteLine("  A bell rings. Once. Twice. Three times.");
            terminal.WriteLine("");
            await Task.Delay(1500);

            terminal.SetColor("cyan");
            terminal.WriteLine("  A woman walks behind you, calling out:");
            terminal.SetColor("bright_red");
            terminal.WriteLine("  \"SHAME!\"");
            terminal.WriteLine("");
            await Task.Delay(1500);

            terminal.SetColor("white");
            terminal.WriteLine("  You walk the length of Main Street.");
            terminal.WriteLine("  Every face you pass is someone who");
            terminal.WriteLine("  lived under your rule.");
            terminal.WriteLine("");
            await Task.Delay(2000);

            terminal.SetColor("yellow");
            terminal.WriteLine("  Rotten vegetables hit your face.");
            terminal.WriteLine("  Someone throws a boot. It connects.");
            terminal.WriteLine("  Children point and laugh.");
            terminal.WriteLine("");
            await Task.Delay(2000);

            terminal.SetColor("bright_red");
            terminal.WriteLine("  \"SHAME!\"");
            terminal.WriteLine("");
            await Task.Delay(1500);

            terminal.SetColor("gray");
            terminal.WriteLine("  The walk takes an eternity.");
            terminal.WriteLine("  Past the weapon shop where you once browsed");
            terminal.WriteLine("  for the finest blades. Past the inn where");
            terminal.WriteLine("  companions once raised their cups to you.");
            terminal.WriteLine("  Past the healer who patched your wounds");
            terminal.WriteLine("  after battles you actually earned.");
            terminal.WriteLine("");
            await Task.Delay(3000);

            terminal.SetColor("bright_red");
            terminal.WriteLine("  \"SHAME!\"");
            terminal.WriteLine("");
            await Task.Delay(1500);

            terminal.SetColor("white");
            terminal.WriteLine("  At the town gates, they cut your bonds.");
            terminal.WriteLine("  The judge stands before you one last time.");
            terminal.WriteLine("");
            await Task.Delay(2000);

            terminal.SetColor("cyan");
            terminal.WriteLine($"  \"{kingName}. You leave this city with nothing.");
            terminal.WriteLine("   No gold. No gear. No title. No dignity.\"");
            terminal.WriteLine("");
            terminal.SetColor("bright_white");
            terminal.WriteLine("  \"But you leave with your life.\"");
            terminal.WriteLine("  \"Do not waste the gods' mercy.\"");
            terminal.WriteLine("");
            await Task.Delay(2500);

            // Apply penalties
            long goldLost = currentPlayer.Gold;
            currentPlayer.Gold = 0;
            currentPlayer.BankGold = 0; // Bank accounts seized
            currentPlayer.Fame = 0;
            currentPlayer.Chivalry = Math.Max(0, currentPlayer.Chivalry - 5000);
            currentPlayer.Darkness += 500;
            currentPlayer.TotalExecutions = 0; // Reset counter
            currentPlayer.RecalculateStats();

            terminal.SetColor("red");
            terminal.WriteLine("  ═══ Penalties ═══");
            terminal.WriteLine("  All equipment: CONFISCATED");
            terminal.WriteLine("  All inventory: CONFISCATED");
            terminal.WriteLine($"  All gold: SEIZED ({goldLost:N0}g on hand, bank accounts frozen)");
            terminal.WriteLine("  Fame: RESET to 0");
            terminal.WriteLine("  Chivalry: -5,000");
            terminal.WriteLine("  Darkness: +500");
            terminal.WriteLine("  Title: STRIPPED");
            terminal.WriteLine("");

            NewsSystem.Instance.Newsy(true, $"REBELLION! The tyrant {kingName} was overthrown! Stripped and paraded through the streets in shame!");
            if (DoorMode.IsOnlineMode)
            {
                UsurperRemake.Server.MudServer.Instance?.BroadcastToAll(
                    $"\u001b[1;31m  *** REBELLION! The tyrant {kingName} was paraded through the streets in shame! The kingdom is free! ***\u001b[0m");
            }

            // Save immediately
            _ = GameEngine.Instance.SaveCurrentGame();

            await terminal.PressAnyKey();

            // Force exit — the tyrant is banished from the castle
            PersistRoyalCourtToWorldState();
            await NavigateToLocation(GameLocation.MainStreet);
            return;
        }

        PersistRoyalCourtToWorldState();
    }

    private async Task SetBailAmount()
    {
        if (currentKing.Prisoners.Count == 0)
        {
            terminal.SetColor("yellow");
            terminal.WriteLine(Loc.Get("castle.no_prisoners_bail"));
            await Task.Delay(1500);
            return;
        }

        terminal.WriteLine("");
        terminal.SetColor("cyan");
        terminal.Write(Loc.Get("castle.bail_prompt"));
        terminal.SetColor("white");
        string input = await terminal.ReadLineAsync();

        var keys = currentKing.Prisoners.Keys.ToList();
        if (!int.TryParse(input, out int idx) || idx < 1 || idx > keys.Count)
        {
            terminal.SetColor("red");
            terminal.WriteLine(Loc.Get("ui.invalid_selection"));
            await Task.Delay(1500);
            return;
        }

        string name = keys[idx - 1];
        terminal.SetColor("cyan");
        terminal.Write(Loc.Get("castle.bail_amount_prompt", name));
        terminal.SetColor("white");
        string amountStr = await terminal.ReadLineAsync();

        if (long.TryParse(amountStr, out long amount) && amount >= 0)
        {
            currentKing.Prisoners[name].BailAmount = amount;
            terminal.SetColor("bright_green");
            if (amount > 0)
            {
                terminal.WriteLine(Loc.Get("castle.bail_set", amount.ToString("N0"), name));

                // Notify the prisoner that bail has been set
                if (DoorMode.IsOnlineMode)
                {
                    var backend = SaveSystem.Instance.Backend as SqlSaveBackend;
                    if (backend != null)
                    {
                        try
                        {
                            await backend.SendMessage("System", name, "system",
                                $"Bail has been set at {amount:N0} gold by {currentKing.GetTitle()} {currentKing.Name}. Use [B] Pay Bail in prison to purchase your freedom.");
                        }
                        catch { }
                    }

                    // Notify online player immediately
                    var mudServer = UsurperRemake.Server.MudServer.Instance;
                    if (mudServer != null)
                    {
                        foreach (var kvp in mudServer.ActiveSessions)
                        {
                            var sessPlayer = kvp.Value.Context?.Engine?.CurrentPlayer;
                            if (sessPlayer != null &&
                                (sessPlayer.DisplayName.Equals(name, StringComparison.OrdinalIgnoreCase) ||
                                 sessPlayer.Name2?.Equals(name, StringComparison.OrdinalIgnoreCase) == true))
                            {
                                kvp.Value.IncomingMessages.Enqueue(
                                    $"\u001b[1;33m  *** Bail has been set at {amount:N0} gold! Use [B] to pay and walk free. ***\u001b[0m");
                                break;
                            }
                        }
                    }
                }
            }
            else
                terminal.WriteLine(Loc.Get("castle.no_bail", name));

            if (DoorMode.IsOnlineMode)
                PersistRoyalCourtToWorldState();
        }

        await Task.Delay(2000);
    }

    // ═══════════════════════════════════════════════════════════════════════════
    // ROYAL MAIL SYSTEM
    // ═══════════════════════════════════════════════════════════════════════════

    private async Task ReadRoyalMail()
    {
        terminal.ClearScreen();
        WriteBoxHeader(Loc.Get("castle.royal_mail"), "bright_magenta");
        terminal.WriteLine("");

        // Generate some random mail if there's none
        if (royalMail.Count == 0)
        {
            GenerateRandomMail();
        }

        if (royalMail.Count == 0)
        {
            terminal.SetColor("gray");
            terminal.WriteLine(Loc.Get("castle.inbox_empty"));
        }
        else
        {
            terminal.SetColor("cyan");
            terminal.WriteLine($"{Loc.Get("castle.header_num"),-3} {Loc.Get("castle.header_from"),-20} {Loc.Get("castle.header_subject"),-35} {Loc.Get("castle.header_status"),-10}");
            WriteDivider(70);

            for (int i = 0; i < royalMail.Count; i++)
            {
                var mail = royalMail[i];
                string status = mail.IsRead ? Loc.Get("castle.mail_status_read") : Loc.Get("castle.mail_status_new");
                string statusColor = mail.IsRead ? "gray" : "bright_yellow";

                terminal.SetColor("white");
                terminal.Write($"{i + 1,-3} {mail.Sender,-20} {mail.Subject,-35} ");
                terminal.SetColor(statusColor);
                terminal.WriteLine(status);
            }
        }

        terminal.WriteLine("");
        terminal.SetColor("cyan");
        terminal.Write(Loc.Get("castle.read_message_prompt"));
        terminal.SetColor("white");
        string input = await terminal.ReadLineAsync();

        if (int.TryParse(input, out int choice) && choice >= 1 && choice <= royalMail.Count)
        {
            var mail = royalMail[choice - 1];
            mail.IsRead = true;

            terminal.ClearScreen();
            terminal.SetColor("bright_cyan");
            terminal.WriteLine(Loc.Get("castle.mail_from", mail.Sender));
            terminal.WriteLine(Loc.Get("castle.mail_subject", mail.Subject));
            WriteDivider(60);
            terminal.SetColor("white");
            terminal.WriteLine("");
            terminal.WriteLine(mail.Body);
            terminal.WriteLine("");

            terminal.SetColor("darkgray");
            terminal.WriteLine(Loc.Get("ui.press_enter"));
            await terminal.ReadKeyAsync();
        }
    }

    private void GenerateRandomMail()
    {
        string[] senders = { Loc.Get("castle.mail_sender_advisor"), Loc.Get("castle.mail_sender_steward"), Loc.Get("castle.mail_sender_priest"), Loc.Get("castle.mail_sender_guild"), Loc.Get("castle.mail_sender_merchant"), Loc.Get("castle.mail_sender_elder") };
        string[] subjects = { Loc.Get("castle.mail_subj_treasury"), Loc.Get("castle.mail_subj_affairs"), Loc.Get("castle.mail_subj_audience"), Loc.Get("castle.mail_subj_trade"), Loc.Get("castle.mail_subj_security"), Loc.Get("castle.mail_subj_festival") };
        string[] bodies = {
            Loc.Get("castle.mail_body_1"),
            Loc.Get("castle.mail_body_2"),
            Loc.Get("castle.mail_body_3"),
            Loc.Get("castle.mail_body_4"),
            Loc.Get("castle.mail_body_5")
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
        WriteBoxHeader(Loc.Get("castle.royal_chambers"), "bright_blue");
        terminal.WriteLine("");

        terminal.SetColor("white");
        terminal.WriteLine(Loc.Get("castle.retire_chambers"));
        terminal.WriteLine(Loc.Get("castle.servants_bath"));
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
            terminal.WriteLine(Loc.Get("castle.shadow_blessing_fades"), "gray");
            currentPlayer.GroggoShadowBlessingDex = 0;
        }

        if (DoorMode.IsOnlineMode)
        {
            // Online mode: King sleeps in the castle (protected by royal guards) and logs out
            // Day does NOT increment — only the 7 PM ET maintenance reset does that
            terminal.SetColor("bright_green");
            terminal.WriteLine(Loc.Get("castle.rest_peacefully"));
            terminal.WriteLine("");
            terminal.WriteLine(Loc.Get("castle.hp_restored", currentPlayer.MaxHP));
            terminal.WriteLine(Loc.Get("castle.mana_restored", currentPlayer.MaxMana));
            terminal.WriteLine("");

            // Show kingdom status
            terminal.SetColor("cyan");
            terminal.WriteLine(Loc.Get("castle.kingdom_status"));
            terminal.SetColor("white");
            terminal.WriteLine(Loc.Get("castle.treasury_balance", currentKing.Treasury.ToString("N0")));
            terminal.WriteLine(Loc.Get("castle.days_reign", currentKing.TotalReign));
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
            terminal.WriteLine(Loc.Get("castle.guards_watch"));
            await Task.Delay(2000);

            throw new LocationExitException(GameLocation.NoWhere);
        }
        else
        {
            // Single-player mode: process daily activities and continue
            currentKing.ProcessDailyActivities();

            terminal.SetColor("bright_green");
            terminal.WriteLine(Loc.Get("castle.rest_night"));
            terminal.WriteLine("");
            terminal.WriteLine(Loc.Get("castle.hp_restored", currentPlayer.MaxHP));
            terminal.WriteLine(Loc.Get("castle.mana_restored", currentPlayer.MaxMana));
            terminal.WriteLine("");

            // Show daily report
            terminal.SetColor("cyan");
            terminal.WriteLine(Loc.Get("castle.daily_report"));
            terminal.SetColor("white");
            terminal.WriteLine(Loc.Get("castle.daily_income_label", currentKing.CalculateDailyIncome().ToString("N0")));
            terminal.WriteLine(Loc.Get("castle.daily_expenses_label", currentKing.CalculateDailyExpenses().ToString("N0")));
            terminal.WriteLine(Loc.Get("castle.treasury_balance", currentKing.Treasury.ToString("N0")));
            terminal.WriteLine(Loc.Get("castle.days_reign", currentKing.TotalReign));

            terminal.WriteLine("");
            terminal.SetColor("darkgray");
            terminal.WriteLine(Loc.Get("ui.press_enter"));
            await terminal.ReadKeyAsync();
        }
    }

    // ═══════════════════════════════════════════════════════════════════════════
    // SECURITY CHECK
    // ═══════════════════════════════════════════════════════════════════════════

    private async Task CheckSecurity()
    {
        terminal.ClearScreen();
        WriteBoxHeader(Loc.Get("castle.security_report"), "bright_cyan");
        terminal.WriteLine("");

        // NPC Guard summary
        terminal.SetColor("cyan");
        terminal.WriteLine(Loc.Get("castle.guard_status_header"));
        terminal.SetColor("white");
        terminal.WriteLine(Loc.Get("castle.npc_guards", currentKing.Guards.Count, King.MaxNPCGuards));

        if (currentKing.Guards.Count > 0)
        {
            int avgLoyalty = (int)currentKing.Guards.Average(g => g.Loyalty);
            string loyaltyStatus = avgLoyalty > 80 ? Loc.Get("castle.loyalty_excellent") : avgLoyalty > 60 ? Loc.Get("castle.loyalty_good") : avgLoyalty > 40 ? Loc.Get("castle.loyalty_fair") : Loc.Get("castle.loyalty_poor");
            terminal.WriteLine(Loc.Get("castle.avg_loyalty", avgLoyalty, loyaltyStatus));
            terminal.WriteLine(Loc.Get("castle.daily_guard_costs", currentKing.Guards.Sum(g => g.DailySalary).ToString("N0")));
            terminal.WriteLine("");

            // Individual guard listing
            terminal.SetColor("cyan");
            terminal.WriteLine($"{Loc.Get("castle.header_name"),-20} {Loc.Get("castle.header_loyalty"),-12} {Loc.Get("castle.header_salary"),-12} {Loc.Get("castle.header_status"),-10}");
            WriteDivider(55);

            foreach (var guard in currentKing.Guards)
            {
                string loyaltyColor = guard.Loyalty > 80 ? "bright_green" :
                                     guard.Loyalty > 50 ? "yellow" : "red";
                string status = guard.IsActive ? Loc.Get("castle.guard_active") : Loc.Get("castle.guard_inactive");

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
            terminal.WriteLine(Loc.Get("castle.no_npc_guards"));
        }

        terminal.WriteLine("");

        // Monster Guard summary
        terminal.SetColor("bright_red");
        terminal.WriteLine(Loc.Get("castle.monster_guards_header"));
        terminal.SetColor("white");
        terminal.WriteLine(Loc.Get("castle.monster_guards_count", currentKing.MonsterGuards.Count, King.MaxMonsterGuards));

        if (currentKing.MonsterGuards.Count > 0)
        {
            terminal.WriteLine(Loc.Get("castle.daily_feeding_costs", currentKing.MonsterGuards.Sum(m => m.DailyFeedingCost).ToString("N0")));
            terminal.WriteLine("");

            terminal.SetColor("cyan");
            terminal.WriteLine($"{Loc.Get("castle.header_name"),-20} {Loc.Get("castle.header_level"),-8} {Loc.Get("castle.header_hp"),-12} {Loc.Get("castle.header_strength"),-10}");
            WriteDivider(55);

            foreach (var monster in currentKing.MonsterGuards)
            {
                terminal.SetColor("red");
                terminal.WriteLine($"{monster.Name,-20} {monster.Level,-8} {monster.HP}/{monster.MaxHP,-8} {monster.Strength,-10}");
            }
        }
        else
        {
            terminal.SetColor("gray");
            terminal.WriteLine(Loc.Get("castle.no_monster_guards"));
        }

        terminal.WriteLine("");

        // Security assessment
        terminal.SetColor("cyan");
        terminal.WriteLine(Loc.Get("castle.security_assessment_header"));
        int securityLevel = CalculateSecurityLevel();
        string securityColor = securityLevel > 80 ? "bright_green" : securityLevel > 50 ? "yellow" : "red";
        terminal.SetColor(securityColor);
        terminal.WriteLine(Loc.Get("castle.overall_security", securityLevel));
        terminal.SetColor("white");
        terminal.WriteLine(Loc.Get("castle.total_guards_info", currentKing.TotalGuardCount));

        if (securityLevel < 50)
        {
            terminal.SetColor("red");
            terminal.WriteLine(Loc.Get("castle.castle_vulnerable"));
        }

        terminal.WriteLine("");
        terminal.SetColor("cyan");
        terminal.WriteLine(Loc.Get("castle.guard_commands"));
        if (IsScreenReader)
        {
            WriteSRMenuOption("H", Loc.Get("castle.hire_guard"));
            WriteSRMenuOption("M", Loc.Get("castle.monster_guard"));
            WriteSRMenuOption("F", Loc.Get("castle.fire_guard"));
            WriteSRMenuOption("P", Loc.Get("castle.pay_bonus"));
            WriteSRMenuOption("D", Loc.Get("castle.dismiss_monster"));
            WriteSRMenuOption("R", Loc.Get("castle.return"));
        }
        else
        {
            terminal.SetColor("white");
            terminal.WriteLine(Loc.Get("castle.guard_menu_text_1"));
            terminal.WriteLine(Loc.Get("castle.guard_menu_text_2"));
        }
        terminal.WriteLine("");

        terminal.SetColor("cyan");
        terminal.Write(Loc.Get("castle.command_prompt"));
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
            terminal.WriteLine(Loc.Get("castle.max_monster_guards"));
            await Task.Delay(2000);
            return;
        }

        terminal.ClearScreen();
        WriteBoxHeader(Loc.Get("castle.monster_market"), "bright_red");
        terminal.WriteLine("");

        terminal.SetColor("white");
        terminal.WriteLine(Loc.Get("castle.beast_master_desc"));
        terminal.WriteLine(Loc.Get("castle.challengers_must_defeat"));
        terminal.WriteLine("");
        terminal.SetColor("yellow");
        terminal.WriteLine(Loc.Get("castle.treasury_display", currentKing.Treasury.ToString("N0")));
        terminal.WriteLine(Loc.Get("castle.current_monsters", currentKing.MonsterGuards.Count, King.MaxMonsterGuards));
        terminal.WriteLine("");

        terminal.SetColor("cyan");
        terminal.WriteLine($"{Loc.Get("castle.header_num"),-3} {Loc.Get("castle.header_name"),-15} {Loc.Get("castle.header_lvl"),-5} {Loc.Get("castle.header_hp"),-7} {Loc.Get("castle.header_str"),-6} {Loc.Get("castle.header_def"),-6} {Loc.Get("castle.header_cost"),-10} {Loc.Get("castle.header_feed"),-10}");
        WriteDivider(75);

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
        terminal.WriteLine(Loc.Get("castle.monster_cost_note"));
        terminal.WriteLine("");
        terminal.SetColor("cyan");
        terminal.Write(Loc.Get("castle.purchase_monster"));
        terminal.SetColor("white");
        string input = await terminal.ReadLineAsync();

        if (int.TryParse(input, out int choice) && choice >= 1 && choice <= sortedMonsters.Length)
        {
            var (name, level, cost) = sortedMonsters[choice - 1];

            if (currentKing.AddMonsterGuard(name, level, cost))
            {
                terminal.SetColor("bright_green");
                terminal.WriteLine(Loc.Get("castle.monster_added", name));
                terminal.WriteLine(Loc.Get("castle.beast_lurks"));
                NewsSystem.Instance.Newsy(true, $"{currentKing.GetTitle()} {currentKing.Name} acquired a fearsome {name} to guard the castle!");
                PersistRoyalCourtToWorldState();
            }
            else
            {
                terminal.SetColor("red");
                terminal.WriteLine(Loc.Get("castle.insufficient_treasury"));
            }
        }

        await Task.Delay(2500);
    }

    private async Task DismissMonsterGuard()
    {
        if (currentKing.MonsterGuards.Count == 0)
        {
            terminal.SetColor("yellow");
            terminal.WriteLine(Loc.Get("castle.no_monster_dismiss"));
            await Task.Delay(2000);
            return;
        }

        terminal.SetColor("cyan");
        terminal.Write(Loc.Get("castle.monster_dismiss_prompt"));
        terminal.SetColor("white");
        string name = await terminal.ReadLineAsync();

        if (currentKing.RemoveMonsterGuard(name))
        {
            terminal.SetColor("yellow");
            terminal.WriteLine(Loc.Get("castle.monster_released", name));
            PersistRoyalCourtToWorldState();
        }
        else
        {
            terminal.SetColor("red");
            terminal.WriteLine(Loc.Get("castle.no_monster_found"));
        }

        await Task.Delay(2000);
    }

    private async Task HireGuard()
    {
        if (currentKing.Guards.Count >= King.MaxNPCGuards)
        {
            terminal.SetColor("yellow");
            terminal.WriteLine(Loc.Get("castle.max_guards"));
            await Task.Delay(2000);
            return;
        }

        if (currentKing.Treasury < GameConfig.GuardRecruitmentCost)
        {
            terminal.SetColor("red");
            terminal.WriteLine(Loc.Get("castle.insufficient_guard_funds", GameConfig.GuardRecruitmentCost.ToString("N0")));
            await Task.Delay(2000);
            return;
        }

        // Generate random guard name
        string[] firstNames = { "Sir Marcus", "Sir Gerald", "Lady Helena", "Sir Roland", "Dame Elise", "Sir Bartholomew", "Lady Catherine", "Sir Edmund" };
        string guardName = firstNames[random.Next(firstNames.Length)];

        terminal.SetColor("cyan");
        terminal.Write(Loc.Get("castle.hire_guard_confirm", guardName, GameConfig.GuardRecruitmentCost.ToString("N0")));
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
                terminal.WriteLine(Loc.Get("castle.guard_joined", guardName));
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
            terminal.WriteLine(Loc.Get("castle.no_guards_dismiss"));
            await Task.Delay(2000);
            return;
        }

        terminal.SetColor("cyan");
        terminal.Write(Loc.Get("castle.guard_dismiss_prompt"));
        terminal.SetColor("white");
        string name = await terminal.ReadLineAsync();

        if (currentKing.RemoveGuard(name))
        {
            terminal.SetColor("yellow");
            terminal.WriteLine(Loc.Get("castle.guard_dismissed", name));
            PersistRoyalCourtToWorldState();
        }
        else
        {
            terminal.SetColor("red");
            terminal.WriteLine(Loc.Get("castle.no_guard_found"));
        }

        await Task.Delay(2000);
    }

    private async Task PayGuardBonus()
    {
        if (currentKing.Guards.Count == 0)
        {
            terminal.SetColor("yellow");
            terminal.WriteLine(Loc.Get("castle.no_guards_pay"));
            await Task.Delay(2000);
            return;
        }

        terminal.SetColor("cyan");
        terminal.Write(Loc.Get("castle.bonus_per_guard"));
        terminal.SetColor("white");
        string input = await terminal.ReadLineAsync();

        if (long.TryParse(input, out long bonus) && bonus > 0)
        {
            long totalCost = bonus * currentKing.Guards.Count;
            if (totalCost > currentKing.Treasury)
            {
                terminal.SetColor("red");
                terminal.WriteLine(Loc.Get("castle.insufficient_treasury_bonus"));
            }
            else
            {
                currentKing.Treasury -= totalCost;
                foreach (var guard in currentKing.Guards)
                {
                    guard.Loyalty = Math.Min(100, guard.Loyalty + (int)(bonus / 100));
                }
                terminal.SetColor("bright_green");
                terminal.WriteLine(Loc.Get("castle.bonus_paid", bonus.ToString("N0")));
                terminal.WriteLine(Loc.Get("castle.loyalty_increased"));
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
        WriteBoxHeader(Loc.Get("castle.history_monarchs"), "bright_yellow");
        terminal.WriteLine("");

        if (monarchHistory.Count == 0)
        {
            terminal.SetColor("gray");
            terminal.WriteLine(Loc.Get("castle.history_empty"));
            terminal.WriteLine("");
        }
        else
        {
            terminal.SetColor("cyan");
            terminal.WriteLine($"{Loc.Get("castle.header_num"),-3} {Loc.Get("castle.header_name"),-25} {Loc.Get("castle.header_title"),-8} {Loc.Get("castle.header_reign"),-12} {Loc.Get("castle.header_end"),-15}");
            WriteDivider(65);

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
            terminal.WriteLine(Loc.Get("castle.current_monarch_header"));
            terminal.SetColor("white");
            terminal.WriteLine($"{currentKing.GetTitle()} {currentKing.Name}");
            terminal.WriteLine(Loc.Get("castle.reign_days", currentKing.TotalReign));
            terminal.WriteLine(Loc.Get("castle.coronation_date", currentKing.CoronationDate.ToString("d")));
        }

        terminal.WriteLine("");
        terminal.SetColor("darkgray");
        terminal.WriteLine(Loc.Get("ui.press_enter"));
        await terminal.ReadKeyAsync();
    }

    // ═══════════════════════════════════════════════════════════════════════════
    // COURT MAGICIAN
    // ═══════════════════════════════════════════════════════════════════════════

    private async Task CourtMagician()
    {
        terminal.ClearScreen();
        WriteBoxHeader(Loc.Get("castle.court_magician"), "bright_magenta");
        terminal.WriteLine("");

        terminal.SetColor("cyan");
        terminal.WriteLine(Loc.Get("castle.wizard_approaches"));
        terminal.SetColor("white");
        terminal.WriteLine(Loc.Get("castle.wizard_greeting"));
        terminal.WriteLine("");

        terminal.SetColor("yellow");
        terminal.WriteLine(Loc.Get("castle.magic_budget", currentKing.MagicBudget.ToString("N0")));
        terminal.WriteLine("");

        terminal.SetColor("cyan");
        terminal.WriteLine(Loc.Get("castle.available_services"));
        if (IsScreenReader)
        {
            WriteSRMenuOption("B", Loc.Get("castle.bless_kingdom"));
            WriteSRMenuOption("D", Loc.Get("castle.detect_threats"));
            WriteSRMenuOption("P", Loc.Get("castle.protection_spell"));
            WriteSRMenuOption("S", Loc.Get("castle.scry_enemy"));
            WriteSRMenuOption("R", Loc.Get("castle.return"));
        }
        else
        {
            terminal.SetColor("white");
            terminal.WriteLine(Loc.Get("castle.bless_menu"));
            terminal.WriteLine(Loc.Get("castle.detect_menu"));
            terminal.WriteLine(Loc.Get("castle.protection_menu"));
            terminal.WriteLine(Loc.Get("castle.scry_menu"));
            terminal.WriteLine(Loc.Get("castle.return_menu"));
        }
        terminal.WriteLine("");

        terminal.SetColor("cyan");
        terminal.Write(Loc.Get("castle.your_wish"));
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
            terminal.WriteLine(Loc.Get("castle.insufficient_magic"));
            await Task.Delay(2000);
            return;
        }

        currentKing.MagicBudget -= 1000;
        currentPlayer.Chivalry += 25;

        terminal.SetColor("bright_yellow");
        terminal.WriteLine("");
        terminal.WriteLine(Loc.Get("castle.wizard_incants"));
        await Task.Delay(1500);
        terminal.SetColor("bright_green");
        terminal.WriteLine(Loc.Get("castle.golden_light"));
        terminal.WriteLine(Loc.Get("castle.people_blessed"));

        NewsSystem.Instance.Newsy(true, $"{currentKing.GetTitle()} {currentKing.Name} blessed the kingdom with powerful magic!");

        await Task.Delay(2500);
    }

    private async Task CastDetectThreats()
    {
        if (currentKing.MagicBudget < 500)
        {
            terminal.SetColor("red");
            terminal.WriteLine(Loc.Get("castle.insufficient_magic"));
            await Task.Delay(2000);
            return;
        }

        currentKing.MagicBudget -= 500;

        terminal.SetColor("bright_blue");
        terminal.WriteLine("");
        terminal.WriteLine(Loc.Get("castle.wizard_gazes"));
        await Task.Delay(1500);

        // Get potential threats (high darkness NPCs)
        var threats = NPCSpawnSystem.Instance.ActiveNPCs
            .Where(n => n.IsAlive && n.Darkness > 300)
            .Take(3)
            .ToList();

        if (threats.Count == 0)
        {
            terminal.SetColor("bright_green");
            terminal.WriteLine(Loc.Get("castle.no_threats"));
        }
        else
        {
            terminal.SetColor("yellow");
            terminal.WriteLine(Loc.Get("castle.sense_darkness"));
            terminal.WriteLine("");
            foreach (var threat in threats)
            {
                terminal.SetColor("red");
                terminal.WriteLine(Loc.Get("castle.threat_entry", threat.Name, threat.Level, threat.Darkness));
            }
        }

        await Task.Delay(3000);
    }

    private async Task CastProtection()
    {
        if (currentKing.MagicBudget < 2000)
        {
            terminal.SetColor("red");
            terminal.WriteLine(Loc.Get("castle.insufficient_magic"));
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
        terminal.WriteLine(Loc.Get("castle.wizard_enchants"));
        await Task.Delay(1500);
        terminal.SetColor("bright_green");
        terminal.WriteLine(Loc.Get("castle.castle_glows"));
        terminal.WriteLine(Loc.Get("castle.guard_loyalty_up"));

        await Task.Delay(2500);
    }

    private async Task CastScry()
    {
        if (currentKing.MagicBudget < 1500)
        {
            terminal.SetColor("red");
            terminal.WriteLine(Loc.Get("castle.insufficient_magic"));
            await Task.Delay(2000);
            return;
        }

        currentKing.MagicBudget -= 1500;

        terminal.SetColor("bright_blue");
        terminal.WriteLine("");
        terminal.WriteLine(Loc.Get("castle.wizard_peers"));
        await Task.Delay(1500);

        // Show info about a random powerful NPC
        var targets = NPCSpawnSystem.Instance.ActiveNPCs
            .Where(n => n.IsAlive && n.Level > 5)
            .ToList();

        if (targets.Count > 0)
        {
            var target = targets[random.Next(targets.Count)];
            terminal.SetColor("cyan");
            terminal.WriteLine(Loc.Get("castle.scry_see", target.Name));
            terminal.SetColor("white");
            terminal.WriteLine(Loc.Get("castle.scry_level", target.Level));
            terminal.WriteLine(Loc.Get("castle.scry_location", target.CurrentLocation));
            terminal.WriteLine($"  {Loc.Get("ui.team")}: {(string.IsNullOrEmpty(target.Team) ? Loc.Get("ui.none") : target.Team)}");
            terminal.WriteLine($"  {Loc.Get("ui.gold")}: ~{target.Gold:N0}");
        }
        else
        {
            terminal.SetColor("gray");
            terminal.WriteLine(Loc.Get("castle.mists_nothing"));
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
            WriteBoxHeader(Loc.Get("castle.fiscal_matters"), "bright_yellow");
            terminal.WriteLine("");

            terminal.SetColor("white");
            terminal.WriteLine(Loc.Get("castle.royal_treasury", currentKing.Treasury.ToString("N0")));
            terminal.WriteLine(Loc.Get("castle.daily_citizen_tax", currentKing.TaxRate));
            terminal.WriteLine(Loc.Get("castle.tax_alignment_label", currentKing.TaxAlignment));
            terminal.SetColor("yellow");
            terminal.WriteLine(Loc.Get("castle.king_sales_tax", currentKing.KingTaxPercent));
            terminal.WriteLine(Loc.Get("castle.city_team_tax", currentKing.CityTaxPercent));
            terminal.SetColor("gray");
            terminal.WriteLine(Loc.Get("castle.sales_tax_revenue", currentKing.DailyTaxRevenue.ToString("N0")));
            terminal.WriteLine("");

            terminal.SetColor("cyan");
            terminal.WriteLine(Loc.Get("castle.financial_summary"));
            terminal.SetColor("bright_green");
            terminal.WriteLine(Loc.Get("castle.daily_income", currentKing.CalculateDailyIncome().ToString("N0")));
            terminal.SetColor("red");
            terminal.WriteLine(Loc.Get("castle.daily_expenses", currentKing.CalculateDailyExpenses().ToString("N0")));
            terminal.SetColor("white");
            long netIncome = currentKing.CalculateDailyIncome() - currentKing.CalculateDailyExpenses();
            string netColor = netIncome >= 0 ? "bright_green" : "red";
            terminal.SetColor(netColor);
            terminal.WriteLine(Loc.Get("castle.net_daily", netIncome.ToString("N0")));
            terminal.WriteLine("");

            terminal.SetColor("cyan");
            terminal.WriteLine(Loc.Get("castle.options_label"));
            if (IsScreenReader)
            {
                WriteSRMenuOption("T", Loc.Get("castle.tax_policy"));
                WriteSRMenuOption("B", Loc.Get("castle.budget"));
                WriteSRMenuOption("W", Loc.Get("castle.treasury_withdraw"));
                WriteSRMenuOption("D", Loc.Get("castle.treasury_deposit"));
                WriteSRMenuOption("R", Loc.Get("castle.return"));
            }
            else
            {
                terminal.SetColor("white");
                terminal.WriteLine(Loc.Get("castle.fiscal_menu_1"));
                terminal.WriteLine(Loc.Get("castle.fiscal_menu_2"));
            }
            terminal.WriteLine("");

            terminal.SetColor("cyan");
            terminal.Write(Loc.Get("castle.decision_prompt"));
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
        terminal.WriteLine(Loc.Get("castle.tax_alignments"));
        terminal.SetColor("white");
        terminal.WriteLine(Loc.Get("castle.tax_all"));
        terminal.WriteLine(Loc.Get("castle.tax_good"));
        terminal.WriteLine(Loc.Get("castle.tax_evil"));
        terminal.WriteLine(Loc.Get("castle.tax_neutral"));
        terminal.WriteLine("");

        terminal.SetColor("cyan");
        terminal.Write(Loc.Get("castle.new_tax_alignment"));
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
        terminal.Write(Loc.Get("castle.new_citizen_tax"));
        terminal.SetColor("white");
        string rateInput = await terminal.ReadLineAsync();

        if (long.TryParse(rateInput, out long rate) && rate >= 0)
        {
            currentKing.TaxRate = Math.Min(rate, 1000);
            terminal.SetColor("bright_green");
            terminal.WriteLine(Loc.Get("castle.citizen_tax_set", currentKing.TaxRate));

            if (rate > 100)
            {
                terminal.SetColor("yellow");
                terminal.WriteLine(Loc.Get("castle.high_tax_warning"));
            }
        }

        terminal.WriteLine("");
        terminal.SetColor("cyan");
        terminal.Write(Loc.Get("castle.king_sales_tax_prompt", currentKing.KingTaxPercent));
        terminal.SetColor("white");
        string kingTaxInput = await terminal.ReadLineAsync();

        if (int.TryParse(kingTaxInput, out int kingTax) && kingTax >= 0)
        {
            currentKing.KingTaxPercent = Math.Min(kingTax, 25);
            terminal.SetColor("bright_green");
            terminal.WriteLine(Loc.Get("castle.king_sales_tax_set", currentKing.KingTaxPercent));

            if (kingTax > 15)
            {
                terminal.SetColor("yellow");
                terminal.WriteLine(Loc.Get("castle.high_sales_warning"));
            }
        }

        terminal.SetColor("cyan");
        terminal.Write(Loc.Get("castle.city_tax_prompt", currentKing.CityTaxPercent));
        terminal.SetColor("white");
        string cityTaxInput = await terminal.ReadLineAsync();

        if (int.TryParse(cityTaxInput, out int cityTax) && cityTax >= 0)
        {
            currentKing.CityTaxPercent = Math.Min(cityTax, 10);
            terminal.SetColor("bright_green");
            terminal.WriteLine(Loc.Get("castle.city_tax_set", currentKing.CityTaxPercent));
        }

        // Persist tax changes to world_state
        PersistRoyalCourtToWorldState();

        await Task.Delay(2000);
    }

    private async Task ShowBudgetDetails()
    {
        terminal.WriteLine("");
        terminal.SetColor("cyan");
        terminal.WriteLine(Loc.Get("castle.income_breakdown"));
        terminal.SetColor("white");

        int npcCount = UsurperRemake.Systems.NPCSpawnSystem.Instance?.ActiveNPCs?.Count ?? 10;
        long citizenTaxIncome = currentKing.TaxRate * Math.Max(10, npcCount);
        long salesTaxIncome = currentKing.DailyTaxRevenue;

        terminal.WriteLine(Loc.Get("castle.citizen_tax_income", citizenTaxIncome.ToString("N0"), currentKing.TaxRate, Math.Max(10, npcCount)));
        terminal.WriteLine(Loc.Get("castle.sales_tax_income", salesTaxIncome.ToString("N0"), currentKing.KingTaxPercent));
        WriteDivider(40);
        terminal.SetColor("bright_green");
        terminal.WriteLine(Loc.Get("castle.total_income", (citizenTaxIncome + salesTaxIncome).ToString("N0")));

        terminal.WriteLine("");
        terminal.SetColor("cyan");
        terminal.WriteLine(Loc.Get("castle.expense_breakdown"));
        terminal.SetColor("white");

        long guardCosts = currentKing.Guards.Sum(g => g.DailySalary);
        long orphanCosts = currentKing.Orphans.Count * GameConfig.OrphanCareCost;
        long baseCosts = 1000;

        terminal.WriteLine(Loc.Get("castle.guard_salaries", guardCosts.ToString("N0"), currentKing.Guards.Count));
        terminal.WriteLine(Loc.Get("castle.orphanage_costs", orphanCosts.ToString("N0"), currentKing.Orphans.Count));
        terminal.WriteLine(Loc.Get("castle.castle_maintenance", baseCosts.ToString("N0")));
        WriteDivider(40);
        terminal.SetColor("red");
        terminal.WriteLine(Loc.Get("castle.total_expenses", (guardCosts + orphanCosts + baseCosts).ToString("N0")));

        terminal.WriteLine("");
        terminal.SetColor("darkgray");
        terminal.WriteLine(Loc.Get("ui.press_enter"));
        await terminal.ReadKeyAsync();
    }

    private async Task WithdrawFromTreasury()
    {
        terminal.WriteLine("");
        terminal.SetColor("cyan");
        terminal.Write(Loc.Get("castle.withdraw_prompt", currentKing.Treasury.ToString("N0")));
        terminal.SetColor("white");
        string input = await terminal.ReadLineAsync();

        if (long.TryParse(input, out long amount) && amount > 0)
        {
            if (amount > currentKing.Treasury)
            {
                terminal.SetColor("red");
                terminal.WriteLine(Loc.Get("castle.treasury_not_enough"));
            }
            else
            {
                currentKing.Treasury -= amount;
                currentPlayer.Gold += amount;
                DebugLogger.Instance.LogInfo("GOLD", $"TREASURY WITHDRAW: {currentPlayer.DisplayName} withdrew {amount:N0}g from treasury (gold now {currentPlayer.Gold:N0}, treasury now {currentKing.Treasury:N0})");
                terminal.SetColor("bright_green");
                terminal.WriteLine(Loc.Get("castle.withdrew_gold", amount.ToString("N0")));
                PersistRoyalCourtToWorldState();
            }
        }

        await Task.Delay(2000);
    }

    private async Task DepositToTreasury()
    {
        terminal.WriteLine("");
        terminal.SetColor("cyan");
        terminal.Write(Loc.Get("castle.deposit_prompt", currentPlayer.Gold.ToString("N0")));
        terminal.SetColor("white");
        string input = await terminal.ReadLineAsync();

        if (long.TryParse(input, out long amount) && amount > 0)
        {
            if (amount > currentPlayer.Gold)
            {
                terminal.SetColor("red");
                terminal.WriteLine(Loc.Get("castle.not_enough_gold"));
            }
            else
            {
                currentPlayer.Gold -= amount;
                currentKing.Treasury += amount;
                terminal.SetColor("bright_green");
                terminal.WriteLine(Loc.Get("castle.deposited_gold", $"{amount:N0}"));
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
            WriteBoxHeader(Loc.Get("castle.royal_orders"), "bright_yellow");
            terminal.WriteLine("");

            terminal.SetColor("white");
            terminal.WriteLine(Loc.Get("castle.orders_desc"));
            terminal.WriteLine("");

            if (!string.IsNullOrEmpty(currentKing.LastProclamation))
            {
                terminal.SetColor("cyan");
                terminal.WriteLine(Loc.Get("castle.last_proclamation"));
                terminal.SetColor("gray");
                terminal.WriteLine($"\"{currentKing.LastProclamation}\"");
                terminal.WriteLine("");
            }

            if (IsScreenReader)
            {
                WriteSRMenuOption("E", Loc.Get("castle.establishments"));
                WriteSRMenuOption("P", Loc.Get("castle.proclamation"));
                WriteSRMenuOption("B", Loc.Get("castle.bounty"));
                WriteSRMenuOption("R", Loc.Get("castle.return"));
            }
            else if (IsBBSSession)
            {
                terminal.SetColor("bright_yellow");
                terminal.Write(" [E]");
                terminal.SetColor("white");
                terminal.Write($"{Loc.Get("castle.establishments")}  ");
                terminal.SetColor("bright_yellow");
                terminal.Write("[P]");
                terminal.SetColor("white");
                terminal.Write($"{Loc.Get("castle.proclamation")}  ");
                terminal.SetColor("bright_yellow");
                terminal.Write("[B]");
                terminal.SetColor("white");
                terminal.WriteLine($"{Loc.Get("castle.bounty")}");
                terminal.SetColor("bright_yellow");
                terminal.Write(" [R]");
                terminal.SetColor("gray");
                terminal.WriteLine($"{Loc.Get("castle.return")}");
            }
            else
            {
                terminal.WriteLine("");
                terminal.Write("  [", "white");
                terminal.Write("E", "bright_yellow");
                terminal.Write($"] {Loc.Get("castle.establishments")}   ", "white");
                terminal.Write("[", "white");
                terminal.Write("P", "bright_yellow");
                terminal.Write($"] {Loc.Get("castle.proclamation")}   ", "white");
                terminal.Write("[", "white");
                terminal.Write("B", "bright_yellow");
                terminal.WriteLine($"] {Loc.Get("castle.bounty")}", "white");

                terminal.Write("  [", "white");
                terminal.Write("R", "bright_yellow");
                terminal.WriteLine($"] {Loc.Get("castle.return")}", "gray");
            }
            terminal.WriteLine("");

            terminal.SetColor("cyan");
            string input = await terminal.GetInput(Loc.Get("castle.orders_prompt"));

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
        terminal.WriteLine(Loc.Get("castle.establishment_status"));
        terminal.WriteLine("");

        int i = 1;
        var establishments = currentKing.EstablishmentStatus.ToList();
        foreach (var est in establishments)
        {
            string status = est.Value ? Loc.Get("castle.open") : Loc.Get("castle.closed");
            string statusColor = est.Value ? "bright_green" : "red";

            string displayName = Loc.Get($"castle.est_{est.Key.ToLowerInvariant()}");
            terminal.SetColor("white");
            terminal.Write($"{i}. {displayName,-20} ");
            terminal.SetColor(statusColor);
            terminal.WriteLine(status);
            i++;
        }

        terminal.WriteLine("");
        terminal.SetColor("cyan");
        terminal.Write(Loc.Get("castle.toggle_establishment"));
        terminal.SetColor("white");
        string input = await terminal.ReadLineAsync();

        if (int.TryParse(input, out int choice) && choice >= 1 && choice <= establishments.Count)
        {
            var key = establishments[choice - 1].Key;
            bool currentlyOpen = currentKing.EstablishmentStatus[key];

            currentKing.EstablishmentStatus[key] = !currentKing.EstablishmentStatus[key];

            // Check for rebellion from closing too many establishments
            if (currentlyOpen) // We just closed one
            {
                int closedCount = currentKing.EstablishmentStatus.Count(e => !e.Value);
                int totalCount = currentKing.EstablishmentStatus.Count;
                double closedPercent = (double)closedCount / totalCount;

                if (closedPercent >= 0.75) // 75% closed = rebellion
                {
                    terminal.SetColor("bright_red");
                    terminal.WriteLine("");
                    terminal.WriteLine("  The people have had enough! Too many shops closed!");
                    await Task.Delay(2000);
                    await TriggerRebellion();
                    return;
                }
                else if (closedCount >= 2)
                {
                    terminal.SetColor("yellow");
                    terminal.WriteLine("");
                    terminal.WriteLine("  The citizens grumble about the closures...");
                    terminal.WriteLine("  Close too many more and the people may revolt.");
                }
            }

            string newStatus = currentKing.EstablishmentStatus[key] ? Loc.Get("castle.opened") : Loc.Get("castle.closed_past");
            string estDisplayName = Loc.Get($"castle.est_{key.ToLowerInvariant()}");
            terminal.SetColor("bright_green");
            terminal.WriteLine(Loc.Get("castle.establishment_toggled", estDisplayName, newStatus));

            NewsSystem.Instance.Newsy(true, $"{currentKing.GetTitle()} {currentKing.Name} has {newStatus} the {estDisplayName}!");

            if (DoorMode.IsOnlineMode)
            {
                string color = currentKing.EstablishmentStatus[key] ? "\u001b[1;32m" : "\u001b[1;31m";
                UsurperRemake.Server.MudServer.Instance?.BroadcastToAll(
                    $"{color}  *** {currentKing.GetTitle()} {currentKing.Name} has {newStatus} the {estDisplayName}! ***\u001b[0m");
                PersistRoyalCourtToWorldState();
            }

            await Task.Delay(2000);
        }
    }

    private async Task IssueProclamation()
    {
        terminal.WriteLine("");
        terminal.SetColor("cyan");
        terminal.WriteLine(Loc.Get("castle.issue_proclamation"));
        terminal.Write(Loc.Get("castle.your_decree"));
        terminal.SetColor("white");
        string proclamation = await terminal.ReadLineAsync();

        if (!string.IsNullOrEmpty(proclamation))
        {
            // Sanitize: strip ANSI escape codes and limit length
            proclamation = System.Text.RegularExpressions.Regex.Replace(proclamation, @"\u001b\[[0-9;]*[a-zA-Z]", "");
            if (proclamation.Length > 200)
                proclamation = proclamation.Substring(0, 200);

            currentKing.LastProclamation = proclamation;
            currentKing.LastProclamationDate = DateTime.Now;

            terminal.SetColor("bright_yellow");
            terminal.WriteLine("");
            terminal.WriteLine(Loc.Get("castle.hear_ye"));
            terminal.SetColor("white");
            terminal.WriteLine(Loc.Get("castle.royal_decree", currentKing.GetTitle(), currentKing.Name));
            terminal.WriteLine($"\"{proclamation}\"");

            NewsSystem.Instance.Newsy(true, $"Royal Proclamation: \"{proclamation}\" - {currentKing.GetTitle()} {currentKing.Name}");

            // Broadcast server-wide
            if (DoorMode.IsOnlineMode)
            {
                UsurperRemake.Server.MudServer.Instance?.BroadcastToAll(
                    $"\u001b[1;33m  *** Royal Proclamation by {currentKing.GetTitle()} {currentKing.Name}: \"{proclamation}\" ***\u001b[0m");
                PersistRoyalCourtToWorldState();
            }

            await Task.Delay(3000);
        }
    }

    private async Task PlaceBounty()
    {
        terminal.WriteLine("");
        terminal.SetColor("cyan");
        terminal.Write(Loc.Get("castle.criminal_name"));
        terminal.SetColor("white");
        string name = await terminal.ReadLineAsync();

        if (string.IsNullOrEmpty(name)) return;

        // Can't bounty yourself
        if (name.Equals(currentKing.Name, StringComparison.OrdinalIgnoreCase) ||
            name.Equals(currentPlayer.DisplayName, StringComparison.OrdinalIgnoreCase))
        {
            terminal.SetColor("yellow");
            terminal.WriteLine("  You cannot place a bounty on yourself.");
            await Task.Delay(2000);
            return;
        }

        terminal.SetColor("cyan");
        terminal.Write(Loc.Get("castle.bounty_amount"));
        terminal.SetColor("white");
        string amountStr = await terminal.ReadLineAsync();

        if (long.TryParse(amountStr, out long amount) && amount > 0)
        {
            if (amount > currentKing.Treasury)
            {
                terminal.SetColor("red");
                terminal.WriteLine(Loc.Get("castle.insufficient_treasury"));
            }
            else
            {
                currentKing.Treasury -= amount;
                terminal.SetColor("bright_red");
                terminal.WriteLine(Loc.Get("castle.bounty_placed", $"{amount:N0}", name));

                NewsSystem.Instance.Newsy(true, $"BOUNTY: {amount:N0} gold on {name} by order of {currentKing.GetTitle()} {currentKing.Name}!");

                // Wire into QuestSystem so the bounty is trackable
                QuestSystem.PostBountyOnPlayer(name, "Royal decree", amount);

                // Broadcast and persist
                if (DoorMode.IsOnlineMode)
                {
                    UsurperRemake.Server.MudServer.Instance?.BroadcastToAll(
                        $"\u001b[1;31m  *** BOUNTY: {amount:N0} gold on {name} by order of {currentKing.GetTitle()} {currentKing.Name}! ***\u001b[0m");
                    PersistRoyalCourtToWorldState();
                }
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
            WriteBoxHeader(Loc.Get("castle.royal_orphanage"), "bright_cyan");
            terminal.WriteLine("");

            // Update ages for real orphans
            foreach (var o in currentKing.Orphans.Where(o => o.IsRealOrphan))
                o.Age = o.ComputedAge;

            terminal.SetColor("white");
            terminal.WriteLine(Loc.Get("castle.visit_orphans"));
            long dailyCost = currentKing.Orphans.Count * GameConfig.OrphanCareCost;
            terminal.WriteLine(Loc.Get("castle.orphan_summary", currentKing.Orphans.Count, GameConfig.MaxRoyalOrphans, dailyCost.ToString("N0"), GameConfig.OrphanCareCost));
            terminal.WriteLine("");

            if (currentKing.Orphans.Count == 0)
            {
                terminal.SetColor("gray");
                terminal.WriteLine(Loc.Get("castle.orphanage_empty"));
            }
            else
            {
                terminal.SetColor("cyan");
                terminal.WriteLine($"{Loc.Get("castle.header_num"),-4} {Loc.Get("castle.header_name"),-20} {Loc.Get("castle.header_age"),-6} {Loc.Get("castle.header_sex"),-8} {Loc.Get("castle.header_race"),-10} {Loc.Get("castle.header_type"),-10} {Loc.Get("castle.header_happy"),-6}");
                WriteDivider(66);

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
            terminal.WriteLine(Loc.Get("castle.commands"));
            long adoptCost = 500 + (currentKing.Orphans.Count * 100);

            // Show commission option if any real orphans are old enough
            bool hasCommissionable = currentKing.Orphans.Any(o => o.IsRealOrphan && o.Age >= GameConfig.OrphanCommissionAge);

            if (IsScreenReader)
            {
                WriteSRMenuOption("A", Loc.Get("castle.adopt_orphan", $"{adoptCost:N0}"));
                WriteSRMenuOption("V", Loc.Get("castle.view_orphan"));
                if (hasCommissionable)
                    WriteSRMenuOption("C", Loc.Get("castle.commission_orphan", GameConfig.OrphanCommissionAge, $"{GameConfig.OrphanCommissionCost:N0}"));
                WriteSRMenuOption("G", Loc.Get("castle.give_gifts"));
                WriteSRMenuOption("R", Loc.Get("castle.return_royal"));
            }
            else
            {
                terminal.SetColor("bright_yellow");
                terminal.Write("  [A] "); terminal.SetColor("white"); terminal.WriteLine(Loc.Get("castle.adopt_orphan", $"{adoptCost:N0}"));
                terminal.SetColor("bright_yellow");
                terminal.Write("  [V] "); terminal.SetColor("white"); terminal.WriteLine(Loc.Get("castle.view_orphan_details"));

                if (hasCommissionable)
                {
                    terminal.SetColor("bright_yellow");
                    terminal.Write("  [C] "); terminal.SetColor("white"); terminal.WriteLine(Loc.Get("castle.commission_orphan", GameConfig.OrphanCommissionAge, $"{GameConfig.OrphanCommissionCost:N0}"));
                }

                terminal.SetColor("bright_yellow");
                terminal.Write("  [G] "); terminal.SetColor("white"); terminal.WriteLine(Loc.Get("castle.give_gifts_desc"));
                terminal.SetColor("bright_yellow");
                terminal.Write("  [R] "); terminal.SetColor("white"); terminal.WriteLine(Loc.Get("castle.return_royal_menu"));
            }
            terminal.WriteLine("");

            terminal.SetColor("cyan");
            terminal.Write(Loc.Get("castle.action_prompt"));
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
            terminal.WriteLine(Loc.Get("castle.no_orphans_view"));
            await Task.Delay(1500);
            return;
        }

        terminal.SetColor("cyan");
        terminal.Write(Loc.Get("castle.orphan_number"));
        terminal.SetColor("white");
        string input = await terminal.ReadLineAsync();

        if (!int.TryParse(input, out int idx) || idx < 1 || idx > currentKing.Orphans.Count)
        {
            terminal.SetColor("red");
            terminal.WriteLine(Loc.Get("ui.invalid_selection"));
            await Task.Delay(1000);
            return;
        }

        var orphan = currentKing.Orphans[idx - 1];
        terminal.WriteLine("");
        terminal.SetColor("bright_cyan");
        terminal.WriteLine($"  === {orphan.Name} ===");
        terminal.SetColor("white");
        terminal.WriteLine(Loc.Get("castle.orphan_age_sex_happy", orphan.Age, orphan.Sex == CharacterSex.Male ? Loc.Get("castle.orphan_boy") : Loc.Get("castle.orphan_girl"), orphan.Happiness));

        if (orphan.IsRealOrphan)
        {
            terminal.SetColor("bright_magenta");
            terminal.WriteLine(Loc.Get("castle.orphan_type_orphaned"));
            terminal.SetColor("white");
            terminal.WriteLine(Loc.Get("castle.orphan_mother", orphan.MotherName ?? "Unknown"));
            terminal.WriteLine(Loc.Get("castle.orphan_father", orphan.FatherName ?? "Unknown"));
            terminal.WriteLine(Loc.Get("castle.orphan_race", orphan.Race));
            string soulDesc = orphan.Soul > 200 ? "Pure-hearted" :
                              orphan.Soul > 100 ? "Good-natured" :
                              orphan.Soul < -200 ? "Dark-souled" :
                              orphan.Soul < -100 ? "Troubled" : "Neutral";
            terminal.WriteLine(Loc.Get("castle.orphan_temperament", soulDesc, orphan.Soul.ToString("+0;-0;0")));

            if (orphan.Age >= GameConfig.OrphanCommissionAge)
            {
                terminal.SetColor("bright_yellow");
                terminal.WriteLine(Loc.Get("castle.orphan_ready", GameConfig.OrphanCommissionAge));
            }
        }
        else
        {
            terminal.SetColor("cyan");
            terminal.WriteLine(Loc.Get("castle.orphan_type_adopted"));
        }

        terminal.SetColor("gray");
        terminal.WriteLine($"  \"{orphan.BackgroundStory}\"");
        terminal.SetColor("white");
        terminal.WriteLine(Loc.Get("castle.orphan_arrived", orphan.ArrivalDate.ToString("yyyy-MM-dd")));

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
            terminal.WriteLine(Loc.Get("castle.orphan_no_eligible", GameConfig.OrphanCommissionAge));
            await Task.Delay(1500);
            return;
        }

        if (currentKing.Treasury < GameConfig.OrphanCommissionCost)
        {
            terminal.SetColor("red");
            terminal.WriteLine(Loc.Get("castle.orphan_insuff_treasury", GameConfig.OrphanCommissionCost.ToString("N0")));
            await Task.Delay(1500);
            return;
        }

        terminal.SetColor("cyan");
        terminal.WriteLine(Loc.Get("castle.eligible_orphans"));
        foreach (var (orphan, _) in eligible)
        {
            int displayIdx = currentKing.Orphans.IndexOf(orphan) + 1;
            terminal.SetColor("white");
            terminal.WriteLine(Loc.Get("castle.orphan_entry", displayIdx, orphan.Name, orphan.Age, orphan.Race, orphan.Sex == CharacterSex.Male ? "M" : "F"));
        }

        terminal.SetColor("cyan");
        terminal.Write(Loc.Get("castle.select_orphan"));
        terminal.SetColor("white");
        string input = await terminal.ReadLineAsync();

        if (!int.TryParse(input, out int idx) || idx < 1 || idx > currentKing.Orphans.Count)
        {
            terminal.SetColor("red");
            terminal.WriteLine(Loc.Get("ui.invalid_selection"));
            await Task.Delay(1000);
            return;
        }

        var selected = currentKing.Orphans[idx - 1];
        if (!selected.IsRealOrphan || selected.Age < GameConfig.OrphanCommissionAge)
        {
            terminal.SetColor("red");
            terminal.WriteLine(Loc.Get("castle.orphan_not_eligible"));
            await Task.Delay(1000);
            return;
        }

        terminal.WriteLine("");
        terminal.SetColor("cyan");
        terminal.WriteLine(Loc.Get("castle.orphan_commission_as", selected.Name));
        terminal.SetColor("bright_yellow");
        terminal.Write("  [G] "); terminal.SetColor("white"); terminal.WriteLine(Loc.Get("castle.commission_guard"));
        terminal.SetColor("bright_yellow");
        terminal.Write("  [M] "); terminal.SetColor("white"); terminal.WriteLine(Loc.Get("castle.commission_mercenary"));
        terminal.SetColor("bright_yellow");
        terminal.Write("  [N] "); terminal.SetColor("white"); terminal.WriteLine(Loc.Get("castle.commission_npc"));
        terminal.SetColor("bright_yellow");
        terminal.Write("  [X] "); terminal.SetColor("white"); terminal.WriteLine(Loc.Get("ui.cancel"));
        terminal.WriteLine("");

        terminal.SetColor("cyan");
        terminal.Write(Loc.Get("ui.choice"));
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
            terminal.WriteLine(Loc.Get("castle.no_guard_slots"));
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
        terminal.WriteLine(Loc.Get("castle.commissioned_guard", orphan.Name));
        terminal.SetColor("white");
        terminal.WriteLine(Loc.Get("castle.loyalty_raised_crown"));
        terminal.SetColor("yellow");
        terminal.WriteLine(Loc.Get("castle.orphan_treasury_minus", GameConfig.OrphanCommissionCost.ToString("N0")));

        currentPlayer.Chivalry += 10;
        PersistRoyalCourtToWorldState();
        await Task.Delay(2500);
    }

    private async Task CommissionAsMercenary(RoyalOrphan orphan)
    {
        if (currentPlayer.RoyalMercenaries.Count >= GameConfig.MaxRoyalMercenaries)
        {
            terminal.SetColor("red");
            terminal.WriteLine(Loc.Get("castle.no_merc_slots"));
            await Task.Delay(1500);
            return;
        }

        // Half the normal mercenary cost (orphan raised by the crown)
        long mercCost = GameConfig.OrphanCommissionCost + (GameConfig.MercenaryBaseCost + currentPlayer.Level * GameConfig.MercenaryCostPerLevel) / 2;
        if (currentKing.Treasury < mercCost)
        {
            terminal.SetColor("red");
            terminal.WriteLine(Loc.Get("castle.orphan_merc_insuff", mercCost.ToString("N0")));
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
        terminal.WriteLine(Loc.Get("castle.commissioned_merc", orphan.Name, role));
        terminal.SetColor("white");
        terminal.WriteLine($"{Loc.Get("ui.level")}: {merc.Level}  {Loc.Get("combat.bar_hp")}: {merc.MaxHP}  {Loc.Get("ui.stat_str")}: {merc.Strength}  {Loc.Get("ui.stat_def")}: {merc.Defence}");
        terminal.SetColor("yellow");
        terminal.WriteLine(Loc.Get("castle.orphan_treasury_minus", mercCost.ToString("N0")));

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
        terminal.WriteLine(Loc.Get("castle.released_citizen", orphan.Name));
        terminal.SetColor("white");
        terminal.WriteLine(Loc.Get("castle.orphan_released"));
        terminal.SetColor("yellow");
        terminal.WriteLine(Loc.Get("castle.orphan_treasury_minus", GameConfig.OrphanCommissionCost.ToString("N0")));

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
            terminal.WriteLine(Loc.Get("castle.orphan_full", GameConfig.MaxRoyalOrphans));
            await Task.Delay(1500);
            return;
        }

        long adoptCost = 500 + (currentKing.Orphans.Count * 100);
        if (currentKing.Treasury < adoptCost)
        {
            terminal.SetColor("red");
            terminal.WriteLine(Loc.Get("castle.orphan_adopt_insuff", adoptCost.ToString("N0")));
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
        string sexStr = sex == CharacterSex.Male ? Loc.Get("castle.boy") : Loc.Get("castle.girl");
        terminal.WriteLine(Loc.Get("castle.orphan_adopted", name, orphan.Age, sexStr));
        terminal.SetColor("gray");
        terminal.WriteLine($"  \"{orphan.BackgroundStory}\"");
        terminal.SetColor("bright_green");
        terminal.WriteLine(Loc.Get("castle.compassion_standing"));
        terminal.SetColor("yellow");
        terminal.WriteLine(Loc.Get("castle.orphan_treasury_minus", adoptCost.ToString("N0")));

        currentPlayer.Chivalry += 15;
        PersistRoyalCourtToWorldState();

        await Task.Delay(2500);
    }

    private async Task GiveGiftsToOrphans()
    {
        if (currentKing.Orphans.Count == 0)
        {
            terminal.SetColor("yellow");
            terminal.WriteLine(Loc.Get("castle.no_orphans_gift"));
            await Task.Delay(1500);
            return;
        }

        terminal.SetColor("white");
        terminal.WriteLine(Loc.Get("castle.orphan_treasury_balance", currentKing.Treasury.ToString("N0")));
        terminal.SetColor("cyan");
        terminal.Write(Loc.Get("castle.gift_amount"));
        terminal.SetColor("white");
        string input = await terminal.ReadLineAsync();

        if (long.TryParse(input, out long amount) && amount > 0)
        {
            if (amount > currentKing.Treasury)
            {
                terminal.SetColor("red");
                terminal.WriteLine(Loc.Get("castle.insufficient_treasury"));
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
                terminal.WriteLine(Loc.Get("castle.children_delighted"));
                terminal.WriteLine(Loc.Get("castle.orphan_happy_boost", happinessBoost));
                terminal.SetColor("yellow");
                terminal.WriteLine(Loc.Get("castle.orphan_treasury_minus", amount.ToString("N0")));

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
        WriteBoxHeader(Loc.Get("castle.royal_marriage"), "bright_magenta");
        terminal.WriteLine("");

        // Check if already married
        if (currentKing.Spouse != null)
        {
            terminal.SetColor("cyan");
            terminal.WriteLine(Loc.Get("castle.already_married", currentKing.Spouse.Name));
            terminal.WriteLine(Loc.Get("castle.married_on", currentKing.Spouse.MarriageDate.ToString("d")));
            terminal.WriteLine(Loc.Get("castle.spouse_happiness", currentKing.Spouse.Happiness));
            terminal.WriteLine(Loc.Get("castle.original_faction", currentKing.Spouse.OriginalFaction));
            terminal.WriteLine("");

            terminal.SetColor("yellow");
            terminal.WriteLine(Loc.Get("castle.options"));
            terminal.Write(" [");
            terminal.SetColor("bright_yellow");
            terminal.Write("G");
            terminal.SetColor("yellow");
            terminal.WriteLine(Loc.Get("castle.gift_spouse_option"));
            terminal.Write(" [");
            terminal.SetColor("bright_yellow");
            terminal.Write("D");
            terminal.SetColor("yellow");
            terminal.WriteLine(Loc.Get("castle.divorce_option"));
            terminal.Write(" [");
            terminal.SetColor("bright_yellow");
            terminal.Write("R");
            terminal.SetColor("yellow");
            terminal.WriteLine(Loc.Get("castle.return_option"));
            terminal.WriteLine("");

            terminal.SetColor("cyan");
            terminal.Write(Loc.Get("ui.your_choice"));
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
        terminal.WriteLine(Loc.Get("castle.marriage_desc_1"));
        terminal.WriteLine(Loc.Get("castle.marriage_desc_2"));
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
            terminal.WriteLine(Loc.Get("castle.no_candidates"));
            terminal.WriteLine(Loc.Get("castle.candidate_requirements"));
            await terminal.PressAnyKey();
            return;
        }

        terminal.SetColor("cyan");
        terminal.WriteLine(Loc.Get("castle.potential_candidates"));
        terminal.WriteLine("");

        terminal.SetColor("white");
        terminal.WriteLine($"{Loc.Get("castle.header_num"),-3} {Loc.Get("castle.header_name"),-20} {Loc.Get("castle.header_level"),-8} {Loc.Get("castle.header_dowry"),-12} {Loc.Get("castle.header_faction_benefit")}");
        WriteDivider(70);

        int i = 1;
        foreach (var npc in eligibleNPCs)
        {
            // Calculate dowry based on level and charisma
            long dowry = (npc.Level * 1000) + (npc.Charisma * 100);
            var faction = DetermineFactionForNPC(npc);

            terminal.SetColor("white");
            terminal.WriteLine($"{i,-3} {npc.Name,-20} {npc.Level,-8} {dowry:N0,-12} {Loc.Get("castle.candidate_entry", "", faction).Trim()}");
            i++;
        }

        terminal.WriteLine("");
        terminal.SetColor("cyan");
        terminal.Write(Loc.Get("castle.propose_candidate"));
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
        terminal.WriteLine(Loc.Get("castle.send_proposal", candidate.Name));
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
            terminal.WriteLine(Loc.Get("castle.accepts_proposal", candidate.Name));
            terminal.WriteLine("");
            terminal.SetColor("yellow");
            terminal.WriteLine(Loc.Get("castle.dowry_received", dowry.ToString("N0")));
            terminal.WriteLine(Loc.Get("castle.faction_loyalty_up", faction));
            terminal.WriteLine("");

            NewsSystem.Instance?.Newsy(true,
                $"ROYAL WEDDING! {currentKing.GetTitle()} {currentKing.Name} has married {candidate.Name}!");
            NewsSystem.Instance?.WriteMarriageNews(currentPlayer.Name, candidate.Name, "Castle");

            DebugLogger.Instance.LogMarriage(currentPlayer.Name, candidate.Name);
        }
        else
        {
            terminal.SetColor("red");
            terminal.WriteLine(Loc.Get("castle.declines_proposal", candidate.Name));
            terminal.WriteLine(Loc.Get("castle.try_more_renowned"));
        }
    }

    private async Task GiftToSpouse()
    {
        terminal.SetColor("cyan");
        terminal.Write(Loc.Get("castle.gift_amount"));
        terminal.SetColor("white");
        string input = await terminal.ReadLineAsync();

        if (long.TryParse(input, out long amount) && amount > 0)
        {
            if (amount > currentKing.Treasury)
            {
                terminal.SetColor("red");
                terminal.WriteLine(Loc.Get("castle.insufficient_funds"));
                return;
            }

            currentKing.Treasury -= amount;
            int happinessBoost = (int)Math.Min(30, amount / 500);
            currentKing.Spouse!.Happiness = Math.Min(100, currentKing.Spouse.Happiness + happinessBoost);

            terminal.SetColor("bright_green");
            terminal.WriteLine(Loc.Get("castle.spouse_pleased", happinessBoost));
        }
    }

    private async Task DivorceSpouse()
    {
        terminal.SetColor("bright_red");
        terminal.WriteLine(Loc.Get("castle.divorce_warning"));
        terminal.WriteLine(Loc.Get("castle.faction_turn_against", currentKing.Spouse!.OriginalFaction));
        terminal.WriteLine("");

        terminal.SetColor("yellow");
        terminal.Write(Loc.Get("ui.confirm_divorce"));
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

            // Clean up registries — sync both RelationshipSystem and RomanceTracker
            NPCMarriageRegistry.Instance.EndMarriage(currentPlayer.ID);
            if (spouseNPC != null)
            {
                RelationshipSystem.ProcessDivorce(currentPlayer, spouseNPC, out _);
                RomanceTracker.Instance?.Divorce(spouseNPC.ID, "Royal divorce", playerInitiated: true);
            }

            terminal.SetColor("red");
            terminal.WriteLine(Loc.Get("castle.divorced", spouseName));
            terminal.WriteLine(Loc.Get("castle.faction_furious", faction));

            NewsSystem.Instance?.Newsy(true,
                $"SCANDAL! {currentKing.GetTitle()} {currentKing.Name} has divorced {spouseName}!");

            currentKing.Spouse = null;
        }
        else
        {
            terminal.SetColor("gray");
            terminal.WriteLine(Loc.Get("castle.divorce_cancelled"));
        }
    }

    // ═══════════════════════════════════════════════════════════════════════════
    // COURT POLITICS
    // ═══════════════════════════════════════════════════════════════════════════

    private async Task ViewCourtPolitics()
    {
        terminal.ClearScreen();
        WriteBoxHeader(Loc.Get("castle.royal_court"), "bright_cyan");
        terminal.WriteLine("");

        // Show court members
        terminal.SetColor("yellow");
        terminal.WriteLine(Loc.Get("castle.court_members"));
        terminal.WriteLine("");

        if (currentKing.CourtMembers.Count == 0)
        {
            terminal.SetColor("gray");
            terminal.WriteLine(Loc.Get("castle.court_not_established"));
            terminal.WriteLine(Loc.Get("castle.court_developing"));
        }
        else
        {
            terminal.SetColor("white");
            terminal.WriteLine($"{Loc.Get("castle.header_role"),-18} {Loc.Get("castle.header_name"),-25} {Loc.Get("castle.header_faction"),-15} {Loc.Get("castle.header_loyalty"),-10} {Loc.Get("castle.header_influence")}");
            WriteDivider(78);

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
                terminal.WriteLine(Loc.Get("castle.courtiers_scheming"));
            }
        }

        terminal.WriteLine("");

        // Show active plots (if detected)
        terminal.SetColor("yellow");
        terminal.WriteLine(Loc.Get("castle.intel_reports"));
        terminal.WriteLine("");

        var discoveredPlots = currentKing.ActivePlots.Where(p => p.IsDiscovered).ToList();
        if (discoveredPlots.Count > 0)
        {
            foreach (var plot in discoveredPlots)
            {
                terminal.SetColor("red");
                terminal.WriteLine(Loc.Get("castle.foiled_plot", plot.PlotType, string.Join(", ", plot.Conspirators)));
            }
        }

        // Hint about undiscovered plots
        var secretPlots = currentKing.ActivePlots.Where(p => !p.IsDiscovered).ToList();
        if (secretPlots.Count > 0)
        {
            terminal.SetColor("gray");
            terminal.WriteLine(Loc.Get("castle.spies_report", secretPlots.Count));
            terminal.WriteLine(Loc.Get("castle.use_detect_threats"));
        }
        else if (discoveredPlots.Count == 0)
        {
            terminal.SetColor("green");
            terminal.WriteLine(Loc.Get("castle.no_plots"));
        }

        terminal.WriteLine("");

        // Show faction standing
        terminal.SetColor("yellow");
        terminal.WriteLine(Loc.Get("castle.faction_relations"));
        terminal.WriteLine("");

        var factionGroups = currentKing.CourtMembers
            .GroupBy(m => m.Faction)
            .Where(g => g.Key != CourtFaction.None);

        foreach (var group in factionGroups)
        {
            int avgLoyalty = (int)group.Average(m => m.LoyaltyToKing);
            string status = avgLoyalty >= 70 ? Loc.Get("castle.faction_loyal") :
                           avgLoyalty >= 40 ? Loc.Get("castle.faction_neutral") : Loc.Get("castle.faction_hostile");
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
                if (IsScreenReader)
                {
                    WriteSRMenuOption("D", Loc.Get("castle.dismiss_plotter"));
                    WriteSRMenuOption("A", Loc.Get("castle.arrest_plotter"));
                    WriteSRMenuOption("B", Loc.Get("castle.bribe_plotter"));
                    WriteSRMenuOption("P", Loc.Get("castle.promote_plotter"));
                    WriteSRMenuOption("Q", Loc.Get("ui.cancel"));
                }
                else
                {
                    terminal.SetColor("cyan");
                    terminal.WriteLine(Loc.Get("castle.court_menu_visual"));
                }
                terminal.SetColor("white");
                terminal.Write(Loc.Get("castle.court_action"));
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
        terminal.WriteLine(Loc.Get("castle.dismiss_which"));
        for (int i = 0; i < currentKing.CourtMembers.Count; i++)
        {
            var m = currentKing.CourtMembers[i];
            terminal.SetColor("white");
            terminal.WriteLine(Loc.Get("castle.court_member_entry", i + 1, m.Name, m.Role, m.Faction));
        }
        terminal.SetColor("cyan");
        terminal.Write(Loc.Get("castle.number_cancel"));
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
            terminal.WriteLine(Loc.Get("castle.dismissed_from_court", member.Name));
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
            terminal.WriteLine(Loc.Get("castle.no_discovered_plots"));
            terminal.WriteLine("");
            return;
        }

        terminal.SetColor("yellow");
        terminal.WriteLine("");
        terminal.WriteLine(Loc.Get("castle.arrest_which"));
        for (int i = 0; i < discoveredPlots.Count; i++)
        {
            var plot = discoveredPlots[i];
            terminal.SetColor("red");
            terminal.WriteLine(Loc.Get("castle.plot_entry", i + 1, plot.PlotType, string.Join(", ", plot.Conspirators), GameConfig.ArrestTrialCost.ToString("N0")));
        }
        terminal.SetColor("cyan");
        terminal.Write(Loc.Get("castle.number_cancel"));
        string input = await terminal.ReadLineAsync();
        if (int.TryParse(input, out int idx) && idx > 0 && idx <= discoveredPlots.Count)
        {
            if (currentKing.Treasury < GameConfig.ArrestTrialCost)
            {
                terminal.SetColor("red");
                terminal.WriteLine(Loc.Get("castle.insufficient_trial"));
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
            terminal.WriteLine(Loc.Get("castle.arrested", GameConfig.ArrestTrialCost.ToString("N0")));
            NewsSystem.Instance?.Newsy(true, $"{currentKing.GetTitle()} {currentKing.Name} has crushed a {plot.PlotType} conspiracy!");
        }
        terminal.WriteLine("");
    }

    private async Task CourtAction_Bribe()
    {
        terminal.SetColor("yellow");
        terminal.WriteLine("");
        terminal.WriteLine(Loc.Get("castle.bribe_which"));
        for (int i = 0; i < currentKing.CourtMembers.Count; i++)
        {
            var m = currentKing.CourtMembers[i];
            long cost = GameConfig.BribeBaseCost + (100 - m.LoyaltyToKing) * 50;
            string loyColor = m.LoyaltyToKing >= 70 ? "bright_green" : m.LoyaltyToKing >= 40 ? "yellow" : "red";
            terminal.SetColor("white");
            terminal.Write($"  {i + 1}. {m.Name,-20} ");
            terminal.SetColor(loyColor);
            terminal.Write(Loc.Get("castle.loyalty_label", m.LoyaltyToKing));
            terminal.SetColor("gray");
            terminal.WriteLine(Loc.Get("castle.cost_label", cost.ToString("N0")));
        }
        terminal.SetColor("cyan");
        terminal.Write(Loc.Get("castle.number_cancel"));
        string input = await terminal.ReadLineAsync();
        if (int.TryParse(input, out int idx) && idx > 0 && idx <= currentKing.CourtMembers.Count)
        {
            var member = currentKing.CourtMembers[idx - 1];
            long cost = GameConfig.BribeBaseCost + (100 - member.LoyaltyToKing) * 50;

            if (currentKing.Treasury < cost)
            {
                terminal.SetColor("red");
                terminal.WriteLine(Loc.Get("castle.insufficient_treasury"));
                terminal.WriteLine("");
                return;
            }

            currentKing.Treasury -= cost;
            var random = Random.Shared;
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
                terminal.WriteLine(Loc.Get("castle.bribe_abandoned", member.Name, loyaltyGain, cost.ToString("N0")));
            }
            else
            {
                terminal.SetColor("green");
                terminal.WriteLine(Loc.Get("castle.bribe_loyalty_up", member.Name, member.LoyaltyToKing, loyaltyGain, cost.ToString("N0")));
            }
        }
        terminal.WriteLine("");
    }

    private async Task CourtAction_Promote()
    {
        terminal.SetColor("yellow");
        terminal.WriteLine("");
        terminal.WriteLine(Loc.Get("castle.promote_which", GameConfig.PromoteCost.ToString("N0")));
        for (int i = 0; i < currentKing.CourtMembers.Count; i++)
        {
            var m = currentKing.CourtMembers[i];
            terminal.SetColor("white");
            terminal.WriteLine(Loc.Get("castle.promote_entry", i + 1, m.Name, m.Role, m.Influence));
        }
        terminal.SetColor("cyan");
        terminal.Write(Loc.Get("castle.number_cancel"));
        string input = await terminal.ReadLineAsync();
        if (int.TryParse(input, out int idx) && idx > 0 && idx <= currentKing.CourtMembers.Count)
        {
            if (currentKing.Treasury < GameConfig.PromoteCost)
            {
                terminal.SetColor("red");
                terminal.WriteLine(Loc.Get("castle.insufficient_treasury"));
                terminal.WriteLine("");
                return;
            }

            var member = currentKing.CourtMembers[idx - 1];
            currentKing.Treasury -= GameConfig.PromoteCost;
            member.LoyaltyToKing = Math.Min(100, member.LoyaltyToKing + GameConfig.PromoteLoyaltyGain);
            member.Influence = Math.Min(100, member.Influence + 5);

            terminal.SetColor("bright_green");
            terminal.WriteLine(Loc.Get("castle.promoted", member.Name, GameConfig.PromoteLoyaltyGain, GameConfig.PromoteCost.ToString("N0")));
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
        WriteBoxHeader(Loc.Get("castle.royal_succession"), "bright_green");
        terminal.WriteLine("");

        // Show current heirs
        terminal.SetColor("yellow");
        terminal.WriteLine(Loc.Get("castle.heirs_title"));
        terminal.WriteLine("");

        if (currentKing.Heirs.Count == 0)
        {
            terminal.SetColor("gray");
            terminal.WriteLine(Loc.Get("castle.no_heirs"));
            if (currentKing.Spouse == null)
            {
                terminal.WriteLine(Loc.Get("castle.consider_marriage"));
            }
            else
            {
                terminal.WriteLine(Loc.Get("castle.child_may_born"));
            }
        }
        else
        {
            terminal.SetColor("white");
            terminal.WriteLine($"{Loc.Get("castle.header_num"),-3} {Loc.Get("castle.header_name"),-20} {Loc.Get("castle.header_age"),-6} {Loc.Get("castle.header_sex"),-8} {Loc.Get("castle.header_claim"),-10} {Loc.Get("castle.header_status")}");
            WriteDivider(60);

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
            terminal.WriteLine(Loc.Get("castle.current_heir", currentKing.DesignatedHeir));
        }
        else
        {
            terminal.SetColor("yellow");
            terminal.WriteLine(Loc.Get("castle.no_heir_designated"));
        }

        terminal.WriteLine("");

        // Options
        terminal.SetColor("cyan");
        terminal.WriteLine(Loc.Get("castle.options"));
        if (IsScreenReader)
        {
            WriteSRMenuOption("D", Loc.Get("castle.designate_heir"));
            WriteSRMenuOption("N", Loc.Get("castle.name_heir"));
            WriteSRMenuOption("R", Loc.Get("castle.return"));
        }
        else
        {
            terminal.Write(" [");
            terminal.SetColor("bright_yellow");
            terminal.Write("D");
            terminal.SetColor("cyan");
            terminal.WriteLine(Loc.Get("castle.designate_heir_option"));
            terminal.Write(" [");
            terminal.SetColor("bright_yellow");
            terminal.Write("N");
            terminal.SetColor("cyan");
            terminal.WriteLine(Loc.Get("castle.name_heir_option"));
            terminal.Write(" [");
            terminal.SetColor("bright_yellow");
            terminal.Write("R");
            terminal.SetColor("cyan");
            terminal.WriteLine(Loc.Get("castle.return_option"));
        }
        terminal.WriteLine("");

        terminal.Write(Loc.Get("ui.your_choice"));
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
            terminal.WriteLine(Loc.Get("castle.no_heirs_designate"));
            await Task.Delay(2000);
            return;
        }

        terminal.SetColor("cyan");
        terminal.Write(Loc.Get("castle.designate_which"));
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
            terminal.WriteLine(Loc.Get("castle.heir_designated", heir.Name));
            terminal.WriteLine(Loc.Get("castle.claim_strengthened"));

            NewsSystem.Instance?.Newsy(false,
                $"{currentKing.GetTitle()} {currentKing.Name} has named {heir.Name} as the royal heir!");
        }

        await Task.Delay(2500);
    }

    private async Task CreateNewHeir()
    {
        terminal.SetColor("yellow");
        terminal.WriteLine(Loc.Get("castle.legitimize_desc"));
        terminal.WriteLine(Loc.Get("castle.legitimize_cost"));
        terminal.WriteLine("");

        if (currentKing.Treasury < 5000)
        {
            terminal.SetColor("red");
            terminal.WriteLine(Loc.Get("castle.insufficient_5000"));
            await Task.Delay(2000);
            return;
        }

        terminal.SetColor("cyan");
        terminal.Write(Loc.Get("castle.new_heir_name"));
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
        terminal.WriteLine(Loc.Get("castle.heir_added", name));

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
        var random = Random.Shared;

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

        // Stats scale with level — aligned with normal NPC scaling from NPCSpawnSystem
        // WeapPow/ArmPow represent built-in gear (NPCs get theirs from equipment)
        switch (role)
        {
            case "Tank":
                merc.Class = CharacterClass.Warrior;
                merc.HP = merc.MaxHP = 100 + level * 50;       // Matches NPC Warrior
                merc.Strength = 10 + level * 5;                 // NPC base STR
                merc.Defence = 10 + level * 3;                  // NPC base DEF
                merc.WeapPow = 5 + level * 2;
                merc.ArmPow = 5 + level * 3;
                merc.Agility = 10 + level * 2;
                merc.Dexterity = 10 + level * 2;
                merc.Constitution = 10 + level * 2;
                merc.Wisdom = 10 + level;
                merc.Intelligence = 10 + level;
                merc.Healing = 3;
                break;

            case "Healer":
                merc.Class = CharacterClass.Cleric;
                merc.HP = merc.MaxHP = 80 + level * 40;        // Matches NPC Cleric
                merc.Mana = merc.MaxMana = 40 + level * 10;
                merc.Strength = 10 + level * 2;
                merc.Defence = 10 + level * 3;
                merc.WeapPow = 3 + level;
                merc.ArmPow = 5 + level * 2;
                merc.Agility = 10 + level * 2;
                merc.Dexterity = 10 + level * 2;
                merc.Constitution = 10 + level * 2;
                merc.Wisdom = 10 + level * 4;                   // NPC Cleric WIS bonus
                merc.Intelligence = 10 + level * 2;
                merc.Healing = 5;
                break;

            case "DPS":
                merc.Class = CharacterClass.Ranger;
                merc.HP = merc.MaxHP = 80 + level * 40;        // NPC default HP
                merc.Strength = 10 + level * 5;                 // NPC base STR
                merc.Defence = 10 + level * 3;
                merc.WeapPow = 5 + level * 3;
                merc.ArmPow = 3 + level * 2;
                merc.Agility = 10 + level * 3;
                merc.Dexterity = 10 + level * 3;
                merc.Constitution = 10 + level * 2;
                merc.Wisdom = 10 + level;
                merc.Intelligence = 10 + level;
                merc.Healing = 3;
                break;

            case "Support":
                merc.Class = CharacterClass.Paladin;
                merc.HP = merc.MaxHP = 80 + level * 40;        // Matches NPC Paladin
                merc.Mana = merc.MaxMana = 30 + level * 8;
                merc.Strength = 10 + level * 3;
                merc.Defence = 10 + level * 3;
                merc.WeapPow = 5 + level * 2;
                merc.ArmPow = 5 + level * 3;
                merc.Agility = 10 + level * 2;
                merc.Dexterity = 10 + level * 2;
                merc.Constitution = 10 + level * 2;
                merc.Wisdom = 10 + level * 3;                   // NPC Paladin WIS bonus
                merc.Intelligence = 10 + level * 2;
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
            WriteBoxHeader(Loc.Get("castle.royal_bodyguards"), "bright_cyan");
            terminal.WriteLine("");

            terminal.SetColor("gray");
            terminal.WriteLine(Loc.Get("castle.hire_mercs_desc"));
            terminal.WriteLine(Loc.Get("castle.max_bodyguards", GameConfig.MaxRoyalMercenaries));
            terminal.WriteLine("");

            // Show current mercenaries
            if (currentPlayer.RoyalMercenaries.Count == 0)
            {
                terminal.SetColor("darkgray");
                terminal.WriteLine(Loc.Get("castle.no_bodyguards"));
            }
            else
            {
                terminal.SetColor("bright_yellow");
                terminal.WriteLine(Loc.Get("castle.current_bodyguards"));
                terminal.SetColor("gray");

                for (int i = 0; i < currentPlayer.RoyalMercenaries.Count; i++)
                {
                    var merc = currentPlayer.RoyalMercenaries[i];
                    string hpColor = merc.HP >= merc.MaxHP ? "bright_green" :
                                     merc.HP > merc.MaxHP / 2 ? "yellow" : "red";

                    terminal.SetColor("white");
                    terminal.Write($"  {i + 1}. {merc.Name}");
                    terminal.SetColor("gray");
                    terminal.Write(Loc.Get("castle.merc_level_entry", merc.Level, merc.Class, merc.Role));
                    terminal.SetColor(hpColor);
                    terminal.Write(Loc.Get("castle.merc_hp_entry", merc.HP, merc.MaxHP));
                    if (merc.MaxMana > 0)
                    {
                        terminal.SetColor("bright_cyan");
                        terminal.Write(Loc.Get("castle.merc_mp", merc.Mana, merc.MaxMana));
                    }
                    terminal.WriteLine("");
                }
            }

            terminal.WriteLine("");

            long hireCost = GameConfig.MercenaryBaseCost + (currentPlayer.Level * GameConfig.MercenaryCostPerLevel);

            // Menu
            if (IsScreenReader)
            {
                WriteSRMenuOption("H", Loc.Get("castle.hire_merc_btn", hireCost.ToString("N0")));
                WriteSRMenuOption("D", Loc.Get("castle.dismiss_guard"));
                WriteSRMenuOption("R", Loc.Get("castle.return"));
            }
            else
            {
                terminal.SetColor("darkgray");
                terminal.Write(" [");
                terminal.SetColor("bright_yellow");
                terminal.Write("H");
                terminal.SetColor("darkgray");
                terminal.Write("]");
                terminal.SetColor("white");
                terminal.Write(Loc.Get("castle.hire_merc_btn", hireCost.ToString("N0")));

                terminal.SetColor("darkgray");
                terminal.Write("[");
                terminal.SetColor("bright_yellow");
                terminal.Write("D");
                terminal.SetColor("darkgray");
                terminal.Write("]");
                terminal.SetColor("white");
                terminal.Write(Loc.Get("castle.dismiss_btn"));

                terminal.SetColor("darkgray");
                terminal.Write("[");
                terminal.SetColor("bright_yellow");
                terminal.Write("R");
                terminal.SetColor("darkgray");
                terminal.Write("]");
                terminal.SetColor("white");
                terminal.WriteLine(Loc.Get("castle.return_btn"));
            }
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
            terminal.WriteLine(Loc.Get("castle.max_bodyguards_already", GameConfig.MaxRoyalMercenaries));
            await terminal.PressAnyKey();
            return;
        }

        if (currentPlayer.Gold < cost)
        {
            terminal.SetColor("red");
            terminal.WriteLine(Loc.Get("castle.need_gold_merc", cost.ToString("N0"), currentPlayer.Gold.ToString("N0")));
            await terminal.PressAnyKey();
            return;
        }

        terminal.WriteLine("");
        terminal.SetColor("bright_yellow");
        terminal.WriteLine(Loc.Get("castle.choose_merc_role"));
        terminal.WriteLine("");

        terminal.SetColor("white");
        terminal.Write("  1. ");
        terminal.SetColor("bright_red");
        terminal.Write(Loc.Get("castle.role_tank"));
        terminal.SetColor("gray");
        terminal.WriteLine(Loc.Get("castle.role_tank_desc"));

        terminal.SetColor("white");
        terminal.Write("  2. ");
        terminal.SetColor("bright_green");
        terminal.Write(Loc.Get("castle.role_healer"));
        terminal.SetColor("gray");
        terminal.WriteLine(Loc.Get("castle.role_healer_desc"));

        terminal.SetColor("white");
        terminal.Write("  3. ");
        terminal.SetColor("bright_magenta");
        terminal.Write(Loc.Get("castle.role_dps"));
        terminal.SetColor("gray");
        terminal.WriteLine(Loc.Get("castle.role_dps_desc"));

        terminal.SetColor("white");
        terminal.Write("  4. ");
        terminal.SetColor("bright_cyan");
        terminal.Write(Loc.Get("castle.role_support"));
        terminal.SetColor("gray");
        terminal.WriteLine(Loc.Get("castle.role_support_desc"));

        terminal.WriteLine("");
        terminal.SetColor("white");
        terminal.Write(Loc.Get("castle.choose_1_4"));
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
        terminal.WriteLine(Loc.Get("castle.hired_bodyguard", merc.Name));
        terminal.SetColor("gray");
        terminal.WriteLine(Loc.Get("castle.merc_role", merc.Role, merc.Class, merc.Level));
        terminal.WriteLine(Loc.Get("castle.merc_hp_str_def", merc.MaxHP, merc.Strength, merc.Defence));
        terminal.WriteLine(Loc.Get("castle.merc_weap_arm", merc.WeapPow, merc.ArmPow));
        if (merc.MaxMana > 0)
            terminal.WriteLine(Loc.Get("castle.merc_mana_potions", merc.MaxMana, merc.Healing));
        terminal.SetColor("yellow");
        terminal.WriteLine(Loc.Get("castle.merc_cost", cost.ToString("N0"), currentPlayer.Gold.ToString("N0")));
        await terminal.PressAnyKey();
    }

    private async Task DismissMercenary()
    {
        if (currentPlayer.RoyalMercenaries.Count == 0)
        {
            terminal.SetColor("red");
            terminal.WriteLine(Loc.Get("castle.no_bodyguards_dismiss"));
            await terminal.PressAnyKey();
            return;
        }

        terminal.WriteLine("");
        terminal.SetColor("yellow");
        terminal.WriteLine(Loc.Get("castle.dismiss_which_bodyguard"));
        for (int i = 0; i < currentPlayer.RoyalMercenaries.Count; i++)
        {
            var merc = currentPlayer.RoyalMercenaries[i];
            terminal.SetColor("white");
            terminal.WriteLine(Loc.Get("castle.dismiss_merc_entry", i + 1, merc.Name, merc.Level, merc.Role));
        }
        terminal.SetColor("gray");
        terminal.Write(Loc.Get("castle.choose_r_cancel"));
        string dismissInput = await terminal.ReadLineAsync();

        if (int.TryParse(dismissInput, out int idx) && idx >= 1 && idx <= currentPlayer.RoyalMercenaries.Count)
        {
            var dismissed = currentPlayer.RoyalMercenaries[idx - 1];
            currentPlayer.RoyalMercenaries.RemoveAt(idx - 1);

            terminal.SetColor("yellow");
            terminal.WriteLine(Loc.Get("castle.dismissed_merc", dismissed.Name));
            await terminal.PressAnyKey();
        }
    }

    // ═══════════════════════════════════════════════════════════════════════════
    // ROYAL QUESTS
    // ═══════════════════════════════════════════════════════════════════════════

    private async Task RoyalQuests()
    {
        terminal.ClearScreen();
        WriteBoxHeader(Loc.Get("castle.royal_bounty"), "bright_red");
        terminal.WriteLine("");

        terminal.SetColor("white");
        terminal.WriteLine(Loc.Get("castle.bounty_board_desc"));
        terminal.WriteLine("");

        // Get bounties posted by the King
        var bounties = QuestSystem.GetKingBounties();

        if (bounties.Count == 0)
        {
            terminal.SetColor("gray");
            terminal.WriteLine(Loc.Get("castle.no_bounties"));
            terminal.WriteLine("");
            terminal.SetColor("yellow");
            terminal.WriteLine(Loc.Get("castle.realm_at_peace"));
        }
        else
        {
            terminal.SetColor("bright_red");
            terminal.WriteLine(Loc.Get("castle.wanted"));
            WriteDivider(60);

            foreach (var bounty in bounties)
            {
                terminal.SetColor("red");
                terminal.Write("  ▪ ");
                terminal.SetColor("white");
                terminal.WriteLine(bounty.Title ?? bounty.Comment ?? "Unknown Target");
                terminal.SetColor("yellow");
                terminal.WriteLine(Loc.Get("castle.reward_label", bounty.GetRewardDescription()));
                terminal.SetColor("gray");
                terminal.WriteLine(Loc.Get("castle.comment_label", bounty.Comment));
                terminal.WriteLine("");
            }
        }

        terminal.WriteLine("");
        terminal.SetColor("cyan");
        terminal.WriteLine(Loc.Get("castle.visit_quest_hall"));
        terminal.WriteLine("");
        terminal.SetColor("darkgray");
        terminal.WriteLine(Loc.Get("ui.press_enter"));
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

        if (currentPlayer.ThroneChallengedToday)
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
        if (currentKing == null || !currentKing.IsActive)
        {
            terminal.SetColor("gray");
            terminal.WriteLine("  The throne is vacant. No one to challenge.");
            await Task.Delay(2000);
            return false;
        }

        // Mark daily attempt — win or lose, you only get one shot per day
        currentPlayer.ThroneChallengedToday = true;

        terminal.ClearScreen();
        WriteBoxHeader(Loc.Get("castle.throne_challenge"), "bright_red");
        terminal.WriteLine("");

        // RULE: King cannot be on a team - must leave team first
        if (!string.IsNullOrEmpty(currentPlayer.Team))
        {
            terminal.SetColor("yellow");
            terminal.WriteLine(Loc.Get("castle.must_leave_team_challenge"));
            terminal.WriteLine(Loc.Get("castle.already_member", currentPlayer.Team));
            terminal.WriteLine("");
            terminal.SetColor("cyan");
            terminal.Write(Loc.Get("castle.leave_team_crown"));
            terminal.SetColor("white");
            string leaveConfirm = await terminal.ReadLineAsync();

            if (leaveConfirm?.ToUpper() != "Y")
            {
                terminal.SetColor("gray");
                terminal.WriteLine(Loc.Get("castle.remain_loyal_team"));
                await Task.Delay(2000);
                return false;
            }

            // Force leave team and clear dungeon party
            string oldTeam = currentPlayer.Team;
            CityControlSystem.Instance.ForceLeaveTeam(currentPlayer);
            GameEngine.Instance?.ClearDungeonParty(); // Clear NPC teammates from dungeon
            terminal.SetColor("yellow");
            terminal.WriteLine(Loc.Get("castle.left_team", oldTeam));
            terminal.WriteLine("");
        }

        terminal.SetColor("white");
        terminal.WriteLine(Loc.Get("castle.chosen_challenge"));
        terminal.WriteLine(Loc.Get("castle.must_defeat", currentKing.GetTitle(), currentKing.Name));
        terminal.WriteLine("");

        // Show monster guard warning
        if (currentKing.MonsterGuards.Count > 0)
        {
            terminal.SetColor("bright_red");
            terminal.WriteLine(Loc.Get("castle.warn_monsters", currentKing.MonsterGuards.Count));
        }

        // Show NPC guard warning
        if (currentKing.Guards.Count > 0)
        {
            terminal.SetColor("yellow");
            terminal.WriteLine(Loc.Get("castle.warn_guards", currentKing.Guards.Count));
        }

        if (currentKing.TotalGuardCount > 0)
        {
            terminal.WriteLine("");
        }

        terminal.SetColor("cyan");
        terminal.Write(Loc.Get("castle.proceed_yn"));
        terminal.SetColor("white");
        string confirm = await terminal.ReadLineAsync();

        if (confirm?.ToUpper() != "Y")
        {
            terminal.SetColor("gray");
            terminal.WriteLine(Loc.Get("castle.reconsider"));
            await Task.Delay(2000);
            return false;
        }

        // PHASE 1: Fight through monster guards first
        foreach (var monster in currentKing.MonsterGuards.ToList())
        {
            terminal.ClearScreen();
            terminal.SetColor("bright_red");
            terminal.WriteLine(Loc.Get("castle.fight_monster_guard", monster.Name, monster.Level));
            terminal.WriteLine("");

            // Create a Character wrapper from MonsterGuard data for PvP combat
            var guardChar = new Character
            {
                Name2 = monster.Name,
                Level = monster.Level,
                HP = monster.HP,
                MaxHP = monster.HP,
                Strength = monster.Strength,
                Defence = monster.Defence,
                WeapPow = monster.WeapPow,
                ArmPow = monster.ArmPow,
                AI = CharacterAI.Computer,
                Class = CharacterClass.Warrior
            };

            var combatEngine = new CombatEngine(terminal);
            var result = await combatEngine.PlayerVsPlayer(currentPlayer, guardChar);

            if (result.Outcome != CombatOutcome.Victory)
            {
                terminal.SetColor("red");
                terminal.WriteLine(Loc.Get("castle.slain_by_monster", monster.Name));
                currentPlayer.HP = Math.Max(1, currentPlayer.HP);
                terminal.WriteLine(Loc.Get("castle.challenge_failed"));
                await Task.Delay(2500);
                return false;
            }
            else
            {
                terminal.SetColor("bright_green");
                terminal.WriteLine(Loc.Get("castle.defeated_monster", monster.Name));
                currentKing.MonsterGuards.Remove(monster);
                NewsSystem.Instance?.Newsy(true, $"{currentPlayer.DisplayName} slew the monster guard {monster.Name}!");

                // Show HP between fights
                terminal.SetColor("cyan");
                terminal.WriteLine("");
                terminal.WriteLine($"You catch your breath... HP: {currentPlayer.HP}/{currentPlayer.MaxHP}");
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
                terminal.WriteLine(Loc.Get("castle.slip_past"));
                terminal.WriteLine(Loc.Get("castle.betrayal_noted"));
                currentKing.Guards.Remove(guard);
                NewsSystem.Instance?.Newsy(true, $"Guard {guard.Name} has betrayed the crown to challenge the throne!");
                await Task.Delay(1500);
                continue;
            }

            terminal.ClearScreen();
            terminal.SetColor("bright_red");
            terminal.WriteLine(Loc.Get("castle.fight_royal_guard", guard.Name));
            terminal.WriteLine("");

            // Look up actual NPC stats if available, otherwise create fallback
            var actualNpc = NPCSpawnSystem.Instance?.GetNPCByName(guard.Name);
            int estKingLevel = currentKing != null ? Math.Max(20, (int)(currentKing.TotalReign / 5) + 20) : 20;

            // Apply loyalty modifier to guard effectiveness
            float loyaltyMod = guard.Loyalty / 100f;

            // Low loyalty guards might flee before combat
            if (guard.Loyalty < 30 && random.Next(100) < 30)
            {
                terminal.SetColor("yellow");
                terminal.WriteLine(Loc.Get("castle.guard_flees", guard.Name));
                currentKing.Guards.Remove(guard);
                NewsSystem.Instance?.Newsy(false, $"Cowardly guard {guard.Name} fled from {currentPlayer.DisplayName}!");
                await Task.Delay(1500);
                continue;
            }

            // Prepare the guard character for PvP combat
            Character guardCharacter;
            if (actualNpc != null)
            {
                // NPC is already a Character — apply loyalty modifier to Strength
                guardCharacter = actualNpc;
                long originalStr = guardCharacter.Strength;
                guardCharacter.Strength = (long)(guardCharacter.Strength * loyaltyMod);

                terminal.SetColor("gray");
                terminal.WriteLine(Loc.Get("castle.guard_stats", guardCharacter.Level, guardCharacter.Strength, guardCharacter.Defence, guard.Loyalty));
                terminal.WriteLine("");

                var combatEngine = new CombatEngine(terminal);
                var result = await combatEngine.PlayerVsPlayer(currentPlayer, guardCharacter);

                // Restore original Strength after combat
                guardCharacter.Strength = originalStr;

                if (result.Outcome != CombatOutcome.Victory)
                {
                    terminal.SetColor("red");
                    terminal.WriteLine(Loc.Get("castle.defeated_by_guard", guard.Name));
                    currentPlayer.HP = Math.Max(1, currentPlayer.HP);
                    terminal.WriteLine(Loc.Get("castle.challenge_failed"));
                    await Task.Delay(2500);
                    return false;
                }
            }
            else
            {
                // Create fallback Character from estimated stats (buffed — royal guards are elite)
                int guardLevel = Math.Max(15, estKingLevel - 3);
                guardCharacter = new Character
                {
                    Name2 = guard.Name,
                    Level = guardLevel,
                    Strength = (long)((50 + estKingLevel * 8) * loyaltyMod),
                    Defence = 15 + estKingLevel * 5,
                    HP = 200 + estKingLevel * 50,
                    MaxHP = 200 + estKingLevel * 50,
                    WeapPow = 25 + estKingLevel * 3,
                    ArmPow = 12 + estKingLevel * 2,
                    AI = CharacterAI.Computer,
                    Class = CharacterClass.Warrior
                };

                var combatEngine = new CombatEngine(terminal);
                var result = await combatEngine.PlayerVsPlayer(currentPlayer, guardCharacter);

                if (result.Outcome != CombatOutcome.Victory)
                {
                    terminal.SetColor("red");
                    terminal.WriteLine(Loc.Get("castle.defeated_by_guard", guard.Name));
                    currentPlayer.HP = Math.Max(1, currentPlayer.HP);
                    terminal.WriteLine(Loc.Get("castle.challenge_failed"));
                    await Task.Delay(2500);
                    return false;
                }
            }

            terminal.SetColor("bright_green");
            terminal.WriteLine(Loc.Get("castle.defeated_guard", guard.Name));
            currentKing.Guards.Remove(guard);
            NewsSystem.Instance?.Newsy(true, $"{currentPlayer.DisplayName} defeated guard {guard.Name}!");

            // Show HP between fights
            terminal.SetColor("cyan");
            terminal.WriteLine("");
            terminal.WriteLine($"You catch your breath... HP: {currentPlayer.HP}/{currentPlayer.MaxHP}");

            await Task.Delay(1500);
        }

        // Fight the king — use real stats with defender bonus
        terminal.ClearScreen();
        terminal.SetColor("bright_red");
        terminal.WriteLine(Loc.Get("castle.final_battle", currentKing.GetTitle(), currentKing.Name));
        terminal.WriteLine("");

        // Look up real king stats: NPC king, player king (offline), or scaled fallback
        Character kingCharacter;
        var kingNpc = NPCSpawnSystem.Instance?.GetNPCByName(currentKing.Name);

        if (kingNpc != null)
        {
            // NPC King — use actual NPC with defender bonus applied
            kingCharacter = kingNpc;
            long origDef = kingCharacter.Defence;
            long origMaxHP = kingCharacter.MaxHP;
            long origHP = kingCharacter.HP;
            kingCharacter.Defence = (long)(kingCharacter.Defence * GameConfig.KingDefenderDefBonus);
            kingCharacter.MaxHP = (long)(kingCharacter.MaxHP * GameConfig.KingDefenderHPBonus);
            kingCharacter.HP = kingCharacter.MaxHP;

            terminal.SetColor("gray");
            terminal.WriteLine(Loc.Get("castle.king_stats", kingCharacter.Level, kingCharacter.Strength, kingCharacter.Defence, kingCharacter.HP));
            terminal.WriteLine("");

            var combatEngine = new CombatEngine(terminal);
            var kingResult = await combatEngine.PlayerVsPlayer(currentPlayer, kingCharacter);

            // Restore original NPC stats after combat
            kingCharacter.Defence = origDef;
            kingCharacter.MaxHP = origMaxHP;
            kingCharacter.HP = Math.Max(1, origHP);

            if (kingResult.Outcome == CombatOutcome.Victory)
            {
                // Continue to victory handling below
            }
            else
            {
                goto KingFightLost;
            }
        }
        else if (currentKing.AI == CharacterAI.Human && DoorMode.IsOnlineMode)
        {
            // Player King (offline) — try loading from database
            PlayerData kingData = null;
            try
            {
                var backend = SaveSystem.Instance?.Backend as SqlSaveBackend;
                if (backend != null)
                {
                    var kingSave = await backend.ReadGameData(currentKing.Name.ToLowerInvariant());
                    if (kingSave?.Player != null)
                        kingData = kingSave.Player;
                }
            }
            catch { /* Fall through */ }

            if (kingData != null)
            {
                kingCharacter = PlayerCharacterLoader.CreateFromSaveData(kingData, currentKing.Name);
            }
            else
            {
                // Fallback values if DB load failed
                int pLevel = 20;
                kingCharacter = new Character
                {
                    Name2 = currentKing.Name,
                    Level = pLevel,
                    Strength = 100,
                    Defence = 50,
                    WeapPow = 50,
                    ArmPow = 30,
                    HP = 500,
                    MaxHP = 500,
                    AI = CharacterAI.Computer,
                    Class = CharacterClass.Warrior
                };
            }

            // Apply defender bonus
            kingCharacter.Defence = (long)(kingCharacter.Defence * GameConfig.KingDefenderDefBonus);
            kingCharacter.MaxHP = (long)(kingCharacter.MaxHP * GameConfig.KingDefenderHPBonus);
            kingCharacter.HP = kingCharacter.MaxHP;

            terminal.SetColor("gray");
            terminal.WriteLine(Loc.Get("castle.king_stats", kingCharacter.Level, kingCharacter.Strength, kingCharacter.Defence, kingCharacter.HP));
            terminal.WriteLine("");

            var combatEngine = new CombatEngine(terminal);
            var kingResult = await combatEngine.PlayerVsPlayer(currentPlayer, kingCharacter);

            if (kingResult.Outcome != CombatOutcome.Victory)
                goto KingFightLost;
        }
        else
        {
            // Fallback — scale with estimated king level (buffed to be a serious challenge)
            int kingLevel = Math.Max(30, currentKing.TotalReign > 0 ? 30 + (int)(currentKing.TotalReign / 3) : 30);
            kingCharacter = new Character
            {
                Name2 = currentKing.Name,
                Level = kingLevel,
                Strength = 80 + kingLevel * 12,
                Defence = (long)((20 + kingLevel * 6) * GameConfig.KingDefenderDefBonus),
                WeapPow = 30 + kingLevel * 4,
                ArmPow = 15 + kingLevel * 3,
                HP = (long)((400 + kingLevel * 60) * GameConfig.KingDefenderHPBonus),
                MaxHP = (long)((400 + kingLevel * 60) * GameConfig.KingDefenderHPBonus),
                AI = CharacterAI.Computer,
                Class = CharacterClass.Warrior
            };

            terminal.SetColor("gray");
            terminal.WriteLine(Loc.Get("castle.king_stats", kingCharacter.Level, kingCharacter.Strength, kingCharacter.Defence, kingCharacter.HP));
            terminal.WriteLine("");

            var combatEngine = new CombatEngine(terminal);
            var kingResult = await combatEngine.PlayerVsPlayer(currentPlayer, kingCharacter);

            if (kingResult.Outcome != CombatOutcome.Victory)
                goto KingFightLost;
        }

        currentPlayer.HP = Math.Max(1, currentPlayer.HP);

        // Player wins!
        terminal.WriteLine("");
        terminal.SetColor("bright_yellow");
        if (!IsScreenReader)
        {
            terminal.WriteLine("════════════════════════════════════════════════");
            terminal.WriteLine(Loc.Get("castle.victory"));
            terminal.WriteLine("════════════════════════════════════════════════");
        }
        else
        {
            terminal.WriteLine(Loc.Get("castle.victory"));
        }
        terminal.WriteLine(Loc.Get("castle.defeated_king", currentKing.GetTitle(), currentKing.Name));
        terminal.WriteLine(Loc.Get("castle.throne_is_yours"));

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
        var inheritedPrisoners = currentKing?.Prisoners?.ToDictionary(p => p.Key, p => p.Value);
        string oldKingName = currentKing.Name;
        bool oldKingWasHuman = currentKing.AI == CharacterAI.Human;
        ClearRoyalMarriage(currentKing); // Clear old king's royal spouse before replacing
        currentPlayer.King = true;
        currentPlayer.NobleTitle = currentPlayer.Sex == CharacterSex.Female ? "Queen" : "King";
        currentKing = King.CreateNewKing(currentPlayer.DisplayName, CharacterAI.Human, currentPlayer.Sex, inheritedOrphans);
        currentKing.Treasury = inheritedTreasury;
        if (inheritedPrisoners != null && inheritedPrisoners.Count > 0)
        {
            foreach (var p in inheritedPrisoners)
                currentKing.Prisoners[p.Key] = p.Value;
        }
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

        KingFightLost:
        // Player lost the king fight
        currentPlayer.HP = Math.Max(1, currentPlayer.HP);
        terminal.WriteLine("");
        terminal.SetColor("red");
        terminal.WriteLine(Loc.Get("castle.you_defeated"));
        terminal.WriteLine(Loc.Get("castle.guards_drag_out"));

        await Task.Delay(3000);
        await NavigateToLocation(GameLocation.MainStreet);
        return true; // Exit castle
    }

    private async Task<bool> ClaimEmptyThrone()
    {
        terminal.ClearScreen();
        WriteBoxHeader(Loc.Get("castle.claiming_throne"), "bright_yellow");
        terminal.WriteLine("");

        // Check level requirement
        if (currentPlayer.Level < GameConfig.MinLevelKing)
        {
            terminal.SetColor("red");
            terminal.WriteLine(Loc.Get("castle.min_level_throne", GameConfig.MinLevelKing));
            terminal.WriteLine(Loc.Get("castle.your_current_level", currentPlayer.Level));
            await Task.Delay(2500);
            return false;
        }

        terminal.SetColor("white");
        terminal.WriteLine(Loc.Get("castle.disarray_1"));
        terminal.WriteLine(Loc.Get("castle.disarray_2"));
        terminal.WriteLine(Loc.Get("castle.disarray_3"));
        terminal.WriteLine("");

        // RULE: King cannot be on a team - must leave team first
        if (!string.IsNullOrEmpty(currentPlayer.Team))
        {
            terminal.SetColor("yellow");
            terminal.WriteLine(Loc.Get("castle.must_abandon_team"));
            terminal.WriteLine(Loc.Get("castle.currently_member_of", currentPlayer.Team));
            terminal.WriteLine("");
            terminal.SetColor("cyan");
            terminal.Write(Loc.Get("castle.leave_team_claim_yn"));
            terminal.SetColor("white");
            string leaveConfirm = await terminal.ReadLineAsync();

            if (leaveConfirm?.ToUpper() != "Y")
            {
                terminal.SetColor("gray");
                terminal.WriteLine(Loc.Get("castle.remain_loyal_team"));
                await Task.Delay(2000);
                return false;
            }

            // Force leave team and clear dungeon party
            string oldTeam = currentPlayer.Team;
            CityControlSystem.Instance.ForceLeaveTeam(currentPlayer);
            GameEngine.Instance?.ClearDungeonParty(); // Clear NPC teammates from dungeon
            terminal.SetColor("yellow");
            terminal.WriteLine(Loc.Get("castle.left_team_for_crown", oldTeam));
            terminal.WriteLine("");
        }

        var title = currentPlayer.Sex == CharacterSex.Male ? "KING" : "QUEEN";
        terminal.SetColor("cyan");
        terminal.Write(Loc.Get("castle.proclaim_self_yn", title));
        terminal.SetColor("white");
        string confirm = await terminal.ReadLineAsync();

        if (confirm?.ToUpper() != "Y")
        {
            terminal.SetColor("gray");
            terminal.WriteLine(Loc.Get("castle.decide_not_claim"));
            await Task.Delay(2000);
            return false;
        }

        // Crown the new monarch — inherit orphans and prisoners from previous reign
        var inheritedOrphans = currentKing?.Orphans?.ToList();
        var inheritedPrisoners = currentKing?.Prisoners?.ToDictionary(p => p.Key, p => p.Value);
        ClearRoyalMarriage(currentKing); // Clear old king's royal spouse if any
        currentPlayer.King = true;
        currentKing = King.CreateNewKing(currentPlayer.DisplayName, CharacterAI.Human, currentPlayer.Sex, inheritedOrphans);
        if (inheritedPrisoners != null && inheritedPrisoners.Count > 0)
        {
            foreach (var p in inheritedPrisoners)
                currentKing.Prisoners[p.Key] = p.Value;
        }
        playerIsKing = true;

        // Track archetype - Major Ruler moment
        UsurperRemake.Systems.ArchetypeTracker.Instance.RecordBecameKing();

        terminal.WriteLine("");
        terminal.SetColor("bright_yellow");
        if (!IsScreenReader)
        {
            terminal.WriteLine("═══════════════════════════════════════════════════════════════");
            terminal.WriteLine(Loc.Get("castle.congratulations_castle"));
            terminal.WriteLine(Loc.Get("castle.interregnum_over", title));
            terminal.WriteLine("═══════════════════════════════════════════════════════════════");
        }
        else
        {
            terminal.WriteLine(Loc.Get("castle.congratulations_castle"));
            terminal.WriteLine(Loc.Get("castle.interregnum_over", title));
        }
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
        WriteBoxHeader(Loc.Get("castle.abdication"), "bright_red");
        terminal.WriteLine("");

        terminal.SetColor("white");
        terminal.WriteLine(Loc.Get("ui.confirm_abdicate"));
        terminal.WriteLine(Loc.Get("castle.cannot_be_undone"));
        terminal.WriteLine("");
        terminal.SetColor("yellow");
        terminal.WriteLine(Loc.Get("castle.lose_privileges_1"));
        terminal.WriteLine(Loc.Get("castle.lose_privileges_2"));
        terminal.WriteLine("");

        terminal.SetColor("cyan");
        terminal.Write(Loc.Get("castle.abdicate_confirm_prompt"));
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
            terminal.WriteLine(Loc.Get("castle.pack_belongings_1"));
            terminal.WriteLine(Loc.Get("castle.pack_belongings_2"));
            terminal.WriteLine(Loc.Get("castle.pack_belongings_3"));
            terminal.WriteLine("");

            currentPlayer.King = false;
            if (currentPlayer.NobleTitle == "King" || currentPlayer.NobleTitle == "Queen")
                currentPlayer.NobleTitle = null;
            currentPlayer.RoyalMercenaries?.Clear(); // Dismiss bodyguards on abdication
            currentPlayer.RecalculateStats(); // Remove Royal Authority HP bonus
            ClearRoyalMarriage(currentKing); // Clear royal spouse before abdication
            currentKing.IsActive = false;
            currentKing = null;
            playerIsKing = false;

            terminal.SetColor("bright_yellow");
            terminal.WriteLine(Loc.Get("castle.has_abdicated", currentPlayer.Sex == CharacterSex.Male ? Loc.Get("castle.king") : Loc.Get("castle.queen")));
            terminal.SetColor("red");
            terminal.WriteLine(Loc.Get("castle.land_disarray"));

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
            terminal.WriteLine(Loc.Get("castle.kingdom_relief"));
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
            WriteBoxHeader(Loc.Get("castle.seek_audience"), "bright_cyan");
            terminal.WriteLine("");
            terminal.SetColor("gray");
            terminal.WriteLine(Loc.Get("castle.no_monarch_audience"));
            terminal.WriteLine(Loc.Get("castle.throne_room_empty"));
            terminal.WriteLine("");
            terminal.SetColor("darkgray");
            terminal.WriteLine(Loc.Get("ui.press_enter"));
            await terminal.ReadKeyAsync();
            return;
        }

        // Check reputation for access
        long reputation = (currentPlayer.Chivalry + currentPlayer.Fame) / 2;

        if (reputation < 25)
        {
            terminal.ClearScreen();
            WriteBoxHeader(Loc.Get("castle.seek_audience"), "bright_cyan");
            terminal.WriteLine("");
            terminal.SetColor("red");
            terminal.WriteLine(Loc.Get("castle.guards_block_path"));
            terminal.WriteLine("");
            terminal.SetColor("yellow");
            terminal.WriteLine(Loc.Get("castle.begone_player", currentPlayer.DisplayName, currentKing.GetTitle()));
            terminal.WriteLine(Loc.Get("castle.prove_worth"));
            terminal.WriteLine("");
            terminal.SetColor("gray");
            terminal.WriteLine(Loc.Get("castle.increase_chivalry_fame"));
            terminal.WriteLine("");
            terminal.SetColor("darkgray");
            terminal.WriteLine(Loc.Get("ui.press_enter"));
            await terminal.ReadKeyAsync();
            return;
        }

        // Audience granted - show menu
        while (true)
        {
            terminal.ClearScreen();
            WriteBoxHeader(Loc.Get("castle.audience_crown"), "bright_cyan");
            terminal.WriteLine("");

            // Display monarch and player status
            terminal.SetColor("bright_yellow");
            terminal.WriteLine(Loc.Get("castle.king_sits_throne", currentKing.GetTitle(), currentKing.Name));
            terminal.SetColor("gray");
            terminal.WriteLine(Loc.Get("castle.reign_treasury", currentKing.TotalReign, currentKing.Treasury));
            terminal.WriteLine("");

            terminal.SetColor("white");
            terminal.WriteLine(Loc.Get("castle.your_standing", GetReputationTitle(reputation), reputation));
            terminal.WriteLine(Loc.Get("castle.your_gold_chiv_dark", currentPlayer.Gold, currentPlayer.Chivalry, currentPlayer.Darkness));
            terminal.WriteLine("");

            // Show greeting based on reputation
            if (reputation >= 150)
            {
                terminal.SetColor("bright_green");
                terminal.WriteLine(Loc.Get("castle.greeting_high_rep", currentPlayer.DisplayName));
            }
            else if (reputation >= 100)
            {
                terminal.SetColor("cyan");
                terminal.WriteLine(Loc.Get("castle.greeting_mid_rep", currentPlayer.DisplayName));
            }
            else
            {
                terminal.SetColor("white");
                terminal.WriteLine(Loc.Get("castle.greeting_low_rep", currentPlayer.DisplayName));
            }
            terminal.WriteLine("");

            // Menu options
            terminal.SetColor("bright_yellow");
            terminal.WriteLine(Loc.Get("castle.petition_crown_for"));
            terminal.WriteLine("");

            if (IsScreenReader)
            {
                WriteSRMenuOption("Q", Loc.Get("castle.request_quest"));
                WriteSRMenuOption("K", Loc.Get("castle.request_knighthood"));
                WriteSRMenuOption("P", Loc.Get("castle.request_pardon"));
                WriteSRMenuOption("L", Loc.Get("castle.request_loan"));
                WriteSRMenuOption("C", Loc.Get("castle.report_crime"));
                WriteSRMenuOption("B", Loc.Get("castle.request_blessing"));
                WriteSRMenuOption("T", Loc.Get("castle.tax_relief"));
                terminal.WriteLine("");
                WriteSRMenuOption("R", Loc.Get("castle.return_castle"));
                terminal.WriteLine("");
            }
            else
            {
                terminal.SetColor("darkgray");
                terminal.Write(" [");
                terminal.SetColor("bright_cyan");
                terminal.Write("Q");
                terminal.SetColor("darkgray");
                terminal.Write("]");
                terminal.SetColor("white");
                terminal.WriteLine($" {Loc.Get("castle.request_quest")}");

                terminal.SetColor("darkgray");
                terminal.Write(" [");
                terminal.SetColor("bright_yellow");
                terminal.Write("K");
                terminal.SetColor("darkgray");
                terminal.Write("]");
                terminal.SetColor("white");
                terminal.Write($" {Loc.Get("castle.request_knighthood")}");
                terminal.SetColor("gray");
                terminal.WriteLine(Loc.Get("castle.req_chiv_fame"));

                terminal.SetColor("darkgray");
                terminal.Write(" [");
                terminal.SetColor("bright_green");
                terminal.Write("P");
                terminal.SetColor("darkgray");
                terminal.Write("]");
                terminal.SetColor("white");
                terminal.Write($" {Loc.Get("castle.request_pardon")}");
                terminal.SetColor("gray");
                terminal.WriteLine(Loc.Get("castle.req_clear_darkness"));

                terminal.SetColor("darkgray");
                terminal.Write(" [");
                terminal.SetColor("bright_magenta");
                terminal.Write("L");
                terminal.SetColor("darkgray");
                terminal.Write("]");
                terminal.SetColor("white");
                terminal.Write($" {Loc.Get("castle.request_loan")}");
                terminal.SetColor("gray");
                terminal.WriteLine(Loc.Get("castle.req_borrow_treasury"));

                terminal.SetColor("darkgray");
                terminal.Write(" [");
                terminal.SetColor("bright_red");
                terminal.Write("C");
                terminal.SetColor("darkgray");
                terminal.Write("]");
                terminal.SetColor("white");
                terminal.WriteLine($" {Loc.Get("castle.report_crime")}");

                terminal.SetColor("darkgray");
                terminal.Write(" [");
                terminal.SetColor("cyan");
                terminal.Write("B");
                terminal.SetColor("darkgray");
                terminal.Write("]");
                terminal.SetColor("white");
                terminal.Write($" {Loc.Get("castle.request_blessing")}");
                terminal.SetColor("gray");
                terminal.WriteLine(Loc.Get("castle.req_temp_stat_boost"));

                terminal.SetColor("darkgray");
                terminal.Write(" [");
                terminal.SetColor("yellow");
                terminal.Write("T");
                terminal.SetColor("darkgray");
                terminal.Write("]");
                terminal.SetColor("white");
                terminal.Write($" {Loc.Get("castle.tax_relief")}");
                terminal.SetColor("gray");
                terminal.WriteLine(Loc.Get("castle.req_reduce_taxes"));

                terminal.WriteLine("");
                terminal.SetColor("darkgray");
                terminal.Write(" [");
                terminal.SetColor("red");
                terminal.Write("R");
                terminal.SetColor("darkgray");
                terminal.Write("]");
                terminal.SetColor("white");
                terminal.WriteLine($" {Loc.Get("castle.return_castle")}");
                terminal.WriteLine("");
            }

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
        if (reputation >= 500) return Loc.Get("castle.rep_legendary_hero");
        if (reputation >= 300) return Loc.Get("castle.rep_champion_realm");
        if (reputation >= 200) return Loc.Get("castle.rep_honored_noble");
        if (reputation >= 150) return Loc.Get("castle.rep_trusted_ally");
        if (reputation >= 100) return Loc.Get("castle.rep_respected_citizen");
        if (reputation >= 50) return Loc.Get("castle.rep_known_adventurer");
        if (reputation >= 25) return Loc.Get("castle.rep_common_petitioner");
        return Loc.Get("castle.rep_unknown");
    }

    /// <summary>
    /// Request a special royal quest from the king
    /// </summary>
    private async Task AudienceRequestQuest()
    {
        terminal.ClearScreen();
        WriteSectionHeader(Loc.Get("castle.request_quest"), "bright_cyan");
        terminal.WriteLine("");

        long reputation = (currentPlayer.Chivalry + currentPlayer.Fame) / 2;

        if (reputation < 50)
        {
            terminal.SetColor("yellow");
            terminal.WriteLine(Loc.Get("castle.quest_waves_dismiss", currentKing.GetTitle(), currentKing.Name));
            terminal.WriteLine(Loc.Get("castle.quest_not_proven"));
            terminal.WriteLine("");
            terminal.SetColor("gray");
            terminal.WriteLine(Loc.Get("castle.requires_rep", 50));
        }
        else
        {
            // Check if player already has an active royal quest
            var existingRoyalQuest = QuestSystem.GetActiveQuestsForPlayer(currentPlayer.Name2)
                .FirstOrDefault(q => q.Initiator == currentKing.Name && q.Title.StartsWith("Royal Commission"));

            if (existingRoyalQuest != null)
            {
                terminal.SetColor("yellow");
                terminal.WriteLine(Loc.Get("castle.quest_raises_eyebrow", currentKing.GetTitle(), currentKing.Name));
                terminal.WriteLine(Loc.Get("castle.quest_already_active"));
                terminal.WriteLine("");
                terminal.SetColor("white");
                terminal.WriteLine(Loc.Get("castle.quest_active_title", existingRoyalQuest.Title));
                terminal.WriteLine(Loc.Get("castle.quest_days_remaining", existingRoyalQuest.DaysRemaining));
                terminal.WriteLine("");
                terminal.SetColor("gray");
                terminal.WriteLine(Loc.Get("castle.quest_complete_first"));
            }
            else
            {
                terminal.SetColor("bright_green");
                terminal.WriteLine(Loc.Get("castle.quest_considers", currentKing.GetTitle(), currentKing.Name));
                terminal.WriteLine("");

                // Generate a special royal quest with better rewards
                var random = Random.Shared;
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
                terminal.WriteLine(Loc.Get("castle.quest_task_worthy"));
                terminal.WriteLine("");
                terminal.SetColor("white");
                terminal.WriteLine(Loc.Get("castle.quest_desc", questDesc));
                terminal.WriteLine(Loc.Get("castle.quest_difficulty", new string('*', difficulty + 1)));
                terminal.WriteLine(Loc.Get("castle.quest_reward", goldReward, xpReward));
                terminal.WriteLine(Loc.Get("castle.quest_time_limit", 7 + difficulty * 2));
                terminal.WriteLine("");

                terminal.SetColor("cyan");
                terminal.Write(Loc.Get("castle.quest_accept_yn"));
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
                    terminal.WriteLine(Loc.Get("castle.quest_excellent"));
                    terminal.WriteLine(Loc.Get("castle.quest_advance_gold", advanceGold));
                    terminal.WriteLine(Loc.Get("castle.quest_advance_xp", advanceXP));
                    terminal.WriteLine("");
                    terminal.SetColor("yellow");
                    terminal.WriteLine(Loc.Get("castle.quest_added"));
                    terminal.WriteLine(Loc.Get("castle.quest_check_hall"));

                    // Show objective
                    if (quest.Objectives.Count > 0)
                    {
                        terminal.WriteLine("");
                        terminal.SetColor("cyan");
                        terminal.WriteLine(Loc.Get("castle.quest_objective", quest.Objectives[0].Description));
                        if (quest.Objectives[0].RequiredProgress > 1)
                        {
                            terminal.WriteLine(Loc.Get("castle.quest_target", quest.Objectives[0].TargetName, quest.Objectives[0].RequiredProgress));
                        }
                    }

                    NewsSystem.Instance?.Newsy(true, $"{currentPlayer.DisplayName} accepted a royal quest from {currentKing.GetTitle()} {currentKing.Name}!");
                }
                else
                {
                    terminal.WriteLine("");
                    terminal.SetColor("gray");
                    terminal.WriteLine(Loc.Get("castle.quest_another_time"));
                }
            }
        }

        terminal.WriteLine("");
        terminal.SetColor("darkgray");
        terminal.WriteLine(Loc.Get("ui.press_enter"));
        await terminal.ReadKeyAsync();
    }

    /// <summary>
    /// Request knighthood/title from the king — cinematic ceremony with server broadcast
    /// </summary>
    private async Task AudienceRequestKnighthood()
    {
        terminal.ClearScreen();
        WriteSectionHeader(Loc.Get("castle.request_knighthood"), "bright_yellow");
        terminal.WriteLine("");

        // Check requirements
        bool hasChivalry = currentPlayer.Chivalry >= 200;
        bool hasFame = currentPlayer.Fame >= 150;
        bool hasLevel = currentPlayer.Level >= 15;
        bool notEvil = currentPlayer.Darkness < currentPlayer.Chivalry;

        if (currentPlayer.IsKnighted)
        {
            terminal.SetColor("cyan");
            terminal.WriteLine(Loc.Get("castle.knight_smiles", currentKing.GetTitle(), currentKing.Name));
            terminal.WriteLine(Loc.Get("castle.knight_already_titled", currentPlayer.NobleTitle, currentPlayer.DisplayName));
            terminal.WriteLine(Loc.Get("castle.knight_serve_honor"));
        }
        else if (!hasChivalry || !hasFame || !hasLevel || !notEvil)
        {
            terminal.SetColor("yellow");
            terminal.WriteLine(Loc.Get("castle.knight_shakes_head", currentKing.GetTitle(), currentKing.Name));
            terminal.WriteLine(Loc.Get("castle.knight_not_ready"));
            terminal.WriteLine("");
            terminal.SetColor("gray");
            terminal.WriteLine(Loc.Get("castle.knight_requirements"));
            terminal.WriteLine(Loc.Get("castle.knight_req_chivalry", hasChivalry ? Loc.Get("ui.yes") : $"{Loc.Get("ui.no")} ({currentPlayer.Chivalry}/200)"));
            terminal.WriteLine(Loc.Get("castle.knight_req_fame", hasFame ? Loc.Get("ui.yes") : $"{Loc.Get("ui.no")} ({currentPlayer.Fame}/150)"));
            terminal.WriteLine(Loc.Get("castle.knight_req_level", hasLevel ? Loc.Get("ui.yes") : $"{Loc.Get("ui.no")} ({currentPlayer.Level}/15)"));
            terminal.WriteLine(Loc.Get("castle.knight_req_honorable", notEvil ? Loc.Get("ui.yes") : Loc.Get("ui.no")));
        }
        else
        {
            // === CINEMATIC KNIGHTING CEREMONY ===
            terminal.ClearScreen();

            // Scene 1: The throne room falls silent
            terminal.SetColor("gray");
            terminal.WriteLine("  The throne room falls silent as the court herald raises his hand.");
            terminal.WriteLine("");
            await Task.Delay(1500);

            terminal.SetColor("bright_yellow");
            terminal.WriteLine($"  \"All rise for {currentKing.GetTitle()} {currentKing.Name}!\"");
            terminal.WriteLine("");
            await Task.Delay(1500);

            // Scene 2: The king addresses the court
            terminal.SetColor("gray");
            terminal.WriteLine("  The assembled nobles and courtiers turn their gaze toward you.");
            terminal.WriteLine("  Torchlight dances across the stone walls of the great hall.");
            terminal.WriteLine("");
            await Task.Delay(2000);

            terminal.SetColor("bright_cyan");
            terminal.WriteLine($"  {currentKing.GetTitle()} {currentKing.Name} rises from the throne and speaks:");
            terminal.WriteLine("");
            await Task.Delay(1000);

            terminal.SetColor("white");
            terminal.WriteLine($"  \"We have watched {currentPlayer.DisplayName} prove their valor");
            terminal.WriteLine("   through countless battles, acts of honor, and service to the realm.\"");
            terminal.WriteLine("");
            await Task.Delay(2000);

            // Scene 3: Approach the throne
            terminal.SetColor("bright_yellow");
            terminal.WriteLine("  \"Step forward and kneel before the throne.\"");
            terminal.WriteLine("");
            await Task.Delay(1500);

            terminal.SetColor("gray");
            terminal.WriteLine("  Your footsteps echo through the silent hall as you approach.");
            terminal.WriteLine("  You kneel on the cold stone before the throne.");
            terminal.WriteLine("");
            await terminal.PressAnyKey();

            // Scene 4: The dubbing
            terminal.ClearScreen();
            terminal.SetColor("gray");
            terminal.WriteLine("");
            terminal.WriteLine("  The king draws the ceremonial blade — an ancient sword that has");
            terminal.WriteLine("  touched the shoulders of every knight in the realm's history.");
            terminal.WriteLine("");
            await Task.Delay(2000);

            string title = currentPlayer.Sex == CharacterSex.Male ? "Sir" : "Dame";
            currentPlayer.NobleTitle = title;

            // Persist knight title to MetaProgression so it survives NG+ cycles
            MetaProgressionSystem.Instance.UnlockedTitles.Add(title);
            MetaProgressionSystem.Instance.SaveData();

            terminal.SetColor("bright_cyan");
            terminal.WriteLine("  The blade touches your right shoulder...");
            terminal.WriteLine("");
            await Task.Delay(1500);

            terminal.SetColor("white");
            terminal.WriteLine("  \"By the authority vested in me as sovereign of this realm...\"");
            terminal.WriteLine("");
            await Task.Delay(1500);

            terminal.SetColor("bright_cyan");
            terminal.WriteLine("  The blade crosses to your left shoulder...");
            terminal.WriteLine("");
            await Task.Delay(1500);

            terminal.SetColor("white");
            terminal.WriteLine("  \"For your valor in battle, your honor in deed,");
            terminal.WriteLine("   and your unwavering service to the people of this land...\"");
            terminal.WriteLine("");
            await Task.Delay(2000);

            terminal.SetColor("bright_yellow");
            terminal.WriteLine($"  \"I dub thee {title} {currentPlayer.DisplayName},");
            terminal.WriteLine("   Knight of the Realm!\"");
            terminal.WriteLine("");
            await Task.Delay(1500);

            // Scene 5: Rise
            terminal.SetColor("bright_green");
            terminal.WriteLine($"  \"Rise, {title} {currentPlayer.DisplayName}. You are now a Knight of the Realm.\"");
            terminal.WriteLine("");
            await Task.Delay(1000);

            terminal.SetColor("gray");
            terminal.WriteLine("  The court erupts in applause. Nobles bow their heads in recognition.");
            terminal.WriteLine("  The herald announces your new title to all present.");
            terminal.WriteLine("");
            await Task.Delay(1500);

            // Bonuses for knighthood
            currentPlayer.Chivalry += 50;
            currentPlayer.Fame += 25;

            // Show benefits
            terminal.SetColor("bright_yellow");
            terminal.WriteLine("  ══════════════════════════════════════════");
            terminal.WriteLine($"   You are now {title} {currentPlayer.DisplayName}!");
            terminal.WriteLine("  ══════════════════════════════════════════");
            terminal.SetColor("bright_green");
            terminal.WriteLine($"   +{(int)(GameConfig.KnightDamageBonus * 100)}% damage in combat (permanent)");
            terminal.WriteLine($"   +{(int)(GameConfig.KnightDefenseBonus * 100)}% defense in combat (permanent)");
            terminal.WriteLine("   +50 Chivalry");
            terminal.WriteLine("   +25 Fame");
            terminal.WriteLine("   Knight title shown in Who's Online");
            terminal.SetColor("bright_yellow");
            terminal.WriteLine("  ══════════════════════════════════════════");

            // News and broadcast
            string knightNews = $"{title} {currentPlayer.DisplayName} was knighted by {currentKing.GetTitle()} {currentKing.Name}!";
            NewsSystem.Instance?.Newsy(true, knightNews);

            // Server-wide broadcast for online mode
            if (UsurperRemake.BBS.DoorMode.IsOnlineMode)
            {
                var mudServer = UsurperRemake.Server.MudServer.Instance;
                if (mudServer != null)
                {
                    mudServer.BroadcastToAll(
                        $"\u001b[1;33m  *** {title} {currentPlayer.DisplayName} has been knighted by {currentKing.GetTitle()} {currentKing.Name}! ***\u001b[0m",
                        currentPlayer.DisplayName.ToLowerInvariant()
                    );
                }
            }
        }

        terminal.WriteLine("");
        terminal.SetColor("darkgray");
        terminal.WriteLine(Loc.Get("ui.press_enter"));
        await terminal.ReadKeyAsync();
    }

    /// <summary>
    /// Request a pardon to reduce darkness
    /// </summary>
    private async Task AudienceRequestPardon()
    {
        terminal.ClearScreen();
        WriteSectionHeader(Loc.Get("castle.request_pardon"), "bright_green");
        terminal.WriteLine("");

        if (currentPlayer.Darkness <= 0)
        {
            terminal.SetColor("cyan");
            terminal.WriteLine(Loc.Get("castle.pardon_puzzled", currentKing.GetTitle(), currentKing.Name));
            terminal.WriteLine(Loc.Get("castle.pardon_no_sins"));
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
            terminal.WriteLine(Loc.Get("castle.pardon_current_darkness", currentPlayer.Darkness));
            terminal.WriteLine("");

            terminal.SetColor("yellow");
            terminal.WriteLine(Loc.Get("castle.pardon_considers", currentKing.GetTitle(), currentKing.Name));
            terminal.WriteLine("");
            terminal.SetColor("white");
            terminal.WriteLine(Loc.Get("castle.pardon_absolution_price"));
            terminal.WriteLine("");
            terminal.WriteLine(Loc.Get("castle.pardon_cost", pardonCost));
            terminal.WriteLine(Loc.Get("castle.your_gold", currentPlayer.Gold));
            terminal.WriteLine("");

            if (currentPlayer.Gold < pardonCost)
            {
                terminal.SetColor("red");
                terminal.WriteLine(Loc.Get("castle.pardon_cannot_afford"));
                terminal.WriteLine("");

                // Offer partial pardon
                long partialCost = pardonCost / 4;
                long partialReduction = currentPlayer.Darkness / 4;

                if (currentPlayer.Gold >= partialCost && partialReduction > 0)
                {
                    terminal.SetColor("cyan");
                    terminal.Write(Loc.Get("castle.pardon_partial_yn", partialReduction, partialCost));
                    string partial = await terminal.ReadLineAsync();

                    if (partial?.ToUpper() == "Y")
                    {
                        currentPlayer.Gold -= partialCost;
                        currentPlayer.Darkness -= partialReduction;
                        currentKing.Treasury += partialCost;

                        terminal.WriteLine("");
                        terminal.SetColor("bright_green");
                        terminal.WriteLine(Loc.Get("castle.pardon_some_forgiven"));
                        terminal.WriteLine(Loc.Get("castle.pardon_darkness_reduced", partialReduction, currentPlayer.Darkness));
                    }
                }
            }
            else
            {
                terminal.SetColor("cyan");
                terminal.Write(Loc.Get("castle.pardon_full_yn", pardonCost));
                string response = await terminal.ReadLineAsync();

                if (response?.ToUpper() == "Y")
                {
                    currentPlayer.Gold -= pardonCost;
                    long oldDarkness = currentPlayer.Darkness;
                    currentPlayer.Darkness = 0;
                    currentKing.Treasury += pardonCost;

                    terminal.WriteLine("");
                    terminal.SetColor("bright_green");
                    terminal.WriteLine(Loc.Get("castle.pardon_hand_blessing", currentKing.GetTitle(), currentKing.Name));
                    terminal.WriteLine(Loc.Get("castle.pardon_sins_forgiven"));
                    terminal.WriteLine("");
                    terminal.WriteLine(Loc.Get("castle.pardon_darkness_to_zero", oldDarkness));

                    NewsSystem.Instance?.Newsy(false, $"{currentPlayer.DisplayName} received a royal pardon from {currentKing.GetTitle()} {currentKing.Name}.");
                }
            }
        }

        terminal.WriteLine("");
        terminal.SetColor("darkgray");
        terminal.WriteLine(Loc.Get("ui.press_enter"));
        await terminal.ReadKeyAsync();
    }

    /// <summary>
    /// Request a loan from the royal treasury
    /// </summary>
    private async Task AudienceRequestLoan()
    {
        terminal.ClearScreen();
        WriteSectionHeader(Loc.Get("castle.request_loan"), "bright_magenta");
        terminal.WriteLine("");

        long reputation = (currentPlayer.Chivalry + currentPlayer.Fame) / 2;

        // Check if player has an outstanding loan
        if (currentPlayer.RoyalLoanAmount > 0)
        {
            // Show loan status and offer repayment
            long totalOwed = (long)(currentPlayer.RoyalLoanAmount * 1.10); // 10% interest
            int daysRemaining = currentPlayer.RoyalLoanDueDay - DailySystemManager.Instance.CurrentDay;

            terminal.SetColor("yellow");
            terminal.WriteLine(Loc.Get("castle.loan_outstanding"));
            terminal.WriteLine("");
            terminal.SetColor("white");
            terminal.WriteLine(Loc.Get("castle.loan_principal", currentPlayer.RoyalLoanAmount));
            terminal.WriteLine(Loc.Get("castle.loan_with_interest", totalOwed));
            terminal.WriteLine(Loc.Get("castle.loan_days_remaining", Math.Max(0, daysRemaining)));
            terminal.WriteLine(Loc.Get("castle.your_gold", currentPlayer.Gold));
            terminal.WriteLine("");

            if (daysRemaining < 0)
            {
                terminal.SetColor("red");
                terminal.WriteLine(Loc.Get("castle.loan_overdue"));
                terminal.WriteLine(Loc.Get("castle.loan_demands_repayment"));
            }

            if (currentPlayer.Gold >= totalOwed)
            {
                terminal.SetColor("cyan");
                terminal.Write(Loc.Get("castle.loan_repay_yn", totalOwed));
                string response = await terminal.ReadLineAsync();

                if (response?.ToUpper() == "Y")
                {
                    currentPlayer.Gold -= totalOwed;
                    currentKing.Treasury += totalOwed;
                    currentPlayer.RoyalLoanAmount = 0;
                    currentPlayer.RoyalLoanDueDay = 0;

                    terminal.WriteLine("");
                    terminal.SetColor("bright_green");
                    terminal.WriteLine(Loc.Get("castle.loan_nods_approvingly", currentKing.GetTitle(), currentKing.Name));
                    terminal.WriteLine(Loc.Get("castle.loan_debt_paid"));
                    terminal.WriteLine("");
                    terminal.WriteLine(Loc.Get("castle.loan_chivalry_bonus"));
                    currentPlayer.Chivalry += 10;

                    NewsSystem.Instance?.Newsy(false, $"{currentPlayer.DisplayName} repaid their royal loan.");
                }
            }
            else
            {
                terminal.SetColor("gray");
                terminal.WriteLine(Loc.Get("ui.not_enough_gold_repay"));
                terminal.WriteLine(Loc.Get("castle.loan_gather_funds"));
            }
        }
        else if (reputation < 75)
        {
            terminal.SetColor("yellow");
            terminal.WriteLine(Loc.Get("castle.loan_skeptical", currentKing.GetTitle(), currentKing.Name));
            terminal.WriteLine(Loc.Get("castle.loan_uncertain_rep"));
            terminal.WriteLine("");
            terminal.SetColor("gray");
            terminal.WriteLine(Loc.Get("castle.requires_rep", 75));
        }
        else if (currentKing.Treasury < 1000)
        {
            terminal.SetColor("red");
            terminal.WriteLine(Loc.Get("castle.loan_sighs", currentKing.GetTitle(), currentKing.Name));
            terminal.WriteLine(Loc.Get("castle.loan_coffers_empty"));
        }
        else
        {
            // Max loan based on reputation and level
            long maxLoan = Math.Min(currentKing.Treasury / 2, reputation * 10 * currentPlayer.Level);
            maxLoan = Math.Max(500, maxLoan); // Minimum loan

            terminal.SetColor("white");
            terminal.WriteLine(Loc.Get("castle.loan_treasury", currentKing.Treasury));
            terminal.WriteLine(Loc.Get("castle.loan_max_available", maxLoan));
            terminal.WriteLine("");
            terminal.SetColor("yellow");
            terminal.WriteLine(Loc.Get("castle.loan_terms"));
            terminal.WriteLine(Loc.Get("castle.loan_failure_warning"));
            terminal.WriteLine("");

            terminal.SetColor("cyan");
            terminal.Write(Loc.Get("castle.loan_how_much", maxLoan));
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
                terminal.WriteLine(Loc.Get("castle.loan_nods", currentKing.GetTitle(), currentKing.Name));
                terminal.WriteLine(Loc.Get("castle.loan_grants", amount));
                terminal.WriteLine(Loc.Get("castle.loan_repay_days", (long)(amount * 1.10)));
                terminal.WriteLine("");
                terminal.WriteLine(Loc.Get("castle.loan_received", amount));

                NewsSystem.Instance?.Newsy(false, $"{currentPlayer.DisplayName} received a loan of {amount:N0} gold from the Crown.");
            }
            else if (amount == 0 || string.IsNullOrEmpty(input))
            {
                terminal.WriteLine("");
                terminal.SetColor("gray");
                terminal.WriteLine(Loc.Get("castle.loan_offer_stands"));
            }
            else
            {
                terminal.WriteLine("");
                terminal.SetColor("red");
                terminal.WriteLine(Loc.Get("castle.loan_invalid_amount"));
            }
        }

        terminal.WriteLine("");
        terminal.SetColor("darkgray");
        terminal.WriteLine(Loc.Get("ui.press_enter"));
        await terminal.ReadKeyAsync();
    }

    /// <summary>
    /// Report a crime and place a bounty on an NPC
    /// </summary>
    private async Task AudienceReportCrime()
    {
        terminal.ClearScreen();
        WriteSectionHeader(Loc.Get("castle.report_crime"), "bright_red");
        terminal.WriteLine("");

        terminal.SetColor("white");
        terminal.WriteLine(Loc.Get("castle.crime_listens", currentKing.GetTitle(), currentKing.Name));
        terminal.WriteLine(Loc.Get("castle.crime_who_wronged"));
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
            terminal.WriteLine(Loc.Get("castle.crime_no_criminals"));
        }
        else
        {
            terminal.SetColor("gray");
            terminal.WriteLine(Loc.Get("castle.crime_suspicious"));
            terminal.WriteLine("");

            for (int i = 0; i < npcs.Count; i++)
            {
                var npc = npcs[i];
                terminal.SetColor("white");
                terminal.Write($"  {i + 1}. {npc.Name,-20}");
                terminal.SetColor("gray");
                terminal.Write(Loc.Get("castle.npc_lv", npc.Level));
                terminal.SetColor("red");
                terminal.WriteLine(Loc.Get("castle.darkness_label", npc.Darkness));
            }

            terminal.WriteLine("");
            terminal.SetColor("cyan");
            terminal.Write(Loc.Get("castle.crime_enter_number"));
            string input = await terminal.ReadLineAsync();

            if (int.TryParse(input, out int choice) && choice > 0 && choice <= npcs.Count)
            {
                var target = npcs[choice - 1];
                long bountyCost = 100 + target.Level * 50;

                terminal.WriteLine("");
                terminal.SetColor("yellow");
                terminal.WriteLine(Loc.Get("castle.crime_bounty_cost", target.Name, bountyCost));
                terminal.WriteLine(Loc.Get("castle.your_gold", currentPlayer.Gold));
                terminal.WriteLine("");

                if (currentPlayer.Gold >= bountyCost)
                {
                    terminal.SetColor("cyan");
                    terminal.Write(Loc.Get("castle.crime_pay_bounty_yn", bountyCost));
                    string confirm = await terminal.ReadLineAsync();

                    if (confirm?.ToUpper() == "Y")
                    {
                        currentPlayer.Gold -= bountyCost;
                        currentKing.Treasury += bountyCost / 2; // Half goes to treasury

                        terminal.WriteLine("");
                        terminal.SetColor("bright_green");
                        terminal.WriteLine(Loc.Get("castle.crime_nods_grimly", currentKing.GetTitle(), currentKing.Name));
                        terminal.WriteLine(Loc.Get("castle.crime_bounty_placed", target.Name));
                        terminal.WriteLine(Loc.Get("castle.crime_justice_served"));

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
                    terminal.WriteLine(Loc.Get("castle.crime_cannot_afford"));
                }
            }
        }

        terminal.WriteLine("");
        terminal.SetColor("darkgray");
        terminal.WriteLine(Loc.Get("ui.press_enter"));
        await terminal.ReadKeyAsync();
    }

    /// <summary>
    /// Request a temporary blessing/buff from the king
    /// </summary>
    private async Task AudienceRequestBlessing()
    {
        terminal.ClearScreen();
        WriteSectionHeader(Loc.Get("castle.request_blessing"), "cyan");
        terminal.WriteLine("");

        long reputation = (currentPlayer.Chivalry + currentPlayer.Fame) / 2;

        if (reputation < 100)
        {
            terminal.SetColor("yellow");
            terminal.WriteLine(Loc.Get("castle.blessing_considers", currentKing.GetTitle(), currentKing.Name));
            terminal.WriteLine(Loc.Get("castle.blessing_not_earned"));
            terminal.WriteLine("");
            terminal.SetColor("gray");
            terminal.WriteLine(Loc.Get("castle.requires_rep", 100));
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
                terminal.WriteLine(Loc.Get("castle.blessing_smiles", currentKing.GetTitle(), currentKing.Name));
                terminal.WriteLine(Loc.Get("castle.blessing_freely_given"));
            }
            else if (reputation >= 200)
            {
                blessingCost /= 2;
                terminal.SetColor("white");
                terminal.WriteLine(Loc.Get("castle.blessing_nods", currentKing.GetTitle(), currentKing.Name));
                terminal.WriteLine(Loc.Get("castle.blessing_for_gold", blessingCost));
            }
            else
            {
                terminal.SetColor("white");
                terminal.WriteLine(Loc.Get("castle.blessing_considers_2", currentKing.GetTitle(), currentKing.Name));
                terminal.WriteLine(Loc.Get("castle.blessing_donation_req", blessingCost));
            }

            terminal.WriteLine("");
            terminal.SetColor("bright_yellow");
            terminal.WriteLine(Loc.Get("castle.blessing_grants"));
            terminal.WriteLine(Loc.Get("castle.blessing_attack_defense"));
            terminal.WriteLine(Loc.Get("castle.blessing_accuracy"));
            terminal.WriteLine("");

            bool canAfford = currentPlayer.Gold >= blessingCost || blessingCost == 0;

            if (!canAfford)
            {
                terminal.SetColor("red");
                terminal.WriteLine(Loc.Get("castle.blessing_need_gold", blessingCost, currentPlayer.Gold));
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
                    terminal.WriteLine(Loc.Get("castle.blessing_hand_raised", currentKing.GetTitle(), currentKing.Name));
                    terminal.WriteLine("");
                    terminal.SetColor("bright_yellow");
                    terminal.WriteLine(Loc.Get("castle.blessing_golden_light"));
                    terminal.WriteLine("");
                    terminal.SetColor("bright_green");
                    terminal.WriteLine(Loc.Get("castle.blessing_strengthened"));
                    terminal.WriteLine(Loc.Get("castle.blessing_combat_stats"));
                    if (hpRestore > 0 || manaRestore > 0)
                    {
                        terminal.WriteLine(Loc.Get("castle.blessing_restored", hpRestore, manaRestore));
                    }
                    terminal.WriteLine(Loc.Get("castle.blessing_lasts"));

                    NewsSystem.Instance?.Newsy(false, $"{currentPlayer.DisplayName} received the Royal Blessing from {currentKing.GetTitle()} {currentKing.Name}.");
                }
            }
        }

        terminal.WriteLine("");
        terminal.SetColor("darkgray");
        terminal.WriteLine(Loc.Get("ui.press_enter"));
        await terminal.ReadKeyAsync();
    }

    /// <summary>
    /// Petition for reduced taxes in the kingdom
    /// </summary>
    private async Task AudiencePetitionTaxRelief()
    {
        terminal.ClearScreen();
        WriteSectionHeader(Loc.Get("castle.petition_tax"), "yellow");
        terminal.WriteLine("");

        long reputation = (currentPlayer.Chivalry + currentPlayer.Fame) / 2;

        terminal.SetColor("white");
        terminal.WriteLine(Loc.Get("castle.tax_current_rate", currentKing.TaxRate));
        terminal.WriteLine("");

        if (reputation < 150)
        {
            terminal.SetColor("yellow");
            terminal.WriteLine(Loc.Get("castle.tax_amused", currentKing.GetTitle(), currentKing.Name));
            terminal.WriteLine(Loc.Get("castle.tax_who_are_you"));
            terminal.WriteLine("");
            terminal.SetColor("gray");
            terminal.WriteLine(Loc.Get("castle.tax_requires_rep"));
        }
        else if (currentKing.TaxRate <= 5)
        {
            terminal.SetColor("cyan");
            terminal.WriteLine(Loc.Get("castle.tax_spreads_hands", currentKing.GetTitle(), currentKing.Name));
            terminal.WriteLine(Loc.Get("castle.tax_already_low"));
            terminal.WriteLine(Loc.Get("castle.tax_realm_needs_gold"));
        }
        else
        {
            // Cost to petition scales with current tax rate and reputation
            long petitionCost = currentKing.TaxRate * 100 * currentPlayer.Level;
            if (reputation >= 300)
                petitionCost /= 2;

            terminal.SetColor("white");
            terminal.WriteLine(Loc.Get("castle.tax_considers_words", currentKing.GetTitle(), currentKing.Name));
            terminal.WriteLine("");
            terminal.WriteLine(Loc.Get("castle.tax_reduction_arranged"));
            terminal.WriteLine("");
            terminal.WriteLine(Loc.Get("castle.tax_cost_reduce", petitionCost));
            terminal.WriteLine(Loc.Get("castle.your_gold", currentPlayer.Gold));
            terminal.WriteLine("");

            if (currentPlayer.Gold >= petitionCost)
            {
                terminal.SetColor("cyan");
                terminal.Write(Loc.Get("castle.tax_pay_yn", petitionCost));
                string response = await terminal.ReadLineAsync();

                if (response?.ToUpper() == "Y")
                {
                    currentPlayer.Gold -= petitionCost;
                    currentKing.Treasury += petitionCost;
                    long oldRate = currentKing.TaxRate;
                    currentKing.TaxRate = Math.Max(5, currentKing.TaxRate - 5);

                    terminal.WriteLine("");
                    terminal.SetColor("bright_green");
                    terminal.WriteLine(Loc.Get("castle.tax_nods_solemnly", currentKing.GetTitle(), currentKing.Name));
                    terminal.WriteLine(Loc.Get("castle.tax_hereby_reduced"));
                    terminal.WriteLine("");
                    terminal.WriteLine(Loc.Get("castle.tax_rate_reduced", oldRate, currentKing.TaxRate));

                    // Fame boost for helping the people
                    currentPlayer.Fame += 20;
                    currentPlayer.Chivalry += 10;
                    terminal.WriteLine(Loc.Get("castle.tax_fame_chivalry"));

                    NewsSystem.Instance?.Newsy(true, $"{currentPlayer.DisplayName} petitioned {currentKing.GetTitle()} {currentKing.Name} for tax relief! Kingdom taxes reduced to {currentKing.TaxRate}%.");
                }
            }
            else
            {
                terminal.SetColor("red");
                terminal.WriteLine(Loc.Get("castle.tax_cannot_afford"));
            }
        }

        terminal.WriteLine("");
        terminal.SetColor("darkgray");
        terminal.WriteLine(Loc.Get("ui.press_enter"));
        await terminal.ReadKeyAsync();
    }

    private async Task DonateToRoyalPurse()
    {
        if (currentKing == null || !currentKing.IsActive)
        {
            terminal.SetColor("yellow");
            terminal.WriteLine(Loc.Get("castle.donate_no_monarch"));
            await Task.Delay(2000);
            return;
        }

        terminal.SetColor("white");
        terminal.WriteLine(Loc.Get("castle.donate_purse_contains", currentKing.Treasury));
        terminal.WriteLine(Loc.Get("castle.donate_you_have", currentPlayer.Gold));
        terminal.WriteLine("");

        terminal.SetColor("cyan");
        terminal.Write(Loc.Get("castle.donate_how_much"));
        terminal.SetColor("white");
        string input = await terminal.ReadLineAsync();

        if (long.TryParse(input, out long amount))
        {
            if (amount <= 0)
            {
                terminal.SetColor("gray");
                terminal.WriteLine(Loc.Get("castle.donate_cancelled"));
            }
            else if (amount > currentPlayer.Gold)
            {
                terminal.SetColor("red");
                terminal.WriteLine(Loc.Get("castle.donate_not_enough"));
            }
            else
            {
                currentPlayer.Gold -= amount;
                currentKing.Treasury += amount;
                int chivalryGain = (int)Math.Min(50, amount / 100);
                currentPlayer.Chivalry += chivalryGain;

                terminal.SetColor("bright_green");
                terminal.WriteLine(Loc.Get("castle.donate_gold", amount));
                terminal.WriteLine(Loc.Get("castle.donate_chivalry", chivalryGain));

                // Increase Crown standing (+1 per 50 gold donated)
                int crownStandingGain = (int)(amount / 50);
                if (crownStandingGain > 0)
                {
                    UsurperRemake.Systems.FactionSystem.Instance.ModifyReputation(UsurperRemake.Systems.Faction.TheCrown, crownStandingGain);
                    terminal.SetColor("bright_yellow");
                    terminal.WriteLine(Loc.Get("castle.crown_standing", crownStandingGain));
                }
            }
        }
        else
        {
            terminal.SetColor("gray");
            terminal.WriteLine(Loc.Get("castle.donate_invalid"));
        }

        await Task.Delay(2500);
    }

    /// <summary>
    /// Player applies for a position in the Royal Guard
    /// </summary>
    private async Task ApplyForRoyalGuard()
    {
        terminal.ClearScreen();
        WriteBoxHeader(Loc.Get("castle.guard_recruitment"), "bright_cyan");
        terminal.WriteLine("");

        // Check if there's a king
        if (currentKing == null || !currentKing.IsActive)
        {
            terminal.SetColor("yellow");
            terminal.WriteLine(Loc.Get("castle.guard_no_monarch"));
            terminal.WriteLine(Loc.Get("castle.guard_no_accept"));
            terminal.WriteLine("");
            terminal.SetColor("darkgray");
            terminal.WriteLine(Loc.Get("ui.press_enter"));
            await terminal.ReadKeyAsync();
            return;
        }

        // Check if guard slots are full
        if (currentKing.Guards.Count >= GameConfig.MaxRoyalGuards)
        {
            terminal.SetColor("yellow");
            terminal.WriteLine(Loc.Get("castle.guard_full_capacity"));
            terminal.WriteLine(Loc.Get("castle.guard_no_positions"));
            terminal.WriteLine("");
            terminal.SetColor("darkgray");
            terminal.WriteLine(Loc.Get("ui.press_enter"));
            await terminal.ReadKeyAsync();
            return;
        }

        // Check if player is already a guard
        if (currentKing.Guards.Any(g => g.Name == currentPlayer.DisplayName || g.Name == currentPlayer.Name2))
        {
            terminal.SetColor("yellow");
            terminal.WriteLine(Loc.Get("castle.guard_already_serving"));
            terminal.WriteLine("");
            terminal.SetColor("darkgray");
            terminal.WriteLine(Loc.Get("ui.press_enter"));
            await terminal.ReadKeyAsync();
            return;
        }

        // Minimum level requirement
        int minLevel = 5;
        if (currentPlayer.Level < minLevel)
        {
            terminal.SetColor("red");
            terminal.WriteLine(Loc.Get("castle.guard_looks_over"));
            terminal.WriteLine("");
            terminal.SetColor("yellow");
            terminal.WriteLine(Loc.Get("castle.guard_come_back"));
            terminal.WriteLine(Loc.Get("castle.guard_min_level", minLevel));
            terminal.WriteLine("");
            terminal.SetColor("darkgray");
            terminal.WriteLine(Loc.Get("ui.press_enter"));
            await terminal.ReadKeyAsync();
            return;
        }

        // Check reputation (chivalry vs darkness)
        if (currentPlayer.Darkness > currentPlayer.Chivalry + 50)
        {
            terminal.SetColor("red");
            terminal.WriteLine(Loc.Get("castle.guard_narrows_eyes"));
            terminal.WriteLine("");
            terminal.SetColor("yellow");
            terminal.WriteLine(Loc.Get("castle.guard_bad_rep"));
            terminal.WriteLine(Loc.Get("castle.guard_seek_redemption"));
            terminal.WriteLine("");
            terminal.SetColor("darkgray");
            terminal.WriteLine(Loc.Get("ui.press_enter"));
            await terminal.ReadKeyAsync();
            return;
        }

        // Display offer
        long salary = GameConfig.BaseGuardSalary + (currentPlayer.Level * 20);
        terminal.SetColor("white");
        terminal.WriteLine(Loc.Get("castle.guard_reviews"));
        terminal.WriteLine("");
        terminal.SetColor("bright_green");
        terminal.WriteLine(Loc.Get("castle.guard_impressive", currentPlayer.DisplayName, currentPlayer.Level));
        terminal.WriteLine("");
        terminal.SetColor("cyan");
        terminal.WriteLine(Loc.Get("castle.guard_position"));
        terminal.WriteLine(Loc.Get("castle.guard_salary", salary));
        terminal.WriteLine(Loc.Get("castle.guard_current_count", currentKing.Guards.Count, GameConfig.MaxRoyalGuards));
        terminal.WriteLine("");
        terminal.SetColor("yellow");
        terminal.WriteLine(Loc.Get("castle.guard_as_guard"));
        terminal.WriteLine(Loc.Get("castle.guard_defend"));
        terminal.WriteLine(Loc.Get("castle.guard_receive_salary"));
        terminal.WriteLine(Loc.Get("castle.guard_gain_prestige"));
        terminal.WriteLine("");

        terminal.SetColor("bright_cyan");
        terminal.Write(Loc.Get("castle.guard_join_yn"));
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
            terminal.WriteLine(Loc.Get("castle.guard_captain_smiles"));
            terminal.WriteLine(Loc.Get("castle.guard_welcome", currentPlayer.DisplayName));
            terminal.WriteLine("");
            terminal.SetColor("bright_yellow");
            terminal.WriteLine(Loc.Get("castle.guard_now_member", currentKing.GetTitle(), currentKing.Name));

            // News announcement
            NewsSystem.Instance?.Newsy(true, $"{currentPlayer.DisplayName} has joined the Royal Guard!");

            // Small chivalry boost
            currentPlayer.Chivalry += 10;
        }
        else
        {
            terminal.WriteLine("");
            terminal.SetColor("gray");
            terminal.WriteLine(Loc.Get("castle.guard_nods_understand"));
            terminal.WriteLine(Loc.Get("castle.guard_offer_stands"));
        }

        terminal.WriteLine("");
        terminal.SetColor("darkgray");
        terminal.WriteLine(Loc.Get("ui.press_enter"));
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
    /// <summary>
    /// Static version for use from other locations (e.g., PrisonLocation bail payment)
    /// </summary>
    public static void PersistRoyalCourtToWorldStateStatic()
    {
        if (!UsurperRemake.BBS.DoorMode.IsOnlineMode) return;
        var osm = OnlineStateManager.Instance;
        if (osm == null) return;
        _ = Task.Run(async () =>
        {
            try { await osm.SaveRoyalCourtToWorldState(); }
            catch (Exception ex)
            {
                DebugLogger.Instance.LogError("CASTLE", $"Failed to persist royal court (static): {ex.Message}");
            }
        });
    }

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
        // Inherit prisoners and orphans from previous king
        var inheritedPrisoners = currentKing?.Prisoners?.ToDictionary(p => p.Key, p => p.Value);
        var inheritedOrphans = currentKing?.Orphans?.ToList();

        ClearRoyalMarriage(currentKing); // Clear old king's royal spouse before crowning NPC
        currentKing = King.CreateNewKing(npc.DisplayName, CharacterAI.Computer, npc.Sex, inheritedOrphans);
        currentKing.Treasury = Math.Max(10000, npc.Gold / 2);
        currentKing.TotalReign = 0;
        currentKing.CoronationDate = DateTime.Now;

        if (inheritedPrisoners != null && inheritedPrisoners.Count > 0)
        {
            foreach (var p in inheritedPrisoners)
                currentKing.Prisoners[p.Key] = p.Value;
        }

        // Deduct donated gold from NPC (prevents gold duplication)
        npc.Gold = Math.Max(0, npc.Gold - npc.Gold / 2);

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
        if (player.NobleTitle == "King" || player.NobleTitle == "Queen")
            player.NobleTitle = null;
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
            terminal.WriteLine($"\n  {Loc.Get("castle.armory_restricted")}");
            await Task.Delay(2000);
            return;
        }

        terminal.ClearScreen();
        WriteBoxHeader(Loc.Get("castle.royal_armory"), "bright_yellow");
        terminal.WriteLine("");

        terminal.SetColor("gray");
        terminal.WriteLine(Loc.Get("castle.armory_desc_1"));
        terminal.WriteLine(Loc.Get("castle.armory_desc_2"));
        terminal.SetColor("yellow");
        terminal.WriteLine(Loc.Get("castle.gold_label", currentPlayer.Gold.ToString("N0")));
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
        terminal.WriteLine(Loc.Get("castle.armory_header"));
        if (!IsScreenReader)
        {
            terminal.SetColor("darkgray");
            terminal.WriteLine("  ─────────────────────────────────────────────────────────");
        }

        if (IsScreenReader)
        {
            bool canBlade = currentPlayer.Gold >= crownBladePrice;
            bool canPlate = currentPlayer.Gold >= royalPlatePrice;
            bool canShield = currentPlayer.Gold >= crownShieldPrice;
            bool canRing = currentPlayer.Gold >= signetRingPrice;
            WriteSRMenuOption("1", $"Crown Blade - {crownBladePrice:N0}g - WeapPow {bladeWeapPow}", canBlade);
            WriteSRMenuOption("2", $"Royal Guard Plate - {royalPlatePrice:N0}g - ArmPow {plateArmPow}", canPlate);
            WriteSRMenuOption("3", $"Crown Shield - {crownShieldPrice:N0}g - ArmPow {shieldArmPow}", canShield);
            WriteSRMenuOption("4", $"Signet Ring - {signetRingPrice:N0}g - +5 CHA, +5 STR", canRing);
            WriteSRMenuOption("0", Loc.Get("ui.leave"));
        }
        else
        {
            string bladeColor = currentPlayer.Gold >= crownBladePrice ? "white" : "darkgray";
            terminal.SetColor("darkgray");
            terminal.Write("  [");
            terminal.SetColor("bright_yellow");
            terminal.Write("1");
            terminal.SetColor("darkgray");
            terminal.Write("] ");
            terminal.SetColor(bladeColor);
            terminal.WriteLine(Loc.Get("castle.crown_blade", crownBladePrice.ToString("N0").PadLeft(10), bladeWeapPow));

            string plateColor = currentPlayer.Gold >= royalPlatePrice ? "white" : "darkgray";
            terminal.SetColor("darkgray");
            terminal.Write("  [");
            terminal.SetColor("bright_yellow");
            terminal.Write("2");
            terminal.SetColor("darkgray");
            terminal.Write("] ");
            terminal.SetColor(plateColor);
            terminal.WriteLine(Loc.Get("castle.royal_plate", royalPlatePrice.ToString("N0").PadLeft(10), plateArmPow));

            string shieldColor = currentPlayer.Gold >= crownShieldPrice ? "white" : "darkgray";
            terminal.SetColor("darkgray");
            terminal.Write("  [");
            terminal.SetColor("bright_yellow");
            terminal.Write("3");
            terminal.SetColor("darkgray");
            terminal.Write("] ");
            terminal.SetColor(shieldColor);
            terminal.WriteLine(Loc.Get("castle.crown_shield", crownShieldPrice.ToString("N0").PadLeft(10), shieldArmPow));

            string ringColor = currentPlayer.Gold >= signetRingPrice ? "white" : "darkgray";
            terminal.SetColor("darkgray");
            terminal.Write("  [");
            terminal.SetColor("bright_yellow");
            terminal.Write("4");
            terminal.SetColor("darkgray");
            terminal.Write("] ");
            terminal.SetColor(ringColor);
            terminal.WriteLine(Loc.Get("castle.signet_ring", signetRingPrice.ToString("N0").PadLeft(10)));

            terminal.SetColor("darkgray");
            terminal.Write("  [");
            terminal.SetColor("bright_yellow");
            terminal.Write("0");
            terminal.SetColor("darkgray");
            terminal.Write("] ");
            terminal.SetColor("white");
            terminal.WriteLine(Loc.Get("ui.leave"));
        }
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
            terminal.WriteLine($"\n  {Loc.Get("castle.armory_cant_afford", itemName)}");
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
        terminal.WriteLine($"\n  {Loc.Get("castle.armory_presents", itemName)}");
        terminal.SetColor("yellow");
        terminal.WriteLine(Loc.Get("castle.armory_wear_honor"));
        terminal.WriteLine("");

        await terminal.PressAnyKey();
    }

    private async Task ShowCrownRecruitment()
    {
        var factionSystem = UsurperRemake.Systems.FactionSystem.Instance;

        terminal.ClearScreen();
        WriteBoxHeader(Loc.Get("castle.the_crown"), "bright_yellow");
        terminal.WriteLine("");

        terminal.SetColor("white");
        terminal.WriteLine(Loc.Get("castle.crown_herald_leads_1"));
        terminal.WriteLine(Loc.Get("castle.crown_herald_leads_2"));
        terminal.WriteLine("");
        await Task.Delay(1500);

        terminal.SetColor("bright_cyan");
        terminal.WriteLine(Loc.Get("castle.crown_chancellor_says_1"));
        terminal.WriteLine(Loc.Get("castle.crown_chancellor_says_2"));
        terminal.WriteLine("");
        await Task.Delay(1500);

        // Check if already in a faction
        if (factionSystem.PlayerFaction != null)
        {
            terminal.SetColor("yellow");
            terminal.WriteLine(Loc.Get("castle.crown_expression_hardens"));
            terminal.WriteLine("");
            terminal.SetColor("bright_cyan");
            terminal.WriteLine(Loc.Get("castle.crown_sworn_allegiance", UsurperRemake.Systems.FactionSystem.Factions[factionSystem.PlayerFaction.Value].Name));
            terminal.WriteLine(Loc.Get("castle.crown_divided_loyalties"));
            terminal.WriteLine(Loc.Get("castle.crown_renounce_return"));
            terminal.WriteLine("");
            await terminal.GetInputAsync(Loc.Get("ui.press_enter"));
            return;
        }

        terminal.SetColor("white");
        terminal.WriteLine(Loc.Get("castle.crown_tapestries"));
        terminal.WriteLine("");
        terminal.SetColor("bright_cyan");
        terminal.WriteLine(Loc.Get("castle.crown_order_justice"));
        terminal.WriteLine(Loc.Get("castle.crown_old_gods_fell"));
        terminal.WriteLine(Loc.Get("castle.crown_law_structure"));
        terminal.WriteLine("");
        await Task.Delay(2000);

        terminal.SetColor("cyan");
        terminal.WriteLine(Loc.Get("castle.crown_no_kneel"));
        terminal.WriteLine(Loc.Get("castle.crown_no_skulk"));
        terminal.WriteLine(Loc.Get("castle.crown_build_protect"));
        terminal.WriteLine("");
        await Task.Delay(1500);

        // Show faction benefits
        WriteSectionHeader(Loc.Get("castle.benefits_crown"), "bright_yellow");
        terminal.SetColor("white");
        terminal.WriteLine(Loc.Get("castle.crown_benefit_discount"));
        terminal.WriteLine(Loc.Get("castle.crown_benefit_guard"));
        terminal.WriteLine(Loc.Get("castle.crown_benefit_friendly"));
        terminal.WriteLine(Loc.Get("castle.crown_benefit_audience"));
        terminal.WriteLine("");

        // Check requirements
        var (canJoin, reason) = factionSystem.CanJoinFaction(UsurperRemake.Systems.Faction.TheCrown, currentPlayer);

        if (!canJoin)
        {
            WriteSectionHeader(Loc.Get("castle.requirements_not_met"), "red");
            terminal.SetColor("yellow");
            terminal.WriteLine(reason);
            terminal.WriteLine("");
            terminal.SetColor("gray");
            terminal.WriteLine(Loc.Get("castle.crown_requires"));
            terminal.WriteLine(Loc.Get("castle.crown_req_level"));
            terminal.WriteLine(Loc.Get("castle.crown_req_chivalry"));
            terminal.WriteLine(Loc.Get("castle.crown_req_darkness"));
            terminal.WriteLine(Loc.Get("castle.crown_your_chivalry", currentPlayer.Chivalry));
            terminal.WriteLine(Loc.Get("castle.crown_your_darkness", currentPlayer.Darkness));
            terminal.WriteLine("");
            terminal.SetColor("bright_cyan");
            terminal.WriteLine(Loc.Get("castle.crown_rep_precedes"));
            if (currentPlayer.Darkness > 500)
            {
                terminal.WriteLine(Loc.Get("castle.crown_no_criminals"));
            }
            else
            {
                terminal.WriteLine(Loc.Get("castle.crown_prove_worth"));
            }
            await terminal.GetInputAsync(Loc.Get("ui.press_enter"));
            return;
        }

        // Can join - offer the choice
        WriteSectionHeader(Loc.Get("castle.requirements_met"), "bright_green");
        terminal.SetColor("white");
        terminal.WriteLine(Loc.Get("castle.crown_extends_scroll"));
        terminal.WriteLine("");
        terminal.SetColor("bright_cyan");
        terminal.WriteLine(Loc.Get("castle.crown_record_speaks"));
        terminal.WriteLine(Loc.Get("castle.crown_swear_oath"));
        terminal.WriteLine("");
        terminal.SetColor("yellow");
        terminal.WriteLine(Loc.Get("castle.crown_warning"));
        terminal.WriteLine(Loc.Get("castle.crown_lock_out"));
        terminal.WriteLine(Loc.Get("castle.crown_decrease_standing"));
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
            terminal.WriteLine(Loc.Get("castle.crown_nods_curtly"));
            terminal.SetColor("bright_cyan");
            terminal.WriteLine(Loc.Get("castle.crown_wise_soul"));
            terminal.WriteLine(Loc.Get("castle.crown_gates_open"));
        }

        await terminal.GetInputAsync(Loc.Get("ui.press_enter"));
    }

    /// <summary>
    /// Perform the oath ceremony to join The Crown
    /// </summary>
    private async Task PerformCrownOath(UsurperRemake.Systems.FactionSystem factionSystem)
    {
        terminal.ClearScreen();
        WriteBoxHeader(Loc.Get("castle.oath_service"), "bright_yellow");
        terminal.WriteLine("");

        terminal.SetColor("white");
        terminal.WriteLine(Loc.Get("castle.oath_led_throne_1"));
        terminal.WriteLine(Loc.Get("castle.oath_led_throne_2"));
        terminal.WriteLine("");
        await Task.Delay(1500);

        terminal.SetColor("cyan");
        terminal.WriteLine(Loc.Get("castle.oath_kneel_seal"));
        terminal.WriteLine(Loc.Get("castle.oath_chancellor_stands"));
        terminal.WriteLine("");
        await Task.Delay(1500);

        terminal.SetColor("bright_cyan");
        terminal.WriteLine(Loc.Get("castle.oath_repeat"));
        terminal.WriteLine("");
        await Task.Delay(1000);

        terminal.SetColor("yellow");
        terminal.WriteLine(Loc.Get("castle.oath_line_1"));
        await Task.Delay(1200);
        terminal.WriteLine(Loc.Get("castle.oath_line_2"));
        await Task.Delay(1200);
        terminal.WriteLine(Loc.Get("castle.oath_line_3"));
        await Task.Delay(1200);
        terminal.WriteLine(Loc.Get("castle.oath_line_4"));
        terminal.WriteLine("");
        await Task.Delay(1500);

        terminal.SetColor("white");
        terminal.WriteLine(Loc.Get("castle.oath_seal_placed"));
        terminal.WriteLine(Loc.Get("castle.oath_trumpets"));
        terminal.WriteLine("");
        await Task.Delay(1500);

        // Actually join the faction
        factionSystem.JoinFaction(UsurperRemake.Systems.Faction.TheCrown, currentPlayer);

        WriteBoxHeader(Loc.Get("castle.joined_crown"), "bright_green");
        terminal.WriteLine("");

        terminal.SetColor("bright_cyan");
        terminal.WriteLine(Loc.Get("castle.oath_rise_servant"));
        terminal.WriteLine(Loc.Get("castle.oath_loyalty_rewarded"));
        terminal.WriteLine("");

        terminal.SetColor("white");
        terminal.WriteLine(Loc.Get("castle.oath_as_member"));
        terminal.SetColor("bright_green");
        terminal.WriteLine(Loc.Get("castle.oath_benefit_1"));
        terminal.WriteLine(Loc.Get("castle.oath_benefit_2"));
        terminal.WriteLine(Loc.Get("castle.oath_benefit_3"));
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
        WriteBoxHeader(Loc.Get("castle.castle_siege"), "bright_red");
        terminal.WriteLine("");

        var backend = SaveSystem.Instance?.Backend as SqlSaveBackend;
        if (backend == null)
        {
            terminal.SetColor("red");
            terminal.WriteLine(Loc.Get("castle.siege_no_backend"));
            await terminal.PressAnyKey();
            return;
        }

        // Must be on a team
        if (string.IsNullOrEmpty(currentPlayer.Team))
        {
            terminal.SetColor("yellow");
            terminal.WriteLine(Loc.Get("castle.siege_need_team"));
            terminal.WriteLine(Loc.Get("castle.siege_visit_team_corner"));
            await terminal.PressAnyKey();
            return;
        }

        // Must have a king to siege
        if (currentKing == null || !currentKing.IsActive)
        {
            terminal.SetColor("yellow");
            terminal.WriteLine(Loc.Get("castle.siege_no_king"));
            terminal.WriteLine(Loc.Get("castle.siege_use_claim"));
            await terminal.PressAnyKey();
            return;
        }

        // Can't siege your own throne
        if (currentKing.Name.Equals(currentPlayer.DisplayName, StringComparison.OrdinalIgnoreCase))
        {
            terminal.SetColor("yellow");
            terminal.WriteLine(Loc.Get("castle.siege_own_castle"));
            await terminal.PressAnyKey();
            return;
        }

        // 24h cooldown
        if (!backend.CanTeamSiege(currentPlayer.Team))
        {
            terminal.SetColor("yellow");
            terminal.WriteLine(Loc.Get("castle.siege_cooldown_1"));
            terminal.WriteLine(Loc.Get("castle.siege_cooldown_2"));
            await terminal.PressAnyKey();
            return;
        }

        // Minimum level
        if (currentPlayer.Level < GameConfig.MinLevelKing)
        {
            terminal.SetColor("yellow");
            terminal.WriteLine(Loc.Get("castle.siege_min_level", GameConfig.MinLevelKing));
            await terminal.PressAnyKey();
            return;
        }

        // Show siege info
        terminal.SetColor("white");
        terminal.WriteLine(Loc.Get("castle.siege_team_prepares", currentPlayer.Team));
        terminal.WriteLine("");

        int totalGuards = currentKing.TotalGuardCount;
        terminal.SetColor("cyan");
        terminal.WriteLine(Loc.Get("castle.siege_king", currentKing.GetTitle(), currentKing.Name));
        terminal.SetColor("red");
        terminal.WriteLine(Loc.Get("castle.siege_monster_guards", currentKing.MonsterGuards.Count));
        terminal.SetColor("yellow");
        terminal.WriteLine(Loc.Get("castle.siege_royal_guards", currentKing.Guards.Count));
        terminal.SetColor("white");
        terminal.WriteLine(Loc.Get("castle.siege_total_defenders", totalGuards));
        terminal.WriteLine("");

        // Load team members for the assault
        var teamMembers = await backend.GetPlayerTeamMembers(currentPlayer.Team, currentPlayer.DisplayName);
        terminal.SetColor("bright_cyan");
        terminal.WriteLine(Loc.Get("castle.siege_your_force"));
        terminal.SetColor("bright_green");
        terminal.WriteLine(Loc.Get("castle.siege_you", currentPlayer.DisplayName, currentPlayer.Level, currentPlayer.Class));
        foreach (var member in teamMembers)
        {
            terminal.SetColor("cyan");
            terminal.WriteLine(Loc.Get("castle.siege_member_entry", member.DisplayName, member.Level));
        }
        terminal.WriteLine("");

        terminal.SetColor("bright_red");
        terminal.Write(Loc.Get("castle.siege_launch_yn"));
        terminal.SetColor("white");
        string confirm = await terminal.ReadLineAsync();

        if (confirm?.ToUpper() != "Y")
        {
            terminal.SetColor("gray");
            terminal.WriteLine(Loc.Get("castle.siege_stands_down"));
            await Task.Delay(2000);
            return;
        }

        // Start siege in DB
        int siegeId = await backend.StartCastleSiege(currentPlayer.Team, Math.Max(1, totalGuards));

        terminal.ClearScreen();
        terminal.SetColor("bright_red");
        if (!IsScreenReader)
        {
            terminal.WriteLine("═══════════════════════════════════════════════════════════════");
            terminal.WriteLine(Loc.Get("castle.siege_begins"));
            terminal.WriteLine("═══════════════════════════════════════════════════════════════");
        }
        else
        {
            terminal.WriteLine(Loc.Get("castle.siege_begins_sr"));
        }
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
            terminal.WriteLine(Loc.Get("castle.siege_monster_blocks", monster.Name, monster.Level));
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
                terminal.WriteLine(Loc.Get("castle.team_strikes_monster", monster.Name, teamDmg, Math.Max(0, monsterHP)));

                if (monsterHP <= 0) break;

                // Monster retaliates
                long monsterDmg = Math.Max(1, monsterStr - teamDefense / memberCount);
                monsterDmg = (long)(monsterDmg * (0.8 + random.NextDouble() * 0.4));
                teamHP -= monsterDmg;

                terminal.SetColor("red");
                terminal.WriteLine(Loc.Get("castle.siege_monster_strikes", monster.Name, monsterDmg, Math.Max(0, teamHP)));

                await Task.Delay(250);
            }

            if (teamHP <= 0)
            {
                terminal.SetColor("red");
                terminal.WriteLine(Loc.Get("castle.siege_overwhelmed", monster.Name));
                siegeFailed = true;
                break;
            }
            else
            {
                guardsDefeated++;
                currentKing.MonsterGuards.Remove(monster);
                terminal.SetColor("bright_green");
                terminal.WriteLine(Loc.Get("castle.siege_monster_defeated", monster.Name));
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
                terminal.WriteLine(Loc.Get("castle.siege_guard_stands", guard.Name, guard.Loyalty));
                terminal.WriteLine("");

                // Low loyalty guards may surrender during siege
                if (guard.Loyalty < 25 && random.Next(100) < 40)
                {
                    terminal.SetColor("bright_yellow");
                    terminal.WriteLine(Loc.Get("castle.siege_guard_surrenders", guard.Name));
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
                    terminal.WriteLine(Loc.Get("castle.team_strikes_guard", guard.Name, teamDmg, Math.Max(0, guardHP)));

                    if (guardHP <= 0) break;

                    long guardDmg = Math.Max(1, guardStr - teamDefense / memberCount);
                    guardDmg = (long)(guardDmg * (0.8 + random.NextDouble() * 0.4));
                    teamHP -= guardDmg;

                    terminal.SetColor("red");
                    terminal.WriteLine(Loc.Get("castle.siege_guard_fights", guard.Name, guardDmg, Math.Max(0, teamHP)));

                    await Task.Delay(250);
                }

                if (teamHP <= 0)
                {
                    terminal.SetColor("red");
                    terminal.WriteLine(Loc.Get("castle.siege_defeated_by", guard.Name));
                    siegeFailed = true;
                    break;
                }
                else
                {
                    guardsDefeated++;
                    currentKing.Guards.Remove(guard);
                    terminal.SetColor("bright_green");
                    terminal.WriteLine(Loc.Get("castle.siege_guard_defeated", guard.Name));
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
            if (!IsScreenReader)
            {
                terminal.WriteLine("═══════════════════════════════════════════════════════════════");
                terminal.WriteLine(Loc.Get("castle.siege_failed"));
                terminal.WriteLine("═══════════════════════════════════════════════════════════════");
            }
            else
            {
                terminal.WriteLine(Loc.Get("castle.siege_failed_sr"));
            }
            terminal.WriteLine("");
            terminal.SetColor("yellow");
            terminal.WriteLine(Loc.Get("castle.siege_team_retreats"));
            terminal.SetColor("gray");
            terminal.WriteLine(Loc.Get("castle.siege_guards_defeated", guardsDefeated, Math.Max(1, totalGuards)));
            currentPlayer.HP = Math.Max(1, currentPlayer.HP / 2);
            terminal.SetColor("red");
            terminal.WriteLine(Loc.Get("castle.siege_lost_half_hp"));

            UsurperRemake.Systems.OnlineStateManager.Instance?.AddNews(
                $"{currentPlayer.Team} attempted to siege the castle but was repelled! ({guardsDefeated}/{totalGuards} guards defeated)",
                "siege");

            await terminal.PressAnyKey();
            return;
        }

        // PHASE 3: Challenge the King!
        terminal.ClearScreen();
        terminal.SetColor("bright_yellow");
        if (!IsScreenReader)
        {
            terminal.WriteLine("═══════════════════════════════════════════════════════════════");
            terminal.WriteLine(Loc.Get("castle.siege_guards_defeated"));
            terminal.WriteLine("═══════════════════════════════════════════════════════════════");
        }
        else
        {
            terminal.WriteLine(Loc.Get("castle.siege_guards_defeated_sr"));
        }
        terminal.WriteLine("");

        terminal.SetColor("white");
        terminal.WriteLine(Loc.Get("castle.siege_storms_throne", currentKing.GetTitle(), currentKing.Name));
        terminal.WriteLine(Loc.Get("castle.siege_draws_weapon"));
        terminal.WriteLine("");

        terminal.SetColor("cyan");
        terminal.WriteLine(Loc.Get("castle.siege_face_single"));
        terminal.SetColor("gray");
        terminal.WriteLine(Loc.Get("castle.siege_team_holds_back"));
        terminal.WriteLine("");

        // Apply damage taken during siege to player
        long hpLost = currentPlayer.HP - Math.Max(1, (long)(currentPlayer.HP * (teamHP / (double)Math.Max(1, currentPlayer.HP + teamMembers.Count * 100))));
        currentPlayer.HP = Math.Max(currentPlayer.HP / 2, currentPlayer.HP - hpLost); // At least keep 50% HP

        terminal.SetColor("yellow");
        terminal.WriteLine(Loc.Get("castle.siege_hp_after", currentPlayer.HP, currentPlayer.MaxHP));
        terminal.WriteLine("");

        terminal.SetColor("bright_red");
        terminal.Write(Loc.Get("castle.siege_face_king_yn"));
        terminal.SetColor("white");
        string faceKing = await terminal.ReadLineAsync();

        if (faceKing?.ToUpper() != "Y")
        {
            await backend.CompleteSiege(siegeId, "retreated");
            terminal.SetColor("gray");
            terminal.WriteLine(Loc.Get("castle.siege_declined_king"));
            terminal.WriteLine(Loc.Get("castle.siege_loyalists_rally"));

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
        terminal.WriteLine(Loc.Get("castle.siege_vs", currentKing.GetTitle(), currentKing.Name, currentPlayer.DisplayName));
        terminal.WriteLine("");
        terminal.SetColor("gray");
        terminal.WriteLine(Loc.Get("castle.siege_king_stats", siegeKingLevel, siegeKingStr, siegeKingDef, kingHP));
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
            terminal.WriteLine(Loc.Get("castle.siege_strike_king", currentKing.Name, playerDamage, Math.Max(0, kingHP)));

            if (kingHP <= 0) break;

            // King attacks (accounts for weapon and armor power)
            long kingDamage = Math.Max(1, siegeKingStr + siegeKingWeapPow - currentPlayer.Defence - currentPlayer.ArmPow);
            kingDamage = (long)(kingDamage * (0.8 + random.NextDouble() * 0.4));
            playerHP -= kingDamage;

            terminal.SetColor("red");
            terminal.WriteLine(Loc.Get("castle.siege_king_strikes", currentKing.Name, kingDamage, Math.Max(0, playerHP)));

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
                terminal.WriteLine(Loc.Get("castle.siege_king_defeated_you", currentKing.Name));
            }
            else
            {
                terminal.WriteLine(Loc.Get("castle.siege_stalemate", currentKing.Name));
            }
            terminal.WriteLine(Loc.Get("castle.siege_failed_final"));
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
        WriteBoxHeader(Loc.Get("castle.siege_victory"), "bright_yellow");
        terminal.WriteLine("");

        terminal.SetColor("white");
        terminal.WriteLine(Loc.Get("castle.siege_king_falls", currentKing.Name));
        terminal.WriteLine(Loc.Get("castle.siege_crown_tumbles"));
        terminal.WriteLine("");
        await Task.Delay(2000);

        // Must leave team to become king
        string siegeTeam = currentPlayer.Team;
        CityControlSystem.Instance.ForceLeaveTeam(currentPlayer);
        GameEngine.Instance?.ClearDungeonParty();

        terminal.SetColor("yellow");
        terminal.WriteLine(Loc.Get("castle.siege_leave_team", siegeTeam));
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

        // Crown new monarch — inherit the previous king's treasury, orphans, and prisoners
        long inheritedTreasury = currentKing.Treasury;
        var inheritedOrphans = currentKing?.Orphans?.ToList();
        var inheritedPrisoners = currentKing?.Prisoners?.ToDictionary(p => p.Key, p => p.Value);
        bool oldKingWasHuman = currentKing.AI == CharacterAI.Human;
        ClearRoyalMarriage(currentKing); // Clear old king's royal spouse before replacing
        currentPlayer.King = true;
        currentPlayer.NobleTitle = currentPlayer.Sex == CharacterSex.Female ? "Queen" : "King";
        currentKing = King.CreateNewKing(currentPlayer.DisplayName, CharacterAI.Human, currentPlayer.Sex, inheritedOrphans);
        currentKing.Treasury = inheritedTreasury;
        if (inheritedPrisoners != null && inheritedPrisoners.Count > 0)
        {
            foreach (var p in inheritedPrisoners)
                currentKing.Prisoners[p.Key] = p.Value;
        }
        playerIsKing = true;
        currentPlayer.PKills++;
        UsurperRemake.Systems.ArchetypeTracker.Instance.RecordBecameKing();

        currentPlayer.HP = playerHP;

        terminal.SetColor("bright_yellow");
        terminal.WriteLine(Loc.Get("castle.siege_all_hail", currentPlayer.DisplayName.ToUpper()));
        terminal.WriteLine("");
        terminal.SetColor("cyan");
        terminal.WriteLine(Loc.Get("castle.siege_remembered"));

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
