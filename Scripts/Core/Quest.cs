using UsurperRemake.Utils;
using System;
using System.Collections.Generic;
using System.Linq;

/// <summary>
/// Quest Record - Pascal-compatible quest structure based on QuestRec
/// </summary>
public partial class Quest
{
    // Primary Quest Data (Pascal QuestRec fields)
    public string Id { get; set; } = "";
    public string Initiator { get; set; } = "";
    public DateTime Date { get; set; }
    public QuestType QuestType { get; set; }
    public QuestTarget QuestTarget { get; set; }
    public byte Difficulty { get; set; }
    public bool Deleted { get; set; } = false;
    public string Comment { get; set; } = "";
    
    // Quest Occupancy
    public string Occupier { get; set; } = "";
    public CharacterRace OccupierRace { get; set; }
    public byte OccupierSex { get; set; }
    public int OccupiedDays { get; set; } = 0;
    public int DaysToComplete { get; set; }
    
    // Quest Requirements
    public int MinLevel { get; set; } = 1;
    public int MaxLevel { get; set; } = 9999;
    
    // Quest Rewards
    public byte Reward { get; set; } = 0;
    public QuestRewardType RewardType { get; set; }

    // Quest Penalties (for failure)
    public byte Penalty { get; set; } = 0;
    public QuestRewardType PenaltyType { get; set; } = QuestRewardType.Nothing;

    // Quest Offering (for targeted quests)
    public string OfferedTo { get; set; } = "";
    public bool Forced { get; set; } = false;

    // Display title used in mails / UI (not part of original Pascal structure but referenced by systems)
    public string Title { get; set; } = "(unnamed quest)";

    // Target NPC name for bounty/assassination quests
    public string TargetNPCName { get; set; } = "";

    // Objective Tracking (for modern quest progress)
    public List<QuestObjective> Objectives { get; set; } = new();
    
    // Quest Monsters
    public List<QuestMonster> Monsters { get; set; } = new();
    
    // Status Properties
    public bool IsActive => !Deleted && !string.IsNullOrEmpty(Occupier);
    public bool IsAvailable => !Deleted && string.IsNullOrEmpty(Occupier);
    public int DaysRemaining => Math.Max(0, DaysToComplete - OccupiedDays);
    
    public Quest()
    {
        Id = $"Q{DateTime.Now:yyyyMMddHHmmss}{Random.Shared.Next() % 1000:D3}";
        Date = DateTime.Now;
        DaysToComplete = 7; // Default 7 days
        Monsters = new List<QuestMonster>();
    }
    
    /// <summary>
    /// Get quest difficulty description
    /// </summary>
    public string GetDifficultyString()
    {
        return Difficulty switch
        {
            1 => "Easy",
            2 => "Medium", 
            3 => "Hard",
            4 => "Extreme",
            _ => "Unknown"
        };
    }
    
    /// <summary>
    /// Get quest target description
    /// </summary>
    public string GetTargetDescription()
    {
        return QuestTarget switch
        {
            QuestTarget.Monster => "Slay Monsters",
            QuestTarget.Assassin => "Assassination Mission",
            QuestTarget.Seduce => "Seduction Mission",
            QuestTarget.ClaimTown => "Claim Territory",
            QuestTarget.GangWar => "Gang War Participation",
            QuestTarget.ClearBoss => "Dungeon Boss Hunt",
            QuestTarget.FindArtifact => "Artifact Recovery",
            QuestTarget.ReachFloor => "Dungeon Expedition",
            QuestTarget.ClearFloor => "Dungeon Sweep",
            QuestTarget.RescueNPC => "Rescue Mission",
            QuestTarget.SurviveDungeon => "Survival Challenge",
            QuestTarget.BuyWeapon => "Weapon Procurement",
            QuestTarget.BuyArmor => "Armor Procurement",
            QuestTarget.BuyAccessory => "Accessory Procurement",
            QuestTarget.BuyShield => "Shield Procurement",
            _ => "Unknown Mission"
        };
    }
    
    /// <summary>
    /// Calculate reward amount based on player level
    /// </summary>
    public long CalculateReward(int playerLevel)
    {
        if (Reward == 0) return 0;
        
        return RewardType switch
        {
            QuestRewardType.Experience => Reward switch
            {
                1 => playerLevel * 100,   // Low exp
                2 => playerLevel * 500,   // Medium exp
                3 => playerLevel * 1000,  // High exp
                _ => 0
            },
            QuestRewardType.Money => Reward switch
            {
                1 => playerLevel * 1100,  // Low gold
                2 => playerLevel * 5100,  // Medium gold
                3 => playerLevel * 11000, // High gold
                _ => 0
            },
            QuestRewardType.Potions => Reward switch
            {
                1 => 50,   // Low potions
                2 => 100,  // Medium potions
                3 => 200,  // High potions
                _ => 0
            },
            QuestRewardType.Darkness or QuestRewardType.Chivalry => Reward switch
            {
                1 => 25,   // Low points
                2 => 75,   // Medium points
                3 => 110,  // High points
                _ => 0
            },
            _ => 0
        };
    }
    
