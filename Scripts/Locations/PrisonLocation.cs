using UsurperRemake.Utils;
using UsurperRemake.Systems;
using System.Collections.Generic;
using System.Threading.Tasks;
using System.Linq;
using System;

/// <summary>
/// Prison Location - The prisoner's perspective inside the Royal Prison
/// Based on PRISONC.PAS from the original Usurper Pascal implementation
/// Provides escape attempts, prisoner communication, and royal justice system
/// </summary>
public partial class PrisonLocation : BaseLocation
{
    private readonly GameEngine gameEngine;
    private new readonly TerminalEmulator terminal;
    private bool refreshMenu = true;
    
    public PrisonLocation(GameEngine engine, TerminalEmulator term) : base("prison")
    {
        gameEngine = engine;
        terminal = term;
        SetLocationProperties();
    }
    
    // Add parameterless constructor for compatibility
    public PrisonLocation() : base("prison")
    {
        gameEngine = GameEngine.Instance;
        terminal = GameEngine.Instance.Terminal;
        SetLocationProperties();
    }
    
    private void SetLocationProperties()
    {
        LocationId = GameLocation.Prison;
        LocationName = GameConfig.DefaultPrisonName;
        LocationDescription = "You are locked in a cold, damp prison cell";
        AllowedClasses = new HashSet<CharacterClass>(); // All classes allowed
        LevelRequirement = 1;
        
        // Add all character classes to allowed set
        foreach (CharacterClass charClass in System.Enum.GetValues<CharacterClass>())
        {
            AllowedClasses.Add(charClass);
        }
    }
    
    /// <summary>
    /// Override base EnterLocation to handle prison-specific logic
    /// </summary>
    public override async Task EnterLocation(Character player, TerminalEmulator term)
    {
        if (player == null) return;

        // Check if player is actually imprisoned
        if (player.DaysInPrison <= 0)
        {
            await terminal.WriteLineAsync("You are not imprisoned! Returning to Main Street.");
            await Task.Delay(1000);
            // Navigate player to Main Street properly
            throw new LocationExitException(GameLocation.MainStreet);
        }

        refreshMenu = true;
        await ShowPrisonInterface(player);
    }
    
    private async Task ShowPrisonInterface(Character player)
    {
        char choice = '?';
        bool exitPrison = false;

        while (!exitPrison)
        {
            // Check if sentence is served (days ran out)
            if (player.DaysInPrison <= 0)
            {
                await terminal.WriteLineAsync();
                await terminal.WriteColorLineAsync("The guards open your cell door.", TerminalEmulator.ColorGreen);
                await terminal.WriteLineAsync("\"Your sentence has been served. You are free to go.\"");
                await terminal.WriteColorLineAsync("You are FREE!", TerminalEmulator.ColorGreen);
                player.CellDoorOpen = false;
                player.RescuedBy = "";
                player.HP = Math.Max(player.HP, player.MaxHP / 2); // Restore some health

                // Reduce Darkness for serving time — prevents arrest loop
                long darknessReduction = Math.Min(player.Darkness, 75);
                if (darknessReduction > 0)
                {
                    player.Darkness -= darknessReduction;
                    await terminal.WriteColorLineAsync($"Your time served has reduced your dark reputation. (-{darknessReduction} Darkness)", "bright_green");
                }
                await Task.Delay(1500);
                throw new LocationExitException(GameLocation.MainStreet);
            }

            // Update location status if needed
            await UpdatePrisonStatus(player);

            // Check if player can walk out (cell door open by rescue)
            if (await CanOpenCellDoor(player))
            {
                await HandleCellDoorOpen(player);
                throw new LocationExitException(GameLocation.MainStreet);
            }

            // Show who else is here if enabled
            if (ShouldShowOthersHere(player))
            {
                await ShowOthersHere(player);
            }

            // Display menu
            await DisplayPrisonMenu(player, true, true);

            // Get user input
            choice = await terminal.GetCharAsync();
            choice = char.ToUpper(choice);

            // Process user choice - returns true if player escaped/freed
            exitPrison = await ProcessPrisonChoice(player, choice);
        }
    }
    
    private Task UpdatePrisonStatus(Character player)
    {
        // This would typically update the online player location
        // For now, just ensure the location is set correctly
        refreshMenu = true;
        return Task.CompletedTask;
    }
    
