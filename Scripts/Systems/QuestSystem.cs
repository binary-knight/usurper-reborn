using UsurperRemake.Utils;
using UsurperRemake.Systems;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;

/// <summary>
/// Quest System - Complete Pascal-compatible quest management engine
/// Based on Pascal PLYQUEST.PAS and RQUESTS.PAS with all quest functionality
/// Handles quest creation, claiming, completion, rewards, and database management
/// </summary>
public partial class QuestSystem
{
    private static List<Quest> questDatabase = new List<Quest>();
    private static Random random = new Random();

    /// <summary>
    /// Add a pre-built quest to the database (for systems that create quests externally).
    /// </summary>
    public static void AddQuestToDatabase(Quest quest)
    {
        questDatabase.Add(quest);
    }

    /// <summary>
    /// Create new quest (Pascal: Royal quest initiation from RQUESTS.PAS)
    /// </summary>
    public static Quest CreateQuest(Character king, QuestTarget target, byte difficulty,
                                   string comment, QuestType questType = QuestType.SingleQuest,
                                   int targetPlayerLevel = 0)
    {
        // Validate king can create quest
        if (king.QuestsLeft < 1)
        {
            throw new InvalidOperationException("King has no quests left today");
        }

        if (questDatabase.Count >= GameConfig.MaxQuestsAllowed)
        {
            throw new InvalidOperationException("Quest database is full");
        }

        // Use king's level as fallback for target player level
        int playerLevel = targetPlayerLevel > 0 ? targetPlayerLevel : Math.Max(1, king.Level);

        var quest = new Quest
        {
            Initiator = king.Name2,
            QuestType = questType,
            QuestTarget = target,
            Difficulty = difficulty,
            Comment = comment,
            Date = DateTime.Now,
            MinLevel = 1,
            MaxLevel = 9999,
            DaysToComplete = GameConfig.DefaultQuestDays
        };

        // Generate quest monsters based on target, difficulty, and target player level
        GenerateQuestMonsters(quest, playerLevel);

        // Set default rewards based on difficulty
        SetDefaultRewards(quest);

        // Add to database
        questDatabase.Add(quest);

        // Update king's quest count
        king.QuestsLeft--;

        // GD.Print($"[QuestSystem] Quest created by {king.Name2}: {quest.GetTargetDescription()}");

        return quest;
    }
    
    /// <summary>
    /// Claim quest for player (Pascal: Quest claiming from PLYQUEST.PAS)
    /// </summary>
    public static QuestClaimResult ClaimQuest(Player player, Quest questToClaim)
    {
        var foundQuest = GetQuestById(questToClaim.Id);
        if (foundQuest == null) return QuestClaimResult.QuestDeleted;
        
        // Validate player can claim
        var claimResult = foundQuest.CanPlayerClaim(player);
        if (claimResult != QuestClaimResult.CanClaim) return claimResult;
        
        // Claim the quest
        foundQuest.Occupier = player.Name2;
        foundQuest.OccupierRace = player.Race;
                        foundQuest.OccupierSex = (byte)((int)player.Sex);
        foundQuest.OccupiedDays = 0;
        
        // Track in player list
        player.ActiveQuests.Add(foundQuest);

        // GD.Print($"[QuestSystem] Quest claimed by {player.Name2}: {foundQuest.Id}");

        // Send confirmation mail (Pascal: Quest claim notification)
        MailSystem.SendQuestClaimedMail(player.Name2, foundQuest.Title);

        // Track telemetry for quest acceptance
        TelemetrySystem.Instance.TrackQuest(
            foundQuest.Title ?? foundQuest.GetTargetDescription(),
            "accepted",
            player.Level,
            reward: null,
            questType: foundQuest.QuestType.ToString()
        );

        return QuestClaimResult.CanClaim;
    }
    
    /// <summary>
    /// Complete quest and give rewards (Pascal: Quest completion from PLYQUEST.PAS)
    /// </summary>
    public static QuestCompletionResult CompleteQuest(Character player, string questId, TerminalEmulator terminal)
    {
        var quest = GetQuestById(questId);
        if (quest == null) return QuestCompletionResult.QuestNotFound;
        
        if (quest.Occupier != player.Name2) return QuestCompletionResult.NotYourQuest;
        if (quest.Deleted) return QuestCompletionResult.QuestDeleted;
        
        // Check if player completed all quest requirements
        if (!ValidateQuestCompletion(player, quest))
        {
            return QuestCompletionResult.RequirementsNotMet;
        }

        // Display completion banner
        terminal.WriteLine("");
        terminal.WriteLine("╔════════════════════════════════════════╗", "bright_yellow");
        terminal.WriteLine("║         QUEST COMPLETED!               ║", "bright_yellow");
        terminal.WriteLine("╚════════════════════════════════════════╝", "bright_yellow");
        terminal.WriteLine("");

        // For equipment purchase quests, remove the purchased item from the player
        // (the quest says "the Merchant Guild needs it", so the item is handed over)
        if (quest.QuestTarget == QuestTarget.BuyWeapon ||
            quest.QuestTarget == QuestTarget.BuyArmor ||
            quest.QuestTarget == QuestTarget.BuyAccessory ||
            quest.QuestTarget == QuestTarget.BuyShield)
        {
            RemoveQuestEquipment(player, quest, terminal);
        }

        // Calculate and give rewards (Pascal reward calculations)
        var rewardAmount = quest.CalculateReward(player.Level);
        ApplyQuestReward(player, quest, rewardAmount, terminal);

        // Mark quest as complete
        quest.Deleted = true;
        quest.Occupier = "";
        
        // Update player statistics
        player.RoyQuests++;
        player.RoyQuestsToday++;
        player.ActiveQuests.Remove(quest);

        // Update global statistics tracking
        StatisticsManager.Current.RecordQuestComplete();

        // Track bounty completion separately for achievements
        if (quest.Initiator == KING_BOUNTY_INITIATOR || quest.QuestTarget == QuestTarget.DefeatNPC)
        {
            StatisticsManager.Current.RecordBountyComplete();
        }

        // Faction standing boost for faction-initiated quests
        var questFaction = GetFactionFromInitiator(quest.Initiator);
        if (questFaction != null)
        {
            var factionSystem = FactionSystem.Instance;
            if (factionSystem != null)
            {
                factionSystem.FactionStanding[questFaction.Value] += 50;
                factionSystem.CompletedFactionQuests.Add(quest.Id);
                terminal.WriteLine($"  Your standing with {quest.Initiator} has improved!", "bright_cyan");
            }
        }

        // Check quest achievements
        CheckQuestAchievements(player);

        // Send completion notification to king (Pascal: King notification)
        SendQuestCompletionMail(player, quest, rewardAmount);

        // News announcement (Pascal: News generation)
        GenerateQuestCompletionNews(player, quest);

        // Track telemetry for quest completion
        TelemetrySystem.Instance.TrackQuest(
            quest.Title ?? quest.GetTargetDescription(),
            "completed",
            player.Level,
            reward: rewardAmount,
            questType: quest.QuestType.ToString()
        );

        // GD.Print($"[QuestSystem] Quest completed by {player.Name2}: {quest.Id}");

        return QuestCompletionResult.Success;
    }
    
    /// <summary>
    /// Get available quests for player (Pascal: Quest listing)
    /// </summary>
    public static List<Quest> GetAvailableQuests(Character player)
    {
        return questDatabase.Where(q =>
            !q.Deleted &&
            string.IsNullOrEmpty(q.Occupier) &&
            !player.King &&
            q.Initiator != player.Name2 &&
            player.Level >= q.MinLevel &&
            player.Level <= q.MaxLevel
        )
        .GroupBy(q => q.Comment)     // Deduplicate by quest name
        .Select(g => g.First())
        .Take(GameConfig.MaxAvailableQuestsShown) // Cap displayed quests
        .ToList();
    }
    
    /// <summary>
    /// Get player's active quests (Pascal: Player quest tracking)
    /// </summary>
    public static List<Quest> GetPlayerQuests(string playerName)
    {
        return questDatabase.Where(q =>
            !q.Deleted &&
            q.Occupier == playerName
        ).ToList();
    }

    /// <summary>
    /// Get active quests for a specific player (alias for GetPlayerQuests)
    /// </summary>
    public static List<Quest> GetActiveQuestsForPlayer(string playerName)
    {
        return GetPlayerQuests(playerName);
    }

    /// <summary>
    /// Get quest by ID
    /// </summary>
    public static Quest GetQuestById(string questId)
    {
        return questDatabase.FirstOrDefault(q => q.Id == questId);
    }
    
    /// <summary>
    /// Offer quest to specific player (Pascal: Quest offering system)
    /// </summary>
    public static void OfferQuestToPlayer(Quest quest, string playerName, bool forced = false)
    {
        quest.OfferedTo = playerName;
        quest.Forced = forced;
        
        // Send quest offer mail (Pascal: Quest offer mail)
        MailSystem.SendQuestOfferMail(playerName, quest.Title);
        
        // GD.Print($"[QuestSystem] Quest offered to {playerName}: {quest.Id}");
    }
    
    /// <summary>
    /// Process daily quest maintenance (Pascal: Quest aging and failure)
    /// </summary>
    public static void ProcessDailyQuestMaintenance()
    {
        var failedQuests = new List<Quest>();
        
        foreach (var quest in questDatabase.Where(q => !q.Deleted && !string.IsNullOrEmpty(q.Occupier)))
        {
            quest.OccupiedDays++;
            
            // Check for quest failure (Pascal: Quest time limit)
            if (quest.OccupiedDays > quest.DaysToComplete)
            {
                failedQuests.Add(quest);
            }
        }
        
        // Process failed quests
        foreach (var failedQuest in failedQuests)
        {
            ProcessQuestFailure(failedQuest);
        }
        
        // Clean up old completed quests (keep database manageable)
        CleanupOldQuests();
        
        // GD.Print($"[QuestSystem] Daily maintenance: {failedQuests.Count} quests failed");
    }
    
