using System;
using System.Collections.Generic;
using System.Linq;
using System.Net.Http;
using System.Text;
using System.Text.Json;
using System.Threading.Tasks;

namespace UsurperRemake.Systems;

/// <summary>
/// Telemetry system for collecting anonymous gameplay statistics during alpha testing.
/// Sends data to PostHog for analytics and visualization.
/// All telemetry is opt-in and can be disabled at any time.
/// </summary>
public class TelemetrySystem
{
    private static TelemetrySystem? _instance;
    public static TelemetrySystem Instance => _instance ??= new TelemetrySystem();

    private static readonly HttpClient httpClient = new HttpClient();

    // PostHog configuration
    // Note: PostHog public API keys are designed to be embedded in client apps (like GA tracking IDs)
    private const string PostHogApiKey = "phc_gut4Tm3SYKurI1mb9vU82YfLy7hdGt0pgXQTBN0NN2c";
    private const string PostHogHost = "https://us.i.posthog.com";

    // Unique session ID (not tied to player identity)
    private string sessionId;

    // Distinct ID for PostHog (persistent across sessions if possible)
    private string distinctId;

    // Whether telemetry is enabled (opt-in)
    public bool IsEnabled { get; private set; } = false;

    // Debug mode for verbose console output (disabled for release)
    public bool DebugMode { get; set; } = false;

    // Track when we last sent data to avoid spam
    private DateTime lastSendTime = DateTime.MinValue;
    private const int MinSecondsBetweenSends = 10; // PostHog handles batching well

    // Batch events to reduce API calls
    private List<PostHogEvent> pendingEvents = new();
    private const int MaxPendingEvents = 20;

    // Game version for all events
    private string gameVersion = "unknown";

    public TelemetrySystem()
    {
        _instance = this;
        sessionId = Guid.NewGuid().ToString("N")[..8]; // Short unique ID per session
        distinctId = GetOrCreateDistinctId();

        DebugLog($"TelemetrySystem initialized. Session: {sessionId}, DistinctId: {distinctId}");
    }

    /// <summary>
    /// Get or create a persistent distinct ID for this installation
    /// </summary>
    private string GetOrCreateDistinctId()
    {
        try
        {
            var idPath = System.IO.Path.Combine(
                AppDomain.CurrentDomain.BaseDirectory,
                ".telemetry_id"
            );
            if (System.IO.File.Exists(idPath))
            {
                var existingId = System.IO.File.ReadAllText(idPath).Trim();
                if (!string.IsNullOrEmpty(existingId))
                    return existingId;
            }

            // Create new ID
            var newId = Guid.NewGuid().ToString("N");
            System.IO.File.WriteAllText(idPath, newId);
            return newId;
        }
        catch
        {
            // Fall back to session-based ID if file operations fail
            return Guid.NewGuid().ToString("N");
        }
    }

    /// <summary>
    /// Log debug messages to console
    /// </summary>
    private void DebugLog(string message)
    {
        if (DebugMode)
        {
            Console.WriteLine($"[Telemetry] {message}");
        }
    }

    /// <summary>
    /// Log error messages to console
    /// </summary>
    private void ErrorLog(string message)
    {
        Console.Error.WriteLine($"[Telemetry ERROR] {message}");
    }

    /// <summary>
    /// Set game version for all events
    /// </summary>
    public void SetGameVersion(string version)
    {
        gameVersion = version;
    }

    /// <summary>
    /// Enable telemetry (player opt-in)
    /// </summary>
    public void Enable()
    {
        DebugLog("Enable() called");
        IsEnabled = true;
        DebugLog($"IsEnabled set to: {IsEnabled}");
        TrackEvent("telemetry_enabled");
    }