    private async Task<bool> CanOpenCellDoor(Character player)
    {
        // In Pascal, this checks if onliner.location == onloc_prisonerop
        // For this implementation, we'll check if player has been rescued
        // This would be set by another player breaking them out
        await Task.CompletedTask;
        return player.CellDoorOpen;
    }
    
    private async Task HandleCellDoorOpen(Character player)
    {
        await terminal.WriteLineAsync();
        await terminal.WriteColorLineAsync("The cell door swings open!", TerminalEmulator.ColorGreen);
        await terminal.WriteLineAsync();

        if (!string.IsNullOrEmpty(player.RescuedBy))
        {
            await terminal.WriteColorAsync(player.RescuedBy, TerminalEmulator.ColorCyan);
            await terminal.WriteLineAsync(" broke you out of prison!");
            await terminal.WriteLineAsync("You owe them your freedom!");
        }
        else
        {
            await terminal.WriteLineAsync("Someone has unlocked your cell!");
        }

        await terminal.WriteLineAsync();
        await terminal.WriteLineAsync("You walk out of your cell.");
        await terminal.WriteColorLineAsync("You are FREE!", TerminalEmulator.ColorGreen);

        // Reset player state
        player.HP = player.MaxHP;
        player.DaysInPrison = 0;
        player.CellDoorOpen = false;
        player.RescuedBy = "";

        // Return to dormitory
        await Task.Delay(GameConfig.PrisonCellOpenDelay);
    }
    
    private bool ShouldShowOthersHere(Character player)
    {
        // In Pascal: if player.ear = global_ear_all
        // For now, always show others
        return true;
    }
    
    private Task ShowOthersHere(Character player)
    {
        // TODO: Implement showing other players in prison
        // This would list other online prisoners
        return Task.CompletedTask;
    }
    
    private async Task DisplayPrisonMenu(Character player, bool force, bool isShort)
    {
        if (isShort)
        {
            if (!player.Expert)
            {
                if (refreshMenu)
                {
                    refreshMenu = false;
                    await ShowPrisonMenuFull();
                }
                
                await terminal.WriteLineAsync();
                await terminal.WriteAsync("Royal Prison (");
                await terminal.WriteColorAsync("?", TerminalEmulator.ColorYellow);
                await terminal.WriteAsync(" for menu) :");
            }
            else
            {
                await terminal.WriteLineAsync();
                await terminal.WriteAsync("Royal Prison (W,M,N,D,O,S,E,A,Q,?) :");
            }
        }
        else
        {
            if (!player.Expert || force)
            {
                await ShowPrisonMenuFull();
            }
        }
    }
    
    private async Task ShowPrisonMenuFull()
    {
        await terminal.ClearScreenAsync();
        await terminal.WriteLineAsync();
        
        // Prison header
        await terminal.WriteColorLineAsync("IIIIIIIIIIIIIIIIIIIIIIII", TerminalEmulator.ColorCyan);
        await terminal.WriteColorLineAsync("III The Royal Prison III", TerminalEmulator.ColorCyan);
        await terminal.WriteColorLineAsync("IIIIIIIIIIIIIIIIIIIIIIII", TerminalEmulator.ColorCyan);
        await terminal.WriteLineAsync();
        
        // Prison atmosphere description
        await terminal.WriteLineAsync("You wake up cold and aching.");
        await terminal.WriteLineAsync("Horrifying screams from the torture-chamber nearby make You");
        await terminal.WriteLineAsync("shudder with fear.");
        await terminal.WriteLineAsync("The Sheriff and his henchmen can be heard chatting in the");
        await terminal.WriteLineAsync("corridor outside.");
        await terminal.WriteLineAsync();
        
        // Menu options
        await terminal.WriteLineAsync("(W)ho else is here          (D)emand to be released!");
        await terminal.WriteLineAsync("(O)pen cell door            (E)scape!");
        await terminal.WriteLineAsync("(S)tatus                    (A)ctivities - Exercise!");

        // Check for Vex companion availability - get player from game engine
        var currentPlayer = gameEngine?.CurrentPlayer;
        if (currentPlayer != null && CanMeetVex(currentPlayer))
        {
            await terminal.WriteColorAsync("(V)", TerminalEmulator.ColorYellow);
            await terminal.WriteColorLineAsync("oice in the darkness     (A peculiar prisoner whispers...)", TerminalEmulator.ColorCyan);
        }

        await terminal.WriteLineAsync("(Q)uit");
    }

