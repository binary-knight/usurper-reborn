using UsurperRemake.Utils;
using Godot;
using System.Collections.Generic;
using System.Threading.Tasks;
using System.Linq;
using System;

/// <summary>
/// Prison Walk Location - Players can attempt to free prisoners from outside
/// Based on PRISONF.PAS from the original Usurper Pascal implementation
/// Provides prison breaking mechanics, guard combat, and prisoner liberation
/// </summary>
public partial class PrisonWalkLocation : BaseLocation
{
    private readonly GameEngine gameEngine;
    private new readonly TerminalEmulator terminal;
    private bool refreshMenu = true;
    
    public PrisonWalkLocation(GameEngine engine, TerminalEmulator term) : base("prisonwalk")
    {
        gameEngine = engine ?? throw new System.ArgumentNullException(nameof(engine));
        terminal = term ?? throw new System.ArgumentNullException(nameof(term));
        
        SetLocationProperties();
    }
    
    // Add parameterless constructor for compatibility
    public PrisonWalkLocation() : base("prison_walk")
    {
        gameEngine = GameEngine.Instance;
        terminal = GameEngine.Instance.Terminal;
        SetLocationProperties();
    }
    
    private void SetLocationProperties()
    {
        LocationId = GameLocation.PrisonWalk;
        LocationName = "Outside the Royal Prison";
        LocationDescription = "A small courtyard where prisoners can exercise";
        AllowedClasses = new HashSet<CharacterClass>();
        LevelRequirement = 1;
        
        // Add all character classes to allowed set
        foreach (CharacterClass charClass in System.Enum.GetValues<CharacterClass>())
        {
            AllowedClasses.Add(charClass);
        }
    }
    
    public async Task<bool> EnterLocation(Character player)
    {
        if (player == null) return false;
        
        // Cannot enter if player is imprisoned
        if (player.DaysInPrison > 0)
        {
            await terminal.WriteLineAsync("You cannot visit the prison while you are imprisoned!");
            await terminal.WriteLineAsync("You must serve your sentence first.");
            await Task.Delay(1000);
            return false;
        }
        
        refreshMenu = true;
        await ShowPrisonWalkInterface(player);
        return true;
    }
    
    private async Task ShowPrisonWalkInterface(Character player)
    {
        char choice = '?';
        
        while (choice != 'R')
        {
            // Update location status if needed
            await UpdateLocationStatus(player);
            
            // Display menu
            await DisplayPrisonWalkMenu(player, true, true);
            
            // Get user input
            choice = await terminal.GetCharAsync();
            choice = char.ToUpper(choice);
            
            // Process user choice
            await ProcessPrisonWalkChoice(player, choice);
        }
        
        // Return message
        await terminal.WriteLineAsync();
        await terminal.WriteLineAsync();
        await terminal.WriteLineAsync("You leave the depressing prison grounds.");
        await terminal.WriteLineAsync();
    }
    
    private Task UpdateLocationStatus(Character player)
    {
        // This would typically update the online player location
        // For now, just ensure the location is set correctly
        refreshMenu = true;
        return Task.CompletedTask;
    }
    
    private async Task DisplayPrisonWalkMenu(Character player, bool force, bool isShort)
    {
        if (isShort)
        {
            if (!player.Expert)
            {
                if (refreshMenu)
                {
                    refreshMenu = false;
                    await ShowPrisonWalkMenuFull();
                }
                
                await terminal.WriteLineAsync();
                await terminal.WriteAsync("Prison walk (");
                await terminal.WriteColorAsync("?", TerminalEmulator.ColorYellow);
                await terminal.WriteAsync(" for menu) :");
            }
            else
            {
                await terminal.WriteLineAsync();
                await terminal.WriteAsync("Prison walk (P,F,S,R,?) :");
            }
        }
        else
        {
            if (!player.Expert || force)
            {
                await ShowPrisonWalkMenuFull();
            }
        }
    }
    
    private async Task ShowPrisonWalkMenuFull()
    {
        await terminal.ClearScreenAsync();
        await terminal.WriteLineAsync();
        
        await terminal.WriteColorLineAsync("Outside the Royal Prison", TerminalEmulator.ColorWhite);
        await terminal.WriteLineAsync();
        await terminal.WriteLineAsync("You walk along side the long stretch of cells.");
        await terminal.WriteLineAsync("From the dark pits You can hear the screams from the tortured souls");
        await terminal.WriteLineAsync("deep in the dungeons. The torture masters must be having a great time.");
        await terminal.WriteLineAsync();
        
        // Menu options
        await terminal.WriteLineAsync("(P)risoners");
        await terminal.WriteLineAsync("(F)ree a prisoner");
        await terminal.WriteLineAsync("(S)tatus");
        await terminal.WriteLineAsync("(R)eturn");
    }
    
