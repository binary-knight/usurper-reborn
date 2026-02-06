using System;
using UsurperRemake.Systems;

/// <summary>
/// Steam integration for achievements and other Steam features.
/// Only activates when the game is launched through Steam.
/// Uses conditional compilation to avoid Steamworks.NET dependency when not available.
/// </summary>
public static class SteamIntegration
{
    private static bool _initialized = false;
    private static bool _steamAvailable = false;
    private static bool _syncDisabledUntilRestart = false;

    // Steam Stat API names - must match Steamworks dashboard configuration
    public static class StatNames
    {
        public const string MonstersKilled = "monsters_killed";
        public const string BossesKilled = "bosses_killed";
        public const string HighestLevel = "highest_level";
        public const string DeepestDungeon = "deepest_dungeon";
        public const string TotalGoldEarned = "gold_earned";
        public const string TotalGoldSpent = "gold_spent";
        public const string TotalDamageDealt = "damage_dealt";
        public const string QuestsCompleted = "quests_completed";
        public const string TotalDeaths = "total_deaths";
        public const string PlayTimeMinutes = "playtime_minutes";
        public const string CriticalHits = "critical_hits";
        public const string SecretsFound = "secrets_found";
        public const string ChestsOpened = "chests_opened";
        public const string ItemsBought = "items_bought";
    }

    /// <summary>
    /// Whether Steam is available and initialized
    /// </summary>
    public static bool IsAvailable => _initialized && _steamAvailable;

    /// <summary>
    /// Initialize Steam integration. Call this at game startup.
    /// Returns true if Steam is available, false otherwise.
    /// </summary>
    public static bool Initialize()
    {
        if (_initialized)
            return _steamAvailable;

        _initialized = true;

        try
        {
#if STEAM_BUILD
            DebugLogger.Instance?.LogInfo("STEAM", "This is a STEAM_BUILD - attempting Steam initialization");
            // Only attempt Steam initialization if compiled with STEAM_BUILD flag
            if (!Steamworks.SteamAPI.Init())
            {
                DebugLogger.Instance?.LogInfo("STEAM", "Steam not running - achievements will be local only");
                _steamAvailable = false;
                return false;
            }

            _steamAvailable = true;
            DebugLogger.Instance?.LogInfo("STEAM", "Steam initialized successfully");

            // Request current stats from Steam
            Steamworks.SteamUserStats.RequestCurrentStats();

            return true;
#else
            // Not a Steam build - Steam features disabled
            DebugLogger.Instance?.LogInfo("STEAM", "Non-Steam build - Steam features disabled");
            _steamAvailable = false;
            return false;
#endif
        }
        catch (Exception ex)
        {
            DebugLogger.Instance?.LogError("STEAM", $"Failed to initialize Steam: {ex.Message}");
            _steamAvailable = false;
            return false;
        }
    }

    /// <summary>
    /// Call this periodically (e.g., in your game loop) to process Steam callbacks.
    /// </summary>
    public static void RunCallbacks()
    {
        if (!_steamAvailable) return;

#if STEAM_BUILD
        try
        {
            Steamworks.SteamAPI.RunCallbacks();
        }
        catch (Exception ex)
        {
            DebugLogger.Instance?.LogError("STEAM", $"Error running Steam callbacks: {ex.Message}");
        }
#endif
    }

    /// <summary>
    /// Shutdown Steam integration. Call this when the game exits.
    /// </summary>
    public static void Shutdown()
    {
        if (!_steamAvailable) return;

#if STEAM_BUILD
        try
        {
            Steamworks.SteamAPI.Shutdown();
            DebugLogger.Instance?.LogInfo("STEAM", "Steam shutdown complete");
        }
        catch (Exception ex)
        {
            DebugLogger.Instance?.LogError("STEAM", $"Error during Steam shutdown: {ex.Message}");
        }
#endif

        _steamAvailable = false;
        _initialized = false;
    }