    /// <summary>
    /// Get full quest display information (Pascal quest display)
    /// </summary>
    public string GetDisplayInfo()
    {
        var status = IsActive ? $"Claimed by {Occupier}" : "Available";
        var timeInfo = IsActive ? $"{DaysRemaining} days left" : $"{DaysToComplete} days to complete";

        return $"{GetTargetDescription()} | {GetDifficultyString()} | {GetRewardDescription()} | {status} | {timeInfo}";
    }

    /// <summary>
    /// Check if a player can claim this quest (Pascal: Quest claim validation)
    /// </summary>
    public QuestClaimResult CanPlayerClaim(Character player)
    {
        // Quest already deleted
        if (Deleted) return QuestClaimResult.QuestDeleted;

        // Quest already claimed by someone
        if (!string.IsNullOrEmpty(Occupier)) return QuestClaimResult.AlreadyClaimed;

        // Royals cannot take quests (they create them)
        if (player.King) return QuestClaimResult.RoyalsNotAllowed;

        // Cannot take your own quest
        if (Initiator == player.Name2) return QuestClaimResult.OwnQuest;

        // Level requirements
        if (player.Level < MinLevel) return QuestClaimResult.LevelTooLow;
        if (player.Level > MaxLevel) return QuestClaimResult.LevelTooHigh;

        // Daily limit check
        if (player.RoyQuestsToday >= GameConfig.MaxQuestsPerDay) return QuestClaimResult.DailyLimitReached;

        // If quest is offered to specific player, only they can claim (unless forced)
        if (!string.IsNullOrEmpty(OfferedTo) && OfferedTo != player.Name2 && !Forced)
        {
            return QuestClaimResult.AlreadyClaimed; // Reuse this result for "not offered to you"
        }

        return QuestClaimResult.CanClaim;
    }

    /// <summary>
    /// Get objective completion progress as percentage
    /// </summary>
    public float GetObjectiveProgress()
    {
        if (Objectives == null || Objectives.Count == 0) return 0f;

        int completed = Objectives.Count(o => o.IsComplete);
        return (float)completed / Objectives.Count;
    }

    /// <summary>
    /// Check if all required (non-optional) objectives are complete
    /// Optional objectives don't block quest completion
    /// </summary>
    public bool AreAllObjectivesComplete()
    {
        if (Objectives == null || Objectives.Count == 0) return true;
        // Primary check: all required (non-optional) objectives are complete
        if (Objectives.Where(o => !o.IsOptional).All(o => o.IsComplete))
            return true;
        // Alternative: for RescueNPC quests, completing ALL optional objectives
        // also satisfies the quest (the NPC may be dead and untalkable)
        if (QuestTarget == QuestTarget.RescueNPC && Objectives.Any(o => o.IsOptional))
            return Objectives.Where(o => o.IsOptional).All(o => o.IsComplete);
        return false;
    }

    /// <summary>
    /// Check if ALL objectives (including optional) are complete
    /// </summary>
    public bool AreAllObjectivesIncludingOptionalComplete()
    {
        if (Objectives == null || Objectives.Count == 0) return true;
        return Objectives.All(o => o.IsComplete);
    }

    /// <summary>
    /// Update progress on a specific objective type
    /// </summary>
    public void UpdateObjectiveProgress(QuestObjectiveType type, int amount = 1, string targetId = "")
    {
        foreach (var objective in Objectives.Where(o => o.ObjectiveType == type && !o.IsComplete))
        {
            // If both caller and objective specify targetIds, they must match.
            // Empty objective TargetId means "match any target of this type".
            if (!string.IsNullOrEmpty(targetId) && !string.IsNullOrEmpty(objective.TargetId)
                && !objective.TargetId.Equals(targetId, StringComparison.OrdinalIgnoreCase)) continue;

            objective.CurrentProgress += amount;
            if (objective.CurrentProgress >= objective.RequiredProgress)
            {
                objective.CurrentProgress = objective.RequiredProgress;
            }
        }
    }

    /// <summary>
    /// Human-readable reward description string – e.g. "High Gold", "Medium Experience".
    /// </summary>
    public string GetRewardDescription()
    {
        if (Reward == 0 || RewardType == QuestRewardType.Nothing)
            return "No reward";

        string level = Reward switch
        {
            1 => "Low",
            2 => "Medium",
            3 => "High",
            _ => "Unknown"
        };

        return $"{level} {RewardType}";
    }
}

/// <summary>
/// Quest Monster data
/// </summary>
public class QuestMonster
{
    public int MonsterType { get; set; }
    public int Count { get; set; }
    public string MonsterName { get; set; } = "";
    
    public QuestMonster(int type, int count, string name = "")
    {
        MonsterType = type;
        Count = count;
        MonsterName = name;
    }
}

/// <summary>
/// Quest Types - Pascal QuestTypes enumeration
/// </summary>
public enum QuestType
{
    SingleQuest = 0,
    TeamQuest = 1
}

