using UsurperRemake.Utils;
using UsurperRemake.Systems;
using UsurperRemake.Data;
using Godot;
using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using System.Linq;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.IO;

/// <summary>
/// Main game engine based on Pascal USURPER.PAS
/// Handles the core game loop, initialization, and game state management
/// Now includes comprehensive save/load system and flexible daily cycles
/// </summary>
public partial class GameEngine : Node
{
    private static readonly Lazy<GameEngine> lazyInstance = new Lazy<GameEngine>(() => new GameEngine());
    private static GameEngine? _fallbackInstance;
    private TerminalEmulator terminal;
    private Character? currentPlayer;

    /// <summary>
    /// Pending notifications to show the player (team events, important world events, etc.)
    /// In MUD mode, stored per-session in SessionContext. In single-player, uses static queue.
    /// </summary>
    public static Queue<string> PendingNotifications
    {
        get
        {
            var ctx = UsurperRemake.Server.SessionContext.Current;
            if (ctx != null) return ctx.PendingNotifications;
            return _staticPendingNotifications;
        }
    }
    private static readonly Queue<string> _staticPendingNotifications = new();

    /// <summary>
    /// Add a notification to be shown to the player
    /// </summary>
    public static void AddNotification(string message)
    {
        if (!string.IsNullOrWhiteSpace(message))
        {
            var queue = PendingNotifications;
            // Cap notifications to prevent unbounded growth
            while (queue.Count >= 100)
                queue.Dequeue();
            queue.Enqueue(message);
        }
    }

    /// <summary>
    /// Flag indicating an intentional exit (quit from menu) vs unexpected termination
    /// In MUD mode, stored per-session in SessionContext.
    /// </summary>
    public static bool IsIntentionalExit
    {
        get
        {
            var ctx = UsurperRemake.Server.SessionContext.Current;
            if (ctx != null) return ctx.IsIntentionalExit;
            return _staticIsIntentionalExit;
        }
        private set
        {
            var ctx = UsurperRemake.Server.SessionContext.Current;
            if (ctx != null) ctx.IsIntentionalExit = value;
            else _staticIsIntentionalExit = value;
        }
    }
    private static bool _staticIsIntentionalExit = false;

    /// <summary>
    /// Mark the current session as an intentional exit (prevents warning on shutdown)
    /// </summary>
    public static void MarkIntentionalExit()
    {
        IsIntentionalExit = true;
    }

    /// <summary>
    /// Thread-safe singleton accessor. In MUD mode, returns the per-session engine.
    /// In single-player/BBS, returns the static fallback instance.
    /// </summary>
    public static GameEngine Instance
    {
        get
        {
            // MUD mode: per-session engine
            var ctx = UsurperRemake.Server.SessionContext.Current;
            if (ctx?.Engine != null) return ctx.Engine;
            // If a specific instance was set (e.g., in Godot), use it
            if (_fallbackInstance != null) return _fallbackInstance;
            // Otherwise use lazy initialization for thread safety
            return lazyInstance.Value;
        }
    }

    /// <summary>
    /// Set the instance explicitly (used when created by Godot scene tree)
    /// </summary>
    public static void SetInstance(GameEngine engine)
    {
        var ctx = UsurperRemake.Server.SessionContext.Current;
        if (ctx != null) ctx.Engine = engine;
        else _fallbackInstance = engine;
    }
    
    // Missing properties for compilation
    public Character? CurrentPlayer
    {
        get => currentPlayer;
        set => currentPlayer = value;
    }

    public static string DataPath => GameConfig.DataPath;

    // Dungeon party NPC IDs (spouses, team members, lovers) - persisted across saves
    private List<string> dungeonPartyNPCIds = new();

    /// <summary>
    /// Get the list of NPC IDs currently in the dungeon party
    /// </summary>
    public List<string> DungeonPartyNPCIds => dungeonPartyNPCIds;

    /// <summary>
    /// Set dungeon party NPC IDs (called from DungeonLocation when party changes)
    /// </summary>
    public void SetDungeonPartyNPCs(IEnumerable<string> npcIds)
    {
        dungeonPartyNPCIds = npcIds.ToList();
    }

    /// <summary>
    /// Clear dungeon party NPCs (called when leaving dungeon or on death)
    /// </summary>
    public void ClearDungeonParty()
    {
        dungeonPartyNPCIds.Clear();
        dungeonPartyPlayerNames.Clear();
    }

    // Player echo teammates for cooperative dungeons (online mode)
    private List<string> dungeonPartyPlayerNames = new();
    public List<string> DungeonPartyPlayerNames => dungeonPartyPlayerNames;
    public void SetDungeonPartyPlayers(IEnumerable<string> names) => dungeonPartyPlayerNames = names.ToList();
    public void ClearDungeonPartyPlayers() => dungeonPartyPlayerNames.Clear();

    /// <summary>
    /// Auto-populate quickbar for characters migrating from pre-quickbar saves.
    /// Fills slots 1-9 with known spells (casters) or learned abilities (martial).
    /// </summary>
    public static void AutoPopulateQuickbar(Character player)
    {
        player.Quickbar = new List<string?>(new string?[9]);
        int slot = 0;

        bool isSpellcaster = ClassAbilitySystem.IsSpellcaster(player.Class);
        if (isSpellcaster)
        {
            // Fill with known spells ordered by level
            var knownSpells = SpellSystem.GetAvailableSpells(player);
            foreach (var spell in knownSpells.OrderBy(s => s.Level))
            {
                if (slot >= 9) break;
                player.Quickbar[slot] = $"spell:{spell.Level}";
                slot++;
            }
        }
        else
        {
            // Fill with learned abilities ordered by level requirement
            var abilities = ClassAbilitySystem.GetAvailableAbilities(player);
            foreach (var ability in abilities.OrderBy(a => a.LevelRequired))
            {
                if (slot >= 9) break;
                player.Quickbar[slot] = ability.Id;
                slot++;
            }
        }
    }

    /// <summary>
    /// Add newly available spells/abilities to empty quickbar slots.
    /// Called after level-up so new skills are immediately usable in combat.
    /// Does NOT overwrite existing slot assignments.
    /// </summary>
    public static void QuickbarAddNewSkills(Character player)
    {
        if (player.Quickbar == null || player.Quickbar.Count < 9)
            player.Quickbar = new List<string?>(new string?[9]);

        // Collect what's already on the quickbar
        var equipped = new HashSet<string>(player.Quickbar.Where(s => s != null)!);

        bool isSpellcaster = ClassAbilitySystem.IsSpellcaster(player.Class);
        if (isSpellcaster)
        {
            var knownSpells = SpellSystem.GetAvailableSpells(player);
            foreach (var spell in knownSpells.OrderBy(s => s.Level))
            {
                string id = $"spell:{spell.Level}";
                if (equipped.Contains(id)) continue;
                int emptySlot = player.Quickbar.IndexOf(null);
                if (emptySlot < 0) break; // No empty slots
                player.Quickbar[emptySlot] = id;
                equipped.Add(id);
            }
        }
        else
        {
            var abilities = ClassAbilitySystem.GetAvailableAbilities(player);
            foreach (var ability in abilities.OrderBy(a => a.LevelRequired))
            {
                if (equipped.Contains(ability.Id)) continue;
                int emptySlot = player.Quickbar.IndexOf(null);
                if (emptySlot < 0) break; // No empty slots
                player.Quickbar[emptySlot] = ability.Id;
                equipped.Add(ability.Id);
            }
        }
    }

    // Terminal access for systems
    public TerminalEmulator Terminal => terminal;
    
    // Core game components
    private GameState gameState;
    private List<NPC> worldNPCs;
    private List<Monster> worldMonsters;
    private LocationManager locationManager;
    private DailySystemManager dailyManager;
    private CombatEngine combatEngine;
    private WorldSimulator worldSimulator;
    
    // Online system
    private List<OnlinePlayer> onlinePlayers;
    
    // Auto-save timer
    private DateTime lastPeriodicCheck;
    
    // Stub classes for compilation
    private class UsurperConfig
    {
        // Pascal compatible config structure
    }
    
    private class ScoreManager
    {
        // Score and ranking management
    }

    /// <summary>
    /// Console entry point for running the full game
    /// </summary>
    public static async Task RunConsoleAsync()
    {
        var engine = Instance;

        // Check if we're in BBS door mode or online mode (both have pre-set player names)
        if (UsurperRemake.BBS.DoorMode.IsInDoorMode || UsurperRemake.BBS.DoorMode.IsOnlineMode)
        {
            await engine.RunBBSDoorMode();
        }
        else
        {
            await engine.RunMainGameLoop();
        }
    }

    /// <summary>
    /// Main game loop for console mode
    /// </summary>
    private async Task RunMainGameLoop()
    {
        InitializeGame();

        // Start version check in background while splash screen shows
        var versionCheckTask = UsurperRemake.Systems.VersionChecker.Instance.CheckForUpdatesAsync();

        // Show splash screen (the colorful USURPER REBORN title)
        await UsurperRemake.UI.SplashScreen.Show(terminal);

        // Wait for version check to complete (should be done by now)
        await versionCheckTask;

        // Show update notification if new version available
        if (UsurperRemake.Systems.VersionChecker.Instance.NewVersionAvailable)
        {
            await UsurperRemake.Systems.VersionChecker.Instance.DisplayUpdateNotification(terminal);
            var shouldExit = await UsurperRemake.Systems.VersionChecker.Instance.PromptAndInstallUpdate(terminal);
            if (shouldExit)
            {
                // Exit the game so the updater can replace files
                Environment.Exit(0);
            }
        }

        // Go directly to main menu (skip the redundant title screen)
        await MainMenu();
    }

    /// <summary>
    /// BBS Door mode - automatically loads or creates character based on drop file
    /// </summary>
    private async Task RunBBSDoorMode()
    {
        InitializeGame();

        var playerName = UsurperRemake.BBS.DoorMode.GetPlayerName();
        UsurperRemake.BBS.DoorMode.Log($"BBS Door mode: Looking for save for '{playerName}'");

        // Show a brief welcome
        terminal.ClearScreen();
        terminal.SetColor("bright_cyan");
        terminal.WriteLine("╔══════════════════════════════════════════════════════════════════════════════╗");
        terminal.SetColor("bright_yellow");
        terminal.WriteLine("║                USURPER REBORN - Halls of Avarice                            ║");
        terminal.SetColor("bright_cyan");
        terminal.WriteLine("╚══════════════════════════════════════════════════════════════════════════════╝");
        terminal.SetColor("gray");
        terminal.WriteLine($"  v{GameConfig.Version}");
        terminal.WriteLine("");
        terminal.SetColor("white");
        terminal.WriteLine($"Welcome, {playerName}!");
        terminal.WriteLine("");

        // Check if this BBS user has an existing save
        var existingSave = SaveSystem.Instance.GetMostRecentSave(playerName);
        bool isSysOp = UsurperRemake.BBS.DoorMode.IsSysOp;
        bool isOnlineAdmin = UsurperRemake.Server.SessionContext.IsActive
            ? (UsurperRemake.Server.SessionContext.Current?.WizardLevel ?? UsurperRemake.Server.WizardLevel.Mortal) >= UsurperRemake.Server.WizardLevel.God
            : UsurperRemake.BBS.DoorMode.IsOnlineMode &&
                string.Equals(UsurperRemake.BBS.DoorMode.OnlineUsername, "rage", StringComparison.OrdinalIgnoreCase);

        if (existingSave != null)
        {
            // Existing character found - offer to load or create new
            terminal.SetColor("green");
            terminal.WriteLine($"Found existing character: Level {existingSave.Level}, Day {existingSave.CurrentDay}");
            terminal.WriteLine($"Last played: {existingSave.SaveTime:yyyy-MM-dd HH:mm}");
            terminal.WriteLine("");

            terminal.SetColor("bright_white");
            terminal.WriteLine("[L] Load existing character");
            terminal.WriteLine("[N] Create new character (WARNING: Overwrites existing!)");
            if (isSysOp)
            {
                terminal.SetColor("bright_yellow");
                terminal.WriteLine("[%] SysOp Administration Console");
                terminal.SetColor("bright_white");
            }
            if (isOnlineAdmin)
            {
                terminal.SetColor("bright_red");
                terminal.WriteLine("[%] Admin Console");
                terminal.SetColor("bright_white");
            }
            if (UsurperRemake.BBS.DoorMode.IsOnlineMode)
            {
                terminal.SetColor("gray");
                terminal.WriteLine("[C] Change Password");
                terminal.SetColor("bright_white");
            }
            terminal.WriteLine("[Q] Quit");
            terminal.WriteLine("");

            var choice = await terminal.GetInput("Your choice: ");

            switch (choice.ToUpper())
            {
                case "L":
                    // LoadSaveByFileName enters the game world automatically
                    await LoadSaveByFileName(existingSave.FileName);
                    break;

                case "N":
                    terminal.SetColor("bright_red");
                    terminal.WriteLine("");
                    terminal.WriteLine("WARNING: This will DELETE your existing character!");
                    var confirm = await terminal.GetInput("Type 'DELETE' to confirm: ");
                    if (confirm == "DELETE")
                    {
                        // Delete existing saves for this player
                        var saves = SaveSystem.Instance.GetPlayerSaves(playerName);
                        foreach (var save in saves)
                        {
                            SaveSystem.Instance.DeleteSave(Path.GetFileNameWithoutExtension(save.FileName));
                        }
                        // CreateNewGame handles character creation and enters the game world
                        await CreateNewGame(playerName);
                    }
                    else
                    {
                        terminal.WriteLine("Character deletion cancelled.", "yellow");
                        await Task.Delay(2000);
                        // Recurse to show menu again
                        await RunBBSDoorMode();
                    }
                    break;

                case "C":
                    if (UsurperRemake.BBS.DoorMode.IsOnlineMode)
                    {
                        await ChangePasswordScreen();
                        await RunBBSDoorMode();
                        return;
                    }
                    else
                    {
                        await LoadSaveByFileName(existingSave.FileName);
                    }
                    break;

                case "%":
                    if (isOnlineAdmin)
                    {
                        await ShowOnlineAdminConsole();
                        await RunBBSDoorMode();
                        return;
                    }
                    else if (isSysOp)
                    {
                        await ShowSysOpConsole();
                        await RunBBSDoorMode();
                        return;
                    }
                    else
                    {
                        await LoadSaveByFileName(existingSave.FileName);
                    }
                    break;

                case "Q":
                    IsIntentionalExit = true;
                    terminal.WriteLine("Goodbye!", "cyan");
                    await Task.Delay(1000);
                    break;

                default:
                    // Default to loading existing character
                    await LoadSaveByFileName(existingSave.FileName);
                    break;
            }
        }
        else
        {
            // No existing character - create new one
            terminal.SetColor("bright_white");
            terminal.WriteLine("[N] Create new character");
            if (isSysOp)
            {
                terminal.SetColor("bright_yellow");
                terminal.WriteLine("[%] SysOp Administration Console");
                terminal.SetColor("bright_white");
            }
            if (isOnlineAdmin)
            {
                terminal.SetColor("bright_red");
                terminal.WriteLine("[%] Admin Console");
                terminal.SetColor("bright_white");
            }
            if (UsurperRemake.BBS.DoorMode.IsOnlineMode)
            {
                terminal.SetColor("gray");
                terminal.WriteLine("[C] Change Password");
                terminal.SetColor("bright_white");
            }
            terminal.WriteLine("[Q] Quit");
            terminal.WriteLine("");

            var choice = await terminal.GetInput("Your choice: ");

            switch (choice.ToUpper())
            {
                case "C":
                    if (UsurperRemake.BBS.DoorMode.IsOnlineMode)
                    {
                        await ChangePasswordScreen();
                        await RunBBSDoorMode();
                        return;
                    }
                    else
                    {
                        await CreateNewGame(playerName);
                    }
                    break;

                case "%":
                    if (isOnlineAdmin)
                    {
                        await ShowOnlineAdminConsole();
                        await RunBBSDoorMode();
                        return;
                    }
                    else if (isSysOp)
                    {
                        await ShowSysOpConsole();
                        await RunBBSDoorMode();
                        return;
                    }
                    else
                    {
                        await CreateNewGame(playerName);
                    }
                    break;

                case "Q":
                    IsIntentionalExit = true;
                    terminal.WriteLine("Goodbye!", "cyan");
                    await Task.Delay(1000);
                    break;

                default:
                    await CreateNewGame(playerName);
                    break;
            }
        }
    }

    public override void _Ready()
    {
        GD.Print("Usurper Reborn - Initializing Game Engine...");

        // Initialize core systems
        InitializeGame();

        // Handle async operations properly since _Ready() can't be async
        // Wrap in try-catch to prevent silent exception swallowing
        _ = Task.Run(async () =>
        {
            try
            {
                await ShowTitleScreen();
                await MainMenu();
            }
            catch (Exception ex)
            {
                GD.PrintErr($"[GameEngine] Fatal error in main loop: {ex.Message}");
                GD.PrintErr(ex.StackTrace);
                UsurperRemake.Systems.DebugLogger.Instance.LogError("CRASH", $"Fatal error in main loop:\n{ex}");
                UsurperRemake.Systems.DebugLogger.Instance.Flush();
                throw; // Re-throw to crash properly rather than hang silently
            }
        });
    }
    
    /// <summary>
    /// Initialize game systems - based on Init_Usurper procedure from Pascal
    /// </summary>
    private void InitializeGame()
    {
        // Ensure we have a working terminal instance when running outside of Godot
        if (terminal == null)
        {
            // If we were truly running inside Godot, the Terminal node would
            // already exist and TerminalEmulator.Instance would have been set.
            terminal = TerminalEmulator.Instance ?? new TerminalEmulator();
        }

        GD.Print("Reading configuration...");
        ReadStartCfgValues();

        // Load SysOp configuration (for BBS door mode settings persistence)
        UsurperRemake.Systems.SysOpConfigSystem.Instance.LoadConfig();

        GD.Print("Initializing core managers...");
        // Create the LocationManager early so that it becomes the singleton before NPCs are loaded
        if (locationManager == null)
        {
            locationManager = new LocationManager(terminal);
        }

        GD.Print("Initializing game data...");
        InitializeItems();      // From INIT.PAS Init_Items
        InitializeMonsters();   // From INIT.PAS Init_Monsters
        InitializeNPCs();       // From INIT.PAS Init_NPCs (needs LocationManager ready)
        InitializeLevels();     // From INIT.PAS Init_Levels
        InitializeGuards();     // From INIT.PAS Init_Guards
        
        GD.Print("Setting up game state...");
        gameState = new GameState
        {
            GameDate = 1,
            LastDayRun = 0,
            PlayersOnline = 0,
            MaintenanceRunning = false
        };
        
        // Initialize remaining core systems (LocationManager already created)
        dailyManager = DailySystemManager.Instance;
        combatEngine = new CombatEngine();

        // World simulator – start background AI processing
        // In MUD/online mode, only the first session should create and start the simulator.
        // Subsequent sessions reuse the existing shared instance to avoid N background loops
        // all processing the same NPC list (which causes N× respawn messages).
        if (WorldSimulator.Instance != null && WorldSimulator.Instance.IsRunning)
        {
            worldSimulator = WorldSimulator.Instance;
        }
        else
        {
            worldSimulator = new WorldSimulator();
            worldSimulator.StartSimulation(worldNPCs ?? new List<NPC>());
        }

        // Initialize collections
        worldMonsters = new List<Monster>();
        onlinePlayers = new List<OnlinePlayer>();

        // Initialize achievement and statistics systems
        GD.Print("Initializing achievement and statistics systems...");
        AchievementSystem.Initialize();
        QuestSystem.EnsureQuestsExist();

        // Initialize periodic check timer
        lastPeriodicCheck = DateTime.Now;

        GD.Print("Game engine initialized successfully!");
    }
    
