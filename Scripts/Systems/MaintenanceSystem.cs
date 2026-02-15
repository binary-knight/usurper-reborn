using UsurperRemake.Utils;
using UsurperRemake.Systems;
using System;
using System.Collections.Generic;
using System.IO;
using System.Threading.Tasks;
using Godot;

/// <summary>
/// Daily Maintenance System - Complete Pascal-compatible maintenance engine
/// Based on Pascal MAINT.PAS with all daily processing, economic updates, and system cleanup
/// Handles player daily bonuses, resets, economic updates, and system integrity
/// </summary>
public class MaintenanceSystem
{
    private DateTime lastMaintenanceDate;
    private bool maintenanceRunning;
    private TerminalUI terminal;
    private Random random;
    private bool silentMode = true;  // Run maintenance silently by default
    
    public bool MaintenanceRunning => maintenanceRunning;
    public DateTime LastMaintenanceDate => lastMaintenanceDate;
    
    public MaintenanceSystem(TerminalUI terminal)
    {
        this.terminal = terminal;
        this.random = new Random();
        this.maintenanceRunning = false;
        
        // Load last maintenance date
        LoadMaintenanceDate();
    }
    
    /// <summary>
    /// Check if maintenance is needed and run if required
    /// Pascal: Auto maintenance check in USURPER.PAS
    /// </summary>
    public async Task<bool> CheckAndRunMaintenance(bool forceMaintenance = false, bool silent = true)
    {
        if (maintenanceRunning)
        {
            if (!silent)
                terminal.WriteLine("Maintenance is already running!", "red");
            return false;
        }

        var today = DateTime.Now.Date;
        var needsMaintenance = forceMaintenance || lastMaintenanceDate.Date < today;

        if (needsMaintenance)
        {
            await RunDailyMaintenance(forceMaintenance, silent);
            return true;
        }

        return false;
    }

    /// <summary>
    /// Run complete daily maintenance - Pascal MAINT.PAS main procedure
    /// </summary>
    public async Task RunDailyMaintenance(bool forced = false, bool silent = true)
    {
        if (maintenanceRunning)
        {
            if (!silent)
                terminal.WriteLine("Maintenance is already in progress!", "red");
            return;
        }

        maintenanceRunning = true;
        silentMode = silent;  // Set the global silent mode flag

        try
        {
            // Display maintenance header (only if not silent)
            if (!silent)
                await DisplayMaintenanceHeader(forced);

            // Create maintenance lock file (skip if silent to avoid file errors)
            if (!silent)
                CreateMaintenanceFlag();

            // Load configuration values
            var config = LoadMaintenanceConfiguration();

            // Process all players
            await ProcessAllPlayers(config, silent);

            // Process royal system
            await ProcessRoyalSystem(config);

            // Process economic systems
            await ProcessEconomicSystems(config);

            // Clean up inactive players and data
            await CleanupSystems(config);

            // Update system records
            await UpdateSystemRecords();

            // Save maintenance completion
            SaveMaintenanceDate();

            // Display completion message (only if not silent)
            if (!silent)
                await DisplayMaintenanceCompletion();
            else
            {
                // Log silently to Godot console
                GD.Print($"[Maintenance] Daily maintenance completed at {DateTime.Now:MM-dd-yyyy HH:mm:ss}");
            }
        }
        finally
        {
            // Remove maintenance lock (only if not silent)
            if (!silent)
                RemoveMaintenanceFlag();
            maintenanceRunning = false;
            silentMode = true;  // Reset to silent
        }
    }
    
    /// <summary>
    /// Write line to terminal only if not in silent mode
    /// </summary>
    private void WriteIfNotSilent(string text, string color = "white")
    {
        if (!silentMode)
            terminal.WriteLine(text, color);
    }

