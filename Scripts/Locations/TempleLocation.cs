using UsurperRemake.Utils;
using UsurperRemake.Systems;
using UsurperRemake.BBS;
using UsurperRemake.Data;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Godot;

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
    private new readonly TerminalEmulator terminal;
    private readonly LocationManager locationManager;
    private readonly GodSystem godSystem;
    private bool refreshMenu = true;
    private new Character currentPlayer;
    private Random random = new Random();

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
        this.terminal = terminal;
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
        // Set the terminal for this location
        // Note: We use our own stored terminal from constructor, but update if needed
        if (term != null && term != terminal)
        {
            // Use the provided terminal for consistency
        }

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
                
                var choice = await terminal.GetInputAsync("Temple area (? for menu) :");
                
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

                    case GameConfig.TempleMenuReturn: // "R"
                        exitLocation = true;
                        break;
                        
                    default:
                        terminal.WriteLine("Invalid choice. Press ? for menu.", "red");
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
        terminal.WriteLine("You enter the Temple Area", "yellow");
        terminal.WriteLine("");
        
        string playerGod = godSystem.GetPlayerGod(currentPlayer.Name2);
        if (!string.IsNullOrEmpty(playerGod))
        {
            terminal.WriteLine($"You worship {playerGod}.", "cyan");
        }
        else
        {
            terminal.WriteLine("You are not a believer.", "gray");
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
        terminal.SetColor("bright_cyan");
        terminal.WriteLine("╔═════════════════════════════════════════════════════════════════════════════╗");
        terminal.SetColor("bright_yellow");
        terminal.WriteLine("║                           TEMPLE OF THE GODS                                ║");
        terminal.SetColor("bright_cyan");
        terminal.WriteLine("╚═════════════════════════════════════════════════════════════════════════════╝");
        terminal.WriteLine("");

        terminal.SetColor("white");
        terminal.WriteLine("The Temple area is crowded with monks, preachers and");
        terminal.WriteLine("processions of priests on their way to the altars.");
        terminal.WriteLine("The doomsday prophets are trying to get your attention.");

        // Hint at ancient stones if seal not collected
        var storyForHint = StoryProgressionSystem.Instance;
        if (!storyForHint.CollectedSeals.Contains(UsurperRemake.Systems.SealType.Creation))
        {
            terminal.WriteLine("");
            terminal.SetColor("gray");
            terminal.WriteLine("In the far corner, ancient stones form the temple's foundation...");
            terminal.WriteLine("They seem older than any altar here.");
        }

        terminal.WriteLine("");

        string playerGod = godSystem.GetPlayerGod(currentPlayer.Name2);
        if (!string.IsNullOrEmpty(playerGod))
        {
            terminal.WriteLine($"You worship {playerGod}.", "cyan");
        }
        else
        {
            terminal.WriteLine("You are not a believer.", "gray");
        }
        terminal.WriteLine("");
        
        // Main menu options - standardized format
        terminal.SetColor("cyan");
        terminal.WriteLine("Temple Services:");
        terminal.WriteLine("");

        // Row 1
        terminal.SetColor("darkgray");
        terminal.Write(" [");
        terminal.SetColor("bright_yellow");
        terminal.Write("W");
        terminal.SetColor("darkgray");
        terminal.Write("]");
        terminal.SetColor("white");
        terminal.Write("orship            ");

        terminal.SetColor("darkgray");
        terminal.Write("[");
        terminal.SetColor("bright_yellow");
        terminal.Write("D");
        terminal.SetColor("darkgray");
        terminal.Write("]");
        terminal.SetColor("white");
        terminal.Write("esecrate altar    ");

        terminal.SetColor("darkgray");
        terminal.Write("[");
        terminal.SetColor("bright_yellow");
        terminal.Write("H");
        terminal.SetColor("darkgray");
        terminal.Write("]");
        terminal.SetColor("white");
        terminal.WriteLine("oly News");

        // Row 2
        terminal.SetColor("darkgray");
        terminal.Write(" [");
        terminal.SetColor("bright_yellow");
        terminal.Write("A");
        terminal.SetColor("darkgray");
        terminal.Write("]");
        terminal.SetColor("white");
        terminal.Write("ltars             ");

        terminal.SetColor("darkgray");
        terminal.Write("[");
        terminal.SetColor("bright_yellow");
        terminal.Write("C");
        terminal.SetColor("darkgray");
        terminal.Write("]");
        terminal.SetColor("white");
        terminal.Write("ontribute         ");

        terminal.SetColor("darkgray");
        terminal.Write("[");
        terminal.SetColor("bright_yellow");
        terminal.Write("I");
        terminal.SetColor("darkgray");
        terminal.Write("]");
        terminal.SetColor("white");
        terminal.WriteLine("tem Sacrifice");

        // Row 3
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
        terminal.Write("G");
        terminal.SetColor("darkgray");
        terminal.Write("]");
        terminal.SetColor("white");
        terminal.Write("od ranking        ");

        terminal.SetColor("darkgray");
        terminal.Write("[");
        terminal.SetColor("bright_yellow");
        terminal.Write("P");
        terminal.SetColor("darkgray");
        terminal.Write("]");
        terminal.SetColor("white");
        terminal.Write("rophecies         ");

        // Daily prayer option - show if player worships a god
        string prayerGod = godSystem.GetPlayerGod(currentPlayer.Name2);
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
                terminal.WriteLine(" Pray");
            }
            else
            {
                terminal.SetColor("gray");
                terminal.WriteLine("(Prayed today)");
            }
        }
        else
        {
            terminal.WriteLine("");
        }

        // Ancient stones option - only show if Seal of Creation not collected
        var story = StoryProgressionSystem.Instance;
        if (!story.CollectedSeals.Contains(UsurperRemake.Systems.SealType.Creation))
        {
            terminal.SetColor("darkgray");
            terminal.Write(" [");
            terminal.SetColor("bright_yellow");
            terminal.Write("E");
            terminal.SetColor("darkgray");
            terminal.Write("]");
            terminal.SetColor("white");
            terminal.Write("xamine Stones     ");
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
            terminal.WriteLine("he Deep Temple");
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
            terminal.Write("editation Chapel ");
            terminal.SetColor("gray");
            terminal.WriteLine("(someone prays alone...)");
        }

        // The Faith faction option - only show if player isn't already a member
        var factionSystem = UsurperRemake.Systems.FactionSystem.Instance;
        if (factionSystem.PlayerFaction != UsurperRemake.Systems.Faction.TheFaith)
        {
            terminal.SetColor("darkgray");
            terminal.Write(" [");
            terminal.SetColor("bright_yellow");
            terminal.Write("F");
            terminal.SetColor("darkgray");
            terminal.Write("]");
            terminal.SetColor("bright_yellow");
            terminal.Write("The Faith ");
            if (factionSystem.PlayerFaction == null)
            {
                terminal.SetColor("gray");
                terminal.WriteLine("(seek the High Priestess...)");
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
            terminal.WriteLine(" You are a member of The Faith.");
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
            terminal.Write(" Inner Sanctum");
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
                terminal.WriteLine("  (meditated today)");
            }
            else
            {
                terminal.SetColor("bright_green");
                terminal.WriteLine($"  ({GameConfig.InnerSanctumCost}g)");
            }
        }

        terminal.SetColor("darkgray");
        terminal.Write(" [");
        terminal.SetColor("bright_yellow");
        terminal.Write("R");
        terminal.SetColor("darkgray");
        terminal.Write("]");
        terminal.SetColor("white");
        terminal.WriteLine("eturn");
        terminal.WriteLine("");
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
        
        if (!string.IsNullOrEmpty(currentGod))
        {
            terminal.WriteLine($"You currently worship {currentGod}.", "white");
            
            var choice = await terminal.GetInputAsync($"Have you lost your faith in {currentGod}? (Y/N) ");
            if (choice.ToUpper() == "Y")
            {
                // Abandon faith
                terminal.WriteLine("");
                terminal.WriteLine($"You don't believe in {currentGod} anymore.", "white");
                terminal.WriteLine($"{currentGod}'s powers diminish...", "yellow");
                
                var noteChoice = await terminal.GetInputAsync($"Send a note to {currentGod}? (Y/N) ");
                string note = "";
                if (noteChoice.ToUpper() == "Y")
                {
                    note = await terminal.GetInputAsync("Note: ");
                    terminal.WriteLine("Done!", "green");
                }
                
                if (string.IsNullOrEmpty(note))
                {
                    var randomNotes = new[] 
                    {
                        "You are not my God!",
                        "farewell..",
                        "never again will I follow you!"
                    };
                    note = randomNotes[new Random().Next(randomNotes.Length)];
                }
                
                // Remove from god system
                godSystem.SetPlayerGod(currentPlayer.Name2, "");
                
                // In Pascal, this would send mail to the god and news
                terminal.WriteLine("");
                terminal.WriteLine("You are no longer a believer.", "yellow");
            }
            else
            {
                terminal.WriteLine("Good for you. The gods don't take too kindly on apostates.", "green");
                goAhead = false;
            }
        }
        
        if (goAhead)
        {
            var selectedGod = await SelectGod("Choose a god to worship");

            if (selectedGod != null)
            {
                terminal.WriteLine("");
                terminal.WriteLine($"You raise your hands and pray to the almighty {selectedGod.Name}", "white");
                terminal.Write("for forgiveness", "white");
                
                // Delay dots animation (Pascal Make_Delay_Dots)
                for (int i = 0; i < 15; i++)
                {
                    terminal.Write(".", "white");
                    await Task.Delay(300);
                }
                terminal.WriteLine("");
                
                terminal.WriteLine($"You are now a believer in {selectedGod.Name}!", "yellow");
                
                // Set in god system
                godSystem.SetPlayerGod(currentPlayer.Name2, selectedGod.Name);
                
                // In Pascal, this would send mail to god and news
                terminal.WriteLine("");
                terminal.WriteLine("The gods smile upon your faith!", "cyan");
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
        
        if (currentPlayer.DarkNr < 1)
        {
            terminal.WriteLine("You don't have any evil deeds left!", "red");
            await Task.Delay(2000);
            return;
        }
        
        var choice = await terminal.GetInputAsync("Do you really want to upset the gods? (Y/N) ");
        if (choice.ToUpper() != "Y")
        {
            terminal.WriteLine("Good for you!", "green");
            await Task.Delay(1000);
            return;
        }
        
        var selectedGod = await SelectGod("Select god to desecrate altar", requireConfirmation: false);
        if (selectedGod == null) return;

        string playerGod = godSystem.GetPlayerGod(currentPlayer.Name2);
        if (!string.IsNullOrEmpty(playerGod) && playerGod == selectedGod.Name)
        {
            terminal.WriteLine("");
            terminal.WriteLine("You are not allowed to abuse your own God!", "red");
            await Task.Delay(2000);
            return;
        }

        terminal.SetColor("red");
        var confirmChoice = await terminal.GetInputAsync($"Are you SURE you want to desecrate {selectedGod.Name}'s altar? (Y/N) ");
        if (confirmChoice.ToUpper() != "Y")
        {
            terminal.WriteLine("Wise choice. The gods remember those who show respect.", "gray");
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
            terminal.WriteLine("You don't have any good deeds left!", "red");
            await Task.Delay(2000);
            return;
        }
        
        var selectedGod = await SelectGod("Who shall receive your gift?", requireConfirmation: false);
        if (selectedGod == null) return;

        string playerGod = godSystem.GetPlayerGod(currentPlayer.Name2);
        bool wrongGod = false;
        bool goAhead = true;
        
        if (!string.IsNullOrEmpty(playerGod) && playerGod != selectedGod.Name)
        {
            terminal.WriteLine("");
            terminal.WriteLine($"{selectedGod.Name} is not your God! Are you sure about this?", "red");
            terminal.WriteLine($"The mighty {playerGod} is not going to be happy.", "red");
            
            var choice = await terminal.GetInputAsync("Continue? (Y/N) ");
            if (choice.ToUpper() == "Y")
            {
                wrongGod = true;
            }
            else
            {
                terminal.WriteLine("Good for you!", "green");
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
        terminal.WriteLine("═══ Altars of the Gods ═══", "magenta");
        terminal.WriteLine("");
        
        var activeGods = godSystem.GetActiveGods();
        if (activeGods.Count == 0)
        {
            terminal.WriteLine("No gods exist in this realm.", "gray");
        }
        else
        {
            foreach (var god in activeGods.OrderByDescending(g => g.Experience))
            {
                terminal.WriteLine($"Altar of {god.Name} the {god.GetTitle()}", "yellow");
                terminal.WriteLine($"  Believers: {god.Believers}", "white");
                terminal.WriteLine($"  Power: {god.Experience}", "cyan");
                terminal.WriteLine("");
            }
        }
        
        await terminal.GetInputAsync("Press Enter to continue...");
    }
    
    /// <summary>
    /// Display god ranking (Pascal TEMPLE.PAS)
    /// </summary>
    private async Task DisplayGodRanking()
    {
        terminal.WriteLine("");
        terminal.WriteLine("");
        
        var rankedGods = godSystem.ListGods(true);
        
        if (rankedGods.Count == 0)
        {
            terminal.WriteLine("No gods exist in this realm.", "gray");
        }
        else
        {
            terminal.WriteLine("   Immortals                Rank                Followers", "white");
            terminal.WriteLine("═══════════════════════════════════════════════════════════", "magenta");
            
            for (int i = 0; i < rankedGods.Count; i++)
            {
                var god = rankedGods[i];
                string line = $"{(i + 1).ToString().PadLeft(3)}. {god.Name.PadRight(25)} {god.GetTitle().PadRight(20)} {god.Believers.ToString().PadLeft(10)}";
                terminal.WriteLine(line, "yellow");
            }
        }
        
        await terminal.GetInputAsync("Press Enter to continue...");
    }
    
    /// <summary>
    /// Display holy news (Pascal TEMPLE.PAS)
    /// </summary>
    private async Task DisplayHolyNews()
    {
        terminal.WriteLine("");
        terminal.WriteLine("");
        terminal.WriteLine("═══ Holy News ═══", "cyan");
        terminal.WriteLine("");
        terminal.WriteLine("The gods watch over the realm...", "white");
        terminal.WriteLine("Divine interventions shape the fate of mortals...", "white");
        terminal.WriteLine("Prayers and sacrifices reach the heavens...", "white");
        terminal.WriteLine("");
        
        var stats = godSystem.GetGodStatistics();
        terminal.WriteLine($"Total Active Gods: {stats["TotalGods"]}", "yellow");
        terminal.WriteLine($"Total Believers: {stats["TotalBelievers"]}", "yellow");
        terminal.WriteLine($"Most Powerful: {stats["MostPowerfulGod"]}", "yellow");
        terminal.WriteLine($"Most Popular: {stats["MostPopularGod"]}", "yellow");
        
        await terminal.GetInputAsync("Press Enter to continue...");
    }
    
    /// <summary>
    /// Select a god from available options (Pascal Select_A_God function)
    /// Shows the list automatically and supports partial name matching
    /// </summary>
    private async Task<God?> SelectGod(string prompt = "Select a god", bool requireConfirmation = true)
    {
        var activeGods = godSystem.GetActiveGods().OrderBy(g => g.Name).ToList();
        if (activeGods.Count == 0)
        {
            terminal.WriteLine("No gods are available.", "red");
            await Task.Delay(1000);
            return null;
        }

        // Always show the list first
        DisplayGodListCompact(activeGods);

        while (true)
        {
            terminal.WriteLine("");
            terminal.WriteLine($"{prompt} (or press Enter to cancel):", "white");
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
                terminal.WriteLine($"No god found matching '{input}'. Try again.", "red");
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
                    terminal.WriteLine("Multiple matches found:", "yellow");
                    foreach (var match in matches)
                    {
                        string alignment = match.Goodness > match.Darkness ? "(Light)" : match.Darkness > match.Goodness ? "(Dark)" : "(Neutral)";
                        terminal.WriteLine($"  - {match.Name} {alignment}", "white");
                    }
                    terminal.WriteLine("Please be more specific.", "gray");
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
            terminal.WriteLine($"You have selected: {selectedGod.Name}, {selectedGod.GetTitle()}");
            terminal.SetColor("gray");
            terminal.WriteLine($"  Alignment: {godAlignment} | Believers: {selectedGod.Believers}");

            if (requireConfirmation)
            {
                terminal.WriteLine("");
                var confirm = await terminal.GetInputAsync($"Are you sure you want to choose {selectedGod.Name}? (Y/N) ");
                if (confirm.ToUpper() != "Y")
                {
                    terminal.WriteLine("Selection cancelled.", "gray");
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
        terminal.WriteLine("═══ Available Gods ═══", "cyan");
        terminal.WriteLine("");

        if (gods.Count == 0)
        {
            terminal.WriteLine("No gods currently accept worshippers.", "gray");
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
        terminal.WriteLine("(Type part of a god's name to select, e.g., 'Sol' for Solarius)");
    }

    /// <summary>
    /// Display list of available gods (full details version)
    /// </summary>
    private void DisplayGodList()
    {
        terminal.WriteLine("");
        terminal.WriteLine("═══ Available Gods ═══", "cyan");
        terminal.WriteLine("");

        var activeGods = godSystem.GetActiveGods().OrderBy(g => g.Name).ToList();

        if (activeGods.Count == 0)
        {
            terminal.WriteLine("No gods currently accept worshippers.", "gray");
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

            terminal.WriteLine($"    Believers: {god.Believers} | Power: {god.Experience:N0}", "white");
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
        
        var random = new Random();
        switch (random.Next(2))
        {
            case 0:
                terminal.WriteLine("When nobody is around You start to", "white");
                terminal.WriteLine("pound away at the altar with a pickaxe.", "white");
                terminal.Write("Hack", "red");
                for (int i = 0; i < 4; i++)
                {
                    await Task.Delay(500);
                    terminal.Write(".", "red");
                }
                terminal.Write("hack", "red");
                for (int i = 0; i < 4; i++)
                {
                    await Task.Delay(500);
                    terminal.Write(".", "red");
                }
                terminal.WriteLine("hack..!", "red");
                break;
                
            case 1:
                terminal.WriteLine("You find some unholy substances and", "white");
                terminal.WriteLine("pour them all over the altar!", "white");
                terminal.WriteLine("The altar is severely damaged!", "red");
                break;
        }
        
        terminal.WriteLine("");
        terminal.WriteLine($"You have desecrated {god.Name}'s altar!", "red");
        terminal.WriteLine("The gods will remember this blasphemy!", "red");
        
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
            terminal.WriteLine($"═══ Sacrifice to {god.Name} ═══", "cyan");
            terminal.WriteLine("");
            terminal.WriteLine("(G)old", "yellow");
            terminal.WriteLine("(S)tatus", "yellow");
            terminal.WriteLine("(R)eturn", "yellow");
            
            var choice = await terminal.GetInputAsync("Sacrifice (? for menu): ");
            
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
                    terminal.WriteLine("Invalid choice.", "red");
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
        var goldStr = await terminal.GetInputAsync("Amount of gold to sacrifice: ");

        if (!long.TryParse(goldStr, out long goldAmount) || goldAmount <= 0)
        {
            terminal.WriteLine("Invalid amount.", "red");
            await Task.Delay(1000);
            return;
        }

        if (goldAmount > currentPlayer.Gold)
        {
            terminal.WriteLine("You don't have that much gold!", "red");
            await Task.Delay(1000);
            return;
        }

        var choice = await terminal.GetInputAsync($"Sacrifice {goldAmount} gold to {god.Name}? (Y/N) ");
        if (choice.ToUpper() != "Y") return;

        // Process sacrifice
        currentPlayer.Gold -= goldAmount;
        var powerGained = godSystem.ProcessGoldSacrifice(god.Name, goldAmount, currentPlayer.Name2);

        terminal.WriteLine("");
        terminal.WriteLine($"{god.Name}'s power is growing!", "yellow");
        terminal.WriteLine("You can feel it...Your reward will come.", "white");
        terminal.WriteLine($"Power increased by {powerGained} points!", "cyan");

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
                terminal.WriteLine($"Duration: {duration.TotalMinutes:F0} minutes", "gray");
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
            terminal.WriteLine($"The darkness accepts your offering. (+{standingGain} Shadows standing)");
        }
        else
        {
            // Good god sacrifice - light act
            currentPlayer.ChivNr++;
            currentPlayer.Chivalry += standingGain;
            UsurperRemake.Systems.FactionSystem.Instance.ModifyReputation(UsurperRemake.Systems.Faction.TheFaith, standingGain);
            terminal.SetColor("bright_cyan");
            terminal.WriteLine($"Your devotion has been noted. (+{standingGain} Faith standing)");
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
                terminal.WriteLine($"*** {playerGod.ToUpper()} SEETHES WITH RAGE! ***");
                terminal.WriteLine("Your betrayal will not be forgotten. The dungeons hold your fate.");
            }
            else if (severity == 2)
            {
                terminal.WriteLine($"{playerGod} grows furious at your treachery!");
                terminal.WriteLine("Darkness awaits you in the depths below...");
            }
            else
            {
                terminal.WriteLine($"{playerGod} is displeased by your unfaithfulness.");
                terminal.WriteLine("Beware the shadows in your future...");
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
                terminal.WriteLine($"Your god {playerGod} no longer exists!", "red");
                terminal.WriteLine("Your faith has been shaken...", "gray");
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
        terminal.WriteLine("═══ Your Status ═══", "cyan");
        terminal.WriteLine("");
        terminal.WriteLine($"Name: {currentPlayer.Name2}", "yellow");
        terminal.WriteLine($"Level: {currentPlayer.Level}", "yellow");
        terminal.WriteLine($"Gold: {currentPlayer.Gold:N0}", "yellow");
        terminal.WriteLine($"Good Deeds: {currentPlayer.ChivNr}", "green");
        terminal.WriteLine($"Evil Deeds: {currentPlayer.DarkNr}", "red");
        
        string playerGod = godSystem.GetPlayerGod(currentPlayer.Name2);
        if (!string.IsNullOrEmpty(playerGod))
        {
            terminal.WriteLine($"God: {playerGod}", "cyan");
        }
        else
        {
            terminal.WriteLine("God: None (Pagan)", "gray");
        }
        
        await terminal.GetInputAsync("Press Enter to continue...");
    }

    #region Old Gods Integration

    /// <summary>
    /// Display prophecies about the Old Gods - hints about the main storyline
    /// </summary>
    private async Task DisplayOldGodsProphecies()
    {
        terminal.WriteLine("");
        terminal.WriteLine("");
        terminal.WriteLine("═══ The Prophecies of the Old Gods ═══", "bright_magenta");
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
                terminal.WriteLine("The Broken Blade has shattered. War finds peace at last.", "green");
            else if (maelkethState.Status == GodStatus.Saved)
                terminal.WriteLine("The Blade remembers honor. War serves justice once more.", "bright_green");
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
                terminal.WriteLine("The Withered Heart beats no more. Love fades from the world.", "gray");
            else if (velouraState.Status == GodStatus.Saved)
                terminal.WriteLine("Love blooms anew where hope was planted.", "bright_magenta");
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
                terminal.WriteLine("The Hollow Judge is silenced. Mortals must find their own justice.", "yellow");
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
                terminal.WriteLine("The Weaver's thread guides you through darkness.", "bright_cyan");
            else if (nocturaState.Status == GodStatus.Defeated)
                terminal.WriteLine("The shadows scatter. Secrets lie bare.", "gray");
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
                terminal.WriteLine("The Fading Light is extinguished. Truth dies in darkness.", "gray");
            else if (aurelionState.Status == GodStatus.Saved)
                terminal.WriteLine("The Light burns anew within a mortal vessel.", "bright_yellow");
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
                terminal.WriteLine("The Mountain crumbles. The foundation breaks.", "gray");
            else if (terravokState.Status == GodStatus.Saved)
                terminal.WriteLine("The Mountain rises. The foundation stands eternal.", "bright_green");
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
                terminal.WriteLine("The Creator's question has been answered. What comes next?", "bright_white");
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
            terminal.WriteLine("The prophecies remain sealed to those not yet ready.", "gray");
            terminal.WriteLine("Grow stronger, and the whispers will find you...", "gray");
        }

        terminal.WriteLine("");

        // Chance for divine vision
        if (random.NextDouble() < 0.15 && currentPlayer.Level >= 30)
        {
            await DisplayDivineVision();
        }

        await terminal.GetInputAsync("Press Enter to continue...");
    }

    /// <summary>
    /// Display a divine vision - rare insight into the story
    /// </summary>
    private async Task DisplayDivineVision()
    {
        terminal.WriteLine("");
        terminal.WriteLine("╔═══════════════════════════════════════════════════════════════╗", "bright_cyan");
        terminal.WriteLine("║              A VISION OVERTAKES YOU                           ║", "bright_cyan");
        terminal.WriteLine("╚═══════════════════════════════════════════════════════════════╝", "bright_cyan");
        terminal.WriteLine("");
        await Task.Delay(1500);

        var story = StoryProgressionSystem.Instance;
        int godsFaced = story.OldGodStates.Count(s => s.Value.Status != GodStatus.Imprisoned);

        if (godsFaced == 0)
        {
            terminal.WriteLine("You see seven figures standing in a circle of light.", "white");
            terminal.WriteLine("Their faces are beautiful, radiant, divine.", "white");
            await Task.Delay(1000);
            terminal.WriteLine("", "white");
            terminal.WriteLine("Then darkness creeps in. One by one, their light dims.", "gray");
            terminal.WriteLine("Their beauty twists. Their smiles become snarls.", "gray");
            await Task.Delay(1000);
            terminal.WriteLine("", "white");
            terminal.WriteLine("\"We were meant to guide you,\" one whispers.", "bright_magenta");
            terminal.WriteLine("\"But you broke our hearts instead.\"", "bright_magenta");
        }
        else if (godsFaced < 4)
        {
            terminal.WriteLine("You see yourself walking through endless halls.", "white");
            terminal.WriteLine("Ahead, a faint light flickers - barely visible.", "white");
            await Task.Delay(1000);
            terminal.WriteLine("", "white");
            terminal.WriteLine("A voice speaks: \"The Light fades with every lie.\"", "bright_yellow");
            terminal.WriteLine("\"Find me before truth dies forever.\"", "bright_yellow");
            await Task.Delay(1000);
            terminal.WriteLine("", "white");
            terminal.WriteLine("You sense the vision comes from... here. The Temple.", "bright_cyan");
        }
        else
        {
            terminal.WriteLine("You stand before a throne of stars.", "white");
            terminal.WriteLine("Upon it sits a figure older than time itself.", "white");
            await Task.Delay(1000);
            terminal.WriteLine("", "white");
            terminal.WriteLine("\"You've come far, child of dust.\"", "bright_white");
            terminal.WriteLine("\"But the final question remains.\"", "bright_white");
            await Task.Delay(1000);
            terminal.WriteLine("", "white");
            terminal.WriteLine("\"Was creation worth the cost?\"", "bright_magenta");
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
            terminal.WriteLine("The path to the Deep Temple is sealed.", "red");
            terminal.WriteLine("You must prove yourself against the other Old Gods first.", "gray");
            await Task.Delay(2000);
            return;
        }

        terminal.ClearScreen();
        terminal.WriteLine("");
        terminal.WriteLine("═══ THE DEEP TEMPLE ═══", "bright_yellow");
        terminal.WriteLine("");
        terminal.WriteLine("You descend stone steps worn smooth by millennia of pilgrims.", "white");
        terminal.WriteLine("The torches here burn with pale, flickering flames.", "white");
        await Task.Delay(1500);
        terminal.WriteLine("");
        terminal.WriteLine("The air grows thick with the weight of forgotten prayers.", "gray");
        terminal.WriteLine("Something watches you from the shadows between the light.", "gray");
        await Task.Delay(1500);

        var story = StoryProgressionSystem.Instance;

        // Check Aurelion's status
        if (story.OldGodStates.TryGetValue(OldGodType.Aurelion, out var aurelionState))
        {
            if (aurelionState.Status == GodStatus.Defeated)
            {
                terminal.WriteLine("");
                terminal.WriteLine("The altar where Aurelion once dwelt is dark and cold.", "gray");
                terminal.WriteLine("Only ash remains where the god of truth once flickered.", "gray");
                terminal.WriteLine("You feel a deep sense of... loss.", "white");
                await terminal.GetInputAsync("Press Enter to return...");
                return;
            }
            else if (aurelionState.Status == GodStatus.Saved)
            {
                terminal.WriteLine("");
                terminal.WriteLine("A warm light fills the chamber.", "bright_yellow");
                terminal.WriteLine("You feel Aurelion's presence within you - truth made flesh.", "bright_white");
                terminal.WriteLine("", "white");
                terminal.WriteLine("\"Thank you,\" his voice echoes in your mind.", "bright_cyan");
                terminal.WriteLine("\"For giving truth a new vessel.\"", "bright_cyan");
                await terminal.GetInputAsync("Press Enter to return...");
                return;
            }
        }

        // Aurelion encounter available
        terminal.WriteLine("");
        terminal.WriteLine("A faint glow pulses at the heart of the Deep Temple.", "bright_yellow");
        terminal.WriteLine("It is weak... barely visible... but unmistakably divine.", "white");
        terminal.WriteLine("");
        terminal.WriteLine("\"You... can see me?\" a voice whispers.", "bright_yellow");
        terminal.WriteLine("\"Few can anymore. The lies have grown so thick...\"", "bright_yellow");
        terminal.WriteLine("");

        var choice = await terminal.GetInputAsync("Approach the fading light? (Y/N) ");

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
                terminal.WriteLine("The light flickers but cannot fully manifest.", "yellow");
                terminal.WriteLine("\"I am... too weak. You must face the others first.\"", "bright_yellow");
                terminal.WriteLine("\"Defeat more of my fallen siblings. Only then...\"", "bright_yellow");
                await Task.Delay(2000);
            }
        }
        else
        {
            terminal.WriteLine("");
            terminal.WriteLine("You step back from the fading light.", "white");
            terminal.WriteLine("\"I understand,\" the voice whispers sadly.", "bright_yellow");
            terminal.WriteLine("\"Not everyone is ready for the truth.\"", "bright_yellow");
        }

        await terminal.GetInputAsync("Press Enter to return...");
    }

    /// <summary>
    /// Process item sacrifice - sacrifice equipment for divine favor
    /// </summary>
    private async Task ProcessItemSacrifice()
    {
        terminal.WriteLine("");
        terminal.WriteLine("");
        terminal.WriteLine("═══ Item Sacrifice ═══", "cyan");
        terminal.WriteLine("");

        string currentGod = godSystem.GetPlayerGod(currentPlayer.Name2);

        if (string.IsNullOrEmpty(currentGod))
        {
            terminal.WriteLine("You must worship a god before you can sacrifice items!", "red");
            terminal.WriteLine("Visit the (W)orship option first.", "gray");
            await Task.Delay(2000);
            return;
        }

        terminal.WriteLine($"You kneel before the altar of {currentGod}.", "white");
        terminal.WriteLine("", "white");
        terminal.WriteLine("What would you sacrifice?", "cyan");
        terminal.WriteLine("", "white");
        terminal.WriteLine("(W)eapon - Offer your weapon for divine blessing", "yellow");
        terminal.WriteLine("(A)rmor - Offer your armor for divine protection", "yellow");
        terminal.WriteLine("(H)ealing potions - Offer potions for divine favor", "yellow");
        terminal.WriteLine("(R)eturn", "yellow");
        terminal.WriteLine("");

        var choice = await terminal.GetInputAsync("Sacrifice: ");

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
            terminal.WriteLine("You have no weapon to sacrifice!", "red");
            await Task.Delay(1500);
            return;
        }

        var confirm = await terminal.GetInputAsync($"Sacrifice your weapon (Power: {currentPlayer.WeapPow}) to {godName}? (Y/N) ");
        if (confirm.ToUpper() != "Y") return;

        long powerGained = currentPlayer.WeapPow * 2;
        godSystem.ProcessGoldSacrifice(godName, powerGained * 100, currentPlayer.Name2); // Convert to equivalent gold power

        terminal.WriteLine("");
        terminal.WriteLine("Your weapon dissolves into divine light!", "bright_yellow");
        terminal.WriteLine($"{godName} accepts your sacrifice!", "cyan");
        terminal.WriteLine($"Divine power increased by {powerGained}!", "bright_cyan");

        // Chance for divine blessing based on weapon power
        if (random.NextDouble() < 0.3 + (currentPlayer.WeapPow / 500.0))
        {
            int blessingBonus = random.Next(2, 6);
            currentPlayer.Strength += blessingBonus;
            terminal.WriteLine($"{godName} blesses you with +{blessingBonus} Strength!", "bright_green");
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
            terminal.WriteLine("You have no armor to sacrifice!", "red");
            await Task.Delay(1500);
            return;
        }

        var confirm = await terminal.GetInputAsync($"Sacrifice your armor (Power: {currentPlayer.ArmPow}) to {godName}? (Y/N) ");
        if (confirm.ToUpper() != "Y") return;

        long powerGained = currentPlayer.ArmPow * 2;
        godSystem.ProcessGoldSacrifice(godName, powerGained * 100, currentPlayer.Name2);

        terminal.WriteLine("");
        terminal.WriteLine("Your armor dissolves into divine light!", "bright_yellow");
        terminal.WriteLine($"{godName} accepts your sacrifice!", "cyan");
        terminal.WriteLine($"Divine power increased by {powerGained}!", "bright_cyan");

        // Chance for divine blessing
        if (random.NextDouble() < 0.3 + (currentPlayer.ArmPow / 500.0))
        {
            int blessingBonus = random.Next(2, 6);
            currentPlayer.Defence += blessingBonus;
            terminal.WriteLine($"{godName} blesses you with +{blessingBonus} Defence!", "bright_green");
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
            terminal.WriteLine("You have no healing potions to sacrifice!", "red");
            await Task.Delay(1500);
            return;
        }

        terminal.WriteLine($"You have {currentPlayer.Healing} healing potions.", "white");
        var amountStr = await terminal.GetInputAsync("How many to sacrifice? ");

        if (!int.TryParse(amountStr, out int amount) || amount <= 0)
        {
            terminal.WriteLine("Invalid amount.", "red");
            await Task.Delay(1000);
            return;
        }

        if (amount > currentPlayer.Healing)
        {
            terminal.WriteLine("You don't have that many potions!", "red");
            await Task.Delay(1000);
            return;
        }

        var confirm = await terminal.GetInputAsync($"Sacrifice {amount} healing potions to {godName}? (Y/N) ");
        if (confirm.ToUpper() != "Y") return;

        long powerGained = amount * 5; // Each potion gives 5 power
        godSystem.ProcessGoldSacrifice(godName, powerGained * 50, currentPlayer.Name2);

        currentPlayer.Healing -= amount;

        terminal.WriteLine("");
        terminal.WriteLine("Your potions evaporate into divine essence!", "bright_yellow");
        terminal.WriteLine($"{godName} accepts your sacrifice!", "cyan");
        terminal.WriteLine($"Divine power increased by {powerGained}!", "bright_cyan");

        // Chance for divine healing
        if (amount >= 3 && random.NextDouble() < 0.5)
        {
            currentPlayer.HP = currentPlayer.MaxHP;
            terminal.WriteLine($"{godName} fully restores your health!", "bright_green");
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
            terminal.WriteLine($"The darkness notes your devotion. (+{amount} Shadows standing)");
        }
        else
        {
            currentPlayer.ChivNr++;
            currentPlayer.Chivalry += amount;
            UsurperRemake.Systems.FactionSystem.Instance.ModifyReputation(UsurperRemake.Systems.Faction.TheFaith, amount);
            terminal.SetColor("bright_cyan");
            terminal.WriteLine($"The light notes your devotion. (+{amount} Faith standing)");
        }
    }

    /// <summary>
    /// Enhanced desecration that rewards XP and darkness (from Pascal TEMPLE.PAS)
    /// </summary>
    private async Task PerformEnhancedDesecration(God god)
    {
        terminal.WriteLine("");
        terminal.WriteLine("");

        var random = new Random();

        // Desecration flavour text
        string[] desecrationMethods = new[]
        {
            "You smash the altar with a pickaxe, shattering holy relics.",
            "You pour unholy substances over the sacred symbols.",
            "You carve blasphemous words into the altar's surface.",
            "You set fire to the offerings left by faithful worshippers.",
            "You topple the statue of the god, watching it shatter."
        };

        terminal.WriteLine(desecrationMethods[random.Next(desecrationMethods.Length)], "red");
        await Task.Delay(1500);

        terminal.WriteLine("");
        terminal.WriteLine($"You have desecrated {god.Name}'s altar!", "bright_red");

        // Process desecration in god system
        godSystem.ProcessAltarDesecration(god.Name, currentPlayer.Name2);

        // Award darkness and XP (from Pascal)
        int darknessGain = random.Next(10, 25);
        long xpGain = (long)(Math.Pow(currentPlayer.Level, 1.5) * 20);

        currentPlayer.Darkness += darknessGain;
        currentPlayer.Experience += xpGain;
        currentPlayer.DarkNr--;

        terminal.WriteLine("", "white");
        terminal.WriteLine($"Darkness flows into your soul! (+{darknessGain} Darkness)", "dark_red");
        terminal.WriteLine($"Experience gained from profane knowledge! (+{xpGain} XP)", "yellow");

        // Chance for curse
        if (random.NextDouble() < 0.2)
        {
            terminal.WriteLine("", "white");
            terminal.WriteLine($"{god.Name} curses you from beyond!", "bright_red");

            int curseDamage = random.Next(10, 30 + currentPlayer.Level);
            currentPlayer.HP = Math.Max(1, currentPlayer.HP - curseDamage);
            terminal.WriteLine($"You take {curseDamage} divine damage!", "red");
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
            terminal.WriteLine("The ancient stones still stand, but their secret has been revealed.", "gray");
            terminal.WriteLine("You remember the truth of creation...", "gray");
            await Task.Delay(1500);
            return;
        }

        terminal.WriteLine("");
        terminal.WriteLine("");
        terminal.SetColor("cyan");
        terminal.WriteLine("You walk past the busy altars, past the crowds of worshippers,");
        terminal.WriteLine("to the far corner of the temple where few tread.");
        terminal.SetColor("white");
        terminal.WriteLine("");
        await Task.Delay(1500);

        terminal.WriteLine("Here, massive stones form the foundation of the building.");
        terminal.WriteLine("They are older than the temple itself - older than any god");
        terminal.WriteLine("whose altar stands above.");
        terminal.WriteLine("");
        await Task.Delay(1500);

        terminal.SetColor("gray");
        terminal.WriteLine("The monks say these stones were here before the city was built.");
        terminal.WriteLine("Before mortals came to this land.");
        terminal.WriteLine("Before even the gods walked the earth.");
        terminal.SetColor("white");
        terminal.WriteLine("");
        await Task.Delay(1500);

        var choice = await terminal.GetInputAsync("Touch the ancient stone? (Y/N) ");

        if (choice.ToUpper() != "Y")
        {
            terminal.WriteLine("");
            terminal.WriteLine("You step back from the stones.", "gray");
            terminal.WriteLine("Perhaps another time...", "gray");
            await Task.Delay(1000);
            return;
        }

        // Discovery sequence
        terminal.WriteLine("");
        terminal.SetColor("bright_yellow");
        terminal.WriteLine("Your hand touches the cold stone...");
        terminal.WriteLine("");
        await Task.Delay(1000);

        terminal.SetColor("white");
        terminal.WriteLine("At first, nothing.");
        terminal.WriteLine("");
        await Task.Delay(800);

        terminal.WriteLine("Then warmth. A pulse, like a heartbeat.");
        terminal.WriteLine("");
        await Task.Delay(800);

        terminal.SetColor("bright_cyan");
        terminal.WriteLine("The stone GLOWS beneath your palm.");
        terminal.WriteLine("Ancient symbols flare to life - a language");
        terminal.WriteLine("older than any spoken by mortal or god.");
        terminal.SetColor("white");
        terminal.WriteLine("");
        await Task.Delay(1500);

        terminal.SetColor("bright_magenta");
        terminal.WriteLine("A voice speaks directly into your mind:");
        terminal.WriteLine("");
        terminal.SetColor("bright_white");
        terminal.WriteLine("  \"You seek truth. So few do anymore.\"");
        terminal.WriteLine("  \"This is the First Seal - the story of creation.\"");
        terminal.WriteLine("  \"Remember it well, for understanding begins here.\"");
        terminal.SetColor("white");
        terminal.WriteLine("");
        await Task.Delay(1500);

        terminal.SetColor("gray");
        await terminal.GetInputAsync("  Press Enter to continue...");

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

        if (string.IsNullOrEmpty(playerGod))
        {
            terminal.WriteLine("");
            terminal.WriteLine("You must worship a god before you can pray for blessings.", "yellow");
            terminal.WriteLine("Visit (W)orship to choose a deity.", "gray");
            await Task.Delay(2000);
            return;
        }

        if (!UsurperRemake.Systems.DivineBlessingSystem.Instance.CanPrayToday(currentPlayer.Name2))
        {
            terminal.WriteLine("");
            terminal.WriteLine("You have already prayed today.", "gray");
            terminal.WriteLine("Return tomorrow for another blessing.", "gray");
            await Task.Delay(1500);
            return;
        }

        var god = godSystem.GetGod(playerGod);
        if (god == null)
        {
            terminal.WriteLine("Your god no longer exists...", "red");
            await Task.Delay(1500);
            return;
        }

        terminal.WriteLine("");
        terminal.WriteLine("");
        terminal.SetColor("cyan");
        terminal.WriteLine($"You kneel before the altar of {playerGod}...");
        await Task.Delay(1000);

        terminal.SetColor("white");
        terminal.WriteLine("Your prayers rise like incense to the heavens...");
        await Task.Delay(1000);

        // Determine prayer response based on god's alignment
        float alignment = (float)(god.Goodness - god.Darkness) / Math.Max(1, god.Goodness + god.Darkness);

        if (alignment > 0.3f)
        {
            terminal.SetColor("bright_yellow");
            terminal.WriteLine("Warm light fills the chamber as your god hears you.");
        }
        else if (alignment < -0.3f)
        {
            terminal.SetColor("dark_magenta");
            terminal.WriteLine("Shadows coil around you as your god acknowledges your devotion.");
        }
        else
        {
            terminal.SetColor("bright_cyan");
            terminal.WriteLine("A sense of balance and clarity washes over you.");
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
                terminal.WriteLine($"  Damage: +{blessing.DamageBonus}%", "red");
            if (blessing.DefenseBonus > 0)
                terminal.WriteLine($"  Defense: +{blessing.DefenseBonus}%", "cyan");
            if (blessing.XPBonus > 0)
                terminal.WriteLine($"  XP Bonus: +{blessing.XPBonus}%", "yellow");

            var duration = blessing.ExpiresAt - DateTime.Now;
            terminal.WriteLine($"  Duration: {duration.TotalMinutes:F0} minutes", "gray");

            terminal.WriteLine("");
            terminal.SetColor("white");
            terminal.WriteLine($"{playerGod}'s blessing is upon you!");
        }
        else
        {
            terminal.WriteLine("");
            terminal.WriteLine("Your prayers go unanswered today...", "gray");
        }

        // Apply small faction effect for daily prayer based on god alignment
        if (alignment > 0.3f)
        {
            // Good god - light action
            UsurperRemake.Systems.FactionSystem.Instance.ModifyReputation(UsurperRemake.Systems.Faction.TheFaith, 1);
            terminal.SetColor("bright_cyan");
            terminal.WriteLine("(+1 Faith standing)");
        }
        else if (alignment < -0.3f)
        {
            // Evil god - dark action
            UsurperRemake.Systems.FactionSystem.Instance.ModifyReputation(UsurperRemake.Systems.Faction.TheShadows, 1);
            terminal.SetColor("bright_magenta");
            terminal.WriteLine("(+1 Shadows standing)");
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
            terminal.WriteLine("The meditation chapel is empty.", "gray");
            terminal.WriteLine("Only silence and candle smoke fill the small room.", "gray");
            await Task.Delay(1500);
            refreshMenu = true;
            return;
        }

        terminal.ClearScreen();
        terminal.SetColor("bright_green");
        terminal.WriteLine("╔══════════════════════════════════════════════════════════════════╗");
        terminal.WriteLine("║                    MEDITATION CHAPEL                              ║");
        terminal.WriteLine("╚══════════════════════════════════════════════════════════════════╝");
        terminal.WriteLine("");
        await Task.Delay(1000);

        terminal.SetColor("white");
        terminal.WriteLine("You step into a small, quiet chapel off the main temple.");
        terminal.WriteLine("A single candle illuminates a woman kneeling before an empty altar.");
        terminal.WriteLine("");
        await Task.Delay(1500);

        terminal.SetColor("gray");
        terminal.WriteLine("She wears the faded robes of a priestess, though they bear no symbol.");
        terminal.WriteLine("Her hands are clasped, but her lips do not move.");
        terminal.WriteLine("She prays to... nothing. An empty space where faith once lived.");
        terminal.WriteLine("");
        await Task.Delay(1500);

        // First dialogue
        terminal.SetColor("cyan");
        terminal.WriteLine("She notices you watching.");
        terminal.WriteLine("");
        terminal.SetColor("bright_cyan");
        terminal.WriteLine($"\"{mira.DialogueHints[0]}\"");
        terminal.WriteLine("");
        await Task.Delay(2000);

        terminal.SetColor("white");
        terminal.WriteLine("She turns back to the empty altar.");
        terminal.WriteLine("");
        terminal.SetColor("cyan");
        terminal.WriteLine($"\"{mira.DialogueHints[1]}\"");
        terminal.WriteLine("");
        await Task.Delay(2000);

        // Show her details
        terminal.SetColor("yellow");
        terminal.WriteLine($"This is {mira.Name}, {mira.Title}.");
        terminal.WriteLine($"Role: {mira.CombatRole}");
        terminal.WriteLine($"Abilities: {string.Join(", ", mira.Abilities)}");
        terminal.WriteLine("");

        terminal.SetColor("gray");
        terminal.WriteLine(mira.BackstoryBrief);
        terminal.WriteLine("");
        await Task.Delay(1500);

        terminal.SetColor("bright_yellow");
        terminal.WriteLine("[R] Ask her to join you");
        terminal.WriteLine("[T] Talk about her past");
        terminal.WriteLine("[L] Leave her to her prayers");
        terminal.WriteLine("");

        var choice = await terminal.GetInputAsync("Your choice: ");

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
                terminal.WriteLine("You leave her to her silent vigil.");
                terminal.WriteLine("As you reach the door, she speaks without turning:");
                terminal.SetColor("cyan");
                terminal.WriteLine($"\"{mira.DialogueHints[2]}\"");
                break;
        }

        // Mark encounter as complete
        StoryProgressionSystem.Instance.SetStoryFlag("mira_temple_encounter_complete", true);
        await terminal.GetInputAsync("Press Enter to continue...");
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
        terminal.WriteLine("\"The dungeons are dangerous,\" you say. \"A healer would be invaluable.\"");
        terminal.WriteLine("");
        await Task.Delay(1000);

        terminal.SetColor("cyan");
        terminal.WriteLine($"{mira.Name} looks at you for a long moment.");
        terminal.WriteLine("Something flickers in her eyes. Not hope - something smaller.");
        terminal.WriteLine("A question, perhaps.");
        terminal.WriteLine("");
        await Task.Delay(1500);

        terminal.SetColor("bright_cyan");
        terminal.WriteLine("\"You want me to heal,\" she says.");
        terminal.WriteLine("\"I can do that. I've always been able to do that.\"");
        terminal.WriteLine("\"But will it matter? Will any of it matter?\"");
        terminal.WriteLine("");
        await Task.Delay(1500);

        terminal.SetColor("white");
        terminal.WriteLine("She doesn't wait for an answer.");
        terminal.WriteLine("");
        terminal.SetColor("cyan");
        terminal.WriteLine("\"Perhaps if I help you long enough, I'll find out.\"");
        terminal.WriteLine("");
        await Task.Delay(1000);

        bool success = await companionSystem.RecruitCompanion(
            UsurperRemake.Systems.CompanionId.Mira, currentPlayer, terminal);

        if (success)
        {
            terminal.SetColor("bright_green");
            terminal.WriteLine("");
            terminal.WriteLine($"{mira.Name} rises from the empty altar.");
            terminal.WriteLine("The candle behind her flickers - but does not go out.");
            terminal.WriteLine("");
            terminal.SetColor("yellow");
            terminal.WriteLine("WARNING: Companions can die permanently. She may find her answer in sacrifice.");

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
        terminal.WriteLine("You sit beside her. The silence stretches between you.");
        terminal.WriteLine("");
        await Task.Delay(1500);

        terminal.SetColor("white");
        terminal.WriteLine(mira.Description);
        terminal.WriteLine("");
        await Task.Delay(1500);

        terminal.SetColor("cyan");
        terminal.WriteLine("\"I was a healer at Veloura's temple,\" she says finally.");
        terminal.WriteLine("\"When the corruption came... the healers became something else.\"");
        terminal.WriteLine("\"I escaped. But I left my faith behind.\"");
        terminal.WriteLine("");
        await Task.Delay(2000);

        if (!string.IsNullOrEmpty(mira.PersonalQuestDescription))
        {
            terminal.SetColor("bright_magenta");
            terminal.WriteLine($"Personal Quest: {mira.PersonalQuestName}");
            terminal.WriteLine($"\"{mira.PersonalQuestDescription}\"");
            terminal.WriteLine("");
        }

        terminal.SetColor("bright_cyan");
        terminal.WriteLine("\"I keep praying,\" she whispers.");
        terminal.WriteLine("\"To an empty altar. To nothing.\"");
        terminal.WriteLine("\"Because if I stop... I don't know what I am anymore.\"");
        terminal.WriteLine("");
        await Task.Delay(2000);

        var followUp = await terminal.GetInputAsync("Ask her to join you? (Y/N): ");
        if (followUp.ToUpper() == "Y")
        {
            await AttemptMiraRecruitment(mira);
        }
        else
        {
            terminal.SetColor("gray");
            terminal.WriteLine("");
            terminal.WriteLine("You squeeze her shoulder gently and leave.");
            terminal.WriteLine("Perhaps another time.");
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
        terminal.SetColor("bright_yellow");
        terminal.WriteLine("╔══════════════════════════════════════════════════════════════════════════════╗");
        terminal.WriteLine("║                              THE FAITH                                       ║");
        terminal.WriteLine("╚══════════════════════════════════════════════════════════════════════════════╝");
        terminal.WriteLine("");

        terminal.SetColor("white");
        terminal.WriteLine("You approach the inner sanctum of the Temple, where only the most");
        terminal.WriteLine("devoted are permitted. An elderly priestess in white robes greets you.");
        terminal.WriteLine("");
        await Task.Delay(1500);

        terminal.SetColor("bright_cyan");
        terminal.WriteLine("\"I am High Priestess Mirael,\" she says, her voice gentle but firm.");
        terminal.WriteLine("\"I have watched your journey with interest, traveler.\"");
        terminal.WriteLine("");
        await Task.Delay(1500);

        // Check if already in a faction
        if (factionSystem.PlayerFaction != null)
        {
            terminal.SetColor("yellow");
            terminal.WriteLine("She studies you with knowing eyes.");
            terminal.WriteLine("");
            terminal.SetColor("bright_cyan");
            terminal.WriteLine($"\"You already serve {UsurperRemake.Systems.FactionSystem.Factions[factionSystem.PlayerFaction.Value].Name}.\"");
            terminal.WriteLine("\"The Faith does not accept divided loyalties.\"");
            terminal.WriteLine("\"Should you ever renounce your current allegiance, seek me again.\"");
            terminal.WriteLine("");
            await terminal.GetInputAsync("Press Enter to continue...");
            refreshMenu = true;
            return;
        }

        terminal.SetColor("white");
        terminal.WriteLine("She gestures to the sacred flames burning eternally on the altar.");
        terminal.WriteLine("");
        terminal.SetColor("bright_cyan");
        terminal.WriteLine("\"The Faith believes the Old Gods were once pure and good.\"");
        terminal.WriteLine("\"They guided humanity with love, wisdom, and truth.\"");
        terminal.WriteLine("\"But mortal worship corrupted them - our fears, our hatreds,\"");
        terminal.WriteLine("\"our lies... they absorbed them all until they broke.\"");
        terminal.WriteLine("");
        await Task.Delay(2000);

        terminal.SetColor("cyan");
        terminal.WriteLine("\"We believe the gods can be HEALED, not destroyed.\"");
        terminal.WriteLine("\"Through devotion, sacrifice, and unwavering faith,\"");
        terminal.WriteLine("\"we will restore them to their former glory.\"");
        terminal.WriteLine("");
        await Task.Delay(1500);

        // Show faction benefits
        terminal.SetColor("bright_yellow");
        terminal.WriteLine("═══ Benefits of The Faith ═══");
        terminal.SetColor("white");
        terminal.WriteLine("• 25% discount on healing services at all healers");
        terminal.WriteLine("• Access to special healing prayers and blessings");
        terminal.WriteLine("• Friendly treatment from clerics and temple NPCs");
        terminal.WriteLine("• Standing with The Faith grows through devotion");
        terminal.WriteLine("");

        // Check requirements
        var (canJoin, reason) = factionSystem.CanJoinFaction(UsurperRemake.Systems.Faction.TheFaith, currentPlayer);

        if (!canJoin)
        {
            terminal.SetColor("red");
            terminal.WriteLine("═══ Requirements Not Met ═══");
            terminal.SetColor("yellow");
            terminal.WriteLine(reason);
            terminal.WriteLine("");
            terminal.SetColor("gray");
            terminal.WriteLine("The Faith requires:");
            terminal.WriteLine("• Level 10 or higher");
            terminal.WriteLine("• Faith Standing 100+ (make gold offerings, pray daily)");
            terminal.WriteLine($"  Your Faith Standing: {factionSystem.FactionStanding[UsurperRemake.Systems.Faction.TheFaith]}");
            terminal.WriteLine("");
            terminal.SetColor("bright_cyan");
            terminal.WriteLine("\"Return when your faith burns brighter,\" Mirael says kindly.");
            terminal.WriteLine("\"Make offerings at our altars. Pray daily. Show your devotion.\"");
            await terminal.GetInputAsync("Press Enter to continue...");
            refreshMenu = true;
            return;
        }

        // Can join - offer the choice
        terminal.SetColor("bright_green");
        terminal.WriteLine("═══ Requirements Met ═══");
        terminal.SetColor("white");
        terminal.WriteLine("High Priestess Mirael extends her hand toward you.");
        terminal.WriteLine("");
        terminal.SetColor("bright_cyan");
        terminal.WriteLine("\"Your devotion has been noted. Your offerings accepted.\"");
        terminal.WriteLine("\"Will you take the sacred oath and join The Faith?\"");
        terminal.WriteLine("");
        terminal.SetColor("yellow");
        terminal.WriteLine("WARNING: Joining The Faith will:");
        terminal.WriteLine("• Lock you out of The Crown and The Shadows");
        terminal.WriteLine("• Decrease standing with rival factions by 100");
        terminal.WriteLine("");

        var choice = await terminal.GetInputAsync("Join The Faith? (Y/N) ");

        if (choice.ToUpper() == "Y")
        {
            await PerformFaithOath(factionSystem);
        }
        else
        {
            terminal.WriteLine("");
            terminal.SetColor("cyan");
            terminal.WriteLine("Mirael nods with understanding.");
            terminal.WriteLine("\"The path of faith is not for everyone. But know this:\"");
            terminal.WriteLine("\"Our doors remain open, should you ever seek the light.\"");
        }

        await terminal.GetInputAsync("Press Enter to continue...");
        refreshMenu = true;
    }

    /// <summary>
    /// Perform the oath ceremony to join The Faith
    /// </summary>
    private async Task PerformFaithOath(UsurperRemake.Systems.FactionSystem factionSystem)
    {
        terminal.ClearScreen();
        terminal.SetColor("bright_yellow");
        terminal.WriteLine("╔══════════════════════════════════════════════════════════════════════════════╗");
        terminal.WriteLine("║                         THE SACRED OATH                                      ║");
        terminal.WriteLine("╚══════════════════════════════════════════════════════════════════════════════╝");
        terminal.WriteLine("");

        terminal.SetColor("white");
        terminal.WriteLine("You kneel before the sacred flames.");
        terminal.WriteLine("High Priestess Mirael stands before you, her hands raised.");
        terminal.WriteLine("");
        await Task.Delay(1500);

        terminal.SetColor("bright_cyan");
        terminal.WriteLine("\"Repeat after me:\"");
        terminal.WriteLine("");
        await Task.Delay(1000);

        terminal.SetColor("yellow");
        terminal.WriteLine("\"I pledge my soul to the restoration of the gods.\"");
        await Task.Delay(1200);
        terminal.WriteLine("\"I will heal what is broken, mend what is torn.\"");
        await Task.Delay(1200);
        terminal.WriteLine("\"Through faith, I will be the light in darkness.\"");
        await Task.Delay(1200);
        terminal.WriteLine("\"Until the gods are pure, I shall not rest.\"");
        terminal.WriteLine("");
        await Task.Delay(1500);

        terminal.SetColor("white");
        terminal.WriteLine("The sacred flames flare brightly, bathing you in warm light.");
        terminal.WriteLine("You feel a profound sense of peace wash over you.");
        terminal.WriteLine("");
        await Task.Delay(1500);

        // Actually join the faction
        factionSystem.JoinFaction(UsurperRemake.Systems.Faction.TheFaith, currentPlayer);

        terminal.SetColor("bright_green");
        terminal.WriteLine("╔══════════════════════════════════════════════════════════════════════════════╗");
        terminal.WriteLine("║              YOU HAVE JOINED THE FAITH                                       ║");
        terminal.WriteLine("╚══════════════════════════════════════════════════════════════════════════════╝");
        terminal.WriteLine("");

        terminal.SetColor("bright_cyan");
        terminal.WriteLine("\"Welcome, child of the light,\" Mirael says warmly.");
        terminal.WriteLine("\"You are now one of us. May your faith never waver.\"");
        terminal.WriteLine("");

        terminal.SetColor("white");
        terminal.WriteLine("As a member of The Faith, you will receive:");
        terminal.SetColor("bright_green");
        terminal.WriteLine("• 25% discount on all healing services");
        terminal.WriteLine("• Recognition from Temple NPCs");
        terminal.WriteLine("• Access to Faith-only blessings and prayers");
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
            terminal.WriteLine("\n  The Inner Sanctum is sealed to outsiders.");
            terminal.WriteLine("  Only members of The Faith may enter.");
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
            terminal.WriteLine("\n  You have already meditated today.");
            terminal.WriteLine("  The sanctum will be ready again tomorrow.");
            await Task.Delay(2000);
            return;
        }

        terminal.ClearScreen();
        terminal.SetColor("bright_cyan");
        terminal.WriteLine("╔══════════════════════════════════════════════════════════════════╗");
        terminal.WriteLine("║                      THE INNER SANCTUM                            ║");
        terminal.WriteLine("╚══════════════════════════════════════════════════════════════════╝");
        terminal.WriteLine("");

        terminal.SetColor("gray");
        terminal.WriteLine("  A chamber of perfect stillness. Incense hangs in the air.");
        terminal.WriteLine("  Ancient runes pulse faintly along the walls.");
        terminal.SetColor("yellow");
        terminal.WriteLine($"\n  Deep meditation costs {GameConfig.InnerSanctumCost} gold.");
        terminal.SetColor("cyan");
        terminal.WriteLine("  The sanctum grants a permanent +1 to a random attribute.");
        terminal.WriteLine("");
        terminal.SetColor("yellow");
        terminal.WriteLine($"  Gold: {currentPlayer.Gold:N0}");
        terminal.WriteLine("");

        var input = await terminal.GetInput("  Enter the sanctum? (Y/N): ");
        if (input.Trim().ToUpper() != "Y")
            return;

        if (currentPlayer.Gold < GameConfig.InnerSanctumCost)
        {
            terminal.SetColor("red");
            terminal.WriteLine("  You cant afford the offering.");
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
        terminal.WriteLine("\n  You kneel on the cold stone and close your eyes...");
        await Task.Delay(2000);
        terminal.SetColor("bright_cyan");
        terminal.WriteLine("  Warmth floods through you. Something shifts within.");
        await Task.Delay(1500);

        // Grant +1 to a random stat
        var rng = new Random();
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
        terminal.WriteLine($"\n  +1 {statName}!");
        terminal.SetColor("gray");
        terminal.WriteLine("  The sanctum's power has left its mark on you.");
        terminal.WriteLine("");

        await terminal.PressAnyKey();
    }

    #endregion
}
