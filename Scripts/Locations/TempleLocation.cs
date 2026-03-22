using UsurperRemake.Utils;
using UsurperRemake.Systems;
using UsurperRemake.BBS;
using UsurperRemake.Data;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;

/// <summary>
/// Temple of the Gods - Complete Pascal-compatible temple system
/// Based on TEMPLE.PAS with worship, sacrifice, and divine services
/// Integrated with Phase 13 God System and Old Gods storyline
///
/// The Temple houses altars to mortal-created gods as well as whispers
/// of the Old Gods - the corrupted divine beings who once guided humanity.
/// Aurelion, The Fading Light, can be encountered in the Deep Temple.
/// </summary>
public partial class TempleLocation : BaseLocation
{
    private readonly LocationManager locationManager;
    private readonly GodSystem godSystem;
    private bool refreshMenu = true;
    private Random random = Random.Shared;

    // Old Gods integration
    private static readonly string[] OldGodsProphecies = new[]
    {
        "The Broken Blade weeps in halls of bone...",
        "Love withers where the heart turns cold...",
        "Justice blind becomes tyranny's tool...",
        "In shadow's web, truth and lies entwine...",
        "The light that fades leaves only dark behind...",
        "Mountains sleep while the world forgets...",
        "The Weary Creator watches, waits, and wonders..."
    };

    private static readonly string[] DivineWhispers = new[]
    {
        "You hear whispers from beyond the veil...",
        "The candles flicker as something ancient stirs...",
        "A cold wind carries the scent of forgotten ages...",
        "The stones remember when gods walked among mortals...",
        "Prayers echo back, transformed into warnings...",
        "The altar trembles with barely contained power..."
    };
    
    public TempleLocation(TerminalEmulator terminal, LocationManager locationManager, GodSystem godSystem)
    {
        this.terminal = terminal;  // sets base class protected field
        this.locationManager = locationManager;
        this.godSystem = godSystem;
        
        LocationName = "Temple of the Gods";
        LocationId = GameLocation.Temple;
        Description = "The Temple area is crowded with monks, preachers and processions of priests on their way to the altars. The doomsday prophets are trying to get your attention.";
    }
    
    // Parameterless constructor for legacy compatibility
    public TempleLocation() : this(TerminalEmulator.Instance ?? new TerminalEmulator(), LocationManager.Instance, UsurperRemake.GodSystemSingleton.Instance)
    {
    }

    /// <summary>
    /// Override EnterLocation to use our custom temple loop
    /// </summary>
    public override async Task EnterLocation(Character player, TerminalEmulator term)
    {
        // Set base class fields so helper methods (WriteBoxHeader, IsScreenReader, etc.) work
        currentPlayer = player;
        if (term != null)
            terminal = term;

        // Run the temple's custom processing loop
        var destination = await ProcessLocation(player);

        // Always throw to navigate back (MainStreet is the default)
        throw new LocationExitException(GameLocation.MainStreet);
    }

    /// <summary>
    /// Main temple processing loop based on Pascal TEMPLE.PAS
    /// </summary>
    public async Task<string> ProcessLocation(Character player)
    {
        currentPlayer = player;
        terminal.ClearScreen();
        
        await DisplayWelcomeMessage();
        await VerifyPlayerGodExists();
        
        bool exitLocation = false;
        refreshMenu = true;
        
        while (!exitLocation)
        {
            try
            {
                await DisplayMenu(refreshMenu);
                refreshMenu = false;
                
                var choice = await terminal.GetInputAsync(Loc.Get("ui.your_choice"));

                // Handle global quick commands (/health, /bug, etc.)
                var (handled, shouldExit) = await TryProcessGlobalCommand(choice);
                if (handled) { refreshMenu = true; continue; }

                switch (choice.ToUpper())
                {
                    case "?":
                        refreshMenu = true;
                        continue;
                        
                    case GameConfig.TempleMenuWorship: // "W"
                        await ProcessWorship();
                        break;
                        
                    case GameConfig.TempleMenuDesecrate: // "D"
                        await ProcessDesecrateAltar();
                        break;
                        
                    case GameConfig.TempleMenuAltars: // "A"
                        await DisplayAltars();
                        break;
                        
                    case GameConfig.TempleMenuContribute: // "C"
                        await ProcessContribute();
                        break;
                        
                    case GameConfig.TempleMenuStatus: // "S"
                        await DisplayPlayerStatus();
                        break;
                        
                    case GameConfig.TempleMenuGodRanking: // "G"
                        await DisplayGodRanking();
                        break;
                        
                    case GameConfig.TempleMenuHolyNews: // "H"
                        await DisplayHolyNews();
                        break;

                    case "P": // Prophecies of the Old Gods
                        await DisplayOldGodsProphecies();
                        break;

                    case "Y": // Daily prayer
                        await ProcessDailyPrayer();
                        break;

                    case "I": // Item sacrifice
                        await ProcessItemSacrifice();
                        break;

                    case "T": // Deep Temple (Aurelion encounter)
                        await EnterDeepTemple();
                        break;

                    case "E": // Examine ancient stones (Seal of Creation)
                        await ExamineAncientStones();
                        break;

                    case "M": // Meditation Chapel (Mira companion recruitment)
                        await VisitMeditationChapel();
                        break;

                    case "F": // The Faith faction recruitment
                        await ShowFaithRecruitment();
                        break;

                    case "N": // Inner Sanctum (Faith only)
                        await VisitInnerSanctum();
                        break;

                    case "J": // Join an immortal god's flock
                        await WorshipImmortalGod();
                        refreshMenu = true;
                        break;

                    case "$": // Sacrifice gold to immortal god
                        await SacrificeToImmortalGod();
                        break;

                    case "L": // Leave immortal god's faith
                        await LeaveImmortalFaith();
                        refreshMenu = true;
                        break;

                    case GameConfig.TempleMenuReturn: // "R"
                        exitLocation = true;
                        break;
                        
                    default:
                        terminal.WriteLine(Loc.Get("temple.invalid_choice"), "red");
                        await Task.Delay(1000);
                        break;
                }
            }
            catch (LocationChangeException ex)
            {
                return ex.NewLocation;
            }
        }
        
        return GameLocation.MainStreet.ToString();
    }
    
    /// <summary>
    /// Display temple welcome message (Pascal TEMPLE.PAS)
    /// </summary>
    private async Task DisplayWelcomeMessage()
    {
        terminal.WriteLine("");
        terminal.WriteLine(Loc.Get("temple.enter_area"), "yellow");
        terminal.WriteLine("");
        
        string playerGod = godSystem.GetPlayerGod(currentPlayer.Name2);
        if (!string.IsNullOrEmpty(playerGod))
        {
            terminal.WriteLine(Loc.Get("temple.worship_god", playerGod), "cyan");
        }
        else if (!string.IsNullOrEmpty(currentPlayer.WorshippedGod))
        {
            terminal.WriteLine(Loc.Get("temple.follow_immortal", currentPlayer.WorshippedGod), "bright_yellow");
        }
        else
        {
            terminal.WriteLine(Loc.Get("temple.not_believer"), "gray");
        }

        await Task.Delay(1500);
    }
    
    /// <summary>
    /// Display temple menu (Pascal TEMPLE.PAS Meny procedure)
    /// </summary>
    private async Task DisplayMenu(bool forceDisplay)
    {
        if (!forceDisplay && currentPlayer.Expert) return;
        
        terminal.ClearScreen();

        // Temple header - standardized format
        WriteBoxHeader(Loc.Get("temple.header_visual"), "bright_cyan");
        terminal.WriteLine("");

        terminal.SetColor("white");
        terminal.WriteLine(Loc.Get("temple.description_line1"));
        terminal.WriteLine(Loc.Get("temple.description_line2"));
        terminal.WriteLine(Loc.Get("temple.description_line3"));

        // Hint at ancient stones if seal not collected
        var storyForHint = StoryProgressionSystem.Instance;
        if (!storyForHint.CollectedSeals.Contains(UsurperRemake.Systems.SealType.Creation))
        {
            terminal.WriteLine("");
            terminal.SetColor("gray");
            terminal.WriteLine(Loc.Get("temple.ancient_stones_hint1"));
            terminal.WriteLine(Loc.Get("temple.ancient_stones_hint2"));
        }

        terminal.WriteLine("");

        string playerGod = godSystem.GetPlayerGod(currentPlayer.Name2);
        if (!string.IsNullOrEmpty(playerGod))
        {
            terminal.WriteLine(Loc.Get("temple.worship_god", playerGod), "cyan");
        }
        else if (!string.IsNullOrEmpty(currentPlayer.WorshippedGod))
        {
            terminal.WriteLine(Loc.Get("temple.follow_immortal", currentPlayer.WorshippedGod), "bright_yellow");
        }
        else
        {
            terminal.WriteLine(Loc.Get("temple.not_believer"), "gray");
        }
        terminal.WriteLine("");
        
        // Main menu options
        string prayerGod = godSystem.GetPlayerGod(currentPlayer.Name2);
        var story = StoryProgressionSystem.Instance;
        var factionSystem = UsurperRemake.Systems.FactionSystem.Instance;
        var immortalGods = await GetImmortalGodsAsync();

        if (IsScreenReader)
        {
            terminal.WriteLine(Loc.Get("temple.services"));
            terminal.WriteLine("");
            terminal.WriteLine(Loc.Get("temple.sr_worship"));
            terminal.WriteLine(Loc.Get("temple.sr_desecrate"));
            terminal.WriteLine(Loc.Get("temple.sr_holy_news"));
            terminal.WriteLine(Loc.Get("temple.sr_altars"));
            terminal.WriteLine(Loc.Get("temple.sr_contribute"));
            terminal.WriteLine(Loc.Get("temple.sr_item_sacrifice"));
            terminal.WriteLine(Loc.Get("temple.sr_status"));
            terminal.WriteLine(Loc.Get("temple.sr_god_ranking"));
            terminal.WriteLine(Loc.Get("temple.sr_prophecies"));

            if (!string.IsNullOrEmpty(prayerGod))
            {
                bool canPray = UsurperRemake.Systems.DivineBlessingSystem.Instance.CanPrayToday(currentPlayer.Name2);
                if (canPray)
                    terminal.WriteLine(Loc.Get("temple.sr_pray"));
                else
                    terminal.WriteLine(Loc.Get("temple.sr_prayed_today"));
            }

            if (!story.CollectedSeals.Contains(UsurperRemake.Systems.SealType.Creation))
                terminal.WriteLine(Loc.Get("temple.sr_examine_stones"));

            if (CanEnterDeepTemple())
                terminal.WriteLine(Loc.Get("temple.sr_deep_temple"));

            if (CanMeetMira())
                terminal.WriteLine(Loc.Get("temple.sr_meditation_chapel"));

            if (factionSystem.PlayerFaction != UsurperRemake.Systems.Faction.TheFaith)
            {
                if (factionSystem.PlayerFaction == null)
                    terminal.WriteLine(Loc.Get("temple.sr_faith_seek"));
                else
                    terminal.WriteLine(Loc.Get("temple.sr_faith_serve_another"));
            }
            else
            {
                terminal.WriteLine(Loc.Get("temple.sr_faith_member"));
            }

            if (FactionSystem.Instance?.HasTempleAccess() == true)
            {
                bool meditatedToday;
                if (DoorMode.IsOnlineMode)
                {
                    var boundary = DailySystemManager.GetCurrentResetBoundary();
                    meditatedToday = currentPlayer.LastInnerSanctumRealDate >= boundary;
                }
                else
                {
                    int today = DailySystemManager.Instance?.CurrentDay ?? 0;
                    meditatedToday = currentPlayer.InnerSanctumLastDay >= today;
                }
                if (meditatedToday)
                    terminal.WriteLine(Loc.Get("temple.sr_inner_sanctum_meditated"));
                else
                    terminal.WriteLine(Loc.Get("temple.sr_inner_sanctum_cost", GameConfig.InnerSanctumCost));
            }

            if (immortalGods.Count > 0)
            {
                terminal.WriteLine("");
                terminal.WriteLine(Loc.Get("temple.sr_ascended_gods"));
                if (!string.IsNullOrEmpty(currentPlayer.WorshippedGod))
                    terminal.WriteLine(Loc.Get("temple.sr_join_immortal_following", currentPlayer.WorshippedGod));
                else
                    terminal.WriteLine(Loc.Get("temple.sr_join_immortal_unaffiliated"));

                if (!string.IsNullOrEmpty(currentPlayer.WorshippedGod))
                {
                    terminal.WriteLine(Loc.Get("temple.sr_sacrifice_gold"));
                    terminal.WriteLine(Loc.Get("temple.sr_leave_immortal"));
                }
            }

            terminal.WriteLine(Loc.Get("temple.sr_return"));
            terminal.WriteLine("");
        }
        else
        {
            terminal.SetColor("cyan");
            terminal.WriteLine(Loc.Get("temple.services"));
            terminal.WriteLine("");

            // Row 1
            terminal.SetColor("darkgray");
            terminal.Write(" [");
            terminal.SetColor("bright_yellow");
            terminal.Write("W");
            terminal.SetColor("darkgray");
            terminal.Write("]");
            terminal.SetColor("white");
            terminal.Write(Loc.Get("temple.menu_worship"));

            terminal.SetColor("darkgray");
            terminal.Write("[");
            terminal.SetColor("bright_yellow");
            terminal.Write("D");
            terminal.SetColor("darkgray");
            terminal.Write("]");
            terminal.SetColor("white");
            terminal.Write(Loc.Get("temple.menu_desecrate"));

            terminal.SetColor("darkgray");
            terminal.Write("[");
            terminal.SetColor("bright_yellow");
            terminal.Write("H");
            terminal.SetColor("darkgray");
            terminal.Write("]");
            terminal.SetColor("white");
            terminal.WriteLine(Loc.Get("temple.menu_holy_news"));

            // Row 2
            terminal.SetColor("darkgray");
            terminal.Write(" [");
            terminal.SetColor("bright_yellow");
            terminal.Write("A");
            terminal.SetColor("darkgray");
            terminal.Write("]");
            terminal.SetColor("white");
            terminal.Write(Loc.Get("temple.menu_altars"));

            terminal.SetColor("darkgray");
            terminal.Write("[");
            terminal.SetColor("bright_yellow");
            terminal.Write("C");
            terminal.SetColor("darkgray");
            terminal.Write("]");
            terminal.SetColor("white");
            terminal.Write(Loc.Get("temple.menu_contribute"));

            terminal.SetColor("darkgray");
            terminal.Write("[");
            terminal.SetColor("bright_yellow");
            terminal.Write("I");
            terminal.SetColor("darkgray");
            terminal.Write("]");
            terminal.SetColor("white");
            terminal.WriteLine(Loc.Get("temple.menu_item_sacrifice"));

            // Row 3
            terminal.SetColor("darkgray");
            terminal.Write(" [");
            terminal.SetColor("bright_yellow");
            terminal.Write("S");
            terminal.SetColor("darkgray");
            terminal.Write("]");
            terminal.SetColor("white");
            terminal.Write(Loc.Get("temple.menu_status"));

            terminal.SetColor("darkgray");
            terminal.Write("[");
            terminal.SetColor("bright_yellow");
            terminal.Write("G");
            terminal.SetColor("darkgray");
            terminal.Write("]");
            terminal.SetColor("white");
            terminal.Write(Loc.Get("temple.menu_god_ranking"));

            terminal.SetColor("darkgray");
            terminal.Write("[");
            terminal.SetColor("bright_yellow");
            terminal.Write("P");
            terminal.SetColor("darkgray");
            terminal.Write("]");
            terminal.SetColor("white");
            terminal.Write(Loc.Get("temple.menu_prophecies"));

            // Daily prayer option - show if player worships a god
            if (!string.IsNullOrEmpty(prayerGod))
            {
                bool canPray = UsurperRemake.Systems.DivineBlessingSystem.Instance.CanPrayToday(currentPlayer.Name2);
                if (canPray)
                {
                    terminal.SetColor("darkgray");
                    terminal.Write("[");
                    terminal.SetColor("bright_yellow");
                    terminal.Write("Y");
                    terminal.SetColor("darkgray");
                    terminal.Write("]");
                    terminal.SetColor("bright_green");
                    terminal.WriteLine(Loc.Get("temple.menu_pray"));
                }
                else
                {
                    terminal.SetColor("gray");
                    terminal.WriteLine(Loc.Get("temple.menu_prayed_today"));
                }
            }
            else
            {
                terminal.WriteLine("");
            }

            // Ancient stones option - only show if Seal of Creation not collected
            if (!story.CollectedSeals.Contains(UsurperRemake.Systems.SealType.Creation))
            {
                terminal.SetColor("darkgray");
                terminal.Write(" [");
                terminal.SetColor("bright_yellow");
                terminal.Write("E");
                terminal.SetColor("darkgray");
                terminal.Write("]");
                terminal.SetColor("white");
                terminal.Write(Loc.Get("temple.menu_examine_stones"));
            }
            else
            {
                terminal.Write("                       ");
            }

            // Deep Temple option - only show if player meets requirements
            if (CanEnterDeepTemple())
            {
                terminal.SetColor("darkgray");
                terminal.Write("[");
                terminal.SetColor("bright_yellow");
                terminal.Write("T");
                terminal.SetColor("darkgray");
                terminal.Write("]");
                terminal.SetColor("bright_magenta");
                terminal.WriteLine(Loc.Get("temple.menu_deep_temple"));
            }
            else
            {
                terminal.WriteLine("");
            }

            // Mira companion option - only show if she can be recruited
            if (CanMeetMira())
            {
                terminal.SetColor("darkgray");
                terminal.Write(" [");
                terminal.SetColor("bright_yellow");
                terminal.Write("M");
                terminal.SetColor("darkgray");
                terminal.Write("]");
                terminal.SetColor("bright_green");
                terminal.Write(Loc.Get("temple.menu_meditation_chapel"));
                terminal.SetColor("gray");
                terminal.WriteLine(Loc.Get("temple.menu_meditation_hint"));
            }

            // The Faith faction option - only show if player isn't already a member
            if (factionSystem.PlayerFaction != UsurperRemake.Systems.Faction.TheFaith)
            {
                terminal.SetColor("darkgray");
                terminal.Write(" [");
                terminal.SetColor("bright_yellow");
                terminal.Write("F");
                terminal.SetColor("darkgray");
                terminal.Write("]");
                terminal.SetColor("bright_yellow");
                terminal.Write(Loc.Get("temple.menu_the_faith"));
                if (factionSystem.PlayerFaction == null)
                {
                    terminal.SetColor("gray");
                    terminal.WriteLine(Loc.Get("temple.menu_faith_seek"));
                }
                else
                {
                    terminal.SetColor("dark_red");
                    terminal.WriteLine(Loc.Get("temple.menu_faith_serve_another"));
                }
            }
            else
            {
                terminal.SetColor("bright_green");
                terminal.WriteLine(Loc.Get("temple.menu_faith_member"));
            }

            // Inner Sanctum (Faith only)
            if (FactionSystem.Instance?.HasTempleAccess() == true)
            {
                terminal.SetColor("darkgray");
                terminal.Write(" [");
                terminal.SetColor("bright_yellow");
                terminal.Write("N");
                terminal.SetColor("darkgray");
                terminal.Write("]");
                terminal.SetColor("cyan");
                terminal.Write(Loc.Get("temple.menu_inner_sanctum"));
                bool meditatedToday;
                if (DoorMode.IsOnlineMode)
                {
                    var boundary = DailySystemManager.GetCurrentResetBoundary();
                    meditatedToday = currentPlayer.LastInnerSanctumRealDate >= boundary;
                }
                else
                {
                    int today = DailySystemManager.Instance?.CurrentDay ?? 0;
                    meditatedToday = currentPlayer.InnerSanctumLastDay >= today;
                }
                if (meditatedToday)
                {
                    terminal.SetColor("gray");
                    terminal.WriteLine(Loc.Get("temple.menu_meditated_today"));
                }
                else
                {
                    terminal.SetColor("bright_green");
                    terminal.WriteLine(Loc.Get("temple.menu_inner_sanctum_cost", GameConfig.InnerSanctumCost));
                }
            }

            // Immortal Worship section — only show if any ascended gods exist
            if (immortalGods.Count > 0)
            {
                terminal.WriteLine("");
                WriteSectionHeader(Loc.Get("temple.ascended_gods"), "bright_yellow");

                terminal.SetColor("darkgray");
                terminal.Write(" [");
                terminal.SetColor("bright_yellow");
                terminal.Write("J");
                terminal.SetColor("darkgray");
                terminal.Write("]");
                terminal.SetColor("white");
                terminal.Write(Loc.Get("temple.menu_join_flock"));
                if (!string.IsNullOrEmpty(currentPlayer.WorshippedGod))
                {
                    terminal.SetColor("bright_green");
                    terminal.WriteLine(Loc.Get("temple.menu_following", currentPlayer.WorshippedGod));
                }
                else
                {
                    terminal.SetColor("gray");
                    terminal.WriteLine(Loc.Get("temple.menu_unaffiliated"));
                }

                if (!string.IsNullOrEmpty(currentPlayer.WorshippedGod))
                {
                    terminal.SetColor("darkgray");
                    terminal.Write(" [");
                    terminal.SetColor("bright_yellow");
                    terminal.Write("$");
                    terminal.SetColor("darkgray");
                    terminal.Write("]");
                    terminal.SetColor("white");
                    terminal.Write(Loc.Get("temple.menu_sacrifice_gold"));
                    terminal.SetColor("gray");
                    terminal.WriteLine(Loc.Get("temple.menu_sacrifice_gold_hint"));

                    terminal.SetColor("darkgray");
                    terminal.Write(" [");
                    terminal.SetColor("bright_yellow");
                    terminal.Write("L");
                    terminal.SetColor("darkgray");
                    terminal.Write("]");
                    terminal.SetColor("white");
                    terminal.WriteLine(Loc.Get("temple.menu_leave_faith"));
                }
            }

            terminal.SetColor("darkgray");
            terminal.Write(" [");
            terminal.SetColor("bright_yellow");
            terminal.Write("R");
            terminal.SetColor("darkgray");
            terminal.Write("]");
            terminal.SetColor("white");
            terminal.WriteLine(Loc.Get("temple.menu_return"));
            terminal.WriteLine("");
        }
    }