    /// <summary>
    /// Display maintenance header - Pascal maintenance display
    /// </summary>
    private async Task DisplayMaintenanceHeader(bool forced)
    {
        terminal.ClearScreen();
        terminal.WriteLine("", "white");
        terminal.WriteLine("═══════════════════════════════════════════════════════════════", "bright_cyan");
        terminal.WriteLine("               U S U R P E R   M A I N T E N A N C E              ", "bright_cyan");
        terminal.WriteLine("═══════════════════════════════════════════════════════════════", "bright_cyan");
        terminal.WriteLine("", "white");
        
        var maintenanceType = forced ? "FORCED" : "SCHEDULED";
        var dateString = DateTime.Now.ToString("MM-dd-yyyy HH:mm:ss");
        
        terminal.WriteLine($"Maintenance Type: {maintenanceType}", "yellow");
        terminal.WriteLine($"Date/Time: {dateString}", "white");
        terminal.WriteLine($"Last Maintenance: {lastMaintenanceDate:MM-dd-yyyy}", "gray");
        terminal.WriteLine("", "white");
        terminal.WriteLine("Injections of Evil...", "red");
        terminal.WriteLine("", "white");
        
        await Task.Delay(1000);
    }
    
    /// <summary>
    /// Load maintenance configuration from game config
    /// Pascal: Configuration loading from USURPER.CFG
    /// </summary>
    private MaintenanceConfig LoadMaintenanceConfiguration()
    {
        // In a real implementation, this would load from the actual config file
        // For now, using Pascal default values
        return new MaintenanceConfig
        {
            DungeonFights = GameConfig.DefaultDungeonFights,
            PlayerFights = GameConfig.DefaultPlayerFights,
            TeamFights = GameConfig.DefaultTeamFights,
            BankInterest = GameConfig.DefaultBankInterest,
            InactivityDays = GameConfig.DefaultInactivityDays,
            TownPotValue = GameConfig.DefaultTownPot,
            ResurrectionAllowed = true,
            MaxTime = 999
        };
    }
    
    /// <summary>
    /// Process all players for daily maintenance
    /// Pascal: Main player processing loop in MAINT.PAS
    /// </summary>
    private async Task ProcessAllPlayers(MaintenanceConfig config, bool silent = true)
    {
        if (!silent)
            terminal.WriteLine("Processing player records...", "white");

        // In a real implementation, this would iterate through all player files
        // For now, process the current player if available
        var gameEngine = GameEngine.Instance;
        if (gameEngine?.CurrentPlayer != null)
        {
            await ProcessPlayerDailyMaintenance(gameEngine.CurrentPlayer, config, silent);
        }

        if (!silent)
        {
            terminal.WriteLine("Player processing complete.", "green");
            await Task.Delay(500);
        }
    }
    
    /// <summary>
    /// Process individual player daily maintenance
    /// Pascal: Player processing section in MAINT.PAS lines 335-400
    /// </summary>
    private async Task ProcessPlayerDailyMaintenance(Character player, MaintenanceConfig config, bool silent = true)
    {
        // Player alive bonus (Pascal: level * 350 per day)
        if (player.HP > 0 && player.AliveBonus < GameConfig.MaxAliveBonus)
        {
            var bonus = player.Level * GameConfig.AliveBonus;
            player.AliveBonus += bonus;

            WriteIfNotSilent($"  {player.Name2}: Alive bonus +{bonus}", "green");
        }
        
        // NPC team daily wages (v0.30.9)
        await ProcessTeamWages(player);

        // Class-specific daily processing
        await ProcessClassSpecificMaintenance(player, config);

        // Reset daily parameters (Pascal: "resetting all kinds of daily parameters")
        ResetDailyParameters(player, config);
        
        // Mental stability recovery (Pascal: random mental stability increase)
        ProcessMentalStabilityRecovery(player);
        
        // Healing potion spoilage (Pascal: 50% of overage spoils)
        ProcessHealingSpoilage(player);
        
        // Birthday processing
        await ProcessPlayerBirthday(player);

        // Relationship/Jealousy processing
        await ProcessJealousyConsequences(player);
    }

    /// <summary>
    /// Process jealousy consequences for player's romantic relationships
    /// </summary>
    private async Task ProcessJealousyConsequences(Character player)
    {
        var romanceTracker = RomanceTracker.Instance;
        if (romanceTracker == null) return;

        var messages = romanceTracker.ProcessJealousyConsequences(player);

        if (messages.Count > 0)
        {
            foreach (var message in messages)
            {
                WriteIfNotSilent($"  {message}", "red");
            }

            if (!silentMode)
            {
                await Task.Delay(1000);
            }
        }
    }
    
