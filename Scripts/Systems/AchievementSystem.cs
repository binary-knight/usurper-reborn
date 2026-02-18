using System;
using System.Collections.Generic;
using System.Linq;
using UsurperRemake.Systems;

/// <summary>
/// Achievement categories for organization
/// </summary>
public enum AchievementCategory
{
    Combat,
    Exploration,
    Economy,
    Social,
    Progression,
    Challenge,
    Secret
}

/// <summary>
/// Achievement rarity/difficulty tier
/// </summary>
public enum AchievementTier
{
    Bronze,      // Easy achievements
    Silver,      // Moderate achievements
    Gold,        // Difficult achievements
    Platinum,    // Very difficult achievements
    Diamond      // Extremely rare achievements
}

/// <summary>
/// Represents a single achievement definition
/// </summary>
public class Achievement
{
    public string Id { get; set; } = "";
    public string Name { get; set; } = "";
    public string Description { get; set; } = "";
    public string SecretHint { get; set; } = "";  // Shown instead of description if secret
    public AchievementCategory Category { get; set; }
    public AchievementTier Tier { get; set; }
    public bool IsSecret { get; set; }
    public int PointValue { get; set; }

    // Reward for unlocking
    public long GoldReward { get; set; }
    public long ExperienceReward { get; set; }
    public string? UnlockMessage { get; set; }

    /// <summary>
    /// Get display color based on tier
    /// </summary>
    public string GetTierColor() => Tier switch
    {
        AchievementTier.Bronze => "yellow",
        AchievementTier.Silver => "white",
        AchievementTier.Gold => "bright_yellow",
        AchievementTier.Platinum => "bright_cyan",
        AchievementTier.Diamond => "bright_magenta",
        _ => "white"
    };

    /// <summary>
    /// Get tier symbol for display
    /// </summary>
    public string GetTierSymbol() => Tier switch
    {
        AchievementTier.Bronze => "[B]",
        AchievementTier.Silver => "[S]",
        AchievementTier.Gold => "[G]",
        AchievementTier.Platinum => "[P]",
        AchievementTier.Diamond => "[D]",
        _ => "[ ]"
    };
}

/// <summary>
/// Player's achievement progress and unlocks
/// </summary>
public class PlayerAchievements
{
    /// <summary>
    /// Set of unlocked achievement IDs
    /// </summary>
    public HashSet<string> UnlockedAchievements { get; set; } = new();

    /// <summary>
    /// When each achievement was unlocked
    /// </summary>
    public Dictionary<string, DateTime> UnlockDates { get; set; } = new();

    /// <summary>
    /// Total achievement points earned
    /// </summary>
    public int TotalPoints => UnlockedAchievements
        .Select(id => AchievementSystem.GetAchievement(id))
        .Where(a => a != null)
        .Sum(a => a!.PointValue);

    /// <summary>
    /// Check if an achievement is unlocked
    /// </summary>
    public bool IsUnlocked(string achievementId) => UnlockedAchievements.Contains(achievementId);

    /// <summary>
    /// Unlock an achievement (returns true if newly unlocked)
    /// </summary>
    public bool Unlock(string achievementId)
    {
        if (UnlockedAchievements.Contains(achievementId))
            return false;

        UnlockedAchievements.Add(achievementId);
        UnlockDates[achievementId] = DateTime.Now;
        return true;
    }

    /// <summary>
    /// Get unlock date for an achievement
    /// </summary>
    public DateTime? GetUnlockDate(string achievementId)
    {
        return UnlockDates.TryGetValue(achievementId, out var date) ? date : null;
    }

    /// <summary>
    /// Get count of unlocked achievements
    /// </summary>
    public int UnlockedCount => UnlockedAchievements.Count;

    /// <summary>
    /// Get completion percentage
    /// </summary>
    public double CompletionPercentage =>
        AchievementSystem.TotalAchievements > 0
            ? (double)UnlockedCount / AchievementSystem.TotalAchievements * 100
            : 0;
}

/// <summary>
/// Central achievement management system
/// </summary>
public static class AchievementSystem
{
    private static readonly Dictionary<string, Achievement> _achievements = new();
    private static bool _initialized = false;

    /// <summary>
    /// Pending achievements to show to player
    /// </summary>
    public static Queue<Achievement> PendingNotifications { get; } = new();

    /// <summary>
    /// Total number of achievements
    /// </summary>
    public static int TotalAchievements => _achievements.Count;

