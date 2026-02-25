using UsurperRemake.Utils;
using System;
using System.Collections.Generic;
using System.Threading.Tasks;

public class Player : Character
{
    public new string RealName { get; set; } = ""; // Player's real name vs character name
    public new DateTime LastLogin { get; set; }
    public DateTime AccountCreated { get; set; }
    public int TotalLogins { get; set; }
    public TimeSpan TotalPlayTime { get; set; }
    
    // Character reference for compatibility with existing code
    public Character Character => this;  // Player IS a Character, so return self
    
    // Player-specific stats
    public int PvPWins { get; set; }
    public int PvPLosses { get; set; }
    public int MonsterKills { get; set; }
    public int Deaths { get; set; }
    public int TimesRuler { get; set; }
    public int DungeonLevel { get; set; } = 1;

    // Player preferences
    public bool AutoFight { get; set; } = false;
    public bool CompactMode { get; set; } = false;
    public string ColorScheme { get; set; } = "classic";
    
    // Special player abilities/unlocks
    public List<string> UnlockedAbilities { get; set; } = new List<string>();
    public new Dictionary<string, bool> Achievements { get; set; } = new Dictionary<string, bool>();
    
    // Status tracking
    public bool IsOnline { get; set; } = false;
    public DateTime SessionStart { get; set; } = DateTime.Now;
    public int TurnsThisSession { get; set; } = 0;
    
    // Missing properties for API compatibility
    public new int TurnsRemaining { get; set; } = GameConfig.TurnsPerDay;
    public new int PrisonsLeft { get; set; } = 0;
    public new int ExecuteLeft { get; set; } = 0;
    public new int MarryActions { get; set; } = 5;
    public new int WolfFeed { get; set; } = 0;
    public new int RoyalAdoptions { get; set; } = 0;
    public new int DaysInPower { get; set; } = 0;
    
    // Additional missing properties for API compatibility
    public override string CurrentLocation { get; set; } = "Main Street";
    
    public bool IsRuler
    {
        get => King;
        set => King = value;
    }
    
    // Add SaveData and LevelUpTracker stubs for compatibility
    public object? SaveData { get; set; } = null;
    public object? LevelUpTracker { get; set; } = null;
    
    public Player() : base()
    {
        AI = CharacterAI.Human;
        // Terminal = null; // Terminal is read-only, use different approach
        SaveData = new PlayerSaveData();
        LevelUpTracker = new PlayerLevelUpTracker();
        AccountCreated = DateTime.Now;
        LastLogin = DateTime.Now;
        TotalLogins = 1;

        // Initialize with starting resources - use safe defaults if config unavailable
        var config = ConfigManager.GetConfig();
        Gold = config?.StartingGold ?? GameConfig.DefaultStartingGold;
        TurnsRemaining = config?.StartingTurns ?? GameConfig.TurnsPerDay;
    }

    public Player(string realName, string characterName, CharacterClass charClass) : base()
    {
        RealName = realName;
        AccountCreated = DateTime.Now;
        LastLogin = DateTime.Now;
        TotalLogins = 1;

        // Initialize with starting resources - use safe defaults if config unavailable
        var config = ConfigManager.GetConfig();
        Gold = config?.StartingGold ?? GameConfig.DefaultStartingGold;
        TurnsRemaining = config?.StartingTurns ?? GameConfig.TurnsPerDay;
    }
    
    private void CheckForNewAbilities()
    {
        var classData = CharacterDataManager.GetClassData(Class);
        if (classData != null)
        {
            foreach (var ability in classData.SpecialAbilities)
            {
                var requiredLevel = GetAbilityRequiredLevel(ability);
                if (Level >= requiredLevel && !UnlockedAbilities.Contains(ability))
                {
                    UnlockedAbilities.Add(ability);
                    GameEngine.Instance?.Terminal?.WriteLine($"You have learned a new ability: {ability}!", "bright_cyan");
                }
            }
        }
    }
    
