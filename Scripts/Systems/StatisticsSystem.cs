using System;
using System.Collections.Generic;

/// <summary>
/// Comprehensive statistics tracking system for player progress
/// Tracks combat stats, economic activity, exploration, and more
/// </summary>
public class PlayerStatistics
{
    // === COMBAT STATISTICS ===
    public long TotalMonstersKilled { get; set; }
    public long TotalMonstersEncountered { get; set; }
    public long TotalBossesKilled { get; set; }
    public long TotalUniquesKilled { get; set; }
    public long TotalPlayerKills { get; set; }      // PvP kills
    public long TotalPlayerDeaths { get; set; }     // Deaths to players
    public long TotalMonsterDeaths { get; set; }    // Deaths to monsters
    public long TotalCombatsWon { get; set; }
    public long TotalCombatsLost { get; set; }
    public long TotalCombatsFled { get; set; }
    public long TotalDamageDealt { get; set; }
    public long TotalDamageTaken { get; set; }
    public long HighestSingleHit { get; set; }
    public long TotalCriticalHits { get; set; }
    public long TotalSpellsCast { get; set; }
    public long TotalAbilitiesUsed { get; set; }

    // === ECONOMIC STATISTICS ===
    public long TotalGoldEarned { get; set; }       // All gold ever obtained
    public long TotalGoldSpent { get; set; }        // All gold ever spent
    public long TotalGoldFromMonsters { get; set; }
    public long TotalGoldFromQuests { get; set; }
    public long TotalGoldFromSelling { get; set; }
    public long TotalGoldFromGambling { get; set; }
    public long TotalGoldLostGambling { get; set; }
    public long TotalGoldStolen { get; set; }       // Via thievery
    public long TotalGoldLostToThieves { get; set; }
    public long HighestGoldHeld { get; set; }       // Peak gold at any point
    public long TotalItemsBought { get; set; }
    public long TotalItemsSold { get; set; }
    public long MostExpensivePurchase { get; set; }

    // === DARK ALLEY STATISTICS (v0.41.0) ===
    public long TotalGamblingRounds { get; set; }
    public long TotalPickpocketAttempts { get; set; }
    public long TotalPickpocketSuccesses { get; set; }
    public long TotalPitFightsWon { get; set; }
    public long TotalPitFightsLost { get; set; }

    // === EXPERIENCE STATISTICS ===
    public long TotalExperienceEarned { get; set; }
    public long ExperienceFromMonsters { get; set; }
    public long ExperienceFromQuests { get; set; }
    public long ExperienceFromTraining { get; set; }
    public int HighestLevelReached { get; set; }
    public int TotalLevelUps { get; set; }

    // === EXPLORATION STATISTICS ===
    public int DeepestDungeonLevel { get; set; }
    public long TotalDungeonFloorsCovered { get; set; }
    public long TotalRoomsExplored { get; set; }
    public long TotalTrapsTriggered { get; set; }
    public long TotalTrapsDisarmed { get; set; }
    public long TotalSecretsFound { get; set; }
    public long TotalChestsOpened { get; set; }
    public Dictionary<string, int> LocationVisits { get; set; } = new();

    // === SOCIAL STATISTICS ===
    public long TotalNPCInteractions { get; set; }
    public long TotalConversations { get; set; }
    public long TotalGiftsGiven { get; set; }
    public long TotalFriendsGained { get; set; }
    public long TotalEnemiesMade { get; set; }
    public long TotalRomances { get; set; }
    public long TotalTeamBattles { get; set; }

    // === MAGIC SHOP STATISTICS ===
    public long TotalEnchantmentsApplied { get; set; }
    public long TotalLoveSpellsCast { get; set; }
    public long TotalDeathSpellsCast { get; set; }
    public long TotalMagicShopGoldSpent { get; set; }
    public long TotalAccessoriesPurchased { get; set; }

    // === SURVIVAL STATISTICS ===
    public long TotalHealingPotionsUsed { get; set; }
    public long TotalManaPotionsUsed { get; set; }
    public long TotalHealthRestored { get; set; }
    public long TotalManaRestored { get; set; }
    public long TotalTimesResurrected { get; set; }
    public long TotalDiseasesContracted { get; set; }
    public long TotalDiseasesCured { get; set; }
    public long TotalPoisonings { get; set; }
    public long TotalCursesBroken { get; set; }