    /// <summary>
    /// Initialize all achievements
    /// </summary>
    public static void Initialize()
    {
        if (_initialized) return;
        _initialized = true;

        // ============ COMBAT ACHIEVEMENTS ============

        // First kills
        Register(new Achievement
        {
            Id = "first_blood",
            Name = "First Blood",
            Description = "Defeat your first monster",
            Category = AchievementCategory.Combat,
            Tier = AchievementTier.Bronze,
            PointValue = 5,
            GoldReward = 50,
            UnlockMessage = "Your journey as a warrior begins!"
        });

        Register(new Achievement
        {
            Id = "monster_slayer_10",
            Name = "Monster Slayer",
            Description = "Defeat 10 monsters",
            Category = AchievementCategory.Combat,
            Tier = AchievementTier.Bronze,
            PointValue = 10,
            GoldReward = 100
        });

        Register(new Achievement
        {
            Id = "monster_slayer_100",
            Name = "Veteran Hunter",
            Description = "Defeat 100 monsters",
            Category = AchievementCategory.Combat,
            Tier = AchievementTier.Silver,
            PointValue = 25,
            GoldReward = 500
        });

        Register(new Achievement
        {
            Id = "monster_slayer_500",
            Name = "Monster Bane",
            Description = "Defeat 500 monsters",
            Category = AchievementCategory.Combat,
            Tier = AchievementTier.Gold,
            PointValue = 50,
            GoldReward = 2500
        });

        Register(new Achievement
        {
            Id = "monster_slayer_1000",
            Name = "Legendary Slayer",
            Description = "Defeat 1,000 monsters",
            Category = AchievementCategory.Combat,
            Tier = AchievementTier.Platinum,
            PointValue = 100,
            GoldReward = 10000
        });

        Register(new Achievement
        {
            Id = "boss_killer",
            Name = "Boss Killer",
            Description = "Defeat your first boss monster",
            Category = AchievementCategory.Combat,
            Tier = AchievementTier.Silver,
            PointValue = 20,
            GoldReward = 500,
            UnlockMessage = "The bigger they are..."
        });

        Register(new Achievement
        {
            Id = "boss_slayer_10",
            Name = "Boss Hunter",
            Description = "Defeat 10 boss monsters",
            Category = AchievementCategory.Combat,
            Tier = AchievementTier.Gold,
            PointValue = 50,
            GoldReward = 2000
        });

        Register(new Achievement
        {
            Id = "unique_killer",
            Name = "Unique Encounter",
            Description = "Defeat a unique monster",
            Category = AchievementCategory.Combat,
            Tier = AchievementTier.Gold,
            PointValue = 30,
            GoldReward = 1000,
            UnlockMessage = "A rare and dangerous foe has fallen!"
        });

        Register(new Achievement
        {
            Id = "critical_master",
            Name = "Critical Master",
            Description = "Land 100 critical hits",
            Category = AchievementCategory.Combat,
            Tier = AchievementTier.Silver,
            PointValue = 25,
            GoldReward = 500
        });

        Register(new Achievement
        {
            Id = "damage_dealer_10000",
            Name = "Damage Dealer",
            Description = "Deal 10,000 total damage",
            Category = AchievementCategory.Combat,
            Tier = AchievementTier.Silver,
            PointValue = 20,
            GoldReward = 300
        });

        Register(new Achievement
        {
            Id = "damage_dealer_100000",
            Name = "Destroyer",
            Description = "Deal 100,000 total damage",
            Category = AchievementCategory.Combat,
            Tier = AchievementTier.Gold,
            PointValue = 50,
            GoldReward = 2000
        });

        Register(new Achievement
        {
            Id = "survivor",
            Name = "Survivor",
            Description = "Win a combat with less than 10% HP remaining",
            Category = AchievementCategory.Combat,
            Tier = AchievementTier.Silver,
            PointValue = 25,
            GoldReward = 500,
            UnlockMessage = "That was too close!"
        });

        Register(new Achievement
        {
            Id = "flawless_victory",
            Name = "Flawless Victory",
            Description = "Win a combat without taking any damage",
            Category = AchievementCategory.Combat,
            Tier = AchievementTier.Silver,
            PointValue = 30,
            GoldReward = 750
        });

        Register(new Achievement
        {
            Id = "pvp_victor",
            Name = "PvP Victor",
            Description = "Win your first player vs player battle",
            Category = AchievementCategory.Combat,
            Tier = AchievementTier.Silver,
            PointValue = 25,
            GoldReward = 500
        });

        Register(new Achievement
        {
            Id = "pvp_veteran",
            Name = "Arena Veteran",
            Description = "Win 10 player vs player battles in the Arena",
            Category = AchievementCategory.Combat,
            Tier = AchievementTier.Gold,
            PointValue = 50,
            GoldReward = 2000
        });

        Register(new Achievement
        {
            Id = "pvp_champion",
            Name = "PvP Champion",
            Description = "Win 50 player vs player battles",
            Category = AchievementCategory.Combat,
            Tier = AchievementTier.Platinum,
            PointValue = 100,
            GoldReward = 5000
        });

        Register(new Achievement
        {
            Id = "gauntlet_champion",
            Name = "Gauntlet Champion",
            Description = "Survived all 10 waves of The Gauntlet",
            Category = AchievementCategory.Combat,
            Tier = AchievementTier.Gold,
            PointValue = 50,
            GoldReward = 250
        });

        // ============ PROGRESSION ACHIEVEMENTS ============

        Register(new Achievement
        {
            Id = "level_5",
            Name = "Adventurer",
            Description = "Reach level 5",
            Category = AchievementCategory.Progression,
            Tier = AchievementTier.Bronze,
            PointValue = 10,
            ExperienceReward = 100
        });

        Register(new Achievement
        {
            Id = "level_10",
            Name = "Seasoned",
            Description = "Reach level 10",
            Category = AchievementCategory.Progression,
            Tier = AchievementTier.Bronze,
            PointValue = 15,
            ExperienceReward = 500
        });

        Register(new Achievement
        {
            Id = "level_25",
            Name = "Veteran",
            Description = "Reach level 25",
            Category = AchievementCategory.Progression,
            Tier = AchievementTier.Silver,
            PointValue = 30,
            ExperienceReward = 2000
        });

        Register(new Achievement
        {
            Id = "level_50",
            Name = "Elite",
            Description = "Reach level 50",
            Category = AchievementCategory.Progression,
            Tier = AchievementTier.Gold,
            PointValue = 50,
            ExperienceReward = 10000
        });

        Register(new Achievement
        {
            Id = "level_75",
            Name = "Master",
            Description = "Reach level 75",
            Category = AchievementCategory.Progression,
            Tier = AchievementTier.Platinum,
            PointValue = 75,
            ExperienceReward = 25000
        });

        Register(new Achievement
        {
            Id = "level_100",
            Name = "Legend",
            Description = "Reach maximum level (100)",
            Category = AchievementCategory.Progression,
            Tier = AchievementTier.Diamond,
            PointValue = 150,
            GoldReward = 100000,
            ExperienceReward = 0,
            UnlockMessage = "You have achieved legendary status!"
        });

        // ============ ECONOMY ACHIEVEMENTS ============

        Register(new Achievement
        {
            Id = "gold_1000",
            Name = "Getting Started",
            Description = "Accumulate 1,000 gold",
            Category = AchievementCategory.Economy,
            Tier = AchievementTier.Bronze,
            PointValue = 5
        });

        Register(new Achievement
        {
            Id = "gold_10000",
            Name = "Comfortable",
            Description = "Accumulate 10,000 gold",
            Category = AchievementCategory.Economy,
            Tier = AchievementTier.Bronze,
            PointValue = 10
        });

        Register(new Achievement
        {
            Id = "gold_100000",
            Name = "Wealthy",
            Description = "Accumulate 100,000 gold",
            Category = AchievementCategory.Economy,
            Tier = AchievementTier.Silver,
            PointValue = 25
        });

        Register(new Achievement
        {
            Id = "gold_500000",
            Name = "Rich",
            Description = "Accumulate 500,000 gold",
            Category = AchievementCategory.Economy,
            Tier = AchievementTier.Gold,
            PointValue = 50
        });

        Register(new Achievement
        {
            Id = "gold_1000000",
            Name = "Millionaire",
            Description = "Accumulate 1,000,000 gold",
            Category = AchievementCategory.Economy,
            Tier = AchievementTier.Platinum,
            PointValue = 100,
            UnlockMessage = "You're swimming in gold!"
        });

        Register(new Achievement
        {
            Id = "big_spender",
            Name = "Big Spender",
            Description = "Spend 100,000 gold total",
            Category = AchievementCategory.Economy,
            Tier = AchievementTier.Silver,
            PointValue = 20
        });

        Register(new Achievement
        {
            Id = "shopaholic",
            Name = "Shopaholic",
            Description = "Buy 50 items from shops",
            Category = AchievementCategory.Economy,
            Tier = AchievementTier.Silver,
            PointValue = 20
        });

        // ============ EXPLORATION ACHIEVEMENTS ============

        Register(new Achievement
        {
            Id = "dungeon_5",
            Name = "Dungeon Crawler",
            Description = "Reach dungeon level 5",
            Category = AchievementCategory.Exploration,
            Tier = AchievementTier.Bronze,
            PointValue = 10
        });

        Register(new Achievement
        {
            Id = "dungeon_10",
            Name = "Deep Delver",
            Description = "Reach dungeon level 10",
            Category = AchievementCategory.Exploration,
            Tier = AchievementTier.Silver,
            PointValue = 25
        });

        Register(new Achievement
        {
            Id = "dungeon_25",
            Name = "Depth Seeker",
            Description = "Reach dungeon level 25",
            Category = AchievementCategory.Exploration,
            Tier = AchievementTier.Gold,
            PointValue = 50
        });

        Register(new Achievement
        {
            Id = "dungeon_50",
            Name = "Abyss Walker",
            Description = "Reach dungeon level 50",
            Category = AchievementCategory.Exploration,
            Tier = AchievementTier.Platinum,
            PointValue = 75
        });

        Register(new Achievement
        {
            Id = "dungeon_100",
            Name = "Lord of the Deep",
            Description = "Reach dungeon level 100",
            Category = AchievementCategory.Exploration,
            Tier = AchievementTier.Diamond,
            PointValue = 150,
            GoldReward = 50000,
            UnlockMessage = "You have conquered the deepest depths!"
        });

        Register(new Achievement
        {
            Id = "treasure_hunter",
            Name = "Treasure Hunter",
            Description = "Open 50 chests",
            Category = AchievementCategory.Exploration,
            Tier = AchievementTier.Silver,
            PointValue = 25,
            GoldReward = 1000
        });

        Register(new Achievement
        {
            Id = "secret_finder",
            Name = "Secret Finder",
            Description = "Discover 10 secrets",
            Category = AchievementCategory.Exploration,
            Tier = AchievementTier.Silver,
            PointValue = 30,
            GoldReward = 500
        });

        // ============ SOCIAL ACHIEVEMENTS ============

        Register(new Achievement
        {
            Id = "first_friend",
            Name = "Friendly",
            Description = "Make your first NPC friend",
            Category = AchievementCategory.Social,
            Tier = AchievementTier.Bronze,
            PointValue = 10
        });

        Register(new Achievement
        {
            Id = "popular",
            Name = "Popular",
            Description = "Have 10 NPC friends",
            Category = AchievementCategory.Social,
            Tier = AchievementTier.Silver,
            PointValue = 25
        });

        Register(new Achievement
        {
            Id = "married",
            Name = "Happily Married",
            Description = "Get married",
            Category = AchievementCategory.Social,
            Tier = AchievementTier.Silver,
            PointValue = 30,
            UnlockMessage = "May your love last forever!"
        });

        Register(new Achievement
        {
            Id = "team_player",
            Name = "Team Player",
            Description = "Join or create a team",
            Category = AchievementCategory.Social,
            Tier = AchievementTier.Bronze,
            PointValue = 15
        });

        Register(new Achievement
        {
            Id = "ruler",
            Name = "Ruler",
            Description = "Become the ruler of the realm",
            Category = AchievementCategory.Social,
            Tier = AchievementTier.Platinum,
            PointValue = 100,
            GoldReward = 10000,
            UnlockMessage = "All hail the new ruler!"
        });

        // ============ CHALLENGE ACHIEVEMENTS ============

        Register(new Achievement
        {
            Id = "nightmare_survivor",
            Name = "Nightmare Survivor",
            Description = "Reach level 10 on Nightmare difficulty",
            Category = AchievementCategory.Challenge,
            Tier = AchievementTier.Gold,
            PointValue = 75,
            GoldReward = 5000
        });

        Register(new Achievement
        {
            Id = "nightmare_master",
            Name = "Nightmare Master",
            Description = "Reach level 50 on Nightmare difficulty",
            Category = AchievementCategory.Challenge,
            Tier = AchievementTier.Diamond,
            PointValue = 200,
            GoldReward = 50000,
            UnlockMessage = "You have conquered the impossible!"
        });

        Register(new Achievement
        {
            Id = "persistent",
            Name = "Persistent",
            Description = "Play for 7 consecutive days",
            Category = AchievementCategory.Challenge,
            Tier = AchievementTier.Silver,
            PointValue = 25
        });

        Register(new Achievement
        {
            Id = "dedicated",
            Name = "Dedicated",
            Description = "Play for 30 consecutive days",
            Category = AchievementCategory.Challenge,
            Tier = AchievementTier.Gold,
            PointValue = 75
        });

        Register(new Achievement
        {
            Id = "no_death_10",
            Name = "Untouchable",
            Description = "Reach level 10 without dying",
            Category = AchievementCategory.Challenge,
            Tier = AchievementTier.Gold,
            PointValue = 50
        });

        // ============ QUEST ACHIEVEMENTS ============

        Register(new Achievement
        {
            Id = "quest_starter",
            Name = "Quest Starter",
            Description = "Complete your first quest",
            Category = AchievementCategory.Progression,
            Tier = AchievementTier.Bronze,
            PointValue = 10,
            GoldReward = 100,
            UnlockMessage = "Your first quest complete - many more await!"
        });

        Register(new Achievement
        {
            Id = "quest_master",
            Name = "Quest Master",
            Description = "Complete 25 quests",
            Category = AchievementCategory.Progression,
            Tier = AchievementTier.Gold,
            PointValue = 50,
            GoldReward = 2500,
            UnlockMessage = "A true adventurer completes what they start!"
        });

        Register(new Achievement
        {
            Id = "bounty_hunter",
            Name = "Bounty Hunter",
            Description = "Complete 10 bounty quests",
            Category = AchievementCategory.Combat,
            Tier = AchievementTier.Silver,
            PointValue = 30,
            GoldReward = 1000,
            UnlockMessage = "Justice has been served!"
        });

        // ============ SECRET ACHIEVEMENTS ============

        Register(new Achievement
        {
            Id = "easter_egg_1",
            Name = "???",
            Description = "Found a hidden secret!",
            SecretHint = "Some secrets are hidden in dark places...",
            Category = AchievementCategory.Secret,
            Tier = AchievementTier.Gold,
            PointValue = 50,
            IsSecret = true,
            GoldReward = 1000
        });

        Register(new Achievement
        {
            Id = "completionist",
            Name = "Completionist",
            Description = "Unlock all other achievements",
            Category = AchievementCategory.Secret,
            Tier = AchievementTier.Diamond,
            PointValue = 500,
            IsSecret = true,
            GoldReward = 100000,
            UnlockMessage = "You have done everything there is to do!"
        });

        // ============ MAGIC SHOP ACHIEVEMENTS ============

        Register(new Achievement
        {
            Id = "first_enchant",
            Name = "Apprentice Enchanter",
            Description = "Enchant an equipped item at the Magic Shop",
            Category = AchievementCategory.Economy,
            Tier = AchievementTier.Bronze,
            PointValue = 10,
            GoldReward = 500,
            UnlockMessage = "Your gear glows with arcane power!"
        });

        Register(new Achievement
        {
            Id = "master_enchanter",
            Name = "Master Enchanter",
            Description = "Enchant 10 equipped items",
            Category = AchievementCategory.Economy,
            Tier = AchievementTier.Silver,
            PointValue = 25,
            GoldReward = 2000,
            UnlockMessage = "Ravanella considers you a fellow artisan."
        });

        Register(new Achievement
        {
            Id = "love_magician",
            Name = "Lovelorn Mage",
            Description = "Cast 5 love spells",
            Category = AchievementCategory.Social,
            Tier = AchievementTier.Silver,
            PointValue = 15,
            GoldReward = 1000,
            UnlockMessage = "The heart wants what magic commands."
        });

        Register(new Achievement
        {
            Id = "dark_magician",
            Name = "Shadow Caster",
            Description = "Cast your first death spell",
            Category = AchievementCategory.Secret,
            Tier = AchievementTier.Silver,
            PointValue = 20,
            GoldReward = 1500,
            IsSecret = true,
            UnlockMessage = "You have stepped into the darkness."
        });

        Register(new Achievement
        {
            Id = "angel_of_death",
            Name = "Angel of Death",
            Description = "Kill 5 NPCs via death spells",
            Category = AchievementCategory.Secret,
            Tier = AchievementTier.Gold,
            PointValue = 50,
            GoldReward = 5000,
            IsSecret = true,
            UnlockMessage = "Death follows in your wake."
        });

        Register(new Achievement
        {
            Id = "big_spender_magic",
            Name = "Magical Patron",
            Description = "Spend 100,000 gold at the Magic Shop",
            Category = AchievementCategory.Economy,
            Tier = AchievementTier.Gold,
            PointValue = 30,
            GoldReward = 5000,
            UnlockMessage = "Ravanella names a shelf after you."
        });

        Register(new Achievement
        {
            Id = "corruption_fragment",
            Name = "The Corruption Remembered",
            Description = "Collect the Wave Fragment from Ravanella",
            Category = AchievementCategory.Secret,
            Tier = AchievementTier.Gold,
            PointValue = 40,
            GoldReward = 3000,
            IsSecret = true,
            UnlockMessage = "The corruption is not evil. It is forgetting."
        });

        Register(new Achievement
        {
            Id = "accessory_collector",
            Name = "Bejeweled",
            Description = "Wear rings in both finger slots and a necklace",
            Category = AchievementCategory.Economy,
            Tier = AchievementTier.Bronze,
            PointValue = 10,
            GoldReward = 500,
            UnlockMessage = "You sparkle with magical accessories!"
        });

        // ============ DARK ALLEY ACHIEVEMENTS ============

        Register(new Achievement
        {
            Id = "dark_alley_gambler",
            Name = "High Roller",
            Description = "Win 1,000 gold gambling in the Dark Alley",
            Category = AchievementCategory.Economy,
            Tier = AchievementTier.Silver,
            PointValue = 25,
            GoldReward = 500,
            UnlockMessage = "Lady Luck favors the bold!"
        });

        Register(new Achievement
        {
            Id = "dark_alley_pit_champion",
            Name = "Pit Champion",
            Description = "Win 10 fights in The Pit",
            Category = AchievementCategory.Combat,
            Tier = AchievementTier.Gold,
            PointValue = 50,
            GoldReward = 1000,
            UnlockMessage = "The crowd roars your name!"
        });

        Register(new Achievement
        {
            Id = "dark_alley_pickpocket",
            Name = "Light Fingers",
            Description = "Successfully pickpocket 20 times",
            Category = AchievementCategory.Challenge,
            Tier = AchievementTier.Silver,
            PointValue = 25,
            GoldReward = 500,
            UnlockMessage = "Your fingers are quicker than the eye!"
        });

        Register(new Achievement
        {
            Id = "dark_alley_debt_free",
            Name = "Debt Free",
            Description = "Pay off a loan from the Loan Shark",
            Category = AchievementCategory.Economy,
            Tier = AchievementTier.Bronze,
            PointValue = 10,
            GoldReward = 200,
            UnlockMessage = "Freedom from debt is the sweetest reward."
        });

        Register(new Achievement
        {
            Id = "dark_alley_king",
            Name = "King of the Alley",
            Description = "Reach 1,000 Dark Alley reputation",
            Category = AchievementCategory.Social,
            Tier = AchievementTier.Platinum,
            PointValue = 100,
            GoldReward = 2000,
            UnlockMessage = "The underground bows to your authority!"
        });
    }

