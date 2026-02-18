using UsurperRemake.Utils;
using UsurperRemake.Systems;
using UsurperRemake.BBS;
using System;
using System.Threading.Tasks;
using Godot;

/// <summary>
/// Modern Daily System Manager - Flexible daily reset system for Steam single-player experience
/// Supports multiple daily cycle modes and integrates with comprehensive save system
/// </summary>
public class DailySystemManager
{
    private static DailySystemManager? instance;
    public static DailySystemManager Instance => instance ??= new DailySystemManager();
    
    private DateTime lastResetTime;
    private DateTime gameStartTime;
    private int currentDay = 1;
    private DailyCycleMode currentMode = DailyCycleMode.Endless;
    private MaintenanceSystem? maintenanceSystem;
    private TerminalUI? terminal;
    
    // Auto-save functionality
    private DateTime lastAutoSave;
    private TimeSpan autoSaveInterval = TimeSpan.FromMinutes(5);
    private bool autoSaveEnabled = true;
    
    public int CurrentDay => currentDay;
    public DailyCycleMode CurrentMode => currentMode;
    public DateTime LastResetTime => lastResetTime;
    public bool AutoSaveEnabled => autoSaveEnabled;
    
    public DailySystemManager()
    {
        gameStartTime = DateTime.Now;
        lastResetTime = DateTime.Now;
        lastAutoSave = DateTime.Now;
        
        // Initialize with terminal from GameEngine when available
        var gameEngine = GameEngine.Instance;
        terminal = gameEngine?.Terminal;
        
        if (terminal != null)
        {
            maintenanceSystem = new MaintenanceSystem(terminal);
        }
    }
    
    /// <summary>
    /// Set the daily cycle mode
    /// </summary>
    public void SetDailyCycleMode(DailyCycleMode mode)
    {
        if (currentMode != mode)
        {
            var oldMode = currentMode;
            currentMode = mode;
            
            terminal?.WriteLine($"Daily cycle mode changed from {oldMode} to {mode}", "bright_cyan");
            
            // Adjust reset time based on new mode
            AdjustResetTimeForMode();
        }
    }
    
    /// <summary>
    /// Get the most recent 7 PM Eastern Time boundary as UTC.
    /// This is the authoritative daily reset point for online mode.
    /// </summary>
    public static DateTime GetCurrentResetBoundary()
    {
        var eastern = TimeZoneInfo.FindSystemTimeZoneById("America/New_York");
        var nowEastern = TimeZoneInfo.ConvertTimeFromUtc(DateTime.UtcNow, eastern);
        var resetToday = nowEastern.Date.AddHours(GameConfig.DailyResetHourEastern);
        if (nowEastern < resetToday)
            resetToday = resetToday.AddDays(-1); // Haven't hit 7 PM yet, use yesterday's
        return TimeZoneInfo.ConvertTimeToUtc(resetToday, eastern);
    }

    /// <summary>
    /// Check if a daily reset should occur based on current mode
    /// </summary>
    public bool ShouldPerformDailyReset()
    {
        var player = GameEngine.Instance?.CurrentPlayer;

        // Online mode: reset at 7 PM Eastern Time
        if (DoorMode.IsOnlineMode)
        {
            if (player == null) return false;
            return player.LastDailyResetBoundary < GetCurrentResetBoundary();
        }

        return currentMode switch
        {
            DailyCycleMode.SessionBased => player?.TurnsRemaining <= 0,
            DailyCycleMode.RealTime24Hour => DateTime.Now.Date > lastResetTime.Date,
            DailyCycleMode.Accelerated4Hour => DateTime.Now - lastResetTime >= TimeSpan.FromHours(4),
            DailyCycleMode.Accelerated8Hour => DateTime.Now - lastResetTime >= TimeSpan.FromHours(8),
            DailyCycleMode.Accelerated12Hour => DateTime.Now - lastResetTime >= TimeSpan.FromHours(12),
            DailyCycleMode.Endless => false, // Never reset in endless mode
            _ => false
        };
    }
    
    /// <summary>
    /// Check and run daily reset if needed (called periodically)
    /// </summary>
    public async Task CheckDailyReset()
    {
        if (ShouldPerformDailyReset())
        {
            await PerformDailyReset();
        }
        
        // Check for auto-save
        if (autoSaveEnabled && DateTime.Now - lastAutoSave >= autoSaveInterval)
        {
            await PerformAutoSave();
        }
    }
    