    /// <summary>
    /// Send PostHog $identify event to set user properties.
    /// This powers User Properties, DAUs, WAUs, Growth accounting, and Retention dashboards.
    /// Call this after character creation or loading to set user properties.
    /// </summary>
    public void Identify(string? characterName = null, string? characterClass = null,
        string? race = null, int? level = null, string? difficulty = null,
        DateTime? firstSeen = null)
    {
        if (!IsEnabled) return;

        var userProps = new Dictionary<string, object>();

        // Standard PostHog properties for user identification
        if (characterName != null) userProps["name"] = characterName;
        if (characterClass != null) userProps["character_class"] = characterClass;
        if (race != null) userProps["race"] = race;
        if (level.HasValue) userProps["level"] = level.Value;
        if (difficulty != null) userProps["difficulty"] = difficulty;
        if (firstSeen.HasValue) userProps["$initial_timestamp"] = firstSeen.Value.ToString("o");

        // Always include platform and version
        userProps["platform"] = GetPlatform();
        userProps["game_version"] = gameVersion;

        // PostHog $identify event sets person properties
        var props = new Dictionary<string, object>
        {
            ["$set"] = userProps,
            ["$session_id"] = sessionId,
            ["game_version"] = gameVersion,
            ["platform"] = GetPlatform(),
            ["$geoip_disable"] = true // Privacy: Don't derive location from IP
        };

        var evt = new PostHogEvent
        {
            Event = "$identify",
            DistinctId = distinctId,
            Properties = props,
            Timestamp = DateTime.UtcNow
        };

        pendingEvents.Add(evt);
        DebugLog($"Queued $identify event with properties: {string.Join(", ", userProps.Keys)}");

        // Flush immediately so user is identified right away
        _ = FlushEventsAsync();
    }

    /// <summary>
    /// Update user properties without a full identify (incremental updates).
    /// Use this when level changes, achievements unlock, etc.
    /// </summary>
    public void SetUserProperties(Dictionary<string, object> properties)
    {
        if (!IsEnabled || properties.Count == 0) return;

        var props = new Dictionary<string, object>
        {
            ["$set"] = properties,
            ["$session_id"] = sessionId,
            ["game_version"] = gameVersion,
            ["platform"] = GetPlatform(),
            ["$geoip_disable"] = true // Privacy: Don't derive location from IP
        };

        var evt = new PostHogEvent
        {
            Event = "$identify",
            DistinctId = distinctId,
            Properties = props,
            Timestamp = DateTime.UtcNow
        };

        pendingEvents.Add(evt);
        DebugLog($"Queued $identify (set) with: {string.Join(", ", properties.Keys)}");
    }

    /// <summary>
    /// Increment numeric user properties (e.g., total_sessions, total_deaths).
    /// PostHog will automatically increment these values.
    /// </summary>
    public void IncrementUserProperty(string propertyName, int incrementBy = 1)
    {
        if (!IsEnabled) return;

        var props = new Dictionary<string, object>
        {
            ["$set_once"] = new Dictionary<string, object>
            {
                [$"first_{propertyName}_date"] = DateTime.UtcNow.ToString("o")
            },
            ["$session_id"] = sessionId,
            ["game_version"] = gameVersion,
            ["platform"] = GetPlatform(),
            ["$geoip_disable"] = true // Privacy: Don't derive location from IP
        };

        // PostHog doesn't have $increment in batch API, so we track as event
        // The user property update happens via $set in separate call
        TrackEvent($"{propertyName}_increment", new Dictionary<string, object>
        {
            ["increment"] = incrementBy
        });
    }

    /// <summary>
    /// Disable telemetry (player opt-out)
    /// </summary>
    public void Disable()
    {
        IsEnabled = false;
        pendingEvents.Clear();
    }

    /// <summary>
    /// Track a gameplay event with properties
    /// </summary>
    public void TrackEvent(string eventName, Dictionary<string, object>? properties = null)
    {
        DebugLog($"TrackEvent called: '{eventName}', IsEnabled={IsEnabled}");

        if (!IsEnabled)
        {
            DebugLog($"Skipping event '{eventName}' - telemetry disabled");
            return;
        }

        var props = properties ?? new Dictionary<string, object>();

        // Add standard properties
        props["$session_id"] = sessionId;
        props["game_version"] = gameVersion;
        props["platform"] = GetPlatform();

        // Privacy: Disable GeoIP processing - we don't need player location data
        props["$geoip_disable"] = true;

        var evt = new PostHogEvent
        {
            Event = eventName,
            DistinctId = distinctId,
            Properties = props,
            Timestamp = DateTime.UtcNow
        };

        pendingEvents.Add(evt);
        DebugLog($"Queued event '{eventName}' (pending: {pendingEvents.Count})");

        // Send if we have enough events or enough time has passed
        if (pendingEvents.Count >= MaxPendingEvents ||
            (DateTime.Now - lastSendTime).TotalSeconds >= MinSecondsBetweenSends)
        {
            DebugLog($"Triggering flush (count={pendingEvents.Count}, timeSinceLast={(DateTime.Now - lastSendTime).TotalSeconds}s)");
            _ = FlushEventsAsync();
        }
    }