    /// <summary>
    /// Generate quest monsters based on target and difficulty
    /// Uses MonsterFamilies system for level-appropriate monsters
    /// </summary>
    private static void GenerateQuestMonsters(Quest quest, int playerLevel = 1)
    {
        if (quest.QuestTarget != QuestTarget.Monster) return;

        quest.Monsters.Clear();

        // Number of monster types based on difficulty
        var monsterTypeCount = quest.Difficulty switch
        {
            1 => 1,     // Easy: 1 type
            2 => 2,     // Medium: 2 types
            3 => 3,     // Hard: 3 types
            4 => 4,     // Extreme: 4 types
            _ => 1
        };

        // Track which monster families we've used to avoid duplicates
        var usedFamilies = new HashSet<string>();

        for (int i = 0; i < monsterTypeCount; i++)
        {
            // Get level-appropriate monster based on player level and difficulty
            // Difficulty adjusts the target level: higher difficulty = slightly higher level monsters
            int targetLevel = Math.Max(1, playerLevel + (quest.Difficulty - 2) * 3);

            // Cap to accessible dungeon range (player level ± 10)
            int maxAccessibleLevel = Math.Min(100, playerLevel + 10);
            targetLevel = Math.Min(targetLevel, maxAccessibleLevel);

            // Get a level-appropriate monster from MonsterFamilies
            var (family, tier) = MonsterFamilies.GetMonsterForLevel(targetLevel, random);

            // Try to avoid duplicate families for variety
            int attempts = 0;
            while (usedFamilies.Contains(family.FamilyName) && attempts < 5)
            {
                (family, tier) = MonsterFamilies.GetMonsterForLevel(targetLevel, random);
                attempts++;
            }
            usedFamilies.Add(family.FamilyName);

            var count = quest.Difficulty switch
            {
                1 => random.Next(3, 8),      // Easy: 3-7 monsters
                2 => random.Next(5, 12),     // Medium: 5-11 monsters
                3 => random.Next(8, 16),     // Hard: 8-15 monsters
                4 => random.Next(12, 21),    // Extreme: 12-20 monsters
                _ => 5
            };

            // Use the tier's MinLevel as a pseudo-type identifier for compatibility
            quest.Monsters.Add(new QuestMonster(tier.MinLevel, count, tier.Name));
        }
    }

    /// <summary>
    /// Generate objectives for dungeon quests
    /// Floor targets are capped to player-accessible range (playerLevel ± 10)
    /// </summary>
    private static void GenerateDungeonQuestObjectives(Quest quest, int playerLevel = 10)
    {
        quest.Objectives.Clear();

        // Calculate accessible floor range: player level ± 10
        int minAccessibleFloor = Math.Max(1, playerLevel - 10);
        int maxAccessibleFloor = Math.Min(100, playerLevel + 10);

        // Helper to cap floor to accessible range
        int CapFloor(int floor) => Math.Min(Math.Max(floor, minAccessibleFloor), maxAccessibleFloor);

        switch (quest.QuestTarget)
        {
            case QuestTarget.ClearBoss:
                // Kill a specific boss - use level-appropriate monster
                var (family, tier) = MonsterFamilies.GetMonsterForLevel(
                    Math.Min(playerLevel + quest.Difficulty * 3, maxAccessibleFloor), random);
                var bossName = $"{tier.Name} Champion";
                var bossId = tier.Name.ToLower().Replace(" ", "_") + "_champion";
                quest.Objectives.Add(new QuestObjective(
                    QuestObjectiveType.KillBoss,
                    $"Defeat the {bossName}",
                    1, bossId, bossName));
                quest.Title = $"Slay the {bossName}";
                break;

            case QuestTarget.FindArtifact:
                // Find an artifact on a specific floor - capped to accessible range
                var artifactFloor = CapFloor(playerLevel + quest.Difficulty * 2 + random.Next(-2, 3));
                var artifactId = $"artifact_{random.Next(1, 100)}";
                var artifactNames = new[] { "Ancient Amulet", "Crystal Orb", "Mystic Tome", "Golden Chalice", "Obsidian Blade" };
                var artifactName = artifactNames[random.Next(artifactNames.Length)];
                quest.Objectives.Add(new QuestObjective(
                    QuestObjectiveType.ReachDungeonFloor,
                    $"Descend to floor {artifactFloor}",
                    artifactFloor, "", $"Floor {artifactFloor}"));
                quest.Objectives.Add(new QuestObjective(
                    QuestObjectiveType.FindArtifact,
                    $"Retrieve the {artifactName}",
                    1, artifactId, artifactName));
                quest.Title = $"Recover the {artifactName}";
                break;

            case QuestTarget.ReachFloor:
                // Reach a specific floor - capped to accessible range
                var targetFloor = CapFloor(playerLevel + quest.Difficulty * 2 + random.Next(1, 4));
                quest.Objectives.Add(new QuestObjective(
                    QuestObjectiveType.ReachDungeonFloor,
                    $"Reach dungeon floor {targetFloor}",
                    targetFloor, "", $"Floor {targetFloor}"));
                // Optional: kill some monsters on the way
                quest.Objectives.Add(new QuestObjective(
                    QuestObjectiveType.KillMonsters,
                    "Defeat monsters along the way",
                    quest.Difficulty * 5, "", "Monsters") { IsOptional = true, BonusReward = 100 });
                quest.Title = $"Expedition to Floor {targetFloor}";
                break;

            case QuestTarget.ClearFloor:
                // Clear all monsters on a specific floor - capped to accessible range
                var clearFloor = CapFloor(playerLevel + quest.Difficulty + random.Next(-1, 3));
                quest.Objectives.Add(new QuestObjective(
                    QuestObjectiveType.ReachDungeonFloor,
                    $"Descend to floor {clearFloor}",
                    clearFloor, "", $"Floor {clearFloor}"));
                quest.Objectives.Add(new QuestObjective(
                    QuestObjectiveType.ClearDungeonFloor,
                    $"Clear all monsters on floor {clearFloor}",
                    1, clearFloor.ToString(), $"Floor {clearFloor}"));
                quest.Title = $"Clear Floor {clearFloor}";
                break;

            case QuestTarget.RescueNPC:
                // Rescue an NPC from a dungeon floor - capped to accessible range
                var rescueFloor = CapFloor(playerLevel + quest.Difficulty * 2 + random.Next(-1, 2));
                var npcNames = new[] { "Lady Elara", "Sir Marcus", "Priest Aldric", "Merchant Tobias", "Scholar Helena" };
                var npcName = npcNames[random.Next(npcNames.Length)];
                quest.Objectives.Add(new QuestObjective(
                    QuestObjectiveType.ReachDungeonFloor,
                    $"Reach floor {rescueFloor} where {npcName} is trapped",
                    rescueFloor, "", $"Floor {rescueFloor}"));
                quest.Objectives.Add(new QuestObjective(
                    QuestObjectiveType.TalkToNPC,
                    $"Find and rescue {npcName}",
                    1, npcName.ToLower().Replace(" ", "_"), npcName));
                quest.Title = $"Rescue {npcName}";
                break;

            case QuestTarget.SurviveDungeon:
                // Survive multiple floors - based on difficulty but within reason
                var surviveFloors = Math.Min(quest.Difficulty * 3 + random.Next(2, 5), 15);
                quest.Objectives.Add(new QuestObjective(
                    QuestObjectiveType.ReachDungeonFloor,
                    $"Survive {surviveFloors} consecutive floors",
                    surviveFloors, "", "Floors"));
                quest.Objectives.Add(new QuestObjective(
                    QuestObjectiveType.KillMonsters,
                    "Defeat at least 10 monsters",
                    10, "", "Monsters") { IsOptional = false });
                quest.Title = $"Survive {surviveFloors} Floors";
                break;
        }
    }

    /// <summary>
    /// Create a dungeon quest (bounty board style)
    /// </summary>
    public static Quest CreateDungeonQuest(QuestTarget target, byte difficulty, string dungeonName = "The Dungeon", int playerLevel = 10)
    {
        if (target < QuestTarget.ClearBoss || target > QuestTarget.SurviveDungeon)
        {
            throw new ArgumentException("Invalid dungeon quest target type");
        }

        var quest = new Quest
        {
            Initiator = "Bounty Board",
            QuestType = QuestType.SingleQuest,
            QuestTarget = target,
            Difficulty = difficulty,
            Comment = $"Dungeon quest in {dungeonName}",
            Date = DateTime.Now,
            MinLevel = Math.Max(1, playerLevel - 5),
            MaxLevel = playerLevel + 15,
            DaysToComplete = GameConfig.DefaultQuestDays + difficulty
        };

        // Generate objectives based on quest type with player level consideration
        GenerateDungeonQuestObjectives(quest, playerLevel);

        // Set rewards based on difficulty
        SetDefaultRewards(quest);

        // Add to database
        questDatabase.Add(quest);

        // GD.Print($"[QuestSystem] Dungeon quest created: {quest.Title}");

        return quest;
    }

    /// <summary>
    /// Get available dungeon quests from the bounty board
    /// </summary>
    public static List<Quest> GetBountyBoardQuests(Character player)
    {
        return questDatabase.Where(q =>
            !q.Deleted &&
            string.IsNullOrEmpty(q.Occupier) &&
            q.Initiator == "Bounty Board" &&
            player.Level >= q.MinLevel &&
            player.Level <= q.MaxLevel
        ).ToList();
    }

