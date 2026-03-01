using UsurperRemake.Utils;
using UsurperRemake.Systems;
using UsurperRemake.BBS;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Threading.Tasks;

/// <summary>
/// The Inn location - social hub with Seth Able, drinking, and team activities
/// Based on Pascal INN.PAS and INNC.PAS
/// </summary>
public class InnLocation : BaseLocation
{
    private NPC sethAble = null!;
    private bool sethAbleAvailable = true;
    private int sethFightsToday = 0;     // Daily fight counter - max 3 per day
    private int sethDefeatsTotal = 0;    // Total times player has beaten Seth this session
    private int lastSethFightDay = -1;   // Track which game day the fights counter is for
    
    public InnLocation() : base(
        GameLocation.TheInn,
        "The Inn",
        "You enter the smoky tavern. The air is thick with the smell of ale and the sound of rowdy conversation."
    ) { }
    
    protected override string[]? GetAmbientMessages() => new[]
    {
        "The hearth crackles and spits a shower of sparks.",
        "Distant laughter erupts from a corner table.",
        "The smell of stale ale and pipe smoke hangs in the air.",
        "A bard strums somewhere, barely audible over the din.",
        "The floorboards creak as someone shuffles past.",
        "A mug is slammed on the bar with a satisfied thud.",
        "The door swings open and closed, letting in a gust of cold air.",
    };

    protected override void SetupLocation()
    {
        // Pascal-compatible exits from ONLINE.PAS onloc_theinn case
        PossibleExits = new List<GameLocation>
        {
            GameLocation.MainStreet,    // loc1 - back to main street
            GameLocation.TeamCorner     // loc2 - team corner
        };
        
        // Inn-specific actions
        LocationActions = new List<string>
        {
            "Buy a drink (5 gold)",         // Drinking system
            "Challenge Seth Able",          // Fight Seth Able
            "Talk to patrons",              // Social interaction  
            "Play drinking game",           // Drinking competition
            "Listen to gossip",             // Information gathering (real simulation events)
            "Check bulletin board",         // News and messages
            "Rest at table",                // Minor healing
            "Order food (10 gold)"          // Stamina boost
        };
        
        // Create Seth Able NPC
        CreateSethAble();
    }
    
    /// <summary>
    /// Create the famous Seth Able NPC
    /// </summary>
    private void CreateSethAble()
    {
        sethAble = new NPC("Seth Able", "drunk_fighter", CharacterClass.Warrior, 15)
        {
            IsSpecialNPC = true,
            SpecialScript = "drunk_fighter",
            IsHostile = false,
            CurrentLocation = "Inn"
        };
        
        // Set Seth Able's stats (he's tough!)
        sethAble.Strength = 45;
        sethAble.Defence = 35;
        sethAble.HP = 200;
        sethAble.MaxHP = 200;
        sethAble.Level = 15;
        sethAble.Experience = 50000;
        sethAble.Gold = 1000;
        
        // Seth is usually drunk
        sethAble.Mental = 30; // Poor mental state from drinking
        
        AddNPC(sethAble);
    }
    
    /// <summary>
    /// Override entry to check for Aldric's bandit defense event
    /// </summary>
    public override async Task EnterLocation(Character player, TerminalEmulator term)
    {
        await base.EnterLocation(player, term);

        // Check if Aldric bandit event should trigger (only once per session)
        await CheckAldricBanditEvent();
    }

    /// <summary>
    /// Flag to track if bandit event already triggered this session
    /// </summary>
    private bool aldricBanditEventTriggered = false;

    /// <summary>
    /// Check if Aldric's recruitment event should trigger
    /// Aldric defends the player from bandits in the tavern
    /// </summary>
    private async Task CheckAldricBanditEvent()
    {
        // Only trigger if:
        // 1. Player is at least level 10 (Aldric's recruit level)
        // 2. Aldric has NOT been recruited yet
        // 3. Aldric is NOT dead
        // 4. Event hasn't triggered this session
        // 5. 20% chance each visit
        if (aldricBanditEventTriggered) return;

        var aldric = CompanionSystem.Instance.GetCompanion(CompanionId.Aldric);
        if (aldric == null || aldric.IsRecruited || aldric.IsDead) return;
        if (currentPlayer.Level < aldric.RecruitLevel) return;

        // 20% chance to trigger the event
        var random = new Random();
        if (random.NextDouble() > 0.20) return;

        aldricBanditEventTriggered = true;
        await TriggerAldricBanditEvent(aldric);
    }

    /// <summary>
    /// Trigger the Aldric bandit defense event
    /// </summary>
    private async Task TriggerAldricBanditEvent(Companion aldric)
    {
        terminal.ClearScreen();

        // Dramatic encounter
        terminal.SetColor("red");
        terminal.WriteLine("╔══════════════════════════════════════════════════════════════════════════════╗");
        { const string t = "TROUBLE AT THE INN!"; int l = (78 - t.Length) / 2, r = 78 - t.Length - l; terminal.WriteLine($"║{new string(' ', l)}{t}{new string(' ', r)}║"); }
        terminal.WriteLine("╚══════════════════════════════════════════════════════════════════════════════╝");
        terminal.WriteLine("");

        await Task.Delay(1000);

        terminal.SetColor("white");
        terminal.WriteLine("You're sitting at the bar when the door bursts open.");
        terminal.WriteLine("Three rough-looking bandits swagger in, their eyes fixing on you.");
        terminal.WriteLine("");
        await Task.Delay(1500);

        terminal.SetColor("red");
        terminal.WriteLine("BANDIT LEADER: \"Well, well... looks like we found ourselves an adventurer.\"");
        terminal.WriteLine("               \"Hand over your gold, and maybe we'll let you keep your teeth.\"");
        terminal.WriteLine("");
        await Task.Delay(1500);

        terminal.SetColor("white");
        terminal.WriteLine("The bandits draw their weapons and move to surround you.");
        terminal.WriteLine("The other patrons quickly move away, not wanting to get involved.");
        terminal.WriteLine("");
        await Task.Delay(1500);

        // Aldric intervenes
        terminal.SetColor("bright_yellow");
        terminal.WriteLine("Suddenly, a chair scrapes loudly against the floor.");
        terminal.WriteLine("");
        await Task.Delay(1000);

        terminal.SetColor("cyan");
        terminal.WriteLine("A tall, broad-shouldered man rises from a shadowy corner.");
        terminal.WriteLine("He wears the tattered remains of what was once fine armor,");
        terminal.WriteLine("and carries a battered but well-maintained shield.");
        terminal.WriteLine("");
        await Task.Delay(1500);

        terminal.SetColor("bright_cyan");
        terminal.WriteLine("ALDRIC: \"Three against one? That's hardly sporting.\"");
        terminal.WriteLine("");
        await Task.Delay(1000);

        terminal.SetColor("red");
        terminal.WriteLine("BANDIT LEADER: \"Stay out of this, old man, unless you want trouble.\"");
        terminal.WriteLine("");
        await Task.Delay(1000);

        terminal.SetColor("bright_cyan");
        terminal.WriteLine("ALDRIC: \"Son, I AM trouble.\"");
        terminal.WriteLine("");
        await Task.Delay(1000);

        // Battle description
        terminal.SetColor("bright_yellow");
        terminal.WriteLine("The stranger moves with practiced efficiency.");
        terminal.WriteLine("His shield deflects the first bandit's clumsy swing.");
        terminal.WriteLine("A quick strike sends the second sprawling.");
        terminal.WriteLine("The leader takes one look at his fallen companions and flees.");
        terminal.WriteLine("");
        await Task.Delay(2000);

        terminal.SetColor("white");
        terminal.WriteLine("The stranger turns to you, wiping a trickle of blood from his lip.");
        terminal.WriteLine("");
        await Task.Delay(1000);

        terminal.SetColor("bright_cyan");
        terminal.WriteLine($"ALDRIC: \"You alright? {currentPlayer.Name2 ?? currentPlayer.Name1}, isn't it?\"");
        terminal.WriteLine("         \"I've heard about your exploits. You've got a reputation.\"");
        terminal.WriteLine("");
        await Task.Delay(1500);

        terminal.SetColor("cyan");
        terminal.WriteLine("He extends a calloused hand.");
        terminal.WriteLine("");
        terminal.SetColor("bright_cyan");
        terminal.WriteLine("ALDRIC: \"Name's Aldric. Used to be captain of the King's Guard.\"");
        terminal.WriteLine("         \"These days I'm just... looking for a purpose.\"");
        terminal.WriteLine("");
        await Task.Delay(1500);

        terminal.SetColor("white");
        terminal.WriteLine("He glances at you appraisingly.");
        terminal.WriteLine("");
        terminal.SetColor("bright_cyan");
        terminal.WriteLine("ALDRIC: \"You seem like someone who could use a shield at their back.\"");
        terminal.WriteLine("         \"And I... could use someone worth protecting again.\"");
        terminal.WriteLine("");

        await Task.Delay(1000);

        // Recruitment choice
        terminal.SetColor("bright_yellow");
        terminal.WriteLine("[Y] Accept Aldric as a companion");
        terminal.WriteLine("[N] Thank him but decline");
        terminal.WriteLine("");

        var choice = await terminal.GetInput("Your choice: ");

        if (choice.ToUpper() == "Y")
        {
            bool success = await CompanionSystem.Instance.RecruitCompanion(CompanionId.Aldric, currentPlayer, terminal);
            if (success)
            {
                terminal.SetColor("bright_green");
                terminal.WriteLine("");
                terminal.WriteLine("Aldric nods solemnly.");
                terminal.WriteLine("");
                terminal.SetColor("bright_cyan");
                terminal.WriteLine("ALDRIC: \"Then let's see what trouble we can find together.\"");
                terminal.WriteLine("         \"I've got your back. That's a promise.\"");
                terminal.WriteLine("");
                terminal.SetColor("yellow");
                terminal.WriteLine("Aldric, The Unbroken Shield, has joined your party!");
                terminal.WriteLine("");
                terminal.SetColor("gray");
                terminal.WriteLine("(Aldric is a tank-type companion who excels at protecting you in combat)");
            }
        }
        else
        {
            terminal.SetColor("cyan");
            terminal.WriteLine("");
            terminal.WriteLine("Aldric nods, a hint of disappointment in his eyes.");
            terminal.WriteLine("");
            terminal.SetColor("bright_cyan");
            terminal.WriteLine("ALDRIC: \"I understand. Not everyone wants a broken old soldier.\"");
            terminal.WriteLine("         \"But if you change your mind, I'll be around.\"");
            terminal.WriteLine("");
            terminal.SetColor("gray");
            terminal.WriteLine("(You can still recruit Aldric by approaching strangers in the Inn)");
        }

        await terminal.PressAnyKey();
    }

    protected override void DisplayLocation()
    {
        if (IsBBSSession) { DisplayLocationBBS(); return; }

        terminal.ClearScreen();

        // Inn header - standardized format
        terminal.SetColor("bright_cyan");
        terminal.WriteLine("╔═════════════════════════════════════════════════════════════════════════════╗");
        terminal.WriteLine($"║{"THE INN - 'The Drunken Dragon'".PadLeft((77 + 30) / 2).PadRight(77)}║");
        terminal.WriteLine("╚═════════════════════════════════════════════════════════════════════════════╝");
        terminal.WriteLine("");
        
        // Atmospheric description
        terminal.SetColor("white");
        terminal.WriteLine("The inn is dimly lit by flickering candles. Rough wooden tables are occupied");
        terminal.WriteLine("by travelers, merchants, and local toughs. The bartender eyes you suspiciously.");
        terminal.WriteLine("");
        
        // Special Seth Able description
        if (sethAbleAvailable)
        {
            terminal.SetColor("red");
            terminal.WriteLine("Seth Able, the notorious drunk fighter, sits hunched over a tankard in");
            terminal.WriteLine("the corner. His bloodshot eyes survey the room, looking for trouble.");
            terminal.WriteLine("");
        }
        
        // Show other NPCs
        ShowNPCsInLocation();
        
        // Show inn-specific menu
        ShowInnMenu();
        
        // Status line
        ShowStatusLine();
    }
    
    /// <summary>
    /// Show Inn-specific menu options
    /// </summary>
    private void ShowInnMenu()
    {
        terminal.SetColor("yellow");
        terminal.WriteLine("Inn Activities:");
        terminal.WriteLine("");

        // Row 1
        terminal.SetColor("darkgray");
        terminal.Write("[");
        terminal.SetColor("bright_yellow");
        terminal.Write("D");
        terminal.SetColor("darkgray");
        terminal.Write("] ");
        terminal.SetColor("white");
        terminal.Write("Buy a drink (5 gold)      ");

        terminal.SetColor("darkgray");
        terminal.Write("[");
        terminal.SetColor("bright_yellow");
        terminal.Write("T");
        terminal.SetColor("darkgray");
        terminal.Write("] ");
        terminal.SetColor("white");
        terminal.WriteLine("Talk to patrons");

        // Row 2
        terminal.SetColor("darkgray");
        terminal.Write("[");
        terminal.SetColor("bright_yellow");
        terminal.Write("F");
        terminal.SetColor("darkgray");
        terminal.Write("] ");
        terminal.SetColor("white");
        terminal.Write("Challenge Seth Able       ");

        terminal.SetColor("darkgray");
        terminal.Write("[");
        terminal.SetColor("bright_yellow");
        terminal.Write("G");
        terminal.SetColor("darkgray");
        terminal.Write("] ");
        terminal.SetColor("white");
        terminal.WriteLine("Play drinking game");

        // Row 3
        terminal.SetColor("darkgray");
        terminal.Write("[");
        terminal.SetColor("bright_yellow");
        terminal.Write("U");
        terminal.SetColor("darkgray");
        terminal.Write("] ");
        terminal.SetColor("white");
        terminal.Write("Listen to gossip          ");

        terminal.SetColor("darkgray");
        terminal.Write("[");
        terminal.SetColor("bright_yellow");
        terminal.Write("B");
        terminal.SetColor("darkgray");
        terminal.Write("] ");
        terminal.SetColor("white");
        terminal.WriteLine("Check bulletin board");

        // Row 4
        terminal.SetColor("darkgray");
        terminal.Write("[");
        terminal.SetColor("bright_yellow");
        terminal.Write("E");
        terminal.SetColor("darkgray");
        terminal.Write("] ");
        terminal.SetColor("white");
        terminal.Write("Rest at table             ");

        terminal.SetColor("darkgray");
        terminal.Write("[");
        terminal.SetColor("bright_yellow");
        terminal.Write("O");
        terminal.SetColor("darkgray");
        terminal.Write("] ");
        terminal.SetColor("white");
        terminal.WriteLine("Order food (10 gold)");
        terminal.WriteLine("");

        // Check for recruitable companions
        var recruitableCompanions = CompanionSystem.Instance.GetRecruitableCompanions(currentPlayer?.Level ?? 1).ToList();
        if (recruitableCompanions.Any())
        {
            terminal.SetColor("bright_magenta");
            terminal.WriteLine("A mysterious stranger catches your eye from a shadowy corner...");
            terminal.WriteLine("");
        }

        // Show recruited companions waiting at the inn
        var recruitedCompanions = CompanionSystem.Instance.GetAllCompanions()
            .Where(c => c.IsRecruited && !c.IsDead).ToList();
        if (recruitedCompanions.Any())
        {
            terminal.SetColor("bright_cyan");
            terminal.WriteLine($"Your companions ({recruitedCompanions.Count}) are resting at a nearby table.");
            terminal.WriteLine("");
        }

        terminal.SetColor("cyan");
        terminal.WriteLine("Special Areas:");

        terminal.SetColor("darkgray");
        terminal.Write("[");
        terminal.SetColor("bright_yellow");
        terminal.Write("C");
        terminal.SetColor("darkgray");
        terminal.Write("] ");
        terminal.SetColor("white");
        terminal.Write("Team Corner                ");

        terminal.SetColor("darkgray");
        terminal.Write("[");
        terminal.SetColor("bright_yellow");
        terminal.Write("W");
        terminal.SetColor("darkgray");
        terminal.Write("] ");
        terminal.SetColor("white");
        terminal.WriteLine("Train with the Master");

        terminal.SetColor("darkgray");
        terminal.Write("[");
        terminal.SetColor("bright_yellow");
        terminal.Write("L");
        terminal.SetColor("darkgray");
        terminal.Write("] ");
        terminal.SetColor("white");
        terminal.WriteLine("Gambling Den");

        // Show companion option if available
        if (recruitableCompanions.Any())
        {
            terminal.SetColor("darkgray");
            terminal.Write("[");
            terminal.SetColor("bright_yellow");
            terminal.Write("A");
            terminal.SetColor("darkgray");
            terminal.Write("] ");
            terminal.SetColor("bright_magenta");
            terminal.WriteLine($"Approach the stranger ({recruitableCompanions.Count} available)");
        }

        // Show party management if player has companions
        if (recruitedCompanions.Any())
        {
            terminal.SetColor("darkgray");
            terminal.Write("[");
            terminal.SetColor("bright_yellow");
            terminal.Write("P");
            terminal.SetColor("darkgray");
            terminal.Write("] ");
            terminal.SetColor("bright_cyan");
            terminal.WriteLine($"Manage your party ({recruitedCompanions.Count} companions)");
        }
        terminal.WriteLine("");

        // Online mode options: Rent a Room + Attack a Sleeper
        if (UsurperRemake.BBS.DoorMode.IsOnlineMode)
        {
            long roomCost = (long)(currentPlayer.Level * GameConfig.InnRoomCostPerLevel);
            terminal.SetColor("darkgray");
            terminal.Write("[");
            terminal.SetColor("bright_yellow");
            terminal.Write("N");
            terminal.SetColor("darkgray");
            terminal.Write("] ");
            terminal.SetColor("bright_green");
            terminal.Write($"Rent a Room & Logout ({roomCost}g, protected)    ");

            terminal.SetColor("darkgray");
            terminal.Write("[");
            terminal.SetColor("bright_yellow");
            terminal.Write("K");
            terminal.SetColor("darkgray");
            terminal.Write("] ");
            terminal.SetColor("red");
            terminal.WriteLine("Attack a sleeper");
        }

        // Single-player: Wait until nightfall option
        if (!UsurperRemake.BBS.DoorMode.IsOnlineMode && currentPlayer != null
            && !DailySystemManager.CanRestForNight(currentPlayer))
        {
            terminal.SetColor("darkgray");
            terminal.Write("[");
            terminal.SetColor("bright_yellow");
            terminal.Write("Z");
            terminal.SetColor("darkgray");
            terminal.Write("] ");
            terminal.SetColor("dark_cyan");
            terminal.WriteLine("Wait until nightfall");
        }

        terminal.SetColor("yellow");
        terminal.WriteLine("Navigation:");

        terminal.SetColor("darkgray");
        terminal.Write("[");
        terminal.SetColor("bright_yellow");
        terminal.Write("R");
        terminal.SetColor("darkgray");
        terminal.Write("] ");
        terminal.SetColor("red");
        terminal.Write("Return to Main Street    ");

        terminal.SetColor("darkgray");
        terminal.Write("[");
        terminal.SetColor("bright_yellow");
        terminal.Write("S");
        terminal.SetColor("darkgray");
        terminal.Write("] ");
        terminal.SetColor("white");
        terminal.Write("Status    ");

        terminal.SetColor("darkgray");
        terminal.Write("[");
        terminal.SetColor("bright_yellow");
        terminal.Write("?");
        terminal.SetColor("darkgray");
        terminal.Write("] ");
        terminal.SetColor("white");
        terminal.WriteLine("Help");
        terminal.WriteLine("");
    }

    /// <summary>
    /// Compact BBS display for 80x25 terminals.
    /// </summary>
    private void DisplayLocationBBS()
    {
        terminal.ClearScreen();

        // Header
        ShowBBSHeader("THE INN - 'The Drunken Dragon'");

        // 1-line description
        terminal.SetColor("white");
        terminal.WriteLine(" Smoky tavern. Ale flows freely. Seth Able eyes you from the corner.");

        // NPCs
        ShowBBSNPCs();

        // Companions hint
        var recruitableCompanions = CompanionSystem.Instance.GetRecruitableCompanions(currentPlayer?.Level ?? 1).ToList();
        var recruitedCompanions = CompanionSystem.Instance.GetAllCompanions()
            .Where(c => c.IsRecruited && !c.IsDead).ToList();
        if (recruitableCompanions.Any())
        {
            terminal.SetColor("bright_magenta");
            terminal.WriteLine(" A mysterious stranger catches your eye from a shadowy corner...");
        }
        if (recruitedCompanions.Any())
        {
            terminal.SetColor("bright_cyan");
            terminal.WriteLine($" Your companions ({recruitedCompanions.Count}) rest at a nearby table.");
        }

        terminal.WriteLine("");

        // Menu rows
        terminal.SetColor("yellow");
        terminal.WriteLine(" Inn Activities:");
        ShowBBSMenuRow(("D", "bright_yellow", "rink(5g)"), ("F", "bright_yellow", "ight Seth"), ("T", "bright_yellow", "alk"), ("G", "bright_yellow", "ame"));
        ShowBBSMenuRow(("U", "bright_yellow", "Rumors"), ("B", "bright_yellow", "ulletin"), ("E", "bright_yellow", "at/Rest"), ("O", "bright_yellow", "rder(10g)"));

        terminal.SetColor("cyan");
        terminal.WriteLine(" Areas:");
        ShowBBSMenuRow(("C", "bright_yellow", "Team Corner"), ("W", "bright_yellow", "Train"), ("L", "bright_yellow", "Gamble"));
        if (recruitableCompanions.Any() || recruitedCompanions.Any())
        {
            var items = new List<(string, string, string)>();
            if (recruitableCompanions.Any())
                items.Add(("A", "bright_yellow", $"Stranger({recruitableCompanions.Count})"));
            if (recruitedCompanions.Any())
                items.Add(("P", "bright_yellow", $"Party({recruitedCompanions.Count})"));
            ShowBBSMenuRow(items.ToArray());
        }

        // Online-mode options
        if (UsurperRemake.BBS.DoorMode.IsOnlineMode)
        {
            long roomCost = (long)(currentPlayer.Level * GameConfig.InnRoomCostPerLevel);
            ShowBBSMenuRow(("N", "bright_yellow", $"Room({roomCost}g)"), ("K", "bright_yellow", "Attack Sleeper"));
        }

        ShowBBSMenuRow(("R", "bright_yellow", "eturn"));

        // Footer: status + quick commands
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

        switch (upperChoice)
        {
            case "D":
                await BuyDrink();
                return false;
                
            case "F":
                await ChallengeSethAble();
                return false;
                
            case "T":
                await TalkToPatrons();
                return false;
                
            case "G":
                await PlayDrinkingGame();
                return false;
                
            case "U":
                await ListenToRumors();
                return false;

            case "R":
                await NavigateToLocation(GameLocation.MainStreet);
                return true;

            case "B":
                await CheckBulletinBoard();
                return false;
                
            case "E":
                await RestAtTable();
                return false;
                
            case "O":
                await OrderFood();
                return false;
                
            case "C":
                await NavigateToLocation(GameLocation.TeamCorner);
                return true;


            case "A":
                await ApproachCompanions();
                return false;

            case "P":
                await ManageParty();
                return false;

            case "W":
                await HandleStatTraining();
                return false;

            case "L":
                await HandleGamblingDen();
                return false;

            case "N":
                if (UsurperRemake.BBS.DoorMode.IsOnlineMode)
                    await RentRoom();
                return false;

            case "K":
                if (UsurperRemake.BBS.DoorMode.IsOnlineMode)
                    await AttackInnSleeper();
                return false;

            case "Z":
                if (!UsurperRemake.BBS.DoorMode.IsOnlineMode && currentPlayer != null)
                    await DailySystemManager.Instance.WaitUntilEvening(currentPlayer, terminal);
                return false;

            case "Q":
            case "M":
                await NavigateToLocation(GameLocation.MainStreet);
                return true;

            case "S":
                await ShowStatus();
                return false;
                
            case "?":
                // Menu already shown
                return false;

            case "0":
                // Talk to NPC (standard "0" option from BaseLocation)
                await TalkToPatrons();
                return false;

            default:
                terminal.WriteLine("Invalid choice! The bartender shakes his head.", "red");
                await Task.Delay(1500);
                return false;
        }
    }
    
