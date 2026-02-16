using UsurperRemake.Utils;
using UsurperRemake.Locations;
using UsurperRemake.Systems;
using Godot;
using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using System.Linq;

/// <summary>
/// Location Manager - handles all location-related functionality
/// Based on Pascal location system from ONLINE.PAS
/// </summary>
public class LocationManager
{
    private Dictionary<GameLocation, BaseLocation> locations;
    private GameLocation currentLocationId;
    private BaseLocation currentLocation;
    private TerminalEmulator terminal;
    private Character currentPlayer;
    
    // Pascal-compatible navigation table (from ONLINE.PAS)
    private Dictionary<GameLocation, List<GameLocation>> navigationTable;
    
    private static LocationManager? _fallbackInstance;
    public static LocationManager Instance
    {
        get
        {
            var ctx = UsurperRemake.Server.SessionContext.Current;
            if (ctx?.LocationManager != null) return ctx.LocationManager;
            if (_fallbackInstance == null)
            {
                // Create a default terminal for head-less scenarios
                _fallbackInstance = new LocationManager(new TerminalEmulator());
            }
            return _fallbackInstance;
        }
    }

    // Ensure that any manually created manager becomes the global instance
    private void RegisterAsSingleton()
    {
        var ctx = UsurperRemake.Server.SessionContext.Current;
        if (ctx != null) ctx.LocationManager = this;
        else if (_fallbackInstance == null) _fallbackInstance = this;
    }

    public LocationManager() : this(new TerminalEmulator())
    {
    }

    public LocationManager(TerminalEmulator term)
    {
        terminal = term;
        locations = new Dictionary<GameLocation, BaseLocation>();
        InitializeLocations();
        InitializeNavigationTable();
        currentLocationId = GameLocation.MainStreet;
        RegisterAsSingleton();
    }
    
    /// <summary>
    /// Initialize all game locations
    /// </summary>
    private void InitializeLocations()
    {
        // Core locations
        locations[GameLocation.MainStreet] = new MainStreetLocation();
        locations[GameLocation.TheInn] = new InnLocation();
        locations[GameLocation.Church] = new ChurchLocation();
        locations[GameLocation.Dungeons] = new DungeonLocation();
        locations[GameLocation.Bank] = new BankLocation();
        locations[GameLocation.WeaponShop] = new WeaponShopLocation();
        locations[GameLocation.ArmorShop] = new ArmorShopLocation();
        locations[GameLocation.AuctionHouse] = new UsurperRemake.Locations.MarketplaceLocation();
        locations[GameLocation.Castle] = new CastleLocation();
        locations[GameLocation.Healer] = new HealerLocation();
        locations[GameLocation.MagicShop] = new MagicShopLocation();
        locations[GameLocation.Master] = new UsurperRemake.Locations.LevelMasterLocation();
        locations[GameLocation.DarkAlley] = new UsurperRemake.Locations.DarkAlleyLocation();
        locations[GameLocation.AnchorRoad] = new AnchorRoadLocation();
        locations[GameLocation.TeamCorner] = new TeamCornerLocation();
        // Hall of Recruitment removed - redundant with Team Corner
        locations[GameLocation.Dormitory] = new UsurperRemake.Locations.DormitoryLocation();
        locations[GameLocation.Temple] = new TempleLocation(terminal, this, UsurperRemake.GodSystemSingleton.Instance);
        locations[GameLocation.Home] = new UsurperRemake.Locations.HomeLocation();
        locations[GameLocation.Prison] = new PrisonLocation();
        locations[GameLocation.PrisonWalk] = new PrisonWalkLocation();
        
        // Phase 11: Prison System
        locations[GameLocation.Prisoner] = new PrisonLocation();
        
        // Phase 12: Relationship System
        locations[GameLocation.LoveCorner] = new LoveStreetLocation();

        // Quest Hall - where players claim and complete quests
        locations[GameLocation.QuestHall] = new QuestHallLocation(terminal);

        // BBS SysOp Console - only accessible to SysOps in door mode
        locations[GameLocation.SysOpConsole] = new SysOpLocation();

        // PvP Arena (online mode only)
        locations[GameLocation.Arena] = new ArenaLocation();

        // Note: Gym removed - stat training doesn't fit single-player endless format

        // GD.Print($"[LocationManager] Initialized {locations.Count} locations");
    }
    
