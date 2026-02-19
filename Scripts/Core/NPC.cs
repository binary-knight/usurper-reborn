using UsurperRemake.Utils;
using UsurperRemake.Systems;
using UsurperRemake.Data;
using Godot;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using UsurperRemake;

/// <summary>
/// NPC Action types for behavior system
/// </summary>
public enum NPCActionType
{
    Idle,
    MoveTo,
    Combat,
    Shop,
    Socialize,
    Rest,
    Patrol,
    Quest,
    Move,
    Talk,
    Fight,
    Trade,
    Work
}

/// <summary>
/// NPC class that extends Character with AI behavior
/// Based on Pascal NPC system with enhanced AI features
/// </summary>
public partial class NPC : Character
{
    // NPC-specific properties
    public NPCBrain? Brain { get; set; }
    public PersonalityProfile? Personality { get; set; }
    public MemorySystem? Memory { get; set; }
    public RelationshipManager? Relationships { get; set; }
    public EmotionalState? EmotionalState { get; set; }
    public GoalSystem? Goals { get; set; }
    
    // NPC behavior settings
    public string Archetype { get; set; } = "citizen";        // thug, merchant, guard, priest, etc.
    public override string CurrentLocation { get; set; } = "Main Street";
    public bool IsSpecialNPC { get; set; } = false;           // Special scripted NPCs like Seth Able
    public string SpecialScript { get; set; } = "";           // Name of special behavior script

    // Story-connected NPC tracking (e.g., "TheStranger", "Lysandra", "Mordecai")
    // Story NPCs cannot become King/Queen and have special narrative roles
    public string StoryRole { get; set; } = "";               // Empty for non-story NPCs
    public string LoreNote { get; set; } = "";                // Description of narrative role
    public bool IsStoryNPC => !string.IsNullOrEmpty(StoryRole);  // Helper property
    
    // Daily behavior tracking
    public DateTime LastInteraction { get; set; }
    public int InteractionsToday { get; set; }
    public string LastSpokenTo { get; set; } = "";
    
    // NPC state flags
    public bool IsAwake { get; set; } = true;
    public bool IsAvailable { get; set; } = true;
    public bool IsInConversation { get; set; } = false;
    public bool IsHostile { get; set; } = false;

    /// <summary>
    /// Tracks permanent death - once true, NPC cannot interact, be recruited, or have intimate scenes.
    /// Different from IsAlive (HP > 0) which is temporary combat state.
    /// </summary>
    public bool IsDead { get; set; } = false;

    /// <summary>
    /// When this NPC was "born" in real time. Used with FamilySystem.DAYS_PER_YEAR to calculate current age.
    /// DateTime.MinValue means not yet set (legacy save migration will compute from Age).
    /// </summary>
    public DateTime BirthDate { get; set; } = DateTime.MinValue;

    /// <summary>
    /// True if this NPC died of old age (permanent, no respawn - the soul has moved on).
    /// Unlike IsDead which can be reversed by respawn, IsAgedDeath is forever.
    /// </summary>
    public bool IsAgedDeath { get; set; } = false;

    /// <summary>
    /// True if this NPC was permanently killed (combat death that stuck).
    /// Like IsAgedDeath, the respawn system skips permadead NPCs.
    /// </summary>
    public bool IsPermaDead { get; set; } = false;

    /// <summary>
    /// If pregnant, when the child is due (real-time DateTime). Null = not pregnant.
    /// </summary>
    public DateTime? PregnancyDueDate { get; set; } = null;

    /// <summary>
    /// What this NPC is currently doing (set by WorldSimulator each tick).
    /// Used for location presence flavor text. Examples: "nursing a drink", "examining a blade"
    /// </summary>
    public string CurrentActivity { get; set; } = "";

    /// <summary>
    /// Emergent role assigned by the Social Influence System based on community needs
    /// and personality fit. Examples: "Defender", "Merchant", "Healer", "Explorer", etc.
    /// </summary>
    public string EmergentRole { get; set; } = "";

    /// <summary>
    /// How many ticks this NPC has held their current emergent role.
    /// Higher values indicate more stable/established roles.
    /// </summary>
    public int RoleStabilityTicks { get; set; } = 0;

    /// <summary>
    /// Tracks recently used dialogue line IDs to prevent repetition.
    /// Managed by NPCDialogueDatabase, persisted across saves.
    /// </summary>
    public List<string> RecentDialogueIds { get; set; } = new();

    // Pascal compatibility flags
    public bool CanInteract => IsAwake && IsAvailable && !IsInConversation;
    public new string Location => CurrentLocation;  // Pascal compatibility
    public new bool IsNPC => true;                  // For compatibility checks
    
    /// <summary>
    /// Alias for King property for compatibility with GoalSystem
    /// </summary>
    public bool IsRuler => King;
    