    private async Task ProcessPrisonWalkChoice(Character player, char choice)
    {
        switch (choice)
        {
            case '?':
                await HandleMenuDisplay(player);
                break;
            case 'S':
                await HandleStatusDisplay(player);
                break;
            case 'P':
                await HandleListPrisoners(player);
                break;
            case 'F':
                await HandleFreePrisoner(player);
                break;
            case 'R':
                // Return - handled by main loop
                break;
            default:
                // Invalid choice, do nothing
                break;
        }
    }
    
    private async Task HandleMenuDisplay(Character player)
    {
        if (player.Expert)
            await DisplayPrisonWalkMenu(player, true, false);
        else
            await DisplayPrisonWalkMenu(player, false, false);
    }
    
    private async Task HandleStatusDisplay(Character player)
    {
        await ShowCharacterStatus(player);
    }
    
    private async Task ShowCharacterStatus(Character player)
    {
        await terminal.WriteLineAsync();
        await terminal.WriteLineAsync("=== CHARACTER STATUS ===");
        await terminal.WriteLineAsync($"Name: {player.DisplayName}");
        await terminal.WriteLineAsync($"Level: {player.Level}");
        await terminal.WriteLineAsync($"Health: {player.HP}/{player.MaxHP}");
        await terminal.WriteLineAsync($"Gold: {player.Gold:N0}");
        await terminal.WriteLineAsync($"Experience: {player.Experience:N0}");
        await terminal.WriteLineAsync($"Chivalry: {player.Chivalry:N0}");
        await terminal.WriteLineAsync($"Darkness: {player.Darkness:N0}");
        await terminal.WriteLineAsync();
        await terminal.WriteAsync("Press Enter to continue...");
        await terminal.GetCharAsync();
    }
    
    private async Task HandleListPrisoners(Character player)
    {
        await terminal.WriteLineAsync();
        await terminal.WriteLineAsync();
        await terminal.WriteLineAsync("You examine the Dungeons.");
        await terminal.WriteLineAsync();
        
        await ListAllPrisoners();
    }
    
    private async Task ListAllPrisoners()
    {
        await terminal.WriteColorLineAsync("Current Prisoners", TerminalEmulator.ColorWhite);
        await terminal.WriteColorLineAsync("=================", TerminalEmulator.ColorWhite);
        
        // Get list of all prisoners
        var prisoners = await GetAllPrisoners();
        
        if (prisoners.Count == 0)
        {
            await terminal.WriteColorLineAsync("The Cells are empty! (how boring!)", TerminalEmulator.ColorCyan);
        }
        else
        {
            int count = 0;
            foreach (var prisoner in prisoners)
            {
                await ShowPrisonerInfo(prisoner);
                count++;
                
                // Pause for long lists
                if (count % 10 == 0)
                {
                    bool continueList = await terminal.ConfirmAsync("Continue search", true);
                    if (!continueList) break;
                }
            }
            
            await terminal.WriteLineAsync();
            await terminal.WriteLineAsync($"There is a total of {prisoners.Count:N0} prisoners.");
        }
        
        await terminal.WriteLineAsync();
        await terminal.WriteAsync("Press Enter to continue...");
        await terminal.GetCharAsync();
    }
    
    private async Task<List<Character>> GetAllPrisoners()
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
    
