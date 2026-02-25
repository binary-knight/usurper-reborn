using UsurperRemake.Utils;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;

/// <summary>
/// God System - Complete Pascal-compatible god management system
/// Based on Pascal VARGODS.PAS with all god functions and procedures
/// Handles god creation, management, believers, sacrifices, and divine interventions
/// </summary>
public class GodSystem
{
    private List<God> gods;
    private Dictionary<string, God> godsByName;
    private Dictionary<string, string> playerGods; // player name -> god name mapping
    private Random random;
    private DateTime lastMaintenance;
    
    public GodSystem()
    {
        gods = new List<God>();
        godsByName = new Dictionary<string, God>();
        playerGods = new Dictionary<string, string>();
        random = new Random();
        lastMaintenance = DateTime.Now;
        
        // Initialize with supreme creator if needed
        InitializeSupremeCreator();

        // Initialize the default pantheon gods
        InitializeDefaultPantheon();
    }
    
    /// <summary>
    /// Initialize the supreme creator god (Pascal global_supreme_creator)
    /// </summary>
    private void InitializeSupremeCreator()
    {
        if (!godsByName.ContainsKey(GameConfig.SupremeCreatorName))
        {
            var supremeCreator = new God
            {
                Name = GameConfig.SupremeCreatorName,
                RealName = "System",
                Id = "SUPREME",
                Level = GameConfig.MaxGodLevel,
                Experience = GameConfig.GodLevel9Experience * 10,
                AI = GameConfig.GodAIComputer,
                Age = 1000,
                Sex = 1,
                Believers = 0,
                Deleted = false,
                DeedsLeft = 99,
                Darkness = 0,
                Goodness = 50000
            };
            
            gods.Add(supremeCreator);
            godsByName[supremeCreator.Name] = supremeCreator;
        }
    }

