using UsurperRemake.Utils;
using UsurperRemake.Systems;
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
        LocationName = Loc.Get("prison_walk.title");
        LocationDescription = Loc.Get("prison_walk.desc");
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
            await terminal.WriteLineAsync(Loc.Get("prison_walk.cannot_visit"));
            await terminal.WriteLineAsync(Loc.Get("prison_walk.serve_sentence"));
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
        await terminal.WriteLineAsync(Loc.Get("prison_walk.leave"));
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
                await terminal.WriteAsync($"{Loc.Get("prison_walk.prompt")} (");
                await terminal.WriteColorAsync("?", TerminalEmulator.ColorYellow);
                await terminal.WriteAsync(" for menu) :");
            }
            else
            {
                await terminal.WriteLineAsync();
                await terminal.WriteAsync($"{Loc.Get("prison_walk.prompt_expert")} :");
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
        
        await terminal.WriteColorLineAsync(Loc.Get("prison_walk.title"), TerminalEmulator.ColorWhite);
        await terminal.WriteLineAsync();
        await terminal.WriteLineAsync(Loc.Get("prison_walk.walk_along"));
        await terminal.WriteLineAsync(Loc.Get("prison_walk.screams"));
        await terminal.WriteLineAsync(Loc.Get("prison_walk.torture"));
        await terminal.WriteLineAsync();

        // Menu options
        await terminal.WriteLineAsync(Loc.Get("prison_walk.menu_prisoners"));
        await terminal.WriteLineAsync(Loc.Get("prison_walk.menu_free"));
        await terminal.WriteLineAsync(Loc.Get("prison_walk.menu_status"));
        await terminal.WriteLineAsync(Loc.Get("prison_walk.menu_return"));
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
        await terminal.WriteLineAsync(Loc.Get("prison_walk.status_title"));
        await terminal.WriteLineAsync($"{Loc.Get("ui.name_label")}: {player.DisplayName}");
        await terminal.WriteLineAsync($"{Loc.Get("ui.level")}: {player.Level}");
        await terminal.WriteLineAsync($"{Loc.Get("ui.health_label")}: {player.HP}/{player.MaxHP}");
        await terminal.WriteLineAsync($"{Loc.Get("ui.gold")}: {player.Gold:N0}");
        await terminal.WriteLineAsync($"{Loc.Get("ui.experience")}: {player.Experience:N0}");
        await terminal.WriteLineAsync($"{Loc.Get("prison_walk.chivalry")}: {player.Chivalry:N0}");
        await terminal.WriteLineAsync($"{Loc.Get("prison_walk.darkness")}: {player.Darkness:N0}");
        await terminal.WriteLineAsync();
        await terminal.WriteAsync(Loc.Get("ui.press_enter"));
        await terminal.GetCharAsync();
    }
    
    private async Task HandleListPrisoners(Character player)
    {
        await terminal.WriteLineAsync();
        await terminal.WriteLineAsync();
        await terminal.WriteLineAsync(Loc.Get("prison_walk.examine"));
        await terminal.WriteLineAsync();
        
        await ListAllPrisoners();
    }
    
    private async Task ListAllPrisoners()
    {
        await terminal.WriteColorLineAsync(Loc.Get("prison_walk.current_prisoners"), TerminalEmulator.ColorWhite);
        await terminal.WriteColorLineAsync("=================", TerminalEmulator.ColorWhite);
        
        // Get list of all prisoners
        var prisoners = await GetAllPrisoners();
        
        if (prisoners.Count == 0)
        {
            await terminal.WriteColorLineAsync(Loc.Get("prison_walk.cells_empty"), TerminalEmulator.ColorCyan);
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
                    bool continueList = await terminal.ConfirmAsync(Loc.Get("prison_walk.continue_search"), true);
                    if (!continueList) break;
                }
            }
            
            await terminal.WriteLineAsync();
            await terminal.WriteLineAsync(Loc.Get("prison_walk.total_prisoners", prisoners.Count.ToString("N0")));
        }
        
        await terminal.WriteLineAsync();
        await terminal.WriteAsync(Loc.Get("ui.press_enter"));
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
            await terminal.WriteColorAsync($" {Loc.Get("prison_walk.prisoner_awake")}", TerminalEmulator.ColorGreen);
        }
        else if (prisoner.HP < 1)
        {
            await terminal.WriteColorAsync($" {Loc.Get("prison_walk.prisoner_dead")}", TerminalEmulator.ColorRed);
        }
        else
        {
            await terminal.WriteAsync($" {Loc.Get("prison_walk.prisoner_sleeping")}");
        }

        // Show days left
        int daysLeft = prisoner.DaysInPrison > 0 ? prisoner.DaysInPrison : 1;
        string dayStr = daysLeft == 1 ? Loc.Get("prison_walk.day_singular") : Loc.Get("prison_walk.day_plural");
        await terminal.WriteLineAsync($" {Loc.Get("prison_walk.days_left", daysLeft.ToString(), dayStr)}");
    }
    
    private async Task HandleFreePrisoner(Character player)
    {
        // Check if someone else is already attempting a prison break
        if (await IsPrisonBreakInProgress())
        {
            await terminal.WriteLineAsync();
            await terminal.WriteLineAsync();
            await terminal.WriteColorLineAsync(Loc.Get("prison_walk.infiltrated"), TerminalEmulator.ColorRed);
            await terminal.WriteColorLineAsync(Loc.Get("prison_walk.too_risky"), TerminalEmulator.ColorRed);
            await Task.Delay(1000);
            return;
        }
        
        await terminal.WriteLineAsync();
        await terminal.WriteLineAsync();
        await terminal.WriteColorLineAsync(Loc.Get("prison_walk.prepare_break"), TerminalEmulator.ColorRed);
        await terminal.WriteLineAsync(Loc.Get("prison_walk.dont_get_caught"));
        await terminal.WriteLineAsync(Loc.Get("prison_walk.get_you"));
        await terminal.WriteLineAsync();
        
        // Get prisoner name to free
        await terminal.WriteLineAsync(Loc.Get("prison_walk.who_free"));
        await terminal.WriteAsync(":");
        string prisonerName = await terminal.GetStringAsync();
        
        if (string.IsNullOrWhiteSpace(prisonerName))
        {
            await terminal.WriteLineAsync(Loc.Get("prison_walk.no_name"));
            return;
        }
        
        // Search for prisoner
        var prisoner = await FindPrisoner(prisonerName);
        
        if (prisoner == null)
        {
            await terminal.WriteLineAsync();
            await terminal.WriteLineAsync(Loc.Get("prison_walk.not_found", prisonerName));
            return;
        }
        
        // Confirm prisoner selection
        bool confirmed = await terminal.ConfirmAsync(Loc.Get("prison_walk.confirm_free", prisoner.DisplayName), false);
        if (!confirmed)
        {
            return;
        }
        
        // Attempt prison break
        await AttemptPrisonBreak(player, prisoner);
    }
    
    private Task<bool> IsPrisonBreakInProgress()
    {
        // Multi-player prison break coordination not yet implemented
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
        await terminal.WriteLineAsync(Loc.Get("prison_walk.attempt_break", prisoner.DisplayName));
        await terminal.WriteLineAsync();
        
        // Set location to prison break (for other systems to detect)
        
        // Gather prison guards for battle
        var guards = await GatherPrisonGuards();
        
        if (guards.Count == 0)
        {
            await terminal.WriteLineAsync(Loc.Get("prison_walk.no_guards"));
            await FreePrisonerSuccessfully(player, prisoner);
            return;
        }
        
        await terminal.WriteLineAsync(Loc.Get("prison_walk.guards_respond", guards.Count.ToString()));
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
        await terminal.WriteLineAsync(Loc.Get("prison_walk.battle_title"));
        await terminal.WriteLineAsync(Loc.Get("prison_walk.must_defeat"));
        await terminal.WriteLineAsync();

        var random = new System.Random();
        int guardsRemaining = guards.Count;
        long playerStartHP = player.HP;
        bool playerFled = false;

        foreach (var guard in guards)
        {
            await terminal.WriteLineAsync();
            await terminal.WriteColorAsync($">>> {guard.Name2}", TerminalEmulator.ColorYellow);
            await terminal.WriteLineAsync(Loc.Get("prison_walk.guard_attacks", guard.Level.ToString(), guard.HP.ToString()));
            await terminal.WriteLineAsync();

            // Combat loop with this guard
            while (guard.HP > 0 && player.HP > 0)
            {
                // Player attacks first
                int playerDamage = CalculateDamage(player, guard, random);
                guard.HP = Math.Max(0, guard.HP - playerDamage);

                await terminal.WriteAsync(Loc.Get("prison_walk.you_strike"));
                await terminal.WriteColorAsync($"{playerDamage}", TerminalEmulator.ColorGreen);
                await terminal.WriteLineAsync(Loc.Get("prison_walk.guard_hp", guard.HP.ToString()));

                if (guard.HP <= 0)
                {
                    guardsRemaining--;
                    await terminal.WriteColorLineAsync(Loc.Get("prison_walk.guard_defeated", guard.Name2), TerminalEmulator.ColorGreen);
                    break;
                }

                // Guard counter-attacks
                int guardDamage = CalculateDamage(guard, player, random);
                player.HP = Math.Max(0, player.HP - guardDamage);

                await terminal.WriteAsync(Loc.Get("prison_walk.strikes_back", guard.Name2));
                await terminal.WriteColorAsync($"{guardDamage}", TerminalEmulator.ColorRed);
                await terminal.WriteLineAsync(Loc.Get("prison_walk.your_hp", player.HP.ToString(), player.MaxHP.ToString()));

                if (player.HP <= 0)
                {
                    await terminal.WriteLineAsync();
                    await terminal.WriteColorLineAsync(Loc.Get("prison_walk.knocked_out"), TerminalEmulator.ColorRed);
                    break;
                }

                // Option to flee if taking heavy damage
                if (player.HP < player.MaxHP / 3 && guard.HP > guard.MaxHP / 4)
                {
                    bool flee = await terminal.ConfirmAsync(Loc.Get("prison_walk.attempt_flee"), false);
                    if (flee)
                    {
                        // 40% chance to escape
                        if (random.Next(100) < 40 + player.Agility / 5)
                        {
                            await terminal.WriteColorLineAsync(Loc.Get("prison_walk.escape_success"), TerminalEmulator.ColorYellow);
                            playerFled = true;
                            break;
                        }
                        else
                        {
                            await terminal.WriteColorLineAsync(Loc.Get("prison_walk.escape_fail"), TerminalEmulator.ColorRed);
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
            await terminal.WriteColorLineAsync(Loc.Get("prison_walk.victory"), TerminalEmulator.ColorGreen);
            await terminal.WriteLineAsync(Loc.Get("prison_walk.damage_taken", (playerStartHP - player.HP).ToString()));

            // Award experience for defeating guards
            long expGained = guards.Sum(g => g.Level * 50);
            player.Experience += expGained;
            await terminal.WriteLineAsync(Loc.Get("prison_walk.xp_gained", expGained.ToString()));

            return true;
        }
        else
        {
            if (playerFled)
            {
                await terminal.WriteColorLineAsync(Loc.Get("prison_walk.fled_scene"), TerminalEmulator.ColorYellow);
            }
            else
            {
                await terminal.WriteColorLineAsync(Loc.Get("prison_walk.captured"), TerminalEmulator.ColorRed);

                bool surrender = await terminal.ConfirmAsync(Loc.Get("prison_walk.surrender"), true);

                if (surrender)
                {
                    await terminal.WriteColorLineAsync(Loc.Get("prison_walk.coward"), TerminalEmulator.ColorRed);
                }
                else
                {
                    await terminal.WriteColorLineAsync(Loc.Get("prison_walk.beaten_unconscious"), TerminalEmulator.ColorRed);
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
        await terminal.WriteColorLineAsync(Loc.Get("prison_walk.success"), TerminalEmulator.ColorGreen);
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
        await terminal.WriteLineAsync(Loc.Get("prison_walk.now_free"));
        await terminal.WriteLineAsync();

        // Increase chivalry for the heroic rescue
        long chivalryGain = 50 + prisoner.Level * 10;
        player.Chivalry += chivalryGain;
        await terminal.WriteLineAsync(Loc.Get("prison_walk.chivalry_gain", chivalryGain.ToString()));

        await terminal.WriteLineAsync();
        await terminal.WriteLineAsync(Loc.Get("prison_walk.thanks"));
        await terminal.WriteLineAsync(Loc.Get("prison_walk.friend"));

        // Add to player's known allies (if applicable)
        // This could trigger future events where the freed prisoner helps the player

        await Task.Delay(2000);
    }
    
    private async Task HandlePrisonBreakFailure(Character player, Character prisoner)
    {
        await terminal.WriteLineAsync();
        await terminal.WriteColorLineAsync(Loc.Get("prison_walk.break_failed"), TerminalEmulator.ColorRed);
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

        await terminal.WriteLineAsync(Loc.Get("prison_walk.thrown_in_cell"));
        await terminal.WriteLineAsync(Loc.Get("prison_walk.sentenced", player.DaysInPrison.ToString()));
        await terminal.WriteLineAsync();

        // Lose some gold as a fine
        long fine = Math.Min(player.Gold, 100 + player.Level * 25);
        if (fine > 0)
        {
            player.Gold -= fine;
            await terminal.WriteLineAsync(Loc.Get("prison_walk.gold_fine", fine.ToString("N0")));
        }

        // Darkness increases for criminal activity
        player.Darkness += 25;
        await terminal.WriteColorLineAsync(Loc.Get("prison_walk.darkness_increase"), TerminalEmulator.ColorMagenta);

        await terminal.WriteLineAsync();
        await terminal.WriteLineAsync(Loc.Get("prison_walk.wake_tomorrow"));
        await terminal.WriteLineAsync(Loc.Get("prison_walk.plan_better"));

        await Task.Delay(2000);
    }
    
    private string GetRaceDisplay(CharacterRace race)
    {
        return race.ToString();
    }
    
    private Task<bool> IsPlayerOnline(Character player)
    {
        // Online player checking not yet implemented
        return Task.FromResult(false);
    }

    public Task<List<string>> GetLocationCommands(Character player)
    {
        var commands = new List<string>
        {
            Loc.Get("prison_walk.help_menu"),
            Loc.Get("prison_walk.help_prisoners"),
            Loc.Get("prison_walk.help_free"),
            Loc.Get("prison_walk.help_status"),
            Loc.Get("prison_walk.help_return")
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
        return Loc.Get("prison_walk.location_status", prisoners.Count.ToString());
    }
} 
