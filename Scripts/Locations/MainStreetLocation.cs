using UsurperRemake.Utils;
using UsurperRemake.Systems;
using UsurperRemake.BBS;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;

/// <summary>
/// Main Street location - central hub of the game
/// Based on Pascal main_menu procedure from GAMEC.PAS
/// </summary>
public class MainStreetLocation : BaseLocation
{
    public MainStreetLocation() : base(
        GameLocation.MainStreet,
        "Main Street",
        "You are standing on the main street of town. The bustling center of all activity."
    ) { }
    
    protected override void SetupLocation()
    {
        // Pascal-compatible exits from ONLINE.PAS onloc_mainstreet case
        PossibleExits = new List<GameLocation>
        {
            GameLocation.TheInn,       // loc1
            GameLocation.Church,       // loc2  
            GameLocation.Darkness,     // loc3
            GameLocation.Master,       // loc4
            GameLocation.MagicShop,    // loc5
            GameLocation.Dungeons,     // loc6
            GameLocation.WeaponShop,   // loc7
            GameLocation.ArmorShop,    // loc8
            GameLocation.Bank,         // loc9
            GameLocation.AuctionHouse,  // loc10
            GameLocation.DarkAlley,    // loc11
            GameLocation.ReportRoom,   // loc12
            GameLocation.Healer,       // loc13
            GameLocation.AnchorRoad,   // loc14
            GameLocation.Home          // loc15
        };
        
        // Location actions based on Pascal main menu
        LocationActions = new List<string>
        {
            "Status",              // (S)tatus
            "Good Deeds",          // (G)ood Deeds
            "Evil Deeds",          // (E)vil Deeds
            "News",                // (N)ews
            "World Events",        // ($) World Events
            "List Characters",     // (L)ist Characters
            "Fame",                // (F)ame
            "Relations",           // (R)elations
            "Inventory"            // (*) Inventory
        };
    }

    protected override string GetMudPromptName() => "Main Street";

    /// <summary>
    /// Returns the menu disclosure tier based on player level.
    /// Tier 1 (Level 1-2): Core combat loop only
    /// Tier 2 (Level 3-4): Town services added
    /// Tier 3 (Level 5+): Full menu
    /// </summary>
    private int GetMenuTier()
    {
        if (currentPlayer == null) return 3;
        if (currentPlayer.Level >= GameConfig.MenuTier3Level) return 3;
        if (currentPlayer.Level >= GameConfig.MenuTier2Level) return 2;
        return 1;
    }

    protected override string[]? GetAmbientMessages() => new[]
    {
        "A merchant bellows the price of his wares.",
        "The crowd shifts and murmurs around you.",
        "A distant bell tolls the hour.",
        "Cart wheels clatter over the cobblestones.",
        "The wind carries the smell of fresh bread from a nearby stall.",
        "A dog barks somewhere down an alley.",
        "Two guards exchange words as they pass.",
    };

    protected override void DisplayLocation()
    {
        terminal.ClearScreen();

        if (IsBBSSession)
        {
            DisplayLocationBBS();
            return;
        }

        // ASCII art header (simplified version)
        terminal.SetColor("bright_blue");
        terminal.WriteLine("╔══════════════════════════════════════════════════════════════════════════════╗");
        terminal.WriteLine($"║{"MAIN STREET".PadLeft((78 + 11) / 2).PadRight(78)}║");
        terminal.WriteLine("╚══════════════════════════════════════════════════════════════════════════════╝");
        terminal.WriteLine("");

        // Online status bar (only in online mode)
        if (DoorMode.IsOnlineMode && OnlineStateManager.Instance != null)
        {
            int onlineCount = OnlineStateManager.Instance.CachedOnlinePlayerCount;
            terminal.SetColor("darkgray");
            terminal.Write(" Players online: ");
            terminal.SetColor("bright_green");
            terminal.Write($"{onlineCount}");
            terminal.SetColor("darkgray");
            terminal.Write("  |  ");
            terminal.SetColor("cyan");
            terminal.Write("/say");
            terminal.SetColor("darkgray");
            terminal.Write(" to chat  |  ");
            terminal.SetColor("cyan");
            terminal.Write("/who");
            terminal.SetColor("darkgray");
            terminal.WriteLine(" for player list");
        }

        // Location description with time/weather
        terminal.SetColor("white");
        terminal.WriteLine($"You are standing on the main street of {GetTownName()}.");
        terminal.WriteLine($"The {GetTimeOfDay()} air is {GetWeather()}.");
        terminal.WriteLine("");

        // Show NPCs in location
        ShowNPCsInLocation();

        // Main Street menu (Pascal-style layout)
        ShowMainStreetMenu();

        // Check for level eligibility message
        ShowLevelEligibilityMessage();

        // Status line
        ShowStatusLine();

        // Show dungeon guidance for brand-new players, or generic navigation hint otherwise
        if (currentPlayer.Level == 1 && currentPlayer.MKills == 0)
        {
            terminal.WriteLine("");
            terminal.SetColor("bright_yellow");
            terminal.Write("  >>> ");
            terminal.SetColor("bright_white");
            terminal.Write("New adventurer? Press ");
            terminal.SetColor("bright_yellow");
            terminal.Write("[D]");
            terminal.SetColor("bright_white");
            terminal.Write(" to enter the Dungeons!");
            terminal.SetColor("bright_yellow");
            terminal.WriteLine(" <<<");
            terminal.SetColor("white");
        }
        else
        {
            HintSystem.Instance.TryShowHint(HintSystem.HINT_MAIN_STREET_NAVIGATION, terminal, currentPlayer.HintsShown);
        }

        // Show low HP hint if player is below 25% health
        if (currentPlayer.HP < currentPlayer.MaxHP * 0.25)
        {
            HintSystem.Instance.TryShowHint(HintSystem.HINT_LOW_HP, terminal, currentPlayer.HintsShown);
        }
    }

    /// <summary>
    /// Compact Main Street display for BBS 80x25 terminals.
    /// Fits header, description, NPCs, menu, status, and prompt within 25 rows.
    /// </summary>
    private void DisplayLocationBBS()
    {
        // Line 1: Header
        terminal.SetColor("bright_blue");
        terminal.Write("╔════════════════════════════════ ");
        terminal.SetColor("bright_white");
        terminal.Write("MAIN STREET");
        terminal.SetColor("bright_blue");
        terminal.WriteLine(" ════════════════════════════════╗");

        // Line 2: Description (one line)
        terminal.SetColor("white");
        terminal.WriteLine($" The {GetTimeOfDay()} streets of {GetTownName()}. The air is {GetWeather()}.");

        // Line 3: NPCs (compressed to one line)
        var liveNPCs = GetLiveNPCsAtLocation();
        if (liveNPCs.Count > 0)
        {
            terminal.SetColor("gray");
            terminal.Write(" You notice: ");
            terminal.SetColor("cyan");
            var names = liveNPCs.Take(2).Select(n => n.Name2).ToList();
            terminal.Write(string.Join(", ", names));
            if (liveNPCs.Count > 2)
            {
                terminal.SetColor("gray");
                terminal.Write($", and {liveNPCs.Count - 2} other{(liveNPCs.Count - 2 == 1 ? "" : "s")}");
            }
            terminal.WriteLine("");
        }

        // Line 4: blank
        terminal.WriteLine("");

        // Menu rows — progressive disclosure based on player level
        int tier = GetMenuTier();

        // Tier 1 (always): Core combat loop
        terminal.SetColor("darkgray");
        terminal.Write(" ["); terminal.SetColor("bright_yellow"); terminal.Write("D"); terminal.SetColor("darkgray"); terminal.Write("]");
        terminal.SetColor("white"); terminal.Write("ungeons    ");
        terminal.SetColor("darkgray"); terminal.Write("["); terminal.SetColor("bright_yellow"); terminal.Write("W"); terminal.SetColor("darkgray"); terminal.Write("]");
        terminal.SetColor("white"); terminal.Write("eapon Shop ");
        terminal.SetColor("darkgray"); terminal.Write("["); terminal.SetColor("bright_yellow"); terminal.Write("A"); terminal.SetColor("darkgray"); terminal.Write("]");
        terminal.SetColor("white"); terminal.Write("rmor Shop  ");
        terminal.SetColor("darkgray"); terminal.Write("["); terminal.SetColor("bright_yellow"); terminal.Write("M"); terminal.SetColor("darkgray"); terminal.Write("]");
        terminal.SetColor("white"); terminal.WriteLine("agic Shop");

        terminal.SetColor("darkgray");
        terminal.Write(" ["); terminal.SetColor("bright_yellow"); terminal.Write("I"); terminal.SetColor("darkgray"); terminal.Write("]");
        terminal.SetColor("white"); terminal.Write("nn         ");
        terminal.SetColor("darkgray"); terminal.Write("["); terminal.SetColor("bright_yellow"); terminal.Write("1"); terminal.SetColor("darkgray"); terminal.Write("]");
        terminal.SetColor("white"); terminal.Write("Healer     ");
        terminal.SetColor("darkgray"); terminal.Write("["); terminal.SetColor("bright_yellow"); terminal.Write("2"); terminal.SetColor("darkgray"); terminal.Write("]");
        terminal.SetColor("white"); terminal.Write("Quest Hall ");
        terminal.SetColor("darkgray"); terminal.Write("["); terminal.SetColor("bright_yellow"); terminal.Write("V"); terminal.SetColor("darkgray"); terminal.Write("]");
        terminal.SetColor("white"); terminal.WriteLine("Master");

        // Tier 2 (Level 3+): Town services
        if (tier >= 2)
        {
            terminal.SetColor("darkgray");
            terminal.Write(" ["); terminal.SetColor("bright_yellow"); terminal.Write("B"); terminal.SetColor("darkgray"); terminal.Write("]");
            terminal.SetColor("white"); terminal.Write("ank        ");
            terminal.SetColor("darkgray"); terminal.Write("["); terminal.SetColor("bright_yellow"); terminal.Write("T"); terminal.SetColor("darkgray"); terminal.Write("]");
            terminal.SetColor("white"); terminal.Write("emple      ");
            terminal.SetColor("darkgray"); terminal.Write("["); terminal.SetColor("bright_yellow"); terminal.Write("K"); terminal.SetColor("darkgray"); terminal.Write("]");
            terminal.SetColor("white"); terminal.Write("Castle     ");
            terminal.SetColor("darkgray"); terminal.Write("["); terminal.SetColor("bright_yellow"); terminal.Write("H"); terminal.SetColor("darkgray"); terminal.Write("]");
            terminal.SetColor("white"); terminal.WriteLine("ome");

            terminal.SetColor("darkgray");
            terminal.Write(" ["); terminal.SetColor("bright_yellow"); terminal.Write("N"); terminal.SetColor("darkgray"); terminal.Write("]");
            terminal.SetColor("white"); terminal.Write("ews        ");
            terminal.SetColor("darkgray"); terminal.Write("["); terminal.SetColor("bright_yellow"); terminal.Write("F"); terminal.SetColor("darkgray"); terminal.Write("]");
            terminal.SetColor("white"); terminal.WriteLine("ame");
        }

        // Tier 3 (Level 5+): Full menu
        if (tier >= 3)
        {
            terminal.SetColor("darkgray");
            terminal.Write(" ["); terminal.SetColor("bright_yellow"); terminal.Write("Y"); terminal.SetColor("darkgray"); terminal.Write("]");
            terminal.SetColor("gray"); terminal.Write("Dark Alley ");
            terminal.SetColor("darkgray"); terminal.Write("["); terminal.SetColor("bright_yellow"); terminal.Write("X"); terminal.SetColor("darkgray"); terminal.Write("]");
            terminal.SetColor("magenta"); terminal.Write("Love St    ");
            terminal.SetColor("darkgray"); terminal.Write("["); terminal.SetColor("bright_yellow"); terminal.Write("O"); terminal.SetColor("darkgray"); terminal.Write("]");
            terminal.SetColor("white"); terminal.Write("ld Church  ");
            terminal.SetColor("darkgray"); terminal.Write("["); terminal.SetColor("bright_yellow"); terminal.Write("J"); terminal.SetColor("darkgray"); terminal.Write("]");
            terminal.SetColor("white"); terminal.WriteLine("Auction");

            terminal.SetColor("darkgray");
            terminal.Write(" ["); terminal.SetColor("bright_yellow"); terminal.Write("C"); terminal.SetColor("darkgray"); terminal.Write("]");
            terminal.SetColor("white"); terminal.Write("hallenges  ");
            terminal.SetColor("darkgray"); terminal.Write("["); terminal.SetColor("bright_yellow"); terminal.Write("L"); terminal.SetColor("darkgray"); terminal.Write("]");
            terminal.SetColor("white"); terminal.Write("odging     ");
            terminal.SetColor("darkgray"); terminal.Write("["); terminal.SetColor("bright_yellow"); terminal.Write("="); terminal.SetColor("darkgray"); terminal.Write("]");
            terminal.SetColor("white"); terminal.Write("Stats      ");
            terminal.SetColor("darkgray"); terminal.Write("["); terminal.SetColor("bright_yellow"); terminal.Write("P"); terminal.SetColor("darkgray"); terminal.Write("]");
            terminal.SetColor("white"); terminal.WriteLine("rogress");

            terminal.SetColor("darkgray");
            terminal.Write(" ["); terminal.SetColor("bright_yellow"); terminal.Write("R"); terminal.SetColor("darkgray"); terminal.Write("]");
            terminal.SetColor("white"); terminal.Write("elations   ");
        }

        // Always: Quit + Settings
        terminal.SetColor("darkgray");
        if (tier < 3) terminal.Write(" "); // indent if not continuing a row
        terminal.Write("["); terminal.SetColor("bright_yellow"); terminal.Write("Q"); terminal.SetColor("darkgray"); terminal.Write("]");
        terminal.SetColor("gray"); terminal.Write("uit        ");
        terminal.SetColor("darkgray"); terminal.Write("["); terminal.SetColor("bright_yellow"); terminal.Write("~"); terminal.SetColor("darkgray"); terminal.Write("]");
        terminal.SetColor("gray"); terminal.WriteLine("Settings");

        // Online multiplayer row (only in online mode)
        if (DoorMode.IsOnlineMode && OnlineChatSystem.IsActive)
        {
            int onlineCount = OnlineStateManager.Instance?.CachedOnlinePlayerCount ?? 0;
            terminal.SetColor("bright_green");
            terminal.Write(" ── Online ── ");
            terminal.SetColor("darkgray"); terminal.Write("["); terminal.SetColor("bright_yellow"); terminal.Write("3"); terminal.SetColor("darkgray"); terminal.Write("]");
            terminal.SetColor("white"); terminal.Write($"Who({onlineCount}) ");
            terminal.SetColor("darkgray"); terminal.Write("["); terminal.SetColor("bright_yellow"); terminal.Write("4"); terminal.SetColor("darkgray"); terminal.Write("]");
            terminal.SetColor("white"); terminal.Write("Chat ");
            terminal.SetColor("darkgray"); terminal.Write("["); terminal.SetColor("bright_yellow"); terminal.Write("5"); terminal.SetColor("darkgray"); terminal.Write("]");
            terminal.SetColor("white"); terminal.Write("News ");
            terminal.SetColor("darkgray"); terminal.Write("["); terminal.SetColor("bright_yellow"); terminal.Write("6"); terminal.SetColor("darkgray"); terminal.Write("]");
            terminal.SetColor("white"); terminal.Write("Arena ");
            terminal.SetColor("darkgray"); terminal.Write("["); terminal.SetColor("bright_yellow"); terminal.Write("7"); terminal.SetColor("darkgray"); terminal.Write("]");
            terminal.SetColor("white"); terminal.WriteLine("Boss");
        }

        // Blank line
        terminal.WriteLine("");

        // Training points reminder (level-ups are now automatic)
        if (currentPlayer.TrainingPoints > 0)
        {
            terminal.SetColor("bright_yellow");
            terminal.Write(" >>> ");
            terminal.SetColor("bright_green");
            terminal.Write($"You have {currentPlayer.TrainingPoints} training points! Visit the Level Master to spend them!");
            terminal.SetColor("bright_yellow");
            terminal.WriteLine(" <<<");
        }

        // New player dungeon guidance (Level 1 with zero kills)
        if (currentPlayer.Level == 1 && currentPlayer.MKills == 0)
        {
            terminal.SetColor("bright_yellow");
            terminal.Write(" >>> ");
            terminal.SetColor("bright_white");
            terminal.Write("New adventurer? Press ");
            terminal.SetColor("bright_yellow");
            terminal.Write("[D]");
            terminal.SetColor("bright_white");
            terminal.Write(" to enter the Dungeons!");
            terminal.SetColor("bright_yellow");
            terminal.WriteLine(" <<<");
        }

        // Line 14: Status line (compact)
        terminal.SetColor("gray");
        terminal.Write(" HP:");
        float hpPct = currentPlayer.MaxHP > 0 ? (float)currentPlayer.HP / currentPlayer.MaxHP : 0;
        terminal.SetColor(hpPct > 0.5f ? "bright_green" : hpPct > 0.25f ? "yellow" : "bright_red");
        terminal.Write($"{currentPlayer.HP}/{currentPlayer.MaxHP}");
        terminal.SetColor("gray");
        terminal.Write(" Gold:");
        terminal.SetColor("yellow");
        terminal.Write($"{currentPlayer.Gold:N0}");
        if (currentPlayer.MaxMana > 0)
        {
            terminal.SetColor("gray");
            terminal.Write(" Mana:");
            terminal.SetColor("blue");
            terminal.Write($"{currentPlayer.Mana}/{currentPlayer.MaxMana}");
        }
        terminal.SetColor("gray");
        terminal.Write(" Lv:");
        terminal.SetColor("cyan");
        terminal.Write($"{currentPlayer.Level}");
        if (currentPlayer.Level < GameConfig.MaxLevel)
        {
            long curXP = currentPlayer.Experience;
            long nextXP = GetExperienceForLevel(currentPlayer.Level + 1);
            long prevXP = GetExperienceForLevel(currentPlayer.Level);
            long xpInto = curXP - prevXP;
            long xpNeed = nextXP - prevXP;
            int pct = xpNeed > 0 ? (int)((xpInto * 100) / xpNeed) : 0;
            pct = Math.Clamp(pct, 0, 100);
            terminal.SetColor("gray");
            terminal.Write($"({pct}%)");
        }
        terminal.WriteLine("");

        // Line 15: Quick commands (compact)
        terminal.SetColor("darkgray");
        terminal.Write(" ["); terminal.SetColor("bright_yellow"); terminal.Write("S"); terminal.SetColor("darkgray"); terminal.Write("]");
        terminal.SetColor("white"); terminal.Write("tatus ");
        terminal.SetColor("darkgray"); terminal.Write("["); terminal.SetColor("bright_yellow"); terminal.Write("*"); terminal.SetColor("darkgray"); terminal.Write("]");
        terminal.SetColor("white"); terminal.Write("Inv ");
        terminal.SetColor("darkgray"); terminal.Write("["); terminal.SetColor("bright_yellow"); terminal.Write("?"); terminal.SetColor("darkgray"); terminal.Write("]");
        terminal.SetColor("white"); terminal.Write("Help ");
        if (liveNPCs.Count > 0)
        {
            terminal.SetColor("darkgray"); terminal.Write("["); terminal.SetColor("bright_yellow"); terminal.Write("0"); terminal.SetColor("darkgray"); terminal.Write("]");
            terminal.SetColor("white"); terminal.Write($"Talk({liveNPCs.Count}) ");
        }
        terminal.SetColor("darkgray"); terminal.Write("["); terminal.SetColor("bright_yellow"); terminal.Write("~"); terminal.SetColor("darkgray"); terminal.Write("]");
        terminal.SetColor("white"); terminal.Write("Prefs ");
        terminal.SetColor("darkgray"); terminal.Write("["); terminal.SetColor("bright_yellow"); terminal.Write("!"); terminal.SetColor("darkgray"); terminal.Write("]");
        terminal.SetColor("white"); terminal.Write("Bug");
        terminal.WriteLine("");

        // Line 16: Bottom border
        terminal.SetColor("bright_blue");
        terminal.WriteLine("╚══════════════════════════════════════════════════════════════════════════════╝");
    }