    /// <summary>
    /// Initialize the default pantheon of worshipable gods
    /// These represent the "mortal-created" gods that echo the Old Gods' domains
    /// Players can worship these from the start - they provide blessings and can be sacrificed to
    ///
    /// LORE: When the Seven Old Gods fell silent after the Sundering, mortals created new
    /// gods to fill the void. These "New Gods" are pale reflections of the originals, powered
    /// by mortal belief rather than true divinity. Some scholars believe they are actually
    /// fragments of the Old Gods' power that escaped imprisonment...
    /// </summary>
    private void InitializeDefaultPantheon()
    {
        // The New Pantheon - mortal-created gods that reflect the Old Gods' domains
        // Each echoes an Old God: Solarius/Aurelion, Valorian/Maelketh, Amara/Veloura,
        // Judicar/Thorgrim, Umbrath/Noctura, Terran/Terravok, Arcanus/Manwe
        var defaultGods = new List<(string name, string domain, long goodness, long darkness, string description, string echoes)>
        {
            // Light and Truth - Echoes Aurelion, The Fading Light
            ("Solarius", "The Radiant", 10000, 0,
                "God of light, truth, and righteousness. Worshippers gain clarity and protection from darkness. " +
                "Some say Solarius is a fragment of Aurelion's fading light, kept alive by mortal faith.",
                "Aurelion"),

            // War and Honor - Echoes Maelketh, The Broken Blade
            ("Valorian", "The Ironwilled", 5000, 2000,
                "God of battle, honor, and strength. Favors warriors who fight with courage. " +
                "Unlike the mad war-god Maelketh, Valorian represents the honor that Maelketh lost.",
                "Maelketh"),

            // Love and Passion - Echoes Veloura, The Withered Heart
            ("Amara", "The Heartsworn", 8000, 1000,
                "Goddess of love, passion, and fertility. Blesses unions and protects lovers. " +
                "Born from what remains of Veloura's domain, Amara embodies love's hope rather than its sorrow.",
                "Veloura"),

            // Law and Justice - Echoes Thorgrim, The Hollow Judge
            ("Judicar", "The Balanced", 7000, 3000,
                "God of law, justice, and order. Punishes oathbreakers and rewards the righteous. " +
                "Where Thorgrim became tyranny, Judicar strives to remain true justice.",
                "Thorgrim"),

            // Shadow and Secrets - Echoes Noctura, The Shadow Weaver
            ("Umbrath", "The Veiled", 2000, 6000,
                "God of shadows, secrets, and the unseen. Favors thieves, assassins, and those who walk in darkness. " +
                "Some whisper that Umbrath IS Noctura, watching mortals through a different mask.",
                "Noctura"),

            // Earth and Endurance - Echoes Terravok, The Sleeping Mountain
            ("Terran", "The Unyielding", 6000, 1000,
                "God of earth, stone, and endurance. Grants fortitude and protection to the faithful. " +
                "When Terravok fell asleep beneath the world, Terran rose to carry his burden.",
                "Terravok"),

            // Death and Transition - No direct Old God echo, but fills a cosmic role
            ("Mortis", "The Final Gate", 3000, 7000,
                "God of death, endings, and rebirth. Neither good nor evil - all must pass through the gate. " +
                "Mortis claims no Old God as predecessor; death existed before even the gods.",
                "None"),

            // Magic and Knowledge - Echoes Manwe, The Creator
            ("Arcanus", "The All-Seeing", 5000, 5000,
                "God of magic, knowledge, and the arcane arts. Seeks truth through mystical means. " +
                "Scholars debate whether Arcanus holds a fragment of Manwe's infinite wisdom.",
                "Manwe"),

            // Nature and the Wild - Represents primal creation
            ("Sylvana", "The Wildmother", 9000, 500,
                "Goddess of nature, animals, and the wild places. Protects the natural order. " +
                "Sylvana rose when the Old Gods' war scarred the land, defender of what remains.",
                "None"),

            // Chaos and Change - Opposite of Order
            ("Discordia", "The Ever-Changing", 1000, 8000,
                "Goddess of chaos, change, and upheaval. Embraces those who reject the status quo. " +
                "Some say Discordia was born from the Sundering itself - chaos made divine.",
                "Sundering")
        };

        foreach (var (name, domain, goodness, darkness, description, echoes) in defaultGods)
        {
            if (!godsByName.ContainsKey(name))
            {
                var god = new God
                {
                    Name = name,
                    RealName = domain,
                    Id = $"PANTHEON_{name.ToUpper()}",
                    Level = random.Next(3, 7), // Level 3-6
                    Experience = random.Next(5000, 20000),
                    AI = GameConfig.GodAIComputer,
                    Age = random.Next(100, 1000),
                    Sex = name == "Amara" || name == "Sylvana" || name == "Discordia" ? 2 : 1,
                    Believers = random.Next(5, 50),
                    Deleted = false,
                    DeedsLeft = 99,
                    Darkness = darkness,
                    Goodness = goodness
                };

                // Store description and lore connections in properties
                god.Properties["Description"] = description;
                god.Properties["Domain"] = domain;
                god.Properties["IsPantheonGod"] = true;
                god.Properties["EchoesOldGod"] = echoes; // Which Old God this deity echoes

                gods.Add(god);
                godsByName[name] = god;
            }
        }

    }

    /// <summary>
    /// Search for gods by user name (Pascal God_Search function)
    /// </summary>
    public List<God> SearchGodsByUser(string userName)
    {
        return gods.Where(g => !g.Deleted && 
                              string.Equals(g.RealName, userName, StringComparison.OrdinalIgnoreCase))
                   .ToList();
    }
    
    /// <summary>
    /// Get god title by level (Pascal God_Title function)
    /// </summary>
    public static string GetGodTitle(int level)
    {
        if (level >= 1 && level <= GameConfig.MaxGodLevel)
        {
            return GameConfig.GodTitles[Math.Clamp(level - 1, 0, GameConfig.GodTitles.Length - 1)];
        }
        return "Lesser Spirit";
    }
    
    /// <summary>
    /// Count believers for a god (Pascal God_Believers function)
    /// </summary>
    public int CountBelievers(string godName, bool listThem = false)
    {
        if (!godsByName.ContainsKey(godName))
            return 0;
            
        var god = godsByName[godName];
        if (listThem)
        {
            // In Pascal this would display the list - here we just return count
            for (int i = 0; i < god.Disciples.Count; i++)
            {
            }
        }
        
        return god.Believers;
    }
    