    // Additional properties for world simulation
    public List<string> KnownCharacters { get; set; } = new();
    public List<string> Enemies { get; set; } = new();
    public List<string> GangMembers { get; set; } = new();
    public string GangId { get; set; } = "";

    /// <summary>
    /// NPC's faction affiliation (The Crown, The Shadows, The Faith)
    /// Null means unaffiliated with any major faction
    /// </summary>
    public Faction? NPCFaction { get; set; } = null;
    public new bool ControlsTurf { get; set; }
    
    // Missing properties for API compatibility
    public string Id { get; set; } = Guid.NewGuid().ToString();  // Unique identifier
    public new string TeamPassword { get; set; } = "";               // Team password for joining

    // Marketplace inventory - items NPC has available to sell (uses global Item class)
    public List<global::Item> MarketInventory { get; set; } = new();
    public int MaxMarketInventory => 5 + (Level / 10);  // 5-15 items based on level
    
    /// <summary>
    /// Constructor for creating a new NPC
    /// </summary>
    public NPC(string name, string archetype, CharacterClass characterClass, int level = 1)
    {
        // Set basic character properties
        Name1 = name;
        Name2 = name;
        AI = CharacterAI.Computer;
        Class = characterClass;
        Level = level;
        Archetype = archetype;
        
        // Generate appropriate stats for the archetype and level
        GenerateStatsForArchetype(archetype, level);
        
        // Initialize NPC systems
        InitializeNPCSystems();
        
        // Set initial equipment and location based on archetype
        SetArchetypeDefaults();

        // Calculate birth date from Age so the aging system works from creation
        if (Age > 0)
            BirthDate = DateTime.Now.AddHours(-Age * GameConfig.NpcLifecycleHoursPerYear);
    }
    
    /// <summary>
    /// Parameter-less constructor for deserialization/restoration (defers system initialization).
    /// Call EnsureSystemsInitialized() after setting Name1/Name2 to properly initialize AI.
    /// </summary>
    public NPC()
    {
        // Minimal setup for deserialization - don't initialize systems with "Unknown" name
        AI = CharacterAI.Computer;
        Class = CharacterClass.Warrior;
        Level = 1;
        Archetype = "citizen";
        // Note: Brain, Personality, Memory, etc. will be null until EnsureSystemsInitialized() is called
    }

    /// <summary>
    /// Ensures NPC AI systems are initialized. Call this after setting Name1/Name2 on a deserialized NPC.
    /// Safe to call multiple times - will only initialize if Brain is null.
    /// </summary>
    public void EnsureSystemsInitialized()
    {
        if (Brain == null)
        {
            InitializeNPCSystems();
        }
    }
    
    /// <summary>
    /// Initialize NPC AI systems
    /// </summary>
    private void InitializeNPCSystems()
    {
        // Create personality based on archetype (only if not already set, e.g., from save restoration)
        Personality ??= PersonalityProfile.GenerateForArchetype(Archetype);

        // Initialize memory system
        Memory ??= new MemorySystem();

        // Initialize relationship manager
        Relationships ??= new RelationshipManager();

        // Initialize emotional state
        EmotionalState ??= new EmotionalState();

        // Initialize goal system (stubbed dependency injection for now)
        Goals ??= new GoalSystem(Personality);

        // Create brain with minimal required parameters
        Brain ??= new NPCBrain(this, Personality);

        // Check if this is a special Pascal NPC
        CheckForSpecialNPC();
    }
    
    /// <summary>
    /// Check if this NPC has special scripted behavior from Pascal
    /// </summary>
    private void CheckForSpecialNPC()
    {
        var specialNPCs = new Dictionary<string, string>
        {
            {"Seth Able", "drunk_fighter"},
            {"Dungeon Guard", "dungeon_guardian"},
            {"King's Guard", "castle_guard"},
            {"Death Knight", "dungeon_boss"},
            {"Bob", "tavern_keeper"},
            {"Bishop", "church_leader"},
            {"Gossip Monger", "love_corner_keeper"},
            {"Gym Masseur", "gym_worker"}
        };
        
        if (specialNPCs.ContainsKey(Name2))
        {
            IsSpecialNPC = true;
            SpecialScript = specialNPCs[Name2];
        }
    }
    