    /// <summary>
    /// Shows a message if the player is eligible for a level raise
    /// </summary>
    private void ShowLevelEligibilityMessage()
    {
        if (currentPlayer.Level >= GameConfig.MaxLevel)
            return;

        long experienceNeeded = GetExperienceForLevel(currentPlayer.Level + 1);

        if (currentPlayer.Experience >= experienceNeeded)
        {
            terminal.WriteLine("");
            terminal.SetColor("bright_yellow");
            terminal.WriteLine("╔══════════════════════════════════════════════════════════════════════════════╗");
            terminal.SetColor("bright_green");
            terminal.WriteLine("║     * You are eligible for a level raise! Visit your Master to advance! *    ║");
            terminal.SetColor("bright_yellow");
            terminal.WriteLine("╚══════════════════════════════════════════════════════════════════════════════╝");
            terminal.WriteLine("");
        }
    }

    /// <summary>
    /// Experience required to have the specified level (cumulative)
    /// </summary>
    private static long GetExperienceForLevel(int level)
    {
        if (level <= 1) return 0;
        long exp = 0;
        for (int i = 2; i <= level; i++)
        {
            // Softened curve: level^2.0 * 50 (rebalanced v0.41.4)
            exp += (long)(Math.Pow(i, 2.0) * 50);
        }
        return exp;
    }

    /// <summary>
    /// Show the Main Street menu - routes to appropriate style based on accessibility settings
    /// </summary>
    private void ShowMainStreetMenu()
    {
        if (currentPlayer.ScreenReaderMode)
        {
            ShowScreenReaderMenu();
        }
        else
        {
            ShowClassicMenu();
        }
    }

    /// <summary>
    /// Show the classic Main Street menu layout (v0.4 style)
    /// Progressive disclosure: Tier 1 (Lv1-2) core loop, Tier 2 (Lv3-4) town services, Tier 3 (Lv5+) full menu.
    /// All keys still work at all levels — only the display is gated.
    /// </summary>
    private void ShowClassicMenu()
    {
        int tier = GetMenuTier();
        terminal.WriteLine("");

        // Row 1 - Primary locations (D/I always, T/O tier 2+)
        terminal.SetColor("darkgray");
        terminal.Write(" [");
        terminal.SetColor("bright_yellow");
        terminal.Write("D");
        terminal.SetColor("darkgray");
        terminal.Write("]");
        terminal.SetColor("white");
        terminal.Write("ungeons     ");

        terminal.SetColor("darkgray");
        terminal.Write("[");
        terminal.SetColor("bright_yellow");
        terminal.Write("I");
        terminal.SetColor("darkgray");
        terminal.Write("]");
        terminal.SetColor("white");
        terminal.Write("nn          ");

        if (tier >= 2)
        {
            terminal.SetColor("darkgray");
            terminal.Write("[");
            terminal.SetColor("bright_yellow");
            terminal.Write("T");
            terminal.SetColor("darkgray");
            terminal.Write("]");
            terminal.SetColor("white");
            terminal.Write("emple       ");

            terminal.SetColor("darkgray");
            terminal.Write("[");
            terminal.SetColor("bright_yellow");
            terminal.Write("O");
            terminal.SetColor("darkgray");
            terminal.Write("]");
            terminal.SetColor("white");
            terminal.Write("ld Church");
        }
        terminal.WriteLine("");

        // Row 2 - Shops (W/A/M always, J tier 3+)
        terminal.SetColor("darkgray");
        terminal.Write(" [");
        terminal.SetColor("bright_yellow");
        terminal.Write("W");
        terminal.SetColor("darkgray");
        terminal.Write("]");
        terminal.SetColor("white");
        terminal.Write("eapon Shop  ");

        terminal.SetColor("darkgray");
        terminal.Write("[");
        terminal.SetColor("bright_yellow");
        terminal.Write("A");
        terminal.SetColor("darkgray");
        terminal.Write("]");
        terminal.SetColor("white");
        terminal.Write("rmor Shop   ");

        terminal.SetColor("darkgray");
        terminal.Write("[");
        terminal.SetColor("bright_yellow");
        terminal.Write("M");
        terminal.SetColor("darkgray");
        terminal.Write("]");
        terminal.SetColor("white");
        terminal.Write("agic Shop   ");

        if (tier >= 3)
        {
            terminal.SetColor("darkgray");
            terminal.Write("[");
            terminal.SetColor("bright_yellow");
            terminal.Write("J");
            terminal.SetColor("darkgray");
            terminal.Write("]");
            terminal.SetColor("white");
            terminal.Write("Auction House");
        }
        terminal.WriteLine("");

        // Row 3 - Services (1/2/V always, B tier 2+)
        terminal.SetColor("darkgray");
        terminal.Write(" ");
        if (tier >= 2)
        {
            terminal.Write("[");
            terminal.SetColor("bright_yellow");
            terminal.Write("B");
            terminal.SetColor("darkgray");
            terminal.Write("]");
            terminal.SetColor("white");
            terminal.Write("ank         ");
            terminal.SetColor("darkgray");
        }

        terminal.Write("[");
        terminal.SetColor("bright_yellow");
        terminal.Write("1");
        terminal.SetColor("darkgray");
        terminal.Write("]");
        terminal.SetColor("white");
        terminal.Write("Healer      ");

        terminal.SetColor("darkgray");
        terminal.Write("[");
        terminal.SetColor("bright_yellow");
        terminal.Write("2");
        terminal.SetColor("darkgray");
        terminal.Write("]");
        terminal.SetColor("white");
        terminal.Write("Quest Hall  ");

        terminal.SetColor("darkgray");
        terminal.Write("[");
        terminal.SetColor("bright_yellow");
        terminal.Write("V");
        terminal.SetColor("darkgray");
        terminal.Write("]");
        terminal.SetColor("white");
        terminal.WriteLine("isit Master");

        // Row 4 - Important locations (K/H tier 2+, C/L tier 3+)
        if (tier >= 2)
        {
            terminal.SetColor("darkgray");
            terminal.Write(" [");
            terminal.SetColor("bright_yellow");
            terminal.Write("K");
            terminal.SetColor("darkgray");
            terminal.Write("]");
            terminal.SetColor("white");
            terminal.Write("Castle      ");

            terminal.SetColor("darkgray");
            terminal.Write("[");
            terminal.SetColor("bright_yellow");
            terminal.Write("H");
            terminal.SetColor("darkgray");
            terminal.Write("]");
            terminal.SetColor("white");
            terminal.Write("ome         ");

            if (tier >= 3)
            {
                terminal.SetColor("darkgray");
                terminal.Write("[");
                terminal.SetColor("bright_yellow");
                terminal.Write("C");
                terminal.SetColor("darkgray");
                terminal.Write("]");
                terminal.SetColor("white");
                terminal.Write("hallenges   ");

                terminal.SetColor("darkgray");
                terminal.Write("[");
                terminal.SetColor("bright_yellow");
                terminal.Write("L");
                terminal.SetColor("darkgray");
                terminal.Write("]");
                terminal.SetColor("white");
                terminal.Write("odging");
            }
            terminal.WriteLine("");
        }

        terminal.WriteLine("");

        // Row 5 - Information (S always, N/F tier 2+)
        terminal.SetColor("darkgray");
        terminal.Write(" [");
        terminal.SetColor("bright_yellow");
        terminal.Write("S");
        terminal.SetColor("darkgray");
        terminal.Write("]");
        terminal.SetColor("white");
        terminal.Write("tatus       ");

        if (tier >= 2)
        {
            terminal.SetColor("darkgray");
            terminal.Write("[");
            terminal.SetColor("bright_yellow");
            terminal.Write("N");
            terminal.SetColor("darkgray");
            terminal.Write("]");
            terminal.SetColor("white");
            terminal.Write("ews         ");

            terminal.SetColor("darkgray");
            terminal.Write("[");
            terminal.SetColor("bright_yellow");
            terminal.Write("F");
            terminal.SetColor("darkgray");
            terminal.Write("]");
            terminal.SetColor("white");
            terminal.Write("ame         ");
        }
        terminal.WriteLine("");

        // Row 6 - Stats & Progress (tier 3+)
        if (tier >= 3)
        {
            terminal.SetColor("darkgray");
            terminal.Write(" [");
            terminal.SetColor("bright_yellow");
            terminal.Write("=");
            terminal.SetColor("darkgray");
            terminal.Write("]");
            terminal.SetColor("white");
            terminal.Write("Stats       ");

            terminal.SetColor("darkgray");
            terminal.Write("[");
            terminal.SetColor("bright_yellow");
            terminal.Write("P");
            terminal.SetColor("darkgray");
            terminal.Write("]");
            terminal.SetColor("white");
            terminal.Write("rogress     ");

            terminal.SetColor("darkgray");
            terminal.Write("[");
            terminal.SetColor("bright_yellow");
            terminal.Write("R");
            terminal.SetColor("darkgray");
            terminal.Write("]");
            terminal.SetColor("white");
            terminal.WriteLine("elations");
        }

        // Row 7 - Shady areas + Quit (Y/X tier 3+, Q/~ always)
        terminal.SetColor("darkgray");
        terminal.Write(" ");
        if (tier >= 3)
        {
            terminal.Write("[");
            terminal.SetColor("bright_yellow");
            terminal.Write("Y");
            terminal.SetColor("darkgray");
            terminal.Write("]");
            terminal.SetColor("gray");
            terminal.Write("Dark Alley  ");

            terminal.SetColor("darkgray");
            terminal.Write("[");
            terminal.SetColor("bright_yellow");
            terminal.Write("X");
            terminal.SetColor("darkgray");
            terminal.Write("]");
            terminal.SetColor("magenta");
            terminal.Write("Love Street ");
            terminal.SetColor("darkgray");
        }

        terminal.Write("[");
        terminal.SetColor("bright_yellow");
        terminal.Write("Q");
        terminal.SetColor("darkgray");
        terminal.Write("]");
        terminal.SetColor("gray");
        terminal.Write("uit Game    ");

        terminal.SetColor("darkgray");
        terminal.Write("[");
        terminal.SetColor("bright_yellow");
        terminal.Write("~");
        terminal.SetColor("darkgray");
        terminal.Write("]");
        terminal.SetColor("gray");
        terminal.WriteLine("Settings");

        // Online multiplayer section (only shown in online mode)
        if (DoorMode.IsOnlineMode && OnlineChatSystem.IsActive)
        {
            terminal.WriteLine("");
            terminal.SetColor("bright_green");
            terminal.Write(" ═══ ");
            terminal.SetColor("bright_white");
            terminal.Write("Online");
            terminal.SetColor("bright_green");
            terminal.WriteLine(" ═══");

            // Show online player count
            int onlineCount = OnlineStateManager.Instance?.CachedOnlinePlayerCount ?? 0;

            terminal.SetColor("darkgray");
            terminal.Write(" [");
            terminal.SetColor("bright_yellow");
            terminal.Write("3");
            terminal.SetColor("darkgray");
            terminal.Write("]");
            terminal.SetColor("white");
            terminal.Write($"Who's Online ");
            terminal.SetColor("bright_green");
            terminal.Write($"({onlineCount}");
            terminal.SetColor("white");
            terminal.WriteLine($" player{(onlineCount != 1 ? "s" : "")})");

            terminal.SetColor("darkgray");
            terminal.Write(" [");
            terminal.SetColor("bright_yellow");
            terminal.Write("4");
            terminal.SetColor("darkgray");
            terminal.Write("]");
            terminal.SetColor("white");
            terminal.Write("Chat         ");

            terminal.SetColor("darkgray");
            terminal.Write("[");
            terminal.SetColor("bright_yellow");
            terminal.Write("5");
            terminal.SetColor("darkgray");
            terminal.Write("]");
            terminal.SetColor("white");
            terminal.Write("News Feed    ");

            terminal.SetColor("darkgray");
            terminal.Write("[");
            terminal.SetColor("bright_yellow");
            terminal.Write("6");
            terminal.SetColor("darkgray");
            terminal.Write("]");
            terminal.SetColor("white");
            terminal.Write("Arena (PvP)  ");

            terminal.SetColor("darkgray");
            terminal.Write("[");
            terminal.SetColor("bright_yellow");
            terminal.Write("7");
            terminal.SetColor("darkgray");
            terminal.Write("]");
            terminal.SetColor("white");
            terminal.WriteLine("World Boss");
        }

        terminal.WriteLine("");
        terminal.SetColor("bright_cyan");
        terminal.WriteLine("╚═════════════════════════════════════════════════════════════════════════════╝");
        terminal.SetColor("white");
        terminal.WriteLine("");
    }