    /// <summary>
    /// Force daily reset to run immediately
    /// </summary>
    public async Task ForceDailyReset()
    {
        await PerformDailyReset(forced: true);
    }
    
    /// <summary>
    /// Perform daily reset based on current mode
    /// </summary>
    private async Task PerformDailyReset(bool forced = false)
    {
        var player = GameEngine.Instance?.CurrentPlayer;
        if (player == null) return;
        
        // Don't reset in endless mode unless forced (single-player only)
        if (!DoorMode.IsOnlineMode && currentMode == DailyCycleMode.Endless && !forced) return;

        // In online mode, record when this reset boundary was applied
        if (DoorMode.IsOnlineMode)
            player.LastDailyResetBoundary = DateTime.UtcNow;

        // Increment day counter
        currentDay++;
        lastResetTime = DateTime.Now;

        // Also update the per-session day on the GameEngine (safe from MUD cross-contamination)
        var engine = GameEngine.Instance;
        if (engine != null)
        {
            engine.SessionCurrentDay = currentDay;
            engine.SessionLastResetTime = lastResetTime;
        }

        // Sync StoryProgressionSystem's game day counter (used for Vex death tracking, etc.)
        try
        {
            StoryProgressionSystem.Instance.CurrentGameDay = currentDay;
        }
        catch { /* StoryProgressionSystem not initialized */ }

        // Log the daily reset
        DebugLogger.Instance.LogDailyReset(currentDay);

        // Display reset message
        await DisplayDailyResetMessage();
        
        // Use MaintenanceSystem for complete Pascal-compatible maintenance
        if (maintenanceSystem != null)
        {
            var maintenanceRan = await maintenanceSystem.CheckAndRunMaintenance(forced);
            
            if (!maintenanceRan)
            {
                // Run basic daily reset if maintenance wasn't needed
                await RunBasicDailyReset();
            }
        }
        else
        {
            // Fallback to basic daily reset
            await RunBasicDailyReset();
        }
        
        // Process mode-specific resets
        await ProcessModeSpecificReset();
        
        // Clean up old mail
        MailSystem.CleanupOldMail();
        
        // Auto-save after reset
        if (autoSaveEnabled)
        {
            await SaveSystem.Instance.AutoSave(player);
        }
        
        GD.Print($"[DailySystem] Day {currentDay} reset completed at {DateTime.Now} (Mode: {currentMode})");
    }
    
    /// <summary>
    /// Display daily reset message based on mode
    /// </summary>
    private async Task DisplayDailyResetMessage()
    {
        if (terminal == null) return;
        
        terminal.WriteLine("", "white");
        terminal.WriteLine("═══════════════════════════════════════", "bright_blue");
        
        var message = currentMode switch
        {
            DailyCycleMode.SessionBased => $"        NEW SESSION BEGINS! (Day {currentDay})",
            DailyCycleMode.RealTime24Hour => $"        NEW DAY DAWNS! (Day {currentDay})",
            DailyCycleMode.Accelerated4Hour => $"        TIME ADVANCES! (Day {currentDay})",
            DailyCycleMode.Accelerated8Hour => $"        TIME ADVANCES! (Day {currentDay})",
            DailyCycleMode.Accelerated12Hour => $"        TIME ADVANCES! (Day {currentDay})",
            DailyCycleMode.Endless => $"        ENDLESS ADVENTURE CONTINUES! (Day {currentDay})",
            _ => $"        DAY {currentDay} BEGINS!"
        };
        
        terminal.WriteLine(message, "bright_yellow");
        terminal.WriteLine("═══════════════════════════════════════", "bright_blue");
        terminal.WriteLine("", "white");
        
        // Mode-specific flavor text
        var flavorText = currentMode switch
        {
            DailyCycleMode.SessionBased => "Your strength and resolve have been restored!",
            DailyCycleMode.RealTime24Hour => "The sun rises on a new day of adventure!",
            DailyCycleMode.Accelerated4Hour => "Time flows swiftly in this realm!",
            DailyCycleMode.Accelerated8Hour => "The hours pass quickly here!",
            DailyCycleMode.Accelerated12Hour => "Day and night cycle rapidly!",
            DailyCycleMode.Endless => "Time has no meaning in your endless quest!",
            _ => "A new day of adventure awaits!"
        };
        
        terminal.WriteLine(flavorText, "cyan");
        terminal.WriteLine("", "white");
        
        await Task.Delay(1000);
    }
    