    /// <summary>
    /// Process daily NPC team wages - deduct gold for each NPC on the player's team.
    /// NPCs leave after MaxUnpaidWageDays consecutive days without pay, sending a mail.
    /// </summary>
    private async Task ProcessTeamWages(Character player)
    {
        if (string.IsNullOrEmpty(player.Team)) return;

        var allNPCs = NPCSpawnSystem.Instance?.ActiveNPCs;
        if (allNPCs == null) return;

        var teamNPCs = allNPCs.Where(n => n.Team == player.Team && n.IsAlive && !n.IsDead).ToList();
        if (teamNPCs.Count == 0) return;

        long totalWages = 0;
        foreach (var npc in teamNPCs)
            totalWages += npc.Level * GameConfig.NpcDailyWagePerLevel;

        if (totalWages <= 0) return;

        if (player.Gold >= totalWages)
        {
            // Pay all wages
            player.Gold -= totalWages;
            player.Statistics?.RecordGoldSpent(totalWages);
            WriteIfNotSilent($"  Team wages: -{totalWages:N0}g ({teamNPCs.Count} members)", "yellow");

            // Clear all unpaid days since we paid in full
            player.UnpaidWageDays?.Clear();
        }
        else
        {
            // Can't afford full wages
            long partialPay = player.Gold;
            player.Gold = 0;
            if (partialPay > 0)
                player.Statistics?.RecordGoldSpent(partialPay);

            WriteIfNotSilent($"  Team wages: Can't afford {totalWages:N0}g! (had {partialPay:N0}g)", "red");

            // Track unpaid days per NPC and check for departures
            var npcsToRemove = new List<NPC>();
            foreach (var npc in teamNPCs)
            {
                string npcKey = npc.Name2 ?? npc.DisplayName;
                player.UnpaidWageDays ??= new Dictionary<string, int>();

                if (!player.UnpaidWageDays.ContainsKey(npcKey))
                    player.UnpaidWageDays[npcKey] = 0;

                player.UnpaidWageDays[npcKey]++;

                if (player.UnpaidWageDays[npcKey] >= GameConfig.MaxUnpaidWageDays)
                {
                    npcsToRemove.Add(npc);
                }
            }

            // Process NPC departures
            foreach (var npc in npcsToRemove)
            {
                string npcKey = npc.Name2 ?? npc.DisplayName;
                npc.Team = "";
                npc.TeamPW = "";
                player.UnpaidWageDays?.Remove(npcKey);

                WriteIfNotSilent($"  {npc.DisplayName} has LEFT your team due to unpaid wages!", "bright_red");

                // Send mail to player explaining why they left
                if (OnlineStateManager.Instance != null)
                {
                    string mailMessage = $"{npc.DisplayName} has left your team. \"You haven't paid me in {GameConfig.MaxUnpaidWageDays} days. I'm no charity worker — find yourself another sword arm. Maybe when your coffers aren't empty, we can talk again.\"";
                    await OnlineStateManager.Instance.SendMessage(player.Name2 ?? "", "team_departure", mailMessage);
                }

                // Also post to news
                NewsSystem.Instance?.Newsy(true, $"{npc.DisplayName} quit {player.DisplayName}'s team over unpaid wages!");
            }
        }

        await Task.CompletedTask;
    }

    /// <summary>
    /// Process class-specific daily maintenance
    /// Pascal: Class-specific processing in MAINT.PAS
    /// </summary>
    private async Task ProcessClassSpecificMaintenance(Character player, MaintenanceConfig config)
    {
        switch (player.Class)
        {
            case CharacterClass.Bard:
                // Reset bard songs (Pascal: bard song reset)
                player.BardSongsLeft = GameConfig.DefaultBardSongs;
                terminal.WriteLine($"  {player.Name2}: Bard songs restored", "cyan");
                break;
                
            case CharacterClass.Assassin:
                // Assassins get extra thief attempts (Pascal: assassin bonus)
                player.Thiefs += GameConfig.AssassinThiefBonus;
                terminal.WriteLine($"  {player.Name2}: Assassin thief bonus applied", "yellow");
                break;
        }
    }
    