    /// <summary>
    /// Show simplified menu for screen readers - plain text, one option per line
    /// </summary>
    private void ShowScreenReaderMenu()
    {
        int tier = GetMenuTier();
        terminal.WriteLine("");
        terminal.WriteLine("Main Street Menu");
        terminal.WriteLine("");

        terminal.WriteLine("Locations:");
        terminal.WriteLine("  D - Dungeons");
        terminal.WriteLine("  I - Inn");
        if (tier >= 2)
        {
            terminal.WriteLine("  T - Temple");
            terminal.WriteLine("  K - Castle");
            terminal.WriteLine("  H - Home");
        }
        if (tier >= 3)
        {
            terminal.WriteLine("  O - Old Church");
            terminal.WriteLine("  L - Lodging (Dormitory)");
        }
        terminal.WriteLine("");

        terminal.WriteLine("Shops:");
        terminal.WriteLine("  W - Weapon Shop");
        terminal.WriteLine("  A - Armor Shop");
        terminal.WriteLine("  M - Magic Shop");
        if (tier >= 3) terminal.WriteLine("  J - Auction House");
        if (tier >= 2) terminal.WriteLine("  B - Bank");
        terminal.WriteLine("  1 - Healer");
        terminal.WriteLine("");

        terminal.WriteLine("Services:");
        terminal.WriteLine("  V - Visit Master");
        terminal.WriteLine("  2 - Quest Hall");
        if (tier >= 3) terminal.WriteLine("  C - Challenges");
        terminal.WriteLine("");

        if (tier >= 2)
        {
            terminal.WriteLine("Information:");
            terminal.WriteLine("  S - Status");
            terminal.WriteLine("  N - News");
            terminal.WriteLine("  F - Fame");
            if (tier >= 3)
            {
                terminal.WriteLine("  = - Stats Record");
                terminal.WriteLine("  P - Progress");
                terminal.WriteLine("  R - Relations");
            }
            terminal.WriteLine("");
        }
        else
        {
            // Tier 1 only shows Status
            terminal.WriteLine("  S - Status");
            terminal.WriteLine("");
        }

        terminal.WriteLine("Other:");
        if (tier >= 3)
        {
            terminal.WriteLine("  Y - Dark Alley");
            terminal.WriteLine("  X - Love Street");
        }
        terminal.WriteLine("  Q - Quit Game");
        terminal.WriteLine("  ? - Help");
        terminal.WriteLine("  ! - Report Bug");
        terminal.WriteLine("");

        if (DoorMode.IsOnlineMode && OnlineChatSystem.IsActive)
        {
            terminal.WriteLine("Online:");
            terminal.WriteLine("  3 - Who's Online");
            terminal.WriteLine("  4 - Chat");
            terminal.WriteLine("  5 - News Feed");
            terminal.WriteLine("  6 - Arena (PvP)");
            terminal.WriteLine("  7 - World Boss");
            terminal.WriteLine("  /say message - Broadcast chat");
            terminal.WriteLine("  /tell player message - Private message");
            terminal.WriteLine("  /who - See online players");
            terminal.WriteLine("  /news - Recent news");
            terminal.WriteLine("");
        }
    }
    
    protected override async Task<bool> ProcessChoice(string choice)
    {
        // Handle global quick commands first
        var (handled, shouldExit) = await TryProcessGlobalCommand(choice);
        if (handled) return shouldExit;

        if (string.IsNullOrWhiteSpace(choice))
            return false;

        var upperChoice = choice.ToUpper().Trim();

        // Handle Main Street specific commands
        switch (upperChoice)
        {
            case "S":
                await ShowStatus();
                return false;
                
            case "D":
                await NavigateToLocation(GameLocation.Dungeons);
                return true;
                
            case "B":
                await NavigateToLocation(GameLocation.Bank);
                return true;
                
            case "I":
                await NavigateToLocation(GameLocation.TheInn);
                return true;
                
            case "C":
                await NavigateToLocation(GameLocation.AnchorRoad); // Challenges
                return true;

            case "L":
                await NavigateToLocation(GameLocation.Dormitory); // Lodging
                return true;

            case "A":
                await NavigateToLocation(GameLocation.ArmorShop);
                return true;
                
            case "W":
                await NavigateToLocation(GameLocation.WeaponShop);
                return true;
                
            case "H":
                await NavigateToLocation(GameLocation.Home);
                return true;
                
            case "F":
                await ShowFame();
                return false;
                
            case "1":
                await NavigateToLocation(GameLocation.Healer);
                return true;

            case "2":
                await NavigateToLocation(GameLocation.QuestHall);
                return true;

            case "Q":
                return await QuitGame();
                
            case "G":
                await ShowGoodDeeds();
                return false;
                
            case "E":
                await ShowEvilDeeds();
                return false;
                
            case "V":
                await NavigateToLocation(GameLocation.Master);
                return true;
                
            case "M":
                await NavigateToLocation(GameLocation.MagicShop);
                return true;
                
            case "N":
                var newsLocation = new NewsLocation();
                await newsLocation.EnterLocation(currentPlayer, terminal);
                return false; // Stay in main street after returning from news

            case "$":
                await ShowWorldEvents();
                return false;
                
            // Team Area removed from Main Street - access via Inn only

            // List Citizens removed - merged into Fame (F) which now shows locations
            // case "L":
            //     await ListCharacters();
            //     return false;
                
            case "T":
                terminal.WriteLine("You enter the Temple of the Gods...", "cyan");
                await Task.Delay(1500);
                throw new LocationExitException(GameLocation.Temple);
                
            case "X":
                terminal.WriteLine("You head to Love Street...", "magenta");
                await Task.Delay(1500);
                throw new LocationExitException(GameLocation.LoveCorner);
                
            case "J":
                if (DoorMode.IsOnlineMode)
                {
                    await ShowAuctionMenu();
                    return false;
                }
                else
                {
                    await NavigateToLocation(GameLocation.AuctionHouse);
                    return true;
                }
                
            case "R":
                await ShowRelations();
                return false;

            case "*":
                await ShowInventory();
                return false;

            case "=":
                await ShowStatistics();
                return false;

            // Achievements removed - available via Trophy Room at Home
            // case "!":
            //     await ShowAchievements();
            //     return false;

            case "9":
                await TestCombat();
                return false;
            
            // Quick navigation
            case "K":
                await NavigateToLocation(GameLocation.Castle);
                return true;
                
            case "P":
                await ShowStoryProgress();
                return false;
                
            case "O":
                await NavigateToLocation(GameLocation.Church);
                return true;

            // Assault removed - players can challenge NPCs via Talk feature
            // case "U":
            //     await AttackSomeone();
            //     return false;

            case "Y":
                terminal.WriteLine("You head to the Dark Alley...", "gray");
                await Task.Delay(1500);
                throw new LocationExitException(GameLocation.DarkAlley);
                
            case "?":
                await ShowHelp();
                return false;

            case "!":
                await BugReportSystem.ReportBug(terminal, currentPlayer);
                return false;

            case "3":
                if (DoorMode.IsOnlineMode && OnlineChatSystem.IsActive)
                {
                    await OnlineChatSystem.Instance!.ShowWhosOnline(terminal);
                }
                else
                {
                    await ListCharacters();
                }
                return false;

            case "4":
                if (DoorMode.IsOnlineMode && OnlineChatSystem.IsActive)
                {
                    terminal.SetColor("bright_cyan");
                    terminal.Write("  Say: ");
                    terminal.SetColor("white");
                    var chatMsg = await terminal.GetInput("");
                    if (!string.IsNullOrWhiteSpace(chatMsg))
                    {
                        await OnlineChatSystem.Instance!.Say(chatMsg);
                        terminal.SetColor("cyan");
                        terminal.WriteLine($"  [You] {chatMsg}");
                        await Task.Delay(1000);
                    }
                }
                return false;

            case "5":
                if (DoorMode.IsOnlineMode && OnlineChatSystem.IsActive)
                {
                    await OnlineChatSystem.Instance!.ShowNews(terminal);
                }
                return false;

            case "6":
                if (DoorMode.IsOnlineMode)
                {
                    throw new LocationExitException(GameLocation.Arena);
                }
                return false;

            case "7":
                if (DoorMode.IsOnlineMode)
                {
                    await ShowWorldBossMenu();
                }
                return false;

            case "SETTINGS":
            case "CONFIG":
                await ShowSettingsMenu();
                return false;
                
            case "MAIL":
            case "CTRL+M":
            case "!M":
                await ShowMail();
                return false;

            // Secret dev menu - hidden command
            case "DEV":
            case "CHEATER":
            case "DEVMENU":
                await EnterDevMenu();
                return false;

            // Talk to NPCs
            case "0":
            case "TALK":
                await TalkToNPC();
                return false;

            // Quick preferences (accessible from any location)
            case "~":
            case "PREFS":
            case "PREFERENCES":
                await ShowPreferencesMenu();
                return false;

            default:
                terminal.WriteLine("Invalid choice! Type ? for help.", "red");
                await Task.Delay(1500);
                return false;
        }
    }
    
    // Main Street specific action implementations
    private async Task ShowGoodDeeds()
    {
        terminal.ClearScreen();
        terminal.SetColor("bright_white");
        terminal.WriteLine("Good Deeds");
        terminal.WriteLine("==========");
        terminal.WriteLine("");
        terminal.SetColor("white");
        terminal.WriteLine($"Your Chivalry: {currentPlayer.Chivalry}");
        terminal.WriteLine($"Good deeds left today: {currentPlayer.ChivNr}");
        terminal.WriteLine("");
        
        if (currentPlayer.ChivNr > 0)
        {
            terminal.WriteLine("Available good deeds:");
            terminal.WriteLine("1. Give gold to the poor");
            terminal.WriteLine("2. Help at the temple");
            terminal.WriteLine("3. Volunteer at orphanage");
            terminal.WriteLine("");
            
            var choice = await terminal.GetInput("Choose a deed (1-3, 0 to cancel): ");
            await ProcessGoodDeed(choice);
        }
        else
        {
            terminal.WriteLine("You have done enough good for today.", "yellow");
        }
        
        await terminal.PressAnyKey();
    }
    
    private async Task ShowEvilDeeds()
    {
        terminal.ClearScreen();
        terminal.SetColor("red");
        terminal.WriteLine("Evil Deeds");
        terminal.WriteLine("==========");
        terminal.WriteLine("");
        terminal.SetColor("white");
        terminal.WriteLine($"Your Darkness: {currentPlayer.Darkness}");
        terminal.WriteLine($"Dark deeds left today: {currentPlayer.DarkNr}");
        terminal.WriteLine("");
        
        if (currentPlayer.DarkNr > 0)
        {
            terminal.WriteLine("Available dark deeds:");
            terminal.WriteLine("1. Rob from the poor");
            terminal.WriteLine("2. Vandalize property");
            terminal.WriteLine("3. Spread malicious rumors");
            terminal.WriteLine("");
            
            var choice = await terminal.GetInput("Choose a deed (1-3, 0 to cancel): ");
            await ProcessEvilDeed(choice);
        }
        else
        {
            terminal.WriteLine("You have caused enough trouble for today.", "yellow");
        }
        
        await terminal.PressAnyKey();
    }
    
    private async Task NavigateToTeamCorner()
    {
        terminal.WriteLine("You head to the Adventurers Team Corner...", "yellow");
        await Task.Delay(1000);
        
        // Navigate to TeamCornerLocation
        await NavigateToLocation(GameLocation.TeamCorner);
    }
    