    /// <summary>
    /// Buy a drink at the inn
    /// </summary>
    private async Task BuyDrink()
    {
        long drinkBasePrice = 5;
        var (drinkKingTax, drinkCityTax, drinkTotalWithTax) = CityControlSystem.CalculateTaxedPrice(drinkBasePrice);

        if (currentPlayer.Gold < drinkTotalWithTax)
        {
            terminal.WriteLine("You don't have enough gold for a drink!", "red");
            await Task.Delay(2000);
            return;
        }

        // Show tax breakdown
        CityControlSystem.Instance.DisplayTaxBreakdown(terminal, "Drink", drinkBasePrice);

        currentPlayer.Gold -= drinkTotalWithTax;
        CityControlSystem.Instance.ProcessSaleTax(drinkBasePrice);
        currentPlayer.DrinksLeft--;
        
        terminal.SetColor("green");
        terminal.WriteLine("You order a tankard of ale from the bartender.");
        terminal.WriteLine("The bitter brew slides down your throat...");
        
        // Random drink effects
        var effect = Random.Shared.Next(1, 5);
        switch (effect)
        {
            case 1:
                terminal.WriteLine("The ale boosts your confidence! (+2 Charisma temporarily)");
                currentPlayer.Charisma += 2;
                break;
            case 2:
                terminal.WriteLine("You feel slightly dizzy but stronger! (+1 Strength temporarily)");
                currentPlayer.Strength += 1;
                break;
            case 3:
                terminal.WriteLine("The alcohol makes you reckless! (-1 Wisdom temporarily)");
                currentPlayer.Wisdom = Math.Max(1, currentPlayer.Wisdom - 1);
                break;
            case 4:
                terminal.WriteLine("You feel relaxed and restored. (+5 HP)");
                currentPlayer.HP = Math.Min(currentPlayer.MaxHP, currentPlayer.HP + 5);
                break;
        }
        
        await Task.Delay(2500);
    }
    
    /// <summary>
    /// Challenge Seth Able to a fight
    /// Max 3 fights per game day. Seth scales to player level so he's always a challenge.
    /// </summary>
    private async Task ChallengeSethAble()
    {
        if (!sethAbleAvailable)
        {
            terminal.WriteLine("Seth Able is passed out under a table. Try again later.", "gray");
            await Task.Delay(1500);
            return;
        }

        // Reset daily counter if new day
        int today = DailySystemManager.Instance?.CurrentDay ?? 0;
        if (today != lastSethFightDay)
        {
            sethFightsToday = 0;
            lastSethFightDay = today;
            sethAbleAvailable = true; // Seth recovers each new day
        }

        // Daily fight limit: 3 per day
        var sethFights = DoorMode.IsOnlineMode ? currentPlayer.SethFightsToday : sethFightsToday;
        if (sethFights >= 3)
        {
            terminal.SetColor("yellow");
            terminal.WriteLine("Seth Able waves you off dismissively.");
            terminal.WriteLine("\"Enough already! I've had my fill of brawling today.\"", "yellow");
            terminal.WriteLine("\"Come back tomorrow if you want another beating!\"", "yellow");
            await Task.Delay(2000);
            return;
        }

        // Calculate Seth's level for display - he scales with player
        int sethLevel = GetSethLevel();

        terminal.ClearScreen();
        terminal.SetColor("red");
        terminal.WriteLine("CHALLENGING SETH ABLE");
        terminal.WriteLine("====================");
        terminal.WriteLine("");

        // Seth's drunken response
        var responses = new[]
        {
            "*hiccup* You want a piece of me?!",
            "You lookin' at me funny, stranger?",
            "*burp* Think you can take the great Seth Able?",
            "I'll show you what a REAL fighter can do!",
            "*sways* Come on then, if you think you're hard enough!"
        };

        terminal.SetColor("yellow");
        terminal.WriteLine($"Seth Able: \"{responses[Random.Shared.Next(0, responses.Length)]}\"");
        terminal.WriteLine("");

        terminal.SetColor("white");
        terminal.WriteLine("WARNING: Seth Able is a dangerous opponent!");
        terminal.WriteLine($"Seth Able - Level {sethLevel} - HP: {GetSethHP(sethLevel)}");
        terminal.WriteLine($"You - Level {currentPlayer.Level} - HP: {currentPlayer.HP}/{currentPlayer.MaxHP}");
        if (sethFights > 0)
        {
            terminal.SetColor("gray");
            terminal.WriteLine($"(Fights today: {sethFights}/3)");
        }
        terminal.WriteLine("");

        var confirm = await terminal.GetInput("Are you sure you want to fight? (y/N): ");

        if (confirm.ToUpper() == "Y")
        {
            await FightSethAble();
        }
        else
        {
            terminal.WriteLine("Seth Able: \"Hah! Smart choice, coward!\"", "yellow");
            await Task.Delay(2000);
        }
    }

    /// <summary>
    /// Get Seth's effective level - scales with player but always 2-5 levels ahead
    /// Minimum level 15 (his base), scales to always be a challenge
    /// </summary>
    private int GetSethLevel()
    {
        int playerLevel = (int)currentPlayer.Level;
        // Seth is always 3 levels above player, minimum 15, max 80
        return Math.Clamp(playerLevel + 3, 15, 80);
    }

    /// <summary>
    /// Get Seth's HP for a given level
    /// </summary>
    private static long GetSethHP(int sethLevel)
    {
        return 100 + sethLevel * 12;
    }

    /// <summary>
    /// Fight Seth Able using full combat engine.
    /// Seth scales to player level so he's always a genuine challenge.
    /// Uses nr:1 to prevent inflated XP from level-based formulas.
    /// </summary>
    private async Task FightSethAble()
    {
        terminal.WriteLine("The inn falls silent as you approach Seth Able...", "red");
        await Task.Delay(2000);

        int sethLevel = GetSethLevel();
        long sethHP = GetSethHP(sethLevel);
        // Stats scale with level: always a tough brawler
        long sethStr = 20 + sethLevel;
        long sethDef = 10 + sethLevel / 2;
        long sethPunch = 20 + sethLevel;
        long sethArmPow = 8 + sethLevel / 3;
        long sethWeapPow = 15 + sethLevel / 2;

        // nr:1 keeps monster Level=1 so GetExperienceReward()/GetGoldReward() yield
        // minimal base rewards. The real reward is the flat bonus below.
        var sethMonster = Monster.CreateMonster(
            nr: 1,
            name: "Seth Able",
            hps: sethHP,
            strength: sethStr,
            defence: sethDef,
            phrase: "You lookin' at me funny?!",
            grabweap: false,
            grabarm: false,
            weapon: "Massive Fists",
            armor: "Thick Skin",
            poisoned: false,
            disease: false,
            punch: sethPunch,
            armpow: sethArmPow,
            weappow: sethWeapPow
        );

        // Override display level (for UI) without affecting reward formulas
        // Note: CreateMonster sets Level = Math.Max(1, nr), so Level=1 for rewards
        sethMonster.IsUnique = true;
        sethMonster.IsBoss = false;
        sethMonster.CanSpeak = true;

        var combatEngine = new CombatEngine(terminal);
        var result = await combatEngine.PlayerVsMonster(currentPlayer, sethMonster);

        if (DoorMode.IsOnlineMode)
            currentPlayer.SethFightsToday++;
        else
            sethFightsToday++;

        if (result.ShouldReturnToTemple)
        {
            terminal.SetColor("yellow");
            terminal.WriteLine("You awaken at the Temple of Light...");
            await Task.Delay(2000);
            await NavigateToLocation(GameLocation.Temple);
            return;
        }

        switch (result.Outcome)
        {
            case CombatOutcome.Victory:
                sethDefeatsTotal++;
                terminal.SetColor("bright_green");
                terminal.WriteLine("");

                if (sethDefeatsTotal == 1)
                {
                    terminal.WriteLine("INCREDIBLE! You have defeated Seth Able!");
                    terminal.WriteLine("The entire inn erupts in shocked silence...");
                    terminal.WriteLine("Even the bartender drops his glass in amazement!");
                    terminal.WriteLine("");
                    terminal.WriteLine("You are now a legend in this tavern!");
                    currentPlayer.PKills++;
                    currentPlayer.Fame += 10;
                    currentPlayer.Chivalry += 5;
                }
                else
                {
                    terminal.WriteLine("You've beaten Seth Able again!");
                    terminal.WriteLine("The patrons cheer, but they've seen this before...");
                    // Diminishing fame - 1 point after first win
                    currentPlayer.Fame += 1;
                }

                // Flat reward: modest XP and gold, NOT scaling with fake level
                // This replaces the combat engine's level-based reward (which is tiny at nr=1)
                long xpReward = currentPlayer.Level * 200;
                long goldReward = 50 + currentPlayer.Level * 5;

                // Diminishing returns: halve rewards after 3rd lifetime win
                if (sethDefeatsTotal > 3)
                {
                    xpReward /= 2;
                    goldReward /= 2;
                }

                currentPlayer.Experience += xpReward;
                currentPlayer.Gold += goldReward;

                terminal.SetColor("white");
                terminal.WriteLine($"You earn {xpReward:N0} experience and {goldReward:N0} gold.");

                // Seth is knocked out for the rest of the day
                sethAbleAvailable = false;
                sethAble.SetState(NPCState.Unconscious);
                break;

            case CombatOutcome.PlayerDied:
                terminal.SetColor("red");
                terminal.WriteLine("");
                terminal.WriteLine("Seth Able's powerful blow knocks you unconscious!");
                terminal.WriteLine("You wake up later with a massive headache...");
                currentPlayer.HP = 1;
                currentPlayer.PDefeats++;
                break;

            case CombatOutcome.PlayerEscaped:
                terminal.SetColor("yellow");
                terminal.WriteLine("");
                terminal.WriteLine("You manage to back away from Seth Able!");
                terminal.WriteLine("'That's right, walk away!' Seth calls after you.");
                terminal.WriteLine("The other patrons chuckle at your retreat.");
                break;

            default:
                terminal.SetColor("red");
                terminal.WriteLine("");
                terminal.WriteLine("Seth Able's massive fist connects with your jaw!");
                terminal.WriteLine("You crash into a table and slide to the floor...");
                terminal.WriteLine("The patrons laugh as Seth returns to his drink.");
                terminal.WriteLine("");
                terminal.WriteLine("'Maybe next time, kid!' Seth gruffs.");
                currentPlayer.PDefeats++;
                break;
        }

        await Task.Delay(3000);
    }
    
    /// <summary>
    /// Override base TalkToNPC so the global [0] command routes through the Inn's
    /// patron interaction flow instead of the generic base location Talk screen.
    /// Without this, [0] in TryProcessGlobalCommand calls base TalkToNPC() directly,
    /// bypassing the Inn's TalkToPatrons() and showing stale relationship labels.
    /// </summary>
    protected override async Task TalkToNPC()
    {
        await TalkToPatrons();
    }

    /// <summary>
    /// Talk to other patrons - now with interactive NPC selection
    /// </summary>
    private async Task TalkToPatrons()
    {
        terminal.ClearScreen();
        terminal.SetColor("cyan");
        terminal.WriteLine("Mingle with Patrons");
        terminal.WriteLine("===================");
        terminal.WriteLine("");

        // Get live NPCs at the Inn
        var npcsHere = GetLiveNPCsAtLocation();

        if (npcsHere.Count == 0)
        {
            terminal.SetColor("gray");
            terminal.WriteLine("The inn is quiet tonight. No interesting patrons to talk to.");
            await terminal.PressAnyKey();
            return;
        }

        // Show NPCs with interaction options
        terminal.SetColor("white");
        terminal.WriteLine("You see the following patrons here:");
        terminal.WriteLine("");

        for (int i = 0; i < Math.Min(npcsHere.Count, 8); i++)
        {
            var npc = npcsHere[i];
            var alignColor = npc.Darkness > npc.Chivalry ? "red" : (npc.Chivalry > 500 ? "bright_green" : "cyan");
            terminal.SetColor(alignColor);
            terminal.WriteLine($"  [{i + 1}] {npc.Name2} - Level {npc.Level} {npc.Class} ({GetAlignmentDisplay(npc)})");
        }

        terminal.WriteLine("");
        terminal.SetColor("bright_yellow");
        terminal.WriteLine("[0] Return to inn menu");
        terminal.WriteLine("");

        var choice = await terminal.GetInput("Choose someone to approach (0-8): ");

        if (int.TryParse(choice, out int npcIndex) && npcIndex > 0 && npcIndex <= Math.Min(npcsHere.Count, 8))
        {
            await InteractWithNPC(npcsHere[npcIndex - 1]);
        }
    }

    /// <summary>
    /// Interactive menu for NPC interaction (Inn-specific override)
    /// Uses the VisualNovelDialogueSystem for full romance features
    /// </summary>
    protected override async Task InteractWithNPC(NPC npc)
    {
        bool continueInteraction = true;

        while (continueInteraction)
        {
            terminal.ClearScreen();
            terminal.SetColor("bright_cyan");
            terminal.WriteLine($"Interacting with {npc.Name2}");
            terminal.WriteLine(new string('─', 30 + npc.Name2.Length));
            terminal.WriteLine("");

            // Show NPC info
            terminal.SetColor("white");
            terminal.WriteLine($"  Level {npc.Level} {npc.Class}");
            terminal.WriteLine($"  {GetNPCMood(npc)}");
            terminal.WriteLine("");

            // Get relationship status
            var relationship = RelationshipSystem.GetRelationshipStatus(currentPlayer, npc);
            terminal.SetColor(GetRelationshipColor(relationship));
            terminal.WriteLine($"  Relationship: {GetRelationshipText(relationship)}");

            // Show alignment compatibility
            var reactionMod = AlignmentSystem.Instance.GetNPCReactionModifier(currentPlayer, npc);
            if (reactionMod >= 1.3f)
            {
                terminal.SetColor("bright_green");
                terminal.WriteLine($"  Alignment: Kindred spirits (excellent rapport)");
            }
            else if (reactionMod >= 1.0f)
            {
                terminal.SetColor("green");
                terminal.WriteLine($"  Alignment: Compatible (good rapport)");
            }
            else if (reactionMod >= 0.7f)
            {
                terminal.SetColor("yellow");
                terminal.WriteLine($"  Alignment: Neutral (standard rapport)");
            }
            else
            {
                terminal.SetColor("red");
                terminal.WriteLine($"  Alignment: Opposing (poor rapport)");
            }
            terminal.WriteLine("");

            // Show interaction options
            terminal.SetColor("yellow");
            terminal.WriteLine("What would you like to do?");
            terminal.WriteLine("");

            terminal.SetColor("bright_yellow");
            terminal.Write("[T]");
            terminal.SetColor("white");
            terminal.WriteLine(" Talk - Have a deep conversation (flirt, confess, romance)");
            terminal.SetColor("bright_yellow");
            terminal.Write("[C]");
            terminal.SetColor("white");
            terminal.WriteLine(" Challenge - Challenge to a duel");
            terminal.SetColor("bright_yellow");
            terminal.Write("[G]");
            terminal.SetColor("white");
            terminal.WriteLine(" Gift - Give a gift (costs 50 gold)");

            terminal.WriteLine("");
            terminal.SetColor("bright_yellow");
            terminal.Write("[0]");
            terminal.SetColor("gray");
            terminal.WriteLine(" Return");
            terminal.WriteLine("");

            var choice = await terminal.GetInput("Your choice: ");

            switch (choice.ToUpper())
            {
                case "T":
                    // Use the full VisualNovelDialogueSystem for all conversation/romance features
                    await UsurperRemake.Systems.VisualNovelDialogueSystem.Instance.StartConversation(currentPlayer, npc, terminal);
                    break;
                case "C":
                    await ChallengeNPC(npc);
                    continueInteraction = false; // Exit after combat
                    break;
                case "G":
                    await GiveGiftToNPC(npc);
                    break;
                case "0":
                    continueInteraction = false;
                    break;
            }
        }
    }

    /// <summary>
    /// Challenge an NPC to a duel
    /// </summary>
    private async Task ChallengeNPC(NPC npc)
    {
        // Seth Able has a dedicated challenge system with daily limits and flat rewards.
        // Redirect to it regardless of how the player reached this point.
        if (npc.IsSpecialNPC && npc.SpecialScript == "drunk_fighter")
        {
            await ChallengeSethAble();
            return;
        }

        terminal.ClearScreen();
        terminal.SetColor("red");
        terminal.WriteLine($"Challenging {npc.Name2} to a Duel!");
        terminal.WriteLine("");

        // Check if they'll accept
        bool accepts = npc.Darkness > 300 || new Random().Next(100) < 50;

        if (!accepts)
        {
            terminal.SetColor("gray");
            terminal.WriteLine($"{npc.Name2} laughs and waves you off. \"I have better things to do.\"");
            await terminal.PressAnyKey();
            return;
        }

        terminal.SetColor("yellow");
        terminal.WriteLine($"{npc.Name2} accepts your challenge!");
        terminal.WriteLine("\"You'll regret this decision!\"");
        terminal.WriteLine("");

        var confirm = await terminal.GetInput("Fight now? (y/N): ");
        if (confirm.ToUpper() != "Y")
        {
            terminal.WriteLine($"{npc.Name2}: \"Changed your mind? Coward!\"", "gray");
            await Task.Delay(2000);
            return;
        }

        // Create monster from NPC for combat
        var npcMonster = Monster.CreateMonster(
            nr: npc.Level,
            name: npc.Name2,
            hps: npc.HP,
            strength: npc.Strength,
            defence: npc.Defence,
            phrase: $"{npc.Name2} readies for battle!",
            grabweap: false,
            grabarm: false,
            weapon: "Weapon",
            armor: "Armor",
            poisoned: false,
            disease: false,
            punch: npc.Strength / 2,
            armpow: npc.ArmPow,
            weappow: npc.WeapPow
        );
        npcMonster.IsProperName = true;
        npcMonster.CanSpeak = true;

        var combatEngine = new CombatEngine(terminal);
        var result = await combatEngine.PlayerVsMonster(currentPlayer, npcMonster);

        // Check if player should return to temple after resurrection
        if (result.ShouldReturnToTemple)
        {
            terminal.SetColor("yellow");
            terminal.WriteLine("You awaken at the Temple of Light...");
            await Task.Delay(2000);
            await NavigateToLocation(GameLocation.Temple);
            return;
        }

        if (result.Outcome == CombatOutcome.Victory)
        {
            terminal.SetColor("bright_green");
            terminal.WriteLine("");
            terminal.WriteLine($"You have defeated {npc.Name2}!");
            terminal.WriteLine("Word of your victory spreads through the inn!");

            currentPlayer.Experience += npc.Level * 100;
            currentPlayer.PKills++;

            // Update relationship negatively
            RelationshipSystem.UpdateRelationship(currentPlayer, npc, -1, 5, false, false);

            // Record defeat memory on NPC for consequence encounters
            npc.Memory?.RecordEvent(new MemoryEvent
            {
                Type = MemoryType.Defeated,
                Description = $"Defeated in a tavern duel by {currentPlayer.Name2}",
                InvolvedCharacter = currentPlayer.Name2,
                Importance = 0.8f,
                EmotionalImpact = -0.7f,
                Location = "Inn"
            });

            // Generate news
            NewsSystem.Instance?.Newsy(true, $"{currentPlayer.Name} defeated {npc.Name2} in a tavern brawl!");
        }
        else if (result.Outcome == CombatOutcome.PlayerDied)
        {
            terminal.SetColor("red");
            terminal.WriteLine("");
            terminal.WriteLine($"{npc.Name2} knocks you unconscious!");
            currentPlayer.HP = 1; // Inn fights don't kill
            currentPlayer.PDefeats++;
        }

        await Task.Delay(3000);
    }

    /// <summary>
    /// Give a gift to an NPC
    /// </summary>
    private async Task GiveGiftToNPC(NPC npc)
    {
        if (currentPlayer.Gold < 50)
        {
            terminal.WriteLine("You don't have enough gold for a gift (50 gold needed).", "red");
            await Task.Delay(2000);
            return;
        }

        terminal.ClearScreen();
        terminal.SetColor("bright_yellow");
        terminal.WriteLine($"Giving a Gift to {npc.Name2}");
        terminal.WriteLine("");

        currentPlayer.Gold -= 50;

        var random = new Random();
        var responses = new[] {
            $"{npc.Name2}'s eyes light up. \"For me? How thoughtful!\"",
            $"{npc.Name2} accepts the gift graciously. \"You're too kind.\"",
            $"{npc.Name2} smiles broadly. \"I won't forget this kindness.\"",
        };

        terminal.SetColor("white");
        terminal.WriteLine(responses[random.Next(responses.Length)]);
        terminal.WriteLine("");

        // Big relationship boost
        RelationshipSystem.UpdateRelationship(currentPlayer, npc, 1, 5, false, false);
        terminal.SetColor("green");
        terminal.WriteLine("(Your relationship improves significantly!)");
        terminal.WriteLine("(-50 gold)");

        await terminal.PressAnyKey();
    }

    /// <summary>
    /// Get NPC mood description
    /// </summary>
    private string GetNPCMood(NPC npc)
    {
        if (npc.Darkness > npc.Chivalry + 200) return "They look aggressive and dangerous.";
        if (npc.Chivalry > npc.Darkness + 200) return "They seem friendly and approachable.";
        if (npc.HP < npc.MaxHP / 2) return "They look tired and worn from battle.";
        return "They seem relaxed and at ease.";
    }

    /// <summary>
    /// Get relationship status text
    /// </summary>
    private string GetRelationshipText(int relationship)
    {
        // Lower numbers are better relationships in Pascal system
        if (relationship <= GameConfig.RelationMarried) return "Married";
        if (relationship <= GameConfig.RelationLove) return "In Love";
        if (relationship <= GameConfig.RelationFriendship) return "Close Friend";
        if (relationship <= GameConfig.RelationNormal) return "Neutral";
        if (relationship <= GameConfig.RelationEnemy) return "Disliked";
        return "Hated Enemy";
    }