    /// <summary>
    /// Register an achievement
    /// </summary>
    private static void Register(Achievement achievement)
    {
        _achievements[achievement.Id] = achievement;
    }

    /// <summary>
    /// Get an achievement by ID
    /// </summary>
    public static Achievement? GetAchievement(string id)
    {
        return _achievements.TryGetValue(id, out var achievement) ? achievement : null;
    }

    /// <summary>
    /// Get all achievements
    /// </summary>
    public static IEnumerable<Achievement> GetAllAchievements() => _achievements.Values;

    /// <summary>
    /// Get achievements by category
    /// </summary>
    public static IEnumerable<Achievement> GetByCategory(AchievementCategory category)
    {
        return _achievements.Values.Where(a => a.Category == category);
    }

    /// <summary>
    /// Try to unlock an achievement for a player
    /// Returns true if newly unlocked, false if already had it or doesn't exist
    /// </summary>
    public static bool TryUnlock(Character player, string achievementId)
    {
        var achievement = GetAchievement(achievementId);
        if (achievement == null) return false;

        if (player.Achievements.Unlock(achievementId))
        {
            // Apply rewards
            if (achievement.GoldReward > 0)
                player.Gold += achievement.GoldReward;
            if (achievement.ExperienceReward > 0)
                player.Experience += achievement.ExperienceReward;

            // Queue notification
            PendingNotifications.Enqueue(achievement);

            // Update statistics
            player.Statistics.TotalExperienceEarned += achievement.ExperienceReward;
            player.Statistics.TotalGoldEarned += achievement.GoldReward;

            // Track achievement telemetry
            TelemetrySystem.Instance.TrackAchievement(
                achievementId,
                achievement.Name,
                player.Level,
                achievement.Category.ToString()
            );

            // Sync with Steam if available (blocked if dev menu was used)
            if (!player.DevMenuUsed)
            {
                SteamIntegration.UnlockAchievement(achievementId);
            }

            // Online news: achievement unlocked
            if (OnlineStateManager.IsActive)
            {
                var displayName = player.Name2 ?? player.Name1;
                _ = OnlineStateManager.Instance!.AddNews(
                    $"{displayName} unlocked \"{achievement.Name}\"!", "quest");
            }

            return true;
        }

        return false;
    }