    /// <summary>
    /// Process worship selection and god faith (Pascal TEMPLE.PAS)
    /// </summary>
    private async Task ProcessWorship()
    {
        terminal.WriteLine("");
        terminal.WriteLine("");
        
        string currentGod = godSystem.GetPlayerGod(currentPlayer.Name2);
        bool goAhead = true;

        // Also check if following an immortal player-god
        if (string.IsNullOrEmpty(currentGod) && !string.IsNullOrEmpty(currentPlayer.WorshippedGod))
        {
            terminal.WriteLine(Loc.Get("temple.follow_immortal_currently", currentPlayer.WorshippedGod), "bright_yellow");
            var choice = await terminal.GetInputAsync(Loc.Get("temple.abandon_for_elder", currentPlayer.WorshippedGod));
            if (choice.ToUpper() == "Y")
            {
                string oldGod = currentPlayer.WorshippedGod;
                currentPlayer.WorshippedGod = "";

                // Persist to DB
                if (DoorMode.IsOnlineMode)
                {
                    try
                    {
                        var backend = SaveSystem.Instance?.Backend as SqlSaveBackend;
                        var sessionUsername = UsurperRemake.Server.SessionContext.Current?.Username;
                        if (backend != null && !string.IsNullOrEmpty(sessionUsername))
                            await backend.SetPlayerWorshippedGod(sessionUsername, "");
                    }
                    catch { }
                }

                terminal.WriteLine("");
                terminal.SetColor("yellow");
                terminal.WriteLine(Loc.Get("temple.renounce_immortal", oldGod));
            }
            else
            {
                terminal.WriteLine(Loc.Get("temple.remain_faithful"), "green");
                goAhead = false;
            }
        }
        else if (!string.IsNullOrEmpty(currentGod))
        {
            terminal.WriteLine(Loc.Get("temple.currently_worship", currentGod), "white");

            var choice = await terminal.GetInputAsync(Loc.Get("temple.lost_faith", currentGod));
            if (choice.ToUpper() == "Y")
            {
                // Abandon faith
                terminal.WriteLine("");
                terminal.WriteLine(Loc.Get("temple.dont_believe", currentGod), "white");
                terminal.WriteLine(Loc.Get("temple.powers_diminish", currentGod), "yellow");

                var noteChoice = await terminal.GetInputAsync(Loc.Get("temple.send_note", currentGod));
                string note = "";
                if (noteChoice.ToUpper() == "Y")
                {
                    note = await terminal.GetInputAsync(Loc.Get("temple.note_prompt"));
                    terminal.WriteLine(Loc.Get("temple.done"), "green");
                }

                if (string.IsNullOrEmpty(note))
                {
                    var randomNotes = new[]
                    {
                        "You are not my God!",
                        "farewell..",
                        "never again will I follow you!"
                    };
                    note = randomNotes[Random.Shared.Next(randomNotes.Length)];
                }

                // Remove from god system
                godSystem.SetPlayerGod(currentPlayer.Name2, "");

                // In Pascal, this would send mail to the god and news
                terminal.WriteLine("");
                terminal.WriteLine(Loc.Get("temple.no_longer_believer"), "yellow");
            }
            else
            {
                terminal.WriteLine(Loc.Get("temple.gods_dont_like_apostates"), "green");
                goAhead = false;
            }
        }

        if (goAhead)
        {
            var selectedGod = await SelectGod(Loc.Get("temple.choose_god_worship"));

            if (selectedGod != null)
            {
                terminal.WriteLine("");
                terminal.WriteLine(Loc.Get("temple.raise_hands_pray", selectedGod.Name), "white");
                terminal.Write(Loc.Get("temple.for_forgiveness"), "white");

                // Delay dots animation (Pascal Make_Delay_Dots)
                for (int i = 0; i < 15; i++)
                {
                    terminal.Write(".", "white");
                    await Task.Delay(300);
                }
                terminal.WriteLine("");

                terminal.WriteLine(Loc.Get("temple.now_believer", selectedGod.Name), "yellow");

                // Set in god system
                godSystem.SetPlayerGod(currentPlayer.Name2, selectedGod.Name);

                // Clear any immortal player-god worship (can only follow one type)
                if (!string.IsNullOrEmpty(currentPlayer.WorshippedGod))
                {
                    terminal.SetColor("yellow");
                    terminal.WriteLine(Loc.Get("temple.bond_severed", currentPlayer.WorshippedGod));
                    currentPlayer.WorshippedGod = "";
                    if (DoorMode.IsOnlineMode)
                    {
                        try
                        {
                            var backend2 = SaveSystem.Instance?.Backend as SqlSaveBackend;
                            var sessionUsername2 = UsurperRemake.Server.SessionContext.Current?.Username;
                            if (backend2 != null && !string.IsNullOrEmpty(sessionUsername2))
                                await backend2.SetPlayerWorshippedGod(sessionUsername2, "");
                        }
                        catch { }
                    }
                }

                // In Pascal, this would send mail to god and news
                terminal.WriteLine("");
                terminal.WriteLine(Loc.Get("temple.gods_smile"), "cyan");
            }
        }

        await Task.Delay(2000);
    }
    
    /// <summary>
    /// Process altar desecration (Pascal TEMPLE.PAS)
    /// </summary>
    private async Task ProcessDesecrateAltar()
    {
        terminal.WriteLine("");
        terminal.WriteLine("");
        
        if (currentPlayer.DesecrationsToday >= 2)
        {
            terminal.WriteLine(Loc.Get("temple.desecration_limit"), "red");
            terminal.WriteLine(Loc.Get("temple.wait_tomorrow"), "gray");
            await Task.Delay(2000);
            return;
        }

        if (currentPlayer.DarkNr < 1)
        {
            terminal.WriteLine(Loc.Get("temple.no_evil_deeds"), "red");
            await Task.Delay(2000);
            return;
        }

        var choice = await terminal.GetInputAsync(Loc.Get("temple.upset_gods"));
        if (choice.ToUpper() != "Y")
        {
            terminal.WriteLine(Loc.Get("temple.good_for_you"), "green");
            await Task.Delay(1000);
            return;
        }
        
        var selectedGod = await SelectGod(Loc.Get("temple.select_god_desecrate"), requireConfirmation: false);
        if (selectedGod == null) return;

        string playerGod = godSystem.GetPlayerGod(currentPlayer.Name2);
        if (!string.IsNullOrEmpty(playerGod) && playerGod == selectedGod.Name)
        {
            terminal.WriteLine("");
            terminal.WriteLine(Loc.Get("temple.not_allowed_abuse_own"), "red");
            await Task.Delay(2000);
            return;
        }

        terminal.SetColor("red");
        var confirmChoice = await terminal.GetInputAsync(Loc.Get("temple.confirm_desecrate", selectedGod.Name));
        if (confirmChoice.ToUpper() != "Y")
        {
            terminal.WriteLine(Loc.Get("temple.wise_choice"), "gray");
            return;
        }

        await PerformEnhancedDesecration(selectedGod);
    }
    
    /// <summary>
    /// Process contribution/sacrifice to gods (Pascal TEMPLE.PAS contribute_to_god)
    /// </summary>
    private async Task ProcessContribute()
    {
        terminal.WriteLine("");
        terminal.WriteLine("");
        
        if (currentPlayer.ChivNr < 1)
        {
            terminal.WriteLine(Loc.Get("temple.no_good_deeds"), "red");
            await Task.Delay(2000);
            return;
        }

        var selectedGod = await SelectGod(Loc.Get("temple.who_receive_gift"), requireConfirmation: false);
        if (selectedGod == null) return;

        string playerGod = godSystem.GetPlayerGod(currentPlayer.Name2);
        bool wrongGod = false;
        bool goAhead = true;
        
        if (!string.IsNullOrEmpty(playerGod) && playerGod != selectedGod.Name)
        {
            terminal.WriteLine("");
            terminal.WriteLine(Loc.Get("temple.not_your_god", selectedGod.Name), "red");
            terminal.WriteLine(Loc.Get("temple.mighty_not_happy", playerGod), "red");

            var choice = await terminal.GetInputAsync(Loc.Get("temple.continue_prompt"));
            if (choice.ToUpper() == "Y")
            {
                wrongGod = true;
            }
            else
            {
                terminal.WriteLine(Loc.Get("temple.good_for_you"), "green");
                goAhead = false;
            }
        }
        
        if (goAhead)
        {
            await ProcessSacrificeMenu(selectedGod, wrongGod);
        }
        
        await Task.Delay(1000);
    }
    
    /// <summary>
    /// Display all altars (Pascal TEMPLE.PAS)
    /// </summary>
    private async Task DisplayAltars()
    {
        terminal.WriteLine("");
        terminal.WriteLine("");
        WriteSectionHeader(Loc.Get("temple.altars"), "magenta");
        terminal.WriteLine("");
        
        var activeGods = godSystem.GetActiveGods();
        if (activeGods.Count == 0)
        {
            terminal.WriteLine(Loc.Get("temple.no_gods_exist"), "gray");
        }
        else
        {
            foreach (var god in activeGods.OrderByDescending(g => g.Experience))
            {
                terminal.WriteLine(Loc.Get("temple.altar_of", god.Name, god.GetTitle()), "yellow");
                terminal.WriteLine(Loc.Get("temple.believers_count", god.Believers), "white");
                terminal.WriteLine(Loc.Get("temple.power_count", god.Experience), "cyan");
                terminal.WriteLine("");
            }
        }
        
        await terminal.GetInputAsync(Loc.Get("ui.press_enter"));
    }
    