    /// <summary>
    /// Initialize Pascal-compatible navigation table
    /// Exact match with ONLINE.PAS navigation case statements
    /// </summary>
    private void InitializeNavigationTable()
    {
        navigationTable = new Dictionary<GameLocation, List<GameLocation>>();
        
        // From Pascal ONLINE.PAS case statements
        navigationTable[GameLocation.MainStreet] = new List<GameLocation>
        {
            GameLocation.TheInn,       // loc1
            GameLocation.Church,       // loc2
            GameLocation.Darkness,     // loc3
            GameLocation.Master,       // loc4
            GameLocation.MagicShop,    // loc5
            GameLocation.Dungeons,     // loc6
            GameLocation.WeaponShop,   // loc7
            GameLocation.ArmorShop,    // loc8
            GameLocation.Bank,         // loc9
            GameLocation.AuctionHouse,  // loc10
            GameLocation.DarkAlley,    // loc11
            GameLocation.ReportRoom,   // loc12
            GameLocation.Healer,       // loc13
            GameLocation.AnchorRoad,   // loc14
            GameLocation.Home,         // loc15 – your personal dwelling
            GameLocation.Arena,        // loc16 – PvP arena (online only)
            GameLocation.Dormitory     // loc17 – lodging
        };
        
        navigationTable[GameLocation.TheInn] = new List<GameLocation>
        {
            GameLocation.MainStreet,   // loc1
            GameLocation.TeamCorner    // loc2
        };

        navigationTable[GameLocation.TeamCorner] = new List<GameLocation>
        {
            GameLocation.TheInn       // loc1
        };
        
        navigationTable[GameLocation.Church] = new List<GameLocation>
        {
            GameLocation.MainStreet   // loc1
        };
        
        navigationTable[GameLocation.Dungeons] = new List<GameLocation>
        {
            GameLocation.MainStreet   // loc1
        };
        
        navigationTable[GameLocation.WeaponShop] = new List<GameLocation>
        {
            GameLocation.MainStreet   // loc1
        };
        
        navigationTable[GameLocation.ArmorShop] = new List<GameLocation>
        {
            GameLocation.MainStreet   // loc1
        };
        
        navigationTable[GameLocation.Bank] = new List<GameLocation>
        {
            GameLocation.MainStreet   // loc1
        };
        
        navigationTable[GameLocation.AuctionHouse] = new List<GameLocation>
        {
            GameLocation.MainStreet,  // loc1
            GameLocation.FoodStore,   // loc2
            // Additional exits will be added as locations are implemented
        };
        
        navigationTable[GameLocation.AnchorRoad] = new List<GameLocation>
        {
            GameLocation.MainStreet,    // loc1
            GameLocation.Dormitory,     // loc2
            GameLocation.BountyRoom,    // loc3
            GameLocation.QuestHall,     // loc4
            GameLocation.Temple,        // loc6
            GameLocation.LoveStreet,    // loc7
            GameLocation.OutsideCastle, // loc8
            GameLocation.PrisonWalk,    // loc9 – outside prison grounds
            GameLocation.Home           // loc10 – your home
        };

        // Note: Gym removed from navigation

        navigationTable[GameLocation.DarkAlley] = new List<GameLocation>
        {
            GameLocation.MainStreet   // loc1
        };
        
        navigationTable[GameLocation.Home] = new List<GameLocation>
        {
            GameLocation.AnchorRoad
        };

        navigationTable[GameLocation.Arena] = new List<GameLocation>
        {
            GameLocation.MainStreet
        };

        // Add more navigation entries as needed
        // This follows the exact Pascal pattern from ONLINE.PAS
    }
    