    /// <summary>
    /// Get relationship color
    /// </summary>
    private string GetRelationshipColor(int relationship)
    {
        // Lower numbers are better relationships in Pascal system
        if (relationship <= GameConfig.RelationLove) return "bright_magenta";
        if (relationship <= GameConfig.RelationFriendship) return "green";
        if (relationship <= GameConfig.RelationNormal) return "gray";
        if (relationship <= GameConfig.RelationEnemy) return "bright_red";
        return "red";
    }
    
    /// <summary>
    /// Play drinking game - full minigame based on original Pascal DRINKING.PAS
    /// Up to 5 NPC opponents, drink choice, soberness tracking, drunk comments, player input per round
    /// </summary>
    private async Task PlayDrinkingGame()
    {
        if (currentPlayer.Gold < 20)
        {
            terminal.WriteLine("You need at least 20 gold to enter the drinking contest!", "red");
            await Task.Delay(1500);
            return;
        }

        // Gather living NPCs as potential opponents
        var maxOpponents = 5;
        var allNPCs = NPCSpawnSystem.Instance?.ActiveNPCs?
            .Where(n => !n.IsDead && n.HP > 0 && n.Name2 != currentPlayer.Name2)
            .OrderBy(_ => (float)Random.Shared.NextDouble())
            .Take(maxOpponents)
            .ToList() ?? new List<NPC>();

        if (allNPCs.Count < 2)
        {
            terminal.WriteLine("There aren't enough patrons in the bar for a contest!", "red");
            await Task.Delay(1500);
            return;
        }

        currentPlayer.Gold -= 20;

        // --- Intro ---
        terminal.ClearScreen();
        terminal.WriteLine("");
        terminal.SetColor("bright_yellow");
        terminal.WriteLine("  +=======================================================+");
        terminal.SetColor("bright_white");
        terminal.WriteLine("  |              DRINKING CONTEST AT THE INN               |");
        terminal.SetColor("bright_yellow");
        terminal.WriteLine("  +=======================================================+");
        terminal.WriteLine("");

        terminal.SetColor("white");
        terminal.WriteLine("  You jump up on the bar counter!");
        terminal.SetColor("bright_cyan");
        terminal.WriteLine("  \"Come on you lazy boozers! I challenge you to a drinking contest!\"");
        terminal.WriteLine("");
        terminal.SetColor("gray");
        terminal.Write("  There is a sudden silence in the room");
        await Task.Delay(600);
        terminal.Write("...");
        await Task.Delay(600);
        terminal.WriteLine("...");
        await Task.Delay(400);
        terminal.SetColor("white");
        terminal.WriteLine("  Then a rowdy bunch of characters make their way toward you...");
        terminal.WriteLine("");

        // Show opponents joining
        var howdyLines = new[]
        {
            " accepts your challenge! \"I need to show you who's the master!\"",
            " sits down and says: \"I'm in! I can't see any competition here though...\"",
            " sits down and stares at you intensely...",
            " sits down and says: \"I feel sorry for you, {0}!\"",
            " sits down and mutters something you can't hear.",
            " sits down and says: \"Are you ready to lose, {0}!? Haha!\"",
            " sits down and says: \"Make room for me, you cry-babies!\"",
            " sits down and says: \"I can't lose!\"",
            " sits down and says: \"You are looking at the current Beer Champion!\"",
            " sits down without saying a word....",
        };

        foreach (var npc in allNPCs)
        {
            var line = howdyLines[Random.Shared.Next(0, howdyLines.Length)];
            line = string.Format(line, currentPlayer.Name2);
            terminal.SetColor("bright_green");
            terminal.Write($"  {npc.Name2}");
            terminal.SetColor("white");
            terminal.WriteLine(line);
            await Task.Delay(400);
        }

        terminal.WriteLine("");
        await terminal.PressAnyKey();

        // --- Drink Choice ---
        terminal.ClearScreen();
        terminal.WriteLine("");
        terminal.SetColor("bright_magenta");
        terminal.WriteLine("  Choose Your Competition Drink:");
        terminal.WriteLine("");
        terminal.SetColor("bright_yellow");
        terminal.Write("  [A] ");
        terminal.SetColor("yellow");
        terminal.WriteLine("Ale            - Easy going, more rounds to survive");
        terminal.SetColor("bright_yellow");
        terminal.Write("  [S] ");
        terminal.SetColor("yellow");
        terminal.WriteLine("Stout          - A solid choice for serious drinkers");
        terminal.SetColor("bright_yellow");
        terminal.Write("  [K] ");
        terminal.SetColor("red");
        terminal.WriteLine("Seth's Bomber  - Rocket fuel! Only the brave dare...");
        terminal.WriteLine("");

        string drinkName;
        int drinkStrength;
        string drinkReaction;
        var drinkChoice = (await terminal.GetInput("  Your choice: ")).Trim().ToUpperInvariant();

        switch (drinkChoice)
        {
            case "S":
                drinkName = "Stout";
                drinkStrength = 3;
                drinkReaction = "Your choice seems to have made everybody content...";
                break;
            case "K":
                drinkName = "Seth's Bomber";
                drinkStrength = 6;
                drinkReaction = "There is a buzz of wonder in the crowded bar...";
                break;
            default: // A or anything else
                drinkName = "Ale";
                drinkStrength = 2;
                drinkReaction = "\"That was a wimpy choice!\", someone shouts from the back.";
                break;
        }

        terminal.SetColor("bright_white");
        terminal.WriteLine($"  {drinkName}!");
        terminal.WriteLine("");
        terminal.SetColor("gray");
        terminal.WriteLine($"  {drinkReaction}");
        terminal.WriteLine("");
        await terminal.PressAnyKey();

        // --- Calculate soberness values ---
        // Based on original: (stamina + strength + charisma + 10) / 10, capped at 100
        long playerSoberness = Math.Min(100, (currentPlayer.Stamina + currentPlayer.Strength + currentPlayer.Constitution + 10) / 10);
        if (playerSoberness < 5) playerSoberness = 5; // minimum floor

        var opponents = new List<(string Name, long Soberness, bool Male)>();
        foreach (var npc in allNPCs)
        {
            long sob = Math.Min(100, (npc.Stamina + npc.Strength + npc.Constitution + 10) / 10);
            if (sob < 3) sob = 3;
            opponents.Add((npc.Name2, sob, npc.Sex == CharacterSex.Male));
        }

        // Rank and show favourite
        var allSob = opponents.Select(o => (o.Name, o.Soberness)).ToList();
        allSob.Add((currentPlayer.Name2, playerSoberness));
        allSob.Sort((a, b) => b.Soberness.CompareTo(a.Soberness));

        terminal.ClearScreen();
        terminal.WriteLine("");
        terminal.SetColor("bright_magenta");
        terminal.Write("  Favourite in this contest is... ");
        terminal.SetColor("bright_white");
        terminal.WriteLine($"{allSob[0].Name}!");
        terminal.WriteLine("");
        await terminal.PressAnyKey();

        // --- Main contest loop ---
        int round = 0;
        bool playerAlive = true;
        int playerRounds = 0;

        while (true)
        {
            round++;

            // Count remaining contestants
            int remaining = opponents.Count(o => o.Soberness > 0) + (playerAlive ? 1 : 0);
            if (remaining <= 1) break;

            terminal.ClearScreen();
            terminal.WriteLine("");
            terminal.SetColor("bright_yellow");
            terminal.WriteLine($"  === Beer Round #{round} ===   ({remaining} contestants remaining)");
            terminal.SetColor("bright_yellow");
            terminal.WriteLine($"  Drinking: {drinkName}");
            terminal.WriteLine("");

            // --- Player's turn ---
            if (playerAlive)
            {
                // Player chooses: drink or try to bow out
                terminal.SetColor("bright_white");
                terminal.WriteLine($"  Your soberness: {GetSobernessBar(playerSoberness)}");
                terminal.WriteLine("");
                terminal.SetColor("bright_yellow");
                terminal.Write("  [D]");
                terminal.SetColor("white");
                terminal.WriteLine(" Down your drink!");
                terminal.SetColor("bright_yellow");
                terminal.Write("  [Q]");
                terminal.SetColor("white");
                terminal.WriteLine(" Try to bow out gracefully");
                terminal.WriteLine("");

                var action = (await terminal.GetInput("  What do you do? ")).Trim().ToUpperInvariant();

                if (action == "Q")
                {
                    // CON check to bow out without embarrassment
                    int bowOutChance = 30 + (int)(currentPlayer.Constitution / 2);
                    if (bowOutChance > 80) bowOutChance = 80;
                    if (Random.Shared.Next(1, 101) <= bowOutChance)
                    {
                        terminal.SetColor("green");
                        terminal.WriteLine("  You stand up steadily and bow to the crowd.");
                        terminal.WriteLine("  \"I know my limits, friends. Good luck to you all!\"");
                        terminal.SetColor("gray");
                        terminal.WriteLine("  The crowd gives a polite, if disappointed, round of applause.");
                        playerAlive = false;
                        playerRounds = round;
                        terminal.WriteLine("");
                        await terminal.PressAnyKey();
                        continue;
                    }
                    else
                    {
                        terminal.SetColor("red");
                        terminal.WriteLine("  You try to stand up but your legs wobble...");
                        terminal.SetColor("yellow");
                        terminal.WriteLine("  \"Sit back down! You're not going anywhere!\"");
                        terminal.SetColor("gray");
                        terminal.WriteLine("  The crowd pushes another drink into your hand!");
                        terminal.WriteLine("");
                        await Task.Delay(800);
                        // Falls through to drinking
                    }
                }

                // Drink!
                terminal.SetColor("bright_cyan");
                terminal.Write("  You take your beer...");
                await Task.Delay(300);
                terminal.Write("Glugg...");
                await Task.Delay(200);
                terminal.Write("Glugg...");
                await Task.Delay(200);
                terminal.WriteLine("Glugg...!");

                // Reduce soberness: random(23 + drinkStrength)
                long reduction = Random.Shared.Next(1, (22 + drinkStrength) + 1);
                playerSoberness -= reduction;

                if (playerSoberness <= 0)
                {
                    playerSoberness = 0;
                    playerAlive = false;
                    playerRounds = round;
                    terminal.WriteLine("");
                    terminal.SetColor("red");
                    terminal.WriteLine("  The room is spinning!");
                    terminal.WriteLine("  You hear evil laughter as you stagger around the room...");
                    terminal.WriteLine("  ...finally falling heavily to the floor!");
                    terminal.SetColor("bright_red");
                    terminal.WriteLine("  You didn't make it, you drunken rat!");
                    terminal.WriteLine("");
                    await terminal.PressAnyKey();
                }
                else
                {
                    await Task.Delay(300);
                }
            }

            // --- Opponents' turns ---
            for (int i = 0; i < opponents.Count; i++)
            {
                var opp = opponents[i];
                if (opp.Soberness <= 0) continue;

                long oppReduction = Random.Shared.Next(1, (22 + drinkStrength) + 1);
                var newSob = opp.Soberness - oppReduction;

                if (playerAlive || round == playerRounds) // Only show if player is conscious
                {
                    terminal.SetColor("bright_green");
                    terminal.Write($"  {opp.Name}");
                    terminal.SetColor("white");
                    terminal.Write(opp.Male ? " takes his beer..." : " takes her beer...");
                    await Task.Delay(200);
                    terminal.Write("Glugg...");
                    await Task.Delay(150);
                    terminal.Write("Glugg...");
                    await Task.Delay(150);
                    terminal.WriteLine("Glugg...!");

                    if (newSob <= 0)
                    {
                        terminal.SetColor("yellow");
                        terminal.Write($"  {opp.Name}");
                        terminal.SetColor("white");
                        terminal.WriteLine(" starts to reel round in a daze!");
                        terminal.SetColor("gray");
                        terminal.WriteLine($"  Everybody laughs as {opp.Name} staggers and falls to the floor!");
                        terminal.SetColor("bright_yellow");
                        terminal.WriteLine("  Another one bites the dust!");
                        terminal.WriteLine("");
                        await Task.Delay(500);
                    }
                }

                opponents[i] = (opp.Name, Math.Max(0, newSob), opp.Male);
            }

            // --- Soberness report ---
            if (playerAlive)
            {
                terminal.WriteLine("");
                terminal.SetColor("bright_magenta");
                terminal.WriteLine("  --- Round Soberness Evaluation ---");
                terminal.WriteLine("");
                terminal.SetColor("bright_cyan");
                terminal.Write("  You - ");
                terminal.SetColor("white");
                terminal.WriteLine(GetDrunkComment(playerSoberness));

                foreach (var opp in opponents)
                {
                    if (opp.Soberness > 0)
                    {
                        terminal.SetColor("bright_green");
                        terminal.Write($"  {opp.Name} - ");
                        terminal.SetColor("white");
                        terminal.WriteLine(GetDrunkComment(opp.Soberness));
                    }
                }

                terminal.WriteLine("");
                await terminal.PressAnyKey();
            }

            // Check if contest is over
            remaining = opponents.Count(o => o.Soberness > 0) + (playerAlive ? 1 : 0);
            if (remaining <= 1) break;
        }

        // --- Results ---
        terminal.ClearScreen();
        terminal.WriteLine("");
        terminal.SetColor("bright_yellow");
        terminal.WriteLine("  +=======================================================+");
        terminal.SetColor("bright_white");
        terminal.WriteLine("  |                  CONTEST RESULTS                       |");
        terminal.SetColor("bright_yellow");
        terminal.WriteLine("  +=======================================================+");
        terminal.WriteLine("");

        // Determine winner
        string winnerName = "";
        if (playerAlive)
        {
            winnerName = currentPlayer.Name2;
        }
        else
        {
            var npcWinner = opponents.FirstOrDefault(o => o.Soberness > 0);
            if (npcWinner.Name != null)
                winnerName = npcWinner.Name;
        }

        terminal.SetColor("white");
        terminal.WriteLine($"  The contest lasted {round} rounds of {drinkName}.");
        terminal.WriteLine("");

        if (playerAlive)
        {
            // Player won!
            terminal.SetColor("bright_green");
            terminal.WriteLine("  Congratulations!");
            terminal.SetColor("white");
            terminal.WriteLine("  You managed to stay sober longer than the rest!");
            terminal.SetColor("bright_yellow");
            terminal.Write("  Three cheers for the Beer Champion! ");
            await Task.Delay(400);
            terminal.Write("...Horray! ");
            await Task.Delay(400);
            terminal.Write("...Horray! ");
            await Task.Delay(400);
            terminal.WriteLine("...Horray!");
            terminal.WriteLine("");

            // XP reward: level * 700 (from original Pascal)
            long xpReward = currentPlayer.Level * 700;
            long goldReward = 50 + currentPlayer.Level * 10;
            currentPlayer.Experience += xpReward;
            currentPlayer.Gold += goldReward;

            terminal.SetColor("bright_white");
            terminal.WriteLine($"  You receive {xpReward:N0} experience points!");
            terminal.WriteLine($"  You win {goldReward:N0} gold from the prize pot!");

            currentPlayer.Statistics?.RecordGoldChange(currentPlayer.Gold);
        }
        else
        {
            // Player lost
            terminal.SetColor("red");
            terminal.WriteLine($"  You passed out in round {playerRounds}!");
            terminal.SetColor("gray");
            terminal.WriteLine("  You wake up later with a splitting headache.");

            if (!string.IsNullOrEmpty(winnerName))
            {
                terminal.SetColor("bright_green");
                terminal.WriteLine($"  {winnerName} won the contest after {round} rounds!");
            }
            else
            {
                terminal.SetColor("yellow");
                terminal.WriteLine("  Nobody managed to stay standing! No winner was found.");
            }

            // Small consolation XP for participating
            long consolationXP = currentPlayer.Level * 100;
            currentPlayer.Experience += consolationXP;
            terminal.SetColor("gray");
            terminal.WriteLine($"  You earned {consolationXP:N0} experience for participating.");
        }

        terminal.WriteLine("");
        await terminal.PressAnyKey();
    }

    /// <summary>
    /// Get a soberness bar visual indicator
    /// </summary>
    private static string GetSobernessBar(long soberness)
    {
        int bars = (int)(soberness / 5);
        if (bars < 0) bars = 0;
        if (bars > 20) bars = 20;
        string filled = new string('#', bars);
        string empty = new string('-', 20 - bars);
        string label;
        if (soberness > 60) label = "Sober";
        else if (soberness > 40) label = "Tipsy";
        else if (soberness > 20) label = "Dizzy";
        else if (soberness > 5) label = "Wasted";
        else label = "Blind Drunk!";
        return $"[{filled}{empty}] {soberness}% - {label}";
    }

    /// <summary>
    /// Get a drunk comment based on soberness level (from original Pascal Drunk_Comment)
    /// </summary>
    private static string GetDrunkComment(long soberness)
    {
        if (soberness <= 0) return "*Blind drunk, out of competition*";
        if (soberness <= 1) return "Burp. WhheramIi?3$...???";
        if (soberness <= 4) return "Hihiii! I can see that everybody has a twin!";
        if (soberness <= 8) return "Gosh! That floor IS REALLY moving!";
        if (soberness <= 12) return "Stand still you rats! Why is the room spinning!?";
        if (soberness <= 15) return "I'm a little dizzy, that's all!";
        if (soberness <= 18) return "That beer hasn't got to me yet!";
        if (soberness <= 24) return "I'm fine, but where is the bathroom please!";
        if (soberness <= 30) return "And a happy new year to ya all! (burp..)";
        if (soberness <= 35) return "Gimme another one, Bartender!";
        if (soberness <= 40) return "Ha! I'm unbeatable!";
        if (soberness <= 50) return "Sober as a rock...";
        if (soberness <= 55) return "A clear and steady mind...";
        if (soberness <= 60) return "Refill please!";
        return "This is boriiiing... (yawn)";
    }
    
    /// <summary>
    /// Listen to rumors
    /// </summary>
    private async Task ListenToRumors()
    {
        terminal.ClearScreen();
        terminal.SetColor("bright_yellow");
        terminal.WriteLine("Tavern Gossip");
        terminal.WriteLine("=============");
        terminal.WriteLine("");

        terminal.SetColor("gray");
        terminal.WriteLine("You lean back and listen to the patrons talking...");
        terminal.WriteLine("");

        var gossip = NewsSystem.Instance?.GetRecentGossip(4) ?? new List<string>();

        if (gossip.Count > 0)
        {
            var gossipPrefixes = new[]
            {
                "\"Did you hear?",
                "\"Word around town is that",
                "\"I heard from a friend that",
                "\"Someone was saying",
                "\"You won't believe this, but",
                "\"The talk of the town is that",
                "\"Between you and me,",
            };

            foreach (var item in gossip)
            {
                terminal.SetColor("white");
                // Strip timestamp prefix [HH:mm] if present
                var text = item.TrimStart();
                if (text.Length > 7 && text[0] == '[' && text[6] == ']')
                    text = text.Substring(7).TrimStart();

                // Strip leading emoji/symbol characters for cleaner dialogue
                while (text.Length > 0 && !char.IsLetterOrDigit(text[0]) && text[0] != '"')
                    text = text.Substring(1).TrimStart();

                if (string.IsNullOrWhiteSpace(text)) continue;

                var prefix = gossipPrefixes[Random.Shared.Next(0, gossipPrefixes.Length)];
                terminal.Write($"  {prefix} ");
                terminal.SetColor("bright_white");
                // Lowercase first char for natural dialogue flow
                if (text.Length > 0 && char.IsUpper(text[0]))
                    text = char.ToLower(text[0]) + text.Substring(1);
                terminal.WriteLine($"{text}\"");
                terminal.WriteLine("");
            }
        }
        else
        {
            // Fallback to static rumors when no simulation events exist yet
            var staticRumors = new[]
            {
                "\"They say the King is planning to increase the royal guard...\"",
                "\"Word is that someone found a magical sword in the dungeons last week.\"",
                "\"The priests at the temple are worried about strange omens.\"",
                "\"A new monster has been spotted in the lower dungeon levels.\"",
                "\"The weapon shop is expecting a shipment of rare items soon.\"",
            };

            terminal.SetColor("white");
            for (int i = 0; i < 3; i++)
            {
                terminal.WriteLine($"  {staticRumors[Random.Shared.Next(0, staticRumors.Length)]}");
                terminal.WriteLine("");
            }
        }

        await terminal.PressAnyKey();
    }
    
    /// <summary>
    /// Check bulletin board
    /// </summary>
    private async Task CheckBulletinBoard()
    {
        terminal.ClearScreen();
        terminal.SetColor("bright_cyan");
        terminal.WriteLine("Inn Bulletin Board");
        terminal.WriteLine("==================");
        terminal.WriteLine("");
        
        terminal.SetColor("white");
        terminal.WriteLine("NOTICES:");
        terminal.WriteLine("- WANTED: Brave adventurers for dungeon exploration");
        terminal.WriteLine("- REWARD: 500 gold for information on the missing merchant");
        terminal.WriteLine("- WARNING: Increased bandit activity on eastern roads");
        terminal.WriteLine("- FOR SALE: Enchanted leather armor, contact Gareth");
        terminal.WriteLine("- TEAM RECRUITMENT: The Iron Wolves are seeking members");
        terminal.WriteLine("");
        
        await terminal.PressAnyKey();
    }
    
