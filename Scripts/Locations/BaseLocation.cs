using UsurperRemake.Utils;
using UsurperRemake.Systems;
using UsurperRemake.Data;
using Godot;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;

/// <summary>
/// Base location class for all game locations
/// Based on Pascal location system from ONLINE.PAS
/// </summary>
public abstract class BaseLocation
{
    public GameLocation LocationId { get; protected set; }
    public string Name { get; protected set; } = "";
    public string Description { get; protected set; } = "";
    public List<GameLocation> PossibleExits { get; protected set; } = new();
    public List<NPC> LocationNPCs { get; protected set; } = new();
    public List<string> LocationActions { get; protected set; } = new();
    
    // Pascal compatibility
    public bool RefreshRequired { get; set; } = true;
    
    protected TerminalEmulator terminal;
    protected Character currentPlayer;
    
    public BaseLocation(GameLocation locationId, string name, string description)
    {
        LocationId = locationId;
        Name = name;
        Description = description;
        SetupLocation();
    }
    
    /// <summary>
    /// Setup location-specific data (exits, NPCs, actions)
    /// </summary>
    protected virtual void SetupLocation()
    {
        // Override in derived classes
    }
    
    /// <summary>
    /// Enter the location - main entry point
    /// </summary>
    public virtual async Task EnterLocation(Character player, TerminalEmulator term)
    {
        currentPlayer = player;
        terminal = term;

        // Log location entry
        UsurperRemake.Systems.DebugLogger.Instance.LogInfo("LOCATION", $"Entered {Name} (ID: {LocationId})");

        // Update player location
        player.Location = (int)LocationId;

        // Update online presence with current location
        if (UsurperRemake.Systems.OnlineStateManager.IsActive)
            UsurperRemake.Systems.OnlineStateManager.Instance!.UpdateLocation(Name);

        // Track location visit statistics
        player.Statistics?.RecordLocationVisit(Name);

        // Check for achievements on location entry (catches non-combat achievements)
        AchievementSystem.CheckAchievements(player);
        await AchievementSystem.ShowPendingNotifications(term);

        // Show any pending game notifications (team events, etc.)
        await ShowPendingGameNotifications(term);

        // Ensure NPCs are initialized (safety check)
        if (NPCSpawnSystem.Instance.ActiveNPCs.Count == 0)
        {
            GD.Print("[BaseLocation] NPCs not initialized, initializing now...");
            await NPCSpawnSystem.Instance.InitializeClassicNPCs();
        }

        // Check for guard defense alert - player may be a royal guard who needs to defend!
        await CheckGuardDefenseAlert();

        // Main location loop
        await LocationLoop();
    }

    /// <summary>
    /// Show any pending game notifications (team events, important world events, etc.)
    /// </summary>
    private async Task ShowPendingGameNotifications(TerminalEmulator term)
    {
        if (GameEngine.PendingNotifications.Count == 0) return;

        var notifications = new List<string>();
        while (GameEngine.PendingNotifications.Count > 0)
        {
            notifications.Add(GameEngine.PendingNotifications.Dequeue());
        }

        term.WriteLine("");
        term.SetColor("bright_yellow");
        term.WriteLine("╔════════════════════════════════════════════════════════════════════════════╗");
        term.WriteLine("║                              IMPORTANT NEWS                                ║");
        term.WriteLine("╠════════════════════════════════════════════════════════════════════════════╣");

        foreach (var notification in notifications)
        {
            term.SetColor("white");
            term.WriteLine($"║  {notification,-74}║");
        }

        term.SetColor("bright_yellow");
        term.WriteLine("╚════════════════════════════════════════════════════════════════════════════╝");
        term.WriteLine("");

        await term.PressAnyKey();
    }

    /// <summary>
    /// Check if player is a royal guard and the throne is under attack
    /// </summary>
    protected virtual async Task CheckGuardDefenseAlert()
    {
        try
        {
            var king = CastleLocation.GetCurrentKing();
            if (king?.ActiveDefenseEvent == null) return;
            if (king.ActiveDefenseEvent.PlayerNotified) return;

            // Check if current player is a royal guard
            var playerGuard = king.Guards.FirstOrDefault(g =>
                g.Name.Equals(currentPlayer.DisplayName, StringComparison.OrdinalIgnoreCase) ||
                g.Name.Equals(currentPlayer.Name2, StringComparison.OrdinalIgnoreCase));

            if (playerGuard == null) return;

            // Player is a guard - notify them!
            king.ActiveDefenseEvent.PlayerNotified = true;

            terminal.ClearScreen();
            terminal.SetColor("bright_red");
            terminal.WriteLine("");
            terminal.WriteLine("╔══════════════════════════════════════════════════════════════════════════════╗");
            terminal.WriteLine("║                    *** URGENT: CASTLE UNDER ATTACK! ***                     ║");
            terminal.WriteLine("╚══════════════════════════════════════════════════════════════════════════════╝");
            terminal.WriteLine("");

            terminal.SetColor("yellow");
            terminal.WriteLine($"A messenger rushes to find you with dire news!");
            terminal.WriteLine("");
            terminal.SetColor("white");
            terminal.WriteLine($"{king.ActiveDefenseEvent.ChallengerName} (Level {king.ActiveDefenseEvent.ChallengerLevel})");
            terminal.WriteLine($"is challenging {king.GetTitle()} {king.Name} for the throne!");
            terminal.WriteLine("");

            terminal.SetColor("cyan");
            terminal.WriteLine("As a Royal Guard, you are honor-bound to defend the crown!");
            terminal.WriteLine("Will you rush to the castle to aid in the defense?");
            terminal.WriteLine("");

            terminal.SetColor("yellow");
            terminal.Write("Rush to defend the throne? (Y/N): ");
            terminal.SetColor("white");

            string response = await terminal.ReadLineAsync();

            if (response?.ToUpper() == "Y")
            {
                king.ActiveDefenseEvent.PlayerResponded = true;
                terminal.SetColor("bright_green");
                terminal.WriteLine("");
                terminal.WriteLine("You rush toward the castle, sword drawn!");
                terminal.WriteLine("Your loyalty to the crown shall not be questioned!");
                await terminal.PressAnyKey();

                // Transport player to castle for defense
                // The player will participate in the defense when they arrive
                await GameEngine.Instance.NavigateToLocation(GameLocation.Castle);
            }
            else
            {
                // Player refused - severe loyalty penalty
                playerGuard.Loyalty = Math.Max(0, playerGuard.Loyalty - 25);

                terminal.SetColor("red");
                terminal.WriteLine("");
                terminal.WriteLine("You turn away from your duty...");
                terminal.WriteLine("The crown will remember this betrayal.");
                terminal.WriteLine($"Your loyalty has dropped to {playerGuard.Loyalty}%!");

                if (playerGuard.Loyalty <= 20)
                {
                    terminal.SetColor("bright_red");
                    terminal.WriteLine("");
                    terminal.WriteLine("*** You have been STRIPPED of your Royal Guard status! ***");
                    king.Guards.Remove(playerGuard);
                    NewsSystem.Instance?.Newsy(true, $"Royal Guard {playerGuard.Name} was dismissed for cowardice!");
                }

                await terminal.PressAnyKey();
            }
        }
        catch
        {
            // King system not available - ignore
        }
    }
    
    /// <summary>
    /// Main location loop - handles display and user input
    /// </summary>
    protected virtual async Task LocationLoop()
    {
        bool exitLocation = false;

        // Check for random encounter when first entering location
        if (ShouldCheckForEncounters())
        {
            var encounterResult = await StreetEncounterSystem.Instance.CheckForEncounter(
                currentPlayer, LocationId, terminal);

            if (encounterResult.EncounterOccurred)
            {
                // If player died in encounter, exit
                if (!currentPlayer.IsAlive)
                    return;
            }
        }

        // Check for narrative encounters (Stranger, Town NPCs)
        await CheckNarrativeEncounters();

        while (!exitLocation && currentPlayer.IsAlive) // No turn limit - continuous gameplay
        {
            // Autosave BEFORE displaying location (save stable state)
            // This ensures we don't save during quit/exit actions
            if (currentPlayer != null)
            {
                await SaveSystem.Instance.AutoSave(currentPlayer);
            }

            // Display location
            DisplayLocation();

            // Show any pending online chat messages
            if (OnlineChatSystem.IsActive)
            {
                OnlineChatSystem.Instance!.DisplayPendingMessages(terminal);
            }

            // Get user choice
            var choice = await GetUserChoice();

            // Process choice
            exitLocation = await ProcessChoice(choice);

            // Increment turn count (drives world simulation)
            if (currentPlayer != null && !string.IsNullOrWhiteSpace(choice))
            {
                currentPlayer.TurnCount++;

                // Apply poison damage each turn
                await ApplyPoisonDamage();

                // Run world simulation every 5 turns
                if (currentPlayer.TurnCount % 5 == 0)
                {
                    await RunWorldSimulationTick();
                }
            }
        }
    }

    /// <summary>
    /// Check if this location should have random encounters
    /// </summary>
    protected virtual bool ShouldCheckForEncounters()
    {
        // Most locations have encounters; override in safe locations
        return LocationId switch
        {
            GameLocation.Home => false,           // Safe zone
            GameLocation.Bank => false,           // Guards present, very safe
            GameLocation.Church => false,         // Sacred ground
            GameLocation.Temple => false,         // Sacred ground
            GameLocation.Dungeons => false,       // Has own encounter system
            GameLocation.Prison => false,         // Special handling
            GameLocation.Master => false,         // Level master's sanctum
            _ => true                             // Other locations have encounters
        };
    }

    /// <summary>
    /// Check for narrative encounters (Stranger and Town NPC stories)
    /// </summary>
    protected virtual async Task CheckNarrativeEncounters()
    {
        if (currentPlayer == null || terminal == null) return;

        var locationName = LocationId.ToString();

        // Track player actions for Stranger encounter system
        StrangerEncounterSystem.Instance.OnPlayerAction(locationName, currentPlayer);

        // Check for Stranger (Noctura) encounter
        if (StrangerEncounterSystem.Instance.ShouldTriggerEncounter(locationName, currentPlayer))
        {
            var encounter = StrangerEncounterSystem.Instance.GetEncounter(locationName, currentPlayer);
            if (encounter != null)
            {
                await DisplayStrangerEncounter(encounter);
            }
        }

        // Check for memorable NPC encounters (Town NPC stories)
        var npcEncounter = TownNPCStorySystem.Instance.GetAvailableNPCEncounter(locationName, currentPlayer);
        if (npcEncounter != null)
        {
            var npcKey = TownNPCStorySystem.MemorableNPCs.FirstOrDefault(kvp => kvp.Value == npcEncounter).Key;
            var stage = TownNPCStorySystem.Instance.GetNextStage(npcKey, currentPlayer);
            if (stage != null)
            {
                await DisplayTownNPCEncounter(npcEncounter, stage, npcKey);
            }
        }
    }

    /// <summary>
    /// Display a Stranger (Noctura) encounter
    /// </summary>
    private async Task DisplayStrangerEncounter(StrangerEncounter encounter)
    {
        terminal.ClearScreen();
        terminal.SetColor("dark_magenta");
        terminal.WriteLine("");
        terminal.WriteLine("╔══════════════════════════════════════════════════════════════════════════════╗");
        terminal.WriteLine("║                          A MYSTERIOUS ENCOUNTER                              ║");
        terminal.WriteLine("╚══════════════════════════════════════════════════════════════════════════════╝");
        terminal.WriteLine("");

        terminal.SetColor("white");
        terminal.WriteLine($"  {encounter.DisguiseData.Name}");
        terminal.SetColor("gray");
        terminal.WriteLine($"  {encounter.DisguiseData.Description}");
        terminal.WriteLine("");

        await Task.Delay(2000);

        terminal.SetColor("bright_magenta");
        terminal.WriteLine($"  \"{encounter.Dialogue}\"");
        terminal.WriteLine("");

        await Task.Delay(2000);

        // Get response options
        var options = StrangerEncounterSystem.Instance.GetResponseOptions(encounter, currentPlayer);

        terminal.SetColor("cyan");
        terminal.WriteLine("  How do you respond?");
        terminal.WriteLine("");

        foreach (var (key, text, _) in options)
        {
            terminal.SetColor("yellow");
            terminal.Write($"    [{key}] ");
            terminal.SetColor("white");
            terminal.WriteLine(text);
        }

        terminal.WriteLine("");
        var choice = await terminal.GetInput("Your response: ");

        var selectedOption = options.FirstOrDefault(o => o.key.Equals(choice, StringComparison.OrdinalIgnoreCase));
        if (selectedOption.response != null)
        {
            terminal.WriteLine("");
            terminal.SetColor("magenta");
            terminal.WriteLine($"  {selectedOption.response}");
            terminal.WriteLine("");
        }

        // Record the encounter
        StrangerEncounterSystem.Instance.RecordEncounter(encounter);

        await terminal.PressAnyKey();
    }

    /// <summary>
    /// Display a memorable Town NPC encounter
    /// </summary>
    private async Task DisplayTownNPCEncounter(MemorableNPCData npc, NPCStoryStage stage, string npcKey)
    {
        terminal.ClearScreen();
        terminal.SetColor("bright_cyan");
        terminal.WriteLine("");
        terminal.WriteLine("╔══════════════════════════════════════════════════════════════════════════════╗");
        terminal.WriteLine($"║                    {npc.Name.ToUpper()} - {npc.Title.ToUpper(),-42}    ║");
        terminal.WriteLine("╚══════════════════════════════════════════════════════════════════════════════╝");
        terminal.WriteLine("");

        terminal.SetColor("gray");
        terminal.WriteLine($"  {npc.Description}");
        terminal.WriteLine("");

        await Task.Delay(1500);

        // Display dialogue
        terminal.SetColor("white");
        foreach (var line in stage.Dialogue)
        {
            terminal.WriteLine($"  {line}");
            await Task.Delay(1500);
        }
        terminal.WriteLine("");

        string? choiceMade = null;

        // Handle choice if present
        if (stage.Choice != null)
        {
            terminal.SetColor("yellow");
            terminal.WriteLine($"  {stage.Choice.Prompt}");
            terminal.WriteLine("");

            foreach (var option in stage.Choice.Options)
            {
                terminal.SetColor("cyan");
                terminal.Write($"    [{option.Key}] ");
                terminal.SetColor("white");
                terminal.WriteLine(option.Text);
            }
            terminal.WriteLine("");

            var input = await terminal.GetInput("Your choice: ");
            var selected = stage.Choice.Options.FirstOrDefault(o =>
                o.Key.Equals(input, StringComparison.OrdinalIgnoreCase));

            if (selected != null)
            {
                choiceMade = selected.Key;

                // Apply choice effects
                if (selected.GoldCost > 0 && currentPlayer.Gold >= selected.GoldCost)
                {
                    currentPlayer.Gold -= selected.GoldCost;
                    terminal.WriteLine($"  (You paid {selected.GoldCost} gold)", "yellow");
                }
                if (selected.Chivalry > 0)
                {
                    currentPlayer.Chivalry += selected.Chivalry;
                    terminal.WriteLine($"  (+{selected.Chivalry} Chivalry)", "bright_green");
                }
                if (selected.Darkness > 0)
                {
                    currentPlayer.Darkness += selected.Darkness;
                    terminal.WriteLine($"  (+{selected.Darkness} Darkness)", "dark_red");
                }
            }
        }

        // Apply rewards if present
        if (stage.Reward != null)
        {
            terminal.WriteLine("");
            terminal.SetColor("bright_green");

            if (stage.Reward.ChivalryBonus > 0)
            {
                currentPlayer.Chivalry += stage.Reward.ChivalryBonus;
                terminal.WriteLine($"  +{stage.Reward.ChivalryBonus} Chivalry!");
            }
            if (stage.Reward.Wisdom > 0)
            {
                currentPlayer.Wisdom += stage.Reward.Wisdom;
                terminal.WriteLine($"  +{stage.Reward.Wisdom} Wisdom!");
            }
            if (stage.Reward.Dexterity > 0)
            {
                currentPlayer.Dexterity += stage.Reward.Dexterity;
                terminal.WriteLine($"  +{stage.Reward.Dexterity} Dexterity!");
            }
            if (stage.Reward.WaveFragment.HasValue)
            {
                OceanPhilosophySystem.Instance.CollectFragment(stage.Reward.WaveFragment.Value);
                terminal.WriteLine("  (A fragment of truth settles into your consciousness...)");
            }
            if (stage.Reward.AwakeningMoment.HasValue)
            {
                OceanPhilosophySystem.Instance.ExperienceMoment(stage.Reward.AwakeningMoment.Value);
                terminal.WriteLine("  (Something shifts in your understanding...)");
            }
        }

        // Apply awakening gain
        if (stage.AwakeningGain > 0)
        {
            OceanPhilosophySystem.Instance.GainInsight(stage.AwakeningGain * 10);
            terminal.SetColor("magenta");
            terminal.WriteLine("  (A deeper understanding settles within you...)");
        }

        // Apply gold loss if any
        if (stage.GoldLost > 0)
        {
            var actualLoss = Math.Min(stage.GoldLost, currentPlayer.Gold);
            currentPlayer.Gold -= actualLoss;
            terminal.SetColor("red");
            terminal.WriteLine($"  (-{actualLoss} gold)");
        }

        // Complete the stage
        TownNPCStorySystem.Instance.CompleteStage(npcKey, stage.StageId, choiceMade);

        terminal.WriteLine("");
        await terminal.PressAnyKey();
    }