    /// <summary>
    /// Run basic daily reset when full maintenance isn't available
    /// </summary>
    private async Task RunBasicDailyReset()
    {
        var player = GameEngine.Instance?.CurrentPlayer;
        if (player == null) return;
        
        // Reset daily parameters based on mode
        if (currentMode != DailyCycleMode.Endless)
        {
            // Restore turns based on mode
            var turnsToRestore = currentMode switch
            {
                DailyCycleMode.SessionBased => GameConfig.TurnsPerDay,
                DailyCycleMode.RealTime24Hour => GameConfig.TurnsPerDay,
                DailyCycleMode.Accelerated4Hour => GameConfig.TurnsPerDay / 6, // Reduced for faster cycles
                DailyCycleMode.Accelerated8Hour => GameConfig.TurnsPerDay / 3,
                DailyCycleMode.Accelerated12Hour => GameConfig.TurnsPerDay / 2,
                _ => GameConfig.TurnsPerDay
            };
            
            player.TurnsRemaining = turnsToRestore;
            
            // Reset daily attempt counters
            player.Fights = GameConfig.DefaultDungeonFights;
            player.PFights = GameConfig.DefaultPlayerFights;
            player.TFights = GameConfig.DefaultTeamFights;
            player.Thiefs = GameConfig.DefaultThiefAttempts;
            player.Brawls = GameConfig.DefaultBrawls;
            player.Assa = GameConfig.DefaultAssassinAttempts;
            
            // Reset class daily abilities
            player.IsRaging = false; // Rage refreshed each combat anyway, but ensure off
            if (player.Class == CharacterClass.Paladin)
            {
                var mods = player.GetClassCombatModifiers();
                player.SmiteChargesRemaining = mods.SmiteCharges;
            }
            else
            {
                player.SmiteChargesRemaining = 0;
            }
            
            // Reset haggling attempts
            HagglingEngine.ResetDailyHaggling(player);

            // Reset Dark Alley daily counters (v0.41.0)
            player.GamblingRoundsToday = 0;
            player.PitFightsToday = 0;

            // Reset real-world-date daily tracking (online mode persistence)
            player.SethFightsToday = 0;
            player.ArmWrestlesToday = 0;
            player.RoyQuestsToday = 0;
            player.LastPrayerRealDate = DateTime.MinValue;
            player.LastInnerSanctumRealDate = DateTime.MinValue;
            player.LastBindingOfSoulsRealDate = DateTime.MinValue;

            terminal?.WriteLine($"Your daily limits have been restored! ({turnsToRestore} turns)", "bright_green");
        }
        else
        {
            // In endless mode, just give a small turn boost if needed
            if (player.TurnsRemaining < 50)
            {
                player.TurnsRemaining += 25;
                terminal?.WriteLine("Your energy has been partially restored!", "bright_green");
            }
        }
        
        // Process daily events
        // In online mode, only process player-specific events (WorldSimService handles king/world)
        if (DoorMode.IsOnlineMode)
            await ProcessPlayerDailyEvents();
        else
            await ProcessDailyEvents();

        // Process bank maintenance
        BankLocation.ProcessDailyMaintenance(player);
    }
    
    /// <summary>
    /// Process mode-specific reset logic
    /// </summary>
    private async Task ProcessModeSpecificReset()
    {
        switch (currentMode)
        {
            case DailyCycleMode.SessionBased:
                await ProcessSessionBasedReset();
                break;
                
            case DailyCycleMode.RealTime24Hour:
                await ProcessRealTimeReset();
                break;
                
            case DailyCycleMode.Accelerated4Hour:
            case DailyCycleMode.Accelerated8Hour:
            case DailyCycleMode.Accelerated12Hour:
                await ProcessAcceleratedReset();
                break;
                
            case DailyCycleMode.Endless:
                await ProcessEndlessReset();
                break;
        }
    }
    
    private async Task ProcessSessionBasedReset()
    {
        terminal?.WriteLine("Session-based reset: Ready for a new adventure session!", "bright_cyan");
        
        // Process NPCs during player absence (minimal)
        await ProcessNPCsDuringAbsence(TimeSpan.FromHours(1)); // Assume 1 hour offline
    }
    