    /// <summary>
    /// Populate the bounty board with available quests (called on new day or when empty)
    /// </summary>
    public static void RefreshBountyBoard(int playerLevel)
    {
        // Remove old unclaimed bounty board quests
        questDatabase.RemoveAll(q => q.Initiator == "Bounty Board" && string.IsNullOrEmpty(q.Occupier) && q.Date < DateTime.Now.AddDays(-3));

        // Count existing bounty board quests
        var existingCount = questDatabase.Count(q => q.Initiator == "Bounty Board" && !q.Deleted && string.IsNullOrEmpty(q.Occupier));

        // Add quests until we have 5 available
        var targetCount = 5;
        while (existingCount < targetCount)
        {
            // Random difficulty based on player level
            var difficulty = (byte)Math.Min(4, Math.Max(1, (playerLevel / 5) + random.Next(-1, 2)));

            // Random dungeon quest type
            var questTypes = new[] { QuestTarget.ClearBoss, QuestTarget.FindArtifact, QuestTarget.ReachFloor, QuestTarget.ClearFloor, QuestTarget.SurviveDungeon };
            var questType = questTypes[random.Next(questTypes.Length)];

            CreateDungeonQuest(questType, difficulty, "The Dungeon", playerLevel);
            existingCount++;
        }

        // GD.Print($"[QuestSystem] Bounty board refreshed with {existingCount} quests");
    }
    
    /// <summary>
    /// Set default rewards based on difficulty
    /// Pascal: Default reward assignment
    /// </summary>
    private static void SetDefaultRewards(Quest quest)
    {
        // Reward level matches difficulty
        quest.Reward = quest.Difficulty switch
        {
            1 => GameConfig.QuestRewardLow,
            2 => GameConfig.QuestRewardMedium,
            3 => GameConfig.QuestRewardHigh,
            4 => GameConfig.QuestRewardHigh,
            _ => GameConfig.QuestRewardLow
        };
        
        // Random reward type (Pascal: Random reward assignment)
        quest.RewardType = (QuestRewardType)random.Next(1, 6); // 1-5 (skip Nothing)
        
        // Set penalty (usually lower than reward)
        // quest.Penalty = (byte)Math.Max(1, quest.Reward - 1);
        // quest.PenaltyType = quest.RewardType;
    }
    
    /// <summary>
    /// Validate quest completion requirements
    /// Uses the modern objective-based system if objectives exist,
    /// otherwise falls back to legacy QuestTarget-based validation
    /// </summary>
    private static bool ValidateQuestCompletion(Character player, Quest quest)
    {
        // If quest has objectives, use modern validation
        if (quest.Objectives != null && quest.Objectives.Count > 0)
        {
            // Check if all required (non-optional) objectives are complete
            return quest.Objectives
                .Where(o => !o.IsOptional)
                .All(o => o.IsComplete);
        }

        // Legacy validation for quests without objectives
        switch (quest.QuestTarget)
        {
            case QuestTarget.Monster:
                // Check if player killed enough monsters using quest-specific tracking
                // First check if there are KillMonsters objectives (preferred method)
                var killObjectives = quest.Objectives?.Where(o =>
                    o.ObjectiveType == QuestObjectiveType.KillMonsters ||
                    o.ObjectiveType == QuestObjectiveType.KillSpecificMonster).ToList();

                if (killObjectives != null && killObjectives.Count > 0)
                {
                    // Use objective-based tracking (quest-specific kills)
                    return killObjectives.All(o => o.IsComplete);
                }

                // Legacy fallback: check Monsters list against objectives progress
                if (quest.Monsters != null && quest.Monsters.Count > 0)
                {
                    int requiredKills = quest.Monsters.Sum(m => m.Count);
                    // Check if any kill objective has enough progress
                    var killProgress = quest.Objectives?.FirstOrDefault(o =>
                        o.ObjectiveType == QuestObjectiveType.KillMonsters);
                    if (killProgress != null)
                    {
                        return killProgress.CurrentProgress >= requiredKills;
                    }
                    // Last resort: assume incomplete if no tracking exists
                    return false;
                }
                return true;

            case QuestTarget.Assassin:
                // Check assassination mission completion
                return player.Assa > 0;

            case QuestTarget.Seduce:
                // Check seduction mission completion
                return player.IntimacyActs > 0;

            case QuestTarget.DefeatNPC:
                // NPC defeat quest - check if target NPC was defeated
                if (!string.IsNullOrEmpty(quest.TargetNPCName))
                {
                    // Quest is complete if NPC was marked as defeated (OccupiedDays set by OnNPCDefeated)
                    // Also check if DefeatNPC objectives are complete
                    bool objectivesComplete = quest.Objectives?.Any(o =>
                        o.ObjectiveType == QuestObjectiveType.DefeatNPC && o.IsComplete) ?? false;
                    return quest.OccupiedDays > 0 || objectivesComplete;
                }
                return true;

            default:
                return true; // Other quest types auto-complete for now
        }
    }
    
    /// <summary>
    /// Apply quest reward to player (Pascal: Reward application)
    /// </summary>
    private static void ApplyQuestReward(Character player, Quest quest, long rewardAmount, TerminalEmulator terminal)
    {
        // Fallback: if reward is 0 (e.g. old save data), give a minimum reward
        if (quest.Reward == 0 || rewardAmount == 0)
        {
            quest.Reward = 1;
            if (quest.RewardType == QuestRewardType.Nothing)
                quest.RewardType = QuestRewardType.Money;
            rewardAmount = quest.CalculateReward(player.Level);
            if (rewardAmount == 0)
                rewardAmount = player.Level * 100; // absolute fallback
        }

        switch (quest.RewardType)
        {
            case QuestRewardType.Experience:
                player.Experience += rewardAmount;
                terminal.WriteLine($"  Reward: {rewardAmount} experience points!", "bright_green");
                break;

            case QuestRewardType.Money:
                player.Gold += rewardAmount;
                terminal.WriteLine($"  Reward: {rewardAmount} gold!", "bright_yellow");
                break;

            case QuestRewardType.Potions:
                player.Healing += (int)rewardAmount;
                terminal.WriteLine($"  Reward: {rewardAmount} healing potions!", "bright_cyan");
                break;

            case QuestRewardType.Darkness:
                player.Darkness += (int)rewardAmount;
                terminal.WriteLine($"  Reward: {rewardAmount} darkness points!", "red");
                break;

            case QuestRewardType.Chivalry:
                player.Chivalry += (int)rewardAmount;
                terminal.WriteLine($"  Reward: {rewardAmount} chivalry points!", "bright_white");
                break;

            default:
                player.Gold += player.Level * 100;
                terminal.WriteLine($"  Reward: {player.Level * 100} gold!", "bright_yellow");
                break;
        }
    }
    
    /// <summary>
    /// Remove the purchased equipment from the player when turning in an equipment quest.
    /// Checks equipped slots and inventory for the matching item.
    /// </summary>
    private static void RemoveQuestEquipment(Character player, Quest quest, TerminalEmulator terminal)
    {
        // Find the target equipment name from the quest objectives
        var equipObjective = quest.Objectives.FirstOrDefault(o =>
            o.ObjectiveType == QuestObjectiveType.PurchaseEquipment);
        if (equipObjective == null) return;

        var targetName = equipObjective.TargetName;
        if (string.IsNullOrEmpty(targetName)) return;

        // Check equipped slots for the item
        foreach (var slot in Enum.GetValues<EquipmentSlot>())
        {
            var equipped = player.GetEquipment(slot);
            if (equipped != null && equipped.Name.Equals(targetName, StringComparison.OrdinalIgnoreCase))
            {
                player.UnequipSlot(slot);
                terminal.WriteLine($"  You hand over your {targetName} to the Merchant Guild.", "gray");
                return;
            }
        }

        // Check inventory (legacy Item list)
        var inventoryItem = player.Inventory?.FirstOrDefault(i =>
            i.Name.Equals(targetName, StringComparison.OrdinalIgnoreCase));
        if (inventoryItem != null)
        {
            player.Inventory.Remove(inventoryItem);
            terminal.WriteLine($"  You hand over your {targetName} to the Merchant Guild.", "gray");
        }
    }

    /// <summary>
    /// Process quest failure (Pascal: Quest failure handling)
    /// </summary>
    private static void ProcessQuestFailure(Quest quest)
    {
        // Send failure mail to player
        MailSystem.SendQuestFailureMail(quest.Occupier, quest.Title);
        
        // Send failure notification to king
        var kingName = quest.Initiator;
        MailSystem.SendQuestFailureNotificationMail(kingName, quest.Title);
        
        // Apply penalty if configured
        ApplyQuestPenalty(quest);
        
        // Mark quest as failed/deleted
        quest.Deleted = true;
        quest.Occupier = "";
        
        // GD.Print($"[QuestSystem] Quest failed: {quest.Id} by {quest.Occupier}");
    }
    
    /// <summary>
    /// Abandon a quest - player voluntarily gives up with no penalty
    /// </summary>
    public static void AbandonQuest(Character player, string questId)
    {
        var quest = questDatabase.FirstOrDefault(q => q.Id == questId);
        if (quest != null)
        {
            quest.Deleted = true;
            quest.Occupier = "";
        }
        player.ActiveQuests.RemoveAll(q => q.Id == questId);
    }

    /// <summary>
    /// Apply quest failure penalty
    /// </summary>
    private static void ApplyQuestPenalty(Quest quest)
    {
        // In a full implementation, would load player and apply penalties
        // For now, just log the penalty
        var penaltyAmount = quest.CalculateReward(10); // Use level 10 as default
        // GD.Print($"[QuestSystem] Penalty applied: {quest.PenaltyType} -{penaltyAmount}");
    }
    
    /// <summary>
    /// Send quest completion mail to king
    /// </summary>
    private static void SendQuestCompletionMail(Character player, Quest quest, long rewardAmount)
    {
        MailSystem.SendQuestCompletionMail(player.Name2, quest.Title, rewardAmount);
    }
    