    /// <summary>
    /// Periodic update for game systems (called regularly during gameplay)
    /// </summary>
    public async Task PeriodicUpdate()
    {
        var now = DateTime.Now;

        // Only run periodic checks every 30 seconds
        if (now - lastPeriodicCheck < TimeSpan.FromSeconds(30))
            return;

        lastPeriodicCheck = now;

        // Check for daily reset
        await dailyManager.CheckDailyReset();

        // World simulation is driven by WorldSimulator.StartSimulation() background loop.
        // Do NOT call SimulateStep() here — it causes double-ticking and in MUD mode
        // each session would double the sim rate.

        // Process NPC behaviors and maintenance
        await RunNPCMaintenanceCycle();
    }

    /// <summary>
    /// Run NPC maintenance cycle - handles NPC movement, activities, and world events
    /// </summary>
    private async Task RunNPCMaintenanceCycle()
    {
        var npcs = NPCSpawnSystem.Instance.ActiveNPCs;
        if (npcs == null || npcs.Count == 0) return;

        var random = new Random();

        // Process each living NPC
        foreach (var npc in npcs.Where(n => n.IsAlive).ToList())
        {
            // 20% chance to move to a different location
            if (random.Next(5) == 0)
            {
                MoveNPCToRandomLocation(npc, random);
            }

            // Process NPC activities (shopping, healing, etc.)
            await ProcessNPCActivity(npc, random);

            // Small chance for NPC to generate news
            if (random.Next(20) == 0)
            {
                GenerateNPCNews(npc, random);
            }
        }

        // Process NPC leveling (rare)
        if (random.Next(10) == 0)
        {
            ProcessNPCLeveling(npcs, random);
        }
    }

    /// <summary>
    /// Move an NPC to a random location in town
    /// </summary>
    private void MoveNPCToRandomLocation(NPC npc, Random random)
    {
        var locations = new[]
        {
            "Main Street", "Market", "Inn", "Temple", "Church",
            "Weapon Shop", "Armor Shop", "Magic Shop", "Castle",
            "Bank", "Healer", "Dark Alley"
        };

        var newLocation = locations[random.Next(locations.Length)];

        // Don't log every move - too spammy
        npc.CurrentLocation = newLocation;
    }

    /// <summary>
    /// Process NPC activity based on their current situation
    /// </summary>
    private async Task ProcessNPCActivity(NPC npc, Random random)
    {
        // Heal if injured
        if (npc.HP < npc.MaxHP && random.Next(3) == 0)
        {
            long healAmount = Math.Min(npc.MaxHP / 10, npc.MaxHP - npc.HP);
            npc.HP += (int)healAmount;
        }

        // Restore mana
        if (npc.Mana < npc.MaxMana && random.Next(2) == 0)
        {
            long manaAmount = Math.Min(npc.MaxMana / 5, npc.MaxMana - npc.Mana);
            npc.Mana += (int)manaAmount;
        }

        // Shopping (if at shop and has gold)
        if (npc.Gold > 500 && random.Next(10) == 0)
        {
            // Buy equipment upgrade
            if (npc.CurrentLocation == "Weapon Shop")
            {
                int cost = random.Next(100, 500);
                if (npc.Gold >= cost)
                {
                    npc.Gold -= cost;
                    npc.WeapPow += random.Next(1, 5);
                }
            }
            else if (npc.CurrentLocation == "Armor Shop")
            {
                int cost = random.Next(100, 400);
                if (npc.Gold >= cost)
                {
                    npc.Gold -= cost;
                    npc.ArmPow += random.Next(1, 4);
                }
            }
        }

        await Task.CompletedTask;
    }

    /// <summary>
    /// Generate news about NPC activities
    /// </summary>
    private void GenerateNPCNews(NPC npc, Random random)
    {
        var newsSystem = NewsSystem.Instance;
        if (newsSystem == null) return;

        var newsItems = new List<string>();

        // Alignment-based news
        if (npc.Darkness > npc.Chivalry + 200)
        {
            newsItems.Add($"{npc.Name2} was seen lurking in the shadows");
            newsItems.Add($"{npc.Name2} threatened a merchant");
            newsItems.Add($"Guards are watching {npc.Name2} closely");
        }
        else if (npc.Chivalry > npc.Darkness + 200)
        {
            newsItems.Add($"{npc.Name2} helped a lost child find their parents");
            newsItems.Add($"{npc.Name2} donated gold to the temple");
            newsItems.Add($"{npc.Name2} protected a merchant from thieves");
        }
        else
        {
            newsItems.Add($"{npc.Name2} was seen at the {npc.CurrentLocation}");
            newsItems.Add($"{npc.Name2} is looking for adventure partners");
        }

        // Class-based news
        switch (npc.Class)
        {
            case CharacterClass.Warrior:
            case CharacterClass.Barbarian:
                newsItems.Add($"{npc.Name2} challenged someone to a duel");
                break;
            case CharacterClass.Magician:
            case CharacterClass.Sage:
                newsItems.Add($"{npc.Name2} was seen studying ancient tomes");
                break;
            case CharacterClass.Assassin:
                newsItems.Add($"Rumors swirl about {npc.Name2}'s latest target");
                break;
        }

        if (newsItems.Count > 0)
        {
            var headline = newsItems[random.Next(newsItems.Count)];
            newsSystem.Newsy(true, headline);
        }
    }

    /// <summary>
    /// Process NPC leveling based on their activities
    /// </summary>
    private void ProcessNPCLeveling(List<NPC> npcs, Random random)
    {
        // Pick a random NPC to level up
        var eligibleNPCs = npcs.Where(n => n.IsAlive && n.Level < 50).ToList();
        if (eligibleNPCs.Count == 0) return;

        var luckyNPC = eligibleNPCs[random.Next(eligibleNPCs.Count)];

        // Level up!
        luckyNPC.Level++;
        luckyNPC.Experience += luckyNPC.Level * 1000;

        // Boost stats
        luckyNPC.MaxHP += random.Next(10, 30);
        luckyNPC.HP = luckyNPC.MaxHP;
        luckyNPC.Strength += random.Next(1, 3);
        luckyNPC.Defence += random.Next(1, 2);
        luckyNPC.WeapPow += random.Next(1, 3);
        luckyNPC.ArmPow += random.Next(1, 2);

        // Generate news about the level up
        var newsSystem = NewsSystem.Instance;
        if (newsSystem != null)
        {
            newsSystem.Newsy(true, $"{luckyNPC.Name2} has reached level {luckyNPC.Level}!");
        }
    }
    
    /// <summary>
    /// Show title screen - displays USURPER.ANS from Pascal
    /// </summary>
    private async Task ShowTitleScreen()
    {
        terminal.ClearScreen();
        terminal.ShowANSIArt("USURPER");
        terminal.SetColor("bright_yellow");
        terminal.WriteLine("USURPER REBORN - Halls of Avarice");
        terminal.SetColor("gray");
        terminal.WriteLine($"v{GameConfig.Version}");
        terminal.WriteLine("");
        terminal.SetColor("gray");
        terminal.WriteLine("1993 - Original by Jakob Dangarden");
        terminal.WriteLine("2025 - Reborn by Jason Knight");
        terminal.WriteLine("");
        terminal.WriteLine("Press Enter to continue...");
        await terminal.WaitForKey();
    }
    
    /// <summary>
    /// Main menu - based on Town_Menu procedure from Pascal
    /// </summary>
    private async Task MainMenu()
    {
        bool done = false;

        while (!done)
        {
            terminal.ClearScreen();

            // Title header
            terminal.SetColor("cyan");
            terminal.WriteLine("╔══════════════════════════════════════════════════════════════════════════════╗");
            terminal.SetColor("cyan");
            terminal.Write("║");
            terminal.SetColor("bright_yellow");
            terminal.Write("                USURPER REBORN");
            terminal.SetColor("white");
            terminal.Write(" - Halls of Avarice                            ");
            terminal.SetColor("cyan");
            terminal.WriteLine("║");
            terminal.SetColor("cyan");
            terminal.WriteLine("╚══════════════════════════════════════════════════════════════════════════════╝");
            terminal.SetColor("darkgray");
            terminal.WriteLine($"  v{GameConfig.Version}");
            terminal.WriteLine("");

            // Menu options with clean color hierarchy
            terminal.SetColor("darkgray");
            terminal.Write("  [");
            terminal.SetColor("bright_cyan");
            terminal.Write("E");
            terminal.SetColor("darkgray");
            terminal.Write("] ");
            terminal.SetColor("bright_green");
            terminal.WriteLine("Enter the Game");

            terminal.SetColor("darkgray");
            terminal.Write("  [");
            terminal.SetColor("bright_cyan");
            terminal.Write("I");
            terminal.SetColor("darkgray");
            terminal.Write("] ");
            terminal.SetColor("white");
            terminal.WriteLine("The Story So Far...");

            terminal.SetColor("darkgray");
            terminal.Write("  [");
            terminal.SetColor("bright_cyan");
            terminal.Write("H");
            terminal.SetColor("darkgray");
            terminal.Write("] ");
            terminal.SetColor("white");
            terminal.WriteLine("Usurper History");

            terminal.SetColor("darkgray");
            terminal.Write("  [");
            terminal.SetColor("bright_cyan");
            terminal.Write("B");
            terminal.SetColor("darkgray");
            terminal.Write("] ");
            terminal.SetColor("white");
            terminal.WriteLine("BBS List - Play Usurper Reborn Online!");

            terminal.SetColor("darkgray");
            terminal.Write("  [");
            terminal.SetColor("bright_cyan");
            terminal.Write("C");
            terminal.SetColor("darkgray");
            terminal.Write("] ");
            terminal.SetColor("gray");
            terminal.WriteLine("Credits");

            // Online Play - not shown in BBS door mode or online server mode (they're already online)
            if (!UsurperRemake.BBS.DoorMode.IsInDoorMode && !UsurperRemake.BBS.DoorMode.IsOnlineMode)
            {
                terminal.WriteLine("");
                terminal.SetColor("darkgray");
                terminal.Write("  [");
                terminal.SetColor("bright_yellow");
                terminal.Write("O");
                terminal.SetColor("darkgray");
                terminal.Write("] ");
                terminal.SetColor("bright_yellow");
                terminal.WriteLine("Online Play - Connect to Shared World");
            }

            terminal.WriteLine("");
            terminal.SetColor("darkgray");
            terminal.Write("  [");
            terminal.SetColor("red");
            terminal.Write("Q");
            terminal.SetColor("darkgray");
            terminal.Write("] ");
            terminal.SetColor("gray");
            terminal.WriteLine("Quit");

            // SysOp option - only visible in BBS door mode for SysOps
            if (UsurperRemake.BBS.DoorMode.IsInDoorMode && UsurperRemake.BBS.DoorMode.IsSysOp)
            {
                terminal.WriteLine("");
                terminal.SetColor("darkgray");
                terminal.Write("  [");
                terminal.SetColor("bright_yellow");
                terminal.Write("%");
                terminal.SetColor("darkgray");
                terminal.Write("] ");
                terminal.SetColor("yellow");
                terminal.WriteLine("SysOp Console");
            }

            terminal.WriteLine("");
            terminal.SetColor("bright_white");
            var choice = await terminal.GetInput("Your choice: ");

            switch (choice.ToUpper())
            {
                case "E":
                    await EnterGame();
                    break;
                case "I":
                    await ShowInstructions();
                    break;
                case "H":
                    await UsurperHistorySystem.Instance.ShowHistory(terminal);
                    break;
                case "B":
                    await ShowBBSList();
                    break;
                case "C":
                    await ShowCredits();
                    break;
                case "Q":
                    IsIntentionalExit = true;
                    done = true;
                    break;
                case "O":
                    if (!UsurperRemake.BBS.DoorMode.IsInDoorMode && !UsurperRemake.BBS.DoorMode.IsOnlineMode)
                    {
                        var onlinePlay = new UsurperRemake.Systems.OnlinePlaySystem(terminal);
                        await onlinePlay.StartOnlinePlay();
                    }
                    break;
                case "%":
                    if (UsurperRemake.BBS.DoorMode.IsInDoorMode && UsurperRemake.BBS.DoorMode.IsSysOp)
                    {
                        await ShowSysOpConsole();
                    }
                    break;
            }
        }
    }

    /// <summary>
    /// SysOp Administration Console - accessible from main menu before any save is loaded.
    /// This allows SysOps to manage the game without affecting player state.
    /// </summary>
    private async Task ShowSysOpConsole()
    {
        var sysopConsole = new SysOpConsoleManager(terminal);
        await sysopConsole.Run();
    }

    /// <summary>
    /// Online Admin Console - accessible from character selection when authenticated as admin.
    /// Manages players, settings, and world state via SqlSaveBackend.
    /// </summary>
    private async Task ShowOnlineAdminConsole()
    {
        var sqlBackend = SaveSystem.Instance.Backend as SqlSaveBackend;
        if (sqlBackend == null)
        {
            terminal.SetColor("red");
            terminal.WriteLine("Admin console requires online mode with SQL backend.");
            await terminal.GetInputAsync("Press Enter to continue...");
            return;
        }
        var adminConsole = new OnlineAdminConsole(terminal, sqlBackend);
        await adminConsole.Run();
    }

    /// <summary>
    /// Change password screen - accessible from character selection in online mode.
    /// Requires current password for verification before allowing change.
    /// </summary>
    private async Task ChangePasswordScreen()
    {
        var sqlBackend = SaveSystem.Instance.Backend as SqlSaveBackend;
        if (sqlBackend == null)
        {
            terminal.SetColor("red");
            terminal.WriteLine("Password change requires online mode.");
            await terminal.GetInputAsync("Press Enter to continue...");
            return;
        }

        terminal.ClearScreen();
        terminal.SetColor("bright_cyan");
        terminal.WriteLine("═══ CHANGE PASSWORD ═══");
        terminal.WriteLine("");

        var username = UsurperRemake.BBS.DoorMode.OnlineUsername;
        if (string.IsNullOrEmpty(username))
        {
            terminal.SetColor("red");
            terminal.WriteLine("No username found.");
            await terminal.GetInputAsync("Press Enter to continue...");
            return;
        }

        terminal.SetColor("white");
        terminal.WriteLine($"Account: {username}");
        terminal.WriteLine("");

        terminal.Write("Current password: ");
        var currentPassword = await Task.Run(() => TerminalEmulator.ReadLineWithBackspace(maskPassword: true));
        if (string.IsNullOrWhiteSpace(currentPassword))
        {
            terminal.SetColor("yellow");
            terminal.WriteLine("Cancelled.");
            await Task.Delay(1000);
            return;
        }

        terminal.Write("New password (min 4 chars): ");
        var newPassword = await Task.Run(() => TerminalEmulator.ReadLineWithBackspace(maskPassword: true));
        if (string.IsNullOrWhiteSpace(newPassword) || newPassword.Length < 4)
        {
            terminal.SetColor("red");
            terminal.WriteLine("Password must be at least 4 characters.");
            await terminal.GetInputAsync("Press Enter to continue...");
            return;
        }

        terminal.Write("Confirm new password: ");
        var confirmPassword = await Task.Run(() => TerminalEmulator.ReadLineWithBackspace(maskPassword: true));
        if (newPassword != confirmPassword)
        {
            terminal.SetColor("red");
            terminal.WriteLine("Passwords do not match.");
            await terminal.GetInputAsync("Press Enter to continue...");
            return;
        }

        var (success, message) = await sqlBackend.ChangePassword(username, currentPassword, newPassword);
        terminal.SetColor(success ? "bright_green" : "red");
        terminal.WriteLine(message);
        await terminal.GetInputAsync("Press Enter to continue...");
    }

