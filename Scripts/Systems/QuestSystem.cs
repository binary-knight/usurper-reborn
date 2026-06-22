using UsurperRemake.UI;
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
    private static Random random = Random.Shared;

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

        // Count claims against daily limit (not completions)
        player.RoyQuestsToday++;

        // GD.Print($"[QuestSystem] Quest claimed by {player.Name2}: {foundQuest.Id}");

        // Send confirmation mail (Pascal: Quest claim notification)
        MailSystem.SendQuestClaimedMail(player.Name2, foundQuest.GetDisplayTitle());

        return QuestClaimResult.CanClaim;
    }
    
    /// <summary>
    /// Complete quest and give rewards (Pascal: Quest completion from PLYQUEST.PAS)
    /// </summary>
    public static QuestCompletionResult CompleteQuest(Character player, string questId, TerminalEmulator terminal)
    {
        // Look up quest by ID, preferring the one owned by this player (handles duplicate IDs
        // that can occur from concurrent MUD sessions or save/load race conditions)
        var quest = questDatabase.FirstOrDefault(q => q.Id == questId &&
            string.Equals(q.Occupier, player.Name2, StringComparison.OrdinalIgnoreCase));
        if (quest == null)
        {
            // Fallback to any quest with this ID
            quest = GetQuestById(questId);
        }
        if (quest == null) return QuestCompletionResult.QuestNotFound;

        if (!string.Equals(quest.Occupier, player.Name2, StringComparison.OrdinalIgnoreCase))
            return QuestCompletionResult.NotYourQuest;
        if (quest.Deleted) return QuestCompletionResult.QuestDeleted;
        
        // Check if player completed all quest requirements
        if (!ValidateQuestCompletion(player, quest))
        {
            return QuestCompletionResult.RequirementsNotMet;
        }

        // Display completion banner
        terminal.WriteLine("");
        UIHelper.WriteBoxHeader(terminal, Loc.Get("quest.completed_banner"), "bright_yellow", 40);
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
        player.ActiveQuests.Remove(quest);

        // Update global statistics tracking
        StatisticsManager.Current.RecordQuestComplete();

        // Fame from quest completion
        player.Fame += 5;

        // Track bounty completion separately for achievements
        if (quest.Initiator == KING_BOUNTY_INITIATOR || quest.QuestTarget == QuestTarget.DefeatNPC || quest.QuestTarget == QuestTarget.Assassin)
        {
            StatisticsManager.Current.RecordBountyComplete();
            player.Fame += 3; // Extra fame for royal bounties
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
                terminal.WriteLine(Loc.Get("quest.standing_improved", quest.GetDisplayInitiator()), "bright_cyan");
            }
        }

        // Check quest achievements
        CheckQuestAchievements(player);

        // Send completion notification to king (Pascal: King notification)
        SendQuestCompletionMail(player, quest, rewardAmount);

        // News announcement (Pascal: News generation)
        GenerateQuestCompletionNews(player, quest);

        // GD.Print($"[QuestSystem] Quest completed by {player.Name2}: {quest.Id}");

        return QuestCompletionResult.Success;
    }
    
    /// <summary>
    /// Get available quests for player (Pascal: Quest listing)
    /// </summary>
    public static List<Quest> GetAvailableQuests(Character player)
    {
        // At daily limit — no point showing quests that can't be claimed
        if (player.RoyQuestsToday >= GameConfig.MaxQuestsPerDay)
            return new List<Quest>();

        return questDatabase.Where(q =>
            !q.Deleted &&
            string.IsNullOrEmpty(q.Occupier) &&
            !player.King &&
            q.Initiator != player.Name2 &&
            player.Level >= q.MinLevel &&
            player.Level <= q.MaxLevel
        )
        .GroupBy(q => q.Title)       // Deduplicate by quest title
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
            string.Equals(q.Occupier, playerName, StringComparison.OrdinalIgnoreCase)
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
        MailSystem.SendQuestOfferMail(playerName, quest.GetDisplayTitle());
        
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
    private static void GenerateDungeonQuestObjectives(Quest quest, int playerLevel = 10, int deepestFloor = 0)
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
                // Use base tier name as targetId so it matches OnMonsterKilled tierId
                var bossName = Loc.Get("quest.title.champion", tier.Name);
                var bossId = tier.Name.ToLower().Replace(" ", "_");
                quest.Objectives.Add(QuestObjective.Localized(
                    QuestObjectiveType.KillBoss,
                    "quest.objective.defeat_boss",
                    new object[] { bossName },
                    1, bossId, bossName));
                quest.Title = Loc.Get("quest.title.defeat_boss", bossName);
                quest.TitleKey = "quest.title.defeat_boss";
                quest.TitleArgs = new List<string> { bossName };
                break;

            case QuestTarget.ReachFloor:
                // Reach a specific floor - must be beyond player's deepest cleared floor
                int expeditionFloor = CapFloor(playerLevel + quest.Difficulty * 2 + random.Next(1, 4));
                // Ensure target is at least 1 floor beyond what they've already reached
                if (deepestFloor > 0 && expeditionFloor <= deepestFloor)
                    expeditionFloor = Math.Min(maxAccessibleFloor, deepestFloor + random.Next(1, 4));
                // If still can't go deeper (at max accessible), fall through to a different quest type
                if (expeditionFloor <= deepestFloor && deepestFloor >= maxAccessibleFloor)
                    expeditionFloor = maxAccessibleFloor; // At cap, just use max — kill req still adds challenge
                quest.Objectives.Add(QuestObjective.Localized(
                    QuestObjectiveType.ReachDungeonFloor,
                    "quest.objective.reach_floor",
                    new object[] { expeditionFloor },
                    expeditionFloor, "", $"Floor {expeditionFloor}"));
                // Required: kill monsters on the target floor to prove you fought there
                int expeditionKillCount = 3 + quest.Difficulty * 2;
                quest.Objectives.Add(QuestObjective.Localized(
                    QuestObjectiveType.KillMonsters,
                    "quest.objective.defeat_monsters_expedition",
                    new object[] { expeditionKillCount },
                    expeditionKillCount, "", "Monsters"));
                quest.Title = Loc.Get("quest.title.expedition", expeditionFloor);
                quest.TitleKey = "quest.title.expedition";
                quest.TitleArgs = new List<string> { expeditionFloor.ToString() };
                break;

            case QuestTarget.ClearFloor:
                // Clear all monsters on a specific floor - capped to accessible range
                var clearFloor = CapFloor(playerLevel + quest.Difficulty + random.Next(-1, 3));
                quest.Objectives.Add(QuestObjective.Localized(
                    QuestObjectiveType.ReachDungeonFloor,
                    "quest.objective.descend_floor",
                    new object[] { clearFloor },
                    clearFloor, "", $"Floor {clearFloor}"));
                quest.Objectives.Add(QuestObjective.Localized(
                    QuestObjectiveType.ClearDungeonFloor,
                    "quest.objective.clear_monsters",
                    new object[] { clearFloor },
                    1, clearFloor.ToString(), $"Floor {clearFloor}"));
                quest.Title = Loc.Get("quest.title.clear_floor", clearFloor);
                quest.TitleKey = "quest.title.clear_floor";
                quest.TitleArgs = new List<string> { clearFloor.ToString() };
                break;

            case QuestTarget.SurviveDungeon:
                // Survive multiple floors - based on difficulty but within reason
                var surviveFloors = Math.Min(quest.Difficulty * 3 + random.Next(2, 5), 15);
                quest.Objectives.Add(QuestObjective.Localized(
                    QuestObjectiveType.ReachDungeonFloor,
                    "quest.objective.survive_floors",
                    new object[] { surviveFloors },
                    surviveFloors, "", "Floors"));
                quest.Objectives.Add(QuestObjective.Localized(
                    QuestObjectiveType.KillMonsters,
                    "quest.objective.defeat_minimum",
                    Array.Empty<object>(),
                    10, "", "Monsters"));
                quest.Title = Loc.Get("quest.title.survive_floors", surviveFloors);
                quest.TitleKey = "quest.title.survive_floors";
                quest.TitleArgs = new List<string> { surviveFloors.ToString() };
                break;
        }
    }

    /// <summary>
    /// Create a dungeon quest (bounty board style)
    /// </summary>
    public static Quest CreateDungeonQuest(QuestTarget target, byte difficulty, string dungeonName = "The Dungeon", int playerLevel = 10, int deepestFloor = 0)
    {
        if (target < QuestTarget.ClearBoss || target > QuestTarget.SurviveDungeon)
        {
            throw new ArgumentException("Invalid dungeon quest target type");
        }

        var quest = new Quest
        {
            Initiator = "Bounty Board",  // English only — shared quest data
            QuestType = QuestType.SingleQuest,
            QuestTarget = target,
            Difficulty = difficulty,
            Comment = Loc.Get("quest.dungeon_quest_comment", dungeonName),
            Date = DateTime.Now,
            MinLevel = Math.Max(1, playerLevel - 5),
            MaxLevel = playerLevel + 15,
            DaysToComplete = GameConfig.DefaultQuestDays + difficulty
        };

        // Generate objectives based on quest type with player level consideration
        GenerateDungeonQuestObjectives(quest, playerLevel, deepestFloor);

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
    public static void RefreshBountyBoard(int playerLevel, int deepestFloor = 0)
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
            var questTypes = new[] { QuestTarget.ClearBoss, QuestTarget.ReachFloor, QuestTarget.ClearFloor, QuestTarget.SurviveDungeon };
            var questType = questTypes[random.Next(questTypes.Length)];

            CreateDungeonQuest(questType, difficulty, Loc.Get("quest.dungeon_name"), playerLevel, deepestFloor);
            existingCount++;
        }

        // GD.Print($"[QuestSystem] Bounty board refreshed with {existingCount} quests");
    }

    // ────────────────────────────────────────────────────────────────────────
    // v0.62.x "Light and Dark" Phase 4 (Mercenary/Sellsword job board).
    // Faction-issued freelance contracts visible at Anchor Road's Sellsword Hall menu.
    // Composes with the existing Quest/QuestObjective pipeline -- contracts ARE Quests with
    // `IsMercContract = true` and an `IssuingFaction` discriminator. RefreshMercBoard mirrors
    // RefreshBountyBoard's "drop unclaimed > N days old, refill to slot count" rhythm.
    // ────────────────────────────────────────────────────────────────────────

    // Issuer sentinel strings for `Quest.Initiator` (English fallback; localized via InitiatorKey).
    public const string MERC_INITIATOR_CROWN = "Crown Steward";
    public const string MERC_INITIATOR_SHADOWS = "Shadow Broker";
    public const string MERC_INITIATOR_FAITH = "Faith Almoner";

    private struct MercTemplate
    {
        public string Id;                    // e.g. "crown_bandit_purge"; drives loc key namespaces
        public UsurperRemake.Systems.Faction Faction;
        public QuestObjectiveType ObjectiveType;
        public string TargetId;              // monster type, location name, or empty for generic
        public int BaseProgress;             // baseline RequiredProgress, scaled by playerLevel at create time
        public float PayoutMultiplier;       // contract-kind multiplier on base gold (0.85 = easy job pays less, 1.25 = harder pays more)
        public int Tier;                     // 1-5; slice 1 ships only tier 1
    }

    // Slice 1: 2 contracts per faction at tier 1.
    // v0.65.0 (1.0-prep SR): tier 2 ships -- 2 more per faction, unlocked at
    // Sellsword rank (10 completions; the existing `Tier <= max(1, rankIdx)`
    // visibility gate makes this work with zero new gating code). Payout
    // multipliers 1.40-1.60 vs tier 1's 0.85-1.00, so climbing the ladder
    // visibly pays. Kill targets chosen from mid-band monster names that
    // actually spawn (Orc 5-20, Ghoul 16-30); the harder jobs lean on
    // ExploreRooms / KillMonsters which have no spawn-band dependency.
    // Tiers 3-5 + Legend's Pick stay deferred.
    private static readonly MercTemplate[] MercTemplatesSlice1 = new[]
    {
        new MercTemplate { Id = "crown_bandit_purge",    Faction = UsurperRemake.Systems.Faction.TheCrown,   ObjectiveType = QuestObjectiveType.KillSpecificMonster, TargetId = "Bandit",     BaseProgress = 3, PayoutMultiplier = 1.00f, Tier = 1 },
        new MercTemplate { Id = "crown_guard_relief",    Faction = UsurperRemake.Systems.Faction.TheCrown,   ObjectiveType = QuestObjectiveType.ExploreRooms,        TargetId = "",           BaseProgress = 10, PayoutMultiplier = 0.85f, Tier = 1 },
        new MercTemplate { Id = "shadows_fence_run",     Faction = UsurperRemake.Systems.Faction.TheShadows, ObjectiveType = QuestObjectiveType.VisitLocation,       TargetId = "DarkAlley",  BaseProgress = 1, PayoutMultiplier = 0.90f, Tier = 1 },
        new MercTemplate { Id = "shadows_jailbreak",     Faction = UsurperRemake.Systems.Faction.TheShadows, ObjectiveType = QuestObjectiveType.VisitLocation,       TargetId = "Prison",     BaseProgress = 1, PayoutMultiplier = 0.90f, Tier = 1 },
        new MercTemplate { Id = "faith_purge_undead",    Faction = UsurperRemake.Systems.Faction.TheFaith,   ObjectiveType = QuestObjectiveType.KillSpecificMonster, TargetId = "Zombie",     BaseProgress = 3, PayoutMultiplier = 1.00f, Tier = 1 },
        new MercTemplate { Id = "faith_escort_pilgrim",  Faction = UsurperRemake.Systems.Faction.TheFaith,   ObjectiveType = QuestObjectiveType.KillMonsters,        TargetId = "",           BaseProgress = 5, PayoutMultiplier = 0.95f, Tier = 1 },

        // Tier 2 (Sellsword rank, 10+ completions)
        new MercTemplate { Id = "crown_warband_breaker", Faction = UsurperRemake.Systems.Faction.TheCrown,   ObjectiveType = QuestObjectiveType.KillSpecificMonster, TargetId = "Orc",        BaseProgress = 5, PayoutMultiplier = 1.60f, Tier = 2 },
        new MercTemplate { Id = "crown_deep_survey",     Faction = UsurperRemake.Systems.Faction.TheCrown,   ObjectiveType = QuestObjectiveType.ExploreRooms,        TargetId = "",           BaseProgress = 25, PayoutMultiplier = 1.40f, Tier = 2 },
        new MercTemplate { Id = "shadows_message_job",   Faction = UsurperRemake.Systems.Faction.TheShadows, ObjectiveType = QuestObjectiveType.KillMonsters,        TargetId = "",           BaseProgress = 12, PayoutMultiplier = 1.55f, Tier = 2 },
        new MercTemplate { Id = "shadows_route_scout",   Faction = UsurperRemake.Systems.Faction.TheShadows, ObjectiveType = QuestObjectiveType.ExploreRooms,        TargetId = "",           BaseProgress = 20, PayoutMultiplier = 1.50f, Tier = 2 },
        new MercTemplate { Id = "faith_ghoul_cleansing", Faction = UsurperRemake.Systems.Faction.TheFaith,   ObjectiveType = QuestObjectiveType.KillSpecificMonster, TargetId = "Ghoul",      BaseProgress = 4, PayoutMultiplier = 1.60f, Tier = 2 },
        new MercTemplate { Id = "faith_lantern_vigil",   Faction = UsurperRemake.Systems.Faction.TheFaith,   ObjectiveType = QuestObjectiveType.ExploreRooms,        TargetId = "",           BaseProgress = 20, PayoutMultiplier = 1.45f, Tier = 2 },
    };

    /// <summary>
    /// Refresh the Sellsword Hall board: drop unclaimed stale contracts, refill per-faction
    /// slot count based on the player's merc rank. Called on entry to the Hall and on daily reset.
    /// </summary>
    public static void RefreshMercBoard(Character player)
    {
        if (player == null) return;
        int playerLevel = Math.Max(1, player.Level);

        // Slot count per faction column scales with rank (Recruit=1, Veteran=2, Legend=3). Capped at template-pool size.
        int rankIdx = (int)UsurperRemake.Systems.AlignmentSystem.Instance.GetMercRank(player);
        int slotsPerFaction = rankIdx >= 0 && rankIdx < GameConfig.MercRankSlotsPerFaction.Length
            ? GameConfig.MercRankSlotsPerFaction[rankIdx]
            : 1;

        // Drop unclaimed stale contracts (mirrors RefreshBountyBoard:511).
        var staleCutoff = DateTime.Now.AddDays(-GameConfig.MercContractStaleDays);
        questDatabase.RemoveAll(q => q.IsMercContract
            && !q.Deleted
            && string.IsNullOrEmpty(q.Occupier)
            && q.Date < staleCutoff);

        // Per-faction refill.
        foreach (var faction in new[] { UsurperRemake.Systems.Faction.TheCrown,
                                        UsurperRemake.Systems.Faction.TheShadows,
                                        UsurperRemake.Systems.Faction.TheFaith })
        {
            int existing = questDatabase.Count(q => q.IsMercContract
                && !q.Deleted
                && string.IsNullOrEmpty(q.Occupier)
                && q.IssuingFaction.HasValue
                && q.IssuingFaction.Value == faction);
            int needed = slotsPerFaction - existing;
            if (needed <= 0) continue;

            // Tier-gate visibility by rank: Recruit (rankIdx 1) sees tier-1 only; future tiers gate
            // by Tier <= max(1, rankIdx). Slice 1 templates are all Tier=1 so this is a no-op now.
            int visibleTier = Math.Max(1, rankIdx);
            var pool = MercTemplatesSlice1.Where(t => t.Faction == faction && t.Tier <= visibleTier).ToList();
            if (pool.Count == 0) continue;

            for (int i = 0; i < needed; i++)
            {
                var tpl = pool[random.Next(pool.Count)];
                CreateMercContract(tpl, playerLevel);
            }
        }

        player.LastMercBoardRefreshUtc = DateTime.UtcNow;
    }

    private static Quest CreateMercContract(MercTemplate tpl, int playerLevel)
    {
        // Required progress scales with player level so the contract isn't trivially easy at endgame.
        int required = tpl.BaseProgress + (playerLevel / 5);

        // Gold scales on PLAYER LEVEL only (NOT player wealth -- can't compound into a printer).
        long goldReward = (long)(playerLevel * GameConfig.MercContractBaseGoldTier1 * tpl.PayoutMultiplier);

        string issuerName = tpl.Faction switch
        {
            UsurperRemake.Systems.Faction.TheCrown => MERC_INITIATOR_CROWN,
            UsurperRemake.Systems.Faction.TheShadows => MERC_INITIATOR_SHADOWS,
            UsurperRemake.Systems.Faction.TheFaith => MERC_INITIATOR_FAITH,
            _ => "Sellsword Hall"
        };
        string initiatorKey = tpl.Faction switch
        {
            UsurperRemake.Systems.Faction.TheCrown => "merc.issuer.crown",
            UsurperRemake.Systems.Faction.TheShadows => "merc.issuer.shadows",
            UsurperRemake.Systems.Faction.TheFaith => "merc.issuer.faith",
            _ => ""
        };

        var quest = new Quest
        {
            Id = $"merc_{tpl.Id}_{Guid.NewGuid().ToString("N").Substring(0, 8)}",
            Initiator = issuerName,
            InitiatorKey = initiatorKey,
            Title = $"Sellsword Contract: {tpl.Id}",  // English fallback; localized via TitleKey
            TitleKey = $"merc.contract.{tpl.Id}.title",
            TitleArgs = new List<string> { required.ToString() },
            Comment = $"Sellsword Hall posting: {tpl.Id}",
            CommentKey = $"merc.contract.{tpl.Id}.comment",
            CommentArgs = new List<string> { required.ToString() },
            Date = DateTime.Now,
            QuestType = QuestType.SingleQuest,
            QuestTarget = QuestTarget.Monster, // closest legacy target; objectives are the real tracking
            Difficulty = (byte)Math.Max(1, tpl.Tier),
            DaysToComplete = 7,
            MinLevel = Math.Max(1, playerLevel - 5),
            MaxLevel = playerLevel + 15,
            Reward = GameConfig.QuestRewardMedium,
            BountyGold = goldReward,                   // honest gold payout via BountyGold (bypasses byte Reward cap)
            RewardType = QuestRewardType.Money,
            Penalty = 2,
            PenaltyType = QuestRewardType.Money,
            IsMercContract = true,
            IssuingFaction = tpl.Faction,
            MercContractTier = tpl.Tier
        };

        var obj = new QuestObjective
        {
            Id = $"merc_obj_{tpl.Id}",
            ObjectiveType = tpl.ObjectiveType,
            TargetId = tpl.TargetId,
            TargetName = tpl.TargetId,
            RequiredProgress = required,
            DescriptionKey = $"merc.contract.{tpl.Id}.objective",
            DescriptionArgs = new List<string> { required.ToString(), tpl.TargetId ?? "" }
        };
        quest.Objectives.Add(obj);

        questDatabase.Add(quest);
        return quest;
    }

    /// <summary>
    /// Filter the questDatabase for merc contracts visible to this player. Pass faction = null
    /// to get all 3 columns; pass a specific faction for a single column.
    /// </summary>
    public static List<Quest> GetAvailableMercContracts(Character player, UsurperRemake.Systems.Faction? faction = null)
    {
        if (player == null) return new List<Quest>();
        return questDatabase.Where(q =>
            q.IsMercContract
            && !q.Deleted
            && string.IsNullOrEmpty(q.Occupier)
            && (faction == null || (q.IssuingFaction.HasValue && q.IssuingFaction.Value == faction.Value))
            && player.Level >= q.MinLevel
            && player.Level <= q.MaxLevel
        ).ToList();
    }

    /// <summary>
    /// Get the merc contracts the player has currently CLAIMED (Occupier = player.Name2). Used by
    /// the Hall to show what's in-progress and offer turn-in.
    /// </summary>
    public static List<Quest> GetClaimedMercContracts(Character player)
    {
        if (player == null) return new List<Quest>();
        return questDatabase.Where(q =>
            q.IsMercContract
            && !q.Deleted
            && q.Occupier == player.Name2
        ).ToList();
    }

    /// <summary>
    /// Turn in a completed merc contract: pay gold + Reputation cascade + alignment shift (Faith/Shadows
    /// only; Crown is alignment-neutral by default). Caps faction standing gain via DailyMercStandingGain
    /// to prevent "merc 30 Crown contracts in a row, jump into Crown membership instantly" exploits.
    /// </summary>
    public static (bool ok, long goldAwarded, int standingAwarded, string reason) CompleteMercContract(Character player, Quest quest)
    {
        if (player == null || quest == null) return (false, 0, 0, "null");
        if (!quest.IsMercContract || !quest.IssuingFaction.HasValue) return (false, 0, 0, "not_merc");
        if (quest.Occupier != player.Name2) return (false, 0, 0, "not_yours");
        if (!ValidateQuestCompletion(player, quest)) return (false, 0, 0, "incomplete");

        // Rank-based pay multiplier (Recruit 1.00x -> Legend 1.15x).
        int rankIdx = (int)UsurperRemake.Systems.AlignmentSystem.Instance.GetMercRank(player);
        float rankMul = rankIdx >= 0 && rankIdx < GameConfig.MercRankPayMultiplier.Length
            ? GameConfig.MercRankPayMultiplier[rankIdx]
            : 1.0f;
        long gold = (long)(quest.BountyGold * rankMul);
        player.Gold += gold;

        // Faction standing award, capped per-faction-per-day. Cascade fires inside ModifyReputation.
        var faction = quest.IssuingFaction.Value;
        int factionKey = (int)faction;
        int gainedToday = player.DailyMercStandingGain != null && player.DailyMercStandingGain.TryGetValue(factionKey, out int g) ? g : 0;
        int baseAward = 10 + quest.MercContractTier * 5; // tier-1 = 15; tier-5 = 35
        int allowedAward = Math.Max(0, Math.Min(baseAward, GameConfig.MaxDailyMercStandingGain - gainedToday));
        if (allowedAward > 0)
        {
            UsurperRemake.Systems.FactionSystem.Instance.ModifyReputation(faction, allowedAward);
            if (player.DailyMercStandingGain == null) player.DailyMercStandingGain = new Dictionary<int, int>();
            player.DailyMercStandingGain[factionKey] = gainedToday + allowedAward;
        }

        // Per-faction alignment shift (Faith = chivalry, Shadows = darkness, Crown = neutral by default).
        // Modest amounts so a long career drifts alignment naturally without forcing it on first contract.
        if (faction == UsurperRemake.Systems.Faction.TheFaith)
            UsurperRemake.Systems.AlignmentSystem.Instance.ChangeAlignment(player, 3, isGood: true, "Faith merc contract");
        else if (faction == UsurperRemake.Systems.Faction.TheShadows)
            UsurperRemake.Systems.AlignmentSystem.Instance.ChangeAlignment(player, 3, isGood: false, "Shadows merc contract");

        // Bookkeeping: career counter + mark the quest complete (matches existing Bounty Board pattern).
        player.MercContractsCompleted++;
        quest.Deleted = true;

        return (true, gold, allowedAward, "ok");
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
        quest.Penalty = (byte)Math.Max(1, quest.Reward - 1);
        quest.PenaltyType = QuestRewardType.Money; // Always gold penalty
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

        // Legacy validation for quests without objectives — these quests
        // should always have objectives, so if we reach here, they're incomplete
        switch (quest.QuestTarget)
        {
            case QuestTarget.Monster:
                // No objectives means no tracking — quest is incomplete
                return false;

            case QuestTarget.Assassin:
                // Must use objective-based tracking, not lifetime stats
                return false;

            case QuestTarget.Seduce:
                // Must use objective-based tracking, not lifetime stats
                return false;

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
                terminal.WriteLine(Loc.Get("quest.reward_xp", rewardAmount), "bright_green");
                break;

            case QuestRewardType.Money:
                player.Gold += rewardAmount;
                player.Statistics?.RecordQuestGoldReward(rewardAmount);
                DebugLogger.Instance.LogInfo("GOLD", $"QUEST REWARD: {player.DisplayName} +{rewardAmount:N0}g from quest '{quest.Title}' (gold now {player.Gold:N0})");
                terminal.WriteLine(Loc.Get("quest.reward_gold", rewardAmount), "bright_yellow");
                break;

            case QuestRewardType.Potions:
                int potionRoom = (int)Math.Max(0, GameConfig.MaxHealingPotions - player.Healing);
                int potionsAwarded = (int)Math.Min(rewardAmount, potionRoom);
                player.Healing += potionsAwarded;
                if (potionsAwarded < rewardAmount)
                    terminal.WriteLine(Loc.Get("quest.reward_potions_capped", potionsAwarded, GameConfig.MaxHealingPotions), "bright_cyan");
                else
                    terminal.WriteLine(Loc.Get("quest.reward_potions", potionsAwarded), "bright_cyan");
                break;

            case QuestRewardType.Darkness:
                // v0.57.12: paired movement — evil-quest darkness reward also lowers chivalry by half
                AlignmentSystem.Instance.ChangeAlignment(player, (int)rewardAmount, isGood: false, "quest.reward");
                terminal.WriteLine(Loc.Get("quest.reward_darkness", rewardAmount), "red");
                break;

            case QuestRewardType.Chivalry:
                // v0.57.12: paired movement — good-quest chivalry reward also lowers darkness by half
                AlignmentSystem.Instance.ChangeAlignment(player, (int)rewardAmount, isGood: true, "quest.reward");
                terminal.WriteLine(Loc.Get("quest.reward_chivalry", rewardAmount), "bright_white");
                break;

            default:
                long fallbackGold = player.Level * 100;
                player.Gold += fallbackGold;
                player.Statistics?.RecordQuestGoldReward(fallbackGold);
                terminal.WriteLine(Loc.Get("quest.reward_gold", fallbackGold), "bright_yellow");
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

        // Check inventory FIRST — prefer removing unequipped copies so player keeps worn gear
        var inventoryItem = player.Inventory?.FirstOrDefault(i =>
            i.Name.Equals(targetName, StringComparison.OrdinalIgnoreCase));
        if (inventoryItem != null)
        {
            player.Inventory.Remove(inventoryItem);
            terminal.WriteLine(Loc.Get("quest.hand_over_item", targetName), "gray");
            return;
        }

        // Only take equipped item if no inventory copy exists
        foreach (var slot in Enum.GetValues<EquipmentSlot>())
        {
            var equipped = player.GetEquipment(slot);
            if (equipped != null && equipped.Name.Equals(targetName, StringComparison.OrdinalIgnoreCase))
            {
                player.UnequipSlot(slot);
                terminal.WriteLine(Loc.Get("quest.hand_over_item", targetName), "gray");
                return;
            }
        }
    }

    /// <summary>
    /// Process quest failure (Pascal: Quest failure handling)
    /// </summary>
    private static void ProcessQuestFailure(Quest quest)
    {
        // Send failure mail to player
        MailSystem.SendQuestFailureMail(quest.Occupier, quest.GetDisplayTitle());

        // Send failure notification to king (in king's language at the time, best-effort)
        var kingName = quest.Initiator;
        MailSystem.SendQuestFailureNotificationMail(kingName, quest.GetDisplayTitle());
        
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
        // Mark ALL matching quests in database (defense against duplicates)
        foreach (var quest in questDatabase.Where(q => q.Id == questId).ToList())
        {
            quest.Deleted = true;
            quest.IsAbandoned = true;
            // Keep Occupier set so the quest is properly serialized with Abandoned status
            // and doesn't reappear as an unclaimed board quest
        }
        player.ActiveQuests.RemoveAll(q => q.Id == questId);
    }

    /// <summary>
    /// Apply quest failure penalty — fame loss and news announcement.
    /// The quest Penalty/PenaltyType fields are set by SetDefaultRewards.
    /// </summary>
    private static void ApplyQuestPenalty(Quest quest)
    {
        if (quest.Penalty == 0) return;

        string playerName = quest.Occupier;
        if (string.IsNullOrEmpty(playerName)) return;

        // Log the penalty
        DebugLogger.Instance.LogInfo("QUEST", $"QUEST FAILED: {playerName} failed quest '{quest.Title}' (difficulty {quest.Difficulty})");

        // Announce the failure (news rendered to bootstrap language at write time; out of scope for v0.61.3)
        NewsSystem.Instance?.Newsy(true, Loc.Get("quest.failure_news", playerName, quest.GetDisplayTitle()));
    }
    
    /// <summary>
    /// Send quest completion mail to king
    /// </summary>
    private static void SendQuestCompletionMail(Character player, Quest quest, long rewardAmount)
    {
        MailSystem.SendQuestCompletionMail(player.Name2, quest.GetDisplayTitle(), rewardAmount);
    }
    
    /// <summary>
    /// Generate news for quest completion
    /// </summary>
    private static void GenerateQuestCompletionNews(Character player, Quest quest)
    {
        NewsSystem.Instance?.Newsy(true, Loc.Get("quest.completion_news", player.Name2, quest.GetDifficultyString()));
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
                TitleKey = questData.TitleKey ?? "",
                TitleArgs = questData.TitleArgs != null ? new List<string>(questData.TitleArgs) : new List<string>(),
                CommentKey = questData.CommentKey ?? "",
                CommentArgs = questData.CommentArgs != null ? new List<string>(questData.CommentArgs) : new List<string>(),
                InitiatorKey = questData.InitiatorKey ?? "",
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
                Deleted = questData.Status == QuestStatus.Completed || questData.Status == QuestStatus.Failed || questData.Status == QuestStatus.Abandoned,
                IsAbandoned = questData.Status == QuestStatus.Abandoned,
                // v0.62.x Phase 4 (Mercenary board): restore faction-issued freelance contract fields.
                // -1 sentinel in QuestData.IssuingFaction maps back to null Faction?.
                IsMercContract = questData.IsMercContract,
                IssuingFaction = questData.IssuingFaction >= 0
                    ? (UsurperRemake.Systems.Faction?)questData.IssuingFaction
                    : null,
                MercContractTier = questData.MercContractTier > 0 ? questData.MercContractTier : 1
            };

            // Restore objectives
            foreach (var objData in questData.Objectives)
            {
                quest.Objectives.Add(new QuestObjective
                {
                    Id = objData.Id,
                    Description = objData.Description,
                    DescriptionKey = objData.DescriptionKey ?? "",
                    DescriptionArgs = objData.DescriptionArgs != null
                        ? new List<string>(objData.DescriptionArgs)
                        : new List<string>(),
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
                TitleKey = questData.TitleKey ?? "",
                TitleArgs = questData.TitleArgs != null ? new List<string>(questData.TitleArgs) : new List<string>(),
                CommentKey = questData.CommentKey ?? "",
                CommentArgs = questData.CommentArgs != null ? new List<string>(questData.CommentArgs) : new List<string>(),
                InitiatorKey = questData.InitiatorKey ?? "",
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
                Deleted = questData.Status == QuestStatus.Completed || questData.Status == QuestStatus.Failed || questData.Status == QuestStatus.Abandoned,
                IsAbandoned = questData.Status == QuestStatus.Abandoned,
                // v0.62.x Phase 4 (Mercenary board): restore faction-issued freelance contract fields.
                // -1 sentinel in QuestData.IssuingFaction maps back to null Faction?.
                IsMercContract = questData.IsMercContract,
                IssuingFaction = questData.IssuingFaction >= 0
                    ? (UsurperRemake.Systems.Faction?)questData.IssuingFaction
                    : null,
                MercContractTier = questData.MercContractTier > 0 ? questData.MercContractTier : 1
            };

            foreach (var objData in questData.Objectives)
            {
                quest.Objectives.Add(new QuestObjective
                {
                    Id = objData.Id,
                    Description = objData.Description,
                    DescriptionKey = objData.DescriptionKey ?? "",
                    DescriptionArgs = objData.DescriptionArgs != null
                        ? new List<string>(objData.DescriptionArgs)
                        : new List<string>(),
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
    /// <summary>
    /// v0.65.0: remove every quest claimed by / offered to a player from the
    /// shared questDatabase. Used at permadeath so a permadied character's
    /// claimed quests don't linger in world_state["quests"] and re-attach by
    /// Occupier name to a same-name recreation (the reported eldruin/Eldruin
    /// quest carry-over). Match is case-insensitive on both Occupier and
    /// OfferedTo, against any of the names the player may have been stored under.
    /// </summary>
    public static int RemovePlayerQuests(params string?[] playerNames)
    {
        var names = (playerNames ?? Array.Empty<string?>())
            .Where(n => !string.IsNullOrWhiteSpace(n))
            .ToList();
        if (names.Count == 0) return 0;
        bool Matches(string? field) =>
            !string.IsNullOrEmpty(field) &&
            names.Any(n => string.Equals(field, n, StringComparison.OrdinalIgnoreCase));
        return questDatabase.RemoveAll(q => Matches(q.Occupier) || Matches(q.OfferedTo));
    }

    /// <summary>
    /// v0.65.0: drop any quest claimed by / offered to playerName whose Id is NOT
    /// in keepIds. Run AFTER MergeWorldQuests during load to close the
    /// re-injection window: MergePlayerQuests purges the dead character's quests
    /// from questDatabase, but MergeWorldQuests then re-adds claimed quests from a
    /// possibly-stale world_state mirror (a permadied character's claimed quests
    /// linger there until the permadeath push lands / for pre-fix permadeaths).
    /// This reasserts that the loading player's claimed set equals exactly their
    /// own save. Returns the number of stale quests removed.
    /// </summary>
    public static int ReconcilePlayerQuests(string playerName, HashSet<string> keepIds)
    {
        if (string.IsNullOrWhiteSpace(playerName)) return 0;
        keepIds ??= new HashSet<string>();
        bool MatchesName(string? field) =>
            !string.IsNullOrEmpty(field) &&
            string.Equals(field, playerName, StringComparison.OrdinalIgnoreCase);
        return questDatabase.RemoveAll(q =>
            (MatchesName(q.Occupier) || MatchesName(q.OfferedTo)) &&
            !keepIds.Contains(q.Id));
    }

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
                TitleKey = questData.TitleKey ?? "",
                TitleArgs = questData.TitleArgs != null ? new List<string>(questData.TitleArgs) : new List<string>(),
                CommentKey = questData.CommentKey ?? "",
                CommentArgs = questData.CommentArgs != null ? new List<string>(questData.CommentArgs) : new List<string>(),
                InitiatorKey = questData.InitiatorKey ?? "",
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
                Deleted = questData.Status == QuestStatus.Completed || questData.Status == QuestStatus.Failed || questData.Status == QuestStatus.Abandoned,
                IsAbandoned = questData.Status == QuestStatus.Abandoned,
                // v0.62.x Phase 4 (Mercenary board): restore faction-issued freelance contract fields.
                // -1 sentinel in QuestData.IssuingFaction maps back to null Faction?.
                IsMercContract = questData.IsMercContract,
                IssuingFaction = questData.IssuingFaction >= 0
                    ? (UsurperRemake.Systems.Faction?)questData.IssuingFaction
                    : null,
                MercContractTier = questData.MercContractTier > 0 ? questData.MercContractTier : 1
            };

            foreach (var objData in questData.Objectives)
            {
                quest.Objectives.Add(new QuestObjective
                {
                    Id = objData.Id,
                    Description = objData.Description,
                    DescriptionKey = objData.DescriptionKey ?? "",
                    DescriptionArgs = objData.DescriptionArgs != null
                        ? new List<string>(objData.DescriptionArgs)
                        : new List<string>(),
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
    /// <summary>
    /// v0.62.x Phase 4 (Mercenary board): increment ExploreRooms objectives by 1 when the player
    /// enters a NEW dungeon room. Called once per first-time room entry from `DungeonLocation.MoveToRoom`
    /// (the `targetRoom.IsExplored = true` branch). Auto-completes shadows_fence_run / faith etc. once
    /// the running total of new rooms hits the contract's required count -- so the contract's `RequiredProgress`
    /// is a NEW-rooms-count, not a current-floor-rooms-count. Cheap: bounded by playerQuests count.
    /// </summary>
    public static void OnRoomExplored(Character player)
    {
        if (player == null) return;
        var playerQuests = GetPlayerQuests(player.Name2);
        foreach (var quest in playerQuests)
        {
            quest.UpdateObjectiveProgress(QuestObjectiveType.ExploreRooms, 1);
        }
    }

    /// <summary>
    /// v0.62.x Phase 4 (Mercenary board): increment VisitLocation objectives that match this location
    /// when the player enters it. Called once per location entry from `BaseLocation.EnterLocation`.
    /// Matches case-insensitively against the location's enum name (e.g. "DarkAlley", "Prison") that
    /// merc contracts set as `objective.TargetId`. Auto-completes shadows_fence_run / shadows_jailbreak
    /// on first visit after claiming. Cheap: bounded by playerQuests count.
    /// </summary>
    public static void OnLocationVisited(Character player, GameLocation location)
    {
        if (player == null) return;
        string locId = location.ToString();
        var playerQuests = GetPlayerQuests(player.Name2);
        foreach (var quest in playerQuests)
        {
            // UpdateObjectiveProgress matches TargetId case-insensitively; merc contracts use the
            // bare enum string (e.g. "DarkAlley"). Use a targetId-specific increment so a contract
            // requiring "Prison" doesn't tick when the player visits "DarkAlley".
            quest.UpdateObjectiveProgress(QuestObjectiveType.VisitLocation, 1, locId);
        }
    }

    public static void OnMonsterKilled(Character player, string monsterName, bool isBoss = false, string tierName = "")
    {
        var playerQuests = GetPlayerQuests(player.Name2);
        string nameId = monsterName.ToLower().Replace(" ", "_");
        // TierName is the base monster type (e.g. "Zombie") even for champion variants ("Zombie Champion")
        string tierId = !string.IsNullOrEmpty(tierName) ? tierName.ToLower().Replace(" ", "_") : "";

        foreach (var quest in playerQuests)
        {
            // Update kill monster objectives
            quest.UpdateObjectiveProgress(QuestObjectiveType.KillMonsters, 1);
            quest.UpdateObjectiveProgress(QuestObjectiveType.KillSpecificMonster, 1, nameId);

            // Also try matching by base tier name (champion/boss variants should count)
            if (!string.IsNullOrEmpty(tierId) && tierId != nameId)
            {
                quest.UpdateObjectiveProgress(QuestObjectiveType.KillSpecificMonster, 1, tierId);
            }

            // Update boss kill objectives
            if (isBoss)
            {
                quest.UpdateObjectiveProgress(QuestObjectiveType.KillBoss, 1, nameId);
                if (!string.IsNullOrEmpty(tierId) && tierId != nameId)
                {
                    quest.UpdateObjectiveProgress(QuestObjectiveType.KillBoss, 1, tierId);
                }
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
            // Calculate reward — use BountyGold if set (king bounties), else legacy byte Reward
            long reward = bounty.BountyGold > 0 ? bounty.BountyGold : bounty.Reward * 100L;
            if (reward <= 0) reward = 500; // Minimum reward

            // Give player the reward immediately
            player.Gold += reward;
            DebugLogger.Instance.LogInfo("GOLD", $"BOUNTY REWARD: {player.DisplayName} +{reward:N0}g for bounty on {npcName} (gold now {player.Gold:N0})");
            long xpReward = Math.Max(player.Level * 50, reward / 5); // XP scales with player level and bounty
            player.Experience += xpReward;
            totalReward += reward;

            // Mark bounty as completed
            bounty.Deleted = true;
            bounty.Occupier = player.Name2;
            bounty.OccupiedDays = 1;

            // Update statistics
            StatisticsManager.Current?.RecordBountyComplete();

            // Announce the bounty completion
            NewsSystem.Instance?.Newsy(true, Loc.Get("quest.bounty_collected_news", player.Name2, npcName, reward));

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
            (q.QuestTarget == QuestTarget.DefeatNPC || q.QuestTarget == QuestTarget.Assassin));

        if (claimed != null) return claimed.Initiator;

        // Also check unclaimed bounties (auto-complete path)
        var unclaimed = questDatabase.FirstOrDefault(q =>
            !q.Deleted &&
            !string.IsNullOrEmpty(q.TargetNPCName) &&
            q.TargetNPCName.Equals(npcName, StringComparison.OrdinalIgnoreCase) &&
            string.IsNullOrEmpty(q.Occupier) &&
            (q.QuestTarget == QuestTarget.DefeatNPC || q.QuestTarget == QuestTarget.Assassin));

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
            return Loc.Get("quest.no_tracked_objectives");
        }

        var completed = quest.Objectives.Count(o => o.IsComplete && !o.IsOptional);
        var required = quest.Objectives.Count(o => !o.IsOptional);
        var optional = quest.Objectives.Count(o => o.IsOptional && o.IsComplete);
        var totalOptional = quest.Objectives.Count(o => o.IsOptional);

        var summary = Loc.Get("quest.progress_summary", completed, required);
        if (totalOptional > 0)
        {
            summary += Loc.Get("quest.progress_summary_bonus", optional, totalOptional);
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
        // Don't add starter quests that already exist in the database (by stable ID).
        // This prevents the QuestDeleted bug in MUD mode where quests got new IDs
        // each time they were regenerated, invalidating cached quest references.

        // Beginner quests (levels 1-15)
        // Note: First arg is stable ID key (never changes), display title comes from Loc
        CreateStarterQuest("Wolf Pack", "quest.starter.wolf_pack", "quest.starter.wolf_pack_desc",
            QuestTarget.Monster, 1, 1, 15,
            new[] { ("Wolf", 5), ("Dire Wolf", 2) });

        CreateStarterQuest("The Goblin Menace", "quest.starter.goblin_menace", "quest.starter.goblin_menace_desc",
            QuestTarget.Monster, 1, 1, 15,
            new[] { ("Goblin", 4), ("Hobgoblin", 2) });

        CreateStarterQuest("Undead Rising", "quest.starter.undead_rising", "quest.starter.undead_rising_desc",
            QuestTarget.Monster, 2, 5, 20,
            new[] { ("Zombie", 5), ("Ghoul", 2) });

        // Intermediate quests (levels 10-35)
        CreateStarterQuest("The Orc Warlord", "quest.starter.orc_warlord", "quest.starter.orc_warlord_desc",
            QuestTarget.Monster, 2, 10, 35,
            new[] { ("Orc", 6), ("Orc Warrior", 3), ("Orc Berserker", 1) });

        CreateStarterQuest("Troll Hunt", "quest.starter.troll_hunt", "quest.starter.troll_hunt_desc",
            QuestTarget.Monster, 2, 15, 40,
            new[] { ("Ogre", 3), ("Troll", 2) });

        CreateStarterQuest("Dungeon Delve", "quest.starter.dungeon_delve", "quest.starter.dungeon_delve_desc",
            QuestTarget.ReachFloor, 2, 10, 40,
            floorTarget: 10);

        // Advanced quests (levels 20-55)
        CreateStarterQuest("Dragon Hunt", "quest.starter.dragon_hunt", "quest.starter.dragon_hunt_desc",
            QuestTarget.Monster, 3, 20, 55,
            new[] { ("Drake", 3), ("Wyvern", 1) });

        CreateStarterQuest("Deep Descent", "quest.starter.deep_descent", "quest.starter.deep_descent_desc",
            QuestTarget.ReachFloor, 3, 20, 60,
            floorTarget: 25);

        CreateStarterQuest("Deep Exploration", "quest.starter.deep_exploration", "quest.starter.deep_exploration_desc",
            QuestTarget.ReachFloor, 3, 10, 40,
            floorTarget: 15);

        // Expert quests (levels 50+)
        CreateStarterQuest("The Lich King", "quest.starter.lich_king", "quest.starter.lich_king_desc",
            QuestTarget.Monster, 4, 50, 100,
            new[] { ("Lich", 1), ("Wraith", 4), ("Shade", 3) });

        CreateStarterQuest("Abyssal Expedition", "quest.starter.abyssal_expedition", "quest.starter.abyssal_expedition_desc",
            QuestTarget.ReachFloor, 4, 35, 100,
            floorTarget: 50);

        // GD.Print($"[QuestSystem] Created {questDatabase.Count} starter quests");
    }

    /// <summary>
    /// Helper to create a starter quest
    /// </summary>
    private static void CreateStarterQuest(string stableKey, string titleKey, string descKey,
        QuestTarget target, byte difficulty, int minLevel, int maxLevel,
        (string name, int count)[]? monsters = null, int floorTarget = 0)
    {
        // Use deterministic ID based on stable key so starter quests survive
        // the remove/recreate cycle in MUD mode without changing IDs
        string stableId = "STARTER_" + stableKey.ToUpper().Replace(" ", "_");

        // v0.61.3: if this starter quest already exists, back-populate its
        // localization keys when they're missing. Saves from before v0.61.3
        // have the keys empty even though the keys themselves match what
        // CreateStarterQuest would generate now. Skip the rest of the work
        // (don't reset progress / occupier / objectives) since the quest is
        // already in the DB.
        var existing = questDatabase.FirstOrDefault(q => q.Id == stableId);
        if (existing != null)
        {
            if (string.IsNullOrEmpty(existing.TitleKey)) existing.TitleKey = titleKey;
            if (string.IsNullOrEmpty(existing.CommentKey)) existing.CommentKey = descKey;
            if (string.IsNullOrEmpty(existing.InitiatorKey)) existing.InitiatorKey = "quest.initiator.royal_council";
            return;
        }

        // v0.61.3: store both the rendered (English fallback) text AND the
        // localization keys. Display sites prefer the keys via GetDisplayTitle
        // / GetDisplayComment so each viewer sees the quest in their own
        // session language. Pre-fix, the rendered text was locked to whatever
        // language the bootstrap process happened in -- usually English since
        // bootstrap runs before any player session attaches a language.
        var quest = new Quest
        {
            Title = Loc.Get(titleKey),
            TitleKey = titleKey,
            Initiator = Loc.Get("quest.initiator.royal_council"),
            InitiatorKey = "quest.initiator.royal_council",
            QuestType = QuestType.SingleQuest,
            QuestTarget = target,
            Difficulty = difficulty,
            Comment = Loc.Get(descKey),
            CommentKey = descKey,
            Date = DateTime.Now,
            MinLevel = minLevel,
            MaxLevel = maxLevel,
            DaysToComplete = 14 // Generous time limit for starter quests
        };
        // Override the auto-generated ID with our stable one
        quest.Id = stableId;

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
            // Create a KillSpecificMonster objective for each monster type.
            // v0.61.3: use QuestObjective.Localized so DescriptionKey + args
            // travel with the objective and translate at display time.
            foreach (var (name, count) in monsters)
            {
                string displayName = count > 1 ? GetPluralName(name) : name;
                quest.Objectives.Add(QuestObjective.Localized(
                    QuestObjectiveType.KillSpecificMonster,
                    "quest.objective.kill_count",
                    new object[] { count, displayName },
                    count,
                    name.ToLower().Replace(" ", "_"),
                    name
                ));
            }
        }
        else if (target == QuestTarget.ReachFloor && floorTarget > 0)
        {
            quest.Objectives.Add(QuestObjective.Localized(
                QuestObjectiveType.ReachDungeonFloor,
                "quest.objective.reach_floor_short",
                new object[] { floorTarget },
                floorTarget,
                "",
                $"Floor {floorTarget}"
            ));
        }
        else if (target == QuestTarget.ClearBoss)
        {
            var bossName = monsters?.FirstOrDefault().name ?? "Boss";
            quest.Objectives.Add(QuestObjective.Localized(
                QuestObjectiveType.KillBoss,
                "quest.objective.defeat_boss",
                new object[] { bossName },
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

    /// <summary>
    /// Get the plural form of a monster name using English pluralization rules.
    /// </summary>
    private static string GetPluralName(string name)
    {
        if (string.IsNullOrEmpty(name)) return name;
        var lower = name.ToLower();

        // Irregular plurals
        if (lower.EndsWith("wolf")) return name.Substring(0, name.Length - 1) + "ves";
        if (lower.EndsWith("thief")) return name.Substring(0, name.Length - 1) + "ves";
        if (lower.EndsWith("elf")) return name.Substring(0, name.Length - 1) + "ves";
        if (lower.EndsWith("man")) return name.Substring(0, name.Length - 2) + "en";

        // Words ending in s, x, z, ch, sh → add "es"
        if (lower.EndsWith("s") || lower.EndsWith("x") || lower.EndsWith("z") ||
            lower.EndsWith("ch") || lower.EndsWith("sh"))
            return name + "es";

        // Words ending in consonant + y → change y to ies
        if (lower.EndsWith("y") && lower.Length > 1 && !"aeiou".Contains(lower[lower.Length - 2]))
            return name.Substring(0, name.Length - 1) + "ies";

        return name + "s";
    }

    #endregion

    #region King Bounty System

    private static string KING_BOUNTY_INITIATOR => "The Crown";

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
            NewsSystem.Instance?.Newsy(true, Loc.Get("quest.new_bounty_news", bounty.Title));
        }
    }

    /// <summary>
    /// Create a bounty targeting a specific NPC
    /// </summary>
    private static Quest CreateNPCBounty(NPC target, string kingName)
    {
        var crimeKeys = new[]
        {
            "quest.crime.crown_crimes",
            "quest.crime.treason",
            "quest.crime.smuggling",
            "quest.crime.treasury_theft",
            "quest.crime.disturbing_peace",
            "quest.crime.dark_sorcery",
            "quest.crime.assault_guard",
            "quest.crime.plotting_rebellion"
        };

        var crimeKey = crimeKeys[random.Next(crimeKeys.Length)];
        var crime = Loc.Get(crimeKey);  // bootstrap-language snapshot for the legacy Comment field
        var difficulty = (byte)Math.Min(4, Math.Max(1, target.Level / 15 + 1));
        var reward = target.Level * 100 * difficulty;

        var bounty = new Quest
        {
            Title = Loc.Get("quest.bounty.wanted", target.Name),
            TitleKey = "quest.bounty.wanted",
            TitleArgs = new List<string> { target.Name },
            Initiator = KING_BOUNTY_INITIATOR,
            // KING_BOUNTY_INITIATOR is a literal English string "The Crown" -- the
            // initiator-key path lets us localize it per viewer.
            InitiatorKey = "quest.initiator.the_crown",
            QuestType = QuestType.SingleQuest,
            QuestTarget = QuestTarget.DefeatNPC,
            Difficulty = difficulty,
            Comment = Loc.Get("quest.bounty.comment_npc", target.Name, crime),
            CommentKey = "quest.bounty.comment_npc",
            // Crime name is a localization key, not the target's name. Use the
            // "loc:" prefix so GetDisplayComment resolves the crime in the
            // viewer's language at render time. target.Name is a real string
            // (NPC name), no prefix needed.
            CommentArgs = new List<string> { target.Name, "loc:" + crimeKey },
            Date = DateTime.Now,
            MinLevel = Math.Max(1, target.Level - 10),
            MaxLevel = 9999,
            DaysToComplete = 14,
            Reward = (byte)Math.Min(255, reward / 100),
            RewardType = QuestRewardType.Money,
            TargetNPCName = target.Name
        };

        bounty.Objectives.Add(QuestObjective.Localized(
            QuestObjectiveType.DefeatNPC,
            "quest.objective.defeat_npc",
            new object[] { target.Name },
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
        var bountyTypeKeys = new[]
        {
            ("quest.generic_bounty.bandit_leader", "quest.generic_bounty.bandit_leader_desc", 5),
            ("quest.generic_bounty.escaped_prisoner", "quest.generic_bounty.escaped_prisoner_desc", 3),
            ("quest.generic_bounty.cult_leader", "quest.generic_bounty.cult_leader_desc", 6),
            ("quest.generic_bounty.rogue_mage", "quest.generic_bounty.rogue_mage_desc", 4),
            ("quest.generic_bounty.orc_warlord", "quest.generic_bounty.orc_warlord_desc", 7)
        };

        var (titleKey, descKey, killCount) = bountyTypeKeys[random.Next(bountyTypeKeys.Length)];
        var title = Loc.Get(titleKey);  // bootstrap-language snapshot for legacy Title field
        var desc = Loc.Get(descKey);
        var difficulty = (byte)(random.Next(1, 5)); // 1-4 difficulty
        killCount += difficulty * 2; // Scale kills with difficulty

        var bounty = new Quest
        {
            // Title (legacy string) is rendered in bootstrap language for back-
            // compat with non-localized display sites. The display layer prefers
            // TitleKey+TitleArgs; passing "loc:<innerKey>" makes Quest.GetDisplayTitle
            // resolve the criminal-type name in the VIEWER's session language at
            // render time instead of freezing the bootstrap-language string.
            Title = Loc.Get("quest.bounty.wanted", title),
            TitleKey = "quest.bounty.wanted",
            TitleArgs = new List<string> { "loc:" + titleKey },
            Initiator = KING_BOUNTY_INITIATOR,
            InitiatorKey = "quest.initiator.the_crown",
            QuestType = QuestType.SingleQuest,
            QuestTarget = QuestTarget.Monster,
            Difficulty = difficulty,
            Comment = desc,
            CommentKey = descKey,
            Date = DateTime.Now,
            MinLevel = difficulty * 5,
            MaxLevel = 9999,
            DaysToComplete = 14
        };

        bounty.Objectives.Add(QuestObjective.Localized(
            QuestObjectiveType.KillMonsters,
            "quest.objective.slay_monsters",
            new object[] { killCount },
            killCount
        ));

        SetDefaultRewards(bounty);
        bounty.Reward = (byte)Math.Min(255, bounty.Reward * 2); // King bounties pay double

        return bounty;
    }

    /// <summary>
    /// The King can post a bounty on the player if they commit crimes
    /// </summary>
    public static void PostBountyOnPlayer(string playerName, string crime, long bountyAmount)
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
            existingBounty.BountyGold += bountyAmount;
            existingBounty.Comment += $" {Loc.Get("quest.bounty.additional_charge", crime)}";
            NewsSystem.Instance?.Newsy(true, Loc.Get("quest.bounty_increased_news", playerName, existingBounty.BountyGold));
            return;
        }

        var bounty = new Quest
        {
            Title = Loc.Get("quest.bounty.wanted", playerName),
            Initiator = KING_BOUNTY_INITIATOR,
            QuestType = QuestType.SingleQuest,
            QuestTarget = QuestTarget.DefeatNPC,
            Difficulty = 4, // Player bounties are always hard
            Comment = Loc.Get("quest.bounty.comment_player", playerName, crime),
            Date = DateTime.Now,
            MinLevel = 1,
            MaxLevel = 9999,
            DaysToComplete = 30, // Long duration for player bounties
            BountyGold = bountyAmount,  // Actual gold amount (not limited by byte Reward)
            RewardType = QuestRewardType.Money,
            TargetNPCName = playerName
        };

        bounty.Objectives.Add(new QuestObjective(
            QuestObjectiveType.DefeatNPC,
            Loc.Get("quest.objective.defeat_npc", playerName),
            1,
            playerName,
            playerName
        ));

        questDatabase.Add(bounty);
        NewsSystem.Instance?.Newsy(true, Loc.Get("quest.player_bounty_news", playerName));
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

        // v0.57.9 (Lumina report: "royal quest on floor 106, max is 100 — uncompletable").
        // The board-quest path has a CapFloor helper that clamps to the player's accessible
        // dungeon range; royal audience quests went through raw Math.Max with no upper bound,
        // so a Lv.94 with difficulty 4 got assigned floor 106. Match the board-quest ceiling:
        // max of MaxDungeonLevel (100) and player.Level + 10 (the dungeon-access range).
        int maxAccessibleFloor = Math.Min(GameConfig.MaxDungeonLevel, player.Level + 10);
        int ClampFloor(int raw) => Math.Clamp(raw, 1, maxAccessibleFloor);

        if (questDescription.Contains("monster") || questDescription.Contains("creature"))
        {
            questTarget = QuestTarget.Monster;
            objectiveType = QuestObjectiveType.KillMonsters;
            targetValue = 5 + difficulty * 3; // 8, 11, 14, 17 monsters
            targetName = GetRandomMonsterForLevel(player.Level);
        }
        else if (questDescription.Contains("artifact") || questDescription.Contains("recover"))
        {
            // FindArtifact removed (no tracking/completion code) — treat as dungeon exploration
            questTarget = QuestTarget.ReachFloor;
            objectiveType = QuestObjectiveType.ReachDungeonFloor;
            targetValue = ClampFloor(player.Level + difficulty * 3);
            targetName = $"Floor {targetValue}";
        }
        else if (questDescription.Contains("floor") || questDescription.Contains("clear"))
        {
            questTarget = QuestTarget.ClearFloor;
            objectiveType = QuestObjectiveType.ClearDungeonFloor;
            targetValue = ClampFloor(player.Level - 5 + difficulty * 5); // Near player level
            targetName = $"Floor {targetValue}";
        }
        else if (questDescription.Contains("criminal") || questDescription.Contains("hunt"))
        {
            questTarget = QuestTarget.DefeatNPC;
            objectiveType = QuestObjectiveType.KillBoss;
            targetValue = 1;
            targetName = Loc.Get("quest.wanted_criminal");
        }
        else // Default: dungeon investigation
        {
            questTarget = QuestTarget.ReachFloor;
            objectiveType = QuestObjectiveType.ReachDungeonFloor;
            targetValue = ClampFloor(player.Level + difficulty * 3);
            targetName = $"Floor {targetValue}";
        }

        var quest = new Quest
        {
            Title = Loc.Get("quest.royal_commission", questDescription),
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

    /// <summary>
    /// Called when a player purchases equipment - checks if any quests are completed
    /// </summary>
    public static void OnEquipmentPurchased(Character player, Equipment equipment)
    {
        if (player == null || equipment == null) return;

        var playerQuests = GetPlayerQuests(player.Name2);

        foreach (var quest in playerQuests)
        {
            if (quest.QuestTarget == QuestTarget.BuyWeapon ||
                quest.QuestTarget == QuestTarget.BuyArmor ||
                quest.QuestTarget == QuestTarget.BuyAccessory ||
                quest.QuestTarget == QuestTarget.BuyShield)
            {
                foreach (var objective in quest.Objectives.Where(o =>
                    o.ObjectiveType == QuestObjectiveType.PurchaseEquipment && !o.IsComplete))
                {
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
    /// Equipment purchase quests disabled — procedural shop inventory doesn't match static IDs.
    /// </summary>
    public static void EnsureEquipmentQuestsExist(int playerLevel)
    {
        // No-op: equipment purchase quests removed in v0.53.0
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
