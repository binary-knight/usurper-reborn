using UsurperRemake.Utils;
using UsurperRemake.Systems;
using Godot;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;

/// <summary>
/// The Inn location - social hub with Seth Able, drinking, and team activities
/// Based on Pascal INN.PAS and INNC.PAS
/// </summary>
public class InnLocation : BaseLocation
{
    private NPC sethAble;
    private bool sethAbleAvailable = true;
    private int sethFightsToday = 0;     // Daily fight counter - max 3 per day
    private int sethDefeatsTotal = 0;    // Total times player has beaten Seth this session
    private int lastSethFightDay = -1;   // Track which game day the fights counter is for
    
    public InnLocation() : base(
        GameLocation.TheInn,
        "The Inn",
        "You enter the smoky tavern. The air is thick with the smell of ale and the sound of rowdy conversation."
    ) { }
    
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
        terminal.WriteLine("║                          TROUBLE AT THE INN!                                  ║");
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
        terminal.ClearScreen();
        
        // Inn header - standardized format
        terminal.SetColor("bright_cyan");
        terminal.WriteLine("╔═════════════════════════════════════════════════════════════════════════════╗");
        terminal.SetColor("bright_yellow");
        terminal.WriteLine("║                         THE INN - 'The Drunken Dragon'                      ║");
        terminal.SetColor("bright_cyan");
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
        terminal.SetColor("bright_cyan");
        terminal.Write("T");
        terminal.SetColor("darkgray");
        terminal.Write("] ");
        terminal.SetColor("white");
        terminal.WriteLine("Talk to patrons");

        // Row 2
        terminal.SetColor("darkgray");
        terminal.Write("[");
        terminal.SetColor("bright_red");
        terminal.Write("F");
        terminal.SetColor("darkgray");
        terminal.Write("] ");
        terminal.SetColor("white");
        terminal.Write("Challenge Seth Able       ");

        terminal.SetColor("darkgray");
        terminal.Write("[");
        terminal.SetColor("bright_magenta");
        terminal.Write("G");
        terminal.SetColor("darkgray");
        terminal.Write("] ");
        terminal.SetColor("white");
        terminal.WriteLine("Play drinking game");

        // Row 3
        terminal.SetColor("darkgray");
        terminal.Write("[");
        terminal.SetColor("cyan");
        terminal.Write("R");
        terminal.SetColor("darkgray");
        terminal.Write("] ");
        terminal.SetColor("white");
        terminal.Write("Listen to gossip          ");

        terminal.SetColor("darkgray");
        terminal.Write("[");
        terminal.SetColor("yellow");
        terminal.Write("B");
        terminal.SetColor("darkgray");
        terminal.Write("] ");
        terminal.SetColor("white");
        terminal.WriteLine("Check bulletin board");

        // Row 4
        terminal.SetColor("darkgray");
        terminal.Write("[");
        terminal.SetColor("green");
        terminal.Write("E");
        terminal.SetColor("darkgray");
        terminal.Write("] ");
        terminal.SetColor("white");
        terminal.Write("Rest at table             ");

        terminal.SetColor("darkgray");
        terminal.Write("[");
        terminal.SetColor("bright_green");
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
        terminal.SetColor("bright_cyan");
        terminal.Write("C");
        terminal.SetColor("darkgray");
        terminal.Write("] ");
        terminal.SetColor("white");
        terminal.WriteLine("Team Corner");


        // Show companion option if available
        if (recruitableCompanions.Any())
        {
            terminal.SetColor("darkgray");
            terminal.Write("[");
            terminal.SetColor("bright_magenta");
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
            terminal.SetColor("bright_cyan");
            terminal.Write("P");
            terminal.SetColor("darkgray");
            terminal.Write("] ");
            terminal.SetColor("bright_cyan");
            terminal.WriteLine($"Manage your party ({recruitedCompanions.Count} companions)");
        }
        terminal.WriteLine("");

        terminal.SetColor("yellow");
        terminal.WriteLine("Navigation:");

        terminal.SetColor("darkgray");
        terminal.Write("[");
        terminal.SetColor("bright_red");
        terminal.Write("Q");
        terminal.SetColor("darkgray");
        terminal.Write("] ");
        terminal.SetColor("red");
        terminal.Write("Return to Main Street    ");

        terminal.SetColor("darkgray");
        terminal.Write("[");
        terminal.SetColor("cyan");
        terminal.Write("S");
        terminal.SetColor("darkgray");
        terminal.Write("] ");
        terminal.SetColor("white");
        terminal.Write("Status    ");

        terminal.SetColor("darkgray");
        terminal.Write("[");
        terminal.SetColor("yellow");
        terminal.Write("?");
        terminal.SetColor("darkgray");
        terminal.Write("] ");
        terminal.SetColor("white");
        terminal.WriteLine("Help");
        terminal.WriteLine("");
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
                
            case "R":
                await ListenToRumors();
                return false;
                
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
        var effect = GD.RandRange(1, 4);
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
        if (sethFightsToday >= 3)
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
        terminal.WriteLine($"Seth Able: \"{responses[GD.RandRange(0, responses.Length - 1)]}\"");
        terminal.WriteLine("");