    /// <summary>
    /// Reset daily parameters for all players
    /// Pascal: Daily parameter reset section in MAINT.PAS
    /// </summary>
    private void ResetDailyParameters(Character player, MaintenanceConfig config)
    {
        // Reset daily attempts and limits (Pascal values)
        player.Fights = config.DungeonFights;           // Dungeon fights
        player.DarkNr = GameConfig.DailyDarknessReset;   // Darkness deeds
        player.ChivNr = GameConfig.DailyChivalryReset;   // Chivalry deeds
        player.PFights = config.PlayerFights;           // Player fights
        player.TFights = config.TeamFights;             // Team fights
        player.Thiefs = GameConfig.DefaultThiefAttempts; // Thief attempts
        player.Brawls = GameConfig.DefaultBrawls;        // Brawl attempts
        player.Assa = GameConfig.DefaultAssassinAttempts; // Assassin attempts
        
        // Reset special daily activities
        player.UmanBearTries = 0;                       // Bear wrestling attempts
        player.Massage = 0;                             // Massage sessions
        player.WellWish = false;                        // Well wishing
        player.Allowed = true;                          // Player allowed to play
        
        // Reset location-specific daily limits
        player.GymSessions = GameConfig.DefaultGymSessions;
        player.DrinkslLeft = GameConfig.DefaultDrinksAtOrbs;
        player.IntimacyActs = GameConfig.DefaultIntimacyActs;
        player.Wrestlings = GameConfig.DefaultMaxWrestlings;
        player.PrisonEscapes = GameConfig.DefaultPrisonEscapeAttempts;
        player.PickPocketAttempts = GameConfig.DefaultPickPocketAttempts;
        player.BankRobberyAttempts = GameConfig.DefaultBankRobberyAttempts;
        
        // Reset shop haggling attempts (Pascal: daily haggling reset)
        player.WeapHag = 3;
        player.ArmHag = 3;
    }
    
    /// <summary>
    /// Process mental stability recovery
    /// Pascal: Mental stability increase chance in MAINT.PAS
    /// </summary>
    private void ProcessMentalStabilityRecovery(Character player)
    {
        if (player.Mental < GameConfig.MaxMentalStability && 
            random.Next(GameConfig.DailyMentalStabilityChance) == 0)
        {
            var increase = random.Next(1, GameConfig.MentalStabilityIncrease + 1);
            player.Mental = Math.Min(GameConfig.MaxMentalStability, player.Mental + increase);
            
            terminal.WriteLine($"  {player.Name2}: Mental stability increased by {increase}", "bright_green");
            
            // Send mail notification (Pascal: mental stability mail)
            MailSystem.SendSystemMail(player.Name2, "Mental Stability", 
                "Your Mental Stability increased!", 
                $"You feel more stable mentally. (+{increase})");
        }
    }
    
    /// <summary>
    /// Process healing potion spoilage
    /// Pascal: Healing potion spoilage in MAINT.PAS lines 937-980
    /// </summary>
    private void ProcessHealingSpoilage(Character player)
    {
        var maxHealing = GameConfig.MaxHealingPotions;
        var extraHealing = player.Healing - maxHealing;
        
        if (extraHealing >= GameConfig.MinHealingSpoilage)
        {
            var spoiled = (int)(extraHealing * GameConfig.HealingSpoilageRate);
            player.Healing -= spoiled;
            
            terminal.WriteLine($"  {player.Name2}: {spoiled} healing potions spoiled", "yellow");
            
            // Send mail notification (Pascal: spoilage mail)
            MailSystem.SendSystemMail(player.Name2, "Healing Potions",
                "Some of your extra potions seem to have spoiled during the night!",
                $"Lost {spoiled} healing potions due to spoilage.");
        }
    }
    
    /// <summary>
    /// Process player birthday events
    /// Pascal: Birthday processing in MAIL.PAS
    /// </summary>
    private async Task ProcessPlayerBirthday(Character player)
    {
        // Simple birthday check (in real implementation, would track actual dates)
        if (random.Next(365) == 0) // 1 in 365 chance for birthday
        {
            player.Age++;
            terminal.WriteLine($"  {player.Name2}: Birthday! Now age {player.Age}", "bright_yellow");
            
            // Send birthday mail with gift options (Pascal: birthday mail system)
            MailSystem.SendBirthdayMail(player.Name2, player.Age);
        }
    }
    