    /// <summary>
    /// Run a tick of world simulation (NPCs act, world events, etc.)
    /// </summary>
    private async Task RunWorldSimulationTick()
    {
        // Run game engine's periodic update for world simulation
        var gameEngine = GameEngine.Instance;
        if (gameEngine != null)
        {
            await gameEngine.PeriodicUpdate();
        }

        // Check for alignment-based random events (5% chance per tick)
        if (currentPlayer != null && terminal != null)
        {
            await AlignmentSystem.Instance.CheckAlignmentEvent(currentPlayer, terminal);
        }
    }

    /// <summary>
    /// Apply poison damage each turn if player is poisoned
    /// </summary>
    private async Task ApplyPoisonDamage()
    {
        if (currentPlayer == null || currentPlayer.Poison <= 0)
            return;

        // Poison damage scales with poison level and player level
        // Base damage: 2-5 HP per turn, plus level scaling, plus poison intensity
        var random = new Random();
        int baseDamage = 2 + random.Next(4);  // 2-5 base damage
        int levelScaling = currentPlayer.Level / 10;  // +1 per 10 levels
        int poisonBonus = currentPlayer.Poison / 5;  // +1 per 5 poison intensity
        int totalDamage = baseDamage + levelScaling + poisonBonus;

        // Cap damage at 10% of max HP to prevent instant deaths
        int maxDamage = (int)Math.Max(3, currentPlayer.MaxHP / 10);
        totalDamage = Math.Min(totalDamage, maxDamage);

        // Apply damage
        currentPlayer.HP -= totalDamage;

        // Show poison damage message
        terminal.SetColor("magenta");
        terminal.WriteLine($"The poison courses through your veins! (-{totalDamage} HP)");

        // Check if player died from poison
        if (currentPlayer.HP <= 0)
        {
            currentPlayer.HP = 0;
            terminal.SetColor("red");
            terminal.WriteLine("The poison has claimed your life!");
            await Task.Delay(1500);
        }
        else
        {
            // Small chance (5%) for poison to naturally wear off slightly each turn
            if (new Random().Next(100) < 5)
            {
                currentPlayer.Poison = Math.Max(0, currentPlayer.Poison - 1);
                if (currentPlayer.Poison == 0)
                {
                    terminal.SetColor("green");
                    terminal.WriteLine("The poison has finally left your system!");
                }
            }
            await Task.Delay(500);
        }
    }

    /// <summary>
    /// Display the location screen
    /// </summary>
    protected virtual void DisplayLocation()
    {
        terminal.ClearScreen();

        // Breadcrumb navigation
        ShowBreadcrumb();

        // Location header
        terminal.SetColor("bright_yellow");
        terminal.WriteLine(Name);
        terminal.SetColor("yellow");
        terminal.WriteLine(new string('═', Name.Length));
        terminal.WriteLine("");
        
        // Location description
        terminal.SetColor("white");
        terminal.WriteLine(Description);
        terminal.WriteLine("");
        
        // Show NPCs in location
        ShowNPCsInLocation();
        
        // Show available actions
        ShowLocationActions();
        
        // Show exits
        ShowExits();
        
        // Status line
        ShowStatusLine();
    }
    
    /// <summary>
    /// Map GameLocation enum to NPC location strings
    /// </summary>
    protected virtual string GetNPCLocationString()
    {
        return LocationId switch
        {
            GameLocation.MainStreet => "Main Street",
            GameLocation.TheInn => "Inn",
            GameLocation.Church => "Temple",
            GameLocation.WeaponShop => "Weapon Shop",
            GameLocation.ArmorShop => "Armor Shop",
            GameLocation.MagicShop => "Magic Shop",
            GameLocation.Marketplace => "Market",
            GameLocation.Steroids => "Level Master",
            GameLocation.DarkAlley => "Dark Alley",
            GameLocation.Orbs => "Inn",
            GameLocation.BobsBeer => "Inn",
            GameLocation.Bank => "Bank",
            GameLocation.Healer => "Healer",
            GameLocation.Dungeons => "Dungeon",
            _ => Name
        };
    }

    /// <summary>
    /// Get NPCs currently at this location from NPCSpawnSystem
    /// </summary>
    protected virtual List<NPC> GetLiveNPCsAtLocation()
    {
        var locationString = GetNPCLocationString();
        var allNPCs = NPCSpawnSystem.Instance.ActiveNPCs ?? new List<NPC>();

        return allNPCs
            .Where(npc => npc.IsAlive &&
                   npc.CurrentLocation?.Equals(locationString, StringComparison.OrdinalIgnoreCase) == true)
            .ToList();
    }

    private static Random _npcRandom = new Random();

    /// <summary>
    /// Get a random shout/action for an NPC based on their personality
    /// </summary>
    protected virtual string GetNPCShout(NPC npc)
    {
        var shouts = new List<string>();

        // Personality-based shouts
        if (npc.Darkness > npc.Chivalry)
        {
            // Evil NPCs
            shouts.AddRange(new[] {
                "glares at you menacingly",
                "mutters a curse under their breath",
                "eyes your gold pouch hungrily",
                "spits on the ground as you pass",
                "sharpens a dagger while watching you",
                "laughs coldly",
                "sneers at the weak",
            });
        }
        else if (npc.Chivalry > 500)
        {
            // Good NPCs
            shouts.AddRange(new[] {
                "nods respectfully",
                "offers a friendly wave",
                "shares news of their latest adventures",
                "mentions a rumor they heard",
                "practices sword forms gracefully",
                "hums a cheerful tune",
                "smiles warmly",
            });
        }
        else
        {
            // Neutral NPCs
            shouts.AddRange(new[] {
                "goes about their business",
                "seems lost in thought",
                "examines some merchandise",
                "chats with a merchant",
                "stretches after a long journey",
                "counts their gold coins",
                "yawns lazily",
            });
        }

        // Class-based shouts
        switch (npc.Class)
        {
            case CharacterClass.Warrior:
            case CharacterClass.Barbarian:
                shouts.Add("flexes their muscles");
                shouts.Add("polishes their weapon");
                break;
            case CharacterClass.Magician:
            case CharacterClass.Sage:
                shouts.Add("reads from an ancient tome");
                shouts.Add("mutters arcane words");
                break;
            case CharacterClass.Cleric:
            case CharacterClass.Paladin:
                shouts.Add("offers a blessing");
                shouts.Add("prays quietly");
                break;
            case CharacterClass.Assassin:
                shouts.Add("watches from the shadows");
                shouts.Add("tests the edge of a blade");
                break;
        }

        return shouts[_npcRandom.Next(shouts.Count)];
    }

    /// <summary>
    /// Get alignment display string
    /// </summary>
    protected virtual string GetAlignmentDisplay(NPC npc)
    {
        if (npc.Darkness > npc.Chivalry + 300) return "(Evil)";
        if (npc.Chivalry > npc.Darkness + 300) return "(Good)";
        return "(Neutral)";
    }

    /// <summary>
    /// Get relationship display information (color, text, symbol) based on relationship level
    /// Relationship levels: Married=10, Love=20, Passion=30, Friendship=40, Trust=50,
    /// Respect=60, Normal=70, Suspicious=80, Anger=90, Enemy=100, Hate=110
    /// </summary>
    protected virtual (string color, string text, string symbol) GetRelationshipDisplayInfo(int relationLevel)
    {
        return relationLevel switch
        {
            <= GameConfig.RelationMarried => ("bright_red", "Married", "<3"),     // 10 - Married (red with heart)
            <= GameConfig.RelationLove => ("bright_magenta", "In Love", "<3"),    // 20 - Love
            <= GameConfig.RelationPassion => ("magenta", "Passionate", ""),        // 30 - Passion
            <= GameConfig.RelationFriendship => ("bright_cyan", "Friends", ""),   // 40 - Friendship
            <= GameConfig.RelationTrust => ("cyan", "Trusted", ""),               // 50 - Trust
            <= GameConfig.RelationRespect => ("bright_green", "Respected", ""),   // 60 - Respect
            <= GameConfig.RelationNormal => ("gray", "Stranger", ""),             // 70 - Normal/Neutral
            <= GameConfig.RelationSuspicious => ("yellow", "Wary", ""),           // 80 - Suspicious
            <= GameConfig.RelationAnger => ("bright_yellow", "Hostile", ""),      // 90 - Anger
            <= GameConfig.RelationEnemy => ("red", "Enemy", ""),                  // 100 - Enemy
            _ => ("dark_red", "Hated", "")                                        // 110+ - Hate
        };
    }

    /// <summary>
    /// Show NPCs in this location - dynamically fetched from NPCSpawnSystem
    /// </summary>
    protected virtual void ShowNPCsInLocation()
    {
        // Get live NPCs from the spawn system
        var liveNPCs = GetLiveNPCsAtLocation();

        // Also include any static LocationNPCs (special NPCs like shopkeepers)
        var allNPCs = new List<NPC>(LocationNPCs);
        foreach (var npc in liveNPCs)
        {
            if (!allNPCs.Any(n => n.Name2 == npc.Name2))
                allNPCs.Add(npc);
        }

        if (allNPCs.Count > 0)
        {
            terminal.SetColor("bright_cyan");
            terminal.WriteLine("People here:");

            foreach (var npc in allNPCs.Take(8)) // Limit display to 8 NPCs
            {
                if (npc.IsAlive)
                {
                    // Color based on alignment
                    if (npc.Darkness > npc.Chivalry + 200)
                        terminal.SetColor("red");
                    else if (npc.Chivalry > npc.Darkness + 200)
                        terminal.SetColor("bright_green");
                    else
                        terminal.SetColor("cyan");

                    var alignment = GetAlignmentDisplay(npc);
                    var classStr = npc.Class.ToString();
                    var shout = GetNPCShout(npc);

                    // Show NPC with their current action/shout
                    terminal.WriteLine($"  {npc.Name2} the Lv{npc.Level} {classStr} {alignment} - {shout}");
                }
            }

            if (allNPCs.Count > 8)
            {
                terminal.SetColor("gray");
                terminal.WriteLine($"  ... and {allNPCs.Count - 8} others");
            }

            terminal.WriteLine("");
        }
    }
    
    /// <summary>
    /// Show location-specific actions
    /// </summary>
    protected virtual void ShowLocationActions()
    {
        if (LocationActions.Count > 0)
        {
            terminal.SetColor("white");
            terminal.WriteLine("Available actions:");
            
            for (int i = 0; i < LocationActions.Count; i++)
            {
                terminal.WriteLine($"  {i + 1}. {LocationActions[i]}");
            }
            terminal.WriteLine("");
        }
    }
    
    /// <summary>
    /// Show available exits (Pascal-compatible)
    /// </summary>
    protected virtual void ShowExits()
    {
        if (PossibleExits.Count > 0)
        {
            terminal.SetColor("yellow");
            terminal.WriteLine("Exits:");
            
            foreach (var exit in PossibleExits)
            {
                var exitName = GetLocationName(exit);
                var exitKey = GetLocationKey(exit);
                terminal.WriteLine($"  ({exitKey}) {exitName}");
            }
            terminal.WriteLine("");
        }
    }
    
    /// <summary>
    /// Show breadcrumb navigation at top of screen
    /// </summary>
    protected virtual void ShowBreadcrumb()
    {
        terminal.SetColor("gray");
        terminal.Write("Location: ");
        terminal.SetColor("bright_cyan");

        // Build breadcrumb path based on current location
        string breadcrumb = GetBreadcrumbPath();
        terminal.WriteLine(breadcrumb);
        terminal.WriteLine("");
    }

    /// <summary>
    /// Get breadcrumb path for current location
    /// </summary>
    protected virtual string GetBreadcrumbPath()
    {
        // Default: just show location name
        // Subclasses can override for more complex paths (e.g., "Main Street > Dungeons > Level 3")
        switch (LocationId)
        {
            case GameLocation.MainStreet:
                return "Main Street";
            case GameLocation.Home:
                return "Anchor Road > Your Home";
            case GameLocation.AnchorRoad:
                return "Main Street > Anchor Road";
            case GameLocation.WeaponShop:
                return "Main Street > Weapon Shop";
            case GameLocation.ArmorShop:
                return "Main Street > Armor Shop";
            case GameLocation.MagicShop:
                return "Main Street > Magic Shop";
            case GameLocation.TheInn:
                return "Main Street > The Inn";
            case GameLocation.DarkAlley:
                return "Main Street > Dark Alley";
            case GameLocation.Church:
                return "Main Street > Church";
            case GameLocation.Bank:
                return "Main Street > Bank";
            case GameLocation.Castle:
                return "Main Street > Royal Castle";
            case GameLocation.Prison:
                return "Anchor Road > Outside Prison";
            default:
                return Name ?? "Unknown";
        }
    }