    /// <summary>
    /// Generate stats appropriate for archetype and level
    /// </summary>
    private void GenerateStatsForArchetype(string archetype, int level)
    {
        // Base stats
        var baseStats = GetBaseStatsForArchetype(archetype);
        
        // Apply level scaling
        Strength = baseStats.Strength + (level * 2);
        Defence = baseStats.Defence + (level * 2);
        Dexterity = baseStats.Dexterity + (level * 2);
        Agility = baseStats.Agility + (level * 2);
        Charisma = baseStats.Charisma + level;
        Wisdom = baseStats.Wisdom + level;
        Stamina = baseStats.Stamina + (level * 3);
        
        // Calculate HP and Mana
        MaxHP = 20 + (level * 8) + (Stamina / 2);
        HP = MaxHP;
        MaxMana = 10 + (level * 2) + (Wisdom / 3);
        Mana = MaxMana;
        
        // Set gold based on archetype and level
        Gold = GetBaseGold(archetype) * level;
        
        // Set experience
        Experience = GetExperienceForLevel(level);
    }
    
    /// <summary>
    /// Get base stats for different archetypes
    /// </summary>
    private (int Strength, int Defence, int Dexterity, int Agility, int Charisma, int Wisdom, int Stamina) GetBaseStatsForArchetype(string archetype)
    {
        return archetype.ToLower() switch
        {
            "thug" or "fighter" => (15, 12, 10, 8, 6, 7, 14),
            "guard" => (14, 15, 8, 7, 9, 8, 16),
            "merchant" => (8, 10, 12, 10, 15, 12, 10),
            "priest" or "cleric" => (9, 11, 8, 7, 12, 16, 12),
            "mystic" or "mage" => (7, 8, 10, 9, 11, 18, 8),
            "assassin" or "thief" => (11, 9, 16, 15, 10, 12, 11),
            "noble" => (10, 12, 11, 10, 16, 14, 12),
            "citizen" => (10, 10, 10, 10, 10, 10, 10),
            _ => (10, 10, 10, 10, 10, 10, 10)
        };
    }
    
    /// <summary>
    /// Get base gold for archetype
    /// </summary>
    private long GetBaseGold(string archetype)
    {
        return archetype.ToLower() switch
        {
            "noble" => 500,
            "merchant" => 300,
            "priest" => 200,
            "guard" => 150,
            "citizen" => 100,
            "thug" => 50,
            _ => 75
        };
    }
    
    /// <summary>
    /// Set archetype-specific defaults
    /// </summary>
    private void SetArchetypeDefaults()
    {
        switch (Archetype.ToLower())
        {
            case "thug":
                CurrentLocation = "Inn";
                Race = (CharacterRace)GD.RandRange(0, 9); // Random race
                Sex = (CharacterSex)GD.RandRange(1, 2);
                IsHostile = GD.Randf() < 0.3f; // 30% chance of being hostile
                break;

            case "guard":
                CurrentLocation = GD.RandRange(0, 2) == 0 ? "Castle" : "Main Street";
                Race = CharacterRace.Human; // Guards are usually human
                Sex = (CharacterSex)GD.RandRange(1, 2);
                break;

            case "merchant":
                CurrentLocation = "Auction House";
                Race = CharacterRace.Human;
                Sex = (CharacterSex)GD.RandRange(1, 2);
                break;

            case "priest":
                CurrentLocation = "Temple";
                Race = CharacterRace.Human;
                Sex = (CharacterSex)GD.RandRange(1, 2);
                Chivalry = Level * 10; // Priests start with chivalry
                break;

            case "mystic":
                CurrentLocation = "Magic Shop";
                Race = CharacterRace.Elf;
                Sex = (CharacterSex)GD.RandRange(1, 2);
                break;

            default:
                CurrentLocation = "Main Street";
                Race = (CharacterRace)GD.RandRange(0, 9);
                Sex = (CharacterSex)GD.RandRange(1, 2);
                break;
        }
        
        // Set age based on level and archetype (minimum 18)
        Age = 18 + Level + GD.RandRange(-5, 10);
        if (Age < 18) Age = 18;
        if (Age > 80) Age = 80;
    }
    
    /// <summary>
    /// Get experience needed for a specific level (Pascal compatible)
    /// </summary>
    private long GetExperienceForLevel(int level)
    {
        if (level <= 1) return 0;
        
        // Experience formula compatible with Pascal
        long exp = 0;
        for (int i = 2; i <= level; i++)
        {
            // Softened curve: level^2.0 * 50 (rebalanced v0.41.4)
            exp += (long)(Math.Pow(i, 2.0) * 50);
        }
        return exp;
    }
    
    /// <summary>
    /// Main NPC update method - called by world simulator
    /// Enhanced with Pascal-compatible behaviors
    /// </summary>
    public void UpdateNPC(float deltaTime)
    {
        // Update emotional state based on recent memories
        EmotionalState.Update(Memory.GetRecentEvents());
        
        // Process goals and decide actions
        var action = Brain.DecideNextAction(GetWorldState());
        
        // Execute the action
        ExecuteAction(action);
        
        // Update memory with recent events
        Memory.ProcessNewMemories();
        
        // Update relationships
        Relationships.UpdateRelationships();
        
        // Enhanced NPC behaviors (Phase 21)
        ProcessEnhancedBehaviors(deltaTime);
    }
    