    /// <summary>
    /// Sync all previously-unlocked achievements to Steam.
    /// Call this on game startup after player is loaded and Steam is initialized.
    /// This ensures achievements earned before Steam integration work properly.
    /// </summary>
    public static void SyncUnlockedToSteam(Character player)
    {
        if (!SteamIntegration.IsAvailable) return;
        if (player.DevMenuUsed)
        {
            DebugLogger.Instance?.LogInfo("ACHIEVEMENTS", "Steam sync skipped - dev menu was used on this save");
            return;
        }

        int synced = 0;
        foreach (var achievementId in player.Achievements.UnlockedAchievements)
        {
            // This will only actually unlock on Steam if not already unlocked there
            if (SteamIntegration.UnlockAchievement(achievementId))
            {
                synced++;
            }
        }

        if (synced > 0)
        {
            DebugLogger.Instance?.LogInfo("ACHIEVEMENTS", $"Synced {synced} previously-unlocked achievements to Steam");
        }
    }

    /// <summary>
    /// Check and award achievements based on player state
    /// Call this after significant game events
    /// </summary>
    public static void CheckAchievements(Character player)
    {
        var stats = player.Statistics;

        // Combat achievements
        if (stats.TotalMonstersKilled >= 1) TryUnlock(player, "first_blood");
        if (stats.TotalMonstersKilled >= 10) TryUnlock(player, "monster_slayer_10");
        if (stats.TotalMonstersKilled >= 100) TryUnlock(player, "monster_slayer_100");
        if (stats.TotalMonstersKilled >= 500) TryUnlock(player, "monster_slayer_500");
        if (stats.TotalMonstersKilled >= 1000) TryUnlock(player, "monster_slayer_1000");
        if (stats.TotalBossesKilled >= 1) TryUnlock(player, "boss_killer");
        if (stats.TotalBossesKilled >= 10) TryUnlock(player, "boss_slayer_10");
        if (stats.TotalUniquesKilled >= 1) TryUnlock(player, "unique_killer");
        if (stats.TotalCriticalHits >= 100) TryUnlock(player, "critical_master");
        if (stats.TotalDamageDealt >= 10000) TryUnlock(player, "damage_dealer_10000");
        if (stats.TotalDamageDealt >= 100000) TryUnlock(player, "damage_dealer_100000");
        if (stats.TotalPlayerKills >= 1) TryUnlock(player, "pvp_victor");
        if (stats.TotalPlayerKills >= 10) TryUnlock(player, "pvp_veteran");
        if (stats.TotalPlayerKills >= 50) TryUnlock(player, "pvp_champion");

        // Progression achievements
        if (player.Level >= 5) TryUnlock(player, "level_5");
        if (player.Level >= 10) TryUnlock(player, "level_10");
        if (player.Level >= 25) TryUnlock(player, "level_25");
        if (player.Level >= 50) TryUnlock(player, "level_50");
        if (player.Level >= 75) TryUnlock(player, "level_75");
        if (player.Level >= 100) TryUnlock(player, "level_100");

        // Economy achievements
        if (stats.HighestGoldHeld >= 1000) TryUnlock(player, "gold_1000");
        if (stats.HighestGoldHeld >= 10000) TryUnlock(player, "gold_10000");
        if (stats.HighestGoldHeld >= 100000) TryUnlock(player, "gold_100000");
        if (stats.HighestGoldHeld >= 500000) TryUnlock(player, "gold_500000");
        if (stats.HighestGoldHeld >= 1000000) TryUnlock(player, "gold_1000000");
        if (stats.TotalGoldSpent >= 100000) TryUnlock(player, "big_spender");
        if (stats.TotalItemsBought >= 50) TryUnlock(player, "shopaholic");

        // Exploration achievements
        if (stats.DeepestDungeonLevel >= 5) TryUnlock(player, "dungeon_5");
        if (stats.DeepestDungeonLevel >= 10) TryUnlock(player, "dungeon_10");
        if (stats.DeepestDungeonLevel >= 25) TryUnlock(player, "dungeon_25");
        if (stats.DeepestDungeonLevel >= 50) TryUnlock(player, "dungeon_50");
        if (stats.DeepestDungeonLevel >= 100) TryUnlock(player, "dungeon_100");
        if (stats.TotalChestsOpened >= 50) TryUnlock(player, "treasure_hunter");
        if (stats.TotalSecretsFound >= 10) TryUnlock(player, "secret_finder");

        // Social achievements
        if (stats.TotalFriendsGained >= 1) TryUnlock(player, "first_friend");
        if (stats.TotalFriendsGained >= 10) TryUnlock(player, "popular");
        if (player.Married) TryUnlock(player, "married");
        if (!string.IsNullOrEmpty(player.Team)) TryUnlock(player, "team_player");
        if (player.King) TryUnlock(player, "ruler");

        // Challenge achievements
        if (player.Difficulty == DifficultyMode.Nightmare && player.Level >= 10)
            TryUnlock(player, "nightmare_survivor");
        if (player.Difficulty == DifficultyMode.Nightmare && player.Level >= 50)
            TryUnlock(player, "nightmare_master");
        if (stats.CurrentStreak >= 7) TryUnlock(player, "persistent");
        if (stats.CurrentStreak >= 30) TryUnlock(player, "dedicated");
        if (player.Level >= 10 && stats.TotalMonsterDeaths == 0 && stats.TotalPlayerDeaths == 0)
            TryUnlock(player, "no_death_10");

        // Dark Alley achievements
        if (stats.TotalGoldFromGambling >= 1000) TryUnlock(player, "dark_alley_gambler");
        if (stats.TotalPitFightsWon >= 10) TryUnlock(player, "dark_alley_pit_champion");
        if (stats.TotalPickpocketSuccesses >= 20) TryUnlock(player, "dark_alley_pickpocket");
        if (player.DarkAlleyReputation >= 1000) TryUnlock(player, "dark_alley_king");

        // Check for completionist (all non-secret achievements)
        var nonSecretCount = _achievements.Values.Count(a => !a.IsSecret && a.Id != "completionist");
        var unlockedNonSecret = player.Achievements.UnlockedAchievements
            .Count(id => GetAchievement(id) is Achievement a && !a.IsSecret && a.Id != "completionist");
        if (unlockedNonSecret >= nonSecretCount)
            TryUnlock(player, "completionist");
    }