    /// <summary>
    /// Track event with anonymous object properties (convenience overload)
    /// </summary>
    public void TrackEvent(string eventName, object? data)
    {
        if (data == null)
        {
            TrackEvent(eventName, (Dictionary<string, object>?)null);
            return;
        }

        // Convert anonymous object to dictionary
        var props = new Dictionary<string, object>();
        foreach (var prop in data.GetType().GetProperties())
        {
            var value = prop.GetValue(data);
            if (value != null)
                props[prop.Name] = value;
        }
        TrackEvent(eventName, props);
    }

    /// <summary>
    /// Get platform string
    /// </summary>
    private string GetPlatform()
    {
        if (OperatingSystem.IsWindows()) return "windows";
        if (OperatingSystem.IsLinux()) return "linux";
        if (OperatingSystem.IsMacOS()) return "macos";
        return "unknown";
    }

    /// <summary>
    /// Track a player milestone (level up, boss kill, etc.)
    /// </summary>
    public void TrackMilestone(string milestoneName, int playerLevel, string? playerClass = null, object? extraData = null)
    {
        var props = new Dictionary<string, object>
        {
            ["milestone"] = milestoneName,
            ["level"] = playerLevel
        };
        if (playerClass != null) props["player_class"] = playerClass;

        TrackEvent("milestone", props);
    }

    /// <summary>
    /// Track player death
    /// </summary>
    public void TrackDeath(int playerLevel, string? causeOfDeath, int dungeonFloor = 0)
    {
        TrackEvent("player_death", new Dictionary<string, object>
        {
            ["level"] = playerLevel,
            ["cause"] = causeOfDeath ?? "unknown",
            ["dungeon_floor"] = dungeonFloor
        });
    }

    /// <summary>
    /// Track combat statistics
    /// </summary>
    public void TrackCombatEnd(int playerLevel, bool victory, int monstersDefeated, int dungeonFloor, long damageDealt, long damageTaken)
    {
        TrackEvent("combat_end", new Dictionary<string, object>
        {
            ["level"] = playerLevel,
            ["victory"] = victory,
            ["monsters_defeated"] = monstersDefeated,
            ["dungeon_floor"] = dungeonFloor,
            ["damage_dealt"] = damageDealt,
            ["damage_taken"] = damageTaken,
            ["damage_ratio"] = damageTaken > 0 ? Math.Round((double)damageDealt / damageTaken, 2) : damageDealt
        });
    }

    /// <summary>
    /// Track session start with basic info
    /// </summary>
    public void TrackSessionStart(string version, string platform)
    {
        gameVersion = version;
        TrackEvent("session_start", new Dictionary<string, object>
        {
            ["game_version"] = version,
            ["platform"] = platform
        });

        // Force flush session start immediately
        _ = FlushEventsAsync();
    }

    /// <summary>
    /// Track new character creation - sends immediately
    /// </summary>
    public void TrackNewCharacter(string race, string characterClass, string sex, string difficulty, int startingGold)
    {
        TrackEvent("new_character", new Dictionary<string, object>
        {
            ["race"] = race,
            ["character_class"] = characterClass,
            ["sex"] = sex,
            ["difficulty"] = difficulty,
            ["starting_gold"] = startingGold
        });

        // Force flush immediately so we see new characters right away
        _ = FlushEventsAsync();
    }

    /// <summary>
    /// Track session end with summary statistics
    /// </summary>
    public void TrackSessionEnd(int finalLevel, int totalPlaytimeMinutes, int totalDeaths, int monstersKilled)
    {
        TrackEvent("session_end", new Dictionary<string, object>
        {
            ["final_level"] = finalLevel,
            ["playtime_minutes"] = totalPlaytimeMinutes,
            ["total_deaths"] = totalDeaths,
            ["monsters_killed"] = monstersKilled
        });

        // Force flush on session end
        _ = FlushEventsAsync();
    }