    /// <summary>
    /// Enhanced NPC behaviors - Pascal NPC_CHEC.PAS and NPCMAINT.PAS integration
    /// </summary>
    private void ProcessEnhancedBehaviors(float deltaTime)
    {
        var random = new Random();
        
        // Inventory management (Pascal NPC_CHEC.PAS logic)
        if (random.Next(100) < 5) // 5% chance per update
        {
            ProcessInventoryCheck();
        }
        
        // Shopping behavior (Pascal NPCMAINT.PAS shopping)
        if (random.Next(200) < 3 && Gold > 100) // 1.5% chance if has gold
        {
            ProcessShoppingBehavior();
        }
        
        // Gang/team management
        if (random.Next(300) < 2) // 0.67% chance
        {
            ProcessGangBehavior();
        }
        
        // Faith/believer system
        if (random.Next(400) < 2) // 0.5% chance
        {
            ProcessBelieverBehavior();
        }
    }
    
    /// <summary>
    /// Pascal NPC_CHEC.PAS inventory management
    /// </summary>
    private void ProcessInventoryCheck()
    {
        // Re-evaluate all equipped items
        Memory.AddMemory("I checked my equipment", "inventory", DateTime.Now);
        
        // Simple equipment optimization
        if (WeaponPower < Level * 15)
        {
            Goals.AddGoal(new Goal("Find Better Weapon", GoalType.Economic, 0.7f));
        }
        
        if (ArmorClass < Level * 10)
        {
            Goals.AddGoal(new Goal("Find Better Armor", GoalType.Economic, 0.6f));
        }
    }
    
    /// <summary>
    /// Pascal NPCMAINT.PAS shopping behavior
    /// </summary>
    private void ProcessShoppingBehavior()
    {
        var shoppingLocations = new[] { "Main Street", "Weapon Shop", "Armor Shop", "Magic Shop" };
        
        if (shoppingLocations.Contains(CurrentLocation))
        {
            // Determine what to buy based on class and needs
            var shoppingGoal = DetermineShoppingGoal();
            if (shoppingGoal != null)
            {
                Goals.AddGoal(shoppingGoal);
                Memory.AddMemory($"I need to buy {shoppingGoal.Name}", "shopping", DateTime.Now);
            }
        }
    }
    
    /// <summary>
    /// Gang behavior from Pascal NPCMAINT.PAS
    /// </summary>
    private void ProcessGangBehavior()
    {
        var random = new Random();

        if (string.IsNullOrEmpty(Team) && string.IsNullOrEmpty(GangId))
        {
            // Not in a gang - consider joining one
            if (Personality.IsLikelyToJoinGang() && random.Next(10) == 0
                && !Goals.AllGoals.Any(g => g.Name.Contains("Gang") || g.Name.Contains("Join")))
            {
                Goals.AddGoal(new Goal("Join Gang", GoalType.Social, 0.8f));
                Memory.AddMemory("I should look for a gang to join", "social", DateTime.Now);
            }
        }
        else
        {
            // In a gang - gang loyalty actions
            if (random.Next(5) == 0)
            {
                string teamName = !string.IsNullOrEmpty(Team) ? Team : "my gang";
                Memory.AddMemory($"I'm loyal to {teamName}", "gang", DateTime.Now);
                if (!Goals.AllGoals.Any(g => g.Name == "Support Gang" && g.IsActive))
                    Goals.AddGoal(new Goal("Support Gang", GoalType.Social, 0.7f));
            }
        }
    }
    
    /// <summary>
    /// Pascal NPCMAINT.PAS believer system
    /// </summary>
    private void ProcessBelieverBehavior()
    {
        var random = new Random();
        
        if (string.IsNullOrEmpty(God))
        {
            // Potential conversion
            if (random.Next(20) == 0)
            {
                var availableGods = new[] { "Nosferatu", "Darkcloak", "Druid" };
                God = availableGods[random.Next(availableGods.Length)];
                Memory.AddMemory($"I found faith in {God}", "faith", DateTime.Now);
                EmotionalState.AdjustMood("spiritual", 0.3f);
            }
        }
        else
        {
            // Existing believer - perform faith actions
            if (random.Next(10) == 0)
            {
                var actions = new[] { "pray", "make offering", "seek guidance" };
                var action = actions[random.Next(actions.Length)];
                Memory.AddMemory($"I {action} to {God}", "faith", DateTime.Now);
                
                // Faith actions affect mood
                EmotionalState.AdjustMood("spiritual", 0.1f);
            }
        }
    }
    