    private async Task ProcessRealTimeReset()
    {
        var timeSinceLastReset = DateTime.Now - lastResetTime;
        terminal?.WriteLine($"Real-time reset: {timeSinceLastReset.TotalHours:F1} hours have passed!", "bright_cyan");
        
        // Process NPCs during real-time absence
        await ProcessNPCsDuringAbsence(timeSinceLastReset);
        
        // Process world events that occurred during absence
        await ProcessWorldEventsDuringAbsence(timeSinceLastReset);
    }
    
    private async Task ProcessAcceleratedReset()
    {
        var cycleName = currentMode switch
        {
            DailyCycleMode.Accelerated4Hour => "4-hour",
            DailyCycleMode.Accelerated8Hour => "8-hour",
            DailyCycleMode.Accelerated12Hour => "12-hour",
            _ => "accelerated"
        };
        
        terminal?.WriteLine($"Accelerated reset: {cycleName} cycle completed!", "bright_cyan");
        
        // Process accelerated world simulation
        var simulatedTime = currentMode switch
        {
            DailyCycleMode.Accelerated4Hour => TimeSpan.FromHours(4),
            DailyCycleMode.Accelerated8Hour => TimeSpan.FromHours(8),
            DailyCycleMode.Accelerated12Hour => TimeSpan.FromHours(12),
            _ => TimeSpan.FromHours(6)
        };
        
        await ProcessNPCsDuringAbsence(simulatedTime);
    }
    
    private async Task ProcessEndlessReset()
    {
        terminal?.WriteLine("Endless mode: Time flows differently here...", "bright_magenta");
        
        // In endless mode, still process some world simulation but less frequently
        if (currentDay % 7 == 0) // Weekly world updates
        {
            await ProcessNPCsDuringAbsence(TimeSpan.FromDays(1));
        }
    }
    
    /// <summary>
    /// Process NPC activities during player absence
    /// </summary>
    private async Task ProcessNPCsDuringAbsence(TimeSpan timeSpan)
    {
        // Note: EnhancedNPCSystem doesn't have an Instance property or ProcessTimePassage method
        // In a full implementation, this would process NPC activities during player absence
        terminal?.WriteLine($"NPCs have been active during your absence ({timeSpan.TotalHours:F1} hours simulated)", "yellow");
    }
    
    /// <summary>
    /// Process world events during player absence
    /// </summary>
    private async Task ProcessWorldEventsDuringAbsence(TimeSpan timeSpan)
    {
        // Note: WorldSimulator doesn't have an Instance property or SimulateTimePassage method
        // In a full implementation, this would simulate world events during player absence
        terminal?.WriteLine("World events have unfolded in your absence!", "yellow");
    }
    
    /// <summary>
    /// Perform auto-save
    /// </summary>
    private async Task PerformAutoSave()
    {
        var player = GameEngine.Instance?.CurrentPlayer;
        if (player != null)
        {
            var success = await SaveSystem.Instance.AutoSave(player);
            if (success)
            {
                lastAutoSave = DateTime.Now;
                terminal?.WriteLine("Auto-saved", "gray");
            }
        }
    }
    
    /// <summary>
    /// Adjust reset time when mode changes
    /// </summary>
    private void AdjustResetTimeForMode()
    {
        // Adjust the last reset time to prevent immediate reset when changing modes
        lastResetTime = currentMode switch
        {
            DailyCycleMode.SessionBased => DateTime.Now, // Reset immediately available
            DailyCycleMode.RealTime24Hour => DateTime.Now.Date, // Next reset at midnight
            DailyCycleMode.Accelerated4Hour => DateTime.Now, // Start new cycle
            DailyCycleMode.Accelerated8Hour => DateTime.Now,
            DailyCycleMode.Accelerated12Hour => DateTime.Now,
            DailyCycleMode.Endless => DateTime.Now.AddDays(1), // Delay next reset
            _ => DateTime.Now
        };
    }
    
    /// <summary>
    /// Load daily system state from save data
    /// </summary>
    public void LoadFromSaveData(SaveGameData saveData)
    {
        currentDay = saveData.CurrentDay;
        lastResetTime = saveData.LastDailyReset;
        currentMode = saveData.DailyCycleMode;
        autoSaveEnabled = saveData.Settings.AutoSaveEnabled;
        autoSaveInterval = saveData.Settings.AutoSaveInterval;

        // Sync StoryProgressionSystem's game day counter
        try
        {
            StoryProgressionSystem.Instance.CurrentGameDay = currentDay;
        }
        catch { /* StoryProgressionSystem not initialized */ }

        GD.Print($"Daily system loaded: Day {currentDay}, Mode {currentMode}");
    }
    