    /// <summary>
    /// Select a god interactively (Pascal Select_A_God function)
    /// </summary>
    public God SelectGod(string excludeName = "", bool numbered = false)
    {
        var availableGods = gods.Where(g => g.IsActive() && g.Name != excludeName).ToList();
        
        if (availableGods.Count == 0)
            return null;
            
        // In a real implementation, this would show a menu
        // For now, return the first available god
        return availableGods.First();
    }
    
    /// <summary>
    /// Check if god is active (Pascal God_Active function)
    /// </summary>
    public bool IsGodActive(God god)
    {
        return god != null && god.IsActive();
    }
    
    /// <summary>
    /// Verify god exists (Pascal Verify_Gods_Existance function)
    /// </summary>
    public bool VerifyGodExists(string godName)
    {
        return godsByName.ContainsKey(godName) && godsByName[godName].IsActive();
    }
    
    /// <summary>
    /// Get random active god (Pascal Get_Random_God function)
    /// </summary>
    public God GetRandomGod()
    {
        var activeGods = gods.Where(g => g.IsActive()).ToList();
        if (activeGods.Count == 0)
            return null;
            
        return activeGods[random.Next(activeGods.Count)];
    }
    
    /// <summary>
    /// Count how many believers a god has (Pascal How_Many_Believers function)
    /// </summary>
    public int CountGodBelievers(God god)
    {
        return god?.Believers ?? 0;
    }
    
    /// <summary>
    /// Load god by name (Pascal Load_God_By_Name function)
    /// </summary>
    public God LoadGodByName(string godName)
    {
        return godsByName.ContainsKey(godName) ? godsByName[godName] : null;
    }
    
    /// <summary>
    /// Check if player has a god (Pascal Player_Has_A_God function)
    /// </summary>
    public bool PlayerHasGod(string playerName)
    {
        return playerGods.ContainsKey(playerName) && 
               !string.IsNullOrEmpty(playerGods[playerName]) &&
               VerifyGodExists(playerGods[playerName]);
    }
    
    /// <summary>
    /// Get player's god name
    /// </summary>
    public string GetPlayerGod(string playerName)
    {
        return playerGods.ContainsKey(playerName) ? playerGods[playerName] : "";
    }
    
    /// <summary>
    /// Set player's god
    /// </summary>
    public void SetPlayerGod(string playerName, string godName)
    {
        string oldGod = playerGods.ContainsKey(playerName) ? playerGods[playerName] : "none";

        if (string.IsNullOrEmpty(godName))
        {
            // Remove god
            if (playerGods.ContainsKey(playerName))
            {
                var oldGodName = playerGods[playerName];
                if (godsByName.ContainsKey(oldGodName))
                {
                    godsByName[oldGodName].RemoveBeliever(playerName);
                }
                playerGods.Remove(playerName);
            }
            UsurperRemake.Systems.DebugLogger.Instance.LogInfo("TEMPLE", $"{playerName} abandoned their god (was: {oldGod})");
        }
        else
        {
            // Remove from old god first
            if (playerGods.ContainsKey(playerName))
            {
                var oldGodName = playerGods[playerName];
                if (godsByName.ContainsKey(oldGodName))
                {
                    godsByName[oldGodName].RemoveBeliever(playerName);
                }
            }

            // Add to new god
            playerGods[playerName] = godName;
            if (godsByName.ContainsKey(godName))
            {
                godsByName[godName].AddBeliever(playerName);
            }
            UsurperRemake.Systems.DebugLogger.Instance.LogInfo("TEMPLE", $"{playerName} now worships {godName} (was: {oldGod})");
        }
    }
    
    /// <summary>
    /// Calculate sacrifice gold return (Pascal Sacrifice_Gold_Return function)
    /// </summary>
    public static long CalculateSacrificeGoldReturn(long goldAmount)
    {
        return God.CalculateSacrificeReturn(goldAmount);
    }
    