    /// <summary>
    /// Determine shopping goal based on class and personality
    /// </summary>
    private Goal? DetermineShoppingGoal()
    {
        switch (Class)
        {
            case CharacterClass.Warrior:
                if (WeaponPower < Level * 20)
                    return new Goal("Buy Better Weapon", GoalType.Economic, Personality.Greed);
                if (ArmorClass < Level * 15)
                    return new Goal("Buy Better Armor", GoalType.Economic, Personality.Greed * 0.8f);
                break;
                
            case CharacterClass.Magician:
                if (Mana < MaxMana * 0.7f)
                    return new Goal("Buy Mana Potions", GoalType.Economic, 0.6f);
                break;
                
            case CharacterClass.Paladin:
                if (HP < MaxHP * 0.8f)
                    return new Goal("Buy Healing Potions", GoalType.Economic, 0.7f);
                break;
        }
        
        return null;
    }
    
    /// <summary>
    /// Execute an NPC action
    /// </summary>
    private void ExecuteAction(NPCAction action)
    {
        switch ((NPCActionType)action.Type)
        {
            case NPCActionType.Move:
                Move(action.Target);
                break;
                
            case NPCActionType.Talk:
                // Handled by conversation system
                break;
                
            case NPCActionType.Fight:
                InitiateFight(action.Target);
                break;
                
            case NPCActionType.Trade:
                // Handled by trading system
                break;
                
            case NPCActionType.Rest:
                Rest();
                break;
                
            case NPCActionType.Work:
                Work();
                break;
                
            case NPCActionType.Socialize:
                Socialize();
                break;
                
            case NPCActionType.Idle:
                // Do nothing
                break;
        }
    }
    
    /// <summary>
    /// Move to a new location
    /// </summary>
    private void Move(string newLocation)
    {
        if (!string.IsNullOrEmpty(newLocation) && newLocation != CurrentLocation)
        {
            CurrentLocation = newLocation;
            Memory.AddMemory($"I moved to {newLocation}", "movement", DateTime.Now);
        }
    }
    
    /// <summary>
    /// Initiate combat with target
    /// </summary>
    private void InitiateFight(string target)
    {
        // This would trigger the combat system
        Memory.AddMemory($"I fought with {target}", "combat", DateTime.Now);
    }
    
    /// <summary>
    /// Rest and recover
    /// </summary>
    private void Rest()
    {
        HP = Math.Min(HP + 10, MaxHP);
        Mana = Math.Min(Mana + 5, MaxMana);
    }
    
    /// <summary>
    /// Work at job (archetype-specific)
    /// </summary>
    private void Work()
    {
        switch (Archetype.ToLower())
        {
            case "merchant":
                Gold += GD.RandRange(5, 20);
                break;
                
            case "guard":
                // Guards patrol
                var locations = new[] { "Main Street", "Castle", "Auction House" };
                CurrentLocation = locations[GD.RandRange(0, locations.Length - 1)];
                break;
                
            case "priest":
                // Priests help others
                Chivalry += 1;
                break;
        }
    }
    
    /// <summary>
    /// Socialize with other NPCs/players
    /// </summary>
    private void Socialize()
    {
        // This would interact with other characters in the same location
        EmotionalState.AdjustMood("social", 0.1f);
    }
    
    /// <summary>
    /// Get current world state for decision making
    /// </summary>
    private WorldState GetWorldState()
    {
        return new WorldState
        {
            CurrentLocation = CurrentLocation,
            TimeOfDay = DateTime.Now.Hour,
            PlayersInArea = GetPlayersInLocation(CurrentLocation),
            NPCsInArea = GetNPCsInLocation(CurrentLocation),
            DangerLevel = CalculateDangerLevel()
        };
    }
    
    /// <summary>
    /// Get greeting for a player (Pascal compatible)
    /// Uses the Dynamic NPC Dialogue Generator for rich, personality-driven greetings
    /// </summary>
    public string GetGreeting(Character player)
    {
        if (IsSpecialNPC)
        {
            return GetSpecialGreeting(player);
        }

        // Check for faction-based greeting if both NPC and player have faction affiliations
        if (NPCFaction != null)
        {
            var playerFaction = FactionSystem.Instance?.PlayerFaction;
            if (playerFaction != null)
            {
                // Use faction-aware greeting
                return FactionSystem.Instance!.GetFactionGreeting(NPCFaction.Value, player);
            }
        }

        // Use the Dynamic NPC Dialogue Generator for rich, contextual greetings
        if (player is Player playerChar)
        {
            return NPCDialogueGenerator.GenerateGreeting(this, playerChar);
        }

        // Fallback for non-Player characters - use legacy personality-based greeting
        string playerName = player?.Name2 ?? player?.Name1 ?? "stranger";
        int relationship = Relationships?.GetRelationshipWith(playerName) ?? 0;
        return Brain?.GenerateGreeting(player!, relationship) ?? "Hello there.";
    }