    /// <summary>
    /// Configure auto-save settings
    /// </summary>
    public void ConfigureAutoSave(bool enabled, TimeSpan interval)
    {
        autoSaveEnabled = enabled;
        autoSaveInterval = interval;
        
        terminal?.WriteLine($"Auto-save {(enabled ? "enabled" : "disabled")}" + 
                          (enabled ? $" (every {interval.TotalMinutes} minutes)" : ""), "cyan");
    }
    
    /// <summary>
    /// Process only player-specific daily events (online mode).
    /// World-level events (king, guards, treasury) are handled by WorldSimService.
    /// </summary>
    private async Task ProcessPlayerDailyEvents()
    {
        var terminal = GameEngine.Instance?.Terminal;
        var player = GameEngine.Instance?.CurrentPlayer;

        // Decrement player prison sentence
        if (player != null && player.DaysInPrison > 0)
        {
            player.DaysInPrison--;
            player.PrisonEscapes = (byte)Math.Min(player.PrisonEscapes + 1, GameConfig.MaxPrisonEscapeAttempts);
            if (terminal != null)
            {
                terminal.WriteLine($"A day passes in prison... ({player.DaysInPrison} day{(player.DaysInPrison == 1 ? "" : "s")} remaining)", "yellow");
            }
        }

        // Update grief system
        try
        {
            var grief = GriefSystem.Instance;
            if (grief.IsGrieving)
            {
                var previousStage = grief.CurrentStage;
                grief.UpdateGrief(currentDay);
                if (grief.CurrentStage != previousStage && terminal != null)
                {
                    terminal.WriteLine("");
                    terminal.WriteLine($"Your grief has evolved... ({previousStage} --> {grief.CurrentStage})", "dark_magenta");
                    var effects = grief.GetCurrentEffects();
                    if (!string.IsNullOrEmpty(effects.Description))
                        terminal.WriteLine($"  {effects.Description}", "gray");
                    terminal.WriteLine("");
                }
            }
        }
        catch { /* Grief system not initialized */ }

        // Process drug effects
        try
        {
            if (player != null && (player.OnDrugs || player.IsAddicted))
            {
                string drugMessage = DrugSystem.ProcessDailyDrugEffects(player);
                if (!string.IsNullOrEmpty(drugMessage) && terminal != null)
                {
                    terminal.SetColor("bright_magenta");
                    terminal.WriteLine(drugMessage);
                }
            }
        }
        catch { /* Drug system error */ }

        // Process Loan Shark interest
        try
        {
            if (player != null && player.LoanAmount > 0)
            {
                long interest = (long)(player.LoanAmount * GameConfig.LoanSharkDailyInterest);
                player.LoanInterestAccrued += interest;
                player.LoanAmount += interest;
                player.LoanDaysRemaining--;

                if (terminal != null)
                {
                    terminal.SetColor("red");
                    terminal.WriteLine($"Loan Shark: Interest of {interest:N0}g accrued. You owe {player.LoanAmount:N0}g ({Math.Max(0, player.LoanDaysRemaining)} day{(player.LoanDaysRemaining == 1 ? "" : "s")} remaining).");
                }

                if (player.LoanDaysRemaining <= 0 && terminal != null)
                {
                    terminal.SetColor("bright_red");
                    terminal.WriteLine("WARNING: The Loan Shark's enforcers are looking for you!");
                }
            }
        }
        catch { /* Loan system error */ }

        // Process Quest System daily maintenance
        QuestSystem.ProcessDailyQuestMaintenance();
        if (player != null)
            QuestSystem.RefreshBountyBoard(player.Level);

        // Blood Price — natural murder weight decay
        if (player != null && player.MurderWeight > 0)
        {
            var elapsed = DateTime.Now - player.LastMurderWeightDecay;
            if (player.LastMurderWeightDecay == DateTime.MinValue)
            {
                player.LastMurderWeightDecay = DateTime.Now;
            }
            else if (elapsed.TotalDays >= 1.0)
            {
                int daysElapsed = (int)elapsed.TotalDays;
                float decay = daysElapsed * GameConfig.MurderWeightDecayPerRealDay;
                player.MurderWeight = Math.Max(0f, player.MurderWeight - decay);
                player.LastMurderWeightDecay = DateTime.Now;
                if (decay > 0)
                    DebugLogger.Instance.LogInfo("BLOOD_PRICE",
                        $"Murder weight decayed by {decay:F1} ({daysElapsed} days). Now {player.MurderWeight:F1}");
            }
        }

        await Task.CompletedTask;
    }