    /// <summary>
    /// Become a god (Pascal Become_God procedure)
    /// </summary>
    public God BecomeGod(string userName, string alias, string playerId, int playerSex, long playerDarkness, long playerGoodness)
    {
        // Check if name is already taken
        if (godsByName.ContainsKey(alias) || 
            alias.Equals("SYSOP", StringComparison.OrdinalIgnoreCase) ||
            alias.Equals(GameConfig.SupremeCreatorName, StringComparison.OrdinalIgnoreCase))
        {
            return null; // Name already taken
        }
        
        // Create new god
        var newGod = new God(userName, alias, playerId, playerSex, playerDarkness, playerGoodness);
        
        // Find empty slot or add to end
        var emptySlot = gods.FindIndex(g => g.Deleted);
        if (emptySlot >= 0)
        {
            newGod.RecordNumber = emptySlot + 1;
            gods[emptySlot] = newGod;
        }
        else
        {
            newGod.RecordNumber = gods.Count + 1;
            gods.Add(newGod);
        }
        
        godsByName[newGod.Name] = newGod;
        
        // In Pascal, this would send news and notifications
        
        return newGod;
    }
    
    /// <summary>
    /// God maintenance - run daily (Pascal God_Maintenance procedure)
    /// </summary>
    public void RunGodMaintenance()
    {
        if ((DateTime.Now - lastMaintenance).TotalHours < GameConfig.GodMaintenanceInterval)
            return;
            
        foreach (var god in gods.Where(g => g.IsActive()))
        {
            god.ResetDailyDeeds();
        }
        
        lastMaintenance = DateTime.Now;
    }
    
    /// <summary>
    /// Get god status display (Pascal God_Status procedure)
    /// </summary>
    public string GetGodStatus(God god)
    {
        if (god == null || !god.IsActive())
            return "God not found or inactive.";
            
        return god.GetStatusDisplay();
    }
    
    /// <summary>
    /// List all gods (Pascal List_Gods procedure)
    /// </summary>
    public List<God> ListGods(bool numbered = false)
    {
        var activeGods = gods.Where(g => g.IsActive()).OrderByDescending(g => g.Experience).ToList();
        
        if (numbered)
        {
            for (int i = 0; i < activeGods.Count; i++)
            {
                var god = activeGods[i];
            }
        }
        
        return activeGods;
    }
    
    /// <summary>
    /// Inform disciples of god events (Pascal Inform_Disciples procedure)
    /// </summary>
    public void InformDisciples(God god, string header, params string[] messages)
    {
        if (god == null || !god.IsActive())
            return;
            
        foreach (var disciple in god.Disciples)
        {
            // In Pascal, this would send mail to each disciple
            // For now, just log the message
            foreach (var message in messages.Where(m => !string.IsNullOrEmpty(m)))
            {
            }
        }
    }
    
    /// <summary>
    /// Process gold sacrifice to god
    /// </summary>
    public long ProcessGoldSacrifice(string godName, long goldAmount, string playerName)
    {
        if (!godsByName.ContainsKey(godName))
            return 0;
            
        var god = godsByName[godName];
        if (!god.IsActive())
            return 0;
            
        var powerGained = CalculateSacrificeGoldReturn(goldAmount);
        god.IncreaseExperience(powerGained);
        
        // Inform god if online (in Pascal this would be a broadcast)
        
        return powerGained;
    }
    
    /// <summary>
    /// Process altar desecration
    /// </summary>
    public void ProcessAltarDesecration(string godName, string playerName)
    {
        if (!godsByName.ContainsKey(godName))
            return;
            
        var god = godsByName[godName];
        if (!god.IsActive())
            return;
            
        // In Pascal, this would reduce god power and inform disciples
        
        // Inform disciples
        InformDisciples(god, "ALTAR DESECRATED!", 
            $"{playerName} desecrated the altar of {godName}, your god!",
            "You must protect your master!");
    }
    
    /// <summary>
    /// Divine intervention - help prisoner escape
    /// </summary>
    public bool DivineInterventionPrisonEscape(God god, string prisonerName)
    {
        if (god == null || !god.IsActive() || !god.UseDeed())
            return false;
            
        // In Pascal, this would actually free the prisoner
        
        return true;
    }
    