    // === TIME STATISTICS ===
    public DateTime CharacterCreated { get; set; } = DateTime.Now;
    public TimeSpan TotalPlayTime { get; set; } = TimeSpan.Zero;
    public int TotalDaysPlayed { get; set; }
    public int TotalSessionsPlayed { get; set; }
    public DateTime LastPlayed { get; set; } = DateTime.Now;
    public TimeSpan LongestSession { get; set; } = TimeSpan.Zero;
    public int CurrentStreak { get; set; }          // Consecutive days played
    public int LongestStreak { get; set; }

    // === WORLD BOSS STATISTICS ===
    public long WorldBossesKilled { get; set; }
    public long WorldBossDamageDealt { get; set; }
    public long WorldBossMVPCount { get; set; }
    public HashSet<string> UniqueWorldBossTypes { get; set; } = new();

    // === ACHIEVEMENT TRIGGERS ===
    public int QuestsCompleted { get; set; }
    public int BountiesCompleted { get; set; }
    public int ChallengesCompleted { get; set; }
    public int TimesRuler { get; set; }
    public int DaysAsRuler { get; set; }

    // Session tracking (not saved, used for calculations)
    [System.Text.Json.Serialization.JsonIgnore]
    public DateTime SessionStart { get; set; } = DateTime.Now;
    [System.Text.Json.Serialization.JsonIgnore]
    private DateTime _lastAccumulatedAt = DateTime.Now; // Tracks last TotalPlayTime accumulation point

    // Session snapshot - captures stats at session start for summary calculation
    [System.Text.Json.Serialization.JsonIgnore]
    private long _sessionStartMonstersKilled;
    [System.Text.Json.Serialization.JsonIgnore]
    private long _sessionStartGoldEarned;
    [System.Text.Json.Serialization.JsonIgnore]
    private long _sessionStartExperience;
    [System.Text.Json.Serialization.JsonIgnore]
    private long _sessionStartDamageDealt;
    [System.Text.Json.Serialization.JsonIgnore]
    private int _sessionStartLevel;
    [System.Text.Json.Serialization.JsonIgnore]
    private long _sessionStartRoomsExplored;
    [System.Text.Json.Serialization.JsonIgnore]
    private long _sessionStartItemsBought;
    [System.Text.Json.Serialization.JsonIgnore]
    private long _sessionStartItemsSold;

    /// <summary>
    /// Update session time when saving. Accumulates time since last call
    /// without resetting SessionStart (which is used for session summary).
    /// </summary>
    public void UpdateSessionTime()
    {
        var now = DateTime.Now;
        var elapsed = now - _lastAccumulatedAt;
        TotalPlayTime += elapsed;
        _lastAccumulatedAt = now;

        // Check longest session using the true session start
        var fullSessionDuration = now - SessionStart;
        if (fullSessionDuration > LongestSession)
            LongestSession = fullSessionDuration;

        LastPlayed = now;
    }

    /// <summary>
    /// Track a new login session
    /// </summary>
    public void TrackNewSession()
    {
        TotalSessionsPlayed++;
        SessionStart = DateTime.Now;
        _lastAccumulatedAt = DateTime.Now;

        // Capture snapshot of current stats for session summary
        CaptureSessionSnapshot();

        // Check for streak
        var daysSinceLastPlay = (DateTime.Now.Date - LastPlayed.Date).Days;
        if (daysSinceLastPlay == 1)
        {
            CurrentStreak++;
            if (CurrentStreak > LongestStreak)
                LongestStreak = CurrentStreak;
        }
        else if (daysSinceLastPlay > 1)
        {
            CurrentStreak = 1; // Reset streak
        }
        // If same day, streak doesn't change

        LastPlayed = DateTime.Now;
    }