    /// <summary>
    /// Get a farewell message for the player
    /// </summary>
    public string GetFarewell(Player player)
    {
        if (player == null) return "Farewell.";
        return NPCDialogueGenerator.GenerateFarewell(this, player);
    }

    /// <summary>
    /// Get small talk/conversation for the player
    /// </summary>
    public string GetSmallTalk(Player player)
    {
        return NPCDialogueGenerator.GenerateSmallTalk(this, player);
    }

    /// <summary>
    /// Get a reaction to an event
    /// </summary>
    public string GetReaction(Player player, string eventType)
    {
        return NPCDialogueGenerator.GenerateReaction(this, player, eventType);
    }
    
    /// <summary>
    /// Get special greeting for special NPCs (Pascal behavior)
    /// </summary>
    private string GetSpecialGreeting(Character player)
    {
        return SpecialScript switch
        {
            "drunk_fighter" => GetSethAbleGreeting(player),
            "dungeon_guardian" => "The dungeon is dangerous! Many never return...",
            "castle_guard" => "Halt! State your business in the castle!",
            "tavern_keeper" => "Welcome to my establishment! What can I get you?",
            _ => "Hello there, traveler."
        };
    }
    
    /// <summary>
    /// Seth Able specific greetings (from Pascal)
    /// </summary>
    private string GetSethAbleGreeting(Character player)
    {
        var greetings = new[]
        {
            "You lookin' at me funny?!",
            "*hiccup* Want to fight?",
            "I can take anyone in this place!",
            "*burp* You think you're tough?",
            "Another pretty boy... pfft!"
        };
        
        return greetings[GD.RandRange(0, greetings.Length - 1)];
    }
    
    /// <summary>
    /// Get display info for terminal
    /// </summary>
    public string GetDisplayInfo()
    {
        var marker = IsNPC ? GameConfig.NpcMark : "";
        var status = IsHostile ? " (Hostile)" : "";
        return $"{marker}{DisplayName} [{Archetype}, Level {Level}]{status}";
    }
    
    // Helper methods for world state
    private List<string> GetPlayersInLocation(string location)
    {
        var players = new List<string>();

        // In single-player mode, check if current player is in this location
        try
        {
            var currentPlayer = GameEngine.Instance?.CurrentPlayer;
            if (currentPlayer != null)
            {
                // Player's current location can be checked via GameEngine's location manager
                // For now, we use the player's tracked location string if available
                var playerLocation = currentPlayer.CurrentLocation;
                if (!string.IsNullOrEmpty(playerLocation) &&
                    playerLocation.Equals(location, StringComparison.OrdinalIgnoreCase))
                {
                    players.Add(currentPlayer.Name2 ?? currentPlayer.Name1);
                }
            }
        }
        catch (Exception ex)
        {
            // GameEngine not ready - return empty list
            GD.PrintErr($"[NPC] GetPlayersInLocation error: {ex.Message}");
        }

        return players;
    }

    private List<string> GetNPCsInLocation(string location)
    {
        var npcNames = new List<string>();

        try
        {
            // Query NPCSpawnSystem for NPCs at this location
            var npcs = NPCSpawnSystem.Instance?.GetNPCsAtLocation(location);
            if (npcs != null)
            {
                foreach (var npc in npcs)
                {
                    if (npc.IsAlive && npc != this) // Exclude self
                    {
                        npcNames.Add(npc.Name2 ?? npc.Name1);
                    }
                }
            }
        }
        catch (Exception ex)
        {
            // NPCSpawnSystem not ready - return empty list
            GD.PrintErr($"[NPC] GetNPCsInLocation error: {ex.Message}");
        }

        return npcNames;
    }
    
    private float CalculateDangerLevel()
    {
        // Calculate danger based on location, time, etc.
        return CurrentLocation switch
        {
            "Dungeon" => 0.8f,
            "Inn" => 0.3f,
            "Main Street" => 0.1f,
            "Castle" => 0.2f,
            _ => 0.1f
        };
    }
    
    /// <summary>
    /// Update the NPC's textual and logical location, keeping the LocationManager caches in sync.
    /// </summary>
    public void UpdateLocation(string newLocation)
    {
        if (string.IsNullOrWhiteSpace(newLocation)) return;

        var oldLocationText = CurrentLocation;
        if (oldLocationText.Equals(newLocation, StringComparison.OrdinalIgnoreCase))
            return; // nothing to do

        // Convert textual representation to GameLocation enum
        GameLocation oldLocId = MapStringToLocationSafe(oldLocationText);
        GameLocation newLocId = MapStringToLocationSafe(newLocation);

        try
        {
            // Remove from previous location list
            if (oldLocId != GameLocation.NoWhere)
            {
                LocationManager.Instance.RemoveNPCFromLocation(oldLocId, this);
            }

            // Update internal field
            CurrentLocation = newLocation;

            // Clear activity so location-contextual flavor text takes over
            CurrentActivity = "";

            // Add to new location list
            if (newLocId != GameLocation.NoWhere)
            {
                LocationManager.Instance.AddNPCToLocation(newLocId, this);
            }
        }
        catch (Exception ex)
        {
            // Log error but continue - LocationManager may not be ready during initialization
            // Will be corrected on next movement attempt
            GD.PrintErr($"[NPC] UpdateLocation error for {Name2}: {ex.Message}");
            CurrentLocation = newLocation;
        }
    }