    /// <summary>
    /// Rest at table for minor healing
    /// </summary>
    private async Task RestAtTable()
    {
        terminal.WriteLine("You find a quiet corner and rest for a while...", "green");
        await Task.Delay(2000);

        // Remove Groggo's Shadow Blessing on rest (v0.41.0)
        if (currentPlayer.GroggoShadowBlessingDex > 0)
        {
            currentPlayer.Dexterity = Math.Max(1, currentPlayer.Dexterity - currentPlayer.GroggoShadowBlessingDex);
            terminal.WriteLine("The Blessing of Shadows fades as you rest...", "gray");
            currentPlayer.GroggoShadowBlessingDex = 0;
        }

        // Blood Price rest penalty — dark memories reduce rest effectiveness
        float restEfficiency = 1.0f;
        if (currentPlayer.MurderWeight >= 6f) restEfficiency = 0.50f;
        else if (currentPlayer.MurderWeight >= 3f) restEfficiency = 0.75f;

        var healing = (long)(Math.Min(10, currentPlayer.MaxHP - currentPlayer.HP) * restEfficiency);
        var manaRecovery = (long)(Math.Min(currentPlayer.MaxMana / 4, currentPlayer.MaxMana - currentPlayer.Mana) * restEfficiency);

        if (healing > 0 || manaRecovery > 0)
        {
            if (healing > 0)
            {
                currentPlayer.HP += healing;
                terminal.WriteLine($"You feel refreshed and recover {healing} HP.", "green");
            }
            if (manaRecovery > 0)
            {
                currentPlayer.Mana += manaRecovery;
                terminal.WriteLine($"Your mind clears, recovering {manaRecovery} mana.", "blue");
            }
            if (restEfficiency < 1.0f)
            {
                terminal.SetColor("dark_red");
                terminal.WriteLine("  Your rest is troubled by dark memories...");
            }
        }
        else
        {
            terminal.WriteLine("You are already at full health.", "white");
        }

        // Check for dreams during rest (nightmares take priority if MurderWeight > 0)
        var dream = DreamSystem.Instance.GetDreamForRest(currentPlayer, 0);
        if (dream != null)
        {
            await Task.Delay(1500);
            terminal.WriteLine("");
            terminal.SetColor("dark_magenta");
            terminal.WriteLine("As you doze, a dream takes shape...");
            terminal.WriteLine("");
            await Task.Delay(1500);

            terminal.SetColor("bright_magenta");
            terminal.WriteLine($"=== {dream.Title} ===");
            terminal.WriteLine("");

            terminal.SetColor("magenta");
            foreach (var line in dream.Content)
            {
                terminal.WriteLine($"  {line}");
                await Task.Delay(1200);
            }

            if (!string.IsNullOrEmpty(dream.PhilosophicalHint))
            {
                terminal.WriteLine("");
                terminal.SetColor("dark_cyan");
                terminal.WriteLine($"  ({dream.PhilosophicalHint})");
            }

            terminal.WriteLine("");
            DreamSystem.Instance.ExperienceDream(dream.Id);
            await terminal.PressAnyKey();
        }
        else
        {
            await Task.Delay(2000);
        }

        // In single-player, resting at the Inn advances the day if it's nighttime
        if (!UsurperRemake.BBS.DoorMode.IsOnlineMode)
        {
            if (DailySystemManager.CanRestForNight(currentPlayer))
            {
                terminal.WriteLine("");
                terminal.SetColor("gray");
                terminal.WriteLine("You settle in for a proper night's rest...");
                await Task.Delay(1500);
                await DailySystemManager.Instance.RestAndAdvanceToMorning(currentPlayer);
                terminal.SetColor("yellow");
                terminal.WriteLine($"A new day dawns. (Day {DailySystemManager.Instance.CurrentDay})");
                await Task.Delay(1500);
            }
            else
            {
                terminal.WriteLine("");
                terminal.SetColor("gray");
                var period = DailySystemManager.GetTimePeriodString(currentPlayer);
                terminal.WriteLine($"It's only {period}. You rest briefly but it's too early to sleep for the night.");
            }
        }
    }

    /// <summary>
    /// Order food for stamina boost
    /// </summary>
    private async Task OrderFood()
    {
        long mealBasePrice = 10;
        var (mealKingTax, mealCityTax, mealTotalWithTax) = CityControlSystem.CalculateTaxedPrice(mealBasePrice);

        if (currentPlayer.Gold < mealTotalWithTax)
        {
            terminal.WriteLine("You don't have enough gold for a meal!", "red");
            await Task.Delay(2000);
            return;
        }

        // Show tax breakdown
        CityControlSystem.Instance.DisplayTaxBreakdown(terminal, "Meal", mealBasePrice);

        currentPlayer.Gold -= mealTotalWithTax;
        CityControlSystem.Instance.ProcessSaleTax(mealBasePrice);
        
        terminal.WriteLine("You order a hearty meal of roasted meat and bread.", "green");
        terminal.WriteLine("The food fills your belly and boosts your stamina!");
        
        currentPlayer.Stamina += 5;
        var healing = Math.Min(15, currentPlayer.MaxHP - currentPlayer.HP);
        if (healing > 0)
        {
            currentPlayer.HP += healing;
            terminal.WriteLine($"You also recover {healing} HP from the nourishing meal.", "green");
        }

        await Task.Delay(2500);
    }

    /// <summary>
    /// Approach potential companions in the inn
    /// </summary>
    private async Task ApproachCompanions()
    {
        var recruitableCompanions = CompanionSystem.Instance.GetRecruitableCompanions(currentPlayer.Level).ToList();

        if (!recruitableCompanions.Any())
        {
            terminal.WriteLine("There are no strangers looking for adventuring partners right now.", "gray");
            await terminal.PressAnyKey();
            return;
        }

        terminal.ClearScreen();
        terminal.SetColor("bright_magenta");
        terminal.WriteLine("╔══════════════════════════════════════════════════════════════════════════════╗");
        terminal.WriteLine("║                        POTENTIAL COMPANIONS                                  ║");
        terminal.WriteLine("╚══════════════════════════════════════════════════════════════════════════════╝");
        terminal.WriteLine("");

        terminal.SetColor("white");
        terminal.WriteLine("In the shadowy corners of the inn, several figures seem to be watching you...");
        terminal.WriteLine("");

        int index = 1;
        foreach (var companion in recruitableCompanions)
        {
            terminal.SetColor("yellow");
            terminal.Write($"[{index}] ");
            terminal.SetColor("bright_cyan");
            terminal.Write($"{companion.Name} - {companion.Title}");
            terminal.SetColor("gray");
            terminal.WriteLine($" ({companion.CombatRole})");
            terminal.SetColor("dark_gray");
            terminal.WriteLine($"    {companion.Description.Substring(0, Math.Min(70, companion.Description.Length))}...");
            terminal.WriteLine($"    Level Req: {companion.RecruitLevel} | Trust: {companion.TrustLevel}%");
            terminal.WriteLine("");
            index++;
        }

        terminal.SetColor("bright_yellow");
        terminal.Write("[0]");
        terminal.SetColor("yellow");
        terminal.WriteLine(" Return to the bar");
        terminal.WriteLine("");

        var choice = await terminal.GetInput("Approach who? ");

        if (int.TryParse(choice, out int selection) && selection > 0 && selection <= recruitableCompanions.Count)
        {
            var selectedCompanion = recruitableCompanions[selection - 1];
            await AttemptCompanionRecruitment(selectedCompanion);
        }
    }

    /// <summary>
    /// Attempt to recruit a specific companion
    /// </summary>
    private async Task AttemptCompanionRecruitment(Companion companion)
    {
        terminal.ClearScreen();
        terminal.SetColor("bright_cyan");
        terminal.WriteLine($"You approach {companion.Name}, {companion.Title}...");
        terminal.WriteLine("");

        // Show companion's introduction from DialogueHints
        terminal.SetColor("white");
        if (companion.DialogueHints.Length > 0)
        {
            terminal.WriteLine($"\"{companion.DialogueHints[0]}\"");
        }
        else
        {
            terminal.WriteLine($"\"Greetings, traveler. You look like someone who could use help...\"");
        }
        terminal.WriteLine("");

        // Show companion details
        terminal.SetColor("gray");
        terminal.WriteLine($"Background: {companion.BackstoryBrief}");
        terminal.WriteLine("");
        terminal.SetColor("yellow");
        terminal.WriteLine($"Combat Role: {companion.CombatRole}");
        terminal.WriteLine($"Abilities: {string.Join(", ", companion.Abilities)}");
        terminal.WriteLine("");

        terminal.SetColor("bright_yellow");
        terminal.WriteLine("[R] Recruit this companion");
        terminal.WriteLine("[T] Talk more to learn about them");
        terminal.WriteLine("[0] Leave them be");
        terminal.WriteLine("");

        var choice = await terminal.GetInput("Your choice: ");

        switch (choice.ToUpper())
        {
            case "R":
                bool success = await CompanionSystem.Instance.RecruitCompanion(companion.Id, currentPlayer, terminal);
                if (success)
                {
                    terminal.SetColor("bright_green");
                    terminal.WriteLine("");
                    terminal.WriteLine($"{companion.Name} has joined you as a companion!");
                    terminal.WriteLine("They will accompany you in the dungeons and fight by your side.");
                    terminal.WriteLine("");
                    terminal.SetColor("yellow");
                    terminal.WriteLine("WARNING: Companions can die permanently. Guard them well.");
                }
                break;

            case "T":
                terminal.WriteLine("");
                terminal.SetColor("cyan");
                terminal.WriteLine($"{companion.Name} shares their story...");
                terminal.WriteLine("");
                terminal.SetColor("white");
                terminal.WriteLine(companion.BackstoryBrief);
                if (!string.IsNullOrEmpty(companion.PersonalQuestDescription))
                {
                    terminal.WriteLine("");
                    terminal.SetColor("bright_magenta");
                    terminal.WriteLine($"Personal Quest: {companion.PersonalQuestName}");
                    terminal.WriteLine($"\"{companion.PersonalQuestDescription}\"");
                }
                break;

            default:
                terminal.WriteLine($"You nod to {companion.Name} and return to the bar.", "gray");
                break;
        }

        await terminal.PressAnyKey();
    }

    #region Party Management

    /// <summary>
    /// Manage your recruited companions
    /// </summary>
    private async Task ManageParty()
    {
        var allCompanions = CompanionSystem.Instance.GetAllCompanions()
            .Where(c => c.IsRecruited && !c.IsDead).ToList();

        if (!allCompanions.Any())
        {
            terminal.WriteLine("You don't have any companions yet.", "gray");
            await terminal.PressAnyKey();
            return;
        }

        while (true)
        {
            terminal.ClearScreen();

            // Show pending notifications first
            if (CompanionSystem.Instance.HasPendingNotifications)
            {
                terminal.SetColor("bright_yellow");
                terminal.WriteLine("==============================================================================");
                terminal.WriteLine("                              NOTIFICATIONS                                   ");
                terminal.WriteLine("==============================================================================");
                terminal.WriteLine("");

                foreach (var notification in CompanionSystem.Instance.GetAndClearNotifications())
                {
                    terminal.SetColor("bright_cyan");
                    terminal.WriteLine(notification);
                    terminal.WriteLine("");
                }

                terminal.SetColor("gray");
                terminal.WriteLine("Press Enter to continue...");
                await terminal.ReadKeyAsync();
                terminal.ClearScreen();
            }

            terminal.SetColor("bright_cyan");
            terminal.WriteLine("==============================================================================");
            terminal.WriteLine("                           P A R T Y   M A N A G E M E N T                    ");
            terminal.WriteLine("==============================================================================");
            terminal.WriteLine("");

            // Show active companions
            var activeCompanions = CompanionSystem.Instance.GetActiveCompanions().ToList();
            terminal.SetColor("bright_green");
            terminal.WriteLine($"ACTIVE COMPANIONS ({activeCompanions.Count}/{CompanionSystem.MaxActiveCompanions}):");
            terminal.WriteLine("");

            if (activeCompanions.Any())
            {
                foreach (var companion in activeCompanions)
                {
                    DisplayCompanionSummary(companion, true);
                }
            }
            else
            {
                terminal.SetColor("gray");
                terminal.WriteLine("  (No active companions - select from reserves below)");
            }
            terminal.WriteLine("");

            // Show reserved companions
            var reserveCompanions = allCompanions.Where(c => !c.IsActive).ToList();
            if (reserveCompanions.Any())
            {
                terminal.SetColor("yellow");
                terminal.WriteLine("RESERVE COMPANIONS:");
                terminal.WriteLine("");
                foreach (var companion in reserveCompanions)
                {
                    DisplayCompanionSummary(companion, false);
                }
                terminal.WriteLine("");
            }

            // Show fallen companions
            var fallen = CompanionSystem.Instance.GetFallenCompanions().ToList();
            if (fallen.Any())
            {
                terminal.SetColor("dark_red");
                terminal.WriteLine("FALLEN COMPANIONS:");
                foreach (var (companion, death) in fallen)
                {
                    terminal.SetColor("gray");
                    terminal.WriteLine($"  {companion.Name} - {companion.Title}");
                    terminal.SetColor("dark_gray");
                    terminal.WriteLine($"    Died: {death.Circumstance}");
                }
                terminal.WriteLine("");
            }

            // Menu options
            terminal.SetColor("yellow");
            terminal.WriteLine("Options:");
            terminal.SetColor("white");
            int index = 1;
            foreach (var companion in allCompanions)
            {
                terminal.WriteLine($"  [{index}] Talk to {companion.Name}");
                index++;
            }
            terminal.WriteLine("");
            terminal.SetColor("bright_yellow");
            terminal.Write("  [S]");
            terminal.SetColor("cyan");
            terminal.WriteLine(" Switch active companions");
            terminal.SetColor("bright_yellow");
            terminal.Write("  [0]");
            terminal.SetColor("yellow");
            terminal.WriteLine(" Return to the bar");
            terminal.WriteLine("");

            var choice = await terminal.GetInput("Choice: ");

            if (choice == "0" || string.IsNullOrWhiteSpace(choice))
                break;

            if (choice.ToUpper() == "S")
            {
                await SwitchActiveCompanions(allCompanions);
                continue;
            }

            if (int.TryParse(choice, out int selection) && selection > 0 && selection <= allCompanions.Count)
            {
                await TalkToRecruitedCompanion(allCompanions[selection - 1]);
            }
        }
    }

    /// <summary>
    /// Display a companion's summary in the party menu
    /// </summary>
    private void DisplayCompanionSummary(Companion companion, bool isActive)
    {
        var companionSystem = CompanionSystem.Instance;
        int currentHP = companionSystem.GetCompanionHP(companion.Id);
        int maxHP = companion.BaseStats.HP;

        // Name and title
        terminal.SetColor(isActive ? "bright_white" : "white");
        terminal.Write($"  {companion.Name}");
        terminal.SetColor("gray");
        terminal.WriteLine($" - {companion.Title}");

        // Stats line
        terminal.SetColor("dark_gray");
        terminal.Write($"    Lvl {companion.Level} {companion.CombatRole} | ");

        // HP with color coding
        terminal.SetColor(currentHP > maxHP / 2 ? "green" : currentHP > maxHP / 4 ? "yellow" : "red");
        terminal.Write($"HP: {currentHP}/{maxHP}");
        terminal.SetColor("dark_gray");
        terminal.WriteLine("");

        // Loyalty and trust
        string loyaltyColor = companion.LoyaltyLevel >= 75 ? "bright_green" :
                              companion.LoyaltyLevel >= 50 ? "yellow" :
                              companion.LoyaltyLevel >= 25 ? "orange" : "red";
        terminal.SetColor("dark_gray");
        terminal.Write("    Loyalty: ");
        terminal.SetColor(loyaltyColor);
        terminal.Write($"{companion.LoyaltyLevel}%");
        terminal.SetColor("dark_gray");
        terminal.Write(" | Trust: ");
        terminal.SetColor("cyan");
        terminal.WriteLine($"{companion.TrustLevel}%");

        // Personal quest status
        if (companion.PersonalQuestCompleted)
        {
            terminal.SetColor("bright_magenta");
            terminal.WriteLine($"    Quest: {companion.PersonalQuestName} (COMPLETE)");
        }
        else if (companion.PersonalQuestStarted)
        {
            terminal.SetColor("magenta");
            terminal.WriteLine($"    Quest: {companion.PersonalQuestName} (In Progress)");
            if (!string.IsNullOrEmpty(companion.PersonalQuestLocationHint))
            {
                terminal.SetColor("gray");
                terminal.WriteLine($"      -> {companion.PersonalQuestLocationHint}");
            }
        }
        else if (companion.LoyaltyLevel >= 50 || companion.PersonalQuestAvailable)
        {
            terminal.SetColor("bright_yellow");
            terminal.WriteLine($"    Quest: {companion.PersonalQuestName} (UNLOCKED - Talk to begin!)");
        }
        else
        {
            terminal.SetColor("dark_gray");
            terminal.WriteLine($"    Quest: Build more loyalty ({companion.LoyaltyLevel}/50)");
        }

        // Romance level (if applicable)
        if (companion.RomanceAvailable && companion.RomanceLevel > 0)
        {
            terminal.SetColor("bright_magenta");
            string hearts = new string('*', Math.Min(companion.RomanceLevel, 10));
            terminal.WriteLine($"    Romance: {hearts} ({companion.RomanceLevel}/10)");
        }

        terminal.WriteLine("");
    }

    /// <summary>
    /// Switch which companions are active in dungeon
    /// </summary>
    private async Task SwitchActiveCompanions(List<Companion> allCompanions)
    {
        terminal.ClearScreen();
        terminal.SetColor("cyan");
        terminal.WriteLine("==============================================================================");
        terminal.WriteLine("                      SELECT ACTIVE COMPANIONS                                 ");
        terminal.WriteLine("==============================================================================");
        terminal.WriteLine("");

        terminal.SetColor("gray");
        terminal.WriteLine($"You can have up to {CompanionSystem.MaxActiveCompanions} companions active in the dungeon.");
        terminal.WriteLine("Active companions fight alongside you but can also be hurt or killed.");
        terminal.WriteLine("");

        terminal.SetColor("yellow");
        terminal.WriteLine("Select companions to activate (enter numbers separated by spaces):");
        terminal.WriteLine("");

        int index = 1;
        foreach (var companion in allCompanions)
        {
            bool isCurrentlyActive = companion.IsActive;
            terminal.SetColor(isCurrentlyActive ? "bright_green" : "white");
            terminal.Write($"  [{index}] {companion.Name}");
            terminal.SetColor("gray");
            terminal.Write($" ({companion.CombatRole})");
            if (isCurrentlyActive)
            {
                terminal.SetColor("bright_green");
                terminal.Write(" [ACTIVE]");
            }
            terminal.WriteLine("");
            index++;
        }

        terminal.WriteLine("");
        terminal.SetColor("yellow");
        terminal.WriteLine("Example: '1 3' to activate companions 1 and 3");
        terminal.WriteLine("Enter nothing to keep current selection");
        terminal.WriteLine("");

        var input = await terminal.GetInput("Activate: ");

        if (string.IsNullOrWhiteSpace(input))
        {
            terminal.WriteLine("No changes made.", "gray");
            await Task.Delay(1000);
            return;
        }

        // Parse selection
        var selections = input.Split(' ', StringSplitOptions.RemoveEmptyEntries);
        var selectedIds = new List<CompanionId>();

        foreach (var sel in selections)
        {
            if (int.TryParse(sel.Trim(), out int num) && num > 0 && num <= allCompanions.Count)
            {
                if (selectedIds.Count < CompanionSystem.MaxActiveCompanions)
                {
                    selectedIds.Add(allCompanions[num - 1].Id);
                }
            }
        }

        if (selectedIds.Count == 0)
        {
            terminal.WriteLine("No valid companions selected. No changes made.", "yellow");
            await Task.Delay(1500);
            return;
        }

        // Apply selection
        bool success = CompanionSystem.Instance.SetActiveCompanions(selectedIds);
        if (success)
        {
            terminal.SetColor("bright_green");
            terminal.WriteLine("");
            terminal.WriteLine("Party updated!");
            foreach (var id in selectedIds)
            {
                var c = CompanionSystem.Instance.GetCompanion(id);
                terminal.WriteLine($"  {c?.Name} is now active.");
            }
        }
        else
        {
            terminal.WriteLine("Failed to update party.", "red");
        }

        await Task.Delay(2000);
    }

    /// <summary>
    /// Have a conversation with a recruited companion
    /// </summary>
    private async Task TalkToRecruitedCompanion(Companion companion)
    {
        terminal.ClearScreen();
        terminal.SetColor("bright_cyan");
        terminal.WriteLine($"╔{'═'.ToString().PadRight(76, '═')}╗");
        terminal.WriteLine($"║  {companion.Name} - {companion.Title}".PadRight(77) + "║");
        terminal.WriteLine($"╚{'═'.ToString().PadRight(76, '═')}╝");
        terminal.WriteLine("");

        // Show full description
        terminal.SetColor("white");
        terminal.WriteLine(companion.Description);
        terminal.WriteLine("");

        // Show backstory
        terminal.SetColor("gray");
        terminal.WriteLine("Background:");
        terminal.SetColor("dark_cyan");
        terminal.WriteLine(companion.BackstoryBrief);
        terminal.WriteLine("");

        // Dialogue based on loyalty level
        terminal.SetColor("cyan");
        string dialogueHint = GetCompanionDialogue(companion);
        terminal.WriteLine($"\"{dialogueHint}\"");
        terminal.WriteLine("");

        // Show stats (with equipment bonuses if any)
        terminal.SetColor("yellow");
        terminal.WriteLine("Stats:");
        terminal.SetColor("white");
        terminal.WriteLine($"  Level: {companion.Level} | Role: {companion.CombatRole}");

        if (companion.EquippedItems.Count > 0)
        {
            // Show effective stats with equipment
            var tempChar = CreateCompanionCharacterWrapper(companion);
            terminal.Write($"  HP: {companion.BaseStats.HP}");
            if (tempChar.MaxHP != companion.BaseStats.HP)
            {
                terminal.SetColor("bright_green");
                terminal.Write($" ({tempChar.MaxHP})");
                terminal.SetColor("white");
            }
            terminal.Write($" | ATK: {companion.BaseStats.Attack}");
            if (tempChar.Strength != companion.BaseStats.Attack)
            {
                terminal.SetColor("bright_green");
                terminal.Write($" ({tempChar.Strength})");
                terminal.SetColor("white");
            }
            terminal.Write($" | DEF: {companion.BaseStats.Defense}");
            if (tempChar.Defence != companion.BaseStats.Defense)
            {
                terminal.SetColor("bright_green");
                terminal.Write($" ({tempChar.Defence})");
                terminal.SetColor("white");
            }
            terminal.WriteLine("");

            // Show equipped item count
            terminal.SetColor("gray");
            terminal.WriteLine($"  Equipment: {companion.EquippedItems.Count} item{(companion.EquippedItems.Count != 1 ? "s" : "")} equipped");
        }
        else
        {
            terminal.WriteLine($"  HP: {companion.BaseStats.HP} | ATK: {companion.BaseStats.Attack} | DEF: {companion.BaseStats.Defense}");
        }

        // Show abilities from class ability system (matches toggle menu)
        var abilityCharClass = companion.CombatRole switch
        {
            CombatRole.Tank => CharacterClass.Warrior,
            CombatRole.Healer => CharacterClass.Cleric,
            CombatRole.Damage => CharacterClass.Assassin,
            CombatRole.Hybrid => CharacterClass.Paladin,
            _ => CharacterClass.Warrior
        };
        var abilityChar = new Character { Class = abilityCharClass, Level = companion.Level };
        var companionAbilities = ClassAbilitySystem.GetAvailableAbilities(abilityChar);
        if (companionAbilities.Count > 0)
        {
            terminal.SetColor("white");
            terminal.WriteLine($"  Abilities ({companionAbilities.Count}): {string.Join(", ", companionAbilities.Select(a => a.Name))}");
        }
        else
        {
            terminal.SetColor("white");
            terminal.WriteLine($"  Abilities: {string.Join(", ", companion.Abilities)}");
        }
        terminal.WriteLine("");

        // Menu options
        terminal.SetColor("yellow");
        terminal.WriteLine("Options:");
        terminal.SetColor("white");

        // Show personal quest option if available
        if (!companion.PersonalQuestStarted && companion.LoyaltyLevel >= 50)
        {
            terminal.SetColor("bright_yellow");
            terminal.Write("  [Q]");
            terminal.SetColor("bright_magenta");
            terminal.WriteLine(" Begin Personal Quest: " + companion.PersonalQuestName);
        }
        else if (companion.PersonalQuestStarted && !companion.PersonalQuestCompleted)
        {
            terminal.SetColor("bright_yellow");
            terminal.Write("  [Q]");
            terminal.SetColor("magenta");
            terminal.WriteLine(" Discuss Quest Progress");
        }

        if (companion.RomanceAvailable)
        {
            terminal.SetColor("bright_yellow");
            terminal.Write("  [R]");
            if (companion.RomancedToday)
            {
                terminal.SetColor("dark_gray");
                terminal.WriteLine(" Deepen your bond (already visited today)");
            }
            else if (companion.RomanceLevel >= 10)
            {
                terminal.SetColor("gray");
                terminal.WriteLine(" Spend time together (Max bond)");
            }
            else
            {
                terminal.SetColor("bright_magenta");
                terminal.WriteLine(" Deepen your bond...");
            }
        }

        terminal.SetColor("bright_yellow");
        terminal.Write("  [G]");
        terminal.SetColor("white");
        terminal.WriteLine(" Give a gift");
        terminal.SetColor("bright_yellow");
        terminal.Write("  [H]");
        terminal.SetColor("white");
        terminal.WriteLine(" View history together");
        terminal.SetColor("bright_yellow");
        terminal.Write("  [E]");
        terminal.SetColor("white");
        terminal.WriteLine(" Manage Equipment");
        terminal.SetColor("bright_yellow");
        terminal.Write("  [A]");
        terminal.SetColor("white");
        terminal.WriteLine(" Manage Combat Skills");
        terminal.SetColor("bright_yellow");
        terminal.Write("  [0]");
        terminal.SetColor("yellow");
        terminal.WriteLine(" Return");
        terminal.WriteLine("");

        var choice = await terminal.GetInput("Choice: ");

        switch (choice.ToUpper())
        {
            case "Q":
                await HandlePersonalQuestInteraction(companion);
                break;
            case "R":
                if (companion.RomanceAvailable)
                    await HandleRomanceInteraction(companion);
                break;
            case "G":
                await HandleGiveGift(companion);
                break;
            case "H":
                await ShowCompanionHistory(companion);
                break;
            case "E":
                await ManageCompanionEquipment(companion);
                break;
            case "A":
                await ManageCompanionAbilities(companion);
                break;
        }
    }