    /// <summary>
    /// Enter the game with modern save/load UI
    /// </summary>
    private async Task EnterGame()
    {
        while (true)
        {
            terminal.ClearScreen();
            terminal.SetColor("bright_cyan");
            terminal.WriteLine("╔═══════════════════════════════════════════════════════════════════════════╗");
            terminal.SetColor("bright_yellow");
            terminal.WriteLine("║                           SAVE FILE MANAGEMENT                           ║");
            terminal.SetColor("bright_cyan");
            terminal.WriteLine("╚═══════════════════════════════════════════════════════════════════════════╝");
            terminal.WriteLine("");

            // Get all unique player names
            var playerNames = SaveSystem.Instance.GetAllPlayerNames();

            if (playerNames.Count == 0)
            {
                // No saves exist - create new character
                terminal.SetColor("yellow");
                terminal.WriteLine("No saved games found.");
                terminal.WriteLine("");
                terminal.SetColor("white");
                terminal.WriteLine("Let's create a new character!");
                terminal.WriteLine("");

                // Go directly to character creation - name will be entered there
                await CreateNewGame("");
                return;
            }

            // Show existing save slots
            terminal.SetColor("bright_green");
            terminal.WriteLine("Existing Save Slots:");
            terminal.WriteLine("");

            for (int i = 0; i < playerNames.Count; i++)
            {
                var playerName = playerNames[i];
                var mostRecentSave = SaveSystem.Instance.GetMostRecentSave(playerName);

                if (mostRecentSave != null)
                {
                    terminal.SetColor("darkgray");
                    terminal.Write($"[");
                    terminal.SetColor("bright_cyan");
                    terminal.Write($"{i + 1}");
                    terminal.SetColor("darkgray");
                    terminal.Write("] ");
                    terminal.SetColor("white");
                    terminal.Write($"{mostRecentSave.PlayerName}");
                    terminal.SetColor("cyan");
                    terminal.Write($" ({mostRecentSave.ClassName})");
                    terminal.SetColor("gray");
                    terminal.Write($" - Level {mostRecentSave.Level}");
                    terminal.SetColor("darkgray");
                    terminal.Write(" | ");
                    terminal.SetColor(mostRecentSave.IsAutosave ? "yellow" : "green");
                    terminal.Write(mostRecentSave.SaveType);
                    terminal.SetColor("darkgray");
                    terminal.Write(" | ");
                    terminal.SetColor("gray");
                    terminal.WriteLine(mostRecentSave.SaveTime.ToString("yyyy-MM-dd HH:mm:ss"));
                }
            }

            terminal.WriteLine("");
            terminal.SetColor("darkgray");
            terminal.Write("[");
            terminal.SetColor("bright_green");
            terminal.Write("N");
            terminal.SetColor("darkgray");
            terminal.Write("] ");
            terminal.SetColor("green");
            terminal.WriteLine("New Character");

            terminal.SetColor("darkgray");
            terminal.Write("[");
            terminal.SetColor("bright_red");
            terminal.Write("B");
            terminal.SetColor("darkgray");
            terminal.Write("] ");
            terminal.SetColor("red");
            terminal.WriteLine("Back to Main Menu");

            terminal.WriteLine("");
            terminal.SetColor("bright_white");
            var choice = await terminal.GetInput("Select save slot or option: ");

            // Handle numeric selection
            if (int.TryParse(choice, out int slotNumber) && slotNumber > 0 && slotNumber <= playerNames.Count)
            {
                var selectedPlayer = playerNames[slotNumber - 1];
                await ShowSaveSlotMenu(selectedPlayer);
                return;
            }

            // Handle letter commands
            switch (choice.ToUpper())
            {
                case "N":
                    var newName = await terminal.GetInput("Enter new character name: ");
                    if (!string.IsNullOrWhiteSpace(newName))
                    {
                        // Refresh player names list in case characters were deleted
                        var currentPlayerNames = SaveSystem.Instance.GetAllPlayerNames();

                        // Case-insensitive check to prevent file system conflicts
                        var nameExists = currentPlayerNames.Any(n =>
                            string.Equals(n, newName, StringComparison.OrdinalIgnoreCase));

                        if (nameExists)
                        {
                            terminal.WriteLine("That name already exists! Choose a different name.", "red");
                            await Task.Delay(2000);
                        }
                        else
                        {
                            await CreateNewGame(newName);
                            return;
                        }
                    }
                    break;

                case "B":
                    return;

                default:
                    terminal.WriteLine("Invalid choice!", "red");
                    await Task.Delay(1500);
                    break;
            }
        }
    }

    /// <summary>
    /// Show save slot menu for a specific player
    /// </summary>
    private async Task ShowSaveSlotMenu(string playerName)
    {
        while (true)
        {
            terminal.ClearScreen();
            terminal.SetColor("bright_cyan");
            terminal.WriteLine($"╔═══════════════════════════════════════════════════════════════════════════╗");
            terminal.SetColor("bright_yellow");
            terminal.WriteLine($"║                      SAVE SLOTS FOR: {playerName.PadRight(33)}║");
            terminal.SetColor("bright_cyan");
            terminal.WriteLine($"╚═══════════════════════════════════════════════════════════════════════════╝");
            terminal.WriteLine("");

            var saves = SaveSystem.Instance.GetPlayerSaves(playerName);

            if (saves.Count == 0)
            {
                terminal.WriteLine("No saves found for this character.", "red");
                await Task.Delay(2000);
                return;
            }

            // Display all saves (autosaves and manual saves)
            terminal.SetColor("bright_green");
            terminal.WriteLine("Available Saves:");
            terminal.WriteLine("");

            for (int i = 0; i < saves.Count && i < 10; i++) // Show up to 10 saves
            {
                var save = saves[i];
                terminal.SetColor("darkgray");
                terminal.Write($"[");
                terminal.SetColor("bright_cyan");
                terminal.Write($"{i + 1}");
                terminal.SetColor("darkgray");
                terminal.Write("] ");

                terminal.SetColor(save.IsAutosave ? "yellow" : "bright_green");
                terminal.Write($"{save.SaveType.PadRight(12)}");

                terminal.SetColor("gray");
                terminal.Write($" - Day {save.CurrentDay}, Level {save.Level}, {save.TurnsRemaining} turns");

                terminal.SetColor("darkgray");
                terminal.Write(" | ");

                terminal.SetColor("cyan");
                terminal.WriteLine(save.SaveTime.ToString("yyyy-MM-dd HH:mm:ss"));
            }

            terminal.WriteLine("");
            terminal.SetColor("darkgray");
            terminal.Write("[");
            terminal.SetColor("bright_red");
            terminal.Write("D");
            terminal.SetColor("darkgray");
            terminal.Write("] ");
            terminal.SetColor("red");
            terminal.WriteLine("Delete this character (all saves)");

            terminal.SetColor("darkgray");
            terminal.Write("[");
            terminal.SetColor("bright_red");
            terminal.Write("B");
            terminal.SetColor("darkgray");
            terminal.Write("] ");
            terminal.SetColor("red");
            terminal.WriteLine("Back");

            terminal.WriteLine("");
            terminal.SetColor("bright_white");
            var choice = await terminal.GetInput("Select save to load: ");

            // Handle numeric selection
            if (int.TryParse(choice, out int saveNumber) && saveNumber > 0 && saveNumber <= saves.Count)
            {
                var selectedSave = saves[saveNumber - 1];
                await LoadSaveByFileName(selectedSave.FileName);
                return;
            }

            // Handle letter commands
            switch (choice.ToUpper())
            {
                case "D":
                    terminal.WriteLine("");
                    var confirm = await terminal.GetInput($"Delete ALL saves for '{playerName}'? Type 'DELETE' to confirm: ");
                    if (confirm == "DELETE")
                    {
                        // Delete all saves for this player
                        foreach (var save in saves)
                        {
                            var filePath = System.IO.Path.Combine(
                                System.IO.Path.Combine(GetUserDataPath(), "saves"),
                                save.FileName);
                            try
                            {
                                System.IO.File.Delete(filePath);
                            }
                            catch (Exception ex)
                            {
                                GD.PrintErr($"Failed to delete {save.FileName}: {ex.Message}");
                            }
                        }
                        terminal.WriteLine("All saves deleted.", "green");
                        await Task.Delay(1500);
                        return;
                    }
                    break;

                case "B":
                    return;

                default:
                    terminal.WriteLine("Invalid choice!", "red");
                    await Task.Delay(1500);
                    break;
            }
        }
    }

    /// <summary>
    /// Load game by filename
    /// </summary>
    private async Task LoadSaveByFileName(string fileName)
    {
        try
        {
            terminal.WriteLine("Loading save...", "yellow");

            var saveData = await SaveSystem.Instance.LoadSaveByFileName(fileName);
            if (saveData == null)
            {
                terminal.WriteLine("Failed to load save file!", "red");
                terminal.WriteLine("The save file may be corrupted or invalid.", "yellow");
                await Task.Delay(2000);
                // In online/door mode, offer to create a new character instead of just exiting
                if (UsurperRemake.BBS.DoorMode.IsInDoorMode || UsurperRemake.BBS.DoorMode.IsOnlineMode)
                {
                    terminal.WriteLine("Starting new character instead...", "yellow");
                    await Task.Delay(1000);
                    await CreateNewGame(fileName);
                }
                return;
            }

            // Validate save data
            if (saveData.Player == null)
            {
                terminal.WriteLine("Save file is missing player data!", "red");
                await Task.Delay(3000);
                return;
            }

            terminal.WriteLine($"Restoring {saveData.Player.Name2 ?? saveData.Player.Name1}...", "green");
            await Task.Delay(500);

            // Log save data before restore
            DebugLogger.Instance.LogDebug("LOAD", $"Save file data - HP={saveData.Player.HP}/{saveData.Player.MaxHP}, BaseMaxHP={saveData.Player.BaseMaxHP}");

            // Restore player from save data
            currentPlayer = RestorePlayerFromSaveData(saveData.Player);

            if (currentPlayer == null)
            {
                DebugLogger.Instance.LogError("LOAD", "Failed to restore player data");
                terminal.WriteLine("Failed to restore player data!", "red");
                await Task.Delay(3000);
                return;
            }

            // Load daily system state
            if (dailyManager != null)
            {
                dailyManager.LoadFromSaveData(saveData);
            }

            // Restore world state
            await RestoreWorldState(saveData.WorldState);

            // Restore NPCs - from player's save as baseline
            await RestoreNPCs(saveData.NPCs);

            // In online mode, override NPCs and royal court with world_state (authoritative source).
            // The world sim maintains shared state 24/7 - player saves may be stale.
            if (UsurperRemake.BBS.DoorMode.IsOnlineMode && OnlineStateManager.Instance != null)
            {
                // Load current NPCs from world_state (teams, levels, equipment, etc.)
                var sharedNpcs = await OnlineStateManager.Instance.LoadSharedNPCs();
                if (sharedNpcs != null && sharedNpcs.Count > 0)
                {
                    await RestoreNPCs(sharedNpcs);
                    DebugLogger.Instance.LogInfo("ONLINE", $"NPCs overridden from world_state: {sharedNpcs.Count} NPCs loaded");
                }

                // Load current royal court from world_state
                await OnlineStateManager.Instance.LoadRoyalCourtFromWorldState();
            }

            // Restore story systems (companions, children, seals, etc.)
            SaveSystem.Instance.RestoreStorySystems(saveData.StorySystems);

            // In online mode, override children and marriages with world_state (authoritative source).
            // RestoreStorySystems loaded stale data from the player's save — the world sim may have
            // created new children and marriages while the player was offline.
            if (UsurperRemake.BBS.DoorMode.IsOnlineMode && OnlineStateManager.Instance != null)
            {
                await LoadSharedChildrenAndMarriages();
            }

            // Restore telemetry settings
            TelemetrySystem.Instance.Deserialize(saveData.Telemetry);

            terminal.WriteLine("Save loaded successfully!", "bright_green");
            await Task.Delay(1000);

            // Online mode: validate player's team still exists
            if (UsurperRemake.BBS.DoorMode.IsOnlineMode && !string.IsNullOrEmpty(currentPlayer.Team))
            {
                if (SaveSystem.Instance?.Backend is SqlSaveBackend teamBackend)
                {
                    // Check if team exists in player_teams or NPC teams
                    bool teamExists = teamBackend.IsTeamNameTaken(currentPlayer.Team) ||
                        NPCSpawnSystem.Instance.ActiveNPCs.Any(n => n.Team == currentPlayer.Team);
                    if (!teamExists)
                    {
                        terminal.SetColor("yellow");
                        terminal.WriteLine($"Your team '{currentPlayer.Team}' no longer exists. You have been removed.");
                        currentPlayer.Team = "";
                        currentPlayer.TeamPW = "";
                        currentPlayer.CTurf = false;
                        await Task.Delay(2000);
                    }
                }
            }

            // Show "While you were gone" summary for online players
            if (UsurperRemake.BBS.DoorMode.IsOnlineMode && SaveSystem.Instance?.Backend is SqlSaveBackend sqlBackend)
            {
                await ShowWhileYouWereGone(sqlBackend);
            }

            // Ensure quests are regenerated with corrected data
            QuestSystem.EnsureQuestsExist(currentPlayer?.Level ?? 10);

            // Start game at saved location
            await locationManager.EnterLocation(GameLocation.MainStreet, currentPlayer);
        }
        catch (Exception ex)
        {
            terminal.WriteLine($"Error loading save: {ex.Message}", "red");
            GD.PrintErr($"Failed to load save {fileName}: {ex}");
            UsurperRemake.Systems.DebugLogger.Instance.LogError("CRASH", $"Failed to load save {fileName}:\n{ex}");
            UsurperRemake.Systems.DebugLogger.Instance.Flush();
            await Task.Delay(3000);
        }
    }

    /// <summary>
    /// Load authoritative children and marriage data from world_state.
    /// Called after RestoreStorySystems to override stale player-save data with
    /// the world sim's current state. This ensures players see children born
    /// and marriages formed while they were offline.
    /// </summary>
    private async Task LoadSharedChildrenAndMarriages()
    {
        try
        {
            // Load children from world_state (maintained by WorldSimService)
            var sharedChildren = await OnlineStateManager.Instance.LoadSharedChildren();
            if (sharedChildren != null)
            {
                // Empty list is valid — means all children aged out or were converted to NPCs
                FamilySystem.Instance.DeserializeChildren(sharedChildren);
                DebugLogger.Instance.LogInfo("ONLINE", $"Children overridden from world_state: {sharedChildren.Count} children");
            }

            // Load marriages from world_state (maintained by WorldSimService)
            var (marriages, affairs) = await OnlineStateManager.Instance.LoadSharedMarriages();
            if (marriages != null)
            {
                NPCMarriageRegistry.Instance.RestoreMarriages(marriages);
                DebugLogger.Instance.LogInfo("ONLINE", $"Marriages overridden from world_state: {marriages.Count} marriages");
            }
            if (affairs != null)
            {
                NPCMarriageRegistry.Instance.RestoreAffairs(affairs);
                DebugLogger.Instance.LogInfo("ONLINE", $"Affairs overridden from world_state: {affairs.Count} affairs");
            }

            // Load world events from world_state so player sees active plagues, festivals, etc.
            var sharedEvents = await OnlineStateManager.Instance.LoadSharedWorldEvents();
            if (sharedEvents != null)
            {
                // Empty list is valid — means no active world events
                WorldEventSystem.Instance.RestoreFromSaveData(sharedEvents, 0);
                DebugLogger.Instance.LogInfo("ONLINE", $"World events loaded from world_state: {WorldEventSystem.Instance.GetActiveEvents().Count} active events");
            }
        }
        catch (Exception ex)
        {
            DebugLogger.Instance.LogError("ONLINE", $"Failed to load shared children/marriages/events: {ex.Message}");
        }
    }

    /// <summary>
    /// Show a "While you were gone" summary for online players on login.
    /// Displays PvP attacks, unread messages, and world news since last logout.
    /// </summary>
    private async Task ShowWhileYouWereGone(SqlSaveBackend backend)
    {
        try
        {
            var username = UsurperRemake.BBS.DoorMode.OnlineUsername;
            if (string.IsNullOrEmpty(username)) return;

            // Get the player's last logout time
            var lastLogout = await backend.GetLastLogoutTime(username);
            if (lastLogout == null) return; // First login ever, skip

            // Query all data sources
            var pvpAttacks = await backend.GetPvPAttacksAgainst(username, lastLogout.Value);
            var news = await backend.GetNewsSince(lastLogout.Value, 15);
            var allMessages = await backend.GetUnreadMessages(username);

            // Filter to direct messages only (not broadcasts)
            var directMessages = allMessages
                .Where(m => !string.Equals(m.ToPlayer, "*", StringComparison.OrdinalIgnoreCase))
                .Take(5)
                .ToList();

            // Get unread mail and pending trade counts
            int unreadMail = backend.GetUnreadMailCount(username);
            int pendingTrades = backend.GetPendingTradeOfferCount(username);

            // If nothing happened, skip silently
            if (pvpAttacks.Count == 0 && directMessages.Count == 0 && news.Count == 0
                && unreadMail == 0 && pendingTrades == 0)
                return;

            terminal.WriteLine("");
            terminal.SetColor("bright_cyan");
            terminal.WriteLine("\u2554\u2550\u2550\u2550\u2550\u2550\u2550\u2550\u2550\u2550\u2550\u2550\u2550\u2550\u2550\u2550\u2550\u2550\u2550\u2550\u2550\u2550\u2550\u2550\u2550\u2550\u2550\u2550\u2550\u2550\u2550\u2550\u2550\u2550\u2550\u2550\u2550\u2550\u2550\u2550\u2550\u2550\u2550\u2550\u2550\u2550\u2550\u2550\u2550\u2550\u2550\u2550\u2550\u2550\u2550\u2550\u2550\u2550\u2550\u2550\u2550\u2550\u2550\u2550\u2550\u2550\u2550\u2550\u2550\u2550\u2550\u2550\u2550\u2550\u2550\u2550\u2550\u2557");
            terminal.SetColor("bright_yellow");
            terminal.WriteLine("\u2551                         WHILE YOU WERE GONE                              \u2551");
            terminal.SetColor("bright_cyan");
            terminal.WriteLine("\u2560\u2550\u2550\u2550\u2550\u2550\u2550\u2550\u2550\u2550\u2550\u2550\u2550\u2550\u2550\u2550\u2550\u2550\u2550\u2550\u2550\u2550\u2550\u2550\u2550\u2550\u2550\u2550\u2550\u2550\u2550\u2550\u2550\u2550\u2550\u2550\u2550\u2550\u2550\u2550\u2550\u2550\u2550\u2550\u2550\u2550\u2550\u2550\u2550\u2550\u2550\u2550\u2550\u2550\u2550\u2550\u2550\u2550\u2550\u2550\u2550\u2550\u2550\u2550\u2550\u2550\u2550\u2550\u2550\u2550\u2550\u2550\u2550\u2550\u2550\u2550\u2550\u2563");

            // --- PvP Attacks Section ---
            if (pvpAttacks.Count > 0)
            {
                terminal.WriteLine("");
                terminal.SetColor("bright_yellow");
                terminal.WriteLine("  -- Arena Attacks --");
                foreach (var attack in pvpAttacks)
                {
                    bool defenderWon = attack.WinnerUsername.Equals(username, StringComparison.OrdinalIgnoreCase)
                        || attack.WinnerUsername.Equals(attack.DefenderName, StringComparison.OrdinalIgnoreCase);

                    if (defenderWon)
                    {
                        terminal.SetColor("bright_green");
                        string goldMsg = attack.GoldStolen > 0 ? $" You gained {attack.GoldStolen:N0} gold." : "";
                        terminal.WriteLine($"  * {attack.AttackerName} attacked you but your shadow defeated them!{goldMsg}");
                    }
                    else
                    {
                        terminal.SetColor("bright_red");
                        string goldMsg = attack.GoldStolen > 0 ? $" They stole {attack.GoldStolen:N0} of your gold." : "";
                        terminal.WriteLine($"  * {attack.AttackerName} attacked you and won!{goldMsg}");
                    }
                }
            }

            // --- Messages Section ---
            if (directMessages.Count > 0)
            {
                terminal.WriteLine("");
                terminal.SetColor("bright_yellow");
                terminal.WriteLine($"  -- Messages ({unreadMail} unread) --");
                foreach (var msg in directMessages)
                {
                    terminal.SetColor("cyan");
                    string msgText = msg.Message.Length > 60 ? msg.Message.Substring(0, 57) + "..." : msg.Message;
                    terminal.WriteLine($"  [{msg.FromPlayer}]: {msgText}");
                }
                if (unreadMail > directMessages.Count)
                {
                    terminal.SetColor("gray");
                    terminal.WriteLine($"  Type /mail to read your full mailbox.");
                }
            }
            else if (unreadMail > 0)
            {
                terminal.WriteLine("");
                terminal.SetColor("bright_yellow");
                terminal.WriteLine($"  -- Mail ({unreadMail} unread) --");
                terminal.SetColor("gray");
                terminal.WriteLine($"  Type /mail to read your mailbox.");
            }

            // --- Trade Packages Section ---
            if (pendingTrades > 0)
            {
                terminal.WriteLine("");
                terminal.SetColor("bright_yellow");
                terminal.WriteLine($"  -- Trade Packages ({pendingTrades} pending) --");
                terminal.SetColor("cyan");
                terminal.WriteLine($"  You have {pendingTrades} package{(pendingTrades != 1 ? "s" : "")} waiting!");
                terminal.SetColor("gray");
                terminal.WriteLine($"  Type /trade to view and accept.");
            }

            // --- World News Section ---
            if (news.Count > 0)
            {
                terminal.WriteLine("");
                terminal.SetColor("bright_yellow");
                terminal.WriteLine("  -- World News --");
                int newsShown = 0;
                foreach (var entry in news)
                {
                    if (newsShown >= 10) break;

                    string color = entry.Category?.ToLower() switch
                    {
                        "combat" => "red",
                        "pvp" => "bright_yellow",
                        "politics" => "bright_cyan",
                        "romance" => "magenta",
                        "economy" => "yellow",
                        "quest" => "green",
                        _ => "white"
                    };
                    terminal.SetColor(color);
                    string newsText = entry.Message.Length > 72 ? entry.Message.Substring(0, 69) + "..." : entry.Message;
                    terminal.WriteLine($"  * {newsText}");
                    newsShown++;
                }
            }

            terminal.WriteLine("");
            terminal.SetColor("bright_cyan");
            terminal.WriteLine("\u255a\u2550\u2550\u2550\u2550\u2550\u2550\u2550\u2550\u2550\u2550\u2550\u2550\u2550\u2550\u2550\u2550\u2550\u2550\u2550\u2550\u2550\u2550\u2550\u2550\u2550\u2550\u2550\u2550\u2550\u2550\u2550\u2550\u2550\u2550\u2550\u2550\u2550\u2550\u2550\u2550\u2550\u2550\u2550\u2550\u2550\u2550\u2550\u2550\u2550\u2550\u2550\u2550\u2550\u2550\u2550\u2550\u2550\u2550\u2550\u2550\u2550\u2550\u2550\u2550\u2550\u2550\u2550\u2550\u2550\u2550\u2550\u2550\u2550\u2550\u2550\u2550\u255d");
            terminal.WriteLine("");

            await terminal.PressAnyKey();
        }
        catch (Exception ex)
        {
            // Fail silently - this is a cosmetic feature that should never block login
            DebugLogger.Instance.LogError("WYWEG", $"Failed to show 'While you were gone': {ex.Message}");
        }
    }