    /// <summary>
    /// Check if Vex can be encountered in prison
    /// </summary>
    private bool CanMeetVex(Character player)
    {
        var companionSystem = UsurperRemake.Systems.CompanionSystem.Instance;
        var vex = companionSystem.GetCompanion(UsurperRemake.Systems.CompanionId.Vex);

        if (vex == null || vex.IsRecruited || vex.IsDead)
            return false;

        if (player.Level < vex.RecruitLevel)
            return false;

        var story = StoryProgressionSystem.Instance;
        if (story.HasStoryFlag("vex_prison_encounter_complete"))
            return false;

        return true;
    }
    
    private async Task<bool> ProcessPrisonChoice(Character player, char choice)
    {
        switch (choice)
        {
            case '?':
                await HandleMenuDisplay(player);
                return false;
            case 'S':
                await HandleStatusDisplay(player);
                return false;
            case 'Q':
                return await HandleQuitConfirmation(player);
            case 'O':
                await HandleOpenCellDoor(player);
                return false;
            case 'D':
                await HandleDemandRelease(player);
                return false;
            case 'E':
                return await HandleEscapeAttempt(player);
            case 'W':
                await HandleListPrisoners(player);
                return false;
            case 'A':
                await HandleActivities(player);
                return false;
            case 'V':
                return await HandleVexEncounter(player);
            default:
                // Invalid choice, do nothing
                return false;
        }
    }

    /// <summary>
    /// Handle prison activity selection - allows prisoners to build stats
    /// </summary>
    private async Task HandleActivities(Character player)
    {
        await terminal.ClearScreenAsync();
        await terminal.WriteColorLineAsync("═══════════════════════════════════════", TerminalEmulator.ColorCyan);
        await terminal.WriteColorLineAsync("           PRISON ACTIVITIES           ", TerminalEmulator.ColorCyan);
        await terminal.WriteColorLineAsync("═══════════════════════════════════════", TerminalEmulator.ColorCyan);
        await terminal.WriteLineAsync();
        await terminal.WriteLineAsync("While imprisoned, you can pass the time");
        await terminal.WriteLineAsync("with activities that improve your body and mind.");
        await terminal.WriteLineAsync();

        var activities = PrisonActivitySystem.Instance.GetAvailableActivities();
        int i = 1;
        foreach (var activity in activities)
        {
            var info = PrisonActivitySystem.ActivityInfo[activity];
            await terminal.WriteColorAsync($"({i}) ", TerminalEmulator.ColorYellow);
            await terminal.WriteAsync($"{info.Name,-15} ");
            await terminal.WriteColorAsync($"{info.Effect}", TerminalEmulator.ColorGreen);
            await terminal.WriteLineAsync();
            await terminal.WriteColorLineAsync($"    {info.Description}", TerminalEmulator.ColorDarkGray);
            i++;
        }

        await terminal.WriteLineAsync();
        await terminal.WriteAsync("Choose activity (0 to cancel): ");
        string input = await terminal.ReadLineAsync();

        if (int.TryParse(input, out int choice) && choice >= 1 && choice <= activities.Count)
        {
            var selectedActivity = activities[choice - 1];
            string result = await PrisonActivitySystem.Instance.PerformActivity(player, selectedActivity);

            await terminal.WriteLineAsync();
            await terminal.WriteColorLineAsync(result, TerminalEmulator.ColorGreen);
            await terminal.WriteLineAsync();
            await terminal.WriteAsync("Press Enter to continue...");
            await terminal.GetCharAsync();
        }

        refreshMenu = true;
    }
    
    private async Task HandleMenuDisplay(Character player)
    {
        if (player.Expert)
            await DisplayPrisonMenu(player, true, false);
        else
            await DisplayPrisonMenu(player, false, false);
    }
    
    private async Task HandleStatusDisplay(Character player)
    {
        await ShowCharacterStatus(player);
    }
    