    private async Task HandleFreePrisoner(Character player)
    {
        // Check if someone else is already attempting a prison break
        if (await IsPrisonBreakInProgress())
        {
            await terminal.WriteLineAsync();
            await terminal.WriteLineAsync();
            await terminal.WriteColorLineAsync("Sorry, the Prison is being infiltrated right now!", TerminalEmulator.ColorRed);
            await terminal.WriteColorLineAsync("There would be too big a risk to break in!", TerminalEmulator.ColorRed);
            await Task.Delay(1000);
            return;
        }
        
        await terminal.WriteLineAsync();
        await terminal.WriteLineAsync();
        await terminal.WriteColorLineAsync("You prepare to break in!", TerminalEmulator.ColorRed);
        await terminal.WriteLineAsync("Don't get caught! You will be jailed instantly if the guards");
        await terminal.WriteLineAsync("get you!");
        await terminal.WriteLineAsync();
        
        // Get prisoner name to free
        await terminal.WriteLineAsync("Who do you wanna set free?");
        await terminal.WriteAsync(":");
        string prisonerName = await terminal.GetStringAsync();
        
        if (string.IsNullOrWhiteSpace(prisonerName))
        {
            await terminal.WriteLineAsync("No prisoner name entered.");
            return;
        }
        
        // Search for prisoner
        var prisoner = await FindPrisoner(prisonerName);
        
        if (prisoner == null)
        {
            await terminal.WriteLineAsync();
            await terminal.WriteLineAsync($"Could not find prisoner '{prisonerName}' in the prison.");
            return;
        }
        
        // Confirm prisoner selection
        bool confirmed = await terminal.ConfirmAsync($"Free {prisoner.DisplayName}", false);
        if (!confirmed)
        {
            return;
        }
        
        // Attempt prison break
        await AttemptPrisonBreak(player, prisoner);
    }
    
    private Task<bool> IsPrisonBreakInProgress()
    {
        // TODO: Check if any other player is currently on location onloc_prisonbreak
        // For now, always return false
        return Task.FromResult(false);
    }
    
    private Task<Character?> FindPrisoner(string searchName)
    {
        // First search NPC prisoners
        var npcPrisoner = UsurperRemake.Systems.NPCSpawnSystem.Instance.FindPrisoner(searchName);
        if (npcPrisoner != null)
        {
            return Task.FromResult<Character?>(npcPrisoner);
        }

        // Could also search player prisoners here if multiplayer is enabled

        return Task.FromResult<Character?>(null);
    }
    
    private async Task AttemptPrisonBreak(Character player, Character prisoner)
    {
        await terminal.WriteLineAsync();
        await terminal.WriteLineAsync($"You attempt to break {prisoner.DisplayName} out of prison!");
        await terminal.WriteLineAsync();
        
        // Set location to prison break (for other systems to detect)
        // TODO: Set player location to onloc_prisonbreak
        
        // Gather prison guards for battle
        var guards = await GatherPrisonGuards();
        
        if (guards.Count == 0)
        {
            await terminal.WriteLineAsync("No guards responded! The prison break succeeds easily!");
            await FreePrisonerSuccessfully(player, prisoner);
            return;
        }
        
        await terminal.WriteLineAsync($"{guards.Count} prison guards respond to the alarm!");
        await terminal.WriteLineAsync();
        
        // Battle with guards
        bool combatResult = await BattlePrisonGuards(player, guards);
        
        if (combatResult)
        {
            // Player won - successful prison break
            await FreePrisonerSuccessfully(player, prisoner);
        }
        else
        {
            // Player lost or surrendered - get imprisoned
            await HandlePrisonBreakFailure(player, prisoner);
        }
    }
    
    private async Task<List<Character>> GatherPrisonGuards()
    {
        await Task.CompletedTask;
        var guards = new List<Character>();
        var random = new System.Random();

        // Number of guards based on player level (1-4 guards)
        int guardCount = Math.Max(1, Math.Min(4, random.Next(1, 3) + (GameEngine.Instance.CurrentPlayer?.Level ?? 1) / 5));

        for (int i = 0; i < guardCount; i++)
        {
            var guard = new Character
            {
                Name1 = GetGuardName(i),
                Name2 = GetGuardName(i),
                Class = CharacterClass.Warrior,
                Race = CharacterRace.Human,
                Level = Math.Max(1, (GameEngine.Instance.CurrentPlayer?.Level ?? 1) - random.Next(-2, 3)),
                AI = CharacterAI.Computer
            };

            // Scale stats based on level
            guard.Strength = 15 + guard.Level * 4;
            guard.Defence = 15 + guard.Level * 3;
            guard.Stamina = 12 + guard.Level * 3;
            guard.Agility = 10 + guard.Level * 2;
            guard.HP = 50 + guard.Level * 25;
            guard.MaxHP = guard.HP;
            guard.WeapPow = 5 + guard.Level * 3;
            guard.ArmPow = 3 + guard.Level * 2;

            guards.Add(guard);
        }

        return guards;
    }

    private string GetGuardName(int index)
    {
        var guardNames = new[]
        {
            "Royal Guard",
            "Prison Warden",
            "Iron Fist Guard",
            "Dungeon Keeper",
            "Jailer",
            "Tower Guard",
            "Cell Block Guardian",
            "Sheriff's Deputy"
        };
        return guardNames[index % guardNames.Length];
    }
    