    /// <summary>
    /// Unlock a Steam achievement by its API name.
    /// The API name should match what's configured in Steamworks.
    /// </summary>
    /// <param name="achievementId">The achievement API name (e.g., "first_blood")</param>
    /// <returns>True if the achievement was unlocked, false if Steam unavailable or already unlocked</returns>
    public static bool UnlockAchievement(string achievementId)
    {
        if (!_steamAvailable)
        {
            DebugLogger.Instance?.LogDebug("STEAM", $"UnlockAchievement({achievementId}): Steam not available");
            return false;
        }

#if STEAM_BUILD
        try
        {
            // Check if already unlocked
            if (Steamworks.SteamUserStats.GetAchievement(achievementId, out bool alreadyUnlocked) && alreadyUnlocked)
            {
                return false; // Already had it
            }

            // Unlock the achievement
            if (Steamworks.SteamUserStats.SetAchievement(achievementId))
            {
                // Store stats to sync with Steam
                Steamworks.SteamUserStats.StoreStats();
                DebugLogger.Instance?.LogInfo("STEAM", $"Achievement unlocked on Steam: {achievementId}");
                return true;
            }
            else
            {
                DebugLogger.Instance?.LogWarning("STEAM", $"Failed to set Steam achievement: {achievementId}");
                return false;
            }
        }
        catch (Exception ex)
        {
            DebugLogger.Instance?.LogError("STEAM", $"Error unlocking Steam achievement {achievementId}: {ex.Message}");
            return false;
        }
#else
        // This branch is only reached in non-Steam builds
        DebugLogger.Instance?.LogDebug("STEAM", $"UnlockAchievement({achievementId}): Not a STEAM_BUILD - achievement not synced");
        return false;
#endif
    }

    /// <summary>
    /// Check if a Steam achievement is unlocked.
    /// </summary>
    public static bool IsAchievementUnlocked(string achievementId)
    {
        if (!_steamAvailable) return false;

#if STEAM_BUILD
        try
        {
            if (Steamworks.SteamUserStats.GetAchievement(achievementId, out bool unlocked))
            {
                return unlocked;
            }
        }
        catch (Exception ex)
        {
            DebugLogger.Instance?.LogError("STEAM", $"Error checking Steam achievement {achievementId}: {ex.Message}");
        }
#endif

        return false;
    }

    /// <summary>
    /// Clear a Steam achievement (for testing only).
    /// </summary>
    public static bool ClearAchievement(string achievementId)
    {
        if (!_steamAvailable) return false;

#if STEAM_BUILD
        try
        {
            if (Steamworks.SteamUserStats.ClearAchievement(achievementId))
            {
                Steamworks.SteamUserStats.StoreStats();
                DebugLogger.Instance?.LogInfo("STEAM", $"Achievement cleared on Steam: {achievementId}");
                return true;
            }
        }
        catch (Exception ex)
        {
            DebugLogger.Instance?.LogError("STEAM", $"Error clearing Steam achievement {achievementId}: {ex.Message}");
        }
#endif

        return false;
    }

    /// <summary>
    /// Reset ALL Steam stats and achievements for current user (TESTING ONLY).
    /// This is a destructive operation - use with caution!
    /// </summary>
    /// <param name="resetAchievements">If true, also resets all achievements</param>
    /// <returns>True if reset was successful</returns>
    public static bool ResetAllStats(bool resetAchievements = true)
    {
        if (!_steamAvailable) return false;

#if STEAM_BUILD
        try
        {
            if (Steamworks.SteamUserStats.ResetAllStats(resetAchievements))
            {
                // Disable stat syncing until game restart to prevent re-triggering achievements
                _syncDisabledUntilRestart = true;
                DebugLogger.Instance?.LogWarning("STEAM", $"ALL STATS RESET! Achievements reset: {resetAchievements}. Stat syncing disabled until restart.");
                return true;
            }
            else
            {
                DebugLogger.Instance?.LogError("STEAM", "Failed to reset Steam stats");
                return false;
            }
        }
        catch (Exception ex)
        {
            DebugLogger.Instance?.LogError("STEAM", $"Error resetting Steam stats: {ex.Message}");
            return false;
        }
#else
        return false;
#endif
    }

    /// <summary>
    /// Get the Steam user's display name, if available.
    /// </summary>
    public static string? GetPlayerName()
    {
        if (!_steamAvailable) return null;

#if STEAM_BUILD
        try
        {
            return Steamworks.SteamFriends.GetPersonaName();
        }
        catch
        {
            return null;
        }
#else
        return null;
#endif
    }

    /// <summary>
    /// Get the Steam App ID (useful for verifying Steam context).
    /// </summary>
    public static uint GetAppId()
    {
#if STEAM_BUILD
        if (!_steamAvailable) return 0;

        try
        {
            return Steamworks.SteamUtils.GetAppID().m_AppId;
        }
        catch
        {
            return 0;
        }
#else
        return 0;
#endif
    }

    /// <summary>
    /// Update a Steam stat (integer value).
    /// Stats must be configured in Steamworks dashboard.
    /// </summary>
    public static bool SetStat(string statName, int value)
    {
        if (!_steamAvailable) return false;

#if STEAM_BUILD
        try
        {
            if (Steamworks.SteamUserStats.SetStat(statName, value))
            {
                Steamworks.SteamUserStats.StoreStats();
                return true;
            }
        }
        catch (Exception ex)
        {
            DebugLogger.Instance?.LogError("STEAM", $"Error setting Steam stat {statName}: {ex.Message}");
        }
#endif

        return false;
    }