    /// <summary>
    /// Get contextual dialogue based on companion's state
    /// </summary>
    private string GetCompanionDialogue(Companion companion)
    {
        // High loyalty dialogue
        if (companion.LoyaltyLevel >= 80)
        {
            return companion.Id switch
            {
                CompanionId.Lyris => "I never thought I'd find someone I could trust again. You've given me hope.",
                CompanionId.Aldric => "You remind me of what I used to fight for. It's... good to feel that again.",
                CompanionId.Mira => "With you, healing feels like it means something. Thank you for that.",
                CompanionId.Vex => "You know, for once... I'm glad I'm still here. Don't tell anyone I said that.",
                _ => "We've been through a lot together."
            };
        }
        // Medium loyalty
        else if (companion.LoyaltyLevel >= 50)
        {
            return companion.Id switch
            {
                CompanionId.Lyris => "There's something about you... like we've met before, in another life.",
                CompanionId.Aldric => "You fight well. I'm glad to have my shield at your side.",
                CompanionId.Mira => "I've been thinking about what you said. Maybe there is a reason to keep going.",
                CompanionId.Vex => "Not bad for an adventurer. Maybe I'll stick around a bit longer.",
                _ => "We're starting to understand each other."
            };
        }
        // Low loyalty - use default hints
        else if (companion.DialogueHints.Length > 0)
        {
            int hintIndex = Math.Min(companion.LoyaltyLevel / 20, companion.DialogueHints.Length - 1);
            return companion.DialogueHints[hintIndex];
        }

        return "...";
    }

    /// <summary>
    /// Handle personal quest interaction
    /// </summary>
    private async Task HandlePersonalQuestInteraction(Companion companion)
    {
        terminal.ClearScreen();
        terminal.SetColor("bright_magenta");
        terminal.WriteLine($"═══ {companion.PersonalQuestName} ═══");
        terminal.WriteLine("");

        if (!companion.PersonalQuestStarted)
        {
            // Start the quest
            terminal.SetColor("white");
            terminal.WriteLine($"{companion.Name} speaks quietly:");
            terminal.WriteLine("");
            terminal.SetColor("cyan");
            terminal.WriteLine($"\"{companion.PersonalQuestDescription}\"");
            terminal.WriteLine("");

            terminal.SetColor("bright_yellow");
            terminal.Write("[Y]");
            terminal.SetColor("yellow");
            terminal.WriteLine(" Accept this quest");
            terminal.SetColor("bright_yellow");
            terminal.Write("[N]");
            terminal.SetColor("yellow");
            terminal.WriteLine(" Not yet");
            terminal.WriteLine("");

            var choice = await terminal.GetInput("Will you help? ");

            if (choice.ToUpper() == "Y")
            {
                bool started = CompanionSystem.Instance.StartPersonalQuest(companion.Id);
                if (started)
                {
                    terminal.SetColor("bright_green");
                    terminal.WriteLine("");
                    terminal.WriteLine($"Quest Begun: {companion.PersonalQuestName}");
                    terminal.WriteLine("");
                    terminal.SetColor("white");
                    terminal.WriteLine($"{companion.Name} nods gratefully.");
                    CompanionSystem.Instance.ModifyLoyalty(companion.Id, 10, "Accepted personal quest");
                }
            }
        }
        else
        {
            // Quest in progress - show status
            terminal.SetColor("white");
            terminal.WriteLine("Quest Status: In Progress");
            terminal.WriteLine("");
            terminal.SetColor("gray");
            terminal.WriteLine($"\"{companion.PersonalQuestDescription}\"");
            terminal.WriteLine("");
            terminal.SetColor("yellow");
            terminal.WriteLine("Seek clues in the dungeon depths...");
        }

        await terminal.PressAnyKey();
    }

    /// <summary>
    /// Handle romance interaction
    /// </summary>
    private async Task HandleRomanceInteraction(Companion companion)
    {
        terminal.ClearScreen();
        terminal.SetColor("bright_magenta");
        terminal.WriteLine("═══ A Quiet Moment ═══");
        terminal.WriteLine("");

        // Already romanced today — once per day limit
        if (companion.RomancedToday)
        {
            terminal.SetColor("white");
            terminal.WriteLine($"You've already spent quality time with {companion.Name} today.");
            terminal.SetColor("gray");
            terminal.WriteLine("Perhaps tomorrow you can share another moment together.");
            await terminal.PressAnyKey();
            return;
        }

        // Mark as used for today
        companion.RomancedToday = true;

        if (companion.RomanceLevel < 1)
        {
            terminal.SetColor("white");
            terminal.WriteLine($"You and {companion.Name} find a quiet corner to talk.");
            terminal.WriteLine("The noise of the tavern fades into background murmur.");
            terminal.WriteLine("");
            terminal.SetColor("cyan");
            terminal.WriteLine($"\"{companion.DialogueHints[0]}\"");
        }
        else
        {
            string milestone = companion.RomanceLevel switch
            {
                1 => $"You share a moment of understanding with {companion.Name}.",
                2 => $"Your eyes meet, and something unspoken passes between you.",
                3 => $"{companion.Name}'s hand brushes against yours.",
                4 => "The world seems to shrink to just the two of you.",
                5 => $"{companion.Name} leans closer, voice soft.",
                _ => $"The bond between you and {companion.Name} deepens."
            };
            terminal.SetColor("white");
            terminal.WriteLine(milestone);
        }

        terminal.WriteLine("");

        // Advance romance if loyalty is high enough
        if (companion.RomanceLevel >= 10)
        {
            terminal.SetColor("bright_magenta");
            terminal.WriteLine($"Your bond with {companion.Name} is as deep as it can be.");
        }
        else if (companion.LoyaltyLevel >= 60)
        {
            // CHA-based success chance: 30% base + 1% per CHA point, cap 80%
            int charisma = (int)(currentPlayer?.Charisma ?? 10);
            int successChance = Math.Min(80, 30 + charisma);
            int roll = new Random().Next(100);

            if (roll < successChance)
            {
                bool advanced = CompanionSystem.Instance.AdvanceRomance(companion.Id, 1);
                if (advanced)
                {
                    terminal.SetColor("bright_magenta");
                    terminal.WriteLine("Your bond has grown stronger.");
                    terminal.SetColor("gray");
                    terminal.WriteLine($"  (Romance: {companion.RomanceLevel}/10)");
                }
            }
            else
            {
                terminal.SetColor("white");
                terminal.WriteLine($"You enjoy the moment, but {companion.Name} seems distracted tonight.");
                terminal.SetColor("gray");
                terminal.WriteLine("(Higher Charisma improves your chances)");
            }
        }
        else
        {
            terminal.SetColor("gray");
            terminal.WriteLine("(Build more trust before deepening this connection)");
        }

        await terminal.PressAnyKey();
    }

    /// <summary>
    /// Give a gift to a companion
    /// </summary>
    private async Task HandleGiveGift(Companion companion)
    {
        terminal.ClearScreen();
        terminal.SetColor("yellow");
        terminal.WriteLine("═══ Give a Gift ═══");
        terminal.WriteLine("");

        if (currentPlayer.Gold < 50)
        {
            terminal.WriteLine("You don't have enough gold to buy a meaningful gift. (Need 50g)", "red");
            await terminal.PressAnyKey();
            return;
        }

        terminal.SetColor("white");
        terminal.WriteLine("Gift Options:");
        terminal.WriteLine("");
        terminal.SetColor("bright_yellow");
        terminal.Write("  [1]");
        terminal.SetColor("white");
        terminal.WriteLine(" Simple Gift (50 gold) - +3 loyalty");
        terminal.SetColor("bright_yellow");
        terminal.Write("  [2]");
        terminal.SetColor("white");
        terminal.WriteLine(" Fine Gift (200 gold) - +8 loyalty");

        if (currentPlayer.Gold >= 500)
        {
            terminal.SetColor("bright_yellow");
            terminal.Write("  [3]");
            terminal.SetColor("white");
            terminal.WriteLine(" Rare Gift (500 gold) - +15 loyalty");
        }

        terminal.SetColor("bright_yellow");
        terminal.Write("  [0]");
        terminal.SetColor("white");
        terminal.WriteLine(" Cancel");
        terminal.WriteLine("");

        var choice = await terminal.GetInput("Choose: ");

        int cost = 0;
        int loyaltyGain = 0;
        string giftDesc = "";

        switch (choice)
        {
            case "1":
                cost = 50;
                loyaltyGain = 3;
                giftDesc = "a thoughtful trinket";
                break;
            case "2":
                if (currentPlayer.Gold >= 200)
                {
                    cost = 200;
                    loyaltyGain = 8;
                    giftDesc = "a fine piece of jewelry";
                }
                break;
            case "3":
                if (currentPlayer.Gold >= 500)
                {
                    cost = 500;
                    loyaltyGain = 15;
                    giftDesc = "a rare artifact";
                }
                break;
        }

        if (cost > 0 && currentPlayer.Gold >= cost)
        {
            currentPlayer.Gold -= cost;
            CompanionSystem.Instance.ModifyLoyalty(companion.Id, loyaltyGain, $"Received gift: {giftDesc}");

            terminal.SetColor("bright_green");
            terminal.WriteLine("");
            terminal.WriteLine($"You give {companion.Name} {giftDesc}.");
            terminal.WriteLine($"{companion.Name} smiles warmly. (+{loyaltyGain} loyalty)");
        }

        await terminal.PressAnyKey();
    }

    /// <summary>
    /// Show history with this companion
    /// </summary>
    private async Task ShowCompanionHistory(Companion companion)
    {
        terminal.ClearScreen();
        terminal.SetColor("cyan");
        terminal.WriteLine($"═══ History with {companion.Name} ═══");
        terminal.WriteLine("");

        if (companion.History.Count == 0)
        {
            terminal.SetColor("gray");
            terminal.WriteLine("Your journey together has just begun...");
        }
        else
        {
            terminal.SetColor("white");
            // Show last 10 events
            var recentHistory = companion.History.TakeLast(10).Reverse();
            foreach (var evt in recentHistory)
            {
                terminal.SetColor("gray");
                terminal.Write($"  {evt.Timestamp:MMM dd} - ");
                terminal.SetColor("white");
                terminal.WriteLine(evt.Description);
            }
        }

        terminal.WriteLine("");
        terminal.SetColor("yellow");
        terminal.WriteLine($"Days together: {(companion.RecruitedDay > 0 ? StoryProgressionSystem.Instance.CurrentGameDay - companion.RecruitedDay : 0)}");
        terminal.WriteLine($"Total loyalty gained: {companion.LoyaltyLevel}%");

        await terminal.PressAnyKey();
    }

    #region Companion Equipment & Abilities

    /// <summary>
    /// Create a Character wrapper from a Companion for equipment management UI
    /// </summary>
    private Character CreateCompanionCharacterWrapper(Companion companion)
    {
        var wrapper = new Character
        {
            Name2 = companion.Name,
            Level = companion.Level,
            Class = companion.CombatRole switch
            {
                CombatRole.Tank => CharacterClass.Warrior,
                CombatRole.Healer => CharacterClass.Cleric,
                CombatRole.Damage => CharacterClass.Assassin,
                CombatRole.Hybrid => CharacterClass.Paladin,
                _ => CharacterClass.Warrior
            },
            BaseStrength = companion.BaseStats.Attack,
            BaseDefence = companion.BaseStats.Defense,
            BaseDexterity = companion.BaseStats.Speed,
            BaseAgility = companion.BaseStats.Speed,
            BaseIntelligence = companion.BaseStats.MagicPower,
            BaseWisdom = companion.BaseStats.HealingPower,
            BaseCharisma = 10,
            BaseConstitution = 10 + companion.Level,
            BaseStamina = 10 + companion.Level,
            BaseMaxHP = companion.BaseStats.HP,
            BaseMaxMana = companion.BaseStats.MagicPower * 5
        };

        // Copy companion's equipment
        foreach (var kvp in companion.EquippedItems)
            wrapper.EquippedItems[kvp.Key] = kvp.Value;

        wrapper.RecalculateStats();

        // Set current HP to max for display purposes
        wrapper.HP = wrapper.MaxHP;
        wrapper.Mana = wrapper.MaxMana;

        return wrapper;
    }

    /// <summary>
    /// Manage equipment for a companion via Character wrapper
    /// </summary>
    private async Task ManageCompanionEquipment(Companion companion)
    {
        var wrapper = CreateCompanionCharacterWrapper(companion);

        await ManageCompanionCharacterEquipment(wrapper);

        // Sync equipment changes back to the companion
        companion.EquippedItems.Clear();
        foreach (var kvp in wrapper.EquippedItems)
        {
            if (kvp.Value > 0)
                companion.EquippedItems[kvp.Key] = kvp.Value;
        }

        await SaveSystem.Instance.AutoSave(currentPlayer);
    }

    /// <summary>
    /// Manage equipment for a specific character (companion wrapper)
    /// Based on TeamCornerLocation.ManageCharacterEquipment
    /// </summary>
    private async Task ManageCompanionCharacterEquipment(Character target)
    {
        while (true)
        {
            terminal.ClearScreen();
            terminal.SetColor("bright_cyan");
            terminal.WriteLine($"═══════════════════════════════════════════════════════════════════════════════");
            terminal.WriteLine($"                    EQUIPMENT: {target.DisplayName.ToUpper()}");
            terminal.WriteLine($"═══════════════════════════════════════════════════════════════════════════════");
            terminal.WriteLine("");

            // Show target's stats
            terminal.SetColor("white");
            terminal.WriteLine($"  Level: {target.Level}  Class: {target.Class}  Race: {target.Race}");
            terminal.WriteLine($"  HP: {target.HP}/{target.MaxHP}  Mana: {target.Mana}/{target.MaxMana}");
            terminal.WriteLine($"  Str: {target.Strength}  Def: {target.Defence}  Agi: {target.Agility}");
            terminal.WriteLine("");

            // Show current equipment
            terminal.SetColor("bright_yellow");
            terminal.WriteLine("Current Equipment:");
            terminal.SetColor("white");

            CompanionDisplayEquipmentSlot(target, EquipmentSlot.MainHand, "Main Hand");
            CompanionDisplayEquipmentSlot(target, EquipmentSlot.OffHand, "Off Hand");
            CompanionDisplayEquipmentSlot(target, EquipmentSlot.Head, "Head");
            CompanionDisplayEquipmentSlot(target, EquipmentSlot.Body, "Body");
            CompanionDisplayEquipmentSlot(target, EquipmentSlot.Arms, "Arms");
            CompanionDisplayEquipmentSlot(target, EquipmentSlot.Hands, "Hands");
            CompanionDisplayEquipmentSlot(target, EquipmentSlot.Legs, "Legs");
            CompanionDisplayEquipmentSlot(target, EquipmentSlot.Feet, "Feet");
            CompanionDisplayEquipmentSlot(target, EquipmentSlot.Waist, "Belt");
            CompanionDisplayEquipmentSlot(target, EquipmentSlot.Face, "Face");
            CompanionDisplayEquipmentSlot(target, EquipmentSlot.Cloak, "Cloak");
            CompanionDisplayEquipmentSlot(target, EquipmentSlot.Neck, "Neck");
            CompanionDisplayEquipmentSlot(target, EquipmentSlot.LFinger, "Left Ring");
            CompanionDisplayEquipmentSlot(target, EquipmentSlot.RFinger, "Right Ring");
            terminal.WriteLine("");

            // Show options
            terminal.SetColor("cyan");
            terminal.WriteLine("Options:");
            terminal.SetColor("darkgray");
            terminal.Write("  [");
            terminal.SetColor("bright_yellow");
            terminal.Write("E");
            terminal.SetColor("darkgray");
            terminal.Write("] ");
            terminal.SetColor("white");
            terminal.WriteLine("Equip item from your inventory");
            terminal.SetColor("darkgray");
            terminal.Write("  [");
            terminal.SetColor("bright_yellow");
            terminal.Write("U");
            terminal.SetColor("darkgray");
            terminal.Write("] ");
            terminal.SetColor("white");
            terminal.WriteLine("Unequip item from them");
            terminal.SetColor("darkgray");
            terminal.Write("  [");
            terminal.SetColor("bright_yellow");
            terminal.Write("T");
            terminal.SetColor("darkgray");
            terminal.Write("] ");
            terminal.SetColor("white");
            terminal.WriteLine("Take all their equipment");
            terminal.SetColor("darkgray");
            terminal.Write("  [");
            terminal.SetColor("bright_yellow");
            terminal.Write("Q");
            terminal.SetColor("darkgray");
            terminal.Write("] ");
            terminal.SetColor("white");
            terminal.WriteLine("Done / Return");
            terminal.WriteLine("");

            terminal.SetColor("cyan");
            terminal.Write("Choice: ");
            terminal.SetColor("white");

            var choice = (await terminal.ReadLineAsync()).ToUpper().Trim();

            switch (choice)
            {
                case "E":
                    await CompanionEquipItemToCharacter(target);
                    break;
                case "U":
                    await CompanionUnequipItemFromCharacter(target);
                    break;
                case "T":
                    await CompanionTakeAllEquipment(target);
                    break;
                case "Q":
                case "":
                    return;
            }
        }
    }

    private void CompanionDisplayEquipmentSlot(Character target, EquipmentSlot slot, string label)
    {
        var item = target.GetEquipment(slot);
        terminal.SetColor("gray");
        terminal.Write($"  {label,-12}: ");
        if (item != null)
        {
            if (!item.IsIdentified)
            {
                terminal.SetColor("magenta");
                terminal.WriteLine($"{item.Name} (unidentified)");
            }
            else
            {
                terminal.SetColor(item.GetRarityColor());
                terminal.Write(item.Name);

                // Build compact stat summary
                var stats = new List<string>();
                if (item.WeaponPower > 0) stats.Add($"Atk:{item.WeaponPower}");
                if (item.ArmorClass > 0) stats.Add($"AC:{item.ArmorClass}");
                if (item.ShieldBonus > 0) stats.Add($"Shield:{item.ShieldBonus}");
                if (item.DefenceBonus > 0) stats.Add($"Def:{item.DefenceBonus}");
                if (item.StrengthBonus != 0) stats.Add($"Str:{item.StrengthBonus:+#;-#}");
                if (item.DexterityBonus != 0) stats.Add($"Dex:{item.DexterityBonus:+#;-#}");
                if (item.AgilityBonus != 0) stats.Add($"Agi:{item.AgilityBonus:+#;-#}");
                if (item.ConstitutionBonus != 0) stats.Add($"Con:{item.ConstitutionBonus:+#;-#}");
                if (item.IntelligenceBonus != 0) stats.Add($"Int:{item.IntelligenceBonus:+#;-#}");
                if (item.WisdomBonus != 0) stats.Add($"Wis:{item.WisdomBonus:+#;-#}");
                if (item.CharismaBonus != 0) stats.Add($"Cha:{item.CharismaBonus:+#;-#}");
                if (item.MaxHPBonus > 0) stats.Add($"HP:{item.MaxHPBonus:+#}");
                if (item.MaxManaBonus > 0) stats.Add($"MP:{item.MaxManaBonus:+#}");
                if (item.CriticalChanceBonus > 0) stats.Add($"Crit:{item.CriticalChanceBonus}%");
                if (item.LifeSteal > 0) stats.Add($"Leech:{item.LifeSteal}%");
                if (item.MagicResistance > 0) stats.Add($"MRes:{item.MagicResistance}%");
                if (item.PoisonDamage > 0) stats.Add($"Psn:{item.PoisonDamage}");

                if (stats.Count > 0)
                {
                    terminal.SetColor("darkgray");
                    terminal.Write($" [{string.Join(" ", stats)}]");
                }
                terminal.WriteLine("");
            }
        }
        else
        {
            // Check if off-hand is empty because of a two-handed weapon
            if (slot == EquipmentSlot.OffHand)
            {
                var mainHand = target.GetEquipment(EquipmentSlot.MainHand);
                if (mainHand?.Handedness == WeaponHandedness.TwoHanded)
                {
                    terminal.SetColor("darkgray");
                    terminal.WriteLine("(using 2H weapon)");
                    return;
                }
            }
            terminal.SetColor("darkgray");
            terminal.WriteLine("(empty)");
        }
    }