    /// <summary>
    /// Generate news for quest completion
    /// </summary>
    private static void GenerateQuestCompletionNews(Character player, Quest quest)
    {
        var newsLines = new[]
        {
            $"{player.Name2} completed a {quest.GetDifficultyString()} quest!",
            $"{player.Name2} returned home and received a reward."
        };
        
        MailSystem.SendNewsMail("Successful Quest!", newsLines);
    }
    
    /// <summary>
    /// Clean up old completed quests
    /// </summary>
    private static void CleanupOldQuests()
    {
        int totalRemoved = 0;

        // Remove deleted quests older than 30 days
        totalRemoved += questDatabase.RemoveAll(q => q.Deleted && q.Date < DateTime.Now.AddDays(-30));

        // Remove unclaimed quests older than 7 days (stale board quests)
        totalRemoved += questDatabase.RemoveAll(q =>
            !q.Deleted &&
            string.IsNullOrEmpty(q.Occupier) &&
            q.Date < DateTime.Now.AddDays(-7));

        // Hard cap: if database still exceeds 200 quests, remove oldest unclaimed first
        if (questDatabase.Count > 200)
        {
            var unclaimed = questDatabase
                .Where(q => string.IsNullOrEmpty(q.Occupier) && !q.Deleted)
                .OrderBy(q => q.Date)
                .ToList();

            int toRemove = questDatabase.Count - 200;
            foreach (var q in unclaimed.Take(toRemove))
            {
                questDatabase.Remove(q);
                totalRemoved++;
            }
        }

        if (totalRemoved > 0)
        {
        }
    }
    
    /// <summary>
    /// Get quest rankings (Pascal: Quest master rankings)
    /// </summary>
    public static List<QuestRanking> GetQuestRankings()
    {
        // In a full implementation, would load all players and rank by quest completions
        // For now, return empty list
        return new List<QuestRanking>();
    }
    
    /// <summary>
    /// Get all quests (for king/admin view)
    /// </summary>
    public static List<Quest> GetAllQuests(bool includeCompleted = false)
    {
        return includeCompleted ?
            questDatabase.ToList() :
            questDatabase.Where(q => !q.Deleted).ToList();
    }

    /// <summary>
    /// Restore quests from save data
    /// </summary>
    public static void RestoreFromSaveData(List<QuestData> savedQuests)
    {
        // Clear existing quests
        questDatabase.Clear();

        if (savedQuests == null || savedQuests.Count == 0)
        {
            // GD.Print("[QuestSystem] No saved quests to restore");
            return;
        }

        foreach (var questData in savedQuests)
        {
            var quest = new Quest
            {
                Id = questData.Id,
                Title = questData.Title,
                Initiator = questData.Initiator,
                Comment = questData.Comment,
                Date = questData.StartTime,
                QuestType = (QuestType)questData.QuestType,
                QuestTarget = (QuestTarget)questData.QuestTarget,
                Difficulty = (byte)questData.Difficulty,
                Occupier = questData.Occupier,
                OccupiedDays = questData.OccupiedDays,
                DaysToComplete = questData.DaysToComplete,
                MinLevel = questData.MinLevel,
                MaxLevel = questData.MaxLevel,
                Reward = (byte)questData.Reward,
                RewardType = (QuestRewardType)questData.RewardType,
                Penalty = (byte)questData.Penalty,
                PenaltyType = (QuestRewardType)questData.PenaltyType,
                OfferedTo = questData.OfferedTo,
                Forced = questData.Forced,
                TargetNPCName = questData.TargetNPCName ?? "",
                Deleted = questData.Status == QuestStatus.Completed || questData.Status == QuestStatus.Failed
            };

            // Restore objectives
            foreach (var objData in questData.Objectives)
            {
                quest.Objectives.Add(new QuestObjective
                {
                    Id = objData.Id,
                    Description = objData.Description,
                    ObjectiveType = (QuestObjectiveType)objData.ObjectiveType,
                    TargetId = objData.TargetId,
                    TargetName = objData.TargetName,
                    RequiredProgress = objData.RequiredProgress,
                    CurrentProgress = objData.CurrentProgress,
                    IsOptional = objData.IsOptional,
                    BonusReward = objData.BonusReward
                });
            }

            // Restore monsters
            foreach (var monsterData in questData.Monsters)
            {
                quest.Monsters.Add(new QuestMonster(
                    monsterData.MonsterType,
                    monsterData.Count,
                    monsterData.MonsterName
                ));
            }

            questDatabase.Add(quest);
        }

        // GD.Print($"[QuestSystem] Restored {questDatabase.Count} quests from save data");
    }

    /// <summary>
    /// Merge unclaimed world/board quests into the database without clearing existing player quests.
    /// Used in single-player mode after player quests have already been loaded.
    /// </summary>
    public static void MergeWorldQuests(List<QuestData> savedQuests)
    {
        if (savedQuests == null || savedQuests.Count == 0) return;

        // Remove existing unclaimed quests (they'll be replaced by saved data)
        questDatabase.RemoveAll(q => string.IsNullOrEmpty(q.Occupier));

        foreach (var questData in savedQuests)
        {
            // Skip if this quest ID already exists (player quest with same ID)
            if (questDatabase.Any(q => q.Id == questData.Id)) continue;

            var quest = new Quest
            {
                Id = questData.Id,
                Title = questData.Title,
                Initiator = questData.Initiator,
                Comment = questData.Comment,
                Date = questData.StartTime,
                QuestType = (QuestType)questData.QuestType,
                QuestTarget = (QuestTarget)questData.QuestTarget,
                Difficulty = (byte)questData.Difficulty,
                Occupier = questData.Occupier,
                OccupiedDays = questData.OccupiedDays,
                DaysToComplete = questData.DaysToComplete,
                MinLevel = questData.MinLevel,
                MaxLevel = questData.MaxLevel,
                Reward = (byte)questData.Reward,
                RewardType = (QuestRewardType)questData.RewardType,
                Penalty = (byte)questData.Penalty,
                PenaltyType = (QuestRewardType)questData.PenaltyType,
                OfferedTo = questData.OfferedTo,
                Forced = questData.Forced,
                TargetNPCName = questData.TargetNPCName ?? "",
                Deleted = questData.Status == QuestStatus.Completed || questData.Status == QuestStatus.Failed
            };

            foreach (var objData in questData.Objectives)
            {
                quest.Objectives.Add(new QuestObjective
                {
                    Id = objData.Id,
                    Description = objData.Description,
                    ObjectiveType = (QuestObjectiveType)objData.ObjectiveType,
                    TargetId = objData.TargetId,
                    TargetName = objData.TargetName,
                    RequiredProgress = objData.RequiredProgress,
                    CurrentProgress = objData.CurrentProgress,
                    IsOptional = objData.IsOptional,
                    BonusReward = objData.BonusReward
                });
            }

            foreach (var monsterData in questData.Monsters)
            {
                quest.Monsters.Add(new QuestMonster(
                    monsterData.MonsterType,
                    monsterData.Count,
                    monsterData.MonsterName
                ));
            }

            questDatabase.Add(quest);
        }

        DebugLogger.Instance.LogDebug("QUEST", $"Merged {savedQuests.Count} world quests (total: {questDatabase.Count})");
    }

    /// <summary>
    /// Merge a player's quests into the shared database without clearing other players' quests.
    /// Used in MUD/online mode where multiple players share the static questDatabase.
    /// Removes any existing quests for this player, then adds back from save data.
    /// </summary>
    public static void MergePlayerQuests(string playerName, List<QuestData> savedQuests)
    {
        // Remove only THIS player's quests from the shared database
        questDatabase.RemoveAll(q =>
            string.Equals(q.Occupier, playerName, StringComparison.OrdinalIgnoreCase) ||
            string.Equals(q.OfferedTo, playerName, StringComparison.OrdinalIgnoreCase));

        if (savedQuests == null || savedQuests.Count == 0)
            return;

        // Add back this player's quests from their save data
        foreach (var questData in savedQuests)
        {
            var quest = new Quest
            {
                Id = questData.Id,
                Title = questData.Title,
                Initiator = questData.Initiator,
                Comment = questData.Comment,
                Date = questData.StartTime,
                QuestType = (QuestType)questData.QuestType,
                QuestTarget = (QuestTarget)questData.QuestTarget,
                Difficulty = (byte)questData.Difficulty,
                Occupier = questData.Occupier,
                OccupiedDays = questData.OccupiedDays,
                DaysToComplete = questData.DaysToComplete,
                MinLevel = questData.MinLevel,
                MaxLevel = questData.MaxLevel,
                Reward = (byte)questData.Reward,
                RewardType = (QuestRewardType)questData.RewardType,
                Penalty = (byte)questData.Penalty,
                PenaltyType = (QuestRewardType)questData.PenaltyType,
                OfferedTo = questData.OfferedTo,
                Forced = questData.Forced,
                TargetNPCName = questData.TargetNPCName ?? "",
                Deleted = questData.Status == QuestStatus.Completed || questData.Status == QuestStatus.Failed
            };

            foreach (var objData in questData.Objectives)
            {
                quest.Objectives.Add(new QuestObjective
                {
                    Id = objData.Id,
                    Description = objData.Description,
                    ObjectiveType = (QuestObjectiveType)objData.ObjectiveType,
                    TargetId = objData.TargetId,
                    TargetName = objData.TargetName,
                    RequiredProgress = objData.RequiredProgress,
                    CurrentProgress = objData.CurrentProgress,
                    IsOptional = objData.IsOptional,
                    BonusReward = objData.BonusReward
                });
            }

            foreach (var monsterData in questData.Monsters)
            {
                quest.Monsters.Add(new QuestMonster(
                    monsterData.MonsterType,
                    monsterData.Count,
                    monsterData.MonsterName
                ));
            }

            questDatabase.Add(quest);
        }
    }