    /// <summary>
    /// Display god ranking (Pascal TEMPLE.PAS)
    /// </summary>
    private async Task DisplayGodRanking()
    {
        terminal.WriteLine("");
        terminal.WriteLine("");

        var rankedGods = godSystem.ListGods(true);

        // Build a unified ranking list: (name, title, followers, isPlayer)
        var ranking = new List<(string Name, string Title, int Followers, bool IsPlayer)>();

        foreach (var god in rankedGods)
            ranking.Add((god.Name, god.GetTitle(), god.Believers, false));

        // Add player immortals
        var playerImmortals = await GetImmortalGodsAsync();
        foreach (var ig in playerImmortals)
        {
            int titleIdx = Math.Clamp(ig.GodLevel - 1, 0, GameConfig.GodTitles.Length - 1);
            ranking.Add((ig.DivineName, GameConfig.GodTitles[titleIdx], ig.Believers, true));
        }

        // Also include current player if they're an immortal and not already listed
        if (currentPlayer.IsImmortal && !string.IsNullOrEmpty(currentPlayer.DivineName)
            && !ranking.Any(r => r.Name.Equals(currentPlayer.DivineName, StringComparison.OrdinalIgnoreCase)))
        {
            int titleIdx = Math.Clamp(currentPlayer.GodLevel - 1, 0, GameConfig.GodTitles.Length - 1);
            int believers = PantheonLocation.CountBelievers(currentPlayer.DivineName);
            ranking.Add((currentPlayer.DivineName, GameConfig.GodTitles[titleIdx], believers, true));
        }

        // Sort by followers descending
        ranking = ranking.OrderByDescending(r => r.Followers).ToList();

        if (ranking.Count == 0)
        {
            terminal.WriteLine(Loc.Get("temple.no_gods_exist"), "gray");
        }
        else
        {
            terminal.WriteLine(Loc.Get("temple.god_ranking_header"), "white");
            WriteThickDivider(59, "magenta");

            for (int i = 0; i < ranking.Count; i++)
            {
                var entry = ranking[i];
                string line = $"{(i + 1).ToString().PadLeft(3)}. {entry.Name.PadRight(25)} {entry.Title.PadRight(20)} {entry.Followers.ToString().PadLeft(10)}";
                terminal.WriteLine(line, entry.IsPlayer ? "bright_cyan" : "yellow");
            }
        }

        await terminal.GetInputAsync(Loc.Get("ui.press_enter"));
    }
    
    /// <summary>
    /// Display holy news (Pascal TEMPLE.PAS)
    /// </summary>
    private async Task DisplayHolyNews()
    {
        terminal.WriteLine("");
        terminal.WriteLine("");
        WriteSectionHeader(Loc.Get("temple.holy_news"), "cyan");
        terminal.WriteLine("");
        terminal.WriteLine(Loc.Get("temple.gods_watch"), "white");
        terminal.WriteLine(Loc.Get("temple.divine_interventions"), "white");
        terminal.WriteLine(Loc.Get("temple.prayers_reach"), "white");
        terminal.WriteLine("");
        
        var stats = godSystem.GetGodStatistics();
        terminal.WriteLine(Loc.Get("temple.total_gods", stats["TotalGods"]), "yellow");
        terminal.WriteLine(Loc.Get("temple.total_believers", stats["TotalBelievers"]), "yellow");
        terminal.WriteLine(Loc.Get("temple.most_powerful", stats["MostPowerfulGod"]), "yellow");
        terminal.WriteLine(Loc.Get("temple.most_popular", stats["MostPopularGod"]), "yellow");
        
        await terminal.GetInputAsync(Loc.Get("ui.press_enter"));
    }
    
    /// <summary>
    /// Select a god from available options (Pascal Select_A_God function)
    /// Shows the list automatically and supports partial name matching
    /// </summary>
    private async Task<God?> SelectGod(string prompt = "Select a god", bool requireConfirmation = true)
    {
        var activeGods = godSystem.GetActiveGods()
            .Where(g => g.Id != "SUPREME") // Exclude Manwe (system god, not worshippable)
            .OrderBy(g => g.Name).ToList();
        if (activeGods.Count == 0)
        {
            terminal.WriteLine(Loc.Get("temple.no_gods_available"), "red");
            await Task.Delay(1000);
            return null;
        }

        // Always show the list first
        DisplayGodListCompact(activeGods);

        while (true)
        {
            terminal.WriteLine("");
            terminal.WriteLine(Loc.Get("temple.select_prompt", prompt), "white");
            var input = await terminal.GetInputAsync("> ");

            if (string.IsNullOrEmpty(input))
            {
                return null;
            }

            // Find matching gods (partial match, case-insensitive)
            var matches = activeGods.Where(g =>
                g.Name.StartsWith(input, StringComparison.OrdinalIgnoreCase) ||
                g.Name.Contains(input, StringComparison.OrdinalIgnoreCase)
            ).ToList();

            if (matches.Count == 0)
            {
                terminal.WriteLine(Loc.Get("temple.no_god_match", input), "red");
                continue;
            }

            God selectedGod;
            if (matches.Count == 1)
            {
                selectedGod = matches[0];
            }
            else
            {
                // Multiple matches - prefer exact start match, then show options
                var startsWithMatch = matches.FirstOrDefault(g =>
                    g.Name.StartsWith(input, StringComparison.OrdinalIgnoreCase));

                if (startsWithMatch != null && matches.Count(g =>
                    g.Name.StartsWith(input, StringComparison.OrdinalIgnoreCase)) == 1)
                {
                    selectedGod = startsWithMatch;
                }
                else
                {
                    // Show ambiguous matches
                    terminal.WriteLine("");
                    terminal.WriteLine(Loc.Get("temple.multiple_matches"), "yellow");
                    foreach (var match in matches)
                    {
                        string alignment = match.Goodness > match.Darkness ? "(Light)" : match.Darkness > match.Goodness ? "(Dark)" : "(Neutral)";
                        terminal.WriteLine($"  - {match.Name} {alignment}", "white");
                    }
                    terminal.WriteLine(Loc.Get("temple.be_more_specific"), "gray");
                    continue;
                }
            }

            // Show selected god and ask for confirmation
            string godAlignment = selectedGod.Goodness > selectedGod.Darkness ? "Light" :
                                  selectedGod.Darkness > selectedGod.Goodness ? "Dark" : "Neutral";
            string alignColor = godAlignment == "Light" ? "bright_cyan" :
                               godAlignment == "Dark" ? "dark_red" : "yellow";

            terminal.WriteLine("");
            terminal.SetColor(alignColor);
            terminal.WriteLine(Loc.Get("temple.selected_god", selectedGod.Name, selectedGod.GetTitle()));
            terminal.SetColor("gray");
            terminal.WriteLine(Loc.Get("temple.alignment_info", godAlignment, selectedGod.Believers));

            if (requireConfirmation)
            {
                terminal.WriteLine("");
                var confirm = await terminal.GetInputAsync(Loc.Get("ui.confirm_choose", selectedGod.Name));
                if (confirm.ToUpper() != "Y")
                {
                    terminal.WriteLine(Loc.Get("temple.selection_cancelled"), "gray");
                    continue;
                }
            }

            return selectedGod;
        }
    }

    /// <summary>
    /// Display a compact list of available gods for selection
    /// </summary>
    private void DisplayGodListCompact(List<God> gods)
    {
        terminal.WriteLine("");
        WriteSectionHeader(Loc.Get("temple.available_gods"), "cyan");
        terminal.WriteLine("");

        if (gods.Count == 0)
        {
            terminal.WriteLine(Loc.Get("temple.no_gods_accept"), "gray");
            return;
        }

        foreach (var god in gods)
        {
            // Get domain/title from properties or use GetTitle()
            string domain = god.Properties.ContainsKey("Domain")
                ? god.Properties["Domain"]?.ToString() ?? god.GetTitle()
                : god.GetTitle();

            // Color based on alignment
            string color = "yellow";
            string alignmentMarker = " ";
            if (god.Goodness > god.Darkness * 2)
            {
                color = "bright_cyan";
                alignmentMarker = "+";  // Light
            }
            else if (god.Darkness > god.Goodness * 2)
            {
                color = "dark_red";
                alignmentMarker = "*";  // Dark
            }

            terminal.WriteLine($"  {alignmentMarker} {god.Name} - {domain}", color);
        }

        terminal.WriteLine("");
        terminal.SetColor("gray");
        terminal.WriteLine(Loc.Get("temple.type_name_hint"));
    }

    /// <summary>
    /// Display list of available gods (full details version)
    /// </summary>
    private void DisplayGodList()
    {
        terminal.WriteLine("");
        WriteSectionHeader(Loc.Get("temple.available_gods"), "cyan");
        terminal.WriteLine("");

        var activeGods = godSystem.GetActiveGods()
            .Where(g => g.Id != "SUPREME") // Exclude Manwe (system god, not worshippable)
            .OrderBy(g => g.Name).ToList();

        if (activeGods.Count == 0)
        {
            terminal.WriteLine(Loc.Get("temple.no_gods_accept"), "gray");
            terminal.WriteLine("");
            return;
        }

        foreach (var god in activeGods)
        {
            // Get domain/title from properties or use GetTitle()
            string domain = god.Properties.ContainsKey("Domain")
                ? god.Properties["Domain"]?.ToString() ?? god.GetTitle()
                : god.GetTitle();

            // Color based on alignment
            string color = "yellow";
            if (god.Goodness > god.Darkness * 2)
                color = "bright_cyan";
            else if (god.Darkness > god.Goodness * 2)
                color = "dark_red";

            terminal.WriteLine($"  {god.Name}, {domain}", color);

            // Show description if available
            if (god.Properties.ContainsKey("Description"))
            {
                terminal.WriteLine($"    {god.Properties["Description"]}", "gray");
            }

            terminal.WriteLine(Loc.Get("temple.god_list_stats", god.Believers, god.Experience.ToString("N0")), "white");
            terminal.WriteLine("");
        }
    }
    
    /// <summary>
    /// Perform altar desecration (Pascal TEMPLE.PAS)
    /// </summary>
    private async Task PerformDesecration(God god)
    {
        terminal.WriteLine("");
        terminal.WriteLine("");
        
        var random = Random.Shared;
        switch (random.Next(2))
        {
            case 0:
                terminal.WriteLine(Loc.Get("temple.desecrate_hack_line1"), "white");
                terminal.WriteLine(Loc.Get("temple.desecrate_hack_line2"), "white");
                terminal.Write(Loc.Get("temple.desecrate_hack_word"), "red");
                for (int i = 0; i < 4; i++)
                {
                    await Task.Delay(500);
                    terminal.Write(".", "red");
                }
                terminal.Write(Loc.Get("temple.desecrate_hack_word_lower"), "red");
                for (int i = 0; i < 4; i++)
                {
                    await Task.Delay(500);
                    terminal.Write(".", "red");
                }
                terminal.WriteLine(Loc.Get("temple.desecrate_hack_final"), "red");
                break;

            case 1:
                terminal.WriteLine(Loc.Get("temple.desecrate_unholy_line1"), "white");
                terminal.WriteLine(Loc.Get("temple.desecrate_unholy_line2"), "white");
                terminal.WriteLine(Loc.Get("temple.desecrate_altar_damaged"), "red");
                break;
        }

        terminal.WriteLine("");
        terminal.WriteLine(Loc.Get("temple.desecrated_altar", god.Name), "red");
        terminal.WriteLine(Loc.Get("temple.gods_remember_blasphemy"), "red");
        
        // Process desecration in god system
        godSystem.ProcessAltarDesecration(god.Name, currentPlayer.Name2);
        
        // Use evil deed
        currentPlayer.DarkNr--;
        
        await Task.Delay(3000);
    }
    
    /// <summary>
    /// Process sacrifice menu (Pascal TEMPLE.PAS)
    /// </summary>
    private async Task ProcessSacrificeMenu(God god, bool wrongGod)
    {
        bool done = false;
        
        while (!done)
        {
            terminal.WriteLine("");
            WriteSectionHeader(Loc.Get("temple.sacrifice_to", god.Name), "cyan");
            terminal.WriteLine("");
            terminal.WriteLine(Loc.Get("temple.sacrifice_gold_option"), "yellow");
            terminal.WriteLine(Loc.Get("temple.sacrifice_status_option"), "yellow");
            terminal.WriteLine(Loc.Get("temple.sacrifice_return_option"), "yellow");
            
            var choice = await terminal.GetInputAsync(Loc.Get("ui.your_choice"));
            
            switch (choice.ToUpper())
            {
                case "G":
                    await ProcessGoldSacrifice(god, wrongGod);
                    break;
                    
                case "S":
                    await DisplayPlayerStatus();
                    break;
                    
                case "R":
                    done = true;
                    break;
                    
                case "?":
                    // Menu already displayed
                    break;
                    
                default:
                    terminal.WriteLine(Loc.Get("temple.invalid_choice_short"), "red");
                    await Task.Delay(1000);
                    break;
            }
        }
    }
    
    /// <summary>
    /// Process gold sacrifice (Pascal TEMPLE.PAS)
    /// </summary>
    private async Task ProcessGoldSacrifice(God god, bool wrongGod)
    {
        terminal.WriteLine("");
        var goldStr = await terminal.GetInputAsync(Loc.Get("temple.gold_sacrifice_prompt"));

        if (!long.TryParse(goldStr, out long goldAmount) || goldAmount <= 0)
        {
            terminal.WriteLine(Loc.Get("temple.invalid_amount"), "red");
            await Task.Delay(1000);
            return;
        }

        if (goldAmount > currentPlayer.Gold)
        {
            terminal.WriteLine(Loc.Get("temple.not_enough_gold"), "red");
            await Task.Delay(1000);
            return;
        }

        var choice = await terminal.GetInputAsync(Loc.Get("temple.confirm_sacrifice_gold", goldAmount, god.Name));
        if (choice.ToUpper() != "Y") return;

        // Process sacrifice
        currentPlayer.Gold -= goldAmount;
        var powerGained = godSystem.ProcessGoldSacrifice(god.Name, goldAmount, currentPlayer.Name2);

        terminal.WriteLine("");
        terminal.WriteLine(Loc.Get("temple.god_power_growing", god.Name), "yellow");
        terminal.WriteLine(Loc.Get("temple.reward_will_come"), "white");
        terminal.WriteLine(Loc.Get("temple.power_increased", powerGained), "cyan");

        // Grant temporary blessing from sacrifice (if worshipping this god)
        string playerGod = godSystem.GetPlayerGod(currentPlayer.Name2);
        if (!wrongGod && playerGod == god.Name && goldAmount >= 100)
        {
            var tempBlessing = UsurperRemake.Systems.DivineBlessingSystem.Instance.GrantSacrificeBlessing(
                currentPlayer, goldAmount, god.Name);

            if (tempBlessing != null)
            {
                terminal.WriteLine("");
                terminal.SetColor("bright_magenta");
                terminal.WriteLine($"*** {tempBlessing.Name} ***");
                terminal.SetColor("white");
                terminal.WriteLine(tempBlessing.Description);
                var duration = tempBlessing.ExpiresAt - DateTime.Now;
                terminal.WriteLine(Loc.Get("temple.duration_minutes", duration.TotalMinutes.ToString("F0")), "gray");
            }
        }

        // Check if god is evil (Darkness > Goodness) to determine faction effect
        bool isEvilGod = god.Darkness > god.Goodness;
        int standingGain = Math.Max(1, (int)(goldAmount / 100));

        if (isEvilGod)
        {
            // Evil god sacrifice - dark act
            currentPlayer.DarkNr++;
            currentPlayer.Darkness += standingGain;
            UsurperRemake.Systems.FactionSystem.Instance.ModifyReputation(UsurperRemake.Systems.Faction.TheShadows, standingGain);
            terminal.SetColor("bright_magenta");
            terminal.WriteLine(Loc.Get("temple.darkness_accepts", standingGain));
        }
        else
        {
            // Good god sacrifice - light act
            currentPlayer.ChivNr++;
            currentPlayer.Chivalry += standingGain;
            UsurperRemake.Systems.FactionSystem.Instance.ModifyReputation(UsurperRemake.Systems.Faction.TheFaith, standingGain);
            terminal.SetColor("bright_cyan");
            terminal.WriteLine(Loc.Get("temple.devotion_noted", standingGain));
        }

        // Divine Wrath System - record betrayal when sacrificing to wrong god
        if (wrongGod && !string.IsNullOrEmpty(playerGod))
        {
            // Check if player's god is opposite alignment from the one they're sacrificing to
            var playerGodData = godSystem.GetGod(playerGod);
            bool playerGodIsEvil = playerGodData != null && playerGodData.Darkness > playerGodData.Goodness;
            bool isOppositeAlignment = playerGodIsEvil != isEvilGod;

            // Severity: 1 = same alignment different god, 2 = opposite alignment, 3 = opposite + large sacrifice
            int severity = 1;
            if (isOppositeAlignment) severity = 2;
            if (isOppositeAlignment && goldAmount >= 500) severity = 3;

            currentPlayer.RecordDivineWrath(playerGod, god.Name, severity);

            terminal.WriteLine("");
            terminal.SetColor("bright_red");
            if (severity >= 3)
            {
                terminal.WriteLine(Loc.Get("temple.god_seethes", playerGod.ToUpper()));
                terminal.WriteLine(Loc.Get("temple.betrayal_not_forgotten"));
            }
            else if (severity == 2)
            {
                terminal.WriteLine(Loc.Get("temple.god_furious", playerGod));
                terminal.WriteLine(Loc.Get("temple.darkness_awaits"));
            }
            else
            {
                terminal.WriteLine(Loc.Get("temple.god_displeased", playerGod));
                terminal.WriteLine(Loc.Get("temple.beware_shadows"));
            }
        }

        await Task.Delay(3000);
    }