    private async Task ShowCharacterStatus(Character player)
    {
        await terminal.WriteLineAsync();
        await terminal.WriteLineAsync("=== PRISONER STATUS ===");
        await terminal.WriteLineAsync($"Name: {player.DisplayName}");
        await terminal.WriteLineAsync($"Level: {player.Level}");
        await terminal.WriteLineAsync($"Health: {player.HP}/{player.MaxHP}");
        await terminal.WriteLineAsync($"Days remaining in prison: {player.DaysInPrison}");
        await terminal.WriteLineAsync($"Escape attempts left: {player.PrisonEscapes}");
        
        if (player.DaysInPrison == 1)
            await terminal.WriteLineAsync("You will likely be released tomorrow.");
        else
            await terminal.WriteLineAsync($"You have {player.DaysInPrison} days left to serve.");
            
        await terminal.WriteLineAsync();
        await terminal.WriteAsync("Press Enter to continue...");
        await terminal.GetCharAsync();
    }
    
    private async Task<bool> HandleQuitConfirmation(Character player)
    {
        await terminal.WriteLineAsync();
        await terminal.WriteLineAsync();

        bool confirmed = await terminal.ConfirmAsync("QUIT game and rest for the night", false);
        if (!confirmed)
        {
            // Don't quit, continue prison loop
            return false;
        }

        // Player is logging out - display sleep message
        await terminal.WriteLineAsync();
        await terminal.WriteLineAsync("You cover yourself with some hay and try to get some sleep.");
        await terminal.WriteLineAsync("It will be a long and cold night with the rats...");
        await terminal.WriteLineAsync();
        await Task.Delay(1500);

        // Save and quit - throw game exit exception
        throw new GameExitException("Player logging out from prison");
    }
    
    private async Task HandleOpenCellDoor(Character player)
    {
        await terminal.WriteLineAsync();
        
        // Check if cell door can be opened (player was rescued)
        if (await CanOpenCellDoor(player))
        {
            await HandleCellDoorOpen(player);
        }
        else
        {
            await terminal.WriteLineAsync();
            await terminal.WriteColorLineAsync("You try to open the Iron door, but it's impossible.", TerminalEmulator.ColorRed);
            await terminal.WriteColorLineAsync("You are trapped in here! Perhaps you should try to escape.", TerminalEmulator.ColorRed);
            await Task.Delay(1000);
        }
    }
    
    private async Task HandleDemandRelease(Character player)
    {
        await terminal.WriteLineAsync();
        await terminal.WriteLineAsync();
        await terminal.WriteColorAsync("You clear your throat : ", TerminalEmulator.ColorWhite);
        await terminal.WriteColorLineAsync("Let me out of here please....!", TerminalEmulator.ColorCyan);
        
        await Task.Delay(GameConfig.PrisonGuardResponseDelay);
        await terminal.WriteLineAsync();
        await terminal.WriteLineAsync();
        await terminal.WriteLineAsync("After a moment you hear a dark voice cry out :");
        
        // Random guard response (Pascal: case random(5))
        var random = new System.Random();
        string response = random.Next(5) switch
        {
            0 => GameConfig.PrisonDemandResponse1,
            1 => GameConfig.PrisonDemandResponse2,
            2 => GameConfig.PrisonDemandResponse3,
            3 => GameConfig.PrisonDemandResponse4,
            _ => GameConfig.PrisonDemandResponse5
        };
        
        await terminal.WriteColorLineAsync(response, TerminalEmulator.ColorMagenta);
        await terminal.WriteLineAsync("(You will probably be released tomorrow)");
    }
    