    /// <summary>
    /// Get a Steam stat (integer value).
    /// </summary>
    public static int GetStat(string statName)
    {
        if (!_steamAvailable) return 0;

#if STEAM_BUILD
        try
        {
            if (Steamworks.SteamUserStats.GetStat(statName, out int value))
            {
                return value;
            }
        }
        catch (Exception ex)
        {
            DebugLogger.Instance?.LogError("STEAM", $"Error getting Steam stat {statName}: {ex.Message}");
        }
#endif

        return 0;
    }

    /// <summary>
    /// Set a Steam stat from a long value (capped to int.MaxValue).
    /// </summary>
    public static bool SetStatLong(string statName, long value)
    {
        // Cap to int.MaxValue since Steam stats are 32-bit integers
        int cappedValue = value > int.MaxValue ? int.MaxValue : (int)value;
        return SetStat(statName, cappedValue);
    }

    /// <summary>
    /// Sync all player statistics to Steam.
    /// Call this when saving, on level up, and at session end.
    /// </summary>
    /// <param name="stats">The player statistics to sync</param>
    /// <returns>True if stats were synced, false if Steam unavailable or sync disabled</returns>
    public static bool SyncPlayerStats(PlayerStatistics? stats)
    {
        if (!_steamAvailable || stats == null) return false;

        // Don't sync after a reset - prevents re-triggering stat-based achievements
        if (_syncDisabledUntilRestart)
        {
            DebugLogger.Instance?.LogInfo("STEAM", "Stat sync skipped - disabled after reset (restart game to re-enable)");
            return false;
        }

#if STEAM_BUILD
        try
        {
            bool anySet = false;

            // Combat stats
            anySet |= SetStatLong(StatNames.MonstersKilled, stats.TotalMonstersKilled);
            anySet |= SetStatLong(StatNames.BossesKilled, stats.TotalBossesKilled);
            anySet |= SetStatLong(StatNames.TotalDamageDealt, stats.TotalDamageDealt);
            anySet |= SetStatLong(StatNames.CriticalHits, stats.TotalCriticalHits);

            // Progress stats
            anySet |= SetStat(StatNames.HighestLevel, stats.HighestLevelReached);
            anySet |= SetStat(StatNames.DeepestDungeon, stats.DeepestDungeonLevel);
            anySet |= SetStat(StatNames.QuestsCompleted, stats.QuestsCompleted);

            // Economy stats
            anySet |= SetStatLong(StatNames.TotalGoldEarned, stats.TotalGoldEarned);
            anySet |= SetStatLong(StatNames.TotalGoldSpent, stats.TotalGoldSpent);
            anySet |= SetStatLong(StatNames.ItemsBought, stats.TotalItemsBought);

            // Survival stats
            long totalDeaths = stats.TotalPlayerDeaths + stats.TotalMonsterDeaths;
            anySet |= SetStatLong(StatNames.TotalDeaths, totalDeaths);

            // Exploration stats
            anySet |= SetStatLong(StatNames.SecretsFound, stats.TotalSecretsFound);
            anySet |= SetStatLong(StatNames.ChestsOpened, stats.TotalChestsOpened);

            // Time stats (convert to minutes)
            int playTimeMinutes = (int)stats.TotalPlayTime.TotalMinutes;
            anySet |= SetStat(StatNames.PlayTimeMinutes, playTimeMinutes);

            if (anySet)
            {
                Steamworks.SteamUserStats.StoreStats();
                DebugLogger.Instance?.LogInfo("STEAM", "Player stats synced to Steam");
            }

            return anySet;
        }
        catch (Exception ex)
        {
            DebugLogger.Instance?.LogError("STEAM", $"Error syncing player stats to Steam: {ex.Message}");
            return false;
        }
#else
        return false;
#endif
    }

    /// <summary>
    /// Sync stats from the current player via GameEngine.
    /// Convenience method that gets current player stats automatically.
    /// </summary>
    public static bool SyncCurrentPlayerStats()
    {
        if (!_steamAvailable) return false;

        try
        {
            var player = GameEngine.Instance?.CurrentPlayer;
            if (player?.Statistics != null)
            {
                return SyncPlayerStats(player.Statistics);
            }
        }
        catch (Exception ex)
        {
            DebugLogger.Instance?.LogError("STEAM", $"Error getting current player for stat sync: {ex.Message}");
        }

        return false;
    }
}