    /// <summary>
    /// Check for special combat achievements
    /// </summary>
    public static void CheckCombatAchievements(Character player, bool tookDamage, double hpPercent)
    {
        if (!tookDamage) TryUnlock(player, "flawless_victory");
        if (hpPercent < 0.1) TryUnlock(player, "survivor");
    }

    /// <summary>
    /// Show any pending achievement notifications
    /// Shows consolidated view if multiple achievements unlocked at once
    /// </summary>
    public static async System.Threading.Tasks.Task ShowPendingNotifications(TerminalEmulator terminal)
    {
        if (PendingNotifications.Count == 0) return;

        // Collect all pending achievements
        var achievements = new List<Achievement>();
        while (PendingNotifications.Count > 0)
        {
            achievements.Add(PendingNotifications.Dequeue());
        }

        // Single achievement - show full display
        if (achievements.Count == 1)
        {
            await ShowAchievementUnlock(terminal, achievements[0]);
            return;
        }

        // Multiple achievements - show consolidated view
        await ShowMultipleAchievements(terminal, achievements);
    }

    /// <summary>
    /// Display a single achievement unlock notification
    /// </summary>
    private static async System.Threading.Tasks.Task ShowAchievementUnlock(TerminalEmulator terminal, Achievement achievement)
    {
        terminal.WriteLine("");
        terminal.SetColor("bright_yellow");
        terminal.WriteLine("╔══════════════════════════════════════════════════════════╗");
        terminal.Write("║");
        terminal.SetColor("bright_cyan");
        terminal.Write($"{"* ACHIEVEMENT UNLOCKED! *",58}");
        terminal.SetColor("bright_yellow");
        terminal.WriteLine("║");
        terminal.WriteLine("╠══════════════════════════════════════════════════════════╣");

        string tierLine = $"  {achievement.GetTierSymbol()} {achievement.Name}";
        terminal.Write("║");
        terminal.SetColor(achievement.GetTierColor());
        terminal.Write($"{tierLine,-58}");
        terminal.SetColor("bright_yellow");
        terminal.WriteLine("║");

        string descLine = $"  {achievement.Description}";
        terminal.Write("║");
        terminal.SetColor("white");
        terminal.Write($"{descLine,-58}");
        terminal.SetColor("bright_yellow");
        terminal.WriteLine("║");

        if (achievement.GoldReward > 0 || achievement.ExperienceReward > 0)
        {
            var rewards = "";
            if (achievement.GoldReward > 0) rewards += $"+{achievement.GoldReward} Gold ";
            if (achievement.ExperienceReward > 0) rewards += $"+{achievement.ExperienceReward} XP";
            string rewardLine = $"  Rewards: {rewards}";
            terminal.Write("║");
            terminal.SetColor("bright_green");
            terminal.Write($"{rewardLine,-58}");
            terminal.SetColor("bright_yellow");
            terminal.WriteLine("║");
        }

        if (!string.IsNullOrEmpty(achievement.UnlockMessage))
        {
            string msgLine = $"  \"{achievement.UnlockMessage}\"";
            terminal.Write("║");
            terminal.SetColor("bright_magenta");
            terminal.Write($"{msgLine,-58}");
            terminal.SetColor("bright_yellow");
            terminal.WriteLine("║");
        }

        terminal.WriteLine("╚══════════════════════════════════════════════════════════╝");
        terminal.WriteLine("");

        await System.Threading.Tasks.Task.Delay(1500);
    }