    private async Task ShowFame()
    {
        // Get all characters (player + NPCs) and rank them
        var npcs = NPCSpawnSystem.Instance.ActiveNPCs;

        // If no NPCs, try to initialize them
        if (npcs == null || npcs.Count == 0)
        {
            await NPCSpawnSystem.Instance.InitializeClassicNPCs();
            npcs = NPCSpawnSystem.Instance.ActiveNPCs;
        }

        // Build a list of all characters for ranking (now includes location)
        var allCharacters = new List<(string Name, int Level, string Class, long Experience, string Location, bool IsPlayer, bool IsAlive)>();

        // Add player
        allCharacters.Add((currentPlayer.DisplayName, currentPlayer.Level, currentPlayer.Class.ToString(), currentPlayer.Experience, "Main Street", true, currentPlayer.IsAlive));

        // Add other online players from the database
        if (UsurperRemake.Systems.OnlineStateManager.IsActive)
        {
            try
            {
                var onlinePlayers = await UsurperRemake.Systems.OnlineStateManager.Instance!.GetAllPlayerSummaries();
                foreach (var op in onlinePlayers)
                {
                    // Skip current player (already added above)
                    if (op.DisplayName.Equals(currentPlayer.DisplayName, StringComparison.OrdinalIgnoreCase))
                        continue;

                    string className = ((CharacterClass)op.ClassId).ToString();
                    string onlineTag = op.IsOnline ? "[ON]" : "";
                    allCharacters.Add((op.DisplayName, op.Level, className, op.Experience, onlineTag, false, true));
                }
            }
            catch { /* If DB query fails, just show NPCs */ }
        }

        // Add NPCs
        if (npcs != null)
        {
            foreach (var npc in npcs)
            {
                string location = string.IsNullOrEmpty(npc.CurrentLocation) ? "???" : npc.CurrentLocation;
                allCharacters.Add((npc.Name, npc.Level, npc.Class.ToString(), npc.Experience, location, false, npc.IsAlive));
            }
        }

        // Sort by level (desc), then experience (desc), then name
        var ranked = allCharacters
            .Where(c => c.IsAlive)
            .OrderByDescending(c => c.Level)
            .ThenByDescending(c => c.Experience)
            .ThenBy(c => c.Name)
            .ToList();

        // Find player's rank
        int playerRank = ranked.FindIndex(c => c.IsPlayer) + 1;
        if (playerRank == 0) playerRank = ranked.Count + 1; // Player is dead

        int currentPage = 0;
        int itemsPerPage = 15;
        int totalPages = Math.Max(1, (ranked.Count + itemsPerPage - 1) / itemsPerPage);

        while (true)
        {
            terminal.ClearScreen();
            terminal.SetColor("bright_yellow");
            terminal.WriteLine("╔══════════════════════════════════════════════════════════════════════════════╗");
            terminal.WriteLine("║                           -= HALL OF FAME =-                                 ║");
            terminal.WriteLine("║                      The Greatest Heroes of the Realm                        ║");
            terminal.WriteLine("╠══════════════════════════════════════════════════════════════════════════════╣");
            terminal.WriteLine("");

            // Show player's rank
            terminal.SetColor("bright_cyan");
            terminal.WriteLine($"  Your Rank: #{playerRank} of {ranked.Count} - {currentPlayer.DisplayName} (Level {currentPlayer.Level})");
            terminal.WriteLine("");

            // Column headers (adjusted for location)
            terminal.SetColor("gray");
            terminal.WriteLine($"  {"Rank",-5} {"Name",-18} {"Lv",3} {"Class",-10} {"Location",-12} {"Experience",10}");
            terminal.WriteLine($"  {"────",-5} {"──────────────────",-18} {"──",3} {"──────────",-10} {"────────────",-12} {"──────────",10}");

            // Display current page
            int startIdx = currentPage * itemsPerPage;
            int endIdx = Math.Min(startIdx + itemsPerPage, ranked.Count);

            for (int i = startIdx; i < endIdx; i++)
            {
                var entry = ranked[i];
                int rank = i + 1;

                // Color coding
                string color;
                if (entry.IsPlayer)
                    color = "bright_green";
                else if (rank <= 3)
                    color = rank == 1 ? "bright_yellow" : (rank == 2 ? "white" : "yellow");
                else if (entry.Level > currentPlayer.Level)
                    color = "bright_red";
                else
                    color = "gray";

                terminal.SetColor(color);

                string rankStr = rank <= 3 ? $"#{rank}" : $"{rank}.";
                string marker = entry.IsPlayer ? "*" : " ";
                string nameDisplay = entry.Name.Length > 17 ? entry.Name.Substring(0, 17) : entry.Name;
                string classDisplay = entry.Class.Length > 10 ? entry.Class.Substring(0, 10) : entry.Class;
                string locDisplay = entry.Location.Length > 12 ? entry.Location.Substring(0, 12) : entry.Location;
                terminal.WriteLine($"  {rankStr,-5}{marker}{nameDisplay,-17} {entry.Level,3} {classDisplay,-10} {locDisplay,-12} {entry.Experience,10:N0}");
            }

            terminal.WriteLine("");
            terminal.SetColor("bright_yellow");
            terminal.WriteLine("╚══════════════════════════════════════════════════════════════════════════════╝");
            terminal.WriteLine("");

            // Navigation
            terminal.SetColor("cyan");
            terminal.WriteLine($"  Page {currentPage + 1}/{totalPages}");
            var options = new List<string>();
            if (currentPage > 0) options.Add("[P]rev");
            if (currentPage < totalPages - 1) options.Add("[N]ext");
            options.Add("[R]eturn");
            terminal.WriteLine($"  {string.Join("  ", options)}");

            string input = (await terminal.GetKeyInput()).ToUpperInvariant();

            if (input == "P" && currentPage > 0)
                currentPage--;
            else if (input == "N" && currentPage < totalPages - 1)
                currentPage++;
            else if (input == "R" || input == "Q" || input == "ESCAPE")
                break;
        }
    }
    
    private async Task ListCharacters()
    {
        // Get NPCs from the spawn system
        var npcs = NPCSpawnSystem.Instance.ActiveNPCs;

        // Debug: If no NPCs, try to initialize them
        if (npcs == null || npcs.Count == 0)
        {
            // GD.Print("[ListCharacters] No NPCs found, attempting to initialize...");
            await NPCSpawnSystem.Instance.InitializeClassicNPCs();
            npcs = NPCSpawnSystem.Instance.ActiveNPCs;
            // GD.Print($"[ListCharacters] After init: {npcs?.Count ?? 0} NPCs");
        }

        var aliveNPCs = npcs?.Where(n => n.IsAlive).OrderByDescending(n => n.Level).ThenBy(n => n.Name).ToList() ?? new List<NPC>();
        var deadNPCs = npcs?.Where(n => !n.IsAlive).OrderByDescending(n => n.Level).ThenBy(n => n.Name).ToList() ?? new List<NPC>();

        int currentPage = 0;
        int itemsPerPage = 18;
        int totalAlivePages = (aliveNPCs.Count + itemsPerPage - 1) / itemsPerPage;
        int totalDeadPages = (deadNPCs.Count + itemsPerPage - 1) / itemsPerPage;
        bool viewingDead = false;

        while (true)
        {
            terminal.ClearScreen();
            terminal.SetColor("bright_cyan");
            terminal.WriteLine("╔══════════════════════════════════════════════════════════════════════════════╗");
            terminal.SetColor("bright_yellow");
            terminal.WriteLine("║                         -= CITIZENS OF THE REALM =-                          ║");
            terminal.SetColor("bright_cyan");
            terminal.WriteLine("╠══════════════════════════════════════════════════════════════════════════════╣");
            terminal.WriteLine("");

            // Always show player first
            terminal.SetColor("bright_green");
            terminal.WriteLine("  ═══ PLAYERS ═══");
            terminal.SetColor("yellow");
            string playerSex = currentPlayer.Sex == CharacterSex.Male ? "M" : "F";
            terminal.WriteLine($"  * {currentPlayer.DisplayName,-18} {playerSex} Lv{currentPlayer.Level,3} {currentPlayer.Class,-10} HP:{currentPlayer.HP}/{currentPlayer.MaxHP} (You)");
            terminal.WriteLine("");

            if (!viewingDead)
            {
                // Show alive NPCs
                terminal.SetColor("bright_green");
                int totalPages = Math.Max(1, totalAlivePages);
                terminal.WriteLine($"  ═══ ADVENTURERS ({aliveNPCs.Count} active) - Page {currentPage + 1}/{totalPages} ═══");

                if (aliveNPCs.Count > 0)
                {
                    int startIdx = currentPage * itemsPerPage;
                    int endIdx = Math.Min(startIdx + itemsPerPage, aliveNPCs.Count);

                    for (int i = startIdx; i < endIdx; i++)
                    {
                        var npc = aliveNPCs[i];
                        // Color based on level relative to player
                        string color = npc.Level > currentPlayer.Level + 5 ? "bright_red" :
                                       npc.Level > currentPlayer.Level ? "yellow" :
                                       npc.Level > currentPlayer.Level - 5 ? "white" : "gray";

                        terminal.SetColor(color);
                        string classStr = npc.Class.ToString();
                        string locationStr = string.IsNullOrEmpty(npc.CurrentLocation) ? "???" : npc.CurrentLocation;
                        string sex = npc.Sex == CharacterSex.Male ? "M" : "F";
                        terminal.WriteLine($"  - {npc.Name,-18} {sex} Lv{npc.Level,3} {classStr,-10} @ {locationStr}");
                    }
                }
                else
                {
                    terminal.SetColor("gray");
                    terminal.WriteLine("  No adventurers found in the realm.");
                }
            }
            else
            {
                // Show dead NPCs
                terminal.SetColor("dark_gray");
                int totalPages = Math.Max(1, totalDeadPages);
                terminal.WriteLine($"  ═══ FALLEN ({deadNPCs.Count}) - Page {currentPage + 1}/{totalPages} ═══");

                if (deadNPCs.Count > 0)
                {
                    int startIdx = currentPage * itemsPerPage;
                    int endIdx = Math.Min(startIdx + itemsPerPage, deadNPCs.Count);

                    for (int i = startIdx; i < endIdx; i++)
                    {
                        var npc = deadNPCs[i];
                        terminal.SetColor("dark_gray");
                        string sex = npc.Sex == CharacterSex.Male ? "M" : "F";
                        terminal.WriteLine($"  † {npc.Name,-18} {sex} Lv{npc.Level,3} {npc.Class,-10} - R.I.P.");
                    }
                }
                else
                {
                    terminal.SetColor("gray");
                    terminal.WriteLine("  No fallen adventurers.");
                }
            }

            terminal.WriteLine("");
            terminal.SetColor("bright_cyan");
            terminal.WriteLine("╚══════════════════════════════════════════════════════════════════════════════╝");
            terminal.WriteLine("");

            // Navigation options
            terminal.SetColor("cyan");
            var options = new List<string>();
            int maxPages = viewingDead ? totalDeadPages : totalAlivePages;
            if (currentPage > 0) options.Add("[P]rev");
            if (currentPage < maxPages - 1) options.Add("[N]ext");
            if (!viewingDead && deadNPCs.Count > 0) options.Add("[D]ead");
            if (viewingDead) options.Add("[A]live");
            options.Add("[R]eturn");

            terminal.WriteLine($"  {string.Join("  ", options)}");
            terminal.WriteLine("");

            string input = (await terminal.GetKeyInput()).ToUpperInvariant();

            if (input == "P" && currentPage > 0)
            {
                currentPage--;
            }
            else if (input == "N" && currentPage < maxPages - 1)
            {
                currentPage++;
            }
            else if (input == "D" && !viewingDead && deadNPCs.Count > 0)
            {
                viewingDead = true;
                currentPage = 0;
            }
            else if (input == "A" && viewingDead)
            {
                viewingDead = false;
                currentPage = 0;
            }
            else if (input == "R" || input == "Q" || input == "ESCAPE")
            {
                break;
            }
        }
    }
    
    private async Task ShowRelations()
    {
        terminal.ClearScreen();
        terminal.SetColor("bright_magenta");
        terminal.WriteLine("Relations");
        terminal.WriteLine("=========");
        terminal.WriteLine("");
        terminal.SetColor("white");
        terminal.WriteLine($"Married: {(currentPlayer.Married ? "Yes" : "No")}");
        terminal.WriteLine($"Children: {currentPlayer.Kids}");
        terminal.WriteLine($"Team: {(string.IsNullOrEmpty(currentPlayer.Team) ? "None" : currentPlayer.Team)}");
        terminal.WriteLine("");

        if (currentPlayer.Married)
        {
            terminal.WriteLine("Family options:");
            terminal.WriteLine("1. Visit home");
            terminal.WriteLine("2. Check on children");
        }
        else
        {
            terminal.WriteLine("You are single. Visit Love Street to find romance!");
        }

        terminal.WriteLine("");
        await terminal.PressAnyKey();
    }

    /// <summary>
    /// Display comprehensive player statistics
    /// </summary>
    private async Task ShowStatistics()
    {
        var stats = currentPlayer.Statistics;
        stats.UpdateSessionTime(); // Ensure current session is counted

        terminal.ClearScreen();
        terminal.SetColor("bright_cyan");
        terminal.WriteLine("╔══════════════════════════════════════════════════════════════════════════════╗");
        { const string t = "PLAYER STATISTICS"; int l = (78 - t.Length) / 2, r = 78 - t.Length - l; terminal.WriteLine($"║{new string(' ', l)}{t}{new string(' ', r)}║"); }
        terminal.WriteLine("╚══════════════════════════════════════════════════════════════════════════════╝");
        terminal.WriteLine("");

        // Combat Stats
        terminal.SetColor("bright_red");
        terminal.WriteLine("═══ COMBAT ═══");
        terminal.SetColor("white");
        terminal.WriteLine($"  Monsters Slain:     {stats.TotalMonstersKilled,10:N0}     Bosses Killed:    {stats.TotalBossesKilled,8:N0}");
        terminal.WriteLine($"  Unique Monsters:    {stats.TotalUniquesKilled,10:N0}     Combat Win Rate:  {stats.GetCombatWinRate(),7:F1}%");
        terminal.WriteLine($"  Combats Won:        {stats.TotalCombatsWon,10:N0}     Combats Lost:     {stats.TotalCombatsLost,8:N0}");
        terminal.WriteLine($"  Times Fled:         {stats.TotalCombatsFled,10:N0}     Player Kills (PvP):{stats.TotalPlayerKills,7:N0}");
        terminal.WriteLine("");
        terminal.SetColor("bright_yellow");
        terminal.WriteLine($"  Total Damage Dealt: {stats.TotalDamageDealt,10:N0}     Damage Taken:     {stats.TotalDamageTaken,8:N0}");
        terminal.WriteLine($"  Highest Single Hit: {stats.HighestSingleHit,10:N0}     Critical Hits:    {stats.TotalCriticalHits,8:N0}");
        terminal.WriteLine("");

        // Economic Stats
        terminal.SetColor("bright_green");
        terminal.WriteLine("═══ ECONOMY ═══");
        terminal.SetColor("white");
        terminal.WriteLine($"  Total Gold Earned:  {stats.TotalGoldEarned,10:N0}     Gold from Monsters:{stats.TotalGoldFromMonsters,7:N0}");
        terminal.WriteLine($"  Gold Spent:         {stats.TotalGoldSpent,10:N0}     Peak Gold Held:   {stats.HighestGoldHeld,8:N0}");
        terminal.WriteLine($"  Items Bought:       {stats.TotalItemsBought,10:N0}     Items Sold:       {stats.TotalItemsSold,8:N0}");
        terminal.WriteLine("");

        // Experience Stats
        terminal.SetColor("bright_magenta");
        terminal.WriteLine("═══ EXPERIENCE ═══");
        terminal.SetColor("white");
        terminal.WriteLine($"  Total XP Earned:    {stats.TotalExperienceEarned,10:N0}     Level Ups:        {stats.TotalLevelUps,8:N0}");
        terminal.WriteLine($"  Highest Level:      {stats.HighestLevelReached,10}     Current Level:    {currentPlayer.Level,8}");
        terminal.WriteLine("");

        // Exploration Stats
        terminal.SetColor("bright_blue");
        terminal.WriteLine("═══ EXPLORATION ═══");
        terminal.SetColor("white");
        terminal.WriteLine($"  Deepest Dungeon:    {stats.DeepestDungeonLevel,10}     Floors Explored:  {stats.TotalDungeonFloorsCovered,8:N0}");
        terminal.WriteLine($"  Chests Opened:      {stats.TotalChestsOpened,10:N0}     Secrets Found:    {stats.TotalSecretsFound,8:N0}");
        terminal.WriteLine($"  Traps Triggered:    {stats.TotalTrapsTriggered,10:N0}     Traps Disarmed:   {stats.TotalTrapsDisarmed,8:N0}");
        terminal.WriteLine("");

        // Survival Stats
        terminal.SetColor("yellow");
        terminal.WriteLine("═══ SURVIVAL ═══");
        terminal.SetColor("white");
        terminal.WriteLine($"  Deaths (Monster):   {stats.TotalMonsterDeaths,10:N0}     Deaths (PvP):     {stats.TotalPlayerDeaths,8:N0}");
        terminal.WriteLine($"  Potions Used:       {stats.TotalHealingPotionsUsed,10:N0}     Health Restored:  {stats.TotalHealthRestored,8:N0}");
        terminal.WriteLine($"  Resurrections:      {stats.TotalTimesResurrected,10:N0}     Diseases Cured:   {stats.TotalDiseasesCured,8:N0}");
        terminal.WriteLine("");

        // Time Stats
        terminal.SetColor("gray");
        terminal.WriteLine("═══ TIME ═══");
        terminal.SetColor("white");
        terminal.WriteLine($"  Total Play Time:    {stats.GetFormattedPlayTime(),10}     Sessions Played:  {stats.TotalSessionsPlayed,8:N0}");
        terminal.WriteLine($"  Character Created:  {stats.CharacterCreated:yyyy-MM-dd}     Current Streak:   {stats.CurrentStreak,8} days");
        terminal.WriteLine($"  Longest Streak:     {stats.LongestStreak,10} days");
        terminal.WriteLine("");

        // Difficulty indicator
        terminal.SetColor(DifficultySystem.GetColor(currentPlayer.Difficulty));
        terminal.WriteLine($"  Difficulty: {DifficultySystem.GetDisplayName(currentPlayer.Difficulty)}");
        terminal.WriteLine("");

        terminal.SetColor("gray");
        terminal.WriteLine("Press Enter to continue...");
        await terminal.PressAnyKey();
    }