    private int GetAbilityRequiredLevel(string ability)
    {
        // Define level requirements for abilities
        return ability switch
        {
            "power_attack" => 3,
            "defend" => 5,
            "berserker_rage" => 10,
            "sneak_attack" => 2,
            "steal" => 4,
            "hide_in_shadows" => 8,
            "heal" => 1,
            "turn_undead" => 3,
            "bless" => 6,
            "divine_favor" => 12,
            "magic_missile" => 1,
            "fireball" => 5,
            "lightning_bolt" => 8,
            "shield" => 3,
            _ => 1
        };
    }
    
    private void UpdateAchievements()
    {
        CheckAchievement("first_level", Level >= 2, "Reached level 2");
        CheckAchievement("experienced", Level >= 10, "Reached level 10");
        CheckAchievement("veteran", Level >= 25, "Reached level 25");
        CheckAchievement("master", Level >= 50, "Reached level 50");
        CheckAchievement("legendary", Level >= 100, "Reached maximum level");
        
        CheckAchievement("wealthy", Gold >= 10000, "Accumulated 10,000 gold");
        CheckAchievement("rich", Gold >= 50000, "Accumulated 50,000 gold");
        CheckAchievement("tycoon", Gold >= 100000, "Accumulated 100,000 gold");
        
        CheckAchievement("monster_hunter", MonsterKills >= 100, "Killed 100 monsters");
        CheckAchievement("monster_slayer", MonsterKills >= 500, "Killed 500 monsters");
        CheckAchievement("monster_bane", MonsterKills >= 1000, "Killed 1000 monsters");
        
        CheckAchievement("pvp_warrior", PvPWins >= 10, "Won 10 PvP battles");
        CheckAchievement("pvp_champion", PvPWins >= 50, "Won 50 PvP battles");
        CheckAchievement("pvp_legend", PvPWins >= 100, "Won 100 PvP battles");
        
        CheckAchievement("ruler", IsRuler, "Became the ruler");
        CheckAchievement("persistent_ruler", TimesRuler >= 5, "Became ruler 5 times");
        
        CheckAchievement("deep_explorer", DungeonLevel >= 10, "Reached dungeon level 10");
        CheckAchievement("depth_seeker", DungeonLevel >= 15, "Reached dungeon level 15");
        CheckAchievement("abyss_walker", DungeonLevel >= 20, "Reached the deepest level");
    }
    
    private void CheckAchievement(string achievementId, bool condition, string description)
    {
        if (condition && !Achievements.ContainsKey(achievementId))
        {
            Achievements[achievementId] = true;
            GameEngine.Instance?.Terminal?.WriteLine($"Achievement Unlocked: {description}!", "bright_magenta");
        }
    }
    
    public void OnLogin()
    {
        TotalLogins++;
        var timeSinceLastLogin = DateTime.Now - LastLogin;
        LastLogin = DateTime.Now;
        
        // Daily reset bonus
        if (timeSinceLastLogin.TotalHours >= 20) // Allow daily reset after 20 hours
        {
            var config = ConfigManager.GetConfig();
            TurnsRemaining = config.StartingTurns;
            GameEngine.Instance?.Terminal?.WriteLine("Your turns have been restored for the new day!", "bright_green");
        }
    }
    
    public void OnLogout()
    {
        // Calculate session time
        // This would be called when saving the game
    }
    
    public void Die()
    {
        Deaths++;
        CurrentHP = 0;
        
        // Death penalties
        var config = ConfigManager.GetConfig();
        if (config.DeathPenalty)
        {
            var expLoss = (long)(Experience * config.DeathPenaltyXP);
            Experience = Math.Max(0, Experience - expLoss);
            
            var goldLoss = Gold / 10; // Lose 10% of gold
            Gold = Math.Max(0, Gold - goldLoss);
            
            GameEngine.Instance?.Terminal?.WriteLine($"Death penalty: Lost {expLoss} experience and {goldLoss} gold!", "red");
        }
        
        // Check if permadeath is enabled
        if (config.PermaDeath)
        {
            GameEngine.Instance?.Terminal?.WriteLine("PERMADEATH: Your character has been permanently deleted!", "bright_red");
            // This would trigger character deletion
        }
        else
        {
            // Respawn with minimal health
            CurrentHP = 1;
            CurrentLocation = "Temple"; // Respawn at temple
            GameEngine.Instance?.Terminal?.WriteLine("You have been resurrected at the temple.", "yellow");
        }
        
        UpdateAchievements();
    }
    