    /// <summary>
    /// Enter a location with the current player
    /// </summary>
    public async Task EnterLocation(GameLocation locationId, Character player)
    {
        currentPlayer = player;
        
        if (!locations.ContainsKey(locationId))
        {
            GD.PrintErr($"[LocationManager] Location {locationId} not found!");
            return;
        }
        
        // Update current location
        var previousLocation = currentLocationId;
        currentLocationId = locationId;
        currentLocation = locations[locationId];
        
        // Update player location (Pascal compatibility)
        currentPlayer.Location = (int)locationId;

        // MUD mode: update room registry for presence tracking
        var ctx = UsurperRemake.Server.SessionContext.Current;
        if (ctx != null && UsurperRemake.Server.RoomRegistry.Instance != null)
        {
            var session = UsurperRemake.Server.MudServer.Instance?.ActiveSessions
                .GetValueOrDefault(ctx.Username.ToLowerInvariant());
            if (session != null)
                UsurperRemake.Server.RoomRegistry.Instance.PlayerEntered(locationId, session);
        }

        // Enter the location
        try
        {
            await currentLocation.EnterLocation(player, terminal);

            // After location loop exits, check if player died
            if (!player.IsAlive)
            {
                await HandlePlayerDeath(player);
                // After resurrection, continue playing at the Inn
                if (player.IsAlive)
                {
                    await EnterLocation(GameLocation.TheInn, player);
                }
            }
        }
        catch (LocationExitException ex)
        {
            // Handle location change request
            if (ex.DestinationLocation != GameLocation.NoWhere)
            {
                await EnterLocation(ex.DestinationLocation, player);
            }
            else
            {
                // NoWhere means quit game - mark as intentional exit
                GameEngine.MarkIntentionalExit();
                terminal.WriteLine("Returning to main menu...", "yellow");
                await Task.Delay(1000);
            }
        }
        catch (LocationChangeException ex)
        {
            // Handle legacy location change exception
            terminal.WriteLine($"Navigating to {ex.Destination}...", "yellow");
            await Task.Delay(1000);

            // Convert string destination to GameLocation enum
            if (Enum.TryParse<GameLocation>(ex.Destination, true, out var destination))
            {
                await EnterLocation(destination, player);
            }
            else
            {
                terminal.WriteLine($"Unknown destination: {ex.Destination}", "red");
                await Task.Delay(1500);
            }
        }
    }

    /// <summary>
    /// Handle player death with penalties and resurrection
    /// </summary>
    private async Task HandlePlayerDeath(Character player)
    {
        terminal.ClearScreen();
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
        if (player.Resurrections > 0)
        {
            terminal.SetColor("yellow");
            terminal.WriteLine($"You have {player.Resurrections} resurrection(s) available!");
            terminal.WriteLine("");
            var resurrect = await terminal.GetInput("Use a resurrection to avoid penalties? (Y/N): ");

            if (resurrect.ToUpper().StartsWith("Y"))
            {
                player.Resurrections--;
                player.Statistics.RecordResurrection();
                player.HP = player.MaxHP;
                terminal.SetColor("bright_green");
                terminal.WriteLine("");
                terminal.WriteLine("Divine light surrounds you!");
                terminal.WriteLine("You have been fully resurrected with no penalties!");
                await Task.Delay(2500);

                player.Location = (int)GameLocation.TheInn;
                await SaveSystem.Instance.AutoSave(player);
                return;
            }
        }

        // Apply death penalties
        terminal.SetColor("red");
        terminal.WriteLine("Death Penalties Applied:");
        terminal.WriteLine("─────────────────────────");

        // Calculate penalties
        long expLoss = player.Experience / 10;  // Lose 10% experience
        long goldLoss = player.Gold / 4;        // Lose 25% gold on hand

        // Apply penalties
        player.Experience = Math.Max(0, player.Experience - expLoss);
        player.Gold = Math.Max(0, player.Gold - goldLoss);

        // Track death count
        player.MDefeats++;

        terminal.SetColor("yellow");
        if (expLoss > 0)
            terminal.WriteLine($"  - Lost {expLoss:N0} experience points");
        if (goldLoss > 0)
            terminal.WriteLine($"  - Lost {goldLoss:N0} gold (dropped upon death)");
        terminal.WriteLine($"  - Monster defeats: {player.MDefeats}");
        terminal.WriteLine("");

        // Resurrect player at the Inn with half HP
        player.HP = Math.Max(1, player.MaxHP / 2);
        player.Location = (int)GameLocation.TheInn;

        // Clear any negative status effects
        player.Poison = 0;

        terminal.SetColor("cyan");
        terminal.WriteLine("You wake up at the Inn, nursed back to health by the innkeeper.");
        terminal.WriteLine($"Your wounds have partially healed. (HP: {player.HP}/{player.MaxHP})");
        terminal.WriteLine("");

        terminal.SetColor("gray");
        terminal.WriteLine("\"You're lucky to be alive, friend. Rest up and try again.\"");
        terminal.WriteLine("");

        await terminal.PressAnyKey();

        // Save the resurrected character
        await SaveSystem.Instance.AutoSave(player);

        terminal.SetColor("green");
        terminal.WriteLine("Your adventure continues...");
        await Task.Delay(1500);
    }
    