    /// <summary>
    /// Clear all quests (for testing or new game)
    /// </summary>
    public static void ClearAllQuests()
    {
        questDatabase.Clear();
        // GD.Print("[QuestSystem] Quest database cleared");
    }

    #region Quest Progress Tracking

    /// <summary>
    /// Update quest progress when a monster is killed
    /// Call this from CombatEngine after monster defeat
    /// </summary>
    public static void OnMonsterKilled(Character player, string monsterName, bool isBoss = false)
    {
        var playerQuests = GetPlayerQuests(player.Name2);

        foreach (var quest in playerQuests)
        {
            // Update kill monster objectives
            quest.UpdateObjectiveProgress(QuestObjectiveType.KillMonsters, 1);
            quest.UpdateObjectiveProgress(QuestObjectiveType.KillSpecificMonster, 1, monsterName.ToLower().Replace(" ", "_"));

            // Update boss kill objectives
            if (isBoss)
            {
                quest.UpdateObjectiveProgress(QuestObjectiveType.KillBoss, 1, monsterName.ToLower().Replace(" ", "_"));
            }
        }
    }

    /// <summary>
    /// Update quest progress when player reaches a dungeon floor
    /// Call this from DungeonLocation when entering a floor
    /// </summary>
    public static void OnDungeonFloorReached(Character player, int floorNumber)
    {
        var playerQuests = GetPlayerQuests(player.Name2);

        foreach (var quest in playerQuests)
        {
            // Update floor objectives - set progress to floor number reached
            foreach (var objective in quest.Objectives.Where(o =>
                o.ObjectiveType == QuestObjectiveType.ReachDungeonFloor && !o.IsComplete))
            {
                if (floorNumber >= objective.RequiredProgress)
                {
                    objective.CurrentProgress = objective.RequiredProgress;
                }
                else if (floorNumber > objective.CurrentProgress)
                {
                    objective.CurrentProgress = floorNumber;
                }
            }
        }
    }

    /// <summary>
    /// Update quest progress when a dungeon floor is fully cleared (all monsters defeated)
    /// Call this from DungeonLocation when IsFloorCleared() becomes true
    /// </summary>
    public static void OnDungeonFloorCleared(Character player, int floorNumber)
    {
        var playerQuests = GetPlayerQuests(player.Name2);

        foreach (var quest in playerQuests)
        {
            quest.UpdateObjectiveProgress(QuestObjectiveType.ClearDungeonFloor, 1, floorNumber.ToString());
        }
    }

    /// <summary>
    /// Update quest progress when player finds an artifact
    /// Call this from DungeonLocation or treasure system
    /// </summary>
    public static void OnArtifactFound(Character player, string artifactId)
    {
        var playerQuests = GetPlayerQuests(player.Name2);

        foreach (var quest in playerQuests)
        {
            quest.UpdateObjectiveProgress(QuestObjectiveType.FindArtifact, 1, artifactId);
        }
    }

    /// <summary>
    /// Update quest progress when player talks to an NPC
    /// Call this from InnLocation or other NPC interaction points
    /// </summary>
    public static void OnNPCInteraction(Character player, string npcId)
    {
        var playerQuests = GetPlayerQuests(player.Name2);

        foreach (var quest in playerQuests)
        {
            quest.UpdateObjectiveProgress(QuestObjectiveType.TalkToNPC, 1, npcId);
        }
    }

    /// <summary>
    /// Update quest progress when player defeats an NPC (bounty system)
    /// Call this from StreetEncounterSystem and BaseLocation.ChallengeNPC when NPC is killed
    /// </summary>
    public static void OnNPCDefeated(Character player, NPC defeatedNPC)
    {
        if (player == null || defeatedNPC == null) return;

        string npcName = defeatedNPC.Name ?? defeatedNPC.Name2 ?? "";
        string npcNameLower = npcName.ToLower().Replace(" ", "_");

        // First, update any claimed quests for this player
        var playerQuests = GetPlayerQuests(player.Name2);
        foreach (var quest in playerQuests)
        {
            // Check if this quest is a bounty targeting this specific NPC
            if (!string.IsNullOrEmpty(quest.TargetNPCName))
            {
                string targetLower = quest.TargetNPCName.ToLower().Replace(" ", "_");
                if (targetLower == npcNameLower || quest.TargetNPCName.Equals(npcName, StringComparison.OrdinalIgnoreCase))
                {
                    // Mark the DefeatNPC objective as complete
                    quest.UpdateObjectiveProgress(QuestObjectiveType.DefeatNPC, 1, npcName);

                    // Also mark the quest as having activity (for legacy validation)
                    quest.OccupiedDays = Math.Max(1, quest.OccupiedDays);
                }
            }

            // Generic NPC defeat objectives
            quest.UpdateObjectiveProgress(QuestObjectiveType.DefeatNPC, 1, npcName);
        }

        // Note: AutoCompleteBountyForNPC should be called by the combat system
        // to show immediate feedback to the player. We just refresh the bounty board here.
        RefreshKingBounties();
    }

    /// <summary>
    /// Auto-complete unclaimed bounties when player kills the target NPC
    /// Gives immediate reward without needing to claim first
    /// Returns the total bounty reward collected (0 if no bounties matched)
    /// </summary>
    public static long AutoCompleteBountyForNPC(Character player, string npcName)
    {
        if (string.IsNullOrEmpty(npcName)) return 0;

        long totalReward = 0;

        // Find ALL bounties targeting this NPC (claimed or unclaimed)
        var matchingBounties = questDatabase.Where(q =>
            !q.Deleted &&
            !string.IsNullOrEmpty(q.TargetNPCName) &&
            q.TargetNPCName.Equals(npcName, StringComparison.OrdinalIgnoreCase)
        ).ToList();

        foreach (var bounty in matchingBounties)
        {
            // Calculate reward
            long reward = bounty.Reward * 100; // Reward is stored as /100
            if (reward <= 0) reward = 500; // Minimum reward

            // Give player the reward immediately
            player.Gold += reward;
            player.Experience += bounty.Reward * 10;
            totalReward += reward;

            // Mark bounty as completed
            bounty.Deleted = true;
            bounty.Occupier = player.Name2;
            bounty.OccupiedDays = 1;

            // Update statistics
            StatisticsManager.Current?.RecordBountyComplete();

            // Announce the bounty completion
            NewsSystem.Instance?.Newsy(true, $"{player.Name2} collected the bounty on {npcName}! (+{reward:N0} gold)");

            // GD.Print($"[QuestSystem] Auto-completed bounty on {npcName} for {player.Name2}, reward: {reward} gold");
        }

        return totalReward;
    }

    /// <summary>
    /// Check if a player has an active bounty/contract for a specific NPC.
    /// Returns the initiator string (e.g. "The Crown", "The Shadows", "Bounty Board")
    /// or null if no matching bounty exists. Used by blood price system to determine
    /// whether a kill is justified (Crown/King bounty) or a paid hit (Shadows contract).
    /// </summary>
    public static string? GetActiveBountyInitiator(string playerName, string npcName)
    {
        if (string.IsNullOrEmpty(npcName)) return null;

        // Check claimed quests first (player accepted the bounty)
        var claimed = questDatabase.FirstOrDefault(q =>
            !q.Deleted &&
            !string.IsNullOrEmpty(q.TargetNPCName) &&
            q.TargetNPCName.Equals(npcName, StringComparison.OrdinalIgnoreCase) &&
            !string.IsNullOrEmpty(q.Occupier) &&
            q.Occupier.Equals(playerName, StringComparison.OrdinalIgnoreCase) &&
            (q.QuestTarget == QuestTarget.DefeatNPC));

        if (claimed != null) return claimed.Initiator;

        // Also check unclaimed bounties (auto-complete path)
        var unclaimed = questDatabase.FirstOrDefault(q =>
            !q.Deleted &&
            !string.IsNullOrEmpty(q.TargetNPCName) &&
            q.TargetNPCName.Equals(npcName, StringComparison.OrdinalIgnoreCase) &&
            string.IsNullOrEmpty(q.Occupier) &&
            (q.QuestTarget == QuestTarget.DefeatNPC));

        return unclaimed?.Initiator;
    }

    /// <summary>
    /// Remove all bounties targeting a specific NPC (when they die) and refresh the bounty board
    /// </summary>
    private static void RemoveBountiesForDeadNPC(string npcName)
    {
        if (string.IsNullOrEmpty(npcName)) return;

        // Find and remove unclaimed bounties targeting this NPC
        var bountiesRemoved = questDatabase.RemoveAll(q =>
            !string.IsNullOrEmpty(q.TargetNPCName) &&
            q.TargetNPCName.Equals(npcName, StringComparison.OrdinalIgnoreCase) &&
            string.IsNullOrEmpty(q.Occupier) && // Only remove unclaimed bounties
            !q.Deleted);

        // If we removed any bounties, spawn replacements
        if (bountiesRemoved > 0)
        {
            // GD.Print($"[QuestSystem] Removed {bountiesRemoved} bounties for dead NPC: {npcName}");
            RefreshKingBounties();
        }
    }

    /// <summary>
    /// Update quest progress when player visits a location
    /// Call this from BaseLocation.EnterLocation
    /// </summary>
    public static void OnLocationVisited(Character player, string locationId)
    {
        var playerQuests = GetPlayerQuests(player.Name2);

        foreach (var quest in playerQuests)
        {
            quest.UpdateObjectiveProgress(QuestObjectiveType.VisitLocation, 1, locationId);
        }
    }

    /// <summary>
    /// Update quest progress when gold is collected
    /// </summary>
    public static void OnGoldCollected(Character player, long amount)
    {
        var playerQuests = GetPlayerQuests(player.Name2);

        foreach (var quest in playerQuests)
        {
            quest.UpdateObjectiveProgress(QuestObjectiveType.CollectGold, (int)Math.Min(amount, int.MaxValue));
        }
    }