    /// <summary>
    /// Display multiple achievements in a consolidated view
    /// </summary>
    private static async System.Threading.Tasks.Task ShowMultipleAchievements(TerminalEmulator terminal, List<Achievement> achievements)
    {
        long totalGold = achievements.Sum(a => a.GoldReward);
        long totalXP = achievements.Sum(a => a.ExperienceReward);

        terminal.WriteLine("");
        terminal.SetColor("bright_yellow");
        terminal.WriteLine("╔══════════════════════════════════════════════════════════╗");
        string headerText = $"* {achievements.Count} ACHIEVEMENTS UNLOCKED! *";
        terminal.Write("║");
        terminal.SetColor("bright_cyan");
        terminal.Write($"{headerText,58}");
        terminal.SetColor("bright_yellow");
        terminal.WriteLine("║");
        terminal.WriteLine("╠══════════════════════════════════════════════════════════╣");

        // Show up to 8 achievements, summarize if more
        foreach (var achievement in achievements.OrderByDescending(a => a.Tier).Take(8))
        {
            var name = achievement.Name.Length > 45 ? achievement.Name.Substring(0, 42) + "..." : achievement.Name;
            string achLine = $"  {achievement.GetTierSymbol()} {name}";
            terminal.Write("║");
            terminal.SetColor(achievement.GetTierColor());
            terminal.Write($"{achLine,-58}");
            terminal.SetColor("bright_yellow");
            terminal.WriteLine("║");
        }

        if (achievements.Count > 8)
        {
            string moreLine = $"  ... and {achievements.Count - 8} more!";
            terminal.Write("║");
            terminal.SetColor("gray");
            terminal.Write($"{moreLine,-58}");
            terminal.SetColor("bright_yellow");
            terminal.WriteLine("║");
        }

        terminal.WriteLine("╠══════════════════════════════════════════════════════════╣");

        if (totalGold > 0 || totalXP > 0)
        {
            var rewards = "";
            if (totalGold > 0) rewards += $"+{totalGold:N0} Gold ";
            if (totalXP > 0) rewards += $"+{totalXP:N0} XP";
            string rewardLine = $"  Total Rewards: {rewards}";
            terminal.Write("║");
            terminal.SetColor("bright_green");
            terminal.Write($"{rewardLine,-58}");
            terminal.SetColor("bright_yellow");
            terminal.WriteLine("║");
        }

        terminal.WriteLine("╚══════════════════════════════════════════════════════════╝");
        terminal.WriteLine("");

        await System.Threading.Tasks.Task.Delay(2500);
    }
}