        terminal.SetColor("white");
        terminal.WriteLine("WARNING: Seth Able is a dangerous opponent!");
        terminal.WriteLine($"Seth Able - Level {sethLevel} - HP: {GetSethHP(sethLevel)}");
        terminal.WriteLine($"You - Level {currentPlayer.Level} - HP: {currentPlayer.HP}/{currentPlayer.MaxHP}");
        if (sethFightsToday > 0)
        {
            terminal.SetColor("gray");
            terminal.WriteLine($"(Fights today: {sethFightsToday}/3)");
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
        terminal.SetColor("yellow");
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

            terminal.SetColor("white");
            terminal.WriteLine("[T] Talk - Have a deep conversation (flirt, confess, romance)");
            terminal.WriteLine("[C] Challenge - Challenge to a duel");
            terminal.WriteLine("[G] Gift - Give a gift (costs 50 gold)");

            terminal.WriteLine("");
            terminal.SetColor("gray");
            terminal.WriteLine("[0] Return");
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
            .OrderBy(_ => GD.Randf())
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
            var line = howdyLines[GD.RandRange(0, howdyLines.Length - 1)];
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
        terminal.SetColor("bright_white");
        terminal.Write("  [A] ");
        terminal.SetColor("yellow");
        terminal.WriteLine("Ale            - Easy going, more rounds to survive");
        terminal.SetColor("bright_white");
        terminal.Write("  [S] ");
        terminal.SetColor("yellow");
        terminal.WriteLine("Stout          - A solid choice for serious drinkers");
        terminal.SetColor("bright_white");
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
                terminal.SetColor("white");
                terminal.WriteLine("  [D] Down your drink!");
                terminal.WriteLine("  [Q] Try to bow out gracefully");
                terminal.WriteLine("");

                var action = (await terminal.GetInput("  What do you do? ")).Trim().ToUpperInvariant();

                if (action == "Q")
                {
                    // CON check to bow out without embarrassment
                    int bowOutChance = 30 + (int)(currentPlayer.Constitution / 2);
                    if (bowOutChance > 80) bowOutChance = 80;
                    if (GD.RandRange(1, 100) <= bowOutChance)
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
                long reduction = GD.RandRange(1, 22 + drinkStrength);
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

                long oppReduction = GD.RandRange(1, 22 + drinkStrength);
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

                var prefix = gossipPrefixes[GD.RandRange(0, gossipPrefixes.Length - 1)];
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
                terminal.WriteLine($"  {staticRumors[GD.RandRange(0, staticRumors.Length - 1)]}");
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

        var healing = Math.Min(10, currentPlayer.MaxHP - currentPlayer.HP);
        if (healing > 0)
        {
            currentPlayer.HP += healing;
            terminal.WriteLine($"You feel refreshed and recover {healing} HP.", "green");
        }
        else
        {
            terminal.WriteLine("You are already at full health.", "white");
        }

        // Check for dreams during rest
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

        terminal.SetColor("yellow");
        terminal.WriteLine("[0] Return to the bar");
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
            terminal.SetColor("cyan");
            terminal.WriteLine("  [S] Switch active companions");
            terminal.SetColor("yellow");
            terminal.WriteLine("  [0] Return to the bar");
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

        // Show stats
        terminal.SetColor("yellow");
        terminal.WriteLine("Stats:");
        terminal.SetColor("white");
        terminal.WriteLine($"  Level: {companion.Level} | Role: {companion.CombatRole}");
        terminal.WriteLine($"  HP: {companion.BaseStats.HP} | ATK: {companion.BaseStats.Attack} | DEF: {companion.BaseStats.Defense}");
        terminal.WriteLine($"  Abilities: {string.Join(", ", companion.Abilities)}");
        terminal.WriteLine("");

        // Menu options
        terminal.SetColor("yellow");
        terminal.WriteLine("Options:");
        terminal.SetColor("white");

        // Show personal quest option if available
        if (!companion.PersonalQuestStarted && companion.LoyaltyLevel >= 50)
        {
            terminal.SetColor("bright_magenta");
            terminal.WriteLine("  [Q] Begin Personal Quest: " + companion.PersonalQuestName);
        }
        else if (companion.PersonalQuestStarted && !companion.PersonalQuestCompleted)
        {
            terminal.SetColor("magenta");
            terminal.WriteLine("  [Q] Discuss Quest Progress");
        }

        if (companion.RomanceAvailable)
        {
            terminal.SetColor("bright_magenta");
            terminal.WriteLine("  [R] Deepen your bond...");
        }

        terminal.SetColor("white");
        terminal.WriteLine("  [G] Give a gift");
        terminal.WriteLine("  [H] View history together");
        terminal.SetColor("yellow");
        terminal.WriteLine("  [0] Return");
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

            terminal.SetColor("yellow");
            terminal.WriteLine("[Y] Accept this quest");
            terminal.WriteLine("[N] Not yet");
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
        if (companion.LoyaltyLevel >= 60)
        {
            bool advanced = CompanionSystem.Instance.AdvanceRomance(companion.Id, 1);
            if (advanced)
            {
                terminal.SetColor("bright_magenta");
                terminal.WriteLine("Your bond has grown stronger.");
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
        terminal.WriteLine("  [1] Simple Gift (50 gold) - +3 loyalty");
        terminal.WriteLine("  [2] Fine Gift (200 gold) - +8 loyalty");

        if (currentPlayer.Gold >= 500)
        {
            terminal.WriteLine("  [3] Rare Gift (500 gold) - +15 loyalty");
        }

        terminal.WriteLine("  [0] Cancel");
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

    #endregion
} 