    private async Task ProcessDailyEvents()
    {
        var terminal = GameEngine.Instance?.Terminal;

        // Decrement player prison sentence on daily reset
        var prisoner = GameEngine.Instance?.CurrentPlayer;
        if (prisoner != null && prisoner.DaysInPrison > 0)
        {
            prisoner.DaysInPrison--;
            prisoner.PrisonEscapes = (byte)Math.Min(prisoner.PrisonEscapes + 1, GameConfig.MaxPrisonEscapeAttempts);
            if (terminal != null)
            {
                terminal.WriteLine($"A day passes in prison... ({prisoner.DaysInPrison} day{(prisoner.DaysInPrison == 1 ? "" : "s")} remaining)", "yellow");
            }
        }

        // Process World Event System - this handles all major events
        await WorldEventSystem.Instance.ProcessDailyEvents(currentDay);

        // Update grief system - advances grief stages based on days passed
        try
        {
            var grief = GriefSystem.Instance;
            if (grief.IsGrieving)
            {
                var previousStage = grief.CurrentStage;
                grief.UpdateGrief(currentDay);

                // Notify player if grief stage changed
                if (grief.CurrentStage != previousStage && terminal != null)
                {
                    terminal.WriteLine("");
                    terminal.WriteLine($"Your grief has evolved... ({previousStage} --> {grief.CurrentStage})", "dark_magenta");

                    // Show stage effect
                    var effects = grief.GetCurrentEffects();
                    if (!string.IsNullOrEmpty(effects.Description))
                    {
                        terminal.WriteLine($"  {effects.Description}", "gray");
                    }
                    terminal.WriteLine("");
                }
            }
        }
        catch { /* Grief system not initialized */ }

        // Process drug effects - duration, expiration, withdrawal, addiction recovery (v0.41.0)
        try
        {
            var drugPlayer = GameEngine.Instance?.CurrentPlayer;
            if (drugPlayer != null && (drugPlayer.OnDrugs || drugPlayer.IsAddicted))
            {
                string drugMessage = DrugSystem.ProcessDailyDrugEffects(drugPlayer);
                if (!string.IsNullOrEmpty(drugMessage) && terminal != null)
                {
                    terminal.SetColor("bright_magenta");
                    terminal.WriteLine(drugMessage);
                }
            }
        }
        catch { /* Drug system error */ }

        // Process Loan Shark interest (v0.41.0)
        try
        {
            var loanPlayer = GameEngine.Instance?.CurrentPlayer;
            if (loanPlayer != null && loanPlayer.LoanAmount > 0)
            {
                long interest = (long)(loanPlayer.LoanAmount * GameConfig.LoanSharkDailyInterest);
                loanPlayer.LoanInterestAccrued += interest;
                loanPlayer.LoanAmount += interest;
                loanPlayer.LoanDaysRemaining--;

                if (terminal != null)
                {
                    terminal.SetColor("red");
                    terminal.WriteLine($"Loan Shark: Interest of {interest:N0}g accrued. You owe {loanPlayer.LoanAmount:N0}g ({Math.Max(0, loanPlayer.LoanDaysRemaining)} day{(loanPlayer.LoanDaysRemaining == 1 ? "" : "s")} remaining).");
                }

                if (loanPlayer.LoanDaysRemaining <= 0)
                {
                    if (terminal != null)
                    {
                        terminal.SetColor("bright_red");
                        terminal.WriteLine("WARNING: The Loan Shark's enforcers are looking for you!");
                    }
                }
            }
        }
        catch { /* Loan system error */ }

        // Process royal finances - guard salaries, monster feeding, tax collection
        try
        {
            var king = CastleLocation.GetCurrentKing();
            if (king?.IsActive == true)
            {
                var expensesBefore = king.CalculateDailyExpenses();
                var incomeBefore = king.CalculateDailyIncome();
                var treasuryBefore = king.Treasury;

                king.ProcessDailyActivities();

                // Process guard loyalty changes based on treasury health
                ProcessGuardLoyalty(king, treasuryBefore, terminal);

                // Check for treasury crisis
                if (king.Treasury < king.CalculateDailyExpenses())
                {
                    ProcessTreasuryCrisis(king, terminal);
                }

                // Log royal finances to news
                var netChange = incomeBefore - expensesBefore;
                if (netChange < 0 && Math.Abs(netChange) > 100)
                {
                    NewsSystem.Instance?.Newsy(false, $"The royal treasury hemorrhages {Math.Abs(netChange)} gold daily!");
                }
            }
        }
        catch { /* King system not initialized */ }

        // Process Quest System daily maintenance (quest expiration, failure processing)
        QuestSystem.ProcessDailyQuestMaintenance();

        // Refresh bounty board with new quests if needed
        var player = GameEngine.Instance?.CurrentPlayer;
        if (player != null)
        {
            QuestSystem.RefreshBountyBoard(player.Level);
        }

        // Blood Price — natural murder weight decay over real time
        if (player != null && player.MurderWeight > 0)
        {
            var elapsed = DateTime.Now - player.LastMurderWeightDecay;
            if (player.LastMurderWeightDecay == DateTime.MinValue)
            {
                player.LastMurderWeightDecay = DateTime.Now;
            }
            else if (elapsed.TotalDays >= 1.0)
            {
                int daysElapsed = (int)elapsed.TotalDays;
                float decay = daysElapsed * GameConfig.MurderWeightDecayPerRealDay;
                player.MurderWeight = Math.Max(0f, player.MurderWeight - decay);
                player.LastMurderWeightDecay = DateTime.Now;
                if (decay > 0)
                    DebugLogger.Instance.LogInfo("BLOOD_PRICE",
                        $"Murder weight decayed by {decay:F1} ({daysElapsed} days). Now {player.MurderWeight:F1}");
            }
        }

        // Special events based on day number
        if (currentDay % 7 == 0) // Weekly events
        {
            await ProcessWeeklyEvent();
        }

        if (currentDay % 30 == 0) // Monthly events
        {
            await ProcessMonthlyEvent();
        }
    }
    