    /// <summary>
    /// Display player achievements
    /// </summary>
    private async Task ShowAchievements()
    {
        // Initialize if needed
        AchievementSystem.Initialize();

        terminal.ClearScreen();
        terminal.SetColor("bright_yellow");
        terminal.WriteLine("╔══════════════════════════════════════════════════════════════════════════════╗");
        { const string t = "* ACHIEVEMENTS *"; int l = (78 - t.Length) / 2, r = 78 - t.Length - l; terminal.WriteLine($"║{new string(' ', l)}{t}{new string(' ', r)}║"); }
        terminal.WriteLine("╠══════════════════════════════════════════════════════════════════════════════╣");

        var achievements = currentPlayer.Achievements;
        int totalAchievements = AchievementSystem.TotalAchievements;
        int unlocked = achievements.UnlockedCount;

        // Summary line
        terminal.SetColor("white");
        terminal.WriteLine($"║  Unlocked: {unlocked}/{totalAchievements} ({achievements.CompletionPercentage:F1}%)                                            ║");
        terminal.SetColor("bright_cyan");
        terminal.WriteLine($"║  Achievement Points: {achievements.TotalPoints}                                                     ║");
        terminal.SetColor("bright_yellow");
        terminal.WriteLine("╠══════════════════════════════════════════════════════════════════════════════╣");

        // Category selection
        terminal.SetColor("white");
        terminal.WriteLine("║  [1] Combat     [2] Progression  [3] Economy    [4] Exploration              ║");
        terminal.WriteLine("║  [5] Social     [6] Challenge    [7] Secret     [A] All                      ║");
        terminal.SetColor("bright_yellow");
        terminal.WriteLine("╚══════════════════════════════════════════════════════════════════════════════╝");
        terminal.WriteLine("");

        terminal.SetColor("cyan");
        var input = await terminal.GetInput("Select category (or press Enter for All): ");
        input = input.Trim().ToUpper();

        AchievementCategory? selectedCategory = input switch
        {
            "1" => AchievementCategory.Combat,
            "2" => AchievementCategory.Progression,
            "3" => AchievementCategory.Economy,
            "4" => AchievementCategory.Exploration,
            "5" => AchievementCategory.Social,
            "6" => AchievementCategory.Challenge,
            "7" => AchievementCategory.Secret,
            _ => null
        };

        // Display achievements
        terminal.ClearScreen();
        terminal.SetColor("bright_yellow");
        var categoryName = selectedCategory?.ToString() ?? "All";
        terminal.WriteLine($"╔═══════════════════════ {categoryName.ToUpper()} ACHIEVEMENTS ═══════════════════════╗");
        terminal.WriteLine("");

        var achievementsToShow = selectedCategory.HasValue
            ? AchievementSystem.GetByCategory(selectedCategory.Value)
            : AchievementSystem.GetAllAchievements();

        int displayCount = 0;
        foreach (var achievement in achievementsToShow.OrderBy(a => a.Tier).ThenBy(a => a.Name))
        {
            bool isUnlocked = achievements.IsUnlocked(achievement.Id);

            // Show tier symbol and name
            terminal.SetColor(achievement.GetTierColor());
            terminal.Write($" {achievement.GetTierSymbol()} ");

            if (isUnlocked)
            {
                terminal.SetColor("bright_green");
                terminal.Write("+ ");
                terminal.SetColor("white");
                terminal.Write(achievement.Name);
                terminal.SetColor("gray");
                terminal.WriteLine($" - {achievement.Description}");

                // Show unlock date
                var unlockDate = achievements.GetUnlockDate(achievement.Id);
                if (unlockDate.HasValue)
                {
                    terminal.SetColor("darkgray");
                    terminal.WriteLine($"     Unlocked: {unlockDate.Value:yyyy-MM-dd}   +{achievement.PointValue} pts");
                }
            }
            else
            {
                terminal.SetColor("darkgray");
                terminal.Write("[ ] ");

                if (achievement.IsSecret)
                {
                    terminal.SetColor("gray");
                    terminal.Write("???");
                    terminal.SetColor("darkgray");
                    terminal.WriteLine($" - {achievement.SecretHint}");
                }
                else
                {
                    terminal.SetColor("gray");
                    terminal.Write(achievement.Name);
                    terminal.SetColor("darkgray");
                    terminal.WriteLine($" - {achievement.Description}");
                }
            }

            displayCount++;

            // Pagination
            if (displayCount > 0 && displayCount % 15 == 0)
            {
                terminal.WriteLine("");
                terminal.SetColor("cyan");
                terminal.WriteLine("Press Enter for more, or Q to quit...");
                var key = await terminal.GetKeyInput();
                if (key?.ToUpper() == "Q") return;
                terminal.ClearScreen();
                terminal.SetColor("bright_yellow");
                terminal.WriteLine($"╔═══════════════════════ {categoryName.ToUpper()} ACHIEVEMENTS ═══════════════════════╗");
                terminal.WriteLine("");
            }
        }

        terminal.WriteLine("");
        terminal.SetColor("gray");
        terminal.WriteLine("Press Enter to continue...");
        await terminal.PressAnyKey();
    }

    /// <summary>
    /// Attack another character in the current location
    /// </summary>
    private async Task AttackSomeone()
    {
        terminal.ClearScreen();
        terminal.SetColor("bright_red");
        terminal.WriteLine("╔══════════════════════════════════════════════════════════════════════════════╗");
        terminal.WriteLine("║                          ATTACK SOMEONE                                      ║");
        terminal.WriteLine("╚══════════════════════════════════════════════════════════════════════════════╝");
        terminal.WriteLine("");

        // Get NPCs in the area
        var allNPCs = NPCSpawnSystem.Instance.ActiveNPCs ?? new List<NPC>();
        var npcsInArea = allNPCs
            .Where(n => n.IsAlive &&
                       (n.CurrentLocation?.Equals("Main Street", StringComparison.OrdinalIgnoreCase) == true ||
                        n.CurrentLocation?.Equals("MainStreet", StringComparison.OrdinalIgnoreCase) == true))
            .Take(10)
            .ToList();

        // Add some random targets if no NPCs found
        if (npcsInArea.Count == 0)
        {
            terminal.SetColor("gray");
            terminal.WriteLine("  There's no one around to attack right now.");
            terminal.WriteLine("");
            await terminal.PressAnyKey();
            return;
        }

        terminal.SetColor("yellow");
        terminal.WriteLine("  Who do you want to attack?");
        terminal.WriteLine("");

        terminal.SetColor("white");
        for (int i = 0; i < npcsInArea.Count; i++)
        {
            var npc = npcsInArea[i];
            terminal.SetColor("cyan");
            terminal.Write($"  [{i + 1}] ");
            terminal.SetColor("white");
            terminal.Write($"{npc.Name}");
            terminal.SetColor("gray");
            terminal.WriteLine($" - Level {npc.Level} {npc.Class}");
        }

        terminal.WriteLine("");
        terminal.SetColor("gray");
        terminal.WriteLine("  [0] Cancel");
        terminal.WriteLine("");

        string choice = await terminal.GetInput("Attack who? ");

        if (int.TryParse(choice, out int targetIndex) && targetIndex >= 1 && targetIndex <= npcsInArea.Count)
        {
            var target = npcsInArea[targetIndex - 1];

            terminal.SetColor("red");
            terminal.WriteLine($"\n  You approach {target.Name} with hostile intent!");
            await Task.Delay(1000);

            // Warn about consequences
            terminal.SetColor("yellow");
            terminal.WriteLine($"\n  Warning: Attacking citizens increases your Darkness!");
            terminal.WriteLine($"  Are you sure? (Y/N)");

            string confirm = (await terminal.GetKeyInput()).ToUpperInvariant();

            if (confirm == "Y")
            {
                // Attack!
                var encounterResult = await StreetEncounterSystem.Instance.AttackCharacter(
                    currentPlayer, target, terminal);

                // Increase darkness for unprovoked attack
                currentPlayer.Darkness += 15;

                if (encounterResult.Victory)
                {
                    terminal.SetColor("green");
                    terminal.WriteLine($"\n  You defeated {target.Name}!");
                    currentPlayer.PKills++;
                }
                else
                {
                    terminal.SetColor("red");
                    terminal.WriteLine($"\n  {target.Name} got the better of you...");
                    currentPlayer.PDefeats++;
                }
            }
            else
            {
                terminal.SetColor("gray");
                terminal.WriteLine("\n  You decide against it.");
            }
        }
        else
        {
            terminal.SetColor("gray");
            terminal.WriteLine("\n  You change your mind.");
        }

        await Task.Delay(1500);
    }

    private async Task<bool> QuitGame()
    {
        // Online mode: auto-dormitory and quick exit (no prompts).
        // Players who want protected sleep should visit the Inn first.
        if (UsurperRemake.BBS.DoorMode.IsOnlineMode)
        {
            // Deduct dormitory cost
            long dormCost = GameConfig.DormitorySleepCost;
            bool isBroke = false;
            if (currentPlayer.Gold >= dormCost)
            {
                currentPlayer.Gold -= dormCost;
            }
            else if (currentPlayer.Gold + currentPlayer.BankGold >= dormCost)
            {
                long shortfall = dormCost - currentPlayer.Gold;
                currentPlayer.Gold = 0;
                currentPlayer.BankGold -= shortfall;
            }
            else
            {
                isBroke = true;
            }

            // Restore and save
            currentPlayer.HP = currentPlayer.MaxHP;
            currentPlayer.Mana = currentPlayer.MaxMana;
            currentPlayer.Stamina = Math.Max(currentPlayer.Stamina, currentPlayer.Constitution * 2);
            await GameEngine.Instance.SaveCurrentGame();

            // Register as sleeping in dormitory (vulnerable)
            var backend = SaveSystem.Instance.Backend as UsurperRemake.Systems.SqlSaveBackend;
            if (backend != null)
            {
                var username = UsurperRemake.BBS.DoorMode.OnlineUsername ?? currentPlayer.Name2;
                await backend.RegisterSleepingPlayer(username, isBroke ? "street" : "dormitory", "[]", 0);
            }

            terminal.SetColor("gray");
            terminal.WriteLine(isBroke
                ? "\n  You curl up in the street and drift off..."
                : "\n  You head to the dormitory and drift off...");
            throw new LocationExitException(GameLocation.NoWhere);
        }

        terminal.ClearScreen();

        // Display session summary
        if (currentPlayer?.Statistics != null)
        {
            var summary = currentPlayer.Statistics.GetSessionSummary();

            terminal.SetColor("bright_cyan");
            terminal.WriteLine("╔══════════════════════════════════════════════════════════════════════════════╗");
            terminal.WriteLine("║                           SESSION SUMMARY                                    ║");
            terminal.WriteLine("╚══════════════════════════════════════════════════════════════════════════════╝");
            terminal.WriteLine("");

            // Session duration
            terminal.SetColor("white");
            terminal.Write("  Session Duration: ");
            terminal.SetColor("bright_yellow");
            if (summary.Duration.TotalHours >= 1)
                terminal.WriteLine($"{(int)summary.Duration.TotalHours}h {summary.Duration.Minutes}m");
            else
                terminal.WriteLine($"{summary.Duration.Minutes}m {summary.Duration.Seconds}s");

            terminal.WriteLine("");
            terminal.SetColor("gray");
            terminal.WriteLine("  ─────────────────────────────────────────────────────────");
            terminal.WriteLine("");

            // Combat stats
            terminal.SetColor("bright_red");
            terminal.Write("  Monsters Slain: ");
            terminal.SetColor("white");
            terminal.WriteLine($"{summary.MonstersKilled:N0}");

            terminal.SetColor("bright_red");
            terminal.Write("  Damage Dealt:   ");
            terminal.SetColor("white");
            terminal.WriteLine($"{summary.DamageDealt:N0}");

            // Progress stats
            if (summary.LevelsGained > 0)
            {
                terminal.SetColor("bright_green");
                terminal.Write("  Levels Gained:  ");
                terminal.SetColor("white");
                terminal.WriteLine($"+{summary.LevelsGained}");
            }

            terminal.SetColor("bright_magenta");
            terminal.Write("  XP Earned:      ");
            terminal.SetColor("white");
            terminal.WriteLine($"{summary.ExperienceGained:N0}");

            // Economy stats
            terminal.SetColor("bright_yellow");
            terminal.Write("  Gold Earned:    ");
            terminal.SetColor("white");
            terminal.WriteLine($"{summary.GoldEarned:N0}");

            if (summary.ItemsBought > 0 || summary.ItemsSold > 0)
            {
                terminal.SetColor("cyan");
                terminal.Write("  Items Traded:   ");
                terminal.SetColor("white");
                terminal.WriteLine($"{summary.ItemsBought} bought, {summary.ItemsSold} sold");
            }

            // Exploration
            if (summary.RoomsExplored > 0)
            {
                terminal.SetColor("bright_blue");
                terminal.Write("  Rooms Explored: ");
                terminal.SetColor("white");
                terminal.WriteLine($"{summary.RoomsExplored:N0}");
            }

            terminal.WriteLine("");
            terminal.SetColor("gray");
            terminal.WriteLine("  ─────────────────────────────────────────────────────────");
            terminal.WriteLine("");
        }

        terminal.SetColor("yellow");
        terminal.WriteLine("  Saving your progress...");

        // Track session end telemetry
        if (currentPlayer != null)
        {
            int playtimeMinutes = (int)currentPlayer.Statistics.TotalPlayTime.TotalMinutes;
            UsurperRemake.Systems.TelemetrySystem.Instance.TrackSessionEnd(
                currentPlayer.Level,
                playtimeMinutes,
                (int)currentPlayer.MDefeats,
                (int)currentPlayer.MKills
            );
        }

        // Actually save the game before quitting!
        await GameEngine.Instance.SaveCurrentGame();

        // Final Steam stats sync at session end
        SteamIntegration.SyncCurrentPlayerStats();

        terminal.WriteLine("");
        terminal.SetColor("bright_green");
        terminal.WriteLine("  Thanks for playing Usurper Reborn!");
        terminal.SetColor("gray");
        terminal.WriteLine("");
        terminal.WriteLine("  Press any key to exit...");
        await terminal.PressAnyKey();

        // Signal game should quit
        throw new LocationExitException(GameLocation.NoWhere);
    }
    
    // Helper methods
    private string GetTownName()
    {
        return "Usurper"; // Could be configurable
    }
    
    private string GetTimeOfDay()
    {
        var hour = DateTime.Now.Hour;
        return hour switch
        {
            >= 6 and < 12 => "morning",
            >= 12 and < 18 => "afternoon", 
            >= 18 and < 22 => "evening",
            _ => "night"
        };
    }
    
    private string GetWeather()
    {
        var weather = new[] { "clear", "cloudy", "misty", "cool", "warm", "breezy" };
        return weather[Random.Shared.Next(0, weather.Length)];
    }
    
    private int GetPlayerRank()
    {
        // Calculate real rank based on all characters
        var npcs = NPCSpawnSystem.Instance.ActiveNPCs;
        int rank = 1;

        if (npcs != null)
        {
            foreach (var npc in npcs.Where(n => n.IsAlive))
            {
                // NPC ranks higher if higher level, or same level with more XP
                if (npc.Level > currentPlayer.Level ||
                    (npc.Level == currentPlayer.Level && npc.Experience > currentPlayer.Experience))
                {
                    rank++;
                }
            }
        }

        return rank;
    }
    