    /// <summary>
    /// Capture current stats as session start snapshot
    /// </summary>
    public void CaptureSessionSnapshot()
    {
        _sessionStartMonstersKilled = TotalMonstersKilled;
        _sessionStartGoldEarned = TotalGoldEarned;
        _sessionStartExperience = TotalExperienceEarned;
        _sessionStartDamageDealt = TotalDamageDealt;
        _sessionStartLevel = HighestLevelReached;
        _sessionStartRoomsExplored = TotalRoomsExplored;
        _sessionStartItemsBought = TotalItemsBought;
        _sessionStartItemsSold = TotalItemsSold;
    }

    /// <summary>
    /// Get session summary data
    /// </summary>
    public SessionSummary GetSessionSummary()
    {
        var sessionDuration = DateTime.Now - SessionStart;
        return new SessionSummary
        {
            Duration = sessionDuration,
            MonstersKilled = TotalMonstersKilled - _sessionStartMonstersKilled,
            GoldEarned = TotalGoldEarned - _sessionStartGoldEarned,
            ExperienceGained = TotalExperienceEarned - _sessionStartExperience,
            DamageDealt = TotalDamageDealt - _sessionStartDamageDealt,
            LevelsGained = HighestLevelReached - _sessionStartLevel,
            RoomsExplored = TotalRoomsExplored - _sessionStartRoomsExplored,
            ItemsBought = TotalItemsBought - _sessionStartItemsBought,
            ItemsSold = TotalItemsSold - _sessionStartItemsSold
        };
    }

    /// <summary>
    /// Session summary data structure
    /// </summary>
    public class SessionSummary
    {
        public TimeSpan Duration { get; set; }
        public long MonstersKilled { get; set; }
        public long GoldEarned { get; set; }
        public long ExperienceGained { get; set; }
        public long DamageDealt { get; set; }
        public int LevelsGained { get; set; }
        public long RoomsExplored { get; set; }
        public long ItemsBought { get; set; }
        public long ItemsSold { get; set; }
    }

    /// <summary>
    /// Record a monster kill
    /// </summary>
    public void RecordMonsterKill(long xpGained, long goldGained, bool isBoss, bool isUnique)
    {
        TotalMonstersKilled++;
        TotalCombatsWon++;

        if (isBoss) TotalBossesKilled++;
        if (isUnique) TotalUniquesKilled++;

        TotalExperienceEarned += xpGained;
        ExperienceFromMonsters += xpGained;
        TotalGoldEarned += goldGained;
        TotalGoldFromMonsters += goldGained;
    }

    /// <summary>
    /// Record a world boss kill contribution
    /// </summary>
    public void RecordWorldBossKill(string bossType, long damageDealt, bool isMVP)
    {
        WorldBossesKilled++;
        WorldBossDamageDealt += damageDealt;
        if (isMVP) WorldBossMVPCount++;
        UniqueWorldBossTypes ??= new HashSet<string>();
        UniqueWorldBossTypes.Add(bossType);
    }

    /// <summary>
    /// Record damage dealt
    /// </summary>
    public void RecordDamageDealt(long damage, bool isCritical)
    {
        TotalDamageDealt += damage;
        if (damage > HighestSingleHit)
            HighestSingleHit = damage;
        if (isCritical)
            TotalCriticalHits++;
    }

    /// <summary>
    /// Record damage taken
    /// </summary>
    public void RecordDamageTaken(long damage)
    {
        TotalDamageTaken += damage;
    }

    /// <summary>
    /// Record player death
    /// </summary>
    public void RecordDeath(bool toPlayer)
    {
        TotalCombatsLost++;
        if (toPlayer)
            TotalPlayerDeaths++;
        else
            TotalMonsterDeaths++;
    }

    /// <summary>
    /// Record gold changes
    /// </summary>
    public void RecordGoldChange(long currentGold)
    {
        if (currentGold > HighestGoldHeld)
            HighestGoldHeld = currentGold;
    }

    /// <summary>
    /// Record gold spent on a purchase
    /// </summary>
    public void RecordPurchase(long amount)
    {
        TotalGoldSpent += amount;
        TotalItemsBought++;
        if (amount > MostExpensivePurchase)
            MostExpensivePurchase = amount;
    }