    private async Task<bool> BattlePrisonGuards(Character player, List<Character> guards)
    {
        await terminal.WriteLineAsync("=== PRISON GUARD BATTLE ===");
        await terminal.WriteLineAsync("You must defeat all guards to free the prisoner!");
        await terminal.WriteLineAsync();

        var random = new System.Random();
        int guardsRemaining = guards.Count;
        long playerStartHP = player.HP;
        bool playerFled = false;

        foreach (var guard in guards)
        {
            await terminal.WriteLineAsync();
            await terminal.WriteColorAsync($">>> {guard.Name2}", TerminalEmulator.ColorYellow);
            await terminal.WriteLineAsync($" (Level {guard.Level}, HP: {guard.HP}) attacks!");
            await terminal.WriteLineAsync();

            // Combat loop with this guard
            while (guard.HP > 0 && player.HP > 0)
            {
                // Player attacks first
                int playerDamage = CalculateDamage(player, guard, random);
                guard.HP = Math.Max(0, guard.HP - playerDamage);

                await terminal.WriteAsync($"You strike for ");
                await terminal.WriteColorAsync($"{playerDamage}", TerminalEmulator.ColorGreen);
                await terminal.WriteLineAsync($" damage! (Guard HP: {guard.HP})");

                if (guard.HP <= 0)
                {
                    guardsRemaining--;
                    await terminal.WriteColorLineAsync($"{guard.Name2} is defeated!", TerminalEmulator.ColorGreen);
                    break;
                }

                // Guard counter-attacks
                int guardDamage = CalculateDamage(guard, player, random);
                player.HP = Math.Max(0, player.HP - guardDamage);

                await terminal.WriteAsync($"{guard.Name2} strikes back for ");
                await terminal.WriteColorAsync($"{guardDamage}", TerminalEmulator.ColorRed);
                await terminal.WriteLineAsync($" damage! (Your HP: {player.HP}/{player.MaxHP})");

                if (player.HP <= 0)
                {
                    await terminal.WriteLineAsync();
                    await terminal.WriteColorLineAsync("You have been knocked unconscious!", TerminalEmulator.ColorRed);
                    break;
                }

                // Option to flee if taking heavy damage
                if (player.HP < player.MaxHP / 3 && guard.HP > guard.MaxHP / 4)
                {
                    bool flee = await terminal.ConfirmAsync("Attempt to flee", false);
                    if (flee)
                    {
                        // 40% chance to escape
                        if (random.Next(100) < 40 + player.Agility / 5)
                        {
                            await terminal.WriteColorLineAsync("You manage to escape!", TerminalEmulator.ColorYellow);
                            playerFled = true;
                            break;
                        }
                        else
                        {
                            await terminal.WriteColorLineAsync("You couldn't escape!", TerminalEmulator.ColorRed);
                        }
                    }
                }

                await Task.Delay(300);
            }

            if (player.HP <= 0 || playerFled)
            {
                break;
            }

            await Task.Delay(500);
        }

        await terminal.WriteLineAsync();

        if (player.HP > 0 && guardsRemaining == 0 && !playerFled)
        {
            await terminal.WriteColorLineAsync("VICTORY! All guards have been defeated!", TerminalEmulator.ColorGreen);
            await terminal.WriteLineAsync($"You took {playerStartHP - player.HP} damage during the fight.");

            // Award experience for defeating guards
            long expGained = guards.Sum(g => g.Level * 50);
            player.Experience += expGained;
            await terminal.WriteLineAsync($"You gained {expGained} experience!");

            return true;
        }
        else
        {
            if (playerFled)
            {
                await terminal.WriteColorLineAsync("You fled the scene! The alarm is raised!", TerminalEmulator.ColorYellow);
            }
            else
            {
                await terminal.WriteColorLineAsync("The prison guards have captured you!", TerminalEmulator.ColorRed);

                bool surrender = await terminal.ConfirmAsync("Surrender peacefully", true);

                if (surrender)
                {
                    await terminal.WriteColorLineAsync("YOU COWARD!", TerminalEmulator.ColorRed);
                }
                else
                {
                    await terminal.WriteColorLineAsync("The guards beat you unconscious!", TerminalEmulator.ColorRed);
                }
            }

            return false;
        }
    }