    private async Task<bool> HandleEscapeAttempt(Character player)
    {
        await terminal.WriteLineAsync();

        if (player.PrisonEscapes < 1)
        {
            await terminal.WriteLineAsync();
            await terminal.WriteColorLineAsync("You have no escape attempts left! Try again tomorrow.", TerminalEmulator.ColorRed);
            await Task.Delay(1000);
            return false;
        }

        await terminal.WriteLineAsync();
        bool confirmed = await terminal.ConfirmAsync("Jail-Break", true);

        if (!confirmed)
        {
            return false;
        }

        // Use escape attempt
        player.PrisonEscapes--;

        await terminal.WriteLineAsync();
        await Task.Delay(GameConfig.PrisonEscapeDelay);

        // Escape chance based on dexterity and level (better than 50/50)
        var random = new System.Random();
        int escapeChance = 40 + (int)(player.Dexterity / 3) + (player.Level / 2);
        escapeChance = Math.Clamp(escapeChance, 30, 80); // 30-80% chance
        bool success = random.Next(100) < escapeChance;

        if (!success)
        {
            await terminal.WriteColorLineAsync("You failed!", TerminalEmulator.ColorRed);

            // Generate news about failed escape
            NewsSystem.Instance.Newsy(true, $"{player.DisplayName} failed to escape from the Royal Prison!");

            await terminal.WriteLineAsync("The guards heard your escape attempt!");
            await terminal.WriteLineAsync("Your sentence has been extended by 1 day!");
            player.DaysInPrison++;
            await Task.Delay(1500);
            return false;
        }
        else
        {
            await terminal.WriteColorLineAsync("Success! You are FREE!", TerminalEmulator.ColorGreen);

            // Generate news about successful escape
            NewsSystem.Instance.Newsy(true, $"{player.DisplayName} has escaped from the Royal Prison!");

            await terminal.WriteLineAsync();
            await Task.Delay(1000);

            // Free the player
            player.HP = player.MaxHP;
            player.DaysInPrison = 0;
            player.CellDoorOpen = false;

            await terminal.WriteLineAsync("You have successfully escaped from prison!");
            await terminal.WriteLineAsync("You are now free to return to your adventures!");
            await Task.Delay(1500);

            // Navigate to Main Street
            throw new LocationExitException(GameLocation.MainStreet);
        }
    }
    
    private async Task HandleListPrisoners(Character player)
    {
        await terminal.WriteLineAsync();
        await terminal.WriteLineAsync();
        await terminal.WriteColorLineAsync("Prisoners", TerminalEmulator.ColorWhite);
        await terminal.WriteColorLineAsync("=========", TerminalEmulator.ColorWhite);
        
        // List other prisoners
        var prisoners = await GetOtherPrisoners(player);
        
        if (prisoners.Count == 0)
        {
            await terminal.WriteColorLineAsync("You are the only prisoner here right now!", TerminalEmulator.ColorCyan);
        }
        else
        {
            foreach (var prisoner in prisoners)
            {
                await ShowPrisonerInfo(prisoner);
            }
        }
        
        // Show player's remaining time
        await terminal.WriteLineAsync();
        string dayStr = player.DaysInPrison == 1 ? "day" : "days";
        await terminal.WriteLineAsync($"You have {player.DaysInPrison} {dayStr} left in prison.");
        
        await terminal.WriteLineAsync();
        await terminal.WriteAsync("Press Enter to continue...");
        await terminal.GetCharAsync();
    }
    
    private async Task<List<Character>> GetOtherPrisoners(Character currentPlayer)
    {
        var prisoners = new List<Character>();
        await Task.CompletedTask;

        // Get NPC prisoners from the NPCSpawnSystem
        var npcPrisoners = UsurperRemake.Systems.NPCSpawnSystem.Instance.GetPrisoners();
        foreach (var npc in npcPrisoners)
        {
            prisoners.Add(npc);
        }

        // Could also add player prisoners here if multiplayer is enabled

        return prisoners;
    }
    
    private async Task ShowPrisonerInfo(Character prisoner)
    {
        await terminal.WriteColorAsync(prisoner.DisplayName, TerminalEmulator.ColorCyan);
        await terminal.WriteAsync($" the {GetRaceDisplay(prisoner.Race)}");
        
        // Show if online/offline/dead
        if (await IsPlayerOnline(prisoner))
        {
            await terminal.WriteColorAsync(" (awake)", TerminalEmulator.ColorGreen);
        }
        else if (prisoner.HP < 1)
        {
            await terminal.WriteColorAsync(" (dead)", TerminalEmulator.ColorRed);
        }
        else
        {
            await terminal.WriteAsync(" (sleeping)");
        }
        
        // Show days left
        int daysLeft = prisoner.DaysInPrison > 0 ? prisoner.DaysInPrison : 1;
        string dayStr = daysLeft == 1 ? "day" : "days";
        await terminal.WriteLineAsync($" ({daysLeft} {dayStr} left)");
    }
    
    private string GetRaceDisplay(CharacterRace race)
    {
        return race.ToString();
    }
    
    private Task<bool> IsPlayerOnline(Character player)
    {
        // TODO: Implement online player checking
        return Task.FromResult(false);
    }