    /// <summary>
    /// Get user data path (cross-platform)
    /// </summary>
    private string GetUserDataPath()
    {
        var appName = "UsurperReloaded";

        if (System.Environment.OSVersion.Platform == PlatformID.Win32NT)
        {
            return System.IO.Path.Combine(System.Environment.GetFolderPath(System.Environment.SpecialFolder.ApplicationData), appName);
        }
        else if (System.Environment.OSVersion.Platform == PlatformID.Unix)
        {
            var home = System.Environment.GetEnvironmentVariable("HOME");
            return System.IO.Path.Combine(home ?? "/tmp", ".local", "share", appName);
        }
        else
        {
            var home = System.Environment.GetEnvironmentVariable("HOME");
            return System.IO.Path.Combine(home ?? "/tmp", "Library", "Application Support", appName);
        }
    }
    
    /// <summary>
    /// Load existing game from save file
    /// </summary>
    private async Task LoadExistingGame(string playerName)
    {
        terminal.WriteLine("Loading game...", "yellow");

        // Clear dungeon party before loading to prevent state leak between saves
        ClearDungeonParty();

        var saveData = await SaveSystem.Instance.LoadGame(playerName);
        if (saveData == null)
        {
            terminal.WriteLine("Failed to load save file!", "red");
            await Task.Delay(2000);
            return;
        }

        // Restore player from save data
        currentPlayer = RestorePlayerFromSaveData(saveData.Player);
        
        // Load daily system state
        dailyManager.LoadFromSaveData(saveData);
        
        // Restore world state
        await RestoreWorldState(saveData.WorldState);
        
        // Restore NPCs - from player's save as baseline
        await RestoreNPCs(saveData.NPCs);

        // In online mode, override NPCs and royal court with world_state (authoritative source).
        // The world sim maintains shared state 24/7 - player saves may be stale.
        if (UsurperRemake.BBS.DoorMode.IsOnlineMode && OnlineStateManager.Instance != null)
        {
            // Load current NPCs from world_state (teams, levels, equipment, etc.)
            var sharedNpcs = await OnlineStateManager.Instance.LoadSharedNPCs();
            if (sharedNpcs != null && sharedNpcs.Count > 0)
            {
                await RestoreNPCs(sharedNpcs);
                DebugLogger.Instance.LogInfo("ONLINE", $"NPCs overridden from world_state: {sharedNpcs.Count} NPCs loaded");
            }

            // Load current royal court from world_state
            await OnlineStateManager.Instance.LoadRoyalCourtFromWorldState();
        }

        // Restore story systems (companions, children, seals, etc.)
        SaveSystem.Instance.RestoreStorySystems(saveData.StorySystems);

        // In online mode, override children and marriages with world_state (authoritative source).
        // RestoreStorySystems loaded stale data from the player's save — the world sim may have
        // created new children and marriages while the player was offline.
        if (UsurperRemake.BBS.DoorMode.IsOnlineMode && OnlineStateManager.Instance != null)
        {
            await LoadSharedChildrenAndMarriages();
        }

        // Restore telemetry settings
        TelemetrySystem.Instance.Deserialize(saveData.Telemetry);

        // Track session start if telemetry is enabled
        if (TelemetrySystem.Instance.IsEnabled)
        {
            TelemetrySystem.Instance.TrackSessionStart(
                GameConfig.Version,
                System.Environment.OSVersion.Platform.ToString()
            );

            // Identify user for PostHog dashboards (DAUs, WAUs, Retention)
            // This updates user properties and ensures they show up in daily/weekly counts
            TelemetrySystem.Instance.Identify(
                characterName: currentPlayer.Name,
                characterClass: currentPlayer.Class.ToString(),
                race: currentPlayer.Race.ToString(),
                level: currentPlayer.Level,
                difficulty: DifficultySystem.CurrentDifficulty.ToString()
            );
        }

        terminal.WriteLine($"Game loaded successfully! Day {saveData.CurrentDay}, {saveData.Player.TurnsRemaining} turns remaining", "green");
        await Task.Delay(1500);
        
        // Check if daily reset is needed after loading
        await dailyManager.CheckDailyReset();
        
        // Enter the game world
        await EnterGameWorld();
    }
    
    /// <summary>
    /// Create new game
    /// </summary>
    private async Task CreateNewGame(string playerName)
    {
        // Reset per-player session systems for new game
        UsurperRemake.Systems.RomanceTracker.Instance.Reset();
        UsurperRemake.Systems.CompanionSystem.Instance?.ResetAllCompanions();
        UsurperRemake.Systems.StoryProgressionSystem.Instance.FullReset();
        UsurperRemake.Systems.ArchetypeTracker.Instance.Reset();
        UsurperRemake.Systems.FactionSystem.Instance.Reset();

        // Reset narrative systems for new game
        UsurperRemake.Systems.StrangerEncounterSystem.Instance.Reset();
        UsurperRemake.Systems.TownNPCStorySystem.Instance.Reset();
        UsurperRemake.Systems.DreamSystem.Instance.Reset();
        UsurperRemake.Systems.OceanPhilosophySystem.Instance.Reset();
        UsurperRemake.Systems.GriefSystem.Instance.Reset();

        // In online mode, world-level systems (NPCs, children, marriages) are shared
        // across all players and managed by the WorldSimService. Don't reset them
        // when a single player creates a new character.
        if (!UsurperRemake.BBS.DoorMode.IsOnlineMode)
        {
            UsurperRemake.Systems.FamilySystem.Instance.Reset();
            UsurperRemake.Systems.NPCSpawnSystem.Instance.ResetNPCs();
            WorldInitializerSystem.Instance.ResetWorld();
            NPCMarriageRegistry.Instance.Reset();
            NPCDialogueDatabase.ClearAllTracking();
        }

        // Clear dungeon party from previous saves
        ClearDungeonParty();

        // Create new player using character creation system
        var newCharacter = await CreateNewPlayer(playerName);
        if (newCharacter == null)
        {
            return; // Player cancelled creation
        }

        currentPlayer = (Character)newCharacter;

        // Auto-populate quickbar with starting spells/abilities
        AutoPopulateQuickbar(currentPlayer);

        // Ask about telemetry opt-in for new players
        await PromptTelemetryOptIn();

        // Save the new game using the character's actual name (Name1)
        // This is important because playerName may be empty if coming from no-saves path
        string savePlayerName = !string.IsNullOrEmpty(currentPlayer.Name1) ? currentPlayer.Name1 : currentPlayer.Name2;
        var success = await SaveSystem.Instance.SaveGame(savePlayerName, currentPlayer);
        if (success)
        {
            terminal.WriteLine("New game saved successfully!", "green");
        }
        else
        {
            terminal.WriteLine("Warning: Failed to save game!", "red");
        }

        await Task.Delay(1500);

        // Online news: announce new adventurer
        if (UsurperRemake.Systems.OnlineStateManager.IsActive)
        {
            var displayName = currentPlayer.Name2 ?? currentPlayer.Name1;
            var className = currentPlayer.Class.ToString();
            _ = UsurperRemake.Systems.OnlineStateManager.Instance!.AddNews(
                $"A new adventurer arrives! {displayName} the {className} begins their journey.", "quest");
        }

        // Play the opening story sequence
        // This establishes the mystery, the goal, and hooks the player
        var openingSystem = OpeningStorySystem.Instance;
        if (StoryProgressionSystem.Instance.CurrentCycle > 1)
        {
            // NG+ has a different opening that acknowledges the cycle
            await openingSystem.PlayNewGamePlusOpening(currentPlayer, terminal);
        }
        else
        {
            // First playthrough - full opening sequence
            await openingSystem.PlayOpeningSequence(currentPlayer, terminal);
        }

        // Enter the game world
        await EnterGameWorld();
    }
    
    /// <summary>
    /// Enter the main game world
    /// </summary>
    private async Task EnterGameWorld()
    {
        if (currentPlayer == null) return;

        // Log game start
        bool isNewGame = currentPlayer.Statistics?.TotalSessionsPlayed <= 1;
        DebugLogger.Instance.LogGameStart(currentPlayer.Name, isNewGame);

        // Initialize NPCs only if they haven't been initialized yet
        // The NPCSpawnSystem has a guard flag to prevent duplicate spawning
        if (NPCSpawnSystem.Instance.ActiveNPCs.Count == 0)
        {
            await NPCSpawnSystem.Instance.InitializeClassicNPCs();

            // Initialize the world with simulated history (100 days of activity)
            // This creates teams, establishes a King, city control, guards, etc.
            if (!WorldInitializerSystem.Instance.IsWorldInitialized)
            {
                terminal.WriteLine("The world stirs with activity...", "cyan");
                await WorldInitializerSystem.Instance.InitializeWorld(100);
                terminal.WriteLine("History has been written. Your adventure begins!", "bright_green");
            }
        }

        // Ensure quests exist (also regenerates stale starter quests with corrected data)
        QuestSystem.EnsureQuestsExist();

        // Check if player is allowed to play
        if (!currentPlayer.Allowed)
        {
            terminal.WriteLine("You are not allowed to play today!", "red");
            await Task.Delay(2000);
            return;
        }

        // Check daily limits (but not in endless mode)
        if (dailyManager.CurrentMode != DailyCycleMode.Endless && !await CheckDailyLimits())
        {
            terminal.WriteLine($"You have used all your turns for today! ({currentPlayer.TurnsRemaining} left)", "red");
            await Task.Delay(2000);
            return;
        }

        // Check if player is in prison
        if (currentPlayer.DaysInPrison > 0)
        {
            await HandlePrison();
            return;
        }

        // Check if player is dead - handle death and continue playing
        if (!currentPlayer.IsAlive)
        {
            await HandleDeath();
            // After death handling, player is resurrected - continue to game
            // (HandleDeath sets HP > 0 and saves)
        }

        // Enter main game using location system
        // If player died and was resurrected, they'll start at the Inn
        var startLocation = currentPlayer.Location > 0
            ? (GameLocation)currentPlayer.Location
            : GameLocation.MainStreet;
        await locationManager.EnterLocation(startLocation, currentPlayer);
    }
    