    public void BecomeRuler()
    {
        IsRuler = true;
        TimesRuler++;
        GameEngine.Instance?.Terminal?.WriteLine("Congratulations! You are now the ruler of the realm!", "bright_yellow");
        UpdateAchievements();
    }
    
    public void LoseRulership()
    {
        IsRuler = false;
        GameEngine.Instance?.Terminal?.WriteLine("You are no longer the ruler.", "yellow");
    }
    
    public int GetPvPRating()
    {
        if (PvPWins + PvPLosses == 0) return 1000; // Starting rating
        
        var winRate = (float)PvPWins / (PvPWins + PvPLosses);
        var baseRating = 1000 + (int)(winRate * 500);
        var levelBonus = Level * 5;
        
        return baseRating + levelBonus;
    }
    
    public string GetTitle()
    {
        if (IsRuler) return "Supreme Ruler";

        // If player has a noble title from the king, use that with their level title
        string levelTitle = Level switch
        {
            >= 90 => "Legendary Hero",
            >= 80 => "Epic Champion",
            >= 70 => "Mighty Warrior",
            >= 60 => "Seasoned Veteran",
            >= 50 => "Skilled Adventurer",
            >= 40 => "Experienced Fighter",
            >= 30 => "Capable Warrior",
            >= 20 => "Brave Defender",
            >= 10 => "Aspiring Hero",
            >= 5 => "Novice Adventurer",
            _ => "Peasant"
        };

        // Prepend noble title if present (e.g., "Sir Legendary Hero")
        if (!string.IsNullOrEmpty(NobleTitle))
        {
            return $"{NobleTitle} {levelTitle}";
        }

        return levelTitle;
    }
    
    public string GetDisplayInfo()
    {
        var title = GetTitle();
        return $"{Name} the {title} (Level {Level} {Class})";
    }
    
    public void ShowPlayerStats(TerminalEmulator terminal)
    {
        terminal.WriteLine("", "white");
        terminal.WriteLine("╔═══════════════════ PLAYER STATISTICS ═══════════════════╗", "bright_blue");
        terminal.WriteLine($"║ Name: {Name,-20} Class: {Class,-15} ║", "white");
        terminal.WriteLine($"║ Level: {Level,-19} Title: {GetTitle(),-15} ║", "white");
        terminal.WriteLine($"║ Experience: {Experience,-25} Gold: {Gold,-10} ║", "white");
        terminal.WriteLine($"║ HP: {CurrentHP}/{MaxHP,-15} Mana: {CurrentMana}/{MaxMana,-12} ║", "white");
        terminal.WriteLine("║                                                         ║", "white");
        terminal.WriteLine($"║ Strength: {Strength,-8} Dexterity: {Dexterity,-8} Constitution: {Constitution,-5} ║", "white");
        terminal.WriteLine($"║ Intelligence: {Intelligence,-4} Wisdom: {Wisdom,-8} Charisma: {Charisma,-8} ║", "white");
        terminal.WriteLine("║                                                         ║", "white");
        terminal.WriteLine($"║ PvP Record: {PvPWins} wins, {PvPLosses} losses                      ║", "white");
        terminal.WriteLine($"║ Monster Kills: {MonsterKills,-8} Deaths: {Deaths,-12}             ║", "white");
        terminal.WriteLine($"║ Times Ruler: {TimesRuler,-10} Deepest Level: {DungeonLevel,-8}       ║", "white");
        terminal.WriteLine("╚═════════════════════════════════════════════════════════╝", "bright_blue");
    }
    
    // Additional missing methods for API compatibility
    public void SendMessage(string message)
    {
        // Terminal?.WriteLine(message); // Terminal is read-only, use different approach
    }
    
    public Task<string> GetInput()
    {
        return Task.FromResult("");
    }
} 