    /// <summary>
    /// Navigate from current location to destination
    /// Validates navigation according to Pascal rules
    /// </summary>
    public async Task<bool> NavigateTo(GameLocation destination, Character player)
    {
        // Check if navigation is allowed
        if (!CanNavigateTo(currentLocationId, destination))
        {
            terminal.WriteLine($"You cannot go to {BaseLocation.GetLocationName(destination)} from here!", "red");
            await Task.Delay(1500);
            return false;
        }
        
        // Perform navigation
        await EnterLocation(destination, player);
        return true;
    }
    
    /// <summary>
    /// Check if navigation is allowed according to Pascal rules
    /// </summary>
    public bool CanNavigateTo(GameLocation from, GameLocation to)
    {
        if (!navigationTable.ContainsKey(from))
            return false;
            
        return navigationTable[from].Contains(to);
    }
    
    /// <summary>
    /// Get available exits from current location
    /// </summary>
    public List<GameLocation> GetAvailableExits(GameLocation from)
    {
        if (navigationTable.ContainsKey(from))
            return new List<GameLocation>(navigationTable[from]);
            
        return new List<GameLocation>();
    }
    
    /// <summary>
    /// Get current location description for online system (Pascal compatible)
    /// </summary>
    public string GetCurrentLocationDescription()
    {
        return currentLocation?.GetLocationDescription() ?? "Unknown location";
    }
    
    /// <summary>
    /// Add NPC to a specific location
    /// </summary>
    public void AddNPCToLocation(GameLocation locationId, NPC npc)
    {
        if (locations.ContainsKey(locationId))
        {
            locations[locationId].AddNPC(npc);
        }
    }
    
    /// <summary>
    /// Remove NPC from a specific location
    /// </summary>
    public void RemoveNPCFromLocation(GameLocation locationId, NPC npc)
    {
        if (locations.ContainsKey(locationId))
        {
            locations[locationId].RemoveNPC(npc);
        }
    }
    
    /// <summary>
    /// Get NPCs in a specific location
    /// </summary>
    public List<NPC> GetNPCsInLocation(GameLocation locationId)
    {
        if (locations.ContainsKey(locationId))
        {
            return new List<NPC>(locations[locationId].LocationNPCs);
        }
        return new List<NPC>();
    }
    
    /// <summary>
    /// Get current location ID
    /// </summary>
    public GameLocation GetCurrentLocation()
    {
        return currentLocationId;
    }