/// <summary>
/// Quest Targets - Pascal QuestTargets enumeration with dungeon extensions
/// </summary>
public enum QuestTarget
{
    // Original Pascal quest types
    Monster = 0,
    Assassin = 1,
    Seduce = 2,
    ClaimTown = 3,
    GangWar = 4,

    // Dungeon-specific quest types
    ClearBoss = 10,             // Kill a specific dungeon boss
    FindArtifact = 11,          // Find and retrieve an artifact from dungeon
    ReachFloor = 12,            // Reach a specific floor in the dungeon
    ClearFloor = 13,            // Clear all monsters on a dungeon floor
    RescueNPC = 14,             // Rescue an NPC trapped in the dungeon
    SurviveDungeon = 15,        // Survive X floors without returning to town

    // NPC-related quest types
    DefeatNPC = 20,             // Defeat a specific NPC (bounty system)

    // Equipment purchase quests
    BuyWeapon = 30,             // Purchase a specific weapon from shops
    BuyArmor = 31,              // Purchase a specific armor piece from shops
    BuyAccessory = 32,          // Purchase a specific accessory (ring, amulet) from shops
    BuyShield = 33              // Purchase a specific shield from shops
}

/// <summary>
/// Quest Reward Types - Pascal QRewardTypes enumeration
/// </summary>
public enum QuestRewardType
{
    Nothing = 0,
    Experience = 1,
    Money = 2,
    Potions = 3,
    Darkness = 4,
    Chivalry = 5
}

/// <summary>
/// Quest Claim Results - Validation results for quest claiming
/// </summary>
public enum QuestClaimResult
{
    CanClaim = 0,                                                // Player can claim quest
    QuestDeleted = 1,                                            // Quest no longer exists
    AlreadyClaimed = 2,                                          // Quest already claimed
    RoyalsNotAllowed = 3,                                        // Royals cannot take quests
    OwnQuest = 4,                                                // Cannot take own quest
    LevelTooLow = 5,                                             // Player level too low
    LevelTooHigh = 6,                                            // Player level too high
    DailyLimitReached = 7                                        // Daily quest limit reached
}

/// <summary>
/// Quest Objective Types - What kind of progress is being tracked
/// </summary>
public enum QuestObjectiveType
{
    // Combat objectives
    KillMonsters = 0,           // Kill X monsters of any type
    KillSpecificMonster = 1,    // Kill X of a specific monster type
    KillBoss = 2,               // Kill a specific boss monster

    // Dungeon objectives
    ReachDungeonFloor = 10,     // Reach floor X in a dungeon
    ClearDungeonFloor = 11,     // Clear all monsters on floor X
    FindArtifact = 12,          // Find a specific artifact in dungeon
    ExploreRooms = 13,          // Explore X rooms in dungeon

    // Collection objectives
    CollectGold = 20,           // Collect X gold
    CollectItems = 21,          // Collect X items
    CollectPotions = 22,        // Collect X potions

    // Social objectives
    TalkToNPC = 30,             // Talk to a specific NPC
    DeliverItem = 31,           // Deliver item to NPC
    Assassinate = 32,           // Assassinate target player
    Seduce = 33,                // Seduce target player
    DefeatNPC = 34,             // Defeat a specific NPC (bounty)

    // Exploration objectives
    VisitLocation = 40,         // Visit a specific location
    SurviveDays = 41,           // Survive X days with quest active

    // Equipment purchase objectives
    PurchaseEquipment = 50      // Purchase a specific piece of equipment
}

/// <summary>
/// Quest Objective - Trackable progress for quest completion
/// </summary>
public class QuestObjective
{
    public string Id { get; set; } = "";
    public string Description { get; set; } = "";
    public QuestObjectiveType ObjectiveType { get; set; }
    public string TargetId { get; set; } = "";          // Monster type, NPC name, location, etc.
    public string TargetName { get; set; } = "";        // Display name for target
    public int RequiredProgress { get; set; } = 1;      // How many needed
    public int CurrentProgress { get; set; } = 0;       // Current count
    public bool IsOptional { get; set; } = false;       // Optional bonus objectives
    public int BonusReward { get; set; } = 0;           // Extra reward for optional objectives

    public bool IsComplete => CurrentProgress >= RequiredProgress;
    public float ProgressPercent => RequiredProgress > 0 ? (float)CurrentProgress / RequiredProgress : 0f;

    public QuestObjective()
    {
        Id = $"OBJ{DateTime.Now:HHmmss}{new Random().Next(100, 999)}";
    }

    public QuestObjective(QuestObjectiveType type, string description, int required, string targetId = "", string targetName = "")
    {
        Id = $"OBJ{DateTime.Now:HHmmss}{new Random().Next(100, 999)}";
        ObjectiveType = type;
        Description = description;
        RequiredProgress = required;
        TargetId = targetId;
        TargetName = targetName;
    }

    /// <summary>
    /// Get display string for objective status
    /// </summary>
    public string GetDisplayString()
    {
        var status = IsComplete ? "[COMPLETE]" : $"[{CurrentProgress}/{RequiredProgress}]";
        var optional = IsOptional ? " (Optional)" : "";
        return $"{status} {Description}{optional}";
    }
} 