    /// <summary>
    /// Verify player's god still exists (Pascal TEMPLE.PAS)
    /// </summary>
    private async Task VerifyPlayerGodExists()
    {
        string playerGod = godSystem.GetPlayerGod(currentPlayer.Name2);
        if (!string.IsNullOrEmpty(playerGod))
        {
            if (!godSystem.VerifyGodExists(playerGod))
            {
                terminal.WriteLine(Loc.Get("temple.god_no_longer_exists", playerGod), "red");
                terminal.WriteLine(Loc.Get("temple.faith_shaken"), "gray");
                godSystem.SetPlayerGod(currentPlayer.Name2, "");
                await Task.Delay(2000);
            }
        }
    }
    
    /// <summary>
    /// Display player status
    /// </summary>
    private async Task DisplayPlayerStatus()
    {
        terminal.WriteLine("");
        WriteSectionHeader(Loc.Get("temple.your_status"), "cyan");
        terminal.WriteLine("");
        terminal.WriteLine($"{Loc.Get("ui.name_label")}: {currentPlayer.Name2}", "yellow");
        terminal.WriteLine($"{Loc.Get("ui.level")}: {currentPlayer.Level}", "yellow");
        terminal.WriteLine($"{Loc.Get("ui.gold")}: {currentPlayer.Gold:N0}", "yellow");
        terminal.WriteLine(Loc.Get("temple.good_deeds", currentPlayer.ChivNr), "green");
        terminal.WriteLine(Loc.Get("temple.evil_deeds", currentPlayer.DarkNr), "red");

        string playerGod = godSystem.GetPlayerGod(currentPlayer.Name2);
        if (!string.IsNullOrEmpty(playerGod))
        {
            terminal.WriteLine(Loc.Get("temple.god_label", playerGod), "cyan");
        }
        else
        {
            terminal.WriteLine(Loc.Get("temple.god_none"), "gray");
        }
        
        await terminal.GetInputAsync(Loc.Get("ui.press_enter"));
    }

    #region Old Gods Integration

    /// <summary>
    /// Display prophecies about the Old Gods - hints about the main storyline
    /// </summary>
    private async Task DisplayOldGodsProphecies()
    {
        terminal.WriteLine("");
        terminal.WriteLine("");
        WriteSectionHeader(Loc.Get("temple.prophecies"), "bright_magenta");
        terminal.WriteLine("");

        // Random divine whisper intro
        terminal.WriteLine(DivineWhispers[random.Next(DivineWhispers.Length)], "gray");
        await Task.Delay(1500);
        terminal.WriteLine("");

        // Show prophecies based on story progression
        var story = StoryProgressionSystem.Instance;
        int propheciesRevealed = 0;

        // Maelketh - The Broken Blade (War God)
        if (story.OldGodStates.TryGetValue(OldGodType.Maelketh, out var maelkethState))
        {
            if (maelkethState.Status == GodStatus.Defeated)
                terminal.WriteLine(Loc.Get("temple.prophecy_maelketh_defeated"), "green");
            else if (maelkethState.Status == GodStatus.Saved)
                terminal.WriteLine(Loc.Get("temple.prophecy_maelketh_saved"), "bright_green");
            else
                terminal.WriteLine(OldGodsProphecies[0], "red");
            propheciesRevealed++;
        }
        else if (currentPlayer.Level >= 50)
        {
            terminal.WriteLine(OldGodsProphecies[0], "red");
            propheciesRevealed++;
        }

        // Veloura - The Withered Heart (Love Goddess)
        if (story.OldGodStates.TryGetValue(OldGodType.Veloura, out var velouraState))
        {
            if (velouraState.Status == GodStatus.Defeated)
                terminal.WriteLine(Loc.Get("temple.prophecy_veloura_defeated"), "gray");
            else if (velouraState.Status == GodStatus.Saved)
                terminal.WriteLine(Loc.Get("temple.prophecy_veloura_saved"), "bright_magenta");
            else
                terminal.WriteLine(OldGodsProphecies[1], "red");
            propheciesRevealed++;
        }
        else if (currentPlayer.Level >= 40)
        {
            terminal.WriteLine(OldGodsProphecies[1], "red");
            propheciesRevealed++;
        }

        // Thorgrim - The Hollow Judge (Law God)
        if (story.OldGodStates.TryGetValue(OldGodType.Thorgrim, out var thorgrimState))
        {
            if (thorgrimState.Status == GodStatus.Defeated)
                terminal.WriteLine(Loc.Get("temple.prophecy_thorgrim_defeated"), "yellow");
            else
                terminal.WriteLine(OldGodsProphecies[2], "red");
            propheciesRevealed++;
        }
        else if (currentPlayer.Level >= 60)
        {
            terminal.WriteLine(OldGodsProphecies[2], "red");
            propheciesRevealed++;
        }

        // Noctura - The Shadow Weaver
        if (story.OldGodStates.TryGetValue(OldGodType.Noctura, out var nocturaState))
        {
            if (nocturaState.Status == GodStatus.Allied)
                terminal.WriteLine(Loc.Get("temple.prophecy_noctura_allied"), "bright_cyan");
            else if (nocturaState.Status == GodStatus.Defeated)
                terminal.WriteLine(Loc.Get("temple.prophecy_noctura_defeated"), "gray");
            else
                terminal.WriteLine(OldGodsProphecies[3], "red");
            propheciesRevealed++;
        }
        else if (currentPlayer.Level >= 70)
        {
            terminal.WriteLine(OldGodsProphecies[3], "red");
            propheciesRevealed++;
        }

        // Aurelion - The Fading Light (encountered at Temple)
        if (story.OldGodStates.TryGetValue(OldGodType.Aurelion, out var aurelionState))
        {
            if (aurelionState.Status == GodStatus.Defeated)
                terminal.WriteLine(Loc.Get("temple.prophecy_aurelion_defeated"), "gray");
            else if (aurelionState.Status == GodStatus.Saved)
                terminal.WriteLine(Loc.Get("temple.prophecy_aurelion_saved"), "bright_yellow");
            else
                terminal.WriteLine(OldGodsProphecies[4], "red");
            propheciesRevealed++;
        }
        else if (currentPlayer.Level >= 55)
        {
            terminal.WriteLine(OldGodsProphecies[4], "red");
            propheciesRevealed++;
        }

        // Terravok - The Sleeping Mountain
        if (story.OldGodStates.TryGetValue(OldGodType.Terravok, out var terravokState))
        {
            if (terravokState.Status == GodStatus.Defeated)
                terminal.WriteLine(Loc.Get("temple.prophecy_terravok_defeated"), "gray");
            else if (terravokState.Status == GodStatus.Saved)
                terminal.WriteLine(Loc.Get("temple.prophecy_terravok_saved"), "bright_green");
            else
                terminal.WriteLine(OldGodsProphecies[5], "red");
            propheciesRevealed++;
        }
        else if (currentPlayer.Level >= 75)
        {
            terminal.WriteLine(OldGodsProphecies[5], "red");
            propheciesRevealed++;
        }

        // Manwe - The Weary Creator (final boss)
        if (story.OldGodStates.TryGetValue(OldGodType.Manwe, out var manweState))
        {
            if (manweState.Status != GodStatus.Imprisoned)
                terminal.WriteLine(Loc.Get("temple.prophecy_manwe_resolved"), "bright_white");
            else
                terminal.WriteLine(OldGodsProphecies[6], "bright_magenta");
            propheciesRevealed++;
        }
        else if (currentPlayer.Level >= 90)
        {
            terminal.WriteLine(OldGodsProphecies[6], "bright_magenta");
            propheciesRevealed++;
        }

        if (propheciesRevealed == 0)
        {
            terminal.WriteLine(Loc.Get("temple.prophecies_sealed"), "gray");
            terminal.WriteLine(Loc.Get("temple.prophecies_grow_stronger"), "gray");
        }

        terminal.WriteLine("");

        // Chance for divine vision
        if (random.NextDouble() < 0.15 && currentPlayer.Level >= 30)
        {
            await DisplayDivineVision();
        }

        await terminal.GetInputAsync(Loc.Get("ui.press_enter"));
    }

    /// <summary>
    /// Display a divine vision - rare insight into the story
    /// </summary>
    private async Task DisplayDivineVision()
    {
        terminal.WriteLine("");
        WriteBoxHeader(Loc.Get("temple.vision"), "bright_cyan", 63);
        terminal.WriteLine("");
        await Task.Delay(1500);

        var story = StoryProgressionSystem.Instance;
        int godsFaced = story.OldGodStates.Count(s => s.Value.Status != GodStatus.Imprisoned);

        if (godsFaced == 0)
        {
            terminal.WriteLine(Loc.Get("temple.vision_seven_figures"), "white");
            terminal.WriteLine(Loc.Get("temple.vision_faces_beautiful"), "white");
            await Task.Delay(1000);
            terminal.WriteLine("", "white");
            terminal.WriteLine(Loc.Get("temple.vision_darkness_creeps"), "gray");
            terminal.WriteLine(Loc.Get("temple.vision_beauty_twists"), "gray");
            await Task.Delay(1000);
            terminal.WriteLine("", "white");
            terminal.WriteLine(Loc.Get("temple.vision_meant_to_guide"), "bright_magenta");
            terminal.WriteLine(Loc.Get("temple.vision_broke_hearts"), "bright_magenta");
        }
        else if (godsFaced < 4)
        {
            terminal.WriteLine(Loc.Get("temple.vision_endless_halls"), "white");
            terminal.WriteLine(Loc.Get("temple.vision_faint_light"), "white");
            await Task.Delay(1000);
            terminal.WriteLine("", "white");
            terminal.WriteLine(Loc.Get("temple.vision_light_fades"), "bright_yellow");
            terminal.WriteLine(Loc.Get("temple.vision_find_me"), "bright_yellow");
            await Task.Delay(1000);
            terminal.WriteLine("", "white");
            terminal.WriteLine(Loc.Get("temple.vision_from_temple"), "bright_cyan");
        }
        else
        {
            terminal.WriteLine(Loc.Get("temple.vision_throne_stars"), "white");
            terminal.WriteLine(Loc.Get("temple.vision_figure_older"), "white");
            await Task.Delay(1000);
            terminal.WriteLine("", "white");
            terminal.WriteLine(Loc.Get("temple.vision_come_far"), "bright_white");
            terminal.WriteLine(Loc.Get("temple.vision_final_question"), "bright_white");
            await Task.Delay(1000);
            terminal.WriteLine("", "white");
            terminal.WriteLine(Loc.Get("temple.vision_worth_cost"), "bright_magenta");
        }

        terminal.WriteLine("");
        await Task.Delay(2000);

        // Record divine vision in story
        story.SetStoryFlag("had_divine_vision", true);

        // Generate news
        NewsSystem.Instance.Newsy(false, $"{currentPlayer.Name2} received a vision from the gods at the Temple.");
    }

    /// <summary>
    /// Check if player can enter the Deep Temple (Aurelion encounter)
    /// </summary>
    private bool CanEnterDeepTemple()
    {
        // Requires level 55+ and at least 3 Old Gods defeated
        if (currentPlayer.Level < 55)
            return false;

        var story = StoryProgressionSystem.Instance;
        int godsDefeated = story.OldGodStates.Count(s => s.Value.Status == GodStatus.Defeated || s.Value.Status == GodStatus.Saved);

        // Can enter if defeated/saved 3+ gods, OR if already encountered Aurelion
        if (godsDefeated >= 3)
            return true;

        if (story.HasStoryFlag("aurelion_encountered"))
            return true;

        return false;
    }