    /// <summary>
    /// Check if player has any completed quests ready to turn in
    /// </summary>
    public static List<Quest> GetCompletedQuests(Character player)
    {
        var playerQuests = GetPlayerQuests(player.Name2);
        return playerQuests.Where(q => q.AreAllObjectivesComplete()).ToList();
    }

    /// <summary>
    /// Get quest progress summary for display
    /// </summary>
    public static string GetQuestProgressSummary(Quest quest)
    {
        if (quest.Objectives.Count == 0)
        {
            return "No tracked objectives";
        }

        var completed = quest.Objectives.Count(o => o.IsComplete && !o.IsOptional);
        var required = quest.Objectives.Count(o => !o.IsOptional);
        var optional = quest.Objectives.Count(o => o.IsOptional && o.IsComplete);
        var totalOptional = quest.Objectives.Count(o => o.IsOptional);

        var summary = $"Progress: {completed}/{required} objectives complete";
        if (totalOptional > 0)
        {
            summary += $" (+{optional}/{totalOptional} bonus)";
        }

        return summary;
    }

    #endregion

    #region Starter Quests

    /// <summary>
    /// Initialize starter quests for new games or when quest board is empty
    /// Creates a variety of quests appropriate for different player levels
    /// </summary>
    public static void InitializeStarterQuests()
    {
        // Remove unclaimed Royal Council starter quests so they get regenerated
        // with updated monster names and objectives. Claimed quests are preserved.
        questDatabase.RemoveAll(q =>
            q.Initiator == "Royal Council" &&
            string.IsNullOrEmpty(q.Occupier) &&
            !q.Deleted);

        // Don't add if there are already Royal Council quests (player has claimed some)
        if (questDatabase.Any(q => q.Initiator == "Royal Council"))
            return;

        // Beginner quests (levels 1-15)
        CreateStarterQuest("Wolf Pack",
            "Wolves have been menacing travelers near the dungeon. Hunt them down.",
            QuestTarget.Monster, 1, 1, 15,
            new[] { ("Wolf", 5), ("Dire Wolf", 2) });

        CreateStarterQuest("Goblin Menace",
            "Goblins from the dungeon have been raiding merchant caravans. Thin their numbers.",
            QuestTarget.Monster, 1, 1, 15,
            new[] { ("Goblin", 4), ("Hobgoblin", 2) });

        CreateStarterQuest("Undead Rising",
            "The undead are stirring in the dungeon depths. Destroy them before they spread.",
            QuestTarget.Monster, 2, 5, 20,
            new[] { ("Zombie", 5), ("Ghoul", 2) });

        // Intermediate quests (levels 10-35)
        CreateStarterQuest("The Orc Warlord",
            "An orc warband grows in strength deep in the dungeon. Cull their forces.",
            QuestTarget.Monster, 2, 10, 35,
            new[] { ("Orc", 6), ("Orc Warrior", 3), ("Orc Berserker", 1) });

        CreateStarterQuest("Troll Hunt",
            "Ogres and trolls lurk in the dungeon's middle levels. Clear them out.",
            QuestTarget.Monster, 2, 15, 40,
            new[] { ("Ogre", 3), ("Troll", 2) });

        CreateStarterQuest("Dungeon Delve",
            "Explore the dungeon depths and report back what you find.",
            QuestTarget.ReachFloor, 2, 10, 40,
            floorTarget: 10);

        // Advanced quests (levels 20-55)
        CreateStarterQuest("Dragon Hunt",
            "Draconic creatures infest the deeper dungeon levels. Slay them.",
            QuestTarget.Monster, 3, 20, 55,
            new[] { ("Drake", 3), ("Wyvern", 1) });

        CreateStarterQuest("The Deep Descent",
            "Reach the 25th floor of the dungeon.",
            QuestTarget.ReachFloor, 3, 20, 60,
            floorTarget: 25);

        CreateStarterQuest("Deep Exploration",
            "Scouts report strange activity on the 15th floor. Investigate the depths.",
            QuestTarget.ReachFloor, 3, 10, 40,
            floorTarget: 15);

        // Expert quests (levels 50+)
        CreateStarterQuest("The Lich King",
            "An ancient lich commands undead legions in the deep dungeon. Destroy them all.",
            QuestTarget.Monster, 4, 50, 100,
            new[] { ("Lich", 1), ("Wraith", 4), ("Shade", 3) });

        CreateStarterQuest("Abyssal Expedition",
            "Reach the 50th floor of the dungeon.",
            QuestTarget.ReachFloor, 4, 35, 100,
            floorTarget: 50);

        // GD.Print($"[QuestSystem] Created {questDatabase.Count} starter quests");
    }

    /// <summary>
    /// Helper to create a starter quest
    /// </summary>
    private static void CreateStarterQuest(string title, string description, QuestTarget target,
        byte difficulty, int minLevel, int maxLevel,
        (string name, int count)[]? monsters = null, int floorTarget = 0)
    {
        var quest = new Quest
        {
            Title = title,
            Initiator = "Royal Council",
            QuestType = QuestType.SingleQuest,
            QuestTarget = target,
            Difficulty = difficulty,
            Comment = description,
            Date = DateTime.Now,
            MinLevel = minLevel,
            MaxLevel = maxLevel,
            DaysToComplete = 14 // Generous time limit for starter quests
        };

        // Add monsters if specified
        if (monsters != null)
        {
            foreach (var (name, count) in monsters)
            {
                quest.Monsters.Add(new QuestMonster(0, count, name));
            }
        }

        // Add objectives based on quest type
        if (target == QuestTarget.Monster && monsters != null)
        {
            // Create a KillSpecificMonster objective for each monster type
            foreach (var (name, count) in monsters)
            {
                quest.Objectives.Add(new QuestObjective(
                    QuestObjectiveType.KillSpecificMonster,
                    $"Kill {count} {name}{(count > 1 ? "s" : "")}",
                    count,
                    name.ToLower().Replace(" ", "_"),
                    name
                ));
            }
        }
        else if (target == QuestTarget.ReachFloor && floorTarget > 0)
        {
            quest.Objectives.Add(new QuestObjective(
                QuestObjectiveType.ReachDungeonFloor,
                $"Reach floor {floorTarget}",
                floorTarget,
                "",
                $"Floor {floorTarget}"
            ));
        }
        else if (target == QuestTarget.ClearBoss)
        {
            var bossName = monsters?.FirstOrDefault().name ?? "Boss";
            quest.Objectives.Add(new QuestObjective(
                QuestObjectiveType.KillBoss,
                $"Defeat the {bossName}",
                1,
                "",
                bossName
            ));
        }

        // Set rewards
        SetDefaultRewards(quest);

        questDatabase.Add(quest);
    }

    /// <summary>
    /// Ensure quests exist - call on game start
    /// </summary>
    public static void EnsureQuestsExist(int playerLevel = 10)
    {
        // Always call — InitializeStarterQuests handles its own
        // idempotency (removes stale unclaimed quests, skips if claimed exist)
        InitializeStarterQuests();

        // Also ensure King bounties exist
        RefreshKingBounties();

        // Also ensure equipment quests exist
        EnsureEquipmentQuestsExist(playerLevel);
    }

    #endregion

    #region King Bounty System

    private const string KING_BOUNTY_INITIATOR = "The Crown";

    /// <summary>
    /// Get all bounties posted by the King
    /// </summary>
    public static List<Quest> GetKingBounties()
    {
        return questDatabase.Where(q =>
            !q.Deleted &&
            q.Initiator == KING_BOUNTY_INITIATOR
        ).ToList();
    }

    /// <summary>
    /// Generate bounties posted by the NPC King
    /// Called periodically by WorldSimulator or on game start
    /// </summary>
    public static void RefreshKingBounties()
    {
        var king = CastleLocation.GetCurrentKing();
        if (king == null) return;

        // Remove old unclaimed King bounties (older than 7 days)
        questDatabase.RemoveAll(q =>
            q.Initiator == KING_BOUNTY_INITIATOR &&
            string.IsNullOrEmpty(q.Occupier) &&
            q.Date < DateTime.Now.AddDays(-7));

        // Count existing King bounties
        var existingCount = questDatabase.Count(q =>
            q.Initiator == KING_BOUNTY_INITIATOR &&
            !q.Deleted);

        // King maintains 3-5 active bounties
        var targetCount = 3 + random.Next(3);

        while (existingCount < targetCount)
        {
            CreateKingBounty(king.Name);
            existingCount++;
        }

        // GD.Print($"[QuestSystem] King bounties refreshed: {existingCount} active");
    }

    /// <summary>
    /// Create a bounty from the King targeting an NPC or criminal
    /// </summary>
    private static void CreateKingBounty(string kingName)
    {
        // Get list of NPCs that already have bounties on them (avoid duplicates)
        var existingBountyTargets = questDatabase
            .Where(q => q.Initiator == KING_BOUNTY_INITIATOR && !q.Deleted && !string.IsNullOrEmpty(q.TargetNPCName))
            .Select(q => q.TargetNPCName.ToLower())
            .ToHashSet();

        // Get list of potential targets (NPCs who aren't the King, guards, story NPCs, or already have bounties)
        var potentialTargets = NPCSpawnSystem.Instance?.ActiveNPCs?
            .Where(n => n.IsAlive &&
                       !n.King &&
                       !n.IsStoryNPC &&
                       n.Level >= 5 &&
                       !existingBountyTargets.Contains((n.Name ?? n.Name2 ?? "").ToLower()))
            .ToList() ?? new List<NPC>();

        Quest bounty;

        // 70% chance to target an NPC, 30% chance for generic criminal bounty
        if (potentialTargets.Count > 0 && random.Next(100) < 70)
        {
            // Target a specific NPC
            var target = potentialTargets[random.Next(potentialTargets.Count)];
            bounty = CreateNPCBounty(target, kingName);
        }
        else
        {
            // Generic criminal bounty
            bounty = CreateGenericBounty(kingName);
        }

        if (bounty != null)
        {
            questDatabase.Add(bounty);
            NewsSystem.Instance?.Newsy(true, $"The Crown has posted a new bounty: {bounty.Title}");
        }
    }