    /// <summary>
    /// Restore player from save data
    /// </summary>
    private Character RestorePlayerFromSaveData(PlayerData playerData)
    {
        Character player = new Player
        {
            // Unique player identifier (critical for romance/family systems)
            ID = !string.IsNullOrEmpty(playerData.Id) ? playerData.Id : (playerData.Name2 ?? playerData.Name1 ?? Guid.NewGuid().ToString()),

            Name1 = playerData.Name1,
            Name2 = playerData.Name2,
            Level = playerData.Level,
            Experience = playerData.Experience,
            HP = playerData.HP,
            MaxHP = playerData.MaxHP,
            Gold = playerData.Gold,
            BankGold = playerData.BankGold,
            
            // Attributes
            Strength = playerData.Strength,
            Defence = playerData.Defence,
            Stamina = playerData.Stamina,
            Agility = playerData.Agility,
            Charisma = playerData.Charisma,
            Dexterity = playerData.Dexterity,
            Wisdom = playerData.Wisdom,
            Intelligence = playerData.Intelligence,
            Constitution = playerData.Constitution,
            Mana = playerData.Mana,
            MaxMana = playerData.MaxMana,

            // Equipment and items (CRITICAL FIXES)
            Healing = playerData.Healing,     // POTIONS
            ManaPotions = playerData.ManaPotions, // MANA POTIONS
            WeapPow = playerData.WeapPow,     // WEAPON POWER
            ArmPow = playerData.ArmPow,       // ARMOR POWER

            // Character details
            Race = playerData.Race,
            Class = playerData.Class,
            Sex = (CharacterSex)playerData.Sex,
            Age = playerData.Age,
            Difficulty = playerData.Difficulty,
            
            // Game state
            TurnCount = playerData.TurnCount,  // World simulation turn counter
            TurnsRemaining = playerData.TurnsRemaining,
            DaysInPrison = (byte)playerData.DaysInPrison,
            CellDoorOpen = playerData.CellDoorOpen,
            RescuedBy = playerData.RescuedBy ?? "",
            PrisonEscapes = (byte)playerData.PrisonEscapes,

            // Daily limits
            Fights = playerData.Fights,
            PFights = playerData.PFights,
            TFights = playerData.TFights,
            Thiefs = playerData.Thiefs,
            Brawls = playerData.Brawls,
            Assa = playerData.Assa,
            DarkNr = playerData.DarkNr > 0 ? playerData.DarkNr : GameConfig.DefaultDarkDeeds,
            ChivNr = playerData.ChivNr > 0 ? playerData.ChivNr : GameConfig.DefaultGoodDeeds,
            
            // Status
            Chivalry = playerData.Chivalry,
            Darkness = playerData.Darkness,
            Mental = playerData.Mental,
            Poison = playerData.Poison,

            // Active status effects (convert int keys back to StatusEffect enum)
            ActiveStatuses = playerData.ActiveStatuses?.ToDictionary(
                kvp => (StatusEffect)kvp.Key,
                kvp => kvp.Value
            ) ?? new Dictionary<StatusEffect, int>(),

            GnollP = playerData.GnollP,
            Addict = playerData.Addict,
            SteroidDays = playerData.SteroidDays,
            DrugEffectDays = playerData.DrugEffectDays,
            ActiveDrug = (DrugType)playerData.ActiveDrug,
            Mercy = playerData.Mercy,

            // Disease status
            Blind = playerData.Blind,
            Plague = playerData.Plague,
            Smallpox = playerData.Smallpox,
            Measles = playerData.Measles,
            Leprosy = playerData.Leprosy,
            LoversBane = playerData.LoversBane,

            // Divine Wrath System
            DivineWrathLevel = playerData.DivineWrathLevel,
            AngeredGodName = playerData.AngeredGodName ?? "",
            BetrayedForGodName = playerData.BetrayedForGodName ?? "",
            DivineWrathPending = playerData.DivineWrathPending,
            DivineWrathTurnsRemaining = playerData.DivineWrathTurnsRemaining,

            // Combat statistics (kill/death counts)
            MKills = playerData.MKills,
            MDefeats = playerData.MDefeats,
            PKills = playerData.PKills,
            PDefeats = playerData.PDefeats,

            // Character settings
            DevMenuUsed = playerData.DevMenuUsed,
            AutoHeal = playerData.AutoHeal,
            CombatSpeed = playerData.CombatSpeed,
            SkipIntimateScenes = playerData.SkipIntimateScenes,
            ScreenReaderMode = playerData.ScreenReaderMode,
            Loyalty = playerData.Loyalty,
            Haunt = playerData.Haunt,
            Master = playerData.Master,
            WellWish = playerData.WellWish,

            // Physical appearance
            Height = playerData.Height,
            Weight = playerData.Weight,
            Eyes = playerData.Eyes,
            Hair = playerData.Hair,
            Skin = playerData.Skin,

            // Ruler status
            King = playerData.King,

            // Social/Team
            Team = playerData.Team,
            TeamPW = playerData.TeamPassword,
            CTurf = playerData.IsTeamLeader,
            TeamRec = playerData.TeamRec,
            BGuard = playerData.BGuard,

            Allowed = true // Always allow loaded players
        };

        // Restore character flavor text
        if (playerData.Phrases?.Count > 0)
        {
            player.Phrases = playerData.Phrases;
        }

        if (playerData.Description?.Count > 0)
        {
            player.Description = playerData.Description;
        }
        
        // Restore items (ensure lists are always initialized)
        player.Item = playerData.Items?.Length > 0
            ? playerData.Items.ToList()
            : new List<int>();

        player.ItemType = playerData.ItemTypes?.Length > 0
            ? playerData.ItemTypes.Select(i => (ObjType)i).ToList()
            : new List<ObjType>();

        // Restore dynamic equipment FIRST (before EquippedItems, so IDs are registered)
        // In single-player, clear all dynamic equipment to avoid stale entries from previous saves.
        // In MUD/online mode, DO NOT clear — EquipmentDatabase is static and shared across all
        // player sessions. Clearing it wipes other players' equipment, causing items to disappear
        // or show wrong items in wrong slots (e.g., another player's longbow in your ring slot).
        if (!UsurperRemake.BBS.DoorMode.IsOnlineMode)
        {
            EquipmentDatabase.ClearDynamicEquipment();
        }

        if (playerData.DynamicEquipment != null && playerData.DynamicEquipment.Count > 0)
        {
            foreach (var equipData in playerData.DynamicEquipment)
            {
                var equipment = new Equipment
                {
                    Name = equipData.Name,
                    Description = equipData.Description ?? "",
                    Slot = (EquipmentSlot)equipData.Slot,
                    WeaponPower = equipData.WeaponPower,
                    ArmorClass = equipData.ArmorClass,
                    ShieldBonus = equipData.ShieldBonus,
                    BlockChance = equipData.BlockChance,
                    StrengthBonus = equipData.StrengthBonus,
                    DexterityBonus = equipData.DexterityBonus,
                    ConstitutionBonus = equipData.ConstitutionBonus,
                    IntelligenceBonus = equipData.IntelligenceBonus,
                    WisdomBonus = equipData.WisdomBonus,
                    CharismaBonus = equipData.CharismaBonus,
                    MaxHPBonus = equipData.MaxHPBonus,
                    MaxManaBonus = equipData.MaxManaBonus,
                    DefenceBonus = equipData.DefenceBonus,
                    MinLevel = equipData.MinLevel,
                    Value = equipData.Value,
                    IsCursed = equipData.IsCursed,
                    Rarity = (EquipmentRarity)equipData.Rarity,
                    WeaponType = (WeaponType)equipData.WeaponType,
                    Handedness = (WeaponHandedness)equipData.Handedness,
                    ArmorType = (ArmorType)equipData.ArmorType
                };
                // Register with the original ID so EquippedItems references still work
                EquipmentDatabase.RegisterDynamicWithId(equipment, equipData.Id);
            }
            // GD.Print($"[GameEngine] Restored {playerData.DynamicEquipment.Count} dynamic equipment items");
        }

        // NEW: Restore equipment system
        if (playerData.EquippedItems != null && playerData.EquippedItems.Count > 0)
        {
            player.EquippedItems = playerData.EquippedItems.ToDictionary(
                kvp => (EquipmentSlot)kvp.Key,
                kvp => kvp.Value
            );
        }

        // Restore curse status for equipped items
        player.WeaponCursed = playerData.WeaponCursed;
        player.ArmorCursed = playerData.ArmorCursed;
        player.ShieldCursed = playerData.ShieldCursed;

        // Restore player inventory (dungeon loot items)
        if (playerData.Inventory != null && playerData.Inventory.Count > 0)
        {
            player.Inventory = playerData.Inventory.Select(itemData => new Item
            {
                Name = itemData.Name,
                Value = itemData.Value,
                Type = itemData.Type,
                Attack = itemData.Attack,
                Armor = itemData.Armor,
                Strength = itemData.Strength,
                Dexterity = itemData.Dexterity,
                Wisdom = itemData.Wisdom,
                Defence = itemData.Defence,
                HP = itemData.HP,
                Mana = itemData.Mana,
                Charisma = itemData.Charisma,
                MinLevel = itemData.MinLevel,
                IsCursed = itemData.IsCursed,
                Cursed = itemData.Cursed,
                IsIdentified = itemData.IsIdentified,
                Shop = itemData.Shop,
                Dungeon = itemData.Dungeon,
                Description = itemData.Description?.ToList() ?? new List<string>()
            }).ToList();
        }

        // Restore base stats
        player.BaseStrength = playerData.BaseStrength > 0 ? playerData.BaseStrength : playerData.Strength;
        player.BaseDexterity = playerData.BaseDexterity > 0 ? playerData.BaseDexterity : playerData.Dexterity;
        player.BaseConstitution = playerData.BaseConstitution > 0 ? playerData.BaseConstitution : playerData.Constitution;
        player.BaseIntelligence = playerData.BaseIntelligence > 0 ? playerData.BaseIntelligence : playerData.Intelligence;
        player.BaseWisdom = playerData.BaseWisdom > 0 ? playerData.BaseWisdom : playerData.Wisdom;
        player.BaseCharisma = playerData.BaseCharisma > 0 ? playerData.BaseCharisma : playerData.Charisma;
        player.BaseMaxHP = playerData.BaseMaxHP > 0 ? playerData.BaseMaxHP : playerData.MaxHP;
        player.BaseMaxMana = playerData.BaseMaxMana > 0 ? playerData.BaseMaxMana : playerData.MaxMana;
        player.BaseDefence = playerData.BaseDefence > 0 ? playerData.BaseDefence : playerData.Defence;
        player.BaseStamina = playerData.BaseStamina > 0 ? playerData.BaseStamina : playerData.Stamina;
        player.BaseAgility = playerData.BaseAgility > 0 ? playerData.BaseAgility : playerData.Agility;

        // If this is an old save without equipment data, initialize from WeapPow/ArmPow
        if ((playerData.EquippedItems == null || playerData.EquippedItems.Count == 0)
            && (playerData.WeapPow > 0 || playerData.ArmPow > 0))
        {
            // Migration: Find best matching equipment based on WeapPow/ArmPow
            MigrateOldEquipmentToNew(player, playerData.WeapPow, playerData.ArmPow);
        }

        // Parse location
        if (int.TryParse(playerData.CurrentLocation, out var locationId))
        {
            player.Location = locationId;
        }

        // Restore romance tracker data
        if (playerData.RomanceData != null)
        {
            UsurperRemake.Systems.RomanceTracker.Instance.LoadFromSaveData(playerData.RomanceData);
        }

        // Restore learned combat abilities
        if (playerData.LearnedAbilities?.Count > 0)
        {
            player.LearnedAbilities = new HashSet<string>(playerData.LearnedAbilities);
        }

        // Restore combat quickbar (with migration for existing saves)
        if (playerData.Quickbar != null && playerData.Quickbar.Any(s => s != null))
        {
            player.Quickbar = playerData.Quickbar.ToList();
            while (player.Quickbar.Count < 9) player.Quickbar.Add(null);
        }
        else
        {
            AutoPopulateQuickbar(player);
        }

        // Restore training system
        player.Trains = playerData.Trains;
        player.TrainingPoints = playerData.TrainingPoints;
        if (playerData.SkillProficiencies?.Count > 0)
        {
            player.SkillProficiencies = playerData.SkillProficiencies.ToDictionary(
                kvp => kvp.Key,
                kvp => (TrainingSystem.ProficiencyLevel)kvp.Value);
        }
        if (playerData.SkillTrainingProgress?.Count > 0)
        {
            player.SkillTrainingProgress = new Dictionary<string, int>(playerData.SkillTrainingProgress);
        }

        // Restore spells and skills (ensure lists are never null)
        if (playerData.Spells?.Count > 0)
        {
            player.Spell = playerData.Spells;
        }
        else if (player.Spell == null)
        {
            player.Spell = new List<List<bool>>();
        }
        if (playerData.Skills?.Count > 0)
        {
            player.Skill = playerData.Skills;
        }
        else if (player.Skill == null)
        {
            player.Skill = new List<int>();
        }

        // Restore legacy equipment slots
        player.LHand = playerData.LHand;
        player.RHand = playerData.RHand;
        player.Head = playerData.Head;
        player.Body = playerData.Body;
        player.Arms = playerData.Arms;
        player.LFinger = playerData.LFinger;
        player.RFinger = playerData.RFinger;
        player.Legs = playerData.Legs;
        player.Feet = playerData.Feet;
        player.Waist = playerData.Waist;
        player.Neck = playerData.Neck;
        player.Neck2 = playerData.Neck2;
        player.Face = playerData.Face;
        player.Shield = playerData.Shield;
        player.Hands = playerData.Hands;
        player.ABody = playerData.ABody;

        // Restore combat flags
        player.Immortal = playerData.Immortal;
        player.BattleCry = playerData.BattleCry ?? "";
        player.BGuardNr = playerData.BGuardNr;

        // Restore gym cooldown timers
        player.LastStrengthTraining = playerData.LastStrengthTraining;
        player.LastDexterityTraining = playerData.LastDexterityTraining;
        player.LastTugOfWar = playerData.LastTugOfWar;
        player.LastWrestling = playerData.LastWrestling;

        // Set the global difficulty mode based on the loaded player
        DifficultySystem.CurrentDifficulty = player.Difficulty;

        // Load player statistics (or initialize if not present)
        if (playerData.Statistics != null)
        {
            player.Statistics = playerData.Statistics;
            player.Statistics.TrackNewSession();
        }
        else
        {
            player.Statistics = new PlayerStatistics();
            player.Statistics.TrackNewSession();
        }
        StatisticsManager.Current = player.Statistics;

        // Load player achievements (or initialize if not present)
        if (playerData.AchievementsData != null)
        {
            player.Achievements.UnlockedAchievements = new HashSet<string>(playerData.AchievementsData.UnlockedAchievements);
            player.Achievements.UnlockDates = new Dictionary<string, DateTime>(playerData.AchievementsData.UnlockDates);
        }
        else
        {
            player.Achievements = new PlayerAchievements();
        }

        // Initialize achievement system
        AchievementSystem.Initialize();

        // Restore Home Upgrade System (Gold Sinks)
        player.HomeLevel = playerData.HomeLevel;
        player.ChestLevel = playerData.ChestLevel;
        player.TrainingRoomLevel = playerData.TrainingRoomLevel;
        player.GardenLevel = playerData.GardenLevel;
        player.HasTrophyRoom = playerData.HasTrophyRoom;
        player.HasTeleportCircle = playerData.HasTeleportCircle;
        player.HasLegendaryArmory = playerData.HasLegendaryArmory;
        player.HasVitalityFountain = playerData.HasVitalityFountain;
        player.PermanentDamageBonus = playerData.PermanentDamageBonus;
        player.PermanentDefenseBonus = playerData.PermanentDefenseBonus;
        player.BonusMaxHP = playerData.BonusMaxHP;

        // Restore Recurring Duelist Rival
        if (playerData.RecurringDuelist != null)
        {
            string playerId = player.ID ?? player.Name;
            DungeonLocation.RestoreRecurringDuelist(playerId,
                playerData.RecurringDuelist.Name,
                playerData.RecurringDuelist.Weapon,
                playerData.RecurringDuelist.Level,
                playerData.RecurringDuelist.TimesEncountered,
                playerData.RecurringDuelist.PlayerWins,
                playerData.RecurringDuelist.PlayerLosses,
                playerData.RecurringDuelist.WasInsulted,
                playerData.RecurringDuelist.IsDead);
        }

        // Restore dungeon progression (cleared boss/seal floors)
        if (playerData.ClearedSpecialFloors != null)
        {
            player.ClearedSpecialFloors = playerData.ClearedSpecialFloors;
        }

        // RESET dungeon floor states on every load - dungeons regenerate fresh each session
        // This prevents stale room states (like boss rooms showing [CLEARED] incorrectly)
        // Note: ClearedSpecialFloors (seals, Old Gods) is still preserved above for permanent progress
        player.DungeonFloorStates = new Dictionary<int, UsurperRemake.Systems.DungeonFloorState>();
        DebugLogger.Instance.LogDebug("LOAD", "Dungeon floor states reset - dungeons will regenerate fresh");

        // Restore hint system (which hints have been shown to this player)
        if (playerData.HintsShown != null)
        {
            player.HintsShown = playerData.HintsShown;
        }

        // CRITICAL: Recalculate stats to apply equipment bonuses from loaded items
        // This ensures WeapPow, ArmPow, and all stat bonuses are correctly applied
        //
        // BUG FIX: We must preserve HP/Mana before RecalculateStats because:
        // 1. RecalculateStats sets MaxHP = BaseMaxHP first (which is lower than final MaxHP)
        // 2. Equipment's ApplyToCharacter sees HP > MaxHP and clamps it down
        // 3. Then constitution/equipment bonuses raise MaxHP back up
        // 4. But HP is already clamped to the intermediate lower value
        //
        // Solution: Save HP/Mana, recalculate, then restore and clamp to final MaxHP
        var savedHP = player.HP;
        var savedMana = player.Mana;

        player.RecalculateStats();

        // Restore the saved HP/Mana, clamped to the newly calculated MaxHP/MaxMana
        player.HP = Math.Min(savedHP, player.MaxHP);
        player.Mana = Math.Min(savedMana, player.MaxMana);

        // Log successful restore
        DebugLogger.Instance.LogLoad(player.Name, player.Level, player.HP, player.MaxHP, player.Gold);
        DebugLogger.Instance.LogDebug("LOAD", $"Stats: STR={player.Strength} DEF={player.Defence} WeapPow={player.WeapPow} ArmPow={player.ArmPow}");

        return player;
    }

    /// <summary>
    /// Restore dungeon floor states from save data
    /// </summary>
    private Dictionary<int, UsurperRemake.Systems.DungeonFloorState> RestoreDungeonFloorStates(
        Dictionary<int, DungeonFloorStateData> savedStates)
    {
        var result = new Dictionary<int, UsurperRemake.Systems.DungeonFloorState>();

        foreach (var kvp in savedStates)
        {
            var saved = kvp.Value;
            var state = new UsurperRemake.Systems.DungeonFloorState
            {
                FloorLevel = saved.FloorLevel,
                LastClearedAt = saved.LastClearedAt,
                LastVisitedAt = saved.LastVisitedAt,
                EverCleared = saved.EverCleared,
                IsPermanentlyClear = saved.IsPermanentlyClear,
                BossDefeated = saved.BossDefeated,
                CurrentRoomId = saved.CurrentRoomId,
                RoomStates = new Dictionary<string, UsurperRemake.Systems.DungeonRoomState>()
            };

            foreach (var roomData in saved.Rooms)
            {
                state.RoomStates[roomData.RoomId] = new UsurperRemake.Systems.DungeonRoomState
                {
                    RoomId = roomData.RoomId,
                    IsExplored = roomData.IsExplored,
                    IsCleared = roomData.IsCleared,
                    TreasureLooted = roomData.TreasureLooted,
                    TrapTriggered = roomData.TrapTriggered,
                    EventCompleted = roomData.EventCompleted,
                    PuzzleSolved = roomData.PuzzleSolved,
                    RiddleAnswered = roomData.RiddleAnswered,
                    LoreCollected = roomData.LoreCollected,
                    InsightGranted = roomData.InsightGranted,
                    MemoryTriggered = roomData.MemoryTriggered,
                    SecretBossDefeated = roomData.SecretBossDefeated
                };
            }

            result[kvp.Key] = state;
        }

        return result;
    }

    /// <summary>
    /// Migrate old WeapPow/ArmPow to new equipment system for old saves
    /// </summary>
    private void MigrateOldEquipmentToNew(Character player, long weapPow, long armPow)
    {
        // Find best matching weapon for WeapPow
        if (weapPow > 0)
        {
            var weapons = EquipmentDatabase.GetWeaponsByHandedness(WeaponHandedness.OneHanded);
            var bestWeapon = weapons
                .Where(w => w.WeaponPower <= weapPow)
                .OrderByDescending(w => w.WeaponPower)
                .FirstOrDefault();

            if (bestWeapon != null)
            {
                player.EquippedItems[EquipmentSlot.MainHand] = bestWeapon.Id;
            }
        }

        // Find best matching armor for ArmPow
        if (armPow > 0)
        {
            var armors = EquipmentDatabase.GetBySlot(EquipmentSlot.Body);
            var bestArmor = armors
                .Where(a => a.ArmorClass <= armPow)
                .OrderByDescending(a => a.ArmorClass)
                .FirstOrDefault();

            if (bestArmor != null)
            {
                player.EquippedItems[EquipmentSlot.Body] = bestArmor.Id;
            }
        }

        // Initialize base stats
        player.InitializeBaseStats();
    }
    
    /// <summary>
    /// Restore world state from save data
    /// </summary>
    private async Task RestoreWorldState(WorldStateData worldState)
    {
        if (worldState == null)
        {
            // GD.Print("[GameEngine] No world state to restore");
            return;
        }

        // Restore economic state
        // This would integrate with bank and economy systems

        // Restore political state
        if (!string.IsNullOrEmpty(worldState.CurrentRuler))
        {
            // Set current ruler if applicable
        }

        // Restore active world events from save data
        var currentDay = dailyManager?.CurrentDay ?? 1;
        WorldEventSystem.Instance.RestoreFromSaveData(worldState.ActiveEvents, currentDay);

        // Restore active quests from save data
        if (worldState.ActiveQuests != null && worldState.ActiveQuests.Count > 0)
        {
            QuestSystem.RestoreFromSaveData(worldState.ActiveQuests);
        }

        // Restore marketplace listings from save data
        if (worldState.MarketplaceListings != null && worldState.MarketplaceListings.Count > 0)
        {
            UsurperRemake.Systems.MarketplaceSystem.Instance.LoadFromSaveData(worldState.MarketplaceListings);
            // GD.Print($"[GameEngine] Restored {worldState.MarketplaceListings.Count} marketplace listings");
        }

        // GD.Print($"[GameEngine] World state restored: {worldState.ActiveEvents?.Count ?? 0} active events, {worldState.ActiveQuests?.Count ?? 0} quests");
        await Task.CompletedTask;
    }
    