    /// <summary>
    /// Local helper that maps loose-text location strings used by AI into GameLocation enum.
    /// Accepts forms like "main_street", "town_square", "inn", etc.
    /// Falls back to NoWhere if unknown.
    /// </summary>
    private static GameLocation MapStringToLocationSafe(string loc)
    {
        if (string.IsNullOrWhiteSpace(loc)) return GameLocation.NoWhere;

        switch (loc.Trim().ToLower())
        {
            case "town_square":
            case "main_street":
            case "mainstreet":
                return GameLocation.MainStreet;

            case "tavern":
            case "inn":
            case "theinn":
                return GameLocation.TheInn;

            case "market":
            case "marketplace":
            case "auction house":
            case "auctionhouse":
                return GameLocation.AuctionHouse;

            case "castle":
                return GameLocation.Castle;

            case "temple":
            case "church":
                return GameLocation.Temple;

            case "dungeon":
            case "dungeons":
                return GameLocation.Dungeons;

            case "dark_alley":
            case "darkalley":
                return GameLocation.DarkAlley;

            case "bank":
                return GameLocation.Bank;

            case "dormitory":
            case "dorm":
                return GameLocation.Dormitory;

            case "anchor_road":
            case "anchorroad":
                return GameLocation.AnchorRoad;

            case "weapon_shop":
            case "weaponshop":
            case "weapons":
                return GameLocation.WeaponShop;

            case "armor_shop":
            case "armorshop":
            case "armour_shop":
            case "armor":
                return GameLocation.ArmorShop;

            case "magic_shop":
            case "magicshop":
            case "magic":
                return GameLocation.MagicShop;

            case "healer":
            case "healers":
                return GameLocation.Healer;

            case "level_master":
            case "levelmaster":
            case "master":
                return GameLocation.Master;

            case "chapel":
            case "shrine":
                return GameLocation.Temple;

            case "love_corner":
            case "lovecorner":
                return GameLocation.LoveCorner;

            default:
                return GameLocation.NoWhere;
        }
    }
    
    // Missing methods for API compatibility
    public bool CanAfford(long cost)
    {
        return Gold >= cost;
    }
    
    public void SpendGold(long amount)
    {
        Gold = Math.Max(0, Gold - amount);
    }
    
    public void GainGold(long amount)
    {
        Gold += amount;
    }
    
    public void AddRelationship(string characterId, int relationValue)
    {
        // Implementation for relationship tracking
    }
    
    public void AddRelationship(string characterId, UsurperRemake.RelationshipType relationType)
    {
        AddRelationship(characterId, (int)relationType);
    }
    
    public long GetAttackPower()
    {
        return WeaponPower + (Strength / 2);
    }
    
    public long GetArmorClass()
    {
        return ArmorClass + (Defence / 3);
    }
    
    public void TakeDamage(long damage)
    {
        HP = Math.Max(0, HP - damage);
    }
    
    public void SetState(object state)
    {
        // Implementation for NPC state management
    }
    
    public void Heal(long amount)
    {
        HP = Math.Min(MaxHP, HP + amount);
    }
    
    public void GainExperience(long exp)
    {
        Experience += exp;
    }
    
    public void ChangeActivity(object activity)
    {
        // Implementation for activity changes
    }

    // Overload expected by WorldSimulator (Activity type, urgency/intensity)
    public void ChangeActivity(UsurperRemake.Activity activity, int intensity)
    {
        // Stub: no behaviour yet
    }

    public void ChangeActivity(UsurperRemake.Activity activity, string description)
    {
        // Stub for compatibility, no actual behaviour now
    }