    /// <summary>
    /// Record gold spent (non-purchase, like services, healing, etc.)
    /// </summary>
    public void RecordGoldSpent(long amount)
    {
        TotalGoldSpent += amount;
    }

    /// <summary>
    /// Record item sold
    /// </summary>
    public void RecordSale(long amount)
    {
        TotalGoldEarned += amount;
        TotalGoldFromSelling += amount;
        TotalItemsSold++;
    }

    /// <summary>
    /// Record level up
    /// </summary>
    public void RecordLevelUp(int newLevel)
    {
        TotalLevelUps++;
        if (newLevel > HighestLevelReached)
            HighestLevelReached = newLevel;
    }

    /// <summary>
    /// Record dungeon exploration
    /// </summary>
    public void RecordDungeonLevel(int level)
    {
        TotalDungeonFloorsCovered++;
        if (level > DeepestDungeonLevel)
            DeepestDungeonLevel = level;
    }

    /// <summary>
    /// Record location visit
    /// </summary>
    public void RecordLocationVisit(string locationName)
    {
        if (!LocationVisits.ContainsKey(locationName))
            LocationVisits[locationName] = 0;
        LocationVisits[locationName]++;
    }

    /// <summary>
    /// Record a PvP kill (killing another player/NPC)
    /// </summary>
    public void RecordPlayerKill()
    {
        TotalPlayerKills++;
        TotalCombatsWon++;
    }

    /// <summary>
    /// Record a quest completion
    /// </summary>
    public void RecordQuestComplete()
    {
        QuestsCompleted++;
    }

    public void RecordQuestGoldReward(long amount)
    {
        TotalGoldFromQuests += amount;
        TotalGoldEarned += amount;
    }

    /// <summary>
    /// Record a bounty completion
    /// </summary>
    public void RecordBountyComplete()
    {
        BountiesCompleted++;
    }

    /// <summary>
    /// Record healing potion usage
    /// </summary>
    public void RecordPotionUsed(long healthRestored)
    {
        TotalHealingPotionsUsed++;
        TotalHealthRestored += healthRestored;
    }

    /// <summary>
    /// Record mana potion usage
    /// </summary>
    public void RecordManaPotionUsed(long manaRestored)
    {
        TotalManaPotionsUsed++;
        TotalManaRestored += manaRestored;
    }

    /// <summary>
    /// Record health restored (from any source - healer, spell, etc.)
    /// </summary>
    public void RecordHealthRestored(long amount)
    {
        TotalHealthRestored += amount;
    }

    /// <summary>
    /// Record resurrection used
    /// </summary>
    public void RecordResurrection()
    {
        TotalTimesResurrected++;
    }

    /// <summary>
    /// Record disease contracted
    /// </summary>
    public void RecordDiseaseContracted()
    {
        TotalDiseasesContracted++;
    }

    /// <summary>
    /// Record disease cured
    /// </summary>
    public void RecordDiseaseCured()
    {
        TotalDiseasesCured++;
    }

    /// <summary>
    /// Record curse broken
    /// </summary>
    public void RecordCurseBroken()
    {
        TotalCursesBroken++;
    }

    /// <summary>
    /// Record poisoning
    /// </summary>
    public void RecordPoisoning()
    {
        TotalPoisonings++;
    }

    /// <summary>
    /// Record an enchantment applied at the Magic Shop
    /// </summary>
    public void RecordEnchantment(long goldSpent)
    {
        TotalEnchantmentsApplied++;
        TotalMagicShopGoldSpent += goldSpent;
    }

    /// <summary>
    /// Record a love spell cast at the Magic Shop
    /// </summary>
    public void RecordLoveSpellCast(long goldSpent)
    {
        TotalLoveSpellsCast++;
        TotalMagicShopGoldSpent += goldSpent;
    }

    /// <summary>
    /// Record a death spell cast at the Magic Shop
    /// </summary>
    public void RecordDeathSpellCast(long goldSpent)
    {
        TotalDeathSpellsCast++;
        TotalMagicShopGoldSpent += goldSpent;
    }