    /// <summary>
    /// Restore NPCs from save data
    /// </summary>
    private async Task RestoreNPCs(List<NPCData> npcData)
    {
        if (npcData == null || npcData.Count == 0)
        {
            GD.Print("No NPC data to restore - will use fresh NPCs");
            return;
        }

        // Clear existing NPCs before restoring
        NPCSpawnSystem.Instance.ClearAllNPCs();

        NPC kingNpc = null;

        foreach (var data in npcData)
        {
            // Create NPC from save data
            var npc = new NPC
            {
                Id = data.Id,
                ID = !string.IsNullOrEmpty(data.CharacterID) ? data.CharacterID : $"npc_{data.Name.ToLower().Replace(" ", "_")}",  // Restore Character.ID (or generate if missing)
                Name1 = data.Name,
                Name2 = data.Name,
                Level = data.Level,
                HP = data.HP,
                MaxHP = data.MaxHP,
                BaseMaxHP = data.BaseMaxHP > 0 ? data.BaseMaxHP : data.MaxHP,  // Fallback to MaxHP if not saved
                BaseMaxMana = data.BaseMaxMana > 0 ? data.BaseMaxMana : data.MaxMana,  // Fallback to MaxMana if not saved
                CurrentLocation = data.Location,

                // Stats
                Experience = data.Experience,
                Strength = data.Strength,
                Defence = data.Defence,
                Agility = data.Agility,
                Dexterity = data.Dexterity,
                Mana = data.Mana,
                MaxMana = data.MaxMana,
                WeapPow = data.WeapPow,
                ArmPow = data.ArmPow,

                // Base stats - CRITICAL for RecalculateStats to work correctly
                // Fallback to current stats if base stats not saved (legacy save compatibility)
                BaseStrength = data.BaseStrength > 0 ? data.BaseStrength : data.Strength,
                BaseDefence = data.BaseDefence > 0 ? data.BaseDefence : data.Defence,
                BaseDexterity = data.BaseDexterity > 0 ? data.BaseDexterity : data.Dexterity,
                BaseAgility = data.BaseAgility > 0 ? data.BaseAgility : data.Agility,
                BaseStamina = data.BaseStamina > 0 ? data.BaseStamina : 50,  // Default stamina
                BaseConstitution = data.BaseConstitution > 0 ? data.BaseConstitution : 10 + data.Level * 2,
                BaseIntelligence = data.BaseIntelligence > 0 ? data.BaseIntelligence : 10,
                BaseWisdom = data.BaseWisdom > 0 ? data.BaseWisdom : 10,
                BaseCharisma = data.BaseCharisma > 0 ? data.BaseCharisma : 10,

                // Class and race
                Class = data.Class,
                Race = data.Race,
                Sex = (CharacterSex)data.Sex,

                // Team and political status
                Team = data.Team,
                CTurf = data.IsTeamLeader,

                // Death status - permanent death tracking
                IsDead = data.IsDead,

                // Lifecycle - aging and natural death
                Age = data.Age > 0 ? data.Age : new Random().Next(18, 50),
                BirthDate = data.BirthDate > DateTime.MinValue
                    ? data.BirthDate
                    : DateTime.Now.AddHours(-(data.Age > 0 ? data.Age : new Random().Next(18, 50)) * GameConfig.NpcLifecycleHoursPerYear),
                IsAgedDeath = data.IsAgedDeath,
                PregnancyDueDate = data.PregnancyDueDate,

                // Marriage status
                IsMarried = data.IsMarried,
                Married = data.Married,
                SpouseName = data.SpouseName ?? "",
                MarriedTimes = data.MarriedTimes,

                // Faction affiliation
                NPCFaction = data.NPCFaction >= 0 ? (UsurperRemake.Systems.Faction)data.NPCFaction : null,

                // Alignment
                Chivalry = data.Chivalry,
                Darkness = data.Darkness,

                // Inventory
                Gold = data.Gold,
                BankGold = data.BankGold,
                AI = CharacterAI.Computer
            };

            // Restore items
            if (data.Items != null && data.Items.Length > 0)
            {
                npc.Item = data.Items.ToList();
            }

            // Restore market inventory for NPC trading
            if (data.MarketInventory != null && data.MarketInventory.Count > 0)
            {
                // Ensure MarketInventory is initialized
                if (npc.MarketInventory == null)
                {
                    npc.MarketInventory = new List<Item>();
                }
                foreach (var itemData in data.MarketInventory)
                {
                    var item = new global::Item
                    {
                        Name = itemData.ItemName,
                        Value = itemData.ItemValue,
                        Type = itemData.ItemType,
                        Attack = itemData.Attack,
                        Armor = itemData.Armor,
                        Strength = itemData.Strength,
                        Defence = itemData.Defence,
                        IsCursed = itemData.IsCursed
                    };
                    npc.MarketInventory.Add(item);
                }
            }

            // Restore personality profile if available, then initialize AI systems
            if (data.PersonalityProfile != null)
            {
                // Reconstruct PersonalityProfile from saved PersonalityData
                npc.Personality = new PersonalityProfile
                {
                    // Core traits
                    Aggression = data.PersonalityProfile.Aggression,
                    Loyalty = data.PersonalityProfile.Loyalty,
                    Intelligence = data.PersonalityProfile.Intelligence,
                    Greed = data.PersonalityProfile.Greed,
                    Sociability = data.PersonalityProfile.Compassion, // Compassion maps to Sociability
                    Courage = data.PersonalityProfile.Courage,
                    Trustworthiness = data.PersonalityProfile.Honesty, // Honesty maps to Trustworthiness
                    Ambition = data.PersonalityProfile.Ambition,
                    Vengefulness = data.PersonalityProfile.Vengefulness,
                    Impulsiveness = data.PersonalityProfile.Impulsiveness,
                    Caution = data.PersonalityProfile.Caution,
                    Mysticism = data.PersonalityProfile.Mysticism,
                    Patience = data.PersonalityProfile.Patience,
                    Archetype = data.Archetype ?? "Balanced",

                    // Romance/Intimacy traits
                    Gender = data.PersonalityProfile.Gender,
                    Orientation = data.PersonalityProfile.Orientation,
                    IntimateStyle = data.PersonalityProfile.IntimateStyle,
                    RelationshipPref = data.PersonalityProfile.RelationshipPref,
                    Romanticism = data.PersonalityProfile.Romanticism,
                    Sensuality = data.PersonalityProfile.Sensuality,
                    Jealousy = data.PersonalityProfile.Jealousy,
                    Commitment = data.PersonalityProfile.Commitment,
                    Adventurousness = data.PersonalityProfile.Adventurousness,
                    Exhibitionism = data.PersonalityProfile.Exhibitionism,
                    Voyeurism = data.PersonalityProfile.Voyeurism,
                    Flirtatiousness = data.PersonalityProfile.Flirtatiousness,
                    Passion = data.PersonalityProfile.Passion,
                    Tenderness = data.PersonalityProfile.Tenderness
                };
                npc.Archetype = data.Archetype ?? "citizen";
            }
            else
            {
                npc.Archetype = data.Archetype ?? "citizen";
            }

            // Initialize AI systems now that name and archetype are set
            // This will use the restored personality if available, or generate one if not
            npc.EnsureSystemsInitialized();

            // Restore NPC memories, goals, and emotional state from saved data
            // This must happen AFTER EnsureSystemsInitialized creates the Brain
            if (npc.Brain != null)
            {
                // Restore memories
                if (data.Memories != null && data.Memories.Count > 0)
                {
                    foreach (var memData in data.Memories)
                    {
                        if (Enum.TryParse<MemoryType>(memData.Type, out var memType))
                        {
                            var memory = new MemoryEvent
                            {
                                Type = memType,
                                Description = memData.Description,
                                InvolvedCharacter = memData.InvolvedCharacter,
                                Timestamp = memData.Timestamp,
                                Importance = memData.Importance,
                                EmotionalImpact = memData.EmotionalImpact
                            };
                            npc.Brain.Memory?.RecordEvent(memory);
                        }
                    }
                    // GD.Print($"[GameEngine] Restored {data.Memories.Count} memories for {npc.Name}");
                }

                // Restore goals
                if (data.CurrentGoals != null && data.CurrentGoals.Count > 0)
                {
                    foreach (var goalData in data.CurrentGoals)
                    {
                        if (Enum.TryParse<GoalType>(goalData.Type, out var goalType))
                        {
                            var goal = new Goal(goalData.Name, goalType, goalData.Priority)
                            {
                                Progress = goalData.Progress,
                                IsActive = goalData.IsActive,
                                TargetValue = goalData.TargetValue,
                                CurrentValue = goalData.CurrentValue,
                                CreatedTime = goalData.CreatedTime
                            };
                            npc.Brain.Goals?.AddGoal(goal);
                        }
                    }
                    // GD.Print($"[GameEngine] Restored {data.CurrentGoals.Count} goals for {npc.Name}");
                }

                // Restore emotional state
                if (data.EmotionalState != null)
                {
                    if (data.EmotionalState.Happiness > 0)
                        npc.Brain.Emotions?.AddEmotion(EmotionType.Joy, data.EmotionalState.Happiness, 120);
                    if (data.EmotionalState.Anger > 0)
                        npc.Brain.Emotions?.AddEmotion(EmotionType.Anger, data.EmotionalState.Anger, 120);
                    if (data.EmotionalState.Fear > 0)
                        npc.Brain.Emotions?.AddEmotion(EmotionType.Fear, data.EmotionalState.Fear, 120);
                    if (data.EmotionalState.Trust > 0)
                        npc.Brain.Emotions?.AddEmotion(EmotionType.Gratitude, data.EmotionalState.Trust, 120);
                    if (data.EmotionalState.Confidence > 0)
                        npc.Brain.Emotions?.AddEmotion(EmotionType.Confidence, data.EmotionalState.Confidence, 120);
                    if (data.EmotionalState.Sadness > 0)
                        npc.Brain.Emotions?.AddEmotion(EmotionType.Sadness, data.EmotionalState.Sadness, 120);
                    if (data.EmotionalState.Greed > 0)
                        npc.Brain.Emotions?.AddEmotion(EmotionType.Greed, data.EmotionalState.Greed, 120);
                    if (data.EmotionalState.Loneliness > 0)
                        npc.Brain.Emotions?.AddEmotion(EmotionType.Loneliness, data.EmotionalState.Loneliness, 120);
                    if (data.EmotionalState.Envy > 0)
                        npc.Brain.Emotions?.AddEmotion(EmotionType.Envy, data.EmotionalState.Envy, 120);
                    if (data.EmotionalState.Pride > 0)
                        npc.Brain.Emotions?.AddEmotion(EmotionType.Pride, data.EmotionalState.Pride, 120);
                    if (data.EmotionalState.Hope > 0)
                        npc.Brain.Emotions?.AddEmotion(EmotionType.Hope, data.EmotionalState.Hope, 120);
                    if (data.EmotionalState.Peace > 0)
                        npc.Brain.Emotions?.AddEmotion(EmotionType.Peace, data.EmotionalState.Peace, 120);
                }
            }

            // Restore enemy list
            if (data.Enemies != null && data.Enemies.Count > 0)
            {
                npc.Enemies = new List<string>(data.Enemies);
            }

            // Fix Experience if it's 0 - legacy saves may not have tracked NPC XP
            // NPCs need proper XP to level up correctly from combat
            if (npc.Experience <= 0 && npc.Level > 1)
            {
                npc.Experience = GetExperienceForNPCLevel(npc.Level);
                // GD.Print($"[GameEngine] Initialized {npc.Name}'s XP to {npc.Experience} for level {npc.Level}");
            }

            // Initialize base stats if they're not set (legacy save compatibility)
            // This ensures RecalculateStats() works correctly after level-ups
            if (npc.BaseMaxHP <= 0)
            {
                npc.BaseMaxHP = npc.MaxHP;
                npc.BaseStrength = npc.Strength;
                npc.BaseDefence = npc.Defence;
                npc.BaseDexterity = npc.Dexterity;
                npc.BaseAgility = npc.Agility;
                npc.BaseStamina = npc.Stamina;
                npc.BaseConstitution = npc.Constitution;
                npc.BaseIntelligence = npc.Intelligence;
                npc.BaseWisdom = npc.Wisdom;
                npc.BaseCharisma = npc.Charisma;
                npc.BaseMaxMana = npc.MaxMana;
            }

            // Restore recently-used dialogue IDs to prevent repetition across sessions
            if (data.RecentDialogueIds != null && data.RecentDialogueIds.Count > 0)
            {
                npc.RecentDialogueIds = new List<string>(data.RecentDialogueIds);
                NPCDialogueDatabase.RestoreRecentlyUsedIds(npc.Name2 ?? npc.Name1 ?? "", data.RecentDialogueIds);
            }

            // Migrate: Assign faction to NPCs that don't have one (legacy save compatibility)
            if (!npc.NPCFaction.HasValue)
            {
                npc.NPCFaction = DetermineFactionForNPC(npc);
                if (npc.NPCFaction.HasValue)
                {
                    GD.Print($"[GameEngine] Migrated {npc.Name} to faction {npc.NPCFaction.Value}");
                }
            }

            // Restore dynamic equipment FIRST (before EquippedItems, so IDs are registered)
            if (data.DynamicEquipment != null && data.DynamicEquipment.Count > 0)
            {
                foreach (var equipData in data.DynamicEquipment)
                {
                    var equipment = new Equipment
                    {
                        Name = equipData.Name,
                        Description = equipData.Description ?? "",
                        Slot = (EquipmentSlot)equipData.Slot,
                        WeaponPower = equipData.WeaponPower,
                        ArmorClass = equipData.ArmorClass,
                        ShieldBonus = equipData.ShieldBonus,
                        BlockChance = equipData.BlockChance,
                        StrengthBonus = equipData.StrengthBonus,
                        DexterityBonus = equipData.DexterityBonus,
                        ConstitutionBonus = equipData.ConstitutionBonus,
                        IntelligenceBonus = equipData.IntelligenceBonus,
                        WisdomBonus = equipData.WisdomBonus,
                        CharismaBonus = equipData.CharismaBonus,
                        MaxHPBonus = equipData.MaxHPBonus,
                        MaxManaBonus = equipData.MaxManaBonus,
                        DefenceBonus = equipData.DefenceBonus,
                        MinLevel = equipData.MinLevel,
                        Value = equipData.Value,
                        IsCursed = equipData.IsCursed,
                        Rarity = (EquipmentRarity)equipData.Rarity,
                        WeaponType = (WeaponType)equipData.WeaponType,
                        Handedness = (WeaponHandedness)equipData.Handedness,
                        ArmorType = (ArmorType)equipData.ArmorType
                    };

                    // Register and get the new ID (may differ from saved ID)
                    int newId = EquipmentDatabase.RegisterDynamic(equipment);

                    // Update the EquippedItems dictionary to use the new ID
                    if (data.EquippedItems != null)
                    {
                        foreach (var slot in data.EquippedItems.Keys.ToList())
                        {
                            if (data.EquippedItems[slot] == equipData.Id)
                            {
                                data.EquippedItems[slot] = newId;
                            }
                        }
                    }
                }
            }

            // Restore equipped items
            if (data.EquippedItems != null && data.EquippedItems.Count > 0)
            {
                foreach (var kvp in data.EquippedItems)
                {
                    var slot = (EquipmentSlot)kvp.Key;
                    var equipmentId = kvp.Value;
                    npc.EquippedItems[slot] = equipmentId;
                }
            }

            // CRITICAL: Validate and fix base stats before RecalculateStats
            // Old saves or corrupted NPCs may have 0 base stats, which causes
            // RecalculateStats to zero out all stats (STR: 0, DEF: -18 issues)
            ValidateAndFixNPCBaseStats(npc);

            // Now recalculate stats with valid base stats
            npc.RecalculateStats();

            // Sanity check: ensure NPC has valid HP (fix corrupted saves)
            long minHP = 20 + (npc.Level * 10);
            if (npc.MaxHP < minHP)
            {
                UsurperRemake.Systems.DebugLogger.Instance.LogWarning("NPC", $"Fixing corrupted NPC {npc.Name}: MaxHP={npc.MaxHP}, BaseMaxHP={npc.BaseMaxHP}, resetting to {minHP}");
                npc.BaseMaxHP = minHP;
                npc.MaxHP = minHP;
                if (npc.HP < 0 || npc.HP > npc.MaxHP)
                {
                    npc.HP = npc.IsDead ? 0 : npc.MaxHP;
                }
            }

            // Add to spawn system
            NPCSpawnSystem.Instance.AddRestoredNPC(npc);

            // Track who was king
            if (data.IsKing)
            {
                kingNpc = npc;
            }
        }

        // Restore the king if there was one
        if (kingNpc != null)
        {
            global::CastleLocation.SetCurrentKing(kingNpc);
            GD.Print($"Restored king: {kingNpc.Name}");
        }

        // Mark NPCs as initialized so they don't get re-created
        NPCSpawnSystem.Instance.MarkAsInitialized();

        // Process dead NPCs for respawn - this queues them with a faster timer
        // since they've been dead since the last save
        var deadCount = npcData.Count(n => n.IsDead);
        UsurperRemake.Systems.DebugLogger.Instance.LogInfo("NPC", $"Restoring {npcData.Count} NPCs, {deadCount} are dead");

        if (worldSimulator != null)
        {
            worldSimulator.ProcessDeadNPCsOnLoad();
        }
        else
        {
            UsurperRemake.Systems.DebugLogger.Instance.LogWarning("NPC", "worldSimulator is null - cannot process dead NPCs!");
        }

        GD.Print($"Restored {npcData.Count} NPCs from save data");
        await Task.CompletedTask;
    }

    /// <summary>
    /// Validate and fix NPC base stats if they are invalid (0 or negative).
    /// This is critical for saves from older versions where base stats weren't saved,
    /// or for corrupted NPCs. Without valid base stats, RecalculateStats() will
    /// zero out all stats causing STR: 0, DEF: -18 type issues.
    /// </summary>
    private void ValidateAndFixNPCBaseStats(NPC npc)
    {
        bool needsFix = false;
        int level = npc.Level > 0 ? npc.Level : 1;

        // Check if base stats are invalid (0 or negative)
        if (npc.BaseStrength <= 0)
        {
            // Calculate reasonable base strength for level and class
            npc.BaseStrength = 10 + (level * 5);
            if (npc.Class == CharacterClass.Warrior || npc.Class == CharacterClass.Barbarian)
                npc.BaseStrength += level * 2;
            needsFix = true;
        }

        if (npc.BaseDefence <= 0)
        {
            npc.BaseDefence = 10 + (level * 3);
            needsFix = true;
        }

        if (npc.BaseAgility <= 0)
        {
            npc.BaseAgility = 10 + (level * 3);
            needsFix = true;
        }

        if (npc.BaseDexterity <= 0)
        {
            npc.BaseDexterity = 10 + (level * 2);
            if (npc.Class == CharacterClass.Assassin)
                npc.BaseDexterity += level * 3;
            needsFix = true;
        }

        if (npc.BaseStamina <= 0)
        {
            npc.BaseStamina = 10 + (level * 4);
            needsFix = true;
        }

        if (npc.BaseConstitution <= 0)
        {
            npc.BaseConstitution = 10 + (level * 2);
            needsFix = true;
        }

        if (npc.BaseIntelligence <= 0)
        {
            npc.BaseIntelligence = 10 + (level * 2);
            if (npc.Class == CharacterClass.Magician)
                npc.BaseIntelligence += level * 3;
            needsFix = true;
        }

        if (npc.BaseWisdom <= 0)
        {
            npc.BaseWisdom = 10 + (level * 2);
            if (npc.Class == CharacterClass.Cleric || npc.Class == CharacterClass.Paladin)
                npc.BaseWisdom += level * 2;
            needsFix = true;
        }

        if (npc.BaseCharisma <= 0)
        {
            npc.BaseCharisma = 10;
            needsFix = true;
        }

        if (npc.BaseMaxHP <= 0)
        {
            // Calculate based on class
            npc.BaseMaxHP = npc.Class switch
            {
                CharacterClass.Warrior or CharacterClass.Barbarian => 100 + (level * 50),
                CharacterClass.Magician => 50 + (level * 25),
                CharacterClass.Cleric or CharacterClass.Paladin => 80 + (level * 40),
                CharacterClass.Assassin => 70 + (level * 35),
                CharacterClass.Sage => 90 + (level * 45),
                _ => 80 + (level * 40)
            };
            needsFix = true;
        }

        if (npc.BaseMaxMana <= 0 && (npc.Class == CharacterClass.Magician ||
            npc.Class == CharacterClass.Cleric || npc.Class == CharacterClass.Paladin ||
            npc.Class == CharacterClass.Sage))
        {
            npc.BaseMaxMana = npc.Class switch
            {
                CharacterClass.Magician => 50 + (level * 30),
                CharacterClass.Cleric or CharacterClass.Paladin => 40 + (level * 20),
                CharacterClass.Sage => 30 + (level * 15),
                _ => 0
            };
            needsFix = true;
        }

        if (needsFix)
        {
            UsurperRemake.Systems.DebugLogger.Instance.LogWarning("NPC",
                $"Fixed corrupted base stats for {npc.Name} (Level {level} {npc.Class}): " +
                $"STR={npc.BaseStrength}, DEF={npc.BaseDefence}, AGI={npc.BaseAgility}");
        }
    }