    private async Task ProcessWeeklyEvent()
    {
        // Force a festival or market event on weekly intervals
        var worldEvents = WorldEventSystem.Instance;
        var roll = GD.RandRange(0, 2);
        switch (roll)
        {
            case 0:
                worldEvents.ForceEvent(WorldEventSystem.EventType.TournamentDay, currentDay);
                break;
            case 1:
                worldEvents.ForceEvent(WorldEventSystem.EventType.MerchantCaravan, currentDay);
                break;
            case 2:
                worldEvents.ForceEvent(WorldEventSystem.EventType.HarvestFestival, currentDay);
                break;
        }
        await Task.CompletedTask;
    }

    private async Task ProcessMonthlyEvent()
    {
        // Force a major event on monthly intervals (usually king's decree or war-related)
        var worldEvents = WorldEventSystem.Instance;
        var roll = GD.RandRange(0, 3);
        switch (roll)
        {
            case 0:
                worldEvents.ForceEvent(WorldEventSystem.EventType.KingFestivalDecree, currentDay);
                break;
            case 1:
                worldEvents.ForceEvent(WorldEventSystem.EventType.KingBounty, currentDay);
                break;
            case 2:
                worldEvents.ForceEvent(WorldEventSystem.EventType.GoldRush, currentDay);
                break;
            case 3:
                worldEvents.ForceEvent(WorldEventSystem.EventType.AncientRelicFound, currentDay);
                break;
        }
        await Task.CompletedTask;
    }
    
    public string GetTimeStatus()
    {
        var uptime = DateTime.Now - gameStartTime;
        var modeText = currentMode switch
        {
            DailyCycleMode.SessionBased => "Session",
            DailyCycleMode.RealTime24Hour => "Real-time",
            DailyCycleMode.Accelerated4Hour => "Fast (4h)",
            DailyCycleMode.Accelerated8Hour => "Fast (8h)",
            DailyCycleMode.Accelerated12Hour => "Fast (12h)",
            DailyCycleMode.Endless => "Endless",
            _ => "Unknown"
        };
        
        return $"Day {currentDay} | Mode: {modeText} | Uptime: {uptime.Days}d {uptime.Hours}h {uptime.Minutes}m";
    }
    