    /// <summary>
    /// Track an error/exception for debugging
    /// </summary>
    public void TrackError(string errorType, string message, string? stackTrace = null)
    {
        var props = new Dictionary<string, object>
        {
            ["error_type"] = errorType,
            ["message"] = message
        };
        if (stackTrace != null)
            props["stack_trace"] = stackTrace.Length > 500 ? stackTrace[..500] : stackTrace;

        TrackEvent("error", props);

        // Force flush errors immediately
        _ = FlushEventsAsync();
    }

    /// <summary>
    /// Track feature usage (which game features players use most)
    /// </summary>
    public void TrackFeatureUsed(string featureName, int playerLevel)
    {
        TrackEvent("feature_used", new Dictionary<string, object>
        {
            ["feature"] = featureName,
            ["level"] = playerLevel
        });
    }

    /// <summary>
    /// Track economy events (purchases, sales, gold changes)
    /// </summary>
    public void TrackEconomy(string action, int amount, int playerLevel, string? itemName = null)
    {
        var props = new Dictionary<string, object>
        {
            ["action"] = action,
            ["amount"] = amount,
            ["level"] = playerLevel
        };
        if (itemName != null) props["item"] = itemName;

        TrackEvent("economy", props);
    }

    /// <summary>
    /// Track dungeon exploration
    /// </summary>
    public void TrackDungeonFloor(int playerLevel, int floor, string action)
    {
        TrackEvent("dungeon_floor", new Dictionary<string, object>
        {
            ["level"] = playerLevel,
            ["floor"] = floor,
            ["action"] = action // entered, cleared, fled
        });
    }

    /// <summary>
    /// Track level up with stat choices
    /// </summary>
    public void TrackLevelUp(int newLevel, string characterClass, int str, int dex, int con, int intel, int wis, int cha)
    {
        TrackEvent("level_up", new Dictionary<string, object>
        {
            ["new_level"] = newLevel,
            ["character_class"] = characterClass,
            ["str"] = str,
            ["dex"] = dex,
            ["con"] = con,
            ["int"] = intel,
            ["wis"] = wis,
            ["cha"] = cha
        });
    }

    // ============================================================
    // ENHANCED TRACKING METHODS FOR COMPREHENSIVE ANALYTICS
    // ============================================================

    /// <summary>
    /// Track combat with full details including outcome type
    /// </summary>
    public void TrackCombat(string outcome, int playerLevel, int dungeonFloor, int monsterCount,
        long damageDealt, long damageTaken, string? monsterType = null, bool isBoss = false,
        int roundsPlayed = 0, string? playerClass = null)
    {
        var props = new Dictionary<string, object>
        {
            ["outcome"] = outcome, // "victory", "defeat", "fled", "draw"
            ["level"] = playerLevel,
            ["dungeon_floor"] = dungeonFloor,
            ["monster_count"] = monsterCount,
            ["damage_dealt"] = damageDealt,
            ["damage_taken"] = damageTaken,
            ["is_boss"] = isBoss,
            ["rounds"] = roundsPlayed
        };
        if (monsterType != null) props["monster_type"] = monsterType;
        if (playerClass != null) props["player_class"] = playerClass;
        if (damageTaken > 0) props["damage_ratio"] = Math.Round((double)damageDealt / damageTaken, 2);

        TrackEvent("combat", props);
    }

    /// <summary>
    /// Track spell or ability usage in combat
    /// </summary>
    public void TrackAbilityUsed(string abilityName, string abilityType, int playerLevel,
        string playerClass, int damage = 0, bool hit = true, string? target = null)
    {
        var props = new Dictionary<string, object>
        {
            ["ability"] = abilityName,
            ["type"] = abilityType, // "spell", "class_ability", "item", "attack"
            ["level"] = playerLevel,
            ["player_class"] = playerClass,
            ["damage"] = damage,
            ["hit"] = hit
        };
        if (target != null) props["target"] = target;

        TrackEvent("ability_used", props);
    }

    /// <summary>
    /// Track shop transactions
    /// </summary>
    public void TrackShopTransaction(string shopType, string action, string itemName,
        long price, int playerLevel, long playerGoldAfter)
    {
        TrackEvent("shop_transaction", new Dictionary<string, object>
        {
            ["shop"] = shopType, // "weapon", "armor", "magic", "healer", "inn"
            ["action"] = action, // "buy", "sell", "repair", "heal"
            ["item"] = itemName,
            ["price"] = price,
            ["level"] = playerLevel,
            ["gold_after"] = playerGoldAfter
        });
    }