    /// <summary>
    /// XP formula matching the player's curve (level^1.8 * 50)
    /// Used to initialize NPC XP when loading legacy saves
    /// </summary>
    private static long GetExperienceForNPCLevel(int level)
    {
        if (level <= 1) return 0;
        long exp = 0;
        for (int i = 2; i <= level; i++)
        {
            exp += (long)(Math.Pow(i, 1.8) * 50);
        }
        return exp;
    }

    /// <summary>
    /// Determine faction for an NPC based on their class and alignment (for legacy save migration)
    /// Uses same logic as NPCSpawnSystem.DetermineFactionForNPC but works with NPC object
    /// </summary>
    private static UsurperRemake.Systems.Faction? DetermineFactionForNPC(NPC npc)
    {
        var random = new Random();

        // Clerics are strongly associated with The Faith
        if (npc.Class == CharacterClass.Cleric)
        {
            // Only evil clerics wouldn't be Faith
            if (npc.Darkness <= npc.Chivalry)
                return UsurperRemake.Systems.Faction.TheFaith;
        }

        // Assassins are associated with The Shadows
        if (npc.Class == CharacterClass.Assassin)
        {
            if (random.Next(100) < 80) // 80% chance
                return UsurperRemake.Systems.Faction.TheShadows;
        }

        // Warriors and Paladins may be Crown members
        if (npc.Class == CharacterClass.Warrior || npc.Class == CharacterClass.Paladin)
        {
            // Good-aligned warriors often serve the Crown
            if (npc.Chivalry > npc.Darkness && random.Next(100) < 60)
                return UsurperRemake.Systems.Faction.TheCrown;
        }

        // Evil-aligned characters may be Shadows
        if (npc.Darkness > npc.Chivalry + 200)
        {
            if (random.Next(100) < 50) // 50% chance
                return UsurperRemake.Systems.Faction.TheShadows;
        }

        // Most NPCs remain unaffiliated
        return null;
    }

    /// <summary>
    /// Save current game state
    /// </summary>
    public async Task SaveCurrentGame()
    {
        if (currentPlayer == null) return;
        
        var playerName = currentPlayer.Name2 ?? currentPlayer.Name1;
        terminal.WriteLine("Saving game...", "yellow");
        
        var success = await SaveSystem.Instance.SaveGame(playerName, currentPlayer);
        
        if (success)
        {
            terminal.WriteLine("Game saved successfully!", "green");
        }
        else
        {
            terminal.WriteLine("Failed to save game!", "red");
        }
        
        await Task.Delay(1000);
    }
    
    /// <summary>
    /// Create new player using comprehensive character creation system
    /// Based on Pascal USERHUNC.PAS implementation
    /// </summary>
    private async Task<Character> CreateNewPlayer(string playerName)
    {
        try
        {
            // Use the CharacterCreationSystem for full Pascal-compatible creation
            var creationSystem = new CharacterCreationSystem(terminal);
            var newCharacter = await creationSystem.CreateNewCharacter(playerName);
            
            if (newCharacter == null)
            {
                // Character creation was aborted
                terminal.WriteLine("");
                terminal.WriteLine("Character creation was cancelled.", "yellow");
                terminal.WriteLine("You must create a character to play Usurper.", "white");
                
                var retry = await terminal.GetInputAsync("Would you like to try again? (Y/N): ");
                if (retry.ToUpper() == "Y")
                {
                    return await CreateNewPlayer(playerName); // Retry
                }
                
                return null; // User chose not to retry
            }
            
            // Character creation successful - message already displayed by CharacterCreationSystem
            return newCharacter;
        }
        catch (OperationCanceledException)
        {
            terminal.WriteLine("Character creation aborted by user.", "red");
            return null;
        }
        catch (Exception ex)
        {
            terminal.WriteLine($"An error occurred during character creation: {ex.Message}", "red");
            GD.PrintErr($"Character creation error: {ex}");
            UsurperRemake.Systems.DebugLogger.Instance.LogError("CRASH", $"Character creation error:\n{ex}");

            terminal.WriteLine("Please try again.", "yellow");
            var retry = await terminal.GetInputAsync("Would you like to try again? (Y/N): ");
            if (retry.ToUpper() == "Y")
            {
                return await CreateNewPlayer(playerName); // Retry
            }
            
            return null;
        }
    }
    
    /// <summary>
    /// Get current location description for compatibility
    /// </summary>
    public string GetCurrentLocationDescription()
    {
        return locationManager?.GetCurrentLocationDescription() ?? "Unknown location";
    }
    
    /// <summary>
    /// Update status line with player info
    /// </summary>
    private void UpdateStatusLine()
    {
        var statusText = $"[{currentPlayer.DisplayName}] " +
                        $"Level: {currentPlayer.Level} " +
                        $"HP: {currentPlayer.HP}/{currentPlayer.MaxHP} " +
                        $"Gold: {currentPlayer.Gold} " +
                        $"Turns: {currentPlayer.TurnsLeft}";
        
        terminal.SetStatusLine(statusText);
    }
    
    /// <summary>
    /// Get NPCs in a specific location (for location system compatibility)
    /// </summary>
    public List<NPC> GetNPCsInLocation(GameLocation locationId)
    {
        return locationManager?.GetNPCsInLocation(locationId) ?? new List<NPC>();
    }
    
    /// <summary>
    /// Add NPC to a specific location
    /// </summary>
    public void AddNPCToLocation(GameLocation locationId, NPC npc)
    {
        locationManager?.AddNPCToLocation(locationId, npc);
    }
    
    /// <summary>
    /// Get current player for location system
    /// </summary>
    public Character GetCurrentPlayer()
    {
        return currentPlayer;
    }
    
    /// <summary>
    /// Check daily limits - based on CHECK_ALLOWED from Pascal
    /// </summary>
    private async Task<bool> CheckDailyLimits()
    {
        // Check if it's a new day
        if (dailyManager.IsNewDay())
        {
            await dailyManager.CheckDailyReset();
        }
        
        return currentPlayer.TurnsRemaining > 0;
    }
    
    /// <summary>
    /// Handle player death - based on death handling from Pascal
    /// Player respawns at the Inn with penalties instead of being deleted
    /// </summary>
    private async Task HandleDeath()
    {
        terminal.ClearScreen();
        terminal.ShowANSIArt("DEATH");
        terminal.SetColor("bright_red");
        terminal.WriteLine("═══════════════════════════════════════════════════════════════");
        terminal.WriteLine("                        YOU HAVE DIED!                          ");
        terminal.WriteLine("═══════════════════════════════════════════════════════════════");
        terminal.WriteLine("");

        terminal.SetColor("gray");
        terminal.WriteLine("Your vision fades to black as death claims you...");
        terminal.WriteLine("");
        await Task.Delay(2000);

        // Check if player has resurrections (from items/temple)
        if (currentPlayer.Resurrections > 0)
        {
            terminal.SetColor("yellow");
            terminal.WriteLine($"You have {currentPlayer.Resurrections} resurrection(s) available!");
            terminal.WriteLine("");
            var resurrect = await terminal.GetInput("Use a resurrection to avoid penalties? (Y/N): ");

            if (resurrect.ToUpper().StartsWith("Y"))
            {
                currentPlayer.Resurrections--;
                currentPlayer.Statistics.RecordResurrection();
                currentPlayer.HP = currentPlayer.MaxHP;
                terminal.SetColor("bright_green");
                terminal.WriteLine("");
                terminal.WriteLine("Divine light surrounds you!");
                terminal.WriteLine("You have been fully resurrected with no penalties!");
                await Task.Delay(2500);

                // Return to the Inn
                currentPlayer.Location = (int)GameLocation.TheInn;
                await SaveSystem.Instance.AutoSave(currentPlayer);
                return;
            }
        }

        // Apply death penalties
        terminal.SetColor("red");
        terminal.WriteLine("Death Penalties Applied:");
        terminal.WriteLine("─────────────────────────");

        // Calculate penalties
        long expLoss = currentPlayer.Experience / 10;  // Lose 10% experience
        long goldLoss = currentPlayer.Gold / 4;        // Lose 25% gold on hand

        // Apply penalties
        currentPlayer.Experience = Math.Max(0, currentPlayer.Experience - expLoss);
        currentPlayer.Gold = Math.Max(0, currentPlayer.Gold - goldLoss);

        // Track death count
        currentPlayer.MDefeats++;

        terminal.SetColor("yellow");
        if (expLoss > 0)
            terminal.WriteLine($"  - Lost {expLoss:N0} experience points");
        if (goldLoss > 0)
            terminal.WriteLine($"  - Lost {goldLoss:N0} gold (dropped upon death)");
        terminal.WriteLine($"  - Monster defeats: {currentPlayer.MDefeats}");
        terminal.WriteLine("");

        // Resurrect player at the Inn with half HP
        currentPlayer.HP = Math.Max(1, currentPlayer.MaxHP / 2);
        currentPlayer.Location = (int)GameLocation.TheInn;

        // Clear any negative status effects
        currentPlayer.Poison = 0;

        terminal.SetColor("cyan");
        terminal.WriteLine("You wake up at the Inn, nursed back to health by the innkeeper.");
        terminal.WriteLine($"Your wounds have partially healed. (HP: {currentPlayer.HP}/{currentPlayer.MaxHP})");
        terminal.WriteLine("");

        // Online news: player death
        if (UsurperRemake.Systems.OnlineStateManager.IsActive)
        {
            var deathName = currentPlayer.Name2 ?? currentPlayer.Name1;
            _ = UsurperRemake.Systems.OnlineStateManager.Instance!.AddNews(
                $"{deathName} has fallen in battle and was carried back to the Inn.", "combat");
        }

        terminal.SetColor("gray");
        terminal.WriteLine("\"You're lucky to be alive, friend. Rest up and try again.\"");
        terminal.WriteLine("");

        await terminal.PressAnyKey();

        // Save the resurrected character
        await SaveSystem.Instance.AutoSave(currentPlayer);

        // Continue playing - don't mark as deleted!
        terminal.SetColor("green");
        terminal.WriteLine("Your adventure continues...");
        await Task.Delay(1500);
    }
    
    /// <summary>
    /// Handle prison - based on prison handling from Pascal
    /// </summary>
    private async Task HandlePrison()
    {
        terminal.ClearScreen();
        terminal.SetColor("gray");
        terminal.WriteLine("You are in PRISON!");
        terminal.WriteLine("═══════════════════");
        terminal.WriteLine("");
        terminal.WriteLine($"Days remaining: {currentPlayer.DaysInPrison}");
        terminal.WriteLine("");
        terminal.WriteLine("1. Wait it out");
        terminal.WriteLine("2. Attempt escape");
        terminal.WriteLine("3. Quit");
        
        var choice = await terminal.GetMenuChoice();
        
        switch (choice)
        {
            case 0: // Wait
                currentPlayer.DaysInPrison--;
                terminal.WriteLine("You wait patiently...", "gray");
                await Task.Delay(2000);
                break;
                
            case 1: // Escape
                if (currentPlayer.PrisonEscapes > 0)
                {
                    await AttemptPrisonEscape();
                }
                else
                {
                    terminal.WriteLine("You have no escape attempts left!", "red");
                    await Task.Delay(2000);
                }
                break;
                
            case 2: // Quit
                await QuitGame();
                break;
        }
    }
    
    /// <summary>
    /// Attempt prison escape
    /// </summary>
    private async Task AttemptPrisonEscape()
    {
        currentPlayer.PrisonEscapes--;
        
        terminal.WriteLine("You attempt to escape...", "yellow");
        await Task.Delay(1000);
        
        // Escape chance based on stats
        var escapeChance = (currentPlayer.Dexterity + currentPlayer.Agility) / 4;
        var roll = GD.RandRange(1, 100);
        
        if (roll <= escapeChance)
        {
            terminal.WriteLine("You successfully escape!", "green");
            currentPlayer.DaysInPrison = 0;
            currentPlayer.Location = 1; // Return to main street
        }
        else
        {
            terminal.WriteLine("You are caught trying to escape!", "red");
            currentPlayer.DaysInPrison += 2; // Extra penalty
        }
        
        await Task.Delay(2000);
    }
    
    /// <summary>
    /// Quit game and save
    /// </summary>
    private async Task QuitGame()
    {
        terminal.WriteLine("Saving game...", "yellow");

        // Track session end telemetry
        if (currentPlayer != null)
        {
            int playtimeMinutes = (int)currentPlayer.Statistics.TotalPlayTime.TotalMinutes;
            TelemetrySystem.Instance.TrackSessionEnd(
                currentPlayer.Level,
                playtimeMinutes,
                (int)currentPlayer.MDefeats,
                (int)currentPlayer.MKills
            );

            // Log game exit
            DebugLogger.Instance.LogGameExit(currentPlayer.Name, "QuitGame");
        }

        // Ensure save completes before exiting
        if (currentPlayer != null)
        {
            try
            {
                string playerName = currentPlayer.Name2 ?? currentPlayer.Name1;
                var success = await SaveSystem.Instance.SaveGame(playerName, currentPlayer);
                if (success)
                {
                    terminal.WriteLine("Game saved successfully!", "bright_green");
                }
                else
                {
                    terminal.WriteLine("Warning: Save may not have completed.", "yellow");
                }
            }
            catch (Exception ex)
            {
                terminal.WriteLine($"Save error: {ex.Message}", "red");
            }
        }

        // Stop background simulation threads
        worldSimulator?.StopSimulation();

        terminal.WriteLine("Goodbye!", "green");
        await Task.Delay(1000);

        // Mark as intentional exit so bootstrap doesn't show warning
        IsIntentionalExit = true;

        // GetTree().Quit(); // Godot API not available, use alternative
        Environment.Exit(0);
    }
    