    private int CalculateDamage(Character attacker, Character defender, System.Random random)
    {
        // Basic damage formula
        int baseDamage = (int)(attacker.Strength / 3 + attacker.WeapPow);
        int variance = Math.Max(1, baseDamage / 3);
        int damage = baseDamage + random.Next(-variance, variance + 1);

        // Apply defense reduction
        int defense = (int)(defender.Defence / 4 + defender.ArmPow / 2);
        damage = Math.Max(1, damage - defense / 2);

        return damage;
    }
    
    private async Task FreePrisonerSuccessfully(Character player, Character prisoner)
    {
        await terminal.WriteLineAsync();
        await terminal.WriteColorLineAsync("SUCCESS! The prisoner has been freed!", TerminalEmulator.ColorGreen);
        await terminal.WriteLineAsync();

        // If prisoner is an NPC, use the NPCSpawnSystem
        if (prisoner is NPC npcPrisoner)
        {
            UsurperRemake.Systems.NPCSpawnSystem.Instance.ReleaseNPC(npcPrisoner, player.DisplayName);
        }
        else
        {
            // For player prisoners, set the cell door open flag
            prisoner.CellDoorOpen = true;
            prisoner.RescuedBy = player.DisplayName;
        }

        await terminal.WriteColorAsync(prisoner.DisplayName, TerminalEmulator.ColorCyan);
        await terminal.WriteLineAsync(" is now free!");
        await terminal.WriteLineAsync();

        // Increase chivalry for the heroic rescue
        long chivalryGain = 50 + prisoner.Level * 10;
        player.Chivalry += chivalryGain;
        await terminal.WriteLineAsync($"Your heroic act increases your Chivalry by {chivalryGain}!");

        await terminal.WriteLineAsync();
        await terminal.WriteLineAsync("The prisoner thanks you profusely and disappears into the night.");
        await terminal.WriteLineAsync("\"I won't forget this! You have made a friend today!\"");

        // Add to player's known allies (if applicable)
        // This could trigger future events where the freed prisoner helps the player

        await Task.Delay(2000);
    }
    
    private async Task HandlePrisonBreakFailure(Character player, Character prisoner)
    {
        await terminal.WriteLineAsync();
        await terminal.WriteColorLineAsync("PRISON BREAK FAILED!", TerminalEmulator.ColorRed);
        await terminal.WriteLineAsync();

        // Calculate sentence based on severity
        int baseSentence = GameConfig.DefaultPrisonSentence + GameConfig.PrisonBreakPenalty;
        int extraDays = prisoner.Level / 2; // Higher level prisoners have more security
        int totalSentence = Math.Min(255, baseSentence + extraDays);

        // Player gets imprisoned
        player.DaysInPrison = (byte)totalSentence;
        player.PrisonEscapes = 1; // Start with 1 escape attempt
        player.CellDoorOpen = false;
        player.RescuedBy = "";

        // Set HP to 1 (badly beaten but not dead)
        player.HP = 1;

        await terminal.WriteLineAsync("You are beaten by the guards and thrown into a cell!");
        await terminal.WriteLineAsync($"You are sentenced to {player.DaysInPrison} days in prison!");
        await terminal.WriteLineAsync();

        // Lose some gold as a fine
        long fine = Math.Min(player.Gold, 100 + player.Level * 25);
        if (fine > 0)
        {
            player.Gold -= fine;
            await terminal.WriteLineAsync($"The crown confiscates {fine:N0} gold as a fine!");
        }

        // Darkness increases for criminal activity
        player.Darkness += 25;
        await terminal.WriteColorLineAsync("Your Darkness increases from your criminal behavior.", TerminalEmulator.ColorMagenta);

        await terminal.WriteLineAsync();
        await terminal.WriteLineAsync("You will wake up in your cell tomorrow...");
        await terminal.WriteLineAsync("Maybe next time, plan your escape better!");

        await Task.Delay(2000);
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
            "P - List prisoners",
            "F - Attempt to free a prisoner",
            "S - Show status",
            "R - Return to Main Street"
        };

        return Task.FromResult(commands);
    }

    public Task<bool> CanEnterLocation(Character player)
    {
        // Cannot enter if player is imprisoned
        return Task.FromResult(player.DaysInPrison <= 0);
    }
    
    public async Task<string> GetLocationStatus(Character player)
    {
        var prisoners = await GetAllPrisoners();
        return $"Outside the Royal Prison - {prisoners.Count} prisoners currently held";
    }
} 