    /// <summary>
    /// Enter the Deep Temple - Aurelion's domain
    /// </summary>
    private async Task EnterDeepTemple()
    {
        if (!CanEnterDeepTemple())
        {
            terminal.WriteLine("");
            terminal.WriteLine(Loc.Get("temple.deep_temple_sealed"), "red");
            terminal.WriteLine(Loc.Get("temple.deep_temple_prove"), "gray");
            await Task.Delay(2000);
            return;
        }

        terminal.ClearScreen();
        terminal.WriteLine("");
        WriteSectionHeader(Loc.Get("temple.deep_temple"), "bright_yellow");
        terminal.WriteLine("");
        terminal.WriteLine(Loc.Get("temple.deep_temple_descend"), "white");
        terminal.WriteLine(Loc.Get("temple.deep_temple_torches"), "white");
        await Task.Delay(1500);
        terminal.WriteLine("");
        terminal.WriteLine(Loc.Get("temple.deep_temple_air_thick"), "gray");
        terminal.WriteLine(Loc.Get("temple.deep_temple_watches"), "gray");
        await Task.Delay(1500);

        var story = StoryProgressionSystem.Instance;

        // Check Aurelion's status
        if (story.OldGodStates.TryGetValue(OldGodType.Aurelion, out var aurelionState))
        {
            if (aurelionState.Status == GodStatus.Defeated)
            {
                terminal.WriteLine("");
                terminal.WriteLine(Loc.Get("temple.aurelion_altar_dark"), "gray");
                terminal.WriteLine(Loc.Get("temple.aurelion_ash_remains"), "gray");
                terminal.WriteLine(Loc.Get("temple.aurelion_sense_loss"), "white");
                await terminal.GetInputAsync(Loc.Get("temple.press_enter_return"));
                return;
            }
            else if (aurelionState.Status == GodStatus.Saved)
            {
                terminal.WriteLine("");
                terminal.WriteLine(Loc.Get("temple.aurelion_warm_light"), "bright_yellow");
                terminal.WriteLine(Loc.Get("temple.aurelion_presence"), "bright_white");
                terminal.WriteLine("", "white");
                terminal.WriteLine(Loc.Get("temple.aurelion_thank_you"), "bright_cyan");
                terminal.WriteLine(Loc.Get("temple.aurelion_new_vessel"), "bright_cyan");
                await terminal.GetInputAsync(Loc.Get("temple.press_enter_return"));
                return;
            }
        }

        // Aurelion encounter available
        terminal.WriteLine("");
        terminal.WriteLine(Loc.Get("temple.aurelion_glow"), "bright_yellow");
        terminal.WriteLine(Loc.Get("temple.aurelion_weak"), "white");
        terminal.WriteLine("");
        terminal.WriteLine(Loc.Get("temple.aurelion_see_me"), "bright_yellow");
        terminal.WriteLine(Loc.Get("temple.aurelion_few_can"), "bright_yellow");
        terminal.WriteLine("");

        var choice = await terminal.GetInputAsync(Loc.Get("temple.approach_light"));

        if (choice.ToUpper() == "Y")
        {
            story.SetStoryFlag("aurelion_encountered", true);

            // Start Aurelion boss encounter
            var bossSystem = OldGodBossSystem.Instance;
            if (bossSystem.CanEncounterBoss(currentPlayer, OldGodType.Aurelion))
            {
                var result = await bossSystem.StartBossEncounter(currentPlayer, OldGodType.Aurelion, terminal);

                if (result.Success)
                {
                    // Generate news
                    switch (result.Outcome)
                    {
                        case BossOutcome.Defeated:
                            NewsSystem.Instance.Newsy(true, $"{currentPlayer.Name2} destroyed Aurelion, the Fading Light! Truth dies in darkness.");
                            break;
                        case BossOutcome.Saved:
                            NewsSystem.Instance.Newsy(true, $"{currentPlayer.Name2} saved Aurelion, the Fading Light! Truth lives on within them.");
                            break;
                        case BossOutcome.Allied:
                            NewsSystem.Instance.Newsy(true, $"{currentPlayer.Name2} has allied with Aurelion, the Fading Light!");
                            break;
                    }
                }
            }
            else
            {
                terminal.WriteLine("");
                terminal.WriteLine(Loc.Get("temple.aurelion_flickers"), "yellow");
                terminal.WriteLine(Loc.Get("temple.aurelion_too_weak"), "bright_yellow");
                terminal.WriteLine(Loc.Get("temple.aurelion_defeat_siblings"), "bright_yellow");
                await Task.Delay(2000);
            }
        }
        else
        {
            terminal.WriteLine("");
            terminal.WriteLine(Loc.Get("temple.step_back"), "white");
            terminal.WriteLine(Loc.Get("temple.aurelion_understand"), "bright_yellow");
            terminal.WriteLine(Loc.Get("temple.aurelion_not_ready"), "bright_yellow");
        }

        await terminal.GetInputAsync(Loc.Get("temple.press_enter_return"));
    }

    /// <summary>
    /// Process item sacrifice - sacrifice equipment for divine favor
    /// </summary>
    private async Task ProcessItemSacrifice()
    {
        terminal.WriteLine("");
        terminal.WriteLine("");
        WriteSectionHeader(Loc.Get("temple.item_sacrifice"), "cyan");
        terminal.WriteLine("");

        string currentGod = godSystem.GetPlayerGod(currentPlayer.Name2);

        if (string.IsNullOrEmpty(currentGod))
        {
            terminal.WriteLine(Loc.Get("temple.must_worship_first"), "red");
            terminal.WriteLine(Loc.Get("temple.visit_worship"), "gray");
            await Task.Delay(2000);
            return;
        }

        terminal.WriteLine(Loc.Get("temple.kneel_before", currentGod), "white");
        terminal.WriteLine("", "white");
        terminal.WriteLine(Loc.Get("temple.what_sacrifice"), "cyan");
        terminal.WriteLine("", "white");
        terminal.WriteLine(Loc.Get("temple.sacrifice_weapon_option"), "yellow");
        terminal.WriteLine(Loc.Get("temple.sacrifice_armor_option"), "yellow");
        terminal.WriteLine(Loc.Get("temple.sacrifice_potions_option"), "yellow");
        terminal.WriteLine(Loc.Get("temple.sacrifice_return"), "yellow");
        terminal.WriteLine("");

        var choice = await terminal.GetInputAsync(Loc.Get("temple.sacrifice_prompt"));

        switch (choice.ToUpper())
        {
            case "W":
                await SacrificeWeapon(currentGod);
                break;
            case "A":
                await SacrificeArmor(currentGod);
                break;
            case "H":
                await SacrificePotions(currentGod);
                break;
            case "R":
                return;
        }
    }

    /// <summary>
    /// Sacrifice weapon to god
    /// </summary>
    private async Task SacrificeWeapon(string godName)
    {
        if (currentPlayer.WeapPow <= 0)
        {
            terminal.WriteLine(Loc.Get("temple.no_weapon"), "red");
            await Task.Delay(1500);
            return;
        }

        var confirm = await terminal.GetInputAsync(Loc.Get("temple.confirm_sacrifice_weapon", currentPlayer.WeapPow, godName));
        if (confirm.ToUpper() != "Y") return;

        long powerGained = currentPlayer.WeapPow * 2;
        godSystem.ProcessGoldSacrifice(godName, powerGained * 100, currentPlayer.Name2); // Convert to equivalent gold power

        terminal.WriteLine("");
        terminal.WriteLine(Loc.Get("temple.dissolves_divine_light"), "bright_yellow");
        terminal.WriteLine(Loc.Get("temple.god_accepts", godName), "cyan");
        terminal.WriteLine(Loc.Get("temple.divine_power_increased", powerGained), "bright_cyan");

        // Chance for divine blessing based on weapon power
        if (random.NextDouble() < 0.3 + (currentPlayer.WeapPow / 500.0))
        {
            int blessingBonus = random.Next(2, 6);
            currentPlayer.Strength += blessingBonus;
            terminal.WriteLine(Loc.Get("temple.blessing_strength", godName, blessingBonus), "bright_green");
        }

        currentPlayer.WeapPow = 0;
        // Note: WeaponName is derived from equipment slots

        // Apply faction effects based on god alignment
        ApplyFactionEffectForSacrifice(godName, (int)Math.Max(1, powerGained / 10));

        // Generate news
        NewsSystem.Instance.Newsy(false, $"{currentPlayer.Name2} sacrificed their weapon to {godName} at the Temple.");

        await Task.Delay(2500);
    }

    /// <summary>
    /// Sacrifice armor to god
    /// </summary>
    private async Task SacrificeArmor(string godName)
    {
        if (currentPlayer.ArmPow <= 0)
        {
            terminal.WriteLine(Loc.Get("temple.no_armor"), "red");
            await Task.Delay(1500);
            return;
        }

        var confirm = await terminal.GetInputAsync(Loc.Get("temple.confirm_sacrifice_armor", currentPlayer.ArmPow, godName));
        if (confirm.ToUpper() != "Y") return;

        long powerGained = currentPlayer.ArmPow * 2;
        godSystem.ProcessGoldSacrifice(godName, powerGained * 100, currentPlayer.Name2);

        terminal.WriteLine("");
        terminal.WriteLine(Loc.Get("temple.armor_dissolves"), "bright_yellow");
        terminal.WriteLine(Loc.Get("temple.god_accepts", godName), "cyan");
        terminal.WriteLine(Loc.Get("temple.divine_power_increased", powerGained), "bright_cyan");

        // Chance for divine blessing
        if (random.NextDouble() < 0.3 + (currentPlayer.ArmPow / 500.0))
        {
            int blessingBonus = random.Next(2, 6);
            currentPlayer.Defence += blessingBonus;
            terminal.WriteLine(Loc.Get("temple.blessing_defence", godName, blessingBonus), "bright_green");
        }

        currentPlayer.ArmPow = 0;
        // Note: ArmorName is derived from equipment slots

        // Apply faction effects based on god alignment
        ApplyFactionEffectForSacrifice(godName, (int)Math.Max(1, powerGained / 10));

        NewsSystem.Instance.Newsy(false, $"{currentPlayer.Name2} sacrificed their armor to {godName} at the Temple.");

        await Task.Delay(2500);
    }

    /// <summary>
    /// Sacrifice healing potions to god
    /// </summary>
    private async Task SacrificePotions(string godName)
    {
        if (currentPlayer.Healing <= 0)
        {
            terminal.WriteLine(Loc.Get("temple.no_potions"), "red");
            await Task.Delay(1500);
            return;
        }

        terminal.WriteLine(Loc.Get("temple.have_potions", currentPlayer.Healing), "white");
        var amountStr = await terminal.GetInputAsync(Loc.Get("temple.how_many_sacrifice"));

        if (!int.TryParse(amountStr, out int amount) || amount <= 0)
        {
            terminal.WriteLine(Loc.Get("temple.invalid_amount"), "red");
            await Task.Delay(1000);
            return;
        }

        if (amount > currentPlayer.Healing)
        {
            terminal.WriteLine(Loc.Get("temple.not_enough_potions"), "red");
            await Task.Delay(1000);
            return;
        }

        var confirm = await terminal.GetInputAsync(Loc.Get("temple.confirm_sacrifice_potions", amount, godName));
        if (confirm.ToUpper() != "Y") return;

        long powerGained = amount * 5; // Each potion gives 5 power
        godSystem.ProcessGoldSacrifice(godName, powerGained * 50, currentPlayer.Name2);

        currentPlayer.Healing -= amount;

        terminal.WriteLine("");
        terminal.WriteLine(Loc.Get("temple.potions_evaporate"), "bright_yellow");
        terminal.WriteLine(Loc.Get("temple.god_accepts", godName), "cyan");
        terminal.WriteLine(Loc.Get("temple.divine_power_increased", powerGained), "bright_cyan");

        // Chance for divine healing
        if (amount >= 3 && random.NextDouble() < 0.5)
        {
            currentPlayer.HP = currentPlayer.MaxHP;
            terminal.WriteLine(Loc.Get("temple.god_restores_health", godName), "bright_green");
        }

        // Apply faction effects based on god alignment
        ApplyFactionEffectForSacrifice(godName, Math.Max(1, amount / 2));

        await Task.Delay(2500);
    }

    /// <summary>
    /// Apply faction standing effects based on god alignment
    /// Good gods (Goodness > Darkness) = Light action → Faith standing
    /// Evil gods (Darkness > Goodness) = Dark action → Shadows standing
    /// </summary>
    private void ApplyFactionEffectForSacrifice(string godName, int amount)
    {
        var god = godSystem.GetGod(godName);
        if (god == null) return;

        bool isEvilGod = god.Darkness > god.Goodness;

        if (isEvilGod)
        {
            currentPlayer.DarkNr++;
            currentPlayer.Darkness += amount;
            UsurperRemake.Systems.FactionSystem.Instance.ModifyReputation(UsurperRemake.Systems.Faction.TheShadows, amount);
            terminal.SetColor("bright_magenta");
            terminal.WriteLine(Loc.Get("temple.darkness_notes_devotion", amount));
        }
        else
        {
            currentPlayer.ChivNr++;
            currentPlayer.Chivalry += amount;
            UsurperRemake.Systems.FactionSystem.Instance.ModifyReputation(UsurperRemake.Systems.Faction.TheFaith, amount);
            terminal.SetColor("bright_cyan");
            terminal.WriteLine(Loc.Get("temple.light_notes_devotion", amount));
        }
    }

    /// <summary>
    /// Enhanced desecration that rewards XP and darkness (from Pascal TEMPLE.PAS)
    /// </summary>
    private async Task PerformEnhancedDesecration(God god)
    {
        terminal.WriteLine("");
        terminal.WriteLine("");

        var random = Random.Shared;

        // Desecration flavour text
        string[] desecrationKeys = new[]
        {
            "temple.desecrate_method_smash",
            "temple.desecrate_method_pour",
            "temple.desecrate_method_carve",
            "temple.desecrate_method_fire",
            "temple.desecrate_method_topple"
        };

        terminal.WriteLine(Loc.Get(desecrationKeys[random.Next(desecrationKeys.Length)]), "red");
        await Task.Delay(1500);

        terminal.WriteLine("");
        terminal.WriteLine(Loc.Get("temple.desecrated_altar", god.Name), "bright_red");

        // Process desecration in god system
        godSystem.ProcessAltarDesecration(god.Name, currentPlayer.Name2);

        // Award darkness and XP (from Pascal)
        int darknessGain = random.Next(10, 25);
        long xpGain = (long)(Math.Pow(currentPlayer.Level, 1.5) * 20);

        currentPlayer.Darkness += darknessGain;
        currentPlayer.Experience += xpGain;
        currentPlayer.DarkNr--;
        currentPlayer.DesecrationsToday++;

        terminal.WriteLine("", "white");
        terminal.WriteLine(Loc.Get("temple.darkness_flows", darknessGain), "dark_red");
        terminal.WriteLine(Loc.Get("temple.xp_from_profane", xpGain), "yellow");

        // Divine retribution — escalates with repeated desecrations
        // First desecration: 30% curse chance, mild damage
        // Second desecration: guaranteed curse, heavy damage + stat loss
        double curseChance = currentPlayer.DesecrationsToday >= 2 ? 1.0 : 0.3;
        if (random.NextDouble() < curseChance)
        {
            terminal.WriteLine("", "white");
            terminal.WriteLine(Loc.Get("temple.god_curses", god.Name), "bright_red");

            int curseDamage = random.Next(10, 30 + currentPlayer.Level);
            if (currentPlayer.DesecrationsToday >= 2)
            {
                // Second desecration: much heavier punishment
                curseDamage *= 3;
                terminal.WriteLine(Loc.Get("temple.divine_fury"), "bright_red");

                // Lose a random base stat point
                string[] stats = { "Strength", "Dexterity", "Constitution", "Intelligence", "Wisdom", "Charisma" };
                string lostStat = stats[random.Next(stats.Length)];
                switch (lostStat)
                {
                    case "Strength": currentPlayer.BaseStrength = Math.Max(1, currentPlayer.BaseStrength - 1); break;
                    case "Dexterity": currentPlayer.BaseDexterity = Math.Max(1, currentPlayer.BaseDexterity - 1); break;
                    case "Constitution": currentPlayer.BaseConstitution = Math.Max(1, currentPlayer.BaseConstitution - 1); break;
                    case "Intelligence": currentPlayer.BaseIntelligence = Math.Max(1, currentPlayer.BaseIntelligence - 1); break;
                    case "Wisdom": currentPlayer.BaseWisdom = Math.Max(1, currentPlayer.BaseWisdom - 1); break;
                    case "Charisma": currentPlayer.BaseCharisma = Math.Max(1, currentPlayer.BaseCharisma - 1); break;
                }
                currentPlayer.RecalculateStats();
                terminal.WriteLine(Loc.Get("temple.stat_diminish", lostStat, lostStat), "red");
            }

            currentPlayer.HP = Math.Max(1, currentPlayer.HP - curseDamage);
            terminal.WriteLine(Loc.Get("temple.divine_damage", curseDamage), "red");
        }

        // Generate news
        NewsSystem.Instance.Newsy(true, $"{currentPlayer.Name2} desecrated the altar of {god.Name}! The gods are furious!");

        await Task.Delay(3000);
    }