    private async Task ProcessGoodDeed(string choice)
    {
        if (int.TryParse(choice, out int deed) && deed >= 1 && deed <= 3)
        {
            currentPlayer.ChivNr--;
            currentPlayer.Chivalry += 10;
            
            var deedName = deed switch
            {
                1 => "giving gold to the poor",
                2 => "helping at the temple",
                3 => "volunteering at the orphanage",
                _ => "performing a good deed"
            };
            
            terminal.WriteLine($"You gain chivalry by {deedName}!", "green");
            await Task.Delay(1500);
        }
    }
    
    private async Task ProcessEvilDeed(string choice)
    {
        if (int.TryParse(choice, out int deed) && deed >= 1 && deed <= 3)
        {
            currentPlayer.DarkNr--;
            currentPlayer.Darkness += 10;
            
            var deedName = deed switch
            {
                1 => "robbing from the poor",
                2 => "vandalizing property", 
                3 => "spreading malicious rumors",
                _ => "performing an evil deed"
            };
            
            terminal.WriteLine($"Your dark soul grows by {deedName}!", "red");
            await Task.Delay(1500);
        }
    }
    
    /// <summary>
    /// Test combat system (DEBUG)
    /// </summary>
    private async Task TestCombat()
    {
        terminal.ClearScreen();
        terminal.SetColor("bright_red");
        terminal.WriteLine("=== COMBAT TEST ===");
        terminal.WriteLine("");
        
        // Create a test monster (Street Thug)
        var testMonster = Monster.CreateMonster(
            nr: 1,
            name: "Street Thug",
            hps: 50,
            strength: 15,
            defence: 8,
            phrase: "Give me your gold!",
            grabweap: true,
            grabarm: false,
            weapon: "Rusty Knife",
            armor: "Torn Clothes",
            poisoned: false,
            disease: false,
            punch: 12,
            armpow: 2,
            weappow: 8
        );
        
        terminal.WriteLine("A street thug jumps out and blocks your path!");
        terminal.WriteLine($"The {testMonster.Name} brandishes a {testMonster.Weapon}!");
        terminal.WriteLine("");
        
        var confirm = await terminal.GetInput("Fight the thug? (Y/N): ");
        
        if (confirm.ToUpper() == "Y")
        {
            // Initialize combat engine
            var combatEngine = new CombatEngine(terminal);
            
            // Execute combat
            var result = await combatEngine.PlayerVsMonster(currentPlayer, testMonster);

            // Check if player should return to temple after resurrection
            if (result.ShouldReturnToTemple)
            {
                terminal.SetColor("yellow");
                terminal.WriteLine("You awaken at the Temple of Light...");
                await Task.Delay(2000);
                await NavigateToLocation(GameLocation.Temple);
                return;
            }

            // Display result summary
            terminal.ClearScreen();
            terminal.SetColor("bright_cyan");
            terminal.WriteLine("=== COMBAT SUMMARY ===");
            terminal.WriteLine("");
            
            foreach (var logEntry in result.CombatLog)
            {
                terminal.WriteLine($"- {logEntry}");
            }
            
            terminal.WriteLine("");
            terminal.SetColor("white");
            terminal.WriteLine($"Final Outcome: {result.Outcome}");
            
            if (result.Outcome == CombatOutcome.Victory)
            {
                terminal.WriteLine("The thug flees into the shadows!", "green");
            }
            else if (result.Outcome == CombatOutcome.PlayerEscaped)
            {
                terminal.WriteLine("You slip away from the dangerous encounter.", "yellow");
            }
        }
        else
        {
            terminal.WriteLine("You wisely avoid the confrontation.", "green");
        }
        
        await terminal.PressAnyKey();
    }
    
    /// <summary>
    /// Show settings and save management menu
    /// </summary>
    private async Task ShowSettingsMenu()
    {
        bool exitSettings = false;
        
        while (!exitSettings)
        {
            terminal.ClearScreen();
            terminal.SetColor("bright_cyan");
            terminal.WriteLine("╔══════════════════════════════════════════════════════════════════════════════╗");
            { const string t = "SETTINGS & SAVE OPTIONS"; int l = (78 - t.Length) / 2, r = 78 - t.Length - l; terminal.WriteLine($"║{new string(' ', l)}{t}{new string(' ', r)}║"); }
            terminal.WriteLine("╚══════════════════════════════════════════════════════════════════════════════╝");
            terminal.WriteLine("");
            
            var dailyManager = DailySystemManager.Instance;
            var currentMode = dailyManager.CurrentMode;
            
            terminal.SetColor("white");
            terminal.WriteLine("Current Settings:");
            terminal.WriteLine($"  Daily Cycle Mode: {GetDailyCycleModeDescription(currentMode)}", "yellow");
            terminal.WriteLine($"  Auto-save: {(dailyManager.AutoSaveEnabled ? "Enabled" : "Disabled")}", "yellow");
            terminal.WriteLine($"  Current Day: {dailyManager.CurrentDay}", "yellow");
            terminal.WriteLine("");
            
            terminal.WriteLine("Options:");
            terminal.WriteLine("1. Change Daily Cycle Mode");
            terminal.WriteLine("2. Configure Auto-save Settings");
            terminal.WriteLine("3. Save Game Now");
            terminal.WriteLine("4. Load Different Save");
            terminal.WriteLine("5. Delete Save Files");
            terminal.WriteLine("6. View Save File Information");
            terminal.WriteLine("7. Force Daily Reset");
            terminal.WriteLine("8. Game Preferences (Combat Speed, Content Settings)");
            terminal.WriteLine("9. Back to Main Street");
            terminal.WriteLine("");

            var choice = await terminal.GetInput("Enter your choice (1-9): ");
            
            switch (choice)
            {
                case "1":
                    await ChangeDailyCycleMode();
                    break;
                    
                case "2":
                    await ConfigureAutoSave();
                    break;
                    
                case "3":
                    await SaveGameNow();
                    break;
                    
                case "4":
                    await LoadDifferentSave();
                    break;
                    
                case "5":
                    await DeleteSaveFiles();
                    break;
                    
                case "6":
                    await ViewSaveFileInfo();
                    break;
                    
                case "7":
                    await ForceDailyReset();
                    break;

                case "8":
                    await ShowGamePreferences();
                    break;

                case "9":
                    exitSettings = true;
                    break;

                default:
                    terminal.WriteLine("Invalid choice!", "red");
                    await Task.Delay(1000);
                    break;
            }
        }
    }

    /// <summary>
    /// Show game preferences menu (combat speed, content settings)
    /// </summary>
    private async Task ShowGamePreferences()
    {
        bool exitPrefs = false;

        while (!exitPrefs)
        {
            terminal.ClearScreen();
            terminal.SetColor("bright_cyan");
            terminal.WriteLine("╔══════════════════════════════════════════════════════════════════════════════╗");
            terminal.WriteLine("║                             GAME PREFERENCES                                 ║");
            terminal.WriteLine("╚══════════════════════════════════════════════════════════════════════════════╝");
            terminal.WriteLine("");

            terminal.SetColor("white");
            terminal.WriteLine("Current Settings:");
            terminal.WriteLine("");

            // Combat Speed
            string speedDesc = currentPlayer.CombatSpeed switch
            {
                CombatSpeed.Instant => "Instant (no delays)",
                CombatSpeed.Fast => "Fast (50% delays)",
                _ => "Normal (full delays)"
            };
            terminal.WriteLine($"  Combat Speed: {speedDesc}", "yellow");

            // Auto-heal
            terminal.WriteLine($"  Auto-heal in Battle: {(currentPlayer.AutoHeal ? "Enabled" : "Disabled")}", "yellow");

            // Skip intimate scenes
            terminal.WriteLine($"  Skip Intimate Scenes: {(currentPlayer.SkipIntimateScenes ? "Enabled (Fade to Black)" : "Disabled (Full Scenes)")}", "yellow");
            terminal.WriteLine("");

            terminal.WriteLine("Options:");
            terminal.WriteLine("1. Change Combat Speed");
            terminal.WriteLine("2. Toggle Auto-heal in Battle");
            terminal.WriteLine("3. Toggle Skip Intimate Scenes");
            terminal.WriteLine("4. Back to Settings");
            terminal.WriteLine("");

            var choice = await terminal.GetInput("Enter your choice (1-4): ");

            switch (choice)
            {
                case "1":
                    await ChangeCombatSpeed();
                    break;

                case "2":
                    currentPlayer.AutoHeal = !currentPlayer.AutoHeal;
                    terminal.WriteLine($"Auto-heal is now {(currentPlayer.AutoHeal ? "ENABLED" : "DISABLED")}", "green");
                    await GameEngine.Instance.SaveCurrentGame();
                    await Task.Delay(1000);
                    break;

                case "3":
                    currentPlayer.SkipIntimateScenes = !currentPlayer.SkipIntimateScenes;
                    if (currentPlayer.SkipIntimateScenes)
                    {
                        terminal.WriteLine("Intimate scenes will now 'fade to black' - showing a brief summary", "green");
                        terminal.WriteLine("instead of detailed romantic content.", "gray");
                    }
                    else
                    {
                        terminal.WriteLine("Intimate scenes will now show full romantic content.", "green");
                    }
                    await GameEngine.Instance.SaveCurrentGame();
                    await Task.Delay(1500);
                    break;

                case "4":
                    exitPrefs = true;
                    break;

                default:
                    terminal.WriteLine("Invalid choice!", "red");
                    await Task.Delay(1000);
                    break;
            }
        }
    }

    /// <summary>
    /// Change combat speed setting
    /// </summary>
    private async Task ChangeCombatSpeed()
    {
        terminal.ClearScreen();
        terminal.SetColor("bright_cyan");
        terminal.WriteLine("═══════════════════════════════════════");
        terminal.WriteLine("           COMBAT SPEED");
        terminal.WriteLine("═══════════════════════════════════════");
        terminal.WriteLine("");

        terminal.SetColor("white");
        terminal.WriteLine("Choose how fast combat text appears:");
        terminal.WriteLine("");

        terminal.WriteLine("1. Normal (Recommended)", "yellow");
        terminal.WriteLine("   - Full delays between combat actions");
        terminal.WriteLine("   - Best for reading and immersion");
        terminal.WriteLine("");

        terminal.WriteLine("2. Fast", "yellow");
        terminal.WriteLine("   - 50% of normal delays");
        terminal.WriteLine("   - Quicker combat, still readable");
        terminal.WriteLine("");

        terminal.WriteLine("3. Instant", "yellow");
        terminal.WriteLine("   - No delays at all");
        terminal.WriteLine("   - Maximum speed, combat flies by");
        terminal.WriteLine("");

        var choice = await terminal.GetInput("Select speed (1-3) or 0 to cancel: ");

        CombatSpeed? newSpeed = choice switch
        {
            "1" => CombatSpeed.Normal,
            "2" => CombatSpeed.Fast,
            "3" => CombatSpeed.Instant,
            _ => null
        };

        if (newSpeed.HasValue)
        {
            currentPlayer.CombatSpeed = newSpeed.Value;
            string desc = newSpeed.Value switch
            {
                CombatSpeed.Instant => "Instant",
                CombatSpeed.Fast => "Fast",
                _ => "Normal"
            };
            terminal.WriteLine($"Combat speed changed to: {desc}", "green");
            await GameEngine.Instance.SaveCurrentGame();
        }

        await Task.Delay(1000);
    }
    
    /// <summary>
    /// Change daily cycle mode
    /// </summary>
    private async Task ChangeDailyCycleMode()
    {
        terminal.ClearScreen();
        terminal.SetColor("bright_cyan");
        terminal.WriteLine("═══════════════════════════════════════");
        terminal.WriteLine("         DAILY CYCLE MODES");
        terminal.WriteLine("═══════════════════════════════════════");
        terminal.WriteLine("");
        
        terminal.SetColor("white");
        terminal.WriteLine("Available modes:");
        terminal.WriteLine("");
        
        terminal.WriteLine("1. Session-Based (Default)", "yellow");
        terminal.WriteLine("   - New day starts when you run out of turns or choose to rest");
        terminal.WriteLine("   - Perfect for casual play sessions");
        terminal.WriteLine("");
        
        terminal.WriteLine("2. Real-Time (24 hours)", "yellow");
        terminal.WriteLine("   - Classic BBS-style daily reset at midnight");
        terminal.WriteLine("   - NPCs continue to act while you're away");
        terminal.WriteLine("");
        
        terminal.WriteLine("3. Accelerated (4 hours)", "yellow");
        terminal.WriteLine("   - New day every 4 real hours");
        terminal.WriteLine("   - Faster progression for active players");
        terminal.WriteLine("");
        
        terminal.WriteLine("4. Accelerated (8 hours)", "yellow");
        terminal.WriteLine("   - New day every 8 real hours");
        terminal.WriteLine("   - Balanced progression");
        terminal.WriteLine("");
        
        terminal.WriteLine("5. Accelerated (12 hours)", "yellow");
        terminal.WriteLine("   - New day every 12 real hours");
        terminal.WriteLine("   - Slower but steady progression");
        terminal.WriteLine("");
        
        terminal.WriteLine("6. Endless", "yellow");
        terminal.WriteLine("   - No turn limits, play as long as you want");
        terminal.WriteLine("   - Perfect for exploration and experimentation");
        terminal.WriteLine("");
        
        var choice = await terminal.GetInput("Select mode (1-6) or 0 to cancel: ");
        
        var newMode = choice switch
        {
            "1" => DailyCycleMode.SessionBased,
            "2" => DailyCycleMode.RealTime24Hour,
            "3" => DailyCycleMode.Accelerated4Hour,
            "4" => DailyCycleMode.Accelerated8Hour,
            "5" => DailyCycleMode.Accelerated12Hour,
            "6" => DailyCycleMode.Endless,
            _ => (DailyCycleMode?)null
        };
        
        if (newMode.HasValue)
        {
            var dailyManager = DailySystemManager.Instance;
            dailyManager.SetDailyCycleMode(newMode.Value);
            
            terminal.WriteLine($"Daily cycle mode changed to: {GetDailyCycleModeDescription(newMode.Value)}", "green");
            
            // Save the change
            await GameEngine.Instance.SaveCurrentGame();
        }
        else if (choice != "0")
        {
            terminal.WriteLine("Invalid choice!", "red");
        }
        
        await terminal.PressAnyKey("Press Enter to continue...");
    }
    
    /// <summary>
    /// Configure auto-save settings
    /// </summary>
    private async Task ConfigureAutoSave()
    {
        terminal.ClearScreen();
        terminal.SetColor("bright_cyan");
        terminal.WriteLine("═══════════════════════════════════════");
        terminal.WriteLine("         AUTO-SAVE SETTINGS");
        terminal.WriteLine("═══════════════════════════════════════");
        terminal.WriteLine("");
        
        var dailyManager = DailySystemManager.Instance;
        
        terminal.SetColor("white");
        terminal.WriteLine($"Current auto-save: {(dailyManager.AutoSaveEnabled ? "Enabled" : "Disabled")}");
        terminal.WriteLine("");
        
        terminal.WriteLine("1. Enable auto-save");
        terminal.WriteLine("2. Disable auto-save");
        terminal.WriteLine("3. Change auto-save interval");
        terminal.WriteLine("4. Back");
        terminal.WriteLine("");
        
        var choice = await terminal.GetInput("Enter your choice (1-4): ");
        
        switch (choice)
        {
            case "1":
                dailyManager.ConfigureAutoSave(true, TimeSpan.FromMinutes(5));
                terminal.WriteLine("Auto-save enabled (every 5 minutes)", "green");
                break;
                
            case "2":
                dailyManager.ConfigureAutoSave(false, TimeSpan.FromMinutes(5));
                terminal.WriteLine("Auto-save disabled", "yellow");
                break;
                
            case "3":
                terminal.WriteLine("Enter auto-save interval in minutes (1-60): ");
                var intervalInput = await terminal.GetInput("");
                if (int.TryParse(intervalInput, out var minutes) && minutes >= 1 && minutes <= 60)
                {
                    dailyManager.ConfigureAutoSave(true, TimeSpan.FromMinutes(minutes));
                    terminal.WriteLine($"Auto-save interval set to {minutes} minutes", "green");
                }
                else
                {
                    terminal.WriteLine("Invalid interval!", "red");
                }
                break;
                
            case "4":
                return;
                
            default:
                terminal.WriteLine("Invalid choice!", "red");
                break;
        }
        
        await terminal.PressAnyKey("Press Enter to continue...");
    }
    