    /// <summary>
    /// Record a magic shop purchase (accessories, potions, etc.)
    /// </summary>
    public void RecordMagicShopPurchase(long goldSpent)
    {
        TotalMagicShopGoldSpent += goldSpent;
    }

    /// <summary>
    /// Record an accessory purchased at the Magic Shop
    /// </summary>
    public void RecordAccessoryPurchase(long goldSpent)
    {
        TotalAccessoriesPurchased++;
        TotalMagicShopGoldSpent += goldSpent;
    }

    /// <summary>
    /// Record a chest being opened
    /// </summary>
    public void RecordChestOpened()
    {
        TotalChestsOpened++;
    }

    /// <summary>
    /// Record a secret being found
    /// </summary>
    public void RecordSecretFound()
    {
        TotalSecretsFound++;
    }

    /// <summary>
    /// Record making a new friend (NPC reaching Friendship level)
    /// </summary>
    public void RecordFriendMade()
    {
        TotalFriendsGained++;
    }

    /// <summary>
    /// Record a trap being triggered
    /// </summary>
    public void RecordTrapTriggered()
    {
        TotalTrapsTriggered++;
    }

    /// <summary>
    /// Record a trap being disarmed
    /// </summary>
    public void RecordTrapDisarmed()
    {
        TotalTrapsDisarmed++;
    }

    // === DARK ALLEY TRACKING (v0.41.0) ===

    public void RecordGamblingWin(long amount)
    {
        TotalGoldFromGambling += amount;
        TotalGoldEarned += amount;
        TotalGamblingRounds++;
    }

    public void RecordGamblingLoss(long amount)
    {
        TotalGoldLostGambling += amount;
        TotalGoldSpent += amount;
        TotalGamblingRounds++;
    }

    public void RecordPickpocketAttempt(bool success, long goldStolen = 0)
    {
        TotalPickpocketAttempts++;
        if (success)
        {
            TotalPickpocketSuccesses++;
            TotalGoldStolen += goldStolen;
            TotalGoldEarned += goldStolen;
        }
    }

    public void RecordPitFight(bool won, long goldChange = 0)
    {
        if (won)
        {
            TotalPitFightsWon++;
            TotalGoldEarned += goldChange;
        }
        else
        {
            TotalPitFightsLost++;
        }
    }

    /// <summary>
    /// Calculate combat win rate
    /// </summary>
    public double GetCombatWinRate()
    {
        long totalCombats = TotalCombatsWon + TotalCombatsLost + TotalCombatsFled;
        if (totalCombats == 0) return 0;
        return (double)TotalCombatsWon / totalCombats * 100;
    }

    /// <summary>
    /// Calculate average damage per hit
    /// </summary>
    public double GetAverageDamagePerHit()
    {
        if (TotalMonstersKilled == 0) return 0;
        return (double)TotalDamageDealt / TotalMonstersKilled;
    }

    /// <summary>
    /// Get formatted play time string (includes current session without accumulating)
    /// </summary>
    public string GetFormattedPlayTime()
    {
        // Peek at total including current un-accumulated time, without side effects
        var currentTotal = TotalPlayTime + (DateTime.Now - _lastAccumulatedAt);

        if (currentTotal.TotalHours >= 1)
            return $"{(int)currentTotal.TotalHours}h {currentTotal.Minutes}m";
        else
            return $"{currentTotal.Minutes}m {currentTotal.Seconds}s";
    }