    /// <summary>
    /// Examine the ancient stones in the temple - discover the Seal of Creation
    /// "Where prayers echo in golden halls, seek the stone that predates the temple itself."
    /// </summary>
    private async Task ExamineAncientStones()
    {
        var story = StoryProgressionSystem.Instance;

        // Already collected
        if (story.CollectedSeals.Contains(UsurperRemake.Systems.SealType.Creation))
        {
            terminal.WriteLine("");
            terminal.WriteLine(Loc.Get("temple.ancient_stones_revealed"), "gray");
            terminal.WriteLine(Loc.Get("temple.remember_truth"), "gray");
            await Task.Delay(1500);
            return;
        }

        terminal.WriteLine("");
        terminal.WriteLine("");
        terminal.SetColor("cyan");
        terminal.WriteLine(Loc.Get("temple.walk_past_altars"));
        terminal.WriteLine(Loc.Get("temple.far_corner"));
        terminal.SetColor("white");
        terminal.WriteLine("");
        await Task.Delay(1500);

        terminal.WriteLine(Loc.Get("temple.massive_stones"));
        terminal.WriteLine(Loc.Get("temple.older_than_temple"));
        terminal.WriteLine(Loc.Get("temple.whose_altar"));
        terminal.WriteLine("");
        await Task.Delay(1500);

        terminal.SetColor("gray");
        terminal.WriteLine(Loc.Get("temple.monks_say"));
        terminal.WriteLine(Loc.Get("temple.before_mortals"));
        terminal.WriteLine(Loc.Get("temple.before_gods"));
        terminal.SetColor("white");
        terminal.WriteLine("");
        await Task.Delay(1500);

        var choice = await terminal.GetInputAsync(Loc.Get("temple.touch_stone"));

        if (choice.ToUpper() != "Y")
        {
            terminal.WriteLine("");
            terminal.WriteLine(Loc.Get("temple.step_back_stones"), "gray");
            terminal.WriteLine(Loc.Get("temple.perhaps_another_time"), "gray");
            await Task.Delay(1000);
            return;
        }

        // Discovery sequence
        terminal.WriteLine("");
        terminal.SetColor("bright_yellow");
        terminal.WriteLine(Loc.Get("temple.hand_touches"));
        terminal.WriteLine("");
        await Task.Delay(1000);

        terminal.SetColor("white");
        terminal.WriteLine(Loc.Get("temple.at_first_nothing"));
        terminal.WriteLine("");
        await Task.Delay(800);

        terminal.WriteLine(Loc.Get("temple.warmth_pulse"));
        terminal.WriteLine("");
        await Task.Delay(800);

        terminal.SetColor("bright_cyan");
        terminal.WriteLine(Loc.Get("temple.stone_glows"));
        terminal.WriteLine(Loc.Get("temple.ancient_symbols"));
        terminal.WriteLine(Loc.Get("temple.older_language"));
        terminal.SetColor("white");
        terminal.WriteLine("");
        await Task.Delay(1500);

        terminal.SetColor("bright_magenta");
        terminal.WriteLine(Loc.Get("temple.voice_speaks"));
        terminal.WriteLine("");
        terminal.SetColor("bright_white");
        terminal.WriteLine(Loc.Get("temple.seek_truth"));
        terminal.WriteLine(Loc.Get("temple.first_seal"));
        terminal.WriteLine(Loc.Get("temple.remember_well"));
        terminal.SetColor("white");
        terminal.WriteLine("");
        await Task.Delay(1500);

        terminal.SetColor("gray");
        await terminal.GetInputAsync(Loc.Get("temple.press_enter_continue"));

        // Collect the seal
        var sealSystem = UsurperRemake.Systems.SevenSealsSystem.Instance;
        await sealSystem.CollectSeal(currentPlayer, UsurperRemake.Systems.SealType.Creation, terminal);

        // Generate news
        NewsSystem.Instance.Newsy(true, $"{currentPlayer.Name2} discovered the Seal of Creation in the Temple!");

        refreshMenu = true;
    }

    /// <summary>
    /// Process daily prayer - grants a temporary blessing once per day
    /// </summary>
    private async Task ProcessDailyPrayer()
    {
        string playerGod = godSystem.GetPlayerGod(currentPlayer.Name2);
        string worshippedImmortal = currentPlayer.WorshippedGod ?? "";

        if (string.IsNullOrEmpty(playerGod) && string.IsNullOrEmpty(worshippedImmortal))
        {
            terminal.WriteLine("");
            terminal.WriteLine(Loc.Get("temple.must_worship_to_pray"), "yellow");
            terminal.WriteLine(Loc.Get("temple.visit_worship_first"), "gray");
            await Task.Delay(2000);
            return;
        }

        if (!UsurperRemake.Systems.DivineBlessingSystem.Instance.CanPrayToday(currentPlayer.Name2))
        {
            terminal.WriteLine("");
            terminal.WriteLine(Loc.Get("temple.already_prayed"), "gray");
            terminal.WriteLine(Loc.Get("temple.return_tomorrow"), "gray");
            await Task.Delay(1500);
            return;
        }

        // === Prayer to an immortal player-god ===
        if (!string.IsNullOrEmpty(worshippedImmortal))
        {
            terminal.WriteLine("");
            terminal.WriteLine("");
            terminal.SetColor("bright_cyan");
            terminal.WriteLine(Loc.Get("temple.kneel_altar", worshippedImmortal));
            await Task.Delay(1000);

            terminal.SetColor("white");
            terminal.WriteLine(Loc.Get("temple.prayers_rise_immortal"));
            await Task.Delay(1000);

            // Mark prayer as done for today (set LastPrayerRealDate for online mode)
            if (UsurperRemake.BBS.DoorMode.IsOnlineMode)
                currentPlayer.LastPrayerRealDate = DateTime.UtcNow;

            // Calculate boon effects and double them for prayer
            var baseEffects = currentPlayer.CachedBoonEffects;
            if (baseEffects != null && baseEffects.HasAnyEffect)
            {
                var boosted = baseEffects.Multiply(GameConfig.GodBoonPrayerMultiplier);

                // Apply as temporary DivineBlessingCombats/Bonus using the strongest buff
                // The prayer buff lasts for a time-based duration simulated as combat count
                int prayerCombats = 20; // ~20 combats ≈ 2 hours of active play
                float prayerBonus = Math.Max(boosted.DamagePercent, boosted.DefensePercent);
                if (prayerBonus > 0)
                {
                    currentPlayer.DivineBlessingCombats = Math.Max(currentPlayer.DivineBlessingCombats, prayerCombats);
                    currentPlayer.DivineBlessingBonus = Math.Max(currentPlayer.DivineBlessingBonus, prayerBonus);
                }

                terminal.SetColor("bright_yellow");
                terminal.WriteLine(Loc.Get("temple.divine_power_surges", worshippedImmortal));
                terminal.WriteLine("");

                // Show boosted boon effects
                var boonLines = DivineBoonRegistry.GetEffectSummaryLines(currentPlayer.DivineBoonConfig);
                // Recalculate from worshipped god's config
                string godConfig = "";
                var backend = SaveSystem.Instance?.Backend as SqlSaveBackend;
                if (backend != null && DoorMode.IsOnlineMode)
                {
                    try { godConfig = await backend.GetGodBoonConfig(worshippedImmortal); } catch { }
                }
                boonLines = DivineBoonRegistry.GetEffectSummaryLines(godConfig);

                terminal.SetColor("bright_green");
                terminal.WriteLine(Loc.Get("temple.prayer_amplifies"));
                foreach (var line in boonLines)
                {
                    terminal.SetColor("white");
                    terminal.WriteLine($"    • {line} (x{GameConfig.GodBoonPrayerMultiplier:0.#})");
                }

                if (prayerBonus > 0)
                {
                    terminal.SetColor("bright_yellow");
                    terminal.WriteLine(Loc.Get("temple.combat_blessing", (int)(prayerBonus * 100), prayerCombats));
                }
            }
            else
            {
                terminal.SetColor("gray");
                terminal.WriteLine(Loc.Get("temple.no_boons_configured", worshippedImmortal));
                terminal.SetColor("white");
                terminal.WriteLine(Loc.Get("temple.prayer_heard_no_boons"));
            }

            // Grant the god experience from the prayer
            if (DoorMode.IsOnlineMode)
            {
                try
                {
                    var prayerBackend = SaveSystem.Instance?.Backend as SqlSaveBackend;
                    if (prayerBackend != null)
                        await prayerBackend.AddGodExperience(worshippedImmortal, 10);
                }
                catch { }
            }

            // Notify the god if online
            if (DoorMode.IsOnlineMode && UsurperRemake.Server.MudServer.Instance != null)
            {
                foreach (var kvp in UsurperRemake.Server.MudServer.Instance.ActiveSessions)
                {
                    var p = kvp.Value.Context?.Engine?.CurrentPlayer;
                    if (p != null && p.DivineName == worshippedImmortal)
                    {
                        kvp.Value.EnqueueMessage(
                            $"\u001b[1;33m  ✦ {currentPlayer.Name2} prayed to you! +10 divine experience. ✦\u001b[0m");
                        break;
                    }
                }
            }

            terminal.WriteLine("");
            await Task.Delay(2000);
            refreshMenu = true;
            return;
        }

        // === Prayer to an NPC god (existing system) ===
        var god = godSystem.GetGod(playerGod);
        if (god == null)
        {
            terminal.WriteLine(Loc.Get("temple.god_no_longer_exists_short"), "red");
            await Task.Delay(1500);
            return;
        }

        terminal.WriteLine("");
        terminal.WriteLine("");
        terminal.SetColor("cyan");
        terminal.WriteLine(Loc.Get("temple.kneel_altar", playerGod));
        await Task.Delay(1000);

        terminal.SetColor("white");
        terminal.WriteLine(Loc.Get("temple.prayers_rise_incense"));
        await Task.Delay(1000);

        // Determine prayer response based on god's alignment
        float alignment = (float)(god.Goodness - god.Darkness) / Math.Max(1, god.Goodness + god.Darkness);

        if (alignment > 0.3f)
        {
            terminal.SetColor("bright_yellow");
            terminal.WriteLine(Loc.Get("temple.warm_light_fills"));
        }
        else if (alignment < -0.3f)
        {
            terminal.SetColor("dark_magenta");
            terminal.WriteLine(Loc.Get("temple.shadows_coil"));
        }
        else
        {
            terminal.SetColor("bright_cyan");
            terminal.WriteLine(Loc.Get("temple.balance_clarity"));
        }
        await Task.Delay(1000);

        // Grant the daily prayer blessing
        var blessing = UsurperRemake.Systems.DivineBlessingSystem.Instance.GrantPrayerBlessing(currentPlayer);

        if (blessing != null)
        {
            terminal.WriteLine("");
            terminal.SetColor("bright_magenta");
            terminal.WriteLine($"*** {blessing.Name} ***");
            terminal.SetColor("white");
            terminal.WriteLine(blessing.Description);
            terminal.WriteLine("");

            if (blessing.DamageBonus > 0)
                terminal.WriteLine(Loc.Get("temple.damage_bonus", blessing.DamageBonus), "red");
            if (blessing.DefenseBonus > 0)
                terminal.WriteLine(Loc.Get("temple.defense_bonus", blessing.DefenseBonus), "cyan");
            if (blessing.XPBonus > 0)
                terminal.WriteLine(Loc.Get("temple.xp_bonus", blessing.XPBonus), "yellow");

            var duration = blessing.ExpiresAt - DateTime.Now;
            terminal.WriteLine(Loc.Get("temple.duration_label", duration.TotalMinutes.ToString("F0")), "gray");

            terminal.WriteLine("");
            terminal.SetColor("white");
            terminal.WriteLine(Loc.Get("temple.blessing_upon_you", playerGod));
        }
        else
        {
            terminal.WriteLine("");
            terminal.WriteLine(Loc.Get("temple.prayers_unanswered"), "gray");
        }

        // Apply small faction effect for daily prayer based on god alignment
        if (alignment > 0.3f)
        {
            // Good god - light action
            UsurperRemake.Systems.FactionSystem.Instance.ModifyReputation(UsurperRemake.Systems.Faction.TheFaith, 1);
            terminal.SetColor("bright_cyan");
            terminal.WriteLine(Loc.Get("temple.faith_standing_gain"));
        }
        else if (alignment < -0.3f)
        {
            // Evil god - dark action
            UsurperRemake.Systems.FactionSystem.Instance.ModifyReputation(UsurperRemake.Systems.Faction.TheShadows, 1);
            terminal.SetColor("bright_magenta");
            terminal.WriteLine(Loc.Get("temple.shadows_standing_gain"));
        }

        await Task.Delay(2000);
        refreshMenu = true;
    }

    #endregion

    #region Mira Companion Recruitment

    /// <summary>
    /// Check if Mira can be met at the temple
    /// </summary>
    private bool CanMeetMira()
    {
        var companionSystem = UsurperRemake.Systems.CompanionSystem.Instance;
        var mira = companionSystem.GetCompanion(UsurperRemake.Systems.CompanionId.Mira);

        // Check requirements
        if (mira == null || mira.IsRecruited || mira.IsDead)
            return false;

        // Level requirement
        if (currentPlayer.Level < mira.RecruitLevel)
            return false;

        // Already completed the encounter (declined)
        var story = StoryProgressionSystem.Instance;
        if (story.HasStoryFlag("mira_temple_encounter_complete"))
            return false;

        return true;
    }

    /// <summary>
    /// Visit the Meditation Chapel - Mira recruitment location
    /// </summary>
    private async Task VisitMeditationChapel()
    {
        var companionSystem = UsurperRemake.Systems.CompanionSystem.Instance;
        var mira = companionSystem.GetCompanion(UsurperRemake.Systems.CompanionId.Mira);

        if (!CanMeetMira())
        {
            terminal.WriteLine("");
            terminal.WriteLine(Loc.Get("temple.meditation_chapel_empty"), "gray");
            terminal.WriteLine(Loc.Get("temple.only_silence"), "gray");
            await Task.Delay(1500);
            refreshMenu = true;
            return;
        }

        terminal.ClearScreen();
        WriteBoxHeader(Loc.Get("temple.meditation"), "bright_green", 66);
        terminal.WriteLine("");
        await Task.Delay(1000);

        terminal.SetColor("white");
        terminal.WriteLine(Loc.Get("temple.step_into_chapel"));
        terminal.WriteLine(Loc.Get("temple.candle_illuminates"));
        terminal.WriteLine("");
        await Task.Delay(1500);

        terminal.SetColor("gray");
        terminal.WriteLine(Loc.Get("temple.faded_robes"));
        terminal.WriteLine(Loc.Get("temple.hands_clasped"));
        terminal.WriteLine(Loc.Get("temple.prays_to_nothing"));
        terminal.WriteLine("");
        await Task.Delay(1500);

        // First dialogue
        terminal.SetColor("cyan");
        terminal.WriteLine(Loc.Get("temple.notices_watching"));
        terminal.WriteLine("");
        terminal.SetColor("bright_cyan");
        terminal.WriteLine($"\"{mira.DialogueHints[0]}\"");
        terminal.WriteLine("");
        await Task.Delay(2000);

        terminal.SetColor("white");
        terminal.WriteLine(Loc.Get("temple.turns_back"));
        terminal.WriteLine("");
        terminal.SetColor("cyan");
        terminal.WriteLine($"\"{mira.DialogueHints[1]}\"");
        terminal.WriteLine("");
        await Task.Delay(2000);

        // Show her details
        terminal.SetColor("yellow");
        terminal.WriteLine(Loc.Get("temple.this_is_companion", mira.Name, mira.Title));
        terminal.WriteLine(Loc.Get("temple.role_label", mira.CombatRole));
        terminal.WriteLine(Loc.Get("temple.abilities_label", string.Join(", ", mira.Abilities)));
        terminal.WriteLine("");

        terminal.SetColor("gray");
        terminal.WriteLine(mira.BackstoryBrief);
        terminal.WriteLine("");
        await Task.Delay(1500);

        terminal.SetColor("bright_yellow");
        if (IsScreenReader)
        {
            terminal.WriteLine(Loc.Get("temple.sr_ask_join"));
            terminal.WriteLine(Loc.Get("temple.sr_talk_past"));
            terminal.WriteLine(Loc.Get("temple.sr_leave_prayers"));
        }
        else
        {
            terminal.WriteLine(Loc.Get("temple.visual_ask_join"));
            terminal.WriteLine(Loc.Get("temple.visual_talk_past"));
            terminal.WriteLine(Loc.Get("temple.visual_leave_prayers"));
        }
        terminal.WriteLine("");

        var choice = await terminal.GetInputAsync(Loc.Get("ui.your_choice"));

        switch (choice.ToUpper())
        {
            case "R":
                await AttemptMiraRecruitment(mira);
                break;

            case "T":
                await TalkToMira(mira);
                break;

            default:
                terminal.SetColor("gray");
                terminal.WriteLine("");
                terminal.WriteLine(Loc.Get("temple.leave_silent_vigil"));
                terminal.WriteLine(Loc.Get("temple.speaks_without_turning"));
                terminal.SetColor("cyan");
                terminal.WriteLine($"\"{mira.DialogueHints[2]}\"");
                break;
        }

        // Mark encounter as complete
        StoryProgressionSystem.Instance.SetStoryFlag("mira_temple_encounter_complete", true);
        await terminal.GetInputAsync(Loc.Get("ui.press_enter"));
        refreshMenu = true;
    }