    /// <summary>
    /// Returns a mood-flavored prefix for this NPC's dialogue based on emotional state
    /// and their impression of the player. Used by shops and interaction screens.
    /// </summary>
    public string GetMoodPrefix(Character player)
    {
        // Try pre-generated dialogue database first
        if (player is Player p)
        {
            var dbLine = NPCDialogueDatabase.GetBestLine("mood_prefix", this, p);
            if (dbLine != null) return dbLine;
        }

        var impression = Memory?.GetCharacterImpression(player?.Name2 ?? "") ?? 0f;
        var dominant = EmotionalState?.GetDominantEmotion();
        var mood = EmotionalState?.GetCurrentMood() ?? 0.5f;

        // Check for grieving (spouse died recently)
        if (Married == false && Memory != null &&
            Memory.HasMemoryOfEvent(MemoryType.SawDeath, null, 168)) // within 7 days
        {
            return $"{Name2} seems distracted, eyes red-rimmed. \"Sorry, I... what did you need?\"";
        }

        // Check if this NPC has heard about the player through gossip but never met them directly
        var playerName = player?.Name2 ?? "";
        bool heardThroughGossip = Memory != null && !string.IsNullOrEmpty(playerName) &&
            Memory.GetMemoriesAboutCharacter(playerName).Any(m => m.Type == MemoryType.HeardGossip) &&
            !Memory.GetMemoriesAboutCharacter(playerName).Any(m =>
                m.Type != MemoryType.HeardGossip && m.Type != MemoryType.SharedOpinion);

        // Gossip-based greetings — NPC has never met player but has heard about them
        if (heardThroughGossip && Math.Abs(impression) > GameConfig.ReputationThresholdForReaction)
        {
            if (impression < -0.5f)
                return $"{Name2} stiffens as you approach. \"I know what you've done. Stay away from me.\"";
            if (impression < -0.3f)
                return $"{Name2} eyes you warily. \"I've heard things about you... nothing good.\"";
            if (impression > 0.5f)
                return $"{Name2}'s eyes light up with recognition. \"You're {playerName}! I've heard great things about you!\"";
            if (impression > 0.3f)
                return $"{Name2} nods respectfully. \"I've heard good things about you, {playerName}.\"";
        }

        // Strong negative impression of the player
        if (impression < -0.5f)
        {
            if (dominant == EmotionType.Anger)
                return $"{Name2}'s jaw tightens when seeing you. \"You've got nerve showing your face here.\"";
            return $"{Name2} eyes you coldly. \"What do you want?\"";
        }

        // Strong positive impression
        if (impression > 0.5f)
        {
            if (dominant == EmotionType.Joy)
                return $"{Name2} beams at you. \"My favorite customer! What can I do for you?\"";
            return $"{Name2} greets you warmly. \"Ah, good to see you again!\"";
        }

        // Emotional states (neutral impression)
        if (dominant != null)
        {
            switch (dominant.Value)
            {
                case EmotionType.Anger:
                    return $"{Name2} looks irritable today. \"Yeah? What is it?\"";
                case EmotionType.Sadness:
                    return $"{Name2} sighs heavily. \"Welcome, I suppose...\"";
                case EmotionType.Fear:
                    return $"{Name2} glances around nervously. \"Oh! You startled me. What do you need?\"";
                case EmotionType.Joy:
                    return $"{Name2} hums a cheerful tune. \"Welcome, welcome!\"";
                case EmotionType.Hope:
                    return $"{Name2} looks up with bright eyes. \"Good timing! What can I help with?\"";
            }
        }

        // Neutral fallback
        return $"{Name2} nods in greeting.";
    }

    /// <summary>
    /// Get a price modifier based on mood and impression of the player.
    /// Happy/positive = small discount, grieving/hostile = markup.
    /// Returns multiplier (e.g. 0.95 for 5% discount, 1.10 for 10% markup).
    /// </summary>
    public float GetMoodPriceModifier(Character player)
    {
        var impression = Memory?.GetCharacterImpression(player?.Name2 ?? "") ?? 0f;
        var mood = EmotionalState?.GetCurrentMood() ?? 0.5f;

        float modifier = 1.0f;

        // Impression-based: -5% for liked, +5% for disliked
        if (impression > 0.5f) modifier -= 0.05f;
        else if (impression < -0.5f) modifier += 0.05f;

        // Reputation-based pricing (gossip-informed, v0.42.0)
        // NPCs who heard good things give a discount, bad things add markup
        if (impression > GameConfig.ReputationThresholdForReaction)
            modifier -= GameConfig.ReputationPricingDiscount;
        else if (impression < -GameConfig.ReputationThresholdForReaction)
            modifier += GameConfig.ReputationPricingMarkup;

        // Mood-based: happy = -5%, grieving/angry = +10%
        if (mood > 0.7f) modifier -= 0.05f;
        else if (mood < 0.3f) modifier += 0.10f;

        // Blood Price markup — merchants charge more for known killers (v0.42.0)
        if (player != null && player.MurderWeight >= GameConfig.MurderWeightShopMarkupThreshold)
            modifier += GameConfig.MurderWeightShopMarkupPercent;

        return Math.Clamp(modifier, 0.80f, 1.30f);
    }
} 