    /// <summary>
    /// Show status line at bottom
    /// </summary>
    protected virtual void ShowStatusLine()
    {
        // HP with urgency coloring
        terminal.SetColor("gray");
        terminal.Write("HP: ");
        float hpPercent = currentPlayer.MaxHP > 0 ? (float)currentPlayer.HP / currentPlayer.MaxHP : 0;
        string hpColor = hpPercent > 0.5f ? "bright_green" : hpPercent > 0.25f ? "yellow" : "bright_red";
        terminal.SetColor(hpColor);
        terminal.Write($"{currentPlayer.HP}");
        terminal.SetColor("gray");
        terminal.Write("/");
        terminal.SetColor(hpColor);
        terminal.Write($"{currentPlayer.MaxHP}");

        terminal.SetColor("gray");
        terminal.Write(" | Gold: ");
        terminal.SetColor("yellow");
        terminal.Write($"{currentPlayer.Gold:N0}");

        if (currentPlayer.MaxMana > 0)
        {
            terminal.SetColor("gray");
            terminal.Write(" | Mana: ");
            terminal.SetColor("blue");
            terminal.Write($"{currentPlayer.Mana}");
            terminal.SetColor("gray");
            terminal.Write("/");
            terminal.SetColor("blue");
            terminal.Write($"{currentPlayer.MaxMana}");
        }

        terminal.SetColor("gray");
        terminal.Write(" | Lv ");
        terminal.SetColor("cyan");
        terminal.Write($"{currentPlayer.Level}");

        // XP progress to next level
        if (currentPlayer.Level < GameConfig.MaxLevel)
        {
            long currentXP = currentPlayer.Experience;
            long nextLevelXP = GetExperienceForLevel(currentPlayer.Level + 1);
            long prevLevelXP = GetExperienceForLevel(currentPlayer.Level);
            long xpIntoLevel = currentXP - prevLevelXP;
            long xpNeeded = nextLevelXP - prevLevelXP;
            int xpPercent = xpNeeded > 0 ? (int)((xpIntoLevel * 100) / xpNeeded) : 0;
            xpPercent = Math.Clamp(xpPercent, 0, 100);

            terminal.SetColor("gray");
            terminal.Write(" (");
            terminal.SetColor(xpPercent >= 90 ? "bright_green" : "white");
            terminal.Write($"{xpPercent}%");
            terminal.SetColor("gray");
            terminal.Write(")");
        }

        terminal.WriteLine("");
        terminal.WriteLine("");

        // Quick command bar
        ShowQuickCommandBar();
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
            exp += (long)(Math.Pow(i, 1.8) * 50);
        }
        return exp;
    }

    /// <summary>
    /// Show quick command bar with common keyboard shortcuts
    /// </summary>
    protected virtual void ShowQuickCommandBar()
    {
        terminal.SetColor("darkgray");
        terminal.Write("─────────────────────────────────────────────────────────────────────────────");
        terminal.WriteLine("");

        terminal.SetColor("gray");
        terminal.Write("Quick Commands: ");

        terminal.SetColor("darkgray");
        terminal.Write("[");
        terminal.SetColor("cyan");
        terminal.Write("S");
        terminal.SetColor("darkgray");
        terminal.Write("]");
        terminal.SetColor("white");
        terminal.Write("tatus  ");

        if (LocationId != GameLocation.MainStreet)
        {
            terminal.SetColor("darkgray");
            terminal.Write("[");
            terminal.SetColor("cyan");
            terminal.Write("Q");
            terminal.SetColor("darkgray");
            terminal.Write("]");
            terminal.SetColor("white");
            terminal.Write("uick Return  ");
        }

        terminal.SetColor("darkgray");
        terminal.Write("[");
        terminal.SetColor("cyan");
        terminal.Write("*");
        terminal.SetColor("darkgray");
        terminal.Write("]");
        terminal.SetColor("white");
        terminal.Write("Inventory  ");

        terminal.SetColor("darkgray");
        terminal.Write("[");
        terminal.SetColor("cyan");
        terminal.Write("?");
        terminal.SetColor("darkgray");
        terminal.Write("]");
        terminal.SetColor("white");
        terminal.Write("Help  ");

        // Show Talk option if NPCs are present
        var npcsHere = GetLiveNPCsAtLocation();
        if (npcsHere.Count > 0)
        {
            terminal.SetColor("darkgray");
            terminal.Write("[");
            terminal.SetColor("bright_green");
            terminal.Write("0");
            terminal.SetColor("darkgray");
            terminal.Write("]");
            terminal.SetColor("white");
            terminal.Write($" Talk ({npcsHere.Count})  ");
        }

        // Show Preferences option
        terminal.SetColor("darkgray");
        terminal.Write("[");
        terminal.SetColor("yellow");
        terminal.Write("~");
        terminal.SetColor("darkgray");
        terminal.Write("]");
        terminal.SetColor("white");
        terminal.Write("Prefs  ");

        // Show slash commands hint
        terminal.SetColor("darkgray");
        terminal.Write("[");
        terminal.SetColor("bright_magenta");
        terminal.Write("/");
        terminal.SetColor("darkgray");
        terminal.Write("]");
        terminal.SetColor("white");
        terminal.Write("Cmds  ");

        // Show bug report hint
        terminal.SetColor("darkgray");
        terminal.Write("[");
        terminal.SetColor("red");
        terminal.Write("!");
        terminal.SetColor("darkgray");
        terminal.Write("]");
        terminal.SetColor("white");
        terminal.Write("Bug");

        terminal.WriteLine("");
        terminal.WriteLine("");
    }

    /// <summary>
    /// Get user choice
    /// </summary>
    protected virtual async Task<string> GetUserChoice()
    {
        terminal.SetColor("bright_white");
        return await terminal.GetInput("Your choice: ");
    }
    
    /// <summary>
    /// Try to process global quick commands (* for inventory, ? for help, etc.)
    /// Returns (handled, shouldExit) - if handled is true, the command was processed
    /// </summary>
    protected async Task<(bool handled, bool shouldExit)> TryProcessGlobalCommand(string choice)
    {
        if (string.IsNullOrWhiteSpace(choice))
            return (false, false);

        var upperChoice = choice.ToUpper().Trim();

        // Handle slash commands (works from any location)
        if (choice.StartsWith("/"))
        {
            // Try online chat commands first (preserve original casing for message content)
            if (OnlineChatSystem.IsActive)
            {
                var handled = await OnlineChatSystem.Instance!.TryProcessCommand(choice.Trim(), terminal);
                if (handled) return (true, false);
            }

            return await ProcessSlashCommand(choice.Substring(1).ToLower().Trim());
        }

        switch (upperChoice)
        {
            case "*":
                await ShowInventory();
                return (true, false);
            case "~":
            case "PREFS":
            case "PREFERENCES":
                await ShowPreferencesMenu();
                return (true, false);
            case "0":
            case "TALK":
                await TalkToNPC();
                return (true, false);
            case "?":
            case "HELP":
                await ShowQuickCommandsHelp();
                return (true, false);
            case "!":
                await BugReportSystem.ReportBug(terminal, currentPlayer);
                return (true, false);
            default:
                return (false, false);
        }
    }

    /// <summary>
    /// Process slash commands like /stats, /quests, /time, etc.
    /// </summary>
    protected async Task<(bool handled, bool shouldExit)> ProcessSlashCommand(string command)
    {
        switch (command)
        {
            case "":
            case "?":
            case "help":
            case "commands":
                await ShowQuickCommandsHelp();
                return (true, false);

            case "s":
            case "st":
            case "stats":
            case "status":
                await ShowStatus();
                return (true, false);

            case "i":
            case "inv":
            case "inventory":
                await ShowInventory();
                return (true, false);

            case "q":
            case "quest":
            case "quests":
                await ShowActiveQuests();
                return (true, false);

            case "g":
            case "gold":
                await ShowGoldStatus();
                return (true, false);

            case "h":
            case "hp":
            case "health":
                await ShowHealthStatus();
                return (true, false);

            case "p":
            case "pref":
            case "prefs":
            case "preferences":
                await ShowPreferencesMenu();
                return (true, false);

            case "pot":
            case "potion":
                await UseQuickPotion();
                return (true, false);

            case "bug":
            case "report":
            case "bugreport":
                await BugReportSystem.ReportBug(terminal, currentPlayer);
                return (true, false);

            default:
                terminal.WriteLine("");
                terminal.SetColor("red");
                terminal.WriteLine($"  Unknown command: /{command}");
                terminal.SetColor("gray");
                terminal.WriteLine("  Type /help or just / for a list of commands.");
                terminal.WriteLine("");
                await terminal.PressAnyKey();
                return (true, false);
        }
    }

    /// <summary>
    /// Show quick commands help
    /// </summary>
    protected async Task ShowQuickCommandsHelp()
    {
        terminal.WriteLine("");
        terminal.SetColor("bright_cyan");
        terminal.WriteLine("╔══════════════════════════════════════════════════════════════════════════════╗");
        terminal.WriteLine("║                           QUICK COMMANDS                                     ║");
        terminal.WriteLine("╠══════════════════════════════════════════════════════════════════════════════╣");
        terminal.SetColor("white");
        terminal.WriteLine("║  These commands work from any location:                                      ║");
        terminal.WriteLine("║                                                                              ║");
        terminal.SetColor("bright_yellow");
        terminal.Write("║  ");
        terminal.SetColor("cyan");
        terminal.Write("/stats    ");
        terminal.SetColor("gray");
        terminal.Write("or ");
        terminal.SetColor("cyan");
        terminal.Write("/s  ");
        terminal.SetColor("white");
        terminal.WriteLine(" - View your character stats                              ║");

        terminal.SetColor("bright_yellow");
        terminal.Write("║  ");
        terminal.SetColor("cyan");
        terminal.Write("/inventory");
        terminal.SetColor("gray");
        terminal.Write(" or ");
        terminal.SetColor("cyan");
        terminal.Write("/i  ");
        terminal.SetColor("white");
        terminal.WriteLine("- View your inventory                                    ║");

        terminal.SetColor("bright_yellow");
        terminal.Write("║  ");
        terminal.SetColor("cyan");
        terminal.Write("/quests   ");
        terminal.SetColor("gray");
        terminal.Write("or ");
        terminal.SetColor("cyan");
        terminal.Write("/q  ");
        terminal.SetColor("white");
        terminal.WriteLine("- View active quests                                     ║");

        terminal.SetColor("bright_yellow");
        terminal.Write("║  ");
        terminal.SetColor("cyan");
        terminal.Write("/gold     ");
        terminal.SetColor("gray");
        terminal.Write("or ");
        terminal.SetColor("cyan");
        terminal.Write("/g  ");
        terminal.SetColor("white");
        terminal.WriteLine("- Show gold and bank balance                             ║");

        terminal.SetColor("bright_yellow");
        terminal.Write("║  ");
        terminal.SetColor("cyan");
        terminal.Write("/health   ");
        terminal.SetColor("gray");
        terminal.Write("or ");
        terminal.SetColor("cyan");
        terminal.Write("/hp ");
        terminal.SetColor("white");
        terminal.WriteLine("- Show health and mana status                            ║");

        terminal.SetColor("bright_yellow");
        terminal.Write("║  ");
        terminal.SetColor("cyan");
        terminal.Write("/potion   ");
        terminal.SetColor("gray");
        terminal.Write("or ");
        terminal.SetColor("cyan");
        terminal.Write("/pot");
        terminal.SetColor("white");
        terminal.WriteLine("- Use a healing potion                                   ║");

        terminal.SetColor("bright_yellow");
        terminal.Write("║  ");
        terminal.SetColor("cyan");
        terminal.Write("/prefs    ");
        terminal.SetColor("gray");
        terminal.Write("or ");
        terminal.SetColor("cyan");
        terminal.Write("/p  ");
        terminal.SetColor("white");
        terminal.WriteLine("- Open preferences menu                                  ║");

        terminal.SetColor("bright_yellow");
        terminal.Write("║  ");
        terminal.SetColor("cyan");
        terminal.Write("/bug      ");
        terminal.SetColor("white");
        terminal.WriteLine("        - Report a bug (opens GitHub)                        ║");

        terminal.SetColor("white");
        terminal.WriteLine("║                                                                              ║");
        terminal.WriteLine("║  Quick keys (single character):                                              ║");
        terminal.SetColor("bright_yellow");
        terminal.Write("║  ");
        terminal.SetColor("cyan");
        terminal.Write("*  ");
        terminal.SetColor("white");
        terminal.WriteLine("- Inventory    ");
        terminal.SetColor("cyan");
        terminal.Write("~  ");
        terminal.SetColor("white");
        terminal.WriteLine("- Preferences    ");
        terminal.SetColor("cyan");
        terminal.Write("S  ");
        terminal.SetColor("white");
        terminal.WriteLine("- Status    ");
        terminal.SetColor("cyan");
        terminal.Write("?  ");
        terminal.SetColor("white");
        terminal.WriteLine("- This help    ");
        terminal.SetColor("cyan");
        terminal.Write("!  ");
        terminal.SetColor("white");
        terminal.WriteLine("- Report bug         ║");

        // Online chat commands (only shown when in online mode)
        if (OnlineChatSystem.IsActive)
        {
            terminal.SetColor("white");
            terminal.WriteLine("║                                                                              ║");
            terminal.WriteLine("║  Online commands:                                                            ║");
            terminal.SetColor("bright_yellow");
            terminal.Write("║  ");
            terminal.SetColor("bright_green");
            terminal.Write("/say <msg>          ");
            terminal.SetColor("white");
            terminal.WriteLine("- Chat to all online players                         ║");
            terminal.SetColor("bright_yellow");
            terminal.Write("║  ");
            terminal.SetColor("bright_green");
            terminal.Write("/tell <name> <msg>  ");
            terminal.SetColor("white");
            terminal.WriteLine("- Private message to a player                        ║");
            terminal.SetColor("bright_yellow");
            terminal.Write("║  ");
            terminal.SetColor("bright_green");
            terminal.Write("/who                ");
            terminal.SetColor("white");
            terminal.WriteLine("- See who's online                                   ║");
            terminal.SetColor("bright_yellow");
            terminal.Write("║  ");
            terminal.SetColor("bright_green");
            terminal.Write("/news               ");
            terminal.SetColor("white");
            terminal.WriteLine("- Recent server news                                 ║");
        }

        terminal.SetColor("bright_cyan");
        terminal.WriteLine("╚══════════════════════════════════════════════════════════════════════════════╝");
        terminal.WriteLine("");

        await terminal.PressAnyKey();
    }

    /// <summary>
    /// Show active quests summary
    /// </summary>
    protected virtual async Task ShowActiveQuests()
    {
        terminal.WriteLine("");
        terminal.SetColor("bright_magenta");
        terminal.WriteLine("╔══════════════════════════════════════════════════════════════════════════════╗");
        terminal.WriteLine("║                           ACTIVE QUESTS                                      ║");
        terminal.WriteLine("╚══════════════════════════════════════════════════════════════════════════════╝");

        var playerName = currentPlayer?.Name2 ?? currentPlayer?.DisplayName ?? "";
        var activeQuests = QuestSystem.GetActiveQuestsForPlayer(playerName);

        if (activeQuests == null || activeQuests.Count == 0)
        {
            terminal.SetColor("gray");
            terminal.WriteLine("  No active quests. Visit the Castle or talk to NPCs to find quests.");
        }
        else
        {
            foreach (var quest in activeQuests.Take(5)) // Show up to 5 quests
            {
                terminal.SetColor("bright_yellow");
                terminal.Write($"  • {quest.Title ?? "Unknown Quest"}");
                terminal.SetColor("gray");
                terminal.WriteLine($" - {quest.GetTargetDescription()}");
            }

            if (activeQuests.Count > 5)
            {
                terminal.SetColor("gray");
                terminal.WriteLine($"  ... and {activeQuests.Count - 5} more quests.");
            }
        }

        terminal.WriteLine("");
        await terminal.PressAnyKey();
    }

    /// <summary>
    /// Show gold status
    /// </summary>
    protected async Task ShowGoldStatus()
    {
        terminal.WriteLine("");
        terminal.SetColor("bright_yellow");
        terminal.Write("  Gold on Hand: ");
        terminal.SetColor("white");
        terminal.WriteLine($"{currentPlayer?.Gold:N0}");

        var bankBalance = currentPlayer?.BankGold ?? 0;
        terminal.SetColor("bright_cyan");
        terminal.Write("  Bank Balance: ");
        terminal.SetColor("white");
        terminal.WriteLine($"{bankBalance:N0}");

        terminal.SetColor("gray");
        terminal.Write("  Total Wealth: ");
        terminal.SetColor("bright_green");
        terminal.WriteLine($"{(currentPlayer?.Gold ?? 0) + bankBalance:N0}");
        terminal.WriteLine("");
        await terminal.PressAnyKey();
    }

    /// <summary>
    /// Show health status
    /// </summary>
    protected async Task ShowHealthStatus()
    {
        terminal.WriteLine("");
        int hpPercent = currentPlayer?.MaxHP > 0 ? (int)(100.0 * currentPlayer.CurrentHP / currentPlayer.MaxHP) : 0;
        int mpPercent = currentPlayer?.MaxMana > 0 ? (int)(100.0 * currentPlayer.CurrentMana / currentPlayer.MaxMana) : 0;

        terminal.SetColor("bright_red");
        terminal.Write("  HP: ");
        terminal.SetColor(hpPercent > 50 ? "bright_green" : hpPercent > 25 ? "yellow" : "red");
        terminal.WriteLine($"{currentPlayer?.CurrentHP}/{currentPlayer?.MaxHP} ({hpPercent}%)");

        terminal.SetColor("bright_blue");
        terminal.Write("  MP: ");
        terminal.SetColor(mpPercent > 50 ? "bright_cyan" : mpPercent > 25 ? "cyan" : "gray");
        terminal.WriteLine($"{currentPlayer?.CurrentMana}/{currentPlayer?.MaxMana} ({mpPercent}%)");
        terminal.WriteLine("");
        await terminal.PressAnyKey();
    }

    /// <summary>
    /// Use a healing potion outside of combat via /potion quick command
    /// </summary>
    protected async Task UseQuickPotion()
    {
        terminal.WriteLine("");
        if (currentPlayer == null)
        {
            terminal.WriteLine("  No active character.", "gray");
            await terminal.PressAnyKey();
            return;
        }

        if (currentPlayer.HP >= currentPlayer.MaxHP)
        {
            terminal.SetColor("cyan");
            terminal.WriteLine("  You're already at full health!");
            terminal.WriteLine("");
            await terminal.PressAnyKey();
            return;
        }

        if (currentPlayer.Healing <= 0)
        {
            terminal.SetColor("yellow");
            terminal.WriteLine("  You don't have any healing potions.");
            terminal.SetColor("gray");
            terminal.WriteLine("  Visit the Healer to buy some.");
            terminal.WriteLine("");
            await terminal.PressAnyKey();
            return;
        }

        // Use one potion
        long healAmount = 30 + currentPlayer.Level * 5 + new Random().Next(10, 30);
        healAmount = Math.Min(healAmount, currentPlayer.MaxHP - currentPlayer.HP);
        currentPlayer.HP += healAmount;
        currentPlayer.Healing--;
        currentPlayer.Statistics?.RecordPotionUsed(healAmount);

        terminal.SetColor("bright_green");
        terminal.WriteLine($"  You drink a healing potion and recover {healAmount} HP!");
        terminal.SetColor("cyan");
        terminal.WriteLine($"  HP: {currentPlayer.HP}/{currentPlayer.MaxHP}  |  Potions remaining: {currentPlayer.Healing}/{currentPlayer.MaxPotions}");
        terminal.WriteLine("");
        await terminal.PressAnyKey();
    }

    /// <summary>
    /// Process user choice - returns true if should exit location
    /// </summary>
    protected virtual async Task<bool> ProcessChoice(string choice)
    {
        if (string.IsNullOrWhiteSpace(choice))
            return false;

        var upperChoice = choice.ToUpper().Trim();
        
        // Check for exits first
        foreach (var exit in PossibleExits)
        {
            if (upperChoice == GetLocationKey(exit))
            {
                await NavigateToLocation(exit);
                return true;
            }
        }
        
        // Check for numbered actions
        if (int.TryParse(upperChoice, out int actionIndex))
        {
            if (actionIndex > 0 && actionIndex <= LocationActions.Count)
            {
                await ExecuteLocationAction(actionIndex - 1);
                return false;
            }
        }
        
        // Check for special commands
        switch (upperChoice)
        {
            case "S":
                await ShowStatus();
                break;
            case "*":
                await ShowInventory();
                break;
            case "?":
                // Help/menu already shown
                break;
            case "Q":
                if (LocationId != GameLocation.MainStreet)
                {
                    await NavigateToLocation(GameLocation.MainStreet);
                    return true;
                }
                break;
            case "0":
            case "TALK":
                await TalkToNPC();
                break;
            case "~":
            case "PREFS":
            case "PREFERENCES":
                await ShowPreferencesMenu();
                break;
            default:
                terminal.SetColor("red");
                terminal.WriteLine($"Invalid choice: '{choice}'");
                terminal.SetColor("gray");
                terminal.Write("Try: ");
                terminal.SetColor("cyan");
                terminal.Write("[S]");
                terminal.SetColor("gray");
                terminal.Write("tatus, ");
                terminal.SetColor("cyan");
                terminal.Write("[*]");
                terminal.SetColor("gray");
                terminal.Write("Inventory");

                if (LocationId != GameLocation.MainStreet)
                {
                    terminal.Write(", ");
                    terminal.SetColor("cyan");
                    terminal.Write("[Q]");
                    terminal.SetColor("gray");
                    terminal.Write("uick Return");
                }

                terminal.Write(", or ");
                terminal.SetColor("cyan");
                terminal.Write("[?]");
                terminal.SetColor("gray");
                terminal.WriteLine(" for help");
                await Task.Delay(2000);
                break;
        }

        return false;
    }
    
    /// <summary>
    /// Execute a location-specific action
    /// </summary>
    protected virtual async Task ExecuteLocationAction(int actionIndex)
    {
        // Override in derived classes
        terminal.WriteLine("Nothing happens.", "gray");
        await Task.Delay(1000);
    }
    
    /// <summary>
    /// Navigate to another location
    /// </summary>
    protected virtual async Task NavigateToLocation(GameLocation destination)
    {
        terminal.WriteLine($"Heading to {GetLocationName(destination)}...", "yellow");
        await Task.Delay(500);

        // Check for faction ambush while traveling
        var ambushed = await CheckFactionAmbush();
        if (ambushed)
        {
            // After surviving ambush, continue to destination
            terminal.WriteLine("");
            terminal.SetColor("yellow");
            terminal.WriteLine("After the encounter, you continue on your way...");
            await Task.Delay(1000);
        }

        // Throw exception to signal location change
        throw new LocationExitException(destination);
    }

    // Track last ambush to prevent constant ambushes
    private static int _travelsSinceLastAmbush = 0;
    private const int MIN_TRAVELS_BETWEEN_AMBUSHES = 3;

    /// <summary>
    /// Check for and handle faction ambushes while traveling
    /// Returns true if an ambush occurred (regardless of outcome)
    /// </summary>
    protected virtual async Task<bool> CheckFactionAmbush()
    {
        var factionSystem = UsurperRemake.Systems.FactionSystem.Instance;
        var npcSpawn = UsurperRemake.Systems.NPCSpawnSystem.Instance;
        var random = new Random();

        // Cooldown: Can't be ambushed too frequently
        _travelsSinceLastAmbush++;
        if (_travelsSinceLastAmbush < MIN_TRAVELS_BETWEEN_AMBUSHES)
            return false;

        // Get NPCs that could ambush (alive, with factions, hostile to player)
        var potentialAmbushers = npcSpawn?.ActiveNPCs?
            .Where(npc => !npc.IsDead &&
                          npc.IsAlive &&  // Must have HP > 0
                          npc.NPCFaction.HasValue &&
                          factionSystem.IsNPCHostileToPlayer(npc.NPCFaction) &&
                          npc.Level <= currentPlayer.Level + 5) // Don't ambush with NPCs way higher level
            .ToList();

        if (potentialAmbushers == null || potentialAmbushers.Count == 0)
            return false;

        // Roll ONCE per travel, picking a random hostile NPC
        // This prevents the "each NPC rolls" problem that caused constant ambushes
        var randomAmbusher = potentialAmbushers[random.Next(potentialAmbushers.Count)];
        int ambushChance = factionSystem.GetAmbushChance(randomAmbusher.NPCFaction);

        // Scale chance slightly by number of hostile NPCs (more enemies = slightly more danger)
        // But cap the bonus to prevent runaway scaling
        int hostileBonus = Math.Min(10, potentialAmbushers.Count);
        ambushChance = Math.Min(40, ambushChance + hostileBonus); // Cap at 40%

        if (random.Next(100) < ambushChance)
        {
            // Ambush triggered!
            _travelsSinceLastAmbush = 0; // Reset cooldown
            await HandleFactionAmbush(randomAmbusher, factionSystem);
            return true;
        }

        return false;
    }

    /// <summary>
    /// Handle a faction ambush encounter
    /// </summary>
    private async Task HandleFactionAmbush(NPC ambusher, UsurperRemake.Systems.FactionSystem factionSystem)
    {
        terminal.ClearScreen();

        // Get faction color
        string factionColor = ambusher.NPCFaction switch
        {
            UsurperRemake.Systems.Faction.TheFaith => "bright_cyan",
            UsurperRemake.Systems.Faction.TheShadows => "bright_magenta",
            UsurperRemake.Systems.Faction.TheCrown => "bright_yellow",
            _ => "white"
        };

        // Show ambush header
        terminal.SetColor("bright_red");
        terminal.WriteLine("╔══════════════════════════════════════════════════════════════════════════════╗");
        terminal.WriteLine("║                              AMBUSH!                                         ║");
        terminal.WriteLine("╚══════════════════════════════════════════════════════════════════════════════╝");
        terminal.WriteLine("");

        // Show faction context
        if (ambusher.NPCFaction.HasValue)
        {
            var factionData = UsurperRemake.Systems.FactionSystem.Factions[ambusher.NPCFaction.Value];
            terminal.SetColor(factionColor);
            terminal.WriteLine($"A member of {factionData.Name} has found you!");
            terminal.WriteLine("");
        }

        // Show ambush dialogue
        string dialogue = factionSystem.GetAmbushDialogue(
            ambusher.NPCFaction ?? UsurperRemake.Systems.Faction.TheCrown,
            ambusher.Name);

        terminal.SetColor("white");
        terminal.WriteLine(dialogue);
        terminal.WriteLine("");
        await Task.Delay(2000);

        // Show ambusher stats
        terminal.SetColor("gray");
        terminal.WriteLine($"  {ambusher.Name} - Level {ambusher.Level} {ambusher.Class}");
        terminal.WriteLine($"  HP: {ambusher.HP}/{ambusher.MaxHP}  STR: {ambusher.Strength}  DEF: {ambusher.Defence}");
        terminal.WriteLine("");

        // Give player choice
        terminal.SetColor("yellow");
        terminal.WriteLine("What do you do?");
        terminal.SetColor("white");
        terminal.WriteLine(" [F]ight - Engage the ambusher");
        terminal.WriteLine(" [R]un  - Attempt to flee (may fail)");
        terminal.WriteLine("");

        var choice = await terminal.GetInputAsync("Your choice: ");

        if (choice.ToUpper() == "R")
        {
            // Attempt to flee - 50% base chance, modified by agility
            int fleeChance = 50 + (int)((currentPlayer.Agility - ambusher.Agility) / 2);
            fleeChance = Math.Clamp(fleeChance, 20, 80);

            if (new Random().Next(100) < fleeChance)
            {
                terminal.WriteLine("");
                terminal.SetColor("green");
                terminal.WriteLine("You manage to escape into the crowd!");
                await Task.Delay(1500);
                return;
            }
            else
            {
                terminal.WriteLine("");
                terminal.SetColor("red");
                terminal.WriteLine("You couldn't escape! They block your path!");
                await Task.Delay(1500);
            }
        }

        // Combat!
        terminal.WriteLine("");
        terminal.SetColor("bright_red");
        terminal.WriteLine("COMBAT BEGINS!");
        await Task.Delay(1000);

        // Use the combat engine to fight the NPC
        var combatEngine = new CombatEngine(terminal);
        var result = await combatEngine.PlayerVsPlayer(currentPlayer, ambusher);

        // Handle combat result
        if (result.Outcome == CombatOutcome.Victory)
        {
            terminal.WriteLine("");
            terminal.SetColor("bright_green");
            terminal.WriteLine($"You defeated {ambusher.Name}!");

            // Mark NPC as permanently dead and queue for respawn
            ambusher.IsDead = true;
            ambusher.HP = 0;
            WorldSimulator.Instance?.QueueNPCForRespawn(ambusher.Id);

            // Display rewards summary (already calculated and given by combat engine)
            terminal.WriteLine("");
            if (result.ExperienceGained > 0 || result.GoldGained > 0)
            {
                terminal.SetColor("bright_yellow");
                terminal.WriteLine("═══ REWARDS ═══");
                terminal.SetColor("yellow");
                if (result.ExperienceGained > 0)
                    terminal.WriteLine($"  Experience: +{result.ExperienceGained:N0}");
                if (result.GoldGained > 0)
                    terminal.WriteLine($"  Gold: +{result.GoldGained:N0}");
            }

            // Display any looted items
            if (result.ItemsFound != null && result.ItemsFound.Count > 0)
            {
                terminal.SetColor("bright_cyan");
                terminal.WriteLine("  Loot:");
                foreach (var item in result.ItemsFound)
                {
                    terminal.WriteLine($"    • {item}");
                }
            }

            // Killing faction NPCs affects standing
            terminal.WriteLine("");
            if (ambusher.NPCFaction.HasValue)
            {
                factionSystem.ModifyReputation(ambusher.NPCFaction.Value, -50);
                terminal.SetColor("red");
                terminal.WriteLine($"  Your standing with {UsurperRemake.Systems.FactionSystem.Factions[ambusher.NPCFaction.Value].Name} has decreased! (-50)");

                // Gain standing with rival factions
                foreach (var faction in UsurperRemake.Systems.FactionSystem.Factions.Keys
                    .Where(f => f != ambusher.NPCFaction.Value))
                {
                    // Only gain with true rivals
                    if ((ambusher.NPCFaction == UsurperRemake.Systems.Faction.TheFaith &&
                         faction == UsurperRemake.Systems.Faction.TheShadows) ||
                        (ambusher.NPCFaction == UsurperRemake.Systems.Faction.TheShadows &&
                         faction == UsurperRemake.Systems.Faction.TheFaith))
                    {
                        factionSystem.ModifyReputation(faction, 10);
                        terminal.SetColor("cyan");
                        terminal.WriteLine($"  {UsurperRemake.Systems.FactionSystem.Factions[faction].Name} approves! (+10 standing)");
                    }
                }
            }

            // Log the event
            UsurperRemake.Systems.DebugLogger.Instance.LogInfo("FACTION",
                $"{currentPlayer.Name2} killed {ambusher.Name} ({ambusher.NPCFaction}) in faction ambush - XP:{result.ExperienceGained}, Gold:{result.GoldGained}");
        }
        else if (result.Outcome == CombatOutcome.PlayerEscaped)
        {
            terminal.WriteLine("");
            terminal.SetColor("yellow");
            terminal.WriteLine("You managed to disengage from combat!");
        }
        // If player lost, the death handling is done by the combat engine

        await terminal.PressAnyKey();
    }
    
    /// <summary>
    /// Show the inventory screen for managing equipment
    /// </summary>
    protected virtual async Task ShowInventory()
    {
        var inventorySystem = new InventorySystem(terminal, currentPlayer);
        await inventorySystem.ShowInventory();
    }

    /// <summary>
    /// Show quick preferences menu (accessible from any location via ~)
    /// </summary>
    protected virtual async Task ShowPreferencesMenu()
    {
        bool exitPrefs = false;

        while (!exitPrefs)
        {
            terminal.ClearScreen();
            terminal.SetColor("bright_yellow");
            terminal.WriteLine("╔══════════════════════════════════════════════════════════════════════════════╗");
            terminal.WriteLine("║                           QUICK PREFERENCES                                  ║");
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

            // Screen reader mode
            terminal.WriteLine($"  Screen Reader Mode: {(currentPlayer.ScreenReaderMode ? "Enabled (Simplified Text)" : "Disabled")}", "yellow");

            // Telemetry
            terminal.WriteLine($"  Alpha Telemetry: {(UsurperRemake.Systems.TelemetrySystem.Instance.IsEnabled ? "Enabled (Sending Anonymous Stats)" : "Disabled")}", "yellow");
            terminal.WriteLine("");

            terminal.SetColor("white");
            terminal.WriteLine("Options:");
            terminal.WriteLine("[1] Toggle Combat Speed (Normal -> Fast -> Instant)");
            terminal.WriteLine("[2] Toggle Auto-heal in Battle");
            terminal.WriteLine("[3] Toggle Skip Intimate Scenes");
            terminal.WriteLine("[4] Toggle Screen Reader Mode (Accessibility)");
            terminal.WriteLine("[5] Toggle Alpha Telemetry (Anonymous Stats)");
            terminal.WriteLine("[0] Back");
            terminal.WriteLine("");

            var choice = await terminal.GetInput("Choice: ");

            switch (choice.Trim())
            {
                case "1":
                    // Cycle through combat speeds
                    currentPlayer.CombatSpeed = currentPlayer.CombatSpeed switch
                    {
                        CombatSpeed.Normal => CombatSpeed.Fast,
                        CombatSpeed.Fast => CombatSpeed.Instant,
                        _ => CombatSpeed.Normal
                    };
                    string newSpeed = currentPlayer.CombatSpeed switch
                    {
                        CombatSpeed.Instant => "Instant",
                        CombatSpeed.Fast => "Fast",
                        _ => "Normal"
                    };
                    terminal.WriteLine($"Combat speed set to: {newSpeed}", "green");
                    await GameEngine.Instance.SaveCurrentGame();
                    await Task.Delay(800);
                    break;

                case "2":
                    currentPlayer.AutoHeal = !currentPlayer.AutoHeal;
                    terminal.WriteLine($"Auto-heal is now {(currentPlayer.AutoHeal ? "ENABLED" : "DISABLED")}", "green");
                    await GameEngine.Instance.SaveCurrentGame();
                    await Task.Delay(800);
                    break;

                case "3":
                    currentPlayer.SkipIntimateScenes = !currentPlayer.SkipIntimateScenes;
                    if (currentPlayer.SkipIntimateScenes)
                    {
                        terminal.WriteLine("Intimate scenes will now 'fade to black'", "green");
                    }
                    else
                    {
                        terminal.WriteLine("Intimate scenes will now show full content", "green");
                    }
                    await GameEngine.Instance.SaveCurrentGame();
                    await Task.Delay(1000);
                    break;

                case "4":
                    currentPlayer.ScreenReaderMode = !currentPlayer.ScreenReaderMode;
                    if (currentPlayer.ScreenReaderMode)
                    {
                        terminal.WriteLine("Screen Reader Mode ENABLED", "green");
                        terminal.WriteLine("Menus will use simplified plain text format.", "white");
                    }
                    else
                    {
                        terminal.WriteLine("Screen Reader Mode DISABLED", "green");
                        terminal.WriteLine("Menus will use visual ASCII art format.", "white");
                    }
                    await GameEngine.Instance.SaveCurrentGame();
                    await Task.Delay(1200);
                    break;

                case "5":
                    if (UsurperRemake.Systems.TelemetrySystem.Instance.IsEnabled)
                    {
                        UsurperRemake.Systems.TelemetrySystem.Instance.Disable();
                        terminal.WriteLine("Alpha Telemetry DISABLED", "green");
                        terminal.WriteLine("We will no longer collect anonymous gameplay statistics.", "white");
                    }
                    else
                    {
                        UsurperRemake.Systems.TelemetrySystem.Instance.Enable();
                        terminal.WriteLine("Alpha Telemetry ENABLED", "green");
                        terminal.WriteLine("Thank you for helping us improve the game!", "white");
                        // Track session start when enabling
                        UsurperRemake.Systems.TelemetrySystem.Instance.TrackSessionStart(
                            GameConfig.Version,
                            System.Environment.OSVersion.Platform.ToString()
                        );
                    }
                    await GameEngine.Instance.SaveCurrentGame();
                    await Task.Delay(1200);
                    break;

                case "0":
                case "":
                    exitPrefs = true;
                    break;

                default:
                    terminal.WriteLine("Invalid choice.", "red");
                    await Task.Delay(500);
                    break;
            }
        }
    }

    /// <summary>
    /// Talk to an NPC at the current location
    /// </summary>
    protected virtual async Task TalkToNPC()
    {
        var npcsHere = GetLiveNPCsAtLocation();

        // Also include any static LocationNPCs, but exclude special NPCs that have
        // their own dedicated interaction paths (e.g., Seth Able has [F] Challenge)
        var allNPCs = new List<NPC>(LocationNPCs.Where(n => !n.IsSpecialNPC));
        foreach (var npc in npcsHere)
        {
            if (!npc.IsSpecialNPC && !allNPCs.Any(n => n.Name2 == npc.Name2))
                allNPCs.Add(npc);
        }

        if (allNPCs.Count == 0)
        {
            terminal.SetColor("gray");
            terminal.WriteLine("There's no one here to talk to.");
            await Task.Delay(1500);
            return;
        }

        terminal.ClearScreen();
        terminal.SetColor("bright_cyan");
        terminal.WriteLine("╔══════════════════════════════════════════════════════════════════════════════╗");
        terminal.WriteLine("║                            PEOPLE NEARBY                                     ║");
        terminal.WriteLine("╚══════════════════════════════════════════════════════════════════════════════╝");
        terminal.WriteLine("");

        terminal.SetColor("yellow");
        terminal.WriteLine("  Who would you like to talk to?");
        terminal.WriteLine("");

        // List NPCs with numbers
        for (int i = 0; i < Math.Min(allNPCs.Count, 10); i++)
        {
            var npc = allNPCs[i];

            // Get relationship status with player
            int relationLevel = RelationshipSystem.GetRelationshipStatus(currentPlayer, npc);
            var (relationColor, relationText, relationSymbol) = GetRelationshipDisplayInfo(relationLevel);

            terminal.SetColor("cyan");
            terminal.Write($"  [{i + 1}] ");

            // Name color based on relationship rather than alignment
            terminal.SetColor(relationColor);
            terminal.Write($"{npc.Name2}");

            // Show class/level
            terminal.SetColor("gray");
            terminal.Write($" - Level {npc.Level} {npc.Class}");

            // Show relationship status in brackets with color
            terminal.Write(" [");
            terminal.SetColor(relationColor);
            terminal.Write(relationText);
            if (!string.IsNullOrEmpty(relationSymbol))
            {
                terminal.SetColor("bright_red");
                terminal.Write($" {relationSymbol}");
            }
            terminal.SetColor("gray");
            terminal.WriteLine("]");
        }

        if (allNPCs.Count > 10)
        {
            terminal.SetColor("gray");
            terminal.WriteLine($"  ... and {allNPCs.Count - 10} others");
        }

        terminal.WriteLine("");
        terminal.SetColor("gray");
        terminal.WriteLine("  [0] Never mind");
        terminal.WriteLine("");

        string choice = await terminal.GetInput("Talk to who? ");

        if (int.TryParse(choice, out int targetIndex) && targetIndex >= 1 && targetIndex <= Math.Min(allNPCs.Count, 10))
        {
            var npc = allNPCs[targetIndex - 1];
            await InteractWithNPC(npc);
        }
        else if (choice != "0")
        {
            terminal.SetColor("gray");
            terminal.WriteLine("You decide not to talk to anyone.");
            await Task.Delay(1000);
        }
    }

    /// <summary>
    /// Have a conversation with an NPC
    /// </summary>
    protected virtual async Task InteractWithNPC(NPC npc)
    {
        bool stayInConversation = true;
        bool isFirstGreeting = true;

        while (stayInConversation)
        {
            terminal.ClearScreen();
            terminal.SetColor("bright_cyan");
            terminal.WriteLine("╔══════════════════════════════════════════════════════════════════════════════╗");
            terminal.SetColor("bright_yellow");
            terminal.WriteLine($"║  Talking to: {npc.Name2,-60}  ║");
            terminal.SetColor("bright_cyan");
            terminal.WriteLine("╚══════════════════════════════════════════════════════════════════════════════╝");
            terminal.WriteLine("");

            // Show NPC info
            terminal.SetColor("gray");
            string sexDisplay = npc.Sex == CharacterSex.Female ? "Female" : "Male";
            terminal.WriteLine($"  Level {npc.Level} {npc.Race} {sexDisplay} {npc.Class}");
            terminal.WriteLine($"  {GetAlignmentDisplay(npc)}");
            terminal.WriteLine("");

            // Get NPC's greeting (only on first interaction)
            if (isFirstGreeting)
            {
                string greeting = npc.GetGreeting(currentPlayer);
                terminal.SetColor("yellow");
                terminal.WriteLine($"  {npc.Name2} says:");
                terminal.SetColor("white");
                terminal.WriteLine($"  \"{greeting}\"");
                terminal.WriteLine("");
                isFirstGreeting = false;
            }

            // Show interaction options
            terminal.SetColor("cyan");
            terminal.WriteLine("  What do you want to do?");
            terminal.WriteLine("");

            terminal.SetColor("darkgray");
            terminal.Write("  [");
            terminal.SetColor("bright_green");
            terminal.Write("1");
            terminal.SetColor("darkgray");
            terminal.Write("]");
            terminal.SetColor("white");
            terminal.WriteLine(" Chat with them");

            terminal.SetColor("darkgray");
            terminal.Write("  [");
            terminal.SetColor("bright_yellow");
            terminal.Write("2");
            terminal.SetColor("darkgray");
            terminal.Write("]");
            terminal.SetColor("white");
            terminal.WriteLine(" Ask about rumors");

            terminal.SetColor("darkgray");
            terminal.Write("  [");
            terminal.SetColor("bright_cyan");
            terminal.Write("3");
            terminal.SetColor("darkgray");
            terminal.Write("]");
            terminal.SetColor("white");
            terminal.WriteLine(" Ask about the dungeons");

            // Only show challenge option if they're a fighter type
            if (npc.Level > 0 && npc.IsAlive)
            {
                terminal.SetColor("darkgray");
                terminal.Write("  [");
                terminal.SetColor("bright_red");
                terminal.Write("4");
                terminal.SetColor("darkgray");
                terminal.Write("]");
                terminal.SetColor("white");
                terminal.WriteLine(" Challenge to a duel");
            }

            // Full conversation option (visual novel style)
            terminal.SetColor("darkgray");
            terminal.Write("  [");
            terminal.SetColor("bright_magenta");
            terminal.Write("5");
            terminal.SetColor("darkgray");
            terminal.Write("]");
            terminal.SetColor("bright_magenta");
            terminal.WriteLine(" Have a deep conversation...");

            terminal.WriteLine("");
            terminal.SetColor("gray");
            terminal.WriteLine("  [0] Walk away");

            // Debug option
            terminal.SetColor("dark_gray");
            terminal.WriteLine("  [9] (DEBUG) View personality traits");
            terminal.WriteLine("");

            string action = await terminal.GetInput("Your choice: ");

            switch (action)
            {
                case "1":
                    await ChatWithNPC(npc);
                    break;
                case "2":
                    await AskForRumors(npc);
                    break;
                case "3":
                    await AskAboutDungeons(npc);
                    break;
                case "4":
                    if (npc.Level > 0 && npc.IsAlive)
                    {
                        await ChallengeNPC(npc);
                        stayInConversation = false; // Exit after combat
                    }
                    break;
                case "5":
                    // Full visual novel style conversation
                    await UsurperRemake.Systems.VisualNovelDialogueSystem.Instance.StartConversation(currentPlayer, npc, terminal);
                    break;
                case "9":
                    await ShowNPCDebugTraits(npc);
                    break;
                case "0":
                default:
                    // Show NPC's farewell using dynamic dialogue system
                    string farewell = npc.GetFarewell(currentPlayer as Player);
                    terminal.SetColor("yellow");
                    terminal.WriteLine($"  {npc.Name2} says:");
                    terminal.SetColor("white");
                    terminal.WriteLine($"  \"{farewell}\"");
                    terminal.WriteLine("");
                    terminal.SetColor("gray");
                    terminal.WriteLine($"  You nod and walk away.");
                    await Task.Delay(1500);
                    stayInConversation = false;
                    break;
            }
        }
    }

    /// <summary>
    /// Have a casual chat with an NPC
    /// Uses the Dynamic NPC Dialogue System for personality-driven conversation
    /// </summary>
    private async Task ChatWithNPC(NPC npc)
    {
        terminal.WriteLine("");

        // Use the dynamic dialogue system for small talk
        string smallTalk = npc.GetSmallTalk(currentPlayer as Player);

        terminal.SetColor("yellow");
        terminal.WriteLine($"  {npc.Name2} says:");
        terminal.SetColor("white");
        terminal.WriteLine($"  \"{smallTalk}\"");
        await Task.Delay(800);

        // Sometimes add a second line of dialogue for variety
        if (new Random().NextDouble() < 0.5)
        {
            await Task.Delay(600);
            string moreTalk = npc.GetSmallTalk(currentPlayer as Player);
            if (moreTalk != smallTalk) // Avoid repetition
            {
                terminal.WriteLine($"  \"{moreTalk}\"");
                await Task.Delay(600);
            }
        }

        // Small relationship boost for friendly chat
        RelationshipSystem.UpdateRelationship(currentPlayer, npc, 1, 1, false, false);

        terminal.WriteLine("");
        await terminal.PressAnyKey();
    }

    /// <summary>
    /// Generate contextual chat lines based on NPC personality
    /// </summary>
    private string[] GenerateNPCChat(NPC npc)
    {
        var random = new Random();
        var chatOptions = new List<string[]>();

        // Class-specific chat
        switch (npc.Class)
        {
            case CharacterClass.Warrior:
                chatOptions.Add(new[] { "These dungeons test even the mightiest warriors.", "Keep your blade sharp and your wits sharper." });
                chatOptions.Add(new[] { "I've seen too many cocky fighters fall to overconfidence.", "Respect the dungeon, and it might let you live." });
                break;
            case CharacterClass.Magician:
                chatOptions.Add(new[] { "The arcane energies here are... unsettling.", "Something ancient stirs in the depths." });
                chatOptions.Add(new[] { "Magic is a tool, not a crutch.", "The wise mage knows when NOT to cast." });
                break;
            case CharacterClass.Cleric:
                chatOptions.Add(new[] { "May the gods watch over your journey.", "Even in darkness, faith is a light." });
                chatOptions.Add(new[] { "The temple offers healing, but true strength comes from within.", "Pray, but keep your mace ready." });
                break;
            case CharacterClass.Assassin:
                chatOptions.Add(new[] { "*glances around nervously*", "Keep your voice down. Walls have ears." });
                chatOptions.Add(new[] { "The shadows hold many secrets.", "Sometimes the unseen blade is the deadliest." });
                break;
            default:
                chatOptions.Add(new[] { "Times are strange in these parts.", "Stay safe out there, friend." });
                chatOptions.Add(new[] { "Have you heard about the dungeons?", "They say great treasure lies below... and great danger." });
                break;
        }

        // Alignment-specific additions
        if (npc.Darkness > npc.Chivalry + 500)
        {
            chatOptions.Add(new[] { "*smirks darkly*", "Power comes to those who take it.", "The weak exist to serve the strong." });
        }
        else if (npc.Chivalry > npc.Darkness + 500)
        {
            chatOptions.Add(new[] { "Honor and courage guide my path.", "We must protect those who cannot protect themselves." });
        }

        return chatOptions[random.Next(chatOptions.Count)];
    }

    /// <summary>
    /// Ask NPC for rumors
    /// </summary>
    private async Task AskForRumors(NPC npc)
    {
        terminal.WriteLine("");
        terminal.SetColor("cyan");
        terminal.WriteLine($"  You ask {npc.Name2} if they've heard any interesting rumors...");
        terminal.WriteLine("");

        var random = new Random();
        var rumors = GetRumors();
        var selectedRumor = rumors[random.Next(rumors.Length)];

        terminal.SetColor("yellow");
        terminal.WriteLine($"  {npc.Name2} leans in and whispers:");
        terminal.SetColor("white");
        terminal.WriteLine($"  \"{selectedRumor}\"");

        terminal.WriteLine("");
        await terminal.PressAnyKey();
    }

    /// <summary>
    /// Get list of rumors NPCs can share
    /// </summary>
    private string[] GetRumors()
    {
        return new[]
        {
            "They say seven ancient seals lie hidden in the dungeon depths...",
            "The old gods stir in their prisons. Dark times are coming.",
            "A mysterious stranger has been seen around town. Trust no one.",
            "The king grows paranoid. He sees enemies everywhere.",
            "Deep in the dungeon, there's a creature that guards untold riches.",
            "Some adventurers never return from the lower levels. Choose your depth wisely.",
            "I've heard whispers of a hidden shop in the Dark Alley...",
            "The temple priests know more than they let on about the old gods.",
            "There's talk of a secret passage in the castle. But I've said too much.",
            "A wave returns to the ocean... whatever that means. Cryptic nonsense if you ask me.",
            "They say Manwe himself cursed these dungeons. But that's just a story... right?",
            "The healers can cure almost anything, for a price.",
            "Team up with others if you want to survive the deeper levels.",
            "I've heard there's a way to save Veloura... if you have the courage to try.",
            "Some NPCs carry powerful items. Defeat them in a duel and claim their gear!"
        };
    }

    /// <summary>
    /// Ask NPC about dungeons
    /// </summary>
    private async Task AskAboutDungeons(NPC npc)
    {
        terminal.WriteLine("");
        terminal.SetColor("cyan");
        terminal.WriteLine($"  You ask {npc.Name2} about the dungeons...");
        terminal.WriteLine("");

        // Give advice based on NPC's level/experience
        terminal.SetColor("yellow");
        terminal.WriteLine($"  {npc.Name2} says:");
        terminal.SetColor("white");

        if (npc.Level > currentPlayer.Level + 10)
        {
            terminal.WriteLine($"  \"You're brave to ask, but you're not ready for the depths I've seen.\"");
            terminal.WriteLine($"  \"Get stronger first. Level {currentPlayer.Level} won't cut it down there.\"");
        }
        else if (npc.Level > currentPlayer.Level)
        {
            terminal.WriteLine($"  \"The upper levels should be manageable for you.\"");
            terminal.WriteLine($"  \"Watch out for floor {Math.Min(npc.Level, 10)} though - things get nasty there.\"");
        }
        else
        {
            terminal.WriteLine($"  \"Ha! You asking ME for dungeon advice?\"");
            terminal.WriteLine($"  \"You look more experienced than I am. Good luck down there.\"");
        }

        terminal.WriteLine("");
        await terminal.PressAnyKey();
    }

    /// <summary>
    /// DEBUG: Show NPC personality and relationship traits
    /// </summary>
    private async Task ShowNPCDebugTraits(NPC npc)
    {
        terminal.ClearScreen();
        terminal.SetColor("bright_magenta");
        terminal.WriteLine("╔══════════════════════════════════════════════════════════════════════════════╗");
        terminal.WriteLine($"║  DEBUG: {npc.Name2,-64}  ║");
        terminal.WriteLine("╚══════════════════════════════════════════════════════════════════════════════╝");
        terminal.WriteLine("");

        var profile = npc.Personality;
        if (profile == null)
        {
            terminal.SetColor("red");
            terminal.WriteLine("  No personality profile found!");
            await terminal.PressAnyKey();
            return;
        }

        // Basic identity
        terminal.SetColor("bright_cyan");
        terminal.WriteLine("  === IDENTITY ===");
        terminal.SetColor("white");
        terminal.WriteLine($"  Gender Identity:    {profile.Gender}");
        terminal.WriteLine($"  Sexual Orientation: {profile.Orientation}");
        terminal.WriteLine("");

        // Faction affiliation
        terminal.SetColor("bright_yellow");
        terminal.WriteLine("  === FACTION ===");
        terminal.SetColor("white");
        terminal.Write("  Faction:            ");
        if (npc.NPCFaction.HasValue)
        {
            var factionColor = npc.NPCFaction.Value switch
            {
                UsurperRemake.Systems.Faction.TheCrown => "bright_yellow",
                UsurperRemake.Systems.Faction.TheFaith => "bright_cyan",
                UsurperRemake.Systems.Faction.TheShadows => "bright_magenta",
                _ => "white"
            };
            terminal.SetColor(factionColor);
            terminal.WriteLine(npc.NPCFaction.Value.ToString());
        }
        else
        {
            terminal.SetColor("gray");
            terminal.WriteLine("None (Independent)");
        }
        terminal.WriteLine("");

        // Relationship preferences
        terminal.SetColor("bright_yellow");
        terminal.WriteLine("  === RELATIONSHIP STYLE ===");
        terminal.SetColor("white");
        terminal.WriteLine($"  Relationship Pref:  {profile.RelationshipPref}");
        terminal.WriteLine($"  Intimate Style:     {profile.IntimateStyle}");
        terminal.WriteLine("");

        // Romance traits
        terminal.SetColor("bright_green");
        terminal.WriteLine("  === ROMANCE TRAITS (0.0-1.0) ===");
        terminal.SetColor("white");

        // Color code based on value - using inline method
        PrintTraitLine("Romanticism:", profile.Romanticism, "(romantic vs practical)");
        PrintTraitLine("Sensuality:", profile.Sensuality, "(physical desire)");
        PrintTraitLine("Passion:", profile.Passion, "(intensity)");
        PrintTraitLine("Flirtatiousness:", profile.Flirtatiousness, "(likely to flirt)");
        PrintTraitLine("Commitment:", profile.Commitment, "(marriage-minded)");
        PrintTraitLine("Tenderness:", profile.Tenderness, "(gentle vs rough)");
        PrintTraitLine("Jealousy:", profile.Jealousy, "(possessiveness)");
        terminal.WriteLine("");

        // Polyamory assessment
        terminal.SetColor("bright_magenta");
        terminal.WriteLine("  === POLYAMORY ASSESSMENT ===");

        bool openToPolyamory = profile.RelationshipPref == RelationshipPreference.OpenRelationship ||
                               profile.RelationshipPref == RelationshipPreference.Polyamorous;
        bool lowJealousy = profile.Jealousy < 0.4f;
        bool lowCommitment = profile.Commitment < 0.5f;

        terminal.SetColor("white");
        terminal.Write("  Open to polyamory: ");
        if (openToPolyamory && lowJealousy)
        {
            terminal.SetColor("bright_green");
            terminal.WriteLine("VERY LIKELY");
        }
        else if (openToPolyamory || (lowJealousy && lowCommitment))
        {
            terminal.SetColor("yellow");
            terminal.WriteLine("POSSIBLE");
        }
        else
        {
            terminal.SetColor("red");
            terminal.WriteLine("UNLIKELY");
        }

        terminal.WriteLine("");
        terminal.SetColor("dark_gray");
        terminal.WriteLine("  Key factors: OpenRelationship/Polyamorous preference + Low Jealousy");
        terminal.WriteLine("");

        await terminal.PressAnyKey();
        await InteractWithNPC(npc); // Return to interaction menu
    }

    /// <summary>
    /// Helper to print a trait line with color coding based on value
    /// </summary>
    private void PrintTraitLine(string name, float value, string description)
    {
        string color = value >= 0.7f ? "bright_green" : value >= 0.4f ? "yellow" : "gray";
        terminal.SetColor(color);
        terminal.Write($"  {name,-18} ");
        terminal.SetColor("white");
        terminal.Write($"{value:F2}");
        terminal.SetColor("dark_gray");
        terminal.WriteLine($"  {description}");
    }
    /// <summary>
    /// Challenge NPC to a duel
    /// </summary>
    private async Task ChallengeNPC(NPC npc)
    {
        terminal.WriteLine("");
        terminal.SetColor("bright_red");
        terminal.WriteLine($"  You challenge {npc.Name2} to a duel!");
        terminal.WriteLine("");

        // Check if NPC accepts
        bool accepts = ShouldNPCAcceptDuel(npc);

        if (!accepts)
        {
            terminal.SetColor("yellow");
            terminal.WriteLine($"  {npc.Name2} says:");
            terminal.SetColor("white");

            if (npc.Level > currentPlayer.Level + 5)
            {
                terminal.WriteLine($"  \"You're not worth my time, weakling. Come back when you're stronger.\"");
            }
            else if (npc.Level < currentPlayer.Level - 5)
            {
                terminal.WriteLine($"  \"I know when I'm outmatched. Maybe another time.\"");
            }
            else
            {
                terminal.WriteLine($"  \"Not today, friend. I've got other things to do.\"");
            }

            await Task.Delay(2000);
            return;
        }

        terminal.SetColor("yellow");
        terminal.WriteLine($"  {npc.Name2} accepts your challenge!");
        terminal.WriteLine("");

        terminal.SetColor("white");
        terminal.WriteLine("  \"Let's settle this honorably!\"");
        terminal.WriteLine("");

        await Task.Delay(1500);

        // Initiate combat through StreetEncounterSystem
        var result = await StreetEncounterSystem.Instance.AttackCharacter(currentPlayer, npc, terminal);

        if (result.Victory)
        {
            terminal.SetColor("bright_green");
            terminal.WriteLine($"\n  You defeated {npc.Name2} in honorable combat!");
            currentPlayer.PKills++;

            // Small reputation boost for honorable duel
            currentPlayer.Chivalry += 5;
        }
        else
        {
            terminal.SetColor("red");
            terminal.WriteLine($"\n  {npc.Name2} got the better of you...");
            currentPlayer.PDefeats++;
        }

        await Task.Delay(2000);
    }

    /// <summary>
    /// Determine if NPC should accept a duel challenge
    /// </summary>
    private bool ShouldNPCAcceptDuel(NPC npc)
    {
        var random = new Random();

        // Level difference affects acceptance
        int levelDiff = npc.Level - currentPlayer.Level;

        // Very high level NPCs don't bother with weak players
        if (levelDiff > 10) return random.Next(100) < 10;  // 10% chance

        // Very low level NPCs are scared
        if (levelDiff < -10) return random.Next(100) < 20; // 20% chance

        // Similar level - personality matters
        if (npc.Darkness > npc.Chivalry)
        {
            return random.Next(100) < 70; // Evil NPCs like fights
        }
        else if (npc.Chivalry > npc.Darkness + 500)
        {
            return random.Next(100) < 40; // Honorable NPCs prefer peace
        }

        return random.Next(100) < 50; // 50-50 otherwise
    }

    /// <summary>
    /// Show player status - Comprehensive character information display
    /// </summary>
    protected virtual async Task ShowStatus()
    {
        terminal.ClearScreen();

        // Header
        terminal.SetColor("bright_cyan");
        terminal.WriteLine("╔══════════════════════════════════════════════════════════════════════════════╗");
        terminal.WriteLine("║                           CHARACTER STATUS                                  ║");
        terminal.WriteLine("╚══════════════════════════════════════════════════════════════════════════════╝");
        terminal.WriteLine("");

        // Basic Info
        terminal.SetColor("yellow");
        terminal.WriteLine("═══ BASIC INFORMATION ═══");
        terminal.SetColor("white");
        terminal.Write("Name: ");
        terminal.SetColor("bright_white");
        terminal.WriteLine(currentPlayer.DisplayName);

        terminal.SetColor("white");
        terminal.Write("Class: ");
        terminal.SetColor("bright_green");
        terminal.Write($"{currentPlayer.Class}");
        terminal.SetColor("white");
        terminal.Write("  |  Race: ");
        terminal.SetColor("bright_green");
        terminal.Write($"{currentPlayer.Race}");
        terminal.SetColor("white");
        terminal.Write("  |  Sex: ");
        terminal.SetColor("bright_green");
        terminal.WriteLine($"{(currentPlayer.Sex == CharacterSex.Male ? "Male" : "Female")}");

        terminal.SetColor("white");
        terminal.Write("Age: ");
        terminal.SetColor("cyan");
        terminal.Write($"{currentPlayer.Age}");
        terminal.SetColor("white");
        terminal.Write("  |  Height: ");
        terminal.SetColor("cyan");
        terminal.Write($"{currentPlayer.Height}cm");
        terminal.SetColor("white");
        terminal.Write("  |  Weight: ");
        terminal.SetColor("cyan");
        terminal.WriteLine($"{currentPlayer.Weight}kg");
        terminal.WriteLine("");

        // Level & Experience
        terminal.SetColor("yellow");
        terminal.WriteLine("═══ LEVEL & EXPERIENCE ═══");
        terminal.SetColor("white");
        terminal.Write("Current Level: ");
        terminal.SetColor("bright_yellow");
        terminal.WriteLine($"{currentPlayer.Level}");

        terminal.SetColor("white");
        terminal.Write("Experience: ");
        terminal.SetColor("bright_cyan");
        terminal.WriteLine($"{currentPlayer.Experience:N0}");

        // Calculate XP needed for next level
        long nextLevelXP = GetExperienceForLevel(currentPlayer.Level + 1);
        long xpNeeded = nextLevelXP - currentPlayer.Experience;

        terminal.SetColor("white");
        terminal.Write("XP to Next Level: ");
        terminal.SetColor("bright_magenta");
        terminal.Write($"{xpNeeded:N0}");
        terminal.SetColor("gray");
        terminal.WriteLine($" (Need {nextLevelXP:N0} total)");
        terminal.WriteLine("");

        // Combat Stats
        terminal.SetColor("yellow");
        terminal.WriteLine("═══ COMBAT STATISTICS ═══");
        terminal.SetColor("white");
        terminal.Write("HP: ");
        terminal.SetColor("bright_red");
        terminal.Write($"{currentPlayer.HP}");
        terminal.SetColor("white");
        terminal.Write("/");
        terminal.SetColor("red");
        terminal.WriteLine($"{currentPlayer.MaxHP}");

        if (currentPlayer.MaxMana > 0)
        {
            terminal.SetColor("white");
            terminal.Write("Mana: ");
            terminal.SetColor("bright_blue");
            terminal.Write($"{currentPlayer.Mana}");
            terminal.SetColor("white");
            terminal.Write("/");
            terminal.SetColor("blue");
            terminal.WriteLine($"{currentPlayer.MaxMana}");
        }

        terminal.SetColor("white");
        terminal.Write("Strength: ");
        terminal.SetColor("bright_green");
        terminal.Write($"{currentPlayer.Strength}");
        terminal.SetColor("white");
        terminal.Write("  |  Defence: ");
        terminal.SetColor("bright_green");
        terminal.Write($"{currentPlayer.Defence}");
        terminal.SetColor("white");
        terminal.Write("  |  Agility: ");
        terminal.SetColor("bright_green");
        terminal.WriteLine($"{currentPlayer.Agility}");

        terminal.SetColor("white");
        terminal.Write("Dexterity: ");
        terminal.SetColor("cyan");
        terminal.Write($"{currentPlayer.Dexterity}");
        terminal.SetColor("white");
        terminal.Write("  |  Stamina: ");
        terminal.SetColor("cyan");
        terminal.Write($"{currentPlayer.Stamina}");
        terminal.SetColor("white");
        terminal.Write("  |  Wisdom: ");
        terminal.SetColor("cyan");
        terminal.WriteLine($"{currentPlayer.Wisdom}");

        terminal.SetColor("white");
        terminal.Write("Intelligence: ");
        terminal.SetColor("cyan");
        terminal.Write($"{currentPlayer.Intelligence}");
        terminal.SetColor("white");
        terminal.Write("  |  Charisma: ");
        terminal.SetColor("cyan");
        terminal.Write($"{currentPlayer.Charisma}");
        terminal.SetColor("white");
        terminal.Write("  |  Constitution: ");
        terminal.SetColor("cyan");
        terminal.WriteLine($"{currentPlayer.Constitution}");
        terminal.WriteLine("");

        // Pagination - Page 1 break
        terminal.SetColor("gray");
        terminal.Write("Press Enter to continue...");
        await terminal.GetInput("");
        terminal.WriteLine("");

        // Equipment - Full Slot Display
        terminal.SetColor("yellow");
        terminal.WriteLine("═══ EQUIPMENT ═══");

        // Combat style indicator
        terminal.SetColor("white");
        terminal.Write("Combat Style: ");
        if (currentPlayer.IsTwoHanding)
        {
            terminal.SetColor("bright_red");
            terminal.WriteLine("Two-Handed (+25% damage, -15% defense)");
        }
        else if (currentPlayer.IsDualWielding)
        {
            terminal.SetColor("bright_yellow");
            terminal.WriteLine("Dual-Wield (+1 attack, -10% defense)");
        }
        else if (currentPlayer.HasShieldEquipped)
        {
            terminal.SetColor("bright_cyan");
            terminal.WriteLine("Sword & Board (balanced, 20% block chance)");
        }
        else
        {
            terminal.SetColor("gray");
            terminal.WriteLine("One-Handed");
        }
        terminal.WriteLine("");

        // Weapons
        terminal.SetColor("bright_red");
        terminal.Write("Main Hand: ");
        DisplayEquipmentSlot(EquipmentSlot.MainHand);
        terminal.SetColor("bright_red");
        terminal.Write("Off Hand:  ");
        DisplayEquipmentSlot(EquipmentSlot.OffHand);
        terminal.WriteLine("");

        // Armor slots (in two columns)
        terminal.SetColor("bright_cyan");
        terminal.Write("Head:      ");
        DisplayEquipmentSlot(EquipmentSlot.Head);
        terminal.SetColor("bright_cyan");
        terminal.Write("Body:      ");
        DisplayEquipmentSlot(EquipmentSlot.Body);
        terminal.SetColor("bright_cyan");
        terminal.Write("Arms:      ");
        DisplayEquipmentSlot(EquipmentSlot.Arms);
        terminal.SetColor("bright_cyan");
        terminal.Write("Hands:     ");
        DisplayEquipmentSlot(EquipmentSlot.Hands);
        terminal.SetColor("bright_cyan");
        terminal.Write("Legs:      ");
        DisplayEquipmentSlot(EquipmentSlot.Legs);
        terminal.SetColor("bright_cyan");
        terminal.Write("Feet:      ");
        DisplayEquipmentSlot(EquipmentSlot.Feet);
        terminal.SetColor("bright_cyan");
        terminal.Write("Waist:     ");
        DisplayEquipmentSlot(EquipmentSlot.Waist);
        terminal.SetColor("bright_cyan");
        terminal.Write("Face:      ");
        DisplayEquipmentSlot(EquipmentSlot.Face);
        terminal.SetColor("bright_cyan");
        terminal.Write("Cloak:     ");
        DisplayEquipmentSlot(EquipmentSlot.Cloak);
        terminal.WriteLine("");

        // Accessories
        terminal.SetColor("bright_magenta");
        terminal.Write("Neck:      ");
        DisplayEquipmentSlot(EquipmentSlot.Neck);
        terminal.SetColor("bright_magenta");
        terminal.Write("Left Ring: ");
        DisplayEquipmentSlot(EquipmentSlot.LFinger);
        terminal.SetColor("bright_magenta");
        terminal.Write("Right Ring:");
        DisplayEquipmentSlot(EquipmentSlot.RFinger);
        terminal.WriteLine("");

        // Equipment totals
        DisplayEquipmentTotals();
        terminal.WriteLine("");

        // Show active buffs if any
        if (currentPlayer.MagicACBonus > 0 || currentPlayer.DamageAbsorptionPool > 0 ||
            currentPlayer.IsRaging || currentPlayer.SmiteChargesRemaining > 0)
        {
            terminal.SetColor("bright_magenta");
            terminal.WriteLine("Active Effects:");

            if (currentPlayer.MagicACBonus > 0)
            {
                terminal.SetColor("magenta");
                terminal.WriteLine($"  - Magic AC Bonus: +{currentPlayer.MagicACBonus}");
            }
            if (currentPlayer.DamageAbsorptionPool > 0)
            {
                terminal.SetColor("magenta");
                terminal.WriteLine($"  - Stoneskin: {currentPlayer.DamageAbsorptionPool} damage absorption");
            }
            if (currentPlayer.IsRaging)
            {
                terminal.SetColor("bright_red");
                terminal.WriteLine("  - RAGING! (+Strength, +HP, -AC)");
            }
            if (currentPlayer.SmiteChargesRemaining > 0)
            {
                terminal.SetColor("yellow");
                terminal.WriteLine($"  - Smite Evil: {currentPlayer.SmiteChargesRemaining} charges");
            }
            terminal.WriteLine("");
        }

        // Wealth
        terminal.SetColor("yellow");
        terminal.WriteLine("═══ WEALTH ═══");
        terminal.SetColor("white");
        terminal.Write("Gold on Hand: ");
        terminal.SetColor("bright_yellow");
        terminal.WriteLine($"{currentPlayer.Gold:N0}");

        terminal.SetColor("white");
        terminal.Write("Gold in Bank: ");
        terminal.SetColor("yellow");
        terminal.WriteLine($"{currentPlayer.BankGold:N0}");

        terminal.SetColor("white");
        terminal.Write("Total Wealth: ");
        terminal.SetColor("bright_yellow");
        terminal.WriteLine($"{(currentPlayer.Gold + currentPlayer.BankGold):N0}");
        terminal.WriteLine("");

        // Pagination - Page 2 break
        terminal.SetColor("gray");
        terminal.Write("Press Enter to continue...");
        await terminal.GetInput("");
        terminal.WriteLine("");

        // Relationships
        terminal.SetColor("yellow");
        terminal.WriteLine("═══ RELATIONSHIPS ═══");
        terminal.SetColor("white");
        terminal.Write("Marital Status: ");

        // Check both Character properties AND RomanceTracker for marriage status
        var romanceTracker = UsurperRemake.Systems.RomanceTracker.Instance;
        bool isMarried = currentPlayer.Married || currentPlayer.IsMarried || (romanceTracker?.IsMarried == true);

        if (isMarried)
        {
            terminal.SetColor("bright_magenta");
            terminal.Write("Married");

            // Get spouse name from RomanceTracker first, fall back to Character property
            string spouseName = "";
            if (romanceTracker?.IsMarried == true)
            {
                var spouse = romanceTracker.PrimarySpouse;
                if (spouse != null)
                {
                    var npc = UsurperRemake.Systems.NPCSpawnSystem.Instance?.ActiveNPCs?
                        .FirstOrDefault(n => n.ID == spouse.NPCId);
                    spouseName = npc?.Name ?? spouse.NPCName;
                }
            }
            if (string.IsNullOrEmpty(spouseName))
            {
                spouseName = currentPlayer.SpouseName;
            }

            if (!string.IsNullOrEmpty(spouseName))
            {
                terminal.SetColor("white");
                terminal.Write(" to ");
                terminal.SetColor("magenta");
                terminal.Write(spouseName);
            }
            terminal.WriteLine("");

            // Show all spouses if polygamous
            if (romanceTracker != null && romanceTracker.Spouses.Count > 1)
            {
                terminal.SetColor("gray");
                terminal.WriteLine($"  ({romanceTracker.Spouses.Count} spouses total)");
            }

            // Get children count from both systems
            int childCount = currentPlayer.Kids;
            var familyChildren = UsurperRemake.Systems.FamilySystem.Instance?.GetChildrenOf(currentPlayer);
            if (familyChildren != null && familyChildren.Count > childCount)
            {
                childCount = familyChildren.Count;
            }

            terminal.SetColor("white");
            terminal.Write("Children: ");
            terminal.SetColor("cyan");
            terminal.WriteLine($"{childCount}");

            if (currentPlayer.Pregnancy > 0)
            {
                terminal.SetColor("white");
                terminal.Write("Pregnancy: ");
                terminal.SetColor("bright_cyan");
                terminal.WriteLine($"{currentPlayer.Pregnancy} days");
            }
        }
        else if (romanceTracker?.CurrentLovers?.Count > 0)
        {
            terminal.SetColor("magenta");
            terminal.WriteLine($"In a relationship ({romanceTracker.CurrentLovers.Count} lover{(romanceTracker.CurrentLovers.Count > 1 ? "s" : "")})");
        }
        else
        {
            terminal.SetColor("gray");
            terminal.WriteLine("Single");
        }

        terminal.SetColor("white");
        terminal.Write("Team: ");
        if (!string.IsNullOrEmpty(currentPlayer.Team))
        {
            terminal.SetColor("bright_green");
            terminal.WriteLine(currentPlayer.Team);
        }
        else
        {
            terminal.SetColor("gray");
            terminal.WriteLine("None");
        }
        terminal.WriteLine("");

        // Alignment & Reputation
        terminal.SetColor("yellow");
        terminal.WriteLine("═══ ALIGNMENT & REPUTATION ═══");

        // Get alignment info from AlignmentSystem
        var (alignText, alignColor) = AlignmentSystem.Instance.GetAlignmentDisplay(currentPlayer);

        terminal.SetColor("white");
        terminal.Write("Alignment: ");
        terminal.SetColor(alignColor);
        terminal.WriteLine(alignText);

        terminal.SetColor("white");
        terminal.Write("Chivalry: ");
        terminal.SetColor("bright_green");
        terminal.Write($"{currentPlayer.Chivalry}/1000");
        terminal.SetColor("white");
        terminal.Write("  |  Darkness: ");
        terminal.SetColor("red");
        terminal.WriteLine($"{currentPlayer.Darkness}/1000");

        // Show alignment bar
        terminal.SetColor("gray");
        terminal.Write("  Holy ");
        terminal.SetColor("bright_green");
        int chivBars = (int)Math.Min(10, currentPlayer.Chivalry / 100);
        int darkBars = (int)Math.Min(10, currentPlayer.Darkness / 100);
        terminal.Write(new string('█', chivBars));
        terminal.SetColor("darkgray");
        terminal.Write(new string('░', 10 - chivBars));
        terminal.Write(" | ");
        terminal.SetColor("red");
        terminal.Write(new string('█', darkBars));
        terminal.SetColor("darkgray");
        terminal.Write(new string('░', 10 - darkBars));
        terminal.WriteLine(" Evil");

        // Show alignment abilities
        var abilities = AlignmentSystem.Instance.GetAlignmentAbilities(currentPlayer);
        if (abilities.Count > 0)
        {
            terminal.SetColor("cyan");
            terminal.WriteLine("  Alignment Abilities:");
            terminal.SetColor("white");
            foreach (var ability in abilities)
            {
                terminal.WriteLine($"    - {ability}");
            }
        }
        terminal.WriteLine("");

        terminal.SetColor("white");
        terminal.Write("Loyalty: ");
        terminal.SetColor("cyan");
        terminal.Write($"{currentPlayer.Loyalty}%");
        terminal.SetColor("white");
        terminal.Write("  |  Mental Health: ");
        terminal.SetColor(currentPlayer.Mental >= 50 ? "green" : "red");
        terminal.WriteLine($"{currentPlayer.Mental}");

        if (currentPlayer.King)
        {
            terminal.SetColor("bright_yellow");
            terminal.WriteLine("*** REIGNING MONARCH ***");
        }
        terminal.WriteLine("");

        // Faction
        terminal.SetColor("yellow");
        terminal.WriteLine("═══ FACTION ═══");
        var factionSystem = UsurperRemake.Systems.FactionSystem.Instance;
        if (factionSystem.PlayerFaction != null)
        {
            var faction = factionSystem.PlayerFaction.Value;
            var factionData = UsurperRemake.Systems.FactionSystem.Factions[faction];

            terminal.SetColor("white");
            terminal.Write("Allegiance: ");
            terminal.SetColor(GetFactionColor(faction));
            terminal.WriteLine(factionData.Name);

            terminal.SetColor("white");
            terminal.Write("Rank: ");
            terminal.SetColor("bright_cyan");
            terminal.Write($"{factionSystem.FactionRank}");
            terminal.SetColor("gray");
            terminal.Write(" (");
            terminal.SetColor("cyan");
            terminal.Write(factionSystem.GetCurrentRankTitle());
            terminal.SetColor("gray");
            terminal.WriteLine(")");

            // Show active bonuses
            terminal.SetColor("bright_green");
            terminal.WriteLine("Active Bonuses:");
            terminal.SetColor("green");
            switch (faction)
            {
                case UsurperRemake.Systems.Faction.TheCrown:
                    terminal.WriteLine("  • 10% discount at all shops");
                    break;
                case UsurperRemake.Systems.Faction.TheFaith:
                    terminal.WriteLine("  • 25% discount on healing services");
                    break;
                case UsurperRemake.Systems.Faction.TheShadows:
                    terminal.WriteLine("  • 20% better prices when selling items");
                    break;
            }
        }
        else
        {
            terminal.SetColor("gray");
            terminal.WriteLine("You have not pledged allegiance to any faction.");
            terminal.SetColor("darkgray");
            terminal.WriteLine("  Visit the Castle, Temple, or Dark Alley to learn more.");
        }

        // Show standing with all factions
        terminal.WriteLine("");
        terminal.SetColor("white");
        terminal.WriteLine("Faction Standing:");
        foreach (var faction in new[] { UsurperRemake.Systems.Faction.TheCrown,
                                         UsurperRemake.Systems.Faction.TheFaith,
                                         UsurperRemake.Systems.Faction.TheShadows })
        {
            var standing = factionSystem.FactionStanding[faction];
            var factionData = UsurperRemake.Systems.FactionSystem.Factions[faction];

            terminal.SetColor("gray");
            terminal.Write("  ");
            terminal.SetColor(GetFactionColor(faction));
            terminal.Write($"{factionData.Name,-15}");
            terminal.SetColor("white");
            terminal.Write(": ");

            // Color based on standing
            if (standing >= 100)
                terminal.SetColor("bright_green");
            else if (standing >= 50)
                terminal.SetColor("green");
            else if (standing >= 0)
                terminal.SetColor("gray");
            else if (standing >= -50)
                terminal.SetColor("yellow");
            else
                terminal.SetColor("red");

            terminal.Write($"{standing,4}");

            // Standing descriptor
            terminal.SetColor("darkgray");
            string standingDesc = standing switch
            {
                >= 200 => " (Revered)",
                >= 100 => " (Honored)",
                >= 50 => " (Friendly)",
                >= 0 => " (Neutral)",
                >= -50 => " (Unfriendly)",
                >= -100 => " (Hostile)",
                _ => " (Hated)"
            };
            terminal.WriteLine(standingDesc);
        }
        terminal.WriteLine("");

        // Pagination - Page 3 break
        terminal.SetColor("gray");
        terminal.Write("Press Enter to continue...");
        await terminal.GetInput("");
        terminal.WriteLine("");

        // Battle Record
        terminal.SetColor("yellow");
        terminal.WriteLine("═══ BATTLE RECORD ═══");
        terminal.SetColor("white");
        terminal.Write("Monster Kills: ");
        terminal.SetColor("bright_green");
        terminal.Write($"{currentPlayer.MKills}");
        terminal.SetColor("white");
        terminal.Write("  |  Monster Defeats: ");
        terminal.SetColor("red");
        terminal.WriteLine($"{currentPlayer.MDefeats}");

        terminal.SetColor("white");
        terminal.Write("Player Kills: ");
        terminal.SetColor("bright_yellow");
        terminal.Write($"{currentPlayer.PKills}");
        terminal.SetColor("white");
        terminal.Write("  |  Player Defeats: ");
        terminal.SetColor("red");
        terminal.WriteLine($"{currentPlayer.PDefeats}");

        // Calculate win rate
        long totalMonsterBattles = currentPlayer.MKills + currentPlayer.MDefeats;
        long totalPlayerBattles = currentPlayer.PKills + currentPlayer.PDefeats;

        if (totalMonsterBattles > 0)
        {
            double monsterWinRate = (double)currentPlayer.MKills / totalMonsterBattles * 100;
            terminal.SetColor("white");
            terminal.Write("Monster Win Rate: ");
            terminal.SetColor("cyan");
            terminal.WriteLine($"{monsterWinRate:F1}%");
        }

        if (totalPlayerBattles > 0)
        {
            double playerWinRate = (double)currentPlayer.PKills / totalPlayerBattles * 100;
            terminal.SetColor("white");
            terminal.Write("PvP Win Rate: ");
            terminal.SetColor("cyan");
            terminal.WriteLine($"{playerWinRate:F1}%");
        }
        terminal.WriteLine("");

        // Dungeon Progress
        terminal.SetColor("yellow");
        terminal.WriteLine("═══ DUNGEON PROGRESS ═══");
        terminal.SetColor("white");
        terminal.Write("Deepest Floor Reached: ");
        int deepestFloor = currentPlayer.Statistics?.DeepestDungeonLevel ?? 1;
        if (currentPlayer is Player playerForDungeon && playerForDungeon.DungeonLevel > deepestFloor)
            deepestFloor = playerForDungeon.DungeonLevel;
        terminal.SetColor("bright_magenta");
        terminal.WriteLine($"{deepestFloor} / 100");

        // Show Old Gods defeated
        var storySystem = UsurperRemake.Systems.StoryProgressionSystem.Instance;
        if (storySystem != null)
        {
            int godsDefeated = storySystem.OldGodStates.Count(g => g.Value.Status == UsurperRemake.Systems.GodStatus.Defeated);
            int godsAllied = storySystem.OldGodStates.Count(g => g.Value.Status == UsurperRemake.Systems.GodStatus.Allied);

            terminal.SetColor("white");
            terminal.Write("Old Gods: ");
            if (godsDefeated > 0)
            {
                terminal.SetColor("bright_red");
                terminal.Write($"{godsDefeated} defeated");
            }
            if (godsAllied > 0)
            {
                if (godsDefeated > 0) terminal.Write(", ");
                terminal.SetColor("bright_green");
                terminal.Write($"{godsAllied} allied");
            }
            if (godsDefeated == 0 && godsAllied == 0)
            {
                terminal.SetColor("gray");
                terminal.Write("None encountered");
            }
            terminal.WriteLine("");

            // Show seals collected
            int sealsCollected = storySystem.CollectedSeals.Count;
            terminal.SetColor("white");
            terminal.Write("Ancient Seals: ");
            terminal.SetColor(sealsCollected > 0 ? "bright_yellow" : "gray");
            terminal.WriteLine($"{sealsCollected}/6 collected");
        }
        terminal.WriteLine("");

        // God Worship & Divine Wrath
        terminal.SetColor("yellow");
        terminal.WriteLine("═══ DIVINE STATUS ═══");
        terminal.SetColor("white");
        terminal.Write("Worshipped God: ");
        string worshippedGod = UsurperRemake.GodSystemSingleton.Instance?.GetPlayerGod(currentPlayer.Name2) ?? "";
        if (!string.IsNullOrEmpty(worshippedGod))
        {
            // Get god alignment indicator from the GodSystem (Darkness > Goodness = Evil)
            var godInfo = UsurperRemake.GodSystemSingleton.Instance?.GetGod(worshippedGod);
            bool isEvilGod = godInfo != null && godInfo.Darkness > godInfo.Goodness;
            terminal.SetColor(isEvilGod ? "red" : "bright_cyan");
            terminal.WriteLine(worshippedGod);
        }
        else
        {
            terminal.SetColor("gray");
            terminal.WriteLine("None (Agnostic)");
        }

        // Show Divine Wrath status if active
        if (currentPlayer.DivineWrathPending)
        {
            terminal.SetColor("bright_red");
            terminal.WriteLine("");
            terminal.WriteLine("*** DIVINE WRATH ACTIVE ***");
            terminal.SetColor("red");
            terminal.WriteLine($"  Angered: {currentPlayer.AngeredGodName}");
            terminal.WriteLine($"  By worshipping: {currentPlayer.BetrayedForGodName}");
            terminal.SetColor("yellow");
            string severity = currentPlayer.DivineWrathLevel switch
            {
                1 => "Minor (same alignment betrayal)",
                2 => "Moderate (opposite alignment betrayal)",
                3 => "Severe (major opposite betrayal)",
                _ => "Unknown"
            };
            terminal.WriteLine($"  Severity: {severity}");
            terminal.SetColor("gray");
            terminal.WriteLine("  (Punishment may strike during dungeon exploration)");
        }
        terminal.WriteLine("");

        // Artifacts (if any collected)
        var artifactSystem = UsurperRemake.Systems.ArtifactSystem.Instance;
        if (artifactSystem != null)
        {
            var artifactAbilities = artifactSystem.GetActiveArtifactAbilities();
            if (artifactAbilities.Count > 0)
            {
                terminal.SetColor("yellow");
                terminal.WriteLine("═══ ARTIFACTS ═══");
                foreach (var ability in artifactAbilities)
                {
                    terminal.SetColor("bright_yellow");
                    terminal.WriteLine($"  {ability}");
                }
                terminal.WriteLine("");
            }
        }

        // Diseases & Afflictions
        if (currentPlayer.Blind || currentPlayer.Plague || currentPlayer.Smallpox ||
            currentPlayer.Measles || currentPlayer.Leprosy || currentPlayer.Poison > 0 ||
            currentPlayer.Addict > 0 || currentPlayer.Haunt > 0)
        {
            terminal.SetColor("bright_red");
            terminal.WriteLine("═══ AFFLICTIONS ═══");

            if (currentPlayer.Blind)
            {
                terminal.SetColor("red");
                terminal.WriteLine("  - Blind");
            }
            if (currentPlayer.Plague)
            {
                terminal.SetColor("red");
                terminal.WriteLine("  - Plague");
            }
            if (currentPlayer.Smallpox)
            {
                terminal.SetColor("red");
                terminal.WriteLine("  - Smallpox");
            }
            if (currentPlayer.Measles)
            {
                terminal.SetColor("red");
                terminal.WriteLine("  - Measles");
            }
            if (currentPlayer.Leprosy)
            {
                terminal.SetColor("red");
                terminal.WriteLine("  - Leprosy");
            }
            if (currentPlayer.Poison > 0)
            {
                terminal.SetColor("red");
                terminal.WriteLine($"  - Poisoned (Level {currentPlayer.Poison})");
            }
            if (currentPlayer.Addict > 0)
            {
                terminal.SetColor("red");
                terminal.WriteLine($"  - Addicted (Level {currentPlayer.Addict})");
            }
            if (currentPlayer.Haunt > 0)
            {
                terminal.SetColor("red");
                terminal.WriteLine($"  - Haunted by {currentPlayer.Haunt} demon(s)");
            }
            terminal.WriteLine("");
        }

        // Footer
        terminal.SetColor("gray");
        terminal.WriteLine("────────────────────────────────────────────────────────────────────────────────");

        terminal.WriteLine("");
        await terminal.PressAnyKey();
    }

    /// <summary>
    /// Get the display color for a faction
    /// </summary>
    private static string GetFactionColor(UsurperRemake.Systems.Faction faction)
    {
        return faction switch
        {
            UsurperRemake.Systems.Faction.TheCrown => "bright_yellow",
            UsurperRemake.Systems.Faction.TheFaith => "bright_cyan",
            UsurperRemake.Systems.Faction.TheShadows => "bright_magenta",
            _ => "white"
        };
    }

    /// <summary>
    /// Display a single equipment slot for the status screen
    /// </summary>
    private void DisplayEquipmentSlot(EquipmentSlot slot)
    {
        var item = currentPlayer.GetEquipment(slot);

        if (item != null)
        {
            // Color based on rarity
            terminal.SetColor(GetEquipmentRarityColor(item.Rarity));
            terminal.Write(item.Name);

            // Show key stats
            var stats = GetEquipmentStatSummary(item);
            if (!string.IsNullOrEmpty(stats))
            {
                terminal.SetColor("gray");
                terminal.Write($" ({stats})");
            }
            terminal.WriteLine("");
        }
        else
        {
            terminal.SetColor("darkgray");
            terminal.WriteLine("Empty");
        }
    }

    /// <summary>
    /// Get color based on equipment rarity
    /// </summary>
    private static string GetEquipmentRarityColor(EquipmentRarity rarity)
    {
        return rarity switch
        {
            EquipmentRarity.Common => "white",
            EquipmentRarity.Uncommon => "green",
            EquipmentRarity.Rare => "blue",
            EquipmentRarity.Epic => "magenta",
            EquipmentRarity.Legendary => "yellow",
            EquipmentRarity.Artifact => "bright_red",
            _ => "white"
        };
    }

    /// <summary>
    /// Get a short summary of equipment stats
    /// </summary>
    private static string GetEquipmentStatSummary(Equipment item)
    {
        var stats = new List<string>();

        if (item.WeaponPower > 0) stats.Add($"WP:{item.WeaponPower}");
        if (item.ArmorClass > 0) stats.Add($"AC:{item.ArmorClass}");
        if (item.ShieldBonus > 0) stats.Add($"Block:{item.ShieldBonus}");
        if (item.StrengthBonus != 0) stats.Add($"Str:{item.StrengthBonus:+#;-#;0}");
        if (item.DexterityBonus != 0) stats.Add($"Dex:{item.DexterityBonus:+#;-#;0}");
        if (item.ConstitutionBonus != 0) stats.Add($"Con:{item.ConstitutionBonus:+#;-#;0}");
        if (item.MaxHPBonus != 0) stats.Add($"HP:{item.MaxHPBonus:+#;-#;0}");
        if (item.MaxManaBonus != 0) stats.Add($"MP:{item.MaxManaBonus:+#;-#;0}");

        // Limit to 4 stats for concise display
        return string.Join(", ", stats.Take(4));
    }

    /// <summary>
    /// Display total equipment bonuses
    /// </summary>
    private void DisplayEquipmentTotals()
    {
        int totalWeapPow = 0, totalArmPow = 0;
        int totalStr = 0, totalDex = 0, totalCon = 0, totalInt = 0, totalWis = 0;
        int totalMaxHP = 0, totalMaxMana = 0;

        foreach (var slot in Enum.GetValues<EquipmentSlot>())
        {
            var item = currentPlayer.GetEquipment(slot);
            if (item != null)
            {
                totalWeapPow += item.WeaponPower;
                totalArmPow += item.ArmorClass + item.ShieldBonus;
                totalStr += item.StrengthBonus;
                totalDex += item.DexterityBonus;
                totalCon += item.ConstitutionBonus;
                totalInt += item.IntelligenceBonus;
                totalWis += item.WisdomBonus;
                totalMaxHP += item.MaxHPBonus;
                totalMaxMana += item.MaxManaBonus;
            }
        }

        terminal.SetColor("yellow");
        terminal.WriteLine("Equipment Totals:");
        terminal.SetColor("white");
        terminal.Write("  Weapon Power: ");
        terminal.SetColor("bright_red");
        terminal.Write($"{totalWeapPow}");
        terminal.SetColor("white");
        terminal.Write("  |  Armor Class: ");
        terminal.SetColor("bright_cyan");
        terminal.WriteLine($"{totalArmPow}");

        // Only show stat bonuses if there are any
        bool hasStatBonuses = totalStr != 0 || totalDex != 0 || totalCon != 0 ||
                              totalInt != 0 || totalWis != 0 || totalMaxHP != 0 || totalMaxMana != 0;
        if (hasStatBonuses)
        {
            terminal.SetColor("white");
            terminal.Write("  Bonuses: ");
            if (totalStr != 0) { terminal.SetColor("green"); terminal.Write($"Str {totalStr:+#;-#;0}  "); }
            if (totalDex != 0) { terminal.SetColor("green"); terminal.Write($"Dex {totalDex:+#;-#;0}  "); }
            if (totalCon != 0) { terminal.SetColor("green"); terminal.Write($"Con {totalCon:+#;-#;0}  "); }
            if (totalInt != 0) { terminal.SetColor("cyan"); terminal.Write($"Int {totalInt:+#;-#;0}  "); }
            if (totalWis != 0) { terminal.SetColor("cyan"); terminal.Write($"Wis {totalWis:+#;-#;0}  "); }
            if (totalMaxHP != 0) { terminal.SetColor("red"); terminal.Write($"MaxHP {totalMaxHP:+#;-#;0}  "); }
            if (totalMaxMana != 0) { terminal.SetColor("blue"); terminal.Write($"MaxMP {totalMaxMana:+#;-#;0}  "); }
            terminal.WriteLine("");
        }
    }

    /// <summary>
    /// Get location name for display
    /// </summary>
    public static string GetLocationName(GameLocation location)
    {
        return location switch
        {
            GameLocation.MainStreet => "Main Street",
            GameLocation.TheInn => "The Inn",
            GameLocation.DarkAlley => "Dark Alley",
            GameLocation.Church => "Church",
            GameLocation.WeaponShop => "Weapon Shop",
            GameLocation.ArmorShop => "Armor Shop",
            GameLocation.Bank => "Bank",
            GameLocation.Marketplace => "Marketplace",
            GameLocation.Dungeons => "Dungeons",
            GameLocation.Castle => "Royal Castle",
            GameLocation.Dormitory => "Dormitory",
            GameLocation.AnchorRoad => "Anchor Road",
            GameLocation.Temple => "Temple",
            GameLocation.BobsBeer => "Bob's Beer",
            GameLocation.Healer => "Healer",
            GameLocation.MagicShop => "Magic Shop",
            GameLocation.Master => "Level Master",
            _ => location.ToString()
        };
    }
    
    /// <summary>
    /// Get location key for navigation
    /// </summary>
    public static string GetLocationKey(GameLocation location)
    {
        return location switch
        {
            GameLocation.MainStreet => "M",
            GameLocation.TheInn => "I",
            GameLocation.DarkAlley => "D",
            GameLocation.Church => "C",
            GameLocation.WeaponShop => "W",
            GameLocation.ArmorShop => "A",
            GameLocation.Bank => "B",
            GameLocation.Marketplace => "K",
            GameLocation.Dungeons => "U",
            GameLocation.Castle => "S",
            GameLocation.Dormitory => "O",
            GameLocation.AnchorRoad => "R",
            GameLocation.Temple => "T",
            GameLocation.BobsBeer => "H",
            GameLocation.Healer => "E",
            GameLocation.MagicShop => "G",
            GameLocation.Master => "L",
            _ => "?"
        };
    }
    
    /// <summary>
    /// Add NPC to this location
    /// </summary>
    public virtual void AddNPC(NPC npc)
    {
        if (!LocationNPCs.Contains(npc))
        {
            LocationNPCs.Add(npc);
            npc.CurrentLocation = LocationId.ToString().ToLower();
        }
    }
    
    /// <summary>
    /// Remove NPC from this location
    /// </summary>
    public virtual void RemoveNPC(NPC npc)
    {
        LocationNPCs.Remove(npc);
    }
    
    /// <summary>
    /// Get location description for online system (Pascal compatible)
    /// </summary>
    public virtual string GetLocationDescription()
    {
        return LocationId switch
        {
            GameLocation.MainStreet => "Main street",
            GameLocation.TheInn => "Inn",
            GameLocation.DarkAlley => "outside the Shady Shops",
            GameLocation.Church => "Church",
            GameLocation.Dungeons => "Dungeons",
            GameLocation.WeaponShop => "Weapon shop",
            GameLocation.Master => "level master",
            GameLocation.MagicShop => "Magic shop",
            GameLocation.ArmorShop => "Armor shop",
            GameLocation.Bank => "Bank",
            GameLocation.Healer => "Healer",
            GameLocation.Marketplace => "Market Place",
            GameLocation.Dormitory => "Dormitory",
            GameLocation.AnchorRoad => "Anchor road",
            GameLocation.BobsBeer => "Bobs Beer",
            GameLocation.Castle => "Royal Castle",
            GameLocation.Prison => "Royal Prison",
            GameLocation.Temple => "Holy Temple",
            _ => Name
        };
    }

    // Convenience constructor for legacy classes that only provide name and skip description
    protected BaseLocation(GameLocation locationId, string name) : this(locationId, name, "")
    {
    }

    // Legacy constructor where parameters were (string name, GameLocation id)
    protected BaseLocation(string name, GameLocation locationId) : this(locationId, name, "")
    {
    }

    // Legacy constructor that passed only a name (defaults to NoWhere)
    protected BaseLocation(string name) : this(GameLocation.NoWhere, name, "")
    {
    }

    // Some pre-refactor code refers to LocationName instead of Name
    public string LocationName
    {
        get => Name;
        set => Name = value;
    }

    // ShortDescription used by some legacy locations
    public string ShortDescription { get; set; } = string.Empty;

    // Pascal fields expected by Prison/Temple legacy code
    public string LocationDescription { get; set; } = string.Empty;
    public HashSet<CharacterClass> AllowedClasses { get; set; } = new();
    public int LevelRequirement { get; set; } = 1;

    // Legacy single-parameter Enter wrapper
    public virtual async Task Enter(Character player)
    {
        await EnterLocation(player, TerminalEmulator.Instance ?? new TerminalEmulator());
    }

    // Legacy OnEnter hook – alias of DisplayLocation for now
    public virtual void OnEnter(Character player)
    {
        // For now simply display location header
        DisplayLocation();
    }

    // Allow derived locations to add menu options without maintaining their own list
    protected List<(string Key, string Text)> LegacyMenuOptions { get; } = new();

    public void AddMenuOption(string key, string text)
    {
        LegacyMenuOptions.Add((key, text));
    }

    // Stub for ShowLocationMenu used by some locations
    protected virtual void ShowLocationMenu()
    {
        // Basic menu display if terminal available
        if (terminal == null || LegacyMenuOptions.Count == 0) return;
        terminal.Clear();
        terminal.WriteLine($"{LocationName} Menu:");
        foreach (var (Key, Text) in LegacyMenuOptions)
        {
            terminal.WriteLine($"({Key}) {Text}");
        }
    }

    // Placeholder Ready method for Godot-style initialization
    public virtual void _Ready()
    {
        // No-op for standalone build
    }

    // Expose CurrentPlayer as Player for legacy code while still maintaining Character
    public Player? CurrentPlayer { get; protected set; }

    // Convenience GetNode wrapper (delegates to global helper)
    protected T GetNode<T>(string path) where T : class, new() => UsurperRemake.Utils.GodotHelpers.GetNode<T>(path);

    // Legacy exit helper used by some derived locations
    protected virtual async Task Exit(Player player)
    {
        // Simply break out by returning
        await Task.CompletedTask;
    }

    // Parameterless constructor retained for serialization or manual instantiation
    protected BaseLocation()
    {
        LocationId = GameLocation.NoWhere;
        Name = string.Empty;
        Description = string.Empty;
    }

    // Legacy helper referenced by some shop locations
    protected void ExitLocation()
    {
        // simply break – actual navigation handled by LocationManager
    }
} 