    /// <summary>
    /// Save game now
    /// </summary>
    private async Task SaveGameNow()
    {
        await GameEngine.Instance.SaveCurrentGame();
        await terminal.PressAnyKey("Press Enter to continue...");
    }
    
    /// <summary>
    /// Load different save file
    /// </summary>
    private async Task LoadDifferentSave()
    {
        terminal.ClearScreen();
        terminal.SetColor("bright_cyan");
        terminal.WriteLine("═══════════════════════════════════════");
        terminal.WriteLine("         LOAD DIFFERENT SAVE");
        terminal.WriteLine("═══════════════════════════════════════");
        terminal.WriteLine("");
        
        var saves = SaveSystem.Instance.GetAllSaves();
        
        if (saves.Count == 0)
        {
            terminal.WriteLine("No save files found!", "red");
            await terminal.PressAnyKey("Press Enter to continue...");
            return;
        }
        
        terminal.SetColor("white");
        terminal.WriteLine("Available save files:");
        terminal.WriteLine("");
        
        for (int i = 0; i < saves.Count; i++)
        {
            var save = saves[i];
            terminal.WriteLine($"{i + 1}. {save.PlayerName} (Level {save.Level}, Day {save.CurrentDay}, {save.TurnsRemaining} turns)");
            terminal.WriteLine($"   Saved: {save.SaveTime:yyyy-MM-dd HH:mm:ss}");
            terminal.WriteLine("");
        }
        
        var choice = await terminal.GetInput($"Select save file (1-{saves.Count}) or 0 to cancel: ");
        
        if (int.TryParse(choice, out var index) && index >= 1 && index <= saves.Count)
        {
            var selectedSave = saves[index - 1];
            terminal.WriteLine($"Loading {selectedSave.PlayerName}...", "yellow");
            
            // This would require restarting the game with the new save
            terminal.WriteLine("Note: Loading a different save requires restarting the game.", "cyan");
            terminal.WriteLine("Please exit and restart, then enter the character name to load.", "cyan");
        }
        else if (choice != "0")
        {
            terminal.WriteLine("Invalid choice!", "red");
        }
        
        await terminal.PressAnyKey("Press Enter to continue...");
    }
    
    /// <summary>
    /// Delete save files
    /// </summary>
    private async Task DeleteSaveFiles()
    {
        terminal.ClearScreen();
        terminal.SetColor("bright_red");
        terminal.WriteLine("═══════════════════════════════════════");
        terminal.WriteLine("         DELETE SAVE FILES");
        terminal.WriteLine("═══════════════════════════════════════");
        terminal.WriteLine("");
        
        terminal.SetColor("red");
        terminal.WriteLine("WARNING: This action cannot be undone!");
        terminal.WriteLine("");
        
        var saves = SaveSystem.Instance.GetAllSaves();
        
        if (saves.Count == 0)
        {
            terminal.WriteLine("No save files found!", "yellow");
            await terminal.PressAnyKey("Press Enter to continue...");
            return;
        }
        
        terminal.SetColor("white");
        terminal.WriteLine("Available save files:");
        terminal.WriteLine("");
        
        for (int i = 0; i < saves.Count; i++)
        {
            var save = saves[i];
            terminal.WriteLine($"{i + 1}. {save.PlayerName} (Level {save.Level}, Day {save.CurrentDay})");
        }
        
        terminal.WriteLine("");
        var choice = await terminal.GetInput($"Select save file to delete (1-{saves.Count}) or 0 to cancel: ");
        
        if (int.TryParse(choice, out var index) && index >= 1 && index <= saves.Count)
        {
            var selectedSave = saves[index - 1];
            
            terminal.WriteLine("");
            var confirm = await terminal.GetInput($"Are you sure you want to delete '{selectedSave.PlayerName}'? Type 'DELETE' to confirm: ");
            
            if (confirm == "DELETE")
            {
                var success = SaveSystem.Instance.DeleteSave(selectedSave.PlayerName);
                if (success)
                {
                    terminal.WriteLine("Save file deleted successfully!", "green");
                }
                else
                {
                    terminal.WriteLine("Failed to delete save file!", "red");
                }
            }
            else
            {
                terminal.WriteLine("Deletion cancelled.", "yellow");
            }
        }
        else if (choice != "0")
        {
            terminal.WriteLine("Invalid choice!", "red");
        }
        
        await terminal.PressAnyKey("Press Enter to continue...");
    }
    
    /// <summary>
    /// View save file information
    /// </summary>
    private async Task ViewSaveFileInfo()
    {
        terminal.ClearScreen();
        terminal.SetColor("bright_cyan");
        terminal.WriteLine("═══════════════════════════════════════");
        terminal.WriteLine("         SAVE FILE INFORMATION");
        terminal.WriteLine("═══════════════════════════════════════");
        terminal.WriteLine("");
        
        var saves = SaveSystem.Instance.GetAllSaves();
        
        if (saves.Count == 0)
        {
            terminal.WriteLine("No save files found!", "red");
            await terminal.PressAnyKey("Press Enter to continue...");
            return;
        }
        
        terminal.SetColor("white");
        foreach (var save in saves)
        {
            terminal.WriteLine($"Character: {save.PlayerName}", "yellow");
            terminal.WriteLine($"Level: {save.Level}");
            terminal.WriteLine($"Current Day: {save.CurrentDay}");
            terminal.WriteLine($"Turns Remaining: {save.TurnsRemaining}");
            terminal.WriteLine($"Last Saved: {save.SaveTime:yyyy-MM-dd HH:mm:ss}");
            terminal.WriteLine($"File: {save.FileName}");
            terminal.WriteLine("");
        }
        
        await terminal.PressAnyKey("Press Enter to continue...");
    }
    
    /// <summary>
    /// Force daily reset
    /// </summary>
    private async Task ForceDailyReset()
    {
        terminal.ClearScreen();
        terminal.SetColor("bright_yellow");
        terminal.WriteLine("═══════════════════════════════════════");
        terminal.WriteLine("         FORCE DAILY RESET");
        terminal.WriteLine("═══════════════════════════════════════");
        terminal.WriteLine("");
        
        terminal.SetColor("white");
        terminal.WriteLine("This will immediately trigger a daily reset, restoring your");
        terminal.WriteLine("daily limits and advancing the game day.");
        terminal.WriteLine("");
        
        var confirm = await terminal.GetInput("Are you sure? (yes/no): ");
        
        if (confirm.ToLower() == "yes")
        {
            var dailyManager = DailySystemManager.Instance;
            await dailyManager.ForceDailyReset();
            
            terminal.WriteLine("Daily reset completed!", "green");
        }
        else
        {
            terminal.WriteLine("Reset cancelled.", "yellow");
        }
        
        await terminal.PressAnyKey("Press Enter to continue...");
    }
    
    /// <summary>
    /// Get description for daily cycle mode
    /// </summary>
    private string GetDailyCycleModeDescription(DailyCycleMode mode)
    {
        return mode switch
        {
            DailyCycleMode.SessionBased => "Session-Based (resets when turns depleted)",
            DailyCycleMode.RealTime24Hour => "Real-Time 24 Hour (resets at midnight)",
            DailyCycleMode.Accelerated4Hour => "Accelerated 4 Hour (resets every 4 hours)",
            DailyCycleMode.Accelerated8Hour => "Accelerated 8 Hour (resets every 8 hours)", 
            DailyCycleMode.Accelerated12Hour => "Accelerated 12 Hour (resets every 12 hours)",
            DailyCycleMode.Endless => "Endless (no turn limits)",
            _ => "Unknown"
        };
    }

    /// <summary>
    /// Show player's mailbox using the MailSystem.
    /// </summary>
    private async Task ShowMail()
    {
        terminal.WriteLine("Checking your mailbox...", "cyan");
        await MailSystem.ReadPlayerMail(currentPlayer.Name2, terminal);
        terminal.WriteLine("Press ENTER to return to Main Street.", "gray");
        await terminal.GetInput("");
    }

    /// <summary>
    /// Show help screen with game commands and tips
    /// </summary>
    private async Task ShowHelp()
    {
        terminal.ClearScreen();
        terminal.SetColor("bright_cyan");
        terminal.WriteLine("╔══════════════════════════════════════════════════════════════════════════════╗");
        terminal.WriteLine("║                              HELP & COMMANDS                                 ║");
        terminal.WriteLine("╚══════════════════════════════════════════════════════════════════════════════╝");
        terminal.WriteLine("");

        terminal.SetColor("bright_yellow");
        terminal.WriteLine("=== LOCATIONS ===");
        terminal.SetColor("white");
        terminal.WriteLine("  [D] Dungeons      - Fight monsters, find treasure, gain experience");
        terminal.WriteLine("  [I] Inn           - Rest, socialize, gamble, and romance");
        terminal.WriteLine("  [W] Weapon Shop   - Buy and sell weapons");
        terminal.WriteLine("  [A] Armor Shop    - Buy and sell armor");
        terminal.WriteLine("  [M] Magic Shop    - Buy spells and magical items");
        terminal.WriteLine("  [H] Healer        - Cure wounds, poison, and ailments");
        terminal.WriteLine("  [B] Bank          - Deposit/withdraw gold, take loans");
        terminal.WriteLine("  [T] Temple        - Pray, donate, receive blessings");
        terminal.WriteLine("  [C] Castle        - Visit the royal court");
        terminal.WriteLine("  [Y] Your Home     - Rest and manage your belongings");
        terminal.WriteLine("  [*] Level Master  - Train to increase your level");
        terminal.WriteLine("  [J] Auction House  - Buy and sell items with other players");
        terminal.WriteLine("  [X] Dark Alley    - Shady dealings and criminal activity");
        terminal.WriteLine("");

        terminal.SetColor("bright_yellow");
        terminal.WriteLine("=== INFORMATION ===");
        terminal.SetColor("white");
        terminal.WriteLine("  [S] Status        - View your character stats");
        // terminal.WriteLine("  [L] List Players  - See other characters in the realm");  // Merged into Fame
        terminal.WriteLine("  [N] News          - Read the daily news");
        terminal.WriteLine("  [F] Fame          - View the hall of fame");
        terminal.WriteLine("  [R] Relations     - Check your relationships with NPCs");
        terminal.WriteLine("  [$] World Events  - See current events affecting the realm");
        terminal.WriteLine("");

        terminal.SetColor("bright_yellow");
        terminal.WriteLine("=== ACTIONS ===");
        terminal.SetColor("white");
        terminal.WriteLine("  [G] Good Deeds    - Perform charitable acts (+Chivalry)");
        terminal.WriteLine("  [E] Evil Deeds    - Commit dark acts (+Darkness)");
        terminal.WriteLine("  [0] Talk to NPCs  - Interact with characters at your location");
        terminal.WriteLine("");

        terminal.SetColor("bright_yellow");
        terminal.WriteLine("=== TIPS ===");
        terminal.SetColor("gray");
        terminal.WriteLine("  - Visit the Dungeons to gain experience and gold");
        terminal.WriteLine("  - When you have enough experience, visit your Level Master to advance");
        terminal.WriteLine("  - Keep gold in the Bank to protect it from thieves");
        terminal.WriteLine("  - Build relationships with NPCs - they can become allies or enemies");
        terminal.WriteLine("  - Your alignment (Chivalry vs Darkness) affects how NPCs treat you");
        terminal.WriteLine("");

        terminal.SetColor("cyan");
        await terminal.PressAnyKey("Press Enter to return to Main Street...");
    }

    /// <summary>
    /// Show current world events affecting the realm
    /// </summary>
    private async Task ShowWorldEvents()
    {
        terminal.ClearScreen();
        WorldEventSystem.Instance.DisplayWorldStatus(terminal);
        terminal.WriteLine("");
        await terminal.PressAnyKey("Press Enter to continue...");
    }

    /// <summary>
    /// Enter the secret developer menu for testing
    /// </summary>
    private async Task EnterDevMenu()
    {
        // In online/BBS mode, restrict dev menu to authorized users only
        if (UsurperRemake.BBS.DoorMode.IsOnlineMode)
        {
            // Online mode: only authorized admins can access
            var playerName = currentPlayer?.Name1;
            if (!string.Equals(playerName, "Rage", StringComparison.OrdinalIgnoreCase) &&
                !string.Equals(playerName, "fastfinge", StringComparison.OrdinalIgnoreCase))
            {
                terminal.SetColor("red");
                terminal.WriteLine("  Access denied.");
                await Task.Delay(1000);
                return;
            }
        }
        else if (UsurperRemake.BBS.DoorMode.IsInDoorMode)
        {
            // BBS door mode: only SysOps can access
            if (!UsurperRemake.BBS.DoorMode.IsSysOp)
            {
                terminal.SetColor("red");
                terminal.WriteLine("  Access denied.");
                await Task.Delay(1000);
                return;
            }
        }

        terminal.SetColor("dark_magenta");
        terminal.WriteLine("");
        terminal.WriteLine("  You notice a strange shimmer in the air...");
        await Task.Delay(500);
        terminal.WriteLine("  Reality seems to bend around you...");
        await Task.Delay(500);

        var devMenu = new DevMenuLocation();
        await devMenu.EnterLocation(currentPlayer, terminal);
    }