    /// <summary>
    /// Reset all statistics to zero. Used when resetting Steam stats to prevent
    /// stat-to-achievement auto-triggers on the next sync.
    /// </summary>
    public void ResetAllStats()
    {
        // Combat stats
        TotalMonstersKilled = 0;
        TotalMonstersEncountered = 0;
        TotalBossesKilled = 0;
        TotalUniquesKilled = 0;
        TotalPlayerKills = 0;
        TotalPlayerDeaths = 0;
        TotalMonsterDeaths = 0;
        TotalCombatsWon = 0;
        TotalCombatsLost = 0;
        TotalCombatsFled = 0;
        TotalDamageDealt = 0;
        TotalDamageTaken = 0;
        HighestSingleHit = 0;
        TotalCriticalHits = 0;
        TotalSpellsCast = 0;
        TotalAbilitiesUsed = 0;

        // Economic stats
        TotalGoldEarned = 0;
        TotalGoldSpent = 0;
        TotalGoldFromMonsters = 0;
        TotalGoldFromQuests = 0;
        TotalGoldFromSelling = 0;
        TotalGoldFromGambling = 0;
        TotalGoldLostGambling = 0;
        TotalGoldStolen = 0;
        TotalGoldLostToThieves = 0;
        HighestGoldHeld = 0;
        TotalItemsBought = 0;
        TotalGamblingRounds = 0;
        TotalPickpocketAttempts = 0;
        TotalPickpocketSuccesses = 0;
        TotalPitFightsWon = 0;
        TotalPitFightsLost = 0;
        TotalItemsSold = 0;
        MostExpensivePurchase = 0;

        // Experience stats
        TotalExperienceEarned = 0;
        ExperienceFromMonsters = 0;
        ExperienceFromQuests = 0;
        ExperienceFromTraining = 0;
        HighestLevelReached = 0;
        TotalLevelUps = 0;

        // Exploration stats
        DeepestDungeonLevel = 0;
        TotalDungeonFloorsCovered = 0;
        TotalRoomsExplored = 0;
        TotalTrapsTriggered = 0;
        TotalTrapsDisarmed = 0;
        TotalSecretsFound = 0;
        TotalChestsOpened = 0;
        LocationVisits.Clear();

        // Social stats
        TotalNPCInteractions = 0;
        TotalConversations = 0;
        TotalGiftsGiven = 0;
        TotalFriendsGained = 0;
        TotalEnemiesMade = 0;
        TotalRomances = 0;
        TotalTeamBattles = 0;

        // Survival stats
        TotalHealingPotionsUsed = 0;
        TotalManaPotionsUsed = 0;
        TotalHealthRestored = 0;
        TotalManaRestored = 0;
        TotalTimesResurrected = 0;
        TotalDiseasesContracted = 0;
        TotalDiseasesCured = 0;
        TotalPoisonings = 0;
        TotalCursesBroken = 0;

        // Magic Shop stats
        TotalEnchantmentsApplied = 0;
        TotalLoveSpellsCast = 0;
        TotalDeathSpellsCast = 0;
        TotalMagicShopGoldSpent = 0;
        TotalAccessoriesPurchased = 0;

        // Time stats - keep CharacterCreated but reset play time
        TotalPlayTime = TimeSpan.Zero;
        TotalDaysPlayed = 0;
        TotalSessionsPlayed = 0;
        LongestSession = TimeSpan.Zero;
        CurrentStreak = 0;
        LongestStreak = 0;

        // World boss stats
        WorldBossesKilled = 0;
        WorldBossDamageDealt = 0;
        WorldBossMVPCount = 0;
        UniqueWorldBossTypes?.Clear();

        // Achievement triggers
        QuestsCompleted = 0;
        BountiesCompleted = 0;
        ChallengesCompleted = 0;
        TimesRuler = 0;
        DaysAsRuler = 0;
    }
}

/// <summary>
/// Static statistics manager for the current player
/// </summary>
public static class StatisticsManager
{
    private static PlayerStatistics? _current;

    /// <summary>
    /// Get or create statistics for current player
    /// </summary>
    public static PlayerStatistics Current
    {
        get => _current ??= new PlayerStatistics();
        set => _current = value;
    }

    /// <summary>
    /// Initialize statistics for a new character
    /// </summary>
    public static void InitializeNew()
    {
        _current = new PlayerStatistics
        {
            CharacterCreated = DateTime.Now,
            SessionStart = DateTime.Now,
            TotalSessionsPlayed = 1
        };
    }

    /// <summary>
    /// Load statistics from save data
    /// </summary>
    public static void LoadFromSaveData(PlayerStatistics? stats)
    {
        if (stats != null)
        {
            _current = stats;
            _current.TrackNewSession();
        }
        else
        {
            InitializeNew();
        }
    }

    /// <summary>
    /// Reset statistics (for testing or new game+)
    /// </summary>
    public static void Reset()
    {
        _current = null;
    }
}