    /// <summary>
    /// Create a bounty targeting a specific NPC
    /// </summary>
    private static Quest CreateNPCBounty(NPC target, string kingName)
    {
        var crimes = new[]
        {
            "wanted for crimes against the Crown",
            "accused of treason",
            "suspected of smuggling contraband",
            "charged with theft from the Royal Treasury",
            "wanted for disturbing the King's peace",
            "accused of dark sorcery",
            "wanted for assault on a royal guard",
            "suspected of plotting rebellion"
        };

        var crime = crimes[random.Next(crimes.Length)];
        var difficulty = (byte)Math.Min(4, Math.Max(1, target.Level / 15 + 1));
        var reward = target.Level * 100 * difficulty;

        var bounty = new Quest
        {
            Title = $"WANTED: {target.Name}",
            Initiator = KING_BOUNTY_INITIATOR,
            QuestType = QuestType.SingleQuest,
            QuestTarget = QuestTarget.Assassin,
            Difficulty = difficulty,
            Comment = $"{target.Name} is {crime}. Bring them to justice!",
            Date = DateTime.Now,
            MinLevel = Math.Max(1, target.Level - 10),
            MaxLevel = 9999,
            DaysToComplete = 14,
            Reward = (byte)Math.Min(255, reward / 100),
            RewardType = QuestRewardType.Money,
            TargetNPCName = target.Name
        };

        bounty.Objectives.Add(new QuestObjective(
            QuestObjectiveType.DefeatNPC,
            $"Defeat {target.Name}",
            1,
            target.Name,
            target.Name
        ));

        return bounty;
    }

    /// <summary>
    /// Create a generic criminal bounty (not targeting a specific NPC)
    /// </summary>
    private static Quest CreateGenericBounty(string kingName)
    {
        var bountyTypes = new[]
        {
            ("Bandit Leader", "Hunt down a bandit leader and their gang in the dungeon", 5),
            ("Escaped Prisoner", "A dangerous criminal has escaped into the dungeon", 3),
            ("Cult Leader", "Root out a dark cult hiding in the dungeon depths", 6),
            ("Rogue Mage", "A mage gone mad lurks in the dungeon", 4),
            ("Orc Warlord", "An orc warlord and raiders have retreated into the dungeon", 7)
        };

        var (title, desc, killCount) = bountyTypes[random.Next(bountyTypes.Length)];
        var difficulty = (byte)(random.Next(1, 5)); // 1-4 difficulty
        killCount += difficulty * 2; // Scale kills with difficulty

        var bounty = new Quest
        {
            Title = $"WANTED: {title}",
            Initiator = KING_BOUNTY_INITIATOR,
            QuestType = QuestType.SingleQuest,
            QuestTarget = QuestTarget.Monster,
            Difficulty = difficulty,
            Comment = desc,
            Date = DateTime.Now,
            MinLevel = difficulty * 5,
            MaxLevel = 9999,
            DaysToComplete = 14
        };

        bounty.Objectives.Add(new QuestObjective(
            QuestObjectiveType.KillMonsters,
            $"Slay {killCount} monsters in the dungeon",
            killCount
        ));

        SetDefaultRewards(bounty);
        bounty.Reward = (byte)Math.Min(255, bounty.Reward * 2); // King bounties pay double

        return bounty;
    }

    /// <summary>
    /// The King can post a bounty on the player if they commit crimes
    /// </summary>
    public static void PostBountyOnPlayer(string playerName, string crime, int bountyAmount)
    {
        var king = CastleLocation.GetCurrentKing();
        if (king == null) return;

        // Check if player already has an active bounty
        var existingBounty = questDatabase.FirstOrDefault(q =>
            q.Initiator == KING_BOUNTY_INITIATOR &&
            q.TargetNPCName == playerName &&
            !q.Deleted);

        if (existingBounty != null)
        {
            // Increase existing bounty
            existingBounty.Reward = (byte)Math.Min(255, existingBounty.Reward + bountyAmount / 100);
            existingBounty.Comment += $" Additional charge: {crime}.";
            NewsSystem.Instance?.Newsy(true, $"The bounty on {playerName} has increased!");
            return;
        }

        var bounty = new Quest
        {
            Title = $"WANTED: {playerName}",
            Initiator = KING_BOUNTY_INITIATOR,
            QuestType = QuestType.SingleQuest,
            QuestTarget = QuestTarget.Assassin,
            Difficulty = 4, // Player bounties are always hard
            Comment = $"{playerName} is wanted for {crime}. Bring them to justice!",
            Date = DateTime.Now,
            MinLevel = 1,
            MaxLevel = 9999,
            DaysToComplete = 30, // Long duration for player bounties
            Reward = (byte)Math.Min(255, bountyAmount / 100),
            RewardType = QuestRewardType.Money,
            TargetNPCName = playerName
        };

        bounty.Objectives.Add(new QuestObjective(
            QuestObjectiveType.DefeatNPC,
            $"Defeat {playerName}",
            1,
            playerName,
            playerName
        ));

        questDatabase.Add(bounty);
        NewsSystem.Instance?.Newsy(true, $"The Crown has posted a bounty on {playerName}!");
        // GD.Print($"[QuestSystem] Bounty posted on player {playerName} for {crime}");
    }

    #endregion

    #region Royal Audience Quests

    /// <summary>
    /// Create a special royal quest from a direct audience with the king
    /// These are personal quests given directly to the player with better rewards
    /// </summary>
    public static Quest CreateRoyalAudienceQuest(Character player, string kingName, int difficulty,
        long goldReward, long xpReward, string questDescription)
    {
        // Determine quest type based on description
        QuestTarget questTarget;
        QuestObjectiveType objectiveType;
        int targetValue;
        string targetName;

        if (questDescription.Contains("monster") || questDescription.Contains("creature"))
        {
            questTarget = QuestTarget.Monster;
            objectiveType = QuestObjectiveType.KillMonsters;
            targetValue = 5 + difficulty * 3; // 8, 11, 14, 17 monsters
            targetName = GetRandomMonsterForLevel(player.Level);
        }
        else if (questDescription.Contains("artifact") || questDescription.Contains("recover"))
        {
            questTarget = QuestTarget.FindArtifact;
            objectiveType = QuestObjectiveType.FindArtifact;
            targetValue = 1;
            targetName = "Royal Artifact";
        }
        else if (questDescription.Contains("floor") || questDescription.Contains("clear"))
        {
            questTarget = QuestTarget.ClearFloor;
            objectiveType = QuestObjectiveType.ClearDungeonFloor;
            targetValue = Math.Max(1, player.Level - 5 + difficulty * 5); // Near player level
            targetName = $"Floor {targetValue}";
        }
        else if (questDescription.Contains("criminal") || questDescription.Contains("hunt"))
        {
            questTarget = QuestTarget.Assassin;
            objectiveType = QuestObjectiveType.KillBoss;
            targetValue = 1;
            targetName = "Wanted Criminal";
        }
        else // Default: dungeon investigation
        {
            questTarget = QuestTarget.ReachFloor;
            objectiveType = QuestObjectiveType.ReachDungeonFloor;
            targetValue = Math.Max(1, player.Level + difficulty * 3);
            targetName = $"Floor {targetValue}";
        }

        var quest = new Quest
        {
            Title = $"Royal Commission: {questDescription}",
            Initiator = kingName,
            QuestType = QuestType.SingleQuest,
            QuestTarget = questTarget,
            Difficulty = (byte)Math.Min(4, difficulty),
            Comment = questDescription,
            Date = DateTime.Now,
            MinLevel = Math.Max(1, player.Level - 5),
            MaxLevel = player.Level + 20,
            DaysToComplete = 7 + difficulty * 2, // 9, 11, 13, 15 days
            Reward = 3, // High reward tier
            RewardType = QuestRewardType.Money,
            // Pre-assign to this player
            Occupier = player.Name2,
            OccupierRace = player.Race,
            OccupierSex = (byte)((int)player.Sex),
            OccupiedDays = 0,
            OfferedTo = player.Name2
        };

        // Add the main objective
        quest.Objectives.Add(new QuestObjective(
            objectiveType,
            questDescription,
            targetValue,
            "",
            targetName
        ));

        // For monster quests, add monsters to track
        if (questTarget == QuestTarget.Monster)
        {
            quest.Monsters.Add(new QuestMonster(0, targetValue, targetName));
        }

        // Store the actual gold/xp rewards as custom values
        // We'll use Reward field creatively: high byte = gold tier, low byte = xp tier
        // Or we can just use Comment to store them... let's use a simpler approach
        // Actually the quest has CalculateReward which uses player level
        // Let's just set high rewards and let the system work

        questDatabase.Add(quest);

        // Also add to player's active quests if they're a Player
        if (player is Player p)
        {
            p.ActiveQuests.Add(quest);
        }

        return quest;
    }

    /// <summary>
    /// Called when the player talks to an NPC — updates TalkToNPC quest objectives.
    /// </summary>
    public static void OnNPCTalkedTo(Character player, string npcName)
    {
        if (player == null || string.IsNullOrEmpty(npcName)) return;

        var playerQuests = GetPlayerQuests(player.Name2);
        foreach (var quest in playerQuests)
        {
            if (!string.IsNullOrEmpty(quest.TargetNPCName) &&
                quest.TargetNPCName.Equals(npcName, StringComparison.OrdinalIgnoreCase))
            {
                quest.UpdateObjectiveProgress(QuestObjectiveType.TalkToNPC, 1, npcName);
                quest.OccupiedDays = Math.Max(1, quest.OccupiedDays);
            }
        }
    }