    public Task<List<string>> GetLocationCommands(Character player)
    {
        var commands = new List<string>
        {
            "? - Show menu",
            "W - Who else is here",
            "D - Demand to be released",
            "O - Try to open cell door",
            "E - Attempt escape",
            "S - Show status",
            "Q - Quit game"
        };

        return Task.FromResult(commands);
    }

    public Task<bool> CanEnterLocation(Character player)
    {
        // Can only enter if actually imprisoned
        return Task.FromResult(player.DaysInPrison > 0);
    }

    public Task<string> GetLocationStatus(Character player)
    {
        int daysLeft = player.DaysInPrison;
        string dayStr = daysLeft == 1 ? "day" : "days";
        return Task.FromResult($"Imprisoned - {daysLeft} {dayStr} remaining, {player.PrisonEscapes} escape attempts left");
    }

    #region Vex Companion Recruitment

    /// <summary>
    /// Handle encountering Vex in prison - he can help you escape
    /// Returns true if player escaped (exits prison)
    /// </summary>
    private async Task<bool> HandleVexEncounter(Character player)
    {
        if (!CanMeetVex(player))
        {
            await terminal.WriteLineAsync();
            await terminal.WriteColorLineAsync("You don't see anyone unusual nearby.", TerminalEmulator.ColorDarkGray);
            await Task.Delay(1500);
            return false;
        }

        var companionSystem = UsurperRemake.Systems.CompanionSystem.Instance;
        var vex = companionSystem.GetCompanion(UsurperRemake.Systems.CompanionId.Vex);

        await terminal.ClearScreenAsync();
        await terminal.WriteLineAsync();
        await terminal.WriteColorLineAsync("╔══════════════════════════════════════════════════════════════════╗", TerminalEmulator.ColorCyan);
        await terminal.WriteColorLineAsync("║                 VOICE IN THE DARKNESS                            ║", TerminalEmulator.ColorCyan);
        await terminal.WriteColorLineAsync("╚══════════════════════════════════════════════════════════════════╝", TerminalEmulator.ColorCyan);
        await terminal.WriteLineAsync();
        await Task.Delay(1000);

        await terminal.WriteLineAsync("A voice drifts from the cell next to yours:");
        await terminal.WriteLineAsync();
        await Task.Delay(500);

        await terminal.WriteColorLineAsync("\"Psst. Hey. You. The one with the look of someone", TerminalEmulator.ColorYellow);
        await terminal.WriteColorLineAsync(" who doesn't belong here.\"", TerminalEmulator.ColorYellow);
        await terminal.WriteLineAsync();
        await Task.Delay(1500);

        await terminal.WriteLineAsync("You peer through the bars. In the adjacent cell,");
        await terminal.WriteLineAsync("a lean figure lounges against the wall with improbable comfort.");
        await terminal.WriteLineAsync("He's grinning - actually grinning - in a place like this.");
        await terminal.WriteLineAsync();
        await Task.Delay(1500);

        await terminal.WriteColorAsync($"\"{vex!.DialogueHints[0]}\"", TerminalEmulator.ColorCyan);
        await terminal.WriteLineAsync();
        await terminal.WriteLineAsync();
        await Task.Delay(2000);

        await terminal.WriteLineAsync("He produces a bent piece of metal from nowhere.");
        await terminal.WriteLineAsync("\"I've been picking locks since before I could walk.\"");
        await terminal.WriteLineAsync("\"Which is good, because I probably won't be walking much longer.\"");
        await terminal.WriteLineAsync();
        await Task.Delay(1500);

        await terminal.WriteColorAsync($"\"{vex.DialogueHints[1]}\"", TerminalEmulator.ColorCyan);
        await terminal.WriteLineAsync();
        await terminal.WriteLineAsync();
        await Task.Delay(1500);

        // Show his details
        await terminal.WriteColorLineAsync($"This is {vex.Name}, {vex.Title}.", TerminalEmulator.ColorYellow);
        await terminal.WriteColorLineAsync($"Role: {vex.CombatRole}", TerminalEmulator.ColorYellow);
        await terminal.WriteColorLineAsync($"Abilities: {string.Join(", ", vex.Abilities)}", TerminalEmulator.ColorYellow);
        await terminal.WriteLineAsync();

        await terminal.WriteColorLineAsync(vex.BackstoryBrief, TerminalEmulator.ColorDarkGray);
        await terminal.WriteLineAsync();
        await Task.Delay(1500);

        await terminal.WriteColorLineAsync("[E] Let him help you escape (and recruit him)", TerminalEmulator.ColorGreen);
        await terminal.WriteColorLineAsync("[T] Talk more about his \"condition\"", TerminalEmulator.ColorCyan);
        await terminal.WriteColorLineAsync("[L] Leave him be", TerminalEmulator.ColorDarkGray);
        await terminal.WriteLineAsync();

        await terminal.WriteAsync("Your choice: ");
        string choice = await terminal.ReadLineAsync();

        switch (choice.ToUpper())
        {
            case "E":
                return await VexHelpsEscape(player, vex);

            case "T":
                await TalkToVex(player, vex);
                return false;

            default:
                await terminal.WriteLineAsync();
                await terminal.WriteColorLineAsync("You shake your head and return to your own cell.", TerminalEmulator.ColorDarkGray);
                await terminal.WriteLineAsync();
                await terminal.WriteColorLineAsync("\"Your loss!\" he calls cheerfully.", TerminalEmulator.ColorYellow);
                await terminal.WriteColorAsync($"\"{vex.DialogueHints[2]}\"", TerminalEmulator.ColorCyan);
                await terminal.WriteLineAsync();
                await Task.Delay(2000);
                break;
        }

        // Mark encounter as complete
        StoryProgressionSystem.Instance.SetStoryFlag("vex_prison_encounter_complete", true);
        refreshMenu = true;
        return false;
    }