    /// <summary>
    /// Divine intervention - bless mortal
    /// </summary>
    public bool DivineInterventionBless(God god, string mortalName)
    {
        if (god == null || !god.IsActive() || !god.UseDeed())
            return false;
            
        // In Pascal, this would provide actual blessing effects
        
        return true;
    }
    
    /// <summary>
    /// Divine intervention - curse mortal
    /// </summary>
    public bool DivineInterventionCurse(God god, string mortalName)
    {
        if (god == null || !god.IsActive() || !god.UseDeed())
            return false;
            
        // In Pascal, this would provide actual curse effects
        
        return true;
    }
    
    /// <summary>
    /// Get all gods
    /// </summary>
    public List<God> GetAllGods()
    {
        return gods.ToList();
    }
    
    /// <summary>
    /// Get active gods
    /// </summary>
    public List<God> GetActiveGods()
    {
        return gods.Where(g => g.IsActive()).ToList();
    }
    
    /// <summary>
    /// Get god by name
    /// </summary>
    public God GetGod(string godName)
    {
        return godsByName.ContainsKey(godName) ? godsByName[godName] : null;
    }
    
    /// <summary>
    /// Add god to system
    /// </summary>
    public void AddGod(God god)
    {
        if (god != null && god.IsValid() && !godsByName.ContainsKey(god.Name))
        {
            gods.Add(god);
            godsByName[god.Name] = god;
        }
    }
    
    /// <summary>
    /// Remove god from system
    /// </summary>
    public void RemoveGod(string godName)
    {
        if (godsByName.ContainsKey(godName))
        {
            var god = godsByName[godName];
            god.Deleted = true;
            
            // Remove all believers
            foreach (var believer in god.Disciples.ToList())
            {
                SetPlayerGod(believer, "");
            }
        }
    }
    
    /// <summary>
    /// Get god statistics
    /// </summary>
    public Dictionary<string, object> GetGodStatistics()
    {
        var activeGods = GetActiveGods();
        var totalBelievers = activeGods.Sum(g => g.Believers);
        var averageLevel = activeGods.Count > 0 ? activeGods.Average(g => g.Level) : 0;
        var totalExperience = activeGods.Sum(g => g.Experience);
        
        return new Dictionary<string, object>
        {
            ["TotalGods"] = activeGods.Count,
            ["TotalBelievers"] = totalBelievers,
            ["AverageLevel"] = Math.Round(averageLevel, 2),
            ["TotalExperience"] = totalExperience,
            ["MostPowerfulGod"] = activeGods.OrderByDescending(g => g.Experience).FirstOrDefault()?.Name ?? "None",
            ["MostPopularGod"] = activeGods.OrderByDescending(g => g.Believers).FirstOrDefault()?.Name ?? "None"
        };
    }
    
    /// <summary>
    /// Convert to dictionary for serialization
    /// </summary>
    public Dictionary<string, object> ToDictionary()
    {
        return new Dictionary<string, object>
        {
            ["Gods"] = gods.Select(g => g.ToDictionary()).ToArray(),
            ["PlayerGods"] = playerGods,
            ["LastMaintenance"] = lastMaintenance.ToBinary()
        };
    }
    
    /// <summary>
    /// Load from dictionary
    /// </summary>
    public static GodSystem FromDictionary(Dictionary<string, object> dict)
    {
        var system = new GodSystem();
        
        if (dict.ContainsKey("Gods"))
        {
            var godDicts = (object[])dict["Gods"];
            foreach (var godDict in godDicts)
            {
                var god = God.FromDictionary((Dictionary<string, object>)godDict);
                system.gods.Add(god);
                system.godsByName[god.Name] = god;
            }
        }
        
        if (dict.ContainsKey("PlayerGods"))
        {
            system.playerGods = (Dictionary<string, string>)dict["PlayerGods"];
        }
        
        if (dict.ContainsKey("LastMaintenance"))
        {
            system.lastMaintenance = DateTime.FromBinary(Convert.ToInt64(dict["LastMaintenance"]));
        }
        
        return system;
    }
} 