    /// <summary>
    /// Track quest events
    /// </summary>
    public void TrackQuest(string questName, string action, int playerLevel,
        long? reward = null, string? questType = null)
    {
        var props = new Dictionary<string, object>
        {
            ["quest"] = questName,
            ["action"] = action, // "accepted", "completed", "failed", "abandoned"
            ["level"] = playerLevel
        };
        if (reward.HasValue) props["reward"] = reward.Value;
        if (questType != null) props["quest_type"] = questType;

        TrackEvent("quest", props);
    }

    /// <summary>
    /// Track NPC interactions
    /// </summary>
    public void TrackNPCInteraction(string npcName, string interactionType, int playerLevel,
        int? relationshipLevel = null, string? outcome = null)
    {
        var props = new Dictionary<string, object>
        {
            ["npc"] = npcName,
            ["interaction"] = interactionType, // "talk", "gift", "recruit", "romance", "fight"
            ["level"] = playerLevel
        };
        if (relationshipLevel.HasValue) props["relationship"] = relationshipLevel.Value;
        if (outcome != null) props["outcome"] = outcome;

        TrackEvent("npc_interaction", props);
    }

    /// <summary>
    /// Track dungeon exploration events
    /// </summary>
    public void TrackDungeonEvent(string eventType, int playerLevel, int floor,
        string? details = null, long? goldChange = null, long? xpGained = null)
    {
        var props = new Dictionary<string, object>
        {
            ["event"] = eventType, // "enter_floor", "find_treasure", "trap", "puzzle", "feature"
            ["level"] = playerLevel,
            ["floor"] = floor
        };
        if (details != null) props["details"] = details;
        if (goldChange.HasValue) props["gold_change"] = goldChange.Value;
        if (xpGained.HasValue) props["xp_gained"] = xpGained.Value;

        TrackEvent("dungeon_event", props);
    }

    /// <summary>
    /// Track location visits
    /// </summary>
    public void TrackLocationVisit(string location, int playerLevel, int visitCount = 1)
    {
        TrackEvent("location_visit", new Dictionary<string, object>
        {
            ["location"] = location,
            ["level"] = playerLevel,
            ["visit_count"] = visitCount
        });
    }

    /// <summary>
    /// Track achievement unlocks
    /// </summary>
    public void TrackAchievement(string achievementId, string achievementName, int playerLevel,
        string? category = null)
    {
        var props = new Dictionary<string, object>
        {
            ["achievement_id"] = achievementId,
            ["achievement_name"] = achievementName,
            ["level"] = playerLevel
        };
        if (category != null) props["category"] = category;

        TrackEvent("achievement_unlocked", props);
    }

    /// <summary>
    /// Track equipment changes
    /// </summary>
    public void TrackEquipment(string action, string slot, string itemName, int itemPower,
        int playerLevel, string? previousItem = null)
    {
        var props = new Dictionary<string, object>
        {
            ["action"] = action, // "equip", "unequip", "upgrade"
            ["slot"] = slot, // "weapon", "armor", "shield", "ring", etc.
            ["item"] = itemName,
            ["power"] = itemPower,
            ["level"] = playerLevel
        };
        if (previousItem != null) props["previous_item"] = previousItem;

        TrackEvent("equipment", props);
    }

    /// <summary>
    /// Track gold sources and sinks for economy analysis
    /// </summary>
    public void TrackGoldChange(string source, long amount, int playerLevel, long totalGold)
    {
        TrackEvent("gold_change", new Dictionary<string, object>
        {
            ["source"] = source, // "combat_loot", "treasure", "quest_reward", "shop_sale", "shop_buy", "healing", "training", "theft"
            ["amount"] = amount, // Positive = gained, negative = spent
            ["level"] = playerLevel,
            ["total_gold"] = totalGold
        });
    }

    /// <summary>
    /// Track companion events
    /// </summary>
    public void TrackCompanion(string companionName, string action, int playerLevel,
        int? companionLevel = null, string? companionClass = null)
    {
        var props = new Dictionary<string, object>
        {
            ["companion"] = companionName,
            ["action"] = action, // "recruited", "dismissed", "died", "leveled_up", "quest_started", "quest_completed"
            ["level"] = playerLevel
        };
        if (companionLevel.HasValue) props["companion_level"] = companionLevel.Value;
        if (companionClass != null) props["companion_class"] = companionClass;

        TrackEvent("companion", props);
    }

