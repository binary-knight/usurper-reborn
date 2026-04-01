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
            await terminal.WriteLineAsync(Loc.Get("prison.not_imprisoned"));
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
                await terminal.WriteColorLineAsync(Loc.Get("prison.guards_open"), TerminalEmulator.ColorGreen);
                await terminal.WriteLineAsync(Loc.Get("prison.sentence_served"));
                await terminal.WriteColorLineAsync(Loc.Get("prison.you_are_free"), TerminalEmulator.ColorGreen);
                player.CellDoorOpen = false;
                player.RescuedBy = "";
                player.HP = Math.Max(player.HP, player.MaxHP / 2); // Restore some health

                // Reduce Darkness for serving time — prevents arrest loop
                long darknessReduction = Math.Min(player.Darkness, 75);
                if (darknessReduction > 0)
                {
                    player.Darkness -= darknessReduction;
                    await terminal.WriteColorLineAsync(Loc.Get("prison.darkness_reduced", darknessReduction), "bright_green");
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
        await terminal.WriteColorLineAsync(Loc.Get("prison.cell_door_open"), TerminalEmulator.ColorGreen);
        await terminal.WriteLineAsync();

        if (!string.IsNullOrEmpty(player.RescuedBy))
        {
            await terminal.WriteColorAsync(player.RescuedBy, TerminalEmulator.ColorCyan);
            await terminal.WriteLineAsync(Loc.Get("prison.broke_out"));
            await terminal.WriteLineAsync(Loc.Get("prison.owe_freedom"));
        }
        else
        {
            await terminal.WriteLineAsync(Loc.Get("prison.someone_unlocked"));
        }

        await terminal.WriteLineAsync();
        await terminal.WriteLineAsync(Loc.Get("prison.walk_out"));
        await terminal.WriteColorLineAsync(Loc.Get("prison.you_are_free"), TerminalEmulator.ColorGreen);

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
        // Online prisoner display not yet implemented
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
                await terminal.WriteAsync(Loc.Get("prison.prompt_prefix"));
                await terminal.WriteColorAsync("?", TerminalEmulator.ColorYellow);
                await terminal.WriteAsync(Loc.Get("prison.prompt_suffix"));
            }
            else
            {
                await terminal.WriteLineAsync();
                await terminal.WriteAsync(Loc.Get("prison.prompt_expert"));
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
        if (!IsScreenReader)
        {
            await terminal.WriteColorLineAsync("IIIIIIIIIIIIIIIIIIIIIIII", TerminalEmulator.ColorCyan);
            await terminal.WriteColorLineAsync($"III {Loc.Get("prison.title")} III", TerminalEmulator.ColorCyan);
            await terminal.WriteColorLineAsync("IIIIIIIIIIIIIIIIIIIIIIII", TerminalEmulator.ColorCyan);
        }
        else
        {
            await terminal.WriteColorLineAsync(Loc.Get("prison.title"), TerminalEmulator.ColorCyan);
        }
        await terminal.WriteLineAsync();
        
        // Prison atmosphere description
        await terminal.WriteLineAsync(Loc.Get("prison.atmo1"));
        await terminal.WriteLineAsync(Loc.Get("prison.atmo2"));
        await terminal.WriteLineAsync(Loc.Get("prison.atmo3"));
        await terminal.WriteLineAsync(Loc.Get("prison.atmo4"));
        await terminal.WriteLineAsync(Loc.Get("prison.atmo5"));
        await terminal.WriteLineAsync();
        
        // Menu options
        if (IsScreenReader)
        {
            await terminal.WriteLineAsync(Loc.Get("prison.sr_menu_who"));
            await terminal.WriteLineAsync(Loc.Get("prison.sr_menu_demand"));
            await terminal.WriteLineAsync(Loc.Get("prison.sr_menu_open"));
            await terminal.WriteLineAsync(Loc.Get("prison.sr_menu_escape"));
            await terminal.WriteLineAsync(Loc.Get("prison.sr_menu_status"));
            await terminal.WriteLineAsync(Loc.Get("prison.sr_menu_activities"));
            await terminal.WriteLineAsync("B. Pay Bail (if bail is set)");
            await terminal.WriteLineAsync("P. Petition the King for release");

            var currentPlayer = gameEngine?.CurrentPlayer;
            if (currentPlayer != null && CanMeetVex(currentPlayer))
            {
                await terminal.WriteLineAsync(Loc.Get("prison.sr_menu_vex"));
            }

            await terminal.WriteLineAsync(Loc.Get("prison.sr_menu_quit"));
        }
        else
        {
            await terminal.WriteLineAsync(Loc.Get("prison.menu_row1"));
            await terminal.WriteLineAsync(Loc.Get("prison.menu_row2"));
            await terminal.WriteLineAsync(Loc.Get("prison.menu_row3"));
            await terminal.WriteLineAsync("(B)ail Payment              (P)etition for Release");

            // Check for Vex companion availability - get player from game engine
            var currentPlayer = gameEngine?.CurrentPlayer;
            if (currentPlayer != null && CanMeetVex(currentPlayer))
            {
                await terminal.WriteColorAsync("(V)", TerminalEmulator.ColorYellow);
                await terminal.WriteColorLineAsync(Loc.Get("prison.menu_vex_suffix"), TerminalEmulator.ColorCyan);
            }

            await terminal.WriteLineAsync(Loc.Get("prison.menu_quit"));
        }
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
            case 'B':
                return await HandlePayBail(player);
            case 'P':
                await HandlePetitionKing(player);
                return false;
            case 'V':
                return await HandleVexEncounter(player);
            default:
                // Invalid choice, do nothing
                return false;
        }
    }

    /// <summary>
    /// Handle bail payment — player pays gold to get released immediately
    /// </summary>
    private async Task<bool> HandlePayBail(Character player)
    {
        if (player.IsMurderConvict)
        {
            await terminal.WriteColorLineAsync("  Murder convicts are not eligible for bail.", TerminalEmulator.ColorRed);
            await Task.Delay(1500);
            return false;
        }

        var king = CastleLocation.GetCurrentKing();
        if (king == null)
        {
            await terminal.WriteColorLineAsync("  There is no king to accept bail.", TerminalEmulator.ColorYellow);
            await Task.Delay(2000);
            return false;
        }

        // Find this player's prison record
        string playerName = player.DisplayName ?? player.Name2 ?? "";
        if (!king.Prisoners.TryGetValue(playerName, out var record))
        {
            await terminal.WriteColorLineAsync("  No bail has been set for you.", TerminalEmulator.ColorYellow);
            await Task.Delay(2000);
            return false;
        }

        if (record.BailAmount <= 0)
        {
            await terminal.WriteColorLineAsync("  The king has not set bail for your release.", TerminalEmulator.ColorYellow);
            await terminal.WriteColorLineAsync("  You must wait, escape, or petition for clemency.", TerminalEmulator.ColorDarkGray);
            await Task.Delay(2000);
            return false;
        }

        await terminal.WriteLineAsync();
        await terminal.WriteColorLineAsync($"  Bail is set at {record.BailAmount:N0} gold.", TerminalEmulator.ColorCyan);
        await terminal.WriteColorLineAsync($"  You have {player.Gold:N0} gold.", TerminalEmulator.ColorWhite);
        await terminal.WriteLineAsync();

        if (player.Gold < record.BailAmount)
        {
            await terminal.WriteColorLineAsync("  You cannot afford bail.", TerminalEmulator.ColorRed);
            await Task.Delay(2000);
            return false;
        }

        await terminal.WriteColorAsync($"  Pay {record.BailAmount:N0} gold for your freedom? (Y/N): ", TerminalEmulator.ColorCyan);
        string confirm = await terminal.ReadLineAsync();
        if (confirm?.Trim().ToUpper() != "Y")
        {
            await terminal.WriteColorLineAsync("  You decide to keep your gold... for now.", TerminalEmulator.ColorDarkGray);
            await Task.Delay(1500);
            return false;
        }

        // Pay bail
        player.Gold -= record.BailAmount;
        long bailPaid = record.BailAmount;

        // Add bail to king's treasury
        king.Treasury += bailPaid;

        // Release from prison
        player.DaysInPrison = 0;
        king.Prisoners.Remove(playerName);

        // Persist changes
        if (UsurperRemake.BBS.DoorMode.IsOnlineMode)
            CastleLocation.PersistRoyalCourtToWorldStateStatic();

        await terminal.WriteLineAsync();
        await terminal.WriteColorLineAsync($"  You pay {bailPaid:N0} gold to the jailer.", TerminalEmulator.ColorYellow);
        await terminal.WriteColorLineAsync("  The cell door swings open.", TerminalEmulator.ColorGreen);
        await terminal.WriteColorLineAsync("  You are free!", TerminalEmulator.ColorGreen);
        await terminal.WriteLineAsync();

        NewsSystem.Instance?.Newsy(true, $"{playerName} paid {bailPaid:N0} gold bail and was released from prison.");

        await terminal.WaitForKey();
        return true; // Exit prison
    }

    /// <summary>
    /// Handle petition to the king for release or bail setting
    /// </summary>
    private async Task HandlePetitionKing(Character player)
    {
        var king = CastleLocation.GetCurrentKing();
        if (king == null)
        {
            await terminal.WriteColorLineAsync("  There is no king to petition.", TerminalEmulator.ColorYellow);
            await Task.Delay(2000);
            return;
        }

        string playerName = player.DisplayName ?? player.Name2 ?? "";

        await terminal.ClearScreenAsync();
        await terminal.WriteColorLineAsync("  ═══ PETITION TO THE CROWN ═══", TerminalEmulator.ColorCyan);
        await terminal.WriteLineAsync();
        await terminal.WriteColorLineAsync("  You send a formal petition to the throne:", TerminalEmulator.ColorWhite);
        await terminal.WriteLineAsync();
        await terminal.WriteColorLineAsync("  1. Request bail be set (if none is set)", TerminalEmulator.ColorCyan);
        await terminal.WriteColorLineAsync("  2. Plead for clemency (request pardon)", TerminalEmulator.ColorCyan);
        await terminal.WriteColorLineAsync("  0. Cancel", TerminalEmulator.ColorDarkGray);
        await terminal.WriteLineAsync();

        string choice = await terminal.ReadLineAsync();

        if (choice?.Trim() == "1")
        {
            // Request bail
            bool hasBail = king.Prisoners.TryGetValue(playerName, out var record) && record?.BailAmount > 0;
            if (hasBail)
            {
                await terminal.WriteColorLineAsync($"  Bail is already set at {record!.BailAmount:N0} gold.", TerminalEmulator.ColorYellow);
            }
            else
            {
                // NPC king auto-sets bail based on player level and crime
                if (king.AI == CharacterAI.Computer)
                {
                    long bailAmount = 1000 + player.Level * 500;
                    if (king.Prisoners.TryGetValue(playerName, out var rec) && rec != null)
                    {
                        rec.BailAmount = bailAmount;
                        await terminal.WriteColorLineAsync($"  The king considers your petition...", TerminalEmulator.ColorWhite);
                        await Task.Delay(1500);
                        await terminal.WriteColorLineAsync($"  Bail has been set at {bailAmount:N0} gold.", TerminalEmulator.ColorGreen);
                        await terminal.WriteColorLineAsync($"  Use [B] to pay bail.", TerminalEmulator.ColorCyan);
                    }
                    else
                    {
                        await terminal.WriteColorLineAsync("  The king cannot find your prison record.", TerminalEmulator.ColorRed);
                    }
                }
                else
                {
                    // Human king — send a message
                    if (UsurperRemake.BBS.DoorMode.IsOnlineMode)
                    {
                        var backend = SaveSystem.Instance.Backend as SqlSaveBackend;
                        if (backend != null)
                        {
                            try
                            {
                                string kingUsername = king.Name.ToLowerInvariant();
                                await backend.SendMessage(playerName, kingUsername, "petition",
                                    $"{playerName} petitions from prison: \"Please set bail for my release! I await your mercy, Your Majesty.\"");
                            }
                            catch { }
                        }
                    }
                    await terminal.WriteColorLineAsync("  Your petition has been sent to the king.", TerminalEmulator.ColorCyan);
                    await terminal.WriteColorLineAsync("  You must wait for a royal response.", TerminalEmulator.ColorDarkGray);
                }
            }
        }
        else if (choice?.Trim() == "2")
        {
            // Plead for clemency
            if (king.AI == CharacterAI.Computer)
            {
                // NPC king: 20% chance of pardon based on chivalry
                int pardonChance = 10 + (int)(player.Chivalry / 500);
                pardonChance = Math.Clamp(pardonChance, 5, 40);

                await terminal.WriteColorLineAsync("  The king considers your plea for mercy...", TerminalEmulator.ColorWhite);
                await Task.Delay(2000);

                if (Random.Shared.Next(100) < pardonChance)
                {
                    player.DaysInPrison = 0;
                    king.Prisoners.Remove(playerName);
                    if (UsurperRemake.BBS.DoorMode.IsOnlineMode)
                        CastleLocation.PersistRoyalCourtToWorldStateStatic();

                    await terminal.WriteColorLineAsync("  \"Very well. I shall show mercy this once.\"", TerminalEmulator.ColorGreen);
                    await terminal.WriteColorLineAsync("  The king pardons you! You are free!", TerminalEmulator.ColorGreen);
                    NewsSystem.Instance?.Newsy(true, $"The king pardoned {playerName} after a plea for clemency.");
                    await terminal.WaitForKey();
                    return;
                }
                else
                {
                    await terminal.WriteColorLineAsync("  \"Your crimes have not been forgotten.\"", TerminalEmulator.ColorRed);
                    await terminal.WriteColorLineAsync("  The king denies your petition.", TerminalEmulator.ColorRed);
                }
            }
            else
            {
                // Human king — send message
                if (UsurperRemake.BBS.DoorMode.IsOnlineMode)
                {
                    var backend = SaveSystem.Instance.Backend as SqlSaveBackend;
                    if (backend != null)
                    {
                        try
                        {
                            string kingUsername = king.Name.ToLowerInvariant();
                            await backend.SendMessage(playerName, kingUsername, "petition",
                                $"{playerName} pleads from prison: \"I beg for clemency! Please pardon my crimes, Your Majesty.\"");
                        }
                        catch { }
                    }
                }
                await terminal.WriteColorLineAsync("  Your plea for clemency has been sent to the king.", TerminalEmulator.ColorCyan);
                await terminal.WriteColorLineAsync("  You must wait for a royal response.", TerminalEmulator.ColorDarkGray);
            }
        }

        await Task.Delay(2000);
    }

    /// <summary>
    /// Handle prison activity selection - allows prisoners to build stats
    /// </summary>
    private async Task HandleActivities(Character player)
    {
        await terminal.ClearScreenAsync();
        if (IsScreenReader)
        {
            await terminal.WriteColorLineAsync(Loc.Get("prison.activities_header"), TerminalEmulator.ColorCyan);
        }
        else
        {
            await terminal.WriteColorLineAsync("═══════════════════════════════════════", TerminalEmulator.ColorCyan);
            await terminal.WriteColorLineAsync($"           {Loc.Get("prison.activities_header")}           ", TerminalEmulator.ColorCyan);
            await terminal.WriteColorLineAsync("═══════════════════════════════════════", TerminalEmulator.ColorCyan);
        }
        await terminal.WriteLineAsync();
        await terminal.WriteLineAsync(Loc.Get("prison.activities_intro1"));
        await terminal.WriteLineAsync(Loc.Get("prison.activities_intro2"));
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
        await terminal.WriteAsync(Loc.Get("prison.choose_activity"));
        string input = await terminal.ReadLineAsync();

        if (int.TryParse(input, out int choice) && choice >= 1 && choice <= activities.Count)
        {
            var selectedActivity = activities[choice - 1];
            string result = await PrisonActivitySystem.Instance.PerformActivity(player, selectedActivity);

            await terminal.WriteLineAsync();
            await terminal.WriteColorLineAsync(result, TerminalEmulator.ColorGreen);
            await terminal.WriteLineAsync();
            await terminal.WriteAsync(Loc.Get("ui.press_enter"));
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
        await terminal.WriteLineAsync($"=== {Loc.Get("prison.title")} ===");
        await terminal.WriteLineAsync($"{Loc.Get("ui.name_label")}: {player.DisplayName}");
        await terminal.WriteLineAsync($"{Loc.Get("ui.level")}: {player.Level}");
        await terminal.WriteLineAsync($"{Loc.Get("ui.health_label")}: {player.HP}/{player.MaxHP}");
        await terminal.WriteLineAsync(Loc.Get("prison.days_remaining", player.DaysInPrison));
        await terminal.WriteLineAsync(Loc.Get("prison.escape_attempts", player.PrisonEscapes));

        if (player.DaysInPrison == 1)
            await terminal.WriteLineAsync(Loc.Get("prison.released_tomorrow"));
        else
            await terminal.WriteLineAsync(Loc.Get("prison.days_left", player.DaysInPrison));
            
        await terminal.WriteLineAsync();
        await terminal.WriteAsync(Loc.Get("ui.press_enter"));
        await terminal.GetCharAsync();
    }
    
    private async Task<bool> HandleQuitConfirmation(Character player)
    {
        await terminal.WriteLineAsync();
        await terminal.WriteLineAsync();

        bool confirmed = await terminal.ConfirmAsync(Loc.Get("prison.quit_confirm"), false);
        if (!confirmed)
        {
            // Don't quit, continue prison loop
            return false;
        }

        // Player is logging out - display sleep message
        await terminal.WriteLineAsync();
        await terminal.WriteLineAsync(Loc.Get("prison.sleep_hay"));
        await terminal.WriteLineAsync(Loc.Get("prison.long_cold_night"));
        await terminal.WriteLineAsync();
        await Task.Delay(1500);

        // Save and quit - throw game exit exception
        throw new GameExitException("Player logging out from prison");
    }
    
    private async Task HandleOpenCellDoor(Character player)
    {
        await terminal.WriteLineAsync();

        if (player.IsMurderConvict)
        {
            await terminal.WriteColorLineAsync("  Maximum security. The door is sealed with enchanted locks.", TerminalEmulator.ColorRed);
            await Task.Delay(1500);
            return;
        }

        // Check if cell door can be opened (player was rescued)
        if (await CanOpenCellDoor(player))
        {
            await HandleCellDoorOpen(player);
        }
        else
        {
            await terminal.WriteLineAsync();
            await terminal.WriteColorLineAsync(Loc.Get("prison.iron_door"), TerminalEmulator.ColorRed);
            await terminal.WriteColorLineAsync(Loc.Get("prison.trapped"), TerminalEmulator.ColorRed);
            await Task.Delay(1000);
        }
    }
    
    private async Task HandleDemandRelease(Character player)
    {
        if (player.IsMurderConvict)
        {
            await terminal.WriteLineAsync();
            await terminal.WriteColorLineAsync("  The guards laugh. \"Murderers don't make demands.\"", TerminalEmulator.ColorRed);
            await Task.Delay(1500);
            return;
        }

        await terminal.WriteLineAsync();
        await terminal.WriteLineAsync();
        await terminal.WriteColorAsync(Loc.Get("prison.clear_throat"), TerminalEmulator.ColorWhite);
        await terminal.WriteColorLineAsync(Loc.Get("prison.let_me_out"), TerminalEmulator.ColorCyan);

        await Task.Delay(GameConfig.PrisonGuardResponseDelay);
        await terminal.WriteLineAsync();
        await terminal.WriteLineAsync();
        await terminal.WriteLineAsync(Loc.Get("prison.dark_voice"));
        
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
        await terminal.WriteLineAsync(Loc.Get("prison.released_probably"));

        // Send demand message to the king
        if (UsurperRemake.BBS.DoorMode.IsOnlineMode)
        {
            var king = CastleLocation.GetCurrentKing();
            if (king != null)
            {
                var backend = SaveSystem.Instance.Backend as SqlSaveBackend;
                if (backend != null)
                {
                    string playerName = player.DisplayName ?? player.Name2 ?? "";
                    try
                    {
                        await backend.SendMessage(playerName, king.Name, "petition",
                            $"{playerName} demands release from prison: \"LET ME OUT! I demand to be freed at once!\"");
                    }
                    catch { }
                }
                await terminal.WriteColorLineAsync("  Your demands echo through the dungeon halls...", TerminalEmulator.ColorDarkGray);
            }
        }
    }
    
    private async Task<bool> HandleEscapeAttempt(Character player)
    {
        await terminal.WriteLineAsync();

        // Murder convicts cannot escape — maximum security
        if (player.IsMurderConvict)
        {
            await terminal.WriteColorLineAsync("", TerminalEmulator.ColorRed);
            await terminal.WriteColorLineAsync("  You are in MAXIMUM SECURITY for murder.", TerminalEmulator.ColorRed);
            await terminal.WriteColorLineAsync("  There is absolutely no chance of escape.", TerminalEmulator.ColorRed);
            await terminal.WriteColorLineAsync("  You must serve your full sentence.", TerminalEmulator.ColorRed);
            await Task.Delay(2000);
            return false;
        }

        if (player.PrisonEscapes < 1)
        {
            await terminal.WriteLineAsync();
            await terminal.WriteColorLineAsync(Loc.Get("prison.no_escapes"), TerminalEmulator.ColorRed);
            await Task.Delay(1000);
            return false;
        }

        await terminal.WriteLineAsync();
        bool confirmed = await terminal.ConfirmAsync(Loc.Get("prison.jailbreak_confirm"), true);

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
            await terminal.WriteColorLineAsync(Loc.Get("prison.escape_failed"), TerminalEmulator.ColorRed);

            // Generate news about failed escape
            NewsSystem.Instance.Newsy(true, $"{player.DisplayName} failed to escape from the Royal Prison!");

            await terminal.WriteLineAsync(Loc.Get("prison.guards_heard"));
            await terminal.WriteLineAsync(Loc.Get("prison.sentence_extended"));
            if (player.DaysInPrison < 255) player.DaysInPrison++;
            await Task.Delay(1500);
            return false;
        }
        else
        {
            await terminal.WriteColorLineAsync(Loc.Get("prison.escape_success"), TerminalEmulator.ColorGreen);

            // Generate news about successful escape
            NewsSystem.Instance.Newsy(true, $"{player.DisplayName} has escaped from the Royal Prison!");

            await terminal.WriteLineAsync();
            await Task.Delay(1000);

            // Free the player
            player.HP = player.MaxHP;
            player.DaysInPrison = 0;
            player.CellDoorOpen = false;

            await terminal.WriteLineAsync(Loc.Get("prison.escaped_message"));
            await terminal.WriteLineAsync(Loc.Get("prison.free_return"));
            await Task.Delay(1500);

            // Navigate to Main Street
            throw new LocationExitException(GameLocation.MainStreet);
        }
    }
    
    private async Task HandleListPrisoners(Character player)
    {
        await terminal.WriteLineAsync();
        await terminal.WriteLineAsync();
        await terminal.WriteColorLineAsync(Loc.Get("prison.prisoners_header"), TerminalEmulator.ColorWhite);
        await terminal.WriteColorLineAsync("=========", TerminalEmulator.ColorWhite);
        
        // List other prisoners
        var prisoners = await GetOtherPrisoners(player);
        
        if (prisoners.Count == 0)
        {
            await terminal.WriteColorLineAsync(Loc.Get("prison.only_prisoner"), TerminalEmulator.ColorCyan);
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
        await terminal.WriteLineAsync(Loc.Get("prison.days_left_info", player.DaysInPrison));
        
        await terminal.WriteLineAsync();
        await terminal.WriteAsync(Loc.Get("ui.press_enter"));
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
        await terminal.WriteAsync($" {Loc.Get("prison.the_race", GetRaceDisplay(prisoner.Race))}");

        // Show if online/offline/dead
        if (await IsPlayerOnline(prisoner))
        {
            await terminal.WriteColorAsync(Loc.Get("prison.awake"), TerminalEmulator.ColorGreen);
        }
        else if (prisoner.HP < 1)
        {
            await terminal.WriteColorAsync(Loc.Get("prison.dead"), TerminalEmulator.ColorRed);
        }
        else
        {
            await terminal.WriteAsync(Loc.Get("prison.sleeping"));
        }

        // Show days left
        int daysLeft = prisoner.DaysInPrison > 0 ? prisoner.DaysInPrison : 1;
        await terminal.WriteLineAsync(Loc.Get("prison.days_left_parens", daysLeft));
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
            Loc.Get("prison.cmd_menu"),
            Loc.Get("prison.cmd_who"),
            Loc.Get("prison.cmd_demand"),
            Loc.Get("prison.cmd_open"),
            Loc.Get("prison.cmd_escape"),
            Loc.Get("prison.cmd_status"),
            Loc.Get("prison.cmd_quit")
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
        return Task.FromResult(Loc.Get("prison.location_status", daysLeft, player.PrisonEscapes));
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
            await terminal.WriteColorLineAsync(Loc.Get("prison.no_one_unusual"), TerminalEmulator.ColorDarkGray);
            await Task.Delay(1500);
            return false;
        }

        var companionSystem = UsurperRemake.Systems.CompanionSystem.Instance;
        var vex = companionSystem.GetCompanion(UsurperRemake.Systems.CompanionId.Vex);

        await terminal.ClearScreenAsync();
        await terminal.WriteLineAsync();
        WriteBoxHeader(Loc.Get("prison.voice_darkness"), "cyan", 66);
        terminal.WriteLine("");
        await Task.Delay(1000);

        await terminal.WriteLineAsync(Loc.Get("prison.vex_voice"));
        await terminal.WriteLineAsync();
        await Task.Delay(500);

        await terminal.WriteColorLineAsync(Loc.Get("prison.vex_psst1"), TerminalEmulator.ColorYellow);
        await terminal.WriteColorLineAsync(Loc.Get("prison.vex_psst2"), TerminalEmulator.ColorYellow);
        await terminal.WriteLineAsync();
        await Task.Delay(1500);

        await terminal.WriteLineAsync(Loc.Get("prison.vex_peer1"));
        await terminal.WriteLineAsync(Loc.Get("prison.vex_peer2"));
        await terminal.WriteLineAsync(Loc.Get("prison.vex_peer3"));
        await terminal.WriteLineAsync();
        await Task.Delay(1500);

        await terminal.WriteColorAsync($"\"{vex!.DialogueHints[0]}\"", TerminalEmulator.ColorCyan);
        await terminal.WriteLineAsync();
        await terminal.WriteLineAsync();
        await Task.Delay(2000);

        await terminal.WriteLineAsync(Loc.Get("prison.vex_metal"));
        await terminal.WriteLineAsync(Loc.Get("prison.vex_locks1"));
        await terminal.WriteLineAsync(Loc.Get("prison.vex_locks2"));
        await terminal.WriteLineAsync();
        await Task.Delay(1500);

        await terminal.WriteColorAsync($"\"{vex.DialogueHints[1]}\"", TerminalEmulator.ColorCyan);
        await terminal.WriteLineAsync();
        await terminal.WriteLineAsync();
        await Task.Delay(1500);

        // Show his details
        await terminal.WriteColorLineAsync(Loc.Get("prison.vex_intro", vex.Name, vex.Title), TerminalEmulator.ColorYellow);
        await terminal.WriteColorLineAsync(Loc.Get("prison.vex_role", vex.CombatRole), TerminalEmulator.ColorYellow);
        await terminal.WriteColorLineAsync(Loc.Get("prison.vex_abilities", string.Join(", ", vex.Abilities)), TerminalEmulator.ColorYellow);
        await terminal.WriteLineAsync();

        await terminal.WriteColorLineAsync(vex.BackstoryBrief, TerminalEmulator.ColorDarkGray);
        await terminal.WriteLineAsync();
        await Task.Delay(1500);

        await terminal.WriteColorLineAsync(Loc.Get("prison.vex_menu_escape"), TerminalEmulator.ColorGreen);
        await terminal.WriteColorLineAsync(Loc.Get("prison.vex_menu_talk"), TerminalEmulator.ColorCyan);
        await terminal.WriteColorLineAsync(Loc.Get("prison.vex_menu_leave"), TerminalEmulator.ColorDarkGray);
        await terminal.WriteLineAsync();

        await terminal.WriteAsync(Loc.Get("ui.your_choice"));
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
                await terminal.WriteColorLineAsync(Loc.Get("prison.vex_shake_head"), TerminalEmulator.ColorDarkGray);
                await terminal.WriteLineAsync();
                await terminal.WriteColorLineAsync(Loc.Get("prison.vex_your_loss"), TerminalEmulator.ColorYellow);
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
        await terminal.WriteColorLineAsync(Loc.Get("prison.vex_excellent"), TerminalEmulator.ColorYellow);
        await terminal.WriteLineAsync();
        await Task.Delay(1000);

        await terminal.WriteLineAsync(Loc.Get("prison.vex_works_lock"));
        await terminal.WriteLineAsync(Loc.Get("prison.vex_clicks"));
        await terminal.WriteLineAsync();
        await Task.Delay(1500);

        await terminal.WriteLineAsync(Loc.Get("prison.vex_trick1"));
        await terminal.WriteLineAsync(Loc.Get("prison.vex_trick2"));
        await terminal.WriteLineAsync(Loc.Get("prison.vex_trick3"));
        await terminal.WriteLineAsync();
        await Task.Delay(1500);

        await terminal.WriteColorLineAsync(Loc.Get("prison.vex_click_loud"), TerminalEmulator.ColorGreen);
        await terminal.WriteLineAsync();
        await terminal.WriteLineAsync(Loc.Get("prison.vex_door_open"));
        await terminal.WriteLineAsync();
        await Task.Delay(1000);

        await terminal.WriteLineAsync(Loc.Get("prison.vex_smoke"));
        await terminal.WriteColorLineAsync(Loc.Get("prison.vex_sewers1"), TerminalEmulator.ColorYellow);
        await terminal.WriteColorLineAsync(Loc.Get("prison.vex_sewers2"), TerminalEmulator.ColorYellow);
        await terminal.WriteLineAsync();
        await Task.Delay(1500);

        await terminal.WriteLineAsync(Loc.Get("prison.vex_passages1"));
        await terminal.WriteLineAsync(Loc.Get("prison.vex_passages2"));
        await terminal.WriteLineAsync();
        await Task.Delay(1000);

        await terminal.WriteColorLineAsync(Loc.Get("prison.vex_dying1"), TerminalEmulator.ColorCyan);
        await terminal.WriteColorLineAsync(Loc.Get("prison.vex_dying2"), TerminalEmulator.ColorCyan);
        await terminal.WriteColorLineAsync(Loc.Get("prison.vex_dying3"), TerminalEmulator.ColorCyan);
        await terminal.WriteLineAsync();
        await Task.Delay(2000);

        await terminal.WriteLineAsync(Loc.Get("prison.vex_glance1"));
        await terminal.WriteLineAsync(Loc.Get("prison.vex_glance2"));
        await terminal.WriteLineAsync();
        await Task.Delay(1000);

        await terminal.WriteColorLineAsync(Loc.Get("prison.vex_tag_along"), TerminalEmulator.ColorYellow);
        await terminal.WriteLineAsync();
        await Task.Delay(1500);

        // Recruit Vex
        bool success = await companionSystem.RecruitCompanion(
            UsurperRemake.Systems.CompanionId.Vex, player, terminal);

        if (success)
        {
            await terminal.WriteColorLineAsync(Loc.Get("prison.vex_joined", vex.Name), TerminalEmulator.ColorGreen);
            await terminal.WriteLineAsync();
            await terminal.WriteColorLineAsync(Loc.Get("prison.vex_warning_dying"), TerminalEmulator.ColorRed);
            await terminal.WriteColorLineAsync(Loc.Get("prison.vex_make_most"), TerminalEmulator.ColorYellow);
            await terminal.WriteLineAsync();

            // Generate news
            NewsSystem.Instance.Newsy(true, $"{player.DisplayName} escaped from the Royal Prison with {vex.Name}'s help!");
        }

        // Free the player
        player.HP = player.MaxHP;
        player.DaysInPrison = 0;
        player.CellDoorOpen = false;

        await terminal.WriteColorLineAsync(Loc.Get("prison.vex_emerge"), TerminalEmulator.ColorWhite);
        await terminal.WriteColorLineAsync(Loc.Get("prison.vex_free"), TerminalEmulator.ColorGreen);
        await terminal.WriteLineAsync();

        // Mark encounter as complete
        StoryProgressionSystem.Instance.SetStoryFlag("vex_prison_encounter_complete", true);

        await terminal.WriteAsync(Loc.Get("ui.press_enter"));
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
        await terminal.WriteLineAsync(Loc.Get("prison.vex_condition"));
        await terminal.WriteLineAsync();
        await Task.Delay(1000);

        await terminal.WriteColorLineAsync(vex.Description, TerminalEmulator.ColorWhite);
        await terminal.WriteLineAsync();
        await Task.Delay(1500);

        await terminal.WriteColorLineAsync(Loc.Get("prison.vex_born1"), TerminalEmulator.ColorCyan);
        await terminal.WriteColorLineAsync(Loc.Get("prison.vex_born2"), TerminalEmulator.ColorCyan);
        await terminal.WriteColorLineAsync(Loc.Get("prison.vex_born3"), TerminalEmulator.ColorCyan);
        await terminal.WriteLineAsync();
        await Task.Delay(2000);

        await terminal.WriteLineAsync(Loc.Get("prison.vex_lockpick"));
        await terminal.WriteLineAsync(Loc.Get("prison.vex_best_thief1"));
        await terminal.WriteLineAsync(Loc.Get("prison.vex_best_thief2"));
        await terminal.WriteLineAsync(Loc.Get("prison.vex_best_thief3"));
        await terminal.WriteLineAsync();
        await Task.Delay(2000);

        if (!string.IsNullOrEmpty(vex.PersonalQuestDescription))
        {
            await terminal.WriteColorLineAsync(Loc.Get("prison.vex_personal_quest", vex.PersonalQuestName), TerminalEmulator.ColorMagenta);
            await terminal.WriteColorLineAsync($"\"{vex.PersonalQuestDescription}\"", TerminalEmulator.ColorMagenta);
            await terminal.WriteLineAsync();
        }

        await terminal.WriteColorLineAsync(Loc.Get("prison.vex_too_short"), TerminalEmulator.ColorYellow);
        await terminal.WriteLineAsync();
        await Task.Delay(1500);

        await terminal.WriteAsync(Loc.Get("prison.vex_escape_prompt"));
        string answer = await terminal.ReadLineAsync();

        if (answer.ToUpper() == "Y")
        {
            await VexHelpsEscape(player, vex);
        }
        else
        {
            await terminal.WriteLineAsync();
            await terminal.WriteColorLineAsync(Loc.Get("prison.vex_suit_yourself"), TerminalEmulator.ColorYellow);
            await Task.Delay(1500);
        }
    }

    #endregion
} 