    // Helper methods
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
        return weather[GD.RandRange(0, weather.Length - 1)];
    }
    
    /// <summary>
    /// Navigate to a specific location using the location manager
    /// </summary>
    public async Task<bool> NavigateToLocation(GameLocation destination)
    {
        return await locationManager.NavigateTo(destination, currentPlayer);
    }
    
    // Placeholder methods for game actions
    private async Task ShowInstructions() => await ShowStoryIntroduction();
    private async Task ListPlayers() => await ShowInfoScreen("Player List", "Player list will be here...");
    private async Task ShowTeams() => await ShowInfoScreen("Teams", "Team information will be here...");
    private async Task ShowGameSettings() => await ShowInfoScreen("Game Settings", "Game settings will be here...");
    private async Task ShowStatus() => await ShowInfoScreen("Status", $"Player: {currentPlayer?.DisplayName}\nLevel: {currentPlayer?.Level}\nHP: {currentPlayer?.HP}/{currentPlayer?.MaxHP}");

    /// <summary>
    /// Display the credits screen
    /// </summary>
    private async Task ShowBBSList()
    {
        terminal.ClearScreen();

        terminal.SetColor("bright_cyan");
        terminal.WriteLine("+=============================================================================+");
        terminal.SetColor("bright_white");
        terminal.WriteLine("|                 BBS LIST - Play Usurper Reborn Online!                      |");
        terminal.SetColor("bright_cyan");
        terminal.WriteLine("+=============================================================================+");
        terminal.WriteLine("");

        terminal.SetColor("white");
        terminal.WriteLine("  The following BBSes are running Usurper Reborn as a door game.");
        terminal.WriteLine("  Connect via telnet or SSH to play online!");
        terminal.WriteLine("");

        // Column headers
        terminal.SetColor("bright_yellow");
        terminal.WriteLine("  BBS Name                       Software    Address");
        terminal.SetColor("darkgray");
        terminal.WriteLine("  ---------------------------------------------------------------------------");

        // --- BBS Entries ---

        terminal.SetColor("bright_white");
        terminal.Write("  Shurato's Heavenly Sphere      ");
        terminal.SetColor("gray");
        terminal.Write("EleBBS      ");
        terminal.SetColor("bright_green");
        terminal.WriteLine("shsbbs.net");

        // --- End BBS Entries ---

        terminal.WriteLine("");
        terminal.SetColor("darkgray");
        terminal.WriteLine("  ---------------------------------------------------------------------------");
        terminal.WriteLine("");

        terminal.SetColor("bright_magenta");
        terminal.WriteLine("  Are you a SysOp running Usurper Reborn on your BBS?");
        terminal.SetColor("gray");
        terminal.WriteLine("  Get listed here! Open an issue or discussion at:");
        terminal.WriteLine("");
        terminal.SetColor("bright_cyan");
        terminal.WriteLine("    https://github.com/binary-knight/usurper-reborn");
        terminal.WriteLine("");
        terminal.SetColor("gray");
        terminal.WriteLine("  Include your BBS name, address, and software. We'd love to add you!");
        terminal.WriteLine("");

        terminal.SetColor("yellow");
        terminal.WriteLine("                         [Press Enter to return]");
        await terminal.WaitForKey();
    }

    private async Task ShowCredits()
    {
        terminal.ClearScreen();

        terminal.SetColor("bright_yellow");
        terminal.WriteLine("+=============================================================================+");
        terminal.WriteLine("|                              CREDITS                                        |");
        terminal.WriteLine("+=============================================================================+");
        terminal.WriteLine("");

        terminal.SetColor("bright_cyan");
        terminal.WriteLine("                         USURPER REBORN");
        terminal.WriteLine("                    A Modern Tribute to a Classic");
        terminal.WriteLine("");

        terminal.SetColor("bright_white");
        terminal.WriteLine("  Original Game:");
        terminal.SetColor("white");
        terminal.WriteLine("    Usurper: Halls of Avarice (1993)");
        terminal.WriteLine("");

        terminal.SetColor("bright_yellow");
        terminal.WriteLine("  ORIGINAL CREATORS:");
        terminal.SetColor("cyan");
        terminal.WriteLine("    Jakob Dangarden          - Original Game Creator (1993)");
        terminal.WriteLine("    Rick Parrish             - Source Code Preservation & Porting");
        terminal.WriteLine("    Daniel Zingaro           - Bug Fixing & Code Quality");
        terminal.WriteLine("");

        terminal.SetColor("bright_green");
        terminal.WriteLine("  THIS REMAKE:");
        terminal.SetColor("cyan");
        terminal.WriteLine("    Jason Knight             - Creator of Usurper Reborn");
        terminal.WriteLine("");
        terminal.SetColor("gray");
        terminal.WriteLine("    Built with C# / .NET 8.0");
        terminal.WriteLine("");

        terminal.SetColor("bright_magenta");
        terminal.WriteLine("  SPECIAL THANKS:");
        terminal.SetColor("gray");
        terminal.WriteLine("    The BBS community of the 1980s and 1990s");
        terminal.WriteLine("    All the SysOps who kept the boards running");
        terminal.WriteLine("    Every player who dialed in to explore Durunghins");
        terminal.WriteLine("    The Break Into Chat wiki for preserving BBS history");
        terminal.WriteLine("");

        terminal.SetColor("bright_white");
        terminal.WriteLine("  LICENSE:");
        terminal.SetColor("gray");
        terminal.WriteLine("    The original Usurper was released under the GNU GPL");
        terminal.WriteLine("    This remake honors that open source tradition");
        terminal.WriteLine("");

        terminal.SetColor("bright_cyan");
        terminal.WriteLine("  COMMUNITY:");
        terminal.SetColor("gray");
        terminal.WriteLine("    Join us on Discord: https://discord.gg/EZhwgDT6Ta");
        terminal.WriteLine("");

        terminal.SetColor("yellow");
        terminal.WriteLine("");
        terminal.WriteLine("                         [Press Enter to return]");
        await terminal.WaitForKey();
    }

    /// <summary>
    /// Display the story introduction and lore
    /// </summary>
    private async Task ShowStoryIntroduction()
    {
        terminal.ClearScreen();

        // Page 1: The Golden Age
        terminal.SetColor("bright_yellow");
        terminal.WriteLine("╔══════════════════════════════════════════════════════════════════════════════╗");
        terminal.WriteLine("║                         THE STORY SO FAR...                                  ║");
        terminal.WriteLine("╚══════════════════════════════════════════════════════════════════════════════╝");
        terminal.WriteLine("");

        terminal.SetColor("bright_cyan");
        terminal.WriteLine("                           ~ The Golden Age ~");
        terminal.WriteLine("");
        terminal.SetColor("white");
        terminal.WriteLine("  Long ago, in an age now lost to memory, the world was watched over by the");
        terminal.WriteLine("  Seven Divine - gods of immense power who guided mortalkind with wisdom and");
        terminal.WriteLine("  grace. Under their benevolent gaze, civilizations flourished. The god of");
        terminal.WriteLine("  war taught honor in battle. The goddess of love blessed every union. The");
        terminal.WriteLine("  god of light ensured truth prevailed over deception.");
        terminal.WriteLine("");
        terminal.SetColor("gray");
        terminal.WriteLine("  It was an age of miracles. An age of heroes. An age of hope.");
        terminal.WriteLine("");

        terminal.SetColor("yellow");
        terminal.WriteLine("                              [Press Enter]");
        await terminal.WaitForKey();

        // Page 2: The Sundering
        terminal.ClearScreen();
        terminal.SetColor("bright_yellow");
        terminal.WriteLine("╔══════════════════════════════════════════════════════════════════════════════╗");
        terminal.WriteLine("║                         THE STORY SO FAR...                                  ║");
        terminal.WriteLine("╚══════════════════════════════════════════════════════════════════════════════╝");
        terminal.WriteLine("");

        terminal.SetColor("bright_red");
        terminal.WriteLine("                            ~ The Sundering ~");
        terminal.WriteLine("");
        terminal.SetColor("white");
        terminal.WriteLine("  Then came the Sundering - a cataclysm whose cause remains unknown. Some say");
        terminal.WriteLine("  mortals grew too proud and turned away from the gods. Others whisper of a");
        terminal.WriteLine("  betrayal among the Divine themselves. Whatever the truth, the result was");
        terminal.WriteLine("  catastrophic.");
        terminal.WriteLine("");
        terminal.SetColor("red");
        terminal.WriteLine("  The Seven were corrupted. Twisted. Imprisoned in realms of their own making.");
        terminal.WriteLine("");
        terminal.SetColor("white");
        terminal.WriteLine("  The god of war became rage incarnate, drowning in endless bloodshed. The");
        terminal.WriteLine("  goddess of love withered into jealousy and obsession. The god of light");
        terminal.WriteLine("  faded to barely a whisper, truth dying with each passing lie.");
        terminal.WriteLine("");

        terminal.SetColor("yellow");
        terminal.WriteLine("                              [Press Enter]");
        await terminal.WaitForKey();

        // Page 3: The Age of Avarice
        terminal.ClearScreen();
        terminal.SetColor("bright_yellow");
        terminal.WriteLine("╔══════════════════════════════════════════════════════════════════════════════╗");
        terminal.WriteLine("║                         THE STORY SO FAR...                                  ║");
        terminal.WriteLine("╚══════════════════════════════════════════════════════════════════════════════╝");
        terminal.WriteLine("");

        terminal.SetColor("bright_magenta");
        terminal.WriteLine("                          ~ The Age of Avarice ~");
        terminal.WriteLine("");
        terminal.SetColor("white");
        terminal.WriteLine("  Centuries have passed. The Old Gods are now mere legends - cautionary tales");
        terminal.WriteLine("  told to frighten children. In their absence, mortals have created new gods");
        terminal.WriteLine("  to worship, pale echoes of the Divine that once were.");
        terminal.WriteLine("");
        terminal.WriteLine("  And in this godless age, a new power has risen: AVARICE.");
        terminal.WriteLine("");
        terminal.SetColor("bright_yellow");
        terminal.WriteLine("  The Halls of Avarice - a sprawling underground complex where adventurers");
        terminal.WriteLine("  seek fortune, glory, and power. Where the strong prey upon the weak. Where");
        terminal.WriteLine("  gold is the only god that matters.");
        terminal.WriteLine("");
        terminal.SetColor("gray");
        terminal.WriteLine("  Some come seeking treasure. Some come seeking fame. Some come to escape");
        terminal.WriteLine("  their past. And some... some hear whispers in the dark. Ancient voices");
        terminal.WriteLine("  calling from the depths. Promising power. Demanding sacrifice.");
        terminal.WriteLine("");

        terminal.SetColor("yellow");
        terminal.WriteLine("                              [Press Enter]");
        await terminal.WaitForKey();

        // Page 4: Your Story Begins
        terminal.ClearScreen();
        terminal.SetColor("bright_yellow");
        terminal.WriteLine("╔══════════════════════════════════════════════════════════════════════════════╗");
        terminal.WriteLine("║                         THE STORY SO FAR...                                  ║");
        terminal.WriteLine("╚══════════════════════════════════════════════════════════════════════════════╝");
        terminal.WriteLine("");

        terminal.SetColor("bright_green");
        terminal.WriteLine("                          ~ Your Story Begins ~");
        terminal.WriteLine("");
        terminal.SetColor("white");
        terminal.WriteLine("  You arrive at the gates of the realm with little more than the clothes on");
        terminal.WriteLine("  your back and a hunger for something more. The bustling Main Street awaits,");
        terminal.WriteLine("  filled with shops, taverns, and opportunities for those bold enough to");
        terminal.WriteLine("  seize them.");
        terminal.WriteLine("");
        terminal.SetColor("cyan");
        terminal.WriteLine("  Will you delve into the deadly Dungeons in search of treasure and glory?");
        terminal.WriteLine("  Will you find love on the cobblestones of this dangerous town?");
        terminal.WriteLine("  Will you rise to become a champion... or fall to become a cautionary tale?");
        terminal.WriteLine("");
        terminal.SetColor("bright_magenta");
        terminal.WriteLine("  And perhaps, if you grow strong enough, you may discover what truly lurks");
        terminal.WriteLine("  in the deepest halls. You may learn the fate of the Old Gods. You may even");
        terminal.WriteLine("  have the chance to save them... or destroy them forever.");
        terminal.WriteLine("");
        terminal.SetColor("bright_white");
        terminal.WriteLine("  The choice, adventurer, is yours.");
        terminal.WriteLine("");

        terminal.SetColor("yellow");
        terminal.WriteLine("                         [Press Enter to return]");
        await terminal.WaitForKey();
    }
    
    private async Task ShowInfoScreen(string title, string content)
    {
        terminal.ClearScreen();
        terminal.SetColor("bright_cyan");
        terminal.WriteLine(title);
        terminal.SetColor("cyan");
        terminal.WriteLine(new string('═', title.Length));
        terminal.WriteLine("");
        terminal.SetColor("white");
        terminal.WriteLine(content);
        terminal.WriteLine("");
        terminal.WriteLine("Press Enter to continue...");
        await terminal.WaitForKey();
    }

    // Placeholder initialization methods
    private void ReadStartCfgValues() { /* Load config from file */ }
    private void InitializeItems()
    {
        // Initialize items using ItemManager
        ItemManager.InitializeItems();
    }
    private void InitializeMonsters() { /* Load monsters from data */ }
    private void InitializeNPCs()
    {
        try
        {
            if (worldNPCs == null)
                worldNPCs = new List<NPC>();

            var dataPath = Path.Combine(DataPath, "npcs.json");
            if (!File.Exists(dataPath))
            {
                GD.PrintErr($"[Init] NPC data file not found at {dataPath}. Using hard-coded specials only.");
                return;
            }

            var json = File.ReadAllText(dataPath);
            using var doc = JsonDocument.Parse(json);

            // Flatten all category arrays (tavern_npcs, guard_npcs, random_npcs, etc.)
            var root = doc.RootElement;
            foreach (var prop in root.EnumerateObject())
            {
                if (prop.Value.ValueKind != JsonValueKind.Array) continue;

                foreach (var npcElem in prop.Value.EnumerateArray())
                {
                    try
                    {
                        var name = npcElem.GetProperty("name").GetString() ?? "Unknown";
                        var archetype = npcElem.GetProperty("archetype").GetString() ?? "citizen";
                        var classStr = npcElem.GetProperty("class").GetString() ?? "warrior";
                        var level = npcElem.GetProperty("level").GetInt32();

                        if (!Enum.TryParse<CharacterClass>(classStr, true, out var charClass))
                        {
                            charClass = CharacterClass.Warrior;
                        }

                        var npc = new NPC(name, archetype, charClass, level);

                        // Gold override if provided
                        if (npcElem.TryGetProperty("gold", out var goldProp) && goldProp.TryGetInt64(out long gold))
                        {
                            npc.Gold = gold;
                        }

                        // Starting location mapping
                        string startLoc = npcElem.GetProperty("startingLocation").GetString() ?? "main_street";
                        var locId = MapStringToLocation(startLoc);
                        npc.UpdateLocation(startLoc); // keep textual for AI compatibility

                        worldNPCs.Add(npc);

                        // Add to LocationManager so they show up to the player
                        LocationManager.Instance.AddNPCToLocation(locId, npc);
                    }
                    catch (Exception exNpc)
                    {
                        GD.PrintErr($"[Init] Failed to load NPC: {exNpc.Message}");
                    }
                }
            }

            GD.Print($"[Init] Loaded {worldNPCs.Count} NPC definitions from data file.");
        }
        catch (Exception ex)
        {
            GD.PrintErr($"[Init] Error loading NPCs: {ex.Message}");
        }
    }
    
    /// <summary>
    /// Map simple string location names from JSON to GameLocation enum.
    /// </summary>
    private static GameLocation MapStringToLocation(string loc)
    {
        return loc.ToLower() switch
        {
            "tavern" or "inn" => GameLocation.TheInn,
            "market" or "marketplace" => GameLocation.Marketplace,
            "town_square" or "main_street" => GameLocation.MainStreet,
            "castle" => GameLocation.Castle,
            "temple" or "church" => GameLocation.Temple,
            "dungeon" or "dungeons" => GameLocation.Dungeons,
            "bank" => GameLocation.Bank,
            "dark_alley" or "alley" => GameLocation.DarkAlley,
            _ => GameLocation.MainStreet
        };
    }
    
    private void InitializeLevels() { /* Load level data */ }
    private void InitializeGuards() { /* Load guard data */ }
    
    // Character creation helpers (now handled by CharacterCreationSystem)
    // These methods are kept for backwards compatibility but are no longer used
    private async Task<CharacterSex> SelectSex() => CharacterSex.Male; // Legacy - use CharacterCreationSystem
    private async Task<CharacterRace> SelectRace() => CharacterRace.Human; // Legacy - use CharacterCreationSystem
    private async Task<CharacterClass> SelectClass() => CharacterClass.Warrior; // Legacy - use CharacterCreationSystem
    private void ApplyRacialBonuses(Character character) { /* Legacy - handled by CharacterCreationSystem */ }
    private void ApplyClassBonuses(Character character) { /* Legacy - handled by CharacterCreationSystem */ }
    private void SetInitialEquipment(Character character) { /* Legacy - handled by CharacterCreationSystem */ }
    private async Task ShowCharacterSummary(Character character) { /* Legacy - handled by CharacterCreationSystem */ }

    /// <summary>
    /// Run magic shop system validation tests
    /// </summary>
    public static void TestMagicShopSystem()
    {
        try
        {
            GD.Print("═══ Running Magic Shop System Tests ═══");
            // MagicShopSystemValidation(); // TODO: Implement this validation method
            GD.Print("═══ Magic Shop Tests Complete ═══");
        }
        catch (System.Exception ex)
        {
            GD.PrintErr($"Magic Shop Test Error: {ex.Message}");
        }
    }

    /// <summary>
    /// Prompt player to opt-in to anonymous telemetry for alpha testing
    /// </summary>
    private async Task PromptTelemetryOptIn()
    {
        terminal.Clear();
        terminal.SetColor("cyan");
        terminal.WriteLine("");
        terminal.WriteLine("╔══════════════════════════════════════════════════════════════╗");
        terminal.WriteLine("║              HELP IMPROVE USURPER REBORN                     ║");
        terminal.WriteLine("╚══════════════════════════════════════════════════════════════╝");
        terminal.WriteLine("");
        terminal.SetColor("white");
        terminal.WriteLine("During this alpha testing phase, we can collect anonymous");
        terminal.WriteLine("gameplay statistics to help improve game balance and identify bugs.");
        terminal.WriteLine("");
        terminal.SetColor("gray");
        terminal.WriteLine("What we collect:");
        terminal.WriteLine("  - Combat statistics (victories, defeats, damage dealt)");
        terminal.WriteLine("  - Player milestones (level ups, boss defeats)");
        terminal.WriteLine("  - Feature usage (which areas you visit most)");
        terminal.WriteLine("  - Errors and crashes");
        terminal.WriteLine("");
        terminal.SetColor("bright_green");
        terminal.WriteLine("What we DON'T collect:");
        terminal.WriteLine("  - Your real name or identifying information");
        terminal.WriteLine("  - Your IP address");
        terminal.WriteLine("  - Chat messages or custom text");
        terminal.WriteLine("");
        terminal.SetColor("yellow");
        terminal.WriteLine("You can disable this at any time from the Settings menu.");
        terminal.WriteLine("");
        terminal.SetColor("white");
        terminal.Write("Would you like to help us improve the game? [Y/N]: ");

        var response = await terminal.GetInput("");
        if (response.Trim().ToUpper() == "Y" || response.Trim().ToUpper() == "YES")
        {
            TelemetrySystem.Instance.Enable();
            terminal.SetColor("bright_green");
            terminal.WriteLine("");
            terminal.WriteLine("Thank you! Your feedback will help make Usurper Reborn better.");
            terminal.WriteLine("");

            // Track session start first
            TelemetrySystem.Instance.TrackSessionStart(
                GameConfig.Version,
                System.Environment.OSVersion.Platform.ToString()
            );

            // Track new character creation with details - this sends immediately
            TelemetrySystem.Instance.TrackNewCharacter(
                currentPlayer.Race.ToString(),
                currentPlayer.Class.ToString(),
                currentPlayer.Sex.ToString(),
                DifficultySystem.CurrentDifficulty.ToString(),
                (int)currentPlayer.Gold
            );

            // Identify user for PostHog dashboards (DAUs, WAUs, Retention)
            TelemetrySystem.Instance.Identify(
                characterName: currentPlayer.Name,
                characterClass: currentPlayer.Class.ToString(),
                race: currentPlayer.Race.ToString(),
                level: currentPlayer.Level,
                difficulty: DifficultySystem.CurrentDifficulty.ToString(),
                firstSeen: DateTime.UtcNow
            );
        }
        else
        {
            TelemetrySystem.Instance.Disable();
            terminal.SetColor("gray");
            terminal.WriteLine("");
            terminal.WriteLine("No problem! You can enable telemetry later in Settings if you change your mind.");
            terminal.WriteLine("");
        }

        await Task.Delay(1500);
    }
}

/// <summary>
/// Menu option for terminal menus
/// </summary>
public class MenuOption
{
    public string Key { get; set; } = "";
    public string Text { get; set; } = "";
    public Func<Task> Action { get; set; } = async () => { };
}

/// <summary>
/// Game state tracking
/// </summary>
public class GameState
{
    public int GameDate { get; set; }
    public int LastDayRun { get; set; }
    public int PlayersOnline { get; set; }
    public bool MaintenanceRunning { get; set; }
}

/// <summary>
/// Online player tracking
/// </summary>
public class OnlinePlayer
{
    public string Name { get; set; } = "";
    public string Node { get; set; } = "";
    public DateTime Arrived { get; set; }
    public string Location { get; set; } = "";
    public bool IsNPC { get; set; }
}

/// <summary>
/// Config record based on Pascal ConfigRecord
/// </summary>
public class ConfigRecord
{
    public bool MarkNPCs { get; set; } = true;
    public int LevelDiff { get; set; } = 5;
    public bool FastPlay { get; set; } = false;
    public string Anchor { get; set; } = "Anchor road";
    public bool SimulNode { get; set; } = false;
    public bool AutoMaint { get; set; } = true;
    // Add more config fields as needed
}

/// <summary>
/// King record based on Pascal KingRec
/// </summary>
public class KingRecord
{
    public string Name { get; set; } = "";
    public CharacterAI AI { get; set; } = CharacterAI.Computer;
    public CharacterSex Sex { get; set; } = CharacterSex.Male;
    public long DaysInPower { get; set; } = 0;
    public byte Tax { get; set; } = 10;
    public long Treasury { get; set; } = 50000;
    // Add more king fields as needed
} 