    /// <summary>
    /// Vex helps the player escape from prison
    /// </summary>
    private async Task<bool> VexHelpsEscape(Character player, UsurperRemake.Systems.Companion vex)
    {
        var companionSystem = UsurperRemake.Systems.CompanionSystem.Instance;

        await terminal.WriteLineAsync();
        await terminal.WriteColorLineAsync("\"Excellent choice!\" Vex grins wider.", TerminalEmulator.ColorYellow);
        await terminal.WriteLineAsync();
        await Task.Delay(1000);

        await terminal.WriteLineAsync("He works the lock with practiced ease.");
        await terminal.WriteLineAsync("*click* *click* *clack*");
        await terminal.WriteLineAsync();
        await Task.Delay(1500);

        await terminal.WriteLineAsync("\"You know what the trick is?\" he whispers.");
        await terminal.WriteLineAsync("\"It's not about forcing things. It's about listening.\"");
        await terminal.WriteLineAsync("\"Every lock wants to be opened. You just have to ask nicely.\"");
        await terminal.WriteLineAsync();
        await Task.Delay(1500);

        await terminal.WriteColorLineAsync("*CLICK*", TerminalEmulator.ColorGreen);
        await terminal.WriteLineAsync();
        await terminal.WriteLineAsync("Your cell door swings silently open.");
        await terminal.WriteLineAsync();
        await Task.Delay(1000);

        await terminal.WriteLineAsync("Vex is already beside you, moving like smoke.");
        await terminal.WriteColorLineAsync("\"Come on. I know a way out. Built this place's sewers", TerminalEmulator.ColorYellow);
        await terminal.WriteColorLineAsync(" for a job once. Long story.\"", TerminalEmulator.ColorYellow);
        await terminal.WriteLineAsync();
        await Task.Delay(1500);

        await terminal.WriteLineAsync("As you move through the dark passages, he talks.");
        await terminal.WriteLineAsync("It's compulsive - like he can't stand silence.");
        await terminal.WriteLineAsync();
        await Task.Delay(1000);

        await terminal.WriteColorLineAsync("\"I'm dying, by the way,\" he mentions casually.", TerminalEmulator.ColorCyan);
        await terminal.WriteColorLineAsync("\"Wasting disease. Got maybe a month left.\"", TerminalEmulator.ColorCyan);
        await terminal.WriteColorLineAsync("\"So I figure - might as well spend it doing something fun.\"", TerminalEmulator.ColorCyan);
        await terminal.WriteLineAsync();
        await Task.Delay(2000);

        await terminal.WriteLineAsync("He glances at you, and for just a moment,");
        await terminal.WriteLineAsync("the mask slips. You see something real beneath the jokes.");
        await terminal.WriteLineAsync();
        await Task.Delay(1000);

        await terminal.WriteColorLineAsync("\"You seem interesting. Mind if I tag along?\"", TerminalEmulator.ColorYellow);
        await terminal.WriteLineAsync();
        await Task.Delay(1500);

        // Recruit Vex
        bool success = await companionSystem.RecruitCompanion(
            UsurperRemake.Systems.CompanionId.Vex, player, terminal);

        if (success)
        {
            await terminal.WriteColorLineAsync($"{vex.Name} has joined you as a companion!", TerminalEmulator.ColorGreen);
            await terminal.WriteLineAsync();
            await terminal.WriteColorLineAsync("WARNING: Vex is dying. His time is limited.", TerminalEmulator.ColorRed);
            await terminal.WriteColorLineAsync("Make the most of the days you have together.", TerminalEmulator.ColorYellow);
            await terminal.WriteLineAsync();

            // Generate news
            NewsSystem.Instance.Newsy(true, $"{player.DisplayName} escaped from the Royal Prison with {vex.Name}'s help!");
        }

        // Free the player
        player.HP = player.MaxHP;
        player.DaysInPrison = 0;
        player.CellDoorOpen = false;

        await terminal.WriteColorLineAsync("You emerge from the sewers into the night air.", TerminalEmulator.ColorWhite);
        await terminal.WriteColorLineAsync("You are FREE!", TerminalEmulator.ColorGreen);
        await terminal.WriteLineAsync();

        // Mark encounter as complete
        StoryProgressionSystem.Instance.SetStoryFlag("vex_prison_encounter_complete", true);

        await terminal.WriteAsync("Press Enter to continue...");
        await terminal.GetCharAsync();

        // Navigate to Main Street
        throw new LocationExitException(GameLocation.MainStreet);
    }