    // Utility accessor used heavily by legacy code
    public BaseLocation? GetLocation(GameLocation id)
    {
        return locations.TryGetValue(id, out var loc) ? loc : null;
    }

    /// <summary>
    /// Legacy ChangeLocation overloads. Many older classes call these synchronous helpers.
    /// They simply enqueue a navigation request that will be executed on the calling thread.
    /// In this stub we just switch currentLocationId and return completed tasks so that the game can compile.
    /// </summary>
    public async Task ChangeLocation(Character player, string locationClassName)
    {
        // Very naive implementation – look for first location whose class name matches.
        var match = locations.Values.FirstOrDefault(l => l.GetType().Name.Equals(locationClassName, StringComparison.OrdinalIgnoreCase));
        if (match != null)
        {
            await EnterLocation(match.LocationId, player);
        }
        else
        {
            terminal.WriteLine($"Unknown location '{locationClassName}'. Returning to Main Street.", "red");
            await EnterLocation(GameLocation.MainStreet, player);
        }
    }

    public async Task ChangeLocation(Character player, GameLocation locationId)
    {
        await EnterLocation(locationId, player);
    }

    // Non-player overload kept for backwards compatibility
    public void ChangeLocation(string locationClassName)
    {
        var dummy = new Player(); // Placeholder – caller didn't provide player reference in legacy code
        ChangeLocation(dummy, locationClassName).Wait();
    }
}

/// <summary>
/// Exception thrown when a location wants to change to another location
/// </summary>
public class LocationExitException : Exception
{
    public GameLocation DestinationLocation { get; }

    public LocationExitException(GameLocation destination) : base($"Exiting to {destination}")
    {
        DestinationLocation = destination;
    }
}

/// <summary>
/// Exception thrown when the player wants to quit the game entirely
/// </summary>
public class GameExitException : Exception
{
    public string Reason { get; }

    public GameExitException(string reason = "Player quit game") : base(reason)
    {
        Reason = reason;
    }
}

/// <summary>
/// Placeholder location for locations not yet implemented
/// </summary>
public class PlaceholderLocation : BaseLocation
{
    public PlaceholderLocation(GameLocation locationId, string name, string description) 
        : base(locationId, name, description)
    {
    }
    
    protected new virtual void SetupLocation()
    {
        // Basic exits - most locations can return to main street
        PossibleExits = new List<GameLocation>
        {
            GameLocation.MainStreet
        };
        
        LocationActions = new List<string>
        {
            "This location is not yet implemented",
            "Return to Main Street"
        };
    }
    
    protected new virtual async Task<bool> ProcessChoice(string choice)
    {
        var upperChoice = choice.ToUpper().Trim();
        
        switch (upperChoice)
        {
            case "M":
            case "1":
            case "2":
                await NavigateToLocation(GameLocation.MainStreet);
                return true;
                
            case "S":
                await ShowStatus();
                return false;
                
            default:
                terminal.WriteLine("This location is not yet implemented. Press M to return to Main Street.", "yellow");
                await Task.Delay(2000);
                return false;
        }
    }
    
    protected new virtual void DisplayLocation()
    {
        terminal.ClearScreen();
        
        // Location header
        terminal.SetColor("bright_yellow");
        terminal.WriteLine(Name);
        terminal.SetColor("yellow");
        terminal.WriteLine(new string('═', Name.Length));
        terminal.WriteLine("");
        
        // Description
        terminal.SetColor("white");
        terminal.WriteLine(Description);
        terminal.WriteLine("");
        
        // Implementation notice
        terminal.SetColor("gray");
        terminal.WriteLine("This location is not yet fully implemented.");
        terminal.WriteLine("More features will be added in future updates.");
        terminal.WriteLine("");
        
        // Simple navigation
        terminal.SetColor("yellow");
        terminal.WriteLine("Navigation:");
        terminal.WriteLine("(M) Return to Main Street");
        terminal.WriteLine("(S) Status");
        terminal.WriteLine("");
        
        // Status line
        ShowStatusLine();
    }
} 