    /// <summary>
    /// Attempt to recruit Mira
    /// </summary>
    private async Task AttemptMiraRecruitment(UsurperRemake.Systems.Companion mira)
    {
        var companionSystem = UsurperRemake.Systems.CompanionSystem.Instance;

        terminal.WriteLine("");
        terminal.SetColor("white");
        terminal.WriteLine(Loc.Get("temple.dungeons_dangerous"));
        terminal.WriteLine("");
        await Task.Delay(1000);

        terminal.SetColor("cyan");
        terminal.WriteLine(Loc.Get("temple.looks_long_moment", mira.Name));
        terminal.WriteLine(Loc.Get("temple.flickers_in_eyes"));
        terminal.WriteLine(Loc.Get("temple.a_question"));
        terminal.WriteLine("");
        await Task.Delay(1500);

        terminal.SetColor("bright_cyan");
        terminal.WriteLine(Loc.Get("temple.want_me_to_heal"));
        terminal.WriteLine(Loc.Get("temple.always_been_able"));
        terminal.WriteLine(Loc.Get("temple.will_it_matter"));
        terminal.WriteLine("");
        await Task.Delay(1500);

        terminal.SetColor("white");
        terminal.WriteLine(Loc.Get("temple.doesnt_wait"));
        terminal.WriteLine("");
        terminal.SetColor("cyan");
        terminal.WriteLine(Loc.Get("temple.perhaps_help"));
        terminal.WriteLine("");
        await Task.Delay(1000);

        bool success = await companionSystem.RecruitCompanion(
            UsurperRemake.Systems.CompanionId.Mira, currentPlayer, terminal);

        if (success)
        {
            terminal.SetColor("bright_green");
            terminal.WriteLine("");
            terminal.WriteLine(Loc.Get("temple.rises_from_altar", mira.Name));
            terminal.WriteLine(Loc.Get("temple.candle_flickers"));
            terminal.WriteLine("");
            terminal.SetColor("yellow");
            terminal.WriteLine(Loc.Get("temple.companion_death_warning"));

            // Generate news
            NewsSystem.Instance.Newsy(false, $"{currentPlayer.Name2} found {mira.Name} praying at an empty altar in the Temple.");
        }
    }

    /// <summary>
    /// Have a deeper conversation with Mira about her past
    /// </summary>
    private async Task TalkToMira(UsurperRemake.Systems.Companion mira)
    {
        terminal.WriteLine("");
        terminal.SetColor("cyan");
        terminal.WriteLine(Loc.Get("temple.sit_beside"));
        terminal.WriteLine("");
        await Task.Delay(1500);

        terminal.SetColor("white");
        terminal.WriteLine(mira.Description);
        terminal.WriteLine("");
        await Task.Delay(1500);

        terminal.SetColor("cyan");
        terminal.WriteLine(Loc.Get("temple.was_healer_veloura"));
        terminal.WriteLine(Loc.Get("temple.corruption_came"));
        terminal.WriteLine(Loc.Get("temple.escaped_left_faith"));
        terminal.WriteLine("");
        await Task.Delay(2000);

        if (!string.IsNullOrEmpty(mira.PersonalQuestDescription))
        {
            terminal.SetColor("bright_magenta");
            terminal.WriteLine(Loc.Get("temple.personal_quest_label", mira.PersonalQuestName));
            terminal.WriteLine($"\"{mira.PersonalQuestDescription}\"");
            terminal.WriteLine("");
        }

        terminal.SetColor("bright_cyan");
        terminal.WriteLine(Loc.Get("temple.keep_praying"));
        terminal.WriteLine(Loc.Get("temple.to_empty_altar"));
        terminal.WriteLine(Loc.Get("temple.if_i_stop"));
        terminal.WriteLine("");
        await Task.Delay(2000);

        var followUp = await terminal.GetInputAsync(Loc.Get("temple.ask_join_prompt"));
        if (followUp.ToUpper() == "Y")
        {
            await AttemptMiraRecruitment(mira);
        }
        else
        {
            terminal.SetColor("gray");
            terminal.WriteLine("");
            terminal.WriteLine(Loc.Get("temple.squeeze_shoulder"));
            terminal.WriteLine(Loc.Get("temple.perhaps_another_time"));
        }
    }

    #endregion

    #region The Faith Faction Recruitment

    /// <summary>
    /// Show The Faith faction recruitment UI
    /// Meet High Priestess Mirael and potentially join The Faith
    /// </summary>
    private async Task ShowFaithRecruitment()
    {
        var factionSystem = UsurperRemake.Systems.FactionSystem.Instance;

        terminal.ClearScreen();
        WriteBoxHeader(Loc.Get("temple.the_faith"), "bright_yellow");
        terminal.WriteLine("");

        terminal.SetColor("white");
        terminal.WriteLine(Loc.Get("temple.faith_approach"));
        terminal.WriteLine(Loc.Get("temple.faith_devoted"));
        terminal.WriteLine("");
        await Task.Delay(1500);

        terminal.SetColor("bright_cyan");
        terminal.WriteLine(Loc.Get("temple.mirael_intro"));
        terminal.WriteLine(Loc.Get("temple.mirael_watched"));
        terminal.WriteLine("");
        await Task.Delay(1500);

        // Check if already in a faction
        if (factionSystem.PlayerFaction != null)
        {
            terminal.SetColor("yellow");
            terminal.WriteLine(Loc.Get("temple.mirael_studies"));
            terminal.WriteLine("");
            terminal.SetColor("bright_cyan");
            terminal.WriteLine(Loc.Get("temple.already_serve", UsurperRemake.Systems.FactionSystem.Factions[factionSystem.PlayerFaction.Value].Name));
            terminal.WriteLine(Loc.Get("temple.no_divided_loyalties"));
            terminal.WriteLine(Loc.Get("temple.renounce_seek_again"));
            terminal.WriteLine("");
            await terminal.GetInputAsync(Loc.Get("ui.press_enter"));
            refreshMenu = true;
            return;
        }

        terminal.SetColor("white");
        terminal.WriteLine(Loc.Get("temple.sacred_flames"));
        terminal.WriteLine("");
        terminal.SetColor("bright_cyan");
        terminal.WriteLine(Loc.Get("temple.faith_old_gods_pure"));
        terminal.WriteLine(Loc.Get("temple.faith_guided"));
        terminal.WriteLine(Loc.Get("temple.faith_corrupted"));
        terminal.WriteLine(Loc.Get("temple.faith_absorbed"));
        terminal.WriteLine("");
        await Task.Delay(2000);

        terminal.SetColor("cyan");
        terminal.WriteLine(Loc.Get("temple.faith_healed"));
        terminal.WriteLine(Loc.Get("temple.faith_devotion"));
        terminal.WriteLine(Loc.Get("temple.faith_restore"));
        terminal.WriteLine("");
        await Task.Delay(1500);

        // Show faction benefits
        WriteSectionHeader(Loc.Get("temple.faith_benefits"), "bright_yellow");
        terminal.SetColor("white");
        terminal.WriteLine(Loc.Get("temple.benefit_healing"));
        terminal.WriteLine(Loc.Get("temple.benefit_prayers"));
        terminal.WriteLine(Loc.Get("temple.benefit_npcs"));
        terminal.WriteLine(Loc.Get("temple.benefit_standing"));
        terminal.WriteLine("");

        // Check requirements
        var (canJoin, reason) = factionSystem.CanJoinFaction(UsurperRemake.Systems.Faction.TheFaith, currentPlayer);

        if (!canJoin)
        {
            WriteSectionHeader(Loc.Get("temple.requirements_not_met"), "red");
            terminal.SetColor("yellow");
            terminal.WriteLine(reason);
            terminal.WriteLine("");
            terminal.SetColor("gray");
            terminal.WriteLine(Loc.Get("temple.faith_requires"));
            terminal.WriteLine(Loc.Get("temple.faith_req_level"));
            terminal.WriteLine(Loc.Get("temple.faith_req_standing"));
            terminal.WriteLine(Loc.Get("temple.faith_your_standing", factionSystem.FactionStanding[UsurperRemake.Systems.Faction.TheFaith]));
            terminal.WriteLine("");
            terminal.SetColor("bright_cyan");
            terminal.WriteLine(Loc.Get("temple.mirael_return"));
            terminal.WriteLine(Loc.Get("temple.mirael_offerings"));
            await terminal.GetInputAsync(Loc.Get("ui.press_enter"));
            refreshMenu = true;
            return;
        }

        // Can join - offer the choice
        WriteSectionHeader(Loc.Get("temple.requirements_met"), "bright_green");
        terminal.SetColor("white");
        terminal.WriteLine(Loc.Get("temple.mirael_extends"));
        terminal.WriteLine("");
        terminal.SetColor("bright_cyan");
        terminal.WriteLine(Loc.Get("temple.mirael_noted"));
        terminal.WriteLine(Loc.Get("temple.mirael_oath"));
        terminal.WriteLine("");
        terminal.SetColor("yellow");
        terminal.WriteLine(Loc.Get("temple.join_warning"));
        terminal.WriteLine(Loc.Get("temple.join_lock_out"));
        terminal.WriteLine(Loc.Get("temple.join_decrease"));
        terminal.WriteLine("");

        var choice = await terminal.GetInputAsync(Loc.Get("temple.join_prompt"));

        if (choice.ToUpper() == "Y")
        {
            await PerformFaithOath(factionSystem);
        }
        else
        {
            terminal.WriteLine("");
            terminal.SetColor("cyan");
            terminal.WriteLine(Loc.Get("temple.mirael_understanding"));
            terminal.WriteLine(Loc.Get("temple.mirael_not_for_everyone"));
            terminal.WriteLine(Loc.Get("temple.mirael_doors_open"));
        }

        await terminal.GetInputAsync(Loc.Get("ui.press_enter"));
        refreshMenu = true;
    }

    /// <summary>
    /// Perform the oath ceremony to join The Faith
    /// </summary>
    private async Task PerformFaithOath(UsurperRemake.Systems.FactionSystem factionSystem)
    {
        terminal.ClearScreen();
        WriteBoxHeader(Loc.Get("temple.sacred_oath"), "bright_yellow");
        terminal.WriteLine("");

        terminal.SetColor("white");
        terminal.WriteLine(Loc.Get("temple.kneel_sacred_flames"));
        terminal.WriteLine(Loc.Get("temple.mirael_stands"));
        terminal.WriteLine("");
        await Task.Delay(1500);

        terminal.SetColor("bright_cyan");
        terminal.WriteLine(Loc.Get("temple.repeat_after_me"));
        terminal.WriteLine("");
        await Task.Delay(1000);

        terminal.SetColor("yellow");
        terminal.WriteLine(Loc.Get("temple.oath_line1"));
        await Task.Delay(1200);
        terminal.WriteLine(Loc.Get("temple.oath_line2"));
        await Task.Delay(1200);
        terminal.WriteLine(Loc.Get("temple.oath_line3"));
        await Task.Delay(1200);
        terminal.WriteLine(Loc.Get("temple.oath_line4"));
        terminal.WriteLine("");
        await Task.Delay(1500);

        terminal.SetColor("white");
        terminal.WriteLine(Loc.Get("temple.flames_flare"));
        terminal.WriteLine(Loc.Get("temple.profound_peace"));
        terminal.WriteLine("");
        await Task.Delay(1500);

        // Actually join the faction
        factionSystem.JoinFaction(UsurperRemake.Systems.Faction.TheFaith, currentPlayer);

        WriteBoxHeader(Loc.Get("temple.joined_faith"), "bright_green");
        terminal.WriteLine("");

        terminal.SetColor("bright_cyan");
        terminal.WriteLine(Loc.Get("temple.mirael_welcome"));
        terminal.WriteLine(Loc.Get("temple.mirael_never_waver"));
        terminal.WriteLine("");

        terminal.SetColor("white");
        terminal.WriteLine(Loc.Get("temple.as_member_receive"));
        terminal.SetColor("bright_green");
        terminal.WriteLine(Loc.Get("temple.receive_discount"));
        terminal.WriteLine(Loc.Get("temple.receive_recognition"));
        terminal.WriteLine(Loc.Get("temple.receive_blessings"));
        terminal.WriteLine("");

        // Generate news
        NewsSystem.Instance.Newsy(true, $"{currentPlayer.Name2} has joined The Faith and sworn the Sacred Oath!");

        // Log to debug
        UsurperRemake.Systems.DebugLogger.Instance.LogInfo("FACTION", $"{currentPlayer.Name2} joined The Faith");
    }

    #endregion

    #region Inner Sanctum