    /// <summary>
    /// Display the player's story progression - seals, gods, awakening, alignment
    /// </summary>
    private async Task ShowStoryProgress()
    {
        var story = StoryProgressionSystem.Instance;
        var ocean = OceanPhilosophySystem.Instance;
        var grief = GriefSystem.Instance;

        terminal.ClearScreen();
        terminal.SetColor("bright_cyan");
        terminal.WriteLine("╔══════════════════════════════════════════════════════════════════════════════╗");
        terminal.WriteLine("║                               ✦ YOUR JOURNEY ✦                               ║");
        terminal.WriteLine("╠══════════════════════════════════════════════════════════════════════════════╣");

        // === SEALS SECTION ===
        terminal.SetColor("bright_yellow");
        terminal.WriteLine("║                            THE SEVEN SEALS                                   ║");
        terminal.SetColor("gray");
        terminal.WriteLine("║  Ancient artifacts that reveal the truth of creation                         ║");
        terminal.WriteLine("║                                                                              ║");

        // Seal status display
        var sealTypes = new[] { SealType.Creation, SealType.FirstWar, SealType.Corruption, SealType.Imprisonment, SealType.Prophecy, SealType.Regret, SealType.Truth };
        var sealNames = new[] { "Creation", "First War", "Corruption", "Imprisonment", "Prophecy", "Regret", "Truth" };

        int sealsCollected = story.CollectedSeals?.Count ?? 0;
        terminal.SetColor("white");
        terminal.Write($"║  Seals Collected: {sealsCollected}/7   ");

        for (int i = 0; i < sealTypes.Length; i++)
        {
            bool hasIt = story.CollectedSeals?.Contains(sealTypes[i]) ?? false;
            if (hasIt)
            {
                terminal.SetColor("bright_green");
                terminal.Write("[X]");
            }
            else
            {
                terminal.SetColor("darkgray");
                terminal.Write("[ ]");
            }
        }
        terminal.SetColor("white");
        terminal.WriteLine($"                                      ║");

        // Show detailed seal info (without floor numbers - let players discover them)
        for (int i = 0; i < sealTypes.Length; i++)
        {
            bool hasIt = story.CollectedSeals?.Contains(sealTypes[i]) ?? false;
            string status = hasIt ? "+" : " ";
            string color = hasIt ? "bright_green" : "darkgray";
            string locationHint = hasIt ? "Found" : "Hidden in the depths...";
            terminal.SetColor(color);
            terminal.WriteLine($"║    {status} Seal of {sealNames[i],-12} - {locationHint,-30}                 ║");
        }

        terminal.SetColor("bright_cyan");
        terminal.WriteLine("╠══════════════════════════════════════════════════════════════════════════════╣");

        // === GODS SECTION ===
        terminal.SetColor("bright_magenta");
        terminal.WriteLine("║                             THE OLD GODS                                     ║");
        terminal.SetColor("gray");
        terminal.WriteLine("║  Ancient beings you may challenge for power or wisdom                        ║");
        terminal.WriteLine("║                                                                              ║");

        var godData = new[]
        {
            ("Maelketh", "God of War", "maelketh_encountered", "maelketh_defeated"),
            ("Terravok", "God of Earth", "terravok_encountered", "terravok_defeated"),
            ("Manwe", "Lord of Air", "manwe_encountered", "manwe_defeated")
        };

        foreach (var (name, title, encFlag, defFlag) in godData)
        {
            bool encountered = story.HasStoryFlag(encFlag);
            bool defeated = story.HasStoryFlag(defFlag);

            string status;
            string color;
            string location;
            if (defeated)
            {
                status = "DEFEATED";
                color = "bright_green";
                location = "Conquered";
            }
            else if (encountered)
            {
                status = "Encountered";
                color = "bright_yellow";
                location = "Known";
            }
            else
            {
                status = "Unknown";
                color = "darkgray";
                location = "Somewhere in the depths...";
            }

            terminal.SetColor(color);
            terminal.WriteLine($"║    {name,-10} {title,-15} {location,-25} [{status,-12}] ║");
        }

        terminal.SetColor("bright_cyan");
        terminal.WriteLine("╠══════════════════════════════════════════════════════════════════════════════╣");

        // === AWAKENING SECTION ===
        terminal.SetColor("bright_blue");
        terminal.WriteLine("║                          OCEAN PHILOSOPHY                                    ║");
        terminal.SetColor("gray");
        terminal.WriteLine("║  Your spiritual awakening through grief, sacrifice, and understanding        ║");
        terminal.WriteLine("║                                                                              ║");

        int awakeningLevel = ocean.AwakeningLevel;
        string awakeningDesc = awakeningLevel switch
        {
            0 => "Unawakened - You see only the surface of things",
            1 => "Stirring - Something deep within begins to move",
            2 => "Ripples - You sense connections between all things",
            3 => "Currents - The depths call to you with ancient whispers",
            4 => "Depths - You understand the ocean's sorrow",
            >= 5 => "Enlightened - You are one with the eternal tide",
            _ => "Unknown"
        };

        terminal.SetColor("bright_cyan");
        terminal.WriteLine($"║  Awakening Level: {awakeningLevel}/5                                                        ║");
        terminal.SetColor("white");
        terminal.WriteLine($"║  {awakeningDesc,-70} ║");

        // Grief status
        terminal.WriteLine("║                                                                              ║");
        string griefStatus = grief.CurrentStage switch
        {
            GriefStage.None => "At Peace",
            GriefStage.Denial => "In Denial - Loss seems unreal",
            GriefStage.Anger => "Angry - Why did this happen?",
            GriefStage.Bargaining => "Bargaining - If only...",
            GriefStage.Depression => "Depressed - The weight of loss",
            GriefStage.Acceptance => "Acceptance - Finding peace",
            _ => "Unknown"
        };

        string griefColor = grief.CurrentStage == GriefStage.None || grief.CurrentStage == GriefStage.Acceptance
            ? "bright_green"
            : "yellow";
        terminal.SetColor(griefColor);
        terminal.WriteLine($"║  Grief: {griefStatus,-66} ║");

        terminal.SetColor("bright_cyan");
        terminal.WriteLine("╠══════════════════════════════════════════════════════════════════════════════╣");

        // === ALIGNMENT SECTION ===
        terminal.SetColor("bright_white");
        terminal.WriteLine("║                              ALIGNMENT                                       ║");

        long chivalry = currentPlayer.Chivalry;
        string alignmentDesc;
        string alignColor;
        if (chivalry >= 100)
        {
            alignmentDesc = "Paragon of Virtue";
            alignColor = "bright_cyan";
        }
        else if (chivalry >= 50)
        {
            alignmentDesc = "Noble Hero";
            alignColor = "bright_green";
        }
        else if (chivalry >= 20)
        {
            alignmentDesc = "Good-Hearted";
            alignColor = "green";
        }
        else if (chivalry >= -20)
        {
            alignmentDesc = "Neutral";
            alignColor = "gray";
        }
        else if (chivalry >= -50)
        {
            alignmentDesc = "Questionable";
            alignColor = "yellow";
        }
        else if (chivalry >= -100)
        {
            alignmentDesc = "Villain";
            alignColor = "red";
        }
        else
        {
            alignmentDesc = "Usurper - Embodiment of Darkness";
            alignColor = "bright_red";
        }

        terminal.SetColor(alignColor);
        terminal.WriteLine($"║  Chivalry: {chivalry,4}  -  {alignmentDesc,-55} ║");

        // Show Darkness and wanted status
        long darkness = currentPlayer.Darkness;
        string darkDesc;
        string darkColor;
        if (darkness > 100)
        {
            darkDesc = "WANTED by the Royal Guard!";
            darkColor = "bright_red";
        }
        else if (darkness > 50)
        {
            darkDesc = "Suspicious reputation";
            darkColor = "yellow";
        }
        else if (darkness > 20)
        {
            darkDesc = "Rumored misdeeds";
            darkColor = "gray";
        }
        else
        {
            darkDesc = "Clean record";
            darkColor = "bright_green";
        }
        terminal.SetColor(darkColor);
        terminal.WriteLine($"║  Darkness: {darkness,4}  -  {darkDesc,-55} ║");

        terminal.SetColor("bright_cyan");
        terminal.WriteLine("╚══════════════════════════════════════════════════════════════════════════════╝");

        // Next objective hint (vague to encourage exploration)
        terminal.WriteLine("");
        terminal.SetColor("bright_yellow");
        if (sealsCollected < 7)
        {
            terminal.WriteLine($"  The ancient seals await discovery in the dungeon's depths...");
        }
        else if (!story.HasStoryFlag("manwe_defeated"))
        {
            terminal.WriteLine("  All seals gathered. The Creator awaits in the deepest reaches...");
        }
        else
        {
            terminal.WriteLine("  You have completed your journey. Seek your ending.");
        }

        terminal.WriteLine("");
        terminal.SetColor("gray");
        await terminal.PressAnyKey("Press Enter to return to Main Street...");
    }

    // ═══════════════════════════════════════════════════════════════════════════
    // World Boss
    // ═══════════════════════════════════════════════════════════════════════════

    private static readonly (string Name, int Level, long BaseHP)[] WorldBossTemplates = new[]
    {
        ("The Abyssal Leviathan",    40, 50000L),
        ("Void Colossus",            50, 80000L),
        ("Shadowlord Malachar",      35, 40000L),
        ("The Crimson Wyrm",         45, 65000L),
        ("Lich King Vareth",         55, 100000L),
        ("The Iron Titan",           60, 120000L),
        ("Dread Serpent Nidhogg",    70, 150000L),
        ("The Nameless Horror",      80, 200000L),
    };

    private async Task ShowWorldBossMenu()
    {
        var backend = SaveSystem.Instance.Backend as SqlSaveBackend;
        if (backend == null) return;

        // Expire old bosses and possibly spawn new one
        await backend.ExpireWorldBosses();
        var boss = await backend.GetActiveWorldBoss();

        if (boss == null)
        {
            // Spawn a new boss
            var rng = new Random();
            var template = WorldBossTemplates[rng.Next(WorldBossTemplates.Length)];
            int bossId = await backend.SpawnWorldBoss(template.Name, template.Level, template.BaseHP, 24);
            boss = await backend.GetActiveWorldBoss();
        }

        if (boss == null)
        {
            terminal.SetColor("gray");
            terminal.WriteLine("\n  No world boss is active right now.");
            await Task.Delay(2000);
            return;
        }

        while (true)
        {
            terminal.ClearScreen();
            terminal.SetColor("bright_magenta");
            terminal.WriteLine("╔══════════════════════════════════════════════════════════════════════════════╗");
            { const string t = "WORLD BOSS"; int l = (78 - t.Length) / 2, r = 78 - t.Length - l; terminal.WriteLine($"║{new string(' ', l)}{t}{new string(' ', r)}║"); }
            terminal.WriteLine("╚══════════════════════════════════════════════════════════════════════════════╝");
            terminal.WriteLine("");

            // Refresh boss state
            boss = await backend.GetActiveWorldBoss();
            if (boss == null || boss.Status != "active")
            {
                terminal.SetColor("bright_green");
                terminal.WriteLine("  The World Boss has been defeated! Check back later for a new challenge.");
                await terminal.PressAnyKey();
                break;
            }

            double hpPercent = (double)boss.CurrentHP / boss.MaxHP * 100;
            string hpBar = new string('█', (int)(hpPercent / 5)) + new string('░', 20 - (int)(hpPercent / 5));
            string hpColor = hpPercent > 50 ? "bright_green" : hpPercent > 25 ? "bright_yellow" : "bright_red";

            terminal.SetColor("bright_red");
            terminal.WriteLine($"  {boss.BossName}  (Level {boss.BossLevel})");
            terminal.SetColor(hpColor);
            terminal.WriteLine($"  HP: [{hpBar}] {boss.CurrentHP:N0} / {boss.MaxHP:N0} ({hpPercent:F1}%)");

            var timeLeft = boss.ExpiresAt - DateTime.UtcNow;
            terminal.SetColor("gray");
            terminal.WriteLine($"  Expires in: {timeLeft.Hours}h {timeLeft.Minutes}m");
            terminal.WriteLine("");

            // Show damage leaderboard
            var leaderboard = await backend.GetWorldBossDamageLeaderboard(boss.Id, 10);
            if (leaderboard.Count > 0)
            {
                terminal.SetColor("bright_yellow");
                terminal.WriteLine("  ═══ Damage Leaderboard ═══");
                for (int i = 0; i < leaderboard.Count; i++)
                {
                    var entry = leaderboard[i];
                    terminal.SetColor(i < 3 ? "bright_yellow" : "white");
                    terminal.WriteLine($"  {i + 1,2}. {entry.PlayerName,-20} {entry.DamageDealt:N0} dmg ({entry.Hits} hits)");
                }
                terminal.WriteLine("");
            }

            terminal.SetColor("cyan");
            terminal.WriteLine("  [A] Attack Boss    [Q] Back");
            terminal.SetColor("white");
            terminal.Write("\n  Choice: ");
            string input = (await terminal.ReadLineAsync())?.Trim().ToUpper() ?? "";

            if (input == "Q" || input == "") break;

            if (input == "A")
            {
                await AttackWorldBoss(backend, boss);
            }
        }
    }

    private async Task AttackWorldBoss(SqlSaveBackend backend, WorldBossInfo boss)
    {
        if (currentPlayer.HP <= 0)
        {
            terminal.SetColor("red");
            terminal.WriteLine("\n  You're too injured to fight! Heal first.");
            await Task.Delay(1500);
            return;
        }

        // Create a boss monster for combat
        var bossMonster = new Character
        {
            Name1 = boss.BossName,
            Name2 = boss.BossName,
            Level = boss.BossLevel,
            HP = Math.Min((int)boss.CurrentHP, int.MaxValue),
            MaxHP = Math.Min((int)boss.MaxHP, int.MaxValue),
            Strength = boss.BossLevel * 3 + 20,
            Defence = boss.BossLevel * 2 + 15,
            Dexterity = boss.BossLevel * 2 + 10,
            Agility = boss.BossLevel + 10,
            AI = CharacterAI.Computer
        };

        // Run combat (limited rounds - player gets 5 rounds then boss "retreats")
        long totalDamage = 0;
        int rounds = 5;
        var rng = new Random();

        terminal.ClearScreen();
        terminal.SetColor("bright_red");
        terminal.WriteLine($"\n  You charge at {boss.BossName}!\n");

        for (int i = 0; i < rounds && currentPlayer.HP > 0; i++)
        {
            // Player attacks
            long playerDmg = Math.Max(1, currentPlayer.Strength + currentPlayer.WeapPow - bossMonster.Defence / 2);
            playerDmg = (long)(playerDmg * (0.7 + rng.NextDouble() * 0.6));
            totalDamage += playerDmg;

            terminal.SetColor("bright_green");
            terminal.WriteLine($"  Round {i + 1}: You strike for {playerDmg:N0} damage!");

            // Boss counter-attacks
            long bossDmg = Math.Max(1, bossMonster.Strength - currentPlayer.Defence / 2);
            bossDmg = (long)(bossDmg * (0.5 + rng.NextDouble() * 0.5));
            currentPlayer.HP = Math.Max(0, currentPlayer.HP - bossDmg);

            terminal.SetColor("bright_red");
            terminal.WriteLine($"  {boss.BossName} strikes back for {bossDmg:N0} damage! (HP: {currentPlayer.HP}/{currentPlayer.MaxHP})");
            await Task.Delay(500);
        }

        terminal.WriteLine("");

        if (totalDamage > 0)
        {
            long remainingHp = await backend.RecordWorldBossDamage(boss.Id, currentPlayer.DisplayName.ToLower(), totalDamage);

            terminal.SetColor("bright_yellow");
            terminal.WriteLine($"  Total damage dealt: {totalDamage:N0}");
            terminal.SetColor("gray");
            terminal.WriteLine($"  Boss HP remaining: {Math.Max(0, remainingHp):N0}");

            if (remainingHp <= 0)
            {
                terminal.SetColor("bright_green");
                terminal.WriteLine($"\n  {boss.BossName} HAS BEEN DEFEATED!");
                terminal.SetColor("yellow");

                // Reward all contributors
                long goldReward = boss.BossLevel * 100;
                long xpReward = boss.BossLevel * 50;
                currentPlayer.Gold += goldReward;
                currentPlayer.Experience += xpReward;
                terminal.WriteLine($"  You earned {goldReward:N0} gold and {xpReward:N0} XP for your contribution!");

                if (UsurperRemake.Systems.OnlineStateManager.IsActive)
                    _ = UsurperRemake.Systems.OnlineStateManager.Instance!.AddNews(
                        $"{boss.BossName} has been defeated! {currentPlayer.DisplayName} dealt the final blow!", "world_boss");
            }
        }

        if (currentPlayer.HP <= 0)
        {
            currentPlayer.HP = Math.Max(1, currentPlayer.MaxHP / 4);
            terminal.SetColor("red");
            terminal.WriteLine("  You were struck down! Healers drag you to safety.");
            terminal.SetColor("cyan");
            terminal.WriteLine($"  Revived with {currentPlayer.HP}/{currentPlayer.MaxHP} HP.");
        }

        // Autosave after boss fight
        await SaveSystem.Instance.AutoSave(currentPlayer as Player);
        await terminal.PressAnyKey();
    }
}