    /// <summary>
    /// Track story/narrative progression
    /// </summary>
    public void TrackStoryProgress(string milestone, int playerLevel, string? details = null)
    {
        var props = new Dictionary<string, object>
        {
            ["milestone"] = milestone,
            ["level"] = playerLevel
        };
        if (details != null) props["details"] = details;

        TrackEvent("story_progress", props);
    }

    /// <summary>
    /// Track player snapshot for periodic state capture
    /// </summary>
    public void TrackPlayerSnapshot(int level, string playerClass, long gold, long totalXp,
        int str, int dex, int con, int intel, int wis, int cha,
        int monstersKilled, int deaths, int dungeonFloorReached)
    {
        TrackEvent("player_snapshot", new Dictionary<string, object>
        {
            ["level"] = level,
            ["player_class"] = playerClass,
            ["gold"] = gold,
            ["total_xp"] = totalXp,
            ["str"] = str,
            ["dex"] = dex,
            ["con"] = con,
            ["int"] = intel,
            ["wis"] = wis,
            ["cha"] = cha,
            ["monsters_killed"] = monstersKilled,
            ["deaths"] = deaths,
            ["max_dungeon_floor"] = dungeonFloorReached
        });
    }

    /// <summary>
    /// Send all pending events to PostHog
    /// </summary>
    private async Task FlushEventsAsync()
    {
        DebugLog($"FlushEventsAsync called. Pending: {pendingEvents.Count}");

        if (pendingEvents.Count == 0)
        {
            DebugLog("Nothing to flush");
            return;
        }

        var eventsToSend = new List<PostHogEvent>(pendingEvents);
        pendingEvents.Clear();
        lastSendTime = DateTime.Now;

        try
        {
            // PostHog batch endpoint
            var batchPayload = new
            {
                api_key = PostHogApiKey,
                batch = eventsToSend.Select(e => new
                {
                    @event = e.Event,
                    distinct_id = e.DistinctId,
                    properties = e.Properties,
                    timestamp = e.Timestamp.ToString("o")
                }).ToArray()
            };

            var json = JsonSerializer.Serialize(batchPayload);
            var content = new StringContent(json, Encoding.UTF8, "application/json");

            DebugLog($"Sending {eventsToSend.Count} events to PostHog...");
            DebugLog($"URL: {PostHogHost}/batch/");
            DebugLog($"Payload: {json}");
            var response = await httpClient.PostAsync($"{PostHogHost}/batch/", content);
            DebugLog($"Response: {response.StatusCode}");
            var responseBody = await response.Content.ReadAsStringAsync();
            DebugLog($"Response body: {responseBody}");

            if (!response.IsSuccessStatusCode)
            {
                var errorBody = await response.Content.ReadAsStringAsync();
                ErrorLog($"PostHog error: {errorBody}");
                // Re-queue events on failure, but cap to prevent infinite buildup
                pendingEvents.AddRange(eventsToSend);
                if (pendingEvents.Count > MaxPendingEvents * 2)
                    pendingEvents.RemoveRange(0, pendingEvents.Count - MaxPendingEvents);
            }
            else
            {
                DebugLog("Events sent successfully!");
            }
        }
        catch (Exception ex)
        {
            ErrorLog($"Exception sending telemetry: {ex.Message}");
            // Don't re-queue on network errors to avoid infinite buildup
        }
    }

    /// <summary>
    /// Serialize telemetry settings for save
    /// </summary>
    public TelemetryData Serialize()
    {
        return new TelemetryData
        {
            IsEnabled = IsEnabled
        };
    }

    /// <summary>
    /// Deserialize telemetry settings from save
    /// </summary>
    public void Deserialize(TelemetryData? data)
    {
        if (data == null) return;
        IsEnabled = data.IsEnabled;
    }
}

/// <summary>
/// PostHog event structure
/// </summary>
internal class PostHogEvent
{
    public string Event { get; set; } = "";
    public string DistinctId { get; set; } = "";
    public Dictionary<string, object> Properties { get; set; } = new();
    public DateTime Timestamp { get; set; }
}

/// <summary>
/// Serializable telemetry settings
/// </summary>
public class TelemetryData
{
    public bool IsEnabled { get; set; }
}
