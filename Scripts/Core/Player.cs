using UsurperRemake.Utils;
using UsurperRemake.Systems;
using System;
using System.Collections.Generic;
using System.Threading.Tasks;

public class Player : Character
{
    // v1.1.1: LastLogin, TurnsRemaining, PrisonsLeft, ExecuteLeft, MarryActions, WolfFeed,
    // RoyalAdoptions, DaysInPower and CompactMode used to be redeclared here with `new`. The
    // save path reads a Character reference and the game logic holds a Player reference, so
    // each field existed twice on one object and the copy that was saved was never the copy
    // that was played. They now live on Character only.
    public new string RealName { get; set; } = ""; // Player's real name vs character name
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
    public string ColorScheme { get; set; } = "classic";
    
    // Special player abilities/unlocks
    public List<string> UnlockedAbilities { get; set; } = new List<string>();
    public new Dictionary<string, bool> Achievements { get; set; } = new Dictionary<string, bool>();
    
    // Status tracking
    public bool IsOnline { get; set; } = false;
    public DateTime SessionStart { get; set; } = DateTime.Now;
    public int TurnsThisSession { get; set; } = 0;
    
    // Missing properties for API compatibility
    
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
    
    private void UpdateAchievements()
    {
        CheckAchievement("first_level", Level >= 2, "player.achievement_first_level");
        CheckAchievement("experienced", Level >= 10, "player.achievement_experienced");
        CheckAchievement("veteran", Level >= 25, "player.achievement_veteran");
        CheckAchievement("master", Level >= 50, "player.achievement_master");
        CheckAchievement("legendary", Level >= 100, "player.achievement_legendary");
        
        CheckAchievement("wealthy", Gold >= 10000, "player.achievement_wealthy");
        CheckAchievement("rich", Gold >= 50000, "player.achievement_rich");
        CheckAchievement("tycoon", Gold >= 100000, "player.achievement_tycoon");
        
        CheckAchievement("monster_hunter", MonsterKills >= 100, "player.achievement_monster_hunter");
        CheckAchievement("monster_slayer", MonsterKills >= 500, "player.achievement_monster_slayer");
        CheckAchievement("monster_bane", MonsterKills >= 1000, "player.achievement_monster_bane");
        
        CheckAchievement("pvp_warrior", PvPWins >= 10, "player.achievement_pvp_warrior");
        CheckAchievement("pvp_champion", PvPWins >= 50, "player.achievement_pvp_champion");
        CheckAchievement("pvp_legend", PvPWins >= 100, "player.achievement_pvp_legend");
        
        CheckAchievement("ruler", IsRuler, "player.achievement_ruler");
        CheckAchievement("persistent_ruler", TimesRuler >= 5, "player.achievement_persistent_ruler");
        
        CheckAchievement("deep_explorer", DungeonLevel >= 10, "player.achievement_deep_explorer");
        CheckAchievement("depth_seeker", DungeonLevel >= 15, "player.achievement_depth_seeker");
        CheckAchievement("abyss_walker", DungeonLevel >= 20, "player.achievement_abyss_walker");
    }
    
    /// <summary>v1.2.5: descriptionKey is the Loc key of the line shown; the id is what Achievements keeps.</summary>
    private void CheckAchievement(string achievementId, bool condition, string descriptionKey)
    {
        if (condition && !Achievements.ContainsKey(achievementId))
        {
            Achievements[achievementId] = true;
            GameEngine.Instance?.Terminal?.WriteLine(Loc.Get("player.achievement_unlocked", Loc.Get(descriptionKey)), "bright_magenta");
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
            
            GameEngine.Instance?.Terminal?.WriteLine(Loc.Get("player.death_penalty", expLoss, goldLoss), "red");
        }
        
        // Check if permadeath is enabled
        if (config.PermaDeath)
        {
            GameEngine.Instance?.Terminal?.WriteLine(Loc.Get("player.permadeath_deleted"), "bright_red");
            // This would trigger character deletion
        }
        else
        {
            // Respawn with minimal health
            CurrentHP = 1;
            CurrentLocation = "Temple"; // Respawn at temple
            GameEngine.Instance?.Terminal?.WriteLine(Loc.Get("player.resurrected_temple"), "yellow");
        }
        
        UpdateAchievements();
    }
    
    public int GetPvPRating()
    {
        if (PvPWins + PvPLosses == 0) return 1000; // Starting rating
        
        var winRate = (float)PvPWins / (PvPWins + PvPLosses);
        var baseRating = 1000 + (int)(winRate * 500);
        var levelBonus = Level * 5;
        
        return baseRating + levelBonus;
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