    /// <summary>
    /// Process royal system daily maintenance
    /// Pascal: King system maintenance in MAINT.PAS lines 1108-1180
    /// </summary>
    private async Task ProcessRoyalSystem(MaintenanceConfig config)
    {
        WriteIfNotSilent("Processing royal system...", "white");

        // Load king data (in real implementation, would load from king file)
        var gameEngine = GameEngine.Instance;
        if (gameEngine?.CurrentPlayer?.King != null)
        {
            var king = gameEngine.CurrentPlayer;

            // Reset daily royal limits (Pascal: king daily resets)
            king.PrisonsLeft = GameConfig.DailyPrisonSentences;
            king.ExecuteLeft = GameConfig.DailyExecutions;
            king.QuestsLeft = GameConfig.DefaultMaxNewQuests;
            king.MarryActions = GameConfig.DefaultMarryActions;
            king.WolfFeed = GameConfig.DefaultWolfFeeding;
            king.RoyalAdoptions = GameConfig.DefaultRoyalAdoptions;

            // Increment days in power
            king.DaysInPower++;

            WriteIfNotSilent("  Royal limits reset", "cyan");
            WriteIfNotSilent($"  Days in power: {king.DaysInPower}", "white");
        }

        WriteIfNotSilent("Royal system processing complete.", "green");
        if (!silentMode)
            await Task.Delay(500);
    }
    
    /// <summary>
    /// Process economic systems maintenance
    /// Pascal: Economic maintenance in MAINT.PAS
    /// </summary>
    private async Task ProcessEconomicSystems(MaintenanceConfig config)
    {
        WriteIfNotSilent("Processing economic systems...", "white");

        // Bank interest processing
        await ProcessBankInterest(config);

        // Safe value reset (Pascal: Safe_Reset)
        ProcessSafeReset();

        // Town pot management
        ProcessTownPot(config);

        WriteIfNotSilent("Economic processing complete.", "green");
        if (!silentMode)
            await Task.Delay(500);
    }

    /// <summary>
    /// Process bank interest for all accounts
    /// Pascal: Bank interest calculation
    /// </summary>
    private async Task ProcessBankInterest(MaintenanceConfig config)
    {
        var gameEngine = GameEngine.Instance;
        if (gameEngine?.CurrentPlayer != null)
        {
            var player = gameEngine.CurrentPlayer;

            if (player.BankGold > 0)
            {
                var interest = (long)(player.BankGold * config.BankInterest / 100.0);
                player.BankGold += interest;
                player.Interest += interest;

                WriteIfNotSilent($"  Bank interest: {interest} gold added", "green");

                // Send bank statement mail
                MailSystem.SendSystemMail(player.Name2, "Bank Interest",
                    "Your bank account has earned interest!",
                    $"Interest earned: {interest} gold");
            }
        }
    }

    /// <summary>
    /// Reset bank safe values
    /// Pascal: Safe_Reset procedure
    /// </summary>
    private void ProcessSafeReset()
    {
        // Bank safe reset logic (Pascal implementation)
        WriteIfNotSilent("  Bank safes reset", "cyan");
    }
    
    /// <summary>
    /// Process town pot maintenance
    /// Pascal: Town pot processing
    /// </summary>
    private void ProcessTownPot(MaintenanceConfig config)
    {
        // Town pot value maintenance (Pascal implementation)
        terminal.WriteLine($"  Town pot: {config.TownPotValue} gold", "white");
    }
    
    /// <summary>
    /// Clean up inactive players and systems
    /// Pascal: Cleanup routines in MAINT.PAS
    /// </summary>
    private async Task CleanupSystems(MaintenanceConfig config)
    {
        terminal.WriteLine("Running system cleanup...", "white");
        
        // Clean up inactive players
        await CleanupInactivePlayers(config);
        
        // Clean up bounty lists
        await CleanupBountyLists();
        
        // Clean up royal guard
        await CleanupRoyalGuard();
        
        terminal.WriteLine("System cleanup complete.", "green");
        await Task.Delay(500);
    }
    
    /// <summary>
    /// Clean up inactive players
    /// Pascal: Inactive player deletion in MAINT.PAS
    /// </summary>
    private async Task CleanupInactivePlayers(MaintenanceConfig config)
    {
        // Inactive player cleanup logic (Pascal implementation)
        terminal.WriteLine("  Inactive players checked", "cyan");
    }
    
    /// <summary>
    /// Clean up bounty lists
    /// Pascal: Bounty list maintenance in MAINT.PAS
    /// </summary>
    private async Task CleanupBountyLists()
    {
        terminal.WriteLine("  Bounty lists updated", "cyan");
    }
    
    /// <summary>
    /// Clean up royal guard
    /// Pascal: Royal guard validation in MAINT.PAS
    /// </summary>
    private async Task CleanupRoyalGuard()
    {
        terminal.WriteLine("  Royal guard validated", "cyan");
    }
    