    private async Task CompanionEquipItemToCharacter(Character target)
    {
        terminal.ClearScreen();
        terminal.SetColor("bright_cyan");
        terminal.WriteLine($"═══ EQUIP ITEM TO {target.DisplayName.ToUpper()} ═══");
        terminal.WriteLine("");

        // Collect equippable items from player's inventory and equipped items
        var equipmentItems = new List<(Equipment item, bool isEquipped, EquipmentSlot? fromSlot)>();

        // Add equippable items from player's inventory
        foreach (var invItem in currentPlayer.Inventory)
        {
            var equipment = ConvertInventoryItemToEquipment(invItem);
            if (equipment != null)
                equipmentItems.Add((equipment, false, (EquipmentSlot?)null));
        }

        // Add player's currently equipped items
        foreach (EquipmentSlot slot in Enum.GetValues(typeof(EquipmentSlot)))
        {
            if (slot == EquipmentSlot.None) continue;
            var equipped = currentPlayer.GetEquipment(slot);
            if (equipped != null)
            {
                equipmentItems.Add((equipped, true, slot));
            }
        }

        if (equipmentItems.Count == 0)
        {
            terminal.SetColor("yellow");
            terminal.WriteLine("You have no equipment to give.");
            await Task.Delay(2000);
            return;
        }

        // Display available items
        terminal.SetColor("white");
        terminal.WriteLine("Available equipment:");
        terminal.WriteLine("");

        for (int i = 0; i < equipmentItems.Count; i++)
        {
            var (item, isEquipped, fromSlot) = equipmentItems[i];
            terminal.SetColor("bright_yellow");
            terminal.Write($"  {i + 1}. ");

            if (!item.IsIdentified)
            {
                terminal.SetColor("magenta");
                terminal.Write($"{item.Name} ");
                terminal.SetColor("darkgray");
                terminal.Write("(unidentified)");
            }
            else
            {
                terminal.SetColor("white");
                terminal.Write($"{item.Name} ");

                // Show item stats
                terminal.SetColor("gray");
                if (item.WeaponPower > 0)
                    terminal.Write($"[Atk:{item.WeaponPower}] ");
                if (item.ArmorClass > 0)
                    terminal.Write($"[AC:{item.ArmorClass}] ");
                if (item.ShieldBonus > 0)
                    terminal.Write($"[Shield:{item.ShieldBonus}] ");

                // Show if currently equipped by player
                if (isEquipped)
                {
                    terminal.SetColor("cyan");
                    terminal.Write($"(your {fromSlot?.GetDisplayName()})");
                }

                // Check if target can use it
                if (!item.CanEquip(target, out string reason))
                {
                    terminal.SetColor("red");
                    terminal.Write($" [{reason}]");
                }
            }

            terminal.WriteLine("");
        }

        terminal.WriteLine("");
        terminal.SetColor("cyan");
        terminal.Write("Select item (0 to cancel): ");
        terminal.SetColor("white");

        var input = await terminal.ReadLineAsync();
        if (!int.TryParse(input, out int itemIdx) || itemIdx < 1 || itemIdx > equipmentItems.Count)
        {
            terminal.SetColor("gray");
            terminal.WriteLine("Cancelled.");
            await Task.Delay(1000);
            return;
        }

        var (selectedItem, wasEquipped, sourceSlot) = equipmentItems[itemIdx - 1];

        // Block unidentified items
        if (!selectedItem.IsIdentified)
        {
            terminal.SetColor("yellow");
            terminal.WriteLine("You must identify this item before giving it to a companion.");
            await Task.Delay(2000);
            return;
        }

        // Check if target can equip
        if (!selectedItem.CanEquip(target, out string equipReason))
        {
            terminal.SetColor("red");
            terminal.WriteLine($"{target.DisplayName} cannot use this item: {equipReason}");
            await Task.Delay(2000);
            return;
        }

        // For one-handed weapons, ask which hand
        EquipmentSlot? targetSlot = null;
        if (selectedItem.Handedness == WeaponHandedness.OneHanded &&
            (selectedItem.Slot == EquipmentSlot.MainHand || selectedItem.Slot == EquipmentSlot.OffHand))
        {
            terminal.WriteLine("");
            terminal.SetColor("cyan");
            terminal.Write("Which hand? [");
            terminal.SetColor("bright_yellow");
            terminal.Write("M");
            terminal.SetColor("cyan");
            terminal.Write("]ain hand or [");
            terminal.SetColor("bright_yellow");
            terminal.Write("O");
            terminal.SetColor("cyan");
            terminal.WriteLine("]ff hand?");
            terminal.Write(": ");
            terminal.SetColor("white");
            var handChoice = (await terminal.ReadLineAsync()).ToUpper().Trim();
            if (handChoice.StartsWith("O"))
                targetSlot = EquipmentSlot.OffHand;
            else
                targetSlot = EquipmentSlot.MainHand;
        }

        // Remove from player
        if (wasEquipped && sourceSlot.HasValue)
        {
            currentPlayer.UnequipSlot(sourceSlot.Value);
            currentPlayer.RecalculateStats();
        }
        else
        {
            // Remove from inventory (find by name)
            var invItem = currentPlayer.Inventory.FirstOrDefault(i => i.Name == selectedItem.Name);
            if (invItem != null)
            {
                currentPlayer.Inventory.Remove(invItem);
            }
        }

        // Track items in target's inventory BEFORE equipping, so we can move displaced items to player
        var targetInventoryBefore = target.Inventory.Count;

        // Equip to target - EquipItem adds displaced items to target's inventory
        var result = target.EquipItem(selectedItem, targetSlot, out string message);
        target.RecalculateStats();

        if (result)
        {
            // Move any items that were added to target's inventory (displaced equipment) to player's inventory
            if (target.Inventory.Count > targetInventoryBefore)
            {
                var displacedItems = target.Inventory.Skip(targetInventoryBefore).ToList();
                foreach (var displaced in displacedItems)
                {
                    target.Inventory.Remove(displaced);
                    currentPlayer.Inventory.Add(displaced);
                }
            }

            terminal.WriteLine("");
            terminal.SetColor("bright_green");
            terminal.WriteLine($"{target.DisplayName} equipped {selectedItem.Name}!");
            if (!string.IsNullOrEmpty(message))
            {
                terminal.SetColor("yellow");
                terminal.WriteLine(message);
            }
        }
        else
        {
            // Failed - return item to player
            var legacyItem = CompanionConvertEquipmentToItem(selectedItem);
            currentPlayer.Inventory.Add(legacyItem);
            terminal.SetColor("red");
            terminal.WriteLine($"Failed to equip: {message}");
        }

        await Task.Delay(2000);
    }

    private async Task CompanionUnequipItemFromCharacter(Character target)
    {
        terminal.ClearScreen();
        terminal.SetColor("bright_cyan");
        terminal.WriteLine($"═══ UNEQUIP FROM {target.DisplayName.ToUpper()} ═══");
        terminal.WriteLine("");

        // Get all equipped slots
        var equippedSlots = new List<(EquipmentSlot slot, Equipment item)>();
        foreach (EquipmentSlot slot in Enum.GetValues(typeof(EquipmentSlot)))
        {
            if (slot == EquipmentSlot.None) continue;
            var item = target.GetEquipment(slot);
            if (item != null)
            {
                equippedSlots.Add((slot, item));
            }
        }

        if (equippedSlots.Count == 0)
        {
            terminal.SetColor("yellow");
            terminal.WriteLine($"{target.DisplayName} has no equipment to unequip.");
            await Task.Delay(2000);
            return;
        }

        terminal.SetColor("white");
        terminal.WriteLine("Equipped items:");
        terminal.WriteLine("");

        for (int i = 0; i < equippedSlots.Count; i++)
        {
            var (slot, item) = equippedSlots[i];
            terminal.SetColor("bright_yellow");
            terminal.Write($"  {i + 1}. ");
            terminal.SetColor("gray");
            terminal.Write($"[{slot.GetDisplayName(),-12}] ");
            terminal.SetColor("white");
            terminal.Write($"{item.Name}");
            if (item.IsCursed)
            {
                terminal.SetColor("red");
                terminal.Write(" (CURSED)");
            }
            terminal.WriteLine("");
        }

        terminal.WriteLine("");
        terminal.SetColor("cyan");
        terminal.Write("Select slot to unequip (0 to cancel): ");
        terminal.SetColor("white");

        var input = await terminal.ReadLineAsync();
        if (!int.TryParse(input, out int slotIdx) || slotIdx < 1 || slotIdx > equippedSlots.Count)
        {
            terminal.SetColor("gray");
            terminal.WriteLine("Cancelled.");
            await Task.Delay(1000);
            return;
        }

        var (selectedSlot, selectedItem) = equippedSlots[slotIdx - 1];

        // Check if cursed
        if (selectedItem.IsCursed)
        {
            terminal.SetColor("red");
            terminal.WriteLine($"The {selectedItem.Name} is cursed and cannot be removed!");
            await Task.Delay(2000);
            return;
        }

        // Unequip and add to player inventory
        var unequipped = target.UnequipSlot(selectedSlot);
        if (unequipped != null)
        {
            target.RecalculateStats();
            var legacyItem = CompanionConvertEquipmentToItem(unequipped);
            currentPlayer.Inventory.Add(legacyItem);

            terminal.WriteLine("");
            terminal.SetColor("bright_green");
            terminal.WriteLine($"Took {unequipped.Name} from {target.DisplayName}.");
            terminal.SetColor("gray");
            terminal.WriteLine("Item added to your inventory.");
        }
        else
        {
            terminal.SetColor("red");
            terminal.WriteLine("Failed to unequip item.");
        }

        await Task.Delay(2000);
    }

    private async Task CompanionTakeAllEquipment(Character target)
    {
        terminal.WriteLine("");
        terminal.SetColor("yellow");
        terminal.WriteLine($"Take ALL equipment from {target.DisplayName}?");
        terminal.Write("This will leave them with nothing. Confirm (Y/N): ");
        terminal.SetColor("white");

        var confirm = await terminal.ReadLineAsync();
        if (!confirm.ToUpper().StartsWith("Y"))
        {
            terminal.SetColor("gray");
            terminal.WriteLine("Cancelled.");
            await Task.Delay(1000);
            return;
        }

        int itemsTaken = 0;
        var cursedItems = new List<string>();

        foreach (EquipmentSlot slot in Enum.GetValues(typeof(EquipmentSlot)))
        {
            if (slot == EquipmentSlot.None) continue;
            var item = target.GetEquipment(slot);
            if (item != null)
            {
                if (item.IsCursed)
                {
                    cursedItems.Add(item.Name);
                    continue;
                }

                var unequipped = target.UnequipSlot(slot);
                if (unequipped != null)
                {
                    var legacyItem = CompanionConvertEquipmentToItem(unequipped);
                    currentPlayer.Inventory.Add(legacyItem);
                    itemsTaken++;
                }
            }
        }

        target.RecalculateStats();

        terminal.WriteLine("");
        if (itemsTaken > 0)
        {
            terminal.SetColor("bright_green");
            terminal.WriteLine($"Took {itemsTaken} item{(itemsTaken != 1 ? "s" : "")} from {target.DisplayName}.");
        }
        else
        {
            terminal.SetColor("yellow");
            terminal.WriteLine($"{target.DisplayName} had no equipment to take.");
        }

        if (cursedItems.Count > 0)
        {
            terminal.SetColor("red");
            terminal.WriteLine($"Could not remove cursed items: {string.Join(", ", cursedItems)}");
        }

        await Task.Delay(2000);
    }

    private Item CompanionConvertEquipmentToItem(Equipment equipment)
    {
        return new Item
        {
            Name = equipment.Name,
            Type = CompanionSlotToObjType(equipment.Slot),
            Value = equipment.Value,
            Attack = equipment.WeaponPower,
            Armor = equipment.ArmorClass,
            Strength = equipment.StrengthBonus,
            Dexterity = equipment.DexterityBonus,
            HP = equipment.MaxHPBonus,
            Mana = equipment.MaxManaBonus,
            Defence = equipment.DefenceBonus,
            IsCursed = equipment.IsCursed,
            MinLevel = equipment.MinLevel,
            StrengthNeeded = equipment.StrengthRequired,
            RequiresGood = equipment.RequiresGood,
            RequiresEvil = equipment.RequiresEvil,
            ItemID = equipment.Id
        };
    }

    private ObjType CompanionSlotToObjType(EquipmentSlot slot) => slot switch
    {
        EquipmentSlot.Head => ObjType.Head,
        EquipmentSlot.Body => ObjType.Body,
        EquipmentSlot.Arms => ObjType.Arms,
        EquipmentSlot.Hands => ObjType.Hands,
        EquipmentSlot.Legs => ObjType.Legs,
        EquipmentSlot.Feet => ObjType.Feet,
        EquipmentSlot.MainHand => ObjType.Weapon,
        EquipmentSlot.OffHand => ObjType.Shield,
        EquipmentSlot.Neck => ObjType.Neck,
        EquipmentSlot.Neck2 => ObjType.Neck,
        EquipmentSlot.LFinger => ObjType.Fingers,
        EquipmentSlot.RFinger => ObjType.Fingers,
        EquipmentSlot.Cloak => ObjType.Abody,
        EquipmentSlot.Waist => ObjType.Waist,
        _ => ObjType.Magic
    };

    /// <summary>
    /// Manage combat abilities for a companion - toggle on/off
    /// </summary>
    private async Task ManageCompanionAbilities(Companion companion)
    {
        // Map CombatRole to CharacterClass (same as GetCompanionsAsCharacters)
        var charClass = companion.CombatRole switch
        {
            CombatRole.Tank => CharacterClass.Warrior,
            CombatRole.Healer => CharacterClass.Cleric,
            CombatRole.Damage => CharacterClass.Assassin,
            CombatRole.Hybrid => CharacterClass.Paladin,
            _ => CharacterClass.Warrior
        };

        while (true)
        {
            terminal.ClearScreen();
            terminal.SetColor("bright_cyan");
            terminal.WriteLine($"═══════════════════════════════════════════════════════════════════════════════");
            terminal.WriteLine($"                  COMBAT SKILLS: {companion.Name.ToUpper()}");
            terminal.WriteLine($"═══════════════════════════════════════════════════════════════════════════════");
            terminal.SetColor("white");
            terminal.WriteLine($"  Role: {companion.CombatRole} (as {charClass}) | Level: {companion.Level}");
            terminal.WriteLine("");

            // Get all abilities for this class at this level
            var tempChar = new Character { Class = charClass, Level = companion.Level };
            var abilities = ClassAbilitySystem.GetAvailableAbilities(tempChar);

            if (abilities.Count == 0)
            {
                terminal.SetColor("yellow");
                terminal.WriteLine("No combat abilities available yet.");
                await terminal.PressAnyKey();
                return;
            }

            int enabledCount = 0;
            for (int i = 0; i < abilities.Count; i++)
            {
                var ability = abilities[i];
                bool isDisabled = companion.DisabledAbilities.Contains(ability.Id);
                if (!isDisabled) enabledCount++;

                terminal.SetColor("bright_yellow");
                terminal.Write($"  [{i + 1,2}] ");
                terminal.SetColor(isDisabled ? "darkgray" : "bright_green");
                terminal.Write(isDisabled ? "[OFF] " : "[ON]  ");
                terminal.SetColor(isDisabled ? "gray" : "white");
                terminal.Write($"{ability.Name,-24}");
                terminal.SetColor("darkgray");
                terminal.Write($" {ability.StaminaCost,2} ST  Lv{ability.LevelRequired,-3}  ");
                terminal.SetColor(isDisabled ? "darkgray" : "gray");
                terminal.WriteLine(ability.Description.Length > 30 ? ability.Description[..30] + "..." : ability.Description);
            }

            terminal.WriteLine("");
            terminal.SetColor("gray");
            terminal.WriteLine($"  {enabledCount}/{abilities.Count} abilities enabled");
            terminal.WriteLine("");
            terminal.SetColor("yellow");
            terminal.WriteLine("  [1-N] Toggle ability  [A] Enable all  [0] Return");
            terminal.WriteLine("");

            var input = await terminal.GetInput("Choice: ");
            if (string.IsNullOrWhiteSpace(input) || input.Trim() == "0") break;

            if (input.Trim().ToUpper() == "A")
            {
                companion.DisabledAbilities.Clear();
                terminal.SetColor("bright_green");
                terminal.WriteLine("All abilities enabled!");
                await Task.Delay(800);
                continue;
            }

            if (int.TryParse(input.Trim(), out int idx) && idx >= 1 && idx <= abilities.Count)
            {
                var ability = abilities[idx - 1];
                if (companion.DisabledAbilities.Contains(ability.Id))
                {
                    companion.DisabledAbilities.Remove(ability.Id);
                    terminal.SetColor("bright_green");
                    terminal.WriteLine($"  Enabled: {ability.Name}");
                }
                else
                {
                    companion.DisabledAbilities.Add(ability.Id);
                    terminal.SetColor("red");
                    terminal.WriteLine($"  Disabled: {ability.Name}");
                }
                await Task.Delay(600);
            }
        }

        await SaveSystem.Instance.AutoSave(currentPlayer);
    }

    #endregion

    #endregion

    #region Stat Training