    private async Task VisitInnerSanctum()
    {
        if (FactionSystem.Instance?.HasTempleAccess() != true)
        {
            terminal.SetColor("red");
            terminal.WriteLine("\n" + Loc.Get("temple.sanctum_sealed"));
            terminal.WriteLine(Loc.Get("temple.sanctum_faith_only"));
            await Task.Delay(2000);
            return;
        }

        bool alreadyMeditated;
        if (DoorMode.IsOnlineMode)
        {
            var boundary = DailySystemManager.GetCurrentResetBoundary();
            alreadyMeditated = currentPlayer.LastInnerSanctumRealDate >= boundary;
        }
        else
        {
            int today = DailySystemManager.Instance?.CurrentDay ?? 0;
            alreadyMeditated = currentPlayer.InnerSanctumLastDay >= today;
        }
        if (alreadyMeditated)
        {
            terminal.SetColor("gray");
            terminal.WriteLine("\n" + Loc.Get("temple.sanctum_already_meditated"));
            terminal.WriteLine(Loc.Get("temple.sanctum_ready_tomorrow"));
            await Task.Delay(2000);
            return;
        }

        terminal.ClearScreen();
        WriteBoxHeader(Loc.Get("temple.inner_sanctum"), "bright_cyan", 66);
        terminal.WriteLine("");

        terminal.SetColor("gray");
        terminal.WriteLine(Loc.Get("temple.sanctum_stillness"));
        terminal.WriteLine(Loc.Get("temple.sanctum_runes"));
        terminal.SetColor("yellow");
        terminal.WriteLine("\n" + Loc.Get("temple.sanctum_cost", GameConfig.InnerSanctumCost));
        terminal.SetColor("cyan");
        terminal.WriteLine(Loc.Get("temple.sanctum_grant"));
        terminal.WriteLine("");
        terminal.SetColor("yellow");
        terminal.WriteLine(Loc.Get("temple.sanctum_gold_label", currentPlayer.Gold.ToString("N0")));
        terminal.WriteLine("");

        var input = await terminal.GetInput(Loc.Get("temple.sanctum_enter_prompt"));
        if (input.Trim().ToUpper() != "Y")
            return;

        if (currentPlayer.Gold < GameConfig.InnerSanctumCost)
        {
            terminal.SetColor("red");
            terminal.WriteLine(Loc.Get("temple.sanctum_cant_afford"));
            await Task.Delay(2000);
            return;
        }

        currentPlayer.Gold -= GameConfig.InnerSanctumCost;
        currentPlayer.Statistics?.RecordGoldSpent(GameConfig.InnerSanctumCost);
        if (DoorMode.IsOnlineMode)
            currentPlayer.LastInnerSanctumRealDate = DateTime.UtcNow;
        else
            currentPlayer.InnerSanctumLastDay = DailySystemManager.Instance?.CurrentDay ?? 0;

        terminal.SetColor("gray");
        terminal.WriteLine("\n" + Loc.Get("temple.sanctum_kneel"));
        await Task.Delay(2000);
        terminal.SetColor("bright_cyan");
        terminal.WriteLine(Loc.Get("temple.sanctum_warmth"));
        await Task.Delay(1500);

        // Grant +1 to a random stat
        var rng = Random.Shared;
        string statName;
        switch (rng.Next(9))
        {
            case 0: currentPlayer.Strength += 1; statName = "Strength"; break;
            case 1: currentPlayer.Defence += 1; statName = "Defence"; break;
            case 2: currentPlayer.Stamina += 1; statName = "Stamina"; break;
            case 3: currentPlayer.Agility += 1; statName = "Agility"; break;
            case 4: currentPlayer.Charisma += 1; statName = "Charisma"; break;
            case 5: currentPlayer.Dexterity += 1; statName = "Dexterity"; break;
            case 6: currentPlayer.Wisdom += 1; statName = "Wisdom"; break;
            case 7: currentPlayer.Intelligence += 1; statName = "Intelligence"; break;
            case 8: currentPlayer.Constitution += 1; statName = "Constitution"; break;
            default: currentPlayer.Strength += 1; statName = "Strength"; break;
        }

        terminal.SetColor("bright_green");
        terminal.WriteLine("\n" + Loc.Get("temple.sanctum_stat_gain", statName));
        terminal.SetColor("gray");
        terminal.WriteLine(Loc.Get("temple.sanctum_power_mark"));
        terminal.WriteLine("");

        await terminal.PressAnyKey();
    }

    #endregion

    #region Immortal God Worship (v0.45.0)

    /// <summary>Get all ascended immortal gods (from NPCSpawnSystem or online DB)</summary>
    private async Task<List<ImmortalGodInfo>> GetImmortalGodsAsync()
    {
        var gods = new List<ImmortalGodInfo>();

        // In MUD mode, query all immortals from the DB
        var backend = SaveSystem.Instance?.Backend as SqlSaveBackend;
        if (backend != null && DoorMode.IsOnlineMode)
        {
            try
            {
                var immortals = await backend.GetImmortalPlayers();
                foreach (var god in immortals)
                {
                    // Don't show the player's own god entry if they ARE the immortal.
                    // Alt characters on the same account (mortal) should still see and worship their god.
                    if (currentPlayer.IsImmortal && !string.IsNullOrEmpty(currentPlayer.DivineName)
                        && currentPlayer.DivineName.Equals(god.DivineName, StringComparison.OrdinalIgnoreCase))
                        continue;

                    gods.Add(new ImmortalGodInfo
                    {
                        DivineName = god.DivineName,
                        GodLevel = god.GodLevel,
                        GodAlignment = god.GodAlignment,
                        Believers = PantheonLocation.CountBelievers(god.DivineName),
                        IsOnline = god.IsOnline,
                        Username = god.Username,
                        DivineBoonConfig = god.DivineBoonConfig ?? ""
                    });
                }
            }
            catch { /* DB unavailable */ }
        }
        else
        {
            // Single-player: only the current player (if they've ascended, which shouldn't happen in Temple)
            var player = GameEngine.Instance?.CurrentPlayer;
            if (player != null && player.IsImmortal && !string.IsNullOrEmpty(player.DivineName))
            {
                gods.Add(new ImmortalGodInfo
                {
                    DivineName = player.DivineName,
                    GodLevel = player.GodLevel,
                    GodAlignment = player.GodAlignment,
                    Believers = PantheonLocation.CountBelievers(player.DivineName),
                    IsOnline = true
                });
            }
        }

        return gods;
    }

    private async Task WorshipImmortalGod()
    {
        var gods = await GetImmortalGodsAsync();
        if (gods.Count == 0)
        {
            terminal.WriteLine(Loc.Get("temple.no_ascended_gods"), "gray");
            await terminal.PressAnyKey();
            return;
        }

        terminal.ClearScreen();
        WriteBoxHeader(Loc.Get("temple.altars_ascended"), "bright_yellow");
        terminal.WriteLine("");

        for (int i = 0; i < gods.Count; i++)
        {
            var god = gods[i];
            string title = PantheonLocation.GetGodTitle(god.GodLevel);
            terminal.SetColor("white");
            terminal.Write($"  {i + 1}. ");
            terminal.SetColor("bright_yellow");
            terminal.Write($"{god.DivineName}");
            terminal.SetColor("gray");
            terminal.Write($" the {title}  ");
            terminal.SetColor(god.IsOnline ? "bright_green" : "gray");
            terminal.Write(god.IsOnline ? "[ONLINE]" : "[OFFLINE]");
            terminal.SetColor("white");
            terminal.WriteLine($"  ({god.GodAlignment}, {god.Believers} believers)");

            // Show boon description
            string desc = DivineBoonRegistry.GenerateDescription(god.DivineBoonConfig, god.GodAlignment);
            terminal.SetColor("gray");
            terminal.WriteLine($"     {desc}");

            // Show individual boons
            var boonLines = DivineBoonRegistry.GetEffectSummaryLines(god.DivineBoonConfig);
            foreach (var line in boonLines)
            {
                terminal.SetColor("darkgray");
                terminal.WriteLine($"     • {line}");
            }

            if (i < gods.Count - 1) terminal.WriteLine("");
        }

        terminal.WriteLine("");
        string input = await terminal.GetInputAsync(Loc.Get("temple.worship_which"));
        if (!int.TryParse(input, out int idx) || idx < 1 || idx > gods.Count) return;

        var chosen = gods[idx - 1];

        // Check if already following this god
        if (currentPlayer.WorshippedGod == chosen.DivineName)
        {
            terminal.WriteLine(Loc.Get("temple.already_follow", chosen.DivineName), "gray");
            await terminal.PressAnyKey();
            return;
        }

        // If already following another player god, warn
        if (!string.IsNullOrEmpty(currentPlayer.WorshippedGod))
        {
            terminal.WriteLine(Loc.Get("temple.currently_follow", currentPlayer.WorshippedGod), "yellow");
            string confirm = await terminal.GetInputAsync(Loc.Get("temple.abandon_prompt"));
            if (confirm.Trim().ToUpper() != "Y") return;
        }

        // If following an NPC god, renounce them — the elder god may punish apostasy
        string oldNpcGod = godSystem.GetPlayerGod(currentPlayer.Name2);
        if (!string.IsNullOrEmpty(oldNpcGod))
        {
            terminal.WriteLine(Loc.Get("temple.currently_worship_elder", oldNpcGod), "yellow");
            string confirm = await terminal.GetInputAsync(Loc.Get("temple.abandon_elder_prompt", oldNpcGod));
            if (confirm.Trim().ToUpper() != "Y") return;

            godSystem.SetPlayerGod(currentPlayer.Name2, "");
            terminal.WriteLine("");
            terminal.SetColor("red");
            terminal.WriteLine(Loc.Get("temple.renounce_elder", oldNpcGod));

            // Divine retribution — the elder god may smite the apostate
            var rng = Random.Shared;
            if (rng.NextDouble() < 0.6) // 60% chance of punishment
            {
                long smiteDamage = Math.Max(1, (long)(currentPlayer.MaxHP * (0.1 + rng.NextDouble() * 0.2)));
                currentPlayer.HP = Math.Max(1, currentPlayer.HP - smiteDamage);
                terminal.SetColor("bright_red");
                terminal.WriteLine(Loc.Get("temple.elder_strikes", oldNpcGod));
                terminal.SetColor("white");
                terminal.WriteLine(Loc.Get("temple.elder_damage", smiteDamage, currentPlayer.HP, currentPlayer.MaxHP));
                await Task.Delay(1500);
            }
            else
            {
                terminal.SetColor("gray");
                terminal.WriteLine(Loc.Get("temple.elder_watches", oldNpcGod));
                await Task.Delay(1000);
            }
        }

        currentPlayer.WorshippedGod = chosen.DivineName;

        // Cache boon effects from the chosen god
        currentPlayer.CachedBoonEffects = DivineBoonRegistry.CalculateEffects(chosen.DivineBoonConfig);

        terminal.WriteLine("");
        terminal.SetColor("bright_cyan");
        terminal.WriteLine(Loc.Get("temple.kneel_immortal", chosen.DivineName));
        terminal.SetColor("white");
        terminal.WriteLine(Loc.Get("temple.divine_presence"));

        // Show boon effects the player will receive
        var effects = currentPlayer.CachedBoonEffects;
        if (effects != null && effects.HasAnyEffect)
        {
            terminal.SetColor("bright_green");
            terminal.WriteLine(Loc.Get("temple.divine_favors_flow"));
            foreach (var line in DivineBoonRegistry.GetEffectSummaryLines(chosen.DivineBoonConfig))
            {
                terminal.SetColor("white");
                terminal.WriteLine($"    • {line}");
            }
        }

        // Notify the god if online
        if (DoorMode.IsOnlineMode && chosen.IsOnline && UsurperRemake.Server.MudServer.Instance != null)
        {
            UsurperRemake.Server.MudServer.Instance.SendToPlayer(chosen.Username,
                $"\u001b[1;33m  ✦ A mortal named {currentPlayer.Name2} now worships you! ✦\u001b[0m");
        }

        // Persist worship atomically to DB so believer counts update immediately
        if (DoorMode.IsOnlineMode)
        {
            try
            {
                var backend = SaveSystem.Instance?.Backend as SqlSaveBackend;
                var sessionUsername = UsurperRemake.Server.SessionContext.Current?.Username;
                if (backend != null && !string.IsNullOrEmpty(sessionUsername))
                    await backend.SetPlayerWorshippedGod(sessionUsername, chosen.DivineName);
            }
            catch { }
        }

        terminal.WriteLine("");
        await terminal.PressAnyKey();
    }

    private async Task SacrificeToImmortalGod()
    {
        if (string.IsNullOrEmpty(currentPlayer.WorshippedGod))
        {
            terminal.WriteLine(IsScreenReader
                ? Loc.Get("temple.must_worship_immortal_sr")
                : Loc.Get("temple.must_worship_immortal"), "gray");
            await terminal.PressAnyKey();
            return;
        }

        terminal.WriteLine("");
        terminal.SetColor("bright_yellow");
        terminal.WriteLine(Loc.Get("temple.sacrifice_gold_to", currentPlayer.WorshippedGod));
        terminal.SetColor("white");
        terminal.WriteLine(Loc.Get("temple.gold_on_hand", currentPlayer.Gold.ToString("N0")));
        terminal.WriteLine("");

        string input = await terminal.GetInputAsync(Loc.Get("temple.amount_to_sacrifice"));
        if (!long.TryParse(input, out long amount) || amount <= 0) return;

        if (amount > currentPlayer.Gold)
        {
            terminal.WriteLine(Loc.Get("temple.not_enough_gold_sacrifice"), "red");
            await terminal.PressAnyKey();
            return;
        }

        currentPlayer.Gold -= amount;
        int power = PantheonLocation.GetSacrificePower(amount);

        // Deliver experience to the god
        bool delivered = false;

        // Check if the god is online (in-memory delivery)
        if (DoorMode.IsOnlineMode && UsurperRemake.Server.MudServer.Instance != null)
        {
            foreach (var kvp in UsurperRemake.Server.MudServer.Instance.ActiveSessions)
            {
                var godPlayer = kvp.Value.Context?.Engine?.CurrentPlayer;
                if (godPlayer != null && godPlayer.IsImmortal && godPlayer.DivineName == currentPlayer.WorshippedGod)
                {
                    godPlayer.GodExperience += power;
                    kvp.Value.EnqueueMessage(
                        $"\u001b[1;33m  ✦ {currentPlayer.Name2} sacrificed {amount:N0} gold at your altar! +{power} divine experience. ✦\u001b[0m");
                    delivered = true;
                    break;
                }
            }
        }

        // Offline god: atomic DB update + message
        if (!delivered && DoorMode.IsOnlineMode)
        {
            var backend = SaveSystem.Instance?.Backend as SqlSaveBackend;
            if (backend != null)
            {
                try
                {
                    await backend.AddGodExperience(currentPlayer.WorshippedGod, power);
                    // Find the god's username for the message
                    var immortals = await backend.GetImmortalPlayers();
                    var godInfo = immortals.FirstOrDefault(g => g.DivineName == currentPlayer.WorshippedGod);
                    if (godInfo != null)
                    {
                        await backend.SendMessage("Temple", godInfo.Username, "divine",
                            $"{currentPlayer.Name2} sacrificed {amount:N0} gold at your altar! +{power} divine experience.");
                    }
                    delivered = true;
                }
                catch { /* DB unavailable */ }
            }
        }

        // Single-player fallback
        if (!delivered)
        {
            var player = GameEngine.Instance?.CurrentPlayer;
            if (player != null && player.IsImmortal && player.DivineName == currentPlayer.WorshippedGod)
            {
                player.GodExperience += power;
            }
        }

        terminal.WriteLine("");
        terminal.SetColor("bright_yellow");
        terminal.WriteLine(Loc.Get("temple.gold_upon_altar", amount.ToString("N0"), currentPlayer.WorshippedGod));
        terminal.SetColor("bright_cyan");
        terminal.WriteLine(Loc.Get("temple.offering_burns", power));

        // Small blessing for the worshipper
        if (power >= 3)
        {
            terminal.SetColor("bright_green");
            terminal.WriteLine(Loc.Get("temple.warm_glow"));
        }

        terminal.WriteLine("");
        await terminal.PressAnyKey();
    }

    private async Task LeaveImmortalFaith()
    {
        if (string.IsNullOrEmpty(currentPlayer.WorshippedGod))
        {
            terminal.WriteLine(Loc.Get("temple.no_immortal_god"), "gray");
            await terminal.PressAnyKey();
            return;
        }

        string godName = currentPlayer.WorshippedGod;
        string confirm = await terminal.GetInputAsync(Loc.Get("temple.abandon_faith_prompt", godName));
        if (confirm.Trim().ToUpper() != "Y") return;

        currentPlayer.WorshippedGod = "";
        terminal.WriteLine("");
        terminal.SetColor("yellow");
        terminal.WriteLine(Loc.Get("temple.turn_away", godName));
        terminal.SetColor("gray");
        terminal.WriteLine(Loc.Get("temple.without_patronage"));

        // Persist atomically to DB so believer counts update immediately
        if (DoorMode.IsOnlineMode)
        {
            try
            {
                var backend = SaveSystem.Instance?.Backend as SqlSaveBackend;
                var sessionUsername = UsurperRemake.Server.SessionContext.Current?.Username;
                if (backend != null && !string.IsNullOrEmpty(sessionUsername))
                    await backend.SetPlayerWorshippedGod(sessionUsername, "");
            }
            catch { }
        }

        terminal.WriteLine("");
        await terminal.PressAnyKey();
    }

    private class ImmortalGodInfo
    {
        public string DivineName { get; set; } = "";
        public int GodLevel { get; set; }
        public string GodAlignment { get; set; } = "";
        public int Believers { get; set; }
        public bool IsOnline { get; set; }
        public string Username { get; set; } = "";
        public string DivineBoonConfig { get; set; } = "";
    }

    #endregion
}