    /// <summary>
    /// Have a deeper conversation with Vex about his condition
    /// </summary>
    private async Task TalkToVex(Character player, UsurperRemake.Systems.Companion vex)
    {
        await terminal.WriteLineAsync();
        await terminal.WriteLineAsync("\"The condition?\" He laughs, but there's an edge to it.");
        await terminal.WriteLineAsync();
        await Task.Delay(1000);

        await terminal.WriteColorLineAsync(vex.Description, TerminalEmulator.ColorWhite);
        await terminal.WriteLineAsync();
        await Task.Delay(1500);

        await terminal.WriteColorLineAsync("\"Born with it. Wasting disease. No cure.\"", TerminalEmulator.ColorCyan);
        await terminal.WriteColorLineAsync("\"Every healer I've seen says the same thing:\"", TerminalEmulator.ColorCyan);
        await terminal.WriteColorLineAsync("\"'Sorry, nothing we can do. Go enjoy your time.'\"", TerminalEmulator.ColorCyan);
        await terminal.WriteLineAsync();
        await Task.Delay(2000);

        await terminal.WriteLineAsync("He spins his lockpick between his fingers.");
        await terminal.WriteLineAsync("\"So I did. Became the best thief in the kingdom.\"");
        await terminal.WriteLineAsync("\"Stole from kings. Robbed temples. Picked pockets at my own funeral.\"");
        await terminal.WriteLineAsync("\"...That last one was a practice run.\"");
        await terminal.WriteLineAsync();
        await Task.Delay(2000);

        if (!string.IsNullOrEmpty(vex.PersonalQuestDescription))
        {
            await terminal.WriteColorLineAsync($"Personal Quest: {vex.PersonalQuestName}", TerminalEmulator.ColorMagenta);
            await terminal.WriteColorLineAsync($"\"{vex.PersonalQuestDescription}\"", TerminalEmulator.ColorMagenta);
            await terminal.WriteLineAsync();
        }

        await terminal.WriteColorLineAsync("\"Life's too short to take seriously. Trust me, I know.\"", TerminalEmulator.ColorYellow);
        await terminal.WriteLineAsync();
        await Task.Delay(1500);

        await terminal.WriteAsync("Let him help you escape? (Y/N): ");
        string answer = await terminal.ReadLineAsync();

        if (answer.ToUpper() == "Y")
        {
            await VexHelpsEscape(player, vex);
        }
        else
        {
            await terminal.WriteLineAsync();
            await terminal.WriteColorLineAsync("\"Suit yourself. Offer stands if you change your mind.\"", TerminalEmulator.ColorYellow);
            await Task.Delay(1500);
        }
    }

    #endregion
} 