    public bool IsNewDay()
    {
        var timeSinceReset = DateTime.Now - lastResetTime;
        return timeSinceReset.TotalMinutes < 5; // New day if reset was less than 5 minutes ago
    }
    
    public TimeSpan GetTimeUntilNextReset()
    {
        return currentMode switch
        {
            DailyCycleMode.SessionBased => TimeSpan.Zero, // Available when turns run out
            DailyCycleMode.RealTime24Hour => DateTime.Now.Date.AddDays(1) - DateTime.Now,
            DailyCycleMode.Accelerated4Hour => lastResetTime.AddHours(4) - DateTime.Now,
            DailyCycleMode.Accelerated8Hour => lastResetTime.AddHours(8) - DateTime.Now,
            DailyCycleMode.Accelerated12Hour => lastResetTime.AddHours(12) - DateTime.Now,
            DailyCycleMode.Endless => TimeSpan.MaxValue, // Never resets
            _ => TimeSpan.Zero
        };
    }

    /// <summary>
    /// Process guard loyalty changes based on treasury health and service time
    /// </summary>
    private void ProcessGuardLoyalty(King king, long treasuryBefore, TerminalUI? terminal)
    {
        var guardsToRemove = new List<RoyalGuard>();
        var random = new Random();

        foreach (var guard in king.Guards)
        {
            // Unpaid guards lose loyalty (treasury was depleted)
            if (king.Treasury < king.CalculateDailyExpenses())
            {
                guard.Loyalty = Math.Max(0, guard.Loyalty - 5);
                if (terminal != null && guard.AI == CharacterAI.Human)
                {
                    // Notify human guards of their pay issues
                }
            }
            else
            {
                // Well-paid guards slowly gain loyalty
                guard.Loyalty = Math.Min(100, guard.Loyalty + 1);
            }

            // Long service increases loyalty cap
            var daysServed = (DateTime.Now - guard.RecruitmentDate).TotalDays;
            if (daysServed > 30)
            {
                guard.Loyalty = Math.Min(100, guard.Loyalty + 1);
            }

            // Very low loyalty = desertion
            if (guard.Loyalty <= 10)
            {
                guardsToRemove.Add(guard);
                NewsSystem.Instance?.Newsy(true, $"Guard {guard.Name} has deserted the royal service!");
            }
            // Low loyalty has chance of desertion
            else if (guard.Loyalty <= 25 && random.Next(100) < 10)
            {
                guardsToRemove.Add(guard);
                NewsSystem.Instance?.Newsy(true, $"Disgruntled guard {guard.Name} has abandoned their post!");
            }
        }

        // Remove deserters
        foreach (var deserter in guardsToRemove)
        {
            king.Guards.Remove(deserter);
        }
    }

    /// <summary>
    /// Handle treasury crisis - guards may desert, monsters may escape
    /// </summary>
    private void ProcessTreasuryCrisis(King king, TerminalUI? terminal)
    {
        var random = new Random();

        // All guards lose extra loyalty during crisis
        foreach (var guard in king.Guards)
        {
            guard.Loyalty = Math.Max(0, guard.Loyalty - 3);
        }

        // Hungry monsters may escape (10% chance per monster when unfed)
        var escapedMonsters = new List<MonsterGuard>();
        foreach (var monster in king.MonsterGuards)
        {
            if (random.Next(100) < 10)
            {
                escapedMonsters.Add(monster);
                NewsSystem.Instance?.Newsy(true, $"The unfed {monster.Name} has escaped from the castle moat!");
            }
        }

        foreach (var monster in escapedMonsters)
        {
            king.MonsterGuards.Remove(monster);
        }

        // Treasury crisis is newsworthy
        if (king.Guards.Count > 0 || king.MonsterGuards.Count > 0)
        {
            NewsSystem.Instance?.Newsy(false, $"Royal treasury crisis! Guards and monsters go unpaid!");
        }
    }
}

// Simple config manager placeholder
public static partial class ConfigManager
{
    public static void LoadConfig()
    {
        // This would normally load from JSON and set static properties
        // For now, the GameConfig class already has default values
    }

    // Generic accessor placeholder so that legacy calls compile
    public static T GetConfig<T>(string key) => default!;
} 