    /// <summary>
    /// Check if a quest initiator is a faction name (for faction standing boost on completion).
    /// </summary>
    public static Faction? GetFactionFromInitiator(string initiator)
    {
        if (initiator == GameConfig.FactionInitiatorCrown) return Faction.TheCrown;
        if (initiator == GameConfig.FactionInitiatorShadows) return Faction.TheShadows;
        if (initiator == GameConfig.FactionInitiatorFaith) return Faction.TheFaith;
        return null;
    }

    /// <summary>
    /// Get a random monster name appropriate for player level
    /// </summary>
    private static string GetRandomMonsterForLevel(int playerLevel)
    {
        // Use actual MonsterFamilies names that players will encounter in the dungeon
        var lowLevel = new[] { "Wolf", "Goblin", "Kobold", "Zombie", "Imp" };
        var midLevel = new[] { "Orc", "Troll", "Ogre", "Wraith", "Wyvern" };
        var highLevel = new[] { "Ancient Dragon", "Archfiend", "Lich", "Titan", "Void Entity" };

        if (playerLevel <= 15)
            return lowLevel[random.Next(lowLevel.Length)];
        else if (playerLevel <= 35)
            return midLevel[random.Next(midLevel.Length)];
        else
            return highLevel[random.Next(highLevel.Length)];
    }

    #endregion

    #region Achievement Tracking

    /// <summary>
    /// Check and unlock quest-related achievements
    /// </summary>
    private static void CheckQuestAchievements(Character player)
    {
        if (player is not Player p) return;

        var stats = StatisticsManager.Current;
        if (stats == null) return;

        // Quest Starter - first quest completed
        if (stats.QuestsCompleted >= 1)
        {
            AchievementSystem.TryUnlock(p, "quest_starter");
        }

        // Quest Master - 25 quests completed
        if (stats.QuestsCompleted >= 25)
        {
            AchievementSystem.TryUnlock(p, "quest_master");
        }

        // Bounty Hunter - 10 bounty quests completed
        if (stats.BountiesCompleted >= 10)
        {
            AchievementSystem.TryUnlock(p, "bounty_hunter");
        }
    }

    #endregion

    #region Equipment Purchase Quests

    private const string MERCHANT_GUILD_INITIATOR = "Merchant Guild";

    /// <summary>
    /// Get all equipment purchase quests
    /// </summary>
    public static List<Quest> GetEquipmentQuests()
    {
        return questDatabase.Where(q =>
            !q.Deleted &&
            q.Initiator == MERCHANT_GUILD_INITIATOR
        ).ToList();
    }

    /// <summary>
    /// Refresh equipment purchase quests on the bounty board
    /// Creates variety by adding weapon, armor, and accessory purchase quests
    /// </summary>
    public static void RefreshEquipmentQuests(int playerLevel)
    {
        // Remove old unclaimed equipment quests
        questDatabase.RemoveAll(q =>
            q.Initiator == MERCHANT_GUILD_INITIATOR &&
            string.IsNullOrEmpty(q.Occupier) &&
            q.Date < DateTime.Now.AddDays(-3));

        // Count existing equipment quests
        var existingCount = questDatabase.Count(q =>
            q.Initiator == MERCHANT_GUILD_INITIATOR &&
            !q.Deleted &&
            string.IsNullOrEmpty(q.Occupier));

        // Maintain 2-3 equipment quests
        var targetCount = 2 + random.Next(2);

        while (existingCount < targetCount)
        {
            CreateEquipmentPurchaseQuest(playerLevel);
            existingCount++;
        }
    }

    /// <summary>
    /// Create an equipment purchase quest
    /// </summary>
    private static void CreateEquipmentPurchaseQuest(int playerLevel)
    {
        // Determine quest type (weapon, armor, accessory, shield)
        var questTypeRoll = random.Next(100);
        QuestTarget questTarget;
        Equipment? targetEquipment = null;
        string questTitle;
        string questDescription;

        if (questTypeRoll < 35)
        {
            // Weapon quest (35%)
            questTarget = QuestTarget.BuyWeapon;
            var weapons = EquipmentDatabase.GetOneHandedWeapons()
                .Concat(EquipmentDatabase.GetTwoHandedWeapons())
                .Where(w => w.Value >= playerLevel * 50 && w.Value <= playerLevel * 500)
                .ToList();

            if (weapons.Count > 0)
            {
                targetEquipment = weapons[random.Next(weapons.Count)];
                questTitle = $"Acquire: {targetEquipment.Name}";
                questDescription = $"The Merchant Guild seeks a {targetEquipment.Name}. Purchase one from any shop.";
            }
            else
            {
                return; // No suitable weapons found
            }
        }
        else if (questTypeRoll < 65)
        {
            // Armor quest (30%)
            questTarget = QuestTarget.BuyArmor;
            var armor = EquipmentDatabase.GetAllArmor()
                .Where(a => a.Value >= playerLevel * 40 && a.Value <= playerLevel * 400)
                .ToList();

            if (armor.Count > 0)
            {
                targetEquipment = armor[random.Next(armor.Count)];
                questTitle = $"Acquire: {targetEquipment.Name}";
                questDescription = $"The Merchant Guild needs a {targetEquipment.Name}. Purchase one from any shop.";
            }
            else
            {
                return; // No suitable armor found
            }
        }
        else if (questTypeRoll < 85)
        {
            // Accessory quest (20%)
            questTarget = QuestTarget.BuyAccessory;
            var accessories = EquipmentDatabase.GetAccessories()
                .Where(a => a.Value >= playerLevel * 60 && a.Value <= playerLevel * 600)
                .ToList();

            if (accessories.Count > 0)
            {
                targetEquipment = accessories[random.Next(accessories.Count)];
                questTitle = $"Acquire: {targetEquipment.Name}";
                questDescription = $"A collector is seeking a {targetEquipment.Name}. Purchase one for a reward.";
            }
            else
            {
                return; // No suitable accessories found
            }
        }
        else
        {
            // Shield quest (15%)
            questTarget = QuestTarget.BuyShield;
            var shields = EquipmentDatabase.GetShields()
                .Where(s => s.Value >= playerLevel * 30 && s.Value <= playerLevel * 300)
                .ToList();

            if (shields.Count > 0)
            {
                targetEquipment = shields[random.Next(shields.Count)];
                questTitle = $"Acquire: {targetEquipment.Name}";
                questDescription = $"The city guard needs a {targetEquipment.Name}. Purchase one from a shop.";
            }
            else
            {
                return; // No suitable shields found
            }
        }

        if (targetEquipment == null) return;

        // Determine difficulty based on item value relative to player level
        var difficulty = (byte)Math.Min(4, Math.Max(1, (int)(targetEquipment.Value / (playerLevel * 100)) + 1));

        var quest = new Quest
        {
            Title = questTitle,
            Initiator = MERCHANT_GUILD_INITIATOR,
            QuestType = QuestType.SingleQuest,
            QuestTarget = questTarget,
            Difficulty = difficulty,
            Comment = questDescription,
            Date = DateTime.Now,
            MinLevel = Math.Max(1, playerLevel - 10),
            MaxLevel = playerLevel + 20,
            DaysToComplete = 7 + difficulty,
            Reward = (byte)Math.Min(3, (int)difficulty), // Reward tier matches difficulty
            RewardType = QuestRewardType.Money // Gold reward
        };

        // Add objective for equipment purchase
        quest.Objectives.Add(new QuestObjective(
            QuestObjectiveType.PurchaseEquipment,
            $"Purchase a {targetEquipment.Name}",
            1,
            targetEquipment.Id.ToString(),
            targetEquipment.Name
        ));

        questDatabase.Add(quest);
    }

    /// <summary>
    /// Called when a player purchases equipment - checks if any quests are completed
    /// </summary>
    public static void OnEquipmentPurchased(Character player, Equipment equipment)
    {
        if (player == null || equipment == null) return;

        var playerQuests = GetPlayerQuests(player.Name2);

        foreach (var quest in playerQuests)
        {
            // Check if this is an equipment purchase quest
            if (quest.QuestTarget == QuestTarget.BuyWeapon ||
                quest.QuestTarget == QuestTarget.BuyArmor ||
                quest.QuestTarget == QuestTarget.BuyAccessory ||
                quest.QuestTarget == QuestTarget.BuyShield)
            {
                // Check if any objective matches this equipment
                foreach (var objective in quest.Objectives.Where(o =>
                    o.ObjectiveType == QuestObjectiveType.PurchaseEquipment && !o.IsComplete))
                {
                    // Match by equipment ID or name
                    if (objective.TargetId == equipment.Id.ToString() ||
                        objective.TargetName.Equals(equipment.Name, StringComparison.OrdinalIgnoreCase))
                    {
                        objective.CurrentProgress = objective.RequiredProgress;
                    }
                }
            }
        }
    }

    /// <summary>
    /// Ensure equipment quests exist alongside regular quests
    /// </summary>
    public static void EnsureEquipmentQuestsExist(int playerLevel)
    {
        var existingCount = questDatabase.Count(q =>
            q.Initiator == MERCHANT_GUILD_INITIATOR &&
            !q.Deleted);

        if (existingCount == 0)
        {
            RefreshEquipmentQuests(playerLevel);
        }
    }

    #endregion
}

/// <summary>
/// Quest completion results
/// </summary>
public enum QuestCompletionResult
{
    Success = 0,
    QuestNotFound = 1,
    NotYourQuest = 2,
    QuestDeleted = 3,
    RequirementsNotMet = 4
}

/// <summary>
/// Quest ranking data for leaderboards
/// </summary>
public class QuestRanking
{
    public string PlayerName { get; set; } = "";
    public int QuestsCompleted { get; set; } = 0;
    public CharacterRace Race { get; set; }
    public byte Sex { get; set; }
} 