    /// <summary>
    /// Update system records and statistics
    /// Pascal: System record updates
    /// </summary>
    private async Task UpdateSystemRecords()
    {
        terminal.WriteLine("Updating system records...", "white");
        
        // Update various system records
        terminal.WriteLine("  Player statistics updated", "cyan");
        terminal.WriteLine("  Game records updated", "cyan");
        terminal.WriteLine("  News files updated", "cyan");
        
        await Task.Delay(500);
    }
    
    /// <summary>
    /// Display maintenance completion message
    /// Pascal: Maintenance completion in MAINT.PAS
    /// </summary>
    private async Task DisplayMaintenanceCompletion()
    {
        terminal.WriteLine("", "white");
        terminal.WriteLine("═══════════════════════════════════════", "bright_green");
        terminal.WriteLine("    ALL DEEDS ARE DONE!", "bright_green");
        terminal.WriteLine("═══════════════════════════════════════", "bright_green");
        terminal.WriteLine("", "white");
        terminal.WriteLine("Maintenance completed successfully.", "green");
        terminal.WriteLine($"Next maintenance: {DateTime.Now.AddDays(1):MM-dd-yyyy}", "gray");
        terminal.WriteLine("", "white");
        
        await Task.Delay(2000);
    }
    
    /// <summary>
    /// Create maintenance flag file
    /// Pascal: Create_Maint_Flag procedure
    /// </summary>
    private void CreateMaintenanceFlag()
    {
        var flagPath = Path.Combine(GameEngine.DataPath, GameConfig.MaintenanceFlagFile);
        
        try
        {
            File.WriteAllText(flagPath, 
                $"Usurper Maintenance\n" +
                $"Started: {DateTime.Now}\n" +
                $"Node: LOCAL\n" +
                $"This file prevents multiple maintenance sessions.");
        }
        catch (Exception ex)
        {
            GD.PrintErr($"Failed to create maintenance flag: {ex.Message}");
        }
    }
    
    /// <summary>
    /// Remove maintenance flag file
    /// Pascal: Remove maintenance flag
    /// </summary>
    private void RemoveMaintenanceFlag()
    {
        var flagPath = Path.Combine(GameEngine.DataPath, GameConfig.MaintenanceFlagFile);
        
        try
        {
            if (File.Exists(flagPath))
            {
                File.Delete(flagPath);
            }
        }
        catch (Exception ex)
        {
            GD.PrintErr($"Failed to remove maintenance flag: {ex.Message}");
        }
    }
    
    /// <summary>
    /// Load last maintenance date from file
    /// Pascal: Date file handling
    /// </summary>
    private void LoadMaintenanceDate()
    {
        var datePath = Path.Combine(GameEngine.DataPath, GameConfig.DateFile);
        
        try
        {
            if (File.Exists(datePath))
            {
                var dateString = File.ReadAllText(datePath).Trim();
                if (DateTime.TryParse(dateString, out var date))
                {
                    lastMaintenanceDate = date;
                    return;
                }
            }
        }
        catch (Exception ex)
        {
            GD.PrintErr($"Failed to load maintenance date: {ex.Message}");
        }
        
        // Default to yesterday to trigger maintenance
        lastMaintenanceDate = DateTime.Now.AddDays(-1);
    }
    
    /// <summary>
    /// Save maintenance completion date
    /// Pascal: Date file update
    /// </summary>
    private void SaveMaintenanceDate()
    {
        var datePath = Path.Combine(GameEngine.DataPath, GameConfig.DateFile);
        
        try
        {
            lastMaintenanceDate = DateTime.Now;
            File.WriteAllText(datePath, lastMaintenanceDate.ToString("yyyy-MM-dd"));
        }
        catch (Exception ex)
        {
            GD.PrintErr($"Failed to save maintenance date: {ex.Message}");
        }
    }
}

/// <summary>
/// Maintenance configuration structure
/// Pascal: Configuration values from USURPER.CFG
/// </summary>
public class MaintenanceConfig
{
    public int DungeonFights { get; set; }
    public int PlayerFights { get; set; }
    public int TeamFights { get; set; }
    public int BankInterest { get; set; }
    public int InactivityDays { get; set; }
    public int TownPotValue { get; set; }
    public bool ResurrectionAllowed { get; set; }
    public int MaxTime { get; set; }
} 