    private async Task HandleStatTraining()
    {
        terminal.ClearScreen();
        terminal.SetColor("bright_yellow");
        terminal.WriteLine("╔══════════════════════════════════════════════════════════════════════════════╗");
        { const string t = "THE MASTER TRAINER"; int l = (78 - t.Length) / 2, r = 78 - t.Length - l; terminal.WriteLine($"║{new string(' ', l)}{t}{new string(' ', r)}║"); }
        terminal.WriteLine("╚══════════════════════════════════════════════════════════════════════════════╝");
        terminal.WriteLine("");

        terminal.SetColor("white");
        terminal.WriteLine("A scarred veteran sits in the corner, methodically sharpening a blade.");
        terminal.WriteLine("He looks up as you approach.");
        terminal.WriteLine("");
        terminal.SetColor("bright_cyan");
        terminal.WriteLine("\"You want to get stronger? I can help — for a price.\"");
        terminal.WriteLine("\"Each session pushes your body and mind beyond its natural limits.\"");
        terminal.WriteLine("\"But the deeper we go, the harder it gets — and the more it costs.\"");
        terminal.WriteLine("");

        var statNames = new[] { "STR", "DEX", "CON", "INT", "WIS", "CHA", "AGI", "STA" };
        var statLabels = new[] { "Strength", "Dexterity", "Constitution", "Intelligence", "Wisdom", "Charisma", "Agility", "Stamina" };

        terminal.SetColor("cyan");
        terminal.WriteLine($"{"#",-4} {"Stat",-16} {"Current",-10} {"Trained",-10} {"Next Cost",-12}");
        terminal.SetColor("darkgray");
        terminal.WriteLine(new string('─', 55));

        for (int i = 0; i < statNames.Length; i++)
        {
            int timesTrained = 0;
            currentPlayer.StatTrainingCounts?.TryGetValue(statNames[i], out timesTrained);

            long currentVal = GetStatValue(statNames[i]);
            string trainedStr = $"{timesTrained}/{GameConfig.MaxStatTrainingsPerStat}";

            if (timesTrained >= GameConfig.MaxStatTrainingsPerStat)
            {
                terminal.SetColor("darkgray");
                terminal.WriteLine($"{i + 1,-4} {statLabels[i],-16} {currentVal,-10} {trainedStr,-10} {"MAXED",-12}");
            }
            else
            {
                long cost = CalculateTrainingCost(timesTrained);
                terminal.SetColor("white");
                terminal.Write($"{i + 1,-4} {statLabels[i],-16} {currentVal,-10} {trainedStr,-10} {cost:N0}g");

                // Show material requirements for 4th/5th training
                var matReqs = GetTrainingMaterialRequirements(timesTrained);
                if (matReqs != null)
                {
                    terminal.Write("  ");
                    for (int j = 0; j < matReqs.Length; j++)
                    {
                        var mat = GameConfig.GetMaterialById(matReqs[j].materialId);
                        bool has = currentPlayer.HasMaterial(matReqs[j].materialId, matReqs[j].count);
                        terminal.SetColor(has ? "bright_green" : "red");
                        terminal.Write($"{matReqs[j].count}x {mat?.Name ?? matReqs[j].materialId}");
                        if (j < matReqs.Length - 1)
                        {
                            terminal.SetColor("gray");
                            terminal.Write(" + ");
                        }
                    }
                }
                terminal.WriteLine("");
            }
        }

        terminal.WriteLine("");
        terminal.SetColor("bright_yellow");
        terminal.WriteLine($"Your Gold: {currentPlayer.Gold:N0}");
        terminal.WriteLine("");
        terminal.SetColor("cyan");
        terminal.Write("Choose stat to train (1-8, 0 to leave): ");
        terminal.SetColor("white");
        string input = await terminal.ReadLineAsync();

        if (int.TryParse(input, out int choice) && choice >= 1 && choice <= 8)
        {
            string statKey = statNames[choice - 1];
            string statLabel = statLabels[choice - 1];

            int timesTrained = 0;
            currentPlayer.StatTrainingCounts?.TryGetValue(statKey, out timesTrained);

            if (timesTrained >= GameConfig.MaxStatTrainingsPerStat)
            {
                terminal.WriteLine("");
                terminal.SetColor("bright_cyan");
                terminal.WriteLine($"\"Your {statLabel} has been pushed as far as training can take it.\"");
                terminal.WriteLine("\"You'll need to find other ways to grow stronger.\"");
                await terminal.PressAnyKey();
                return;
            }

            long cost = CalculateTrainingCost(timesTrained);

            if (currentPlayer.Gold < cost)
            {
                terminal.WriteLine("");
                terminal.SetColor("red");
                terminal.WriteLine($"You need {cost:N0} gold but only have {currentPlayer.Gold:N0}.");
                terminal.SetColor("bright_cyan");
                terminal.WriteLine("\"Come back when you can afford my services.\"");
                await terminal.PressAnyKey();
                return;
            }

            // Check material requirements for 4th/5th training
            var trainingMatReqs = GetTrainingMaterialRequirements(timesTrained);
            if (trainingMatReqs != null)
            {
                var missing = trainingMatReqs.Where(r => !currentPlayer.HasMaterial(r.materialId, r.count)).ToList();
                if (missing.Count > 0)
                {
                    terminal.WriteLine("");
                    terminal.SetColor("bright_cyan");
                    terminal.WriteLine("\"This level of training requires rare materials to push past your limits.\"");
                    foreach (var req in missing)
                    {
                        var mat = GameConfig.GetMaterialById(req.materialId);
                        terminal.SetColor("red");
                        terminal.WriteLine($"  Missing: {req.count}x {mat?.Name ?? req.materialId}");
                    }
                    terminal.SetColor("darkgray");
                    terminal.WriteLine("  These materials can be found deep in the dungeon.");
                    await terminal.PressAnyKey();
                    return;
                }
            }

            // Pay and train
            currentPlayer.Gold -= cost;
            currentPlayer.Statistics?.RecordGoldSpent(cost);

            // Consume materials
            if (trainingMatReqs != null)
            {
                foreach (var req in trainingMatReqs)
                {
                    currentPlayer.ConsumeMaterial(req.materialId, req.count);
                    var mat = GameConfig.GetMaterialById(req.materialId);
                    terminal.SetColor(mat?.Color ?? "white");
                    terminal.WriteLine($"  The {mat?.Name ?? req.materialId} dissolves into your body, fueling the transformation...");
                }
                await Task.Delay(500);
            }

            // Apply the +1 stat bonus
            ApplyStatBonus(statKey);

            // Record training
            currentPlayer.StatTrainingCounts ??= new Dictionary<string, int>();
            currentPlayer.StatTrainingCounts[statKey] = timesTrained + 1;

            terminal.WriteLine("");
            terminal.SetColor("bright_yellow");
            terminal.WriteLine($"You pay the Master Trainer {cost:N0} gold.");
            terminal.WriteLine("");

            // Training narrative
            switch (statKey)
            {
                case "STR":
                    terminal.SetColor("white");
                    terminal.WriteLine("Hours of lifting, pulling, and breaking things later...");
                    break;
                case "DEX":
                    terminal.SetColor("white");
                    terminal.WriteLine("Dodging thrown knives, catching falling coins, threading needles...");
                    break;
                case "CON":
                    terminal.SetColor("white");
                    terminal.WriteLine("Endurance runs, ice baths, and a truly terrible herbal tonic...");
                    break;
                case "INT":
                    terminal.SetColor("white");
                    terminal.WriteLine("Puzzles, strategy games, and ancient texts until your head throbs...");
                    break;
                case "WIS":
                    terminal.SetColor("white");
                    terminal.WriteLine("Meditation, perception drills, and learning to listen to silence...");
                    break;
                case "CHA":
                    terminal.SetColor("white");
                    terminal.WriteLine("Public speaking, negotiation exercises, and a very expensive haircut...");
                    break;
                case "AGI":
                    terminal.SetColor("white");
                    terminal.WriteLine("Obstacle courses, balance beams, and jumping over increasingly sharp things...");
                    break;
                case "STA":
                    terminal.SetColor("white");
                    terminal.WriteLine("Running until you collapse, then running some more...");
                    break;
            }

            await Task.Delay(1500);
            terminal.WriteLine("");
            terminal.SetColor("bright_green");
            long newVal = GetStatValue(statKey);
            terminal.WriteLine($"Your {statLabel} increased to {newVal}! (+1)");
            terminal.SetColor("bright_cyan");
            terminal.WriteLine($"\"Good work. You've got {GameConfig.MaxStatTrainingsPerStat - timesTrained - 1} more sessions available for {statLabel}.\"");
        }

        await terminal.PressAnyKey();
    }

    private long CalculateTrainingCost(int timesTrained)
    {
        long baseCost = currentPlayer.Level * GameConfig.StatTrainingBaseCostPerLevel;
        return baseCost * (long)((timesTrained + 1) * (timesTrained + 1));
    }

    /// <summary>
    /// Returns material requirements for the Nth stat training (0-indexed).
    /// 4th training (index 3) requires Heart of the Ocean.
    /// 5th training (index 4) requires Heart of the Ocean + Eye of Manwe.
    /// </summary>
    private static (string materialId, int count)[]? GetTrainingMaterialRequirements(int timesTrained)
    {
        return timesTrained switch
        {
            3 => new[] { ("heart_of_the_ocean", 1) },
            4 => new[] { ("heart_of_the_ocean", 1), ("eye_of_manwe", 1) },
            _ => null
        };
    }

    private long GetStatValue(string statKey)
    {
        return statKey switch
        {
            "STR" => currentPlayer.Strength,
            "DEX" => currentPlayer.Dexterity,
            "CON" => currentPlayer.Constitution,
            "INT" => currentPlayer.Intelligence,
            "WIS" => currentPlayer.Wisdom,
            "CHA" => currentPlayer.Charisma,
            "AGI" => currentPlayer.Agility,
            "STA" => currentPlayer.Stamina,
            _ => 0
        };
    }

    private void ApplyStatBonus(string statKey)
    {
        switch (statKey)
        {
            case "STR": currentPlayer.BaseStrength++; currentPlayer.Strength++; break;
            case "DEX": currentPlayer.BaseDexterity++; currentPlayer.Dexterity++; break;
            case "CON": currentPlayer.BaseConstitution++; currentPlayer.Constitution++; break;
            case "INT": currentPlayer.BaseIntelligence++; currentPlayer.Intelligence++; break;
            case "WIS": currentPlayer.BaseWisdom++; currentPlayer.Wisdom++; break;
            case "CHA": currentPlayer.BaseCharisma++; currentPlayer.Charisma++; break;
            case "AGI": currentPlayer.BaseAgility++; currentPlayer.Agility++; break;
            case "STA": currentPlayer.BaseStamina++; currentPlayer.Stamina++; break;
        }
    }

    #endregion

    #region Gambling Den

    private int _armWrestlesToday = 0;

    private async Task HandleGamblingDen()
    {
        terminal.ClearScreen();
        terminal.SetColor("bright_red");
        terminal.WriteLine("╔══════════════════════════════════════════════════════════════════════════════╗");
        { const string t = "GAMBLING DEN"; int l = (78 - t.Length) / 2, r = 78 - t.Length - l; terminal.WriteLine($"║{new string(' ', l)}{t}{new string(' ', r)}║"); }
        terminal.WriteLine("╚══════════════════════════════════════════════════════════════════════════════╝");
        terminal.WriteLine("");

        terminal.SetColor("white");
        terminal.WriteLine("You descend a narrow staircase into a smoky room beneath the Inn.");
        terminal.WriteLine("The clatter of dice and murmur of wagers fill the air.");
        terminal.WriteLine("");

        terminal.SetColor("bright_yellow");
        terminal.WriteLine($"Your Gold: {currentPlayer.Gold:N0}");
        terminal.WriteLine("");

        terminal.SetColor("cyan");
        terminal.WriteLine("Games Available:");
        terminal.WriteLine("");

        terminal.SetColor("darkgray");
        terminal.Write("[");
        terminal.SetColor("bright_yellow");
        terminal.Write("1");
        terminal.SetColor("darkgray");
        terminal.Write("] ");
        terminal.SetColor("white");
        terminal.WriteLine("High-Low Dice       (Guess higher or lower, 1.8x payout)");

        terminal.SetColor("darkgray");
        terminal.Write("[");
        terminal.SetColor("bright_yellow");
        terminal.Write("2");
        terminal.SetColor("darkgray");
        terminal.Write("] ");
        terminal.SetColor("white");
        terminal.WriteLine("Skull & Bones       (Blackjack with bone tiles, 2x payout)");

        terminal.SetColor("darkgray");
        terminal.Write("[");
        terminal.SetColor("bright_yellow");
        terminal.Write("3");
        terminal.SetColor("darkgray");
        terminal.Write("] ");
        terminal.SetColor("white");
        var armWrestles = DoorMode.IsOnlineMode ? currentPlayer.ArmWrestlesToday : _armWrestlesToday;
        terminal.WriteLine($"Arm Wrestling       (STR contest vs NPC, {armWrestles}/{GameConfig.MaxArmWrestlesPerDay} today)");

        terminal.WriteLine("");
        terminal.SetColor("cyan");
        terminal.Write("Choose a game (0 to leave): ");
        terminal.SetColor("white");
        string input = await terminal.ReadLineAsync();

        switch (input?.Trim())
        {
            case "1":
                await PlayHighLowDice();
                break;
            case "2":
                await PlaySkullAndBones();
                break;
            case "3":
                await PlayArmWrestling();
                break;
        }
    }

    private async Task PlayHighLowDice()
    {
        terminal.ClearScreen();
        terminal.SetColor("bright_yellow");
        terminal.WriteLine("═══════════ HIGH-LOW DICE ═══════════");
        terminal.WriteLine("");

        if (currentPlayer.Gold <= 0)
        {
            terminal.SetColor("red");
            terminal.WriteLine("You don't have any gold to wager!");
            await terminal.PressAnyKey();
            return;
        }

        terminal.SetColor("white");
        terminal.WriteLine($"Gold on hand: {currentPlayer.Gold:N0}");
        terminal.SetColor("cyan");
        terminal.Write("How much will you wager? ");
        terminal.SetColor("white");
        string betInput = await terminal.ReadLineAsync();

        if (!long.TryParse(betInput, out long bet) || bet <= 0)
        {
            terminal.SetColor("red");
            terminal.WriteLine("Invalid bet.");
            await terminal.PressAnyKey();
            return;
        }

        if (bet > currentPlayer.Gold)
        {
            terminal.SetColor("red");
            terminal.WriteLine("You don't have that much gold!");
            await terminal.PressAnyKey();
            return;
        }

        var rng = new Random();
        int doubleDownCount = 0;

        while (true)
        {
            int firstRoll = rng.Next(1, 7);
            terminal.WriteLine("");
            terminal.SetColor("bright_yellow");
            terminal.WriteLine($"The dealer rolls: [{firstRoll}]");
            terminal.WriteLine("");
            terminal.SetColor("cyan");
            terminal.Write("Will the next roll be ");
            terminal.SetColor("bright_yellow");
            terminal.Write("[H]");
            terminal.SetColor("cyan");
            terminal.Write("igher or ");
            terminal.SetColor("bright_yellow");
            terminal.Write("[L]");
            terminal.SetColor("cyan");
            terminal.Write("ower? ");
            terminal.SetColor("white");
            string guess = (await terminal.ReadLineAsync()).ToUpper().Trim();

            if (guess != "H" && guess != "L")
            {
                terminal.SetColor("red");
                terminal.WriteLine("Invalid choice. Bet forfeited.");
                currentPlayer.Gold -= bet;
                currentPlayer.Statistics?.RecordGoldSpent(bet);
                break;
            }

            int secondRoll = rng.Next(1, 7);
            terminal.SetColor("bright_yellow");
            terminal.WriteLine($"The next roll: [{secondRoll}]");
            await Task.Delay(800);

            if (secondRoll == firstRoll)
            {
                terminal.SetColor("yellow");
                terminal.WriteLine("It's a tie! Your bet is returned.");
                break;
            }

            bool guessedHigher = guess == "H";
            bool wasHigher = secondRoll > firstRoll;

            if (guessedHigher == wasHigher)
            {
                long winnings = (long)(bet * GameConfig.HighLowPayoutMultiplier) - bet;
                terminal.SetColor("bright_green");
                terminal.WriteLine($"You win! +{winnings:N0} gold!");
                doubleDownCount++;

                if (doubleDownCount < GameConfig.GamblingMaxDoubleDown)
                {
                    long totalPot = bet + winnings;
                    terminal.WriteLine("");
                    terminal.SetColor("cyan");
                    terminal.Write($"Double or nothing? Current pot: {totalPot:N0}g ");
                    terminal.SetColor("bright_yellow");
                    terminal.Write("[Y]");
                    terminal.SetColor("cyan");
                    terminal.Write("/");
                    terminal.SetColor("bright_yellow");
                    terminal.Write("[N]");
                    terminal.SetColor("cyan");
                    terminal.Write(" ");
                    terminal.SetColor("white");
                    string dd = (await terminal.ReadLineAsync()).ToUpper().Trim();

                    if (dd == "Y")
                    {
                        bet = totalPot;
                        continue;
                    }
                    else
                    {
                        currentPlayer.Gold += winnings;
                        break;
                    }
                }
                else
                {
                    currentPlayer.Gold += winnings;
                    terminal.SetColor("yellow");
                    terminal.WriteLine("Maximum double-downs reached. Collecting winnings!");
                    break;
                }
            }
            else
            {
                terminal.SetColor("red");
                terminal.WriteLine($"You lose! -{bet:N0} gold.");
                currentPlayer.Gold -= bet;
                currentPlayer.Statistics?.RecordGoldSpent(bet);
                break;
            }
        }

        terminal.WriteLine("");
        terminal.SetColor("bright_yellow");
        terminal.WriteLine($"Gold remaining: {currentPlayer.Gold:N0}");
        await terminal.PressAnyKey();
    }

    private async Task PlaySkullAndBones()
    {
        terminal.ClearScreen();
        terminal.SetColor("bright_cyan");
        terminal.WriteLine("═══════════ SKULL & BONES ═══════════");
        terminal.WriteLine("");

        if (currentPlayer.Gold <= 0)
        {
            terminal.SetColor("red");
            terminal.WriteLine("You don't have any gold to wager!");
            await terminal.PressAnyKey();
            return;
        }

        terminal.SetColor("white");
        terminal.WriteLine("Rules: Draw bone tiles to reach 21 without going over.");
        terminal.WriteLine("Face tiles (Skull, Crown, Sword) = 10. Dealer stands on 17.");
        terminal.WriteLine("");
        terminal.SetColor("white");
        terminal.WriteLine($"Gold on hand: {currentPlayer.Gold:N0}");
        terminal.SetColor("cyan");
        terminal.Write("How much will you wager? ");
        terminal.SetColor("white");
        string betInput = await terminal.ReadLineAsync();

        if (!long.TryParse(betInput, out long bet) || bet <= 0 || bet > currentPlayer.Gold)
        {
            terminal.SetColor("red");
            terminal.WriteLine(bet > currentPlayer.Gold ? "You don't have that much gold!" : "Invalid bet.");
            await terminal.PressAnyKey();
            return;
        }

        var rng = new Random();
        string[] faceTiles = { "Skull", "Crown", "Sword" };

        // Player's turn
        int playerTotal = 0;
        int playerCards = 0;
        var playerHand = new List<string>();

        int DrawTile()
        {
            int val = rng.Next(1, 11);
            if (val == 10)
            {
                string face = faceTiles[rng.Next(faceTiles.Length)];
                playerHand.Add(face);
            }
            else
            {
                playerHand.Add(val.ToString());
            }
            return val;
        }

        // Initial two tiles
        int tile1 = DrawTile();
        playerTotal += tile1;
        playerCards++;
        int tile2 = DrawTile();
        playerTotal += tile2;
        playerCards++;

        // Check for blackjack
        bool playerBlackjack = playerTotal == 21 && playerCards == 2;

        terminal.WriteLine("");
        terminal.SetColor("bright_yellow");
        terminal.WriteLine($"Your tiles: {string.Join(", ", playerHand)} = {playerTotal}");

        if (playerBlackjack)
        {
            terminal.SetColor("bright_green");
            terminal.WriteLine("SKULL & BONES! Natural 21!");
        }

        // Player draws
        while (playerTotal < 21 && !playerBlackjack)
        {
            terminal.WriteLine("");
            terminal.SetColor("bright_yellow");
            terminal.Write("[H]");
            terminal.SetColor("cyan");
            terminal.Write("it or ");
            terminal.SetColor("bright_yellow");
            terminal.Write("[S]");
            terminal.SetColor("cyan");
            terminal.Write("tand? ");
            terminal.SetColor("white");
            string action = (await terminal.ReadLineAsync()).ToUpper().Trim();

            if (action == "H")
            {
                playerHand.Clear();
                int newTile = rng.Next(1, 11);
                string tileName = newTile == 10 ? faceTiles[rng.Next(faceTiles.Length)] : newTile.ToString();
                playerTotal += newTile;
                playerCards++;
                terminal.SetColor("bright_yellow");
                terminal.WriteLine($"You drew: {tileName} ({newTile}) — Total: {playerTotal}");

                if (playerTotal > 21)
                {
                    terminal.SetColor("red");
                    terminal.WriteLine("BUST! You went over 21.");
                    currentPlayer.Gold -= bet;
                    currentPlayer.Statistics?.RecordGoldSpent(bet);
                    terminal.WriteLine($"You lose {bet:N0} gold. Remaining: {currentPlayer.Gold:N0}");
                    await terminal.PressAnyKey();
                    return;
                }
            }
            else
            {
                break;
            }
        }

        // Dealer's turn
        terminal.WriteLine("");
        terminal.SetColor("yellow");
        terminal.WriteLine("Dealer's turn...");
        await Task.Delay(800);

        int dealerTotal = 0;
        int dealerCards = 0;
        while (dealerTotal < 17)
        {
            int tile = rng.Next(1, 11);
            dealerTotal += tile;
            dealerCards++;
        }

        bool dealerBlackjack = dealerTotal == 21 && dealerCards == 2;
        terminal.SetColor("bright_yellow");
        terminal.WriteLine($"Dealer's total: {dealerTotal}");
        await Task.Delay(500);

        // Determine winner
        terminal.WriteLine("");
        if (dealerTotal > 21)
        {
            terminal.SetColor("bright_green");
            long winnings = playerBlackjack ? (long)(bet * GameConfig.BlackjackBonusPayout) - bet : bet;
            terminal.WriteLine($"Dealer busts! You win {winnings:N0} gold!");
            currentPlayer.Gold += winnings;
        }
        else if (playerBlackjack && !dealerBlackjack)
        {
            long winnings = (long)(bet * GameConfig.BlackjackBonusPayout) - bet;
            terminal.SetColor("bright_green");
            terminal.WriteLine($"Skull & Bones beats dealer! You win {winnings:N0} gold!");
            currentPlayer.Gold += winnings;
        }
        else if (playerTotal > dealerTotal)
        {
            long winnings = (long)(bet * GameConfig.BlackjackPayoutMultiplier) - bet;
            terminal.SetColor("bright_green");
            terminal.WriteLine($"You beat the dealer! You win {winnings:N0} gold!");
            currentPlayer.Gold += winnings;
        }
        else if (playerTotal == dealerTotal)
        {
            terminal.SetColor("yellow");
            terminal.WriteLine("Push! Bet returned.");
        }
        else
        {
            terminal.SetColor("red");
            terminal.WriteLine($"Dealer wins. You lose {bet:N0} gold.");
            currentPlayer.Gold -= bet;
            currentPlayer.Statistics?.RecordGoldSpent(bet);
        }

        terminal.WriteLine("");
        terminal.SetColor("bright_yellow");
        terminal.WriteLine($"Gold remaining: {currentPlayer.Gold:N0}");
        await terminal.PressAnyKey();
    }

    private async Task PlayArmWrestling()
    {
        terminal.ClearScreen();
        terminal.SetColor("bright_red");
        terminal.WriteLine("═══════════ ARM WRESTLING ═══════════");
        terminal.WriteLine("");

        var armWrestles = DoorMode.IsOnlineMode ? currentPlayer.ArmWrestlesToday : _armWrestlesToday;
        if (armWrestles >= GameConfig.MaxArmWrestlesPerDay)
        {
            terminal.SetColor("yellow");
            terminal.WriteLine("\"You've had enough for today, friend. Come back tomorrow.\"");
            await terminal.PressAnyKey();
            return;
        }

        // Find an NPC to wrestle
        var allNPCs = NPCSpawnSystem.Instance?.ActiveNPCs;
        if (allNPCs == null || allNPCs.Count == 0)
        {
            terminal.SetColor("yellow");
            terminal.WriteLine("No one seems interested in arm wrestling right now.");
            await terminal.PressAnyKey();
            return;
        }

        var rng = new Random();
        var candidates = allNPCs.Where(n => n.IsAlive && !n.IsDead).ToList();
        if (candidates.Count == 0)
        {
            terminal.SetColor("yellow");
            terminal.WriteLine("No one seems interested in arm wrestling right now.");
            await terminal.PressAnyKey();
            return;
        }

        var opponent = candidates[rng.Next(candidates.Count)];
        long wagerAmount = opponent.Level * GameConfig.ArmWrestleBetPerLevel;

        terminal.SetColor("white");
        terminal.WriteLine($"{opponent.DisplayName} (Level {opponent.Level}, STR {opponent.Strength}) slams their");
        terminal.WriteLine("elbow on the table and grins at you.");
        terminal.WriteLine("");
        terminal.SetColor("bright_cyan");
        terminal.WriteLine($"\"{wagerAmount:N0} gold says I can put you down.\"");
        terminal.WriteLine("");

        if (currentPlayer.Gold < wagerAmount)
        {
            terminal.SetColor("red");
            terminal.WriteLine($"You need {wagerAmount:N0} gold to accept the challenge.");
            await terminal.PressAnyKey();
            return;
        }

        terminal.SetColor("cyan");
        terminal.Write($"Accept the challenge? ({wagerAmount:N0}g) ");
        terminal.SetColor("bright_yellow");
        terminal.Write("[Y]");
        terminal.SetColor("cyan");
        terminal.Write("/");
        terminal.SetColor("bright_yellow");
        terminal.Write("[N]");
        terminal.SetColor("cyan");
        terminal.Write(" ");
        terminal.SetColor("white");
        string accept = (await terminal.ReadLineAsync()).ToUpper().Trim();

        if (accept != "Y")
        {
            terminal.SetColor("yellow");
            terminal.WriteLine("You back away from the table.");
            await terminal.PressAnyKey();
            return;
        }

        if (DoorMode.IsOnlineMode)
            currentPlayer.ArmWrestlesToday++;
        else
            _armWrestlesToday++;

        terminal.WriteLine("");
        terminal.SetColor("white");
        terminal.WriteLine("You clasp hands...");
        await Task.Delay(1000);
        terminal.WriteLine("Three... two... one... GO!");
        await Task.Delay(800);

        // STR contest with randomness
        double playerScore = currentPlayer.Strength * (0.7 + rng.NextDouble() * 0.6);
        double npcScore = opponent.Strength * (0.7 + rng.NextDouble() * 0.6);

        terminal.WriteLine("");
        if (playerScore > npcScore)
        {
            terminal.SetColor("bright_green");
            terminal.WriteLine($"You slam {opponent.DisplayName}'s arm to the table!");
            terminal.WriteLine($"You win {wagerAmount:N0} gold!");
            currentPlayer.Gold += wagerAmount;

            // Positive impression
            if (opponent.Brain?.Memory != null && currentPlayer.Name2 != null)
            {
                var impr = opponent.Brain.Memory.CharacterImpressions;
                impr[currentPlayer.Name2] = (impr.TryGetValue(currentPlayer.Name2, out float c1) ? c1 : 0f) + 0.1f;
            }
        }
        else if (playerScore < npcScore)
        {
            terminal.SetColor("red");
            terminal.WriteLine($"{opponent.DisplayName} forces your arm down with a grunt!");
            terminal.WriteLine($"You lose {wagerAmount:N0} gold.");
            currentPlayer.Gold -= wagerAmount;
            currentPlayer.Statistics?.RecordGoldSpent(wagerAmount);

            // Slightly negative impression
            if (opponent.Brain?.Memory != null && currentPlayer.Name2 != null)
            {
                var impr = opponent.Brain.Memory.CharacterImpressions;
                impr[currentPlayer.Name2] = (impr.TryGetValue(currentPlayer.Name2, out float c2) ? c2 : 0f) - 0.1f;
            }
        }
        else
        {
            terminal.SetColor("yellow");
            terminal.WriteLine("Neither of you can budge! It's a draw.");
        }

        terminal.WriteLine("");
        terminal.SetColor("bright_yellow");
        terminal.WriteLine($"Gold remaining: {currentPlayer.Gold:N0}");
        terminal.SetColor("darkgray");
        var armWrestlesDone = DoorMode.IsOnlineMode ? currentPlayer.ArmWrestlesToday : _armWrestlesToday;
        terminal.WriteLine($"Arm wrestling matches today: {armWrestlesDone}/{GameConfig.MaxArmWrestlesPerDay}");
        await terminal.PressAnyKey();
    }

    #endregion

    #region Rent a Room (Online Mode)

    private static readonly (string type, string name, int baseCost, int baseHp)[] GuardOptions = new[]
    {
        ("rookie_npc",  "Rookie Guard",  GameConfig.GuardRookieBaseCost,  80),
        ("veteran_npc", "Veteran Guard", GameConfig.GuardVeteranBaseCost, 150),
        ("elite_npc",   "Elite Guard",   GameConfig.GuardEliteBaseCost,   250),
        ("hound",       "Guard Hound",   GameConfig.GuardHoundBaseCost,   60),
        ("troll",       "Guard Troll",   GameConfig.GuardTrollBaseCost,   200),
        ("drake",       "Guard Drake",   GameConfig.GuardDrakeBaseCost,   300),
    };

    private async Task RentRoom()
    {
        long roomCost = (long)(currentPlayer.Level * GameConfig.InnRoomCostPerLevel);
        long totalAvailable = currentPlayer.Gold + currentPlayer.BankGold;
        if (totalAvailable < roomCost)
        {
            terminal.WriteLine($"You need {roomCost:N0} gold for a room. You have {currentPlayer.Gold:N0} on hand and {currentPlayer.BankGold:N0} in the bank.", "red");
            await Task.Delay(1500);
            return;
        }

        terminal.ClearScreen();
        terminal.SetColor("bright_cyan");
        terminal.WriteLine("╔══════════════════════════════════╗");
        terminal.WriteLine("║        Rent a Private Room       ║");
        terminal.WriteLine("╚══════════════════════════════════╝");
        terminal.SetColor("white");
        terminal.WriteLine($"\n  Room cost: {roomCost:N0} gold");
        terminal.WriteLine("  You will be healed fully and logged out safely.");
        terminal.SetColor("green");
        terminal.WriteLine("  Sleeping at the Inn grants +50% ATK/DEF if you're attacked.\n");

        // Guard hiring loop
        var hiredGuards = new List<(string type, string name, int hp)>();
        float levelMultiplier = 1.0f + currentPlayer.Level / 10.0f;
        long totalGuardCost = 0;

        while (hiredGuards.Count < GameConfig.MaxSleepGuards)
        {
            terminal.SetColor("yellow");
            terminal.WriteLine($"  Guards hired: {hiredGuards.Count}/{GameConfig.MaxSleepGuards}");
            if (hiredGuards.Count > 0)
            {
                terminal.SetColor("cyan");
                foreach (var g in hiredGuards)
                    terminal.WriteLine($"    - {g.name} (HP: {g.hp})");
            }
            terminal.WriteLine("");

            terminal.SetColor("white");
            for (int i = 0; i < GuardOptions.Length; i++)
            {
                var opt = GuardOptions[i];
                int cost = GetGuardCost(opt.baseCost, levelMultiplier, hiredGuards.Count);
                int hp = (int)(opt.baseHp * levelMultiplier);
                bool canAfford = currentPlayer.Gold + currentPlayer.BankGold - roomCost - totalGuardCost >= cost;
                terminal.SetColor(canAfford ? "white" : "dark_red");
                terminal.WriteLine($"  [{i + 1}] {opt.name,-16} {cost,6:N0}g  (HP: {hp})");
            }
            terminal.SetColor("bright_yellow");
            terminal.Write("  [D]");
            terminal.SetColor("bright_green");
            terminal.WriteLine(" Done hiring");
            terminal.SetColor("white");
            terminal.WriteLine($"\n  Your gold: {currentPlayer.Gold:N0} (bank: {currentPlayer.BankGold:N0})  |  Room: {roomCost:N0}  |  Guards: {totalGuardCost:N0}  |  Total: {roomCost + totalGuardCost:N0}");

            var input = await terminal.GetInput("\n  Choice: ");
            if (string.IsNullOrWhiteSpace(input) || input.Trim().ToUpper() == "D")
                break;

            if (int.TryParse(input.Trim(), out int guardIdx) && guardIdx >= 1 && guardIdx <= GuardOptions.Length)
            {
                var chosen = GuardOptions[guardIdx - 1];
                int cost = GetGuardCost(chosen.baseCost, levelMultiplier, hiredGuards.Count);
                int hp = (int)(chosen.baseHp * levelMultiplier);

                if (currentPlayer.Gold + currentPlayer.BankGold - roomCost - totalGuardCost < cost)
                {
                    terminal.WriteLine("  You can't afford that guard.", "red");
                    await Task.Delay(1000);
                    continue;
                }

                totalGuardCost += cost;
                hiredGuards.Add((chosen.type, chosen.name, hp));
                terminal.WriteLine($"  Hired {chosen.name}! (HP: {hp})", "green");
                await Task.Delay(500);
            }
            terminal.ClearScreen();
            terminal.SetColor("bright_cyan");
            terminal.WriteLine("╔══════════════════════════════════╗");
            terminal.WriteLine("║        Rent a Private Room       ║");
            terminal.WriteLine("╚══════════════════════════════════╝");
            terminal.SetColor("white");
        }

        // Confirm total cost
        long totalCost = roomCost + totalGuardCost;
        terminal.SetColor("yellow");
        terminal.WriteLine($"\n  Total cost: {totalCost:N0} gold (Room: {roomCost:N0} + Guards: {totalGuardCost:N0})");
        var confirm = await terminal.GetInput("  Rent this room and log out? (y/N): ");
        if (!confirm.Equals("Y", StringComparison.OrdinalIgnoreCase))
        {
            terminal.WriteLine("  You decide not to rent a room.", "gray");
            await Task.Delay(1000);
            return;
        }

        if (currentPlayer.Gold + currentPlayer.BankGold < totalCost)
        {
            terminal.WriteLine("  You can't afford this!", "red");
            await Task.Delay(1500);
            return;
        }

        // Pay from gold on hand first, then bank
        if (currentPlayer.Gold >= totalCost)
        {
            currentPlayer.Gold -= totalCost;
        }
        else
        {
            long shortfall = totalCost - currentPlayer.Gold;
            currentPlayer.Gold = 0;
            currentPlayer.BankGold -= shortfall;
            terminal.WriteLine($"  ({shortfall:N0}g withdrawn from your bank account)", "gray");
        }

        // Remove Groggo's Shadow Blessing on rest (v0.41.0)
        if (currentPlayer.GroggoShadowBlessingDex > 0)
        {
            currentPlayer.Dexterity = Math.Max(1, currentPlayer.Dexterity - currentPlayer.GroggoShadowBlessingDex);
            terminal.WriteLine("  The Blessing of Shadows fades as you rest...", "gray");
            currentPlayer.GroggoShadowBlessingDex = 0;
        }

        // Restore HP/Mana/Stamina
        currentPlayer.HP = currentPlayer.MaxHP;
        currentPlayer.Mana = currentPlayer.MaxMana;
        currentPlayer.Stamina = Math.Max(currentPlayer.Stamina, currentPlayer.Constitution * 2);

        if (!UsurperRemake.BBS.DoorMode.IsOnlineMode)
        {
            await DailySystemManager.Instance.ForceDailyReset();
        }

        // Save game
        await GameEngine.Instance.SaveCurrentGame();

        // Build guards JSON
        var guardsJson = "[]";
        if (hiredGuards.Count > 0)
        {
            var guardsList = hiredGuards.Select(g => new { type = g.type, hp = g.hp, maxHp = g.hp }).ToList();
            guardsJson = System.Text.Json.JsonSerializer.Serialize(guardsList);
        }

        // Register as sleeping at the Inn (protected)
        var backend = SaveSystem.Instance.Backend as SqlSaveBackend;
        if (backend != null)
        {
            var username = UsurperRemake.BBS.DoorMode.OnlineUsername ?? currentPlayer.Name2;
            await backend.RegisterSleepingPlayer(username, "inn", guardsJson, 1);
        }

        terminal.ClearScreen();
        terminal.SetColor("bright_green");
        terminal.WriteLine("\n  The innkeeper shows you to a private room upstairs.");
        terminal.WriteLine("  You lock the heavy door and collapse into a real bed.");
        if (hiredGuards.Count > 0)
        {
            terminal.SetColor("cyan");
            terminal.WriteLine($"  Your {hiredGuards.Count} guard{(hiredGuards.Count > 1 ? "s take" : " takes")} position outside your door.");
        }
        terminal.SetColor("gray");
        terminal.WriteLine("\n  You drift into a deep, protected sleep... (logging out)");
        await Task.Delay(2000);

        throw new LocationExitException(GameLocation.NoWhere);
    }

    private static int GetGuardCost(int baseCost, float levelMultiplier, int guardsAlreadyHired)
    {
        return (int)(baseCost * levelMultiplier * (1.0f + GameConfig.GuardCostMultiplierPerExtra * guardsAlreadyHired));
    }

    private async Task AttackInnSleeper()
    {
        var backend = SaveSystem.Instance.Backend as SqlSaveBackend;
        if (backend == null)
        {
            terminal.WriteLine("Not available.", "gray");
            await Task.Delay(1000);
            return;
        }

        // Gather targets: sleeping NPCs at inn + offline players at inn
        var sleepingNPCNames = WorldSimulator.GetSleepingNPCsAt("inn");
        var offlineSleepers = await backend.GetSleepingPlayers();
        var innPlayerSleepers = offlineSleepers
            .Where(s => s.SleepLocation == "inn" && !s.IsDead)
            .Where(s => !s.Username.Equals(DoorMode.OnlineUsername ?? "", StringComparison.OrdinalIgnoreCase))
            .ToList();

        if (sleepingNPCNames.Count == 0 && innPlayerSleepers.Count == 0)
        {
            terminal.WriteLine("No vulnerable sleepers at the Inn.", "gray");
            await Task.Delay(1500);
            return;
        }

        terminal.ClearScreen();
        terminal.SetColor("bright_red");
        terminal.WriteLine("Inn — Sleeping Guests");
        terminal.WriteLine("");

        // Skip NPCs on the player's team or spouse/lover
        // Level filter: can only attack sleepers within ±5 levels
        string playerTeam = currentPlayer?.Team ?? "";
        string playerName = currentPlayer?.Name2 ?? currentPlayer?.Name1 ?? "";
        int attackerLevel = currentPlayer?.Level ?? 1;
        var targets = new List<(string name, bool isNPC)>();
        foreach (var npcName in sleepingNPCNames)
        {
            var npc = NPCSpawnSystem.Instance.GetNPCByName(npcName);
            if (npc != null && !string.IsNullOrEmpty(playerTeam) &&
                playerTeam.Equals(npc.Team, StringComparison.OrdinalIgnoreCase))
                continue;
            if (npc != null && (npc.SpouseName.Equals(playerName, StringComparison.OrdinalIgnoreCase)
                || RelationshipSystem.IsMarriedOrLover(npcName, playerName)))
                continue;
            if (npc != null && Math.Abs(npc.Level - attackerLevel) > 5)
                continue;
            string lvlStr = npc != null ? $" (Lvl {npc.Level})" : "";
            terminal.WriteLine($"  {targets.Count + 1}. {npcName}{lvlStr} [SLEEPING NPC]", "yellow");
            targets.Add((npcName, true));
        }
        foreach (var s in innPlayerSleepers)
        {
            // Level filter: can only attack players within ±5 levels
            var targetSave = await backend.ReadGameData(s.Username);
            int targetLevel = targetSave?.Player?.Level ?? 1;
            if (Math.Abs(targetLevel - attackerLevel) > 5)
                continue;

            int guardCount = 0;
            try { guardCount = JsonSerializer.Deserialize<List<object>>(s.GuardsJson)?.Count ?? 0; } catch { }
            string guardLabel = guardCount > 0 ? $" [{guardCount} guard{(guardCount != 1 ? "s" : "")}]" : "";
            terminal.WriteLine($"  {targets.Count + 1}. {s.Username} (Lvl {targetLevel}){guardLabel} [SLEEPING PLAYER]", "red");
            targets.Add((s.Username, false));
        }

        terminal.SetColor("white");
        var input = await terminal.GetInput("\nWho do you attack? (number or name, blank to cancel): ");
        if (string.IsNullOrWhiteSpace(input)) return;

        (string name, bool isNPC) chosen = default;
        if (int.TryParse(input, out int idx) && idx >= 1 && idx <= targets.Count)
            chosen = targets[idx - 1];
        else
        {
            var match = targets.FirstOrDefault(t => t.name.Equals(input, StringComparison.OrdinalIgnoreCase));
            if (match.name != null)
                chosen = match;
        }

        if (chosen.name == null)
        {
            terminal.WriteLine("No such sleeper.", "red");
            await Task.Delay(1000);
            return;
        }

        if (chosen.isNPC)
            await AttackInnSleepingNPC(chosen.name);
        else
            await AttackInnSleepingPlayer(backend, chosen.name);
    }

    private async Task AttackInnSleepingNPC(string npcName)
    {
        var npc = NPCSpawnSystem.Instance.GetNPCByName(npcName);
        if (npc == null || !npc.IsAlive || npc.IsDead)
        {
            terminal.WriteLine("They are no longer here.", "gray");
            await Task.Delay(1000);
            return;
        }

        terminal.ClearScreen();
        terminal.SetColor("bright_red");
        terminal.WriteLine($"\n  You pick the lock to {npcName}'s room at the Inn...\n");
        await Task.Delay(1500);

        currentPlayer.Darkness += 30; // Extra darkness for invading inn

        // Inn NPCs fight with defense boost (+50% STR/DEF, better rested)
        long origStr = npc.Strength;
        long origDef = npc.Defence;
        npc.Strength = (long)(npc.Strength * (1.0 + GameConfig.InnDefenseBoost));
        npc.Defence = (long)(npc.Defence * (1.0 + GameConfig.InnDefenseBoost));

        var combatEngine = new CombatEngine(terminal);
        var result = await combatEngine.PlayerVsPlayer(currentPlayer, npc);

        // Restore NPC stats
        npc.Strength = origStr;
        npc.Defence = origDef;

        if (result.Outcome == CombatOutcome.Victory)
        {
            long stolenGold = (long)(npc.Gold * GameConfig.SleeperGoldTheftPercent);
            if (stolenGold > 0)
            {
                currentPlayer.Gold += stolenGold;
                npc.Gold -= stolenGold;
                terminal.WriteLine($"You rifle through their belongings and steal {stolenGold:N0} gold!", "yellow");
            }

            terminal.SetColor("dark_red");
            terminal.WriteLine($"\nYou leave {npcName}'s body in their room.");

            // Record murder memory
            npc.Memory?.RecordEvent(new MemoryEvent
            {
                Type = MemoryType.Murdered,
                Description = $"Murdered in my sleep at the Inn by {currentPlayer.Name2}",
                InvolvedCharacter = currentPlayer.Name2,
                Importance = 1.0f,
                EmotionalImpact = -1.0f,
                Location = "Inn"
            });

            // Faction standing penalty — worse at the inn (civilized place)
            if (npc.NPCFaction.HasValue)
            {
                var factionSystem = UsurperRemake.Systems.FactionSystem.Instance;
                factionSystem?.ModifyReputation(npc.NPCFaction.Value, -250);
                terminal.SetColor("red");
                terminal.WriteLine($"Your standing with {UsurperRemake.Systems.FactionSystem.Factions[npc.NPCFaction.Value].Name} has plummeted! (-250)");
            }

            // Witness memories for NPCs at the Inn
            foreach (var witness in LocationManager.Instance.GetNPCsInLocation(GameLocation.TheInn)
                .Where(n => n.IsAlive && n.Name2 != npcName))
            {
                witness.Memory?.RecordEvent(new MemoryEvent
                {
                    Type = MemoryType.SawDeath,
                    Description = $"Witnessed {currentPlayer.Name2} murder {npcName} at the Inn",
                    InvolvedCharacter = currentPlayer.Name2,
                    Importance = 0.8f,
                    EmotionalImpact = -0.6f,
                    Location = "Inn"
                });
            }

            WorldSimulator.WakeUpNPC(npcName);

            try { OnlineStateManager.Instance?.AddNews($"{currentPlayer.Name2} murdered {npcName} in their sleep at the Inn!", "combat"); } catch { }

            await Task.Delay(2000);
        }
        else
        {
            terminal.SetColor("cyan");
            terminal.WriteLine($"{npcName} fought you off — the Inn's thick walls muffled the struggle!");
            WorldSimulator.WakeUpNPC(npcName);
            await Task.Delay(2000);
        }
        await terminal.WaitForKeyPress();
    }

    private async Task AttackInnSleepingPlayer(SqlSaveBackend backend, string targetUsername)
    {
        var rng = new Random();
        var target = (await backend.GetSleepingPlayers())
            .FirstOrDefault(s => s.Username.Equals(targetUsername, StringComparison.OrdinalIgnoreCase));
        if (target == null) return;

        var victimSave = await backend.ReadGameData(target.Username);
        if (victimSave?.Player == null)
        {
            terminal.WriteLine("Could not load their data.", "red");
            await Task.Delay(1000);
            return;
        }

        terminal.ClearScreen();
        terminal.SetColor("bright_red");
        terminal.WriteLine($"\n  You sneak toward {target.Username}'s room at the Inn...\n");
        await Task.Delay(1500);

        // Fight through guards
        bool guardsRepelled = false;
        var guards = new List<(string type, string name, int hp, int maxHp)>();
        try
        {
            var guardArray = JsonNode.Parse(target.GuardsJson) as JsonArray;
            if (guardArray != null)
            {
                foreach (var g in guardArray)
                {
                    if (g == null) continue;
                    string gType = g["type"]?.GetValue<string>() ?? "rookie_npc";
                    string gName = g["name"]?.GetValue<string>() ?? "Guard";
                    int gHp = g["hp"]?.GetValue<int>() ?? 50;
                    int gMaxHp = g["max_hp"]?.GetValue<int>() ?? gHp;
                    guards.Add((gType, gName, gHp, gMaxHp));
                }
            }
        }
        catch { }

        int victimLevel = victimSave.Player.Level;

        for (int gi = 0; gi < guards.Count; gi++)
        {
            var (gType, gName, gHp, gMaxHp) = guards[gi];
            terminal.SetColor("yellow");
            terminal.WriteLine($"\n  A {gName} blocks your path!");
            await Task.Delay(1000);

            var guardChar = HeadlessCombatResolver.CreateGuardCharacter(gType, gHp, victimLevel, rng);
            var guardCombat = new CombatEngine(terminal);
            var guardResult = await guardCombat.PlayerVsPlayer(currentPlayer, guardChar);

            if (guardResult.Outcome == CombatOutcome.Victory)
            {
                terminal.SetColor("green");
                terminal.WriteLine($"  You cut down the {gName}!");
                guards.RemoveAt(gi);
                gi--;
                await Task.Delay(1000);
            }
            else
            {
                terminal.SetColor("red");
                terminal.WriteLine($"  The {gName} drives you back! Attack failed!");
                int remainingHp = (int)Math.Max(1, guardChar.HP);
                guards[gi] = (gType, gName, remainingHp, gMaxHp);
                guardsRepelled = true;
                await Task.Delay(2000);
                break;
            }
        }

        // Update guards in DB
        var updatedGuards = guards.Select(g => new { type = g.type, name = g.name, hp = g.hp, max_hp = g.maxHp });
        await backend.UpdateSleeperGuards(target.Username, JsonSerializer.Serialize(updatedGuards));

        if (guardsRepelled)
        {
            var failLog = JsonSerializer.Serialize(new
            {
                attacker = currentPlayer.Name2,
                type = "player",
                result = "guards_repelled"
            });
            await backend.AppendSleepAttackLog(target.Username, failLog);
            await terminal.WaitForKeyPress();
            return;
        }

        // Guards defeated — fight the sleeper with inn defense boost
        var victim = PlayerCharacterLoader.CreateFromSaveData(victimSave.Player, target.Username);
        long victimGold = victim.Gold;
        victim.Gold = 0;

        if (target.InnDefenseBoost)
        {
            victim.Strength = (long)(victim.Strength * (1.0 + GameConfig.InnDefenseBoost));
            victim.Defence = (long)(victim.Defence * (1.0 + GameConfig.InnDefenseBoost));
        }

        terminal.SetColor("bright_red");
        terminal.WriteLine($"\n  You reach {target.Username}...\n");
        await Task.Delay(1000);

        currentPlayer.Darkness += 30;

        var combatEngine = new CombatEngine(terminal);
        var result = await combatEngine.PlayerVsPlayer(currentPlayer, victim);

        if (result.Outcome == CombatOutcome.Victory)
        {
            long stolenGold = (long)(victimGold * GameConfig.SleeperGoldTheftPercent);
            if (stolenGold > 0)
            {
                currentPlayer.Gold += stolenGold;
                await backend.DeductGoldFromPlayer(target.Username, stolenGold);
                terminal.WriteLine($"You rifle through their belongings and steal {stolenGold:N0} gold!", "yellow");
            }

            string stolenItemName = await StealRandomItem(backend, target.Username, victimSave);
            if (stolenItemName != null)
                terminal.WriteLine($"You also take their {stolenItemName}!", "yellow");

            long xpLoss = (long)(victimSave.Player.Experience * GameConfig.SleeperXPLossPercent / 100.0);
            if (xpLoss > 0)
                await DeductXPFromPlayer(backend, target.Username, xpLoss);

            await backend.MarkSleepingPlayerDead(target.Username);

            var logEntry = JsonSerializer.Serialize(new
            {
                attacker = currentPlayer.Name2,
                type = "player",
                result = "attacker_won",
                gold_stolen = stolenGold,
                item_stolen = stolenItemName ?? (object)null!,
                xp_lost = xpLoss
            });
            await backend.AppendSleepAttackLog(target.Username, logEntry);

            await backend.SendMessage(currentPlayer.Name2, target.Username, "sleep_attack",
                $"{currentPlayer.Name2} broke into your Inn room and murdered you! They stole {stolenGold:N0} gold{(stolenItemName != null ? $" and your {stolenItemName}" : "")}.");

            terminal.SetColor("dark_red");
            terminal.WriteLine($"\nYou leave {target.Username}'s body in their room.");
            await Task.Delay(2000);
        }
        else
        {
            terminal.SetColor("cyan");
            terminal.WriteLine($"{target.Username} fought you off even in their sleep!");
            await Task.Delay(2000);
        }
        await terminal.WaitForKeyPress();
    }

    private async Task<string?> StealRandomItem(SqlSaveBackend backend, string username, SaveGameData saveData)
    {
        var rng = new Random();
        try
        {
            var playerData = saveData.Player;
            if (playerData == null) return null;

            var stealable = new List<(int index, string name)>();
            if (playerData.DynamicEquipment != null)
            {
                for (int i = 0; i < playerData.DynamicEquipment.Count; i++)
                {
                    var eq = playerData.DynamicEquipment[i];
                    if (eq != null && !string.IsNullOrEmpty(eq.Name))
                        stealable.Add((i, eq.Name));
                }
            }

            if (stealable.Count == 0) return null;

            var (index, name) = stealable[rng.Next(stealable.Count)];
            var stolenEquip = playerData.DynamicEquipment![index];

            if (playerData.EquippedItems != null)
            {
                var slotToRemove = playerData.EquippedItems
                    .Where(kvp => kvp.Value == stolenEquip.Id)
                    .Select(kvp => kvp.Key)
                    .FirstOrDefault(-1);
                if (slotToRemove >= 0)
                    playerData.EquippedItems.Remove(slotToRemove);
            }

            playerData.DynamicEquipment.RemoveAt(index);
            await backend.WriteGameData(username, saveData);
            return name;
        }
        catch { return null; }
    }

    private async Task DeductXPFromPlayer(SqlSaveBackend backend, string username, long xpLoss)
    {
        try
        {
            var saveData = await backend.ReadGameData(username);
            if (saveData?.Player != null)
            {
                saveData.Player.Experience = Math.Max(0, saveData.Player.Experience - xpLoss);
                await backend.WriteGameData(username, saveData);
            }
        }
        catch { }
    }

    #endregion
}
